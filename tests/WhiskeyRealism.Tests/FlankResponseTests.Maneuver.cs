using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;

// BC11 decision 4: envelopment execution (EnvelopmentManeuver, the maneuver table, the catalog's six enveloping rects).
internal static partial class FlankResponseTests
{
    private static (string, Action)[] ManeuverTests()
    {
        return new (string, Action)[]
        {
            ("bc11 NonEnvelopmentWingNeverManeuvers", NonEnvelopmentWingNeverManeuvers),
            ("bc11 StartDelayWingHoldsThenReleases", StartDelayWingHoldsThenReleases),
            ("bc11 EnvelopsSetOnExactlySixRects", EnvelopsSetOnExactlySixRects),
            ("bc11 HoldUntilOffsetAfterCenterContact", HoldUntilOffsetAfterCenterContact),
            ("bc11 MarchTargetsRectAnchorOnLanes", MarchTargetsRectAnchorOnLanes),
            ("bc11 DeepRaidMarchesAroundLessPressuredEnd", DeepRaidMarchesAroundLessPressuredEnd),
            ("bc11 TurnFacesEnemyLineEnd", TurnFacesEnemyLineEnd),
            ("bc11 AttackAfterTurnHoldOrContact", AttackAfterTurnHoldOrContact),
            ("bc11 RiverOnInwardLegMirrorsSingleFlank", RiverOnInwardLegMirrorsSingleFlank),
            ("bc11 RiverOnInwardLegAbandonsDoubleWing", RiverOnInwardLegAbandonsDoubleWing),
            ("bc11 RiverOnMarchLegAbandons", RiverOnMarchLegAbandons),
            ("bc11 InwardLegClearReadsOnlyRiverId", InwardLegClearReadsOnlyRiverId),
            ("bc11 HighGroundEnemyAnchorsFlank", HighGroundEnemyAnchorsFlank),
            ("bc11 RememberedBelowHalfConfidenceIgnoredForHeight", RememberedBelowHalfConfidenceIgnoredForHeight),
            ("bc11 LostStrengthAndTimeoutAbandon", LostStrengthAndTimeoutAbandon),
            ("bc11 DefenderNeverManeuvers", DefenderNeverManeuvers),
            ("bc11 ManeuverReachesEveryNodeOfTheChild", ManeuverReachesEveryNodeOfTheChild),
            ("bc11 ChildNodeTakesAnchorAndDeepNodesFollow", ChildNodeTakesAnchorAndDeepNodesFollow),
            ("bc11 ArrivalAllowsHqStandingDepthBehindTheLine", ArrivalAllowsHqStandingDepthBehindTheLine),
            ("bc11 WingsDoNotWaitForACenterNobodyHolds", WingsDoNotWaitForACenterNobodyHolds),
            ("bc11 HoldTargetDriftStaysOutOfManeuverHash", HoldTargetDriftStaysOutOfManeuverHash),
            ("bc11 CollectOwnOnlyDirectChildren", CollectOwnOnlyDirectChildren),
            ("bc11 ManeuverTimersUseDoctrineClock", ManeuverTimersUseDoctrineClock),
            ("bc11 ManeuverPlumbingIsWired", ManeuverPlumbingIsWired),
            ("bc11 ManeuverAllocatesNothingAfterWarmup", ManeuverAllocatesNothingAfterWarmup),
        };
    }

    // A FlankMarch child 1,000 m from its rect at (1000, 500), 900 battle-s after the Center child's contact, enemy line end at
    // (800, 1500); nothing wrong on either leg.
    private static ManeuverInput Maneuver(double now = 10.0)
    {
        return new ManeuverInput
        {
            Kind = PlayRectKind.FlankMarch, Envelops = true, AnchorX = 1000f, AnchorZ = 500f, StartOffsetSeconds = 900f, MarchRouteKnown = true,
            TemplateHasCenter = true, PlayActivatedHours = 10.0, CenterContactHours = -1.0,
            ChildX = 0f, ChildZ = 0f, ChildStrengthBucket = 2,
            HasEnemyExtent = true, EnemyEndX = 800f, EnemyEndZ = 1500f, EnemyAnchorX = 0f, EnemyAnchorZ = 1500f,
            OutflankX = 900f, OutflankZ = 1500f,
            AnchorHeight = 10f, CorridorEnemyCount = 0, MaxCorridorEnemyHeight = float.MinValue, HeightDifferenceAnchored = 5f,
            CanMirror = false, Stance = BattleStance.Meeting, NowHours = now,
        };
    }

    private static ManeuverInput After(ManeuverInput input, double now)
    {
        input.NowHours = now;
        return input;
    }

    // Plays the FlankMarch input to the March phase (contact at 10.5, +0.25 h).
    private static ManeuverState Marching(out ManeuverInput input)
    {
        input = Maneuver();
        input.CenterContactHours = 10.5;
        var state = default(ManeuverState);
        EnvelopmentManeuver.Step(ref state, After(input, 10.75));
        AssertEqual(ManeuverPhase.March, state.Phase, "fixture reaches March");
        return state;
    }

    private static void NonEnvelopmentWingNeverManeuvers()
    {
        // GenericAggressive's LeftWing: no offset, not enveloping: a plain role assignment.
        PlayTemplate template = PlayTemplateCatalog.Lookup(BattlePlanId.GenericAggressive);
        PlayRect wing = default(PlayRect);
        foreach (PlayRect rect in template.Rects) if (rect.Kind == PlayRectKind.LeftWing) wing = rect;
        AssertFalse(wing.Envelops, "the generic wing does not envelop");
        var zone = new CorpsAssignment("child-1", wing.Kind, 0, -300f, 0f, wing.StartTimeOffsetSeconds, wing.Envelops);
        AssertFalse(DirectChildAllocator.ManeuverOf(zone).HasValue, "no maneuver rides with the zone");

        ManeuverInput input = Maneuver();
        input.Kind = PlayRectKind.LeftWing;
        input.Envelops = false;
        input.StartOffsetSeconds = 0f;
        var state = default(ManeuverState);
        for (double now = 10.0; now < 14.0; now += 0.2)
        {
            ManeuverOrder order = EnvelopmentManeuver.Step(ref state, After(input, now));
            AssertEqual(ManeuverPhase.None, order.Phase, "no March, Turn or Attack at " + now);
            AssertFalse(order.Overrides, "no override");
        }
    }

