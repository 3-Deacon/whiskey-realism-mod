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
            float aggressionBias01,
            TacticalIntentModel enemyIntent,
            WhiskeyRealism.Tactical.Orchestrator.Plays.ManeuverSpec maneuver = default(WhiskeyRealism.Tactical.Orchestrator.Plays.ManeuverSpec))
        {
            Maneuver = maneuver;
            ChildId = childId ?? string.Empty;
            RawUnitTyp = rawUnitTyp;
            EffectiveCommandLevel = effectiveCommandLevel;
            DisplayName = displayName ?? string.Empty;
            PrimarySector = primarySector;
            Role = role;
            Axis = axis;
            AxisSector = axisSector;
            SupportPriority01 = Clamp01(supportPriority01);
            AggressionBias01 = ClampOrHalf(aggressionBias01);
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
        public float AggressionBias01 { get; }
        public TacticalIntentModel EnemyIntent { get; }

        /// <summary>
        /// BC11 decision 4: the maneuver the child's zone assignment carries (an enveloping rect, or a wing rect with a
        /// start delay); HasValue is false for every other child. The roles stay as the rect kind maps them.
        /// </summary>
        public WhiskeyRealism.Tactical.Orchestrator.Plays.ManeuverSpec Maneuver { get; }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static float ClampOrHalf(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0.5f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
