using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.PlayerOrders;

using static TestHarness;

// The capture decision and the delivery pass (D3, D8, D8a): driven through a fake host that
// plays vanilla's CheckCurrentOrderUpdate (accepts, refuses, throws, relays, refreshes).
internal static partial class WlOrderFlowTests
{
    private sealed class FakeOrder
    {
        public int Type;
        public float X, Y, Z;
    }

    private enum VanillaMode { Accept, Refuse, Throw, Relay, Refresh, ForeignWrite, SessionOnly }

    private sealed class FakeHost : IWlOrderHost
    {
        public double Now = 10.0;
        public bool Eod;
        public bool Readmit = true;
        public FakeOrder Slot;
        public int SessionNo;
        public HashSet<int> GoneUnits = new HashSet<int>();
        public bool ReleaseDue = true;
        public bool Delivering;
        public VanillaMode Vanilla = VanillaMode.Accept;
        public int Calls;
        public double Age = double.PositiveInfinity;
        public bool Held, Defensive, Flag2;
        public readonly List<string> Rows = new List<string>();
        public readonly List<(int type, P62Outcome outcome)> P62 = new List<(int, P62Outcome)>();
        public readonly List<int> AcceptedTypes = new List<int>();
        public readonly List<int> CalledTypes = new List<int>();
        public CourierEntry? Retreat;
        public CourierEntry? Release;
        public CourierEntry? FollowUp;
        public bool FollowUpDue = true;
        public int FollowUpBuilds;
        public int ReleaseBuilds, NonPayingMarks, Restamps;
        public int LastNonPayingSession = -1;
        public Func<bool> OnSlotRead; // returns true to swap the slot on this read
        public int SlotReads;
        public bool ObservedRestoredInThrowCallback, ObservedDeliveringOffInThrowCallback;
        public FakeOrder SavedBeforeClear;
        public object RestoredWith;

        public double NowHours => Now;
        public bool EodCycle => Eod;
        public bool ReadmitOn => Readmit;
        public int SlotType => Slot == null ? -1 : Slot.Type;
        public int Session => SessionNo;

        public object SlotObject
        {
            get
            {
                SlotReads++;
                if (OnSlotRead != null && OnSlotRead()) Slot = new FakeOrder { Type = 0 };
                return Slot;
            }
        }

        public ActiveOrderView ReadActive()
        {
            if (Slot == null) return default(ActiveOrderView);
            return new ActiveOrderView(true, Slot.Type, Slot.X, Slot.Y, Slot.Z, 0f, Age, Now, Held, Defensive, Flag2);
        }

        public bool UnitGone(in CourierEntry e) => GoneUnits.Contains(e.UnitId);
        public MoveState Move = MoveState.Executed;
        public int MoveAsks;
        public MoveState MoveOf(in CourierEntry e) { MoveAsks++; return Move; }
        public bool ReleaseStillDue(in CourierEntry e) => ReleaseDue;
        public void SetDelivering(bool on) => Delivering = on;
        public void ClearSlot() { SavedBeforeClear = Slot; Slot = null; }
        public void RestoreSlot(object saved) { Slot = (FakeOrder)saved; RestoredWith = saved; }

        public void CallVanilla(in CourierEntry e)
        {
            AssertTrue(Delivering, "vanilla is called with the bypass set");
            Calls++;
            CalledTypes.Add(e.Type);
            switch (Vanilla)
            {
                case VanillaMode.Accept:
                    Slot = new FakeOrder { Type = e.Type, X = e.X, Y = e.Y, Z = e.Z };
                    SessionNo++;
                    break;
                case VanillaMode.Throw:
                    throw new InvalidOperationException("vanilla threw");
                case VanillaMode.Relay:
                    SessionNo++;
                    Slot = new FakeOrder { Type = e.Type == 11 ? 2 : 11 };
                    break;
                case VanillaMode.Refresh:
                    Slot = new FakeOrder { Type = e.Type, X = e.X, Z = e.Z };
                    break;
                case VanillaMode.ForeignWrite:
                    Slot = new FakeOrder { Type = 0 };
                    break;
                case VanillaMode.SessionOnly:
                    SessionNo++;
                    break;
            }
        }

        public void VanillaThrew(in CourierEntry e, Exception ex)
        {
            ObservedRestoredInThrowCallback = Slot != null;
            ObservedDeliveringOffInThrowCallback = !Delivering;
        }

        public void Accepted(in CourierEntry e) => AcceptedTypes.Add(e.Type);
        public void Row(in CourierEntry e, string action, string reason) => Rows.Add(e.Type + ":" + action + ":" + reason);
        public void NotifyP62(in CourierEntry e, P62Outcome outcome) => P62.Add((e.Type, outcome));

        public bool TryBuildRetreat(out CourierEntry entry)
        {
            entry = Retreat ?? default(CourierEntry);
            return Retreat.HasValue;
        }

        public bool TryBuildRelease(out CourierEntry entry)
        {
            ReleaseBuilds++;
            entry = Release ?? default(CourierEntry);
            return Release.HasValue;
        }

        public bool TryBuildFollowUp(out CourierEntry entry)
        {
            FollowUpBuilds++;
            entry = FollowUp ?? default(CourierEntry);
            return FollowUp.HasValue;
        }

        public bool FollowUpStillDue(in CourierEntry e) => FollowUpDue;

