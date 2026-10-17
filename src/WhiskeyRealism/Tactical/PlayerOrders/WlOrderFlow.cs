using System;

namespace WhiskeyRealism.Tactical.PlayerOrders
{
    // The bypass the capture Prefix honours: set around a delivery transaction's call into vanilla so the
    // delivered order (and the type-11 relay vanilla makes while delivering it) is not captured twice.
    // One flag for both scopes: the battle delivery (WlOrderFlowRuntime) and the campaign delivery
    // (WlCampaignCourierRuntime) run on the main thread, one at a time.
    internal static class WlOrderBypass
    {
        internal static bool Active;
    }

    // What #62 (the player-order doctrine) is told about an entry it queued.
    internal enum P62Outcome
    {
        Accepted,     // vanilla showed it: RecordAccepted
        Attempt,      // refused, dropped by policy, or vanilla threw: RecordAttempt (its throttle applies)
        ClearPending, // structurally gone (superseded, unit gone, cleared...): drop the pending signature only
    }

    internal enum CaptureVerdict
    {
        Pass,  // vanilla runs the call
        Drop,  // the call is skipped and nothing is queued
        Queue, // the call is skipped and its order goes on a courier
    }

    // The order a follow-up replaced, while the follow-up is the order on the panel (D17 (b)). A repeat of
    // that order (same type, within 110 m of its point) would only reopen what the player has reached:
    // vanilla's same-type dedupe no longer sees it behind the hold, and it would open a paying session with
    // the player already in the zone. It is dropped, `follow-up-holds`; anything else follows the normal rules.
    internal readonly struct FollowUpHoldView
    {
        public FollowUpHoldView(bool active, int type, float x, float z)
        {
            Active = active;
            Type = type;
            X = x;
            Z = z;
        }

        public bool Active { get; }
        public int Type { get; }
        public float X { get; }
        public float Z { get; }

        public bool Blocks(int type, float x, float z)
        {
            if (!Active || type != Type) return false;
            float dx = x - X;
            float dz = z - Z;
            return dx * dx + dz * dz <= PlayerOrderReadmitPolicy.ReadmitMeters * PlayerOrderReadmitPolicy.ReadmitMeters;
        }
    }

    // Where the vanilla move of the unit an order names stands (WlMoveState.Classify).
    internal enum MoveState
    {
        Executed, // no move of that unit is queued any more: its superior's order has been carried out
        InFlight, // the move is still in its superior's order queue (courier riding or processing time not reached)
        Lost,     // the move is still queued but its courier is gone: vanilla will clear the waypoints, not carry it out
    }

    // What vanilla's own order queue says about a unit's move (decompile build 14703111). SetWaypoint with
    // an order delay does not give the unit its path at once: it queues the order on the unit's scene
    // parent (91461-91504) and the path waits for the courier (AddOrderCourierline 125009-125171). A move
    // entry (ordertype 0 append, 1 new path) stays in that queue until ProcessOrders carries it out and
    // deletes it (125425, DeleteOrderQueue 124844); carrying it out transmits every path the unit holds at
    // that time (SetOrderStatus 125484-125508), so a path added while an entry is pending rides that entry,
    // new or older. A mounted courier line (type 1) whose courier is gone is vanilla's own test for a lost
    // order (125281-125286, 125308-125320): it clears the untransmitted waypoints, so the move will not be
    // made. The unit's orderstate says nothing about one order: it is shared by every order to the unit,
    // and carrying out an entry resets it to 0 (124849, 125506).
    internal static class WlMoveState
    {
        public static MoveState Classify(bool moveQueued, bool courierLost)
        {
            if (!moveQueued) return MoveState.Executed;
            return courierLost ? MoveState.Lost : MoveState.InFlight;
        }

        // Does a queue entry of the carrier unit carry the order? A move entry (ordertype 0 or 1) whose
        // processing time is not before the carrier's: the entry the dispatch recorded, or a later one that
        // replaced it. An older entry of the same unit (earlier processing time) is another move.
        public static bool Carries(int orderType, float entryProcessingTime, float carrierProcessingTime)
        {
            return orderType >= 0 && orderType <= 1 && entryProcessingTime >= carrierProcessingTime;
        }
    }

