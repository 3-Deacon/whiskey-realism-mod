using System;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    // BC14 requirement-leg-cost: the cost of a straight leg over BattleTerrainGrid, shared by the per-battle link table (RiverRegions)
    // and the feasibility evaluation's legs. The grid is sampled every StepMeters along the segment (BattleTerrainGrid.SampleLine's
    // samples: the start, then every whole step up to the last one before the end); sample i prices the stretch that starts at it, and
    // the last stretch is pro-rated to what remains of the leg, so a leg of 1,000 units over road (cost 1) costs exactly 1,000.
    //
    // Open river (BattleTerrainGrid.RiverId, not frozen): a sample within CrossingLinkMeters of a crossing the leg starts or ends at is
    // that crossing's footprint (the leg is about to ford or bridge it) and is priced at the crossing's own id. Any other open-river
    // sample is a bend of a river the leg skirts; it is priced at the swamp cost as a detour proxy and the leg is marked estimated until
    // a NavMesh result replaces it. On a frozen map the river is crossed at its own table cost.
    internal static class LegSampler
    {
        public const float StepMeters = 10f;

        /// <summary>Cost units (speed-independent): metres times the terrain's speed cost, summed along the leg.</summary>
        public static float Cost(BattleTerrainGrid g, float x0, float z0, float x1, float z1,
            bool footprintA, float ax, float az, int crossingIdA,
            bool footprintB, float bx, float bz, int crossingIdB,
            out bool estimated)
        {
            estimated = false;
            float dx = x1 - x0, dz = z1 - z0;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            int count = (int)Math.Floor(d / StepMeters) + 1;
            float ux = d > 0f ? dx / d : 0f;
            float uz = d > 0f ? dz / d : 0f;
            float r2 = RiverRegions.CrossingLinkMeters * RiverRegions.CrossingLinkMeters;
            int swampCost = g.SpeedCost(BattleTerrainGrid.SwampId);
            float total = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i * StepMeters;
                float px = x0 + ux * t, pz = z0 + uz * t;
                int id = g.IdAt(px, pz);
                float seg = i < count - 1 ? StepMeters : d - (count - 1) * StepMeters;
                int cost;
                if (id == BattleTerrainGrid.RiverId && !g.Frozen)
                {
                    float ex = px - ax, ez = pz - az;
                    float fx = px - bx, fz = pz - bz;
                    if (footprintA && ex * ex + ez * ez <= r2) cost = g.SpeedCost(crossingIdA);
                    else if (footprintB && fx * fx + fz * fz <= r2) cost = g.SpeedCost(crossingIdB);
                    else
                    {
                        cost = swampCost;
                        estimated = true;
                    }
                }
                else
                {
                    cost = g.SpeedCost(id);
                }
                total += seg * cost;
            }
            return total;
        }
    }

    // BC14 requirement-river-regions and requirement-link-legs: the map's land regions, separated by open river, and the crossings
    // (fords and bridges, vanilla's own list) that link them. One instance per battle (RiverRegionsRuntime owns it, BattleScope
    // resets it), built stepwise after TerrainPicture.Ready inside the picture runtime's step budget:
    //   stage 1  one lattice row per unit: first pass of a 4-connected two-pass labelling (union-find on provisional labels);
    //   stage 2  one row per unit: relabel to final region ids, in lattice order;
    //   stage 3  one listed crossing per unit: the regions with land within CrossingLinkMeters of it; two or more make a link;
    //   stage 4  one link pair per unit: the cost of the leg between two links that share a region (requirement-leg-cost).
    // Ready only when all four are done. Until then every consumer treats the map as unknown.
    //
    // The lattice is the regions' own: BattleTerrainGrid cells sampled in blocks of TerrainPicture.SampleMeters (5 units), a block
    // counted as water when any of its four quarter-points reads open river, ford or bridge (the picture's cell id prefers water the
    // same way), so a river narrower than a block still separates the banks. Open river (4) splits; a ford (5) or bridge (6) cell is
    // not land either, or the crossing would join the banks it exists to link. A frozen map is one region with no links.
    public sealed class RiverRegions
    {
        public const float CrossingLinkMeters = 60f;
        public const int MaxRegions = 64, MaxCrossings = 256, MaxRoutedLinks = 64;
        public const int MaxRegionsPerLink = 4;

        private const float BlockMeters = TerrainPicture.SampleMeters;
        private const int StageNew = 0, StageRows = 1, StageRelabel = 2, StageLinks = 3, StageTable = 4, StageReady = 5;
        private const string TruncatedKey = "tactical-battle-core:river-regions-truncated";

        private int _stage = StageNew;
        private BattleTerrainGrid _grid;
        private float _sizeX, _sizeZ;
        private int _nx, _nz;

        // labelling
        private int[] _prov;                // provisional label per block, 0 for water (freed after stage 2)
        private int[] _uf;                  // union-find over provisional labels (freed after stage 2)
        private int[] _rootRegion;          // provisional root -> final region + 1, -1 dropped (freed after stage 2)
        private ushort[] _final;            // final region + 1 per block, 0 for water or a dropped region
        private int _provCount;
        private int _row;
        private int _regionCount;
        private bool _regionsTruncated;

        // links (stage 3)
        private int _crossingCursor;
        private int _linkCount;
        private int _crossingTotal;
        private bool _linksTruncated;
        private float[] _linkX, _linkZ;
        private int[] _linkGridIndex, _linkCrossingId, _linkRegionCount;
        private int[] _linkRegions;         // MaxRegionsPerLink per link

        // link table (stage 4)
        private int _routed;
        private float[] _legCost;           // _routed x _routed, NaN when the two share no region
        private bool[] _legEstimated;
        private int _pairA, _pairB;

        public bool Ready => _stage == StageReady;

        /// <summary>Units of work done by all Step calls and by the last one (a lattice row, a listed crossing or a link pair is a unit).</summary>
        internal int TotalUnits { get; private set; }
        internal int UnitsLastStep { get; private set; }

        public bool Frozen { get; private set; }
        public bool Failed { get; private set; }
        public int RegionCount => _regionCount;
        public int LinkCount => _linkCount;

        /// <summary>Every listed crossing the build looked at, links and crossings inside one region alike.</summary>
        public int CrossingTotal => _crossingTotal;

        public bool RegionsTruncated => _regionsTruncated;
        public bool LinksTruncated => _linksTruncated;

        /// <summary>The region of a world point; -1 on open river or any crossing block, outside the map, in a dropped region or before the build is Ready.</summary>
        public int RegionAt(float x, float z)
        {
            if (_stage != StageReady) return -1;
            if (Frozen) return InMap(x, z) ? 0 : -1;
            int ix = (int)Math.Floor(x / BlockMeters), iz = (int)Math.Floor((z + _sizeZ) / BlockMeters);
            if (ix < 0 || iz < 0 || ix >= _nx || iz >= _nz) return -1;
            return _final[iz * _nx + ix] - 1;
        }

        /// <summary>Link <paramref name="link"/>'s crossing point, its index in the grid's crossing list and its id (ford 5 or bridge 6). Returns how many regions it joins; 0 when out of range.</summary>
        public int LinkCrossing(int link, out float x, out float z, out int gridCrossingIndex, out int crossingId)
        {
            if (link < 0 || link >= _linkCount)
            {
                x = z = 0f;
                gridCrossingIndex = -1;
                crossingId = 0;
                return 0;
            }
            x = _linkX[link];
            z = _linkZ[link];
            gridCrossingIndex = _linkGridIndex[link];
            crossingId = _linkCrossingId[link];
            return _linkRegionCount[link];
        }

        /// <summary>The regions the link joins (at most four), written to <paramref name="regions"/>; returns how many.</summary>
        public int RegionsOfLink(int link, Span<int> regions)
        {
            if (link < 0 || link >= _linkCount) return 0;
            int n = Math.Min(_linkRegionCount[link], regions.Length);
            for (int i = 0; i < n; i++) regions[i] = _linkRegions[link * MaxRegionsPerLink + i];
            return n;
        }

        /// <summary>Whether the link takes part in the link table and the path search (the first MaxRoutedLinks links).</summary>
        public bool IsRouted(int link) => link >= 0 && link < _routed;

        /// <summary>Whether the link joins the region.</summary>
        public bool LinkTouches(int link, int region)
        {
            if (link < 0 || link >= _linkCount) return false;
            int n = _linkRegionCount[link];
            for (int i = 0; i < n; i++) if (_linkRegions[link * MaxRegionsPerLink + i] == region) return true;
            return false;
        }

        /// <summary>
        /// The nearest land block centre to a point that is not on land itself (a rect anchor the projection laid on open river, a unit
        /// standing on a ford or bridge deck). A point already on land is returned as it is. At equal distance a block in
        /// <paramref name="preferRegion"/> wins (the body's own bank). False when no land lies within <see cref="SnapReachMeters"/>
        /// or the regions are not Ready.
        /// </summary>
        public bool TrySnapToLand(float x, float z, int preferRegion, out float snapX, out float snapZ)
        {
            snapX = x;
            snapZ = z;
            if (_stage != StageReady) return false;
            if (RegionAt(x, z) >= 0) return true;
            if (Frozen)
            {
                snapX = Math.Max(0f, Math.Min(_sizeX, x));
                snapZ = Math.Max(-_sizeZ, Math.Min(0f, z));
                return true;
            }
            int cx = (int)Math.Floor(x / BlockMeters), cz = (int)Math.Floor((z + _sizeZ) / BlockMeters);
            int maxRing = (int)Math.Ceiling(SnapReachMeters / BlockMeters);
            float best2 = float.MaxValue;
            bool bestPreferred = false;
            int foundRing = -1;
            for (int ring = 1; ring <= maxRing + 1; ring++)
            {
                if (foundRing >= 0 && ring > foundRing + 1) break;     // the ring after the first hit can hold a nearer corner
                for (int dz = -ring; dz <= ring; dz++)
                {
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Math.Abs(dx) != ring && Math.Abs(dz) != ring) continue;
                        int ix = cx + dx, iz = cz + dz;
                        if (ix < 0 || iz < 0 || ix >= _nx || iz >= _nz) continue;
                        int region = _final[iz * _nx + ix] - 1;
                        if (region < 0) continue;
                        float px = (ix + 0.5f) * BlockMeters, pz = -_sizeZ + (iz + 0.5f) * BlockMeters;
                        float d2 = (px - x) * (px - x) + (pz - z) * (pz - z);
                        bool preferred = region == preferRegion;
                        bool tie = Math.Abs(d2 - best2) < 1e-3f;
                        if (d2 < best2 - 1e-3f || (tie && preferred && !bestPreferred))
                        {
                            best2 = d2;
                            bestPreferred = preferred;
                            snapX = px;
                            snapZ = pz;
                            if (foundRing < 0) foundRing = ring;
                        }
                    }
                }
            }
            return foundRing >= 0 && best2 <= SnapReachMeters * SnapReachMeters;
        }

        /// <summary>How far <see cref="TrySnapToLand"/> looks for land.</summary>
        public const float SnapReachMeters = 300f;

        /// <summary>The number of links in the link table.</summary>
        public int RoutedCount => _routed;

        /// <summary>The cost, in speed-independent cost units, of the leg between two routed links that share a region; 0 for a link and itself, NaN when they share none or one is not routed.</summary>
        public float LinkLegCost(int linkA, int linkB)
        {
            if (!IsRouted(linkA) || !IsRouted(linkB)) return float.NaN;
            if (linkA == linkB) return 0f;
            return _legCost[linkA * _routed + linkB];
        }

        /// <summary>True when the link-to-link leg crosses a bend of the river (its cost is an estimate until the NavMesh confirms it).</summary>
        public bool LinkLegEstimated(int linkA, int linkB)
        {
            if (!IsRouted(linkA) || !IsRouted(linkB) || linkA == linkB) return false;
            return _legEstimated[linkA * _routed + linkB];
        }

        /// <summary>Whether the two links lie in a common region (a leg between them exists).</summary>
        public bool LinksShareRegion(int linkA, int linkB)
        {
            if (linkA < 0 || linkB < 0 || linkA >= _linkCount || linkB >= _linkCount) return false;
            for (int i = 0; i < _linkRegionCount[linkA]; i++)
                for (int j = 0; j < _linkRegionCount[linkB]; j++)
                    if (_linkRegions[linkA * MaxRegionsPerLink + i] == _linkRegions[linkB * MaxRegionsPerLink + j]) return true;
            return false;
        }

        /// <summary>
        /// Runs units of work until Ready, or until <paramref name="clock"/> reaches <paramref name="deadlineTicks"/> after a unit.
        /// A call does at least one unit, so a deadline already past still makes progress. True when Ready. Does nothing and
        /// returns false while the picture is not Ready (the regions follow the picture).
        /// </summary>
        public bool Step(TerrainPicture picture, BattleTerrainGrid grid, long deadlineTicks, Func<long> clock)
        {
            if (_stage == StageReady) return true;
            if (Failed || picture == null || !picture.Ready || grid == null) return false;
            if (_stage == StageNew && !Begin(grid)) return false;
            UnitsLastStep = 0;
            while (_stage != StageReady)
            {
                DoUnit();
                TotalUnits++;
                UnitsLastStep++;
                if (_stage != StageReady && clock() >= deadlineTicks) break;
            }
            return _stage == StageReady;
        }

        private bool Begin(BattleTerrainGrid grid)
        {
            _grid = grid;
            _sizeX = grid.SizeX;
            _sizeZ = grid.SizeZ;
            if (!(_sizeX > 0f) || !(_sizeZ > 0f))
            {
                Failed = true;
                return false;
            }
            if (grid.Frozen)
            {
                Frozen = true;
                _regionCount = 1;
                _stage = StageReady;
                return true;
            }
            _nx = Math.Max(1, (int)Math.Ceiling(_sizeX / BlockMeters));
            _nz = Math.Max(1, (int)Math.Ceiling(_sizeZ / BlockMeters));
            int cells = _nx * _nz;
            _prov = new int[cells];
            _uf = new int[cells / 2 + 2];
            _rootRegion = new int[cells / 2 + 2];
            _final = new ushort[cells];
            _linkX = new float[MaxCrossings];
            _linkZ = new float[MaxCrossings];
            _linkGridIndex = new int[MaxCrossings];
            _linkCrossingId = new int[MaxCrossings];
            _linkRegionCount = new int[MaxCrossings];
            _linkRegions = new int[MaxCrossings * MaxRegionsPerLink];
            _stage = StageRows;
            return true;
        }

        private void DoUnit()
        {
            switch (_stage)
            {
                case StageRows:
                    LabelRow(_row);
                    if (++_row >= _nz)
                    {
                        _row = 0;
                        _stage = StageRelabel;
                    }
                    break;
                case StageRelabel:
                    RelabelRow(_row);
                    if (++_row >= _nz)
                    {
                        _prov = null;
                        _uf = null;
                        _rootRegion = null;
                        _stage = StageLinks;
                    }
                    break;
                case StageLinks:
                    if (_crossingCursor < _grid.CrossingCount) ClassifyCrossing(_crossingCursor++);
                    if (_crossingCursor >= _grid.CrossingCount) BeginTable();
                    break;
                case StageTable:
                    DoPair();
                    break;
            }
        }

        // ---- stage 1: first pass ---------------------------------------------------------------------------------

        private bool IsLand(int ix, int iz)
        {
            float x0 = ix * BlockMeters, z0 = -_sizeZ + iz * BlockMeters;
            const float lo = BlockMeters * 0.25f, hi = BlockMeters * 0.75f;
            return !IsWater(_grid.IdAt(x0 + lo, z0 + lo)) && !IsWater(_grid.IdAt(x0 + hi, z0 + lo))
                && !IsWater(_grid.IdAt(x0 + lo, z0 + hi)) && !IsWater(_grid.IdAt(x0 + hi, z0 + hi));
        }

        // River, ford and bridge: none of them is land of a region (the crossings only link regions).
        private static bool IsWater(int id) => id == BattleTerrainGrid.RiverId || id == BattleTerrainGrid.FordId || id == BattleTerrainGrid.BridgeId;

        private void LabelRow(int iz)
        {
            for (int ix = 0; ix < _nx; ix++)
            {
                int idx = iz * _nx + ix;
                if (!IsLand(ix, iz))
                {
                    _prov[idx] = 0;
                    continue;
                }
                int left = ix > 0 ? _prov[idx - 1] : 0;
                int up = iz > 0 ? _prov[idx - _nx] : 0;
                int label;
                if (left == 0 && up == 0)
                {
                    label = ++_provCount;
                    _uf[label] = label;
                }
                else if (left != 0 && up != 0)
                {
                    Union(left, up);
                    label = Find(left);
                }
                else
                {
                    label = left != 0 ? left : up;
                }
                _prov[idx] = label;
            }
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

        // ---- stage 2: final ids in lattice order -------------------------------------------------------------------

        private void RelabelRow(int iz)
        {
            for (int ix = 0; ix < _nx; ix++)
            {
                int idx = iz * _nx + ix;
                int p = _prov[idx];
                if (p == 0)
                {
                    _final[idx] = 0;
                    continue;
                }
                int root = Find(p);
                int region = _rootRegion[root];
                if (region == 0)
                {
                    if (_regionCount < MaxRegions) region = ++_regionCount;
                    else
                    {
                        region = -1;
                        _regionsTruncated = true;
                        OnceLog.Warning(TruncatedKey, "[RiverRegions] more than " + MaxRegions + " land regions: the rest are dropped and read as river");
                    }
                    _rootRegion[root] = region;
                }
                _final[idx] = region > 0 ? (ushort)region : (ushort)0;
            }
        }

        // ---- stage 3: links ----------------------------------------------------------------------------------------

        private void ClassifyCrossing(int k)
        {
            _grid.Crossing(k, out float cx, out float cz, out int id);
            _crossingTotal++;
            Span<int> found = stackalloc int[MaxRegionsPerLink];
            int n = 0;
            float reach = CrossingLinkMeters + BlockMeters;
            int ix0 = Math.Max(0, (int)Math.Floor((cx - reach) / BlockMeters)), ix1 = Math.Min(_nx - 1, (int)Math.Floor((cx + reach) / BlockMeters));
            int iz0 = Math.Max(0, (int)Math.Floor((cz + _sizeZ - reach) / BlockMeters)), iz1 = Math.Min(_nz - 1, (int)Math.Floor((cz + _sizeZ + reach) / BlockMeters));
            float r2 = CrossingLinkMeters * CrossingLinkMeters;
            for (int iz = iz0; iz <= iz1; iz++)
            {
                for (int ix = ix0; ix <= ix1; ix++)
                {
                    int v = _final[iz * _nx + ix] - 1;
                    if (v < 0) continue;
                    float ex = (ix + 0.5f) * BlockMeters - cx, ez = -_sizeZ + (iz + 0.5f) * BlockMeters - cz;
                    if (ex * ex + ez * ez > r2) continue;
                    bool seen = false;
                    for (int i = 0; i < n; i++) if (found[i] == v) { seen = true; break; }
                    if (seen || n >= MaxRegionsPerLink) continue;
                    found[n++] = v;
                }
            }
            if (n < 2) return;            // a crossing inside one region (a bridge over a creek) links nothing
            if (_linkCount >= MaxCrossings)
            {
                _linksTruncated = true;
                OnceLog.Warning(TruncatedKey, "[RiverRegions] more than " + MaxCrossings + " crossing links: the rest are dropped");
                return;
            }
            int link = _linkCount++;
            _linkX[link] = cx;
            _linkZ[link] = cz;
            _linkGridIndex[link] = k;
            _linkCrossingId[link] = id;
            _linkRegionCount[link] = n;
            for (int i = 0; i < n; i++) _linkRegions[link * MaxRegionsPerLink + i] = found[i];
        }

        // ---- stage 4: the link table -------------------------------------------------------------------------------

        private void BeginTable()
        {
            _routed = Math.Min(_linkCount, MaxRoutedLinks);
            if (_linkCount > MaxRoutedLinks)
                OnceLog.Warning(TruncatedKey, "[RiverRegions] more than " + MaxRoutedLinks + " crossing links: the rest are kept as crossings and not routed");
            _legCost = new float[_routed * _routed];
            _legEstimated = new bool[_routed * _routed];
            for (int i = 0; i < _legCost.Length; i++) _legCost[i] = float.NaN;
            _pairA = 0;
            _pairB = 1;
            _stage = _routed < 2 ? StageReady : StageTable;
        }

        private void DoPair()
        {
            int a = _pairA, b = _pairB;
            if (LinksShareRegion(a, b))
            {
                float cost = LegSampler.Cost(_grid, _linkX[a], _linkZ[a], _linkX[b], _linkZ[b],
                    true, _linkX[a], _linkZ[a], _linkCrossingId[a], true, _linkX[b], _linkZ[b], _linkCrossingId[b], out bool estimated);
                _legCost[a * _routed + b] = _legCost[b * _routed + a] = cost;
                _legEstimated[a * _routed + b] = _legEstimated[b * _routed + a] = estimated;
            }
            if (++_pairB >= _routed)
            {
                _pairA++;
                _pairB = _pairA + 1;
            }
            if (_pairA >= _routed - 1) _stage = StageReady;
        }

        private bool InMap(float x, float z) => x >= 0f && x <= _sizeX && z >= -_sizeZ && z <= 0f;
    }
}
