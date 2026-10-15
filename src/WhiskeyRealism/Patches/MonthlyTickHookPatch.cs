using System;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    [HarmonyPatch(typeof(AICampaign), "Update")]
    internal static class MonthlyTickHookPatch
    {
        static MonthlyTickHookPatch()
        {
            CampaignScope.Register("monthly-cadence", ResetCadence);
        }

        [HarmonyPostfix]
        internal static void Postfix(AICampaign __instance)
        {
            using (TelemetryPerf.Scope("campaign.patch.monthly-tick-hook", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
            OnceLog.Info("strategic-cadence", "Strategic cadence hook wired (monthly heartbeat + weekly CIC review)");
            try
            {
                // R15: a campaign scene whose AICampaign instance was never loaded from a save
                // is a new campaign -> reset the singleton coordinator's state.
                if (StrategicCoordinator.Epoch.ShouldResetOnUpdate(__instance))
                {
                    if (StrategicCoordinator.Instance == null) StrategicCoordinator.Bootstrap();
                    StrategicCoordinator.Instance?.ResetAll(CampaignEpochTracker.ResetReason(SceneManagement.currentoperation));
                }

                if (!ReviewCadenceCatchUp.CampaignIsLive(SceneManagement.currentoperation))
                {
                    OnceLog.Info("strategic-cadence:not-live", "[MonthlyTickHookPatch] waiting for SceneManagement.currentoperation == 2 (campaign live)");
                    return;
                }

                int day = ReadGameDay();
                int month = ReadGameMonth();
                int year  = ReadGameYear();
                if (day <= 0 || month <= 0 || year <= 0)
                {
                    OnceLog.Info("strategic-cadence:date-wait", "[MonthlyTickHookPatch] campaign date unavailable; waiting for campaign scene");
                    return;
                }

                bool dateChanged = day != _lastNotifiedDay || month != _lastNotifiedMonth || year != _lastNotifiedYear;
                bool runtimeReady = OperationalRuntimeReady() && !StrategicCoordinator.WlCareerStartPending();
                if (!_startupGate.ShouldNotify(dateChanged, runtimeReady))
                    return;

                _lastNotifiedDay = day;
                _lastNotifiedMonth = month;
                _lastNotifiedYear = year;

                if (StrategicCoordinator.Instance == null) StrategicCoordinator.Bootstrap();
                StrategicCoordinator.Instance.NotifyDateAdvanced(day, month, year);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("strategic-cadence:postfix", "[MonthlyTickHookPatch] cadence hook failed: " + ex.Message);
            }
            } // end using TelemetryPerf.Scope
        }

        // Cached BattleUnits component reference. GameObject.Find is expensive
        // and we run every frame. Refreshes on null (after scene reloads).
        private static UnityEngine.Component _bunitsCached;
        private static FieldInfo _bunitsYearField;
        private static FieldInfo _stormField;
        private static FieldInfo _stormDayField;
        private static FieldInfo _stormMonthField;
        private static FieldInfo _aiFactionField;
        private static int _lastNotifiedDay = -1;
        private static int _lastNotifiedMonth = -1;
        private static int _lastNotifiedYear = -1;
        private static OperationalStartupGate _startupGate = new OperationalStartupGate();

        // Called by StrategicCoordinator.ResetAll: the next campaign may start on the same
        // date the previous one was last notified on.
        internal static void ResetCadence()
        {
            _lastNotifiedDay = -1;
            _lastNotifiedMonth = -1;
            _lastNotifiedYear = -1;
            _startupGate = new OperationalStartupGate();
            _bunitsCached = null;
        }

        private static UnityEngine.Component ResolveBunits()
        {
            if (_bunitsCached != null) return _bunitsCached;
            try
            {
                var go = UnityEngine.GameObject.Find("GameController");
                if (go == null) return null;
                var bunitsType = AccessTools.TypeByName("BattleUnits");
                if (bunitsType == null) return null;
                _bunitsCached = go.GetComponent(bunitsType);
                _stormField = AccessTools.Field(bunitsType, "uniStormSystem");
                _bunitsYearField = AccessTools.Field(bunitsType, "year");
                return _bunitsCached;
            }
            catch { return null; }
        }

        private static object ResolveStorm()
        {
            var bunits = ResolveBunits();
            if (bunits == null || _stormField == null) return null;
            var storm = _stormField.GetValue(bunits);
            if (storm == null) return null;
            var stormType = storm.GetType();
            if (_stormDayField == null) _stormDayField = AccessTools.Field(stormType, "dayCounter");
            if (_stormMonthField == null) _stormMonthField = AccessTools.Field(stormType, "monthCounter");
            return storm;
        }

        private static int ReadGameMonth()
        {
            try
            {
                // Vanilla: bunits.uniStormSystem.monthCounter (1-based; Jan=1).
                var storm = ResolveStorm();
                return storm != null && _stormMonthField != null ? (int)_stormMonthField.GetValue(storm) : -1;
            }
            catch { return -1; }
        }

        private static int ReadGameDay()
        {
            try
            {
                // Vanilla: bunits.uniStormSystem.dayCounter (1-based).
                var storm = ResolveStorm();
                return storm != null && _stormDayField != null ? (int)_stormDayField.GetValue(storm) : -1;
            }
            catch { return -1; }
        }

        private static int ReadGameYear()
        {
            try
            {
                // Vanilla: bunits.year — the BattleUnits instance field gets
                // set on scenario load (decompile line 25326). GameVars.year
                // exists but is never assigned; that was the v0.2.1 bug.
                var bunits = ResolveBunits();
                return bunits != null && _bunitsYearField != null ? (int)_bunitsYearField.GetValue(bunits) : -1;
            }
            catch { return -1; }
        }

        private static bool OperationalRuntimeReady()
        {
            try
            {
                if (_aiFactionField == null)
                    _aiFactionField = AccessTools.Field(typeof(AICampaign), "aifaction");
                var list = _aiFactionField?.GetValue(null) as System.Collections.IList;
                return list != null && list.Count > 0;
            }
            catch { return false; }
        }
    }
}
