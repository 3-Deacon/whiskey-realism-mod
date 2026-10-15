using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // #44 — single owner of the AIBattle instance macroai (WS T3 / R2, BR-C1,
    // BR-H2, T10). Postfix on AIBattle.CheckGlobalAIStrategy (decompile 6314).
    //
    // Vanilla anchors (build 14703111):
    //   6339-6359  victory-bar retreat: vanilla stamps startoffallback only on
    //              its own transition to 3 (6355);
    //   6362       macroai == 3 ends the battle (SetEndOfBattle 8/12) once
    //              currenttimefromstart >= startoffallback + timetostartairetreattimer
    //              -> a Whiskey-written 3 with startoffallback == 0 ends the battle
    //              on the next cycle, so Whiskey never writes 3;
    //   6374       macroai == 3 returns before anything else;
    //   6378/6383  GameVars.aistrategy >= 0 / sideinformation[side].macroai >= 0
    //              are scripted overrides -> no Whiskey opinion;
    //   5857       UpdateAITasks runs CheckGlobalAIStrategy then AdjustGroupAIStance,
    //              so the value written here drives this cycle's group stances.
    // The instance macroai is not persisted (only sideinformation.macroai is,
    // sideinfo.dat 74819/74689), so re-asserting it each cycle leaves no trace
    // in saves. The former BattleMacroAIOverridePatch wrote the persisted
    // sideinformation.macroai and is gone; its reinforcement-opportunity opinion
    // is consumed here as the fallback when the operations ledger has none.
    // Deliberately no reset of sideinformation.macroai: a mid-battle save made
    // with a pre-T3 build can carry a Whiskey-written value (saved 74819,
    // loaded 74689), but it is indistinguishable from a genuine scripted value
    // (scenario file 88254, honoured as an override at 6383). Such a save
    // keeps that side on the stale value and this patch defers to it; only
    // mid-battle dev saves from pre-T3 builds are affected.
    // W&L player side (docs/wl-player-model.md rule 4): its macroai produces
    // dispatches to the player, so only a planned-operation opinion that
    // differs from vanilla's current and pending (newmacroaitoset) value is
    // written there, never the doctrine fallback, and nothing for a CIC player.
    [HarmonyPatch(typeof(AIBattle), "CheckGlobalAIStrategy")]
    internal static class BattleMacroStrategyPatch
    {
        private static readonly Dictionary<string, float> _lastLoggedAt = new Dictionary<string, float>();
        private static FieldInfo _bunitsField;
        private static FieldInfo _unitsUsedField;
        private static FieldInfo _receivedFireField;

        [HarmonyPostfix]
        internal static void Postfix(AIBattle __instance)
        {
            using (TelemetryPerf.Scope("tactical.patch.battle-macro-strategy", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
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
        }

        private static void Apply(AIBattle battle)
        {
            int side = battle.sideofai;
            int vanillaMacro = battle.macroai;
            var bunits = SafeField<BattleUnits>(battle, ref _bunitsField, "bunits");
            if (side < 0 || side > 1 || bunits == null) return;

            bool modeAllowsWrites = TacticalWriteGuard.ModeAllowsWrites();
            bool feudInstance = TacticalWriteGuard.IsFeudInstance(battle);
            bool scriptedAiStrategy = GameVars.aistrategy >= 0;
            bool scriptedSideMacro = SideInfoMacro(bunits, side) >= 0;
            bool endBattle = EndBattleActive(bunits);
            if (!BattleWriterPolicy.MacroAiMayHaveOpinion(
                    modeAllowsWrites, feudInstance, scriptedAiStrategy, scriptedSideMacro, endBattle, vanillaMacro))
                return;

            int allianceId = ResolveAllianceId(bunits, side);
            int ledgerOpinion;
            int doctrineOpinion = -1;
            string source;
            if (Plugin.EnableTacticalOrchestratorArmy != null && Plugin.EnableTacticalOrchestratorArmy.Value)
            {
                // A null side orchestrator is a suppressed (player-CIC) or not yet
                // bootstrapped side: no opinion, never the legacy scorer.
                if (allianceId < 0 || allianceId >= 2) return;
                var army = TacticalBattleCoordinator.GetSideOrchestrator(allianceId)?.Army;
                if (army == null) return;
                ledgerOpinion = OrchestratorMacroOpinion(army);
                doctrineOpinion = DoctrineMacroOpinion(army);
                source = "orchestrator";
            }
            else
            {
                ledgerOpinion = ScorerMacroOpinion(bunits, side, vanillaMacro, scriptedAiStrategy, scriptedSideMacro, endBattle, battle);
                source = "scorer";
            }

            if (ledgerOpinion == 3 || doctrineOpinion == 3)
                OnceLog.Info("tactical-macro:retreat-opinion-ignored",
                    "[TacticalMacroDecision] retreat (3) opinion ignored; vanilla owns army retreat (CheckGlobalAIStrategy 6355/6362).");

            int write = BattleWriterPolicy.ResolveMacroAiWrite(new MacroAiWriteInput(
                modeAllowsWrites,
                feudInstance,
                scriptedAiStrategy,
                scriptedSideMacro,
                endBattle,
                vanillaMacro,
                ledgerOpinion,
                doctrineOpinion,
                IsWlPlayerSide(battle, allianceId),
                WlPlayerIsCic(),
                battle.newmacroaitoset));
            if (write == BattleWriterPolicy.NoMacroWrite) return;

            battle.macroai = write;
            LogWrite(allianceId, vanillaMacro, write, ledgerOpinion, doctrineOpinion, source);
        }

        // W&L forces ai_vs_ai (29789), so aibattle[1] (isplayeraiorfeud == 1,
        // SetSide 3408) runs the player side's macro AI.
        private static bool IsWlPlayerSide(AIBattle battle, int allianceId)
        {
            return DLC_WL.dlc_scenarioactive &&
                (battle.isplayeraiorfeud == 1 || allianceId == GameVars.playeralliance);
        }

        private static bool WlPlayerIsCic()
        {
            try { return DLC_WL.dlc_scenarioactive && DLC_WL.IsCommanderInChief(); }
            catch { return true; }
        }

        // Operations-ledger opinion; an army in Commander Mode Off has none
        // (the plugin-level mode gate already stops writes in Off/MonitorOnly).
        private static int OrchestratorMacroOpinion(ArmyOrchestrator army)
        {
            if (army.CommanderMode == TacticalCommanderMode.Off) return -1;
            return TacticalOperationsLedgerModel.OperationMacroAi(army.CurrentOperation);
        }

        // Former BattleMacroAIOverridePatch opinion (Enable Vanilla Macro AI Sync).
        private static int DoctrineMacroOpinion(ArmyOrchestrator army)
        {
            if (Plugin.EnableVanillaMacroAiSync == null || !Plugin.EnableVanillaMacroAiSync.Value) return -1;
            if (!army.HasPlan) return -1;

            var opportunity = army.CurrentReinforcementOpportunity;
            float aggression01 = (army.CommanderPersonality.Aggression + 1f) * 0.5f;
            if (aggression01 < 0f) aggression01 = 0f;
            if (aggression01 > 1f) aggression01 = 1f;
            int? mapped = TacticalVanillaMacroAiMapping.MapOpportunityToMacroAi(
                opportunity.Opportunity, opportunity.CurrentRatio, aggression01);
            return mapped.HasValue ? mapped.Value : -1;
        }

        private static int ScorerMacroOpinion(
            BattleUnits bunits,
            int side,
            int vanillaMacro,
            bool scriptedAiStrategy,
            bool scriptedSideMacro,
            bool endBattle,
            AIBattle battle)
        {
            var units = SafeList(battle, ref _unitsUsedField, "unitsused");
            var odds = BuildRuntimeOdds(bunits, side, units);
            var decision = TacticalDoctrineScorer.DecideMacro(new TacticalMacroDecisionInput(
                vanillaMacro,
                scriptedAiStrategy,
                scriptedSideMacro,
                vanillaMacro == 3 || endBattle,
                CommanderAggression01(bunits, side),
                odds));
            if (decision.Kind != TacticalDoctrineDecisionKind.Apply) return -1;
            LogDecision(side, vanillaMacro, decision, odds);
            return decision.MacroAi;
        }

        private static void LogWrite(int allianceId, int previous, int written, int ledger, int doctrine, string source)
        {
            string signature = allianceId + "|" + previous + "|" + written + "|" + source;
            if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "macro-write", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            TelemetryRouter.LegacyInfo("[TacticalMacroDecision] side=" + allianceId
                + " old=" + TacticalTelemetry.MacroName(previous)
                + " whiskey=" + TacticalTelemetry.MacroName(written)
                + " ledger=" + ledger
                + " doctrine=" + doctrine
                + " source=" + source, TelemetryLayer.Tactical);
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
            float own = Math.Max(1f, SideInfoTotalActiveForce(bunits, side));
            float confirmedEnemy = EstimateVisibleEnemyStrength(units);
            float inferredEnemy = EstimateInferredEnemyStrength(units);
            float reinforcements = SideInfoReinforcementsWithin24Hours(bunits, side);
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

            TelemetryRouter.LegacyInfo("[TacticalMacroDecision] side=" + side +
                " old=" + TacticalTelemetry.MacroName(vanillaMacro) +
                " whiskey=" + TacticalTelemetry.MacroName(decision.MacroAi) +
                " reason=" + decision.Reason +
                " current=" + odds.CurrentGlobalOdds.ToString("0.00") +
                " projected=" + odds.ProjectedGlobalOdds.ToString("0.00") +
                " confidence=" + odds.Confidence.ToString("0.00"), TelemetryLayer.Tactical);
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableTacticalMacroStanceScorer.Value;
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

        private static float SideInfoTotalActiveForce(BattleUnits bunits, int side)
        {
            try
            {
                if (bunits == null || bunits.sideinformation == null) return 0f;
                if (side < 0 || side >= bunits.sideinformation.Length) return 0f;
                return bunits.sideinformation[side].totalactiveforce;
            }
            catch
            {
                return 0f;
            }
        }

        private static float SideInfoReinforcementsWithin24Hours(BattleUnits bunits, int side)
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
