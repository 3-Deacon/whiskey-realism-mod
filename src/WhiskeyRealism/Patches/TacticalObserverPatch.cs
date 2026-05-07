using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Slice B0 observer for vanilla AIBattle tactical methods. This patch reads
    // battle/group state after vanilla runs and emits bounded telemetry only.
    [HarmonyPatch]
    internal static class TacticalObserverPatch
    {
        private static readonly Dictionary<string, float> _lastEmittedAt = new Dictionary<string, float>();
        private static int _chargeBeforeId;
        private static TacticalObserverSnapshot _chargeBefore = TacticalObserverSnapshot.Empty();
        private static TacticalObserverSnapshot _feudBefore = TacticalObserverSnapshot.Empty();

        private static FieldInfo _macroAiField;
        private static FieldInfo _sideOfAiField;
        private static FieldInfo _bunitsField;
        private static FieldInfo _unitsUsedField;
        private static FieldInfo _allGroupsAssignedField;
        private static FieldInfo _objectiveChainField;
        private static FieldInfo _orderedStanceField;
        private static FieldInfo _parentRegimentField;
        private static FieldInfo _allianceField;
        private static FieldInfo _sideField;
        private static FieldInfo _orderStateField;

        [HarmonyPatch(typeof(AIBattle), "CheckGlobalAIStrategy")]
        [HarmonyPostfix]
        internal static void CheckGlobalAIStrategyPostfix(AIBattle __instance)
        {
            Observe(__instance, TacticalObservedEvent.Macro, null, null);
        }

        [HarmonyPatch(typeof(AIBattle), "AdjustGroupAIStance")]
        [HarmonyPostfix]
        internal static void AdjustGroupAIStancePostfix(AIBattle __instance)
        {
            Observe(__instance, TacticalObservedEvent.Group, null, null);
            Observe(__instance, TacticalObservedEvent.Sector, null, null);
            Observe(__instance, TacticalObservedEvent.Order, null, null);
        }

        [HarmonyPatch(typeof(AIBattle), "MicroAICheckForCharges")]
        [HarmonyPrefix]
        internal static void MicroAICheckForChargesPrefix(AIBattle __instance, Regiment aigroup)
        {
            if (!Enabled()) return;
            try
            {
                int key = SafeInstanceId(aigroup);
                _chargeBeforeId = key;
                _chargeBefore = key != 0 ? SnapshotGroup(aigroup) : TacticalObserverSnapshot.Empty();
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-observer:charge-prefix", "Tactical charge observer Prefix failed: " + ex.Message);
            }
        }

        [HarmonyPatch(typeof(AIBattle), "MicroAICheckForCharges")]
        [HarmonyPostfix]
        internal static void MicroAICheckForChargesPostfix(AIBattle __instance, Regiment aigroup)
        {
            TacticalObserverSnapshot before = null;
            try
            {
                int key = SafeInstanceId(aigroup);
                if (key != 0 && key == _chargeBeforeId) before = _chargeBefore;
            }
            catch
            {
                before = null;
            }

            Observe(__instance, TacticalObservedEvent.Charge, before, aigroup);
        }

        [HarmonyPatch(typeof(AIBattle), "CheckForFeudGroupActions")]
        [HarmonyPrefix]
        internal static void CheckForFeudGroupActionsPrefix(AIBattle __instance)
        {
            if (!Enabled()) return;
            try
            {
                _feudBefore = SnapshotBattle(__instance);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-observer:feud-prefix", "Tactical feud observer Prefix failed: " + ex.Message);
            }
        }

        [HarmonyPatch(typeof(AIBattle), "CheckForFeudGroupActions")]
        [HarmonyPostfix]
        internal static void CheckForFeudGroupActionsPostfix(AIBattle __instance)
        {
            Observe(__instance, TacticalObservedEvent.Feud, _feudBefore, null);
        }

        [HarmonyPatch(typeof(AIBattle), "CheckUseOfReserves")]
        [HarmonyPostfix]
        internal static void CheckUseOfReservesPostfix(AIBattle __instance, Regiment aigroup)
        {
            Observe(__instance, TacticalObservedEvent.Reserve, null, aigroup);
        }

        [HarmonyPatch(typeof(AIBattle), "LinkReservesToLineGroup")]
        [HarmonyPostfix]
        internal static void LinkReservesToLineGroupPostfix(AIBattle __instance)
        {
            Observe(__instance, TacticalObservedEvent.Reserve, null, null);
        }

        [HarmonyPatch(typeof(AIBattle), "AssignReserves")]
        [HarmonyPostfix]
        internal static void AssignReservesPostfix(AIBattle __instance)
        {
            Observe(__instance, TacticalObservedEvent.Reserve, null, null);
        }

        [HarmonyPatch(typeof(AIBattle), "CheckAIBombardment")]
        [HarmonyPostfix]
        internal static void CheckAIBombardmentPostfix(AIBattle __instance, Regiment aigroup)
        {
            Observe(__instance, TacticalObservedEvent.Artillery, null, aigroup);
        }

        [HarmonyPatch(typeof(AIBattle), "CheckLineFallbacks")]
        [HarmonyPostfix]
        internal static void CheckLineFallbacksPostfix(AIBattle __instance, Regiment aigroup)
        {
            Observe(__instance, TacticalObservedEvent.Fallback, null, aigroup);
        }

        [HarmonyPatch(typeof(AIBattle), "MicroAICheckForRetreats")]
        [HarmonyPostfix]
        internal static void MicroAICheckForRetreatsPostfix(AIBattle __instance, Regiment aigroup)
        {
            Observe(__instance, TacticalObservedEvent.Fallback, null, aigroup);
        }

        // B2 command/order friction stays read-only: these Postfixes interpret vanilla queue/courier state.
        // They must not call SetWaypoint, AddToOrderQueue, SetOrderStatus, or mutate Regiment order fields.
        [HarmonyPatch(typeof(Regiment), "AddToOrderQueue")]
        [HarmonyPostfix]
        internal static void AddToOrderQueuePostfix(
            Regiment __instance,
            GameObject advisedunit,
            bool queueprocessingtime,
            int ordertype,
            float timetomove,
            float manualfinalrotation,
            bool modifylastwaypoint,
            bool clearpaths,
            bool overridebugle,
            bool _usecover,
            bool _newpath)
        {
            ObserveQueuedOrder(__instance, advisedunit, queueprocessingtime, ordertype, timetomove, modifylastwaypoint, _newpath);
        }

        [HarmonyPatch(typeof(Regiment), "AddOrderCourierline")]
        [HarmonyPostfix]
        internal static void AddOrderCourierlinePostfix(
            Regiment __instance,
            Regiment sourceunit,
            Regiment _targetunit,
            bool overridebugle,
            bool secondarycourier)
        {
            ObserveCourierLine(__instance, sourceunit, _targetunit, secondarycourier);
        }

        private static void Observe(AIBattle battle, TacticalObservedEvent eventType, TacticalObserverSnapshot before, Regiment group)
        {
            if (!Enabled()) return;

            try
            {
                OnceLog.Info("tactical-observer", "TacticalObserverPatch wired");

                var context = BuildContext(battle, group);
                string signature = TacticalTelemetry.Signature(eventType, context);
                bool verbose = Plugin.Instance != null && Plugin.Instance.TacticalObserverVerboseLogging.Value;
                float minSeconds = Plugin.Instance != null
                    ? Mathf.Max(1f, Plugin.Instance.TacticalObserverMinSecondsBetweenSummaries.Value)
                    : 30f;
                float now = Time.realtimeSinceStartup;
                string key = eventType.ToString();

                if (!TacticalTelemetry.ShouldEmit(_lastEmittedAt, key, signature, now, minSeconds, verbose))
                    return;

                string message = TacticalTelemetry.Summary(eventType, context);
                if (before != null)
                {
                    var after = group != null ? SnapshotGroup(group) : SnapshotBattle(battle);
                    message += " delta=" + TacticalTelemetry.Delta(before, after);
                }

                Plugin.Log.LogInfo(message);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-observer:" + eventType, "Tactical observer " + eventType + " failed: " + ex.Message);
            }
        }

        private static void ObserveQueuedOrder(
            Regiment issuer,
            GameObject advisedunit,
            bool queueProcessingTime,
            int orderType,
            float timeToMove,
            bool modifyLastWaypoint,
            bool newPath)
        {
            if (!Enabled()) return;

            try
            {
                var target = SafeRegiment(advisedunit);
                var queued = FindLatestQueuedOrder(issuer, advisedunit, orderType);
                if (queued == null) return;

                bool sourceUnderCommander = issuer != null && issuer.dlcw_isundercommander;
                bool targetUnderCommander = target != null && target.dlcw_isundercommander;
                string relation = OrderRelation(sourceUnderCommander, targetUnderCommander);
                float delay = queued.processingtime - GameVars.currenttimefromstart;
                int queueCount = issuer != null && issuer.orderqueue != null ? issuer.orderqueue.Count : -1;
                string signature = "queued|" + SafeInstanceId(issuer) + "|" + SafeInstanceId(target) + "|" +
                    orderType + "|" + queueCount + "|" + BucketSeconds(delay) + "|" + relation;

                EmitDirect(
                    "PlayerOrderQueued",
                    signature,
                    "[TacticalPlayerOrder] event=queued relation=" + relation +
                    " source=" + SafeUnitName(issuer) +
                    " sourceUnderCommander=" + sourceUnderCommander +
                    " target=" + SafeUnitName(target) +
                    " targetUnderCommander=" + targetUnderCommander +
                    " orderType=" + OrderTypeName(orderType) +
                    " queueCount=" + queueCount +
                    " delayHrs=" + FormatHours(delay) +
                    " queueProcessing=" + queueProcessingTime +
                    " newPath=" + newPath +
                    " modifyLast=" + modifyLastWaypoint +
                    " timedMove=" + FormatHours(timeToMove) +
                    " dlcWl=" + SafeDlcWlActive());

                var friction = TacticalOrderFriction.Evaluate(new TacticalOrderFrictionInput(
                    orderDelayEnabled: SafeUseOrderDelays(),
                    queueProcessing: queueProcessingTime,
                    queueDelayHours: delay,
                    delivery: TacticalOrderDelivery.Unknown,
                    deliveryProcessHours: 0f,
                    courierMissing: false,
                    orderState: SafeOrderState(target),
                    intendedPathId: SafePathId(target, true),
                    transmittedPathId: SafePathId(target, false),
                    contactChangedMaterially: false,
                    commanderInitiative01: 0.50f));
                var command = TacticalCommandLedger.Summarize(
                    BuildCommanderProfile(issuer),
                    BuildCommanderProfile(target),
                    friction);

                EmitDirect(
                    "TacticalCommandQueued",
                    "command-queued|" + SafeInstanceId(issuer) + "|" + SafeInstanceId(target) + "|" + command.Signature(),
                    "[TacticalCommand] event=queued relation=" + relation +
                    " source=" + SafeUnitName(issuer) +
                    " target=" + SafeUnitName(target) +
                    " summary=" + command.Signature() +
                    " reason=" + command.Reason +
                    " dlcWl=" + SafeDlcWlActive());
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-observer:player-order-queued", "Tactical player-order queue observer failed: " + ex.Message);
            }
        }

        private static void ObserveCourierLine(Regiment owner, Regiment sourceunit, Regiment targetunit, bool secondaryCourier)
        {
            if (!Enabled()) return;

            try
            {
                var line = FindLatestCourierLine(owner);
                var lineSource = line != null ? SafeRegiment(line.sourceunit) : sourceunit;
                var lineTarget = line != null ? SafeRegiment(line.targetunit) : targetunit;
                bool sourceUnderCommander = lineSource != null && lineSource.dlcw_isundercommander;
                bool targetUnderCommander = lineTarget != null && lineTarget.dlcw_isundercommander;
                string relation = OrderRelation(sourceUnderCommander, targetUnderCommander);
                string delivery = line == null ? "unknown" : (line.type == 0 ? "bugle" : "courier");
                TacticalOrderDelivery deliveryKind = DeliveryKind(delivery);
                float processTime = line != null ? line.processtime : 0f;
                string signature = "courier|" + SafeInstanceId(lineSource) + "|" + SafeInstanceId(lineTarget) + "|" +
                    delivery + "|" + BucketSeconds(processTime) + "|" + relation;

                EmitDirect(
                    "PlayerOrderCourier",
                    signature,
                    "[TacticalPlayerOrder] event=delivery relation=" + relation +
                    " source=" + SafeUnitName(lineSource) +
                    " sourceUnderCommander=" + sourceUnderCommander +
                    " target=" + SafeUnitName(lineTarget) +
                    " targetUnderCommander=" + targetUnderCommander +
                    " delivery=" + delivery +
                    " processHrs=" + FormatHours(processTime) +
                    " secondary=" + secondaryCourier +
                    " dlcWl=" + SafeDlcWlActive());

                bool courierMissing = line != null &&
                    deliveryKind == TacticalOrderDelivery.Courier &&
                    line.lineactive &&
                    line.courierref == null;
                var friction = TacticalOrderFriction.Evaluate(new TacticalOrderFrictionInput(
                    orderDelayEnabled: SafeUseOrderDelays(),
                    queueProcessing: false,
                    queueDelayHours: 0f,
                    delivery: deliveryKind,
                    deliveryProcessHours: processTime,
                    courierMissing: courierMissing,
                    orderState: SafeOrderState(lineTarget),
                    intendedPathId: SafePathId(lineTarget, true),
                    transmittedPathId: SafePathId(lineTarget, false),
                    contactChangedMaterially: false,
                    commanderInitiative01: 0.50f));
                var command = TacticalCommandLedger.Summarize(
                    BuildCommanderProfile(lineSource),
                    BuildCommanderProfile(lineTarget),
                    friction);

                EmitDirect(
                    "TacticalOrderDelivery",
                    "order-delivery|" + SafeInstanceId(lineSource) + "|" + SafeInstanceId(lineTarget) + "|" +
                    delivery + "|" + command.Signature(),
                    "[TacticalOrder] event=delivery relation=" + relation +
                    " source=" + SafeUnitName(lineSource) +
                    " target=" + SafeUnitName(lineTarget) +
                    " delivery=" + delivery +
                    " friction=" + friction.State +
                    " delivered=" + friction.IsDelivered +
                    " delayed=" + friction.IsDelayed +
                    " pathLag=" + friction.TransmittedPathDiffers +
                    " pressure=" + FormatHours(friction.DelayPressure) +
                    " command=" + command.Signature() +
                    " dlcWl=" + SafeDlcWlActive());
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-observer:player-order-courier", "Tactical player-order courier observer failed: " + ex.Message);
            }
        }

        private static void EmitDirect(string key, string signature, string message)
        {
            bool verbose = Plugin.Instance != null && Plugin.Instance.TacticalObserverVerboseLogging.Value;
            float minSeconds = Plugin.Instance != null
                ? Mathf.Max(1f, Plugin.Instance.TacticalObserverMinSecondsBetweenSummaries.Value)
                : 30f;
            if (!TacticalTelemetry.ShouldEmit(_lastEmittedAt, key, signature, Time.realtimeSinceStartup, minSeconds, verbose))
                return;

            Plugin.Log.LogInfo(message);
        }

        private static TacticalBattleContext BuildContext(AIBattle battle, Regiment group)
        {
            var context = TacticalBattleContext.Empty();
            if (battle == null) return context;

            int side = SafeIntField(battle, ref _sideOfAiField, "sideofai", -1);
            int macro = SafeIntField(battle, ref _macroAiField, "macroai", -99);
            var bunits = SafeField<BattleUnits>(battle, ref _bunitsField, "bunits");
            var unitsUsed = SafeList(battle, ref _unitsUsedField, "unitsused");
            var allGroups = SafeList(battle, ref _allGroupsAssignedField, "allgroupsassigned");
            var chain = SafeList(battle, ref _objectiveChainField, "objective" + "chain");
            bool groupScoped = group != null;

            context.Side = side;
            context.MacroAi = macro;
            context.Alliance = SafeAlliance(bunits, side);
            context.GroupCount = groupScoped ? 1 : CountList(allGroups);
            context.ObjectiveChainCount = CountList(chain);
            context.SectorSource = context.ObjectiveChainCount > 0
                ? TacticalSectorSource.ObjectiveChain
                : TacticalSectorSource.None;
            context.SectorSignature = "chains=" + context.ObjectiveChainCount + ",groups=" + context.GroupCount;
            context.OrderSignature = groupScoped ? BuildGroupOrderSignature(group) : BuildOrderSignature(unitsUsed);
            context.ForceBalance = SafeForceBalance(bunits, side);
            context.ReinforcementsWithin24Hours = SafeReinforcements(bunits, side);

            if (groupScoped) MergeGroupCounts(group, context);
            else CountUnits(unitsUsed, context);

            return context;
        }

        private static TacticalObserverSnapshot SnapshotBattle(AIBattle battle)
        {
            var context = BuildContext(battle, null);
            return new TacticalObserverSnapshot
            {
                GroupCount = context.GroupCount,
                ChargingCount = context.ChargingCount,
                FeudGroupCount = context.FeudGroupCount,
                ReserveGroupCount = context.ReserveGroupCount,
                ArtilleryGroupCount = context.ArtilleryGroupCount,
                FallbackCount = context.FallbackCount,
                RetreatingCount = context.RetreatingCount,
                Signature = TacticalTelemetry.Signature(TacticalObservedEvent.Macro, context)
            };
        }

        private static TacticalObserverSnapshot SnapshotGroup(Regiment group)
        {
            var snapshot = TacticalObserverSnapshot.Empty();
            if (group == null || group.allattachedunits == null) return snapshot;

            snapshot.GroupCount = 1;
            for (int i = 0; i < group.allattachedunits.Length; i++)
            {
                var unit = group.allattachedunits[i];
                if (unit == null) continue;
                if (unit.movementmode == 3) snapshot.ChargingCount++;
                if (unit.movementmode == 2) snapshot.FallbackCount++;
                if (unit.movementmode == 5 || unit.movementmode == 6) snapshot.RetreatingCount++;
                if (unit.unittyp == 2) snapshot.ArtilleryGroupCount++;
            }

            snapshot.Signature = "g=" + SafeInstanceId(group) + "|c=" + snapshot.ChargingCount + "|f=" + snapshot.FallbackCount;
            return snapshot;
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableTacticalObserver.Value;
        }

        private static void CountUnits(IList units, TacticalBattleContext context)
        {
            if (units == null || context == null) return;
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i] as Regiment;
                if (unit == null) continue;
                CountUnit(unit, context);
            }
        }

        private static void MergeGroupCounts(Regiment group, TacticalBattleContext context)
        {
            if (group == null || group.allattachedunits == null || context == null) return;
            for (int i = 0; i < group.allattachedunits.Length; i++)
                CountUnit(group.allattachedunits[i], context);
        }

        private static void CountUnit(Regiment unit, TacticalBattleContext context)
        {
            if (unit == null || context == null) return;
            if (unit.movementmode == 3) context.ChargingCount++;
            if (unit.movementmode == 2) context.FallbackCount++;
            if (unit.movementmode == 5 || unit.movementmode == 6) context.RetreatingCount++;
            if (unit.ai_feudstance >= 0) context.FeudGroupCount++;
            if (unit.unittyp == 2) context.ArtilleryGroupCount++;
            if (unit.unittyp > 13 && SafeIntField(unit, ref _orderedStanceField, "ai_" + "stanceordered", -1) == 1)
                context.ReserveGroupCount++;
            if (unit.unitrange != null)
            {
                if (unit.unitrange.closestenemyunitfarreg != null) context.VisibleEnemyCount++;
                else if (unit.unitrange.closestenemyunit != null) context.VisibleEnemyCount++;
            }
        }

        private static string BuildOrderSignature(IList units)
        {
            if (units == null) return "-";

            int moving = 0;
            int waiting = 0;
            int interrupted = 0;
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i] as Regiment;
                if (unit == null) continue;
                if (unit.regimentpaths > 0) moving++;
                if (unit.pathinterrupted) interrupted++;
                if (unit.regimentpaths <= 0 && unit.movementmode == 0) waiting++;
            }

            return "moving=" + moving + ",waiting=" + waiting + ",interrupted=" + interrupted;
        }

        private static string BuildGroupOrderSignature(Regiment group)
        {
            if (group == null || group.allattachedunits == null) return "-";

            int moving = 0;
            int waiting = 0;
            int interrupted = 0;
            for (int i = 0; i < group.allattachedunits.Length; i++)
            {
                var unit = group.allattachedunits[i];
                if (unit == null) continue;
                if (unit.regimentpaths > 0) moving++;
                if (unit.pathinterrupted) interrupted++;
                if (unit.regimentpaths <= 0 && unit.movementmode == 0) waiting++;
            }

            return "group=" + SafeInstanceId(group) + ",moving=" + moving + ",waiting=" + waiting + ",interrupted=" + interrupted;
        }

        private static Regiment.OrderQueue FindLatestQueuedOrder(Regiment issuer, GameObject advisedUnit, int orderType)
        {
            if (issuer == null || issuer.orderqueue == null) return null;
            for (int i = issuer.orderqueue.Count - 1; i >= 0; i--)
            {
                var queued = issuer.orderqueue[i];
                if (queued == null) continue;
                if (queued.ordertype == orderType && queued.advisedunit == advisedUnit) return queued;
            }

            return null;
        }

        private static Regiment.OrderQueue.CourierLine FindLatestCourierLine(Regiment issuer)
        {
            if (issuer == null || issuer.orderqueue == null) return null;
            for (int i = issuer.orderqueue.Count - 1; i >= 0; i--)
            {
                var queued = issuer.orderqueue[i];
                if (queued == null || queued.courierline == null || queued.courierline.Count <= 0) continue;
                return queued.courierline[queued.courierline.Count - 1];
            }

            return null;
        }

        private static Regiment SafeRegiment(GameObject unit)
        {
            try
            {
                return unit != null ? unit.GetComponent<Regiment>() : null;
            }
            catch
            {
                return null;
            }
        }

        private static string OrderRelation(bool sourceUnderCommander, bool targetUnderCommander)
        {
            if (!sourceUnderCommander && targetUnderCommander) return "ai-to-player-subordinate";
            if (sourceUnderCommander && targetUnderCommander) return "player-chain";
            if (sourceUnderCommander && !targetUnderCommander) return "player-to-ai";
            return "ai-chain";
        }

        private static string OrderTypeName(int orderType)
        {
            if (orderType == 0) return "move-append";
            if (orderType == 1) return "move-new";
            if (orderType == 2) return "stop";
            if (orderType >= 3 && orderType <= 9) return "stance-" + (orderType - 3);
            if (orderType >= 10 && orderType <= 19) return "formation-" + (orderType - 10);
            if (orderType == 20) return "refuse-left";
            if (orderType == 21) return "refuse-right";
            if (orderType == 23) return "detach-toggle";
            if (orderType >= 30 && orderType <= 39) return "combat-" + (orderType - 30);
            if (orderType >= 100 && orderType < 120) return "campaign-stance-" + (orderType - 100);
            if (orderType >= 120 && orderType < 130) return "cavalry-" + (orderType - 120);
            return "type-" + orderType;
        }

        private static string SafeUnitName(Regiment unit)
        {
            try
            {
                if (unit == null) return "-";
                string name = ((UnityEngine.Object)unit).name;
                return (string.IsNullOrEmpty(name) ? "unit" : name.Replace(' ', '_')) + "#" + SafeInstanceId(unit);
            }
            catch
            {
                return "unit#" + SafeInstanceId(unit);
            }
        }

        private static bool SafeDlcWlActive()
        {
            try
            {
                return DLC_WL.dlc_scenarioactive;
            }
            catch
            {
                return false;
            }
        }

        private static string FormatHours(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0.00";
            return value.ToString("0.00");
        }

        private static string BucketSeconds(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0";
            return Mathf.Round(value * 2f).ToString("0");
        }

        private static int SafeAlliance(BattleUnits bunits, int side)
        {
            try
            {
                if (bunits == null || bunits.alliance == null) return -1;
                if (side < 0 || side >= bunits.alliance.Length) return -1;
                return bunits.alliance[side];
            }
            catch
            {
                return -1;
            }
        }

        private static float SafeForceBalance(BattleUnits bunits, int side)
        {
            try
            {
                if (bunits == null || bunits.sideinformation == null) return 0f;
                if (side < 0 || side >= bunits.sideinformation.Length) return 0f;
                return bunits.sideinformation[side].forcebalance;
            }
            catch
            {
                return 0f;
            }
        }

        private static float SafeReinforcements(BattleUnits bunits, int side)
        {
            try
            {
                if (bunits == null || bunits.sideinformation == null) return 0f;
                if (side < 0 || side >= bunits.sideinformation.Length) return 0f;
                return bunits.sideinformation[side].reinforcementarrivalswithin24hrs;
            }
            catch
            {
                return 0f;
            }
        }

        private static int SafeIntField(object instance, ref FieldInfo cache, string name, int fallback)
        {
            try
            {
                if (instance == null) return fallback;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                if (cache == null) return fallback;
                return (int)cache.GetValue(instance);
            }
            catch
            {
                return fallback;
            }
        }

        private static T SafeField<T>(object instance, ref FieldInfo cache, string name) where T : class
        {
            try
            {
                if (instance == null) return null;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                return cache != null ? cache.GetValue(instance) as T : null;
            }
            catch
            {
                return null;
            }
        }

        private static IList SafeList(object instance, ref FieldInfo cache, string name)
        {
            try
            {
                if (instance == null) return null;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                return cache != null ? cache.GetValue(instance) as IList : null;
            }
            catch
            {
                return null;
            }
        }

        private static TacticalCommanderProfile BuildCommanderProfile(Regiment unit)
        {
            if (unit == null)
            {
                return TacticalCommanderProfile.FromVanillaShape(
                    0,
                    "unknown",
                    -1,
                    false,
                    false,
                    -1,
                    -1,
                    -1,
                    0.50f);
            }

            try
            {
                return TacticalCommanderProfile.FromVanillaShape(
                    SafeInstanceId(unit),
                    SafeUnitName(unit),
                    unit.unittyp,
                    unit.istopunit,
                    unit.dlcw_isundercommander,
                    SafeParentId(unit),
                    SafeAlliance(unit),
                    SafeSide(unit),
                    0.50f);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-observer:commander-profile", "Tactical commander profile lookup failed: " + ex.Message);
                return TacticalCommanderProfile.FromVanillaShape(
                    SafeInstanceId(unit),
                    SafeUnitName(unit),
                    -1,
                    false,
                    false,
                    -1,
                    -1,
                    -1,
                    0.50f);
            }
        }

        private static int SafePathId(Regiment unit, bool ignoreOrderDelay)
        {
            try
            {
                return unit != null ? unit.GetLastTransmittedPath(ignoreOrderDelay) : -1;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-observer:path-id", "Tactical path id lookup failed: " + ex.Message);
                return -1;
            }
        }

        private static int SafeParentId(Regiment unit)
        {
            try
            {
                if (unit == null) return -1;
                if (unit.parentregiment != null)
                {
                    var parent = SafeRegiment(unit.parentregiment);
                    return SafeInstanceId(parent);
                }

                if (_parentRegimentField == null) _parentRegimentField = AccessTools.Field(typeof(Regiment), "parentregiment");
                var parentObj = _parentRegimentField != null ? _parentRegimentField.GetValue(unit) as GameObject : null;
                var reflectedParent = SafeRegiment(parentObj);
                return SafeInstanceId(reflectedParent);
            }
            catch
            {
                return -1;
            }
        }

        private static int SafeAlliance(Regiment unit)
        {
            try
            {
                if (unit == null) return -1;
                if (_allianceField == null) _allianceField = AccessTools.Field(typeof(Regiment), "alliance");
                if (_allianceField != null) return (int)_allianceField.GetValue(unit);
                return unit.alliance;
            }
            catch
            {
                return -1;
            }
        }

        private static int SafeSide(Regiment unit)
        {
            try
            {
                if (unit == null) return -1;
                if (_sideField == null) _sideField = AccessTools.Field(typeof(Regiment), "side");
                return _sideField != null ? (int)_sideField.GetValue(unit) : -1;
            }
            catch
            {
                return -1;
            }
        }

        private static int SafeOrderState(Regiment unit)
        {
            return SafeIntField(unit, ref _orderStateField, "orderstate", -1);
        }

        private static bool SafeUseOrderDelays()
        {
            try
            {
                return GameVars.useorderdelays;
            }
            catch
            {
                return false;
            }
        }

        private static TacticalOrderDelivery DeliveryKind(string delivery)
        {
            if (delivery == "bugle") return TacticalOrderDelivery.Bugle;
            if (delivery == "courier") return TacticalOrderDelivery.Courier;
            if (delivery == "immediate") return TacticalOrderDelivery.Immediate;
            return TacticalOrderDelivery.Unknown;
        }

        private static int CountList(IList list)
        {
            return list == null ? 0 : list.Count;
        }

        private static int SafeInstanceId(UnityEngine.Object obj)
        {
            try
            {
                return obj != null ? obj.GetInstanceID() : 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