        public void MarkNonPayingSession(int session)
        {
            NonPayingMarks++;
            LastNonPayingSession = session;
        }

        public void RestampNonPayingSession() => Restamps++;
    }

    private static WlOrderFlow NewFlow(FakeHost host) => new WlOrderFlow(host);

    private static void Put(WlOrderFlow flow, int type, double issuedAt, double dueAt, int unit = 1, float x = 0f,
        OrderOrigin origin = OrderOrigin.Vanilla, int issuer = 0)
    {
        CourierEntry e = QEntry(issuer: issuer == 0 ? unit : issuer, type: type, origin: origin, x: x, issuedAt: issuedAt, dueAt: dueAt);
        e.UnitId = unit;
        flow.Queue.TryEnqueue(in e, 0.0, out _);
    }

    private static string RowsOf(FakeHost h) => string.Join(",", h.Rows);

    // ------------------------------------------------------------- delivery pass

    private static void PassBuffersBeforeDelivering()
    {
        // An entry the pass puts back (the slot changed under it) must not be taken again in the same
        // pass: requeued entries return only after the pass.
        var host = new FakeHost { Slot = new FakeOrder { Type = 13 }, Vanilla = VanillaMode.ForeignWrite };
        var flow = NewFlow(host);
        Put(flow, 15, 9.0, 9.5, unit: 1, x: 9000f);
        flow.RunPass();
        AssertEqual(1, host.Calls, "vanilla called once, not once per re-take");
        AssertEqual(1, flow.Queue.Count, "the entry is back in the queue after the pass");
    }

    private static void SupersededUsesHighestAcceptedIssue()
    {
        var host = new FakeHost { Slot = null };
        var flow = NewFlow(host);
        Put(flow, 1, issuedAt: 3.0, dueAt: 9.0, unit: 1, x: 0f);     // issued later, due first
        Put(flow, 2, issuedAt: 2.0, dueAt: 9.5, unit: 2, x: 2000f);  // issued earlier, due second
        flow.RunPass();
        AssertEqual("1", string.Join(",", host.CalledTypes), "only the later order is delivered");
        AssertTrue(host.Rows.Contains("2:discard:superseded"), "the earlier one was countermanded by the pass's own acceptance: " + RowsOf(host));
    }

    private static void CheckOrderSupersededUnitGoneCrossScopeReleaseStale()
    {
        // One entry that qualifies for every discard reason: the first check wins.
        var host = new FakeHost { Slot = new FakeOrder { Type = 5 } };
        var flow = NewFlow(host);
        flow.Queue.MarkAccepted(100.0);
        host.GoneUnits.Add(7);
        host.ReleaseDue = false;
        Put(flow, 12, issuedAt: 1.0, dueAt: 9.0, unit: 7, origin: OrderOrigin.Release);
        flow.RunPass();
        AssertEqual("12:discard:superseded", RowsOf(host), "superseded beats unit-gone, cross-scope and release-stale");

        host = new FakeHost { Slot = new FakeOrder { Type = 5 } };
        flow = NewFlow(host);
        host.GoneUnits.Add(7);
        host.ReleaseDue = false;
        Put(flow, 12, issuedAt: 1.0, dueAt: 9.0, unit: 7, origin: OrderOrigin.Release);
        flow.RunPass();
        AssertEqual("12:discard:unit-gone", RowsOf(host), "unit-gone beats cross-scope and release-stale");

        host = new FakeHost { Slot = new FakeOrder { Type = 5 } };
        flow = NewFlow(host);
        host.ReleaseDue = false;
        Put(flow, 12, issuedAt: 1.0, dueAt: 9.0, unit: 7, origin: OrderOrigin.Release);
        flow.RunPass();
        AssertEqual("12:discard:cross-scope", RowsOf(host), "cross-scope beats release-stale");

        host = new FakeHost { Slot = new FakeOrder { Type = 12 } };
        flow = NewFlow(host);
        Put(flow, 12, issuedAt: 1.0, dueAt: 9.0, unit: 7, origin: OrderOrigin.Release);
        flow.RunPass();
        AssertEqual("12:discard:release-stale", RowsOf(host), "a release whose slot is no longer 13");
        AssertEqual(0, host.Calls, "nothing reached vanilla");
    }

    private static void ReleaseStaleWhenSlotNoLongerThirteen()
    {
        var host = new FakeHost { Slot = new FakeOrder { Type = 13 }, ReleaseDue = false };
        var flow = NewFlow(host);
        Put(flow, 12, 9.0, 9.0, origin: OrderOrigin.Release);
        flow.RunPass();
        AssertEqual("12:discard:release-stale", RowsOf(host), "ReleaseDue now false");

        host = new FakeHost { Slot = new FakeOrder { Type = 14 }, ReleaseDue = true };
        flow = NewFlow(host);
        Put(flow, 12, 9.0, 9.0, origin: OrderOrigin.Release);
        flow.RunPass();
        AssertEqual("12:discard:release-stale", RowsOf(host), "slot no longer 13 (vanilla moved on)");

        host = new FakeHost { Slot = new FakeOrder { Type = 13 }, ReleaseDue = true };
        flow = NewFlow(host);
        Put(flow, 12, 9.0, 9.0, origin: OrderOrigin.Release);
        flow.RunPass();
        AssertEqual(1, host.Calls, "a live release is delivered");
        AssertEqual(12, host.Slot.Type, "the hold replaced engage-at-will");
    }

