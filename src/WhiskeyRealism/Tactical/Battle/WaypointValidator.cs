using System;

namespace WhiskeyRealism.Tactical.Battle
{
    // Terrain reads the validator needs (R4, docs/vanilla-vision-and-movement.md).
    // The runtime implementation (WaypointValidatorRuntime) wraps vanilla's
    // BattlefieldSetup probes; tests use a fake.
    public interface ITerrainProbe
    {
        // Battle terrain 4 (river) or 8 (fences/walls) at the point
        // (BattlefieldSetup.GetCurrentTerrainOnPos, 26727). The ids are the
        // battle terrain table (GamePrefs.loadcontrols reads battleprefs.txt,
        // 54325-54330; "do not change rank"): 3 creek, 4 river, 5 river crossing
        // (ford), 6 bridge, 7 road, 8 fences/walls. They are NavMesh area
        // indices (26596-26623; 8 is the "FencesWalls, Sea (C)" area). Sea is 8
        // only on the campaign map (campaignprefs.txt; GrabFleet 12224,
        // SetRetreatToDepot 116051), not in battle.
        bool IsWaterOrFence(float x, float z);

        // BattlefieldSetup.CheckTerrainLine(start, target, {4, 8}, 2f) (27638/27650).
        bool LineCrossesWater(float x0, float z0, float x1, float z1);

        // BattlefieldSetup.CheckIfFinalWaypointIsOnTerrain(start, target, 4) (25574):
        // walks a river endpoint back toward the start in 1 m steps. Returns true
        // when the target moved.
        bool StepBackFromRiver(float x0, float z0, ref float x1, ref float z1);
    }

    // Validation of Whiskey-generated targets before a vanilla move (plan BC4 step 3,
    // R4). Vanilla clamps every path target in RegimentSetPath (131057-131068: x to
    // [1, sizeX-2], z to [-sizeZ+2, -1]; the battle z axis is negative) and pulls a
    // river endpoint back (131075 → 25574), but it never checks fences, a friendly
    // group already standing on the target, or an artillery position's line of fire.
    // Steps, in order:
    //   1. Non-finite target → OffMap; clamp exactly like vanilla (never rejects).
    //   2. Vanilla's river pull-back, then while the target is on water/fence, step
    //      25 m back toward the start, at most 4 times, else Water. An in-contact
    //      withdrawal must also not cross water or a fence line (CheckTerrainLine
    //      {4, 8}): it is an immediate move (no courier) under fire, and the NavMesh
    //      would otherwise route it over a distant ford. A planned move may cross —
    //      AddPath routes it over a ford or bridge (130364-130394). CheckTerrainLine
    //      tests its start position first (27659-27686), so the line starts one
    //      step out: a group standing at a fence or wall (8) may still withdraw.
    //      A ford is its own id (5, river crossing: the bake keeps the crossing
    //      area over the river, 26601-26618, and lists 5/6 as crossings, 26628),
    //      so a group on a ford never trips the {4, 8} line at its start.
    //   3. Move only: a target inside another own group's footprint is rejected
    //      unless ReliefOrSupport or ImmediatePlacement (a deployment pass lays its groups out
    //      itself, and the snapshot is stale after its first placement). The group's own ancestors and descendants are not
    //      blockers.
    //   4. ArtilleryFirePosition: a visible enemy within 60 m of the target →
    //      PointBlank; the segment from the target to FireTargetId passing within
    //      40 m of an own unit → FriendlyFireLine.
    public static class WaypointValidator
    {
        public const float FriendlyFireClearanceMeters = 40f;
        // = #61 MinWaypointDistance (60 m): the radius #61 treated as "the same place".
        public const float FriendlyFootprintRadiusMeters = 60f;
        // = ArtillerySetWaypointVetoPatch.PointBlankMeters.
        public const float ArtilleryPointBlankMeters = 60f;
        public const float StepBackMeters = 25f;
        public const int MaxStepBacks = 4;
        // CheckTerrainLine step for the withdrawal line (2 m, as 27638/27650 callers use).
        public const float TerrainLineStepMeters = 2f;

