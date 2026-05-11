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
        public float X { get; }
        public float Z { get; }
        public float FacingDegrees { get; }

        public CommandNodeOperationalState(
            string nodeId,
            CommandEchelonKind echelon,
            CommandNodeRole role,
            CommandTaskType task,
            CommandTaskState taskState)
            : this(nodeId, echelon, role, task, taskState, 0f, 0f, 0f)
        {
        }

        public CommandNodeOperationalState(
            string nodeId,
            CommandEchelonKind echelon,
            CommandNodeRole role,
            CommandTaskType task,
            CommandTaskState taskState,
            float x,
            float z,
            float facingDegrees)
        {
            NodeId = string.IsNullOrWhiteSpace(nodeId) ? "node-unknown" : nodeId;
            Echelon = echelon;
            Role = role;
            Task = task;
            TaskState = taskState;
            X = SanitizeFinite(x);
            Z = SanitizeFinite(z);
            FacingDegrees = NormalizeFacing(facingDegrees);
        }

        public static CommandNodeOperationalState Create(
            string nodeId,
            CommandEchelonKind echelon,
            CommandNodeRole role,
            CommandTaskType task,
            float x,
            float z,
            float facingDegrees)
        {
            return new CommandNodeOperationalState(
                nodeId,
                echelon,
                role,
                task,
                CommandTaskState.Planning,
                x,
                z,
                facingDegrees);
        }

        private static float SanitizeFinite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private static float NormalizeFacing(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            value %= 360f;
            return value < 0f ? value + 360f : value;
        }
    }
}
