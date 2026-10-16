using System;
using System.Collections.Generic;
using System.IO;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// BC13 task 3: the opening detector (spec § 5). World frame of the fixtures: the front axis has its origin at (0, 0) and
// forward +z, so lateral = x and depth = z; the enemy stands at positive depth, the own line at 0 and behind.
internal static partial class CommanderOpportunityTests
{
    private static (string, Action)[] DetectionTests()
    {
        return new (string, Action)[]
        {
            ("bc13 DET-GAP a 300 m perceived gap is emitted with aim and width", DetGap),
            ("bc13 DET-GAP-SECOND-LINE a unit behind the gap closes it", DetGapSecondLine),
            ("bc13 DET-GAP-NARROW 140 m is not a gap", DetGapNarrow),
            ("bc13 DET-HIDDEN a hidden enemy never closes or opens anything", DetHidden),
            ("bc13 DET-REMEMBERED-LOW confidence 0.4 is ignored", DetRememberedLow),
            ("bc13 DET-SHAKEN 0.4 share at morale 0.44 emits and 0.46 does not", DetShaken),
            ("bc13 DET-SHAKEN-REMEMBERED remembered morale is not counted as shaken", DetShakenRemembered),
            ("bc13 DET-FLANK-RIVER a river 70 m beyond the end anchors and 90 m does not", DetFlankRiver),
            ("bc13 DET-FLANK-CREST a crest within 50 m anchors", DetFlankCrest),
            ("bc13 DET-FLANK-REFUSED a refused end of 45 degrees anchors", DetFlankRefused),
            ("bc13 DET-FLANK-SCREEN cavalry beyond the end anchors", DetFlankScreen),
            ("bc13 DET-BATTERY unsupported emits, infantry at 240 m blocks it, a garrisoned battery emits nothing", DetBattery),
            ("bc13 DET-CLUSTER vanilla's 0.65 and 1.3 rules over any qualifying cluster", DetCluster),
            ("bc13 DET-IDENTITY match keeps the id, confirm and lost times, overflow", DetIdentity),
            ("bc13 DET-IDENTITY clusters past 64 drop the farthest from the own line", DetIdentityClusters),
            ("bc13 DET-GEOM the five-unit fixture", DetGeom),
            ("bc13 DET-OWNGAP the own line gap without the excluded subtrees", DetOwnGap),
            ("bc13 DET-TERRAIN river anchor, crest anchor and the recovery path", DetTerrain),
            ("bc13 DET-PIN EnemyRecords is read only through a visible observation's row", DetPin),
        };
    }

    // ------------------------------------------------------------------ fixtures

    private sealed class AllPerceived : IPerceivedGround
    {
        public bool IsPerceived(float x, float z) => true;
    }

    private sealed class FnGround : IPerceivedGround
    {
        private readonly Func<float, float, bool> _fn;
        public int Calls;
        public FnGround(Func<float, float, bool> fn) { _fn = fn; }
        public bool IsPerceived(float x, float z) { Calls++; return _fn(x, z); }
    }

    // A river along a vertical world line x == RiverX, a crest at a point, and a river segment test.
    private sealed class FnTerrain : IOpeningTerrain
    {
        public float? RiverX;
        public float? CrestX, CrestZ;
        public bool SegmentRiver;
        public bool RiverWithin(float x, float z, float dirX, float dirZ, float meters)
        {
            if (!RiverX.HasValue || Math.Abs(dirX) < 1e-6f) return false;
            float t = (RiverX.Value - x) / dirX;
            return t >= 0f && t <= meters;
        }
        public bool CrestWithin(float x, float z, float meters)
        {
            if (!CrestX.HasValue) return false;
            float dx = CrestX.Value - x, dz = CrestZ.Value - z;
            return dx * dx + dz * dz <= meters * meters;
        }
        public bool SegmentCrossesRiver(float x0, float z0, float x1, float z1) => SegmentRiver;
    }

    private sealed class DetFixture
    {
        public readonly List<UnitRecord> Own = new List<UnitRecord>();
        public readonly List<int> Parents = new List<int>();
        public readonly List<EnemyObservation> Obs = new List<EnemyObservation>();
        public readonly List<UnitRecord> Rows = new List<UnitRecord>();
        private int _id = 1000;

        public int Inf(float lateral, float depth, float width = 100f, float morale = 0.8f, float strength = 500f, float heading = 0f,
            bool routed = false, bool melee = false)
            => Enemy(TacticalUnitType.Infantry, lateral, depth, width, morale, strength, heading, routed, melee, false);

        public int Cav(float lateral, float depth, float morale = 0.8f, float strength = 300f)
            => Enemy(TacticalUnitType.Cavalry, lateral, depth, 60f, morale, strength, 0f, false, false, false);