    private static void CrossScopeUsesWhiskeyBattleSet()
    {
        foreach (int slotType in new[] { 0, 1, 2, 3, 4, 11, 12, 13, 14, 15 })
        {
            var host = new FakeHost { Slot = new FakeOrder { Type = slotType, X = 5000f } };
            var flow = NewFlow(host);
            Put(flow, 15, 9.0, 9.0, unit: 1, x: 9000f);
            flow.RunPass();
            AssertFalse(RowsOf(host).Contains("cross-scope"), "battle slot type " + slotType + " is not cross-scope: " + RowsOf(host));
        }
        foreach (int slotType in new[] { 5, 6, 7, 8, 9, 10, 16 })
        {
            var host = new FakeHost { Slot = new FakeOrder { Type = slotType } };
            var flow = NewFlow(host);
            Put(flow, 15, 9.0, 9.0);
            flow.RunPass();
            AssertEqual("15:discard:cross-scope", RowsOf(host), "campaign slot type " + slotType);
        }
    }

    private static void AcceptedNeedsSessionAndType()
    {
        var host = new FakeHost { Slot = null };
        var flow = NewFlow(host);
        Put(flow, 2, 9.0, 9.0);
        flow.RunPass();
        AssertEqual("2", string.Join(",", host.AcceptedTypes), "session advanced and the slot holds the entry's type");
        AssertEqual("2:deliver:accepted", RowsOf(host), "row");
        AssertEqual(0, host.P62.Count, "a non-P62 entry notifies no one");
        // The accepted issue time raises the supersession bar.
        Put(flow, 1, issuedAt: 5.0, dueAt: 9.0, unit: 2, x: 4000f);
        flow.RunPass();
        AssertTrue(host.Rows.Contains("1:discard:superseded"), "issued before the accepted one: " + RowsOf(host));
    }

    private static void RefreshedIsNotAccepted()
    {
        var host = new FakeHost { Slot = new FakeOrder { Type = 11 }, Vanilla = VanillaMode.Refresh };
        var flow = NewFlow(host);
        Put(flow, 11, 9.0, 9.0, origin: OrderOrigin.P62);
        flow.RunPass();
        AssertEqual(0, host.AcceptedTypes.Count, "an in-place refresh is not an acceptance");
        AssertEqual("11:deliver:refreshed", RowsOf(host), "row");
        AssertEqual(P62Outcome.ClearPending, host.P62[0].outcome, "#62 clears its pending signature only");
        // MarkAccepted was not called: an older entry is not superseded.
        Put(flow, 2, issuedAt: 0.5, dueAt: 9.0, unit: 2, x: 4000f);
        host.Slot = null;
        host.Vanilla = VanillaMode.Accept;
        flow.RunPass();
        AssertFalse(host.Rows.Contains("2:discard:superseded"), "no acceptance recorded by the refresh: " + RowsOf(host));
    }

    private static void RelayTookSlotNotRestored()
    {
        var host = new FakeHost { Slot = new FakeOrder { Type = 13 }, Vanilla = VanillaMode.Relay };
        var flow = NewFlow(host);
        Put(flow, 12, 9.0, 9.0, origin: OrderOrigin.Release);
        flow.RunPass();
        AssertEqual(11, host.Slot.Type, "the relay's order stays on the panel");
        AssertTrue(host.Rows.Contains("12:discard:relay-took-slot"), RowsOf(host));
        AssertEqual(0, host.AcceptedTypes.Count, "not an acceptance of the entry");
    }

    private static void RefusedAfterClearRestoresOnlyNullSlot()
    {
        var host = new FakeHost { Slot = new FakeOrder { Type = 13 }, Vanilla = VanillaMode.Refuse };
        FakeOrder old = host.Slot;
        var flow = NewFlow(host);
        Put(flow, 15, 9.0, 9.0, x: 9000f);
        flow.RunPass();
        AssertTrue(ReferenceEquals(old, host.Slot), "the saved object is back when vanilla refused");
        AssertEqual(0, host.SessionNo, "session unchanged");
        AssertTrue(host.Rows.Contains("15:discard:vanilla-refused"), RowsOf(host));
    }

    private static void RefusedWithForeignSlotRequeues()
    {
        var host = new FakeHost { Slot = new FakeOrder { Type = 13 }, Vanilla = VanillaMode.ForeignWrite };
        FakeOrder old = host.Slot;
        var flow = NewFlow(host);
        Put(flow, 15, 9.0, 9.0, x: 9000f);
        flow.RunPass();
        AssertFalse(ReferenceEquals(old, host.Slot), "the saved object is NOT restored over someone else's order");
        AssertEqual(0, host.Slot.Type, "the foreign order stays");
        AssertEqual(1, flow.Queue.Count, "the entry is requeued");
        AssertTrue(host.Rows.Contains("15:requeue:slot-changed"), RowsOf(host));
    }

