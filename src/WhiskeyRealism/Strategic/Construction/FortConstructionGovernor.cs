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

    public static class FortConstructionGovernor
    {
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