        public int Art(float lateral, float depth, bool garrisoned = false, float strength = 200f)
            => Enemy(TacticalUnitType.Artillery, lateral, depth, 40f, 0.8f, strength, 0f, false, false, garrisoned);

        private int Enemy(int type, float lateral, float depth, float width, float morale, float strength, float heading, bool routed, bool melee, bool garrisoned)
        {
            int id = _id++;
            Obs.Add(new EnemyObservation
            {
                InstanceId = id, UnitTyp = type, X = lateral, Z = depth, Strength = strength, State = ObservationState.Visible,
                Confidence01 = 1f, Morale = morale, Heading = heading,
            });
            UnitFlags flags = UnitFlags.Alive | UnitFlags.Active | (routed ? UnitFlags.Routed : 0) | (melee ? UnitFlags.InMelee : 0) | (garrisoned ? UnitFlags.Garrisoned : 0);
            Rows.Add(new UnitRecord { InstanceId = id, UnitTyp = type, X = lateral, Z = depth, UnitWidth = width, Morale = morale, Flags = flags, ParentIndex = -1 });
            return id;
        }

        // A remembered enemy: the observation only, no row (an enemy the side does not see now).
        public int Remembered(int type, float lateral, float depth, float confidence, float morale = 0.8f, float strength = 500f, float heading = 0f)
        {
            int id = _id++;
            Obs.Add(new EnemyObservation
            {
                InstanceId = id, UnitTyp = type, X = lateral, Z = depth, Strength = strength, State = ObservationState.Remembered,
                Confidence01 = confidence, Morale = morale, Heading = heading,
            });
            return id;
        }

        // An enemy that is in the true state (a row) but not an observation: hidden from the side.
        public void Hidden(float lateral, float depth, float width = 100f)
        {
            Rows.Add(new UnitRecord { InstanceId = _id++, UnitTyp = TacticalUnitType.Infantry, X = lateral, Z = depth, UnitWidth = width, Morale = 0.8f, Flags = UnitFlags.Alive | UnitFlags.Active, ParentIndex = -1 });
        }

        public int OwnInf(float lateral, float depth, float width = 100f, int parent = -1)
        {
            Own.Add(new UnitRecord
            {
                InstanceId = 1 + Own.Count, UnitTyp = TacticalUnitType.Infantry, X = lateral, Z = depth, UnitWidth = width,
                Strength = 500, Morale = 0.9f, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry,
            });
            Parents.Add(parent);
            return Own.Count - 1;
        }

        public int OwnGroup(int parent = -1)
        {
            Own.Add(new UnitRecord
            {
                InstanceId = 1 + Own.Count, UnitTyp = TacticalUnitType.BattleGroupBrigade, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup,
            });
            Parents.Add(parent);
            return Own.Count - 1;
        }

        public BattleSnapshot Build(double hours = 10.0)
        {
            var s = new BattleSnapshot();
            s.BeginBuild(7, 0, 0, 1, hours);
            for (int i = 0; i < Own.Count; i++)
            {
                UnitRecord r = Own[i];
                s.AddOwn(in r);
                s.Own[i].ParentIndex = Parents[i];
            }
            s.IndexCommandTree(null);
            s.SetFrontAxis(TacticalFrontAxis.FromCentroids(0f, 0f, 0f, 1000f).WithOwnExtent(-500f, 500f, 5));
            for (int i = 0; i < Rows.Count; i++)
            {
                UnitRecord r = Rows[i];
                s.AddEnemy(in r);
            }
            for (int i = 0; i < Obs.Count; i++)
            {
                EnemyObservation o = Obs[i];
                s.AddObservation(in o);
            }
            return s;
        }
    }

    private static OpeningRules Rules => OpeningRules.Shipped;

    private static int Detect(OpeningDetector d, DetFixture fx, IPerceivedGround ground = null, IOpeningTerrain terrain = null,
        double now = 10.0, bool defender = false, OpeningRules? rules = null)
    {
        return d.Detect(fx.Build(now), ground ?? new AllPerceived(), terrain ?? new FnTerrain(), rules ?? Rules, defender, now);
    }

    private static int CountKind(OpeningDetector d, OpeningKind kind)
    {
        int n = 0;
        foreach (Opening o in d.Openings.ToArray()) if (o.Kind == kind) n++;
        return n;
    }

    private static Opening First(OpeningDetector d, OpeningKind kind)
    {
        foreach (Opening o in d.Openings.ToArray()) if (o.Kind == kind) return o;
        throw new Exception("no " + kind + " opening");
    }

