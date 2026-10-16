using System;
using System.Collections.Generic;
using System.IO;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// BC13 task 4: the perceived-ground lattice under the per-tick probe cap (spec § 5, requirement-perceived-ground),
// bounded penetration for a Defender (requirement-detect-penetration) and the Counterstroke task
// (requirement-defender-counterstroke). Frame as in the Detection partial: lateral = x, depth = z, the enemy at +z.
internal static partial class CommanderOpportunityTests
{
    private static (string, Action)[] ProbeTests()
    {
        return new (string, Action)[]
        {
            ("bc13 DET-UNPERCEIVED one unperceived probe point suppresses the gap and is counted", DetUnperceived),
            ("bc13 DET-REVERSE-SLOPE a hidden second line behind the gap means the gap is not emitted", DetReverseSlope),
            ("bc13 DET-RANGE-ONLY a point is perceived only within an own unit's reach", DetRangeOnly),
            ("bc13 DET-PERCEIVED-INCIDENT only the fog-grid probe masks the W&L intelligence incidents", DetPerceivedIncident),
            ("bc13 DET-PROBE-BUDGET at most 128 points a tick, oldest lattice first, emission after completion", DetProbeBudget),
            ("bc13 DET-PROBE-BUDGET a restarted lattice joins the back and a completed one stays emitted", DetProbeRestart),
            ("bc13 DET-PROBE-BUDGET restart rules of a lattice", DetLatticeRestartRules),
            ("bc13 DET-PROBE-BUDGET a lattice behind nine others ends within the completion bound", DetCompletionBound),
            ("bc13 DET-PROBE-BUDGET a time-bucket turn re-probes a completed lattice while its candidate stays emitted", DetBucketTurn),
            ("bc13 DET-PEN a Defender emits Penetration for a visible sample 150 m behind its trace", DetPen),
            ("bc13 DET-PEN-SHALLOW a sample 60 m behind the trace emits nothing", DetPenShallow),
            ("bc13 DET-PEN-MELEE melee, or within 50 m of an own front-line sample, emits nothing", DetPenMelee),
            ("bc13 DET-PEN-REMEMBERED a remembered sample emits nothing", DetPenRemembered),
            ("bc13 DET-PEN-CONTACT close contact with an irregular line is no penetration", DetPenContact),
            ("bc13 DET-PEN-BRIDGE no trace across a gap of 150 m or more", DetPenBridge),
            ("bc13 COUNTER-BOUNDS an aim more than 400 m behind the own trace is not emitted", CounterBounds),
            ("bc13 COUNTER-TASK the Counterstroke task passes the guards and maps to attack", CounterTask),
            ("bc13 COUNTER-PIN only the exploit override creates the Counterstroke task", CounterPin),
        };
    }

    // ------------------------------------------------------------------ helpers

    // Detect until every lattice is done (no candidate deferred), at the same time, so a test sees the settled result.
    private static int Settle(OpeningDetector d, DetFixture fx, IPerceivedGround ground = null, IOpeningTerrain terrain = null,
        double now = 10.0, bool defender = false, OpeningRules? rules = null, int maxTicks = 12)
    {
        int count = 0;
        for (int t = 0; t < maxTicks; t++)
        {
            count = Detect(d, fx, ground, terrain, now, defender, rules);
            if (d.Deferred == 0) break;
        }
        return count;
    }

    private static ProbeBox BoxOf(int lateralPoints, int depthPoints, float originX = 0f)
    {
        return new ProbeBox
        {
            OriginX = originX, OriginZ = 0f, RightX = 1f, RightZ = 0f, ForwardX = 0f, ForwardZ = 1f,
            LateralMin = 0f, LateralMax = 50f * (lateralPoints - 1), DepthMin = 0f, DepthMax = 50f * (depthPoints - 1),
            LateralPoints = lateralPoints, DepthPoints = depthPoints,
        };
    }

    private static ProbeEpoch Epoch(long bucket, PerceptionMode mode = PerceptionMode.FogGrid, int fallback = 0)
        => new ProbeEpoch { Mode = mode, Fallback = fallback, Bucket = bucket };

