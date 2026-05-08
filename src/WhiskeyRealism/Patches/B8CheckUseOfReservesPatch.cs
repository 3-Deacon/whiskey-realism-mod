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
        private static FieldInfo _isPlayerAiOrFeudField;

        [HarmonyPostfix]
        public static void Postfix(AIBattle __instance, Regiment aigroup)
        {
            if (aigroup == null) return;

            try
            {
                OnceLog.Info("b8-check-reserves", "B8 CheckUseOfReserves Postfix wired.");

                var allUnits = aigroup.allattachedunits;
                if (allUnits == null) return;

                int? isPlayerAiOrFeud = IsPlayerAiOrFeud(__instance);
                if (!isPlayerAiOrFeud.HasValue) return;

                bool writesEnabled = Plugin.EnableTacticalWithdrawalDoctrine != null
                    && Plugin.EnableTacticalWithdrawalDoctrine.Value;

                List<Regiment> withdrawalList = null;

                for (int i = 0; i < allUnits.Length; i++)
                {
                    var unit = allUnits[i];
                    if (unit == null) continue;
                    if (unit.unittyp > TacticalUnitType.Cavalry) continue;
                    if (unit.isrouted || unit.markedforrout) continue;
                    if (unit.permanentlydetached) continue;
                    if (!TacticalGateHelpers.PassesWlOwnership(aigroup.ai_feudstance, isPlayerAiOrFeud.Value))
                        continue;

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

                    EmitHelpRequest(aigroup, decision);

                    if (writesEnabled
                        && (decision == TacticalWithdrawalDoctrine.Decision.RearGuard
                            || decision == TacticalWithdrawalDoctrine.Decision.FullRetreat))
                    {
                        if (withdrawalList == null) withdrawalList = new List<Regiment>();
                        withdrawalList.Add(unit);
                    }
                }

                if (withdrawalList != null && withdrawalList.Count > 0)
                {
                    float endDate = GameVars.currenttimefromstart + 600f;
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
    }
}
