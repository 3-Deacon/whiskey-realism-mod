using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical
{
    // Single source of truth for CommandTaskType → vanilla group ai_stance.
    // Every Whiskey stance write goes through Util/TacticalStanceWriter with a
    // value from here, so #45 (group stance) and #61 (posture executor) cannot
    // disagree about what a task means.
    //
    // Vanilla stance semantics (decompile, build 14703111):
    //   groupstancename (65061): 0 none, 1 screen, 2 defend, 3 attack, 4 assault.
    //   UpdateMicroAI (5694-5736): RepositionAIGroupMicroTowardsEnemy runs only
    //     for ai_stance > 0 && != 2. Screen moves with movementmode 4 and all
    //     unit types; attack moves infantry only (restrictunittypes 0); assault
    //     skips cover. Defend never advances toward the enemy.
    //   battleprefs "% of max weapon distance for micro ai placement": screen
    //     0.9, attack 0.85, assault 0.75 — screen keeps contact at long range.
    //   AdjustCavalryBehavior (5047/5060): no cavalry charges in screen/defend.
    //   CheckLineFallbacks (5167): low-morale fallback thresholds per stance.
    //   AdjustGroupFormations (5875): no formation change while defending.
    //   AdjustGroupAIStance (4239-4260): assault is vanilla's own
    //     strength-triggered escalation; macroai 3 (retreat) sets screen.
    //
    // Mapping and why:
    //   Screen / Probe / Scout → screen (1): contact at 0.9 × range, skirmish
    //     movement, no charges — reconnaissance without commitment. (A probe on
    //     attack (3) would close to 0.85 × range and advance its infantry; that
    //     is a commitment, not a probe.)
    //   AttackObjective / SupportAttack → attack (3).
    //   Counterstroke → attack (3): a Defender's bounded counterstroke against a penetration (BC13); only an exploit
    //     order carries it, and the #45 scorer below gives no opinion on it.
    //   FixEnemy → attack (3): a fixing force must stay engaged. Defend (2)
    //     disables the micro advance (5694), so the group would never close to
    //     fire range unless the enemy came to it; screen (1) gives ground. Attack
    //     closes to 0.85 × range without the charges of assault (4).
    //   Hold / HoldChoke / GuardFlank / Consolidate / Delay / FormUp /
    //     AdvanceToAssembly / FallBackToLine / Reserve / Release / Recover →
    //     defend (2): hold the ground reached, fire, no micro advance.
    //   None → none (0): no opinion — the writer never writes it.
    //   Assault (4) is never produced; it stays vanilla's decision.
    public static class TacticalCommandTaskToStanceMapper
    {
        public const int StanceNone = 0;
        public const int StanceScreen = 1;
        public const int StanceDefend = 2;
        public const int StanceAttack = 3;
        public const int StanceAssault = 4;

        public static int Map(CommandTaskType task)
        {
            switch (task)
            {
                case CommandTaskType.Screen:
                case CommandTaskType.Probe:
                case CommandTaskType.Scout:
                    return StanceScreen;

                case CommandTaskType.AttackObjective:
                case CommandTaskType.SupportAttack:
                case CommandTaskType.FixEnemy:
                case CommandTaskType.Counterstroke:
                    return StanceAttack;

                case CommandTaskType.FormUp:
                case CommandTaskType.AdvanceToAssembly:
                case CommandTaskType.Consolidate:
                case CommandTaskType.GuardFlank:
                case CommandTaskType.HoldObjective:
                case CommandTaskType.HoldChoke:
                case CommandTaskType.FallBackToLine:
                case CommandTaskType.ReserveWait:
                case CommandTaskType.ReleaseReserve:
                case CommandTaskType.Delay:
                case CommandTaskType.RecoverStuckOrder:
                    return StanceDefend;

                case CommandTaskType.None:
                default:
                    return StanceNone;
            }
        }

        // #45 group-stance scorer view of a ledger task. Returns -1 for tasks the
        // scorer has no opinion on (movement/recon tasks belong to #61, which
        // writes the stance with its waypoint). Hold-type tasks commit to attack
        // when the sector shows a visible weak point (TacticalDoctrineScorer.
        // ShouldCommitVisibleWeakPoint). Values come from Map so both writers agree.
        // BC9: a Defender's ledger stance is capped at defend (2): Whiskey never
        // issues attack stance for a Defender (vanilla's own cascade still may).
        public static int MapForGroupStanceScorer(CommandTaskType task, bool visibleWeakPoint, Orchestrator.BattleStance stance)
        {
            int mapped = MapForGroupStanceScorerAnyStance(task, visibleWeakPoint);
            return stance == Orchestrator.BattleStance.Defender && mapped > StanceDefend ? StanceDefend : mapped;
        }

        private static int MapForGroupStanceScorerAnyStance(CommandTaskType task, bool visibleWeakPoint)
        {
            switch (task)
            {
                case CommandTaskType.HoldObjective:
                case CommandTaskType.HoldChoke:
                    return visibleWeakPoint ? StanceAttack : Map(task);
                case CommandTaskType.AttackObjective:
                case CommandTaskType.SupportAttack:
                case CommandTaskType.FixEnemy:
                case CommandTaskType.ReserveWait:
                case CommandTaskType.FallBackToLine:
                    return Map(task);
                default:
                    return -1;
            }
        }
    }
}
