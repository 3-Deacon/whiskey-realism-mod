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
        private static string _lastDiaryCycleDiagnostic = "not-checked";

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
                    OnceLog.Warning("wl-diary-startup:check", "[W&LCamp] diary readiness check failed closed: " + ex.Message);
                    return false;
                }

                _vanillaThresholdDepth++;
                __state = true;
                return true;
            }

            [HarmonyFinalizer]
            internal static Exception Finalizer(Exception __exception, bool __state)
            {
                if (__state && _vanillaThresholdDepth > 0) _vanillaThresholdDepth--;
                if (__exception is NullReferenceException && DLC_WL.dlc_scenarioactive)
                {
                    OnceLog.Warning(
                        "wl-diary-startup:vanilla-nre",
                        "[W&LCamp] suppressed W&L Diary.UpdateEvents NullReferenceException after cycle preflight missed a vanilla null; updateCycle=" +
                        ReadStaticInt(DiaryUpdateCycleField, -1) +
                        " diagnostic=" + _lastDiaryCycleDiagnostic +
                        " message=" + __exception.Message);
                    return null;
                }
                return __exception;
            }
        }

        private static bool ShouldSkipDiaryUpdateEvents()
        {
            int chosenCommanderId = DLC_WL.dlc_chosencommander;
            bool commanderReady = IsChosenCommanderRecordReady(chosenCommanderId);
            Regiment currentCommand = commanderReady ? GameVars.commander[chosenCommanderId].currentcommand : null;
            bool commanderHasCommand = commanderReady && UnityAlive(currentCommand);
            int updateCycle = ReadStaticInt(DiaryUpdateCycleField, -1);
            bool updateCycleReady = DiaryUpdateCycleReady(updateCycle, currentCommand);

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
                updateCycle,
                CampaignGroupLookupReady(currentCommand),
                updateCycleReady);
        }

        private static string BuildDiaryReadinessDiagnostic()
        {
            int chosenCommanderId = DLC_WL.dlc_chosencommander;
            bool commanderReady = IsChosenCommanderRecordReady(chosenCommanderId);
            Regiment currentCommand = commanderReady ? GameVars.commander[chosenCommanderId].currentcommand : null;
            bool commanderHasCommand = commanderReady && UnityAlive(currentCommand);
            return "frame=" + GameVars.frame +
                " commander=" + chosenCommanderId +
                " commanderReady=" + commanderReady +
                " hasCommand=" + commanderHasCommand +
                " campaignGroupLookup=" + CampaignGroupLookupReady(currentCommand) +
                " diaryEvents=" + DiaryEventsReady() +
                " food=" + FoodReady() +
                " cardinalPoints=" + CardinalPointsReady() +
                " weather=" + WeatherReady() +
                " updateCycle=" + ReadStaticInt(DiaryUpdateCycleField, -1) +
                " cycleReady=" + _lastDiaryCycleDiagnostic;
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

        private static bool CampaignGroupLookupReady(Regiment currentCommand)
        {
            if (!UnityAlive(currentCommand)) return false;
            try
            {
                BattleUnits.GetCampaignGroup(currentCommand);
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "wl-diary-startup:campaign-group",
                    "[W&LCamp] skipped Diary.UpdateEvents until BattleUnits.GetCampaignGroup is safe: " + ex.Message);
                return false;
            }
        }

        private static bool DiaryUpdateCycleReady(int updateCycle, Regiment currentCommand)
        {
            try
            {
                Regiment campaignGroup = null;
                if (UnityAlive(currentCommand))
                {
                    campaignGroup = BattleUnits.GetCampaignGroup(currentCommand);
                }

                switch (updateCycle)
                {
                    case 0:
                        return DiaryCycleZeroReady(campaignGroup);
                    case 1:
                        return DiaryWeatherCycleReady(campaignGroup);
                    case 4:
                        return DiarySupplyCycleReady(campaignGroup);
                    case 5:
                        return DiaryEnemyContactCycleReady(campaignGroup);
                    case 6:
                        return CommanderIndexReady(DLC_WL.dlc_chosencommander);
                    case 7:
                        if (UnityAlive(campaignGroup)) AICampaign.IsUnitPartOfDefendingCapital(campaignGroup);
                        return MarkDiaryCycleReady(updateCycle);
                    case 8:
                        return DiaryFriendlyOperationCycleReady(campaignGroup, defensive: false);
                    case 9:
                        return DiaryFriendlyOperationCycleReady(campaignGroup, defensive: true);
                    case 10:
                        return DiaryCampStationCycleReady();
                    default:
                        return MarkDiaryCycleReady(updateCycle);
                }
            }
            catch (Exception ex)
            {
                return MarkDiaryCycleUnsafe(updateCycle, "exception=" + ex.GetType().Name + ":" + ex.Message);
            }
        }

        private static bool DiaryCycleZeroReady(Regiment campaignGroup)
        {
            if (!UnityAlive(campaignGroup)) return MarkDiaryCycleReady(0);

            if (campaignGroup.HasMovedRecently(0.002739726f, usecampaigngroup: true))
            {
                int state = BattlefieldSetup.GetStateOfField(campaignGroup.GetPosition(), usecloseststate: true);
                int lastState = BattlefieldSetup.GetStateOfField(campaignGroup.GetLastWaypointPos(), usecloseststate: true);
                if (state != lastState && !NationIndexReady(lastState))
                {
                    return MarkDiaryCycleUnsafe(0, "nation-index-unready:" + lastState);
                }

                Regiment parentUnit = BattleUnits.GetParentUnit(campaignGroup);
                if (UnityAlive(parentUnit) && !ReferenceEquals(parentUnit, campaignGroup) && !CommanderIndexReady(parentUnit.commander))
                {
                    return MarkDiaryCycleUnsafe(0, "parent-commander-unready:" + parentUnit.commander);
                }

                if (!CardinalPointsReady()) return MarkDiaryCycleUnsafe(0, "cardinal-points-unready");
                Diary.GetDirection(campaignGroup.GetPosition(), campaignGroup.GetLastWaypointPos(), GetCardinalPoints());
                return MarkDiaryCycleReady(0);
            }

            Town closestTown = BattleUnits.GetClosestTown(campaignGroup.GetPosition());
            if (!UnityAlive(closestTown)) return MarkDiaryCycleUnsafe(0, "closest-town-unready");
            return MarkDiaryCycleReady(0);
        }

        private static bool DiaryWeatherCycleReady(Regiment campaignGroup)
        {
            if (!UnityAlive(campaignGroup)) return MarkDiaryCycleReady(1);
            Weather weather = GetWeatherInstance();
            if (!UnityAlive(weather)) return MarkDiaryCycleUnsafe(1, "weather-unready");
            weather.GetClimateData(campaignGroup.GetPosition(), 100);
            campaignGroup.GetWeatherID();
            return MarkDiaryCycleReady(1);
        }

        private static bool DiarySupplyCycleReady(Regiment campaignGroup)
        {
            if (!UnityAlive(campaignGroup)) return MarkDiaryCycleReady(4);
            if (campaignGroup.unittyp > 13 && (campaignGroup.groupsupplystate == null || campaignGroup.groupsupplystate.Length <= 2))
            {
                return MarkDiaryCycleUnsafe(4, "group-supplystate-unready");
            }
            return MarkDiaryCycleReady(4);
        }

        private static bool DiaryEnemyContactCycleReady(Regiment campaignGroup)
        {
            if (!UnityAlive(campaignGroup)) return MarkDiaryCycleReady(5);
            if (!campaignGroup.inbattle)
            {
                Regiment closestEnemy = campaignGroup.GetClosestEnemyUnitReg(campaignGroup.commanderrange);
                if (UnityAlive(closestEnemy)) closestEnemy.GetCommanderNameAndUnit();
                return MarkDiaryCycleReady(5);
            }

            Autocalc siege = campaignGroup.GetSiegeAutocalc();
            if (UnityAlive(siege) && !UnityAlive(campaignGroup.garrisonreference) && siege.type == 2)
            {
                siege.GetSiegeUnitName();
            }
            return MarkDiaryCycleReady(5);
        }

        private static bool DiaryFriendlyOperationCycleReady(Regiment campaignGroup, bool defensive)
        {
            int cycle = defensive ? 9 : 8;
            if (!UnityAlive(campaignGroup)) return MarkDiaryCycleReady(cycle);

            var position = ((UnityEngine.Component)campaignGroup).transform.position;
            Regiment operation = defensive
                ? AICampaign.GetClosestDefensiveOperation(position, campaignGroup.alliance, friendly: true, campaignGroup)
                : AICampaign.GetClosestOffensiveOperation(position, campaignGroup.alliance, friendly: true, campaignGroup);

            if (!UnityAlive(operation)) return MarkDiaryCycleReady(cycle);
            operation.GetCommanderNameAndUnit();

            if (defensive)
            {
                Town closestTown = BattleUnits.GetClosestTown(((UnityEngine.Component)operation).transform.position);
                if (!UnityAlive(closestTown)) return MarkDiaryCycleUnsafe(cycle, "operation-town-unready");
            }

            return MarkDiaryCycleReady(cycle);
        }

        private static bool DiaryCampStationCycleReady()
        {
            if (Camp.stations == null) return MarkDiaryCycleReady(10);
            if (Diary.DiaryEvent.campstationbonusbelow == null || Diary.DiaryEvent.campstationbonusabove == null)
            {
                return MarkDiaryCycleUnsafe(10, "camp-station-event-arrays-unready");
            }
            if (Camp.stations.Count > Diary.DiaryEvent.campstationbonusbelow.Length ||
                Camp.stations.Count > Diary.DiaryEvent.campstationbonusabove.Length)
            {
                return MarkDiaryCycleUnsafe(10, "camp-station-event-array-short");
            }
            return MarkDiaryCycleReady(10);
        }

        private static List<string> GetCardinalPoints()
        {
            return DiaryCardinalPointsField == null ? null : DiaryCardinalPointsField.GetValue(null) as List<string>;
        }

        private static Weather GetWeatherInstance()
        {
            var cachedWeather = DiaryWeatherField == null ? null : DiaryWeatherField.GetValue(null) as Weather;
            if (UnityAlive(cachedWeather)) return cachedWeather;

            var weatherObject = UnityEngine.GameObject.Find("WeatherObj");
            return UnityAlive(weatherObject) ? weatherObject.GetComponent<Weather>() : null;
        }

        private static bool NationIndexReady(int stateId)
        {
            return stateId >= 0 && GameVars.nation != null && stateId < GameVars.nation.Length && GameVars.nation[stateId] != null;
        }

        private static bool CommanderIndexReady(int commanderId)
        {
            return commanderId >= 0 && GameVars.commander != null && commanderId < GameVars.commander.Count && GameVars.commander[commanderId] != null;
        }

        private static bool MarkDiaryCycleReady(int updateCycle)
        {
            _lastDiaryCycleDiagnostic = "cycle=" + updateCycle + ":ready";
            return true;
        }

        private static bool MarkDiaryCycleUnsafe(int updateCycle, string reason)
        {
            _lastDiaryCycleDiagnostic = "cycle=" + updateCycle + ":" + reason;
            OnceLog.Warning(
                "wl-diary-startup:cycle:" + updateCycle,
                "[W&LCamp] skipped Diary.UpdateEvents until vanilla diary update cycle is safe: " + _lastDiaryCycleDiagnostic);
            return false;
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
