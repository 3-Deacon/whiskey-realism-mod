using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    internal static class CommandNodeOperationsRuntime
    {
        public static IReadOnlyList<CommandNodeOperationalState> Build(
            IReadOnlyList<CommandNodeIntent> intents,
            TacticalOperationShape operationShape)
        {
            return Build(
                intents,
                new OperationRecord(operationShape, TacticalOperationPhase.Planning, "objective-unknown", 0f),
                Array.Empty<ObjectiveRecord>(),
                BattleStance.Meeting);
        }

        public static IReadOnlyList<CommandNodeOperationalState> Build(
            IReadOnlyList<CommandNodeIntent> intents,
            OperationRecord operation,
            IReadOnlyList<ObjectiveRecord> objectives,
            BattleStance stance,
            ICommandGround ground = null)
        {
            if (intents == null || intents.Count == 0) return Array.Empty<CommandNodeOperationalState>();

            bool contact = HasObjectiveContact(operation, objectives);
            bool atObjective = IsAtObjective(operation, objectives);
            var states = new CommandNodeOperationalState[intents.Count];
            for (int i = 0; i < intents.Count; i++)
            {
                var intent = intents[i];
                var role = MapRole(intent.Role, stance);
                // A Defender's label is a label: its task is planned from the role the allocator meant (as before the label changed) and then held to
                // Hold by the stance guard below, so relabelling Main, SupportMain and Fix as Defender moves no Defender.
                var task = CommandNodeTaskPlanner.PlanTask(
                    stance == BattleStance.Defender ? PlanningRole(intent.Role) : role,
                    operation.Shape,
                    contact,
                    atObjective);
                // BC9 stance guard where node tasks are planned: every reader of the
                // army's command operations (#45's ledger branch, the posture pipeline,
                // TacticalCommandAssignment rows, the W&L player-order adapter) sees a
                // Defender's task without Probe or attack.
                task = TacticalDecisionDoctrine.TaskForStance(task, stance);

                states[i] = new CommandNodeOperationalState(
                    intent.NodeId,
                    MapEchelon(intent.Depth),
                    role,
                    task,
                    CommandTaskState.Planning);
                // Smoke 2026-10-17: a state built from the intent alone has no position, and every node was planned from the map origin. The
                // ground places it where the node stands; a node the ground does not know stays unplaced and is planned nowhere.
                if (ground != null && ground.TryPlace(intent.NodeId, out float x, out float z, out float facing))
                    states[i] = states[i].WithPosition(x, z, facing);
            }

            return states;
        }

        public static bool TryBuildSingle(
            CommandNodeIntent intent,
            OperationRecord operation,
            IReadOnlyList<ObjectiveRecord> objectives,
            BattleStance stance,
            out CommandNodeOperationalState state)
        {
            var states = Build(new[] { intent }, operation, objectives, stance);
            if (states.Count == 0)
            {
                state = default(CommandNodeOperationalState);
                return false;
            }

            state = states[0];
            return true;
        }

        private static bool HasObjectiveContact(OperationRecord operation, IReadOnlyList<ObjectiveRecord> objectives)
        {
            var record = FindPrimaryObjective(operation, objectives);
            if (!record.HasUsableStrengthEvidence) return false;

            return record.Status == TacticalObjectiveStatus.WeaklyHeld ||
                record.Status == TacticalObjectiveStatus.StronglyHeld ||
                record.Status == TacticalObjectiveStatus.Contested ||
                record.EnemyStrength > 0f;
        }

        private static bool IsAtObjective(OperationRecord operation, IReadOnlyList<ObjectiveRecord> objectives)
        {
            var record = FindPrimaryObjective(operation, objectives);
            return record.Status == TacticalObjectiveStatus.Contested ||
                record.Status == TacticalObjectiveStatus.Secured;
        }

        private static ObjectiveRecord FindPrimaryObjective(OperationRecord operation, IReadOnlyList<ObjectiveRecord> objectives)
        {
            if (objectives == null || objectives.Count == 0) return default(ObjectiveRecord);

            string primary = operation.PrimaryObjectiveId;
            for (int i = 0; i < objectives.Count; i++)
            {
                if (string.Equals(objectives[i].Observation.ObjectiveId, primary, StringComparison.Ordinal))
                    return objectives[i];
            }

            return objectives[0];
        }

        // The role the node task planner reads for a Defender's child: the pre-stance mapping (Main, SupportMain and Fix as the attack side's roles, a refused
        // flank as Defender).
        private static CommandNodeRole PlanningRole(DirectChildRole role)
        {
            switch (role)
            {
                case DirectChildRole.Main: return CommandNodeRole.MainEffort;
                case DirectChildRole.SupportMain: return CommandNodeRole.SupportingAttack;
                case DirectChildRole.Fix: return CommandNodeRole.FixingForce;
                default: return MapRole(role, BattleStance.Defender);
            }
        }

        /// <summary>
        /// The node role label for an allocated direct-child role on this side of the field. A Defender holds: its Main, SupportMain and Fix children are
        /// Defender nodes, never MainEffort, SupportingAttack or FixingForce (smoke 2026-10-17: a CSA brigade labelled SupportingAttack; the task was already
        /// held to Hold by <see cref="TacticalDecisionDoctrine.TaskForStance"/>, the label was not). BC13's Counterstroke is the one exception and does not come
        /// through here: ExploitLifecycle writes its own nodes. A side that is not defending guards a refused flank as a flank guard (FlankMarch, whose
        /// task is GuardFlank) and is never given the Defender role (smoke: Union nodes labelled Defender).
        /// </summary>
        internal static CommandNodeRole MapRole(DirectChildRole role, BattleStance stance)
        {
            bool defending = stance == BattleStance.Defender;
            switch (role)
            {
                case DirectChildRole.Main:
                    return defending ? CommandNodeRole.Defender : CommandNodeRole.MainEffort;
                case DirectChildRole.SupportMain:
                    return defending ? CommandNodeRole.Defender : CommandNodeRole.SupportingAttack;
                case DirectChildRole.Fix:
                    return defending ? CommandNodeRole.Defender : CommandNodeRole.FixingForce;
                case DirectChildRole.Screen:
                    return CommandNodeRole.ScreeningForce;
                case DirectChildRole.Reserve:
                    return CommandNodeRole.Reserve;
                case DirectChildRole.Fallback:
                    return CommandNodeRole.FallbackGuard;
                case DirectChildRole.RefuseLeft:
                case DirectChildRole.RefuseRight:
                    return defending ? CommandNodeRole.Defender : CommandNodeRole.FlankMarch;
                case DirectChildRole.Unknown:
                default:
                    return CommandNodeRole.Unknown;
            }
        }

        private static CommandEchelonKind MapEchelon(int depth)
        {
            if (depth <= 0) return CommandEchelonKind.ArmyLike;
            if (depth == 1) return CommandEchelonKind.CorpsLike;
            if (depth == 2) return CommandEchelonKind.DivisionLike;
            return CommandEchelonKind.BrigadeLike;
        }
    }
}
