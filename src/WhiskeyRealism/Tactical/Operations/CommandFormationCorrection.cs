using System;

namespace WhiskeyRealism.Tactical.Operations
{
    public static class CommandFormationCorrection
    {
        public static bool NeedsCorrection(
            int actualFormation,
            int orderedFormation,
            int groupFormation,
            int targetFormation)
        {
            if (targetFormation < 0 || targetFormation > 4) return false;

            bool hasAnySignal = false;
            if (actualFormation >= 0)
            {
                hasAnySignal = true;
                if (actualFormation != targetFormation) return true;
            }

            if (orderedFormation >= 0)
            {
                hasAnySignal = true;
                if (orderedFormation != targetFormation) return true;
            }

            if (groupFormation >= 0)
            {
                hasAnySignal = true;
                if (groupFormation != targetFormation) return true;
            }

            return !hasAnySignal;
        }

        public static bool ShouldFaceThreat(CommandTaskType task)
        {
            return task == CommandTaskType.FallBackToLine ||
                task == CommandTaskType.HoldObjective ||
                task == CommandTaskType.HoldChoke ||
                task == CommandTaskType.FixEnemy ||
                task == CommandTaskType.GuardFlank ||
                task == CommandTaskType.Delay ||
                task == CommandTaskType.Consolidate ||
                task == CommandTaskType.ReserveWait;
        }

        public static float ThreatFacingRotationDegrees(
            float ownX,
            float ownZ,
            float enemyX,
            float enemyZ)
        {
            float dx = enemyX - ownX;
            float dz = enemyZ - ownZ;
            return (float)(Math.Atan2(dx, dz) / Math.PI * 180.0);
        }

        public static bool NeedsFacingCorrection(
            float currentRotationDegrees,
            float targetRotationDegrees,
            float toleranceDegrees)
        {
            if (float.IsNaN(currentRotationDegrees) ||
                float.IsNaN(targetRotationDegrees) ||
                float.IsInfinity(currentRotationDegrees) ||
                float.IsInfinity(targetRotationDegrees))
                return false;

            float tolerance = Math.Max(0f, toleranceDegrees);
            float delta = NormalizeDelta(currentRotationDegrees - targetRotationDegrees);
            return Math.Abs(delta) > tolerance;
        }

        private static float NormalizeDelta(float value)
        {
            float delta = value % 360f;
            if (delta > 180f) delta -= 360f;
            if (delta < -180f) delta += 360f;
            return delta;
        }
    }
}
