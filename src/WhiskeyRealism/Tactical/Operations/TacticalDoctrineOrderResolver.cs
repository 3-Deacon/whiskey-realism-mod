using System;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tactical.Operations
{
    /// <summary>
    /// Resolves the per-Regiment <see cref="CommandDoctrineOrder"/> + matching
    /// <see cref="BattlefieldPictureSnapshot"/> from the side orchestrator's
    /// active operations ledger. Extracted from the inline
    /// <c>BattleGroupStancePatch.TryResolveDoctrineOrder</c> so the
    /// heavy-gate-fire pre-compute in
    /// <see cref="TacticalBattleCoordinatorRuntime"/> and the legacy patch
    /// fallback path call the same code.
    ///
    /// Stateless (apart from reading the side orchestrator's published list).
    /// All exceptions degrade silently to "no order" — patches must never
    /// throw from resolver helpers.
    /// </summary>
    internal static class TacticalDoctrineOrderResolver
    {
        /// <summary>
        /// Returns true when an active doctrine order with a populated
        /// purpose exists for the given <paramref name="group"/>. On false,
        /// <paramref name="order"/> is <c>default</c> and
        /// <paramref name="picture"/> is an empty picture.
        /// </summary>
        public static bool TryResolve(
            Regiment group,
            out CommandDoctrineOrder order,
            out BattlefieldPictureSnapshot picture)
        {
            order = default(CommandDoctrineOrder);
            picture = new BattlefieldPictureSnapshot(Array.Empty<BattlefieldObjectiveEstimate>());
            try
            {
                if (group == null) return false;
                TacticalBattleOrchestrator side = TacticalBattleCoordinator.GetSideOrchestrator(group.alliance);
                ArmyOrchestrator army = side?.Army;
                var orders = army?.CurrentDoctrineOrders;
                if (orders == null || orders.Count == 0) return false;

                int componentInstanceId = TacticalPatchIds.ComponentInstanceId(group);
                int gameObjectInstanceId = TacticalPatchIds.GameObjectInstanceId(group);
                for (int i = 0; i < orders.Count; i++)
                {
                    CommandDoctrineOrder candidate = orders[i];
                    if (!TacticalPatchIds.NodeIdMatches(candidate.NodeId, gameObjectInstanceId, componentInstanceId))
                        continue;
                    if (!candidate.HasPurpose) return false;

                    order = candidate;
                    if (side?.OperationsLedger != null)
                        picture = side.OperationsLedger.CurrentBattlefieldPicture;
                    return true;
                }
            }
            catch
            {
                // Degrade silently — caller treats false as "no doctrine opinion".
            }

            return false;
        }
    }
}