    // ------------------------------------------------------------------ perceived ground

    private static void DetUnperceived()
    {
        var d = new OpeningDetector();
        // The gap of GapLine(): lateral 50-350, depth 550-900. One probe point (200, 700) is hidden.
        Settle(d, GapLine(), new FnGround((x, z) => !(Math.Abs(x - 200f) < 1f && Math.Abs(z - 700f) < 1f)));
        AssertEqual(0, CountKind(d, OpeningKind.Gap), "one unperceived point suppresses the gap");
        AssertEqual(1, d.SuppressedUnperceived, "and it is counted");
        var all = new OpeningDetector();
        Settle(all, GapLine());
        AssertEqual(1, CountKind(all, OpeningKind.Gap), "with every point perceived the gap is emitted");
        AssertEqual(0, all.SuppressedUnperceived, "nothing suppressed");
    }

    private static void DetReverseSlope()
    {
        // A second line 150 m behind the gap sits on a reverse slope: the fog grid does not show depth 740-900, though the
        // ground beside the gap is in sight.
        var d = new OpeningDetector();
        Settle(d, GapLine(), new FnGround((x, z) => z < 740f));
        AssertEqual(0, CountKind(d, OpeningKind.Gap), "hidden ground behind the gap means no gap");
        Settle(d, GapLine(), new FnGround((x, z) => true), now: 10.2);
        AssertEqual(1, CountKind(d, OpeningKind.Gap), "once the ground is in sight it is a gap");
    }

    private static void DetRangeOnly()
    {
        var ground = new RangeOnlyPerceivedGround();
        ground.SetReach(new[] { 0f, 1000f }, new[] { 0f, 0f }, new[] { 100f, 100f }, 2);
        AssertTrue(ground.IsPerceived(60f, 60f), "within an own unit's reach");
        AssertTrue(ground.IsPerceived(1000f, 99f), "within the second unit's reach");
        AssertFalse(ground.IsPerceived(500f, 0f), "between the units, beyond both");
        AssertFalse(ground.IsPerceived(1200f, 0f), "beyond all reach");
        ground.SetReach(new float[0], new float[0], new float[0], 0);
        AssertFalse(ground.IsPerceived(0f, 0f), "no own unit, nothing perceived");
    }

    private static void DetPerceivedIncident()
    {
        AssertTrue(BattlePerception.MasksPlayerIntelIncidents(PerceptionMode.FogGrid, true), "FogGrid masks the W&L incidents");
        AssertFalse(BattlePerception.MasksPlayerIntelIncidents(PerceptionMode.OwnSideWithinFog, true), "OwnSideWithinFog reads the player's own grid: no mask");
        AssertFalse(BattlePerception.MasksPlayerIntelIncidents(PerceptionMode.RangeOnly, true), "RangeOnly asks no grid");
        AssertFalse(BattlePerception.MasksPlayerIntelIncidents(PerceptionMode.FogGrid, false), "no scenario, nothing to mask");
        // The runtime half (source pin): the handle masks the incidents once for the whole block of fog-grid probes, in
        // FogGrid mode only, and the oracle asks UnitIsHidden for an infantry regiment's spotting factor.
        string runtime = File.ReadAllText(TestFixtures.FindRepoPath("src/WhiskeyRealism/Tactical/Battle/PerceivedGroundRuntime.cs"));
        int open = runtime.IndexOf("internal static Handle Open(", StringComparison.Ordinal);
        int fogCase = runtime.IndexOf("case PerceptionMode.FogGrid:", open, StringComparison.Ordinal);
        int ownCase = runtime.IndexOf("case PerceptionMode.OwnSideWithinFog:", open, StringComparison.Ordinal);
        int rangeCase = runtime.IndexOf("default:", open, StringComparison.Ordinal);
        AssertTrue(open > 0 && fogCase > open && ownCase > fogCase && rangeCase > ownCase, "Open has the three modes");
        AssertTrue(runtime.IndexOf("WithIntelIncidentMask(mode)", fogCase, ownCase - fogCase, StringComparison.Ordinal) > 0, "the FogGrid case masks the incidents");
        AssertTrue(runtime.IndexOf("WithIntelIncidentMask(", ownCase, rangeCase - ownCase, StringComparison.Ordinal) < 0, "OwnSideWithinFog reads the player's own grid: no mask");
        AssertContains(runtime, "UnitIsHidden(new Vector3(x, 0f, z), unittype: 0)", "an infantry regiment's spotting factor, nothing else");
    }

