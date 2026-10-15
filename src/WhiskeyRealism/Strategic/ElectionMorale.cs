using System;

namespace WhiskeyRealism.Strategic
{
    // The 1864 election as the Union's morale need sees it. Vanilla objective 26 ("Maintain Morale
    // Above 50", Union, chapter 2 only) reads national morale against GamePrefs.lincolnmaintainmoraleabove
    // (0.50) while the date is before 9 Nov 1864 + one day (CampaignObjective 179386-179418); morale
    // under the line then costs the Union lincolninfluenceunionmorale, and 4 Mar 1865 ends the
    // campaign (179408-179412). Chapter 3 (Lincoln re-elected) only comes when the line was met
    // (CheckForChapterUpdate 211663-211667). A linear 1 - morale need has no deadline in it: this
    // adds the shortfall to the line, scaled by how close the verdict day is. Pure; own and public
    // values only (national morale is public UI).
    public static class ElectionMorale
    {
        public const int ObjectiveId = 26;
        public const int UnionAlliance = 0;

        // The last day vanilla measures morale (the +0.002739726 yr in 179393 is one day).
        public const int VerdictDay = 9;
        public const int VerdictMonth = 11;
        public const int VerdictYear = 1864;

        // Pressure opens about 1 June (150 days out) and reaches full strength on the verdict day.
        public const int RampDays = 150;

        // Morale just on the line is still one bad week from the cliff; the boost covers this much above it.
        public const float SafetyMargin = 0.05f;

        // No verdict ahead: the day is past, the date unreadable, or the objective is not live.
        public const int NoVerdictAhead = -1;

        // Whole days from the campaign date to the verdict day (0 on the day itself), or
        // NoVerdictAhead after it or for a date that does not exist.
        public static int DaysToVerdict(int day, int month, int year)
        {
            if (year < 1 || year > 9999 || month < 1 || month > 12) return NoVerdictAhead;
            if (day < 1 || day > DateTime.DaysInMonth(year, month)) return NoVerdictAhead;
            int days = (int)(new DateTime(VerdictYear, VerdictMonth, VerdictDay) - new DateTime(year, month, day)).TotalDays;
            return days < 0 ? NoVerdictAhead : days;
        }

        // The extra morale need in [0, 1]: the shortfall to the line (plus the margin) as a share
        // of the line, times how far the ramp has run.
        public static float Boost(float morale, float threshold, int daysToVerdict)
        {
            if (daysToVerdict < 0) return 0f;
            if (!IsFinite(morale) || !IsFinite(threshold) || threshold <= 0f) return 0f;
            float shortfall = Clamp01((threshold + SafetyMargin - morale) / threshold);
            float urgency = 1f - Clamp01(daysToVerdict / (float)RampDays);
            return shortfall * urgency;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static float Clamp01(float value) => value < 0f ? 0f : (value > 1f ? 1f : value);
    }
}