    // The queued move that carries an executor dispatch: among the units one dispatch moved (the ordered
    // group, or the children a fan-out around the player's command ordered), the one whose entry is carried
    // out last. The player is told one leg after that entry; the others are carried out earlier or with it.
    internal struct WlMoveCarrier
    {
        public int UnitId;
        public float ProcessingTime;

        public bool Has => UnitId != 0;

        public void Consider(int unitId, float processingTime)
        {
            if (unitId == 0) return;
            if (UnitId == 0 || processingTime > ProcessingTime)
            {
                UnitId = unitId;
                ProcessingTime = processingTime;
            }
        }
    }

    // One CheckCurrentOrderUpdate call, as the capture Prefix sees it.
    internal struct CaptureInput
    {
        public CaptureFacts Facts;
        public FollowUpHoldView Hold;
        public bool Enabled;
        public bool ReadmitOn;
        public bool CouriersOn;
        public bool ModeAllowsWrites;
        public bool Delivering;
        // N1: the player's command is routed (D6), so vanilla's 13 and 14 are dropped.
        public bool PlayerRouted;
        public OrderCandidateView Candidate;
    }

    // Everything the delivery transaction touches in the game, so the pass is testable apart from
    // Unity. All members run on the main thread.
    internal interface IWlOrderHost
    {
        double NowHours { get; }
        bool EodCycle { get; }
        bool ReadmitOn { get; }
        // DLC_WL.givenorder by reference, its type (-1 when empty) and GivenOrders.givenorderssession.
        object SlotObject { get; }
        int SlotType { get; }
        int Session { get; }
        ActiveOrderView ReadActive();
        bool UnitGone(in CourierEntry e);
        // Where the move of the entry's unit stands in vanilla's order queue; asked only of an entry
        // captured with AwaitsMove.
        MoveState MoveOf(in CourierEntry e);
        bool ReleaseStillDue(in CourierEntry e);
        void SetDelivering(bool on);
        void ClearSlot();
        void RestoreSlot(object saved);
        // Vanilla's CheckCurrentOrderUpdate with the entry's original arguments.
        void CallVanilla(in CourierEntry e);
        void VanillaThrew(in CourierEntry e, Exception ex);
        // Vanilla showed the order: text, age stamp, prestige stamp.
        void Accepted(in CourierEntry e);
        void Row(in CourierEntry e, string action, string reason);
        void NotifyP62(in CourierEntry e, P62Outcome outcome);
        // A retreat the player should have (D8a step 0), or false when the host's conditions fail.
        bool TryBuildRetreat(out CourierEntry entry);
        // The hold that releases engage-at-will (D5, D6), or false while contact lasts or the hysteresis
        // has not passed. Asked only while the slot holds a type 13.
        bool TryBuildRelease(out CourierEntry entry);
        // The hold after an attack order has been reached and held (D17 (b)), or false while the
        // conditions fail. Asked only while the slot holds a type 1-4 and nothing is pending.
        bool TryBuildFollowUp(out CourierEntry entry);
        // The follow-up is still wanted when its courier arrives: the same reached order is on the panel.
        bool FollowUpStillDue(in CourierEntry e);
        // D21: vanilla showed an order that pays no prestige (an automatic hold). The host keeps the
        // top units' lastcombatzonecount at that session so vanilla's first-entry branch never fires.
        void MarkNonPayingSession(int session);
        // Called once at the start of every pass: re-asserts the stamp while a non-paying session is active.
        void RestampNonPayingSession();
    }

    // When engage-at-will's contact test has been false (D5): the start of the current quiet spell on the
    // doctrine clock, NaN while there is contact or none has begun.
    internal sealed class ReleaseLatch
    {
        public double QuietSince { get; private set; } = double.NaN;

        // contact: a Visible enemy in fire range, or vanilla's own 13 trigger. Returns QuietSince.
        public double Update(bool contact, double nowHours)
        {
            if (contact) QuietSince = double.NaN;
            else if (double.IsNaN(QuietSince)) QuietSince = nowHours;
            return QuietSince;
        }

        public void Reset()
        {
            QuietSince = double.NaN;
        }
    }

    // When the order on the panel was accepted, on the doctrine clock (D4's age). The order present
    // when the battle was first looked at (or after a load) has no known age: +infinity, which the
    // policy treats as past the hold.
    internal sealed class OrderAgeTracker
    {
        private bool _seen;
        private int _session;
        private double _since;

