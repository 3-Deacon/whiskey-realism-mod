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
// Types 13/14 belong to vanilla's CheckRemovalOfOrders (6800-6808) and are never returned;
// 15 is returned for #62's retreat intent only (the one 15 Whiskey sends, 6798 being
// vanilla's own). Campaign types (5-10, 16) are never returned: a probe is a 1, not the
// campaign "Redeploy" a 5 was.
//
// One intent -> type map (plan 2026-10-15-wl-order-flow.md D15): #62's PlayerOrderIntent goes
// through the same task table as the executor's CommandTaskType.

using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.PlayerOrders;

namespace WhiskeyRealism.Tactical.Battle
{
    internal static class TacticalDispatchTypeMapper
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
                case CommandTaskType.ReleaseReserve:
                    // Never an 11: vanilla's UpdateReserveOrder (6765-6775) would drag it to the reserve's
                    // linked group.
                    return targetEnemyHeld ? 0 : 2;
                case CommandTaskType.HoldObjective:
                case CommandTaskType.HoldChoke:
                case CommandTaskType.FallBackToLine:
                    return 12;
                case CommandTaskType.ReserveWait:
                    return 11;
                case CommandTaskType.Screen:
                case CommandTaskType.Probe:
                case CommandTaskType.GuardFlank:
                case CommandTaskType.Scout:
                case CommandTaskType.AdvanceToAssembly:
                case CommandTaskType.FormUp:
                    return 1;
                case CommandTaskType.FixEnemy:
                    return 3;
                default:
                    return None;
            }
        }

        // What a dispatched task is for, for the condition line under vanilla's order text (D10).
        public static OrderIntentKind IntentKind(CommandTaskType task)
        {
            switch (task)
            {
                case CommandTaskType.AttackObjective:
                case CommandTaskType.FixEnemy:
                case CommandTaskType.ReleaseReserve:
                    return OrderIntentKind.Attack;
                case CommandTaskType.SupportAttack:
                    return OrderIntentKind.Support;
                case CommandTaskType.HoldObjective:
                case CommandTaskType.HoldChoke:
                case CommandTaskType.GuardFlank:
                    return OrderIntentKind.Hold;
                case CommandTaskType.FallBackToLine:
                case CommandTaskType.Delay:
                    return OrderIntentKind.Delay;
                case CommandTaskType.Screen:
                case CommandTaskType.Probe:
                case CommandTaskType.Scout:
                case CommandTaskType.AdvanceToAssembly:
                case CommandTaskType.FormUp:
                    return OrderIntentKind.Deploy;
                default:
                    return OrderIntentKind.None;
            }
        }

        public static OrderIntentKind IntentKind(PlayerOrderIntent intent)
        {
            switch (intent)
            {
                case PlayerOrderIntent.AttackObjective: return OrderIntentKind.Attack;
                case PlayerOrderIntent.SupportMainEffort: return OrderIntentKind.Support;
                case PlayerOrderIntent.HoldObjective: return OrderIntentKind.Hold;
                case PlayerOrderIntent.FallBackToLine: return OrderIntentKind.Delay;
                case PlayerOrderIntent.ProbeObjective:
                case PlayerOrderIntent.AdvanceToAssemblyArea: return OrderIntentKind.Deploy;
                default: return OrderIntentKind.None;
            }
        }

        // #62's intent: retreat is its own 15; the rest ride the task table. An attack on an objective
        // is an attack on what the enemy holds or is (targetEnemyHeld).
        internal static int Map(PlayerOrderIntent intent, bool targetEnemyHeld)
        {
            switch (intent)
            {
                case PlayerOrderIntent.RetreatToExit:
                    return 15;
                case PlayerOrderIntent.AttackObjective:
                    return Map(CommandTaskType.AttackObjective, targetEnemyHeld, false);
                case PlayerOrderIntent.SupportMainEffort:
                    return Map(CommandTaskType.SupportAttack, targetEnemyHeld, false);
                case PlayerOrderIntent.HoldObjective:
                    return Map(CommandTaskType.HoldObjective, targetEnemyHeld, false);
                case PlayerOrderIntent.FallBackToLine:
                    return Map(CommandTaskType.FallBackToLine, targetEnemyHeld, false);
                case PlayerOrderIntent.ProbeObjective:
                    return Map(CommandTaskType.Probe, targetEnemyHeld, false);
                case PlayerOrderIntent.AdvanceToAssemblyArea:
                    return Map(CommandTaskType.AdvanceToAssembly, targetEnemyHeld, false);
                default:
                    return None;
            }
        }
    }
}
