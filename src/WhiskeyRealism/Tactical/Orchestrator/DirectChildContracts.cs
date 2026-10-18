using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    public enum DirectChildRole
    {
        Unknown = 0,
        Main,
        SupportMain,
        Fix,
        Screen,
        RefuseLeft,
        RefuseRight,
        Reserve,
        Fallback,
    }

    public enum DirectChildAxis
    {
        None = 0,
        SectorAxis,
        Withdraw,
        Hold,
    }

    /// <summary>
    /// Discovery-time snapshot of one army-direct-child command unit. Built by
    /// DirectChildDiscovery from vanilla AIBattle.unitsused + Regiment.GetAttachedUnitsReg.
    /// Pure: no Unity types.
    /// </summary>
    public readonly struct DirectChildSnapshot
    {
        public DirectChildSnapshot(
            string childId,
            string parentArmyId,
            int rawUnitTyp,
            int commandHierarchyShift,
            string displayName,
            bool active)
        {
            ChildId = childId ?? string.Empty;
            ParentArmyId = parentArmyId ?? string.Empty;
            RawUnitTyp = rawUnitTyp;
            CommandHierarchyShift = commandHierarchyShift;
            DisplayName = displayName ?? string.Empty;
            Active = active;
        }

        public string ChildId { get; }
        public string ParentArmyId { get; }
        public int RawUnitTyp { get; }
        public int CommandHierarchyShift { get; }
        public string DisplayName { get; }
        public bool Active { get; }

        public int EffectiveCommandLevel => RawUnitTyp - CommandHierarchyShift;
    }

    /// <summary>
    /// Bucketed evidence for one direct child. Allocator only re-runs when the
    /// signature changes. Mirrors the strategic FrontSectorRuntime.Signature
    /// 0.5-bucket pattern.
    /// </summary>
    public readonly struct DirectChildEvidence
    {
        public DirectChildEvidence(
            int ownStrengthBucket,
            int enemyStrengthBucket,
            bool contactFlag,
            int primarySector,
            int flankExposureBucket,
            float confidence01,
            bool deployedReserve = false)
        {
            OwnStrengthBucket = NonNeg(ownStrengthBucket);
            EnemyStrengthBucket = NonNeg(enemyStrengthBucket);
            ContactFlag = contactFlag;
            PrimarySector = primarySector;
            FlankExposureBucket = NonNeg(flankExposureBucket);
            Confidence01 = Clamp01(confidence01);
            DeployedReserve = deployedReserve;
        }

        public int OwnStrengthBucket { get; }
        public int EnemyStrengthBucket { get; }
        public bool ContactFlag { get; }
        public int PrimarySector { get; }
        public int FlankExposureBucket { get; }
        public float Confidence01 { get; }

        /// <summary>
        /// BC10: the executor placed this child as a deployment reserve and has issued no other
        /// Move to it since (DeploymentDoctrine.IsDeployedReserve). The allocator gives it the
        /// Reserve role ahead of the main-effort pick; the first Move any other source issues
        /// to its groups clears the flag, and ReleaseReserves releases it like any Reserve child.
        /// </summary>
        public bool DeployedReserve { get; }

        public bool SignatureEquals(DirectChildEvidence other)
        {
            return OwnStrengthBucket == other.OwnStrengthBucket
                && EnemyStrengthBucket == other.EnemyStrengthBucket
                && ContactFlag == other.ContactFlag
                && PrimarySector == other.PrimarySector
                && FlankExposureBucket == other.FlankExposureBucket
                && DeployedReserve == other.DeployedReserve;
        }

        private static int NonNeg(int v) => v < 0 ? 0 : v;

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }

    /// <summary>
    /// BC14 requirement-defender-crossing-release: where a reserve released to guard a crossing stands and which way it faces
    /// (the posture producer reads it as the GuardFlank target and heading). HasValue false is none.
    /// </summary>
    public readonly struct GuardPoint
    {
        public GuardPoint(float x, float z, float heading)
        {
            HasValue = true;
            X = x;
            Z = z;
            Heading = heading;
        }

        public bool HasValue { get; }
        public float X { get; }
        public float Z { get; }
        public float Heading { get; }
    }

    /// <summary>
    /// BC14O requirement-main-effort-objective: where a Screen-role child stands to screen a secondary objective (the objective itself when own-held,
    /// else the point short of it on the own side). HasValue false is none.
    /// </summary>
    public readonly struct ScreenTarget
    {
        public ScreenTarget(float x, float z, float heading = 0f)
        {
            HasValue = true;
            X = x;
            Z = z;
            Heading = heading;
        }

        public bool HasValue { get; }
        public float X { get; }
        public float Z { get; }
        /// <summary>The heading the child faces there, degrees: toward the enemy along the own front axis.</summary>
        public float Heading { get; }
    }

    /// <summary>
    /// Per-direct-child intent produced by DirectChildAllocator (ArmyOrchestrator.CurrentDirectChildIntents).
    /// Cascaded to consumers (#42 gate, future O4 division attach).
    /// </summary>
    public readonly struct DirectChildIntent
    {
        public DirectChildIntent(
            string childId,
            int rawUnitTyp,
            int effectiveCommandLevel,
            string displayName,
            int primarySector,
            DirectChildRole role,
            DirectChildAxis axis,
            int axisSector,
            float supportPriority01,
            TacticalIntentModel enemyIntent,
            WhiskeyRealism.Tactical.Orchestrator.Plays.ManeuverSpec maneuver = default(WhiskeyRealism.Tactical.Orchestrator.Plays.ManeuverSpec),
            WhiskeyRealism.Tactical.Orchestrator.Plays.PlayRectKind zoneRectKind = WhiskeyRealism.Tactical.Orchestrator.Plays.PlayRectKind.Unknown,
            GuardPoint guardPoint = default(GuardPoint),
            ScreenTarget screenTarget = default(ScreenTarget),
            bool releasedFromReserve = false)
        {
            ReleasedFromReserve = releasedFromReserve;
            Screen = screenTarget;
            Guard = guardPoint;
            Maneuver = maneuver;
            ZoneRectKind = zoneRectKind;
            ChildId = childId ?? string.Empty;
            RawUnitTyp = rawUnitTyp;
            EffectiveCommandLevel = effectiveCommandLevel;
            DisplayName = displayName ?? string.Empty;
            PrimarySector = primarySector;
            Role = role;
            Axis = axis;
            AxisSector = axisSector;
            SupportPriority01 = Clamp01(supportPriority01);
            EnemyIntent = enemyIntent;
        }

        public string ChildId { get; }
        public int RawUnitTyp { get; }
        public int EffectiveCommandLevel { get; }
        public string DisplayName { get; }
        public int PrimarySector { get; }
        public DirectChildRole Role { get; }
        public DirectChildAxis Axis { get; }
        public int AxisSector { get; }
        public float SupportPriority01 { get; }
        public TacticalIntentModel EnemyIntent { get; }

        /// <summary>
        /// The allocation's own release: the base allocation held the child in Reserve and a release (crisis, stagger or odds, flank, crossing guard,
        /// after the release hysteresis) took it out. A child whose base role was already a committed one (the evidence tiers gave it Main, SupportMain
        /// or a refuse role) is not released from anything and reads false: the reserve-release producer moves only a released child.
        /// </summary>
        public bool ReleasedFromReserve { get; }

        /// <summary>
        /// BC11 decision 4: the maneuver the child's zone assignment carries (an enveloping rect, or a wing rect with a
        /// start delay); HasValue is false for every other child. The roles stay as the rect kind maps them.
        /// </summary>
        public WhiskeyRealism.Tactical.Orchestrator.Plays.ManeuverSpec Maneuver { get; }

        /// <summary>
        /// BC11: the play rect this child's role came from (Unknown when the role came from evidence, or was kept off the zone by
        /// BC10's deployed-reserve rule). The envelopment wings wait only on a Center child whose role really is its zone's.
        /// </summary>
        public WhiskeyRealism.Tactical.Orchestrator.Plays.PlayRectKind ZoneRectKind { get; }

        /// <summary>BC14: the guard point of a reserve released to a threatened crossing (none for every other child).</summary>
        public GuardPoint Guard { get; }

        public bool HasGuardPoint => Guard.HasValue;
        public float GuardX => Guard.X;
        public float GuardZ => Guard.Z;
        public float GuardHeading => Guard.Heading;

        /// <summary>BC14O: the secondary objective this Screen-role child screens (none for every other child).</summary>
        public ScreenTarget Screen { get; }

        public bool HasScreenTarget => Screen.HasValue;
        public float ScreenX => Screen.X;
        public float ScreenZ => Screen.Z;
        public float ScreenHeading => Screen.Heading;

        /// <summary>The same intent screening a secondary objective at (<paramref name="x"/>, <paramref name="z"/>); every other field is carried.</summary>
        public DirectChildIntent WithScreenTarget(float x, float z, float heading = 0f)
            => new DirectChildIntent(ChildId, RawUnitTyp, EffectiveCommandLevel, DisplayName, PrimarySector, Role, Axis, AxisSector, SupportPriority01, EnemyIntent,
                Maneuver, ZoneRectKind, Guard, new ScreenTarget(x, z, heading), ReleasedFromReserve);

        /// <summary>The same intent with its release mark set to <paramref name="released"/>; every other field is carried.</summary>
        public DirectChildIntent WithReleasedFromReserve(bool released)
            => new DirectChildIntent(ChildId, RawUnitTyp, EffectiveCommandLevel, DisplayName, PrimarySector, Role, Axis, AxisSector, SupportPriority01, EnemyIntent,
                Maneuver, ZoneRectKind, Guard, Screen, released);

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
