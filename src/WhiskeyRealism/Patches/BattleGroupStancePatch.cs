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
    // B5 group sector stance scorer. This is deliberately limited to stance
    // pressure; it does not issue movement, reserve, artillery, fallback, or
    // charge orders.
    [HarmonyPatch(typeof(AIBattle), "AdjustGroupAIStance")]
    internal static class BattleGroupStancePatch
    {
        private static readonly Dictionary<string, float> _lastLoggedAt = new Dictionary<string, float>();
        private static FieldInfo _macroAiField;
        private static FieldInfo _sideOfAiField;
        private static FieldInfo _bunitsField;
        private static FieldInfo _unitsUsedField;
        private static FieldInfo _orderedStanceField;

        [HarmonyPostfix]
        internal static void Postfix(AIBattle __instance)
        {
            if (!Enabled() || __instance == null) return;

            try
            {
                Apply(__instance);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-group-stance:failed", "Tactical group sector stance failed: " + ex.Message);
            }
        }

        private static void Apply(AIBattle battle)
        {
            int side = SafeIntField(battle, ref _sideOfAiField, "sideofai", -1);
            int macro = SafeIntField(battle, ref _macroAiField, "macroai", -99);
            var bunits = SafeField<BattleUnits>(battle, ref _bunitsField, "bunits");
            var units = SafeList(battle, ref _unitsUsedField, "unitsused");
            if (side < 0 || macro < 0 || bunits == null || units == null) return;

            for (int i = 0; i < units.Count; i++)
            {
                var group = units[i] as Regiment;
                if (group == null || !TacticalDoctrineScorer.AllowsLocalGroupStanceWriter(group.unittyp)) continue;
                ApplyGroup(bunits, side, macro, group, i);
            }
        }

        private static void ApplyGroup(BattleUnits bunits, int side, int macro, Regiment group, int index)
        {
            if (!WlAllowsControl(group)) return;
            if (!OrderFrictionAllowsChange(group)) return;

            var sector = BuildGroupSector(group, index);
            var decision = TacticalDoctrineScorer.DecideGroupStance(new TacticalGroupStanceDecisionInput(
                SafeIntField(group, ref _orderedStanceField, "ai_" + "stanceordered", group.ai_stanceordered),
                macro,
                sector,
                true,
                true));

            if (decision.Kind != TacticalDoctrineDecisionKind.Apply) return;
            if (decision.GroupStance == group.ai_stanceordered) return;
            if (decision.GroupStance == 4) return;
            if (decision.GroupStance < 0 || decision.GroupStance > 3) return;

            var gameObject = UnityObject(group);
            if (gameObject == null || !gameObject.activeInHierarchy) return;

            bunits.ChangeStance(gameObject, decision.GroupStance, immediate: false, overwriteaigroups: false);
            group.ai_stance = decision.GroupStance;
            group.ai_stanceordered = decision.GroupStance;
            group.lastaistancechangetime = GameVars.currenttimefromstart;
            LogDecision(side, group, sector, decision);
        }

        private static TacticalSectorAssessment BuildGroupSector(Regiment group, int index)
        {
            float own = Math.Max(group.groupowninrange, group.groupstrengthaigroup);
            float enemy = Math.Max(0f, group.groupenemiesinrange);
            bool hasEnemy = enemy > 0f;
            bool hasClosestEnemy = group.unitrange != null && group.unitrange.closestenemyunitfarreg != null;
            float confidence = hasEnemy ? (hasClosestEnemy ? 0.8f : 0.55f) : 0.45f;
            bool flankRisk = group.flanksthreated > 0f || group.outflanked > 0;
            bool strongPoint = group.covervalue > 0.5f || group.fortinrange;
            var sector = new TacticalSectorAssessment(
                index,
                TacticalSectorSource.AngleSlice,
                own,
                enemy,
                confidence,
                strongPoint,
                flankRisk,
                TacticalSectorMission.Hold);
            var result = TacticalSectorLedger.Evaluate(new[] { sector });
            return result.Sectors.Length > 0 ? result.Sectors[0] : sector;
        }

        private static bool WlAllowsControl(Regiment group)
        {
            var decision = TacticalWlActionGuard.Decide(
                true,
                DLC_WL.dlc_scenarioactive,
                TacticalWlGuardAction.FeudMovement,
                group != null && group.dlcw_isundercommander,
                group != null && group.dlcw_isundercommander,
                AttachedUnitUnderPlayerCommander(group));
            return decision.Allow;
        }

        private static bool AttachedUnitUnderPlayerCommander(Regiment group)
        {
            if (group == null || group.allattachedunits == null) return false;
            for (int i = 0; i < group.allattachedunits.Length; i++)
            {
                var unit = group.allattachedunits[i];
                if (unit != null && unit.dlcw_isundercommander) return true;
            }

            return false;
        }

        private static bool OrderFrictionAllowsChange(Regiment group)
        {
            return TacticalOrderSettlementGate.Evaluate(new TacticalOrderSettlementGate.Input
            {
                OrderQueueCount = SafeOrderQueueCount(group),
                OrderState = group != null ? group.orderstate : -1,
                RegimentPaths = group != null ? group.regimentpaths : 0,
                PathInterrupted = group != null && group.pathinterrupted,
                MovementMode = group != null ? group.movementmode : -1
            }).AllowChange;
        }

        private static int SafeOrderQueueCount(Regiment group)
        {
            try { return group != null && group.orderqueue != null ? group.orderqueue.Count : 0; }
            catch { return 1; }
        }

        private static void LogDecision(
            int side,
            Regiment group,
            TacticalSectorAssessment sector,
            TacticalGroupStanceDecision decision)
        {
            string signature = side + "|" + SafeInstanceId(group) + "|" + sector.SectorId + "|" +
                decision.GroupStance + "|" + decision.Reason;
            if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "group-decision", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            Plugin.Log.LogInfo("[TacticalGroupDecision] side=" + side +
                " group=" + SafeInstanceId(group) +
                " sector=" + sector.SectorId +
                " stance=" + decision.GroupStance +
                " mission=" + sector.Mission +
                " reason=" + decision.Reason +
                " odds=" + sector.Odds.ToString("0.00") +
                " confidence=" + sector.Confidence.ToString("0.00"));
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableTacticalGroupSectorStance.Value;
        }

        private static int SafeIntField(object instance, ref FieldInfo cache, string name, int fallback)
        {
            try
            {
                if (instance == null) return fallback;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                if (cache == null) return fallback;
                return Convert.ToInt32(cache.GetValue(instance));
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

        private static GameObject UnityObject(Regiment unit)
        {
            try
            {
                return unit != null ? ((Component)unit).gameObject : null;
            }
            catch
            {
                return null;
            }
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
