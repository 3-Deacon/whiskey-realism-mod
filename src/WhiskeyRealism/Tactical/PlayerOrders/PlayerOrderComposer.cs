using System;

namespace WhiskeyRealism.Tactical.PlayerOrders
{
    internal readonly struct CommandNodeIntentSnapshot
    {
        public CommandNodeIntentSnapshot(
            string nodeId,
            string sourceNodeId,
            string role,
            string axis,
            int primarySector,
            int supportPriority,
            float aggressionBias01,
            int depth)
        {
            NodeId = Clean(nodeId);
            SourceNodeId = Clean(sourceNodeId);
            Role = Clean(role);
            Axis = Clean(axis);
            PrimarySector = Math.Max(0, primarySector);
            SupportPriority = Math.Max(0, Math.Min(100, supportPriority));
            AggressionBias01 = Clamp01(aggressionBias01);
            Depth = Math.Max(0, depth);
        }

        public string NodeId { get; }
        public string SourceNodeId { get; }
        public string Role { get; }
        public string Axis { get; }
        public int PrimarySector { get; }
        public int SupportPriority { get; }
        public float AggressionBias01 { get; }
        public int Depth { get; }

        private static string Clean(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }

    internal readonly struct CommandIntentResolutionSnapshot
    {
        public CommandIntentResolutionSnapshot(bool found, CommandNodeIntentSnapshot intent, string reason)
        {
            Found = found;
            Intent = intent;
            Reason = string.IsNullOrWhiteSpace(reason) ? string.Empty : reason.Trim();
        }

        public bool Found { get; }
        public CommandNodeIntentSnapshot Intent { get; }
        public string Reason { get; }
    }

    internal readonly struct CommandTaskSnapshot
    {
        public CommandTaskSnapshot(
            bool found,
            string role,
            string task,
            PlayerOrderPoint targetPoint,
            PlayerOrderPoint fallbackTarget,
            string objectiveKey,
            string reason)
        {
            Found = found;
            Role = Clean(role);
            Task = Clean(task);
            TargetPoint = targetPoint;
            FallbackTarget = fallbackTarget;
            ObjectiveKey = Clean(objectiveKey);
            Reason = Clean(reason);
        }

        public bool Found { get; }
        public string Role { get; }
        public string Task { get; }
        public PlayerOrderPoint TargetPoint { get; }
        public PlayerOrderPoint FallbackTarget { get; }
        public string ObjectiveKey { get; }
        public string Reason { get; }

        private static string Clean(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    internal readonly struct ArmyDoctrineSnapshot
    {
        public ArmyDoctrineSnapshot(string operationPhase, string operationShape, string objectiveKey, string reason)
        {
            OperationPhase = Clean(operationPhase);
            OperationShape = Clean(operationShape);
            ObjectiveKey = Clean(objectiveKey);
            Reason = Clean(reason);
        }

        public string OperationPhase { get; }
        public string OperationShape { get; }
        public string ObjectiveKey { get; }
        public string Reason { get; }

        private static string Clean(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    internal readonly struct PlayerOrderComposerInput
    {
        public PlayerOrderComposerInput(
            bool hasSideOrchestrator,
            bool isPlayerSubordinate,
            bool isCommanderInChief,
            string unitKey,
            string battleIdentity,
            int givenOrderSession,
            CommandIntentResolutionSnapshot commandIntent,
            CommandTaskSnapshot commandTask,
            ArmyDoctrineSnapshot doctrine,
            PlayerOrderPoint unitPosition,
            PlayerOrderPoint objectivePoint,
            PlayerOrderPoint fallbackPoint,
            PlayerOrderPoint exitPoint,
            bool hasValidObjective,
            bool hasValidFallback,
            bool hasValidExitPoint)
        {
            HasSideOrchestrator = hasSideOrchestrator;
            IsPlayerSubordinate = isPlayerSubordinate;
            IsCommanderInChief = isCommanderInChief;
            UnitKey = Clean(unitKey);
            BattleIdentity = Clean(battleIdentity);
            GivenOrderSession = Math.Max(0, givenOrderSession);
            CommandIntent = commandIntent;
            CommandTask = commandTask;
            Doctrine = doctrine;
            UnitPosition = unitPosition;
            ObjectivePoint = objectivePoint;
            FallbackPoint = fallbackPoint;
            ExitPoint = exitPoint;
            HasValidObjective = hasValidObjective;
            HasValidFallback = hasValidFallback;
            HasValidExitPoint = hasValidExitPoint || exitPoint.ValidExitPoint;
        }

        public bool HasSideOrchestrator { get; }
        public bool IsPlayerSubordinate { get; }
        public bool IsCommanderInChief { get; }
        public string UnitKey { get; }
        public string BattleIdentity { get; }
        public int GivenOrderSession { get; }
        public CommandIntentResolutionSnapshot CommandIntent { get; }
        public CommandTaskSnapshot CommandTask { get; }
        public ArmyDoctrineSnapshot Doctrine { get; }
        public PlayerOrderPoint UnitPosition { get; }
        public PlayerOrderPoint ObjectivePoint { get; }
        public PlayerOrderPoint FallbackPoint { get; }
        public PlayerOrderPoint ExitPoint { get; }
        public bool HasValidObjective { get; }
        public bool HasValidFallback { get; }
        public bool HasValidExitPoint { get; }

        private static string Clean(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    internal static class PlayerOrderComposer
    {
        public static PlayerOrderCandidate Compose(PlayerOrderComposerInput input)
        {
            if (!input.HasSideOrchestrator ||
                !input.IsPlayerSubordinate ||
                input.IsCommanderInChief ||
                string.IsNullOrEmpty(input.UnitKey))
            {
                return default(PlayerOrderCandidate);
            }

            if (!input.CommandIntent.Found && !input.CommandTask.Found)
            {
                return default(PlayerOrderCandidate);
            }

            if (WantsRetreat(input) && input.HasValidExitPoint)
            {
                return Candidate(input, PlayerOrderIntent.RetreatToExit, input.ExitPoint, "exit", "retreat-with-valid-exit");
            }

            if (WantsFallback(input) && input.HasValidFallback)
            {
                return Candidate(input, PlayerOrderIntent.FallBackToLine, input.FallbackPoint, FallbackObjectiveKey(input), "fallback-line");
            }

            if (WantsHold(input) && input.HasValidObjective)
            {
                return Candidate(input, PlayerOrderIntent.HoldObjective, input.ObjectivePoint, ObjectiveKey(input), "hold-objective");
            }

            if (WantsHold(input) && PlayerOrderTargetPolicy.IsValid(input.UnitPosition))
            {
                return Candidate(input, PlayerOrderIntent.HoldObjective, input.UnitPosition, "hold-position", "hold-position");
            }

            if (WantsSupport(input) && input.HasValidObjective)
            {
                return Candidate(input, PlayerOrderIntent.SupportMainEffort, input.ObjectivePoint, ObjectiveKey(input), "support-main-effort");
            }

            if (WantsAttack(input) && input.HasValidObjective)
            {
                return Candidate(input, PlayerOrderIntent.AttackObjective, input.ObjectivePoint, ObjectiveKey(input), "attack-objective");
            }

            if (WantsProbe(input) && input.HasValidObjective)
            {
                return Candidate(input, PlayerOrderIntent.ProbeObjective, input.ObjectivePoint, ObjectiveKey(input), "probe-objective");
            }

            if (WantsAdvance(input) && input.HasValidObjective)
            {
                return Candidate(input, PlayerOrderIntent.AdvanceToAssemblyArea, input.ObjectivePoint, ObjectiveKey(input), "advance-assembly");
            }

            return default(PlayerOrderCandidate);
        }

        private static PlayerOrderCandidate Candidate(
            PlayerOrderComposerInput input,
            PlayerOrderIntent intent,
            PlayerOrderPoint target,
            string objectiveKey,
            string reason)
        {
            var mapped = PlayerOrderVanillaMapper.Map(intent, PlayerOrderScope.Tactical);
            return new PlayerOrderCandidate(
                scope: PlayerOrderScope.Tactical,
                intent: intent,
                vanillaType: mapped.Type,
                priority: PlayerOrderPriority.ForIntent(intent),
                unitKey: input.UnitKey,
                battleIdentity: input.BattleIdentity,
                givenOrderSession: input.GivenOrderSession,
                targetPoint: target,
                rotation: RotationFor(intent),
                objectiveKey: objectiveKey,
                reason: reason + ":" + SourceReason(input),
                activeCampaignActionable: false,
                campaignGroupFlag: false,
                validExitPoint: intent == PlayerOrderIntent.RetreatToExit && input.HasValidExitPoint);
        }

        private static bool WantsRetreat(PlayerOrderComposerInput input)
        {
            return IsAny(input.Doctrine.OperationPhase, "aborting", "softabort", "withdraw", "withdrawal", "retreat") ||
                IsAny(input.CommandIntent.Intent.Axis, "withdraw", "retreat") && IsFallbackRole(input) ||
                IsAny(input.CommandTask.Task, "retreat", "withdraw", "withdrawal", "leavefield");
        }

        private static bool WantsFallback(PlayerOrderComposerInput input)
        {
            return IsFallbackRole(input) ||
                IsAny(input.CommandIntent.Intent.Axis, "withdraw") ||
                IsAny(input.CommandTask.Task, "fallbacktoline", "fallback", "delay", "withdraw") ||
                IsAny(input.Doctrine.OperationShape, "delayandfallback");
        }

        private static bool WantsHold(PlayerOrderComposerInput input)
        {
            return IsAny(input.CommandIntent.Intent.Role, "defender", "guard", "refuseleft", "refuseright") ||
                IsAny(input.CommandTask.Role, "defender", "guard", "fallbackguard") && IsAny(input.CommandTask.Task, "holdobjective", "holdchoke", "guardflank") ||
                IsAny(input.CommandTask.Task, "holdobjective", "holdchoke", "guardflank", "defend", "hold");
        }

        private static bool WantsSupport(PlayerOrderComposerInput input)
        {
            return IsAny(input.CommandIntent.Intent.Role, "supportmain", "supportingattack", "support") ||
                IsAny(input.CommandTask.Role, "supportmain", "supportingattack", "support") ||
                IsAny(input.CommandTask.Task, "supportattack", "releasereserve");
        }

        private static bool WantsAttack(PlayerOrderComposerInput input)
        {
            return IsAny(input.CommandIntent.Intent.Role, "main", "maineffort") ||
                IsAny(input.CommandTask.Role, "maineffort") ||
                IsAny(input.CommandTask.Task, "attackobjective", "fixenemy", "attack", "seize");
        }

        private static bool WantsProbe(PlayerOrderComposerInput input)
        {
            return IsAny(input.CommandIntent.Intent.Role, "screen", "probe", "screeningforce") ||
                IsAny(input.CommandTask.Role, "screeningforce", "probe") ||
                IsAny(input.CommandTask.Task, "probe", "screen", "scout");
        }

        private static bool WantsAdvance(PlayerOrderComposerInput input)
        {
            return IsAny(input.CommandTask.Task, "formup", "advancetoassembly") ||
                IsAny(input.Doctrine.OperationPhase, "planning", "scouting", "forming");
        }

        private static bool IsFallbackRole(PlayerOrderComposerInput input)
        {
            return IsAny(input.CommandIntent.Intent.Role, "fallback", "fallbackguard") ||
                IsAny(input.CommandTask.Role, "fallback", "fallbackguard");
        }

        private static string ObjectiveKey(PlayerOrderComposerInput input)
        {
            if (!string.IsNullOrEmpty(input.CommandTask.ObjectiveKey)) return input.CommandTask.ObjectiveKey;
            if (!string.IsNullOrEmpty(input.Doctrine.ObjectiveKey)) return input.Doctrine.ObjectiveKey;
            return "objective";
        }

        private static string FallbackObjectiveKey(PlayerOrderComposerInput input)
        {
            return "fallback-line";
        }

        private static string SourceReason(PlayerOrderComposerInput input)
        {
            if (!string.IsNullOrEmpty(input.CommandTask.Reason)) return input.CommandTask.Reason;
            if (!string.IsNullOrEmpty(input.CommandIntent.Reason)) return input.CommandIntent.Reason;
            if (!string.IsNullOrEmpty(input.Doctrine.Reason)) return input.Doctrine.Reason;
            return "composer";
        }

        private static float RotationFor(PlayerOrderIntent intent)
        {
            return intent == PlayerOrderIntent.RetreatToExit || intent == PlayerOrderIntent.ProbeObjective ? -1f : 0f;
        }

        private static bool IsAny(string value, params string[] expected)
        {
            if (expected == null || expected.Length == 0) return false;
            string normalized = Normalize(value);
            for (int i = 0; i < expected.Length; i++)
            {
                if (normalized == Normalize(expected[i])) return true;
            }

            return false;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return value.Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).Trim().ToLowerInvariant();
        }
    }
}
