using System;
using System.Collections.Generic;
using System.Text;

namespace WhiskeyRealism.Strategic
{
    // #25's reasons row field (foresight F1): why the defence intent ledger forbade the formations the candidate filter removes
    // (or, while the planner runs and the filter only observes, would remove). "reason:n,..." sorted by reason, so the signature that
    // dedupes the row does not change with the order the ids were met in.
    internal static class DefenseFilterTally
    {
        internal static string Reasons(IReadOnlyDictionary<int, string> reasonByUnitId, IEnumerable<int> ids)
        {
            if (reasonByUnitId == null || ids == null) return "-";
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (int id in ids)
            {
                if (!reasonByUnitId.TryGetValue(id, out string reason) || string.IsNullOrEmpty(reason)) continue;
                counts.TryGetValue(reason, out int n);
                counts[reason] = n + 1;
            }
            if (counts.Count == 0) return "-";
            var sb = new StringBuilder();
            foreach (var pair in counts)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(pair.Key).Append(':').Append(pair.Value);
            }
            return sb.ToString();
        }
    }
}
