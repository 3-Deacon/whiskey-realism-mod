# D11 build dry run, pass 8b: phase 2

Worktree `.worktrees/plan-foresight` at `f904cfda` (read only). S = spec D11 in `docs/superpowers/specs/2026-10-14-campaign-planner-design.md` (S:n = line), P = `docs/superpowers/plans/2026-10-18-strategic-foresight.md`, T = `docs/loop/foresight/test-plan.md`. Shipped code under `src/WhiskeyRealism/Strategic/Planner/` (file names shortened), tests under `tests/WhiskeyRealism.Tests/`. Decompile `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` present. Phase 1 (F1-F5, F9) assumed built as P says.

**First-priority verdict (ownership table vs shipped G3/G4).** I walked shipped `Plan` (TheaterPlanner.cs:195-277), `Evaluate` (314-385), `Decide` (397-470), `Reserve` (1248-1256), `MassFor` (976-1011), `ReinforcementPick` (1017-1038), `SelectMass` (1055-1096), `PostFromOutside` (808-866), `Garrisons`/`HoldFacing` (787-905), `ReserveRules.Capital`/`Hold` (ReserveRules.cs:164-231) and `ForceAllocator.Run` (ForceAllocator.cs:314-360) through `ListSurpluses`. With rows 4-6, 11, 12 empty and no covering input, rows 1-3, 7, 8 and 9 restate the shipped predicates, and G4's RoleOf order reproduces the shipped assignment order, apart from the one named Marching-builder difference, whose tally cascade the oracle does not name (GAP-12). The table itself is sound. The new majors are in the producers it will carry and in the state those producers key on. These cover: the commitment test against `CurrentMainObjective` (GAP-01), sortie identity (GAP-02, GAP-03), a builder that is also committed (GAP-04), the covering count (GAP-05), persisted sector ids (GAP-06), the F6a seam tests (GAP-07), the department have during a push (GAP-08) and vanilla's job 6 (GAP-09).

## Gaps (18: 0 blocker, 9 major, 9 minor)

**GAP-01. After a no-candidate review, re-picking the same objective finds no committed formation: the unready corps are sent home for one review and recalled the next. `CommitmentSurvivesANoCandidateReview` cannot be written from S.** S:339 judges "committed" against "the state's `CurrentMainObjective` as `Evaluate` reads it (before `Decide`)". It then claims "Objective, not `DecisionSerial`, is the key: `Decide` ... bumps it when the same objective is picked again from none, so a serial key would strand or drop the very corps the RC4 chain is about". Shipped `Decide` writes `CurrentMainObjective = -1` on no-candidate (TheaterPlanner.cs:408-417). The trace, for an unready corps with a sortie of O:
  - On the no-candidate review N, rows 4 and 12 see O as current, the corps is `Committed`, the empty mass leaves it, and row 12 makes it `Returning`. G4 assigns `Redeploy return-home` and the mover sends it home.
  - On review N+1, the state reads -1 before `Decide`, so row 4 never fires. The corps is row 5 `Returning`. Massable for O is "`MassEligible` or committed to the candidate's objective" (P:341), and the corps is neither, so it is not in O's capacity. It stays `Returning` and walks home again.
  - On N+2 (O current again), the sortie still reads O (a Redeploy dispatch records no sortie), so the corps is `Committed` and sent back to the front.

  So the objective key keeps the sortie record alive across the gap, but not the corps' place in the mass. The two sentences of S:339 give different answers for T's `CommitmentSurvivesANoCandidateReview` (TC-COM-KEY).
