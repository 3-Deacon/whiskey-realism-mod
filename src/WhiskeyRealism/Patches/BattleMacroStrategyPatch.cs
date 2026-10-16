using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
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
    // in saves.
    // Deliberate exception to the Postfix-only rule: a non-blocking, void,
    // value-restoring Prefix (it never skips vanilla). Shadow
    // (BattleWriterPolicy.MacroAiRestoreBeforeVanilla): when the Postfix
    // overwrites macroai it records vanilla's own value per AIBattle; the
    // Prefix puts that value back before vanilla runs, unless something else
    // wrote macroai in between (e.g. the debug panel, 189257). Without it:
    //   6388  newmacroaitoset != macroai && timer expired reverts Whiskey's
    //         1/2 every cycle, logging through the ungated Debug.Log in
    //         GameVars.DebugOwnLog (66964);
    //   6438  newmacroaitoset >= 0 && macroai <= 0 re-stamps the timer every
    //         cycle, so a Whiskey 0 would lock the side in assault.
    // With it vanilla's timers only see vanilla state, and when Whiskey stops
    // having an opinion vanilla's own value simply stays (handed back once).
    // The Prefix runs whatever the flags/mode, so turning Whiskey off hands
    // back. Saved values are keyed weakly per AIBattle instance and dropped
    // at battle end (ClearShadows, registered with BattleScope).
    // The former BattleMacroAIOverridePatch wrote the persisted
    // sideinformation.macroai and is gone; its reinforcement-opportunity opinion
    // is consumed here as the fallback when the operations ledger has none.
    // Deliberately no reset of sideinformation.macroai: a mid-battle save made
    // with a pre-T3 build can carry a Whiskey-written value (saved 74819,
    // loaded 74689), but it is indistinguishable from a genuine scripted value
    // (scenario file 88254, honoured as an override at 6383). Such a save
    // keeps that side on the stale value and this patch defers to it; only
    // mid-battle dev saves from pre-T3 builds are affected.
    // Battle core (BC5b): the legacy scorer's enemy pressure (sector and
    // inferred strength) is the battle snapshot's slice pressure
    // (BattleWriterPolicy.SnapshotPressure), no longer vanilla's
    // enemystrengthwithinangle over true enemy positions (122719-122733), and
    // its in-range enemy strength is BattleWriterPolicy.SnapshotEnemyStrengthInRange
    // (perceived enemies within vanilla's line-of-sight reach) instead of
    // groupenemiesinrange (120558-120563, not fog-filtered for the AI side), and
    // its flank risk is BattleWriterPolicy.PerceivedFlankRisk (perceived flank
    // pressure) instead of flanksthreated / outflanked (120566-120580 over the
    // unfiltered range lists, 122650, 122976+).
    // Troop quality (BC8): the orchestrator opinion's force ratio is vanilla's force
    // balance (84862, TacticalForceBalanceEvidence) with the initiative-divided attack
    // threshold (6396-6430); the legacy scorer's odds and sectors are weighed by vanilla's
    // experience blend (GetGroupStrength 6057; TroopQuality.SideBlends / SectorBlends).
    // W&L player side (docs/wl-player-model.md rule 4): its macroai produces
    // dispatches to the player, so only a planned-operation opinion that
    // differs from vanilla's current and pending (newmacroaitoset) value is
    // written there, never the fallback (doctrine or legacy scorer), and
    // nothing for a CIC player.
    [HarmonyPatch(typeof(AIBattle), "CheckGlobalAIStrategy")]
    internal static class BattleMacroStrategyPatch
    {
        static BattleMacroStrategyPatch()
        {
            BattleScope.Register("macro-shadows", ClearShadows);
        }

        private static readonly Dictionary<string, float> _lastLoggedAt = new Dictionary<string, float>();
        private static ConditionalWeakTable<AIBattle, MacroShadow> _shadows = new ConditionalWeakTable<AIBattle, MacroShadow>();
        private static FieldInfo _bunitsField;
        private static FieldInfo _unitsUsedField;
        private static FieldInfo _receivedFireField;

        // Battle-end reset of the per-AIBattle saved vanilla values.
        internal static void ClearShadows()
        {
            _shadows = new ConditionalWeakTable<AIBattle, MacroShadow>();
        }

        private sealed class MacroShadow
        {
            public int Written;
            public int VanillaMacro;
        }

        [HarmonyPrefix]
        internal static void Prefix(AIBattle __instance)
        {
            using (TelemetryPerf.Scope("tactical.patch.battle-macro-strategy.prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.battle-macro-strategy.prefix");
                try
                {
                    if (__instance == null || !_shadows.TryGetValue(__instance, out MacroShadow shadow)) return;
                    _shadows.Remove(__instance);
                    int restore = BattleWriterPolicy.MacroAiRestoreBeforeVanilla(true, shadow.Written, shadow.VanillaMacro, __instance.macroai);
                    if (restore != BattleWriterPolicy.NoMacroWrite) __instance.macroai = restore;
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("tactical-macro:restore-failed", "Tactical macro shadow restore failed: " + ex.Message);
                }
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(AIBattle __instance)
        {
            using (TelemetryPerf.Scope("tactical.patch.battle-macro-strategy.postfix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.battle-macro-strategy.postfix");
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
            int ledgerOpinion = -1;
            int fallbackOpinion;
            string source;
            if (Plugin.EnableTacticalOrchestratorArmy != null && Plugin.EnableTacticalOrchestratorArmy.Value)
            {
                // A null side orchestrator is a suppressed (player-CIC) or not yet
                // bootstrapped side: no opinion, never the legacy scorer.
                if (allianceId < 0 || allianceId >= 2) return;
                var army = TacticalBattleCoordinator.GetSideOrchestrator(allianceId)?.Army;
                if (army == null) return;
                ledgerOpinion = OrchestratorMacroOpinion(army);
                fallbackOpinion = DoctrineMacroOpinion(army);
                source = "orchestrator";
            }
            else
            {
                // The legacy scorer is not a planned operation: it may fill the
                // AI side's fallback slot but never reaches the W&L player side.
                fallbackOpinion = ScorerMacroOpinion(bunits, side, vanillaMacro, scriptedAiStrategy, scriptedSideMacro, endBattle, battle);
                source = "scorer";
            }

            if (ledgerOpinion == 3 || fallbackOpinion == 3)
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
                fallbackOpinion,
                IsWlPlayerSide(battle, allianceId),
                WlPlayerIsCic(),
                battle.newmacroaitoset));
            if (write == BattleWriterPolicy.NoMacroWrite) return;

            battle.macroai = write;
            _shadows.Remove(battle);
            _shadows.Add(battle, new MacroShadow { Written = write, VanillaMacro = vanillaMacro });
            LogWrite(allianceId, vanillaMacro, write, ledgerOpinion, fallbackOpinion, source);
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
            BattleCore.TryGetSnapshot(ResolveAllianceId(bunits, side), out BattleSnapshot snapshot);
            var odds = BuildRuntimeOdds(bunits, side, units, snapshot);
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

        private static void LogWrite(int allianceId, int previous, int written, int ledger, int fallback, string source)
        {
            string signature = allianceId + "|" + previous + "|" + written + "|" + source;
            if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "macro-write", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            TelemetryRouter.LegacyInfo("[TacticalMacroDecision] side=" + allianceId
                + " old=" + TacticalTelemetry.MacroName(previous)
                + " whiskey=" + TacticalTelemetry.MacroName(written)
                + " ledger=" + ledger
                + " fallback=" + fallback
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

        // Enemy terms come from the battle snapshot (fog-honest): visible strength
        // and per-group slice pressure. Own terms stay vanilla's own side
        // information — totalactiveforce and reinforcementarrivalswithin24hrs
        // (the side's OWN arrivals, read by vanilla for its own retreat test at
        // 6414) — which the side legitimately knows.
        private static TacticalOddsAssessment BuildRuntimeOdds(BattleUnits bunits, int side, IList units, BattleSnapshot snapshot)
        {
            float own = Math.Max(1f, SideInfoTotalActiveForce(bunits, side));
            float confirmedEnemy = EstimateVisibleEnemyStrength(units);
            float inferredEnemy = EstimateInferredEnemyStrength(units, snapshot);
            float reinforcements = SideInfoReinforcementsWithin24Hours(bunits, side);
            var contact = TacticalContactLedger.Classify(new TacticalContactInput(
                confirmedEnemy,
                confirmedEnemy,
                inferredEnemy,
                confirmedEnemy > 0f ? 0f : 9999f,
                AnyReceivedFire(units),
                confirmedEnemy <= 0f));

            float ownBlend = 1f, enemyBlend = 1f;
            if (snapshot != null)
                TroopQuality.SideBlends(TroopQuality.OwnQuality(snapshot.Own, snapshot.OwnCount), snapshot.PerceivedEnemyQuality,
                    TroopQualityRuntime.Prefs.ExpInfluence, TroopQualityRuntime.Prefs.MoraleInfluence, out ownBlend, out enemyBlend);

            return TacticalOddsDoctrine.Evaluate(new TacticalOddsInput(
                own,
                confirmedEnemy,
                confirmedEnemy,
                inferredEnemy,
                reinforcements,
                0f,
                contact,
                BuildSectorAssessments(units, snapshot),
                ownBlend,
                enemyBlend));
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

        private static TacticalSectorAssessment[] BuildSectorAssessments(IList units, BattleSnapshot snapshot)
        {
            if (units == null) return Array.Empty<TacticalSectorAssessment>();

            var sectors = new List<TacticalSectorAssessment>();
            int sectorId = 0;
            float sightRange = OrderExecutorRuntime.GroupRangeReach();
            if (snapshot != null)
            {
                if (_flankThreatMean.Length < snapshot.OwnCount)
                {
                    _flankThreatMean = new float[snapshot.OwnCount * 2];
                    _flankThreatCount = new int[snapshot.OwnCount * 2];
                }
                ArmyEvidenceBuilder.MeanFlankThreatByGroup(snapshot, _flankThreatMean, _flankThreatCount);
            }
            for (int i = 0; i < units.Count; i++)
            {
                var group = units[i] as Regiment;
                if (group == null || group.unittyp <= 13) continue;

                int index = OwnIndex(snapshot, group);
                Regiment closest = TacticalFogOfWarContact.ClosestVisibleEnemy(group);
                TroopQuality.SectorBlends(snapshot, index, TroopQualityRuntime.Prefs.ExpInfluence, TroopQualityRuntime.Prefs.MoraleInfluence, out float ownBlend, out float enemyBlend);
                sectors.Add(TacticalGroupSectorEstimator.BuildSector(new TacticalGroupContactInput(
                    sectorId++,
                    Math.Max(group.groupowninrange, group.groupstrengthaigroup),
                    BattleWriterPolicy.SnapshotEnemyStrengthInRange(snapshot, index, sightRange),
                    BattleWriterPolicy.SnapshotPressure(snapshot, index),
                    closest != null ? closest.strength : 0f,
                    closest != null ? closest.unittyp : -1,
                    closest != null ? SafeName(closest) : string.Empty,
                    closest != null && closest.isrouted,
                    closest != null && closest.permanentlydetached,
                    BattleWriterPolicy.PerceivedFlankRisk(snapshot, index, _flankThreatMean),
                    group.covervalue > 0.5f || group.fortinrange,
                    ownBlend,
                    enemyBlend)));
            }

            return sectors.ToArray();
        }

        private static float EstimateVisibleEnemyStrength(IList units)
        {
            return TacticalFogOfWarContact.VisibleEnemyStrength(units);
        }

        private static float EstimateInferredEnemyStrength(IList units, BattleSnapshot snapshot)
        {
            if (units == null || snapshot == null) return 0f;

            float total = 0f;
            for (int i = 0; i < units.Count; i++)
                total += BattleWriterPolicy.SnapshotPressure(snapshot, OwnIndex(snapshot, units[i] as Regiment));
            return total;
        }

        // Per own record: mean vanilla outflanked weight of its regiments over the
        // snapshot's flank pressure (grow-only scratch, main thread).
        private static float[] _flankThreatMean = new float[64];
        private static int[] _flankThreatCount = new int[64];

        // The unit's record in the side's snapshot (keyed by GameObject id), or -1.
        private static int OwnIndex(BattleSnapshot snapshot, Regiment unit)
        {
            if (snapshot == null || (UnityEngine.Object)unit == null) return -1;
            GameObject go = ((Component)unit).gameObject;
            return go != null ? snapshot.IndexOfOwn(go.GetInstanceID()) : -1;
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

        // The army-off fallback's aggression. With Enable Per-Commander Personality on it is the side's army
        // commander's table entry (0.5 without one), so army-on and army-off agree (BC13
        // requirement-army-vector-from-table); off, the commander's live initiative as before.
        private static float CommanderAggression01(BattleUnits bunits, int side)
        {
            try
            {
                if (bunits == null || side < 0 || side >= bunits.alliance.Length) return 0.5f;
                int commanderId = bunits.GetCommandingOfficerFromSide(side);
                if (Plugin.PerCommanderPersonalityEnabled())
                {
                    CommanderPersonalityTable table = CommanderPersonalityRuntime.TableFor(bunits.alliance[side]);
                    return table != null && table.TryGet(commanderId, out PersonalityVector vector, out _)
                        ? CommanderPersonalityDerivation.To01(vector.Aggression)
                        : 0.5f;
                }
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
