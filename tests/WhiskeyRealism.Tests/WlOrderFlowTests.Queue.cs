using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.PlayerOrders;

using static TestHarness;

// The courier queue (D8): one per scope, every discard reported, no silent drops.
internal static partial class WlOrderFlowTests
{
    private sealed class RecordingSink : ICourierDiscardSink
    {
        public readonly List<(int type, int issuer, string reason)> Discards = new List<(int, int, string)>();
        public void Discarded(in CourierEntry e, string reason) => Discards.Add((e.Type, e.IssuerKey, reason));
        public int Count(string reason)
        {
            int n = 0;
            foreach (var d in Discards) if (d.reason == reason) n++;
            return n;
        }
    }

    private const double Hold = PlayerOrderReadmitPolicy.MinHoldBattleHours;

    private static CourierEntry QEntry(int issuer = 1, int type = 2, OrderOrigin origin = OrderOrigin.Vanilla, float x = 0f, float z = 0f,
        double issuedAt = 0.0, double dueAt = 0.0, bool campaign = false)
        => new CourierEntry
        {
            UnitId = 100 + issuer,
            Type = type,
            X = x,
            Z = z,
            Origin = origin,
            IssuerKey = issuer,
            IssuedAt = issuedAt,
            DueAt = dueAt,
            CampaignCall = campaign,
            Name = "Peach Orchard",
        };

    private static bool Enq(WlCourierQueue q, in CourierEntry e, out string drop, double window = Hold) => q.TryEnqueue(in e, window, out drop);

    private static void SameIssuerTypeWithinHoldDropped()
    {
        var sink = new RecordingSink();
        var q = new WlCourierQueue(sink);
        AssertTrue(Enq(q, QEntry(issuer: 1, type: 2, x: 0, issuedAt: 0.0), out _), "first");
        AssertFalse(Enq(q, QEntry(issuer: 1, type: 2, x: 600, issuedAt: 0.1), out string drop), "same issuer, type and origin inside the hold");
        AssertEqual("rate-limited", drop, "reason");
        AssertTrue(Enq(q, QEntry(issuer: 2, type: 2, x: 1200, issuedAt: 0.1), out _), "another issuer");
        AssertTrue(Enq(q, QEntry(issuer: 1, type: 3, x: 1800, issuedAt: 0.1), out _), "another type");
        AssertTrue(Enq(q, QEntry(issuer: 1, type: 2, x: 2400, issuedAt: 0.30), out _), "after the hold window");
        // A taken entry still counts: the HQ already sent that courier.
        var q2 = new WlCourierQueue(sink);
        Enq(q2, QEntry(issuer: 1, type: 2, x: 0, issuedAt: 0.0, dueAt: 0.0), out _);
        AssertTrue(q2.TryTakeDue(0.0, out _, out _), "take it");
        AssertFalse(Enq(q2, QEntry(issuer: 1, type: 2, x: 600, issuedAt: 0.1), out drop), "already-taken entries still rate-limit");
        AssertEqual("rate-limited", drop, "taken reason");
        AssertEqual(0, sink.Discards.Count, "a capture drop is returned, not reported to the sink");
    }

    private static void ReleaseDoesNotRateLimitVanillaDefend()
    {
        var q = new WlCourierQueue(new RecordingSink());
        AssertTrue(Enq(q, QEntry(issuer: 5, type: 12, origin: OrderOrigin.Vanilla, x: 0, issuedAt: 0.0), out _), "vanilla defend");
        AssertTrue(Enq(q, QEntry(issuer: 5, type: 12, origin: OrderOrigin.Release, x: 900, issuedAt: 0.01), out _), "a release 12 from the same issuer is another origin");
        AssertFalse(Enq(q, QEntry(issuer: 5, type: 12, origin: OrderOrigin.Release, x: 1800, issuedAt: 0.02), out string drop), "but a second release is limited");
        AssertEqual("rate-limited", drop, "reason");
    }