    // A line of infantry at depth 600: lateral 0 and 400 (width 100, so the edges are 50 and 350: a 300 m gap).
    private static DetFixture GapLine(float secondLateral = 400f)
    {
        var fx = new DetFixture();
        fx.Inf(0f, 600f);
        fx.Inf(secondLateral, 600f);
        return fx;
    }

    // ------------------------------------------------------------------ tests

    private static void DetGap()
    {
        var d = new OpeningDetector();
        Detect(d, GapLine());
        AssertEqual(1, CountKind(d, OpeningKind.Gap), "one gap");
        Opening gap = First(d, OpeningKind.Gap);
        AssertNear(300f, gap.Width, 1e-3f, "width = edge to edge");
        AssertNear(200f, gap.AimX, 1e-3f, "aim lateral = the gap centre");
        AssertNear(700f, gap.AimZ, 1e-3f, "aim depth = the deeper edge + 100");
        AssertEqual(-1, gap.TargetObservation, "a gap has no target");
        AssertNear(0f, gap.Heading, 1e-3f, "a gap's heading is the axis forward bearing (+z = 0)");
        AssertNear(1f, gap.Confidence01, 1e-4f, "two visible units: confidence 1");
    }

    private static void DetGapSecondLine()
    {
        var fx = GapLine();
        fx.Inf(200f, 750f);   // a second line 150 m behind the gap, inside the corridor
        var d = new OpeningDetector();
        Detect(d, fx);
        AssertEqual(0, CountKind(d, OpeningKind.Gap), "a unit behind the gap closes it");
    }

    private static void DetGapNarrow()
    {
        var d = new OpeningDetector();
        Detect(d, GapLine(240f));   // edges 50 and 190: 140 m
        AssertEqual(0, CountKind(d, OpeningKind.Gap), "140 m is not a gap");
        Detect(d, GapLine(250f), now: 10.1);   // edges 50 and 200: exactly 150 m
        AssertEqual(1, CountKind(d, OpeningKind.Gap), "150 m is");
    }

    private static void DetHidden()
    {
        var fx = GapLine();
        fx.Hidden(200f, 650f);   // a true unit in the gap that the side does not see
        var d = new OpeningDetector();
        Detect(d, fx);
        AssertEqual(1, CountKind(d, OpeningKind.Gap), "a hidden unit never closes a gap");
        var fx2 = new DetFixture();
        fx2.Hidden(0f, 600f);
        fx2.Hidden(400f, 600f);
        var d2 = new OpeningDetector();
        Detect(d2, fx2);
        AssertEqual(0, d2.Count, "hidden units open nothing");
    }

    private static void DetRememberedLow()
    {
        var fx = GapLine();
        fx.Remembered(TacticalUnitType.Infantry, 200f, 650f, confidence: 0.4f);
        var d = new OpeningDetector();
        Detect(d, fx);
        AssertEqual(1, CountKind(d, OpeningKind.Gap), "a remembered sample at 0.4 is ignored");
        var fx2 = GapLine();
        fx2.Remembered(TacticalUnitType.Infantry, 200f, 650f, confidence: 0.5f);
        var d2 = new OpeningDetector();
        Detect(d2, fx2);
        AssertEqual(0, CountKind(d2, OpeningKind.Gap), "a remembered sample at 0.5 closes the gap");
    }

    // Three infantry in one sector at depth 600 and morale m; their strength share is 1.0 or 0.4.
    private static DetFixture ShakenFixture(float shakenMorale, float shakenStrength)
    {
        var fx = new DetFixture();
        fx.Inf(-60f, 600f, morale: shakenMorale, strength: shakenStrength);
        fx.Inf(60f, 600f, morale: 0.8f, strength: 1000f - shakenStrength);
        return fx;
    }

    private static void DetShaken()
    {
        var d = new OpeningDetector();
        Detect(d, ShakenFixture(0.44f, 400f));
        AssertEqual(1, CountKind(d, OpeningKind.ShakenSector), "0.4 share at morale 0.44 emits");
        Opening o = First(d, OpeningKind.ShakenSector);
        AssertNear(-60f, o.AimX, 1e-3f, "aim = the shaken unit");
        AssertEqual(1000, o.TargetObservation, "target = the shaken sample");
        var d2 = new OpeningDetector();
        Detect(d2, ShakenFixture(0.46f, 400f));
        AssertEqual(0, CountKind(d2, OpeningKind.ShakenSector), "morale 0.46 is above the bar");
        var d3 = new OpeningDetector();
        Detect(d3, ShakenFixture(0.44f, 390f));
        AssertEqual(0, CountKind(d3, OpeningKind.ShakenSector), "a share of 0.39 is below the minimum");
        // routed units alone far behind the line are a pursuit, not an opening
        var fx = new DetFixture();
        fx.Inf(0f, 900f, routed: true, morale: 0.1f);
        fx.Inf(50f, 900f, routed: true, morale: 0.1f);   // the same sector (a band is 200 m wide)
        var d4 = new OpeningDetector();
        Detect(d4, fx);
        AssertEqual(0, CountKind(d4, OpeningKind.ShakenSector), "a routed-only crowd with no formed enemy near is no opening");
        fx.Inf(0f, 700f, morale: 0.9f, strength: 100f);   // a formed unit within 300 m of the routed crowd
        var d5 = new OpeningDetector();
        Detect(d5, fx);
        AssertEqual(1, CountKind(d5, OpeningKind.ShakenSector), "a routed crowd near a formed enemy is");
    }

