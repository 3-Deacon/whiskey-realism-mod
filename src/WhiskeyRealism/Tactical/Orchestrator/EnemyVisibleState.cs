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
        /// Enemy strength below this share of the sector's own strength is a
        /// picket, not a force: odds against it are not meaningful (a 50-man
        /// screen in front of a corps used to read as 100:1 "decisive" odds).
        /// Relative, never an absolute head count — GTCW battles run from
        /// 1,500 to 60,000 a side.
        /// </summary>
        public const float MinMeaningfulEnemyFractionOfOwn = 0.10f;

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

        public bool HasMeaningfulEnemy =>
            EnemyStrength > 0f && EnemyStrength >= OwnStrength * MinMeaningfulEnemyFractionOfOwn;

        public float Odds => !HasMeaningfulEnemy ? 0f : OwnStrength / Math.Max(1f, EnemyStrength);

        private static float SanitizeStrength(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value < 0f ? 0f : value;
        }
    }

    /// <summary>
    /// One unit's position and strength as the side sees it. Own units come
    /// from the own roster; enemy units only from vanilla's fog-filtered
    /// per-regiment sight lists (<c>unitrange.enemyinrangereg</c> /
    /// <c>enemyinfirerangereg</c>, Regiment 122649/122692).
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
    /// Visible enemy state bundle, fog-honest: enemy figures come only from
    /// what the side's own regiments can see. Built by ArmyEvidenceBuilder and
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
            float enemyReinforcementStrength24h,
            TacticalFrontAxis frontAxis = default(TacticalFrontAxis),
            IReadOnlyDictionary<int, int> instanceSectors = null,
            IReadOnlyDictionary<int, float> instanceFlankThreat = null)
        {
            Sectors = CopySectors(sectors);
            EnemyReserveCommitFraction = Clamp01(enemyReserveCommitFraction);
            AnyContactSpotted = anyContactSpotted;
            AnyContactBroken = anyContactBroken;
            EnemyReinforcementStrength24h = SanitizeStrength(enemyReinforcementStrength24h);
            FrontAxis = frontAxis;
            InstanceSectors = CopyMap(instanceSectors);
            InstanceFlankThreat = CopyThreat(instanceFlankThreat);
        }

        public EnemyVisibleSector[] Sectors { get; }
        public float EnemyReserveCommitFraction { get; }
        public bool AnyContactSpotted { get; }
        public bool AnyContactBroken { get; }
        public float EnemyReinforcementStrength24h { get; }
        public TacticalFrontAxis FrontAxis { get; }
        public IReadOnlyDictionary<int, int> InstanceSectors { get; }

        /// <summary>Own command group instance id → vanilla <c>flanksthreated</c>.</summary>
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
