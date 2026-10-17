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
        string flowCode = Code(FlowRuntime);
        AssertTrue(Regex.IsMatch(BodyOf(flowCode, "private static CaptureVerdict Route("), @"entry\.DispatchTask = call\.DispatchTask;"), "the capture puts the dispatch's task on the entry");
        AssertTrue(Regex.IsMatch(BodyOf(flowCode, "internal static bool EnqueueWhiskey("), @"Carrier = carrier, DispatchTask = dispatchTask,"), "and Whiskey's emitter hands it to the capture");
        string rowBody = BodyOf(flowCode, "public void Row(in CourierEntry e, string action, string reason)");
        AssertTrue(Regex.IsMatch(rowBody, @"if \(action == ""discard"" && e\.Origin == OrderOrigin\.Executor\)\s*Tactical\.Battle\.TacticalPlayerDispatchRuntime\.Memory\.ForgetDispatch\(e\.UnitId\);"),
            "an executor dispatch that was discarded is forgotten, so the next order to that place is sent");
        AssertTrue(BodyOf(flowCode, "public bool TaskHolds(").Contains("TacticalPlayerDispatchRuntime.TaskHolds(e.UnitId, e.DispatchTask, out heldAtHours)"), "the host asks the memory for the entry's unit and task");
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

    // Vanilla's orders that say where the chain is going (smoke 2026-10-17 orders 2, 4, 10: a support order to a zone the
    // brigade never reached, or was halted from by 4th Division's CheckEnemyContact 117379-117381).
    private static void PutPursuit(WlOrderFlow flow, double issued, int unit = 1, int type = 2, float x = 0f)
    {
        CourierEntry e = QEntry(issuer: unit, type: type, origin: OrderOrigin.Vanilla, x: x, issuedAt: issued, dueAt: issued + Leg);
        e.UnitId = unit;
        e.Pursuit = true;
        e.Carrier = new WlMoveCarrier { UnitId = unit, OrderType = WlMoveCarrier.NoQueueEntry, X = x };
        flow.Queue.TryEnqueue(in e, 0.0, out _);
    }

    private static void VanillaOrderWhoseCarrierAbandonedTheMoveIsDiscarded()
    {
        var host = new FakeHost { Now = 10.5, Slot = null, Move = MoveState.Lost };
        var flow = NewFlow(host);
        PutPursuit(flow, issued: 10.0);
        flow.RunPass();
        AssertEqual(0, host.Calls, "the brigade no longer pursues the point: the player is not told to support there");
        AssertEqual("2:discard:chain-abandoned", RowsOf(host), "with the reason");
        AssertEqual(0, flow.Queue.Count, "and it is gone");
    }

    private static void VanillaOrderWhoseCarrierPursuesIsDeliveredWithItsDueTime()
    {
        var host = new FakeHost { Now = 10.5, Slot = null, Move = MoveState.Executed, Basis = MoveBasis.Path };
        var flow = NewFlow(host);
        PutPursuit(flow, issued: 10.0);
        flow.RunPass();
        AssertEqual(1, host.Calls, "the carrier holds a path that ends at the point: delivered, on the courier's own time");
        AssertEqual(MoveBasis.Path, host.GateOnLastRow, "the verdict rides on the row");
    }

    private static void PursuitIsCheckedAtDeliveryNotAtCapture()
    {
        var host = new FakeHost { Now = 10.0, Slot = null, Move = MoveState.Lost };
        var flow = NewFlow(host);
        PutPursuit(flow, issued: 10.0);
        flow.RunPass();
        AssertEqual(0, host.MoveAsks, "an order that is not yet due is not looked at: its courier is still riding");
        AssertEqual(1, flow.Queue.Count, "it stays queued");
        host.Now = 10.0 + Leg;
        flow.RunPass();
        AssertEqual(1, host.MoveAsks, "asked once, when it is due");
    }

    private static void AnArmedVanillaOrderIsNeverWaitedFor()
    {
        // Unlike an executor dispatch, no queue entry is waited for: the order is due when its courier arrives, however long the
        // carrier's own move takes.
        var host = new FakeHost { Now = 10.5, Slot = null, Move = MoveState.InFlight };
        var flow = NewFlow(host);
        PutPursuit(flow, issued: 10.0);
        flow.RunPass();
        AssertEqual(1, host.Calls, "a carrier still on its way is a carrier pursuing: InFlight is not asked to hold the order");
    }

    private static void OnlyTheWaypointTypesAreArmed()
    {
        AssertTrue(WlMoveState.VanillaFollowsAWaypoint(0), "take objective");
        AssertTrue(WlMoveState.VanillaFollowsAWaypoint(2), "support");
        AssertTrue(WlMoveState.VanillaFollowsAWaypoint(3), "attack isolated target");
        AssertTrue(WlMoveState.VanillaFollowsAWaypoint(4), "assault");
        foreach (int t in new[] { 1, 5, 11, 12, 13, 14, 15 })
            AssertFalse(WlMoveState.VanillaFollowsAWaypoint(t), "type " + t + " describes no move of the called unit (engage is the halt itself, 117383)");
    }

    private static void RuntimeArmsVanillaOrdersFromTheCalledUnit()
    {
        string code = Code(FlowRuntime);
        string route = BodyOf(code, "private static CaptureVerdict Route(");
        AssertTrue(Regex.IsMatch(route, @"else if \(call\.Origin == OrderOrigin\.Vanilla && CouriersOn\(\) && TryPursuit\(unit, type, out WlMoveCarrier pursuit\)\)\s*\{\s*entry\.Pursuit = true;\s*entry\.Carrier = pursuit;\s*\}"),
            "Route arms only a vanilla-origin order, only while couriers are on");
        string pursuit = BodyOf(code, "private static bool TryPursuit(");
        AssertTrue(pursuit.Contains("unit.regimentpaths <= 0") && pursuit.Contains("unit.lastsetwaypointposition") && pursuit.Contains("WlMoveCarrier.NoQueueEntry"),
            "the called unit must hold a path now; its target is its last set waypoint, not the order's position; no queue entry is waited for");
        AssertTrue(pursuit.Contains("unit.gameObject.GetInstanceID()"), "the carrier is the called unit itself");
    }

    // Smoke 2026-10-17: the rows could not say where an order pointed, why it was admitted, how the move gate released it, or
    // whether the player was detached from his brigade when it was made.
    private static void FlowRowsCarryPositionAdmitGateAndDetachedFlag()
    {
        string code = Code(FlowRuntime);
        string row = BodyOf(code, "private static void EmitRow(");
        foreach (string field in new[] { ".WithField(\"x\", (double)row.X)", ".WithField(\"z\", (double)row.Z)", ".WithField(\"gate\", MoveBasisName.Of(row.Gate))",
                     ".WithField(\"admit\", row.AdmitReason ?? string.Empty)", ".WithField(\"detached\", row.PlayerDetached)" })
            AssertTrue(row.Contains(field), "the row writes " + field);
        AssertTrue(BodyOf(code, "private static bool TryMakeEntry(").Contains("PlayerDetached = (UnityEngine.Object)_command != null && _command.permanentlydetached"),
            "every entry notes the player's detached flag (vanilla's permanentlydetached, 85321-85323) when it is made");
        AssertTrue(Regex.IsMatch(BodyOf(code, "private static CaptureVerdict Route("), @"entry\.AdmitReason = reason;"), "the capture notes the policy's reason");
    }

    private static void GateNamesAreStable()
    {
        AssertEqual("arrived", MoveBasisName.Of(MoveBasis.Arrived), "arrived");
        AssertEqual("path", MoveBasisName.Of(MoveBasis.Path), "path");
        AssertEqual("contact", MoveBasisName.Of(MoveBasis.Contact), "contact");
        AssertEqual(string.Empty, MoveBasisName.Of(MoveBasis.None), "no gate looked");
    }
}
