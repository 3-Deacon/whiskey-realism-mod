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
            Doctrine("union-ohio", 0, "OhioValley", 0.9f, 0.5f, 0.15f, 0.3f,
                "OhioValley", "NorthwestVirginia", "MarylandPennsylvaniaCorridor")
        };

        private static readonly List<ArmyAreaDoctrine> Confederate = new List<ArmyAreaDoctrine>
        {
            Doctrine("csa-anv", 1, "VirginiaCapitalCorridor", 1.0f, 0.3f, 0.35f, 0.45f,
                "VirginiaCapitalCorridor", "ShenandoahValley", "MarylandPennsylvaniaCorridor", "WashingtonDefenses"),
            Doctrine("csa-northwest-va", 1, "NorthwestVirginia", 0.95f, 0.4f, 0.15f, 0.5f,
                "NorthwestVirginia", "ShenandoahValley")
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

        public static bool IsInactiveFullWarCommand(int allianceId, string unitName)
        {
            string name = Normalize(unitName);
            if (allianceId == 0)
            {
                return name.Contains("army of the tennessee") ||
                       name.Contains("army of west tennessee") ||
                       name.Contains("army of the cumberland") ||
                       name.Contains("army of the gulf") ||
                       name.Contains("army of west mississippi");
            }

            return name.Contains("army of tennessee") ||
                   name.Contains("army of mississippi") ||
                   name.Contains("army of the mississippi") ||
                   name.Contains("trans-mississippi") ||
                   name.Contains("army of the west") ||
                   name.Contains("missouri state guard") ||
                   name.Contains("army of louisiana") ||
                   name.Contains("army of western louisiana");
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
                case "union-ohio":
                    return normalizedUnitName.Contains("army of the ohio");
                case "csa-anv":
                    return normalizedUnitName.Contains("army of northern virginia") ||
                           normalizedUnitName.Contains("army of virginia") ||
                           normalizedUnitName.Contains("army of the potomac") ||
                           normalizedUnitName.Contains("army of the valley");
                case "csa-northwest-va":
                    return normalizedUnitName.Contains("army of the northwest") ||
                           normalizedUnitName.Contains("porterfield");
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
