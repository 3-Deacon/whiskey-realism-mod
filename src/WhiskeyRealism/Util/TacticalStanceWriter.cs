using System;
using System.Collections.Generic;
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
    // already agrees with the ordered stance) refreshes the vanilla hold
    // without a ChangeStance.
    public static class TacticalStanceWriter
    {
        private static readonly Dictionary<int, StanceHoldRecord> _records = new Dictionary<int, StanceHoldRecord>();

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

                // Cheap steady-state path: our claim is fresh and nothing moved.
                if (record.HasValue &&
                    record.Stance == targetStance &&
                    record.Source >= source &&
                    group.ai_stanceordered == targetStance &&
                    now - record.WrittenAtHours >= 0f &&
                    now - record.WrittenAtHours < TacticalStanceOwnership.HoldHours * 0.5f)
                    return false;

                AIBattle battle = FindOwningAiBattle(bunits, group);
                var input = new StanceWriteInput(
                    isAiGroup: battle != null,
                    targetStance: targetStance,
                    currentOrderedStance: group.ai_stanceordered,
                    macroAi: battle != null ? battle.macroai : -1,
                    nowHours: now,
                    record: record,
                    source: source);
                StanceWriteDecision decision = TacticalStanceOwnership.Decide(input);
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

        // Called on battle teardown (coordinator OnBattleEnd).
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
