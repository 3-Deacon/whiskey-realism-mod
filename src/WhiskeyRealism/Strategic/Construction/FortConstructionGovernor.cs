namespace WhiskeyRealism.Strategic.Construction
{
    public sealed class FortConstructionGovernorOptions
    {
        public int LocalSoftCap = 2;
        public int LocalHardCap = 4;
        public int CapitalSoftCap = 4;
        public int CapitalHardCap = 7;
        public float ThreatThreshold = 0.35f;
    }

    public struct FortConstructionSiteContext
    {
        public int ExistingFortCount;
        public int ActiveOrderCount;
        public bool NearCapital;
        public float ThreatRatio;
    }

    public struct FortConstructionGovernorDecision
    {
        public bool Allow;
        public string Reason;
        public int SoftCap;
        public int HardCap;
        public int CurrentCount;
    }

    // Mirrors the early returns of vanilla AICampaign.CheckFortConstruction
    // (decompile 16360-16381). When any fails, vanilla never reads fortconstructionsites.
    public struct FortConstructionCallGate
    {
        public bool HasBattlefieldSetup;
        public bool HasBattleUnits;
        public bool FactionAlreadyConstructing;
        public bool UnitPresent;
        public bool UnitEligible;
    }

    // Site decisions depend on the site and world state, not on the unit job 21 is
    // visiting, so they are reused across one faction's job-21 cycle and rescored when a
    // new cycle starts or forts/orders/sites change.
    public sealed class FortSiteDecisionCache
    {
        private readonly System.Collections.Generic.Dictionary<int, FortConstructionGovernorDecision> _decisions =
            new System.Collections.Generic.Dictionary<int, FortConstructionGovernorDecision>();
        private long _signature = long.MinValue;

        public static long Signature(int siteCount, int fortCount, int orderCount)
        {
            return ((long)(siteCount & 0xFFFFF) << 40) | ((long)(fortCount & 0xFFFFF) << 20) | (uint)(orderCount & 0xFFFFF);
        }

        public void BeginCall(bool newCycle, long signature)
        {
            if (!newCycle && signature == _signature) return;
            _decisions.Clear();
            _signature = signature;
        }

        public bool TryGet(int siteIndex, out FortConstructionGovernorDecision decision)
        {
            return _decisions.TryGetValue(siteIndex, out decision);
        }

        public void Store(int siteIndex, FortConstructionGovernorDecision decision)
        {
            _decisions[siteIndex] = decision;
        }
    }

    public static class FortConstructionGovernor
    {
        public static bool ShouldEvaluateSites(FortConstructionCallGate gate)
        {
            return gate.HasBattlefieldSetup &&
                gate.HasBattleUnits &&
                !gate.FactionAlreadyConstructing &&
                gate.UnitPresent &&
                gate.UnitEligible;
        }

        public static FortConstructionGovernorDecision Decide(
            FortConstructionSiteContext context,
            FortConstructionGovernorOptions options = null)
        {
            options = options ?? new FortConstructionGovernorOptions();
            int current = context.ExistingFortCount + context.ActiveOrderCount;
            int softCap = context.NearCapital ? options.CapitalSoftCap : options.LocalSoftCap;
            int hardCap = context.NearCapital ? options.CapitalHardCap : options.LocalHardCap;

            if (current >= hardCap)
                return Decision(false, "hard-cap", softCap, hardCap, current);

            if (current >= softCap && context.ThreatRatio < options.ThreatThreshold)
                return Decision(false, "saturated-low-threat", softCap, hardCap, current);

            return Decision(true, "allowed", softCap, hardCap, current);
        }

        private static FortConstructionGovernorDecision Decision(
            bool allow,
            string reason,
            int softCap,
            int hardCap,
            int current)
        {
            return new FortConstructionGovernorDecision
            {
                Allow = allow,
                Reason = reason,
                SoftCap = softCap,
                HardCap = hardCap,
                CurrentCount = current
            };
        }
    }
}
