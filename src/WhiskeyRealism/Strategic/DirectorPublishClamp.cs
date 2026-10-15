namespace WhiskeyRealism.Strategic
{
    /// <summary>
    /// Rate-limits Director posture publishes to at most one per alliance per game day.
    /// R16: the previous single shared 1-publish-per-real-second clamp let alliance 0 take
    /// the slot every review and froze alliance 1's posture; it was also wall-clock based,
    /// which starves the director at 20x fast-forward. Keyed on the campaign day serial now.
    /// </summary>
    public sealed class DirectorPublishClamp
    {
        private readonly int[] _lastPublishDay = { int.MinValue, int.MinValue };

        /// <summary>
        /// Returns true and records <paramref name="daySerial"/> when this alliance has
        /// not published yet on that game day. Out-of-range alliances (Europe = 2) never publish.
        /// </summary>
        public bool TryPublish(int allianceId, int daySerial)
        {
            if (allianceId < 0 || allianceId >= _lastPublishDay.Length) return false;
            if (_lastPublishDay[allianceId] == daySerial) return false;
            _lastPublishDay[allianceId] = daySerial;
            return true;
        }
    }
}

namespace WhiskeyRealism.Strategic
{
    /// <summary>
    /// Cadence catch-up for the daily review. The review observes the UniStorm calendar
    /// (dayCounter/monthCounter, rolled over in UniStormSystem.CalculateMonths, decompile
    /// 157819) once per AICampaign.Update frame (MonthlyTickHookPatch); at high campaign
    /// speed / long frames the date can advance several days between two observations, so
    /// the review can jump e.g. day 6 -> 9 and miss the
    /// day % 7 weekly gates and the day % 2 alliance-alternating gate of
    /// StrategicCadencePolicy. These pure checks report a gate that fell strictly between two
    /// reviews so the caller can force the skipped work. Day serial = year*372 + month*31 + day.
    /// </summary>
    public static class ReviewCadenceCatchUp
    {
        public static int DaySerial(int day, int month, int year) => year * 372 + month * 31 + day;

        public static bool SkippedWeeklyGate(int lastReviewSerial, int currentSerial)
        {
            if (lastReviewSerial < 0 || currentSerial - lastReviewSerial < 2) return false;
            if (currentSerial - lastReviewSerial > 31) return true;
            for (int serial = lastReviewSerial + 1; serial < currentSerial; serial++)
            {
                int dayOfMonth = serial % 31;
                if (dayOfMonth == 0) dayOfMonth = 31;
                if (dayOfMonth % 7 == 0) return true;
            }
            return false;
        }

        public static bool SkippedAlternatingGate(int lastReviewSerial, int currentSerial)
        {
            return lastReviewSerial >= 0 && currentSerial - lastReviewSerial >= 2;
        }
    }
}