    private static void DetShakenRemembered()
    {
        var fx = new DetFixture();
        fx.Inf(60f, 600f, morale: 0.8f, strength: 600f);
        fx.Remembered(TacticalUnitType.Infantry, -60f, 600f, confidence: 0.9f, morale: 0.1f, strength: 400f);
        var d = new OpeningDetector();
        Detect(d, fx);
        AssertEqual(0, CountKind(d, OpeningKind.ShakenSector), "a remembered unit's stale low morale is never shaken");
    }

    // A line of two units at depth 600 (lateral 0 and 100, width 100) and a terrain; returns the left-end flank opening if any.
    private static int FlankCount(IOpeningTerrain terrain, Action<DetFixture> extra = null, IPerceivedGround ground = null)
    {
        var fx = new DetFixture();
        fx.Inf(0f, 600f);
        fx.Inf(100f, 600f);
        extra?.Invoke(fx);
        var d = new OpeningDetector();
        Detect(d, fx, ground, terrain);
        return CountKind(d, OpeningKind.FlankInAir);
    }

    private static void DetFlankRiver()
    {
        // The left end's outer edge is at lateral -50; the right end's at 150.
        AssertEqual(2, FlankCount(new FnTerrain()), "no anchor: both ends are in the air");
        AssertEqual(1, FlankCount(new FnTerrain { RiverX = -50f - 70f }), "a river 70 m beyond the left end anchors it");
        AssertEqual(2, FlankCount(new FnTerrain { RiverX = -50f - 90f }), "a river 90 m beyond does not");
        AssertEqual(2, FlankCount(new FnTerrain { RiverX = 150f - 70f }), "a river inside the line is not beyond the right end");
    }

    private static void DetFlankCrest()
    {
        AssertEqual(1, FlankCount(new FnTerrain { CrestX = -50f - 40f, CrestZ = 600f }), "a crest within 50 m of the left end anchors it");
        AssertEqual(2, FlankCount(new FnTerrain { CrestX = -50f - 60f, CrestZ = 600f }), "a crest at 60 m does not");
        var notReady = OpeningRules.Shipped;
        notReady.PictureReady = false;
        var fx = new DetFixture();
        fx.Inf(0f, 600f);
        fx.Inf(100f, 600f);
        var d = new OpeningDetector();
        Detect(d, fx, null, new FnTerrain { CrestX = -90f, CrestZ = 600f }, rules: notReady);
        AssertEqual(2, CountKind(d, OpeningKind.FlankInAir), "with the picture not ready the crest test is skipped");
    }

    private static void DetFlankRefused()
    {
        // Three units; the left end's heading is turned 45 degrees from the line mean (0).
        Func<float, int> count = endHeading =>
        {
            var fx = new DetFixture();
            fx.Inf(0f, 600f, heading: endHeading);
            fx.Inf(100f, 600f, heading: 0f);
            fx.Inf(200f, 600f, heading: 0f);
            var d = new OpeningDetector();
            Detect(d, fx);
            int n = 0;
            foreach (Opening o in d.Openings.ToArray()) if (o.Kind == OpeningKind.FlankInAir && o.AimX < 0f) n++;
            return n;
        };
        AssertEqual(1, count(0f), "an aligned left end is in the air");
        AssertEqual(1, count(44f), "44 degrees is not refused");
        AssertEqual(0, count(45f), "45 degrees anchors the end (refused)");
        AssertEqual(0, count(315f), "-45 degrees anchors it too");
    }

    private static void DetFlankScreen()
    {
        AssertEqual(1, FlankCount(new FnTerrain(), fx => fx.Cav(-200f, 640f)), "cavalry 150 m beyond the left end anchors it");
        AssertEqual(2, FlankCount(new FnTerrain(), fx => fx.Cav(-450f, 640f)), "cavalry 400 m beyond is no screen");
        AssertEqual(2, FlankCount(new FnTerrain(), fx => fx.Cav(-200f, 1000f)), "cavalry 400 m deeper is no screen");
    }

