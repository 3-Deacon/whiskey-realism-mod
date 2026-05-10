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
            if (intents == null || intents.Count == 0) return Array.Empty<CommandNodeOperationalState>();

            var states = new CommandNodeOperationalState[intents.Count];
            for (int i = 0; i < intents.Count; i++)
            {
                var intent = intents[i];
                var role = MapRole(intent.Role);
                var task = CommandNodeTaskPlanner.PlanTask(
                    role,
                    operationShape,
                    contact: false,
                    atObjective: false);

                states[i] = new CommandNodeOperationalState(
                    intent.NodeId,
                    MapEchelon(intent.Depth),
                    role,
                    task,
                    CommandTaskState.Planning);
            }

            return states;
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
