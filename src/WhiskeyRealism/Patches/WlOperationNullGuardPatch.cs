using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla DLC_WL.CommanderRelations.Operation.UpdateOperation reads
    // usedtopgroup.transform before its null cleanup branch, so missing units can
    // throw before FinishOperation() runs.
    [HarmonyPatch(typeof(DLC_WL.CommanderRelations.Operation), "UpdateOperation")]
    internal static class WlOperationNullGuardPatch
    {
        private static MethodInfo _finishOperationMethod;

        [HarmonyFinalizer]
        internal static Exception Finalizer(
            DLC_WL.CommanderRelations.Operation __instance,
            Exception __exception,
            ref bool __result)
        {
            if (__exception == null) return null;
            if (!Enabled()) return __exception;

            try
            {
                bool hasTopGroup = __instance != null &&
                    (UnityEngine.Object)__instance.usedtopgroup != null;
                if (!WlOperationNullGuard.ShouldFinishMissingOperation(
                        modEnabled: true,
                        operationExists: __instance != null,
                        usedTopGroupExists: hasTopGroup))
                    return __exception;

                FinishOperation(__instance);
                __result = true;
                OnceLog.Warning(
                    "wl-operation-null-guard",
                    "[Patch:WLOperationNullGuard] finished missing W&L operation after vanilla null-before-transform fault.");
                return null;
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "wl-operation-null-guard:failed",
                    "[Patch:WLOperationNullGuard] failed while handling missing operation unit: " + ex.Message);
                return __exception;
            }
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableWlOperationNullGuard != null &&
                Plugin.Instance.EnableWlOperationNullGuard.Value;
        }

        private static void FinishOperation(DLC_WL.CommanderRelations.Operation operation)
        {
            if (_finishOperationMethod == null)
            {
                _finishOperationMethod = AccessTools.Method(
                    typeof(DLC_WL.CommanderRelations.Operation),
                    "FinishOperation");
            }

            if (_finishOperationMethod == null)
            {
                OnceLog.Warning(
                    "wl-operation-null-guard:missing-finish",
                    "[Patch:WLOperationNullGuard] missing FinishOperation anchor; suppressing null operation fault without explicit finish.");
                return;
            }

            _finishOperationMethod.Invoke(operation, null);
        }
    }
}
