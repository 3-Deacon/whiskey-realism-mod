using System;
using System.IO;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using static TestHarness;

// BC14 task 3: the BC13 extractions (one stated enemy-sample filter, one single-link clustering). Parity tests: an extraction must
// reproduce the code it replaces exactly.
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] ExtractionTests() => new (string, Action)[]
    {
        ("bc14 CLUSTER-PARITY the isolated-cluster openings are identical after ClusterPass calls SingleLinkClusters.Label", ClusterParity),
        ("bc14 CLUSTER-LABEL chains join, labels follow the lowest member, singles stand alone, nothing is allocated twice", ClusterLabel),
        ("bc14 SAMPLES-PARITY EnemySamples.Accepts agrees with each of BC13's three former inline filters on 200 observations", SamplesParity),
        ("bc14 SAMPLES-CALLERS the detector, the opportunity tick and the cavalry patch call EnemySamples.Accepts", SamplesCallers),
    };

    // Taken from the inline union-find ClusterPass carried before the extraction (50 random sets).
    const string ClusterParityGolden = "ef574132a775c336b2dfef36ae14de541cd42a3db6a02b4d92602e9a8ad6e59d|sets=50|clusterOpenings=156";

    static void ClusterParity()
    {
        string digest = CommanderOpportunityTests.IsolatedClusterDigest(50);
        AssertEqual(ClusterParityGolden, digest, "isolated-cluster digest over 50 random sample sets");
    }

    static void ClusterLabel()
    {
        var links = new SingleLinkClusters();
        // 0 and 1 are 100 apart, 1 and 2 are 100 apart: one chain although 0 and 2 are 200 apart. 3 stands alone, 4 and 5 are a pair.
        float[] xs = { 0f, 100f, 200f, 1000f, 2000f, 2100f };
        float[] zs = { 0f, 0f, 0f, 0f, 500f, 500f };
        var labels = new int[6];
        int n = links.Label(xs, zs, 6, 150f, labels);
        AssertEqual(3, n, "three clusters");
        AssertEqual(0, labels[0], "the chain is cluster 0");
        AssertEqual(0, labels[1], "the chain");
        AssertEqual(0, labels[2], "a chain joins points further apart than the link");
        AssertEqual(1, labels[3], "the single is cluster 1 (numbered by its lowest member)");
        AssertEqual(2, labels[4], "the pair is cluster 2");
        AssertEqual(2, labels[5], "the pair");
        AssertEqual(0, links.Label(xs, zs, 0, 150f, labels), "no points, no clusters");
        AssertEqual(1, links.Label(xs, zs, 1, 150f, labels), "one point, one cluster");
        AssertEqual(0, labels[0], "its label");
        // A link of exactly the distance joins (the test is <=), a hair under does not.
        AssertEqual(1, links.Label(new[] { 0f, 150f }, new[] { 0f, 0f }, 2, 150f, labels), "exactly the link joins");
        AssertEqual(2, links.Label(new[] { 0f, 150.5f }, new[] { 0f, 0f }, 2, 150f, labels), "just past the link does not");
        // Points in different cells still join across the cell border.
        AssertEqual(1, links.Label(new[] { 149f, 151f }, new[] { 10f, 10f }, 2, 150f, labels), "neighbouring cells join");
    }

    // The three filters as BC13 wrote them inline (OpeningDetector.BuildSamples, OpportunityTick.NearestEnemy and
    // CavalryStrikeDisciplinePatch.Supported before the extraction).
    static bool FormerOpeningDetectorFilter(in EnemyObservation o)
    {
        bool visible = o.State == ObservationState.Visible;
        if (!visible && !(o.State == ObservationState.Remembered && o.Confidence01 >= 0.5f)) return false;
        if (o.IsReinforcement || o.UnitTyp < 0 || o.UnitTyp > 2) return false;
        return true;
    }

    static bool FormerOpportunityTickFilter(in EnemyObservation o)
    {
        if (o.IsReinforcement || o.UnitTyp < 0 || o.UnitTyp > 2) return false;
        if (o.State != ObservationState.Visible && o.Confidence01 < 0.5f) return false;
        return true;
    }

    static bool FormerCavalryPatchFilter(in EnemyObservation o)
    {
        if (o.IsReinforcement) return false;
        if (o.UnitTyp != TacticalUnitType.Infantry && o.UnitTyp != TacticalUnitType.Cavalry) return false;
        if (o.State != ObservationState.Visible && o.Confidence01 < 0.5f) return false;
        return true;
    }

    static void SamplesParity()
    {
        uint state = 0x1234ABCDu;
        float Next() { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return (state & 0xFFFFFFu) / (float)0x1000000; }
        int accepted = 0, rejected = 0;
        for (int i = 0; i < 200; i++)
        {
            var o = new EnemyObservation
            {
                InstanceId = 1000 + i,
                UnitTyp = (int)(Next() * 7f) - 1,
                State = Next() < 0.5f ? ObservationState.Visible : ObservationState.Remembered,
                Confidence01 = i % 7 == 0 ? 0.5f : Next(),
                IsReinforcement = Next() < 0.15f,
            };
            bool accepts = EnemySamples.Accepts(in o);
            AssertEqual(FormerOpeningDetectorFilter(in o), accepts, "OpeningDetector's former filter, observation " + i);
            AssertEqual(FormerOpportunityTickFilter(in o), accepts, "OpportunityTick's former filter, observation " + i);
            bool cavalryEquivalent = accepts && (o.UnitTyp == TacticalUnitType.Infantry || o.UnitTyp == TacticalUnitType.Cavalry);
            AssertEqual(FormerCavalryPatchFilter(in o), cavalryEquivalent, "the cavalry patch's former filter, observation " + i);
            if (accepts) accepted++; else rejected++;
        }
        AssertTrue(accepted > 20 && rejected > 20, "the random set exercises both outcomes (" + accepted + "/" + rejected + ")");
        AssertEqual(0.5f, EnemySamples.RememberedMinConfidence, "the remembered floor");
    }

    static void SamplesCallers()
    {
        foreach (string rel in new[]
        {
            "src/WhiskeyRealism/Tactical/Battle/OpeningDetector.cs",
            "src/WhiskeyRealism/Tactical/Battle/OpportunityTick.cs",
            "src/WhiskeyRealism/Patches/CavalryStrikeDisciplinePatch.cs",
        })
        {
            string text = File.ReadAllText(TestFixtures.FindRepoPath(rel));
            AssertContains(text, "EnemySamples.Accepts(", rel + " calls the one sample filter");
        }
        string opportunity = File.ReadAllText(TestFixtures.FindRepoPath("src/WhiskeyRealism/Tactical/Battle/OpportunityTick.cs"));
        AssertFalse(opportunity.Contains("o.UnitTyp > 2"), "no inline unit-type test is left in the opportunity tick");
        string detector = File.ReadAllText(TestFixtures.FindRepoPath("src/WhiskeyRealism/Tactical/Battle/OpeningDetector.cs"));
        AssertContains(detector, "SingleLinkClusters", "ClusterPass labels through the shared clustering");
        AssertFalse(detector.Contains("private int Find(int i)"), "the inline union-find is gone from the detector");
    }
}
