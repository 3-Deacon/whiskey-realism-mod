using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // #42 — W&L feud replay for AIBattle.CheckForFeudGroupActions (4931-4960).
    //
    // Vanilla: for each assigned group (unittyp > 13) that feuds (ai_feudstance
    // >= 0, or every group on the feud instance, isplayeraiorfeud == 2), is idle
    // (no paths, not interrupted) and still able to fight, it rolls
    // initiative^2 * probfeudgroupmovement (scaled for political / volunteer
    // commanders) and moves the group toward GetClosestEnemyUnit(
    // neededdistancefeudgroupmovement) with a delayed SetWaypoint (useorderdelay:
    // true, 4957), stamping lastfeudactiontime. It never sets a stance and has
    // no W&L ownership check (PerformAIActionDLCWL is not called).
    //
    // This Prefix (kept as a Prefix-skip, plan BC5 inventory) replays that body
    // with three changes: the target is the closest VISIBLE enemy of the battle
    // snapshot (fog-honest; remembered enemies are not targets) inside the same
    // cap; the move is a FeudReplay intent run through the battle-core executor
    // (OrderExecutorRuntime.ExecuteInline: guard, chain fan-out, validation,
    // dedupe, cooldown, refusal under a higher arbiter claim), where the Feud
    // refusal is exempt for FeudReplay but PlayerOwned never is; and the
    // orchestrator's direct-child gate may deny it. lastfeudactiontime is stamped
    // only when the executor issued the move. Vanilla runs instead when Tactical
    // Commander Mode is not Active, the flag is off, a vanilla anchor is missing
    // or the side has no battle snapshot (the battle core is not running).
    [HarmonyPatch(typeof(AIBattle), "CheckForFeudGroupActions")]
    internal static class BattleFeudActionGatePatch
    {
        private static FieldInfo _allGroupsAssignedField;
        private static FieldInfo _bunitsField;
        private static FieldInfo _isPlayerAiOrFeudField;
        private static MethodInfo _isGroupStillAbleToFightMethod;
        private static bool _missingRequiredAnchorLogged;

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        internal static bool Prefix(AIBattle __instance)
        {
            using (TelemetryPerf.Scope("tactical.patch.feud-action-gate", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.feud-action-gate");
                if (!TacticalWriteGuard.ModeAllowsWrites() || !Enabled()) return true;

                bool tookOwnership = false;
                try
                {
                    var groups = AllGroupsAssigned(__instance);
                    var bunits = BattleUnits(__instance);
                    int? isPlayerAiOrFeud = IsPlayerAiOrFeud(__instance);
                    if (!isPlayerAiOrFeud.HasValue) return true;
                    if (!HasIsGroupStillAbleToFight()) return true;
                    if (groups == null || bunits == null) return true;
                    if (!SnapshotsAvailable(groups)) return true;

                    tookOwnership = true;
                    for (int i = 0; i < groups.Count; i++)
                    {
                        var group = groups[i] as Regiment;
                        if (group == null) continue;

                        bool feudEligible =
                            group.unittyp > 13 &&
                            ((group.ai_feudstance >= 0) | (isPlayerAiOrFeud.Value == 2)) &&
                            group.regimentpaths <= 0 &&
                            !group.pathinterrupted &&
                            IsGroupStillAbleToFight(__instance, group);

                        if (!feudEligible) continue;

                        float commanderInitiative = GameVars.commander[group.commander].GetCommanderInitiative();
                        float probability = Mathf.Pow(commanderInitiative, 2f) * GamePrefs.probfeudgroupmovement;
                        if (GameVars.commander[group.commander].political)
                        {
                            probability *= GamePrefs.chanceoffeudspoliticalcommanders;
                        }
                        if (!GameVars.commander[group.commander].westpoint && !GameVars.commander[group.commander].political)
                        {
                            probability *= GamePrefs.chanceoffeudsvolunteercommanders;
                        }

                        // Vanilla rolls for every eligible group before testing its target (4953).
                        if (UnityEngine.Random.Range(0f, 1f) > probability) continue;
                        if (!BattleCore.TryGetSnapshot(group.alliance, out BattleSnapshot snapshot)) continue;
                        int index = snapshot.IndexOfOwn(((Component)group).gameObject.GetInstanceID());
                        if (index < 0) continue;
                        if (!BattleWriterPolicy.TryFeudReplayIntent(snapshot, index, GamePrefs.neededdistancefeudgroupmovement, out BattleOrderIntent intent))
                            continue;

                        var orchDecision = DecideDirectChildGate(__instance, bunits, group, new Vector3(intent.TargetX, 0f, intent.TargetZ));
                        if (!orchDecision.Allow)
                        {
                            LogDeniedOrch(group, orchDecision);
                            continue;
                        }

                        if (!OrderExecutorRuntime.ExecuteInline(bunits, group, intent, out OrderOutcome outcome))
                        {
                            LogRefused(group, outcome.Reason);
                            continue;
                        }

                        group.lastfeudactiontime = CurrentBattleHour(bunits);
                        GameVars.DebugOwnLog("AI: group " + ((object)group)?.ToString() +
                            " is under feud and moving towards closest visible enemy at " + intent.TargetX + "," + intent.TargetZ +
                            " prob:" + probability +
                            " init:" + commanderInitiative);
                    }
                }
                catch (Exception ex)
                {
                    if (tookOwnership)
                    {
                        OnceLog.Warning("tactical-feud-guard:failed-owned", "BattleFeudActionGatePatch failed after taking ownership of the vanilla body; skipping vanilla this call to avoid duplicate movement side effects: " + ex.Message);
                        return false;
                    }

                    OnceLog.Warning("tactical-feud-guard:failed-pre", "BattleFeudActionGatePatch failed before movement ownership; falling back to vanilla: " + ex.Message);
                    return true;
                }

                return false;
            }
        }

        // Every assigned group's side has a battle snapshot this battle; without
        // one the battle core is not running and vanilla keeps its feud body.
        private static bool SnapshotsAvailable(IList groups)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                var group = groups[i] as Regiment;
                if (group == null) continue;
                if (!BattleCore.TryGetSnapshot(group.alliance, out _))
                {
                    OnceLog.Info("tactical-feud-guard:no-snapshot",
                        "[TacticalFeudGuard] no battle snapshot for a feud side; vanilla CheckForFeudGroupActions runs");
                    return false;
                }
            }
            return true;
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableWlTacticalChargeGuard.Value;
        }

        private static IList AllGroupsAssigned(AIBattle battle)
        {
            if (_allGroupsAssignedField == null)
                _allGroupsAssignedField = AccessTools.Field(typeof(AIBattle), "allgroupsassigned");
            if (_allGroupsAssignedField == null)
            {
                LogMissingRequiredAnchor("allgroupsassigned");
                return null;
            }

            IList groups = _allGroupsAssignedField.GetValue(battle) as IList;
            if (groups == null) LogMissingRequiredAnchor("allgroupsassigned:value");
            return groups;
        }

        private static BattleUnits BattleUnits(AIBattle battle)
        {
            if (_bunitsField == null)
                _bunitsField = AccessTools.Field(typeof(AIBattle), "bunits");
            if (_bunitsField == null)
            {
                LogMissingRequiredAnchor("bunits");
                return null;
            }

            BattleUnits bunits = _bunitsField.GetValue(battle) as BattleUnits;
            if (bunits == null) LogMissingRequiredAnchor("bunits:value");
            return bunits;
        }

        private static int? IsPlayerAiOrFeud(AIBattle battle)
        {
            if (_isPlayerAiOrFeudField == null)
                _isPlayerAiOrFeudField = AccessTools.Field(typeof(AIBattle), "isplayeraiorfeud");
            if (_isPlayerAiOrFeudField == null)
            {
                LogMissingRequiredAnchor("isplayeraiorfeud");
                return null;
            }

            object value = _isPlayerAiOrFeudField.GetValue(battle);
            if (value is int result) return result;

            LogMissingRequiredAnchor("isplayeraiorfeud:value");
            return null;
        }

        private static float CurrentBattleHour(BattleUnits bunits)
        {
            return bunits.uniStormSystem.Hour + (float)(bunits.battlepasseddays * 24);
        }

        private static bool HasIsGroupStillAbleToFight()
        {
            if (_isGroupStillAbleToFightMethod == null)
                _isGroupStillAbleToFightMethod = AccessTools.Method(typeof(AIBattle), "IsGroupStillAbleToFight");
            if (_isGroupStillAbleToFightMethod != null) return true;

            LogMissingRequiredAnchor("IsGroupStillAbleToFight");
            return false;
        }

        private static bool IsGroupStillAbleToFight(AIBattle battle, Regiment group)
        {
            object value = _isGroupStillAbleToFightMethod.Invoke(battle, new object[] { group, false });
            return value is bool result && result;
        }

        private static void LogRefused(Regiment group, SkipReason reason)
        {
            OnceLog.Info("tactical-feud-guard:refused", "[TacticalFeudGuard] feud replay refused by the order executor reason=" + reason +
                " group=" + SafeName(group) + " (first occurrence)");
        }

        private static void LogMissingRequiredAnchor(string anchor)
        {
            if (_missingRequiredAnchorLogged) return;
            _missingRequiredAnchorLogged = true;
            OnceLog.Warning("tactical-feud-guard:missing-anchor", "BattleFeudActionGatePatch missing required vanilla anchor " + anchor + "; falling back to vanilla before movement ownership when possible.");
        }

        private static string SafeName(Regiment unit)
        {
            if (unit == null) return "<null>";
            try { return ((UnityEngine.Object)((Component)unit).gameObject).name; }
            catch { return unit.GetHashCode().ToString(); }
        }

        // ---- O3: orchestrator direct-child gate branch ----

        private static FieldInfo _sideOfAiField;

        private static DirectChildGateDecision DecideDirectChildGate(
            AIBattle battle, BattleUnits bunits, Regiment group, Vector3 targetPos)
        {
            try
            {
                if (Plugin.EnableTacticalOrchestratorDirectChildGate == null
                    || !Plugin.EnableTacticalOrchestratorDirectChildGate.Value)
                    return new DirectChildGateDecision(true, "gate-disabled", DirectChildRole.Unknown);

                var sideOrch = ResolveSideOrchestrator(battle, bunits);
                if (sideOrch == null || sideOrch.Army == null)
                    return new DirectChildGateDecision(true, "no-orchestrator", DirectChildRole.Unknown);

                bool sideIsAi = !IsPlayerSide(sideOrch.AllianceId);
                if (!sideIsAi)
                    return new DirectChildGateDecision(true, "player-side", DirectChildRole.Unknown);

                int instanceId = ((Component)group).gameObject.GetInstanceID();
                string childId = "child-" + instanceId;
                var role = sideOrch.Army.GetDirectChildRole(childId);
                if (role == DirectChildRole.Unknown)
                {
                    // Fall back to the synth-army id used by DirectChildDiscovery when an army
                    // root has no qualifying direct children. The synth represents the army root
                    // itself, so feud movement on that root should be evaluated against the
                    // synth's role.
                    string synthId = "synth-army-" + instanceId;
                    var synthRole = sideOrch.Army.GetDirectChildRole(synthId);
                    if (synthRole != DirectChildRole.Unknown)
                    {
                        childId = synthId;
                        role = synthRole;
                    }
                }
                if (role == DirectChildRole.Unknown)
                    return new DirectChildGateDecision(true, "not-registered", DirectChildRole.Unknown);

                var maybeIntent = sideOrch.Army.GetDirectChildIntent(childId);
                int axisSector = maybeIntent.HasValue ? maybeIntent.Value.AxisSector : 0;
                int primarySector = maybeIntent.HasValue ? maybeIntent.Value.PrimarySector : 0;

                Vector3 groupPos = ((Component)group).gameObject.transform.position;
                // Both bearings are relative to the group's position so DecideAxis / DecideFallback
                // compare like-with-like. The target is the snapshot's closest visible enemy.
                float intendedBearingFromGroup = Mathf.Atan2(targetPos.z - groupPos.z, targetPos.x - groupPos.x);
                float dist = Vector3.Distance(groupPos, targetPos);
                float enemyBearingFromGroup = intendedBearingFromGroup;

                var input = new TacticalDirectChildGate.Input(
                    gateEnabled: true,
                    sideIsAi: true,
                    role: role,
                    axisSector: axisSector,
                    primarySector: primarySector,
                    intendedTargetBearingFromGroupRadians: intendedBearingFromGroup,
                    intendedTargetDistanceFromGroup: dist,
                    nearestEnemyBearingFromGroupRadians: enemyBearingFromGroup,
                    feudMaxDistance: GamePrefs.neededdistancefeudgroupmovement,
                    intendedTargetSector: ArmyEvidenceBuilder.SectorOfPoint(sideOrch.AllianceId, targetPos.x, targetPos.z));

                return TacticalDirectChildGate.Decide(input);
            }
            catch (Exception e)
            {
                OnceLog.Warning("tactical-direct-child-gate:exception",
                    "TacticalDirectChildGate.Decide failed: " + e.GetType().Name + " " + e.Message);
                return new DirectChildGateDecision(true, "gate-exception", DirectChildRole.Unknown);
            }
        }

        private static TacticalBattleOrchestrator ResolveSideOrchestrator(AIBattle battle, BattleUnits bunits)
        {
            try
            {
                int side = AIBattleSideOf(battle);
                if (side != 0 && side != 1) return null;
                if (bunits == null || bunits.alliance == null || side >= bunits.alliance.Length) return null;
                int allianceId = bunits.alliance[side];
                return TacticalBattleCoordinator.GetSideOrchestrator(allianceId);
            }
            catch
            {
                return null;
            }
        }

        private static int AIBattleSideOf(AIBattle battle)
        {
            if (_sideOfAiField == null) _sideOfAiField = AccessTools.Field(typeof(AIBattle), "sideofai");
            if (_sideOfAiField == null) return -1;
            var v = _sideOfAiField.GetValue(battle);
            return (v is int side) ? side : -1;
        }

        private static bool IsPlayerSide(int allianceId)
        {
            try
            {
                if (GameVars.ai_vs_ai) return false;
                return allianceId == GameVars.playeralliance;
            }
            catch
            {
                return false;
            }
        }

        private static void LogDeniedOrch(Regiment group, DirectChildGateDecision decision)
        {
            OnceLog.Info("tactical-direct-child-gate:deny",
                "[TacticalDirectChildGate] action=deny role=" + decision.Role
                + " reason=" + decision.Reason
                + " group=" + SafeName(group)
                + " surface=CheckForFeudGroupActions");
        }
    }
}
