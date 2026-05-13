using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.PlayerOrders
{
    internal sealed class PlayerOrderDiagnostics
    {
        private readonly int _capacity;
        private readonly Queue<string> _order = new Queue<string>();
        private readonly HashSet<string> _seen = new HashSet<string>();

        public PlayerOrderDiagnostics(int capacity = 64)
        {
            _capacity = capacity < 1 ? 1 : capacity;
        }

        public bool ShouldLog(string unitKey, string signature)
        {
            var formatted = FormatSignature(unitKey, signature);
            if (_seen.Contains(formatted))
            {
                return false;
            }

            _seen.Add(formatted);
            _order.Enqueue(formatted);
            while (_order.Count > _capacity)
            {
                _seen.Remove(_order.Dequeue());
            }

            return true;
        }

        public static string FormatSignature(string unitKey, string signature)
        {
            return Sanitize(unitKey) + "|" + Sanitize(signature);
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unknown";
            }

            var trimmed = value.Trim();
            return trimmed.Length <= 96 ? trimmed : trimmed.Substring(0, 96);
        }
    }
}
