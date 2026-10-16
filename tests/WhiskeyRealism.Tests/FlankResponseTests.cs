using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// BC11 (docs/superpowers/plans/2026-10-15-battle-core.md § BC11): flank response and
// envelopment execution. Sections: pressure facing (decision 1), the refused flank reaching
// vanilla (decision 2, red first: the two combined tests failed on the shipped policy), the
// reserve answering a threatened flank (decision 3), the envelopment maneuver (decision 4,
// FlankResponseTests.Maneuver.cs), the flank tally patch (decision 6, FlankResponseTests.Tally.cs).
internal static partial class FlankResponseTests
{
    public static void Run()
    {
        RunAll(Concat(Concat(Concat(FacingAndRefuseTests(), ManeuverTests()), TallyTests()), ReviewTests()));
    }

    private static (string, Action)[] FacingAndRefuseTests()
    {
        return new (string, Action)[]
        {
            // pressure facing
            ("bc11 PressureWindowPicksFlankOverCloserFrontUnit", PressureWindowPicksFlankOverCloserFrontUnit),
            ("bc11 PressureHysteresisKeepsFrontBelow1_25", PressureHysteresisKeepsFrontBelow1_25),
            ("bc11 NoPressureFallsBackToClosestVisible", NoPressureFallsBackToClosestVisible),
            ("bc11 RegimentPulseStillClosestVisible", RegimentPulseStillClosestVisible),
            ("bc11 BinCentresMatchVanillaWidth", BinCentresMatchVanillaWidth),
            ("bc11 PressureWindowWrapsAroundNorth", PressureWindowWrapsAroundNorth),
            ("bc11 FacingSourceIsWiredAndFlagged", FacingSourceIsWiredAndFlagged),
            // refuse reaches vanilla
            ("bc11 RefuseReachesVanillaOutOfCombat", RefuseReachesVanillaOutOfCombat),
            ("bc11 RefuseReachesVanillaInCombat", RefuseReachesVanillaInCombat),
            ("bc11 RefuseRotationsMatchSkewedPosition", RefuseRotationsMatchSkewedPosition),
            ("bc11 FacingOnlyWheelStillRefusedWithEnemiesInRange", FacingOnlyWheelStillRefusedWithEnemiesInRange),
            ("bc11 RefuseInPlaceNeverPlayerOwnedOrDetached", RefuseInPlaceNeverPlayerOwnedOrDetached),
            ("bc11 RefuseInPlaceSkipsRegimentWhoseClosestEnemyIsInFront", RefuseInPlaceSkipsRegimentWhoseClosestEnemyIsInFront),
            ("bc11 RefuseInPlaceKeepsRegimentWithClosestEnemyOnFlank", RefuseInPlaceKeepsRegimentWithClosestEnemyOnFlank),
            ("bc11 RefuseInPlaceSkipsUnderFireRegimentWithoutFlankEnemy", RefuseInPlaceSkipsUnderFireRegimentWithoutFlankEnemy),
            ("bc11 RefuseInPlaceTakesFourDeepestSetbacksFirst", RefuseInPlaceTakesFourDeepestSetbacksFirst),
            ("bc11 RefuseWingShareExcludesTheShallowEnd", RefuseWingShareExcludesTheShallowEnd),
            ("bc11 RefuseInPlaceSkipsRegimentAlreadyOnTheSkewedLine", RefuseInPlaceSkipsRegimentAlreadyOnTheSkewedLine),
            ("bc11 RefuseSideFollowsFlankPressureNotClosestEnemy", RefuseSideFollowsFlankPressureNotClosestEnemy),
            ("bc11 RefuseNeedsEngagementOrFlankPressure", RefuseNeedsEngagementOrFlankPressure),
            ("bc11 RefuseGeometryRejectsFrontalPressure", RefuseGeometryRejectsFrontalPressure),
            ("bc11 RefuseDueOverridesOnlyAlreadyCorrect", RefuseDueOverridesOnlyAlreadyCorrect),
            ("bc11 RefuseRuntimeIsWired", RefuseRuntimeIsWired),
            // a reserve answers a threatened flank
            ("bc11 ThreatenedWingReleasesReserveToRefuse", ThreatenedWingReleasesReserveToRefuse),
            ("bc11 NoFlankReleaseWithoutContact", NoFlankReleaseWithoutContact),
            ("bc11 FlankReleaseIsPerWingAndNearestReserve", FlankReleaseIsPerWingAndNearestReserve),
            // zero allocation of the per-tick paths
            ("bc11 FlankResponseAllocatesNothingAfterWarmup", FlankResponseAllocatesNothingAfterWarmup),
        };
    }

    private const int Brigade = 100, Left1 = 1, Left2 = 2, Right1 = 3, Right2 = 4, EnemyId = 900;

    // Zero allocation after warm-up, read without runtime noise (BattleMovementHonestyTests.AssertNoAllocation): at least
    // six of eight windows of `calls` invocations allocate nothing.
    private static void AssertNoAllocation(Action work, int calls, string label)
    {
        const int windows = 8, cleanRequired = 6;
        for (int k = 0; k < 30; k++) work();
        int clean = 0;
        long worst = 0L;
        for (int window = 0; window < windows; window++)
        {
            long before = TestFixtures.BeginAllocationWindow();
            for (int k = 0; k < calls; k++) work();
            long bytes = TestFixtures.AllocatedSince(before);
            if (bytes == 0L) clean++;
            else if (bytes > worst) worst = bytes;
        }
        AssertTrue(clean >= cleanRequired, label + ": " + clean + " of " + windows + " windows were clean, worst window " + worst + " bytes");
    }

    private sealed class RecordingSink : IIntentSink
    {
        public readonly List<BattleOrderIntent> Intents = new List<BattleOrderIntent>();
        public void Submit(in BattleOrderIntent intent) { Intents.Add(intent); }
    }

    private sealed class AllowGuard : IOrderGuard
    {
        public bool CanWrite(in UnitRecord unit, out SkipReason reason) { reason = SkipReason.None; return true; }
    }

    // ------------------------------------------------------------------ fixtures

    // (bearing from +z clockwise, distance, strength) to the world position of an enemy around (0, 0).
    private static (float x, float z, float strength) Enemy(float bearing, float distance, float strength)
    {
        double r = bearing * Math.PI / 180.0;
        return ((float)(Math.Sin(r) * distance), (float)(Math.Cos(r) * distance), strength);
    }

