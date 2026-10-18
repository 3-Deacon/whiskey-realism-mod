using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Generic tactical archetypes (renamed 2026-05-23 from historical-figure
    /// labels: "LeeEnvelopment" / "ShermanManeuverFix" / etc. The historical
    /// names leaked into the selection logic as FactionAffinity bias and
    /// caused incorrect mental models — playbooks are pure tactical patterns
    /// available to ANY commander on EITHER side whose personality + force
    /// situation fits. Selection is fully personality + terrain + odds +
    /// composition driven; no faction lock or commander-name shortcut.
    /// </summary>
    public enum BattlePlanId
    {
        Unknown = 0,
        DoubleEnvelopment,          // converging two-wing envelopment (was LeeEnvelopment)
        MobileFlankMarch,           // fast operational flank (was JacksonValleyShuffle)
        PreparedDefense,            // methodical defensive setup (was McClellanPreparedDefense)
        ManeuverAndFix,             // fix center, maneuver wing to flank (was ShermanManeuverFix)
        ContinuousAttrition,        // sustained attritional pressure (was GrantContinuousAttrition)
        ReverseSlopeDefense,        // defensive reverse-slope hold (was LongstreetDefensiveOverslope)
        WideFlankApproach,          // single wide-arc flank march (was HookerFlankDeparture)
        FrontalShockAssault,        // aggressive frontal commit (was HoodFrontalAssault)
        ForcedFrontalAttack,        // frontal attack under disadvantage (was BurnsideForcedAssault)
        HesitatedCommit,            // methodical-aggressive with hesitation (was BraggIndecisiveCommit)
        GenericAggressive,
        GenericCautious,
        GenericMethodical,
        GenericDesperate,
        // Cavalry-as-main-effort playbooks. CavalryFollowDoctrine handles
        // per-unit guard/scout/screen/raid behavior; these playbooks make
        // cavalry the army's decisive arm rather than a supporting one.
        CavalryScreeningDelay,      // cavalry screen + delaying action (was BufordCavalryScreenDelay)
        CavalryRaid,                // cavalry deep raid (was ForrestCavalryRaid)
        // Meeting engagement: both sides arrive piecemeal, neither set up.
        // Distinct from Probe (Probe assumes deploy-from-march; meeting
        // engagement is collide-while-moving).
        MeetingEngagement,
        // Disciplined fight-yield-yield delaying defense. Uses
        // CommandTaskType.Delay + WithdrawalDoctrine for ordered withdrawal
        // rather than Fallback (panic) or GenericDesperate (last stand).
        DelayingDefense,            // disciplined yielding defense (was JohnstonFabianDelay)
        // BC14W: a frontal assault on the focal point of perceived enemy works, offered only when no turn will do and the commander is willing.
        FocalAssault,
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
            int jitterSeed,
            bool mirrored = false)
        {
            Mirrored = mirrored;
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

        /// <summary>
        /// BC14 requirement-selected-side: the feasibility evaluation scored this plan on its mirrored side (every rect's lateral offset
        /// negated), so the play executor projects the template mirrored from activation. False for every plan picked without a ledger.
        /// </summary>
        public bool Mirrored { get; }

        public TacticalBattlePlan WithPhase(BattlePhase phase) =>
            new TacticalBattlePlan(PlanId, phase, MainEffortSector, FixingSectors, ScreeningSectors, ReserveCommitTriggerOdds, 0f, JitterSeed, Mirrored);

        public TacticalBattlePlan WithReserveCommitTriggerOdds(float reserveCommitTriggerOdds) =>
            new TacticalBattlePlan(PlanId, Phase, MainEffortSector, FixingSectors, ScreeningSectors, reserveCommitTriggerOdds, AgeSeconds, JitterSeed, Mirrored);

        public TacticalBattlePlan WithMainEffortSector(int mainEffortSector) =>
            new TacticalBattlePlan(PlanId, Phase, mainEffortSector, FixingSectors, ScreeningSectors, ReserveCommitTriggerOdds, AgeSeconds, JitterSeed, Mirrored);

        public TacticalBattlePlan WithMirrored(bool mirrored) =>
            new TacticalBattlePlan(PlanId, Phase, MainEffortSector, FixingSectors, ScreeningSectors, ReserveCommitTriggerOdds, AgeSeconds, JitterSeed, mirrored);

        private static float Sanitize(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            return v;
        }
    }
}
