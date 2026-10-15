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
    /// reviews so the caller can force the skipped work.
    /// </summary>
    public static class ReviewCadenceCatchUp
    {
        // Days from 1 January of year 1 (always positive for a game date, so -1 stays every
        // consumer's "never"). UniStorm's campaign calendar (calendarType 1) rolls months over with
        // Gregorian lengths and leap years (CalculateMonths 157818-157853, February 157834-157845)
        // and builds UniStormDate = new DateTime(year, month, day) from the counters (157889; 155866
        // at load), so this is that date's day count; a month end adds no phantom days. Vanilla's
        // "|| yearCounter % 400 == 0" sits outside its February test (157834), a precedence quirk
        // that matters only in years divisible by 400: do not copy it here. GameVars.currenttimefromstart
        // is no day counter (30171: months are 1/12 year wide). The legacy year*372 + month*31 + day
        // serials of the CIC operations, battle history and capital threat are a different scale,
        // persisted in the sidecar: never compare the two.
        public static int DaySerial(int day, int month, int year)
        {
            int m0 = month - 1;
            int carry = m0 >= 0 ? m0 / 12 : (m0 - 11) / 12;
            year += carry;
            month = m0 - carry * 12 + 1;
            int y = month <= 2 ? year - 1 : year;
            int era = (y >= 0 ? y : y - 399) / 400;
            int yearOfEra = y - era * 400;
            int dayOfYear = (153 * ((month + 9) % 12) + 2) / 5 + day - 1;
            int dayOfEra = yearOfEra * 365 + yearOfEra / 4 - yearOfEra / 100 + dayOfYear;
            return era * 146097 + dayOfEra - FromMarchEpoch;
        }

        // The day of the month of a DaySerial (the inverse of the civil-date arithmetic above).
        internal static int DayOfMonth(int serial)
        {
            int z = serial + FromMarchEpoch;
            int era = (z >= 0 ? z : z - 146096) / 146097;
            int dayOfEra = z - era * 146097;
            int yearOfEra = (dayOfEra - dayOfEra / 1460 + dayOfEra / 36524 - dayOfEra / 146096) / 365;
            int dayOfYear = dayOfEra - (365 * yearOfEra + yearOfEra / 4 - yearOfEra / 100);
            int monthFromMarch = (5 * dayOfYear + 2) / 153;
            return dayOfYear - (153 * monthFromMarch + 2) / 5 + 1;
        }

        // 1 March of year 0 -> 1 January of year 1.
        private const int FromMarchEpoch = 306;

        public static bool SkippedWeeklyGate(int lastReviewSerial, int currentSerial)
        {
            if (lastReviewSerial < 0 || currentSerial - lastReviewSerial < 2) return false;
            if (currentSerial - lastReviewSerial > 31) return true;
            for (int serial = lastReviewSerial + 1; serial < currentSerial; serial++)
            {
                if (DayOfMonth(serial) % 7 == 0) return true;
            }
            return false;
        }

        // Vanilla's "campaign is running" state: CampaignController sets
        // SceneManagement.currentoperation = 2 at frame 30 (decompile 30081-30085), after the
        // save is loaded (frame 25, AICampaign.Load) and initial ownerships exist (frame 30);
        // PrepareLaunchingBattleFromCampaign also requires 2. Reviewing earlier would plan on
        // empty/stale state before the sidecar is applied.
        public static bool CampaignIsLive(int currentOperation) => currentOperation == 2;

        public static bool SkippedAlternatingGate(int lastReviewSerial, int currentSerial)
        {
            return lastReviewSerial >= 0 && currentSerial - lastReviewSerial >= 2;
        }
    }
}
