using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    // Runtime terrain probes for WaypointValidator (R4,
    // docs/vanilla-vision-and-movement.md). Excluded from the test csproj.
    //
    // Vanilla anchors (decompile, build 14703111):
    //   BattlefieldSetup.GetCurrentTerrainOnPos (static, 26727) reads the baked
    //     terrainspecs grid: 4 river, 8 fences/walls.
    //   BattlefieldSetup.CheckTerrainLine(start, end, ids, step) (27638/27650):
    //     true when any step from start toward end lies on one of the ids.
    //   BattlefieldSetup.CheckIfFinalWaypointIsOnTerrain(start, end, 4) (25574):
    //     walks a river endpoint back toward start in 1 m steps; returns start
    //     when the whole segment is river. RegimentSetPath runs the same pull-back
    //     on every battle path (131075).
    //   The instance comes from the private static BattleUnits.bfs (78972).
    //   Map size: Terrain.activeTerrain.terrainData.size, read by reflection (the
    //     mod does not reference UnityEngine.TerrainModule), as RegimentSetPath's
    //     clamp uses it (131057-131068).
    internal sealed class WaypointValidatorRuntime : ITerrainProbe
    {
        internal static readonly WaypointValidatorRuntime Instance = new WaypointValidatorRuntime();

        private const int RiverTerrain = 4;
        private const int FenceTerrain = 8;
        private static readonly int[] WaterOrFenceIds = { RiverTerrain, FenceTerrain };

        private static FieldInfo _bfsField;
        private static bool _bfsFieldResolved;
        private static PropertyInfo _activeTerrainProperty;
        private static PropertyInfo _terrainDataProperty;
        private static PropertyInfo _terrainSizeProperty;
        private static bool _terrainReflectionResolved;

        public bool IsWaterOrFence(float x, float z)
        {
            try
            {
                int terrain = BattlefieldSetup.GetCurrentTerrainOnPos(new Vector3(x, 0f, z));
                return terrain == RiverTerrain || terrain == FenceTerrain;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:terrain-point-failed",
                    "[WaypointValidator] GetCurrentTerrainOnPos threw: " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        public bool LineCrossesWater(float x0, float z0, float x1, float z1)
        {
            BattlefieldSetup bfs = Bfs();
            if ((UnityEngine.Object)bfs == null) return false;
            try
            {
                return bfs.CheckTerrainLine(new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z1), WaterOrFenceIds, WaypointValidator.TerrainLineStepMeters);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:terrain-line-failed",
                    "[WaypointValidator] CheckTerrainLine threw: " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        public bool StepBackFromRiver(float x0, float z0, ref float x1, ref float z1)
        {
            BattlefieldSetup bfs = Bfs();
            if ((UnityEngine.Object)bfs == null) return false;
            try
            {
                Vector3 start = new Vector3(x0, 0f, z0);
                Vector3 pulled = bfs.CheckIfFinalWaypointIsOnTerrain(start, new Vector3(x1, 0f, z1), RiverTerrain);
                // Vanilla returns the start when the whole segment is river; leave the
                // target for the validator's own step-back to reject.
                if (pulled.x == start.x && pulled.z == start.z) return false;
                if (pulled.x == x1 && pulled.z == z1) return false;
                x1 = pulled.x;
                z1 = pulled.z;
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:terrain-stepback-failed",
                    "[WaypointValidator] CheckIfFinalWaypointIsOnTerrain threw: " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        /// <summary>Terrain.activeTerrain.terrainData.size x/z; false (0, 0) when unavailable.</summary>
        internal static bool TryMapSize(out float sizeX, out float sizeZ)
        {
            sizeX = 0f;
            sizeZ = 0f;
            try
            {
                if (!_terrainReflectionResolved)
                {
                    _terrainReflectionResolved = true;
                    Type terrainType = AccessTools.TypeByName("UnityEngine.Terrain");
                    Type terrainDataType = AccessTools.TypeByName("UnityEngine.TerrainData");
                    _activeTerrainProperty = terrainType != null ? AccessTools.Property(terrainType, "activeTerrain") : null;
                    _terrainDataProperty = terrainType != null ? AccessTools.Property(terrainType, "terrainData") : null;
                    _terrainSizeProperty = terrainDataType != null ? AccessTools.Property(terrainDataType, "size") : null;
                    if (_activeTerrainProperty == null || _terrainDataProperty == null || _terrainSizeProperty == null)
                        OnceLog.Warning("tactical-battle-core:terrain-size-missing",
                            "[WaypointValidator] Terrain size not readable; targets are not map-clamped");
                }

                if (_activeTerrainProperty == null || _terrainDataProperty == null || _terrainSizeProperty == null) return false;
                object terrain = _activeTerrainProperty.GetValue(null, null);
                object data = terrain != null ? _terrainDataProperty.GetValue(terrain, null) : null;
                if (data == null) return false;
                Vector3 size = (Vector3)_terrainSizeProperty.GetValue(data, null);
                sizeX = size.x;
                sizeZ = size.z;
                return sizeX > 3f && sizeZ > 3f;
            }
            catch
            {
                return false;
            }
        }

        private static BattlefieldSetup Bfs()
        {
            try
            {
                if (!_bfsFieldResolved)
                {
                    _bfsFieldResolved = true;
                    _bfsField = AccessTools.Field(typeof(BattleUnits), "bfs");
                    if (_bfsField == null)
                        OnceLog.Warning("tactical-battle-core:bfs-missing",
                            "[WaypointValidator] BattleUnits.bfs not found; river and fence-line probes are skipped");
                }
                return _bfsField != null ? _bfsField.GetValue(null) as BattlefieldSetup : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
