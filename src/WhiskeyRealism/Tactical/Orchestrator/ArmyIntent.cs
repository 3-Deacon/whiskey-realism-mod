using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Intent struct cascaded from the army echelon down to consumers (#42 gate,
    /// future O4 division attach, telemetry). Carries the active plan id, current
    /// phase, sector allocation, reserve trigger, [0,1] aggression bias, and the
    /// optional per-direct-child intent list. Immutable.
    /// </summary>
    public readonly struct ArmyIntent
    {
        private static readonly IReadOnlyList<DirectChildIntent> EmptyChildren = Array.Empty<DirectChildIntent>();

        public ArmyIntent(
            BattlePlanId planId,
            BattlePhase phase,
            int mainEffortSector,
            int[] fixingSectors,
            int[] screeningSectors,
            float reserveCommitTriggerOdds,
            float aggressionBias01)
            : this(planId, phase, mainEffortSector, fixingSectors, screeningSectors,
                   reserveCommitTriggerOdds, aggressionBias01, directChildIntents: null)
        {
        }

        public ArmyIntent(
            BattlePlanId planId,
            BattlePhase phase,
            int mainEffortSector,
            int[] fixingSectors,
            int[] screeningSectors,
            float reserveCommitTriggerOdds,
            float aggressionBias01,
            IReadOnlyList<DirectChildIntent> directChildIntents)
        {
            PlanId = planId;
            Phase = phase;
            MainEffortSector = mainEffortSector;
            FixingSectors = fixingSectors ?? Array.Empty<int>();
            ScreeningSectors = screeningSectors ?? Array.Empty<int>();
            ReserveCommitTriggerOdds = Sanitize(reserveCommitTriggerOdds);
            AggressionBias01 = Clamp01(aggressionBias01);
            DirectChildIntents = directChildIntents ?? EmptyChildren;
        }

        public BattlePlanId PlanId { get; }
        public BattlePhase Phase { get; }
        public int MainEffortSector { get; }

        /// <summary>
        /// Sector ids assigned the fixing role. Treat as read-only; the orchestrator
        /// reuses this reference across cascaded intent instances, so mutating contents
        /// corrupts older intent snapshots.
        /// </summary>
        public int[] FixingSectors { get; }

        /// <summary>
        /// Sector ids assigned the screening role. Treat as read-only; the orchestrator
        /// reuses this reference across cascaded intent instances, so mutating contents
        /// corrupts older intent snapshots.
        /// </summary>
        public int[] ScreeningSectors { get; }

        public float ReserveCommitTriggerOdds { get; }
        public float AggressionBias01 { get; }

        /// <summary>
        /// Per-direct-child intent (Main / SupportMain / Fix / Screen / Reserve /
        /// RefuseLeft / RefuseRight / Fallback / Unknown). Empty when no children
        /// have been registered yet. Read-only; allocator returns a fresh array
        /// each tick and the orchestrator stores the reference.
        /// </summary>
        public IReadOnlyList<DirectChildIntent> DirectChildIntents { get; }

        private static float Sanitize(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            return v;
        }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0.5f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
