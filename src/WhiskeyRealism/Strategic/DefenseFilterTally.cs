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
        // An order-independent 64-bit fingerprint of the same tally, for a dedupe that allocates nothing: the sum and the xor of an FNV-1a hash
        // of each counted id's reason. Two states with the same reasons and counts have the same fingerprint, whatever order the ids stand in;
        // the row is built (Reasons) only when it changed.
        internal static long Fingerprint(IReadOnlyDictionary<int, string> reasonByUnitId, IReadOnlyList<int> ids)
        {
            if (reasonByUnitId == null || ids == null) return 0L;
            long sum = 0L, xor = 0L;
            for (int i = 0; i < ids.Count; i++)
            {
                if (!reasonByUnitId.TryGetValue(ids[i], out string reason) || string.IsNullOrEmpty(reason)) continue;
                long h = Fnv(reason);
                unchecked { sum += h; }
                xor ^= h * 31L;
            }
            return unchecked(sum * 1000003L) ^ xor;
        }

        private static long Fnv(string text)
        {
            unchecked
            {
                ulong h = 14695981039346656037UL;
                for (int i = 0; i < text.Length; i++) h = (h ^ text[i]) * 1099511628211UL;
                return (long)h;
            }
        }

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
