using System.Collections.Generic;
using System.Globalization;

namespace WhiskeyRealism.Strategic.Projects
{
    public sealed class ProjectDoctrineLogGate
    {
        private readonly HashSet<string> seenSignatures = new HashSet<string>();

        public bool ShouldLog(string signature)
        {
            if (string.IsNullOrEmpty(signature))
                return false;

            return seenSignatures.Add(signature);
        }

        public static string SelectionSignature(int alliance, int lane, int oldProjectId, int newProjectId, string reason)
        {
            return alliance + "|" + lane + "|" + oldProjectId + "|" + newProjectId + "|" + (reason ?? "");
        }

        public static string StarvedLaneSignature(ProjectLaneIntent intent)
        {
            if (intent == null)
                return "missing";

            return intent.Alliance + "|"
                + intent.SubsidyLane + "|"
                + intent.QueuedProjectId + "|"
                + FormatWhole(intent.FundingAvailable) + "|"
                + FormatWhole(intent.FundingNeeded) + "|"
                + FormatOneDecimal(intent.NetFundingPerDay) + "|"
                + FormatOneDecimal(intent.TimeToFundEstimateDays) + "|"
                + intent.ConstructionCurrentlyWins + "|"
                + intent.CriticalDoctrineProject;
        }

        private static string FormatWhole(float value)
        {
            return SafeFinite(value).ToString("F0", CultureInfo.InvariantCulture);
        }

        private static string FormatOneDecimal(float value)
        {
            return SafeFinite(value).ToString("F1", CultureInfo.InvariantCulture);
        }

        private static float SafeFinite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }
}
