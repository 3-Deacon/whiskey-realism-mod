# D11 build dry run, pass 11b: phase 2

Worktree `.worktrees/plan-foresight` at `cc886c90` (read only). S = spec D11 in `docs/superpowers/specs/2026-10-14-campaign-planner-design.md` (S:n = line), P = `docs/superpowers/plans/2026-10-18-strategic-foresight.md`, T = `docs/loop/foresight/test-plan.md`. Shipped code under `src/WhiskeyRealism/Strategic/Planner/` (file names shortened), decompile `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`. Phase 1 (F1-F5, F9) assumed built as P says.

**First-priority verdict.** The simplification settles pass-10b's ownership and order gaps: row 4 keys on `LastMass`, which an empty mass keeps (GAP-01), the end is the last-mass update after `SelectMass` (GAP-02), and `CommitmentEnded` drives a de-list (GAP-03). It opens three new holes:

- **A stalled push never ends.** `LastMass` is cleared only by another objective's non-empty mass or by the objective leaving the candidate list. A push whose odds collapse can therefore park its corps as `Hold committed-wait` for as long as no candidate is eligible. They sit outside every hold, outside the donor pool and the covering pick, still on vanilla's offensive list for job 6 (GAP-01). Their absence from `DepartmentHave` draws a donor into that department (GAP-04).
- **Same-objective replacement drops every transiently busy member.** The non-empty mass replaces `LastMass` every review. A corps in battle, in a vanilla defensive operation, embarked or in transfer is not `Movable`, so it is not committed and not in that day's mass, and it loses its commitment for good. That is RC4 again, after a battle (GAP-03).
- **D6's lock does not fit the quiet period.** `DecisionSerial` does not tick on a no-candidate review. A push's own reinforcement therefore locks the whole following quiet period, and the quiet period allows one transfer however long it lasts. The removed decision record named exactly this reason ("`DecisionSerial` does not tick with no main effort, so 'per decision' is undefined") (GAP-05).

Department reinforcement also mixes the home department (the donor pool) with the standing department (the have), so a pick can be spent on a formation that already stands in the receiver (GAP-06). A pick interrupted by a push strands its formation with no rule to send it on (GAP-07). Not build-ready for phase 2: 8 majors.

## Gaps (18: 0 blocker, 8 major, 10 minor)

**GAP-01. A stalled push keeps its corps `Committed` indefinitely: outside every hold, the donor pool and the covering pick, and still on vanilla's offensive list.** [element: commitment]

What S says:
- S:337 *The last mass*: "an empty mass (a no-candidate review) leaves it as it is; it is cleared when its objective is no longer a candidate".
- *Ends*: "the exemption stops when another objective's non-empty mass replaces the last mass ... or the objective is no longer a candidate (won, accomplished, unavailable)".
- "A `Committed` claim the mass did not take (a no-candidate review, ...) is `Hold` in place that review, reason `committed-wait`".

