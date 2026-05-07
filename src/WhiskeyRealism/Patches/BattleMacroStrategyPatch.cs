using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
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
            if (side < 0 || bunits == null) return;

            var odds = BuildRuntimeOdds(bunits, side);
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

            _macroAiField.SetValue(battle, decision.MacroAi);
            LogDecision(side, vanillaMacro, decision, odds);
        }

        private static TacticalOddsAssessment BuildRuntimeOdds(BattleUnits bunits, int side)
        {
            float forceBalance = SafeSideInfoFloat(bunits, side, "forcebalance");
            float own = Math.Max(1f, SafeSideInfoFloat(bunits, side, "totalactiveforce"));
            float enemy = own;
            if (forceBalance > 0.01f && forceBalance < 0.99f)
                enemy = own * Math.Max(0.1f, (1f - forceBalance) / Math.Max(0.1f, forceBalance));

            float reinforcements = SafeSideInfoFloat(bunits, side, "reinforcementarrivalswithin24hrs");
            var contact = TacticalContactLedger.Classify(new TacticalContactInput(
                enemy,
                enemy,
                enemy,
                0f,
                false,
                false));

            return TacticalOddsDoctrine.Evaluate(new TacticalOddsInput(
                own,
                enemy,
                enemy,
                enemy,
                reinforcements,
                0f,
                contact,
                Array.Empty<TacticalSectorAssessment>()));
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
    }
}