    private static void RetreatNeverRateLimited()
    {
        var q = new WlCourierQueue(new RecordingSink());
        AssertTrue(Enq(q, QEntry(issuer: 1, type: 15, origin: OrderOrigin.Retreat, x: 0, issuedAt: 0.0), out _), "first retreat");
        AssertTrue(Enq(q, QEntry(issuer: 1, type: 15, origin: OrderOrigin.Retreat, x: 2000, issuedAt: 0.01), out _), "second retreat inside the window, another exit");
        // En-route still applies to a retreat: the same exit is already riding.
        AssertFalse(Enq(q, QEntry(issuer: 1, type: 15, origin: OrderOrigin.Retreat, x: 2050, issuedAt: 0.02), out string drop), "same exit");
        AssertEqual("en-route", drop, "reason");
    }

    private static void SamePointPendingDropped()
    {
        var q = new WlCourierQueue(new RecordingSink());
        AssertTrue(Enq(q, QEntry(issuer: 1, type: 2, x: 0, z: 0), out _), "pending");
        AssertFalse(Enq(q, QEntry(issuer: 2, type: 2, x: 100, z: 0), out string drop), "within 110 m");
        AssertEqual("en-route", drop, "reason");
        AssertFalse(Enq(q, QEntry(issuer: 3, type: 2, x: 110, z: 0), out _), "exactly 110 m is the same point");
        AssertTrue(Enq(q, QEntry(issuer: 4, type: 2, x: 110.5f, z: 0), out _), "110.5 m is a new point");
        AssertTrue(Enq(q, QEntry(issuer: 5, type: 3, x: 0, z: 0), out _), "another type at the same point");
        // The distance is flat XZ: height is not an input at all.
        AssertFalse(Enq(q, QEntry(issuer: 6, type: 2, x: 0, z: 109), out _), "XZ");
    }

    private static void DueOrderDelivery()
    {
        var q = new WlCourierQueue(new RecordingSink());
        Enq(q, QEntry(issuer: 1, type: 1, x: 0, dueAt: 0.10), out _);
        Enq(q, QEntry(issuer: 2, type: 2, x: 1000, dueAt: 0.05), out _);
        Enq(q, QEntry(issuer: 3, type: 3, x: 2000, dueAt: 0.30), out _);
        AssertFalse(q.TryTakeDue(0.04, out _, out _), "nothing due before 0.05");
        AssertTrue(q.TryTakeDue(0.12, out CourierEntry first, out bool sup), "due");
        AssertEqual(2, first.Type, "earliest due first");
        AssertFalse(sup, "not superseded");
        AssertTrue(q.TryTakeDue(0.12, out CourierEntry second, out _), "second due");
        AssertEqual(1, second.Type, "then the next");
        AssertFalse(q.TryTakeDue(0.12, out _, out _), "the third is not due");
        AssertEqual(1, q.Count, "one pending");
        AssertTrue(q.TryTakeDue(0.30, out CourierEntry third, out _), "due at exactly its time");
        AssertEqual(3, third.Type, "third");
    }

    private static void EqualDueTiesByEnqueueOrder()
    {
        var q = new WlCourierQueue(new RecordingSink());
        Enq(q, QEntry(issuer: 1, type: 3, x: 0, dueAt: 0.2), out _);
        Enq(q, QEntry(issuer: 2, type: 1, x: 1000, dueAt: 0.2), out _);
        Enq(q, QEntry(issuer: 3, type: 4, x: 2000, dueAt: 0.2), out _);
        q.TryTakeDue(0.5, out CourierEntry a, out _);
        q.TryTakeDue(0.5, out CourierEntry b, out _);
        q.TryTakeDue(0.5, out CourierEntry c, out _);
        AssertEqual(3, a.Type, "enqueue order 1");
        AssertEqual(1, b.Type, "enqueue order 2");
        AssertEqual(4, c.Type, "enqueue order 3");
    }

