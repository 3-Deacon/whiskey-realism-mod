# Stage G Strategic Foresight Implementation Plan

Produced-by: claude-opus-5-5 (high)

**Author seat:** worktree `.worktrees/plan-foresight`, branch `plan/strategic-foresight`, cut from `main` `2bfddd87`.

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development (recommended) or superpowers:executing-plans, task by task; superpowers:test-driven-development for every pure-logic change (red first, exit 134 on the first failing assertion). Steps use checkbox (`- [ ]`) syntax.

**Goal:** build spec amendment D11, "Strategic foresight amendments 2026-10-18 (D11)" in `docs/superpowers/specs/2026-10-14-campaign-planner-design.md` (binding, with its "Vanilla behaviour relied on" anchors, requirements, fit criteria and open questions). Each AI side plans from a fog-honest picture of the whole enemy army (beliefs, spotting marks, the public `troopsfielded`, anchors), keeps a covering force between the capitals, sizes its capital floor by march time, reinforces a short department, keeps a dispatched main effort together, brings stranded corps home, takes depot builders only from surpluses of ready formations, uses the scenario's live minimum strength, and gives vanilla's own interception back to vanilla (#25 telemetry-only while the planner runs).

**Architecture:** pure files under `src/WhiskeyRealism/Strategic/Planner/` (in the test csproj) plus their runtime readers (`*Runtime.cs`, not in the test csproj), as stage G. Everything is daily-review work: the Plan job (snapshot, detection, marks, beliefs, picture, plan, allocation) and the Execute job (mover). Patches read only the published plan (`CampaignPlanStore`); the only patch whose behaviour changes is #25, and #127's defence clause changes inside `CampaignWithholdView` (pure).

**Tech stack:** C# netstandard2.1, BepInEx 5.4 + HarmonyX, Unity 2021.3.16f1 Mono x64. Console harness `tests/WhiskeyRealism.Tests` (net8.0; `internal static class *Tests { public static void Run() }`).

**Inputs (read before your slice):** D11 (all of it) and the campaign-planner spec's "Vanilla and W&L model"; `docs/superpowers/plans/2026-10-15-campaign-planner.md` § Global Constraints (binding here too) and the as-built sections of the slices you touch (G3, G4, G5a, G5c, G8, the smoke fixes); root `AGENTS.md`, `src/WhiskeyRealism/{Strategic,Patches}/AGENTS.md`, `tests/WhiskeyRealism.Tests/AGENTS.md`; the investigation `docs/loop/foresight/investigation.md` and decisions `docs/loop/foresight/decisions.md` (repository copies of the session's working files); the test plan `docs/loop/foresight/test-plan.md` (frozen at the gate). Decompile `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` (build 14703111; regenerate per `docs/findings.md` if `/tmp` was wiped).

## Outline

1. Gates and sequencing (the concurrent branch, the ordinals, the two phases).
2. Flags.
3. **Phase 1** (F1, F2, F3, F4, F4b, F5, F9, F10a): the picture, the capital threat, #25's restore, the marks, the live minimum, the depot builder rule. Built on `feat/strategic-foresight`.
4. **Phase 2** (F6a, F6b, F7, F8, F10b): the ownership table first, then the covering force, commitment and return home, department reinforcement. Not built until its own dry run logs no blocker and no major.
5. Each slice: why, owned files, vanilla to read, interfaces, steps (tests first), verification, smoke rows, rollback, prove-mode plants.
6. Smoke checklists (per phase) and coverage (requirement -> slice).

## Global constraints (in addition to the campaign-planner plan's)

