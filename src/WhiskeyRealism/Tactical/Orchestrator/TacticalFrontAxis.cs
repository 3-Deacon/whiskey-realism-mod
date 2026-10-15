using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pure battle-front geometry for one side. Forward points from the own
    /// centroid to the target (visible-enemy centroid, or an objective when no
    /// enemy is in sight); the lateral axis is forward rotated 90 degrees
    /// clockwise in the XZ plane — the same handedness as
    /// <see cref="Plays.PlayProjection"/> (<c>right = (forwardZ, -forwardX)</c>).
    /// A negative lateral offset is the army's LEFT, so lower sector ids are
    /// to the left: <see cref="DirectChildAllocator"/> (<c>PrimarySector &lt;
    /// mainSector</c> = left), the role cascade (index 0 = RefuseLeft) and the
    /// play rects all read the same picture.
    ///
    /// Sectors are equal-width lateral bands over the own army's lateral extent
    /// (units beyond it clamp into the edge bands), so a sector is a piece of
    /// ground, not a position in <c>BattleUnits.completeunitlist</c>, and a
    /// rout elsewhere on the field does not renumber it.
    /// </summary>
    public readonly struct TacticalFrontAxis
    {
        public const int DefaultBandCount = 5;

        /// <summary>Heading wobble (degrees) ignored by <see cref="Stabilize"/>.</summary>
        public const float DefaultMinHeadingChangeDegrees = 10f;

        /// <summary>Narrowest frontage the bands are spread over (world units).</summary>
        public const float MinBandFrontage = 100f;

        /// <summary>
        /// Enemy extent must pass ours by this share of our frontage before the
        /// flank counts as overlapped (keeps a ragged line from reading as a
        /// turned flank).
        /// </summary>
        public const float EnemyOverlapMarginFraction = 0.05f;

        private const float DegenerateDistance = 1f;

        private TacticalFrontAxis(
            float originX, float originZ,
            float forwardX, float forwardZ,
            float lateralMin, float lateralMax, int bandCount,
            bool hasEnemyExtent, float enemyLateralMin, float enemyLateralMax,
            float targetDistance)
        {
            IsValid = true;
            TargetDistance = targetDistance;
            OriginX = originX;
            OriginZ = originZ;
            ForwardX = forwardX;
            ForwardZ = forwardZ;
            LateralMin = lateralMin;
            LateralMax = lateralMax;
            BandCount = bandCount;
            HasEnemyExtent = hasEnemyExtent;
            EnemyLateralMin = enemyLateralMin;
            EnemyLateralMax = enemyLateralMax;
        }

        public static TacticalFrontAxis None => default(TacticalFrontAxis);

        public bool IsValid { get; }

        /// <summary>Distance from the own centroid to the target the axis was
        /// built toward (0 when degenerate). PlayProjection scales rect depth by it.</summary>
        public float TargetDistance { get; }
        public float OriginX { get; }
        public float OriginZ { get; }
        public float ForwardX { get; }
        public float ForwardZ { get; }
        public float RightX => ForwardZ;
        public float RightZ => -ForwardX;
        public float LateralMin { get; }
        public float LateralMax { get; }
        public int BandCount { get; }
        public bool HasEnemyExtent { get; }
        public float EnemyLateralMin { get; }
        public float EnemyLateralMax { get; }

        public int CenterSector => BandCount <= 0 ? 0 : BandCount / 2;

        /// <summary>Compass-style heading of forward, degrees in [0, 360).</summary>
        public float HeadingDegrees
        {
            get
            {
                double deg = Math.Atan2(ForwardX, ForwardZ) * 180.0 / Math.PI;
                if (deg < 0) deg += 360.0;
                return (float)deg;
            }
        }

        public int HeadingBucket(int degreesPerBucket)
        {
            if (!IsValid || degreesPerBucket <= 0) return -1;
            return (int)(HeadingDegrees / degreesPerBucket);
        }

        public bool EnemyOverlapsLeft
            => IsValid && HasEnemyExtent && EnemyLateralMin < LateralMin - OverlapMargin;

        public bool EnemyOverlapsRight
            => IsValid && HasEnemyExtent && EnemyLateralMax > LateralMax + OverlapMargin;

        private float OverlapMargin => Math.Max(0f, LateralMax - LateralMin) * EnemyOverlapMarginFraction;

        /// <summary>
        /// Axis from own centroid toward the target. A target within 1 unit of
        /// the centroid (no enemy, no objective) falls back to forward = +Z, as
        /// PlayProjection does.
        /// </summary>
        public static TacticalFrontAxis FromCentroids(float ownX, float ownZ, float targetX, float targetZ)
        {
            if (!Finite(ownX) || !Finite(ownZ)) return None;
            float dx = Finite(targetX) ? targetX - ownX : 0f;
            float dz = Finite(targetZ) ? targetZ - ownZ : 0f;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            float fx = 0f, fz = 1f;
            if (dist >= DegenerateDistance)
            {
                fx = dx / dist;
                fz = dz / dist;
            }
            return new TacticalFrontAxis(ownX, ownZ, fx, fz, 0f, 0f, 0, false, 0f, 0f,
                dist >= DegenerateDistance ? dist : 0f);
        }

        /// <summary>
        /// Spreads <paramref name="bandCount"/> lateral bands over the own
        /// army's lateral extent (padded to <see cref="MinBandFrontage"/>).
        /// </summary>
        public TacticalFrontAxis WithOwnExtent(float lateralMin, float lateralMax, int bandCount = DefaultBandCount)
        {
            if (!IsValid) return this;
            if (!Finite(lateralMin) || !Finite(lateralMax)) { lateralMin = 0f; lateralMax = 0f; }
            if (lateralMax < lateralMin) { float t = lateralMin; lateralMin = lateralMax; lateralMax = t; }
            float width = lateralMax - lateralMin;
            if (width < MinBandFrontage)
            {
                float mid = (lateralMin + lateralMax) * 0.5f;
                lateralMin = mid - MinBandFrontage * 0.5f;
                lateralMax = mid + MinBandFrontage * 0.5f;
            }
            if (bandCount < 1) bandCount = 1;
            return new TacticalFrontAxis(OriginX, OriginZ, ForwardX, ForwardZ,
                lateralMin, lateralMax, bandCount, HasEnemyExtent, EnemyLateralMin, EnemyLateralMax, TargetDistance);
        }

        public TacticalFrontAxis WithEnemyExtent(float enemyLateralMin, float enemyLateralMax)
        {
            if (!IsValid || !Finite(enemyLateralMin) || !Finite(enemyLateralMax)) return this;
            if (enemyLateralMax < enemyLateralMin) { float t = enemyLateralMin; enemyLateralMin = enemyLateralMax; enemyLateralMax = t; }
            return new TacticalFrontAxis(OriginX, OriginZ, ForwardX, ForwardZ,
                LateralMin, LateralMax, BandCount, true, enemyLateralMin, enemyLateralMax, TargetDistance);
        }

        public float LateralOf(float x, float z)
            => (x - OriginX) * RightX + (z - OriginZ) * RightZ;

        public int SectorOf(float x, float z)
        {
            if (!IsValid) return 0;
            return SectorOfLateral(LateralOf(x, z));
        }

        public int SectorOfLateral(float lateral)
        {
            if (!IsValid || BandCount <= 1) return 0;
            float width = LateralMax - LateralMin;
            if (width <= 0f || !Finite(lateral)) return CenterSector;
            int band = (int)Math.Floor((lateral - LateralMin) / width * BandCount);
            if (band < 0) return 0;
            if (band >= BandCount) return BandCount - 1;
            return band;
        }

        /// <summary>
        /// Keeps the previous heading when the new one differs by less than
        /// <paramref name="minHeadingChangeDegrees"/>; the origin always follows
        /// the army. Prevents centroid jitter from re-banding units every tick.
        /// Call on the bare axis, before <see cref="WithOwnExtent"/>.
        /// </summary>
        public static TacticalFrontAxis Stabilize(TacticalFrontAxis previous, TacticalFrontAxis next, float minHeadingChangeDegrees)
        {
            if (!next.IsValid) return previous;
            if (!previous.IsValid) return next;
            float dot = previous.ForwardX * next.ForwardX + previous.ForwardZ * next.ForwardZ;
            if (dot > 1f) dot = 1f;
            if (dot < -1f) dot = -1f;
            double angle = Math.Acos(dot) * 180.0 / Math.PI;
            if (angle >= minHeadingChangeDegrees) return next;
            return new TacticalFrontAxis(next.OriginX, next.OriginZ, previous.ForwardX, previous.ForwardZ,
                next.LateralMin, next.LateralMax, next.BandCount,
                next.HasEnemyExtent, next.EnemyLateralMin, next.EnemyLateralMax, next.TargetDistance);
        }

        /// <summary>
        /// Flank-exposure bucket (0-3) consumed by
        /// <see cref="DirectChildAllocator"/> (refuse at 3) and the leaf
        /// cascade (cautious branch at 2). Inputs: the child holds an army
        /// wing; the visible enemy line extends past that wing; vanilla's own
        /// group <c>flanksthreated</c> (Regiment 110958 — the group sum of
        /// subordinate <c>outflanked</c> states, 120568-120580: +1 one flank,
        /// +2 both, +3 rear, +4 surrounded).
        /// </summary>
        public static int FlankExposureBucket(bool isWing, bool enemyOverlapsFlank, float vanillaFlanksThreated)
        {
            int bucket = 0;
            if (isWing) bucket++;
            if (isWing && enemyOverlapsFlank) bucket++;
            if (Finite(vanillaFlanksThreated) && vanillaFlanksThreated > 0f)
                bucket += vanillaFlanksThreated >= 2f ? 2 : 1;
            if (!isWing && bucket > 2) bucket = 2;
            return bucket > 3 ? 3 : bucket;
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