    private static void OlderThanAcceptedIsSuperseded()
    {
        var sink = new RecordingSink();
        var q = new WlCourierQueue(sink);
        Enq(q, QEntry(issuer: 1, type: 1, x: 0, issuedAt: 1.0, dueAt: 1.5), out _);
        Enq(q, QEntry(issuer: 2, type: 2, x: 1000, issuedAt: 2.0, dueAt: 2.5), out _);
        Enq(q, QEntry(issuer: 3, type: 3, x: 2000, issuedAt: 2.0, dueAt: 2.6), out _);
        q.MarkAccepted(2.0);
        AssertTrue(q.TryTakeDue(1.6, out CourierEntry older, out bool sup), "older entry is returned");
        AssertTrue(sup, "issued strictly before the last accepted: a later order countermanded it");
        AssertEqual(1, older.Type, "which entry");
        AssertEqual(1, sink.Count("superseded"), "reported through the sink");
        AssertTrue(q.TryTakeDue(3.0, out _, out sup), "same issue time");
        AssertFalse(sup, "equal is not older");
        // The highest acceptance wins, not the latest call.
        q.MarkAccepted(1.0);
        Enq(q, QEntry(issuer: 4, type: 4, x: 3000, issuedAt: 1.5, dueAt: 1.5), out _);
        q.TryTakeDue(2.0, out _, out sup);
        AssertTrue(sup, "a lower later acceptance does not lower the bar");
    }

    private static void RetreatNeverSuperseded()
    {
        var q = new WlCourierQueue(new RecordingSink());
        Enq(q, QEntry(issuer: 1, type: 15, origin: OrderOrigin.Retreat, x: 0, issuedAt: 1.0, dueAt: 1.0), out _);
        q.MarkAccepted(5.0);
        AssertTrue(q.TryTakeDue(2.0, out _, out bool sup), "taken");
        AssertFalse(sup, "a retreat is never countermanded by a lesser order");
    }

    private static void RequeueBypassesCadenceAndEnRoute()
    {
        var sink = new RecordingSink();
        var q = new WlCourierQueue(sink);
        Enq(q, QEntry(issuer: 1, type: 2, x: 0, issuedAt: 0.0, dueAt: 0.0), out _);
        q.TryTakeDue(0.0, out CourierEntry taken, out _);
        AssertTrue(Enq(q, QEntry(issuer: 2, type: 2, x: 50, issuedAt: 0.1, dueAt: 0.1), out _), "another issuer near the first");
        q.Requeue(in taken);
        AssertEqual(2, q.Count, "the requeued entry is back although its key is inside the hold and its point is en route");
        AssertEqual(0, sink.Discards.Count, "nothing discarded");
        // Order is by due time, then the original enqueue order.
        q.TryTakeDue(1.0, out CourierEntry a, out _);
        AssertEqual(1, a.IssuerKey, "the original enqueue order survives the requeue");
    }

    private static void OverflowDiscardsOldestAndReports()
    {
        var sink = new RecordingSink();
        var q = new WlCourierQueue(sink);
        for (int i = 0; i < WlCourierQueue.MaxPending; i++)
            AssertTrue(Enq(q, QEntry(issuer: 10 + i, type: 1, x: i * 1000f, issuedAt: 1.0 + i, dueAt: 50.0), out _), "fill " + i);
        AssertEqual(WlCourierQueue.MaxPending, q.Count, "full");
        AssertTrue(Enq(q, QEntry(issuer: 99, type: 1, x: 99000f, issuedAt: 20.0, dueAt: 50.0), out _), "the ninth enters");
        AssertEqual(WlCourierQueue.MaxPending, q.Count, "still at capacity");
        AssertEqual(1, sink.Discards.Count, "one discard");
        AssertEqual("courier-overflow", sink.Discards[0].reason, "reason");
        AssertEqual(10, sink.Discards[0].issuer, "the earliest issue time went");
        // Ties go by enqueue order.
        var sink2 = new RecordingSink();
        var q2 = new WlCourierQueue(sink2);
        for (int i = 0; i < WlCourierQueue.MaxPending; i++)
            Enq(q2, QEntry(issuer: 10 + i, type: 1, x: i * 1000f, issuedAt: 5.0, dueAt: 50.0), out _);
        Enq(q2, QEntry(issuer: 99, type: 1, x: 99000f, issuedAt: 5.0, dueAt: 50.0), out _);
        AssertEqual(10, sink2.Discards[0].issuer, "equal issue times: first enqueued goes");
    }

