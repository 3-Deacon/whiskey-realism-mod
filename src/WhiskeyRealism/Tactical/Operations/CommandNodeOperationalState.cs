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
        /// <summary>
        /// BC13: a Defender's bounded local counterstroke against a penetration (requirement-defender-counterstroke). Created
        /// only by an exploit order whose opening is a Penetration; the Defender stance guard passes it, the stance mapper
        /// gives attack (3), and the #45 scorer has no opinion on it.
        /// </summary>
        Counterstroke
    }

    public enum CommandTaskState
    {
        Planning,
        Committed
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

        /// <summary>
        /// BC14W (requirement-works-hold), additive: why the state's task is what it is when something other than the ledger set it. Only
        /// <c>works-hold</c> exists (<see cref="WorksHoldGuard.Reason"/>): the works guard turned an attack into a hold. Null for every state
        /// the ledger, the fallback builder or the shipped test constructors give.
        /// </summary>
        public string Reason { get; }

        /// <summary>BC14W: the task the works guard replaced (AttackObjective, SupportAttack or FixEnemy); None when nothing was replaced.</summary>
        public CommandTaskType PreGuardTask { get; }

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
            float facingDegrees,
            string reason = null,
            CommandTaskType preGuardTask = CommandTaskType.None)
        {
            Reason = reason;
            PreGuardTask = preGuardTask;
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

        /// <summary>
        /// The same state with another role and task (the posture pipeline's rewrites). The works-hold marks ride only with a hold: a state
        /// rewritten to any other task (a probe, a recon screen) is no longer the guard's, so the stance rule and the contest, which key on
        /// the task and the reason together, stop treating it as one.
        /// </summary>
        public CommandNodeOperationalState WithRoleAndTask(CommandNodeRole role, CommandTaskType task)
        {
            bool hold = task == CommandTaskType.HoldObjective;
            return new CommandNodeOperationalState(NodeId, Echelon, role, task, TaskState, X, Z, FacingDegrees,
                hold ? Reason : null, hold ? PreGuardTask : CommandTaskType.None);
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
