using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.BugFixes
{
    // BUG-LOG-009. BattleUnits.GrabSubordinateByStrength (decompile 93549-93597) picks
    // subordinates "by strength" up to strengthtosearchfor: it tests
    // num + num5 <= strengthtosearchfor but then writes num = num5 (93594) instead of
    // accumulating, so the picked total can exceed the target (its one caller,
    // AICampaign.CheckTransferOfUnits 17295, then moves more than the deficit).
    internal static class SubordinateStrengthSelector
    {
        internal struct Candidate
        {
            // Present and of a type vanilla takes (unittyp 13 or <= 2, and in overridetype).
            internal bool Eligible;
            internal int Strength;
            // transfertime > 0 (already moving to another parent).
            internal bool InTransfer;
        }

        // Candidates are in vanilla's visiting order (allattachedunits from last to
        // first). maxCount is vanilla's num3 - leaveminimumbrigades. Returns the indices
        // of the picked candidates in visiting order.
        internal static List<int> Select(IReadOnlyList<Candidate> candidates, int target, int minimumAmount, int maxCount)
        {
            var picked = new List<int>();
            if (candidates == null) return picked;
            int total = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (!c.Eligible || c.InTransfer) continue;
                if (total + c.Strength > target || c.Strength < minimumAmount) continue;
                if (picked.Count >= maxCount) continue;
                total += c.Strength;
                picked.Add(i);
            }
            return picked;
        }
    }
}
