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

    // Verify round 3, major: vanilla restamps a group's last waypoint only for the group a Move is issued to (92067) and for regiments (131250). A brigade order to A
    // still reads "path ends at A" after the division sends the brigade to B. On every written group Move, Maintenance included, the queued orders of the non-detached
    // descendant groups whose new target differs by more than 110 m are voided, and the vanilla orders armed with them.
    private static UnitRecord Rec(int id, int parent, UnitFlags flags, float x = 0f, float z = 0f)
        => new UnitRecord { InstanceId = id, ParentIndex = parent, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup | flags, X = x, Z = z };

    // 0 division(10); 1 brigade 11 (offset +400 x); 2 sub-group 12 of brigade 11; 3 brigade 13 (detached); 4 sub-group 14 under the detached brigade;
    // 5 brigade 15 (player-owned); 6 routed brigade 16.
    private static UnitRecord[] Division() => new[]
    {
        Rec(10, -1, UnitFlags.AncestorOfPlayerCommand, 0f, 0f),
        Rec(11, 0, UnitFlags.None, 400f, 0f),
        Rec(12, 1, UnitFlags.None, 400f, 100f),
        Rec(13, 0, UnitFlags.PermanentlyDetached, -400f, 0f),
        Rec(14, 3, UnitFlags.None, -400f, 100f),
        Rec(15, 0, UnitFlags.PlayerOwned, 0f, 400f),
        Rec(16, 0, UnitFlags.Routed, 0f, -400f),
    };

    private static WlCourierQueue QueueOfStaleOrders(RecordingSink sink)
    {
        var q = new WlCourierQueue(sink);
        // every order points at A = (0, 0) in the old place; unit ids are the groups' instance ids; each has its own type so none is dropped as en-route
        void Put(int unit, OrderOrigin origin, int type, bool pursuit = false, float x = 0f)
        {
            CourierEntry e = QEntry(issuer: unit, type: type, origin: origin, x: x, issuedAt: 1.0, dueAt: 9.0);
            e.UnitId = unit;
            e.Pursuit = pursuit;
            AssertTrue(q.TryEnqueue(in e, 0.0, out _), "fixture: queued " + unit + "/" + type);
        }
        Put(10, OrderOrigin.Executor, 1);               // the ordered group's own
        Put(11, OrderOrigin.Executor, 2);               // non-detached brigade
        Put(11, OrderOrigin.Vanilla, 4, pursuit: true); // armed vanilla order of the same brigade
        Put(11, OrderOrigin.Vanilla, 11);               // an unarmed vanilla order (reserve): not a pursuit
        Put(12, OrderOrigin.Executor, 3);               // sub-group of the brigade
        Put(13, OrderOrigin.Executor, 0);               // detached brigade
        Put(14, OrderOrigin.Executor, 5);               // under the detached brigade
        return q;
    }

    private static int VoidStale(WlCourierQueue q, UnitRecord[] own, bool maintenance, float x = 1000f, float z = 0f)
    {
        var issued = new[] { new IssuedTarget { InstanceId = 10, X = x, Z = z } };
        return new StaleDispatchVoid().Apply(q, own, own.Length, 0, maintenance, x, z, issued, 1, 5000f, 5000f, WlCourierQueue.SupersededIntent);
    }

    private static void ADivisionMoveVoidsTheStaleOrdersOfNonDetachedBrigades()
    {
        var sink = new RecordingSink();
        var q = QueueOfStaleOrders(sink);
        int voided = VoidStale(q, Division(), maintenance: false);
        AssertEqual(4, voided, "the division's own, the brigade's executor and armed vanilla order, and the sub-group's");
        AssertEqual(4, sink.Count("superseded-intent"), "one row each");
        AssertEqual(3, q.Count, "left: the detached brigade's order, the one under it, and the unarmed reserve order");
    }

    private static void ADetachedBrigadeAndItsSubtreeAreUntouched()
    {
        var sink = new RecordingSink();
        var q = QueueOfStaleOrders(sink);
        VoidStale(q, Division(), maintenance: false);
        foreach (var d in sink.Discards)
            AssertTrue(d.issuer != 13 && d.issuer != 14, "no order of unit " + d.issuer + " (the detached brigade or a group under it) was voided");
        AssertTrue(q.HasPendingOrigin(OrderOrigin.Executor), "executor orders remain: the detached brigade's");
        // The same fixture without the detached flag would void them: control.
        var own = Division();
        own[3].Flags &= ~UnitFlags.PermanentlyDetached;
        var sink2 = new RecordingSink();
        var q2 = QueueOfStaleOrders(sink2);
        VoidStale(q2, own, maintenance: false);
        AssertEqual(6, sink2.Count("superseded-intent"), "control: attached, the brigade 13 and the group 14 under it are voided too (the unarmed reserve order stays)");
    }

    private static void AMaintenanceDivisionMoveVoidsTheDescendantsButNotItsOwnOrder()
    {
        var sink = new RecordingSink();
        var q = QueueOfStaleOrders(sink);
        int voided = VoidStale(q, Division(), maintenance: true);
        AssertEqual(3, voided, "the brigade's two and the sub-group's: the descendants' waypoints are stale whatever kind of move re-paths them");
        foreach (var d in sink.Discards)
            AssertTrue(d.issuer != 10, "the ordered group's own order stays: a recovery's target can be a fresh point and is no new order");
    }

    private static void AnOrderAlreadyAtTheNewOffsetTargetIsKept()
    {
        // brigade 11 keeps its +400 m offset from the division: a division move to (1000, 0) sends it to (1400, 0).
        var own = Division();
        var sink = new RecordingSink();
        var q = new WlCourierQueue(sink);
        CourierEntry atNew = QEntry(issuer: 11, type: 2, origin: OrderOrigin.Executor, x: 1450f, issuedAt: 1.0, dueAt: 9.0);   // 50 m from (1400, 0)
        atNew.UnitId = 11;
        AssertTrue(q.TryEnqueue(in atNew, 0.0, out _), "fixture");
        AssertEqual(0, VoidStale(q, own, maintenance: false), "the brigade is already bound where the move sends it (within 110 m): the order is still true");
        CourierEntry far = QEntry(issuer: 11, type: 3, origin: OrderOrigin.Executor, x: 1600f, issuedAt: 1.0, dueAt: 9.0);   // 200 m from (1400, 0)
        far.UnitId = 11;
        AssertTrue(q.TryEnqueue(in far, 0.0, out _), "fixture");
        AssertEqual(1, VoidStale(q, own, maintenance: false), "past 110 m it is stale");
    }

    private static void AFanOutChildAndItsDescendantsAreVoided()
    {
        // the division's move fans out to brigade 11 (issued, offset target), which carries sub-group 12
        var own = Division();
        var sink = new RecordingSink();
        var q = QueueOfStaleOrders(sink);
        var issued = new[] { new IssuedTarget { InstanceId = 11, X = 1400f, Z = 0f } };
        int voided = new StaleDispatchVoid().Apply(q, own, own.Length, 0, maintenance: false, 1000f, 0f, issued, 1, 5000f, 5000f, WlCourierQueue.SupersededIntent);
        AssertEqual(4, voided, "the ordered group's own, the moved brigade's two, and the sub-group under it");
    }

    private static void TheVoidAllocatesNothingAfterWarmUp()
    {
        var own = Division();
        var voider = new StaleDispatchVoid();
        var issued = new[] { new IssuedTarget { InstanceId = 10, X = 1000f, Z = 0f } };
        var q = new WlCourierQueue(null);
        voider.Apply(q, own, own.Length, 0, false, 1000f, 0f, issued, 1, 5000f, 5000f, WlCourierQueue.SupersededIntent);
        CourierEntry e = QEntry(issuer: 11, type: 2, origin: OrderOrigin.Executor, x: 0f, issuedAt: 1.0, dueAt: 9.0);
        e.UnitId = 11;
        long mark = TestFixtures.BeginAllocationWindow();
        for (int i = 0; i < 500; i++)
        {
            q.TryEnqueue(in e, 0.0, out _);
            voider.Apply(q, own, own.Length, 0, false, 1000f, 0f, issued, 1, 5000f, 5000f, WlCourierQueue.SupersededIntent);
        }
        AssertEqual(0L, TestFixtures.AllocatedSince(mark), "the walk uses its pooled buffers");
    }

    private static void TheExecutorGivesTheVoidItsOwnInputs()
    {
        string exec = BodyOf(Code("src/WhiskeyRealism/Tactical/Battle/OrderExecutorRuntime.cs"), "internal static int Execute(");
        AssertTrue(exec.Contains("WaypointValidatorRuntime.TryMapSize(out float voidSizeX, out float voidSizeZ);") && exec.Contains("_issued, issued, voidSizeX, voidSizeZ);"),
            "the moved units, their count and the map size (the offsets are clamped like the executor's own)");
        AssertTrue(Code("src/WhiskeyRealism/Tactical/Battle/StaleDispatchVoid.cs").Contains("PermanentlyDetached | UnitFlags.PlayerOwned | UnitFlags.PlayerCommand | UnitFlags.Routed"),
            "detached, player-owned and routed units are not re-pathed by a group order");
        AssertFalse(Code("src/WhiskeyRealism/Tactical/Battle/StaleDispatchVoid.cs").Contains("transform.parent"), "descendants come from the snapshot's parent map");
    }
}
