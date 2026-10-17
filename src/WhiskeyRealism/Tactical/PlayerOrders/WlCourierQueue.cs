using System;

namespace WhiskeyRealism.Tactical.PlayerOrders
{
    // One order on its way to the W&L player's panel: the original CheckCurrentOrderUpdate
    // arguments, who sent it, and when it was issued and is due. Unit references are not held
    // (an entry resolves its unit by instance id at delivery, so nothing is pinned across frames).
    internal struct CourierEntry
    {
        public int UnitId;
        public int Type;
        public float X, Y, Z;
        public string Name;
        public float Rotation, Width, Depth;
        public bool CampaignCall;
        public OrderOrigin Origin;
        // The issuing headquarters' GameObject instance id (the snapshot's InstanceId convention).
        public int IssuerKey;
        public double IssuedAt, DueAt;
        public long EnqueueSeq;
        // Diagnostics for the flow row: the courier path the delay was computed over.
        public int Legs;
        public float Meters;
        // Origin == P62 only: the candidate #62 composed, for its accept/attempt bookkeeping.
        public PlayerOrderCandidate P62Candidate;
        // What the order is for, for the condition lines under vanilla's text (Whiskey origins).
        public OrderIntentKind Kind;
        // Campaign entries (CampaignCall): the issuing unit by GameObject name and its commander id (an
        // instance id does not survive a save), and the commander shown as issuer.
        public string UnitName;
        public int UnitCommanderId;
        public int IssuerCommanderId;
        // Campaign entries: the player's command the order was queued for, as a key that survives a reload (the
        // command unit's GameObject name and the chosen commander id); null = not known (an old sidecar). An
        // order never rides on to a different command.
        public string PlayerCommandKey;
    }

    // Told about every entry the queue discards by itself: superseded, overflow, cleared. A capture
    // drop (rate-limited, en-route) is returned to the caller instead, so nothing is reported twice
    // and nothing is dropped silently.
    internal interface ICourierDiscardSink
    {
        void Discarded(in CourierEntry e, string reason);
    }

    // The courier queue (D8): one per scope, a fixed pool of MaxPending entries, no allocation after
    // construction. Battle times are doctrine hours, campaign times campaign years.
    internal sealed class WlCourierQueue
    {
        public const int MaxPending = 8;
        public const string RateLimited = "rate-limited";
        public const string EnRoute = "en-route";
        public const string Overflow = "courier-overflow";
        public const string Superseded = "superseded";
        // Vanilla's own order dedupe radius (8643); the en-route and campaign target radius.
        public const float SamePointMeters = 110f;
        private const int Retreat = 15;
        private const int HistoryCapacity = 16;

        private struct Sent
        {
            public int IssuerKey, Type;
            public OrderOrigin Origin;
            public bool Campaign;
            public float X, Z;
            public double At;
        }

        private readonly ICourierDiscardSink _sink;
        private readonly CourierEntry[] _pending = new CourierEntry[MaxPending];
        private readonly Sent[] _sent = new Sent[HistoryCapacity];
        private int _count;
        private int _sentCount;
        private int _sentNext;
        private long _nextSeq;
        private double _highestAccepted = double.NegativeInfinity;

        public WlCourierQueue(ICourierDiscardSink sink)
        {
            _sink = sink;
        }

        public int Count => _count;

        // The entry's IssuedAt is "now". Returns false with a drop reason when (a) the same issuer
        // already sent this type from this origin inside the hold window (a retreat is exempt; a
        // campaign key also includes the target), (b) an entry of the same type is already riding
        // to within 110 m, or (e) the queue is full and the incoming entry is the one to go.
        public bool TryEnqueue(in CourierEntry e, double holdWindow, out string dropReason)
        {
            dropReason = null;
            if (e.Type != Retreat && SentWithin(in e, holdWindow))
            {
                dropReason = RateLimited;
                return false;
            }
            if (RidingTo(in e))
            {
                dropReason = EnRoute;
                return false;
            }

            CourierEntry stored = e;
            stored.EnqueueSeq = _nextSeq;
            if (_count == MaxPending && EvictFor(in stored))
            {
                dropReason = Overflow;
                return false;
            }
            _nextSeq++;
            _pending[_count++] = stored;
            RecordSent(in stored);
            return true;
        }

        // An entry the delivery pass drops after it has left the queue (its own checks), reported
        // through the sink like every other discard.
        public void Discard(in CourierEntry e, string reason)
        {
            _sink?.Discarded(in e, reason);
        }

        // True when an entry from this origin is waiting.
        public bool HasPendingOrigin(OrderOrigin origin)
        {
            for (int i = 0; i < _count; i++)
                if (_pending[i].Origin == origin) return true;
            return false;
        }

        // True when an entry of this type is waiting.
        public bool HasPending(int type)
        {
            for (int i = 0; i < _count; i++)
                if (_pending[i].Type == type) return true;
            return false;
        }

        // The earliest-due entry that is due (ties by enqueue order), removed from the queue.
        // `superseded` is set when it was issued strictly before the last accepted delivery and
        // is not a retreat: a later order from the chain countermands it, and it is reported to the
        // sink and must be discarded by the caller.
        public bool TryTakeDue(double now, out CourierEntry e, out bool superseded)
        {
            int best = -1;
            for (int i = 0; i < _count; i++)
            {
                if (_pending[i].DueAt > now) continue;
                if (best < 0 || _pending[i].DueAt < _pending[best].DueAt
                    || (_pending[i].DueAt == _pending[best].DueAt && _pending[i].EnqueueSeq < _pending[best].EnqueueSeq))
                    best = i;
            }
            if (best < 0)
            {
                e = default(CourierEntry);
                superseded = false;
                return false;
            }
            e = _pending[best];
            RemoveAt(best);
            superseded = IsSuperseded(in e);
            if (superseded) _sink?.Discarded(in e, Superseded);
            return true;
        }

