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
        e.Carrier = armed ? new WlMoveCarrier { UnitId = carrier != 0 ? carrier : unit, OrderType = 1 } : default(WlMoveCarrier);
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

    // A newer move voids the queued orders of the old one BEFORE the new order is queued: voiding after Emit would discard the
    // order the new move just queued.
    private static void ExecutorVoidsTheGroupsOldOrdersBeforeQueueingTheNewOne()
    {
        string body = BodyOf(Code("src/WhiskeyRealism/Tactical/Battle/OrderExecutorRuntime.cs"), "internal static int Execute(");
        int voidAt = body.IndexOf("WlOrderFlowRuntime.SupersedeExecutorOrders(winner.GroupId, plan.X, plan.Z);", StringComparison.Ordinal);
        int emit = body.IndexOf("TacticalPlayerDispatchRuntime.Emit(", StringComparison.Ordinal);
        AssertTrue(voidAt > 0 && emit > voidAt, "the group's old executor orders are voided before Emit queues the new one");
        AssertTrue(Regex.IsMatch(body, @"if \(executed > 0 && winner\.Channel == OrderChannel\.Move && !OrderExecutor\.IsImmediatePlacement\(winner\) && \(winner\.Flags & IntentFlags\.Maintenance\) == 0\)\s*WlOrderFlowRuntime\.SupersedeExecutorOrders"),
            "only an executed Move that is not a deployment placement and not maintenance voids: a recovery's target can be a fresh assembly point (TryRecoveryPath), and it would void a real order the brigade is still carrying out");
        string flow = Code(FlowRuntime);
        string supersede = BodyOf(flow, "internal static void SupersedeExecutorOrders(");
        AssertTrue(supersede.Contains("catch (Exception ex)") && supersede.Contains("OnceLog.Warning(\"wl-order:supersede-intent\""), "never throws, one static warning key");
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
        carrier.Consider(5, 17.30f, 17.10f, 1, 100f, 200f);
        carrier.Consider(6, 17.50f, 17.12f, 1, 110f, 210f);
        carrier.Consider(7, 17.40f, 17.14f, 1, 120f, 220f);
        carrier.Consider(0, 99f, 99f, 1, 0f, 0f);
        AssertEqual(6, carrier.UnitId, "the child carried out last carries the player's order");
        AssertNear(17.50f, carrier.ProcessingTime, 1e-4f, "with its processing time");
        AssertNear(17.12f, carrier.Stamp, 1e-4f, "and its own creation stamp, which is what the order is matched by");
        AssertTrue(carrier.X == 110f && carrier.Z == 210f && carrier.OrderType == 1, "and its own target and ordertype");
        carrier.Consider(8, 17.50f, 17.20f, 1, 130f, 230f);
        AssertEqual(6, carrier.UnitId, "a tie keeps the first");
    }

    private static WlMoveCarrier Rides(float stamp, int orderType = 1) => new WlMoveCarrier { UnitId = 1, Stamp = stamp, OrderType = orderType };

    private static void OnlyTheCarryingEntryCounts()
    {
        AssertTrue(WlMoveState.Carries(1, 17.10f, Rides(17.10f)), "the recorded entry itself");
        AssertTrue(WlMoveState.Carries(1, 17.30f, Rides(17.10f)), "a later entry of the unit and ordertype (the recorded one was replaced)");
        AssertFalse(WlMoveState.Carries(1, 17.00f, Rides(17.10f)), "an entry created before the recorded one is another order's");
        AssertFalse(WlMoveState.Carries(2, 17.30f, Rides(17.10f, 2)), "a stop entry is no move");
        AssertFalse(WlMoveState.Carries(4, 17.30f, Rides(17.10f, 4)), "nor is a stance entry");
    }

    // R2 of verify round 2: ProcessOrders rewrites an entry's lastupdate on every pass once its processing time has
    // passed (125331), so an OLDER entry of the same unit that is due has a stamp at or after the stamp taken from a
    // newer pending one, and the stamp alone lets it carry the newer order (a lost courier on it would then discard
    // a newer order). Two entries of one unit and one ordertype cannot coexist (AddToOrderQueue replaces the first,
    // 124929-124933), so an older entry is of the other move ordertype: the ordertype keeps it out.
    private static void OlderDueEntryOfTheOtherOrderTypeDoesNotCarry()
    {
        // The recorded carrier: a new-path entry (1) created at 17.30. The older append entry (0) was created at
        // 17.00 and is being processed: its stamp is rewritten to 17.45.
        WlMoveCarrier carrier = Rides(17.30f, orderType: 1);
        AssertFalse(WlMoveState.Carries(0, 17.45f, carrier), "the older entry's rewritten stamp is later than the carrier's, but it is the other ordertype");
        AssertTrue(WlMoveState.Carries(1, 17.45f, carrier), "the carrier itself, due and being processed, still carries");
        AssertTrue(17.45f >= carrier.Stamp, "fixture: by stamp alone the older entry would match");
    }

    // F2 of verify round 1: a permanently detached pool brigade's second SetWaypoint deletes its entry and adds the
    // new one without the 0.02 h standard time (124929-124931, 124966), so the replacement's PROCESSING time can be
    // earlier than the old entry's, while its creation stamp is later. Matching by processing time released the
    // order early; the creation stamp does not.
    private static void ReplacementEntryWithAnEarlierProcessingTimeStillCarries()
    {
        // The old entry (the unit's latest, the recorded carrier): created 17.100, processing 17.540.
        // The replacement: created 17.300, processing 17.320 (queued behind nothing else any more).
        const float carrierStamp = 17.100f, replacementStamp = 17.300f, replacementProcessing = 17.320f, carrierProcessing = 17.540f;
        AssertTrue(replacementProcessing < carrierProcessing, "fixture: the replacement is earlier in processing time");
        AssertTrue(WlMoveState.Carries(1, replacementStamp, Rides(carrierStamp)), "the replacement carries the order (stamp)");
        AssertFalse(WlMoveState.Carries(1, replacementProcessing, Rides(carrierProcessing)),
            "control: matched by processing time as before the fix, the replacement is missed and the order released early");
    }

    // R1 of verify round 2: the delivery pass runs at most once per 30 AI frames (4970, 5684), so a unit that carried
    // its move out and finished it between two passes has no path when the entry is first seen gone. The reading must
    // not be "lost": a lost move was never made, an arrived or fighting unit made it.
    private static void ArrivalInsideTheGapIsCarriedOutNotLost()
    {
        AssertEqual(MoveBasis.Arrived, WlMoveState.BasisAfterGone(pathHeld: false, pathEndMeters: 0f, distanceToTargetMeters: 20f, inContact: false), "arrived within 20 m of its target");
        AssertEqual(MoveBasis.Arrived, WlMoveState.BasisAfterGone(false, 0f, WlMoveState.ArrivalMeters, false), "exactly at the arrival distance");
        AssertEqual(MoveState.Executed, WlMoveState.Classify(false, false, WlMoveState.BasisAfterGone(false, 0f, 35f, false) != MoveBasis.None), "the player is told");
        AssertEqual(MoveBasis.None, WlMoveState.BasisAfterGone(false, 0f, WlMoveState.ArrivalMeters + 1f, false), "just outside it is not an arrival");
        AssertNear(WhiskeyRealism.Tactical.Battle.OrderArbiter.DedupeMeters, WlMoveState.ArrivalMeters, 1e-4f, "the arrival distance is the executor's own: a unit within it counts as at its target");
    }

    // Smoke 2026-10-17 (session 20261017-180916), order #6: the fall-back (type 12) the executor dispatched at 19:01:32 for a
    // brigade target about 2 km to the rear. The brigade moved about 45 m, stopped at (1232,-799) in contact and stayed.
    // The old test (any path, or contact, or arrival) released the order on a path that was a re-lay and on a contact that
    // was 2 km short of the target.
    private static void ContactFarFromTheTargetIsLostSmokeOrder6()
    {
        AssertEqual(MoveBasis.None, WlMoveState.BasisAfterGone(pathHeld: false, pathEndMeters: 0f, distanceToTargetMeters: 2000f, inContact: true),
            "stopped in contact 2 km short of the target: the move was not carried out");
        AssertEqual(MoveState.Lost, WlMoveState.Classify(false, false, WlMoveState.BasisAfterGone(false, 0f, 2000f, true) != MoveBasis.None), "the order is discarded as move-lost");
    }

    private static void ContactNearTheTargetIsCarriedOut()
    {
        // A unit that met the enemy near the objective halted (CheckEnemyContact StopRegiment, 117380): it executed the move up to the fight.
        AssertEqual(MoveBasis.Contact, WlMoveState.BasisAfterGone(false, 0f, WlMoveState.ContactNearMeters, true), "in contact exactly at the named distance");
        AssertEqual(MoveBasis.None, WlMoveState.BasisAfterGone(false, 0f, WlMoveState.ContactNearMeters + 1f, true), "one metre farther is not near");
        AssertEqual(MoveBasis.None, WlMoveState.BasisAfterGone(false, 0f, 250f, false), "near the target but not in contact and no path: a loss, as before");
        AssertTrue(WlMoveState.ContactNearMeters > WlMoveState.ArrivalMeters, "the contact distance is wider than arrival, or it would add nothing");
    }

    private static void PathCountsOnlyWhenItEndsAtTheTarget()
    {
        AssertEqual(MoveBasis.Path, WlMoveState.BasisAfterGone(pathHeld: true, pathEndMeters: 25f, distanceToTargetMeters: 900f, inContact: false), "marching on a path that ends at the target");
        AssertEqual(MoveBasis.Path, WlMoveState.BasisAfterGone(true, WlMoveState.ArrivalMeters, 900f, false), "a path ending exactly at the arrival distance");
        AssertEqual(MoveBasis.None, WlMoveState.BasisAfterGone(true, WlMoveState.ArrivalMeters + 1f, 900f, false),
            "a path that ends 61 m from the target is another move (a formation re-lay or a later retarget)");
        AssertEqual(MoveBasis.None, WlMoveState.BasisAfterGone(pathHeld: false, pathEndMeters: 5f, distanceToTargetMeters: 900f, inContact: false),
            "a path end with no path held says nothing");
    }

    private static void BasisPrecedenceIsArrivedThenPathThenContact()
    {
        AssertEqual(MoveBasis.Arrived, WlMoveState.BasisAfterGone(true, 10f, 20f, true), "standing at the target outranks the rest");
        AssertEqual(MoveBasis.Path, WlMoveState.BasisAfterGone(true, 10f, 200f, true), "a path to the target outranks contact");
        AssertEqual(MoveBasis.Contact, WlMoveState.BasisAfterGone(true, 900f, 200f, true), "contact near the target counts when the path went elsewhere");
    }

    private static void GateVerdictIsOnTheDeliveryRow()
    {
        var host = new FakeHost { Now = 10.5, Slot = null, Move = MoveState.Executed, Basis = MoveBasis.Contact };
        var flow = NewFlow(host);
        PutMoveOrder(flow, issued: 10.0);
        flow.RunPass();
        host.Now = 10.5 + Leg;
        flow.RunPass();
        AssertEqual("1:deliver:accepted", host.Rows[host.Rows.Count - 1], "delivered after the leg");
        AssertEqual(MoveBasis.Contact, host.GateOnLastRow, "the gate's verdict rides on the entry the row is written for");
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
        AssertEqual(MoveState.InFlight, WlMoveState.Classify(moveQueued: true, courierLost: false, carriedOutAfterGone: true), "queued is in flight");
        AssertEqual(MoveState.InFlight, WlMoveState.Classify(moveQueued: true, courierLost: false, carriedOutAfterGone: false), "a queued move's path is not asked");
        AssertEqual(MoveState.Lost, WlMoveState.Classify(moveQueued: true, courierLost: true, carriedOutAfterGone: true), "queued with its courier gone is lost");
        AssertEqual(MoveState.Executed, WlMoveState.Classify(moveQueued: false, courierLost: false, carriedOutAfterGone: true), "gone and carried out (path, arrival or contact)");
        // F1 of the verify round: vanilla clears a lost line's waypoints (125282) and deletes the entry one or
        // two passes later (125425), so by the time a pass looks the courier line is gone with the entry; only
        // the path is left to tell the lost move from the carried-out one.
        AssertEqual(MoveState.Lost, WlMoveState.Classify(moveQueued: false, courierLost: false, carriedOutAfterGone: false), "gone and nothing to show for it: lost");
        AssertEqual(MoveState.Lost, WlMoveState.Classify(moveQueued: false, courierLost: true, carriedOutAfterGone: false), "gone, nothing to show: lost whatever the line said");
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
        AssertTrue(Regex.IsMatch(body, @"if \(unit\.regimentpaths > 0 && WlMoveQueue\.TryPending\(unit,\s*out float queuedAt,\s*out float stamp,\s*out int orderType\)\)\s*carrier\.Consider\(own\[targetIndex\]\.InstanceId,\s*queuedAt,\s*stamp,\s*orderType,\s*x,\s*z\);"),
            "the carrier candidate is the issued target (a child in a fan-out) that holds a path, with its pending entry's processing time, stamp and ordertype and the target it was given");
        AssertTrue(Regex.IsMatch(body, @"IntentKind\(winner\.Task\),\s*in carrier,\s*winner\.Task\)"), "and Emit receives it, with the group's task");
        string code = Code(FlowRuntime);
        AssertTrue(Regex.IsMatch(BodyOf(code, "private static CaptureVerdict Route("), @"if\s*\(call\.Carrier\.Has && CouriersOn\(\)\)\s*\{\s*entry\.AwaitsMove = true;\s*entry\.Carrier = call\.Carrier;\s*\}"),
            "Route arms an entry from the call's carrier and only while couriers are on");
        AssertFalse(BodyOf(code, "private static bool TryMakeEntry(").Contains("AwaitsMove"), "TryMakeEntry arms nothing: the executor carrier and the vanilla pursuit are armed in Route");
        string moveOf = BodyOf(code, "public MoveState MoveOf(");
        AssertTrue(moveOf.Contains("Regiment carrier = ResolveUnit(e.Carrier.UnitId);") && moveOf.Contains("WlMoveQueue.StateOf(carrier, in e.Carrier, out basis)"),
            "the host asks about the carrier unit (not the ordered unit) and its entry");
        string queue = Code("src/WhiskeyRealism/Tactical/PlayerOrders/WlMoveQueue.cs");
        AssertTrue(queue.Contains("WlMoveState.Carries(entry.ordertype, entry.lastupdate, in carrier)"), "only an entry that carries the order counts, matched by ordertype and creation stamp");
        AssertTrue(queue.Contains("line.type == 1 && line.lineactive") && queue.Contains("line.targetunit") && queue.Contains("line.courierref == null"),
            "lost is vanilla's own test (125282-125286: an active mounted line without a courier) on the entry's primary line");
        AssertTrue(queue.Contains("WlMoveState.BasisAfterGone(IsMoving(unit), pathEnd, distance, unit.combatstatussubordinates > 0f, carrier.SlackMeters)") && queue.Contains("carrier.X") && queue.Contains("carrier.Z"),
            "an entry that is gone is told lost from carried out by the unit's path end, its distance to the target it was given and its contact");
        AssertTrue(queue.Contains("unit.lastsetwaypointposition"), "the held path's end is the unit's last set waypoint, which vanilla stamps on every group move (92067)");
        AssertTrue(queue.Contains("if (queued) return WlMoveState.Classify(true, lost, false);"), "a queued entry is judged by its courier line alone: the path says nothing while the move waits");
        AssertTrue(queue.Contains("if (latest == null || entry.processingtime > latest.processingtime) latest = entry;"), "the carrier entry of a unit is its latest pending move");
        AssertTrue(queue.Contains("processingTime = entry.processingtime;") && queue.Contains("stamp = entry.lastupdate;") && queue.Contains("orderType = entry.ordertype;"), "the carrier records the entry's processing time and its creation stamp");
        AssertTrue(queue.Contains("unit.transform.parent"), "the scene parent is the queue owner");
        AssertFalse(queue.Contains("queue == null"), "the owner queue is never null (orderqueue is initialised at its declaration, 111866): no unreachable no-queue verdict");
        AssertTrue(TestFixtures.ReadRepoFile("src/WhiskeyRealism/Tactical/PlayerOrders/WlMoveQueue.cs").Contains("SetWaypoint 91461-91504"),
            "and the comment names the vanilla lines it mirrors (SetWaypoint 91461-91504)");
        AssertFalse(queue.Contains("orderstate"), "the unit's shared orderstate is not evidence about one order");
    }
}
