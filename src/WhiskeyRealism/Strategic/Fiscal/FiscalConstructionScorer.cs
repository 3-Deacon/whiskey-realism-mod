namespace WhiskeyRealism.Strategic.Fiscal
{
    public static class FiscalConstructionScorer
    {
        public static float Multiplier(FiscalOutput intent, int alliance, string buildingName, int subsidyType)
        {
            if (intent == null || string.IsNullOrEmpty(buildingName)) return 1f;

            string name = buildingName.ToLowerInvariant();
            float mult = 1f;

            if (name.Contains("bank") && intent.Posture <= FiscalPosture.BalancedWar)
                mult += alliance == 1 ? 0.60f : 0.35f;
            if ((name.Contains("market") || name.Contains("rail") || name.Contains("depot")) &&
                (intent.SupplyProtection || intent.LogisticsExpansion))
                mult += 0.75f;
            if (name.Contains("hospital") && intent.SupplyProtection)
                mult += 0.35f;
            if ((name.Contains("shipyard") || name.Contains("naval")) && alliance == 1 &&
                intent.Posture >= FiscalPosture.CreditDefense)
                mult -= 0.50f;
            if ((name.Contains("factory") || name.Contains("foundry") || name.Contains("industrial")) &&
                intent.Posture == FiscalPosture.EmergencySolvency)
                mult -= 0.45f;

            return mult < 0.15f ? 0.15f : mult;
        }

        // Applies director posture biases to an already-computed fiscal multiplier.
        // Supply/logistics adjustments are additive; expansion damper is multiplicative.
        // Returns baseMultiplier unchanged when posture is null.
        public static float ApplyDirectorBiases(float baseMultiplier, string buildingName, DirectorPosture posture)
        {
            if (posture == null || string.IsNullOrEmpty(buildingName)) return baseMultiplier;
            string name = buildingName.ToLowerInvariant();

            float mult = baseMultiplier;
            if (IsSupplyOrRecovery(name))  mult += posture.SupplyConstructionBias;
            if (IsLogisticsBuilding(name)) mult += posture.LogisticsBias;
            if (IsPrivateExpansion(name))  mult *= System.Math.Max(0.05f, 1f - posture.ExpansionDamper);

            return System.Math.Max(0f, mult);
        }

        private static bool IsSupplyOrRecovery(string lowerName)
        {
            return lowerName.Contains("depot") || lowerName.Contains("hospital");
        }

        private static bool IsLogisticsBuilding(string lowerName)
        {
            return lowerName.Contains("market") || lowerName.Contains("rail");
        }

        private static bool IsPrivateExpansion(string lowerName)
        {
            return lowerName.Contains("bank") ||
                   lowerName.Contains("factory") ||
                   lowerName.Contains("foundry") ||
                   lowerName.Contains("industrial") ||
                   lowerName.Contains("shipyard") ||
                   lowerName.Contains("naval");
        }
    }
}