    private static void StartDelayWingHoldsThenReleases()
    {
        // HesitatedCommit: both wings carry 600 battle-s, not enveloping.
        PlayTemplate template = PlayTemplateCatalog.Lookup(BattlePlanId.HesitatedCommit);
        PlayRect wing = default(PlayRect);
        foreach (PlayRect rect in template.Rects) if (rect.Kind == PlayRectKind.LeftWing) wing = rect;
        AssertEqual(600f, wing.StartTimeOffsetSeconds, "HesitatedCommit wing offset");
        var zone = new CorpsAssignment("child-1", wing.Kind, 0, -300f, 0f, wing.StartTimeOffsetSeconds, wing.Envelops);
        ManeuverSpec spec = DirectChildAllocator.ManeuverOf(zone);
        AssertTrue(spec.HasValue && !spec.Envelops && spec.StartOffsetSeconds == 600f, "a start delay only");
        var methodical = PlayTemplateCatalog.Lookup(BattlePlanId.GenericMethodical);
        foreach (PlayRect rect in methodical.Rects)
            if (rect.Kind == PlayRectKind.LeftWing || rect.Kind == PlayRectKind.RightWing)
                AssertEqual(300f, rect.StartTimeOffsetSeconds, "GenericMethodical wing offset");

        ManeuverInput input = Maneuver();
        input.Kind = PlayRectKind.LeftWing;
        input.Envelops = false;
        input.StartOffsetSeconds = 600f;
        input.TemplateHasCenter = true;
        var state = default(ManeuverState);
        ManeuverOrder order = EnvelopmentManeuver.Step(ref state, After(input, 10.0));
        AssertEqual(ManeuverPhase.Hold, order.Phase, "holds at the play's activation");
        AssertEqual(CommandTaskType.FormUp, order.Task, "task FormUp");
        AssertEqual(input.ChildX, order.TargetX, "at its own position");
        order = EnvelopmentManeuver.Step(ref state, After(input, 10.0 + 599.5 / 3600.0));
        AssertEqual(ManeuverPhase.Hold, order.Phase, "still holding at 599.5 battle-s");
        order = EnvelopmentManeuver.Step(ref state, After(input, 10.0 + 600.5 / 3600.0));
        AssertEqual(ManeuverPhase.Released, order.Phase, "released after 600 battle-s");
        AssertFalse(order.Overrides, "back on its allocator role: no override");
        for (double now = 10.2; now < 14.0; now += 0.3)
        {
            order = EnvelopmentManeuver.Step(ref state, After(input, now));
            AssertEqual(ManeuverPhase.Released, order.Phase, "never marches, turns or attacks (" + now + ")");
        }
    }

    private static void EnvelopsSetOnExactlySixRects()
    {
        var found = new List<string>();
        foreach (BattlePlanId id in Enum.GetValues(typeof(BattlePlanId)))
        {
            PlayTemplate template = PlayTemplateCatalog.Lookup(id);
            if (template == null) continue;
            foreach (PlayRect rect in template.Rects)
                if (rect.Envelops) found.Add(id + ":" + rect.Kind);
        }
        found.Sort(StringComparer.Ordinal);
        string[] expected =
        {
            "CavalryRaid:DeepRaid", "DoubleEnvelopment:LeftWing", "DoubleEnvelopment:RightWing", "ManeuverAndFix:RightWing",
            "MobileFlankMarch:FlankMarch", "WideFlankApproach:FlankMarch",
        };
        AssertEqual(string.Join(",", expected), string.Join(",", found), "the enveloping rects");
        // The envelops flag rides the projection into the zone assignment.
        PlayTemplate raid = PlayTemplateCatalog.Lookup(BattlePlanId.MobileFlankMarch);
        var rects = PlayProjection.Project(raid, 0f, 0f, 0f, 1000f, 600f);
        int envelops = 0;
        for (int i = 0; i < rects.Count; i++) if (rects[i].Envelops) envelops++;
        AssertEqual(1, envelops, "one projected enveloping rect");
        var corps = new[] { new CorpsPosition("child-1", -600f, 300f), new CorpsPosition("child-2", 0f, 300f), new CorpsPosition("child-3", 0f, -400f) };
        var assignments = PlayCorpsAssignment.Assign(rects, corps, Array.Empty<ObjectivePosition>());
        int carried = 0;
        foreach (CorpsAssignment a in assignments) if (a.Envelops) { carried++; AssertEqual(PlayRectKind.FlankMarch, a.RectKind, "kind"); }
        AssertEqual(1, carried, "the assignment carries Envelops");
    }

    private static void HoldUntilOffsetAfterCenterContact()
    {
        ManeuverInput input = Maneuver();
        var state = default(ManeuverState);
        ManeuverOrder order = EnvelopmentManeuver.Step(ref state, After(input, 10.2));
        AssertEqual(ManeuverPhase.Hold, order.Phase, "holds until the Center child sees an enemy");
        AssertEqual("await-center-contact", order.Reason, "reason");
        AssertEqual(CommandTaskType.FormUp, order.Task, "FormUp");
        AssertEqual(0f, order.TargetX, "at the child's position (no move)");

        input.CenterContactHours = 10.5;
        order = EnvelopmentManeuver.Step(ref state, After(input, 10.7));
        AssertEqual(ManeuverPhase.Hold, order.Phase, "900 battle-s (0.25 h) after the contact: not yet at 10.7");
        AssertEqual("hold-offset", order.Reason, "reason");
        order = EnvelopmentManeuver.Step(ref state, After(input, 10.75));
        AssertEqual(ManeuverPhase.March, order.Phase, "marches at contact + 900 battle-s");

        // The later of activation and contact: a play activated after the contact counts from its activation.
        ManeuverInput late = Maneuver();
        late.PlayActivatedHours = 12.0;
        late.CenterContactHours = 10.5;
        var lateState = default(ManeuverState);
        AssertEqual(ManeuverPhase.Hold, EnvelopmentManeuver.Step(ref lateState, After(late, 12.2)).Phase, "counts from the activation");
        AssertEqual(ManeuverPhase.March, EnvelopmentManeuver.Step(ref lateState, After(late, 12.25)).Phase, "activation + offset");

        // No Center rect in the template: from the activation alone.
        ManeuverInput noCenter = Maneuver();
        noCenter.TemplateHasCenter = false;
        var noCenterState = default(ManeuverState);
        AssertEqual(ManeuverPhase.Hold, EnvelopmentManeuver.Step(ref noCenterState, After(noCenter, 10.2)).Phase, "offset not yet elapsed");
        AssertEqual(ManeuverPhase.March, EnvelopmentManeuver.Step(ref noCenterState, After(noCenter, 10.25)).Phase, "activation + 900 battle-s");
    }

    // The child node (id == source) sits at depth 1; its sub-nodes one level deeper each.
    private static CommandNodeIntent NodeIntent(string nodeId, string source, int depth = -1) =>
        new CommandNodeIntent(nodeId, source, DirectChildRole.Main, DirectChildAxis.SectorAxis, 0, 50, 0.5f,
            depth >= 0 ? depth : (nodeId == source ? 1 : 2));

