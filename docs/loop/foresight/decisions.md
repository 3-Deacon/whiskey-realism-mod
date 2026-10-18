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

## Integrator refinements, 2026-10-18 (strategic foresight phase 2, third round)

1. A stalled push: commitment ends exactly when D4's existing main-effort rule ends the push (odds below the required ratio for SwitchDays reviews, or the objective unavailable or accomplished), with no new constant. Corps waiting in committed-wait are taken off vanilla's offensive list; once the commitment ends they are unclaimed and placed by the holds.
2. The department lock: derived from live state, not from the decision counter. A department is locked while any formation in the last published allocation holds a reinforcement role toward it, and released when that formation arrives or its role changes.
3. An interrupted transfer: a formation's home department changes only when it arrives; an interrupted pick leaves it in the donor's department and the next review re-decides from scratch.

The user may override each of these. Spec: D11 requirement-commitment and requirement-department-reinforcement.

## Integrator decisions, 2026-10-18 (strategic foresight phase 2, pass 12b)

1. An ended push's corps (GAP-03): a formation whose push ended does not keep its old march. Through the existing mover path (`CampaignMover` through `VanillaMoveHost` and `AICampaign.MoveUnitTo`, the planner's move writer), a formation that a role now places gets a move to that role's post. A formation with no post is stopped where it stands, by a `MoveUnitTo` to its own position. The smoke counts it (`[Reserves] endedMarching=`, `[CampaignMover] stopped=`).
   - Premise checked against the code: the shipped mover has no stop move. The stop is a new (role, reason) entry, (`Hold`, `committed-wait`), issued by the shipped `Dispatch`, which already skips a formation whose waypoint lies within `ArrivalRadius` of the target. So only a formation that is still marching gets the stop.
   - Vanilla's readiness check refuses a target on enemy soil at readiness step 0-1 (SetWaypoint 91336-91350, reached through the `DefensiveMovingOrder` the planner's `Move` queues, 14523, 9892, 10048). Those corps are the ones the stop is for. D11 therefore issues the stop as `MoveUnitTo(..., movearoundenemyunits: false, checkforreadiness: false)`, the same skip vanilla's own retreat order makes (35841). That choice is routed as `question-stop-readiness` (recommended: (a)).
2. Department moves across a load (GAP-06): the formations in a department move are saved in the sidecar by unit key, as `lastMass` is (`departmentMoves: [{unitKey, theater}]`), so a load does not free the locks. D11 keeps them in `PlannerState.DepartmentMoves`. Row 5 and the lock read that record, so `Plan` no longer reads the last published allocation. The record also keeps a formation that is briefly busy (a battle on the march) without freeing the lock (GAP-04).
3. The remaining pass-12b gaps (GAP-01, 02, 04, 05, 07 and the ten minors) are folded as the dry-run seat proposed.
4. No pass 13b: phase 2 goes to crit round 3. A builder who meets an unanswered gap logs it in `docs/loop/foresight/gaps-build-phase2.md` (new) and stops on a contradiction.

The user may override each of these. Spec: D11 requirement-commitment-mover, requirement-decision-end, requirement-department-reinforcement.