        public void Observe(int session, double nowHours)
        {
            if (!_seen)
            {
                _seen = true;
                _session = session;
                _since = double.NegativeInfinity;
                return;
            }
            if (session == _session) return;
            _session = session;
            _since = nowHours;
        }

        // An order Whiskey delivered: stamped at delivery.
        public void Accepted(int session, double nowHours)
        {
            _seen = true;
            _session = session;
            _since = nowHours;
        }

        public double AgeHours(double nowHours)
        {
            return double.IsNegativeInfinity(_since) ? double.PositiveInfinity : nowHours - _since;
        }

        public void Reset()
        {
            _seen = false;
            _session = 0;
            _since = double.NegativeInfinity;
        }
    }

    // The W&L order flow, pure (D1-D8a): the capture decision, and the delivery pass that takes due
    // couriers off the queue and gives each to vanilla through the host. Vanilla's
    // CheckCurrentOrderUpdate (decompile 8233-8667) is the only thing that shows an order; this class
    // decides when it is called and what to do with the order slot around the call.
    internal sealed class WlOrderFlow : ICourierDiscardSink
    {
        private readonly IWlOrderHost _host;
        private readonly CourierEntry[] _buffer = new CourierEntry[WlCourierQueue.MaxPending];
        private readonly CourierEntry[] _back = new CourierEntry[WlCourierQueue.MaxPending];
        private string _battleIdentity;
        private string _commandKey;
        private bool _wasCic;
        private bool _hasBoundary;
        // The follow-up in force: the order it replaced (type, point) and the session it opened.
        private int _holdSession = int.MinValue;
        private int _holdType;
        private float _holdX, _holdZ;
        private int _replacedType;
        private float _replacedX, _replacedZ;

        public const string FollowUpHolds = "follow-up-holds";

        public WlOrderFlow(IWlOrderHost host)
        {
            _host = host;
            Queue = new WlCourierQueue(this);
        }

        public WlCourierQueue Queue { get; }

        // The replaced order while the follow-up is still the order on the panel; inactive once anything
        // else has been shown (a new session) or the slot is no longer a 12.
        public FollowUpHoldView Hold()
        {
            if (_holdSession == int.MinValue || _holdSession != _host.Session || _host.SlotType != 12) return default(FollowUpHoldView);
            return new FollowUpHoldView(true, _holdType, _holdX, _holdZ);
        }

        public void ResetHold()
        {
            _holdSession = int.MinValue;
        }

        // Reasons a discard is structural: the order is gone for a reason that says nothing about the
        // player's own panel logic (#62 clears its pending signature and no more).
        internal static bool IsStructural(string reason)
        {
            switch (reason)
            {
                case WlCourierQueue.Superseded:
                case WlCourierQueue.Overflow:
                case "unit-gone":
                case "cross-scope":
                case "release-stale":
                case "relay-took-slot":
                case "eod-clear":
                case "command-change":
                case "battle-end":
                    return true;
                default:
                    return false;
            }
        }

        // The queue's own discards (superseded, overflow, clear) and the pass's: one row each.
        public void Discarded(in CourierEntry e, string reason)
        {
            _host.Row(in e, "discard", reason);
            if (e.Origin == OrderOrigin.P62)
                _host.NotifyP62(in e, IsStructural(reason) ? P62Outcome.ClearPending : P62Outcome.Attempt);
        }

        // The automatic holds (the released engage-at-will, the arrival follow-up) show on the panel but pay
        // no prestige (user decision, crit round 1); every other origin pays as vanilla does.
        public static bool IsNonPaying(OrderOrigin origin)
        {
            return origin == OrderOrigin.Release || origin == OrderOrigin.FollowUp;
        }

        public const string MoveInFlight = "move-in-flight";
        public const string MoveLost = "move-lost";

        public static double DueAt(double issuedAt, double delayHours, bool couriersOn, double deferUntil)
        {
            double due = couriersOn ? issuedAt + delayHours : issuedAt;
            if (!double.IsNaN(deferUntil) && deferUntil > due) due = deferUntil;
            return due;
        }

