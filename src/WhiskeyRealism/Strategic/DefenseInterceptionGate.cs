using System.Collections.Generic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    // What patch #25 does with vanilla's candidate list in one CheckForDefensiveOperations call (foresight F2, D11 requirement-interception).
    // Filter: remove the defence intent ledger's forbidden formations for the call and revert a forbidden commit (the legacy enforcement,
    // and the rollback). Observe: remove nothing and revert nothing, and report what Filter would have removed.
    internal enum DefenseFilterMode
    {
        Filter = 0,
        Observe = 1
    }

    internal static class DefenseInterceptionGate
    {
        // The mode for an AI alliance's call. Observe only when the interception flag, the planner and its executors are on, the alliance is
        // 0 or 1, a plan is published and its allocation fresh (CampaignWithholdView.TryFreshAllocation: executors allowed, within
        // PlanFreshness.MaxAgeDays) and the allocation assigns at least one formation (a CIC side's allocation is empty: the planner
        // issues it nothing). Otherwise Filter, with the first cause that applies in a fixed order: europe, flag, planner-off, no-plan, stale,
        // empty. A published plan whose allocation is not fresh is "stale", also when the cause is the pre-war gate, which the published
        // context carries but no input here tells apart.
        internal static DefenseFilterMode ModeFor(int allianceId, bool interceptionFlag, bool plannerFlag, bool executorsFlag, bool planPublished,
                                                  bool freshAllocation, int assignmentCount, out string filterWhy)
        {
            filterWhy = "";
            if (allianceId < 0 || allianceId > 1) return Filtering(out filterWhy, "europe");
            if (!interceptionFlag) return Filtering(out filterWhy, "flag");
            if (!plannerFlag || !executorsFlag) return Filtering(out filterWhy, "planner-off");
            if (!planPublished) return Filtering(out filterWhy, "no-plan");
            if (!freshAllocation) return Filtering(out filterWhy, "stale");
            if (assignmentCount <= 0) return Filtering(out filterWhy, "empty");
            return DefenseFilterMode.Observe;
        }

        private static DefenseFilterMode Filtering(out string filterWhy, string why)
        {
            filterWhy = why;
            return DefenseFilterMode.Filter;
        }

        // The filter row's mode= value: why the gate returned Filter.
        internal static string FilterModeText(string filterWhy)
        {
            return "filter:" + filterWhy;
        }

        // How many of the forbidden ids stand in vanilla's candidate list (ownunits): what the filter's removed= counts. The ids it
        // counted are appended to `into` when given, in the order they stand, so the caller can tally their reasons.
        internal static int WouldRemove(IReadOnlyCollection<int> presentIds, ICollection<int> forbidden, List<int> into = null)
        {
            int count = 0;
            if (presentIds == null || forbidden == null) return 0;
            foreach (int id in presentIds)
            {
                if (!forbidden.Contains(id)) continue;
                count++;
                if (into != null) into.Add(id);
            }
            return count;
        }

        // The observed row's dedupe key: it changes with the alliance, the count or the reasons, nothing else.
        internal static string ObservedSignature(int allianceId, int wouldRemove, string reasons)
        {
            return "alliance=" + allianceId + "|wouldRemove=" + wouldRemove + "|reasons=" + (reasons ?? "-");
        }
    }

    // Per alliance, the last observed row's signature, so a call that finds the same thing writes nothing. Per campaign
    // (CampaignScope), bound-checked: alliance 2 (Europe) and anything outside 0 and 1 is never indexed and never written.
    internal static class ObservedLog
    {
        private const int Alliances = 2;
        private static readonly string[] Last = new string[Alliances];

        static ObservedLog()
        {
            CampaignScope.Register("defense-filter-observed-log", () =>
            {
                for (int a = 0; a < Last.Length; a++) Last[a] = null;
            });
        }

        internal static bool ShouldEmit(int allianceId, string signature)
        {
            if (allianceId < 0 || allianceId >= Alliances) return false;
            if (string.Equals(Last[allianceId], signature, System.StringComparison.Ordinal)) return false;
            Last[allianceId] = signature;
            return true;
        }
    }
}
