using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tactical.Operations
{
    // Maps the army ReinforcementOpportunity onto a brigade/leaf task (#61).
    // Returns the original task with a null reason when the opportunity has
    // nothing to add.
    //
    // BR-H1 (2026-10-14): DefensiveHold is "no opinion" — the same contract
    // TacticalVanillaMacroAiMapping uses (DefensiveHold → vanilla decides). It
    // no longer rewrites Attack/Probe/Support to HoldObjective; at near parity
    // that rewrite made both armies sit, where vanilla attacks in a meeting
    // engagement (CheckGlobalAIStrategy ~6400).
    public static class TacticalOpportunityTaskOverride
    {
        public static CommandTaskType Override(
            CommandTaskType originalTask,
            DirectChildRole role,
            ReinforcementOpportunity opportunity,
            out string overrideReason)
            => Override(originalTask, role, opportunity, worksHold: false, out overrideReason);

        /// <summary>
        /// BC14W: the same override with the works hold. A task the works hold guard turned into a hold (or a leaf it holds) is left a hold: an
        /// <c>AttackNow</c> opportunity must not turn it back into an attack, which would undo the guard on the node path, the no-order path and
        /// the leaf path alike. The other opportunities keep their rewrite (a withdrawal, a consolidation, which are not attacks).
        /// </summary>
        public static CommandTaskType Override(
            CommandTaskType originalTask,
            DirectChildRole role,
            ReinforcementOpportunity opportunity,
            bool worksHold,
            out string overrideReason)
        {
            overrideReason = null;
            switch (opportunity)
            {
                case ReinforcementOpportunity.WithdrawalToFightLater:
                    // Committed roles disengage; covering roles keep the task
                    // that lets the army withdraw (screen-and-delay).
                    if (originalTask == CommandTaskType.FallBackToLine) return originalTask;
                    switch (role)
                    {
                        case DirectChildRole.Main:
                        case DirectChildRole.SupportMain:
                        case DirectChildRole.Fix:
                            overrideReason = "withdrawal-aggressive-role";
                            return CommandTaskType.FallBackToLine;
                        case DirectChildRole.Screen:
                        case DirectChildRole.RefuseLeft:
                        case DirectChildRole.RefuseRight:
                        case DirectChildRole.Reserve:
                        case DirectChildRole.Fallback:
                            return originalTask;
                        default:
                            overrideReason = "withdrawal-unknown-role";
                            return CommandTaskType.FallBackToLine;
                    }

                case ReinforcementOpportunity.WaitAndConsolidate:
                    if (originalTask == CommandTaskType.AttackObjective ||
                        originalTask == CommandTaskType.SupportAttack)
                    {
                        overrideReason = "wait-for-relief";
                        return CommandTaskType.Consolidate;
                    }
                    return originalTask;

                case ReinforcementOpportunity.AttackNow:
                    if (!worksHold &&
                        (originalTask == CommandTaskType.HoldObjective ||
                         originalTask == CommandTaskType.Consolidate) &&
                        (role == DirectChildRole.Main || role == DirectChildRole.SupportMain))
                    {
                        overrideReason = "attack-now";
                        return CommandTaskType.AttackObjective;
                    }
                    return originalTask;

                case ReinforcementOpportunity.DefensiveHold:
                case ReinforcementOpportunity.NoOpportunity:
                default:
                    return originalTask;
            }
        }
    }
}