    // ------------------------------------------------------------------ the lattice under the cap

    // A line of 11 infantry 450 m apart (width 100): ten 350 m gaps, each an 8 x 8 = 64 point lattice.
    private static DetFixture TenGaps()
    {
        var fx = new DetFixture();
        for (int i = 0; i < 11; i++) fx.Inf(i * 450f, 600f);
        return fx;
    }

    private static void DetProbeBudget()
    {
        var d = new OpeningDetector();
        var fx = TenGaps();
        int[] expected = { 2, 4, 6, 8, 10 };
        int previousPoints = 0;
        for (int tick = 0; tick < 5; tick++)
        {
            Detect(d, fx, null, null, 10.0 + tick * 0.001);
            AssertTrue(d.ProbePointsEvaluated <= OpeningDetector.MaxProbePointsPerTick, "tick " + tick + " probed " + d.ProbePointsEvaluated);
            AssertEqual(expected[tick], CountKind(d, OpeningKind.Gap), "tick " + tick + ": gaps emitted only once their lattice is complete");
            if (tick > 0)
            {
                AssertTrue(d.ProbeCached > 0, "tick " + tick + ": earlier results are reused");
                AssertTrue(d.ProbeCached >= previousPoints, "the cache grows");
            }
            previousPoints = d.ProbePointsEvaluated;
        }
        AssertEqual(2, d.Deferred, "the two flank-end lattices (49 points each) still wait behind the ten gaps");
        Detect(d, fx, null, null, 10.005);
        AssertEqual(0, d.Deferred, "nothing waits after the sixth tick (738 points at 128 a tick)");
        // Oldest first: the gaps came out left to right.
        Opening[] open = d.Openings.ToArray();
        float lastAim = float.MinValue;
        foreach (Opening o in open)
        {
            if (o.Kind != OpeningKind.Gap) continue;
            AssertTrue(o.FirstSeenHours >= 10.0, "first seen");
            lastAim = Math.Max(lastAim, o.AimX);
        }
        AssertTrue(lastAim > 4000f, "the last gap (lateral 4275) is emitted too");
        // Settled, nothing is probed at all inside the epoch.
        Detect(d, fx, null, null, 10.006);
        AssertEqual(0, d.ProbePointsEvaluated, "a settled lattice is reused, not re-probed, inside its time bucket");
        AssertEqual(10, CountKind(d, OpeningKind.Gap), "and the gaps stay emitted");
    }

