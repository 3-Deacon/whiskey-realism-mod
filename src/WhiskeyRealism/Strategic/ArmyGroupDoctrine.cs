using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public sealed class ArmyGroupPlan
    {
        public string AreaKey;
        public List<string> UnitKeys = new List<string>();
    }

    public sealed class ArmyGroupCommanderPreference
    {
        public int AllianceId;
        public string AreaKey;
        public List<string> PreferredLastNames = new List<string>();
    }

    public static class ArmyGroupDoctrine
    {
        public static List<ArmyGroupPlan> PlanGroups(ArmyAreaLedger ledger, int minimumUnitsPerGroup = 2)
        {
            var result = new List<ArmyGroupPlan>();
            if (ledger == null || minimumUnitsPerGroup < 2) return result;

            var byArea = new Dictionary<string, ArmyGroupPlan>(StringComparer.OrdinalIgnoreCase);
            foreach (var assignment in ledger.Assignments)
            {
                if (assignment == null || assignment.OutOfArea) continue;
                if (assignment.Behavior == ArmyAreaBehavior.Recover) continue;
                string area = assignment.Doctrine?.PrimaryAreaKey;
                if (string.IsNullOrEmpty(area) || area == "Unassigned") continue;

                if (!byArea.TryGetValue(area, out var plan))
                {
                    plan = new ArmyGroupPlan { AreaKey = area };
                    byArea[area] = plan;
                }
                plan.UnitKeys.Add(assignment.UnitKey);
            }

            foreach (var plan in byArea.Values)
                if (plan.UnitKeys.Count >= minimumUnitsPerGroup)
                    result.Add(plan);

            result.Sort((a, b) => string.CompareOrdinal(a.AreaKey, b.AreaKey));
            return result;
        }

        public static ArmyGroupCommanderPreference ResolveCommanderPreference(int allianceId, string areaKey)
        {
            var preference = new ArmyGroupCommanderPreference
            {
                AllianceId = allianceId,
                AreaKey = areaKey
            };

            switch ((allianceId == 1 ? "csa:" : "union:") + (areaKey ?? string.Empty))
            {
                case "csa:VirginiaCapitalCorridor":
                    preference.PreferredLastNames.AddRange(new[] { "Lee", "Johnston", "Beauregard", "Longstreet" });
                    break;
                case "csa:TennesseeGeorgiaCorridor":
                    preference.PreferredLastNames.AddRange(new[] { "Johnston", "Bragg", "Hood", "Hardee" });
                    break;
                case "csa:MississippiRiverCorridor":
                    preference.PreferredLastNames.AddRange(new[] { "Pemberton", "Johnston", "Polk" });
                    break;
                case "csa:TransMississippi":
                    preference.PreferredLastNames.AddRange(new[] { "Kirby Smith", "Price", "Magruder" });
                    break;
                case "union:VirginiaCapitalCorridor":
                    preference.PreferredLastNames.AddRange(new[] { "McClellan", "Meade", "Grant", "Hooker" });
                    break;
                case "union:MississippiRiverCorridor":
                    preference.PreferredLastNames.AddRange(new[] { "Grant", "Sherman", "McPherson" });
                    break;
                case "union:TennesseeGeorgiaCorridor":
                    preference.PreferredLastNames.AddRange(new[] { "Rosecrans", "Thomas", "Sherman" });
                    break;
                case "union:CumberlandGapEastTennessee":
                    preference.PreferredLastNames.AddRange(new[] { "Buell", "Burnside", "Thomas" });
                    break;
                case "union:GulfCoastLowerMississippi":
                    preference.PreferredLastNames.AddRange(new[] { "Banks", "Butler", "Canby" });
                    break;
            }

            return preference;
        }
    }
}
