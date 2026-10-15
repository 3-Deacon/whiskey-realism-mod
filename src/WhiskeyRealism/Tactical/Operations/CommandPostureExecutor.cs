namespace WhiskeyRealism.Tactical.Operations
{
    public enum PostureExecutionAction
    {
        NoWrite,
        SetFormation,
        SetWaypoint,
        SetFormationAndWaypoint,
        ChangeStance,
        ReleaseReserve,
        FallbackToLine,
        RecoverInterruptedOrder
    }

    public enum PostureExecutionTarget
    {
        None,
        CurrentPosition,
        AssemblyArea,
        ObjectiveApproach,
        ReserveArea,
        FallbackLine,
        RecoveryPath,
        ReleasePoint,
        DoctrinePrimaryTarget,
        DoctrineSupportTarget,
        DoctrineFallbackTarget
    }

    public readonly struct PostureExecutionDecision
    {
        public PostureExecutionDecision(
            PostureExecutionAction action,
            string reason,
            PostureExecutionTarget target = PostureExecutionTarget.None,
            bool clearInterruptedPaths = false)
        {
            Action = action;
            Reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;
            Target = target;
            ClearInterruptedPaths = clearInterruptedPaths;
        }

        public PostureExecutionAction Action { get; }
        public string Reason { get; }
        public PostureExecutionTarget Target { get; }
        public bool ClearInterruptedPaths { get; }
    }

    public readonly struct WriteEligibilitySnapshot
    {
        public WriteEligibilitySnapshot(
            bool modeAllowsWrites,
            bool playerProtected,
            bool routed,
            bool orderPending,
            bool recentOrder,
            bool alreadyDoingCorrectTask = false,
            bool atAssignedLocation = false,
            bool missingLedgerAssignment = false,
            bool closeEngaged = false)
        {
            ModeAllowsWrites = modeAllowsWrites;
            PlayerProtected = playerProtected;
            Routed = routed;
            OrderPending = orderPending;
            RecentOrder = recentOrder;
            AlreadyDoingCorrectTask = alreadyDoingCorrectTask;
            AtAssignedLocation = atAssignedLocation;
            MissingLedgerAssignment = missingLedgerAssignment;
            CloseEngaged = closeEngaged;
        }

        public bool ModeAllowsWrites { get; }
        public bool PlayerProtected { get; }
        public bool Routed { get; }
        public bool OrderPending { get; }
        public bool RecentOrder { get; }
        public bool AlreadyDoingCorrectTask { get; }
        public bool AtAssignedLocation { get; }
        public bool MissingLedgerAssignment { get; }
        public bool CloseEngaged { get; }
    }

    // Battle-level gate for the #61 posture executor (Postfix on
    // AIBattle.AdjustGroupFormations).
    // Vanilla anchors: AdjustGroupFormations itself returns for state < 5 or
    // macroai < 0 (5879); CheckGlobalAIStrategy returns once macroai == 3
    // (6374) and vanilla sets every group to screen for the army retreat (4258);
    // BattleUnits.eodcycle > 0 is the end-of-day phase where vanilla stops
    // issuing AI orders (8259, 83526) and autosaves (66904); UpdateAITasks (5857)
    // runs every instance under ai_vs_ai, and isplayeraiorfeud == 2 is the W&L
    // feud instance (SetSide 3408) that duplicates side 0's cycle.
    public static class PostureExecutorBattleGate
    {
        public static bool Allows(int state, int macroAi, int eodCycle, int isPlayerAiOrFeud, bool aiVsAi, out string reason)
        {
            if (state < 5) { reason = "battle-not-started"; return false; }
            if (macroAi < 0) { reason = "macro-inactive"; return false; }
            if (macroAi == 3) { reason = "vanilla-retreat"; return false; }
            if (eodCycle > 0) { reason = "end-of-day"; return false; }
            if (isPlayerAiOrFeud == 2) { reason = "feud-instance"; return false; }
            if (isPlayerAiOrFeud != 0 && !aiVsAi) { reason = "player-instance"; return false; }
            reason = "allow";
            return true;
        }
    }

    public enum FormationWriteKind
    {
        None,
        // Rotate each non-moving regiment in place (Regiment.RotateRegiment,
        // 130159): no target clearing, no slot re-lay.
        RotateInPlace,
        // SetGroupFormation(current formation, rotation, newpath:false,
        // modifylastwaypoint:true): skips ClearTargets/ChangeRegimentFormation
        // (91884) and wheels the group to rotated slots.
        WheelGroup,
        // BattleUnits.SetFormation (90971): courier-delivered formation change,
        // the way AdjustGroupFormations does it (5927).
        VanillaFormation
    }

    public readonly struct FormationWriteInput
    {
        public FormationWriteInput(
            bool needsFormation,
            bool needsFacing,
            bool needsRefuse,
            float combatStatusSubordinates,
            float groupSubordinatesMoving,
            int regimentPaths,
            float coverValueSubordinates,
            float nowHours,
            float lastWriteHours)
        {
            NeedsFormation = needsFormation;
            NeedsFacing = needsFacing;
            NeedsRefuse = needsRefuse;
            CombatStatusSubordinates = combatStatusSubordinates;
            GroupSubordinatesMoving = groupSubordinatesMoving;
            RegimentPaths = regimentPaths;
            CoverValueSubordinates = coverValueSubordinates;
            NowHours = nowHours;
            LastWriteHours = lastWriteHours;
        }

        public bool NeedsFormation { get; }
        public bool NeedsFacing { get; }
        public bool NeedsRefuse { get; }
        public float CombatStatusSubordinates { get; }
        public float GroupSubordinatesMoving { get; }
        public int RegimentPaths { get; }
        public float CoverValueSubordinates { get; }
        public float NowHours { get; }
        public float LastWriteHours { get; }
    }

    public readonly struct FormationWriteDecision
    {
        public FormationWriteDecision(FormationWriteKind kind, string reason)
        {
            Kind = kind;
            Reason = string.IsNullOrEmpty(reason) ? "unspecified" : reason;
        }

        public FormationWriteKind Kind { get; }
        public string Reason { get; }
    }

    // R8 (2026-10-14): how #61 may change a group's formation / facing.
    // Vanilla AdjustGroupFormations (5875-5935) only re-forms a group when its
    // stance has settled, it has no path, subordinates are not moving
    // (groupsubordinatesmoving <= 0), it is not in combat
    // (combatstatussubordinates <= 0) and not in cover
    // (covervaluesubordinates <= 0), and it goes through SetFormation (courier).
    // SetGroupFormation with newpath (91884) runs ClearTargets +
    // ChangeRegimentFormation + RegimentSetPath on every regiment — a re-lay —
    // so it is never used on a group in contact.
    public static class CommandFormationWritePolicy
    {
        // 3 game-minutes between formation/facing writes; 6 under fire (each
        // rotation stops the regiments, StopRegiment manualstop).
        public const float CooldownHours = 0.05f;
        public const float UnderFireCooldownHours = 0.1f;

        public static FormationWriteDecision Decide(in FormationWriteInput input)
        {
            if (!input.NeedsFormation && !input.NeedsFacing && !input.NeedsRefuse)
                return new FormationWriteDecision(FormationWriteKind.None, "already-correct");

            bool inCombat = input.CombatStatusSubordinates > 0f;
            if (input.LastWriteHours >= 0f && input.NowHours >= input.LastWriteHours)
            {
                float cooldown = inCombat ? UnderFireCooldownHours : CooldownHours;
                if (input.NowHours - input.LastWriteHours < cooldown)
                    return new FormationWriteDecision(FormationWriteKind.None, inCombat ? "under-fire-cooldown" : "cooldown");
            }

            if (input.RegimentPaths > 0 || input.GroupSubordinatesMoving > 0f)
                return new FormationWriteDecision(FormationWriteKind.None, "moving");

            if (inCombat)
            {
                return input.NeedsFacing
                    ? new FormationWriteDecision(FormationWriteKind.RotateInPlace, "combat-rotate-in-place")
                    : new FormationWriteDecision(FormationWriteKind.None, "combat-no-relay");
            }

            if (input.NeedsFormation && input.CoverValueSubordinates <= 0f)
                return new FormationWriteDecision(FormationWriteKind.VanillaFormation, "vanilla-set-formation");

            if (input.NeedsFacing || input.NeedsRefuse)
                return new FormationWriteDecision(FormationWriteKind.WheelGroup, "wheel");

            return new FormationWriteDecision(FormationWriteKind.None, "in-cover");
        }

        // SetFormation (90971) remaps its argument for groups (unittyp >= 14):
        // 0→3, 1→1, 2→1, 3→2, 4→4, 5→0. Returns the argument that yields the
        // given group formation, or -1.
        public static int VanillaSetFormationIndexForGroup(int groupFormation)
        {
            switch (groupFormation)
            {
                case 0: return 5;
                case 1: return 1;
                case 2: return 3;
                case 3: return 0;
                case 4: return 4;
                default: return -1;
            }
        }
    }

    public static class CommandWaypointWritePolicy
    {
        private const float MovementFlagThreshold = 0.05f;

        public static bool ShouldUseOrderDelayForExecutorWaypoint(CommandTaskType task, bool battleActive)
        {
            if (!battleActive) return true;
            if (IsExecutorMovementTask(task)) return false;
            return true;
        }

        public static bool ShouldUseBlockedMovingOrderForExecutorWaypoint(
            CommandTaskType task,
            float distance,
            float threshold,
            bool hasBlockedCrossingData,
            bool battleActive)
        {
            if (!battleActive) return false;
            if (!hasBlockedCrossingData) return false;
            if (!IsFinite(distance) || !IsFinite(threshold)) return false;
            if (threshold <= 0f || distance <= threshold) return false;

            switch (task)
            {
                case CommandTaskType.AttackObjective:
                case CommandTaskType.SupportAttack:
                case CommandTaskType.FixEnemy:
                case CommandTaskType.AdvanceToAssembly:
                    return true;
                default:
                    return false;
            }
        }

        public static bool ShouldStampGroupMovementForExecutorWaypoint(CommandTaskType task)
        {
            return IsExecutorMovementTask(task);
        }

        public static bool IsExecutorMovementActive(
            bool pathInterrupted,
            int regimentPaths,
            int movementMode,
            float groupSubordinatesMoving,
            float groupSubordinatesMovingNonAi,
            bool hasLastWaypoint,
            float distanceToLastWaypoint,
            float minWaypointDistance)
        {
            if (pathInterrupted) return false;
            if (regimentPaths <= 0) return false;
            if (movementMode > 0) return true;
            if (groupSubordinatesMoving > MovementFlagThreshold ||
                groupSubordinatesMovingNonAi > MovementFlagThreshold)
                return true;
            if (!hasLastWaypoint) return false;
            if (!IsFinite(distanceToLastWaypoint) || !IsFinite(minWaypointDistance)) return false;
            return distanceToLastWaypoint > minWaypointDistance;
        }

        public static bool ShouldSkipDuplicateWaypoint(
            float currentWaypointX,
            float currentWaypointZ,
            float targetX,
            float targetZ,
            float tolerance,
            bool pathInterrupted)
        {
            return ShouldSkipDuplicateWaypoint(
                currentWaypointX,
                currentWaypointZ,
                targetX,
                targetZ,
                tolerance,
                pathInterrupted,
                regimentPaths: 1,
                activeMove: true,
                currentX: targetX,
                currentZ: targetZ);
        }

        public static bool ShouldSkipDuplicateWaypoint(
            float currentWaypointX,
            float currentWaypointZ,
            float targetX,
            float targetZ,
            float tolerance,
            bool pathInterrupted,
            int regimentPaths,
            bool activeMove,
            float currentX,
            float currentZ)
        {
            // Microshift fix (2026-05-21): the old "if (pathInterrupted) return false" bypass
            // unconditionally allowed re-writes whenever vanilla flagged the path as
            // interrupted (which happens frequently during active combat: terrain, friendly
            // unit-in-way, momentary path-recompute). Each re-write triggered vanilla's
            // pathfinder to recompute, causing the visible "brief move and revert" microshift
            // for units that were logically pursuing the same target across many cycles.
            //
            // New behavior: even when pathInterrupted, still apply distance-based dedup —
            // if the new target is within InterruptedReissueDistance of the current waypoint,
            // skip the re-write and let vanilla resolve the interrupt on its own path. Only
            // a TRUE target relocation (genuinely different objective) bypasses dedup.
            if (!IsFinite(currentWaypointX) || !IsFinite(currentWaypointZ)) return false;
            if (!IsFinite(targetX) || !IsFinite(targetZ)) return false;
            if (currentWaypointX == 0f && currentWaypointZ == 0f) return false;

            float safeTolerance = IsFinite(tolerance) && tolerance > 0f ? tolerance : 1f;
            float dx = currentWaypointX - targetX;
            float dz = currentWaypointZ - targetZ;
            float distanceSq = (dx * dx) + (dz * dz);

            // When path is interrupted, use a larger threshold so genuinely-different
            // targets still bypass dedup but probe-drift (~20-50m off the existing target)
            // does not. InterruptedReissueDistance is intentionally larger than tolerance
            // because path interrupt is signal that vanilla wants a fresh decision; we
            // honor that for real relocations.
            float effectiveTolerance = pathInterrupted
                ? (safeTolerance > InterruptedReissueDistance ? safeTolerance : InterruptedReissueDistance)
                : safeTolerance;
            if (distanceSq > effectiveTolerance * effectiveTolerance) return false;

            if (!activeMove && regimentPaths <= 0 && IsFinite(currentX) && IsFinite(currentZ))
            {
                float currentDx = currentX - targetX;
                float currentDz = currentZ - targetZ;
                if ((currentDx * currentDx) + (currentDz * currentDz) > effectiveTolerance * effectiveTolerance)
                    return false;
            }

            return true;
        }

        // Microshift fix tunable. When pathInterrupted is set, the new-target must be at
        // least this far from the current waypoint before dedup releases. Calibrated to
        // ~3x the standard MinWaypointDistance so probe targets that drift by tens of
        // meters as a unit moves still resolve to "same logical target" rather than
        // triggering a new path recompute every cycle.
        private const float InterruptedReissueDistance = 60f;

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsExecutorMovementTask(CommandTaskType task)
        {
            switch (task)
            {
                case CommandTaskType.FormUp:
                case CommandTaskType.AdvanceToAssembly:
                case CommandTaskType.AttackObjective:
                case CommandTaskType.SupportAttack:
                case CommandTaskType.FixEnemy:
                case CommandTaskType.Scout:
                case CommandTaskType.Probe:
                case CommandTaskType.Screen:
                case CommandTaskType.FallBackToLine:
                case CommandTaskType.ReleaseReserve:
                case CommandTaskType.RecoverStuckOrder:
                    return true;
                default:
                    return false;
            }
        }
    }

    public static class CommandPostureExecutor
    {
        public static bool IdleCanSatisfyTask(CommandTaskType task)
        {
            return task == CommandTaskType.ReserveWait ||
                task == CommandTaskType.HoldObjective ||
                task == CommandTaskType.HoldChoke ||
                task == CommandTaskType.Delay ||
                task == CommandTaskType.Consolidate;
        }

        public static PostureExecutionDecision Decide(
            CommandDoctrineOrder order,
            CommandPhysicalState physical,
            float nowSeconds)
        {
            return Decide(
                order,
                physical,
                new WriteEligibilitySnapshot(
                    modeAllowsWrites: true,
                    playerProtected: physical.PlayerProtected,
                    routed: physical.Routed,
                    orderPending: false,
                    recentOrder: false),
                nowSeconds);
        }

        public static PostureExecutionDecision Decide(
            CommandDoctrineOrder order,
            CommandPhysicalState physical,
            WriteEligibilitySnapshot eligibility,
            float nowSeconds)
        {
            if (!eligibility.ModeAllowsWrites)
            {
                return NoWrite("mode-monitor-only");
            }

            if (eligibility.PlayerProtected || physical.PlayerProtected)
            {
                return NoWrite("player-protected");
            }

            if (eligibility.Routed || physical.Routed)
            {
                return NoWrite("routed");
            }

            if (eligibility.OrderPending)
            {
                return NoWrite("order-pending");
            }

            if (ShouldBlockForRecentOrder(order.Task, physical, eligibility.RecentOrder))
            {
                return NoWrite("recent-order");
            }

            if (!order.HasPurpose || order.Task == CommandTaskType.None)
            {
                return NoWrite("missing-ledger-assignment");
            }

            if (eligibility.AlreadyDoingCorrectTask)
            {
                return NoWrite("already-correct");
            }

            if (order.AllowsIdle && order.Task == CommandTaskType.ReserveWait)
            {
                if (order.PrimaryTarget.HasValue && !eligibility.AtAssignedLocation)
                {
                    return new PostureExecutionDecision(
                        PostureExecutionAction.SetWaypoint,
                        "reserve-area",
                        PostureExecutionTarget.DoctrinePrimaryTarget);
                }

                return NoWrite("legal-idle");
            }

            if (eligibility.CloseEngaged)
            {
                if (TacticalDecisionDoctrine.ShouldBreakOffRecon(
                        order.Task,
                        closeEngaged: true,
                        hasFallbackTarget: order.FallbackTarget.HasValue))
                {
                    return new PostureExecutionDecision(
                        PostureExecutionAction.SetFormationAndWaypoint,
                        "close-engaged-screen-breakoff",
                        PostureExecutionTarget.DoctrineFallbackTarget);
                }

                if (order.Task == CommandTaskType.FallBackToLine)
                {
                    return new PostureExecutionDecision(
                        PostureExecutionAction.SetFormationAndWaypoint,
                        "close-engaged-fallback-line",
                        PostureExecutionTarget.DoctrineFallbackTarget);
                }

                return Formation("close-engaged-" + TaskReason(order.Task));
            }

            if (order.Task == CommandTaskType.FallBackToLine && physical.PathInterrupted && !physical.ActiveMove)
            {
                return new PostureExecutionDecision(
                    PostureExecutionAction.FallbackToLine,
                    "fallback-line",
                    PostureExecutionTarget.DoctrineFallbackTarget,
                    clearInterruptedPaths: true);
            }

            if (physical.PathInterrupted && !physical.ActiveMove)
            {
                return new PostureExecutionDecision(
                    PostureExecutionAction.RecoverInterruptedOrder,
                    "interrupted-inactive",
                    PostureExecutionTarget.RecoveryPath,
                    clearInterruptedPaths: true);
            }

            if (physical.ActiveMove)
            {
                return NoWrite("movement-in-progress");
            }

            switch (order.Task)
            {
                case CommandTaskType.FormUp:
                case CommandTaskType.AdvanceToAssembly:
                    return FormationAndWaypoint(TaskReason(order.Task), PostureExecutionTarget.DoctrinePrimaryTarget);
                case CommandTaskType.Scout:
                case CommandTaskType.Probe:
                case CommandTaskType.AttackObjective:
                case CommandTaskType.SupportAttack:
                case CommandTaskType.FixEnemy:
                case CommandTaskType.Screen:
                case CommandTaskType.GuardFlank:
                    return FormationAndWaypoint(TaskReason(order.Task), PostureExecutionTarget.DoctrinePrimaryTarget);
                case CommandTaskType.FallBackToLine:
                    return new PostureExecutionDecision(
                        PostureExecutionAction.FallbackToLine,
                        "fallback-line",
                        PostureExecutionTarget.DoctrineFallbackTarget,
                        clearInterruptedPaths: true);
                default:
                    return Decide(
                        new CommandNodeOperationalState(
                            order.NodeId,
                            CommandEchelonKind.DivisionLike,
                            order.Role,
                            order.Task,
                            CommandTaskState.Committed),
                        physical,
                        eligibility);
            }
        }

        public static PostureExecutionDecision Decide(
            CommandNodeOperationalState state,
            CommandPhysicalState physical,
            WriteEligibilitySnapshot eligibility)
        {
            if (!eligibility.ModeAllowsWrites)
            {
                return NoWrite("mode-monitor-only");
            }

            if (eligibility.PlayerProtected || physical.PlayerProtected)
            {
                return NoWrite("player-protected");
            }

            if (eligibility.Routed || physical.Routed)
            {
                return NoWrite("routed");
            }

            if (eligibility.OrderPending)
            {
                return NoWrite("order-pending");
            }

            if (ShouldBlockForRecentOrder(state.Task, physical, eligibility.RecentOrder))
            {
                return NoWrite("recent-order");
            }

            if (eligibility.MissingLedgerAssignment || state.Task == CommandTaskType.None)
            {
                return NoWrite("missing-ledger-assignment");
            }

            if (eligibility.AlreadyDoingCorrectTask)
            {
                return NoWrite("already-correct");
            }

            if (physical.ActiveMove)
            {
                return NoWrite("movement-in-progress");
            }

            if (eligibility.CloseEngaged)
            {
                if (state.Task == CommandTaskType.FallBackToLine)
                {
                    return new PostureExecutionDecision(
                        PostureExecutionAction.SetFormationAndWaypoint,
                        "close-engaged-fallback-line",
                        PostureExecutionTarget.FallbackLine);
                }

                return Formation("close-engaged-" + TaskReason(state.Task));
            }

            if (physical.PathInterrupted && physical.Paths <= 0 && !physical.ActiveMove)
            {
                return new PostureExecutionDecision(
                    PostureExecutionAction.RecoverInterruptedOrder,
                    "illegal-idle-path-interrupted",
                    PostureExecutionTarget.RecoveryPath,
                    clearInterruptedPaths: true);
            }

            switch (state.Task)
            {
                case CommandTaskType.Scout:
                    return FormationAndWaypoint("scout", PostureExecutionTarget.ObjectiveApproach);
                case CommandTaskType.FormUp:
                    return FormationAndWaypoint("form-up", PostureExecutionTarget.AssemblyArea);
                case CommandTaskType.AdvanceToAssembly:
                    return FormationAndWaypoint("advance-to-assembly", PostureExecutionTarget.AssemblyArea);
                case CommandTaskType.AttackObjective:
                    return FormationAndWaypoint("attack-objective", PostureExecutionTarget.ObjectiveApproach);
                case CommandTaskType.HoldObjective:
                    return Formation("hold-objective");
                case CommandTaskType.FixEnemy:
                    return FormationAndWaypoint("fix-enemy", PostureExecutionTarget.ObjectiveApproach);
                case CommandTaskType.Screen:
                    return FormationAndWaypoint("screen", PostureExecutionTarget.ObjectiveApproach);
                case CommandTaskType.Probe:
                    return FormationAndWaypoint("probe", PostureExecutionTarget.ObjectiveApproach);
                case CommandTaskType.SupportAttack:
                    return FormationAndWaypoint("support-attack", PostureExecutionTarget.ObjectiveApproach);
                case CommandTaskType.HoldChoke:
                    return Formation("hold-choke");
                case CommandTaskType.GuardFlank:
                    return Formation("guard-flank");
                case CommandTaskType.Delay:
                    return Formation("delay");
                case CommandTaskType.Consolidate:
                    return Formation("consolidate");
                case CommandTaskType.FallBackToLine:
                    return new PostureExecutionDecision(
                        PostureExecutionAction.FallbackToLine,
                        "fallback-line",
                        PostureExecutionTarget.FallbackLine);
                case CommandTaskType.ReleaseReserve:
                    return new PostureExecutionDecision(
                        PostureExecutionAction.ReleaseReserve,
                        "release-reserve",
                        PostureExecutionTarget.ReleasePoint);
                case CommandTaskType.RecoverStuckOrder:
                    return new PostureExecutionDecision(
                        PostureExecutionAction.RecoverInterruptedOrder,
                        "recover-stuck-order",
                        PostureExecutionTarget.RecoveryPath,
                        clearInterruptedPaths: true);
                case CommandTaskType.ReserveWait:
                    return eligibility.AtAssignedLocation
                        ? Formation("reserve-hold")
                        : new PostureExecutionDecision(
                            PostureExecutionAction.SetWaypoint,
                            "reserve-area",
                            PostureExecutionTarget.ReserveArea);
                default:
                    return NoWrite("already-valid");
            }
        }

        private static PostureExecutionDecision NoWrite(string reason)
        {
            return new PostureExecutionDecision(PostureExecutionAction.NoWrite, reason);
        }

        public static bool ShouldBlockForRecentOrder(
            CommandTaskType task,
            CommandPhysicalState physical,
            bool recentOrder)
        {
            if (!recentOrder) return false;
            if (!IsMovementTask(task)) return true;
            return physical.ActiveMove || physical.Paths > 0;
        }

        private static bool IsMovementTask(CommandTaskType task)
        {
            switch (task)
            {
                case CommandTaskType.FormUp:
                case CommandTaskType.AdvanceToAssembly:
                case CommandTaskType.AttackObjective:
                case CommandTaskType.SupportAttack:
                case CommandTaskType.FixEnemy:
                case CommandTaskType.Scout:
                case CommandTaskType.Probe:
                case CommandTaskType.Screen:
                case CommandTaskType.FallBackToLine:
                case CommandTaskType.ReleaseReserve:
                case CommandTaskType.RecoverStuckOrder:
                    return true;
                default:
                    return false;
            }
        }

        private static PostureExecutionDecision Formation(string reason)
        {
            return new PostureExecutionDecision(
                PostureExecutionAction.SetFormation,
                reason,
                PostureExecutionTarget.CurrentPosition);
        }

        private static PostureExecutionDecision FormationAndWaypoint(
            string reason,
            PostureExecutionTarget target)
        {
            return new PostureExecutionDecision(
                PostureExecutionAction.SetFormationAndWaypoint,
                reason,
                target);
        }

        private static string TaskReason(CommandTaskType task)
        {
            switch (task)
            {
                case CommandTaskType.Scout:
                    return "scout";
                case CommandTaskType.Probe:
                    return "probe";
                case CommandTaskType.Screen:
                    return "screen";
                case CommandTaskType.FormUp:
                    return "form-up";
                case CommandTaskType.AdvanceToAssembly:
                    return "advance-to-assembly";
                case CommandTaskType.AttackObjective:
                    return "attack-objective";
                case CommandTaskType.FixEnemy:
                    return "fix-enemy";
                case CommandTaskType.SupportAttack:
                    return "support-attack";
                case CommandTaskType.HoldObjective:
                    return "hold-objective";
                case CommandTaskType.HoldChoke:
                    return "hold-choke";
                case CommandTaskType.GuardFlank:
                    return "guard-flank";
                case CommandTaskType.ReserveWait:
                    return "reserve-wait";
                case CommandTaskType.ReleaseReserve:
                    return "release-reserve";
                case CommandTaskType.FallBackToLine:
                    return "fallback-line";
                case CommandTaskType.Delay:
                    return "delay";
                case CommandTaskType.Consolidate:
                    return "consolidate";
                case CommandTaskType.RecoverStuckOrder:
                    return "recover-stuck-order";
                default:
                    return "unknown";
            }
        }
    }
}
