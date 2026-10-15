using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Util
{
    // The one ai_stance writer for Whiskey (WS T4 binding decision). Every
    // Whiskey stance write — #45 group stance, #61 posture executor, any T3
    // group-level stance intent — goes through here.
    //
    // Vanilla anchors (decompile, build 14703111):
    //   AIBattle.AdjustGroupAIStance (4221-4278) is the model: it writes stance
    //     only for groups in its pool (unitsused), via
    //     ChangeStance(go, s, immediate:false, overwriteaigroups:false), then
    //     sets ai_stance / ai_stanceordered / lastaistancechangetime itself.
    //     This writer does the same, except lastaistancechangetime gets the
    //     hold stamp from TacticalStanceOwnership so vanilla's 4264 guard skips
    //     the group while Whiskey holds an opinion.
    //   BattleUnits.ChangeStance (90772): dedups on ai_stanceordered (not
    //     ai_stance); stance > 0 sets permanentlydetached and, for the player
    //     alliance outside ai_vs_ai, adds the unit to aibattle[1]'s pool. Vanilla
    //     artillery AI skips detached batteries (3789, 3834, 3890, 3928) and
    //     SetGroupFormation skips detached children (92070) — so attached
    //     sub-units (regiments, batteries, brigades nested inside an AI group)
    //     are refused.
    //   Pool membership (85227-85262): only groups (unittyp > 13) at the AI
    //     allocation level are added to aibattle[0]/[1]. aibattle[2] is the W&L
    //     feud instance and is not a Whiskey target.
    //
    // Returns true only when a ChangeStance was issued. A "claim" (Whiskey
    // already agrees with the ordered stance) records ownership for the
    // #45/#61 priority rules and re-stamps the vanilla hold without a
    // ChangeStance. Before either, vanilla's per-group stance is computed
    // (AIBattle.GetGroupStrength, 6025, private — cached delegate) so the
    // owner can yield to vanilla's weak-group defend/screen; a yield releases
    // the stamp (see TacticalStanceOwnership).
    public static class TacticalStanceWriter
    {
        static TacticalStanceWriter()
        {
            BattleScope.Register("stance-writer", ResetForBattleEnd);
        }

        private static readonly Dictionary<int, StanceHoldRecord> _records = new Dictionary<int, StanceHoldRecord>();
        private static Func<AIBattle, Regiment, bool, bool, bool, bool, float> _groupStrength;
        private static bool _groupStrengthTried;

        public static bool EnsureStance(
            BattleUnits bunits,
            Regiment group,
            int targetStance,
            string contextTag = null,
            StanceWriteSource source = StanceWriteSource.Scorer)
        {
            try
            {
                if (bunits == null || (UnityEngine.Object)group == null) return false;

                int id = group.GetInstanceID();
                float now = GameVars.currenttimefromstart;
                _records.TryGetValue(id, out StanceHoldRecord record);

                // Cheap steady-state path: our record is fresh and nothing moved.
                // Skips the vanilla per-group check, so a group that turns weak
                // is yielded at most HoldHours * 0.5 after the last full pass.
                if (record.HasValue &&
                    record.Stance == targetStance &&
                    record.Source >= source &&
                    group.ai_stanceordered == targetStance &&
                    now - record.WrittenAtHours >= 0f &&
                    now - record.WrittenAtHours < TacticalStanceOwnership.HoldHours * 0.5f)
                    return false;

                AIBattle battle = FindOwningAiBattle(bunits, group);
                int macro = battle != null ? battle.macroai : -1;
                int vanillaStance = TacticalStanceOwnership.NeedsVanillaGroupStance(battle != null, targetStance, macro)
                    ? VanillaGroupStance(battle, bunits, group, macro)
                    : TacticalStanceOwnership.VanillaStanceUnknown;
                var input = new StanceWriteInput(
                    isAiGroup: battle != null,
                    targetStance: targetStance,
                    currentOrderedStance: group.ai_stanceordered,
                    macroAi: macro,
                    nowHours: now,
                    record: record,
                    source: source,
                    vanillaGroupStance: vanillaStance);
                StanceWriteDecision decision = TacticalStanceOwnership.Decide(input);
                if (TacticalStanceOwnership.ReleasesVanillaHold(decision))
                {
                    Yield(group, id, now);
                    OnceLog.Info(
                        "tactical-stance-write:yield:" + source + ":" + decision.Reason,
                        "[TacticalStanceWrite] yielded " + source + " stance=" + targetStance
                        + " vanilla=" + vanillaStance + " reason=" + decision.Reason + " (first occurrence)");
                    return false;
                }
                if (decision.Action == StanceWriteAction.Refuse)
                {
                    OnceLog.Info(
                        "tactical-stance-write:refuse:" + source + ":" + decision.Reason,
                        "[TacticalStanceWrite] refused " + source + " stance=" + targetStance
                        + " reason=" + decision.Reason + " (first occurrence)");
                    return false;
                }

                if (!TacticalWriteGuard.CanWrite(group)) return false;
                GameObject gameObject = ((Component)group).gameObject;
                if (gameObject == null || !gameObject.activeInHierarchy) return false;

                float stamp = TacticalStanceOwnership.VanillaHoldStamp(
                    now,
                    TacticalStanceOwnership.HoldHours,
                    GamePrefs.updatetimeforresettingofaistance);
                bool wrote = false;
                if (decision.Action == StanceWriteAction.Write)
                {
                    int was = group.ai_stanceordered;
                    bunits.ChangeStance(gameObject, targetStance, immediate: false, overwriteaigroups: false);
                    group.ai_stance = targetStance;
                    group.ai_stanceordered = targetStance;
                    wrote = true;
                    OnceLog.Info(
                        "tactical-stance-write:" + source + ":" + decision.Reason + ":" + targetStance,
                        "[TacticalStanceWrite] " + source + " " + decision.Reason + " stance=" + targetStance
                        + " (was " + was + ") first=" + (contextTag ?? "-"));
                }

                if (TacticalStanceOwnership.StampsVanillaHold(decision, input))
                    group.lastaistancechangetime = stamp;
                _records[id] = TacticalStanceOwnership.Next(decision, input);
                return wrote;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-stance-write:exception",
                    "[TacticalStanceWrite] EnsureStance threw: " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        public static bool EnsureStanceForTask(
            BattleUnits bunits,
            Regiment group,
            CommandTaskType task,
            string contextTag = null,
            StanceWriteSource source = StanceWriteSource.Scorer)
        {
            int stance = TacticalCommandTaskToStanceMapper.Map(task);
            if (stance == TacticalCommandTaskToStanceMapper.StanceNone) return false;
            return EnsureStance(bunits, group, stance, contextTag, source);
        }

        // Hand one group back to vanilla: drop ownership and pull a future
        // stamp back so 4264 re-evaluates it on the next macro cycle. Only a
        // group Whiskey held can carry a Whiskey stamp; vanilla's own stamps
        // are left alone (no vanilla-state write in MonitorOnly).
        private static void Yield(Regiment group, int id, float now)
        {
            if (!_records.Remove(id)) return;
            float release = TacticalStanceOwnership.ReleaseStamp(now);
            if (group.lastaistancechangetime > release) group.lastaistancechangetime = release;
        }

        // Vanilla's per-group stance for the group (AdjustGroupAIStance
        // 4236-4252 inputs), or VanillaStanceUnknown when it cannot be read.
        private static int VanillaGroupStance(AIBattle battle, BattleUnits bunits, Regiment group, int macro)
        {
            try
            {
                var strengthOf = GroupStrengthDelegate();
                if (strengthOf == null) return TacticalStanceOwnership.VanillaStanceUnknown;
                float[] assault = GamePrefs.strengthtriggerforassault_micro;
                float[] defend = GamePrefs.strengthtriggerfordefend_micro;
                float[] screen = GamePrefs.strengthtriggerforscreening_micro;
                if (assault == null || defend == null || screen == null ||
                    macro < 0 || macro >= assault.Length || macro >= defend.Length || macro >= screen.Length)
                    return TacticalStanceOwnership.VanillaStanceUnknown;

                float strength = strengthOf(battle, group, false, true, true, true);
                return TacticalStanceOwnership.VanillaPerGroupStance(new VanillaGroupStanceInput(
                    strength,
                    macro,
                    lineGroup: group.GetUnitSymbolData().oobunitsymbolid == 0,
                    defenderBattle: bunits.GetBattleType(group.alliance, usereadinesswhendraw: true) == 1,
                    assaultTrigger: assault[macro],
                    defendTrigger: defend[macro],
                    screenTrigger: screen[macro]));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-stance-write:vanilla-stance-failed",
                    "[TacticalStanceWrite] vanilla per-group stance unavailable: " + ex.GetType().Name + " " + ex.Message);
                return TacticalStanceOwnership.VanillaStanceUnknown;
            }
        }

        private static Func<AIBattle, Regiment, bool, bool, bool, bool, float> GroupStrengthDelegate()
        {
            if (_groupStrengthTried) return _groupStrength;
            _groupStrengthTried = true;
            try
            {
                var method = AccessTools.Method(typeof(AIBattle), "GetGroupStrength",
                    new[] { typeof(Regiment), typeof(bool), typeof(bool), typeof(bool), typeof(bool) });
                if (method != null)
                    _groupStrength = AccessTools.MethodDelegate<Func<AIBattle, Regiment, bool, bool, bool, bool, float>>(method);
            }
            catch (Exception ex)
            {
                _groupStrength = null;
                OnceLog.Warning("tactical-stance-write:group-strength-missing",
                    "[TacticalStanceWrite] AIBattle.GetGroupStrength delegate failed: " + ex.GetType().Name + " " + ex.Message);
                return null;
            }

            if (_groupStrength == null)
                OnceLog.Warning("tactical-stance-write:group-strength-missing",
                    "[TacticalStanceWrite] AIBattle.GetGroupStrength not found; claims will not stamp the vanilla hold");
            return _groupStrength;
        }

        // Army retreat (macroai 3): hand every Whiskey-held group of this AI
        // back to vanilla so AdjustGroupAIStance screens it on the next cycle
        // (4258) and MicroAICheckForRetreats (5641) can run.
        public static void ReleaseForRetreat(AIBattle battle)
        {
            try
            {
                if ((UnityEngine.Object)battle == null || battle.unitsused == null || _records.Count == 0) return;
                float release = TacticalStanceOwnership.ReleaseStamp(GameVars.currenttimefromstart);
                for (int i = 0; i < battle.unitsused.Count; i++)
                {
                    Regiment group = battle.unitsused[i];
                    if ((UnityEngine.Object)group == null) continue;
                    if (!_records.Remove(group.GetInstanceID())) continue;
                    if (group.lastaistancechangetime > release) group.lastaistancechangetime = release;
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-stance-write:release-failed",
                    "[TacticalStanceWrite] ReleaseForRetreat threw: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        // Battle end (BattleScope).
        public static void ResetForBattleEnd()
        {
            _records.Clear();
        }

        private static AIBattle FindOwningAiBattle(BattleUnits bunits, Regiment group)
        {
            if (group.unittyp <= 13) return null;
            AIBattle[] battles = bunits.aibattle;
            if (battles == null) return null;
            for (int i = 0; i < battles.Length && i < 2; i++)
            {
                AIBattle battle = battles[i];
                if ((UnityEngine.Object)battle == null) continue;
                if (TacticalWriteGuard.IsFeudInstance(battle)) continue;
                if (battle.unitsused != null && battle.unitsused.Contains(group)) return battle;
            }

            return null;
        }
    }
}