    private static void MarchTargetsRectAnchorOnLanes()
    {
        var state = Marching(out ManeuverInput input);
        ManeuverOrder order = EnvelopmentManeuver.Step(ref state, After(input, 10.8));
        AssertEqual(ManeuverPhase.March, order.Phase, "marching");
        AssertEqual(CommandTaskType.AdvanceToAssembly, order.Task, "AdvanceToAssembly");
        AssertEqual(1000f, order.TargetX, "the rect anchor x");
        AssertEqual(500f, order.TargetZ, "the rect anchor z");

        // The child node and two divisions of child 7 (and a stranger): the child node itself takes the anchor, its two direct
        // sub-nodes the 180 m lanes along the lateral axis (+x), centred on the anchor.
        var nodes = new[] { NodeIntent("node-7", "node-7"), NodeIntent("node-70", "node-7"), NodeIntent("node-71", "node-7"), NodeIntent("node-9", "node-9") };
        var table = EnvelopmentManeuver.ExpandToNodes(new[] { new ChildManeuver("child-7", order, 1f, 0f) }, nodes);
        AssertEqual(3, table.Count, "every node of the child, none of another's");
        AssertTrue(table.TryGet("node-7", out ManeuverNodeEntry a), "child node");
        AssertTrue(table.TryGet("node-70", out ManeuverNodeEntry b), "first division");
        AssertTrue(table.TryGet("node-71", out ManeuverNodeEntry c), "second division");
        AssertFalse(table.TryGet("node-9", out _), "the stranger has none");
        AssertNear(1000f, a.TargetX, 0.01f, "the child node is sent to the anchor itself");
        AssertNear(1000f - EnvelopmentManeuver.LaneMeters * 0.5f, b.TargetX, 0.01f, "first lane");
        AssertNear(1000f + EnvelopmentManeuver.LaneMeters * 0.5f, c.TargetX, 0.01f, "last lane");
        AssertNear(500f, b.TargetZ, 0.01f, "lanes run along the lateral axis only");
        AssertEqual(180f, EnvelopmentManeuver.LaneMeters, "lane spacing");

        // A hold carries no target: the child stands where it is.
        var hold = new ManeuverOrder { Phase = ManeuverPhase.Hold, Task = CommandTaskType.FormUp, Heading = -1f, Reason = "hold-offset" };
        var holdTable = EnvelopmentManeuver.ExpandToNodes(new[] { new ChildManeuver("child-7", hold, 1f, 0f) }, nodes);
        holdTable.TryGet("node-70", out ManeuverNodeEntry held);
        AssertFalse(held.HasTarget, "no target during a hold");
        AssertTrue(held.Holds, "hold");
    }

    private static void DeepRaidMarchesAroundLessPressuredEnd()
    {
        TacticalFrontAxis axis = TacticalFrontAxis.FromCentroids(0f, 0f, 0f, 1500f).WithOwnExtent(-300f, 300f).WithEnemyExtent(-250f, 250f);
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        s.IndexCommandTree(null);
        s.AddObservation(new EnemyObservation { InstanceId = 1, X = 100f, Z = 1500f, Strength = 3000f, State = ObservationState.Visible, Confidence01 = 1f });
        s.AddObservation(new EnemyObservation { InstanceId = 2, X = -100f, Z = 1500f, Strength = 500f, State = ObservationState.Visible, Confidence01 = 1f });
        EnvelopmentManeuver.EnemyPressureSplit(s, in axis, out float left, out float right);
        AssertNear(500f, left, 0.01f, "left half");
        AssertNear(3000f, right, 0.01f, "right half");
        AssertTrue(EnvelopmentManeuver.OutflankPoint(in axis, 1500f, left, right, rectIsRight: true, out float ox, out float oz), "an outflank point");
        AssertNear(-250f - EnvelopmentManeuver.OutflankMarginMeters, ox, 0.01f, "300 m beyond the less pressured (left) end of the line");
        AssertNear(1500f, oz, 0.01f, "at the anchor's depth");
        AssertTrue(EnvelopmentManeuver.OutflankPoint(in axis, 1500f, 500f, 500f, rectIsRight: true, out ox, out _) && ox > 0f, "a tie keeps the rect's side (right)");
        AssertTrue(EnvelopmentManeuver.EnemyLineEnd(in axis, 0f, 1500f, rightSide: true, out float ex, out float ez), "line end");
        AssertNear(250f, ex, 0.01f, "right end x");
        AssertNear(1500f, ez, 0.01f, "at the enemy's depth");

        ManeuverInput input = Maneuver();
        input.Kind = PlayRectKind.DeepRaid;
        input.TemplateHasCenter = false;
        input.StartOffsetSeconds = 0f;
        input.AnchorX = 0f;
        input.AnchorZ = 1500f;
        input.OutflankX = ox;
        input.OutflankZ = oz;
        var state = default(ManeuverState);
        ManeuverOrder order = EnvelopmentManeuver.Step(ref state, After(input, 10.0));
        AssertEqual(ManeuverPhase.March, order.Phase, "no offset: marches at once");
        AssertEqual("march-outflank", order.Reason, "first to the outflank point");
        AssertEqual(ox, order.TargetX, "outflank x");
        input.ChildX = ox + 10f;
        input.ChildZ = oz - 10f;
        order = EnvelopmentManeuver.Step(ref state, After(input, 10.05));
        AssertEqual("march-anchor", order.Reason, "then round to the rect");
        AssertEqual(0f, order.TargetX, "anchor x");
        AssertTrue(state.OutflankReached, "remembered");
        input.ChildArrived = true;
        AssertEqual(ManeuverPhase.Turn, EnvelopmentManeuver.Step(ref state, After(input, 10.1)).Phase, "arrival at the anchor turns");
    }

    private static void TurnFacesEnemyLineEnd()
    {
        var state = Marching(out ManeuverInput input);
        input.ChildX = 990f;
        input.ChildZ = 510f;
        input.ChildArrived = true;
        ManeuverOrder order = EnvelopmentManeuver.Step(ref state, After(input, 10.9));
        AssertEqual(ManeuverPhase.Turn, order.Phase, "on arrival");
        float expected = PostureSense.HeadingTo(990f, 510f, 800f, 1500f);
        AssertNear(expected, order.Heading, 0.01f, "the bearing to the perceived enemy line end");
        AssertEqual(CommandTaskType.AdvanceToAssembly, order.Task, "stands on its anchor");

        // With no enemy extent the play's enemy anchor stands in.
        var noExtentState = Marching(out ManeuverInput noExtent);
        noExtent.HasEnemyExtent = false;
        noExtent.ChildArrived = true;
        noExtent.ChildX = 1000f;
        noExtent.ChildZ = 500f;
        ManeuverOrder anchorOrder = EnvelopmentManeuver.Step(ref noExtentState, After(noExtent, 10.9));
        AssertNear(PostureSense.HeadingTo(1000f, 500f, 0f, 1500f), anchorOrder.Heading, 0.01f, "the enemy anchor");

        // The heading rides to the doctrine order of every node, as ManeuverHeading.
        var nodes = new[] { NodeIntent("node-7", "node-7"), NodeIntent("node-70", "node-7") };
        var table = EnvelopmentManeuver.ExpandToNodes(new[] { new ChildManeuver("child-7", order, 1f, 0f) }, nodes);
        table.TryGet("node-70", out ManeuverNodeEntry entry);
        AssertNear(expected, entry.Heading, 0.01f, "node entry heading");
    }

