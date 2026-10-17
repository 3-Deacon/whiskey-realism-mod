using System;
using System.Text.RegularExpressions;
using WhiskeyRealism.Tactical.PlayerOrders;

using static TestHarness;

// The player is told of a move when the superior that carries it has it (2026-10-16 smoke: "I got an
// order to move from 1st brigade, but then that brigade never moves"). The executor queued the brigade's
// move behind 21 other orders on the army's order queue (processing time +0.43 h, vanilla's
// AddToOrderQueue 124945-124955), while the player's order was timed from the brigade's headquarters
// alone (+0.02 h), so the panel showed the move 0.41 h before the brigade carried it out. An order whose
// unit still has a move in vanilla's queue waits for that move, then runs its own leg to the player.
internal static partial class WlOrderFlowTests
{
    private const double Leg = 0.02;

    // An executor dispatch captured at `issued` while the brigade's move was still queued: due after the
    // brigade-to-player leg alone, armed to wait for the move.
    private static void PutMoveOrder(WlOrderFlow flow, double issued, int unit = 1, int type = 1, float x = 0f,
        OrderOrigin origin = OrderOrigin.Executor, bool armed = true)
    {
        CourierEntry e = QEntry(issuer: unit, type: type, origin: origin, x: x, issuedAt: issued, dueAt: issued + Leg);
        e.UnitId = unit;
        e.AwaitsMove = armed;
        e.LegHours = armed ? Leg : 0.0;
        flow.Queue.TryEnqueue(in e, 0.0, out _);
    }

    private static void MoveInFlightHoldsTheOrderPastItsDueTime()
    {
        var host = new FakeHost { Now = 10.5, Slot = null, Move = MoveState.InFlight };
        var flow = NewFlow(host);
        PutMoveOrder(flow, issued: 10.0);
        flow.RunPass();
        AssertEqual(0, host.Calls, "the panel is not shown while the brigade's move is in flight");
        AssertEqual(1, flow.Queue.Count, "the order stays queued");
        AssertEqual("1:defer:move-in-flight", RowsOf(host), "one row says why");
    }

    private static void HeldOrderIsLookedAtBeforeItsDueTime()
    {
        // The leg runs from the moment the move executes, so a move that executes before the order's
        // original due time must be seen on the pass that finds it: the entry is examined every pass.
        var host = new FakeHost { Now = 10.0, Slot = null, Move = MoveState.InFlight };
        var flow = NewFlow(host);
        PutMoveOrder(flow, issued: 10.0);
        flow.RunPass();
        AssertEqual(1, host.MoveAsks, "an armed entry is examined although its leg has not passed");
        host.Now = 10.004;
        host.Move = MoveState.Executed;
        flow.RunPass();
        AssertEqual(0, host.Calls, "the move executed at 10.004: the leg runs from there");
        host.Now = 10.004 + Leg;
        flow.RunPass();
        AssertEqual(1, host.Calls, "and the order is shown when the leg has passed");
    }

    private static void HoldRowIsWrittenOnce()
    {
        var host = new FakeHost { Now = 10.5, Slot = null, Move = MoveState.InFlight };
        var flow = NewFlow(host);
        PutMoveOrder(flow, issued: 10.0);
        for (int pass = 0; pass < 5; pass++)
        {
            host.Now += 0.01;
            flow.RunPass();
        }
        AssertEqual("1:defer:move-in-flight", RowsOf(host), "five passes, one row");
        AssertEqual(5, host.MoveAsks, "the move is asked about on every pass");
    }

    private static void ExecutedMoveReleasesTheOrderAfterItsLeg()
    {
        var host = new FakeHost { Now = 10.5, Slot = null, Move = MoveState.InFlight };
        var flow = NewFlow(host);
        PutMoveOrder(flow, issued: 10.0);
        flow.RunPass();
        host.Now = 10.9;
        host.Move = MoveState.Executed;
        flow.RunPass();
        AssertEqual(0, host.Calls, "the brigade has the order at 10.9; the bugle to the player takes the leg");
        AssertEqual(1, flow.Queue.Count, "so the order waits");
        host.Now = 10.9 + Leg - 0.001;
        flow.RunPass();
        AssertEqual(0, host.Calls, "not yet one tick before");
        host.Now = 10.9 + Leg;
        flow.RunPass();
        AssertEqual(1, host.Calls, "shown when the leg has passed");
        AssertEqual("1:defer:move-in-flight,1:deliver:accepted", RowsOf(host), "held once, then delivered: " + RowsOf(host));
    }

