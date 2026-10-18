using System;
using System.Collections.Generic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>
    /// BC14O requirement-historical-chain: a battle's designer chain (vanilla's <c>AIBattle.objectivechain</c>, from <c>Config/aistrategies.dat</c>) is a
    /// strong prior, not an override. A chain names its next objective <c>objectives[0]</c> and the battle hour it is due, <c>objectivetimetomove[0]</c>.
    /// These are the side's own orders, read from its own AIBattle; they are not information about the enemy. Reading vanilla's lists only: no native call.
    /// </summary>
    internal static class ObjectiveChains
    {
        /// <summary>
        /// Vanilla's own due test (UpdateMovingTargets 6928): the head's time is at or before the battle hour (<c>Hour + 24 x battlepasseddays</c>,
        /// which vanilla keeps in <c>GameVars.currenttimefromstart</c>). Inclusive.
        /// </summary>
        public static bool IsDue(float timeToMove, float battleHours) => timeToMove <= battleHours;

        /// <summary>
        /// The next objective of every due designer chain, de-duplicated (an objective named by several chains gets the prior once), written to
        /// <paramref name="into"/> as the capture's id. A chain switched to dynamic (<c>usedstrategyid</c> below 0, vanilla's 7406-7410), an empty one and
        /// a pure wait entry (a null head: a timed pause, 7814-7818) give none; the prior returns when vanilla moves past the wait and a real head is next.
        /// <paramref name="idOf"/> maps the chain's objective reference to the capture's id by reference. Returns how many.
        /// </summary>
        public static int CollectDue(IReadOnlyList<AIBattle.ObjectiveChain> chains, float battleHours, Func<Objectives, string> idOf, string[] into)
        {
            int n = 0;
            for (int c = 0; chains != null && c < chains.Count && n < into.Length; c++)
            {
                if (!TryDueHead(chains[c], battleHours, out Objectives head)) continue;
                string id = idOf(head);
                if (id == null || Contains(into, n, id)) continue;
                into[n++] = id;
            }
            return n;
        }

        /// <summary>
        /// Fills <paramref name="table"/> with every due designer chain's own group (<c>linegroup_centerunit</c>, the one group vanilla's
        /// <c>UpdateMovingTargets</c> sends to the chain's head at its due hour) and the head's place: the groups the chain contest may answer. A chain
        /// whose head the map does not know (<paramref name="pointOf"/> false) or whose group is not a battle group adds nothing. Returns how many.
        /// </summary>
        public static int FillGroups(IReadOnlyList<AIBattle.ObjectiveChain> chains, float battleHours, Func<Objectives, string> idOf, Func<Regiment, int> groupIdOf,
            DueChainTable.PointLookup pointOf, DueChainTable table)
        {
            table.Clear();
            for (int c = 0; chains != null && c < chains.Count; c++)
            {
                if (!TryDueHead(chains[c], battleHours, out Objectives head)) continue;
                Regiment group = chains[c].linegroup_centerunit;
                if (ReferenceEquals(group, null)) continue;
                string id = idOf(head);
                if (id == null || !pointOf(id, out float x, out float z)) continue;
                int groupId = groupIdOf(group);
                if (groupId == 0) continue;
                table.Add(groupId, x, z);
            }
            return table.Count;
        }

        // A designer chain's head if it is due: not dynamic (usedstrategyid below 0, vanilla's 7406-7410), not empty, not a wait entry (a null head, a
        // timed pause, 7814-7818) and its hour has come. The null test is on the reference: a Unity object that is "destroyed" is still an objective.
        private static bool TryDueHead(AIBattle.ObjectiveChain chain, float battleHours, out Objectives head)
        {
            head = null;
            if (chain == null || chain.usedstrategyid < 0 || chain.objectives == null || chain.objectivetimetomove == null) return false;
            if (chain.objectives.Count == 0 || chain.objectivetimetomove.Count == 0) return false;
            head = chain.objectives[0];
            return !ReferenceEquals(head, null) && IsDue(chain.objectivetimetomove[0], battleHours);
        }

        private static bool Contains(string[] ids, int count, string id)
        {
            for (int i = 0; i < count; i++) if (string.Equals(ids[i], id, StringComparison.Ordinal)) return true;
            return false;
        }
    }
    /// <summary>
    /// The due designer chains' own groups and where their head objective stands, per alliance, refreshed with each evaluation by
    /// <c>ObjectiveRuntime.Fill</c> (<see cref="ObjectiveChains.FillGroups"/>). The chain contest answers a move of one of these groups to a place near
    /// its head, and no other move: a group vanilla moved for another reason (it marches to the sound of guns, assaults, retreats outflanked) is not
    /// the chain's, and is left to the reaction vanilla gave it.
    /// </summary>
    public sealed class DueChainTable
    {
        /// <summary>A waypoint this close to the head objective is the chain's move (vanilla sends the group to the objective, or 150 m short by its own near test, or a spacing shift of the spot).</summary>
        public const float NearObjectiveMeters = 150f;

        public delegate bool PointLookup(string objectiveId, out float x, out float z);

        private const int Capacity = 16;
        private static readonly DueChainTable[] _byAlliance = { new DueChainTable(), new DueChainTable() };

        static DueChainTable()
        {
            BattleScope.Register("due-chain-table", ResetAll);
        }

        private readonly int[] _groups = new int[Capacity];
        private readonly float[] _x = new float[Capacity], _z = new float[Capacity];

        public int Count { get; private set; }

        /// <summary>Bound-checked: alliances 0 and 1 only.</summary>
        public static DueChainTable ForAlliance(int alliance)
            => alliance >= 0 && alliance < _byAlliance.Length ? _byAlliance[alliance] : null;

        public void Clear() => Count = 0;

        public void Add(int groupId, float x, float z)
        {
            if (Count >= Capacity) return;
            _groups[Count] = groupId;
            _x[Count] = x;
            _z[Count] = z;
            Count++;
        }

        /// <summary>The group is a due chain's own group; the head objective's place when it is.</summary>
        public bool TryFind(int groupId, out float x, out float z)
        {
            for (int i = 0; i < Count; i++)
            {
                if (_groups[i] != groupId) continue;
                x = _x[i];
                z = _z[i];
                return true;
            }
            x = z = 0f;
            return false;
        }

        private static void ResetAll()
        {
            for (int i = 0; i < _byAlliance.Length; i++) _byAlliance[i].Clear();
        }
    }
}
