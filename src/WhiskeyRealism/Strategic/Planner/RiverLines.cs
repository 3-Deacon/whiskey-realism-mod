using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // The campaign terrain questions the planner asks (G3, G0 landing B). Pure interface; the
    // runtime answer is TerrainLineRuntime, a fixture answers it in the harness.
    public interface ITerrainLineProbe
    {
        // Vanilla's engagement line test (CheckIfUnitIsWithinModifiedRange 80502-80517, called by
        // EstablishCampaignBattle 80683-80687): true when BattlefieldSetup.CountTerrainLine from a
        // toward b over terrainrestrictions_combat {1,3,4,5,6,8} (64535) stops short of the
        // distance, i.e. a river, sea or impassable cell lies on the line.
        bool Blocks(float ax, float az, float bx, float bz);

        // A river cell (terrain 3 small river, 4 river, 5 river crossing: the cells River Expedition
        // speeds, 133482-133491) within range of the point (BattlefieldSetup.GetCurrentTerrainOnPos,
        // 26727). The march-time river corridor.
        bool RiverNear(float x, float z, float range);
    }

    // A river delays a threat, it never removes one. Vanilla starts a campaign battle only when
    // the engagement line is clear (80683-80687), so an army behind a river line must cross at a
    // ford or bridge (BattlefieldSetup.rivercrossingpositions, 26628-26638) to fight.
    public static class RiverLines
    {
        // Straight XZ distance when the line is clear; when it is blocked, the shortest detour
        // a -> crossing -> b over the snapshot's crossings (never shorter than the straight line);
        // with no crossing the straight distance stays. The detour legs are not re-probed: the
        // crossing cells (terrain 5/6) are themselves in terrainrestrictions_combat, so a leg that
        // ends on one would always read as blocked.
        public static float EffectiveDistance(float ax, float az, float bx, float bz, ITerrainLineProbe probe,
                                              IReadOnlyList<CrossingRecord> crossings)
        {
            float straight = Distance(ax, az, bx, bz);
            if (probe == null || crossings == null || crossings.Count == 0) return straight;
            if (!probe.Blocks(ax, az, bx, bz)) return straight;

            float best = float.MaxValue;
            for (int i = 0; i < crossings.Count; i++)
            {
                var c = crossings[i];
                float viaCrossing = Distance(ax, az, c.X, c.Z) + Distance(c.X, c.Z, bx, bz);
                if (viaCrossing < best) best = viaCrossing;
            }
            return Math.Max(straight, best);
        }

        internal static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
