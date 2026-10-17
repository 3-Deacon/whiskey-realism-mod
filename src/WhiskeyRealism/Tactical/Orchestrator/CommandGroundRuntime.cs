using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    // Runtime half of ICommandGround (excluded from the test csproj): where a command node stands now, read from the side's battle snapshot
    // (the node id is the group's GameObject instance id, the snapshot's own key; the position is vanilla's Regiment.GetPosition, the facing
    // the transform's yaw), and the side's crossing picture (the same one the executor's crossing gate asks). A node the snapshot does not hold
    // is not placed, and the ledger plans nothing for it; no snapshot places no node.
    internal sealed class CommandGroundRuntime : ICommandGround
    {
        private readonly BattleSnapshot _snapshot;
        private readonly int _alliance;

        internal CommandGroundRuntime(BattleSnapshot snapshot, int alliance)
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
                OnceLog.Info("tactical-battle-core:command-ground-unplaced",
                    "[CommandGround] a command node is not in the side's battle snapshot; the ledger plans no post for it (side=" + _alliance + ")");
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
