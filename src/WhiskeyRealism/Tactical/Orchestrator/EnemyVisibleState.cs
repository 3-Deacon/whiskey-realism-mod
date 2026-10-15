using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Per-sector own-vs-enemy visible strength plus recent-fire flag. A sector
    /// is a lateral band of the front (<see cref="TacticalFrontAxis"/>).
    /// Built by ArmyEvidenceBuilder and consumed by ArmyIntentInference.Build.
    /// </summary>
    public readonly struct EnemyVisibleSector
    {
        /// <summary>
        /// Largest odds the evidence will report, here and army-wide
        /// (ArmyEvidenceBuilder.KnownEnemyOdds). Beyond 10:1 the exact figure
        /// changes no decision, and a 50-man screen in front of a corps no
        /// longer reads as 100:1.
        /// </summary>
        public const float MaxKnowableOdds = 10f;

        public EnemyVisibleSector(int sectorId, float ownStrength, float enemyStrength, bool recentFire)
        {
            SectorId = sectorId;
            OwnStrength = SanitizeStrength(ownStrength);
            EnemyStrength = SanitizeStrength(enemyStrength);
            RecentFire = recentFire;
        }

        public int SectorId { get; }
        public float OwnStrength { get; }
        public float EnemyStrength { get; }
        public bool RecentFire { get; }

        public bool HasEnemy => EnemyStrength > 0f;

        /// <summary>Raw band odds, capped at <see cref="MaxKnowableOdds"/>; 0 with no enemy in the band.</summary>
        public float Odds => !HasEnemy ? 0f : Math.Min(MaxKnowableOdds, OwnStrength / Math.Max(1f, EnemyStrength));

        private static float SanitizeStrength(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value < 0f ? 0f : value;
        }
    }

    /// <summary>
    /// One unit's position and strength as the side sees it. Own units come
    /// from the side's battle snapshot roster; enemy units only from its
    /// visible and remembered observations (BC2a: vanilla's AI-side fog grid,
    /// UnitIsHidden 101179 with the arguments of 127225), never from vanilla's
    /// range-only sight lists, which skip the fog test for player-alliance
    /// targets (122650/122699).
    /// </summary>
    public readonly struct FrontUnitSample
    {
        public FrontUnitSample(int instanceId, float x, float z, float strength, bool recentFire)
        {
            InstanceId = instanceId;
            X = x;
            Z = z;
            Strength = float.IsNaN(strength) || float.IsInfinity(strength) || strength < 0f ? 0f : strength;
            RecentFire = recentFire;
        }

        public int InstanceId { get; }
        public float X { get; }
        public float Z { get; }
        public float Strength { get; }
        public bool RecentFire { get; }
    }

    /// <summary>
    /// Pure band aggregation for <see cref="EnemyVisibleState"/>.
    /// </summary>
    public static class EnemyVisibleSectorBuilder
    {
        /// <summary>
        /// Fits lateral bands over the own units' extent and records the visible
        /// enemy extent (for flank overlap).
        /// </summary>
        public static TacticalFrontAxis FitAxis(
            TacticalFrontAxis bare,
            IReadOnlyList<FrontUnitSample> ownUnits,
            IReadOnlyList<FrontUnitSample> enemyUnits,
            int bandCount = TacticalFrontAxis.DefaultBandCount)
        {
            if (!bare.IsValid) return bare;
            float min = 0f, max = 0f;
            bool any = Extent(bare, ownUnits, ref min, ref max);
            var axis = any ? bare.WithOwnExtent(min, max, bandCount) : bare.WithOwnExtent(0f, 0f, bandCount);
            float emin = 0f, emax = 0f;
            if (Extent(bare, enemyUnits, ref emin, ref emax))
                axis = axis.WithEnemyExtent(emin, emax);
            return axis;
        }

        /// <summary>
        /// One sector per populated band. Own strength is summed over own
        /// combat units in the band; enemy strength over visible enemies,
        /// each enemy counted once even when several own regiments see it
        /// (vanilla's sight lists overlap heavily).
        /// </summary>
        public static EnemyVisibleSector[] BuildSectors(
            TacticalFrontAxis axis,
            IReadOnlyList<FrontUnitSample> ownUnits,
            IReadOnlyList<FrontUnitSample> enemyUnits)
        {
            int bands = axis.IsValid && axis.BandCount > 0 ? axis.BandCount : 1;
            var own = new float[bands];
            var enemy = new float[bands];
            var fire = new bool[bands];
            var populated = new bool[bands];

            if (ownUnits != null)
            {
                for (int i = 0; i < ownUnits.Count; i++)
                {
                    var u = ownUnits[i];
                    int b = axis.SectorOf(u.X, u.Z);
                    own[b] += u.Strength;
                    if (u.RecentFire) fire[b] = true;
                    populated[b] = true;
                }
            }

            if (enemyUnits != null)
            {
                var seen = new HashSet<int>();
                for (int i = 0; i < enemyUnits.Count; i++)
                {
                    var u = enemyUnits[i];
                    if (u.InstanceId != 0 && !seen.Add(u.InstanceId)) continue;
                    int b = axis.SectorOf(u.X, u.Z);
                    enemy[b] += u.Strength;
                    populated[b] = true;
                }
            }

            var result = new List<EnemyVisibleSector>(bands);
            for (int b = 0; b < bands; b++)
            {
                if (!populated[b]) continue;
                result.Add(new EnemyVisibleSector(b, own[b], enemy[b], fire[b]));
            }
            return result.ToArray();
        }

        /// <summary>
        /// Coverage bands for intel confidence: <paramref name="bandCount"/>
        /// bands across the combined lateral extent of own units and known
        /// (visible or remembered) enemy positions, marked observed where an
        /// own combat unit stands. Bands over enemy-held ground with no own
        /// observer stay unobserved. Separate from the sector bands, which stay
        /// fitted over the own extent for role allocation. Each band's lateral
        /// span is written to <paramref name="bandLateralMin"/> /
        /// <paramref name="bandLateralMax"/> in a frame anchored at the world
        /// origin (lateral of (0, 0) is 0), so the spans stay comparable across
        /// builds while the heading holds. Returns the number of bands written
        /// (0 when there is nothing to fit).
        /// </summary>
        public static int CoverageBands(
            TacticalFrontAxis bare,
            IReadOnlyList<FrontUnitSample> ownUnits,
            IReadOnlyList<FrontUnitSample> knownEnemyUnits,
            bool[] observed,
            float[] bandLateralMin,
            float[] bandLateralMax,
            int bandCount = TacticalFrontAxis.DefaultBandCount)
        {
            if (observed == null || bandCount <= 0 || observed.Length < bandCount) return 0;
            if (bandLateralMin == null || bandLateralMax == null || bandLateralMin.Length < bandCount || bandLateralMax.Length < bandCount) return 0;
            Array.Clear(observed, 0, bandCount);
            if (!bare.IsValid || ownUnits == null || ownUnits.Count == 0) return 0;
            float min = 0f, max = 0f;
            if (!Extent(bare, ownUnits, ref min, ref max)) return 0;
            float emin = 0f, emax = 0f;
            if (Extent(bare, knownEnemyUnits, ref emin, ref emax))
            {
                if (emin < min) min = emin;
                if (emax > max) max = emax;
            }
            var axis = bare.WithOwnExtent(min, max, bandCount);
            for (int i = 0; i < ownUnits.Count; i++)
            {
                int b = axis.SectorOf(ownUnits[i].X, ownUnits[i].Z);
                if (b >= 0 && b < bandCount) observed[b] = true;
            }
            float worldOffset = bare.LateralOf(0f, 0f);
            float width = (max - min) / bandCount;
            for (int b = 0; b < bandCount; b++)
            {
                bandLateralMin[b] = min + b * width - worldOffset;
                bandLateralMax[b] = min + (b + 1) * width - worldOffset;
            }
            return bandCount;
        }

        /// <summary>Instance id → band for own command groups.</summary>
        public static Dictionary<int, int> MapInstances(TacticalFrontAxis axis, IReadOnlyList<FrontUnitSample> groups)
        {
            var map = new Dictionary<int, int>();
            if (groups == null) return map;
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                if (g.InstanceId == 0) continue;
                map[g.InstanceId] = axis.SectorOf(g.X, g.Z);
            }
            return map;
        }

        private static bool Extent(TacticalFrontAxis axis, IReadOnlyList<FrontUnitSample> units, ref float min, ref float max)
        {
            if (units == null || units.Count == 0) return false;
            bool any = false;
            for (int i = 0; i < units.Count; i++)
            {
                float lat = axis.LateralOf(units[i].X, units[i].Z);
                if (float.IsNaN(lat) || float.IsInfinity(lat)) continue;
                if (!any) { min = lat; max = lat; any = true; continue; }
                if (lat < min) min = lat;
                if (lat > max) max = lat;
            }
            return any;
        }
    }

    /// <summary>
    /// Perceived enemy state bundle: enemy figures come only from the side's
    /// battle-snapshot observations (fog-honest, BC2a/BC2b).
    /// Built by ArmyEvidenceBuilder and
    /// consumed by ArmyIntentInference.Build. Carries the front geometry the
    /// sectors were built on so every consumer (allocator, cascade, coordinator
    /// sector map) bands units the same way, including on the heavy-gated path
    /// where the state is reused from the cached snapshot.
    /// </summary>
    public readonly struct EnemyVisibleState
    {
        private static readonly IReadOnlyDictionary<int, int> EmptySectors = new Dictionary<int, int>();
        private static readonly IReadOnlyDictionary<int, float> EmptyThreat = new Dictionary<int, float>();

        public EnemyVisibleState(
            EnemyVisibleSector[] sectors,
            float enemyReserveCommitFraction,
            bool anyContactSpotted,
            bool anyContactBroken,
            TacticalFrontAxis frontAxis = default(TacticalFrontAxis),
            IReadOnlyDictionary<int, int> instanceSectors = null,
            IReadOnlyDictionary<int, float> instanceFlankThreat = null,
            float intelConfidence01 = 1f)
        {
            IntelConfidence01 = Clamp01(intelConfidence01);
            Sectors = CopySectors(sectors);
            EnemyReserveCommitFraction = Clamp01(enemyReserveCommitFraction);
            AnyContactSpotted = anyContactSpotted;
            AnyContactBroken = anyContactBroken;
            FrontAxis = frontAxis;
            InstanceSectors = CopyMap(instanceSectors);
            InstanceFlankThreat = CopyThreat(instanceFlankThreat);
        }

        public EnemyVisibleSector[] Sectors { get; }
        public float EnemyReserveCommitFraction { get; }
        public bool AnyContactSpotted { get; }
        public bool AnyContactBroken { get; }
        public TacticalFrontAxis FrontAxis { get; }
        public IReadOnlyDictionary<int, int> InstanceSectors { get; }

        /// <summary>
        /// The contact ledger's confidence when this state was built
        /// (TacticalEnemyContactLedger.Confidence). Band odds blend toward
        /// unknown (1) by it, the same way army-wide odds do.
        /// </summary>
        public float IntelConfidence01 { get; }

        /// <summary>
        /// Band odds as the doctrine should read them: 1 + confidence × (raw − 1),
        /// monotone in confidence with no cliff; 0 when the band has no enemy.
        /// </summary>
        public float SectorOdds(EnemyVisibleSector sector)
            => !sector.HasEnemy ? 0f : 1f + IntelConfidence01 * (sector.Odds - 1f);

        /// <summary>Own command group instance id → mean outflanked weight of its regiments (0-4).</summary>
        public IReadOnlyDictionary<int, float> InstanceFlankThreat { get; }

        public float FlankThreatForInstance(int instanceId)
        {
            if (InstanceFlankThreat != null && InstanceFlankThreat.TryGetValue(instanceId, out float v)) return v;
            return 0f;
        }

        public int SectorForInstance(int instanceId, int fallback)
        {
            if (InstanceSectors != null && InstanceSectors.TryGetValue(instanceId, out int sector)) return sector;
            return fallback;
        }

        private static IReadOnlyDictionary<int, int> CopyMap(IReadOnlyDictionary<int, int> map)
        {
            if (map == null || map.Count == 0) return EmptySectors;
            var copy = new Dictionary<int, int>(map.Count);
            foreach (var kv in map) copy[kv.Key] = kv.Value;
            return copy;
        }

        private static IReadOnlyDictionary<int, float> CopyThreat(IReadOnlyDictionary<int, float> map)
        {
            if (map == null || map.Count == 0) return EmptyThreat;
            var copy = new Dictionary<int, float>(map.Count);
            foreach (var kv in map) copy[kv.Key] = kv.Value;
            return copy;
        }

        private static EnemyVisibleSector[] CopySectors(EnemyVisibleSector[] sectors)
        {
            if (sectors == null || sectors.Length == 0) return Array.Empty<EnemyVisibleSector>();
            var copy = new EnemyVisibleSector[sectors.Length];
            Array.Copy(sectors, copy, sectors.Length);
            return copy;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private static float SanitizeStrength(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value < 0f ? 0f : value;
        }
    }
}