        // The reasons a call reaches vanilla untouched that need only the switches and the CIC and
        // campaign facts, in the order they are tested. False means the capture continues.
        public static bool PassesUntouched(in CaptureInput c, out string reason)
        {
            if (!c.Enabled) { reason = "disabled"; return true; }
            if (!c.ReadmitOn && !c.CouriersOn) { reason = "flags-off"; return true; }
            if (c.Delivering) { reason = "delivering"; return true; }
            if (c.Facts.PlayerIsCic) { reason = "cic"; return true; }
            if (c.Facts.CampaignCall) { reason = "campaign"; return true; }
            if (!c.ModeAllowsWrites) { reason = "mode"; return true; }
            reason = null;
            return false;
        }

        // Capture: does this call go on a courier, get dropped, or reach vanilla untouched?
        // A call vanilla's own rules would drop, and the policy drops too, is dropped here and never queued:
        // vanilla's continuous callers repeat it on every pass, and queued copies would crowd real entries
        // out of the eight-entry queue.
        public static CaptureVerdict DecideCapture(in CaptureInput c, in ActiveOrderView active, out double deferUntil, out string reason)
        {
            deferUntil = double.NaN;
            if (PassesUntouched(in c, out reason)) return CaptureVerdict.Pass;
            if (!WlOrderCaptureGate.ReachesDedupe(in c.Facts)) { reason = "gate"; return CaptureVerdict.Pass; }

            int type = c.Candidate.Type;
            if (type == 13 || type == 14)
            {
                // The player's own contact state (6800-6809): never couriered. A routed command
                // keeps the released hold (N1), so vanilla's 13/14 are dropped while it is routed.
                if (c.ReadmitOn && c.PlayerRouted) { reason = "routed"; return CaptureVerdict.Drop; }
                reason = "vanilla-transition";
                return CaptureVerdict.Pass;
            }
            if (type == 11 && active.HasOrder && active.Type == 11) { reason = "reserve-refresh"; return CaptureVerdict.Pass; }

            if (c.Hold.Blocks(type, c.Candidate.X, c.Candidate.Z)) { reason = FollowUpHolds; return CaptureVerdict.Drop; }

            ReadmitAction action;
            if (c.ReadmitOn)
            {
                action = PlayerOrderReadmitPolicy.Decide(in active, in c.Candidate, out deferUntil, out reason);
            }
            else if (PlayerOrderReadmitPolicy.PredictVanilla(in active, in c.Candidate))
            {
                action = ReadmitAction.Drop;
                reason = "vanilla-drops";
            }
            else
            {
                action = ReadmitAction.Admit;
                reason = "vanilla-accepts";
            }

            if (action == ReadmitAction.Drop) return CaptureVerdict.Drop;
            return CaptureVerdict.Queue;
        }

        // Enqueues a captured entry; a capture drop (rate-limited, en-route, overflow) writes its row.
        public bool Enqueue(in CourierEntry e, double holdWindow)
        {
            if (Queue.TryEnqueue(in e, holdWindow, out string drop))
            {
                _host.Row(in e, "capture", "queued");
                return true;
            }
            _host.Row(in e, "drop", drop);
            return false;
        }

        // The player's battle, command or CIC status changed: orders queued for the old one are void.
        public void NoteBoundary(string battleIdentity, string commandKey, bool isCic)
        {
            if (!_hasBoundary)
            {
                _hasBoundary = true;
            }
            else if (!string.Equals(_battleIdentity, battleIdentity, StringComparison.Ordinal)
                || !string.Equals(_commandKey, commandKey, StringComparison.Ordinal)
                || _wasCic != isCic)
            {
                Queue.Clear("command-change");
            }
            _battleIdentity = battleIdentity;
            _commandKey = commandKey;
            _wasCic = isCic;
        }

        // One delivery pass (D8a).
        public void RunPass()
        {
            // The end-of-day cycle clears the order (47985) and nothing is delivered meanwhile.
            if (_host.EodCycle)
            {
                Queue.Clear("eod-clear");
                return;
            }

            _host.RestampNonPayingSession();
            DeriveRetreat();
            DeriveRelease();
            DeriveFollowUp();

            // 1. Everything due goes into a buffer first; deferred and requeued entries return only
            //    after the pass, so the pass never takes one twice.
            double now = _host.NowHours;
            int taken = 0;
            while (taken < _buffer.Length && Queue.TryTakeDue(now, out CourierEntry due, out bool superseded))
            {
                if (superseded) continue; // reported by the queue
                _buffer[taken++] = due;
            }

            int back = 0;
            for (int i = 0; i < taken; i++)
            {
                CourierEntry e = _buffer[i];
                _buffer[i] = default(CourierEntry);
                if (!Process(ref e, out bool requeue)) continue;
                if (requeue && back < _back.Length) _back[back++] = e;
            }
            for (int i = 0; i < back; i++)
            {
                Queue.Requeue(in _back[i]);
                _back[i] = default(CourierEntry);
            }
        }

