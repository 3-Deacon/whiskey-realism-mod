# Stage G Strategic Foresight Implementation Plan

Produced-by: claude-opus-5-5 (high)

**Author seat:** worktree `.worktrees/plan-foresight`, branch `plan/strategic-foresight`, cut from `main` `2bfddd87`.

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development (recommended) or superpowers:executing-plans, task by task; superpowers:test-driven-development for every pure-logic change (red first, exit 134 on the first failing assertion). Steps use checkbox (`- [ ]`) syntax.

**Goal:** build spec amendment D11, "Strategic foresight amendments 2026-10-18 (D11)" in `docs/superpowers/specs/2026-10-14-campaign-planner-design.md` (binding, with its "Vanilla behaviour relied on" anchors, requirements, fit criteria and open questions). Each AI side plans from a fog-honest picture of the whole enemy army (beliefs, spotting marks, the public `troopsfielded`, anchors), keeps a covering force between the capitals, sizes its capital floor by march time, reinforces a short department, keeps a dispatched main effort together, brings stranded corps home, takes depot builders only from surpluses of ready formations, uses the scenario's live minimum strength, and gives vanilla's own interception back to vanilla (#25 telemetry-only while the planner runs).

**Architecture:** pure files under `src/WhiskeyRealism/Strategic/Planner/` (in the test csproj) plus their runtime readers (`*Runtime.cs`, not in the test csproj), as stage G. Everything is daily-review work: the Plan job (snapshot, detection, marks, beliefs, picture, plan, allocation) and the Execute job (mover). Patches read only the published plan (`CampaignPlanStore`); the only patch whose behaviour changes is #25, and #127's defence clause changes inside `CampaignWithholdView` (pure).

**Tech stack:** C# netstandard2.1, BepInEx 5.4 + HarmonyX, Unity 2021.3.16f1 Mono x64. Console harness `tests/WhiskeyRealism.Tests` (net8.0; `internal static class *Tests { public static void Run() }`).

**Inputs (read before your slice):** D11 (all of it) and the campaign-planner spec's "Vanilla and W&L model"; `docs/superpowers/plans/2026-10-15-campaign-planner.md` § Global Constraints (binding here too) and the as-built sections of the slices you touch (G3, G4, G5a, G5c, G8, the smoke fixes); root `AGENTS.md`, `src/WhiskeyRealism/{Strategic,Patches}/AGENTS.md`, `tests/WhiskeyRealism.Tests/AGENTS.md`; the investigation `scratchpad/loop/foresight/investigation.md` and decisions `scratchpad/loop/foresight/decisions.md` (session scratchpad); the test plan `docs/loop/foresight/test-plan.md` (frozen at the gate). Decompile `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` (build 14703111; regenerate per `docs/findings.md` if `/tmp` was wiped).

## Outline

1. Gates and sequencing (the concurrent branch, the ordinals).
2. Flags.
3. Slices F1-F10: each with why, owned files, vanilla to read, interfaces, steps (tests first), verification, smoke rows, rollback, prove-mode plants.
4. Smoke checklist.
5. Coverage (requirement -> slice).

## Global constraints (in addition to the campaign-planner plan's)

- **Gates before each commit:** `./build.sh` 0 warnings; the test build 0 warnings; `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` exit 0 and its PASS count; `python3 -I tools/config-audit.py .` 0 findings; `python3 -I tools/orphan-sweep.py .` 0 (and `--include-test-only` adds nothing this plan added); `python3 -I tools/test_smoke_check.py` OK. `git log --oneline -10 && git status` before each commit. Messages `<type>(foresight): <summary> [F<n>]`, then the decompile anchors, then `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Changed semantics are re-pinned, never dropped.** A test that pins a rule D11 supersedes (D1's `max`, D2's `S` term and per-sector switch, P7's floor without a baseline, the `MinGarrisonStrength` const, the nearest-belief hold release) is rewritten in the same commit to pin the new rule and named in the commit message. Find them with `grep -ln "PresumedAt\|HoldThreatAtTarget\|MinGarrisonStrength\|CapitalFloorStrength\|HoldNeedAt" tests/WhiskeyRealism.Tests/*.cs` at the start of the slice.
- **Census.** Every new contract member names its reader file in `PlannerReadCensusTests` (`CensusReceiverTypes` says what the census can resolve); delete the member or name its reader, never allow-list.
- **Patches read published state.** New published members: `TheaterPlan.Covering`, `TheaterPlan.MinGarrisonStrength`, `TheaterPlan.CapitalReach`, `TheaterPlan.Returns`, `TheaterPlan.DepartmentMoves`. `PlannerPatchesReadOnlyPublishedState` must stay green.
- **Randomness.** The AI-side mark channel draws from `System.Random` seeded per D11 (requirement-marks-ai-side); no slice calls `UnityEngine.Random`.
- **W&L.** `WlChainRules.MayMove` for every new move (covering, department, return home); `CountsTowardCoverage` for every new sum; a CIC side's allocation stays empty (`ForceAllocator.StandsDown`).
- **Sidecar.** Format v3, additive fields only (an older file reads defaults): `beliefs[].baseRadius`, `plannerState.coveringForward`, `coveringVerdictDays`, `coveringKeys`, `sorties`, `departmentInFlight`. `PersistenceDto.cs` and `SidecarDtoCodec.cs` are shared files: each slice edits only its own fields.

## Gates and sequencing

- **`fix/recon-unfilled-reason` merges first.** It is on `9b01218a` (one commit over `2bfddd87`) and edits `ForceAllocator.cs` (`AssignRecon`, `DrawFromHoldReserve`, `Run`'s tail, new `FreeScouts`/`WhyUnfilled`/`RoleTag`/`UnfilledWhy`), `CampaignPlannerContracts.cs` (`Allocation.ReconUnfilledReason` becomes `ReconUnfilledWhy`, new `ReconFree`), `ReconStep.cs`, `ReconPlanner.cs`, `MoveTracker.cs` (`TryGetMove`), `CampaignMover.cs` (`MoverTally.MissRows`, `MissRow`, the `[CampaignMoverMiss]` row), `CampaignMoverRuntime.cs`, `TelemetryTagPolicy.cs` and the spec's 2026-10-17 section. The foresight build branch `feat/strategic-foresight` is cut from the `main` that contains it, and this branch (`plan/strategic-foresight`, docs only) is merged into it first. **If it has not merged when F1 starts**, F1 waits; no foresight slice is built beside it. The seams, should the integrator ever have to merge the two the other way round: `ForceAllocator.Run` (this plan adds `AssignCovering`, `AssignReturning`, `AssignDepartmentMoves` between `AssignCapitalGuards` and `AssignDepotBuilders`; that branch rewrites the last lines), `SectorMayGiveScout` (this plan replaces `MinGarrisonStrength` there; that branch only re-comments it), `CampaignMover.MoverTally` (this plan adds `IssueRows` and `HeldReadiness` next to `MissRows`), `TelemetryTagPolicy` (both add one line each).
- **Patch ordinals.** This plan adds **no Harmony patch** (#25 keeps its number and changes behaviour; #127's change is inside `CampaignWithholdView`), so it claims no ordinal. A slice that finds it needs one takes the next free number at its integration merge (next free on `main` is **#133**, `docs/patch-catalog.md` § Conventions). Stage H's plan (on branch `plan/stage-h-naval` only, not on `main`) carries provisional #131-#134, which already collide with the assigned #131 (`LayoutObserverPatch`) and #132 (`ApproachLineAdvancePatch`): stage H renumbers at its own integration; this plan uses none of #131-#134.
- **Order.** F1 (telemetry) -> F2 (#25) and F3 (picture) in parallel -> F4 (marks, after F3) -> F5 (live minimum, after F3; decision 6) -> F6 (covering force) -> F7 (commitment and return home) -> F8 (department reinforcement) -> F9 (depot builders) -> F10 (integration, docs, smoke). F2 is disjoint from F3 (only `Plugin.cs` and `TelemetryTagPolicy.cs` are shared, one line each). F6-F9 each edit `ForceAllocator.cs`, `TheaterPlanner.cs` and `CampaignPlannerContracts.cs` and are sequential. A user-facing gate (the smoke after F3, for `question-capital-share`) sits between F5 and F6; see the smoke checklist.

## Flags (all default on, truthful descriptions, section "Campaign Planner", bound in the slice that first reads them)

| Flag | Slice | Off means |
|---|---|---|
| `Enable Vanilla Defensive Interception` | F2 | #25 filters and reverts as before D11 |
| `Enable Strategic Foresight Picture` | F3 | D2's presumption, P7's floor and the 200-radius capital threat, the nearest-belief hold release, as before D11 |
| `Enable Spotting Mark Beliefs` | F4 | no mark detections on either side |
| `Enable Covering Force` | F6 | no covering reserve, no `covering-force` role |
| `Enable Main Effort Commitment` | F7 | D5's readiness rule for every mass member, no sorties, no return home |
| `Enable Department Reinforcement` | F8 | no department transfer |

The live minimum strength (F5) and the depot builder rule (F9) are corrections of the allocator's rules and have no flag of their own; `Enable Campaign Planner` is their rollback, as for every planner rule. `python3 -I tools/config-audit.py .` must stay at 0 findings (OFF, UNREAD, DUPLICATE, DESC).

---

## F1 — Telemetry the investigation could not read (requirement-telemetry, first half)

**Why:** the investigation reconstructed causes 1 and 3 from code and geometry because no row named the moved formation, its target, the reserve flags or #25's reasons. Every later slice's smoke reads these rows; they ship first, with no behaviour change.

**Owned files:** `Strategic/Planner/CampaignMover.cs` (`MoverTally.IssueRows`, `IssueRow`), `Strategic/Planner/CampaignMoverRuntime.cs` (emit), `Strategic/Planner/TheaterPlanner.cs` (`Trace`: `@<x>,<z>` per candidate), create `Strategic/Planner/ReservesRow.cs` (pure: the `[Reserves]` row from snapshot, plan, allocation), `Strategic/Planner/CampaignPlannerRuntime.cs` (emit `[Reserves]` on change of signature), create `Strategic/DefenseFilterTally.cs` (pure: reasons tally), `Patches/CheckForDefensiveOperationsCandidateFilterPatch.cs` (`EmitDefenseIntentFilter` carries `reasons=`), `Telemetry/TelemetryTagPolicy.cs` (`CampaignMoverIssue`, `Reserves`), test `tests/WhiskeyRealism.Tests/ForesightTelemetryTests.cs`, csproj lines for the two pure files.

**Interfaces:**
```csharp
// CampaignMover.MoverTally
public readonly List<string> IssueRows = new List<string>();   // one per issued move, at most MoveTracker.MaxMovesPerDay a review
// "[CampaignMoverIssue] alliance=<a> unit=<key> role=<Role> reason=<reason> target=<x>,<z> objective=<id> from=<x>,<z> readiness=<step>"
internal static class ReservesRow { internal static string Build(CampaignSnapshot s, TheaterPlan plan, Allocation a); }
// F1 shape: "[Reserves] alliance=<a> capital=<held>/<floor> hold=<sector:held/need,...>" (at most 6 sectors, largest need first);
// F3, F6, F7, F8 append covering=, committed=, returning=, deptShort=, vanillaDefWithheld= (D11 requirement-telemetry is the final shape).
internal static class DefenseFilterTally { internal static string Reasons(IReadOnlyDictionary<int, string> reasonByUnitId); } // "reason:n,..." sorted by reason
```

**Steps:**
- [ ] Tests first (`ForesightTelemetryTests`): `IssueRowNamesUnitRoleTargetObjectiveAndOrigin`, `IssueRowsNeverExceedTheDailyCap`, `TraceCarriesEachCandidatesCoordinates`, `ReservesRowNamesCapitalAndHoldShares`, `ReservesRowSignatureUnchangedWhenNothingChanges`, `DefenseFilterReasonsAreTalliedAndSorted`, `NewRowsAreRegisteredInTheTagPolicy`.
- [ ] Implement; the mover adds an issue row exactly where it records `MoveOutcome.Issued` or `Retried` (not `Queued`, not refused).
- [ ] Gates; commit `feat(foresight): mover issue rows, trace coordinates, reserve and #25 reason rows [F1]`.

**Verification:** the gates above. **Smoke rows:** `[CampaignMoverIssue] alliance=1 unit=Army_of_Virginia:<id> role=DepotBuilder reason=depot-task target=1101,-554 objective=-1 from=1247,-803 readiness=<n>` (the shape; the baseline save's own case); `[Reserves] alliance=1 capital=<n>/<n> hold=<k>:<n>/<n>`; `DefenseIntent` filter signature `alliance=1|removed=8|forbidden=8|reasons=asset-proximity-local-only:<n>,...`. **Rollback:** none needed (rows only); `Logging Profile` governs them. **PROVE-MODE plants:** drop the target from `IssueRow` -> `IssueRowNamesUnitRoleTargetObjectiveAndOrigin` red; emit a row per queued move -> `IssueRowsNeverExceedTheDailyCap` red.

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
    internal static DefenseFilterMode ModeFor(int allianceId, bool flagOn, bool freshAllocation);
}
```

**Steps:**
- [ ] Tests first: `ObservesWhileThePlannerRunsForTheAlliance`, `FiltersWhenTheFlagIsOff`, `FiltersWhenThePlanIsStale`, `FiltersForEurope`, `FiltersForACicSide` (empty allocation is not fresh), `ObservedRowCountsWhatWouldBeRemoved`, and a source pin `ObservingPrefixRemovesNothingAndPostfixRevertsNothing` (the Prefix returns before `ownUnits.RemoveAt`, the Postfix finds no snapshot).
- [ ] Implement: in `Observe` the Prefix computes `CollectForbiddenIds`, emits `DefenseIntent` with `decision=filter-observed` and `wouldRemove=`, `reasons=` (signature-bounded per alliance), takes no snapshot and removes nothing.
- [ ] Gates; catalog row; commit `feat(foresight): #25 observes while the planner runs, vanilla's interception restored [F2]`.

**Smoke rows:** `DefenseIntent` `decision=filter-observed` `alliance=1 wouldRemove=<n> reasons=<...>`; no `filter-candidates` row for alliances 0 and 1 while the planner runs; vanilla defensive operations appear in the snapshot (`[Reserves] vanillaDefWithheld=0` from F6 on; until then the `InDefensiveOps` count in `[Allocation] busy=`). **Rollback:** `Enable Vanilla Defensive Interception = false`. **PROVE-MODE plants:** return `Observe` for a stale plan -> `FiltersWhenThePlanIsStale` red; remove from `ownunits` in observe mode -> the source pin red.

## F3 — Enemy picture, hold need and release, capital reach and floor (requirements enemy-total, picture-partition, hold-need, hold-release, candidate-floor-capacity, capital-window, capital-floor)

**Why:** root causes 1 and 5: one sighting switched the Manassas sector's presumption off, released its hold and scouted the target; the capital threat counted nothing beyond 200, so Washington (389 from Richmond) never counted; the floor had no baseline.

**Owned files:** `Strategic/Planner/CampaignSnapshot.cs` (`EnemyTroopsFielded`, `EnemyCapitalX/Z`, `HasEnemyCapital`, reset), `Strategic/Planner/CampaignSnapshotRuntime.cs` (`ReadScalars`: `GameVars.alliance[1 - alliance].troopsfielded` and `.capital`), create `Strategic/Planner/EnemyPicture.cs` (pure), `Strategic/Planner/TheaterPlanner.cs` (`Inputs.Picture`; `PresumedAt` -> picture; `HoldNeedAt`; `BuildSectorFacts`; `HoldThreatAtTarget`; `CapitalThreat`; eligibility floor; `Trace` `anchors=`), `Strategic/Planner/ReserveRules.cs` (`PictureThreat`: vanilla's weight over picture elements within a reach; `VanillaThreat` stays for G8), `Strategic/Planner/ForceAllocator.cs` (`SectorMayGiveScout`'s belief-driven test of requirement-hold-need), `Strategic/Planner/CampaignPlannerContracts.cs` (`TheaterPlan.CapitalReach`, `TheaterPlan.PictureRow` is not added: the rows are built by the runtime from `EnemyPicture`), `Strategic/Planner/CampaignPlannerRuntime.cs` (`[EnemyPicture]`, `[CapitalThreat]` rows), `Patches/DefensiveOpsPatch.cs` (`[CapitalFloor]` adds `reach=` from the published plan), `Plugin.cs` (`Enable Strategic Foresight Picture`), `Telemetry/TelemetryTagPolicy.cs` (`EnemyPicture`, `CapitalThreat`), test `EnemyPictureTests.cs`, the rewritten pins (Global constraints), csproj line, census rows.

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
// TheaterPlanner: CapitalReach = max(RelocationRange, ReinforcementMaxDays x MarchTime.SpeedPerDay(Speed, 15, Speed.MountedBaseSpeed[15], Speed.RoadCost)).
```

**Steps:**
- [ ] Tests first (`EnemyPictureTests`), the investigation's geometry as the fixture (Richmond 1263.6,-1010.9; Washington 1349.9,-631.6; Manassas 1245,-681; Winchester 1107,-555; Romney 982.6,-515.7; relocation range 200; minimum strength 2000):
  - picture: `EveryManIsPlacedOnce` (believed + faded + unaccounted = total), `AFadingBeliefKeepsItsStrengthWhereLastSeen`, `AnExpiredBeliefReturnsItsMenToTheAnchors`, `CapitalTakesItsShareWhenUnscouted`, `ObjectiveAnchorsShareByImportance`, `AScoutedAnchorPassesItsShareOn`, `EveryAnchorScoutedLeavesTheRestAtTheCapital`, `UnknownTotalSpreadsNothing`, `AfloatBeliefsAddNoLandMass`, `AlliancesTwoToFourIgnored`.
  - D1/D2 superseded: `EstimateIsBelievedPlusPresumed` (not max), `AScoutedTargetStillCarriesUnscoutedAnchorsNearIt`, `ASightingAt165DoesNotSwitchOffWashington` (the 2nd Division near Winchester leaves the Washington anchor undisplaced and the Manassas sector's need above 0.6 x its presumption), `HoldNeedHoldsUnseenMenAtTheHoldRatio`, `FloorUsesOnlyUndisplacedImportance`, `BalanceStillRanksByTheBelievedLayer`.
  - release: `AHoldIsReleasedOnlyWhenEveryElementItFacesIsAtTheTarget` (Romney does not release the Manassas hold while the Washington anchor is in its reach), `ASectorFacingNothingReleasesNothing`.
  - eligibility: `ACapacityBelowTheLiveMinimumIsIneligible`, `OddsRuleUnchangedAboveTheMinimum`.
  - departments and D9: `DepartmentNeedCountsEachElementOnce`, `ASectorHoldingAgainstABelievedArmyStillRefusesAScout`, `AnAnchorAloneMakesASectorFace`.
  - capital: `CapitalReachIsTheLargerOfRangeAndTenRoadDays`, `WashingtonCountsTowardRichmondsFloor`, `FloorHasVanillasBaseline`, `ThreatExcludesTheBaseline` (no enemy: floor = minimum, threat01 = 0, no trigger), `FloorIsNeverBelowFourVanillaNeed` (the same weight over a subset), `RailsNotCreditedInTheReach`.
  - flag off: `FlagOffRestoresTheD2Presumption` (the old rule through the flag, so the rollback is real and tested).
- [ ] Implement `EnemyPicture` and wire it in `TheaterPlanner.Evaluate` (built after `CapitalThreat` reads it; one build per `Evaluate` call; the second terrain pass reuses it) and in `HoldNeedAt` (D8's post).
- [ ] Rewrite the superseded pins (Global constraints) in the same commit.
- [ ] Rows: `[EnemyPicture] alliance total believed faded unaccounted anchors displaced capitalShare top`, `[CapitalThreat] alliance reach days corpsPerDay weighted floor threat elements`, on change of signature; `CampaignPlan:trace` `anchors=`.
- [ ] Gates; census; commit `feat(foresight): enemy picture, per-anchor presumption, capital reach and floor [F3]`.

**Smoke rows (002/A, CSA):** `[EnemyPicture] alliance=1 total=<Union troopsfielded> believed=<n> faded=<n> unaccounted=<n> anchors=<n> displaced=<n> capitalShare=0.40 top=Washington:<n>;...`; `[CapitalThreat] alliance=1 reach=<at least 200> days=10 corpsPerDay=<x> weighted=<above 0> floor=<at least 2000> threat=<0..1>`; `[CapitalFloor] alliance=1 floor=<at least vanillaNeed> reach=<x>`; `CampaignPlan:trace` with `est=` above the sighting alone near Romney. **Rollback:** `Enable Strategic Foresight Picture = false`. **PROVE-MODE plants:** `max` instead of the sum -> `EstimateIsBelievedPlusPresumed` red; per-sector `FreshLandBeliefWithin` switch restored -> `ASightingAt165DoesNotSwitchOffWashington` red; drop the baseline -> `FloorHasVanillasBaseline` red; baseline in threat01 -> `ThreatExcludesTheBaseline` red.

## F4 — Spotting marks on both sides (requirements marks-player-side, marks-ai-side, mark-beliefs; decision 2)

**Why:** the CSA's beliefs held 2 of about 15 Union groups; the player's own map showed the rest as marks (the Army of Occupation's 207 marks, the 1st Division at 1346,-671 and 1198,-614). Vanilla gives the AI no such channel (142938).

**Owned files:** create `Strategic/Planner/MarkRules.cs` (pure: `BandRound`, `MarkRadius`, the creation chance, the offset), create `Strategic/Planner/MarkLedger.cs` (pure, per alliance, `CampaignScope`), `Strategic/Planner/EnemyDetectionRuntime.cs` (player side: read `SpottedEnemy`; AI side: drive `MarkLedger` with the fog probe), `Strategic/Planner/EnemyArmyModel.cs` (mark application, `BaseRadius`, `RadiusAt`), `Strategic/Planner/CampaignPlannerContracts.cs` (`DetectionSource.Mark = 5`, `DetectionSources.FromSaved`, `EnemyDetection.MarkDay`, `MarkRadius`, `EnemyBelief.BaseRadius`), `Strategic/Planner/TheaterPlanner.cs` (`IsScouted`, `FreshLandBeliefWithin` skip `Mark`), `Strategic/Planner/ReconPlanner.cs` (the probe's "answered" test skips marks if it reads beliefs directly; check `ProbeWanted`), `PersistenceDto.cs` / `SidecarDtoCodec.cs` (`baseRadius`), `Plugin.cs` (`Enable Spotting Mark Beliefs`), `Telemetry/TelemetryTagPolicy.cs` (nothing new: `[EnemyBeliefs]` gains `marks=`), `docs/bug-fixes/README.md` (log `BUG-INTEL-001`, status "not fixed; question-intel-bug"), test `SpottingMarkTests.cs`, csproj lines, census.

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
    internal static void Offset(System.Random rng, float intel, float[] distanceVariation, bool hasClosestEnemy, float awayAngleDeg,
                                float ageYears, float prevOffsetX, float prevOffsetZ, out float dx, out float dz);  // 142695-142712
}
internal sealed class MarkLedger
{
    // One entry per enemy group key: the newest mark's day, position and offset. Evaluate once per review per hidden enemy group.
    internal bool Evaluate(string unitKey, float x, float z, int daySerial, float dateYears, in MarkInputs inputs, System.Random rng, out EnemyDetection mark);
    internal void Drop(string unitKey);            // detected by rule a-d, in battle
    internal void PruneUnseen(int daySerial);      // absent for PlannerState.PruneAfterMissedReviews reviews
}
```

**Steps:**
- [ ] Tests first (`SpottingMarkTests`): `BandRoundingMatchesVanillasBands`, `MarkRadiusFromStoredIntelligence`, `CreationChanceCappedAtVanillasMaximum`, `BugIntel001NegativeTimeConstantFloorsAtAThousandthOfAYear`, `NoMarkUntilTheGroupMovesFiftyUnits`, `OffsetPointsAwayFromTheClosestEnemy`, `OffsetBlendsWithThePreviousByAge`, `SeededRollsAreReproducible`, `NoUnityRandomInTheMarkChannel` (source pin), `MarkMatchesByExactKeyOnly`, `MarkNeverRetiresAnotherBelief`, `SameDaySightingBeatsAMark`, `OlderMarkIgnored`, `MarkBeliefNeverScouts`, `MarkBeliefCountsInTheTotals`, `MarkRadiusGrowsWithDrift`, `MarkSourceRoundTripsInTheSidecar` (5 and `baseRadius`), `AnOlderFileReadsBaseRadiusZero`, `PlayerSideReadsOnlyTheNewestMark`, `PlayerAllianceUnitsNeverRegisteredByVanilla` (documents 142938), `GarrisonsAndFleetsGetNoMark`.
- [ ] Implement; the AI-side probe reuses G1's masked `UnitIsHidden` loop (gated on `usefow`, W&L incidents masked) and runs inside `campaign.planner.detect`.
- [ ] Gates; census; bug log; commit `feat(foresight): spotting marks as beliefs on both sides [F4]`.

**Smoke rows:** `[EnemyBeliefs] alliance=<a> mode=FogGrid|OwnSideWithinFog seen=<n> marks=<n> contact=<n> remembered=<n> expired=<n>` with `marks>0` on both sides in a W&L career; the CSA's beliefs include the Union's Army of Occupation in the West (the `[EnemyPicture]` top or the West sector's `[Reserves] hold=` need). **Rollback:** `Enable Spotting Mark Beliefs = false`. **PROVE-MODE plants:** let a mark scout -> `MarkBeliefNeverScouts` red; let step 4 run for marks -> `MarkNeverRetiresAnotherBelief` red; `UnityEngine.Random` in `MarkLedger` -> the source pin red.

## F5 — Live minimum strength (requirement-live-min-strength; decision 6)

**Why:** `ForceAllocator.MinGarrisonStrength = 5000` claims to mirror vanilla's minimum, which is 2000 in 002/A. Lands after F3 (decision 6): the picture must already hold the unseen men before every floor shrinks 2.5 times.

**Owned files:** `Strategic/Planner/CampaignPlannerContracts.cs` (`TheaterPlan.MinGarrisonStrength`), `Strategic/Planner/TheaterPlanner.cs` (set it; `NeedFor`, `HoldNeedAt`, the presumption floor read it; `MinStrengthFallback`), `Strategic/Planner/ForceAllocator.cs` (delete the const; `Target`, `SectorMayGiveScout` read the plan), `Strategic/Planner/ReinforcementRouting.cs` (reads the plan), test `LiveMinimumStrengthTests.cs`, the rewritten pins, census.

**Steps:**
- [ ] Tests first: `PlanPublishesTheScenariosMinimum`, `FallbackOnlyWhenTheReadIsNotPositive` (one warning), `EveryFloorDerivesFromThePlan` (a source pin: no `5000f` and no `MinGarrisonStrength` const left under `Strategic/Planner/`), `ReinforcementRoutingReadsThePlansMinimum`, `HoldTargetIsTheLiveMinimumInAQuietSector`.
- [ ] Implement; the first-review row (`CampaignPlannerRuntime.LogFirstReview`, `[CampaignPlanner] first review`) gains `minStrength=<n>`.
- [ ] Gates; commit `fix(foresight): the scenario's live minimum strength, one definition site [F5]`.

**Smoke rows:** `[CampaignPlanner] first review alliance=1 ... minStrength=2000` (002/A); `[Allocation] need=` floors at 2000. **Rollback:** `Enable Campaign Planner`. **PROVE-MODE plant:** restore the const in `Target` -> `HoldTargetIsTheLiveMinimumInAQuietSector` red.

## F6 — Covering force and the Redeploy role (requirements covering-post, covering-size, covering-members, covering-post-choice, covering-hold, covering-lifecycle, redeploy-role; decision 4)

**Why:** nothing held the approach between the capitals (investigation (c), (h)); the capital floor guards the capital itself (`CapitalGuard` marches to it, `ForceAllocator` `AssignCapitalGuards`).

**Owned files:** `Strategic/Planner/CampaignSnapshot.cs` (`Places`, `PlaceRecord`), `Strategic/Planner/CampaignSnapshotRuntime.cs` (`ReadPlaces`: `BattleUnits.towns` with `Owner`, `BattleUnits.smalltowns` with `allianceowner`; rail counts through the snapshot's `LinesNearTown` / `LinesOfIip`), create `Strategic/Planner/CoveringForce.cs` (pure: corridor, posts, need, verdict), `Strategic/Planner/ReserveRules.cs` (`Covering`; `Hold` takes the covering, committed and excluded masks), `Strategic/Planner/TheaterPlanner.cs` (`Reserve` calls it; `MassFor` excludes members except for the enemy-capital candidate; publish `TheaterPlan.Covering`), `Strategic/Planner/PlannerState.cs` (`CoveringKeys`, `CoveringForward`, `CoveringVerdictDays`, `CoveringVerdictKnown`, `CoveringPostKey`, written once in `Plan` after `Decide`), `Strategic/Planner/CampaignPlannerContracts.cs` (`FormationRole.Redeploy = 9`, `CoveringPlan`, `TheaterPlan.Covering`), `Strategic/Planner/ForceAllocator.cs` (`AssignCovering`; `Summary` `covering=` `redeploy=`; D9's, the depot's and the posts' pools exclude `covering-force`), `Strategic/Planner/CampaignMover.cs` (the (role, reason) priority of requirement-redeploy-role; `MovesRole`), `Strategic/Planner/CampaignWithholdView.cs` (`Read` also yields the plan; the covering defence clause; `Redeploy` withheld from offense), `Strategic/Planner/CampaignPlannerRuntime.cs` (`[CoveringCorridor]`, `[CoveringForce]`, `[Reserves] covering= vanillaDefWithheld=`), `PersistenceDto.cs` / `SidecarDtoCodec.cs` (the three covering fields), `Plugin.cs` (`Enable Covering Force`), `Telemetry/TelemetryTagPolicy.cs` (`CoveringCorridor`, `CoveringForce`), test `CoveringForceTests.cs`, csproj lines, census.

**Vanilla to read:** 78940, 82518-82535, 103719, 143173-143220 (`Town.Owner`, `IsCapital`, `CityName`), 62230, 11668-11869, 13460-13504 and 13610 (what vanilla's defence takes), 14080-14158; the snapshot's rail index (`CampaignSnapshot.cs` `LinesNearTown`, `LinesOfIip`).

**Interfaces:**
```csharp
public struct PlaceRecord { public string Name; public float X, Z; public int Owner; public int RailLines; public bool IsTown; }
public sealed class CoveringPlan
{
    public string PostName; public float X, Z; public bool Forward; public float Need, Held, Radius;  // Radius = RelocationRange (the defence clause)
    public List<int> InstanceIds = new List<int>();
    public string Reason;   // null when posted; "no-capital", "no-corridor-place", "flag-off", "cic-side" otherwise
}
internal static class CoveringForce
{
    internal static void Corridor(CampaignSnapshot s, List<int> intoPlaceIndexes);   // band 0.5 x range, strictly between, rail >= 1
    internal static int ForwardPost(CampaignSnapshot s, List<int> corridor);         // nearest the enemy capital at >= 0.5 x range from it, -1
    internal static int RearPost(CampaignSnapshot s, List<int> corridor);            // nearest the own capital at >= range from it, -1
    internal static float Need(CampaignSnapshot s, EnemyPicture p, float holdRatio, float minStrength, ITerrainLineProbe probe);
    internal static bool Verdict(bool forwardMet, PlannerState state);               // 3-review hysteresis on CoveringForward
}
```

**Steps:**
- [ ] Tests first (`CoveringForceTests`; fixture: the 002/A places of `IIPsTowns.dat` 1861-06-27 listed in D11, with rail counts as **assumed** in the fixture and named so): `CorridorBandAndBetweenness`, `ForwardPostIsManassasJunctionIn002A`, `FairfaxInsideTheStandoffIsNotForward`, `RearPostIsFredericksburgIn002A`, `KingGeorgeWithoutRailIsNotRear`, `ScenarioOneRangeMovesTheRearPost`, `NoCorridorPlaceNoCoveringForce`, `NeedIsTheHoldRatioOfTheEnemyCapitalsEstimate`, `NeedNeverBelowTheLiveMinimum`, `IncumbentsKeepTheirPlace`, `MembersNearestThePostWithTheHomeDiscount`, `PoolLimitedToTenDaysMarch`, `ForwardWhenMetElseRear`, `VerdictNeedsThreeReviews`, `ACapitalGuardIsNeverACoveringMember`, `MapGroupPlayerNeverTheOnlyHolder`, `PlayerCommandNeverAMemberAndCountsNothing`, `CarryingGroupMayBeAMember`, `BusyFormationsAtThePostCount`, `MembersCountInThePostsSectorOnly`, `MassForExcludesMembersExceptAgainstTheEnemyCapital`, `ScoutDrawDepotAndPostsNeverTakeAMember`, `MemberAtPostHoldsWithheldElseRedeploys`, `DefenceMayTakeAMemberAgainstAnEnemyNearThePost`, `OffenceNeverTakesAMember`, `RedeployMovePriorityOrder`, `RedeployNeedsNoTempoRelease`, `CicSideHasNoCoveringForce`, `CoveringStateRoundTripsInTheSidecar`, `UnionSideCoversToo` (the rule is symmetric).
- [ ] **Runtime step one: the corridor census.** `[CoveringCorridor] alliance=<a> candidates=<name:t:dOwn:dEnemy:rails;...> forward=<name|none> rear=<name|none>` on the first review and on change. In the first smoke of 002/A (checklist step 3), if the CSA's forward post is not Manassas Junction or its rear is not Fredericksburg, stop and raise `question-corridor-posts` with the row; do not tune the rule to the names.
- [ ] Implement the rest; the mover's priority becomes a (role, reason) list.
- [ ] Gates; census; commit `feat(foresight): covering force between the capitals, Redeploy role [F6]`.

**Smoke rows (002/A):** `[CoveringCorridor] alliance=1 candidates=Manassas Junction:0.82:331:116:<n>;...;Fredericksburg:0.51:209:199:<n>;... forward=Manassas Junction rear=Fredericksburg`; `[CoveringForce] alliance=1 post=Manassas_Junction kind=forward|rear reason=- need=<n> held=<n> members=<unit+unit> short=<n> verdictDays=<n>`; `[CampaignMoverIssue] alliance=1 role=Redeploy reason=covering-force target=1245,-681`; `[Allocation] covering=<n> redeploy=<n>`; `[Reserves] covering=<held>/<need> vanillaDefWithheld=0`; a Union `[CoveringForce] alliance=0` row as well. **Rollback:** `Enable Covering Force = false`. **PROVE-MODE plants:** let `MassFor` take members for any target -> `MassForExcludesMembersExceptAgainstTheEnemyCapital` red; drop the AI-holder rule -> `MapGroupPlayerNeverTheOnlyHolder` red; serve `Redeploy return-home` before `MainEffort` -> `RedeployMovePriorityOrder` red.

## F7 — Main effort commitment, sorties and return home (requirements commitment, commitment-mover, return-home)

**Why:** root cause 4: the CSA main effort went initial, no-candidate (`cap=0`), initial, no-candidate in six days because the march dropped readiness below step 2; after the drop every formation held where it stood, far from home.

**Owned files:** `Strategic/Planner/PlannerState.cs` (`Sorties`: key -> decision, objective, role, origin; prune with the departments), `Strategic/Planner/TheaterPlanner.cs` (`Massable` = `MassEligible` or committed to the candidate's objective, read by `MassFor` and the covering pick; the committed and returning masks into `ReserveRules.Hold`; `TheaterPlan.ReturningIds` and `Returns` after `Decide`), `Strategic/Planner/ReserveRules.cs` (returning formations count 0, as `Recovers`), `Strategic/Planner/CampaignPlannerContracts.cs` (`TheaterPlan.Returns`, `ReturnMove`), `Strategic/Planner/ForceAllocator.cs` (`AssignReturning`), `Strategic/Planner/CampaignMover.cs` (record sorties at first dispatch; no new march at step < 2, `HeldReadiness`; the readiness wait in `JudgeLastReview`), `Strategic/Planner/MoveTracker.cs` (a `Judge` variant that leaves an issued record unjudged while the formation's step is below 2, up to `QueuedExpiryDays`), `Strategic/Planner/CampaignPlannerRuntime.cs` (`[Reserves] committed= returning=`), `PersistenceDto.cs` / `SidecarDtoCodec.cs` (`sorties`), `Plugin.cs` (`Enable Main Effort Commitment`), test `CommitmentTests.cs`, census.

**Vanilla to read:** 14068-14078, 14091, 91336-91350, 113937-113965, 173418-173433.

**Steps:**
- [ ] Tests first (`CommitmentTests`): `ADispatchedMemberStaysMassedThroughAReadinessDip`, `RetreatOrLowMoraleStillRelieves` (14072), `LowSupplyStillRecovers`, `AnUncommittedFormationStillNeedsStepTwo`, `NoNewMarchAtStepOne`, `AnIssuedMoveIsNotJudgedWhileReadinessIsLow`, `TheReadinessWaitEndsAfterFourteenDays`, `SortieRecordedAtFirstDispatchOnly`, `OriginKeptAcrossReissues`, `AnEndedDecisionSendsItsSortiesHome`, `TheNewMassTakesFirst`, `ArrivedSortieIsDeleted`, `GaveUpSortieHoldsInPlace`, `AReserveMayTakeAReturningFormation`, `ReturningCountsInNoHold`, `ReturnHomeNeverCrowdsOutTheMass` (the cap), `PlayerCommandNeverHasASortie`, `SortiesRoundTripInTheSidecar`, `FlagOffRestoresD5`.
- [ ] Implement; the investigation's chain as a fixture test: `TheCsaMainEffortSurvivesTheMarch` (06-22 dispatch, readiness falls to step 1 on 06-24, the decision keeps `cap>0`).
- [ ] Gates; census; commit `feat(foresight): dispatched corps stay in the main effort, stranded corps go home [F7]`.

**Smoke rows:** `[Reserves] alliance=1 committed=<n> returning=<n>`; `[CampaignMover] ... heldReadiness=<n>`; no `[CampaignPlan] alliance=1 switch=no-candidate` while `committed>0`; `[CampaignMoverIssue] reason=return-home target=<origin>` after a decision ends. **Rollback:** `Enable Main Effort Commitment = false`. **PROVE-MODE plants:** drop `Committed` from the mass test -> `ADispatchedMemberStaysMassedThroughAReadinessDip` red; judge low-readiness records -> `AnIssuedMoveIsNotJudgedWhileReadinessIsLow` red.

## F8 — Department shortfall reinforcement (requirement-department-reinforcement; decision 5)

**Why:** the West (Porterfield's 1,402 men, Recover) had no way to be reinforced: D6's path serves only the main effort, #125 routes recruits within 300 of the recruiting cell.

**Owned files:** `Strategic/Planner/TheaterPlanner.cs` (`DepartmentReinforcement` after the mass; `TheaterPlan.DepartmentMoves`; the in-flight mask; `DepartmentNeed` is F3's), `Strategic/Planner/PlannerState.cs` (`DepartmentInFlight`; transfer back on give-up), `Strategic/Planner/CampaignPlannerContracts.cs` (`DepartmentMove`, `TheaterPlan.DepartmentMoves`), `Strategic/Planner/ForceAllocator.cs` (`AssignDepartmentMoves`), `Strategic/Planner/CampaignPlannerRuntime.cs` (`[DepartmentReinforcement]`, `[Reserves] deptShort=`), `PersistenceDto.cs` / `SidecarDtoCodec.cs` (`departmentInFlight`), `Plugin.cs` (`Enable Department Reinforcement`), `Telemetry/TelemetryTagPolicy.cs` (`DepartmentReinforcement`), test `DepartmentReinforcementTests.cs`, census.

**Steps:**
- [ ] Tests first: `TheShortestDepartmentGetsOneFormation`, `TheDonorsHoldStaysMet`, `SoonestWithinTenDaysRailCredited`, `OneInFlightPerReceivingDepartment`, `AtMostOnePickPerReview`, `ThePickTransfersTheDepartmentAtOnce`, `ArrivalClearsTheRecord`, `GiveUpTransfersBack`, `PrunedFormationClearsTheRecord`, `NeverAReserveMassCoveringGarrisonOrBuilder`, `NeverThePlayersCommandOrMapGroup`, `WorksWithNoMainEffort` (`main=none`), `DepartmentInFlightRoundTripsInTheSidecar`.
- [ ] Implement; gates; census; commit `feat(foresight): a short department gets one formation at a time [F8]`.

**Smoke rows:** `[Reserves] alliance=1 deptShort=West:<need>/<have>`; `[DepartmentReinforcement] alliance=1 unit=<name> from=East to=West target=<x,z> days=<d> state=Picked`, later `state=Arrived`; `[CampaignMoverIssue] reason=department-reinforcement`. **Rollback:** `Enable Department Reinforcement = false`. **PROVE-MODE plants:** allow two in flight -> `OneInFlightPerReceivingDepartment` red; skip the donor test -> `TheDonorsHoldStaysMet` red.

## F9 — Depot builders from ready surpluses (requirement-depot-builder)

**Why:** root cause 3: the Army of Virginia (unready, the Fredericksburg screen) was the smallest free formation of the depot site's sector.

**Owned files:** `Strategic/Planner/ForceAllocator.cs` (`SmallestIn`: `MassEligible`, the surplus test on the first pass, the last-body rule on the second), test `DepotBuilderRuleTests.cs`.

**Steps:**
- [ ] Tests first: `AnUnreadyFormationNeverBuilds`, `ABuilderLeavesItsSectorsHoldMet`, `TheMassGivesABuilderOnlyWhenNoSurplusExists`, `TheMassKeepsItsLastBody`, `NeitherHoldReserveNorCoveringBuilds`, `TheInvestigationsArmyOfVirginiaIsNotPicked` (fixture: the 06-23 sector with the Army of Virginia at readiness step 1).
- [ ] Implement; gates; commit `fix(foresight): depot builders only from ready surpluses [F9]`.

**Smoke rows:** `[CampaignMoverIssue] role=DepotBuilder readiness=<2 or 3>`. **Rollback:** `Enable Campaign Planner`. **PROVE-MODE plant:** drop `MassEligible` -> `AnUnreadyFormationNeverBuilds` red.

## F10 — Integration, docs, smoke

- [ ] Merge F1-F9 in order into `feat/strategic-foresight`; full gates; `PlannerPatchesReadOnlyPublishedState`, `PlannerReadCensusTests`, `PipelineOrderPinned` (add the picture build and the department step to the named Plan job calls) green.
- [ ] Docs: spec D11 "as built" notes (deviations only); `docs/patch-catalog.md` #25 row and the planner runtime rows (picture, marks, covering, department); `docs/handoff.md`; `docs/telemetry.md` (every new row and field); `docs/bug-fixes/README.md` (`BUG-INTEL-001`); `AGENTS.md` campaign write owners (the `Redeploy` role, the covering force's defence clause) and patch hygiene (#25's observe mode).
- [ ] Build, deploy and verify the deployed DLL (`stat` and `sha256sum` of `dist/WhiskeyRealism.dll` and the plugin copy must match) before any smoke.

## Smoke checklist (in order; a fresh 002/A W&L career from 1861-06-02, the player below map level, 30 game days, `Logging Profile = TacticalTuning`)

1. After F1+F2: every alliance-0/1 #25 row is `filter-observed`; `[CampaignMoverIssue]` rows name unit, role and target.
2. After F3: `[EnemyPicture] alliance=1` shows the Union's `total`; the conservation holds (test-plan count TC-PIC-CONSERVE); `[CapitalThreat] alliance=1 weighted>0 floor>=2000`. **Gate (the user's):** the integrator reports the measured `total`, the Washington anchor's share and the would-be covering need (`CoveringForce.Need` from the row's numbers) and asks `question-capital-share` before F6 merges.
3. After F4: `[EnemyBeliefs] marks>0` on both sides.
4. After F6: `[CoveringCorridor]` names Manassas Junction / Fredericksburg for the CSA (else `question-corridor-posts`, stop); `[CoveringForce] alliance=1 short=0` on most reviews (criterion-covering-held); no covering member in a `MainEffort`, `Support` or `DepotBuilder` issue row (criterion-axis-not-stripped).
5. After F7-F9: criteria mass-kept, return-home, department-reinforced, builders-fit.
6. Whole run: criterion-review-budget (`campaign.planner.review` under 4 ms per alliance at 20x); no exception; no `[CampaignMoverIssue]` naming the W&L player's own command.

## Coverage (requirement -> slice)

| Requirement (D11) | Slice |
|---|---|
| requirement-telemetry | F1 (mover, trace, reserves, #25 reasons), F3-F8 (their rows) |
| requirement-interception | F2 |
| requirement-enemy-total, requirement-picture-partition, requirement-hold-need, requirement-hold-release, requirement-candidate-floor-capacity, requirement-capital-window, requirement-capital-floor | F3 |
| requirement-marks-player-side, requirement-marks-ai-side, requirement-mark-beliefs | F4 |
| requirement-live-min-strength | F5 |
| requirement-covering-post, requirement-covering-size, requirement-covering-members, requirement-covering-post-choice, requirement-covering-hold, requirement-covering-lifecycle, requirement-redeploy-role | F6 |
| requirement-commitment, requirement-commitment-mover, requirement-return-home | F7 |
| requirement-department-reinforcement | F8 |
| requirement-depot-builder | F9 |

Open questions routed by this plan: `question-capital-share` (gate after F3's smoke), `question-corridor-posts` (F6 runtime step one), `question-intel-bug` (user, any time; F4 mirrors vanilla until decided).

<!-- author-sweep: applied purpose-advantage-measurement, fit-criteria, guardrails, read-paths, roles, lifecycle, exceptions, concurrency, decision-records; n/a: none -->
