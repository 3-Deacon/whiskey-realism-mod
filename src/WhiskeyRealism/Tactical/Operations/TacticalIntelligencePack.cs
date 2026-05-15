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

    public enum TacticalMassingPhase
    {
        NoContact = 0,
        ProbeForLine = 1,
        FormLine = 2,
        MassSupport = 3,
        CommitAssault = 4,
        Exploit = 5,
        PauseOrFallback = 6
    }

    public readonly struct TacticalMassingInput
    {
        public TacticalMassingInput(
            TacticalOperationPhase operationPhase,
            int mainEffortCount,
            int supportCount,
            int reserveCount,
            bool artilleryReady,
            bool objectiveExposed,
            float objectiveConfidence01,
            float ownStrength,
            float enemyStrength,
            float reserveFraction)
            : this(
                operationPhase,
                mainEffortCount,
                supportCount,
                reserveCount,
                artilleryReady,
                objectiveExposed,
                objectiveConfidence01,
                ownStrength,
                enemyStrength,
                reserveFraction,
                new TacticalEnduranceDecision(
                    canAssault: true,
                    canHold: true,
                    canFallback: true,
                    needsRelief: false,
                    reason: "endurance-ready"))
        {
        }

        public TacticalMassingInput(
            TacticalOperationPhase operationPhase,
            int mainEffortCount,
            int supportCount,
            int reserveCount,
            bool artilleryReady,
            bool objectiveExposed,
            float objectiveConfidence01,
            float ownStrength,
            float enemyStrength,
            float reserveFraction,
            TacticalEnduranceDecision endurance)
        {
            OperationPhase = operationPhase;
            MainEffortCount = Math.Max(0, mainEffortCount);
            SupportCount = Math.Max(0, supportCount);
            ReserveCount = Math.Max(0, reserveCount);
            ArtilleryReady = artilleryReady;
            ObjectiveExposed = objectiveExposed;
            ObjectiveConfidence01 = Clamp01(objectiveConfidence01);
            OwnStrength = SanitizeNonNegative(ownStrength);
            EnemyStrength = SanitizeNonNegative(enemyStrength);
            ReserveFraction = Clamp01(reserveFraction);
            Endurance = endurance;
        }

        public TacticalOperationPhase OperationPhase { get; }
        public int MainEffortCount { get; }
        public int SupportCount { get; }
        public int ReserveCount { get; }
        public bool ArtilleryReady { get; }
        public bool ObjectiveExposed { get; }
        public float ObjectiveConfidence01 { get; }
        public float OwnStrength { get; }
        public float EnemyStrength { get; }
        public float ReserveFraction { get; }
        public TacticalEnduranceDecision Endurance { get; }

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

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public readonly struct TacticalMassingDecision
    {
        public TacticalMassingDecision(TacticalMassingPhase phase, bool mayCommitAssault, string reason)
        {
            Phase = phase;
            MayCommitAssault = mayCommitAssault;
            Reason = string.IsNullOrWhiteSpace(reason) ? "massing-unspecified" : reason.Trim();
        }

        public TacticalMassingPhase Phase { get; }
        public bool MayCommitAssault { get; }
        public string Reason { get; }
    }

    public static class TacticalMassingCycle
    {
        public static TacticalMassingDecision Evaluate(TacticalMassingInput input)
        {
            if (!input.ObjectiveExposed || input.ObjectiveConfidence01 < 0.45f)
                return new TacticalMassingDecision(TacticalMassingPhase.ProbeForLine, false, "probe-contact");

            if (input.MainEffortCount <= 0)
                return new TacticalMassingDecision(TacticalMassingPhase.FormLine, false, "no-main-effort");

            float odds = input.OwnStrength / Math.Max(1f, input.EnemyStrength);
            if (input.SupportCount <= 0 && odds < 1.65f)
                return new TacticalMassingDecision(TacticalMassingPhase.MassSupport, false, "support-not-ready");

            if (input.ReserveFraction < 0.10f && input.ReserveCount <= 0)
                return new TacticalMassingDecision(TacticalMassingPhase.MassSupport, false, "reserve-not-ready");

            if (!input.Endurance.CanAssault || input.Endurance.NeedsRelief)
                return new TacticalMassingDecision(TacticalMassingPhase.MassSupport, false, "endurance-" + input.Endurance.Reason);

            if (!input.ArtilleryReady && odds < 1.80f)
                return new TacticalMassingDecision(TacticalMassingPhase.MassSupport, false, "artillery-not-ready");

            if (odds < 1.35f)
                return new TacticalMassingDecision(TacticalMassingPhase.PauseOrFallback, false, "odds-not-ready");

            if (input.OperationPhase == TacticalOperationPhase.Exploiting && odds >= 1.50f)
                return new TacticalMassingDecision(TacticalMassingPhase.Exploit, true, "exploit-ready");

            bool mayAssault = input.OperationPhase == TacticalOperationPhase.Committed &&
                input.ObjectiveConfidence01 >= 0.70f;
            return new TacticalMassingDecision(
                mayAssault ? TacticalMassingPhase.CommitAssault : TacticalMassingPhase.FormLine,
                mayAssault,
                mayAssault ? "commit-ready" : "formed-not-committed");
        }
    }

    public enum OperationalReserveMission
    {
        Hold = 0,
        Rally = 1,
        RelieveLine = 2,
        Counterattack = 3,
        SealFlank = 4,
        RefuseReserve = 5,
        WithdrawReserve = 6,
        FinalReserve = 7,
        ExploitReserve = 8,
        FlankShift = 9
    }

    public readonly struct OperationalReserveInput
    {
        public OperationalReserveInput(
            float reserveFraction,
            float mainEffortOdds,
            float flankThreat01,
            float reserveEndurance01,
            bool assaultAuthorized,
            bool fallbackPressure)
            : this(
                reserveFraction,
                mainEffortOdds,
                flankThreat01,
                reserveEndurance01,
                assaultAuthorized,
                fallbackPressure,
                minimumHeldFraction: 0.08f,
                lineReliefPressure01: fallbackPressure ? 1f : 0f,
                exploitOpportunity01: 0f)
        {
        }

        public OperationalReserveInput(
            float reserveFraction,
            float mainEffortOdds,
            float flankThreat01,
            float reserveEndurance01,
            bool assaultAuthorized,
            bool fallbackPressure,
            float minimumHeldFraction,
            float lineReliefPressure01,
            float exploitOpportunity01)
        {
            ReserveFraction = Clamp01(reserveFraction);
            MainEffortOdds = IsFinite(mainEffortOdds) ? mainEffortOdds : 0f;
            FlankThreat01 = Clamp01(flankThreat01);
            ReserveEndurance01 = Clamp01(reserveEndurance01);
            AssaultAuthorized = assaultAuthorized;
            FallbackPressure = fallbackPressure;
            MinimumHeldFraction = Clamp01(minimumHeldFraction);
            LineReliefPressure01 = Clamp01(lineReliefPressure01);
            ExploitOpportunity01 = Clamp01(exploitOpportunity01);
        }

        public float ReserveFraction { get; }
        public float MainEffortOdds { get; }
        public float FlankThreat01 { get; }
        public float ReserveEndurance01 { get; }
        public bool AssaultAuthorized { get; }
        public bool FallbackPressure { get; }
        public float MinimumHeldFraction { get; }
        public float LineReliefPressure01 { get; }
        public float ExploitOpportunity01 { get; }

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

    public readonly struct OperationalReserveDecision
    {
        public OperationalReserveDecision(OperationalReserveMission mission, bool shouldMove, string reason)
            : this(mission, shouldMove, 0f, false, reason)
        {
        }

        public OperationalReserveDecision(
            OperationalReserveMission mission,
            bool shouldMove,
            float commitFraction01,
            bool protectsFinalReserve,
            string reason)
        {
            Mission = mission;
            ShouldMove = shouldMove;
            CommitFraction01 = Clamp01(commitFraction01);
            ProtectsFinalReserve = protectsFinalReserve;
            Reason = string.IsNullOrWhiteSpace(reason) ? "reserve-unspecified" : reason.Trim();
        }

        public OperationalReserveMission Mission { get; }
        public bool ShouldMove { get; }
        public float CommitFraction01 { get; }
        public bool ProtectsFinalReserve { get; }
        public string Reason { get; }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public static class OperationalReserveDoctrine
    {
        public static OperationalReserveDecision Decide(OperationalReserveInput input)
        {
            if (input.ReserveFraction <= 0.02f)
                return new OperationalReserveDecision(OperationalReserveMission.Hold, false, "no-reserve");
            if (input.ReserveFraction <= Math.Max(0.02f, input.MinimumHeldFraction))
                return new OperationalReserveDecision(
                    OperationalReserveMission.FinalReserve,
                    false,
                    0f,
                    true,
                    "final-reserve");
            if (input.ReserveEndurance01 < 0.25f)
                return new OperationalReserveDecision(OperationalReserveMission.WithdrawReserve, true, "reserve-spent");
            if (input.FallbackPressure || input.LineReliefPressure01 >= 0.70f)
                return new OperationalReserveDecision(
                    OperationalReserveMission.RelieveLine,
                    true,
                    CommitFraction(input, 0.45f),
                    false,
                    "line-relief");
            if (input.FlankThreat01 >= 0.70f)
                return new OperationalReserveDecision(
                    OperationalReserveMission.SealFlank,
                    true,
                    CommitFraction(input, 0.35f),
                    false,
                    "flank-threat");
            if (input.MainEffortOdds < 0.90f)
                return new OperationalReserveDecision(
                    OperationalReserveMission.RelieveLine,
                    true,
                    CommitFraction(input, 0.40f),
                    false,
                    "main-effort-under-pressure");
            if (input.AssaultAuthorized && input.MainEffortOdds >= 1.55f && input.ExploitOpportunity01 >= 0.70f)
                return new OperationalReserveDecision(
                    OperationalReserveMission.ExploitReserve,
                    true,
                    CommitFraction(input, 0.50f),
                    false,
                    "exploit-ready");
            if (input.AssaultAuthorized && input.MainEffortOdds >= 1.60f && input.ReserveFraction >= 0.18f)
                return new OperationalReserveDecision(
                    OperationalReserveMission.Counterattack,
                    true,
                    CommitFraction(input, 0.50f),
                    false,
                    "counterattack-ready");
            return new OperationalReserveDecision(OperationalReserveMission.Rally, false, "hold-rally");
        }

        private static float CommitFraction(OperationalReserveInput input, float desiredFractionOfReserve)
        {
            float available = Math.Max(0f, input.ReserveFraction - input.MinimumHeldFraction);
            return Math.Min(available, Math.Max(0.05f, input.ReserveFraction * desiredFractionOfReserve));
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

    public readonly struct TacticalEnduranceInput
    {
        public TacticalEnduranceInput(
            float infantryAmmo01,
            float artilleryAmmo01,
            float fatigue01,
            float morale01,
            float casualtyPressure01)
        {
            InfantryAmmo01 = Clamp01(infantryAmmo01);
            ArtilleryAmmo01 = Clamp01(artilleryAmmo01);
            Fatigue01 = Clamp01(fatigue01);
            Morale01 = Clamp01(morale01);
            CasualtyPressure01 = Clamp01(casualtyPressure01);
        }

        public float InfantryAmmo01 { get; }
        public float ArtilleryAmmo01 { get; }
        public float Fatigue01 { get; }
        public float Morale01 { get; }
        public float CasualtyPressure01 { get; }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }

    public readonly struct TacticalEnduranceDecision
    {
        public TacticalEnduranceDecision(
            bool canAssault,
            bool canHold,
            bool canFallback,
            bool needsRelief,
            string reason)
        {
            CanAssault = canAssault;
            CanHold = canHold;
            CanFallback = canFallback;
            NeedsRelief = needsRelief;
            Reason = string.IsNullOrWhiteSpace(reason) ? "endurance-unspecified" : reason.Trim();
        }

        public bool CanAssault { get; }
        public bool CanHold { get; }
        public bool CanFallback { get; }
        public bool NeedsRelief { get; }
        public string Reason { get; }
    }

    public static class TacticalEnduranceGate
    {
        public static TacticalEnduranceDecision Evaluate(TacticalEnduranceInput input)
        {
            bool lowAmmo = input.InfantryAmmo01 < 0.20f;
            bool exhausted = input.Fatigue01 >= 0.80f;
            bool shaken = input.Morale01 < 0.45f;
            bool battered = input.CasualtyPressure01 >= 0.60f;
            bool needsRelief = lowAmmo || exhausted || shaken || battered;
            bool canAssault = !lowAmmo && !exhausted && input.Morale01 >= 0.55f && input.CasualtyPressure01 < 0.55f;
            bool canHold = input.Morale01 >= 0.25f && input.CasualtyPressure01 < 0.85f;
            return new TacticalEnduranceDecision(
                canAssault,
                canHold,
                true,
                needsRelief,
                needsRelief ? "relief-needed" : "endurance-ready");
        }
    }

    public enum TacticalSupportRequestType
    {
        LineRelief = 0,
        ArtillerySupport = 1,
        CavalryScreen = 2,
        ReserveCounterattack = 3
    }

    public readonly struct TacticalSupportRequest
    {
        public TacticalSupportRequest(
            string sourceNodeId,
            TacticalSupportRequestType type,
            string objectiveId,
            float priority01,
            float issuedAtSeconds,
            float expiresAtSeconds)
        {
            SourceNodeId = string.IsNullOrWhiteSpace(sourceNodeId) ? "node-unknown" : sourceNodeId.Trim();
            Type = type;
            ObjectiveId = string.IsNullOrWhiteSpace(objectiveId) ? "objective-unknown" : objectiveId.Trim();
            Priority01 = Clamp01(priority01);
            IssuedAtSeconds = SanitizeNonNegative(issuedAtSeconds);
            ExpiresAtSeconds = Math.Max(IssuedAtSeconds, SanitizeNonNegative(expiresAtSeconds));
        }

        public string SourceNodeId { get; }
        public TacticalSupportRequestType Type { get; }
        public string ObjectiveId { get; }
        public float Priority01 { get; }
        public float IssuedAtSeconds { get; }
        public float ExpiresAtSeconds { get; }

        public string Key => SourceNodeId + "|" + Type + "|" + ObjectiveId;

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
    }

    public sealed class TacticalSupportRequestLedger
    {
        private readonly Dictionary<string, TacticalSupportRequest> _requests =
            new Dictionary<string, TacticalSupportRequest>(StringComparer.Ordinal);

        public int Count { get { return _requests.Count; } }

        public void AddOrUpdate(TacticalSupportRequest request)
        {
            if (_requests.TryGetValue(request.Key, out TacticalSupportRequest existing) &&
                existing.Priority01 > request.Priority01)
            {
                return;
            }

            _requests[request.Key] = request;
        }

        public TacticalSupportRequest PeekHighestPriority(float nowSeconds)
        {
            TacticalSupportRequest best = default(TacticalSupportRequest);
            bool found = false;
            foreach (TacticalSupportRequest request in _requests.Values)
            {
                if (request.ExpiresAtSeconds < nowSeconds) continue;
                if (!found || request.Priority01 > best.Priority01 ||
                    (Math.Abs(request.Priority01 - best.Priority01) < 0.001f &&
                     request.IssuedAtSeconds < best.IssuedAtSeconds))
                {
                    best = request;
                    found = true;
                }
            }

            return found
                ? best
                : new TacticalSupportRequest("node-unknown", TacticalSupportRequestType.LineRelief, "objective-unknown", 0f, 0f, 0f);
        }
    }

    public enum PlayerSubordinateOrderIntent
    {
        None = 0,
        Hold = 1,
        Move = 2,
        Attack = 3,
        Support = 4,
        Fallback = 5,
        Screen = 6
    }

    public readonly struct PlayerSubordinateOrderInput
    {
        public PlayerSubordinateOrderInput(
            bool wlScenarioActive,
            bool playerIsCommander,
            bool playerUnderCommander,
            bool existingOrderFresh,
            CommandDoctrineOrder order)
        {
            WlScenarioActive = wlScenarioActive;
            PlayerIsCommander = playerIsCommander;
            PlayerUnderCommander = playerUnderCommander;
            ExistingOrderFresh = existingOrderFresh;
            Order = order;
        }

        public bool WlScenarioActive { get; }
        public bool PlayerIsCommander { get; }
        public bool PlayerUnderCommander { get; }
        public bool ExistingOrderFresh { get; }
        public CommandDoctrineOrder Order { get; }
    }

    public readonly struct PlayerSubordinateOrderDecision
    {
        public PlayerSubordinateOrderDecision(
            PlayerSubordinateOrderIntent intent,
            bool shouldIssueWlOrder,
            bool allowsDirectMovementWrite,
            string reason)
        {
            Intent = intent;
            ShouldIssueWlOrder = shouldIssueWlOrder;
            AllowsDirectMovementWrite = allowsDirectMovementWrite;
            Reason = string.IsNullOrWhiteSpace(reason) ? "player-order-unspecified" : reason.Trim();
        }

        public PlayerSubordinateOrderIntent Intent { get; }
        public bool ShouldIssueWlOrder { get; }
        public bool AllowsDirectMovementWrite { get; }
        public string Reason { get; }
    }

    public static class PlayerSubordinateOrderDoctrine
    {
        public static PlayerSubordinateOrderDecision Decide(PlayerSubordinateOrderInput input)
        {
            if (!input.WlScenarioActive)
                return NoOrder("wl-inactive");
            if (input.PlayerIsCommander || !input.PlayerUnderCommander)
                return NoOrder("not-subordinate");
            if (input.ExistingOrderFresh)
                return NoOrder("existing-order-fresh");

            PlayerSubordinateOrderIntent intent = IntentFor(input.Order.Task);
            if (intent == PlayerSubordinateOrderIntent.None)
                return NoOrder("no-mapped-intent");

            return new PlayerSubordinateOrderDecision(intent, true, false, "wl-order");
        }

        private static PlayerSubordinateOrderDecision NoOrder(string reason)
        {
            return new PlayerSubordinateOrderDecision(
                PlayerSubordinateOrderIntent.None,
                false,
                false,
                reason);
        }

        private static PlayerSubordinateOrderIntent IntentFor(CommandTaskType task)
        {
            switch (task)
            {
                case CommandTaskType.AttackObjective:
                    return PlayerSubordinateOrderIntent.Attack;
                case CommandTaskType.SupportAttack:
                case CommandTaskType.FixEnemy:
                case CommandTaskType.ReleaseReserve:
                    return PlayerSubordinateOrderIntent.Support;
                case CommandTaskType.FallBackToLine:
                case CommandTaskType.Delay:
                    return PlayerSubordinateOrderIntent.Fallback;
                case CommandTaskType.Screen:
                case CommandTaskType.Probe:
                case CommandTaskType.Scout:
                    return PlayerSubordinateOrderIntent.Screen;
                case CommandTaskType.FormUp:
                case CommandTaskType.AdvanceToAssembly:
                    return PlayerSubordinateOrderIntent.Move;
                case CommandTaskType.HoldObjective:
                case CommandTaskType.HoldChoke:
                case CommandTaskType.ReserveWait:
                    return PlayerSubordinateOrderIntent.Hold;
                default:
                    return PlayerSubordinateOrderIntent.None;
            }
        }
    }
}
