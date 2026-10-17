using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Util
{
    // The one ai_stance writer for Whiskey (WS T4 binding decision) and the
    // Stance channel executor of the battle core: OrderExecutorRuntime calls
    // EnsureStance(bunits, group, stance, reason, IntentSource) for every Stance
    // winner. Until BC5 migrates them, #45 and #61 still call it directly.
    //
    // Clocks (BC4): hold records run on the doctrine clock
    // (TacticalDoctrineTime, frozen across vanilla's night end-of-day cycle);
    // lastaistancechangetime stays on GameVars.currenttimefromstart because
    // vanilla compares it with that clock (4264).
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
        // Group id -> doctrine hour of the first renewal since a producer last claimed it (the renewal bound).
        private static readonly Dictionary<int, float> _renewSince = new Dictionary<int, float>();
        internal const string RenewTag = "hold-renew";
        private static Func<AIBattle, Regiment, bool, bool, bool, bool, float> _groupStrength;
        private static bool _groupStrengthTried;

        public static bool EnsureStance(
            BattleUnits bunits,
            Regiment group,
            int targetStance,
            string contextTag = null,
            StanceWriteSource source = StanceWriteSource.Scorer,
            bool worksHold = false)
        {
            try
            {
                if (bunits == null || (UnityEngine.Object)group == null) return false;

                int id = group.GetInstanceID();
                float now = (float)TacticalDoctrineTime.NowBattleHours;
                float vanillaNow = GameVars.currenttimefromstart;
                _records.TryGetValue(id, out StanceHoldRecord record);
                // A producer's own claim or write restarts the renewal bound; a renewal does not.
                if (!ReferenceEquals(contextTag, RenewTag)) _renewSince.Remove(id);

                // Cheap steady-state path: our record is fresh, nothing moved and
                // vanilla's raw-clock stamp still holds (see SteadyStateHolds).
                // When the stamp lapsed (night jump) the full pass below claims
                // ("held") and re-stamps.
                if (TacticalStanceOwnership.SteadyStateHolds(record, targetStance, source, group.ai_stanceordered, now,
                        group.lastaistancechangetime, vanillaNow, GamePrefs.updatetimeforresettingofaistance))
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
                    vanillaGroupStance: vanillaStance,
                    vanillaNowHours: vanillaNow,
                    worksHold: worksHold);
                StanceWriteDecision decision = TacticalStanceOwnership.Decide(input);
                if (TacticalStanceOwnership.ReleasesVanillaHold(decision))
                {
                    Yield(group, id, vanillaNow);
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

                float stamp = TacticalStanceOwnership.VanillaHoldStamp(input, GamePrefs.updatetimeforresettingofaistance);
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

        // Stance channel entry for the order executor: the intent source maps
        // onto the ownership ladder (TacticalStanceOwnership.SourceFor).
        public static bool EnsureStance(
            BattleUnits bunits,
            Regiment group,
            int targetStance,
            string contextTag,
            IntentSource source,
            bool worksHold = false)
        {
            return EnsureStance(bunits, group, targetStance, contextTag, TacticalStanceOwnership.SourceFor(source), worksHold);
        }

        // Keeps the holds of groups that carry an order alive (TacticalStanceOwnership.RenewsWhileOrdered): the
        // producers go quiet while a group marches or waits for its order, and a hold nobody renews lapses
        // after HoldHours, when vanilla's AdjustGroupAIStance writes its own stance over Whiskey's. Runs once
        // per arbiter tick after the winners were issued, so a producer's own claim has already refreshed its
        // record (and restarted the bound) and the cheap path below returns at once. Each renewal is the
        // group's own record through the full decision (the vanilla weak-group carve-out, the army retreat).
        // Renewals alone last RenewalBoundHours; then the hold lapses. Reads the snapshot; writes only what
        // EnsureStance writes.
        public static int RenewWhileOrdered(BattleUnits bunits, BattleSnapshot snapshot)
        {
            if (bunits == null || snapshot == null || _records.Count == 0) return 0;
            int renewed = 0;
            try
            {
                UnitRecord[] own = snapshot.Own;
                float now = (float)TacticalDoctrineTime.NowBattleHours;
                for (int i = 0; i < snapshot.OwnCount; i++)
                {
                    if (!TacticalStanceOwnership.IsRenewalCandidate((own[i].Flags & UnitFlags.IsGroup) != 0,
                            (own[i].Flags & UnitFlags.PlayerOwned) != 0, own[i].RegimentPaths, own[i].QueuedOrders)) continue;
                    Regiment group = BattleSnapshotBuilder.UnitAt(snapshot, i, enemy: false);
                    if ((UnityEngine.Object)group == null) continue;
                    int id = group.GetInstanceID();
                    if (!_records.TryGetValue(id, out StanceHoldRecord record)) continue;
                    if (!TacticalStanceOwnership.RenewsWhileOrdered(record, own[i].AiStanceOrdered, carryingOrder: true)) continue;
                    if (!_renewSince.TryGetValue(id, out float since)) _renewSince[id] = since = now;
                    if (!TacticalStanceOwnership.RenewalLive(since, now)) continue;
                    EnsureStance(bunits, group, record.Stance, RenewTag, record.Source);
                    renewed++;
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-stance-write:renew-failed",
                    "[TacticalStanceWrite] RenewWhileOrdered threw: " + ex.GetType().Name + " " + ex.Message);
            }
            return renewed;
        }

        // Hand one group back to vanilla: drop ownership and pull a future
        // stamp back so 4264 re-evaluates it on the next macro cycle (raw
        // clock: vanillaNow is GameVars.currenttimefromstart). Only a
        // group Whiskey held can carry a Whiskey stamp; vanilla's own stamps
        // are left alone (no vanilla-state write in MonitorOnly).
        private static void Yield(Regiment group, int id, float vanillaNow)
        {
            _renewSince.Remove(id);
            if (!_records.Remove(id)) return;
            float release = TacticalStanceOwnership.ReleaseStamp(vanillaNow);
            if (group.lastaistancechangetime > release) group.lastaistancechangetime = release;
        }

        // Vanilla's per-group stance for the group (AdjustGroupAIStance
        // 4236-4252 inputs), or VanillaStanceUnknown when it cannot be read.
        internal static int VanillaGroupStance(AIBattle battle, BattleUnits bunits, Regiment group, int macro)
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
            _renewSince.Clear();
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
