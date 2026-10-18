# D11 build dry run, pass 9b: phase 2

Worktree `.worktrees/plan-foresight` at `308271a8` (read only). S = spec D11 in `docs/superpowers/specs/2026-10-14-campaign-planner-design.md` (S:n = line), P = `docs/superpowers/plans/2026-10-18-strategic-foresight.md`, T = `docs/loop/foresight/test-plan.md`. Shipped code under `src/WhiskeyRealism/Strategic/Planner/` (file names shortened), tests under `tests/WhiskeyRealism.Tests/`. Decompile `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` present. Phase 1 (F1-F5, F9) assumed built as P says.

**First-priority verdict.** The pass-8b fold made commitment candidate-relative (S:339 *Commitment is candidate-relative*) but left the older definitions beside it (S:339's first sentence, S:341's *returning*). It also left the arrival rule ("arrival deletes only a sortie that is not committed", S:310) and the pipeline order (row 11 before row 12, P:374) as they were. Walked review by review, the new rule produces three wrong states that no test as named catches:
- a corps of an old push never ends its sortie at home (GAP-02);
- a no-candidate wait has no end and is left to vanilla's job 6 (GAP-03);
- the department have counts a corps walking home or not, depending on whether its old objective is still offered (GAP-06).

The other majors are an appended fold that contradicts its own paragraph (GAP-05), a won objective sending its mass home (GAP-04), and `SplitReserve` on committed members (GAP-07). The ownership table and the covering force are otherwise sound. Their pass-8b folds are complete apart from the minors below.

## Gaps (17: 0 blocker, 7 major, 10 minor)

