using System;
using System.Collections;
using HarmonyLib;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Bridge layer patch #12 — Postfix on MainMenu.CheckForCheckBoxUpdates
    // (decompile line 193612). Vanilla pairs CheckBoxes[0] (Historic) and
    // CheckBoxes[1] (Dynamic) as a mutually-exclusive radio. We force
    // CheckBoxes[0]=true / CheckBoxes[1]=false after every state-change pass
    // so the player visually cannot toggle to Dynamic.
    //
    // The vanilla method also calls ChooseHistoricPolicies(historic: false)
    // when the player flips to Dynamic. By the time our Postfix runs, vanilla
    // may have already called that with the wrong arg — we re-invoke with
    // historic: true to put the policies back where they belong.
    [HarmonyPatch]
    internal static class HistoricCheckboxLockPatch
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
                OnceLog.Info("settings:checkbox", "HistoricCheckboxLockPatch wired");

                var t = __instance.GetType();
                var cbField = AccessTools.Field(t, "CheckBoxes");
                if (cbField?.GetValue(__instance) is not Array cbArr) return;
                if (cbArr.Length < 2) return;

                var historicCb = cbArr.GetValue(0);
                var dynamicCb  = cbArr.GetValue(1);
                if (historicCb == null || dynamicCb == null) return;

                bool historicWasOff = !ReadIsActive(historicCb);
                bool dynamicWasOn   = ReadIsActive(dynamicCb);

                // Force radio-state: Historic ON, Dynamic OFF.
                ForceCheck(historicCb, true);
                ForceCheck(dynamicCb, false);

                // Sync the lastcheckboxstates cache so vanilla doesn't fire its
                // change-detected branch on the next call.
                var lastField = AccessTools.Field(t, "lastcheckboxstates");
                if (lastField?.GetValue(__instance) is bool[] last && last.Length >= 2)
                {
                    last[0] = true;
                    last[1] = false;
                }

                // If the player just flipped to Dynamic, vanilla called
                // ChooseHistoricPolicies(historic: false). Reverse it.
                if (historicWasOff || dynamicWasOn)
                {
                    var chooseMethod = AccessTools.Method(t, "ChooseHistoricPolicies", new[] { typeof(bool) });
                    chooseMethod?.Invoke(__instance, new object[] { true });
                    Plugin.Log.LogInfo("[Settings] Historic checkbox override: forced back to Historic (player tried to switch to Dynamic)");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[HistoricCheckboxLockPatch] " + ex.Message);
            }
        }

        private static bool ReadIsActive(object checkBox)
        {
            if (checkBox == null) return false;
            var f = AccessTools.Field(checkBox.GetType(), "isactive");
            return f != null && (bool)f.GetValue(checkBox);
        }

        private static void ForceCheck(object checkBox, bool desired)
        {
            if (checkBox == null) return;
            // Vanilla signature: public void Check(bool newstate = true, bool manuallyset = false)
            // (decompile line 186823). Pass both args explicitly.
            var checkMethod = AccessTools.Method(checkBox.GetType(), "Check", new[] { typeof(bool), typeof(bool) });
            if (checkMethod != null)
            {
                checkMethod.Invoke(checkBox, new object[] { desired, false });
                return;
            }
            var f = AccessTools.Field(checkBox.GetType(), "isactive");
            f?.SetValue(checkBox, desired);
        }
    }
}
