using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // B1 W&L guard for AIBattle.CheckForFeudGroupActions. Vanilla can move a
    // feuding formation toward the closest enemy through delayed SetWaypoint
    // without PerformAIActionDLCWL; this Prefix mirrors the vanilla method and
    // blocks only protected player-subordinate group movement.
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
            if (!Enabled()) return true;

            bool tookOwnership = false;
            try
            {
                var groups = AllGroupsAssigned(__instance);
                var bunits = BattleUnits(__instance);
                int? isPlayerAiOrFeud = IsPlayerAiOrFeud(__instance);
                if (!isPlayerAiOrFeud.HasValue) return true;
                if (!HasIsGroupStillAbleToFight()) return true;
                if (groups == null || bunits == null) return true;

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

                    GameObject closestEnemy = group.GetClosestEnemyUnit(GamePrefs.neededdistancefeudgroupmovement);
                    if (UnityEngine.Random.Range(0f, 1f) > probability || closestEnemy == null) continue;

                    bool attachedUnderCommander = ContainsAttachedUnderCommander(group);
                    var wlDecision = TacticalWlActionGuard.Decide(
                        configEnabled: Plugin.Instance.EnableWlTacticalChargeGuard.Value,
                        dlcScenarioActive: DLC_WL.dlc_scenarioactive,
                        action: TacticalWlGuardAction.FeudMovement,
                        unitUnderCommander: group.dlcw_isundercommander,
                        groupUnderCommander: group.dlcw_isundercommander,
                        attachedUnitUnderCommander: attachedUnderCommander);

                    tookOwnership = true;
                    group.lastfeudactiontime = CurrentBattleHour(bunits);

                    if (!wlDecision.Allow)
                    {
                        LogDenied(group, wlDecision.Reason);
                        continue;
                    }

                    var orchDecision = DecideDirectChildGate(__instance, bunits, group, closestEnemy, isPlayerAiOrFeud.Value);
                    if (!orchDecision.Allow)
                    {
                        LogDeniedOrch(group, orchDecision);
                        continue;
                    }

                    GameVars.DebugOwnLog("AI: group " + ((object)group)?.ToString() +
                        " is under feud and moving towards closest enemy: " +
                        ((object)closestEnemy)?.ToString() +
                        " curr pos:" + ((object)((Component)group).gameObject.transform.position).ToString() +
                        " enemy pos:" + ((object)closestEnemy.transform.position).ToString() +
                        " prob:" + probability +
                        " init:" + commanderInitiative);
                    bunits.SetWaypoint(group, closestEnemy.transform.position, newpath: true, doublequick: false, -1f, modifylastwaypoint: false, useorderdelay: true, -1f, -1, showmovementoptions: false);
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

        private static bool ContainsAttachedUnderCommander(Regiment group)
        {
            if (group == null || group.allattachedunits == null) return false;
            for (int i = 0; i < group.allattachedunits.Length; i++)
            {
                var unit = group.allattachedunits[i];
                if (unit != null && unit.dlcw_isundercommander) return true;
            }
            return false;
        }

        private static void LogDenied(Regiment group, string reason)
        {
            OnceLog.Info("tactical-feud-guard", "BattleFeudActionGatePatch wired");
            OnceLog.Info("tactical-feud-guard:deny:" + SafeName(group), "[TacticalFeudGuard] action=deny reason=" + reason +
                " group=" + SafeName(group));
        }

        private static void LogMissingRequiredAnchor(string anchor)
        {
            if (_missingRequiredAnchorLogged) return;
            _missingRequiredAnchorLogged = true;
            OnceLog.Warning("tactical-feud-guard:missing-anchor:" + anchor, "BattleFeudActionGatePatch missing required vanilla anchor " + anchor + "; falling back to vanilla before movement ownership when possible.");
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
            AIBattle battle, BattleUnits bunits, Regiment group, GameObject closestEnemy, int isPlayerAiOrFeud)
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

                string childId = "child-" + ((Component)group).gameObject.GetInstanceID();
                var role = sideOrch.Army.GetDirectChildRole(childId);
                if (role == DirectChildRole.Unknown)
                    return new DirectChildGateDecision(true, "not-registered", DirectChildRole.Unknown);

                var maybeIntent = sideOrch.Army.GetDirectChildIntent(childId);
                int axisSector = maybeIntent.HasValue ? maybeIntent.Value.AxisSector : 0;
                int primarySector = maybeIntent.HasValue ? maybeIntent.Value.PrimarySector : 0;

                Vector3 groupPos = ((Component)group).gameObject.transform.position;
                Vector3 targetPos = closestEnemy.transform.position;
                float groupBearing = Mathf.Atan2(groupPos.z, groupPos.x);
                float intendedBearing = Mathf.Atan2(targetPos.z, targetPos.x);
                float dist = Vector3.Distance(groupPos, targetPos);
                float enemyBearingFromGroup = Mathf.Atan2(targetPos.z - groupPos.z, targetPos.x - groupPos.x);

                var input = new TacticalDirectChildGate.Input(
                    gateEnabled: true,
                    sideIsAi: true,
                    role: role,
                    axisSector: axisSector,
                    primarySector: primarySector,
                    groupBearingFromOriginRadians: groupBearing,
                    intendedTargetBearingFromOriginRadians: intendedBearing,
                    intendedTargetDistanceFromGroup: dist,
                    nearestEnemyBearingFromGroupRadians: enemyBearingFromGroup,
                    feudMaxDistance: GamePrefs.neededdistancefeudgroupmovement,
                    intendedTargetSector: ResolveTargetSector(targetPos));

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

        private static int ResolveTargetSector(Vector3 targetPos)
        {
            // TODO: O3.x — wire to TacticalSectorLedger when a position-→sector resolver lands.
            // Return -1 so TacticalDirectChildGate.Input ctor coerces it to primarySector,
            // making Screen/Refuse role decisions degrade to "always in-sector when we can't
            // tell" (denial precision tightens after smoke calibration). Acceptable because
            // Main/SupportMain/Fix/Reserve/Fallback decisions don't read this field.
            return -1;
        }

        private static void LogDeniedOrch(Regiment group, DirectChildGateDecision decision)
        {
            OnceLog.Info("tactical-direct-child-gate:deny:" + SafeName(group) + ":" + decision.Reason,
                "[TacticalDirectChildGate] action=deny role=" + decision.Role
                + " reason=" + decision.Reason
                + " group=" + SafeName(group)
                + " surface=CheckForFeudGroupActions");
        }
    }
}
