using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public static class HistoricalArmyAreaRegistry
    {
        private static readonly ArmyAreaDoctrine FallbackUnion = Doctrine(
            "union-default", 0, "Unassigned", 0.2f, 0.9f, 0.0f, 0.0f,
            "Unassigned");

        private static readonly ArmyAreaDoctrine FallbackConfederate = Doctrine(
            "csa-default", 1, "Unassigned", 0.2f, 0.9f, 0.0f, 0.0f,
            "Unassigned");

        private static readonly List<ArmyAreaDoctrine> Union = new List<ArmyAreaDoctrine>
        {
            Doctrine("union-aop", 0, "VirginiaCapitalCorridor", 1.0f, 0.35f, 0.25f, 0.35f,
                "VirginiaCapitalCorridor", "ShenandoahValley", "WashingtonDefenses", "MarylandPennsylvaniaCorridor"),
            Doctrine("union-shenandoah", 0, "ShenandoahValley", 0.9f, 0.45f, 0.2f, 0.25f,
                "ShenandoahValley", "VirginiaCapitalCorridor", "MarylandPennsylvaniaCorridor"),
            Doctrine("union-cumberland", 0, "TennesseeGeorgiaCorridor", 0.95f, 0.4f, 0.15f, 0.4f,
                "TennesseeGeorgiaCorridor", "CumberlandGapEastTennessee", "AtlantaCorridor"),
            Doctrine("union-tennessee", 0, "MississippiRiverCorridor", 1.0f, 0.45f, 0.35f, 0.2f,
                "MississippiRiverCorridor", "TennesseeGeorgiaCorridor", "AtlantaCorridor"),
            Doctrine("union-ohio", 0, "CumberlandGapEastTennessee", 0.9f, 0.5f, 0.15f, 0.3f,
                "CumberlandGapEastTennessee", "KentuckyTennesseeCorridor", "TennesseeGeorgiaCorridor"),
            Doctrine("union-gulf", 0, "GulfCoastLowerMississippi", 0.9f, 0.55f, 0.2f, 0.2f,
                "GulfCoastLowerMississippi", "MississippiRiverCorridor", "TransMississippi")
        };

        private static readonly List<ArmyAreaDoctrine> Confederate = new List<ArmyAreaDoctrine>
        {
            Doctrine("csa-anv", 1, "VirginiaCapitalCorridor", 1.0f, 0.3f, 0.35f, 0.45f,
                "VirginiaCapitalCorridor", "ShenandoahValley", "MarylandPennsylvaniaCorridor", "WashingtonDefenses"),
            Doctrine("csa-tennessee", 1, "TennesseeGeorgiaCorridor", 1.0f, 0.35f, 0.25f, 0.45f,
                "TennesseeGeorgiaCorridor", "KentuckyTennesseeCorridor", "AtlantaCorridor", "MississippiRiverCorridor"),
            Doctrine("csa-mississippi", 1, "MississippiRiverCorridor", 0.9f, 0.45f, 0.2f, 0.35f,
                "MississippiRiverCorridor", "TennesseeGeorgiaCorridor", "GulfCoastLowerMississippi"),
            Doctrine("csa-transmiss", 1, "TransMississippi", 0.95f, 0.55f, 0.15f, 0.25f,
                "TransMississippi", "MissouriArkansasCorridor", "GulfCoastLowerMississippi"),
            Doctrine("csa-gulf", 1, "GulfCoastLowerMississippi", 0.8f, 0.6f, 0.1f, 0.3f,
                "GulfCoastLowerMississippi", "MississippiRiverCorridor", "TransMississippi")
        };

        public static ArmyAreaDoctrine Resolve(int allianceId, string unitName, string commanderName = null)
        {
            string name = Normalize(unitName);
            var doctrines = allianceId == 0 ? Union : Confederate;

            for (int i = 0; i < doctrines.Count; i++)
            {
                if (Matches(doctrines[i].DoctrineId, name))
                    return doctrines[i];
            }

            return allianceId == 0 ? FallbackUnion : FallbackConfederate;
        }

        private static bool Matches(string doctrineId, string normalizedUnitName)
        {
            switch (doctrineId)
            {
                case "union-aop":
                    return normalizedUnitName.Contains("army of the potomac") ||
                           normalizedUnitName.Contains("army of virginia");
                case "union-shenandoah":
                    return normalizedUnitName.Contains("shenandoah");
                case "union-cumberland":
                    return normalizedUnitName.Contains("army of the cumberland");
                case "union-tennessee":
                    return normalizedUnitName.Contains("army of the tennessee") ||
                           normalizedUnitName.Contains("army of west tennessee");
                case "union-ohio":
                    return normalizedUnitName.Contains("army of the ohio");
                case "union-gulf":
                    return normalizedUnitName.Contains("army of the gulf") ||
                           normalizedUnitName.Contains("army of west mississippi");
                case "csa-anv":
                    return normalizedUnitName.Contains("army of northern virginia") ||
                           normalizedUnitName.Contains("army of virginia") ||
                           normalizedUnitName.Contains("army of the potomac") ||
                           normalizedUnitName.Contains("army of the valley");
                case "csa-tennessee":
                    return normalizedUnitName.Contains("army of tennessee");
                case "csa-mississippi":
                    return normalizedUnitName.Contains("army of mississippi") ||
                           normalizedUnitName.Contains("army of the mississippi");
                case "csa-transmiss":
                    return normalizedUnitName.Contains("trans-mississippi") ||
                           normalizedUnitName.Contains("army of the west") ||
                           normalizedUnitName.Contains("missouri state guard");
                case "csa-gulf":
                    return normalizedUnitName.Contains("army of louisiana") ||
                           normalizedUnitName.Contains("army of western louisiana");
                default:
                    return false;
            }
        }

        private static ArmyAreaDoctrine Doctrine(string id, int allianceId, string primary, float historicalWeight,
            float flexibility, float offensiveBias, float defensiveBias, params string[] preferred)
        {
            return new ArmyAreaDoctrine
            {
                DoctrineId = id,
                AllianceId = allianceId,
                PrimaryAreaKey = primary,
                PreferredAreaKeys = new List<string>(preferred),
                HistoricalWeight = historicalWeight,
                Flexibility = flexibility,
                OffensiveBias = offensiveBias,
                DefensiveBias = defensiveBias
            };
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant();
        }
    }
}
