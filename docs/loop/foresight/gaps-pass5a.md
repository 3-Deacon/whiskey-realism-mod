# D11 build dry run, pass 5a: F1, F2, F4, F4b, F5

Builder seat, dry run. Worktree `.worktrees/plan-foresight` at `1a0c4c57` (contains `main` `dbdd4de4`). S = `docs/superpowers/specs/2026-10-14-campaign-planner-design.md` (D11, lines 245-422), P = `docs/superpowers/plans/2026-10-18-strategic-foresight.md`, T = `docs/loop/foresight/test-plan.md`. Shipped files are under `src/WhiskeyRealism/`. Vanilla is `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` (present). No repository file was edited.

**I hit the 20-gap cap.** To stay under it I merged related findings: GAP-10 covers the date source and the rows that need a date, GAP-12 covers the F4b contradictions and the bug-fix labels, and GAP-20 holds four small F4 interface choices.

## Gaps (20: 13 major, 7 minor)

**GAP-01. Vanilla's mark offset points toward the closest enemy, not away from it.** S:258 says `AddMark` offsets the mark "away from `closestenemyunitfarreg` at `GetAngle + 180 +- 45` degrees". S:290 says "`GetAngle` and `GetAnglePos` keep vanilla's conventions (151207, 151304)". P:189 and T:29 pin `OffsetPointsAwayFromTheClosestEnemy`. The decompile gives the opposite result. `Tools.GetAngle(start, end)` returns `atan2(dx, dz)` in degrees minus 180 (151304-151310), so it is the reverse bearing. `GetAnglePos(start, angle, d)` computes `a = angle - 90` and returns `x + cos(a) d`, `z - sin(a) d` (151207-151217). `AddMark` passes `GetAngle(unit, enemy) + 180 + Random.Range(-45, 45)` (142700-142701). Worked cases, with `Mathf.Atan2(y, x)` called as `Atan2(num = dx, num2 = dz)` (151309). Enemy due east, at (10,0) from the unit at (0,0): `atan2(10, 0) = 90`, minus 180 gives -90, plus 180 gives 90. `GetAnglePos(90)` sets `a = 0`, so `x = cos(0) d = +d` and `z = -sin(0) d = 0`: toward the enemy. Enemy due north, at (0,10): `atan2(0, 10) = 0`, minus 180 gives -180, plus 180 gives 0. `GetAnglePos(0)` sets `a = -90`, so `x = cos(-90) d = 0` and `z = -sin(-90) d = +d`: toward the enemy again. The ±45 draw only spreads the angle around that bearing. The mark is therefore displaced 3 to `num` units toward the observing side.
- Severity: major
- Impact: copying vanilla's two functions exactly, as S requires, makes the test named in P and T fail. Making the test pass means departing from vanilla. Both sides' mark positions and the `AMarkBeliefReKeysWithinItsBaseRadius` geometry depend on which one is chosen.
- Assumption: mirror vanilla exactly (toward the enemy). Rename the test `OffsetPointsTowardTheClosestEnemyAsVanillaDoes`, and correct S:258's wording at F10 as an "as built" deviation.
- Grounding: vanilla

**GAP-02. requirement-marks-ai-side gives two different in-fog predicates.** The first sentence of S:290 makes a group eligible when "rule a-d did not detect today, that `fows[1].UnitIsHidden` (127225 arguments, gated on `usefow` as G1 is) reports hidden, that is active, ...". The *Fidelity* clause of the same requirement says "the in-fog predicate is 'not detected today by rules a-d' (G1's own verdict, which requires an own viewer and so keeps Europe's painting out, C14); the one-argument `fows[1].UnitIsHidden(position)` ... is used only to mirror the refusal ... (142724) and the drop ... (142800-142807)". The two readings differ for a group that the grid shows but that no own viewer covers. For example, a cell Europe paints passes `ViewerCoverage.Covers` = false (EnemyDetectionRuntime.cs:425), so rule (b) does not detect the group. The first reading then refuses a mark because `UnitIsHidden` says visible. The second reading lets the mark through. The first reading also costs a 127225-argument probe for every undetected enemy, while G1 probes only covered ones (`NeedsGridProbe`, EnemyArmyModel.cs:484-487). That cost falls inside the 2 ms `campaign.planner.detect` scope.
- Severity: major
- Impact: the AI side's mark set, and so `marks=`, differs between the two readings. `CreationChance`'s inputs and the perf budget depend on the choice.
- Assumption: Fidelity governs. The in-fog predicate is "not detected today by rules a-d", and only the one-argument `UnitIsHidden` calls run (refusal and drop). Delete the 127225 clause from the first sentence at F10.
- Grounding: shipped code

