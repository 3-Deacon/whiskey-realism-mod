using System;

namespace WhiskeyRealism.Tactical.Battle
{
    // The per-battle terrain grid (BC10 decision 1): vanilla's own 1 m terrain-id array
    // behind the pure queries every BC10 consumer shares (covered crossings, the path
    // probe's honest scores, road routing, the avenue flags, and BC12's picture). Pure: no
    // Unity or vanilla types. BattleTerrainGridRuntime fills it once per battle.
    //
    // Vanilla anchors (decompile, build 14703111):
    //   BattlefieldSetup.terrainspecs (24731, int[sizeX + 1, sizeZ + 1], 1 m cells) is built
    //     once at load by ReadInTerrainSpecs (26550-26670): the dominant texture id, then
    //     overwritten by the NavMesh area under the cell where that area is not 0 (a road
    //     through a forest reads road), a river cell with a bridge or crossing above it
    //     reads that crossing; ids outside [0, terrainspecifics) become 0.
    //   GetCurrentTerrainOnPos (26727-26771) indexes it as [(int)x, (int)(z + sizeZ)], each
    //     index clamped to (int)size - 1 (the compare is on the truncated value) and to 0.
    //   Ids (battleprefs.txt order): 0 grass, 3 creek, 4 river, 5 ford (river crossing),
    //     6 bridge, 7 road, 8 fences and walls, 9-13 pontoons, 14 forest, 15 swamp, 16 field.
    //     GamePrefs.terrainspeccost is the speed cost per id (units move at 1 / cost,
    //     GetTerrainSpeedAdjustment 132888-132901); a frozen map rewrites the river and
    //     crossing entries to the grass cost (26232-26233).
    //   Fords (5) and bridges (6) are listed in BattlefieldSetup.rivercrossingpositions
    //     (26628-26640, de-duplicated at 20 m).
    //   107567 writes ids 9 + rank into terrainspecs for placed model objects, so an id past
    //     the cost table reads as grass here.
    public sealed class BattleTerrainGrid
    {
        public const int RiverId = 4;
        public const int FordId = 5;
        public const int BridgeId = 6;
        public const int RoadId = 7;
        public const int FenceId = 8;
        public const int ForestId = 14;
        public const int SwampId = 15;

        private readonly int[,] _ids;
        private readonly int[] _speedCost;
        private readonly float[] _crossingX;
        private readonly float[] _crossingZ;
        private readonly int[] _crossingId;
        private readonly int _width;
        private readonly int _height;

        public BattleTerrainGrid(int[,] ids, float sizeX, float sizeZ, int[] speedCost, bool frozen,
            float[] crossingX, float[] crossingZ, int[] crossingId, int crossingCount)
        {
            _ids = ids;
            SizeX = sizeX;
            SizeZ = sizeZ;
            _speedCost = speedCost;
            Frozen = frozen;
            _width = ids != null ? ids.GetLength(0) : 0;
            _height = ids != null ? ids.GetLength(1) : 0;
            int n = crossingX != null && crossingZ != null && crossingId != null
                ? Math.Max(0, Math.Min(crossingCount, Math.Min(crossingX.Length, Math.Min(crossingZ.Length, crossingId.Length))))
                : 0;
            _crossingX = new float[n];
            _crossingZ = new float[n];
            _crossingId = new int[n];
            if (n > 0)
            {
                Array.Copy(crossingX, _crossingX, n);
                Array.Copy(crossingZ, _crossingZ, n);
                Array.Copy(crossingId, _crossingId, n);
            }
        }

        public float SizeX { get; }
        public float SizeZ { get; }

        /// <summary>GameVars.isfrozen(): a frozen river carries units. Refreshed by the runtime accessor.</summary>
        public bool Frozen { get; internal set; }

        public int CrossingCount => _crossingX.Length;

        /// <summary>
        /// The battle's grid for pure-compiled readers (the avenue adapter): bound once by the plugin to
        /// BattleTerrainGridRuntime.Get, which builds it lazily and returns null when the terrain
        /// array is unreadable. Null in the harness, where nothing is anchored.
        /// </summary>
        public static Func<BattleTerrainGrid> Provider { get; set; }

        public static BattleTerrainGrid Current()
        {
            Func<BattleTerrainGrid> provider = Provider;
            return provider != null ? provider() : null;
        }

        /// <summary>The terrain id at a world point: GetCurrentTerrainOnPos (26727-26771).</summary>
        public int IdAt(float x, float z)
        {
            if (_ids == null) return 0;
            int ix = (int)x;
            int iz = (int)(z + SizeZ);
            if ((float)ix > SizeX - 1f) ix = (int)SizeX - 1;
            if ((float)iz > SizeZ - 1f) iz = (int)SizeZ - 1;
            if (ix < 0) ix = 0;
            if (iz < 0) iz = 0;
            // The vanilla array is one cell larger than the map; a smaller fixture is clamped too.
            if (ix >= _width) ix = _width - 1;
            if (iz >= _height) iz = _height - 1;
            return ix < 0 || iz < 0 ? 0 : _ids[ix, iz];
        }

        /// <summary>
        /// The speed cost of an id (GamePrefs.terrainspeccost): movement time per metre is
        /// proportional to it. An id outside the table reads as grass.
        /// </summary>
        public int SpeedCost(int id)
        {
            if (_speedCost == null || _speedCost.Length == 0) return 2;
            if (id < 0 || id >= _speedCost.Length) return _speedCost[0];
            return _speedCost[id];
        }

        /// <summary>
        /// Whether units cross the id. The open river is the only impassable id: AddPath
        /// prices it at 500000 (130364-130394), frozen it is crossed (26230-26233). Fords,
        /// bridges, creeks and fences are crossed at their speed cost.
        /// </summary>
        public bool Passable(int id)
        {
            return id != RiverId || Frozen;
        }

        /// <summary>
        /// Reads the ids along a line every <paramref name="stepMeters"/> (the start, then
        /// every step up to and including the last whole step before the end). Returns how
        /// many ids were written, at most the span length.
        /// </summary>
        public int SampleLine(float x0, float z0, float x1, float z1, float stepMeters, Span<int> ids)
        {
            if (!(stepMeters > 0f) || ids.Length == 0) return 0;
            float dx = x1 - x0;
            float dz = z1 - z0;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            int count = (int)Math.Floor(d / stepMeters) + 1;
            if (count > ids.Length) count = ids.Length;
            float ux = d > 0f ? dx / d : 0f;
            float uz = d > 0f ? dz / d : 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i * stepMeters;
                ids[i] = IdAt(x0 + ux * t, z0 + uz * t);
            }
            return count;
        }

        /// <summary>The k-th listed crossing (ford 5 or bridge 6); id 0 when k is out of range.</summary>
        public void Crossing(int k, out float x, out float z, out int id)
        {
            if (k < 0 || k >= _crossingX.Length)
            {
                x = 0f;
                z = 0f;
                id = 0;
                return;
            }
            x = _crossingX[k];
            z = _crossingZ[k];
            id = _crossingId[k];
        }
    }
}
