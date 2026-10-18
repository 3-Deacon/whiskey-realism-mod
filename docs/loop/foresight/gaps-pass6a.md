# D11 build dry run, pass 6a: phase 1 (F1, F2, F3, F4, F4b, F5, F9, F10a)

Builder seat, dry run. Worktree `.worktrees/plan-foresight` at `13418736`. S = `docs/superpowers/specs/2026-10-14-campaign-planner-design.md` (D11, lines 245-439), P = `docs/superpowers/plans/2026-10-18-strategic-foresight.md`, T = `docs/loop/foresight/test-plan.md`. Shipped files are under `src/WhiskeyRealism/`. The vanilla source is `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` (present). No repository file was edited.

**I hit the 20-gap cap.** To stay under it I merged related findings:
- GAP-11 covers the F1/F2 ownership text and the `ObservedLog` registration.
- GAP-13 covers the signature changes that break call sites and `PipelineOrderPinned`.
- GAP-14 covers every row-field mismatch.
- GAP-19 covers small naming and signature choices.

## Gaps (20: 0 blocker, 10 major, 10 minor)

**GAP-01. `MarkLedger.Evaluate` has no way to ask the grid, so vanilla's refusal and drop cannot be built or tested.**
- **What S requires.** S:290 *Fidelity* says the one-argument `fows[1].UnitIsHidden(position)` mirrors two vanilla checks:
  - Vanilla refuses a mark whose new position the grid sees (`AddMark`, 142724: `oppositefow.UnitIsHidden(val)`, where `val` is the true position plus the freshly drawn offset).
  - Vanilla drops every mark when the grid sees the newest mark's position (`UpdateMarks`, 142800-142807: `RemoveMarks` and return).