- **Gates before each commit:** `./build.sh` 0 warnings; the test build 0 warnings; `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` exit 0 and its PASS count; `python3 -I tools/config-audit.py .` 0 findings; `python3 -I tools/orphan-sweep.py .` 0 (and `--include-test-only` adds nothing this plan added); `python3 -I tools/test_smoke_check.py` OK. `git log --oneline -10 && git status` before each commit. Messages `<type>(foresight): <summary> [F<n>]`, then the decompile anchors, then `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Changed semantics are re-pinned, never dropped.** A test that pins a rule D11 supersedes (D1's `max`, D2's `S` term and per-sector switch, P7's floor without a baseline, the `MinGarrisonStrength` const, the nearest-belief hold release) is rewritten in the same commit to pin the new rule and named in the commit message. Find them with `grep -ln "PresumedAt\|HoldThreatAtTarget\|MinGarrisonStrength\|CapitalFloorStrength\|HoldNeedAt" tests/WhiskeyRealism.Tests/*.cs` at the start of the slice.
- **Census.** Every new contract member names its reader file in `PlannerReadCensusTests` (`CensusReceiverTypes` says what the census can resolve); delete the member or name its reader, never allow-list.
- **Patches read published state.** New published members (phase 1: `TheaterPlan.MinGarrisonStrength`, `CapitalReach`, `CapitalWeighted`, `CapitalThreat01`; phase 2: the rest): `TheaterPlan.Claims`, `TheaterPlan.SectorHeld`, `TheaterPlan.Covering`, `TheaterPlan.MinGarrisonStrength`, `TheaterPlan.CapitalReach`, `TheaterPlan.Returns`, `TheaterPlan.DepartmentMoves`. `PlannerPatchesReadOnlyPublishedState` must stay green.
- **Randomness.** The AI-side mark channel draws from `System.Random` seeded per D11 (requirement-marks-ai-side); no slice calls `UnityEngine.Random`.
- **W&L.** `WlChainRules.MayMove` for every new move (covering, department, return home); `CountsTowardCoverage` for every new sum; a CIC side's allocation stays empty (`ForceAllocator.StandsDown`).
- **Sidecar.** Format v3, additive fields only (an older file reads defaults), all inside the faction's `planner` node (`FactionDto.planner`, `PlannerStateDto`): `beliefs[].baseRadius` (`EnemyBeliefDto`); `sorties: [{unitKey, objectiveId, role, fromTheater, originX, originZ, missedReviews}]` (`fromTheater` empty except for D6's reinforcement); `departmentInFlight: [{to, unitKey, from, pickDay, targetX, targetZ}]` (theaters by name, as `DepartmentDto`); `coveringKeys: [{unitKey, sectorId}]` (the axis sector each member was first claimed in), `coveringForward`, `coveringVerdictDays`, `coveringVerdictKnown`, `coveringPostKey`. New state is restored through dedicated `PlannerState.RestoreSorties`, `RestoreDepartmentInFlight`, `RestoreCovering` (as `RestoreDepartments`), never by lengthening the positional `Restore`; a record with an unknown role or theater name is skipped. `PersistenceDto.cs` and `SidecarDtoCodec.cs` are shared files: each slice edits only its own fields.

## Gates and sequencing

- **`fix/recon-unfilled-reason` is merged** (`main` `50c8f0d0`, in `dbdd4de4`; this branch merged `main` `4e34a6b3`). It owns, and this plan builds on unchanged: `ForceAllocator.AssignRecon`, `DrawFromHoldReserve`, `FreeScouts`/`WhyUnfilled`/`RoleTag`/`UnfilledWhy`, `Allocation.ReconUnfilledWhy`, `ReconFree`, `ReconMass` (the `[Recon]` row's `why=`, `free=`, `mass=`), `MoveTracker.TryGetMove`, `CampaignMover.MoverTally.MissRows` and the `[CampaignMoverMiss]` row, and the quoted-name convention (`TelemetryLegacyParser.Quote`). The foresight build branch `feat/strategic-foresight` is cut from `main` at or after `dbdd4de4`, with this branch merged into it first. The regions this plan adds next to that branch's: `ForceAllocator.Run` (F6a replaces its `ReserveRules.Capital`/`Hold` re-run by a read of `TheaterPlan.Claims`; the claimed roles, `Redeploy` among them, are assigned there, before `AssignDepotBuilders`), `SectorMayGiveScout` (the live minimum and the D11 belief-driven test), `CampaignMover.MoverTally` (`IssueRows`, `HeldReadiness` next to `MissRows`), `TelemetryTagPolicy` (one line per new tag).
- **Patch ordinals.** This plan adds **no Harmony patch unless F4b is built** (#25 keeps its number and changes behaviour; #127's change is inside `CampaignWithholdView`); F4b's patch carries the working label `FS-F4b-1`. A slice that finds it needs a patch gives it a working label (`FS-<slice>-<n>`, e.g. `FS-F4-1`) in its commits, docs and tests, as stage H's plan does, and the integrator assigns the real ordinal at the merge (next free on `main` is **#134**: #133 is `PlayerUnitRepathObserverPatch`, `docs/patch-catalog.md` § Conventions). Stage H's plan (branch `plan/stage-h-naval`) uses working labels too; neither plan reserves a number.
- **Two phases.** Pass 5 of the build-from-spec dry run (`docs/loop/foresight/gaps-pass5a.md`, `gaps-pass5b.md`) found 20 gaps in F1/F2/F4/F5 (13 major, all with grounded answers, folded) and 20 in F6a-F9 (2 blocker, 15 major) whose recurring cause was ownership: who holds a formation when the hold, the covering force, commitment and the department transfer all want it. The plan therefore ships in two phases.
- **Phase 1 order.** F1 (telemetry) -> F2 (#25) and F3 (picture) in parallel -> F4 (marks, after F3) -> **user gate: `question-capital-share` and `question-intel-bug`, on the smoke after F4** -> F4b (BUG-INTEL-001 fix, only if the user picks (b)) -> F5 (live minimum, after F3; decision 6) -> F9 (depot builders) -> F10a (integration, docs, smoke). F2 is disjoint from F3 (only `Plugin.cs` and `TelemetryTagPolicy.cs` are shared, one line each). **Precondition for opening `feat/strategic-foresight`:** dry-run pass 6a over F1, F2, F3, F4, F4b, F5 and F9 logs no blocker and no major. F9 is in phase 1 because it reads only shipped claims (`MassEligible`, the hold tally, `Free`); its covering clause is vacuous until phase 2 (a covering member is a hold reserve, which `Free` already refuses).
- **Phase 2 order.** F6a (the ownership table, `RoleOf`, the Redeploy role and withhold clauses; settled before anything else) -> F6b (covering force) -> F7 (commitment and return home) -> F8 (department reinforcement) -> F10b (integration, docs, smoke). Built on `feat/strategic-foresight-p2`, cut from `main` after phase 1 merges. F6a-F8 each edit `ForceAllocator.cs`, `TheaterPlanner.cs` and `CampaignPlannerContracts.cs` and are sequential. **Merge seam F6a/F6b:** F6a lands D11's whole ownership table (requirement-claims: every claimant, the order, the *takes from* sets, the counted-in rule) and `ForceAllocator.RoleOf` with every row, `Returning` and `DepartmentMove` included; the producers of rows 4-6, 11 and 12 and the covering members are empty in F6a. F6b, F7 and F8 each fill their producers and re-run F6a's table tests (`OwnershipRowsTakeOnlyFromTheirListedClaimants`, `OneOwnerAtTheEndOfPlan`, `G3AndG4ReadOneOwnershipTable`) and F6b's pool tests (`CoveringNeverStripsAnotherSectorsHold`, `HeldBuildersAndGiveUpsNotInThePool`, `MassForExcludesMembersExceptAgainstTheEnemyCapital`) against their own claimants, without editing them. **Precondition for opening `feat/strategic-foresight-p2`:** a dry run over F6a, F6b, F7 and F8 that logs no blocker and no major.

## Flags (all default on, truthful descriptions, section "Campaign Planner", bound in the slice that first reads them)

| Flag | Slice | Off means |
|---|---|---|
| `Enable Vanilla Defensive Interception` | F2 (phase 1) | #25 filters and reverts as before D11 |
| `Enable Strategic Foresight Picture` | F3 (phase 1) | D2's presumption, P7's floor and the 200-radius capital threat, the nearest-belief hold release, D6's sum of `SectorFacts.Need` as the department need, D9's old belief-driven test, and `TheaterPlan.MinGarrisonStrength` = `MinStrengthFallback` (5000), so F5's live minimum never sits under the old presumption (decision 6), as before D11 (the eligibility floor stays: it does not read the picture) |
| `Enable Spotting Mark Beliefs` | F4 (phase 1) | no mark detections on either side |
| `Fix Spotting Mark Timing` | F4b (phase 1, only if the user picks (b) of `question-intel-bug`) | vanilla's mark timing (BUG-INTEL-001) on both sides |
| `Enable Covering Force` | F6b (phase 2) | no covering force: the axis sectors hold where they stand, as other sectors |
| `Enable Main Effort Commitment` | F7 (phase 2) | D5's readiness rule for every mass member, no sorties, no return home |
| `Enable Department Reinforcement` | F8 (phase 2) | no department transfer |

The live minimum strength (F5) and the depot builder rule (F9) are corrections of the allocator's rules and have no flag of their own; `Enable Campaign Planner` is their rollback, as for every planner rule. `python3 -I tools/config-audit.py .` must stay at 0 findings (OFF, UNREAD, DUPLICATE, DESC).

---

## Phase 1 (build-ready after dry-run pass 6a)

## F1 — Telemetry the investigation could not read (requirement-telemetry, first half)

**Why:** the investigation reconstructed causes 1 and 3 from code and geometry because no row named the moved formation, its target, the reserve flags or #25's reasons. Every later slice's smoke reads these rows; they ship first, with no behaviour change.

**Owned files:** `Strategic/Planner/CampaignMover.cs` (`MoverTally.IssueRows`, `IssueRow`), `Strategic/Planner/CampaignMoverRuntime.cs` (emit), `Strategic/Planner/TheaterPlanner.cs` (`Trace`: `@<x>,<z>` per candidate), create `Strategic/Planner/ReservesRow.cs` (pure: the `[Reserves]` row from snapshot, plan, allocation), `Strategic/Planner/CampaignPlannerRuntime.cs` (emit `[Reserves]` every review), create `Strategic/DefenseFilterTally.cs` (pure: reasons tally), `Strategic/ReviewCadenceCatchUp.cs` (`DateOf(serial, out y, out m, out d)`, the inverse of `DaySerial`, and `FormatDate`), `Strategic/Planner/TheaterPlanner.cs` (`Summary`: `date=` after the signature compare), `Strategic/Planner/ForceAllocator.cs` (`Summary`: `date=`; extract `internal static RoleStrength` and `CountedHold` from the pass for `ReservesRow`; `ReasonPinnedBuilder = "depot-task-pinned"` for `AssignPinnedBuilders`), `Strategic/Planner/EnemyDetectionRuntime.cs` (`[EnemyBeliefs]` `date=`), the capture of yesterday's allocation and plan in `CampaignPlannerRuntime.ReviewPlan` before `CampaignPlanStore.Publish`, the re-pinned `ConstructionSteeringTests.AllocatorAndPlannerWiringPinsTheWishedBuilders` (its pinned-builder line names the new reason), `Patches/CheckForDefensiveOperationsCandidateFilterPatch.cs` (`EmitDefenseIntentFilter` carries `reasons=`), `Telemetry/TelemetryTagPolicy.cs` (`CampaignMoverIssue`, `Reserves`), test `tests/WhiskeyRealism.Tests/ForesightTelemetryTests.cs`, csproj lines for the two pure files.

**Interfaces:**
```csharp
// CampaignMover.MoverTally
public readonly List<string> IssueRows = new List<string>();   // one per issued move, at most MoveTracker.MaxMovesPerDay a review
// "[CampaignMoverIssue] alliance=<a> date=<yyyy-mm-dd> unit=<key> role=<Role> reason=<reason> target=<x>,<z> objective=<id> from=<x>,<z> readiness=<step> pick=<new|pinned|->"
internal static class ReservesRow { internal static string Build(CampaignSnapshot s, SectorModel sectors, TheaterPlan plan, Allocation a, PlannerState state, Allocation previous, TheaterPlan previousPlan); }
// F1 shape: "[Reserves] alliance=<a> capital=<held>/<floor> hold=<sector:held/need,...>" (at most 6 sectors, largest need first);
// Owners of the later fields (D11 requirement-telemetry is the final shape): F2 enemyOnOwnSoil=, vanillaDefActive=; F6b covering=, holdReserve=,
// vanillaDefWithheld=, vanillaDefHoldOrCovering=; F7 committed=, returning=, returningUnits=, massInVanillaDef=; F8 deptShort= (with donor|nodonor|queued).
internal static class DefenseFilterTally { internal static string Reasons(IReadOnlyDictionary<int, string> reasonByUnitId, IEnumerable<int> ids); } // "reason:n,..." over ids, sorted by reason
// every place, post and unit name (and every joined list of them) in a D11 row goes through TelemetryLegacyParser.Quote, as [CampaignMoverMiss] does
```

**Steps:**
- [ ] Tests first (`ForesightTelemetryTests`): `IssueRowNamesUnitRoleTargetObjectiveAndOrigin`, `IssueRowsNeverExceedTheDailyCap`, `TraceCarriesEachCandidatesCoordinates`, `ReservesRowNamesCapitalAndHoldShares`, `DefenseFilterReasonsAreTalliedAndSorted`, `NewRowsAreRegisteredInTheTagPolicy` (tag and category), `RowNamesAreQuoted`, `ARefusedRetryWritesNoIssueRow`, `ReservesRowEveryReview`, `ReservesDefenceCountsReadYesterdaysAllocation` (a previous Reserve formation now `InDefensiveOps` gives `vanillaDefWithheld=1`; the capture is before `Publish`), `DateOfInvertsDaySerial` (month ends, leap years), `DatedRowsKeepTheirDedupe` (`[CampaignPlan]`, `[Allocation]`, `[EnemyBeliefs]`: the same plan on two days writes one row), `IssueRowPickIsPinnedNewOrDash`, `ReservesRowSharesTheAllocatorsCountedHold` (source pin: `ReservesRow` calls `ForceAllocator.CountedHold` and `RoleStrength`, defines no sum of its own).
- [ ] Implement; the mover adds an issue row in `Dispatch` only when the move was made (`moved`: outcome Issued or Retried; `MoveIssue` reports a refused retry as `Retried` with `moved == false`, which writes none). Every new row carries `date=` and the shipped `[CampaignPlan]`, `[Allocation]`, `[EnemyBeliefs]`, `[CampaignMover]`, `[CampaignMoverMiss]` rows gain it after their signature compare; F1 builds all of `[Reserves]`' defence counts (`vanillaDefWithheld`, `vanillaDefHoldOrCovering`, `massInVanillaDef`: everything they read exists today; F6b adds only the `covering-force` reason) except `enemyOnOwnSoil` and `vanillaDefActive`, which are F2's; register `CampaignMoverIssue` (Write) and `Reserves` (State); `[Reserves]` is emitted every review per alliance; the trace token is ` at=<x>,<z>`, never glued to `objective@sector`.
- [ ] Gates; commit `feat(foresight): mover issue rows, trace coordinates, reserve and #25 reason rows [F1]`.

**Verification:** the gates above. **Smoke rows:** `[CampaignMoverIssue] alliance=1 date=1861-06-23 unit="Army of Virginia:<id>" role=DepotBuilder reason=depot-task target=1101,-554 objective=-1 from=1247,-803 readiness=<n>` (the shape; the baseline save's own case); `[Reserves] alliance=1 capital=<n>/<n> hold=<k>:<n>/<n>`; `DefenseIntent` filter signature `alliance=1|removed=8|forbidden=8|reasons=asset-proximity-local-only:<n>,...`. **Rollback:** none needed (rows only); `Logging Profile` governs them. **PROVE-MODE plants:** drop the target from `IssueRow` -> `IssueRowNamesUnitRoleTargetObjectiveAndOrigin` red; emit a row per queued move -> `IssueRowsNeverExceedTheDailyCap` red.

## F2 — #25 telemetry-only while the planner runs (requirement-interception; decision 1)

**Why:** every alliance-1 #25 row read `removed=forbidden` (the CSA's whole list), so vanilla's only interception (13505-13780) never ran for either side; the planner's Hold design counts on it.

**Owned files:** create `Strategic/DefenseInterceptionGate.cs` (pure), `Patches/CheckForDefensiveOperationsCandidateFilterPatch.cs` (Prefix: observe or filter; Postfix: no revert while observing), `Plugin.cs` (bind `Enable Vanilla Defensive Interception`), `Telemetry/TelemetryTagPolicy.cs` (nothing new: `DefenseIntent` exists), test `DefenseInterceptionTests.cs`, csproj line, `docs/patch-catalog.md` row #25 (as built).

**Vanilla to read:** 13505-13780 (candidates 13608-13634, the 13610 exclusions, destination test 13600-13602), 13460-13504; `CampaignWithholdView.TryFreshAllocation`.

**Interfaces:**
```csharp
internal enum DefenseFilterMode { Filter = 0, Observe = 1 }
internal static class DefenseInterceptionGate
{
    // Observe when the flag is on and the planner runs for the alliance (alliances 0 and 1, a fresh published allocation with executors
    // allowed: CampaignWithholdView.TryFreshAllocation); Filter otherwise (the legacy enforcement, the rollback).
    internal static DefenseFilterMode ModeFor(int allianceId, bool interceptionFlag, bool plannerFlag, bool executorsFlag, bool freshAllocation, int assignmentCount);
    internal static int WouldRemove(IReadOnlyCollection<int> presentIds, ICollection<int> forbidden);   // what removed= counted
    internal static string ObservedSignature(int allianceId, int wouldRemove, string reasons);
}
internal static class ObservedLog   // per-alliance last signature, bound-checked, CampaignScope.Register("defense-filter-observed-log", ...)
{
    internal static bool ShouldEmit(int allianceId, string signature);
}
```

**Steps:**
- [ ] Tests first: `ObservesWhileThePlannerRunsForTheAlliance`, `FiltersWhenTheFlagIsOff`, `FiltersWhenThePlanIsStale`, `FiltersForEurope`, `FiltersForACicSide` (an empty allocation), `FiltersWhenThePlannerOrItsExecutorsAreOff`, `ObservedRowCountsWhatWouldBeRemoved` (`WouldRemove`), `ObservedRowEmittedWithNothingToRemove` (forbidden set non-empty, none present: `wouldRemove=0`), `ObservedRowNotGatedOnVerbose` (source pin: the observed branch tests no verbose flag), `ObservedRowDedupesPerCampaign` (`ShouldEmit`, reset by `CampaignScope.ResetAll`), `ObservedLogIgnoresAllianceTwo`, and a source pin `ObservingPrefixRemovesNothingAndPostfixRevertsNothing` (the Prefix returns before `ownUnits.RemoveAt`, the Postfix finds no snapshot).
- [ ] Implement: in `Observe` the Prefix computes `CollectForbiddenIds`, emits `DefenseIntent` with `decision=filter-observed` and `wouldRemove=`, `reasons=` (signature-bounded per alliance: the last signature per alliance is static state registered with `CampaignScope.Register("defense-filter-observed-log", ...)` from the patch's static constructor, as `defensive-ops-revert-log` is; the array is bound-checked, alliance 2 never indexes it), takes no snapshot and removes nothing. F2 also adds `[Reserves] enemyOnOwnSoil=<n>` (live land beliefs with `OnOwnSoil`, as D11 requirement-telemetry defines it) and `vanillaDefActive=<n>` (own formations in `InDefensiveOps` today) for criterion-interception-restored; `ObservedRowDedupesPerCampaign` pins the scope.
- [ ] Gates; catalog row; commit `feat(foresight): #25 observes while the planner runs, vanilla's interception restored [F2]`.

**Smoke rows:** `DefenseIntent` `decision=filter-observed` `alliance=1 wouldRemove=<n> reasons=<...>`; no `filter-candidates` row for alliances 0 and 1 while the planner runs; vanilla defensive operations appear in the snapshot (`[Reserves] vanillaDefWithheld=0` from F6b on; until then the `InDefensiveOps` count in `[Allocation] busy=`). **Rollback:** `Enable Vanilla Defensive Interception = false`. **PROVE-MODE plants:** return `Observe` for a stale plan -> `FiltersWhenThePlanIsStale` red; remove from `ownunits` in observe mode -> the source pin red.

## F3 — Enemy picture, hold need and release, capital reach and floor (requirements enemy-total, picture-partition, hold-need, hold-release, candidate-floor-capacity, capital-window, capital-floor)

**Why:** root causes 1 and 5: one sighting switched the Manassas sector's presumption off, released its hold and scouted the target; the capital threat counted nothing beyond 200, so Washington (389 from Richmond) never counted; the floor had no baseline.

**Owned files:** `Strategic/Planner/CampaignSnapshot.cs` (`EnemyTroopsFielded`, `EnemyCapitalX/Z`, `HasEnemyCapital`, reset), `Strategic/Planner/CampaignSnapshotRuntime.cs` (`ReadScalars`: `GameVars.alliance[1 - alliance].troopsfielded` and `.capital`), create `Strategic/Planner/EnemyPicture.cs` (pure), `Strategic/Planner/TheaterPlanner.cs` (`Inputs.Picture`; `PresumedAt` -> picture; `HoldNeedAt`; `BuildSectorFacts`; `HoldThreatAtTarget`; `CapitalThreat`; eligibility floor; `Trace` `anchors=`), `Strategic/Planner/ReserveRules.cs` (`PictureThreat`: vanilla's weight over picture elements within a reach; `VanillaThreat` stays for G8), `Strategic/Planner/ForceAllocator.cs` (`SectorMayGiveScout`'s belief-driven test of requirement-hold-need), `Strategic/Planner/CampaignPlannerContracts.cs` (`TheaterPlan.CapitalReach`, `TheaterPlan.PictureRow` is not added: the rows are built by the runtime from `EnemyPicture`), `Strategic/Planner/CampaignPlannerRuntime.cs` (`[EnemyPicture]`, `[CapitalThreat]` rows), `Strategic/Planner/PlannerSettings.cs` (`ForesightPicture`, refreshed by the coordinator beside `PlanTrace`; tests set it in try/finally), `TheaterPlanner.MinStrength(Inputs)` (the one reader of the minimum for F3's floors: the 5000 const until F5), `Patches/DefensiveOpsPatch.cs` (`[CapitalFloor]` adds `reach=` from the published plan), `Plugin.cs` (`Enable Strategic Foresight Picture`), `Telemetry/TelemetryTagPolicy.cs` (`EnemyPicture`, `CapitalThreat`), test `EnemyPictureTests.cs`, the rewritten pins (Global constraints), csproj line, census rows.

**Vanilla to read:** 81488-81614 (`troopsfielded` 81515, 81551, 81567), 180109-180126, 11668-11869 (11712, 11719, 11727, 11735-11738), 62230 (`Alliance.capital`), 14099; the spec's G1 detection section; `TheaterPlanner.IsScouted`, `LandThreatNear`, `EnemyGroundWithin`.

**Interfaces:**
```csharp
public struct PictureAnchor { public float X, Z, Importance01, Share; public bool IsCapital, Displaced; }
internal sealed class EnemyPicture
{
    public const float CapitalShare = 0.4f;              // D11 requirement-picture-partition; question-capital-share
    public float Total;                                  // EnemyTroopsFielded, NaN when unknown
    public float Believed, Faded, Unaccounted;           // sums (the [EnemyPicture] row)
    public readonly List<PictureAnchor> Anchors = new List<PictureAnchor>();
    // Built once per review after the beliefs and before the sector facts. isScouted is TheaterPlanner.IsScouted (marks excluded from F4 on).
    internal static EnemyPicture Build(CampaignSnapshot s, Func<float, float, bool> isScouted);
    // Faded land masses plus anchor shares within straight distance r, each weighted min(1, r / RiverLines.EffectiveDistance).
    internal float Presumed(CampaignSnapshot s, float x, float z, float r, ITerrainLineProbe probe);
    // The largest Importance01 of an undisplaced, enemy-held anchor within r (the floor's I), 0 when none.
    internal float UndisplacedImportance(float x, float z, float r);
}
// ReserveRules: vanilla's weight (11727) over the picture's elements (live beliefs, faded masses, anchor shares; afloat at straight distance) within `reach`.
internal static float PictureThreat(CampaignSnapshot s, EnemyPicture p, float x, float z, float reach, ITerrainLineProbe probe);
// TheaterPlanner: CapitalReach = max(RelocationRange, ReliefDays x CorpsRoadPerDay); ReliefDays = the largest MarchTime.EstimateDays to the own capital
// among own MassEligible formations of the capital sector's home department (none: the reach is RelocationRange); CorpsRoadPerDay =
// MarchTime.SpeedPerDay(Speed, 15, Speed.MountedBaseSpeed[15], Speed.RoadCost). Requirement-capital-window.
// Shares: CapitalShare 0.4 and the importance-proportional share are both computed; the plan runs on CapitalShare until question-capital-share.
```

**Steps:**
- [ ] Tests first (`EnemyPictureTests`), the investigation's geometry as the fixture (Richmond 1263.6,-1010.9; Washington 1349.9,-631.6; Manassas 1245,-681; Winchester 1107,-555; Romney 982.6,-515.7; relocation range 200); every floor is asserted relative to `TheaterPlanner.MinStrength(inputs)`, never as a literal, so F5 changes the helper's source and re-pins these by name (`FloorHasVanillasBaseline`, `ThreatExcludesTheBaseline`, `ACapacityBelowTheLiveMinimumIsIneligible`, `RetainNeverReenablesBelowTheMinimum`):
  - crit r1: `AnAnchorCountsInOneSectorsNeed`, `TheEastCanDonateToAShortWestIn002A` (FX-002A: three eastern sectors, a Washington anchor, a short West), `CapitalReachIsTheReliefRace`, `ReliefRaceExcludesTheWestFromRichmond` (the worked example), `CapitalTriggerAt002AStart` (both sides of own 5,017 within 200), `BothCapitalSharesReported`.
  - picture: `EveryManIsPlacedOnce` (believed + faded + unaccounted = total), `AFadingBeliefKeepsItsStrengthWhereLastSeen`, `AnExpiredBeliefReturnsItsMenToTheAnchors`, `CapitalTakesItsShareWhenUnscouted`, `ObjectiveAnchorsShareByImportance`, `AScoutedAnchorPassesItsShareOn`, `EveryAnchorScoutedLeavesTheRestAtTheCapital`, `UnknownTotalSpreadsNothing`, `AfloatBeliefsAddNoLandMass`, `AlliancesTwoToFourIgnored`.
  - D1/D2 superseded: `EstimateIsBelievedPlusPresumed` (not max), `AScoutedTargetStillCarriesUnscoutedAnchorsNearIt`, `ASightingAt165DoesNotSwitchOffWashington` (the 2nd Division near Winchester leaves the Washington anchor undisplaced and the Manassas sector's need above 0.6 x its presumption), `HoldNeedHoldsUnseenMenAtTheHoldRatio`, `FloorUsesOnlyUndisplacedImportance`, `BalanceStillRanksByTheBelievedLayer`.
  - release: `AHoldIsReleasedOnlyWhenEveryElementItFacesIsAtTheTarget` (Romney does not release the Manassas hold while the Washington anchor is in its reach), `ASectorFacingNothingReleasesNothing`.
  - eligibility: `ACapacityBelowTheLiveMinimumIsIneligible`, `OddsRuleUnchangedAboveTheMinimum`.
  - pass-4 rules: `PictureBuiltOncePerReview`, `CapitalAnchorImportanceIsOne`, `NoAnchorMeansUnplacedNotUnaccounted`, `RetainNeverReenablesBelowTheMinimum`, `MissingSpeedArrayFallsBackToTheRange`, `FlagOffRestoresTheDepartmentSumAndTheOldScoutTest`.
  - departments and D9: `DepartmentNeedCountsEachElementOnce`, `DepartmentNeedLandKindsWeightedTieToLowerSector`, `ASectorHoldingAgainstABelievedArmyStillRefusesAScout`, `AnAnchorAloneMakesASectorFace`.
  - capital: `WashingtonCountsTowardRichmondsFloor`, `FloorHasVanillasBaseline`, `ThreatExcludesTheBaseline` (no enemy: floor = minimum, threat01 = 0, no trigger), `FloorIsNeverBelowFourVanillaNeed` (the same weight over a subset), `RailsNotCreditedInTheReach`.
  - flag off: `FlagOffRestoresTheD2Presumption` (the old rule through the flag, so the rollback is real and tested).
- [ ] Implement `EnemyPicture`; `TheaterPlanner.Plan` builds it once before the first `Evaluate` (into `Inputs.Picture`; `Evaluate` builds it only when null) and returns it through `out EnemyPicture picture`; `CapitalThreat`, the sector facts, `HoldNeedAt` (D8's post) and the candidates read it; `TheaterPlan.CapitalReach`, `CapitalWeighted`, `CapitalThreat01` carry the row's numbers.
- [ ] Rewrite the superseded pins (Global constraints) in the same commit.
- [ ] Rows: `[EnemyPicture] alliance total believed faded unaccounted anchors displaced capitalShare top`, `[CapitalThreat] alliance reach relief corpsPerDay weighted floor threat elements`, on change of signature; `CampaignPlan:trace` `anchors=`.
- [ ] Gates; census; commit `feat(foresight): enemy picture, per-anchor presumption, capital reach and floor [F3]`.

**Smoke rows (002/A, CSA):** `[EnemyPicture] alliance=1 total=<Union troopsfielded> believed=<n> faded=<n> unaccounted=<n> anchors=<n> displaced=<n> capitalShare=0.40 capitalShareProp=<x> top=Washington:<n>;...`; `[CapitalThreat] alliance=1 reach=<at least 200> relief=<days> corpsPerDay=<x> weighted=<above 0> floor=<at least the minimum: 5000 before F5, 2000 after> threat=<0..1>`; `[CapitalFloor] alliance=1 floor=<at least vanillaNeed> reach=<x>`; `CampaignPlan:trace` with `est=` above the sighting alone near Romney. **Rollback:** `Enable Strategic Foresight Picture = false`. **PROVE-MODE plants:** `max` instead of the sum -> `EstimateIsBelievedPlusPresumed` red; per-sector `FreshLandBeliefWithin` switch restored -> `ASightingAt165DoesNotSwitchOffWashington` red; drop the baseline -> `FloorHasVanillasBaseline` red; baseline in threat01 -> `ThreatExcludesTheBaseline` red.

## F4 — Spotting marks on both sides (requirements marks-player-side, marks-ai-side, mark-beliefs; decision 2)

**Why:** the CSA's beliefs held 2 of about 15 Union groups; the player's own map showed the rest as marks (the Army of Occupation's 207 marks, the 1st Division at 1346,-671 and 1198,-614). Vanilla gives the AI no such channel (142938).

**Owned files:** create `Strategic/Planner/MarkRules.cs` (pure: `BandRound`, `MarkRadius`, the creation chance, the offset), create `Strategic/Planner/MarkLedger.cs` (pure, per alliance, `CampaignScope`), `Strategic/Planner/EnemyDetectionRuntime.cs` (player side: read `SpottedEnemy`; AI side: drive `MarkLedger` with the fog probe), `Strategic/Planner/EnemyArmyModel.cs` (mark application, `BaseRadius`, `RadiusAt`), `Strategic/Planner/CampaignPlannerContracts.cs` (`DetectionSource.Mark = 5`, `DetectionSources.FromSaved`, `EnemyDetection.MarkDay`, `MarkRadius`, `EnemyBelief.BaseRadius`), `Strategic/Planner/TheaterPlanner.cs` (`IsScouted`, `FreshLandBeliefWithin` skip `Mark`), `Strategic/Planner/EnemyBeliefReadings.cs` (`IsSeaInvasionThreat`'s landed branch and `IsRaidThreat` skip `Mark`), `Strategic/Planner/ReconMissionLedger.cs` (`InContact` skips `Mark`), `Strategic/Planner/CampaignPlannerRuntime.cs` (`CampaignSeed` initialised before `Detect`, passed in), `Strategic/Planner/ReconPlanner.cs` (the probe's "answered" test skips marks if it reads beliefs directly; check `ProbeWanted`), `PersistenceDto.cs` / `SidecarDtoCodec.cs` (`baseRadius`), `Plugin.cs` (`Enable Spotting Mark Beliefs`), `Telemetry/TelemetryTagPolicy.cs` (nothing new: `[EnemyBeliefs]` gains `marks=`), `docs/bug-fixes/README.md` (log `BUG-INTEL-001` as `Confirmed`), test `SpottingMarkTests.cs`, csproj lines, census.

**Vanilla to read:** 142530-143122 (fields 142536-142544, 142592-142606; `Update` 142642-142655; `AddMark` 142658-142751; `UpdateMarks` 142773-142906; `AddUnit` 142936-142951), 80215-80262, 101179-101237, 116504-116534, 127210-127229, `Config/unitprefs.txt` 36-37/80-81 and `Campaigns/002/A/campaignprefs.txt` 656-669.

**Interfaces:**
```csharp
internal static class MarkRules
{
    internal static float BandRound(float strength, int[] approximationValues);   // 80226-80255 without the 80261 multiplier
    internal static float MarkRadius(float intel, float[] distanceVariation);     // 142695
    // 142819-142846 with vanilla's live arrays (BUG-INTEL-001 included): elapsed years since the newest mark, intel, spottingfactor[0..1],
    // variation draw, the perk factors; capped at maximumcreationprobability, floored at 0.
    internal static float CreationChance(float elapsedYears, float intel, float spotting0, float spotting1, float variationDraw, float perkFactor, float cap);
    // Toward the closest enemy, as vanilla (GetAngle is the reverse bearing, 151304; +180 through GetAnglePos, 151207, points at it); blended
    // with the previous offset only when a previous mark exists (142706).
    internal static void Offset(System.Random rng, float intel, float[] distanceVariation, bool hasClosestEnemy, float angleToEnemyDeg,
                                bool hasPrevious, float ageYears, float prevOffsetX, float prevOffsetZ, out float dx, out float dz);  // 142695-142712
    internal static int MarkDay(float timeshown, int todaySerial);   // requirement-marks-player-side's decode; at most today
    internal static int NewestMark(IReadOnlyList<float> timeshown);  // the last index (AddMark appends, 142721); -1 when none
    internal static bool PlayerSideMark(int relation, bool detectedToday, int markCount);   // RelationOf == Enemy, not detected, has a mark
}
internal struct MarkInputs   // read once per group by the runtime
{
    public float Intel, Spotting0, Spotting1, SpottingVariation, PerkFactor, Cap, MinDistance, DistanceVariation0, DistanceVariation1;
    public bool HasClosestEnemy, Active, Garrison, Fleet; public float ClosestEnemyX, ClosestEnemyZ;
}
internal static class MarkLedgers { internal static MarkLedger For(int alliance); }   // null outside 0..1; CampaignScope reset, beside MoveTrackers
// Draws come in vanilla's order: variation (142819), roll (142866), angle (142700), magnitude (142701).
// EnemyArmyModel.Update: steps 1-5 over the non-mark detections, then the marks in a second pass (never in EffectiveKeys);
// EnemyArmyModel.AppliedMarkKeys beside ExpiredKeys.
internal sealed class MarkLedger
{
    // One entry per enemy group key: the newest mark's day, position and offset. Evaluate once per review per hidden enemy group.
    internal bool Evaluate(string unitKey, float x, float z, int daySerial, float dateYears, in MarkInputs inputs, System.Random rng, out EnemyDetection mark); // true while a newest mark is held, new or old
    internal void Drop(string unitKey);            // detected by rule a-d, in battle
    internal void PruneUnseen(ICollection<string> presentKeys);   // counts missed reviews; drops an entry absent PlannerState.PruneAfterMissedReviews of them
}
```

**Steps:**
- [ ] Tests first (`SpottingMarkTests`): `BandRoundingMatchesVanillasBands`, `MarkRadiusFromStoredIntelligence`, `CreationChanceCappedAtVanillasMaximum`, `BugIntel001NegativeTimeConstantFloorsAtAThousandthOfAYear`, `NoMarkUntilTheGroupMovesFiftyUnits`, `OffsetPointsTowardTheClosestEnemyAsVanillaDoes`, `OffsetBlendsWithThePreviousByAge`, `AFirstMarkIsNotBlended`, `SeededRollsAreReproducible`, `NoUnityRandomInTheMarkChannel` (source pin), `MarkMatchesByExactKeyOnly`, `MarkNeverRetiresAnotherBelief`, `SameDaySightingBeatsAMark`, `OlderMarkIgnored`, `AStaleNewestMarkKeepsNoBeliefAlive`, `MarkDayDecodesTheCalendarDate` (28 Feb -> 1 Mar is 1 day; 31 Jan 23:00 stays in January), `ASightingNeverRetiresAMarkBelief`, `AMarkBeliefReKeysWithinItsBaseRadius`, `AMarkCarriesNoEmbarkedTrait`, `SeenCountExcludesMarks`, `FirstMarkComesAtTheCap`, `AGridSeenMarkIsRefusedAndDropsTheMarks`, `MarkBeliefNeverScouts`, `MarkBeliefCountsInTheTotals`, `MarkRadiusGrowsWithDrift`, `MarkSourceRoundTripsInTheSidecar` (5 and `baseRadius`), `AnOlderFileReadsBaseRadiusZero`, `PlayerSideReadsOnlyTheNewestMark` (`MarkRules.NewestMark`), `PlayerSideMarkNeedsAnUndetectedEnemyWithAMark` (`PlayerSideMark`), `RuntimeReadsMarksOnlyForEnemyGroups` (source pin: `EnemyDetectionRuntime` reads `spottedmarks` only for groups `DetectionRules.RelationOf` classes Enemy; 142938 is why the player alliance never appears), `GarrisonsAndFleetsGetNoMark` (a regression pin, green from the start: `RelationOf` already drops them), `AMarkNeverLandsAGroup`, `AMarkIsNoRaid`, `AMarkIsNoRecontact`, `SameDaySightingAndMarkMakeOneBelief`, `EvaluateReturnsTheStoredNewestMark`, `MarksCountAppliedMarks`, `TheSeedIsSetBeforeDetect` (source pin), `MarkLedgerIgnoresAllianceTwo`.
- [ ] Implement; the AI-side probe reuses G1's masked `UnitIsHidden` loop (gated on `usefow`, W&L incidents masked) and runs inside `campaign.planner.detect`.
- [ ] Gates; census; bug log; commit `feat(foresight): spotting marks as beliefs on both sides [F4]`.

**Smoke rows:** `[EnemyBeliefs] alliance=<a> date=<d> mode=FogGrid|OwnSideWithinFog seen=<n> remembered=<n> expired=<n> contact=<n> marks=<n>` with `marks>0` on both sides in a W&L career, and `[EnemyPicture] alliance=<a> seen=<n> marks=<n> unaccounted=<n> capitalShare=<x> capitalShareProp=<x>`: under BUG-INTEL-001 `marks` is about the enemy's hidden group count from the first review and `unaccounted` falls to garrison and HQ residue (the gate shows that shift to the user). **Rollback:** `Enable Spotting Mark Beliefs = false`. **PROVE-MODE plants:** let a mark scout -> `MarkBeliefNeverScouts` red; let step 4 run for marks -> `MarkNeverRetiresAnotherBelief` red; `UnityEngine.Random` in `MarkLedger` -> the source pin red. Also: `MarkLedgers.For` is bound-checked for alliances (`if (alliance < 0 || alliance >= ledgers.Length) return null;`), pinned by `MarkLedgerIgnoresAllianceTwo`.

## F4b — BUG-INTEL-001 fix, both sides (only if the user picks (b) of question-intel-bug)

**Why:** 142819 reads the concealment array `spottingfactor[0..1]` (-50, -60) where `timefactorspottings` (0.1, 0.006 game years; loaded 55804-55806, never read) is meant, so marks come on every 50-unit move. **Owned files:** create `Patches/SpottingMarkTimingFixPatch.cs` (working label `FS-F4b-1`; the integrator assigns the ordinal at merge), `docs/bug-fixes/README.md` (BUG-INTEL-001 `In progress` until the smoke, then `Shipped` with the ordinal), `Plugin.cs` (`Fix Spotting Mark Timing`, default on), `Strategic/Planner/MarkRules.cs` (`CreationChance` reads the same pair the fix makes vanilla read, so both sides change at once), test `SpottingMarkTimingTests.cs`. **Patch shape:** a non-skipping Prefix on `SpottedEnemy.UpdateMarks(int)` (private, 142773; no argument to swap) saves `spottingfactor[0..1]` in `__state` and writes `timefactorspottings[0..1]` into them, and a Finalizer restores them, so vanilla's body runs unchanged. The swap is safe: `spottingfactor` is read only at 101203 and 142819, and the one-argument `UnitIsHidden(pos)` that `UpdateMarks`/`AddMark` call takes `unittype = -1` and skips 101202-101204. Both entries wrap `TelemetryPerf.Scope("campaign.patch.spotting-mark-timing.<prefix|finalizer>", ..., 2.0)` (it runs every frame for one unit, 142646-142654) and call `PatchFirstFire.Mark`; `tools/smoke-situational.txt` lists it as fog-only; F10a adds its catalog row. A Prefix-skip or Transpiler is not used. **Tests:** `FixedTimingUsesTimeFactorSpottings`, `BothSidesShareOneFormula`, `FlagOffKeepsVanillasTiming`. **Smoke:** marks thin to one per 2-36 game days per hidden group on both sides. **Rollback:** `Fix Spotting Mark Timing = false`.

## F5 — Live minimum strength (requirement-live-min-strength; decision 6)

**Why:** `ForceAllocator.MinGarrisonStrength = 5000` claims to mirror vanilla's minimum, which is 2000 in 002/A. Lands after F3 (decision 6): the picture must already hold the unseen men before every floor shrinks 2.5 times.

**Owned files:** `Strategic/Planner/CampaignPlannerContracts.cs` (`TheaterPlan.MinGarrisonStrength`), `Strategic/Planner/TheaterPlanner.cs` (set it; `NeedFor`, `HoldNeedAt`, the presumption floor read it; `MinStrengthFallback`), `Strategic/Planner/ForceAllocator.cs` (delete the const; `Target`, `SectorMayGiveScout` read the plan), `Strategic/Planner/ReinforcementRouting.cs` (reads the plan), `Strategic/Planner/ReservesRow.cs` and `Strategic/Planner/ReconPlanner.cs` (read `plan.MinGarrisonStrength`; `ReconPlanner.Context` gets the plan), `TheaterPlanner.MinStrength(Inputs)` (now reads the plan's value; F3's floor pins re-pinned by name), `Strategic/Planner/CampaignPlannerRuntime.cs` (the fallback warning when the raw read is not positive or not finite; `LogFirstReview` gets the plan), test `LiveMinimumStrengthTests.cs`, the rewritten pins, census.

**Steps:**
- [ ] Find the fixtures whose floor changes: `grep -ln "MinStrengthForMovement\|MinGarrisonStrength" tests/WhiskeyRealism.Tests/*.cs`; re-read every hit (a fixture that sets `MinStrengthForMovement = 1000f` now floors at 1000) and name the changed ones in the commit.
- [ ] Tests first: `PlanPublishesTheScenariosMinimum`, `FallbackOnlyWhenTheReadIsNotPositive`, `AHandBuiltPlanKeepsTheFallback`, `EveryFloorDerivesFromThePlan` (a source pin: the only `5000f` under `Strategic/Planner/` is `MinStrengthFallback`'s declaration; no `MinGarrisonStrength` const), `ReinforcementRoutingReadsThePlansMinimum`, `HoldTargetIsTheLiveMinimumInAQuietSector`, `ReconAndReservesReadThePlansMinimum`, `TheWarningKeysOnTheRawRead` (source pin), `PictureOffKeepsTheOldFloor` (picture flag off: `TheaterPlan.MinGarrisonStrength` is `MinStrengthFallback`, 5000, whatever vanilla's minimum).
- [ ] Implement; the first-review row (`CampaignPlannerRuntime.LogFirstReview`, `[CampaignPlanner] first review`) gains `minStrength=<n>`.
- [ ] Gates; commit `fix(foresight): the scenario's live minimum strength, one definition site [F5]`.

**Smoke rows:** `[CampaignPlanner] first review alliance=1 ... minStrength=2000` (002/A); `[Allocation] need=` floors at 2000. **Rollback:** `Enable Campaign Planner`. **PROVE-MODE plant:** restore the const in `Target` -> `HoldTargetIsTheLiveMinimumInAQuietSector` red.

## F9 — Depot builders from ready surpluses (requirement-depot-builder)

**Why:** root cause 3: the Army of Virginia (unready, the Fredericksburg screen) was the smallest free formation of the depot site's sector.

**Owned files:** `Strategic/Planner/ForceAllocator.cs` (`SmallestIn`: `MassEligible`, the surplus test on the first pass, the last-body rule on the second), test `DepotBuilderRuleTests.cs`.

**Steps:**
- [ ] Tests first: `AQuietSectorsSurplusMayBuild` (the standing-hold measure, not the hold tally, which is 0 there), `AnUnreadyFormationNeverBuilds`, `ABuilderLeavesItsSectorsHoldMet`, `TheMassGivesABuilderOnlyWhenNoSurplusExists`, `TheMassKeepsItsLastBody`, `TheMassBuilderStandsInTheTasksSector` (the shipped same-sector test, ForceAllocator.cs:639), `NoHoldReserveBuilds` (phase 2's F6b adds `ScoutDrawDepotAndPostsNeverTakeAMember` for the covering force), `TheInvestigationsArmyOfVirginiaIsNotPicked` (fixture: the 06-23 sector with the Army of Virginia at readiness step 1).
- [ ] Implement; gates; commit `fix(foresight): depot builders only from ready surpluses [F9]`.

**Smoke rows:** `[CampaignMoverIssue] role=DepotBuilder readiness=<2 or 3>`. **Rollback:** `Enable Campaign Planner`. **PROVE-MODE plant:** drop `MassEligible` -> `AnUnreadyFormationNeverBuilds` red.

## F10a — Phase 1 integration, docs, smoke

- [ ] Merge F1-F5 and F9 (with F4b when chosen) in order into `feat/strategic-foresight`; full gates; `PlannerReadCensusTests` (every new member with its reader), `PlannerPatchesReadOnlyPublishedState` (its `PublishedTypes` adds nothing in phase 1: the new published members are scalars on `TheaterPlan`), `PipelineOrderPinned` (in `ReviewPlan`: the `[Reserves]` row after the allocation row and yesterday's allocation captured before `Publish`; in `TheaterPlanner.Plan`: the picture built before the first `Evaluate`) green. Phase 1 adds no Harmony patch and no `PatchFirstFire.Mark` unless F4b is built; F4b's two marks are listed in `tools/smoke-situational.txt` (fog-only) and its catalog row is written here.
- [ ] Docs: spec D11 "as built" notes (deviations only); `docs/patch-catalog.md` #25 row (and F4b's row when built) and the planner runtime rows (picture, marks); `docs/handoff.md`; `docs/telemetry.md` (every new row and field); `docs/bug-fixes/README.md` (`BUG-INTEL-001`); `AGENTS.md` patch hygiene (#25's observe mode).
- [ ] Build, deploy and verify the deployed DLL (`stat` and `sha256sum` of `dist/WhiskeyRealism.dll` and the plugin copy must match) before any smoke.

## Phase 2 (built only after its own dry run logs no blocker and no major)

## F6a — The ownership table, RoleOf, the Redeploy role, withhold clauses (requirements claims, redeploy-role)

**Why:** every dry run found G3 and G4 disagreeing on who holds a formation; pass 5b's two blockers were the same class (the hold claiming committed, returning and in-flight formations; a covering member's sector). One published ownership table, with an explicit *takes from* set per step, ends it. Settled before any other phase-2 slice.

**Owned files:** `Strategic/Planner/CampaignPlannerContracts.cs` (`Claim`, `Claimant`, `TheaterPlan.Claims`, `TheaterPlan.SectorHeld`, `FormationRole.Redeploy = 9`), `Strategic/Planner/TheaterPlanner.cs` (rows 1-8 in `Evaluate`, rows 9-12 after `Decide`; publish), `Strategic/Planner/ReserveRules.cs` (`Hold(s, sectors, need, nearX, nearZ, claims, CoveringInput covering, HoldTally tally)`: candidates `MassEligible` claimed `None` or `PinnedBuilder`, counted anyway as requirement-claims' row 8 says; `covering` null until F6b), `Strategic/Planner/ForceAllocator.cs` (read the table; `RoleOf`; no `ReserveRules.Capital(` or `ReserveRules.Hold(` call; `Marches` and `Summary` learn `Redeploy`), `Strategic/Planner/CampaignMover.cs` ((role, reason) priority, `string.Equals(..., Ordinal)`; `MovesRole`; de-list `Redeploy` from `unitsinoffensiveoperations`), `Strategic/Planner/CampaignWithholdView.cs` (`Read` also yields the plan; `Redeploy` withheld from offense; the covering clause slot before the same-sector exception, filled by F6b), tests `OwnershipTableTests.cs`, the re-pinned `ForceAllocatorTests.ReserveRulesAreSharedWithThePlanner` (now: the allocator reads `TheaterPlan.Claims` and calls neither `ReserveRules.Capital(` nor `ReserveRules.Hold(`) and `ConstructionSteeringTests.AllocatorAndPlannerWiringPinsTheWishedBuilders`, the three `ReserveRules.Hold` callers in `ForceAllocatorTests.cs`, census, `PlannerPatchesReadOnlyPublishedState` (`PublishedTypes` adds `Claim`). No comment in `ForceAllocator.cs` or `ReserveRules.cs` names `enemyunits` (`AllocatorIsPureAndFogHonestBySource`).

**Interfaces:**
```csharp
public enum Claimant { None = 0, PinnedBuilder = 1, Immobile = 2, Recover = 3, Committed = 4, Returning = 5, DepartmentMove = 6, Capital = 7, Hold = 8, Mass = 9 }
public struct Claim { public int InstanceId; public Claimant Claimant; public int SectorId; public float Share; public float TargetX, TargetZ; public string Reason; }
// TheaterPlan.Claims: one per own formation, snapshot order. SectorId/Share: where the hold pass counted it and what it added (-1/0 none).
// TheaterPlan.SectorHeld: float[] per sector (ReserveRules.Hold's tally).
internal sealed class CoveringInput { /* F6b: post, axis sector ids and AxisShare, CoveringNeed, incumbents (key -> sector), home lookup, gave-up moves */ }
// ForceAllocator: the one claim -> role mapping, every row of requirement-claims' table (Returning and DepartmentMove -> Redeploy included).
internal static void RoleOf(in Claim c, in FormationRecord f, out FormationRole role, out string reason, out bool withheld);
```

**Steps:**
- [ ] Tests first (`OwnershipTableTests`): `OwnershipRowsTakeOnlyFromTheirListedClaimants` (one case per row, hand-built claims), `OneOwnerAtTheEndOfPlan`, `TheHoldNeverCountsOrReservesCommittedReturningOrInFlight` (rows 4-6 hand-set), `APinnedBuilderStaysAReserveBodyAsShipped`, `RecoverIsClaimedBeforeTheHolds`, `CapitalMayTakeCommittedReturningOrInFlight`, `MassTakesOnlyItsListedClaimants`, `EveryClaimCarriesItsCountedSectorAndShare`, `SectorHeldIsTheD9Tally`, `RoleOfCoversEveryRow` (Returning and DepartmentMove -> Redeploy), `G3AndG4ReadOneOwnershipTable`, `F6aReproducesTheShippedRoles` (the investigation's 06-23 fixture and the shipped allocator fixtures: no committed, returning or in-flight claim and no covering input give the shipped roles exactly), `G4LaterStepsRunOverNoneAndMass` (recon's mass pick, the depot's second pass, the reserve split, D9's scout, a post incumbent), `RedeployMovePriorityOrder`, `RedeployNeedsNoTempoRelease`, `RedeployReasonsWithholdTheRightPools`, `RedeployIsTakenOffVanillasOffensiveList`, `RedeployGiveUpFallsBackToHold` (through `RoleOf` and `Assign`).
- [ ] Implement with the producers of rows 4-6, 11, 12 and the covering input empty; re-pin the two source pins by name; gates; census; commit `refactor(foresight): one ownership table read by G3 and G4, RoleOf, the Redeploy role [F6a]`.

**Smoke rows:** none new; `[Allocation]` counts equal the shipped build's on the same save (`F6aReproducesTheShippedRoles` is the harness form). **Rollback:** `Enable Campaign Planner` (a refactor). **PROVE-MODE plants:** let the hold take a `Committed` claim -> `TheHoldNeverCountsOrReservesCommittedReturningOrInFlight` red; let G4 re-run `ReserveRules.Hold` -> the re-pinned source pin red.

## F6b — Covering force (requirements covering-post, covering-size, covering-members, covering-post-choice, covering-hold, covering-lifecycle; decision 4)

**Why:** nothing held the approach between the capitals (investigation (c), (h)); the capital floor guards the capital itself (`CapitalGuard` marches to it).

**Owned files:** `Strategic/Planner/CampaignSnapshot.cs` (`Places`), `Strategic/Planner/CampaignPlannerContracts.cs` (`PlaceRecord`, `CoveringPlan`, `CoveringKind`, `TheaterPlan.Covering`), `Strategic/Planner/CampaignSnapshotRuntime.cs` (`ReadPlaces`: `BattleUnits.towns` with `Town.Owner`), create `Strategic/Planner/CoveringForce.cs` (pure: points, posts, axis sectors, need, verdict), `Strategic/Planner/ReserveRules.cs` (`Hold` with `CoveringInput`: incumbents in their recorded sector first, axis sectors' bodies by distance to the stored verdict's post, members across the axis sectors each up to its `AxisShare`; claimant `Hold`, reason `covering-force`), `Strategic/Planner/TheaterPlanner.cs` (`MassFor` excludes members except for the enemy-capital candidate; publish `TheaterPlan.Covering` with `ThreatNear`), `Strategic/Planner/PlannerState.cs` (`CoveringKeys` as unit key -> sector, `CoveringForward`, `CoveringVerdictDays`, `CoveringVerdictKnown`, `CoveringPostKey`, written once in `Plan` after `Decide`), `Strategic/Planner/ForceAllocator.cs` (`Hold`/`Redeploy covering-force` from the claims; `Counted` and `HoldLeaving` treat a redeploying member as holding its own sector), `Strategic/Planner/CampaignPlannerRuntime.cs` (`[CoveringCorridor]`, `[CoveringForce]`, `[Reserves] covering= holdReserve= enemyOnOwnSoil= vanillaDefActive= vanillaDefWithheld= vanillaDefHoldOrCovering=`), `PersistenceDto.cs` / `SidecarDtoCodec.cs` (the covering fields), `Plugin.cs` (`Enable Covering Force`), `Telemetry/TelemetryTagPolicy.cs` (`CoveringCorridor`, `CoveringForce`), test `CoveringForceTests.cs`, csproj line, census.

**Vanilla to read:** 78940, 143173-143220 (`Town.Owner`, `IsCapital`, `CityName`), 62230, 11668-11869 (11719-11727: the radius the points come from), 14587-14632 (towns first), 13460-13504 and 13600-13610 (what vanilla's defence takes, BUG-AI-002), 14080-14158.

**Interfaces:**
```csharp
public struct PlaceRecord { public string Name; public float X, Z; public int Owner; }   // towns only, in BattleUnits.towns order (CampaignPlannerContracts.cs, censused)
public enum CoveringKind { None = 0, Forward = 1, Rear = 2 }
public sealed class CoveringPlan   // never null after Plan
{
    public string PostName; public float PostX, PostZ; public CoveringKind Kind; public float Need, NeedProp, Held, Radius;  // Radius = RelocationRange
    public bool ThreatNear;   // a live land belief (marks included) on own soil within Radius of the post at plan time: the defence clause
    public List<int> InstanceIds = new List<int>();
    public string Reason;     // null when posted; "no-capital", "no-corridor-place", "no-axis-sector", "flag-off", "cic-side", "main-effort-enemy-capital"
}
internal static class CoveringForce
{
    internal static void Points(CampaignSnapshot s, out float fx, out float fz, out float rx, out float rz);   // at R from each capital on the axis
    internal static int PostNear(CampaignSnapshot s, float x, float z);                                         // own-held town nearest within R, -1
    // AxisShare(k) = min(HoldNeed(k), HR' x the land elements of k's need lying within R of the axis segment); axis sectors have AxisShare > 0.
    internal static void AxisShares(CampaignSnapshot s, SectorModel sectors, EnemyPicture picture, float[] holdNeed, float holdRatio, float[] into);
    internal static float Need(float estimateAtEnemyCapital, float holdRatio, float minStrength, float sumAxisShare); // min(max(HR' x est, min), sum); 0 axis: None
    internal static bool Verdict(bool forwardMet, string postKey, PlannerState state, out bool write);          // SwitchDays hysteresis
}
```

**Steps:**
- [ ] Tests first (`CoveringForceTests`; fixture FX-002A's towns, with the town/small-town split **assumed** as the save's type field suggests and named so): `PointsAreAtTheRelocationRangeFromEachCapital`, `PostIsTheNearestOwnHeldTownWithinRange`, `OneTownForBothPointsIsOnePost` (002/A: the 389 axis), `NoTownWithinRangeNoCoveringForce`, `SmallTownsAreNeverPosts`, `AxisShareCountsOnlyElementsOnTheAxis`, `APeninsulaSectorHasNoAxisShare` (FX-002A with a sector facing Fort Monroe whose centroid lies within R of the axis), `NoAxisSectorNoCoveringForce`, `NeedIsCappedAtTheAxisShares`, `NeedNeverBelowTheLiveMinimum`, `CoveringNeverStripsAnotherSectorsHold` (every sector's hold stays at its need with and without the force), `NoSectorGivesMoreThanItsAxisShare`, `MembersAreTheAxisHoldReservesNearestThePost`, `MembersOrderedByTheStoredVerdictsPost`, `IncumbentsKeepTheirPlace`, `AnIncumbentCountsInItsRecordedSectorWhereverItStands`, `AnArrivedMemberIsNotCountedTwice`, `MembersCountInTheirOwnSectorsHold`, `MapGroupPlayerNeverTheOnlyHolder`, `PlayerCommandNeverAMemberAndCountsNothing`, `CarryingGroupMayBeAMember`, `BusyFormationsAtThePostCount`, `ForwardVerdictOnCountedStrength`, `VerdictNeedsThreeReviews`, `EnemyCapitalPushLeavesTheCoveringStateUntouched`, `EnemyCapitalPushMakesTheLeftBehindOrdinaryHoldReserves`, `MassForTakesMembersForAnyEnemyCapitalTarget`, `LostCapitalWritesNoneKey`, `CoveringUnbindsOnLostCapitalNoPlaceFlagOffAndEnemyCapitalPush`, `MassForExcludesMembersExceptAgainstTheEnemyCapital`, `ScoutDrawDepotAndPostsNeverTakeAMember`, `HeldBuildersAndGiveUpsNotInThePool`, `MemberAtPostHoldsWithheldElseRedeploys`, `RedeployingMemberInThePostSectorCountsInG4`, `DefenceMayTakeAMemberWhenThreatNear`, `ThreatNearNeedsAnAdvanceOnOwnSoil`, `ThreatNearCountsMarks`, `CoveringClauseBeforeTheSameSectorException`, `CoveringClauseReadsBeliefsNotVanillasArgument` (the fixture's vanilla argument is a different enemy than the intruder: `enemyunits[j]` against `list4[j]`), `OffenceNeverTakesAMember`, `CicSideHasNoCoveringForce`, `CoveringStateRoundTripsInTheSidecar` (keys with their sectors), `UnionSideCoversToo`, and F6a's table tests re-run with covering input.
- [ ] **Runtime step one: the corridor census.** `[CoveringCorridor] alliance=<a> date=<d> candidates=<...> forward=<town|none> rear=<town|none> axis=<sectors>` on the first review and on change; the posts are whatever the rule gives (`question-corridor-posts`, recommended: accept); the row is shown to the user, nothing is tuned to names.
- [ ] Implement the rest; gates; census; commit `feat(foresight): covering force as the axis's own hold, posted between the capitals [F6b]`.

