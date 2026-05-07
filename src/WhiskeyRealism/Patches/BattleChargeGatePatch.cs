using System;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // B1 W&L guard for AIBattle.MicroAICheckForCharges. Vanilla owns charge
    // initiation and cancellation in one small method; this Prefix mirrors that
    // body so player-subordinate charge initiation can be blocked without
    // skipping the cancellation branch.
    [HarmonyPatch(typeof(AIBattle), "MicroAICheckForCharges")]
    internal static class BattleChargeGatePatch
    {
        private static FieldInfo _bunitsField;
        private static FieldInfo _isPlayerAiOrFeudField;
        private static bool _missingRequiredAnchorLogged;

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        internal static bool Prefix(AIBattle __instance, Regiment aigroup, int restrictunittypes)
        {
            if (!Enabled()) return true;

            bool tookOwnership = false;
            try
            {
                if (aigroup == null || aigroup.allattachedunits == null) return true;

                BattleUnits bunits = BattleUnits(__instance);
                if (bunits == null) return true;

                int? isPlayerAiOrFeud = IsPlayerAiOrFeud(__instance);
                if (!isPlayerAiOrFeud.HasValue) return true;

                Regiment[] allattachedunits = aigroup.allattachedunits;

                for (int i = 0; i < allattachedunits.Length; i++)
                {
                    Regiment unit = allattachedunits[i];
                    if (unit == null || unit.groupaiobject != aigroup) continue;

                    bool chargeStance = aigroup.ai_stance == 4;
                    bool typeAllowed = ((unit.unittyp <= 13 && restrictunittypes == 13) | (unit.unittyp == restrictunittypes));
                    bool feudAllowed = ((aigroup.ai_feudstance == -1) | (isPlayerAiOrFeud.Value == 2));

                    if (!unit.permanentlydetached &&
                        chargeStance &&
                        !unit.isrouted &&
                        !unit.markedforrout &&
                        unit.movementmode != 4 &&
                        unit.movementmode != 5 &&
                        unit.movementmode != 6 &&
                        unit.movementmode != 1 &&
                        unit.movementmode != 3 &&
                        unit.movementmode != 2 &&
                        unit.unittyp != 5 &&
                        typeAllowed &&
                        feudAllowed &&
                        unit.lastaichargetime < GameVars.currenttimefromstart + GamePrefs.timetorenewaichargecheck)
                    {
                        TacticalWlGuardDecision decision = TacticalWlActionGuard.Decide(
                            configEnabled: Plugin.Instance.EnableWlTacticalChargeGuard.Value,
                            dlcScenarioActive: DLC_WL.dlc_scenarioactive,
                            action: TacticalWlGuardAction.ChargeInitiation,
                            unitUnderCommander: unit.dlcw_isundercommander,
                            groupUnderCommander: aigroup.dlcw_isundercommander,
                            attachedUnitUnderCommander: false);

                        if (decision.Allow)
                        {
                            tookOwnership = true;
                            unit.SetMovementMode(3);
                            aigroup.lastfeudactiontime = CurrentBattleHour(bunits);
                        }
                        else
                        {
                            tookOwnership = true;
                            aigroup.lastfeudactiontime = CurrentBattleHour(bunits);
                            LogDenied(unit, aigroup, decision.Reason);
                        }
                    }

                    if (!unit.permanentlydetached &&
                        !unit.isrouted &&
                        !unit.markedforrout &&
                        unit.movementmode == 3 &&
                        !chargeStance &&
                        unit.unittyp != 5 &&
                        typeAllowed &&
                        feudAllowed)
                    {
                        tookOwnership = true;
                        unit.SetMovementMode();
                        aigroup.lastfeudactiontime = CurrentBattleHour(bunits);
                    }
                }
            }
            catch (Exception ex)
            {
                if (tookOwnership)
                {
                    OnceLog.Warning("tactical-charge-guard:failed-owned", "BattleChargeGatePatch failed after taking ownership of the vanilla body; skipping vanilla this call to avoid duplicate movement side effects: " + ex.Message);
                    return false;
                }

                OnceLog.Warning("tactical-charge-guard:failed-pre", "BattleChargeGatePatch failed before movement ownership; falling back to vanilla: " + ex.Message);
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

        private static void LogDenied(Regiment unit, Regiment group, string reason)
        {
            OnceLog.Info("tactical-charge-guard", "BattleChargeGatePatch wired");
            OnceLog.Info("tactical-charge-guard:deny:" + SafeName(unit), "[TacticalChargeGuard] action=deny reason=" + reason +
                " unit=" + SafeName(unit) +
                " group=" + SafeName(group));
        }

        private static void LogMissingRequiredAnchor(string anchor)
        {
            if (_missingRequiredAnchorLogged) return;
            _missingRequiredAnchorLogged = true;
            OnceLog.Warning("tactical-charge-guard:missing-anchor:" + anchor, "BattleChargeGatePatch missing required vanilla anchor " + anchor + "; falling back to vanilla.");
        }

        private static string SafeName(Regiment unit)
        {
            if (unit == null) return "<null>";
            try { return ((UnityEngine.Component)unit).gameObject.name; }
            catch { return unit.GetHashCode().ToString(); }
        }
    }
}
