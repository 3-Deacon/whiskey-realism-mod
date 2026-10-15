using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Runtime-only adapter. Derives per-side army evidence from the side's per-tick
    /// <see cref="BattleSnapshot"/> (BC2b) without throwing back into Harmony/runtime
    /// callers: own figures from snapshot.Own, enemy figures only from the snapshot's
    /// visible and remembered observations (vanilla AI-side fog, BC2a). The few live reads
    /// left are own-side (sideinformation[own], the own AIBattle's objectivechain, own
    /// scheduled arrivals, own units' receivedfire and fire-range lists) or map knowledge
    /// (entry-point ownership, objectives).
    /// </summary>
    internal static partial class ArmyEvidenceBuilder
    {
        static ArmyEvidenceBuilder()
        {
            BattleScope.Register("army-evidence", ResetForBattleEnd);
        }

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
                float ownReinforcementsArriving)
                : this(ownEvidence, enemyVisible, ownMainEffortStrength,
                       ownArmyMorale, ownReservesCommittedFraction,
                       ownReinforcementsArriving,
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
                float ownReinforcementsArriving,
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
                OwnReinforcementsArriving = ownReinforcementsArriving;
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
            /// <summary>
            /// Own reinforcement strength due inside vanilla's retreat-decision window
            /// (<see cref="OwnReinforcementsArriving"/>). Own side only: the enemy's
            /// schedule is not known; enemy arrivals count once seen, through the contact
            /// ledger.
            /// </summary>
            public float OwnReinforcementsArriving { get; }
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

        internal static Bundle Build(BattleSnapshot snapshot, AIBattle battle)
        {
            var fallback = NoEvidence();

            try
            {
                if (snapshot == null) return fallback;
                int allianceId = snapshot.Alliance;
                int side = snapshot.Side;
                var bunits = ResolveBattleUnits(battle);
                if (bunits == null || side < 0) return fallback;

                float ownActive = SafeSideInfoFloat(bunits, side, "totalactiveforce");
                if (ownActive <= 0f) return fallback;

                var enemyVisible = BuildEnemyVisibleState(snapshot, bunits);
                OwnAverages(snapshot.Own, snapshot.OwnCount, out float ownAvgFatigue, out float ownAvgAmmo, out float ownMoraleRatio);

                // Odds against the enemy force this side has seen
                // (TacticalEnemyContactLedger), not the enemy's
                // sideinformation.totalactiveforce, which the side cannot know.
                // Blended from unknown (1) toward the observed ratio by the
                // ledger's coverage-based confidence (KnownEnemyOdds).
                float odds = KnownEnemyOdds(ownActive, ContactLedger(allianceId), DoctrineHours());
                // Default main effort = the center band of the front.
                var ownEvidence = new ArmyEvidence(odds, BattlefieldTerrain(allianceId, enemyVisible.FrontAxis),
                    defaultMainEffortSector: DefaultMainEffortSector(enemyVisible.FrontAxis));
                // Own reserves from the side's OWN AIBattle (aibattle[] by
                // sideofai, feud instance skipped) — the coordinator may hand us
                // the other side's instance, whose objectivechain only lists
                // its own groups.
                float ownReservesCommitted = EstimateReserveCommitFraction(
                    ResolveOwnAiBattle(bunits, side) ?? battle, bunits, side, ownActive);

                var arrivals = BuildArrivalsForSide(bunits, side);
                NearestReinforcement(arrivals, out float nearestHours, out float nearestStrength);

                // Morale relative to battle start (T17); the doctrine floor
                // (0.4) means "lost 60% of the morale it started with", not an
                // absolute level a green army may start below.
                return new Bundle(
                    ownEvidence,
                    enemyVisible,
                    Math.Max(1f, ownActive),
                    ownMoraleRatio,
                    ownReservesCommitted,
                    ReinforcementsArrivingWithin(arrivals, GamePrefs.reinforcementmaxtimeforairetreatdecisions),
                    ownAvgFatigue01: ownAvgFatigue,
                    ownAvgAmmo01: ownAvgAmmo,
                    nearestReinforcementHours: nearestHours,
                    nearestReinforcementStrength: nearestStrength);
            }
            catch (Exception e)
            {
                OnceLog.Warning("tactical-orch:evidence-builder", "[TacticalOrchestrator] ArmyEvidenceBuilder.Build degraded: "
                    + e.GetType().Name + " " + e.Message);
                return fallback;
            }
        }

        /// <summary>
        /// Own strength due inside vanilla's retreat-decision window
        /// (<see cref="ReinforcementsArrivingWithin"/>, 84764-84767) from the side's own
        /// scheduled arrivals. For readers outside the evidence build (observer telemetry).
        /// </summary>
        internal static float OwnReinforcementsArriving(BattleUnits bunits, int side)
        {
            return ReinforcementsArrivingWithin(BuildArrivalsForSide(bunits, side), GamePrefs.reinforcementmaxtimeforairetreatdecisions);
        }

        /// <summary>W&amp;L: unit under the player's command (Regiment.dlcw_isundercommander).</summary>
        private static bool IsPlayerCommanded(Regiment unit)
        {
            try { return unit != null && unit.dlcw_isundercommander; }
            catch { return false; }
        }

        private static Bundle NoEvidence()
        {
            return new Bundle(
                new ArmyEvidence(1f, TerrainKind.Open, defaultMainEffortSector: DefaultMainEffortSector(TacticalFrontAxis.None)),
                new EnemyVisibleState(Array.Empty<EnemyVisibleSector>(), 0f, false, false, intelConfidence01: 0f),
                ownMainEffortStrength: 1f,
                ownArmyMorale: 1f,
                ownReservesCommittedFraction: 0f,
                ownReinforcementsArriving: 0f);
        }

        // Last evidence axis per alliance: the stabilization reference for the
        // nearest-objective fallback and the axis SectorOfPoint / MapOwnGroupsToSectors
        // band with. Reset when the BattleUnits instance changes (new battle / reload).
        private static readonly TacticalFrontAxis[] _lastFittedAxis = new TacticalFrontAxis[2];
        private static readonly TacticalEnemyContactLedger[] _contactLedgers =
            { new TacticalEnemyContactLedger(), new TacticalEnemyContactLedger() };
        private static readonly HashSet<int> _watchedEntryBuffer = new HashSet<int>();
        private static System.WeakReference<BattleUnits> _battleOwner;

        // Scratch buffers for BuildEnemyVisibleState. Main-thread only and not
        // re-entrant; nothing returned to callers aliases them.
        private static readonly List<FrontUnitSample> _ownCombatBuffer = new List<FrontUnitSample>();
        private static readonly List<FrontUnitSample> _ownGroupsBuffer = new List<FrontUnitSample>();
        private static readonly List<FrontUnitSample> _enemyBuffer = new List<FrontUnitSample>();
        private static readonly List<FrontUnitSample> _sectorEnemyBuffer = new List<FrontUnitSample>();
        private static readonly List<FrontUnitSample> _knownEnemyBuffer = new List<FrontUnitSample>();
        private static readonly HashSet<int> _engagedIds = new HashSet<int>();
        private static readonly HashSet<Regiment> _seenFireEnemy = new HashSet<Regiment>(ReferenceComparer.Instance);
        private static float[] _flankThreatMean = new float[BattleSnapshot.InitialCapacity];
        private static int[] _flankThreatCount = new int[BattleSnapshot.InitialCapacity];

        private sealed class ReferenceComparer : IEqualityComparer<Regiment>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public bool Equals(Regiment a, Regiment b) => ReferenceEquals(a, b);
            public int GetHashCode(Regiment r) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(r);
        }

        private static readonly bool[] _ownBandBuffer = new bool[TacticalFrontAxis.DefaultBandCount];
        private static readonly float[] _bandMinBuffer = new float[TacticalFrontAxis.DefaultBandCount];
        private static readonly float[] _bandMaxBuffer = new float[TacticalFrontAxis.DefaultBandCount];

        /// <summary>
        /// Clears every per-battle cache (front axes, contact ledgers, terrain,
        /// force-balance start time). Battle end
        /// (BattleScope); also runs when the BattleUnits
        /// instance changes.
        /// </summary>
        internal static void ResetForBattleEnd()
        {
            for (int i = 0; i < 2; i++)
            {
                _lastFittedAxis[i] = TacticalFrontAxis.None;
                _contactLedgers[i].Reset();
                _terrainByAlliance[i] = null;
            }
            _battleOwner = null;
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

        /// <summary>Doctrine time in battle hours (TacticalDoctrineTime).</summary>
        private static float DoctrineHours() => (float)TacticalDoctrineTime.NowBattleHours;

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

        /// <summary>
        /// The AIBattle that plays <paramref name="allianceId"/>'s side: vanilla
        /// assigns aibattle[1] to the player's side, aibattle[0] to the other and
        /// aibattle[2] to the feud instance on side 0 (85203-85212, SetSide 3408).
        /// Each instance's objectivechain / macroai belong to its own side only, so
        /// per-side reads use this, never whichever AIBattle fired the tick.
        /// <paramref name="fallback"/> when it cannot be resolved.
        /// </summary>
        internal static AIBattle ResolveAiBattleForAlliance(AIBattle fallback, int allianceId)
        {
            try
            {
                var bunits = ResolveBattleUnits(fallback);
                if (bunits == null) return fallback;
                int side = ResolveSideFromAlliance(bunits, allianceId);
                if (side < 0) return fallback;
                return ResolveOwnAiBattle(bunits, side) ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        /// <summary>
        /// Lateral band of a world point on the front axis last fitted for
        /// <paramref name="allianceId"/> by <see cref="Build"/>; -1 before the first
        /// fit or for alliances other than 0/1.
        /// </summary>
        internal static int SectorOfPoint(int allianceId, float x, float z)
        {
            if (allianceId < 0 || allianceId > 1) return -1;
            return BandOfPoint(_lastFittedAxis[allianceId], x, z);
        }

        private readonly struct EntrySample
        {
            public EntrySample(int id, float x, float z) { Id = id; X = x; Z = z; }
            public int Id { get; }
            public float X { get; }
            public float Z { get; }
        }

        /// <summary>
        /// The enemy side's map entry points: <c>BattleUnits.entrypoints</c>
        /// (78974) whose <c>EntryPoint.owner</c> is the enemy alliance. Owner is
        /// map knowledge — set from deployment proximity (87691), the
        /// frontline zones (87891-87895) and capture (98919). Unowned points
        /// (owner -1) are not counted. Whether anything is scheduled to arrive
        /// is not read here.
        /// </summary>
        private static List<EntrySample> EnemyEntryPoints(BattleUnits bunits, int enemyAllianceId)
        {
            var list = new List<EntrySample>();
            try
            {
                var points = bunits.entrypoints;
                if (points == null || enemyAllianceId < 0) return list;
                for (int i = 0; i < points.Length; i++)
                {
                    var e = points[i];
                    if ((object)e == null || e.owner != enemyAllianceId) continue;
                    var p = e.transform.position;
                    list.Add(new EntrySample(e.GetInstanceID(), p.x, p.z));
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

        /// <summary>
        /// Builds the per-band own/enemy picture for one side from its battle snapshot.
        ///
        /// Own samples come from snapshot.Own: combat regiments (alive, on the field, not
        /// routed) and command groups at or above the shifted command level (not routed, not
        /// W&amp;L player-owned). Enemy figures come only from the snapshot's observations
        /// (<see cref="CollectEnemySamples"/>): the axis target and the contact picture from
        /// visible enemies, band strength from visible plus confidence-weighted remembered
        /// ones, coverage from every known position. An enemy is engaged when it is visible
        /// and in an own regiment's vanilla fire-range list (enemyinfirerangereg, 122692).
        /// Group flank threat is the vanilla outflanked weight over the snapshot's perceived
        /// flank pressure (<see cref="MeanFlankThreatByGroup"/>).
        ///
        /// Sectors are lateral bands of the snapshot's front axis (own centroid toward the
        /// visible-enemy centroid; the nearest objective we do not hold until anything is
        /// seen, <see cref="EvidenceFrontAxis"/>), keyed by GameObject instance id.
        /// </summary>
        private static EnemyVisibleState BuildEnemyVisibleState(BattleSnapshot s, BattleUnits bunits)
        {
            try
            {
                int ownAllianceId = s.Alliance;
                int effectiveCommandMin = ClampShiftedMin(ReadCommandHierarchyShift());
                var ownCombat = _ownCombatBuffer;
                var ownGroups = _ownGroupsBuffer;
                ownCombat.Clear();
                ownGroups.Clear();
                _engagedIds.Clear();
                _seenFireEnemy.Clear();
                Dictionary<int, float> flankThreat = null;
                bool anyRecentFire = false;
                EnsureBattleOwner(bunits);
                var enemyEntries = EnemyEntryPoints(bunits, SafeAlliance(bunits, OppositeSide(s.Side)));
                // Enemy-side entry points an own unit watches (coverage only).
                var visibleEntries = _watchedEntryBuffer;
                visibleEntries.Clear();
                bool entryPointsChecked = enemyEntries.Count == 0;

                if (_flankThreatMean.Length < s.OwnCount)
                {
                    _flankThreatMean = new float[s.OwnCount * 2];
                    _flankThreatCount = new int[s.OwnCount * 2];
                }
                MeanFlankThreatByGroup(s, _flankThreatMean, _flankThreatCount);

                // W&L: the player's regiments count at his order reliability (chain-of-command
                // spec §E): a player who ignores his dispatches is an unreliable executor, so the
                // sectors he holds get less weight for Main/SupportMain and draw reserve relief.
                float playerReliability = PlayerComplianceRuntime.Reliability;
                UnitRecord[] own = s.Own;
                for (int i = 0; i < s.OwnCount; i++)
                {
                    ref UnitRecord u = ref own[i];
                    bool routed = (u.Flags & (UnitFlags.Routed | UnitFlags.MarkedForRout)) != 0;
                    if (u.UnitTyp >= effectiveCommandMin)
                    {
                        // W&L: the player's sub-groups leak into aibattle[1]'s
                        // pool (AddUnitToPool 7641 adds descendants,
                        // RemoveUnitFromPool removes only the root). They are
                        // never AI command groups, so they get no sector/role.
                        if (routed || (u.Flags & UnitFlags.PlayerOwned) != 0) continue;
                        ownGroups.Add(new FrontUnitSample(u.InstanceId, u.X, u.Z, Math.Max(0, u.GroupStrengthAiGroup), false));
                        if (_flankThreatMean[i] > 0f)
                        {
                            if (flankThreat == null) flankThreat = new Dictionary<int, float>();
                            flankThreat[u.InstanceId] = _flankThreatMean[i];
                        }
                        continue;
                    }

                    if (u.UnitTyp > TacticalUnitType.MaxCombat || u.UnitTyp == TacticalUnitType.Excluded) continue;
                    if (!IsAliveCombatUnit(u.Strength, (u.Flags & UnitFlags.Active) != 0, routed)) continue;

                    Regiment live = BattleSnapshotBuilder.UnitAt(s, i, enemy: false);
                    bool fire = HasReceivedFire(live);
                    if (fire) anyRecentFire = true;
                    float weight = PlayerComplianceLedger.StrengthWeight((u.Flags & UnitFlags.PlayerOwned) != 0, playerReliability);
                    ownCombat.Add(new FrontUnitSample(u.InstanceId, u.X, u.Z, u.Strength * weight, fire));
                    AddFireRangeIds(live);
                    if (!entryPointsChecked) MarkVisibleEntryPoints(u.UnitTyp, u.X, u.Z, enemyEntries, visibleEntries);
                    entryPointsChecked = enemyEntries.Count > 0 && visibleEntries.Count == enemyEntries.Count;
                }

                var enemyVisible = _enemyBuffer;
                CollectEnemySamples(s, _engagedIds, enemyVisible, _sectorEnemyBuffer, _knownEnemyBuffer);

                var ownSample = ownCombat.Count > 0 ? ownCombat : ownGroups;
                var previous = ownAllianceId >= 0 && ownAllianceId <= 1 ? _lastFittedAxis[ownAllianceId] : TacticalFrontAxis.None;
                bool hasObjective = false;
                float objectiveX = 0f, objectiveZ = 0f;
                if (s.FrontAxis.IsValid && s.FrontAxis.TargetDistance <= 0f)
                    hasObjective = TryNearestOpenObjective(ownAllianceId, s.FrontAxis.OriginX, s.FrontAxis.OriginZ, out objectiveX, out objectiveZ);
                var axis = EvidenceFrontAxis(s.FrontAxis, previous, hasObjective, objectiveX, objectiveZ, ownSample, enemyVisible);
                if (ownAllianceId >= 0 && ownAllianceId <= 1) _lastFittedAxis[ownAllianceId] = axis;
                var sectors = EnemyVisibleSectorBuilder.BuildSectors(axis, ownCombat, _sectorEnemyBuffer);
                var instanceSectors = EnemyVisibleSectorBuilder.MapInstances(axis, ownGroups);

                // The enemy's reserve share is the part of the force we know of
                // that is NOT yet in fire range of our line; contact-broken comes
                // from real contact history. Never the enemy AIBattle's
                // reservegroups designation.
                float visibleStrength = 0f, engagedStrength = 0f;
                for (int i = 0; i < enemyVisible.Count; i++)
                {
                    visibleStrength += enemyVisible[i].Strength;
                    if (enemyVisible[i].RecentFire) engagedStrength += enemyVisible[i].Strength;
                }
                float nowHours = (float)s.DoctrineHours;
                var ledger = ContactLedger(ownAllianceId);
                float enemyReserveCommitFraction = TacticalEnemyContactLedger.UnknownReserveCommitFraction;
                bool contactBroken = false;
                float intelConfidence = 0f;
                if (ledger != null)
                {
                    ledger.Observe(s, engagedStrength);
                    // Coverage: bands across own units and known (visible +
                    // remembered) enemy positions, observed where an own combat
                    // unit stands; and the enemy-side entry points an own unit watches.
                    // The fitted axis serves as the coverage frame: CoverageBands re-spreads
                    // its own bands over own + known extent and only keeps origin and heading.
                    int bandCount = EnemyVisibleSectorBuilder.CoverageBands(
                        axis, ownCombat, _knownEnemyBuffer, _ownBandBuffer, _bandMinBuffer, _bandMaxBuffer);
                    ledger.ObserveCoverage(nowHours, _bandMinBuffer, _bandMaxBuffer, _ownBandBuffer, bandCount,
                        visibleEntries.Count, enemyEntries.Count, axis.HeadingDegrees);
                    enemyReserveCommitFraction = ledger.ReserveCommitFraction(engagedStrength);
                    contactBroken = ledger.ContactBroken(nowHours, visibleStrength);
                    intelConfidence = ledger.Confidence(nowHours);
                }

                return new EnemyVisibleState(
                    sectors,
                    enemyReserveCommitFraction,
                    anyContactSpotted: enemyVisible.Count > 0 || anyRecentFire,
                    anyContactBroken: contactBroken,
                    frontAxis: axis,
                    instanceSectors: instanceSectors,
                    instanceFlankThreat: flankThreat,
                    intelConfidence01: intelConfidence);
            }
            catch
            {
                return EmptyEnemyVisible();
            }
        }

        // Ids of the enemies in an own regiment's vanilla fire-range list. Regiments of one
        // brigade list the same enemies, so repeats are skipped by reference before the
        // native gameObject read.
        private static void AddFireRangeIds(Regiment own)
        {
            try
            {
                var list = (UnityEngine.Object)own != null && own.unitrange != null ? own.unitrange.enemyinfirerangereg : null;
                if (list == null) return;
                for (int i = 0; i < list.Count; i++)
                {
                    var enemy = list[i];
                    if ((object)enemy == null || !_seenFireEnemy.Add(enemy)) continue;
                    var go = enemy.gameObject;
                    if (go != null) _engagedIds.Add(go.GetInstanceID());
                }
            }
            catch { }
        }

        /// <summary>
        /// Own command group (GameObject instance id) → lateral band, using the
        /// last front axis fitted for this alliance by <see cref="Build"/> and the
        /// side's current battle snapshot. Empty until a Build has run. Lets callers
        /// that only have the battle (the direct-child allocator's fallback when the
        /// evidence carries no instance-sector map) band groups the same way the
        /// evidence did.
        /// </summary>
        internal static Dictionary<int, int> MapOwnGroupsToSectors(int allianceId)
        {
            var map = new Dictionary<int, int>();
            try
            {
                if (allianceId < 0 || allianceId > 1) return map;
                var axis = _lastFittedAxis[allianceId];
                if (!axis.IsValid || !BattleCore.TryGetSnapshot(allianceId, out BattleSnapshot s)) return map;
                int effectiveCommandMin = ClampShiftedMin(ReadCommandHierarchyShift());
                UnitRecord[] own = s.Own;
                for (int i = 0; i < s.OwnCount; i++)
                {
                    if (own[i].UnitTyp < effectiveCommandMin) continue;
                    if ((own[i].Flags & (UnitFlags.Routed | UnitFlags.MarkedForRout | UnitFlags.PlayerOwned)) != 0) continue;
                    map[own[i].InstanceId] = axis.SectorOf(own[i].X, own[i].Z);
                }
            }
            catch { }
            return map;
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

        private static EnemyVisibleState EmptyEnemyVisible()
        {
            return new EnemyVisibleState(Array.Empty<EnemyVisibleSector>(), 0f, false, false, intelConfidence01: 0f);
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
        // clock (TacticalDoctrineTime) is a TIMESTAMP — vanilla's
        // currenttimefromstart is `uniStormSystem.Hour + battlepasseddays * 24`
        // (SetTimeVariables 93545) — so a battle that begins at 14:00 would
        // report 14h elapsed without subtracting the start, firing the
        // doctrine's prolonged-delay gate (>= 6h) on tick 1. Read through
        // TacticalDoctrineTime so the night freeze applies here too.
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
        /// vanilla BattleUnits.sideinformation (own strength), the OWN side's
        /// BattleUnits.scheduledarrival entries (own arrival timing + strength),
        /// the contact ledger (enemy estimate, confidence) and commander
        /// personality fields. Returns null
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

                float ownDeployed = Math.Max(0f, SafeSideInfoFloat(bunits, side, "totalactiveforce"));

                // In-range enemy picture (T11): the enemy force this side has had
                // in range (largest sighting, decaying) and the ledger's
                // coverage-based confidence. No enemy schedule is read: enemy
                // reinforcements count once they are on the map and in range.
                // The ledger is refreshed by Build on the evidence cadence;
                // this read is cheap. Own arrivals come from the side's own
                // scheduledarrival entries — the side knows its own schedule.
                EnsureBattleOwner(bunits);
                ForceBalanceIntel(ContactLedger(allianceId), DoctrineHours(), out float enemyDeployed, out float intelConfidence);

                var ownArrivals = BuildArrivalsForSide(bunits, side);

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