**Smoke rows (002/A):** `[CoveringCorridor] alliance=1 date=<d> candidates="Fredericksburg:<n>:<n>:209:199;..." forward=Fredericksburg rear=Fredericksburg axis=<k,...>` (expected one post per side); `[CoveringForce] alliance=1 post=Fredericksburg kind=forward|rear need=<n> needProp=<n> held=<n> members="<unit+unit>" short=<n> threatNear=<0|1> verdictDays=<n>`; `[CampaignMoverIssue] alliance=1 role=Redeploy reason=covering-force target=<post>`; `[Allocation] covering=<n> redeploy=<n>`; `[Reserves] covering=<held>/<need> vanillaDefWithheld=0`; a Union `[CoveringForce] alliance=0` row as well. **Rollback:** `Enable Covering Force = false`. **PROVE-MODE plants:** draw a member from a non-axis sector -> `CoveringNeverStripsAnotherSectorsHold` red; let `MassFor` take members for any target -> `MassForExcludesMembersExceptAgainstTheEnemyCapital` red; read vanilla's argument in the clause -> `CoveringClauseReadsBeliefsNotVanillasArgument` red.

## F7 — Main effort commitment, sorties and return home (requirements commitment, commitment-mover, return-home)

**Why:** root cause 4: the CSA main effort went initial, no-candidate (`cap=0`), initial, no-candidate in six days because the march dropped readiness below step 2; after the drop every formation held where it stood, far from home.

