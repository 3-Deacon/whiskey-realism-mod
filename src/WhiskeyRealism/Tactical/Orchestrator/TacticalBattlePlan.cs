using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    public enum BattlePlanId
    {
        Unknown = 0,
        LeeEnvelopment,
        JacksonValleyShuffle,
        McClellanPreparedDefense,
        ShermanManeuverFix,
        GrantContinuousAttrition,
        LongstreetDefensiveOverslope,
        HookerFlankDeparture,
        HoodFrontalAssault,
        BurnsideForcedAssault,
        BraggIndecisiveCommit,
        GenericAggressive,
        GenericCautious,
        GenericMethodical,
        GenericDesperate,
        // Cavalry-as-main-effort playbooks. Existing CavalryFollowDoctrine
        // handles per-unit guard/scout/screen/raid behavior; these playbooks
        // make cavalry the army's decisive arm rather than a supporting one.
        BufordCavalryScreenDelay,
        ForrestCavalryRaid,
        // Meeting engagement: both sides arrive piecemeal, neither set up.
        // Distinct from Probe (Probe assumes deploy-from-march; meeting
        // engagement is collide-while-moving). McPherson's Ridge Day 1.
        MeetingEngagement,
        // Joseph Johnston's Atlanta-campaign Fabian doctrine. Uses existing
        // CommandTaskType.Delay + WithdrawalDoctrine to thread disciplined
        // fight-yield-fight-yield rather than Fallback (panic) or
        // GenericDesperate (last stand).
        JohnstonFabianDelay,
    }

    public enum BattlePhase
    {
        Probe = 0,
        MainEffort = 1,
        Exploit = 2,
        Consolidate = 3,
        Withdraw = 4,
    }

    /// <summary>
    /// The army-echelon plan: source-playbook id, current phase, sector allocation,
    /// reserve commit trigger, and age. Read-only struct; orchestrator replaces it
    /// wholesale via WithPhase/WithAge or a fresh instance on replan.
    /// </summary>
    public readonly struct TacticalBattlePlan
    {
        public TacticalBattlePlan(
            BattlePlanId planId,
            BattlePhase phase,
            int mainEffortSector,
            int[] fixingSectors,
            int[] screeningSectors,
            float reserveCommitTriggerOdds,
            float ageSeconds,
            int jitterSeed)
        {
            PlanId = planId;
            Phase = phase;
            MainEffortSector = mainEffortSector;
            FixingSectors = fixingSectors ?? Array.Empty<int>();
            ScreeningSectors = screeningSectors ?? Array.Empty<int>();
            ReserveCommitTriggerOdds = Sanitize(reserveCommitTriggerOdds);
            AgeSeconds = Math.Max(0f, Sanitize(ageSeconds));
            JitterSeed = jitterSeed;
        }

        public BattlePlanId PlanId { get; }
        public BattlePhase Phase { get; }
        public int MainEffortSector { get; }

        /// <summary>
        /// Sector ids assigned the fixing role. Treat as read-only; the orchestrator
        /// reuses this reference across <see cref="WithPhase"/> / <see cref="WithAge"/>
        /// instances, so mutating contents corrupts older plan snapshots.
        /// </summary>
        public int[] FixingSectors { get; }

        /// <summary>
        /// Sector ids assigned the screening role. Treat as read-only; the orchestrator
        /// reuses this reference across <see cref="WithPhase"/> / <see cref="WithAge"/>
        /// instances, so mutating contents corrupts older plan snapshots.
        /// </summary>
        public int[] ScreeningSectors { get; }

        public float ReserveCommitTriggerOdds { get; }
        public float AgeSeconds { get; }
        public int JitterSeed { get; }

        public TacticalBattlePlan WithPhase(BattlePhase phase) =>
            new TacticalBattlePlan(PlanId, phase, MainEffortSector, FixingSectors, ScreeningSectors, ReserveCommitTriggerOdds, 0f, JitterSeed);

        public TacticalBattlePlan WithAge(float ageSeconds) =>
            new TacticalBattlePlan(PlanId, Phase, MainEffortSector, FixingSectors, ScreeningSectors, ReserveCommitTriggerOdds, ageSeconds, JitterSeed);

        public TacticalBattlePlan WithReserveCommitTriggerOdds(float reserveCommitTriggerOdds) =>
            new TacticalBattlePlan(PlanId, Phase, MainEffortSector, FixingSectors, ScreeningSectors, reserveCommitTriggerOdds, AgeSeconds, JitterSeed);

        public TacticalBattlePlan WithMainEffortSector(int mainEffortSector) =>
            new TacticalBattlePlan(PlanId, Phase, mainEffortSector, FixingSectors, ScreeningSectors, ReserveCommitTriggerOdds, AgeSeconds, JitterSeed);

        private static float Sanitize(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            return v;
        }
    }
}
