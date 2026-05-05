using System;

namespace WhiskeyRealism.Strategic
{
    public static class StrategicCadencePolicy
    {
        public static bool ShouldRunAlternatingByAlliance(int day, int alliance, bool forceRefresh = false)
        {
            if (forceRefresh) return true;
            if (day <= 0 || alliance < 0) return false;
            return day % 2 == alliance % 2;
        }

        public static bool ShouldRunWeeklyOrSourceChanged(
            int day,
            string currentSourceSignature,
            string previousSourceSignature,
            bool forceRefresh = false)
        {
            if (forceRefresh) return true;
            if (day <= 0) return false;
            if (SourceChanged(currentSourceSignature, previousSourceSignature)) return true;
            return day % 7 == 0;
        }

        public static bool SourceChanged(string currentSourceSignature, string previousSourceSignature)
        {
            if (previousSourceSignature == null) return true;
            return !string.Equals(currentSourceSignature ?? string.Empty, previousSourceSignature, StringComparison.Ordinal);
        }
    }
}
