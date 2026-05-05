using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public static class DynamicArmyAreaDoctrine
    {
        public static bool IsDynamic(ArmyAreaDoctrine doctrine)
        {
            return doctrine != null &&
                   doctrine.DoctrineId != null &&
                   doctrine.DoctrineId.Contains("-dynamic-");
        }

        public static ArmyAreaDoctrine FromCurrentArea(int allianceId, string currentAreaKey)
        {
            string area = string.IsNullOrEmpty(currentAreaKey) ? "OhioValley" : currentAreaKey;
            var preferred = new List<string> { area };
            AddAdjacent(preferred, area);

            bool confederate = allianceId == 1;
            return new ArmyAreaDoctrine
            {
                DoctrineId = (confederate ? "csa" : "union") + "-dynamic-" + area,
                AllianceId = allianceId,
                PrimaryAreaKey = area,
                PreferredAreaKeys = preferred,
                HistoricalWeight = 0.35f,
                Flexibility = 0.85f,
                OffensiveBias = confederate ? 0.2f : 0.3f,
                DefensiveBias = confederate ? 0.45f : 0.3f
            };
        }

        private static void AddAdjacent(List<string> preferred, string area)
        {
            switch (area)
            {
                case "NorthwestVirginia":
                    Add(preferred, "ShenandoahValley");
                    Add(preferred, "OhioValley");
                    break;
                case "ShenandoahValley":
                    Add(preferred, "NorthwestVirginia");
                    Add(preferred, "VirginiaCapitalCorridor");
                    Add(preferred, "MarylandPennsylvaniaCorridor");
                    break;
                case "VirginiaCapitalCorridor":
                    Add(preferred, "ShenandoahValley");
                    Add(preferred, "WashingtonDefenses");
                    Add(preferred, "CoastalCarolinaVirginia");
                    break;
                case "WashingtonDefenses":
                    Add(preferred, "MarylandPennsylvaniaCorridor");
                    Add(preferred, "VirginiaCapitalCorridor");
                    break;
                case "MarylandPennsylvaniaCorridor":
                    Add(preferred, "WashingtonDefenses");
                    Add(preferred, "ShenandoahValley");
                    Add(preferred, "OhioValley");
                    break;
                case "CoastalCarolinaVirginia":
                    Add(preferred, "VirginiaCapitalCorridor");
                    Add(preferred, "CarolinaInterior");
                    break;
                case "CarolinaInterior":
                    Add(preferred, "CoastalCarolinaVirginia");
                    Add(preferred, "VirginiaCapitalCorridor");
                    break;
                default:
                    Add(preferred, "NorthwestVirginia");
                    Add(preferred, "MarylandPennsylvaniaCorridor");
                    break;
            }
        }

        private static void Add(List<string> values, string value)
        {
            if (!values.Contains(value)) values.Add(value);
        }
    }
}
