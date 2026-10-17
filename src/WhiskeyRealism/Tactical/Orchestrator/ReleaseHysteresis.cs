using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// The release and the return of a reserve each need the picture to hold (verify r2: a leaf reserve flipped 20 times in 20 ticks, and the
    /// army-level crisis release is stateless and flips the same way). A reserve is released only after its trigger has held across at least
    /// two observations spanning <see cref="ConfirmHours"/> of the doctrine clock, stays released for at least <see cref="MinHoldHours"/> and
    /// until the trigger has been gone for <see cref="ReturnMarginHours"/>, and is not released again for <see cref="RejoinCooldownHours"/> after
    /// it returned. All on the doctrine clock (battle game hours, frozen overnight), never on real time. One instance per owner (the army, each
    /// holding command's release); keys are the child ids. Allocation free once a key has been seen.
    /// </summary>
    public sealed class ReleaseHysteresis
    {
        /// <summary>Two coordinator cycles (0.015 h each) of the same trigger before a reserve is let go.</summary>
        public const double ConfirmHours = 0.03;
        /// <summary>A released reserve stays out at least this long.</summary>
        public const double MinHoldHours = 0.25;
        /// <summary>And it returns only after the trigger has been absent this long.</summary>
        public const double ReturnMarginHours = 0.05;
        /// <summary>After a return, the next release waits this long.</summary>
        public const double RejoinCooldownHours = 0.10;

        private struct Entry
        {
            public bool HasRaw, Active;
            public int Observations;
            public double RawSince, LastObservedAt, LastRawAt, ReleasedAt, ReturnedAt;
            public DirectChildRole Role;
            public int Sector;
        }

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);

        public int ActiveCount
        {
            get
            {
                int n = 0;
                foreach (var pair in _entries) if (pair.Value.Active) n++;
                return n;
            }
        }

        public void Clear() => _entries.Clear();

        /// <summary>
        /// One observation of <paramref name="key"/>: the raw release the allocator made this call (a role other than Reserve, with the sector it was
        /// sent to) or none. Returns whether the reserve is released now; <paramref name="role"/> and <paramref name="sector"/> are what it holds
        /// (the last raw ones while it stays released after the trigger ended).
        /// </summary>
        public bool Step(string key, bool raw, DirectChildRole rawRole, int rawSector, double now, out DirectChildRole role, out int sector)
        {
            if (!_entries.TryGetValue(key, out Entry e))
            {
                e = new Entry { RawSince = double.NegativeInfinity, LastObservedAt = double.NegativeInfinity, LastRawAt = double.NegativeInfinity,
                    ReleasedAt = double.NegativeInfinity, ReturnedAt = double.NegativeInfinity, Role = DirectChildRole.Reserve, Sector = -1 };
            }
            if (raw)
            {
                if (!e.HasRaw)
                {
                    e.HasRaw = true;
                    e.RawSince = now;
                    e.Observations = 0;
                }
                if (now > e.LastObservedAt + 1e-9) e.Observations++;
                e.LastRawAt = now;
                if (e.Active)
                {
                    e.Role = rawRole;
                    e.Sector = rawSector;
                }
                else if (e.Observations >= 2 && now - e.RawSince >= ConfirmHours && now - e.ReturnedAt >= RejoinCooldownHours)
                {
                    e.Active = true;
                    e.ReleasedAt = now;
                    e.Role = rawRole;
                    e.Sector = rawSector;
                }
            }
            else
            {
                e.HasRaw = false;
                e.Observations = 0;
                if (e.Active && now - e.ReleasedAt >= MinHoldHours && now - e.LastRawAt >= ReturnMarginHours)
                {
                    e.Active = false;
                    e.ReturnedAt = now;
                }
            }
            e.LastObservedAt = now;
            _entries[key] = e;
            role = e.Active ? e.Role : DirectChildRole.Reserve;
            sector = e.Active ? e.Sector : -1;
            return e.Active;
        }
    }
}
