using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static TestFixtures;

// BC14 copy sites (spec § 13.1 S9): a field added to a value that is copied field by field must be forwarded by every copy,
// or the copy silently drops it. Each test builds the value with the field set and passes it through the copy.
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] CopySiteTests() => new (string, Action)[]
    {
        ("bc14 READER-DOCTRINE-COPY a doctrine order keeps its via through WithSop, WithTask, WithManeuver and WithExploit", ReaderDoctrineCopy),
        ("bc14 READER-CHILD-COPY a direct-child intent keeps its guard point through the held-main swap and WithGuardPoint", ReaderChildCopy),
        ("bc14 READER-MIRROR-COPY a mirrored projection carries its authored anchors through Assign, AssignInto and ManeuverOf", ReaderMirrorCopy),
        ("bc14 PROJECT-PARITY the pooled projection equals Project on every template", ProjectParity),
        ("bc14 ASSIGN-PARITY the pooled assignment equals Assign on every template", AssignParity),
    };

    static CommandDoctrineOrder PlainOrder() => CommandDoctrineOrder.Create("node-1", CommandNodeRole.MainEffort, CommandTaskType.AttackObjective,
        "obj-1", DoctrineTargetPoint.From(100f, 200f), DoctrineTargetPoint.None, DoctrineTargetPoint.None, DoctrineAllowedIdleReason.None,
        900f, 12f, 0.8f, "plain");

    static void ReaderDoctrineCopy()
    {
        CommandDoctrineOrder plain = PlainOrder();
        AssertFalse(plain.HasVia, "Create gives no via");
        AssertTrue(float.IsNaN(plain.ViaX) && float.IsNaN(plain.ViaZ), "no via reads NaN");
        AssertFalse(default(CommandDoctrineOrder).HasVia, "the 'no order' value carries no via");

        CommandDoctrineOrder via = plain.WithVia(310f, -45f);
        AssertTrue(via.HasVia, "WithVia sets the via");
        AssertNear(310f, via.ViaX, 1e-4f, "via x");
        AssertNear(-45f, via.ViaZ, 1e-4f, "via z");
        AssertEqual(plain.NodeId, via.NodeId, "WithVia keeps the node");
        AssertEqual(plain.Task, via.Task, "WithVia keeps the task");
        AssertEqual(plain.ObjectiveId, via.ObjectiveId, "WithVia keeps the objective");
        AssertNear(plain.PrimaryTarget.X, via.PrimaryTarget.X, 1e-4f, "WithVia keeps the target");
        AssertEqual(plain.Reason, via.Reason, "WithVia keeps the reason");
        AssertNear(plain.Confidence01, via.Confidence01, 1e-4f, "WithVia keeps the confidence");

        AssertTrue(via.WithSop(via.Sop).HasVia, "WithSop keeps the via");
        CommandDoctrineOrder task = via.WithTask(CommandTaskType.HoldObjective, "t");
        AssertTrue(task.HasVia && Math.Abs(task.ViaX - 310f) < 1e-4f && Math.Abs(task.ViaZ + 45f) < 1e-4f, "WithTask keeps the via");
        CommandDoctrineOrder maneuver = via.WithManeuver(DoctrineTargetPoint.From(5f, 6f), DoctrineAllowedIdleReason.None, 90f, false, "m");
        AssertTrue(maneuver.HasVia && Math.Abs(maneuver.ViaX - 310f) < 1e-4f, "WithManeuver keeps the via");
        CommandDoctrineOrder exploit = via.WithExploit(7);
        AssertTrue(exploit.HasVia && Math.Abs(exploit.ViaZ + 45f) < 1e-4f, "WithExploit keeps the via");
        AssertEqual(7, exploit.ExploitId, "WithExploit still sets the exploit id");
    }

    static void ReaderChildCopy()
    {
        DirectChildIntent plain = DirectIntent("child-1", DirectChildRole.RefuseRight, DirectChildAxis.Hold, 4, 0.3f);
        AssertFalse(plain.HasGuardPoint, "an ordinary intent has no guard point");

        DirectChildIntent guarded = plain.WithGuardPoint(1000f, 210f, 270f);
        AssertTrue(guarded.HasGuardPoint, "WithGuardPoint sets it");
        AssertNear(1000f, guarded.GuardX, 1e-4f, "guard x");
        AssertNear(210f, guarded.GuardZ, 1e-4f, "guard z");
        AssertNear(270f, guarded.GuardHeading, 1e-4f, "guard heading");
        AssertEqual(plain.ChildId, guarded.ChildId, "WithGuardPoint keeps the child");
        AssertEqual(plain.Role, guarded.Role, "WithGuardPoint keeps the role");
        AssertEqual(plain.Axis, guarded.Axis, "WithGuardPoint keeps the axis");
        AssertEqual(plain.AxisSector, guarded.AxisSector, "WithGuardPoint keeps the axis sector");
        AssertEqual(plain.PrimarySector, guarded.PrimarySector, "WithGuardPoint keeps the primary sector");
        AssertNear(plain.SupportPriority01, guarded.SupportPriority01, 1e-4f, "WithGuardPoint keeps the support priority");

        // The held-main challenge swaps roles through DirectChildAllocator.With: the incumbent takes Main and the challenger the
        // incumbent's old role. Both children carry a guard point here, and both must keep it.
        DirectChildIntent incumbent = DirectIntent("child-1", DirectChildRole.SupportMain, DirectChildAxis.SectorAxis, 2, 0.7f).WithGuardPoint(1f, 2f, 3f);
        DirectChildIntent challenger = DirectIntent("child-2", DirectChildRole.Main, DirectChildAxis.SectorAxis, 3, 1f).WithGuardPoint(4f, 5f, 6f);
        var state = new MainRoleHysteresisState { IncumbentMainId = "child-1" };
        var held = DirectChildAllocator.StabilizeMain(new[] { incumbent, challenger }, state);
        AssertEqual(DirectChildRole.Main, held[0].Role, "the incumbent is held in Main");
        AssertEqual(DirectChildRole.SupportMain, held[1].Role, "the challenger takes the incumbent's old role");
        AssertTrue(held[0].HasGuardPoint && Math.Abs(held[0].GuardX - 1f) < 1e-4f, "the held incumbent keeps its guard point");
        AssertTrue(held[1].HasGuardPoint && Math.Abs(held[1].GuardX - 4f) < 1e-4f && Math.Abs(held[1].GuardHeading - 6f) < 1e-4f, "the swapped challenger keeps its guard point");
    }

    static readonly BattlePlanId[] AllTemplatePlans =
    {
        BattlePlanId.DoubleEnvelopment, BattlePlanId.MobileFlankMarch, BattlePlanId.WideFlankApproach, BattlePlanId.ManeuverAndFix,
        BattlePlanId.FrontalShockAssault, BattlePlanId.ForcedFrontalAttack, BattlePlanId.ContinuousAttrition, BattlePlanId.HesitatedCommit,
        BattlePlanId.PreparedDefense, BattlePlanId.ReverseSlopeDefense, BattlePlanId.DelayingDefense, BattlePlanId.CavalryRaid,
        BattlePlanId.CavalryScreeningDelay, BattlePlanId.MeetingEngagement, BattlePlanId.GenericAggressive, BattlePlanId.GenericCautious,
        BattlePlanId.GenericMethodical, BattlePlanId.GenericDesperate,
    };

    static void ReaderMirrorCopy()
    {
        PlayTemplate template = PlayTemplateCatalog.Lookup(BattlePlanId.ManeuverAndFix);
        var noCrest = default(TerrainLine);
        // Army at (0, 0) facing +z (forward 600 to the enemy at (0, 600)), frontage 600: the right wing's relative anchor (0.85, 0.5)
        // lands at lateral +255, forward 300; mirrored it lands at lateral -255.
        var plain = PlayProjection.Project(template, 0f, 0f, 0f, 600f, 600f, in noCrest, false);
        var mirrored = PlayProjection.Project(template, 0f, 0f, 0f, 600f, 600f, in noCrest, true);
        int wing = -1;
        for (int i = 0; i < template.Rects.Count; i++) if (template.Rects[i].Kind == PlayRectKind.RightWing) wing = i;
        AssertTrue(wing >= 0, "the template has a right wing");
        AssertFalse(plain[wing].Mirrored, "a plain projection is not mirrored");
        AssertNear(0f, plain[wing].AuthoredX, 1e-4f, "and sets no authored anchor");
        AssertNear(0f, plain[wing].AuthoredZ, 1e-4f, "and sets no authored anchor");
        AssertNear(255f, plain[wing].AnchorX, 1e-3f, "the right wing's lateral on a plain projection");
        AssertTrue(mirrored[wing].Mirrored, "the mirrored projection says so");
        AssertNear(-255f, mirrored[wing].AnchorX, 1e-3f, "the lateral offset is negated in the projection frame");
        AssertNear(plain[wing].AnchorZ, mirrored[wing].AnchorZ, 1e-3f, "the forward offset is not");
        AssertNear(255f, mirrored[wing].AuthoredX, 1e-3f, "the authored anchor is the un-mirrored one");
        AssertNear(plain[wing].AnchorZ, mirrored[wing].AuthoredZ, 1e-3f, "authored z");

        // Assign and AssignInto copy it (and TemplateEnvelops) into the corps assignment.
        var corps = new List<CorpsPosition>
        {
            new CorpsPosition("child-1", -300f, 0f), new CorpsPosition("child-2", 0f, 0f), new CorpsPosition("child-3", 300f, 0f),
        };
        var objectives = Array.Empty<ObjectivePosition>();
        var viaAssign = PlayCorpsAssignment.Assign(mirrored, corps, objectives);
        var into = new CorpsAssignment[template.Rects.Count];
        int n = PlayCorpsAssignment.AssignInto(mirrored, corps, objectives, into);
        AssertEqual(viaAssign.Count, n, "both assign the same number of rects");
        bool sawWing = false;
        for (int i = 0; i < n; i++)
        {
            AssertTrue(viaAssign[i].ProjectedMirrored && into[i].ProjectedMirrored, "every assignment of a mirrored projection says so");
            AssertTrue(viaAssign[i].TemplateEnvelops && into[i].TemplateEnvelops, "ManeuverAndFix envelops: TemplateEnvelops");
            if (into[i].RectKind == PlayRectKind.RightWing)
            {
                sawWing = true;
                AssertNear(255f, into[i].AuthoredAnchorX, 1e-3f, "AssignInto carries the authored anchor");
                AssertNear(255f, viaAssign[i].AuthoredAnchorX, 1e-3f, "Assign carries the authored anchor");
                AssertNear(-255f, into[i].RectAnchorX, 1e-3f, "while the rect anchor is the mirrored one");
                // ManeuverOf copies it into the spec.
                ManeuverSpec spec = DirectChildAllocator.ManeuverOf(in into[i]);
                AssertTrue(spec.HasValue && spec.Envelops, "an enveloping spec");
                AssertTrue(spec.ProjectedMirrored, "the spec knows it was projected mirrored");
                AssertNear(255f, spec.AuthoredAnchorX, 1e-3f, "and keeps the authored anchor for BC11's one mirrored try");
                AssertNear(plain[wing].AnchorZ, spec.AuthoredAnchorZ, 1e-3f, "authored z");
            }
        }
        AssertTrue(sawWing, "the wing was assigned");
        var plainAssigned = PlayCorpsAssignment.Assign(plain, corps, objectives);
        for (int i = 0; i < plainAssigned.Count; i++) AssertFalse(plainAssigned[i].ProjectedMirrored, "a plain projection's assignments are not mirrored");
        // A template with no enveloping rect: TemplateEnvelops stays false.
        var frontal = PlayCorpsAssignment.Assign(PlayProjection.Project(PlayTemplateCatalog.Lookup(BattlePlanId.FrontalShockAssault), 0f, 0f, 0f, 600f, 600f, in noCrest, false), corps, objectives);
        for (int i = 0; i < frontal.Count; i++) AssertFalse(frontal[i].TemplateEnvelops, "FrontalShockAssault has no enveloping rect");
    }

    static void ProjectParity()
    {
        var noCrest = default(TerrainLine);
        var rects = new WorldPlayRect[8];
        uint state = 0xBADC0DEu;
        float Next() { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return (state & 0xFFFFFFu) / (float)0x1000000; }
        foreach (BattlePlanId plan in AllTemplatePlans)
        {
            PlayTemplate template = PlayTemplateCatalog.Lookup(plan);
            for (int trial = 0; trial < 5; trial++)
            {
                float ax = Next() * 1000f, az = Next() * 1000f, ex = Next() * 1000f, ez = Next() * 1000f, frontage = 200f + Next() * 1500f;
                var expected = PlayProjection.Project(template, ax, az, ex, ez, frontage);
                int n = PlayProjection.ProjectInto(template, ax, az, ex, ez, frontage, in noCrest, false, rects);
                AssertEqual(expected.Count, n, plan + " rect count");
                for (int i = 0; i < n; i++)
                {
                    AssertNear(expected[i].AnchorX, rects[i].AnchorX, 1e-4f, plan + " anchor x " + i);
                    AssertNear(expected[i].AnchorZ, rects[i].AnchorZ, 1e-4f, plan + " anchor z " + i);
                    AssertNear(expected[i].HalfExtentX, rects[i].HalfExtentX, 1e-4f, plan + " half x " + i);
                    AssertNear(expected[i].HalfExtentZ, rects[i].HalfExtentZ, 1e-4f, plan + " half z " + i);
                    AssertEqual(expected[i].Kind, rects[i].Kind, plan + " kind " + i);
                    AssertEqual(expected[i].Priority, rects[i].Priority, plan + " priority " + i);
                    AssertEqual(expected[i].Envelops, rects[i].Envelops, plan + " envelops " + i);
                    AssertFalse(rects[i].Mirrored, plan + " plain projection is not mirrored");
                }
            }
        }
    }

    static void AssignParity()
    {
        var noCrest = default(TerrainLine);
        var into = new CorpsAssignment[8];
        uint state = 0xFEEDBEEFu;
        float Next() { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return (state & 0xFFFFFFu) / (float)0x1000000; }
        var objectives = new List<ObjectivePosition> { new ObjectivePosition("o1", 100f, 300f, true), new ObjectivePosition("o2", -200f, 100f, false) };
        foreach (BattlePlanId plan in AllTemplatePlans)
        {
            PlayTemplate template = PlayTemplateCatalog.Lookup(plan);
            for (int trial = 0; trial < 6; trial++)
            {
                var rects = PlayProjection.Project(template, 0f, 0f, 0f, 600f, 600f, in noCrest, trial % 2 == 1);
                int corpsCount = 1 + (int)(Next() * 7f);
                var corps = new List<CorpsPosition>();
                for (int c = 0; c < corpsCount; c++) corps.Add(new CorpsPosition("child-" + (c + 1), -500f + Next() * 1000f, -100f + Next() * 300f));
                var expected = PlayCorpsAssignment.Assign(rects, corps, trial < 3 ? objectives : (IReadOnlyList<ObjectivePosition>)Array.Empty<ObjectivePosition>());
                int n = PlayCorpsAssignment.AssignInto(rects, corps, trial < 3 ? objectives : (IReadOnlyList<ObjectivePosition>)Array.Empty<ObjectivePosition>(), into);
                AssertEqual(expected.Count, n, plan + " assignment count, trial " + trial);
                for (int i = 0; i < n; i++)
                {
                    AssertEqual(expected[i].ChildId, into[i].ChildId, plan + " child " + i);
                    AssertEqual(expected[i].RectKind, into[i].RectKind, plan + " rect kind " + i);
                    AssertEqual(expected[i].RectIndex, into[i].RectIndex, plan + " rect index " + i);
                    AssertNear(expected[i].RectAnchorX, into[i].RectAnchorX, 1e-4f, plan + " anchor x " + i);
                    AssertEqual(expected[i].Envelops, into[i].Envelops, plan + " envelops " + i);
                }
            }
        }
    }
}