    private static void AttackAfterTurnHoldOrContact()
    {
        var state = Marching(out ManeuverInput input);
        input.ChildArrived = true;
        double turnAt = 10.9;
        AssertEqual(ManeuverPhase.Turn, EnvelopmentManeuver.Step(ref state, After(input, turnAt)).Phase, "turn");
        AssertEqual(ManeuverPhase.Turn, EnvelopmentManeuver.Step(ref state, After(input, turnAt + 0.04)).Phase, "0.04 h: still turning");
        ManeuverOrder attack = EnvelopmentManeuver.Step(ref state, After(input, turnAt + 0.0501));
        AssertEqual(ManeuverPhase.Attack, attack.Phase, "attacks after TurnHoldHours 0.05");
        AssertEqual(CommandTaskType.AttackObjective, attack.Task, "AttackObjective");
        AssertEqual(800f, attack.TargetX, "the enemy line end x");
        AssertEqual(1500f, attack.TargetZ, "the enemy line end z");

        // First visible contact within 250 m attacks at once.
        var contactState = Marching(out ManeuverInput contact);
        contact.ChildArrived = true;
        EnvelopmentManeuver.Step(ref contactState, After(contact, 10.9));
        contact.ChildInContact = true;
        AssertEqual(ManeuverPhase.Attack, EnvelopmentManeuver.Step(ref contactState, After(contact, 10.91)).Phase, "contact attacks");

        // With no extent the attack goes to the play's enemy anchor.
        var anchorState = Marching(out ManeuverInput anchorInput);
        anchorInput.ChildArrived = true;
        anchorInput.HasEnemyExtent = false;
        EnvelopmentManeuver.Step(ref anchorState, After(anchorInput, 10.9));
        ManeuverOrder anchored = EnvelopmentManeuver.Step(ref anchorState, After(anchorInput, 11.0));
        AssertEqual(0f, anchored.TargetX, "anchor x");
        AssertEqual(1500f, anchored.TargetZ, "anchor z");
    }

    private static void RiverOnInwardLegMirrorsSingleFlank()
    {
        ManeuverInput input = Maneuver();
        input.CenterContactHours = 10.5;
        input.InwardLegHitsRiver = true;
        input.CanMirror = true;
        var state = default(ManeuverState);
        ManeuverOrder order = EnvelopmentManeuver.Step(ref state, After(input, 10.75));
        AssertEqual(ManeuverPhase.Hold, order.Phase, "a failing single flank goes back to Hold on the mirror tick: the mirrored leg has no route result yet");
        AssertEqual("mirror-flank", order.Reason, "reason");
        AssertTrue(state.Mirrored, "the mirrored side is tried once");
        // The runtime now supplies the mirrored anchor and, once planned, its route: clear inputs march to it.
        input.InwardLegHitsRiver = false;
        input.AnchorX = -1000f;
        input.MarchRouteKnown = false;
        AssertEqual(ManeuverPhase.Hold, EnvelopmentManeuver.Step(ref state, After(input, 10.755)).Phase, "no route result for the mirrored leg: still Hold");
        input.MarchRouteKnown = true;
        order = EnvelopmentManeuver.Step(ref state, After(input, 10.76));
        AssertEqual(ManeuverPhase.March, order.Phase, "the mirrored side is clear and judged on its route");
        AssertEqual(-1000f, order.TargetX, "the March is ordered to the mirrored anchor");
        // The mirrored side fails while still in Hold (checked before any launch): final as well.
        var holdState = default(ManeuverState);
        ManeuverInput fails = Maneuver();
        fails.CenterContactHours = 10.5;
        fails.InwardLegHitsRiver = true;
        fails.CanMirror = true;
        EnvelopmentManeuver.Step(ref holdState, After(fails, 10.75));
        AssertTrue(holdState.Mirrored, "mirrored once");
        AssertEqual(ManeuverPhase.Abandoned, EnvelopmentManeuver.Step(ref holdState, After(fails, 10.76)).Phase, "the mirrored leg fails before launch: abandoned, not mirrored again");
        // A second failure is final.
        input.InwardLegHitsRiver = true;
        order = EnvelopmentManeuver.Step(ref state, After(input, 10.77));
        AssertEqual(ManeuverPhase.Abandoned, order.Phase, "one mirrored try only");
        AssertEqual("river-on-leg", order.Reason, "reason");
        AssertEqual(ManeuverPhase.Abandoned, EnvelopmentManeuver.Step(ref state, After(Maneuver(), 11.5)).Phase, "abandoned stays abandoned for the plan");

        // The mirrored point is the reflection across the army's centre line.
        TacticalFrontAxis axis = TacticalFrontAxis.FromCentroids(0f, 0f, 0f, 1000f).WithOwnExtent(-300f, 300f);
        EnvelopmentManeuver.MirrorAcrossAxis(in axis, 800f, 500f, out float mx, out float mz);
        AssertNear(-800f, mx, 0.01f, "mirrored x");
        AssertNear(500f, mz, 0.01f, "depth kept");
        AssertTrue(EnvelopmentManeuver.RectIsRight(in axis, 800f, 500f) && !EnvelopmentManeuver.RectIsRight(in axis, mx, mz), "the sides swap");
    }

    private static void RiverOnInwardLegAbandonsDoubleWing()
    {
        ManeuverInput input = Maneuver();
        input.Kind = PlayRectKind.RightWing;
        input.CenterContactHours = 10.5;
        input.InwardLegHitsRiver = true;
        input.CanMirror = false;
        var state = default(ManeuverState);
        ManeuverOrder order = EnvelopmentManeuver.Step(ref state, After(input, 10.75));
        AssertEqual(ManeuverPhase.Abandoned, order.Phase, "a failing wing of a double envelopment is abandoned");
        AssertEqual("river-on-leg", order.Reason, "reason");
        AssertFalse(order.Overrides, "the child returns to its allocator role");
        AssertFalse(state.Mirrored, "no mirror");
    }

    private static void RiverOnMarchLegAbandons()
    {
        var state = Marching(out ManeuverInput input);
        input.MarchLegHitsRiver = true;
        ManeuverOrder order = EnvelopmentManeuver.Step(ref state, After(input, 10.8));
        AssertEqual(ManeuverPhase.Abandoned, order.Phase, "a river on the march leg abandons a double wing");
        AssertEqual("river-on-leg", order.Reason, "reason");
    }

    private static void InwardLegClearReadsOnlyRiverId()
    {
        int[] ids = { 0, 1, BattleTerrainGrid.FordId, BattleTerrainGrid.BridgeId, BattleTerrainGrid.RoadId, BattleTerrainGrid.ForestId };
        AssertTrue(EnvelopmentManeuver.InwardLegClear(ids, ids.Length), "fords, bridges, roads and forest are not river id 4");
        int[] withRiver = { 0, 1, BattleTerrainGrid.RiverId, 1 };
        AssertFalse(EnvelopmentManeuver.InwardLegClear(withRiver, withRiver.Length), "river id 4");
        AssertTrue(EnvelopmentManeuver.InwardLegClear(withRiver, 2), "only the sampled prefix counts");
        AssertEqual(4, BattleTerrainGrid.RiverId, "FlankMove's {4}");
        AssertEqual(2f, EnvelopmentManeuver.InwardStepMeters, "2 m steps");
    }