        public static SkipReason Validate(
            IBattleUnitView s,
            int groupIndex,
            in BattleOrderIntent i,
            float sizeX,
            float sizeZ,
            ITerrainProbe terrain,
            out float x,
            out float z)
        {
            x = i.TargetX;
            z = i.TargetZ;
            if (!IsFinite(x) || !IsFinite(z)) return SkipReason.OffMap;
            VanillaMapBounds.ClampXZ(x, z, sizeX, sizeZ, out x, out z);

            UnitRecord[] own = s.Own;
            float startX = own[groupIndex].X;
            float startZ = own[groupIndex].Z;

            if (terrain != null)
            {
                terrain.StepBackFromRiver(startX, startZ, ref x, ref z);
                bool checkLine = (i.Flags & IntentFlags.InContactWithdrawal) != 0;
                for (int step = 0; ; step++)
                {
                    bool blocked = terrain.IsWaterOrFence(x, z) ||
                        (checkLine && LineCrossesWaterPastStart(terrain, startX, startZ, x, z));
                    if (!blocked) break;
                    if (step == MaxStepBacks) return SkipReason.Water;
                    StepToward(startX, startZ, ref x, ref z, StepBackMeters);
                }
            }

            if (i.Channel == OrderChannel.Move && (i.Flags & (IntentFlags.ReliefOrSupport | IntentFlags.ImmediatePlacement)) == 0 &&
                InsideFriendlyFootprint(s, groupIndex, x, z))
                return SkipReason.FriendlyFootprint;

            if ((i.Flags & IntentFlags.ArtilleryFirePosition) != 0)
            {
                if (VisibleEnemyWithin(s, x, z, ArtilleryPointBlankMeters)) return SkipReason.PointBlank;
                if (FireLineBlocked(s, groupIndex, x, z, i.FireTargetId)) return SkipReason.FriendlyFireLine;
            }

            return SkipReason.None;
        }

        // The line from one step beyond the start cell to the target; a target
        // inside that first step has no line to test.
        private static bool LineCrossesWaterPastStart(ITerrainProbe terrain, float startX, float startZ, float x, float z)
        {
            float dx = x - startX;
            float dz = z - startZ;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d <= TerrainLineStepMeters) return false;
            float fromX = startX + dx / d * TerrainLineStepMeters;
            float fromZ = startZ + dz / d * TerrainLineStepMeters;
            return terrain.LineCrossesWater(fromX, fromZ, x, z);
        }

        private static void StepToward(float startX, float startZ, ref float x, ref float z, float meters)
        {
            float dx = startX - x;
            float dz = startZ - z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d <= meters)
            {
                x = startX;
                z = startZ;
                return;
            }
            x += dx / d * meters;
            z += dz / d * meters;
        }

        private static bool InsideFriendlyFootprint(IBattleUnitView s, int groupIndex, float x, float z)
        {
            UnitRecord[] own = s.Own;
            float r2 = FriendlyFootprintRadiusMeters * FriendlyFootprintRadiusMeters;
            for (int j = 0; j < s.OwnCount; j++)
            {
                if (j == groupIndex) continue;
                UnitFlags f = own[j].Flags;
                if ((f & UnitFlags.IsGroup) == 0 || !Standing(f)) continue;
                if (DistSq(own[j].X, own[j].Z, x, z) >= r2) continue;
                if (s.IsAncestorOf(groupIndex, j) || s.IsAncestorOf(j, groupIndex)) continue;
                return true;
            }
            return false;
        }

        private static bool VisibleEnemyWithin(IBattleUnitView s, float x, float z, float meters)
        {
            EnemyObservation[] obs = s.Observations;
            float r2 = meters * meters;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (obs[k].State != ObservationState.Visible) continue;
                if (DistSq(obs[k].X, obs[k].Z, x, z) < r2) return true;
            }
            return false;
        }

        private static bool FireLineBlocked(IBattleUnitView s, int groupIndex, float x, float z, int fireTargetId)
        {
            if (fireTargetId == 0) return false;
            EnemyObservation[] obs = s.Observations;
            int target = -1;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (obs[k].InstanceId == fireTargetId) { target = k; break; }
            }
            if (target < 0) return false;

            float tx = obs[target].X;
            float tz = obs[target].Z;
            UnitRecord[] own = s.Own;
            float r2 = FriendlyFireClearanceMeters * FriendlyFireClearanceMeters;
            for (int j = 0; j < s.OwnCount; j++)
            {
                if (j == groupIndex || !Standing(own[j].Flags)) continue;
                if (s.IsAncestorOf(groupIndex, j) || s.IsAncestorOf(j, groupIndex)) continue;
                if (SegmentDistSq(own[j].X, own[j].Z, x, z, tx, tz) < r2) return true;
            }
            return false;
        }

        private static bool Standing(UnitFlags f)
        {
            return (f & UnitFlags.Alive) != 0 && (f & UnitFlags.Active) != 0 && (f & UnitFlags.Routed) == 0;
        }

        private static float DistSq(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return dx * dx + dz * dz;
        }

        private static float SegmentDistSq(float px, float pz, float ax, float az, float bx, float bz)
        {
            float vx = bx - ax;
            float vz = bz - az;
            float len2 = vx * vx + vz * vz;
            float t = len2 > 0f ? ((px - ax) * vx + (pz - az) * vz) / len2 : 0f;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            return DistSq(px, pz, ax + t * vx, az + t * vz);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
