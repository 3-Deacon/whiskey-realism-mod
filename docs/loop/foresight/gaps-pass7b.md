# D11 build dry run, pass 7b: phase 2

Worktree `.worktrees/plan-foresight` at `4193ea0e` (read only). S = spec D11 in `docs/superpowers/specs/2026-10-14-campaign-planner-design.md` (S:n = line), P = `docs/superpowers/plans/2026-10-18-strategic-foresight.md`, T = `docs/loop/foresight/test-plan.md`. Shipped code under `src/WhiskeyRealism/Strategic/Planner/` (file names shortened), tests under `tests/WhiskeyRealism.Tests/`. The decompile `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` is present. Phase 1 (F1-F5, F9) is assumed built as P describes.

**First-priority verdict.** I walked every shipped G3 step (`Plan` 195-277, `Evaluate` 314-385, `Reserve` 1248-1256, `MassFor` 976-1011, `SelectMass` 1055-1096, `PostFromOutside` 808-866, `HoldFacing` 787-797) and every shipped G4 step (`ForceAllocator.Run` 314-360 through `ListSurpluses`). With rows 4-6, 11 and 12 empty and no covering input, rows 1-8 reproduce shipped `ReserveRules.Capital`, `ReserveRules.Hold` and `MassFor`. That covers Capital's exclusions, Hold's candidate and counted-anyway sets, and the mass's takes. The G4 order "hold reserves skipping the mass" (S:327) also keeps a hand-built plan's mass overriding a Hold claim, as shipped `AssignHoldReserve` does (ForceAllocator.cs:472-477). One place still breaks `F6aReproducesTheShippedRoles` when the table is coded literally: `HoldFacing` (GAP-04). Two more things are not equivalence breaks, but they are new rules the merge seam cannot carry without settling them: the Marching builder has no row (GAP-05), and the row-9 cell is stale (GAP-01). The `TestClaims` fold works for hand-built plans. It does not cover real plans allocated with inputs the plan never saw (GAP-10).

## Gaps (18: 0 blocker, 8 major, 10 minor)

