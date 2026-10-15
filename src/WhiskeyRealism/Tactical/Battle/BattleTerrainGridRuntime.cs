using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    // Runtime half of BattleTerrainGrid (BC10 decision 1). Excluded from the test csproj.
    // Reads, once per battle, vanilla's terrain-id array by cached reflection (a reference,
    // not a copy: it is public static in this build, BattlefieldSetup.terrainspecs 24731,
    // but a rename must degrade to a warning), the speed-cost table (GamePrefs.terrainspeccost,
    // referenced so the frozen-map rewrite 26232-26233 is seen), the map size, and the
    // listed ford and bridge cells (BattlefieldSetup.rivercrossingpositions 24723, filled by
    // ReadInTerrainSpecs 26628-26640). Public terrain data: no unit is read.
    //
    // The grid does not exist until the map's terrain pass has run (ReadInTerrainSpecs is a
    // load-time coroutine, 26550), so Get() returns null before that and retries; a missing
    // field is latched and every consumer keeps its pre-BC10 behaviour.
    internal static class BattleTerrainGridRuntime
    {
        private static BattleTerrainGrid _grid;
        private static bool _missing;
        private static FieldInfo _specsField;
        private static bool _specsResolved;
        private static FieldInfo _crossingsField;
        private static bool _crossingsResolved;

        static BattleTerrainGridRuntime()
        {
            BattleScope.Register("battle-terrain-grid", Reset);
        }

        /// <summary>
        /// The battle's terrain grid, built on the first call after the map is loaded; its
        /// frozen flag is refreshed on every call. Null while unavailable. Never throws.
        /// </summary>
        internal static BattleTerrainGrid Get()
        {
            if (_grid != null)
            {
                _grid.Frozen = IsFrozen();
                return _grid;
            }
            if (_missing) return null;
            using (TelemetryPerf.Scope("tactical.terrain-grid.build", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                try
                {
                    _grid = Build();
                }
                catch (Exception ex)
                {
                    _missing = true;
                    OnceLog.Warning("tactical-battle-core:terrain-grid-missing",
                        "[BattleTerrain] grid not built, consumers keep their pre-BC10 behaviour: " + ex.GetType().Name + " " + ex.Message);
                }
            }
            return _grid;
        }

        private static BattleTerrainGrid Build()
        {
            if (!_specsResolved)
            {
                _specsResolved = true;
                _specsField = AccessTools.Field(typeof(BattlefieldSetup), "terrainspecs");
                _crossingsField = AccessTools.Field(typeof(BattlefieldSetup), "rivercrossingpositions");
                _crossingsResolved = _crossingsField != null;
            }
            if (_specsField == null || !_crossingsResolved)
            {
                _missing = true;
                OnceLog.Warning("tactical-battle-core:terrain-grid-missing",
                    "[BattleTerrain] BattlefieldSetup.terrainspecs or rivercrossingpositions not found; consumers keep their pre-BC10 behaviour");
                return null;
            }

            int[,] ids = _specsField.GetValue(null) as int[,];
            if (ids == null || !WaypointValidatorRuntime.TryMapSize(out float sizeX, out float sizeZ)) return null;
            int[] speedCost = GamePrefs.terrainspeccost;
            if (speedCost == null || speedCost.Length == 0) return null;

            var xs = new List<float>(32);
            var zs = new List<float>(32);
            var kinds = new List<int>(32);
            BattlefieldSetup bfs = WaypointValidatorRuntime.Bfs();
            List<Vector3> listed = (UnityEngine.Object)bfs != null ? _crossingsField.GetValue(bfs) as List<Vector3> : null;
            int fords = 0, bridges = 0;
            if (listed != null)
            {
                for (int i = 0; i < listed.Count; i++)
                {
                    int id = BattlefieldSetup.GetCurrentTerrainOnPos(listed[i]);
                    if (id != BattleTerrainGrid.FordId && id != BattleTerrainGrid.BridgeId) continue;
                    xs.Add(listed[i].x);
                    zs.Add(listed[i].z);
                    kinds.Add(id);
                    if (id == BattleTerrainGrid.FordId) fords++;
                    else bridges++;
                }
            }

            var grid = new BattleTerrainGrid(ids, sizeX, sizeZ, speedCost, IsFrozen(), xs.ToArray(), zs.ToArray(), kinds.ToArray(), xs.Count);
            OnceLog.Info("tactical-battle-core:terrain-grid",
                "[BattleTerrain] grid " + (int)sizeX + "x" + (int)sizeZ + " m, crossings=" + xs.Count + " (fords=" + fords + " bridges=" + bridges + ")");
            return grid;
        }

        private static bool IsFrozen()
        {
            try { return GameVars.isfrozen(); }
            catch { return false; }
        }

        private static void Reset()
        {
            _grid = null;
            _missing = false;
        }
    }
}