        // D8a step 0. Vanilla's retreat reaches a player only through a call that needs a non-null
        // slot (6788), so after a load, at battle start or after the end-of-day clear it never comes;
        // the host reports the entry when the army is retreating and the conditions hold.
        private void DeriveRetreat()
        {
            if (_host.SlotType == 15 || Queue.HasPending(15)) return;
            if (!_host.TryBuildRetreat(out CourierEntry entry)) return;
            Enqueue(in entry, 0.0);
        }

        // D5: engage-at-will is released into a hold at the player's position once contact has ended
        // (or at once while his command is routed); one release at a time.
        private void DeriveRelease()
        {
            if (_host.SlotType != 13 || Queue.HasPendingOrigin(OrderOrigin.Release)) return;
            if (!_host.TryBuildRelease(out CourierEntry entry)) return;
            Enqueue(in entry, 0.0);
        }

        // D17 (b): a reached attack order is followed by a hold at the zone; nothing newer may be pending.
        private void DeriveFollowUp()
        {
            int type = _host.SlotType;
            if (type < 1 || type > 4 || Queue.Count > 0) return;
            if (!_host.TryBuildFollowUp(out CourierEntry entry)) return;
            Enqueue(in entry, 0.0);
        }

        // Steps 2-6 for one buffered entry. Returns false when the entry is finished (and reported);
        // true when it must be considered again (requeue).
        private bool Process(ref CourierEntry e, out bool requeue)
        {
            requeue = false;
            if (Queue.IsSuperseded(in e)) { Queue.Discard(in e, WlCourierQueue.Superseded); return false; }
            if (_host.UnitGone(in e)) { Queue.Discard(in e, "unit-gone"); return false; }
            if (e.AwaitsMove && !ReleaseOnMove(ref e, out requeue)) return requeue;
            if (IsCrossScope(_host.SlotType)) { Queue.Discard(in e, "cross-scope"); return false; }
            if (e.Origin == OrderOrigin.Release && (_host.SlotType != 13 || !_host.ReleaseStillDue(in e)))
            {
                Queue.Discard(in e, "release-stale");
                return false;
            }

            if (e.Origin == OrderOrigin.FollowUp && !_host.FollowUpStillDue(in e))
            {
                Queue.Discard(in e, "follow-up-stale");
                return false;
            }
            if (Hold().Blocks(e.Type, e.X, e.Z))
            {
                Queue.Discard(in e, FollowUpHolds);
                return false;
            }

            object token = _host.SlotObject;
            ActiveOrderView active = _host.ReadActive();
            if (e.Origin == OrderOrigin.FollowUp)
            {
                _replacedType = active.Type;
                _replacedX = active.X;
                _replacedZ = active.Z;
            }
            var candidate = new OrderCandidateView(e.Type, e.X, e.Y, e.Z, e.Rotation, e.Origin, e.CampaignCall);
            ReadmitAction action;
            string reason;
            double deferUntil = double.NaN;
            if (_host.ReadmitOn)
            {
                action = PlayerOrderReadmitPolicy.Decide(in active, in candidate, out deferUntil, out reason);
            }
            else if (PlayerOrderReadmitPolicy.PredictVanilla(in active, in candidate))
            {
                action = ReadmitAction.Drop;
                reason = "vanilla-drops";
            }
            else
            {
                action = ReadmitAction.Admit;
                reason = "vanilla-accepts";
            }

            switch (action)
            {
                case ReadmitAction.Defer:
                    e.DueAt = deferUntil;
                    _host.Row(in e, "defer", reason);
                    requeue = true;
                    return true;
                case ReadmitAction.Drop:
                    Queue.Discard(in e, reason);
                    return false;
            }

            // The shared-row rule: read the slot again right before writing; if it is not the object
            // the decision was made on, write nothing and look at the entry again next pass.
            if (!ReferenceEquals(_host.SlotObject, token))
            {
                _host.Row(in e, "requeue", "slot-changed");
                requeue = true;
                return true;
            }
            return Deliver(ref e, token, clear: action == ReadmitAction.AdmitWithClear, out requeue);
        }

