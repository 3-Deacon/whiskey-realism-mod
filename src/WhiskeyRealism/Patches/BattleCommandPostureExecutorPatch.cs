using System;
using System.Collections;
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
    // Vanilla AIBattle.AdjustGroupFormations chooses a group formation after
    // stance updates. This Postfix applies the active operations-ledger posture
    // executor after vanilla, using only vanilla formation/waypoint/stance APIs.
    [HarmonyPatch(typeof(AIBattle), "AdjustGroupFormations")]
    internal static class BattleCommandPostureExecutorPatch
    {
        private const float RecentOrderSeconds = 30f;
        private const float TelemetrySeconds = 30f;
        private const float ObjectiveApproachStandOff = 75f;
        private const float AssemblyStandOff = 200f;
        private const float ReserveStandOff = 350f;
        private const float FallbackStandOff = 250f;
        private const float MaxConservativeWaypointDistance = 2500f;
        private const float MinWaypointDistance = 15f;

        private static readonly Dictionary<int, float> _lastExecutorOrderAt = new Dictionary<int, float>();
        private static readonly Dictionary<string, float> _lastTelemetryAt = new Dictionary<string, float>();

        private static FieldInfo _stateField;
        private static FieldInfo _macroAiField;
        private static FieldInfo _isPlayerAiOrFeudField;
        private static FieldInfo _sideOfAiField;
        private static FieldInfo _bunitsField;
        private static FieldInfo _unitsUsedField;

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        internal static void Postfix(AIBattle __instance)
        {
            if (!EnabledForWrites() || __instance == null) return;

            try
            {
                OnceLog.Info("tactical-command-posture-executor", "BattleCommandPostureExecutorPatch wired");
                Apply(__instance);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-command-posture-executor:failed",
                    "BattleCommandPostureExecutorPatch failed: " + ex.Message);
            }
        }

        private static void Apply(AIBattle battle)
        {
            int state = SafeIntField(battle, ref _stateField, "state", -1);
            int macro = SafeIntField(battle, ref _macroAiField, "macroai", -99);
            int side = SafeIntField(battle, ref _sideOfAiField, "sideofai", -1);
            int isPlayerAiOrFeud = SafeIntField(battle, ref _isPlayerAiOrFeudField, "isplayeraiorfeud", -1);
            var bunits = SafeField<BattleUnits>(battle, ref _bunitsField, "bunits");
            var units = SafeList(battle, ref _unitsUsedField, "unitsused");

            if (state < 5 || macro < 0 || side < 0 || bunits == null || units == null) return;
            if (isPlayerAiOrFeud != 0 && !GameVars.ai_vs_ai) return;

            for (int i = 0; i < units.Count; i++)
            {
                var group = units[i] as Regiment;
                if (!IsEligibleCommandGroup(group)) continue;
                TryApplyGroup(bunits, side, group);
            }
        }

        private static void TryApplyGroup(BattleUnits bunits, int side, Regiment group)
        {
            if (!TryResolveLedgerState(group, out CommandNodeOperationalState state, out TacticalBattleOrchestrator orchestrator))
                return;

            int instanceId = SafeInstanceId(group);
            bool playerProtected = IsPlayerProtected(group);
            bool routed = SafeRouted(group);
            bool orderPending = HasPendingOrder(group);
            bool recentOrder = HasRecentExecutorOrder(instanceId);
            var physical = BuildPhysicalState(group, playerProtected, routed);
            TacticalIdleClassification idle = TacticalCommandMonitor.ClassifyIdle(state, physical);
            int targetFormation = TargetFormationForTask(state.Task, group);
            bool alreadyCorrect = IsAlreadyDoingCorrectTask(group, state, physical, targetFormation, idle);

            var eligibility = new WriteEligibilitySnapshot(
                modeAllowsWrites: true,
                playerProtected: playerProtected,
                routed: routed,
                orderPending: orderPending,
                recentOrder: recentOrder,
                alreadyDoingCorrectTask: alreadyCorrect,
                atAssignedLocation: idle == TacticalIdleClassification.ValidIdle,
                missingLedgerAssignment: false,
                closeEngaged: HasCloseEngagement(group));

            var decision = CommandPostureExecutor.Decide(state, physical, eligibility);
            if (decision.Action == PostureExecutionAction.NoWrite)
            {
                EmitPostureTelemetry(side, group, state, decision, idle, applied: false, extraReason: decision.Reason);
                return;
            }

            if (!CanWrite(group, eligibility, physical))
            {
                EmitPostureTelemetry(side, group, state, decision, idle, applied: false, extraReason: "write-gate-denied");
                return;
            }

            bool hasTarget = TryResolveTarget(group, orchestrator, state, decision.Target, out Vector3 target);
            bool wrote = ApplyDecision(bunits, group, state, decision, targetFormation, hasTarget, target);

            if (wrote)
            {
                _lastExecutorOrderAt[instanceId] = Time.realtimeSinceStartup;
                EmitPostureTelemetry(side, group, state, decision, idle, applied: true, extraReason: decision.Reason);
            }
            else
            {
                EmitPostureTelemetry(side, group, state, decision, idle, applied: false, extraReason: "target-unresolved");
            }
        }

        private static bool ApplyDecision(
            BattleUnits bunits,
            Regiment group,
            CommandNodeOperationalState state,
            PostureExecutionDecision decision,
            int targetFormation,
            bool hasTarget,
            Vector3 target)
        {
            switch (decision.Action)
            {
                case PostureExecutionAction.SetFormation:
                    return SetFormation(bunits, group, targetFormation);
                case PostureExecutionAction.SetFormationAndWaypoint:
                    if (!hasTarget) return false;
                    bool formed = SetFormation(bunits, group, targetFormation);
                    return SetWaypoint(bunits, group, target) || formed;
                case PostureExecutionAction.SetWaypoint:
                case PostureExecutionAction.ReleaseReserve:
                case PostureExecutionAction.FallbackToLine:
                case PostureExecutionAction.RecoverInterruptedOrder:
                    return hasTarget && SetWaypoint(bunits, group, target);
                case PostureExecutionAction.ChangeStance:
                    return ChangeStance(bunits, group, StanceForTask(state.Task));
                default:
                    return false;
            }
        }

        private static bool SetFormation(BattleUnits bunits, Regiment group, int targetFormation)
        {
            if (!CanUseGroupFormation(group)) return false;
            if (targetFormation < 0 || targetFormation > 4) return false;
            if (SafeGroupFormation(group) == targetFormation) return false;

            bunits.SetGroupFormation(
                group,
                targetFormation,
                manualfinalrotation: -1f,
                targetpos: default(Vector3),
                immediateplacement: false,
                newpath: true,
                modifylastwaypoint: false,
                newstate: 2,
                refuseflank: -1,
                ignoredeplyomentzone: false,
                skiprotation: false,
                showmovementoptions: false,
                placeentrenchments: false,
                adjustbyterrainshape: true);
            return true;
        }

        private static bool SetWaypoint(BattleUnits bunits, Regiment group, Vector3 target)
        {
            if (!IsSafeWaypoint(group, target)) return false;

            bunits.SetWaypoint(
                group,
                target,
                newpath: true,
                doublequick: false,
                manualfinalrotation: -1f,
                modifylastwaypoint: false,
                useorderdelay: true,
                timetomove: -1f,
                direction: -1,
                showmovementoptions: false,
                ignorebattlemonuments: false,
                groupmoveonly: false,
                ignoredisabledships: false,
                checkforreadiness: true,
                clearinterruptionpaths: true);
            return true;
        }

        private static bool ChangeStance(BattleUnits bunits, Regiment group, int stance)
        {
            if (stance < 0 || stance > 3) return false;
            if (group.ai_stanceordered == stance) return false;

            GameObject gameObject = UnityObject(group);
            if (gameObject == null || !gameObject.activeInHierarchy) return false;

            bunits.ChangeStance(gameObject, stance, immediate: false, overwriteaigroups: false);
            return true;
        }

        private static bool TryResolveTarget(
            Regiment group,
            TacticalBattleOrchestrator orchestrator,
            CommandNodeOperationalState state,
            PostureExecutionTarget targetKind,
            out Vector3 target)
        {
            target = default(Vector3);
            switch (targetKind)
            {
                case PostureExecutionTarget.ObjectiveApproach:
                case PostureExecutionTarget.ReleasePoint:
                    return TryObjectiveApproach(group, orchestrator, ObjectiveApproachStandOff, out target);
                case PostureExecutionTarget.AssemblyArea:
                    return TryObjectiveApproach(group, orchestrator, AssemblyStandOff, out target);
                case PostureExecutionTarget.FallbackLine:
                    return TryFallbackFromObjective(group, orchestrator, out target);
                case PostureExecutionTarget.RecoveryPath:
                    return TryRecoveryPath(group, orchestrator, out target);
                case PostureExecutionTarget.ReserveArea:
                    return TryObjectiveApproach(group, orchestrator, ReserveStandOff, out target);
                case PostureExecutionTarget.CurrentPosition:
                case PostureExecutionTarget.None:
                    return false;
                default:
                    return false;
            }
        }

        private static bool TryObjectiveApproach(Regiment group, TacticalBattleOrchestrator orchestrator, float standOff, out Vector3 target)
        {
            target = default(Vector3);
            if (!TryPrimaryObjectivePoint(group, orchestrator, out Vector3 objective)) return false;

            Vector3 current = SafePosition(group);
            if (IsDefaultVector(current)) return false;

            float distance = SafeDistance(current, objective);
            if (distance < MinWaypointDistance || distance > MaxConservativeWaypointDistance) return false;

            float offset = Math.Min(Math.Max(standOff, 0f), Math.Max(0f, distance - MinWaypointDistance));
            target = Vector3.MoveTowards(objective, current, offset);
            return IsSafeWaypoint(group, target);
        }

        private static bool TryFallbackFromObjective(
            Regiment group,
            TacticalBattleOrchestrator orchestrator,
            out Vector3 target)
        {
            target = default(Vector3);
            if (!TryPrimaryObjectivePoint(group, orchestrator, out Vector3 objective)) return false;

            Vector3 current = SafePosition(group);
            if (IsDefaultVector(current)) return false;

            float dx = current.x - objective.x;
            float dz = current.z - objective.z;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < 0.01f) return false;

            target = new Vector3(
                current.x + (dx / length * FallbackStandOff),
                SafeBattleY(),
                current.z + (dz / length * FallbackStandOff));
            return IsSafeWaypoint(group, target);
        }

        private static bool TryRecoveryPath(
            Regiment group,
            TacticalBattleOrchestrator orchestrator,
            out Vector3 target)
        {
            target = default(Vector3);
            try
            {
                if (group != null)
                {
                    Vector3 lastWaypoint = group.lastsetwaypointposition;
                    if (IsSafeWaypoint(group, lastWaypoint))
                    {
                        target = lastWaypoint;
                        return true;
                    }
                }
            }
            catch { }

            return TryObjectiveApproach(group, orchestrator, AssemblyStandOff, out target);
        }

        private static bool TryPrimaryObjectivePoint(
            Regiment group,
            TacticalBattleOrchestrator orchestrator,
            out Vector3 objective)
        {
            objective = default(Vector3);
            if (TryCurrentSetObjectivePoint(group, out objective)) return true;

            var objectives = orchestrator?.OperationsLedger?.CurrentObjectives;
            if (objectives == null || objectives.Count == 0) return false;

            string primary = orchestrator.Army != null
                ? orchestrator.Army.CurrentOperation.PrimaryObjectiveId
                : "objective-unknown";
            for (int i = 0; i < objectives.Count; i++)
            {
                var record = objectives[i];
                if (!string.Equals(record.Observation.ObjectiveId, primary, StringComparison.Ordinal))
                    continue;

                objective = new Vector3(record.Observation.Location.X, SafeBattleY(), record.Observation.Location.Z);
                return !IsDefaultVector(objective);
            }

            return false;
        }

        private static bool TryCurrentSetObjectivePoint(Regiment group, out Vector3 objective)
        {
            objective = default(Vector3);
            try
            {
                if (group == null) return false;
                object current = group.GetType().GetField("currentsetobjective")?.GetValue(group);
                var component = current as Component;
                if (component == null) return false;
                Vector3 position = component.transform.position;
                objective = new Vector3(position.x, SafeBattleY(), position.z);
                return !IsDefaultVector(objective);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSafeWaypoint(Regiment group, Vector3 target)
        {
            Vector3 current = SafePosition(group);
            if (IsDefaultVector(current) || IsDefaultVector(target)) return false;
            float distance = SafeDistance(current, target);
            return distance >= MinWaypointDistance && distance <= MaxConservativeWaypointDistance;
        }

        private static bool CanWrite(
            Regiment group,
            WriteEligibilitySnapshot eligibility,
            CommandPhysicalState physical)
        {
            if (!eligibility.ModeAllowsWrites) return false;
            if (eligibility.PlayerProtected || physical.PlayerProtected) return false;
            if (eligibility.Routed || physical.Routed) return false;
            if (eligibility.OrderPending) return false;
            if (eligibility.RecentOrder) return false;
            if (physical.ActiveMove) return false;
            if (HasPendingOrder(group)) return false;
            if (HasRecentExecutorOrder(SafeInstanceId(group))) return false;
            return true;
        }

        private static CommandPhysicalState BuildPhysicalState(Regiment group, bool playerProtected, bool routed)
        {
            return new CommandPhysicalState(
                routed: routed,
                playerProtected: playerProtected,
                pathInterrupted: group != null && group.pathinterrupted,
                paths: SafeRegimentPaths(group),
                activeMove: HasActiveMoveMakingProgress(group),
                formation: SafeGroupFormation(group));
        }

        private static bool IsAlreadyDoingCorrectTask(
            Regiment group,
            CommandNodeOperationalState state,
            CommandPhysicalState physical,
            int targetFormation,
            TacticalIdleClassification idle)
        {
            if (group == null || physical.PathInterrupted) return false;
            if (physical.ActiveMove) return true;
            if (targetFormation >= 0 && SafeGroupFormation(group) != targetFormation) return false;
            return idle == TacticalIdleClassification.ValidIdle &&
                (state.Task == CommandTaskType.ReserveWait ||
                 state.Task == CommandTaskType.HoldObjective ||
                 state.Task == CommandTaskType.HoldChoke ||
                 state.Task == CommandTaskType.FixEnemy ||
                 state.Task == CommandTaskType.Screen ||
                 state.Task == CommandTaskType.Probe ||
                 state.Task == CommandTaskType.Delay ||
                 state.Task == CommandTaskType.Consolidate);
        }

        private static bool TryResolveLedgerState(
            Regiment group,
            out CommandNodeOperationalState state,
            out TacticalBattleOrchestrator side)
        {
            state = default;
            side = null;

            try
            {
                if (group == null) return false;
                side = TacticalBattleCoordinator.GetSideOrchestrator(group.alliance);
                ArmyOrchestrator army = side?.Army;
                var operations = army?.CurrentCommandOperations;
                int instanceId = SafeInstanceId(group);
                string nodeId = "node-" + instanceId;
                if (operations != null)
                {
                    for (int i = 0; i < operations.Count; i++)
                    {
                        if (!string.Equals(operations[i].NodeId, nodeId, StringComparison.Ordinal)) continue;
                        state = operations[i];
                        return true;
                    }
                }

                var resolution = army?.ResolveCommandIntentForGroup(instanceId);
                if (resolution.HasValue &&
                    resolution.Value.Found &&
                    CommandNodeOperationsRuntime.TryBuildSingle(
                        resolution.Value.Intent,
                        army.CurrentOperation,
                        side?.OperationsLedger?.CurrentObjectives,
                        out state))
                    return true;
            }
            catch { }

            return false;
        }

        private static bool IsEligibleCommandGroup(Regiment group)
        {
            try
            {
                if (group == null) return false;
                if (group.alliance == GameVars.playeralliance && !GameVars.ai_vs_ai) return false;
                GameObject gameObject = UnityObject(group);
                return gameObject != null && gameObject.activeInHierarchy;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPlayerProtected(Regiment group)
        {
            try
            {
                if (group == null) return true;
                if (group.alliance == GameVars.playeralliance && !GameVars.ai_vs_ai) return true;
                if (!WlOwnershipSafe(group)) return true;
                if (IsWlCurrentCommandOrChain(group)) return true;
                if (SafeDlcTakenOver(group)) return true;
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool WlOwnershipSafe(Regiment group)
        {
            try
            {
                if (!DLC_WL.dlc_scenarioactive) return true;
                if (group == null) return false;
                if (group.dlcw_isundercommander) return false;
                if (group.allattachedunits == null) return true;

                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    Regiment unit = group.allattachedunits[i];
                    if (unit != null && unit.dlcw_isundercommander) return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsWlCurrentCommandOrChain(Regiment group)
        {
            try
            {
                if (!DLC_WL.dlc_scenarioactive) return false;
                if (DLC_WL.dlc_chosencommander < 0) return false;
                if (GameVars.commander == null || DLC_WL.dlc_chosencommander >= GameVars.commander.Count) return true;

                Regiment current = GameVars.commander[DLC_WL.dlc_chosencommander].currentcommand;
                if ((UnityEngine.Object)current == null) return false;
                if ((UnityEngine.Object)current == (UnityEngine.Object)group) return true;

                Transform currentTransform = current.transform;
                Transform groupTransform = group.transform;
                if (currentTransform == null || groupTransform == null) return true;

                return currentTransform.IsChildOf(groupTransform) || groupTransform.IsChildOf(currentTransform);
            }
            catch
            {
                return true;
            }
        }

        private static bool HasPendingOrder(Regiment group)
        {
            if (PendingOrderOn(group)) return true;
            try
            {
                if (group == null || group.allattachedunits == null) return false;
                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    if (PendingOrderOn(group.allattachedunits[i])) return true;
                }
            }
            catch
            {
                return true;
            }

            return false;
        }

        private static bool PendingOrderOn(Regiment unit)
        {
            try
            {
                if (unit == null) return false;
                if (unit.orderstate > 0) return true;
                return unit.orderqueue != null && unit.orderqueue.Count > 0;
            }
            catch
            {
                return true;
            }
        }

        private static bool HasRecentExecutorOrder(int instanceId)
        {
            if (instanceId == 0) return true;
            if (!_lastExecutorOrderAt.TryGetValue(instanceId, out float last)) return false;
            return Time.realtimeSinceStartup - last < RecentOrderSeconds;
        }

        private static bool HasActiveMoveMakingProgress(Regiment group)
        {
            try
            {
                if (group == null) return false;
                if (group.pathinterrupted) return false;
                if (group.regimentpaths <= 0) return false;
                if (group.groupsubordinatesmoving > 0.05f || group.groupsubordinatesmovingnonai > 0.05f) return true;
                if (group.movementmode > 0) return true;
                Vector3 lastWaypoint = group.lastsetwaypointposition;
                return !IsDefaultVector(lastWaypoint) && SafeDistance(SafePosition(group), lastWaypoint) > MinWaypointDistance;
            }
            catch
            {
                return true;
            }
        }

        private static bool HasCloseEngagement(Regiment group)
        {
            try
            {
                return group != null &&
                    (group.combatstatussubordinates > 0.2f ||
                     group.combatstatussubordinatesengagednonari > 0.2f ||
                     group.groupenemiesinrange > 0f);
            }
            catch
            {
                return true;
            }
        }

        private static int TargetFormationForTask(CommandTaskType task, Regiment group)
        {
            if (!CanUseGroupFormation(group)) return -1;

            switch (task)
            {
                case CommandTaskType.AttackObjective:
                case CommandTaskType.SupportAttack:
                    return 3;
                case CommandTaskType.FormUp:
                case CommandTaskType.AdvanceToAssembly:
                case CommandTaskType.FixEnemy:
                case CommandTaskType.HoldObjective:
                case CommandTaskType.HoldChoke:
                case CommandTaskType.GuardFlank:
                case CommandTaskType.FallBackToLine:
                case CommandTaskType.Delay:
                case CommandTaskType.Consolidate:
                case CommandTaskType.ReserveWait:
                case CommandTaskType.ReleaseReserve:
                case CommandTaskType.RecoverStuckOrder:
                    return 0;
                case CommandTaskType.Screen:
                case CommandTaskType.Probe:
                case CommandTaskType.Scout:
                    return SafeGroupFormation(group) == 4 ? 0 : SafeGroupFormation(group);
                default:
                    return SafeGroupFormation(group);
            }
        }

        private static bool CanUseGroupFormation(Regiment group)
        {
            try
            {
                return group != null && group.unittyp > 13;
            }
            catch
            {
                return false;
            }
        }

        private static int StanceForTask(CommandTaskType task)
        {
            switch (task)
            {
                case CommandTaskType.AttackObjective:
                case CommandTaskType.SupportAttack:
                    return 3;
                case CommandTaskType.FixEnemy:
                case CommandTaskType.HoldObjective:
                case CommandTaskType.HoldChoke:
                case CommandTaskType.ReserveWait:
                case CommandTaskType.FallBackToLine:
                    return 2;
                default:
                    return -1;
            }
        }

        private static bool SafeRouted(Regiment group)
        {
            try { return group == null || group.isrouted || group.markedforrout; }
            catch { return true; }
        }

        private static bool SafeDlcTakenOver(Regiment group)
        {
            try { return DLC_WL.dlc_scenarioactive && group != null && DLC_WL.IsUnitTakenOver(group); }
            catch { return true; }
        }

        private static int SafeRegimentPaths(Regiment group)
        {
            try { return group != null ? Math.Max(0, group.regimentpaths) : 0; }
            catch { return 0; }
        }

        private static int SafeGroupFormation(Regiment group)
        {
            try { return group != null ? group.groupformation : -1; }
            catch { return -1; }
        }

        private static Vector3 SafePosition(Regiment group)
        {
            try { return group != null ? group.transform.position : default(Vector3); }
            catch { return default(Vector3); }
        }

        private static float SafeBattleY()
        {
            return 0f;
        }

        private static bool IsDefaultVector(Vector3 value)
        {
            return float.IsNaN(value.x) ||
                float.IsNaN(value.y) ||
                float.IsNaN(value.z) ||
                float.IsInfinity(value.x) ||
                float.IsInfinity(value.y) ||
                float.IsInfinity(value.z) ||
                (Math.Abs(value.x) < 0.01f && Math.Abs(value.z) < 0.01f);
        }

        private static float SafeDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static void EmitPostureTelemetry(
            int side,
            Regiment group,
            CommandNodeOperationalState state,
            PostureExecutionDecision decision,
            TacticalIdleClassification idle,
            bool applied,
            string extraReason)
        {
            string reason = string.IsNullOrEmpty(extraReason) ? decision.Reason : extraReason;
            string signature = side + "|" + SafeInstanceId(group) + "|" + state.Task + "|" + decision.Action +
                "|" + decision.Target + "|" + reason + "|" + applied + "|" + idle;
            string key = "tactical-command-posture:" + side + ":" + SafeInstanceId(group);
            if (!TacticalTelemetry.ShouldEmit(_lastTelemetryAt, key, signature, Time.realtimeSinceStartup, TelemetrySeconds, false))
                return;

            Plugin.Log.LogInfo("[TacticalCommandPosture] side=" + side +
                " node=" + TacticalOperationsTelemetry.SafeToken(state.NodeId) +
                " group=" + SafeName(group) + "#" + SafeInstanceId(group) +
                " task=" + state.Task +
                " decision=" + decision.Action +
                " target=" + decision.Target +
                " applied=" + applied +
                " reason=" + TacticalOperationsTelemetry.SafeToken(reason) +
                " currentFormation=" + SafeGroupFormation(group) +
                " paths=" + SafeRegimentPaths(group) +
                " pathInterrupted=" + (group != null && group.pathinterrupted) +
                " activeMove=" + HasActiveMoveMakingProgress(group));
        }

        private static bool EnabledForWrites()
        {
            try
            {
                return Plugin.Instance != null &&
                    Plugin.Instance.Enabled.Value &&
                    TacticalCommanderModePolicy.AllowsWrites(Plugin.Instance.TacticalCommanderModeValue);
            }
            catch
            {
                return false;
            }
        }

        private static int SafeIntField(object instance, ref FieldInfo cache, string name, int fallback)
        {
            try
            {
                if (instance == null) return fallback;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                if (cache == null) return fallback;
                return Convert.ToInt32(cache.GetValue(instance));
            }
            catch
            {
                return fallback;
            }
        }

        private static T SafeField<T>(object instance, ref FieldInfo cache, string name) where T : class
        {
            try
            {
                if (instance == null) return null;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                return cache != null ? cache.GetValue(instance) as T : null;
            }
            catch
            {
                return null;
            }
        }

        private static IList SafeList(object instance, ref FieldInfo cache, string name)
        {
            try
            {
                if (instance == null) return null;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                return cache != null ? cache.GetValue(instance) as IList : null;
            }
            catch
            {
                return null;
            }
        }

        private static GameObject UnityObject(Regiment unit)
        {
            try { return unit != null ? unit.gameObject : null; }
            catch { return null; }
        }

        private static int SafeInstanceId(UnityEngine.Object obj)
        {
            try { return obj != null ? obj.GetInstanceID() : 0; }
            catch { return 0; }
        }

        private static string SafeName(Regiment group)
        {
            try { return group != null ? TacticalCurrentOrderSignature.Safe(group.name) : "-"; }
            catch { return "-"; }
        }
    }
}
