using System;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    [HarmonyPatch(typeof(AICampaign), "Update")]
    internal static class MonthlyTickHookPatch
    {
        [HarmonyPostfix]
        internal static void Postfix()
        {
            OnceLog.Info("strategic-cadence", "Strategic cadence hook wired (monthly heartbeat + weekly CIC review)");
            try
            {
                int day = ReadGameDay();
                int month = ReadGameMonth();
                int year  = ReadGameYear();
                if (day <= 0 || month <= 0 || year <= 0)
                {
                    OnceLog.Warning("strategic-cadence:date-unavailable", "[MonthlyTickHookPatch] campaign date unavailable");
                    return;
                }

                if (day == _lastNotifiedDay && month == _lastNotifiedMonth && year == _lastNotifiedYear)
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
        }

        // Cached BattleUnits component reference. GameObject.Find is expensive
        // and we run every frame. Refreshes on null (after scene reloads).
        private static UnityEngine.Component _bunitsCached;
        private static FieldInfo _bunitsYearField;
        private static FieldInfo _stormField;
        private static FieldInfo _stormDayField;
        private static FieldInfo _stormMonthField;
        private static int _lastNotifiedDay = -1;
        private static int _lastNotifiedMonth = -1;
        private static int _lastNotifiedYear = -1;

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
    }
}
