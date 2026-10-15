namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Maps Whiskey's <see cref="ReinforcementOpportunity"/> doctrine output to
    /// vanilla GTCW's per-side <c>BattleUnits.sideinformation[side].macroai</c>
    /// state. Pure logic; consumed by <c>BattleMacroAIOverridePatch</c>.
    ///
    /// Vanilla anchors (decompile / 2026-05-23):
    ///   <list type="bullet">
    ///     <item><c>AIBattle.CheckGlobalAIStrategy()</c> at line 6314 — consumes
    ///       <c>sideinformation[sideofai].macroai</c> at line 6383-6385 and uses it
    ///       as the override for the instance <c>macroai</c>.</item>
    ///     <item><c>AIBattle.AdjustGroupAIStance()</c> at line 4220 — uses the
    ///       resolved <c>macroai</c> to drive per-group <c>ai_stanceordered</c>.
    ///       When macroai == 2 (defend), every visible group gets ai_stance = 2,
    ///       which causes vanilla's micro-AI to form a line of battle.</item>
    ///   </list>
    ///
    /// Macroai enum (from decompile observation):
    ///   0 = assault with flanking (envelopment)
    ///   1 = frontal assault (no flanking)
    ///   2 = defend (FORMS LINE OF BATTLE via vanilla's group-stance cascade)
    ///   3 = retreat / fallback (vanilla-owned; never written by Whiskey)
    ///  -1 = unset (vanilla computes via its own hysteresis-driven path)
    /// </summary>
    public static class TacticalVanillaMacroAiMapping
    {
        public const int MacroAiFlankAttack = 0;
        public const int MacroAiFrontalAttack = 1;
        public const int MacroAiDefend = 2;
        public const int MacroAiRetreat = 3;
        public const int MacroAiUnset = -1;

        /// <summary>
        /// Returns the vanilla macroai value Whiskey wants for the given doctrine
        /// opportunity, or <c>null</c> if we should leave vanilla's macroai
        /// untouched (so vanilla's own decision pipeline runs).
        ///
        /// The flank-vs-frontal split for AttackNow uses the same envelopment
        /// ratio threshold that <see cref="ArmyOrchestrator.EffectiveEnvelopmentMinRatio"/>
        /// applies — so vanilla's macroai stays consistent with our own
        /// cascade-mode selection (DoubleWing/Echelon vs SingleMainEffort).
        /// </summary>
        public static int? MapOpportunityToMacroAi(
            ReinforcementOpportunity opportunity,
            float currentRatio,
            float commanderAggression01)
        {
            switch (opportunity)
            {
                case ReinforcementOpportunity.AttackNow:
                    float threshold = ArmyOrchestrator.EffectiveEnvelopmentMinRatio(commanderAggression01);
                    return currentRatio >= threshold ? MacroAiFlankAttack : MacroAiFrontalAttack;

                case ReinforcementOpportunity.WaitAndConsolidate:
                case ReinforcementOpportunity.DelayingDefense:
                case ReinforcementOpportunity.WithdrawalToFightLater:
                    // Defensive posture for vanilla: form the line, hold
                    // ground, no offensive maneuver (macroai = 2). Withdrawal
                    // maps here too: macroai 3 is irreversible in vanilla
                    // (6374 returns while 3) and ends the battle once
                    // currenttimefromstart >= startoffallback + retreat timer
                    // (6362), and startoffallback is only stamped on vanilla's
                    // OWN transition to 3 (6355). The army retreat stays
                    // vanilla's decision.
                    return MacroAiDefend;

                case ReinforcementOpportunity.DefensiveHold:
                    // "Hold and refuse decisive engagement" is the doctrine's
                    // fall-through, not a conviction. Returning no opinion lets
                    // vanilla's pipeline decide; forcing 2 here made near-even
                    // meeting engagements passive mutual-defend battles (BR-H1).
                case ReinforcementOpportunity.NoOpportunity:
                default:
                    // Pre-doctrine state or fall-through — don't override
                    // vanilla. Vanilla's own CheckGlobalAIStrategy hysteresis +
                    // force-balance computation handles the bootstrap and
                    // ambiguous cases.
                    return null;
            }
        }

        /// <summary>
        /// True when the macroai value is one Whiskey may write (0-2). Retreat
        /// (3) is excluded: see <see cref="MapOpportunityToMacroAi"/>.
        /// </summary>
        public static bool IsLegalMacroAi(int value)
        {
            return value >= MacroAiFlankAttack && value <= MacroAiDefend;
        }
    }
}
