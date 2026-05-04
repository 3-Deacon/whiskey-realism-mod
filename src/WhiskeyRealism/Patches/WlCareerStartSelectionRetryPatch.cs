using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla calls CareerInformationPanel.ShowStartUnitSelectionList once at
    // campaign frame 50. If that one-shot call races panel/list readiness, the
    // W&L career start can remain paused with no command-selection popup. This
    // retry is bounded, only active while our own start gate says the player has
    // no command, and it stops once the unit-selection list is visible.
    [HarmonyPatch(typeof(AICampaign), "Update")]
    internal static class WlCareerStartSelectionRetryPatch
    {
        private const int FirstRetryFrame = 50;
        private const int RetryEveryUnityFrames = 15;
        private const int MaxAttempts = 40;

        private static int _lastAttemptUnityFrame = -1;
        private static int _attempts;
        private static object _careerPanel;
        private static FieldInfo _gameFrameField;
        private static FieldInfo _unitSelectionListObjectField;
        private static MethodInfo _showStartUnitSelectionListMethod;

        [HarmonyPostfix]
        internal static void Postfix()
        {
            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                if (!StrategicCoordinator.WlCareerStartPending())
                {
                    _attempts = 0;
                    _lastAttemptUnityFrame = -1;
                    return;
                }

                int frame = ReadFrame();
                if (frame < FirstRetryFrame) return;
                if (_attempts >= MaxAttempts) return;
                int unityFrame = Time.frameCount;
                if (_lastAttemptUnityFrame >= 0 && unityFrame - _lastAttemptUnityFrame < RetryEveryUnityFrames) return;

                var panel = ResolveCareerPanel();
                if (panel == null)
                {
                    OnceLog.Warning("wl-start-selection:no-panel", "[W&LStartSelection] retry skipped: CareerInformation panel unavailable");
                    return;
                }

                if (UnitSelectionListVisible(panel)) return;
                if (_showStartUnitSelectionListMethod == null)
                {
                    OnceLog.Warning("wl-start-selection:no-method", "[W&LStartSelection] retry skipped: ShowStartUnitSelectionList method unavailable");
                    return;
                }

                _lastAttemptUnityFrame = unityFrame;
                _attempts++;
                _showStartUnitSelectionListMethod.Invoke(panel, new object[] { true });

                if (_attempts == 1 || Plugin.Instance.VerboseLogging.Value)
                    Plugin.Log.LogInfo($"[W&LStartSelection] retried command-selection popup gameFrame={frame} unityFrame={unityFrame} attempt={_attempts} {DescribeWlStatus()}");
                if (_attempts >= MaxAttempts && !UnitSelectionListVisible(panel))
                    OnceLog.Warning("wl-start-selection:max-attempts", $"[W&LStartSelection] command-selection popup still hidden after {_attempts} retries; {DescribeWlStatus()}");
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
            _unitSelectionListObjectField = AccessTools.Field(panelType, "UnitSelectionListObject");
            _showStartUnitSelectionListMethod = AccessTools.Method(panelType, "ShowStartUnitSelectionList", new[] { typeof(bool) });
            return _careerPanel;
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
}
