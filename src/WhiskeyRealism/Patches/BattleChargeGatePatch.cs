using System;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
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
                                    screenRoutedTargetVisible);
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
                if (group == null || group.unitrange == null || group.unitrange.enemystrengthwithinangle == null)
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
            OnceLog.Info(
                "tactical-orchestrator-charge-gate:deny:" + SafeName(unit),
                "[TacticalOrchestratorChargeGate] action=deny" +
                " role=" + decision.Role +
                " reason=" + decision.Reason +
                " resolutionReason=" + context.Resolution.Reason +
                " primarySector=" + SafePrimarySector(context.Resolution) +
                " localOdds=" + context.LocalOdds +
                " mainEffortSupportAvailable=" + context.MainEffortSupportAvailable +
                " screenRoutedTargetVisible=" + screenRoutedTargetVisible +
                " unit=" + SafeName(unit) + "#" + SafeInstanceId(unit) +
                " group=" + SafeName(group) + "#" + SafeInstanceId(group));
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
