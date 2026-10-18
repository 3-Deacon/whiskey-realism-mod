# D11 build dry run, pass 10b: phase 2

Worktree `.worktrees/plan-foresight` at `c04e0122` (read only). S = spec D11 in `docs/superpowers/specs/2026-10-14-campaign-planner-design.md` (S:n = line), P = `docs/superpowers/plans/2026-10-18-strategic-foresight.md`, T = `docs/loop/foresight/test-plan.md`. Shipped code under `src/WhiskeyRealism/Strategic/Planner/` (file names shortened), decompile `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`. Phase 1 (F1-F5, F9) assumed built as P says.

**First-priority verdict.** The simplification removed every pass-9b gap that came from the return lifecycle (GAP-02, 04, 06, 08, 15 are moot) and the one-have rule settles GAP-05. What it leaves is the hand-over from a decision to "the holds":

- **The re-pick after a no-candidate review.** Row 4 is keyed on `CurrentMainObjective`, and shipped `Decide` sets that to -1 on that review. On the next review the holds therefore count and reserve the old push's corps while `Massable` still puts them in the re-picked objective's capacity (GAP-01).
- **The switch end.** It reads the new mass before `SelectMass` has run (GAP-02).
- **An ended push stays on vanilla's offensive list.** It is never taken off, so vanilla's job 6, not the holds, places it (GAP-03).

Department reinforcement adds two capacity and need leaks around in-flight formations and the push (GAP-04, GAP-05). criterion-mass-kept cannot be measured as written (GAP-06). Not build-ready for phase 2: 8 majors.

## Gaps (20: 0 blocker, 8 major, 12 minor)

**GAP-01. On the review after a no-candidate review, row 4 claims nothing. The holds count and reserve the old push's corps, while `Massable` still puts them in the re-picked objective's capacity.** [element: commitment]

What S says:
- S:337 *In the table*: "row 4 claims `Committed` every formation committed to the current main effort's objective (`CurrentMainObjective` as `Evaluate` reads it, before `Decide`)".
- Shipped `Decide` writes `state.CurrentMainObjective = -1` on a no-candidate review (TheaterPlanner.cs:408-416). So on review N+1, row 4 has no objective to claim for, and S:337 confirms it: "from the next review it is committed only while its objective is the main effort again".
- But S:337 *In the capacity* keeps `Massable = MassEligible or committed to the candidate's objective`, "so a no-candidate review followed by a re-pick of `O` keeps `O`'s corps".

Trace (review N+1, sortie of O kept, O still offered):
1. The old push's corps are claimed `None`.
2. A ready corps is a hold candidate of the sector it stands in. When reserved there, `MassFor` drops it from O's capacity (`holds >= 0 && holds != targetSector && !HoldThreatAtTarget`, TheaterPlanner.cs:989-990), so it is not kept.
3. An unready corps (step < 2) is counted anyway in its standing sector's hold (`!MassEligible`, ReserveRules.cs:210-217), so the hold reserves one fewer body there. `Massable` also puts it in O's capacity. If O is re-picked, row 8 takes it, and the sector's counted hold falls below its need. The pass-5b blocker class (the hold counting committed formations) is back on this review.
4. Row 8's "committed to this objective first, whatever the need" (S:321, S:337) does not say whether it means a `Committed` claim (there is none on this review) or a formation with a sortie of O. So the need's stop may cut the old push.