- Severity: major
- Impact: the RC4 chain returns whenever a no-candidate review occurs (S:408 counts odds drops and accomplished or unavailable objectives as legitimate). The unready corps oscillate home and back, and criterion-return-home counts the oscillation as a pass.
- Assumption: commitment is candidate-relative in `MassFor` (a sortie of the candidate's objective, whatever `CurrentMainObjective` reads). Rows 4 and 12 treat a sortie of an objective that is still offered and not accomplished as "suspended", not returning, for `SwitchDays` reviews after a no-candidate review (that is, `Returning` only once its objective is no longer a candidate or another objective is chosen). The test asserts the corps is not sent home on N or N+1 when O is re-picked at N+1.
- Grounding: shipped code

**GAP-02. Vanilla's re-keys strand sorties, in-flight records and covering keys inside a session. S moves them only on `RekeyInstance`/`RekeyFromLive`.** S:341: "`PlannerState.RekeyInstance` and `RekeyFromLive` (D6 (g)) also move a formation's sortie, every `DepartmentInFlight` value and every `CoveringKeys` element under the old key". A unit key is `name:commander` (SnapshotRules.UnitKey). Three things change the commander between reviews:
  - vanilla's job 24 (`AssignCommando`, 17024; the #6 guard protects only the planner's own picks);
  - a fallen commander's replacement (`ReplaceCommanderOfUnit`, 80022-80028);
  - vertical replacement.

  The next review sees the new key in `PlannerState.Observe` (PlannerState.cs:136-157), which carries only `_departments` across by instance id. It does not call `RekeyInstance`. Then `_keyOfInstance` points at the new key before rows 4-6 and the covering incumbents are read (AssignDepartments runs before `Evaluate`, TheaterPlanner.cs:206). `RekeyInstance` is called only by the appointment executor (CampaignPlannerRuntime.cs:270-275), and `RekeyFromLive` only at `Export` (209-226). So the sortie under the old key is unreachable and is pruned after 10 reviews. The same holds for a `DepartmentInFlight` value and a `CoveringKeys` element (Observe does not move shipped `ReinforcementKey` either, but `RekeyInstance` does, 118).
- Severity: major
- Impact: a corps of the push whose commander falls in battle at the objective loses its commitment. It drops out at readiness 1, so RC4 returns, and it has no trip home. An in-flight record blocks its receiving department for 10 reviews ("one in flight per receiving department", S:335) and then ends as `Pruned`. A covering incumbent is lost.
- Assumption: `Observe`'s carry-over path moves every unit-key-keyed record of the formation (sortie, `DepartmentInFlight` value, `CoveringKeys` element, `ReinforcementKey`) through one private re-key routine shared with `RekeyInstance`. Test: `AVanillaCommanderChangeKeepsTheSortie`.
- Grounding: shipped code, vanilla