    private static void OneRowPerOutcome()
    {
        // Each way an entry can end writes exactly one row.
        var cases = new (string name, Action<FakeHost, WlOrderFlow> setup, string expected)[]
        {
            ("accepted", (h, f) => { h.Slot = null; Put(f, 2, 9, 9); }, "2:deliver:accepted"),
            ("refused", (h, f) => { h.Slot = null; h.Vanilla = VanillaMode.Refuse; Put(f, 2, 9, 9); }, "2:discard:vanilla-refused"),
            ("threw", (h, f) => { h.Slot = null; h.Vanilla = VanillaMode.Throw; Put(f, 2, 9, 9); }, "2:discard:vanilla-threw"),
            ("unit gone", (h, f) => { h.GoneUnits.Add(1); Put(f, 2, 9, 9); }, "2:discard:unit-gone"),
            ("policy drop", (h, f) => { h.Slot = new FakeOrder { Type = 13 }; Put(f, 3, 9, 9); }, "3:discard:engage-at-will"),
            ("deferred", (h, f) => { h.Slot = new FakeOrder { Type = 2 }; h.Age = 0.0; Put(f, 2, 9, 9, x: 5000f); }, "2:defer:retarget-deferred"),
        };
        foreach (var c in cases)
        {
            var host = new FakeHost();
            var flow = NewFlow(host);
            c.setup(host, flow);
            flow.RunPass();
            AssertEqual(c.expected, RowsOf(host), "one row for: " + c.name);
        }
        // An EOD clear reports every pending entry once.
        var h2 = new FakeHost { Eod = true };
        var f2 = NewFlow(h2);
        Put(f2, 1, 9, 99, unit: 1, x: 0f);
        Put(f2, 2, 9, 99, unit: 2, x: 2000f);
        f2.RunPass();
        AssertEqual(2, h2.Rows.Count, "one row per cleared entry");
        AssertEqual(2, CountRows(h2, "discard:eod-clear"), "eod rows");
    }

    private static void P62BookkeepingPerOutcome()
    {
        P62Outcome Run(Action<FakeHost> configure, int type = 2, int slot = -1)
        {
            var host = new FakeHost { Slot = slot < 0 ? null : new FakeOrder { Type = slot } };
            configure(host);
            var flow = NewFlow(host);
            Put(flow, type, 9.0, 9.0, origin: OrderOrigin.P62, x: 5000f);
            flow.RunPass();
            AssertEqual(1, host.P62.Count, "exactly one #62 callback");
            return host.P62[0].outcome;
        }
        AssertEqual(P62Outcome.Accepted, Run(h => { }), "accepted");
        AssertEqual(P62Outcome.Attempt, Run(h => h.Vanilla = VanillaMode.Refuse), "vanilla refused");
        AssertEqual(P62Outcome.Attempt, Run(h => { }, type: 3, slot: 13), "policy drop");
        AssertEqual(P62Outcome.ClearPending, Run(h => h.GoneUnits.Add(1)), "unit gone is structural");
        AssertEqual(P62Outcome.ClearPending, Run(h => { }, type: 2, slot: 5), "cross-scope is structural");
        AssertEqual(P62Outcome.ClearPending, Run(h => h.Vanilla = VanillaMode.Relay), "relay took the slot");
        // A non-#62 origin never calls #62.
        var host2 = new FakeHost();
        var flow2 = NewFlow(host2);
        Put(flow2, 2, 9.0, 9.0, origin: OrderOrigin.Executor);
        flow2.RunPass();
        AssertEqual(0, host2.P62.Count, "executor entries do not touch #62");
        // Superseded and EOD-cleared #62 entries clear the pending signature only.
        var host3 = new FakeHost();
        var flow3 = NewFlow(host3);
        Put(flow3, 2, 9.0, 99.0, origin: OrderOrigin.P62);
        flow3.Queue.Clear("command-change");
        AssertEqual(P62Outcome.ClearPending, host3.P62[0].outcome, "a clear");
    }

    private static void DeliveryRestoresSlotWhenSessionUnchanged()
    {
        var host = new FakeHost { Slot = new FakeOrder { Type = 13 }, Vanilla = VanillaMode.Refuse };
        FakeOrder old = host.Slot;
        var flow = NewFlow(host);
        Put(flow, 15, 9.0, 9.0, x: 9000f);
        flow.RunPass();
        AssertTrue(ReferenceEquals(old, host.RestoredWith), "RestoreSlot received the saved object");
        AssertTrue(ReferenceEquals(old, host.Slot), "the slot holds it again");
        AssertFalse(host.Delivering, "the bypass is off again");
    }

    private static void AdvancedSessionIsNeverRestoredOver()
    {
        // Vanilla advanced the session (it issued something) and left the slot empty: the saved
        // order is stale, so it is not put back.
        var host = new FakeHost { Slot = new FakeOrder { Type = 13 }, Vanilla = VanillaMode.SessionOnly };
        var flow = NewFlow(host);
        Put(flow, 15, 9.0, 9.0, x: 9000f);
        flow.RunPass();
        AssertTrue(host.Slot == null, "the slot stays as vanilla left it");
        AssertTrue(host.RestoredWith == null, "RestoreSlot was not called after the session advanced");
    }

    private static void DeliveryRestoresSlotAndFlagOnVanillaThrow()
    {
        var host = new FakeHost { Slot = new FakeOrder { Type = 13 }, Vanilla = VanillaMode.Throw };
        FakeOrder old = host.Slot;
        var flow = NewFlow(host);
        Put(flow, 15, 9.0, 9.0, x: 9000f);
        flow.RunPass();
        AssertTrue(ReferenceEquals(old, host.Slot), "a throw leaves the saved order, not a null slot (6788 would go silent)");
        AssertFalse(host.Delivering, "a throw leaves capture enabled");
        AssertTrue(host.ObservedRestoredInThrowCallback, "restored by the time the throw is handled (a finally, not code after the catch)");
        AssertTrue(host.ObservedDeliveringOffInThrowCallback, "bypass off by the time the throw is handled");
        AssertTrue(host.Rows.Contains("15:discard:vanilla-threw"), RowsOf(host));
    }

