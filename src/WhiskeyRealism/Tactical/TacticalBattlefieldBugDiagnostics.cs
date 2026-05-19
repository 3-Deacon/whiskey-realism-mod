using System;
using System.Globalization;

namespace WhiskeyRealism.Tactical
{
    public enum TacticalBattlefieldBugObservationKind
    {
        None = 0,
        CurrentOrderReplacement = 1,
        DelayedWaypointDrift = 2,
        CourierQueueIndexMismatch = 3,
        ObjectiveChainPlayerSubordinate = 4,
        ReserveDirectPathBypass = 5,
        FallbackRetreatException = 6,
        PathfinderBacktrackShape = 7
    }

    public readonly struct TacticalCurrentOrderSignature
    {
        public static TacticalCurrentOrderSignature Empty =>
            new TacticalCurrentOrderSignature(0, -1, 0f, 0f, 0f, null);

        public TacticalCurrentOrderSignature(int unitId, int type, float x, float z, float rotation, string destination)
        {
            UnitId = unitId;
            Type = type;
            X = SanitizeFloat(x);
            Z = SanitizeFloat(z);
            Rotation = NormalizeDegrees(rotation);
            Destination = Safe(destination);
        }

        public int UnitId { get; }
        public int Type { get; }
        public float X { get; }
        public float Z { get; }
        public float Rotation { get; }
        public string Destination { get; }

        public bool IsEmpty => UnitId <= 0 || Type < 0;

        public string Signature =>
            "unit=" + ClampCount(UnitId) +
            " type=" + Type +
            " x=" + Bucket(X) +
            " z=" + Bucket(Z) +
            " rot=" + Bucket(Rotation) +
            " dest=" + Destination;

        internal static int ClampCount(int value)
        {
            return value < 0 ? 0 : value;
        }

        internal static float SanitizeFloat(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value;
        }

        internal static float NormalizeDegrees(float value)
        {
            value = SanitizeFloat(value) % 360f;
            if (value < 0f) value += 360f;
            return value;
        }

        internal static string Bucket(float value)
        {
            float bucketed = (float)(Math.Round(SanitizeFloat(value) * 2f) / 2f);
            return bucketed.ToString("0.0", CultureInfo.InvariantCulture);
        }

        internal static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value)) return "-";
            var chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (char.IsControl(c) || char.IsWhiteSpace(c) || c == '|' || c == '=' || c == '{' || c == '}')
                    chars[i] = '_';
            }

            return new string(chars);
        }

        internal static string CleanSignature(string value)
        {
            if (string.IsNullOrEmpty(value)) return "-";

            var chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (char.IsControl(chars[i]))
                    chars[i] = '_';
            }

            return new string(chars);
        }
    }

    public readonly struct TacticalBugDiagnosticDecision
    {
        public TacticalBugDiagnosticDecision(
            TacticalBattlefieldBugObservationKind kind,
            bool isRisk,
            string reason,
            string signature)
        {
            Kind = kind;
            IsRisk = isRisk;
            Reason = TacticalCurrentOrderSignature.Safe(reason);
            Signature = TacticalCurrentOrderSignature.CleanSignature(signature);
            Summary = Prefix(kind) +
                " risk=" + isRisk +
                " reason=" + Reason +
                " " + Signature;
        }

        public TacticalBattlefieldBugObservationKind Kind { get; }
        public bool IsRisk { get; }
        public bool IsDuplicateRisk => IsRisk;
        public string Reason { get; }
        public string Signature { get; }
        public string Summary { get; }

        private static string Prefix(TacticalBattlefieldBugObservationKind kind)
        {
            switch (kind)
            {
                case TacticalBattlefieldBugObservationKind.CurrentOrderReplacement:
                    return "[TacticalCurrentOrder]";
                case TacticalBattlefieldBugObservationKind.DelayedWaypointDrift:
                    return "[TacticalWaypointDrift]";
                case TacticalBattlefieldBugObservationKind.CourierQueueIndexMismatch:
                    return "[TacticalCourierQueue]";
                case TacticalBattlefieldBugObservationKind.ObjectiveChainPlayerSubordinate:
                    return "[TacticalObjectiveMove]";
                case TacticalBattlefieldBugObservationKind.ReserveDirectPathBypass:
                    return "[TacticalReserveMove]";
                case TacticalBattlefieldBugObservationKind.FallbackRetreatException:
                    return "[TacticalFallback]";
                case TacticalBattlefieldBugObservationKind.PathfinderBacktrackShape:
                    return "[TacticalPathShape]";
                default:
                    return "[TacticalDiagnostic]";
            }
        }
    }

    public readonly struct TacticalPathfinderAddPathDecision
    {
        public TacticalPathfinderAddPathDecision(
            bool shouldOverrideResult,
            int overrideResult,
            bool shouldRemoveAddedPath,
            string reason,
            string signature)
        {
            ShouldOverrideResult = shouldOverrideResult;
            OverrideResult = overrideResult;
            ShouldRemoveAddedPath = shouldRemoveAddedPath;
            Reason = TacticalCurrentOrderSignature.Safe(reason);
            Signature = TacticalCurrentOrderSignature.CleanSignature(signature);
        }

        public bool ShouldOverrideResult { get; }
        public int OverrideResult { get; }
        public bool ShouldRemoveAddedPath { get; }
        public string Reason { get; }
        public string Signature { get; }
        public bool IsBehaviorChange => ShouldOverrideResult || ShouldRemoveAddedPath;
    }

    public static class TacticalBattlefieldBugDiagnostics
    {
        public static TacticalBugDiagnosticDecision ClassifyCurrentOrderReplacement(
            bool calledFromCampaign,
            TacticalCurrentOrderSignature oldOrder,
            TacticalCurrentOrderSignature newOrder,
            float nearDistance,
            float nearRotationDegrees)
        {
            string signature = "calledFromCampaign=" + calledFromCampaign +
                " old={" + oldOrder.Signature + "}" +
                " new={" + newOrder.Signature + "}";

            if (!calledFromCampaign)
                return Decision(TacticalBattlefieldBugObservationKind.CurrentOrderReplacement, false, "battle-call-has-vanilla-duplicate-guard", signature);
            if (oldOrder.IsEmpty || newOrder.IsEmpty)
                return Decision(TacticalBattlefieldBugObservationKind.CurrentOrderReplacement, false, "missing-order", signature);
            if (oldOrder.UnitId != newOrder.UnitId)
                return Decision(TacticalBattlefieldBugObservationKind.CurrentOrderReplacement, false, "different-unit", signature);

            if (oldOrder.Type == newOrder.Type &&
                oldOrder.Destination == newOrder.Destination &&
                Distance(oldOrder, newOrder) <= Threshold(nearDistance) &&
                AngleDifference(oldOrder.Rotation, newOrder.Rotation) <= Threshold(nearRotationDegrees))
            {
                return Decision(TacticalBattlefieldBugObservationKind.CurrentOrderReplacement, true, "campaign-duplicate-near", signature);
            }

            return Decision(TacticalBattlefieldBugObservationKind.CurrentOrderReplacement, true, "campaign-replacement-material-change", signature);
        }

        public static TacticalBugDiagnosticDecision ClassifyDelayedWaypointDrift(
            bool orderDelayEnabled,
            bool activeMoveOrder,
            bool queueAdded,
            int pathCountBefore,
            int pathCountAfter,
            float xBefore,
            float zBefore,
            float xAfter,
            float zAfter)
        {
            int before = TacticalCurrentOrderSignature.ClampCount(pathCountBefore);
            int after = TacticalCurrentOrderSignature.ClampCount(pathCountAfter);
            string signature = "delay=" + orderDelayEnabled +
                " activeMove=" + activeMoveOrder +
                " queueAdded=" + queueAdded +
                " paths=" + before + "->" + after +
                " before=" + PointSignature(xBefore, zBefore) +
                " after=" + PointSignature(xAfter, zAfter);

            if (!orderDelayEnabled)
                return Decision(TacticalBattlefieldBugObservationKind.DelayedWaypointDrift, false, "delay-disabled", signature);
            if (!activeMoveOrder)
                return Decision(TacticalBattlefieldBugObservationKind.DelayedWaypointDrift, false, "no-active-move-order", signature);
            if (queueAdded)
                return Decision(TacticalBattlefieldBugObservationKind.DelayedWaypointDrift, false, "queue-added", signature);
            if (before != after || PointSignature(xBefore, zBefore) != PointSignature(xAfter, zAfter))
                return Decision(TacticalBattlefieldBugObservationKind.DelayedWaypointDrift, true, "path-mutated-without-queue", signature);

            return Decision(TacticalBattlefieldBugObservationKind.DelayedWaypointDrift, false, "path-stable", signature);
        }

        public static TacticalBugDiagnosticDecision ClassifyCourierQueueIndex(
            bool secondaryCourier,
            int orderQueueCount,
            int activeQueueIndex,
            int appendQueueIndex)
        {
            int queueCount = TacticalCurrentOrderSignature.ClampCount(orderQueueCount);
            string signature = "secondary=" + secondaryCourier +
                " queues=" + queueCount +
                " active=" + activeQueueIndex +
                " append=" + appendQueueIndex;

            if (!secondaryCourier)
                return Decision(TacticalBattlefieldBugObservationKind.CourierQueueIndexMismatch, false, "primary-courier", signature);
            if (queueCount <= 1)
                return Decision(TacticalBattlefieldBugObservationKind.CourierQueueIndexMismatch, false, "single-queue", signature);
            if (activeQueueIndex < 0 || appendQueueIndex < 0)
                return Decision(TacticalBattlefieldBugObservationKind.CourierQueueIndexMismatch, false, "unknown-index", signature);
            if (activeQueueIndex != appendQueueIndex)
                return Decision(TacticalBattlefieldBugObservationKind.CourierQueueIndexMismatch, true, "secondary-courier-appended-to-latest", signature);

            return Decision(TacticalBattlefieldBugObservationKind.CourierQueueIndexMismatch, false, "secondary-courier-active-queue", signature);
        }

        public static TacticalBugDiagnosticDecision ClassifyObjectiveChainMovement(
            bool objectiveChainMove,
            bool centerGroupUnderPlayerCommander,
            bool attachedPlayerSubordinate,
            int attachedUnitCount)
        {
            int attached = TacticalCurrentOrderSignature.ClampCount(attachedUnitCount);
            string signature = "objectiveChain=" + objectiveChainMove +
                " centerUnderCommander=" + centerGroupUnderPlayerCommander +
                " attachedPlayerSubordinate=" + attachedPlayerSubordinate +
                " attached=" + attached;

            if (!objectiveChainMove)
                return Decision(TacticalBattlefieldBugObservationKind.ObjectiveChainPlayerSubordinate, false, "no-objective-chain-move", signature);
            if (centerGroupUnderPlayerCommander)
                return Decision(TacticalBattlefieldBugObservationKind.ObjectiveChainPlayerSubordinate, true, "objective-chain-player-center-group", signature);
            if (!attachedPlayerSubordinate)
                return Decision(TacticalBattlefieldBugObservationKind.ObjectiveChainPlayerSubordinate, false, "ai-chain", signature);

            return Decision(TacticalBattlefieldBugObservationKind.ObjectiveChainPlayerSubordinate, true, "objective-chain-player-subordinate-attached", signature);
        }

        public static TacticalBugDiagnosticDecision ClassifyObjectiveChainMutation(
            bool exposedPlayerSubordinateChain,
            bool centerMutated,
            bool attachedPlayerSubordinateMutated,
            int changedUnitCount)
        {
            int changed = TacticalCurrentOrderSignature.ClampCount(changedUnitCount);
            string signature = "exposed=" + exposedPlayerSubordinateChain +
                " centerMutated=" + centerMutated +
                " attachedMutated=" + attachedPlayerSubordinateMutated +
                " changed=" + changed;

            if (!exposedPlayerSubordinateChain)
                return Decision(TacticalBattlefieldBugObservationKind.ObjectiveChainPlayerSubordinate, false, "objective-chain-ai-only-mutation", signature);
            if (attachedPlayerSubordinateMutated)
                return Decision(TacticalBattlefieldBugObservationKind.ObjectiveChainPlayerSubordinate, true, "objective-chain-player-subordinate-mutated", signature);
            if (centerMutated)
                return Decision(TacticalBattlefieldBugObservationKind.ObjectiveChainPlayerSubordinate, true, "objective-chain-center-mutated", signature);

            return Decision(TacticalBattlefieldBugObservationKind.ObjectiveChainPlayerSubordinate, false, "objective-chain-no-mutation", signature);
        }

        public static TacticalBugDiagnosticDecision ClassifyReserveDirectPathBypass(
            bool reserveSupportMove,
            bool orderDelayEnabled,
            bool directPathIssued,
            bool queuedOrderIssued,
            int reserveCandidateCount)
        {
            int candidates = TacticalCurrentOrderSignature.ClampCount(reserveCandidateCount);
            string signature = "reserveSupport=" + reserveSupportMove +
                " delay=" + orderDelayEnabled +
                " directPath=" + directPathIssued +
                " queued=" + queuedOrderIssued +
                " candidates=" + candidates;

            if (!reserveSupportMove)
                return Decision(TacticalBattlefieldBugObservationKind.ReserveDirectPathBypass, false, "no-reserve-support-move", signature);
            if (!orderDelayEnabled)
                return Decision(TacticalBattlefieldBugObservationKind.ReserveDirectPathBypass, false, "delay-disabled", signature);
            if (!directPathIssued)
                return Decision(TacticalBattlefieldBugObservationKind.ReserveDirectPathBypass, false, "no-direct-path", signature);
            if (queuedOrderIssued)
                return Decision(TacticalBattlefieldBugObservationKind.ReserveDirectPathBypass, false, "queued-order", signature);

            return Decision(TacticalBattlefieldBugObservationKind.ReserveDirectPathBypass, true, "reserve-direct-path-bypasses-delay", signature);
        }

        public static bool ShouldSuppressFallbackRetreatException(string methodName, Exception exception)
        {
            if (!(exception is NullReferenceException)) return false;
            return methodName == "MicroAICheckForRetreats" ||
                   methodName == "CheckLineFallbacks" ||
                   methodName == "CheckOutOfFireRange";
        }

        public static TacticalBugDiagnosticDecision ClassifyPathShape(
            bool showMovementOptions,
            bool pathCreated,
            int cornerCount,
            float directDistance,
            float pathLength,
            float firstSegmentDeltaDegrees,
            string navStatus,
            int pathStatus,
            bool orderDelayEnabled)
        {
            int corners = TacticalCurrentOrderSignature.ClampCount(cornerCount);
            float direct = Threshold(directDistance);
            float length = Threshold(pathLength);
            float delta = AngleDifference(0f, firstSegmentDeltaDegrees);
            float ratio = direct <= 0.1f ? 0f : length / direct;
            string status = TacticalCurrentOrderSignature.Safe(navStatus);
            string signature = "ui=" + showMovementOptions +
                " created=" + pathCreated +
                " corners=" + corners +
                " direct=" + TacticalCurrentOrderSignature.Bucket(direct) +
                " length=" + TacticalCurrentOrderSignature.Bucket(length) +
                " ratio=" + TacticalCurrentOrderSignature.Bucket(ratio) +
                " firstDelta=" + TacticalCurrentOrderSignature.Bucket(delta) +
                " navStatus=" + status +
                " pathStatus=" + pathStatus +
                " delay=" + orderDelayEnabled;

            if (!showMovementOptions)
                return Decision(TacticalBattlefieldBugObservationKind.PathfinderBacktrackShape, false, "non-ui-path", signature);
            if (!pathCreated)
                return Decision(TacticalBattlefieldBugObservationKind.PathfinderBacktrackShape, false, "no-path-created", signature);
            if (corners <= 0)
                return Decision(TacticalBattlefieldBugObservationKind.PathfinderBacktrackShape, false, "no-corners", signature);
            if (direct < 5f)
                return Decision(TacticalBattlefieldBugObservationKind.PathfinderBacktrackShape, false, "near-target", signature);
            if (!string.IsNullOrEmpty(status) && status != "-" && status != "PathComplete")
                return Decision(TacticalBattlefieldBugObservationKind.PathfinderBacktrackShape, true, "navmesh-noncomplete", signature);
            if (orderDelayEnabled && pathStatus >= 0 && pathStatus < 2)
                return Decision(TacticalBattlefieldBugObservationKind.PathfinderBacktrackShape, true, "path-not-transmitted", signature);
            if (delta > 90f)
                return Decision(TacticalBattlefieldBugObservationKind.PathfinderBacktrackShape, true, "backward-first-segment", signature);
            if (ratio >= 1.5f)
                return Decision(TacticalBattlefieldBugObservationKind.PathfinderBacktrackShape, true, "excessive-path-ratio", signature);

            return Decision(TacticalBattlefieldBugObservationKind.PathfinderBacktrackShape, false, "path-shape-normal", signature);
        }

        public static TacticalPathfinderAddPathDecision ClassifyAddPathOutcome(
            int vanillaResult,
            int pathCountBefore,
            int pathCountAfter,
            int cornerCount,
            string navStatus,
            float finalDistanceToTarget,
            float endpointTolerance)
        {
            int before = TacticalCurrentOrderSignature.ClampCount(pathCountBefore);
            int after = TacticalCurrentOrderSignature.ClampCount(pathCountAfter);
            int corners = TacticalCurrentOrderSignature.ClampCount(cornerCount);
            int result = vanillaResult == 0 ? 0 : 1;
            float finalDistance = Threshold(finalDistanceToTarget);
            float tolerance = Math.Max(0.1f, Threshold(endpointTolerance));
            string status = TacticalCurrentOrderSignature.Safe(navStatus);
            bool pathAdded = after > before;
            string signature = "result=" + result +
                " paths=" + before + "->" + after +
                " corners=" + corners +
                " navStatus=" + status +
                " finalDelta=" + TacticalCurrentOrderSignature.Bucket(finalDistance) +
                " tolerance=" + TacticalCurrentOrderSignature.Bucket(tolerance);

            if (!pathAdded)
                return AddPathDecision(false, result, false, "no-added-path", signature);
            if (corners <= 0)
                return AddPathDecision(true, 0, true, "no-corners", signature);
            if (!string.IsNullOrEmpty(status) && status != "-" && status != "PathComplete")
                return AddPathDecision(true, 0, true, "navmesh-noncomplete", signature);
            if (result == 0 && finalDistance <= tolerance)
                return AddPathDecision(true, 1, false, "endpoint-within-tolerance", signature);
            if (result == 0)
                return AddPathDecision(true, 0, true, "failed-endpoint-mismatch", signature);

            return AddPathDecision(false, result, false, "vanilla-accepted", signature);
        }

        private static TacticalBugDiagnosticDecision Decision(
            TacticalBattlefieldBugObservationKind kind,
            bool risk,
            string reason,
            string signature)
        {
            return new TacticalBugDiagnosticDecision(kind, risk, reason, signature);
        }

        private static TacticalPathfinderAddPathDecision AddPathDecision(
            bool overrideResult,
            int result,
            bool removeAddedPath,
            string reason,
            string signature)
        {
            return new TacticalPathfinderAddPathDecision(overrideResult, result, removeAddedPath, reason, signature);
        }

        private static float Threshold(float value)
        {
            value = TacticalCurrentOrderSignature.SanitizeFloat(value);
            return value < 0f ? 0f : value;
        }

        private static float Distance(TacticalCurrentOrderSignature a, TacticalCurrentOrderSignature b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static float AngleDifference(float a, float b)
        {
            float delta = Math.Abs((a - b) % 360f);
            return delta > 180f ? 360f - delta : delta;
        }

        private static string PointSignature(float x, float z)
        {
            return "x=" + TacticalCurrentOrderSignature.Bucket(x) +
                ",z=" + TacticalCurrentOrderSignature.Bucket(z);
        }
    }
}
