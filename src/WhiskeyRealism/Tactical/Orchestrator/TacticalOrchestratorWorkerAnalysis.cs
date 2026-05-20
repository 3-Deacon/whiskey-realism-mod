using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pure worker-thread analysis: takes an immutable
    /// <see cref="TacticalOrchestratorWorkerInput"/>, produces a fresh
    /// <see cref="TacticalDecisionSnapshot"/>. The analysis path runs entirely
    /// off the Unity main thread.
    ///
    /// MUST NOT call any Unity API or vanilla static
    /// (<c>BattleUnits.completeunitlist</c>, <c>Regiment.*</c> instance fields,
    /// <c>Time.*</c>, <c>Plugin.Instance.*</c> config getters, etc.). All
    /// inputs must come from the <see cref="TacticalOrchestratorWorkerInput"/>
    /// container — Task 8 baked the frozen config snapshot into the input so
    /// the worker never reads <c>Plugin.Instance</c>.
    ///
    /// On exception, returns <see cref="TacticalDecisionSnapshot.Empty"/> and
    /// logs via <c>Plugin.Log.LogWarning</c> (BepInEx <c>ManualLogSource</c> is
    /// the only thread-safe logger we have; <see cref="WhiskeyRealism.Util.OnceLog"/>
    /// dedup state is not proven thread-safe). The empty-on-failure contract
    /// keeps Task 14's main-thread patches on their legacy fallback path
    /// (<c>TryGetGroupStance</c> returns false → vanilla branch).
    ///
    /// Task 11 scope: populates intent (via the existing stateless
    /// <see cref="ArmyIntentInference.Build"/>), commander personality, force
    /// availability, command tree, and a partial strategic-intent snapshot.
    /// Plan-state-dependent fields (CurrentPlan, HasPlan, LastReplanTrigger)
    /// stay at defaults because the worker input does NOT yet carry a
    /// snapshot of <c>ArmyOrchestrator</c> plan state (PlanAgeSeconds,
    /// CurrentPlan, Phase, HistoryGlobalOdds, MainEffortSector). Wiring those
    /// is a follow-up; see commit message for details.
    /// PopulateGroupDecisions remains empty: the captured per-unit
    /// observations don't expose the per-group sector inputs
    /// <c>TacticalDoctrineScorer.DecideGroupStance</c> needs.
    /// </summary>
    internal static class TacticalOrchestratorWorkerAnalysis
    {
        public static TacticalDecisionSnapshot Analyze(
            TacticalOrchestratorWorkerInput input,
            long cycleId)
        {
            try
            {
                var side0 = ProcessSide(0, input);
                var side1 = ProcessSide(1, input);

                var groupStance0 = new Dictionary<int, TacticalGroupStanceDecision>(0);
                var groupStance1 = new Dictionary<int, TacticalGroupStanceDecision>(0);
                PopulateGroupDecisions(0, input, groupStance0);
                PopulateGroupDecisions(1, input, groupStance1);

                return new TacticalDecisionSnapshot(
                    battleSequence: input.BattleSequence,
                    cycleId: cycleId,
                    capturedAtBattleHours: input.BattleHours,
                    capturedAtRealtimeSeconds: input.RealtimeSeconds,
                    side0: side0,
                    side1: side1,
                    groupStance0: groupStance0,
                    groupStance1: groupStance1);
            }
            catch (Exception e)
            {
                // Worker-thread-safe warning: do NOT use OnceLog (dedup state not thread-safe).
                Plugin.Log.LogWarning("[TacticalOrchestratorWorkerAnalysis] cycle "
                    + cycleId + " degraded: " + e.GetType().Name + " " + e.Message);
                return TacticalDecisionSnapshot.Empty;
            }
        }

        private static TacticalSideDecision ProcessSide(int sideIdx, TacticalOrchestratorWorkerInput input)
        {
            if (input == null) return TacticalSideDecision.Empty;

            CommanderRosterEntry commanderEntry = sideIdx == 0
                ? input.Side0CommanderEntry
                : input.Side1CommanderEntry;
            ArmyEvidenceBuilder.Bundle bundle = sideIdx == 0
                ? input.Side0Bundle
                : input.Side1Bundle;
            CommandTreeSnapshot commandTree = sideIdx == 0
                ? input.Side0CommandTree
                : input.Side1CommandTree;

            // Alliance resolution: prefer the roster entry's recorded alliance
            // (authoritative — set when commanders are discovered from vanilla
            // in DiscoverCommandersFromVanilla); fall back to sideIdx when the
            // side is suppressed (no commander attached). Keep within [-1, 1].
            int allianceId = commanderEntry != null ? commanderEntry.AllianceId : sideIdx;
            if (allianceId < -1 || allianceId > 1) allianceId = -1;

            // Intent inference: existing pure scorer, runs unchanged. Empty
            // EnemyVisibleState short-circuits to Unknown intent (line 17-19
            // of ArmyIntentInference.cs), so suppressed sides (no enemy
            // visibility captured) naturally degrade. Build() reads
            // enemy.Sectors.Length, which throws if a caller passes a
            // default(EnemyVisibleState) (Sectors=null). Normalize to an
            // empty-sectors instance before calling Build() so suppressed
            // sides degrade cleanly without forcing every input producer to
            // run the EnemyVisibleState ctor.
            EnemyVisibleState enemy = bundle.EnemyVisible;
            if (enemy.Sectors == null)
            {
                enemy = new EnemyVisibleState(
                    sectors: Array.Empty<EnemyVisibleSector>(),
                    enemyReserveCommitFraction: enemy.EnemyReserveCommitFraction,
                    anyContactSpotted: enemy.AnyContactSpotted,
                    anyContactBroken: enemy.AnyContactBroken,
                    enemyReinforcementStrength24h: enemy.EnemyReinforcementStrength24h);
            }
            TacticalIntentModel intent = ArmyIntentInference.Build(
                bundle.OwnEvidence,
                enemy);

            // Commander personality from roster entry; default vector when no
            // commander discovered (suppressed side or pre-bootstrap).
            PersonalityVector personality = commanderEntry != null
                ? commanderEntry.PersonalityVector
                : default(PersonalityVector);

            // Force-availability mirrors DriveOperationsLedger's computation:
            // OwnMainEffortStrength as the available pool, plus a reserve
            // headroom of (1 - committedFraction) clamped to [0, 1].
            float reserveAvailable = 1f - Clamp01(bundle.OwnReservesCommittedFraction);
            if (reserveAvailable < 0f) reserveAvailable = 0f;
            var force = new ForceAvailabilitySnapshot(
                bundle.OwnMainEffortStrength,
                reserveAvailable);

            // Strategic intent: port of BuildStrategicBattleIntentSnapshot
            // (TacticalBattleCoordinatorRuntime.cs:647-672). Plan-id /
            // theater-intent text fields stay empty because plan state
            // isn't in the input; downstream consumers (operations ledger)
            // treat empty as the no-plan signal.
            var strategicIntent = new StrategicBattleIntentSnapshot(
                casualtyPressure: Clamp01(1f - bundle.OwnArmyMorale),
                timePressure: 0f,
                theaterIntent: intent.PrimaryIntent.ToString(),
                campaignIntent: string.Empty,
                allianceId: allianceId,
                campaignObjectiveId: string.Empty,
                theaterPriority: Clamp01(bundle.OwnEvidence.CurrentOdds / 2f),
                casualtyTolerance: personality.CasualtyTolerance,
                preserveForceBias: Clamp01((personality.Caution + 1f) * 0.5f),
                commanderPersonality: personality);

            return new TacticalSideDecision(
                allianceId: allianceId,
                currentPlan: default,                        // deferred: needs plan-state in input
                currentIntent: intent,
                commandTree: commandTree,
                strategicIntent: strategicIntent,
                forceAvailability: force,
                commanderPersonality: personality,
                lastReplanTrigger: ReplanTrigger.None,        // deferred: needs plan-state + replan clock
                hasPlan: false);                              // deferred: needs plan-state in input
        }

        private static void PopulateGroupDecisions(
            int sideIdx,
            TacticalOrchestratorWorkerInput input,
            Dictionary<int, TacticalGroupStanceDecision> output)
        {
            // Deferred — see class-level summary. Today's captured per-unit
            // observations (TacticalUnitObservation in
            // TacticalUnitObservationAggregate) carry position/strength/
            // fatigue/ammo/visibility, but NOT the per-group sector signals
            // BattleGroupStancePatch.BuildGroupSector reads:
            //   - Regiment.groupowninrange / groupenemiesinrange / groupstrengthaigroup
            //   - Regiment.unitrange.enemystrengthwithinangle[]
            //   - Regiment.flanksthreated / outflanked / covervalue / fortinrange
            //   - TacticalFogOfWarContact.ClosestVisibleEnemy(group) — live walk
            // Until those are added to the captured snapshot (or the per-unit
            // observation grows the necessary fields), TryGetGroupStance keeps
            // returning false and Task 14's main-thread patch retains the
            // legacy per-group computation path. Wiring this is a follow-up.
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
