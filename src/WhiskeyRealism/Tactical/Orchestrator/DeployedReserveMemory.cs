using System.Collections.Generic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    // Remembers which deployed-reserve children the allocator has released (BC10). The executor's
    // outcome says a child was placed as a reserve and no other source has moved it since, but a
    // ReleaseReserves role change (crisis, Exploit, odds) issues no Move: without this memory the
    // child would fall back into Reserve as soon as the crisis passed. A released child stays
    // released for the battle; its role is the evidence path's from then on.
    public sealed class DeployedReserveMemory
    {
        private readonly HashSet<int> _released = new HashSet<int>();

        /// <summary>The coordinator's one instance, cleared at battle end.</summary>
        internal static readonly DeployedReserveMemory Shared = CreateShared();

        private static DeployedReserveMemory CreateShared()
        {
            var memory = new DeployedReserveMemory();
            BattleScope.Register("deployed-reserve-memory", memory.Clear);
            return memory;
        }

        /// <summary>Clears the flag of every child already released.</summary>
        public void Apply(bool[] deployedReserve, int[] childIds)
        {
            if (deployedReserve == null || childIds == null) return;
            for (int i = 0; i < deployedReserve.Length && i < childIds.Length; i++)
                if (deployedReserve[i] && _released.Contains(childIds[i])) deployedReserve[i] = false;
        }

        /// <summary>
        /// After an allocation: a child that carried the flag and now holds any role but Reserve
        /// has been released. Unknown is no decision and releases nothing.
        /// </summary>
        public void NoteRoles(IReadOnlyList<bool> deployedReserve, int[] childIds, IReadOnlyList<DirectChildRole> roles)
        {
            if (deployedReserve == null || childIds == null || roles == null) return;
            for (int i = 0; i < deployedReserve.Count && i < childIds.Length && i < roles.Count; i++)
                if (deployedReserve[i] && roles[i] != DirectChildRole.Reserve && roles[i] != DirectChildRole.Unknown)
                    _released.Add(childIds[i]);
        }

        public void Clear()
        {
            _released.Clear();
        }
    }
}
