using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical
{
    // Scope B refactor (2026-05-22). Maps Whiskey's richer doctrine
    // CommandTaskType vocabulary down to vanilla GT's 5 ai_stance values
    // per `groupstancename = ["none", "screen", "defend", "attack", "assault"]`
    // at decompile line 65061.
    //
    // Architectural context: prior Whiskey patches issued waypoints via
    // `bunits.SetWaypoint` WITHOUT first setting ai_stance, which left
    // vanilla micro-AI without context for how to execute. Per the W&L
    // audit (docs/scourge-of-war-ai-anchors.md "2026-05-22 W&L stance
    // refactor"), the canonical vanilla/W&L pipeline is:
    //
    //   ChangeStance(unit, mapped_stance) → SetWaypoint(unit, target)
    //
    // ChangeStance updates `ai_stanceordered`, manages aibattle[1] pool
    // membership, and cascades through `groupaiobject` so subordinates
    // inherit the stance automatically. Doing only SetWaypoint bypasses all
    // of this.
    //
    // The mapping is many-to-one: Whiskey's richer task vocabulary (Probe,
    // GuardFlank, FallBackToLine, etc.) compiles down to the 5 vanilla
    // stances. Vanilla's AI then drives micro-behavior CORRECTLY because it
    // understands the stance. Pure function — no Unity API, harness-testable.
    public static class TacticalCommandTaskToStanceMapper
    {
        // Vanilla ai_stance values (decompile line 65061).
        public const int StanceNone = 0;
        public const int StanceScreen = 1;
        public const int StanceDefend = 2;
        public const int StanceAttack = 3;
        public const int StanceAssault = 4;

        // Maps a CommandTaskType to its corresponding vanilla ai_stance.
        //
        // Mapping rationale:
        //   Screen / Probe / Scout      → screen (1)   forward-light commitment
        //   FormUp / AdvanceToAssembly  → defend (2)   organizing posture
        //   Consolidate                 → defend (2)   stabilizing posture
        //   GuardFlank                  → defend (2)   defensive flank role
        //   HoldObjective               → defend (2)   defensive stance
        //   FallBackToLine              → defend (2)   defensive withdrawal
        //   Reserve / ReleaseReserve    → defend (2)   reserved formation
        //   AttackObjective             → attack (3)   committed advance
        //   SupportAttack               → attack (3)   committed support
        //   FixEnemy                    → attack (3)   aggressive holding
        //   RecoverStuckOrder           → defend (2)   resume defensive
        //   None                        → none (0)     no stance
        //
        // For close-engaged scenarios where the brigade should be in melee
        // mode, callers should override the result with StanceAssault (4)
        // based on engagement state (separate signal).
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

        // Returns true when the task represents an offensive movement
        // (vanilla AI should prefer aggressive micro-behaviors). Used by
        // callers to decide whether to escalate stance to Assault (4) when
        // enemy is in melee range.
        public static bool IsOffensiveCommitment(CommandTaskType task)
        {
            switch (task)
            {
                case CommandTaskType.AttackObjective:
                case CommandTaskType.SupportAttack:
                case CommandTaskType.FixEnemy:
                    return true;
                default:
                    return false;
            }
        }
    }
}
