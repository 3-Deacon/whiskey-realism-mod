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
    // start gate says the player has no command, and it stops once the
    // unit-selection list is visible.
    [HarmonyPatch(typeof(AICampaign), "Update")]
    internal static class WlCareerStartSelectionRetryPatch
    {
        private const int RetryEveryUnityFrames = 15;
        private const int MaxAttempts = 120;

        private static readonly WlStartSelectionRetryGate RetryGate = new WlStartSelectionRetryGate(MaxAttempts, RetryEveryUnityFrames);
        private static object _careerPanel;
        private static FieldInfo _controllerCareerPanelField;
        private static FieldInfo _gameFrameField;
        private static FieldInfo _unitSelectionListObjectField;
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
                if (UnitSelectionListVisible(panel)) return;
                if (_showStartUnitSelectionListMethod == null)
                {
                    OnceLog.Warning("wl-start-selection:no-method", "[W&LStartSelection] retry skipped: ShowStartUnitSelectionList method unavailable");
                    return;
                }
                if (!RetryGate.ShouldAttempt(pending: true, listVisible: false, panelAvailable: true, unityFrame: unityFrame)) return;

                _showStartUnitSelectionListMethod.Invoke(panel, new object[] { true });

                if (RetryGate.Attempts == 1 || Plugin.Instance.VerboseLogging.Value)
                    Plugin.Log.LogInfo($"[W&LStartSelection] retried command-selection popup source={source} gameFrame={frame} unityFrame={unityFrame} attempt={RetryGate.Attempts} {DescribeWlStatus()}");
                if (RetryGate.Exhausted && !UnitSelectionListVisible(panel))
                    OnceLog.Warning("wl-start-selection:max-attempts", $"[W&LStartSelection] command-selection popup still hidden after {RetryGate.Attempts} retries; {DescribeWlStatus()}");
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-start-selection:retry", "[W&LStartSelection] retry failed: " + ex.Message);
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
            _showStartUnitSelectionListMethod = AccessTools.Method(panelType, "ShowStartUnitSelectionList", new[] { typeof(bool) });
        }

        private static bool UnitSelectionListVisible(object panel)
        {
            var listObject = _unitSelectionListObjectField?.GetValue(panel) as GameObject;
            return listObject != null && listObject.activeInHierarchy;
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
