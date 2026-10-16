using System;

namespace WhiskeyRealism.Strategic.Planner
{
    // The theater a historical army belongs to (plan P12): the allocator's tie-break toward keeping
    // a formation in its home theater (ForceAllocator.HomeTheaterTieBreak). It maps a historical
    // army's name to the Theater of its doctrine's primary area, and is a prior only: a formation
    // with no home (Unknown) gets no tie-break, and a live margin never reads it. The Virginia
    // capital corridor and the Shenandoah Valley are East, the Ohio valley and north-west Virginia
    // are West (TheaterClassifier.FromPosition at a point inside each area); the rows are pinned in
    // ForceAllocatorTests.HomeTheaterPriorRows.
    public static class HomeTheaterPrior
    {
        private struct Rule
        {
            public int Alliance;
            public Theater Home;
            public string[] Names;
        }

        // Order matters: the first doctrine whose names match the (lowercased) unit name wins.
        private static readonly Rule[] Rules =
        {
            new Rule { Alliance = 0, Home = Theater.East, Names = new[] { "army of the potomac", "army of virginia" } },
            new Rule { Alliance = 0, Home = Theater.East, Names = new[] { "shenandoah" } },
            new Rule { Alliance = 0, Home = Theater.West, Names = new[] { "army of the ohio" } },
            new Rule { Alliance = 1, Home = Theater.East, Names = new[] { "army of northern virginia", "army of virginia", "army of the potomac", "army of the valley" } },
            new Rule { Alliance = 1, Home = Theater.West, Names = new[] { "army of the northwest", "porterfield" } }
        };

        private static readonly Func<string, Theater>[] PerAlliance =
        {
            name => For(0, name),
            name => For(1, name)
        };

        // Theater.Unknown when the name belongs to no historical doctrine of the alliance.
        public static Theater For(int alliance, string unitName)
        {
            if (string.IsNullOrEmpty(unitName)) return Theater.Unknown;
            string name = unitName.Trim().ToLowerInvariant();
            for (int r = 0; r < Rules.Length; r++)
            {
                if (Rules[r].Alliance != alliance) continue;
                for (int n = 0; n < Rules[r].Names.Length; n++)
                    if (name.Contains(Rules[r].Names[n])) return Rules[r].Home;
            }
            return Theater.Unknown;
        }

        // The allocator's delegate, one per alliance (no closure per review); Unknown for any other alliance.
        public static Func<string, Theater> ForAlliance(int alliance)
        {
            return alliance >= 0 && alliance < PerAlliance.Length ? PerAlliance[alliance] : (name => Theater.Unknown);
        }
    }
}
