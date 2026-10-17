using WhiskeyRealism.Tactical.Orchestrator.Plays;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    // Runtime half of RiverCrossingRule (excluded from the test csproj): the battle's RiverRegions (BC14, built once the terrain
    // picture is Ready) with the side's own crossing watch, one answer per planned Move. No ready regions (feasibility off, the
    // map not built yet, a frozen map) means no river is known and every leg is the same bank: the gate fails open, as the
    // rest of BC14 reads an unready picture as unknown rather than as a wall. Rollback: Tactical / Enable Crossing Move Gate.
    internal sealed class RiverCrossingGateRuntime : ICrossingGate, ICrossingPicture
    {
        internal static readonly RiverCrossingGateRuntime Instance = new RiverCrossingGateRuntime();

        private static bool _firstBlockLogged;
        private static bool _firstViaLogged;

        static RiverCrossingGateRuntime()
        {
            BattleScope.Register("river-crossing-gate", () => { _firstBlockLogged = false; _firstViaLogged = false; });
        }

        private RiverRegions _regions;
        private int _alliance;

        public CrossingVerdict Check(int alliance, float x0, float z0, float x1, float z1, out float viaX, out float viaZ)
        {
            viaX = viaZ = 0f;
            _regions = RiverRegionsRuntime.Ready();
            if (_regions == null) return CrossingVerdict.SameBank;
            _alliance = alliance;
            CrossingVerdict verdict = RiverCrossingRule.Decide(this, x0, z0, x1, z1, out viaX, out viaZ);
            if (verdict == CrossingVerdict.NoKnownCrossing && !_firstBlockLogged)
            {
                _firstBlockLogged = true;
                OnceLog.Info("tactical-battle-core:crossing-gate-block",
                    "[CrossingGate] first move refused: its leg crosses a river and the side knows no crossing for it (side=" + alliance + ")");
            }
            else if (verdict == CrossingVerdict.ViaCrossing && !_firstViaLogged)
            {
                _firstViaLogged = true;
                OnceLog.Info("tactical-battle-core:crossing-gate-via",
                    "[CrossingGate] first move routed through a known crossing (side=" + alliance + ")");
            }
            return verdict;
        }

        public bool Ready => _regions != null;
        public int RegionAt(float x, float z) => _regions.RegionAt(x, z);
        public int LinkCount => _regions.LinkCount;
        public bool LinkTouches(int link, int region) => _regions.LinkTouches(link, region);
        public void LinkPoint(int link, out float x, out float z) => _regions.LinkCrossing(link, out x, out z, out _, out _);
        public CrossingState StateOf(int link) => FeasibilityRuntime.LinkState(_alliance, link);
    }
}
