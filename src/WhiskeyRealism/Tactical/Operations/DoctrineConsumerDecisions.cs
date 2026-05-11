using System;

namespace WhiskeyRealism.Tactical.Operations
{
    public enum DoctrineConsumerAction
    {
        Observe = 0,
        Allow = 1,
        Deny = 2
    }

    public readonly struct DoctrineStanceDecision
    {
        public DoctrineStanceDecision(DoctrineConsumerAction action, string reason)
        {
            Action = action;
            Reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim();
        }

        public DoctrineConsumerAction Action { get; }
        public string Reason { get; }
    }

    public readonly struct DoctrineChargeDecision
    {
        public DoctrineChargeDecision(DoctrineConsumerAction action, string reason)
        {
            Action = action;
            Reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim();
        }

        public DoctrineConsumerAction Action { get; }
        public string Reason { get; }
    }

    public readonly struct DoctrineReserveDecision
    {
        public DoctrineReserveDecision(DoctrineConsumerAction action, CommandTaskType task, string reason)
        {
            Action = action;
            Task = task;
            Reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim();
        }

        public DoctrineConsumerAction Action { get; }
        public CommandTaskType Task { get; }
        public string Reason { get; }
    }

    public readonly struct DoctrineArtilleryDecision
    {
        public DoctrineArtilleryDecision(DoctrineConsumerAction action, string reason)
        {
            Action = action;
            Reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim();
        }

        public DoctrineConsumerAction Action { get; }
        public string Reason { get; }
    }

    public static class DoctrineConsumerDecisions
    {
        private const float FreshExposureConfidenceFloor = 0.75f;
        private const float TargetMatchTolerance = 100f;

        public static DoctrineStanceDecision DecideStance(
            CommandDoctrineOrder order,
            bool enemyMainLineExposed,
            float localOdds)
        {
            if (order.Task == CommandTaskType.ReserveWait)
                return new DoctrineStanceDecision(DoctrineConsumerAction.Deny, "reserve-held");
            if (order.Task == CommandTaskType.AttackObjective &&
                enemyMainLineExposed &&
                IsFinite(localOdds) &&
                localOdds >= 1.2f)
                return new DoctrineStanceDecision(DoctrineConsumerAction.Allow, "doctrine-attack");
            if (order.Task == CommandTaskType.FallBackToLine)
                return new DoctrineStanceDecision(DoctrineConsumerAction.Deny, "fallback");

            return new DoctrineStanceDecision(DoctrineConsumerAction.Observe, "no-doctrine-opinion");
        }

        public static DoctrineChargeDecision DecideCharge(
            CommandDoctrineOrder order,
            bool enemyMainLineExposed,
            float localOdds,
            bool targetRouted)
        {
            if (order.Task == CommandTaskType.ReserveWait)
                return new DoctrineChargeDecision(DoctrineConsumerAction.Deny, "reserve-held");
            if (order.Task == CommandTaskType.FallBackToLine)
                return new DoctrineChargeDecision(DoctrineConsumerAction.Deny, "fallback");
            if (!enemyMainLineExposed && !targetRouted)
                return new DoctrineChargeDecision(DoctrineConsumerAction.Observe, "main-line-not-exposed");
            if ((order.Task == CommandTaskType.AttackObjective ||
                    order.Task == CommandTaskType.SupportAttack) &&
                IsFinite(localOdds) &&
                localOdds >= 1.5f)
                return new DoctrineChargeDecision(DoctrineConsumerAction.Allow, "doctrine-charge");

            return new DoctrineChargeDecision(DoctrineConsumerAction.Observe, "odds-not-ready");
        }

