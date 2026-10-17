using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pacing of the reserve-release producer's moves (pure; one instance per side, kept by the producer). Two rules, both on the doctrine clock:
    /// <list type="bullet">
    /// <item>The hold: a release must stand for <see cref="HoldMinutes"/> of doctrine time, in one role, before its move goes out. A release the
    /// allocation takes back inside the hold (or swaps for another role) sends nothing; the allocator's own roles changed every few seconds in the
    /// 2026-10-17 smoke and each change queued a courier order. A crisis release (<see cref="DirectChildRole.Fix"/>) is exempt: the allocator already
    /// confirmed it across two cycles (ReleaseHysteresis), and the line it answers is in contact now. So is a crossing guard (a release with a guard
    /// point): it is timed to the enemy's arrival at the crossing and holds its own binding (ReserveReleaseState.BindCrossing).</item>
    /// <item>The dead-band: no new move while the last one the executor took is still waiting for its courier (the unit's queue is not empty, for at
    /// most <see cref="CourierWaitCapHours"/>), and none to a target within the executor's dedupe radius of the last one. The executor's own duplicate
    /// test reads vanilla's last waypoint, which vanilla's link follow keeps overwriting, so it cannot tell a repeat of this producer's order; the
    /// record here is the producer's own, confirmed against the executor's last issued move (a move the executor skipped is dropped, so it is retried).</item>
    /// </list>
    /// </summary>
    internal sealed class ReserveReleasePacing
    {
        /// <summary>Doctrine minutes a release role must persist before its move goes out.</summary>
        public const double HoldMinutes = 3.0;

        public const double HoldHours = HoldMinutes / 60.0;

        /// <summary>A move still queued this long after it went out no longer holds the next one back (a queue entry of another order, a lost courier).</summary>
        public const double CourierWaitCapHours = 0.25;

        // The executor stamps a move with its own tick's clock, the producer with the snapshot's: the same tick within a few seconds.
        private const double SameTickToleranceHours = 0.001;

        public enum Admission
        {
            /// <summary>Submit the move.</summary>
            Go = 0,
            /// <summary>The last release move is still waiting for its courier.</summary>
            CourierWait = 1,
            /// <summary>The target is within the dedupe radius of the last release target.</summary>
            DeadBand = 2,
        }

        private struct Entry
        {
            public bool Releasing;
            public DirectChildRole Role;
            public double Since;
            public bool HasMove;
            public bool Confirmed;
            public float MoveX, MoveZ;
            public double SubmittedAt;
        }

        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();

        public void Clear() => _entries.Clear();

        /// <summary>
        /// One observation of a group the ledger holds in reserve: whether the allocation released it and in which role. True when the move may go out
        /// (the release has stood for <see cref="HoldHours"/> in this role, or is a crisis release or a crossing guard). Not released: false, and the group's next release
        /// starts its hold afresh.
        /// </summary>
        public bool HoldElapsed(int groupId, bool released, DirectChildRole role, bool guardPoint, double nowHours)
        {
            _entries.TryGetValue(groupId, out Entry e);
            if (!released)
            {
                if (e.Releasing)
                {
                    e.Releasing = false;
                    _entries[groupId] = e;
                }
                return false;
            }
            if (!e.Releasing || e.Role != role)
            {
                e.Releasing = true;
                e.Role = role;
                e.Since = nowHours;
                _entries[groupId] = e;
            }
            return role == DirectChildRole.Fix || guardPoint || nowHours - e.Since >= HoldHours;
        }

        /// <summary>The doctrine hours the group's current release has stood, 0 when it is not being released.</summary>
        public double HeldFor(int groupId, double nowHours)
            => _entries.TryGetValue(groupId, out Entry e) && e.Releasing ? Math.Max(0.0, nowHours - e.Since) : 0.0;

        /// <summary>
        /// Whether a release move to (<paramref name="x"/>, <paramref name="z"/>) may go out now. <paramref name="queuedOrders"/> is the group's
        /// courier queue (UnitRecord.QueuedOrders); <paramref name="executorMoveHours"/> is the hour of the executor's last issued move for the
        /// group (NaN when it has none).
        /// </summary>
        public Admission Admit(int groupId, float x, float z, double nowHours, int queuedOrders, double executorMoveHours)
        {
            if (!_entries.TryGetValue(groupId, out Entry e) || !e.HasMove) return Admission.Go;
            if (!e.Confirmed)
            {
                if (!double.IsNaN(executorMoveHours) && executorMoveHours >= e.SubmittedAt - SameTickToleranceHours)
                {
                    e.Confirmed = true;
                }
                else if (nowHours > e.SubmittedAt + SameTickToleranceHours)
                {
                    // The executor took nothing from the tick it was submitted in (a cooldown, a refused crossing): it is as if it was never sent.
                    e.HasMove = false;
                    _entries[groupId] = e;
                    return Admission.Go;
                }
                else
                {
                    return Admission.Go;
                }
                _entries[groupId] = e;
            }
            if (queuedOrders > 0 && nowHours - e.SubmittedAt < CourierWaitCapHours) return Admission.CourierWait;
            float dx = x - e.MoveX, dz = z - e.MoveZ;
            float r = OrderArbiter.DedupeMeters;
            return dx * dx + dz * dz <= r * r ? Admission.DeadBand : Admission.Go;
        }

        /// <summary>The producer submitted a release move for the group.</summary>
        public void NoteSubmitted(int groupId, float x, float z, double nowHours)
        {
            _entries.TryGetValue(groupId, out Entry e);
            e.HasMove = true;
            e.Confirmed = false;
            e.MoveX = x;
            e.MoveZ = z;
            e.SubmittedAt = nowHours;
            _entries[groupId] = e;
        }
    }
}
