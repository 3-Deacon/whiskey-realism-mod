using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
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
