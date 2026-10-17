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

        // The battle-core arbiter tick (#68). The master Enabled switch (it can be
        // turned off at runtime) and Tactical Commander Mode come first: with either
        // off the side stands down and writes nothing, not even the retreat
        // release (which lowers lastaistancechangetime). Then the battle gate
        // above; an army retreat (macroai 3) releases Whiskey-held stances.
        public static ArbiterTickAction ArbiterAction(bool pluginEnabled, bool modeAllowsWrites,
            int state, int macroAi, int eodCycle, int isPlayerAiOrFeud, bool aiVsAi)
        {
            if (!pluginEnabled || !modeAllowsWrites) return ArbiterTickAction.StandDown;
            if (Allows(state, macroAi, eodCycle, isPlayerAiOrFeud, aiVsAi, out string reason)) return ArbiterTickAction.Run;
            return reason == "vanilla-retreat" ? ArbiterTickAction.StandDownAndReleaseRetreat : ArbiterTickAction.StandDown;
        }
    }

    public enum ArbiterTickAction
    {
        Run,
        StandDown,
        StandDownAndReleaseRetreat,
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
        VanillaFormation,
        // BC11, appended. A refused flank of a group in combat: the wing regiments
        // whose SkewedPosition setback is at least FlankFacing.RefuseWingShare of the
        // refuse depth rotate in place (Regiment.RotateRegiment) to the skewed-line
        // rotation (78155-78167), only where vanilla's micro turn (4560-4608) would
        // not turn them back. No re-lay, no position change.
        RefuseInPlace
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
                // BC11: a refused flank in combat rotates the wing in place (RotateInPlace would
                // drop the refuse); a facing-only write keeps the shipped rotation.
                if (input.NeedsRefuse) return new FormationWriteDecision(FormationWriteKind.RefuseInPlace, "combat-refuse-in-place");
                return input.NeedsFacing
                    ? new FormationWriteDecision(FormationWriteKind.RotateInPlace, "combat-rotate-in-place")
                    : new FormationWriteDecision(FormationWriteKind.None, "combat-no-relay");
            }

            if (input.NeedsFormation && input.CoverValueSubordinates <= 0f)
                return new FormationWriteDecision(FormationWriteKind.VanillaFormation, "vanilla-set-formation");

            // BC11 (bug fix): vanilla re-forms a group only when it is out of combat, and combatstatussubordinates
            // is that gate (AdjustGroupFormations 5886). A refused flank is that wheel whatever the enemies in
            // reach: a visible enemy within 250 m is always "in range", which kept refuseflank at -1. A
            // facing-only wheel keeps the shipped enemies-in-range refusal.
            if (input.NeedsRefuse)
                return new FormationWriteDecision(FormationWriteKind.WheelGroup, "refuse-wheel");

            if (input.NeedsFacing)
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
            if (!Battle.CommandWaypointWritePolicy.IsMovementTask(task)) return new LeafMoveDecision(false, "non-movement-task");
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
    //   flanksthreated / outflanked → #44's perceived flank-risk rule
    //     (BattleWriterPolicy.PerceivedFlankRisk: the group's own outflanked
    //     state over the snapshot's flank pressure, or a positive mean vanilla
    //     outflanked weight of its regiments, ArmyEvidenceBuilder.MeanFlankThreatByGroup);
    //   orderqueue / orderstate → UnitRecord.QueuedOrders (own queue + move
    //     orders in flight to the unit or a descendant, 124998/124878);
    //   groupsubordinatesmoving → moving descendants (regimentpaths > 0);
    //   pathinterrupted → UnitFlags.PathInterrupted (111012; contact stopped
    //     the move, 117330), so Decide's interrupted/recovery branches fire;
    //   covervalue / coverobject → UnitRecord.CoverValue / CoverObject with
    //     vanilla's no-cover rule (covervalue <= 0 or coverobject 3,
    //     CheckLineFallbacks 5167);
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

        public static bool Routed(in UnitRecord unit)
        {
            return (unit.Flags & (UnitFlags.Routed | UnitFlags.MarkedForRout)) != 0;
        }

        public static bool PathInterrupted(in UnitRecord unit)
        {
            return (unit.Flags & UnitFlags.PathInterrupted) != 0;
        }

        /// <summary>In cover by vanilla's rule: not (covervalue &lt;= 0 or coverobject 3) (5167).</summary>
        public static bool InCover(in UnitRecord unit)
        {
            return unit.CoverValue > 0f && unit.CoverObject != 3;
        }

        // #61 HasActiveMoveMakingProgress over the snapshot (the movement-flag
        // input groupsubordinatesmoving is the moving-descendant count here). An
        // interrupted path is no active move (IsExecutorMovementActive).
        public static bool ActiveMove(in UnitRecord unit, int movingUnits)
        {
            if (PathInterrupted(unit)) return false;
            if (movingUnits <= 0) return false;
            if (unit.MovementMode > 0) return true;
            // #61 read groupsubordinatesmoving > 0.05: a moving subordinate counts.
            if (movingUnits > (unit.RegimentPaths > 0 ? 1 : 0)) return true;
            if (!HasPoint(unit.LastWaypointX, unit.LastWaypointZ)) return false;
            return Distance(unit.X, unit.Z, unit.LastWaypointX, unit.LastWaypointZ) > MinWaypointMeters;
        }

        // #61 HasPendingOrder: the settlement gate over the queued orders and the
        // interrupted path the snapshot carries (orderstate is not carried).
        public static bool PendingOrder(in UnitRecord unit, bool activeMove, bool allowStaleQueuedBypass)
        {
            return TacticalOrderSettlementGate.HasBlockingPendingOrder(new TacticalOrderSettlementGate.Input
            {
                OrderQueueCount = unit.QueuedOrders,
                OrderState = 0,
                RegimentPaths = unit.RegimentPaths,
                PathInterrupted = PathInterrupted(unit),
                MovementMode = unit.MovementMode,
                ActiveMove = activeMove,
                AllowStaleQueuedBypass = allowStaleQueuedBypass,
            });
        }

        public const string RefusePromotedReason = "refuse-flank";

        /// <summary>
        /// BC11 decision 2: which posture NoWrite verdicts a due refused flank may override. A group that is already
        /// doing its task (formation right, idle where idle is the task) has nothing else to write; every other NoWrite
        /// (moving, order pending, routed, protected) keeps its verdict.
        /// </summary>
        public static bool RefusePromotesNoWrite(string reason)
        {
            return string.Equals(reason, "already-correct", StringComparison.Ordinal) ||
                string.Equals(reason, "already-valid", StringComparison.Ordinal);
        }

        /// <summary>
        /// #61 ResolveRefusedFlank, BC11 decision 2: a group with flank risk on a holding/guarding task refuses
        /// the flank that is under threat; vanilla skews the slots (SetGroupFormation refuseflank, 92095-92097).
        /// It needs close engagement (melee, or a visible enemy within CloseContactMeters) or perceived flank
        /// pressure on one side (BattleSnapshot.FlankPressure left / right above 0). The side is the flank with more
        /// pressure (FlankFacing.PressuredSide); on a tie, or with no pressure, the closest visible enemy decides as
        /// before (CommandFormationCorrection.RefuseDecisionForThreatFacing, 45-150 degrees off the facing). On a
        /// pressured side the geometry test runs on that side's pressure bearing (FlankFacing.SideBearing), and a
        /// closest-enemy fallback must agree with the side.
        /// </summary>
        public static RefuseFlank RefuseFor(BattleSnapshot s, int index, CommandTaskType task, bool closeEngaged, bool flankRisk)
        {
            if (s == null || index < 0 || index >= s.OwnCount || !flankRisk) return RefuseFlank.None;
            RefuseFlank pressured = FlankFacing.PressuredSide(s, index);
            bool pressure = s.FlankPressure(index, 0) > 0f || s.FlankPressure(index, 1) > 0f;
            if (!closeEngaged && !pressure) return RefuseFlank.None;
            if (CommandFormationCorrection.RefuseFlankParameter(TacticalRefuseFlankIntent.Decision.RefuseLeft, task, true) < 0)
                return RefuseFlank.None;
            ref UnitRecord rec = ref s.Own[index];

            float threatRotation;
            if (pressured != RefuseFlank.None && FlankFacing.SideBearing(s, index, rec.RotationY, pressured, out float sideBearing))
            {
                threatRotation = sideBearing;
            }
            else
            {
                int k = s.ClosestVisibleEnemy(index, out _);
                if (k < 0) return RefuseFlank.None;
                threatRotation = CommandFormationCorrection.ThreatFacingRotationDegrees(rec.X, rec.Z, s.Observations[k].X, s.Observations[k].Z);
            }
            TacticalRefuseFlankIntent.Decision decision = CommandFormationCorrection.RefuseDecisionForThreatFacing(rec.RotationY, threatRotation);
            RefuseFlank side;
            switch (CommandFormationCorrection.RefuseFlankParameter(decision, task, true))
            {
                case 0: side = RefuseFlank.Left; break;
                case 1: side = RefuseFlank.Right; break;
                default: return RefuseFlank.None;
            }
            return pressured != RefuseFlank.None && pressured != side ? RefuseFlank.None : side;
        }

        /// <summary>
        /// The floor half of #61's safe-waypoint band (60 m): a target closer than
        /// that is where the unit already stands. The executor dedupes a repeat of
        /// the last waypoint but has no floor, and a move onto the unit's own
        /// position adds no path (Failed, then a rotated retry of a ~0 m vector).
        /// </summary>
        public static bool IsMoveAway(in UnitRecord unit, float x, float z)
        {
            return HasPoint(x, z) && Distance(unit.X, unit.Z, x, z) >= MinWaypointMeters;
        }

        /// <summary>
        /// The regiments #61 fire control orders (TryApplyFireControlToUnit):
        /// infantry, skirmishers and cavalry. Never a battery — BC5b's artillery
        /// doctrine owns battery combat behavior (7/8) on the same channel and
        /// priority.
        /// </summary>
        public static bool IsFireControlUnit(in UnitRecord unit)
        {
            if ((unit.Flags & (UnitFlags.IsArtillery | UnitFlags.IsGroup)) != 0) return false;
            return unit.UnitTyp == TacticalUnitType.Infantry ||
                unit.UnitTyp == TacticalUnitType.Skirmisher ||
                unit.UnitTyp == TacticalUnitType.Cavalry;
        }

        public static CommandPhysicalState Physical(in UnitRecord unit, int movingUnits)
        {
            return new CommandPhysicalState(
                routed: Routed(unit),
                playerProtected: (unit.Flags & UnitFlags.PlayerOwned) != 0,
                pathInterrupted: PathInterrupted(unit),
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

        /// <summary>
        /// Every descendant of <paramref name="root"/> (breadth-first over the
        /// snapshot's command tree), the set #61 fire control read from
        /// allattachedunits. Writes own indices to result; returns the count.
        /// Grow-only buffers, bounded by the roster.
        /// </summary>
        public static int CollectDescendants(BattleSnapshot s, int root, ref int[] result, ref int[] children)
        {
            int n = s != null ? s.OwnCount : 0;
            if (result == null || result.Length < n) result = new int[Math.Max(n, 64)];
            if (children == null || children.Length < n) children = new int[Math.Max(n, 64)];
            if (root < 0 || root >= n) return 0;
            int count = 0;
            int node = root;
            for (int head = 0; ; head++)
            {
                int c = s.DirectChildren(node, children);
                for (int k = 0; k < c && count < n; k++) result[count++] = children[k];
                if (head >= count) break;
                node = result[head];
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
        // A FallBackToLine under close engagement or flank risk: immediate, line-checked.
        public bool InContactWithdrawal;
        // An objective-type move over 150 m: through vanilla BlockedMovingOrder
        // (PostureIntentPlan.AvoidsCoveredCrossings).
        public bool AvoidCoveredCrossings;
        // BC13: the request is an exploit order's. Its Move is not delayed (no courier cadence), as vanilla's own
        // isolated-target move (CheckForSeparatedEnemy 8188).
        public bool Exploit;
        // BC13: the exploit's id (ExploitEntry.Id) when Exploit, else 0; carried on the Move, Stance and Formation intents.
        public int ExploitId;
        // #61 refused flank, carried on the formation and facing intents.
        public RefuseFlank Refuse;
        // #61 threat facing (holding tasks with a visible threat): the wheel's
        // manualfinalrotation on the formation intent.
        public bool HasThreatHeading;
        public float ThreatHeading;
        public IntentPriority Priority;
        public IntentSource Source;
        public string Reason; // static literal
        // BC10 road march: the planned via points ride on the Move (the executor sends the group
        // through them); ColumnWait holds the Move back while group formation 4 (march column,
        // TargetFormation) is ordered: the formation intent goes out alone.
        public int ViaCount;
        /// <summary>The vias are a BC10 road route (flag <see cref="IntentFlags.RoadRoute"/> on the move).</summary>
        public bool RoadRoute;
        public int RoadLinkCount, RoadLink0, RoadLink1;
        public float ViaX0, ViaZ0, ViaX1, ViaZ1;
        public bool ColumnWait;
    }

    // #61 ApplyDecision as intents (plan BC5a). Stance goes with every move
    // (#61 changed stance before SetWaypoint, L768-774; the arbiter now issues the
    // move first, OrderArbiter: a stance order queued ahead of a move delays it); a formation (and the
    // threat facing, SubmitFacing) go with formation decisions and with SetFormationAndWaypoint,
    // where the executor's formation policy refuses them while the group moves —
    // vanilla re-forms only a settled group (AdjustGroupFormations 5886), and #61
    // re-formed only when no move went out (L976-988).
    /// <summary>
    /// BC9: the posture pipeline's and the leaf cascade's final stance guard
    /// (TacticalDecisionDoctrine.TaskForStance), after every rewrite of the task:
    /// a Defender's Probe becomes Screen and its attack tasks HoldObjective.
    /// </summary>
    public static class PostureStanceGuard
    {
        public const string ScreenReason = "defender-screen";
        public const string HoldReason = "defender-hold";

        /// <summary>The leaf cascade's guarded task.</summary>
        public static CommandTaskType Task(CommandTaskType task, BattleStance stance) =>
            TacticalDecisionDoctrine.TaskForStance(task, stance);

        /// <summary>Rewrites the state's task (and the order's with it); true when it changed.</summary>
        public static bool Apply(BattleStance stance, ref CommandNodeOperationalState state, bool hasOrder, ref CommandDoctrineOrder order)
        {
            CommandTaskType guarded = TacticalDecisionDoctrine.TaskForStance(state.Task, stance);
            if (guarded == state.Task && (!hasOrder || TacticalDecisionDoctrine.TaskForStance(order.Task, stance) == order.Task))
                return false;
            state = new CommandNodeOperationalState(state.NodeId, state.Echelon, state.Role, guarded, state.TaskState,
                state.X, state.Z, state.FacingDegrees);
            if (hasOrder) order = order.WithTask(guarded, guarded == CommandTaskType.Screen ? ScreenReason : HoldReason);
            return true;
        }
    }

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
                    return SubmitFormation(sink, r);
                case PostureExecutionAction.SetFormationAndWaypoint:
                    if (!r.HasTarget) return 0;
                    if (r.ColumnWait) return SubmitStance(sink, r) + SubmitFormation(sink, r);
                    return SubmitStance(sink, r) + SubmitMove(sink, r) + SubmitFormation(sink, r);
                case PostureExecutionAction.SetWaypoint:
                case PostureExecutionAction.ReleaseReserve:
                case PostureExecutionAction.FallbackToLine:
                case PostureExecutionAction.RecoverInterruptedOrder:
                    if (!r.HasTarget) return 0;
                    if (r.ColumnWait) return SubmitStance(sink, r) + SubmitFormation(sink, r);
                    return SubmitStance(sink, r) + SubmitMove(sink, r);
                default:
                    return 0;
            }
        }

        /// <summary>
        /// A group's threat facing. On a clean subtree (no player-owned or
        /// detached descendant) one group Facing intent, which the executor turns
        /// into the shipped rotate-in-place or wheel (CommandFormationWritePolicy);
        /// otherwise one Facing intent per regiment the group's order may reach
        /// (PostureSense.CollectRotatableRegiments). Returns the intents submitted.
        /// </summary>
        public static int SubmitFacing(IIntentSink sink, BattleSnapshot s, int groupIndex, UnitFlags descendantFlags, float heading,
            CommandTaskType task, RefuseFlank refuse, IntentPriority priority, IntentSource source, string reason,
            int[] queue, int[] regiments)
        {
            heading = PostureSense.NormalizeHeading(heading);
            if (sink == null || s == null || heading < 0f || groupIndex < 0 || groupIndex >= s.OwnCount) return 0;
            if (PostureSense.SubtreeAcceptsGroupFacing(descendantFlags))
            {
                sink.Submit(GroupFacing(s.Own[groupIndex].InstanceId, heading, task, refuse, priority, source, reason));
                return 1;
            }

            int count = PostureSense.CollectRotatableRegiments(s, groupIndex, queue, regiments);
            for (int k = 0; k < count; k++)
                sink.Submit(GroupFacing(s.Own[regiments[k]].InstanceId, heading, task, RefuseFlank.None, priority, source, reason));
            return count;
        }

        private static BattleOrderIntent GroupFacing(int id, float heading, CommandTaskType task, RefuseFlank refuse,
            IntentPriority priority, IntentSource source, string reason)
        {
            return new BattleOrderIntent
            {
                GroupId = id, Channel = OrderChannel.Facing, Heading = heading, Task = task, Refuse = refuse,
                Priority = priority, Source = source, Reason = reason,
            };
        }

        /// <summary>
        /// Shipped #61 TryQueueBlockedMovingOrder over the snapshot: an
        /// Attack/Support/Fix/AdvanceToAssembly group move farther than 150 m
        /// avoids the crossings the enemy covers (vanilla line-group moves,
        /// 6953-6956).
        /// </summary>
        public static bool AvoidsCoveredCrossings(CommandTaskType task, float fromX, float fromZ, float toX, float toZ)
        {
            return Battle.CommandWaypointWritePolicy.ShouldUseBlockedMovingOrderForExecutorWaypoint(task, PostureSense.Distance(fromX, fromZ, toX, toZ));
        }

        /// <summary>
        /// BC14 requirement-crossing-via: the crossing a maneuvering wing's order is routed through. Only a March or an Attack on the doctrine
        /// order's primary target carries it (a hold, a turn and a main-body member's order have none, and an exploit order is its own), and the
        /// via is the order's own, which the maneuver dropped once the child stood past it.
        /// </summary>
        public static bool TryManeuverVia(in CommandDoctrineOrder order, CommandTaskType task, bool exploit, PostureExecutionTarget target,
            out float viaX, out float viaZ)
        {
            viaX = 0f;
            viaZ = 0f;
            if (exploit || !order.HasVia || target != PostureExecutionTarget.DoctrinePrimaryTarget) return false;
            if (task != CommandTaskType.AdvanceToAssembly && task != CommandTaskType.AttackObjective) return false;
            viaX = order.ViaX;
            viaZ = order.ViaZ;
            return true;
        }

        /// <summary>
        /// Which of the two route aids a move may still use. A move through a crossing via is routed by that via alone: a BC10 road route plans
        /// its own via points, and a move around covered crossings is vanilla's BlockedMovingOrder, which has no via points at all (the executor
        /// would drop the crossing). Both outputs are true when nothing suppresses them; the caller keeps its own conditions
        /// (PostureRoutes.Applies, AvoidsCoveredCrossings).
        /// </summary>
        public static void RouteFlagsFor(CommandTaskType task, bool hasManeuverVia, out bool useRoadRoute, out bool avoidCoveredCrossings)
        {
            bool routedByVia = hasManeuverVia && (task == CommandTaskType.AdvanceToAssembly || task == CommandTaskType.AttackObjective);
            useRoadRoute = !routedByVia;
            avoidCoveredCrossings = !routedByVia;
        }

        /// <summary>
        /// A nested leaf brigade's move to its slot, facing <paramref name="heading"/>
        /// (-1 none): no stance (it is not an AI group), and never through
        /// BlockedMovingOrder (#61's leaf path called SetWaypoint directly).
        /// </summary>
        public static BattleOrderIntent LeafMove(int groupId, CommandTaskType task, float x, float z, float heading, bool inContactWithdrawal)
        {
            return MoveIntent(groupId, task, x, z, heading, inContactWithdrawal, avoidCoveredCrossings: false, exploit: false,
                IntentPriority.Executor, IntentSource.PostureExecutor, LeafReason);
        }

        public static BattleOrderIntent FacingPulse(int unitId, float heading)
        {
            return new BattleOrderIntent
            {
                GroupId = unitId, Channel = OrderChannel.Facing, Heading = PostureSense.NormalizeHeading(heading),
                Priority = IntentPriority.Scorer, Source = IntentSource.FacingPulse, Reason = FacingPulseReason,
            };
        }

        /// <summary>
        /// The facing pulse's brigade intents for one decision: the formation correction,
        /// then (#61's SetFormation threat facing, for the tasks that face a threat or,
        /// BC9, a Screen/Probe watching an entry point) the group facing on a clean
        /// subtree. Returns the intents submitted.
        /// </summary>
        public static int SubmitBrigadePulse(IIntentSink sink, int groupId, in TacticalFacingPulseInput input,
            in TacticalFacingPulseDecision d, bool subtreeAcceptsGroupFacing)
        {
            int n = 0;
            if (d.ShouldWrite && d.TargetFormation >= 0 && d.TargetFormation != input.CurrentFormation)
            {
                sink.Submit(FacingPulseFormation(groupId, d.TargetFormation));
                n++;
            }
            if (d.ShouldWrite && CommandFormationCorrection.ShouldFaceThreat(input.Task, input.ThreatSource) && subtreeAcceptsGroupFacing &&
                CommandFormationCorrection.NeedsFacingCorrection(input.CurrentFacingDegrees, d.TargetFacingDegrees, PostureSense.FacingToleranceDegrees))
            {
                sink.Submit(FacingPulse(groupId, d.TargetFacingDegrees));
                n++;
            }
            return n;
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
                Priority = r.Priority, Source = r.Source, Reason = r.Reason, ExploitId = r.ExploitId,
            });
            return 1;
        }

        private static int SubmitMove(IIntentSink sink, in PostureIntentRequest r)
        {
            BattleOrderIntent move = MoveIntent(r.GroupId, r.Task, r.TargetX, r.TargetZ, -1f, r.InContactWithdrawal, r.AvoidCoveredCrossings, r.Exploit,
                r.Priority, r.Source, r.Reason);
            move.ExploitId = r.ExploitId;
            if (r.ViaCount > 0)
            {
                move.ViaCount = Math.Min(r.ViaCount, 2);
                if (r.RoadRoute)
                {
                    move.Flags |= IntentFlags.RoadRoute;
                    move.RoadLinkCount = r.RoadLinkCount;
                    move.RoadLink0 = r.RoadLink0;
                    move.RoadLink1 = r.RoadLink1;
                }
                move.ViaX0 = r.ViaX0;
                move.ViaZ0 = r.ViaZ0;
                move.ViaX1 = r.ViaX1;
                move.ViaZ1 = r.ViaZ1;
            }
            sink.Submit(move);
            return 1;
        }

        private static int SubmitFormation(IIntentSink sink, in PostureIntentRequest r)
        {
            int n = 0;
            // #61 SetFormation wrote for a formation change or a refused flank
            // alone (CommandFormationWritePolicy needsRefuse → wheel).
            if ((r.NeedsFormation || r.Refuse != RefuseFlank.None) && r.TargetFormation >= 0 && r.TargetFormation <= 4)
            {
                sink.Submit(new BattleOrderIntent
                {
                    GroupId = r.GroupId, Channel = OrderChannel.Formation, Value = r.TargetFormation,
                    Heading = r.HasThreatHeading ? PostureSense.NormalizeHeading(r.ThreatHeading) : -1f, Task = r.Task,
                    Refuse = r.Refuse, Priority = r.Priority, Source = r.Source, Reason = r.Reason, ExploitId = r.ExploitId,
                });
                n++;
            }
            return n;
        }

        // B-61clock: planned moves go by courier like vanilla AI group moves; an
        // in-contact withdrawal is immediate (CheckIfOutflanked 6235). A move
        // through BlockedMovingOrder is immediate too once the executor takes
        // that path (UpdateOrders' SetWaypoint has useorderdelay:false); the
        // courier flag stays for its plain-SetWaypoint fallback.
        private static BattleOrderIntent MoveIntent(int groupId, CommandTaskType task, float x, float z, float heading,
            bool inContactWithdrawal, bool avoidCoveredCrossings, bool exploit, IntentPriority priority, IntentSource source, string reason)
        {
            bool withdrawal = inContactWithdrawal && task == CommandTaskType.FallBackToLine;
            IntentFlags flags = IntentFlags.None;
            if (Battle.CommandWaypointWritePolicy.ShouldUseOrderDelayForExecutorWaypoint(task, battleActive: true, inContactWithdrawal: withdrawal, exploit: exploit))
                flags |= IntentFlags.UseOrderDelay;
            if (withdrawal) flags |= IntentFlags.InContactWithdrawal;
            if (avoidCoveredCrossings) flags |= IntentFlags.AvoidCoveredCrossings;
            return new BattleOrderIntent
            {
                GroupId = groupId, Channel = OrderChannel.Move, TargetX = x, TargetZ = z,
                Heading = heading >= 0f ? PostureSense.NormalizeHeading(heading) : -1f, Task = task,
                Flags = flags, Priority = priority, Source = source, Reason = reason,
            };
        }
    }

    // B-leaf target (plan BC5a): shipped #61 TryFormationSlot over the snapshot.
    // SetGroupFormation stores the group's waypoint and rotation (92067-92068), stands
    // its commander groupdepth + groupunitdistancetoback behind that waypoint (92050),
    // and paths each child group k to
    // GetAngleTerrainPosXY(waypoint, rotation, -offset[k].x, offset[k].z + child
    // groupdepth) (92086: num + groupdepth2 / 2 = the full groupdepth2), facing the
    // rotation. Shipped read those live (LeafFormationSlot.OffsetXZ = the formula of
    // GetAngleTerrainPosXYStatic 26937-26949, without its terrain clamp; the executor's
    // validator clamps to the map, 131057-131068); the snapshot now carries them
    // (UnitRecord.LastWaypointRotation, GroupDepth, SlotOffsetX/Z with
    // UnitFlags.HasLayoutSlot). The parent's commander position never enters it.
    // A leaf within 60 m of its slot has no move. A refused flank
    // (refuseflank, 92095-92101) moves a child straight back by SkewedPosition's
    // depth (78109-78130) and turns it to the skewed line. Vanilla keeps no refuse
    // state and its AI never refuses (the player UI does: RefuseFlank 91742 ->
    // SetGroupFormation 91790; SetWaypoint forwards direction as refuseflank,
    // 91559), so the refused slot counts only for the flank the order executor
    // last refused on the parent (OrderExecutor.LastIssuedRefuse); a leaf within
    // 60 m of it holds and keeps its facing. Terrain-shape interpolation
    // (adjustbyterrainshape, 92089-92093) is not applied, as shipped. The parent's
    // waypoint is current only while it was the top unit of its last
    // SetGroupFormation (92065-92068): a division moved with its corps keeps a
    // stale one.
    public static class LeafSlotTarget
    {
        public static bool TryResolve(IBattleUnitView s, int leafIndex, float refuseFlankDepth, float refuseFlankSkew,
            RefuseFlank parentRefuse, out int parentIndex, out float x, out float z, out float heading)
        {
            parentIndex = -1;
            x = 0f;
            z = 0f;
            heading = -1f;
            if (s == null || leafIndex < 0 || leafIndex >= s.OwnCount) return false;
            UnitRecord[] own = s.Own;
            ref UnitRecord leaf = ref own[leafIndex];
            int parent = leaf.ParentIndex;
            if (parent < 0 || parent >= s.OwnCount || parent == leafIndex) return false;
            parentIndex = parent;
            if ((leaf.Flags & UnitFlags.HasLayoutSlot) == 0) return false;
            ref UnitRecord p = ref own[parent];
            // No waypoint yet also covers the -999 rotation sentinel (112527): the
            // rotation is stored together with the waypoint.
            if (!PostureSense.HasPoint(p.LastWaypointX, p.LastWaypointZ)) return false;
            float rotation = p.LastWaypointRotation;
            if (float.IsNaN(rotation) || float.IsInfinity(rotation)) return false;

            OffsetXZ(p.LastWaypointX, p.LastWaypointZ, rotation, -leaf.SlotOffsetX, leaf.SlotOffsetZ + leaf.GroupDepth, out x, out z);
            heading = PostureSense.NormalizeHeading(rotation);
            // A leaf already at its slot has no move to make.
            if (!PostureSense.IsMoveAway(leaf, x, z)) return false;
            float back = RefusedSetback(leaf.SlotOffsetX, p.GroupWidth, refuseFlankDepth, refuseFlankSkew, parentRefuse);
            if (back > 0f)
            {
                OffsetXZ(x, z, rotation, 0f, back, out float rx, out float rz);
                if (!PostureSense.IsMoveAway(leaf, rx, rz)) return false;
            }
            return true;
        }

        /// <summary>
        /// SkewedPosition's setback of a refused child (78109-78130): with
        /// t = clamp01((offsetX + width / 2) / width), pow(t, skew) for the right
        /// flank (refuseflank 1), pow(1 - t, skew) for the left (0), times
        /// width * depthFactor (GamePrefs.refuseflankdepth). The executor refuses
        /// only Left or Right (vanilla's both-flanks 2 is a player-UI value).
        /// </summary>
        public static float RefusedSetback(float offsetX, float groupWidth, float depthFactor, float skew, RefuseFlank refuse)
        {
            if (!(groupWidth > 0f) || !(depthFactor > 0f) || float.IsInfinity(groupWidth)) return 0f;
            float t = (offsetX + groupWidth / 2f) / groupWidth;
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            float u;
            switch (refuse)
            {
                case RefuseFlank.Left: u = 1f - t; break;
                case RefuseFlank.Right: u = t; break;
                default: return 0f;
            }
            return (float)Math.Pow(u, skew) * groupWidth * depthFactor;
        }

        // GetAngleTerrainPosXYStatic (26937-26949) without the terrain clamp: distZ is
        // rearward, distX to the left of a unit facing angle (0 = +z, 90 = +x).
        private static void OffsetXZ(float anchorX, float anchorZ, float angle, float distX, float distZ, out float x, out float z)
        {
            double a = (angle - 90.0) * Math.PI / 180.0;
            double b = angle * Math.PI / 180.0;
            x = (float)(anchorX - Math.Cos(a) * distZ - Math.Cos(b) * distX);
            z = (float)(anchorZ + Math.Sin(a) * distZ + Math.Sin(b) * distX);
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
    // puts AddPath's battle costs (roads allowed, path not interrupted) on its own
    // NavMeshQueryFilter for NavMesh.CalculatePath instead of running on whatever
    // the last AddPath left, and instead of writing the global costs itself (BC10).
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