    // A brigade at the origin facing +z (0 = +z, 90 = +x), four regiments in a line across it (x = -90, -30, 30, 90, each
    // 60 m wide, FireRange 150), the given visible enemies, perception built with `slices` bins.
    private static BattleSnapshot FixtureWith(int slices, params (float x, float z, float strength)[] enemies)
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        s.SetSliceCount(slices);
        UnitFlags live = UnitFlags.Alive | UnitFlags.Active;
        s.AddOwn(new UnitRecord
        {
            InstanceId = Brigade, UnitTyp = TacticalUnitType.BattleGroupBrigade, X = 0f, Z = 0f, RotationY = 0f,
            Formation = 0, GroupWidth = 240f, GroupDepth = 60f, Flags = live | UnitFlags.IsGroup,
        });
        int[] ids = { Left1, Left2, Right1, Right2 };
        float[] xs = { -90f, -30f, 30f, 90f };
        for (int i = 0; i < ids.Length; i++)
        {
            s.AddOwn(new UnitRecord
            {
                InstanceId = ids[i], UnitTyp = TacticalUnitType.Infantry, X = xs[i], Z = 0f, RotationY = 0f, FireRange = 150f,
                SlotOffsetX = xs[i], UnitWidth = 60f, Flags = live | UnitFlags.IsInfantry | UnitFlags.HasLayoutSlot,
            });
            s.Own[i + 1].ParentIndex = 0;
        }
        s.IndexCommandTree(null);
        for (int e = 0; e < enemies.Length; e++)
        {
            s.AddObservation(new EnemyObservation
            {
                InstanceId = EnemyId + e, UnitTyp = TacticalUnitType.Infantry, X = enemies[e].x, Z = enemies[e].z, Strength = enemies[e].strength,
                State = ObservationState.Visible, Confidence01 = 1f,
            });
        }
        SlicePressureBuilder.Build(s, new SliceGeometry { MaxRangeMeters = 250f, FlankShiftMeters = 150f });
        return s;
    }

    private static BattleSnapshot BrigadeFixture(float enemyX = -200f, float enemyZ = 0f, float enemyStrength = 500f)
    {
        return FixtureWith(20, (enemyX, enemyZ, enemyStrength));
    }

    private static float AngleDiff(float a, float b)
    {
        float d = Math.Abs(a - b) % 360f;
        return d > 180f ? 360f - d : d;
    }

    // ------------------------------------------------------------------ decision 1: pressure facing

    // The closest enemy is ahead at 120 m; three times its strength stands on the left at 220 m. Vanilla's per-regiment rule
    // (and the shipped facing) follows the closest enemy; the pressure window follows the mass.
    private static void PressureWindowPicksFlankOverCloserFrontUnit()
    {
        BattleSnapshot s = FixtureWith(20, Enemy(0f, 120f, 500f), Enemy(270f, 220f, 1500f));
        int k = s.ClosestVisibleEnemy(0, out float distance);
        AssertTrue(k >= 0 && distance < 130f, "the closest visible enemy is the front one");
        AssertNear(0f, PostureSense.HeadingTo(0f, 0f, s.Observations[k].X, s.Observations[k].Z), 0.5f, "the shipped facing would stay on the closest enemy");

        AssertTrue(FlankFacing.MostPressuredBearing(s, 0, currentFacing: 0f, out float bearing, out float share), "pressure within the slice range");
        AssertNear(270f, bearing, 0.5f, "the window of the mass: left");
        AssertTrue(share < FlankFacing.KeepShare, "the front window is under 0.8 of the best, so the facing turns");
    }

    private static void PressureHysteresisKeepsFrontBelow1_25()
    {
        // Front 100, left 124: the best window is 1.24 x the front window, under 1.25 -> keep the current facing.
        BattleSnapshot keep = FixtureWith(20, Enemy(0f, 120f, 100f), Enemy(270f, 220f, 124f));
        AssertTrue(FlankFacing.MostPressuredBearing(keep, 0, 0f, out float bearing, out float share), "pressure");
        AssertNear(0f, bearing, 0.01f, "1.24 x the front: keep facing the front");
        AssertTrue(share >= FlankFacing.KeepShare, "share " + share + " is at least 0.8");

        // Left 126: 1.26 x -> turn.
        BattleSnapshot turn = FixtureWith(20, Enemy(0f, 120f, 100f), Enemy(270f, 220f, 126f));
        AssertTrue(FlankFacing.MostPressuredBearing(turn, 0, 0f, out bearing, out share), "pressure");
        AssertNear(270f, bearing, 0.5f, "1.26 x the front: turn left");
        AssertTrue(share < FlankFacing.KeepShare, "share " + share + " is under 0.8");
    }

    // No pressure within aidefensivemaxrange (250 m): the slice window has nothing, the closest visible enemy decides as before.
    private static void NoPressureFallsBackToClosestVisible()
    {
        BattleSnapshot s = FixtureWith(20, Enemy(270f, 400f, 900f));
        AssertFalse(FlankFacing.MostPressuredBearing(s, 0, 0f, out float bearing, out _), "400 m is outside the 250 m slice range");
        AssertEqual(-1f, bearing, "no bearing is claimed");
        AssertTrue(s.ClosestVisibleEnemy(0, out float distance) >= 0 && distance > 390f, "the closest visible enemy is still there to fall back to");
        string producers = SourceText("Tactical", "Battle", "Producers", "PostureIntentProducer.cs");
        string method = Between(producers, "internal static bool TryFacingBearing(", "internal static bool TryThreat(");
        AssertPinned(method, "if (!TryThreat(s, i, out float x, out float z)) return false;", "falls back to the closest visible enemy");
        AssertPinned(method, "source = ClosestFacingSource;", "and says so");
    }

    // Vanilla turns a regiment to its own closest enemy (StopMicroUnitsCloseToEnemyAndRotateTowards 4539-4613): the regiment
    // pulse keeps that rule, only the brigade and division scopes read the pressure window.
    private static void RegimentPulseStillClosestVisible()
    {
        string producers = SourceText("Tactical", "Battle", "Producers", "PostureIntentProducer.cs");
        string input = Between(producers, "private static TacticalFacingPulseInput Input(", "private static TacticalFacingPulseScope ScopeFor(");
        AssertPinned(input, "if (hasThreat && scope != TacticalFacingPulseScope.Regiment && Plugin.FlankResponseEnabled() &&", "pressure only above the regiment scope");
        AssertPinned(input, "FlankFacing.MostPressuredBearing(s, u, rec.RotationY, out float pressureBearing, out _))", "with hysteresis on the unit's facing");
        int scopeTest = input.IndexOf("scope != TacticalFacingPulseScope.Regiment", StringComparison.Ordinal);
        int pressureRead = input.IndexOf("MostPressuredBearing", StringComparison.Ordinal);
        AssertTrue(scopeTest >= 0 && scopeTest < pressureRead, "scope test first");
    }

    // Bin j is centred on j x w + w / 2 (MostEnemiesAreOutsideAngle 4800); the shipped 20 fills 18 bins.
    private static void BinCentresMatchVanillaWidth()
    {
        BattleSnapshot s = FixtureWith(20, Enemy(105f, 100f, 400f));
        FlankFacing.MostPressuredBearing(s, 0, 200f, out float bearing, out _);
        AssertNear(110f, bearing, 0.01f, "20 degree bins: bearing 105 is bin 5, centred on 110");

        BattleSnapshot last = FixtureWith(20, Enemy(355f, 100f, 400f));
        FlankFacing.MostPressuredBearing(last, 0, 180f, out bearing, out _);
        AssertNear(350f, bearing, 0.01f, "the last of the 18 bins is centred on 350");

        BattleSnapshot wide = FixtureWith(36, Enemy(100f, 100f, 400f));
        FlankFacing.MostPressuredBearing(wide, 0, 0f, out bearing, out _);
        AssertNear(90f, bearing, 0.01f, "36 degree bins (10 of them): bearing 100 is bin 2, centred on 90");
    }

    // The window's mean is circular: 355 and 5 degrees average to north, not to south.
    private static void PressureWindowWrapsAroundNorth()
    {
        BattleSnapshot s = FixtureWith(20, Enemy(355f, 120f, 300f), Enemy(5f, 120f, 300f));
        AssertTrue(FlankFacing.MostPressuredBearing(s, 0, 180f, out float bearing, out _), "pressure");
        AssertTrue(AngleDiff(0f, bearing) < 0.5f, "the circular mean of 350 and 10 is 0, got " + bearing);
    }

    private static void FacingSourceIsWiredAndFlagged()
    {
        string producers = SourceText("Tactical", "Battle", "Producers", "PostureIntentProducer.cs");
        string method = Between(producers, "internal static bool TryFacingBearing(", "internal static bool TryThreat(");
        AssertPinned(method, "Plugin.FlankResponseEnabled() && FlankFacing.MostPressuredBearing(s, i, currentFacing, out bearing, out _)", "the pressure read is behind the flag");
        AssertPinned(method, "source = PressureFacingSource;", "facingSource pressure");
        AssertPinned(producers, ".WithField(\"facingSource\", facingSource ?? PostureTargets.NoFacingSource)", "telemetry carries facingSource");
        AssertPinned(producers, "PostureTargets.TryFacingBearing(s, i, rec.RotationY, out heading, out facingSource))", "the posture pipeline uses it");
    }

    // ------------------------------------------------------------------ decision 2: the refused flank reaches vanilla

    // RefuseFor -> PostureIntentPlan.Submit -> the formation intent, as the posture pipeline builds it.
    private static BattleOrderIntent RefuseFormationIntent(BattleSnapshot s, CommandTaskType task)
    {
        bool closeEngaged = PostureSense.CloseEngaged(s, 0, UnitFlags.None);
        RefuseFlank refuse = PostureSense.RefuseFor(s, 0, task, closeEngaged, flankRisk: true);
        var sink = new RecordingSink();
        PostureIntentPlan.Submit(sink, new PostureIntentRequest
        {
            GroupId = Brigade, Action = PostureExecutionAction.SetFormation, Task = task,
            TargetFormation = CommandFormationCorrection.TargetFormationForTask(task, 0), NeedsFormation = false,
            Refuse = refuse, Priority = IntentPriority.Executor, Source = IntentSource.PostureExecutor, Reason = "posture-executor",
        });
        AssertEqual(1, sink.Intents.Count, "one formation intent");
        return sink.Intents[0];
    }

    // The executor's formation verdict for the group (OrderExecutor.DecideFormation, what OrderExecutorRuntime.IssueFormation issues).
    private static FormationWriteDecision DecideFormation(BattleSnapshot s, in BattleOrderIntent w, float combatStatusSubordinates)
    {
        // OrderExecutorRuntime.GroupRangeReach is runtime-only; vanilla's reach is at least the 250 m close-contact range.
        float enemiesInRange = BattleWriterPolicy.SnapshotEnemyStrengthInRange(s, 0, 250f);
        return OrderExecutor.DecideFormation(w, s.Own[0].Formation, combatStatusSubordinates, 0f, 0, 0f, 2.0f, -1f, enemiesInRange);
    }

    // Out of combat (combatstatussubordinates 0) with a visible enemy 200 m off the left flank: the refused flank
    // reaches vanilla as the wheel with refuseflank 0 (left). Red on the shipped policy: the 200 m enemy counts
    // as "enemies in range", so the policy returned None and the plan carried -1.
    private static void RefuseReachesVanillaOutOfCombat()
    {
        BattleSnapshot s = BrigadeFixture();
        BattleOrderIntent w = RefuseFormationIntent(s, CommandTaskType.HoldObjective);
        AssertEqual(RefuseFlank.Left, w.Refuse, "the posture sense refuses the left flank");
        AssertEqual(0, OrderExecutor.VanillaRefuseFlank(w.Refuse), "refuseflank 0 (left)");
        FormationWriteDecision d = DecideFormation(s, w, combatStatusSubordinates: 0f);
        AssertEqual("WheelGroup", d.Kind.ToString(), "the refuse wheel is issued whatever the enemies in reach (" + d.Reason + ")");
    }

    // In combat the refuse is the limited in-place rotation (never a re-lay): the left-wing regiments at skewed rotations,
    // the right wing untouched. Red on the shipped policy: combat-no-relay for a refuse-only write.
    private static void RefuseReachesVanillaInCombat()
    {
        BattleSnapshot s = BrigadeFixture();
        BattleOrderIntent w = RefuseFormationIntent(s, CommandTaskType.HoldObjective);
        FormationWriteDecision d = DecideFormation(s, w, combatStatusSubordinates: 1f);
        AssertEqual("RefuseInPlace", d.Kind.ToString(), "in combat the refuse rotates the wing in place (" + d.Reason + ")");

        MicroTurnRule rule = ShippedRule;
        var queue = new int[16]; var regiments = new int[16]; var headings = new float[16];
        int count = FlankFacing.RefuseRotations(s, 0, w.Refuse, 240f, 0.5f, 4f, in rule, queue, regiments, headings, out int skipped);
        AssertEqual(2, count, "the two left-wing regiments");
        AssertEqual(0, skipped, "the enemy is 110 m off: vanilla's 40 m micro turn would not undo them");
        var rotated = new HashSet<int>();
        for (int k = 0; k < count; k++) rotated.Add(s.Own[regiments[k]].InstanceId);
        AssertTrue(rotated.Contains(Left1) && rotated.Contains(Left2), "left wing rotated");
        AssertFalse(rotated.Contains(Right1) || rotated.Contains(Right2), "right wing untouched");
        for (int k = 0; k < count; k++)
        {
            float x = s.Own[regiments[k]].SlotOffsetX;
            AssertNear(0f, AngleDiff(ExpectedRotation(240f, 0.5f, 4f, x, 60f, left: true), headings[k]), 0.05f, "skewed rotation of the regiment at x=" + x);
            AssertTrue(AngleDiff(0f, headings[k]) > OrderExecutor.FacingToleranceDegrees, "a real turn");
        }
        AssertEqual(Left1, s.Own[regiments[0]].InstanceId, "the deepest setback goes first");
    }

    private static MicroTurnRule ShippedRule => MicroTurnRule.From(40f, 25f, 15f, 0.5f);

    // SkewedPosition (78109-78168) derived directly: the chord between the unit's left and right edge, each pushed
    // back by pow(t, skew) x depth, facing 0 so the chord lies on the x axis; rotation = atan2(width, depthLeft - depthRight) - 90.
    private static float ExpectedRotation(float groupWidth, float depthFactor, float skew, float x, float unitWidth, bool left)
    {
        double refuseDepth = groupWidth * depthFactor;
        double tl = (x - unitWidth / 2.0 + groupWidth / 2.0) / groupWidth;
        double tr = (x + unitWidth / 2.0 + groupWidth / 2.0) / groupWidth;
        if (left) { tl = 1.0 - tl; tr = 1.0 - tr; }
        double dl = Math.Pow(tl, skew) * refuseDepth;
        double dr = Math.Pow(tr, skew) * refuseDepth;
        double degrees = Math.Atan2(unitWidth, dl - dr) * 180.0 / Math.PI - 90.0;
        degrees %= 360.0;
        if (degrees < 0) degrees += 360.0;
        return (float)degrees;
    }

    // Fixture at t = 0, 0.5, 1 (the unit centre across the group's frontage) with skew 4 and depth 0.5, left and right refuse.
    private static void RefuseRotationsMatchSkewedPosition()
    {
        foreach (float t in new[] { 0f, 0.5f, 1f })
        {
            float x = (t - 0.5f) * 240f;
            foreach (bool left in new[] { true, false })
            {
                float expected = ExpectedRotation(240f, 0.5f, 4f, x, 60f, left);
                float actual = FlankFacing.SkewedRotation(0f, x, 60f, 240f, 120f, left ? RefuseFlank.Left : RefuseFlank.Right, 4f);
                AssertNear(0f, AngleDiff(expected, actual), 0.05f, "t=" + t + " " + (left ? "left" : "right") + ": expected " + expected + " got " + actual);
            }
        }
        // Mirror symmetry: the right flank's rotation at +x is the left flank's at -x, reflected.
        float l = FlankFacing.SkewedRotation(0f, -90f, 60f, 240f, 120f, RefuseFlank.Left, 4f);
        float r = FlankFacing.SkewedRotation(0f, 90f, 60f, 240f, 120f, RefuseFlank.Right, 4f);
        AssertNear(0f, AngleDiff(360f - l, r), 0.05f, "mirror image");
        // The setback is SkewedPosition's: pow(1 - t, skew) x width x depth for the left flank (LeafSlotTarget.RefusedSetback).
        AssertNear((float)(Math.Pow(1.0 - 0.125, 4.0) * 120.0), FlankFacing.Setback(-90f, 240f, 0.5f, 4f, RefuseFlank.Left), 0.01f, "left setback");
        // A group turned to face east lays the same line rotated: the rotation adds the group's heading.
        float east = FlankFacing.SkewedRotation(90f, -90f, 60f, 240f, 120f, RefuseFlank.Left, 4f);
        AssertNear(0f, AngleDiff((l + 90f) % 360f, east), 0.05f, "rotated with the group");
    }

    private static void FacingOnlyWheelStillRefusedWithEnemiesInRange()
    {
        var facing = CommandFormationWritePolicy.Decide(new FormationWriteInput(false, true, false, 0f, 0f, 0, 0f, 2f, -1f, 0.4f));
        AssertEqual(FormationWriteKind.None, facing.Kind, "a facing-only wheel re-paths every child: not with enemies in range");
        AssertEqual("enemies-in-range", facing.Reason, "reason");
        var refuse = CommandFormationWritePolicy.Decide(new FormationWriteInput(false, false, true, 0f, 0f, 0, 0f, 2f, -1f, 0.4f));
        AssertEqual(FormationWriteKind.WheelGroup, refuse.Kind, "the refused flank wheels");
        var both = CommandFormationWritePolicy.Decide(new FormationWriteInput(false, true, true, 0f, 0f, 0, 0f, 2f, -1f, 0.4f));
        AssertEqual(FormationWriteKind.WheelGroup, both.Kind, "a facing with a refused flank wheels too");
        var moving = CommandFormationWritePolicy.Decide(new FormationWriteInput(false, false, true, 0f, 0.5f, 0, 0f, 2f, -1f, 0f));
        AssertEqual(FormationWriteKind.None, moving.Kind, "vanilla re-forms only a settled group: never while it moves");
        var inCombatFacing = CommandFormationWritePolicy.Decide(new FormationWriteInput(false, true, false, 0.5f, 0f, 0, 0f, 2f, -1f, 0f));
        AssertEqual(FormationWriteKind.RotateInPlace, inCombatFacing.Kind, "a facing-only write in combat is the shipped rotation");
        var inCombatRefuseFacing = CommandFormationWritePolicy.Decide(new FormationWriteInput(false, true, true, 0.5f, 0f, 0, 0f, 2f, -1f, 0f));
        AssertEqual(FormationWriteKind.RefuseInPlace, inCombatRefuseFacing.Kind, "a refused facing in combat keeps the refuse");
    }

    private static void RefuseInPlaceNeverPlayerOwnedOrDetached()
    {
        BattleSnapshot s = BrigadeFixture();
        s.Own[1].Flags |= UnitFlags.PlayerOwned;
        s.Own[2].Flags |= UnitFlags.PermanentlyDetached;
        MicroTurnRule rule = ShippedRule;
        var queue = new int[16]; var regiments = new int[16]; var headings = new float[16];
        AssertEqual(0, FlankFacing.RefuseRotations(s, 0, RefuseFlank.Left, 240f, 0.5f, 4f, in rule, queue, regiments, headings, out _),
            "a player-owned and a detached wing regiment are never rotated");

        // The group itself: a player-owned group is refused by the executor's guard before any write is planned.
        BattleSnapshot owned = BrigadeFixture();
        owned.Own[0].Flags |= UnitFlags.PlayerOwned;
        var executor = new OrderExecutor();
        executor.Bind(new AllowGuard(), null);
        BattleOrderIntent w = RefuseFormationIntent(owned, CommandTaskType.HoldObjective);
        ExecutionPlan plan = executor.Plan(owned, w, 2.0);
        AssertFalse(plan.Execute, "a player-owned group is not planned");
        AssertEqual(SkipReason.PlayerOwned, plan.Skip, "the executor never moves the player's command");
    }

    private static void RefuseInPlaceSkipsRegimentWhoseClosestEnemyIsInFront()
    {
        // 30 m straight ahead of the left-most regiment: vanilla turns it back (angle ~54 degrees from the skewed rotation).
        BattleSnapshot s = FixtureWith(20, (-90f, 30f, 500f));
        MicroTurnRule rule = ShippedRule;
        var queue = new int[16]; var regiments = new int[16]; var headings = new float[16];
        int count = FlankFacing.RefuseRotations(s, 0, RefuseFlank.Left, 240f, 0.5f, 4f, in rule, queue, regiments, headings, out int skipped);
        AssertTrue(skipped >= 1, "the regiment with its enemy in front is skipped, skipped=" + skipped);
        for (int k = 0; k < count; k++)
            AssertTrue(s.Own[regiments[k]].InstanceId != Left1, "Left1 keeps vanilla's facing to its enemy");
        AssertTrue(FlankFacing.VanillaTurnsBack(s, 1, 306f, in rule), "vanilla's micro turn would undo the skewed rotation");
    }

    private static void RefuseInPlaceKeepsRegimentWithClosestEnemyOnFlank()
    {
        // The left-most regiment's skewed rotation is ~306 degrees; an enemy 30 m away at bearing 310 is within 25 degrees of it.
        double r = 310.0 * Math.PI / 180.0;
        BattleSnapshot s = FixtureWith(20, (-90f + (float)Math.Sin(r) * 30f, (float)Math.Cos(r) * 30f, 500f));
        MicroTurnRule rule = ShippedRule;
        var queue = new int[16]; var regiments = new int[16]; var headings = new float[16];
        int count = FlankFacing.RefuseRotations(s, 0, RefuseFlank.Left, 240f, 0.5f, 4f, in rule, queue, regiments, headings, out int skipped);
        bool left1 = false;
        for (int k = 0; k < count; k++) if (s.Own[regiments[k]].InstanceId == Left1) left1 = true;
        AssertTrue(left1, "an enemy already on the flank is within the trigger angle: the regiment is turned, not skipped");
        AssertEqual(0, skipped, "nothing skipped");
        AssertFalse(FlankFacing.VanillaTurnsBack(s, 1, 306f, in rule), "vanilla leaves it");
    }

    private static void RefuseInPlaceSkipsUnderFireRegimentWithoutFlankEnemy()
    {
        // An enemy 100 m ahead of the left-most regiment: out of the 40 m reach while it is not under fire...
        BattleSnapshot calm = FixtureWith(20, (-90f, 100f, 500f));
        MicroTurnRule rule = ShippedRule;
        AssertFalse(FlankFacing.VanillaTurnsBack(calm, 1, 306f, in rule), "not under fire: 100 m is outside distancetoturntowardsenemy");
        // ...inside the fire-range reach once the regiment has received fire.
        BattleSnapshot fire = FixtureWith(20, (-90f, 100f, 500f));
        fire.Own[1].ReceivedFire = true;
        AssertTrue(FlankFacing.VanillaTurnsBack(fire, 1, 306f, in rule), "under fire: the reach is its fire range, the enemy is in front");
        var queue = new int[16]; var regiments = new int[16]; var headings = new float[16];
        int count = FlankFacing.RefuseRotations(fire, 0, RefuseFlank.Left, 240f, 0.5f, 4f, in rule, queue, regiments, headings, out int skipped);
        AssertTrue(skipped >= 1, "skipped");
        for (int k = 0; k < count; k++) AssertTrue(fire.Own[regiments[k]].InstanceId != Left1, "Left1 is not turned");
        // In cover the trigger widens by additionalanglewhenincovermicro: 54 degrees stays over 25 + 15.
        fire.Own[1].CoverValue = 0.9f;
        fire.Own[1].CoverObject = 1;
        AssertTrue(FlankFacing.VanillaTurnsBack(fire, 1, 306f, in rule), "54 degrees is over 25 + 15");
        AssertFalse(FlankFacing.VanillaTurnsBack(fire, 1, 345f, in rule), "15 degrees is under the trigger");
    }

    private static void RefuseInPlaceTakesFourDeepestSetbacksFirst()
    {
        // Twelve regiments across 720 m: only four may rotate in one tick, the deepest setbacks first.
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        s.SetSliceCount(20);
        UnitFlags live = UnitFlags.Alive | UnitFlags.Active;
        s.AddOwn(new UnitRecord { InstanceId = Brigade, UnitTyp = TacticalUnitType.BattleGroupBrigade, GroupWidth = 720f, Flags = live | UnitFlags.IsGroup });
        for (int i = 0; i < 12; i++)
        {
            float x = -330f + i * 60f;
            s.AddOwn(new UnitRecord
            {
                InstanceId = 10 + i, UnitTyp = TacticalUnitType.Infantry, X = x, FireRange = 150f, SlotOffsetX = x, UnitWidth = 60f,
                Flags = live | UnitFlags.IsInfantry | UnitFlags.HasLayoutSlot,
            });
            s.Own[i + 1].ParentIndex = 0;
        }
        s.IndexCommandTree(null);
        SlicePressureBuilder.Build(s, new SliceGeometry { MaxRangeMeters = 250f, FlankShiftMeters = 150f });
        MicroTurnRule rule = ShippedRule;
        var queue = new int[32]; var regiments = new int[32]; var headings = new float[32];
        int count = FlankFacing.RefuseRotations(s, 0, RefuseFlank.Left, 720f, 0.5f, 2f, in rule, queue, regiments, headings, out _);
        AssertEqual(FlankFacing.MaxRefuseRotationsPerTick, count, "at most four per group per tick");
        var seen = new HashSet<int>();
        for (int k = 0; k < count; k++) seen.Add(s.Own[regiments[k]].InstanceId);
        AssertTrue(seen.Contains(10) && seen.Contains(11) && seen.Contains(12) && seen.Contains(13), "the four left-most regiments have the deepest setbacks");
        float previous = float.MaxValue;
        for (int k = 0; k < count; k++)
        {
            float setback = FlankFacing.Setback(s.Own[regiments[k]].SlotOffsetX, 720f, 0.5f, 2f, RefuseFlank.Left);
            AssertTrue(setback <= previous + 0.001f, "deepest first");
            previous = setback;
        }
    }

    // A regiment already within the executor's 15 degree facing tolerance of its skewed rotation is no rotation to spend one of
    // the four per-tick slots on (the executor would only report it as not rotated).
    private static void RefuseInPlaceSkipsRegimentAlreadyOnTheSkewedLine()
    {
        BattleSnapshot s = BrigadeFixture();
        float skewed = ExpectedRotation(240f, 0.5f, 4f, -90f, 60f, left: true);
        s.Own[1].RotationY = skewed + 10f;
        MicroTurnRule rule = ShippedRule;
        var queue = new int[16]; var regiments = new int[16]; var headings = new float[16];
        int count = FlankFacing.RefuseRotations(s, 0, RefuseFlank.Left, 240f, 0.5f, 4f, in rule, queue, regiments, headings, out int skipped);
        AssertEqual(1, count, "only the second left regiment still has a turn to make");
        AssertEqual(Left2, s.Own[regiments[0]].InstanceId, "Left2");
        AssertEqual(0, skipped, "being on the line is not a micro-turn skip");
    }

    // RefuseWingShare 0.1 of the refuse depth: with a linear skew (1.0) the shallow end still has a steep gradient, so only the
    // setback share keeps a regiment that barely moves back out of the rotation.
    private static void RefuseWingShareExcludesTheShallowEnd()
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        s.SetSliceCount(20);
        UnitFlags live = UnitFlags.Alive | UnitFlags.Active;
        s.AddOwn(new UnitRecord { InstanceId = Brigade, UnitTyp = TacticalUnitType.BattleGroupBrigade, GroupWidth = 240f, Flags = live | UnitFlags.IsGroup });
        float[] xs = { -90f, 100f };
        for (int i = 0; i < xs.Length; i++)
        {
            s.AddOwn(new UnitRecord
            {
                InstanceId = 10 + i, UnitTyp = TacticalUnitType.Infantry, X = xs[i], FireRange = 150f, SlotOffsetX = xs[i], UnitWidth = 60f,
                Flags = live | UnitFlags.IsInfantry | UnitFlags.HasLayoutSlot,
            });
            s.Own[i + 1].ParentIndex = 0;
        }
        s.IndexCommandTree(null);
        SlicePressureBuilder.Build(s, new SliceGeometry { MaxRangeMeters = 250f, FlankShiftMeters = 150f });
        float shallow = FlankFacing.Setback(100f, 240f, 0.5f, 1f, RefuseFlank.Left);
        AssertTrue(shallow > 0f && shallow < FlankFacing.RefuseWingShare * 120f, "the regiment at x=100 sets back " + shallow + " m, under 10% of the 120 m depth");
        AssertTrue(AngleDiff(0f, FlankFacing.SkewedRotation(0f, 100f, 60f, 240f, 120f, RefuseFlank.Left, 1f)) > OrderExecutor.FacingToleranceDegrees,
            "yet its skewed rotation is a real turn (so only the wing share excludes it)");
        MicroTurnRule rule = ShippedRule;
        var queue = new int[8]; var regiments = new int[8]; var headings = new float[8];
        int count = FlankFacing.RefuseRotations(s, 0, RefuseFlank.Left, 240f, 0.5f, 1f, in rule, queue, regiments, headings, out _);
        AssertEqual(1, count, "only the deep end rotates");
        AssertEqual(10, s.Own[regiments[0]].InstanceId, "the regiment at x=-90");
    }

    // The side follows the flank with more perceived pressure, not the closest enemy: a small regiment close on the right,
    // the mass on the left.
    private static void RefuseSideFollowsFlankPressureNotClosestEnemy()
    {
        BattleSnapshot s = FixtureWith(20, Enemy(90f, 100f, 100f), Enemy(270f, 200f, 1500f));
        AssertEqual(RefuseFlank.Left, FlankFacing.PressuredSide(s, 0), "left holds more flank pressure");
        int k = s.ClosestVisibleEnemy(0, out _);
        AssertTrue(s.Observations[k].X > 0f, "the closest visible enemy is on the right");
        AssertEqual(RefuseFlank.Left, PostureSense.RefuseFor(s, 0, CommandTaskType.HoldObjective, closeEngaged: true, flankRisk: true),
            "the refused side is the pressured one");
        AssertEqual(RefuseFlank.None, PostureSense.RefuseFor(s, 0, CommandTaskType.HoldObjective, closeEngaged: true, flankRisk: false), "no flank risk");
        AssertEqual(RefuseFlank.None, PostureSense.RefuseFor(s, 0, CommandTaskType.AttackObjective, closeEngaged: true, flankRisk: true), "attack task");
        // A tie names no side; the closest enemy's bearing decides (the nearer one is on the right).
        BattleSnapshot tie = FixtureWith(20, Enemy(90f, 100f, 800f), Enemy(270f, 110f, 800f));
        AssertEqual(RefuseFlank.None, FlankFacing.PressuredSide(tie, 0), "a flank-pressure tie names no side");
        AssertEqual(RefuseFlank.Right, PostureSense.RefuseFor(tie, 0, CommandTaskType.HoldObjective, closeEngaged: true, flankRisk: true),
            "on a tie the closest enemy's bearing decides (the right)");
    }

    private static void RefuseNeedsEngagementOrFlankPressure()
    {
        // 400 m out, no flank pressure and nobody within 250 m: not engaged, not refused.
        BattleSnapshot far = FixtureWith(20, Enemy(270f, 400f, 900f));
        AssertFalse(PostureSense.CloseEngaged(far, 0, UnitFlags.None), "not engaged");
        AssertEqual(RefuseFlank.None, PostureSense.RefuseFor(far, 0, CommandTaskType.HoldObjective, closeEngaged: false, flankRisk: true), "no engagement, no pressure");
        // 290 m out on the left: past the 250 m contact range but inside the left flank point's 150 m radius (150 + 140).
        BattleSnapshot flank = FixtureWith(20, Enemy(270f, 290f, 900f));
        AssertFalse(PostureSense.CloseEngaged(flank, 0, UnitFlags.None), "290 m is not close contact");
        AssertTrue(flank.FlankPressure(0, 0) > 0f, "the left flank point sees it");
        AssertEqual(RefuseFlank.Left, PostureSense.RefuseFor(flank, 0, CommandTaskType.HoldObjective, closeEngaged: false, flankRisk: true),
            "perceived flank pressure alone refuses the flank");
    }

    private static void RefuseGeometryRejectsFrontalPressure()
    {
        // Enemy 31 degrees off the facing, 117 m: inside the left flank point's radius but too frontal for a refuse.
        BattleSnapshot s = FixtureWith(20, (-60f, 100f, 900f));
        AssertTrue(s.FlankPressure(0, 0) > 0f, "the left flank point sees it");
        AssertEqual(RefuseFlank.None, PostureSense.RefuseFor(s, 0, CommandTaskType.HoldObjective, closeEngaged: true, flankRisk: true),
            "under 45 degrees off the facing is a frontal threat");
    }

    private static void RefuseDueOverridesOnlyAlreadyCorrect()
    {
        AssertTrue(PostureSense.RefusePromotesNoWrite("already-correct"), "a group already doing its task");
        AssertTrue(PostureSense.RefusePromotesNoWrite("already-valid"), "a task with nothing to write");
        foreach (string keep in new[] { "movement-in-progress", "order-pending", "routed", "player-protected", "recent-order", "legal-idle", "missing-ledger-assignment" })
            AssertFalse(PostureSense.RefusePromotesNoWrite(keep), keep + " keeps its verdict");
        AssertEqual("refuse-flank", PostureSense.RefusePromotedReason, "reason");
    }

    private static void RefuseRuntimeIsWired()
    {
        string producers = SourceText("Tactical", "Battle", "Producers", "PostureIntentProducer.cs");
        string run = Between(producers, "internal static bool Run(", "private static bool CanBypassStaleQueuedOrder(");
        AssertPinned(run, "(refuseExecutor == null || refuseExecutor.LastIssuedRefuse(rec.InstanceId) != refuse);", "a refuse already ordered on that side is not re-ordered");
        AssertPinned(run, "if (refuseDue && PostureSense.RefusePromotesNoWrite(decision.Reason))", "a due refuse overrides an already-correct verdict");
        AssertPinned(run, "Refuse = refuseDue ? refuse : RefuseFlank.None,", "the formation intent carries the refuse only when due");

        string runtime = SourceText("Tactical", "Battle", "OrderExecutorRuntime.cs");
        string formation = Between(runtime, "private static bool IssueFormation(", "// Group facing: in contact rotate");
        string facing = Between(runtime, "private static bool IssueFacing(", "// SetGroupFormation(current formation, rotation, newpath:false,");
        AssertPinned(formation, "case FormationWriteKind.RefuseInPlace: return RefuseInPlace(view, targetIndex, w, feudReplay);", "the formation channel issues it");
        AssertPinned(facing, "case FormationWriteKind.RefuseInPlace: return RefuseInPlace(view, targetIndex, w, feudReplay);", "the facing channel issues it");
        string inPlace = Between(runtime, "private static bool RefuseInPlace(", "// SetGroupFormation(current formation, rotation, newpath:false,");
        AssertPinned(inPlace, "TacticalWriteGuard.CanWrite(child, feudReplay)", "each rotated regiment passes the write guard");
        AssertPinned(inPlace, "RotateInPlace(child, _rotationHeadings[k])", "the one write is the shipped in-place rotation");
        AssertFalse(inPlace.Contains("NoteLayout("), "an in-place rotation lays out nothing and notes no layout");
        AssertFalse(inPlace.Contains("SetGroupFormation("), "no re-lay in combat");
    }

    // ------------------------------------------------------------------ decision 3: a reserve answers a threatened flank

    private static TacticalBattlePlan FlankPlan() =>
        new TacticalBattlePlan(BattlePlanId.GenericAggressive, BattlePhase.MainEffort, mainEffortSector: 2,
            fixingSectors: new int[0], screeningSectors: new int[0], reserveCommitTriggerOdds: 50f, ageSeconds: 0f, jitterSeed: 0);

    private static DirectChildSnapshot[] Children(int n)
    {
        var snaps = new DirectChildSnapshot[n];
        for (int i = 0; i < n; i++) snaps[i] = new DirectChildSnapshot("c" + i, "a", 15, 0, "Corps" + i, true);
        return snaps;
    }

    // c0 sector 0 (the left wing), c1 a deployed reserve at sector 1, c2 the main effort at sector 2, c3 sector 4 (the right wing).
    private static DirectChildEvidence[] WingEvidence(bool leftContact, int leftExposure, bool rightContact, int rightExposure)
    {
        return new[]
        {
            new DirectChildEvidence(3, 1, leftContact, 0, leftExposure, 0.8f),
            new DirectChildEvidence(3, 0, false, 1, 0, 0.7f, deployedReserve: true),
            new DirectChildEvidence(3, 1, true, 2, 0, 0.8f),
            new DirectChildEvidence(3, 1, rightContact, 4, rightExposure, 0.8f),
        };
    }

    private static IReadOnlyList<DirectChildIntent> Allocate(DirectChildEvidence[] evidence, ReserveReleaseContext release)
    {
        var personality = new PersonalityVector(0.2f, 0f, 0f, 0f, 0f);
        return DirectChildAllocator.AllocateWithZones(FlankPlan(), personality, Children(evidence.Length), evidence, null, null, release);
    }

    private static void ThreatenedWingReleasesReserveToRefuse()
    {
        var intents = Allocate(WingEvidence(true, 3, false, 0), new ReserveReleaseContext(0.5f));
        AssertEqual(DirectChildRole.RefuseLeft, intents[1].Role, "the reserve behind a threatened left wing guards its flank");
        AssertEqual(0, intents[1].AxisSector, "with the wing's sector as the axis");
        AssertEqual(DirectChildAxis.Hold, intents[1].Axis, "a holding role");

        var right = Allocate(WingEvidence(false, 0, true, 3), new ReserveReleaseContext(0.5f));
        AssertEqual(DirectChildRole.RefuseRight, right[1].Role, "a threatened right wing draws it to the right");
        AssertEqual(4, right[1].AxisSector, "right wing sector");

        // RefuseLeft/Right become Defender nodes with the GuardFlank task, a holding task a Defender keeps.
        AssertEqual(CommandTaskType.GuardFlank, TacticalDecisionDoctrine.TaskForStance(CommandTaskType.GuardFlank, BattleStance.Defender),
            "a Defender keeps GuardFlank");
        string nodes = SourceText("Tactical", "Orchestrator", "CommandNodeOperationsRuntime.cs");
        AssertPinned(nodes, "case DirectChildRole.RefuseLeft: case DirectChildRole.RefuseRight: return CommandNodeRole.Defender;", "the node role");
    }

    private static void NoFlankReleaseWithoutContact()
    {
        var calm = Allocate(WingEvidence(false, 3, false, 0), new ReserveReleaseContext(0.5f));
        AssertEqual(DirectChildRole.Reserve, calm[1].Role, "exposure alone, without contact, releases nothing");
        var low = Allocate(WingEvidence(true, 2, false, 0), new ReserveReleaseContext(0.5f));
        AssertEqual(DirectChildRole.Reserve, low[1].Role, "contact with moderate exposure (bucket 2) releases nothing");
        var off = Allocate(WingEvidence(true, 3, false, 0), new ReserveReleaseContext(0.5f, flankRelease: false));
        AssertEqual(DirectChildRole.Reserve, off[1].Role, "Enable Flank Response And Envelopment off: reserves release as before");
        var unset = Allocate(WingEvidence(true, 3, false, 0), default(ReserveReleaseContext));
        AssertEqual(DirectChildRole.Reserve, unset[1].Role, "a default context releases none");
        string army = SourceText("Tactical", "Orchestrator", "ArmyOrchestrator.cs");
        AssertPinned(army, "bool flank = Plugin.FlankResponseEnabled();", "the flag reaches the allocator");
        AssertPinned(army, "return new ReserveReleaseContext(_lastGlobalOdds, flank);", "with the stagger flag off the context is the BC11 one");
    }

    private static void FlankReleaseIsPerWingAndNearestReserve()
    {
        // Two reserves (sectors 1 and 3), both wings threatened: each wing takes the nearer reserve.
        var personality = new PersonalityVector(0.2f, 0f, 0f, 0f, 0f);
        var evidence = new[]
        {
            new DirectChildEvidence(3, 1, true, 0, 3, 0.8f),
            new DirectChildEvidence(3, 0, false, 1, 0, 0.7f, deployedReserve: true),
            new DirectChildEvidence(3, 1, true, 2, 0, 0.8f),
            new DirectChildEvidence(3, 0, false, 3, 0, 0.7f, deployedReserve: true),
            new DirectChildEvidence(3, 1, true, 4, 3, 0.8f),
        };
        var intents = DirectChildAllocator.AllocateWithZones(FlankPlan(), personality, Children(5), evidence, null, null, new ReserveReleaseContext(0.5f));
        AssertEqual(DirectChildRole.RefuseLeft, intents[1].Role, "sector 1 reserve to the left wing");
        AssertEqual(DirectChildRole.RefuseRight, intents[3].Role, "sector 3 reserve to the right wing");
        // One reserve, two threatened wings: the first wing in index order takes it; it is not released twice.
        var one = new[]
        {
            new DirectChildEvidence(3, 1, true, 0, 3, 0.8f),
            new DirectChildEvidence(3, 0, false, 1, 0, 0.7f, deployedReserve: true),
            new DirectChildEvidence(3, 1, true, 2, 0, 0.8f),
            new DirectChildEvidence(3, 1, true, 4, 3, 0.8f),
        };
        var single = DirectChildAllocator.AllocateWithZones(FlankPlan(), personality, Children(4), one, null, null, new ReserveReleaseContext(0.5f));
        AssertEqual(DirectChildRole.RefuseLeft, single[1].Role, "the single reserve goes to the first threatened wing");
    }

    // ------------------------------------------------------------------ zero allocation

    private static void FlankResponseAllocatesNothingAfterWarmup()
    {
        BattleSnapshot s = FixtureWith(20, Enemy(0f, 120f, 500f), Enemy(270f, 220f, 1500f), Enemy(300f, 90f, 700f));
        MicroTurnRule rule = ShippedRule;
        var queue = new int[16]; var regiments = new int[16]; var headings = new float[16];
        int sink = 0;
        AssertNoAllocation(() =>
        {
            FlankFacing.MostPressuredBearing(s, 0, 0f, out float bearing, out _);
            FlankFacing.SideBearing(s, 0, 0f, RefuseFlank.Left, out float sideBearing);
            sink += (int)(bearing + sideBearing) + (int)FlankFacing.PressuredSide(s, 0);
            sink += (int)PostureSense.RefuseFor(s, 0, CommandTaskType.HoldObjective, true, true);
            sink += FlankFacing.RefuseRotations(s, 0, RefuseFlank.Left, 240f, 0.5f, 4f, in rule, queue, regiments, headings, out int skipped) + skipped;
            sink += FlankFacing.VanillaTurnsBack(s, 1, 306f, in rule) ? 1 : 0;
        }, 100, "pressure facing and the in-place refuse");
        GC.KeepAlive(sink);
    }

    // ------------------------------------------------------------------ source helpers

    internal static string SourceText(params string[] parts)
    {
        return System.IO.File.ReadAllText(System.IO.Path.Combine(TestFixtures.FindRepoPath("src/WhiskeyRealism"), System.IO.Path.Combine(parts)));
    }

    private static string Flat(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

    internal static void AssertPinned(string body, string expected, string label)
    {
        AssertTrue(Flat(body).Contains(Flat(expected)), label + ": " + expected);
    }

    // The text from the first `from` to the next `to` (exclusive), whitespace untouched.
    internal static string Between(string text, string from, string to)
    {
        int start = text.IndexOf(from, StringComparison.Ordinal);
        AssertTrue(start >= 0, "found " + from);
        int end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        AssertTrue(end > start, "found " + to + " after " + from);
        return text.Substring(start, end - start);
    }

    private static (string, Action)[] Concat((string, Action)[] a, (string, Action)[] b)
    {
        var all = new (string, Action)[a.Length + b.Length];
        Array.Copy(a, all, a.Length);
        Array.Copy(b, 0, all, a.Length, b.Length);
        return all;
    }
}
