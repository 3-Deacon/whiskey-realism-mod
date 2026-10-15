using WhiskeyRealism.Telemetry;

namespace WhiskeyRealism.Strategic
{
    public class EraStageManager
    {
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

        // Pure era for a date (non-monotonic). Calendar years set the stage; Vicksburg falling
        // before July 1863 / Atlanta before September 1864 pull the next stage early.
        public static EraStage ForDate(int currentMonth, int currentYear, bool vicksburgFallenEarly, bool atlantaFallenEarly)
        {
            EraStage target = EraStage.Amateur1861;
            if (currentYear >= 1862) target = EraStage.Operational1862;
            if (currentYear >= 1863) target = EraStage.Decisive1863;
            if (currentYear >= 1864) target = EraStage.TotalWar1864;

            if (vicksburgFallenEarly && currentYear == 1863 && currentMonth < 7 && target < EraStage.Decisive1863)
                target = EraStage.Decisive1863;
            if (atlantaFallenEarly && currentYear == 1864 && currentMonth < 9 && target < EraStage.TotalWar1864)
                target = EraStage.TotalWar1864;
            return target;
        }

        // Re-derives the stage from the date in either direction (used on campaign reset).
        public void ResetToDate(int currentMonth, int currentYear)
        {
            Stage = ForDate(currentMonth, currentYear, false, false);
        }

        // Within one campaign the era only moves forward.
        public void CheckTransition(int currentMonth, int currentYear,
                                    bool vicksburgFallenEarly, bool atlantaFallenEarly)
        {
            EraStage target = ForDate(currentMonth, currentYear, vicksburgFallenEarly, atlantaFallenEarly);
            if (target > Stage)
            {
                var prev = Stage;
                Stage = target;
                TelemetryRouter.LegacyInfo($"[CampaignPace] action=era-advanced previous={prev} current={Stage} date={currentYear}-{currentMonth:D2}", TelemetryLayer.Campaign);
            }
        }
    }
}