**Owned files:** `Strategic/Planner/PlannerState.cs` (`Sorties`: key -> objective, role, donor theater, origin, missed reviews; pruned on its own count), `Strategic/Planner/TheaterPlanner.cs` (`Massable` = `MassEligible` or committed to the candidate's objective, read by `MassFor` only; the producers of rows 4, 5 and 12 of requirement-claims; `SelectMass` ranks committed, then returning, then the rest by army; `TheaterPlan.Returns` after `Decide`; a reinforcement's first `Returning` claim transfers its department back to the donor), `Strategic/Planner/ReserveRules.cs` (returning formations count 0, as `Recovers`), `Strategic/Planner/CampaignPlannerContracts.cs` (`TheaterPlan.Returns`, `ReturnMove { InstanceId, OriginX, OriginZ }`), `Strategic/Planner/ForceAllocator.cs` (`Redeploy return-home` from the `Returning` claims), `Strategic/Planner/CampaignMover.cs` (record a sortie when `moved || outcome == Queued`; no new march and no retry at step < 2 on enemy ground, skipped before `ShouldIssue` and counted after the arrived check, `HeldReadiness`; the tracker is unchanged), `Strategic/Planner/CampaignPlannerRuntime.cs` (`[Reserves] committed= returning=`), `PersistenceDto.cs` / `SidecarDtoCodec.cs` (`sorties`), `Plugin.cs` (`Enable Main Effort Commitment`), test `CommitmentTests.cs`, census.

