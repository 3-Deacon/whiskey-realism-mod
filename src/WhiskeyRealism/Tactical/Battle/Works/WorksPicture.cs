using System;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tactical.Battle
{
    // BC14W requirement-works-picture and requirement-works-shape (spec § 7A). The works as one side knows them: segments of works samples
    // (regiments standing in works cover) and works points (the model pieces of every ObstacleLine the side has perceived), their ends, salients,
    // junctions and focal points, in the front frame (TacticalFrontAxis: lateral to the right of the heading, depth toward the enemy). Pure; no
    // Unity or vanilla types. Fog-honest by construction: it reads the snapshot's observations (EnemySamples.Accepts) and the points the runtime
    // reports as perceived, nothing else, so a hidden regiment and an unperceived piece change no segment, focal point or gate.

    /// <summary>The works points the side may count (the runtime's ObstacleLine pieces, or a fixture's): where each stands and whether it has been perceived.</summary>
    public interface IFortificationPoints
    {
        int Count { get; }
        void Get(int i, out float x, out float z);

        /// <summary>The side has perceived the piece (a probe of the perceived ground found it); it stays perceived for the battle.</summary>
        bool Perceived(int i);

        /// <summary>The piece stands inside the side's own deployment zone (computed by the runtime at enumeration).</summary>
        bool InOwnZone(int i);
    }

    /// <summary>What the posture pipeline, the contest, the recon and the guard ask of the works picture.</summary>
    public interface IWorksQuery
    {
        /// <summary>The point lies on a held segment: within its lateral extent plus <see cref="WorksRules.WorksEndMeters"/> and within <see cref="WorksRules.SecondLineMeters"/> of the profile there.</summary>
        bool OnHeldWorks(float x, float z);

        /// <summary>The point is in the works band of a held segment: within <see cref="WorksRules.WorksBandMeters"/> in front of its profile, or past it.</summary>
        bool InWorksBand(float x, float z);
    }

    public struct WorksSegment
    {
        public float LateralMin, LateralMax;
        /// <summary>Into the picture's profile arrays (held segments only; an empty segment has none).</summary>
        public int ProfileStart, ProfileCount;
        public float HeldStrength, MeanCover, MeanMorale;
        /// <summary>At least one holder stands in it.</summary>
        public bool Held;
        public bool MinEndAnchored, MaxEndAnchored;
    }

    public enum FocalKind : byte { Point = 0, Salient = 1, Junction = 2 }

    public struct FocalPoint
    {
        public float X, Z, Score;
        public FocalKind Kind;
        public int Segment;
    }

    /// <summary>
    /// One side's works picture, rebuilt whole per evaluation into pooled arrays (grow-only; a rebuild allocates nothing once they fit).
    /// <see cref="BuildEnemy"/> reads the works the side is up against, <see cref="BuildOwn"/> a Defender's own.
    /// </summary>
    public sealed class WorksPicture : IWorksQuery
    {
        /// <summary>Enemy bodies the junctions are made from (the evaluation's MaxEnemyBodies).</summary>
        public const int MaxBodies = 16;

        // ---- the items (holders first, then perceived works points) before they are segmented
        private float[] _ix = new float[256], _iz = new float[256], _ilat = new float[256], _idepth = new float[256];
        private int[] _iseg = new int[256];
        private int[] _order = new int[256];
        private float[] _key = new float[256];
        private int _itemCount;

        // ---- the holders (the works samples or own works regiments); holder h is item h
        private readonly float[] _hvalue = new float[WorksRules.MaxHolders], _hw = new float[WorksRules.MaxHolders];
        private readonly float[] _hcover = new float[WorksRules.MaxHolders], _hmorale = new float[WorksRules.MaxHolders];
        private readonly int[] _hseg = new int[WorksRules.MaxHolders];
        private int _holderCount;

        // ---- the holders sorted by lateral, for attributing a point to the works around it
        private readonly int[] _sorder = new int[WorksRules.MaxHolders];
        private readonly float[] _skey = new float[WorksRules.MaxHolders];

        // ---- the profile (one point per populated lateral bin of a held segment, in lateral order)
        private readonly float[] _plat = new float[WorksRules.MaxProfilePoints], _pdepth = new float[WorksRules.MaxProfilePoints];
        private readonly float[] _px = new float[WorksRules.MaxProfilePoints], _pz = new float[WorksRules.MaxProfilePoints];
        private readonly int[] _pbin = new int[WorksRules.MaxProfilePoints];
        private readonly float[] _psalience = new float[WorksRules.MaxProfilePoints];
        private readonly bool[] _papex = new bool[WorksRules.MaxProfilePoints];
        private int _profileCount;

        private readonly WorksSegment[] _segments = new WorksSegment[WorksRules.MaxSegments];
        private readonly int[] _segFirst = new int[WorksRules.MaxSegments], _segLast = new int[WorksRules.MaxSegments];
        private readonly FocalPoint[] _focal = new FocalPoint[WorksRules.MaxFocalPoints];
        private FocalPoint _best;
        private bool _hasBest;
        private float _bestDistance2;
        private TacticalFrontAxis _axis;

        // junction scratch: the bodies standing on held works, in lateral order
        private readonly int[] _bodyOrder = new int[MaxBodies];
        private readonly float[] _bodyKey = new float[MaxBodies];

        public int SegmentCount { get; private set; }
        public int HeldSegmentCount { get; private set; }
        public int OpenEnds { get; private set; }
        public int AnchoredEnds { get; private set; }
        public int SalientCount { get; private set; }
        public int JunctionCount { get; private set; }

        /// <summary>The focal points kept in <see cref="Focal"/> (at most <see cref="WorksRules.MaxFocalPoints"/>; the best is tracked across all of them).</summary>
        public int FocalCount { get; private set; }

        public int ProfileCount => _profileCount;

        /// <summary>The holders counted (regiments standing in works cover).</summary>
        public int WorksSamples => _holderCount;

        /// <summary>The perceived works points counted for this side's picture.</summary>
        public int WorksPointsCounted { get; private set; }

        /// <summary>Perceived works points past <see cref="WorksRules.MaxWorksPoints"/> that the last build did not keep.</summary>
        public int PointsDropped { get; private set; }

        /// <summary>Holders, segments, profile points or focal points another bound refused to keep in the last build (a bounded picture reports the loss).</summary>
        public int Dropped { get; private set; }

        public bool HasHeldWorks => HeldSegmentCount > 0;

        public ref readonly WorksSegment Segment(int i) => ref _segments[i];

        /// <summary>The i-th profile point of the picture (held segments in lateral order, each in lateral order).</summary>
        public void Profile(int i, out float lateral, out float depth, out float x, out float z)
        {
            lateral = _plat[i];
            depth = _pdepth[i];
            x = _px[i];
            z = _pz[i];
        }

        public ref readonly FocalPoint Focal(int i) => ref _focal[i];

        /// <summary>The focal point with the lowest score (ties to the one nearer the own centroid, then the lower lateral); false when there is no held works.</summary>
        public bool TryFocal(out FocalPoint best)
        {
            best = _best;
            return _hasBest;
        }

        public void Clear()
        {
            SegmentCount = 0;
            HeldSegmentCount = 0;
            OpenEnds = 0;
            AnchoredEnds = 0;
            SalientCount = 0;
            JunctionCount = 0;
            FocalCount = 0;
            WorksPointsCounted = 0;
            PointsDropped = 0;
            Dropped = 0;
            _itemCount = 0;
            _holderCount = 0;
            _profileCount = 0;
            _hasBest = false;
            _best = default(FocalPoint);
            _axis = TacticalFrontAxis.None;
        }

        // ---- the enemy's works --------------------------------------------------------------------------------------

        /// <summary>
        /// The works the side is up against, from the observations it holds and the works points it has perceived. A works sample is an enemy
        /// sample (<see cref="EnemySamples.Accepts"/>) last seen Standing in works cover above the vanilla threshold. A perceived works point counts
        /// for the enemy when a works sample stands within <see cref="WorksRules.WorksGapMeters"/> of it, or, with none that near, when it lies
        /// outside the side's own deployment zone. <paramref name="bodyX"/>/<paramref name="bodyZ"/> are the enemy's bodies (the single-link
        /// clusters of its samples) whose neighbours on the works make the junctions.
        /// </summary>
        public void BuildEnemy(BattleSnapshot s, in TacticalFrontAxis axis, ReadOnlySpan<float> bodyX, ReadOnlySpan<float> bodyZ, int bodyCount,
            IFortificationPoints points, TerrainPicture picture)
        {
            Clear();
            if (s == null || !axis.IsValid) return;
            _axis = axis;
            EnemyObservation[] obs = s.Observations;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (!EnemySamples.Accepts(in obs[k]) || !IsWorksSample(in obs[k])) continue;
                float strength = obs[k].State == ObservationState.Visible ? obs[k].Strength : obs[k].Strength * obs[k].Confidence01;
                if (!(strength > 0f)) continue;
                AddHolder(obs[k].X, obs[k].Z, strength, obs[k].Morale > 0f ? obs[k].Morale : 1f, obs[k].CoverValue);
            }
            AddPoints(points, ownRule: false);
            Finish(bodyX, bodyZ, bodyCount, picture);
        }

        /// <summary>
        /// A Defender's own works: its regiments standing in works cover (own data, no fog) and the works points it owns, a piece being its own
        /// when an own works regiment stands within <see cref="WorksRules.WorksGapMeters"/> of it or it lies inside the own deployment zone.
        /// </summary>
        public void BuildOwn(BattleSnapshot s, in TacticalFrontAxis axis, IFortificationPoints points, TerrainPicture picture)
        {
            Clear();
            if (s == null || !axis.IsValid) return;
            _axis = axis;
            UnitRecord[] own = s.Own;
            const UnitFlags live = UnitFlags.Alive | UnitFlags.Active;
            for (int i = 0; i < s.OwnCount; i++)
            {
                UnitFlags f = own[i].Flags;
                if ((f & live) != live || (f & (UnitFlags.IsGroup | UnitFlags.Routed | UnitFlags.PlayerOwned)) != 0) continue;
                if (own[i].UnitTyp < 0 || own[i].UnitTyp > 2) continue;
                // Standing: a regiment with no path of its own is holding its ground.
                if (own[i].RegimentPaths > 0 || !(own[i].CoverValue > CoveredCrossings.MinCoverValue) || !WorksRules.IsWorksCover(own[i].CoverObject)) continue;
                if (!(own[i].Strength > 0)) continue;
                AddHolder(own[i].X, own[i].Z, own[i].Strength, own[i].Morale > 0f ? own[i].Morale : 1f, own[i].CoverValue);
            }
            AddPoints(points, ownRule: true);
            Finish(default(ReadOnlySpan<float>), default(ReadOnlySpan<float>), 0, picture);
        }

        private static bool IsWorksSample(in EnemyObservation o)
            => o.Standing && o.CoverValue > CoveredCrossings.MinCoverValue && WorksRules.IsWorksCover(o.CoverObject);

        // Holders are added before any point, so holder h is item h.
        private void AddHolder(float x, float z, float strength, float morale, float cover)
        {
            if (_holderCount >= WorksRules.MaxHolders) { Dropped++; return; }
            int h = _holderCount++;
            _hw[h] = strength;
            _hcover[h] = cover;
            _hmorale[h] = morale;
            _hvalue[h] = strength * morale * (1f + cover);
            _hseg[h] = -1;
            AddItem(x, z);
        }

        private void AddItem(float x, float z)
        {
            if (_itemCount == _ix.Length) GrowItems();
            int i = _itemCount++;
            _ix[i] = x;
            _iz[i] = z;
            _ilat[i] = _axis.LateralOf(x, z);
            _idepth[i] = _axis.DepthOf(x, z);
            _iseg[i] = -1;
        }

        private void GrowItems()
        {
            int size = _ix.Length * 2;
            Array.Resize(ref _ix, size);
            Array.Resize(ref _iz, size);
            Array.Resize(ref _ilat, size);
            Array.Resize(ref _idepth, size);
            Array.Resize(ref _iseg, size);
            Array.Resize(ref _order, size);
            Array.Resize(ref _key, size);
        }

        // Works points the side may count: perceived, and the enemy's (or, for the Defender, its own) by the holder rule.
        private void AddPoints(IFortificationPoints points, bool ownRule)
        {
            if (points == null) return;
            for (int h = 0; h < _holderCount; h++) { _sorder[h] = h; _skey[h] = _ilat[h]; }
            Array.Sort(_skey, _sorder, 0, _holderCount);
            float gap2 = WorksRules.WorksGapMeters * WorksRules.WorksGapMeters;
            for (int i = 0; i < points.Count; i++)
            {
                if (!points.Perceived(i)) continue;
                points.Get(i, out float x, out float z);
                if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(z) || float.IsInfinity(z)) continue;
                // A holder near it, or, with none that near, the zone decides: the enemy's piece lies outside the own zone, an own piece inside.
                bool counts = HolderWithinGap(_axis.LateralOf(x, z), x, z, gap2) || points.InOwnZone(i) == ownRule;
                if (!counts) continue;
                if (_itemCount - _holderCount >= WorksRules.MaxWorksPoints) { PointsDropped++; continue; }
                AddItem(x, z);
                WorksPointsCounted++;
            }
        }

        private bool HolderWithinGap(float lateral, float x, float z, float gap2)
        {
            float gap = WorksRules.WorksGapMeters;
            int lo = 0, hi = _holderCount;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_skey[mid] < lateral - gap) lo = mid + 1; else hi = mid;
            }
            for (int k = lo; k < _holderCount && _skey[k] <= lateral + gap; k++)
            {
                int h = _sorder[k];
                float dx = _ix[h] - x, dz = _iz[h] - z;
                if (dx * dx + dz * dz <= gap2) return true;
            }
            return false;
        }

        // ---- shape ---------------------------------------------------------------------------------------------------

        private void Finish(ReadOnlySpan<float> bodyX, ReadOnlySpan<float> bodyZ, int bodyCount, TerrainPicture picture)
        {
            BuildSegments();
            BuildProfiles();
            MarkEnds(picture);
            FindSalients();
            ScoreFocalPoints();
            AddJunctions(bodyX, bodyZ, bodyCount);
        }

        // Items sorted by lateral; neighbours within the gap (inclusive) join one segment.
        private void BuildSegments()
        {
            int n = _itemCount;
            for (int i = 0; i < n; i++) { _order[i] = i; _key[i] = _ilat[i]; }
            Array.Sort(_key, _order, 0, n);
            SegmentCount = 0;
            for (int k = 0; k < n; k++)
            {
                int item = _order[k];
                bool join = k > 0 && _ilat[item] - _ilat[_order[k - 1]] <= WorksRules.WorksGapMeters;
                if (!join)
                {
                    if (SegmentCount >= WorksRules.MaxSegments) { Dropped += n - k; break; }
                    _segments[SegmentCount] = new WorksSegment { LateralMin = _ilat[item], LateralMax = _ilat[item] };
                    _segFirst[SegmentCount] = k;
                    SegmentCount++;
                }
                int seg = SegmentCount - 1;
                _segLast[seg] = k;
                _segments[seg].LateralMax = _ilat[item];
                _iseg[item] = seg;
                if (item < _holderCount) _hseg[item] = seg;
            }
            // Holders: strength, the strength-weighted cover and morale, and whether the segment is held.
            for (int h = 0; h < _holderCount; h++)
            {
                int seg = _hseg[h];
                if (seg < 0) continue;
                _segments[seg].HeldStrength += _hw[h];
                _segments[seg].MeanCover += _hcover[h] * _hw[h];
                _segments[seg].MeanMorale += _hmorale[h] * _hw[h];
                _segments[seg].Held = true;
            }
            for (int i = 0; i < SegmentCount; i++)
            {
                if (!_segments[i].Held) continue;
                HeldSegmentCount++;
                float w = _segments[i].HeldStrength;
                _segments[i].MeanCover = w > 0f ? _segments[i].MeanCover / w : 0f;
                _segments[i].MeanMorale = w > 0f ? _segments[i].MeanMorale / w : 0f;
            }
        }

        // The depth profile of each held segment: per FocalSampleMeters lateral bin, the works point or sample nearest the own side. A point up
        // to SecondLineMeters behind it in the same bin is on the segment but does not move the profile.
        private void BuildProfiles()
        {
            for (int sg = 0; sg < SegmentCount; sg++)
            {
                if (!_segments[sg].Held) continue;
                _segments[sg].ProfileStart = _profileCount;
                int k = _segFirst[sg];
                while (k <= _segLast[sg])
                {
                    int bin = (int)Math.Floor(_ilat[_order[k]] / WorksRules.FocalSampleMeters);
                    int bestItem = _order[k];
                    int next = k + 1;
                    while (next <= _segLast[sg] && (int)Math.Floor(_ilat[_order[next]] / WorksRules.FocalSampleMeters) == bin)
                    {
                        if (_idepth[_order[next]] < _idepth[bestItem]) bestItem = _order[next];
                        next++;
                    }
                    if (_profileCount >= WorksRules.MaxProfilePoints) Dropped++;
                    else
                    {
                        int p = _profileCount++;
                        _plat[p] = _ilat[bestItem];
                        _pdepth[p] = _idepth[bestItem];
                        _px[p] = _ix[bestItem];
                        _pz[p] = _iz[bestItem];
                        _pbin[p] = bin;
                        _psalience[p] = 0f;
                        _papex[p] = false;
                    }
                    k = next;
                }
                _segments[sg].ProfileCount = _profileCount - _segments[sg].ProfileStart;
            }
        }

        // An end is anchored when the terrain anchors it outward along the front axis (TacticalTerrainLinePlanner.FlankAnchored), or another
        // held segment continues beyond it: that segment's on-segment reach (its extent less WorksEndMeters) begins within WorksGapMeters of the
        // end. Works 150 apart already join one segment, so the continuing segment is the next one, up to 200 units off.
        private void MarkEnds(TerrainPicture picture)
        {
            int previous = -1;
            for (int sg = 0; sg < SegmentCount; sg++)
            {
                if (!_segments[sg].Held || _segments[sg].ProfileCount == 0) continue;
                ref WorksSegment seg = ref _segments[sg];
                int first = seg.ProfileStart, last = seg.ProfileStart + seg.ProfileCount - 1;
                seg.MinEndAnchored = TacticalTerrainLinePlanner.FlankAnchored(picture, _px[first], _pz[first], -_axis.RightX, -_axis.RightZ);
                seg.MaxEndAnchored = TacticalTerrainLinePlanner.FlankAnchored(picture, _px[last], _pz[last], _axis.RightX, _axis.RightZ);
                if (previous >= 0 && seg.LateralMin - _segments[previous].LateralMax - WorksRules.WorksEndMeters <= WorksRules.WorksGapMeters)
                {
                    seg.MinEndAnchored = true;
                    _segments[previous].MaxEndAnchored = true;
                }
                previous = sg;
            }
            for (int sg = 0; sg < SegmentCount; sg++)
            {
                if (!_segments[sg].Held || _segments[sg].ProfileCount == 0) continue;
                if (_segments[sg].MinEndAnchored) AnchoredEnds++; else OpenEnds++;
                if (_segments[sg].MaxEndAnchored) AnchoredEnds++; else OpenEnds++;
            }
        }

        // A profile point that stands at least SalientDepthMeters nearer the own side than the chord between the profile points 300 units either
        // side of it is a salient apex; in a run of such points the nearest one is the apex.
        private void FindSalients()
        {
            int reach = (int)(WorksRules.FrontageReachMeters / WorksRules.FocalSampleMeters);
            for (int sg = 0; sg < SegmentCount; sg++)
            {
                if (!_segments[sg].Held) continue;
                int start = _segments[sg].ProfileStart, end = start + _segments[sg].ProfileCount;
                for (int p = start; p < end; p++)
                {
                    int before = ProfileAtBin(start, end, _pbin[p] - reach), after = ProfileAtBin(start, end, _pbin[p] + reach);
                    _psalience[p] = before >= 0 && after >= 0 ? (_pdepth[before] + _pdepth[after]) * 0.5f - _pdepth[p] : 0f;
                }
                int run = -1;
                for (int p = start; p < end; p++)
                {
                    if (_psalience[p] >= WorksRules.SalientDepthMeters)
                    {
                        if (run < 0 || _psalience[p] > _psalience[run]) run = p;
                        continue;
                    }
                    if (run >= 0) { _papex[run] = true; SalientCount++; run = -1; }
                }
                if (run >= 0) { _papex[run] = true; SalientCount++; }
            }
        }

        private int ProfileAtBin(int start, int end, int bin)
        {
            for (int p = start; p < end; p++) if (_pbin[p] == bin) return p;
            return -1;
        }

        // Every profile point of a held segment, with the salient apexes marked, scores holders / frontage x shape; the lowest is the focal point.
        private void ScoreFocalPoints()
        {
            FocalCount = 0;
            for (int sg = 0; sg < SegmentCount; sg++)
            {
                if (!_segments[sg].Held) continue;
                int start = _segments[sg].ProfileStart, end = start + _segments[sg].ProfileCount;
                for (int p = start; p < end; p++)
                    Offer(sg, _px[p], _pz[p], _plat[p], _papex[p] ? FocalKind.Salient : FocalKind.Point);
            }
        }

        private void Offer(int segment, float x, float z, float lateral, FocalKind kind)
        {
            float shape = kind == FocalKind.Salient ? WorksRules.SalientFactor : (kind == FocalKind.Junction ? WorksRules.JunctionFactor : 1f);
            float score = HoldersValue(segment, x, z) / Frontage(segment, lateral) * shape;
            var point = new FocalPoint { X = x, Z = z, Score = score, Kind = kind, Segment = segment };
            if (FocalCount < _focal.Length) _focal[FocalCount++] = point;
            else Dropped++;
            float dx = x - _axis.OriginX, dz = z - _axis.OriginZ;
            float distance2 = dx * dx + dz * dz;
            float bestLateral = _hasBest ? _axis.LateralOf(_best.X, _best.Z) : 0f;
            if (!_hasBest || score < _best.Score || (score == _best.Score && (distance2 < _bestDistance2 || (distance2 == _bestDistance2 && lateral < bestLateral))))
            {
                _best = point;
                _bestDistance2 = distance2;
                _hasBest = true;
            }
        }

        // The sum of strength x morale x (1 + cover) over the segment's holders within OddsRadiusMeters (BC13's 300, inclusive).
        private float HoldersValue(int segment, float x, float z)
        {
            float r2 = WorksRules.FrontageReachMeters * WorksRules.FrontageReachMeters, sum = 0f;
            for (int h = 0; h < _holderCount; h++)
            {
                if (_hseg[h] != segment) continue;
                float dx = _ix[h] - x, dz = _iz[h] - z;
                if (dx * dx + dz * dz <= r2) sum += _hvalue[h];
            }
            return sum;
        }

        // The works frontage within 300 laterally of the point: the segment's extent clipped to the reach (at least one bin, for a lone sample).
        private float Frontage(int segment, float lateral)
        {
            float lo = Math.Max(_segments[segment].LateralMin, lateral - WorksRules.FrontageReachMeters);
            float hi = Math.Min(_segments[segment].LateralMax, lateral + WorksRules.FrontageReachMeters);
            return Math.Max(hi - lo, WorksRules.FocalSampleMeters);
        }

        // The midpoint between two enemy bodies that are adjacent in lateral order among the bodies whose centroids lie on one held segment.
        private void AddJunctions(ReadOnlySpan<float> bodyX, ReadOnlySpan<float> bodyZ, int bodyCount)
        {
            int n = Math.Min(Math.Min(bodyCount, MaxBodies), Math.Min(bodyX.Length, bodyZ.Length));
            int on = 0;
            for (int b = 0; b < n; b++)
            {
                int seg = SegmentOf(bodyX[b], bodyZ[b]);
                if (seg < 0) continue;
                _bodyOrder[on] = b;
                _bodyKey[on] = _axis.LateralOf(bodyX[b], bodyZ[b]);
                on++;
            }
            if (on < 2) return;
            Array.Sort(_bodyKey, _bodyOrder, 0, on);
            for (int k = 0; k + 1 < on; k++)
            {
                int a = _bodyOrder[k], c = _bodyOrder[k + 1];
                int segA = SegmentOf(bodyX[a], bodyZ[a]);
                if (segA < 0 || segA != SegmentOf(bodyX[c], bodyZ[c])) continue;
                float mx = (bodyX[a] + bodyX[c]) * 0.5f, mz = (bodyZ[a] + bodyZ[c]) * 0.5f;
                JunctionCount++;
                Offer(segA, mx, mz, _axis.LateralOf(mx, mz), FocalKind.Junction);
            }
        }

        // ---- queries -------------------------------------------------------------------------------------------------

        /// <summary>The held segment a point lies on (within its extent plus WorksEndMeters and within SecondLineMeters of the profile there), or -1.</summary>
        public int SegmentOf(float x, float z)
        {
            OnHeldWorks(x, z, out int segment);
            return segment;
        }

        public bool OnHeldWorks(float x, float z) => OnHeldWorks(x, z, out _);

        public bool OnHeldWorks(float x, float z, out int segment)
        {
            segment = -1;
            if (HeldSegmentCount == 0 || !_axis.IsValid) return false;
            float lateral = _axis.LateralOf(x, z), depth = _axis.DepthOf(x, z);
            for (int sg = 0; sg < SegmentCount; sg++)
            {
                if (!_segments[sg].Held || _segments[sg].ProfileCount == 0) continue;
                if (lateral < _segments[sg].LateralMin - WorksRules.WorksEndMeters || lateral > _segments[sg].LateralMax + WorksRules.WorksEndMeters) continue;
                if (Math.Abs(depth - ProfileDepthAt(sg, lateral)) > WorksRules.SecondLineMeters) continue;
                segment = sg;
                return true;
            }
            return false;
        }

        public bool InWorksBand(float x, float z)
        {
            if (HeldSegmentCount == 0 || !_axis.IsValid) return false;
            float lateral = _axis.LateralOf(x, z), depth = _axis.DepthOf(x, z);
            for (int sg = 0; sg < SegmentCount; sg++)
            {
                if (!_segments[sg].Held || _segments[sg].ProfileCount == 0) continue;
                if (lateral < _segments[sg].LateralMin - WorksRules.WorksEndMeters || lateral > _segments[sg].LateralMax + WorksRules.WorksEndMeters) continue;
                if (depth >= ProfileDepthAt(sg, lateral) - WorksRules.WorksBandMeters) return true;
            }
            return false;
        }

        // The profile depth at a lateral: the nearest populated bin's.
        private float ProfileDepthAt(int segment, float lateral)
        {
            int start = _segments[segment].ProfileStart, end = start + _segments[segment].ProfileCount;
            int lo = start, hi = end;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_plat[mid] < lateral) lo = mid + 1; else hi = mid;
            }
            int a = lo > start ? lo - 1 : lo, b = lo < end ? lo : lo - 1;
            return Math.Abs(_plat[a] - lateral) <= Math.Abs(_plat[b] - lateral) ? _pdepth[a] : _pdepth[b];
        }
    }

    /// <summary>One ObstacleLine model piece, as the runtime read it: the instance id of its model object, where it stands, how complete it is and whether it lies in the side's own zone.</summary>
    public struct WorksPiece
    {
        public int InstanceId;
        public float X, Z, Completion;
        public bool InOwnZone;
    }

    /// <summary>
    /// The side's works points (requirement-works-picture): the pieces of every ObstacleLine, re-read by <see cref="Merge"/> and perceived by
    /// <see cref="ProbeStep"/>. A piece's identity is the instance id of its model object, so its perceived stamp survives a re-read; a piece
    /// absent from the re-read (destroyed) is dropped, one below <see cref="WorksRules.WorksMinCompletion"/> is not kept, and past
    /// <see cref="WorksRules.MaxWorksPoints"/> the pieces farthest from the own centroid are dropped and counted. Pooled; per side, in BattleScope.
    /// </summary>
    public sealed class WorksPointTable : IFortificationPoints
    {
        private struct Entry
        {
            public int InstanceId;
            public float X, Z;
            public bool InOwnZone, Perceived;
            /// <summary>The probe order: the merge that first saw the piece, then the last probe that found it unperceived (oldest first).</summary>
            public long Stamp;
        }

        private Entry[] _entries = new Entry[256];
        private int _count;
        private long _clock;
        private readonly System.Collections.Generic.Dictionary<int, int> _byId = new System.Collections.Generic.Dictionary<int, int>(256);
        private Entry[] _next = new Entry[256];
        private float[] _distance = new float[256];
        private int[] _rank = new int[256];

        public int Count => _count;

        /// <summary>Pieces the last <see cref="Merge"/> dropped for want of room (the farthest from the own centroid).</summary>
        public int Dropped { get; private set; }

        public void Get(int i, out float x, out float z)
        {
            x = _entries[i].X;
            z = _entries[i].Z;
        }

        public bool Perceived(int i) => _entries[i].Perceived;

        public bool InOwnZone(int i) => _entries[i].InOwnZone;

        /// <summary>
        /// One re-read of the pieces: matched to the known ones by instance id (a stamp survives), the absent dropped, a piece below
        /// <see cref="WorksRules.WorksMinCompletion"/> not kept, and past <see cref="WorksRules.MaxWorksPoints"/> the farthest from
        /// (<paramref name="ownX"/>, <paramref name="ownZ"/>) dropped and counted.
        /// </summary>
        public void Merge(ReadOnlySpan<WorksPiece> pieces, float ownX, float ownZ)
        {
            _clock++;
            int kept = 0;
            if (_next.Length < pieces.Length)
            {
                int size = Math.Max(pieces.Length, _next.Length * 2);
                _next = new Entry[size];
                _distance = new float[size];
                _rank = new int[size];
            }
            for (int i = 0; i < pieces.Length; i++)
            {
                if (!(pieces[i].Completion >= WorksRules.WorksMinCompletion)) continue;
                Entry entry;
                if (pieces[i].InstanceId != 0 && _byId.TryGetValue(pieces[i].InstanceId, out int known)) entry = _entries[known];
                else entry = new Entry { InstanceId = pieces[i].InstanceId, Stamp = _clock };
                entry.X = pieces[i].X;
                entry.Z = pieces[i].Z;
                entry.InOwnZone = pieces[i].InOwnZone;
                _next[kept++] = entry;
            }
            Dropped = 0;
            if (kept > WorksRules.MaxWorksPoints)
            {
                for (int i = 0; i < kept; i++)
                {
                    float dx = _next[i].X - ownX, dz = _next[i].Z - ownZ;
                    _distance[i] = dx * dx + dz * dz;
                    _rank[i] = i;
                }
                Array.Sort(_distance, _rank, 0, kept);
                Dropped = kept - WorksRules.MaxWorksPoints;
                // Keep the nearest MaxWorksPoints, in their read order.
                Array.Sort(_rank, 0, WorksRules.MaxWorksPoints);
                for (int i = 0; i < WorksRules.MaxWorksPoints; i++) _next[i] = _next[_rank[i]];
                kept = WorksRules.MaxWorksPoints;
            }
            if (_entries.Length < kept) _entries = new Entry[Math.Max(kept, _entries.Length * 2)];
            Array.Copy(_next, _entries, kept);
            _count = kept;
            _byId.Clear();
            for (int i = 0; i < _count; i++) if (_entries[i].InstanceId != 0) _byId[_entries[i].InstanceId] = i;
        }

        /// <summary>
        /// Probes at most min(<paramref name="budget"/>, <see cref="WorksRules.MaxWorksProbesPerTick"/>) pieces the side has not perceived yet,
        /// oldest first (by merge, then by the last probe that missed), through the perceived ground; a perceived piece stays perceived.
        /// Returns how many were probed.
        /// </summary>
        public int ProbeStep(IPerceivedGround ground, int budget)
        {
            if (ground == null || _count == 0) return 0;
            int allowed = Math.Min(budget, WorksRules.MaxWorksProbesPerTick);
            int probed = 0;
            for (; probed < allowed; probed++)
            {
                int pick = -1;
                for (int i = 0; i < _count; i++)
                {
                    if (_entries[i].Perceived) continue;
                    if (pick < 0 || _entries[i].Stamp < _entries[pick].Stamp) pick = i;
                }
                if (pick < 0) break;
                _clock++;
                _entries[pick].Stamp = _clock;
                _entries[pick].Perceived = ground.IsPerceived(_entries[pick].X, _entries[pick].Z);
            }
            return probed;
        }
    }
}

