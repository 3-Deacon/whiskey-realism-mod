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

        private static int ReadGameMonth()
        {
            try
            {
                var t = AccessTools.TypeByName("GameVars");
                var f = AccessTools.Field(t, "currentmonth");
                return f != null ? (int)f.GetValue(null) + 1 : -1;
            }
            catch { return -1; }
        }

        private static int ReadGameYear()
        {
            try
            {
                // Vanilla: public static int year (decompile line 64790).
                var t = AccessTools.TypeByName("GameVars");
                var f = AccessTools.Field(t, "year");
                return f != null ? (int)f.GetValue(null) : -1;
            }
            catch { return -1; }
        }
    }
}
