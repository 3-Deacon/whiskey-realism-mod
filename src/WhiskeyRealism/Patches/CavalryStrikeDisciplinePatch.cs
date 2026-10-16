using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Battle.BugFixes;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // #120 CavalryStrikeDisciplinePatch (BUG-TAC-030) - Vanilla AIBattle.AdjustCavalryBehavior (5036-5056, micro cycle 7) assigns a
    // cavalry regiment's charge from Regiment.GetClosestEnemyUnitReg(maxdistancecavalryattack) (5049): the closest enemy unit in
    // the regiment's range list, artillery or with morale under maxenemymoraleforcavalrychargenonarty, not routed (5050).
    // That list is the true battle state: a regiment out of sight, in square or in a fort, artillery with a regiment standing
    // beside it, or a target the charger's odds do not favour is charged all the same, and the charger's own outflanking
    // (nomicroaichargeifunitisoutflanked, 54784) is never read.
    //
    // A result-override steer (the SkirmisherScreenDistancePatch precedent), no skip and no Transpiler:
    //   - a non-skipping Prefix, a Priority.First Postfix and a Finalizer on AdjustCavalryBehavior set and clear a flag naming the
    //     AIBattle (the only GetClosestEnemyUnitReg call in the method is 5049, pinned FIX-CAV-PIN);
    //   - a Postfix on GetClosestEnemyUnitReg(float, int[]) acts only while the flag is set, for an AI cavalry regiment of a side
    //     that DrivesOrchestrator, in Commander Mode Active (TacticalWriteGuard.CanWrite), and keeps vanilla's pick only when
    //     CavalryStrikeCheck passes over what the side perceives. A live exploit's cavalry target within vanilla's reach
    //     replaces a failing pick; otherwise the result becomes null and vanilla's caller (5050) skips the charge this cycle.
    // Fail-open: no snapshot, perception off, the charger missing from the snapshot or any exception keeps vanilla's result.
    // Whiskey never calls AssignChargeTarget; vanilla's own cycle 7 does the charging.
    [HarmonyPatch(typeof(AIBattle), "AdjustCavalryBehavior")]
    internal static class CavalryStrikeDisciplinePatch
    {
        // The AIBattle whose AdjustCavalryBehavior is running; null outside it. Main thread only.
        private static AIBattle _inside;

        internal static AIBattle Inside => _inside;

        static CavalryStrikeDisciplinePatch()
        {
            BattleScope.Register("cavalry-strike-discipline", () => _inside = null);
        }

        [HarmonyPrefix]
        internal static void Prefix(AIBattle __instance)
        {
            using (TelemetryPerf.Scope("tactical.patch.cavalry-strike.adjust-prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.cavalry-strike.adjust-prefix");
                _inside = __instance;
            }
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        internal static void Postfix()
        {
            using (TelemetryPerf.Scope("tactical.patch.cavalry-strike.adjust-postfix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.cavalry-strike.adjust-postfix");
                _inside = null;
            }
        }

        [HarmonyFinalizer]
        internal static void Finalizer()
        {
            using (TelemetryPerf.Scope("tactical.patch.cavalry-strike.adjust-finalizer", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.cavalry-strike.adjust-finalizer");
                _inside = null;
            }
        }
    }

    [HarmonyPatch(typeof(Regiment), "GetClosestEnemyUnitReg", new[] { typeof(float), typeof(int[]) })]
    internal static class CavalryStrikeTargetPatch
    {
        private const double RowIntervalHours = 0.05;
        private static readonly Dictionary<int, double> _lastRow = new Dictionary<int, double>(64);

        static CavalryStrikeTargetPatch()
        {
            BattleScope.Register("cavalry-strike-rows", _lastRow.Clear);
        }

        [HarmonyPostfix]
        internal static void Postfix(Regiment __instance, ref Regiment __result)
        {
            using (TelemetryPerf.Scope("tactical.patch.cavalry-strike.target-postfix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.cavalry-strike.target-postfix");
                AIBattle battle = CavalryStrikeDisciplinePatch.Inside;
                if (battle == null || (UnityEngine.Object)__result == null) return;
                try
                {
                    CavalryStrikeScope scope = Facts(battle, __instance, out BattleSnapshot snapshot, out int charger);
                    if (!CavalryStrikeGate.Applies(in scope)) return;

                    Regiment vanilla = __result;
                    bool allowed = Evaluate(snapshot, charger, __instance, vanilla, out string reason);
                    if (allowed)
                    {
                        Row(__instance, snapshot, "allowed", null);
                        return;
                    }
                    Regiment substitute = ExploitSubstitute(snapshot, charger, __instance, out string substituteReason);
                    if (substitute != null)
                    {
                        __result = substitute;
                        Row(__instance, snapshot, "allowed", substituteReason);
                        return;
                    }
                    __result = null;
                    Row(__instance, snapshot, "refused", reason);
                    if (!OnceLog.AlreadyLogged("tactical-battle-core:cavalry-strike-first-refusal"))
                        OnceLog.Info("tactical-battle-core:cavalry-strike-first-refusal", "[CavalryStrike] first refused: reason=" + reason);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("tactical-battle-core:cavalry-strike-failed",
                        "[CavalryStrike] discipline check failed; vanilla's pick kept: " + ex.GetType().Name + " " + ex.Message);
                }
            }
        }

        // The runtime facts the pure gate decides on; each failed term stops the reading (the rest stay false), so a call outside the
        // discipline costs a few field reads.
        private static CavalryStrikeScope Facts(AIBattle battle, Regiment unit, out BattleSnapshot snapshot, out int charger)
        {
            snapshot = null;
            charger = -1;
            var scope = new CavalryStrikeScope { InsideAdjustCavalry = battle != null, HasVanillaResult = true };
            scope.FlagOn = BattleFixGate.Runs(BattleFixKind.CavalryStrike, Plugin.MasterEnabled(), Plugin.CavalryStrikeDisciplineEnabled(),
                TacticalWriteGuard.ModeAllowsWrites());
            if (!scope.FlagOn || (UnityEngine.Object)unit == null) return scope;
            scope.ChargerIsCavalry = unit.unittyp == TacticalUnitType.Cavalry;
            if (!scope.ChargerIsCavalry) return scope;
            scope.SideDrivenByOrchestrator = TacticalBattleCoordinator.DrivesOrchestrator(battle.isplayeraiorfeud);
            scope.FeudInstance = TacticalWriteGuard.IsFeudInstance(battle);
            if (!scope.SideDrivenByOrchestrator || scope.FeudInstance) return scope;
            scope.CanWrite = TacticalWriteGuard.CanWrite(unit);
            scope.FogHonestPerceptionOn = Plugin.EnableBattleFogHonestPerception != null && Plugin.EnableBattleFogHonestPerception.Value;
            if (!scope.CanWrite || !scope.FogHonestPerceptionOn) return scope;
            scope.HasSnapshot = BattleCore.TryGetSnapshot(unit.alliance, out snapshot) && snapshot != null;
            if (!scope.HasSnapshot) return scope;
            charger = snapshot.IndexOfOwn(unit.gameObject.GetInstanceID());
            scope.ChargerInSnapshot = charger >= 0;
            return scope;
        }

        // The check over what the side perceives of the target (its Visible observation) and the charger's own record.
        private static bool Evaluate(BattleSnapshot s, int charger, Regiment unit, Regiment target, out string reason)
        {
            int targetId = target.gameObject.GetInstanceID();
            EnemyObservation[] obs = s.Observations;
            int found = -1;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (obs[k].InstanceId != targetId) continue;
                found = k;
                break;
            }
            var input = new CavalryStrikeInput { ShakenMorale = GamePrefs.maxenemymoraleforcavalrychargenonarty };
            if (found >= 0 && CavalryStrikeCheck.TargetSeen(true, obs[found].State))
            {
                input.TargetVisible = true;
                input.TargetUnitTyp = obs[found].UnitTyp;
                input.TargetMorale = obs[found].Morale;
                input.TargetGarrisoned = target.garrisonreference != null;
                input.TargetInSquare = BattleRecon.IsSquare(target.unittyp, target.formation);
                if (input.TargetUnitTyp == TacticalUnitType.Artillery) input.TargetSupported = Supported(s, found);
                else FillOdds(ref input, in s.Own[charger], unit, in obs[found], target);
            }
            input.ChargerBarredByOutflank = GamePrefs.nomicroaichargeifunitisoutflanked && s.Own[charger].Outflanked != 0;
            return CavalryStrikeCheck.Allows(in input, out reason);
        }

        // An infantry or cavalry sample of the target's side within CavalryStrikeCheck.SupportMeters of the artillery (requirement-detect-unsupported-battery's test).
        private static bool Supported(BattleSnapshot s, int battery)
        {
            EnemyObservation[] obs = s.Observations;
            float r2 = CavalryStrikeCheck.SupportMeters * CavalryStrikeCheck.SupportMeters;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (k == battery || !EnemySamples.Accepts(in obs[k])) continue;
                if (obs[k].UnitTyp != TacticalUnitType.Infantry && obs[k].UnitTyp != TacticalUnitType.Cavalry) continue;
                float dx = obs[k].X - obs[battery].X, dz = obs[k].Z - obs[battery].Z;
                if (dx * dx + dz * dz <= r2) return true;
            }
            return false;
        }

        // Vanilla's UpdateChargeTarget odds (122229-122234): the charger's own record and its mounted weapon's meleemounted factor
        // over the target's observed strength, experience and morale and its visible fatigue.
        private static void FillOdds(ref CavalryStrikeInput input, in UnitRecord own, Regiment unit, in EnemyObservation target, Regiment targetUnit)
        {
            ChargeOddsRules odds = TroopQualityRuntime.Prefs.ChargeOdds;
            input.OddsTrigger = odds.Trigger;
            if (!(odds.Trigger > 0f)) return;
            float ratio = TroopQuality.VanillaChargeOdds(own.Strength, own.Experience, own.Morale, own.Fatigue,
                target.Strength, target.Experience, target.Morale, targetUnit.fatigue, odds);
            if (unit.mounted > 0 && unit.weapon >= 0) ratio *= GameVars.weapon[unit.weapon].meleemounted;
            input.Odds = ratio;
        }

        // A live exploit whose asset holds this regiment: its cavalry target within vanilla's own reach, if the regiment's range
        // list holds it and it passes both the check and vanilla's 5050 filter.
        private static Regiment ExploitSubstitute(BattleSnapshot s, int charger, Regiment unit, out string reason)
        {
            reason = null;
            ExploitTable table = ExploitTable.ForAlliance(s.Alliance);
            for (int i = 0; i < table.Count; i++)
            {
                ref ExploitEntry e = ref table.At(i);
                if (e.Phase == ExploitPhase.None || e.Phase == ExploitPhase.Recall || e.TargetObservation < 0 || !e.Cavalry) continue;
                int asset = s.IndexOfOwn(e.AssetId);
                if (asset < 0 || (asset != charger && !s.IsAncestorOf(asset, charger))) continue;

                Regiment[] candidates = unit.unitrange.closestenemyunit;
                if (candidates == null) continue;
                for (int c = 0; c < candidates.Length; c++)
                {
                    Regiment candidate = candidates[c];
                    if ((UnityEngine.Object)candidate == null || candidate.gameObject.GetInstanceID() != e.TargetObservation) continue;
                    // What the side perceives first; the live candidate's own fields (reach, vanilla's 5050 filter) only after it passes.
                    if (!Evaluate(s, charger, unit, candidate, out _)) continue;
                    float dx = candidate.GetPosition().x - unit.GetPosition().x, dz = candidate.GetPosition().z - unit.GetPosition().z;
                    if (!CavalryStrikeCheck.SubstituteInReach((float)Math.Sqrt(dx * dx + dz * dz), GamePrefs.maxdistancecavalryattack)) continue;
                    // vanilla's own 5050 filter
                    bool vanillaOk = (candidate.unittyp == TacticalUnitType.Artillery ||
                        (candidate.unittyp <= TacticalUnitType.MaxCombat && candidate.morale < GamePrefs.maxenemymoraleforcavalrychargenonarty)) && !candidate.isrouted;
                    if (!vanillaOk) continue;
                    reason = CavalryStrikeCheck.ExploitTarget;
                    return candidate;
                }
            }
            return null;
        }

        // One TacticalCavalryStrike row per regiment per 0.05 doctrine-h (a state-change-free throttle on the doctrine clock).
        private static void Row(Regiment unit, BattleSnapshot s, string outcome, string reason)
        {
            if (!TelemetryRouter.ShouldEmit(TelemetryRouter.CurrentProfile, TelemetryLayer.Tactical, TelemetryCategory.Decision)) return;
            int id = unit.gameObject.GetInstanceID();
            double now = s.DoctrineHours;
            if (_lastRow.TryGetValue(id, out double last) && now - last < RowIntervalHours) return;
            _lastRow[id] = now;
            string name = WhiskeyRealism.Tactical.Battle.Producers.PostureTelemetry.NameOf(s, s.IndexOfOwn(id) >= 0 ? s.IndexOfOwn(id) : 0);
            TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.Decision, "TacticalCavalryStrike", TelemetrySeverity.Info,
                ev => ev
                    .WithSide(s.Alliance)
                    .WithUnit(name)
                    .WithDecision("TacticalCavalryStrike", outcome, "regiment=" + id)
                    .WithField("outcome", outcome)
                    .WithField("reason", reason ?? "ok")
                    .WithField("doctrineHours", now));
        }
    }
}
