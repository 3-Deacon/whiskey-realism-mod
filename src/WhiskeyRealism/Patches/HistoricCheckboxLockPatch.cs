using System;
using System.Collections;
using System.Reflection;
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
                var t = __instance.GetType();
                ResolveMembers(t);
                if (_checkBoxesField?.GetValue(__instance) is not Array cbArr) return;
                if (cbArr.Length < 2) return;

                var historicCb = cbArr.GetValue(0);
                var dynamicCb  = cbArr.GetValue(1);
                if (historicCb == null || dynamicCb == null) return;

                bool historicWasOff = !ReadIsActive(historicCb);
                bool dynamicWasOn   = ReadIsActive(dynamicCb);
                bool alreadyLocked = !historicWasOff && !dynamicWasOn &&
                    ReadFrozen(historicCb) && ReadFrozen(dynamicCb) &&
                    LastStatesSynced(__instance);
                if (alreadyLocked) return;

                OnceLog.Info("settings:checkbox", "HistoricCheckboxLockPatch wired");

                // Force radio-state: Historic ON, Dynamic OFF.
                ForceCheck(historicCb, true);
                ForceCheck(dynamicCb, false);

                // Freeze both — disabled-look + clicks ignored (CheckBox.frozen).
                Freeze(historicCb, true);
                Freeze(dynamicCb, true);

                // Sync the lastcheckboxstates cache so vanilla doesn't fire its
                // change-detected branch on the next call.
                if (_lastCheckboxStatesField?.GetValue(__instance) is bool[] last && last.Length >= 2)
                {
                    last[0] = true;
                    last[1] = false;
                }

                // If the player just flipped to Dynamic, vanilla called
                // ChooseHistoricPolicies(historic: false). Reverse it.
                if (historicWasOff || dynamicWasOn)
                {
                    _chooseHistoricPoliciesMethod?.Invoke(__instance, new object[] { true });
                    Plugin.Log.LogInfo("[Settings] Historic checkbox override: forced back to Historic (player tried to switch to Dynamic)");
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("settings:checkbox:error", "[HistoricCheckboxLockPatch] " + ex.Message);
            }
        }

        private static void ResolveMembers(Type mainMenuType)
        {
            if (_mainMenuType == mainMenuType) return;
            _mainMenuType = mainMenuType;
            _checkBoxesField = AccessTools.Field(mainMenuType, "CheckBoxes");
            _lastCheckboxStatesField = AccessTools.Field(mainMenuType, "lastcheckboxstates");
            _chooseHistoricPoliciesMethod = AccessTools.Method(mainMenuType, "ChooseHistoricPolicies", new[] { typeof(bool) });
        }

        private static void ResolveCheckboxMembers(Type checkBoxType)
        {
            if (_checkBoxType == checkBoxType) return;
            _checkBoxType = checkBoxType;
            _isActiveField = AccessTools.Field(checkBoxType, "isactive");
            _frozenField = AccessTools.Field(checkBoxType, "frozen");
            _checkMethod = AccessTools.Method(checkBoxType, "Check", new[] { typeof(bool), typeof(bool) });
            _freezeMethod = AccessTools.Method(checkBoxType, "Freeze", new[] { typeof(bool) });
        }

        private static bool LastStatesSynced(object instance)
        {
            if (_lastCheckboxStatesField?.GetValue(instance) is bool[] last && last.Length >= 2)
                return last[0] && !last[1];
            return false;
        }

        private static bool ReadIsActive(object checkBox)
        {
            if (checkBox == null) return false;
            ResolveCheckboxMembers(checkBox.GetType());
            return _isActiveField != null && (bool)_isActiveField.GetValue(checkBox);
        }

        private static bool ReadFrozen(object checkBox)
        {
            if (checkBox == null) return false;
            ResolveCheckboxMembers(checkBox.GetType());
            return _frozenField != null && (bool)_frozenField.GetValue(checkBox);
        }

        private static void ForceCheck(object checkBox, bool desired)
        {
            if (checkBox == null) return;
            ResolveCheckboxMembers(checkBox.GetType());
            // Vanilla signature: public void Check(bool newstate = true, bool manuallyset = false)
            // (decompile line 186823). Pass both args explicitly.
            if (_checkMethod != null)
            {
                _checkMethod.Invoke(checkBox, new object[] { desired, false });
                return;
            }
            _isActiveField?.SetValue(checkBox, desired);
        }

        private static void Freeze(object checkBox, bool freeze)
        {
            if (checkBox == null) return;
            ResolveCheckboxMembers(checkBox.GetType());
            _freezeMethod?.Invoke(checkBox, new object[] { freeze });
        }

        private static Type _mainMenuType;
        private static Type _checkBoxType;
        private static FieldInfo _checkBoxesField;
        private static FieldInfo _lastCheckboxStatesField;
        private static FieldInfo _isActiveField;
        private static FieldInfo _frozenField;
        private static MethodInfo _chooseHistoricPoliciesMethod;
        private static MethodInfo _checkMethod;
        private static MethodInfo _freezeMethod;
    }
}
