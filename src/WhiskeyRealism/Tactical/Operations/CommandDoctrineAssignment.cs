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
            float nowSeconds,
            float reserveFraction = 0f)
        {
            if (nodes == null || nodes.Length == 0) return Array.Empty<CommandDoctrineOrder>();

            bool objectiveMatched;
            BattlefieldObjectiveEstimate objective = ResolveObjective(
                operation,
                picture.Objectives,
                out objectiveMatched);
            float odds = ResolveOdds(ownStrength, objective.EnemyStrength);
            TacticalBattlefrontSnapshot battlefront = TacticalBattlefrontGeometry.Build(nodes, objective);
            TacticalMassingDecision massing = TacticalMassingCycle.Evaluate(new TacticalMassingInput(
                operation.Phase,
                CountRole(nodes, CommandNodeRole.MainEffort),
                CountRole(nodes, CommandNodeRole.SupportingAttack),
                CountRole(nodes, CommandNodeRole.Reserve),
                artilleryReady: true,
                objectiveExposed: objective.MainLineExposed,
                objectiveConfidence01: objective.Confidence01,
                ownStrength: ownStrength,
                enemyStrength: objective.EnemyStrength,
                reserveFraction: reserveFraction));
            var orders = new List<CommandDoctrineOrder>(nodes.Length);

            for (int i = 0; i < nodes.Length; i++)
            {
                CommandNodeOperationalState node = nodes[i];
                CommandTaskType task = ResolveTask(node.Role, operation, objective, objectiveMatched, odds);
                task = ApplyScourgeSlotTask(task, node.Role, i, nodes.Length);
                DoctrineAllowedIdleReason idle = ResolveIdle(node.Role, task, operation);
                TacticalBattleLinePlan linePlan = TacticalBattleLinePlanner.PlanNode(
                    node,
                    task,
                    objective,
                    objectiveMatched,
                    i,
                    battlefront,
                    nodes.Length);
                TacticalSopDecision sop = TacticalSopDoctrine.Resolve(
                    node.Role,
                    task,
                    operation.Phase,
                    objective,
                    ownStrength,
                    reserveFraction);
                sop = ApplyOperationalGates(sop, task, massing, battlefront);

                orders.Add(CommandDoctrineOrder.Create(
                    node.NodeId,
                    node.Role,
                    task,
                    objective.ObjectiveId,
                    linePlan.PrimaryTarget,
                    linePlan.SupportTarget,
                    linePlan.FallbackTarget,
                    idle,
                    operation.MinimumCommitSeconds,
                    nowSeconds,
                    objective.Confidence01,
                    linePlan.Reason).WithSop(sop));
            }

            return orders.ToArray();
        }

        private static CommandTaskType ApplyScourgeSlotTask(
            CommandTaskType task,
            CommandNodeRole role,
            int commandIndex,
            int nodeCount)
        {
            if (role == CommandNodeRole.FlankMarch &&
                (task == CommandTaskType.FormUp || task == CommandTaskType.AdvanceToAssembly))
            {
                return CommandTaskType.GuardFlank;
            }

            if (nodeCount < 6) return task;
            if (task != CommandTaskType.FormUp &&
                task != CommandTaskType.AdvanceToAssembly)
            {
                return task;
            }

            int safeIndex = commandIndex < 0 ? 0 : commandIndex;
            if (safeIndex == 0 || safeIndex == nodeCount - 1)
            {
                return CommandTaskType.GuardFlank;
            }

            return task;
        }

        private static TacticalSopDecision ApplyOperationalGates(
            TacticalSopDecision sop,
            CommandTaskType task,
            TacticalMassingDecision massing,
            TacticalBattlefrontSnapshot battlefront)
        {
            bool assaultTask = task == CommandTaskType.AttackObjective ||
                task == CommandTaskType.SupportAttack;

            if (!assaultTask) return sop;
            if (sop.Authority == TacticalSopAuthority.Probe ||
                sop.Authority == TacticalSopAuthority.Screen ||
                sop.Authority == TacticalSopAuthority.Fallback)
            {
                return sop;
            }

            if (massing.Phase == TacticalMassingPhase.MassSupport && !massing.MayCommitAssault)
            {
                return new TacticalSopDecision(
                    TacticalSopAuthority.Attack,
                    false,
                    true,
                    true,
                    Math.Min(sop.RiskBudget01, 0.45f),
                    Math.Max(45f, sop.ReacquireSeconds),
                    "massing-" + massing.Reason);
            }

            if (battlefront.Gap == TacticalBattlefrontGap.WideUnsupportedGap &&
                sop.Authority == TacticalSopAuthority.Attack)
            {
                return new TacticalSopDecision(
                    TacticalSopAuthority.Attack,
                    false,
                    true,
                    true,
                    Math.Min(sop.RiskBudget01, 0.40f),
                    Math.Max(45f, sop.ReacquireSeconds),
                    "battlefront-wide-gap");
            }

            return sop;
        }

        private static int CountRole(CommandNodeOperationalState[] nodes, CommandNodeRole role)
        {
            int count = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].Role == role) count++;
            }

            return count;
        }

        private static CommandTaskType ResolveTask(
            CommandNodeRole role,
            OperationRecord operation,
            BattlefieldObjectiveEstimate objective,
            bool objectiveMatched,
            float odds)
        {
            if (role == CommandNodeRole.Unknown) return CommandTaskType.FormUp;

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

            if (operation.Phase == TacticalOperationPhase.Scouting &&
                TacticalDecisionDoctrine.IsReconnaissanceContact(objective))
            {
                return TacticalDecisionDoctrine.ReconnaissanceTaskFor(role);
            }

            if (role == CommandNodeRole.Probe) return CommandTaskType.Probe;

            if (ShouldAdvanceCommittedMapObjective(operation, objective))
            {
                if (role == CommandNodeRole.MainEffort) return CommandTaskType.AttackObjective;
                if (role == CommandNodeRole.SupportingAttack) return CommandTaskType.SupportAttack;
                if (role == CommandNodeRole.FixingForce) return CommandTaskType.FixEnemy;
                if (role == CommandNodeRole.ScreeningForce) return CommandTaskType.Screen;
            }

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
            return TacticalDecisionDoctrine.HasAttackableLineEvidence(operation, objective);
        }

        private static bool ShouldAdvanceCommittedMapObjective(
            OperationRecord operation,
            BattlefieldObjectiveEstimate objective)
        {
            if (operation.Phase != TacticalOperationPhase.Committed) return false;
            if (operation.Shape == TacticalOperationShape.DefensiveNetwork ||
                operation.Shape == TacticalOperationShape.DelayAndFallback)
            {
                return false;
            }

            if (string.Equals(objective.ObjectiveId, "objective-unknown", StringComparison.Ordinal))
                return false;

            if (float.IsNaN(objective.X) || float.IsInfinity(objective.X) ||
                float.IsNaN(objective.Z) || float.IsInfinity(objective.Z))
            {
                return false;
            }

            return objective.Confidence01 >= 0.35f ||
                objective.Type == TacticalObjectiveType.VictoryPoint ||
                objective.Type == TacticalObjectiveType.Bridge ||
                objective.Type == TacticalObjectiveType.Ford ||
                objective.Type == TacticalObjectiveType.RoadJunction ||
                objective.Type == TacticalObjectiveType.Town ||
                objective.Type == TacticalObjectiveType.Ridge ||
                objective.Type == TacticalObjectiveType.ChokePoint;
        }

        private static DoctrineAllowedIdleReason ResolveIdle(
            CommandNodeRole role,
            CommandTaskType task,
            OperationRecord operation)
        {
            if (role == CommandNodeRole.Unknown)
            {
                return DoctrineAllowedIdleReason.None;
            }

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
                if (IsCommittedFallbackEnemyLine(operation) ||
                    IsUnknownPrimary(operation.PrimaryObjectiveId))
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

        private static bool IsUnknownPrimary(string objectiveId)
        {
            return string.IsNullOrWhiteSpace(objectiveId) ||
                string.Equals(objectiveId, "objective-unknown", StringComparison.Ordinal);
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
