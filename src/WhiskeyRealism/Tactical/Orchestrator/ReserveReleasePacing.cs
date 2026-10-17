using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// The executor's last issued Move for a group, as the producer reads it back (OrderExecutor.LastIssuedMove and TryGetLastOutcome): the hour, the
    /// target, and whether the order was this producer's (outcome source ReserveRelease, a move issued, taken or retried).
    /// </summary>
    internal readonly struct ExecutorMove
    {
        public ExecutorMove(bool fromRelease, double hours, float x, float z)
        {
            FromRelease = fromRelease;
            Hours = hours;
            X = x;
            Z = z;
        }

        public bool FromRelease { get; }
        public double Hours { get; }
        public float X { get; }
        public float Z { get; }

        /// <summary>The executor has issued no move for the group.</summary>
        public static ExecutorMove None => new ExecutorMove(false, double.NaN, 0f, 0f);
    }

    /// <summary>
    /// Pacing of the reserve-release producer's moves (pure; one instance per side, kept by the producer). Two rules, both on the doctrine clock:
    /// <list type="bullet">
    /// <item>The hold: a release must stand for <see cref="HoldMinutes"/> of doctrine time before its move goes out. It is held as a release, whatever
    /// the release role (a flank guard that swaps between RefuseLeft and RefuseRight, or SupportMain and Main, is one release: the move goes out for the
    /// role standing when the hold completes); a release the allocation takes back inside the hold sends nothing, and the next one starts afresh. The
    /// allocator's own roles changed every few seconds in the 2026-10-17 smoke and each change queued a courier order. A crisis release
    /// (<see cref="DirectChildRole.Fix"/>) is exempt: the allocator already confirmed it across two cycles (ReleaseHysteresis), and the line it answers
    /// is in contact now. So is a crossing guard (a release with a guard point): it is timed to the enemy's arrival at the crossing and holds its own
    /// binding (ReserveReleaseState.BindCrossing).</item>
    /// <item>The dead-band: no new move while the last one the executor took is still waiting for its courier (the unit's queue is not empty, for at
    /// most <see cref="CourierWaitCapHours"/>), and none to a target within the executor's dedupe radius of the last one WHILE THE SAME RELEASE STANDS.
    /// The executor's own duplicate test reads vanilla's last waypoint, which vanilla's link follow keeps overwriting, so it cannot tell a repeat of
    /// this producer's order. The record is the producer's own and is confirmed only by the executor's last move for the group when that move is this
    /// producer's, at the hour of the submission or later and within the dedupe radius of the recorded target (a release that lost the arbitration to
    /// another producer's move is not delivered; an unconfirmed record is dropped the next tick, so the move is retried). The target record ends with
    /// its release: a second release is judged afresh. The courier wait outlives the release for the cap, so a second release does not queue behind
    /// an order still in transit.</item>
    /// </list>
    /// </summary>
    internal sealed class ReserveReleasePacing
    {
        /// <summary>Doctrine minutes a release must stand before its move goes out.</summary>
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
            public double Since;
            // The move this release sent: pending confirmation, then the dead-band's target. Ends with the release.
            public bool HasMove;
            public bool Confirmed;
            public float MoveX, MoveZ;
            public double SubmittedAt;
            // The doctrine hour until which a confirmed move may still be queued; NaN when none. Outlives the release.
            public double CourierUntil;
        }

        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();

        public void Clear() => _entries.Clear();

        /// <summary>
        /// One observation of a group the ledger holds in reserve: whether the allocation released it, and in which role (<paramref name="role"/> only
        /// tells a crisis Fix from the rest). True when the move may go out: the release has stood for <see cref="HoldHours"/>, or is a crisis release or a
        /// crossing guard. Not released: false, the release's move record ends, and the group's next release starts its hold afresh.
        /// </summary>
        public bool HoldElapsed(int groupId, bool released, DirectChildRole role, bool guardPoint, double nowHours)
        {
            _entries.TryGetValue(groupId, out Entry e);
            if (!released)
            {
                if (e.Releasing || e.HasMove)
                {
                    e.Releasing = false;
                    e.HasMove = false;
                    e.Confirmed = false;
                    _entries[groupId] = e;
                }
                return false;
            }
            if (!e.Releasing)
            {
                e.Releasing = true;
                e.Since = nowHours;
                e.HasMove = false;
                e.Confirmed = false;
                _entries[groupId] = e;
            }
            return role == DirectChildRole.Fix || guardPoint || nowHours - e.Since >= HoldHours;
        }

        /// <summary>The doctrine hours the group's current release has stood, 0 when it is not being released.</summary>
        public double HeldFor(int groupId, double nowHours)
            => _entries.TryGetValue(groupId, out Entry e) && e.Releasing ? Math.Max(0.0, nowHours - e.Since) : 0.0;

        /// <summary>
        /// Whether a release move to (<paramref name="x"/>, <paramref name="z"/>) may go out now. <paramref name="queuedOrders"/> is the group's
        /// courier queue (UnitRecord.QueuedOrders); <paramref name="executor"/> is the executor's last issued move for the group.
        /// </summary>
        public Admission Admit(int groupId, float x, float z, double nowHours, int queuedOrders, in ExecutorMove executor)
        {
            if (!_entries.TryGetValue(groupId, out Entry e)) return Admission.Go;
            float r = OrderArbiter.DedupeMeters;
            if (e.HasMove && !e.Confirmed)
            {
                if (Delivers(in e, in executor, r))
                {
                    e.Confirmed = true;
                    e.CourierUntil = e.SubmittedAt + CourierWaitCapHours;
                    _entries[groupId] = e;
                }
                else if (nowHours > e.SubmittedAt + SameTickToleranceHours)
                {
                    // The executor took nothing of this producer's from the tick it was submitted in (a cooldown, a refused crossing, a lost
                    // arbitration): it is as if it was never sent.
                    e.HasMove = false;
                    _entries[groupId] = e;
                }
                else
                {
                    return Admission.Go;
                }
            }
            if (queuedOrders > 0 && nowHours < e.CourierUntil) return Admission.CourierWait;
            if (!e.HasMove) return Admission.Go;
            float dx = x - e.MoveX, dz = z - e.MoveZ;
            return dx * dx + dz * dz <= r * r ? Admission.DeadBand : Admission.Go;
        }

        // The executor's last move is this producer's, not older than the submission, and went to the recorded target (within the dedupe radius).
        private static bool Delivers(in Entry e, in ExecutorMove move, float radius)
        {
            if (!move.FromRelease || double.IsNaN(move.Hours) || move.Hours < e.SubmittedAt - SameTickToleranceHours) return false;
            float dx = move.X - e.MoveX, dz = move.Z - e.MoveZ;
            return dx * dx + dz * dz <= radius * radius;
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
            e.CourierUntil = double.NaN;
            _entries[groupId] = e;
        }
    }
}