**GAP-03. Mark beliefs trigger the landing and raid inferences, and recon contact, through readers S does not name.** S:289 says a mark carries "`Traits` without `Embarked` (a report never triggers the landing inference)". S:291 limits the "never scouts" exclusion to `IsScouted`, `FreshLandBeliefWithin` and D7's probe. The shipped landing test does not need `Embarked` on the report. `EnemyBeliefReadings.IsSeaInvasionThreat` (EnemyBeliefReadings.cs:84-91) returns `landed` for any non-`Embarked` belief with `OnOwnSoil` and `LastEmbarkedDay >= 0` within `LandingWindowDays`. `EnemyArmyModel.Update` carries `LastEmbarkedDay` over from the matched belief (EnemyArmyModel.cs:150-151). So a group seen afloat on day N and marked ashore on own soil on day N+2 raises a landing, which raises orders. Three other readers branch on "seen recently" or "seen today" rather than on strength, and none is told whether to skip marks:
  - `IsRaidThreat` (cavalry majority on own soil, 95-100, with `CavalryMajority` "from the live group" per S:289).
  - `ReconMissionLedger.InContact` (`LastSeenDay == s.DaySerial`, ReconMissionLedger.cs:293). An AI-side mark made today has `MarkDay = today`.
  - The `BeliefsFor` consumers (`DefenseIntentLedger`, `ContactEvidenceLedger` via `BeliefConfidence01`).
- Severity: major
- Impact: a report placed 25 units off the truth (002/A) would raise sea-invasion orders, raid threats and recon "contact". S claims the first of these cannot happen. P:163's owned files list neither `EnemyBeliefReadings.cs` nor `ReconMissionLedger.cs`.
- Assumption: marks count in strength sums only. `IsSeaInvasionThreat`'s landed branch, `IsRaidThreat` and `InContact` skip `LastSource == Mark`, and those files join F4's owned list. Add pins `AMarkNeverLandsAGroup`, `AMarkIsNoRaid` and `AMarkIsNoRecontact`.
- Grounding: shipped code

**GAP-04. A same-day Seen detection and mark of one group collide in `Update`'s step 1.** S:291 and S:351 say a mark of a group already detected today is ignored and "the Seen detection wins". P:189 and T:29 pin `SameDaySightingBeatsAMark`, which has to feed both detections to the pure `Update`. The runtime only ever adds a mark for a group that rules a-d did not detect (S:289, S:290), but the pure model does not know that. `EffectiveKeys` (EnemyArmyModel.cs:205-229) counts the two detections under one `UnitKey` and gives both the same composite key `name:commander#<instance>`. The exact match (90) then misses the belief keyed `name:commander`. Step 3 (95-108) re-keys the belief to the first detection by `NameOf`, which keeps the part before the last ':'. The second detection becomes a new duplicate belief. This happens before any mark-after-Seen ordering S:291 describes can apply.
- Severity: major
- Impact: written as S describes, the pin fails, or the model carries two beliefs for one group: double strength in the picture and in the `troopsfielded` accounting.
- Assumption: `Update` splits the detections by `Source` first. Seen, Contact and ObjectiveGarrison run through steps 1-5 as shipped. Marks then apply in a second pass that drops any mark whose key matches a detection of this review, never enters `EffectiveKeys`, and never runs steps 3-4.
- Grounding: shipped code

**GAP-05. What `MarkLedger.Evaluate` returns, and so what `marks=` counts, is open.** P:182 gives `bool Evaluate(..., out EnemyDetection mark)` as "evaluate once per review per hidden enemy group". S:290 says "the newest mark per group becomes one `Source = Mark` detection as on the player side". On the player side the newest mark is read every review whatever its age (S:289). It is not stated whether `Evaluate` returns true, and outputs the stored newest mark, on a review where no new mark is created. It is also not stated whether `[EnemyBeliefs] marks=` (S:291, S:344) counts mark detections or mark beliefs. Under BUG-INTEL-001 a new mark comes only on a 50-unit move (142866), so a stationary army makes none.
- Severity: major
- Impact: under "created only", `marks=` reads 0 on every review where no hidden group moved 50 units. criterion-marks-read (S:385, T:30: `marks>0` at least once in every 7 game days) then measures movement rather than the channel, and the two sides become asymmetric (the player side always re-reads its newest mark).
- Assumption: `Evaluate` returns true whenever the ledger holds a newest mark for the group, created now or earlier, and outputs it. `marks=` counts the mark detections this review applied to a belief (after GAP-04's filter and S:291's stale-age rule). This is the same `AppliedMarkKeys` set as GAP-19, and `MarkBeliefCountsInTheTotals` pins it.
- Grounding: none

