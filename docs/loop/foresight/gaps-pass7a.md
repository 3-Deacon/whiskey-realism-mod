# D11 build dry run, pass 7a: phase 1

Builder seat, dry run, nothing built or edited. Worktree `.worktrees/plan-foresight` at `4193ea0e`. S = `docs/superpowers/specs/2026-10-14-campaign-planner-design.md` (D11, lines 245-439), P = `docs/superpowers/plans/2026-10-18-strategic-foresight.md`, T = `docs/loop/foresight/test-plan.md`. Shipped files are under `src/WhiskeyRealism/` (`Strategic/Planner/` unless named). Vanilla: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` (present). Scope: F1, F2, F3, F4, F4b, F5, F9, F10a.

## Gaps (16: 0 blocker, 7 major, 9 minor)

**GAP-01. Nothing says who writes each picture element's assigned sector and weight, or when.**
- **What S requires.** S:278 keeps, for each land element, "the facing sector it is assigned to (requirement-hold-need) with its weight", so that every later reader reads one assignment. S:283 assigns an element to "the nearest facing sector by centroid among the sectors whose reach holds it, the lower sector id on a tie", weighted `min(1, reach / EffectiveDistance)` from that sector's centroid. `DepartmentNeed` assigns `Live`, `Faded` and `Anchor` elements by the same rule (S:283).
- **What P provides.** The picture's only constructor is `EnemyPicture.Build(CampaignSnapshot s, Func<float,float,bool> isScouted)` (P:142). It takes no `SectorModel`, no facing set and no probe. P:170 adds `PictureElement { Kind, X, Z, Mass, AssignedSector, Weight }`. S:272 and P:167 build the picture once, before the first `Evaluate`.
- **Why that is not enough.**
  - Facing, reach and the river-effective weight exist only inside `BuildSectorFacts` (TheaterPlanner.cs:1217-1228). `Facing` reads `Threat`, which is weighted through the probe.
  - `Plan` runs `Evaluate` twice on a terrain-filling review (TheaterPlanner.cs:209 with `ReviewTerrain`, 218 with `Probe = null`).
  - An assignment computed in pass 1 and kept would carry probe-dependent weights into pass 2's needs.
  - Neither S nor P names the method that writes `AssignedSector`/`Weight`, says whether it is rewritten in each pass, or says whether "nearest by centroid" is a straight or an effective distance.
- Severity: major
- Impact: `ElementsCarryTheirAssignedSector`, `AnAnchorCountsInOneSectorsNeed` and `DepartmentNeedLandKindsWeightedTieToLowerSector` (T:16, T:20) have no declared subject. A builder could plausibly do any of three things: assign once in `Build` (impossible without sectors, so the builder invents parameters), assign once in pass 1 and reuse it, or keep a second assignment for the department need. Phase 2's `AxisShares` reads "their assignment (F3)" (S:303), so the choice made here carries forward.
- Assumption:
  - Add `EnemyPicture.AssignSectors(SectorFacts facts, SectorModel sectors, float relocationRange, ITerrainLineProbe probe)` and call it at the end of `BuildSectorFacts`, after `Facing` and `Radius` are set. It is re-run, and overwrites `AssignedSector`/`Weight`, in every `Evaluate` pass.
  - "Nearest" is the straight centroid distance (`RiverLines.Distance`). Reach membership is also straight (`<= Radius + R`). The weight uses `RiverLines.EffectiveDistance` with that pass's probe.
  - `Live` elements are assigned by the same call, but they enter only the department need (`Assigned(sector)` sums `Faded` and `Anchor` only).
  - The `[EnemyPicture]` row reads the last pass.
- Grounding: shipped code

**GAP-02. F9: the Marching incumbent's step, its target and its exemption from the surplus test are open, and a wrong target ends the task.**
- **What S says.** S:345: a Marching task's builder (`HeldBuilder.Wished == false`, at the task's site) "keeps its pick while it is `Movable`, readiness aside", with reason `depot-task-pinned`. P:253 restates this. P:249 gives F9 only `SmallestIn` in `ForceAllocator.cs`.
- **How the shipped allocator handles builders.**
  - Only Wished builders are pinned. `ReserveRules.WishedBuilders` skips `!held[k].Wished` (ReserveRules.cs:153), and `AssignPinnedBuilders` runs before every reserve (ForceAllocator.cs:320-323, 438-449). `HasPinnedBuilder` also tests `Wished` only (ForceAllocator.cs:622).
  - A Marching builder is an ordinary formation until `AssignDepotBuilders` (ForceAllocator.cs:333). By then `ReserveRules.Capital` or `Hold` may already have claimed it if it is ready (ForceAllocator.cs:323-327).
  - `HeldBuilder.TargetX/Z` for a Marching task is the ledger's site from the *last* review (DepotTaskLedger.cs:302-303).
  - `FollowPlan` moves the task to today's plan site (within `SiteMatchRadius` 25). It then abandons the task with `builder-retasked` unless an assignment sits within `AssignmentEpsilon` 1 of the *new* site (DepotTaskLedger.cs:362-391, 76-78).
- **What is undecided.**
  1. Is the incumbent claimed in the pinned step (ahead of the capital and hold reserves, which changes more than "readiness aside"), or in the depot step (where the hold reserve may already have taken a ready incumbent)?
  2. Is it assigned at `HeldBuilder.TargetX/Z`, or at the matching plan task's `X/Z`?
  3. Does it bypass the new surplus test when F3's larger needs leave its sector without a surplus?
- Severity: major
- Impact: assigning at the ledger's old site makes `BuilderAssignedAt` miss whenever the plan's site moved. The task is then abandoned the same review, which is the churn the rule exists to stop. Claiming the incumbent in the pinned step also takes a ready marching builder out of the capital and hold reserves, which no line authorises. `AMarchingBuilderKeepsItsTaskThroughAReadinessDip` cannot be written red-first until the step is fixed.
- Assumption:
  - The incumbent is claimed at the depot step, before `SmallestIn`'s first pass. It qualifies when it is still `Free` (no earlier role took it), is `Movable`, stands in the task's sector, and its `HeldBuilder.SiteX/Z` lies within `SiteMatchRadius` of the plan task.
  - It is assigned at the plan task's `X/Z` with `ReasonPinnedBuilder`. It is exempt from both `MassEligible` and the surplus test.
  - A reserve that took it first wins, as shipped.
- Grounding: shipped code

**GAP-03. A mark near the main objective can stop the probe while the target stays unscouted, so a staged push gets no scout.** This was pass-6a's "readers" area: S names the skip, but P leaves it as "check".
- **What S says.** S:291: `IsScouted` and `FreshLandBeliefWithin`, "and so anchor displacement, D1's release and D7's probe answer", skip `Mark` beliefs.
- **What the shipped code does.**
  - `ReconPlanner.ProbeWanted` does not call `IsScouted`. It returns false on any belief with `Confidence01 >= ProbeStaleConfidence` within `R` of the main target (ReconPlanner.cs:459-464).
  - `ProbeStaleConfidence` equals `ScoutFreshConfidence` (ReconPlanner.cs:63, TheaterPlanner.cs:101).
  - `ReconStep` also feeds `ConfidenceWithin(s.Beliefs, ...)`, marks included, to the probe's escalation gate (ReconStep.cs:106; ContactEvidenceLedger.cs:50).
- **What P says.** P:179 says only "the probe's 'answered' test skips marks if it reads beliefs directly; check `ProbeWanted`". No pin is named.
- **The consequence.** A fresh mark near the objective has confidence about 1; under BUG-INTEL-001 every moving hidden group gets one. That mark makes the probe unwanted, while `IsScouted` (marks excluded) keeps `MainScouted` false and `MainStaged` true. The result is the 2026-10-17 failure mode, a staged main effort with no probe.
- Severity: major
- Impact: a plausible silent wrong build, because S's "and so" reads as if the `IsScouted` change already covers it. No named test would go red.
- Assumption:
  - `ProbeWanted` skips beliefs whose `LastSource` is Mark. New pin: `AMarkNeverAnswersTheProbe`, a source pin or a `ReconPlanner` harness case.
  - The escalation gate's `BeliefConfidence01` keeps counting marks: a probe in contact already has a real detection.
  - Add both to F4's owned-file line for `ReconPlanner.cs` / `ReconStep.cs`.
- Grounding: shipped code

**GAP-04. Two retired test names still sit beside their replacements, and each contradicts its replacement.** This leaves the pass-6a GAP-20 and GAP-01 folds inadequate.
- **The Embarked pair.** P:219 still lists `AMarkCarriesNoEmbarkedTrait`. P:221 adds `AnEmbarkedGroupsMarkIsAfloat`, and T:29 lists both under TC-MARK-REVERSE. S:289 now says a mark carries `Embarked` "exactly when the marked group is embarked".
- **The grid pair.** P:219 still lists `AGridSeenMarkIsRefusedAndDropsTheMarks`. P:221 adds `GridSeenCandidateIsRefusedAndTheStoredMarkStays` and `GridSeenStoredMarkDropsTheEntry`, and T:29 lists all three under TC-MARK-FIDELITY. S:290: a seen candidate is refused and "the stored newest is still returned". Vanilla does not drop marks on a refused candidate: `AddMark` returns (142724), and only `UpdateMarks`' newest-position test calls `RemoveMarks` (142800-142807).
- Severity: major
- Impact: the plain reading of each old name asserts what its replacement forbids. A builder cannot make both red and then both green. T's matrix maps all four, so `prove` would report a missing case.
- Assumption:
  - Delete `AMarkCarriesNoEmbarkedTrait` from P:219 and T:29, or rename it `ALandGroupsMarkCarriesNoEmbarkedTrait` (a group that is not embarked).
  - Delete `AGridSeenMarkIsRefusedAndDropsTheMarks`; its two halves are the new pins.
- Grounding: vanilla / none

**GAP-05. F4b: once the bug is fixed, Whiskey's once-per-review roll no longer matches vanilla's per-frame rate, and the "2 to 36 days" expectation describes the wrong quantity.**
- **What S and P say.**
  - S:290 *Cadence* argues the AI side's daily rate equals vanilla's *because* of BUG-INTEL-001: the chance is capped at 0.9, so both are bounded by the 50-unit gate.
  - P:228 says `CreationChance` reads the fixed pair "so both sides change at once". Its smoke row: "marks thin to one per 2-36 game days per hidden group on both sides".
  - S:428 says the same ("2 to 36 days between marks").
- **What vanilla does once fixed.** `num3` is `0.006` game years at intel 1 (about 2.2 days). The chance per evaluation is `elapsed / num3`: about 0.46 after one game day, and it reaches the 0.9 cap by about 2 days (142819-142846).
- **Why the rates diverge.**
  - Vanilla evaluates each registered unit once per pass of a per-frame round-robin (`Update`, 142642-142655), i.e. many times per game day.
  - So vanilla's actual time between marks is set by the 50-unit gate plus a fraction of a day: a corps moves 50 units in about 0.3 days (S:295's 140-190 units a day).
  - Whiskey rolls once per review, at about 0.46 on day one.
  - "2-36 days" is the time constant `num3` (0.006 and 0.1 years), not the time between marks.
- Severity: major
- Impact:
  - Under F4b the AI side gets marks several times less often than the W&L player sees them, which breaks decision 2's "symmetric".
  - F4b's smoke expectation is wrong for both sides, so the smoke cannot pass or fail on it.
  - A builder following P builds the asymmetric version and believes it symmetric.
- Assumption:
  - Under F4b, `CreationChance` is a per-day hazard: `1 - (1 - c)^k`, where `c` is vanilla's per-evaluation chance at the review and `k` is a stated constant for evaluations per game day (or the chance is simply taken at the cap). S states that the rate is approximate and why.
  - The smoke expectation becomes "marks no more often than one per 50 units moved, and a stationary hidden group keeps its last mark", not "2-36 days".
  - This needs the author's confirmation. With the user picking (a), F4b is never built and this gap does not arise.
- Grounding: vanilla

**GAP-06. TC-INT-SMOKE's instrument check cannot be computed from the rows.**
- **What T and S require.** T:66 and S:405: "while the planner runs, every #25 row for alliances 0 and 1 is `decision=filter-observed` with nothing removed".
- **Legitimate Filter rows.** `DefenseInterceptionGate.ModeFor` returns Filter, correctly, in several cases (S:353, P:106):
  - before the first review publishes (`TryFreshAllocation` false);
  - when the plan is more than `PlanFreshness.MaxAgeDays` old (paused job machine, a failed review);
  - with an empty allocation.
- **The rows do not say which.** The shipped `filter-candidates` row carries `alliance|removed|forbidden` only (CheckForDefensiveOperationsCandidateFilterPatch.cs:395-402), plus `reasons=` after F1. Nothing tells a legitimate Filter row from a broken gate.
- **The verbose gate.** The filter row is verbose-gated (patch :177; `Defense Intent Verbose Logging`, default true, Plugin.cs:1284-1287), while the observed row is not (S:353). With verbose logging off, the check passes vacuously.
- Severity: major
- Impact: on a fresh career, vanilla's defence pass can run before the first publish, so the check can fail on day 1 with nothing wrong. The prover cannot tell this from a gate defect.
- Assumption:
  - The filter row (and `[Reserves]`) gains `mode=filter:<why>` with why one of `no-plan|stale|empty|flag|planner-off|europe`.
  - The instrument check counts only alliance-0/1 #25 rows on days whose `[Reserves]` row exists (a Full review published that day).
  - The smoke requires `Defense Intent Verbose Logging = true`.
- Grounding: shipped code

**GAP-07. The `question-capital-share` gate is still worded against phase-2 rows and a phase-2 quantity.** This leaves the pass-6a GAP-02 fold inadequate: a phase-1 gate that silently depends on phase 2.
- **What P's gate now reads.** P:369 reads `0.6 x [EnemyPicture] enemyCapitalEst=` and `enemyCapitalEstProp=`.
- **What S still says.**
  - S:277 still says the alternative is reported in "`[EnemyPicture] capitalShareProp=`, `[CoveringForce] needProp=`, so the user can pick at the gate after F4".
  - S:426 still says "Both are computed and reported at once (... `[CoveringForce] need= needProp=`)". Its recommendation is "(b) when the measured `capitalShareProp` keeps the covering need within the eastern army".
- **Why the recommendation cannot be evaluated after phase 1.**
  - Phase 2's covering need is capped: `min(max(0.6 x Estimate, Min), sum of AxisShare)` (S:302). The gate's `0.6 x enemyCapitalEst` is the uncapped term.
  - No phase-1 row reports the CSA's eastern army strength. `[CampaignPlan] depts=` gives counts only (TheaterPlanner.cs:1458).
- Severity: major
- Impact: at the gate, the integrator cannot apply S's own recommendation rule from phase-1 rows. Any phase-1 number shown, and any decision taken on it, differs from the need phase 2 will build.
- Assumption:
  - S:277 and S:426 name `[EnemyPicture] enemyCapitalEst=`/`enemyCapitalEstProp=` as the gate's read, and call it the uncapped would-be need.
  - `[Reserves]` (F1) gains `dept=<theater:have,...>` (`HoldStrength` of each department's formations) so that "within the eastern army" can be read.
  - The recommendation is restated as "(b) when `0.6 x enemyCapitalEstProp` is at most the East department's have".
- Grounding: shipped code / none

**GAP-08. `MarkLedger.Evaluate`'s output mark carries fields no declared input provides, and how a mark treats embarkation is half-defined.**
- **What the interface provides.** `Evaluate(unitKey, x, z, daySerial, dateYears, in MarkInputs, rng, gridHidden, out EnemyDetection mark)` (P:212, P:221). Neither its parameters nor `MarkInputs` (P:200-204) carry what the detection needs:
  - `Strength` (S:289: the live `groupstrengthdirect` banded by `BandRound`);
  - `UnitTyp` and `InstanceId`;
  - `Traits` (`Embarked`, and `OnOwnSoil`/`NearOwnCoast` judged at the mark position).
- **Strength source.** It is unstated whether a stored mark returned on later reviews re-reads the live strength (as vanilla's tooltip does, 143060) or keeps the strength it had when created.
- **Embarkation in the second pass.**
  - It is unstated whether the mark pass writes `LastEmbarkedDay` as step 5 does (EnemyArmyModel.cs:150-151). If it does, a mark afloat followed by a sighting ashore infers a landing from a mark. S:289 skips only the *landed* branch of `IsSeaInvasionThreat`.
  - Now that marks carry `Embarked`, an embarked mark near the coast also raises the "watch" branch (EnemyBeliefReadings.cs:87-88). S neither allows nor forbids this.
- **Groups that cannot get a new mark.** It is unstated whether a group with no `closestenemyunitfarreg`, or inactive, still has its stored mark returned. Vanilla zeroes only the chance and keeps the marks (142854-142862). S:290's list reads as a filter on calling `Evaluate`.
- Severity: minor
- Impact: a builder fills these in the runtime and the defaults are obvious, but `AnEmbarkedGroupsMarkIsAfloat` then has no pure subject unless the trait reaches `EnemyArmyModel`.
- Assumption:
  - `Evaluate` outputs position, day, radius and offset only. The runtime fills `Strength = BandRound(live groupstrengthdirect)` on every review, plus `UnitTyp`, `InstanceId`, and `TraitsOf(e, markPosition, ...)`.
  - A mark never writes `LastEmbarkedDay`, and the watch branch may fire on an embarked mark.
  - `Evaluate` is called for every hidden enemy group; the conditions gate creation only.
- Grounding: shipped code / vanilla

**GAP-09. Stale text left by the pass-6a GAP-11, GAP-14 and GAP-19 folds.**
- **P:118 vs P:119/S:362.** P:118 still says the observed log is "registered with `CampaignScope.Register(...)` from the patch's static constructor". P:119 and S:362 say `ObservedLog` registers itself.
- **P:122 vs P:85/S:360.** P:122 still says "`[Reserves] vanillaDefWithheld=0` from F6b on; until then the `InDefensiveOps` count in `[Allocation] busy=`". F1 builds that field (P:85, S:360).
- **P:41's parallel claim.** P:41 still calls F2 "disjoint from F3 (only `Plugin.cs` and `TelemetryTagPolicy.cs` shared)". F2 now owns the `[Reserves]` emit in `CampaignPlannerRuntime.cs` (P:119), which F3 also edits for its rows (P:128).
- **P:66 vs P:85/S:359.** P:66 says the trace token is "`@<x>,<z>`"; P:85 and S:359 say "` at=<x>,<z>`, never glued".
- **P:169 vs P:170.** P:169 still prints the old `[EnemyPicture]` field list; P:170 says S's list governs.
- **P:31.** P:31's published-member list repeats `MinGarrisonStrength` and `CapitalReach` under "phase 2: the rest".
- Severity: minor
- Impact: contradictory sentences. The later or more specific sentence wins in each case, but a builder may follow the wrong one, and running F2 and F3 in parallel will conflict on one file.
- Assumption:
  - P:119, P:85, S:359 and P:170 govern.
  - F2 and F3 run in sequence, or the `[Reserves]` emit line is F1's alone.
- Grounding: shipped code

**GAP-10. Naming left open: the date helper, a method that does not exist, and the trace's `anchors=`.**
- **The date helper.** S:357 still names `ReviewCadenceCatchUp.DateOf(daySerial)`. P:66 names `DateOf(serial, out y, out m, out d)` plus `FormatDate(serial)`.
- **A method that does not exist.** S:349 and P:240 name `ForceAllocator.CouldScout`. The method is `ReconPlanner.CouldScout` (ReconPlanner.cs:505), called from `ForceAllocator` at :837 and :857. Two other `CanMove` callers have no plan in reach: `ScoutPool` (from `ReconStep.CountsOf`, ReconStep.cs:160) and `TaskRanges`. Both use the detachment branch, which reads no minimum.
- **The trace's `anchors=`.** S:359 and P:169 still do not say whether it counts per candidate or the whole picture (pass-6a GAP-19, not folded).
- Severity: minor
- Impact: naming only; one obvious answer each.
- Assumption:
  - P's `DateOf` and `FormatDate`.
  - `ReconPlanner.CouldScout(s, f, kind, minStrength)`; the detachment-only callers pass `plan?.MinGarrisonStrength ?? MinStrengthFallback`.
  - `anchors=` is per candidate: undisplaced anchors with a share above 0 within `R` of the target.
- Grounding: shipped code

**GAP-11. `enemyCapitalEstProp` needs each anchor's proportional mass, and the declared types carry only one share.**
- **What S requires.** S:361 requires `enemyCapitalEstProp` = `Estimate(enemy capital, R)` "under each share". The proportional rule spreads U over *all* undisplaced anchors (S:277).
- **What the types carry.** `PictureAnchor` has a single `Share` (P:134). `EnemyPicture.CapitalShareProp` (P:170) is the capital's fraction only. `Presumed(s, x, z, r, probe)` (P:144) has no share selector.
- Severity: minor
- Impact: a builder might swap only the capital anchor's mass, which mis-states the gate's number whenever an objective anchor lies within `R` of the enemy capital.
- Assumption: `PictureAnchor.ShareProp` (each anchor's U fraction under the proportional rule), and `Presumed(..., bool proportional)`.
- Grounding: none

**GAP-12. No slice fills `[EnemyPicture] seen=` and `marks=`, and their source is undefined.**
- **What S and P say.** S:361 and P:170 have F3 emit them as 0 "until F4".
- **What F4 owns.** F4's owned files (P:179) include `CampaignPlannerRuntime.cs` only for the seed, and its steps never mention the picture row.
- **Undefined source.** S does not say whether `seen` is `[EnemyBeliefs]`' non-mark detection count and `marks` its `AppliedMarkKeys.Count`.
- Severity: minor
- Impact: after F4 the fields stay 0, and P:224's smoke row (`[EnemyPicture] seen=<n> marks=<n>`) cannot be produced.
- Assumption:
  - F4 fills both from the same counts `[EnemyBeliefs]` uses: the non-mark detections and `AppliedMarkKeys.Count`.
  - F4's owned line for `CampaignPlannerRuntime.cs` adds "the `[EnemyPicture]` fields".
- Grounding: shipped code

**GAP-13. With `Enable Strategic Foresight Picture` off, the outputs are undefined.**
- **What P:49 lists.** The rules the flag restores.
- **What it does not say.**
  - whether `Plan` still builds the picture;
  - whether `[EnemyPicture]` and `[CapitalThreat]` are still emitted;
  - what `TheaterPlan.CapitalReach`/`CapitalReliefDays`/`CapitalCorpsPerDay` and `[CapitalFloor] reach=` hold;
  - whether the trace's `anchors=` prints.
- **The tests.** `FlagOffRestoresTheD2Presumption` and `FlagOffRestoresTheDepartmentSumAndTheOldScoutTest` (T:16) pin only the rules.
- Severity: minor
- Impact: a rollback run's rows read inconsistently (for example `reach=480` beside a 200-radius threat).
- Assumption:
  - Flag off: the picture is still built and its row emitted (telemetry only).
  - `CapitalReach = RelocationRange`, with relief and corps rate 0.
  - `[CapitalThreat]` reports the P7 numbers.
- Grounding: none

**GAP-14. F9's surplus rule names phase-2 claimants.**
- **What S says.** S:345 defines the standing hold as formations "not mass, committed or returning". The covering force is "claimed before the depot step".
- **What exists in phase 1.** `committed` and `returning` are F7's (phase 2) and do not exist in phase 1.
- **What P says.** P:41 declares only the covering clause vacuous in phase 1.
- Severity: minor
- Impact: a phase-1 slice's rule silently references phase-2 artifacts. It is vacuous, but it is unstated whether F9 adds placeholders.
- Assumption: F9 implements "not mass" only. F7 adds "committed or returning" and re-runs F9's tests. P:41's vacuity note names both.
- Grounding: shipped code

**GAP-15. P:260's extended `PipelineOrderPinned` (over `TheaterPlanner.Plan`'s body) has no owning slice.**
- **What exists.** The shipped pin covers `ReviewPlan`, `ReviewExecute`, the steering executor and the job list (CampaignExecutorTests.cs:1277-1327), not `TheaterPlanner.Plan`.
- **Who owns the file.** F1 and F4 own `CampaignExecutorTests.cs` for their re-pins (P:86, P:221). F3 does not list it.
- **What F10a assumes.** F10a lists "in `TheaterPlanner.Plan`: the picture built before the first `Evaluate`" among pins that must be green, as if they already existed.
- Severity: minor
- Impact: either no one writes the pin, or F10a writes a new test at integration, against the source-of-truth gate.
- Assumption: F3 owns `CampaignExecutorTests.cs` for one added `AssertInOrder` over `TheaterPlanner.Plan` (`EnemyPicture.Build` before the first `Evaluate(inputs, candidates)`). This duplicates `PictureBuiltOncePerReview`; the source pin is that test.
- Grounding: shipped code

**GAP-16. `TheaterPlanner.MinStrength(Inputs)` has no stated source for a bare `Evaluate`.**
- **What S and P say.**
  - S:349: `Inputs.MinStrength` initialises to `MinStrengthFallback`, and `Plan` sets the minimum from the snapshot.
  - P:234: `MinStrength(Inputs)` "now reads the plan's value", but `Inputs` holds no plan.
  - F3's pins call `Evaluate` directly (S:272: a bare `Evaluate` builds the picture).
- **The undecided case.** In such a test, the helper returns 5000 unless the test sets `Inputs.MinStrength`, even when `S.MinStrengthForMovement = 2000`.
- Severity: minor
- Impact: the "relative to `MinStrength(inputs)`" assertions stay correct, but a test that builds a snapshot at 2000 and expects 2000 floors from `Evaluate` would fail.
- Assumption:
  - `MinStrength(Inputs)` returns `Inputs.MinStrength`.
  - `Plan` sets that field (validated, with the fallback and the picture-off rule) before the first `Evaluate`.
  - Tests that want the live value set the field.
- Grounding: shipped code

## Pass-6a folds checked

- GAP-01 (grid probe): adequate in S:290 and P:221 (`gridHidden`; grid missing or broken: no new mark). P:212's interface block still lacks the parameter, and the old test name survives (-> GAP-04).
- GAP-02 (gate needs a phase-2 number): inadequate (-> GAP-07). P:369 and S:361 add `enemyCapitalEst`/`enemyCapitalEstProp`, but S:277 and S:426 still key the gate on `[CoveringForce]` and on the eastern army.
- GAP-03 (`DepartmentHoldMet`): adequate. S:283 and S:335 now agree: shipped `have` against `DepartmentNeed`. The test was renamed `TheEastKeepsItsHoldWithoutADonorIn002A` (P:158, T:20).
- GAP-04 (relief race inputs): adequate (S:295, P:170). Straight distance, no probe, `OwnIips` before `CapitalThreat`, no rail on either side, `MaxValue` skipped.
- GAP-05 (`VanillaNeed` inputs): adequate (S:296, P:173). It is called with `MinStrength`, beliefs at morale 1 and `s.Own`, and the claim is restated "on the review's snapshot".
- GAP-06 (D8 post need): adequate (S:283, `SecessionPostCountsNoElementASectorHolds`). Its dependence on the per-element assignment is GAP-01.
- GAP-07 (Europe anchors): adequate (S:277 `HolderAlliance == e`; `AnEuropeHeldObjectiveIsNoAnchor`, `AnAllianceTwoSnapshotSpreadsNothing`).
- GAP-08 (anchor labels): adequate. S:271 `EnemyCapitalName`; S:361 `enemyCapital=`; criterion S:396 keyed on it; `AnchorsAreLabelled`.
- GAP-09 (dates after the compare; source pins): adequate (S:357, P:86).
- GAP-10 (Marching builder through a readiness dip): the rule is folded (S:345, P:253), but its step and target are open (-> GAP-02).
- GAP-11 (ownership text, `ObservedLog` reset): inadequate (-> GAP-09). P:75-76 and P:119 are fixed; P:118 and P:122 still contradict them.
- GAP-12 (`ForesightPicture` initial value): adequate (S:349 "initialises to true"; P:170; `ForesightPictureDefaultsOn`).
- GAP-13 (signature breaks, `PipelineOrderPinned`): adequate. There is an overload (S:272, P:170), and F1 and F4 own their re-pins (P:86, P:221). The F10a pin over `Plan` is new (-> GAP-15).
- GAP-14 (row field lists): adequate in S:361 and P:170 (S governs; relief and corps fields published; `elements=` defined). P:169's stale list is -> GAP-09.
- GAP-15 (Recon minimum plumbing): adequate for `CanMove`, `FlagsOf`, the census row and the rollback note (S:349, P:240). The method name is wrong (-> GAP-10).
- GAP-16 (`remembered=` set exclusion): adequate (S:291, P:221).
- GAP-17 (F4b pure subject): adequate (`TimingPair`, the usefow-only gate, F4b owns the fill line; P:228). The cadence problem is new (-> GAP-05).
- GAP-18 (F9 surplus measure, fixtures): adequate (S:345 `Counted` + `HoldStrength`; P:253 regression pin and minimal fixture).
- GAP-19 (naming): partly folded. `MarkDay`, `PlayerSideMark(UnitRelation)` and `CountedHold`'s signature are in. `DateOf` in S and `anchors=` are not (-> GAP-10).
- GAP-20 (embarked marks): adequate in S:289 and P:221, but `AMarkCarriesNoEmbarkedTrait` survives (-> GAP-04).

## Checked and clean

- **F1, capture of yesterday's allocation.** `AgeRecon` already reads `TryGetAllocation` before `Publish` (CampaignPlannerRuntime.cs:323-330, 157), so capturing the previous allocation and plan there is well defined.
- **F1, `PipelineOrderPinned`'s anchor line.** `"plan = TheaterPlanner.Plan(snapshot, sectors, id => ..."` (CampaignExecutorTests.cs:1295) is a prefix, so the new overload, with `out picture` added after `out trace`, keeps it matching.
- **F1, issue-row source.** `Dispatch` has the assignment, `f.ReadinessStep`, `ObjectiveOf` and `moved` (CampaignMover.cs:316-350). That is enough for every `[CampaignMoverIssue]` field, including `pick=` from `Assignment.Reason`.
- **F2, observe path.** The Postfix revert runs only when the filter path filled `_defensiveOpsBefore` (patch :208 vs :135). The early return at `forbidden.Count == 0` (:111) matches "emitted whenever the forbidden set is non-empty". `TryFreshAllocation` covers freshness, executors allowed and Europe (CampaignWithholdView.cs:161-184).
- **F2, guardrail measure.** `WithheldFromDefense` withholds every role with `Withheld` set, except a Hold whose enemy stands in the same sector (CampaignWithholdView.cs:126-134). Reserve, CapitalGuard, DepotBuilder, Recon and Recover are always `Withheld = true` (ForceAllocator.cs:469, 593, 611, 718, 938), so `vanillaDefWithheld=0` is the right guardrail.
- **F3, relief race inputs.** `MarchTime.EstimateDays` with a null snapshot skips the rail branch, and with a null probe skips the river branch (MarchTime.cs:66-88). `OwnIips` is cheap to move ahead of `CapitalThreat` (TheaterPlanner.cs:321-323).
- **F3, corps road rate.** `groupspeed` starts at `unitbasicspeed[15,1]` and only falls (120179). `num11 *= groupspeed` (133991). So `SpeedPerDay(m, 15, MountedBaseSpeed[15], RoadCost)` is the ceiling rate.
- **F3, `VanillaNeed`.** Its inputs are available on the snapshot: beliefs, morale 1, the capital, `RelocationRange`, `s.Own` with waypoint fallback (CapitalFloorPolicy.cs:VanillaNeed). It can be negative (FX-BASE -42005), so `max()` with the planner's term is safe.
- **F3, the D6 donor test.** `DepartmentHoldMet` (TheaterPlanner.cs:955-968) only needs its `need` side replaced. `have` stays every coverage-counting formation of the department, as S:335 now says.
- **F3, release.** `HoldThreatAtTarget` has a single caller, `MassFor` (TheaterPlanner.cs:990, 1135-1138). Replacing it touches no other reader. G4 never re-runs release (`MarkMass` precedes `AssignHoldReserve`, ForceAllocator.cs:325-327, 475).
- **F4, vanilla mark mechanics.**
  - Draw order: the variation at 142819, the roll as the first operand of `&&` at 142866 (always drawn), then the angle and magnitude in `AddMark` at 142700-142701.
  - The int angle overload gives -45..44.
  - The age blend `min(1, age x 25)` runs only with a previous mark (142706-142711).
  - The 50-unit gate measures from `startpos`, which is `default(Vector3)` when there is no mark (142780, 142816).
  - All of these match S:258 and S:290.
- **F4, AI-side enemy list.** `CollectUnits` already excludes inactive groups, fleets and garrisons (EnemyDetectionRuntime.cs:253-269). `GarrisonsAndFleetsGetNoMark` is correctly a regression pin.
- **F4, the seed.** `CampaignSeed` is computed today after `Detect` and only on the Full path (CampaignPlannerRuntime.cs:110 vs 124-125). S:290 and P:221 move it and re-pin the `Detect` line; the inputs (scenario, date) are available before line 110.
- **F5, minimum readers.** Every non-detachment `CanMove` caller has a plan in reach: `PickFormation` takes `plan` (ReconPlanner.cs:116), and `MainBodyCavalryLeft` and `Incumbent` are called from it. Only the detachment-only callers lack one (GAP-10).
- **F9, `Free` already refuses hold reserves.** `Free` (ForceAllocator.cs:597-600) refuses every formation already given a role, so `NoHoldReserveBuilds` is green on shipped code, as P:253 says.
