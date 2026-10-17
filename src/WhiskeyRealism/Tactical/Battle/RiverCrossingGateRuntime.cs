using WhiskeyRealism.Tactical.Orchestrator.Plays;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    // Runtime half of RiverCrossingRule (excluded from the test csproj): the side's BC14 crossing picture (FeasibilityRuntime.CrossingPicture:
    // the battle's RiverRegions, the side's own crossing watch and ledger), one answer per planned Move. No picture (feasibility off, the
    // map not built yet, a frozen map) means no river is known and every leg is the same bank: the gate fails open, as the rest of BC14
    // reads an unready picture as unknown rather than as a wall. A refused leg posts its look request into the ledger. Rollback: Tactical /
    // Enable Crossing Move Gate.
    internal sealed class RiverCrossingGateRuntime : ICrossingGate
    {
        internal static readonly RiverCrossingGateRuntime Instance = new RiverCrossingGateRuntime();

        private static bool _firstBlockLogged;
        private static bool _firstViaLogged;

        static RiverCrossingGateRuntime()
        {
            BattleScope.Register("river-crossing-gate", () => { _firstBlockLogged = false; _firstViaLogged = false; });
        }

        public CrossingVerdict Check(int alliance, float x0, float z0, float x1, float z1, bool attack, out CrossingRoute route)
        {
            route = default;
            ICrossingPicture picture = FeasibilityRuntime.CrossingPicture(alliance);
            if (picture == null) return CrossingVerdict.SameBank;
            CrossingVerdict verdict = RiverCrossingRule.Decide(picture, x0, z0, x1, z1, attack, out route);
            if (verdict == CrossingVerdict.NoKnownCrossing && !_firstBlockLogged)
            {
                _firstBlockLogged = true;
                OnceLog.Info("tactical-battle-core:crossing-gate-block",
                    "[CrossingGate] first move refused: its leg crosses a river and the side knows no crossing for it (side=" + alliance + ", look link=" + route.LookLink + ")");
            }
            else if (verdict == CrossingVerdict.ViaCrossing && !_firstViaLogged)
            {
                _firstViaLogged = true;
                OnceLog.Info("tactical-battle-core:crossing-gate-via",
                    "[CrossingGate] first move routed through known crossings (side=" + alliance + ", crossings=" + route.Crossings + ", vias=" + route.ViaCount + ")");
            }
            return verdict;
        }
    }
}
