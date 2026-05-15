using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.PlayerOrders
{
    internal sealed class PlayerOrderRuntimeAdapter
    {
        private static FieldInfo _battleUnitsField;
        private static bool _battleUnitsFieldMissing;

        internal readonly struct RuntimeSnapshot
        {
            public RuntimeSnapshot(
                bool wlActive,
                string battleIdentity,
                string currentCommandKey,
                bool isCommanderInChief,
                PlayerOrderActiveSnapshot activeOrder,
                long tick)
            {
                WlActive = wlActive;
                BattleIdentity = battleIdentity ?? string.Empty;
                CurrentCommandKey = currentCommandKey ?? string.Empty;
                IsCommanderInChief = isCommanderInChief;
                ActiveOrder = activeOrder;
                Tick = tick;
            }

            public bool WlActive { get; }
            public string BattleIdentity { get; }
            public string CurrentCommandKey { get; }
            public bool IsCommanderInChief { get; }
            public PlayerOrderActiveSnapshot ActiveOrder { get; }
            public long Tick { get; }
        }

        internal readonly struct RuntimeEvaluation
        {
            public RuntimeEvaluation(
                bool canEvaluate,
                string skipReason,
                Regiment currentCommand,
                PlayerOrderActiveSnapshot activeOrder,
                PlayerOrderCandidate candidate,
                long tick)
            {
                CanEvaluate = canEvaluate;
                SkipReason = skipReason ?? string.Empty;
                CurrentCommand = currentCommand;
                ActiveOrder = activeOrder;
                Candidate = candidate;
                Tick = tick;
            }

            public bool CanEvaluate { get; }
            public string SkipReason { get; }
            public Regiment CurrentCommand { get; }
            public PlayerOrderActiveSnapshot ActiveOrder { get; }
            public PlayerOrderCandidate Candidate { get; }
            public long Tick { get; }
        }

        private readonly PlayerOrderDedupeState _dedupeState;

        public PlayerOrderRuntimeAdapter(PlayerOrderDedupeState dedupeState)
        {
            _dedupeState = dedupeState ?? new PlayerOrderDedupeState();
        }

        public RuntimeSnapshot Snapshot(AIBattle battle, bool forceVanillaProvenance)
        {
            try
            {
                bool wlActive = IsWlActive();
                bool isCic = IsCommanderInChief();
                Regiment current = ResolveCurrentCommand();
                string battleIdentity = BattleIdentity(battle);
                string currentKey = UnitKey(current);
                var active = ReadActiveOrder(battleIdentity, forceVanillaProvenance);
                return new RuntimeSnapshot(wlActive, battleIdentity, currentKey, isCic, active, Tick());
            }
            catch (Exception ex)
            {
                Warn("snapshot", ex);
                return new RuntimeSnapshot(false, string.Empty, string.Empty, true, default(PlayerOrderActiveSnapshot), Tick());
            }
        }

        public RuntimeEvaluation Evaluate(AIBattle battle, bool forceVanillaProvenance)
        {
            RuntimeSnapshot snapshot = Snapshot(battle, forceVanillaProvenance);
            try
            {
                if (!snapshot.WlActive) return Skip(snapshot, "wl-inactive");
                if (battle == null) return Skip(snapshot, "missing-battle");

                Regiment current = ResolveCurrentCommand();
                if (current == null) return Skip(snapshot, "missing-current-command");
                if (snapshot.IsCommanderInChief) return Skip(snapshot, "player-cic");

                string unitKey = UnitKey(current);
                if (string.IsNullOrEmpty(unitKey)) return Skip(snapshot, "invalid-unit-key");

                TacticalBattleOrchestrator side = TacticalBattleCoordinator.GetSideOrchestrator(current.alliance);
                if (side == null) return Skip(snapshot, "no-side-orchestrator");
                ArmyOrchestrator army = side.Army;
                if (army == null) return Skip(snapshot, "no-army-orchestrator");

                CommandIntentResolution resolution = ResolveCommandIntent(army, current);
                CommandTaskSnapshot task = ResolveCommandTask(army, current, resolution, side);
                PlayerOrderPoint objective = ObjectivePoint(task, resolution, current, out string objectiveSource);
                PlayerOrderPoint fallback = FallbackPoint(task, current);
                PlayerOrderPoint exit = ResolveExitPoint(battle, current, out bool hasExit);
                StrategicBattleIntentSnapshot strategicIntent = army.CurrentStrategicBattleIntent;
                string objectiveKey = !string.IsNullOrEmpty(task.ObjectiveKey)
                    ? task.ObjectiveKey
                    : objectiveSource == "visible-enemy"
                        ? "visible-enemy"
                    : !string.IsNullOrEmpty(army.CurrentOperation.PrimaryObjectiveId)
                        ? army.CurrentOperation.PrimaryObjectiveId
                        : strategicIntent.CampaignObjectiveId;

                var input = new PlayerOrderComposerInput(
                    hasSideOrchestrator: true,
                    isPlayerSubordinate: true,
                    isCommanderInChief: false,
                    unitKey: unitKey,
                    battleIdentity: snapshot.BattleIdentity,
                    givenOrderSession: snapshot.ActiveOrder.GivenOrderSession,
                    commandIntent: ToSnapshot(resolution),
                    commandTask: task,
                    doctrine: new ArmyDoctrineSnapshot(
                        army.CurrentOperation.Phase.ToString(),
                        army.CurrentOperation.Shape.ToString(),
                        objectiveKey,
                        "runtime-orchestrator:" + strategicIntent.CampaignIntent + "/" + strategicIntent.TheaterIntent),
                    unitPosition: ToPoint(current),
                    objectivePoint: objective,
                    fallbackPoint: fallback,
                    exitPoint: exit,
                    hasValidObjective: HasPoint(objective),
                    hasValidFallback: HasPoint(fallback),
                    hasValidExitPoint: hasExit);

                PlayerOrderCandidate candidate = PlayerOrderComposer.Compose(input);
                return new RuntimeEvaluation(true, string.Empty, current, snapshot.ActiveOrder, candidate, snapshot.Tick);
            }
            catch (Exception ex)
            {
                Warn("evaluate", ex);
                return Skip(snapshot, "runtime-error:" + ex.GetType().Name);
            }
        }

        public bool ActiveMatchesIssued(
            PlayerOrderCandidate candidate,
            PlayerOrderActiveSnapshot before,
            PlayerOrderActiveSnapshot active)
        {
            if (!candidate.HasCandidate || !active.HasActiveOrder) return false;
            if (active.GivenOrderSession <= before.GivenOrderSession) return false;
            if (active.VanillaType != candidate.VanillaType) return false;
            if (string.IsNullOrEmpty(active.UnitKey)) return false;
            if (Bucket(active.TargetPoint.X) != Bucket(candidate.TargetPoint.X)) return false;
            if (Bucket(active.TargetPoint.Z) != Bucket(candidate.TargetPoint.Z)) return false;
            if (Bucket(active.Rotation) != Bucket(candidate.Rotation + 180f)) return false;
            return string.Equals(active.ObjectiveKey, ExpectedDestination(candidate), StringComparison.Ordinal);
        }

        public PlayerOrderCandidate WithSession(PlayerOrderCandidate candidate, int givenOrderSession)
        {
            return new PlayerOrderCandidate(
                candidate.Scope,
                candidate.Intent,
                candidate.VanillaType,
                candidate.Priority,
                candidate.UnitKey,
                candidate.BattleIdentity,
                givenOrderSession,
                candidate.TargetPoint,
                candidate.Rotation,
                candidate.ObjectiveKey,
                candidate.Reason,
                candidate.ActiveCampaignActionable,
                candidate.CampaignGroupFlag,
                candidate.ValidExitPoint);
        }

        public PlayerOrderCandidate WithAcceptedOrder(PlayerOrderCandidate candidate, PlayerOrderActiveSnapshot active)
        {
            return new PlayerOrderCandidate(
                candidate.Scope,
                candidate.Intent,
                candidate.VanillaType,
                candidate.Priority,
                active.UnitKey,
                candidate.BattleIdentity,
                active.GivenOrderSession,
                active.TargetPoint,
                active.Rotation,
                active.ObjectiveKey,
                candidate.Reason,
                candidate.ActiveCampaignActionable,
                candidate.CampaignGroupFlag,
                candidate.ValidExitPoint || active.TargetPoint.ValidExitPoint);
        }

        private RuntimeEvaluation Skip(RuntimeSnapshot snapshot, string reason)
        {
            return new RuntimeEvaluation(
                false,
                reason,
                null,
                snapshot.ActiveOrder,
                default(PlayerOrderCandidate),
                snapshot.Tick);
        }

        private PlayerOrderActiveSnapshot ReadActiveOrder(string battleIdentity, bool forceVanillaProvenance)
        {
            try
            {
                var order = DLC_WL.givenorder;
                if (order == null)
                    return new PlayerOrderActiveSnapshot(
                        PlayerOrderScope.Tactical,
                        PlayerOrderIntent.None,
                        -1,
                        0,
                        string.Empty,
                        battleIdentity,
                        SafeGivenOrderSession(),
                        default(PlayerOrderPoint),
                        0f,
                        string.Empty,
                        "no-active-order",
                        false,
                        false,
                        PlayerOrderProvenance.Unknown);

                string unitKey = UnitKey(order.groupunit);
                var point = new PlayerOrderPoint(order.position.x, order.position.z);
                int session = SafeGivenOrderSession();
                int currentOperation = SafeCurrentOperation();
                bool activeForScene = forceVanillaProvenance ||
                    PlayerOrderVanillaScene.IsGivenOrderActiveForScene(order.type, currentOperation);
                PlayerOrderScope scope = PlayerOrderVanillaScene.ScopeForVanillaType(order.type);
                var provisional = new PlayerOrderActiveSnapshot(
                    scope,
                    PlayerOrderIntent.None,
                    order.type,
                    PlayerOrderPriority.ForActiveVanillaType(order.type, scope, PlayerOrderProvenance.Unknown),
                    unitKey,
                    battleIdentity,
                    session,
                    point,
                    order.arearotation,
                    order.destinationname,
                    activeForScene ? "active-order" : "inactive-for-scene",
                    false,
                    false,
                    PlayerOrderProvenance.Unknown,
                    battleEnded: !activeForScene,
                    stale: !activeForScene);
                var provenance = forceVanillaProvenance
                    ? PlayerOrderProvenance.Vanilla
                    : ClassifyProvenance(provisional);
                return new PlayerOrderActiveSnapshot(
                    scope,
                    PlayerOrderIntent.None,
                    order.type,
                    PlayerOrderPriority.ForActiveVanillaType(order.type, scope, provenance),
                    unitKey,
                    battleIdentity,
                    session,
                    point,
                    order.arearotation,
                    order.destinationname,
                    activeForScene ? "active-order" : "inactive-for-scene",
                    false,
                    false,
                    provenance,
                    battleEnded: !activeForScene,
                    stale: !activeForScene);
            }
            catch (Exception ex)
            {
                Warn("active", ex);
                return default(PlayerOrderActiveSnapshot);
            }
        }

        private PlayerOrderProvenance ClassifyProvenance(PlayerOrderActiveSnapshot active)
        {
            try
            {
                if (_dedupeState.TryGetShadow(active.UnitKey, out var shadow) &&
                    shadow.ActiveSignature.MatchesActiveOrder(active))
                {
                    return shadow.ActiveSignature.Scope == PlayerOrderScope.Campaign
                        ? PlayerOrderProvenance.WhiskeyCampaign
                        : PlayerOrderProvenance.WhiskeyTactical;
                }
            }
            catch { }

            return PlayerOrderProvenance.Unknown;
        }

        private static CommandIntentResolution ResolveCommandIntent(ArmyOrchestrator army, Regiment current)
        {
            try
            {
                int componentId = TacticalPatchIds.ComponentInstanceId(current);
                int gameObjectId = TacticalPatchIds.GameObjectInstanceId(current);
                return army.ResolveCommandIntentForGroup(componentId, gameObjectId);
            }
            catch (Exception ex)
            {
                Warn("resolve-intent", ex);
                return new CommandIntentResolution(false, default(CommandNodeIntent), "resolve-error");
            }
        }

        private static CommandIntentResolutionSnapshot ToSnapshot(CommandIntentResolution resolution)
        {
            return new CommandIntentResolutionSnapshot(
                resolution.Found,
                new CommandNodeIntentSnapshot(
                    resolution.Intent.NodeId,
                    resolution.Intent.SourceNodeId,
                    resolution.Intent.Role.ToString(),
                    resolution.Intent.Axis.ToString(),
                    resolution.Intent.PrimarySector,
                    resolution.Intent.SupportPriority,
                    resolution.Intent.AggressionBias01,
                    resolution.Intent.Depth),
                resolution.Reason);
        }

        private static CommandTaskSnapshot ResolveCommandTask(
            ArmyOrchestrator army,
            Regiment current,
            CommandIntentResolution resolution,
            TacticalBattleOrchestrator side)
        {
            try
            {
                CommandDoctrineOrder order;
                if (TryResolveDoctrineOrder(army, current, resolution, out order))
                {
                    return new CommandTaskSnapshot(
                        true,
                        order.Role.ToString(),
                        order.Task.ToString(),
                        ToPoint(order.PrimaryTarget.HasValue ? order.PrimaryTarget : order.SupportTarget),
                        ToPoint(order.FallbackTarget),
                        order.ObjectiveId,
                        order.Reason);
                }

                CommandNodeOperationalState state;
                if (TryResolveCommandOperation(army, current, resolution, out state))
                {
                    return new CommandTaskSnapshot(
                        true,
                        state.Role.ToString(),
                        state.Task.ToString(),
                        new PlayerOrderPoint(state.X, state.Z),
                        new PlayerOrderPoint(state.X, state.Z),
                        army.CurrentOperation.PrimaryObjectiveId,
                        "command-operation");
                }

                DirectChildIntent directChild;
                if (TryResolveDirectChildIntent(army, current, out directChild))
                {
                    return new CommandTaskSnapshot(
                        false,
                        directChild.Role.ToString(),
                        directChild.Axis.ToString(),
                        default(PlayerOrderPoint),
                        default(PlayerOrderPoint),
                        army.CurrentOperation.PrimaryObjectiveId,
                        "direct-child-intent-no-target");
                }
            }
            catch (Exception ex)
            {
                Warn("resolve-task", ex);
            }

            return new CommandTaskSnapshot(
                false,
                resolution.Intent.Role.ToString(),
                string.Empty,
                default(PlayerOrderPoint),
                default(PlayerOrderPoint),
                army.CurrentOperation.PrimaryObjectiveId,
                "intent-only");
        }

        private static bool TryResolveDoctrineOrder(
            ArmyOrchestrator army,
            Regiment current,
            CommandIntentResolution resolution,
            out CommandDoctrineOrder order)
        {
            order = default(CommandDoctrineOrder);
            var orders = army.CurrentDoctrineOrders;
            if (orders == null || orders.Count == 0) return false;

            int componentId = TacticalPatchIds.ComponentInstanceId(current);
            int gameObjectId = TacticalPatchIds.GameObjectInstanceId(current);
            for (int i = 0; i < orders.Count; i++)
            {
                var candidate = orders[i];
                if (!candidate.HasPurpose) continue;
                if (string.Equals(candidate.NodeId, resolution.Intent.NodeId, StringComparison.Ordinal) ||
                    TacticalPatchIds.NodeIdMatches(candidate.NodeId, gameObjectId, componentId))
                {
                    order = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveDirectChildIntent(
            ArmyOrchestrator army,
            Regiment current,
            out DirectChildIntent intent)
        {
            intent = default(DirectChildIntent);
            var intents = army.CurrentDirectChildIntents;
            if (intents == null || intents.Count == 0) return false;

            int componentId = TacticalPatchIds.ComponentInstanceId(current);
            int gameObjectId = TacticalPatchIds.GameObjectInstanceId(current);
            for (int i = 0; i < intents.Count; i++)
            {
                var candidate = intents[i];
                if (TacticalPatchIds.NodeIdMatches(candidate.ChildId, gameObjectId, componentId))
                {
                    intent = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveCommandOperation(
            ArmyOrchestrator army,
            Regiment current,
            CommandIntentResolution resolution,
            out CommandNodeOperationalState state)
        {
            state = default(CommandNodeOperationalState);
            var operations = army.CurrentCommandOperations;
            if (operations == null || operations.Count == 0) return false;

            int componentId = TacticalPatchIds.ComponentInstanceId(current);
            int gameObjectId = TacticalPatchIds.GameObjectInstanceId(current);
            for (int i = 0; i < operations.Count; i++)
            {
                var candidate = operations[i];
                if (string.Equals(candidate.NodeId, resolution.Intent.NodeId, StringComparison.Ordinal) ||
                    TacticalPatchIds.NodeIdMatches(candidate.NodeId, gameObjectId, componentId))
                {
                    state = candidate;
                    return true;
                }
            }

            return false;
        }

        private static PlayerOrderPoint ObjectivePoint(
            CommandTaskSnapshot task,
            CommandIntentResolution resolution,
            Regiment current,
            out string source)
        {
            return PlayerOrderTargetPolicy.ResolveObjectivePoint(
                task.TargetPoint,
                CurrentObjectivePoint(current),
                ClosestVisibleEnemyPoint(current),
                AllowsVisibleEnemyFallback(task, resolution),
                out source);
        }

        private static PlayerOrderPoint FallbackPoint(CommandTaskSnapshot task, Regiment current)
        {
            if (HasPoint(task.FallbackTarget)) return task.FallbackTarget;
            return default(PlayerOrderPoint);
        }

        private static PlayerOrderPoint CurrentObjectivePoint(Regiment current)
        {
            try
            {
                if (current == null) return default(PlayerOrderPoint);
                object currentObjective = current.GetType().GetField("currentsetobjective")?.GetValue(current);
                var component = currentObjective as Component;
                if (component == null) return default(PlayerOrderPoint);
                Vector3 position = component.transform.position;
                return new PlayerOrderPoint(position.x, position.z);
            }
            catch (Exception ex)
            {
                Warn("current-objective", ex);
                return default(PlayerOrderPoint);
            }
        }

        private static PlayerOrderPoint ClosestVisibleEnemyPoint(Regiment current)
        {
            try
            {
                Regiment enemy = current != null && current.unitrange != null
                    ? current.unitrange.closestenemyunitfarreg
                    : null;
                if (enemy == null) return default(PlayerOrderPoint);
                Vector3 position = enemy.GetPosition();
                return new PlayerOrderPoint(position.x, position.z);
            }
            catch (Exception ex)
            {
                Warn("closest-visible-enemy", ex);
                return default(PlayerOrderPoint);
            }
        }

        private static bool AllowsVisibleEnemyFallback(CommandTaskSnapshot task, CommandIntentResolution resolution)
        {
            return IsAny(task.Task, "attackobjective", "supportattack", "fixenemy", "probe", "screen") ||
                IsAny(task.Role, "maineffort", "main", "supportmain", "supportingattack", "screeningforce", "probe") ||
                IsAny(resolution.Intent.Role.ToString(), "maineffort", "main", "supportmain", "supportingattack", "screeningforce", "probe");
        }

        private static PlayerOrderPoint ResolveExitPoint(AIBattle battle, Regiment current, out bool hasExit)
        {
            hasExit = false;
            try
            {
                BattleUnits bunits = ResolveBattleUnits(battle);
                if (bunits == null || current == null || current.unitrange == null)
                {
                    return default(PlayerOrderPoint);
                }

                Vector3 start = current.unitrange.flankposition[2];
                float angle = current.unitrange.retreatangle;
                EntryPoint entry = bunits.SearchForClosestEntryPoint(start, current.alliance, false, angle, 30f);
                if (entry == null) return default(PlayerOrderPoint);

                Vector3 position = entry.transform.position;
                hasExit = true;
                return new PlayerOrderPoint(position.x, position.z, validExitPoint: true);
            }
            catch (Exception ex)
            {
                Warn("exit-point", ex);
                return default(PlayerOrderPoint);
            }
        }

        private static BattleUnits ResolveBattleUnits(AIBattle battle)
        {
            try
            {
                if (_battleUnitsField == null && !_battleUnitsFieldMissing)
                {
                    _battleUnitsField = AccessTools.Field(typeof(AIBattle), "bunits");
                    _battleUnitsFieldMissing = _battleUnitsField == null;
                }

                if (_battleUnitsField != null)
                {
                    var value = _battleUnitsField.GetValue(battle) as BattleUnits;
                    if (value != null) return value;
                }
            }
            catch (Exception ex)
            {
                Warn("bunits-field", ex);
            }

            try
            {
                var controller = GameObject.Find("GameController");
                return controller != null ? controller.GetComponent<BattleUnits>() : null;
            }
            catch (Exception ex)
            {
                Warn("bunits-find", ex);
                return null;
            }
        }

        private static Regiment ResolveCurrentCommand()
        {
            try
            {
                if (!DLC_WL.dlc_scenarioactive) return null;
                int chosen = DLC_WL.dlc_chosencommander;
                if (chosen < 0 || GameVars.commander == null || chosen >= GameVars.commander.Count) return null;
                return GameVars.commander[chosen].currentcommand;
            }
            catch (Exception ex)
            {
                Warn("current-command", ex);
                return null;
            }
        }

        private static bool IsWlActive()
        {
            try { return DLC_WL.dlc_scenarioactive; }
            catch { return false; }
        }

        private static bool IsCommanderInChief()
        {
            try { return DLC_WL.dlc_scenarioactive && DLC_WL.IsCommanderInChief(); }
            catch { return true; }
        }

        private static int SafeGivenOrderSession()
        {
            try { return DLC_WL.GivenOrders.givenorderssession; }
            catch { return 0; }
        }

        private static int SafeCurrentOperation()
        {
            try { return SceneManagement.currentoperation; }
            catch { return 0; }
        }

        private static string BattleIdentity(AIBattle battle)
        {
            try
            {
                if (battle == null) return string.Empty;
                return "battle-" + battle.GetInstanceID();
            }
            catch { return string.Empty; }
        }

        private static string UnitKey(Regiment unit)
        {
            int id = TacticalPatchIds.GameObjectInstanceId(unit);
            if (id == 0) id = TacticalPatchIds.ComponentInstanceId(unit);
            return id == 0 ? string.Empty : "unit-" + id;
        }

        private static PlayerOrderPoint ToPoint(Regiment unit)
        {
            try
            {
                if (unit == null) return default(PlayerOrderPoint);
                Vector3 position = unit.GetPosition();
                return new PlayerOrderPoint(position.x, position.z);
            }
            catch
            {
                return default(PlayerOrderPoint);
            }
        }

        private static PlayerOrderPoint ToPoint(DoctrineTargetPoint point)
        {
            return point.HasValue ? new PlayerOrderPoint(point.X, point.Z) : default(PlayerOrderPoint);
        }

        private static bool HasPoint(PlayerOrderPoint point)
        {
            return IsFinite(point.X) && IsFinite(point.Z) && (Math.Abs(point.X) > 0.001f || Math.Abs(point.Z) > 0.001f || point.ValidExitPoint);
        }

        private static int Bucket(float value)
        {
            if (!IsFinite(value)) return 0;
            return (int)Math.Round(value / 10f);
        }

        private static string ExpectedDestination(PlayerOrderCandidate candidate)
        {
            if (candidate.Intent == PlayerOrderIntent.RetreatToExit) return string.Empty;
            return string.IsNullOrWhiteSpace(candidate.ObjectiveKey) ? "Objective" : candidate.ObjectiveKey;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsAny(string value, params string[] expected)
        {
            if (expected == null || expected.Length == 0) return false;
            string normalized = Normalize(value);
            for (int i = 0; i < expected.Length; i++)
            {
                if (normalized == Normalize(expected[i])) return true;
            }

            return false;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return value.Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).Trim().ToLowerInvariant();
        }

        private static long Tick()
        {
            try { return Time.frameCount; }
            catch { return Environment.TickCount; }
        }

        private static void Warn(string key, Exception ex)
        {
            try
            {
                OnceLog.Warning("player-order-runtime:" + key, "[PlayerOrderIntent] runtime adapter " + key + " failed: " + ex.GetType().Name + " " + ex.Message);
            }
            catch { }
        }
    }
}