**GAP-06. Decoding `timeshown` is given two ways, and the inversion is ambiguous at month ends.** S:289 first gives "`MarkDay` = today minus `floor((campaign date - timeshown) x 365.25)`". The same requirement then says "*Mark day:* `timeshown` is decoded to a calendar date by inverting `GetCampaignFloatingDate` ... (a straight `x 365.25` misorders month ends by up to 2 days)". Vanilla's date is `year + (monthCounter - 1)/12 + (dayCounter - 1)/365.25 + Hour/24/365.25` (66617). The month and day terms overlap. Day 31 after about 10:30 gives `(30 + h/24)/365.25 >= 1/12`, which is above the next month's day 1 at 00:00, so a naive `month = floor(frac x 12) + 1` decodes it as the next month with a negative day. P:189's pin `MarkDayDecodesTheCalendarDate` (28 Feb -> 1 Mar is 1 day) does not reach that case.
- Severity: major
- Impact: the player side's `MarkDay` drives a mark's confidence, `LastSeenDay`, the "older than the belief" test and the stale-age rule. A wrong decode around month ends flips mark-or-sighting precedence.
- Assumption: the inversion governs (delete the `floor` formula). Decode `month = clamp(floor(frac x 12) + 1, 1, 12)` and `dayFrac = (frac - (month - 1)/12) x 365.25`. When `dayFrac >= days in month`, step to the next month with `dayFrac - daysInMonth`. Then `day = floor(dayFrac) + 1`, `DaySerial` via `ReviewCadenceCatchUp.DaySerial`, capped at today. Add a pin for 31 Jan 23:00.
- Grounding: vanilla

**GAP-07. Several F4 pins cannot be written red-first against the harness.** The player-side reader lives in `EnemyDetectionRuntime` (runtime: it reads `SpottedEnemy`, which vanilla keeps on the `GameController` GameObject, 67365). Runtime adapters are not in the test csproj (AGENTS.md § Testing; EnemyDetectionRuntime.cs:14). P names no pure seam for the player side.
  - `PlayerSideReadsOnlyTheNewestMark` (P:189, T:29). Vanilla's newest mark is `spottedmarks[Count - 1]`, since `AddMark` appends (142741).
  - `PlayerAllianceUnitsNeverRegisteredByVanilla`, which only "documents 142938": a fact about vanilla's `AddUnit` that no harness can exercise.
  - `GarrisonsAndFleetsGetNoMark`, which passes on shipped code, because `DetectionRules.RelationOf` already drops fleets and garrisons before any mark logic (EnemyArmyModel.cs:553-558).
- Severity: major
- Impact: a builder cannot drive these red. They end up as tautologies or as source pins of unknown shape, and TC-MARK-NEWEST (T:29) proves nothing.
- Assumption: add a pure `MarkRules.NewestMark(IReadOnlyList<(float timeshown, float x, float z, float intel)>)` and a pure player-side filter `MarkRules.PlayerSideMark(...)`, both called by the runtime and tested. Turn the 142938 pin into a source pin that `EnemyDetectionRuntime` reads marks only for groups `DetectionRules.RelationOf` classes `Enemy`. State `GarrisonsAndFleetsGetNoMark` as a regression pin (green from the start), not red-first.
- Grounding: shipped code / vanilla

**GAP-08. F3's tests and floors assume the live 2000 minimum, which only F5 delivers.** P:142 builds F3's fixture with "minimum strength 2000". S:281 and S:285 make F3's `Floor` and eligibility floor read "the live `MinGarrisonStrength` of requirement-live-min-strength", but that requirement is F5's, and F5 lands after F3 and F4 (P:39, decision 6). Until F5, every planner floor reads the const `ForceAllocator.MinGarrisonStrength = 5000` (ForceAllocator.cs:86; TheaterPlanner.cs:769, 1046, 1322). `CampaignSnapshot.MinStrengthForMovement` is read only by `ReconPlanner` (ReconPlanner.cs:497). So these F3 pins run on 5000 while their fixture says 2000: `FloorHasVanillasBaseline`, `ThreatExcludesTheBaseline` ("floor = minimum"), `ACapacityBelowTheLiveMinimumIsIneligible`, `RetainNeverReenablesBelowTheMinimum`, the `[CapitalThreat] floor>=2000` smoke. F5's re-pin step (P:206) greps for `MinStrengthForMovement|MinGarrisonStrength`, which these F3 pins may not name.
- Severity: major
- Impact: F3 either writes its pins at 5000 and F5 silently re-pins them, or writes them at 2000 and they are red until F5. Either way F3's own gates (P:27, exit 0) cannot both hold and keep the fixture.
- Assumption: F3 reads the minimum through one helper (`TheaterPlanner.MinStrength(Inputs)`) that returns the const until F5. F3's fixture pins assert relative to that helper. F5 changes only the helper's source and lists F3's pins in its re-pin commit.
- Grounding: shipped code

