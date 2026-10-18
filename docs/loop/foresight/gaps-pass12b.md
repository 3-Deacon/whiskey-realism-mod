# D11 build dry run, pass 12b: phase 2

Worktree `.worktrees/plan-foresight` at `b87c7e87` (read only). S = spec D11 in `docs/superpowers/specs/2026-10-14-campaign-planner-design.md` (S:n = line), P = `docs/superpowers/plans/2026-10-18-strategic-foresight.md`, T = `docs/loop/foresight/test-plan.md`. Shipped code is under `src/WhiskeyRealism/Strategic/Planner/` (file names shortened). Decompile: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`. Phase 1 (F1-F5, F9) is assumed built as P says. The integrator's three pass-11b decisions (`docs/loop/foresight/decisions.md`, third round) are taken as given.

**First-priority verdict.** The three decisions are folded and they close pass 11b's majors. Commitment ends with D4's push (S:337 *Ends*). The department lock is read from live state (S:333 *Lock*). The home department changes only on arrival (S:333 *Home department*). They open or expose seven new majors:

- The end rule contradicts the plan's own test and the spec's conflict paragraph: an empty mass now *is* the end (GAP-01).
- A challenger's capacity while the push's corps are claimed `Committed` is undefined (GAP-02).
- An ended push's corps keep the march they were given (GAP-03).
- The live lock cannot be computed at plan time as worded, and its D6 clause never applies (GAP-04).
- D6's own donor test is left on the home department while the have moves to standing (GAP-05).
- A load silently drops every department move and its lock (GAP-06).
- The commitment-end review ejects the covering incumbents that S promises will return (GAP-07).

Not build-ready for phase 2: 7 majors.

## Gaps (17: 0 blocker, 7 major, 10 minor)

**GAP-01. The end rule clears `LastMass` on an empty mass, but P and T still pin `AnEmptyMassKeepsTheLastMass`, and S:365 keeps the pre-decision clear rule.** [element: commitment]

What S says:
- S:337 *Ends*: "the commitment ends on the review `Decide` no longer keeps the last mass's objective (a switch, or no main effort) ... and the last mass is cleared (or replaced by the new mass)".
- S:365 (*Shared rows*): "`PlannerState.LastMass`: ... replaced by a non-empty mass, cleared when its objective is no longer a candidate". That is the rule the third-round decision replaced.

What P and T say:
- P:350 lists `AnEmptyMassKeepsTheLastMass`.
- P:354's PROVE plant reads "let an empty mass clear the last mass -> `AnEmptyMassKeepsTheLastMass` red".
- T:48 TC-COM-KEY lists the same test.

Why the mass is empty only on an end review (shipped code):
- A chosen candidate is `Eligible`, and that requires `Force >= 0` (TheaterPlanner.cs:379).
- `PickForce` returns -1 only for an empty capacity (TheaterPlanner.cs:1154-1170).
- `SelectMass` always takes at least the first army, because `need >= MinGarrisonStrength > 0` (TheaterPlanner.cs:1043-1047, 1083-1088).
- So an empty mass happens exactly when `Decide` returns -1 (no main effort).

Under S:337 that review ends the commitment and clears `LastMass`. Under P:350, P:354 and S:365 it keeps it, which is the pre-decision stall (pass-11b GAP-01) back again.
- Severity: major. It is a contradiction between S and P/T, the class P:41 tells the builder to stop on.
- Impact:
  - The test cannot be written red-first against S.
  - The PROVE plant is the correct behaviour under S.
  - A builder who follows S:365 or P keeps a stalled push committed indefinitely.
- Assumption: S:337 governs. Then:
  - S:365 reads "replaced by a non-empty mass, cleared on the review `Decide` no longer keeps its objective".
  - `AnEmptyMassKeepsTheLastMass` becomes `ANoCandidateReviewClearsTheLastMass`, and P:354's plant becomes "keep the last mass on an empty mass -> `CommitmentEndsWhenD4EndsThePush` red".
  - T:48 follows.
- Grounding: shipped code (TheaterPlanner.cs:379, 408-416, 1043-1047, 1083-1096, 1154-1170)

**GAP-02. S does not say whether a challenger's capacity, computed in `Evaluate` before `Decide`, admits a ready formation claimed `Committed` to the current push.** [element: commitment]

What S says:
- Rows 1-7 run inside `Evaluate`, before every candidate's `MassFor` (S:310). Row 4 claims the push's members `Committed`.
- Row 8's *takes from* admits `Committed` only "to the chosen objective first ... a formation whose commitment ends this review only as an ordinary D3 member when `MassEligible`" (S:321). Which objective is chosen, and whether the commitment ends, are not known until `Decide` runs.
- `Massable` for a candidate P other than the last mass's objective O is plain `MassEligible` (S:337 *In the capacity*).
- "every `MassFor` call after `Decide` sees the same rows 1-7" (S:310).

There are two readings for a ready O-committed corps:
- (a) It is in P's capacity, as shipped `MassFor` admits every `MassEligible` formation to every candidate (TheaterPlanner.cs:985-1001). P's odds, balance and score then count the push's own army while O is still the main effort, and the margin switch (TheaterPlanner.cs:433-451) is judged on it.
- (b) It is not in P's capacity. P is judged without it, and on a switch review `SelectMass` cannot take it, because its capacity is the same pre-`Decide` list. That contradicts S:337 ("a member the new mass takes as an ordinary `MassEligible` formation is `Mass`") and the test `AnEndedPushsReadyCorpsMayJoinTheNewMassAsOrdinaryMembers` (P:350, T:48).
- Severity: major
- Impact:
  - Different switch decisions.
  - The switch test cannot be written red-first.
  - `MassTakesOnlyItsListedClaimants` (P:293) fixes one reading or the other.
- Assumption: (a), which reproduces the shipped capacity. S:321's cell adds "for every other candidate's capacity, a `Committed` formation counts as an ordinary member when `MassEligible`". Test: `AChallengersCapacityCountsTheReadyCommittedCorps`.
- Grounding: shipped code (TheaterPlanner.cs:338, 433-451, 976-1011)

**GAP-03. A corps whose push ended keeps the march it was given: a `Hold` role issues no order, and nothing stops a waypoint already set.** [element: decision-end]

What S says:
- S:339: a formation whose decision ended "is placed by the holds ... where it stands".
- S:337: on the end review it is "`Hold` in place ..., reason `committed-wait`".
- S:338: the mover takes it off `unitsinoffensiveoperations` "so vanilla's job 6 does not place it instead".

What the code and vanilla do:
- The mover dispatches only the roles in `Priority`, and `Hold` is not among them (CampaignMover.cs:115-116, 142-145).
- `IMoveHost` / `IMoverHost` have `Move`, `ListOffensive` and nothing that stops a formation (MoveIssue.cs:6-14, CampaignMover.cs:12-20).
- Vanilla's own relief removes a unit from its lists and leaves its path (`RelieveUnitsInOperations` 14068-14078).
- An issued campaign march continues at low readiness (`CheckReadinessActions` 113937-113965, S:263).

A corps the mover sent onto the old objective's enemy ground therefore marches on alone after the push ends, and arrives as the piecemeal attack that RC4 and requirement-decision-end exist to prevent. The de-list stops job 6 re-targeting it; it does not stop the march in progress. The holds then "place" it where that march leaves it. S does not mention this case.
- Severity: major
- Impact:
  - The behaviour the user asked for ("troops where the threat is") silently fails for exactly the corps the end rule is about.
  - No test or smoke row shows it (TC-END-SMOKE counts `endedListed`, not position).
- Assumption: as shipped, with no stop (a stop would be a new campaign write path, the integrator's to approve). S:339 names the case as accepted. F7's smoke counts it, for example `[Reserves] endedMarching=<n>`: formations of the previous review's `CommitmentEnded` whose waypoint is still more than `ArrivalRadius` from their position. The snapshot can read that; whether the waypoint is on enemy ground needs the mover's host. Alternatively, route to the integrator: give committed-wait formations a `MoveUnitTo` to their own position, which needs `WlChainRules.MayMove` and a tracker record.
- Grounding: shipped code (CampaignMover.cs:12-20, 115-116, 316-351; MoveIssue.cs:6-14), vanilla (14068-14078, 113937-113965)

**GAP-04. The live lock cannot be read as worded at plan time, and its D6 clause never locks anything.** [element: department-reinforcement]

What S says (S:333 *Lock*): a department is locked "while a formation of the last published allocation holds a reinforcement role toward it (`Redeploy department-reinforcement` with its target in the department's sectors, or D6's `Support reinforcement` toward a main effort in it), and released when that formation arrives ... or its role changes".

The timing problem:
- The lock is read by row 10 inside `Plan`.
- Today's roles exist only after `ForceAllocator.Allocate`, which runs after `Plan` (CampaignPlannerRuntime.cs:139-153).
- At plan time the only signal of "its role changes" is today's claim: whether row 5 claimed it `DepartmentMove`.

That gives two readings:
- (a) The lock is yesterday's allocation alone. It then lingers one review after arrival or a role change, against "released when that formation arrives".
- (b) The lock is today's row-5 claim toward the department.

Under (b), the D6 clause is vacuous:
- The pick runs only on a review with no main effort (S:333 *Pick*), and on such a review no formation is Support toward a main effort.
- On the first quiet review, yesterday's D6 reinforcement is a last-mass member, claimed `Committed` and then committed-wait (S:337), never `DepartmentMove`.

Under (b), a brief interruption also frees the lock while the move goes on:
- A formation in a battle on the way is `Immobile` (row 2, S:315), so row 5 does not claim it (row 5 needs "`None` after rows 1-3").
- The lock is released, and the same review's row 10 sends a second formation.
- The first formation's vanilla march continues. The next review re-decides it "from scratch" (S:333), so it is never claimed again.
- Severity: major
- Impact:
  - `ADepartmentIsLockedWhileAReinforcementMovesTowardIt` and `TheLockReleasesOnArrivalOrARoleChange` (P:363, T:54) fix different behaviour under (a) and (b).
  - The D6 clause has no test that can fail.
  - Under (b) a single battle on the march doubles the transfer, the stream the lock exists to prevent.
- Assumption: (b). A department is locked on a review exactly when row 5 claims a `DepartmentMove` toward it this review. The D6 clause is deleted, because a push already blocks the pick.
  - A battle on the march is one more interruption on S:333's list ("a push, a capital take, Recover, a give-up, P8"). The lock frees and the formation re-decides. A second pick the same review is accepted and named in S:333.
  - Test: `TheLockIsTodaysDepartmentMoveClaim`, `ABattleOnTheMarchIsAnInterruption`.
  - Alternative for the integrator: key row 5 and the lock on the mover's in-transit record, not on yesterday's allocation. That record carries the role and the target (`MoveTracker.TryGetMove(..., out string role)`, CampaignMover.cs:185) and is persisted (`ReboundMoves`, CampaignPlannerRuntime.cs:302). Keyed on "an in-transit `Redeploy` record whose target lies in d", the lock would survive a busy review and a load, which would also close GAP-06. Unverified: whether a battle leaves the record in transit (`MoveTracker.Judge` may mark it `Failed`).
- Grounding: shipped code (CampaignPlannerRuntime.cs:139-157, 302; TheaterPlanner.cs:123-125; CampaignMover.cs:185)

**GAP-05. D6's donor test still names the candidate's home department while the have it reads becomes by-standing.** [element: department-reinforcement]

What S says (S:333 *Have, one definition*):
- `DepartmentHave(d)` counts the formations that *stand* in `d`.
- It "is the donor test and D6's `DepartmentHoldMet` have (F8 re-pins D6's tests by name)".
- Row 10's own donor test is applied to the department the formation stands in (`DepartmentHave(d') - its HoldStrength >= DepartmentNeed(d')`).
- S does not say which department D6's test is asked about.

What the code does:
- Shipped `ReinforcementPick` calls `DepartmentHoldMet(c, DepartmentOf(c, i), i)` with the *home* department (TheaterPlanner.cs:1029).
- Shipped `DepartmentHoldMet` sums the formations whose home is that department (TheaterPlanner.cs:955-968).
- P:360 refactors `ReinforcementPick` only to "take its lock as a parameter". Its internal home-department donor test therefore stays, and the department pick, which reuses it, would apply it too, against S:333.

With a by-standing have and a home-department argument, D6's test is vacuous for a formation that stands outside its home: excluding it changes nothing in its home's have. The department it actually leaves is never tested. That is pass-11b GAP-06 again, on D6's path.
- Severity: major
- Impact:
  - D6 can strip the department a reinforcement stands in (for example an ended push's corps, requirement-decision-end).
  - `D6sDepartmentHoldMetUsesDepartmentHave` (P:363, T:54) has no defined expected value for that formation.
  - The shared `ReinforcementPick` cannot serve row 10 unchanged.
- Assumption: both paths test the department the formation stands in, `TheaterOf(HoldSectorOf(f))`. `ReinforcementPick` takes its candidate list and its donor-department function as parameters as well as the lock. Test: `D6TestsTheDepartmentTheReinforcementStandsIn`.
- Grounding: shipped code (TheaterPlanner.cs:941-945, 955-968, 1017-1038)

**GAP-06. A load drops every department move and frees every lock, and S does not say so.** [element: department-reinforcement]

What S and P say:
- S:365: "department reinforcement keeps no state of its own (its lock and moves are read from the last published allocation)".
- P:360: "No new sidecar field".
- The last published allocation lives in `CampaignPlanStore`, which `CampaignScope` clears (CampaignPlanStore.cs `Reset`, registered in its static constructor). F1's capture is in memory.

On the first review after a load:
- Row 5 claims nothing and every department is unlocked.
- Row 10 may pick a second formation for the same department, while the first one's vanilla march continues. The tracker's move records are restored (CampaignPlannerRuntime.cs:302, `ReboundMoves`), but no role sends the formation any more.
- The first formation never gets its arrival transfer, because arrival is detected only through yesterday's `Redeploy` (S:333). It stays homed in the donor department while it stands in the receiver.

Commitment (`lastMass`) and the covering force (`coveringKeys`) are persisted for exactly this reason (P:34). Marks name their after-load behaviour, "accepted and logged" (S:290). The department path names none.
- Severity: major
- Impact:
  - Every save/load during a transfer doubles it.
  - `TheLockReleasesOnArrivalOrARoleChange` and `TheHomeDepartmentChangesOnlyOnArrival` have no defined result across a load.
- Assumption: accepted and named. After a load the department moves and locks restart from the next review, and S:333 says so. Alternatively, persist the moving formations' unit keys and receiving theater in the faction's `planner` node, the way `lastMass` is. Test: `ALoadForgetsDepartmentMovesAsNamed` (or `DepartmentMovesRoundTripInTheSidecar`).
- Grounding: shipped code (CampaignPlanStore.cs `Reset` / static constructor; CampaignPlannerRuntime.cs:279-304)

**GAP-07. On the review a push on the enemy capital ends, the covering incumbents are claimed `Committed`, are not reselected, and are dropped from `CoveringKeys`, although S says they return when the push ends.** [element: covering-force]

What S says:
- S:306: on the push's days `CoveringKeys` "is left untouched so the incumbents return when the push ends".
- S:304: the covering state is left untouched only on a `Kind = None` day with reason `main-effort-enemy-capital`.
- S:303: an incumbent is taken first only while it is "still claimable (claimant `None`, `MassEligible`, ...)"; "A formation that leaves the force (not reselected, its claim changed) is dropped from `CoveringKeys`".

Trace (incumbent K taken into the push on the enemy capital):
1. K becomes `Mass` and so a last-mass member.
2. On the review `Decide` ends the push, `Evaluate` still reads the push as current, so row 4 claims K `Committed` before row 7's covering pick (S:310, S:337).
3. After `Decide` there is no enemy-capital push, so `Kind` is not `None` for `main-effort-enemy-capital`, and the covering state writes run.
4. K was not reselected and its claim changed, so it is dropped.

The same drop happens to any incumbent that dips below readiness step 2 (not `MassEligible`) for one review.
- Severity: major. S states two incompatible outcomes, and the second one is what a builder following S:303 builds.
- Impact:
  - The push's covering corps lose their recorded sector and their incumbency the first review after the push.
  - `EnemyCapitalPushLeavesTheCoveringStateUntouched` (P:332) passes either way.
  - No test pins the return.
- Assumption: an incumbent claimed `Committed` or `Immobile`, or failing only `MassEligible`, keeps its `CoveringKeys` entry that review. It is counted by position as the non-massable count already does (S:303 *The non-massable count*), and taken again first once it is claimable. Test: `IncumbentsReturnAfterTheEnemyCapitalPushEnds`, `AReadinessDipKeepsTheIncumbency`.
- Grounding: none (S's own rules S:303, S:304, S:306, S:310, S:337)

**GAP-08. `deptShort=` statuses are "computed before the pick" yet `donor` means "the picked department", and no status is given during a push.** [element: department-reinforcement]

What S says:
- S:333: `DepartmentShort` is "computed before the pick; status `donor` for the picked department, `locked` ..., `nodonor` for every other".
- The first unlocked department may find no donor (no fall-through).
- During a push the pick does not run, but `[Reserves] deptShort=` is written every review (S:358).

criterion-department-reinforced (S:410) counts "`donor` ... a `Picked` row follows within 1 review: 100 %":
- If `donor` is set before the pick, a donorless or in-push review fails a correct build.
- If it is set after the pick, the criterion is tautological (the `Picked` row is the same review's).
- Severity: minor
- Impact: `DepartmentShortStatusesBeforeThePick` (P:363) cannot fix one expected value. The criterion is either failing or vacuous.
- Assumption:
  - `donor` means a donor was found and picked this review. The first unlocked department with none reads `nodonor`.
  - Every department reads `push` during a push.
  - The criterion becomes "on every quiet review the first ranked unlocked short department either has a `Picked` row the same review or no formation meets row 10's pool test (harness-pinned)".
- Grounding: none

**GAP-09. criterion-mass-kept reads "the last mass's objective" candidate on the very review `LastMass` is cleared, and that candidate may not exist.** [element: other]
- S:358: "the trace always carries the last mass's objective candidate".
- `Trace` is built at the end of `Plan` (TheaterPlanner.cs:275), after the last-mass update has cleared or replaced `LastMass` (S:310 order).
- On the `no-candidate` review the criterion reads (S:407), a builder who reads `state.LastMass` at trace time finds none.
- A `no-candidate` review whose objective became unavailable or accomplished has no candidate record for it at all, because `Evaluate` skips it (TheaterPlanner.cs:334).
- Severity: minor
- Impact: TC-COM-SMOKE (T:50) reads "missing" on a correct build.
- Assumption:
  - The trace carries the objective of `LastMass` as it stood before today's update.
  - The criterion counts only reviews whose trace holds that candidate.
  - `switch=` other than `odds` or `no-candidate`, and a `no-candidate` with the objective unavailable or accomplished, are excluded.
- Grounding: shipped code (TheaterPlanner.cs:275, 334, 1462-1493)

**GAP-10. The flag-off wording disagrees between S:310 and S:337, and does not say whether the `CommitmentEnded` de-list or row 5 runs with its flag off.** [element: commitment]
- S:310: "its state (the last mass, covering keys) is kept and ignored, and the bookkeeping that ends or moves existing state still runs".
- S:337: "the last-mass update still runs (bookkeeping)". Replacing the last mass is not "kept".
- With `Enable Main Effort Commitment` off, S does not say whether `CommitmentEnded` is published and de-listed by the mover. That is a vanilla list write, so a rollback that still de-lists is not the pre-D11 behaviour (P:53, "D5's readiness rule for every mass member, no last-mass exemption").
- With `Enable Department Reinforcement` off, S does not say whether row 5 keeps claiming an in-flight `DepartmentMove` ("moves existing state") or drops it ("no department transfer", P:54).
- Severity: minor
- Impact: `FlagOffRestoresD5` and the department flag-off test pin one reading or the other.
- Assumption:
  - The last-mass update runs. `CommitmentEnded` is published but the mover de-lists it only with the flag on.
  - With the department flag off, row 5 claims nothing (the formation falls to the holds where it stands).
  - S:310 reads "the last mass is updated and ignored".
- Grounding: none

**GAP-11. `IClaimSource.Produce(s, state, claims)` lacks the inputs rows 4 and 5 read.** [element: ownership-table]
- P:289 declares `Produce(CampaignSnapshot s, PlannerState state, Claim[] claims)`.
- Row 4 reads the give-ups for the last mass's objective (S:310 *Give-ups*). Those are a `ClaimTable.Build` parameter, not part of the `Produce` arguments.
- Row 5 reads the last published allocation, the sector model and each sector's `Facing` (S:333 *Moving*).
- Severity: minor
- Impact: the builder changes the interface, or injects per review through constructors. `RowSourcesAreEmptyInF6a` and the fixture registry depend on that shape.
- Assumption: `Produce(in ClaimContext ctx, Claim[] claims)`, where `ClaimContext` carries the snapshot, sectors, `SectorFacts`, state, give-ups and previous allocation.
- Grounding: none

**GAP-12. The receiving department is not defined against renumbered sectors, and a receiver that stops facing leaves the move with no target and no arrival.** [element: department-reinforcement]
- Row 5 and the lock key on yesterday's `Redeploy department-reinforcement` "with its target in the department's sectors" and "its receiving department" (S:333).
- Sector ids renumber when an objective is accomplished or a secession objective toggles (S:303, SectorModel.cs `Build`: seeds per live objective, numbered in area order).
- Yesterday's `Assignment.SectorId` read under today's model may name another theater. The covering force uses `areaIndex` for exactly this reason (S:303).
- If the receiving department has no facing sector today, the target ("the receiver's neediest sector's centroid", a facing sector) is undefined, and arrival ("stands in a facing sector") can never happen.
- S:329 also gives `Redeploy` a second arrival rule ("within `max(BugleRange, ArrivalRadius)` of the point it is an ordinary `Hold`"), which the department path does not use.
- Severity: minor
- Impact: row 5 can redeploy a formation toward the wrong department after a renumber, or claim it `DepartmentMove` with no target forever.
- Assumption:
  - The receiver is `TheaterClassifier.FromPosition(yesterday's TargetX/Z)`, which is the target sector's own theater (SectorModel.cs: a sector's theater is `FromPosition` of its centroid).
  - With no facing sector in the receiver, row 5 claims nothing.
  - S:329 states that `department-reinforcement` arrives by S:333's rule.
- Grounding: shipped code (SectorModel.cs `Build`, theater from centroid)

**GAP-13. Where in `Plan` the arrival `Transfer` is written is not stated, and `AssignDepartments` has already read the homes for the review.** [element: department-reinforcement]
- S:333: "`PlannerState.Transfer` on the review it first stands in a facing sector of the receiver". Arrival needs `Facing`, which is built in `Evaluate`, and `Evaluate` runs twice on a terrain-filling review (TheaterPlanner.cs:209-219).
- `AssignDepartments` fills `c.Department` before `Evaluate` (TheaterPlanner.cs:206, 909-921).
- The covering force says "Every `PlannerState` write here is applied once in `Plan` after `Decide`" (S:304). The department path says nothing.
- Severity: minor
- Impact: the home seen by `HoldKey`'s discount, the capital relief race (S:295) and `MassFor`'s natives that review depends on the builder's choice. `TheHomeDepartmentChangesOnlyOnArrival` does not say on which review the new home is read.
- Assumption: the transfer is applied once, after `Decide`, beside the covering state writes. `c.Department` is not refreshed that review, and the new home applies from the next review.
- Grounding: shipped code (TheaterPlanner.cs:206-219, 909-921)

**GAP-14. Row 10's pool does not exclude D8's secession pledges, and G4 assigns the `Redeploy` claims before it seats them.** [element: department-reinforcement]
- `PostFromOutside` (row 9) pledges formations through `c.GarrisonReserved` and `post.InstanceIds`, and leaves their claim `None` (S:322; TheaterPlanner.cs:828-830, 857-858).
- Row 10's pool skips the retained G8 incumbents and the recon missions only (S:310, S:333).
- G4 assigns "the `Redeploy` claims" before "D8's secession seats" (S:325), and `AssignGarrisons` skips a formation already done (ForceAllocator.cs:494).
- So a pledge picked by row 10 leaves its post named in `TheaterPlan.Garrisons` and unmanned.
- Severity: minor
- Impact: `NeverAReserveMassCoveringGarrisonOrBuilder` (P:363) does not say which garrisons are meant.
- Assumption: row 10's pool also skips `c.GarrisonReserved`. Test: `ASecessionPledgeIsNeverADonor`.
- Grounding: shipped code (TheaterPlanner.cs:808-866; ForceAllocator.cs:484-504)

**GAP-15. The readiness skip names two different enemy-ground tests.** [element: commitment]
- S:338 says the skip applies "when the target is enemy ground by the mover's own `EnemyHeld` test", and in the same sentence that "the skip asks the state the way vanilla does, `IsOnEnemySoil(false, target, returntrueifnotonstate: true, usestateoccupation: true)` ... a point on no state counts as enemy".
- Shipped `EnemyHeld` returns false for a point on no state (`ground >= 0`, CampaignMover.cs:376-380) and reads the state owner, not occupation.
- P:345 says "by vanilla's state lookup".
- Severity: minor
- Impact: `APointOnNoStateCountsAsEnemyForTheSkip` (P:350) is red under the first reading and green under the second.
- Assumption: the `IsOnEnemySoil` mirror, added to `IMoverHost`. S:338 drops "by the mover's own `EnemyHeld` test".
- Grounding: shipped code (CampaignMover.cs:376-380), vanilla (139304-139336)

**GAP-16. `ListSurpluses` lists a committed-wait formation in a sector with no need as a surplus.** [element: ownership-table]
- S:310 lists the readers that skip `committed-wait`: `Counted`, `CountedHold`, `ListShortfalls` and `WithholdHoldPrefixes`.
- `ListSurpluses` takes every `Hold` that is not immobile, not withheld, and stands in a sector with `_need <= 0` (ForceAllocator.cs:1161-1178). A committed-wait formation is not withheld (S:337), so on the end review it is listed, and G5b may route from it.
- Severity: minor
- Impact: a one-review surplus row that no S rule names.
- Assumption: `ListSurpluses` skips `committed-wait`, and S:310 adds it to the list.
- Grounding: shipped code (ForceAllocator.cs:1161-1178)

**GAP-17. P:293 still says "in-flight claim".** [element: other]
- P:293 `F6aReproducesTheShippedRoles`: "no committed or in-flight claim and no covering input give the shipped roles exactly". No in-flight claim exists. The claimant is `DepartmentMove`.
- S:306's "In-flight members" (covering members on the march) is fine in meaning but uses the retired term.
- Severity: minor
- Impact: a fixture written around a record that does not exist.
- Assumption: P:293 reads "no `Committed` or `DepartmentMove` claim". S:306 reads "Marching members".
- Grounding: none

## Pass-11b gaps checked (adequate | inadequate -> GAP-NN | moot)

- GAP-01 (stalled push never ends): adequate. S:337 *Ends* ties the end to `Decide`, under the integrator's decision. The fold left contradicting text in S:365, P:350, P:354 and T:48 -> GAP-01 (new).
- GAP-02 (committed-wait role sector): adequate. S:310 gives role sector -1 and lists the skipping readers; T:48 has `ACommittedWaitIsCountedInNoHoldInG3OrG4`. `ListSurpluses` is not in that list -> GAP-16.
- GAP-03 (busy members lose commitment): adequate. S:337 keeps a non-`Movable` member in `LastMass`; T:48 has `ABattleKeepsTheLastMassMembership` and `AVanillaDefenceLoanResumesTheCommitment`.
- GAP-04 (`DepartmentHave` excludes `Committed`): adequate. S:333 excludes `Mass` only; T:54 has `HaveExcludesTheMass` and `AStalledPushDrawsNoDonor`.
- GAP-05 (D6's lock in the quiet period): adequate in principle (the lock is derived from live state, S:333). Its evaluation point and its D6 clause -> GAP-04.
- GAP-06 (donor by home vs have by standing): adequate for row 10 (S:333 filters by where the formation stands). Left on D6's path -> GAP-05.
- GAP-07 (an interrupted transfer strands its formation): adequate. The home changes only on arrival and the next review re-decides (S:333). The load case -> GAP-06.
- GAP-08 (three readings of row 8's committed take): adequate. S:321 and S:337 agree, and P:350/T:48 use `EveryFormationCommittedToTheChosenObjectiveIsTakenWhateverTheNeed`. The pre-`Decide` capacity -> GAP-02.
- GAP-09 (`[DepartmentReinforcement]` states): adequate. S:333, S:359 and P:366 all list `Picked|Arrived|GaveUp`, each with its trigger.
- GAP-10 (published members, `NoteReinforcement`): adequate. P:31 and P:370 list `DepartmentShortfall`, with no `DepartmentMoves` and no `ReinforcementTargetX/Z`. The department pick does not use `NoteReinforcement`.
- GAP-11 ("in flight" test names): adequate (T:37, P:293, P:299 use `...CommittedOrDepartmentMove`). One remaining phrase in P:293 -> GAP-17.
- GAP-12 (de-list keyed on the mover's own listing): adequate. S:338 names the job-4 case as accepted.
- GAP-13 (`endedListed` strictness): adequate. S:358 and T:51 read "0 on the review after each end".
- GAP-14 (trace cap, row 6 and `committed=`): adequate as far as it went (S:358). The timing of the read and an absent candidate -> GAP-09.
- GAP-15 (flag-off last-mass update): adequate in S:337. The S:310 wording and the de-list -> GAP-10.
- GAP-16 (no fall-through): adequate ("Only the first ranked unlocked department is tried; there is no fall-through", S:333). The status meaning -> GAP-08.
- GAP-17 (give-up key for the department): adequate ("no standing `GaveUpMove` whose target lies in the department's sectors", S:333).
- GAP-18 (stale sortie in the capital race): adequate (S:295 reads "a marching one included").

## Tally by element

| Element | Blocker | Major | Minor |
|---|---|---|---|
| ownership-table | 0 | 0 | 2 (GAP-11, 16) |
| covering-force | 0 | 1 (GAP-07) | 0 |
| commitment | 0 | 2 (GAP-01, 02) | 2 (GAP-10, 15) |
| department-reinforcement | 0 | 3 (GAP-04, 05, 06) | 4 (GAP-08, 12, 13, 14) |
| decision-end | 0 | 1 (GAP-03) | 0 |
| redeploy-role | 0 | 0 | 0 |
| other | 0 | 0 | 2 (GAP-09, 17) |
| **Total** | 0 | 7 | 10 |

## Checked and clean

- **Removed lifecycle text.** No requirement, table cell or test still reads sorties, `DepartmentInFlight`, an in-flight record, origins, `Ended|Pruned`, `ReinforcementTargetX/Z`, `DepartmentMoves`, a return lifecycle or D6's decision lock for the department path.
  - The remaining mentions name what was removed: the decision records S:380-382, the integrator notes S:251, S:333, S:337 and S:339, and P:275's history of pass 5b.
  - "Without D6's decision lock" appears at S:333 and P:360 ("the department pick none").
  - The negative pins are `NoReturnRoleOrReasonExists` (P:350) and T:51's `return-home` check.
  - The two stale phrases are GAP-17 (P:293, S:306). The stale end rule is GAP-01 (S:365).
- **Row numbers 1-10.**
  - The table (S:314-323) is 1-10, with row 9 D8 and row 10 the department pick.
  - Every textual reference agrees: S:303 "row 7"; S:306 "a step after row 8"; S:310 "Rows 1-7", "rows 8-10", "row 9", "row 10", "rows 4 and 5", "row 5", "row 4"; S:317 "via row 8"; S:320 "rows 1-7"; S:333 "row 10", "row 5", "rows 1-3"; S:337 "row 4", "row 8", "rows 1-7"; S:358 "rows 1-7".
  - The plan agrees: P:42 "rows 4, 5 and 10"; P:277 "rows 1-7", "rows 8-10", "row 7"; P:288; P:293; P:294 "row 2", "row 7"; P:295; P:296 "row 8"; P:335; P:336 "row 7"; P:345 "row 4"; P:360 "row 10", "row 5".
  - No row 11 or 12 is cited.
- **The three integrator decisions are folded.**
  - The commitment end reads `Decide` with no new constant (S:337; P:350 `NoNewConstantDecidesTheEnd`; S:381 decision record).
  - The lock comes from the last published allocation, with no record and no counter (S:333, S:365, S:380).
  - The home changes only on arrival (S:333; P:363 `TheHomeDepartmentChangesOnlyOnArrival`, `AnInterruptedPickStaysTheDonors`, `TheNextReviewReDecides`).
- **D4's end rule fits the shipped hysteresis.** `Retain` keeps an objective eligible for fewer than `SwitchDays` reviews within `OddsDeadband` (TheaterPlanner.cs:474-494), `Trigger` gives `odds`, `accomplished` or `unavailable` once `held < 0` (512-548), and `best < 0` gives `no-candidate` (408-416). Each one is an end under S:337, with no new state.
- **Busy members across a push.** A member in battle or a vanilla defensive operation is `Immobile` (row 2, through `Busy`, TheaterPlanner.cs:123-125), stays in `LastMass` (S:337), and is `Committed` again once movable, whatever its readiness. The covering pick and row 10 cannot take it, because they take `None` only.
- **The D6 reinforcement inside a push.** It is a last-mass member and is committed (S:337). `ReinforcementInstanceId` is set while it is in the mass, and `NoteReinforcement`'s transfer at the pick is unchanged (TheaterPlanner.cs:1100-1112). D6 is not touched by the arrival rule, which S:333 scopes to the department path.
- **The covering force's points, posts, axis shares, verdict and defence clause** are unchanged by the third-round decisions. The only interaction found is GAP-07.
- **`committed-wait` readers.**
  - `ArmyGroupDoctrine.PlanGroups` skips `SectorId < 0` (ArmyGroupDoctrine.cs:28).
  - `SteeringRules` centres a `Hold` on its position (SteeringRules.cs:101-105).
  - `PostCandidate`, `IsHoldReserve` and `Free` refuse a done `Hold` whose reason is not `hold-reserve` (ForceAllocator.cs:569-578, 597-600, 758-762).
  - `WithheldFromDefense` returns false for a formation that is not withheld (CampaignWithholdView.cs:131).
  - The one reader not covered is GAP-16.
