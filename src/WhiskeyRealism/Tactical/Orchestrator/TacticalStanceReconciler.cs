namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Reconciles the two stance signals the orchestrator holds:
    ///
    ///   1. <see cref="BattleStance"/> — read from vanilla
    ///      <c>BattleUnits.GetBattleType</c> (objective-points based). Answers
    ///      "is our mission to take ground?" (Attacker) vs "hold ground?"
    ///      (Defender) vs "neither yet" (Meeting).
    ///   2. <see cref="ReinforcementOpportunity"/> — the force-ratio +
    ///      reinforcement-timing aware doctrine outcome.
    ///
    /// Problem (2026-05-25): <see cref="TacticalPlaybookCatalog.Select"/> uses
    /// the objective-based stance as a HARD filter
    /// (<see cref="TacticalPlaybookCatalog.StanceAllowsPlaybook"/>). An
    /// outnumbered force that happens to be the objective-attacker was FORCED
    /// to pick an offensive playbook (FrontalShockAssault) — every defensive
    /// archetype was filtered out before scoring, so the doctrine's
    /// DelayingDefensePressure / DefensivePressure biases were dead code. Smoke
    /// on 2026-05-26 confirmed: side outnumbered 0.44 with doctrine
    /// DelayingDefense executed FrontalShockAssault.
    ///
    /// Fix: when the force-aware doctrine wants a non-offensive posture, the
    /// objective-attacker is demoted to <see cref="BattleStance.Defender"/> for
    /// play selection. An outnumbered force that is nominally the attacker
    /// should delay/defend, not launch a doomed frontal assault. This mirrors
    /// Scourge of War, where GetBattleType (OpenType/Rank) sets the base
    /// posture but offai modulates aggressiveness by force ratio — an
    /// outnumbered "attacker" still goes defensive.
    ///
    /// The reconciliation lives here (pure) rather than inside the catalog so
    /// the catalog hard-filter stays purely stance-driven, and the
    /// doctrine↔objective trade happens where both signals are available.
    /// </summary>
    public static class TacticalStanceReconciler
    {
        /// <summary>
        /// Returns the stance the playbook catalog should be handed. Demotes an
        /// objective-attacker (or Meeting) to <see cref="BattleStance.Defender"/>
        /// when the doctrine outcome is non-offensive (DelayingDefense,
        /// WaitAndConsolidate, WithdrawalToFightLater). DefensiveHold is the
        /// doctrine's no-opinion fall-through (BR-H1) and does not demote.
        /// Otherwise passes the objective-based stance through unchanged.
        ///
        /// Note: only demotes in the defensive direction. A doctrine of
        /// AttackNow does NOT promote a Defender to Attacker — that direction is
        /// already closed inside the doctrine itself
        /// (<c>stanceAllowsAttack = ev.Stance != BattleStance.Defender</c>), so
        /// a Defender never produces AttackNow in the first place.
        /// </summary>
        public static BattleStance ResolveEffectiveStance(
            BattleStance objectiveStance,
            ReinforcementOpportunity doctrine)
        {
            if (IsNonOffensive(doctrine) && objectiveStance != BattleStance.Defender)
                return BattleStance.Defender;
            return objectiveStance;
        }

        /// <summary>
        /// True when the doctrine outcome wants the force NOT to press an
        /// attack. NoOpportunity, DefensiveHold (both neutral) and AttackNow
        /// are excluded.
        /// </summary>
        public static bool IsNonOffensive(ReinforcementOpportunity doctrine)
        {
            switch (doctrine)
            {
                case ReinforcementOpportunity.DelayingDefense:
                case ReinforcementOpportunity.WaitAndConsolidate:
                case ReinforcementOpportunity.WithdrawalToFightLater:
                    return true;
                default:
                    return false;
            }
        }
    }
}