    private static void DetProbeRestart()
    {
        // Four 350 m gaps (units 0, 450, 900, 1350, 1800).
        Func<float, DetFixture> line = shift =>
        {
            var f = new DetFixture();
            f.Inf(0f + shift, 600f);
            f.Inf(450f, 600f);
            f.Inf(900f, 600f);
            f.Inf(1350f, 600f);
            f.Inf(1800f, 600f);
            return f;
        };
        var d = new OpeningDetector();
        Detect(d, line(0f), null, null, 10.0);
        AssertEqual(2, CountKind(d, OpeningKind.Gap), "tick 1: the two oldest lattices finish");
        Func<OpeningDetector, float, double> lastSeenAt = (det, aimBelow) =>
        {
            foreach (Opening o in det.Openings.ToArray()) if (o.Kind == OpeningKind.Gap && o.AimX < aimBelow) return o.LastSeenHours;
            return double.NaN;
        };
        double firstGapSeen = lastSeenAt(d, 300f);
        AssertEqual(10.0, firstGapSeen, "the first gap was proved on tick 1");

        // The first unit moves 30 m: the first gap's box shifts by more than half the probe spacing, so its lattice restarts and
        // joins the back of the serving order, behind gaps 3 and 4 and the two flank-end lattices.
        Detect(d, line(-30f), null, null, 10.001);
        AssertTrue(d.Deferred >= 1, "the restarted gap waits");
        AssertEqual(10.0, lastSeenAt(d, 300f), "the restarted gap's opening is held, not refreshed");
        int served = 0;
        foreach (Opening o in d.Openings.ToArray()) if (o.Kind == OpeningKind.Gap && o.LastSeenHours == 10.001) served++;
        AssertEqual(3, served, "tick 2 served the older lattices (gaps 3 and 4) first, and gap 2 stays proved from its cache");

        int ticks = 2;
        while (lastSeenAt(d, 300f) < 10.001 && ticks < 8)
        {
            Detect(d, line(-30f), null, null, 10.0 + 0.001 * ticks);
            ticks++;
        }
        AssertTrue(lastSeenAt(d, 300f) > 10.001, "the restarted lattice completes, after the older ones");
        AssertEqual(3, ticks, "tick 3: the older right-flank lattice (49 points) first, then the restarted gap (64)");
        Detect(d, line(-30f), null, null, 10.0 + 0.001 * ticks);
        AssertEqual(0, d.Deferred, "nothing waits once the restarted left-flank lattice is done");
    }

    private static void DetLatticeRestartRules()
    {
        var lattice = new PerceivedGroundLattice();
        ProbeBox box = BoxOf(4, 4);
        lattice.Begin(0, OpeningKind.Gap, in box, Epoch(200), keepLastResult: false, order: 1);
        ProbeBox shifted20 = BoxOf(4, 4, originX: 20f);
        ProbeBox shifted30 = BoxOf(4, 4, originX: 30f);
        AssertFalse(lattice.NeedsRestart(in shifted20, Epoch(200), out _), "a 20 m shift keeps the cache");
        AssertTrue(lattice.NeedsRestart(in shifted30, Epoch(200), out bool keep30), "a 30 m shift restarts");
        AssertFalse(keep30, "a geometry restart drops the last result");
        AssertTrue(lattice.NeedsRestart(in box, Epoch(200, PerceptionMode.RangeOnly), out bool keepMode), "a mode change restarts");
        AssertFalse(keepMode, "and drops the last result");
        AssertTrue(lattice.NeedsRestart(in box, Epoch(200, fallback: 2), out bool keepFallback), "a fallback change restarts");
        AssertFalse(keepFallback, "and drops the last result");
        AssertFalse(lattice.NeedsRestart(in box, Epoch(201), out _), "a time-bucket turn leaves a lattice in progress alone");
        // complete it
        int budget = 128;
        AssertEqual(LatticeState.Perceived, lattice.Step(new AllPerceived(), ref budget), "complete");
        AssertEqual(128 - 16, budget, "16 points evaluated");
        AssertFalse(lattice.NeedsRestart(in box, Epoch(200), out _), "a completed lattice inside its bucket stays");
        AssertTrue(lattice.NeedsRestart(in box, Epoch(201), out bool keepBucket), "a completed lattice is re-probed when the bucket turns");
        AssertTrue(keepBucket, "and keeps its last result while it runs");
    }