    private static void OverflowNeverEvictsRetreat()
    {
        var sink = new RecordingSink();
        var q = new WlCourierQueue(sink);
        Enq(q, QEntry(issuer: 1, type: 15, origin: OrderOrigin.Retreat, x: 0, issuedAt: 0.5, dueAt: 50.0), out _);
        for (int i = 1; i < WlCourierQueue.MaxPending; i++)
            Enq(q, QEntry(issuer: 10 + i, type: 1, x: i * 1000f, issuedAt: 1.0 + i, dueAt: 50.0), out _);
        AssertTrue(Enq(q, QEntry(issuer: 99, type: 1, x: 99000f, issuedAt: 20.0, dueAt: 50.0), out _), "ninth");
        AssertEqual(1, sink.Discards.Count, "one discard");
        AssertEqual(11, sink.Discards[0].issuer, "the retreat is older but the earliest non-retreat went");
        // Eight retreats pending: an incoming non-retreat is the one evicted and is returned, not sunk.
        var sink2 = new RecordingSink();
        var q2 = new WlCourierQueue(sink2);
        for (int i = 0; i < WlCourierQueue.MaxPending; i++)
            Enq(q2, QEntry(issuer: i, type: 15, origin: OrderOrigin.Retreat, x: i * 1000f, issuedAt: i, dueAt: 50.0), out _);
        AssertFalse(Enq(q2, QEntry(issuer: 99, type: 1, x: 99000f, issuedAt: 20.0, dueAt: 50.0), out string drop), "incoming evicted");
        AssertEqual("courier-overflow", drop, "reason");
        AssertEqual(0, sink2.Discards.Count, "returned to the caller, not double-reported");
        AssertEqual(WlCourierQueue.MaxPending, q2.Count, "all retreats kept");
    }

    private static void ClearReportsEveryEntry()
    {
        var sink = new RecordingSink();
        var q = new WlCourierQueue(sink);
        Enq(q, QEntry(issuer: 1, type: 1, x: 0), out _);
        Enq(q, QEntry(issuer: 2, type: 2, x: 1000), out _);
        Enq(q, QEntry(issuer: 3, type: 15, origin: OrderOrigin.Retreat, x: 2000), out _);
        q.Clear("eod-clear");
        AssertEqual(0, q.Count, "empty");
        AssertEqual(3, sink.Count("eod-clear"), "every pending entry reported with the reason");
        q.Clear("eod-clear");
        AssertEqual(3, sink.Discards.Count, "clearing an empty queue reports nothing");
    }

    private static void ClearForgetsHistory()
    {
        var q = new WlCourierQueue(new RecordingSink());
        Enq(q, QEntry(issuer: 1, type: 2, x: 0, issuedAt: 0.0), out _);
        q.MarkAccepted(9.0);
        q.Clear("command-change");
        AssertTrue(Enq(q, QEntry(issuer: 1, type: 2, x: 5000, issuedAt: 0.05, dueAt: 0.0), out _), "cadence history forgotten");
        AssertTrue(q.TryTakeDue(1.0, out _, out bool sup), "taken");
        AssertFalse(sup, "acceptance history forgotten");
    }

    private static void CampaignWindowIsOneDayInYears()
    {
        AssertNear(1f / 365.25f, (float)WlCampaignDispatchPolicy.RateLimitYears, 1e-9f, "one game day in campaign years (8,766 hours a year, vanilla's clock)");
        double window = WlCampaignDispatchPolicy.RateLimitYears;
        var q = new WlCourierQueue(new RecordingSink());
        // A campaign key includes the target: another capital inside the day still arrives.
        AssertTrue(Enq(q, QEntry(issuer: 1, type: 8, x: 0, z: 0, issuedAt: 0.0, campaign: true), out _, window), "first");
        AssertFalse(Enq(q, QEntry(issuer: 1, type: 8, x: 0, z: 0, issuedAt: window * 0.5, campaign: true), out string drop, window), "same target inside the day");
        AssertTrue(drop == "rate-limited" || drop == "en-route", "reason " + drop);
        AssertTrue(Enq(q, QEntry(issuer: 1, type: 8, x: 900, z: 0, issuedAt: window * 0.5, campaign: true), out _, window), "another target inside the day");
        while (q.TryTakeDue(1.0, out _, out _)) { }
        AssertTrue(Enq(q, QEntry(issuer: 1, type: 8, x: 0, z: 0, issuedAt: window * 1.1, campaign: true), out _, window), "same target after the day");
    }
}
