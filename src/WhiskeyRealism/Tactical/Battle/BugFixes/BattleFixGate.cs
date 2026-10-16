namespace WhiskeyRealism.Tactical.Battle.BugFixes
{
    /// <summary>The BC13 vanilla battle bug fixes and steers, for the gate that decides whether each runs.</summary>
    public enum BattleFixKind
    {
        StanceCooldown,
        MacroUpgrade,
        ArtillerySide,
        ReserveSupportPick,
        CavalryStrike,
        SeparatedEnemy,
    }

    /// <summary>
    /// BC13 (AGENTS.md § Release mode contract): who runs under which Commander Mode. A vanilla bug fix (#119 the stance cooldown
    /// re-stamp, #122-#124 the three IL fixes) corrects a vanilla value and is not Whiskey commander behaviour: it runs behind its
    /// own flag in every Commander Mode. A steer that serves Whiskey's command (#120 the cavalry charge discipline, #121 the
    /// isolated-target suppression) runs only where Whiskey writes: Commander Mode Active.
    /// </summary>
    public static class BattleFixGate
    {
        public static bool ServesWhiskeyCommand(BattleFixKind kind)
        {
            return kind == BattleFixKind.CavalryStrike || kind == BattleFixKind.SeparatedEnemy;
        }

        public static bool Runs(BattleFixKind kind, bool pluginEnabled, bool flagOn, bool commanderModeAllowsWrites)
        {
            if (!pluginEnabled || !flagOn) return false;
            return !ServesWhiskeyCommand(kind) || commanderModeAllowsWrites;
        }
    }
}
