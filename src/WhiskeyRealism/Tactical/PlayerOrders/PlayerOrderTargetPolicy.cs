using System;

namespace WhiskeyRealism.Tactical.PlayerOrders
{
    internal static class PlayerOrderTargetPolicy
    {
        public static PlayerOrderPoint ResolveObjectivePoint(
            PlayerOrderPoint commandTarget,
            PlayerOrderPoint currentObjective,
            PlayerOrderPoint closestVisibleEnemy,
            bool allowVisibleEnemyFallback,
            out string source)
        {
            if (IsValid(commandTarget))
            {
                source = "command-target";
                return commandTarget;
            }

            if (IsValid(currentObjective))
            {
                source = "current-objective";
                return currentObjective;
            }

            if (allowVisibleEnemyFallback && IsValid(closestVisibleEnemy))
            {
                source = "visible-enemy";
                return closestVisibleEnemy;
            }

            source = "none";
            return default(PlayerOrderPoint);
        }

        public static bool IsValid(PlayerOrderPoint point)
        {
            return IsFinite(point.X) &&
                IsFinite(point.Z) &&
                (Math.Abs(point.X) > 0.001f || Math.Abs(point.Z) > 0.001f || point.ValidExitPoint);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
