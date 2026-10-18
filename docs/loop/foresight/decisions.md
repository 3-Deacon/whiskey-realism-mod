# User decisions, 2026-10-18 (strategic foresight)

User, verbatim: "okay, fix it all correctly". This approved the integrator's recommended options, presented in the message before it (the AskUserQuestion was dismissed with a message instead of answers):
1. #25 CheckForDefensiveOperationsCandidateFilterPatch: telemetry only while the planner runs. Vanilla's CheckForDefensiveOperations interception runs again, and PlannerDefensiveAvailabilityPatch is the only defensive gate.
2. Spotting marks as beliefs on both sides, symmetric. The player's alliance reads its SpottedEnemy marks. The AI alliance gets an equivalent mark channel built from its own fog grid with vanilla's mark formula.
3. The enemy's total force (alliance[e].troopsfielded, public on the campaign strategy screen) is an input to the presumption.
4. A covering force between the enemy capital and own capital, posted forward at Manassas Junction, with Fredericksburg as the fallback when the force is too small. The main effort may never take it, except for a target at the enemy capital itself.
5. A department-shortfall reinforcement path, for example to the West: one formation per decision from a department whose hold has surplus.
6. The minimum garrison strength comes from the scenario's live value (s.MinStrengthForMovement), not the hardcoded 5000, and it lands after the presumption.
Also approved: depot builders are never taken from a hold or covering formation, nor from an unready one; formations dispatched to the main effort stay in it through readiness dips; stranded formations return to their home sector when the main effort ends; mover telemetry names the formation and target.
Industry guidance the user accepted: an honest planner plus a reactive reflex that is never switched off (vanilla interception restored). Defensive omniscience in the planner is a later option behind a flag, and only if smoke shows holes.

## Integrator refinement, 2026-10-18 (strategic foresight phase 2)

Refines the approved item "stranded formations return to their home sector when the main effort ends". The integrator added this item from root cause 4; the user's ask is foresight, troops where the threat is.

Refinement, as given by the integrator: "a formation whose decision ended is unclaimed and is placed by the holds, the capital reserve and department reinforcement. There is no separate return lifecycle. The user may override this."

Kept: a dispatched member stays in the mass through readiness dips while its objective is the main effort. Removed: origins, the Returning claimant, the donor transfer back and the sidecar origin fields. Department "have" has one definition: formations that stand in the department and are not in the mass or committed. Spec: D11 requirement-decision-end.

## Integrator refinements, 2026-10-18 (strategic foresight phase 2, second round)

1. Commitment with no sortie record: the planner persists only the last non-empty mass (its objective and members). A member stays readiness-exempt in that objective's capacity while the objective is still a candidate, and stops being exempt when another objective is chosen or the objective falls. The user may override this.
2. Department reinforcement reuses D6's ReinforcementPick for the shortest department when there is no push, under D6's own lock (one reinforcement per decision), with no in-flight record of its own. The user may override this.

Spec: D11 requirement-commitment and requirement-department-reinforcement.
