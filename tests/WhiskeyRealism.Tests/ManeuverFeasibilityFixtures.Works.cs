using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using static BattleMovementHonestyTests;

// BC14W fixtures (no tests here): a works field in the spec's own frame (§ 6 scenarios 6-9), fake works points and the pure picture built
// over them. Lateral is the right of the army's heading and depth is forward toward the enemy; the map is the 2,400-unit square of FeasField
// (world x = lateral + 1,200, z = depth - 1,200), forward +z.
internal static class WorksFixtures
{
    public const int Size = 2400;
    public static float X(float lateral) => lateral + Size * 0.5f;
    public static float Z(float depth) => depth - Size * 0.5f;

    /// <summary>The axis of an army at lateral 0, depth 0 facing the enemy straight ahead (forward +z, right +x).</summary>
    public static TacticalFrontAxis Axis() => TacticalFrontAxis.FromCentroids(X(0f), Z(0f), X(0f), Z(600f));

    /// <summary>A snapshot with no own units and the given observations (the works picture reads the observations and, for a Defender, the own records).</summary>
    public static BattleSnapshot Snapshot(params EnemyObservation[] observations)
    {
        var tree = new ManeuverFeasibilityFixtures.TreeFixture();
        return tree.Build(10.0, observations);
    }

    private static int _id = 9000;

    /// <summary>One enemy observation standing in cover (earth parapet 7, value 0.40 by default): a works sample when Visible.</summary>
    public static EnemyObservation Sample(float lateral, float depth, float strength, float cover = 0.40f, int coverObject = 7, bool standing = true,
        ObservationState state = ObservationState.Visible, float confidence = 1f, float morale = 1f, int unitTyp = TacticalUnitType.Infantry)
        => new EnemyObservation
        {
            InstanceId = _id++, UnitTyp = unitTyp, X = X(lateral), Z = Z(depth), Strength = strength, State = state, Confidence01 = confidence,
            Heading = 180f, FireRange = 100f, Morale = morale, CoverValue = cover, CoverObject = coverObject, Standing = standing,
        };

    /// <summary>The works points of a fixture: a position, whether the side has perceived it and whether it lies in the side's own zone.</summary>
    public sealed class Points : IFortificationPoints
    {
        public readonly List<(float x, float z, bool perceived, bool ownZone)> Items = new List<(float, float, bool, bool)>();
        public int Count => Items.Count;
        public void Get(int i, out float x, out float z) { x = Items[i].x; z = Items[i].z; }
        public bool Perceived(int i) => Items[i].perceived;
        public bool InOwnZone(int i) => Items[i].ownZone;

        /// <summary>A piece every <paramref name="step"/> units of lateral from <paramref name="from"/> to <paramref name="to"/> at a depth that may depend on the lateral.</summary>
        public Points Line(float from, float to, float step, Func<float, float> depth, bool perceived = true, bool ownZone = false)
        {
            for (float l = from; l <= to + 0.001f; l += step) Items.Add((X(l), Z(depth(l)), perceived, ownZone));
            return this;
        }
    }

    /// <summary>The enemy's bodies of a set of observations: their single-link clusters at 150 units (the evaluation's own rule).</summary>
    public static void Bodies(BattleSnapshot s, out float[] bx, out float[] bz, out int count)
    {
        var x = new List<float>();
        var z = new List<float>();
        var w = new List<float>();
        for (int i = 0; i < s.ObservationCount; i++)
        {
            if (!EnemySamples.Accepts(in s.Observations[i])) continue;
            x.Add(s.Observations[i].X);
            z.Add(s.Observations[i].Z);
            w.Add(s.Observations[i].Strength);
        }
        var labels = new int[Math.Max(1, x.Count)];
        int clusters = new SingleLinkClusters().Label(x.ToArray(), z.ToArray(), x.Count, OpeningDetector.ClusterLinkMeters, labels);
        bx = new float[clusters];
        bz = new float[clusters];
        var bw = new float[clusters];
        for (int i = 0; i < x.Count; i++) { bx[labels[i]] += x[i] * w[i]; bz[labels[i]] += z[i] * w[i]; bw[labels[i]] += w[i]; }
        for (int k = 0; k < clusters; k++) { bx[k] /= bw[k]; bz[k] /= bw[k]; }
        count = clusters;
    }

    public static WorksPicture BuildEnemy(BattleSnapshot s, IFortificationPoints points, TerrainPicture picture = null, TacticalFrontAxis? axis = null)
    {
        Bodies(s, out float[] bx, out float[] bz, out int count);
        var picture2 = new WorksPicture();
        picture2.BuildEnemy(s, axis ?? Axis(), bx, bz, count, points, picture);
        return picture2;
    }

    /// <summary>A real terrain picture whose river runs down every column left of <paramref name="riverBelowX"/> and right of <paramref name="riverFromX"/> (world x).</summary>
    public static TerrainPicture RiverPicture(float riverBelowX, float riverFromX)
    {
        BattleTerrainGrid grid = GridOf(Size, Size, (x, zi) => x < riverBelowX || x >= riverFromX ? River : Grass);
        return TerrainPictureTests.Build(grid, Size, TerrainPictureTests.Flat());
    }
}
