using System;

namespace WhiskeyRealism.Strategic.Construction
{
    public struct ConstructionSteeringDecision
    {
        public float Multiplier;
        public string Reason;
    }

    public static class ConstructionSteeringScorer
    {
        public static ConstructionSteeringDecision DecidePrivateMultiplier(
            ConstructionOutput output,
            int buildingTypeId,
            string buildingName,
            float fiscalMultiplier)
        {
            fiscalMultiplier = FiniteOrDefault(fiscalMultiplier, 1f);

            if (output == null)
                return Decision(Clamp(fiscalMultiplier, 0.1f, 3f), "fiscal-only");

            var suppressed = SuppressionFor(output, buildingTypeId, buildingName);
            if (suppressed.HasValue)
                return Decision(0.1f, "suppressed:" + suppressed.Value);

            if (output.TopPrivateBuilding.Kind == ConstructionCandidateKind.PrivateBuilding &&
                output.TopPrivateBuilding.BuildingTypeId == buildingTypeId)
            {
                float topScore = FiniteOrDefault(output.TopPrivateBuilding.Score, 0f);
                float scoreBoost = Clamp(1f + Math.Max(0f, topScore), 1.25f, 2.5f);
                return Decision(Clamp(fiscalMultiplier * scoreBoost, 0.1f, 3f), "ledger-top-private");
            }

            if (output.Posture == ConstructionPosture.EmergencyHold)
                return Decision(Clamp(fiscalMultiplier * 0.35f, 0.1f, 1f), "emergency-hold");

            if (output.Posture == ConstructionPosture.FieldSupply && IsLogisticsBuilding(buildingTypeId, buildingName))
                return Decision(Clamp(fiscalMultiplier * 1.25f, 0.1f, 2.5f), "field-supply-logistics");

            return Decision(Clamp(fiscalMultiplier, 0.1f, 3f), "fiscal-ledger-neutral");
        }

        private static ConstructionSuppressionReason? SuppressionFor(ConstructionOutput output, int buildingTypeId, string buildingName)
        {
            if (output.Suppressions == null) return null;
            string wanted = Normalize(buildingName);
            for (int i = 0; i < output.Suppressions.Length; i++)
            {
                var suppression = output.Suppressions[i];
                if (suppression.Kind != ConstructionCandidateKind.PrivateBuilding)
                    continue;

                if (suppression.BuildingTypeId >= 0 && suppression.BuildingTypeId == buildingTypeId)
                    return suppression.Reason;

                if (suppression.BuildingTypeId == ConstructionSuppression.MissingBuildingTypeId &&
                    Normalize(suppression.Name) == wanted)
                    return suppression.Reason;
            }
            return null;
        }

        // Applies director posture biases to an already-computed multiplier.
        // Supply/logistics adjustments are additive; expansion damper is multiplicative.
        // Returns baseMultiplier unchanged when posture is null.
        public static float ApplyDirectorBiases(
            float baseMultiplier,
            int buildingTypeId,
            string buildingName,
            DirectorPosture posture)
        {
            if (posture == null) return baseMultiplier;
            string name = Normalize(buildingName);

            float mult = baseMultiplier;
            if (IsSupplyOrRecovery(name))              mult += posture.SupplyConstructionBias;
            if (IsLogisticsBuilding(buildingTypeId, name)) mult += posture.LogisticsBias;
            if (IsPrivateExpansion(name))              mult *= System.Math.Max(0.05f, 1f - posture.ExpansionDamper);

            return System.Math.Max(0f, mult);
        }

        private static bool IsSupplyOrRecovery(string normalizedName)
        {
            return normalizedName.Contains("depot") || normalizedName.Contains("hospital");
        }

        private static bool IsPrivateExpansion(string normalizedName)
        {
            return normalizedName.Contains("bank") ||
                   normalizedName.Contains("factory") ||
                   normalizedName.Contains("foundry") ||
                   normalizedName.Contains("industrial") ||
                   normalizedName.Contains("shipyard") ||
                   normalizedName.Contains("naval");
        }

        private static bool IsLogisticsBuilding(int buildingTypeId, string buildingName)
        {
            string normalized = Normalize(buildingName);
            return buildingTypeId == 13 ||
                normalized.Contains("market") ||
                normalized.Contains("hospital");
        }

        private static float FiniteOrDefault(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.Trim().ToLowerInvariant();
        }

        private static ConstructionSteeringDecision Decision(float multiplier, string reason)
        {
            return new ConstructionSteeringDecision
            {
                Multiplier = multiplier,
                Reason = reason
            };
        }

        private static float Clamp(float value, float min, float max)
        {
            if (float.IsNaN(value)) return min;
            if (float.IsNegativeInfinity(value)) return min;
            if (float.IsPositiveInfinity(value)) return max;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
