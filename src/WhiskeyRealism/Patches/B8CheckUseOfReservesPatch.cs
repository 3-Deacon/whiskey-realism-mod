using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AIBattle.CheckUseOfReserves(Regiment) at decompile line 6062 supports
    // outflanked friendly units by moving an unengaged reserve toward them. This
    // Postfix uses the same input set to compute TacticalWithdrawalDoctrine.Decision
    // per attached unit, emits a help-request telemetry sink, and conditionally
    // issues SetWithdrawal calls when the config flag is on. Reserve-list mutation
    // is NOT performed here -- vanilla owns that.
    [HarmonyPatch(typeof(AIBattle), "CheckUseOfReserves")]
    public static class B8CheckUseOfReservesPatch
    {
        private static FieldInfo _bunitsField;
        private static FieldInfo _isPlayerAiOrFeudField;
        private static MethodInfo _performAiActionDlcWlMethod;

        [HarmonyPostfix]
        public static void Postfix(AIBattle __instance, Regiment aigroup)
        {
            if (Plugin.Instance == null || Plugin.Instance.Enabled == null || !Plugin.Instance.Enabled.Value) return;
            if (aigroup == null) return;

            try
            {
                OnceLog.Info("b8-check-reserves", "B8 CheckUseOfReserves Postfix wired.");

                var allUnits = aigroup.allattachedunits;

                int? isPlayerAiOrFeud = IsPlayerAiOrFeud(__instance);
                if (!isPlayerAiOrFeud.HasValue) return;
                if (!HasPerformAiActionDlcWl()) return;

                if (allUnits == null)
                {
                    EmitHelpRequest(aigroup, TacticalWithdrawalDoctrine.Decision.HoldLine);
                    return;
                }

                bool writesEnabled = Plugin.EnableTacticalWithdrawalDoctrine != null
                    && Plugin.EnableTacticalWithdrawalDoctrine.Value;

                List<Regiment> withdrawalList = null;
                TacticalWithdrawalDoctrine.Decision strongestDecision = TacticalWithdrawalDoctrine.Decision.HoldLine;

                for (int i = 0; i < allUnits.Length; i++)
                {
                    var unit = allUnits[i];
                    if (unit == null) continue;
                    if (unit.unittyp > TacticalUnitType.Cavalry) continue;
                    if (unit.isrouted || unit.markedforrout) continue;
                    if (unit.permanentlydetached) continue;
                    if (!TacticalGateHelpers.PassesWlOwnership(aigroup.ai_feudstance, isPlayerAiOrFeud.Value))
                        continue;
                    if (!PerformAiActionDlcWl(unit, aigroup)) continue;

                    var snapshot = BuildSnapshot(unit, aigroup, isPlayerAiOrFeud.Value);
                    var moraleInput = TacticalWithdrawalInputAdapter.ToMoralePressureInput(snapshot);
                    var moraleResult = TacticalMoralePressure.Score(moraleInput);

                    var quadrantInput = TacticalWithdrawalInputAdapter.ToQuadrantInput(snapshot);
                    var quadrantOutput = TacticalQuadrantThreatScorer.Score(quadrantInput);

                    var fatigueResult = TacticalFatigueState.Score(snapshot.Fatigue);

                    var doctrineInput = new TacticalWithdrawalDoctrine.Input
                    {
                        MoralePressure = moraleResult,
                        RearPressureFlag = quadrantOutput.RearPressureFlag,
                        Fatigue = fatigueResult,
                        AiFeudStance = snapshot.AiFeudStance,
                        IsPlayerAiOrFeud = snapshot.IsPlayerAiOrFeud,
                    };
                    var decision = TacticalWithdrawalDoctrine.Score(doctrineInput);

                    if (DecisionRank(decision) > DecisionRank(strongestDecision))
                        strongestDecision = decision;

                    if (writesEnabled
                        && PassesWithdrawalWriteEnvelope(unit)
                        && (decision == TacticalWithdrawalDoctrine.Decision.RearGuard
                            || decision == TacticalWithdrawalDoctrine.Decision.FullRetreat))
                    {
                        if (withdrawalList == null) withdrawalList = new List<Regiment>();
                        withdrawalList.Add(unit);
                    }
                }

                EmitHelpRequest(aigroup, strongestDecision);

                if (withdrawalList != null && withdrawalList.Count > 0)
                {
                    if (!TryGetWithdrawalEndDate(__instance, out float endDate)) return;
                    Vector3 fromPosition = new Vector3();
                    BattleUnits.SetWithdrawal(endDate, withdrawalList, aigroup.alliance, fromPosition, false);
                    OnceLog.Info("b8-set-withdrawal", "B8 SetWithdrawal applied count=" + withdrawalList.Count);
                }
            }
            catch (System.Exception ex)
            {
                OnceLog.Warning("b8-check-reserves-error", "[B8] CheckUseOfReserves Postfix error: " + ex.Message);
            }
        }

        private static TacticalWithdrawalInputAdapter.Snapshot BuildSnapshot(Regiment unit, Regiment aigroup, int isPlayerAiOrFeud)
        {
            float fallbackThreshold = 0.4f;
            try
            {
                if (GamePrefs.moraletriggerforfallbackifenemyclose != null
                    && aigroup.ai_stance >= 0
                    && aigroup.ai_stance < GamePrefs.moraletriggerforfallbackifenemyclose.Length)
                {
                    fallbackThreshold = GamePrefs.moraletriggerforfallbackifenemyclose[aigroup.ai_stance];
                }
            }
            catch { }

            bool fired = false;
            try
            {
                if (unit.unitrange != null && unit.unitrange.closestenemyunitfarreg != null)
                {
                    fired = unit.ReceivedFireFromUnit(unit.unitrange.closestenemyunitfarreg)
                        || unit.CheckReceivedFireOtherUnit(unit.unitrange.closestenemyunitfarreg);
                }
            }
            catch { }

            float[] slices = null;
            float sliceWidth = 10f;
            try
            {
                if (unit.unitrange != null) slices = unit.unitrange.enemystrengthwithinangle;
                if (GamePrefs.aidefensiveslices > 0) sliceWidth = 360f / GamePrefs.aidefensiveslices;
            }
            catch { }

            float facing = 0f;
            try { facing = ((UnityEngine.Component)unit).transform.eulerAngles.y; } catch { }

            return new TacticalWithdrawalInputAdapter.Snapshot
            {
                Morale = unit.morale,
                BattleStartMorale = unit.battlestartmorale,
                BattleStartMoraleInitialized = unit.battlestartmorale >= 0f,
                FallbackThreshold = fallbackThreshold,
                Outflanked = unit.outflanked,
                FriendlyRoutedNear = unit.friendlyroutednear,
                EnemyRoutedNear = unit.enemyroutednear,
                ReceivedFireFromClosestFar = fired,
                CoverValue = unit.covervalue,
                CoverObject = unit.coverobject,
                AiFeudStance = aigroup.ai_feudstance,
                IsPlayerAiOrFeud = isPlayerAiOrFeud,
                Fatigue = unit.fatigue,
                EnemyStrengthWithinAngle = slices,
                SliceWidthDegrees = sliceWidth,
                UnitFacingDegrees = facing,
            };
        }

        private static void EmitHelpRequest(Regiment aigroup, TacticalWithdrawalDoctrine.Decision decision)
        {
            int sectorId = aigroup.GetInstanceID();
            TacticalHelpRequest.Decision request;
            switch (decision)
            {
                case TacticalWithdrawalDoctrine.Decision.Screen:
                    request = TacticalHelpRequest.Decision.RequestReserveScreen;
                    break;
                case TacticalWithdrawalDoctrine.Decision.RearGuard:
                    request = TacticalHelpRequest.Decision.RequestLineRelief;
                    break;
                case TacticalWithdrawalDoctrine.Decision.FullRetreat:
                    request = TacticalHelpRequest.Decision.RequestMainEffortShift;
                    break;
                default:
                    request = TacticalHelpRequest.Decision.NoRequest;
                    break;
            }
            TacticalSectorLedger.SetHelpRequest(sectorId, request);
        }

        private static int DecisionRank(TacticalWithdrawalDoctrine.Decision decision)
        {
            switch (decision)
            {
                case TacticalWithdrawalDoctrine.Decision.FullRetreat:
                    return 3;
                case TacticalWithdrawalDoctrine.Decision.RearGuard:
                    return 2;
                case TacticalWithdrawalDoctrine.Decision.Screen:
                    return 1;
                default:
                    return 0;
            }
        }

        private static bool PassesWithdrawalWriteEnvelope(Regiment unit)
        {
            if (unit == null) return false;
            if (unit.isrouted || unit.markedforrout) return false;
            if (unit.permanentlydetached) return false;
            if (unit.regimentpaths > 0) return false;
            return true;
        }

        private static bool TryGetWithdrawalEndDate(AIBattle battle, out float endDate)
        {
            endDate = 0f;

            BattleUnits bunits = GetBattleUnits(battle);
            if (bunits == null) return false;

            try
            {
                if (bunits.uniStormSystem == null)
                {
                    OnceLog.Warning("b8-check-reserves-missing-unistorm", "[B8] Missing BattleUnits.uniStormSystem; skipping withdrawal writes.");
                    return false;
                }

                endDate = new Tools.Date(
                    bunits.uniStormSystem.dayCounter,
                    bunits.uniStormSystem.monthCounter,
                    bunits.year).yearfraction;
                if (endDate <= 0f)
                {
                    OnceLog.Warning("b8-check-reserves-invalid-withdrawal-date", "[B8] Invalid campaign withdrawal date; skipping withdrawal writes.");
                    return false;
                }

                return true;
            }
            catch (System.Exception ex)
            {
                OnceLog.Warning("b8-check-reserves-withdrawal-date-error", "[B8] Failed computing campaign withdrawal date: " + ex.Message);
                return false;
            }
        }

        private static BattleUnits GetBattleUnits(AIBattle battle)
        {
            try
            {
                if (battle == null) return null;

                if (_bunitsField == null)
                    _bunitsField = AccessTools.Field(typeof(AIBattle), "bunits");
                if (_bunitsField == null)
                {
                    OnceLog.Warning("b8-check-reserves-missing-bunits", "[B8] Missing AIBattle.bunits; skipping withdrawal writes.");
                    return null;
                }

                BattleUnits bunits = _bunitsField.GetValue(battle) as BattleUnits;
                if (bunits == null)
                    OnceLog.Warning("b8-check-reserves-null-bunits", "[B8] AIBattle.bunits was null; skipping withdrawal writes.");
                return bunits;
            }
            catch (System.Exception ex)
            {
                OnceLog.Warning("b8-check-reserves-bunits-error", "[B8] Failed reading AIBattle.bunits: " + ex.Message);
                return null;
            }
        }

        private static int? IsPlayerAiOrFeud(AIBattle battle)
        {
            try
            {
                if (battle == null) return null;

                if (_isPlayerAiOrFeudField == null)
                    _isPlayerAiOrFeudField = AccessTools.Field(typeof(AIBattle), "isplayeraiorfeud");
                if (_isPlayerAiOrFeudField == null)
                {
                    OnceLog.Warning("b8-check-reserves-missing-isplayeraiorfeud", "[B8] Missing AIBattle.isplayeraiorfeud; skipping withdrawal doctrine.");
                    return null;
                }

                object value = _isPlayerAiOrFeudField.GetValue(battle);
                if (value is int result) return result;

                OnceLog.Warning("b8-check-reserves-invalid-isplayeraiorfeud", "[B8] AIBattle.isplayeraiorfeud was not an int; skipping withdrawal doctrine.");
                return null;
            }
            catch (System.Exception ex)
            {
                OnceLog.Warning("b8-check-reserves-isplayeraiorfeud-error", "[B8] Failed reading AIBattle.isplayeraiorfeud: " + ex.Message);
                return null;
            }
        }

        private static bool HasPerformAiActionDlcWl()
        {
            if (_performAiActionDlcWlMethod == null)
            {
                _performAiActionDlcWlMethod = AccessTools.Method(
                    typeof(AIBattle),
                    "PerformAIActionDLCWL",
                    new[] { typeof(Regiment), typeof(Regiment) });
            }
            if (_performAiActionDlcWlMethod != null) return true;

            OnceLog.Warning("b8-check-reserves-missing-perform-ai-action-dlcwl", "[B8] Missing AIBattle.PerformAIActionDLCWL(Regiment, Regiment); skipping withdrawal doctrine.");
            return false;
        }

        private static bool PerformAiActionDlcWl(Regiment unit, Regiment aigroup)
        {
            try
            {
                object value = _performAiActionDlcWlMethod.Invoke(null, new object[] { unit, aigroup });
                if (value is bool result) return result;

                OnceLog.Warning("b8-check-reserves-invalid-perform-ai-action-dlcwl", "[B8] AIBattle.PerformAIActionDLCWL did not return bool; skipping unit.");
                return false;
            }
            catch (System.Exception ex)
            {
                OnceLog.Warning("b8-check-reserves-perform-ai-action-dlcwl-error", "[B8] Failed invoking AIBattle.PerformAIActionDLCWL: " + ex.Message);
                return false;
            }
        }
    }
}
