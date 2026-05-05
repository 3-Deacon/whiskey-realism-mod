using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public sealed class DefenseCooldownTable
    {
        private readonly Dictionary<string, int> _remaining = new Dictionary<string, int>();

        // Tracks signatures that have been MarkRecovered'd so subsequent calls are
        // idempotent (the counter is not reset on repeated MarkRecovered calls for
        // the same signature while it is still active).  Cleared when the entry
        // expires or when Clear() is called.
        private readonly HashSet<string> _recoveredStarted = new HashSet<string>();

        public void MarkActive(string signature, int cooldownDays)
        {
            if (string.IsNullOrEmpty(signature)) return;
            _remaining[signature] = cooldownDays;
            // A fresh Active call resets the recovered-started flag so the next
            // Recovered transition is treated as a new first occurrence.
            _recoveredStarted.Remove(signature);
        }

        public void MarkRecovered(string signature, int cooldownDays)
        {
            if (string.IsNullOrEmpty(signature)) return;
            // Idempotent: only the first MarkRecovered call (per Active cycle) sets
            // the counter.  Subsequent calls while the entry is active are ignored.
            if (_recoveredStarted.Contains(signature)) return;
            _remaining[signature] = cooldownDays;
            _recoveredStarted.Add(signature);
        }

        public int RemainingDays(string signature)
        {
            if (string.IsNullOrEmpty(signature)) return 0;
            return _remaining.TryGetValue(signature, out var n) ? n : 0;
        }

        public bool IsActive(string signature)
        {
            return RemainingDays(signature) > 0;
        }

        public void Tick()
        {
            var keys = new List<string>(_remaining.Keys);
            foreach (var k in keys)
            {
                int v = _remaining[k] - 1;
                if (v <= 0)
                {
                    _remaining.Remove(k);
                    _recoveredStarted.Remove(k);
                }
                else
                {
                    _remaining[k] = v;
                }
            }
        }

        public void Clear(string signature)
        {
            if (string.IsNullOrEmpty(signature)) return;
            _remaining.Remove(signature);
            _recoveredStarted.Remove(signature);
        }
    }
}
