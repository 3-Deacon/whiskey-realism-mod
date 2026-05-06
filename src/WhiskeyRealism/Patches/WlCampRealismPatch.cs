using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla Camp.EvaluateCampTime() converts observed W&L camp time into station histories;
    // Camp.Station.GetCurrentBonus() turns those histories into station payoff; Camp.GetModifier()
    // applies unit-facing station effects. This patch corrects short-camp minimum undercrediting,
    // caps Rest's full-reward target, adds bounded responsive weighting for safe stations, and
    // softens command-count dilution.
    internal static class WlCampRealismPatch
    {
        private static readonly FieldInfo CampTimeHistoryField = AccessTools.Field(typeof(Camp), "camptimehistory");
        private static readonly FieldInfo BattlefieldSetupRefField = AccessTools.Field(typeof(Camp), "battlefieldsetupref");
        private static readonly FieldInfo DiaryEventsField = AccessTools.Field(typeof(Diary), "diaryevents");
        private static readonly FieldInfo DiaryCardinalPointsField = AccessTools.Field(typeof(Diary), "cardinalpoints");
        private static readonly FieldInfo DiaryUpdateCycleField = AccessTools.Field(typeof(Diary), "updatecycle");
        private static readonly FieldInfo DiaryWeatherField = AccessTools.Field(typeof(Diary), "weather");
        private static int _vanillaThresholdDepth;
        private static string _lastCorrectionSignature;
        private static string _lastModifierSignature;
        private static string _lastBonusSignature;

        [HarmonyPatch(typeof(Camp), "EvaluateCampTime")]
        internal static class EvaluateCampTimePatch
        {
            [HarmonyPrefix]
            internal static void Prefix()
            {
                TryRefreshCurrentStatus();
            }

            [HarmonyPostfix]
            internal static void Postfix()
            {
                TryCorrectShortCampHistory();
            }
        }

        [HarmonyPatch(typeof(Camp.Station), "GetCurrentBonus")]
        internal static class StationBonusPatch
        {
            [HarmonyPostfix]
            internal static void Postfix(Camp.Station __instance, bool useaverage, ref float __result)
            {
                TryApplyRestRewardCap(__instance, useaverage, ref __result);
                TryApplyResponsiveBonus(__instance, useaverage, ref __result);
            }
        }

        [HarmonyPatch(typeof(Camp), "GetModifier")]
        internal static class ModifierPatch
        {
            [HarmonyPostfix]
            internal static void Postfix(int stationid, bool dividebycommandedunits, ref float __result)
            {
                TryApplyUnitPayoffTuning(stationid, dividebycommandedunits, ref __result);
            }
        }

        [HarmonyPatch(typeof(Camp.Station), "CheckEventTriggers")]
        internal static class CampEventThresholdScopePatch
        {
            [HarmonyPrefix]
            internal static void Prefix()
            {
                _vanillaThresholdDepth++;
            }

            [HarmonyFinalizer]
            internal static Exception Finalizer(Exception __exception)
            {
                if (_vanillaThresholdDepth > 0) _vanillaThresholdDepth--;
                return __exception;
            }
        }

        [HarmonyPatch(typeof(Diary), "UpdateEvents")]
        internal static class DiaryThresholdScopePatch
        {
            [HarmonyPrefix]
            internal static bool Prefix(ref bool __state)
            {
                __state = false;
                try
                {
                    if (ShouldSkipDiaryUpdateEvents())
                    {
                        OnceLog.Warning(
                            "wl-diary-startup:skip",
                            "[W&LCamp] skipped Diary.UpdateEvents until W&L diary dependencies are ready " + BuildDiaryReadinessDiagnostic());
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("wl-diary-startup:check", "[W&LCamp] diary readiness check failed open: " + ex.Message);
                }

                _vanillaThresholdDepth++;
                __state = true;
                return true;
            }

            [HarmonyFinalizer]
            internal static Exception Finalizer(Exception __exception, bool __state)
            {
                if (__state && _vanillaThresholdDepth > 0) _vanillaThresholdDepth--;
                return __exception;
            }
        }

        private static bool ShouldSkipDiaryUpdateEvents()
        {
            int chosenCommanderId = DLC_WL.dlc_chosencommander;
            bool commanderReady = IsChosenCommanderRecordReady(chosenCommanderId);
            bool commanderHasCommand = commanderReady && UnityAlive(GameVars.commander[chosenCommanderId].currentcommand);
            int updateCycle = ReadStaticInt(DiaryUpdateCycleField, -1);

            return WlCareerStartGate.ShouldSkipDiaryEventUpdate(
                DLC_WL.dlc_scenarioactive,
                GameVars.frame,
                chosenCommanderId,
                commanderReady,
                commanderHasCommand,
                DiaryEventsReady(),
                FoodReady(),
                CardinalPointsReady(),
                updateCycle != 1 || WeatherReady(),
                updateCycle);
        }

        private static string BuildDiaryReadinessDiagnostic()
        {
            int chosenCommanderId = DLC_WL.dlc_chosencommander;
            bool commanderReady = IsChosenCommanderRecordReady(chosenCommanderId);
            bool commanderHasCommand = commanderReady && UnityAlive(GameVars.commander[chosenCommanderId].currentcommand);
            return "frame=" + GameVars.frame +
                " commander=" + chosenCommanderId +
                " commanderReady=" + commanderReady +
                " hasCommand=" + commanderHasCommand +
                " diaryEvents=" + DiaryEventsReady() +
                " food=" + FoodReady() +
                " cardinalPoints=" + CardinalPointsReady() +
                " weather=" + WeatherReady() +
                " updateCycle=" + ReadStaticInt(DiaryUpdateCycleField, -1);
        }

        private static bool IsChosenCommanderRecordReady(int chosenCommanderId)
        {
            if (chosenCommanderId < 0) return false;
            if (GameVars.commander == null) return false;
            if (chosenCommanderId >= GameVars.commander.Count) return false;
            return GameVars.commander[chosenCommanderId] != null;
        }

        private static bool DiaryEventsReady()
        {
            var diaryEvents = DiaryEventsField == null ? null : DiaryEventsField.GetValue(null) as IList;
            if (diaryEvents == null) return false;
            if (!DiaryEventIndexReady(diaryEvents, Diary.DiaryEvent.weightincrease)) return false;
            for (int i = 0; i < diaryEvents.Count; i++)
            {
                if (diaryEvents[i] == null) return false;
            }
            return true;
        }

        private static bool DiaryEventIndexReady(IList diaryEvents, int eventId)
        {
            if (eventId < 0 || eventId >= diaryEvents.Count) return false;
            return diaryEvents[eventId] != null;
        }

        private static bool CardinalPointsReady()
        {
            var cardinalPoints = DiaryCardinalPointsField == null ? null : DiaryCardinalPointsField.GetValue(null) as IList;
            return cardinalPoints != null && cardinalPoints.Count >= 8;
        }

        private static bool FoodReady()
        {
            return DLC_WL.food != null && DLC_WL.food.Count > 0;
        }

        private static bool WeatherReady()
        {
            var cachedWeather = DiaryWeatherField == null ? null : DiaryWeatherField.GetValue(null) as Weather;
            if (UnityAlive(cachedWeather)) return true;

            var weatherObject = UnityEngine.GameObject.Find("WeatherObj");
            return UnityAlive(weatherObject) && UnityAlive(weatherObject.GetComponent<Weather>());
        }

        private static int ReadStaticInt(FieldInfo field, int fallback)
        {
            if (field == null) return fallback;
            object value = field.GetValue(null);
            return value is int i ? i : fallback;
        }

        private static bool UnityAlive(UnityEngine.Object value)
        {
            return value != null;
        }

        private static void TryRefreshCurrentStatus()
        {
            try
            {
                if (!AccountingEnabled()) return;
                if (Camp.stations == null) return;
                if (DLC_WL.dlc_chosencommander < 0 || DLC_WL.dlc_chosencommander >= GameVars.commander.Count) return;
                if (BattlefieldSetupRefField == null || BattlefieldSetupRefField.GetValue(null) == null) return;
                Camp.currentstatus = Camp.PlayerUnitStatus();
                OnceLog.Info("wl-camp-realism", "[W&LCamp] camp realism patch active");
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-camp-realism:status", "[W&LCamp] status refresh failed: " + ex.Message);
            }
        }

        private static void TryCorrectShortCampHistory()
        {
            try
            {
                if (!AccountingEnabled()) return;
                if (Camp.stations == null || Camp.stations.Count == 0) return;
                var campHistory = CampTimeHistoryField == null ? null : CampTimeHistoryField.GetValue(null) as List<float>;
                if (campHistory == null || campHistory.Count == 0) return;

                float actual = campHistory[campHistory.Count - 1];
                var minimums = new float[Camp.stations.Count];
                for (int i = 0; i < Camp.stations.Count; i++)
                    minimums[i] = Camp.stations[i] != null ? Camp.stations[i].GetMinTime() : 0f;

                var corrected = new float[minimums.Length];
                float minimumTotal;
                if (!WlCampRealism.TryCorrectShortCampMinimumCredits(actual, minimums, corrected, out minimumTotal)) return;

                for (int i = 0; i < Camp.stations.Count; i++)
                {
                    var station = Camp.stations[i];
                    if (station == null || station.timehistory == null || station.timehistory.Count == 0) continue;
                    int last = station.timehistory.Count - 1;
                    float old = station.timehistory[last];
                    station.timehistory[last] = corrected[i];
                    TraceCorrection(i, actual, minimumTotal, old, corrected[i]);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-camp-realism:correct", "[W&LCamp] short-camp correction failed: " + ex.Message);
            }
        }

        private static void TryApplyRestRewardCap(Camp.Station station, bool useAverage, ref float result)
        {
            try
            {
                if (!RestRewardCapEnabled()) return;
                if (station == null || Camp.stations == null) return;
                int stationId = Camp.stations.IndexOf(station);
                if (stationId < 0) return;
                if (!WlCampRealism.UsesRestRewardCap(stationId)) return;
                if (!Camp.IsCampStationAvailable(station)) return;

                float stationHours = useAverage ? station.averagetimespent : station.hoursassigned;
                float companionHours = useAverage ? station.companionaveragetimespent : (station.assignedcompanion < 0 ? 0f : 1f);
                float old = result;
                result = WlCampRealism.ComputeRestRewardBonus(
                    stationId,
                    result,
                    stationHours,
                    companionHours,
                    station.GetMinTimeBonus(),
                    station.GetMaxTimeBonus(),
                    Plugin.Instance.WlCampRestNeutralHours.Value,
                    Plugin.Instance.WlCampRestMaxRewardHours.Value);
                TraceBonus(stationId, old, result, "restBonus");
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-camp-realism:rest", "[W&LCamp] rest reward cap failed: " + ex.Message);
            }
        }

        private static void TryApplyResponsiveBonus(Camp.Station station, bool useAverage, ref float result)
        {
            try
            {
                if (!ResponsiveEnabled()) return;
                if (_vanillaThresholdDepth > 0) return;
                if (!useAverage || station == null || Camp.stations == null) return;
                int stationId = Camp.stations.IndexOf(station);
                if (stationId < 0) return;
                if (!WlCampRealism.UsesResponsiveBonusWeighting(stationId)) return;
                if (!Camp.IsCampStationAvailable(station)) return;

                float old = result;
                result = WlCampRealism.ComputeResponsiveBonus(
                    stationId,
                    useAverage,
                    result,
                    station.averagetimespent,
                    station.companionaveragetimespent,
                    ToArray(station.timehistory),
                    ToArray(station.companiontimehistory),
                    station.GetMinTimeBonus(),
                    station.GetMaxTimeBonus(),
                    Plugin.Instance.WlCampRecentBonusWindowDays.Value,
                    Plugin.Instance.WlCampRecentBonusWeight.Value);
                TraceBonus(stationId, old, result, "responsiveBonus");
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-camp-realism:bonus", "[W&LCamp] responsive bonus failed: " + ex.Message);
            }
        }

        private static void TryApplyUnitPayoffTuning(int stationId, bool divideByCommandedUnits, ref float result)
        {
            try
            {
                if (!UnitPayoffEnabled()) return;
                if (!WlCampRealism.UsesUnitPayoffTuning(stationId, divideByCommandedUnits)) return;
                if (!DLC_WL.dlc_scenarioactive || Camp.stations == null) return;
                if (stationId < 0 || stationId >= Camp.stations.Count) return;
                var station = Camp.stations[stationId];
                if (station == null) return;

                int commanded = DLC_WL.GetNumberOfCommandedUnits();
                float old = result;
                float bonus = station.GetCurrentBonus();
                result = WlCampRealism.ComputeUnitPayoffModifier(
                    stationId,
                    divideByCommandedUnits,
                    result,
                    bonus,
                    station.maxbonusmalus,
                    commanded,
                    Plugin.Instance.WlCampUnitEffectDivisorPower.Value);
                TraceModifier(stationId, commanded, old, result);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-camp-realism:modifier", "[W&LCamp] unit payoff tuning failed: " + ex.Message);
            }
        }

        private static bool AccountingEnabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableWlCampAccountingFix != null &&
                Plugin.Instance.EnableWlCampAccountingFix.Value;
        }

        private static bool RestRewardCapEnabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableWlCampRestRewardCap != null &&
                Plugin.Instance.EnableWlCampRestRewardCap.Value;
        }

        private static bool ResponsiveEnabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableWlCampResponsiveBonusWeighting != null &&
                Plugin.Instance.EnableWlCampResponsiveBonusWeighting.Value;
        }

        private static bool UnitPayoffEnabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableWlCampUnitPayoffTuning != null &&
                Plugin.Instance.EnableWlCampUnitPayoffTuning.Value;
        }

        private static float[] ToArray(List<float> values)
        {
            return values == null ? new float[0] : values.ToArray();
        }

        private static void TraceCorrection(int stationId, float actual, float minimumTotal, float oldCredit, float newCredit)
        {
            if (!VerboseTrace()) return;
            string sig = stationId + ":" + actual.ToString("0.00") + ":" + oldCredit.ToString("0.00") + ":" + newCredit.ToString("0.00");
            if (_lastCorrectionSignature == sig) return;
            _lastCorrectionSignature = sig;
            Plugin.Log.LogInfo("[W&LCamp] station=" + stationId + " actual=" + actual.ToString("F2") +
                " minimumTotal=" + minimumTotal.ToString("F2") + " vanillaCredit=" + oldCredit.ToString("F2") +
                " correctedCredit=" + newCredit.ToString("F2"));
        }

        private static void TraceBonus(int stationId, float oldBonus, float newBonus, string label)
        {
            if (!VerboseTrace()) return;
            if (Math.Abs(oldBonus - newBonus) < 0.01f) return;
            string sig = label + ":" + stationId + ":" + oldBonus.ToString("0.00") + ":" + newBonus.ToString("0.00");
            if (_lastBonusSignature == sig) return;
            _lastBonusSignature = sig;
            Plugin.Log.LogInfo("[W&LCamp] station=" + stationId + " vanillaBonus=" + oldBonus.ToString("F2") +
                " " + label + "=" + newBonus.ToString("F2"));
        }

        private static void TraceModifier(int stationId, int commanded, float oldModifier, float newModifier)
        {
            if (!VerboseTrace()) return;
            if (Math.Abs(oldModifier - newModifier) < 0.01f) return;
            string sig = stationId + ":" + commanded + ":" + oldModifier.ToString("0.00") + ":" + newModifier.ToString("0.00");
            if (_lastModifierSignature == sig) return;
            _lastModifierSignature = sig;
            Plugin.Log.LogInfo("[W&LCamp] station=" + stationId + " commanded=" + commanded +
                " vanillaModifier=" + oldModifier.ToString("F2") + " tunedModifier=" + newModifier.ToString("F2"));
        }

        private static bool VerboseTrace()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.EnableWlCampVerboseTrace != null &&
                Plugin.Instance.EnableWlCampVerboseTrace.Value;
        }
    }
}
