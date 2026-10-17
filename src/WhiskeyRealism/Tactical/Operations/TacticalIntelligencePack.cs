using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Operations
{
    public enum TacticalBattlefrontGap
    {
        None = 0,
        NoFrontage = 1,
        Cohesive = 2,
        WideUnsupportedGap = 3
    }

    public readonly struct TacticalBattlefrontSnapshot
    {
        public TacticalBattlefrontSnapshot(
            bool hasFrontage,
            DoctrineTargetPoint center,
            DoctrineTargetPoint leftFlankTarget,
            DoctrineTargetPoint rightFlankTarget,
            float frontageWidth,
            TacticalBattlefrontGap gap,
            string reason)
        {
            HasFrontage = hasFrontage;
            Center = center;
            LeftFlankTarget = leftFlankTarget;
            RightFlankTarget = rightFlankTarget;
            FrontageWidth = SanitizeNonNegative(frontageWidth);
            Gap = gap;
            Reason = string.IsNullOrWhiteSpace(reason) ? "battlefront-unspecified" : reason.Trim();
        }

        public bool HasFrontage { get; }
        public DoctrineTargetPoint Center { get; }
        public DoctrineTargetPoint LeftFlankTarget { get; }
        public DoctrineTargetPoint RightFlankTarget { get; }
        public float FrontageWidth { get; }
        public TacticalBattlefrontGap Gap { get; }
        public string Reason { get; }

        private static float SanitizeNonNegative(float value)
        {
            if (!IsFinite(value) || value < 0f) return 0f;
            return value;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public static class TacticalBattlefrontGeometry
    {
        private const float UnsupportedGapWidth = 700f;

        public static TacticalBattlefrontSnapshot Build(
            CommandNodeOperationalState[] nodes,
            BattlefieldObjectiveEstimate objective)
        {
            nodes = nodes ?? Array.Empty<CommandNodeOperationalState>();
            float minX = 0f;
            float maxX = 0f;
            float minZ = 0f;
            float maxZ = 0f;
            int count = 0;

            for (int i = 0; i < nodes.Length; i++)
            {
                CommandNodeOperationalState node = nodes[i];
                if (node.Role == CommandNodeRole.Unknown || !IsFinite(node.X) || !IsFinite(node.Z))
                    continue;

                if (count == 0)
                {
                    minX = maxX = node.X;
                    minZ = maxZ = node.Z;
                }
                else
                {
                    if (node.X < minX) minX = node.X;
                    if (node.X > maxX) maxX = node.X;
                    if (node.Z < minZ) minZ = node.Z;
                    if (node.Z > maxZ) maxZ = node.Z;
                }

                count++;
            }

            if (count == 0)
            {
                return new TacticalBattlefrontSnapshot(
                    false,
                    DoctrineTargetPoint.None,
                    DoctrineTargetPoint.None,
                    DoctrineTargetPoint.None,
                    0f,
                    TacticalBattlefrontGap.NoFrontage,
                    "battlefront-no-command-nodes");
            }

            float widthX = maxX - minX;
            float widthZ = maxZ - minZ;
            float width = (float)Math.Sqrt((widthX * widthX) + (widthZ * widthZ));
            var center = DoctrineTargetPoint.From((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);

            Direction direction = Direction.FromCenterToObjective(center, objective);
            DoctrineTargetPoint left = DoctrineTargetPoint.From(
                center.X + direction.LateralX * Math.Max(250f, width * 0.5f),
                center.Z + direction.LateralZ * Math.Max(250f, width * 0.5f));
            DoctrineTargetPoint right = DoctrineTargetPoint.From(
                center.X - direction.LateralX * Math.Max(250f, width * 0.5f),
                center.Z - direction.LateralZ * Math.Max(250f, width * 0.5f));

            TacticalBattlefrontGap gap = width >= UnsupportedGapWidth && count <= 2
                ? TacticalBattlefrontGap.WideUnsupportedGap
                : TacticalBattlefrontGap.Cohesive;

            return new TacticalBattlefrontSnapshot(
                true,
                center,
                left,
                right,
                width,
                gap,
                gap == TacticalBattlefrontGap.WideUnsupportedGap ? "battlefront-wide-gap" : "battlefront-cohesive");
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private readonly struct Direction
        {
            private Direction(float x, float z)
            {
                X = x;
                Z = z;
                LateralX = -z;
                LateralZ = x;
            }

            public float X { get; }
            public float Z { get; }
            public float LateralX { get; }
            public float LateralZ { get; }

            public static Direction FromCenterToObjective(
                DoctrineTargetPoint center,
                BattlefieldObjectiveEstimate objective)
            {
                float dx = objective.X - center.X;
                float dz = objective.Z - center.Z;
                float length = (float)Math.Sqrt((dx * dx) + (dz * dz));
                return length > 0.001f
                    ? new Direction(dx / length, dz / length)
                    : new Direction(0f, 1f);
            }
        }
    }

    public enum TacticalFallbackStep
    {
        Hold = 0,
        Stabilize = 1,
        ScreenWithdrawal = 2,
        FallbackByBounds = 3,
        RearGuard = 4,
        FullRetreat = 5
    }

    public readonly struct TacticalFallbackInput
    {
        public TacticalFallbackInput(
            float odds,
            float morale01,
            float fatigue01,
            float flankThreat01,
            bool rearThreat,
            bool hasFallbackTarget,
            bool reserveReliefAvailable,
            bool wlGateAllows)
        {
            Odds = IsFinite(odds) ? odds : 0f;
            Morale01 = Clamp01(morale01);
            Fatigue01 = Clamp01(fatigue01);
            FlankThreat01 = Clamp01(flankThreat01);
            RearThreat = rearThreat;
            HasFallbackTarget = hasFallbackTarget;
            ReserveReliefAvailable = reserveReliefAvailable;
            WlGateAllows = wlGateAllows;
        }

        public float Odds { get; }
        public float Morale01 { get; }
        public float Fatigue01 { get; }
        public float FlankThreat01 { get; }
        public bool RearThreat { get; }
        public bool HasFallbackTarget { get; }
        public bool ReserveReliefAvailable { get; }
        public bool WlGateAllows { get; }

        private static float Clamp01(float value)
        {
            if (!IsFinite(value) || value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public readonly struct TacticalFallbackDecision
    {
        public TacticalFallbackDecision(
            TacticalFallbackStep step,
            bool requiresOrderedFallback,
            bool preservesForce,
            string reason)
        {
            Step = step;
            RequiresOrderedFallback = requiresOrderedFallback;
            PreservesForce = preservesForce;
            Reason = string.IsNullOrWhiteSpace(reason) ? "fallback-unspecified" : reason.Trim();
        }

        public TacticalFallbackStep Step { get; }
        public bool RequiresOrderedFallback { get; }
        public bool PreservesForce { get; }
        public string Reason { get; }
    }

    public static class TacticalFallbackLadder
    {
        public static TacticalFallbackDecision Decide(TacticalFallbackInput input)
        {
            if (!input.WlGateAllows)
                return new TacticalFallbackDecision(TacticalFallbackStep.Hold, false, false, "wl-gate");

            TacticalFallbackStep step = TacticalFallbackStep.Hold;
            if (input.Odds < 0.45f || input.Morale01 < 0.20f)
                step = TacticalFallbackStep.RearGuard;
            else if (input.Odds < 0.75f || input.FlankThreat01 >= 0.75f)
                step = TacticalFallbackStep.ScreenWithdrawal;
            else if (input.Odds < 0.95f || input.Morale01 < 0.50f)
                step = TacticalFallbackStep.Stabilize;

            if (input.HasFallbackTarget &&
                step == TacticalFallbackStep.Stabilize &&
                (input.FlankThreat01 >= 0.60f || input.Odds < 0.85f))
            {
                step = TacticalFallbackStep.ScreenWithdrawal;
            }

            if (input.Fatigue01 >= 0.80f || input.RearThreat)
                step = Bump(step);
            if (input.ReserveReliefAvailable && step == TacticalFallbackStep.FullRetreat)
                step = TacticalFallbackStep.RearGuard;
            if (input.HasFallbackTarget && step == TacticalFallbackStep.ScreenWithdrawal)
                step = TacticalFallbackStep.FallbackByBounds;

            bool ordered = step == TacticalFallbackStep.FallbackByBounds ||
                step == TacticalFallbackStep.RearGuard ||
                step == TacticalFallbackStep.FullRetreat;
            bool preserve = step == TacticalFallbackStep.RearGuard ||
                step == TacticalFallbackStep.FullRetreat;

            return new TacticalFallbackDecision(step, ordered, preserve, Reason(step));
        }

        private static TacticalFallbackStep Bump(TacticalFallbackStep step)
        {
            switch (step)
            {
                case TacticalFallbackStep.Hold: return TacticalFallbackStep.Stabilize;
                case TacticalFallbackStep.Stabilize: return TacticalFallbackStep.ScreenWithdrawal;
                case TacticalFallbackStep.ScreenWithdrawal: return TacticalFallbackStep.FallbackByBounds;
                case TacticalFallbackStep.FallbackByBounds: return TacticalFallbackStep.RearGuard;
                case TacticalFallbackStep.RearGuard: return TacticalFallbackStep.FullRetreat;
                default: return step;
            }
        }

        private static string Reason(TacticalFallbackStep step)
        {
            switch (step)
            {
                case TacticalFallbackStep.Stabilize: return "stabilize";
                case TacticalFallbackStep.ScreenWithdrawal: return "screen-withdrawal";
                case TacticalFallbackStep.FallbackByBounds: return "fallback-by-bounds";
                case TacticalFallbackStep.RearGuard: return "rear-guard";
                case TacticalFallbackStep.FullRetreat: return "full-retreat";
                default: return "hold";
            }
        }
    }

    public enum TacticalArtilleryMission
    {
        Preserve = 0,
        SupportMainEffort = 1,
        CounterBattery = 2,
        ConserveAmmo = 3,
        Displace = 4,
        HoldFireDangerClose = 5,
        DefensiveFallback = 6
    }

    public enum TacticalArtilleryAmmoMission
    {
        Preserve = 0,
        SolidShot = 1,
        Shell = 2,
        Shrapnel = 3,
        Canister = 4
    }

    public readonly struct TacticalArtilleryMissionInput
    {
        public TacticalArtilleryMissionInput(
            bool requestedSupport,
            bool enemyArtilleryVisible,
            float ammoRatio01,
            float targetDistance,
            float optimalRange,
            float maxRange,
            bool friendlyDangerClose,
            bool threatenedByCloseEnemy,
            bool canDisplace)
            : this(
                requestedSupport,
                enemyArtilleryVisible,
                ammoRatio01,
                targetDistance,
                optimalRange,
                maxRange,
                friendlyDangerClose,
                threatenedByCloseEnemy,
                canDisplace,
                fieldOfFireClear: true,
                hasWeakPointTarget: false,
                weakPointX: 0f,
                weakPointZ: 0f,
                hasSafeRepositionTarget: false,
                safeRepositionX: 0f,
                safeRepositionZ: 0f)
        {
        }

        public TacticalArtilleryMissionInput(
            bool requestedSupport,
            bool enemyArtilleryVisible,
            float ammoRatio01,
            float targetDistance,
            float optimalRange,
            float maxRange,
            bool friendlyDangerClose,
            bool threatenedByCloseEnemy,
            bool canDisplace,
            bool fieldOfFireClear,
            bool hasWeakPointTarget,
            float weakPointX,
            float weakPointZ,
            bool hasSafeRepositionTarget,
            float safeRepositionX,
            float safeRepositionZ)
        {
            RequestedSupport = requestedSupport;
            EnemyArtilleryVisible = enemyArtilleryVisible;
            AmmoRatio01 = Clamp01(ammoRatio01);
            TargetDistance = SanitizeNonNegative(targetDistance);
            OptimalRange = SanitizeNonNegative(optimalRange);
            MaxRange = SanitizeNonNegative(maxRange);
            FriendlyDangerClose = friendlyDangerClose;
            ThreatenedByCloseEnemy = threatenedByCloseEnemy;
            CanDisplace = canDisplace;
            FieldOfFireClear = fieldOfFireClear;
            HasWeakPointTarget = hasWeakPointTarget && IsFiniteArtilleryValue(weakPointX) && IsFiniteArtilleryValue(weakPointZ);
            WeakPointTarget = HasWeakPointTarget
                ? DoctrineTargetPoint.From(weakPointX, weakPointZ)
                : DoctrineTargetPoint.None;
            HasSafeRepositionTarget = hasSafeRepositionTarget && IsFiniteArtilleryValue(safeRepositionX) && IsFiniteArtilleryValue(safeRepositionZ);
            SafeRepositionTarget = HasSafeRepositionTarget
                ? DoctrineTargetPoint.From(safeRepositionX, safeRepositionZ)
                : DoctrineTargetPoint.None;
        }

        public bool RequestedSupport { get; }
        public bool EnemyArtilleryVisible { get; }
        public float AmmoRatio01 { get; }
        public float TargetDistance { get; }
        public float OptimalRange { get; }
        public float MaxRange { get; }
        public bool FriendlyDangerClose { get; }
        public bool ThreatenedByCloseEnemy { get; }
        public bool CanDisplace { get; }
        public bool FieldOfFireClear { get; }
        public bool HasWeakPointTarget { get; }
        public DoctrineTargetPoint WeakPointTarget { get; }
        public bool HasSafeRepositionTarget { get; }
        public DoctrineTargetPoint SafeRepositionTarget { get; }

        private static float SanitizeNonNegative(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return 0f;
            return value;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static bool IsFiniteArtilleryValue(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public readonly struct TacticalArtilleryMissionDecision
    {
        public TacticalArtilleryMissionDecision(TacticalArtilleryMission mission, bool allowsFire, string reason)
            : this(
                mission,
                allowsFire,
                TacticalArtilleryAmmoMission.Preserve,
                DoctrineTargetPoint.None,
                false,
                DoctrineTargetPoint.None,
                reason)
        {
        }

        public TacticalArtilleryMissionDecision(
            TacticalArtilleryMission mission,
            bool allowsFire,
            TacticalArtilleryAmmoMission ammoMission,
            DoctrineTargetPoint assignmentTarget,
            bool shouldReposition,
            DoctrineTargetPoint repositionTarget,
            string reason)
        {
            Mission = mission;
            AllowsFire = allowsFire;
            AmmoMission = ammoMission;
            AssignmentTarget = assignmentTarget;
            ShouldReposition = shouldReposition;
            RepositionTarget = repositionTarget;
            Reason = string.IsNullOrWhiteSpace(reason) ? "artillery-unspecified" : reason.Trim();
        }

        public TacticalArtilleryMission Mission { get; }
        public bool AllowsFire { get; }
        public TacticalArtilleryAmmoMission AmmoMission { get; }
        public DoctrineTargetPoint AssignmentTarget { get; }
        public bool ShouldReposition { get; }
        public DoctrineTargetPoint RepositionTarget { get; }
        public string Reason { get; }
    }

    public static class TacticalArtilleryMissionPlanner
    {
        public static TacticalArtilleryMissionDecision Decide(TacticalArtilleryMissionInput input)
        {
            if (input.FriendlyDangerClose)
                return new TacticalArtilleryMissionDecision(
                    TacticalArtilleryMission.HoldFireDangerClose,
                    false,
                    TacticalArtilleryAmmoMission.Canister,
                    input.WeakPointTarget,
                    false,
                    DoctrineTargetPoint.None,
                    "friendly-danger-close");
            if (input.ThreatenedByCloseEnemy)
                return new TacticalArtilleryMissionDecision(
                    input.CanDisplace ? TacticalArtilleryMission.Displace : TacticalArtilleryMission.DefensiveFallback,
                    false,
                    TacticalArtilleryAmmoMission.Preserve,
                    input.WeakPointTarget,
                    input.CanDisplace && input.HasSafeRepositionTarget,
                    input.SafeRepositionTarget,
                    input.CanDisplace ? "displace" : "defensive-fallback");
            if (input.AmmoRatio01 < 0.10f)
                return new TacticalArtilleryMissionDecision(TacticalArtilleryMission.ConserveAmmo, false, "low-ammo");
            if (!input.FieldOfFireClear && input.CanDisplace && input.HasSafeRepositionTarget)
                return new TacticalArtilleryMissionDecision(
                    TacticalArtilleryMission.Displace,
                    false,
                    TacticalArtilleryAmmoMission.Preserve,
                    input.WeakPointTarget,
                    true,
                    input.SafeRepositionTarget,
                    "field-of-fire-reposition");
            if (input.EnemyArtilleryVisible && InRange(input))
                return new TacticalArtilleryMissionDecision(
                    TacticalArtilleryMission.CounterBattery,
                    true,
                    input.TargetDistance > input.OptimalRange ? TacticalArtilleryAmmoMission.SolidShot : TacticalArtilleryAmmoMission.Shell,
                    input.WeakPointTarget,
                    false,
                    DoctrineTargetPoint.None,
                    "counterbattery");
            if (input.RequestedSupport && InRange(input) && input.AmmoRatio01 >= 0.20f)
                return new TacticalArtilleryMissionDecision(
                    TacticalArtilleryMission.SupportMainEffort,
                    true,
                    input.TargetDistance > input.OptimalRange ? TacticalArtilleryAmmoMission.Shell : TacticalArtilleryAmmoMission.Shrapnel,
                    input.WeakPointTarget,
                    false,
                    DoctrineTargetPoint.None,
                    "support-main-effort");
            return new TacticalArtilleryMissionDecision(TacticalArtilleryMission.Preserve, false, "preserve");
        }

        private static bool InRange(TacticalArtilleryMissionInput input)
        {
            float max = input.MaxRange > 0f ? input.MaxRange : Math.Max(1f, input.OptimalRange);
            return input.TargetDistance <= max;
        }
    }
}
