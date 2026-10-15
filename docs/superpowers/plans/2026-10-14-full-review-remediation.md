# Full-Review Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Use superpowers:test-driven-development for every pure-logic change. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix every confirmed defect from the 2026-10-14 full mod review on top of `feat/orchestrator-worker-thread`, verified against the current vanilla build, and merge the result into `main`.

**Architecture:** Eight disjoint workstreams (file ownership is exclusive), each on its own branch/worktree cut from the integration base, implemented test-first, reviewed against vanilla, then merged sequentially into `feat/orchestrator-worker-thread` and finally into `main`. A small shared base commit (write guard + mode cache) lands first so battle workstreams build against one API.

**Tech Stack:** C# netstandard2.1, BepInEx 5.4 + HarmonyX, Unity 2021.3 Mono; console test harness `tests/WhiskeyRealism.Tests`.

**Inputs (read before your workstream):**
- Review: `docs/reviews/2026-10-14-full-mod-review.md` (findings; written against `main` 262a50d)
- Re-anchor table: `docs/reviews/2026-10-14-reanchor-on-branch.md` (every finding located on this branch — use these file:line anchors)
- Tactical logic detail: `docs/reviews/2026-10-14-tactical-logic-review.md`
- Vanilla decompile (current build 14703111): `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`

## Global Constraints

