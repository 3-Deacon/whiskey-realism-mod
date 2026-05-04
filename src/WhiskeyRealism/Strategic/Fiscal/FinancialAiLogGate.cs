using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Fiscal
{
    public sealed class FinancialAiLogGate
    {
        private readonly HashSet<string> _seen = new HashSet<string>();

        public bool ShouldLog(string signature)
        {
            if (string.IsNullOrEmpty(signature)) return false;
            return _seen.Add(signature);
        }

        public static string Signature(int alliance, string laneType, int lane, float oldValue, float newValue, FiscalPosture posture)
        {
            return alliance + ":" + laneType + ":" + lane + ":" +
                   oldValue.ToString("F2") + ":" +
                   newValue.ToString("F2") + ":" +
                   posture;
        }
    }
}
