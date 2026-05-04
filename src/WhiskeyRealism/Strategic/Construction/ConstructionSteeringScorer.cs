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
            if (output == null)
                return Decision(Clamp(fiscalMultiplier, 0.1f, 3f), "fiscal-only");

            var suppressed = SuppressionFor(output, buildingName);
            if (suppressed.HasValue)
                return Decision(0.1f, "suppressed:" + suppressed.Value);

            if (output.TopPrivateBuilding.Kind == ConstructionCandidateKind.PrivateBuilding &&
                output.TopPrivateBuilding.BuildingTypeId == buildingTypeId)
            {
                float scoreBoost = Clamp(1f + Math.Max(0f, output.TopPrivateBuilding.Score), 1.25f, 2.5f);
                return Decision(Clamp(fiscalMultiplier * scoreBoost, 0.1f, 3f), "ledger-top-private");
            }

            if (output.Posture == ConstructionPosture.EmergencyHold)
                return Decision(Clamp(fiscalMultiplier * 0.35f, 0.1f, 1f), "emergency-hold");

            if (output.Posture == ConstructionPosture.FieldSupply && IsLogisticsBuilding(buildingTypeId, buildingName))
                return Decision(Clamp(fiscalMultiplier * 1.25f, 0.1f, 2.5f), "field-supply-logistics");

            return Decision(Clamp(fiscalMultiplier, 0.1f, 3f), "fiscal-ledger-neutral");
        }

        private static ConstructionSuppressionReason? SuppressionFor(ConstructionOutput output, string buildingName)
        {
            if (output.Suppressions == null) return null;
            string wanted = Normalize(buildingName);
            for (int i = 0; i < output.Suppressions.Length; i++)
            {
                var suppression = output.Suppressions[i];
                if (suppression.Kind == ConstructionCandidateKind.PrivateBuilding &&
                    Normalize(suppression.Name) == wanted)
                    return suppression.Reason;
            }
            return null;
        }

        private static bool IsLogisticsBuilding(int buildingTypeId, string buildingName)
        {
            string normalized = Normalize(buildingName);
            return buildingTypeId == 13 ||
                normalized.Contains("market") ||
                normalized.Contains("hospital") ||
                normalized.Contains("bank");
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
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
