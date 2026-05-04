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

        public void CheckTransition(int currentMonth, int currentYear,
                                    bool vicksburgFallenEarly, bool atlantaFallenEarly)
        {
            EraStage target = Stage;

            if (currentYear >= 1862 && target < EraStage.Operational1862) target = EraStage.Operational1862;
            if (currentYear >= 1863 && target < EraStage.Decisive1863)    target = EraStage.Decisive1863;
            if (currentYear >= 1864 && target < EraStage.TotalWar1864)    target = EraStage.TotalWar1864;

            if (vicksburgFallenEarly && currentYear == 1863 && currentMonth < 7 && target < EraStage.Decisive1863)
                target = EraStage.Decisive1863;
            if (atlantaFallenEarly && currentYear == 1864 && currentMonth < 9 && target < EraStage.TotalWar1864)
                target = EraStage.TotalWar1864;

            if (target > Stage)
            {
                var prev = Stage;
                Stage = target;
                Plugin.Log.LogInfo($"[Era] advanced {prev} → {Stage} ({currentYear}-{currentMonth:D2})");
            }
        }
    }
}