**GAP-09. `[CampaignMoverIssue] pick=new|pinned` is in S but not P, and the mover cannot tell new from pinned.** S:341 lists `pick=new|pinned`, and criterion-builders-fit (S:400) counts only `pick=new` DepotBuilder rows. P:67's interface string has no `pick=` (nor `date=`). Both builder paths write the same role and reason: `AssignPinnedBuilders` uses `Assign(..., DepotBuilder, ..., ReasonDepotTask, true)` (ForceAllocator.cs:446) and `AssignDepotBuilders` does the same (611). `Assignment` carries no other mark (CampaignPlannerContracts.cs:461-471). The value of `pick=` for roles other than DepotBuilder is also undefined.
- Severity: major
- Impact: criterion-builders-fit cannot be counted, and the "a pinned builder is not counted" exception has no row field to test.
- Assumption: add `ForceAllocator.ReasonPinnedBuilder = "depot-task-pinned"` for the pinned path (a reason change, re-pinned). `IssueRow` writes `pick=pinned` for that reason, `pick=new` for `ReasonDepotTask`, and `pick=-` for every other role.
- Grounding: shipped code

**GAP-10. `date=` has no source in the mover, and P does not say which shipped rows gain it.** S:340 requires `date=YYYY-MM-DD` on every row. Criteria count review days from rows that carry no date today:
  - `[EnemyBeliefs]` (EnemyDetectionRuntime.cs:227-235), for criterion-marks-read's "every 7 game days".
  - `[CampaignPlan]` (`TheaterPlanner.Summary`, 1449), whose 30-day `main=` count S:377 says "needs F1's `date=`".
  - `[Allocation]` (`ForceAllocator.Summary`, 206).

  P:61's owned files cover none of these three (only `TheaterPlanner.Trace`). All three rows dedupe on a whole-string compare (`LastSignature` 233, `LastPlanRow` 401, `LastAllocationRow` 427), so a date inside the string would defeat the dedupe. For the new mover rows, the pure `CampaignMover` sees only `s.DaySerial` (CampaignMover.cs:186-190). `ReviewExecute(alliance, jobMachinePaused)` carries no calendar date (CampaignPlannerRuntime.cs:170; StrategicCoordinator.cs:588 holds `d.Day/Month/Year` but does not pass them). `ReviewCadenceCatchUp` has `DayOfMonth` but no full serial-to-date inverse (ReviewCadenceCatchUp.cs:38-48).
- Severity: major
- Impact: criterion-marks-read, the axis-not-stripped guardrail and the picture-conserves guardrail (S:377, S:383, S:385) cannot be counted. T:11's "in force on a day" needs a date on the row.
- Assumption: add `ReviewCadenceCatchUp.DateOf(serial, out y, out m, out d)` (the inverse of `DaySerial`) and a pure `FormatDate(serial)`. F1 adds `date=` to `[CampaignMoverIssue]`, `[CampaignMoverMiss]`, `[CampaignMover]`, `[Reserves]`, `[CampaignPlan]`, `[Allocation]` and `[EnemyBeliefs]`, appended after the dedupe compare (the signature stays dateless), and those three shipped files join F1's owned list.
- Grounding: shipped code

