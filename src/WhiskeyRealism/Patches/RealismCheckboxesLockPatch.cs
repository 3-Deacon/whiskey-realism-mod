using System;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Telemetry;
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
            using (TelemetryPerf.Scope("campaign.patch.realism-checkboxes-lock", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
            if (Plugin.Instance == null || !Plugin.Instance.OverrideVanillaSettings.Value) return;
            try
            {
                ResolveMembers(__instance.GetType());
                if (RealismStateAlreadyLocked(__instance)) return;

                OnceLog.Info("settings:realism", "RealismCheckboxesLockPatch wired");

                ForceAndFreeze(_fogOfWarField?.GetValue(__instance), true);
                ForceAndFreeze(_orderDelaysField?.GetValue(__instance), true);
                ForceAndFreeze(_feudsField?.GetValue(__instance), true);
                ForceAndFreeze(_fullReadinessField?.GetValue(__instance), true);
                ForceAndFreeze(_allAutomanageField?.GetValue(__instance), true);

                // Belt-and-suspenders — write the underlying GameVars directly
                // so a frame's worth of staleness doesn't leak into game logic.
                _useFowField?.SetValue(null, true);
                _useOrderDelaysField?.SetValue(null, true);
                _debugDeactivateFeudsField?.SetValue(null, false);
                _fullReadinessGameVarField?.SetValue(null, false);
                // Automanage: vanilla MainMenu.SetAutomanage(true) sets all 6 sub-fields. Call it.
                if (!AutomanageAlreadyOn())
                    _setAutomanageMethod?.Invoke(__instance, new object[] { true });
            }
            catch (Exception ex)
            {
                OnceLog.Warning("settings:realism:error", "[RealismCheckboxesLockPatch] " + ex.Message);
            }
            } // end using TelemetryPerf.Scope
        }

        private static void ResolveMembers(Type mainMenuType)
        {
            if (_mainMenuType == mainMenuType) return;
            _mainMenuType = mainMenuType;
            _fogOfWarField = AccessTools.Field(mainMenuType, "FogOfWarCB");
            _orderDelaysField = AccessTools.Field(mainMenuType, "OrderDelaysCB");
            _feudsField = AccessTools.Field(mainMenuType, "FeudsCB");
            _fullReadinessField = AccessTools.Field(mainMenuType, "FullReadinessCB");
            _allAutomanageField = AccessTools.Field(mainMenuType, "AllAutomanageCB");
            _setAutomanageMethod = AccessTools.Method(mainMenuType, "SetAutomanage", new[] { typeof(bool) });

            var gv = AccessTools.TypeByName("GameVars");
            _useFowField = AccessTools.Field(gv, "usefow");
            _useOrderDelaysField = AccessTools.Field(gv, "useorderdelays");
            _debugDeactivateFeudsField = AccessTools.Field(gv, "debug_deactivatefeuds");
            _fullReadinessGameVarField = AccessTools.Field(gv, "fullreadiness");
            _automanageConstructionPField = AccessTools.Field(gv, "automanageconstructionp");
            _automanageConstructionGField = AccessTools.Field(gv, "automanageconstructiong");
            _automanageFinancesField = AccessTools.Field(gv, "automanagefinances");
            _automanagePoliciesField = AccessTools.Field(gv, "automanagepolicies");
            _automanageProjectsField = AccessTools.Field(gv, "automanageprojects");
            _automanageWeaponsField = AccessTools.Field(gv, "automanageweapons");
        }

        private static bool RealismStateAlreadyLocked(object instance)
        {
            return CheckboxLocked(_fogOfWarField?.GetValue(instance), true)
                && CheckboxLocked(_orderDelaysField?.GetValue(instance), true)
                && CheckboxLocked(_feudsField?.GetValue(instance), true)
                && CheckboxLocked(_fullReadinessField?.GetValue(instance), true)
                && CheckboxLocked(_allAutomanageField?.GetValue(instance), true)
                && ReadStaticBool(_useFowField) == true
                && ReadStaticBool(_useOrderDelaysField) == true
                && ReadStaticBool(_debugDeactivateFeudsField) == false
                && ReadStaticBool(_fullReadinessGameVarField) == false
                && AutomanageAlreadyOn();
        }

        private static bool AutomanageAlreadyOn()
        {
            return ReadStaticBool(_automanageConstructionPField) == true
                && ReadStaticBool(_automanageConstructionGField) == true
                && ReadStaticBool(_automanageFinancesField) == true
                && ReadStaticBool(_automanagePoliciesField) == true
                && ReadStaticBool(_automanageProjectsField) == true
                && ReadStaticBool(_automanageWeaponsField) == true;
        }

        private static bool? ReadStaticBool(FieldInfo field)
        {
            if (field == null) return null;
            return field.GetValue(null) as bool?;
        }

        private static bool CheckboxLocked(object cb, bool desired)
        {
            if (cb == null) return false;
            ResolveCheckboxMembers(cb.GetType());
            bool active = _isActiveField != null && (bool)_isActiveField.GetValue(cb);
            bool frozen = _frozenField != null && (bool)_frozenField.GetValue(cb);
            return active == desired && frozen;
        }

        private static void ForceAndFreeze(object cb, bool desired)
        {
            try
            {
                if (cb == null) return;

                ResolveCheckboxMembers(cb.GetType());
                _checkMethod?.Invoke(cb, new object[] { desired, false });

                _freezeMethod?.Invoke(cb, new object[] { true });
            }
            catch (Exception ex)
            {
                OnceLog.Warning("settings:realism:checkbox", "[RealismCheckboxesLock] " + ex.Message);
            }
        }

        private static void ResolveCheckboxMembers(Type checkBoxType)
        {
            if (_checkBoxType == checkBoxType) return;
            _checkBoxType = checkBoxType;
            _checkMethod = AccessTools.Method(checkBoxType, "Check", new[] { typeof(bool), typeof(bool) });
            _freezeMethod = AccessTools.Method(checkBoxType, "Freeze", new[] { typeof(bool) });
            _isActiveField = AccessTools.Field(checkBoxType, "isactive");
            _frozenField = AccessTools.Field(checkBoxType, "frozen");
        }

        private static Type _mainMenuType;
        private static Type _checkBoxType;
        private static FieldInfo _fogOfWarField;
        private static FieldInfo _orderDelaysField;
        private static FieldInfo _feudsField;
        private static FieldInfo _fullReadinessField;
        private static FieldInfo _allAutomanageField;
        private static FieldInfo _useFowField;
        private static FieldInfo _useOrderDelaysField;
        private static FieldInfo _debugDeactivateFeudsField;
        private static FieldInfo _fullReadinessGameVarField;
        private static FieldInfo _automanageConstructionPField;
        private static FieldInfo _automanageConstructionGField;
        private static FieldInfo _automanageFinancesField;
        private static FieldInfo _automanagePoliciesField;
        private static FieldInfo _automanageProjectsField;
        private static FieldInfo _automanageWeaponsField;
        private static MethodInfo _setAutomanageMethod;
        private static MethodInfo _checkMethod;
        private static MethodInfo _freezeMethod;
        private static FieldInfo _isActiveField;
        private static FieldInfo _frozenField;
    }
}
