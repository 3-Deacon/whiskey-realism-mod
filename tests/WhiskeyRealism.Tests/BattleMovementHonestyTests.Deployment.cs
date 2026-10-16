using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// BC10 decisions 7-8: fog-honest deployment and the deployment reserve echelon. Vanilla's
// DoPlacementAIUnitsWithinDeploymentzoneNew (85524-85892) chooses objectives and facing from the
// true enemy; DeploymentDoctrine plans the same choices from what the side can know.
internal static partial class BattleMovementHonestyTests
{
    private static readonly float[] NoEntries = new float[0];

    private static BattleSnapshot DeploySnapshot(PerceptionMode mode, float visibleStrength = 0f, float cx = 0f, float cz = 0f, params EnemyObservation[] observations)
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        s.IndexCommandTree(null);
        s.SetPerception(mode, mode == PerceptionMode.RangeOnly ? PerceptionFallbackReason.GridNotPainted : PerceptionFallbackReason.None);
        foreach (EnemyObservation o in observations) s.AddObservation(in o);
        s.SetEnemySummary(visibleStrength, 0f, cx, cz);
        return s;
    }

    private static DeploymentGroup Placed(int id, float x, float z, float heading = 0f, int child = 0, int brigades = 1)
    {
        return new DeploymentGroup
        {
            GroupId = id, DirectChildId = child, VanillaX = x, VanillaZ = z, VanillaHeading = heading,
            OldX = x, OldZ = z, Brigades = brigades,
        };
    }

    private static bool[,] Matrix(int objectives, int groups, bool value)
    {
        var m = new bool[objectives, groups];
        for (int o = 0; o < objectives; o++)
            for (int g = 0; g < groups; g++) m[o, g] = value;
        return m;
    }

    private static DeploymentPlan Plan(BattleSnapshot s, DeploymentGroup[] groups, DeploymentObjective[] objectives, bool firstDay,
        float[] entryX, float[] entryZ, out int replaced, float centreX = 500f, float centreZ = -500f)
    {
        var plan = new DeploymentPlan();
        replaced = DeploymentDoctrine.PlanPlacements(s, groups, groups.Length, objectives, objectives.Length,
            Matrix(objectives.Length, groups.Length, true), Matrix(objectives.Length, groups.Length, false),
            0, firstDay, entryX, entryZ, entryX.Length, centreX, centreZ, plan);
        return plan;
    }

    // RangeOnly (every day-1 deployment): vanilla's range lists admit enemies within 150-450 m
    // with no fog test (122650), so no observation is used; the point is the nearest enemy-owned
    // entry point.
    private static void Day1RangeOnlyIgnoresVisibleUsesEntryPoint()
    {
        BattleSnapshot s = DeploySnapshot(PerceptionMode.RangeOnly, 500f, 0f, -900f,
            Obs(1, 0f, -900f, 0f, 30f));
        DeploymentDoctrine.AssumedEnemyPoint(s, 0f, 0f, new[] { 1000f, -3000f }, new[] { -1000f, 3000f }, 2, 500f, -500f,
            out float x, out float z, out AssumedEnemySource source);
        AssertEqual(AssumedEnemySource.EntryPoint, source, "the nearest entry point, not the visible centroid");
        AssertNear(1000f, x, 0.001f, "x");
        AssertNear(-1000f, z, 0.001f, "z");

        // The nearest entry point per group: a group near the second entry point reads that one.
        DeploymentDoctrine.AssumedEnemyPoint(s, -2900f, 2900f, new[] { 1000f, -3000f }, new[] { -1000f, 3000f }, 2, 500f, -500f,
            out x, out z, out _);
        AssertNear(-3000f, x, 0.001f, "the second entry point is nearer this group: x");
    }

    private static void FogGridAssumedEnemyVisibleThenEntryPointThenCentre()
    {
        BattleSnapshot seen = DeploySnapshot(PerceptionMode.FogGrid, 800f, 40f, -700f, Obs(1, 40f, -700f, 0f, 30f));
        DeploymentDoctrine.AssumedEnemyPoint(seen, 0f, 0f, new[] { 1000f }, new[] { -1000f }, 1, 500f, -500f,
            out float x, out float z, out AssumedEnemySource source);
        AssertEqual(AssumedEnemySource.Visible, source, "a painted grid and a visible enemy");
        AssertNear(40f, x, 0.001f, "centroid x");
        AssertNear(-700f, z, 0.001f, "centroid z");

        BattleSnapshot blind = DeploySnapshot(PerceptionMode.FogGrid);
        DeploymentDoctrine.AssumedEnemyPoint(blind, 0f, 0f, new[] { 1000f }, new[] { -1000f }, 1, 500f, -500f, out x, out z, out source);
        AssertEqual(AssumedEnemySource.EntryPoint, source, "nothing visible: the entry point");
        AssertNear(1000f, x, 0.001f, "entry x");

        DeploymentDoctrine.AssumedEnemyPoint(blind, 0f, 0f, NoEntries, NoEntries, 0, 500f, -500f, out x, out z, out source);
        AssertEqual(AssumedEnemySource.MapCentre, source, "no entry point: the map centre DoUnitPositioning used");
        AssertNear(500f, x, 0.001f, "centre x");
        AssertNear(-500f, z, 0.001f, "centre z");
    }

    // Day 1 (GetSuitableObjectiveForInitialAIPlacement 86260-86281) picks the candidate nearest
    // the enemy. The true enemy sits next to B in the true records; the side assumes the entry
    // point, which is next to A.
    private static void Day1PicksObjectiveNearestAssumedEnemyNotTrueEnemy()
    {
        BattleSnapshot s = DeploySnapshot(PerceptionMode.RangeOnly);
        s.AddEnemy(new UnitRecord { InstanceId = 77, UnitTyp = TacticalUnitType.Infantry, X = -300f, Z = -120f, Alliance = 1, Flags = UnitFlags.Alive | UnitFlags.Active });
        var objectives = new[]
        {
            new DeploymentObjective { X = 300f, Z = -100f, Owner = -1 },   // A
            new DeploymentObjective { X = -300f, Z = -100f, Owner = -1 },  // B
        };
        DeploymentGroup[] groups = { Placed(1, 0f, -500f) };
        DeploymentPlan plan = Plan(s, groups, objectives, true, new[] { 1000f }, new[] { -100f }, out int replaced);
        AssertEqual(0, plan.Placements[0].ObjectiveIndex, "A: nearest the entry point the side assumes");
        AssertEqual(1, replaced, "vanilla's spot at (0, -500) is not within 25 m of A");
        AssertNear(300f, plan.Placements[0].X, 0.001f, "placed on A (x)");
        AssertNear(-100f, plan.Placements[0].Z, 0.001f, "placed on A (z)");
        AssertEqual(PlacementKind.Replaced, plan.Placements[0].Kind, "kind");
    }

    // Candidates: a Visible observation within 50 m excludes an objective under a painted grid;
    // a Remembered one does not (the fog-honest filter reads what the side sees now).
    private static void FogGridObjectiveFilterUsesVisibleOnly()
    {
        var objectives = new[]
        {
            new DeploymentObjective { X = 300f, Z = -100f, Owner = -1 },   // A, nearest the enemy
            new DeploymentObjective { X = -300f, Z = -100f, Owner = -1 },  // B
        };
        DeploymentGroup[] groups = { Placed(1, 0f, -500f) };

        BattleSnapshot visible = DeploySnapshot(PerceptionMode.FogGrid, 500f, 320f, -100f, Obs(1, 320f, -100f, 0f, 30f));
        DeploymentPlan plan = Plan(visible, groups, objectives, true, new[] { 1000f }, new[] { -100f }, out _);
        AssertEqual(1, plan.Placements[0].ObjectiveIndex, "a visible enemy within 50 m of A excludes A");

        BattleSnapshot remembered = DeploySnapshot(PerceptionMode.FogGrid, 500f, 900f, -100f,
            Obs(1, 320f, -100f, 0f, 30f, ObservationState.Remembered, 0.9f), Obs(2, 900f, -100f, 0f, 30f));
        plan = Plan(remembered, groups, objectives, true, new[] { 1000f }, new[] { -100f }, out _);
        AssertEqual(0, plan.Placements[0].ObjectiveIndex, "a remembered enemy does not exclude A");
    }

    private static void RangeOnlyObjectiveFilterUsesEntryPointsOnly()
    {
        var objectives = new[]
        {
            new DeploymentObjective { X = 300f, Z = -100f, Owner = -1 },   // A, nearest the entry point
            new DeploymentObjective { X = -300f, Z = -100f, Owner = -1 },  // B
        };
        DeploymentGroup[] groups = { Placed(1, 0f, -500f) };
        // A visible observation on A changes nothing under RangeOnly (no observation is used).
        BattleSnapshot s = DeploySnapshot(PerceptionMode.RangeOnly, 500f, 300f, -100f, Obs(1, 300f, -100f, 0f, 30f));
        DeploymentPlan plan = Plan(s, groups, objectives, true, new[] { 1000f }, new[] { -100f }, out _);
        AssertEqual(0, plan.Placements[0].ObjectiveIndex, "the visible observation is ignored");

        // An entry point within 50 m of A excludes A.
        plan = Plan(s, groups, objectives, true, new[] { 330f }, new[] { -100f }, out _);
        AssertEqual(1, plan.Placements[0].ObjectiveIndex, "an enemy entry point within 50 m excludes the objective");
    }

    // Later days (GetSuitableObjectiveForAIPlacement 86285-86301) have no enemy input: the
    // nearest candidate to the group.
    private static void LaterDaysPickNearestToGroup()
    {
        BattleSnapshot s = DeploySnapshot(PerceptionMode.RangeOnly);
        var objectives = new[]
        {
            new DeploymentObjective { X = 300f, Z = -100f, Owner = -1 },   // A, nearest the entry point
            new DeploymentObjective { X = -300f, Z = -100f, Owner = -1 },  // B, nearest the group
        };
        DeploymentGroup[] groups = { Placed(1, -250f, -400f) };
        DeploymentPlan plan = Plan(s, groups, objectives, false, new[] { 1000f }, new[] { -100f }, out _);
        AssertEqual(1, plan.Placements[0].ObjectiveIndex, "B: nearest the group, not the enemy");
        // The later-day front stands one group depth behind the objective along the facing (85795-85803).
        groups[0].DepthMeters = 40f;
        plan = Plan(s, groups, objectives, false, new[] { 1000f }, new[] { -100f }, out _);
        AssertTrue(plan.Placements[0].X < -300f, "the front stands behind the objective, away from the enemy side (x " + plan.Placements[0].X + ")");
    }

    private static void KeepsVanillaPlacementWhenSameObjectiveAndFacing()
    {
        BattleSnapshot s = DeploySnapshot(PerceptionMode.RangeOnly);
        var objectives = new[] { new DeploymentObjective { X = 300f, Z = -100f, Owner = -1 } };
        // The entry point lies due east of the objective: the facing is 90.
        float[] ex = { 1000f }, ez = { -100f };
        DeploymentGroup[] kept = { Placed(1, 310f, -100f, 90f) };
        DeploymentPlan plan = Plan(s, kept, objectives, true, ex, ez, out int replaced);
        AssertEqual(0, replaced, "within the spacing radius of the same objective, same facing");
        AssertEqual(PlacementKind.Kept, plan.Placements[0].Kind, "kept");
        AssertNear(310f, plan.Placements[0].X, 0.001f, "vanilla's spot stands");

        DeploymentGroup[] turned19 = { Placed(1, 310f, -100f, 109f) };
        Plan(s, turned19, objectives, true, ex, ez, out replaced);
        AssertEqual(0, replaced, "19 degrees off is inside the 20 degree tolerance");
        DeploymentGroup[] turned21 = { Placed(1, 310f, -100f, 111f) };
        plan = Plan(s, turned21, objectives, true, ex, ez, out replaced);
        AssertEqual(1, replaced, "21 degrees off is re-placed");
        AssertNear(90f, plan.Placements[0].Heading, 0.5f, "facing the assumed enemy");

        DeploymentGroup[] farOff = { Placed(1, 400f, -100f, 90f) };
        Plan(s, farOff, objectives, true, ex, ez, out replaced);
        AssertEqual(1, replaced, "100 m from the objective is outside one brigade's 25 m spacing radius");
    }

    // Every position the deployment plan holds is a line front (the targetpos SetGroupFormation
    // takes, 92044-92058); the headquarters stands depth + back behind it. The conversions the
    // runtime uses are inverses at any facing.
    private static void DeploymentFramesRoundTripAtAnyFacing()
    {
        for (float facing = 0f; facing < 360f; facing += 37f)
        {
            BattleWriterPolicy.DeploymentLineFrontFromHq(321f, -654f, facing, 24f, 11f, out float fx, out float fz);
            BattleWriterPolicy.DeploymentHqFromLineFront(fx, fz, facing, 24f, 11f, out float hx, out float hz);
            AssertNear(321f, hx, 0.01f, "hq x at facing " + facing);
            AssertNear(-654f, hz, 0.01f, "hq z at facing " + facing);
            AssertNear(35f, (float)Math.Sqrt((fx - 321f) * (fx - 321f) + (fz + 654f) * (fz + 654f)), 0.01f, "front is depth + back from the hq");
        }
        // A placement planned on the front frame lands the headquarters behind the objective,
        // facing the assumed enemy (due +x here): hq = front minus 35 m along +x.
        BattleWriterPolicy.DeploymentHqFromLineFront(300f, -100f, 90f, 24f, 11f, out float qx, out float qz);
        AssertNear(265f, qx, 0.01f, "hq stands behind the front, away from the enemy");
        AssertNear(-100f, qz, 0.01f, "same z");
    }

    // CheckForSimilarPositions compares against list5, every group placed earlier in the call
    // (85870): vanilla's list holds each objective-path group it placed, whether or not Whiskey
    // later agrees with the spot. A front within half a spacing radius (12.5 m) of any earlier
    // placement, Kept, Locked or Replaced, is moved a radius away.
    private static void SpacingCountsEveryGroupPlacedInTheCall()
    {
        BattleSnapshot s = DeploySnapshot(PerceptionMode.RangeOnly);
        var objectives = new[]
        {
            new DeploymentObjective { X = 300f, Z = -100f, Owner = -1 },
            new DeploymentObjective { X = 308f, Z = -100f, Owner = -1 },
        };
        float[] ex = { 3000f }, ez = { -100f };
        // Both groups start far away, so both are re-placed; the second front is 8 m (< 12.5 m) from the first.
        DeploymentPlan both = Plan(s, new[] { Placed(1, 0f, -500f, 90f), Placed(2, 0f, -600f, 90f) }, objectives, true, ex, ez, out int replaced);
        AssertEqual(2, replaced, "both re-placed");
        AssertTrue(Dist(both.Placements[0].X, both.Placements[0].Z, both.Placements[1].X, both.Placements[1].Z) >= 24f,
            "the second group stands a spacing radius (25 m) from the first");

        // The first group already stands on the objective nearest the enemy (x 308) facing it: it is kept,
        // but it is still in vanilla's list, so the second group (objective x 300, 8 m away) is spaced from it.
        DeploymentPlan kept = Plan(s, new[] { Placed(1, 308f, -100f, 90f), Placed(2, 0f, -600f, 90f) }, objectives, true, ex, ez, out replaced);
        AssertEqual(PlacementKind.Kept, kept.Placements[0].Kind, "group 1 is kept");
        AssertEqual(1, replaced, "only the second is re-placed");
        AssertTrue(Dist(kept.Placements[0].X, kept.Placements[0].Z, kept.Placements[1].X, kept.Placements[1].Z) >= 24f,
            "a kept neighbour counts: the second group is a spacing radius away from it");

        // A locked group is placed by vanilla too.
        DeploymentGroup locked = Placed(1, 308f, -100f, 270f);
        locked.Locked = true;
        DeploymentPlan lockedPlan = Plan(s, new[] { locked, Placed(2, 0f, -600f, 90f) }, objectives, true, ex, ez, out _);
        AssertTrue(Dist(lockedPlan.Placements[0].X, lockedPlan.Placements[0].Z, lockedPlan.Placements[1].X, lockedPlan.Placements[1].Z) >= 24f,
            "a locked neighbour counts too");
    }

    // The discipline loop and the planner share one rule for the groups the pass must not touch.
    private static void LockedGroupRuleCoversWlParentAndPlayerFlags()
    {
        const UnitFlags live = UnitFlags.Alive | UnitFlags.Active;
        AssertFalse(DeploymentDoctrine.IsLockedGroup(10, 99, true, live), "an ordinary AI group");
        AssertTrue(DeploymentDoctrine.IsLockedGroup(99, 99, true, live), "the W&L command's AI group");
        AssertTrue(DeploymentDoctrine.IsLockedGroup(10, 99, true, live | UnitFlags.PlayerOwned), "player-owned");
        AssertTrue(DeploymentDoctrine.IsLockedGroup(10, 99, true, live | UnitFlags.PlayerCommand), "the player's command");
        AssertTrue(DeploymentDoctrine.IsLockedGroup(10, 99, true, live | UnitFlags.AncestorOfPlayerCommand), "an ancestor of the player's command");
        AssertTrue(DeploymentDoctrine.IsLockedGroup(10, 99, false, live), "a group the snapshot does not list cannot be written");
        AssertFalse(DeploymentDoctrine.IsLockedGroup(10, 0, true, live), "no W&L group (0) matches no group");
        // The patch's terrain loop consults the rule before correcting a group.
        string patch = Code("src/WhiskeyRealism/Patches/TacticalDeploymentTerrainDisciplinePatch.cs");
        string plan = Code("src/WhiskeyRealism/Tactical/Battle/DeploymentPlanRuntime.cs");
        AssertTrue(System.Text.RegularExpressions.Regex.IsMatch(patch, @"DeploymentPlanRuntime\.IsLocked\([^;]*\)\s*\)?\s*continue;|if \(!DeploymentPlanRuntime\.IsLocked\("),
            "the terrain loop skips locked groups");
        AssertTrue(plan.Contains("DeploymentDoctrine.IsLockedGroup("), "and shares the rule with the planner");
    }

    // ------------------------------------------------------------------ reserve echelon

    // A line of single-group children along +x at z = 0 facing an enemy assumed due +z far away.
    // Child k stands at z = -100 * k, so the larger the id, the farther from the enemy.
    private static DeploymentGroup[] LineOfChildren(int children)
    {
        var groups = new DeploymentGroup[children];
        for (int k = 0; k < children; k++)
            groups[k] = Placed(100 + k, 20f * k, -100f * k, 0f, child: 1 + k);
        return groups;
    }

    private static DeploymentPlan ReservePlan(DeploymentGroup[] groups, out int moved)
    {
        BattleSnapshot s = DeploySnapshot(PerceptionMode.RangeOnly);
        DeploymentPlan plan = Plan(s, groups, new DeploymentObjective[0], true, new[] { 500f }, new[] { 3000f }, out _);
        moved = DeploymentDoctrine.ProposeReserve(plan, groups, groups.Length);
        return plan;
    }

    private static int Reserved(DeploymentPlan plan)
    {
        int n = 0;
        for (int i = 0; i < plan.Count; i++)
            if (plan.Placements[i].Kind == PlacementKind.Reserve) n++;
        return n;
    }

    private static void ReserveIsFloorThirdOfEligibleChildren()
    {
        DeploymentGroup[] groups = LineOfChildren(7);
        DeploymentPlan plan = ReservePlan(groups, out int moved);
        AssertEqual(2, moved, "floor(7 / 3) = 2 children");
        AssertEqual(PlacementKind.Reserve, plan.Placements[6].Kind, "the farthest child from the enemy");
        AssertEqual(PlacementKind.Reserve, plan.Placements[5].Kind, "the next farthest");
        AssertEqual(PlacementKind.Kept, plan.Placements[4].Kind, "the rest stay in line");
        ReservePlan(LineOfChildren(3), out moved);
        AssertEqual(1, moved, "exactly three children give one reserve");
        ReservePlan(LineOfChildren(6), out moved);
        AssertEqual(2, moved, "six give two");
    }

    private static void NoReserveBelowThreeChildren()
    {
        DeploymentPlan plan = ReservePlan(LineOfChildren(2), out int moved);
        AssertEqual(0, moved, "two children");
        AssertEqual(0, Reserved(plan), "nobody is marked");
        // Three children but one holds a player-owned unit: two are eligible.
        DeploymentGroup[] groups = LineOfChildren(3);
        groups[2].PlayerOwned = true;
        plan = ReservePlan(groups, out moved);
        AssertEqual(0, moved, "two eligible children");
    }

    private static void ReserveNeverPlayerOwnedOrCavalryOnly()
    {
        DeploymentGroup[] groups = LineOfChildren(6);
        groups[5].PlayerOwned = true;       // the farthest child holds the player's unit
        groups[4].CavalryOnly = true;       // the next is cavalry only
        groups[3].WlCommandAncestor = true; // and the next is the W&L command's ancestor
        DeploymentPlan plan = ReservePlan(groups, out int moved);
        AssertEqual(1, moved, "three eligible children: one reserve");
        AssertEqual(PlacementKind.Reserve, plan.Placements[2].Kind, "the farthest eligible child");
        AssertEqual(PlacementKind.Kept, plan.Placements[5].Kind, "player-owned stays");
        AssertEqual(PlacementKind.Kept, plan.Placements[4].Kind, "cavalry-only stays");
        AssertEqual(PlacementKind.Kept, plan.Placements[3].Kind, "the command ancestor stays");

        groups = LineOfChildren(4);
        groups[3].ArtilleryOnly = true;
        plan = ReservePlan(groups, out moved);
        AssertEqual(1, moved, "an artillery-only child is not eligible: three remain, one reserve");
        AssertEqual(PlacementKind.Kept, plan.Placements[3].Kind, "the artillery child stays though it is farthest");
        AssertEqual(PlacementKind.Reserve, plan.Placements[2].Kind, "the farthest eligible child goes");
    }

    // The reserve stands ReserveDepthMeters behind the line centroid of the other children,
    // along the axis away from the assumed enemy (here +z, so behind is -z).
    private static void ReserveDepth425mClampedToZoneElseKept()
    {
        DeploymentGroup[] groups = LineOfChildren(6);
        DeploymentPlan plan = ReservePlan(groups, out _);
        float lineZ = 0f;
        int line = 0;
        for (int i = 0; i < 4; i++) { lineZ += plan.Placements[i].Z; line++; }
        lineZ /= line;
        for (int i = 4; i < 6; i++)
        {
            AssertEqual(PlacementKind.Reserve, plan.Placements[i].Kind, "reserve " + i);
            // (the assumed enemy is (500, 3000); the axis from it to the line is almost -z)
            float behind = (float)Math.Sqrt(Math.Pow(plan.Placements[i].X - plan.LineCentroidX, 2) + Math.Pow(plan.Placements[i].Z - plan.LineCentroidZ, 2));
            AssertTrue(behind > 400f && behind < 460f, "about 425 m behind the line centroid: " + behind);
        }

        // The runtime clamps the proposed targets into the deployment zone; a clamp that keeps
        // the depth is applied as the placement.
        var cx = new float[groups.Length];
        var cz = new float[groups.Length];
        for (int i = 0; i < groups.Length; i++) { cx[i] = plan.Placements[i].X + 3f; cz[i] = plan.Placements[i].Z - 1f; }
        int remaining = DeploymentDoctrine.ApplyReserveClamp(plan, groups, groups.Length, cx, cz);
        AssertEqual(2, remaining, "both children stay in reserve");
        AssertNear(cx[4], plan.Placements[4].X, 0.001f, "the clamped point is the placement");
        AssertNear(cz[5], plan.Placements[5].Z, 0.001f, "the clamped point is the placement (z)");
    }

    private static void ReserveClampRevertsChildUnder150m()
    {
        DeploymentGroup[] groups = LineOfChildren(6);
        DeploymentPlan plan = ReservePlan(groups, out _);
        float oldX4 = plan.Base[4].X, oldZ4 = plan.Base[4].Z;
        var cx = new float[groups.Length];
        var cz = new float[groups.Length];
        for (int i = 0; i < groups.Length; i++) { cx[i] = plan.Placements[i].X; cz[i] = plan.Placements[i].Z; }
        // Child 5's zone clamp leaves only 100 m of depth behind the line centroid.
        cx[4] = plan.LineCentroidX + plan.AxisX * 100f;
        cz[4] = plan.LineCentroidZ + plan.AxisZ * 100f;
        int remaining = DeploymentDoctrine.ApplyReserveClamp(plan, groups, groups.Length, cx, cz);
        AssertEqual(1, remaining, "one child keeps its reserve");
        AssertEqual(PlacementKind.Kept, plan.Placements[4].Kind, "the shallow child reverts to its fog-honest placement");
        AssertNear(oldX4, plan.Placements[4].X, 0.001f, "reverted x");
        AssertNear(oldZ4, plan.Placements[4].Z, 0.001f, "reverted z");
        AssertEqual(PlacementKind.Reserve, plan.Placements[5].Kind, "the other stays");
        // Just under 150 m reverts, just over stays (the bar is "at least 150 m").
        foreach ((float depth, int expected) in new[] { (149f, 1), (151f, 2) })
        {
            plan = ReservePlan(groups, out _);
            for (int i = 0; i < groups.Length; i++) { cx[i] = plan.Placements[i].X; cz[i] = plan.Placements[i].Z; }
            cx[4] = plan.LineCentroidX + plan.AxisX * depth;
            cz[4] = plan.LineCentroidZ + plan.AxisZ * depth;
            AssertEqual(expected, DeploymentDoctrine.ApplyReserveClamp(plan, groups, groups.Length, cx, cz), depth + " m deep");
        }
    }

    // One translation vector per child: the child's groups keep their relative offsets.
    private static void ReserveChildGroupsKeepRelativeSpacing()
    {
        var groups = new List<DeploymentGroup>();
        for (int k = 0; k < 3; k++)
        {
            groups.Add(Placed(100 + 10 * k, 20f * k, -100f * k, 0f, child: 1 + k));
            groups.Add(Placed(101 + 10 * k, 20f * k + 60f, -100f * k - 30f, 0f, child: 1 + k));
        }
        DeploymentGroup[] arr = groups.ToArray();
        DeploymentPlan plan = ReservePlan(arr, out int moved);
        AssertEqual(2, moved, "one child, two groups");
        // Child 3 is the farthest (z -200): groups 4 and 5.
        AssertEqual(PlacementKind.Reserve, plan.Placements[4].Kind, "child 3 group a");
        AssertEqual(PlacementKind.Reserve, plan.Placements[5].Kind, "child 3 group b");
        AssertNear(60f, plan.Placements[5].X - plan.Placements[4].X, 0.01f, "x offset kept");
        AssertNear(-30f, plan.Placements[5].Z - plan.Placements[4].Z, 0.01f, "z offset kept");

        // Two reserve children sit on the +-90 m lanes across the axis.
        DeploymentGroup[] six = LineOfChildren(6);
        DeploymentPlan two = ReservePlan(six, out _);
        float across = Math.Abs(two.Placements[5].X - two.Placements[4].X);
        AssertTrue(across > 170f && across < 190f, "the two reserve children stand 180 m apart across the axis: " + across);
    }

    // ------------------------------------------------------------------ deployed reserve outcome

    private static void OrderedFrom(OrderExecutor executor, int unitId, IntentSource source)
    {
        _issued[0] = new IssuedTarget { InstanceId = unitId, X = 500f, Z = -500f };
        var intent = new BattleOrderIntent
        {
            GroupId = unitId, Channel = OrderChannel.Move, TargetX = 500f, TargetZ = -500f, Heading = -1f,
            Priority = IntentPriority.Executor, Source = source, Task = CommandTaskType.ReserveWait, Reason = "test",
        };
        executor.RecordIssued(intent, 1.0, _issued, 1, null, 0, 0, 1);
    }

    private static void DeployedReserveFromExecutorOutcomeSource()
    {
        BattleSnapshot s = ArrivalFixture(out int[] ix);
        var executor = new OrderExecutor();
        AssertFalse(DeploymentDoctrine.IsDeployedReserve(s, executor, ix[0]), "no outcome");
        OrderedFrom(executor, SubGroup, IntentSource.Deployment);
        AssertFalse(DeploymentDoctrine.IsDeployedReserve(s, executor, ix[0]), "an ordinary deployment placement is not a reserve");
        OrderedFrom(executor, Mem2, IntentSource.DeploymentReserve);
        AssertTrue(DeploymentDoctrine.IsDeployedReserve(s, executor, ix[0]), "a reserve placement of a group under the child");
        AssertTrue(DeploymentDoctrine.IsDeployedReserve(s, executor, ix[1]), "the sub group is an ancestor of Mem2");
        AssertFalse(DeploymentDoctrine.IsDeployedReserve(s, executor, ix[2]), "Mem1 is a sibling, not a descendant");
        AssertFalse(DeploymentDoctrine.IsDeployedReserve(null, executor, 0), "null snapshot");
        AssertFalse(DeploymentDoctrine.IsDeployedReserve(s, executor, -1), "no child");
    }

    // ------------------------------------------------------------------ immediate placement (executor)

    private static BattleOrderIntent Placement(int group, float x, float z, float heading, IntentSource source = IntentSource.Deployment)
    {
        return new BattleOrderIntent
        {
            GroupId = group, Channel = OrderChannel.Move, TargetX = x, TargetZ = z, Heading = heading, Task = CommandTaskType.None,
            Priority = IntentPriority.Executor, Source = source, Reason = "deployment", Flags = IntentFlags.ImmediatePlacement,
        };
    }

    // The placement has no courier and no W&L dispatch; the executor judges it by where the
    // group stands at the next observation (the placed tree's lastsetwaypointposition is
    // cleared again, 85873-85883), Moved within DedupeMeters of the target, else Failed with
    // no retry and no backoff (the arbiter is not running at deployment).
    private static void ImmediatePlacementIssuesSetGroupFormationOnceAndPlacedOutcome()
    {
        var view = new FixtureView();
        int g = view.Add(1, 100f, -600f);
        OrderExecutor executor = ExecutorOver(null);
        BattleOrderIntent placement = Placement(1, 500f, -500f, 90f);

        ExecutionPlan plan = executor.Plan(view, placement, 1.0);
        AssertTrue(plan.Execute, "the placement executes");
        AssertFalse(plan.UseOrderDelay, "an immediate placement has no courier");
        AssertEqual(OrderExecutor.NoDispatch, plan.DispatchType, "and sends no W&L dispatch");

        _issued[0] = new IssuedTarget { InstanceId = 1, X = 500f, Z = -500f };
        executor.RecordIssued(placement, 1.0, _issued, 1, null, 0, 0, 0);
        var outcomes = new OrderOutcome[4];
        view.Own[g].X = 520f;
        view.Own[g].Z = -480f;
        executor.ObserveOutcomes(view, 1.01, outcomes, out int count);
        AssertEqual(1, count, "one outcome");
        AssertEqual(OrderOutcomeKind.Moved, outcomes[0].Kind, "28 m from the target is placed");
        AssertEqual(IntentSource.Deployment, outcomes[0].Source, "the outcome keeps the deployment source");

        // A group that is not near its target at the next observation failed, once, with no retry or backoff.
        var executor2 = ExecutorOver(null);
        view.Own[g].X = 100f;
        view.Own[g].Z = -600f;
        executor2.RecordIssued(placement, 1.0, _issued, 1, null, 0, 0, 0);
        executor2.ObserveOutcomes(view, 1.01, outcomes, out count);
        AssertEqual(1, count, "failure is one outcome, no Retried or GaveUp");
        AssertEqual(OrderOutcomeKind.Failed, outcomes[0].Kind, "failed");
        AssertEqual(0, executor2.TakeRetries(new BattleOrderIntent[4]), "no retry ticket");
        AssertTrue(executor2.Plan(view, Placement(1, 500f, -500f, 90f), 1.02).Execute, "no failed-write backoff either");

        // The write itself is one SetGroupFormation call in the runtime (source pin).
        string runtime = StripComments(System.IO.File.ReadAllText(TestFixtures.FindRepoPath("src/WhiskeyRealism/Tactical/Battle/OrderExecutorRuntime.cs")));
        AssertEqual(1, System.Text.RegularExpressions.Regex.Matches(runtime, @"immediateplacement:\s*true").Count, "one immediate-placement SetGroupFormation in the executor runtime");
    }

    private static void ImmediatePlacementAcceptedBesideSamePassPlacement()
    {
        var view = new FixtureView();
        view.Add(1, 100f, -600f);
        view.Add(2, 500f, -500f); // another group of the same pass already stands on the target
        OrderExecutor executor = ExecutorOver(null);

        BattleOrderIntent plain = Placement(1, 510f, -500f, 90f);
        plain.Flags = IntentFlags.None;
        AssertEqual(SkipReason.FriendlyFootprint, executor.Plan(view, plain, 1.0).Skip, "a plain move onto another group is refused");
        ExecutionPlan plan = executor.Plan(view, Placement(1, 510f, -500f, 90f), 1.0);
        AssertTrue(plan.Execute, "an immediate placement skips the friendly-footprint rule (the pass lays groups out by decision 7's spacing)");

        _issued[0] = new IssuedTarget { InstanceId = 1, X = 510f, Z = -500f };
        executor.RecordIssued(Placement(1, 510f, -500f, 90f), 1.0, _issued, 1, null, 0, 0, 0);
        view.Own[0].LastWaypointX = 510f;
        view.Own[0].LastWaypointZ = -500f;
        AssertTrue(executor.Plan(view, Placement(1, 512f, -502f, 90f), 1.0).Execute,
            "a corrective re-placement in the same pass is neither Duplicate nor in Cooldown");
    }

    private static void ImmediatePlacementRefusesPlayerOwned()
    {
        var view = new FixtureView();
        view.Add(1, 100f, -600f, UnitFlags.PlayerOwned);
        AssertEqual(SkipReason.PlayerOwned, ExecutorOver(null).Plan(view, Placement(1, 500f, -500f, 90f), 1.0).Skip, "the player's own unit is never placed");
        var feud = new FixtureView();
        feud.Add(1, 100f, -600f, UnitFlags.Feud);
        AssertEqual(SkipReason.Feud, ExecutorOver(null).Plan(feud, Placement(1, 500f, -500f, 90f), 1.0).Skip, "nor the feud instance");
    }

    // ------------------------------------------------------------------ allocator alignment

    private static TacticalBattlePlan MainSector2Plan()
    {
        return new TacticalBattlePlan(BattlePlanId.DoubleEnvelopment, BattlePhase.MainEffort, mainEffortSector: 2,
            fixingSectors: new int[0], screeningSectors: new int[0], reserveCommitTriggerOdds: 1.6f, ageSeconds: 0f, jitterSeed: 0);
    }

    private static DirectChildSnapshot[] Children(int n)
    {
        var snaps = new DirectChildSnapshot[n];
        for (int i = 0; i < n; i++) snaps[i] = new DirectChildSnapshot("c" + i, "a", 15, 0, "Corps " + i, true);
        return snaps;
    }

    // At the side's first allocation after the deployment pass a child the executor placed as
    // a reserve is Reserve before the main-effort pick and the SupportMain fall-through. Without
    // the flag the strongest brigade on the main sector would have been Main.
    private static void DeployedReserveEvidenceGivesReserveRoleFirst()
    {
        var personality = new PersonalityVector(0.2f, 0.0f, 0.0f, 0.0f, 0f);
        DirectChildEvidence[] evidence =
        {
            new DirectChildEvidence(2, 1, false, 2, 0, 0.7f),
            new DirectChildEvidence(4, 1, false, 2, 0, 0.7f),
            new DirectChildEvidence(2, 1, false, 1, 0, 0.7f),
        };
        var plain = DirectChildAllocator.AllocateWithZones(MainSector2Plan(), personality, Children(3), evidence, null, null);
        AssertEqual(DirectChildRole.Main, plain[1].Role, "without the flag the strongest main-sector brigade is Main");

        DirectChildEvidence[] flagged =
        {
            evidence[0],
            new DirectChildEvidence(4, 1, false, 2, 0, 0.7f, deployedReserve: true),
            evidence[2],
        };
        var intents = DirectChildAllocator.AllocateWithZones(MainSector2Plan(), personality, Children(3), flagged, null, null);
        AssertEqual(DirectChildRole.Reserve, intents[1].Role, "the deployed reserve keeps the Reserve role though it is the strongest on the main sector");
        AssertEqual(DirectChildRole.Main, intents[0].Role, "the main pick takes the next brigade");
        AssertEqual(DirectChildRole.SupportMain, intents[2].Role, "and the SupportMain fall-through skips the reserve");

        // The flag is part of the evidence signature, so the allocator re-runs when it clears.
        AssertFalse(evidence[1].SignatureEquals(flagged[1]), "DeployedReserve changes the signature");
    }

    // ReleaseReserves still applies to a deployed reserve: a crisis on an adjacent sector
    // (own line engaged and outmatched by two buckets) turns it into Fix on that band.
    private static void DeployedReserveReleasedByCrisis()
    {
        var personality = new PersonalityVector(0.2f, 0.0f, 0.0f, 0.0f, 0f);
        DirectChildEvidence[] evidence =
        {
            new DirectChildEvidence(3, 1, true, 2, 0, 0.8f),                                   // main
            new DirectChildEvidence(3, 1, false, 1, 0, 0.7f, deployedReserve: true),           // the reserve behind the left
            new DirectChildEvidence(1, 4, true, 0, 0, 0.8f),                                   // sector 0 in crisis: 1 vs 4
        };
        var intents = DirectChildAllocator.AllocateWithZones(MainSector2Plan(), personality, Children(3), evidence, null, null);
        AssertEqual(DirectChildRole.Fix, intents[1].Role, "a crisis on the adjacent sector releases the deployed reserve");
        AssertEqual(0, intents[1].AxisSector, "onto the crisis band");

        // Without a crisis (and no Exploit, odds below the trigger) it stays Reserve.
        evidence[2] = new DirectChildEvidence(1, 1, false, 0, 0, 0.8f);
        intents = DirectChildAllocator.AllocateWithZones(MainSector2Plan(), personality, Children(3), evidence, null, null,
            new ReserveReleaseContext(0.5f));
        AssertEqual(DirectChildRole.Reserve, intents[1].Role, "stays in reserve when nothing releases it");
    }

    private static void DeployedReserveFlowsThroughTheEvidenceBuilder()
    {
        var enemy = new EnemyVisibleState(new[] { new EnemyVisibleSector(2, 1000f, 1000f, false) }, 0.3f, false, false);
        var snaps = new[]
        {
            new DirectChildSnapshot("c0", "a", 15, 0, "First", true),
            new DirectChildSnapshot("c1", "a", 15, 0, "Second", true),
        };
        var evidence = DirectChildEvidenceBuilder.BuildAll(snaps, new[] { 2, 2 }, new[] { 0, 0 }, enemy, new[] { false, true });
        AssertFalse(evidence[0].DeployedReserve, "child 0");
        AssertTrue(evidence[1].DeployedReserve, "child 1 was placed as a reserve");
        evidence = DirectChildEvidenceBuilder.BuildAll(snaps, new[] { 2, 2 }, new[] { 0, 0 }, enemy);
        AssertFalse(evidence[1].DeployedReserve, "no list: nobody is a deployed reserve");
    }

    // The army's direct child a placed group belongs to: the nearest ancestor-or-self whose
    // instance id is one of the discovered direct children (the snapshot's D5 parent map, not
    // the scene graph). A group under none is its own child id 0.
    private static void DirectChildOfWalksToTheArmysChild()
    {
        BattleSnapshot s = ArrivalFixture(out int[] ix);   // Top(100) -> SubGroup(110) -> Mem2, Mem3;  Top -> Mem1;  Outside
        int[] children = { SubGroup, 777 };
        AssertEqual(SubGroup, DeploymentDoctrine.DirectChildOf(s, ix[3], children, 2), "Mem2 is under the child SubGroup");
        AssertEqual(SubGroup, DeploymentDoctrine.DirectChildOf(s, ix[1], children, 2), "the child itself");
        AssertEqual(0, DeploymentDoctrine.DirectChildOf(s, ix[2], children, 2), "Mem1 hangs off the army root, not a listed child");
        AssertEqual(0, DeploymentDoctrine.DirectChildOf(s, ix[5], children, 2), "Outside belongs to none");
        AssertEqual(0, DeploymentDoctrine.DirectChildOf(s, -1, children, 2), "bad index");
        AssertEqual(0, DeploymentDoctrine.DirectChildOf(s, ix[3], children, 0), "no children listed");
    }

    // A group the pass must not touch (the W&L command's AI group, whose parent and command stay
    // together) keeps its vanilla placement and still holds the objective vanilla gave it, so no
    // other group is re-placed onto it (vanilla's list of objectives already taken, 85766).
    private static void LockedGroupIsNeverReplacedAndHoldsItsObjective()
    {
        BattleSnapshot s = DeploySnapshot(PerceptionMode.RangeOnly);
        var objectives = new[]
        {
            new DeploymentObjective { X = 300f, Z = -100f, Owner = -1 },   // nearest the enemy
            new DeploymentObjective { X = -300f, Z = -100f, Owner = -1 },
        };
        float[] ex = { 3000f }, ez = { -100f };
        DeploymentGroup locked = Placed(1, 290f, -100f, 270f);   // stands on objective 0, facing the wrong way
        locked.Locked = true;
        DeploymentPlan plan = Plan(s, new[] { locked, Placed(2, 0f, -600f, 90f) }, objectives, true, ex, ez, out int replaced);
        AssertEqual(PlacementKind.Kept, plan.Placements[0].Kind, "the locked group is never re-placed, whatever its facing");
        AssertNear(290f, plan.Placements[0].X, 0.001f, "vanilla's spot stands");
        AssertEqual(1, plan.Placements[1].ObjectiveIndex, "the other group cannot take the locked group's objective");
        AssertEqual(1, replaced, "one re-placement");
    }

    // ------------------------------------------------------------------ runtime wiring (source pins)

    private static string Code(string repoPath)
    {
        return StripComments(System.IO.File.ReadAllText(TestFixtures.FindRepoPath(repoPath)));
    }

    // #60 writes only through the battle core's executor: no vanilla writer call in the patch or
    // in the planning runtime, and the executor runtime is the only SetGroupFormation caller left in src.
    private static void DeploymentWritesOnlyThroughTheExecutor()
    {
        string patch = Code("src/WhiskeyRealism/Patches/TacticalDeploymentTerrainDisciplinePatch.cs");
        string plan = Code("src/WhiskeyRealism/Tactical/Battle/DeploymentPlanRuntime.cs");
        var vanillaWriter = new System.Text.RegularExpressions.Regex(
            @"\.(SetGroupFormation|SetWaypoint|SetFormation|ChangeStance|RegimentSetPath)\(");
        AssertFalse(vanillaWriter.IsMatch(patch), "the #60 patch calls no vanilla writer");
        AssertFalse(vanillaWriter.IsMatch(plan), "nor does the planning runtime");
        AssertTrue(plan.Contains("OrderExecutorRuntime.TryPlanInline(") && plan.Contains("OrderExecutorRuntime.ExecutePlannedInline("),
            "the placement is an inline executor Move");
        AssertTrue(plan.Contains("Flags = IntentFlags.ImmediatePlacement") && plan.Contains("Priority = IntentPriority.Executor"),
            "an immediate placement at Executor priority");
        AssertTrue(plan.Contains("IntentSource.DeploymentReserve") && plan.Contains("IntentSource.Deployment"), "both placement sources");
        AssertTrue(patch.Contains("DeploymentPlanRuntime.Place("), "the terrain correction places through the same call");

        string src = TestFixtures.FindRepoPath("src/WhiskeyRealism");
        var callers = new List<string>();
        foreach (string path in System.IO.Directory.GetFiles(src, "*.cs", System.IO.SearchOption.AllDirectories))
            if (StripComments(System.IO.File.ReadAllText(path)).Contains(".SetGroupFormation(")) callers.Add(System.IO.Path.GetFileName(path));
        AssertEqual("OrderExecutorRuntime.cs", string.Join(",", callers), "the executor runtime is the only SetGroupFormation caller");
    }

    private static void DeploymentPatchIsPrefixAndPostfixWithScopes()
    {
        string patch = Code("src/WhiskeyRealism/Patches/TacticalDeploymentTerrainDisciplinePatch.cs");
        AssertTrue(System.Text.RegularExpressions.Regex.IsMatch(patch, @"\[HarmonyPrefix\]\s*internal static void Prefix\("), "a void (non-skipping) Prefix");
        AssertTrue(patch.Contains("TelemetryPerf.Scope(\"tactical.patch.deployment-terrain.prefix\"") &&
                   patch.Contains("PatchFirstFire.Mark(\"tactical.patch.deployment-terrain.prefix\")"), "the Prefix scope and first-fire id");
        AssertTrue(patch.Contains("TelemetryPerf.Scope(\"tactical.patch.deployment-terrain.postfix\"") &&
                   patch.Contains("PatchFirstFire.Mark(\"tactical.patch.deployment-terrain.postfix\")"), "the Postfix scope and first-fire id");
        AssertTrue(patch.Contains("DeploymentDoctrineEnabled()") && patch.Contains("DeploymentPlanRuntime.Run("), "the doctrine flag gates the re-placement");
        AssertFalse(patch.Contains("GetVisibleEnemyBearing"), "the range-only visible bearing is gone");
        AssertTrue(patch.Contains("TerrainDisciplineEnabled() || DeploymentDoctrineEnabled()"), "the patch runs while either flag is on");
        AssertTrue(patch.Contains("if (!TerrainDisciplineEnabled()) return;"), "the terrain correction needs its own flag, the doctrine does not");
        string probe = Code("src/WhiskeyRealism/Tactical/TacticalTerrainProbe.cs");
        AssertFalse(probe.Contains("GetVisibleEnemyBearing") || probe.Contains("enemyinrangereg"), "the probe reads no enemy unit list");
        string observer = Code("src/WhiskeyRealism/Patches/TacticalDeploymentObserverPatch.cs");
        AssertTrue(observer.Contains("GetAssumedEnemyBearing("), "#58's observer row reads the same assumed point");
    }

    private static void DeployedReserveIsReadFromTheExecutorOutcome()
    {
        string coordinator = Code("src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs");
        AssertTrue(coordinator.Contains("DeploymentDoctrine.IsDeployedReserve("), "the evidence reads the executor's outcome");
        AssertTrue(coordinator.Contains("DeployedReserveFlags(side.AllianceId, childInstanceIds)") &&
                   coordinator.Contains("DeployedReserveMemory.Shared.Apply(deployedReserve, childInstanceIds)") &&
                   System.Text.RegularExpressions.Regex.IsMatch(coordinator, @"DirectChildEvidenceBuilder\.BuildAll\([^;]*deployedReserve\)"),
            "the flags pass the release memory and reach the builder");
        AssertTrue(coordinator.Contains("DeployedReserveMemory.Shared.NoteRoles(deployedReserve, childInstanceIds, allocatedRoles)"),
            "and the allocated roles feed the memory after the allocation");
    }

    // A terrain correction of a reserve group is issued with source Deployment; its outcome would
    // replace the reserve's. The placement carries DeploymentReserve instead, so the child stays a deployed reserve.
    private static void TerrainCorrectedReserveKeepsItsReserveSource()
    {
        BattleSnapshot s = ArrivalFixture(out int[] ix);
        var executor = new OrderExecutor();
        AssertEqual(IntentSource.Deployment, DeploymentDoctrine.PlacementSource(executor, Mem2, IntentSource.Deployment), "no outcome: a plain placement");
        OrderedFrom(executor, Mem2, IntentSource.DeploymentReserve);
        IntentSource carried = DeploymentDoctrine.PlacementSource(executor, Mem2, IntentSource.Deployment);
        AssertEqual(IntentSource.DeploymentReserve, carried, "the correction of a reserve group stays a reserve placement");
        OrderedFrom(executor, Mem2, carried);   // the correction's own outcome
        AssertTrue(DeploymentDoctrine.IsDeployedReserve(s, executor, ix[0]), "the child is still a deployed reserve after the correction");

        var other = new OrderExecutor();
        OrderedFrom(other, Mem2, IntentSource.PostureExecutor);
        AssertEqual(IntentSource.Deployment, DeploymentDoctrine.PlacementSource(other, Mem2, IntentSource.Deployment), "a group some other source moved is not a reserve");
        AssertEqual(IntentSource.DeploymentReserve, DeploymentDoctrine.PlacementSource(null, Mem2, IntentSource.DeploymentReserve), "a requested reserve is never downgraded");
        string plan = Code("src/WhiskeyRealism/Tactical/Battle/DeploymentPlanRuntime.cs");
        AssertTrue(plan.Contains("DeploymentDoctrine.PlacementSource("), "every placement goes through the source rule");
        AssertEqual(425f, DeploymentDoctrine.ReserveDepthMeters, "the ledger's reserve stand-off");
        AssertEqual(90f, DeploymentDoctrine.ReserveLaneMeters, "and lane");
    }

    private static void OrderedAt(OrderExecutor executor, int unitId, IntentSource source, double hours)
    {
        _issued[0] = new IssuedTarget { InstanceId = unitId, X = 500f, Z = -500f };
        var intent = new BattleOrderIntent
        {
            GroupId = unitId, Channel = OrderChannel.Move, TargetX = 500f, TargetZ = -500f, Heading = -1f,
            Priority = IntentPriority.Executor, Source = source, Task = CommandTaskType.None, Reason = "test",
        };
        executor.RecordIssued(intent, hours, _issued, 1, null, 0, 0, 1);
    }

    // The release producer moves the direct-child node, not the placed AI groups under it: the groups'
    // last outcome is still DeploymentReserve, but a later Move by another source anywhere in the child's
    // subtree ends the deployed reserve.
    private static void DeployedReserveClearsWhenAnotherSourceMovesTheChild()
    {
        BattleSnapshot s = ArrivalFixture(out int[] ix);   // child Top(100) -> SubGroup(110) -> Mem2, Mem3
        var executor = new OrderExecutor();
        OrderedAt(executor, SubGroup, IntentSource.DeploymentReserve, 1.0);
        AssertTrue(DeploymentDoctrine.IsDeployedReserve(s, executor, ix[0]), "placed as a reserve");
        OrderedAt(executor, Top, IntentSource.ReserveRelease, 2.0);
        AssertFalse(DeploymentDoctrine.IsDeployedReserve(s, executor, ix[0]), "the release producer moved the child node: released");

        var posture = new OrderExecutor();
        OrderedAt(posture, SubGroup, IntentSource.DeploymentReserve, 1.0);
        OrderedAt(posture, Mem3, IntentSource.PostureExecutor, 1.5);
        AssertFalse(DeploymentDoctrine.IsDeployedReserve(s, posture, ix[0]), "a posture move of a descendant releases it too");

        var earlier = new OrderExecutor();
        OrderedAt(earlier, Top, IntentSource.PostureExecutor, 0.5);
        OrderedAt(earlier, SubGroup, IntentSource.DeploymentReserve, 1.0);
        AssertTrue(DeploymentDoctrine.IsDeployedReserve(s, earlier, ix[0]), "an older move of another source does not count");
    }

    // ReleaseReserves is an allocator role change that issues no Move, so the outcome alone would let the
    // child fall back into Reserve when the crisis passes. The memory keeps a released child released.
    private static void ReleasedDeployedReserveDoesNotFlipBack()
    {
        var memory = new DeployedReserveMemory();
        int[] ids = { 11, 12, 13 };
        bool[] flags = { true, true, false };
        memory.Apply(flags, ids);
        AssertTrue(flags[0] && flags[1], "nothing released yet");
        DirectChildRole[] roles = { DirectChildRole.Reserve, DirectChildRole.Fix, DirectChildRole.Main };
        memory.NoteRoles(new[] { true, true, false }, ids, roles);

        bool[] later = { true, true, false };
        memory.Apply(later, ids);
        AssertTrue(later[0], "a child still in Reserve stays a deployed reserve");
        AssertFalse(later[1], "the child released into Fix does not flip back when the crisis passes");
        memory.Clear();
        later = new[] { false, true, false };
        memory.Apply(later, ids);
        AssertTrue(later[1], "the memory is per battle");
    }

    private static void DeployedReserveOutranksZoneRolesButNotTheFallbackCheck()
    {
        var personality = new PersonalityVector(0.2f, 0.0f, 0.0f, 0.0f, 0f);
        DirectChildEvidence[] evidence =
        {
            new DirectChildEvidence(2, 1, false, 2, 0, 0.7f),
            new DirectChildEvidence(4, 1, false, 2, 0, 0.7f, deployedReserve: true),
            new DirectChildEvidence(2, 1, false, 1, 0, 0.7f),
        };
        var zones = new[]
        {
            new WhiskeyRealism.Tactical.Orchestrator.Plays.CorpsAssignment("c1", WhiskeyRealism.Tactical.Orchestrator.Plays.PlayRectKind.LeftWing, 0, 0f, 0f, 0f),
        };
        var intents = DirectChildAllocator.AllocateWithZones(MainSector2Plan(), personality, Children(3), evidence, null, zones);
        AssertEqual(DirectChildRole.Reserve, intents[1].Role, "a zone row for a deployed reserve (a wing would be Main) does not override the reserve");

        // Severe overmatch still beats the reserve: the safety check stays first.
        var overmatched = new[]
        {
            evidence[0],
            new DirectChildEvidence(1, 4, true, 2, 0, 0.7f, deployedReserve: true),
            evidence[2],
        };
        var attack = new TacticalIntentModel(InferredIntent.Attack, 2, 0.9f, 0f, new EvidenceTag[0]);
        var enemyIntent = new[] { attack, attack, attack };
        var fell = DirectChildAllocator.AllocateWithZones(MainSector2Plan(), personality, Children(3), overmatched, enemyIntent, null);
        AssertEqual(DirectChildRole.Fallback, fell[1].Role, "a deployed reserve in contact and overmatched falls back");
    }

    // Vanilla has already placed every group before the Postfix runs, so a Kept or Locked group stands at
    // its spot whatever its place in grp order: a re-placed front is spaced from all of them, and a locked
    // group keeps its objective even when it comes last.
    private static void LockedAndKeptGroupsLateInOrderAreStillRespected()
    {
        BattleSnapshot s = DeploySnapshot(PerceptionMode.RangeOnly);
        var objectives = new[]
        {
            new DeploymentObjective { X = 300f, Z = -100f, Owner = -1 },   // nearest the enemy
            new DeploymentObjective { X = -300f, Z = -100f, Owner = -1 },
        };
        float[] ex = { 3000f }, ez = { -100f };

        // The locked W&L parent is last and stands on the nearest objective: the first group must not take it.
        DeploymentGroup locked = Placed(2, 295f, -100f, 270f);
        locked.Locked = true;
        DeploymentPlan plan = Plan(s, new[] { Placed(1, 0f, -600f, 90f), locked }, objectives, true, ex, ez, out int replaced);
        AssertEqual(1, plan.Placements[0].ObjectiveIndex, "the earlier group takes the other objective: the locked one keeps its own");
        AssertNear(-300f, plan.Placements[0].X, 0.001f, "and stands there");
        AssertEqual(PlacementKind.Kept, plan.Placements[1].Kind, "the locked group is untouched");

        // Two objectives 8 m apart: a Kept group LAST on the nearer one; the first group's front must keep clear of it.
        var close = new[]
        {
            new DeploymentObjective { X = 300f, Z = -100f, Owner = -1 },
            new DeploymentObjective { X = 308f, Z = -100f, Owner = -1 },
        };
        plan = Plan(s, new[] { Placed(1, 0f, -600f, 90f), Placed(2, 308f, -100f, 90f) }, close, true, ex, ez, out replaced);
        AssertEqual(PlacementKind.Kept, plan.Placements[1].Kind, "the late group stands on its objective: kept");
        AssertTrue(Dist(plan.Placements[0].X, plan.Placements[0].Z, plan.Placements[1].X, plan.Placements[1].Z) >= 24f,
            "the re-placed first group keeps a spacing radius from the kept group that is later in order");

        // The same with a locked group last (on the nearer objective, so the first group takes the other one).
        DeploymentGroup lockedLast = Placed(2, 308f, -100f, 270f);
        lockedLast.Locked = true;
        plan = Plan(s, new[] { Placed(1, 0f, -600f, 90f), lockedLast }, close, true, ex, ez, out _);
        AssertTrue(Dist(plan.Placements[0].X, plan.Placements[0].Z, plan.Placements[1].X, plan.Placements[1].Z) >= 24f,
            "and from a locked group that is later in order");
    }
}
