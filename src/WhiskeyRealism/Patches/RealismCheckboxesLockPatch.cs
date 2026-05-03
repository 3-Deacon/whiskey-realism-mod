using System;
using HarmonyLib;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Bridge layer patch #14 — Postfix on MainMenu.CheckForCheckBoxUpdates
    // (sister to HistoricCheckboxLockPatch). Forces 5 realism-related
    // checkboxes ON and visually greyed out (CheckBox.Freeze):
    //
    //   FogOfWarCB       → GameVars.usefow = true            (war fog ON)
    //   OrderDelaysCB    → GameVars.useorderdelays = true    (delays ON)
    //   FeudsCB          → debug_deactivatefeuds = false     (feuds active)
    //   FullReadinessCB  → GameVars.fullreadiness = false    (readiness gameplay ACTIVE; vanilla flag is inverted)
    //   AllAutomanageCB  → automanage all subsystems ON
    //
    // CheckBox.Freeze(true) (decompile line ~186890) sets `frozen` so
    // CheckClicks() ignores user input — visually greyed out.
    [HarmonyPatch]
    internal static class RealismCheckboxesLockPatch
    {
        [HarmonyTargetMethod]
        internal static System.Reflection.MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("MainMenu");
            return AccessTools.Method(t, "CheckForCheckBoxUpdates");
        }

        [HarmonyPostfix]
        internal static void Postfix(object __instance)
        {
            if (Plugin.Instance == null || !Plugin.Instance.OverrideVanillaSettings.Value) return;
            try
            {
                OnceLog.Info("settings:realism", "RealismCheckboxesLockPatch wired");

                ForceAndFreezeByName(__instance, "FogOfWarCB",      true);
                ForceAndFreezeByName(__instance, "OrderDelaysCB",   true);
                ForceAndFreezeByName(__instance, "FeudsCB",         true);
                ForceAndFreezeByName(__instance, "FullReadinessCB", true);
                ForceAndFreezeByName(__instance, "AllAutomanageCB", true);

                // Belt-and-suspenders — write the underlying GameVars directly
                // so a frame's worth of staleness doesn't leak into game logic.
                var gv = AccessTools.TypeByName("GameVars");
                AccessTools.Field(gv, "usefow")               ?.SetValue(null, true);
                AccessTools.Field(gv, "useorderdelays")       ?.SetValue(null, true);
                AccessTools.Field(gv, "debug_deactivatefeuds")?.SetValue(null, false);
                AccessTools.Field(gv, "fullreadiness")        ?.SetValue(null, false);
                // Automanage: vanilla MainMenu.SetAutomanage(true) sets all 6 sub-fields. Call it.
                var setAutomanage = AccessTools.Method(__instance.GetType(), "SetAutomanage", new[] { typeof(bool) });
                setAutomanage?.Invoke(__instance, new object[] { true });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[RealismCheckboxesLockPatch] " + ex.Message);
            }
        }

        private static void ForceAndFreezeByName(object instance, string fieldName, bool desired)
        {
            try
            {
                var f = AccessTools.Field(instance.GetType(), fieldName);
                var cb = f?.GetValue(instance);
                if (cb == null) return;

                var checkMethod = AccessTools.Method(cb.GetType(), "Check", new[] { typeof(bool), typeof(bool) });
                checkMethod?.Invoke(cb, new object[] { desired, false });

                var freezeMethod = AccessTools.Method(cb.GetType(), "Freeze", new[] { typeof(bool) });
                freezeMethod?.Invoke(cb, new object[] { true });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[RealismCheckboxesLock] {fieldName}: {ex.Message}");
            }
        }
    }
}