    // FlankMove 117049: bfs.GetTerrainHeight(enemy) - bfs.GetTerrainHeight(turn point) >= heightdifferencetoenemyunitanchored (5 m).
    private static void HighGroundEnemyAnchorsFlank()
    {
        AssertTrue(EnvelopmentManeuver.AnchoredHighGround(10f, new[] { 15f }, 1, 5f), "exactly 5 m above fails the flank");
        AssertFalse(EnvelopmentManeuver.AnchoredHighGround(10f, new[] { 14.99f }, 1, 5f), "4.99 m above passes");
        AssertTrue(EnvelopmentManeuver.AnchoredHighGround(10f, new[] { 3f, 12f, 40f }, 3, 5f), "any enemy high enough");
        AssertFalse(EnvelopmentManeuver.AnchoredHighGround(10f, new[] { 40f }, 0, 5f), "count 0 reads nothing");

        ManeuverInput input = Maneuver();
        input.CenterContactHours = 10.5;
        input.CorridorEnemyCount = 1;
        input.MaxCorridorEnemyHeight = input.AnchorHeight + 5f;
        var state = default(ManeuverState);
        ManeuverOrder order = EnvelopmentManeuver.Step(ref state, After(input, 10.75));
        AssertEqual(ManeuverPhase.Abandoned, order.Phase, "enemy 5 m above the anchor: abandoned");
        AssertEqual("anchored-high-ground", order.Reason, "reason");
        input.MaxCorridorEnemyHeight = input.AnchorHeight + 4.99f;
        state = default(ManeuverState);
        AssertEqual(ManeuverPhase.March, EnvelopmentManeuver.Step(ref state, After(input, 10.75)).Phase, "4.99 m passes");
        input.CorridorEnemyCount = 0;
        input.MaxCorridorEnemyHeight = 1000f;
        state = default(ManeuverState);
        AssertEqual(ManeuverPhase.March, EnvelopmentManeuver.Step(ref state, After(input, 10.75)).Phase, "no enemy on the corridor: height ignored");
    }

