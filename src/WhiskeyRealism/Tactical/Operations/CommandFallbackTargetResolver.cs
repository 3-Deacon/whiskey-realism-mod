using System;

namespace WhiskeyRealism.Tactical.Operations
{
    public static class CommandFallbackTargetResolver
    {
        public static bool TryResolve(
            TacticalMapPoint current,
            TacticalMapPoint? objective,
            TacticalMapPoint? threat,
            float standOff,
            float minDistance,
            float maxDistance,
            out TacticalMapPoint target,
            out string source)
        {
            target = default;
            source = "unresolved";

            float boundedStandOff = ClampDistance(standOff, minDistance, maxDistance);
            if (boundedStandOff <= 0f) return false;

            if (objective.HasValue &&
                TryStepAway(current, objective.Value, boundedStandOff, minDistance, maxDistance, out target))
            {
                source = "objective";
                return true;
            }

            if (threat.HasValue &&
                TryStepAway(current, threat.Value, boundedStandOff, minDistance, maxDistance, out target))
            {
                source = "visible-threat";
                return true;
            }

            return false;
        }

        private static bool TryStepAway(
            TacticalMapPoint current,
            TacticalMapPoint awayFrom,
            float standOff,
            float minDistance,
            float maxDistance,
            out TacticalMapPoint target)
        {
            target = default;
            float dx = current.X - awayFrom.X;
            float dz = current.Z - awayFrom.Z;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < 0.01f || float.IsNaN(length) || float.IsInfinity(length)) return false;

            float distance = ClampDistance(standOff, minDistance, maxDistance);
            if (distance <= 0f) return false;

            target = new TacticalMapPoint(
                current.X + (dx / length * distance),
                current.Z + (dz / length * distance));
            return true;
        }

        private static float ClampDistance(float value, float minDistance, float maxDistance)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            float min = SanitizeFloorZero(minDistance);
            float max = SanitizeFloorZero(maxDistance);
            if (max > 0f && value > max) value = max;
            return value < min ? 0f : value;
        }

        private static float SanitizeFloorZero(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value < 0f ? 0f : value;
        }
    }
}