    private static void DeferRequeuesAtDeferUntil()
    {
        var host = new FakeHost { Slot = new FakeOrder { Type = 2 }, Age = 0.05, Now = 10.0 };
        var flow = NewFlow(host);
        Put(flow, 2, 9.0, 9.0, x: 5000f);
        flow.RunPass();
        AssertEqual(0, host.Calls, "deferred, not delivered");
        AssertEqual(1, flow.Queue.Count, "kept");
        double until = 10.0 - 0.05 + PlayerOrderReadmitPolicy.MinHoldBattleHours;
        host.Now = until - 0.01;
        flow.RunPass();
        AssertEqual(0, host.Calls, "still not due just before deferUntil");
        host.Now = until + 0.0001;
        host.Age = double.PositiveInfinity; // the hold has now passed
        flow.RunPass();
        AssertEqual(1, host.Calls, "delivered once the hold has passed");
    }

    // ----------------------------------------------------------- derived retreat

    private static CourierEntry RetreatEntry() => QEntry(issuer: 4, type: 15, origin: OrderOrigin.Retreat, x: 9000f, issuedAt: 10.0, dueAt: 10.0);

    private static void RetreatDerivedWhenSlotNull()
    {
        var host = new FakeHost { Slot = null, Retreat = RetreatEntry() };
        var flow = NewFlow(host);
        flow.RunPass();
        AssertEqual("15", string.Join(",", host.CalledTypes), "a retreat is derived and delivered into an empty slot");
        AssertEqual(15, host.Slot.Type, "on the panel");
    }

    private static void RetreatDerivedAfterLoad()
    {
        // A mid-battle load drops 13/14/15 and the slot is null; session 0 as at battle start.
        var host = new FakeHost { Slot = null, SessionNo = 0, Retreat = RetreatEntry() };
        var flow = NewFlow(host);
        flow.RunPass();
        AssertEqual(1, host.Calls, "re-derived after the load");
        AssertTrue(host.Rows.Contains("15:deliver:accepted"), RowsOf(host));
    }

    private static void RetreatNotDerivedWhenPendingOrActive()
    {
        var active = new FakeHost { Slot = new FakeOrder { Type = 15 }, Retreat = RetreatEntry() };
        NewFlow(active).RunPass();
        AssertEqual(0, active.Calls, "a retreat is already on the panel");

        var pending = new FakeHost { Slot = null, Retreat = RetreatEntry() };
        var flow = NewFlow(pending);
        Put(flow, 15, 10.0, 99.0, unit: 9, origin: OrderOrigin.Vanilla, x: 3000f);   // a vanilla retreat still riding
        flow.RunPass();
        AssertEqual(0, pending.Calls, "nothing due");
        AssertEqual(1, flow.Queue.Count, "and no second retreat was derived next to the pending one");
    }

    private static void RetreatNotDerivedForCic()
    {
        // The host reports no retreat entry for a CIC (vanilla sends a CIC no 15, 6788).
        var host = new FakeHost { Slot = null, Retreat = null };
        var flow = NewFlow(host);
        flow.RunPass();
        AssertEqual(0, host.Calls, "no derivation without a host entry");
        AssertEqual(0, flow.Queue.Count, "nothing queued");
    }

    // ------------------------------------------------------------------ release

    private static CourierEntry ReleaseEntry() => QEntry(issuer: 5, type: 12, origin: OrderOrigin.Release, x: 2000f, issuedAt: 10.0, dueAt: 10.0);

    private static void ReleaseDerivedOverEngageAtWillOnly()
    {
        var host = new FakeHost { Slot = new FakeOrder { Type = 13 }, Release = ReleaseEntry() };
        var flow = NewFlow(host);
        flow.RunPass();
        AssertEqual("12", string.Join(",", host.CalledTypes), "the hold replaced engage-at-will");
        AssertEqual(12, host.Slot.Type, "on the panel");
        AssertTrue(host.Rows.Contains("12:deliver:accepted"), RowsOf(host));

        foreach (int slot in new[] { -1, 0, 12, 14, 15 })
        {
            var other = new FakeHost { Slot = slot < 0 ? null : new FakeOrder { Type = slot }, Release = ReleaseEntry() };
            NewFlow(other).RunPass();
            AssertEqual(0, other.ReleaseBuilds, "the host is not asked for a release over slot type " + slot);
        }
    }

    private static void ReleaseNotDerivedTwiceWhilePending()
    {
        var host = new FakeHost { Slot = new FakeOrder { Type = 13 }, Release = ReleaseEntry(), Now = 10.0 };
        var flow = NewFlow(host);
        // A release already on its courier (due later): no second one.
        CourierEntry pending = ReleaseEntry();
        pending.DueAt = 11.0;
        flow.Queue.TryEnqueue(in pending, 0.0, out _);
        flow.RunPass();
        AssertEqual(0, host.ReleaseBuilds, "no release is derived next to a pending one");
        AssertEqual(1, flow.Queue.Count, "the pending one waits");
    }