**GAP-03. "A changed claim" ends sorties in S:310, but S:339/S:367 keep them through busy and Recover. A literal build deletes the push's sorties in battle.** S:310 (*After-`Decide` order*): "record and sortie ends (arrival, give-up, prune, a changed claim) are applied right after `Decide`". S:367 lists the sortie deletes as "arrival, give-up, prune, or a reserve taking the formation". S:339 says a member taken by vanilla's defence is "busy and not committed until vanilla ends the operation", which implies its sortie survives. A committed corps in battle is row 2 `Immobile`, and one low on supply is row 3 `Recover`: both are "a changed claim".
- Severity: major
- Impact: if a builder deletes on any changed claim, every corps that fights at the objective (`InBattle` is `Busy`, TheaterPlanner.cs:123) loses its sortie. After the battle (readiness low) it is neither massable nor returning, which is RC4 again. A Recover detour also strands it at the supply source.
- Assumption: "a changed claim" ends `DepartmentInFlight` records only (S:335). A sortie ends only on arrival (uncommitted), give-up, prune or a `Capital` claim. `Immobile` and `Recover` keep it. Test: `ABattleAtTheObjectiveKeepsTheSortie`.
- Grounding: none (S's own text)

**GAP-04. A mass member picked as a depot builder is `Committed` by row 4, sent home by row 12, and loses its task.** G4's depot step takes a mass member on its second pass (ForceAllocator.cs:609, 633-645; F9 keeps it, S:345). From the next review the depot ledger lists it as a Marching held builder (DepotTaskLedger.cs:291-305), and its claim is `None` (row 1 takes Wished builders only). Rows 4 and 5 have no held-builder skip, while row 9 does ("Never ... a Marching held builder (`IsHeldBuilder`)", S:322). So whenever its sortie still reads O, the review goes like this:
  - It is `Committed`. `MassFor` drops it (IsHeldBuilder, TheaterPlanner.cs:988).
  - Row 12 makes it `Returning`, and G4 assigns `Redeploy return-home` before the depot step (S:327).
  - F9's incumbent claim needs it still `Free` (S:345), so `BuilderAssignedAt` finds no builder and the ledger ends the task `builder-retasked` (DepotTaskLedger.cs:376-386).

  The sortie still reads O whenever the mover has not yet dispatched it as a builder: `DepotBuilder` is served fifth under the cap of 6 (CampaignMover.cs:115-116), and an "arrived or underway" formation is skipped (335). S:367's "a create for a key with an open sortie keeps the origin" does not say whether a `DepotBuilder` dispatch overwrites the objective.
- Severity: major
- Impact: a builder taken from the push walks home and its depot task is abandoned, which is the RC3 class D11 fixes. `AMarchingBuilderKeepsItsTaskThroughAReadinessDip` passes on a fixture with no sortie and fails on one with a sortie.
- Assumption: rows 4 and 5 (and so row 12) skip a formation the depot ledger holds as a builder (`IsHeldBuilder`, either kind). Its claim stays `None` and its sortie is kept. Every recorded dispatch (`DepotBuilder` included) overwrites the sortie's objective and role and keeps its origin. Test: `ABuilderFromThePushKeepsItsTask`.
- Grounding: shipped code

**GAP-05. The covering count admits committed, returning and in-flight formations near the post. A staged push or a corps walking home fills the covering force.** S:303 *Counted toward `CoveringNeed`*: "every own formation within `R` of the post ... that counts toward coverage, is not recovering and is not massable (the busy, the unready, the map-group player ...)". An unready `Committed`, `Returning` or `DepartmentMove` formation is "not massable" (`MassEligible` false, TheaterPlanner.cs:740-745), so it counts. Row 8's counted-anyway set excludes exactly those claimants ("never `Recover`, `Committed`, `Returning`, `DepartmentMove`", S:321). The verdict reuses the same count ("the non-massable formations within `R` of the forward post", S:304).
- Severity: major
- Impact: when the push stages or marches along the Richmond-Washington axis, its unready corps meet `CoveringNeed`. The pick takes no member (the incumbents drop from `CoveringKeys`), the verdict reads forward, and the axis is bare the day the push moves on. That is RC1 again, and `criterion-covering-held` still passes on `members` only when the map-group rule fires.
- Assumption: the non-massable count uses row 8's counted-anyway predicate over claims (claims `None`, `Immobile` or `PinnedBuilder` that `MassEligible` refuses and `Recovers` does not, at `HoldStrength`, never `Committed`, `Returning`, `DepartmentMove` or `Capital`). Test: `AStagedPushNearThePostIsNotCovering`.
- Grounding: shipped code

**GAP-06. `CoveringKeys` persist a sector id. Sector ids renumber whenever an objective is accomplished or a secession objective toggles.** S:303 (`CoveringKeys`: "unit key -> the axis sector it was first claimed in, persisted"), P:34 (`coveringKeys: [{unitKey, sectorId}]`). `SectorModel.Build` seeds one sector per area holding a non-accomplished objective, "numbered in area-list order" (SectorModel.cs:52-69). `ReadObjectives` skips any objective whose `IsDeactivated()` is true (CampaignSnapshotRuntime.cs:545). D8's objectives 15 and 16 toggle with state support around 0.45 (S:231). So a seed appearing or vanishing shifts every later sector id. `SectorModel` exposes no stable sector key (only `SectorOfArea`, `SectorAt`, `TheaterOf`, `Centroid`, 208-230).
- Severity: major
- Impact: after any accomplishment or toggle, an incumbent's "recorded sector" names a different sector. If that sector is an axis sector, the incumbent is taken first and counted in the wrong sector's hold. G4's `Counted`, the shortfall list, `[Reserves] hold=` and G5b routing then disagree with the sector the formation actually holds for. The one-sector-per-element guarantee of the table breaks silently.
- Assumption: a covering key stores a stable sector anchor, the AI area index of the area the member stood in when first claimed (persisted as `areaIndex`). The plan maps it to today's id through `SectorModel.SectorOfArea`. An anchor that maps to no axis sector makes the incumbent an ordinary candidate where it stands. Test: `ARenumberedSectorKeepsTheIncumbentsSector`.
- Grounding: shipped code, vanilla

**GAP-07. F6a's table tests cannot exercise rows 4-6 and 11. "Re-run without editing" (P:42) leaves F7's and F8's producers untested by them.** P:42: F6b, F7 and F8 "re-run F6a's table tests (`OwnershipRowsTakeOnlyFromTheirListedClaimants`, `OneOwnerAtTheEndOfPlan`, `G3AndG4ReadOneOwnershipTable`) ... against their own claimants, without editing them". In F6a the producers of rows 4-6, 11 and 12 "are empty" (P:42, P:294), and the state they read does not exist yet: `PlannerState.Sorties` is F7's (P:341), `DepartmentInFlight` F8's (P:358) and `CoveringKeys` F6b's (P:302). F6a's per-row cases ("one case per row, hand-built claims", P:291) for rows 4, 5, 6 and 11 can therefore only be structurally green. Re-run unchanged in F7 or F8, they still build no sortie or record, so they never reach the new producer. Row 12's conversion (`Committed` not in the mass -> `Returning`) needs no state and can be F6a's.
- Severity: major
- Impact: the merge seam the plan's precondition relies on is not real for four rows. Either F7 and F8 edit F6a's tests, against P:42, or their producers' takes-from sets go unpinned by the table tests (`ARoleChangeClearsTheRecord` and the like test lifecycle, not the takes-from set).
- Assumption: F6a builds row 12's conversion and row 9's `Committed` take in full. It declares the producer seam as a per-row source the table calls (rows 4, 5, 6 and 11: an `IClaimSource` that is empty in F6a). The table tests are parameterized over a registry of per-row fixtures, and F7, F8 and F6b each add their fixture (a sortie, a record, a covering input) to that registry without changing a test body. P:42 says "add a fixture", not "without editing".
- Grounding: none (P's own seam)

**GAP-08. `DepartmentHave` leaves out the mass, while `MassFor` takes the target sector's hold reserve. Every main effort makes its own department short and draws a donor.** S:335: `DepartmentHave(d)` counts claims `None`, `PinnedBuilder`, `Immobile`, `Capital`, `Hold` ("no `Recover`, `Committed`, `Returning` or `Mass`"). `DepartmentNeed(d)` counts `HoldRatio'` times the elements assigned to d's facing sectors (S:283), the believed enemy at the target included. Shipped `MassFor` admits the hold reserve of the target's own sector and of every released sector (TheaterPlanner.cs:989-990), and row 9 re-claims them `Mass`. So the main effort's department reads short by about its target sector's hold, the same review the push starts. Row 11 then gives it one formation of another department, and another after each arrival.
- Severity: major
- Impact: the push gets a second cross-department feed beside D6's "at most one per decision" reinforcement (S:227 (d)), one corps at a time while the push lasts. This cuts against "armies are territorial" (D6). No criterion or test speaks to it (`WorksWithNoMainEffort` covers only the opposite case).
- Assumption: `DepartmentHave(d)` also counts d's `Mass` and `Committed` formations (they hold against the elements at the target, which are assigned to the target sector), so only a shortfall outside the push draws a donor. Test: `APushDoesNotMakeItsDepartmentShort`. If the feed is intended, S says so and names the test.
- Grounding: shipped code

**GAP-09. Vanilla's job 6 marches a listed covering member away from its post. The mover de-lists only `Recover` and `Redeploy`.** S:305: "A covering member stays withheld from every offensive". S:340: "The mover also takes every `Redeploy` formation out of vanilla's `unitsinoffensiveoperations`". Shipped `UpdateMicroMovementInOffensive` (13968-14061) re-targets every idle listed unit (`GetLastTransmittedPath() < 0`, step at least 2, not in vanilla's capital or depot lists) to the closest enemy objective of its area by `MoveUnitTo`. It reads no availability gate, so `CampaignWithholdView` cannot stop it. Two paths list a formation:
  - the mover, which lists `MainEffort`/`Support` on dispatch (CampaignMover.cs:350);
  - vanilla's own gather (14383-14385), which takes non-withheld formations, for example hold-reserve bodies past the withheld prefix.

  A formation listed either way and chosen today as a covering member at its post (role `Hold`, not `Redeploy`) stays listed, as does a former mass member that gave up outbound (`Hold gave-up`, claim `None` next review).
- Severity: major
- Impact: once idle, a listed covering member whose AI area holds an enemy objective walks off to it; with none in its area, vanilla de-lists it instead (the `else` at 14057). The forward post stands at `R` from the enemy capital, where areas plausibly border enemy objectives. No Whiskey row shows the march (`criterion-axis-not-stripped` reads only `[CampaignMoverIssue]`).
- Assumption: in the pass that releases `Recover` (CampaignMover.cs:201-211), the mover de-lists every `covering-force` member of either role, and every `CapitalGuard` taken from a `Committed` or `Returning` claim. Test: `ACoveringMemberIsTakenOffVanillasOffensiveList`.
- Grounding: vanilla, shipped code

**GAP-10. Fold of pass-7b GAP-06: no interface or rule says what "the garrison ledger's live incumbents" `Plan` receives.** S:310 ("`Plan` receives the garrison ledger's live incumbents (`GarrisonLedgers.For(alliance)`, the records `PlanPosts` would retain)"), P:363. `PlanPosts` runs after `Plan` (CampaignPlannerRuntime.cs:143). Its retention needs a site still in `CollectSites` (not within `SiteRadius` of a secession post), `VanillaThreat` or a quiet span under 14 days, the cap of 6 sorted manned-first, and `Enable Garrison Manning` (GarrisonPlanner.cs:144-238, GarrisonRuntime.cs:33). `Plan`'s signature in S:272/P:172 has no such parameter.
- Severity: minor
- Impact: a builder either re-implements `PlanOne`'s retention inside the pure planner or passes the raw ledger. The latter also shields a quiet, released or flag-off post's former holder from row 11.
- Assumption: the runtime calls a pure `GarrisonPlanner.RetainedIncumbents(s, ledger.Posts, flagOn)` (the `PlanOne` retention without the mass term) and passes the instance ids to a new `Plan(..., IReadOnlyCollection<int> garrisonIncumbents)` overload. Test: `AReleasedPostsHolderMayDonate`.
- Grounding: shipped code

**GAP-11. Fold of pass-7b GAP-14 is inadequate: P's interface still has no probe.** P:322: `AxisShares(CampaignSnapshot s, SectorModel sectors, EnemyPicture picture, float[] holdNeed, float holdRatio, float[] into)`. P:332 says "`AxisShares` takes the probe", and S:303 gives `AxisShares(s, sectors, picture, holdNeed, holdRatio, probe, into)`.
- Severity: minor
- Impact: one obvious answer (S).
- Assumption: the P:322 block gains `ITerrainLineProbe probe` before `into`.
- Grounding: none

**GAP-12. Fold of pass-7b GAP-05: `F6aReproducesTheShippedRoles` names one role difference, but the Marching-builder rule also changes the tally and cascades.** S:321: a Marching held builder "is never a candidate here and is counted nowhere ... the one intended role difference". Shipped `Hold` counts an unready Marching builder anyway, since it is `!MassEligible` and counts toward coverage (ReserveRules.cs:210-218). F9 keeps such a builder as its incumbent, so the pre-F6a goldens carry it in the tally. Counted nowhere, its sector's `SectorHeld` drops. The hold then reserves another body (another role changes), and `SectorMayGiveScout` and the mass capacity can follow. The investigation's 06-23 case is exactly this, the unready Army of Virginia as a depot builder.
- Severity: minor
- Impact: the oracle fails on the 06-23 fixture in more places than its named difference. The rule itself is clear.
- Assumption: the fixture names "a Marching builder is neither a hold candidate nor counted, and the bodies the hold reserves in its place" as the intended difference, asserted as such.
- Grounding: shipped code

**GAP-13. Fold of pass-7b GAP-09: P's pin omits two after-`Decide` steps.** S:310 and S:306 order record and sortie ends right after `Decide`, the covering left-behind rewrite "a step after row 9, before `PostFromOutside`", and "the covering state writes come last". P:370's `PipelineOrderPinned` list is `Evaluate -> Decide -> the record and sortie ends -> SelectMass -> PostFromOutside -> DepartmentReinforcement -> the return moves -> AssignPostures`.
- Severity: minor
- Impact: the two steps are unpinned.
- Assumption: the list gains "the covering left-behind rewrite" after `SelectMass` and "the covering state writes" after the return moves.
- Grounding: none

**GAP-14. `ArmyGroupDoctrine.PlanGroups` reads `FormationRole` but is not in S:331's list of `Redeploy` readers.** It groups every assignment by `SectorId` except `Recover`, `Recon` and `DepotBuilder` (ArmyGroupDoctrine.cs:29). `ArmyGroupManagementPatch` steers vanilla's job 32 from it (ArmyGroupManagementPatch.cs:59). A `Redeploy` formation carries `SectorId` = its recorded sector (covering), the receiving sector (department) or its standing sector (return-home) (S:305, S:310). It is grouped into that sector's army group while it marches elsewhere.
- Severity: minor
- Impact: an army-group attach for a formation on its way out; the census of S:331 is incomplete.
- Assumption: `PlanGroups` skips `Redeploy` as it skips the singly-tasked roles. The census lists `ArmyGroupDoctrine.cs`, and `RedeployReadersHandleIt` covers it.
- Grounding: shipped code

**GAP-15. On the day the verdict flips, S does not say which post a covering member's claim target and G4's Hold/Redeploy distance use, nor the order among one sector's incumbents.** S:304: the members "are ordered by the post of the **stored** verdict", the raw verdict is judged afterwards, and the state writes (the flip) come last. S:305 decides Hold or Redeploy "within ... of the post". `Claim.TargetX/Z` (S:310) and `CoveringPlan.PostX/Z` (S:306) are not tied to either verdict. S:303 takes incumbents "in recorded-sector order and within each sector's `AxisShare`", with no order inside a sector.
- Severity: minor
- Impact: one review's targets on a flip. With one post (002/A) it never arises.
- Assumption: the published post and every member's target are the stored verdict's post on the flip review (the new post from the next review). Inside a recorded sector, incumbents go by distance to that post, the lower instance id on a tie.
- Grounding: none

**GAP-16. The department donor pool can take an active recon mission's formation.** S:335's donor excludes reserves, the mass, garrison posts (live G8 incumbents, P:363), covering members, held builders, returning, committed and in-flight formations. A recon mission's formation is a `None` claim in G3 (G4 assigns `Recon` later, ForceAllocator.cs:334). Row 11 runs before G4 and may pick a standing screen or picket. G4's recon step then sees it `Redeploy` and re-picks or leaves the task unfilled.
- Severity: minor
- Impact: one task's mission ends and is re-bound. Picks of the main effort's own mass are safe (claimed `Mass`).
- Assumption: the donor pool also skips the incumbents of active missions (`ReconMissionLedger.IsActive`, passed to `Plan` as the garrison incumbents are), as G8's are skipped.
- Grounding: shipped code

**GAP-17. "Taken by the capital reserve, the department stays transferred (home defence of its new department)" is false when the capital lies in the donor department.** S:335. Row 7 may take a `DepartmentMove` (S:320). For an East -> West transfer, the Richmond guard is then a West formation. `DepartmentHave(West)` counts it (claims `Capital` count), so the West reads met while its men stand at Richmond.
- Severity: minor
- Impact: a short West silently stops drawing donors while the guard stands. `deptShort=` hides it.
- Assumption: a capital take keeps the transfer only when the capital's sector theater is the receiving department; otherwise the department goes back to the donor, as any other end.
- Grounding: none

**GAP-18. Committed-first `SelectMass` does not say whether a committed corps "enters" its army (D6 (e)).** S:339: "committed formations first ... then the rest by army as D3", "the need's stop applies from the other committed formations on". Shipped `SelectMass` takes whole armies once one member is entered (TheaterPlanner.cs:1067-1088, D6 (e) `AnArmysCorpsGoTogether`). Take a corps of an army that was unready at dispatch and is ready now. If committed members meet the need, it is left behind, and the army is split.
- Severity: minor
- Impact: an army's late corps stays home through a push. `AnArmysCorpsGoTogether` stays green, because its fixture has no sorties.
- Assumption: an army with a committed member is entered, and its other capacity members are taken with the committed ones, before the need's stop.
- Grounding: shipped code

## Pass-7b folds checked

- GAP-01 (row 9 cell, other-committed rank): adequate (S:322 cell, S:339 order, P:348).
- GAP-02 (outbound give-up on the give-up review): adequate (S:310 *Give-ups*, P:349).
- GAP-03 (sortie key after a same-day appointment): adequate (S:339 *Keys written by the Execute job*, P:349). Vanilla's re-key path is new: GAP-02.
- GAP-04 (`HoldFacing` reads chosen bodies only): adequate (S:321, P:293).
- GAP-05 (Marching builder row): adequate as a rule (S:321, P:293). The oracle wording misses the tally cascade: GAP-12.
- GAP-06 (G8 incumbents as donors): adequate as a rule (S:310, P:363). The interface and retention rule are missing: GAP-10.
- GAP-07 (incumbents not reselected; enemy-capital left-behind): adequate (S:303, S:306, P:332).
- GAP-08 (committed locked reinforcement): adequate (S:339, P:349).
- GAP-09 (after-`Decide` order): adequate in S (S:310). P's pin omits two steps: GAP-13.
- GAP-10 (`TestClaims` routing, stray `Committed`): adequate (S:310, P:293).
- GAP-11 (`Capital` over claims; `ClaimTable.cs` owners): adequate (S:310, P:293).
- GAP-12 (`WithholdHoldPrefixes`): adequate (S:305 last sentence, P:332).
- GAP-13 (S:367 keyed by objective): adequate (S:367).
- GAP-14 (`AxisShares` probe): inadequate. P:322's interface contradicts P:332 and S:303: GAP-11.
- GAP-15 (what runs with a flag off): adequate (S:310 *Flags off*, P:349).
- GAP-16 (readiness skip lookup; `Redeploy`): adequate (S:340, P:349).
- GAP-17 (publication and census): adequate: (a) S:331; (b) S:327; (c) P:31, P:370; (d) P:34; (e) P:341.
- GAP-18 (single-post `Kind`): adequate (S:301).

## Checked and clean

- **Row 1** (`PinnedBuilder`) against shipped G4:
  - `AssignPinnedBuilders` uses the same `WishedBuilders` mask that G3's `Capital` excludes (ForceAllocator.cs:438-449, TheaterPlanner.cs:1253).
  - A pinned builder that the hold reserves stays `DepotBuilder` (`AssignHoldReserve` skips `_done`, 472-477).
  - `HoldLeaving` counts it as leaving (796-806), as S:314 says.
- **Row 2** (`Immobile`) against shipped `AssignImmobile`, which ignores `_done` (451-462), so row 2 takes `PinnedBuilder`. `Recovers` needs `Movable` (ReserveRules.cs:139-143), so rows 2 and 3 are disjoint.
- **Row 3** (`Recover`) claimed before `Capital`/`Hold`/`Mass` is equivalent on real plans: `MassEligible` and `Recovers` are disjoint (TheaterPlanner.cs:740-745), and so stay for a committed formation (S:339 keeps the supply and morale minima). Shipped `AssignRecover` skips `_inMass` (ForceAllocator.cs:585), so a hand-built mass could override Recover. The recover fixtures I read (`AlwaysWithheldRoles`, the raw-supply case, `RecoverIsPerFormationAndWalksToSupply`, ForceAllocatorTests.cs:135-175, 455-600) keep their recovering formations out of `MassInstanceIds`. I did not sweep all 88 `MassInstanceIds` sites.
- **Rows 7 and 8** restate shipped `Capital` (`MassEligible && !excluded`, nearest the capital) and `Hold`: candidates are `MassEligible && !capitalReserved`; the counted-anyway set is `(!MassEligible || capital) && CountsTowardCoverage && !Recovers && (!capital || capital sector)` (ReserveRules.cs:164-231). Reserve bodies count at `Strength` and counted-anyway bodies at `HoldStrength`, as S:321 says.
- **G3 and G4 hold inputs agree.** G4's `_need` comes from `SectorPlan.HoldNeed`. A sector with no `SectorPlan` has no own formation and no threat (TheaterPlanner.cs:705-708), so it reserves nothing either way. `NearX/Z` come from `NearestLandBelief` from the centroid on both sides (1229-1231; ForceAllocator.cs:373-379).
- **Row 9** restates `MassFor`'s exclusions: Capital, gave-up for the objective, held builder, other sectors' holds unless released (TheaterPlanner.cs:988-990).
- **`HoldFacing`** reads `HoldReserved` only (787-797), which S:321's chosen-body rule restates.
- **Give-ups.**
  - `MoveTracker.CollectGaveUp` carries `ObjectiveId` and lists planner-budget give-ups only (MoveTracker.cs:235-242).
  - `CampaignMover.ObjectiveOf` gives -1 for every role but MainEffort/Support (356-362), so a builder's, a Recover's, a covering or a return-home give-up is objective -1, matched by target as S:339 says.
  - A refusal writes a `Failed`/`GaveUp` record, and `MoveIssue` returns `moved` only for a taken move (MoveIssue.cs:22-49), which fits "moved || Queued".
- **`Redeploy` readers that already do the right thing:**
  - `ReconMissionLedger.cs:310` (the rally point considers only `Hold`/`MainEffort`);
  - `SteeringRules.cs:96-101` (a box only for MainEffort, Support, Hold);
  - `CapitalFloorPolicy.cs:83` (reads `CapitalGuard` only);
  - `DepotTaskLedger.cs:449` (reads `DepotBuilder` only).
- **`CampaignWithholdView`:**
  - the offense clauses are at 117-119, where `Role == Redeploy` slots in;
  - the defence `!Withheld` test is at 131 and the same-sector `Hold` exception at 132, so the covering clause fits between them;
  - `Read` already fetches the plan (179).
- **Mover.**
  - One `Priority` array drives dispatch and `MovesRole` (CampaignMover.cs:115-123).
  - The tempo gate applies only to MainEffort/Support (338-339), so `Redeploy` needs no release.
  - The Recover de-list pass (201-211) is the slot for `Redeploy`.
- **Enemy capital match.** A town record's position is `PositionOf(town)` and the capital's is `PositionOf(alliance.capital)` (CampaignSnapshotRuntime.cs:170-181, 590-596), so `SameTargetRadius` against `EnemyCapitalX/Z` matches the capital's town record exactly.
- **`DepartmentHoldMet`** keeps its shipped have, every formation of the department (TheaterPlanner.cs:955-968), as S:335 says. `PostFromOutside`'s outside pool restated over `None` claims equals the shipped tests (849-850).
