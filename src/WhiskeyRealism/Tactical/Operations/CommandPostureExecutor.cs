using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;

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

    // Battle-level gate of the battle-core arbiter tick (BattleCoreArbiterPatch,
    // Postfix on AIBattle.AdjustGroupFormations; plan D3).
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
        // modifylastwaypoint:true): skips ClearTargets (91960 runs it only for
        // !modifylastwaypoint || newpath) but re-paths every child to its
        // rotated slot — so only with no enemies in range.
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
            float lastWriteHours,
            float groupEnemiesInRange)
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
            GroupEnemiesInRange = groupEnemiesInRange;
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
        // Regiment.groupenemiesinrange.
        public float GroupEnemiesInRange { get; }
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
            {
                return input.GroupEnemiesInRange > 0f
                    ? new FormationWriteDecision(FormationWriteKind.None, "enemies-in-range")
                    : new FormationWriteDecision(FormationWriteKind.WheelGroup, "wheel");
            }

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

    // BR-M1: the Main-effort arrival hold (TacticalMainArrivalSynchronizer)
    // arms once per role/task assignment. When it ends — supports closed, the
    // budget ran out, or an aggressive commander released it — it stays
    // released until the caller drops the state (role/task changed). Without
    // the latch, an expired hold re-armed on the next tick and the brigade
    // flipped Screen (1) ↔ Attack (3) with a waypoint re-issue each time.
    public static class MainArrivalHoldLatch
    {
        // Hold budget in battle seconds (15 battle-minutes), measured on the
        // doctrine clock. Same value as T1's
        // TacticalMainArrivalSynchronizer.DefaultMaxHoldSeconds.
        public const float MaxHoldBattleSeconds = 15f * 60f;

        public readonly struct State
        {
            public State(bool active, bool released, float startedAtHours)
            {
                Active = active;
                Released = released;
                StartedAtHours = startedAtHours;
            }

            public bool Active { get; }
            public bool Released { get; }
            public float StartedAtHours { get; }
        }

        public static bool ShouldEvaluate(State state)
        {
            return !state.Released;
        }

        public static float ElapsedHours(State state, float nowHours)
        {
            if (!state.Active || nowHours < state.StartedAtHours) return 0f;
            return nowHours - state.StartedAtHours;
        }

        public static float ElapsedBattleSeconds(State state, float nowHours)
        {
            return TacticalBattleClock.HoursToBattleSeconds(ElapsedHours(state, nowHours));
        }

        public static State Next(State previous, float nowHours, bool holding)
        {
            if (previous.Released) return previous;
            if (!holding) return new State(false, true, previous.StartedAtHours);
            if (previous.Active && nowHours >= previous.StartedAtHours) return previous;
            return new State(true, false, nowHours);
        }
    }

    public readonly struct LeafMoveDecision
    {
        public LeafMoveDecision(bool move, string reason)
        {
            Move = move;
            Reason = string.IsNullOrEmpty(reason) ? "unspecified" : reason;
        }

        public bool Move { get; }
        public string Reason { get; }
    }

    // B-leaf (2026-10-14): when #61 may move a brigade nested inside an AI
    // group. Such a brigade is an attached sub-unit: vanilla moves it as part
    // of its parent's SetGroupFormation (slots built from
    // groupposition.unitoffsetposition, 92060-92090). So the leaf pass only
    // moves it for movement tasks, only to its slot in the parent's formation,
    // never while the parent itself has an order in flight, and with the same
    // gates as the group path (no move in contact, none over a pending order
    // or an active move).
    public static class LeafBrigadeMovePolicy
    {
        public static LeafMoveDecision Decide(
            CommandTaskType task,
            bool parentHasActiveOrder,
            bool hasFormationSlot,
            bool closeEngaged,
            bool pendingOrder,
            bool activeMove)
        {
            if (!CommandWaypointWritePolicy.IsMovementTask(task)) return new LeafMoveDecision(false, "non-movement-task");
            if (parentHasActiveOrder) return new LeafMoveDecision(false, "parent-ordered");
            if (!hasFormationSlot) return new LeafMoveDecision(false, "no-slot");
            if (closeEngaged) return new LeafMoveDecision(false, "close-engaged");
            if (pendingOrder) return new LeafMoveDecision(false, "order-pending");
            if (activeMove) return new LeafMoveDecision(false, "movement-in-progress");
            return new LeafMoveDecision(true, "move-to-slot");
        }
    }

    // Roster identity for per-battle caches (#61 perf): a cache built from the
    // unit roster stays valid until a unit is added/removed, a unit routs, or
    // a new battle starts — not merely until the next frame.
    public readonly struct ProbeRosterSignature : IEquatable<ProbeRosterSignature>
    {
        public ProbeRosterSignature(int allianceId, int unitCount, int routedCount, int battleKey)
        {
            AllianceId = allianceId;
            UnitCount = unitCount;
            RoutedCount = routedCount;
            BattleKey = battleKey;
        }

        public int AllianceId { get; }
        public int UnitCount { get; }
        public int RoutedCount { get; }
        public int BattleKey { get; }

        public bool Equals(ProbeRosterSignature other)
        {
            return AllianceId == other.AllianceId &&
                UnitCount == other.UnitCount &&
                RoutedCount == other.RoutedCount &&
                BattleKey == other.BattleKey;
        }

        public override bool Equals(object obj)
        {
            return obj is ProbeRosterSignature other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((AllianceId * 397 ^ UnitCount) * 397 ^ RoutedCount) * 397 ^ BattleKey;
            }
        }
    }

    // Moved to Tactical/Battle/OrderExecutor.cs (BC4). This forwarder stays
    // while BattleGroupStancePatch (#45, deleted by BC5b) and the T4 tests
    // still name it in this namespace; BC7 deletes it.
    public static class TacticalBattleClock
    {
        public const float RealSecondsPerBattleHourAtOneX = Battle.TacticalBattleClock.RealSecondsPerBattleHourAtOneX;
        public const float BattleSecondsPerHour = Battle.TacticalBattleClock.BattleSecondsPerHour;

        public static float HoursToBattleSeconds(float hours) => Battle.TacticalBattleClock.HoursToBattleSeconds(hours);

        public static float RealSecondsAtOneXToHours(float seconds) => Battle.TacticalBattleClock.RealSecondsAtOneXToHours(seconds);
    }

    // Moved to Tactical/Battle/OrderExecutor.cs (BC4). This forwarder stays
    // while TacticalObserverPatch (outside BC5a's region) and
    // TacticalCommandTests still name it in this namespace; BC7 deletes it.
    public static class CommandWaypointWritePolicy
    {
        public const float FailedWriteBackoffHours = Battle.CommandWaypointWritePolicy.FailedWriteBackoffHours;

        public static bool ShouldUseOrderDelayForExecutorWaypoint(CommandTaskType task, bool battleActive, bool inContactWithdrawal = false)
            => Battle.CommandWaypointWritePolicy.ShouldUseOrderDelayForExecutorWaypoint(task, battleActive, inContactWithdrawal);

        public static bool MovementIssued(int movingPathsBefore, int movingPathsAfter, int queuedBefore, int queuedAfter)
            => Battle.CommandWaypointWritePolicy.MovementIssued(movingPathsBefore, movingPathsAfter, queuedBefore, queuedAfter);

        public static float FailedWriteBackoffUntil(float nowHours) => Battle.CommandWaypointWritePolicy.FailedWriteBackoffUntil(nowHours);

        public static bool IsBackedOff(float nowHours, float backoffUntilHours) => Battle.CommandWaypointWritePolicy.IsBackedOff(nowHours, backoffUntilHours);

        public static bool ShouldUseBlockedMovingOrderForExecutorWaypoint(CommandTaskType task, float distance, float threshold, bool hasBlockedCrossingData, bool battleActive)
            => Battle.CommandWaypointWritePolicy.ShouldUseBlockedMovingOrderForExecutorWaypoint(task, distance, threshold, hasBlockedCrossingData, battleActive);

        public static bool ShouldStampGroupMovementForExecutorWaypoint(CommandTaskType task)
            => Battle.CommandWaypointWritePolicy.ShouldStampGroupMovementForExecutorWaypoint(task);

        public static bool IsMovementTask(CommandTaskType task) => Battle.CommandWaypointWritePolicy.IsMovementTask(task);

        public static bool IsExecutorMovementActive(bool pathInterrupted, int regimentPaths, int movementMode, float groupSubordinatesMoving,
            float groupSubordinatesMovingNonAi, bool hasLastWaypoint, float distanceToLastWaypoint, float minWaypointDistance)
            => Battle.CommandWaypointWritePolicy.IsExecutorMovementActive(pathInterrupted, regimentPaths, movementMode, groupSubordinatesMoving,
                groupSubordinatesMovingNonAi, hasLastWaypoint, distanceToLastWaypoint, minWaypointDistance);

        public static bool ShouldSkipDuplicateWaypoint(float currentWaypointX, float currentWaypointZ, float targetX, float targetZ, float tolerance, bool pathInterrupted)
            => Battle.CommandWaypointWritePolicy.ShouldSkipDuplicateWaypoint(currentWaypointX, currentWaypointZ, targetX, targetZ, tolerance, pathInterrupted);

        public static bool ShouldSkipDuplicateWaypoint(float currentWaypointX, float currentWaypointZ, float targetX, float targetZ, float tolerance,
            bool pathInterrupted, int regimentPaths, bool activeMove, float currentX, float currentZ)
            => Battle.CommandWaypointWritePolicy.ShouldSkipDuplicateWaypoint(currentWaypointX, currentWaypointZ, targetX, targetZ, tolerance,
                pathInterrupted, regimentPaths, activeMove, currentX, currentZ);
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

    // ---- BC5a: the #61 posture executor as battle-core intent producers ----
    //
    // The producers (Tactical/Battle/Producers) read the BattleSnapshot only.
    // #61 read live Regiment fields the snapshot does not carry; these are the
    // snapshot proxies that replace them (plan BC5a, recorded in the commit):
    //   combatstatussubordinates / groupenemiesinrange (close engagement) →
    //     InMelee on the group or a descendant, or the closest visible enemy
    //     within CloseContactMeters (vanilla aidefensivemaxrange 250, the AI's
    //     own pressure radius, 122719);
    //   flanksthreated / outflanked → any positive snapshot flank pressure;
    //   orderqueue / orderstate → UnitRecord.QueuedOrders (own queue + move
    //     orders in flight to the unit or a descendant, 124998/124878);
    //   groupsubordinatesmoving → moving descendants (regimentpaths > 0);
    //   pathinterrupted → not carried: the PathInterrupted branches of Decide
    //     do not fire from the producers;
    //   receivedfire → close engagement;
    //   ClosestVisibleEnemy (TacticalFogOfWarContact) → the snapshot's
    //     fog-honest closest visible observation.
    public static class PostureSense
    {
        public const float CloseContactMeters = 250f;
        // = #61 MinWaypointDistance / OrderArbiter.DedupeMeters.
        public const float MinWaypointMeters = OrderArbiter.DedupeMeters;
        // = #61 MaxConservativeWaypointDistance.
        public const float MaxWaypointMeters = 2500f;
        // = #61 FacingRefreshToleranceDegrees.
        public const float FacingToleranceDegrees = OrderExecutor.FacingToleranceDegrees;

        /// <summary>
        /// Per own unit: the OR of its descendants' flags (self excluded) and the
        /// number of units in its subtree (self included) with regimentpaths &gt; 0.
        /// Grow-only buffers; O(units x depth).
        /// </summary>
        public static void AggregateDescendants(BattleSnapshot s, ref UnitFlags[] descendantFlags, ref int[] movingUnits)
        {
            int n = s != null ? s.OwnCount : 0;
            if (descendantFlags == null || descendantFlags.Length < n) descendantFlags = new UnitFlags[Math.Max(n, 64)];
            if (movingUnits == null || movingUnits.Length < n) movingUnits = new int[Math.Max(n, 64)];
            if (n == 0) return;
            UnitRecord[] own = s.Own;
            for (int i = 0; i < n; i++)
            {
                descendantFlags[i] = UnitFlags.None;
                movingUnits[i] = own[i].RegimentPaths > 0 ? 1 : 0;
            }
            for (int i = 0; i < n; i++)
            {
                bool moving = own[i].RegimentPaths > 0;
                int p = own[i].ParentIndex;
                for (int steps = 0; p >= 0 && p < n && steps < n; steps++)
                {
                    descendantFlags[p] |= own[i].Flags;
                    if (moving) movingUnits[p]++;
                    p = own[p].ParentIndex;
                }
            }
        }

        public static bool CloseEngaged(BattleSnapshot s, int index, UnitFlags descendantFlags)
        {
            if (s == null || index < 0 || index >= s.OwnCount) return false;
            if (((s.Own[index].Flags | descendantFlags) & UnitFlags.InMelee) != 0) return true;
            return s.ClosestVisibleEnemy(index, out float distance) >= 0 && distance <= CloseContactMeters;
        }

        public static bool FlankRisk(BattleSnapshot s, int index)
        {
            if (s == null) return false;
            return s.FlankPressure(index, 0) > 0f || s.FlankPressure(index, 1) > 0f || s.FlankPressure(index, 2) > 0f;
        }

        public static bool Routed(in UnitRecord unit)
        {
            return (unit.Flags & (UnitFlags.Routed | UnitFlags.MarkedForRout)) != 0;
        }

        // #61 HasActiveMoveMakingProgress over the snapshot (the movement-flag
        // input groupsubordinatesmoving is the moving-descendant count here).
        public static bool ActiveMove(in UnitRecord unit, int movingUnits)
        {
            if (movingUnits <= 0) return false;
            if (unit.MovementMode > 0) return true;
            if (!HasPoint(unit.LastWaypointX, unit.LastWaypointZ)) return false;
            return Distance(unit.X, unit.Z, unit.LastWaypointX, unit.LastWaypointZ) > MinWaypointMeters;
        }

        // #61 HasPendingOrder: the settlement gate over the queued orders the
        // snapshot carries (orderstate is not carried).
        public static bool PendingOrder(in UnitRecord unit, bool activeMove, bool allowStaleQueuedBypass)
        {
            return TacticalOrderSettlementGate.HasBlockingPendingOrder(new TacticalOrderSettlementGate.Input
            {
                OrderQueueCount = unit.QueuedOrders,
                OrderState = 0,
                RegimentPaths = unit.RegimentPaths,
                PathInterrupted = false,
                MovementMode = unit.MovementMode,
                ActiveMove = activeMove,
                AllowStaleQueuedBypass = allowStaleQueuedBypass,
            });
        }

        public static CommandPhysicalState Physical(in UnitRecord unit, int movingUnits)
        {
            return new CommandPhysicalState(
                routed: Routed(unit),
                playerProtected: (unit.Flags & UnitFlags.PlayerOwned) != 0,
                pathInterrupted: false,
                paths: unit.RegimentPaths,
                activeMove: ActiveMove(unit, movingUnits),
                formation: unit.Formation);
        }

        /// <summary>Vanilla facing degrees (0 = +z, 90 = +x) from one point to another, in [0, 360).</summary>
        public static float HeadingTo(float fromX, float fromZ, float toX, float toZ)
        {
            return NormalizeHeading(CommandFormationCorrection.ThreatFacingRotationDegrees(fromX, fromZ, toX, toZ));
        }

        public static float NormalizeHeading(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees)) return -1f;
            float d = degrees % 360f;
            return d < 0f ? d + 360f : d;
        }

        /// <summary>
        /// Fog-honest replacement for vanilla groupenemiesinrange (the max of the
        /// subordinates' unitrange.enemiesinrange, 120558-120563: enemy strength
        /// within the line-of-sight range, 122650-122655, which vanilla does not
        /// fog-filter for the AI side): the strength of the enemies this side
        /// perceives within rangeMeters of the unit or a descendant. Visible
        /// counts in full, remembered by confidence; off-map reinforcements never.
        /// </summary>
        public static float EnemyStrengthInRange(IBattleUnitView s, int index, float rangeMeters)
        {
            if (s == null || index < 0 || index >= s.OwnCount || rangeMeters <= 0f) return 0f;
            UnitRecord[] own = s.Own;
            // The unit and its descendants, once (a regiment is just itself).
            if (_subtree.Length < s.OwnCount) _subtree = new int[Math.Max(s.OwnCount, _subtree.Length * 2)];
            int members = 0;
            for (int j = 0; j < s.OwnCount; j++)
                if (j == index || s.IsAncestorOf(index, j)) _subtree[members++] = j;

            EnemyObservation[] obs = s.Observations;
            float r2 = rangeMeters * rangeMeters;
            float total = 0f;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (obs[k].IsReinforcement) continue;
                for (int m = 0; m < members; m++)
                {
                    float dx = obs[k].X - own[_subtree[m]].X;
                    float dz = obs[k].Z - own[_subtree[m]].Z;
                    if (dx * dx + dz * dz > r2) continue;
                    total += obs[k].State == ObservationState.Visible ? obs[k].Strength : obs[k].Strength * obs[k].Confidence01;
                    break;
                }
            }
            return total;
        }

        // Main-thread scratch for EnemyStrengthInRange (grow-only).
        private static int[] _subtree = new int[64];

        /// <summary>
        /// A group Facing order may go to the group itself only when no descendant
        /// is player-owned or detached: vanilla's group cascades skip detached
        /// subordinates (GetAttachedUnitsReg(excludedechainedunits) 119865,
        /// SetGroupFormation 91988-92030), and the executor's in-place rotation
        /// walks every descendant. Otherwise the regiments are faced one by one.
        /// </summary>
        public static bool SubtreeAcceptsGroupFacing(UnitFlags descendantFlags)
        {
            return (descendantFlags & (UnitFlags.PlayerOwned | UnitFlags.PermanentlyDetached)) == 0;
        }

        /// <summary>
        /// The regiments a group's in-place rotation may turn (#61
        /// RotateRegimentsInPlace): combat, non-artillery regiments that are
        /// standing (no path, not charging), alive and active, reached from the
        /// group without passing a detached or player-owned node — vanilla's group
        /// orders skip a detached subtree (119865). Writes own indices to result;
        /// queue is the walk's scratch. Returns the count.
        /// </summary>
        public static int CollectRotatableRegiments(BattleSnapshot s, int groupIndex, int[] queue, int[] result)
        {
            if (s == null || queue == null || result == null || queue.Length == 0) return 0;
            if (groupIndex < 0 || groupIndex >= s.OwnCount) return 0;
            UnitRecord[] own = s.Own;
            int n = s.OwnCount;
            int count = 0, head = 0, tail = 0;
            queue[tail++] = groupIndex;
            while (head < tail)
            {
                int node = queue[head++];
                for (int j = 0; j < n; j++)
                {
                    if (j == node || own[j].ParentIndex != node) continue;
                    UnitFlags f = own[j].Flags;
                    if ((f & (UnitFlags.PlayerOwned | UnitFlags.PermanentlyDetached)) != 0) continue;
                    if ((f & UnitFlags.IsGroup) != 0)
                    {
                        if (tail < queue.Length) queue[tail++] = j;
                        continue;
                    }
                    if (!Rotatable(own[j])) continue;
                    if (count < result.Length) result[count++] = j;
                }
            }
            return count;
        }

        private static bool Rotatable(in UnitRecord unit)
        {
            if (unit.UnitTyp > TacticalUnitType.MaxCombat || unit.UnitTyp == TacticalUnitType.Artillery) return false;
            if ((unit.Flags & UnitFlags.IsArtillery) != 0) return false;
            if ((unit.Flags & (UnitFlags.Alive | UnitFlags.Active)) != (UnitFlags.Alive | UnitFlags.Active)) return false;
            if (Routed(unit)) return false;
            return unit.RegimentPaths <= 0 && unit.MovementMode != 3;
        }

        internal static bool HasPoint(float x, float z)
        {
            return !(x == 0f && z == 0f) && !float.IsNaN(x) && !float.IsNaN(z) && !float.IsInfinity(x) && !float.IsInfinity(z);
        }

        internal static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }

    // One posture decision of a group, ready to become intents.
    public struct PostureIntentRequest
    {
        public int GroupId;
        public PostureExecutionAction Action;
        public CommandTaskType Task;
        public bool HasTarget;
        public float TargetX, TargetZ;
        public int TargetFormation;
        public bool NeedsFormation;
        public bool HasFacing;
        public float Heading;
        // A FallBackToLine under close engagement or flank risk: immediate, line-checked.
        public bool InContactWithdrawal;
        public IntentPriority Priority;
        public IntentSource Source;
        public string Reason; // static literal
    }

    // #61 ApplyDecision as intents (plan BC5a). Stance goes with every move
    // (#61 changed stance before SetWaypoint, L768-774); a formation and the
    // threat facing go with formation decisions and with SetFormationAndWaypoint,
    // where the executor's formation policy refuses them while the group moves —
    // vanilla re-forms only a settled group (AdjustGroupFormations 5886), and #61
    // re-formed only when no move went out (L976-988).
    public static class PostureIntentPlan
    {
        public const string ExecutorReason = "posture-executor";
        public const string LeafReason = "leaf-brigade";
        public const string DivisionPlayReason = "division-play";
        public const string FacingPulseReason = "facing-pulse";
        public const string FireControlReason = "fire-control";

        public static int Submit(IIntentSink sink, in PostureIntentRequest r)
        {
            if (sink == null || r.GroupId == 0) return 0;
            switch (r.Action)
            {
                case PostureExecutionAction.ChangeStance:
                    return SubmitStance(sink, r);
                case PostureExecutionAction.SetFormation:
                    return SubmitFormationAndFacing(sink, r);
                case PostureExecutionAction.SetFormationAndWaypoint:
                    if (!r.HasTarget) return 0;
                    return SubmitStance(sink, r) + SubmitMove(sink, r) + SubmitFormationAndFacing(sink, r);
                case PostureExecutionAction.SetWaypoint:
                case PostureExecutionAction.ReleaseReserve:
                case PostureExecutionAction.FallbackToLine:
                case PostureExecutionAction.RecoverInterruptedOrder:
                    if (!r.HasTarget) return 0;
                    return SubmitStance(sink, r) + SubmitMove(sink, r);
                default:
                    return 0;
            }
        }

        /// <summary>A nested leaf brigade's move to its slot: no stance (it is not an AI group).</summary>
        public static BattleOrderIntent LeafMove(int groupId, CommandTaskType task, float x, float z, bool inContactWithdrawal)
        {
            return MoveIntent(groupId, task, x, z, inContactWithdrawal, IntentPriority.Executor, IntentSource.PostureExecutor, LeafReason);
        }

        public static BattleOrderIntent FacingPulse(int unitId, float heading)
        {
            return new BattleOrderIntent
            {
                GroupId = unitId, Channel = OrderChannel.Facing, Heading = PostureSense.NormalizeHeading(heading),
                Priority = IntentPriority.Scorer, Source = IntentSource.FacingPulse, Reason = FacingPulseReason,
            };
        }

        public static BattleOrderIntent FacingPulseFormation(int groupId, int formation)
        {
            return new BattleOrderIntent
            {
                GroupId = groupId, Channel = OrderChannel.Formation, Value = formation, Heading = -1f,
                Priority = IntentPriority.Scorer, Source = IntentSource.FacingPulse, Reason = FacingPulseReason,
            };
        }

        public static BattleOrderIntent FireControl(int regimentId, int combatBehavior)
        {
            return new BattleOrderIntent
            {
                GroupId = regimentId, Channel = OrderChannel.CombatBehavior, Value = combatBehavior, Heading = -1f,
                Flags = IntentFlags.PerRegiment, Priority = IntentPriority.Executor, Source = IntentSource.FireControl,
                Reason = FireControlReason,
            };
        }

        /// <summary>
        /// Doctrine cooldowns (#61 facing pulse, fire control, formation writes)
        /// read the executor's record of what it actually issued for that source,
        /// instead of a producer-side stamp set at submission.
        /// </summary>
        public static bool IssuedWithin(OrderExecutor executor, int unitId, OrderChannel channel, IntentSource source,
            double nowHours, double windowHours)
        {
            if (executor == null || !executor.TryGetLastOutcome(unitId, channel, out OrderOutcome outcome)) return false;
            if (outcome.Kind != OrderOutcomeKind.Issued || outcome.Source != source) return false;
            double age = nowHours - outcome.AtHours;
            return age >= 0.0 && age < windowHours;
        }

        private static int SubmitStance(IIntentSink sink, in PostureIntentRequest r)
        {
            int stance = TacticalCommandTaskToStanceMapper.Map(r.Task);
            if (stance == TacticalCommandTaskToStanceMapper.StanceNone) return 0;
            sink.Submit(new BattleOrderIntent
            {
                GroupId = r.GroupId, Channel = OrderChannel.Stance, Value = stance, Heading = -1f, Task = r.Task,
                Priority = r.Priority, Source = r.Source, Reason = r.Reason,
            });
            return 1;
        }

        private static int SubmitMove(IIntentSink sink, in PostureIntentRequest r)
        {
            sink.Submit(MoveIntent(r.GroupId, r.Task, r.TargetX, r.TargetZ, r.InContactWithdrawal, r.Priority, r.Source, r.Reason));
            return 1;
        }

        private static int SubmitFormationAndFacing(IIntentSink sink, in PostureIntentRequest r)
        {
            int n = 0;
            if (r.NeedsFormation && r.TargetFormation >= 0 && r.TargetFormation <= 4)
            {
                sink.Submit(new BattleOrderIntent
                {
                    GroupId = r.GroupId, Channel = OrderChannel.Formation, Value = r.TargetFormation, Heading = -1f, Task = r.Task,
                    Priority = r.Priority, Source = r.Source, Reason = r.Reason,
                });
                n++;
            }
            if (r.HasFacing)
            {
                float heading = PostureSense.NormalizeHeading(r.Heading);
                if (heading >= 0f)
                {
                    sink.Submit(new BattleOrderIntent
                    {
                        GroupId = r.GroupId, Channel = OrderChannel.Facing, Heading = heading, Task = r.Task,
                        Priority = r.Priority, Source = r.Source, Reason = r.Reason,
                    });
                    n++;
                }
            }
            return n;
        }

        // B-61clock: planned moves go by courier like vanilla AI group moves; an
        // in-contact withdrawal is immediate (CheckIfOutflanked 6235).
        private static BattleOrderIntent MoveIntent(int groupId, CommandTaskType task, float x, float z, bool inContactWithdrawal,
            IntentPriority priority, IntentSource source, string reason)
        {
            bool withdrawal = inContactWithdrawal && task == CommandTaskType.FallBackToLine;
            IntentFlags flags = IntentFlags.None;
            if (CommandWaypointWritePolicy.ShouldUseOrderDelayForExecutorWaypoint(task, battleActive: true, inContactWithdrawal: withdrawal))
                flags |= IntentFlags.UseOrderDelay;
            if (withdrawal) flags |= IntentFlags.InContactWithdrawal;
            return new BattleOrderIntent
            {
                GroupId = groupId, Channel = OrderChannel.Move, TargetX = x, TargetZ = z, Heading = -1f, Task = task,
                Flags = flags, Priority = priority, Source = source, Reason = reason,
            };
        }
    }

    // B-leaf target (plan BC5a): #61 placed a nested leaf brigade at its slot in
    // the parent's formation from groupposition.unitoffsetposition and the
    // parent's lastsetwaypointrotation (SetGroupFormation 92060-92075), with the
    // parent found through the scene graph. Neither the
    // layout nor the rotation is in the snapshot, so the slot is the leaf's
    // current offset from its command parent (the snapshot parent map, plan D5)
    // re-applied at the parent's last waypoint — the rule the executor uses for
    // fanned-out children (OrderExecutor.OffsetTarget). The executor's validator
    // clamps it to the map (131057-131068).
    public static class LeafSlotTarget
    {
        public static bool TryResolve(IBattleUnitView s, int leafIndex, out int parentIndex, out float x, out float z)
        {
            parentIndex = -1;
            x = 0f;
            z = 0f;
            if (s == null || leafIndex < 0 || leafIndex >= s.OwnCount) return false;
            UnitRecord[] own = s.Own;
            int parent = own[leafIndex].ParentIndex;
            if (parent < 0 || parent >= s.OwnCount || parent == leafIndex) return false;
            parentIndex = parent;
            if (!PostureSense.HasPoint(own[parent].LastWaypointX, own[parent].LastWaypointZ)) return false;
            x = own[parent].LastWaypointX + (own[leafIndex].X - own[parent].X);
            z = own[parent].LastWaypointZ + (own[leafIndex].Z - own[parent].Z);
            return PostureSense.HasPoint(x, z);
        }
    }

    // Spec §3: the role cascade (leaf map) leaves out player-owned units — the
    // W&L player's command and its descendants, take-overs, and the player side
    // outside W&L — by the snapshot's PlayerOwned flag. The player's AI ancestors
    // stay (they are not protected), so their AI brigades keep their roles.
    // Replaces the old dlcw_isundercommander id-set probe filter.
    public static class PostureProbeFilter
    {
        public static IReadOnlyList<TacticalCommandTreeProbe.ExtendedProbe> WithoutPlayerOwned(
            IReadOnlyList<TacticalCommandTreeProbe.ExtendedProbe> probes,
            IBattleUnitView s)
        {
            if (probes == null) return probes;
            if (s == null) return Array.Empty<TacticalCommandTreeProbe.ExtendedProbe>();
            List<TacticalCommandTreeProbe.ExtendedProbe> kept = null;
            for (int i = 0; i < probes.Count; i++)
            {
                bool owned = IsPlayerOwned(s, probes[i].InstanceId);
                if (kept == null)
                {
                    if (!owned) continue;
                    kept = new List<TacticalCommandTreeProbe.ExtendedProbe>(probes.Count);
                    for (int k = 0; k < i; k++) kept.Add(probes[k]);
                    continue;
                }
                if (!owned) kept.Add(probes[i]);
            }
            return kept ?? probes;
        }

        private static bool IsPlayerOwned(IBattleUnitView s, int instanceId)
        {
            int index = s.IndexOfOwn(instanceId);
            return index >= 0 && (s.Own[index].Flags & UnitFlags.PlayerOwned) != 0;
        }
    }

    // R5: NavMesh area costs are global and Regiment.AddPath rewrites them for
    // every path (130364-130394), so the posture producer's path-quality probe
    // sets AddPath's battle costs (roads allowed, path not interrupted) before
    // every NavMesh.CalculatePath instead of running on whatever the last
    // AddPath left.
    public static class PostureNavAreaCosts
    {
        public static readonly string[] AreaNames =
        {
            VanillaPathAreaCosts.RiversAreaName,
            VanillaPathAreaCosts.RoadsAreaName,
            VanillaPathAreaCosts.BridgesAreaName,
            VanillaPathAreaCosts.FencesWallsAreaName,
        };

        public static float Cost(int area, bool frozen)
        {
            switch (area)
            {
                case 0: return VanillaPathAreaCosts.Rivers(frozen);
                case 1: return VanillaPathAreaCosts.Roads;
                case 2: return VanillaPathAreaCosts.Bridges;
                case 3: return VanillaPathAreaCosts.FencesWalls;
                default: return 1f;
            }
        }
    }
}
