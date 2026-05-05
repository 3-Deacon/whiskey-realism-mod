using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    // Tracks per-signature cooldown day counts for the defense ledger's
    // Recovered-posture stabilization buffer. Signatures are owned by
    // DefenseThreatSignature; the table itself is shape-agnostic.
    //
    // MarkActive / MarkRecovered set (or reset) the countdown for a signature.
    // They are kept as separate methods so call sites carry semantic intent, and
    // so future tuning can give them different default durations without refactors.
    //
    // Tick decrements all active entries by one day and removes any that reach zero.
    // IsActive / RemainingDays / Clear are read + release helpers.
    public sealed class DefenseCooldownTable
    {
        private readonly Dictionary<string, int> _remaining = new Dictionary<string, int>();

        public void MarkActive(string signature, int cooldownDays)
        {
            if (string.IsNullOrEmpty(signature)) return;
            _remaining[signature] = cooldownDays;
        }

        public void MarkRecovered(string signature, int cooldownDays)
        {
            if (string.IsNullOrEmpty(signature)) return;
            _remaining[signature] = cooldownDays;
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
                if (v <= 0) _remaining.Remove(k);
                else _remaining[k] = v;
            }
        }

        public void Clear(string signature)
        {
            if (!string.IsNullOrEmpty(signature)) _remaining.Remove(signature);
        }
    }
}