    private static void ExecutedMoveWithNoLegDeliversAtOnce()
    {
        var host = new FakeHost { Now = 10.5, Slot = null, Move = MoveState.InFlight };
        var flow = NewFlow(host);
        CourierEntry e = QEntry(issuer: 1, type: 1, origin: OrderOrigin.Executor, issuedAt: 10.0, dueAt: 10.0);
        e.UnitId = 1;
        e.AwaitsMove = true;
        e.LegHours = 0.0;
        flow.Queue.TryEnqueue(in e, 0.0, out _);
        flow.RunPass();
        host.Move = MoveState.Executed;
        flow.RunPass();
        AssertEqual(1, host.Calls, "a zero leg is shown on the pass that finds the move executed");
    }

    private static void LostMoveDiscardsTheOrder()
    {
        var host = new FakeHost { Now = 10.5, Slot = null, Move = MoveState.Lost };
        var flow = NewFlow(host);
        PutMoveOrder(flow, issued: 10.0);
        flow.RunPass();
        AssertEqual(0, host.Calls, "the brigade will not make a move whose courier was lost: the player is not told of it");
        AssertEqual(0, flow.Queue.Count, "and the order is gone");
        AssertEqual("1:discard:move-lost", RowsOf(host), "with the reason");
    }

    private static void LostMoveAfterAHoldDiscardsToo()
    {
        var host = new FakeHost { Now = 10.5, Slot = null, Move = MoveState.InFlight };
        var flow = NewFlow(host);
        PutMoveOrder(flow, issued: 10.0);
        flow.RunPass();
        host.Move = MoveState.Lost;
        flow.RunPass();
        AssertEqual("1:defer:move-in-flight,1:discard:move-lost", RowsOf(host), "held, then discarded");
        AssertEqual(0, host.Calls, "never shown");
    }

    private static void UnarmedEntryIsNotAskedAboutAMove()
    {
        var host = new FakeHost { Now = 10.5, Slot = null, Move = MoveState.InFlight };
        var flow = NewFlow(host);
        PutMoveOrder(flow, issued: 10.0, origin: OrderOrigin.Executor, armed: false);
        flow.RunPass();
        AssertEqual(0, host.MoveAsks, "an order captured with no move in flight never consults the queue");
        AssertEqual(1, host.Calls, "and is shown at its due time as before");
    }

    private static void EntryNotYetDueAndNotArmedWaits()
    {
        var host = new FakeHost { Now = 10.0, Slot = null };
        var flow = NewFlow(host);
        PutMoveOrder(flow, issued: 10.0, armed: false);
        flow.RunPass();
        AssertEqual(0, host.Calls, "an unarmed entry keeps waiting for its due time");
        AssertEqual(1, flow.Queue.Count, "and stays queued");
    }

    private static void SupersededHeldOrderIsStillDiscarded()
    {
        // A later order was delivered while this one waited for its move: the pass discards it as
        // superseded, as it does every other entry, rather than holding it for ever.
        var host = new FakeHost { Now = 10.5, Slot = null, Move = MoveState.InFlight };
        var flow = NewFlow(host);
        PutMoveOrder(flow, issued: 10.0, unit: 1, x: 0f);
        flow.Queue.MarkAccepted(10.2);
        flow.RunPass();
        AssertEqual("1:discard:superseded", RowsOf(host), "superseded beats the move gate");
        AssertEqual(0, host.MoveAsks, "and the move is not even asked about");
    }

    private static void GoneUnitHeldOrderIsDiscarded()
    {
        var host = new FakeHost { Now = 10.5, Slot = null, Move = MoveState.InFlight };
        host.GoneUnits.Add(1);
        var flow = NewFlow(host);
        PutMoveOrder(flow, issued: 10.0);
        flow.RunPass();
        AssertEqual("1:discard:unit-gone", RowsOf(host), "a unit that left the battle has no move");
    }

    private static void HeldOrderStillMeetsTheReadmitPolicy()
    {
        // The move gate only times the order: once released it is decided against the slot as it stands.
        var host = new FakeHost { Now = 10.9, Slot = new FakeOrder { Type = 15 }, Move = MoveState.Executed };
        var flow = NewFlow(host);
        PutMoveOrder(flow, issued: 10.0);
        flow.RunPass();
        host.Now = 10.9 + Leg;
        flow.RunPass();
        AssertEqual(0, host.Calls, "an active retreat (clause g) still refuses the order");
        AssertTrue(host.Rows.Count > 0 && host.Rows[host.Rows.Count - 1].StartsWith("1:discard:"), "with the policy's own discard: " + RowsOf(host));
    }

