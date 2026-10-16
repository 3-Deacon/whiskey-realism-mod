using WhiskeyRealism.Telemetry;

namespace WhiskeyRealism.Strategic
{
    public class EraStageManager
    {
        // Vanilla's chapter 3 is "Lincoln re-elected": Policy.CheckForChapterUpdate sets it once the
        // date is past 9 Nov 1864 with objective 26 accomplished and 27 not (211663-211667, 211676-211681).
        // The late-war stage is that chapter, not a calendar date: if the election is lost it never comes.
        public const int LateWarChapter = 3;

        // The one definition of "late war" for every Whiskey reader. An unknown chapter (-1, before
        // vanilla's frame-50 update) is not late war.
        public static bool IsLateWar(int chapter) => chapter >= LateWarChapter;

        public EraStage Stage;

        public PersonalityVector StageVector
        {
            get
            {
                switch (Stage)
                {
                    case EraStage.Amateur1861:     return new PersonalityVector(-0.3f, +0.5f, -0.2f, -0.4f, +0.1f);
                    case EraStage.Operational1862: return new PersonalityVector( 0f,    0f,   +0.1f,  0f,    0f);
                    case EraStage.Decisive1863:    return new PersonalityVector(+0.2f, -0.2f, +0.3f, +0.2f,  0f);
                    case EraStage.TotalWar1864:    return new PersonalityVector(+0.4f, -0.4f, +0.2f, +0.6f, -0.2f);
                    default:                       return default(PersonalityVector);
                }
            }
        }

        // Pure era for a date and the vanilla chapter (non-monotonic). Calendar years set the
        // stage up to Decisive1863 (Vicksburg falling before July 1863 pulls it early). No chapter
        // boundary coincides with those calendar boundaries (chapter 1 starts at the war, chapter 2
        // on 6 Nov 1862), so they keep their dates. TotalWar1864 is vanilla's chapter 3 only.
        public static EraStage ForDate(int currentMonth, int currentYear, bool vicksburgFallenEarly, int chapter)
        {
            EraStage target = EraStage.Amateur1861;
            if (currentYear >= 1862) target = EraStage.Operational1862;
            if (currentYear >= 1863) target = EraStage.Decisive1863;

            if (vicksburgFallenEarly && currentYear == 1863 && currentMonth < 7 && target < EraStage.Decisive1863)
                target = EraStage.Decisive1863;
            if (IsLateWar(chapter))
                target = EraStage.TotalWar1864;
            return target;
        }

        // Re-derives the stage from the date and chapter in either direction (used on campaign reset).
        public void ResetToDate(int currentMonth, int currentYear, int chapter)
        {
            Stage = ForDate(currentMonth, currentYear, false, chapter);
        }

        // A sidecar written before the late-war stage followed the chapter holds TotalWar1864 from
        // 1 Jan 1864. With the chapter known (0..2) that stage is not real: re-derive it. Chapter -1
        // is "not known yet": no change. Returns true when the stage was pulled back.
        public bool ResetStaleLateWar(int currentMonth, int currentYear, int chapter)
        {
            if (Stage != EraStage.TotalWar1864 || chapter < 0 || IsLateWar(chapter)) return false;
            var prev = Stage;
            ResetToDate(currentMonth, currentYear, chapter);
            TelemetryRouter.LegacyInfo($"[CampaignPace] action=era-regressed previous={prev} current={Stage} chapter={chapter} date={currentYear}-{currentMonth:D2}", TelemetryLayer.Campaign);
            return true;
        }

        // Within one campaign the era only moves forward.
        public void CheckTransition(int currentMonth, int currentYear,
                                    bool vicksburgFallenEarly, int chapter)
        {
            EraStage target = ForDate(currentMonth, currentYear, vicksburgFallenEarly, chapter);
            if (target > Stage)
            {
                var prev = Stage;
                Stage = target;
                TelemetryRouter.LegacyInfo($"[CampaignPace] action=era-advanced previous={prev} current={Stage} date={currentYear}-{currentMonth:D2}", TelemetryLayer.Campaign);
            }
        }
    }
}
