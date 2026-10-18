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
        AssertTrue(Regex.IsMatch(contracts, @"Maintenance = \d+,"), "the intent flag exists");
        int maintenance = (int)WhiskeyRealism.Tactical.Battle.IntentFlags.Maintenance;
        AssertTrue(maintenance > 0 && (maintenance & (maintenance - 1)) == 0, "a single bit");
        foreach (WhiskeyRealism.Tactical.Battle.IntentFlags other in Enum.GetValues(typeof(WhiskeyRealism.Tactical.Battle.IntentFlags)))
            if (other != WhiskeyRealism.Tactical.Battle.IntentFlags.Maintenance)
                AssertTrue(((int)other & maintenance) == 0, "no other intent flag shares the Maintenance bit: " + other);
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
        AssertTrue(BodyOf(flowCode, "public void Accepted(in CourierEntry e)").Contains("Memory.NotePanel(Session, e.Origin == OrderOrigin.Executor ? e.UnitId : 0);"),
            "an accepted order tells the memory what is on the panel, and whose it is when the executor sent it");
        AssertTrue(BodyOf(flowCode, "internal static void OnDeliveryPass(").Contains("Memory.NotePanel(DLC_WL.GivenOrders.givenorderssession, 0);"),
            "every pass notes the panel's session, so an order that took the panel without a Whiskey delivery is seen");
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
        AssertTrue(Regex.IsMatch(exec, @"if \(executed > 0 && winner\.Task != CommandTaskType\.None && \(winner\.Flags & IntentFlags\.WorksHold\) == 0\)\s*TacticalPlayerDispatchRuntime\.NoteTask\(winner\.GroupId, winner\.Task, nowHours\);"),
            "every executed write of a group notes the group's task, except the works hold's stance, which names HoldObjective whatever the group does (SubmitWorksHoldStance)");
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
        AssertTrue(WlMoveState.VanillaFollowsAWaypoint(12), "defend an own-held objective: UpdateMovingTargets sets one waypoint, then orders 0 or 12 (6958-7002)");
        foreach (int t in new[] { 1, 5, 11, 13, 14, 15 })
            AssertFalse(WlMoveState.VanillaFollowsAWaypoint(t), "type " + t + " describes no move of the called unit (engage is the halt itself, 117383)");
    }

    private static void RuntimeArmsVanillaOrdersFromTheCalledUnit()
    {
        string code = Code(FlowRuntime);
        string route = BodyOf(code, "private static CaptureVerdict Route(");
        AssertTrue(Regex.IsMatch(route, @"else if \(call\.Origin == OrderOrigin\.Vanilla && CouriersOn\(\) && TryPursuit\(unit, type, position, out WlMoveCarrier pursuit\)\)\s*\{\s*entry\.Pursuit = true;\s*entry\.Carrier = pursuit;\s*\}"),
            "Route arms only a vanilla-origin order, only while couriers are on");
        string pursuit = BodyOf(code, "private static bool TryPursuit(");
        AssertTrue(pursuit.Contains("!WlMoveQueue.IsMoving(unit)") && pursuit.Contains("X = position.x, Z = position.z") && pursuit.Contains("WlMoveCarrier.NoQueueEntry"),
            "the called unit must hold a path now; its target is the order's position, not the waypoint read at capture; no queue entry is waited for");
        AssertFalse(pursuit.Substring(pursuit.IndexOf("pursuit = new WlMoveCarrier", StringComparison.Ordinal)).Contains("last"),
            "the carrier's target never comes from the waypoint read at capture (it can be the previous move's: BlockedMovingOrder sets it two frames later, 3382); it is read for the type-12 shape test only");
        AssertTrue(pursuit.Contains("SlackMeters = WlMoveState.VanillaWaypointSlackMeters"), "with the callers' waypoint slack");
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

    // Smoke-independent hazard found in review: an objective move over 150 m goes through BlockedMovingOrder (6950-6958), which sets
    // the waypoint two frames after the order is given. The target is the order's point, with the callers' slack.
    private static void VanillaPursuitToleratesTheCallersWaypointOffset()
    {
        float slack = WlMoveState.VanillaWaypointSlackMeters;
        AssertEqual(MoveBasis.Path, WlMoveState.BasisAfterGone(true, 200f, 900f, false, slack), "a path ending 200 m from the order's point (the 70 m terrain offset plus a 150 m similar-positions shift fits)");
        AssertEqual(MoveBasis.None, WlMoveState.BasisAfterGone(true, 200f, 900f, false), "the executor's exact reading would call that a re-laid path");
        AssertEqual(MoveBasis.None, WlMoveState.BasisAfterGone(true, ReadingJustPast(slack), 900f, false, slack), "past the slack it is another move");
        AssertEqual(MoveBasis.Arrived, WlMoveState.BasisAfterGone(false, 0f, 270f, false, slack), "standing within 60 m plus the slack");
        AssertEqual(MoveBasis.Contact, WlMoveState.BasisAfterGone(false, 0f, 520f, true, slack), "a contact halt within 310 m plus the slack");
        AssertEqual(MoveBasis.None, WlMoveState.BasisAfterGone(false, 0f, 531f, true, slack), "and not one metre farther");
        AssertTrue(slack >= 70f + 150f, "the slack covers the terrain offset of UpdateMovingTargets (6941) and the similar-positions shift (8700-8705)");
    }

    private static float ReadingJustPast(float slack) => WlMoveState.ArrivalMeters + slack + 1f;

    // A maintenance move voids nothing and an executor carrier stays exact.
    private static void ExecutorCarriersCarryNoSlack()
    {
        var carrier = default(WlMoveCarrier);
        carrier.Consider(5, 17.3f, 17.1f, 1, 100f, 200f);
        AssertEqual(0f, carrier.SlackMeters, "an executor carrier is exact");
        string queue = Code("src/WhiskeyRealism/Tactical/PlayerOrders/WlMoveQueue.cs");
        AssertTrue(queue.Contains("unit.combatstatussubordinates > 0f, carrier.SlackMeters)"), "the reading uses the carrier's slack");
    }

    // Verify round 1, major 3 (smoke14): the brigade read paths 0 on 6 of 18 marching rows more than 280 m from its waypoint; its regiments
    // held the paths. The group's own path is not the only sign of a march.
    private static void AGroupWhoseRegimentsMarchIsStillMoving()
    {
        AssertTrue(WlMoveState.IsMoving(1, 0f), "the group holds a path");
        AssertTrue(WlMoveState.IsMoving(0, 0.4f), "no path of its own, a share of its regiments marching (groupsubordinatesmoving)");
        AssertFalse(WlMoveState.IsMoving(0, 0f), "neither");
        AssertEqual(MoveBasis.Path, WlMoveState.BasisAfterGone(WlMoveState.IsMoving(0, 0.4f), 20f, 600f, false), "so a march whose group path is empty still reads as pursuing a point it is heading for");
        AssertEqual(MoveBasis.None, WlMoveState.BasisAfterGone(WlMoveState.IsMoving(0, 0f), 20f, 600f, false), "control: stopped, it does not");
        string queue = Code("src/WhiskeyRealism/Tactical/PlayerOrders/WlMoveQueue.cs");
        AssertTrue(queue.Contains("WlMoveState.IsMoving(unit.regimentpaths, unit.groupsubordinatesmoving)"), "the runtime reads vanilla's recomputed share, not groupsubordinatesmovingnonai (only regiments this group ordered)");
        AssertTrue(Code(FlowRuntime).Contains("!WlMoveQueue.IsMoving(unit)") && Code("src/WhiskeyRealism/Strategic/PlayerComplianceRuntime.cs").Contains("WlMoveQueue.IsMoving(carrier)"),
            "arming, the pursuit reading and compliance share the one reading");
    }

    private static void ThePlayerIsNeverHisOwnChain()
    {
        string pursuit = BodyOf(Code(FlowRuntime), "private static bool TryPursuit(");
        AssertTrue(pursuit.Contains("(UnityEngine.Object)unit == (UnityEngine.Object)_command) return false;"), "an order to the player's own command is not armed");
    }

    // Verify round 2, major: UpdateMovingTargets (6958-7002) moves the unit and orders 12 at its objective; CheckGlobalAIStrategy (6463) orders 12 at
    // the closest objective or the parent's centre without a move.
    private static void Type12IsArmedOnlyInTheShapeOfTheWaypointCaller()
    {
        float slack = WlMoveState.VanillaWaypointSlackMeters;
        AssertTrue(WlMoveState.ArmsVanillaOrder(12, 70f), "6958-7002: the waypoint lies 70 m from the objective the 12 names");
        AssertTrue(WlMoveState.ArmsVanillaOrder(12, 0f), "6463's else branch names the unit's own last waypoint: it passes while the unit pursues it");
        AssertTrue(WlMoveState.ArmsVanillaOrder(12, slack), "the slack is inclusive");
        AssertFalse(WlMoveState.ArmsVanillaOrder(12, slack + 1f), "6463 at an objective or the parent's centre, the marching parent's waypoint elsewhere: not armed");
        AssertFalse(WlMoveState.ArmsVanillaOrder(12, 2500f), "a parent marching across the field when the macro turns defensive: its defend order is not discarded as abandoned");
        AssertFalse(WlMoveState.ArmsVanillaOrder(12, float.MaxValue), "no waypoint set at all");
        foreach (int t in new[] { 0, 2, 3, 4 })
            AssertTrue(WlMoveState.ArmsVanillaOrder(t, 2500f), "type " + t + " is always the waypoint callers': armed whatever the distance (the pursuit reading carries its own slack)");
        foreach (int t in new[] { 1, 5, 11, 13, 14, 15 })
            AssertFalse(WlMoveState.ArmsVanillaOrder(t, 0f), "type " + t + " is never armed");
        string pursuit = BodyOf(Code(FlowRuntime), "private static bool TryPursuit(");
        AssertTrue(pursuit.Contains("WlMoveState.ArmsVanillaOrder(type,") && pursuit.Contains("last.x == 0f && last.z == 0f") && pursuit.Contains("float.MaxValue"),
            "the runtime asks the rule with the distance from the unit's last waypoint to the order's point");
    }

    // Verify round 2, minor 7: the division's fan-out sends a brigade's regiments to B while the brigade's own waypoint, and so its pursuit reading,
    // still says A; its queued executor dispatch for A is voided when the fan-out moves it.
    private static void AFanOutVoidsTheMovedChildrensStaleOrders()
    {
        var sink = new RecordingSink();
        var q = new WlCourierQueue(sink);
        // the brigade (unit 101) was ordered to A = (0, 0); another brigade (102) to C; the division (103) has its own order.
        Enq(q, QEntry(issuer: 1, type: 1, origin: OrderOrigin.Executor, x: 0, issuedAt: 1.0, dueAt: 9.0), out _);
        Enq(q, QEntry(issuer: 2, type: 1, origin: OrderOrigin.Executor, x: 3000, issuedAt: 1.0, dueAt: 9.0), out _);
        Enq(q, QEntry(issuer: 3, type: 12, origin: OrderOrigin.Executor, x: 6000, issuedAt: 1.0, dueAt: 9.0), out _);
        // the executor fans the division's move (103) out to brigade 101 at B = (1500, 0), offset 380 m: the same calls the runtime makes
        AssertEqual(1, q.DiscardSupersededIntents(101, 1500f, 0f, WlCourierQueue.SupersededIntent), "the brigade's order to A is voided by its own move to B");
        AssertEqual(0, q.DiscardSupersededIntents(101, 1500f, 0f, WlCourierQueue.SupersededIntent), "once");
        AssertEqual(2, q.Count, "the other brigade's and the division's orders stay");
        string exec = BodyOf(Code("src/WhiskeyRealism/Tactical/Battle/OrderExecutorRuntime.cs"), "internal static int Execute(");
        AssertTrue(Regex.IsMatch(exec, @"for \(int k = 0; k < issued; k\+\+\)\s*if \(_issued\[k\]\.InstanceId != winner\.GroupId\)\s*WlOrderFlowRuntime\.SupersedeExecutorOrders\(_issued\[k\]\.InstanceId, _issued\[k\]\.X, _issued\[k\]\.Z\);"),
            "every child the move reached, with its own target, voids its queued executor orders");
        AssertTrue(exec.IndexOf("_issued[k].InstanceId, _issued[k].X", StringComparison.Ordinal) < exec.IndexOf("TacticalPlayerDispatchRuntime.Emit(", StringComparison.Ordinal), "before the new order is queued");
    }
}