**Vanilla to read:** 14068-14078, 14091, 91336-91350, 113937-113965, 173418-173433.

**Steps:**
- [ ] Tests first (`CommitmentTests`): `ADispatchedMemberStaysMassedThroughAReadinessDip`, `RetreatOrLowMoraleStillRelieves` (14072), `LowSupplyStillRecovers`, `AnUncommittedFormationStillNeedsStepTwo`, `NoNewMarchAtStepOne`, `AMovingUnreadyCorpsIsConfirmed`, `AFailedRecordKeepsItsRetryWhileUnready`, `LowReadinessAloneNeverGivesUp`, `AFailedRecordIsNotWithheldFromDefence`, `SortieRecordedAtFirstDispatchOnly`, `ASortieIsRecordedWhenMovedOrQueued`, `ABuilderSortieIsNeverCommitted`, `SortiesPruneOnTheirOwnMissedReviews`, `AReinforcementGoesHomeToItsDonorDepartment`, `SelectMassRanksCommittedThenReturning`, `HeldReadinessCountedAfterTheArrivedCheck`, `OriginKeptAcrossReissues`, `AnEndedDecisionSendsItsSortiesHome`, `TheNewMassTakesFirst`, `CommitmentSurvivesANoCandidateReview` (objective key, not the serial), `TheReinforcementHasASortie`, `ACommittedFormationTheMassLeftGoesHome`, `HoldNeverReservesACommittedFormation`, `CommittedAndReturningClaimsAgreeInG3AndG4` (F6a's table tests re-run with these producers), `CoveringPoolTestsWithCommittedClaims` (F6b's pool tests re-run), `AnAppointmentRekeysTheSortie`, `ArrivedSortieIsDeleted`, `GaveUpSortieHoldsInPlace`, `AReserveMayTakeAReturningFormation`, `ReturningCountsInNoHold`, `ReturnHomeNeverCrowdsOutTheMass` (the cap), `PlayerCommandNeverHasASortie`, `SortiesRoundTripInTheSidecar`, `FlagOffRestoresD5`.
- [ ] Implement; the investigation's chain as a fixture test: `TheCsaMainEffortSurvivesTheMarch` (06-22 dispatch, readiness falls to step 1 on 06-24, the decision keeps `cap>0`).
- [ ] Gates; census; commit `feat(foresight): dispatched corps stay in the main effort, stranded corps go home [F7]`.

