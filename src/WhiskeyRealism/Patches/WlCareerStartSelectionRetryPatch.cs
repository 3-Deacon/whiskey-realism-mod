using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla CampaignController.Update calls careerinformationpanel
    // .ShowStartUnitSelectionList() once at GameVars.frame == 50 (decompile 30145-30155),
    // then pauses the campaign. ShowStartUnitSelectionList (182353) only opens the list
    // for a W&L scenario whose chosen commander has no command. If that one-shot call
    // races panel/list readiness, the career start can remain paused with no
    // command-selection popup. This retry is bounded, only active while our own start
    // gate says the player has no command, waits for vanilla's frame-50 startup point,
    // and stops once the unit-selection list is usable.
    //
    // Both postfixes run every frame, so the pending check comes before any lookup, the
    // panel is cached (Unity-null aware) and GameObject.Find is throttled
    // (WlStartSelectionPanelLookup).
    [HarmonyPatch(typeof(AICampaign), "Update")]
    internal static class WlCareerStartSelectionRetryPatch
    {
        private const int RetryEveryUnityFrames = 15;
        private const int MaxAttempts = 120;
        private const int MinReadyCampaignFrame = 50;

        private static readonly WlStartSelectionRetryGate RetryGate = new WlStartSelectionRetryGate(MaxAttempts, RetryEveryUnityFrames, MinReadyCampaignFrame);
        private static readonly FieldInfo ControllerCareerPanelField = AccessTools.Field(typeof(CampaignController), "careerinformationpanel");
        private static readonly FieldInfo UnitLineAppointCommandField = AccessTools.Field(typeof(CareerInformationPanel), "unitlineappointcommand");
        private static CareerInformationPanel _careerPanel;
        private static int _nextFindFrame;
        private static readonly System.Collections.Generic.HashSet<string> LogOnceKeys =
            new System.Collections.Generic.HashSet<string>();

        [HarmonyPostfix]
        internal static void Postfix()
        {
            using (TelemetryPerf.Scope("campaign.patch.wl-career-start-retry.ai", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.wl-career-start-retry.ai");
                try
                {
                    if (!RetryActive()) return;
                    TryRetry(ResolveCareerPanel(), "ai");
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("wl-start-selection:ai-postfix", "[W&LStartSelection] postfix failed: " + DescribeException(ex));
                }
            }
        }

        // Cheap gate evaluated before any panel lookup.
        internal static bool RetryActive()
        {
            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return false;
                if (StrategicCoordinator.WlCareerStartPending()) return true;
                RetryGate.Reset();
                return false;
            }
            catch
            {
                return false;
            }
        }

        internal static void TryRetry(CareerInformationPanel panel, string source)
        {
            try
            {
                EmitCampaignInfoOnce("wl-start-selection", () => "[W&LStartSelection] retry patch active while command selection is pending");
                int frame = GameVars.frame;
                int unityFrame = Time.frameCount;

                if (panel == null)
                {
                    OnceLog.Warning("wl-start-selection:no-panel:" + source, "[W&LStartSelection] retry skipped from " + source + ": CareerInformation panel unavailable");
                    return;
                }

                bool listVisible = UnitSelectionListVisible(panel);
                int lineCount = UnitSelectionLineCount(panel);
                bool listUsable = listVisible && lineCount > 0;
                if (listUsable)
                {
                    EmitCampaignInfoOnce("wl-start-selection:visible-pending", () => $"[W&LStartSelection] command-selection list is usable; waiting for player command source={source} gameFrame={frame} unityFrame={unityFrame} lines={lineCount} {DescribeWlStatus()}");
                    return;
                }
                if (frame < MinReadyCampaignFrame)
                    EmitCampaignInfoOnce("wl-start-selection:waiting-ready", () => $"[W&LStartSelection] retry waiting for vanilla frame {MinReadyCampaignFrame} source={source} gameFrame={frame} unityFrame={unityFrame} {DescribeWlStatus()}");
                else if (listVisible)
                    EmitCampaignInfoOnce("wl-start-selection:visible-empty", () => $"[W&LStartSelection] command-selection panel is visible but has no selectable rows; retrying source={source} gameFrame={frame} unityFrame={unityFrame} contentActive={UnitSelectionContentActive(panel)} lines={lineCount} {DescribeWlStatus()}");
                if (!RetryGate.ShouldAttempt(pending: true, listVisible: listUsable, panelAvailable: true, campaignFrame: frame, startupDataReady: false, unityFrame: unityFrame)) return;

                panel.ShowStartUnitSelectionList(true);

                if (RetryGate.Attempts == 1 || Plugin.Instance.VerboseLogging.Value)
                    TelemetryRouter.LegacyInfo($"[W&LStartSelection] retried command-selection popup source={source} gameFrame={frame} unityFrame={unityFrame} attempt={RetryGate.Attempts} linesBefore={lineCount} linesAfter={UnitSelectionLineCount(panel)} {DescribeWlStatus()}", TelemetryLayer.Campaign);
                if (RetryGate.Exhausted && !UnitSelectionListVisible(panel))
                    OnceLog.Warning("wl-start-selection:max-attempts", $"[W&LStartSelection] command-selection popup still hidden after {RetryGate.Attempts} retries; {DescribeWlStatus()}");
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-start-selection:retry", "[W&LStartSelection] retry failed: " + DescribeException(ex));
            }
        }

        private static void EmitCampaignInfoOnce(string key, Func<string> line)
        {
            if (!LogOnceKeys.Add(key ?? string.Empty)) return;
            TelemetryRouter.LegacyInfo(line(), TelemetryLayer.Campaign);
        }

        private static CareerInformationPanel ResolveCareerPanel()
        {
            bool alive = _careerPanel != null; // Unity null: destroyed panels drop out
            int unityFrame = Time.frameCount;
            if (!WlStartSelectionPanelLookup.ShouldFindPanel(pending: true, alive, unityFrame, _nextFindFrame))
                return alive ? _careerPanel : null;

            _nextFindFrame = WlStartSelectionPanelLookup.NextFindFrame(unityFrame);
            var go = GameObject.Find("UI/CareerInformation");
            _careerPanel = go != null ? go.GetComponent<CareerInformationPanel>() : null;
            return _careerPanel;
        }

        internal static CareerInformationPanel ResolveCareerPanelFromController(CampaignController controller)
        {
            if (_careerPanel != null) return _careerPanel;
            if (controller != null && ControllerCareerPanelField != null)
            {
                var panel = ControllerCareerPanelField.GetValue(controller) as CareerInformationPanel;
                if (panel != null)
                {
                    _careerPanel = panel;
                    return panel;
                }
            }

            return ResolveCareerPanel();
        }

        private static bool UnitSelectionListVisible(CareerInformationPanel panel)
        {
            var listObject = panel.UnitSelectionListObject;
            return listObject != null && listObject.activeInHierarchy;
        }

        private static bool UnitSelectionContentActive(CareerInformationPanel panel)
        {
            var contentObject = panel.UnitSelectionListContentObject;
            return contentObject != null && contentObject.activeInHierarchy;
        }

        private static int UnitSelectionLineCount(CareerInformationPanel panel)
        {
            var lines = UnitLineAppointCommandField?.GetValue(panel) as IList;
            return lines?.Count ?? 0;
        }

        private static string DescribeWlStatus()
        {
            try
            {
                bool active = DLC_WL.dlc_scenarioactive;
                int chosen = DLC_WL.dlc_chosencommander;
                var commanders = GameVars.commander;
                bool hasCommand = commanders != null && chosen >= 0 && chosen < commanders.Count &&
                    commanders[chosen] != null && commanders[chosen].currentcommand != null;
                return $"active={active} chosen={chosen} commanders={commanders?.Count ?? -1} hasCommand={hasCommand}";
            }
            catch
            {
                return "status=unavailable";
            }
        }

        private static string DescribeException(Exception ex)
        {
            if (ex is TargetInvocationException target && target.InnerException != null)
                return target.InnerException.GetType().Name + ": " + target.InnerException.Message;
            return ex.GetType().Name + ": " + ex.Message;
        }
    }

    [HarmonyPatch(typeof(CampaignController), "Update")]
    internal static class WlCareerStartSelectionRetryCampaignControllerPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(CampaignController __instance)
        {
            using (TelemetryPerf.Scope("campaign.patch.wl-career-start-retry.campaign", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.wl-career-start-retry.campaign");
                try
                {
                    if (!WlCareerStartSelectionRetryPatch.RetryActive()) return;
                    WlCareerStartSelectionRetryPatch.TryRetry(WlCareerStartSelectionRetryPatch.ResolveCareerPanelFromController(__instance), "campaign");
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("wl-start-selection:campaign-postfix", "[W&LStartSelection] campaign postfix failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }
    }
}
