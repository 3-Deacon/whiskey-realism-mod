using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // How one executor action went (plan G5a, P8). Judge reports Confirmed / Failed / GaveUp / Queued; IssueOutcome
    // reports Issued / Retried / Queued; the mover's tally counts the rest (SkippedPlayer, SkippedBusy, Deferred).
    public enum MoveOutcome { None = 0, Issued = 1, Confirmed = 2, Failed = 3, Retried = 4, GaveUp = 5, SkippedPlayer = 6, SkippedBusy = 7, Queued = 8, Deferred = 9 }

    // Which daily budget a move spends (plan G5d): the planner's roles (MaxMovesPerDay) or reconnaissance
    // (MaxReconMovesPerDay), so scouting never starves the main effort and the main effort never starves the scouts.
    public enum MoveBudget { Planner = 0, Recon = 1 }

    public struct MoveRecord
    {
        public int InstanceId;      // in-session lookup only, never persisted
        public string UnitKey;      // name + ":" + commander, the sidecar convention; persisted identity
        public float TargetX, TargetZ;
        public int IssuedDay;
        public int Retries;
        public int ObjectiveId;     // the objective a MainEffort or Support move went toward, -1 none (G3 reads it; persisted with the record)
    }

    // A move the tracker gave up on (P8): this formation, at this target. The allocator reads the set so the next review does not
    // send the same formation to the same place again (plan G5a, "the allocator reassigns the role"); G3 reads the objective
    // the move was issued for, so a mass whose members all gave up on the main objective is not kept.
    public struct GaveUpMove
    {
        public int InstanceId;
        public float TargetX, TargetZ;
        public int ObjectiveId;     // -1 when the move was not toward an objective (a depot, a recon hold point, a supply source)
    }

    // The move bookkeeping of one alliance (P8): at most MaxMovesPerDay MoveUnitTo calls per review day, a move judged at the
    // next review (confirmed when the formation's last waypoint moved toward the target or it covered ConfirmFraction of the
    // way, else failed), one retry, then given up for that target (a new target starts afresh). A move queued behind a running
    // order is not a failure and spends none of the budget. Pure; written only by the mover inside the review, read by the
    // availability patches through InTransit.
    //
    // A record's life: Issued (MoveUnitTo took it) or Queued (an order was running) -> judged: Confirmed, or Failed (one
    // retry allowed) -> reissued -> judged again: Confirmed or GaveUp. A refusal (vanilla refused the target, or it is
    // restricted ground) is a miss without the waiting: RecordRefused. A restored record is settled (RestoreState).
    public sealed class MoveTracker
    {
        public const int MaxMovesPerDay = 6;
        // The reconnaissance executor's own cap (plan G5d), counted apart from MaxMovesPerDay.
        public const int MaxReconMovesPerDay = 3;
        // A give-up lasts this many review days, then the formation may try the same target again. A give-up costs two
        // reviews (one miss, one retry); vanilla's refusal of a target (MoveUnitTo, 14486) follows a state's war status, which
        // changes in days or weeks, not reviews. Fourteen days is a week of daily reviews on each side of that: long enough
        // that the retry (2 of the daily budget of 6 per formation) does not repeat every cycle, short enough that a
        // formation is not shut out of an objective for a season (plan G5a, as built item 3).
        public const int GaveUpExpiryDays = 14;
        // A queued record nobody has touched for this many review days is dropped whatever the order ahead of it is doing: the
        // mover re-records a wanted queued move at every review, so only a record nobody wants any more (or that no pass can reach)
        // gets this old. A bound on top of SettleQueued, so no record stays queued, and the formation withheld, forever.
        public const int QueuedExpiryDays = 14;
        public const float ConfirmFraction = 0.25f;
        // A waypoint within this of the target's distance does not count as having moved toward it.
        private const float WaypointEpsilon = 1f;
        private const float TargetEpsilon = 1f;

        private enum State { Issued, Queued, Confirmed, Failed, GaveUp }

        private sealed class Entry
        {
            public int InstanceId;
            public string UnitKey;
            public float FromX, FromZ, TargetX, TargetZ;
            public int IssuedDay;
            public int Retries;
            public int ObjectiveId = -1;
            public MoveBudget Budget;
            public State State;
        }

        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
        private readonly List<int> _expired = new List<int>();
        private readonly int[] _budgetDay = { int.MinValue, int.MinValue };
        private readonly int[] _budgetUsed = new int[2];

        public bool ShouldIssue(int instanceId, float tx, float tz, int daySerial, MoveBudget budget = MoveBudget.Planner)
        {
            if (BudgetUsed(budget, daySerial) >= CapOf(budget)) return false;
            if (!_entries.TryGetValue(instanceId, out var e) || !SameTarget(e, tx, tz)) return true;
            switch (e.State)
            {
                case State.Queued:
                case State.Failed:
                case State.Confirmed:
                    return true;      // Confirmed: the caller skips a formation already underway (its waypoint is the target)
                case State.GaveUp:
                    return Expired(e, daySerial);  // not again at this target until the give-up lapses
                default:
                    return false;     // Issued: in transit
            }
        }

        public void RecordIssued(int instanceId, float fromX, float fromZ, float tx, float tz, int daySerial, bool queued, string unitKey = null, int objectiveId = -1,
                                 MoveBudget budget = MoveBudget.Planner)
        {
            _entries.TryGetValue(instanceId, out var previous);
            bool same = previous != null && SameTarget(previous, tx, tz);
            int retries = 0;
            if (same && previous.State == State.Failed) retries = previous.Retries + 1;
            else if (same && previous.State == State.Queued) retries = previous.Retries;
            _entries[instanceId] = new Entry
            {
                InstanceId = instanceId, UnitKey = unitKey ?? (previous != null ? previous.UnitKey : null),
                FromX = fromX, FromZ = fromZ, TargetX = tx, TargetZ = tz,
                IssuedDay = daySerial, Retries = retries, ObjectiveId = objectiveId, Budget = budget, State = queued ? State.Queued : State.Issued
            };
            if (!queued) Spend(budget, daySerial);
        }

        // What issuing this move now would be: Queued behind a running order, Retried after a failed attempt at the same target,
        // else Issued.
        public MoveOutcome IssueOutcome(int instanceId, float tx, float tz, bool queued)
        {
            if (queued) return MoveOutcome.Queued;
            if (_entries.TryGetValue(instanceId, out var e) && SameTarget(e, tx, tz) && e.State == State.Failed) return MoveOutcome.Retried;
            return MoveOutcome.Issued;
        }

        // Vanilla refused the move (MoveUnitTo returned false with no order running: a state neither own, allied nor at war,
        // 14493) or the target is restricted ground. A first refusal allows one retry, the second ends it for this target.
        public void RecordRefused(int instanceId, float tx, float tz, int daySerial, string unitKey = null, int objectiveId = -1, MoveBudget budget = MoveBudget.Planner)
        {
            _entries.TryGetValue(instanceId, out var previous);
            bool again = previous != null && SameTarget(previous, tx, tz) && previous.State == State.Failed;
            _entries[instanceId] = new Entry
            {
                InstanceId = instanceId, UnitKey = unitKey ?? (previous != null ? previous.UnitKey : null),
                FromX = tx, FromZ = tz, TargetX = tx, TargetZ = tz,
                IssuedDay = daySerial, Retries = again ? previous.Retries + 1 : 0, ObjectiveId = objectiveId, Budget = budget,
                State = again ? State.GaveUp : State.Failed
            };
        }

        // The move the tracker holds for the formation, for the miss row (a failed or given-up move names its target, start and tries).
        public bool TryGetMove(int instanceId, out float fromX, out float fromZ, out float targetX, out float targetZ, out int issuedDay, out int retries, out int objectiveId)
        {
            if (!_entries.TryGetValue(instanceId, out var e))
            {
                fromX = fromZ = targetX = targetZ = 0f;
                issuedDay = retries = 0;
                objectiveId = -1;
                return false;
            }
            fromX = e.FromX;
            fromZ = e.FromZ;
            targetX = e.TargetX;
            targetZ = e.TargetZ;
            issuedDay = e.IssuedDay;
            retries = e.Retries;
            objectiveId = e.ObjectiveId;
            return true;
        }

        public MoveOutcome Judge(int instanceId, float nowX, float nowZ, float waypointX, float waypointZ, int daySerial)
        {
            if (!_entries.TryGetValue(instanceId, out var e)) return MoveOutcome.None;
            if (e.State == State.Queued) return MoveOutcome.Queued;
            if (e.State != State.Issued || daySerial <= e.IssuedDay) return MoveOutcome.None;
            float start = Distance(e.FromX, e.FromZ, e.TargetX, e.TargetZ);
            bool towardTarget = Distance(waypointX, waypointZ, e.TargetX, e.TargetZ) < start - WaypointEpsilon;
            bool coveredShare = Distance(nowX, nowZ, e.TargetX, e.TargetZ) <= (1f - ConfirmFraction) * start;
            if (towardTarget || coveredShare)
            {
                e.State = State.Confirmed;
                return MoveOutcome.Confirmed;
            }
            if (e.Retries < 1)
            {
                e.State = State.Failed;
                return MoveOutcome.Failed;
            }
            e.State = State.GaveUp;
            return MoveOutcome.GaveUp;
        }

        // The one rule that settles a Queued record (plan G5d, verify r2 R1/R2; Judge leaves it Queued by design, 9966-10052: one order
        // is served at a time). A queued move is a promise to send the formation once the order ahead of it (a DefensiveMovingOrder,
        // FormationFlags.MoveQueued) has ended; it settles, and the formation is no longer in transit or withheld from vanilla, when
        // the order ahead has ended (the owner then re-issues the move if it still wants it) or when no owner wants the move any more
        // (the role changed, the mission ended). `budget` names the owner: a record of the other budget is untouched.
        // A queued RETRY that is still wanted settles to Failed with its retry count kept, so the owner's re-issue is still the retry
        // and the next miss gives up as it did before the settle existed (verify r3 N1); a first attempt that never failed, and any
        // record nobody wants, is removed.
        public bool SettleQueued(int instanceId, MoveBudget budget, bool wanted, bool orderRunning)
        {
            if (!_entries.TryGetValue(instanceId, out var e) || e.State != State.Queued || e.Budget != budget) return false;
            if (wanted && orderRunning) return false;
            if (wanted && e.Retries >= 1)
            {
                e.State = State.Failed;
                return true;
            }
            _entries.Remove(instanceId);
            return true;
        }

        // The bound: queued records untouched for QueuedExpiryDays review days are dropped (either budget).
        public void SettleExpiredQueued(int daySerial)
        {
            _expired.Clear();
            foreach (var pair in _entries)
                if (pair.Value.State == State.Queued && daySerial - pair.Value.IssuedDay >= QueuedExpiryDays) _expired.Add(pair.Key);
            for (int i = 0; i < _expired.Count; i++) _entries.Remove(_expired[i]);
        }

        // Issued or queued and not yet judged: the formation is the mover's, vanilla's offense and defence keep off it.
        public bool InTransit(int instanceId)
        {
            return _entries.TryGetValue(instanceId, out var e) && (e.State == State.Issued || e.State == State.Queued);
        }

        public void CollectInTransit(List<int> into)
        {
            into.Clear();
            foreach (var pair in _entries)
                if (pair.Value.State == State.Issued || pair.Value.State == State.Queued) into.Add(pair.Key);
            into.Sort();
        }

        // Every planner move given up on and not yet lapsed (GaveUpExpiryDays), ordered by instance id so the planner's and the
        // allocator's input is deterministic. Reconnaissance give-ups are not listed (plan G5d, verify F1): G4 matches a give-up
        // by target point, and a recall point can be the capital (a CapitalGuard's post), so a scout that could not reach it
        // would cost the guard its role; the recon executor keeps its own give-ups apart (ShouldIssue) and a post it gave up on
        // is not re-sent until the give-up lapses.
        public void CollectGaveUp(List<GaveUpMove> into, int daySerial)
        {
            into.Clear();
            foreach (var pair in _entries)
                if (pair.Value.State == State.GaveUp && pair.Value.Budget == MoveBudget.Planner && !Expired(pair.Value, daySerial))
                    into.Add(new GaveUpMove { InstanceId = pair.Key, TargetX = pair.Value.TargetX, TargetZ = pair.Value.TargetZ, ObjectiveId = pair.Value.ObjectiveId });
            into.Sort((a, b) => a.InstanceId.CompareTo(b.InstanceId));
        }

        public void Forget(int instanceId)
        {
            _entries.Remove(instanceId);
        }

        // True when the formation is on a move toward (tx, tz) within `radius` that was issued at an earlier review than daySerial and
        // not judged a failure: Issued (judged at this review's mover pass, which runs after the executors) or Confirmed (underway).
        // A queued move has not started and a failed or given-up one is not going there. The depot executor reads it before it wishes
        // ahead of a builder's arrival (plan G5c, verify r2 F6a).
        public bool MarchingToward(int instanceId, float tx, float tz, float radius, int daySerial)
        {
            if (!_entries.TryGetValue(instanceId, out var e) || (e.State != State.Issued && e.State != State.Confirmed)) return false;
            if (e.IssuedDay >= daySerial) return false;
            return Distance(e.TargetX, e.TargetZ, tx, tz) <= radius;
        }

        public void Clear()
        {
            _entries.Clear();
            for (int b = 0; b < _budgetDay.Length; b++)
            {
                _budgetDay[b] = int.MinValue;
                _budgetUsed[b] = 0;
            }
        }

        // The planner's records, for the sidecar (G6). Recon records are not persisted: a load restarts the scouts' moves.
        public IEnumerable<MoveRecord> Records
        {
            get
            {
                foreach (var e in _entries.Values)
                    if (e.Budget == MoveBudget.Planner)
                        yield return new MoveRecord { InstanceId = e.InstanceId, UnitKey = e.UnitKey, TargetX = e.TargetX, TargetZ = e.TargetZ, IssuedDay = e.IssuedDay, Retries = e.Retries, ObjectiveId = e.ObjectiveId };
            }
        }

        // After a load every record is settled: one that had already used its retry stays given up, the rest may be reissued
        // at the next review if the formation is still unfinished (the in-session ids are rebound by unit key on the first review, PlannerRebind.Resolve).
        public void Restore(IEnumerable<MoveRecord> records)
        {
            Clear();
            if (records == null) return;
            foreach (var r in records)
                _entries[r.InstanceId] = new Entry
                {
                    InstanceId = r.InstanceId, UnitKey = r.UnitKey, FromX = r.TargetX, FromZ = r.TargetZ, TargetX = r.TargetX, TargetZ = r.TargetZ,
                    IssuedDay = r.IssuedDay, Retries = r.Retries, ObjectiveId = r.ObjectiveId, State = r.Retries >= 1 ? State.GaveUp : State.Failed
                };
        }

        private static bool Expired(Entry e, int daySerial)
        {
            return daySerial - e.IssuedDay >= GaveUpExpiryDays;
        }

        private static int CapOf(MoveBudget budget)
        {
            return budget == MoveBudget.Recon ? MaxReconMovesPerDay : MaxMovesPerDay;
        }

        private int BudgetUsed(MoveBudget budget, int daySerial)
        {
            return daySerial == _budgetDay[(int)budget] ? _budgetUsed[(int)budget] : 0;
        }

        private void Spend(MoveBudget budget, int daySerial)
        {
            int b = (int)budget;
            if (daySerial != _budgetDay[b])
            {
                _budgetDay[b] = daySerial;
                _budgetUsed[b] = 0;
            }
            _budgetUsed[b]++;
        }

        private static bool SameTarget(Entry e, float tx, float tz)
        {
            return SameTarget(e.TargetX, e.TargetZ, tx, tz);
        }

        internal static bool SameTarget(float ax, float az, float bx, float bz)
        {
            return Math.Abs(ax - bx) <= TargetEpsilon && Math.Abs(az - bz) <= TargetEpsilon;
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }

    // The per-alliance trackers: executor bookkeeping, written by the mover inside the review and read by the availability
    // patches (CampaignWithholdView.InTransit). Per campaign.
    public static class MoveTrackers
    {
        private static readonly MoveTracker[] Trackers = { new MoveTracker(), new MoveTracker() };

        static MoveTrackers()
        {
            WhiskeyRealism.Util.CampaignScope.Register("campaign-planner-move-trackers", Reset);
        }

        private static void Reset()
        {
            for (int a = 0; a < Trackers.Length; a++) Trackers[a] = new MoveTracker();
        }

        public static MoveTracker For(int alliance)
        {
            return alliance >= 0 && alliance < Trackers.Length ? Trackers[alliance] : null;
        }
    }
}