    private static void ReleaseLatchStartsQuietWhenBothFalse()
    {
        var latch = new ReleaseLatch();
        AssertTrue(double.IsNaN(latch.QuietSince), "not quiet at the start");
        AssertTrue(double.IsNaN(latch.Update(contact: true, nowHours: 5.0)), "contact: not quiet");
        AssertEqual(6.0, latch.Update(contact: false, nowHours: 6.0), "quiet since the first look with no contact");
        AssertEqual(6.0, latch.Update(contact: false, nowHours: 6.5), "the clock keeps running from the start of the quiet");
        AssertTrue(double.IsNaN(latch.Update(contact: true, nowHours: 6.6)), "contact resets it");
        AssertEqual(7.0, latch.Update(contact: false, nowHours: 7.0), "and it starts again");
        latch.Reset();
        AssertTrue(double.IsNaN(latch.QuietSince), "reset forgets");
    }

    private static void NonPayingDeliveryStampsTopUnits()
    {
        foreach (OrderOrigin origin in new[] { OrderOrigin.Release, OrderOrigin.FollowUp })
        {
            var host = new FakeHost { Slot = origin == OrderOrigin.Release ? new FakeOrder { Type = 13 } : null };
            var flow = NewFlow(host);
            Put(flow, 12, 9.0, 9.0, origin: origin);
            flow.RunPass();
            AssertEqual(1, host.NonPayingMarks, origin + " accepted: the session is marked non-paying once");
            AssertEqual(1, host.LastNonPayingSession, "for the session vanilla just bumped");
        }
        // Every pass re-asserts the stamp while a non-paying session is active.
        var h = new FakeHost();
        var f = NewFlow(h);
        f.RunPass();
        f.RunPass();
        AssertEqual(2, h.Restamps, "one restamp call per pass");
        h.Eod = true;
        f.RunPass();
        AssertEqual(2, h.Restamps, "none during the end-of-day cycle");
    }

    private static void PayingOriginsUntouched()
    {
        foreach (OrderOrigin origin in new[] { OrderOrigin.Vanilla, OrderOrigin.P62, OrderOrigin.Executor, OrderOrigin.Retreat })
        {
            var host = new FakeHost { Slot = null };
            var flow = NewFlow(host);
            Put(flow, 2, 9.0, 9.0, origin: origin);
            flow.RunPass();
            AssertEqual(1, host.AcceptedTypes.Count, origin + " was delivered");
            AssertEqual(0, host.NonPayingMarks, origin + " pays prestige as vanilla does");
        }
        AssertFalse(WlOrderFlow.IsNonPaying(OrderOrigin.Vanilla), "vanilla pays");
        AssertTrue(WlOrderFlow.IsNonPaying(OrderOrigin.Release) && WlOrderFlow.IsNonPaying(OrderOrigin.FollowUp), "the automatic holds do not");
    }

    // ------------------------------------------------------------- boundaries

    private static void CommandChangeClearsQueue()
    {
        var host = new FakeHost();
        var flow = NewFlow(host);
        Put(flow, 1, 9, 99, unit: 1, x: 0f);
        Put(flow, 2, 9, 99, unit: 2, x: 2000f);
        flow.NoteBoundary("battle-1", "unit-10", false);
        AssertEqual(2, flow.Queue.Count, "the first boundary only records it");
        flow.NoteBoundary("battle-1", "unit-10", false);
        AssertEqual(2, flow.Queue.Count, "no change: kept");
        flow.NoteBoundary("battle-1", "unit-11", false);
        AssertEqual(0, flow.Queue.Count, "a new player command clears the queue");
        AssertEqual(2, CountRows(host, "discard:command-change"), "every entry reported");
        Put(flow, 1, 9, 99, unit: 1, x: 0f);
        flow.NoteBoundary("battle-1", "unit-11", true);
        AssertEqual(0, flow.Queue.Count, "a CIC change clears it too");
    }

    private static int CountRows(FakeHost h, string suffix)
    {
        int n = 0;
        foreach (string r in h.Rows) if (r.EndsWith(suffix, StringComparison.Ordinal)) n++;
        return n;
    }

    private static void EodClearsBattleQueue()
    {
        var host = new FakeHost { Eod = true, Retreat = RetreatEntry() };
        var flow = NewFlow(host);
        Put(flow, 1, 9, 99, unit: 1, x: 0f);
        flow.RunPass();
        AssertEqual(0, flow.Queue.Count, "cleared at eodcycle > 0");
        AssertEqual(0, host.Calls, "nothing delivered during the end-of-day cycle");
        AssertEqual(1, CountRows(host, "discard:eod-clear"), "reported");
        AssertFalse(RowsOf(host).Contains("15:"), "no retreat is derived during the EOD cycle");
    }

    private static void CrossScopeEntryDiscarded()
    {
        var host = new FakeHost { Slot = new FakeOrder { Type = 8 } };
        var flow = NewFlow(host);
        Put(flow, 12, 9.0, 9.0);
        flow.RunPass();
        AssertEqual("12:discard:cross-scope", RowsOf(host), "a campaign order is on the slot");
        AssertEqual(0, host.Calls, "vanilla untouched");
    }