`CommitmentSurvivesANoCandidateReview` (P:350, "back in the mass when the objective is re-picked") cannot be written red-first from S, and its outcome depends on the fixture.
- Severity: major
- Impact: the case commitment exists for (RC4's initial, no-candidate, initial chain) keeps the unready corps in the capacity but splits the push across the holds. A sector reads met on a corps that the next mass then takes.
- Assumption: row 4 keys on the objective of the last decision, not on `CurrentMainObjective`. That is a persisted `PlannerState.LastMainObjective`, kept through a no-candidate review and cleared when another objective is chosen or the sortie ends, so a formation committed to it stays `Committed`: neither counted nor reserved by the holds. Row 8's "committed first" set is the `Committed` claims. S:337's "from the next review ..." sentence is rewritten to match. Test: `ARePickAfterNoCandidateKeepsTheOldPushOutOfTheHolds`.
- Grounding: shipped code (TheaterPlanner.cs:408-416, 989-990; ReserveRules.cs:206-228)

**GAP-02. The sortie end "the chosen main effort has another objective and the new mass did not take the formation" is applied before the new mass exists.** [element: commitment]

What S and P say:
- S:337 *Sortie ends*, "applied right after `Decide`", lists that end.
- S:310 *After-`Decide` order*: the sortie ends are applied "right after `Decide`, before `SelectMass`".
- P:370's `PipelineOrderPinned` pins `Decide -> the record and sortie ends -> SelectMass (every MassFor call after Decide runs after the ends)`.

So the end cannot read `TheaterPlan.MassInstanceIds`. A builder either deletes every sortie of another objective on the switch review, which drops the "did not take" clause, or moves this one end after `SelectMass`, against the pin. `ASwitchEndsTheSortiesTheNewMassLeft` (P:350) and the source pin cannot both pass.

What survives also has no purpose that S states. A formation the new mass took keeps its sortie of O, the old objective, because only the mover's recorded dispatch overwrites it (S:337). The first dispatch of a new decision is deferred until the tempo roll (CampaignMover.cs:338-345).
- Severity: major
- Impact: the build order and the test disagree. Either way, an old-push corps taken by the new mass may keep an O-sortie that re-commits it to O on a later re-pick of O.
- Assumption: this end is evaluated after `SelectMass` (and before `PostFromOutside`), and P:370's pin names it there. The other ends stay right after `Decide`. A formation the new mass took keeps its sortie until the mover's recorded dispatch overwrites it, as S:337 says. Test: `ASwitchEndIsJudgedAfterTheNewMass`.
- Grounding: none (S's and P's own text); shipped code for the deferral (CampaignMover.cs:336-345)

**GAP-03. A formation whose decision ended stays on vanilla's `unitsinoffensiveoperations`, so vanilla's job 6, not the holds, places it.** [element: decision-end]

How it stays listed:
- S:339: "A formation whose decision ended is unclaimed and is placed by the holds, the capital reserve and department reinforcement."
- The mover lists every `MainEffort` and `Support` it dispatches (CampaignMover.cs:350). It de-lists only `Recover` (CampaignMover.cs:201-211).
- S:338 adds only `Redeploy`, `covering-force` members and `CapitalGuard`.

What vanilla then does with it:
- After the end, the formation is `None`, then G4 makes it `Hold hold-reserve` (ForceAllocator.cs:472-477, not withheld) or `Hold garrison` (AssignRemainder, ForceAllocator.cs:973-983). Either way it is still listed.
- Vanilla's `UpdateMicroMovementInOffensive` (13968-14061) takes every listed unit with no path that is not in battle (14004) and has readiness step 2 or more (14030). It sends it to the closest objective of its area (14013, `MoveUnitTo` 14036) and reads no Whiskey availability gate (no patch on it under `src/`).
- S:338 itself cites this vanilla behaviour as the reason for its de-lists.

This was pass-9b GAP-03's second half. The fold bounded committed-wait to one review but added no de-list, and the case now covers every ended decision: won, switched, given up and no-candidate.
- Severity: major
- Impact: the day after a push ends, every ready corps of it still idle near the front marches alone onto the nearest enemy objective of its area, as an uncoordinated vanilla attack. "Troops where the threat is" (S:339) holds only until vanilla's next job-6 tick. `AnEndedDecisionsFormationIsUnclaimed` passes, because nothing in the harness models job 6.
- Assumption: the de-list is keyed on what Whiskey itself listed, not on the `InOffensiveOps` flag (vanilla's job 4 lists units too, and de-listing those would cancel vanilla's own offensives). The plan publishes the instance ids whose sortie ended this review (the after-`Decide` and after-`SelectMass` ends, GAP-02), and the mover's release pass takes each one still flagged `InOffensiveOps` off `unitsinoffensiveoperations` unless it is again `MainEffort` or `Support`. A `MainEffort` member that was never dispatched has no sortie and was never listed by the mover. Test: `AnEndedPushsCorpsIsTakenOffVanillasOffensiveList`.
- Grounding: vanilla (13968-14061: 14004, 14013, 14030, 14036), shipped code (CampaignMover.cs:201-211, 350; ForceAllocator.cs:472-477, 973-983)

**GAP-04. `MassFor`'s capacity still counts an in-flight `DepartmentMove` formation that row 8 may never take.** [element: department-reinforcement]

What S says:
- Row 8 never takes `DepartmentMove` (S:321).
- S lists `MassFor`'s exclusions as the shipped capital and hold reserves (TheaterPlanner.cs:988-990) and the covering members (S:305). It names no `DepartmentMove` exclusion.

Why the formation is in the capacity:
- An in-flight formation is `MassEligible`.
- Until arrival it belongs to the donor's home department ("until then it is the donor's", S:333), so `MassFor`'s department filter (TheaterPlanner.cs:994) counts it as a native of every donor-department candidate.
- Its claim is `DepartmentMove` (row 5), so no `HoldReserved` or `CapitalReserved` entry drops it.

The odds are therefore judged on a capacity that includes a corps walking to another department, and `SelectMass` cannot take it.
- Severity: major
- Impact: a donor department's candidate is eligible, and its `Need` is met, on a body that is leaving, so the published mass is smaller than the odds assumed. `cap=` overstates it. The same applies to a sortie of O on a formation later picked as a donor (`Massable` keeps it in O's capacity).
- Assumption: `MassFor` skips a formation claimed `DepartmentMove` (with the claim table's `Capital` and `Hold` exclusions taken from the claims, as shipped). Test: `MassForSkipsAnInFlightDepartmentMove`.
- Grounding: shipped code (TheaterPlanner.cs:976-1011)

**GAP-05. *During a push* leaves out only the target sector's elements, so the push still makes its own department short wherever the mass took a released or target-sector hold.** [element: department-reinforcement]

What S says:
- S:333: "the ranking and the donor test leave out of `DepartmentNeed` the elements assigned to the chosen objective's target sector ..., so the mass's absence from the have never makes its own department short".
- The have excludes `Mass` (S:333).
- Row 8 takes the `Hold` bodies of the target's own sector and of every sector `HoldThreatAtTarget` released (S:321). A sector is released when every element it faces lies within `R` of the target (S:284), but those elements stay assigned to their own facing sector, not to the target sector (S:283's nearest-facing-sector rule).

