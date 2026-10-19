using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic.Planner;

namespace WhiskeyRealism.Strategic
{
    // One planner sector's top formations the patch should hold in one army group.
    public sealed class ArmyGroupPlan
    {
        public int SectorId;
        public List<int> InstanceIds = new List<int>();
    }

    public enum ArmyGroupPlanAction { None, AttachToOwn, AttachToArea, CreateNew }

    public static class ArmyGroupDoctrine
    {
        // The allocation's formations grouped by sector: every assignment with a sector except the ones tasked
        // singly (Recover, Recon, DepotBuilder, Redeploy: it marches elsewhere), a group of at least minimumUnitsPerGroup, in sector order.
        // A Hold covering-force member (D11 requirement-redeploy-role, foresight F6b-2) is grouped by the sector it stands in, not by the
        // sector that reserved it (its Assignment.SectorId, where it is counted): it holds its post, within arrival range of the point
        // its assignment targets, so that point's sector (sectors.SectorAt) is the one it stands in. Without a sector model it keeps the
        // assignment's sector.
        public static List<ArmyGroupPlan> PlanGroups(Allocation allocation, int minimumUnitsPerGroup = 2, SectorModel sectors = null)
        {
            var result = new List<ArmyGroupPlan>();
            if (allocation == null || minimumUnitsPerGroup < 2) return result;

            var bySector = new Dictionary<int, ArmyGroupPlan>();
            foreach (var assignment in allocation.Assignments)
            {
                if (assignment.SectorId < 0) continue;
                if (assignment.Role == FormationRole.Recover || assignment.Role == FormationRole.Recon || assignment.Role == FormationRole.DepotBuilder
                    || assignment.Role == FormationRole.Redeploy) continue;
                int sector = assignment.SectorId;
                if (sectors != null && ForceAllocator.IsCoveringMember(in assignment))
                {
                    int standing = sectors.SectorAt(assignment.TargetX, assignment.TargetZ);
                    if (standing >= 0) sector = standing;
                }
                if (!bySector.TryGetValue(sector, out var plan))
                {
                    plan = new ArmyGroupPlan { SectorId = sector };
                    bySector[sector] = plan;
                }
                plan.InstanceIds.Add(assignment.InstanceId);
            }

            foreach (var plan in bySector.Values)
                if (plan.InstanceIds.Count >= minimumUnitsPerGroup)
                    result.Add(plan);

            result.Sort((a, b) => a.SectorId.CompareTo(b.SectorId));
            return result;
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

        // vanilla ArmyGroup.GetArmyGroupInRange (75631) never offers the W&L
        // player's own group; the patch neither attaches to it nor re-commands it.
        public static bool IsPlayerArmyGroup(bool wlScenarioActive, int groupCommanderId, int chosenCommanderId)
        {
            return wlScenarioActive && groupCommanderId == chosenCommanderId;
        }

        // Which group the sector's formations join: the army group already holding one of them (while it has room),
        // else one in range of the sector, else a new one. A full own group takes no more (CanAddFurtherUnits, 75770)
        // and nothing else is done for the sector: appointing its commander belongs to the appointment executor.
        public static ArmyGroupPlanAction ResolveArmyGroupPlanAction(bool ownGroupFound, bool ownGroupHasRoom, bool areaGroupFound)
        {
            if (ownGroupFound)
                return ownGroupHasRoom ? ArmyGroupPlanAction.AttachToOwn : ArmyGroupPlanAction.None;
            return areaGroupFound ? ArmyGroupPlanAction.AttachToArea : ArmyGroupPlanAction.CreateNew;
        }
    }
}
