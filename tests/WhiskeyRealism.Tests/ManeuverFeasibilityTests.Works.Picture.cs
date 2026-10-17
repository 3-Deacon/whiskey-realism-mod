using System;
using System.IO;
using System.Text.RegularExpressions;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;
using static TestFixtures;
using static WorksFixtures;

// BC14W task 3: the works picture (spec § 7A requirement-works-picture and requirement-works-shape; § 13.2 WORKS-* and FOCAL-SCORE).
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] WorksPictureTests() => new (string, Action)[]
    {
        ("bc14w WORKS-SAMPLES a works sample stands in works cover above 0.15, standing and perceived; nothing else is one", WSamples),
        ("bc14w WORKS-POINTS a piece counts once complete and perceived, for the battle, for the enemy by the holder rule, at most 16 probes a tick", WPoints),
        ("bc14w WORKS-SEGMENTS works 150 units apart laterally join one segment, 160 apart make two; a segment with no holder is not held", WSegments),
        ("bc14w WORKS-ENDS a terrain-anchored end, or one another held segment continues, is anchored; otherwise open", WEnds),
        ("bc14w WORKS-SALIENT a profile point 250 nearer than its 300-unit chord is an apex, 100 nearer is not", WSalient),
        ("bc14w WORKS-JUNCTION two bodies on held works give a junction at their midpoint; with one off the works, none", WJunction),
        ("bc14w FOCAL-SCORE the lowest holders-per-frontage point wins, the apex and the junction at their factors, ties by the own centroid then the lateral", WFocalScore),
        ("bc14w WORKS-FOG a hidden entrenched regiment and an unperceived piece change nothing, the same perceived change the picture", WFog),
        ("bc14w WORKS-LINES the pieces of several lines, one per model object, build one picture; a piece below half done or destroyed is absent", WLines),
        ("bc14w WORKS-OWN a Defender's own works are its regiments in works cover and the pieces within the gap of them or in its zone", WOwn),
        ("bc14w WORKS-BROKEN-IN a strike point is broken in when the works around it hold no one, or 40% of the strength within 300 of it is shaken; elsewhere on the works, or off the known works, it is not", WBrokenIn),
        ("bc14w FEAS-PIN-W the BC14W sources read no enemy record, scheduledarrival, durationleft, occupiedby, fortificationpoints or march literal", WFeasPin),
    };

    static void WSamples()
    {
        // Control: a standing earth parapet at 0.40 is a works sample (the segment is held).
        WorksPicture ok = BuildEnemy(Snapshot(Sample(0f, 600f, 1000f)), new Points());
        AssertEqual(1, ok.WorksSamples, "an earth parapet at 0.40, standing, is a works sample");
        AssertEqual(1, ok.HeldSegmentCount, "and holds its segment");

        // 0.15 is vanilla's own threshold and is not above it; a moving regiment is no works; forest, creek, road, fence and buildings are not works.
        AssertEqual(0, BuildEnemy(Snapshot(Sample(0f, 600f, 1000f, cover: 0.15f)), new Points()).WorksSamples, "cover 0.15 is not above the threshold");
        AssertEqual(0, BuildEnemy(Snapshot(Sample(0f, 600f, 1000f, standing: false)), new Points()).WorksSamples, "a moving regiment is no works");
        foreach (int notWorks in new[] { 3, 5, 6, 1, 12, 13, 14, 0 })
            AssertEqual(0, BuildEnemy(Snapshot(Sample(0f, 600f, 1000f, coverObject: notWorks)), new Points()).WorksSamples, "cover object " + notWorks + " is not works");
        foreach (int works in new[] { 2, 4, 7, 8, 9, 10, 11, 15, 16 })
            AssertEqual(1, BuildEnemy(Snapshot(Sample(0f, 600f, 1000f, coverObject: works)), new Points()).WorksSamples, "cover object " + works + " is works");

        // A remembered sample counts at confidence 0.5 and not below; it counts at its confidence-weighted strength.
        AssertEqual(0, BuildEnemy(Snapshot(Sample(0f, 600f, 1000f, state: ObservationState.Remembered, confidence: 0.4f)), new Points()).WorksSamples,
            "a remembered regiment at confidence 0.4 is not a sample");
        WorksPicture remembered = BuildEnemy(Snapshot(Sample(0f, 600f, 1000f, state: ObservationState.Remembered, confidence: 0.5f)), new Points());
        AssertEqual(1, remembered.WorksSamples, "at 0.5 it is");
        AssertNear(500f, remembered.Segment(0).HeldStrength, 1e-3f, "at half its strength");

        // A regiment that is not an observation contributes nothing: an entrenched enemy record the side never saw.
        BattleSnapshot hidden = Snapshot();
        var record = new UnitRecord { InstanceId = 77, UnitTyp = 0, X = X(0f), Z = Z(600f), Strength = 5000, CoverValue = 0.45f, CoverObject = 7, Flags = UnitFlags.Alive | UnitFlags.Active };
        hidden.AddEnemy(in record);
        AssertEqual(0, BuildEnemy(hidden, new Points()).WorksSamples, "an enemy record that is no observation is nothing");

        // A reinforcement column and a non-combat type are not samples.
        EnemyObservation column = Sample(0f, 600f, 1000f);
        column.IsReinforcement = true;
        AssertEqual(0, BuildEnemy(Snapshot(column), new Points()).WorksSamples, "a reinforcement column is not a sample");
        AssertEqual(0, BuildEnemy(Snapshot(Sample(0f, 600f, 1000f, unitTyp: 7)), new Points()).WorksSamples, "nor is a headquarters unit type");
    }

    // 3 perceived pieces: one 100 from a holder, one 400 from it in the enemy's zone and one 400 from it in the own zone.
    static void WPoints()
    {
        BattleSnapshot s = Snapshot(Sample(0f, 600f, 3000f));
        var points = new Points();
        points.Items.Add((X(60f), Z(600f), true, true));       // near a holder: counts although it lies in the own zone
        points.Items.Add((X(500f), Z(600f), true, false));     // no holder near, outside the own zone: the enemy's
        points.Items.Add((X(-500f), Z(600f), true, true));     // no holder near, inside the own zone: not the enemy's
        points.Items.Add((X(300f), Z(600f), false, false));    // not perceived: nothing
        WorksPicture picture = BuildEnemy(s, points);
        AssertEqual(2, picture.WorksPointsCounted, "a piece near a holder and a piece outside the own zone count");
        AssertEqual(2, picture.SegmentCount, "the piece beside the holder joins its segment, the far one stands apart as a segment of its own");
        AssertEqual(1, picture.HeldSegmentCount, "and only the first is held");

        // The table: completion, the perceived stamp, a re-read, the probe budget and its order.
        var table = new WorksPointTable();
        var pieces = new WorksPiece[40];
        for (int i = 0; i < pieces.Length; i++) pieces[i] = new WorksPiece { InstanceId = 100 + i, X = X(i * 10f), Z = Z(600f), Completion = 1f, InOwnZone = false };
        pieces[3].Completion = 0.49f;
        table.Merge(pieces, X(0f), Z(0f));
        AssertEqual(39, table.Count, "a piece below half done is not kept");
        var perceivedAll = new ManeuverFeasibilityFixtures.FnProbe((x, z) => true);
        AssertEqual(16, table.ProbeStep(perceivedAll, 100), "at most 16 probes a tick");
        int perceived = 0;
        for (int i = 0; i < table.Count; i++) if (table.Perceived(i)) perceived++;
        AssertEqual(16, perceived, "sixteen pieces are perceived");
        // Oldest merge first: the first 16 kept pieces, in merge order.
        for (int i = 0; i < 16; i++) AssertTrue(table.Perceived(i), "piece " + i + " was probed in merge order");
        AssertEqual(5, table.ProbeStep(perceivedAll, 5), "the budget caps a probe below 16");
        for (int i = 16; i < 21; i++) AssertTrue(table.Perceived(i), "the next five were probed");

        // A piece found unperceived is probed again later (a perceived piece never is), oldest probe first.
        var table2 = new WorksPointTable();
        table2.Merge(pieces, X(0f), Z(0f));
        int calls = 0;
        var nothing = new ManeuverFeasibilityFixtures.FnProbe((x, z) => { calls++; return false; });
        table2.ProbeStep(nothing, 16);
        AssertEqual(16, calls, "16 probes found nothing");
        table2.ProbeStep(nothing, 16);
        AssertEqual(32, calls, "and the next 16 are other pieces: a missed piece goes to the back");
        table2.ProbeStep(perceivedAll, 16);
        table2.ProbeStep(perceivedAll, 16);
        table2.ProbeStep(perceivedAll, 16);
        for (int i = 0; i < table2.Count; i++) AssertTrue(table2.Perceived(i), "every piece is perceived in the end");
        int before = 0;
        var counting = new ManeuverFeasibilityFixtures.FnProbe((x, z) => { before++; return false; });
        AssertEqual(0, table2.ProbeStep(counting, 16), "a perceived piece is never probed again");
        AssertEqual(0, before, "and the ground is not asked");

        // A re-read keeps the stamp by instance id (not by position in the read) and drops a destroyed piece; a new piece appears at the re-read.
        var reread = new WorksPiece[pieces.Length - 1];
        for (int i = 1; i < pieces.Length; i++) reread[i - 1] = pieces[pieces.Length - i];   // reversed, the first dropped
        table2.Merge(reread, X(0f), Z(0f));
        AssertEqual(38, table2.Count, "the destroyed piece and the unfinished one are absent");
        for (int i = 0; i < table2.Count; i++) AssertTrue(table2.Perceived(i), "the stamp survived the re-read by instance id, not by index");
        var drawn = new WorksPiece[reread.Length + 1];
        Array.Copy(reread, drawn, reread.Length);
        drawn[drawn.Length - 1] = new WorksPiece { InstanceId = 999, X = X(5f), Z = Z(600f), Completion = 1f };
        table2.Merge(drawn, X(0f), Z(0f));
        AssertEqual(39, table2.Count, "a piece drawn after the first read appears at the next re-read");
        bool newOne = false;
        for (int i = 0; i < table2.Count; i++) if (!table2.Perceived(i)) newOne = true;
        AssertTrue(newOne, "and is not perceived until probed");

        // The stamp follows the piece, not its place in the read: with only the first sixteen perceived, a re-read in the opposite order keeps exactly those
        // sixteen perceived, whichever position each now has.
        var table3 = new WorksPointTable();
        table3.Merge(pieces, X(0f), Z(0f));
        table3.ProbeStep(perceivedAll, 16);
        var backwards = new WorksPiece[pieces.Length];
        for (int i = 0; i < pieces.Length; i++) backwards[i] = pieces[pieces.Length - 1 - i];
        table3.Merge(backwards, X(0f), Z(0f));
        AssertEqual(39, table3.Count, "the same 39 pieces, read the other way round");
        for (int i = 0; i < table3.Count; i++)
        {
            table3.Get(i, out float px, out _);
            int n = (int)Math.Round((px - X(0f)) / 10f);
            AssertEqual(n <= 16 && n != 3, table3.Perceived(i), "piece " + n + " keeps its own stamp at position " + i);
        }

        // Past 2,048 pieces the farthest from the own centroid are dropped and counted.
        var many = new WorksPiece[WorksRules.MaxWorksPoints + 10];
        for (int i = 0; i < many.Length; i++) many[i] = new WorksPiece { InstanceId = 1000 + i, X = X(0f) + i, Z = Z(0f), Completion = 1f };
        var big = new WorksPointTable();
        big.Merge(many, X(0f), Z(0f));
        AssertEqual(WorksRules.MaxWorksPoints, big.Count, "2,048 pieces are kept");
        AssertEqual(10, big.Dropped, "ten are dropped and counted");
        big.Get(WorksRules.MaxWorksPoints - 1, out float lastX, out _);
        AssertTrue(lastX < X(0f) + WorksRules.MaxWorksPoints, "the nearest were kept, the farthest dropped");
    }

    static void WSegments()
    {
        WorksPicture joined = BuildEnemy(Snapshot(Sample(0f, 600f, 1000f), Sample(150f, 600f, 1000f)), new Points());
        AssertEqual(1, joined.SegmentCount, "works 150 apart laterally join one segment");
        AssertNear(0f, joined.Segment(0).LateralMin, 1e-3f, "its lateral minimum");
        AssertNear(150f, joined.Segment(0).LateralMax, 1e-3f, "and maximum");
        WorksPicture split = BuildEnemy(Snapshot(Sample(0f, 600f, 1000f), Sample(160f, 600f, 1000f)), new Points());
        AssertEqual(2, split.SegmentCount, "160 apart make two");
        AssertEqual(2, split.HeldSegmentCount, "and both are held");

        // A segment of pieces alone has no holder: it shapes the picture and is not held.
        var points = new Points().Line(-300f, 300f, 50f, l => 600f);
        WorksPicture empty = BuildEnemy(Snapshot(Sample(1000f, 600f, 1000f)), points);
        AssertEqual(2, empty.SegmentCount, "the pieces form a segment of their own");
        AssertEqual(1, empty.HeldSegmentCount, "only the one with a holder is held");
        AssertFalse(empty.OnHeldWorks(X(0f), Z(600f)), "a point on empty works is not on held works");
        AssertTrue(empty.OnHeldWorks(X(1000f), Z(600f)), "a point on the held segment is");
        // The holders' summary.
        WorksPicture two = BuildEnemy(Snapshot(Sample(0f, 600f, 3000f, cover: 0.4f), Sample(100f, 600f, 1000f, cover: 0.2f)), new Points());
        AssertNear(4000f, two.Segment(0).HeldStrength, 1e-2f, "the held strength");
        AssertNear((0.4f * 3000f + 0.2f * 1000f) / 4000f, two.Segment(0).MeanCover, 1e-4f, "the strength-weighted cover");
    }

    static void WEnds()
    {
        // A river left of lateral -640 anchors the left end of a line from -600; the right end stands open.
        TerrainPicture river = RiverPicture(riverBelowX: X(-640f), riverFromX: float.MaxValue);
        var points = new Points().Line(-600f, 150f, 50f, l => 600f);
        WorksPicture picture = BuildEnemy(Snapshot(Sample(-450f, 600f, 1000f), Sample(0f, 600f, 1000f)), points, river);
        AssertEqual(1, picture.HeldSegmentCount, "one held segment");
        AssertTrue(picture.Segment(0).MinEndAnchored, "its left end rests on the river");
        AssertFalse(picture.Segment(0).MaxEndAnchored, "its right end is open");
        AssertEqual(1, picture.OpenEnds, "one open end");
        AssertEqual(1, picture.AnchoredEnds, "one anchored end");
        // Control: with no terrain picture both ends are open.
        WorksPicture bare = BuildEnemy(Snapshot(Sample(-450f, 600f, 1000f), Sample(0f, 600f, 1000f)), new Points().Line(-600f, 150f, 50f, l => 600f));
        AssertEqual(2, bare.OpenEnds, "no terrain anchors nothing");

        // Another held segment that continues beyond the end anchors it: the next begins 190 on (within the 200 of its on-segment reach), or does not 260 on.
        WorksPicture continued = BuildEnemy(Snapshot(Sample(0f, 600f, 1000f), Sample(190f, 600f, 1000f)), new Points());
        AssertEqual(2, continued.SegmentCount, "two segments");
        AssertTrue(continued.Segment(0).MaxEndAnchored && continued.Segment(1).MinEndAnchored, "the facing ends continue each other");
        AssertFalse(continued.Segment(0).MinEndAnchored || continued.Segment(1).MaxEndAnchored, "the outer ends are open");
        WorksPicture apart = BuildEnemy(Snapshot(Sample(0f, 600f, 1000f), Sample(260f, 600f, 1000f)), new Points());
        AssertEqual(4, apart.OpenEnds, "260 apart, every end is open");
    }

    // A profile at depth 600 with a bulge: depth(l) = 600 - bulge (1 - |l| / 250) within 250 of lateral 0.
    static Func<float, float> Bulge(float bulge) => l => Math.Abs(l) <= 250f ? 600f - bulge * (1f - Math.Abs(l) / 250f) : 600f;

    static void WSalient()
    {
        var holders = new[] { Sample(-450f, 600f, 1000f), Sample(450f, 600f, 1000f) };
        WorksPicture deep = BuildEnemy(Snapshot(holders), new Points().Line(-600f, 600f, 50f, Bulge(250f)));
        AssertEqual(1, deep.SalientCount, "a profile point 250 nearer than its chord is an apex");
        bool found = false;
        for (int i = 0; i < deep.FocalCount; i++)
            if (deep.Focal(i).Kind == FocalKind.Salient) { found = true; AssertNear(Z(350f), deep.Focal(i).Z, 1e-2f, "at the tip of the bulge"); AssertNear(X(0f), deep.Focal(i).X, 1e-2f, "at lateral 0"); }
        AssertTrue(found, "the apex is a focal point of kind Salient");
        AssertEqual(0, BuildEnemy(Snapshot(holders), new Points().Line(-600f, 600f, 50f, Bulge(100f))).SalientCount, "100 nearer is not");
        AssertEqual(0, BuildEnemy(Snapshot(holders), new Points().Line(-600f, 600f, 50f, Bulge(149f))).SalientCount, "just under the threshold is not");
        AssertEqual(1, BuildEnemy(Snapshot(holders), new Points().Line(-600f, 600f, 50f, Bulge(300f))).SalientCount, "a deeper bulge is still one apex (the run's nearest point)");
    }

    static void WJunction()
    {
        AssertEqual(WhiskeyRealism.Tactical.Orchestrator.Plays.ManeuverFeasibility.MaxEnemyBodies, WorksPicture.MaxBodies, "the picture takes as many bodies as the evaluation builds");
        // Two bodies 300 apart (separate at the 150-unit link) on the works: a junction at the midpoint.
        BattleSnapshot two = Snapshot(Sample(-150f, 600f, 5000f), Sample(150f, 600f, 5000f));
        WorksPicture picture = BuildEnemy(two, new Points().Line(-300f, 300f, 50f, l => 600f));
        AssertEqual(1, picture.JunctionCount, "two bodies on held works give a junction");
        bool found = false;
        for (int i = 0; i < picture.FocalCount; i++)
            if (picture.Focal(i).Kind == FocalKind.Junction) { found = true; AssertNear(X(0f), picture.Focal(i).X, 1e-2f, "at the midpoint"); AssertNear(Z(600f), picture.Focal(i).Z, 1e-2f, "at the line"); }
        AssertTrue(found, "it is a focal point of kind Junction");
        // With one of them off the works (a body far in front of the profile) none.
        BattleSnapshot off = Snapshot(Sample(-150f, 600f, 5000f), Sample(150f, 600f, 5000f), Sample(450f, 100f, 3000f, cover: 0f, coverObject: 0));
        AssertEqual(1, BuildEnemy(off, new Points().Line(-300f, 300f, 50f, l => 600f)).JunctionCount, "a body off the works adds none");
        BattleSnapshot one = Snapshot(Sample(-150f, 600f, 5000f), Sample(150f, 100f, 5000f, cover: 0f, coverObject: 0));
        AssertEqual(0, BuildEnemy(one, new Points().Line(-300f, 300f, 50f, l => 600f)).JunctionCount, "with one of the two off the works, none");
    }

    // The § 6 scenario 7 enemy: bodies on the works at -450, -150, +150, +450 and a second parapet 80 behind at lateral 0; works every 50.
    static BattleSnapshot Scenario7Enemy() => Snapshot(Sample(-450f, 600f, 6000f), Sample(-150f, 600f, 5000f), Sample(150f, 600f, 5000f), Sample(450f, 600f, 7000f),
        Sample(0f, 680f, 2000f));

    static BattleSnapshot Scenario8Enemy() => Snapshot(Sample(-450f, 600f, 7000f), Sample(-200f, 600f, 6000f), Sample(200f, 600f, 6000f), Sample(0f, 350f, 3000f),
        Sample(450f, 600f, 3000f));

    static void WFocalScore()
    {
        WorksPicture seven = BuildEnemy(Scenario7Enemy(), new Points().Line(-600f, 600f, 50f, l => 600f));
        AssertTrue(seven.TryFocal(out FocalPoint junction), "scenario 7 has a focal point");
        AssertEqual(FocalKind.Junction, junction.Kind, "the junction between the -450 and -150 bodies");
        AssertNear(X(-300f), junction.X, 0.5f, "at lateral -300");
        AssertNear(Z(600f), junction.Z, 0.5f, "and depth 600");
        AssertNear(11000f * 1.4f / 600f * 0.8f, junction.Score, 0.01f, "11,000 x 1.4 / 600 x 0.8 = 20.5");
        float next = float.MaxValue;
        for (int i = 0; i < seven.FocalCount; i++)
            if (seven.Focal(i).Score > junction.Score && seven.Focal(i).Score < next) next = seven.Focal(i).Score;
        AssertNear(6000f * 1.4f / 400f, next, 0.01f, "the next lowest is the point at lateral -500: 6,000 x 1.4 / 400 = 21.0");

        // Scenario 8: the apex scores half (3,000 x 1.4 / 600 x 0.5 = 3.5) and is the focal point.
        WorksPicture eight = BuildEnemy(Scenario8Enemy(), new Points().Line(-600f, 600f, 50f, Bulge(250f)));
        AssertTrue(eight.TryFocal(out FocalPoint apex), "scenario 8 has a focal point");
        AssertEqual(FocalKind.Salient, apex.Kind, "the apex");
        AssertNear(X(0f), apex.X, 0.5f, "at lateral 0");
        AssertNear(Z(350f), apex.Z, 0.5f, "at depth 350");
        AssertNear(3000f * 1.4f / 600f * 0.5f, apex.Score, 0.01f, "3.5 per unit");

        // The salient factor matters: the same point scored as a plain profile point would be 7.0, above the junction beside it (16.8).
        // Ties: two equal points go to the one nearer the own centroid, then to the lower lateral.
        WorksPicture tie = BuildEnemy(Snapshot(Sample(-100f, 600f, 1000f), Sample(100f, 600f, 1000f)), new Points());
        AssertTrue(tie.TryFocal(out FocalPoint t), "the tie has a focal point");
        AssertTrue(t.X < X(0f), "equal scores and equal distance go to the lower lateral");
        WorksPicture nearer = BuildEnemy(Snapshot(Sample(-100f, 700f, 1000f), Sample(100f, 600f, 1000f)), new Points());
        AssertTrue(nearer.TryFocal(out FocalPoint n), "the second tie has a focal point");
        AssertTrue(n.X > X(0f), "equal scores go to the point nearer the own centroid");
        AssertFalse(BuildEnemy(Snapshot(), new Points()).TryFocal(out _), "no works, no focal point");
    }

    static void WFog()
    {
        var points = new Points().Line(-300f, 300f, 50f, l => 600f);
        // Hidden regiment: the entrenched enemy the side has not seen changes nothing.
        BattleSnapshot seen = Snapshot(Sample(0f, 600f, 3000f));
        BattleSnapshot withHidden = Snapshot(Sample(0f, 600f, 3000f));
        var record = new UnitRecord { InstanceId = 88, UnitTyp = 0, X = X(-600f), Z = Z(600f), Strength = 9000, CoverValue = 0.5f, CoverObject = 7, Flags = UnitFlags.Alive | UnitFlags.Active };
        withHidden.AddEnemy(in record);
        WorksPicture a = BuildEnemy(seen, points), b = BuildEnemy(withHidden, points);
        AssertEqual(a.SegmentCount, b.SegmentCount, "a hidden regiment adds no segment");
        AssertTrue(a.TryFocal(out FocalPoint fa) && b.TryFocal(out FocalPoint fb) && fa.X == fb.X && fa.Z == fb.Z && fa.Score == fb.Score, "and moves no focal point");
        AssertNear(a.Segment(0).HeldStrength, b.Segment(0).HeldStrength, 1e-3f, "nor any strength");

        // An unperceived piece: the far piece (and the one beside it) change nothing while unperceived, and extend the line once perceived.
        var unseen = new Points().Line(-300f, 300f, 50f, l => 600f);
        unseen.Line(350f, 700f, 50f, l => 600f, perceived: false);
        WorksPicture c = BuildEnemy(seen, unseen);
        AssertNear(a.Segment(0).LateralMax, c.Segment(0).LateralMax, 1e-3f, "unperceived pieces do not extend the segment");
        AssertEqual(a.WorksPointsCounted, c.WorksPointsCounted, "nor count");
        var perceived = new Points().Line(-300f, 300f, 50f, l => 600f);
        perceived.Line(350f, 700f, 50f, l => 600f, perceived: true);
        WorksPicture d = BuildEnemy(seen, perceived);
        AssertNear(700f, d.Segment(0).LateralMax, 1e-3f, "the same pieces, perceived, extend it to their last");
        // Contrast for the regiment: perceived (a second observation), it is a holder.
        BattleSnapshot nowSeen = Snapshot(Sample(0f, 600f, 3000f), Sample(-600f, 600f, 9000f));
        WorksPicture e = BuildEnemy(nowSeen, points);
        AssertEqual(2, e.WorksSamples, "the same regiment perceived is a works sample");
        AssertNear(12000f, e.Segment(0).HeldStrength + e.Segment(1).HeldStrength, 1e-2f, "and its strength counts");
    }

    static void WLines()
    {
        // Three lines (an AI group's, a drawn one, a loaded one) read as pieces with distinct model-object ids.
        var pieces = new System.Collections.Generic.List<WorksPiece>();
        int id = 5000;
        foreach (float depth in new[] { 600f, 640f, 700f })
            for (float l = -200f; l <= 200f; l += 50f) pieces.Add(new WorksPiece { InstanceId = id++, X = X(l), Z = Z(depth), Completion = 1f });
        pieces.Add(new WorksPiece { InstanceId = id++, X = X(250f), Z = Z(600f), Completion = 0.4f });     // unfinished
        var table = new WorksPointTable();
        table.Merge(pieces.ToArray(), X(0f), Z(0f));
        AssertEqual(27, table.Count, "27 finished pieces, the unfinished one skipped");
        table.ProbeStep(new ManeuverFeasibilityFixtures.FnProbe((x, z) => true), 16);
        table.ProbeStep(new ManeuverFeasibilityFixtures.FnProbe((x, z) => true), 16);
        AssertEqual(27, CountPerceived(table), "all perceived after two ticks");
        // A holder makes it the enemy's: the segment spans the built extent and no further, and the second and third lines do not move the profile.
        WorksPicture picture = BuildEnemy(Snapshot(Sample(0f, 600f, 1000f)), table);
        AssertEqual(1, picture.HeldSegmentCount, "one held segment");
        AssertNear(-200f, picture.Segment(0).LateralMin, 1e-3f, "from the first piece");
        AssertNear(200f, picture.Segment(0).LateralMax, 1e-3f, "to the last: nothing past it counts");
        picture.Profile(picture.Segment(0).ProfileStart, out _, out float depth0, out _, out _);
        AssertNear(600f, depth0, 1e-2f, "the profile is the nearest line");
        // A piece destroyed (absent from the next read) leaves the picture at the next build.
        pieces.RemoveRange(0, 9);
        table.Merge(pieces.ToArray(), X(0f), Z(0f));
        AssertEqual(18, table.Count, "the first line is gone");
        WorksPicture later = BuildEnemy(Snapshot(Sample(0f, 600f, 1000f)), table);
        later.Profile(later.Segment(0).ProfileStart, out _, out float depth1, out _, out _);
        AssertNear(640f, depth1, 1e-2f, "the profile is the next line");
    }

    static int CountPerceived(WorksPointTable table)
    {
        int n = 0;
        for (int i = 0; i < table.Count; i++) if (table.Perceived(i)) n++;
        return n;
    }

    static void WOwn()
    {
        // The Defender's own works: two regiments in works cover (and a moving one, a group and a player's regiment that are not).
        var tree = new ManeuverFeasibilityFixtures.TreeFixture();
        int army = tree.Add(TacticalUnitType.BattleGroupArmy, X(0f), Z(0f), -1);
        int standing = tree.Add(TacticalUnitType.Infantry, X(0f), Z(0f), army, UnitFlags.IsInfantry, 2000f);
        int standing2 = tree.Add(TacticalUnitType.Infantry, X(80f), Z(0f), army, UnitFlags.IsInfantry, 1000f);
        int moving = tree.Add(TacticalUnitType.Infantry, X(-200f), Z(0f), army, UnitFlags.IsInfantry, 1000f);
        int players = tree.Add(TacticalUnitType.Infantry, X(300f), Z(0f), army, UnitFlags.IsInfantry | UnitFlags.PlayerOwned, 1000f);
        BattleSnapshot s = tree.Build(10.0);
        foreach (int i in new[] { standing, standing2, moving, players }) { s.Own[i].CoverValue = 0.45f; s.Own[i].CoverObject = 7; }
        s.Own[moving].RegimentPaths = 1;
        var points = new Points();
        points.Items.Add((X(130f), Z(0f), true, false));     // within the gap of an own works regiment: own
        points.Items.Add((X(900f), Z(0f), true, true));      // inside the own zone, no regiment near: own
        points.Items.Add((X(-900f), Z(0f), true, false));    // neither: the enemy's, not ours
        var picture = new WorksPicture();
        picture.BuildOwn(s, Axis(), points, null);
        AssertEqual(2, picture.WorksSamples, "two own regiments stand in works cover: the moving one and the player's do not count");
        AssertEqual(2, picture.WorksPointsCounted, "the two pieces that are the side's own");
        AssertTrue(picture.HeldSegmentCount >= 1, "the own works are held");
    }

    // The fog and march pins over the BC14W sources: FEAS-PIN's and MARCH-RULES-PIN's scanners, plus the two works-specific reads.
    static readonly Regex WorksReads = new Regex(@"EnemyRecords|scheduledarrival|durationleft|occupiedby|fortificationpoints", RegexOptions.Compiled);

    internal static string[] Bc14wFiles()
    {
        string[] relative =
        {
            "src/WhiskeyRealism/Tactical/Battle/Works/WorksRules.cs",
            "src/WhiskeyRealism/Tactical/Battle/Works/WorksPicture.cs",
            "src/WhiskeyRealism/Tactical/Battle/Works/PreparationTable.cs",
            "src/WhiskeyRealism/Tactical/Battle/Works/MoveContest.cs",
            "src/WhiskeyRealism/Tactical/Battle/Works/FortificationPointsRuntime.cs",
            "src/WhiskeyRealism/Tactical/Orchestrator/Plays/PlayExecutorRules.cs",
            "src/WhiskeyRealism/Tactical/Orchestrator/Plays/ManeuverFeasibility.Works.cs",
            "src/WhiskeyRealism/Tactical/Orchestrator/Plays/ManeuverFeasibility.Station.cs",
            "src/WhiskeyRealism/Tactical/Orchestrator/Playbooks/FocalAssaultPlaybook.cs",
        };
        // Every task has landed: a file that is not found is a path that moved, and a pin over fewer files than it names says nothing.
        var found = new System.Collections.Generic.List<string>();
        foreach (string rel in relative) found.Add(TestFixtures.FindRepoPath(rel));
        AssertEqual(relative.Length, found.Count, "every BC14W file is found");
        return found.ToArray();
    }

    static string FirstWorksRead(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            string code = line;
            int comment = code.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) code = code.Substring(0, comment);
            Match m = WorksReads.Match(code);
            if (m.Success) return m.Value + " in: " + line.Trim();
        }
        return null;
    }

    static void WFeasPin()
    {
        AssertTrue(FirstWorksRead("var r = snapshot.EnemyRecords[i];") != null, "the scanner fires on a planted EnemyRecords read");
        AssertTrue(FirstWorksRead("bool b = fort.occupiedby != null;") != null, "and on occupiedby");
        AssertTrue(FirstWorksRead("var p = bfs.fortifications[i].fortificationpoints;") != null, "and on fortificationpoints");
        AssertTrue(FirstWorksRead("float t = reg.scheduledarrival;") != null, "and on scheduledarrival");
        AssertTrue(FirstWorksRead("var o = s.Observations[i]; // EnemyRecords are never read") == null, "a comment naming the rule is no read");
        AssertTrue(FirstMarchLiteral("float t = cost * 1000f;") != null, "the march scanner fires on a planted literal");
        string[] files = Bc14wFiles();
        AssertEqual(9, files.Length, "the BC14W file set is found");
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            string read = FirstWorksRead(text);
            AssertTrue(read == null, Path.GetFileName(file) + " reads hidden enemy state: " + read);
            string literal = FirstMarchLiteral(text);
            AssertTrue(literal == null, Path.GetFileName(file) + " carries a vanilla literal: " + literal);
        }
    }

    // The integrator's ruling (verify round 2): a fix facing works is pinned at stance 2 until the strike it pins for has broken in. "Broken in" is read
    // from the picture: the holders within WorksRules.FrontageReachMeters of the strike point are gone (the works there are known and empty) or
    // WorksRules.ShakenShareMin of their strength is below WorksRules.ShakenMorale. Fog-honest by construction: only accepted observations are holders.
    static void WBrokenIn()
    {
        var points = new Points().Line(-600f, 600f, 50f, l => 600f);
        float sx = X(-300f), sz = Z(600f);

        // Control: a line of healthy holders is not broken in at any point.
        WorksPicture healthy = BuildEnemy(Snapshot(Sample(-300f, 600f, 1000f), Sample(0f, 600f, 1000f), Sample(300f, 600f, 1000f)), points);
        AssertTrue(healthy.OnHeldWorks(sx, sz), "control: the strike point is on held works");
        AssertFalse(healthy.StrikeBrokenIn(sx, sz), "healthy holders: not broken in");

        // Shaken: 40% of the strength within reach is below 0.45; 30% is not.
        WorksPicture shaken = BuildEnemy(Snapshot(Sample(-300f, 600f, 400f, morale: 0.3f), Sample(-250f, 600f, 600f), Sample(300f, 600f, 1000f)), points);
        AssertTrue(shaken.StrikeBrokenIn(sx, sz), "40% of the strength near the strike shaken: broken in");
        WorksPicture most = BuildEnemy(Snapshot(Sample(-300f, 600f, 300f, morale: 0.3f), Sample(-250f, 600f, 700f), Sample(300f, 600f, 1000f)), points);
        AssertFalse(most.StrikeBrokenIn(sx, sz), "30% shaken: not broken in");
        WorksPicture edge = BuildEnemy(Snapshot(Sample(-300f, 600f, 400f, morale: 0.45f), Sample(-250f, 600f, 600f)), points);
        AssertFalse(edge.StrikeBrokenIn(sx, sz), "morale exactly 0.45 is not shaken");

        // Shaken holders far from the strike do not count for it.
        WorksPicture far = BuildEnemy(Snapshot(Sample(-300f, 600f, 1000f), Sample(500f, 600f, 1000f, morale: 0.1f)), points);
        AssertFalse(far.StrikeBrokenIn(sx, sz), "shaken holders 800 away: not this strike's");
        AssertTrue(far.StrikeBrokenIn(X(500f), Z(600f)), "and broken in at their own");

        // Carried: the pieces there are perceived (the works are known) but no holder stands within reach of the strike.
        WorksPicture carried = BuildEnemy(Snapshot(Sample(450f, 600f, 1000f)), points);
        AssertTrue(carried.StrikeBrokenIn(sx, sz), "known works, nobody within 300 of the strike: carried");
        AssertFalse(carried.StrikeBrokenIn(X(450f), Z(600f)), "control: where the holder stands it is not");

        // Fog: a hidden regiment and an unknown stretch say nothing. No pieces perceived there and no holder: the picture covers nothing there.
        WorksPicture unknown = BuildEnemy(Snapshot(Sample(450f, 600f, 1000f)), new Points());
        AssertFalse(unknown.StrikeBrokenIn(sx, sz), "no works known at the strike: not broken in");
        AssertFalse(healthy.StrikeBrokenIn(X(1500f), Z(600f)), "beyond the segment's extent: not broken in");
        AssertFalse(new WorksPicture().StrikeBrokenIn(sx, sz), "an empty picture breaks in nothing");
    }
}