**GAP-11. F1's test `ReservesDefenceCountsReadYesterdaysAllocation` covers fields F1 does not own, and the capture point for "yesterday" is unstated.** P:77 and T:69 put `ReservesDefenceCountsReadYesterdaysAllocation` in F1. P:68-71 give F1 `[Reserves]` as only `capital=` and `hold=`, and assign `vanillaDefWithheld=` and `vanillaDefHoldOrCovering=` to F6b and `massInVanillaDef=` to F7. Yet F1's `ReservesRow.Build` already takes `previousAllocation, previousPlan`. Inside the review, `CampaignPlanStore.TryGetAllocation` returns the working copy (CampaignPlanStore.cs:39-52). `Publish` overwrites that copy with today's (157), so "yesterday's" must be read before line 157, as `AgeRecon` does at 327. P does not say so.
- Severity: major
- Impact: either F1 builds three fields early, or F1's test has nothing to assert. A builder who reads "previous" after `Publish` gets today's allocation, and every count reads 0 by construction (S:343's own warning).
- Assumption: F1 ships the previous-allocation plumbing (captured before `Publish`) and all three counts. Everything they need exists today: roles, `Withheld`, `MassInstanceIds`, `InDefensiveOps`. F6b only adds the `covering-force` reason to `vanillaDefHoldOrCovering`. The pin feeds a previous Reserve formation that is now `InDefensiveOps` and expects `vanillaDefWithheld=1`.
- Grounding: shipped code

**GAP-12. F4b adds a Harmony patch, which P:38 and F10 say D11 never does, and its shape is open.** P:38 says "This plan adds **no Harmony patch**". P:309 (F10) says "D11 adds no Harmony patch and no `PatchFirstFire.Mark`, so `tools/smoke-check.py`'s first-fire lists and `tools/smoke-situational.txt` need no entry". P:197 (F4b) creates `Patches/SpottingMarkTimingFixPatch.cs`. Every patch entry must call `PatchFirstFire.Mark` and wrap a `TelemetryPerf.Scope` (AGENTS.md § Patch hygiene), and smoke-check lists every `Mark` (tools/smoke-check.py:4-8). F10's catalog list (P:310) has no row for it. On shape: `UpdateMarks(int unitid)` is private and has no argument to swap (142773). `spottingfactor` is a per-unit-type static array (53058) that `FogOfWar.UnitIsHidden` also reads (101203). Within `UpdateMarks` and `AddMark` the only grid calls are `GetIntelligenceOnPos` and the one-argument `UnitIsHidden(pos)`, which leaves `unittype = -1` and so skips 101202-101204. The only reads of `spottingfactor` in the whole decompile are 101203 and 142819, so a swap of elements [0] and [1] for the duration of the call is safe. `UpdateMarks` runs every frame for one unit (142646-142654), so the scope's cost matters. Bug-fix labels are also off: F4 logs BUG-INTEL-001 as "not fixed; question-intel-bug" (P:163), but `docs/bug-fixes/AGENTS.md` offers `Confirmed`, and "fixed" (P:197) is `Shipped` only after a catalog ordinal and a smoke.
- Severity: major
- Impact: if F4b is chosen, F10's checks contradict the plan (`smoke-check` reports an expected-never-fired id unless it is listed), and the builder has to pick the swap shape alone.
- Assumption: F4b's patch is a non-skipping Prefix that saves `spottingfactor[0..1]` in `__state` and writes `timefactorspottings[0..1]` into them, plus a Finalizer that restores them. It is labelled `FS-F4b-1`, calls `PatchFirstFire.Mark("campaign.patch.spotting-mark-timing.prefix|finalizer")` (listed in `smoke-situational.txt` as fog-only), and has a catalog row at F10. Fix P:38 and P:309 to "no patch unless F4b". Log F4 as `Confirmed` and F4b as `In progress` until the smoke.
- Grounding: vanilla / shipped code

**GAP-13. F2's row and dedupe pins have no pure seam.** P:103 names `ObservedRowCountsWhatWouldBeRemoved`, `ObservedRowNotGatedOnVerbose` and `ObservedRowDedupesPerCampaign`. Only `ObservingPrefixRemovesNothingAndPostfixRevertsNothing` is declared a source pin. The row, its `wouldRemove` count (forbidden ids present in `ownunits`), the per-alliance signature and the `CampaignScope` reset all live in `CheckForDefensiveOperationsCandidateFilterPatch` (Unity `Regiment`/`IList` types), which is not in the test csproj (the csproj compiles only the swap and IL helpers under `Patches/`). The only pure file F2 creates is `DefenseInterceptionGate` (`ModeFor`, P:94-99).
- Severity: major
- Impact: the three tests can only be source pins of a shape P does not give, or are not written at all. TC-INT-NOREMOVE (T:64) proves nothing about the count or the dedupe.
- Assumption: put the logic in pure helpers: `DefenseInterceptionGate.WouldRemove(IReadOnlyCollection<int> presentIds, ISet<int> forbidden)`, `ObservedSignature(alliance, wouldRemove, reasons)`, and a per-alliance `ObservedLog.ShouldEmit(alliance, signature)` (bound-checked, registered with `CampaignScope.Register("defense-filter-observed-log", ...)`). Test those behaviourally. Pin the patch by source (it calls them; the observed branch tests no verbose flag).
- Grounding: shipped code

**GAP-14. The mark seed is drawn before `CampaignSeed` exists, and `Detect` cannot reach it.** S:290 seeds the rolls with `CampaignSeed ^ (alliance x 7919) ^ DaySerial ^ FNV-1a(UnitKey)`. `EnemyDetectionRuntime.Detect` runs at CampaignPlannerRuntime.cs:110, takes `(snapshot, playerAlliance, fogHonest)`, and has no `PlannerState` (`States` is private to `CampaignPlannerRuntime`, 35). The seed is first set at 124-125, after the Bookkeeping return (119).
- Severity: minor
- Impact: the first review of a new campaign rolls on seed 0 (a load restores the seed first). A bookkeeping-only start delays the real seed further. Still deterministic, but not the stated formula.
- Assumption: move the `CampaignSeed` initialisation above `Detect` and pass `state.CampaignSeed` into `Detect` and on to the ledger. Use the shipped fold `(int)(seed ^ (seed >> 32))` (TheaterPlanner.cs:643-644) and `PlannerState.Fnv1a64` (PlannerState.cs:218).
- Grounding: shipped code

**GAP-15. `ReservesRow` needs the allocator's private sums, and "every review" does not cover paused days.** S:343 defines `[Reserves]` capital held as "the allocator's `SumOf`" and sector held as "the allocator's `Counted` rule". Both are private members of the private `ForceAllocator.Pass` (ForceAllocator.cs:995-1009, 1136-1142) over the pass's arrays (`_role`, `_sector`, `_roleSector`). `ReservesRow` (P:68) is a separate pure file that sees only the published `Allocation`. A second definition of "held" is the exact gap class the claim table exists to end (S:310). Separately, T:11 makes a `[Reserves]` row the review-day denominator, but `ReviewPlan` returns before the plan on StandDown and Bookkeeping days (CampaignPlannerRuntime.cs:115, 119), so paused days emit none.
- Severity: minor
- Impact: two copies of the hold sum can drift. Criteria counted "per review day" silently exclude paused days.
- Assumption: extract internal static `ForceAllocator.CountedHold(snapshot, sectors, allocation, sector)` and `RoleStrength(...)`, used by both `Pass` and `ReservesRow`. State in T:11 that review days are Full-mode reviews.
- Grounding: shipped code

**GAP-16. F5's test with the picture flag off needs a flag path into the pure planner that P never defines.** P:46 and S:332 make `TheaterPlan.MinGarrisonStrength = MinStrengthFallback` when `Enable Strategic Foresight Picture` is off, and P:207 pins `PictureOffKeepsTheOldFloor`. The pure planner has no Plugin access. The shipped pattern is `PlannerSettings` statics refreshed by `StrategicCoordinator` (PlannerSettings.cs:3-10). P names neither that nor an `Inputs` field for the picture flag; F3 owns the mechanism, and F5's test shape depends on its choice.
- Severity: minor
- Impact: F5's pin cannot be written until F3's flag path is known. A shared static also leaks between harness tests unless each test restores it.
- Assumption: F3 adds `PlannerSettings.ForesightPicture` (refreshed by the coordinator beside `PlanTrace`). F5's pin sets it false in a try/finally.
- Grounding: shipped code

**GAP-17. F5's reader list is incomplete, and the fallback warning has no trigger.** S:332 lists the readers of the minimum as `NeedFor`, `HoldNeedAt`, the presumption floor, `ForceAllocator.Target`, `SectorMayGiveScout` and `ReinforcementRouting`. P:203's owned files match that list. Missing are:
  - `ReservesRow` (F1, `need = max(HoldNeed, MinGarrisonStrength)`, S:343).
  - F3's eligibility floor and `Retain` (S:285).
  - `ReconPlanner` (ReconPlanner.cs:497 reads the raw `s.MinStrengthForMovement` with no fallback, so a read of 0 makes every formation eligible).

  On the warning: S:332 says `ReviewPlan` logs `OnceLog.Warning("campaign-planner:min-strength-fallback")` "when the fallback was used", but not how the runtime knows. With the picture flag off the fallback is used by rule, and a Config scenario reads a legitimate 5000. The first-review row's `minStrength=` (P:208) could be the raw read or the plan's value; `LogFirstReview(s, sectors)` has no plan (CampaignPlannerRuntime.cs:454).
- Severity: minor
- Impact: deleting the const forces each missing reader through a compile error with no stated answer. A warning keyed on "plan value == 5000" fires falsely on Config scenarios and on picture-off reviews.
- Assumption: the three missing readers take `plan.MinGarrisonStrength`. `ReconPlanner`'s 14099 mirror reads the same validated value, and its `Context` gets the plan. The runtime warns when the raw `s.MinStrengthForMovement` is not positive or not finite, whatever the flag. `minStrength=` prints `plan.MinGarrisonStrength`, with `plan` passed to `LogFirstReview`.
- Grounding: shipped code

**GAP-18. The F2 observed row is unspecified when nothing would be removed, and its category is unstated.** S:336 emits the observed row "whenever the Prefix reaches the forbidden set (after the ledger's own checks)". The shipped Prefix returns before that when `forbidden.Count == 0` (CheckForDefensiveOperationsCandidateFilterPatch.cs:111). With forbidden ids present but none in `ownunits`, `wouldRemove` is 0. T:65 makes TC-INT-SMOKE inconclusive without at least one observed row per alliance. The category is also open: the filter row it replaces is `TelemetryCategory.Gate` (398), while the `DefenseIntent` tag is registered `State` (TelemetryTagPolicy.cs:145).
- Severity: minor
- Impact: a run where the ledger forbids nothing that vanilla lists reads as inconclusive. The category decides which profile keeps the row.
- Assumption: emit with `wouldRemove=0` whenever `forbidden.Count > 0` (signature-bounded, so once per change). Use category `Gate`, as the filter row does.
- Grounding: shipped code

**GAP-19. `[EnemyBeliefs]`'s `seen=`, `remembered=` and `marks=` cannot be computed as S defines them.** S:291 says "`seen=` excludes marks, `marks=` counts them, `remembered=` excludes mark beliefs refreshed today". Shipped `Report` sets `seen = s.Detections.Count` (every source, garrisons included) and `remembered` = beliefs with `LastSeenDay != s.DaySerial` (EnemyDetectionRuntime.cs:222-225). A mark sets `LastSeenDay = MarkDay`, which is below today for any mark older than today, and the model records no "applied today" flag. P:193 also orders the fields `seen marks contact remembered expired`, while the shipped order is `seen remembered expired contact`.
- Severity: minor
- Impact: `remembered` overcounts every refreshed mark belief, and the smoke parser's field order is ambiguous.
- Assumption: `Update` returns, or exposes, the keys of marks it applied this review (`AppliedMarkKeys`, beside `ExpiredKeys`). `Report` subtracts those from `remembered` and counts them as `marks`, and `seen` counts non-mark detections. Fields are appended (`... contact=<n> marks=<n>`) so the existing order holds.
- Grounding: shipped code

**GAP-20. Four small F4 interface choices.**
  - **`MarkInputs`** (P:182) is used but never defined.
  - **`Offset`** (P:176-177) has no "no previous mark" case. Passing `ageYears = 0` with a zero previous offset makes vanilla's blend (`new x min(1, age x 25) + prev x (1 - that)`, 142709-142712) return a zero offset. Vanilla blends only when a previous mark exists (142707).
  - **Draw order.** The seeded `System.Random` draw order is unstated. Vanilla's is variation 142819, roll 142866, angle 142700, magnitude 142701.
  - **The ledger's home.** Where the per-alliance `MarkLedger` array lives is open (`MarkLedgerIgnoresAllianceTwo`, P:193, needs a pure accessor). So is whether `PruneUnseen`'s 10 means reviews or days: `PruneUnseen(int daySerial)` takes a day, while `PlannerState.PruneAfterMissedReviews` counts missed passes (PlannerState.cs:74, 99).
- Severity: minor
- Impact: naming and unit choices with one plausible answer each, except the offset case, where the wrong call puts every first mark exactly on the true position.
- Assumption:
  - `MarkInputs` carries intel, `spotting0` and `spotting1`, `spottingVariation`, `perkFactor`, `cap`, `minDistance`, `distanceVariation[2]`, `hasClosestEnemy`, closest-enemy X/Z, `active`, `garrison` and `fleet`.
  - `Offset` gets a `bool hasPrevious`.
  - Draws come in vanilla's order.
  - A pure `MarkLedgers.For(alliance)` registers its reset with `CampaignScope.Register`, beside `MoveTrackers` (MoveTracker.cs:340-358).
  - The prune counts missed reviews, as `PlannerState` does.
- Grounding: vanilla / shipped code

## Checked and clean

- F1: the issue-row cap holds by construction. `MoveIssue.Issue` returns `moved == true` only for Issued, or Retried with the move taken (MoveIssue.cs:47-49); a refused retry returns false (31, 44). `RecordIssued` spends budget only when not queued (MoveTracker.cs:114), and `ShouldIssue` caps at `MaxMovesPerDay` (85). So "row only when `moved`" gives at most 6 a review, and a refused retry writes none (P:78).
- F1: `unit=`, `target=`, `from=`, `objective=` and `readiness=` are all available in `Dispatch` (`f`, `a.TargetX/Z`, `ObjectiveOf`, `f.ReadinessStep`; CampaignMover.cs:316-362). The quoting convention is `TelemetryLegacyParser.Quote` (CampaignMover.cs:186; TelemetryLegacyParser.cs:68).
- F1: the trace token ` at=<x>,<z>` fits after `objective@sector` (TheaterPlanner.cs:1475). Candidate `X/Z` is the objective position (336).
- F1: `DefenseFilterTally.Reasons(reasonByUnitId, ids)` has its inputs. `CollectForbiddenIds` already returns `reasonByUnitId` (CheckForDefensiveOperationsCandidateFilterPatch.cs:351-385), and the removal loop (166-175) can collect the removed ids. `EmitDefenseIntentFilter`'s signature string is where `reasons=` goes (397).
- F1: tag registration. `AddCampaign("CampaignMoverIssue", Write)` and `AddCampaign("Reserves", State)` follow the existing pattern (TelemetryTagPolicy.cs:173-192). An unregistered `Reserves` would fall to System/Health (228), which is why it must be registered.
- F1: `[Reserves]` capital held. `SumOf(CapitalGuard)` (ForceAllocator.cs:1136-1142) and `plan.CapitalFloorStrength` are on the published plan and allocation.
- F2: vanilla's candidate loop, its exclusions and the destination-on-own-soil test match S:260 (13595-13610).
- F2: `CampaignWithholdView.TryFreshAllocation` already covers "fresh, executors allowed (the pre-war gate), plan within `MaxAgeDays`". Europe gets `MoveTrackers.For` null, so false (CampaignWithholdView.cs:161-184). `Enable Campaign Planner` and `Enable Campaign Planner Executors` are separate config flags (`CampaignPlannerRuntime.ExecutorsEnabled`, 479-482), which matches `ModeFor`'s inputs.
- F2: a CIC side gets an empty allocation (`ForceAllocator.StandsDown`, 136-149), so `assignmentCount >= 1` separates it.
- F2: the observe path passes `PlannerPatchesReadOnlyPublishedState`. `CampaignWithholdView` and `DefenseInterceptionGate` are not in the forbidden regex or the owner list (CampaignExecutorTests.cs:1341-1351), and reading `Assignments.Count` is no write.
- F2: the Postfix reverts nothing when the Prefix took no snapshot. It is gated on `_defensiveOpsBefore` (208), which only the filter path fills (135).
- F2: `#25` is the only patch that consumes the defence ledger (`grep -ln "DefensePackageAggregator\|StrategicLedgerSet\|FormationDirectiveLedger\|DefenseIntent" src/WhiskeyRealism/Patches/*.cs` returns only CheckForDefensiveOperationsCandidateFilterPatch.cs). `PlannerDefensiveAvailabilityPatch` (#127) is the only planner gate on `IsUnitAvailableForDefensiveOperation` (CampaignAvailabilityFilterPatch.cs:64-92).
- F4: `SpottedEnemy`'s fields are public (`unitsattached`, `regref`, `spottedmarks`, `timeshown`, `intelligence`, `vectorvariation`; 142536-142605), and the instance is on `GameController` (vanilla's own lookup, 67365, 112431).
- F4: the creation chance, its cap, its floor, the zeroing for inactive, garrison and no-closest-enemy units, the 50-unit gate measured from `default(Vector3)` when no mark exists, and both perks match S:258 and S:290 (142812-142866). The first mark therefore comes at the cap (`date - 0` is about 1861 years).
- F4: `GetIntelligenceOnPos` returns 1 while `UseIntelligenceMap` is off (101231-101237), so `MarkRadius` is `intelligencedistancevariationfactor[1]`.
- F4: the angle draw is the int overload `Random.Range(-45, 45)`, giving -45 to 44 (142700). Fleets get no mark, and a mark the opposite grid sees is refused (both 142724).
- F4: `BandRound` matches `TransformNumber`'s bands (80226-80255) plus `GetApproximateValue`'s small-figures branch (150478-150494). Strengths under 100 round to 5 (80251).
- F4: `timefactorspottings` is loaded at 55804-55806 and never read; `spottingfactor` is read only at 101203 and 142819 (grep over the decompile).
- F4: the sidecar change is additive. `EnemyBeliefDto` takes a `baseRadius` with default 0 (PersistenceDto.cs:144-159; SidecarDtoCodec.cs:464-470, 527-531), and `DetectionSources.FromSaved` can map 5 to Mark without disturbing 0-4 (CampaignPlannerContracts.cs:207-212).
- F4: `ReconPlanner.ProbeWanted` reads beliefs directly (ReconPlanner.cs:448-465), as P:163 anticipates, and is the one D7 probe-answer site to skip marks.
- F4: `PlannerState.PruneAfterMissedReviews = 10` exists (PlannerState.cs:74).
- F4b: the one-argument `UnitIsHidden(position)` takes `unittype = -1` and skips the `spottingfactor` term (101179, 101202-101204). A swap of `spottingfactor[0..1]` during `UpdateMarks` therefore changes nothing but 142819.
- F5: `CampaignSnapshot.MinStrengthForMovement` is read from `GamePrefs.aiminimumstrengthformovement` (CampaignSnapshotRuntime.cs:187), and the snapshot resets it to 0 (CampaignSnapshot.cs:65). The fallback rule "not positive" therefore covers an unset snapshot in every hand-built test fixture.
- F5: the only `5000f` under `Strategic/Planner/` is ForceAllocator.cs:86 (grep). The other 5000 mentions are in comments (TheaterPlanner.cs:102), which `PlannerReadCensusTests.CodeLines` strips.
- F5: `ReinforcementRouting.ShortfallStrength` already holds the plan (ReinforcementRouting.cs:152-163).
- F5: the fixtures whose floor changes are findable. Test fixtures set `MinStrengthForMovement` to 1000 (11 sites), 2000 (1) and 5000 (12). `TheaterPlanner.NeedFor` is called directly by ScoutFirstTests.cs:740-745, so the new minimum parameter re-pins those six lines.
