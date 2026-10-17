using System;
using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    /// <summary>
    /// The crossing gate's picture (<see cref="ICrossingPicture"/>) over BC14's own: the battle's river regions, the side's crossing watch
    /// and the side's feasibility ledger (the selected plan's crossings and the look request a refused move posts). Pure: the runtime binds
    /// the regions it has built, and the harness binds a fixture's.
    /// </summary>
    internal sealed class LedgerCrossingPicture : ICrossingPicture
    {
        private readonly CrossingWatch _watch;
        private readonly FeasibilityLedger _ledger;
        private RiverRegions _regions;

        public LedgerCrossingPicture(CrossingWatch watch, FeasibilityLedger ledger)
        {
            _watch = watch;
            _ledger = ledger;
        }

        /// <summary>The regions to read; null (not built, or the feasibility off) leaves the picture not ready, so every leg is on one bank.</summary>
        public void Bind(RiverRegions regions) => _regions = regions;

        public bool Ready => _regions != null && _regions.Ready;
        public int RegionAt(float x, float z) => _regions.RegionAt(x, z);
        public int LinkCount => _regions.LinkCount;
        public int RegionsOfLink(int link, Span<int> regions) => _regions.RegionsOfLink(link, regions);
        public void LinkPoint(int link, out float x, out float z) => _regions.LinkCrossing(link, out x, out z, out _, out _);
        public CrossingState StateOf(int link) => _watch.StateOf(link);
        public object Topology => _regions;
        public bool IsPlanned(int link) => _ledger.IsPlannedLink(link);
        public void RequestLook(int link) => _ledger.PostGateLook(link);
    }
}