Two leaks:
- A released sector's elements stay in `DepartmentNeed` while its hold bodies are now `Mass` and out of the have.
- The `Floor` term of `DepartmentNeed` (`max(sum of Floor over d's facing sectors, ...)`, S:283) still holds the target sector's floor, whose bodies the mass took.

Either one makes the department short the review the mass leaves. F8 then picks a donor from another department within 10 days for a threat the push itself answers.
- Severity: major
- Impact: every push that releases a hold, or takes its target sector's holders, draws a transfer from another department. That is the donor stream criterion-department-reinforced would score as a success (S:410). `APushDoesNotMakeItsDepartmentShort` (P:363) passes on a fixture with no release.
- Assumption: during a push, `DepartmentNeed` also leaves out:
  - the elements assigned to every sector whose hold row 8 drew from (the target's and the released ones);
  - those sectors' `Floor` terms.

  Tests: `AReleasedHoldDoesNotMakeThePushsDepartmentShort`, `TheTargetSectorsFloorIsLeftOutDuringAPush`.
- Grounding: none (S's own rules S:283, S:284, S:321, S:333)

**GAP-06. criterion-mass-kept reads a field the row does not carry and that is 0 by construction, and its exemption cannot be read from any row.** [element: other]

What S:407 says:
- "no `[CampaignPlan] switch=no-candidate` row with `cap=0`, or with `cap` below the committed strength, while `[Reserves] committed>0` (a drop for the odds, an accomplished or unavailable objective is legitimate and not counted)".

What the shipped rows do:
- `[CampaignPlan]` has no `cap=` field (TheaterPlanner.cs:1449-1459). `cap=` is `[Allocation]`'s (ForceAllocator.cs:214).
- `plan.MainCapacityStrength` is written only inside `if (main >= 0)` (TheaterPlanner.cs:225-251), so `cap=0` on every no-candidate review.
- An odds drop with no other eligible candidate also reads `switch=no-candidate` (`best < 0` is tested first, TheaterPlanner.cs:408-416). `odds` is `Trigger`'s reason only when another candidate is eligible (518). So the exemption has no field.
- `[Reserves] committed=` "counts the formations committed to the chosen objective (in the mass or `committed-wait`)" (S:358). A no-candidate review has no chosen objective, while committed-wait exists only on such a review (S:337).

The criterion therefore counts every no-candidate review, or none. TC-COM-SMOKE (T:50) cannot be computed.
- Severity: major
- Impact: RC4's one smoke measure is not computable, and a builder implementing `committed=` from S:358 writes 0 on exactly the review the criterion reads.
- Assumption: the criterion reads the last decision's objective candidate in `CampaignPlan:trace` (its `mass=` and `odds=`, TheaterPlanner.cs:1484-1489). It counts a no-candidate review whose old-objective candidate has `mass` below the committed men while that candidate's odds would pass on the committed men (its `est=` and `Required`). `committed=` counts the formations committed to the objective row 4 keyed on this review (GAP-01's last decision), chosen or not.
- Grounding: shipped code (TheaterPlanner.cs:225-251, 408-416, 1449-1489; ForceAllocator.cs:214)

**GAP-07. Table row 5 still says a `DepartmentMove` counts "in its receiving department's `have`". The one definition counts it where it stands.** [element: department-reinforcement]

What S says:
- S:318 (row 5, *Counts in a sector hold*): "never in a sector; in its receiving department's `have`".
- S:333 *Have, one definition*: the `HoldStrength` of the own formations "that stand in `d` ... and are not claimed `Mass` or `Committed`". That admits a `DepartmentMove` and a `Recover` where they stand.
- The integrator's refinement (decisions.md) says "formations that stand in the department and are not in the mass or committed".

For the days of the march, an in-flight formation stands in the donor's department, so the two texts give different numbers for the donor's and the receiver's have. They also differ for the neediest sector's `held` ("the same sum", S:333), for `deptShort=` and for the `[Reserves] dept=` row.
- Severity: major
- Impact: a builder writing from the table builds a receiving-department have, and `HaveCountsWhereFormationsStand` (P:363) or `OneHaveForRankDonorAndRow` fails on whichever reading the other text gives. That is the very contradiction the one-have fold was meant to end. S also never says whether `Recover` counts in the have, though row 7 says it never counts in a hold (S:320).
- Assumption: S:333's standing definition wins. Row 5's cell becomes "never in a sector hold; in `DepartmentHave` of the department it stands in". `Recover` counts in the have, as shipped `DepartmentHoldMet` counts every formation (TheaterPlanner.cs:961-966), and S says so.
- Grounding: none (S's own text); shipped code for `Recover` (TheaterPlanner.cs:955-968)

**GAP-08. `SplitReserve` cannot tell a committed mass member from another one.** [element: commitment]

What S and P say, and what G4 can see:
- S:337: "`SplitReserve` never turns a committed formation of the chosen objective into `Reserve`". P:345 owns it in `ForceAllocator.cs`.
- Row 8 replaces the `Committed` claim with `Mass` (S:310: "a step that takes a formation replaces its claim").
- `Claim.Reason` is not specified for a `Mass` claim (S:310, P:282).
- G4 receives no `PlannerState` (`ForceAllocator.Allocate`, ForceAllocator.cs:130-132).
- Shipped `SplitReserve` reads only `_inMass` and the distance to the target (ForceAllocator.cs:908-940).

`SplitReserveKeepsCommittedFormations` (P:350) needs a channel that S does not name. Pass-9b GAP-07's assumption (reason `committed`) was not folded.
- Severity: major
- Impact: a builder invents the channel: a claim reason, a published set, or passing the state to G4. On GAP-01's re-pick review no `Committed` claim exists at all, so the result differs by builder.
- Assumption: row 8 writes `Claim.Reason = "committed"` on a `Mass` claim taken from a formation committed to the chosen objective, and `SplitReserve` skips those members (the excess comes from the others, farthest first). The census names the reason's reader.
- Grounding: shipped code (ForceAllocator.cs:130-137, 908-940)

**GAP-09. A switch review leaves the old mass's untaken members as `Hold committed-wait`, which S assigns to a no-candidate review only.** [element: decision-end]

Row 4 fired in `Evaluate` (`CurrentMainObjective` = O), and the new mass for P takes only formations in P's capacity. `Massable` admits an O-committed formation to P's capacity only through `MassEligible` (S:337), so every unready corps of the O push stays `Committed`.

S:337 says "A `Committed` claim the mass did not take (a no-candidate review) is `Hold` in place ..., reason `committed-wait`, not withheld; it stays on vanilla's offensive list that review (it is a push member ...)". On a switch review, those formations have just ended their decision (S:339), yet `RoleOf` makes them committed-wait, listed and outside every hold, for one review.

`TheSwitchReviewTakesTheOldMassIntoTheNew` (P:350) reads as the whole old mass. It is only the ready part.
- Severity: minor
- Impact: one review of an ended push outside every hold and on vanilla's list. `AnEndedDecisionsFormationIsUnclaimed` needs a two-review fixture.
- Assumption: the switch-review leftovers are claimed `None` at the end step that runs after `SelectMass` (GAP-02), so the holds' counts do not see them this review but G4 does, and they are not committed-wait. The test name says "the ready members of the old mass".
- Grounding: none (S's own text)

**GAP-10. A battle keeps a sortie, a switch ends it, and S does not say which wins.** [element: commitment]
S:337: "A battle (`Immobile`) or a supply detour (`Recover`) keeps it". The same paragraph ends a sortie when "the chosen main effort has another objective and the new mass did not take the formation". A corps in battle on a switch review (rows 2 and 3 never reach row 8, S:321) meets both conditions.
- Severity: minor
- Impact: `ABattleAtTheObjectiveKeepsTheSortie` and `ASwitchEndsTheSortiesTheNewMassLeft` disagree on that fixture.
- Assumption: the ends take precedence: battle and supply keep a sortie only while its objective is still the main effort (or, with no main effort, the last one).
- Grounding: none

**GAP-11. Two sites still list the old sortie ends.** [element: commitment]
- S:365: `PlannerState.Sorties` is written "by the plan once per review after `Decide` (delete on arrival, give-up, prune, or a reserve taking the formation)". S:337's ends have no arrival end and no general "a reserve" end (only a `Capital` claim).
- S:364: the plan does "sortie reads", yet it deletes.

This is pass-9b GAP-07's third site, not rewritten.
- Severity: minor
- Impact: a builder reading S:365 deletes a sortie on arrival at the objective, or when the hold reserves the formation, and a siege corps loses its commitment.
- Assumption: S:365 and S:364 are rewritten to S:337's list.
- Grounding: none

**GAP-12. The claim table has two rows numbered 10 and no row 9.** [element: ownership-table]
S:322 (D8 `PostFromOutside`, "not a claimant") and S:323 (`DepartmentMove`, the new pick) are both numbered 10. S:310 "rows 8-10 after `Decide`", "row 10's donor pool skips them" and "rows 4, 5 and 10" (also P:42, P:288, P:297, P:360) read the second as 10, so D8 is 9.
- Severity: minor
- Impact: the registry's row ids (`IClaimSource.Row`, P:289) and `OwnershipRowsTakeOnlyFromTheirListedClaimants`' "one case per row" have an ambiguous row count.
- Assumption: S:322 is row 9.
- Grounding: none

**GAP-13. Row 10's claim source has no call site: `ClaimTable.Build` runs rows 1-7, and `Produce(s, state, claims)` cannot carry row 10's inputs.** [element: ownership-table]

What P and S say:
- P:288: "`ClaimTable.Build(..., IReadOnlyList<IClaimSource> rowSources)` calls them in row order". P:289's signature is `Produce(CampaignSnapshot s, PlannerState state, Claim[] claims)`.
- Row 10 runs after `Decide` in `TheaterPlanner.DepartmentReinforcement` (S:310, S:333), outside `Build`. It needs the department needs, the mass, the target sector and the garrison and recon incumbents.
- S:310's `ClaimTable.Build(s, sectors, holdNeed, nearX, nearZ, capitalFloor, held, gaveUp, state, covering)` has no `rowSources` parameter.

Pass-9b GAP-16 is folded for rows 4 and 5 only.
- Severity: minor
- Impact: `RowSourcesAreEmptyInF6a` and `TheFixtureRegistryDrivesEveryRowCase` cannot name row 10's seam. F8 invents it.
- Assumption: `ClaimTable.Build` takes `rowSources` for rows 4 and 5 (S:310's signature gains it). Row 10 is `TheaterPlanner.DepartmentReinforcement` itself, behind an `IClaimSource` whose `Produce` receives a `DepartmentPickInput` (needs, mass ids, target sector, incumbents).
- Grounding: none

**GAP-14. "Still a candidate" still says enemy-held. Shipped `Evaluate` also offers neutral-held records.** [element: commitment]
S:337 defines a candidate as "an enemy-held, available, not accomplished objective record". Shipped candidates are every record that is available, not accomplished and not held by the side (TheaterPlanner.cs:334), neutral-held ones included.
- Severity: minor
- Impact: a sortie to a neutral-held objective ends on the review after dispatch ("no longer a candidate") although `Evaluate` keeps scoring it. That is pass-9b GAP-14, not folded.
- Assumption: "candidate" means `Evaluate`'s filter exactly (TheaterPlanner.cs:334), cited rather than paraphrased.
- Grounding: shipped code

**GAP-15. Department arrival uses two radii, and the arrival transfer leaves the review's department array stale.** [element: department-reinforcement]
- Row 5 skips "a record whose formation already stands within the arrival radius of its target" (S:310, S:333), while the end is "arrival within `max(BugleRange, ArrivalRadius)`" (S:333). Between 10 (`CampaignMover.ArrivalRadius`) and `BugleRange`, row 5 claims `DepartmentMove` (`Redeploy`, so the mover may still march it) on the same review the end clears the record.
- The transfer runs after `Decide` (`PlannerState.Transfer`), but `c.Department` was filled before `Evaluate` (TheaterPlanner.cs:206, 909-921). So the second `MassFor` and `SelectMass` still treat the arrived formation as the donor's.
- Severity: minor
- Impact: one review of a redundant march and a stale native filter.
- Assumption: row 5 uses `max(BugleRange, ArrivalRadius)`. The arrival transfer also refreshes `c.Department[i]`.
- Grounding: shipped code (CampaignMover.cs:112; TheaterPlanner.cs:206, 909-921)

**GAP-16. A mass member already within `ArrivalRadius` of its target is never dispatched, so it never gets a sortie.** [element: commitment]
S:337 records a sortie only when the mover dispatches (`moved || outcome == Queued`). Shipped `Dispatch` returns before issuing when the waypoint is within `ArrivalRadius` of the target (CampaignMover.cs:335). The snapshot's waypoint is `GetLastWaypointPos` (CampaignSnapshotRuntime.cs:351), which returns the formation's own position when it has no path (130572-130602). For a staged push the start line is the forward-most member's own position (TheaterPlanner.cs:290-310), so that member, and any member already at the objective, is never committed. A readiness dip then drops it from the mass, and the start line moves.
- Severity: minor
- Impact: a push's leading corps is the one member commitment does not keep.
- Assumption: the mover also records (or keeps) a sortie for a `MainEffort` or `Support reinforcement` member it skips as arrived or underway, keyed by `KeyOf`. Test: `AnArrivedMassMemberHasASortie`.
- Grounding: shipped code (CampaignMover.cs:335; CampaignSnapshotRuntime.cs:351-364; TheaterPlanner.cs:290-310), vanilla (130572-130602)

**GAP-17. The covering non-massable count cites "row 7's counted-anyway predicate" but excludes the `Capital` that row 7 counts.** [element: covering-force]
S:303: "row 7's counted-anyway predicate over claims (`None`, `Immobile` or `PinnedBuilder` ...; never `Committed`, `DepartmentMove` or `Capital`)". Row 7 counts anyway "a `Capital` in the capital's sector" (S:320; shipped ReserveRules.cs:210-217).
- Severity: minor
- Impact: a builder reusing the predicate counts a capital guard standing within `R` of the post, if the post lies in the capital's sector. That is unlikely in 002/A (Fredericksburg is about 209 from Richmond).
- Assumption: the covering count is row 7's predicate minus `Capital`, as its list says, and S drops the word "row 7's" or names the difference.
- Grounding: shipped code

**GAP-18. `PlanGroups`' covering case has no test.** [element: redeploy-role]
S:329 now says `ArmyGroupDoctrine.PlanGroups` "groups a `Hold covering-force` member by the sector it stands in, not its recorded sector" (pass-9b GAP-17's fold). P:296 owns `ArmyGroupDoctrine.cs` for "skips `Redeploy`" only, and T:38 lists only `PlanGroupsSkipsRedeploy`.
- Severity: minor
- Impact: the second half of the fold has no red-first test.
- Assumption: `PlanGroupsGroupsACoveringMemberWhereItStands` is added to F6b (the first slice with a member).
- Grounding: shipped code (ArmyGroupDoctrine.cs:29, per pass 9b)

**GAP-19. The decision-end smoke case is vacuous, and no fit criterion measures requirement-decision-end.** [element: decision-end]
TC-END-SMOKE (T:51, P:386) checks that no `[CampaignMoverIssue]` carries a `return-home` reason, which no build can emit after `NoReturnRoleOrReasonExists`. criterion-return-home was removed, and none replaced it (S:385-418).
- Severity: minor
- Impact: nothing in the smoke shows that an ended push is placed by the holds, the capital reserve or department reinforcement (GAP-03's job-6 leak would pass).
- Assumption: TC-END-SMOKE counts formations of an ended decision (the `[Reserves]`-day after a `switch=` other than `hold`) that appear in a vanilla `micro movement in offensive` move. With GAP-03's de-list there is none. Otherwise it is named a measurement gap.
- Grounding: vanilla (14036; the debug string at 14048 is not in the tuning log, so this needs `InOffensiveOps` on the snapshot against yesterday's roles)

**GAP-20. The plan's goal still promises the removed lifecycle.** [element: other]
P:9: "keeps a dispatched main effort together, brings stranded corps home, takes depot builders ...".
- Severity: minor
- Impact: the plan's own goal contradicts requirement-decision-end and F7's `NoReturnRoleOrReasonExists`.
- Assumption: P:9 reads "places formations whose decision ended where the threat is" (S:249's wording).
- Grounding: none

## Pass-9b gaps checked (adequate | inadequate -> GAP-NN | moot)

- GAP-01 (three definitions of committed): adequate. One predicate (S:337), no *returning*, and row 4's G4 cell names `committed-wait` (S:317). The two senses (the predicate and the row-4 claim) diverge on the review after a no-candidate review: GAP-01.
- GAP-02 (an old push's corps never ends at home): moot (no return lifecycle).
- GAP-03 (committed-wait has no end; job 6): the end is adequate (committed-wait lasts one review, S:337). The de-list is inadequate -> GAP-03, now for every ended decision.
- GAP-04 (a won objective sends the mass home): moot.
- GAP-05 (`DepartmentHave` contradicts itself): adequate at S:333. Table row 5 contradicts it -> GAP-07.
- GAP-06 (the have and a corps walking home): moot (no `Returning`).
- GAP-07 (sortie-end sites; `SplitReserve`): S:310 adequate. S:365 inadequate -> GAP-11. The `SplitReserve` channel is inadequate -> GAP-08.
- GAP-08 (capital take keeps the transfer): moot (transfer only on arrival; a capital take is `state=Ended`, S:333).
- GAP-09 (pipeline order of the covering rewrite): adequate (P:370 `SelectMass -> the covering left-behind rewrite -> PostFromOutside`).
- GAP-10 (the second `MassFor`): adequate (P:370 "every `MassFor` call after `Decide` runs after the ends"). The order itself conflicts with the switch end: GAP-02.
- GAP-11 (`Observe` carry-over without a department record): adequate (S:337 "with or without a department record"; P:350 `AVanillaCommanderChangeKeepsTheSortie`).
- GAP-12 (`CapitalGuard` taken from committed): adequate (S:338 "every `CapitalGuard`").
- GAP-13 (`[Reserves]` committed / returning): `returning=` moot. `committed=` is defined (S:358) against a chosen objective that a no-candidate review lacks -> GAP-06.
- GAP-14 (candidate leaves out `Available`): inadequate -> GAP-14 (`Available` added, "enemy-held" kept).
- GAP-15 (transfer back inside `Evaluate`): moot. The arrival transfer's timing is GAP-15.
- GAP-16 (`IClaimSource` undeclared): adequate for rows 4 and 5 (P:288-289). Row 10 inadequate -> GAP-13.
- GAP-17 (`PlanGroups` and a covering member): adequate in S (S:329). The test is missing -> GAP-18.

**Removed lifecycle and old row numbers.**
- Stale: P:9 (GAP-20); S:365's "arrival ... a reserve taking the formation" (GAP-11); the duplicate row 10 (GAP-12).
- Clean:
  - S:251 and S:382 name the return item as refined or rejected.
  - P:350 `NoReturnRoleOrReasonExists` and T:51 are negative pins.
  - P:275's "returning" describes pass 5b's history.
  - S:306 "a step after row 8" and S:310/S:321 "row 8" (`Mass`) match the new numbering.
  - No text cites a row 11 or 12.

## Tally by element

| Element | Blocker | Major | Minor |
|---|---|---|---|
| ownership-table | 0 | 0 | 2 (GAP-12, 13) |
| covering-force | 0 | 0 | 1 (GAP-17) |
| commitment | 0 | 3 (GAP-01, 02, 08) | 4 (GAP-10, 11, 14, 16) |
| department-reinforcement | 0 | 3 (GAP-04, 05, 07) | 1 (GAP-15) |
| decision-end | 0 | 1 (GAP-03) | 2 (GAP-09, 19) |
| redeploy-role | 0 | 0 | 1 (GAP-18) |
| other | 0 | 1 (GAP-06) | 1 (GAP-20) |
| **Total** | 0 | 8 | 12 |

## Checked and clean

- **Committed-wait is bounded.** On the review after a no-candidate review, row 4 claims nothing (`CurrentMainObjective` = -1, TheaterPlanner.cs:410), so committed-wait lasts exactly one review (pass-9b GAP-03's first half). GAP-01 is the price.
- **Row 8 and capacity.** `SelectMass` takes only from the `MassFor` list (TheaterPlanner.cs:1055-1096). "Then the other committed" is therefore the ready old-push corps in P's capacity, and the unready ones stay `Committed` (GAP-09).
- **Rows 2 and 3 before row 4.** A committed corps in battle is `Immobile` and one low on supply is `Recover`. Both keep their sortie and never reach row 8 (S:321).
- **Capital over claims.** Shipped `Capital` takes `MassEligible` formations nearest the capital (ReserveRules.cs:164-178). Taking `Committed` and `DepartmentMove` too is the shipped home-defence-first order. The sortie and record ends are stated (S:333, S:337).
- **The donor pool.** The donor is `None`, `MassEligible`, standing in d' with `DepartmentHave(d') - HoldStrength >= DepartmentNeed(d')`, and not a garrison or recon incumbent (S:333). A decision-ended corps standing in another department is a donor by standing (`AnEndedPushsCorpsMayDonate`), and its home is then transferred on arrival.
- **`Redeploy` withholding.** `WithheldFromOffense` adds `Role == Redeploy` after `a.Withheld` (CampaignWithholdView.cs:117-119). `Withheld` stays false for `department-reinforcement`, so vanilla's defence may use it on the way, as S:329 says.
- **The mover's (role, reason) list.** `Hold` is not in `Priority` (CampaignMover.cs:115-116), so committed-wait and a hold reserve are never dispatched. `Redeploy` slots in as S:329 orders. The readiness skip comes after the arrived return (335) and before the tempo deferral (340).
- **The covering force.** Points, posts, axis shares, the incumbents' `areaIndex`, the verdict hysteresis, the defence clause slot (CampaignWithholdView.cs:131-132) and the enemy-capital rewrite (S:306, P:370) are unchanged by the simplification. Decision-ended corps near the axis are ordinary `None` candidates of the covering pick, which is intended.
- **Job 4.** `IsUnitAvailableForOffensiveOperations` refuses a unit already listed. The leak is job 6 alone (GAP-03).
