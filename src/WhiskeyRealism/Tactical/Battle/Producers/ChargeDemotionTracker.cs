using System;

namespace WhiskeyRealism.Tactical.Battle.Producers
{
    // Charge demotion telemetry, pure half. The ChargeDemotion producer hands every
    // demotion intent it submits to Submitted; on the next tick Confirm reads the
    // executor's last Stance outcome for each group and counts the demotion only when
    // the executor issued it (Issued, source ChargeDemotion, at or after the
    // submission). A demotion the arbiter dropped, the executor deduped or refused, or
    // another source's stance write that landed instead is not counted.
    // TryTakeWindow buckets the per-side count on real time for the counter row.
    public sealed class ChargeDemotionTracker
    {
        public struct Demotion
        {
            public int GroupId;
            public int Stance;
            public double SubmittedAtHours;
        }

        private Demotion[] _pending = new Demotion[8];
        private int _pendingCount;
        private float _lastTakeAt = float.NegativeInfinity;

        private int _total;
        private int _window;

        public void Submitted(in BattleOrderIntent intent, double nowHours)
        {
            if (intent.Channel != OrderChannel.Stance || intent.Source != IntentSource.ChargeDemotion) return;
            for (int i = 0; i < _pendingCount; i++)
            {
                if (_pending[i].GroupId != intent.GroupId) continue;
                _pending[i].Stance = intent.Value;
                _pending[i].SubmittedAtHours = nowHours;
                return;
            }
            if (_pendingCount == _pending.Length) Array.Resize(ref _pending, _pending.Length * 2);
            _pending[_pendingCount++] = new Demotion { GroupId = intent.GroupId, Stance = intent.Value, SubmittedAtHours = nowHours };
        }

        // Counts every confirmed demotion, writes up to confirmed.Length of them and
        // returns how many it wrote. Pending is consumed either way.
        public int Confirm(OrderExecutor executor, Demotion[] confirmed)
        {
            int written = 0;
            for (int i = 0; i < _pendingCount; i++)
            {
                ref Demotion d = ref _pending[i];
                if (executor == null || !executor.TryGetLastOutcome(d.GroupId, OrderChannel.Stance, out OrderOutcome outcome)) continue;
                if (outcome.Kind != OrderOutcomeKind.Issued || outcome.Source != IntentSource.ChargeDemotion) continue;
                if (outcome.AtHours < d.SubmittedAtHours) continue;
                _total++;
                _window++;
                if (confirmed != null && written < confirmed.Length) confirmed[written++] = d;
            }
            _pendingCount = 0;
            return written;
        }

        // True at most once per periodSeconds, and only when a demotion was confirmed
        // since the last take. window and total are set either way.
        public bool TryTakeWindow(float nowSeconds, float periodSeconds, out int window, out int total)
        {
            window = _window;
            total = _total;
            if (_window == 0 || nowSeconds - _lastTakeAt < periodSeconds) return false;
            _window = 0;
            _lastTakeAt = nowSeconds;
            return true;
        }

        public void Clear()
        {
            _pendingCount = 0;
            _lastTakeAt = float.NegativeInfinity;
            _total = 0;
            _window = 0;
        }
    }
}
