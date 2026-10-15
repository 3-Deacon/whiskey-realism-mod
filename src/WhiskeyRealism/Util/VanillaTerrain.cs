using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WhiskeyRealism.Util
{
    // Terrain.activeTerrain.SampleHeight, the height vanilla's order code puts under an order's
    // position (AIBattle.CheckCurrentOrderUpdate 8638-8639, battle and campaign alike). Read by
    // reflection: the mod does not reference UnityEngine.TerrainModule (as WaypointValidatorRuntime).
    internal static class VanillaTerrain
    {
        private static bool _resolved;
        private static PropertyInfo _activeTerrain;
        private static MethodInfo _sampleHeight;

        // The terrain height at position, or `fallback` when the terrain cannot be read.
        internal static float SampleHeight(Vector3 position, float fallback)
        {
            try
            {
                if (!_resolved)
                {
                    _resolved = true;
                    Type terrainType = AccessTools.TypeByName("UnityEngine.Terrain");
                    _activeTerrain = terrainType != null ? AccessTools.Property(terrainType, "activeTerrain") : null;
                    _sampleHeight = terrainType != null ? AccessTools.Method(terrainType, "SampleHeight", new[] { typeof(Vector3) }) : null;
                    if (_activeTerrain == null || _sampleHeight == null)
                        OnceLog.Warning("vanilla-terrain:unreadable", "[VanillaTerrain] Terrain.SampleHeight not readable; order heights unchanged");
                }
                object terrain = _activeTerrain?.GetValue(null, null);
                if (terrain == null || _sampleHeight == null) return fallback;
                return (float)_sampleHeight.Invoke(terrain, new object[] { position });
            }
            catch
            {
                return fallback;
            }
        }
    }
}
