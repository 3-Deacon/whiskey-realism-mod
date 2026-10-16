using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // How one executor action went (plan G5a, P8). Judge reports Confirmed / Failed / GaveUp / Queued; IssueOutcome
    // reports Issued / Retried / Queued; the mover's tally counts the rest (SkippedPlayer, SkippedBusy, Deferred).
    public enum MoveOutcome { None = 0, Issued = 1, Confirmed = 2, Failed = 3, Retried = 4, GaveUp = 5, SkippedPlayer = 6, SkippedBusy = 7, Queued = 8, Deferred = 9 }

    public struct MoveRecord
    {
        public int InstanceId;      // in-session lookup only, never persisted
        public string UnitKey;      // name + ":" + commander, the sidecar convention; persisted identity
        public float TargetX, TargetZ;
        public int IssuedDay;
        public int Retries;
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
            public State State;
        }

        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
        private int _budgetDay = int.MinValue;
        private int _budgetUsed;

        public bool ShouldIssue(int instanceId, float tx, float tz, int daySerial)
        {
            if (BudgetUsed(daySerial) >= MaxMovesPerDay) return false;
            if (!_entries.TryGetValue(instanceId, out var e) || !SameTarget(e, tx, tz)) return true;
            switch (e.State)
            {
                case State.Queued:
                case State.Failed:
                case State.Confirmed:
                    return true;      // Confirmed: the caller skips a formation already underway (its waypoint is the target)
                default:
                    return false;     // Issued: in transit; GaveUp: not again at this target
            }
        }

        public void RecordIssued(int instanceId, float fromX, float fromZ, float tx, float tz, int daySerial, bool queued, string unitKey = null)
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
                IssuedDay = daySerial, Retries = retries, State = queued ? State.Queued : State.Issued
            };
            if (!queued) Spend(daySerial);
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
        public void RecordRefused(int instanceId, float tx, float tz, int daySerial, string unitKey = null)
        {
            _entries.TryGetValue(instanceId, out var previous);
            bool again = previous != null && SameTarget(previous, tx, tz) && previous.State == State.Failed;
            _entries[instanceId] = new Entry
            {
                InstanceId = instanceId, UnitKey = unitKey ?? (previous != null ? previous.UnitKey : null),
                FromX = tx, FromZ = tz, TargetX = tx, TargetZ = tz,
                IssuedDay = daySerial, Retries = again ? previous.Retries + 1 : 0,
                State = again ? State.GaveUp : State.Failed
            };
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

        public void Forget(int instanceId)
        {
            _entries.Remove(instanceId);
        }

        public void Clear()
        {
            _entries.Clear();
            _budgetDay = int.MinValue;
            _budgetUsed = 0;
        }

        public IEnumerable<MoveRecord> Records
        {
            get
            {
                foreach (var e in _entries.Values)
                    yield return new MoveRecord { InstanceId = e.InstanceId, UnitKey = e.UnitKey, TargetX = e.TargetX, TargetZ = e.TargetZ, IssuedDay = e.IssuedDay, Retries = e.Retries };
            }
        }

        // After a load every record is settled: one that had already used its retry stays given up, the rest may be reissued
        // at the next review if the formation is still unfinished (the in-session ids are rebound by unit key in G6).
        public void Restore(IEnumerable<MoveRecord> records)
        {
            Clear();
            if (records == null) return;
            foreach (var r in records)
                _entries[r.InstanceId] = new Entry
                {
                    InstanceId = r.InstanceId, UnitKey = r.UnitKey, FromX = r.TargetX, FromZ = r.TargetZ, TargetX = r.TargetX, TargetZ = r.TargetZ,
                    IssuedDay = r.IssuedDay, Retries = r.Retries, State = r.Retries >= 1 ? State.GaveUp : State.Failed
                };
        }

        private int BudgetUsed(int daySerial)
        {
            return daySerial == _budgetDay ? _budgetUsed : 0;
        }

        private void Spend(int daySerial)
        {
            if (daySerial != _budgetDay)
            {
                _budgetDay = daySerial;
                _budgetUsed = 0;
            }
            _budgetUsed++;
        }

        private static bool SameTarget(Entry e, float tx, float tz)
        {
            return Math.Abs(e.TargetX - tx) <= TargetEpsilon && Math.Abs(e.TargetZ - tz) <= TargetEpsilon;
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
