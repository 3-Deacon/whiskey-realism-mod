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

    public enum ArmyGroupPlanAction { None, AttachToOwn, AppointOwnOnly, AttachToArea, CreateNew }

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
                case "csa:NorthwestVirginia":
                    preference.PreferredLastNames.AddRange(new[] { "Garnett", "Porterfield", "Lee" });
                    break;
                case "union:VirginiaCapitalCorridor":
                    preference.PreferredLastNames.AddRange(new[] { "McClellan", "Meade", "Grant", "Hooker" });
                    break;
                case "union:OhioValley":
                    preference.PreferredLastNames.AddRange(new[] { "McClellan", "Rosecrans", "Buell" });
                    break;
            }

            return preference;
        }

        // Repeat of vanilla CheckArmyGroupManagement's early return (decompile
        // 17705-17723): !GameVars.allowarmygroups || !alliance.AllowsArmyGroups()
        // (projects 88/89, 62545) || the CIC test. vanillaCicGuardBlocks is that CIC
        // test as vanilla applies it this pass: with #70 (BUG-WL-001) only for the
        // W&L CIC player's own alliance (CicFactionAiGuard.VanillaGuardBlocks).
        public static bool ArmyGroupsAllowed(bool allowArmyGroups, bool allianceAllowsArmyGroups, bool vanillaCicGuardBlocks)
        {
            if (!allowArmyGroups || !allianceAllowsArmyGroups) return false;
            return !vanillaCicGuardBlocks;
        }

        // Full combinedname (Config/commanders.txt) for historical surnames that
        // collide inside one alliance: 6 Lees, 4 Johnstons, 3 Shermans, 2 Grants,
        // 3 Garnetts. Keyed "<alliance>:<surname>".
        private static readonly Dictionary<string, string> CanonicalCommanderNames =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "1:lee", "Robert E. Lee" },
                { "1:johnston", "Joseph E. Johnston" },
                { "1:bragg", "Braxton Bragg" },
                { "1:hood", "John B. Hood" },
                { "1:beauregard", "P.G.T. Beauregard" },
                { "1:longstreet", "James Longstreet" },
                { "1:garnett", "Robert S. Garnett" },
                { "1:porterfield", "George A. Porterfield" },
                { "0:mcclellan", "George B. McClellan" },
                { "0:burnside", "Ambrose E. Burnside" },
                { "0:hooker", "Joseph Hooker" },
                { "0:meade", "George G. Meade" },
                { "0:grant", "Ulysses S. Grant" },
                { "0:sherman", "William T. Sherman" },
                { "0:sheridan", "Philip H. Sheridan" },
                { "0:rosecrans", "William Rosecrans" },
                { "0:buell", "Don Carlos Buell" },
            };

        public static string CanonicalCommanderName(int allianceId, string lastName)
        {
            string key = allianceId + ":" + NormalizeName(lastName);
            return CanonicalCommanderNames.TryGetValue(key, out var full) ? full : null;
        }

        // Exact match on the catalogued historical figure when the surname is
        // ambiguous; otherwise the last name token must equal the surname
        // (never a substring: "Lee" must not hit "Fitzhugh Lee" or "Leonidas").
        public static bool CommanderNameMatches(int allianceId, string preferredLastName, string combinedName)
        {
            string name = NormalizeName(combinedName);
            string want = NormalizeName(preferredLastName);
            if (name.Length == 0 || want.Length == 0) return false;

            string canonical = CanonicalCommanderName(allianceId, want);
            if (canonical != null) return name == NormalizeName(canonical);

            int space = name.LastIndexOf(' ');
            string last = space >= 0 ? name.Substring(space + 1) : name;
            return last == want;
        }

        private static string NormalizeName(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var parts = value.Replace(".", " ").ToLowerInvariant()
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts);
        }

        // vanillaCanBePicked = Commander.CanBePickedForAIReplacement(alliance,
        // includeofficerswithcommand: true) (decompile 60748): same alliance,
        // IsAvailable (alive / not captured / not wounded), not serving under the
        // W&L player, not on a staff, not already an army-group commander, not
        // the alliance CIC. The W&L avatar is excluded explicitly too.
        public static bool IsEligibleArmyGroupCommander(bool vanillaCanBePicked, bool isWlChosenCommander, bool commandsMemberOrNothing)
        {
            return vanillaCanBePicked && !isWlChosenCommander && commandsMemberOrNothing;
        }

        // vanilla ArmyGroup.GetArmyGroupInRange (75631) never offers the W&L
        // player's own group; the patch neither attaches to it nor re-commands it.
        public static bool IsPlayerArmyGroup(bool wlScenarioActive, int groupCommanderId, int chosenCommanderId)
        {
            return wlScenarioActive && groupCommanderId == chosenCommanderId;
        }

        // "Which group do the plan's units attach to" and "which group's commander
        // changes" are separate: the commander changes only on a group that holds
        // at least one plan unit. A full own group (CanAddFurtherUnits false,
        // 75770) gets no new units but keeps the appointment; capacity never
        // diverts the appointment onto an unrelated group found in range.
        public static ArmyGroupPlanAction ResolveArmyGroupPlanAction(bool ownGroupFound, bool ownGroupHasRoom, bool areaGroupFound)
        {
            if (ownGroupFound)
                return ownGroupHasRoom ? ArmyGroupPlanAction.AttachToOwn : ArmyGroupPlanAction.AppointOwnOnly;
            return areaGroupFound ? ArmyGroupPlanAction.AttachToArea : ArmyGroupPlanAction.CreateNew;
        }

        public static bool ShouldAppointAfter(ArmyGroupPlanAction action, int attachedCount)
        {
            switch (action)
            {
                case ArmyGroupPlanAction.AttachToOwn:
                case ArmyGroupPlanAction.AppointOwnOnly:
                case ArmyGroupPlanAction.CreateNew:
                    return true;
                case ArmyGroupPlanAction.AttachToArea:
                    return attachedCount > 0;
                default:
                    return false;
            }
        }
    }
}
