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

                // Worker-migration: compute ObjectiveRecord[] off main per side via the
                // pure-data builder. Empty context (config flag off, suppressed side)
                // yields an empty record array — main detects this via TryGetObjectiveRecords
                // and falls back to the legacy on-main builder.
                ObjectiveRecord[] objectives0 = BuildObjectivesForSide(0, input);
                ObjectiveRecord[] objectives1 = BuildObjectivesForSide(1, input);

                // Regimental intelligence (2026-05-21). Per-regiment decisions ported
                // from Scourge of War's StandTargetAmmoAI decision tree. Computed on
                // worker thread from observation data; applied on main inside the
                // group-stance patch's per-regiment write surface.
                var regimentalDecisions0 = BuildRegimentalDecisionsForSide(0, input);
                var regimentalDecisions1 = BuildRegimentalDecisionsForSide(1, input);

                // Worker-migration fix (2026-05-21): with F stagger, only one side has
                // fresh observations per Tick (the building side). The other side gets
                // empty records AND empty group decisions this cycle. Without
                // preservation, the next published snapshot would BLOW AWAY the prior
                // cycle's data for the non-building side — main would then:
                //   - For ObjectiveRecords: fall back to legacy on-main BuildObjectiveRecordsFromAggregate
                //   - For group stance / sector: tier-2 (worker snapshot) miss → tier-3
                //     legacy ApplyGroup (~15-33 ms / call)
                // Both defeat the worker-migration perf win.
                //
                // Preserve: if this cycle produced no data for a side AND the prior
                // published snapshot has data for that side on the SAME battleSequence,
                // carry it forward. Stale by one F-stagger cycle (~4-8s wallclock) but
                // observation state doesn't shift materially in that window. The dicts
                // are immutable-by-convention so reference-sharing across snapshots is
                // safe (the snapshot ctor takes ownership but never mutates).
                // Carry-forward is bounded (BR-M2): only data produced for the SAME
                // battle within MaxCarryForwardBattleHours of this cycle is forwarded,
                // and the forwarded side keeps its original production time so a side
                // that stops producing data ages out instead of living forever.
                bool fresh0 = HasFreshObservations(input, 0);
                bool fresh1 = HasFreshObservations(input, 1);
                float dataHours0 = input.BattleHours;
                float dataHours1 = input.BattleHours;
                var priorSnapshot = TacticalDecisionSnapshot.Current;
                if (!fresh0 && CanCarryForward(priorSnapshot, input, 0))
                {
                    dataHours0 = priorSnapshot.GetSideDataBattleHours(0);
                    if (objectives0.Length == 0 && priorSnapshot.TryGetObjectiveRecords(0, out ObjectiveRecord[] prior0))
                        objectives0 = prior0;
                    groupStance0 = PreferNonEmpty(groupStance0, priorSnapshot.GetGroupStanceDict(0));
                    groupSector0 = PreferNonEmpty(groupSector0, priorSnapshot.GetGroupSectorDict(0));
                    regimentalDecisions0 = PreferNonEmpty(regimentalDecisions0, priorSnapshot.GetRegimentalDecisionsDict(0));
                }
                if (!fresh1 && CanCarryForward(priorSnapshot, input, 1))
                {
                    dataHours1 = priorSnapshot.GetSideDataBattleHours(1);
                    if (objectives1.Length == 0 && priorSnapshot.TryGetObjectiveRecords(1, out ObjectiveRecord[] prior1))
                        objectives1 = prior1;
                    groupStance1 = PreferNonEmpty(groupStance1, priorSnapshot.GetGroupStanceDict(1));
                    groupSector1 = PreferNonEmpty(groupSector1, priorSnapshot.GetGroupSectorDict(1));
                    regimentalDecisions1 = PreferNonEmpty(regimentalDecisions1, priorSnapshot.GetRegimentalDecisionsDict(1));
                }

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
                    groupSector1: groupSector1,
                    objectiveRecords0: objectives0,
                    objectiveRecords1: objectives1,
                    regimentalDecisions0: regimentalDecisions0,
                    regimentalDecisions1: regimentalDecisions1,
                    side0DataBattleHours: dataHours0,
                    side1DataBattleHours: dataHours1);
            }
            catch (Exception e)
            {
                // Worker-thread-safe warning: do NOT use OnceLog (dedup state not thread-safe).
                Plugin.Log.LogWarning("[TacticalOrchestratorWorkerAnalysis] cycle "
                    + cycleId + " degraded: " + e.GetType().Name + " " + e.Message);
                return TacticalDecisionSnapshot.Empty;
            }
        }

        // True when this cycle captured observations for the given alliance (F-stagger:
        // only the side whose heavy snapshot was built carries observations).
        private static bool HasFreshObservations(TacticalOrchestratorWorkerInput input, int allianceId)
        {
            return (input.Side0AllianceId == allianceId && input.Side0Observations.Count > 0)
                || (input.Side1AllianceId == allianceId && input.Side1Observations.Count > 0);
        }

        private static bool CanCarryForward(TacticalDecisionSnapshot prior, TacticalOrchestratorWorkerInput input, int allianceId)
        {
            return prior != null
                && TacticalDecisionSnapshot.IsCarryForwardFresh(
                    prior.BattleSequence,
                    input.BattleSequence,
                    prior.GetSideDataBattleHours(allianceId),
                    input.BattleHours,
                    TacticalDecisionSnapshot.MaxCarryForwardBattleHours);
        }

        private static Dictionary<int, T> PreferNonEmpty<T>(Dictionary<int, T> current, Dictionary<int, T> prior)
        {
            if (current != null && current.Count > 0) return current;
            return prior != null && prior.Count > 0 ? prior : current;
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
            // Gap-fill chunk 3: captured plan state from main. When HasPlan is
            // false, the snapshot is Empty (default plan, sector=-1) and the
            // worker publishes hasPlan=false / currentPlan=default — same as
            // pre-chunk-3 behavior. When HasPlan is true, the worker forwards
            // the sync-tick's plan into TacticalSideDecision.
            TacticalArmyPlanStateSnapshot planState = sideIdx == 0
                ? input.Side0PlanState
                : input.Side1PlanState;

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
                currentPlan: planState.CurrentPlan,            // chunk 3: captured from ArmyOrchestrator
                currentIntent: intent,
                commandTree: commandTree,
                strategicIntent: strategicIntent,
                forceAvailability: force,
                commanderPersonality: personality,
                lastReplanTrigger: ReplanTrigger.None,        // deferred: needs stateless EvaluateReplanTrigger
                hasPlan: planState.HasPlan);                  // chunk 3: captured from ArmyOrchestrator
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

        // Per-session bookkeeping for first-emission deduplication. Worker thread is NOT
        // proven OnceLog-safe; using volatile bool flags is the cheap dedup primitive
        // that works across threads without contention.
        private static long _objectivesDiagFlags;  // bit-packed: 6 bits (3 reason flags × 2 sides)

        private static bool TryClaimDiagBit(int sideIdx, int reasonIdx)
        {
            long bit = 1L << (sideIdx * 4 + reasonIdx);
            long prev, next;
            do
            {
                prev = System.Threading.Interlocked.Read(ref _objectivesDiagFlags);
                if ((prev & bit) != 0) return false;
                next = prev | bit;
            } while (System.Threading.Interlocked.CompareExchange(ref _objectivesDiagFlags, next, prev) != prev);
            return true;
        }

        // Worker-migration (2026-05-21): runs BuildObjectiveRecordsFromContext on the
        // worker thread. The context is pure data captured on main; the per-side
        // IObservationSource has been the worker's input since the chunk-1 migration.
        // Returns empty array on suppressed side / empty context / null source — main
        // detects empty via TryGetObjectiveRecords and uses its legacy on-main builder.
        //
        // Diagnostic logs (cross-thread-safe via Interlocked-CAS bit flags) emit ONCE
        // per (side, reason) per session so smoke can see why the worker isn't
        // producing records. Each branch claims a flag bit; subsequent hits on the
        // same branch are silent.
        private static ObjectiveRecord[] BuildObjectivesForSide(int sideIdx, TacticalOrchestratorWorkerInput input)
        {
            try
            {
                if (input == null) return Array.Empty<ObjectiveRecord>();

                ObjectiveRecordsCaptureContext context = sideIdx == 0
                    ? input.Side0ObjectiveCaptureContext
                    : input.Side1ObjectiveCaptureContext;
                int allianceId = sideIdx == 0
                    ? input.Side0AllianceId
                    : input.Side1AllianceId;

                if (context == null || context == ObjectiveRecordsCaptureContext.Empty)
                {
                    if (TryClaimDiagBit(sideIdx, 0))
                    {
                        Plugin.Log.LogInfo("[TacticalOrchestratorWorkerAnalysis] side=" + sideIdx
                            + " context-empty (alliance=" + allianceId
                            + ") — main thread didn't capture; check EnableWorkerObjectiveRecords flag");
                    }
                    return Array.Empty<ObjectiveRecord>();
                }

                var sourceAdapter = WorkerObservationSourceAdapter.Build(input, sideIdx);
                if (sourceAdapter == null)
                {
                    if (TryClaimDiagBit(sideIdx, 1))
                    {
                        int obsCount = sideIdx == 0
                            ? (input.Side0Observations?.Count ?? -1)
                            : (input.Side1Observations?.Count ?? -1);
                        Plugin.Log.LogInfo("[TacticalOrchestratorWorkerAnalysis] side=" + sideIdx
                            + " observation-adapter-null (alliance=" + allianceId
                            + " obsCount=" + obsCount + ")");
                    }
                    return Array.Empty<ObjectiveRecord>();
                }

                var records = TacticalVisionRuntimeAdapter.BuildObjectiveRecordsFromContext(
                    sourceAdapter,
                    context,
                    allianceId);
                if (TryClaimDiagBit(sideIdx, 2))
                {
                    Plugin.Log.LogInfo("[TacticalOrchestratorWorkerAnalysis] side=" + sideIdx
                        + " first-build success (alliance=" + allianceId
                        + " records=" + (records?.Length ?? 0)
                        + " mapObj=" + context.MapObjectives.Count
                        + " chains=" + context.ObjectiveChains.Count
                        + " avenues=" + context.ApproachAvenues.Length + ")");
                }
                return records ?? Array.Empty<ObjectiveRecord>();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestratorWorkerAnalysis] BuildObjectivesForSide "
                    + sideIdx + " degraded: " + e.GetType().Name + " " + e.Message);
                return Array.Empty<ObjectiveRecord>();
            }
        }

        // Reset the diagnostic flags so the next battle gets a fresh set of one-shot
        // emissions. Called from TacticalBattleCoordinatorRuntime.ResetRuntimeTickState.
        internal static void ResetForBattleStart()
        {
            System.Threading.Interlocked.Exchange(ref _objectivesDiagFlags, 0L);
        }

        // Regimental intelligence builder (2026-05-21). For each commandable own-side
        // regiment in the observation array, compute a quadrant threat assessment
        // and run the doctrine scorer to produce a per-regiment action. Results are
        // keyed by GameObject InstanceID and consumed on main via
        // TacticalDecisionSnapshot.TryGetRegimentalDecision.
        //
        // Empty observation array → empty dict (default NoAction at apply time).
        private static Dictionary<int, TacticalRegimentalDecision> BuildRegimentalDecisionsForSide(int sideIdx, TacticalOrchestratorWorkerInput input)
        {
            try
            {
                if (input == null) return new Dictionary<int, TacticalRegimentalDecision>(0);

                IReadOnlyList<TacticalUnitObservation> observations = sideIdx == 0
                    ? input.Side0Observations
                    : input.Side1Observations;
                int allianceId = sideIdx == 0
                    ? input.Side0AllianceId
                    : input.Side1AllianceId;
                if (observations == null || observations.Count == 0)
                    return new Dictionary<int, TacticalRegimentalDecision>(0);

                // Build allied + enemy index partition for this side. Mirrors
                // WorkerObservationSourceAdapter logic but inlined so we don't
                // construct another adapter just to read indices.
                var alliedIndices = new List<int>(observations.Count);
                var enemyIndices = new List<int>(observations.Count);
                for (int i = 0; i < observations.Count; i++)
                {
                    var obs = observations[i];
                    if (obs.Alliance == allianceId) alliedIndices.Add(i);
                    else if (obs.Alliance >= 0) enemyIndices.Add(i);
                }

                var result = new Dictionary<int, TacticalRegimentalDecision>(alliedIndices.Count);
                for (int ai = 0; ai < alliedIndices.Count; ai++)
                {
                    int idx = alliedIndices[ai];
                    var own = observations[idx];
                    if (own.IsRouted || own.PermanentlyDetached) continue;
                    if (own.InstanceId == 0) continue;
                    // Only regiments at the writeable layer get scored. Mirrors
                    // the same filter the existing group-stance patch uses.
                    if (!TacticalDoctrineScorer.AllowsLocalGroupStanceWriter(own.Unittyp)) continue;

                    var threats = TacticalRegimentalThreatComputer.Compute(
                        own,
                        observations,
                        alliedIndices,
                        enemyIndices);

                    // Casualty fraction not in the observation today; pass 0 and
                    // let the doctrine's morale check do the work. (Future: capture
                    // strength_history vs strength for a real casualty fraction.)
                    float casualtyFraction = 0f;
                    // Worker doesn't have direct morale on the observation either
                    // — TacticalUnitObservation captures position/strength but not
                    // a per-unit morale01. Use a neutral default of 0.7 so the
                    // doctrine's "low morale" branch only fires via casualty path.
                    float ownMorale01 = 0.7f;
                    // stanceAllowsWheel: pessimistic default true; the brigade
                    // executor's eligibility gate still filters apply-time.
                    bool stanceAllowsWheel = true;
                    bool isRouted = own.IsRouted;

                    var decision = TacticalRegimentalIntelligenceDoctrine.Decide(
                        threats,
                        ownMorale01,
                        casualtyFraction,
                        isRouted,
                        stanceAllowsWheel);

                    if (decision.Action != RegimentalAction.NoAction)
                    {
                        result[own.InstanceId] = decision;
                    }
                }
                return result;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestratorWorkerAnalysis] BuildRegimentalDecisionsForSide "
                    + sideIdx + " degraded: " + e.GetType().Name + " " + e.Message);
                return new Dictionary<int, TacticalRegimentalDecision>(0);
            }
        }
    }
}
