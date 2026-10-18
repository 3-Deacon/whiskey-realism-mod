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
3. The remaining pass-12b gaps (GAP-01, 02, 05, 07 and the ten minors) are folded as the dry-run seat proposed. GAP-04 is folded through the `DepartmentMoves` record of item 2, the seat's named alternative, and not through its first proposal (the lock as today's row-5 claim), because the GAP-06 decision supplies the record. The user may override this.
4. No pass 13b: phase 2 goes to crit round 3. A builder who meets an unanswered gap logs it in `docs/loop/foresight/gaps-build-phase2.md` (new) and stops on a contradiction.
5. The stop leaves one grounded gap open: a march still waiting in vanilla's order queue is served after the push ends. It is routed as `question-stop-queued`.

The user may override each of these. Spec: D11 requirement-commitment-mover, requirement-decision-end, requirement-department-reinforcement.

## Integrator decisions, 2026-10-18 (the five open questions, decided under the user's direction)

The user directed (2026-10-18): "AI should be intelligent enough to look at the frontier, competitors and industry future / standards to make the best calls", and "we want to be on par at the minimum, but doing better than the competition". The integrator therefore decides each open question now instead of holding it for a gate. The smoke rows the plan names still report. The user may override each decision.

1. `question-stop-readiness` and `question-stop-queued` (phase 2): neither option. The stop uses vanilla's own campaign stop, the pair `regiment.StopRegiment(skipfinalrotation: true, manualstop: true, ...)` then `DefensiveMovingOrder.RemoveOrder(regiment)` (13350-13351, the port-operation branch of `CheckFleetMovements` at 13148; `RemoveOrder` 9949-9963 drops every queued order of that regiment). No `SetWaypoint` runs, so vanilla's readiness check is not met and needs no skip. The queued march is removed through vanilla's own removal, so the hole closes. The stop stays a mover entry (`Hold`, `committed-wait`), issued only to a formation that is still marching or has a queued order (`DefensiveMovingOrder.OrderRunning`, 9933-9947), and it is counted by `[CampaignMover] stopped=`. Ground: vanilla's own AI stops a campaign unit this way; option (b) left a corps marching on an abandoned objective, and no reference AI ships that.
2. `question-capital-share` (phase 1): (b), the importance-proportional share from vanilla's importance values. `CapitalShare 0.4` is deleted. The post-F4 rows still report `capitalShareProp=`, and the East's `[Reserves] dept=` strength against `0.6 x enemyCapitalEstProp` is reported as the covering force's expected short. Ground: a fixed 0.4 is a tuning constant; vanilla already ranks its towns, and the estimate should follow vanilla's own weights.
3. `question-intel-bug` (phase 1): (b). F4b fixes BUG-INTEL-001 on both sides behind `Fix Spotting Mark Timing` (default on, its own rollback flag, like the shipped bug fixes). It is built after F4 with no user gate. Ground: a mark timer that reads the concealment array is a vanilla defect. Fixing it on both sides keeps the planner fog-honest and fair.
4. `question-corridor-posts` (phase 2): (a). The posts are whatever the rule gives; `[CoveringCorridor]` reports them, and nothing is tuned to town names.
5. Phase 1 has no user gate after F4. The order is F4 -> F4b -> F5 -> F9 -> F10a.

Spec: D11 requirement-commitment-mover, requirement-picture-partition, requirement-marks-*, requirement-covering-force.

## Integrator decisions, 2026-10-18 (strategic foresight phase 2, fourth round)

Taken by the integrator on crit round 3's findings (verdict revise, covered commit `64289a51`), the last document round; folded with no further crit and no dry-run pass.

1. Commitment covers only the dispatched members (crit R3-9). The approved item reads "formations dispatched to the main effort stay in it through readiness dips", and this keeps its text: a member of the last mass is committed only when the mover dispatched it toward the last mass's objective, read from the move record the tracker already keeps (`ObjectiveId`, persisted with the record), collected for the plan as the give-ups are. A member the tempo gate deferred, or that the mover has not yet sent, is an ordinary member under D5's readiness rule. This narrows the second round's item 1: commitment still has no sortie record of its own, but it now reads the tracker's existing per-formation move record beside the last mass.
2. F6b is split in two slices (crit R3-10). F6b-1 is the covering corridor: the towns in the snapshot, the points, the posts, the axis shares and the `[CoveringCorridor]` row, taking no formation. F6b-2 is the covering force: the need, the verdict, the members, the hold, the defence clause and the lifecycle. The need and the verdict go to F6b-2 instead of F6b-1, as the crit named them, because each has its only `src` reader in the published `CoveringPlan` that F6b-2 adds, and the census and the orphan sweep fail a member or function with no `src` reader in the slice that adds it.
3. `endedListed` is a reported count with no hard 0 (crit R3-7). Vanilla's job 4 may list an ended corps again before the next review, which is a legitimate vanilla act, so the smoke reads the count against vanilla's relisting. `endedMarching` keeps its 0, over the formations the stop was for.

The user may override each of these. Spec: D11 requirement-commitment, requirement-commitment-mover and the `[Reserves]` row of requirement-telemetry; plan F6b-1, F6b-2 and F7.