    private static void RememberedBelowHalfConfidenceIgnoredForHeight()
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        s.IndexCommandTree(null);
        // Corridor: anchor (0, 0) to the line end (0, 400), plus 150 m, 100 m wide.
        s.AddObservation(new EnemyObservation { InstanceId = 1, X = 0f, Z = 200f, Strength = 100f, State = ObservationState.Remembered, Confidence01 = 0.49f });
        s.AddObservation(new EnemyObservation { InstanceId = 2, X = 10f, Z = 300f, Strength = 100f, State = ObservationState.Remembered, Confidence01 = 0.5f });
        s.AddObservation(new EnemyObservation { InstanceId = 3, X = 0f, Z = 500f, Strength = 100f, State = ObservationState.Visible, Confidence01 = 1f });
        s.AddObservation(new EnemyObservation { InstanceId = 4, X = 60f, Z = 100f, Strength = 100f, State = ObservationState.Visible, Confidence01 = 1f });
        s.AddObservation(new EnemyObservation { InstanceId = 5, X = 0f, Z = 560f, Strength = 100f, State = ObservationState.Visible, Confidence01 = 1f });
        s.AddObservation(new EnemyObservation { InstanceId = 6, X = 0f, Z = 100f, Strength = 100f, State = ObservationState.Visible, Confidence01 = 1f, IsReinforcement = true });
        Func<float, float, float> heights = (x, z) => z;   // the height is the z, so the highest counted enemy is the farthest one
        int count = EnvelopmentManeuver.CorridorEnemies(s, 0f, 0f, 0f, 400f, heights, out float max);
        // Counted: #2 (remembered at exactly 0.5) and #3 (visible, 500 <= 400 + 150). Not: #1 (0.49), #4 (60 m across), #5 (560 > 550), #6 (reinforcement).
        AssertEqual(2, count, "remembered at 0.5 counts, below it does not; the corridor is the leg plus 150 m, 100 m wide");
        AssertNear(500f, max, 0.01f, "the highest counted enemy");
        AssertEqual(0, EnvelopmentManeuver.CorridorEnemies(s, 0f, 0f, 0f, 400f, null, out _), "no height source: nothing read");
    }

    private static void LostStrengthAndTimeoutAbandon()
    {
        var state = Marching(out ManeuverInput input);
        input.ChildStrengthBucket = 0;
        ManeuverOrder order = EnvelopmentManeuver.Step(ref state, After(input, 10.8));
        AssertEqual(ManeuverPhase.Abandoned, order.Phase, "the lowest strength bucket abandons");
        AssertEqual("lost-strength", order.Reason, "reason");

        var late = Marching(out ManeuverInput timeout);
        AssertEqual(ManeuverPhase.March, EnvelopmentManeuver.Step(ref late, After(timeout, 10.75 + 0.99)).Phase, "inside MaxManeuverHours");
        ManeuverOrder expired = EnvelopmentManeuver.Step(ref late, After(timeout, 10.75 + 1.0));
        AssertEqual(ManeuverPhase.Abandoned, expired.Phase, "MaxManeuverHours 1.0 doctrine-h after the march began");
        AssertEqual("timeout", expired.Reason, "reason");
        // The hold before the march does not count towards the timeout: a wait for contact is not a timeout (the Hold has its own
        // ceiling, HoldEndsAtTheCeiling).
        ManeuverInput waiting = Maneuver();
        var waitState = default(ManeuverState);
        EnvelopmentManeuver.Step(ref waitState, After(waiting, 10.0));
        AssertEqual(ManeuverPhase.Hold, EnvelopmentManeuver.Step(ref waitState, After(waiting, 10.9)).Phase, "still waiting for the Center child's contact");
    }

    private static void DefenderNeverManeuvers()
    {
        var state = Marching(out ManeuverInput input);
        input.Stance = BattleStance.Defender;
        ManeuverOrder order = EnvelopmentManeuver.Step(ref state, After(input, 10.8));
        AssertEqual(ManeuverPhase.None, order.Phase, "a Defender gets no maneuver");
        AssertFalse(order.Overrides, "no override");
        AssertEqual(ManeuverPhase.None, state.Phase, "the state is dropped");
        // The doctrine build ignores a table on a Defender side too, and the stance guard still applies to a task it sets.
        var nodes = new[] { new CommandNodeOperationalState("node-7", CommandEchelonKind.DivisionLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, CommandTaskState.Planning) };
        ManeuverOrder turn = new ManeuverOrder { Phase = ManeuverPhase.Turn, Task = CommandTaskType.AdvanceToAssembly, TargetX = 1000f, TargetZ = 500f, Heading = 345f, Reason = "turn-to-line-end" };
        var table = EnvelopmentManeuver.ExpandToNodes(new[] { new ChildManeuver("child-7", turn, 1f, 0f) }, new[] { NodeIntent("node-7", "node-7") });
        CommandDoctrineOrder[] defender = DoctrineOrders(nodes, BattleStance.Defender, table);
        AssertEqual(-1f, defender[0].ManeuverHeading, "no maneuver heading on a Defender side");
        AssertFalse(defender[0].PrimaryTarget.HasValue && defender[0].PrimaryTarget.X == 1000f && defender[0].PrimaryTarget.Z == 500f, "no maneuver target either");
        ManeuverOrder attack = new ManeuverOrder { Phase = ManeuverPhase.Attack, Task = CommandTaskType.AttackObjective, TargetX = 800f, TargetZ = 1500f, Heading = -1f, Reason = "attack-line-end" };
        var attackTable = EnvelopmentManeuver.ExpandToNodes(new[] { new ChildManeuver("child-7", attack, 1f, 0f) }, new[] { NodeIntent("node-7", "node-7") });
        AssertTrue(DoctrineOrders(nodes, BattleStance.Defender, attackTable)[0].Task != CommandTaskType.AttackObjective, "a Defender's order is never an attack");
        CommandDoctrineOrder[] meeting = DoctrineOrders(nodes, BattleStance.Meeting, table);
        AssertEqual(345f, meeting[0].ManeuverHeading, "the same table on a Meeting side carries the heading");
        AssertEqual(CommandTaskType.AttackObjective, DoctrineOrders(nodes, BattleStance.Meeting, attackTable)[0].Task, "and attacks");
    }

    private static CommandDoctrineOrder[] DoctrineOrders(CommandNodeOperationalState[] nodes, BattleStance stance, ManeuverNodeTable table)
    {
        var operation = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Committed, "obj-1", 0f);
        var objective = new BattlefieldObjectiveEstimate("obj-1", TacticalObjectiveType.VictoryPoint, 500f, 0.8f, true, 0.8f, 0f, 1000f, 0f, 0f);
        var picture = new BattlefieldPictureSnapshot(new[] { objective });
        return CommandDoctrineAssignment.Build(nodes, operation, picture, 20000f, 100f, 0.3f, stance, table);
    }

    private static void ManeuverReachesEveryNodeOfTheChild()
    {
        var nodes = new[]
        {
            new CommandNodeOperationalState("node-7", CommandEchelonKind.CorpsLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, CommandTaskState.Planning),
            new CommandNodeOperationalState("node-70", CommandEchelonKind.DivisionLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, CommandTaskState.Planning),
            new CommandNodeOperationalState("node-71", CommandEchelonKind.DivisionLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, CommandTaskState.Planning),
            new CommandNodeOperationalState("node-9", CommandEchelonKind.CorpsLike, CommandNodeRole.FixingForce, CommandTaskType.FormUp, CommandTaskState.Planning),
        };
        var intents = new[] { NodeIntent("node-7", "node-7"), NodeIntent("node-70", "node-7"), NodeIntent("node-71", "node-7"), NodeIntent("node-9", "node-9") };

        var march = new ManeuverOrder { Phase = ManeuverPhase.Turn, Task = CommandTaskType.AdvanceToAssembly, TargetX = 1000f, TargetZ = 500f, Heading = 345f, Reason = "turn-to-line-end" };
        var table = EnvelopmentManeuver.ExpandToNodes(new[] { new ChildManeuver("child-7", march, 1f, 0f) }, intents);
        CommandDoctrineOrder[] orders = DoctrineOrders(nodes, BattleStance.Meeting, table);
        for (int i = 0; i < 3; i++)
        {
            AssertEqual(CommandTaskType.AdvanceToAssembly, orders[i].Task, "node " + i + " carries the maneuver task");
            AssertEqual(345f, orders[i].ManeuverHeading, "node " + i + " carries the heading");
            AssertTrue(orders[i].PrimaryTarget.HasValue, "node " + i + " has a target");
            AssertFalse(orders[i].ManeuverHold, "not a hold");
            AssertEqual(500f, orders[i].PrimaryTarget.Z, "lane z");
        }
        AssertNear(1000f, orders[0].PrimaryTarget.X, 0.01f, "the child's own node is routed to the anchor, not a lane");
        AssertNear(1000f - EnvelopmentManeuver.LaneMeters * 0.5f, orders[1].PrimaryTarget.X, 0.01f, "first division lane");
        AssertNear(1000f + EnvelopmentManeuver.LaneMeters * 0.5f, orders[2].PrimaryTarget.X, 0.01f, "second division lane");
        AssertEqual(-1f, orders[3].ManeuverHeading, "the other child is untouched");
        AssertTrue(orders[3].Task != CommandTaskType.AdvanceToAssembly || orders[3].Role != CommandNodeRole.MainEffort, "no maneuver task on another child's node");

        var hold = new ManeuverOrder { Phase = ManeuverPhase.Hold, Task = CommandTaskType.FormUp, TargetX = 0f, TargetZ = 0f, Heading = -1f, Reason = "hold-offset" };
        var holdTable = EnvelopmentManeuver.ExpandToNodes(new[] { new ChildManeuver("child-7", hold, 1f, 0f) }, intents);
        CommandDoctrineOrder[] holds = DoctrineOrders(nodes, BattleStance.Meeting, holdTable);
        for (int i = 0; i < 3; i++)
        {
            AssertTrue(holds[i].ManeuverHold, "node " + i + " holds");
            AssertEqual(CommandTaskType.FormUp, holds[i].Task, "FormUp");
            AssertFalse(holds[i].PrimaryTarget.HasValue, "no target to route to");
            AssertEqual(DoctrineAllowedIdleReason.FormingUp, holds[i].AllowedIdle, "allowed idle");
        }
    }

    // A four-node subtree: corps (depth 1), two divisions (depth 2), a brigade under the first division (depth 3).
    private static void ChildNodeTakesAnchorAndDeepNodesFollow()
    {
        var intents = new[]
        {
            NodeIntent("node-7", "node-7", 1), NodeIntent("node-70", "node-7", 2), NodeIntent("node-71", "node-7", 2), NodeIntent("node-700", "node-7", 3),
        };
        var march = new ManeuverOrder { Phase = ManeuverPhase.March, Task = CommandTaskType.AdvanceToAssembly, TargetX = 1000f, TargetZ = 500f, Heading = -1f, Reason = "march" };
        var table = EnvelopmentManeuver.ExpandToNodes(new[] { new ChildManeuver("child-7", march, 1f, 0f) }, intents);
        AssertEqual(4, table.Count, "all four nodes");
        table.TryGet("node-7", out ManeuverNodeEntry corps);
        table.TryGet("node-70", out ManeuverNodeEntry divisionA);
        table.TryGet("node-71", out ManeuverNodeEntry divisionB);
        table.TryGet("node-700", out ManeuverNodeEntry brigade);
        AssertNear(1000f, corps.TargetX, 0.01f, "the corps node is sent to the anchor, so the arrival test can pass");
        AssertNear(1000f - EnvelopmentManeuver.LaneMeters * 0.5f, divisionA.TargetX, 0.01f, "division A lane");
        AssertNear(1000f + EnvelopmentManeuver.LaneMeters * 0.5f, divisionB.TargetX, 0.01f, "division B lane");
        AssertNear(1000f, brigade.TargetX, 0.01f, "a deeper node follows its parent's formation: the anchor, not a lane");

        // One direct sub-node: no lane spread at all.
        var single = EnvelopmentManeuver.ExpandToNodes(new[] { new ChildManeuver("child-7", march, 1f, 0f) },
            new[] { NodeIntent("node-7", "node-7", 1), NodeIntent("node-70", "node-7", 2) });
        single.TryGet("node-70", out ManeuverNodeEntry only);
        AssertNear(1000f, only.TargetX, 0.01f, "a lone division stands on the anchor");
    }

    // SetGroupFormation paths a group's HQ groupdepth + groupunitdistancetoback behind the waypoint (92044, 92058): a child that has
    // reached its anchor never has the HQ on the anchor.
    private static void ArrivalAllowsHqStandingDepthBehindTheLine()
    {
        AssertTrue(EnvelopmentManeuver.Arrived(1000f, 380f, 1000f, 500f, 0f), "exactly 120 m is inside the bare tolerance");
        AssertFalse(EnvelopmentManeuver.Arrived(1000f, 379f, 1000f, 500f, 0f), "121 m is outside it");
        AssertFalse(EnvelopmentManeuver.Arrived(1000f, 300f, 1000f, 500f, 0f), "an HQ 200 m behind the anchor has not arrived on the bare tolerance");
        AssertTrue(EnvelopmentManeuver.Arrived(1000f, 300f, 1000f, 500f, 100f), "...but has, with the standing depth added");
        AssertFalse(EnvelopmentManeuver.Arrived(1000f, 100f, 1000f, 500f, 100f), "a column still far behind has not arrived");
        AssertFalse(EnvelopmentManeuver.Arrived(1000f, 300f, 1000f, 500f, -50f), "a negative slack is ignored, not subtracted");
    }

    // A DirectChildIntent standing for a child whose role came from the given play rect (Unknown: from evidence).
    private static DirectChildIntent ZoneIntent(string childId, PlayRectKind zoneRect)
    {
        return new DirectChildIntent(childId, 15, 15, childId, 0, DirectChildRole.Main, DirectChildAxis.SectorAxis, 0, 0.5f, 0.5f,
            new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, Array.Empty<EvidenceTag>()), default(ManeuverSpec), zoneRect);
    }

    private static void WingsDoNotWaitForACenterNobodyHolds()
    {
        PlayTemplate template = PlayTemplateCatalog.Lookup(BattlePlanId.DoubleEnvelopment);
        var rects = PlayProjection.Project(template, 0f, 0f, 0f, 1000f, 600f);
        var twoCorps = new[] { new CorpsPosition("child-1", -600f, 300f), new CorpsPosition("child-2", 600f, 300f) };
        var assignments = PlayCorpsAssignment.Assign(rects, twoCorps, Array.Empty<ObjectivePosition>());
        bool hasCenterRect = false;
        foreach (PlayRect rect in template.Rects) if (rect.Kind == PlayRectKind.Center) hasCenterRect = true;
        AssertTrue(hasCenterRect, "the template has a Center rect");
        var wingIntents = new[] { ZoneIntent("child-1", PlayRectKind.LeftWing), ZoneIntent("child-2", PlayRectKind.RightWing) };
        foreach (CorpsAssignment a in assignments) AssertTrue(a.RectKind != PlayRectKind.Center, "the wings outrank the Center: no corps holds it");
        AssertFalse((EnvelopmentManeuver.CenterChildId(assignments, wingIntents) != null), "with nobody on the Center the wings count from activation");

        var threeCorps = new[] { new CorpsPosition("child-1", -600f, 300f), new CorpsPosition("child-2", 0f, 300f), new CorpsPosition("child-3", 600f, 300f) };
        var full = PlayCorpsAssignment.Assign(rects, threeCorps, Array.Empty<ObjectivePosition>());
        string centerChild = null;
        foreach (CorpsAssignment a in full) if (a.RectKind == PlayRectKind.Center) centerChild = a.ChildId;
        AssertTrue(centerChild != null, "a third corps takes the Center");
        var actingIntents = new List<DirectChildIntent>();
        foreach (CorpsAssignment a in full) actingIntents.Add(ZoneIntent(a.ChildId, a.RectKind));
        AssertTrue((EnvelopmentManeuver.CenterChildId(full, actingIntents) != null), "the wings wait for the contact of a Center child that acts as it");
        AssertEqual(centerChild, EnvelopmentManeuver.CenterChildId(full, actingIntents), "the Center child is the one that acts");
        AssertFalse((EnvelopmentManeuver.CenterChildId(null, actingIntents) != null), "no assignment, no wait");
        AssertFalse((EnvelopmentManeuver.CenterChildId(full, null) != null), "no intents, no wait");

        string runtime = SourceText("Tactical", "Orchestrator", "Plays", "PlayExecutorRuntime.cs");
        AssertPinned(runtime, "string centerChildId = templateHasCenterRect ? EnvelopmentManeuver.CenterChildId(_lastResult[allianceId], intents) : null;",
            "a template without a Center rect, or without a child acting as it, makes the wings count from activation");

        // Without the Center, the offset runs from activation and the wing releases (Hold -> March).
        var state = default(ManeuverState);
        ManeuverInput input = Maneuver();
        input.TemplateHasCenter = false;
        input.CenterContactHours = -1.0;
        input.PlayActivatedHours = 10.0;
        AssertEqual(ManeuverPhase.Hold, EnvelopmentManeuver.Step(ref state, After(input, 10.1)).Phase, "before the offset");
        AssertEqual(ManeuverPhase.March, EnvelopmentManeuver.Step(ref state, After(input, 10.3)).Phase, "after 900 s from activation");
        AssertEqual("offset", state.ProceedReason, "proceeded on the offset");
    }

    private static void HoldTargetDriftStaysOutOfManeuverHash()
    {
        var a = new ChildManeuver("child-7", new ManeuverOrder { Phase = ManeuverPhase.Hold, Task = CommandTaskType.FormUp, TargetX = 0f, TargetZ = 0f, Heading = -1f, Reason = "hold-offset" }, 1f, 0f);
        var b = new ChildManeuver("child-7", new ManeuverOrder { Phase = ManeuverPhase.Hold, Task = CommandTaskType.FormUp, TargetX = 400f, TargetZ = -900f, Heading = -1f, Reason = "hold-offset" }, 1f, 0f);
        AssertEqual(EnvelopmentManeuver.Hash(new[] { a }), EnvelopmentManeuver.Hash(new[] { b }), "a hold's drifting position does not re-tick the ledger");
        var m1 = new ChildManeuver("child-7", new ManeuverOrder { Phase = ManeuverPhase.March, Task = CommandTaskType.AdvanceToAssembly, TargetX = 1000f, TargetZ = 500f, Heading = -1f, Reason = "march" }, 1f, 0f);
        var m2 = new ChildManeuver("child-7", new ManeuverOrder { Phase = ManeuverPhase.March, Task = CommandTaskType.AdvanceToAssembly, TargetX = 1400f, TargetZ = 500f, Heading = -1f, Reason = "march" }, 1f, 0f);
        AssertTrue(EnvelopmentManeuver.Hash(new[] { m1 }) != EnvelopmentManeuver.Hash(new[] { m2 }), "a march target change does re-tick");
    }

    private static void CollectOwnOnlyDirectChildren()
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        UnitFlags live = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup;
        s.AddOwn(new UnitRecord { InstanceId = 1, UnitTyp = 17, X = 0f, Z = 0f, Flags = live });      // the army headquarters
        s.AddOwn(new UnitRecord { InstanceId = 2, UnitTyp = 16, X = 100f, Z = 0f, Flags = live });     // a direct child
        s.AddOwn(new UnitRecord { InstanceId = 3, UnitTyp = 15, X = 200f, Z = 0f, Flags = live });     // a grandchild group
        s.IndexCommandTree(null);
        var xs = new List<float>(); var zs = new List<float>(); var corps = new List<CorpsPosition>();
        int count = PlayAnchor.CollectOwn(s, 15, xs, zs, corps, new HashSet<string> { "child-2" });
        AssertEqual(1, count, "only the direct child is offered a rect");
        AssertEqual("child-2", corps[0].ChildId, "the direct child");
        AssertEqual(3, xs.Count, "the centroid still reads every active unit");
        AssertEqual(0, PlayAnchor.CollectOwn(s, 15, new List<float>(), new List<float>(), new List<CorpsPosition>(), new HashSet<string>()), "no children, no candidates");
    }

    private static void ManeuverTimersUseDoctrineClock()
    {
        // A night clock frozen across the end-of-day cycle: the same NowHours gives the same verdict however many times it is stepped.
        ManeuverInput input = Maneuver();
        input.CenterContactHours = 10.5;
        var state = default(ManeuverState);
        for (int i = 0; i < 50; i++)
            AssertEqual(ManeuverPhase.Hold, EnvelopmentManeuver.Step(ref state, After(input, 10.7)).Phase, "frozen clock, no progress " + i);
        AssertEqual(ManeuverPhase.March, EnvelopmentManeuver.Step(ref state, After(input, 10.75)).Phase, "the doctrine hour moves it");
        // A clock that went back (reload) before the contact keeps holding rather than marching early.
        var back = default(ManeuverState);
        AssertEqual(ManeuverPhase.Hold, EnvelopmentManeuver.Step(ref back, After(input, 9.0)).Phase, "clock before the play's activation holds");

        string runtime = SourceText("Tactical", "Orchestrator", "Plays", "PlayExecutorRuntime.cs");
        string step = Between(runtime, "public static IReadOnlyList<ChildManeuver> StepManeuvers(", "// The Center rect's child sees an enemy within ContactMeters");
        AssertPinned(step, "double now = TacticalDoctrineTime.NowBattleHours;", "the doctrine clock");
        foreach (string real in new[] { "DateTime", "Environment.TickCount", "realtimeSinceStartup", "Time.time", "Time.unscaledTime", "Stopwatch" })
            AssertFalse(step.Contains(real), "no " + real + " in the maneuver steps");
        string pure = SourceText("Tactical", "Orchestrator", "Plays", "EnvelopmentManeuver.cs");
        foreach (string real in new[] { "DateTime", "Environment.TickCount", "realtimeSinceStartup", "Time.time" })
            AssertFalse(pure.Contains(real), "no " + real + " in EnvelopmentManeuver");
    }

    private static void ManeuverPlumbingIsWired()
    {
        string coordinator = SourceText("Tactical", "Orchestrator", "TacticalBattleCoordinatorRuntime.cs");
        AssertPinned(coordinator, ".ComputeZoneAssignments(side.AllianceId, planId, shift, childCount, axis, childIds);", "the direct children reach CollectOwn");
        AssertPinned(coordinator, "PlayExecutorRuntime.StepManeuvers(", "the coordinator steps the maneuvers each direct-child cycle");
        AssertPinned(coordinator, "side.Army.CurrentDirectChildIntents, evidence, axis, side.Army.CurrentBattleStance", "from the side's evidence and stance");
        AssertPinned(coordinator, "unchecked(CommandNodeIntentsHash(side.Army.CurrentCommandNodeIntents) * 31 + side.Army.ManeuverHash)", "a phase change ticks the ledger gate");
        string runtime = SourceText("Tactical", "Orchestrator", "Plays", "PlayExecutorRuntime.cs");
        AssertPinned(runtime, "if (!Plugin.FlankResponseEnabled() || planId == BattlePlanId.Unknown", "the maneuver is behind Enable Flank Response And Envelopment");
        AssertPinned(runtime, "EnvelopmentManeuver.InwardLegClear(_riverSamples, n)", "the river checks read the terrain grid");
        AssertPinned(runtime, "EnvelopmentManeuver.CorridorEnemies(snapshot, anchorX, anchorZ, inwardX, inwardZ, TerrainHeightAt, out float maxHeight)", "the height check reads terrain heights and perceived enemies");
        AssertPinned(runtime, "grid.SampleLine(anchorX, anchorZ, inwardX, inwardZ, EnvelopmentManeuver.InwardStepMeters, _riverSamples)", "2 m samples on the inward leg");
        string army = SourceText("Tactical", "Orchestrator", "ArmyOrchestrator.cs");
        AssertPinned(army, "EnvelopmentManeuver.ExpandToNodes(_maneuvers, _commandNodeIntents)", "the army expands the orders to nodes");
        string ledger = SourceText("Tactical", "Orchestrator", "TacticalOperationsLedgerRuntime.cs");
        AssertPinned(ledger, "_currentForce.ReserveFraction, stance, maneuvers, reverseSlope);", "the ledger hands the table to the doctrine build");
        string producers = SourceText("Tactical", "Battle", "Producers", "PostureIntentProducer.cs");
        AssertPinned(producers, "if (kind == PostureExecutionTarget.DoctrinePrimaryTarget && order.ManeuverHold) return false;", "a hold burns no nav plan");
        AssertPinned(producers, "if (hasOrder && order.ManeuverHeading >= 0f && rec.UnitTyp > TacticalUnitType.MaxCombat && !physical.ActiveMove &&", "a Turn faces the group");
        // The maneuver data reaches the plays: rg RoleForRectKind|RectAnchorX|StartTimeOffsetSeconds shows readers outside Plays/.
        string allocator = SourceText("Tactical", "Orchestrator", "DirectChildAllocator.cs");
        AssertPinned(allocator, "zone.RectAnchorX, zone.RectAnchorZ, zone.StartTimeOffsetSeconds", "the allocator reads the anchor and the offset");
        AssertPinned(allocator, "fromZone[i] ? ManeuverOf(zoneOf[i]) : default(ManeuverSpec)", "a maneuver rides only with a zone-assigned role");
    }

    private static void ManeuverAllocatesNothingAfterWarmup()
    {
        var state = default(ManeuverState);
        ManeuverInput input = Maneuver();
        input.CenterContactHours = 10.5;
        TacticalFrontAxis axis = TacticalFrontAxis.FromCentroids(0f, 0f, 0f, 1500f).WithOwnExtent(-300f, 300f).WithEnemyExtent(-250f, 250f);
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        s.IndexCommandTree(null);
        for (int i = 0; i < 40; i++)
            s.AddObservation(new EnemyObservation { InstanceId = i + 1, X = -200f + i * 10f, Z = 1000f + i, Strength = 100f, State = ObservationState.Visible, Confidence01 = 1f });
        Func<float, float, float> heights = HeightOfZero;
        int[] ids = { 0, 1, 2, 3 };
        float sink = 0f;
        AssertNoAllocation(() =>
        {
            state = default(ManeuverState);
            sink += EnvelopmentManeuver.Step(ref state, After(input, 10.0)).TargetX;
            sink += EnvelopmentManeuver.Step(ref state, After(input, 10.75)).TargetX;
            input.ChildArrived = false;
            EnvelopmentManeuver.EnemyPressureSplit(s, in axis, out float l, out float r);
            sink += l + r;
            sink += EnvelopmentManeuver.CorridorEnemies(s, 0f, 900f, 0f, 1500f, heights, out float max) + max;
            sink += EnvelopmentManeuver.OutflankPoint(in axis, 1500f, l, r, true, out float ox, out float oz) ? ox + oz : 0f;
            sink += EnvelopmentManeuver.EnemyLineEnd(in axis, 0f, 1500f, true, out float ex, out float ez) ? ex + ez : 0f;
            sink += EnvelopmentManeuver.InwardLegClear(ids, ids.Length) ? 1f : 0f;
            sink += EnvelopmentManeuver.LaneOffset(2, 5);
        }, 100, "the maneuver steps and geometry");
        GC.KeepAlive(sink);
    }

    private static float HeightOfZero(float x, float z) => 0f;
}
