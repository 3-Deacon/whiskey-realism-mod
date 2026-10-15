using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Runtime-only vanilla adapter. Extracts per-side army evidence from
    /// BattleUnits without throwing back into Harmony/runtime callers.
    /// </summary>
    internal static partial class ArmyEvidenceBuilder
    {
        // Between ArmyIntentInference reserve thresholds: not committed, not uncommitted.
        private const float UnknownReserveCommitFraction = 0.35f;
        private static FieldInfo _bunitsFieldCache;
        private static FieldInfo _objectiveChainFieldCache;
        private static FieldInfo _commandHierarchyShiftField;
        private static readonly Dictionary<string, FieldInfo> _sideInfoFieldCache = new Dictionary<string, FieldInfo>();
        private static readonly Dictionary<string, FieldInfo> _objectFieldCache = new Dictionary<string, FieldInfo>();

        internal readonly struct Bundle
        {
            // Legacy ctor (kept for call sites that don't yet pass readiness
            // data). Defaults the new readiness fields to neutral values so
            // TacticalSectorReadinessDoctrine.Decide returns PushReady (current
            // behavior preserved). The synthesized-from-snapshot path in
            // DriveTickCycle still hits this until the snapshot also carries
            // the new fields.
            public Bundle(
                ArmyEvidence ownEvidence,
                EnemyVisibleState enemyVisible,
                float ownMainEffortStrength,
                float ownArmyMorale,
                float ownReservesCommittedFraction,
                float reinforcementsArrivingDelta)
                : this(ownEvidence, enemyVisible, ownMainEffortStrength,
                       ownArmyMorale, ownReservesCommittedFraction,
                       reinforcementsArrivingDelta,
                       ownAvgFatigue01: 0.2f,
                       ownAvgAmmo01: 0.9f,
                       nearestReinforcementHours: 0f,
                       nearestReinforcementStrength: 0f)
            { }

            public Bundle(
                ArmyEvidence ownEvidence,
                EnemyVisibleState enemyVisible,
                float ownMainEffortStrength,
                float ownArmyMorale,
                float ownReservesCommittedFraction,
                float reinforcementsArrivingDelta,
                float ownAvgFatigue01,
                float ownAvgAmmo01,
                float nearestReinforcementHours,
                float nearestReinforcementStrength)
            {
                OwnEvidence = ownEvidence;
                EnemyVisible = enemyVisible;
                OwnMainEffortStrength = ownMainEffortStrength;
                OwnArmyMorale = ownArmyMorale;
                OwnReservesCommittedFraction = ownReservesCommittedFraction;
                ReinforcementsArrivingDelta = reinforcementsArrivingDelta;
                OwnAvgFatigue01 = ClampUnit(ownAvgFatigue01, 0.2f);
                OwnAvgAmmo01 = ClampUnit(ownAvgAmmo01, 0.9f);
                NearestReinforcementHours = SanitizeNonNeg(nearestReinforcementHours);
                NearestReinforcementStrength = SanitizeNonNeg(nearestReinforcementStrength);
            }

            public ArmyEvidence OwnEvidence { get; }
            public EnemyVisibleState EnemyVisible { get; }
            public float OwnMainEffortStrength { get; }
            public float OwnArmyMorale { get; }
            public float OwnReservesCommittedFraction { get; }
            public float ReinforcementsArrivingDelta { get; }
            // Readiness fields (added 2026-05-19 for fresh-troops decision).
            // Army-wide averages, not sector-specific — first cut. Future
            // refinement: aggregate by main-effort-sector brigade membership.
            public float OwnAvgFatigue01 { get; }
            public float OwnAvgAmmo01 { get; }
            public float NearestReinforcementHours { get; }
            public float NearestReinforcementStrength { get; }

            private static float ClampUnit(float v, float fallback)
            {
                if (float.IsNaN(v) || float.IsInfinity(v)) return fallback;
                if (v < 0f) return 0f;
                if (v > 1f) return 1f;
                return v;
            }

            private static float SanitizeNonNeg(float v)
            {
                if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f) return 0f;
                return v;
            }
        }

        internal static Bundle Build(AIBattle battle, int allianceId)
        {
            var fallback = NoEvidence();

            try
            {
                var bunits = ResolveBattleUnits(battle);
                if (bunits == null) return fallback;

                int side = ResolveSideFromAlliance(bunits, allianceId);
                if (side < 0) return fallback;

                float ownActive = SafeSideInfoFloat(bunits, side, "totalactiveforce");
                if (ownActive <= 0f) return fallback;

                int frame = SafeFrameCount();
                if (_bundleMemo.TryGet(bunits, allianceId, frame, out var shared)) return shared;

                var enemyVisible = BuildEnemyVisibleState(battle, bunits, side, allianceId);
                EstimateOwnAverages(bunits, side, out float ownAvgFatigue, out float ownAvgAmmo, out float ownMoraleRatio);

                // Odds against the enemy force this side has had in range
                // (TacticalEnemyContactLedger), not the enemy's
                // sideinformation.totalactiveforce, which the side cannot know.
                // Unknown (1) until the ledger is confident and the seen force
                // clears the meaningful-enemy floor (KnownEnemyOdds).
                float odds = KnownEnemyOdds(ownActive, ContactLedger(allianceId), DoctrineHours());
                // Default main effort = the center band of the front, not
                // "whatever group is first in completeunitlist".
                var ownEvidence = new ArmyEvidence(odds, BattlefieldTerrain(allianceId, enemyVisible.FrontAxis),
                    defaultMainEffortSector: DefaultMainEffortSector(enemyVisible.FrontAxis));
                // Morale relative to battle start (T17); the doctrine floor
                // (0.4) means "lost 60% of the morale it started with", not an
                // absolute level a green army may start below.
                float ownMorale = ownMoraleRatio;
                // Own reserves from the side's OWN AIBattle (aibattle[] by
                // sideofai, feud instance skipped) — the coordinator may hand us
                // the other side's instance, whose objectivechain only lists
                // its own groups.
                float ownReservesCommitted = EstimateReserveCommitFraction(
                    ResolveOwnAiBattle(bunits, side) ?? battle, bunits, side, ownActive);
                float ownReinforcements = SanitizeNonNegative(
                    SafeSideInfoFloat(bunits, side, "reinforcementarrivalswithin24hrs"));
                // Enemy reinforcements only once their entry point is in sight.
                float enemyReinforcements = enemyVisible.EnemyReinforcementStrength24h;

                // Readiness data: army-wide avg fatigue + ammo (for now; future
                // refinement will aggregate per-sector) + nearest reinforcement
                // arrival for this side from the scheduledarrival queue.
                FindNearestReinforcement(bunits, side, out float nearestHours, out float nearestStrength);

                var bundle = new Bundle(
                    ownEvidence,
                    enemyVisible,
                    Math.Max(1f, ownActive),
                    ownMorale,
                    ownReservesCommitted,
                    ownReinforcements - enemyReinforcements,
                    ownAvgFatigue01: ownAvgFatigue,
                    ownAvgAmmo01: ownAvgAmmo,
                    nearestReinforcementHours: nearestHours,
                    nearestReinforcementStrength: nearestStrength);
                _bundleMemo.Store(bunits, allianceId, frame, bundle);
                return bundle;
            }
            catch (Exception e)
            {
                OnceLog.Warning("tactical-orch:evidence-builder", "[TacticalOrchestrator] ArmyEvidenceBuilder.Build degraded: "
                    + e.GetType().Name + " " + e.Message);
                return fallback;
            }
        }

        /// <summary>
        /// Walks BattleUnits.completeunitlist filtered to the given side and
        /// returns the average fatigue and ammo01 across alive combat regiments
        /// (unittyp 0-3). Ammo uses vanilla's own per-unit rule
        /// (<see cref="NormalizeAmmo01"/>: ammo[0], or SumUp/3 for artillery);
        /// the alive filter is <see cref="IsAliveCombatUnit"/> (strength &gt; 0,
        /// active, not routed) because groupstrengthactive is only maintained on
        /// groups. Defaults to 0.2 fatigue / 0.9 ammo when nothing is sampled.
        /// </summary>
        private static void EstimateOwnAverages(BattleUnits bunits, int side, out float avgFatigue01, out float avgAmmo01, out float moraleRatio01)
        {
            avgFatigue01 = 0.2f;
            avgAmmo01 = 0.9f;
            moraleRatio01 = 1f;
            try
            {
                int sideAlliance = SafeAlliance(bunits, side);
                if (sideAlliance < 0) return;

                var units = SafeGetCompleteUnitList();
                if (units == null || units.Count == 0) return;

                float fatigueSum = 0f;
                float ammoSum = 0f;
                float moraleSum = 0f;
                float startMoraleSum = 0f;
                int sampled = 0;

                for (int i = 0; i < units.Count; i++)
                {
                    var unit = units[i] as Regiment;
                    if (unit == null) continue;
                    if (unit.alliance != sideAlliance) continue;
                    // Combat regiments only (Infantry=0, Cavalry=1, Artillery=2,
                    // Skirmisher=3); officers/HQ carry noisy values.
                    if (unit.unittyp > 3) continue;
                    if (!IsAliveCombatUnit(unit.strength, SafeActive(unit), unit.isrouted)) continue;

                    float fatigue = unit.fatigue;
                    if (float.IsNaN(fatigue) || float.IsInfinity(fatigue)) continue;
                    if (fatigue < 0f) fatigue = 0f;
                    if (fatigue > 1f) fatigue = 1f;

                    fatigueSum += fatigue;
                    ammoSum += NormalizeAmmo01(unit.ammo, unit.unittyp, 0.9f);
                    if (unit.battlestartmorale > 0f && !float.IsNaN(unit.morale))
                    {
                        moraleSum += unit.morale;
                        startMoraleSum += unit.battlestartmorale;
                    }
                    sampled++;
                }

                if (sampled == 0) return;
                avgFatigue01 = fatigueSum / sampled;
                avgAmmo01 = ammoSum / sampled;
                moraleRatio01 = NormalizedMorale01(moraleSum, startMoraleSum, 1f);
            }
            catch
            {
                avgFatigue01 = 0.2f;
                avgAmmo01 = 0.9f;
                moraleRatio01 = 1f;
            }
        }

        /// <summary>W&amp;L: unit under the player's command (Regiment.dlcw_isundercommander).</summary>
        private static bool IsPlayerCommanded(Regiment unit)
        {
            try { return unit != null && unit.dlcw_isundercommander; }
            catch { return false; }
        }

        private static bool SafeActive(Regiment unit)
        {
            try { return unit != null && unit.gameObject != null && unit.gameObject.activeInHierarchy; }
            catch { return false; }
        }

        /// <summary>
        /// Returns the nearest scheduled arrival for the given side: hours
        /// until landing + the regiment's estimated strength. Reuses
        /// BuildArrivalsForSide which already filters by alliance and reads
        /// scheduledarrival durationleft / regiment strength. Returns (0, 0)
        /// when no arrivals queued.
        /// </summary>
        private static void FindNearestReinforcement(BattleUnits bunits, int side, out float hours, out float strength)
        {
            hours = 0f;
            strength = 0f;
            try
            {
                var arrivals = BuildArrivalsForSide(bunits, side);
                if (arrivals == null || arrivals.Count == 0) return;
                float bestHours = float.MaxValue;
                float bestStrength = 0f;
                for (int i = 0; i < arrivals.Count; i++)
                {
                    var a = arrivals[i];
                    if (a.HoursUntilArrival < bestHours)
                    {
                        bestHours = a.HoursUntilArrival;
                        bestStrength = a.EstimatedStrength;
                    }
                }
                if (bestHours < float.MaxValue)
                {
                    hours = bestHours;
                    strength = bestStrength;
                }
            }
            catch { }
        }

        private static Bundle NoEvidence()
        {
            return new Bundle(
                new ArmyEvidence(1f, TerrainKind.Open, defaultMainEffortSector: DefaultMainEffortSector(TacticalFrontAxis.None)),
                new EnemyVisibleState(Array.Empty<EnemyVisibleSector>(), 0f, false, false, 0f),
                ownMainEffortStrength: 1f,
                ownArmyMorale: 1f,
                ownReservesCommittedFraction: 0f,
                reinforcementsArrivingDelta: 0f);
        }

        // Last bare (un-banded) front axis per alliance, kept so centroid jitter
        // does not re-band units every tick (TacticalFrontAxis.Stabilize) and
        // so a side with no enemy in sight keeps facing where it last faced.
        // Reset when the BattleUnits instance changes (new battle / reload).
        private static readonly TacticalFrontAxis[] _lastBareAxis = new TacticalFrontAxis[2];
        private static readonly TacticalFrontAxis[] _lastFittedAxis = new TacticalFrontAxis[2];
        private static readonly TacticalEnemyContactLedger[] _contactLedgers =
            { new TacticalEnemyContactLedger(), new TacticalEnemyContactLedger() };
        private static readonly HashSet<int>[] _visibleEnemyEntryPoints = { new HashSet<int>(), new HashSet<int>() };
        private static System.WeakReference<BattleUnits> _battleOwner;

        // Build result per side for the current frame (shared by every call
        // site in the same coordinator tick).
        private static readonly PerSideFrameMemo<Bundle> _bundleMemo = new PerSideFrameMemo<Bundle>();

        // Scratch buffers for BuildEnemyVisibleState. Main-thread only and not
        // re-entrant; nothing returned to callers aliases them.
        private static readonly List<FrontUnitSample> _ownCombatBuffer = new List<FrontUnitSample>();
        private static readonly List<FrontUnitSample> _ownGroupsBuffer = new List<FrontUnitSample>();
        private static readonly List<FrontUnitSample> _enemyBuffer = new List<FrontUnitSample>();
        private static readonly HashSet<Regiment> _seenEnemyBuffer = new HashSet<Regiment>(ReferenceComparer.Instance);

        private sealed class ReferenceComparer : IEqualityComparer<Regiment>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public bool Equals(Regiment a, Regiment b) => ReferenceEquals(a, b);
            public int GetHashCode(Regiment r) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(r);
        }

        private static int[] _outflankedBuffer = new int[64];

        /// <summary>
        /// Mean vanilla outflanked weight over the group's combat regiments
        /// (<see cref="MeanOutflankedWeight"/>). Walks <c>allattachedunits</c>,
        /// the list vanilla's own group pass uses (120148), with the same
        /// unittyp &lt;= 13 filter; alive/unrouted stands in for
        /// activeInHierarchy so the walk stays on managed fields.
        /// </summary>
        private static float SubordinateFlankThreat(Regiment group)
        {
            try
            {
                var subs = group.allattachedunits;
                if (subs == null || subs.Length == 0) return 0f;
                if (_outflankedBuffer.Length < subs.Length) _outflankedBuffer = new int[subs.Length * 2];
                int n = 0;
                for (int i = 0; i < subs.Length; i++)
                {
                    var r = subs[i];
                    if ((object)r == null || r.unittyp > TacticalUnitType.MaxCombat) continue;
                    if (r.strength <= 0 || r.isrouted) continue;
                    _outflankedBuffer[n++] = r.outflanked;
                }
                return MeanOutflankedWeight(_outflankedBuffer, n);
            }
            catch { return 0f; }
        }

        private static int SafeFrameCount()
        {
            try { return UnityEngine.Time.frameCount; }
            catch { return -1; }
        }

        /// <summary>
        /// Clears every per-battle cache (front axes, contact ledgers, visible
        /// entry points, terrain, force-balance start time). Called on battle
        /// teardown (wired by the coordinator); also runs when the BattleUnits
        /// instance changes.
        /// </summary>
        internal static void ResetForBattleEnd()
        {
            for (int i = 0; i < 2; i++)
            {
                _lastBareAxis[i] = TacticalFrontAxis.None;
                _lastFittedAxis[i] = TacticalFrontAxis.None;
                _contactLedgers[i].Reset();
                _visibleEnemyEntryPoints[i] = new HashSet<int>();
                _terrainByAlliance[i] = null;
            }
            _battleOwner = null;
            _bundleMemo.Reset();
            _battleStartDoctrineHours = -1f;
            _battleStartOwner = null;
        }

        private static void EnsureBattleOwner(BattleUnits bunits)
        {
            bool same = _battleOwner != null && _battleOwner.TryGetTarget(out var owner) && ReferenceEquals(owner, bunits);
            if (same) return;
            ResetForBattleEnd();
            _battleOwner = new System.WeakReference<BattleUnits>(bunits);
        }

        private static readonly TerrainKind?[] _terrainByAlliance = new TerrainKind?[2];
        private const int TerrainSamplesLateral = 7;
        private const int TerrainSamplesDepth = 5;

        /// <summary>
        /// Battlefield terrain for playbook selection (T12; was the constant
        /// Open). Samples a 7x5 grid over the side's front — its lateral extent,
        /// from its line out to the target — with vanilla's
        /// BattlefieldSetup.GetCurrentTerrainOnPos (26727) and terrain height
        /// (GetTerrainHeight 27002 via TacticalTerrainProbe), classified by
        /// <see cref="ClassifyTerrain"/>. Computed once per battle per side.
        /// </summary>
        private static TerrainKind BattlefieldTerrain(int allianceId, TacticalFrontAxis axis)
        {
            if (allianceId < 0 || allianceId > 1) return TerrainKind.Open;
            var cached = _terrainByAlliance[allianceId];
            if (cached.HasValue) return cached.Value;
            if (!axis.IsValid || axis.BandCount <= 0) return TerrainKind.Open;
            try
            {
                float depth = axis.TargetDistance >= 1f ? axis.TargetDistance : 800f;
                var ids = new int[TerrainSamplesLateral * TerrainSamplesDepth];
                float minH = float.MaxValue, maxH = float.MinValue;
                int k = 0;
                for (int a = 0; a < TerrainSamplesLateral; a++)
                {
                    float lat = axis.LateralMin + (axis.LateralMax - axis.LateralMin) * a / (TerrainSamplesLateral - 1);
                    for (int b = 0; b < TerrainSamplesDepth; b++)
                    {
                        float d = depth * b / (TerrainSamplesDepth - 1);
                        float x = axis.OriginX + axis.RightX * lat + axis.ForwardX * d;
                        float z = axis.OriginZ + axis.RightZ * lat + axis.ForwardZ * d;
                        var point = new UnityEngine.Vector3(x, 0f, z);
                        ids[k++] = BattlefieldSetup.GetCurrentTerrainOnPos(point);
                        float h = TacticalTerrainProbe.WithTerrainHeight(point).y;
                        if (h < minH) minH = h;
                        if (h > maxH) maxH = h;
                    }
                }
                var kind = ClassifyTerrain(ids, maxH > minH ? maxH - minH : 0f);
                _terrainByAlliance[allianceId] = kind;
                return kind;
            }
            catch
            {
                return TerrainKind.Open;
            }
        }

        private static TacticalEnemyContactLedger ContactLedger(int allianceId)
            => allianceId >= 0 && allianceId <= 1 ? _contactLedgers[allianceId] : null;

        /// <summary>Doctrine time in battle hours (TacticalDoctrineClock).</summary>
        private static float DoctrineHours() => (float)TacticalDoctrineClock.NowBattleHours;

        /// <summary>
        /// Own AIBattle for <paramref name="side"/> from BattleUnits.aibattle[]
        /// (SelectAiBattleIndexForSide: sideofai match, feud instance skipped).
        /// </summary>
        private static AIBattle ResolveOwnAiBattle(BattleUnits bunits, int side)
        {
            try
            {
                var all = bunits?.aibattle;
                if (all == null || all.Length == 0) return null;
                var sides = new int[all.Length];
                var kinds = new int[all.Length];
                for (int i = 0; i < all.Length; i++)
                {
                    sides[i] = all[i] != null ? all[i].sideofai : -99;
                    kinds[i] = all[i] != null ? all[i].isplayeraiorfeud : 2;
                }
                int idx = SelectAiBattleIndexForSide(sides, kinds, side);
                return idx >= 0 ? all[idx] : null;
            }
            catch { return null; }
        }

        private readonly struct EntrySample
        {
            public EntrySample(int id, float x, float z) { Id = id; X = x; Z = z; }
            public int Id { get; }
            public float X { get; }
            public float Z { get; }
        }

        /// <summary>
        /// Entry points of the enemy's still-scheduled arrivals
        /// (BattleUnits.scheduledarrival 78960, ScheduledArrival.entrypoint 78616).
        /// </summary>
        private static List<EntrySample> EnemyEntryPoints(BattleUnits bunits, int ownAllianceId)
        {
            var list = new List<EntrySample>();
            try
            {
                var arrivals = bunits.scheduledarrival;
                if (arrivals == null) return list;
                var seen = new HashSet<int>();
                for (int i = 0; i < arrivals.Count; i++)
                {
                    var a = arrivals[i];
                    if (a?.regimentrefreg == null || a.entrypoint == null) continue;
                    if (a.regimentrefreg.alliance == ownAllianceId) continue;
                    int id = a.entrypoint.GetInstanceID();
                    if (!seen.Add(id)) continue;
                    var p = a.entrypoint.transform.position;
                    list.Add(new EntrySample(id, p.x, p.z));
                }
            }
            catch { }
            return list;
        }

        /// <summary>
        /// An entry point counts as watched when an own combat unit is within
        /// its line of sight (GamePrefs.maximumlineofsight[unittyp], the value
        /// vanilla seeds raycastrange[0] with in InitMaxUnitRanges 122510).
        /// </summary>
        private static void MarkVisibleEntryPoints(int unittyp, float x, float z, List<EntrySample> entries, HashSet<int> visible)
        {
            if (entries.Count == 0) return;
            float sight;
            try
            {
                var los = GamePrefs.maximumlineofsight;
                if (los == null || unittyp < 0 || unittyp >= los.Length) return;
                sight = los[unittyp];
            }
            catch { return; }
            if (sight <= 0f) return;
            float sight2 = sight * sight;
            for (int i = 0; i < entries.Count; i++)
            {
                if (visible.Contains(entries[i].Id)) continue;
                float dx = entries[i].X - x, dz = entries[i].Z - z;
                if (dx * dx + dz * dz <= sight2) visible.Add(entries[i].Id);
            }
        }

        private static float SumVisibleEnemyArrivals(BattleUnits bunits, int enemySide, HashSet<int> visibleEntries, float withinHours)
        {
            float total = 0f;
            foreach (var a in VisibleArrivalsForSide(bunits, enemySide, visibleEntries))
            {
                if (a.HoursUntilArrival <= withinHours) total += a.EstimatedStrength;
            }
            return total;
        }

        private static IReadOnlyList<ReinforcementArrival> VisibleArrivalsForSide(BattleUnits bunits, int side, HashSet<int> visibleEntries)
        {
            var result = new List<ReinforcementArrival>();
            if (visibleEntries == null || visibleEntries.Count == 0) return result;
            try
            {
                int sideAlliance = SafeAlliance(bunits, side);
                var arrivals = bunits.scheduledarrival;
                if (sideAlliance < 0 || arrivals == null) return result;
                for (int i = 0; i < arrivals.Count; i++)
                {
                    var a = arrivals[i];
                    var regiment = a?.regimentrefreg;
                    if (regiment == null || a.entrypoint == null) continue;
                    if (regiment.alliance != sideAlliance) continue;
                    if (!visibleEntries.Contains(a.entrypoint.GetInstanceID())) continue;
                    float strength = ArrivalStrength(regiment.groupstrength, regiment.groupstrengthaigroup);
                    if (strength <= 0f) continue;
                    result.Add(new ReinforcementArrival(ArrivalHoursFromDurationLeft(a.durationleft), strength));
                }
            }
            catch { }
            return result;
        }

        /// <summary>
        /// Builds the per-band own/enemy picture for one side.
        ///
        /// Enemy strength is the union of vanilla's range-limited sight lists
        /// across the side's alive combat regiments (<c>unitrange.enemyinrangereg</c>
        /// within raycastrange[0], 122649, and <c>enemyinfirerangereg</c>, 122692),
        /// each enemy counted once. The lists are range-limited, not fog-limited
        /// (vanilla AI perception; 122650/122699 skip the fog test for
        /// player-alliance targets); fog-honest perception arrives with the
        /// battle-core snapshot. The old per-group sum of
        /// <c>unitrange.enemystrengthwithinangle</c> (122720-122732) holds
        /// every enemy within aidefensivemaxrange for EVERY
        /// regiment, so a brigade of four counted the same enemy four times and
        /// a corps about forty times.
        ///
        /// Sectors are lateral bands of a <see cref="TacticalFrontAxis"/> from
        /// the own centroid toward the visible-enemy centroid (or the nearest
        /// objective we do not hold), keyed by GameObject instance id.
        /// </summary>
        private static EnemyVisibleState BuildEnemyVisibleState(AIBattle battle, BattleUnits bunits, int ownSide, int ownAllianceId)
        {
            try
            {
                var units = SafeGetCompleteUnitList();
                if (units == null) return EmptyEnemyVisible();

                int enemySide = OppositeSide(ownSide);
                int effectiveCommandMin = ClampShiftedMin(ReadCommandHierarchyShift());
                var ownCombat = _ownCombatBuffer;
                var ownGroups = _ownGroupsBuffer;
                var enemyVisible = _enemyBuffer;
                var seenEnemy = _seenEnemyBuffer;
                ownCombat.Clear();
                ownGroups.Clear();
                enemyVisible.Clear();
                seenEnemy.Clear();
                Dictionary<int, float> flankThreat = null;
                bool anyRecentFire = false;
                EnsureBattleOwner(bunits);
                var enemyEntries = EnemyEntryPoints(bunits, ownAllianceId);
                // The watched-entry set is kept per alliance for BuildForceBalance;
                // refill it in place.
                var visibleEntries = ownAllianceId >= 0 && ownAllianceId <= 1
                    ? _visibleEnemyEntryPoints[ownAllianceId]
                    : new HashSet<int>();
                visibleEntries.Clear();
                bool entryPointsChecked = enemyEntries.Count == 0;

                for (int i = 0; i < units.Count; i++)
                {
                    var reg = units[i] as Regiment;
                    if (reg == null || reg.alliance != ownAllianceId) continue;
                    bool routed = reg.isrouted || reg.markedforrout;

                    // Managed-field filters first; position and activeInHierarchy
                    // are native calls, made only for units that qualify.
                    if (reg.unittyp >= effectiveCommandMin)
                    {
                        // W&L: the player's sub-groups leak into aibattle[1]'s
                        // pool (AddUnitToPool 7641 adds descendants,
                        // RemoveUnitFromPool removes only the root). They are
                        // never AI command groups, so they get no sector/role.
                        if (routed || IsPlayerCommanded(reg)) continue;
                        if (!TryPosition(reg, out int gid, out float gx, out float gz)) continue;
                        ownGroups.Add(new FrontUnitSample(gid, gx, gz, Math.Max(0, reg.groupstrengthaigroup), false));
                        float threat = SubordinateFlankThreat(reg);
                        if (threat > 0f)
                        {
                            if (flankThreat == null) flankThreat = new Dictionary<int, float>();
                            flankThreat[gid] = threat;
                        }
                        continue;
                    }

                    if (reg.unittyp > TacticalUnitType.MaxCombat || reg.unittyp == TacticalUnitType.Excluded) continue;
                    if (reg.strength <= 0 || routed) continue;
                    if (!IsAliveCombatUnit(reg.strength, SafeActive(reg), routed)) continue;
                    if (!TryPosition(reg, out int id, out float x, out float z)) continue;

                    bool fire = HasReceivedFire(reg);
                    if (fire) anyRecentFire = true;
                    ownCombat.Add(new FrontUnitSample(id, x, z, reg.strength, fire));
                    CollectVisibleEnemies(reg, ownAllianceId, seenEnemy, enemyVisible);
                    if (!entryPointsChecked) MarkVisibleEntryPoints(reg.unittyp, x, z, enemyEntries, visibleEntries);
                    entryPointsChecked = enemyEntries.Count > 0 && visibleEntries.Count == enemyEntries.Count;
                }

                var bare = BareAxis(bunits, ownAllianceId, ownCombat.Count > 0 ? ownCombat : ownGroups, enemyVisible);
                var axis = EnemyVisibleSectorBuilder.FitAxis(bare, ownCombat.Count > 0 ? ownCombat : ownGroups, enemyVisible);
                if (ownAllianceId >= 0 && ownAllianceId <= 1) _lastFittedAxis[ownAllianceId] = axis;
                var sectors = EnemyVisibleSectorBuilder.BuildSectors(axis, ownCombat, enemyVisible);
                var instanceSectors = EnemyVisibleSectorBuilder.MapInstances(axis, ownGroups);

                // In-range enemy knowledge (T6/T11): the enemy's reserve share
                // is the part of the force we have seen that is NOT yet in fire
                // range of our line; contact-broken comes from real contact
                // history. Never the enemy AIBattle's reservegroups designation.
                float visibleStrength = 0f, engagedStrength = 0f;
                for (int i = 0; i < enemyVisible.Count; i++)
                {
                    visibleStrength += enemyVisible[i].Strength;
                    if (enemyVisible[i].RecentFire) engagedStrength += enemyVisible[i].Strength;
                }
                float nowHours = DoctrineHours();
                var ledger = ContactLedger(ownAllianceId);
                float enemyReserveCommitFraction = TacticalEnemyContactLedger.UnknownReserveCommitFraction;
                bool contactBroken = false;
                if (ledger != null)
                {
                    ledger.Observe(nowHours, visibleStrength, engagedStrength);
                    enemyReserveCommitFraction = ledger.ReserveCommitFraction(engagedStrength);
                    contactBroken = ledger.ContactBroken(nowHours, visibleStrength);
                }
                float enemyReinforcements = SumVisibleEnemyArrivals(bunits, enemySide, visibleEntries, withinHours: 24f);

                return new EnemyVisibleState(
                    sectors,
                    enemyReserveCommitFraction,
                    anyContactSpotted: enemyVisible.Count > 0 || anyRecentFire,
                    anyContactBroken: contactBroken,
                    enemyReinforcements,
                    frontAxis: axis,
                    instanceSectors: instanceSectors,
                    instanceFlankThreat: flankThreat);
            }
            catch
            {
                return EmptyEnemyVisible();
            }
        }

        /// <summary>
        /// Own-centroid → target axis, stabilized against the last axis for this
        /// alliance. Target = strength-weighted centroid of visible enemies; with
        /// nothing in sight, the nearest objective not held by this alliance
        /// (BattleUnits.objectives 78910, owner 84809 — public map knowledge);
        /// otherwise keep facing the last known way.
        /// </summary>
        private static TacticalFrontAxis BareAxis(
            BattleUnits bunits,
            int allianceId,
            IReadOnlyList<FrontUnitSample> own,
            IReadOnlyList<FrontUnitSample> enemy)
        {
            if (!Centroid(own, out float ox, out float oz)) return TacticalFrontAxis.None;

            int slot = allianceId == 1 ? 1 : 0;
            var previous = allianceId >= 0 && allianceId <= 1 ? _lastBareAxis[slot] : TacticalFrontAxis.None;

            TacticalFrontAxis next;
            if (Centroid(enemy, out float ex, out float ez))
                next = TacticalFrontAxis.FromCentroids(ox, oz, ex, ez);
            else if (TryNearestOpenObjective(allianceId, ox, oz, out float tx, out float tz))
                next = TacticalFrontAxis.FromCentroids(ox, oz, tx, tz);
            else if (previous.IsValid)
                next = TacticalFrontAxis.FromCentroids(ox, oz, ox + previous.ForwardX * previous.TargetDistance,
                    oz + previous.ForwardZ * previous.TargetDistance);
            else
                next = TacticalFrontAxis.FromCentroids(ox, oz, ox, oz);

            var stable = TacticalFrontAxis.Stabilize(previous, next, TacticalFrontAxis.DefaultMinHeadingChangeDegrees);
            if (allianceId >= 0 && allianceId <= 1) _lastBareAxis[slot] = stable;
            return stable;
        }

        /// <summary>
        /// Own command group (GameObject instance id) → lateral band, using the
        /// last front axis fitted for this alliance by <see cref="Build"/>.
        /// Empty until a Build has run. Lets callers that only have the battle
        /// (the worker capture path) band groups the same way the evidence did.
        /// </summary>
        internal static Dictionary<int, int> MapOwnGroupsToSectors(int allianceId)
        {
            var map = new Dictionary<int, int>();
            try
            {
                if (allianceId < 0 || allianceId > 1) return map;
                var axis = _lastFittedAxis[allianceId];
                if (!axis.IsValid) return map;
                var units = SafeGetCompleteUnitList();
                if (units == null) return map;
                int effectiveCommandMin = ClampShiftedMin(ReadCommandHierarchyShift());
                for (int i = 0; i < units.Count; i++)
                {
                    var reg = units[i] as Regiment;
                    if (reg == null || reg.alliance != allianceId) continue;
                    if (reg.unittyp < effectiveCommandMin) continue;
                    if (reg.isrouted || reg.markedforrout || IsPlayerCommanded(reg)) continue;
                    if (!TryPosition(reg, out int id, out float x, out float z)) continue;
                    map[id] = axis.SectorOf(x, z);
                }
            }
            catch { }
            return map;
        }

        private static bool Centroid(IReadOnlyList<FrontUnitSample> units, out float x, out float z)
        {
            x = 0f; z = 0f;
            if (units == null || units.Count == 0) return false;
            double sx = 0, sz = 0, w = 0;
            for (int i = 0; i < units.Count; i++)
            {
                double weight = Math.Max(1f, units[i].Strength);
                sx += units[i].X * weight;
                sz += units[i].Z * weight;
                w += weight;
            }
            if (w <= 0) return false;
            x = (float)(sx / w);
            z = (float)(sz / w);
            return true;
        }

        private static bool TryNearestOpenObjective(int allianceId, float ox, float oz, out float tx, out float tz)
        {
            tx = 0f; tz = 0f;
            try
            {
                var objectives = BattleUnits.objectives;
                if (objectives == null) return false;
                float best = float.MaxValue;
                for (int i = 0; i < objectives.Length; i++)
                {
                    var obj = objectives[i];
                    if (obj == null || obj.owner == allianceId) continue;
                    var p = obj.transform.position;
                    float dx = p.x - ox, dz = p.z - oz;
                    float d = dx * dx + dz * dz;
                    if (d < best) { best = d; tx = p.x; tz = p.z; }
                }
                return best < float.MaxValue;
            }
            catch
            {
                return false;
            }
        }

        private static void CollectVisibleEnemies(Regiment own, int ownAllianceId, HashSet<Regiment> seen, List<FrontUnitSample> output)
        {
            try
            {
                var range = own.unitrange;
                if (range == null) return;
                // Fire-range list first so an enemy in both lists is recorded
                // as engaged (FrontUnitSample.RecentFire doubles as "in contact").
                AddVisible(range.enemyinfirerangereg, ownAllianceId, seen, output, engaged: true);
                AddVisible(range.enemyinrangereg, ownAllianceId, seen, output, engaged: false);
            }
            catch { }
        }

        private static void AddVisible(List<Regiment> list, int ownAllianceId, HashSet<Regiment> seen, List<FrontUnitSample> output, bool engaged)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var enemy = list[i];
                if ((object)enemy == null || enemy.alliance == ownAllianceId) continue;
                // Same eligibility vanilla applies when filling the lists (122644).
                if (enemy.unittyp == TacticalUnitType.Excluded || enemy.unittyp > TacticalUnitType.MaxCombat) continue;
                if (enemy.isrouted) continue;
                // Dedupe by reference before the native reads: every regiment
                // in a brigade lists the same enemies, so most entries are
                // repeats. An enemy rejected below is rejected for every list.
                if (!seen.Add(enemy)) continue;
                if (!SafeActive(enemy)) continue;
                if (!TryPosition(enemy, out int id, out float x, out float z)) continue;
                output.Add(new FrontUnitSample(id, x, z, enemy.strength, engaged));
            }
        }

        private static bool TryPosition(Regiment reg, out int instanceId, out float x, out float z)
        {
            instanceId = 0; x = 0f; z = 0f;
            try
            {
                var go = reg.gameObject;
                if (go == null) return false;
                instanceId = go.GetInstanceID();
                var p = go.transform.position;
                if (float.IsNaN(p.x) || float.IsNaN(p.z) || float.IsInfinity(p.x) || float.IsInfinity(p.z)) return false;
                x = p.x;
                z = p.z;
                return instanceId != 0;
            }
            catch
            {
                return false;
            }
        }

        private static EnemyVisibleState EmptyEnemyVisible()
        {
            return new EnemyVisibleState(Array.Empty<EnemyVisibleSector>(), 0f, false, false, 0f);
        }

        private static BattleUnits ResolveBattleUnits(AIBattle battle)
        {
            try
            {
                if (battle == null) return null;
                if (_bunitsFieldCache == null)
                    _bunitsFieldCache = AccessTools.Field(typeof(AIBattle), "bunits");
                return _bunitsFieldCache?.GetValue(battle) as BattleUnits;
            }
            catch
            {
                return null;
            }
        }

        private static int ResolveSideFromAlliance(BattleUnits bunits, int allianceId)
        {
            try
            {
                if (bunits == null || bunits.alliance == null) return -1;
                for (int side = 0; side < 2 && side < bunits.alliance.Length; side++)
                {
                    if (bunits.alliance[side] == allianceId) return side;
                }
            }
            catch { }
            return -1;
        }

        private static bool IsUsableOwnGroup(Regiment group, int ownAllianceId, int effectiveCommandMin)
        {
            try
            {
                if (group == null) return false;
                if (group.alliance != ownAllianceId) return false;
                if (group.unittyp < effectiveCommandMin) return false;
                if (group.isrouted || group.markedforrout) return false;
                if (IsPlayerCommanded(group)) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasReceivedFire(Regiment unit)
        {
            try
            {
                return unit != null && unit.receivedfire != null && unit.receivedfire.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private static float SafeSideInfoFloat(BattleUnits bunits, int side, string fieldName)
        {
            return TrySideInfoFloat(bunits, side, fieldName, out var value) ? value : 0f;
        }

        /// <summary>
        /// Reads vanilla's <c>BattleUnits.GetBattleType(alliance)</c> and maps
        /// to <see cref="BattleStance"/>. Returns Meeting on any failure so
        /// the doctrine + playbook layer fall through to evidence-based
        /// behavior rather than acting on a wrong stance assumption.
        /// </summary>
        private static BattleStance ReadBattleStance(BattleUnits bunits, int allianceId)
        {
            try
            {
                if (bunits == null) return BattleStance.Meeting;
                int code = bunits.GetBattleType(allianceId);
                switch (code)
                {
                    case 1: return BattleStance.Attacker;
                    case 2: return BattleStance.Defender;
                    default: return BattleStance.Meeting;
                }
            }
            catch { return BattleStance.Meeting; }
        }

        // Battle-start doctrine time for elapsed-hours computation. The doctrine
        // clock (TacticalDoctrineClock) is a TIMESTAMP — vanilla's
        // currenttimefromstart is `uniStormSystem.Hour + battlepasseddays * 24`
        // (SetTimeVariables 93545) — so a battle that begins at 14:00 would
        // report 14h elapsed without subtracting the start, firing the
        // doctrine's prolonged-delay gate (>= 6h) on tick 1. Read through
        // TacticalDoctrineClock so the night freeze applies here too.
        private static float _battleStartDoctrineHours = -1f;
        private static System.WeakReference<BattleUnits> _battleStartOwner;

        /// <summary>
        /// Elapsed doctrine hours since this BattleUnits was first read. Resets
        /// when the BattleUnits reference changes (battle teardown + restart)
        /// or the clock goes backward.
        /// </summary>
        private static float SafeReadBattleHours(BattleUnits bunits)
        {
            try
            {
                if (bunits == null) return 0f;
                float now = DoctrineHours();

                bool sameOwner = false;
                if (_battleStartOwner != null && _battleStartOwner.TryGetTarget(out var cachedOwner))
                    sameOwner = ReferenceEquals(cachedOwner, bunits);

                if (!sameOwner || _battleStartDoctrineHours < 0f || now < _battleStartDoctrineHours)
                {
                    _battleStartDoctrineHours = now;
                    _battleStartOwner = new System.WeakReference<BattleUnits>(bunits);
                    return 0f;
                }

                return now - _battleStartDoctrineHours;
            }
            catch { return 0f; }
        }

        private static bool TrySideInfoFloat(BattleUnits bunits, int side, string fieldName, out float value)
        {
            value = 0f;
            try
            {
                if (bunits == null || bunits.sideinformation == null) return false;
                if (side < 0 || side >= bunits.sideinformation.Length) return false;
                var info = bunits.sideinformation[side];
                if (info == null) return false;
                var field = ResolveSideInfoField(info.GetType(), fieldName);
                if (field == null) return false;
                object raw = field.GetValue(info);
                if (raw == null) return false;
                value = Convert.ToSingle(raw);
                return true;
            }
            catch
            {
                value = 0f;
                return false;
            }
        }

        private static FieldInfo ResolveSideInfoField(Type infoType, string fieldName)
        {
            if (infoType == null) return null;

            string key = infoType.FullName + ":" + fieldName;
            if (_sideInfoFieldCache.ContainsKey(key))
                return _sideInfoFieldCache[key];

            var field = infoType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _sideInfoFieldCache[key] = field;
            return field;
        }

        private static float EstimateReserveCommitFraction(AIBattle battle, BattleUnits bunits, int side, float activeForce)
        {
            if (!TrySumReservePoolStrength(battle, bunits, side, out float reserveStrength, out bool hasDesignatedReserves))
                return UnknownReserveCommitFraction;

            // Chains exist but no reservegroups designated for this side. The army
            // has no reserve plan; report "unknown" rather than 100% committed.
            // Otherwise ArmyReplanTriggers.ReserveExhaustion fires every cycle.
            if (!hasDesignatedReserves)
                return UnknownReserveCommitFraction;

            float committed = 1f - reserveStrength / Math.Max(1f, activeForce);
            return Clamp01OrDefault(committed, UnknownReserveCommitFraction);
        }

        private static bool TrySumReservePoolStrength(AIBattle battle, BattleUnits bunits, int side, out float reserveStrength, out bool hasDesignatedReserves)
        {
            reserveStrength = 0f;
            hasDesignatedReserves = false;
            try
            {
                var chains = ResolveObjectiveChains(battle);
                if (chains == null || chains.Count == 0) return false;

                int alliance = SafeAlliance(bunits, side);
                if (alliance < 0) return false;
                int effectiveCommandMin = ClampShiftedMin(ReadCommandHierarchyShift());

                bool observedChain = false;
                var seen = new HashSet<int>();
                for (int i = 0; i < chains.Count; i++)
                {
                    object chain = chains[i];
                    if (chain == null) continue;
                    observedChain = true;

                    var reserves = SafeObjectList(chain, "reservegroups");
                    if (reserves == null) continue;

                    for (int j = 0; j < reserves.Count; j++)
                    {
                        var group = reserves[j] as Regiment;
                        if (!IsUsableOwnGroup(group, alliance, effectiveCommandMin)) continue;

                        int id = SafeInstanceId(group);
                        if (id != 0 && !seen.Add(id)) continue;

                        hasDesignatedReserves = true;
                        reserveStrength += ActiveGroupStrength(group);
                    }
                }

                return observedChain;
            }
            catch
            {
                reserveStrength = 0f;
                hasDesignatedReserves = false;
                return false;
            }
        }

        private static IList ResolveObjectiveChains(AIBattle battle)
        {
            try
            {
                if (battle == null) return null;
                if (_objectiveChainFieldCache == null)
                    _objectiveChainFieldCache = typeof(AIBattle).GetField(
                        "objectivechain",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return _objectiveChainFieldCache?.GetValue(battle) as IList;
            }
            catch
            {
                return null;
            }
        }

        private static IList SafeObjectList(object instance, string fieldName)
        {
            try
            {
                if (instance == null) return null;
                var field = ResolveObjectField(instance.GetType(), fieldName);
                return field?.GetValue(instance) as IList;
            }
            catch
            {
                return null;
            }
        }

        private static FieldInfo ResolveObjectField(Type type, string fieldName)
        {
            if (type == null) return null;

            string key = type.FullName + ":" + fieldName;
            if (_objectFieldCache.ContainsKey(key))
                return _objectFieldCache[key];

            var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _objectFieldCache[key] = field;
            return field;
        }

        private static int SafeAlliance(BattleUnits bunits, int side)
        {
            try
            {
                if (bunits == null || bunits.alliance == null) return -1;
                if (side < 0 || side >= bunits.alliance.Length) return -1;
                return bunits.alliance[side];
            }
            catch
            {
                return -1;
            }
        }

        private static int SafeInstanceId(Regiment group)
        {
            try
            {
                return group == null ? 0 : group.GetInstanceID();
            }
            catch
            {
                return 0;
            }
        }

        private static float ActiveGroupStrength(Regiment group)
        {
            try
            {
                if (group == null) return 0f;
                int strength = group.groupstrengthactive;
                if (strength <= 0) strength = group.groupstrengthaigroup;
                return Math.Max(0f, strength);
            }
            catch
            {
                return 0f;
            }
        }

        private static IList SafeGetCompleteUnitList()
        {
            try
            {
                return BattleUnits.completeunitlist as IList;
            }
            catch
            {
                return null;
            }
        }

        internal static int ReadCommandHierarchyShift()
        {
            try
            {
                if (_commandHierarchyShiftField == null)
                {
                    _commandHierarchyShiftField = AccessTools.Field(typeof(GamePrefs), "commandhierarchyshift");
                }

                if (_commandHierarchyShiftField == null) return 0;
                var value = _commandHierarchyShiftField.GetValue(null);
                return value is int shift ? shift : 0;
            }
            catch
            {
                return 0;
            }
        }

        internal static int ClampShiftedMin(int shift)
        {
            int min = TacticalUnitType.MaxCombat + 1 + shift;
            if (min < 1) return 1;
            if (min > 18) return 18;
            return min;
        }

        private static int OppositeSide(int side)
        {
            return side == 0 ? 1 : 0;
        }

        private static float Clamp01OrDefault(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private static float SanitizeNonNegative(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value < 0f ? 0f : value;
        }

        // ---------- Reinforcement Opportunity force balance ----------

        /// <summary>
        /// Builds TacticalForceBalanceEvidence for the given alliance by reading
        /// vanilla BattleUnits.sideinformation (current strength), BattleUnits.scheduledarrival
        /// (per-arrival timing + strength), and commander personality fields. Returns null
        /// on any failure so callers can fall through to existing behavior.
        /// </summary>
        internal static TacticalForceBalanceEvidence? BuildForceBalance(
            AIBattle battle,
            int allianceId,
            float commanderInitiative01,
            float commanderAggression01)
        {
            try
            {
                var bunits = ResolveBattleUnits(battle);
                if (bunits == null) return null;
                int side = ResolveSideFromAlliance(bunits, allianceId);
                if (side < 0) return null;
                int enemySide = OppositeSide(side);

                float ownDeployed = Math.Max(0f, SafeSideInfoFloat(bunits, side, "totalactiveforce"));

                // In-range enemy picture (T11): the enemy force this side has had
                // in range (largest sighting, decaying), arrivals only through an
                // entry point our units can see, and confidence from contact
                // coverage/recency instead of a fixed 0.8. The ledger and the
                // watched entry points are refreshed by Build on the evidence
                // cadence; this read is cheap.
                EnsureBattleOwner(bunits);
                var ledger = ContactLedger(allianceId);
                float enemyDeployed = ledger == null ? 0f : ledger.EstimatedEnemyStrength;
                float intelConfidence = ledger == null ? 0f : ledger.Confidence(DoctrineHours(), ownDeployed);

                var ownArrivals = BuildArrivalsForSide(bunits, side);
                var enemyArrivals = allianceId >= 0 && allianceId <= 1
                    ? VisibleArrivalsForSide(bunits, enemySide, _visibleEnemyEntryPoints[allianceId])
                    : (IReadOnlyList<ReinforcementArrival>)Array.Empty<ReinforcementArrival>();

                // Casualty escalation inputs (2026-05-23 / J). Read from vanilla
                // BattleUnits.sideinformation. casualtyratio: 0..1; routedratio:
                // 0..1; elapsed battle hours on doctrine time.
                float ownCasualtyRatio = Math.Max(0f, Math.Min(1f, SafeSideInfoFloat(bunits, side, "casualtyratio")));
                float ownRoutedRatio = Math.Max(0f, Math.Min(1f, SafeSideInfoFloat(bunits, side, "routedratio")));
                float battleHours = SafeReadBattleHours(bunits);

                // Stance (2026-05-23 / L) — read vanilla's GetBattleType(alliance)
                // at decompile line 86095. Returns 0 (meeting), 1 (we attack),
                // 2 (we defend). Hard-filters doctrine + playbook so they can't
                // diverge from vanilla's scenario-determined posture.
                BattleStance stance = ReadBattleStance(bunits, allianceId);

                return new TacticalForceBalanceEvidence(
                    ownDeployed,
                    enemyDeployed,
                    ownArrivals,
                    enemyArrivals,
                    commanderInitiative01,
                    commanderAggression01,
                    intelConfidence,
                    ownCasualtyRatio01: ownCasualtyRatio,
                    ownRoutedFraction01: ownRoutedRatio,
                    battleHoursElapsed: battleHours,
                    stance: stance);
            }
            catch (Exception e)
            {
                OnceLog.Warning("tactical-orch:force-balance",
                    "[TacticalOrchestrator] ArmyEvidenceBuilder.BuildForceBalance degraded: "
                    + e.GetType().Name + " " + e.Message);
                return null;
            }
        }

        private static IReadOnlyList<ReinforcementArrival> BuildArrivalsForSide(BattleUnits bunits, int side)
        {
            try
            {
                int sideAlliance = SafeAlliance(bunits, side);
                if (sideAlliance < 0) return Array.Empty<ReinforcementArrival>();

                var arrivals = bunits.scheduledarrival;
                if (arrivals == null || arrivals.Count == 0) return Array.Empty<ReinforcementArrival>();

                var result = new List<ReinforcementArrival>();
                for (int i = 0; i < arrivals.Count; i++)
                {
                    var arrival = arrivals[i];
                    var regiment = arrival?.regimentrefreg;
                    if (regiment == null) continue;
                    if (regiment.alliance != sideAlliance) continue;
                    // durationleft is game hours (87562-87584) — no /3600.
                    float hours = ArrivalHoursFromDurationLeft(arrival.durationleft);
                    float strength = ArrivalStrength(regiment.groupstrength, regiment.groupstrengthaigroup);
                    if (strength <= 0f) continue;
                    result.Add(new ReinforcementArrival(hours, strength));
                }
                return result;
            }
            catch
            {
                return Array.Empty<ReinforcementArrival>();
            }
        }
    }
}