        public static DoctrineReserveDecision DecideReserve(
            CommandDoctrineOrder order,
            float mainEffortOdds,
            float reserveFraction,
            float currentTimeSeconds)
        {
            if (order.Task == CommandTaskType.FallBackToLine)
                return new DoctrineReserveDecision(DoctrineConsumerAction.Allow, CommandTaskType.FallBackToLine, "fallback-relief");

            if (order.Role == CommandNodeRole.Reserve &&
                order.Task == CommandTaskType.ReserveWait &&
                IsFinite(mainEffortOdds) &&
                IsFinite(reserveFraction) &&
                mainEffortOdds < 0.9f &&
                reserveFraction >= 0.15f)
            {
                return new DoctrineReserveDecision(DoctrineConsumerAction.Allow, CommandTaskType.ReleaseReserve, "main-effort-under-pressure");
            }

            if (order.Role == CommandNodeRole.Reserve)
                return new DoctrineReserveDecision(DoctrineConsumerAction.Deny, CommandTaskType.ReserveWait, "reserve-held");

            return new DoctrineReserveDecision(DoctrineConsumerAction.Observe, order.Task, "no-doctrine-opinion");
        }

        public static DoctrineArtilleryDecision DecideArtillery(
            CommandDoctrineOrder order,
            bool enemyMainLineExposed,
            bool friendlyCloseRange)
        {
            if (friendlyCloseRange)
                return new DoctrineArtilleryDecision(DoctrineConsumerAction.Deny, "friendly-close-range");
            if (enemyMainLineExposed &&
                (order.Task == CommandTaskType.AttackObjective ||
                 order.Task == CommandTaskType.SupportAttack ||
                 order.Task == CommandTaskType.FixEnemy))
            {
                return new DoctrineArtilleryDecision(DoctrineConsumerAction.Allow, "support-main-effort");
            }

            return new DoctrineArtilleryDecision(DoctrineConsumerAction.Observe, "no-doctrine-opinion");
        }

        public static bool EnemyMainLineExposed(CommandDoctrineOrder order, BattlefieldPictureSnapshot picture)
        {
            BattlefieldObjectiveEstimate[] objectives = picture.Objectives;
            if (objectives == null || objectives.Length == 0) return false;

            if (HasKnownObjectiveId(order.ObjectiveId))
            {
                for (int i = 0; i < objectives.Length; i++)
                {
                    if (!string.Equals(objectives[i].ObjectiveId, order.ObjectiveId, StringComparison.Ordinal))
                        continue;

                    return FreshMainLineExposure(objectives[i]);
                }

                return false;
            }

            if (TryTargetExposure(order.PrimaryTarget, objectives, out bool primaryExposed)) return primaryExposed;
            if (TryTargetExposure(order.SupportTarget, objectives, out bool supportExposed)) return supportExposed;
            if (TryTargetExposure(order.FallbackTarget, objectives, out bool fallbackExposed)) return fallbackExposed;
            return false;
        }

        public static bool AllowsChargeAfterAuthoritativeGate(
            DoctrineChargeDecision doctrineDecision,
            bool authoritativeDenied)
        {
            if (doctrineDecision.Action == DoctrineConsumerAction.Deny) return false;
            return !authoritativeDenied;
        }

        private static bool TryTargetExposure(
            DoctrineTargetPoint target,
            BattlefieldObjectiveEstimate[] objectives,
            out bool exposed)
        {
            exposed = false;
            if (!target.HasValue) return false;

            float bestDistanceSquared = float.MaxValue;
            int bestIndex = -1;
            for (int i = 0; i < objectives.Length; i++)
            {
                float dx = objectives[i].X - target.X;
                float dz = objectives[i].Z - target.Z;
                float distanceSquared = dx * dx + dz * dz;
                if (!IsFinite(distanceSquared) || distanceSquared > TargetMatchTolerance * TargetMatchTolerance)
                    continue;
                if (distanceSquared >= bestDistanceSquared) continue;

                bestDistanceSquared = distanceSquared;
                bestIndex = i;
            }

            if (bestIndex < 0) return false;
            exposed = FreshMainLineExposure(objectives[bestIndex]);
            return true;
        }

        private static bool FreshMainLineExposure(BattlefieldObjectiveEstimate objective)
        {
            return objective.MainLineExposed &&
                objective.Confidence01 >= FreshExposureConfidenceFloor;
        }

        private static bool HasKnownObjectiveId(string objectiveId)
        {
            return !string.IsNullOrWhiteSpace(objectiveId) &&
                !string.Equals(objectiveId, "objective-unknown", StringComparison.Ordinal);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
