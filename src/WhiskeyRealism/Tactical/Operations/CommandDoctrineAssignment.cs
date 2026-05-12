using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Operations
{
    public static class CommandDoctrineAssignment
    {
        public static CommandDoctrineOrder[] Build(
            CommandNodeOperationalState[] nodes,
            OperationRecord operation,
            BattlefieldPictureSnapshot picture,
            float ownStrength,
            float nowSeconds)
        {
            if (nodes == null || nodes.Length == 0) return Array.Empty<CommandDoctrineOrder>();

            bool objectiveMatched;
            BattlefieldObjectiveEstimate objective = ResolveObjective(
                operation,
                picture.Objectives,
                out objectiveMatched);
            float odds = ResolveOdds(ownStrength, objective.EnemyStrength);
            var orders = new List<CommandDoctrineOrder>(nodes.Length);

            for (int i = 0; i < nodes.Length; i++)
            {
                CommandNodeOperationalState node = nodes[i];
                CommandTaskType task = ResolveTask(node.Role, operation, objective, objectiveMatched, odds);
                DoctrineAllowedIdleReason idle = ResolveIdle(node.Role, task, operation);
                DoctrineTargetPoint primary = ResolvePrimaryTarget(task, objective);
                DoctrineTargetPoint fallback = ResolveFallbackTarget(task, node, objective);

                orders.Add(CommandDoctrineOrder.Create(
                    node.NodeId,
                    node.Role,
                    task,
                    objective.ObjectiveId,
                    primary,
                    DoctrineTargetPoint.None,
                    fallback,
                    idle,
                    operation.MinimumCommitSeconds,
                    nowSeconds,
                    objective.Confidence01,
                    "doctrine-assignment"));
            }

            return orders.ToArray();
        }

        private static CommandTaskType ResolveTask(
            CommandNodeRole role,
            OperationRecord operation,
            BattlefieldObjectiveEstimate objective,
            bool objectiveMatched,
            float odds)
        {
            if (role == CommandNodeRole.Unknown) return CommandTaskType.None;

            if (operation.Phase == TacticalOperationPhase.SoftAbort ||
                operation.Shape == TacticalOperationShape.DelayAndFallback)
            {
                if (role == CommandNodeRole.FallbackGuard || odds < 0.85f)
                {
                    return CommandTaskType.FallBackToLine;
                }
            }

            if (role == CommandNodeRole.Reserve) return CommandTaskType.ReserveWait;
            if (!objectiveMatched) return CommandTaskType.FormUp;

            if (role == CommandNodeRole.MainEffort &&
                IsCommittedFallbackEnemyLine(operation))
            {
                return CommandTaskType.AttackObjective;
            }

            if (role == CommandNodeRole.SupportingAttack &&
                IsCommittedFallbackEnemyLine(operation))
            {
                return CommandTaskType.SupportAttack;
            }

            if (role == CommandNodeRole.MainEffort &&
                objective.MainLineExposed &&
                MainEffortConfidenceAllowsAttack(operation, objective) &&
                odds >= 1.25f)
            {
                return CommandTaskType.AttackObjective;
            }

            if (role == CommandNodeRole.SupportingAttack &&
                objective.MainLineExposed &&
                odds >= 1.15f)
            {
                return CommandTaskType.SupportAttack;
            }

            if (role == CommandNodeRole.FixingForce && objective.MainLineExposed)
            {
                return CommandTaskType.FixEnemy;
            }

            if (role == CommandNodeRole.ScreeningForce) return CommandTaskType.Screen;

            return CommandTaskType.FormUp;
        }

        private static bool MainEffortConfidenceAllowsAttack(
            OperationRecord operation,
            BattlefieldObjectiveEstimate objective)
        {
            if (objective.Confidence01 >= 0.65f) return true;
            return operation.Phase == TacticalOperationPhase.Committed &&
                objective.Type == TacticalObjectiveType.EnemyLine &&
                objective.Confidence01 >= 0.50f;
        }

        private static DoctrineAllowedIdleReason ResolveIdle(
            CommandNodeRole role,
            CommandTaskType task,
            OperationRecord operation)
        {
            if (role == CommandNodeRole.Reserve && task == CommandTaskType.ReserveWait)
            {
                return DoctrineAllowedIdleReason.HeldReserve;
            }

            if (task == CommandTaskType.HoldObjective)
            {
                return DoctrineAllowedIdleReason.DefendingObjective;
            }

            if (operation.Phase == TacticalOperationPhase.Forming && task == CommandTaskType.FormUp)
            {
                return DoctrineAllowedIdleReason.FormingUp;
            }

            return DoctrineAllowedIdleReason.None;
        }

        private static DoctrineTargetPoint ResolvePrimaryTarget(
            CommandTaskType task,
            BattlefieldObjectiveEstimate objective)
        {
            if (task == CommandTaskType.AttackObjective ||
                task == CommandTaskType.SupportAttack ||
                task == CommandTaskType.FixEnemy ||
                task == CommandTaskType.Screen)
            {
                return DoctrineTargetPoint.From(objective.X, objective.Z);
            }

            return DoctrineTargetPoint.None;
        }

        private static DoctrineTargetPoint ResolveFallbackTarget(
            CommandTaskType task,
            CommandNodeOperationalState node,
            BattlefieldObjectiveEstimate objective)
        {
            if (task != CommandTaskType.FallBackToLine) return DoctrineTargetPoint.None;

            double dx = (double)node.X - objective.X;
            double dz = (double)node.Z - objective.Z;
            double length = Math.Sqrt(dx * dx + dz * dz);
            if (double.IsNaN(length) || double.IsInfinity(length) || length < 1d)
            {
                return DoctrineTargetPoint.From(node.X, ClampToFloat((double)node.Z - 300d));
            }

            return DoctrineTargetPoint.From(
                ClampToFloat(node.X + dx / length * 300d),
                ClampToFloat(node.Z + dz / length * 300d));
        }

        private static float ClampToFloat(double value)
        {
            if (double.IsNaN(value)) return 0f;
            if (value > float.MaxValue) return float.MaxValue;
            if (value < -float.MaxValue) return -float.MaxValue;
            return (float)value;
        }

        private static BattlefieldObjectiveEstimate ResolveObjective(
            OperationRecord operation,
            BattlefieldObjectiveEstimate[] objectives,
            out bool objectiveMatched)
        {
            objectiveMatched = false;
            objectives = objectives ?? Array.Empty<BattlefieldObjectiveEstimate>();
            string objectiveId = operation.PrimaryObjectiveId;
            for (int i = 0; i < objectives.Length; i++)
            {
                if (string.Equals(objectives[i].ObjectiveId, objectiveId, StringComparison.Ordinal))
                {
                    objectiveMatched = !string.Equals(
                        objectives[i].ObjectiveId,
                        "objective-unknown",
                        StringComparison.Ordinal);
                    return objectives[i];
                }
            }

            if (objectives.Length > 0)
            {
                if (IsCommittedFallbackEnemyLine(operation))
                {
                    objectiveMatched = true;
                }

                return objectives[0];
            }

            return new BattlefieldObjectiveEstimate(
                "objective-unknown",
                TacticalObjectiveType.UnknownVanillaObjective,
                0f,
                0f,
                false,
                0f,
                0f,
                0f,
                0f,
                0f);
        }

        private static bool IsCommittedFallbackEnemyLine(OperationRecord operation)
        {
            return operation.Phase == TacticalOperationPhase.Committed &&
                !string.IsNullOrWhiteSpace(operation.PrimaryObjectiveId) &&
                operation.PrimaryObjectiveId.StartsWith("enemy-line-", StringComparison.Ordinal);
        }

        private static float ResolveOdds(float ownStrength, float enemyStrength)
        {
            if (float.IsNaN(ownStrength) || float.IsInfinity(ownStrength) || ownStrength < 0f)
            {
                ownStrength = 0f;
            }

            return ownStrength / Math.Max(1f, enemyStrength);
        }
    }
}