Trace (an odds collapse with the objective still offered, so `Evaluate`'s filter still lists it, TheaterPlanner.cs:334):
1. Shipped `Decide` returns -1 and writes `CurrentMainObjective = -1` (TheaterPlanner.cs:408-416), with no `DecisionSerial` tick.
2. Each later review is another no-candidate review. The mass is empty, so `LastMass` stands.
3. Row 4 claims every member `Committed` again.
4. Row 7 never counts or reserves them (S:320). Row 10's pool takes only `None` (S:323). The covering pick takes only `None` (S:303). `CommitmentEnded` never names them, so S:338 never de-lists them.
5. Vanilla's job 6 (`UpdateMicroMovementInOffensive`) takes every listed unit with no path, not in battle and at readiness step 2 or more, and sends it to the nearest objective of its area (14004 path and battle, 14013 `GetClosestObjectivePosOfArea`, 14030 `GetReadinessStep < 2`, 14036 `MoveUnitTo`). It skips only capital defenders and depot builders (14023). Each committed-wait corps that recovers to step 2 is therefore marched alone onto the nearest enemy objective.

"That review" in S:337 is every review of the stall. Nothing in S or P bounds it.
- Severity: major (near blocker: every stalled push parks its corps this way, and RC4's own chain is a stall)
- Impact:
  - The holds reserve other bodies to replace corps that stand idle in their sectors.
  - The stalled push's department reads short and draws a donor (GAP-04).
  - Job 6 brings back the piecemeal attack that requirement-decision-end (S:339) says the de-list prevents.
  - `CommitmentSurvivesANoCandidateReview` (P:350) passes, because it covers one review.
- Assumption: a bounded wait mirroring `Retain`'s deadband (TheaterPlanner.cs:474-494). After `SwitchDays` (3) consecutive reviews with an empty mass, the plan clears `LastMass` and publishes its members in `CommitmentEnded`, so the mover de-lists them. Committed-wait formations are also taken off `unitsinoffensiveoperations` while they wait (the mover re-lists a member when it dispatches it again, CampaignMover.cs:350). Tests: `AStalledPushEndsAfterSwitchDays`, `ACommittedWaitIsNotOnVanillasOffensiveList`.
- Grounding: shipped code (TheaterPlanner.cs:334, 408-416, 474-494; CampaignMover.cs:350), vanilla (13968-14061)

**GAP-02. G4's hold sums and prefix pass count a `Hold committed-wait` formation that G3 never counted, and S gives it no role sector.** [element: ownership-table]

What S says:
- S:310 *Role sector and target* lists the pin's sector, `_sector[i]` for Immobile and Recover, the capital's, `Claim.SectorId` for Hold and every covering member, and the target sector for `department-reinforcement`.
- `Committed` -> `Hold committed-wait` has no entry. Its `Claim.SectorId` is -1, because row 4 counts "never" (S:317).
- S:337 says committed-wait is "not withheld".

What G4 does with a `Hold` in place:
- With role sector `_sector[i]`, shipped `Counted` adds it (ForceAllocator.cs:995-1001). So do `CountedHold`, the `[Reserves] hold=` row (S:358) and `ListShortfalls`.
- `WithholdHoldPrefixes` (1040-1063) may set `_withheld` on it, since its reason is not immobile (1050). S:305 special-cases only `covering-force`.

G3's tally (row 7) never saw the body, so G3 and G4 disagree on that sector's hold, the very class the claim table exists to end (S:378).
- Severity: major
- Impact:
  - `[Reserves] hold=` over-reports the sector by the waiting corps.
  - When the builder picks `_sector[i]`, as the other in-place Hold rows do, the prefix may withhold the committed-wait corps instead of a reserved body, against S:337, which leaves a hold reserve free for vanilla's offensive gather. With role sector -1 the shipped sums skip it unchanged, but S:310 does not say which.
  - `G3AndG4ReadOneOwnershipTable` cannot be written red-first: either reading passes one half.
- Assumption: committed-wait takes role sector -1 (no hold). `Counted`, `CountedHold`, `ListShortfalls` and `WithholdHoldPrefixes` skip reason `committed-wait`, as row 7 does, and S:310's list names it. Test: `ACommittedWaitIsCountedInNoHoldInG3OrG4`.
- Grounding: shipped code (ForceAllocator.cs:995-1001, 1040-1063)

**GAP-03. The same objective's non-empty mass replaces `LastMass` every review, so a corps that is briefly not `Movable` (battle, a vanilla defensive operation, embarked, in transfer) loses its commitment for good.** [element: commitment]

What S says:
- S:337: "a non-empty mass replaces `PlannerState.LastMass`". The *Committed* predicate requires "the member is movable".
- S:337 also says a mass member that vanilla's defence takes "is then `InDefensiveOps`, busy and not committed until vanilla ends the operation", which promises that the commitment resumes.

Trace (review N, push on O, member A in battle):
1. `Busy` includes `InBattle` and `InDefensiveOps` (TheaterPlanner.cs:123-125), so `Movable` is false and A is not committed.
2. Row 2 claims A `Immobile`, and `MassFor` skips it (`!MassEligible`, 988).
3. The other members make a non-empty mass, which replaces `LastMass` without A.
4. On review N+1, A is out of battle at readiness step 1 (a march and a battle both cost readiness). It is no longer a last-mass member, so it is neither committed nor `MassEligible`. It falls to the holds, and the push loses it.

This is RC4 (the readiness dip) after a battle instead of after a march.
- Severity: major
- Impact:
  - Every corps that fights at or near the objective, or that vanilla's defence borrows, drops out of the push.
  - `ABattleAtTheObjectiveKeepsTheCommitment` (P:350, T:48) has no supporting text in S: the old sentence "a battle (`Immobile`) ... keeps it" was removed. Its expected result is undecidable.
  - Whether the dropped member enters `CommitmentEnded`, and so whether the mover de-lists a corps in battle, is unstated.
- Assumption: the last-mass update keeps a member that is still in `s.Own` but not `Movable` today (in battle, in a defensive operation, embarked, in transfer). Only a member that is movable and was not taken by the mass for another reason (`Capital`, a give-up, retreat, morale, supply, builder) is dropped. A dropped member enters `CommitmentEnded`. Tests: `ABattleKeepsTheLastMassMembership`, `AVanillaDefenceLoanResumesTheCommitment`.
- Grounding: shipped code (TheaterPlanner.cs:123-125, 733-745, 976-1011)

**GAP-04. `DepartmentHave` excludes `Committed`, so a stalled push's department reads short and draws a donor while its own corps wait idle in it.** [element: department-reinforcement]

What S says:
- S:333: `DepartmentHave(d)` counts formations "not claimed `Mass` or `Committed`". The pick runs only with no main effort.
- With no main effort there is no `Mass`, but there are `Committed` claims on every no-candidate review (GAP-01).

Trace:
- The committed corps stand in the push's target department, where they marched. Their department's have drops by their strength while `DepartmentNeed` (S:283) is unchanged.
- The department ranks short with `donor`, and row 10 sends a formation from another department within 10 days.
- criterion-department-reinforced (S:410) then scores that pick as a success.

Pass-10b GAP-05's donor stream (fixed for a running push by "during a push the pick does not run") comes back for the stalled push.
- Severity: major
- Impact: every stall draws a transfer into a department that holds its own idle corps. `HaveExcludesMassAndCommitted` (P:363) pins the cause.
- Assumption: on a review with no main effort, `Committed` formations count in `DepartmentHave` where they stand (they are there and can hold). The exclusion applies to `Mass` only, and P:363's test becomes `HaveExcludesTheMass` with a committed-wait case. Test: `AStalledPushDrawsNoDonor`.
- Grounding: none (S's own rules S:283, S:333, S:337)

**GAP-05. "One reinforcement per decision" is undefined in the quiet period: `DecisionSerial` does not tick on a no-candidate review, so a push's own D6 reinforcement locks the whole following quiet period.** [element: department-reinforcement]

What S says:
- S:333 defines the lock as free when `ReinforcementDecision != DecisionSerial`, and says "a new decision frees the lock" and "With the lock taken, every other short department reads `locked` until the next decision".

What shipped `Decide` does:
- It ticks `DecisionSerial` only when the chosen objective changes (TheaterPlanner.cs:459-462). A no-candidate review leaves it as it is (408-416).

What follows:
- (a) A push that took a D6 reinforcement (`NoteReinforcement`, 1100-1112) leaves `ReinforcementDecision == DecisionSerial`. The pick is then `locked` for the whole quiet period after it, and row 5 claims that push's reinforcement as `DepartmentMove` and redeploys it whenever it does not yet stand in a facing sector of its department.
- (b) A quiet period with a free lock allows exactly one transfer, however many reviews it lasts.

The decision record this refinement replaced gave the same reason: "`DecisionSerial` does not tick with no main effort, so 'per decision' is undefined" (removed at `cc886c90`). S now states the rule without stating these consequences.
- Severity: major
- Impact:
  - A builder may read a quiet period as a new decision, or tick the serial, and the tests `OnePerDecisionUnderD6sLock`, `ANewDecisionFreesTheLock` and `OtherShortDepartmentsReadLocked` (P:363) then fix different behaviour.
  - Taken literally, the West, the department D6 most often gives from, is never refilled after a push that drew on it.
  - Row 5's case (a) has no test.
- Assumption: as shipped (no tick on a no-candidate review). S states (a) and (b), and row 5 says it covers a push's D6 reinforcement left marching when the push ends. Tests: `APushsReinforcementLocksTheFollowingQuietPeriod`, `AQuietPeriodGetsOneTransfer`, `RowFiveRedeploysAnEndedPushsReinforcement`. Whether that is the behaviour the user wants is routed to the integrator's refinement note (S:333).
- Grounding: shipped code (TheaterPlanner.cs:408-416, 459-462, 948-951, 1100-1112; PlannerState.cs:42)

**GAP-06. The donor pool is filtered by home department while `DepartmentHave` counts by standing, so a pick can be spent on a formation that already stands in the receiver.** [element: department-reinforcement]

What S says:
- S:333: the pool is "the foreign list `MassFor` would build for a target in that sector (... of another department)". `MassFor`'s foreign test is the home department (`DepartmentOf(c, i) != targetTheater`, TheaterPlanner.cs:994).
- The donor test is "the donor department's `DepartmentHoldMet` without it", now on `DepartmentHave`, which counts by `HoldSectorOf` (standing).

Trace (an East-home corps standing in the short West; requirement-decision-end's own case, an ended push's corps left where it stands, or one moved by vanilla's job 4 or 6):
1. It is foreign to a West target and about 0 days away.
2. The East's have does not count it, so excluding it leaves East's test unchanged and it passes.
3. It is picked, and `NoteReinforcement` transfers it to the West.
4. Row 5 never claims it, because it already stands in a facing sector of its department.
5. Nothing moves, the West's have is unchanged, `deptShort=` reads the same next review, and the lock is spent for the decision (GAP-05).
- Severity: major
- Impact: `HaveCountsWhereFormationsStand` and `ThePickIsD6sReinforcementPick` (P:363) cannot both be written red-first. The interaction has no named result.
- Assumption: the department pool excludes any formation whose `HoldSectorOf` sector already lies in the receiving theater. "Of another department" means standing outside the receiver, and the donor test is applied to the department it stands in. Test: `AFormationAlreadyInTheReceiverIsNoDonor`.
- Grounding: shipped code (TheaterPlanner.cs:909-921, 941-945, 955-968, 994; ReserveRules.cs:116-122)

**GAP-07. A department pick interrupted by a push strands its formation: it is homed in the receiver but stands in the donor, and no rule ever sends it on.** [element: department-reinforcement]

What S says:
- Row 5 claims the locked formation only "while there is no main effort" (S:318).
- S:333: "a new decision frees the lock, and a push then draws on it as a formation of its new department".

Trace (picked on review N for the West, marching through the East, a push chosen on review N+2):
1. On review N+2, `Evaluate` reads `CurrentMainObjective -1`, so row 5 claims it. `Decide` ticks the serial and the lock is free.
2. From review N+3 there is a main effort, so row 5 does not claim it. It is `None`, standing in an East sector, and becomes a hold body or `Hold garrison` there. Its march stops.
3. Its home is now the West (the transfer at the pick). For a West target it is native, so it is never in the West's foreign list again (TheaterPlanner.cs:994). Row 10 can never pick it for the West.
4. The East's have counts it (standing), and `MassFor` for an East target treats it as foreign (D6's one reinforcement).

The same happens after a give-up (`Hold gave-up`, S:333), once the lock frees.
- Severity: major
- Impact:
  - The West stays short with a West formation idle in the East, for good.
  - `ArrivalInAFacingSectorHoldsByTheOrdinaryRules` and `ANewDecisionFreesTheLock` (P:363) pass.
  - The case has no test.
- Assumption: row 5 claims the formation of `ReinforcementKey` whenever its home department has no main effort aimed into it (or simply whenever it is not in the mass) and it does not stand in a facing sector of its home department, whether or not the lock is current. Test: `APushDoesNotStrandAnInterruptedTransfer`.
- Grounding: shipped code (TheaterPlanner.cs:459-462, 976-1011, 1100-1112)

**GAP-08. Row 8 orders "the other committed" as a priority, but S:337 takes only the chosen objective's committed first, and P/T test a third reading.** [element: commitment]

There are three readings:
- S:321 (row 8 *Takes from*): "`Committed` (to any objective: committed to this objective first, then the other committed)", an ordered priority over both sets.
- S:337 *In the table*: "row 8 takes the chosen objective's committed formations first, whatever the need ..., then the rest by army as D3", so the other objective's committed are ordinary D3 members.
- P:350 / T:48 `EveryCommittedFormationIsTakenWhateverTheNeed`: every committed formation regardless of objective.

On a switch from O to P, a ready O-committed corps in P's capacity is, by reading:
- taken ahead of P's nearer natives;
- taken by army distance until the need is met;
- taken whatever the need.
- Severity: major
- Impact: three different switch-review masses. `TheSwitchReviewTakesTheOldMassIntoTheNew` cannot be written red-first. This is a contradiction between S and the plan's tests (the plan's stop-on-contradiction rule, P:41).
- Assumption: S:337's reading. Only formations committed to the chosen objective are taken whatever the need. Other committed formations in the capacity are ordinary D3 members (`Massable` admits them only as `MassEligible`). S:321's cell is rewritten to match, and the test becomes `EveryFormationCommittedToTheChosenObjectiveIsTakenWhateverTheNeed`.
- Grounding: shipped code (TheaterPlanner.cs:1055-1096)

**GAP-09. `[DepartmentReinforcement]` still lists the removed states, and with no record S does not say how Arrived and GaveUp are detected.** [element: department-reinforcement]
- S:359 lists `state=Picked|Arrived|GaveUp|Ended|Pruned`. S:333 and P:366 list `Picked|Arrived|GaveUp`. `Ended` and `Pruned` were the in-flight record's states.
- "Write, on change" needs something to compare against. Arrived is the review on which row 5 stops claiming the `ReinforcementKey` formation because it stands in a facing sector of its department. GaveUp is the tracker's give-up on its `Redeploy department-reinforcement` move. Neither is defined.
- Severity: minor
- Impact: criterion-department-reinforced's read path and P:366's smoke rows have no emitter rule.
- Assumption: the runtime compares today's claim of the `ReinforcementKey` formation with yesterday's captured plan, the capture F1 already makes (S:358). `Picked` on the pick, `Arrived` when a `DepartmentMove` claim becomes anything else while the formation stands in a facing sector of its department, `GaveUp` when G4 assigns it `Hold gave-up`. S:359 drops `Ended|Pruned`.
- Grounding: none

**GAP-10. The plan still names in-flight published members and an unanchored target, and "`NoteReinforcement` reused" cannot work unchanged with no main effort.** [element: department-reinforcement]
- P:31 lists `TheaterPlan.DepartmentMoves` as a published member. P:370 adds `DepartmentMove` to `PublishedTypes`, but it is an enum value of `Claimant`, not a type. P:360 adds `TheaterPlan.ReinforcementTargetX/Z`, which S does not define (S:310 puts the target in the claim's `TargetX/Z`).
- Shipped `NoteReinforcement` reads `plan.ReinforcementInstanceId` and sets `ReinforcementTo = TheaterOf(plan.MainSectorId)` (TheaterPlanner.cs:1100-1112). With no main effort, `MainSectorId` is -1, `TheaterOf(-1)` is `Unknown` (SectorModel.cs:220-223) and `Transfer` ignores `Unknown` (PlannerState.cs:165-168). The lock is set and no transfer happens.
- S does not say whether the no-push pick writes `plan.ReinforcementInstanceId/Name/From/To`, which feed the `[CampaignPlan]` `xdept=` field (TheaterPlanner.cs:1459).
- Severity: minor (`TheDepartmentTransfersAtThePickAsD6` catches the transfer red)
- Impact: the builder invents the signature, the published members and whether `xdept=` shows a department pick.
- Assumption: `NoteReinforcement(c, state, instanceId, Theater to)`, with the D6 caller passing the main sector's theater. The department pick writes no `plan.Reinforcement*` field and publishes through the claim and `[DepartmentReinforcement]`. P:31, P:360 and P:370 drop `DepartmentMoves`, `ReinforcementTargetX/Z` and the `DepartmentMove` type.
- Grounding: shipped code

**GAP-11. Test names still say "in flight".** [element: ownership-table]
T:37 and P:293 name `TheHoldNeverCountsOrReservesCommittedOrInFlight` and `CapitalMayTakeCommittedOrInFlight`, and P:299's PROVE plant names the first. No in-flight record exists. The rows are `Committed` and `DepartmentMove`.
- Severity: minor
- Impact: the fixture may be written around a record that does not exist.
- Assumption: the tests are renamed `...CommittedOrDepartmentMove` and their fixtures hand-set row 4 and row 5 claims.
- Grounding: none

**GAP-12. The `CommitmentEnded` de-list assumes the mover listed every last-mass member, but an undispatched member may have been listed by vanilla's job 4.** [element: decision-end]
- S:338: "the mover listed it ... the de-list is keyed on that set, never on the `InOffensiveOps` flag alone".
- Shipped `MassEligible` does not exclude `InOffensiveOps` (only `InDefensiveOps` is `Busy`, TheaterPlanner.cs:118-125). A member already within `ArrivalRadius` (CampaignMover.cs:335) or deferred (340) is never dispatched, and so never listed by the mover.
- The mover keeps no record of what it listed (CampaignMover.cs:350).
- Severity: minor
- Impact: such a member, listed by vanilla's own offensive, is de-listed when the commitment ends, which cancels a vanilla offensive (the case S:338 means to protect).
- Assumption: accepted and named. A last-mass member is the mover's to release whoever listed it, because Whiskey's march superseded vanilla's for it.
- Grounding: shipped code

**GAP-13. TC-END-SMOKE's "every `[Reserves]` row reads `endedListed` at 0" also fails on legitimate cases.** [element: decision-end]
- S:358: `endedListed=` counts the previous review's `CommitmentEnded` still `InOffensiveOps`.
- S:338 does not de-list a formation that is `MainEffort` or `Support` again. Once de-listed, an ended formation is a plain `Hold` that is not withheld, which vanilla's job 4 may list again before the next review (only the withheld are kept from it, CampaignWithholdView.cs:111-120).
- Severity: minor
- Impact: the smoke case (T:51) fails on a correct build.
- Assumption: `endedListed=` counts only formations not `MainEffort` or `Support` today and not listed by vanilla after the de-list. That second condition cannot be read from the snapshot, so T:51 bounds the count to "0 on the review after each end" rather than "every row".
- Grounding: shipped code

**GAP-14. criterion-mass-kept reads a trace candidate that may be absent, and a committed count that row 6 may have taken.** [element: other]
- `Trace` writes at most `MaxTraceCandidates` (12) candidates by score (TheaterPlanner.cs:92, 1472). A stalled objective may be outside them.
- `committed=` is defined as "row 4's claims" (S:358), but row 6 may overwrite a `Committed` claim with `Capital` (S:319). That formation is then out of `MassFor`'s capacity, so `mass=` falls below `committed=` on a correct build.
- Severity: minor
- Impact: TC-COM-SMOKE (T:50) reads "missing" or "failed" on a correct build.
- Assumption: the trace always includes the last mass's objective candidate, appended beyond the 12 when needed. `committed=` counts the claims still `Committed` after rows 1-7.
- Grounding: shipped code

**GAP-15. With `Enable Main Effort Commitment` off, S does not say whether the last-mass replacement runs.** [element: commitment]
S:310: "with a flag off nothing new is created (no last-mass exemption ...), its state (the last mass ...) is kept and ignored, and the bookkeeping that ends or moves existing state still runs". The replacement both creates new state and ends old state.
- Severity: minor
- Impact: if it does not run, turning the flag back on re-commits formations to a stale objective. `AFlagOffKeepsTheLastMassAndIgnoresIt` (P:350) pins one reading.
- Assumption: the replacement and the clear run with the flag off (bookkeeping), and only the exemption (row 4, `Massable`) is off.
- Grounding: none

**GAP-16. When the first ranked department has no donor, S does not say whether the pick tries the next one or what the others read.** [element: department-reinforcement]
S:333 runs the pick "for the first ranked department" and gives the statuses `donor` (picked), `locked` and `nodonor`.
- Severity: minor
- Impact: with a far, donorless shortest department (none within 10 days), the second-shortest is never served, and its status is undefined.
- Assumption: no fall-through. The first ranked reads `nodonor`, the others read `nodonor` too while the lock is free. Test: `OnlyTheShortestIsTried`.
- Grounding: none

**GAP-17. "Not given up there" has no key: `MassFor`'s give-up test is by objective, and the department target is recomputed each review.** [element: department-reinforcement]
- S:333's pool excludes formations "given up there". `MassFor` tests `GaveUpOn(instance, objectiveId)` (TheaterPlanner.cs:1124-1131). A department target has no objective.
- G4's `GaveUpAt` compares target points (ForceAllocator.cs:394-401) against a target that S:333 recomputes each review.
- Severity: minor
- Impact: a formation that gave up on the West may be picked again as soon as the neediest sector changes.
- Assumption: "given up there" means a standing `GaveUpMove` whose target lies in the receiving department's sectors (`MoveTracker.SameTarget` against any facing sector centroid of the department).
- Grounding: shipped code

**GAP-18. A stale reference to sorties in the capital race.** [element: other]
S:295: "a marching one included since phase 1 has no sortie". Phase 2 has no sortie either.
- Severity: minor
- Impact: a reader may expect phase 2 to exempt a committed corps from the race.
- Assumption: it reads "a marching one included".
- Grounding: none

## Pass-10b gaps checked (adequate | inadequate -> GAP-NN | moot)

- GAP-01 (re-pick after a no-candidate review): adequate. Row 4 keys on `LastMass`, which an empty mass keeps (S:337), so a re-pick finds the corps `Committed` and in `Massable`. The same rule leaves a stall unbounded -> GAP-01 (new).
- GAP-02 (switch end before `SelectMass`): moot. The end is the last-mass update after `SelectMass` (S:310, P:370).
- GAP-03 (an ended push stays on vanilla's list): adequate (S:338 `CommitmentEnded` de-list; S:358 `endedListed=`). The stall is GAP-01, the key is GAP-12, the smoke is GAP-13.
- GAP-04 (`MassFor` counts an in-flight `DepartmentMove`): adequate. Row 8 is `MassFor`/`SelectMass` and "never" takes `DepartmentMove` (S:321), and "a step takes only formations whose current claimant its row lists" (S:310).
- GAP-05 (a push makes its own department short): moot ("During a push the pick does not run", S:333). It returns for the stall -> GAP-04 (new).
- GAP-06 (criterion-mass-kept unmeasurable): adequate. It now reads the trace's `mass=` against `committed=` (S:407, S:358). The trace cap and row 6 are GAP-14.
- GAP-07 (row 5 have cell): adequate. Row 5 counts "never" (S:318) and the have is by standing (S:333). `Recover` is admitted by the definition's own words, so it is adequate by implication.
- GAP-08 (`SplitReserve` channel): adequate (`Claim.Reason = "committed"`, S:310).
- GAP-09 (switch leftovers as committed-wait): adequate. S:337 names the case and accepts it. The priority over "the other committed" is GAP-08 (new).
- GAP-10 (battle vs switch): moot as written (no sortie). The battle case resurfaces as a membership loss -> GAP-03 (new).
- GAP-11 (old sortie-end sites S:364-365): adequate (S:364-365 name `LastMass` written once after `SelectMass`).
- GAP-12 (two rows 10): adequate (row 9 is D8, S:322).
- GAP-13 (row 10's seam): adequate (`rowSources` in `ClaimTable.Build`'s signature; "row 10 is `TheaterPlanner.DepartmentReinforcement` itself", S:310, P:288).
- GAP-14 (candidate means `Evaluate`'s filter): adequate (S:337 cites TheaterPlanner.cs:334).
- GAP-15 (two arrival radii; stale department array): moot. There is no record and no radius, and the transfer is at the pick as D6's is.
- GAP-16 (an arrived mass member gets no sortie): moot. Commitment is by mass membership, not by dispatch.
- GAP-17 (the covering count and `Capital`): adequate ("row 7's counted-anyway predicate over claims without its `Capital` term", S:303).
- GAP-18 (`PlanGroups` covering test): adequate (`PlanGroupsGroupsACoveringMemberWhereItStands`, P:334, T:42).
- GAP-19 (vacuous decision-end smoke): adequate in kind (`endedListed=`, T:51). Its strictness is GAP-13.
- GAP-20 (P:9 goal): adequate ("places formations whose push ended where the threat is").

## Tally by element

| Element | Blocker | Major | Minor |
|---|---|---|---|
| ownership-table | 0 | 1 (GAP-02) | 1 (GAP-11) |
| covering-force | 0 | 0 | 0 |
| commitment | 0 | 3 (GAP-01, 03, 08) | 1 (GAP-15) |
| department-reinforcement | 0 | 4 (GAP-04, 05, 06, 07) | 4 (GAP-09, 10, 16, 17) |
| decision-end | 0 | 0 | 2 (GAP-12, 13) |
| redeploy-role | 0 | 0 | 0 |
| other | 0 | 0 | 2 (GAP-14, 18) |
| **Total** | 0 | 8 | 10 |

## Checked and clean

- **Removed lifecycle text.**
  - No requirement, table cell or test reads sorties, `DepartmentInFlight`, in-flight records, origins or a return lifecycle.
  - The remaining mentions are the integrator notes and decision records that name what was removed (S:251, S:333, S:337, S:339, S:380-382), P:275's history of pass 5b, and the negative pins `NoReturnRoleOrReasonExists` and T:51's `return-home` check.
  - The stale exceptions are GAP-09 (S:359), GAP-10 (P:31, P:360, P:370), GAP-11 (T:37, P:293, P:299) and GAP-18 (S:295).
- **Row numbers 1-10.**
  - The table (S:314-323) is 1-10, with row 9 D8 and row 10 the department pick.
  - Every textual reference agrees: S:303 "row 7", S:306 "a step after row 8", S:310 "rows 1-7", "rows 8-10", "row 9", "row 10", "rows 4 and 5", S:333 "row 5"; P:42 "rows 4, 5 and 10", P:277 "rows 1-7", "rows 8-10", P:288, P:294 "row 2", P:296 "row 8", P:345 "row 4", P:360 "row 5".
  - No row 11 or 12 is cited.
- **After-`Decide` order.** S:310 and P:370 agree: `SelectMass` -> last-mass update -> covering left-behind rewrite -> `PostFromOutside` -> `DepartmentReinforcement` -> covering state writes -> `AssignPostures`. The last-mass update reads `MassInstanceIds` after they exist.
- **Re-pick after a no-candidate review.** `Decide` with `current < 0` draws (`initial`) and ticks `DecisionSerial` (TheaterPlanner.cs:420-423, 459-462), so the old D6 lock frees. The old reinforcement, whose home was transferred to the target theater, is a native of the re-pick and committed through `LastMass`, so it is taken first.
- **Lock at campaign start.** `ReinforcementDecision` starts at -1 and `DecisionSerial` at 0 (PlannerState.cs:18, 42), so the first quiet period's lock is free.
- **`Redeploy department-reinforcement` withholding.** `WithheldFromOffense` adds `Role == Redeploy` after `a.Withheld` (CampaignWithholdView.cs:111-120), and `Withheld` stays false, so vanilla's defence may use it on the way, as S:329 says.
- **The mover's (role, reason) list.** `Hold` is not in `Priority` (CampaignMover.cs:115-116), so committed-wait is never dispatched by the mover. Vanilla's job 6 still may (GAP-01).
- **Capital over committed.** Shipped `Capital` takes `MassEligible` formations nearest the capital (ReserveRules.cs:164-178). Taking `Committed` and `DepartmentMove` is home defence first. A ready committed corps may be taken, and it then leaves `LastMass` (intended). An unready one is not `MassEligible`, so the capital never takes a committed corps it cannot move.
- **Covering force.** Points, posts, axis shares, `areaIndex` incumbents, the verdict hysteresis, the defence clause slot (CampaignWithholdView.cs:131-132) and the enemy-capital rewrite are untouched by the simplification. A decision-ended corps near the axis is an ordinary `None` candidate of the covering pick.
