using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
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

        internal sealed class ReserveMovementState
        {
            public BattleReserveCommitGatePatch.UnitState[] Units;
            public DoctrineReserveDecision DoctrineDecision;
        }

        [HarmonyPrefix]
        internal static void Prefix(Regiment aigroup, out ReserveMovementState __state)
        {
            __state = null;

            if (Plugin.Instance == null || Plugin.Instance.Enabled == null || !Plugin.Instance.Enabled.Value) return;
            if (aigroup == null) return;

            try
            {
                if (!TryResolveDoctrineOrder(aigroup, out CommandDoctrineOrder doctrineOrder)) return;

                DoctrineReserveDecision doctrineDecision = DoctrineConsumerDecisions.DecideReserve(
                    doctrineOrder,
                    LocalOdds(aigroup),
                    ReserveFraction(aigroup),
                    SafeCurrentTimeSeconds());
                if (doctrineDecision.Action != DoctrineConsumerAction.Deny) return;

                __state = new ReserveMovementState
                {
                    DoctrineDecision = doctrineDecision,
                    Units = SnapshotUnits(aigroup)
                };
            }
            catch
            {
                __state = null;
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(AIBattle __instance, Regiment aigroup, ReserveMovementState __state)
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

                if (__state != null && __state.DoctrineDecision.Action == DoctrineConsumerAction.Deny)
                {
                    RemoveDeniedReserveMovement(aigroup, __state);
                    EmitHelpRequest(aigroup, TacticalWithdrawalDoctrine.Decision.HoldLine);
                    OnceLog.Info(
                        "b8-check-reserves:doctrine-deny:" + SafeInstanceId(aigroup),
                        "[B8] reserve doctrine denied reserve mutation reason=" + __state.DoctrineDecision.Reason +
                        " task=" + __state.DoctrineDecision.Task +
                        " group=" + SafeName(aigroup));
                    return;
                }

                if (TryResolveDoctrineOrder(aigroup, out CommandDoctrineOrder doctrineOrder))
                {
                    DoctrineReserveDecision doctrineDecision = DoctrineConsumerDecisions.DecideReserve(
                        doctrineOrder,
                        LocalOdds(aigroup),
                        ReserveFraction(aigroup),
                        SafeCurrentTimeSeconds());
                    if (doctrineDecision.Action == DoctrineConsumerAction.Deny)
                    {
                        RemoveDeniedReserveMovement(aigroup, __state);
                        EmitHelpRequest(aigroup, TacticalWithdrawalDoctrine.Decision.HoldLine);
                        OnceLog.Info(
                            "b8-check-reserves:doctrine-deny:" + SafeInstanceId(aigroup),
                            "[B8] reserve doctrine denied reserve mutation reason=" + doctrineDecision.Reason +
                            " task=" + doctrineDecision.Task +
                            " group=" + SafeName(aigroup));
                        return;
                    }
                }

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

        private static BattleReserveCommitGatePatch.UnitState[] SnapshotUnits(Regiment group)
        {
            try
            {
                if (group == null || group.allattachedunits == null)
                    return System.Array.Empty<BattleReserveCommitGatePatch.UnitState>();

                BattleReserveCommitGatePatch.UnitState[] units =
                    new BattleReserveCommitGatePatch.UnitState[group.allattachedunits.Length];
                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    Regiment unit = group.allattachedunits[i];
                    units[i] = BattleReserveCommitGatePatch.SnapshotUnit(unit);
                }

                return units;
            }
            catch
            {
                return System.Array.Empty<BattleReserveCommitGatePatch.UnitState>();
            }
        }

        private static void RemoveDeniedReserveMovement(Regiment aigroup, ReserveMovementState state)
        {
            try
            {
                if (state == null || state.Units == null) return;

                for (int i = 0; i < state.Units.Length; i++)
                {
                    BattleReserveCommitGatePatch.UnitState before = state.Units[i];
                    Regiment unit = before.Unit;
                    if (unit == null) continue;

                    int afterPaths = SafePathCount(unit);
                    int afterQueue = SafeQueueCount(unit);
                    if (!TacticalReservePolicyLedger.ShouldRemoveDeniedReserveMovement(
                        doctrineDeny: true,
                        beforePaths: before.Paths,
                        afterPaths: afterPaths,
                        beforeQueueCount: before.QueueCount,
                        afterQueueCount: afterQueue,
                        useOrderDelays: SafeUseOrderDelays()))
                    {
                        continue;
                    }

                    BattleReserveCommitGatePatch.RemoveAddedPaths(unit, before.Paths, afterPaths);
                    BattleReserveCommitGatePatch.RestoreMovementState(unit, before);
                    OnceLog.Info(
                        "b8-check-reserves:doctrine-remove:" + SafeInstanceId(unit),
                        "[B8] reserve doctrine removed vanilla direct path reason=" +
                        state.DoctrineDecision.Reason +
                        " unit=" + SafeName(unit) +
                        " group=" + SafeName(aigroup) +
                        " paths=" + before.Paths + "->" + afterPaths +
                        " queue=" + before.QueueCount + "->" + afterQueue);
                }
            }
            catch (System.Exception ex)
            {
                OnceLog.Warning(
                    "b8-check-reserves-doctrine-remove-error",
                    "[B8] reserve doctrine path removal skipped: " + ex.Message);
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

        private static bool TryResolveDoctrineOrder(Regiment group, out CommandDoctrineOrder order)
        {
            order = default(CommandDoctrineOrder);

            try
            {
                if (group == null) return false;
                TacticalBattleOrchestrator side = TacticalBattleCoordinator.GetSideOrchestrator(group.alliance);
                ArmyOrchestrator army = side?.Army;
                var orders = army?.CurrentDoctrineOrders;
                if (orders == null || orders.Count == 0) return false;

                int componentInstanceId = TacticalPatchIds.ComponentInstanceId(group);
                int gameObjectInstanceId = TacticalPatchIds.GameObjectInstanceId(group);
                for (int i = 0; i < orders.Count; i++)
                {
                    CommandDoctrineOrder candidate = orders[i];
                    if (!TacticalPatchIds.NodeIdMatches(candidate.NodeId, gameObjectInstanceId, componentInstanceId))
                        continue;
                    if (!candidate.HasPurpose) return false;

                    order = candidate;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static float LocalOdds(Regiment group)
        {
            try
            {
                if (group == null) return 1f;
                float own = System.Math.Max(Sanitize(group.groupowninrange), Sanitize(group.groupstrengthaigroup));
                float enemy = System.Math.Max(Sanitize(group.groupenemiesinrange), SumEnemyStrengthWithinAngle(group));
                if (enemy <= 0f) return own > 0f ? 2f : 1f;
                return own / System.Math.Max(1f, enemy);
            }
            catch
            {
                return 1f;
            }
        }

        private static float ReserveFraction(Regiment group)
        {
            try
            {
                if (group == null || group.allattachedunits == null || group.allattachedunits.Length == 0)
                    return 0f;

                float total = 0f;
                float reserve = 0f;
                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    Regiment unit = group.allattachedunits[i];
                    if (unit == null) continue;
                    if (unit.isrouted || unit.markedforrout || unit.permanentlydetached) continue;
                    if (unit.unittyp > TacticalUnitType.Cavalry) continue;

                    float strength = Sanitize(unit.groupstrengthactive);
                    total += strength;
                    if (unit.regimentpaths <= 0 && !unit.inengagement)
                        reserve += strength;
                }

                if (total <= 0f) return 0f;
                return reserve / total;
            }
            catch
            {
                return 0f;
            }
        }

        private static float SumEnemyStrengthWithinAngle(Regiment group)
        {
            try
            {
                if (group == null || group.unitrange == null || group.unitrange.enemystrengthwithinangle == null)
                    return 0f;

                float total = 0f;
                for (int i = 0; i < group.unitrange.enemystrengthwithinangle.Length; i++)
                    total += System.Math.Max(0f, group.unitrange.enemystrengthwithinangle[i]);
                return total;
            }
            catch
            {
                return 0f;
            }
        }

        private static float SafeCurrentTimeSeconds()
        {
            try { return Time.realtimeSinceStartup; }
            catch { return 0f; }
        }

        private static int SafePathCount(Regiment unit)
        {
            try { return unit != null ? System.Math.Max(0, unit.regimentpaths) : 0; }
            catch { return 0; }
        }

        private static int SafeQueueCount(Regiment unit)
        {
            try { return unit != null && unit.orderqueue != null ? unit.orderqueue.Count : 0; }
            catch { return 0; }
        }

        private static bool SafeUseOrderDelays()
        {
            try { return GameVars.useorderdelays; }
            catch { return false; }
        }

        private static float Sanitize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return 0f;
            return value;
        }

        private static int SafeInstanceId(Regiment group)
        {
            try { return group != null ? group.GetInstanceID() : 0; }
            catch { return 0; }
        }

        private static string SafeName(Regiment group)
        {
            try { return group != null ? TacticalCurrentOrderSignature.Safe(group.name) : "-"; }
            catch { return "-"; }
        }
    }
}
