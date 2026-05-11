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
                Array.Empty<ObjectiveRecord>());
        }

        public static IReadOnlyList<CommandNodeOperationalState> Build(
            IReadOnlyList<CommandNodeIntent> intents,
            OperationRecord operation,
            IReadOnlyList<ObjectiveRecord> objectives)
        {
            if (intents == null || intents.Count == 0) return Array.Empty<CommandNodeOperationalState>();

            bool contact = HasObjectiveContact(operation, objectives);
            bool atObjective = IsAtObjective(operation, objectives);
            var states = new CommandNodeOperationalState[intents.Count];
            for (int i = 0; i < intents.Count; i++)
            {
                var intent = intents[i];
                var role = MapRole(intent.Role);
                var task = CommandNodeTaskPlanner.PlanTask(
                    role,
                    operation.Shape,
                    contact,
                    atObjective);

                states[i] = new CommandNodeOperationalState(
                    intent.NodeId,
                    MapEchelon(intent.Depth),
                    role,
                    task,
                    CommandTaskState.Planning);
            }

            return states;
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

        private static CommandNodeRole MapRole(DirectChildRole role)
        {
            switch (role)
            {
                case DirectChildRole.Main:
                    return CommandNodeRole.MainEffort;
                case DirectChildRole.SupportMain:
                    return CommandNodeRole.SupportingAttack;
                case DirectChildRole.Fix:
                    return CommandNodeRole.FixingForce;
                case DirectChildRole.Screen:
                    return CommandNodeRole.ScreeningForce;
                case DirectChildRole.Reserve:
                    return CommandNodeRole.Reserve;
                case DirectChildRole.Fallback:
                    return CommandNodeRole.FallbackGuard;
                case DirectChildRole.RefuseLeft:
                case DirectChildRole.RefuseRight:
                    return CommandNodeRole.Defender;
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
