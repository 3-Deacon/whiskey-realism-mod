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

        public static void Reset()
        {
            _lastReplanByAlliance.Clear();
            _clockByAlliance.Clear();
        }

        public static void ResetForTest()
        {
            Reset();
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
            float reinforcementsArrivingDelta,
            int minReplanSeconds) =>
            MaybeReplan(
                orchestrator, deltaSeconds, ownEvidence, enemyVisible,
                ownMainEffortStrength, ownArmyMorale, ownReservesCommittedFraction,
                reinforcementsArrivingDelta, minReplanSeconds,
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
            float reinforcementsArrivingDelta,
            int minReplanSeconds,
            float ownAvgFatigue01,
            float ownAvgAmmo01,
            float nearestReinforcementHours,
            float nearestReinforcementStrength,
            float battleDeltaSeconds = -1f)
        {
            if (orchestrator == null) return ReplanTrigger.None;

            var allianceId = orchestrator.AllianceId;
            if (deltaSeconds > 0f && !float.IsNaN(deltaSeconds) && !float.IsInfinity(deltaSeconds))
            {
                float currentClock;
                _clockByAlliance.TryGetValue(allianceId, out currentClock);
                _clockByAlliance[allianceId] = currentClock + deltaSeconds;
            }
            // Time compression: if caller supplied a battle-time delta,
            // advance plan age on real-time and phase age on battle-time
            // separately. Phase budgets are commander-pace (battle clock)
            // decisions; replan rate limits are wallclock (real time).
            // Legacy callers that don't pass battleDeltaSeconds keep the
            // single-source behavior via AdvancePlanAge.
            if (battleDeltaSeconds >= 0f)
            {
                orchestrator.AdvancePlanAgeRealtimeOnly(deltaSeconds);
                orchestrator.AdvancePhaseAge(battleDeltaSeconds);
            }
            else
            {
                orchestrator.AdvancePlanAge(deltaSeconds);
            }

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
                    if (s.EnemyStrength <= 0f) continue;
                    if (s.SectorId == preShiftMainEffortSector)
                        preShiftMainEffortOdds = s.Odds;
                    if (s.Odds > decisiveSectorOdds)
                    {
                        decisiveSectorOdds = s.Odds;
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
                    if (s.SectorId == currentMainEffortSector && s.EnemyStrength > 0f)
                        currentMainEffortOdds = s.Odds;
                    if (s.SectorId != currentMainEffortSector && s.Odds > bestNonMainEffortOdds)
                        bestNonMainEffortOdds = s.Odds;
                }
            }

            // ---- Phase progression evaluator ----
            //
            // Drives Probe -> MainEffort -> Exploit -> Consolidate -> Withdraw
            // each tick. AdvancePhase resets phase age (NOT plan age) on
            // actual transitions so each phase has its own budget while the
            // overarching plan deadline (180s) still backstops via PhaseDeadline
            // replan trigger.
            orchestrator.EvaluateAndAdvancePhase(
                globalOddsCurrent: ownEvidence.CurrentOdds,
                mainEffortOddsCurrent: currentMainEffortOdds,
                mainEffortOddsHistory: orchestrator.HistoryGlobalOdds,
                armyMoraleCurrent: ownArmyMorale,
                armyMoraleFloor: 0.4f,
                reservesCommittedFraction: ownReservesCommittedFraction,
                mainEffortReadiness: readiness.Result);

            var input = new ReplanTriggerInput(
                planAgeSeconds: orchestrator.PlanAgeSeconds,
                currentPhase: orchestrator.CurrentPlan.Phase,
                mainEffortOwnStrength: ownMainEffortStrength,
                mainEffortHistoryOwnStrength: ownMainEffortStrength,
                globalOddsCurrent: ownEvidence.CurrentOdds,
                globalOddsHistory: orchestrator.HistoryGlobalOdds,
                armyMoraleCurrent: ownArmyMorale,
                armyMoraleFloor: 0.4f,
                reservesCommittedFraction: ownReservesCommittedFraction,
                reinforcementsArrivingDelta: reinforcementsArrivingDelta,
                enemyMainEffortShiftConfidenceWeighted: ConfidenceWeightedShift(intent),
                mainEffortLocalOdds: currentMainEffortOdds,
                mainEffortLocalOddsHistory: orchestrator.HistoryGlobalOdds,
                bestNonMainEffortSectorOdds: bestNonMainEffortOdds,
                currentMainEffortOdds: currentMainEffortOdds);

            var trigger = orchestrator.CheckReplanTriggers(input);
            if (trigger == ReplanTrigger.None) return ReplanTrigger.None;

            float now;
            _clockByAlliance.TryGetValue(allianceId, out now);
            if (_lastReplanByAlliance.TryGetValue(allianceId, out var lastReplan) &&
                now - lastReplan < minReplanSeconds)
            {
                return ReplanTrigger.None;
            }

            orchestrator.Replan(ownEvidence, intent);
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
