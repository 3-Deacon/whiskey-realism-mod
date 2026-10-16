namespace WhiskeyRealism.Tactical.Operations
{
    using WhiskeyRealism.Tactical;

    public enum DoctrineAllowedIdleReason
    {
        None = 0,
        HeldReserve = 1,
        FormingUp = 2,
        DefendingObjective = 4,
        PlayerProtected = 6
    }

    public readonly struct CommandDoctrineOrder
    {
        private CommandDoctrineOrder(
            string nodeId,
            CommandNodeRole role,
            CommandTaskType task,
            string objectiveId,
            DoctrineTargetPoint primaryTarget,
            DoctrineTargetPoint supportTarget,
            DoctrineTargetPoint fallbackTarget,
            DoctrineAllowedIdleReason allowedIdle,
            float minCommitUntilSeconds,
            float issuedAtSeconds,
            float confidence01,
            TacticalSopDecision sop,
            TacticalOrderDelivery delivery,
            string parentNodeId,
            float courierIntervalSeconds,
            string reason,
            float maneuverHeading = -1f,
            bool maneuverHold = false,
            int exploitId = -1,
            bool hasVia = false,
            float viaX = 0f,
            float viaZ = 0f)
        {
            ExploitId = exploitId;
            HasVia = hasVia && IsFinite(viaX) && IsFinite(viaZ);
            _viaX = HasVia ? viaX : 0f;
            _viaZ = HasVia ? viaZ : 0f;
            ManeuverHeading = maneuverHeading >= 0f && IsFinite(maneuverHeading) ? maneuverHeading % 360f : -1f;
            ManeuverHold = maneuverHold;
            NodeId = SanitizeText(nodeId, "node-unknown");
            Role = role;
            Task = task;
            ObjectiveId = SanitizeText(objectiveId, "objective-unknown");
            PrimaryTarget = primaryTarget;
            SupportTarget = supportTarget;
            FallbackTarget = fallbackTarget;
            AllowedIdle = allowedIdle;
            MinCommitUntilSeconds = SanitizeNonNegative(minCommitUntilSeconds);
            IssuedAtSeconds = SanitizeNonNegative(issuedAtSeconds);
            Confidence01 = Clamp01(confidence01);
            Sop = sop.Authority == TacticalSopAuthority.None && task != CommandTaskType.None
                ? TacticalSopDecision.ForAssignedTask(role, task)
                : sop;
            Delivery = delivery == TacticalOrderDelivery.Unknown ? TacticalOrderDelivery.Immediate : delivery;
            ParentNodeId = SanitizeText(parentNodeId, string.Empty);
            CourierIntervalSeconds = SanitizePositive(courierIntervalSeconds, 900f);
            Reason = SanitizeText(reason, "unspecified");
        }

        public string NodeId { get; }
        public CommandNodeRole Role { get; }
        public CommandTaskType Task { get; }
        public string ObjectiveId { get; }
        public DoctrineTargetPoint PrimaryTarget { get; }
        public DoctrineTargetPoint SupportTarget { get; }
        public DoctrineTargetPoint FallbackTarget { get; }
        public DoctrineAllowedIdleReason AllowedIdle { get; }
        public float MinCommitUntilSeconds { get; }
        public float IssuedAtSeconds { get; }
        public float Confidence01 { get; }
        public TacticalSopDecision Sop { get; }
        public TacticalOrderDelivery Delivery { get; }
        public string ParentNodeId { get; }
        public float CourierIntervalSeconds { get; }
        public string Reason { get; }

        /// <summary>
        /// BC11 decision 4, additive: the bearing the order's group faces on arrival (EnvelopmentManeuver's Turn phase);
        /// -1 for no maneuver heading. The posture producer submits a group Facing with it.
        /// </summary>
        public float ManeuverHeading { get; }

        /// <summary>
        /// BC13, additive: the id of the exploit this order belongs to (ExploitTable's exploit order and its descendants'
        /// follow orders); -1 for every ordinary doctrine order.
        /// </summary>
        public int ExploitId { get; }

        private readonly float _viaX;
        private readonly float _viaZ;

        /// <summary>
        /// BC14, additive: the order carries a crossing via (the first crossing of the maneuver leg's chosen route). False is none,
        /// so default(CommandDoctrineOrder), the "no order" value, carries none.
        /// </summary>
        public bool HasVia { get; }

        /// <summary>The via's world x; NaN when there is no via.</summary>
        public float ViaX => HasVia ? _viaX : float.NaN;

        /// <summary>The via's world z; NaN when there is no via.</summary>
        public float ViaZ => HasVia ? _viaZ : float.NaN;

        /// <summary>BC11: the order is an EnvelopmentManeuver hold (stand where you are, no target to plan a route to).</summary>
        public bool ManeuverHold { get; }

        public bool HasPurpose { get { return Role != CommandNodeRole.Unknown || Task != CommandTaskType.None; } }
        public bool AllowsIdle { get { return AllowedIdle != DoctrineAllowedIdleReason.None; } }

        public static CommandDoctrineOrder Create(
            string nodeId,
            CommandNodeRole role,
            CommandTaskType task,
            string objectiveId,
            DoctrineTargetPoint primaryTarget,
            DoctrineTargetPoint supportTarget,
            DoctrineTargetPoint fallbackTarget,
            DoctrineAllowedIdleReason allowedIdle,
            float minCommitUntilSeconds,
            float issuedAtSeconds,
            float confidence01,
            string reason)
        {
            return new CommandDoctrineOrder(
                nodeId,
                role,
                task,
                objectiveId,
                primaryTarget,
                supportTarget,
                fallbackTarget,
                allowedIdle,
                minCommitUntilSeconds,
                issuedAtSeconds,
                confidence01,
                TacticalSopDecision.ForAssignedTask(role, task),
                TacticalOrderDelivery.Immediate,
                string.Empty,
                900f,
                reason);
        }

        public CommandDoctrineOrder WithSop(TacticalSopDecision sop)
        {
            return new CommandDoctrineOrder(
                NodeId,
                Role,
                Task,
                ObjectiveId,
                PrimaryTarget,
                SupportTarget,
                FallbackTarget,
                AllowedIdle,
                MinCommitUntilSeconds,
                IssuedAtSeconds,
                Confidence01,
                sop,
                Delivery,
                ParentNodeId,
                CourierIntervalSeconds,
                Reason,
                ManeuverHeading,
                ManeuverHold,
                ExploitId,
                HasVia, _viaX, _viaZ);
        }

        public CommandDoctrineOrder WithTask(CommandTaskType task, string reason)
        {
            return new CommandDoctrineOrder(
                NodeId,
                Role,
                task,
                ObjectiveId,
                PrimaryTarget,
                SupportTarget,
                FallbackTarget,
                AllowedIdle,
                MinCommitUntilSeconds,
                IssuedAtSeconds,
                Confidence01,
                TacticalSopDecision.ForAssignedTask(Role, task),
                Delivery,
                ParentNodeId,
                CourierIntervalSeconds,
                reason,
                ManeuverHeading,
                ManeuverHold,
                ExploitId,
                HasVia, _viaX, _viaZ);
        }

        /// <summary>
        /// BC11 decision 4: the order a maneuvering child's nodes carry. Replaces the primary target, the allowed idle
        /// (a hold is FormingUp) and the maneuver heading; the SOP is resolved for the task by the caller.
        /// </summary>
        public CommandDoctrineOrder WithManeuver(DoctrineTargetPoint primaryTarget, DoctrineAllowedIdleReason allowedIdle,
            float maneuverHeading, bool maneuverHold, string reason)
        {
            return new CommandDoctrineOrder(
                NodeId,
                Role,
                Task,
                ObjectiveId,
                primaryTarget,
                SupportTarget,
                FallbackTarget,
                allowedIdle,
                MinCommitUntilSeconds,
                IssuedAtSeconds,
                Confidence01,
                Sop,
                Delivery,
                ParentNodeId,
                CourierIntervalSeconds,
                reason,
                maneuverHeading,
                maneuverHold,
                ExploitId,
                HasVia, _viaX, _viaZ);
        }

        /// <summary>BC13: the same order marked as exploit <paramref name="exploitId"/>'s (see <see cref="ExploitId"/>).</summary>
        public CommandDoctrineOrder WithExploit(int exploitId)
        {
            return new CommandDoctrineOrder(
                NodeId,
                Role,
                Task,
                ObjectiveId,
                PrimaryTarget,
                SupportTarget,
                FallbackTarget,
                AllowedIdle,
                MinCommitUntilSeconds,
                IssuedAtSeconds,
                Confidence01,
                Sop,
                Delivery,
                ParentNodeId,
                CourierIntervalSeconds,
                Reason,
                ManeuverHeading,
                ManeuverHold,
                exploitId,
                HasVia, _viaX, _viaZ);
        }

        /// <summary>
        /// BC14 requirement-crossing-via: the same order with a crossing via. A March or an Attack carrying a via is issued as the
        /// two-leg move (BC10's via0). Every other field is forwarded.
        /// </summary>
        public CommandDoctrineOrder WithVia(float x, float z)
        {
            return new CommandDoctrineOrder(
                NodeId,
                Role,
                Task,
                ObjectiveId,
                PrimaryTarget,
                SupportTarget,
                FallbackTarget,
                AllowedIdle,
                MinCommitUntilSeconds,
                IssuedAtSeconds,
                Confidence01,
                Sop,
                Delivery,
                ParentNodeId,
                CourierIntervalSeconds,
                Reason,
                ManeuverHeading,
                ManeuverHold,
                ExploitId,
                true, x, z);
        }

        private static string SanitizeText(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static float SanitizeNonNegative(float value)
        {
            return IsFinite(value) && value > 0f ? value : 0f;
        }

        private static float SanitizePositive(float value, float fallback)
        {
            return IsFinite(value) && value > 0f ? value : fallback;
        }

        private static float Clamp01(float value)
        {
            if (!IsFinite(value) || value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>One node's maneuver override (see <see cref="ManeuverNodeTable"/>).</summary>
    public readonly struct ManeuverNodeEntry
    {
        public ManeuverNodeEntry(string nodeId, WhiskeyRealism.Tactical.Orchestrator.Plays.ManeuverPhase phase, CommandTaskType task,
            bool hasTarget, float targetX, float targetZ, float heading, string reason, bool hasVia = false, float viaX = 0f, float viaZ = 0f,
            bool isGuard = false)
        {
            IsGuard = isGuard;
            HasVia = hasVia;
            ViaX = viaX;
            ViaZ = viaZ;
            NodeId = nodeId ?? string.Empty;
            Phase = phase;
            Task = task;
            HasTarget = hasTarget;
            TargetX = targetX;
            TargetZ = targetZ;
            Heading = heading;
            Reason = reason ?? "maneuver";
        }

        public string NodeId { get; }
        public WhiskeyRealism.Tactical.Orchestrator.Plays.ManeuverPhase Phase { get; }
        public CommandTaskType Task { get; }
        public bool HasTarget { get; }
        public float TargetX { get; }
        public float TargetZ { get; }
        public float Heading { get; }
        public string Reason { get; }

        /// <summary>BC14: the crossing the node's move is routed through (requirement-crossing-via); HasVia false is none.</summary>
        public bool HasVia { get; }
        public float ViaX { get; }
        public float ViaZ { get; }

        /// <summary>
        /// BC14: the entry is a reserve's crossing guard (requirement-defender-crossing-release), not a BC11 maneuver: the doctrine build applies it
        /// to a Defender's node too (a guard holds ground; it attacks no one).
        /// </summary>
        public bool IsGuard { get; }
        public bool Holds => Phase == WhiskeyRealism.Tactical.Orchestrator.Plays.ManeuverPhase.Hold;
    }

    /// <summary>
    /// BC11 decision 4: the side's maneuver table, keyed by command-node id and covering every node of each maneuvering
    /// child (EnvelopmentManeuver.ExpandToNodes). CommandDoctrineAssignment.Build overrides the task, the primary target
    /// and the maneuver heading of the nodes it names.
    /// </summary>
    public sealed class ManeuverNodeTable
    {
        private readonly System.Collections.Generic.Dictionary<string, ManeuverNodeEntry> _byNode =
            new System.Collections.Generic.Dictionary<string, ManeuverNodeEntry>(System.StringComparer.Ordinal);

        public int Count => _byNode.Count;

        public void Add(in ManeuverNodeEntry entry)
        {
            if (!string.IsNullOrEmpty(entry.NodeId)) _byNode[entry.NodeId] = entry;
        }

        public bool TryGet(string nodeId, out ManeuverNodeEntry entry)
        {
            entry = default(ManeuverNodeEntry);
            return nodeId != null && _byNode.TryGetValue(nodeId, out entry);
        }
    }
}
