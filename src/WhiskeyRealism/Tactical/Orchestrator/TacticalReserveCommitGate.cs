using System;
using System.Collections.Generic;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    // Reserve decisions, pure half (BC5c, plan docs/superpowers/plans/2026-10-15-battle-core.md).
    //
    // #59 deny (BattleReserveCommitGatePatch on vanilla AIBattle.CheckUseOfReserves
    // 6062-6170): vanilla commits one idle reserve regiment toward an outflanked
    // sibling with a direct RegimentSetPath (6170), so only a rollback can deny it.
    // The patch acts only for a group whose resolved role is Reserve (ledger state
    // first, then the command-node intent). For it the rule is: deny, unless the
    // direct-child allocator released it (T15: crisis Fix, Exploit or trigger odds)
    // before the ledger caught up; observe when a committed unit or the group is
    // player-owned. The deleted B8 doctrine deny needed order.Role == Reserve, and
    // the order's role is the ledger node's role (CommandDoctrineAssignment.Build;
    // ArmyOrchestrator 1072-1084), so it was this same rule. There is no
    // already-engaged exemption: vanilla only commits a unit that is not engaged
    // (6129), and inengagement is set only when a unit fires (FireBullet 119050),
    // which nothing in CheckUseOfReserves does.
    //
    // #56 courier parity: vanilla's reserve path ignores the order delay (6170);
    // with GameVars.useorderdelays the path is removed and the same target is
    // re-issued through the inline executor with the courier delay (SetWaypoint
    // queue 91483-91510). PlanCourierParity decides before anything is removed,
    // so a refused re-issue leaves vanilla's own move in place.
    //
    // Reserve release (ReserveReleaseIntentProducer): a group the ledger still
    // holds in Reserve but the allocator released gets a Support move to stand
    // behind its own line in the sector the allocator gave it (spec §4 ladder).
    internal static class TacticalReserveCommitGate
    {
        // How far behind the own line a released reserve stands: the friendly
        // footprint radius the executor treats as "the same place"
        // (WaypointValidator.FriendlyFootprintRadiusMeters = #61's 60 m). Vanilla's
        // own reserve support target is one regiment width behind the endangered
        // unit (GetAngleTerrainPosXY(pos, facing, -width, ...), 6121).
        public const float ReleaseStandOffMeters = WaypointValidator.FriendlyFootprintRadiusMeters;

        public enum Action
        {
            Observe = 0,
            Allow = 1,
            Deny = 2,
        }

        public enum CourierParityAction
        {
            Leave = 0,
            Convert = 1,
        }

        public readonly struct Input
        {
            public Input(bool playerControlled, bool reserveReleased)
            {
                PlayerControlled = playerControlled;
                ReserveReleased = reserveReleased;
            }

            public bool PlayerControlled { get; }
            public bool ReserveReleased { get; }
        }

        public readonly struct Decision
        {
            public Decision(Action action, string reason)
            {
                Action = action;
                Reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
            }

            public Action Action { get; }
            public string Reason { get; }
        }

        /// <summary>The rule for a Reserve-role group whose units vanilla just committed.</summary>
        public static Decision Decide(Input input)
        {
            if (input.PlayerControlled)
                return new Decision(Action.Observe, "player-controlled");
            return input.ReserveReleased
                ? new Decision(Action.Allow, "reserve-released")
                : new Decision(Action.Deny, "role-reserve-hold");
        }

        /// <summary>
        /// The reserve-release producer: <paramref name="held"/> (the resolution the
        /// gate acts on, ledger state first) still says Reserve while
        /// <paramref name="allocator"/> (the command-node intent the direct-child
        /// allocator produced) has re-roled the group to a committed role.
        /// </summary>
        public static bool ReleasedByAllocator(CommandIntentResolution held, CommandIntentResolution allocator)
        {
            if (!held.Found || held.Intent.Role != DirectChildRole.Reserve) return false;
            if (!allocator.Found) return false;
            DirectChildRole role = allocator.Intent.Role;
            return role != DirectChildRole.Reserve && role != DirectChildRole.Unknown;
        }

        /// <summary>CheckUseOfReserves 6084: vanilla returns for an inactive group or a regiment-level unit (unittyp &lt;= 13).</summary>
        public static bool GroupMayCommit(bool activeInHierarchy, int groupUnitTyp)
        {
            return activeInHierarchy && groupUnitTyp > TacticalUnitType.MaxCombat;
        }

        /// <summary>
        /// A reserve unit vanilla gave a direct path this call (paths rose, no courier
        /// order queued): the set a deny rolls back.
        /// </summary>
        public static bool VanillaCommittedUnit(int beforePaths, int afterPaths, int beforeQueue, int afterQueue, bool useOrderDelays)
        {
            return TacticalReservePolicyLedger.ShouldRemoveDeniedReserveMovement(
                doctrineDeny: true, beforePaths, afterPaths, beforeQueue, afterQueue, useOrderDelays);
        }

        /// <summary>
        /// The resolution the gate acts on: the operations-ledger state for the group
        /// when there is one (Reserve role or ReserveWait task holds it in Reserve),
        /// otherwise the allocator's command-node intent.
        /// </summary>
        public static CommandIntentResolution HeldResolution(
            IReadOnlyList<CommandNodeOperationalState> operations,
            int gameObjectId,
            int componentId,
            CommandIntentResolution allocator)
        {
            if (operations != null)
            {
                for (int i = 0; i < operations.Count; i++)
                {
                    if (TacticalPatchIds.NodeIdMatches(operations[i].NodeId, gameObjectId, componentId))
                        return LedgerResolution(operations[i]);
                }
            }
            return allocator;
        }

        /// <summary>#56's re-issue of vanilla's reserve move (6170: target, facing, doublequick 6171).</summary>
        public static BattleOrderIntent CourierParityIntent(int unitId, float x, float z, float heading)
        {
            return new BattleOrderIntent
            {
                GroupId = unitId,
                Channel = OrderChannel.Move,
                TargetX = x,
                TargetZ = z,
                Heading = heading,
                Priority = IntentPriority.Support,
                Source = IntentSource.ReserveCourierParity,
                // The target sits one regiment width behind the endangered sibling, so it
                // may fall inside a friendly group's footprint: a support move.
                Flags = IntentFlags.UseOrderDelay | IntentFlags.DoubleQuick | IntentFlags.ReliefOrSupport,
                Task = CommandTaskType.None,
                Reason = "reserve-courier-parity",
            };
        }

        /// <summary>
        /// Convert only when vanilla gave the unit a direct path this call, SetWaypoint
        /// would queue it, Whiskey may write the unit, the unit is in the side's battle
        /// snapshot (the inline executor plans against it), and no higher arbiter claim
        /// owns its Move channel (D4). Anything else leaves vanilla's move alone.
        /// </summary>
        public static CourierParityAction PlanCourierParity(
            OrderArbiter arbiter,
            in BattleOrderIntent intent,
            bool unitWritable,
            bool unitInSnapshot,
            bool conversionApplies,
            int beforePaths,
            int afterPaths,
            int beforeQueue,
            int afterQueue,
            bool useOrderDelays)
        {
            if (!unitWritable || !unitInSnapshot || !conversionApplies) return CourierParityAction.Leave;
            if (!TacticalReservePolicyLedger.ShouldDelayGuardConsumeReserveMovement(
                    doctrineDeny: false, beforePaths, afterPaths, beforeQueue, afterQueue, useOrderDelays))
                return CourierParityAction.Leave;
            if (OrderExecutor.InlineClaimCheck(arbiter, intent) != SkipReason.None) return CourierParityAction.Leave;
            return CourierParityAction.Convert;
        }

        /// <summary>
        /// #56 removes vanilla's immediate path only when the screen converts and the
        /// executor's inline plan will execute; otherwise vanilla's commit stands.
        /// </summary>
        public static bool RemoveVanillaPath(CourierParityAction action, in ExecutionPlan plan)
        {
            return action == CourierParityAction.Convert && plan.Execute;
        }

        /// <summary>
        /// The reserve-release producer's allocator view, without allocating: the
        /// direct child's intent (ArmyOrchestrator.CurrentDirectChildIntents; ids are
        /// "child-" / "synth-army-" + the group's GameObject instance id,
        /// CommandIntentResolver) re-roled out of Reserve, and the band it was sent to,
        /// DirectChildIntent.AxisSector (the crisis band or the main effort's band,
        /// DirectChildAllocator.ReleaseReserves). The command-node intent carries only
        /// the child's own band. Pair with a ledger state that HeldInReserve.
        /// </summary>
        public static bool TryReleasedDirectChild(IReadOnlyList<DirectChildIntent> intents, int instanceId, out int sector)
        {
            sector = 0;
            if (intents == null || instanceId == 0) return false;
            for (int i = 0; i < intents.Count; i++)
            {
                string id = intents[i].ChildId;
                if (!SuffixIs(id, "child-", instanceId) && !SuffixIs(id, "synth-army-", instanceId)) continue;
                DirectChildRole role = intents[i].Role;
                if (role == DirectChildRole.Reserve || role == DirectChildRole.Unknown) return false;
                sector = intents[i].AxisSector;
                return true;
            }
            return false;
        }

        // id == prefix + instanceId (canonical int form), without allocating.
        private static bool SuffixIs(string id, string prefix, int instanceId)
        {
            if (id == null || id.Length <= prefix.Length || string.CompareOrdinal(id, 0, prefix, 0, prefix.Length) != 0) return false;
            int i = prefix.Length;
            bool negative = id[i] == '-';
            if (negative) i++;
            if (i >= id.Length || id.Length - i > 10 || (id[i] == '0' && id.Length - i > 1)) return false;
            long value = 0;
            for (; i < id.Length; i++)
            {
                char c = id[i];
                if (c < '0' || c > '9') return false;
                value = value * 10 + (c - '0');
            }
            return (negative ? -value : value) == instanceId;
        }

        /// <summary>
        /// A released reserve's move: behind the centroid of its standing sibling
        /// groups in <paramref name="targetSector"/> (the allocator's crisis band, or
        /// the main effort's band), stood off <see cref="ReleaseStandOffMeters"/>
        /// against the front. No front axis or no own line there: no move.
        /// </summary>
        public static bool TryPlanRelease(BattleSnapshot s, int groupIndex, int targetSector, out BattleOrderIntent intent)
        {
            intent = default;
            if (s == null || groupIndex < 0 || groupIndex >= s.OwnCount) return false;
            if ((s.Own[groupIndex].Flags & (UnitFlags.PlayerOwned | UnitFlags.Feud)) != 0) return false;
            TacticalFrontAxis axis = s.FrontAxis;
            if (!axis.IsValid) return false;

            UnitRecord[] own = s.Own;
            int parent = own[groupIndex].ParentIndex;
            float sumX = 0f, sumZ = 0f;
            int count = 0;
            for (int j = 0; j < s.OwnCount; j++)
            {
                if (j == groupIndex || own[j].ParentIndex != parent) continue;
                UnitFlags f = own[j].Flags;
                if ((f & UnitFlags.IsGroup) == 0 || (f & UnitFlags.Alive) == 0 || (f & UnitFlags.Active) == 0 || (f & UnitFlags.Routed) != 0)
                    continue;
                if (axis.SectorOf(own[j].X, own[j].Z) != targetSector) continue;
                sumX += own[j].X;
                sumZ += own[j].Z;
                count++;
            }
            if (count == 0) return false;

            intent = new BattleOrderIntent
            {
                GroupId = own[groupIndex].InstanceId,
                Channel = OrderChannel.Move,
                TargetX = sumX / count - axis.ForwardX * ReleaseStandOffMeters,
                TargetZ = sumZ / count - axis.ForwardZ * ReleaseStandOffMeters,
                Heading = axis.HeadingDegrees,
                Priority = IntentPriority.Support,
                Source = IntentSource.ReserveRelease,
                Flags = IntentFlags.UseOrderDelay | IntentFlags.ReliefOrSupport,
                Task = CommandTaskType.ReleaseReserve,
                Reason = "reserve-release",
            };
            return true;
        }

        /// <summary>The ledger holds the node in Reserve (Reserve role or ReserveWait task).</summary>
        public static bool HeldInReserve(in CommandNodeOperationalState state)
        {
            return state.Role == CommandNodeRole.Reserve || state.Task == CommandTaskType.ReserveWait;
        }

        // Only "held in Reserve or not" matters to the gate and the release producer;
        // a ledger state that does not hold the node resolves to Unknown.
        private static CommandIntentResolution LedgerResolution(CommandNodeOperationalState state)
        {
            return new CommandIntentResolution(
                true,
                new CommandNodeIntent(
                    state.NodeId,
                    state.NodeId,
                    HeldInReserve(state) ? DirectChildRole.Reserve : DirectChildRole.Unknown,
                    DirectChildAxis.Hold,
                    primarySector: 0,
                    supportPriority: 0,
                    aggressionBias01: 0f,
                    depth: 0),
                "operations-ledger-command-state:" + state.Role + ":" + state.Task);
        }
    }
}
