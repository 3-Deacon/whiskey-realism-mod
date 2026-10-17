using System;
using System.Collections.Generic;
using System.Globalization;

namespace WhiskeyRealism.Strategic.Planner
{
    // Who is re-armed first (plan G9, decision 1). Vanilla's AICampaign.CheckWeaponUpgrades (16080) walks the alliance's unit list in
    // scene-hierarchy order and gives each unit the best model in stock, so whoever stands first takes the scarce new ones
    // (UpgradeWeaponForUnit 16155, GetBestWeapon 16224). The patch reorders that list before the call, by the allocation role of the
    // campaign group each unit belongs to, and writes vanilla's order back after it; vanilla's own per-unit rule, stock checks and
    // W&L skip run unchanged. Pure: the runtime (OrdnanceRuntime) reads the units and the allocation.
    public static class RearmPriority
    {
        // The ranks, best first. Recover formations and formations in transit are re-armed last: they are not at the front.
        private const int MainEffortRank = 0, SupportRank = 1, ReserveRank = 2, HoldRank = 3, CapitalGuardRank = 4;
        private const int DepotBuilderRank = 5, ReconRank = 6, GarrisonRank = 7;
        public const int UnassignedRank = 8;
        public const int LastRank = 9;

        // The rank of a unit whose campaign group holds `role` in the allocation (null: the allocation does not name the group).
        public static int RankOf(FormationRole? role, bool inTransit)
        {
            if (inTransit) return LastRank;
            if (role == null) return UnassignedRank;
            switch (role.Value)
            {
                case FormationRole.MainEffort: return MainEffortRank;
                case FormationRole.Support: return SupportRank;
                case FormationRole.Reserve: return ReserveRank;
                case FormationRole.Hold: return HoldRank;
                case FormationRole.CapitalGuard: return CapitalGuardRank;
                case FormationRole.DepotBuilder: return DepotBuilderRank;
                case FormationRole.Recon: return ReconRank;
                case FormationRole.Garrison: return GarrisonRank;
                case FormationRole.Recover: return LastRank;
                default: throw new ArgumentOutOfRangeException(nameof(role), role, "a role the re-arming order does not rank");
            }
        }

        // The indexes of `ranks` in ascending rank, ties kept in index order (a stable order).
        public static int[] StableOrder(int[] ranks)
        {
            var order = new int[ranks.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) => ranks[a] != ranks[b] ? ranks[a].CompareTo(ranks[b]) : a.CompareTo(b));
            return order;
        }

        // Reorders `list` in place by `ranks` (one per element) and returns vanilla's order, to be given back to Restore.
        public static T[] Reorder<T>(List<T> list, int[] ranks)
        {
            if (ranks.Length != list.Count) throw new ArgumentException("one rank per list element", nameof(ranks));
            var saved = list.ToArray();
            int[] order = StableOrder(ranks);
            for (int i = 0; i < order.Length; i++) list[i] = saved[order[i]];
            return saved;
        }

        // Writes vanilla's order back into the same list object. False (the list is left as it is) when its length changed since
        // Reorder: it is not the list that was saved.
        public static bool Restore<T>(List<T> list, T[] saved)
        {
            if (list.Count != saved.Length) return false;
            for (int i = 0; i < saved.Length; i++) list[i] = saved[i];
            return true;
        }

        // Whether the Prefix reorders the alliance's list for this call. The new-campaign pass (isinitialization) drops garrisons,
        // shuffles and equips every unit (16092-16131): vanilla's. The rest is the planner's shared stand-down rule.
        public static bool ShouldReorder(bool enabled, int alliance, int playerAlliance, bool aiVsAi, bool wlCicPlayerAlliance,
                                         bool isInitialization, bool jobMachinePaused, bool freshPlan)
        {
            return !isInitialization && SteeringGate.ShouldSteer(enabled, alliance, playerAlliance, aiVsAi, wlCicPlayerAlliance, jobMachinePaused, freshPlan);
        }

        // [Ordnance]: how many units stand at each rank. The scouts, depot builders, garrisons and unassigned units are one "other".
        public static string Row(int alliance, int[] rankCounts)
        {
            int other = rankCounts[DepotBuilderRank] + rankCounts[ReconRank] + rankCounts[GarrisonRank] + rankCounts[UnassignedRank];
            return "[Ordnance] alliance=" + alliance.ToString(CultureInfo.InvariantCulture) +
                " rearm main=" + rankCounts[MainEffortRank].ToString(CultureInfo.InvariantCulture) +
                " support=" + rankCounts[SupportRank].ToString(CultureInfo.InvariantCulture) +
                " reserve=" + rankCounts[ReserveRank].ToString(CultureInfo.InvariantCulture) +
                " hold=" + rankCounts[HoldRank].ToString(CultureInfo.InvariantCulture) +
                " capital=" + rankCounts[CapitalGuardRank].ToString(CultureInfo.InvariantCulture) +
                " other=" + other.ToString(CultureInfo.InvariantCulture) +
                " last=" + rankCounts[LastRank].ToString(CultureInfo.InvariantCulture);
        }
    }

    // Which allocated group a unit belongs to, when groups nest: every unit of a group takes the group's rank, and a unit that several
    // groups contain takes the rank of the smallest one (the battle core's D5 rule, on allattachedunits sizes). Two groups of one size:
    // the first claim stands. Pooled by the runtime: Clear between calls.
    public sealed class RankClaims<TKey> where TKey : class
    {
        private readonly Dictionary<TKey, int> _rank = new Dictionary<TKey, int>();
        private readonly Dictionary<TKey, int> _size = new Dictionary<TKey, int>();

        public void Clear()
        {
            _rank.Clear();
            _size.Clear();
        }

        public void Claim(TKey key, int rank, int groupSize)
        {
            if (key == null) return;
            if (_size.TryGetValue(key, out int held) && held <= groupSize) return;
            _rank[key] = rank;
            _size[key] = groupSize;
        }

        public int RankOf(TKey key, int fallback)
        {
            return key != null && _rank.TryGetValue(key, out int rank) ? rank : fallback;
        }
    }
}
