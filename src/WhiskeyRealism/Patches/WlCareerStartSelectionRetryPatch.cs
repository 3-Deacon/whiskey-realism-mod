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
        private const int MinReadyCampaignFrame = 50;

        private static readonly WlStartSelectionRetryGate RetryGate = new WlStartSelectionRetryGate(MaxAttempts, RetryEveryUnityFrames, MinReadyCampaignFrame);
        private static object _careerPanel;
        private static FieldInfo _controllerCareerPanelField;
        private static FieldInfo _gameFrameField;
        private static FieldInfo _unitSelectionListObjectField;
        private static MethodInfo _showStartUnitSelectionListMethod;
        private static MethodInfo _getUnitListForStartSelectionMethod;

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
                bool startupDataReady = StartupSelectionDataReady(panel);
                if (frame < MinReadyCampaignFrame && !startupDataReady)
                    OnceLog.Info("wl-start-selection:waiting-ready", $"[W&LStartSelection] retry waiting for startup data source={source} gameFrame={frame} unityFrame={unityFrame} {DescribeWlStatus()}");
                if (!RetryGate.ShouldAttempt(pending: true, listVisible: false, panelAvailable: true, campaignFrame: frame, startupDataReady: startupDataReady, unityFrame: unityFrame)) return;
                if (frame < MinReadyCampaignFrame)
                    OnceLog.Info("wl-start-selection:stalled-ready", $"[W&LStartSelection] campaign frame below {MinReadyCampaignFrame}, but startup data is ready; retrying source={source} gameFrame={frame} unityFrame={unityFrame}");

                _showStartUnitSelectionListMethod.Invoke(panel, new object[] { true });

                if (RetryGate.Attempts == 1 || Plugin.Instance.VerboseLogging.Value)
                    Plugin.Log.LogInfo($"[W&LStartSelection] retried command-selection popup source={source} gameFrame={frame} unityFrame={unityFrame} attempt={RetryGate.Attempts} {DescribeWlStatus()}");
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
            _showStartUnitSelectionListMethod = AccessTools.Method(panelType, "ShowStartUnitSelectionList", new[] { typeof(bool) });
        }

        private static bool UnitSelectionListVisible(object panel)
        {
            var listObject = _unitSelectionListObjectField?.GetValue(panel) as GameObject;
            return listObject != null && listObject.activeInHierarchy;
        }

        private static bool StartupSelectionDataReady(object panel)
        {
            try
            {
                if (panel == null || _unitSelectionListObjectField?.GetValue(panel) == null) return false;

                var dlcType = AccessTools.TypeByName("DLC_WL");
                var gv = AccessTools.TypeByName("GameVars");
                if (dlcType == null || gv == null) return false;
                bool active = Convert.ToBoolean(AccessTools.Field(dlcType, "dlc_scenarioactive")?.GetValue(null) ?? false);
                if (!active) return false;
                int chosen = Convert.ToInt32(AccessTools.Field(dlcType, "dlc_chosencommander")?.GetValue(null) ?? -1);
                var commanders = AccessTools.Field(gv, "commander")?.GetValue(null) as IList;
                if (commanders == null || chosen < 0 || chosen >= commanders.Count) return false;

                var commander = commanders[chosen];
                if (commander == null) return false;
                var commanderType = commander.GetType();
                if (AccessTools.Field(commanderType, "currentcommand")?.GetValue(commander) != null) return false;
                var armyGroup = AccessTools.Method(commanderType, "GetArmyGroup")?.Invoke(commander, Array.Empty<object>());
                if (armyGroup != null) return false;

                if (_getUnitListForStartSelectionMethod == null)
                    _getUnitListForStartSelectionMethod = AccessTools.Method(dlcType, "GetUnitListForStartSelection");
                if (_getUnitListForStartSelectionMethod == null) return false;

                float prestige = Convert.ToSingle(AccessTools.Field(dlcType, "prestige")?.GetValue(null) ?? 0f);
                var units = _getUnitListForStartSelectionMethod.Invoke(null, new[] { commander, (object)prestige }) as IList;
                return units != null && units.Count > 0;
            }
            catch (NullReferenceException)
            {
                return false;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-start-selection:data-ready", "[W&LStartSelection] startup readiness check failed: " + DescribeException(ex));
                return false;
            }
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
