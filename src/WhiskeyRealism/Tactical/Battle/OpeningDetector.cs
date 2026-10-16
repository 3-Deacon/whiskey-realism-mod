using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tactical.Battle
{
    // BC13 (spec docs/superpowers/specs/2026-10-15-battle-opportunity-design.md § 5): the fog-honest detector of
    // local openings for one side. Pure over the side's BattleSnapshot, its front axis, a perceived-ground oracle and
    // the public terrain. Five opening kinds vanilla lacks (a gap, a shaken sector, a flank in the air, an unsupported
    // battery, an isolated cluster) and, for a Defender with Enable Defender Counterstroke on, a bounded penetration.
    //
    // Fog rule (requirement-enemy-samples): the enemy inputs are only the observations that are Visible, or
    // Remembered at Confidence01 >= 0.5, not reinforcement columns, of type infantry, cavalry or artillery. A Visible
    // observation's own EnemyRecords row is read for width, routed, garrisoned, melee and morale (the BC6b precedent);
    // no other EnemyRecords read exists in this file (test DET-PIN pins the region below).
    //
    // Frame: lateral L = FrontAxis.LateralOf(x, z); depth D = the projection on the axis forward vector from the
    // axis origin. Forward points at the enemy, so an enemy unit is "shallower" with a smaller D and an own unit with
    // a larger D (requirement-detector-geometry).
    //
    // One instance per side, pooled and grow-only: Detect allocates only while its arrays still grow. The perceived
    // ground of an absence-asserting opening (a gap, a flank in the air, a battery) is proved by a probe lattice
    // (PerceivedGroundLattice), at most MaxProbePointsPerTick points a tick across all of the side's lattices, oldest
    // first; a candidate is emitted only once its lattice is complete and every point is perceived.
    public sealed class OpeningDetector
    {
        public const float GapMinMeters = 150f, LineDepthBandMeters = 200f, GapDepthMeters = 300f, ProbeSpacingMeters = 50f,
            RememberedMinConfidence = 0.5f, DefaultEnemyFrontageMeters = 100f, ShakenShareMin = 0.4f, FlankWatchMeters = 300f,
            FlankOffsetMeters = 150f, SupportMeters = 250f, ClusterLinkMeters = 150f, RefusedEndDegrees = 45f,
            CounterstrokeMaxMeters = 400f, PenetrationMinDepthMeters = 100f, PenetrationClearMeters = 50f;

        // Doctrine hours are doubles, so are the times compared with them: a float 0.05 is 0.05000000074 and would put
        // the bucket boundary at 10.0 h on the wrong side of a double division.
        public const double ConfirmHours = 0.02, LostHours = 0.05, ProbeEpochHours = 0.05;
        public const int MaxOpenings = 32, MaxClusters = 64, MaxProbePointsPerTick = 128;
        // Pooled lattices: one per candidate that needs its ground proved, including those that ended unperceived (their
        // result is kept until the time bucket turns). Twice the opening table, so the candidates beyond MaxOpenings that the
        // identity rule sorts out still get their proof; a candidate with no slot waits (counted as deferred).
        public const int MaxLattices = 2 * MaxOpenings;

        // Constants of the rules above that the spec states in prose.
        public const float FrontWindowMeters = 300f;            // front line: same-side infantry within +-300 m lateral
        public const float GapCorridorBackMeters = 50f;         // the corridor starts 50 m in front of the shallower edge
        public const float GapAimDepthMeters = 100f;            // the aim sits 100 m behind the deeper edge
        public const float FlankDepthMeters = 300f;             // the ground beyond a flank end: 300 m outward by 300 m of depth
        public const float RefusedNeighbourMeters = 400f;       // the refused-end mean heading: line samples within 400 m
        public const float ShakenFormedMeters = 300f;           // a routed-only crowd counts when within 300 m of a formed enemy
        public const float OwnGapEnemyMeters = 500f;            // line-integrity: an own gap matters facing a perceived enemy within 500 m
        public const float OpeningMatchMeters = 100f;           // a detection matches last tick's opening of its kind within 100 m
        public const float LocalEnemyValueRadiusMeters = 300f;  // OpportunityDoctrine.OddsRadiusMeters
        public const float ClusterValueShare = 0.65f;           // vanilla CheckForSeparatedEnemy 8155
        public const float ClusterSeparationFactor = 1.3f;      // vanilla CheckForSeparatedEnemy 8148
        public const float ClusterMinAssetMeters = 250f;        // vanilla stops when a cluster is within 250 m (8160)
        // A cluster can qualify for no asset unless its nearest neighbour is further than 1.3 x the smallest asset
        // distance, so a nearer neighbour is not worth an opening.
        public const float ClusterIsolationFloorMeters = ClusterSeparationFactor * ClusterMinAssetMeters;
        private const int MaxBands = 16;
        private const int CellOffset = 16384, CellStride = 32768;

        private struct Sample
        {
            public float X, Z, L, D, W, Strength, Morale, Heading, Conf;
            public int Id;
            public byte Type;
            public bool Visible, Routed, Garrisoned, InMelee, Front;
        }

        private struct Candidate
        {
            public Opening O;
            public float ClusterX, ClusterZ, ClusterValue, ClusterNearest;
            /// <summary>The pooled lattice proving the ground this candidate asserts, or -1 when it needs none.</summary>
            public int Lattice;
        }

        private struct ClusterInfo
        {
            public float X, Z, Value, Nearest;
        }

        // ---- pooled state ------------------------------------------------------------------
        private Sample[] _s = new Sample[256];
        private int _n;
        private int[] _inf = new int[256], _front = new int[256];
        private int _nfront;
        private readonly Dictionary<int, int> _rowByInstanceId = new Dictionary<int, int>(256);
        private Sample[] _o = new Sample[64];
        private int _no;
        private int[] _oinf = new int[64], _ofront = new int[64];
        private int _nofront;
        private readonly PerceivedGroundLattice[] _lattices = new PerceivedGroundLattice[MaxLattices];
        private long _tick, _latticeOrder;
        private Candidate[] _cand = new Candidate[64];
        private int _candCount;
        private Opening[] _open = new Opening[MaxOpenings];
        private ClusterInfo[] _info = new ClusterInfo[MaxOpenings];
        private bool[] _matched = new bool[MaxOpenings];
        private bool[] _candMatched = new bool[64];
        private int _openCount;
        private int _nextId = 1;
        private readonly float[] _secTotal = new float[MaxBands], _secVisible = new float[MaxBands], _secShaken = new float[MaxBands], _secRouted = new float[MaxBands];
        private float[] _cx = new float[64], _cz = new float[64], _cvalue = new float[64], _cstrength = new float[64], _cconf = new float[64], _cbest = new float[64];
        private int[] _cbestId = new int[64], _cidx = new int[256], _uf = new int[256], _ckey = new int[256], _corder = new int[256];
        private bool[] _ckeep = new bool[64];
        private float[] _cnearest = new float[64];

        // ---- per-call context (cleared before Detect returns) --------------------------------
        private TacticalFrontAxis _axis;
        private IPerceivedGround _perceived;
        private IOpeningTerrain _terrain;
        private OpeningRules _rules;
        private double _now;
        private ProbeEpoch _epoch;

        /// <summary>The openings the detector holds (including those unseen for less than <see cref="LostHours"/>).</summary>
        public ReadOnlySpan<Opening> Openings => new ReadOnlySpan<Opening>(_open, 0, _openCount);

        /// <summary>Candidates the last Detect call refused because a probe point of the ground they assert was unperceived.</summary>
        public int SuppressedUnperceived { get; private set; }

        /// <summary>Probe points the last Detect call asked the oracle about.</summary>
        public int ProbePointsEvaluated { get; private set; }

        public int Count => _openCount;

        /// <summary>Candidates the last Detect call held back: their lattice is not complete (or has no pooled slot) and no earlier complete result is emitted.</summary>
        public int Deferred { get; private set; }

        /// <summary>Point results the last Detect call reused instead of probing.</summary>
        public int ProbeCached { get; private set; }

        /// <summary>Lattices that reached their start, lattices that completed or ended unperceived, and lattices that ended past the completion bound (0 expected), in the last Detect call.</summary>
        public int LatticesStarted { get; private set; }
        public int LatticesEnded { get; private set; }
        public int DeferOverBound { get; private set; }

        /// <summary>
        /// Detects the side's openings for this tick and returns how many it holds. Allocation-free after warm-up.
        /// A Defender side (with the counterstroke flag on) detects Penetration only: it can launch nothing else.
        /// </summary>
        public int Detect(BattleSnapshot s, IPerceivedGround perceived, IOpeningTerrain terrain, in OpeningRules rules, bool defender, double nowHours)
        {
            SuppressedUnperceived = 0;
            ProbePointsEvaluated = 0;
            Deferred = 0;
            ProbeCached = 0;
            LatticesStarted = 0;
            LatticesEnded = 0;
            DeferOverBound = 0;
            _candCount = 0;
            _now = nowHours;
            _tick++;
            if (s != null && perceived != null && terrain != null && s.FrontAxis.IsValid)
            {
                _axis = s.FrontAxis;
                _perceived = perceived;
                _terrain = terrain;
                _rules = rules;
                _epoch = new ProbeEpoch { Mode = s.Perception, Fallback = (int)s.PerceptionFallback, Bucket = ProbeEpoch.BucketOf(nowHours) };
                try
                {
                    BuildSamples(s);
                    if (defender)
                    {
                        if (rules.CounterstrokeOn) PenetrationPass(s);
                    }
                    else
                    {
                        _nfront = BuildFront(_s, _n, true, ref _inf, ref _front);
                        GapPass();
                        ShakenPass();
                        FlankPass();
                        BatteryPass();
                        ClusterPass();
                    }
                    ResolveProbes();
                }
                finally
                {
                    _perceived = null;
                    _terrain = null;
                }
            }
            UpdateIdentity(nowHours);
            return _openCount;
        }

        /// <summary>Writes the cluster behind a ClusterKind opening: centroid, value, and the nearest other cluster's distance.</summary>
        public bool TryCluster(int id, out float x, out float z, out float value, out float nearestOtherMeters)
        {
            for (int i = 0; i < _openCount; i++)
            {
                if (_open[i].Id != id || _open[i].Kind != OpeningKind.IsolatedCluster) continue;
                x = _info[i].X;
                z = _info[i].Z;
                value = _info[i].Value;
                nearestOtherMeters = _info[i].Nearest;
                return true;
            }
            x = z = value = nearestOtherMeters = 0f;
            return false;
        }

        /// <summary>
        /// Vanilla's per-group rule for an isolated cluster (CheckForSeparatedEnemy 8148-8160): the cluster's value is at
        /// most 0.65 x the group's strength x morale, its nearest neighbour is further than 1.3 x the group's distance to
        /// it, and the group is at least 250 m away. Unlike vanilla, any qualifying cluster may be chosen.
        /// </summary>
        public static bool ClusterQualifies(float clusterValue, float nearestOtherMeters, float assetStrengthMorale, float assetDistanceMeters)
        {
            return clusterValue <= ClusterValueShare * assetStrengthMorale
                && nearestOtherMeters > ClusterSeparationFactor * assetDistanceMeters
                && assetDistanceMeters >= ClusterMinAssetMeters;
        }

        /// <summary>Marks an opening as claimed by a live exploit (derived each tick by the producer; never stored beyond it).</summary>
        public void ClearClaims()
        {
            for (int i = 0; i < _openCount; i++) _open[i].Claimed = false;
        }

        public void SetClaimed(int openingId)
        {
            for (int i = 0; i < _openCount; i++)
            {
                if (_open[i].Id == openingId)
                {
                    _open[i].Claimed = true;
                    return;
                }
            }
        }

        // ================================================================================ own gap

        private static Sample[] _ownScratch = new Sample[64];
        private static int[] _ownInf = new int[64], _ownFront = new int[64];

        /// <summary>
        /// The own line's gap at a lateral position when the excluded subtrees (the commitment ledger's roots and the
        /// asset's own) are taken away: the edge gap between the two own front-line regiments that straddle
        /// <paramref name="lateral"/>, when it is at least <see cref="GapMinMeters"/>, the two stand within
        /// <see cref="LineDepthBandMeters"/> of depth and a perceived enemy stands within
        /// <see cref="OwnGapEnemyMeters"/> of it; else 0 (requirement-risk-check, line-integrity: requirement-detect-gap
        /// applied to own front-line regiments, with no perceived-ground test).
        /// </summary>
        public static float OwnGapAt(BattleSnapshot s, ReadOnlySpan<int> excludedSubtreeRoots, float lateral)
        {
            if (s == null || !s.FrontAxis.IsValid) return 0f;
            TacticalFrontAxis axis = s.FrontAxis;
            int n = 0;
            for (int i = 0; i < s.OwnCount; i++)
            {
                ref UnitRecord r = ref s.Own[i];
                if (!IsOwnLineRegiment(in r) || InExcludedSubtree(s, i, excludedSubtreeRoots)) continue;
                if (n == _ownScratch.Length) Array.Resize(ref _ownScratch, n * 2);
                FillOwn(ref _ownScratch[n], in r, in axis);
                n++;
            }
            int front = BuildFront(_ownScratch, n, false, ref _ownInf, ref _ownFront);
            for (int k = 0; k + 1 < front; k++)
            {
                ref Sample a = ref _ownScratch[_ownFront[k]];
                ref Sample b = ref _ownScratch[_ownFront[k + 1]];
                if (lateral < a.L || lateral > b.L) continue;
                float edgeGap = (b.L - b.W * 0.5f) - (a.L + a.W * 0.5f);
                if (edgeGap < GapMinMeters || Math.Abs(a.D - b.D) > LineDepthBandMeters) continue;
                float midL = (a.L + a.W * 0.5f + b.L - b.W * 0.5f) * 0.5f;
                float midD = (a.D + b.D) * 0.5f;
                float gx = axis.OriginX + midL * axis.RightX + midD * axis.ForwardX;
                float gz = axis.OriginZ + midL * axis.RightZ + midD * axis.ForwardZ;
                return PerceivedEnemyWithin(s, gx, gz, OwnGapEnemyMeters) ? edgeGap : 0f;
            }
            return 0f;
        }

        private static bool IsOwnLineRegiment(in UnitRecord r)
        {
            UnitFlags f = r.Flags;
            if ((f & UnitFlags.IsGroup) != 0 || (f & UnitFlags.IsInfantry) == 0) return false;
            if ((f & UnitFlags.Alive) == 0 || (f & UnitFlags.Routed) != 0) return false;
            return !float.IsNaN(r.X) && !float.IsNaN(r.Z);
        }

        private static bool InExcludedSubtree(BattleSnapshot s, int ownIndex, ReadOnlySpan<int> roots)
        {
            for (int k = 0; k < roots.Length; k++)
            {
                int root = roots[k];
                if (root == ownIndex || s.IsAncestorOf(root, ownIndex)) return true;
            }
            return false;
        }

        private static void FillOwn(ref Sample sample, in UnitRecord r, in TacticalFrontAxis axis)
        {
            sample = default;
            sample.X = r.X;
            sample.Z = r.Z;
            ToFrame(in axis, r.X, r.Z, out sample.L, out sample.D);
            sample.W = r.UnitWidth > 0f ? r.UnitWidth : DefaultEnemyFrontageMeters;
            sample.Type = 0;
            sample.Id = r.InstanceId;
            sample.Visible = true;
            sample.Conf = 1f;
        }

        private static bool PerceivedEnemyWithin(BattleSnapshot s, float x, float z, float meters)
        {
            float r2 = meters * meters;
            EnemyObservation[] obs = s.Observations;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (obs[k].State != ObservationState.Visible || obs[k].IsReinforcement) continue;
                if (obs[k].UnitTyp < 0 || obs[k].UnitTyp > 2) continue;
                float dx = obs[k].X - x, dz = obs[k].Z - z;
                if (dx * dx + dz * dz <= r2) return true;
            }
            return false;
        }

        // ================================================================================ samples

        private void BuildSamples(BattleSnapshot s)
        {
            IndexVisibleRows(s);
            int cap = s.ObservationCount;
            if (_s.Length < cap) _s = new Sample[Math.Max(cap, _s.Length * 2)];
            _n = 0;
            EnemyObservation[] obs = s.Observations;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                ref EnemyObservation o = ref obs[k];
                bool visible = o.State == ObservationState.Visible;
                if (!visible && !(o.State == ObservationState.Remembered && o.Confidence01 >= RememberedMinConfidence)) continue;
                if (o.IsReinforcement || o.UnitTyp < 0 || o.UnitTyp > 2) continue;
                ref Sample m = ref _s[_n];
                m = default;
                m.X = o.X;
                m.Z = o.Z;
                ToFrame(in _axis, o.X, o.Z, out m.L, out m.D);
                m.W = DefaultEnemyFrontageMeters;
                m.Strength = o.Strength;
                m.Morale = o.Morale;
                m.Heading = o.Heading;
                m.Conf = visible ? 1f : o.Confidence01;
                m.Id = o.InstanceId;
                m.Type = (byte)o.UnitTyp;
                m.Visible = visible;
                if (visible) ReadVisibleRow(s, o.InstanceId, ref m);
                _n++;
            }
        }

        // BEGIN-VISIBLE-ROWS (DET-PIN: EnemyRecords is indexed only between these markers)
        private void IndexVisibleRows(BattleSnapshot s)
        {
            _rowByInstanceId.Clear();
            for (int e = 0; e < s.EnemyRecordCount; e++)
                if (s.EnemyRecords[e].InstanceId != 0) _rowByInstanceId[s.EnemyRecords[e].InstanceId] = e;
        }

        private void ReadVisibleRow(BattleSnapshot s, int instanceId, ref Sample m)
        {
            if (!_rowByInstanceId.TryGetValue(instanceId, out int e)) return;
            ref UnitRecord row = ref s.EnemyRecords[e];
            if (row.UnitWidth > 0f) m.W = row.UnitWidth;
            m.Morale = row.Morale;
            m.Routed = (row.Flags & UnitFlags.Routed) != 0;
            m.Garrisoned = (row.Flags & UnitFlags.Garrisoned) != 0;
            m.InMelee = (row.Flags & UnitFlags.InMelee) != 0;
        }
        // END-VISIBLE-ROWS

        private static void ToFrame(in TacticalFrontAxis axis, float x, float z, out float lateral, out float depth)
        {
            float dx = x - axis.OriginX, dz = z - axis.OriginZ;
            lateral = dx * axis.RightX + dz * axis.RightZ;
            depth = dx * axis.ForwardX + dz * axis.ForwardZ;
        }

        private void FromFrame(float lateral, float depth, out float x, out float z)
        {
            x = _axis.OriginX + lateral * _axis.RightX + depth * _axis.ForwardX;
            z = _axis.OriginZ + lateral * _axis.RightZ + depth * _axis.ForwardZ;
        }

        // The front-line rule (requirement-detector-geometry): an infantry sample is front-line when no other infantry
        // sample of the same side within +-300 m lateral is more than LineDepthBandMeters shallower. Fills `front`
        // with the front-line samples in lateral order and returns how many. The lateral sort is an insertion sort
        // over an index array (no comparer, no allocation).
        private static int BuildFront(Sample[] s, int n, bool enemySide, ref int[] inf, ref int[] front)
        {
            if (inf.Length < n) inf = new int[Math.Max(n, inf.Length * 2)];
            if (front.Length < n) front = new int[Math.Max(n, front.Length * 2)];
            int m = 0;
            for (int i = 0; i < n; i++)
            {
                s[i].Front = false;
                if (s[i].Type == 0) inf[m++] = i;
            }
            for (int a = 1; a < m; a++)
            {
                int v = inf[a];
                float lv = s[v].L;
                int b = a - 1;
                while (b >= 0 && s[inf[b]].L > lv)
                {
                    inf[b + 1] = inf[b];
                    b--;
                }
                inf[b + 1] = v;
            }
            int count = 0;
            for (int a = 0; a < m; a++)
            {
                ref Sample sa = ref s[inf[a]];
                float advance = enemySide ? -sa.D : sa.D;
                bool isFront = true;
                for (int b = a - 1; b >= 0 && isFront; b--)
                {
                    ref Sample sb = ref s[inf[b]];
                    if (sa.L - sb.L > FrontWindowMeters) break;
                    if ((enemySide ? -sb.D : sb.D) - advance > LineDepthBandMeters) isFront = false;
                }
                for (int b = a + 1; b < m && isFront; b++)
                {
                    ref Sample sb = ref s[inf[b]];
                    if (sb.L - sa.L > FrontWindowMeters) break;
                    if ((enemySide ? -sb.D : sb.D) - advance > LineDepthBandMeters) isFront = false;
                }
                if (isFront)
                {
                    sa.Front = true;
                    front[count++] = inf[a];
                }
            }
            return count;
        }

        // ================================================================================ candidates

        // Adds a candidate (after the probe proof for an absence-asserting one) and returns its index, or -1 when
        // the probe found unperceived ground.
        private int AddCandidate(OpeningKind kind, float aimX, float aimZ, float heading, float width, float confidence, int target, bool hasBox, in ProbeBox box)
        {
            int lattice = -1;
            if (hasBox)
            {
                lattice = RequestProbe(kind, in box);
                if (lattice < 0)
                {
                    Deferred++;   // no pooled lattice slot free this tick
                    return -1;
                }
            }
            if (_candCount == _cand.Length) Array.Resize(ref _cand, _cand.Length * 2);
            ref Candidate c = ref _cand[_candCount];
            c = default;
            c.Lattice = lattice;
            c.O.Kind = kind;
            c.O.AimX = aimX;
            c.O.AimZ = aimZ;
            c.O.Heading = heading;
            c.O.Width = width;
            c.O.Confidence01 = confidence;
            c.O.TargetObservation = target;
            c.O.Sector = _axis.SectorOf(aimX, aimZ);
            c.O.LocalEnemyValue = LocalEnemyValue(aimX, aimZ);
            return _candCount++;
        }

        // The lattice proving a candidate's ground: the one this candidate had last tick (same kind, box centre within
        // 100 m), restarted when its geometry, the perception mode or the fallback changed (or, once it is complete,
        // when the time bucket turned), else a new one at the back of the serving order. -1 when the pool is full.
        private int RequestProbe(OpeningKind kind, in ProbeBox box)
        {
            box.Centre(out float cx, out float cz);
            int found = -1;
            float bestD = OpeningMatchMeters * OpeningMatchMeters;
            int free = -1;
            for (int i = 0; i < _lattices.Length; i++)
            {
                PerceivedGroundLattice lattice = _lattices[i];
                if (lattice == null || !lattice.InUse)
                {
                    if (free < 0) free = i;
                    continue;
                }
                if (lattice.Claimed || lattice.Kind != kind) continue;
                lattice.Box.Centre(out float lx, out float lz);
                float dx = lx - cx, dz = lz - cz;
                float d = dx * dx + dz * dz;
                if (d <= bestD) { bestD = d; found = i; }
            }
            if (found >= 0)
            {
                PerceivedGroundLattice lattice = _lattices[found];
                lattice.Claimed = true;
                if (lattice.NeedsRestart(in box, in _epoch, out bool keep))
                    lattice.Begin(found, kind, in box, in _epoch, keep, ++_latticeOrder);
                return found;
            }
            if (free < 0) return -1;
            if (_lattices[free] == null) _lattices[free] = new PerceivedGroundLattice();
            _lattices[free].Begin(free, kind, in box, in _epoch, false, ++_latticeOrder);
            _lattices[free].Claimed = true;
            return free;
        }

        // Serves this tick's budget to the claimed lattices, then keeps the candidates whose ground is proved: a complete
        // lattice that perceives every point, or one re-probing a bucket turn whose last complete result was proved.
        private void ResolveProbes()
        {
            var stats = new LatticeTickStats();
            PerceivedGroundLattice.Serve(_lattices, _lattices.Length, _perceived, MaxProbePointsPerTick, _tick, ref stats);
            ProbePointsEvaluated = stats.PointsProbed;
            ProbeCached = stats.PointsCached;
            LatticesStarted = stats.Started;
            LatticesEnded = stats.Ended;
            DeferOverBound = stats.OverBound;

            int kept = 0;
            for (int c = 0; c < _candCount; c++)
            {
                int index = _cand[c].Lattice;
                if (index >= 0)
                {
                    PerceivedGroundLattice lattice = _lattices[index];
                    if (lattice.State == LatticeState.Unperceived)
                    {
                        SuppressedUnperceived++;
                        continue;
                    }
                    if (lattice.State == LatticeState.Incomplete && !lattice.LastResultPerceived)
                    {
                        Deferred++;
                        continue;
                    }
                }
                if (kept != c) _cand[kept] = _cand[c];
                kept++;
            }
            _candCount = kept;

            // A lattice no candidate asked for this tick is dropped: its candidate is gone.
            for (int i = 0; i < _lattices.Length; i++)
            {
                if (_lattices[i] == null) continue;
                if (!_lattices[i].Claimed) _lattices[i].InUse = false;
                _lattices[i].Claimed = false;
            }
        }

        private float LocalEnemyValue(float aimX, float aimZ)
        {
            float r2 = LocalEnemyValueRadiusMeters * LocalEnemyValueRadiusMeters;
            float value = 0f;
            for (int i = 0; i < _n; i++)
            {
                float dx = _s[i].X - aimX, dz = _s[i].Z - aimZ;
                if (dx * dx + dz * dz > r2) continue;
                value += _s[i].Visible ? _s[i].Strength * _s[i].Morale : _s[i].Strength * _s[i].Conf * _s[i].Morale;
            }
            return value;
        }

        // ================================================================================ gap

        private void GapPass()
        {
            for (int k = 0; k + 1 < _nfront; k++)
            {
                ref Sample a = ref _s[_front[k]];
                ref Sample b = ref _s[_front[k + 1]];
                if (Math.Abs(a.D - b.D) > LineDepthBandMeters) continue;
                float leftEdge = a.L + a.W * 0.5f, rightEdge = b.L - b.W * 0.5f;
                float edgeGap = rightEdge - leftEdge;
                if (edgeGap < GapMinMeters) continue;
                float depthMin = Math.Min(a.D, b.D) - GapCorridorBackMeters;
                float depthMax = Math.Max(a.D, b.D) + GapDepthMeters;
                if (CorridorOccupied(leftEdge, rightEdge, depthMin, depthMax)) continue;
                var box = ProbeBox.Make(in _axis, leftEdge, rightEdge, depthMin, depthMax, ProbeSpacingMeters);
                float aimD = Math.Max(a.D, b.D) + GapAimDepthMeters;
                FromFrame((leftEdge + rightEdge) * 0.5f, aimD, out float ax, out float az);
                AddCandidate(OpeningKind.Gap, ax, az, _axis.HeadingDegrees, edgeGap, a.Conf * b.Conf, -1, true, in box);
            }
        }

        // No enemy sample of any type has its centre strictly between the edges within the corridor depth: a second
        // line behind the gap closes it.
        private bool CorridorOccupied(float leftEdge, float rightEdge, float depthMin, float depthMax)
        {
            for (int i = 0; i < _n; i++)
            {
                if (_s[i].L > leftEdge && _s[i].L < rightEdge && _s[i].D >= depthMin && _s[i].D <= depthMax) return true;
            }
            return false;
        }

        // ================================================================================ shaken sector

        private void ShakenPass()
        {
            int bands = _axis.BandCount > 0 ? Math.Min(_axis.BandCount, MaxBands) : 1;
            for (int b = 0; b < bands; b++) _secTotal[b] = _secVisible[b] = _secShaken[b] = _secRouted[b] = 0f;
            for (int i = 0; i < _n; i++)
            {
                int b = SectorBand(_s[i].L, bands);
                _secTotal[b] += _s[i].Visible ? _s[i].Strength : _s[i].Strength * _s[i].Conf;
                if (!_s[i].Visible) continue;
                _secVisible[b] += _s[i].Strength;
                if (_s[i].Routed) _secRouted[b] += _s[i].Strength;
                else if (_s[i].Morale < _rules.ShakenMorale) _secShaken[b] += _s[i].Strength;
            }

            for (int b = 0; b < bands; b++)
            {
                float total = _secTotal[b];
                if (!(total > 0f) || (_secShaken[b] + _secRouted[b]) / total < ShakenShareMin) continue;

                float sumW = 0f, sumX = 0f, sumZ = 0f;
                bool qualifies = false;
                for (int i = 0; i < _n; i++)
                {
                    if (!_s[i].Visible || SectorBand(_s[i].L, bands) != b || !IsShakenOrRouted(in _s[i])) continue;
                    sumW += _s[i].Strength;
                    sumX += _s[i].X * _s[i].Strength;
                    sumZ += _s[i].Z * _s[i].Strength;
                    // routed-only crowds far behind their line are a pursuit, not an opening
                    if (!_s[i].Routed || NearFormedEnemy(i)) qualifies = true;
                }
                if (!qualifies || !(sumW > 0f)) continue;

                float aimX = sumX / sumW, aimZ = sumZ / sumW;
                int target = NearestShaken(aimX, aimZ, b, bands);
                AddCandidate(OpeningKind.ShakenSector, aimX, aimZ, 0f, 0f, _secVisible[b] / total, target, false, default);
            }
        }

        private int SectorBand(float lateral, int bands)
        {
            int sector = _axis.SectorOfLateral(lateral);
            return sector < 0 ? 0 : sector >= bands ? bands - 1 : sector;
        }

        private bool IsShakenOrRouted(in Sample m) => m.Routed || m.Morale < _rules.ShakenMorale;

        private bool NearFormedEnemy(int index)
        {
            float r2 = ShakenFormedMeters * ShakenFormedMeters;
            for (int j = 0; j < _n; j++)
            {
                if (j == index || !_s[j].Visible || _s[j].Routed || _s[j].Morale < _rules.ShakenMorale) continue;
                float dx = _s[j].X - _s[index].X, dz = _s[j].Z - _s[index].Z;
                if (dx * dx + dz * dz <= r2) return true;
            }
            return false;
        }

        // The shaken (not routed) sample nearest the centroid; else the nearest routed one.
        private int NearestShaken(float x, float z, int band, int bands)
        {
            int best = -1, bestRouted = -1;
            float bestD = float.MaxValue, bestRoutedD = float.MaxValue;
            for (int i = 0; i < _n; i++)
            {
                if (!_s[i].Visible || SectorBand(_s[i].L, bands) != band || !IsShakenOrRouted(in _s[i])) continue;
                float dx = _s[i].X - x, dz = _s[i].Z - z;
                float d = dx * dx + dz * dz;
                if (_s[i].Routed)
                {
                    if (d < bestRoutedD) { bestRoutedD = d; bestRouted = i; }
                }
                else if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            int pick = best >= 0 ? best : bestRouted;
            return pick >= 0 ? _s[pick].Id : -1;
        }

        // ================================================================================ flank in the air

        private void FlankPass()
        {
            if (_nfront < 2) return;
            CheckFlankEnd(_front[0], -1);
            CheckFlankEnd(_front[_nfront - 1], +1);
        }

        // sign -1: the left end (outward is -right), +1: the right end.
        private void CheckFlankEnd(int endIndex, int sign)
        {
            Sample end = _s[endIndex];
            float edgeL = end.L + sign * end.W * 0.5f;
            float outX = sign * _axis.RightX, outZ = sign * _axis.RightZ;
            FromFrame(edgeL, end.D, out float edgeX, out float edgeZ);

            if (_terrain.RiverWithin(edgeX, edgeZ, outX, outZ, _rules.AnchorRiverMeters)) return;
            if (_rules.PictureReady && _terrain.CrestWithin(edgeX, edgeZ, _rules.AnchorHillMeters)) return;
            if (IsRefusedEnd(endIndex)) return;
            if (HasScreenBeyond(edgeL, end.D, sign)) return;

            float farL = edgeL + sign * FlankWatchMeters;
            var box = ProbeBox.Make(in _axis, edgeL, farL, end.D - FlankDepthMeters * 0.5f, end.D + FlankDepthMeters * 0.5f, ProbeSpacingMeters);
            FromFrame(edgeL + sign * FlankOffsetMeters, end.D, out float aimX, out float aimZ);
            float heading = BearingDegrees(-outX, -outZ);
            AddCandidate(OpeningKind.FlankInAir, aimX, aimZ, heading, 0f, end.Conf, -1, true, in box);
        }

        // The end's heading differs from the strength-weighted circular mean heading of the line samples within 400 m
        // (the end itself excluded) by at least 45 degrees. An end with no such neighbour is not refused.
        private bool IsRefusedEnd(int endIndex)
        {
            ref Sample end = ref _s[endIndex];
            double sumSin = 0.0, sumCos = 0.0;
            float r2 = RefusedNeighbourMeters * RefusedNeighbourMeters;
            bool any = false;
            for (int k = 0; k < _nfront; k++)
            {
                int j = _front[k];
                if (j == endIndex) continue;
                float dx = _s[j].X - end.X, dz = _s[j].Z - end.Z;
                if (dx * dx + dz * dz > r2) continue;
                double rad = _s[j].Heading * (Math.PI / 180.0);
                double w = Math.Max(1f, _s[j].Strength);
                sumSin += Math.Sin(rad) * w;
                sumCos += Math.Cos(rad) * w;
                any = true;
            }
            if (!any) return false;
            float mean = (float)(Math.Atan2(sumSin, sumCos) * (180.0 / Math.PI));
            return AngleBetween(end.Heading, mean) >= RefusedEndDegrees;
        }

        private bool HasScreenBeyond(float edgeL, float endDepth, int sign)
        {
            for (int i = 0; i < _n; i++)
            {
                float offset = sign * (_s[i].L - edgeL);
                if (offset > 0f && offset <= FlankWatchMeters && Math.Abs(_s[i].D - endDepth) <= FlankDepthMeters) return true;
            }
            return false;
        }

        // ================================================================================ unsupported battery

        private void BatteryPass()
        {
            float r2 = SupportMeters * SupportMeters;
            for (int i = 0; i < _n; i++)
            {
                ref Sample battery = ref _s[i];
                if (battery.Type != 2 || !battery.Visible || battery.Garrisoned) continue;
                bool supported = false;
                for (int j = 0; j < _n && !supported; j++)
                {
                    if (_s[j].Type == 2) continue;
                    float dx = _s[j].X - battery.X, dz = _s[j].Z - battery.Z;
                    supported = dx * dx + dz * dz <= r2;
                }
                if (supported) continue;
                var box = ProbeBox.Make(in _axis, battery.L - SupportMeters, battery.L + SupportMeters, battery.D - SupportMeters, battery.D + SupportMeters, ProbeSpacingMeters);
                AddCandidate(OpeningKind.UnsupportedBattery, battery.X, battery.Z, 0f, 0f, battery.Conf, battery.Id, true, in box);
            }
        }

        // ================================================================================ isolated cluster

        private void ClusterPass()
        {
            int n = _n;
            if (n < 2) return;
            if (_ckey.Length < n)
            {
                int size = Math.Max(n, _ckey.Length * 2);
                _ckey = new int[size];
                _corder = new int[size];
                _uf = new int[size];
                _cidx = new int[size];
            }
            for (int i = 0; i < n; i++)
            {
                int cx = (int)Math.Floor(_s[i].X / ClusterLinkMeters);
                int cz = (int)Math.Floor(_s[i].Z / ClusterLinkMeters);
                _ckey[i] = (cx + CellOffset) * CellStride + (cz + CellOffset);
                _corder[i] = i;
                _uf[i] = i;
                _cidx[i] = -1;
            }
            Array.Sort(_ckey, _corder, 0, n);

            float link2 = ClusterLinkMeters * ClusterLinkMeters;
            for (int p = 0; p < n; p++)
            {
                int u = _corder[p];
                int ucx = _ckey[p] / CellStride - CellOffset;
                int ucz = _ckey[p] % CellStride - CellOffset;
                for (int dcx = -1; dcx <= 1; dcx++)
                {
                    for (int dcz = -1; dcz <= 1; dcz++)
                    {
                        int key = (ucx + dcx + CellOffset) * CellStride + (ucz + dcz + CellOffset);
                        for (int q = LowerBound(key, n); q < n && _ckey[q] == key; q++)
                        {
                            int v = _corder[q];
                            if (v <= u) continue;
                            float dx = _s[v].X - _s[u].X, dz = _s[v].Z - _s[u].Z;
                            if (dx * dx + dz * dz <= link2) Union(u, v);
                        }
                    }
                }
            }

            int clusters = 0;
            for (int i = 0; i < n; i++)
            {
                int root = Find(i);
                if (_cidx[root] < 0)
                {
                    EnsureClusterArrays(clusters + 1);
                    _cidx[root] = clusters;
                    _cx[clusters] = _cz[clusters] = _cvalue[clusters] = _cstrength[clusters] = 0f;
                    _cbest[clusters] = -1f;
                    _cconf[clusters] = 1f;
                    _cbestId[clusters] = -1;
                    clusters++;
                }
                int c = _cidx[root];
                float w = Math.Max(1f, _s[i].Strength);
                _cx[c] += _s[i].X * w;
                _cz[c] += _s[i].Z * w;
                _cstrength[c] += w;
                _cvalue[c] += _s[i].Visible ? _s[i].Strength * _s[i].Morale : _s[i].Strength * _s[i].Conf * _s[i].Morale;
                _cconf[c] *= _s[i].Conf;
                if (_s[i].Strength > _cbest[c])
                {
                    _cbest[c] = _s[i].Strength;
                    _cbestId[c] = _s[i].Id;
                }
            }
            if (clusters < 2) return;
            for (int c = 0; c < clusters; c++)
            {
                _cx[c] /= _cstrength[c];
                _cz[c] /= _cstrength[c];
                _ckeep[c] = true;
            }

            // Beyond MaxClusters the clusters farthest from the own line (the axis origin) are dropped.
            for (int kept = clusters; kept > MaxClusters; kept--)
            {
                int far = -1;
                float farD = -1f;
                for (int c = 0; c < clusters; c++)
                {
                    if (!_ckeep[c]) continue;
                    float dx = _cx[c] - _axis.OriginX, dz = _cz[c] - _axis.OriginZ;
                    float d = dx * dx + dz * dz;
                    if (d > farD) { farD = d; far = c; }
                }
                _ckeep[far] = false;
            }

            for (int c = 0; c < clusters; c++)
            {
                if (!_ckeep[c]) continue;
                float nearest = float.MaxValue;
                for (int d = 0; d < clusters; d++)
                {
                    if (d == c || !_ckeep[d]) continue;
                    float dx = _cx[d] - _cx[c], dz = _cz[d] - _cz[c];
                    float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (dist < nearest) nearest = dist;
                }
                _cnearest[c] = nearest;
            }
            for (int c = 0; c < clusters; c++)
            {
                if (!_ckeep[c] || !(_cnearest[c] > ClusterIsolationFloorMeters) || _cnearest[c] == float.MaxValue) continue;
                int slot = AddCandidate(OpeningKind.IsolatedCluster, _cx[c], _cz[c], 0f, 0f, _cconf[c], _cbestId[c], false, default);
                if (slot < 0) continue;
                _cand[slot].ClusterX = _cx[c];
                _cand[slot].ClusterZ = _cz[c];
                _cand[slot].ClusterValue = _cvalue[c];
                _cand[slot].ClusterNearest = _cnearest[c];
            }
        }

        private void EnsureClusterArrays(int needed)
        {
            if (_cx.Length >= needed) return;
            int size = Math.Max(needed, _cx.Length * 2);
            Array.Resize(ref _cx, size);
            Array.Resize(ref _cz, size);
            Array.Resize(ref _cvalue, size);
            Array.Resize(ref _cstrength, size);
            Array.Resize(ref _cconf, size);
            Array.Resize(ref _cbest, size);
            Array.Resize(ref _cbestId, size);
            Array.Resize(ref _ckeep, size);
            Array.Resize(ref _cnearest, size);
        }

        private int Find(int i)
        {
            while (_uf[i] != i)
            {
                _uf[i] = _uf[_uf[i]];
                i = _uf[i];
            }
            return i;
        }

        private void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb) _uf[ra < rb ? rb : ra] = ra < rb ? ra : rb;
        }

        private int LowerBound(int key, int n)
        {
            int lo = 0, hi = n;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_ckey[mid] < key) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        // ================================================================================ penetration

        private void PenetrationPass(BattleSnapshot s)
        {
            if (_o.Length < s.OwnCount) _o = new Sample[Math.Max(s.OwnCount, _o.Length * 2)];
            _no = 0;
            for (int i = 0; i < s.OwnCount; i++)
            {
                if (!IsOwnLineRegiment(in s.Own[i])) continue;
                FillOwn(ref _o[_no], in s.Own[i], in _axis);
                _no++;
            }
            _nofront = BuildFront(_o, _no, false, ref _oinf, ref _ofront);
            if (_nofront == 0) return;

            float clear2 = PenetrationClearMeters * PenetrationClearMeters;
            for (int i = 0; i < _n; i++)
            {
                ref Sample e = ref _s[i];
                if (!e.Visible || e.InMelee || (e.Type != 0 && e.Type != 1)) continue;
                if (!OwnTraceDepth(e.L, out float traceDepth)) continue;
                float behind = traceDepth - e.D;
                if (behind < PenetrationMinDepthMeters || behind > CounterstrokeMaxMeters) continue;
                bool clear = true;
                for (int k = 0; k < _nofront && clear; k++)
                {
                    float dx = _o[_ofront[k]].X - e.X, dz = _o[_ofront[k]].Z - e.Z;
                    clear = dx * dx + dz * dz > clear2;
                }
                if (!clear) continue;
                AddCandidate(OpeningKind.Penetration, e.X, e.Z, 0f, 0f, e.Conf, e.Id, false, default);
            }
        }

        /// <summary>
        /// The own trace: the depth of the own front line at a lateral position. Flat across a regiment's footprint,
        /// linear between two adjacent front-line regiments whose edges are no more than GapMinMeters apart; there is
        /// no trace across a larger gap or beyond the line's ends.
        /// </summary>
        private bool OwnTraceDepth(float lateral, out float depth)
        {
            for (int k = 0; k < _nofront; k++)
            {
                ref Sample a = ref _o[_ofront[k]];
                if (lateral >= a.L - a.W * 0.5f && lateral <= a.L + a.W * 0.5f)
                {
                    depth = a.D;
                    return true;
                }
            }
            for (int k = 0; k + 1 < _nofront; k++)
            {
                ref Sample a = ref _o[_ofront[k]];
                ref Sample b = ref _o[_ofront[k + 1]];
                if (lateral <= a.L || lateral >= b.L) continue;
                float edgeGap = (b.L - b.W * 0.5f) - (a.L + a.W * 0.5f);
                if (edgeGap > GapMinMeters)
                {
                    depth = 0f;
                    return false;
                }
                depth = a.D + (b.D - a.D) * ((lateral - a.L) / (b.L - a.L));
                return true;
            }
            depth = 0f;
            return false;
        }

        // ================================================================================ identity

        private void UpdateIdentity(double now)
        {
            if (_matched.Length < _openCount) _matched = new bool[_open.Length];
            for (int i = 0; i < _openCount; i++) _matched[i] = false;
            if (_candMatched.Length < _candCount) _candMatched = new bool[Math.Max(_candCount, _candMatched.Length * 2)];

            // 1. match each detection to last tick's opening of its kind within 100 m
            float match2 = OpeningMatchMeters * OpeningMatchMeters;
            for (int c = 0; c < _candCount; c++)
            {
                _candMatched[c] = false;
                ref Candidate cand = ref _cand[c];
                int best = -1;
                float bestD = float.MaxValue;
                for (int i = 0; i < _openCount; i++)
                {
                    if (_matched[i] || _open[i].Kind != cand.O.Kind) continue;
                    float dx = _open[i].AimX - cand.O.AimX, dz = _open[i].AimZ - cand.O.AimZ;
                    float d = dx * dx + dz * dz;
                    if (d <= match2 && d < bestD) { bestD = d; best = i; }
                }
                if (best < 0) continue;
                _matched[best] = true;
                _candMatched[c] = true;
                Opening o = cand.O;
                o.Id = _open[best].Id;
                o.FirstSeenHours = _open[best].FirstSeenHours;
                o.LastSeenHours = now;
                o.Claimed = _open[best].Claimed;
                _open[best] = o;
                _info[best] = new ClusterInfo { X = cand.ClusterX, Z = cand.ClusterZ, Value = cand.ClusterValue, Nearest = cand.ClusterNearest };
            }

            // 2. drop what was not seen for LostHours
            int kept = 0;
            for (int i = 0; i < _openCount; i++)
            {
                if (!_matched[i] && now - _open[i].LastSeenHours >= LostHours) continue;
                if (kept != i)
                {
                    _open[kept] = _open[i];
                    _info[kept] = _info[i];
                    _matched[kept] = _matched[i];
                }
                kept++;
            }
            _openCount = kept;

            // 3. add the new detections, dropping the lowest-confidence unconfirmed opening past MaxOpenings
            for (int c = 0; c < _candCount; c++)
            {
                if (_candMatched[c]) continue;
                ref Candidate cand = ref _cand[c];
                Opening o = cand.O;
                o.FirstSeenHours = now;
                o.LastSeenHours = now;
                if (_openCount == MaxOpenings && !MakeRoomFor(in o)) continue;
                o.Id = _nextId++;
                _open[_openCount] = o;
                _info[_openCount] = new ClusterInfo { X = cand.ClusterX, Z = cand.ClusterZ, Value = cand.ClusterValue, Nearest = cand.ClusterNearest };
                _openCount++;
            }

            // 4. confirm after continuous presence
            for (int i = 0; i < _openCount; i++)
                _open[i].Confirmed = now - _open[i].FirstSeenHours >= ConfirmHours;
        }

        // Past MaxOpenings the lowest-confidence unconfirmed opening goes (ties: the newest); a claimed opening never.
        // The new detection is itself unconfirmed: when it is the lowest it is the one dropped. Returns whether a
        // slot was freed for it.
        private bool MakeRoomFor(in Opening incoming)
        {
            int victim = -1;
            float lowest = incoming.Confidence01;
            double newest = incoming.FirstSeenHours;
            for (int i = 0; i < _openCount; i++)
            {
                if (_open[i].Confirmed || _open[i].Claimed) continue;
                bool lower = _open[i].Confidence01 < lowest;
                bool tie = _open[i].Confidence01 == lowest && _open[i].FirstSeenHours >= newest;
                if (lower || tie)
                {
                    victim = i;
                    lowest = _open[i].Confidence01;
                    newest = _open[i].FirstSeenHours;
                }
            }
            if (victim < 0) return false;
            for (int i = victim; i + 1 < _openCount; i++)
            {
                _open[i] = _open[i + 1];
                _info[i] = _info[i + 1];
            }
            _openCount--;
            return true;
        }

        // ================================================================================ small helpers

        internal static float BearingDegrees(float dx, float dz)
        {
            double deg = Math.Atan2(dx, dz) * (180.0 / Math.PI);
            if (deg < 0.0) deg += 360.0;
            return (float)deg;
        }

        private static float AngleBetween(float a, float b)
        {
            float d = Math.Abs(a - b) % 360f;
            return d > 180f ? 360f - d : d;
        }
    }
}