    private static void HeldEntriesDoNotBlockOthersAndKeepEnqueueOrder()
    {
        // A held entry goes back to the queue after the pass; an unarmed order due in the same pass is shown.
        var host = new FakeHost { Now = 10.5, Slot = null, Move = MoveState.InFlight };
        var flow = NewFlow(host);
        PutMoveOrder(flow, issued: 10.0, unit: 1, type: 1, x: 0f);
        PutMoveOrder(flow, issued: 10.1, unit: 2, type: 2, x: 5000f, origin: OrderOrigin.P62, armed: false);
        flow.RunPass();
        AssertEqual("2", string.Join(",", host.CalledTypes), "the P62 order is delivered");
        AssertEqual(1, flow.Queue.Count, "the held move order is still queued");
    }

    // The smoke of 2026-10-16 (session 20261017-001611, tactical.jsonl lines 1530-1531, 2137, 6168): the
    // executor's Scout move of 1st_Brigade was queued at doctrine hour 17.180 as the 22nd entry on
    // Army_of_the_Potomac's queue (due +0.43 h) and dispatched to the player with the brigade-to-player leg
    // alone (43 m, bugle, 0.02 h). The panel showed it at 17.200; the brigade carried the move out, and
    // bugled it to its regiments, at 17.606.
    private static void SmokeBrigadeMoveReachesThePlayerWhenTheBrigadeHasIt()
    {
        var host = new FakeHost { Now = 17.180, Slot = null, Move = MoveState.InFlight };
        var flow = NewFlow(host);
        PutMoveOrder(flow, issued: 17.180);
        for (double t = 17.180; t < 17.600; t += 0.01)
        {
            host.Now = t;
            flow.RunPass();
            AssertEqual(0, host.Calls, "the panel stays empty at " + t.ToString("0.000") + " while the brigade waits for its courier");
        }
        host.Now = 17.606;
        host.Move = MoveState.Executed;
        flow.RunPass();
        AssertEqual(0, host.Calls, "17.606: the brigade carries the move out; the bugle to the player is on its way");
        host.Now = 17.626;
        flow.RunPass();
        AssertEqual(1, host.Calls, "17.626: the player is told, one leg after the brigade");
        AssertEqual("1:defer:move-in-flight,1:deliver:accepted", RowsOf(host), "rows: " + RowsOf(host));
    }

    private static void HeldPassAllocatesNothing()
    {
        // The pass runs every UpdateDLCPlayerOrders (once per 30 frames) for as long as a move is in flight.
        var host = new FakeHost { Now = 10.5, Slot = null, Move = MoveState.InFlight };
        var flow = NewFlow(host);
        PutMoveOrder(flow, issued: 10.0);
        PutMoveOrder(flow, issued: 10.1, unit: 2, type: 0, x: 4000f);
        flow.RunPass(); // warm-up: the one hold row per entry
        long before = TestFixtures.BeginAllocationWindow();
        for (int i = 0; i < 2000; i++)
        {
            host.Now += 0.0005;
            flow.RunPass();
        }
        long allocated = TestFixtures.AllocatedSince(before);
        AssertEqual(0L, allocated, "held pass allocated bytes");
        AssertEqual(2, flow.Queue.Count, "both entries still held");
        AssertEqual(0, host.Calls, "nothing shown");
    }

    // The order the executor's dispatch must wait for is the one it just queued: the dispatch follows the
    // vanilla move calls of the same Execute, so TryMakeEntry finds the entry in the queue.
    private static void ExecutorDispatchFollowsTheMoveItWaitsFor()
    {
        string body = BodyOf(Code("src/WhiskeyRealism/Tactical/Battle/OrderExecutorRuntime.cs"), "internal static int Execute(");
        int move = body.IndexOf("IssueMove(", StringComparison.Ordinal);
        int dispatch = body.IndexOf("TacticalPlayerDispatchRuntime.Emit(", StringComparison.Ordinal);
        AssertTrue(move >= 0 && dispatch > move, "Emit comes after IssueMove in Execute");
    }

