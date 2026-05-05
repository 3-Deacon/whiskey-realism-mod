namespace WhiskeyRealism.Strategic
{
    public static class ArmyAreaClassifier
    {
        public static string FromPosition(float x, float z)
        {
            if (x > 1420f && z < -1120f) return "CoastalCarolinaVirginia";
            if (x > 1225f && z > -725f) return "WashingtonDefenses";
            if (x > 950f && x < 1225f && z > -760f && z < -400f) return "ShenandoahValley";
            if (x > 300f && x < 850f && z > -850f && z < -450f) return "NorthwestVirginia";
            if (x > 1150f && z > -450f) return "MarylandPennsylvaniaCorridor";
            if (x > 1050f && z < -850f) return "VirginiaCapitalCorridor";
            if (z < -1250f) return "CarolinaInterior";
            return "OhioValley";
        }
    }
}
