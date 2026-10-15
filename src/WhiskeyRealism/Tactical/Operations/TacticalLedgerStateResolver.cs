using WhiskeyRealism.Patches;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tactical.Operations
{
    /// <summary>
    /// Resolves the per-Regiment <see cref="CommandNodeOperationalState"/> from
    /// the side orchestrator's active operations ledger. Extracted from the
    /// inline #45 ledger lookup; the heavy-gate-fire pre-compute in
    /// <see cref="TacticalBattleCoordinatorRuntime"/> calls it.
    ///
    /// Stateless. All exceptions degrade silently to "no state".
    /// </summary>
    internal static class TacticalLedgerStateResolver
    {
        /// <summary>
        /// Returns true when the ledger holds an operational state matching
        /// the given <paramref name="group"/>. On false,
        /// <paramref name="state"/> is <c>default</c>.
        /// </summary>
        public static bool TryResolve(Regiment group, out CommandNodeOperationalState state)
        {
            state = default(CommandNodeOperationalState);
            try
            {
                if (group == null) return false;
                TacticalBattleOrchestrator side = TacticalBattleCoordinator.GetSideOrchestrator(group.alliance);
                var operations = side?.Army?.CurrentCommandOperations;
                if (operations == null || operations.Count == 0) return false;

                int componentInstanceId = TacticalPatchIds.ComponentInstanceId(group);
                int gameObjectInstanceId = TacticalPatchIds.GameObjectInstanceId(group);
                for (int i = 0; i < operations.Count; i++)
                {
                    if (TacticalPatchIds.NodeIdMatches(operations[i].NodeId, gameObjectInstanceId, componentInstanceId))
                    {
                        state = operations[i];
                        return true;
                    }
                }
            }
            catch
            {
                // Degrade silently — caller treats false as "no ledger opinion".
            }

            return false;
        }
    }
}
