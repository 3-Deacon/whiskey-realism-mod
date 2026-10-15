using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.BugFixes
{
    // BUG-LOG-003. AICampaign.UpdateImportanceValues (decompile 14904-14998) walks one
    // IIP, one CBuilding and one Town per call and adds each to the AIArea under its
    // position (AIArea.GetAIArea(aiareas.GetColorOnPos(pos))), but only while
    // airuns <= 1 (14927, 14946, 14965). Buildings created later (supply depots, forts,
    // companies) never join their area, so AIArea.UpdateSupplyCapacity (10772-10801)
    // and the offensive objective pick (GetClosestObjectivePosOfArea 14542-14658) keep
    // the area contents of the first two AI runs. The fix registers what vanilla walks
    // after that with the same rule, and drops members that no longer exist.
    internal static class AIAreaMembership
    {
        // Vanilla's own registration covers airuns 0 and 1.
        internal const int VanillaLastRegisteringRun = 1;

        internal static bool ShouldRegister(bool fixEnabled, int aiRuns)
        {
            return fixEnabled && aiRuns > VanillaLastRegisteringRun;
        }

        internal static bool AddIfMissing<T>(IList<T> members, T item) where T : class
        {
            if (members == null || item == null || members.Contains(item)) return false;
            members.Add(item);
            return true;
        }

        internal static int RemoveWhere<T>(IList<T> members, Func<T, bool> isGone)
        {
            if (members == null || isGone == null) return 0;
            int removed = 0;
            for (int i = members.Count - 1; i >= 0; i--)
            {
                if (!isGone(members[i])) continue;
                members.RemoveAt(i);
                removed++;
            }
            return removed;
        }
    }
}
