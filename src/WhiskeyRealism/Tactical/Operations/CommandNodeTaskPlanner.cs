namespace WhiskeyRealism.Tactical.Operations
{
    public static class CommandNodeTaskPlanner
    {
        public static CommandTaskType PlanTask(
            CommandNodeRole role,
            TacticalOperationShape shape,
            bool contact,
            bool atObjective)
        {
            switch (role)
            {
                case CommandNodeRole.Reserve:
                    return CommandTaskType.ReserveWait;
                case CommandNodeRole.Defender:
                    return atObjective ? CommandTaskType.HoldObjective : CommandTaskType.AdvanceToAssembly;
                case CommandNodeRole.FallbackGuard:
                    return CommandTaskType.FallBackToLine;
                case CommandNodeRole.FixingForce:
                    return contact ? CommandTaskType.FixEnemy : CommandTaskType.AdvanceToAssembly;
                case CommandNodeRole.ScreeningForce:
                    return CommandTaskType.Screen;
                case CommandNodeRole.Probe:
                    return CommandTaskType.Probe;
                case CommandNodeRole.MainEffort:
                    return shape == TacticalOperationShape.DefensiveNetwork
                        ? CommandTaskType.HoldObjective
                        : CommandTaskType.AttackObjective;
                case CommandNodeRole.SupportingAttack:
                    return CommandTaskType.SupportAttack;
                default:
                    return CommandTaskType.FormUp;
            }
        }
    }
}