        // The move gate (WlMoveCarrier). True when the move has been carried out and the entry's leg has
        // passed, so the pass goes on with it. False when the entry is finished (a lost move: reported) or
        // must wait: requeue says so, and an entry still waiting for its move or for its leg goes back.
        private bool ReleaseOnMove(ref CourierEntry e, out bool requeue)
        {
            requeue = false;
            switch (_host.MoveOf(in e))
            {
                case MoveState.Lost:
                    Queue.Discard(in e, MoveLost);
                    return false;
                case MoveState.InFlight:
                    if (!e.MoveHeld)
                    {
                        e.MoveHeld = true;
                        _host.Row(in e, "defer", MoveInFlight);
                    }
                    requeue = true;
                    return false;
            }

            // The superior has the move: the bugle from him to the player starts now. The original due time
            // stays when it is later (a policy deferral).
            double now = _host.NowHours;
            e.AwaitsMove = false;
            e.DueAt = Math.Max(e.DueAt, now + e.LegHours);
            if (e.DueAt <= now) return true;
            requeue = true;
            return false;
        }

        private bool Deliver(ref CourierEntry e, object saved, bool clear, out bool requeue)
        {
            requeue = false;
            if (clear) _host.ClearSlot();
            int sessionBefore = _host.Session;
            object slotBefore = clear ? null : saved;
            bool threw = false;
            bool foreignAfterClear = false;
            try
            {
                try
                {
                    _host.SetDelivering(true);
                    _host.CallVanilla(in e);
                }
                finally
                {
                    // Whatever vanilla did (including throwing): an order Whiskey emptied the slot for
                    // and vanilla refused is put back, and capture is on again.
                    if (clear && _host.Session == sessionBefore)
                    {
                        if (_host.SlotObject == null) _host.RestoreSlot(saved);
                        else foreignAfterClear = true;
                    }
                    _host.SetDelivering(false);
                }
            }
            catch (Exception ex)
            {
                threw = true;
                _host.VanillaThrew(in e, ex);
            }

            if (threw)
            {
                Queue.Discard(in e, "vanilla-threw");
                return false;
            }

            int sessionAfter = _host.Session;
            int typeAfter = _host.SlotType;
            if (sessionAfter != sessionBefore)
            {
                if (typeAfter == e.Type)
                {
                    Queue.MarkAccepted(e.IssuedAt);
                    _host.Accepted(in e);
                    _host.Row(in e, "deliver", "accepted");
                    if (e.Origin == OrderOrigin.P62) _host.NotifyP62(in e, P62Outcome.Accepted);
                    if (IsNonPaying(e.Origin)) _host.MarkNonPayingSession(sessionAfter);
                    if (e.Origin == OrderOrigin.FollowUp)
                    {
                        _holdSession = sessionAfter;
                        _holdType = _replacedType;
                        _holdX = _replacedX;
                        _holdZ = _replacedZ;
                    }
                    return false;
                }
                // Vanilla's nested type-11 relay (8333-8336) took the slot; its order stays.
                Queue.Discard(in e, "relay-took-slot");
                return false;
            }

            if (e.Type == 11 && typeAfter == 11 && !ReferenceEquals(_host.SlotObject, slotBefore))
            {
                // Vanilla's in-place refresh of an active reserve order (flag4): not an acceptance.
                _host.Row(in e, "deliver", "refreshed");
                if (e.Origin == OrderOrigin.P62) _host.NotifyP62(in e, P62Outcome.ClearPending);
                return false;
            }

            if (foreignAfterClear)
            {
                // Something else wrote the slot while ours was empty: leave it and look again.
                _host.Row(in e, "requeue", "slot-changed");
                requeue = true;
                return true;
            }

            Queue.Discard(in e, "vanilla-refused");
            return false;
        }

        // The slot holds a campaign order (the scene load clears it at frame 2): a battle entry
        // never delivers over it. Whiskey's battle set is 0-4 and 11-15; vanilla's IsBattleOrder
        // leaves out 13-15.
        private static bool IsCrossScope(int slotType)
        {
            if (slotType < 0) return false;
            return !((slotType >= 0 && slotType <= 4) || (slotType >= 11 && slotType <= 15));
        }
    }
}
