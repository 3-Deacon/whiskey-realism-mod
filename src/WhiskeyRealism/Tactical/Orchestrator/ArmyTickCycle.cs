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

        public static ReplanTrigger MaybeReplan(
            ArmyOrchestrator orchestrator,
            float deltaSeconds,
            ArmyEvidence ownEvidence,
            EnemyVisibleState enemyVisible,
            float ownMainEffortStrength,
            float ownArmyMorale,
            float ownReservesCommittedFraction,
            float reinforcementsArrivingDelta,
            int minReplanSeconds)
        {
            if (orchestrator == null) return ReplanTrigger.None;

            var allianceId = orchestrator.AllianceId;
            if (deltaSeconds > 0f && !float.IsNaN(deltaSeconds) && !float.IsInfinity(deltaSeconds))
            {
                float currentClock;
                _clockByAlliance.TryGetValue(allianceId, out currentClock);
                _clockByAlliance[allianceId] = currentClock + deltaSeconds;
            }
            orchestrator.AdvancePlanAge(deltaSeconds);

            if (!orchestrator.HasPlan) return ReplanTrigger.None;

            var intent = ArmyIntentInference.Build(ownEvidence, enemyVisible);
            orchestrator.ObserveIntent(intent);
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
                enemyMainEffortShiftConfidenceWeighted: ConfidenceWeightedShift(intent));

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
