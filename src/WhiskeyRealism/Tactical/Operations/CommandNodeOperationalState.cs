namespace WhiskeyRealism.Tactical.Operations
{
    public enum CommandNodeRole
    {
        Unknown,
        MainEffort,
        SupportingAttack,
        FixingForce,
        ScreeningForce,
        Reserve,
        Defender,
        FallbackGuard,
        Probe,
        FlankMarch
    }

    public enum CommandTaskType
    {
        None,
        Scout,
        Probe,
        Screen,
        FormUp,
        AdvanceToAssembly,
        AttackObjective,
        FixEnemy,
        SupportAttack,
        HoldObjective,
        HoldChoke,
        GuardFlank,
        ReserveWait,
        ReleaseReserve,
        FallBackToLine,
        Delay,
        Consolidate,
        RecoverStuckOrder
    }

    public enum CommandTaskState
    {
        Planning,
        MovingToAssembly,
        Forming,
        WaitingForCommit,
        Committed,
        Engaged,
        Reorganizing,
        Complete,
        Failed
    }

    public enum CommandEchelonKind
    {
        Unknown,
        ArmyLike,
        CorpsLike,
        DivisionLike,
        BrigadeLike
    }

    public readonly struct CommandNodeOperationalState
    {
        public string NodeId { get; }
        public CommandEchelonKind Echelon { get; }
        public CommandNodeRole Role { get; }
        public CommandTaskType Task { get; }
        public CommandTaskState TaskState { get; }

        public CommandNodeOperationalState(
            string nodeId,
            CommandEchelonKind echelon,
            CommandNodeRole role,
            CommandTaskType task,
            CommandTaskState taskState)
        {
            NodeId = string.IsNullOrWhiteSpace(nodeId) ? "node-unknown" : nodeId;
            Echelon = echelon;
            Role = role;
            Task = task;
            TaskState = taskState;
        }
    }
}
