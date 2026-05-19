using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // B1 W&L guard and Slice 3 orchestrator charge gate for
    // AIBattle.MicroAICheckForCharges. Vanilla owns charge initiation and
    // cancellation in one small method; this Prefix mirrors that body so charge
    // initiation can be blocked without skipping the cancellation branch.
    [HarmonyPatch(typeof(AIBattle), "MicroAICheckForCharges")]
    internal static class BattleChargeGatePatch
    {
        private static readonly object TelemetryGate = new object();
        private static readonly HashSet<string> _typedDenyTelemetryKeys = new HashSet<string>();
        private static FieldInfo _bunitsField;
        private static FieldInfo _isPlayerAiOrFeudField;
        private static bool _missingRequiredAnchorLogged;

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        internal static bool Prefix(AIBattle __instance, Regiment aigroup, int restrictunittypes)
        {
            using (TelemetryPerf.Scope("tactical.patch.battle-charge-gate", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
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
                bool orchestratorContextInitialized = false;
                OrchestratorChargeContext orchestratorContext = default;
                bool doctrineContextInitialized = false;
                bool hasDoctrineOrder = false;
                CommandDoctrineOrder doctrineOrder = default(CommandDoctrineOrder);
                BattlefieldPictureSnapshot doctrinePicture = new BattlefieldPictureSnapshot(Array.Empty<BattlefieldObjectiveEstimate>());

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
                            aigroup.lastfeudactiontime = CurrentBattleHour(bunits);

                            if (!doctrineContextInitialized)
                            {
                                hasDoctrineOrder = TryResolveDoctrineOrder(aigroup, out doctrineOrder, out doctrinePicture);
                                doctrineContextInitialized = true;
                            }

                            bool screenRoutedTargetVisible = ScreenRoutedChargeTargetVisible(unit);
                            DoctrineChargeDecision doctrineDecision = new DoctrineChargeDecision(DoctrineConsumerAction.Observe, "no-doctrine-order");
                            if (hasDoctrineOrder)
                            {
                                float localOdds = LocalOdds(aigroup);
                                doctrineDecision = DoctrineConsumerDecisions.DecideCharge(
                                    doctrineOrder,
                                    DoctrineConsumerDecisions.EnemyMainLineExposed(doctrineOrder, doctrinePicture),
                                    localOdds,
                                    screenRoutedTargetVisible,
                                    BuildEnduranceDecision(aigroup),
                                    BuildMeleeFearDecision(aigroup, unit, doctrineOrder),
                                    BuildInfantryFireDecision(aigroup, unit, doctrineOrder));
                                if (doctrineDecision.Action == DoctrineConsumerAction.Deny)
                                {
                                    LogDeniedDoctrine(unit, aigroup, doctrineOrder, doctrineDecision, localOdds, screenRoutedTargetVisible);
                                    continue;
                                }
                            }

                            if (!orchestratorContextInitialized)
                            {
                                orchestratorContext = BuildOrchestratorChargeContext(aigroup);
                                orchestratorContextInitialized = true;
                            }

                            TacticalOrchestratorChargeGate.Decision orchestratorDecision =
                                DecideOrchestratorCharge(orchestratorContext, screenRoutedTargetVisible);
                            bool orchestratorDenied = orchestratorDecision.Action == TacticalOrchestratorChargeGate.Action.Deny;
                            if (!DoctrineConsumerDecisions.AllowsChargeAfterAuthoritativeGate(doctrineDecision, orchestratorDenied))
                            {
                                LogDeniedOrchestrator(unit, aigroup, orchestratorDecision, orchestratorContext, screenRoutedTargetVisible);
                                continue;
                            }

                            bool b6cDenied = TryB6cDeny(unit, aigroup);
                            if (!DoctrineConsumerDecisions.AllowsChargeAfterAuthoritativeGate(doctrineDecision, b6cDenied)) continue;

                            unit.SetMovementMode(3);
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
            } // end using TelemetryPerf.Scope
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled.Value &&
                (Plugin.Instance.EnableWlTacticalChargeGuard.Value ||
                    Plugin.Instance.EnableTacticalChargeDenial.Value ||
                    Plugin.Instance.EnableTacticalOrchestratorChargeGate.Value ||
                    Plugin.Instance.TacticalOperationsLedgerAllowsWrites);
        }

        private static bool LocalReactionProducerEnabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.EnableTacticalObserver.Value &&
                Plugin.Instance.EnableTacticalCommanderIntentDoctrine.Value &&
                Plugin.Instance.EnableTacticalLocalReactionDoctrine.Value;
        }

        private static bool TryB6cDeny(Regiment unit, Regiment group)
        {
            if (!Plugin.Instance.EnableTacticalChargeDenial.Value || !LocalReactionProducerEnabled())
                return false;

            TacticalLocalReactionDecision reaction = TacticalReactionContext.Shared.GetReaction(SafeInstanceId(group));
            if (!IsExplicitChargeDenial(reaction)) return false;

            LogDeniedB6c(unit, group, reaction);
            return true;
        }

        private static OrchestratorChargeContext BuildOrchestratorChargeContext(Regiment group)
        {
            if (Plugin.Instance == null || !Plugin.Instance.EnableTacticalOrchestratorChargeGate.Value)
            {
                return OrchestratorChargeContext.Disabled;
            }

            CommandIntentResolution resolution = ResolveIntent(group);
            bool playerControlled = HasPlayerOwnership(group);
            float localOdds = LocalOdds(group);
            bool mainEffortSupportAvailable = MainEffortSupportAvailable(group, resolution);

            return new OrchestratorChargeContext(
                true,
                resolution,
                playerControlled,
                localOdds,
                mainEffortSupportAvailable);
        }

        private static TacticalOrchestratorChargeGate.Decision DecideOrchestratorCharge(
            OrchestratorChargeContext context,
            bool screenRoutedTargetVisible)
        {
            if (!context.Enabled)
            {
                return new TacticalOrchestratorChargeGate.Decision(
                    TacticalOrchestratorChargeGate.Action.Allow,
                    DirectChildRole.Unknown,
                    "orchestrator-charge-gate-disabled");
            }

            return TacticalOrchestratorChargeGate.Decide(
                new TacticalOrchestratorChargeGate.Input(
                    vanillaWouldCharge: true,
                    chargeCancellation: false,
                    resolution: context.Resolution,
                    playerControlled: context.PlayerControlled,
                    localOdds: context.LocalOdds,
                    mainEffortSupportAvailable: context.MainEffortSupportAvailable,
                    screenRoutedTargetVisible: screenRoutedTargetVisible));
        }

        private static CommandIntentResolution ResolveIntent(Regiment group)
        {
            try
            {
                if (group == null)
                    return new CommandIntentResolution(false, default, "no-group");

                TacticalBattleOrchestrator side = TacticalBattleCoordinator.GetSideOrchestrator(group.alliance);
                if (side == null || side.Army == null)
                    return new CommandIntentResolution(false, default, "no-side-orchestrator");

                return side.Army.ResolveCommandIntentForGroup(
                    TacticalPatchIds.ComponentInstanceId(group),
                    TacticalPatchIds.GameObjectInstanceId(group));
            }
            catch (Exception ex)
            {
                return new CommandIntentResolution(false, default, "resolve-error:" + ex.GetType().Name);
            }
        }

        private static bool HasPlayerOwnership(Regiment group)
        {
            try
            {
                if (group == null) return true;
                if (!SafeAiVsAi() && group.alliance == SafePlayerAlliance()) return true;
                if (group.dlcw_isundercommander) return true;
                if (group.allattachedunits == null) return false;

                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    Regiment attached = group.allattachedunits[i];
                    if (attached != null && attached.dlcw_isundercommander) return true;
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        private static float LocalOdds(Regiment group)
        {
            try
            {
                if (group == null) return 1f;
                float own = Math.Max(Sanitize(group.groupowninrange), Sanitize(group.groupstrengthaigroup));
                float enemy = Math.Max(Sanitize(group.groupenemiesinrange), SumEnemyStrengthWithinAngle(group));
                return enemy <= 0f ? 1f : own / Math.Max(1f, enemy);
            }
            catch
            {
                return 1f;
            }
        }

        private static float SumEnemyStrengthWithinAngle(Regiment group)
        {
            try
            {
                if (group == null ||
                    group.unitrange == null ||
                    group.unitrange.enemystrengthwithinangle == null ||
                    !TacticalFogOfWarContact.HasVisibleEnemy(group))
                    return 0f;

                float total = 0f;
                for (int i = 0; i < group.unitrange.enemystrengthwithinangle.Length; i++)
                    total += Math.Max(0f, group.unitrange.enemystrengthwithinangle[i]);
                return total;
            }
            catch
            {
                return 0f;
            }
        }

        private static bool MainEffortSupportAvailable(Regiment group, CommandIntentResolution resolution)
        {
            try
            {
                if (group == null || !resolution.Found) return false;
                TacticalBattleOrchestrator side = TacticalBattleCoordinator.GetSideOrchestrator(group.alliance);
                if (side == null || side.Army == null || side.Army.CurrentCommandNodeIntents == null) return false;

                int sector = resolution.Intent.PrimarySector;
                var intents = side.Army.CurrentCommandNodeIntents;
                for (int i = 0; i < intents.Count; i++)
                {
                    CommandNodeIntent intent = intents[i];
                    if (intent.Role != DirectChildRole.Main) continue;
                    if (Math.Abs(intent.PrimarySector - sector) <= 1)
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static TacticalEnduranceDecision BuildEnduranceDecision(Regiment group)
        {
            return TacticalEnduranceGate.Evaluate(new TacticalEnduranceInput(
                infantryAmmo01: AverageAmmoRatio(group, artilleryOnly: false),
                artilleryAmmo01: AverageAmmoRatio(group, artilleryOnly: true),
                fatigue01: AverageFatigue(group),
                morale01: GroupMoraleRatio(group),
                casualtyPressure01: CasualtyPressure(group)));
        }

        private static TacticalMeleeFearDecision BuildMeleeFearDecision(
            Regiment group,
            Regiment unit,
            CommandDoctrineOrder order)
        {
            try
            {
                Regiment target = ClosestChargeTarget(unit) ?? ClosestChargeTarget(group);
                if (group == null || target == null) return TacticalMeleeFearDecision.NoOpinion;

                float ownMen = Math.Max(Sanitize(group.groupstrengthactive), Sanitize(unit != null ? unit.strength : 0f));
                float enemyMen = Math.Max(Sanitize(target.groupstrengthactive), Sanitize(target.strength));
                if (ownMen <= 0f || enemyMen <= 0f) return TacticalMeleeFearDecision.NoOpinion;

                float ownY = SafePosition(group).y;
                float enemyY = SafePosition(target).y;
                float ownHighGround = ownY > enemyY + 3f ? Clamp01((ownY - enemyY) / 20f) : 0f;
                float enemyHighGround = enemyY > ownY + 3f ? Clamp01((enemyY - ownY) / 20f) : 0f;
                float flanking = EstimateFlankPressure(group, target);

                return TacticalMeleeFearDoctrine.Decide(new TacticalMeleeFearInput(
                    ownKind: CloseCombatKind(unit ?? group),
                    enemyKind: CloseCombatKind(target),
                    ownMen: ownMen,
                    enemyMen: enemyMen,
                    ownMorale01: GroupMoraleRatio(group),
                    enemyMorale01: GroupMoraleRatio(target),
                    leaderAggression01: order.Sop.RiskBudget01,
                    flanking01: flanking,
                    rearThreat01: flanking > 0.80f ? 0.50f : 0f,
                    ownHighGround01: ownHighGround,
                    enemyHighGround01: enemyHighGround,
                    enemyDefensiveTerrain01: 0f,
                    ownUnderFire01: ReceivedFire(unit ?? group, target) ? 0.65f : 0f,
                    enemyUnderFire01: ReceivedFire(target, unit ?? group) ? 0.65f : 0f,
                    ownChargingOrRunning: true,
                    targetRouted: target.isrouted || target.markedforrout));
            }
            catch
            {
                return TacticalMeleeFearDecision.NoOpinion;
            }
        }

        private static TacticalInfantryFireDecision BuildInfantryFireDecision(
            Regiment group,
            Regiment unit,
            CommandDoctrineOrder order)
        {
            try
            {
                Regiment firingUnit = unit ?? group;
                if (firingUnit == null) return TacticalInfantryFireDecision.NoOpinion;
                if (firingUnit.unittyp != TacticalUnitType.Infantry &&
                    firingUnit.unittyp != TacticalUnitType.Skirmisher)
                {
                    return TacticalInfantryFireDecision.NoOpinion;
                }

                Regiment target = ClosestChargeTarget(firingUnit) ?? ClosestChargeTarget(group);
                if (target == null) return TacticalInfantryFireDecision.NoOpinion;

                float distance = Vector3.Distance(SafePosition(firingUnit), SafePosition(target));
                TacticalFireControlDecision decision = TacticalFireControlDoctrine.Decide(new TacticalFireControlInput(
                    firingUnit.unittyp,
                    mounted: false,
                    order.Task,
                    order.Role,
                    hasTarget: distance > 0f,
                    targetDistance: distance,
                    effectiveFireRange: SafeEffectiveFireRange(firingUnit),
                    ammoRatio01: UnitAmmoRatio(firingUnit),
                    morale01: GroupMoraleRatio(firingUnit),
                    fatigue01: Clamp01(firingUnit.fatigue),
                    inCover: UnitInCover(firingUnit),
                    enemyAdvancing: target.regimentpaths > 0 || target.movementmode > 0,
                    friendlyBlockedAhead: false,
                    alignedToTarget: IsAlignedToTarget(firingUnit, target),
                    loadedVolley: UnitLoadedVolley(firingUnit),
                    currentChargeOrdered: false,
                    currentCombatBehaviorOrdered: firingUnit.combatbehaviorordered));

                return decision.FireDiscipline;
            }
            catch
            {
                return TacticalInfantryFireDecision.NoOpinion;
            }
        }

        private static Regiment ClosestChargeTarget(Regiment unit)
        {
            try
            {
                if (unit == null || unit.unitrange == null) return null;
                return TacticalFogOfWarContact.ClosestVisibleEnemy(unit);
            }
            catch
            {
                return null;
            }
        }

        private static TacticalCloseCombatKind CloseCombatKind(Regiment unit)
        {
            try
            {
                if (unit == null) return TacticalCloseCombatKind.Infantry;
                if (unit.unittyp == TacticalUnitType.Cavalry) return TacticalCloseCombatKind.Cavalry;
                if (unit.unittyp == TacticalUnitType.Artillery) return TacticalCloseCombatKind.Artillery;
                if (unit.unittyp == TacticalUnitType.Skirmisher) return TacticalCloseCombatKind.Skirmisher;
                return TacticalCloseCombatKind.Infantry;
            }
            catch
            {
                return TacticalCloseCombatKind.Infantry;
            }
        }

        private static float EstimateFlankPressure(Regiment attacker, Regiment defender)
        {
            try
            {
                if (attacker == null || defender == null) return 0f;
                Vector3 toAttacker = attacker.transform.position - defender.transform.position;
                toAttacker.y = 0f;
                if (toAttacker.sqrMagnitude < 0.01f) return 0f;
                toAttacker.Normalize();
                Vector3 forward = defender.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.01f) return 0f;
                forward.Normalize();
                float dot = Vector3.Dot(forward, toAttacker);
                if (dot < -0.50f) return 1f;
                if (Math.Abs(dot) < 0.35f) return 0.65f;
                return 0f;
            }
            catch
            {
                return 0f;
            }
        }

        private static bool ReceivedFire(Regiment unit, Regiment source)
        {
            try
            {
                return unit != null && source != null &&
                    (unit.ReceivedFireFromUnit(source) || unit.CheckReceivedFireOtherUnit(source));
            }
            catch
            {
                return false;
            }
        }

        private static Vector3 SafePosition(Regiment unit)
        {
            try { return unit != null ? unit.transform.position : default(Vector3); }
            catch { return default(Vector3); }
        }

        private static float SafeEffectiveFireRange(Regiment unit)
        {
            try
            {
                if (unit == null) return 0f;
                float value = unit.GetFireRange(true);
                if (!float.IsNaN(value) && !float.IsInfinity(value) && value > 0f)
                    return value;
                value = unit.firerange;
                return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f ? value : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        private static float UnitAmmoRatio(Regiment unit)
        {
            try
            {
                if (unit == null || unit.ammo == null || unit.ammo.Length == 0) return 1f;
                float total = 0f;
                int count = 0;
                for (int i = 0; i < unit.ammo.Length; i++)
                {
                    float value = unit.ammo[i];
                    if (float.IsNaN(value) || float.IsInfinity(value)) continue;
                    total += Math.Max(0f, value);
                    count++;
                }

                return count > 0 ? Clamp01(total / count) : 1f;
            }
            catch
            {
                return 1f;
            }
        }

        private static bool UnitInCover(Regiment unit)
        {
            try
            {
                return unit != null && unit.covervalue > 0.05f && unit.coverobject != 3;
            }
            catch
            {
                return false;
            }
        }

        private static bool UnitLoadedVolley(Regiment unit)
        {
            try
            {
                if (unit == null) return false;
                if (unit.allspritesreloaded) return true;
                return GameVars.currenttimefromstart - unit.lastfiredshottime > 0.05f;
            }
            catch
            {
                return true;
            }
        }

        private static bool IsAlignedToTarget(Regiment unit, Regiment target)
        {
            try
            {
                if (unit == null || target == null) return true;
                Vector3 own = SafePosition(unit);
                Vector3 enemy = SafePosition(target);
                Vector3 direction = enemy - own;
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.01f) return true;
                direction.Normalize();

                Vector3 forward = unit.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.01f) return true;
                forward.Normalize();

                return Vector3.Dot(forward, direction) >= 0.25f;
            }
            catch
            {
                return true;
            }
        }

        private static float AverageAmmoRatio(Regiment group, bool artilleryOnly)
        {
            try
            {
                if (group == null || group.allattachedunits == null || group.allattachedunits.Length == 0)
                    return 1f;

                float total = 0f;
                int count = 0;
                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    Regiment unit = group.allattachedunits[i];
                    if (unit == null || unit.ammo == null || unit.ammo.Length == 0) continue;
                    bool artillery = unit.unittyp == TacticalUnitType.Artillery;
                    if (artilleryOnly != artillery) continue;
                    if (unit.isrouted || unit.markedforrout || unit.permanentlydetached) continue;

                    float unitTotal = 0f;
                    for (int slot = 0; slot < unit.ammo.Length; slot++)
                        unitTotal += Math.Max(0f, unit.ammo[slot]);
                    total += Clamp01(unitTotal / unit.ammo.Length);
                    count++;
                }

                return count > 0 ? Clamp01(total / count) : 1f;
            }
            catch
            {
                return 1f;
            }
        }

        private static float AverageFatigue(Regiment group)
        {
            try
            {
                if (group == null || group.allattachedunits == null || group.allattachedunits.Length == 0)
                    return group != null ? Clamp01(group.fatigue) : 0f;

                float total = 0f;
                int count = 0;
                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    Regiment unit = group.allattachedunits[i];
                    if (unit == null || unit.isrouted || unit.markedforrout || unit.permanentlydetached) continue;
                    if (unit.unittyp > TacticalUnitType.Cavalry) continue;
                    total += Clamp01(unit.fatigue);
                    count++;
                }

                return count > 0 ? Clamp01(total / count) : Clamp01(group.fatigue);
            }
            catch
            {
                return 0f;
            }
        }

        private static float GroupMoraleRatio(Regiment group)
        {
            try
            {
                if (group == null) return 1f;
                if (group.battlestartmorale > 0f)
                    return Clamp01(group.morale / Math.Max(0.01f, group.battlestartmorale));
                return Clamp01(group.morale);
            }
            catch
            {
                return 1f;
            }
        }

        private static float CasualtyPressure(Regiment group)
        {
            try
            {
                if (group == null) return 0f;
                float active = Sanitize(group.groupstrengthactive);
                float total = Math.Max(active, Sanitize(group.groupstrength));
                if (total <= 0f) return 0f;
                return Clamp01(1f - (active / Math.Max(1f, total)));
            }
            catch
            {
                return 0f;
            }
        }

        private static bool ScreenRoutedChargeTargetVisible(Regiment unit)
        {
            try
            {
                if (unit == null || unit.unitrange == null || unit.unitrange.enemyinrangereg == null)
                    return false;
                if (unit.unitrange.enemyinrangereg.Count <= 0)
                    return false;

                Regiment target = unit.unitrange.enemyinrangereg[0];
                return target != null && target.isrouted;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryResolveDoctrineOrder(
            Regiment group,
            out CommandDoctrineOrder order,
            out BattlefieldPictureSnapshot picture)
        {
            order = default(CommandDoctrineOrder);
            picture = new BattlefieldPictureSnapshot(Array.Empty<BattlefieldObjectiveEstimate>());
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
                    if (side?.OperationsLedger != null)
                        picture = side.OperationsLedger.CurrentBattlefieldPicture;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static int SafePlayerAlliance()
        {
            try { return GameVars.playeralliance; }
            catch { return -99; }
        }

        private static bool SafeAiVsAi()
        {
            try { return GameVars.ai_vs_ai; }
            catch { return false; }
        }

        private static float Sanitize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return Math.Max(0f, value);
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static bool IsExplicitChargeDenial(TacticalLocalReactionDecision reaction)
        {
            return reaction.Reason != "no-decision" &&
                reaction.Reaction == LocalReaction.DenyCharge;
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

        private static void LogDeniedB6c(Regiment unit, Regiment group, TacticalLocalReactionDecision reaction)
        {
            OnceLog.Info("tactical-charge-deny:movement:" + SafeName(unit), "[TacticalChargeDeny] surface=movement action=deny" +
                " unit=" + SafeName(unit) + "#" + SafeInstanceId(unit) +
                " group=" + SafeName(group) + "#" + SafeInstanceId(group) +
                " reaction=" + reaction.Reaction +
                " reason=" + reaction.Reason);
        }

        private static void LogDeniedDoctrine(
            Regiment unit,
            Regiment group,
            CommandDoctrineOrder order,
            DoctrineChargeDecision decision,
            float localOdds,
            bool targetRouted)
        {
            OnceLog.Info(
                "tactical-doctrine-charge:deny:" + SafeName(unit),
                "[TacticalDoctrineCharge] action=deny" +
                " task=" + order.Task +
                " reason=" + decision.Reason +
                " localOdds=" + localOdds +
                " targetRouted=" + targetRouted +
                " unit=" + SafeName(unit) + "#" + SafeInstanceId(unit) +
                " group=" + SafeName(group) + "#" + SafeInstanceId(group));
        }

        private static void LogDeniedOrchestrator(
            Regiment unit,
            Regiment group,
            TacticalOrchestratorChargeGate.Decision decision,
            OrchestratorChargeContext context,
            bool screenRoutedTargetVisible)
        {
            OnceLog.Info("tactical-orchestrator-charge-gate", "BattleChargeGatePatch orchestrator branch wired");
            string inputSignature = TacticalTelemetry.DecisionInputSignature(
                "TacticalOrchestratorChargeGate",
                SafeInstanceId(unit),
                SafeInstanceId(group),
                decision.Role,
                context.Resolution.Reason,
                SafePrimarySector(context.Resolution),
                context.LocalOdds,
                context.MainEffortSupportAvailable,
                screenRoutedTargetVisible);
            if (!MarkTypedDenyTelemetryOnce("orchestrator:" + SafeInstanceId(unit)))
                return;

            TelemetryRouter.Emit(
                TelemetryLayer.Tactical,
                TelemetryCategory.Gate,
                "TacticalOrchestratorChargeGate",
                TelemetrySeverity.Info,
                ev => ev
                    .WithUnit(SafeName(unit))
                    .WithDecision("deny", decision.Reason, inputSignature)
                    .WithField("confidence", 1.0)
                    .WithField("score", context.LocalOdds)
                    .WithField("selectedTarget", "primarySector=" + SafePrimarySector(context.Resolution))
                    .WithField("gateResult", "deny")
                    .WithField("gateReason", decision.Reason)
                    .WithField("writeAction", "MicroAICheckForCharges")
                    .WithField("writeResult", "suppressed")
                    .WithField("role", decision.Role.ToString())
                    .WithField("resolutionReason", context.Resolution.Reason)
                    .WithField("primarySector", SafePrimarySector(context.Resolution))
                    .WithField("localOdds", context.LocalOdds)
                    .WithField("mainEffortSupportAvailable", context.MainEffortSupportAvailable)
                    .WithField("screenRoutedTargetVisible", screenRoutedTargetVisible)
                    .WithField("unit", SafeName(unit) + "#" + SafeInstanceId(unit))
                    .WithField("group", SafeName(group) + "#" + SafeInstanceId(group)));
        }

        private static bool MarkTypedDenyTelemetryOnce(string key)
        {
            try
            {
                lock (TelemetryGate)
                {
                    if (_typedDenyTelemetryKeys.Contains(key))
                        return false;

                    _typedDenyTelemetryKeys.Add(key);
                    return true;
                }
            }
            catch
            {
                return true;
            }
        }

        private static int SafePrimarySector(CommandIntentResolution resolution)
        {
            return resolution.Found ? resolution.Intent.PrimarySector : -1;
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

        private static int SafeInstanceId(UnityEngine.Object obj)
        {
            try
            {
                return obj != null ? obj.GetInstanceID() : 0;
            }
            catch
            {
                return 0;
            }
        }

        private readonly struct OrchestratorChargeContext
        {
            public static readonly OrchestratorChargeContext Disabled =
                new OrchestratorChargeContext(
                    false,
                    new CommandIntentResolution(false, default, "orchestrator-charge-gate-disabled"),
                    false,
                    1f,
                    false);

            public OrchestratorChargeContext(
                bool enabled,
                CommandIntentResolution resolution,
                bool playerControlled,
                float localOdds,
                bool mainEffortSupportAvailable)
            {
                Enabled = enabled;
                Resolution = resolution;
                PlayerControlled = playerControlled;
                LocalOdds = localOdds;
                MainEffortSupportAvailable = mainEffortSupportAvailable;
            }

            public bool Enabled { get; }
            public CommandIntentResolution Resolution { get; }
            public bool PlayerControlled { get; }
            public float LocalOdds { get; }
            public bool MainEffortSupportAvailable { get; }
        }
    }
}
