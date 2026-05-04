using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla calls CareerInformationPanel.ShowStartUnitSelectionList once near
    // campaign start, then pauses the campaign. If that one-shot call races
    // panel/list readiness, the W&L career start can remain paused with no
    // command-selection popup. This retry is bounded, only active while our own
    // start gate says the player has no command, waits for vanilla's frame-50
    // startup point, and stops once the unit-selection list is visible.
    [HarmonyPatch(typeof(AICampaign), "Update")]
    internal static class WlCareerStartSelectionRetryPatch
    {
        private const int RetryEveryUnityFrames = 15;
        private const int MaxAttempts = 120;
        private const int MinReadyCampaignFrame = 50;

        private static readonly WlStartSelectionRetryGate RetryGate = new WlStartSelectionRetryGate(MaxAttempts, RetryEveryUnityFrames, MinReadyCampaignFrame);
        private static object _careerPanel;
        private static FieldInfo _controllerCareerPanelField;
        private static FieldInfo _gameFrameField;
        private static FieldInfo _unitSelectionListObjectField;
        private static FieldInfo _unitSelectionListContentObjectField;
        private static FieldInfo _unitLineAppointCommandField;
        private static MethodInfo _showStartUnitSelectionListMethod;

        [HarmonyPostfix]
        internal static void Postfix()
        {
            TryRetry(ResolveCareerPanel(), "ai");
        }

        internal static void TryRetry(object panel, string source)
        {
            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                if (!StrategicCoordinator.WlCareerStartPending())
                {
                    RetryGate.Reset();
                    return;
                }

                OnceLog.Info("wl-start-selection", "[W&LStartSelection] retry patch active while command selection is pending");
                int frame = ReadFrame();
                int unityFrame = Time.frameCount;

                if (panel == null)
                {
                    OnceLog.Warning("wl-start-selection:no-panel:" + source, $"[W&LStartSelection] retry skipped from {source}: CareerInformation panel unavailable");
                    return;
                }

                CachePanelMembers(panel.GetType());
                bool listVisible = UnitSelectionListVisible(panel);
                int lineCount = UnitSelectionLineCount(panel);
                bool listUsable = listVisible && lineCount > 0;
                if (listUsable)
                {
                    OnceLog.Info("wl-start-selection:visible-pending", $"[W&LStartSelection] command-selection list is usable; waiting for player command source={source} gameFrame={frame} unityFrame={unityFrame} lines={lineCount} {DescribeWlStatus()}");
                    return;
                }
                if (_showStartUnitSelectionListMethod == null)
                {
                    OnceLog.Warning("wl-start-selection:no-method", "[W&LStartSelection] retry skipped: ShowStartUnitSelectionList method unavailable");
                    return;
                }
                if (frame < MinReadyCampaignFrame)
                    OnceLog.Info("wl-start-selection:waiting-ready", $"[W&LStartSelection] retry waiting for vanilla frame {MinReadyCampaignFrame} source={source} gameFrame={frame} unityFrame={unityFrame} {DescribeWlStatus()}");
                else if (listVisible)
                    OnceLog.Info("wl-start-selection:visible-empty", $"[W&LStartSelection] command-selection panel is visible but has no selectable rows; retrying source={source} gameFrame={frame} unityFrame={unityFrame} contentActive={UnitSelectionContentActive(panel)} lines={lineCount} {DescribeWlStatus()}");
                if (!RetryGate.ShouldAttempt(pending: true, listVisible: listUsable, panelAvailable: true, campaignFrame: frame, startupDataReady: false, unityFrame: unityFrame)) return;

                _showStartUnitSelectionListMethod.Invoke(panel, new object[] { true });

                if (RetryGate.Attempts == 1 || Plugin.Instance.VerboseLogging.Value)
                    Plugin.Log.LogInfo($"[W&LStartSelection] retried command-selection popup source={source} gameFrame={frame} unityFrame={unityFrame} attempt={RetryGate.Attempts} linesBefore={lineCount} linesAfter={UnitSelectionLineCount(panel)} {DescribeWlStatus()}");
                if (RetryGate.Exhausted && !UnitSelectionListVisible(panel))
                    OnceLog.Warning("wl-start-selection:max-attempts", $"[W&LStartSelection] command-selection popup still hidden after {RetryGate.Attempts} retries; {DescribeWlStatus()}");
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-start-selection:retry", "[W&LStartSelection] retry failed: " + DescribeException(ex));
            }
        }

        private static int ReadFrame()
        {
            if (_gameFrameField == null)
            {
                var gv = AccessTools.TypeByName("GameVars");
                _gameFrameField = AccessTools.Field(gv, "frame");
            }

            return _gameFrameField != null ? Convert.ToInt32(_gameFrameField.GetValue(null)) : -1;
        }

        private static object ResolveCareerPanel()
        {
            if (_careerPanel != null) return _careerPanel;

            var panelType = AccessTools.TypeByName("CareerInformationPanel");
            if (panelType == null) return null;

            var go = GameObject.Find("UI/CareerInformation");
            if (go == null) return null;

            _careerPanel = go.GetComponent(panelType);
            CachePanelMembers(panelType);
            return _careerPanel;
        }

        internal static object ResolveCareerPanelFromController(CampaignController controller)
        {
            if (controller == null) return ResolveCareerPanel();
            if (_controllerCareerPanelField == null)
                _controllerCareerPanelField = AccessTools.Field(typeof(CampaignController), "careerinformationpanel");

            var panel = _controllerCareerPanelField?.GetValue(controller);
            if (panel != null)
            {
                _careerPanel = panel;
                CachePanelMembers(panel.GetType());
                return panel;
            }

            return ResolveCareerPanel();
        }

        private static void CachePanelMembers(Type panelType)
        {
            if (panelType == null) return;
            _unitSelectionListObjectField = AccessTools.Field(panelType, "UnitSelectionListObject");
            _unitSelectionListContentObjectField = AccessTools.Field(panelType, "UnitSelectionListContentObject");
            _unitLineAppointCommandField = AccessTools.Field(panelType, "unitlineappointcommand");
            _showStartUnitSelectionListMethod = AccessTools.Method(panelType, "ShowStartUnitSelectionList", new[] { typeof(bool) });
        }

        private static bool UnitSelectionListVisible(object panel)
        {
            var listObject = _unitSelectionListObjectField?.GetValue(panel) as GameObject;
            return listObject != null && listObject.activeInHierarchy;
        }

        private static bool UnitSelectionContentActive(object panel)
        {
            var contentObject = _unitSelectionListContentObjectField?.GetValue(panel) as GameObject;
            return contentObject != null && contentObject.activeInHierarchy;
        }

        private static int UnitSelectionLineCount(object panel)
        {
            var lines = _unitLineAppointCommandField?.GetValue(panel) as IList;
            return lines?.Count ?? 0;
        }

        private static string DescribeWlStatus()
        {
            try
            {
                var dlcType = AccessTools.TypeByName("DLC_WL");
                var gv = AccessTools.TypeByName("GameVars");
                bool active = Convert.ToBoolean(AccessTools.Field(dlcType, "dlc_scenarioactive")?.GetValue(null) ?? false);
                int chosen = Convert.ToInt32(AccessTools.Field(dlcType, "dlc_chosencommander")?.GetValue(null) ?? -1);
                var commanders = AccessTools.Field(gv, "commander")?.GetValue(null) as IList;
                bool hasCommand = false;
                if (commanders != null && chosen >= 0 && chosen < commanders.Count)
                {
                    var commander = commanders[chosen];
                    hasCommand = commander != null && AccessTools.Field(commander.GetType(), "currentcommand")?.GetValue(commander) != null;
                }
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
            WlCareerStartSelectionRetryPatch.TryRetry(WlCareerStartSelectionRetryPatch.ResolveCareerPanelFromController(__instance), "campaign");
        }
    }
}