    private static void DetBattery()
    {
        var fx = new DetFixture();
        fx.Art(0f, 700f);
        var d = new OpeningDetector();
        Detect(d, fx);
        AssertEqual(1, CountKind(d, OpeningKind.UnsupportedBattery), "an unsupported battery emits");
        AssertEqual(1000, First(d, OpeningKind.UnsupportedBattery).TargetObservation, "target = the battery");
        var fx2 = new DetFixture();
        fx2.Art(0f, 700f);
        fx2.Inf(240f, 700f);
        var d2 = new OpeningDetector();
        Detect(d2, fx2);
        AssertEqual(0, CountKind(d2, OpeningKind.UnsupportedBattery), "infantry at 240 m blocks it");
        var fx3 = new DetFixture();
        fx3.Art(0f, 700f);
        fx3.Inf(260f, 700f);
        var d3 = new OpeningDetector();
        Detect(d3, fx3);
        AssertEqual(1, CountKind(d3, OpeningKind.UnsupportedBattery), "infantry at 260 m does not");
        var fx4 = new DetFixture();
        fx4.Art(0f, 700f, garrisoned: true);
        var d4 = new OpeningDetector();
        Detect(d4, fx4);
        AssertEqual(0, CountKind(d4, OpeningKind.UnsupportedBattery), "a garrisoned battery emits nothing");
    }

    private static void DetCluster()
    {
        // Two clusters 600 m apart: the western pair is weak, the eastern group strong.
        var fx = new DetFixture();
        fx.Inf(0f, 600f, strength: 100f);
        fx.Inf(100f, 600f, strength: 100f);
        fx.Inf(700f, 600f, strength: 800f);
        fx.Inf(800f, 600f, strength: 800f);
        var d = new OpeningDetector();
        Detect(d, fx);
        AssertEqual(2, CountKind(d, OpeningKind.IsolatedCluster), "both clusters are isolated (600 m apart)");
        Opening west = default;
        foreach (Opening o in d.Openings.ToArray()) if (o.Kind == OpeningKind.IsolatedCluster && o.AimX < 400f) west = o;
        AssertNear(50f, west.AimX, 1e-3f, "centroid x");
        AssertNear(600f, west.AimZ, 1e-3f, "centroid z");
        AssertNear(2f * 100f * 0.8f, west.ClusterValue, 1e-3f, "value = sum strength x morale");
        AssertNear(700f, west.ClusterNearestMeters, 1e-3f, "nearest other cluster: centroid to centroid");
        AssertEqual(1000, west.TargetObservation, "target = the strongest member");
        AssertNear(2f * 100f * 0.8f, west.ClusterValue, 1e-3f, "the opening carries the cluster value");
        AssertNear(700f, west.ClusterNearestMeters, 1e-3f, "and the nearest neighbour");
        AssertFalse(west.Shaken, "steady infantry is not a shaken cluster");
        var broken = new DetFixture();
        broken.Inf(0f, 600f, strength: 100f, morale: 0.2f);
        broken.Inf(100f, 600f, strength: 100f, morale: 0.3f);
        broken.Inf(700f, 600f, strength: 800f);
        var db = new OpeningDetector();
        Detect(db, broken);
        Opening shakenWest = default;
        foreach (Opening o in db.Openings.ToArray()) if (o.Kind == OpeningKind.IsolatedCluster && o.AimX < 400f) shakenWest = o;
        AssertTrue(shakenWest.Id > 0 && shakenWest.Shaken, "a cluster whose mean visible morale is under the bar is shaken");

        // vanilla's rule for an asset of strength x morale 400 at 400 m: 160 <= 0.65 x 400 = 260 and 700 > 1.3 x 400 = 520.
        AssertTrue(OpeningDetector.ClusterQualifies(160f, 700f, 400f, 400f), "the weak, distant cluster qualifies");
        AssertFalse(OpeningDetector.ClusterQualifies(270f, 700f, 400f, 400f), "value above 0.65 of the asset");
        AssertTrue(OpeningDetector.ClusterQualifies(260f, 700f, 400f, 400f), "value at exactly 0.65 qualifies");
        AssertFalse(OpeningDetector.ClusterQualifies(160f, 520f, 400f, 400f), "neighbour at exactly 1.3 x the distance does not");
        AssertFalse(OpeningDetector.ClusterQualifies(160f, 700f, 400f, 200f), "an asset within 250 m of the cluster");
        // Any qualifying cluster, not only the nearest: an asset nearer the strong cluster still qualifies against the weak one.
        AssertTrue(OpeningDetector.ClusterQualifies(160f, 700f, 400f, 500f), "a farther qualifying cluster is chosen too");

        // A neighbour nearer than 325 m can qualify for no asset: no opening.
        var near = new DetFixture();
        near.Inf(0f, 600f);
        near.Inf(300f, 600f);   // 300 m apart: 150 m link needs a chain, so two clusters 300 m apart
        var dn = new OpeningDetector();
        Detect(dn, near);
        AssertEqual(0, CountKind(dn, OpeningKind.IsolatedCluster), "clusters 300 m apart (< 325) are not isolated");
        // A single cluster is the whole army, not an isolated one.
        var one = new DetFixture();
        one.Inf(0f, 600f);
        one.Inf(100f, 600f);
        var d1 = new OpeningDetector();
        Detect(d1, one);
        AssertEqual(0, CountKind(d1, OpeningKind.IsolatedCluster), "one cluster is no isolated cluster");
    }