    private static void DetCompletionBound()
    {
        // Nine 14-point lattices ahead of a 300-point one, a budget of 128 a tick.
        var pool = new PerceivedGroundLattice[10];
        for (int i = 0; i < 9; i++)
        {
            pool[i] = new PerceivedGroundLattice();
            ProbeBox small = BoxOf(2, 7);
            pool[i].Begin(i, OpeningKind.Gap, in small, Epoch(200), false, order: i + 1);
        }
        pool[9] = new PerceivedGroundLattice();
        ProbeBox big = BoxOf(10, 30);
        pool[9].Begin(9, OpeningKind.Gap, in big, Epoch(200), false, order: 10);
        var ground = new AllPerceived();
        var stats = new LatticeTickStats();
        long ended = -1;
        for (long tick = 1; tick <= 6 && ended < 0; tick++)
        {
            stats = default;
            for (int i = 0; i < 10; i++) pool[i].Claimed = true;
            PerceivedGroundLattice.Serve(pool, 10, ground, OpeningDetector.MaxProbePointsPerTick, tick, ref stats);
            AssertTrue(stats.PointsProbed <= OpeningDetector.MaxProbePointsPerTick, "the cap holds on tick " + tick);
            if (pool[9].State != LatticeState.Incomplete) ended = tick;
        }
        AssertTrue(ended > 0, "the big lattice ends");
        AssertTrue(pool[9].StartTick > 0, "it has a start");
        int bound = (300 + OpeningDetector.MaxProbePointsPerTick - 1) / OpeningDetector.MaxProbePointsPerTick;
        AssertTrue(ended - pool[9].StartTick + 1 <= bound, "ended at " + ended + ", started " + pool[9].StartTick + ", bound " + bound);
        AssertEqual(0, stats.OverBound, "no lattice is over the bound");
        for (int i = 0; i < 9; i++) AssertEqual(LatticeState.Perceived, pool[i].State, "lattice " + i + " done");
    }

    private static void DetBucketTurn()
    {
        // One wide gap (1000 m: 21 x 8 = 168 points) takes two ticks. After it is emitted, a bucket turn re-probes it:
        // the candidate stays emitted while the new lattice runs.
        var fx = new DetFixture();
        fx.Inf(0f, 600f);
        fx.Inf(1100f, 600f);
        Action<OpeningDetector, IPerceivedGround> settleTicks = (det, ground) =>
        {
            Detect(det, fx, ground, null, 10.0);
            AssertEqual(0, CountKind(det, OpeningKind.Gap), "168 points do not fit in one tick: deferred");
            Detect(det, fx, ground, null, 10.001);
            AssertEqual(1, CountKind(det, OpeningKind.Gap), "complete on the second tick");
            Detect(det, fx, ground, null, 10.03);
            Detect(det, fx, ground, null, 10.049);   // the last tick of the 0.05 h time bucket 200
            AssertEqual(0, det.ProbePointsEvaluated, "a settled lattice is not re-probed inside its bucket");
        };
        var d = new OpeningDetector();
        settleTicks(d, new AllPerceived());
        double seen = First(d, OpeningKind.Gap).LastSeenHours;
        Detect(d, fx, null, null, 10.051);   // bucket 201: the completed lattice is re-probed
        AssertTrue(d.ProbePointsEvaluated > 0, "the completed lattice is re-probed");
        AssertEqual(1, CountKind(d, OpeningKind.Gap), "the gap stays emitted while the new lattice runs");
        AssertTrue(First(d, OpeningKind.Gap).LastSeenHours > seen, "and is refreshed");
        Detect(d, fx, null, null, 10.052);
        AssertEqual(1, CountKind(d, OpeningKind.Gap), "still emitted when the new lattice completes");

        // A re-probe that finds an unperceived point suppresses the candidate at once.
        var e = new OpeningDetector();
        settleTicks(e, new AllPerceived());
        double lastSeen = First(e, OpeningKind.Gap).LastSeenHours;
        Detect(e, fx, new FnGround((x, z) => x > 100f), null, 10.051);
        AssertTrue(e.SuppressedUnperceived >= 1, "suppressed at once");
        AssertEqual(lastSeen, First(e, OpeningKind.Gap).LastSeenHours, "the held opening is not refreshed");
    }

    // ------------------------------------------------------------------ penetration

    // A Defender line: four own infantry at lateral 0, 100, 200, 300, depth 0, width 100 (a solid trace at depth 0 over
    // lateral -50..350). Enemy samples are added by each test.
    private static DetFixture DefenderLine()
    {
        var fx = new DetFixture();
        for (int i = 0; i < 4; i++) fx.OwnInf(i * 100f, 0f);
        return fx;
    }

