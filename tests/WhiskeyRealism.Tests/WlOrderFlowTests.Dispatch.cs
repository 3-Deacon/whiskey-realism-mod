using System;
using System.Text.RegularExpressions;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.PlayerOrders;

using static TestHarness;

// The executor's dispatch to the player (2026-10-17 smoke, orders 6 and 13): a dispatch is delivered only while the group
// is still under the task it was made for, once that task has held for the task-hold time; interrupted-path recovery and a
// leaf brigade's formation slot are maintenance, not orders; a target within 110 m of the last dispatched one is the same
// order again.
internal static partial class WlOrderFlowTests
{
    private static void PutDispatch(WlOrderFlow flow, double issued, int task = 4, int unit = 1, int type = 1, float x = 0f)
    {
        CourierEntry e = QEntry(issuer: unit, type: type, origin: OrderOrigin.Executor, x: x, issuedAt: issued, dueAt: issued + Leg);
        e.UnitId = unit;
        e.DispatchTask = task;
        flow.Queue.TryEnqueue(in e, 0.0, out _);
    }

    private static void DispatchWhoseTaskChangedIsDiscarded()
    {
        var host = new FakeHost { Now = 10.5, Slot = null, TaskChangedSince = true };
        var flow = NewFlow(host);
        PutDispatch(flow, issued: 10.0);
        flow.RunPass();
        AssertEqual(0, host.Calls, "the group has another task now: the player is not told of the old one");
        AssertEqual("1:discard:task-changed", RowsOf(host), "with the reason");
        AssertEqual(0, flow.Queue.Count, "and it is gone");
    }

    private static void DispatchWaitsUntilItsTaskHasHeld()
    {
        var host = new FakeHost { Now = 10.5, Slot = null, TaskHeldAt = 10.6 };
        var flow = NewFlow(host);
        PutDispatch(flow, issued: 10.0);
        flow.RunPass();
        AssertEqual(0, host.Calls, "the task began at 10.58: the hold is not met at 10.5");
        AssertEqual("1:defer:task-hold", RowsOf(host), "one row says why");
        AssertEqual(1, flow.Queue.Count, "the order is requeued");
        host.Now = 10.59;
        flow.RunPass();
        AssertEqual(0, host.Calls, "not yet at 10.59");
        AssertEqual("1:defer:task-hold", RowsOf(host), "and the row is not repeated while it is not due");
        host.Now = 10.6;
        flow.RunPass();
        AssertEqual(1, host.Calls, "shown when the hold is met");
    }

    private static void DispatchWhoseTaskHasHeldIsDeliveredAtOnce()
    {
        var host = new FakeHost { Now = 10.5, Slot = null, TaskHeldAt = 9.0 };
        var flow = NewFlow(host);
        PutDispatch(flow, issued: 10.0);
        flow.RunPass();
        AssertEqual(1, host.Calls, "a task that has held for longer than the hold costs nothing");
        AssertEqual(1, host.TaskAsks, "the task was asked about once");
    }

    private static void OrdersWithoutADispatchTaskAreNotAskedAboutATask()
    {
        var host = new FakeHost { Now = 10.5, Slot = null, TaskChangedSince = true };
        var flow = NewFlow(host);
        CourierEntry e = QEntry(issuer: 1, type: 2, origin: OrderOrigin.Vanilla, issuedAt: 10.0, dueAt: 10.0);
        flow.Queue.TryEnqueue(in e, 0.0, out _);
        flow.RunPass();
        AssertEqual(0, host.TaskAsks, "vanilla's, #62's, the release and the follow-up carry no task");
        AssertEqual(1, host.Calls, "and are delivered");
    }