    private static void MoveGateArmsOnlyMoveShapedOrders()
    {
        AssertTrue(WlOrderFlow.ArmsMoveGate(OrderOrigin.Executor, 1), "executor move");
        AssertTrue(WlOrderFlow.ArmsMoveGate(OrderOrigin.Executor, 0), "executor attack");
        AssertTrue(WlOrderFlow.ArmsMoveGate(OrderOrigin.Executor, 11), "executor reserve");
        AssertTrue(WlOrderFlow.ArmsMoveGate(OrderOrigin.Executor, 12), "executor hold");
        AssertFalse(WlOrderFlow.ArmsMoveGate(OrderOrigin.Executor, 13), "never engage at will");
        AssertFalse(WlOrderFlow.ArmsMoveGate(OrderOrigin.Executor, 15), "never a retreat");
        for (int t = 0; t <= 4; t++) AssertTrue(WlOrderFlow.ArmsMoveGate(OrderOrigin.Vanilla, t), "vanilla type " + t);
        AssertTrue(WlOrderFlow.ArmsMoveGate(OrderOrigin.Vanilla, 12), "vanilla defend");
        AssertFalse(WlOrderFlow.ArmsMoveGate(OrderOrigin.Vanilla, 11), "vanilla reserve is relayed to the unit it is linked to");
        AssertFalse(WlOrderFlow.ArmsMoveGate(OrderOrigin.Vanilla, 13), "vanilla contact state");
        AssertFalse(WlOrderFlow.ArmsMoveGate(OrderOrigin.Vanilla, 14), "vanilla vanguard state");
        AssertFalse(WlOrderFlow.ArmsMoveGate(OrderOrigin.Vanilla, 15), "vanilla retreat");
        foreach (OrderOrigin o in new[] { OrderOrigin.P62, OrderOrigin.Release, OrderOrigin.FollowUp, OrderOrigin.Retreat, OrderOrigin.CampaignBridge })
            AssertFalse(WlOrderFlow.ArmsMoveGate(o, 1), o + " is not about a move of its unit");
    }

    private static void MoveStateClassification()
    {
        AssertEqual(MoveState.InFlight, WlMoveState.Classify(moveQueued: true, courierLost: false), "queued is in flight");
        AssertEqual(MoveState.Lost, WlMoveState.Classify(moveQueued: true, courierLost: true), "queued with its courier gone is lost");
        AssertEqual(MoveState.Executed, WlMoveState.Classify(moveQueued: false, courierLost: false), "not queued is executed");
        AssertEqual(MoveState.Executed, WlMoveState.Classify(moveQueued: false, courierLost: true), "a deleted entry has no courier to lose");
    }

    // The runtime arms the gate from vanilla's queue for exactly the orders ArmsMoveGate names, only while
    // couriers are on, and only reads the queue.
    private static void RuntimeArmsFromVanillasQueue()
    {
        string code = Code(FlowRuntime);
        string make = BodyOf(code, "private static bool TryMakeEntry(");
        Match arm = Regex.Match(make, @"if\s*\(\s*CouriersOn\(\)\s*&&\s*WlOrderFlow\.ArmsMoveGate\(origin,\s*type\)\s*&&\s*MoveStateOf\(unit\)\s*!=\s*MoveState\.Executed\s*\)\s*\{\s*entry\.AwaitsMove\s*=\s*true;\s*entry\.LegHours\s*=\s*delay;\s*\}");
        AssertTrue(arm.Success, "TryMakeEntry arms the entry from the unit's queued move with the courier delay as its leg");
        string queued = BodyOf(code, "private static MoveState MoveStateOf(");
        AssertTrue(queued.Contains("entry.ordertype > 1") && queued.Contains("entry.advisedunit"), "the queue entry is a move (ordertype 0 or 1) of the unit");
        AssertTrue(queued.Contains("parent.GetComponent<Regiment>()"), "SetWaypoint's queue owner: the scene parent (91461-91504)");
        AssertTrue(queued.Contains("WlMoveState.Classify(queued, lost)"), "the verdict is the pure classification");
        string lost = BodyOf(code, "private static bool CourierLost(");
        AssertTrue(lost.Contains("line.type == 1") && lost.Contains("line.targetunit") && lost.Contains("line.courierref == null"),
            "lost is vanilla's own test (125281-125286) on the entry's primary mounted courier line");
        AssertFalse(code.Contains("orderstate"), "the unit's shared orderstate is not evidence about one order");
        AssertTrue(BodyOf(code, "public MoveState MoveOf(").Contains("MoveStateOf(unit)"), "the host answers from the queue");
    }
}