- **Vanilla first.** Before changing any code that reads or writes vanilla state, `rg` the vanilla method(s) in the decompile and READ the full body plus the relevant callers. Record the anchor line(s) in the code header comment and the commit message. A fix that is not grounded in the vanilla body is rejected in review.
- **Exclusive file ownership.** Edit only files your workstream owns (listed per workstream). Exceptions: your own new test file, ONE registration line in `tests/WhiskeyRealism.Tests/Program.cs` `Main` (next to `ObjectiveRecordsFromAggregateTests.Run();`), and `<Compile Include>` lines in `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`. If a fix needs another file, stop that item and report it.
- **No `Plugin.cs` edits.** If you need a config default/description change or a new flag, list it in your report; the integrator applies all `Plugin.cs` changes. Prefer existing flags.
- **Tests:** new tests go in `tests/WhiskeyRealism.Tests/<Workstream>FixTests.cs` as `internal static class <Workstream>FixTests { public static void Run() {…} }`, printing `PASS <name>` per test (follow `ObjectiveRecordsFromAggregateTests.cs`). Use `TestHarness.AssertEqual/AssertTrue/AssertFalse`. Write the failing test first, run it, see it fail, then fix. For runtime-only code, extract the decision into a pure `internal static` function and test that.
- **Patch hygiene (AGENTS.md):** never throw from a patch; every Prefix/Postfix/Finalizer you touch or add wraps its body in `using (TelemetryPerf.Scope("<layer>.patch.<name>", …))`; bounded logs via `OnceLog` with STATIC keys (never embed instance ids/names in OnceLog keys); reflection cached in static fields; try/finally-safe snapshot/restore (use a `[HarmonyFinalizer]` for restores); alliance index bounds-checked (`aifaction` includes alliance 2 = Europe).
- **No new Prefix-skip or Transpiler** without listing it in your report with justification.
- **Default-on policy stays:** fix behavior, don't disable it. Rollback flags must actually roll back.
- **Battle writes** go through `WhiskeyRealism.Tactical.TacticalWriteGuard` (base commit, below): `TacticalWriteGuard.CanWrite(group)` before any vanilla battle-state write; it enforces `Tactical Commander Mode = Active` and W&L/player protection.
- **Clocks:** battle doctrine timers use battle time (`GameVars.currenttimefromstart`, game-hours) — never `Time.realtimeSinceStartup`/`DateTime`/`Environment.TickCount` for doctrine. Real time is allowed only for telemetry throttles and CPU budgets.
- **Verify before reporting:** `./build.sh` (0 errors; do not add warnings — checkpoint has 1) and `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` (all PASS). Paste the final PASS/FAIL counts in your report.
- **Commits:** one commit per finding ID (or tightly related group), message `fix(<area>): <summary> [<IDs>]` + vanilla anchors + trailer `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Run `git status` before committing.
- **Out of scope (do NOT implement):** new 1862-63 historical operation profiles, strategy-depth features (live objective scoring / theater-commander tier / randomness), gap slices (supply-depot executor, naval/blockade/amphibious offense, march-to-the-guns), making playbooks shape plans. Report anything you believe is a bug but falls in these buckets.
- **Report format:** table `ID | status (fixed/partial/deferred) | commit | tests | note`, then "Vanilla anchors read", "Config changes needed (for integrator)", "Deferred + why", final build/test output.

## Integration base

- [ ] **Task 0 (integrator): base commit on `feat/orchestrator-worker-thread`**
  - Create `src/WhiskeyRealism/Tactical/TacticalWriteGuard.cs`:
    - `public static bool ModeAllowsWrites()` — cached `TacticalCommanderModePolicy.AllowsWrites(Plugin mode)`.
    - `public static bool IsPlayerProtected(Regiment group)` and `(Regiment group, out string reason)` — the three W&L checks moved from `BattleCommandPostureExecutorPatch` (`WlOwnershipSafe`, `IsWlCurrentCommandOrChain`, `dlc taken over`), fail-closed.
    - `public static bool IsFeudOrPlayerInstance(AIBattle battle)` — true when `battle.isplayeraiorfeud != 0` and the side is not genuinely AI-controlled (see vanilla `UpdateAITasks` 5857).
    - `public static bool CanWrite(Regiment group)` = `ModeAllowsWrites() && !IsPlayerProtected(group)`.
  - `Plugin.cs`: cache the parsed mode (invalidate on `SettingChanged`), warn once on an unrecognized mode string, bind with `AcceptableValueList("Off","MonitorOnly","Active")`.
  - `Util/TacticalStanceWriter`, `TacticalFormationWriter`, `TacticalCombatBehaviorWriter`: return false unless `TacticalWriteGuard.CanWrite(group)`.
  - `BattleCommandPostureExecutorPatch.IsPlayerProtected` delegates to the guard (keeps its diagnostic emit).

## Workstreams

| WS | Model | Branch | Scope |
|---|---|---|---|
| C1 | Opus | `fix/c1-campaign-lifecycle` | Strategic plan lifecycle, catalog, persistence |
| C2a | Opus | `fix/c2a-campaign-military` | Campaign military write surfaces |
| C2b | Opus | `fix/c2b-campaign-economy` | Campaign economy / admin write surfaces |
| P | Opus | `fix/p-telemetry-infra` | Telemetry pipeline, logging infra, hitch probe, diagnostics & guard patches |
| T1 | Opus | `fix/t1-tactical-evidence` | Orchestrator evidence, geometry, triggers, phase doctrine |
| T2 | Opus | `fix/t2-tactical-lifecycle` | Battle lifecycle, operations director, observer patch, worker thread, caches |
| T3 | Opus | `fix/t3-battle-writers` | Battle writer/gate patches (non-#61) |
| T4 | Opus | `fix/t4-posture-stance` | #61 posture executor + single stance owner + writers |

---

### WS C1 — Campaign plan lifecycle, catalog, persistence

**Owned files:** `Strategic/PhaseTruthLedger.cs`, `Strategic/CIC.cs`, `Strategic/CicReviewRouter.cs`, `Strategic/StrategicCoordinator.cs`, `Strategic/DirectorPublishClamp.cs`, `Strategic/StrategicResilienceDirector.cs`, `Strategic/PersistenceDto.cs` (and any `*Dto*.cs` under `Strategic/`), `Strategic/HistoricalOperationCatalog.cs`, `Strategic/HistoricalOperationContextBuilder.cs`, `Strategic/EraStageManager.cs`, `Strategic/SuccessionScheduler.cs`, `Strategic/OperationalProbeRuntime.cs`, `Strategic/OperationDecisionMemory.cs`, `Patches/PickCampaignObjectivePatch.cs`, `Patches/AICampaignSaveLoadPatch.cs`, `Patches/BattleResultObserverPatch.cs`, `Patches/MonthlyTickHookPatch.cs`.

**Vanilla to read first:** `AICampaign.PickCampaignObjective` (~17769), `CampaignObjective.GetAvailableObjectives` (~178838), `UpdateImportanceValues` (~14906, followed-objective weight), `AICampaign.Load` + call sites (~30003-30023), vanilla save/load path code, `BattleMonument.UpdateAllianceWon` (76496), the daily/monthly date fields read by `MonthlyTickHookPatch`, `Config/campaignobjectives.dat` format (game install `Config/`).

- [ ] **R3 — Recover deadlock.** In `PhaseTruthLedger`, evaluate the phase/plan deadline BEFORE `ForceBelowThreshold`. Reinterpret `RequiredForce`: compare against a LOCAL (target-sector) own:enemy ratio derived from `ForceFractionRequired` as a commitment share, not `f × (own+enemy)` theater-wide. When the force condition clears, restore the phase template's posture flags. Make `Phase.Transition` either evaluated or removed (no dead field). Update the harness test at `Program.cs` (~20064) that currently locks the deadlock in. Tests: deadline expiry advances/replans while force is short; force-short before deadline → Recover; Recover clears when ratio recovers.
- [ ] **R4 — Battle resets.** `RecordBattleOutcome` dirties a CIC only when the battle is major (`IsMajorResult`) AND in the plan's target theater/sector (or within the target radius). Otherwise record history only. Test: minor out-of-theater battle leaves phase index unchanged; major in-theater battle triggers review.
- [ ] **R16 — Director clamp.** Clamp per alliance (or per game day per alliance), never shared. Skip creating/reviewing an AI CIC for the player's alliance outside W&L unless vanilla AI controls it (check how vanilla decides which factions run `AICampaign` logic). Test: two alliances reviewed in the same instant both publish.
- [ ] **R5 — Catalog vs game data.** (a) Add a harness test that parses `Config/campaignobjectives.dat` (copy the needed rows into a test fixture string — the harness must not depend on the game install) and asserts every profile/phase `TargetObjectiveId` exists for the profile's alliance, scenario and chapter. (b) Fix the wrong targets it finds: CSA ops targeting Union-only objectives 31/36; `csa-western-depth`/`union-western-pressure` match vs target mismatch; `csa-capital-defense`/`csa-protraction-defense` must target a CSA-held/defended objective, not objective 4 "Capture Washington" (read the .dat to choose). (c) At match time, reject a profile whose phase targets are unavailable (prevents the daily select→abort loop). (d) NoProfile with doctrine enabled → fall back to the legacy generic plan instead of blocking vanilla in `PickCampaignObjectivePatch`; if no generic plan can be built, let vanilla's `PickCampaignObjective` run (do not freeze `followedcampaignobjective`).
- [ ] **CIC router drift.** Make `CIC.ReviewPlanWithTruth` delegate to `CicReviewRouter` (single implementation; Abort semantics identical) so tests cover the runtime path. Add `Strategic/CIC.cs`, `SuccessionScheduler.cs`, `EraStageManager.cs` to the test csproj; isolate their reflection blocks behind a seam so they compile in the harness.
- [ ] **R15/H3 — Reset between campaigns.** Add `StrategicCoordinator.ResetAll()` clearing ALL per-campaign state (CICs, eras, succession fired/applied ids, battle history, director memories, fiscal memory, defense cooldowns, probe states, signature arrays, ledgers, `LastSeenMonth/Year`, `_forcedAllSuccession`, `_playerCicAnnounced`, `OnceLog` reset stays). Call it (a) at the start of the load Postfix before applying the sidecar, (b) when a new campaign/career starts (find the vanilla new-campaign entry — read how `currentoperation` / scenario setup work around `AICampaign.Load` 30003-30023 and `CampaignController` init), (c) when a save without sidecar loads. Era must be derivable from the date in both directions.
- [ ] **PER-atomic / PER-version.** Sidecar write: temp file + `File.Replace` (with `.bak`) or `File.Move` fallback. Load: check `Version`; build state into temporaries and swap only on full success (transactional `ApplyDto`); null-safe for `Succession`, `FiredEvents`, `MinorOfficer.Personality`; restore `LastSeenMonth/Year`; don't fabricate an all-zero `LastPosture` when fields are missing. Tests for DTO round-trip, missing fields, corrupt JSON.
- [ ] **PER-notpersisted / PER-force.** Persist operational-probe state, defense cooldowns, fiscal memory. `ForceAllSuccessionEvents` must not write forced ids into the sidecar.
- [ ] **C-Probe.** `OperationalProbeRuntime`: on Pause/Withdraw issue a real stop/recall through the same vanilla API path the probe used to commit (read it); reset probe state when the plan target changes; compare the probe formation's strength against LOCAL enemy strength near the probe target (not whole-sector). Add hysteresis between Probe and Withdraw.
- [ ] **C-HardWired.** Wire `EnemyThreatensCapitalCorridor`, `CapitalDangerStreakDays`, `DaysSinceFrontSignatureChange` from real state (enemy strength within a radius of the alliance capital; streak counters advanced on the daily review). Use live positions consistently (same classifier as PhaseTruth). Remove the dead `NearTargetRadius` or use it.
- [ ] **Cadence.** Daily review must process each skipped day's weekly (`day%7`) and monthly gates when fast-forward jumps several days in one frame (don't hardcode `day=1` for the monthly tick; detect month rollover).
- [ ] **H-saveload.** `AICampaignSaveLoadPatch` catch block must null-check `StrategicCoordinator.Instance` before calling into it.

### WS C2a — Campaign military write surfaces

**Owned files:** `Patches/ImportanceValuesPatch.cs`, `Patches/ArmyGroupManagementPatch.cs`, `Strategic/ArmyGroupDoctrine.cs`, `Patches/CommanderReplacementPatch.cs`, `Patches/CommanderAssignmentPreviousCommandPatch.cs`, `Strategic/CommanderAssignmentGuard.cs`, `Patches/DefensiveOpsPatch.cs`, `Patches/CheckForDefensiveOperationsCandidateFilterPatch.cs`, `Strategic/ArmyAreaRuntime.cs`, `Strategic/CoastalDefenseCustomOrderRunner.cs`, `Patches/CoordinatedOffensiveOperationsPatch.cs`, `Patches/CoordinatedOffensiveMicroMovementPatch.cs`, `Strategic/CoordinatedOperationPackageLedger.cs`, `Strategic/CoordinatedOperationRuntime.cs`, `Patches/TransferOfUnitsPatch.cs`, `Strategic/DefenseIntentRuntime.cs`, `Strategic/WlStrategicOrderBridge.cs`.

**Vanilla to read first:** `CalculateMostValueableAIZones` (10964), `CheckOffensiveMovements` (~14166), `IsUnitAvailableForOffensiveOperations` (~14093), `IsWithinOperationsTheater` (~17090), `CheckArmyGroupManagement` (17723), `ArmyGroup.AddUnit` (~75345), `CreateNewArmyGroup` (~75653), `ArmyGroup.AppointCommander` (~75578), `CanBePickedForAIReplacement` (~60748), `RemoveFromCommand` (~60830), `AssignUnitToDefendCapital` (11668; farthest-defender removal ~11778, move loop ~11829), `CheckForDefensiveOperations` (13505) incl. defensive-moving-order handling (~13716, ~13773, ~13842), `CheckTransferOfUnits` (17232, `num4` ~17275), `ZoneRecruiting` (~12507), `UpdateCampaignTheaters` (~17084), `CheckCombinationOfUnits` (~17141), `SeaInvasion.GetUnitStrengthInRange` (141061/141066), `SceneManagement` fields (36335+).

- [ ] **R7 ImportanceValues.** Replace the hard override of `mostvalueableaiareaclose[aifaction]` for every AIArea with: only for areas inside the plan's theater AND within vanilla's distance gate AND passing vanilla's supply-capacity gate (`ownpoints <= supplycapacity`); preferably as an importance bonus that lets vanilla's selection still win elsewhere. Units with no `theaterposition` must not be pulled cross-map.
- [ ] **C-ArmyGroup.** Repeat vanilla's guard (`!GameVars.allowarmygroups || !alliance.AllowsArmyGroups() || (DLC_WL.dlc_scenarioactive && DLC_WL.IsCommanderInChief())`) at Postfix top. `FindPreferredCommander`: filter with `CanBePickedForAIReplacement(alliance, includeofficerswithcommand: true)`, available/alive, not already an army-group commander, not `dlc_chosencommander`, not on the player's staff. `ExistingGroupFor` applies the same player-group exclusion as `GetArmyGroupInRange`; `IsEligibleTopUnit` excludes `dlcw_isundercommander`.
- [ ] **C-CmdrReplace.** Resolve the target army group by `ReplacedRole` + area/theater, not by first commander index. Replace via `ArmyGroup.AppointCommander` (so `commanderid` changes). Drop the `currentcommand != null` requirement that vanilla's own flow invalidates. Disambiguate names (first+last or commander id; handle Lee/Johnston/Hill/Smith/Anderson); require availability via `CanBePickedForAIReplacement`; exclude `dlc_chosencommander`. `CommanderAssignmentPreviousCommandPatch`: don't leave the previous unit with `commander = -1` when a replacement held a command.
- [ ] **C-DefOps.** Convert to a Prefix pre-pledge (or issue the same vanilla move toward the capital) so vanilla's move loop actually moves the candidate; skip units in the coastal runner's custom-order set; no add/remove churn.
- [ ] **B-46-campaign (Finalizer).** `CheckForDefensiveOperationsCandidateFilterPatch`: move the restore into a `[HarmonyFinalizer]` (keep Postfix idempotent or remove it).
- [ ] **C-ArmyArea.** `IsIdleForAreaOrder` must reuse vanilla's exclusion set: `groupstodefendcapital`, supply-depot builders (`unitsconstructingsupplydepots`), fort orders, raid forces, sea-invasion forces, units in offensive/defensive ops. Drop the `theaterposition = anchor` write; rely on the move order.
- [ ] **C-Coastal.** Remove units from `unitsinoffensiveoperations` too; queue through `DefensiveMovingOrder` (match vanilla's structure) or target a position that keeps the enemy within bugle range so vanilla doesn't stop/remove them.
- [ ] **C-CoordOff.** Clear `ownunits` only when the package actually committed. Mirror `IsUnitAvailableForOffensiveOperations` for candidate eligibility. Honour `recruitingzonesupdated` (≤1 commit per faction job cycle). Require `Ratio >= RequiredReinforceRatio` for `SingleLead`; apply chapter-aggression and winter gates like vanilla. Gate the per-call signature/classify work behind vanilla's early-return conditions; cache reflection. Clear package locks set through W&L current-order commits.
- [ ] **C-Transfer.** Steer only when vanilla's deficit is null or less severe than the plan target's shortfall (or alternate passes); restore `positiondeficit` in a `[HarmonyFinalizer]`.
- [ ] **C-DefIntent.** `AccessTools.Method(SeaInvasion, "GetUnitStrengthInRange", new[]{typeof(int),typeof(bool),typeof(int)})`; log once if null.
- [ ] **C-DeadRefl-scene.** `WlStrategicOrderBridge`: replace `currentcampaign`/`currentsave` with real fields (`SceneManagement.currentcampaignscenario` + the vanilla save-name field — find it) so the context key is unique.

### WS C2b — Campaign economy / admin write surfaces

**Owned files:** `Patches/RecruitmentPatch.cs`, `Strategic/RecruitmentIntentLedger.cs`, `Patches/FinancialAIPatch.cs`, `Strategic/Fiscal/*`, `Patches/PolicySelectionPatch.cs`, `Patches/EconomyConstructionPatch.cs`, `Strategic/Construction/*`, `Patches/FortConstructionGovernorPatch.cs`, `Patches/CampaignAiUpdateGovernorPatch.cs`, `Patches/FastForwardAiCatchUpPatch.cs`, `Patches/PerkSelectionPatch.cs`, `Strategic/PerkSelectionScorer.cs`, `Patches/WlCareerStartSelectionRetryPatch.cs`.

**Vanilla to read first:** `ZoneRecruiting` (~12507, abort at `strengthRatio > 1f && num13 == 1` ~12606), recruiting bonus (~12337), financial AI (~15383-15444), `UpdateCompanyFoundationList` (~15034, ~15286-15295), `CheckFortConstruction` (16347-16367, `IsFactionAlreadyConstructingFort`), `AICampaign.Update` (11159-11240, `sqrt(gamespeed)` loop ~11195), `UpdateUnitAI` job table (11304, 11420-11435), `CheckPerkSelection` (11871, `Random.Range(0, n/3-1)` ~11880), `Policies.CheckAIPolicyChange`, `CampaignController` career start (~30154), `ShowStartUnitSelectionList` (182353).

- [ ] **C-Recruit.** Drafts-only eligibility requires `StrengthRatio <= 1`. With no active plan / `TargetObjectiveId == -1`, `DefaultRecruitmentTheater` returns `Theater.Unknown` and steering falls back to vanilla.
- [ ] **C-Fiscal.** Replace point snapshot (`min=max=current`) with directional floor/ceiling bands so vanilla's tax/subsidy nudges in the allowed direction survive; point band only in crisis. Apply the King Cotton lane-0 target or remove it. Supply stress: `LowSupplyFormationCount > 0` must not block Expansion/policy research near-permanently — use a proportion/threshold and never `return false` from `PolicySelectionPatch` when the penalised policy is the only option.
- [ ] **C-EconConstr.** Bias `bestiipplacesprob` only for vanilla's selection and restore the raw value afterward (Prefix/Postfix/Finalizer); don't let inflated stale values block better sites or inflate build rate.
- [ ] **C-ConstrRuntime.** `TopSupplyTheater`: compare like with like (area key vs area key, or map area→Theater). Capital threat per site, not a sector average.
- [ ] **C-FortGov.** Short-circuit on vanilla's early-return conditions before scoring; cache scores per job cycle; allow high threat to exceed the soft cap.
- [ ] **C-Governor.** Pass caps must not starve job-cycle work at 20x/50x: guarantee at least vanilla's job progression per game day (scale budget with game days elapsed), and do not disable `FastForwardAiCatchUpPatch` when the governor is on (or make the governor implement the catch-up).
- [ ] **C-Perk.** Match vanilla's candidate range `Random.Range(0, n/3-1)` semantics (AI never gets the last perk) and add a weighted-random tiebreak among near-equal scores so selection is not fully deterministic.
- [ ] **P-wlretry.** `WlCareerStartSelectionRetryPatch`: bail on `WlCareerStartPending()` first; cache panel members only when the panel instance changes; cache `TypeByName`/`GameObject.Find` results.

### WS P — Telemetry pipeline, logging infra, hitch probe, diagnostics & guard patches

**Owned files:** `Telemetry/*` (all), `Util/OnceLog.cs`, `Util/Reflection.cs`, `Patches/TacticalPatchIds.cs`, `Patches/TacticalRegimentDiagnosticsPatch.cs`, `Patches/TacticalMapKnowledgeDiagnosticsPatch.cs`, `Patches/TacticalFallbackRetreatNullGuardPatch.cs`, `Patches/EconomyAllianceDataGuardPatch.cs`, `Patches/EconomyFilterMapInitializationGuardPatch.cs`, `Patches/EconomyFilterMapAssignFiltersBootstrapPatch.cs`, `Patches/WlCampRealismPatch.cs`, `Patches/WlOperationNullGuardPatch.cs`, `Patches/MilitiaActStateHandoverGuardPatch.cs`, new `Telemetry/TelemetryFrameProbe.cs`.

- [ ] **P-telemetry-rows.** `TelemetryPerf.Scope`: emit a full row only when `slow`; aggregate sub-threshold calls per scope on the main thread (count/sum/max) and emit one aggregate row per scope per ~1 s of real time. Avoid allocating per call (no closure, no `Stopwatch` allocation — use `Stopwatch.GetTimestamp()` in a struct/pooled scope; `NoopScope` path stays alloc-free). Cache sanitized scope names. Wire the `Telemetry Performance Warnings` flag to the slow-row path.
- [ ] **TEL-queue / TEL-prio.** O(1) detail/protected counters; ring buffer or per-priority queues; fix protected-row eviction bug; Performance rows must not evict Decision/State rows.
- [ ] **P-queue / TEL-sink / TEL-run.** Writer keeps one open `StreamWriter` per stem (flush per batch, close on rotate/shutdown); manifest written on rotation/shutdown/≥5 s timer only; transient write failures retry with backoff and disable the sink only after N consecutive failures; `Run()` has an outer try/catch that logs once and keeps the thread alive; `FlushUntilEmpty` is bounded.
- [ ] **P-shutdown.** Remove the duplicate manifest/summary writes; keep slowest scopes as a bounded top-8 incrementally; summary at shutdown streams (`File.ReadLines`) and is skipped/bounded above a size threshold.
- [ ] **Router.** `CurrentProfile` read without a lock (volatile); `DiagnosticsSnapshot()` cheap; `WithDecision` blank signature does not drop the event silently; `IsSeriousLine` matches whole tokens, not substrings.
- [ ] **P-probe (hitch probe).** `Telemetry/TelemetryFrameProbe` with `Tick(float unscaledDeltaTime)` called once per frame (integrator wires the call from `Plugin.Update`). For frames > 250 ms emit a protected row with: duration, `GC.CollectionCount(0..2)` deltas, `GC.GetTotalMemory(false)`, Mono heap used/size (`UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong/GetMonoHeapSizeLong`), top-level scopes entered this frame with durations (thread-static list populated by `TelemetryPerf`), telemetry queue depth. Keep max and count > 500 ms in the session summary.
- [ ] **OnceLog.** Check the fired set before building the message; add `OnceLog.Claim(string key)`; bound `_fired` (static keys only — document that dynamic keys are forbidden).
- [ ] **P-nodeid.** `TacticalPatchIds.NodeIdMatches` must not allocate: parse the numeric suffix of `nodeId` once (`"node-"` prefix, optional `-` sign) and compare ints. Same contract; add tests including negative ids.
- [ ] **P-regdiag / map knowledge.** Regiment diagnostics: default watch list must be empty (integrator changes the `Plugin.cs` default; you make empty list a fast no-op); cache matched instance ids per battle, rescan only when `completeunitlist.Count` changes. Map-knowledge diagnostics: build anchors once per battle.
- [ ] **H-finalizer.** Null-guard Finalizers suppress only exceptions whose stack/site matches the guarded vanilla method body (e.g. check `__exception is NullReferenceException` AND the throwing frame is the vanilla method, not a mod Postfix); `EconomyFilterMapInitializationGuardPatch` forces completion only for the known exception types it guards; keep bounded logging. WlCampRealism diary cycle-4: treat null `groupsupplystate` as safe (vanilla handles it at ~183044).
- [ ] **Util/Reflection.** `Warn` bounded via OnceLog; delete dead helpers.

### WS T1 — Orchestrator evidence, geometry, triggers, phase doctrine

**Owned files:** `Tactical/Orchestrator/ArmyEvidenceBuilder.cs`, `TacticalUnitObservationAggregate*.cs`, `EnemyVisibleState.cs`, `ArmyIntentInference.cs`, `ArmyTickCycle.cs`, `ArmyReplanTriggers.cs`, `TacticalPhaseProgressionDoctrine.cs`, `TacticalSectorReadinessDoctrine.cs`, `TacticalCommandTreeProbe.cs`, `TacticalLeafBrigadeMap.cs`, `TacticalRoleCascade.cs`, `DirectChildAllocator.cs`, `TacticalReinforcementOpportunityDoctrine.cs`, `TacticalOpportunityHysteresis.cs`, `TacticalMainArrivalSynchronizer.cs`, `ArmyOrchestrator.cs`, `HistoricalFigureRegistry.cs`, `Plays/*`, plus a new pure `Tactical/Orchestrator/TacticalFrontAxis.cs`. In `TacticalBattleCoordinatorRuntime.cs` T1 owns ONLY the sector-index map / role-allocation region (`BuildInstanceToSectorIndexMap` and the corps-role allocation call sites ~1642-1765); T2 owns the rest of that file.

(Detailed items are added after the branch-delta review lands; see "Battle workstreams addendum" below.)

### WS T2, T3, T4

See "Battle workstreams addendum" below.

---

## Battle workstreams addendum

_(Filled in by the integrator after the branch-delta review.)_

## Merge & release

- [ ] Merge order into `feat/orchestrator-worker-thread`: base (Task 0) → P → C1 → C2a → C2b → T1 → T2 → T3 → T4. After each merge: build + full harness. Resolve `Program.cs`/csproj registration conflicts by keeping both lines.
- [ ] Integrator applies all `Plugin.cs` changes (config table in review §6 + workstream requests), wires `TelemetryFrameProbe.Tick` into `Plugin.Update`.
- [ ] Final review pass over the merged diff (Opus) against vanilla for cross-workstream interactions (writer ownership, clocks, protection).
- [ ] Docs: `docs/handoff.md`, `docs/patch-catalog.md`, `docs/findings.md` (266,136 lines; anchors), `AGENTS.md` (narrow transform.parent rule; Mode contract), `docs/telemetry.md`, `MEMORY.md`; archive this plan.
- [ ] Fast-forward/merge `feat/orchestrator-worker-thread` into `main` (no PR). Build from `main`, record DLL SHA-256.
- [ ] Deploy + hash verification requires BepInEx in the game folder (currently wiped) — ask the user.
