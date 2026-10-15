using System;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Stage 1 (F) — writes Whiskey's doctrine output to
    // BattleUnits.sideinformation[side].macroai as a Prefix on
    // AIBattle.CheckGlobalAIStrategy.
    //
    // Vanilla anchors (decompile / 2026-05-23):
    //   AIBattle.CheckGlobalAIStrategy() — decompile line 6314.
    //     Reads bunits.sideinformation[sideofai].macroai at line 6383 and uses it
    //     as the override for the instance macroai (line 6385) BEFORE its own
    //     hysteresis-driven decision pipeline runs. If we WRITE the side info
    //     before vanilla reads it, vanilla picks up our value.
    //   AIBattle.AdjustGroupAIStance() — decompile line 4220.
    //     Walks every group on the side and writes ai_stanceordered based on the
    //     resolved macroai. macroai == 2 (defend) forces every group to
    //     ai_stance = 2, which is how vanilla naturally forms the line of battle.
    //
    // We don't replace vanilla's machinery — we DRIVE it. After this patch
    // writes macroai, vanilla's own AdjustGroupAIStance handles per-group
    // stance for brigades Whiskey doesn't have explicit roles for.
    //
    // Player protection: skipped when the side's alliance == GameVars.playeralliance
    // AND we're not in AI-vs-AI mode. Mirrors vanilla's own
    // `aigroup.alliance == GameVars.playeralliance && !GameVars.ai_vs_ai && !GamePrefs.playeraistanceactive[*]`
    // gate observed throughout AIBattle.
    [HarmonyPatch(typeof(AIBattle), "CheckGlobalAIStrategy")]
    internal static class BattleMacroAIOverridePatch
    {
        private static FieldInfo _bunitsField;
        private static FieldInfo _sideofaiField;
        private static bool _missingAnchorLogged;
        private static readonly object TelemetryGate = new object();
        private static readonly System.Collections.Generic.Dictionary<string, float> _lastWriteTelemetryAt =
            new System.Collections.Generic.Dictionary<string, float>();
        private const float WriteTelemetryThrottleSeconds = 5f;

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        internal static void Prefix(AIBattle __instance)
        {
            using (TelemetryPerf.Scope("tactical.patch.macroai-override", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                try
                {
                    if (Plugin.EnableVanillaMacroAiSync == null || !Plugin.EnableVanillaMacroAiSync.Value)
                        return;

                    int sideofai = ReadSideOfAi(__instance);
                    if (sideofai < 0 || sideofai > 1) return;

                    BattleUnits bunits = ReadBattleUnits(__instance);
                    if (bunits == null) return;
                    if (bunits.sideinformation == null || sideofai >= bunits.sideinformation.Length) return;
                    if (bunits.alliance == null || sideofai >= bunits.alliance.Length) return;

                    int allianceId = bunits.alliance[sideofai];

                    // Player protection: don't override the player's side macro AI
                    // when ai_vs_ai is off. Player-CIC is already suppressed in the
                    // coordinator; this is defense in depth.
                    if (allianceId == GameVars.playeralliance && !GameVars.ai_vs_ai) return;

                    var orchestrator = TacticalBattleCoordinator.GetSideOrchestrator(allianceId);
                    if (orchestrator == null || orchestrator.Army == null) return;
                    if (!orchestrator.Army.HasPlan) return;

                    var opportunity = orchestrator.Army.CurrentReinforcementOpportunity;
                    float aggression01 = (orchestrator.Army.CommanderPersonality.Aggression + 1f) * 0.5f;
                    if (aggression01 < 0f) aggression01 = 0f;
                    if (aggression01 > 1f) aggression01 = 1f;

                    int? mapped = TacticalVanillaMacroAiMapping.MapOpportunityToMacroAi(
                        opportunity.Opportunity, opportunity.CurrentRatio, aggression01);
                    if (!mapped.HasValue) return;
                    int newMacroAi = mapped.Value;
                    if (!TacticalVanillaMacroAiMapping.IsLegalMacroAi(newMacroAi)) return;

                    int currentMacroAi = bunits.sideinformation[sideofai].macroai;
                    if (currentMacroAi == newMacroAi) return; // skip-if-correct

                    bunits.sideinformation[sideofai].macroai = newMacroAi;
                    OnceLog.Info(
                        "tactical-macroai-override-wired",
                        "BattleMacroAIOverridePatch wired");

                    EmitWriteTelemetry(sideofai, allianceId, currentMacroAi, newMacroAi, opportunity);
                }
                catch (Exception ex)
                {
                    try { Plugin.Log?.LogWarning("[macroai-override] " + ex.Message); } catch { }
                }
            }
        }

        private static int ReadSideOfAi(AIBattle battle)
        {
            try
            {
                if (_sideofaiField == null) _sideofaiField = AccessTools.Field(typeof(AIBattle), "sideofai");
                if (_sideofaiField == null) { LogMissingAnchor("sideofai"); return -1; }
                object v = _sideofaiField.GetValue(battle);
                if (v is int i) return i;
                LogMissingAnchor("sideofai:value");
                return -1;
            }
            catch { return -1; }
        }

        private static BattleUnits ReadBattleUnits(AIBattle battle)
        {
            try
            {
                if (_bunitsField == null) _bunitsField = AccessTools.Field(typeof(AIBattle), "bunits");
                if (_bunitsField == null) { LogMissingAnchor("bunits"); return null; }
                var v = _bunitsField.GetValue(battle) as BattleUnits;
                if (v == null) LogMissingAnchor("bunits:value");
                return v;
            }
            catch { return null; }
        }

        private static void LogMissingAnchor(string anchor)
        {
            if (_missingAnchorLogged) return;
            _missingAnchorLogged = true;
            try { Plugin.Log?.LogWarning("[macroai-override] missing required anchor: " + anchor); } catch { }
        }

        private static void EmitWriteTelemetry(
            int sideofai, int allianceId, int previous, int current,
            ReinforcementOpportunityDecision opportunity)
        {
            try
            {
                string sig = "side=" + sideofai + "|prev=" + previous + "|curr=" + current
                    + "|opp=" + opportunity.Opportunity;
                float nowSeconds = System.Environment.TickCount * 0.001f;
                string key = "macroai-override:" + sideofai;
                lock (TelemetryGate)
                {
                    if (!WhiskeyRealism.Tactical.TacticalTelemetry.ShouldEmit(
                            _lastWriteTelemetryAt, key, sig, nowSeconds,
                            WriteTelemetryThrottleSeconds, false))
                        return;
                }

                TelemetryRouter.Emit(
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Write,
                    "TacticalMacroAiOverride",
                    TelemetrySeverity.Info,
                    ev => ev
                        .WithDecision("TacticalMacroAiOverride", "summary", sig)
                        .WithField("side", sideofai)
                        .WithField("alliance", allianceId)
                        .WithField("previousMacroAi", previous)
                        .WithField("newMacroAi", current)
                        .WithField("opportunity", opportunity.Opportunity.ToString())
                        .WithField("currentRatio", opportunity.CurrentRatio)
                        .WithField("attackThreshold", opportunity.AttackThreshold));
            }
            catch { }
        }
    }
}
