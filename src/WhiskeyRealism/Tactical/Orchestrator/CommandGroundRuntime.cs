using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    // Runtime half of ICommandGround (excluded from the test csproj): where a command node stands now, read from the side's battle snapshot
    // (the node id is the group's GameObject instance id, the snapshot's own key; the position is vanilla's Regiment.GetPosition, the facing
    // the transform's yaw), and the side's crossing picture (the same one the executor's crossing gate asks). A node the snapshot does not hold
    // is not placed, and the ledger plans no post for it; no snapshot places no node. One instance per side, rebound each ledger tick.
    internal sealed class CommandGroundRuntime : ICommandGround
    {
        private static readonly CommandGroundRuntime[] _sides = { new CommandGroundRuntime(), new CommandGroundRuntime() };
        private static bool _unplacedLogged;

        static CommandGroundRuntime()
        {
            BattleScope.Register("command-ground", () =>
            {
                _unplacedLogged = false;
                for (int i = 0; i < _sides.Length; i++) _sides[i].Bind(null, -1);
            });
        }

        private BattleSnapshot _snapshot;
        private int _alliance = -1;

        private CommandGroundRuntime()
        {
        }

        /// <summary>The side's ground over its current snapshot (null: no node is placed). An alliance outside 0..1 gets a fresh, unshared ground.</summary>
        internal static CommandGroundRuntime For(BattleSnapshot snapshot, int alliance)
        {
            CommandGroundRuntime ground = alliance >= 0 && alliance < _sides.Length ? _sides[alliance] : new CommandGroundRuntime();
            ground.Bind(snapshot, alliance);
            return ground;
        }

        private void Bind(BattleSnapshot snapshot, int alliance)
        {
            _snapshot = snapshot;
            _alliance = alliance;
        }

        public ICrossingPicture Crossings => FeasibilityRuntime.CrossingPicture(_alliance);

        public bool TryPlace(string nodeId, out float x, out float z, out float facingDegrees)
        {
            x = z = facingDegrees = 0f;
            int index = _snapshot != null ? _snapshot.IndexOfOwn(TacticalBattleCoordinator.ParseCommandNodeInstanceId(nodeId)) : -1;
            if (index < 0 || index >= _snapshot.OwnCount)
            {
                // Once per battle, and the message is built only then (this runs for every unplaced node every ledger tick).
                if (!_unplacedLogged)
                {
                    _unplacedLogged = true;
                    OnceLog.Info("tactical-battle-core:command-ground-unplaced",
                        "[CommandGround] a command node is not in the side's battle snapshot; the ledger plans no post for it (side=" + _alliance + ", node=" + nodeId + ")");
                }

                return false;
            }

            UnitRecord unit = _snapshot.Own[index];
            x = unit.X;
            z = unit.Z;
            facingDegrees = unit.RotationY;
            return true;
        }
    }
}
