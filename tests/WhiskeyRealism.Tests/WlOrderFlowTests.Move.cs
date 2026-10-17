using System;
using System.Text.RegularExpressions;
using WhiskeyRealism.Tactical.PlayerOrders;

using static TestHarness;

// The player is told of a move when the superior that carries it has it (2026-10-16 smoke: "I got an
// order to move from 1st brigade, but then that brigade never moves"). The executor queued the brigade's
// move behind 21 other orders on the army's order queue (processing time +0.43 h, vanilla's
// AddToOrderQueue 124954-124966), while the player's order was timed from the brigade's headquarters
// alone (+0.02 h), so the panel showed the move 0.41 h before the brigade carried it out. An executor
// dispatch waits for the queued move that carries it (the ordered group's, or the latest child's when
// a fan-out around the player's command ordered the children), then runs its own leg to the player.
internal static partial class WlOrderFlowTests
{
    private const double Leg = 0.02;

    // An executor dispatch captured at `issued` while the move it accompanies was still queued: due after the
    // brigade-to-player leg alone, armed to wait for the carrier unit's move (the ordered unit by default).
    private static void PutMoveOrder(WlOrderFlow flow, double issued, int unit = 1, int type = 1, float x = 0f,
        OrderOrigin origin = OrderOrigin.Executor, bool armed = true, int carrier = 0)
    {
        CourierEntry e = QEntry(issuer: unit, type: type, origin: origin, x: x, issuedAt: issued, dueAt: issued + Leg);
        e.UnitId = unit;
        e.AwaitsMove = armed;
        e.LegHours = armed ? Leg : 0.0;
        e.CarrierUnitId = armed ? (carrier != 0 ? carrier : unit) : 0;
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

    private static void PolicyDeferralSurvivesTheMoveGate()
    {
        // A capture that the re-admit policy defers (inside the hold window) carries a later due time than
        // the leg alone; the move gate never makes it earlier.
        var host = new FakeHost { Now = 10.5, Slot = null, Move = MoveState.InFlight };
        var flow = NewFlow(host);
        CourierEntry e = QEntry(issuer: 1, type: 1, origin: OrderOrigin.Executor, issuedAt: 10.0, dueAt: 12.0);
        e.UnitId = 1;
        e.AwaitsMove = true;
        e.LegHours = Leg;
        flow.Queue.TryEnqueue(in e, 0.0, out _);
        flow.RunPass();
        host.Now = 10.9;
        host.Move = MoveState.Executed;
        flow.RunPass();
        host.Now = 10.9 + Leg;
        flow.RunPass();
        AssertEqual(0, host.Calls, "the deferral to 12.0 still holds after the move executed");
        host.Now = 12.0;
        flow.RunPass();
        AssertEqual(1, host.Calls, "and the order is shown at 12.0");
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

    // A fan-out (the player's command is not detached from the brigade): the planner orders the brigade's
    // children, the brigade itself has no queue entry, and the dispatch still goes out for the brigade. The
    // children's moves ride the brigade's own queue; the gate asks about the carrier child, not the brigade.
    private static void FanOutOrderWaitsForTheLatestChildMove()
    {
        var host = new FakeHost { Now = 10.5, Slot = null, Move = MoveState.Executed, RecordAsks = true };
        host.CarrierMoves[7] = MoveState.InFlight;
        var flow = NewFlow(host);
        PutMoveOrder(flow, issued: 10.0, unit: 100, carrier: 7);
        flow.RunPass();
        AssertEqual(0, host.Calls, "the brigade (100) has no move of its own, its child (7) is still waiting");
        AssertEqual("1:defer:move-in-flight", RowsOf(host), "held for the child's move");
        AssertTrue(host.MoveAskedAbout.Count == 1 && host.MoveAskedAbout[0] == 7, "the gate asked about the carrier child, not the ordered unit");
        host.Now = 10.9;
        host.CarrierMoves[7] = MoveState.Executed;
        flow.RunPass();
        AssertEqual(0, host.Calls, "the child's move is carried out: the leg runs");
        host.Now = 10.9 + Leg;
        flow.RunPass();
        AssertEqual(1, host.Calls, "then the player is told");
    }

    private static void MoveCarrierIsTheLatestEntryOfTheUnitsOrdered()
    {
        var carrier = default(WlMoveCarrier);
        AssertFalse(carrier.Has, "a dispatch that moved nothing through the queue has no carrier");
        carrier.Consider(5, 17.30f);
        carrier.Consider(6, 17.50f);
        carrier.Consider(7, 17.40f);
        carrier.Consider(0, 99f);
        AssertEqual(6, carrier.UnitId, "the child carried out last carries the player's order");
        AssertNear(17.50f, carrier.ProcessingTime, 1e-4f, "with its processing time");
        carrier.Consider(8, 17.50f);
        AssertEqual(6, carrier.UnitId, "a tie keeps the first");
    }

    private static void OnlyTheCarryingEntryCounts()
    {
        AssertTrue(WlMoveState.Carries(1, 17.50f, 17.50f), "the recorded entry itself");
        AssertTrue(WlMoveState.Carries(0, 17.60f, 17.50f), "a later entry of the unit (the recorded one was replaced)");
        AssertFalse(WlMoveState.Carries(1, 17.40f, 17.50f), "an older move of the same unit is another order's");
        AssertFalse(WlMoveState.Carries(2, 17.60f, 17.50f), "a stop entry is no move");
        AssertFalse(WlMoveState.Carries(4, 17.60f, 17.50f), "nor is a stance entry");
    }

    // The player always ends up with the latest order the chain gave (the held order is superseded, not lost).
    private static void LaterOrderDeliveredFirstSupersedesTheHeldOne()
    {
        var host = new FakeHost { Now = 10.5, Slot = null, Move = MoveState.InFlight };
        var flow = NewFlow(host);
        PutMoveOrder(flow, issued: 10.0, unit: 1, type: 0, x: 0f);                          // A: held for its move
        PutMoveOrder(flow, issued: 10.1, unit: 2, type: 0, x: 3000f, origin: OrderOrigin.P62, armed: false);  // B: later, unarmed
        host.Now = 10.5;
        flow.RunPass();
        AssertEqual("3000", string.Join(",", host.CalledXs), "B is shown with its own timing");
        AssertEqual(1, flow.Queue.Count, "A is still held");
        host.Move = MoveState.Executed;
        host.Now = 11.0;
        flow.RunPass();
        host.Now = 11.0 + Leg;
        flow.RunPass();
        AssertEqual("3000", string.Join(",", host.CalledXs), "A, older than B, is never shown");
        AssertTrue(host.Rows.Contains("0:discard:superseded"), "and it is reported, not lost: " + RowsOf(host));
        AssertEqual(0, flow.Queue.Count, "nothing is left queued");
    }

    private static void HeldOrdersReleasedTogetherArriveInIssueOrder()
    {
        var host = new FakeHost { Now = 10.5, Slot = null, Move = MoveState.InFlight };
        var flow = NewFlow(host);
        PutMoveOrder(flow, issued: 10.0, unit: 1, type: 0, x: 0f, carrier: 9);
        PutMoveOrder(flow, issued: 10.2, unit: 2, type: 0, x: 3000f, carrier: 9);
        flow.RunPass();
        host.Move = MoveState.Executed;
        host.Now = 11.0;
        flow.RunPass();
        host.Now = 11.0 + Leg;
        flow.RunPass();
        AssertEqual("0,3000", string.Join(",", host.CalledXs), "both are shown, the later one last: the player ends with the latest");
    }

    private static void LatestHeldOrderWinsWhenItsMoveIsCarriedOutFirst()
    {
        var host = new FakeHost { Now = 10.5, Slot = null, Move = MoveState.InFlight };
        host.CarrierMoves[10] = MoveState.InFlight;
        host.CarrierMoves[20] = MoveState.InFlight;
        var flow = NewFlow(host);
        PutMoveOrder(flow, issued: 10.0, unit: 1, type: 0, x: 0f, carrier: 10);
        PutMoveOrder(flow, issued: 10.2, unit: 2, type: 0, x: 3000f, carrier: 20);
        flow.RunPass();
        host.CarrierMoves[20] = MoveState.Executed;
        host.Now = 10.7;
        flow.RunPass();
        host.Now = 10.7 + Leg;
        flow.RunPass();
        AssertEqual("3000", string.Join(",", host.CalledXs), "the later order's move was carried out first: it is shown");
        host.CarrierMoves[10] = MoveState.Executed;
        host.Now = 11.2;
        flow.RunPass();
        host.Now = 11.2 + Leg;
        flow.RunPass();
        AssertEqual("3000", string.Join(",", host.CalledXs), "the earlier order arrives after it and is countermanded");
        AssertTrue(host.Rows.Contains("0:discard:superseded"), "with a row: " + RowsOf(host));
    }

    private static void LostLatestMoveLeavesThePlayerWithTheLastOrderHeCan()
    {
        var host = new FakeHost { Now = 10.5, Slot = null, Move = MoveState.InFlight };
        host.CarrierMoves[10] = MoveState.Executed;
        host.CarrierMoves[20] = MoveState.Lost;
        var flow = NewFlow(host);
        PutMoveOrder(flow, issued: 10.0, unit: 1, type: 0, x: 0f, carrier: 10);
        PutMoveOrder(flow, issued: 10.2, unit: 2, type: 0, x: 3000f, carrier: 20);
        flow.RunPass();
        host.Now = 10.5 + Leg;
        flow.RunPass();
        AssertEqual("0", string.Join(",", host.CalledXs), "the move that was lost is not announced; the earlier one is");
        AssertTrue(host.Rows.Contains("0:discard:move-lost"), "the lost one is reported: " + RowsOf(host));
    }

    // Property: whatever the issue order, the release times and the lost moves, every captured order ends in
    // exactly one reported outcome, nothing stays queued, and the last order shown is the latest-issued one
    // whose move was not lost.
    private static void NoOrderIsSilentlyLostAndTheLatestIsShownLast()
    {
        var rng = new Random(20261016);
        for (int trial = 0; trial < 400; trial++)
        {
            var host = new FakeHost { Now = 10.0, Slot = null, Move = MoveState.InFlight };
            var flow = NewFlow(host);
            int n = 2 + rng.Next(6);
            var execAt = new double[n];
            var lost = new bool[n];
            var armed = new bool[n];
            int latestKept = -1;
            for (int i = 0; i < n; i++)
            {
                armed[i] = rng.Next(4) != 0;
                lost[i] = armed[i] && rng.Next(5) == 0;
                execAt[i] = 10.2 + rng.NextDouble() * 0.9;
                // x apart by 500 m so no capture is a near-duplicate; one issuer per order so no cadence drop.
                PutMoveOrder(flow, issued: 10.0 + 0.01 * i, unit: 1 + i, type: 0, x: 500f * i,
                    origin: armed[i] ? OrderOrigin.Executor : OrderOrigin.P62, armed: armed[i], carrier: 100 + i);
                if (!lost[i]) latestKept = i;
            }
            for (double t = 10.0; t < 13.0; t += 0.01)
            {
                host.Now = t;
                for (int i = 0; i < n; i++)
                {
                    if (!armed[i]) continue;
                    host.CarrierMoves[100 + i] = t >= execAt[i] ? (lost[i] ? MoveState.Lost : MoveState.Executed) : MoveState.InFlight;
                }
                flow.RunPass();
            }
            int terminal = 0;
            foreach (string row in host.Rows)
                if (row.EndsWith(":deliver:accepted") || row.Contains(":discard:")) terminal++;
            AssertEqual(n, terminal, "trial " + trial + ": every order has exactly one reported outcome: " + RowsOf(host));
            AssertEqual(0, flow.Queue.Count, "trial " + trial + ": nothing is left queued");
            if (latestKept >= 0)
                AssertEqual(500f * latestKept, host.CalledXs[host.CalledXs.Count - 1], "trial " + trial + ": the last order shown is the latest-issued one that was not lost");
            for (int k = 1; k < host.CalledXs.Count; k++)
                AssertTrue(host.CalledXs[k] > host.CalledXs[k - 1], "trial " + trial + ": orders are shown in issue order");
        }
    }

    private static void MoveStateClassification()
    {
        AssertEqual(MoveState.InFlight, WlMoveState.Classify(moveQueued: true, courierLost: false), "queued is in flight");
        AssertEqual(MoveState.Lost, WlMoveState.Classify(moveQueued: true, courierLost: true), "queued with its courier gone is lost");
        AssertEqual(MoveState.Executed, WlMoveState.Classify(moveQueued: false, courierLost: false), "not queued is executed");
        AssertEqual(MoveState.Executed, WlMoveState.Classify(moveQueued: false, courierLost: true), "a deleted entry has no courier to lose");
    }

    // The executor notes the queued move of every unit it moved (not only the ordered group), the dispatch
    // carries the one carried out last, and the flow arms an entry only from that carrier.
    private static void RuntimeArmsFromTheExecutorsCarrier()
    {
        string exec = Code("src/WhiskeyRealism/Tactical/Battle/OrderExecutorRuntime.cs");
        string body = BodyOf(exec, "internal static int Execute(");
        int loop = body.IndexOf("for (int t = 0; t < plan.Targets.Count; t++)", StringComparison.Ordinal);
        int issue = body.IndexOf("IssueMove(", StringComparison.Ordinal);
        int note = body.IndexOf("carrier.Consider(", StringComparison.Ordinal);
        int emit = body.IndexOf("TacticalPlayerDispatchRuntime.Emit(", StringComparison.Ordinal);
        AssertTrue(loop >= 0 && issue > loop && note > issue && emit > note, "each moved target's queue entry is noted after its IssueMove, inside the target loop, before Emit");
        AssertTrue(Regex.IsMatch(body, @"if \(WlMoveQueue\.TryPending\(unit,\s*out float queuedAt\)\)\s*carrier\.Consider\(own\[targetIndex\]\.InstanceId,\s*queuedAt\);"),
            "the carrier candidate is the issued target (a child in a fan-out), with its pending entry's processing time");
        AssertTrue(Regex.IsMatch(body, @"IntentKind\(winner\.Task\),\s*in carrier\)"), "and Emit receives it");
        string code = Code(FlowRuntime);
        AssertTrue(Regex.IsMatch(BodyOf(code, "private static CaptureVerdict Route("), @"if\s*\(call\.CarrierUnitId != 0 && CouriersOn\(\)\)\s*\{\s*entry\.AwaitsMove = true;\s*entry\.CarrierUnitId = call\.CarrierUnitId;\s*entry\.CarrierTime = call\.CarrierTime;\s*\}"),
            "Route arms an entry from the call's carrier and only while couriers are on");
        AssertFalse(BodyOf(code, "private static bool TryMakeEntry(").Contains("AwaitsMove"), "no other origin is armed: vanilla's own orders are timed as before");
        AssertTrue(BodyOf(code, "public MoveState MoveOf(").Contains("WlMoveQueue.StateOf(carrier, e.CarrierTime)"), "the host asks about the carrier unit and its entry");
        string queue = Code("src/WhiskeyRealism/Tactical/PlayerOrders/WlMoveQueue.cs");
        AssertTrue(queue.Contains("WlMoveState.Carries(entry.ordertype, entry.processingtime, carrierProcessingTime)"), "only an entry that carries the order counts");
        AssertTrue(queue.Contains("line.type == 1") && queue.Contains("line.targetunit") && queue.Contains("line.courierref == null"),
            "lost is vanilla's own test (125281-125286) on the entry's primary mounted courier line");
        AssertTrue(queue.Contains("if (latest == null || entry.processingtime > latest.processingtime) latest = entry;"), "the carrier entry of a unit is its latest pending move");
        AssertTrue(queue.Contains("unit.transform.parent"), "the scene parent is the queue owner");
        AssertTrue(TestFixtures.ReadRepoFile("src/WhiskeyRealism/Tactical/PlayerOrders/WlMoveQueue.cs").Contains("SetWaypoint 91461-91504"),
            "and the comment names the vanilla lines it mirrors (SetWaypoint 91461-91504)");
        AssertFalse(queue.Contains("orderstate"), "the unit's shared orderstate is not evidence about one order");
    }
}
