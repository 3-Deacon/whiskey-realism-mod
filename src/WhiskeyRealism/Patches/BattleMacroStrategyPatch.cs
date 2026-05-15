using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // B4 tactical macro stance scorer. Vanilla owns retreat timers and the
    // dynamic macro path; this Postfix only biases macroai after safe vanilla
    // paths have run and only when the default-off config is enabled.
    [HarmonyPatch(typeof(AIBattle), "CheckGlobalAIStrategy")]
    internal static class BattleMacroStrategyPatch
    {
        private static readonly Dictionary<string, float> _lastLoggedAt = new Dictionary<string, float>();
        private static FieldInfo _macroAiField;
        private static FieldInfo _sideOfAiField;
        private static FieldInfo _bunitsField;
        private static FieldInfo _unitsUsedField;
        private static FieldInfo _receivedFireField;

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
                OnceLog.Warning("tactical-macro:failed", "Tactical macro stance scorer failed: " + ex.Message);
            }
        }

        private static void Apply(AIBattle battle)
        {
            int side = SafeIntField(battle, ref _sideOfAiField, "sideofai", -1);
            int vanillaMacro = SafeIntField(battle, ref _macroAiField, "macroai", -99);
            var bunits = SafeField<BattleUnits>(battle, ref _bunitsField, "bunits");
            var units = SafeList(battle, ref _unitsUsedField, "unitsused");
            if (side < 0 || bunits == null) return;

            int allianceId = ResolveAllianceId(bunits, side);
            if (TryApplyOrchestrator(battle, bunits, vanillaMacro, allianceId)) return;

            // Fallback: existing scorer path preserved verbatim for regression triage.
            var odds = BuildRuntimeOdds(bunits, side, units);
            var decision = TacticalDoctrineScorer.DecideMacro(new TacticalMacroDecisionInput(
                vanillaMacro,
                GameVars.aistrategy >= 0,
                SideInfoMacro(bunits, side) >= 0,
                vanillaMacro == 3 || EndBattleActive(bunits),
                CommanderAggression01(bunits, side),
                odds));

            if (decision.Kind != TacticalDoctrineDecisionKind.Apply) return;
            if (decision.MacroAi == vanillaMacro) return;
            if (decision.MacroAi < -1 || decision.MacroAi > 3) return;
            if (_macroAiField == null) return;
            if (!AllowsVanillaWrites()) return;

            _macroAiField.SetValue(battle, decision.MacroAi);
            LogDecision(side, vanillaMacro, decision, odds);
        }

        private static bool TryApplyOrchestrator(AIBattle battle, BattleUnits bunits, int vanillaMacro, int allianceId)
        {
            if (Plugin.EnableTacticalOrchestratorArmy == null || !Plugin.EnableTacticalOrchestratorArmy.Value) return false;
            if (allianceId < 0 || allianceId >= 2) return false;
            if (vanillaMacro == 3 || EndBattleActive(bunits)) return true;

            var sideOrch = TacticalBattleCoordinator.GetSideOrchestrator(allianceId);
            if (sideOrch?.Army == null) return false;

            int macro = OperationMacroAi(sideOrch.Army);
            if (macro == int.MinValue && !sideOrch.Army.HasPlan) return false;
            if (macro == int.MinValue)
                macro = sideOrch.Army.CurrentMacroAi;

            // -1 means "let vanilla decide" -- return true so we DON'T fall through to the scorer.
            // The orchestrator/ledger has spoken; its choice is "no opinion this tick."
            if (macro < -1 || macro > 3) return true;
            if (macro == -1) return true;
            if (macro == vanillaMacro) return true;        // already aligned; no write needed
            if (_macroAiField == null) return true;
            if (!AllowsVanillaWrites()) return true;

            _macroAiField.SetValue(battle, macro);
            string planId = sideOrch.Army.HasPlan ? sideOrch.Army.CurrentPlan.PlanId.ToString() : "no-plan";
            string planPhase = sideOrch.Army.HasPlan ? sideOrch.Army.CurrentPlan.Phase.ToString() : "no-plan";
            OnceLog.Info("orch-macro-write:" + allianceId + ":" + vanillaMacro + "->" + macro,
                "[TacticalMacroDecision] side=" + allianceId
                + " old=" + TacticalTelemetry.MacroName(vanillaMacro)
                + " orchestrator=" + TacticalTelemetry.MacroName(macro)
                + " operation=" + sideOrch.Army.CurrentOperation.Shape
                + " opPhase=" + sideOrch.Army.CurrentOperation.Phase
                + " plan=" + planId
                + " phase=" + planPhase);
            return true;
        }

        private static int OperationMacroAi(ArmyOrchestrator army)
        {
            if (army == null || army.CommanderMode == TacticalCommanderMode.Off)
                return int.MinValue;

            return TacticalOperationsLedgerModel.OperationMacroAi(army.CurrentOperation);
        }

        private static int ResolveAllianceId(BattleUnits bunits, int side)
        {
            try
            {
                if (bunits == null || bunits.alliance == null || side < 0 || side >= bunits.alliance.Length) return -1;
                return bunits.alliance[side];
            }
            catch { return -1; }
        }

        private static TacticalOddsAssessment BuildRuntimeOdds(BattleUnits bunits, int side, IList units)
        {
            float own = Math.Max(1f, SafeSideInfoFloat(bunits, side, "totalactiveforce"));
            float confirmedEnemy = EstimateVisibleEnemyStrength(units);
            float inferredEnemy = EstimateInferredEnemyStrength(units);
            float reinforcements = SafeSideInfoFloat(bunits, side, "reinforcementarrivalswithin24hrs");
            var contact = TacticalContactLedger.Classify(new TacticalContactInput(
                confirmedEnemy,
                confirmedEnemy,
                inferredEnemy,
                confirmedEnemy > 0f ? 0f : 9999f,
                AnyReceivedFire(units),
                confirmedEnemy <= 0f));

            return TacticalOddsDoctrine.Evaluate(new TacticalOddsInput(
                own,
                confirmedEnemy,
                confirmedEnemy,
                inferredEnemy,
                reinforcements,
                0f,
                contact,
                BuildSectorAssessments(units)));
        }

        private static void LogDecision(
            int side,
            int vanillaMacro,
            TacticalMacroDecision decision,
            TacticalOddsAssessment odds)
        {
            string signature = side + "|" + vanillaMacro + "|" + decision.MacroAi + "|" +
                decision.Reason + "|" + odds.DecisiveSectorId + "|" + odds.InferiorForcePosture;
            if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "macro-decision", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            Plugin.Log.LogInfo("[TacticalMacroDecision] side=" + side +
                " old=" + TacticalTelemetry.MacroName(vanillaMacro) +
                " whiskey=" + TacticalTelemetry.MacroName(decision.MacroAi) +
                " reason=" + decision.Reason +
                " current=" + odds.CurrentGlobalOdds.ToString("0.00") +
                " projected=" + odds.ProjectedGlobalOdds.ToString("0.00") +
                " confidence=" + odds.Confidence.ToString("0.00"));
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableTacticalMacroStanceScorer.Value;
        }

        private static bool AllowsVanillaWrites()
        {
            try
            {
                return Plugin.Instance != null &&
                    TacticalCommanderModePolicy.AllowsWrites(Plugin.Instance.TacticalCommanderModeValue);
            }
            catch
            {
                return false;
            }
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

        private static TacticalSectorAssessment[] BuildSectorAssessments(IList units)
        {
            if (units == null) return Array.Empty<TacticalSectorAssessment>();

            var sectors = new List<TacticalSectorAssessment>();
            int sectorId = 0;
            for (int i = 0; i < units.Count; i++)
            {
                var group = units[i] as Regiment;
                if (group == null || group.unittyp <= 13) continue;

                Regiment closest = TacticalFogOfWarContact.ClosestVisibleEnemy(group);
                sectors.Add(TacticalGroupSectorEstimator.BuildSector(new TacticalGroupContactInput(
                    sectorId++,
                    Math.Max(group.groupowninrange, group.groupstrengthaigroup),
                    group.groupenemiesinrange,
                    EnemyAngleStrength(group),
                    closest != null ? closest.strength : 0f,
                    closest != null ? closest.unittyp : -1,
                    closest != null ? SafeName(closest) : string.Empty,
                    closest != null && closest.isrouted,
                    closest != null && closest.permanentlydetached,
                    group.flanksthreated > 0f || group.outflanked > 0,
                    group.covervalue > 0.5f || group.fortinrange)));
            }

            return sectors.ToArray();
        }

        private static float EstimateVisibleEnemyStrength(IList units)
        {
            return TacticalFogOfWarContact.VisibleEnemyStrength(units);
        }

        private static float EstimateInferredEnemyStrength(IList units)
        {
            if (units == null) return 0f;

            float total = 0f;
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i] as Regiment;
                total += EnemyAngleStrength(unit);
            }

            return total;
        }

        private static float EnemyAngleStrength(Regiment unit)
        {
            try
            {
                if (unit == null ||
                    unit.unitrange == null ||
                    unit.unitrange.enemystrengthwithinangle == null ||
                    !TacticalFogOfWarContact.HasVisibleEnemy(unit))
                    return 0f;
                float total = 0f;
                for (int i = 0; i < unit.unitrange.enemystrengthwithinangle.Length; i++)
                    total += Math.Max(0f, unit.unitrange.enemystrengthwithinangle[i]);
                return total;
            }
            catch
            {
                return 0f;
            }
        }

        private static bool AnyReceivedFire(IList units)
        {
            if (units == null) return false;

            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i] as Regiment;
                if (unit == null) continue;
                try
                {
                    if (_receivedFireField == null) _receivedFireField = AccessTools.Field(unit.GetType(), "receivedfire");
                    var received = _receivedFireField != null ? _receivedFireField.GetValue(unit) as IList : null;
                    if (received != null && received.Count > 0) return true;
                }
                catch
                {
                }
            }

            return false;
        }

        private static float SafeSideInfoFloat(BattleUnits bunits, int side, string fieldName)
        {
            try
            {
                if (bunits == null || bunits.sideinformation == null) return 0f;
                if (side < 0 || side >= bunits.sideinformation.Length) return 0f;
                var info = bunits.sideinformation[side];
                var field = AccessTools.Field(info.GetType(), fieldName);
                if (field == null) return 0f;
                object value = field.GetValue(info);
                return value == null ? 0f : Convert.ToSingle(value);
            }
            catch
            {
                return 0f;
            }
        }

        private static int SideInfoMacro(BattleUnits bunits, int side)
        {
            try
            {
                if (bunits == null || bunits.sideinformation == null) return -1;
                if (side < 0 || side >= bunits.sideinformation.Length) return -1;
                return bunits.sideinformation[side].macroai;
            }
            catch
            {
                return -1;
            }
        }

        private static bool EndBattleActive(BattleUnits bunits)
        {
            try
            {
                return bunits != null && bunits.endbattle >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static float CommanderAggression01(BattleUnits bunits, int side)
        {
            try
            {
                if (bunits == null || side < 0 || side >= bunits.alliance.Length) return 0.5f;
                int commanderId = bunits.GetCommandingOfficerFromSide(side);
                if (GameVars.commander == null || commanderId < 0 || commanderId >= GameVars.commander.Count) return 0.5f;
                float initiative = GameVars.commander[commanderId].GetCommanderInitiative();
                if (float.IsNaN(initiative) || float.IsInfinity(initiative)) return 0.5f;
                if (initiative < 0f) return 0f;
                if (initiative > 1f) return 1f;
                return initiative;
            }
            catch
            {
                return 0.5f;
            }
        }

        private static string SafeName(Regiment unit)
        {
            try
            {
                return unit != null ? ((Component)unit).gameObject.name : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
