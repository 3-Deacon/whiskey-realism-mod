using System;
using System.IO;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;
using static TestFixtures;

// BC14W: the copy sites of the works-hold marks (spec § 15 item 7's census, rows W): the operational state's Reason and PreGuardTask, the
// doctrine order's formation, preparation and PreGuardTask, the leaf entry's WorksHeld and the maneuver node's formation. A field a copy
// drops is a hold that quietly turns back into an attack, so each copy has its own test.
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] WorksCopyTests() => new (string, Action)[]
    {
        ("bc14w READER-STATE-COPY the works-hold reason and pre-guard task survive every copy that keeps a hold and no copy that leaves it", WReaderStateCopy),
    };

    static CommandNodeOperationalState HeldState(CommandTaskType task = CommandTaskType.HoldObjective)
        => new CommandNodeOperationalState("node-7", CommandEchelonKind.DivisionLike, CommandNodeRole.MainEffort, task, CommandTaskState.Committed,
            10f, 20f, 90f, WorksHoldGuard.Reason, CommandTaskType.AttackObjective);

    static void WReaderStateCopy()
    {
        CommandNodeOperationalState held = HeldState();
        AssertEqual("works-hold", held.Reason, "the constant is the literal the spec names");

        // A rewrite to another holding task keeps the mark; a rewrite to any other task is no longer a works hold.
        CommandNodeOperationalState sameHold = held.WithRoleAndTask(CommandNodeRole.SupportingAttack, CommandTaskType.HoldObjective);
        AssertEqual(WorksHoldGuard.Reason, sameHold.Reason, "WithRoleAndTask to HoldObjective keeps Reason");
        AssertEqual(CommandTaskType.AttackObjective, sameHold.PreGuardTask, "and PreGuardTask");
        AssertEqual(CommandNodeRole.SupportingAttack, sameHold.Role, "the role still changes");
        CommandNodeOperationalState probe = held.WithRoleAndTask(CommandNodeRole.Probe, CommandTaskType.Probe);
        AssertTrue(probe.Reason == null, "WithRoleAndTask to Probe loses Reason");
        AssertEqual(CommandTaskType.None, probe.PreGuardTask, "and PreGuardTask");
        AssertEqual(10f, probe.X, "every other field is kept");

        // The Defender stance guard keeps the mark when it rewrites a task to a hold.
        CommandNodeOperationalState attack = HeldState(CommandTaskType.AttackObjective);
        CommandDoctrineOrder none = default(CommandDoctrineOrder);
        AssertTrue(PostureStanceGuard.Apply(BattleStance.Defender, ref attack, false, ref none), "the guard rewrote an attack for a Defender");
        AssertEqual(CommandTaskType.HoldObjective, attack.Task, "to a hold");
        AssertEqual(WorksHoldGuard.Reason, attack.Reason, "PostureStanceGuard.Apply keeps Reason");
        AssertEqual(CommandTaskType.AttackObjective, attack.PreGuardTask, "and PreGuardTask");

        // The exploit overlay takes the marks from the exploit's state, never the ledger's: a breach exploit on a guarded child is unmarked.
        CommandNodeOperationalState exploitUnmarked = new CommandNodeOperationalState("node-7", CommandEchelonKind.Unknown, CommandNodeRole.SupportingAttack,
            CommandTaskType.AttackObjective, CommandTaskState.Committed, 0f, 0f, 0f);
        CommandNodeOperationalState overlaid = ExploitPosture.Overlay(held, true, exploitUnmarked, ExploitOverrideKind.Exploit);
        AssertTrue(overlaid.Reason == null, "ExploitPosture.Overlay: the exploit's state decides Reason");
        AssertEqual(CommandTaskType.None, overlaid.PreGuardTask, "and PreGuardTask");
        AssertEqual(CommandTaskType.AttackObjective, overlaid.Task, "the exploit's task");
        AssertEqual(CommandEchelonKind.DivisionLike, overlaid.Echelon, "the ledger's identity is kept");
        CommandNodeOperationalState exploitMarked = new CommandNodeOperationalState("node-7", CommandEchelonKind.Unknown, CommandNodeRole.SupportingAttack,
            CommandTaskType.HoldObjective, CommandTaskState.Committed, 0f, 0f, 0f, WorksHoldGuard.Reason, CommandTaskType.AttackObjective);
        CommandNodeOperationalState overlaidMarked = ExploitPosture.Overlay(HeldState(CommandTaskType.AttackObjective), true, exploitMarked, ExploitOverrideKind.Exploit);
        AssertEqual(WorksHoldGuard.Reason, overlaidMarked.Reason, "an exploit state the guard marked keeps its mark through the overlay");
        AssertEqual(CommandTaskType.AttackObjective, overlaidMarked.PreGuardTask, "and its pre-guard task");
        CommandNodeOperationalState follower = ExploitPosture.Overlay(held, true, exploitUnmarked, ExploitOverrideKind.Follow);
        AssertEqual(WorksHoldGuard.Reason, follower.Reason, "a follower keeps its ledger state whole");

        // Create, the fallback builder and the shipped positional constructors give none.
        CommandNodeOperationalState created = CommandNodeOperationalState.Create("node-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.Reserve, CommandTaskType.ReserveWait, 1f, 2f, 3f);
        AssertTrue(created.Reason == null && created.PreGuardTask == CommandTaskType.None, "Create gives none");
        var five = new CommandNodeOperationalState("node-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.Reserve, CommandTaskType.ReserveWait, CommandTaskState.Planning);
        AssertTrue(five.Reason == null && five.PreGuardTask == CommandTaskType.None, "the five-argument constructor gives none");
        var eight = new CommandNodeOperationalState("node-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.Reserve, CommandTaskType.ReserveWait, CommandTaskState.Planning, 1f, 2f, 3f);
        AssertTrue(eight.Reason == null && eight.PreGuardTask == CommandTaskType.None, "the eight-argument constructor gives none");
        var operation = new OperationRecord(TacticalOperationShape.DefensiveNetwork, TacticalOperationPhase.Forming, "ridge-a", 0f);
        bool built = CommandNodeOperationsRuntime.TryBuildSingle(
            new CommandNodeIntent("node--27662", "node--26354", DirectChildRole.Main, DirectChildAxis.SectorAxis, 2, 100, 3),
            operation, new[] { ObjectiveRecordFor("ridge-a", 900f, 100f, TacticalObjectiveStatus.WeaklyHeld) }, BattleStance.Meeting, out CommandNodeOperationalState single);
        AssertTrue(built, "the fallback state is built");
        AssertTrue(single.Reason == null && single.PreGuardTask == CommandTaskType.None, "TryBuildSingle gives none");
    }
}
