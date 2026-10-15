using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // History as a weight (spec decision; plan P12). HistoricalOperationCatalog's six profiles
    // reduced to (alliance, objective ids, date window, priority); the per-phase machinery is not
    // carried. Copied here so G6 can delete the catalog; TheaterPlannerTests.PriorTableMatchesCatalog
    // pins every row against the catalog until then.
    //   Window: the first day of the start month to the last day (31) of the end month, as the
    //     catalog's InDateWindow compares months; serials are ReviewCadenceCatchUp.DaySerial.
    //   Priority: the catalog sorts ascending (CompareMatches: 10 before 15 before 20), so its
    //     value is stored as a tier 3 / 2 / 1 (10 / 15 / 20) and the weight is x tier / 3, never
    //     above 1, so the planner's prior term PriorLambda x Weight stays within
    //     PriorMaxContribution (plan P3).
    public static class HistoricalPriorTable
    {
        public struct Entry
        {
            public string Id;
            public int Alliance;
            public int[] ObjectiveIds;
            public int StartSerial, EndSerial;
            public int Priority; // tier 1..3, 3 = the catalog's most important (10)
        }

        public const int MaxTier = 3;
        public const float HalfLifeMonths = 3f;
        private const int DaysPerMonth = 31; // ReviewCadenceCatchUp.DaySerial's month width

        private static readonly Entry[] Table =
        {
            Row("union-east-pressure", 0, new[] { 3 }, 4, 1861, 12, 1861, 10),
            Row("csa-valley-disruption", 1, new[] { 32 }, 4, 1861, 12, 1861, 15),
            Row("union-coastal-pressure", 0, new[] { 37 }, 1, 1862, 12, 1862, 20),
            Row("union-western-pressure", 0, new[] { 29 }, 1, 1862, 12, 1862, 10),
            Row("csa-western-depth", 1, new[] { 30 }, 1, 1862, 12, 1862, 10),
            Row("union-late-pressure", 0, new[] { 3 }, 1, 1864, 12, 1865, 10)
        };

        public static IReadOnlyList<Entry> Entries => Table;

        // 1 inside the window, 0.5^(monthsOutside / 3) outside, x tier / 3.
        public static float Weight(in Entry e, int daySerial)
        {
            float window = 1f;
            int outside = daySerial < e.StartSerial ? e.StartSerial - daySerial
                : daySerial > e.EndSerial ? daySerial - e.EndSerial : 0;
            if (outside > 0) window = (float)Math.Pow(0.5, outside / (double)DaysPerMonth / HalfLifeMonths);
            float tier = Math.Max(0, Math.Min(MaxTier, e.Priority)) / (float)MaxTier;
            return window * tier;
        }

        // The strongest entry of the alliance naming the objective; 0 and id null when none does.
        public static float PriorFor(int alliance, int objectiveId, int daySerial, out string id)
        {
            id = null;
            float best = 0f;
            var entries = Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.Alliance != alliance || Array.IndexOf(e.ObjectiveIds, objectiveId) < 0) continue;
                float w = Weight(e, daySerial);
                if (w <= best) continue;
                best = w;
                id = e.Id;
            }
            return best;
        }

        // Catalog priority (ascending importance) -> tier.
        internal static int TierOf(int catalogPriority)
        {
            if (catalogPriority <= 10) return 3;
            return catalogPriority <= 15 ? 2 : 1;
        }

        private static Entry Row(string id, int alliance, int[] objectives, int startMonth, int startYear, int endMonth, int endYear, int catalogPriority)
        {
            return new Entry
            {
                Id = id,
                Alliance = alliance,
                ObjectiveIds = objectives,
                StartSerial = ReviewCadenceCatchUp.DaySerial(1, startMonth, startYear),
                EndSerial = ReviewCadenceCatchUp.DaySerial(DaysPerMonth, endMonth, endYear),
                Priority = TierOf(catalogPriority)
            };
        }
    }
}
