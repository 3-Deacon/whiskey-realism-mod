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
    // new cycle starts (ObserveRunthrough) or forts/orders/sites change (BeginCall).
    public sealed class FortSiteDecisionCache
    {
        private readonly System.Collections.Generic.Dictionary<int, FortConstructionGovernorDecision> _decisions =
            new System.Collections.Generic.Dictionary<int, FortConstructionGovernorDecision>();
        private long _signature = long.MinValue;

        public static long Signature(int siteCount, int fortCount, int orderCount)
        {
            return ((long)(siteCount & 0xFFFFF) << 40) | ((long)(fortCount & 0xFFFFF) << 20) | (uint)(orderCount & 0xFFFFF);
        }

        private int _lastRunthrough = -1;

        // Call on every CheckFortConstruction call, before vanilla's unit gate:
        // currentfortconstructionunitrunthrough only grows within a cycle and is reset
        // to 0 when job 21 completes (16398-16402, 11522-11525).
        public void ObserveRunthrough(int runthrough)
        {
            if (runthrough == 0 || runthrough < _lastRunthrough)
                _decisions.Clear();
            _lastRunthrough = runthrough;
        }

        public void BeginCall(long signature)
        {
            if (signature == _signature) return;
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

    // The planner's fort site preference (plan G5c, spec "construction steering"). Vanilla's CheckFortConstruction (16347-16405) walks
    // fortconstructionsites in list order and orders the visiting unit onto the FIRST site that passes its tests (16383-16392), so
    // the order of the list is the only preference it has. When a site on a planned axis or in a threatened sector is one vanilla
    // would take for this unit, the fort patch (#27) hides every other site for the call, and vanilla takes it. When none is, the
    // list stays as the saturation governor left it.
    //   preferred[i]  the board marks site i as on a MainEffort axis or in a Hold / Screen sector;
    //   eligible[i]   site i passes every test vanilla applies to this unit (range, other forts, frontline, ability) and the governor;
    //   rank[i]       the construction ledger's Fort candidate score for site i (the TopFort carries a small bonus).
    public static class FortSitePreference
    {
        // The index of the one preferred eligible site to leave visible (highest rank, lowest index among equals), -1 for none.
        public static int Choose(bool[] preferred, bool[] eligible, float[] rank)
        {
            int best = -1;
            for (int i = 0; i < preferred.Length && i < eligible.Length; i++)
            {
                if (!preferred[i] || !eligible[i]) continue;
                float r = i < rank.Length ? rank[i] : 0f;
                if (best < 0 || r > (best < rank.Length ? rank[best] : 0f)) best = i;
            }
            return best;
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
