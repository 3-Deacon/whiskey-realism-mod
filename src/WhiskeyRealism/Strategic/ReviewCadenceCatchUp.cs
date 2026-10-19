using System;
using System.Globalization;

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
        // at load), so this is that date's day count; a month end adds no phantom days. month is
        // vanilla's monthCounter, 1-12. Vanilla's February rollover has two quirks that bite only in
        // years divisible by 100 ("|| yearCounter % 400 == 0" outside the February test, 157834; a
        // non-leap century year falls through to a 30-day February, 157841): do not copy them here.
        // GameVars.currenttimefromstart
        // is no day counter (30171: months are 1/12 year wide). The legacy year*372 + month*31 + day
        // serials of the CIC operations, battle history and capital threat are a different scale,
        // persisted in the sidecar: never compare the two.
        public static int DaySerial(int day, int month, int year)
        {
            int y = month <= 2 ? year - 1 : year;
            int era = (y >= 0 ? y : y - 399) / 400;
            int yearOfEra = y - era * 400;
            int dayOfYear = (153 * ((month + 9) % 12) + 2) / 5 + day - 1;
            int dayOfEra = yearOfEra * 365 + yearOfEra / 4 - yearOfEra / 100 + dayOfYear;
            return era * 146097 + dayOfEra - FromMarchEpoch;
        }

        // The calendar date of a DaySerial: the inverse of the civil-date arithmetic above (Hinnant's civil_from_days over the March-based
        // year), so a month end and a leap day invert exactly. month is 1-12, as vanilla's monthCounter.
        public static void DateOf(int serial, out int year, out int month, out int day)
        {
            int z = serial + FromMarchEpoch;
            int era = (z >= 0 ? z : z - 146096) / 146097;
            int dayOfEra = z - era * 146097;
            int yearOfEra = (dayOfEra - dayOfEra / 1460 + dayOfEra / 36524 - dayOfEra / 146096) / 365;
            int dayOfYear = dayOfEra - (365 * yearOfEra + yearOfEra / 4 - yearOfEra / 100);
            int monthFromMarch = (5 * dayOfYear + 2) / 153;
            day = dayOfYear - (153 * monthFromMarch + 2) / 5 + 1;
            month = monthFromMarch < 10 ? monthFromMarch + 3 : monthFromMarch - 9;
            year = yearOfEra + era * 400 + (month <= 2 ? 1 : 0);
        }

        // The day of the month of a DaySerial.
        internal static int DayOfMonth(int serial)
        {
            DateOf(serial, out _, out _, out int day);
            return day;
        }

        // "yyyy-MM-dd": the date= field of the planner's telemetry rows (the legacy parser lifts it into campaignDate).
        internal static string FormatDate(int serial)
        {
            DateOf(serial, out int year, out int month, out int day);
            return year.ToString("D4", CultureInfo.InvariantCulture) + "-" + month.ToString("D2", CultureInfo.InvariantCulture)
                + "-" + day.ToString("D2", CultureInfo.InvariantCulture);
        }

        // A row with the review date appended. The planner's deduped rows compare their undated text and call this on the line they write,
        // so a row that did not change is not written again just because the day did.
        internal static string Dated(string row, int serial)
        {
            return row + " date=" + FormatDate(serial);
        }

        // The same with the date right after alliance=, where [Reserves] and [CampaignMoverIssue] write it: the foresight rows read alliance, then
        // date. A row with no alliance= field is dated at its end.
        internal static string DatedAfterAlliance(string row, int serial)
        {
            int at = row.IndexOf(" alliance=", StringComparison.Ordinal);
            if (at < 0) return Dated(row, serial);
            int end = row.IndexOf(' ', at + 1);
            if (end < 0) end = row.Length;
            return row.Substring(0, end) + " date=" + FormatDate(serial) + row.Substring(end);
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

        // Vanilla's chapter is known once Policy.CheckForChapterUpdate has run: Policy.CurrentChapter is
        // reset to -1 on frame 1 (29857), a loaded save restores it on frame 4 (29906-29908), and a new
        // campaign gets its first update on frame 50 (30146-30149). That method sets the chapter to 0 or
        // more unconditionally (211611), so a negative chapter means "not yet", never "no chapter". Every
        // objective is deactivated while it is -1 (IsDeactivated 178914), so the first review waits for it.
        public static bool ChapterKnown(int chapter) => chapter >= 0;

        // The wait for it is bounded. Every campaign scene reaches frame 50 (CampaignController.Awake
        // zeroes the frame, 29726) and BattleUnits.Update retries the update every
        // updatetimeforcampaigndata (79323), so the chapter should be known by frame 51; this limit only
        // keeps a campaign in which it never was from going without reviews. Readers clamp -1.
        public const int ChapterWaitLastFrame = 200;

        public static bool ChapterWaitOver(int chapter, int frame) => ChapterKnown(chapter) || frame > ChapterWaitLastFrame;

        public static bool SkippedAlternatingGate(int lastReviewSerial, int currentSerial)
        {
            return lastReviewSerial >= 0 && currentSerial - lastReviewSerial >= 2;
        }
    }
}
