using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pure army-orchestrator tick driver. Runtime callers own telemetry and
    /// battle-object adaptation; this class only advances state and replans.
    /// </summary>
    public static class ArmyTickCycle
    {
        private static readonly Dictionary<int, float> _lastReplanByAlliance = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> _clockByAlliance = new Dictionary<int, float>();
        private static readonly Dictionary<int, double> _lastDoctrineNowByAlliance = new Dictionary<int, double>();

        public static void Reset()
        {
            _lastReplanByAlliance.Clear();
            _clockByAlliance.Clear();
            _lastDoctrineNowByAlliance.Clear();
        }

        public static void ResetForTest()
        {
            Reset();
        }

        /// <summary>
        /// Stamps a replan made outside <see cref="MaybeReplan(ArmyOrchestrator, float, ArmyEvidence, EnemyVisibleState, float, float, float, float, int, float, float, float, float, float, double)"/>
        /// (the coordinator's new-battle-day replan) so it counts against
        /// "Min Replan Seconds" like any other. Uses the doctrine time.
        /// </summary>
        public static void RecordReplan(int allianceId) => RecordReplan(allianceId, TacticalDoctrineTime.NowBattleSeconds);

        internal static void RecordReplan(int allianceId, double doctrineNowSeconds)
        {
            if (double.IsNaN(doctrineNowSeconds) || double.IsInfinity(doctrineNowSeconds)) return;
            _clockByAlliance[allianceId] = (float)doctrineNowSeconds;
            _lastReplanByAlliance[allianceId] = (float)doctrineNowSeconds;
            _lastDoctrineNowByAlliance[allianceId] = doctrineNowSeconds;
        }

        /// <summary>
        /// Legacy overload that defaults readiness fields to neutral values
        /// (PushReady decision). Existing call sites stay binary-compatible
        /// until DriveTickCycle wires through the new fatigue/ammo data.
        /// </summary>
        public static ReplanTrigger MaybeReplan(
            ArmyOrchestrator orchestrator,
            float deltaSeconds,
            ArmyEvidence ownEvidence,
            EnemyVisibleState enemyVisible,
            float ownMainEffortStrength,
            float ownArmyMorale,
            float ownReservesCommittedFraction,
            float ownReinforcementsArriving,
            int minReplanSeconds) =>
            MaybeReplan(
                orchestrator, deltaSeconds, ownEvidence, enemyVisible,
                ownMainEffortStrength, ownArmyMorale, ownReservesCommittedFraction,
                ownReinforcementsArriving, minReplanSeconds,
                ownAvgFatigue01: 0.2f,
                ownAvgAmmo01: 0.9f,
                nearestReinforcementHours: 0f,
                nearestReinforcementStrength: 0f);

        public static ReplanTrigger MaybeReplan(
            ArmyOrchestrator orchestrator,
            float deltaSeconds,
            ArmyEvidence ownEvidence,
            EnemyVisibleState enemyVisible,
            float ownMainEffortStrength,
            float ownArmyMorale,
            float ownReservesCommittedFraction,
            float ownReinforcementsArriving,
            int minReplanSeconds,
            float ownAvgFatigue01,
            float ownAvgAmmo01,
            float nearestReinforcementHours,
            float nearestReinforcementStrength,
            float battleDeltaSeconds = -1f,
            double doctrineNowSeconds = double.NaN)
        {
            if (orchestrator == null) return ReplanTrigger.None;

            var allianceId = orchestrator.AllianceId;
            // Doctrine clock (T8): plan age, phase age and the replan cooldown
            // all run on BATTLE time when the caller supplies it. Wall-clock
            // deltas drove the 180 s plan deadline before, so at 20x
            // compression a plan "expired" after ~10 battle-minutes and at 1x
            // every 3 wall-clock minutes. Callers pass
            // TacticalDoctrineTime.NowBattleSeconds, the coordinator's doctrine
            // clock, which is frozen overnight; a frozen value yields a zero
            // delta. Legacy callers without a battle clock keep the
            // single-clock behaviour.
            bool doctrineClock = !double.IsNaN(doctrineNowSeconds) && !double.IsInfinity(doctrineNowSeconds);
            if (doctrineClock)
            {
                double delta = 0.0;
                if (_lastDoctrineNowByAlliance.TryGetValue(allianceId, out var lastDoctrineNow) && doctrineNowSeconds > lastDoctrineNow)
                    delta = doctrineNowSeconds - lastDoctrineNow;
                _lastDoctrineNowByAlliance[allianceId] = doctrineNowSeconds;
                battleDeltaSeconds = (float)delta;
            }
            bool battleClock = battleDeltaSeconds >= 0f;
            float clockDelta = battleClock ? battleDeltaSeconds : deltaSeconds;
            // The replan cooldown runs on doctrine time itself when supplied,
            // the same clock RecordReplan stamps; otherwise on summed deltas.
            if (doctrineClock)
            {
                _clockByAlliance[allianceId] = (float)doctrineNowSeconds;
            }
            else if (clockDelta > 0f && !float.IsNaN(clockDelta) && !float.IsInfinity(clockDelta))
            {
                float currentClock;
                _clockByAlliance.TryGetValue(allianceId, out currentClock);
                _clockByAlliance[allianceId] = currentClock + clockDelta;
            }
            orchestrator.AdvancePlanAge(clockDelta);

            orchestrator.ObserveFrontGeometry(enemyVisible);
            orchestrator.ObserveGlobalOdds(ownEvidence.CurrentOdds);
            if (!orchestrator.HasPlan) return ReplanTrigger.None;

            var intent = ArmyIntentInference.Build(ownEvidence, enemyVisible);
            orchestrator.ObserveIntent(intent);

            // ---- Sector readiness decision (fresh-troops gate) ----
            //
            // Computes whether the commander has effective force to push, OR
            // should hold for reinforcements, OR push despite fatigue, OR
            // preserve the force. Army-wide averages for now (per-sector
            // aggregation is a future refinement). Defaults make the decision
            // a no-op (PushReady) when the caller passes neutral data.
            float aggression01 = (orchestrator.CommanderPersonality.Aggression + 1f) * 0.5f;
            if (aggression01 < 0f) aggression01 = 0f;
            if (aggression01 > 1f) aggression01 = 1f;
            float enemyStrengthForReadiness = ownMainEffortStrength > 0f && ownEvidence.CurrentOdds > 0.01f
                ? ownMainEffortStrength / ownEvidence.CurrentOdds
                : ownMainEffortStrength;
            var readinessInput = new TacticalSectorReadinessDoctrine.Input(
                ownRawStrength: ownMainEffortStrength,
                avgFatigue01: ownAvgFatigue01,
                avgAmmo01: ownAvgAmmo01,
                avgMorale01: ownArmyMorale,
                enemyStrength: enemyStrengthForReadiness,
                reinforcementHours: nearestReinforcementHours,
                reinforcementStrength: nearestReinforcementStrength,
                commanderAggression01: aggression01);
            var readiness = TacticalSectorReadinessDoctrine.Decide(readinessInput);

            // ---- Sector-level evidence for phase progression + main-effort shift ----
            //
            // EnemyVisibleState.Sectors already carries per-sector own/enemy
            // strength (built from BattleUnits + vision per tick). We use it
            // to compute:
            //   currentMainEffortOdds       - odds in the plan's main effort sector
            //   decisiveSectorId            - sector with the best opportunity right now
            //   bestNonMainEffortOdds       - best odds outside the current main effort
            // These feed the phase progression doctrine, the offensive replan
            // triggers, and ConsiderMainEffortShift. Sector index here is the
            // EnemyVisibleSector.SectorId, which matches plan.MainEffortSector
            // (both originate from the same group-sector estimator).
            // Pass 1: identify the decisive sector AND the current main effort
            // sector's odds in one walk. Both feed the hysteresis check in
            // ConsiderMainEffortShift below — without the current sector's
            // odds, the shift can't tell whether the candidate is materially
            // better or just marginally better.
            int preShiftMainEffortSector = orchestrator.CurrentPlan.MainEffortSector;
            int decisiveSectorId = -1;
            float decisiveSectorOdds = 0f;
            float preShiftMainEffortOdds = 0f;
            if (enemyVisible.Sectors != null)
            {
                for (int i = 0; i < enemyVisible.Sectors.Length; i++)
                {
                    var s = enemyVisible.Sectors[i];
                    // Bands with no enemy carry no odds signal. Band odds are
                    // capped and blended toward unknown by intel confidence
                    // (EnemyVisibleState.SectorOdds), so a fully observed weak
                    // point is a decisive-sector candidate and a picket seen
                    // under low coverage is not.
                    if (!s.HasEnemy) continue;
                    float odds = enemyVisible.SectorOdds(s);
                    if (s.SectorId == preShiftMainEffortSector)
                        preShiftMainEffortOdds = odds;
                    if (odds > decisiveSectorOdds)
                    {
                        decisiveSectorOdds = odds;
                        decisiveSectorId = s.SectorId;
                    }
                }
            }

            // ---- Main effort shift (with hysteresis) ----
            //
            // Hysteresis-aware: candidate must beat current by 25% in odds.
            // Critical at high time compression (20x) where sector odds
            // bounce in real-time terms — without the margin, main effort
            // could oscillate every wallclock second. Internal guards still
            // block Consolidate/Withdraw and same-sector calls.
            if (decisiveSectorId >= 0)
            {
                orchestrator.ConsiderMainEffortShift(
                    decisiveSectorId, decisiveSectorOdds, preShiftMainEffortOdds);
            }

            // Pass 2 (post-shift): compute the offensive-trigger inputs
            // against the POST-SHIFT main effort sector. Otherwise the
            // BreakthroughOpportunity trigger would fire spuriously right
            // after shifting (the just-shifted-to sector would still appear
            // as "off-axis best" from the pre-shift snapshot).
            int currentMainEffortSector = orchestrator.CurrentPlan.MainEffortSector;
            float currentMainEffortOdds = ownEvidence.CurrentOdds; // fallback if sector not seen
            float bestNonMainEffortOdds = 0f;
            if (enemyVisible.Sectors != null)
            {
                for (int i = 0; i < enemyVisible.Sectors.Length; i++)
                {
                    var s = enemyVisible.Sectors[i];
                    if (!s.HasEnemy) continue;
                    float odds = enemyVisible.SectorOdds(s);
                    if (s.SectorId == currentMainEffortSector)
                        currentMainEffortOdds = odds;
                    else if (odds > bestNonMainEffortOdds)
                        bestNonMainEffortOdds = odds;
                }
            }

            // ---- Main-effort band history (T7) ----
            //
            // The main band's own strength and odds at the last replan (or at
            // first sighting of this band) are the history the sector-loss and
            // breakthrough checks compare against. Previously both "histories"
            // were the army-wide total / global odds, so MainEffortSectorLoss
            // could never fire and the 1.35x breakthrough ratio divided sector
            // odds by global odds.
            float mainBandOwn = 0f;
            float mainBandOdds = 0f;
            bool mainBandSeen = false;
            if (enemyVisible.Sectors != null)
            {
                for (int i = 0; i < enemyVisible.Sectors.Length; i++)
                {
                    var s = enemyVisible.Sectors[i];
                    if (s.SectorId != currentMainEffortSector) continue;
                    mainBandSeen = true;
                    mainBandOwn = s.OwnStrength;
                    if (s.HasEnemy) mainBandOdds = enemyVisible.SectorOdds(s);
                }
            }
            var baseline = orchestrator.LastReplanBaseline;
            if (mainBandSeen && mainBandOwn > 0f && !baseline.HasMainEffortSnapshot(currentMainEffortSector))
            {
                baseline = baseline.WithMainEffort(currentMainEffortSector, mainBandOwn, mainBandOdds);
                orchestrator.RecordReplanBaseline(baseline);
            }
            else if (baseline.MainEffortSector == currentMainEffortSector && baseline.MainEffortOdds <= 0f && mainBandOdds > 0f)
            {
                baseline = baseline.WithMainEffort(currentMainEffortSector, baseline.MainEffortOwnStrength, mainBandOdds);
                orchestrator.RecordReplanBaseline(baseline);
            }
            float mainOwnCurrent = mainBandSeen && mainBandOwn > 0f ? mainBandOwn : ownMainEffortStrength;
            float mainOwnHistory = baseline.HasMainEffortSnapshot(currentMainEffortSector)
                ? baseline.MainEffortOwnStrength
                : mainOwnCurrent;
            float mainOddsHistory = baseline.MainEffortSector == currentMainEffortSector && baseline.MainEffortOdds > 0f
                ? baseline.MainEffortOdds
                : currentMainEffortOdds;
            float reinforcementShare = ownReinforcementsArriving / Math.Max(1f, ownMainEffortStrength);

            // ---- Phase progression evaluator ----
            //
            // Drives Probe -> MainEffort -> Exploit -> Consolidate -> Withdraw
            // each tick. AdvancePhase resets phase age (NOT plan age) on
            // actual transitions so each phase has its own budget while the
            // overarching plan deadline (ArmyReplanTriggers.PlanDeadlineBattleSeconds)
            // still backstops via the PhaseDeadline replan trigger.
            orchestrator.EvaluateAndAdvancePhase(
                globalOddsCurrent: ownEvidence.CurrentOdds,
                mainEffortOddsCurrent: currentMainEffortOdds,
                mainEffortOddsHistory: mainOddsHistory,
                armyMoraleCurrent: ownArmyMorale,
                armyMoraleFloor: 0.4f,
                reservesCommittedFraction: ownReservesCommittedFraction,
                mainEffortReadiness: readiness.Result,
                intelConfidence01: enemyVisible.IntelConfidence01);

            var input = new ReplanTriggerInput(
                planAgeSeconds: orchestrator.PlanAgeSeconds,
                currentPhase: orchestrator.CurrentPlan.Phase,
                mainEffortOwnStrength: mainOwnCurrent,
                mainEffortHistoryOwnStrength: mainOwnHistory,
                globalOddsCurrent: ownEvidence.CurrentOdds,
                globalOddsHistory: orchestrator.HistoryGlobalOdds,
                armyMoraleCurrent: ownArmyMorale,
                armyMoraleFloor: 0.4f,
                reservesCommittedFraction: ownReservesCommittedFraction,
                ownReinforcementsArriving: ownReinforcementsArriving,
                enemyMainEffortShiftConfidenceWeighted: ConfidenceWeightedShift(intent),
                mainEffortLocalOdds: currentMainEffortOdds,
                mainEffortLocalOddsHistory: mainOddsHistory,
                bestNonMainEffortSectorOdds: bestNonMainEffortOdds,
                currentMainEffortOdds: currentMainEffortOdds,
                // Opportunity-change trigger inputs (2026-05-21). The orchestrator
                // refreshes _currentReinforcementOpportunity every tick via
                // UpdateReinforcementOpportunity; _lastReplanOpportunity is
                // snapshotted in Replan(). Differing values fire OpportunityChange.
                currentOpportunity: orchestrator.CurrentReinforcementOpportunity.Opportunity,
                lastReplanOpportunity: orchestrator.LastReplanOpportunity,
                armyMoraleAtLastReplan: baseline.Morale,
                reservesCommittedAtLastReplan: baseline.ReservesCommitted,
                ownStrength: ownMainEffortStrength,
                reinforcementShareAtLastReplan: baseline.ReinforcementShare,
                currentEnemyIntent: intent.PrimaryIntent,
                enemyIntentAtLastReplan: baseline.EnemyIntent);

            var trigger = orchestrator.CheckReplanTriggers(input);
            if (trigger == ReplanTrigger.None) return ReplanTrigger.None;

            float now;
            _clockByAlliance.TryGetValue(allianceId, out now);
            // "Min Replan Seconds" is measured on the same clock as plan age:
            // battle seconds when the caller supplies the battle clock.
            float cooldown = minReplanSeconds;
            // A stamp later than now means the clock ran backwards (reload
            // to an earlier save): treat the last replan as long ago.
            if (_lastReplanByAlliance.TryGetValue(allianceId, out var lastReplan) &&
                lastReplan <= now && now - lastReplan < cooldown)
            {
                return ReplanTrigger.None;
            }

            orchestrator.Replan(ownEvidence, intent);
            orchestrator.RecordReplanBaseline(new ReplanBaseline(
                ownArmyMorale,
                ownReservesCommittedFraction,
                reinforcementShare,
                intent.PrimaryIntent,
                orchestrator.CurrentPlan.MainEffortSector,
                mainOwnCurrent,
                mainBandOdds));
            _lastReplanByAlliance[allianceId] = now;
            return trigger;
        }

        private static float ConfidenceWeightedShift(TacticalIntentModel intent)
        {
            if (intent.PrimaryIntent == InferredIntent.Unknown) return 0f;
            if (intent.Confidence01 < ArmyIntentInference.ConfidenceFloor) return 0f;

            float baseWeight;
            switch (intent.PrimaryIntent)
            {
                case InferredIntent.Attack:
                    baseWeight = 1.0f;
                    break;
                case InferredIntent.Defend:
                case InferredIntent.Refuse:
                    baseWeight = 0.5f;
                    break;
                case InferredIntent.Withdraw:
                    baseWeight = 0.7f;
                    break;
                case InferredIntent.Probe:
                    baseWeight = 0.3f;
                    break;
                default:
                    baseWeight = 0f;
                    break;
            }

            return Math.Min(1f, baseWeight * intent.Confidence01);
        }
    }
}
