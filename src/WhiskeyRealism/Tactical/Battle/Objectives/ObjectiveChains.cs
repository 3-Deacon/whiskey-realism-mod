using System;
using System.Collections.Generic;

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
                AIBattle.ObjectiveChain chain = chains[c];
                if (chain == null || chain.usedstrategyid < 0 || chain.objectives == null || chain.objectivetimetomove == null) continue;
                if (chain.objectives.Count == 0 || chain.objectivetimetomove.Count == 0) continue;
                // The null test is on the reference: a wait entry is a real null, and a Unity object that is "destroyed" is still an objective.
                Objectives head = chain.objectives[0];
                if (ReferenceEquals(head, null) || !IsDue(chain.objectivetimetomove[0], battleHours)) continue;
                string id = idOf(head);
                if (id == null || Contains(into, n, id)) continue;
                into[n++] = id;
            }
            return n;
        }

        private static bool Contains(string[] ids, int count, string id)
        {
            for (int i = 0; i < count; i++) if (string.Equals(ids[i], id, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