    private static void DetIdentity()
    {
        var d = new OpeningDetector();
        Detect(d, GapLine(), now: 10.0);
        Opening a = First(d, OpeningKind.Gap);
        AssertFalse(a.Confirmed, "not confirmed on first sight");
        // the next tick, 0.015 h later and 40 m of aim shift: matched
        var shifted = new DetFixture();
        shifted.Inf(30f, 600f);
        shifted.Inf(430f, 600f);
        Detect(d, shifted, now: 10.015);
        Opening b = First(d, OpeningKind.Gap);
        AssertEqual(a.Id, b.Id, "the same id inside 100 m");
        AssertEqual(10.0, b.FirstSeenHours, "FirstSeen kept");
        AssertFalse(b.Confirmed, "0.015 h is below the confirm time");
        Detect(d, shifted, now: 10.025);
        AssertTrue(First(d, OpeningKind.Gap).Confirmed, "confirmed after 0.02 h");
        // not seen: kept for LostHours, then dropped
        var none = new DetFixture();
        Detect(d, none, now: 10.065);
        AssertEqual(1, CountKind(d, OpeningKind.Gap), "held 0.04 h after the last sight");
        Detect(d, none, now: 10.08);
        AssertEqual(0, CountKind(d, OpeningKind.Gap), "lost 0.05 h after the last sight");
        // a new opening gets a new id
        Detect(d, GapLine(), now: 10.1);
        AssertTrue(First(d, OpeningKind.Gap).Id != a.Id, "ids are not reused");
        // beyond 100 m is a different opening
        var far = new DetFixture();
        far.Inf(200f, 600f);
        far.Inf(600f, 600f);
        Detect(d, far, now: 10.11);
        AssertEqual(2, CountKind(d, OpeningKind.Gap), "an aim 200 m away does not match; the old one is held until lost");

        // Overflow. A 200 m gap whose right unit is only remembered (confidence 0.6) and 31 lone batteries (confidence 1.0):
        // 32 openings, nothing else (clusters 300 m or 200 m apart are not isolated; both flank ends look onto unperceived ground).
        Func<DetFixture> full = () =>
        {
            var f = new DetFixture();
            f.Inf(0f, 600f);
            f.Remembered(TacticalUnitType.Infantry, 300f, 600f, confidence: 0.6f);
            for (int i = 0; i < 31; i++) f.Art(2000f + i * 200f, 700f);
            return f;
        };
        var blind = new FnGround((x, z) => !((x > -400f && x < -10f) || (x > 360f && x < 700f)));
        var many = new OpeningDetector();
        Settle(many, full(), blind, now: 20.0, maxTicks: 80);
        AssertEqual(32, many.Count, "32 held");
        int gapId = First(many, OpeningKind.Gap).Id;
        AssertNear(0.6f, First(many, OpeningKind.Gap).Confidence01, 1e-4f, "the gap's confidence is the product of its samples'");

        // the 33rd opening drops the lowest-confidence unconfirmed one
        var thirtyThird = full();
        thirtyThird.Art(2000f + 31 * 200f, 700f);
        Settle(many, thirtyThird, blind, now: 20.001, maxTicks: 80);
        AssertEqual(32, many.Count, "capped at MaxOpenings");
        AssertEqual(0, CountKind(many, OpeningKind.Gap), "the lowest-confidence unconfirmed opening (the 0.6 gap) was dropped");
        AssertEqual(32, CountKind(many, OpeningKind.UnsupportedBattery), "the new battery took its place");

        // a claimed opening is never dropped: the new one is refused instead
        var guarded = new OpeningDetector();
        Settle(guarded, full(), blind, now: 20.0, maxTicks: 80);
        guarded.SetClaimed(First(guarded, OpeningKind.Gap).Id);
        Settle(guarded, thirtyThird, blind, now: 20.001, maxTicks: 80);
        AssertEqual(32, guarded.Count, "still capped");
        AssertEqual(1, CountKind(guarded, OpeningKind.Gap), "the claimed gap stays");
        AssertEqual(31, CountKind(guarded, OpeningKind.UnsupportedBattery), "the 33rd opening was refused (a tie goes to the oldest)");
        AssertTrue(gapId > 0, "ids are positive");
    }

