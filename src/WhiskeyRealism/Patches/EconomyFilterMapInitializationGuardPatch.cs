using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla BattleUnits.CampaignDataRuns spins in
    // while (!Economy.UpdateFilterMaps(initialization:true)) with no iteration
    // cap. This guard does not replace CampaignDataRuns; it makes the
    // initialization UpdateFilterMaps surface return complete after repeated
    // no-progress false results or an initialization-only exception. Runtime
    // calls are not looped, but a null inside a single map item can otherwise
    // rethrow every campaign frame, so the finalizer skips that one iterator
    // slot using vanilla's cursor semantics.
    [HarmonyPatch(typeof(Economy), "UpdateFilterMaps")]
    internal static class EconomyFilterMapInitializationGuardPatch
    {
        private const int MaxRuntimeDiagnosticLogs = 12;

        private static readonly CampaignFilterMapInitializationGuard Guard = new CampaignFilterMapInitializationGuard();
        private static int _runtimeDiagnosticLogs;

        [HarmonyPrefix]
        internal static void Prefix(ref CampaignFilterMapState __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.economy-filter-init-guard.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                __state = CaptureState();
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(bool initialization, ref bool __result, CampaignFilterMapState __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.economy-filter-init-guard.postfix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                try
                {
                    if (!Enabled()) return;
                    var decision = Guard.Observe(initialization, __result, __state, CaptureState());
                    if (!decision.ForceComplete) return;

                    __result = true;
                    OnceLog.Warning(
                        "filter-map-init:" + decision.Reason,
                        "[Patch:FilterMapInit] forced initialization complete after repeated no-progress UpdateFilterMaps result");
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("filter-map-init:postfix", "[Patch:FilterMapInit] postfix failed: " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static Exception Finalizer(
            Exception __exception,
            bool initialization,
            ref bool __result,
            CampaignFilterMapState __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.economy-filter-init-guard.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                try
                {
                    if (__exception == null || !Enabled()) return __exception;
                    var decision = Guard.ObserveException(initialization, __exception, __state);
                    if (!decision.ForceComplete) return __exception;

                    if (initialization)
                    {
                        __result = true;
                        OnceLog.Warning(
                            "filter-map-init:" + decision.Reason,
                            "[Patch:FilterMapInit] suppressed initialization UpdateFilterMaps exception and ended bounded startup loop: "
                                + __exception.Message);
                        return null;
                    }

                    var current = CaptureState();
                    bool vanillaAlreadyAdvanced = !__state.SamePosition(current);
                    CampaignFilterMapState after;
                    if (vanillaAlreadyAdvanced)
                    {
                        after = current;
                    }
                    else if (!CampaignFilterMapInitializationGuard.TryAdvanceRuntimeCursor(__state, out after))
                    {
                        return __exception;
                    }

                    if (!vanillaAlreadyAdvanced)
                        ApplyState(after);
                    __result = false;
                    LogRuntimeDiagnostic(decision, __state, after, vanillaAlreadyAdvanced, __exception);
                    return null;
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("filter-map-init:finalizer", "[Patch:FilterMapInit] finalizer failed: " + ex.Message);
                    return __exception;
                }
            }
        }

        private static CampaignFilterMapState CaptureState()
        {
            return new CampaignFilterMapState(
                Economy.workforcetownrunthrough,
                Economy.workforceiiprunthrough,
                Economy.workforcecorprunthrough,
                BattleUnits.towns != null ? BattleUnits.towns.Count : -1,
                BattleUnits.smalltowns != null ? BattleUnits.smalltowns.Count : -1,
                BattleUnits.iips != null ? BattleUnits.iips.Count : -1,
                BattleUnits.cbuildings != null ? BattleUnits.cbuildings.Count : -1);
        }

        private static void ApplyState(CampaignFilterMapState state)
        {
            Economy.workforcetownrunthrough = state.TownRun;
            Economy.workforceiiprunthrough = state.IipRun;
            Economy.workforcecorprunthrough = state.CorporateRun;
        }

        private static void LogRuntimeDiagnostic(
            CampaignFilterMapGuardDecision decision,
            CampaignFilterMapState before,
            CampaignFilterMapState after,
            bool vanillaAlreadyAdvanced,
            Exception exception)
        {
            if (_runtimeDiagnosticLogs >= MaxRuntimeDiagnosticLogs)
            {
                OnceLog.Warning(
                    "filter-map-runtime:diagnostic-limit",
                    "[Patch:FilterMapRuntime] suppressed further runtime UpdateFilterMaps diagnostics after "
                        + MaxRuntimeDiagnosticLogs + " samples");
                return;
            }

            _runtimeDiagnosticLogs++;
            string diagnostic = CampaignFilterMapInitializationGuard.BuildRuntimeDiagnostic(
                before,
                after,
                BuildRuntimeProbeSummary(before));
            OnceLog.Warning(
                "filter-map-runtime:" + decision.Reason + ":" + before.Signature(),
                "[Patch:FilterMapRuntime] suppressed runtime UpdateFilterMaps NullReferenceException "
                    + (vanillaAlreadyAdvanced ? "after vanilla advanced cursor " : "and advanced cursor ")
                    + diagnostic
                    + " exception=" + exception.GetType().Name + ":" + exception.Message);
        }

        private static string BuildRuntimeProbeSummary(CampaignFilterMapState state)
        {
            try
            {
                IList towns = BattleUnits.towns as IList;
                IList smalltowns = BattleUnits.smalltowns as IList;
                IList iips = BattleUnits.iips as IList;
                IList cbuildings = BattleUnits.cbuildings as IList;

                return "lists towns=" + CountOf(towns)
                    + " smalltowns=" + CountOf(smalltowns)
                    + " iips=" + CountOf(iips)
                    + " cbuildings=" + CountOf(cbuildings)
                    + " maps " + DescribeMaps()
                    + " symbols " + DescribeProductionMapSymbols()
                    + " globals " + DescribeGlobals()
                    + " active "
                    + DescribeListItem("town", towns, state.TownRun, DescribeTown)
                    + " "
                    + DescribeListItem("smalltown", smalltowns, state.IipRun, DescribeTown)
                    + " "
                    + DescribeListItem("iip", iips, state.IipRun, DescribeIip)
                    + " "
                    + DescribeListItem("cbuilding", cbuildings, state.CorporateRun, DescribeBuilding);
            }
            catch (Exception ex)
            {
                return "diagnostic-failed=" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static string DescribeMaps()
        {
            return "workforce=" + UnityStatus(Economy.availableworkforce)
                + " slavery=" + UnityStatus(Economy.slavery)
                + " capital=" + UnityStatus(Economy.availablecapital)
                + " bottlenecks=" + UnityStatus(Economy.transportbottlenecks)
                + " market=" + UnityStatus(Economy.marketcapacity)
                + " hospitals=" + UnityStatus(Economy.hospitals)
                + " trade=" + UnityStatus(Economy.tradeandsupply)
                + " supply=" + UnityStatus(Economy.supply);
        }

        private static string DescribeProductionMapSymbols()
        {
            try
            {
                MapSymbols symbols = BattlefieldSetup.productionmapsymbols;
                if (!UnityAlive(symbols)) return "production=" + UnityStatus(symbols);

                var gridField = AccessTools.Field(typeof(MapSymbols), "gridelements");
                var maxField = AccessTools.Field(typeof(MapSymbols), "maxabsvalues");
                var shownField = AccessTools.Field(typeof(MapSymbols), "shown");
                var grid = gridField != null ? gridField.GetValue(symbols) as Array : null;
                var maxValues = maxField != null ? maxField.GetValue(symbols) as IList : null;
                object shown = shownField != null ? shownField.GetValue(symbols) : null;

                return "production=ok"
                    + " grid=" + ArrayShape(grid)
                    + " maxabsvalues=" + CountOf(maxValues)
                    + " shown=" + (shown == null ? "<unknown>" : shown.ToString());
            }
            catch (Exception ex)
            {
                return "production=diagnostic-failed:" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static string DescribeGlobals()
        {
            return "alliances=" + LengthOf(GameVars.alliance)
                + " nations=" + LengthOf(GameVars.nation)
                + " buildingtypes=" + CountOf(GameVars.buildingtypes as IList)
                + " resources=" + LengthOf(Economy.resources)
                + " playerAlliance=" + GameVars.playeralliance;
        }

        private static string DescribeListItem(string label, IList list, int index, Func<object, string> describe)
        {
            try
            {
                if (list == null) return label + "[" + index + "]=list-null";
                if (index < 0) return label + "[" + index + "]=inactive";
                if (index >= list.Count) return label + "[" + index + "]=out-of-range/" + list.Count;
                object value = list[index];
                return label + "[" + index + "]=" + ObjectSummary(value) + describe(value);
            }
            catch (Exception ex)
            {
                return label + "[" + index + "]=describe-failed:" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static string DescribeTown(object value)
        {
            return " population=" + SafeField(value, "representingpopulation");
        }

        private static string DescribeIip(object value)
        {
            var iip = value as IIP;
            if (!UnityAlive(iip)) return "";
            return " type=" + iip.IIPType
                + " owner=" + iip.allianceowner + "/" + IndexStatus(GameVars.alliance, iip.allianceowner)
                + " tradeNode=" + iip.IsTradeNode
                + " bottlenecks=" + iip.transportbottlenecks;
        }

        private static string DescribeBuilding(object value)
        {
            var building = value as CBuilding;
            if (!UnityAlive(building)) return "";
            return " type=" + building.BuildingType
                + " level=" + building.level
                + " owner=" + building.Owner + "/" + IndexStatus(GameVars.alliance, building.Owner)
                + " state=" + building.state + "/" + IndexStatus(GameVars.nation, building.state)
                + " timer=" + building.constructiontimer
                + " stockLen=" + LengthOf(building.stock)
                + " buildingTypeSlot=" + IndexStatus(GameVars.buildingtypes as IList, building.BuildingType);
        }

        private static string ObjectSummary(object value)
        {
            if (value == null) return "null";
            var unity = value as UnityEngine.Object;
            string type = value.GetType().Name;
            if (!ReferenceEquals(unity, null))
            {
                if (!UnityAlive(unity)) return type + "(destroyed)";
                return type
                    + "(name=" + SafeUnityName(unity)
                    + " component=" + ComponentStatus(value as Component)
                    + ")";
            }

            return type;
        }

        private static string ComponentStatus(Component component)
        {
            if (!UnityAlive(component)) return "none";
            return "self=ok go=" + UnityStatus(component.gameObject) + " transform=" + UnityStatus(component.transform);
        }

        private static string SafeUnityName(UnityEngine.Object value)
        {
            try { return value != null ? value.name : "<destroyed>"; }
            catch (Exception ex) { return "<name-failed:" + ex.GetType().Name + ">"; }
        }

        private static string SafeField(object value, string fieldName)
        {
            try
            {
                if (value == null) return "<null>";
                var field = AccessTools.Field(value.GetType(), fieldName);
                if (field == null) return "<missing>";
                object fieldValue = field.GetValue(value);
                return fieldValue == null ? "<null>" : fieldValue.ToString();
            }
            catch (Exception ex)
            {
                return "<failed:" + ex.GetType().Name + ">";
            }
        }

        private static string IndexStatus(Array values, int index)
        {
            try
            {
                if (values == null) return "array-null";
                if (index < 0 || index >= values.Length) return "out-of-range/" + values.Length;
                return values.GetValue(index) == null ? "null" : "ok";
            }
            catch (Exception ex)
            {
                return "failed:" + ex.GetType().Name;
            }
        }

        private static string IndexStatus(IList values, int index)
        {
            try
            {
                if (values == null) return "list-null";
                if (index < 0 || index >= values.Count) return "out-of-range/" + values.Count;
                return values[index] == null ? "null" : "ok";
            }
            catch (Exception ex)
            {
                return "failed:" + ex.GetType().Name;
            }
        }

        private static int CountOf(IList list)
        {
            try { return list != null ? list.Count : -1; }
            catch { return -2; }
        }

        private static int LengthOf(Array values)
        {
            try { return values != null ? values.Length : -1; }
            catch { return -2; }
        }

        private static string ArrayShape(Array values)
        {
            try
            {
                if (values == null) return "null";
                if (values.Rank == 1) return values.GetLength(0).ToString();
                if (values.Rank == 2) return values.GetLength(0) + "x" + values.GetLength(1);
                return "rank" + values.Rank + "/len" + values.Length;
            }
            catch (Exception ex)
            {
                return "failed:" + ex.GetType().Name;
            }
        }

        private static string UnityStatus(UnityEngine.Object value)
        {
            if (ReferenceEquals(value, null)) return "null";
            return value == null ? "destroyed" : "ok";
        }

        private static bool UnityAlive(UnityEngine.Object value)
        {
            return !ReferenceEquals(value, null) && value != null;
        }

        private static bool Enabled()
        {
            try { return Plugin.Instance != null && Plugin.Instance.Enabled.Value; }
            catch { return false; }
        }
    }
}