**Smoke rows:** `[Reserves] alliance=1 committed=<n> returning=<n>`; `[CampaignMover] ... heldReadiness=<n>`; no `[CampaignPlan] alliance=1 switch=no-candidate` while `committed>0`; `[CampaignMoverIssue] reason=return-home target=<origin>` after a decision ends. **Rollback:** `Enable Main Effort Commitment = false`. **PROVE-MODE plants:** drop `Committed` from the mass test -> `ADispatchedMemberStaysMassedThroughAReadinessDip` red; give up a `Failed` record below step 2 -> `AFailedRecordKeepsItsRetryWhileUnready` red.

## F8 — Department shortfall reinforcement (requirement-department-reinforcement; decision 5)

**Why:** the West (Porterfield's 1,402 men, Recover) had no way to be reinforced: D6's path serves only the main effort, #125 routes recruits within 300 of the recruiting cell.

**Owned files:** `Strategic/Planner/TheaterPlanner.cs` (`DepartmentReinforcement` after the mass; `DepartmentHave(d)`, the one have for the rank, the donor test and the row; `TheaterPlan.DepartmentMoves`; the producers of rows 6 and 11; `DepartmentNeed` is F3's; D6's `DepartmentHoldMet` unchanged), `Strategic/Planner/PlannerState.cs` (`DepartmentInFlight`; transfer back on give-up), `Strategic/Planner/CampaignPlannerContracts.cs` (`DepartmentMove { InstanceId, From, To, TargetX, TargetZ, PickDay }`, `TheaterPlan.DepartmentMoves`), `Strategic/Planner/ForceAllocator.cs` (`Redeploy department-reinforcement` from the `DepartmentMove` claims), `Strategic/Planner/CampaignPlannerRuntime.cs` (`[DepartmentReinforcement]`, `[Reserves] deptShort=`), `PersistenceDto.cs` / `SidecarDtoCodec.cs` (`departmentInFlight`), `Plugin.cs` (`Enable Department Reinforcement`), `Telemetry/TelemetryTagPolicy.cs` (`DepartmentReinforcement`), test `DepartmentReinforcementTests.cs`, census.

**Steps:**
- [ ] Tests first: `TheShortestDepartmentGetsOneFormation`, `TheDonorsHoldStaysMet`, `SoonestWithinTenDaysRailCredited`, `OneInFlightPerReceivingDepartment`, `AtMostOnePickPerReview`, `ThePickTransfersTheDepartmentAtOnce`, `ArrivalClearsTheRecord`, `GiveUpTransfersBack`, `PrunedFormationClearsTheRecord`, `NeverAReserveMassCoveringGarrisonOrBuilder`, `NeverThePlayersCommandOrMapGroup`, `ARoleChangeClearsTheRecord` (Recover, battle, vanilla defence: `state=Ended`, transferred back), `ACapitalReserveTakeKeepsTheTransfer`, `DepartmentMoveClaimAgreesInG3AndG4`, `OneHaveForRankDonorAndRow`, `AnInFlightFormationCountsForItsReceiver`, `D6sDepartmentHoldMetIsUnchanged`, `DaysIsTheEstimateAtThePick`, `CoveringPoolTestsWithDepartmentClaims`, `ASecondShortDepartmentIsQueued` (two short departments with donors: one `Picked`, the other `queued`), `AMarchingArmyDoesNotHoldItsDepartment`, `NoReturningOrCommittedDonor`, `WorksWithNoMainEffort` (`main=none`), `DepartmentInFlightRoundTripsInTheSidecar`.
- [ ] Implement; gates; census; commit `feat(foresight): a short department gets one formation at a time [F8]`.

**Smoke rows:** `[Reserves] alliance=1 deptShort=West:<need>/<have>:donor|nodonor|queued`; `[DepartmentReinforcement] alliance=1 unit=<name> from=East to=West target=<x,z> days=<d> state=Picked`, later `state=Arrived` or `state=Ended`; `[CampaignMoverIssue] reason=department-reinforcement`. **Rollback:** `Enable Department Reinforcement = false`. **PROVE-MODE plants:** allow two in flight -> `OneInFlightPerReceivingDepartment` red; skip the donor test -> `TheDonorsHoldStaysMet` red.

## F10b — Phase 2 integration, docs, smoke

- [ ] Merge F6a, F6b, F7, F8 in order into `feat/strategic-foresight-p2`; full gates; `PlannerReadCensusTests`; `PlannerPatchesReadOnlyPublishedState` (`PublishedTypes` adds `Claim`, `CoveringPlan`, `ReturnMove`, `DepartmentMove`); `PipelineOrderPinned` over `TheaterPlanner.Plan`'s body: ownership rows 1-8 -> `Evaluate` -> `Decide` -> `SelectMass` -> `PostFromOutside` -> `DepartmentReinforcement` -> the return moves -> `AssignPostures`; in `ReviewPlan` the covering rows after the allocation row.
- [ ] Docs: spec D11 "as built" notes (deviations only); `docs/patch-catalog.md` planner runtime rows (covering, department); `docs/handoff.md`; `docs/telemetry.md`; `docs/bug-fixes/README.md` (`BUG-AI-002`, 13600-13610, read around and not fixed); `AGENTS.md` campaign write owners (the `Redeploy` role, the covering force's defence clause).
- [ ] Build, deploy and verify the deployed DLL (`stat` and `sha256sum` must match) before any smoke.

## Smoke checklists (in order; a fresh 002/A W&L career from 1861-06-02, the player below map level, 30 game days, `Logging Profile = FullTuning`, as the baseline: `TacticalTuning` keeps Tactical-layer rows only)

**Phase 1** (`feat/strategic-foresight`):

1. After F1+F2: every alliance-0/1 #25 row is `filter-observed`; `[CampaignMoverIssue]` rows name unit, role and target.
2. After F3: `[EnemyPicture] alliance=1` shows the Union's `total`; the conservation holds (test-plan count TC-PIC-CONSERVE); `[CapitalThreat] alliance=1 weighted>0` and `floor>=` the minimum (5000 until F5, 2000 after); the department need counts Washington's anchor once (harness TC-HOLD-EASTDONOR; the donation itself is phase 2's F8).
3. After F4: `[EnemyBeliefs] marks>0` on both sides; `[EnemyPicture] unaccounted=` falls. **Gate (the user's):** the integrator reports, from the post-F4 rows, the measured `total`, `unaccounted`, both capital shares (`capitalShare=` and `capitalShareProp=`) and the would-be covering need computed from the rows (`0.6 x` the estimate at the enemy capital, under each share), and asks `question-capital-share` (recommended: decide here, on these numbers) and `question-intel-bug` (recommended: (b), F4b) before F4b and F5.
3b. After F5 and F9: `[CampaignPlanner] first review minStrength=2000`; criterion-builders-fit; criteria capital-threat-live, picture-conserves, marks-read and interception-restored over the 30 days.

**Phase 2** (`feat/strategic-foresight-p2`):

4. After F6a: `[Allocation]` counts unchanged against the same save. After F6b: `[CoveringCorridor]` shows one post per side (whatever the rule gives; recommended `question-corridor-posts` (a): accept it); `[CoveringForce] alliance=1` reports `short=` against `need=` on every review (criterion-covering-held: `held/need` on the share of reviews it names, a reported short not hidden); no covering member in a `MainEffort`, `Support` or `DepotBuilder` issue row and no non-axis sector's hold below its need because of the force (criterion-axis-not-stripped).
5. After F7 and F8: criteria mass-kept, return-home, department-reinforced.
6. Each phase's whole run: criterion-review-budget (`campaign.planner.review` under 4 ms per alliance at 20x); no exception; no `[CampaignMoverIssue]` naming the W&L player's own command.

## Coverage (requirement -> slice)

| Requirement (D11) | Slice | Phase |
|---|---|---|
| requirement-telemetry | F1 (mover, trace, reserves, #25 reasons, dates), F2-F8 (their rows) | 1 (2 for the covering, commitment and department rows) |
| requirement-interception | F2 | 1 |
| requirement-enemy-total, requirement-picture-partition, requirement-hold-need, requirement-hold-release, requirement-candidate-floor-capacity, requirement-capital-window, requirement-capital-floor | F3 | 1 |
| requirement-marks-player-side, requirement-marks-ai-side, requirement-mark-beliefs | F4 (F4b if `question-intel-bug` is (b)) | 1 |
| requirement-live-min-strength | F5 | 1 |
| requirement-claims, requirement-redeploy-role | F6a (F6b, F7, F8 fill their producers) | 2 |
| requirement-covering-post, requirement-covering-size, requirement-covering-members, requirement-covering-post-choice, requirement-covering-hold, requirement-covering-lifecycle | F6b | 2 |
| requirement-commitment, requirement-commitment-mover, requirement-return-home | F7 | 2 |
| requirement-department-reinforcement | F8 | 2 |
| requirement-depot-builder | F9 | 1 |

Open questions routed by this plan: `question-capital-share` (gate after F4's smoke; recommended: pick on the measured shares), `question-corridor-posts` (F6b runtime step one; recommended: (a) accept the rule's posts), `question-intel-bug` (gate after F4's smoke; recommended: (b) fix both sides behind `Fix Spotting Mark Timing`, slice F4b; F4 mirrors vanilla until decided).

<!-- author-sweep: applied purpose-advantage-measurement, fit-criteria, guardrails, read-paths, roles, lifecycle, exceptions, concurrency, decision-records; n/a: none -->
