using System;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tactical.Battle
{
    // BC13 (spec docs/superpowers/specs/2026-10-15-battle-opportunity-design.md § 5, § 8): the types of the
    // fog-honest opening detector. Pure: no Unity or vanilla types.

    /// <summary>What an opening is.</summary>
    public enum OpeningKind : byte
    {
        Gap = 0,
        ShakenSector = 1,
        FlankInAir = 2,
        UnsupportedBattery = 3,
        IsolatedCluster = 4,
    }

    /// <summary>
    /// One detected opening. Positions are world X, Z; times are doctrine hours. Claimed is derived by the
    /// opportunity producer each tick from the live exploit entries, never stored by the detector's own logic.
    /// </summary>
    public struct Opening
    {
        public int Id;
        public OpeningKind Kind;
        public float AimX, AimZ;
        /// <summary>Compass degrees. A Gap's is the front axis's forward bearing, a FlankInAir's points along the line
        /// toward its centre; every other kind's is set at launch (the bearing from the asset to the aim).</summary>
        public float Heading;
        public float Width;
        /// <summary>Sum over the enemy samples within OpportunityDoctrine.OddsRadiusMeters of the aim: a Visible sample
        /// counts strength x morale, a Remembered one strength x confidence x its last morale.</summary>
        public float LocalEnemyValue;
        public float Confidence01;
        public int Sector;
        /// <summary>The target's instance id (a battery, the nearest shaken sample, a cluster's strongest sample, the
        /// penetrating sample); -1 for a Gap and a FlankInAir.</summary>
        public int TargetObservation;
        public double FirstSeenHours, LastSeenHours;
        public bool Confirmed, Claimed;
    }

    /// <summary>
    /// Whether the side perceives a point of ground (requirement-perceived-ground). The runtime implements it with the
    /// vanilla fog grid (or the range-only reach); an opening that asserts the ABSENCE of enemy is emitted only when
    /// every probe point of the ground it asserts is perceived.
    /// </summary>
    public interface IPerceivedGround
    {
        bool IsPerceived(float x, float z);
    }

    /// <summary>The public terrain the anchor and recovery tests read (BC10 grid, BC12 picture).</summary>
    public interface IOpeningTerrain
    {
        /// <summary>A river cell (BC10 id 4, the map not frozen) within <paramref name="meters"/> from the point along the direction.</summary>
        bool RiverWithin(float x, float z, float dirX, float dirZ, float meters);

        /// <summary>A terrain-picture crest cell within <paramref name="meters"/> of the point (false when the picture is not ready).</summary>
        bool CrestWithin(float x, float z, float meters);

        /// <summary>The straight segment crosses a river cell that carries no units. A ford or a bridge passes; a frozen map passes.</summary>
        bool SegmentCrossesRiver(float x0, float z0, float x1, float z1);
    }

    /// <summary>Game values the detector reads, taken once per battle (GamePrefs) so the detector stays pure.</summary>
    public struct OpeningRules
    {
        /// <summary>GamePrefs.maxenemymoraleforcavalrychargenonarty (shipped 0.45): vanilla's own cavalry-target bar.</summary>
        public float ShakenMorale;
        /// <summary>GamePrefs.distancetocheckforanchoredflanksriver (shipped 80 m).</summary>
        public float AnchorRiverMeters;
        /// <summary>GamePrefs.distancetocheckforanchoredflankhills (shipped 50 m).</summary>
        public float AnchorHillMeters;
        /// <summary>The BC12 terrain picture is ready; when it is not, the crest test is skipped.</summary>
        public bool PictureReady;
        /// <summary>Enable Defender Counterstroke: a Defender side detects Penetration.</summary>
        public bool CounterstrokeOn;

        public static OpeningRules Shipped => new OpeningRules
        {
            ShakenMorale = 0.45f, AnchorRiverMeters = 80f, AnchorHillMeters = 50f, PictureReady = true, CounterstrokeOn = true,
        };
    }

    /// <summary>
    /// A rectangle of ground in the front axis's frame (lateral x depth) and the lattice of probe points over it.
    /// Points are spaced at most <see cref="OpeningDetector.ProbeSpacingMeters"/> apart and include both edges.
    /// </summary>
    public struct ProbeBox
    {
        public const int MaxPointsPerAxis = 64;

        public float OriginX, OriginZ, RightX, RightZ, ForwardX, ForwardZ;
        public float LateralMin, LateralMax, DepthMin, DepthMax;
        public int LateralPoints, DepthPoints;

        public int PointCount => LateralPoints * DepthPoints;

        public static ProbeBox Make(in TacticalFrontAxis axis, float lateralA, float lateralB, float depthA, float depthB, float spacing)
        {
            var box = new ProbeBox
            {
                OriginX = axis.OriginX, OriginZ = axis.OriginZ,
                RightX = axis.RightX, RightZ = axis.RightZ,
                ForwardX = axis.ForwardX, ForwardZ = axis.ForwardZ,
                LateralMin = Math.Min(lateralA, lateralB), LateralMax = Math.Max(lateralA, lateralB),
                DepthMin = Math.Min(depthA, depthB), DepthMax = Math.Max(depthA, depthB),
            };
            box.LateralPoints = PointsFor(box.LateralMax - box.LateralMin, spacing);
            box.DepthPoints = PointsFor(box.DepthMax - box.DepthMin, spacing);
            return box;
        }

        private static int PointsFor(float span, float spacing)
        {
            if (!(span > 0f) || !(spacing > 0f)) return 1;
            int n = (int)Math.Ceiling(span / spacing) + 1;
            return n > MaxPointsPerAxis ? MaxPointsPerAxis : n;
        }

        /// <summary>World position of the k-th lattice point, k in [0, PointCount): lateral-major.</summary>
        public void PointAt(int k, out float x, out float z)
        {
            int li = k / DepthPoints;
            int di = k - li * DepthPoints;
            float lateral = LateralPoints > 1 ? LateralMin + li * ((LateralMax - LateralMin) / (LateralPoints - 1)) : LateralMin;
            float depth = DepthPoints > 1 ? DepthMin + di * ((DepthMax - DepthMin) / (DepthPoints - 1)) : DepthMin;
            x = OriginX + lateral * RightX + depth * ForwardX;
            z = OriginZ + lateral * RightZ + depth * ForwardZ;
        }
    }
}
