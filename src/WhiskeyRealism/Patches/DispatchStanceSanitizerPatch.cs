using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla Messages.Message.GenerateMessageContent() renders W&L dispatch text from unit stance;
    // this Postfix only sanitizes known bad stance-0 wording in newly generated player-chain dispatches.
    [HarmonyPatch(typeof(Messages.Message), "GenerateMessageContent")]
    internal static class DispatchStanceSanitizerPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(Messages.Message __instance)
        {
            using (TelemetryPerf.Scope("campaign.patch.dispatch-sanitizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                try
                {
                    if (Plugin.Instance == null || Plugin.Instance.Enabled == null || !Plugin.Instance.Enabled.Value) return;
                    if (__instance == null || !WlDispatchSanitizer.IsCandidateType(__instance.type)) return;
                    if (!DLC_WL.dlc_scenarioactive) return;

                    Regiment unit = ResolveMessageUnit(__instance);
                    if (!IsPlayerChainWlUnit(unit)) return;

                    WlDispatchSanitizerResult result = WlDispatchSanitizer.Sanitize(__instance.type, __instance.content);
                    if (!result.Changed) return;

                    __instance.content = result.Content;
                    OnceLog.Info(
                        "wl-dispatch-sanitizer:sanitized:" + __instance.type,
                        "[W&LDispatch] sanitized stance text for message type " + __instance.type + ": " + Preview(result.Content));
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("wl-dispatch-sanitizer:postfix", "[W&LDispatch] sanitizer Postfix failed: " + ex.Message);
                }
            }
        }

        private static Regiment ResolveMessageUnit(Messages.Message message)
        {
            if (message.type == 57) return message.sender;
            return message.regref != null ? message.regref : message.sender;
        }

        private static bool IsPlayerChainWlUnit(Regiment unit)
        {
            if (unit == null) return false;
            if (unit.alliance != GameVars.playeralliance) return false;
            return unit.dlcw_isundercommander || DLC_WL.IsPlayerPartOfUnit(unit);
        }

        private static string Preview(string content)
        {
            if (string.IsNullOrEmpty(content)) return "";
            return content.Length <= 80 ? content : content.Substring(0, 80);
        }
    }
}