    private static void DeliveryWritesNothingWhenSlotChangedSinceRead()
    {
        var host = new FakeHost { Slot = new FakeOrder { Type = 13 } };
        var flow = NewFlow(host);
        // The slot changes between the decision's read and the write.
        int reads = 0;
        host.OnSlotRead = () => ++reads == 2;
        Put(flow, 15, 9.0, 9.0, x: 9000f);
        flow.RunPass();
        AssertEqual(0, host.Calls, "vanilla not called over a slot that changed");
        AssertEqual(0, host.Slot.Type, "the foreign order is untouched");
        AssertEqual(1, flow.Queue.Count, "requeued for the next pass");
        AssertTrue(host.Rows.Contains("15:requeue:slot-changed"), RowsOf(host));
        AssertTrue(host.SavedBeforeClear == null, "the slot was never cleared");
    }

    // ------------------------------------------------------------------ capture

    private static CaptureInput Capturable(int type = 2, float x = 3000f)
    {
        var facts = AncestorCall();
        facts.Type = type;
        return new CaptureInput
        {
            Facts = facts,
            Enabled = true,
            ReadmitOn = true,
            CouriersOn = true,
            ModeAllowsWrites = true,
            Candidate = new OrderCandidateView(type, x, 0f, 0f, 0f, OrderOrigin.Vanilla, false),
        };
    }

    private static CaptureVerdict Verdict(in CaptureInput c, ActiveOrderView active, out string reason)
        => WlOrderFlow.DecideCapture(in c, in active, out _, out reason);

    private static void DeliveringFlagBypassesCapture()
    {
        CaptureInput c = Capturable();
        AssertEqual(CaptureVerdict.Queue, Verdict(c, default(ActiveOrderView), out _), "a call is captured");
        c.Delivering = true;
        AssertEqual(CaptureVerdict.Pass, Verdict(c, default(ActiveOrderView), out string reason), "Whiskey's own delivery call reaches vanilla");
        AssertEqual("delivering", reason, "reason");
    }

    private static void NestedRelayCapturedUnlessDelivering()
    {
        // vanilla's type-11 relay (8333-8336) re-enters CheckCurrentOrderUpdate on its own account.
        CaptureInput relay = Capturable(type: 11);
        AssertEqual(CaptureVerdict.Queue, Verdict(relay, default(ActiveOrderView), out _), "a relay call from vanilla's own AI is a new order to the player");
        relay.Delivering = true;
        AssertEqual(CaptureVerdict.Pass, Verdict(relay, default(ActiveOrderView), out _), "a relay inside Whiskey's delivery is not captured twice");
        // A type 11 onto an active 11 is vanilla's in-place refresh and is never emptied or queued.
        CaptureInput refresh = Capturable(type: 11);
        AssertEqual(CaptureVerdict.Pass, Verdict(refresh, new ActiveOrderView(true, 11, 0, 0, 0, 0f, 1.0, 10.0, false, false, false), out string reason), "reserve refresh");
        AssertEqual("reserve-refresh", reason, "reason");
    }

    private static void CaptureOffWhenBothFlagsOff()
    {
        CaptureInput c = Capturable();
        c.ReadmitOn = false;
        AssertEqual(CaptureVerdict.Queue, Verdict(c, default(ActiveOrderView), out _), "couriers alone capture");
        c.CouriersOn = false;
        c.ReadmitOn = true;
        AssertEqual(CaptureVerdict.Queue, Verdict(c, default(ActiveOrderView), out _), "re-admit alone captures (D2)");
        c.ReadmitOn = false;
        AssertEqual(CaptureVerdict.Pass, Verdict(c, default(ActiveOrderView), out string reason), "both off: everything reaches vanilla");
        AssertEqual("flags-off", reason, "reason");
        c = Capturable();
        c.Enabled = false;
        AssertEqual(CaptureVerdict.Pass, Verdict(c, default(ActiveOrderView), out _), "master switch");
    }

    private static void CicPassesEverything()
    {
        CaptureInput c = Capturable();
        c.Facts.PlayerIsCic = true;
        AssertEqual(CaptureVerdict.Pass, Verdict(c, default(ActiveOrderView), out string reason), "a CIC player: Whiskey stands down");
        AssertEqual("cic", reason, "reason");
    }

    private static void ModeNotActivePassesBattleCalls()
    {
        CaptureInput c = Capturable();
        c.ModeAllowsWrites = false;
        AssertEqual(CaptureVerdict.Pass, Verdict(c, default(ActiveOrderView), out string reason), "MonitorOnly/Off: no battle capture");
        AssertEqual("mode", reason, "reason");
    }

    private static void RoutedDropsVanillaThirteenAndFourteen()
    {
        foreach (int type in new[] { 13, 14 })
        {
            CaptureInput c = Capturable(type: type);
            AssertEqual(CaptureVerdict.Pass, Verdict(c, default(ActiveOrderView), out string reason), "type " + type + " reaches vanilla while the command is not routed");
            AssertEqual("vanilla-transition", reason, "reason");
            c.PlayerRouted = true;
            AssertEqual(CaptureVerdict.Drop, Verdict(c, default(ActiveOrderView), out reason), "type " + type + " dropped while routed (the released hold stays)");
            AssertEqual("routed", reason, "reason");
            c.ReadmitOn = false;
            AssertEqual(CaptureVerdict.Pass, Verdict(c, default(ActiveOrderView), out _), "type " + type + ": re-admit off is vanilla");
        }
        // Routed does not touch other orders.
        CaptureInput other = Capturable(type: 2);
        other.PlayerRouted = true;
        AssertEqual(CaptureVerdict.Queue, Verdict(other, default(ActiveOrderView), out _), "a support order is still couriered");
    }