    private static int Penetrations(DetFixture fx, bool defender = true, OpeningRules? rules = null)
    {
        var d = new OpeningDetector();
        Settle(d, fx, defender: defender, rules: rules);
        return CountKind(d, OpeningKind.Penetration);
    }

    private static void DetPen()
    {
        var fx = DefenderLine();
        int id = fx.Inf(150f, -150f);
        var d = new OpeningDetector();
        Settle(d, fx, defender: true);
        AssertEqual(1, CountKind(d, OpeningKind.Penetration), "a visible sample 150 m behind the trace is a penetration");
        Opening o = First(d, OpeningKind.Penetration);
        AssertEqual(id, o.TargetObservation, "target = the penetrating sample");
        AssertNear(150f, o.AimX, 1e-3f, "aim x");
        AssertNear(-150f, o.AimZ, 1e-3f, "aim z");
        AssertEqual(0, CountKind(d, OpeningKind.Gap) + CountKind(d, OpeningKind.FlankInAir), "a Defender detects nothing else");
        var off = OpeningRules.Shipped;
        off.CounterstrokeOn = false;
        AssertEqual(0, Penetrations(fx, true, off), "flag off: no penetration");
        AssertEqual(0, Penetrations(fx, defender: false), "a non-Defender emits no penetration");
        // cavalry penetrates too; artillery does not
        var cav = DefenderLine();
        cav.Cav(150f, -150f);
        AssertEqual(1, Penetrations(cav), "cavalry behind the line");
        var art = DefenderLine();
        art.Art(150f, -150f);
        AssertEqual(0, Penetrations(art), "a battery behind the line is not a breakthrough");
    }

    private static void DetPenShallow()
    {
        var fx = DefenderLine();
        fx.Inf(150f, -60f);
        AssertEqual(0, Penetrations(fx), "60 m behind the trace is the line's own close fight");
        var edge = DefenderLine();
        edge.Inf(150f, -100f);
        AssertEqual(1, Penetrations(edge), "exactly 100 m behind counts");
    }

    private static void DetPenMelee()
    {
        var fx = DefenderLine();
        fx.Inf(150f, -150f, melee: true);
        AssertEqual(0, Penetrations(fx), "a sample in melee is a fight, not a breakthrough");
        // Within 50 m of an own front-line sample: a steep trace A(0,0) C(30,-190) B(60,0), widths 20.
        var steep = new DetFixture();
        steep.OwnInf(0f, 0f, 20f);
        steep.OwnInf(30f, -190f, 20f);
        steep.OwnInf(60f, 0f, 20f);
        steep.Inf(45f, -200f);
        AssertEqual(0, Penetrations(steep), "within 50 m of an own front-line sample (clear rule)");
        var open = new DetFixture();
        open.OwnInf(0f, 0f, 20f);
        open.OwnInf(30f, -190f, 20f);
        open.OwnInf(60f, 0f, 20f);
        open.Inf(45f, -250f);
        AssertEqual(1, Penetrations(open), "the same line with the sample 60 m from C is a penetration");
    }

    private static void DetPenRemembered()
    {
        var fx = DefenderLine();
        fx.Remembered(TacticalUnitType.Infantry, 150f, -150f, confidence: 0.9f);
        AssertEqual(0, Penetrations(fx), "a remembered sample never qualifies");
    }

    private static void DetPenContact()
    {
        // The enemy line in close contact with an irregular own line: samples up to 40 m behind the trace, some in melee.
        var fx = new DetFixture();
        fx.OwnInf(0f, 0f);
        fx.OwnInf(100f, -30f);
        fx.OwnInf(200f, 10f);
        fx.OwnInf(300f, -20f);
        fx.Inf(20f, -40f, melee: true);
        fx.Inf(110f, -60f);
        fx.Inf(210f, -25f, melee: true);
        fx.Inf(300f, -55f);
        // Clear of every own sample (81 m or more) and not in melee, 95 m behind the trace (the 200 m regiment's footprint reaches
        // lateral 160): only the 100 m minimum depth keeps it out.
        fx.Inf(160f, -85f);
        var d = new OpeningDetector();
        Settle(d, fx, defender: true);
        AssertEqual(0, CountKind(d, OpeningKind.Penetration), "close contact alone is no counterstroke trigger");
    }

