namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Maps Whiskey's <see cref="ReinforcementOpportunity"/> doctrine output to
    /// vanilla GTCW's per-side <c>BattleUnits.sideinformation[side].macroai</c>
    /// state. Pure logic; consumed by <c>BattleMacroStrategyPatch</c> (#44) as the
    /// fallback opinion when the operations ledger has none.
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
                    // maps here too and is a hold, not a retreat: #44 writes
                    // the value only into the AIBattle instance macroai, in a
                    // Postfix on CheckGlobalAIStrategy (6314), and never 3. A
                    // Whiskey-written 3 would carry no startoffallback stamp
                    // (vanilla stamps it only on its own transition, 6355),
                    // so 6362 would end the battle on the next cycle.
                    //
                    // Vanilla's own retreat paths still run before the
                    // Postfix each cycle, unaffected by the 2:
                    //   6336-6349  victory bar below trigger -> SetEndOfBattle
                    //              8/12 + retreat timer (does not read macroai);
                    //   6351-6359  losing on points with >1% losses -> stamps
                    //              startoffallback, macroai = 3; from then on
                    //              6374 returns early, #44 takes no opinion
                    //              while macroai == 3, and 6362 ends the battle
                    //              after timetostartairetreattimer;
                    //   6412-6418  attacker screening retreat (no arrivals in
                    //              24 h, casualties > 5%, force balance below
                    //              GetScreeningTrigger) -> macroai = 3 without a
                    //              startoffallback stamp. Its num4 comes from
                    //              newmacroaitoset and force balance, which #44
                    //              does not write, so the 2 neither triggers
                    //              nor blocks it.
                    // When those paths do not fire, the instance 2 drives this
                    // cycle's AdjustGroupAIStance (UpdateAITasks 5866/5869).
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
    }
}