    private static void DetIdentityClusters()
    {
        // 66 isolated singletons 400 m apart along the lateral; the two farthest from the own line (the origin) are dropped.
        var fx = new DetFixture();
        for (int i = 0; i < 66; i++) fx.Inf(-13000f + i * 400f, 600f + (i % 2) * 5f, width: 40f, strength: 100f);
        var d = new OpeningDetector();
        Detect(d, fx);
        int clusters = CountKind(d, OpeningKind.IsolatedCluster);
        AssertEqual(OpeningDetector.MaxOpenings, d.Count, "capped at the opening table size");
        AssertTrue(clusters <= OpeningDetector.MaxOpenings, "clusters bounded by the table");
        float farthest = 0f;
        foreach (Opening o in d.Openings.ToArray()) farthest = Math.Max(farthest, Math.Abs(o.AimX));
        AssertTrue(farthest < 13000f - 1f, "the farthest cluster from the own line was dropped");
    }

    // The plan's fixture: five visible infantry at lateral 0-800 every 200, width 150, depth 600; a visible battery at
    // lateral 1100, depth 700; no river; picture not ready; every probe point perceived except those beyond lateral 1000.
    private static void DetGeom()
    {
        var fx = new DetFixture();
        for (int i = 0; i < 5; i++) fx.Inf(i * 200f, 600f, width: 150f);
        fx.Art(1100f, 700f);
        var rules = OpeningRules.Shipped;
        rules.PictureReady = false;
        var d = new OpeningDetector();
        Detect(d, fx, new FnGround((x, z) => x <= 1000f), new FnTerrain(), rules: rules);
        AssertEqual(1, CountKind(d, OpeningKind.FlankInAir), "one flank in the air");
        Opening flank = First(d, OpeningKind.FlankInAir);
        AssertNear(-75f - 150f, flank.AimX, 1e-3f, "left end: aim at lateral -225 (edge -75, 150 m beyond)");
        AssertNear(600f, flank.AimZ, 1e-3f, "at the end's depth");
        AssertEqual(0, CountKind(d, OpeningKind.UnsupportedBattery), "the battery's ring lies beyond lateral 1000: unperceived");
        AssertTrue(d.SuppressedUnperceived >= 1, "the battery was counted as suppressed");
        // The right end (edge 875) is anchored by the battery screen (225 m beyond, 100 m deeper) before any probe is asked.
        // Edge distances against centre distances: a screen 310 m beyond the right edge is not a screen.
        var fx2 = new DetFixture();
        for (int i = 0; i < 5; i++) fx2.Inf(i * 200f, 600f, width: 150f);
        fx2.Cav(875f + 310f, 600f);
        var d2 = new OpeningDetector();
        Detect(d2, fx2, null, new FnTerrain(), rules: rules);
        AssertEqual(2, CountKind(d2, OpeningKind.FlankInAir), "310 m beyond the edge is no screen: both ends are open");
    }

    private static void DetOwnGap()
    {
        var fx = new DetFixture();
        int brigade = fx.OwnGroup();
        fx.OwnInf(0f, 0f, 120f, brigade);
        int asset = fx.OwnGroup(brigade);
        fx.OwnInf(250f, 0f, 120f, asset);
        fx.OwnInf(500f, 0f, 120f, brigade);
        fx.Inf(250f, 300f);   // a perceived enemy facing the gap
        BattleSnapshot s = fx.Build();
        AssertNear(0f, OpeningDetector.OwnGapAt(s, ReadOnlySpan<int>.Empty, 250f), 1e-3f, "with the asset in the line there is no gap");
        float gap = OpeningDetector.OwnGapAt(s, new[] { asset }, 250f);
        AssertNear(380f, gap, 1e-3f, "without the asset's subtree the line has a 380 m edge gap at its lateral");
        AssertNear(0f, OpeningDetector.OwnGapAt(s, new[] { asset }, 700f), 1e-3f, "beyond the line's ends there is no gap");
        // no perceived enemy within 500 m: the gap does not matter
        var quiet = new DetFixture();
        int qb = quiet.OwnGroup();
        quiet.OwnInf(0f, 0f, 120f, qb);
        int qa = quiet.OwnGroup(qb);
        quiet.OwnInf(250f, 0f, 120f, qa);
        quiet.OwnInf(500f, 0f, 120f, qb);
        quiet.Inf(250f, 900f);
        AssertNear(0f, OpeningDetector.OwnGapAt(quiet.Build(), new[] { qa }, 250f), 1e-3f, "an enemy 900 m away does not make the gap matter");
    }