        // Issued strictly before the highest accepted delivery, and not a retreat.
        public bool IsSuperseded(in CourierEntry e)
        {
            return e.Type != Retreat && e.IssuedAt < _highestAccepted;
        }

        // D8 (d): the delivery pass puts an entry back (a deferral, or a changed slot). It bypasses
        // the cadence and en-route tests and keeps its original enqueue order.
        public void Requeue(in CourierEntry e)
        {
            CourierEntry stored = e;
            if (_count == MaxPending && EvictFor(in stored))
            {
                _sink?.Discarded(in stored, Overflow);
                return;
            }
            _pending[_count++] = stored;
        }

        // D11: an entry loaded from the sidecar. It keeps its issue and due times, takes the next
        // enqueue order, bypasses the cadence and en-route tests and leaves no cadence history (that is
        // not persisted). A full queue discards it like any overflow.
        public void Restore(in CourierEntry e)
        {
            CourierEntry stored = e;
            stored.EnqueueSeq = _nextSeq;
            if (_count == MaxPending && EvictFor(in stored))
            {
                _sink?.Discarded(in stored, Overflow);
                return;
            }
            _nextSeq++;
            _pending[_count++] = stored;
        }

        // Gives every pending entry that has no identity the command the player has now (the first delivery
        // pass after an old sidecar was loaded); an entry that has one keeps it.
        public void StampPlayerCommand(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            for (int i = 0; i < _count; i++)
                if (string.IsNullOrEmpty(_pending[i].PlayerCommandKey)) _pending[i].PlayerCommandKey = key;
        }

        // The pending entries, in no particular order, for the sidecar. Returns how many were copied.
        public int CopyPending(CourierEntry[] destination)
        {
            int n = Math.Min(_count, destination.Length);
            for (int i = 0; i < n; i++) destination[i] = _pending[i];
            return n;
        }

        public void MarkAccepted(double issuedAt)
        {
            if (issuedAt > _highestAccepted) _highestAccepted = issuedAt;
        }

        // D8 (f): reports every pending entry, forgets the cadence and acceptance history.
        public void Clear(string reason)
        {
            for (int i = 0; i < _count; i++)
            {
                _sink?.Discarded(in _pending[i], reason);
                _pending[i] = default(CourierEntry);
            }
            _count = 0;
            _sentCount = 0;
            _sentNext = 0;
            _highestAccepted = double.NegativeInfinity;
        }

        private bool SentWithin(in CourierEntry e, double window)
        {
            for (int i = 0; i < _sentCount; i++)
            {
                Sent s = _sent[i];
                if (s.IssuerKey != e.IssuerKey || s.Type != e.Type || s.Origin != e.Origin || s.Campaign != e.CampaignCall) continue;
                double age = e.IssuedAt - s.At;
                if (!(age >= 0.0 && age < window)) continue;
                if (e.CampaignCall && !Near(s.X, s.Z, e.X, e.Z)) continue;
                return true;
            }
            return false;
        }

        private bool RidingTo(in CourierEntry e)
        {
            for (int i = 0; i < _count; i++)
                if (_pending[i].Type == e.Type && Near(_pending[i].X, _pending[i].Z, e.X, e.Z)) return true;
            return false;
        }

        // Flat XZ, 110 inclusive (D8 (b), D11).
        private static bool Near(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return dx * dx + dz * dz <= SamePointMeters * SamePointMeters;
        }

        // The queue is full: drops the entry with the earliest issue time (ties by enqueue order),
        // never a retreat while another type is among the candidates. Returns true when that entry
        // is the incoming one (nothing was evicted; the caller reports it); otherwise the evicted
        // pending entry is reported to the sink and its slot is free.
        private bool EvictFor(in CourierEntry incoming)
        {
            bool anyOther = incoming.Type != Retreat;
            for (int i = 0; i < _count && !anyOther; i++) anyOther = _pending[i].Type != Retreat;

            int victim = -1; // -1: the incoming entry
            CourierEntry best = incoming;
            bool bestEligible = !anyOther || incoming.Type != Retreat;
            for (int i = 0; i < _count; i++)
            {
                bool eligible = !anyOther || _pending[i].Type != Retreat;
                if (!eligible) continue;
                if (!bestEligible || Older(in _pending[i], in best))
                {
                    victim = i;
                    best = _pending[i];
                    bestEligible = true;
                }
            }
            if (victim < 0) return true;
            _sink?.Discarded(in best, Overflow);
            RemoveAt(victim);
            return false;
        }

        private static bool Older(in CourierEntry a, in CourierEntry b)
        {
            return a.IssuedAt < b.IssuedAt || (a.IssuedAt == b.IssuedAt && a.EnqueueSeq < b.EnqueueSeq);
        }

        private void RemoveAt(int index)
        {
            _count--;
            _pending[index] = _pending[_count];
            _pending[_count] = default(CourierEntry);
        }

        private void RecordSent(in CourierEntry e)
        {
            _sent[_sentNext] = new Sent
            {
                IssuerKey = e.IssuerKey,
                Type = e.Type,
                Origin = e.Origin,
                Campaign = e.CampaignCall,
                X = e.X,
                Z = e.Z,
                At = e.IssuedAt,
            };
            _sentNext = (_sentNext + 1) % HistoryCapacity;
            if (_sentCount < HistoryCapacity) _sentCount++;
        }
    }
}
