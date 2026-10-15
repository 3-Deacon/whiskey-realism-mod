// Whiskey command task → vanilla W&L order type for the player's share of an order
// given to an ancestor of the player's command (chain-of-command spec §C). Pure.
//
// Vanilla anchors (decompile, build 14703111; type table in docs/wl-player-model.md):
//   0 take objective  — UpdateMovingTargets, objective not held by the unit's own
//                       alliance (6998); 12 when it is (7002)
//   1 deploy to engage — Regiment.CheckEnemyContact (117383); Whiskey's screen wording
//   2 support          — MarchToSoundOfGuns (3766)
//   3 attack enemy     — CheckForSeparatedEnemy (8210)
//   4 assault          — AssaultMove (3646)
//   11 reserve         — CheckReserveOrder (6841)
//   12 defend          — UpdateMovingTargets own objective (7002), CheckGlobalAIStrategy (6463)
// Types 13/14/15 belong to vanilla's CheckRemovalOfOrders (6798-6808) and are never
// returned. Campaign types (5-10, 16) are never returned either.

using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Battle
{
    public static class TacticalDispatchTypeMapper
    {
        public const int None = -1;

        // targetEnemyHeld: the objective is not held by the ordered side (vanilla's
        // own owner test at 6996). chargeSupported: the attack is a charge-supported
        // assault.
        public static int Map(CommandTaskType task, bool targetEnemyHeld, bool chargeSupported)
        {
            switch (task)
            {
                case CommandTaskType.AttackObjective:
                    if (chargeSupported) return 4;
                    return targetEnemyHeld ? 0 : 12;
                case CommandTaskType.SupportAttack:
                    return 2;
                case CommandTaskType.HoldObjective:
                case CommandTaskType.HoldChoke:
                case CommandTaskType.FallBackToLine:
                    return 12;
                case CommandTaskType.ReserveWait:
                case CommandTaskType.ReleaseReserve:
                    return 11;
                case CommandTaskType.Screen:
                case CommandTaskType.Probe:
                case CommandTaskType.GuardFlank:
                    return 1;
                case CommandTaskType.FixEnemy:
                    return 3;
                default:
                    return None;
            }
        }
    }
}
