using System;
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
            OnceLog.Info("monthlytick", "MonthlyTickHookPatch wired");
            try
            {
                int month = ReadGameMonth();
                int year  = ReadGameYear();
                if (month <= 0 || year <= 0) return;

                if (StrategicCoordinator.Instance == null) StrategicCoordinator.Bootstrap();
                StrategicCoordinator.Instance.NotifyDateAdvanced(month, year);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[MonthlyTickHookPatch] " + ex.Message);
            }
        }

        // Cached BattleUnits component reference. GameObject.Find is expensive
        // and we run every frame. Refreshes on null (after scene reloads).
        private static UnityEngine.Component _bunitsCached;

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
                return _bunitsCached;
            }
            catch { return null; }
        }

        private static int ReadGameMonth()
        {
            try
            {
                // Vanilla: bunits.uniStormSystem.monthCounter (1-based; Jan=1).
                var bunits = ResolveBunits();
                if (bunits == null) return -1;
                var stormField = AccessTools.Field(bunits.GetType(), "uniStormSystem");
                var storm = stormField?.GetValue(bunits);
                if (storm == null) return -1;
                var mField = AccessTools.Field(storm.GetType(), "monthCounter");
                return mField != null ? (int)mField.GetValue(storm) : -1;
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
                if (bunits == null) return -1;
                var yField = AccessTools.Field(bunits.GetType(), "year");
                return yField != null ? (int)yField.GetValue(bunits) : -1;
            }
            catch { return -1; }
        }
    }
}
