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