- **What P provides.** P names the test `AGridSeenMarkIsRefusedAndDropsTheMarks` (P:214, T:29 TC-MARK-FIDELITY). The pure interface (P:204-210) is `Evaluate(unitKey, x, z, daySerial, dateYears, in MarkInputs, System.Random, out EnemyDetection)`. `MarkInputs` (P:195-199) has no grid field and no callback.
- **The refusal cannot be supplied.** The position to test exists only inside `Evaluate`, after the angle and magnitude draws (142700-142701), so the runtime cannot precompute the answer. The drop needs the stored newest position, and the ledger declares no accessor for it.
- **Grid missing or broken.** S/P do not say whether the AI-side channel runs when the grid is absent or broken. Shipped code then detects by contact only (`DetectionRules.GridUnavailable`, EnemyArmyModel.cs:544-547; `_gridBroken`, EnemyDetectionRuntime.cs:160-162, 442-446).
- Severity: major
- Impact: the refusal cannot be implemented with the declared signature, and the pin cannot be written red-first against it. A builder will either skip the refusal (marks inside the grid's view) or invent an interface.
- Assumption:
  - `Evaluate` takes one more parameter, `Func<float, float, bool> gridHidden` (the masked one-argument `UnitIsHidden`). It is called once for the stored newest position (a visible position means drop and return false), and once for the candidate position after the offset draws (a visible position means refuse: no new entry, and the stored newest mark is still returned).
  - With the grid missing or broken (`_gridBroken`), the AI side creates no mark and returns stored ones unchanged.
- Grounding: vanilla / shipped code

**GAP-02. The user gate after F4 needs a number no phase-1 row carries; it depends on phase 2's covering need.**
- **What P requires.** At the gate after F4 (P:357), the integrator reports "the would-be covering need computed from the rows (`0.6 x` the estimate at the enemy capital, under each share)". S:426 (question-capital-share) frames the choice on that number. S:302 defines it as `HoldRatio' x Estimate(enemy capital, R)`, where `Estimate` = believed land threat plus `Presumed` within `R` of the enemy capital.
- **What the phase-1 rows carry.**
  - `[EnemyPicture]` (S:361) carries totals, `capitalShare=`/`capitalShareProp=` and the top 3 anchors' masses.
  - `[CapitalThreat]` weighs elements around the *own* capital.
  - No row carries the believed land strength or the faded mass within `R` of the *enemy* capital.
- **Why that matters after F4.** S:297 says the weight then "moves from the anchor to the believed Union corps near Washington", which is exactly the part no row reports. The only source of the number is `[CoveringForce] need=/needProp=`, which is F6b (phase 2).
- Severity: major
- Impact: the phase-1 gate cannot be presented as P:357 describes. This is a phase-1 slice silently depending on a phase-2 artifact.
- Assumption: F3 adds `enemyCapitalEst=<n>` and `enemyCapitalEstProp=<n>` to `[EnemyPicture]`: `LandThreatNear(enemy capital, R) + Presumed(enemy capital, R)` under `CapitalShare` and under the proportional share, 0 with no enemy capital. The gate reads `0.6 x` each.
- Grounding: shipped code / none

**GAP-03. `TheEastCanDonateToAShortWestIn002A` depends on F8's donor rule; S contradicts itself on D6's `DepartmentHoldMet`.**
- **What the test plan asks.** The F3 test (P:154, T:20 TC-HOLD-EASTDONOR: "three eastern sectors, a Washington anchor, a short West") asserts a donation to a short department.
- **Phase 1 has no such path.** The only department test it has is D6's `DepartmentHoldMet(c, department, excluded)` (TheaterPlanner.cs:955-968), used only by the main effort's reinforcement pick (1029). Giving a formation to a *short department* is F8 (S:335: `DepartmentHave`, the donor test `DepartmentHave(donor) - HoldStrength >= DepartmentNeed(donor)`), and P:356 itself says "the donation itself is phase 2's F8".
- **S contradicts itself.**
  - S:283 amends D6 (d): "`DepartmentHoldMet`'s need is `DepartmentNeed(d)` = ...". P:49's flag-off list agrees ("D6's sum of `SectorFacts.Need` as the department need" is what the flag restores).
  - S:335 says "D6's `DepartmentHoldMet` stays as shipped for D6".
- Severity: major
- Impact: the F3 test's subject is undecided. A builder either pulls F8's donor rule into phase 1 or writes a test that does not do what its name says. Whether F3 changes D6's reinforcement pick depends on which S line governs.
- Assumption:
  - S:283 governs: F3 replaces `DepartmentHoldMet`'s need with `DepartmentNeed`. S:335's "as shipped" means only the `have` side.
  - F3's test asserts `DepartmentHoldMet(East, excluded = the formation)` is true on FX-002A, so the East can give D6's reinforcement. Rename it `TheEastKeepsItsHoldWithoutADonorIn002A`, and leave "short West" to F8.
- Grounding: shipped code

**GAP-04. The capital reach's relief race leaves distance, probe, rails and "no estimate" open.**
- **What S defines.** S:295 sets `ReliefDays` = "the largest `MarchTime.EstimateDays` to the own capital among the own `MassEligible` formations" of the capital's home department. It says "Rails are not credited for the enemy".
- **What it leaves open.** `EstimateDays(m, f, toX, toZ, effectiveDistance, s, probe, ownIips)` (MarchTime.cs:66-88) needs four choices S does not make:
  - the distance (straight, or `RiverLines.EffectiveDistance`);
  - the probe (null, or `c.Probe`);
  - whether the *own* rail leg is credited (it is whenever `UseRails && TrainCovers`, 81-86);
  - what to do with a formation whose estimate is `float.MaxValue` (no speed, 70). Taken as "the largest", it makes the reach infinite.
- **The test does not settle rails.** P's test `RailsNotCreditedInTheReach` (P:161, T:25) reads either way: enemy side only, or both sides.
- **Ordering.** `CapitalThreat` runs before `c.OwnIips` is set inside `Evaluate` (TheaterPlanner.cs:321 vs 323).
- Severity: major
- Impact: crediting own rails shrinks `ReliefDays`. One fast train leg can bring the reach back to 200 and drop Washington (389) out of Richmond's threat again, which is root cause 5. The pin names an ambiguous rule.
- Assumption:
  - Use the straight distance and probe null with `c.OwnIips`, as `ReinforcementPick` does (TheaterPlanner.cs:1030-1031).
  - Credit no rail leg for either side in the reach (call with `s = null` so the rail branch is skipped), and say so in the pin. This deliberately departs from the plain reading of S:295 ("not credited for the *enemy*" implies the own side is credited, as `EstimateDays` does). The reason is that own rails can only shrink the reach below the march race that root cause 5 needs, and the pin's name (`RailsNotCreditedInTheReach`) already reads as both sides. The author should either confirm this or keep own rails and accept the collapse-to-200 risk named under Impact.
  - Skip a formation whose estimate is `MaxValue`.
  - Compute `OwnIips` before `CapitalThreat`.
- Grounding: shipped code

**GAP-05. The capital floor's `VanillaNeed` term has no specified inputs, so S's "floor never below #4's `vanillaNeed`" is not delivered.**
- **What S claims.** S:296 sets `CapitalFloorStrength = max(Min + CapitalFloorRatio x weighted, CapitalFloorPolicy.VanillaNeed(...))` and says the `[CapitalFloor]` row's `floor=` and `vanillaNeed=` "can no longer contradict each other". P:168 sets the smoke row to `floor=<at least vanillaNeed>`.
- **How #4 computes its need.** #4 calls `VanillaNeed` (DefensiveOpsPatch.cs:111-119) at vanilla's own call time with:
  - the live `GamePrefs.aiminimumstrengthformovement` (2000 in 002/A);
  - `BeliefsFor`;
  - own units read with `groupstrengthactive` (`LiveFormationReader.Read(..., activeStrength: true, ...)`, LiveFormationReader.cs:14-17, 36).
- **What F3 has.** F3 has the snapshot: `FormationRecord.Strength` is `groupstrengthdirect` (CampaignPlannerContracts.cs:56), and the minimum is `MinStrength(c)`, which is 5000 until F5. S/P do not say which minimum, strength measure or own list F3 passes.
- **What the pin checks.** The pin `FloorIsNeverBelowFourVanillaNeed` (P:161, T:25) can only check the `max()`.
- Severity: major
- Impact:
  - Own strength subtracted at direct strength, and positions read at review time rather than #4's call time, can make F3's term smaller than #4's. The smoke row can then show `floor < vanillaNeed`.
  - Passing 5000 against #4's 2000 inflates the floor before F5.
- Assumption:
  - F3 passes `MinStrength(c)`, `s.Beliefs`, morale 1, `s.CapitalX/Z`, `s.RelocationRange` and `s.Own`.
  - S's claim is restated as "never below `VanillaNeed` on the review's snapshot".
  - P:168's smoke row becomes "floor at least vanillaNeed when the row and the review share a day; small gaps are timing".
- Grounding: shipped code

**GAP-06. D8's garrison post need: "the post counting as a sector of its own state" is undefined.**
- **What S says.** S:283 says "`HoldNeedAt` (D8's secession post) uses the same assignment, the post counting as a sector of its own state".
- **Shipped order.** The post need is computed in `Garrisons` (TheaterPlanner.cs:868-905, need at 890-892). `Garrisons` runs inside `Reserve` *after* `BuildSectorFacts` and after `ReserveRules.Hold` has already reserved against `Facts.Need` (TheaterPlanner.cs:322, 1254-1255).
- **Two readings, both open.**
  - If the post competes with the facing sectors for the nearest assignment, the sector needs already built (and reserved on) would have to lose those elements, which is a second pass.
  - If the post does not compete, it counts elements a sector already counts, which contradicts "each presumed element counts in one sector only".
  - "Of its own state" names no rule either: which elements may it take, and is its reach `RelocationRange` (as shipped) or something else?
- **Coverage.** No test names the post.
- Severity: major
- Impact: the Union's Kentucky and Missouri garrison need (D8) is built either double-counted or by an invented rule, and no pin catches it.
- Assumption:
  - The post does not compete. Its presumed term is the `Faded` and `Anchor` elements within `RelocationRange` of the post whose assigned sector is *not* a facing sector (elements no sector holds against), weighted `min(1, R / EffectiveDistance)`.
  - Need = `max(HoldRatio' x (threat + that), Floor(post, R))`.
  - Add the pin `SecessionPostCountsNoElementASectorHolds`.
- Grounding: shipped code

**GAP-07. The anchor holder rule lets Europe's objectives carry the major enemy's unseen men, and `AlliancesTwoToFourIgnored` names no subject.**
- **What S says.**
  - S:271 says "alliances 0 and 1 only; Europe is not counted".
  - S:277 makes an anchor "every enemy-held objective record (`ObjectiveRecord.HolderAlliance` neither the side nor -1, not accomplished)". That includes a record held by alliance 2 or above, so U (the enemy `e = 1 - alliance`'s unseen men) would be spread onto a European-held objective.
- **What the test does not say.** P:155's `AlliancesTwoToFourIgnored` (T:16 TC-PIC-ANCHORS) does not say which of these it pins:
  - a snapshot of alliance 2-4 builds an empty picture (and `Plan` already returns for alliances above 1, TheaterPlanner.cs:201);
  - objectives held by 2-4 are not anchors;
  - `troopsfielded` of 2-4 is never read.
- Severity: major
- Impact: the test cannot be written red-first without choosing its subject. The anchor rule as written is a plausible wrong build.
- Assumption: anchors are the enemy capital and the objective records whose `HolderAlliance == e` (`1 - alliance`). `AlliancesTwoToFourIgnored` pins both of these:
  - an alliance-2-held objective is no anchor and takes no share;
  - a snapshot of alliance 2 builds total unknown with nothing spread.
- Grounding: shipped code

**GAP-08. `[EnemyPicture] top=` must name anchors, but no anchor carries a name; a fit criterion keys on it.**
- **What S and P require.**
  - S:361 requires `top=<anchor:mass;...(3)>`.
  - P:168's smoke example writes `top=Washington:<n>`.
  - criterion-capital-threat-live (S:396, T:26) counts "review days on which `[EnemyPicture] alliance=1 top=` lists the capital anchor".
- **What exists.**
  - `PictureAnchor` (P:130) has `X, Z, Importance01, Share, IsCapital, Displaced` and no label.
  - `ObjectiveRecord` (CampaignPlannerContracts.cs:117-133) has only `ObjectiveId`.
  - The snapshot reads no capital name (CampaignSnapshot.cs:24-25; ReadScalars, CampaignSnapshotRuntime.cs:170-189).
- **Two further problems with the criterion.**
  - A displaced capital or a share of 0 takes the capital out of `top=` even while the Union holds Washington.
  - The capital may not rank in the top 3.
- Severity: major
- Impact: the criterion's read path cannot identify the capital anchor, and the smoke example cannot be produced.
- Assumption:
  - The snapshot reads `EnemyCapitalName` (`Town.CityName`, 143192, quoted, censused).
  - Anchor labels are that name for the capital and `obj<ObjectiveId>` for objective anchors.
  - The criterion's condition becomes "`HasEnemyCapital`", reported as a new field `enemyCapital=<name|none>`, instead of "listed in `top=`".
- Grounding: shipped code / vanilla

**GAP-09. F1's dedupe and every-review tests live in runtime files with no seam, and P puts `date=` in the wrong place.**
- **What P says.** P:66 lists `TheaterPlanner.Summary` and `ForceAllocator.Summary` as F1's `date=` sites, while P:83 and S:357 say the date goes "after their signature compare".
- **Where the compare actually happens.** The compare is on `Summary`'s output in the runtime (`EmitPlan` CampaignPlannerRuntime.cs:399-401, `EmitAllocation` 426-427, `Report` EnemyDetectionRuntime.cs:227-235). A date inside `Summary` defeats the dedupe.
- **The tests have no behavioural form.**
  - `DatedRowsKeepTheirDedupe` (P:82, T:69 TC-TEL-DATE) and `ReservesRowEveryReview` (TC-TEL-TAGS) exercise emitters in `CampaignPlannerRuntime` and `EnemyDetectionRuntime`, which are not in the test csproj (EnemyDetectionRuntime.cs:14).
  - P does not declare them source pins or give them a pure seam. This is the same class as pass-5 GAP-13.
- Severity: major
- Impact: the tests either cannot be written red-first or become source pins of unstated shape. A builder following P:66 puts the date inside `Summary` and breaks the one-row-per-change contract.
- Assumption:
  - `Summary` stays dateless.
  - A pure `ReviewCadenceCatchUp.FormatDate(serial)` is appended by each emitter after its compare.
  - `DatedRowsKeepTheirDedupe` is a source pin over the three emitters: the compare happens before the append.
  - `ReservesRowEveryReview` is a source pin: `[Reserves]` is emitted in `ReviewPlan`'s Full path after `EmitAllocation`, with no compare.
- Grounding: shipped code

**GAP-10. F9: a Marching depot builder whose readiness drops below step 2 is either dropped mid-march or re-issued against criterion-builders-fit.**
- **The rule.** S:345 and P:242 make `SmallestIn` take only a `MassEligible` formation (readiness step 2 and above).
- **Who gets re-picked.**
  - A Marching task's builder is not pinned. "The allocator still re-picks it" every review (DepotTaskLedger.cs:285-291), and the ledger follows the allocation (`FollowPlan`, 113, 362).
  - Only a Wished builder is pinned (`AssignPinnedBuilders`, ForceAllocator.cs:438-449).
- **The undecided case.** S/P do not decide what happens to a builder picked ready on day 1 whose readiness falls to step 1 on the march (002/A readiness dips on a march were cause 4).
  - If the readiness test applies to incumbents, `SmallestIn` drops it, another formation is sent, and the first holds wherever it stands: churn, with the task re-bound.
  - If incumbents are exempt, the mover's re-issue writes `[CampaignMoverIssue] role=DepotBuilder pick=new readiness=1`, which criterion-builders-fit (S:417) counts as a violation.
- Severity: major
- Impact: either reading is a plausible build, and each fails the other's expectation. No test covers the incumbent.
- Assumption: an incumbent Marching builder (`HeldBuilder.Wished == false`, same task site) keeps its pick while `TheaterPlanner.Movable` holds, readiness aside. `pick=` reads `pinned` for it too (reason `depot-task-pinned` for every held builder), so the criterion counts only new picks. Add the pin `AMarchingBuilderKeepsItsTaskThroughAReadinessDip`.
- Grounding: shipped code

**GAP-11. Stale ownership text for the `[Reserves]` fields, and two homes for `ObservedLog`'s reset.** This was pass-5 GAP-11, folded only in part.
- **Who builds the defence counts.**
  - P:83 and S:360 say F1 builds `vanillaDefWithheld`, `vanillaDefHoldOrCovering` and `massInVanillaDef`.
  - The interface comment P:75-76 still assigns `vanillaDefWithheld=` and `vanillaDefHoldOrCovering=` to F6b and `massInVanillaDef=` to F7.
  - F6b's owned-file line P:285 lists `[Reserves] ... enemyOnOwnSoil= vanillaDefActive= vanillaDefWithheld= vanillaDefHoldOrCovering=` again, although P:115 gives `enemyOnOwnSoil`/`vanillaDefActive` to F2.
- **F2's owned files.** F2 adds those two fields (P:115), but its owned files (P:92) list neither `ReservesRow.cs` nor `CampaignPlannerRuntime.cs`.
- **Where `ObservedLog` registers its reset.**
  - The interface (P:107) places `CampaignScope.Register("defense-filter-observed-log", ...)` on the pure `ObservedLog`.
  - P:115 and S:362 say the registration is made "from the patch's static constructor".
  - The patch is not in the harness, so `ObservedRowDedupesPerCampaign` (reset by `CampaignScope.ResetAll`) passes only if `ObservedLog` registers itself. `ScopedState.cs` is in the test csproj (csproj:519), so the self-registered form is testable.
- Severity: minor
- Impact: contradictory ownership lines. The tests force the right answer once the builder sees them red.
- Assumption:
  - P:83/S:360 govern. P:75-76 and P:285 are corrected to "F1 builds them; F6b adds the `covering-force` reason only".
  - F2's owned files gain `ReservesRow.cs`.
  - `ObservedLog` registers its own reset from its static constructor, per AGENTS.md's "owner's static constructor" rule.
- Grounding: shipped code

**GAP-12. `PlannerSettings.ForesightPicture` has no stated initial value.**
- **What S and P say.** S:349 and P:124 add it, "refreshed by the coordinator beside `PlanTrace`; tests set it in try/finally".
- **The shipped default.** The shipped `PlanTrace` is `internal static bool PlanTrace;`, which defaults to false (PlannerSettings.cs; refreshed at StrategicCoordinator.cs:796).
- **The consequence.** Declared the same way, every harness test that does not set the flag runs the flag-off (D2) path. That includes every new F3 pin and F5's `PlanPublishesTheScenariosMinimum`: with the flag off, `MinGarrisonStrength` is always 5000.
- Severity: minor
- Impact: a whole slice's pins run on the rollback path unless each sets the flag. Red tests reveal it, but the fix is undecided.
- Assumption: `internal static bool ForesightPicture = true;` (the all-on rule). Only the flag-off tests set it false, in try/finally.
- Grounding: shipped code

**GAP-13. The signature changes F3 and F4 make break shipped call sites and a pin that P assigns only to F10a.**
- **No optional out parameter in C#.** S:272 hands the picture back through "an optional `out EnemyPicture picture` parameter of `Plan`". C# has no optional `out` parameter, and `Plan` ends with the optional `gaveUp`/`heldBuilders` (TheaterPlanner.cs:195-197). A required `out` before them changes the 63 `TheaterPlanner.Plan(` call sites (tests in 11 files).
- **`Detect` needs the seed.** F4 passes `CampaignSeed` into `Detect` (S:290, P:174). `PipelineOrderPinned` pins the exact call string `"EnemyDetectionRuntime.Detect(snapshot, GameVars.playeralliance, EnemyDetectionRuntime.FogHonestFlagsOn());"` (CampaignExecutorTests.cs:1283).
- **The pin goes red mid-phase.** F4's own gate (exit 0, P:28) goes red. P names `PipelineOrderPinned` only in F10a (P:252), and lists `CampaignExecutorTests.cs` in neither F3 nor F4.
- Severity: minor
- Impact: re-pins land in the wrong slice, or the 63 sites churn.
- Assumption:
  - Add an overload, `Plan(..., out string trace, out EnemyPicture picture, gaveUp, heldBuilders)`, and have the existing signature call it.
  - F4 re-pins the `Detect` line of `PipelineOrderPinned` in its own commit (named in the message). F1 adds its capture and `[Reserves]` lines there too.
- Grounding: shipped code

**GAP-14. Row field lists disagree between P and S, and two `[CapitalThreat]` fields have no source.**
- **`[EnemyPicture]` field lists.**
  - P:165's row is `alliance total believed faded unaccounted anchors displaced capitalShare top`.
  - S:361's is `total believed faded unaccounted unplaced anchors displaced capitalShare capitalShareProp seen marks top`.
  - TC-PIC-CONSERVE (T:17) needs `unplaced`.
  - P does not say which slice adds `seen`/`marks`. P:218 shows them after F4, but F4's owned files (P:174) do not include the row's emitter.
- **What the share fields report.** It is not stated whether `capitalShare=` is the constant 0.40 (P:168 prints `0.40`) or the share the capital actually took (0 when displaced, 1 when no objective anchor). `EnemyPicture` (P:131-143) has no field for the proportional share.
- **`[CapitalThreat]` fields with no source.**
  - `relief=` and `corpsPerDay=` (S:361, P:165) are carried by no published member. Phase 1 adds only `CapitalReach`, `CapitalWeighted` and `CapitalThreat01` (P:31), and S:295's "`CapitalReach` carries ... `reach=`, `relief=`" cannot recover days from a `max()`.
  - `elements=` is undefined.
- Severity: minor
- Impact: a builder following P:165 omits `unplaced`, and the conservation count fails.
- Assumption:
  - S:361's list governs; F3 emits `seen`/`marks` as 0 until F4 fills them.
  - `capitalShare=` is the realized fraction of U given to the capital; `capitalShareProp=` is the same fraction under the proportional rule (`EnemyPicture.CapitalShareProp`).
  - Add `TheaterPlan.CapitalReliefDays` and `CapitalCorpsPerDay` (censused, read by `CampaignPlannerRuntime`).
  - `elements=` is the count of picture elements inside the reach.
- Grounding: shipped code

**GAP-15. ReconPlanner's minimum: P's plumbing does not match the code, and the picture-off rollback changes recon.**
- **The plumbing.**
  - S:349 and P:228 say ReconPlanner reads `plan.MinGarrisonStrength` "through its `Context`" and that "`ReconPlanner.Context` gets the plan". The `Context` already holds `Plan` (ReconPlanner.cs:717-733).
  - The only minimum read is the static `CanMove(s, f, detachment)` (497). Five call sites in ReconPlanner (143, 476, 521, 545, 564) and `ForceAllocator` (through `CouldScout`, ForceAllocator.cs:837, 857) reach it without a `Context`.
- **The census.** The census row `"CampaignSnapshot.MinStrengthForMovement | P/ReconPlanner.cs"` (PlannerReadCensusTests.cs:702) must move.
- **The other raw read.** `FlagsOf(..., s.MinStrengthForMovement)` (CampaignSnapshotRuntime.cs:376, vanilla's `UnitIsFightingForce` mirror) is another raw read S neither lists nor exempts.
- **The rollback.** With the picture flag off, the plan's minimum is 5000 (P:49), but shipped recon read the raw 2000 in 002/A. So the "as before D11" rollback is not, for recon.
- Severity: minor
- Impact: undecided plumbing and an inexact rollback.
- Assumption:
  - `CanMove`/`CouldScout` take `float minStrength`, and every caller passes `plan.MinGarrisonStrength`.
  - `FlagsOf` stays on the raw read (it mirrors vanilla's own predicate). The census row moves to `P/TheaterPlanner.cs`.
  - The rollback note in P:49 names recon's 5000.
- Grounding: shipped code

**GAP-16. `[EnemyBeliefs] remembered=` "subtracts" the applied marks, which double-counts today's marks.**
- **What S says.** S:291: "`marks=` the applied marks, `remembered=` subtracts them".
- **Shipped `remembered`.** It counts beliefs with `LastSeenDay != today` (EnemyDetectionRuntime.cs:223-225).
- **The problem.** An AI-side mark created this review has `MarkDay == today` and is not in `remembered` to begin with. Subtracting the applied-mark count would therefore under-count, and can go negative.
- Severity: minor
- Impact: a wrong `remembered=` on the AI side every review.
- Assumption: `remembered` = beliefs with `LastSeenDay != today` whose key is not in `AppliedMarkKeys` (a set exclusion, not a count subtraction).
- Grounding: shipped code

**GAP-17. F4b's tests have no pure subject, and its gating and owned files are incomplete.**
- **The tests.** The tests `FixedTimingUsesTimeFactorSpottings`, `BothSidesShareOneFormula` and `FlagOffKeepsVanillasTiming` (P:222, T:33) test nothing in the harness as declared:
  - the patch is not compiled there;
  - `MarkRules.CreationChance` takes `spotting0/1` as plain parameters (P:186), so it is the same formula whichever pair is passed.
- **The owned files.** The pair's selection happens where `MarkInputs` is filled (`EnemyDetectionRuntime`), which F4b does not own (P:222 lists the patch, the bug log, `Plugin.cs`, `MarkRules.cs` and the test).
- **The gating.** S/P do not say whether the fix depends on `Enable Campaign Planner`. It changes the W&L player's own map whether or not the planner runs.
- Severity: minor
- Impact: tautological tests, or tests of unstated shape.
- Assumption:
  - Add a pure `MarkRules.TimingPair(bool fix, float[] spottingFactor, float[] timeFactorSpottings, out float s0, out float s1)`, called by both the patch Prefix and the runtime. The three tests exercise it, plus a source pin that both call sites use it.
  - F4b owns `EnemyDetectionRuntime.cs`'s fill line.
  - The patch is gated on its own flag and `GameVars.usefow` only, like the shipped bug-fix patches.
- Grounding: shipped code / vanilla

**GAP-18. F9's surplus measure leaves three cases open, and two named pins cannot be written red-first.**
- **The surplus measure.** S:345 counts formations that "hold the sector as Hold or CapitalGuard". It is open whether a `CapitalGuard` standing outside the capital's sector counts in the sector it stands in. `Counted` (ForceAllocator.cs:995-1002) counts only `standing == role sector`.
- **The subtraction.** "Less this formation" does not say whether it subtracts `HoldStrength` or `Strength`.
- **Two pins.**
  - `NoHoldReserveBuilds` (P:245, T:56) is green on shipped code: hold reserves are `_done` before `AssignDepotBuilders` (ForceAllocator.cs:320-333, `Free` 597-600). P does not mark it as a regression pin.
  - `TheInvestigationsArmyOfVirginiaIsNotPicked` needs "the 06-23 sector" composition, but `investigation.md` gives no roster, strengths or readiness for it, only the Army of Virginia's role and target (investigation.md:29, 64).
- Severity: minor
- Impact: small rule choices, and fixture data that has to be invented.
- Assumption:
  - Use the `Counted` rule (`standing sector == role sector`), and subtract `HoldStrength`.
  - `NoHoldReserveBuilds` is a regression pin.
  - The Army of Virginia fixture is built minimally (that army at step 1 and the smallest formation, a ready alternative, and the sector need met), and is named as an assumption in the test.
- Grounding: shipped code

**GAP-19. Naming and signature choices S and P state differently or not at all.**
- `ReviewCadenceCatchUp.DateOf`: S:357 has `DateOf(daySerial)`; P:66 has `DateOf(serial, out y, out m, out d)` plus `FormatDate`.
- The mark-date decode: S:289 calls it `MarkRules.MarkDate`; P:191 calls it `MarkRules.MarkDay(float timeshown, int todaySerial)`.
- `MarkRules.PlayerSideMark(int relation, ...)` (P:193) takes an `int`, where the shipped type is `UnitRelation` (EnemyArmyModel.cs:446-451).
- `ForceAllocator.CountedHold`/`RoleStrength` (S:360, P:66) have no signature. The pass's `Counted` works over private arrays (ForceAllocator.cs:995-1009), while `ReservesRow` sees only the published `Allocation`.
- The trace's `anchors=<n>` (S:359, P:165) does not say whether it is per candidate (anchors within `R` of the target) or the picture's count.
- Severity: minor
- Impact: naming only. Each has one obvious answer.
- Assumption:
  - P's `DateOf(serial, out y, out m, out d)` + `FormatDate(serial)`.
  - `MarkRules.MarkDay` (S renamed).
  - `PlayerSideMark(UnitRelation, ...)`.
  - `CountedHold(CampaignSnapshot, SectorModel, Allocation, int sector)` recomputes `HoldSectorOf`, and the pass calls it from `ListShortfalls` after the assignments are written.
  - `anchors=` is per candidate: undisplaced anchors with a share above 0 within `R` of the target.
- Grounding: shipped code

**GAP-20. A mark of an embarked army counts as a land army at sea.**
- **What S says.** S:289 gives a mark "`Traits` without `Embarked`", so that it never triggers the landing inference.
- **Vanilla marks embarked armies.** `AddMark` refuses only fleets (`unittyp == 17`, 142724), so an army group afloat (`currentmodeltype` 1/2, `SnapshotRules.IsEmbarked`) is marked.
- **What follows from no `Embarked` trait.** Its belief is not `Afloat` (`ReserveRules.Afloat`, ReserveRules.cs:63-66), so it becomes a `Live` land element at a sea position. It then counts in `LandThreatNear`, the hold needs, the department need and the land balance, which S:279/S:283 keep afloat beliefs out of.
- **The original reason has gone.** Since the pass-5 GAP-03 fold, the landed branch of `IsSeaInvasionThreat` skips `Mark` beliefs anyway (S:289), so dropping the trait no longer serves its stated purpose.
- Severity: minor
- Impact: Union sea-borne forces (the Peninsula) would raise land hold needs at the coast and in the picture's land layer.
- Assumption: a mark carries `Embarked` when the marked group is embarked (afloat elements, never land). The landed branch already skips mark beliefs, so no landing is inferred. Add the pin `AnEmbarkedGroupsMarkIsAfloat`.
- Grounding: shipped code / vanilla

## Pass-5 folds checked

- GAP-01 (offset direction): adequate. S:258 says "toward", with the worked case. P:189 and T:29 pin `OffsetPointsTowardTheClosestEnemyAsVanillaDoes`.
- GAP-02 (in-fog predicate): adequate. S:290's first sentence now defers to *Fidelity*.
- GAP-03 (mark readers): adequate. S:289 names the three skips. P:174 owns `EnemyBeliefReadings.cs` and `ReconMissionLedger.cs`. The pins are in P:214. A side effect is logged as GAP-20.
- GAP-04 (same-day collision): adequate. S:291's second pass; P:202-203.
- GAP-05 (`Evaluate` returns the stored mark; what `marks=` counts): adequate. S:290-291.
- GAP-06 (`MarkDay` decode): adequate. The decode is spelled out in S:289 and both pin dates are in P:214. The name differs (-> GAP-19).
- GAP-07 (pure player-side seams): adequate. `NewestMark` and `PlayerSideMark` are pure; the source pin and the regression pin are stated (P:192-193, 214).
- GAP-08 (F3's minimum helper): adequate. `TheaterPlanner.MinStrength(Inputs)` (S:349, P:124, 153).
- GAP-09 (`pick=`): adequate. `ReasonPinnedBuilder` (P:66), the interface (P:72), S:358. The only `ReasonDepotTask` readers are tests (ForceAllocatorTests.cs:520 new path; ConstructionSteeringTests.cs:1207, re-pinned). The Marching-builder case is new (-> GAP-10).
- GAP-10 (`date=`): inadequate (-> GAP-09). The rows are listed, but P puts the date in `Summary`, before the runtime's compare, and the dedupe tests have no seam. `DateOf`'s signature differs (-> GAP-19).
- GAP-11 (F1 builds the defence counts): inadequate (-> GAP-11). P:83 and S:360 are folded; P:75-76 and P:285 still contradict them.
- GAP-12 (F4b patch shape and labels): adequate for the shape, label, `smoke-situational` entry and catalog row (P:39, 222, 252). The test seams are logged as GAP-17.
- GAP-13 (F2 pure seams): adequate. `WouldRemove`, `ObservedSignature` and `ObservedLog.ShouldEmit` are pure (S:353, P:98-110). The registration-site contradiction is logged as GAP-11.
- GAP-14 (seed before `Detect`): adequate (S:290, P:174, `TheSeedIsSetBeforeDetect`). The `PipelineOrderPinned` re-pin is logged as GAP-13.
- GAP-15 (`ReservesRow` sums; review days): adequate. `CountedHold`/`RoleStrength` (S:360); review day = Full-mode review (S:357, T:11). The signature is logged as GAP-19.
- GAP-16 (flag path into the pure planner): adequate. `PlannerSettings.ForesightPicture` (S:349, P:124). Its initial value is logged as GAP-12.
- GAP-17 (F5's readers; warning trigger): adequate for the reader list, the warning on the raw read and `LogFirstReview` getting the plan. Inadequate for ReconPlanner's plumbing (-> GAP-15).
- GAP-18 (observed row with nothing to remove; its category): adequate. S:353 gives `Gate` and `wouldRemove=0` emitted.
- GAP-19 (`seen`/`remembered`/`marks`): adequate (`AppliedMarkKeys`, appended fields, S:291). The `remembered` arithmetic is logged as GAP-16.
- GAP-20 (`MarkInputs`, `hasPrevious`, draw order, ledger home, prune unit): adequate for all four items (P:186-210). The grid probe the interface still lacks is new (-> GAP-01).

## Checked and clean

- **F1, issue-row cap.** A row only when `moved` gives at most `MaxMovesPerDay` a review, and a refused retry writes none (MoveIssue.cs:21-49; `Dispatch`, CampaignMover.cs:346-350).
- **F1, `pick=` source.** `Assignment.Reason` is on the published assignment (CampaignPlannerContracts.cs:461-471), so `IssueRow` can map it to `pick=` with no new field. The two builder paths are ForceAllocator.cs:446 and 611.
- **F1, date inverse.** `ReviewCadenceCatchUp` is in the test csproj (csproj:429). `DayOfMonth` (ReviewCadenceCatchUp.cs:38-47) already holds the civil-date inverse that `DateOf` extends.
- **F1, rows with no dedupe.** `[CampaignMover]` and `[CampaignMoverMiss]` have no dedupe (CampaignMoverRuntime.cs:56-59), so appending `date=` to them is free. `CampaignPlan:trace` already carries `date` (CampaignPlannerRuntime.cs:405-409).
- **F1, yesterday's allocation.** `Publish` overwrites the working copy (CampaignPlanStore.cs:93-99) and `AgeRecon` reads it before then (CampaignPlannerRuntime.cs:323-330), so capturing the previous allocation and plan before `Publish` is well defined.
- **F1, tag registration.** `AddCampaign(tag, category)` (TelemetryTagPolicy.cs:161-180) covers `CampaignMoverIssue`, `Reserves`, `EnemyPicture` and `CapitalThreat`. `ExtractTag` takes the bracketed name exactly.
- **F2, mode inputs.** `TryFreshAllocation` (CampaignWithholdView.cs:161-184) covers a fresh plan, executors allowed and Europe (`MoveTrackers.For(2)` null). `ExecutorsEnabled` reads the two planner flags (CampaignPlannerRuntime.cs:479-482).
- **F2, observe path.** The Postfix revert is gated on `_defensiveOpsBefore` (CheckForDefensiveOperationsCandidateFilterPatch.cs:208), which only the filter path fills (135). The `forbidden.Count == 0` return (111) matches S:353's "emitted whenever the forbidden set is non-empty".
- **F3, `troopsfielded`.** It is a float field (62368), reset at 81515 and summed at 81567 for `unittyp <= 13` with `transfertime <= 0`. `Alliance.capital` is a `Town` (62230), as S:257 and S:271 say.
- **F3, corps road rate.** `MarchTime.SpeedPerDay` (MarchTime.cs:48-54) multiplies the base speed by the group speed, as vanilla does (133991), so `SpeedPerDay(m, 15, MountedBaseSpeed[15], RoadCost)` is the corps ceiling rate. The index-15 guard must sit outside the call, as S:295 says.
- **F3, eligibility in `Retain`.** `Retain`'s structure (TheaterPlanner.cs:474-494) admits the capacity-below-minimum return before the `BelowOddsDays` counter. `Decide` then drops the objective through `HeldRecord`/`BestOf` (586-627) and `Trigger`'s "odds" reason (516-519).
- **F3, vanilla weight.** `ReserveRules.VanillaThreat`'s weight (ReserveRules.cs:47-60) is the weight `PictureThreat` reuses. Afloat beliefs use the straight distance, as S:295 says.
- **F3, terrain probing.** `ReviewTerrain` tags each terrain question with the last `Mark(group, id)` (RiverLines.cs:84-101). New element distances asked inside `CapitalThreat` and `BuildSectorFacts` therefore inherit the capital and sector groups, and need no new probe order.
- **F4, vanilla draws.** The draw order and the 50-unit gate measured from the newest mark's displayed position (`startpos`, 142795 and 142816; the gate at 142866; the roll is the first operand of `&&`, so it is always drawn). Also the perk-3 loop over `enemyinrange` within `commanderrange` (142831-142844), and the age blend (`AddMark` 142706-142712). All match S:258/S:290.
- **F4, player alliance.** `AddUnit` returns for the player's alliance (142938), so the player side reads only enemy groups.
- **F4, readers of `s.Detections`.** The only readers are `Report` and `Update` (grep over `src`), so appending marks to it reaches no other "seen today" reader.
- **F4b, the swapped arrays.** `timefactorspottings` is a `float[2]` loaded at 55804-55806 and never read. `spottingfactor` is read only at 101203 and 142819. The swap shape of P:222 is therefore safe.
- **F5, readers of the minimum.** The F5 reader list is complete against grep: ForceAllocator.cs:789, 990; TheaterPlanner.cs:769, 1046, 1322; ReinforcementRouting.cs:162; ReconPlanner.cs:497, plus `FlagsOf`, logged as GAP-15. The only `5000f` is ForceAllocator.cs:86.
- **F9, shipped depot pins.** `DepotBuilderIsSmallestFormationInSector` (ForceAllocatorTests.cs:503-521) and `GaveUpDepotBuilderIsReplacedByTheNextFormation` (CampaignExecutorTests.cs:325-343) still hold under the surplus rule: 19,000 standing against a 5,000 target, fixtures at readiness step 3 (ForceAllocatorTests.cs:1887).
- **F10a, census scope.** The census covers `CampaignPlannerContracts.cs` and `CampaignSnapshot.cs` only (PlannerReadCensusTests.cs:27-33). The new `MarkRules`/`MarkLedger`/`EnemyPicture` files need no rows; the new `EnemyDetection`, `EnemyBelief`, `TheaterPlan` and `CampaignSnapshot` members do.
