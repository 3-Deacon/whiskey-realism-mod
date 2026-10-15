using System;
using HarmonyLib;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Accessors for DLC_WL's private cumulative-effect arrays (DC:43199-43201):
    // cumulativeeffectsrunning (multipliers EffectMultiplier(id, 1) returns) and
    // cumulativeeffectsonce (additive values EffectMultiplier(id, 0) returns).
    // Shared by the W&L economy patches; a missing field downgrades to one
    // bounded warning and the fix standing down. No [HarmonyPatch].
    internal static class WlEffectArrays
    {
        private static AccessTools.FieldRef<float[]> _running;
        private static AccessTools.FieldRef<float[]> _once;
        private static bool _resolved;

        internal static float[] Running()
        {
            Resolve();
            try { return _running?.Invoke(); }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-effect-arrays:read-running", "[Patch:WlEffects] reading DLC_WL.cumulativeeffectsrunning failed: " + ex.Message);
                return null;
            }
        }

        internal static float[] Once()
        {
            Resolve();
            try { return _once?.Invoke(); }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-effect-arrays:read-once", "[Patch:WlEffects] reading DLC_WL.cumulativeeffectsonce failed: " + ex.Message);
                return null;
            }
        }

        // Slot read that tolerates a short or missing array.
        internal static bool TryGet(float[] array, int id, out float value)
        {
            value = 0f;
            if (array == null || id < 0 || id >= array.Length) return false;
            value = array[id];
            return true;
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                var running = AccessTools.Field(typeof(DLC_WL), "cumulativeeffectsrunning");
                var once = AccessTools.Field(typeof(DLC_WL), "cumulativeeffectsonce");
                if (running != null) _running = AccessTools.StaticFieldRefAccess<float[]>(running);
                if (once != null) _once = AccessTools.StaticFieldRefAccess<float[]>(once);
                if (_running == null || _once == null)
                    OnceLog.Warning("wl-effect-arrays:missing", "[Patch:WlEffects] DLC_WL cumulative-effect arrays not found; the W&L effect fixes stand down");
            }
            catch (Exception ex)
            {
                _running = null;
                _once = null;
                OnceLog.Warning("wl-effect-arrays:resolve", "[Patch:WlEffects] resolving DLC_WL cumulative-effect arrays failed: " + ex.Message);
            }
        }
    }
}