    private static void CouriersOffDeliversNextPass()
    {
        // With couriers off the order is due the moment it is captured, whatever the delay.
        AssertTrue(Math.Abs(10.0 - WlOrderFlow.DueAt(issuedAt: 10.0, delayHours: 0.4, couriersOn: false, deferUntil: double.NaN)) < 1e-12, "due = issued");
        AssertTrue(Math.Abs(10.4 - WlOrderFlow.DueAt(10.0, 0.4, true, double.NaN)) < 1e-12, "due = issued + delay");
        AssertTrue(Math.Abs(12.0 - WlOrderFlow.DueAt(10.0, 0.4, true, 12.0)) < 1e-12, "a deferral can push it later");
        AssertTrue(Math.Abs(12.0 - WlOrderFlow.DueAt(10.0, 0.4, false, 12.0)) < 1e-12, "even with couriers off");
        AssertTrue(Math.Abs(10.4 - WlOrderFlow.DueAt(10.0, 0.4, true, 5.0)) < 1e-12, "a deferral never pulls it earlier");
        var host = new FakeHost();
        var flow = NewFlow(host);
        Put(flow, 2, 10.0, WlOrderFlow.DueAt(10.0, 0.4, false, double.NaN));
        flow.RunPass();
        AssertEqual(1, host.Calls, "delivered by the very next pass");
    }

    private static void CaptureDropsAgainstTheSlotAndNeverQueuesTheDrop()
    {
        CaptureInput c = Capturable(type: 3);
        ActiveOrderView engage = new ActiveOrderView(true, 13, 0, 0, 0, 0f, 99.0, 10.0, false, false, false);
        AssertEqual(CaptureVerdict.Drop, WlOrderFlow.DecideCapture(in c, in engage, out _, out string reason), "policy drop");
        AssertEqual("engage-at-will", reason, "reason");
        // Readmit off: vanilla's dedupe only.
        c.ReadmitOn = false;
        ActiveOrderView sameType = new ActiveOrderView(true, 3, 0, 0, 0, 0f, 99.0, 10.0, false, false, false);
        AssertEqual(CaptureVerdict.Drop, WlOrderFlow.DecideCapture(in c, in sameType, out _, out reason), "vanilla would drop it");
        AssertEqual("vanilla-drops", reason, "reason");
        // A defer carries its time.
        c = Capturable(type: 2, x: 5000f);
        ActiveOrderView young = new ActiveOrderView(true, 2, 0, 0, 0, 0f, 0.05, 10.0, false, false, false);
        AssertEqual(CaptureVerdict.Queue, WlOrderFlow.DecideCapture(in c, in young, out double until, out reason), "deferred is queued");
        AssertTrue(Math.Abs(until - (10.0 - 0.05 + PlayerOrderReadmitPolicy.MinHoldBattleHours)) < 1e-9, "deferUntil carried");
    }

    private static void DroppedCapturesCannotEvictRealEntries()
    {
        // Seven real entries ride; vanilla's continuous callers re-send an order the policy drops on every
        // pass. None of the re-sends may take a place in the eight-entry queue.
        var host = new FakeHost { Slot = new FakeOrder { Type = 13 }, Now = 10.0 };
        var flow = NewFlow(host);
        for (int i = 0; i < 7; i++) Put(flow, 1 + i % 4, 10.0, 99.0, unit: 1 + i, x: 1000f * (i + 1), origin: OrderOrigin.Vanilla, issuer: 100 + i);
        AssertEqual(7, flow.Queue.Count, "seven real entries");
        ActiveOrderView engage = new ActiveOrderView(true, 13, 0, 0, 0, 0f, 99.0, 10.0, false, false, false);
        for (int i = 0; i < 40; i++)
        {
            CaptureInput c = Capturable(type: 2 + i % 3, x: 3000f + i * 500f);
            CaptureVerdict v = WlOrderFlow.DecideCapture(in c, in engage, out _, out _);
            AssertEqual(CaptureVerdict.Drop, v, "re-send " + i + " is dropped at capture");
        }
        AssertEqual(7, flow.Queue.Count, "the queue holds only the real entries");
        AssertEqual(0, host.Rows.FindAll(r => r.Contains("courier-overflow")).Count, "nothing was evicted");
    }

    // ------------------------------------------------------------- order age

    private static void OrderAgeTrackerStampsSessions()
    {
        var age = new OrderAgeTracker();
        age.Observe(session: 4, nowHours: 10.0);
        AssertTrue(double.IsPositiveInfinity(age.AgeHours(10.5)), "the order present when first seen is of unknown age");
        age.Observe(4, 11.0);
        AssertTrue(double.IsPositiveInfinity(age.AgeHours(11.0)), "same session, still unknown");
        age.Observe(5, 12.0);
        AssertEqual(0.5, age.AgeHours(12.5), "a new session is stamped when first seen");
        age.Accepted(6, 13.0);
        age.Observe(6, 13.5);
        AssertEqual(1.0, age.AgeHours(14.0), "an accepted delivery is stamped at delivery, not at the next look");
        age.Reset();
        age.Observe(6, 20.0);
        AssertTrue(double.IsPositiveInfinity(age.AgeHours(21.0)), "a reset forgets (battle boundary)");
    }
}