**GAP-01. The GAP-01 fold left three definitions of "committed" and "returning" in S, and two of them contradict committed-wait.** [element: commitment/sorties]
- S:339 still opens: "A formation is *committed* to objective `O` while it has a sortie of `O` and `O` is the state's `CurrentMainObjective` as `Evaluate` reads it (before `Decide`)". The fold appended at the end of the same bullet: "row 4 claims `Committed` every formation whose sortie's objective is still a candidate this review ... on a review with no main effort it holds in place (`Hold committed-wait`)".
- S:341 still defines *returning* as "a sortie that is not committed (its objective is not the current one, **or there is no main effort**; ...)". That is the opposite of committed-wait.
- Table row 4's G4 column (S:317) says only "via row 9 when the mass takes it, else row 12". `committed-wait` appears only in S:310 *Hand-built tables* and S:339.
- T's TC-COM-KEY `CommitmentSurvivesANoCandidateReview` (P:352: "not sent home on the no-candidate review or the one after") and TC-RET-SEND `AnEndedDecisionSendsItsSortiesHome` cannot both be written from S:341 as it reads. A no-candidate review is an ended decision by S:341's text.
- Severity: major
- Impact: a top-down builder implements S:339's first sentence or S:341, and RC4's no-candidate oscillation (pass-8b GAP-01) returns. Every reader of "committed" disagrees on which predicate to call: `Massable`, row 4, row 12, F9's surplus ("not committed or returning", S:345), `DepartmentHave`, the arrival end and `[Reserves] committed=`.
- Assumption: one predicate, `PlannerState.CommittedTo(key) = objective` of a sortie whose objective is an offered candidate record, defined once (row 4). S:339's first sentence and S:341's parenthesis are rewritten to it. *Returning* means row 5 (the objective is no longer offered, or a builder's sortie whose task ended) or row 12 (another main effort chosen). Row 4's G4 cell names `Hold committed-wait`.
- Grounding: none (S's own text)

**GAP-02. Under candidate-relative commitment, a corps of an old push never ends its sortie at home while the old objective is still offered. It stands at its origin as `Redeploy return-home` for good, counted in no hold.** [element: commitment/sorties]
Trace, with S:310, S:339 and S:341 as written. Corps C has a sortie of objective O and origin H. The main effort switches to P while O is still offered (`Evaluate` keeps every available, unaccomplished, not-own-held record, TheaterPlanner.cs:334).
- Each review, row 4 claims C `Committed` (O is still a candidate). Row 4 has no distance test.
- C is not in P's capacity when it is unready, of another department (`MassFor`'s department filter, TheaterPlanner.cs:994) or past P's need.
- Row 12 then turns C into `Returning` ("a main effort with another objective is chosen"), so G4 assigns `Redeploy return-home`.
- C walks to H. On arrival, the after-`Decide` end step runs before row 12, and C's claim there is still `Committed`. Since "arrival deletes only a sortie that is not committed" (S:310), the sortie survives.
- Next review: row 4 `Committed` again, row 12 `Returning` again, target H while C stands at H. S:331's "within `max(BugleRange, ArrivalRadius)` of the point it is an ordinary `Hold` at the next review" never takes effect, because the next review re-derives `Returning`.
- The rows never count `Committed` or `Returning` in a sector hold (S:317-318). So C's home sector reserves another body in its place, and C stays withheld from vanilla's offensives (S:331) until O falls, is accomplished or C is pruned.
- Severity: major
- Impact: every push that ends with its objective still standing parks its unready or foreign corps at home outside every hold and every ordinary role, indefinitely. `ArrivedSortieIsDeleted` passes on a fixture whose objective is gone and fails on this one. criterion-return-home counts the case as a pass ("stands within arrival radius of its origin", S:411).
- Assumption: the arrival end runs on the after-`Decide` state and deletes a sortie whose formation stands within `max(BugleRange, ArrivalRadius)` of its origin unless its objective is the objective chosen this review (a staged gather near home for the current push keeps it). Row 12 applies S:341's distance test, so a `Committed` formation at its origin becomes an ordinary claim. Test: `AnOldPushsCorpsEndsItsSortieAtHome`.
- Grounding: shipped code (TheaterPlanner.cs:334, 994); none for the arrival rule (S's own text)

**GAP-03. `Hold committed-wait` has no end. Not withheld and still on vanilla's offensive list, it is left to vanilla's job 6.** [element: commitment/sorties]
- S:339: "on a review with no main effort it holds in place (`Hold`, reason `committed-wait`, not withheld)". Nothing bounds it. Pass-8b's assumption bounded it by `SwitchDays`, and the fold dropped the bound. A legitimate drop "for the odds" (S:408) can last as long as the presumption keeps the CSA's eastern offensives ineligible (S:302's own example). The push's corps then stand at the front for the whole period, counted in no sector hold (row 4 is never counted, S:317), and never appear in `returningUnits=`, so criterion-return-home cannot see them. That is RC4's second half ("every formation held where it stood, far from home", P:341), after every legitimate drop.
- The mover listed them in `unitsinoffensiveoperations` at dispatch (CampaignMover.cs:350). S:340's de-list covers only `Redeploy`, covering members and a `CapitalGuard` taken from a committed or returning claim.
- Vanilla's `UpdateMicroMovementInOffensive` (13968-14061) re-targets every idle listed unit at readiness step 2 or more, with no availability gate, to the closest objective of its area (`MoveUnitTo`, 14036), or de-lists it when there is none (14057). Job 4 does not re-take a listed unit (`IsUnitAvailableForOffensiveOperations` 14109 refuses one already in `unitsinoffensiveoperations`), so the leak is job 6. Once de-listed, `WithheldFromOffense` (CampaignWithholdView.cs:117-119) does not withhold a `Hold` that is not withheld, so job 4 may then take it.
- The role sector and target of the `Hold` are not stated either: the claim's `SectorId` is -1 (row 4 is counted nowhere) and `TargetX/Z` is unspecified (S:310 *Role sector and target* has no entry for it).
- Severity: major
- Impact: on a no-candidate day, the corps that recover their readiness march alone onto the nearest enemy objective of their area: an uncoordinated vanilla attack by the push the planner just suspended. A long no-candidate spell strands the push forward exactly as before D11.
- Assumption:
  - committed-wait lasts at most `TheaterPlanner.SwitchDays` consecutive no-main-effort reviews (counted per sortie, persisted), then row 12 makes the formation `Returning`;
  - while waiting, the formation is withheld from offence only (the `MainEffort` clause at CampaignWithholdView.cs:118 extended by reason `committed-wait`) and de-listed in the mover's release pass;
  - its role sector is its standing sector (`_sector[i]`) and its target is its position.

  Tests: `ACommittedWaitEndsAfterSwitchDays`, `ACommittedWaitIsTakenOffVanillasOffensiveList`.
- Grounding: vanilla (13968-14061, 14080-14158), shipped code

**GAP-04. A won objective sends its whole mass home.** [element: commitment/sorties]
S:341: a formation is returning when its sortie is not committed. Under the candidate-relative rule, that holds once the objective is "no longer a candidate" (S:339), and an objective the side now holds is skipped by `Evaluate` (`o.HolderAlliance == s.Alliance`, TheaterPlanner.cs:334). On the review after a capture:
- every mass member that the next main effort's capacity does not take is claimed `Returning` (row 5) and sent back to its origin;
- the hold reserves cannot keep it, because row 8 takes only `None` (S:321);
- only the capital reserve (row 7) or a new mass (row 9) can.

The investigation's fix brings back only "a formation whose home sector's hold is short" (investigation.md:72). The user's decision speaks of "stranded formations" (decisions.md:10). D11 returns every formation, whatever the need at home and whatever the ground it now holds.
- Severity: major
- Impact: the AI takes an objective and walks off it the next day. The captured objective's own sector, now facing the enemy, is held only by what the hold can reserve from `None` formations. criterion-return-home rewards it.
- Assumption: a sortie whose objective the side now holds (or that is accomplished in the side's favour) is deleted at the after-`Decide` end step, and its formations become ordinary claims where they stand (the hold may reserve them). Only a decision that ended without the objective (odds, unavailable, a switch, give-ups) sends corps home. If D11 means to bring a victorious army home, S says so and names the test.
- Grounding: shipped code (TheaterPlanner.cs:334); none for intent (investigation.md:72 conditions on a short home hold)

**GAP-05. The GAP-08 fold contradicts its own paragraph: S:335 says both that `DepartmentHave` excludes `Mass` and `Committed` and that it counts them.** [element: department-reinforcement]
S:335 *Pool and sums* still reads: "`DepartmentHave(d)` = `HoldStrength` of d's formations that ... are claimed `None`, `PinnedBuilder`, `Immobile`, `Capital` or `Hold` (no `Recover`, `Committed`, `Returning` or `Mass`)". The same bullet then appends: "`DepartmentHave(d)` also counts d's `Mass` and `Committed` formations". P:367 follows the appended sentence (`APushDoesNotMakeItsDepartmentShort`).
- Severity: major
- Impact: a builder writing `DepartmentHave` from the definition sentence builds the shipped-before-fold behaviour (pass-8b GAP-08's donor stream). `OneHaveForRankDonorAndRow` passes either way.
- Assumption: the definition sentence is rewritten to the claim set `None`, `PinnedBuilder`, `Immobile`, `Capital`, `Hold`, `Mass`, `Committed` (and see GAP-06 for the timing), and the trailing sentence is deleted.
- Grounding: none (S's own text)

**GAP-06. `DepartmentHave` counts a corps walking home, or not, depending on whether its old objective is still offered. A returning push makes its department short and draws a donor that then over-fills it.** [element: department-reinforcement]
- P:374 pins `SelectMass -> PostFromOutside -> DepartmentReinforcement -> ... -> the return moves`, and table rows 11 and 12 run in that order (S:324-325). At row 11, a corps of an old push whose objective is still offered still holds its row 4 `Committed` claim, because row 12 has not yet turned it into `Returning`. GAP-05's fold counts `Committed` in the have, so it counts.
- A corps whose objective was taken or lost is row 5 `Returning` from `Evaluate` on, and S:335 and S:341 ("counts in no sector's or department's hold") leave it out.
- A committed-wait corps (no main effort) counts at the front.
- So the same physical state, a corps marching back across its department, counts in one case and not in the other.
- Where it does not count, the department reads short for the whole return march. Row 11 picks a donor within 10 days, and the returning corps then arrive on top of it. The donor test protects only the donor.
- Severity: major
- Impact: `[Reserves] deptShort=` and the donor picks follow the objective's fate, not the department's ground. Every return march can trigger a transfer that the arrivals make redundant. criterion-department-reinforced counts it as a success.
- Assumption: `DepartmentHave(d)` is computed over the claims after row 12 (the return moves before `DepartmentReinforcement`, P:374 re-pinned), and it counts d's `Returning` formations (a D6 reinforcement's department already transferred back on its first `Returning` claim, S:341), as it counts `Mass` and `Committed`. Tests: `AReturningPushDoesNotDrawADonor`, `ACorpsWalkingHomeCountsTheSameWhateverItsObjective`.
- Grounding: shipped code (pipeline order in TheaterPlanner.cs:222-274); none for the have (S's own text)

**GAP-07. The sortie-end fold left two contradicting sites, and S never says what `SplitReserve` does to a committed member.** [element: commitment/sorties]
- S:339 *Sortie ends*: "only on arrival (uncommitted), give-up, prune or a `Capital` claim". S:310 *After-`Decide` order* still lists "(arrival, give-up, prune, a changed claim)", and S:367 still lists "delete on arrival, give-up, prune, or a reserve taking the formation".
- S:310 *Row 9* also says `SelectMass` "takes every committed formation of the chosen objective in the capacity whatever the need (an issued march is not recalled for being over it) ... and `SplitReserve` turns an excess into `Reserve` as shipped". Shipped `SplitReserve` (ForceAllocator.cs:908-940) turns the mass members **farthest** from the target into `Reserve` once the mass exceeds `ReserveSurplusMargin` (1.3) x the required force, held at their own position and withheld. With every committed formation taken whatever the need, that excess is typically the committed corps that fell back on the march.
- A `Reserve` is in the G8 post pool (`PostCandidate`, ForceAllocator.cs:575) and in `Allocation.Surpluses` (1167). G8 may seat a committed corps at an ordinary garrison post the same review (`Garrison`, marching elsewhere). Is that "a reserve taking the formation" (S:367, sortie deleted)? Is it a changed claim (S:310)? Or is the sortie kept (S:339)? G4 cannot tell a committed `Mass` claim from another, since row 9 replaces the claim.
- Severity: major
- Impact: the corps D11 keeps in the mass through a readiness dip is split off as a reserve and can be posted away. "Keeps a dispatched main effort together" (S:249) fails exactly when the enemy estimate shrinks. A builder picks one of three sortie-end sets.
- Assumption:
  - S:310 and S:367 are rewritten to S:339's list;
  - a `Mass` claim taken from `Committed` carries reason `committed` (`Claim.Reason`), and `SplitReserve` never makes such a member `Reserve` (the excess comes from the uncommitted members, farthest first).

  Test: `SplitReserveKeepsCommittedMembers`.
- Grounding: shipped code (ForceAllocator.cs:575, 908-940, 1167)

**GAP-08. The GAP-17 fold is appended next to two unconditional sentences that it contradicts.** [element: department-reinforcement]
S:335 says "the capital reserve may still take it (home defence first; the record is cleared, the department stays transferred)" and "Taken by the capital reserve, the department stays transferred (home defence of its new department)". It then appends: "A capital take keeps the transfer only when the capital's sector theater is the receiving department; otherwise the department goes back". P:364 keeps `ACapitalReserveTakeKeepsTheTransfer` beside P:367's `ACapitalTakeOutsideTheReceiverTransfersBack`.
- Severity: minor
- Impact: the rule is clear from the last sentence. The first test name reads as unconditional.
- Assumption: both earlier sentences gain "when the capital lies in the receiving department". The first test's fixture puts the capital there.
- Grounding: none

**GAP-09. `PipelineOrderPinned` places the covering left-behind rewrite after `DepartmentReinforcement`. S puts it before `PostFromOutside`.** [element: covering-force]
S:306: the rewrite is "a step after row 9, before `PostFromOutside`". P:374's list reads `SelectMass -> PostFromOutside -> DepartmentReinforcement -> the covering left-behind rewrite (after SelectMass) -> the return moves`.
- Severity: minor
- Impact: the source pin and S disagree. On the enemy-capital day, `PostFromOutside`'s inside pledge would see `covering-force` members instead of ordinary hold reserves.
- Assumption: S's order. P:374 moves the rewrite to directly after `SelectMass`.
- Grounding: none

**GAP-10. The plan's second `MassFor` call is not placed relative to the record and sortie ends.** [element: ownership-table]
Shipped `Plan` calls `MassFor` again after `Decide` for `MainCapacityStrength`, "the capacity the odds were judged on" (TheaterPlanner.cs:251). S:310 applies the ends "right after `Decide`, before `SelectMass`". An outbound give-up or an arrival deleted there changes `Massable` between `Evaluate`'s capacity and this one. P:374's pin does not name the call.
- Severity: minor
- Impact: the published `cap=` can differ from the capacity the candidate's odds were judged on, which criterion-mass-kept reads ("`cap` below the committed strength", S:408).
- Assumption: the ends run after the second `MassFor` (immediately before `SelectMass`), and the pin names it.
- Grounding: shipped code

**GAP-11. `Observe`'s carry-over, which the GAP-02 fold builds on, fires only when the old key has a department record.** [element: commitment/sorties]
S:339 *Re-keys by vanilla* moves the sortie, records, covering key and `ReinforcementKey` through `Observe`'s carry-over. Shipped `Observe` carries over only when `_departments.TryGetValue(previousKey)` succeeds (PlannerState.cs:143-150). A formation first observed with `standing == Theater.Unknown` records no department (line 154), so its re-key moves nothing, and its sortie and records stay under the old key.
- Severity: minor
- Impact: a re-keyed corps standing in an unknown-theater sector loses its sortie. That case is rare.
- Assumption: the carry-over runs whenever `previousKey != unitKey`, moving whichever records exist under the old key.
- Grounding: shipped code

**GAP-12. The mover cannot tell which `CapitalGuard` was "taken from a `Committed` or `Returning` claim".** [element: redeploy-role]
S:340: the mover de-lists "every `CapitalGuard` taken from a `Committed` or `Returning` claim". Row 7 replaces the claim with `Capital` (S:310), the `Capital` take deletes the sortie at the after-`Decide` step (S:339), and `Assignment` keeps its shape (S:327). By the Execute job, nothing published records the earlier claim.
- Severity: minor
- Impact: the rule as written cannot be implemented.
- Assumption: the mover de-lists every `CapitalGuard` whose formation is flagged `InOffensiveOps` (the outcome intended: job 6 skips only `groupstodefendcapital` and `unitsconstructingsupplydepots` members, 14023, and the mover's guard is not in that list).
- Grounding: vanilla (14023), shipped code

**GAP-13. The `[Reserves]` fields `committed=` and `returning=`, and criterion-mass-kept's "committed strength", are never defined.** [element: other]
S:360 lists `committed=<n> returning=<n> returningUnits=<unit+...>` with no definition. S:408 reads "`cap` below the committed strength, while `[Reserves] committed>0`". At the end of `Plan`, a `Committed` claim survives only as committed-wait: a review with a main effort turns every committed formation into `Mass` or `Returning`. So "count of `Committed` claims" is 0 on every normal review.
- Severity: minor
- Impact: `ReservesRow`'s committed test cannot be written red-first, and the criterion's guard depends on the builder's pick.
- Assumption: `committed=` counts formations whose sortie's objective is the chosen objective (or the last one, on a no-candidate review), and the committed strength is their summed `Strength`. `returning=` counts the end-of-`Plan` `Returning` claims, and `returningUnits` names them.
- Grounding: none

**GAP-14. Row 4's "candidate" leaves out `Available`.** [element: commitment/sorties]
S:339: "still a candidate this review (an enemy-held, not accomplished objective record)". Shipped candidates are records that are `Available`, not `Accomplished` and not held by the side (TheaterPlanner.cs:334), so they include neutral-held records and exclude unavailable ones.
- Severity: minor
- Impact: a sortie of a neutral objective is `Returning` by S's parenthesis and admissible by `Massable`. A sortie of an unavailable one is `Committed`.
- Assumption: "candidate" means the `Evaluate` filter exactly; the parenthesis is replaced by a reference to it.
- Grounding: shipped code

**GAP-15. The D6 reinforcement's transfer back "when its `Returning` claim is first written" is a `PlannerState` write from a row that runs inside `Evaluate`, which runs twice on a terrain-filling review.** [element: commitment/sorties]
S:341. Row 5 is part of `ClaimTable.Build` (rows 1-8, inside `Evaluate`, S:310). Row 12 runs after `Decide`. S:304 confines its own writes to "once in `Plan` after `Decide`". `c.Department` (`AssignDepartments`, called at TheaterPlanner.cs:206, body 909-921) is computed before `Evaluate`, so a transfer inside it leaves that review's department filter stale.
- Severity: minor
- Impact: the review's `MassFor` department filter, `DepartmentHave` and the `dept=` row read the old department.
- Assumption: the transfer back is applied at the after-`Decide` end step, and `c.Department` is refreshed for that formation.
- Grounding: shipped code

**GAP-16. The seam fold names `IClaimSource` but no interface declares it, and `ClaimTable.Build` has no parameter for it.** [element: ownership-table]
- S:310 *Seam*: "rows 4, 5, 6 and 11 read a per-row claim source (`IClaimSource`, empty in F6a)". P:281-288 declares no such type. `ClaimTable.Build(s, sectors, holdNeed, nearX, nearZ, capitalFloor, held, gaveUp, state, covering)` (S:310) takes none.
- Row 11 is not in `ClaimTable.Build` at all (it runs after `Decide`).
- T's `RowSourcesAreEmptyInF6a` and `TheFixtureRegistryDrivesEveryRowCase` need the type and the registry.
- Severity: minor
- Impact: F7 and F8 each extend a seam whose shape F6a invented. The seam does not hold unless F6a fixes it.
- Assumption: `internal interface IClaimSource { void Claim(CampaignSnapshot s, PlannerState state, IList<Claim> claims); }`. `ClaimTable.Build` takes `IReadOnlyList<IClaimSource> rows` for rows 4-6. `TheaterPlanner.DepartmentReinforcement` takes one for row 11. The registry is a static list of `(row, fixture builder)` in `OwnershipTableTests`.
- Grounding: none

**GAP-17. A `Hold covering-force` member at the post is grouped by `ArmyGroupDoctrine` into its recorded sector's army group.** [element: covering-force]
The GAP-14 fold skips `Redeploy` in `PlanGroups` (S:331). A member at its post is `Hold` with `Assignment.SectorId` = its recorded sector, wherever it stands (S:305). `PlanGroups` groups by `SectorId` (ArmyGroupDoctrine.cs:29), and `ArmyGroupManagementPatch` steers vanilla's job 32 from it (ArmyGroupManagementPatch.cs:59).
- Severity: minor
- Impact: an army-group attach that pulls a member standing at the post toward a sector it does not stand in.
- Assumption: `PlanGroups` skips reason `covering-force` as it skips `Redeploy` (or groups a covering member by `_sector[i]`). `PlanGroupsSkipsRedeploy` gains the case.
- Grounding: shipped code

## Pass-8b folds checked

- GAP-01 (commitment candidate-relative, committed-wait): rule added at S:339's end and P:352, but the older definitions stand (S:339's first sentence, S:341, S:317). The fold contradicts: GAP-01. Its consequences: GAP-02, GAP-03.
- GAP-02 (vanilla re-keys): adequate (S:339 *Re-keys by vanilla*, P:352). Shipped `Observe`'s branch condition limits it: GAP-11.
- GAP-03 (sortie ends): rule at S:339 *Sortie ends*, but S:310 *After-`Decide` order* and S:367 are not rewritten: GAP-07.
- GAP-04 (builder from the push): adequate (S:339 *Builders*, P:352 `ABuilderFromThePushKeepsItsTask`).
- GAP-05 (non-massable covering count): adequate (S:303 last sentences, P:334).
- GAP-06 (covering keys by area): adequate. `FormationRecord.AreaIndex` exists (CampaignPlannerContracts.cs:67). `SectorModel.Build` gives every area a sector (SectorModel.cs:76-83), so `SectorOfArea` returns -1 only for an unknown index (208-211). P:34 persists `areaIndex`.
- GAP-07 (seam): rule adequate (S:310 *Seam*, P:42, P:294), but the interface is missing: GAP-16.
- GAP-08 (`DepartmentHave` counts the push): appended next to the contradicting definition: GAP-05. Timing against row 12: GAP-06.
- GAP-09 (job 6 and covering members): adequate for covering members (S:340, P:334). The `CapitalGuard` clause is not implementable: GAP-12. Committed-wait is uncovered: GAP-03.
- GAP-10 (G8 retained incumbents): adequate (S:335, P:367 `GarrisonPlanner.RetainedIncumbents`, the `Plan(..., garrisonIncumbents, reconIncumbents)` overload).
- GAP-11 (`AxisShares` probe): adequate (P:323 carries `ITerrainLineProbe probe`).
- GAP-12 (oracle cascade): adequate (S:310 *Oracle*, P:294).
- GAP-13 (pipeline pin steps): both steps present in P:374, but the left-behind rewrite is placed against S:306: GAP-09.
- GAP-14 (`ArmyGroupDoctrine`): adequate for `Redeploy` (S:331, P:294). `Hold covering-force`: GAP-17.
- GAP-15 (flip-review post, incumbent order): adequate (S:303 last sentence, S:304 last sentence, P:334).
- GAP-16 (recon missions as donors): adequate (S:335, P:367 `AReconMissionIsNeverADonor`). `ReconMissionLedger.IsActive` takes a state (ReconMissionLedger.cs:242), so the runtime passes the active missions' instance ids.
- GAP-17 (capital take outside the receiver): rule appended next to two unconditional sentences: GAP-08.
- GAP-18 (an army with a committed member): adequate (S:339 *Armies*, P:352 `AnArmyWithACommittedMemberIsEntered`).

## Tally by element (blocker/major/minor)

| Element | Blocker | Major | Minor |
|---|---|---|---|
| ownership-table | 0 | 0 | 2 (GAP-10, 16) |
| covering-force | 0 | 0 | 2 (GAP-09, 17) |
| commitment/sorties | 0 | 5 (GAP-01, 02, 03, 04, 07) | 3 (GAP-11, 14, 15) |
| department-reinforcement | 0 | 2 (GAP-05, 06) | 1 (GAP-08) |
| redeploy-role | 0 | 0 | 1 (GAP-12) |
| other | 0 | 0 | 1 (GAP-13) |
| **Total** | 0 | 7 | 10 |

## Checked and clean

- **`RoleOf`'s inputs.** `RoleOf(in Claim, in FormationRecord)` (P:287) can compute S:310's "distance to the post": a covering member's `Claim.TargetX/Z` is the post (S:310 *Role sector and target*) and the record carries the position.
- **Rows 2 and 3 before row 4.** A committed formation in battle is `Immobile` and one low on supply is `Recover`. Both keep their sortie (S:339). Next review the formation is `Committed` again, and row 9 never takes `Immobile` or `Recover` (S:322).
- **`MassFor` and the department filter.** The mass's natives are by construction of the target's department (`DepartmentOf(c, i) != targetTheater` goes foreign, TheaterPlanner.cs:994). The D6 reinforcement is transferred at the pick (`NoteReinforcement`, 1100-1112). So GAP-08's "Mass and Committed hold against the elements at the target" counts them in the department whose need holds those elements, for `Mass`. `Committed` to another objective is GAP-06.
- **Job 4 and the offensive list.** `IsUnitAvailableForOffensiveOperations` refuses a unit already in `unitsinoffensiveoperations` (14109), at readiness below step 2 (14091) and in `groupstodefendcapital` or `unitsconstructingsupplydepots` (14118, 14122). The leak is job 6 alone while a formation stays listed (GAP-03, GAP-12).
- **Covering pick against shipped `Hold`.** Shipped hold candidates are the formations standing in the sector (`HoldSectorOf(...) != sector`, ReserveRules.cs:209). S:303's members are a subset of the axis sectors' reserved bodies, each sector capped at its `AxisShare` (at most `HoldNeed`), so the covering force never adds to the holds (S:302).
- **Defence clause slot.** `WithheldFromDefense` (CampaignWithholdView.cs:126-134). The covering clause sits after `!a.Withheld` (131) and before the same-sector exception (132), which reads `a.SectorId`, a covering member's recorded sector. The clause must therefore decide `covering-force` alone, as S:305 says.
- **Mover.**
  - `Hold` and `Reserve` are not in `Priority` (CampaignMover.cs:115-116), so committed-wait and a split `Reserve` are never dispatched.
  - The arrived-or-underway return (335) comes before the readiness skip S:340 places "after ... the arrived-or-underway return".
  - Sorties keyed through `KeyOf` (P:351) slot in at 348-350.
- **Department pick vs sorties.** A donor with a sortie is impossible at the pick: an uncommitted sortie at its origin is deleted at the after-`Decide` end step before row 11, a committed one is claimed `Committed`, a held builder's is skipped, and row 11 takes only `None` (S:324). No in-flight formation is later claimed `Returning` by row 5 before row 6.
- **`Observe` timing.** `AssignDepartments` runs before `Evaluate` (TheaterPlanner.cs:206), so the GAP-02 fold's carry-over re-keys before rows 4-6 read state (subject to GAP-11).
