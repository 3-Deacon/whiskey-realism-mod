using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Intent struct cascaded from the army echelon down to corps. Carries the
    /// active plan id, current phase, sector allocation, reserve trigger, and a
    /// [0,1] aggression bias derived from commander personality. Immutable.
    /// </summary>
    public readonly struct ArmyIntent
    {
        public ArmyIntent(
            BattlePlanId planId,
            BattlePhase phase,
            int mainEffortSector,
            int[] fixingSectors,
            int[] screeningSectors,
            float reserveCommitTriggerOdds,
            float aggressionBias01)
        {
            PlanId = planId;
            Phase = phase;
            MainEffortSector = mainEffortSector;
            FixingSectors = fixingSectors ?? Array.Empty<int>();
            ScreeningSectors = screeningSectors ?? Array.Empty<int>();
            ReserveCommitTriggerOdds = float.IsNaN(reserveCommitTriggerOdds) ? 1.0f : reserveCommitTriggerOdds;
            AggressionBias01 = Clamp01(aggressionBias01);
        }

        public BattlePlanId PlanId { get; }
        public BattlePhase Phase { get; }
        public int MainEffortSector { get; }
        public int[] FixingSectors { get; }
        public int[] ScreeningSectors { get; }
        public float ReserveCommitTriggerOdds { get; }
        public float AggressionBias01 { get; }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0.5f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