    private static void DetPenBridge()
    {
        // Own units at lateral 0 and 400 (edges 50 and 350: a 300 m gap): no trace across it.
        var fx = new DetFixture();
        fx.OwnInf(0f, 0f);
        fx.OwnInf(400f, 0f);
        fx.Inf(200f, -150f);
        AssertEqual(0, Penetrations(fx), "no trace across a gap longer than 150 m");
        var near = new DetFixture();
        near.OwnInf(0f, 0f);
        near.OwnInf(250f, 0f);   // edges 50 and 200: exactly 150 m, bridged
        near.Inf(125f, -150f);
        AssertEqual(1, Penetrations(near), "a gap of exactly 150 m is bridged");
        var wide = new DetFixture();
        wide.OwnInf(0f, 0f);
        wide.OwnInf(252f, 0f);   // 152 m: not bridged
        wide.Inf(126f, -150f);
        AssertEqual(0, Penetrations(wide), "152 m is not");
    }

    private static void CounterBounds()
    {
        var fx = DefenderLine();
        fx.Inf(150f, -420f);
        AssertEqual(0, Penetrations(fx), "420 m behind the trace is beyond the counterstroke bound");
        var ok = DefenderLine();
        ok.Inf(150f, -390f);
        AssertEqual(1, Penetrations(ok), "390 m behind is within it");
    }

    // ------------------------------------------------------------------ the Counterstroke task

    private static void CounterTask()
    {
        AssertEqual(CommandTaskType.Counterstroke, TacticalDecisionDoctrine.TaskForStance(CommandTaskType.Counterstroke, BattleStance.Defender),
            "the Defender guard passes the Counterstroke task unchanged");
        AssertEqual(CommandTaskType.Counterstroke, TacticalDecisionDoctrine.TaskForStance(CommandTaskType.Counterstroke, BattleStance.Attacker), "and for an Attacker");
        AssertEqual(TacticalCommandTaskToStanceMapper.StanceAttack, TacticalCommandTaskToStanceMapper.Map(CommandTaskType.Counterstroke), "stance 3");
        foreach (var stance in new[] { BattleStance.Meeting, BattleStance.Attacker, BattleStance.Defender })
            AssertEqual(-1, TacticalCommandTaskToStanceMapper.MapForGroupStanceScorer(CommandTaskType.Counterstroke, true, stance),
                "the #45 scorer has no opinion on Counterstroke (" + stance + ")");
        var state = new CommandNodeOperationalState("node-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.Reserve, CommandTaskType.Counterstroke, CommandTaskState.Committed, 0f, 0f, 0f);
        var order = default(CommandDoctrineOrder);
        AssertFalse(PostureStanceGuard.Apply(BattleStance.Defender, ref state, false, ref order), "PostureStanceGuard leaves Counterstroke alone");
        AssertEqual(CommandTaskType.Counterstroke, state.Task, "task kept");
    }

    // Only the exploit override creates the task. The mapper names it to give the stance, and the exploit lifecycle (task 6)
    // builds the order; no ledger, director, play, cascade or recon path may. The bare word also names strategic enum
    // members such as OperationPosture.Counterstroke, so the pin matches the qualified name.
    private static void CounterPin()
    {
        string root = TestFixtures.FindRepoPath("src/WhiskeyRealism");
        var offenders = new List<string>();
        foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            if (name == "TacticalCommandTaskToStanceMapper.cs" || name == "ExploitLifecycle.cs") continue;
            if (File.ReadAllText(file).Contains("CommandTaskType.Counterstroke")) offenders.Add(name);
        }
        AssertEqual(0, offenders.Count, "CommandTaskType.Counterstroke named outside the mapper and the exploit lifecycle: " + string.Join(",", offenders));
    }
}
