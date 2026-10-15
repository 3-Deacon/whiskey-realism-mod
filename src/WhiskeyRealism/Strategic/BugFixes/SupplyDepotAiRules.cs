using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.BugFixes
{
    // AICampaign.CheckSupplyDepotConstruction (decompile 14659-14779), job 19.
    //
    // BUG-LOG-006: the builder loop (14690-14713) sets the skip flag for any depot the
    // alliance started within commander range, finished or not (14697), so a unit next
    // to its own finished, empty or captured depot neither moves nor wishes a depot.
    //
    // BUG-LOG-007: builders are recorded within commander range (14695), but
    // CBuilding.IncreaseCondition advances construction only while a unit of the
    // starting alliance stands within its own bugle range (96626-96655); and the
    // replace-by-smaller dedupe adds the new unit even when it is not smaller
    // (14706-14711), pinning several units to one depot.
    //
    // Not a bug (BUG-LOG-005): the installation test at 14740 also admits an undefended
    // enemy depot, which the unit captures by walking up (frontline 87208-87250 ->
    // CheckNewOwner 95810 -> ChangeOwnership 95402-95452 keeps the stock) and then draws
    // from (UpdateIIPSupplyLink 114296-114306).
    internal static class SupplyDepotAiRules
    {
        internal struct BuilderUnit
        {
            // Vanilla's builder candidates: present, stationary (regimentpaths <= 0) and
            // not a garrison (14690).
            internal bool Eligible;
            internal int Strength;   // groupstrengthdirect
            internal float X;
            internal float Z;
            internal float BugleRange;
        }

        internal struct DepotSite
        {
            internal float X;
            internal float Z;
            // Started by this alliance and still under construction (14695, 14698).
            internal bool UnderConstruction;
        }

        // A finished depot the alliance started is hidden from the builder loop for the
        // call, so only depots under construction set the skip flag.
        internal static bool ShouldMaskFinishedOwnDepot(int startedByAlliance, int alliance, float constructionTimer)
        {
            return startedByAlliance == alliance && constructionTimer <= 0f;
        }

        // Vanilla's builder bookkeeping (14690-14713) with the construction radius
        // (bugle range, 96631) and one builder per depot, the smallest. Units are visited
        // in ownunits order and depots in BattleUnits.supplydepot order, as vanilla does;
        // a unit builds at most one depot. Returns unit indices in insertion order.
        internal static List<int> SelectBuilders(IReadOnlyList<BuilderUnit> units, IReadOnlyList<DepotSite> depots)
        {
            var builders = new List<int>();
            if (units == null || depots == null) return builders;
            var builderDepots = new List<int>();
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (!unit.Eligible) continue;
                for (int j = 0; j < depots.Count; j++)
                {
                    var depot = depots[j];
                    if (!depot.UnderConstruction) continue;
                    if (XzDistance(unit.X, unit.Z, depot.X, depot.Z) >= unit.BugleRange) continue;
                    if (builders.Contains(i)) break;
                    int existing = builderDepots.IndexOf(j);
                    if (existing >= 0)
                    {
                        if (units[builders[existing]].Strength <= unit.Strength) continue;
                        builders.RemoveAt(existing);
                        builderDepots.RemoveAt(existing);
                    }
                    builders.Add(i);
                    builderDepots.Add(j);
                }
            }
            return builders;
        }

        private static float XzDistance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
