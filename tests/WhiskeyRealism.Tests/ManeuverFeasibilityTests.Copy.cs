using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
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
}