    private static void TaskGateComesAfterTheMoveGate()
    {
        // The move must be carried out first: while it is in flight the task is not asked about.
        var host = new FakeHost { Now = 10.5, Slot = null, Move = MoveState.InFlight, TaskChangedSince = true };
        var flow = NewFlow(host);
        CourierEntry e = QEntry(issuer: 1, type: 1, origin: OrderOrigin.Executor, issuedAt: 10.0, dueAt: 10.0);
        e.UnitId = 1;
        e.DispatchTask = 4;
        e.AwaitsMove = true;
        e.LegHours = Leg;
        e.Carrier = new WlMoveCarrier { UnitId = 1, OrderType = 1 };
        flow.Queue.TryEnqueue(in e, 0.0, out _);
        flow.RunPass();
        AssertEqual(0, host.TaskAsks, "an order waiting for its move is not asked about its task yet");
        host.Move = MoveState.Executed;
        host.Now = 10.6;
        flow.RunPass();
        host.Now = 10.6 + Leg;
        flow.RunPass();
        AssertEqual("1:defer:move-in-flight,1:discard:task-changed", RowsOf(host), "once the move is carried out the changed task discards it: " + RowsOf(host));
    }

    private static void TaskChangedIsStructuralForP62()
    {
        AssertTrue(WlOrderFlow.IsStructural(WlOrderFlow.TaskChanged), "a changed task says nothing about the player's own panel logic");
    }

    // Maintenance moves announce nothing: the dispatch decision is the executor's plan step 9 and the posture intent plan marks the move.
    private static void MaintenanceMovesAreNotDispatched()
    {
        string contracts = Code("src/WhiskeyRealism/Tactical/Battle/BattleCoreContracts.cs");
        AssertTrue(Regex.IsMatch(contracts, @"Maintenance = 65536,"), "the intent flag exists");
        string exec = Code("src/WhiskeyRealism/Tactical/Battle/OrderExecutor.cs");
        AssertTrue(Regex.IsMatch(exec, @"if \(w\.Channel == OrderChannel\.Move && !immediate && \(w\.Flags & IntentFlags\.Maintenance\) == 0 && plan\.Targets\.PlayerShareNeedsDispatch\)"),
            "step 9 dispatches only a move that is not maintenance");
        string plan = Code("src/WhiskeyRealism/Tactical/Operations/CommandPostureExecutor.cs");
        string submit = BodyOf(plan, "private static int SubmitMove(");
        AssertTrue(Regex.IsMatch(submit, @"if \(r\.Action == PostureExecutionAction\.RecoverInterruptedOrder\) move\.Flags \|= IntentFlags\.Maintenance;"),
            "a recovery of an interrupted order marks its move");
        string leaf = BodyOf(plan, "public static BattleOrderIntent LeafMove(");
        AssertTrue(leaf.Contains("IntentFlags.Maintenance"), "a leaf brigade's move to its formation slot is maintenance too");
    }

    private static void DispatchRuntimeAppliesTheMemory()
    {
        string run = Code("src/WhiskeyRealism/Tactical/Battle/TacticalPlayerDispatchRuntime.cs");
        string emit = BodyOf(run, "internal static bool Emit(");
        AssertTrue(emit.Contains("Memory.IsRepeat(") && emit.Contains("Memory.RecordDispatch("), "Emit asks the memory first and records a queued dispatch");
        AssertTrue(emit.IndexOf("Memory.IsRepeat(", StringComparison.Ordinal) < emit.IndexOf("WlOrderFlowRuntime.EnqueueWhiskey(", StringComparison.Ordinal),
            "the repeat test comes before the enqueue");
        AssertTrue(run.Contains("BattleScope.Register(\"tactical-player-dispatch\", () => { Dedupe.ClearForBattleBoundary(string.Empty); Memory.Clear(); });"),
            "the memory is battle-scoped");
        string exec = BodyOf(Code("src/WhiskeyRealism/Tactical/Battle/OrderExecutorRuntime.cs"), "internal static int Execute(");
        AssertTrue(Regex.IsMatch(exec, @"if \(executed > 0 && winner\.Task != CommandTaskType\.None\)\s*TacticalPlayerDispatchRuntime\.NoteTask\(winner\.GroupId, winner\.Task, nowHours\);"),
            "every executed write of a group notes the group's task");
    }
}