**GAP-01. Row 9's *takes from* cell still says "`Committed` to this objective". The row-9 paragraph and P say "to any objective".** S:322 (table row 9): "`None`; `Hold` of the target's own sector ...; `Committed` to this objective; `Returning`". S:310 (the pass-6b fold): "*Row 9* takes `Committed` to any objective, ranked committed to this objective, then other committed, then returning". P:339 (F7 pass-6b step): "row 9 takes `Committed` to any objective". S:339 describes `SelectMass`'s order as "the chosen candidate's committed formations first ... then the returning formations ..., then the rest by army" and has no "other committed" rank.
- Severity: major
- Impact: F6a writes `OwnershipRowsTakeOnlyFromTheirListedClaimants` from the table. Its row-9 case then rejects a corps committed to the old objective. P:42 says F7 re-runs that test "without editing" it, but F7's `TheSwitchReviewTakesTheOldMassIntoTheNew` needs the opposite result. So the merge seam breaks on the switch review, which is exactly pass-6b GAP-07's case.
- Assumption: the paragraph is the rule. The cell reads "`Committed` (to any objective; ranked committed to this objective, then other committed)", and S:339's order gains the "other committed" rank before the returning ones. The need's stop applies to the other committed formations and everything after them.
- Grounding: none (S's own text)

**GAP-02. On the review a dispatched corps gives up on its outbound march, row 12 sends it home. The sortie is deleted only after that.** S:339: "an outbound give-up (a `GaveUpMove` with the sortie's objective) deletes the sortie, so the formation holds in place (P8's `Hold gave-up`)". S:367: the plan deletes sorties "once per review after `Decide` (delete on arrival, give-up, ...)". Row 4 is read "against `PlannerState` as it stands" (S:310). Here is what happens on the review after the give-up is judged:
  - The formation is `Committed` (its sortie still stands, and O is current).
  - `MassFor` drops it from O's capacity through `GaveUpOn` (TheaterPlanner.cs:988, 1124-1131).
  - Row 12 turns "the `Committed` formations the mass did not take" into `Returning` (S:325), so G4 gives it `Redeploy return-home` with the origin as target.
  - G4's `Hold gave-up` fallback matches only a role target equal to the given-up target (ForceAllocator.cs:394-401, 411-419). The origin is not that target, so the fallback does not fire, and the mover issues the march home.
  - The sortie is deleted afterwards, but the issued march continues.

  `ClaimTable.Build` takes `gaveUp` (S:310), but no row reads it except the covering pool.
- Severity: major
- Impact: `AnOutboundGiveUpHoldsInPlace` and `GaveUpSortieHoldsInPlace` (T:47) fail, or a builder picks an order silently. A corps that cannot reach the objective walks home instead of holding.
- Assumption: row 4 (and row 5) skips a formation with a non-lapsed `GaveUpMove` whose `ObjectiveId` is the sortie's objective. That formation's claim stays `None`, and the after-`Decide` step deletes the sortie the same review. For a builder's sortie (objective -1), only a give-up at the depot site counts as outbound. Otherwise every `-1` give-up, a Recover or covering one included, would delete it.
- Grounding: shipped code

**GAP-03. The mover writes a new sortie under the snapshot's unit key. That key is stale when the same day's appointment re-keyed the formation.** `ReviewExecute` runs `CommanderAppointmentRuntime.Execute` before `CampaignMoverRuntime.Execute` (CampaignPlannerRuntime.cs:185-202), and both use the snapshot `ReviewPlan` built. A seated commander calls `CampaignPlannerRuntime.RekeyDepartment` -> `PlannerState.RekeyInstance` (CommanderAppointmentRuntime.cs:274-277, CampaignPlannerRuntime.cs:270-275). That updates `_keyOfInstance` and moves the existing keys (S:341 adds sorties, in-flight records and covering keys). The mover then records the day's first sortie under `f.UnitKey` from the Plan-job snapshot, which is the old key (S:339 "unit key -> objective id"). At save time, `RekeyFromLive` finds `_keyOfInstance` already at the new key, so it does nothing (PlannerState.cs:113-117). The next review's `Observe` sees the new key, so the sortie under the old key is never read again and is pruned after 10 reviews.
- Severity: major
- Impact: the appointment planner seats commanders on the very armies the main effort uses. On a day when one is seated and dispatched, the corps gets no commitment through its readiness dip and no trip home. RC4 returns for that corps. `AnAppointmentRekeysTheSortie` only covers re-keying a sortie that already exists.
- Assumption: the mover resolves the key through `PlannerState`'s instance map (a `KeyOf(instanceId)` reader over `_keyOfInstance`, falling back to `f.UnitKey`), and the same rule applies to every key the Execute job writes.
- Grounding: shipped code

**GAP-04. `HoldFacing` "reads a body the pass chose by its `SectorId` and `Share`, whatever its claimant". Counted-anyway bodies carry the same two fields, so a literal build changes D8's flag-alone need.** S:321 (row 8). S:310 gives every claim, `None` included, the counted sector and share, "-1 and 0 when counted nowhere". So a busy, unready or map-group formation that the hold counted anyway also has `SectorId >= 0`. Shipped `HoldFacing` counts reserve bodies only (`c.HoldReserved[i] >= 0`, TheaterPlanner.cs:792), and its sum lowers a flag-alone secession post's `Need` (890-891). The `Claim` struct (P:275) has no field that marks a chosen body. Pass-6b GAP-03's assumption named the rule ("a formation claimed `Hold`, or a `PinnedBuilder` the hold pass took as a reserve body"), but S kept "whatever its claimant".
- Severity: major
- Impact: a builder who reads every claim with `SectorId >= 0` raises `HoldFacing` by the counted-anyway strength. A flag-alone post's `Need` then falls and its `Limit` and pledges change. `F6aReproducesTheShippedRoles` breaks on any fixture with a Union secession post and a busy or unready formation in the facing sector. `HoldFacingReadsTheChosenBody` cannot be written from S alone.
- Assumption: a chosen body is a claim with `SectorId >= 0` whose claimant is `Hold`, or `PinnedBuilder` that `MassEligible` admits (the counted-anyway pinned builder is the one it refuses). `HoldFacing` runs inside `Evaluate` after rows 1-8, before row 9, as shipped `Garrisons` does inside `Reserve`. `ClaimTable` may also keep an internal reserved mask that is not published.
- Grounding: shipped code

**GAP-05. A Marching task's builder has no row. Row 8's candidate set admits it, so the hold can take the incumbent that requirement-depot-builder keeps.** `CollectHeldBuilders` lists Marching tasks with `Wished = false` (DepotTaskLedger.cs:292-305). Row 1 takes only Wished builders (`ReserveRules.WishedBuilders`, ReserveRules.cs:147-160). Row 8's candidates are "`MassEligible` claimed `None` or `PinnedBuilder`" (P:270, S:321), and a ready Marching builder is a `None` claim. Shipped `Hold` has no `IsHeldBuilder` test either (ReserveRules.cs:206-220). Row 9 excludes it ("Never ... a Marching held builder", S:322), and row 8 says nothing. S:345 has the incumbent "keep its pick while it is `Movable`", but G4's depot step runs over `Free` (not `_done`) formations (ForceAllocator.cs:597-600, 639). A reserved builder is `Hold`, so `BuilderAssignedAt` finds no builder at the site, and the ledger re-binds or abandons the task (`builder-retasked`, DepotTaskLedger.cs:382-400).
- Severity: major
- Impact: a ready builder marching to its site through a sector with a hold need is reserved, loses its task and stops being a builder. That is the re-binding S:345's incumbent rule exists to stop. F9's rule holds only while the builder is unready, because then `MassEligible` refuses it and the hold counts it anyway. `AMarchingBuilderKeepsItsTaskThroughAReadinessDip` passes and the ready case fails.
- Assumption: row 8 skips `IsHeldBuilder` formations as candidates (as row 9 does) and counts a Marching builder nowhere, since it is leaving for its site as a surplus. The claim stays `None` so G4's incumbent step sees it. The fixture names this as the one intended role difference from the pre-F6a build, or F9 is amended to the same rule in phase 1.
- Grounding: shipped code

**GAP-06. The department donor (row 11) and the covering pick can take a G8 garrison-post incumbent. G8's ordinary posts do not exist yet when `Plan` runs.** S:335: the donor is a formation "that no reserve, mass, garrison post, covering force or held builder holds". `GarrisonRuntime.PlanPosts(snapshot, plan)` runs after `TheaterPlanner.Plan` (CampaignPlannerRuntime.cs, right after the `campaign.planner.plan` scope), and its incumbents come from `GarrisonLedgers` (GarrisonPlanner.cs:33-58). Inside `Plan`, `TheaterPlan.Garrisons` holds only D8's secession posts. So row 11 picks a seated G8 garrison (a `None` claim), and G4 assigns `Redeploy` before `ClaimPostIncumbents` can seat it (S:327 order). The covering pick has the same problem. In shipped code a post may retake a hold-reserve incumbent in its own sector (`PostCandidate`, ForceAllocator.cs:576-577), but a `covering-force` member fails that `ReferenceEquals` test, so the post loses its incumbent to the force. S names only the reverse direction ("the garrison posts ... never take a covering member").
- Severity: major
- Impact: `NeverAReserveMassCoveringGarrisonOrBuilder` cannot pass on a fixture with a G8 incumbent. A short West takes the garrison of an East depot or fort, and the post is refilled from elsewhere the next review.
- Assumption: `Plan` receives the garrison ledger's live incumbents (`GarrisonLedgers.For(alliance).Posts`, the records `PlanPosts` would retain). Row 11's donor pool skips them. The covering pick may take one, and the post then refills, which is accepted and named.
- Grounding: shipped code

**GAP-07. S does not say how a recorded-sector body that is not a covering member this review is claimed and counted.** S:303: an incumbent "is a reserve body of its **recorded** sector wherever on the axis it stands, counted there before any other body, and counted in no other sector". Members are then taken "until their counted strength reaches `CoveringNeed`". The non-massable formations at the post count first, so the pick can stop before an incumbent. A formation that leaves the force "counts by position again", but the `CoveringKeys` write lands after `Decide` (S:304), so for one review it is still counted in its recorded sector. Four cases are left open:
  - (a) an incumbent counted in its recorded sector A while standing in B, not reselected as a member. Its reason is `hold-reserve`, and G4's `Counted`, `WithholdHoldPrefixes` and `ListShortfalls` count a `hold-reserve` body only where it stands (ForceAllocator.cs:995-1001, 1048, 1087). G3's `SectorHeld[A]` counts it and G4 does not, which is the disagreement F6a exists to end.
  - (b) an incumbent whose recorded sector has no need or is no longer an axis sector. `Hold` skips a sector whose need is not above 0 (ReserveRules.cs:203), and "counted in no other sector" then leaves it counted nowhere.
  - (c) "wherever on the axis it stands": no rule covers an incumbent that stands off the axis band.
  - (d) on the review the main effort targets the enemy capital (known only after `Decide`), "the members the mass did not take become ordinary hold reserves of their sector". No step is named that rewrites their claim reason after row 9. "Their sector" is not defined either: the recorded or the standing one.
- Severity: major
- Impact: `AnIncumbentCountsInItsRecordedSectorWhereverItStands`, `EnemyCapitalPushMakesTheLeftBehindOrdinaryHoldReserves` and `G4CountsAMemberInItsRecordedSector` each need a rule. A false shortfall in A reaches `[Reserves] hold=`, `Allocation.Shortfalls` and G5b routing.
- Assumption: only a reselected member is counted in its recorded sector. An incumbent the pick stops before, or one whose recorded sector is not an axis sector with a need, or one off the R-band of the axis, is a candidate of the sector it stands in like any other body. On the enemy-capital day, a step after row 9 (in `Plan`, before `PostFromOutside`) rewrites each remaining member to `Hold hold-reserve` with `SectorId` = the sector it stands in. Its `Share` stays where the hold pass put it, and the mismatch is accepted for that one review and pinned.
- Grounding: shipped code

**GAP-08. A committed D6 reinforcement goes through `SelectMass`'s natives-first foreign branch.** S:339: "`SelectMass` takes the chosen candidate's committed formations first ... whatever the need" (S:310). Shipped `SelectMass` excludes `c.Foreign` from the natives. It adds the foreign pick only `if (total < need)` and only there sets `plan.ReinforcementInstanceId` (TheaterPlanner.cs:1058-1060, 1089-1094). G4 assigns `Support reinforcement` only to that id (ForceAllocator.cs:965-967). The locked reinforcement reaches the capacity only through `ReinforcementPick`'s foreign list (1017-1038), which still applies the 10-day test. S does not say whether "committed first" covers the foreign pick.
- Severity: major
- Impact: once the need shrinks so that the natives meet it, the dispatched reinforcement is left out. Row 12 then makes it `Returning`, and its first `Returning` claim transfers it back to the donor department. It is sent home in the middle of the push, which `ADispatchedMemberStaysMassedThroughAReadinessDip` and `TheReinforcementHasASortie` do not cover.
- Assumption: a committed locked reinforcement is taken first with the other committed formations whatever the need. `plan.ReinforcementInstanceId` is set whenever it is in the mass, and the 10-day test applies only to a new pick.
- Grounding: shipped code

**GAP-09. The order of the after-`Decide` writes is not stated.** P:359's `PipelineOrderPinned` fixes `Evaluate -> Decide -> SelectMass -> PostFromOutside -> DepartmentReinforcement -> the return moves -> AssignPostures`. S:367 has the plan write "once per review after `Decide`". Neither says where these steps fall relative to that order:
  - (a) clearing an in-flight record (arrival, give-up, role change) before or after row 11's ranking. That decides whether a department whose record ended today can be picked today and whether it shows `inflight`.
  - (b) deleting sorties.
  - (c) the covering state writes and the GAP-07 (d) rewrite.
  - (d) S:367's "delete on arrival" applies to a committed formation still within `max(BugleRange, ArrivalRadius)` of its origin, for example a staged gather near home. That would drop the commitment the day after dispatch.
- Severity: minor
- Impact: one review's difference in each case.
- Assumption: record and sortie ends are applied right after `Decide`, before `SelectMass`, so a department freed today may be picked today. Arrival deletes only a sortie that is not committed. The covering writes come last. The pin lists these steps.
- Grounding: shipped code

**GAP-10. `TestClaims` does not cover real plans allocated with inputs the plan never saw, and the routing point is not named.** S:310/P:285 route "every harness file that hand-builds a plan". Three cases fall outside that rule or its list:
  - `ReconNeverTasksTests.Review` (484-492) builds a real `TheaterPlanner.Plan` without `pinned` and allocates with `pinned` (used at 1361). Its claims carry no `PinnedBuilder`, so "2 builds the pinned depot" fails.
  - `GarrisonPlannerTests.cs:350-356` and `PlannerSidecarTests.cs:599-600` pass `pinned`/`held` to `Allocate` over `HandPlan`. The helper's signature `For(plan, s, sectors, held)` needs that list at the call, so it cannot run inside `HandPlan`/`ManualPlan`. `AlwaysWithheldRoles` also sets `CapitalFloorStrength` after `ManualPlan` (ForceAllocatorTests.cs:470-471).
  - `DailyReviewSchedulerTests.cs:532-548` (a random hand-built floor, need and mass) and `PlannerSidecarTests.cs` are not in P:285's list.

  A leftover `Committed` claim in a hand-built table has no `RoleOf` row (S:317: "via row 9 ..., else row 12"). F7/F8 hand-built claims also need `state` and `gaveUp`, which the helper does not take. The 06-23 roster's "sidecar move records" are in the Windows save folder, not the repo.
- Severity: minor
- Impact: mechanical, but each red test needs a fixture choice.
- Assumption: `TestClaims.For(plan, s, sectors, held, gaveUp = null, state = null)` runs inside each `Alloc`/`Allocate` helper at call time. A real-plan fixture that allocates with `held` passes the same list to `Plan`. `RoleOf` maps a leftover `Committed` to `Hold` in place, reason `garrison`, and a test pins that. The roster file is copied into `tests/` as fixture data with its source path named.
- Grounding: shipped code

**GAP-11. The row seams and their owners are not stated.** `ClaimTable.Build` produces rows 1-8 in one call (S:310). F6a's `TheHoldNeverCountsOrReservesCommittedReturningOrInFlight` and `CapitalMayTakeCommittedReturningOrInFlight` hand-set rows 4-6 (P:284). Only `ReserveRules.Hold` gets a claims signature (P:270). `ReserveRules.Capital`'s signature over claims is not given, and shipped `Capital` has an `excluded` mask (ReserveRules.cs:164). `ClaimTable.cs` is created in F6a (P:285), but F6b (row 8's covering input), F7 (rows 4, 5) and F8 (row 6) do not list it in their owned files (P:294, 332, 348).
- Severity: minor
- Impact: one obvious answer each.
- Assumption: `ReserveRules.Capital(s, floor, claims)` writes `Capital` claims over `MassEligible` formations claimed `None`, `Committed`, `Returning` or `DepartmentMove`. The tests call `Capital` and `Hold` with hand-built claims. `ClaimTable.cs` joins F6b, F7 and F8's owned files.
- Grounding: shipped code

**GAP-12. The covering count rule leaves out `WithholdHoldPrefixes`.** S:305: "`Counted`, `CountedHold`, `HoldLeaving` and the shortfall lists count a `covering-force` member in its role sector wherever it stands". Pass-6b GAP-11 also named `WithholdHoldPrefixes` (ForceAllocator.cs:1048, the same standing-equals-role-sector test). As it stands, a member at a post in B with recorded sector A is left out of A's prefix sum, so more of A's `hold-reserve` bodies are withheld from vanilla's offense than A's target needs.
- Severity: minor
- Impact: conservative over-withholding in an axis sector.
- Assumption: `WithholdHoldPrefixes` counts a `covering-force` member in its role sector toward the target, as `Counted` does. It is already withheld.
- Grounding: shipped code

**GAP-13. S:367 still keys sorties by decision.** S:367: "create, at first dispatch of a decision; origin never overwritten within that decision". S:339 (the pass-6b fold): "Objective, not `DecisionSerial`, is the key ... Sorties are keyed by objective throughout and keep their origin while they exist".
- Severity: minor
- Impact: one obvious answer (S:339).
- Assumption: S:367 reads "at first dispatch toward an objective; origin never overwritten while the sortie exists".
- Grounding: none

**GAP-14. `AxisShares`' interface lacks the probe, and "its believed threat" is ambiguous between the overlapping sum and the Live element assignment.** S:303: "`AxisShares` reads `EnemyPicture.Elements` and their assignment (F3) and the probe". P:314's signature `AxisShares(s, sectors, picture, holdNeed, holdRatio, into)` has no probe and no `Crossings` for the `min(1, reach / EffectiveDistance)` weights. S:302 counts "its believed threat" (`LandThreatNear`, which overlaps between sectors, S:283) and also says each land element is assigned to one sector (S:278, the `Live` kind included for the department need). With the overlapping sum, one seen army near two axis sectors counts in both shares, which raises the cap `sum AxisShare`.
- Severity: minor
- Impact: the cap is looser. No sector is stripped, because each sector still gives at most its own share.
- Assumption: the signature adds `ITerrainLineProbe probe`. The believed part is the overlapping `LandThreatNear` restricted to beliefs within R of the axis, as S:302 says ("its believed threat"), and the overlap is named.
- Grounding: shipped code

**GAP-15. What a flag turned off still writes is not stated.** S:310: "a flag turned off keeps its state (sorties, records, keys) and ignores it". P:53: `Enable Main Effort Commitment` off means "no sorties". It is open whether the mover still records sorties with the flag off, and whether arrival, give-up, prune and rekey still delete or move them. If nothing is maintained, turning the flag back on sends corps to origins from weeks before.
- Severity: minor
- Impact: one review of stale returns after the flag is turned back on.
- Assumption: with a flag off, nothing new is created (no sorties, picks or members). Bookkeeping that ends or moves existing state still runs: prune, rekey, arrival, give-up and record ends.
- Grounding: none

**GAP-16. The readiness skip's `EnemyHeld` is not a superset of vanilla's refusal, and `Redeploy` has no skip.** S:340 says the skip "refuses a little more than vanilla does". Vanilla's `SetWaypoint` asks `IsOnEnemySoil(false, target, returntrueifnotonstate: true, usestateoccupation: true)` (91336-91343; 139304-139336). For a point on no state, that returns true, so vanilla refuses. The mover's host looks the state up with `usecloseststate: true` and gives such a point its closest state's alliance (CampaignMoverRuntime.cs:101-106), so the mover issues the move. The occupation clause runs the other way, and there the mover refuses more. A `Redeploy return-home` to an origin recorded on enemy soil (a staged member's start line) at step 0 or 1 has no skip, so it gets the Failed -> GaveUp chain.
- Severity: minor
- Impact: rare give-ups in the RC4 pattern.
- Assumption: the skip uses the same lookup as vanilla (no closest-state fallback, a point on no state counts as enemy) and applies to `Redeploy` too. The S:340 sentence is corrected.
- Grounding: vanilla

**GAP-17. Publication and census details.**
  - (a) Where `[Allocation]`'s `redeploy=` and `covering=` go (S:331). `Summary` is the dedupe signature, and `AllocationSummaryNamesTheRoles` pins the whole string (ForceAllocatorTests.cs:1722), but no slice lists that re-pin.
  - (b) S:327 names `CampaignPlannerRuntime` as a reader of the table. The `[Reserves]` counts are built in `ReservesRow.cs` (S:360), which is the file the census sees.
  - (c) `TheaterPlan.DepartmentShort`'s element type has no name. It is missing from P:31's published list and from F10b's `PublishedTypes` (P:359).
  - (d) P:34's sidecar list omits `reinforcementFromTheater` (P:339), and no dedicated restore method is named for it (P:34 forbids lengthening the positional `Restore`).
  - (e) `TheaterPlan.Returns` (P:332) duplicates the `Returning` claims and names no reader.
- Severity: minor
- Impact: naming only.
- Assumption:
  - (a) Append after `busy=`, before `player=`, and re-pin in F6a.
  - (b) The census names `ReservesRow` and `ForceAllocator`.
  - (c) `DepartmentShortfall { Theater, Need, Have, Status }`, added to both lists.
  - (d) `reinforcementFromTheater` beside `reinforcementKey`, restored through `RestoreSorties`.
  - (e) Delete `Returns` and read the claims.
- Grounding: shipped code

**GAP-18. The `Kind` of a single covering post is not stated.** S:301: "When both points give the same town, or only one point gives a town, there is one post and no verdict hysteresis". `CoveringKind` is `Forward` or `Rear` (S:306), and `[CoveringForce] kind=forward|rear`. S:302's example says "the verdict is rear (which in 002/A is the same post)", which implies the verdict is still computed.
- Severity: minor
- Impact: what the row shows, and whether `verdictDays` moves.
- Assumption: with one post, `Kind` is the raw verdict each review (forward when the counted strength reaches the need, else rear), the count stays 0, and `CoveringForward` is written raw.
- Grounding: none

## Pass-6b folds checked

- GAP-01 (claims for hand-built plans; `F6aReproducesTheShippedRoles` oracle): adequate for hand-built plans (`ClaimTable.Build`, `TestClaims.For`, frozen literals, S:310, P:285). The routing point, the unlisted files and real plans allocated with `held` are new: GAP-10.
- GAP-02 (busy pinned builder): adequate. Row 2 takes `PinnedBuilder` (S:315, P:285).
- GAP-03 (counted-anyway set; `HoldFacing`): adequate for the counted-anyway set (S:321). Inadequate for `HoldFacing` ("whatever its claimant"): GAP-04.
- GAP-04 (`RoleOf` outputs; role sector; Recover target): adequate (S:310, P:280, P:285).
- GAP-05 (Redeploy readers, `RearmPriority` throw): adequate (S:331, P:285).
- GAP-06 (`PostFromOutside` pledging committed, returning, in-flight): adequate (S:310 D8 sentence, P:339, P:352).
- GAP-07 (switch review, row 9): inadequate. The paragraph and P were changed, but the table cell and S:339's order were not: GAP-01.
- GAP-08 (committed taken whatever the need): adequate for natives (S:310). The foreign reinforcement is new: GAP-08.
- GAP-09 (give-up matching): adequate as a matching rule (S:339, P:339). The give-up review's own order is new: GAP-02.
- GAP-10 (donor theater at dispatch): adequate as a rule (S:339, P:339). The sidecar list and restore path: GAP-17 (d).
- GAP-11 (member counted in its recorded sector in G4): adequate for `Counted`, `CountedHold`, `HoldLeaving`, the shortfalls and `[Reserves]` (S:305, S:360). Inadequate for `WithholdHoldPrefixes`: GAP-12. Incumbents that are not members: GAP-07.
- GAP-12 (picture elements and assignment for `AxisShare`): adequate as a rule (S:278, S:303). The interface lacks the probe: GAP-14.
- GAP-13 (crossing body, non-massable first, `HoldStrength`): adequate (S:303 *Sums*, P:323).
- GAP-14 (`deptShort` carrier and statuses): adequate (S:335 `TheaterPlan.DepartmentShort`, P:352). The type name and `PublishedTypes`: GAP-17 (c).
- GAP-15 (capitals as posts, inclusivity, clamp): adequate (S:301, P:323).
- GAP-16 (flag carriers; state kept when off): adequate as a carrier (S:310). What still writes while off: GAP-15.
- GAP-17 (row-6 arrival; `Need` for the neediest sector): adequate (S:310 row 6, S:335, P:352).
- GAP-18 (a) first-transfer marker: adequate (S:341). (b) objective keying: inadequate, because S:367 still says decision: GAP-13. (c) the mover overwrites at its next dispatch: adequate (S:339).
- GAP-19 (a) pin over `Evaluate` and `Plan`: adequate (P:359). (b) `Ordinal` comparison: adequate (S:331). (c) Fort Monroe and Alexandria read from the save: adequate (P:323).

## Checked and clean

- Rows 1-3 against shipped G4:
  - A Recovers formation that is a pinned builder stays `DepotBuilder` (row 3 takes `None` only; shipped `AssignRecover` skips `_done`, ForceAllocator.cs:581-595).
  - Recovers formations are never `MassEligible`, so they are never Capital, hold reserve or mass (TheaterPlanner.cs:740-745, ReserveRules.cs:139-143).
- Row 7 against shipped `Capital`:
  - The `WishedBuilders` mask equals "takes from `None`" once row 1 has claimed the Wished builders (ReserveRules.cs:147-178, TheaterPlanner.cs:1253, ForceAllocator.cs:323).
  - A busy Wished builder is not `MassEligible` either way.
- Row 8 against shipped `Hold`: the candidates are `MassEligible && !capitalReserved`. The counted-anyway set is `(!MassEligible || capital) && CountsTowardCoverage && !Recovers && (!capital || capital sector)` (ReserveRules.cs:206-228). Restated over claims it is S:321's row 8, the map-group player included.
- G3 and G4 read the same hold inputs:
  - `SectorPlan.HoldNeed` is missing only for a sector with no own formation and no threat (TheaterPlanner.cs:705-708). There `Hold` reserves and counts nothing, so `SectorHeld` is 0 either way.
  - `NearX/Z` comes from the same `NearestLandBelief` from the centroid (TheaterPlanner.cs:1229-1231, ForceAllocator.cs:373-379).
- The hand-built mass and the claim order: shipped G4 lets the mass override a hold reserve of any sector (`AssignHoldReserve` skips `_inMass`, 472-477). Capital beats the mass (`AssignCapitalGuards` after `MarkMass`, 464-470, and `Assign` clears `_inMass`). S:327's "capital guards, hold reserves skipping the mass" keeps both for a `TestClaims` table that has no row 9.
- `PostFromOutside` restated over claims ("outside: `None` only, with the shipped tests") equals the shipped pool. A pinned or Marching builder fails `IsHeldBuilder`; Immobile and Recover fail `MassEligible`; the mass fails the mass test (TheaterPlanner.cs:849-850).
- G4's later steps are unchanged under claims:
  - `PostCandidate`, `IsHoldReserve` and `DrawFromHoldReserve` test `ReferenceEquals(..., ReasonHoldReserve)` (ForceAllocator.cs:576, 760, 818), so a `covering-force` member is never retaken, drawn or built from.
  - `SmallestIn` takes only `Free` formations (639).
  - `HoldLeaving` reads `ShareSector`/`Share` (796-806), which S:310 maps to `Claim.SectorId`/`Share`.
- The mover:
  - One `Priority` array drives both dispatch and `MovesRole` (CampaignMover.cs:115-123).
  - `marches` gates only `MainEffort` and `Support` (338-339), so `Redeploy` needs no tempo release.
  - `ReleaseRecoverFromOffense` (201-211) is the slot for de-listing `Redeploy`.
  - `Dispatch`'s order (W&L skip, `Movable` skip, waypoint-arrived return, tempo deferral, `ShouldIssue`, 325-346) fits `heldReadiness=`.
- `MoveTracker`:
  - It keeps a give-up for `GaveUpExpiryDays` (14) and lists only planner-budget give-ups (MoveTracker.cs:53, 235-242), so give-up matching by origin or record target sees the record on later reviews.
  - `ShouldIssue` re-issues a `Failed` record when asked (83-98), so the readiness skip before it is the whole rule, as S:340 says.
- Vanilla:
  - `GetReadinessStep` (173418-173433) and the `SetWaypoint` readiness refusal (91336-91350) are as S:263 cites, apart from GAP-16's lookup.
  - `CollectHeldBuilders` marks Marching tasks `Wished = false` with the site as target (DepotTaskLedger.cs:292-305).
  - The ledger abandons or re-binds a task whose builder the allocation re-tasked (380-400).
- `CampaignWithholdView`:
  - `Read` already fetches the plan (179).
  - The shipped same-sector `Hold` exception is at 132, after the `!Withheld` test at 131, so the covering clause fits between them.
  - The offense-only clauses are at 117-119.
