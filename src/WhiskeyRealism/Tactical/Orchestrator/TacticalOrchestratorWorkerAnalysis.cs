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
    /// Gap-fill chunk 1 (2026-05-19): PopulateGroupDecisions now iterates the
    /// per-side captured observations, rebuilds <see cref="TacticalGroupContactInput"/>
    /// from the augmented <see cref="TacticalUnitObservation"/> fields, and
    /// stores per-unit <see cref="TacticalGroupStanceDecision"/> values keyed
    /// by GameObject InstanceID. <see cref="BattleGroupStancePatch"/>'s
    /// TryApplyFromDecisionSnapshot hits route to an apply-only fast path that
    /// skips the per-tick DecideGroupStance recompute.
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
                var groupSector0 = new Dictionary<int, TacticalSectorAssessment>(0);
                var groupSector1 = new Dictionary<int, TacticalSectorAssessment>(0);
                // Route by alliance id, not by side index — _groupStance0 holds
                // alliance-0 decisions, _groupStance1 holds alliance-1 decisions.
                // input.SideNAllianceId is sourced from bunits.alliance[N] so it
                // matches the patch's allianceId lookup at read time.
                PopulateGroupDecisions(
                    input.Side0Observations,
                    input.Side0AllianceId,
                    input.Side0MacroAi,
                    groupStance0,
                    groupStance1,
                    groupSector0,
                    groupSector1);
                PopulateGroupDecisions(
                    input.Side1Observations,
                    input.Side1AllianceId,
                    input.Side1MacroAi,
                    groupStance0,
                    groupStance1,
                    groupSector0,
                    groupSector1);

                return new TacticalDecisionSnapshot(
                    battleSequence: input.BattleSequence,
                    cycleId: cycleId,
                    capturedAtBattleHours: input.BattleHours,
                    capturedAtRealtimeSeconds: input.RealtimeSeconds,
                    side0: side0,
                    side1: side1,
                    groupStance0: groupStance0,
                    groupStance1: groupStance1,
                    groupSector0: groupSector0,
                    groupSector1: groupSector1);
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

        /// <summary>
        /// Walks the per-side captured observations and emits a
        /// <see cref="TacticalGroupStanceDecision"/> for every commandable
        /// group whose alliance matches <paramref name="allianceId"/>. The
        /// decision is routed into <paramref name="groupStance0"/> when
        /// <paramref name="allianceId"/> is 0 or <paramref name="groupStance1"/>
        /// when 1; other alliances are dropped (matches
        /// <see cref="TacticalDecisionSnapshot.TryGetGroupStance"/>'s
        /// alliance-keyed dict layout).
        ///
        /// Pure: reads only the captured observation fields, calls
        /// <see cref="TacticalGroupSectorEstimator.BuildSector"/> +
        /// <see cref="TacticalDoctrineScorer.DecideGroupStance"/> (both
        /// documented pure — no static state, no Unity API). The
        /// WlAllowsControl / OrderFrictionAllowsChange gates from
        /// BattleGroupStancePatch.ApplyGroup are NOT replicated here — they
        /// remain on the main-thread apply path so the patch's W&L/order-friction
        /// guards are the source of truth. The worker pre-computes the decision
        /// assuming gates would have allowed it; the apply path enforces them
        /// at apply time. (Both gates require live Regiment state the
        /// observation doesn't carry.)
        /// </summary>
        private static void PopulateGroupDecisions(
            IReadOnlyList<TacticalUnitObservation> observations,
            int allianceId,
            int macroAi,
            Dictionary<int, TacticalGroupStanceDecision> groupStance0,
            Dictionary<int, TacticalGroupStanceDecision> groupStance1,
            Dictionary<int, TacticalSectorAssessment> groupSector0,
            Dictionary<int, TacticalSectorAssessment> groupSector1)
        {
            if (observations == null || observations.Count == 0) return;
            // Route only into the alliance-matching dict (0 or 1). Drop
            // alliance > 1 (Europe / unmapped) so it never poisons either
            // bucket — matches TacticalDecisionSnapshot's [0, 1] domain.
            Dictionary<int, TacticalGroupStanceDecision> output;
            Dictionary<int, TacticalSectorAssessment> sectorOutput;
            if (allianceId == 0) { output = groupStance0; sectorOutput = groupSector0; }
            else if (allianceId == 1) { output = groupStance1; sectorOutput = groupSector1; }
            else return;

            int sectorIndex = 0;
            for (int i = 0; i < observations.Count; i++)
            {
                TacticalUnitObservation obs = observations[i];
                if (obs.Alliance != allianceId) continue;
                if (!TacticalDoctrineScorer.AllowsLocalGroupStanceWriter(obs.Unittyp)) continue;

                // OwnStrength = max(groupowninrange, groupstrengthaigroup) —
                // mirrors BattleGroupStancePatch.BuildGroupSector line 426.
                float ownStrength = obs.GroupOwnInRange;
                if (obs.GroupAiGroup > ownStrength) ownStrength = obs.GroupAiGroup;

                // Closest-enemy fields default to neutral values when the
                // observation has no visible enemy. IsVisibleLineContact's
                // gate (Strength > 0 + formed-line unittyp + non-skirm name +
                // non-routed + non-permanently-detached) rejects empty input
                // naturally, so default-zero observations land in the
                // AngleSlice branch with the right confidence floor.
                float closestStrength = obs.HasVisibleEnemy ? obs.VisibleEnemyStrength : 0f;
                int closestUnittyp = obs.HasVisibleEnemy ? obs.VisibleEnemyUnittyp : -1;
                string closestName = obs.HasVisibleEnemy ? obs.VisibleEnemyName : string.Empty;
                bool closestRouted = obs.HasVisibleEnemy && obs.VisibleEnemyRouted;
                bool closestPermDetached = obs.HasVisibleEnemy && obs.VisibleEnemyPermanentlyDetached;

                var sectorInput = new TacticalGroupContactInput(
                    sectorIndex,
                    ownStrength,
                    obs.GroupEnemiesInRange,
                    obs.GroupAngleEnemyStrength,
                    closestStrength,
                    closestUnittyp,
                    closestName,
                    closestRouted,
                    closestPermDetached,
                    obs.GroupFlankRisk,
                    obs.GroupStrongPoint);
                TacticalSectorAssessment sector = TacticalGroupSectorEstimator.BuildSector(sectorInput);

                // vanillaStance must come from the captured ai_stanceordered.
                // If the observation didn't capture it (-1), fall back to 2
                // (hold) so DecideGroupStance still produces a usable decision —
                // the main-thread apply path re-reads vanilla state and treats
                // any mismatch (stance == vanillaOrdered) as a no-op write.
                int vanillaStance = obs.GroupVanillaStanceOrdered >= 0
                    ? obs.GroupVanillaStanceOrdered
                    : 2;

                // WlAllowsControl + OrderFrictionAllowsChange enforced on main
                // at apply time; assume true here so the cached decision is
                // ready when the apply path's live gates pass.
                var input = new TacticalGroupStanceDecisionInput(
                    vanillaStance,
                    macroAi,
                    sector,
                    true,   // WlAllowsControl — re-checked on main
                    true);  // OrderFrictionAllowsChange — re-checked on main
                TacticalGroupStanceDecision decision = TacticalDoctrineScorer.DecideGroupStance(input);

                // Store every decision, including Skip — the apply path needs
                // to know the worker considered the unit (TryGetGroupStance
                // returning true claims iteration ownership for this group).
                // Last-writer-wins on duplicate instance ids; observation
                // capture walks completeunitlist linearly so collisions are
                // not expected for valid GameObject ids.
                output[obs.InstanceId] = decision;
                sectorOutput[obs.InstanceId] = sector;
                sectorIndex++;
            }
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