    private static void DetPin()
    {
        string src = File.ReadAllText(TestFixtures.FindRepoPath("src/WhiskeyRealism/Tactical/Battle/OpeningDetector.cs"));
        int begin = src.IndexOf("// BEGIN-VISIBLE-ROWS", StringComparison.Ordinal);
        int end = src.IndexOf("// END-VISIBLE-ROWS", StringComparison.Ordinal);
        AssertTrue(begin > 0 && end > begin, "the visible-row region is marked");
        int at = 0, outside = 0;
        while ((at = src.IndexOf("EnemyRecord", at, StringComparison.Ordinal)) >= 0)
        {
            if (at < begin || at > end)
            {
                // a comment line naming the pin is allowed; code is not
                int lineStart = src.LastIndexOf('\n', at) + 1;
                string line = src.Substring(lineStart, src.IndexOf('\n', at) - lineStart).TrimStart();
                if (!line.StartsWith("//", StringComparison.Ordinal)) outside++;
            }
            at += 11;
        }
        AssertEqual(0, outside, "EnemyRecords is indexed only inside the visible-row region");
    }

    // A 1000 x 1000 map of grass (id 0) with a river band x in [500, 510), a ford (id 5) across it for z in [-600, -580)
    // and a bridge (6) for z in [-400, -390).
    private static BattleTerrainGrid RiverGrid(bool frozen)
    {
        var ids = new int[1001, 1001];
        for (int x = 500; x < 510; x++)
        {
            for (int zi = 0; zi < 1001; zi++) ids[x, zi] = BattleTerrainGrid.RiverId;
            for (int zi = 400; zi < 420; zi++) ids[x, zi] = BattleTerrainGrid.FordId;     // z = -600 .. -580
            for (int zi = 600; zi < 610; zi++) ids[x, zi] = BattleTerrainGrid.BridgeId;   // z = -400 .. -390
        }
        return new BattleTerrainGrid(ids, 1000f, 1000f, new[] { 2 }, frozen, null, null, null, 0);
    }

    private static void DetTerrain()
    {
        var terrain = new OpeningTerrain(RiverGrid(false), null);
        // 70 m before the river going east: anchored at reach 80, not at reach 60.
        AssertTrue(terrain.RiverWithin(430f, -100f, 1f, 0f, 80f), "a river 70 m ahead is within 80");
        AssertFalse(terrain.RiverWithin(430f, -100f, 1f, 0f, 60f), "and not within 60");
        AssertFalse(terrain.RiverWithin(430f, -100f, -1f, 0f, 80f), "not in the opposite direction");
        AssertFalse(new OpeningTerrain(RiverGrid(true), null).RiverWithin(430f, -100f, 1f, 0f, 80f), "a frozen river anchors nothing");
        AssertFalse(new OpeningTerrain(null, null).RiverWithin(430f, -100f, 1f, 0f, 80f), "no grid, no river");
        // The recovery path.
        AssertTrue(terrain.SegmentCrossesRiver(400f, -100f, 600f, -100f), "the open river blocks the way home");
        AssertFalse(terrain.SegmentCrossesRiver(400f, -590f, 600f, -590f), "a ford passes");
        AssertFalse(terrain.SegmentCrossesRiver(400f, -395f, 600f, -395f), "a bridge passes");
        AssertFalse(terrain.SegmentCrossesRiver(100f, -100f, 400f, -100f), "same bank");
        AssertFalse(new OpeningTerrain(RiverGrid(true), null).SegmentCrossesRiver(400f, -100f, 600f, -100f), "a frozen map passes");
        AssertTrue(new OpeningTerrain(null, null).SegmentCrossesRiver(100f, -100f, 400f, -100f), "no grid fails closed");
        AssertFalse(new OpeningTerrain(RiverGrid(false), null).CrestWithin(100f, -100f, 50f), "no picture, no crest");
        // A picture with one crest cell (prominence 5 m) at cell (5, 40): its centre is (110, -190).
        var picture = new TerrainPicture(RiverGrid(false), 1000f, 1000f);
        picture.ProminenceLayer = new float[picture.Columns * picture.Rows];
        picture.ProminenceLayer[40 * picture.Columns + 5] = 5f;
        var withCrest = new OpeningTerrain(RiverGrid(false), picture);
        AssertFalse(withCrest.CrestWithin(110f, -150f, 50f), "a picture that is not ready is no crest");
        picture.Ready = true;
        AssertTrue(withCrest.CrestWithin(110f, -150f, 50f), "a crest cell within 50 m");
        AssertFalse(withCrest.CrestWithin(110f, -60f, 50f), "and not one 130 m away");
    }
}
