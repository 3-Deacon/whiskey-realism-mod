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
