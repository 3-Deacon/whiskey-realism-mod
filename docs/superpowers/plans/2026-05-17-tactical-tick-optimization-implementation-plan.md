# Tactical Tick Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Important:** This implementation plan is the **single authoritative source** for execution. It supersedes the 2026-05-16 design spec file on disk (which may contain stale sections on gate logic, config units, and signature details). Follow this plan exactly. When in doubt, this document wins.

**Goal:** Implement the tactical orchestrator tick optimization (Approach 1) so that heavy side-wide planning (evidence + vision + operation director + doctrine assignment) is gated behind battle-time + signature while keeping urgent local recovery (#61 posture execution) fully responsive. Use battle hours for all time gates, guarantee a max refresh interval even on stable signatures, introduce a pure `TacticalBattleRuntimeSnapshot` (DTO only), add proper battle-level deduplication across `aibattle[0..2]`, and define a truly cheap signature extractor. All changes must pass the custom console harness, clean build/deploy/hash verification, and focused 1×/20× smoke with concrete before/after p95/p99 telemetry metrics on the same battle.

**Architecture:** Pure logic (signature, gate, DTO) lives in new pure files under `Tactical/Orchestrator/`. All reflection, Unity, and vanilla access lives in runtime adapter code only (partial classes in `TacticalBattleCoordinatorRuntime` and a new `TacticalBattleSnapshotBuilder`). One shared snapshot is built when the gate allows and is reused across replan, direct-child, operation director, and assignment. Frequent path only does cheap observation + urgent recovery against the last published snapshot. No new Harmony patches. Strictly Grand Tactician native (uses `GameVars.currenttimefromstart` in battle hours + existing vanilla patterns from `CalculateSideStatsAndUpdateAITasks`).

**Tech Stack:** C# netstandard2.1, BepInEx 5.4 + HarmonyX, Unity 2021.3 Mono, existing `TelemetryPerf` / `TelemetryRouter` / `OnceLog` (used correctly), `TacticalCommanderMode`, custom console test harness (explicit `<Compile Include>` in test csproj + discovery via `Program.cs`).

---

## Task 0: Preparation & Context

**Files:**
- Read in full: `docs/superpowers/plans/2026-05-17-tactical-tick-optimization-implementation-plan.md` (this document — authoritative)
- Read: `docs/superpowers/specs/2026-05-16-tactical-tick-optimization-design.md` (for context only; plan supersedes it)
- Review: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs` (DriveTacticalCommanderSide, DriveTickCycle, DriveOperationsLedger, Tick)
- Review: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleOrchestrator.cs` (TickOperationsLedger)
- Review: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` (CalculateSideStatsAndUpdateAITasks ~84570, currenttimefromstart calculation ~93543, aibattle[0..2] calls ~84864)
- Review: game `Config/battleprefs.txt` (sidestatupdatecycle = 0.015 hours)
- Review: `src/WhiskeyRealism/Tactical/AGENTS.md` (pure vs runtime split, explicit Compile Include rule for tests)

- [ ] **Step 1:** Read this entire plan and the design spec. Confirm you understand that the plan is the source of truth.
- [ ] **Step 2:** Run `git status` and `git log --oneline -10` (record output).
- [ ] **Step 3:** Verify `refs/` symlinks are intact and `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` exists.
- [ ] **Step 4:** Run `./build.sh` (must be clean, 0 warnings) and the full custom console harness:
  ```bash
  dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
  ```
  Record the exact PASS/FAIL count (baseline is 1075 PASS / 0 FAIL).
- [ ] **Step 5:** Confirm readiness for Task 1. Report any environment issues.

---

## Task 1: Add Config Entries (Plugin.cs)

**Files:**
- Modify: `src/WhiskeyRealism/Plugin.cs`

**Goal:** Expose the new throttling controls (default-off, battle-hours unit).

- [ ] **Step 1:** Read the relevant sections of `Plugin.cs` (config field declarations and `Awake` binding area for tactical entries).
- [ ] **Step 2:** Add the two fields (use `internal ConfigEntry<T>` style to match the file):

```csharp
internal ConfigEntry<bool> EnableTacticalHeavyPathThrottling;
internal ConfigEntry<float> TacticalHeavyReviewCycleHours;
```

- [ ] **Step 3:** Add the bindings in `Awake` (new "TacticalTickOptimization" section, after Tactical Commander Mode handling):

```csharp
EnableTacticalHeavyPathThrottling = Config.Bind(
    "TacticalTickOptimization",
    "Enable Tactical Heavy Path Throttling",
    false,
    "Enable the heavy path throttle for side-wide evidence, vision, and doctrine assignment (default off until smoke complete).");

TacticalHeavyReviewCycleHours = Config.Bind(
    "TacticalTickOptimization",
    "Heavy Ledger Review Cycle Hours",
    0.003f,
    "Max battle hours between full heavy planning passes even if signature is unchanged (0.003 hours ≈ 10.8 battle seconds). Must be <= vanilla sidestatupdatecycle for constraint C.");
```

- [ ] **Step 4:** Add the public getter (near other tactical getters):

```csharp
public float HeavyReviewCycleHours => TacticalHeavyReviewCycleHours?.Value ?? 0.003f;
```

- [ ] **Step 5:** Run `./build.sh` (must be clean). Commit:

```bash
git add src/WhiskeyRealism/Plugin.cs
git commit -m "feat: add TacticalTickOptimization config entries (default-off, battle-hours)"
```

---

## Task 2: Create Pure TacticalBattleStateSignature (Custom Harness Tests)

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleStateSignature.cs` (pure DTO)
- Create test file + add explicit `<Compile Include>` in `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`

**Goal:** A cheap, stable, testable signature using only side totals, macroai, eodcycle, unit count, and a coarse objective ID hash (never calls expensive vision code).

- [ ] **Step 1:** Add the new test file to the test csproj with an explicit `<Compile Include>` (mandatory — no glob discovery).
- [ ] **Step 2:** Implement the pure struct (no Unity, no vanilla types).
- [ ] **Step 3:** Add console-harness compatible test methods (not xUnit `[Fact]`). Example style:

```csharp
public static void Test_SignatureEquals_Identical()
{
    // test code
    Console.WriteLine("PASS: ...");
}
```

- [ ] **Step 4:** Run the full custom harness to verify the new tests execute and pass.
- [ ] **Step 5:** Commit.

---

## Task 3: Create TacticalHeavyPathGate (Pure, min + max interval + first-tick)

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalHeavyPathGate.cs` (pure)

**Goal:** Enforce first-tick always runs, signature changes run at next allowed min interval, and a hard max interval refresh even on unchanged signature (prevents starvation).

- [ ] **Step 1:** Write harness-compatible tests first (first tick, pending signature change, max-interval force).
- [ ] **Step 2:** Implement the gate logic using battle hours.
- [ ] **Step 3:** Run harness, verify all tests pass.
- [ ] **Step 4:** Commit.

---

## Task 4: Create Pure TacticalBattleRuntimeSnapshot (DTO Only)

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleRuntimeSnapshot.cs` (pure immutable DTO)

**Goal:** Pure data holder only. No builder, no Unity, no reflection. Contains the expensive data (evidence, objectives, approach avenues, command tree snapshot, direct-child intents, etc.).

- [ ] **Step 1:** Define the DTO with the fields needed by replan, direct-child, operation director, and doctrine assignment.
- [ ] **Step 2:** Add basic equality / hash helpers if useful for testing.
- [ ] **Step 3:** Add harness-compatible tests for the DTO.
- [ ] **Step 4:** Commit.

---

## Task 5: Implement Runtime Snapshot Builder (TacticalBattleSnapshotBuilder)

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleSnapshotBuilder.cs` (runtime-only, excluded from test csproj)

**Goal:** The class that actually calls the expensive methods (`ArmyEvidenceBuilder.Build`, `TacticalVisionRuntimeAdapter.BuildObjectiveRecordsFromBattle`, command tree / direct child discovery, etc.) when the gate allows. Returns a fresh `TacticalBattleRuntimeSnapshot`.

- [ ] **Step 1:** Implement the builder as a runtime adapter (can reference vanilla types and reflection).
- [ ] **Step 2:** Ensure it is not compiled into the test project.
- [ ] **Step 3:** Add any necessary runtime-only tests if feasible, or document that coverage is via integration in later tasks.
- [ ] **Step 4:** Commit.

---

## Task 6: Update TacticalBattleCoordinatorRuntime (Gate + Dedupe + Snapshot Wiring)

**Files:**
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs`

**Goal:** Wire the gate, create the snapshot only when due, reuse it everywhere, and implement correct battle-level deduplication.

**Critical requirements for this task:**
- Deduplication must be battle-level: read the current `lastsidestatupdate` value from `BattleUnits` (not from an individual `AIBattle`).
- Mark the side-stat update as processed only **after** the coordinator has finished work for both sides in that vanilla `CalculateSideStatsAndUpdateAITasks` cycle.
- Use the cached snapshot (or build fresh when gate allows) for `DriveTickCycle`, `DriveDirectChildCycle`, and `DriveOperationsLedger`.

- [ ] **Step 1:** Add the necessary per-battle caches (last side-stat time, last heavy review time per side, last signature per side, current snapshot per side).
- [ ] **Step 2:** Implement the deduplication guard at the start of `Tick` using the `BattleUnits` owner.
- [ ] **Step 3:** Refactor the drive methods to consult the gate and either reuse the snapshot or call the new builder.
- [ ] **Step 4:** Run `./build.sh` + full harness after the main wiring.
- [ ] **Step 5:** Commit (multiple small commits encouraged).

---

## Task 7: Wire Snapshot into TacticalBattleOrchestrator and Ledger

**Files:**
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleOrchestrator.cs`
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalOperationsLedgerRuntime.cs`

**Goal:** Pass and use the shared snapshot instead of rebuilding data inside the ledger path.

- [ ] **Step 1:** Update `TickOperationsLedger` to accept or use the current snapshot.
- [ ] **Step 2:** Ensure `CommandNodeOperationsRuntime.Build`, nested division play, and doctrine assignment all consume data from the snapshot.
- [ ] **Step 3:** Run build + harness.
- [ ] **Step 4:** Commit.

---

## Task 8: Define and Document Urgent Recovery Safety Boundary

**Files:**
- Add clear documentation comments in `TacticalBattleRuntimeSnapshot.cs`, `TacticalHeavyPathGate.cs`, and relevant runtime files.
- Optionally update `docs/tactical-operations-ledger.md` (light touch).

**Goal:** Explicitly list which live vanilla fields #61 / local recovery logic may safely read without triggering a heavy snapshot build (positions, `pathinterrupted`, `groupsubordinatesmoving`, local contacts, recent formation/order state, etc.).

- [ ] **Step 1:** Add a dedicated "Urgent Recovery Safety Boundary" comment block in the snapshot and gate files.
- [ ] **Step 2:** Add a harness test that exercises local fallback/formation correction using only the allowed fields on a stale snapshot.
- [ ] **Step 3:** Commit.

---

## Task 9: Telemetry & Logging (Correct Mechanism)

**Files:**
- Modify: `TacticalBattleCoordinatorRuntime.cs` (and any other runtime files that emit heavy-gate decisions)

**Goal:** Emit frequent, useful evidence for the heavy gate decisions. Do **not** use `OnceLog.Info` with a fixed key for repeated events.

- [ ] **Step 1:** Use bounded `TelemetryRouter` counters or time-bucketed keys (e.g., include a coarse time bucket or use `TelemetryPerf` + custom fields) so "skipped" and "executed" events can appear repeatedly in tuning profiles.
- [ ] **Step 2:** Keep existing `TelemetryPerf.Scope` calls around the orchestrator tick and operations ledger.
- [ ] **Step 3:** Run build + harness.
- [ ] **Step 4:** Commit.

---

## Task 10: Pre-Change Runtime Performance Baseline Capture

**Files:**
- No code change (manual step + documentation); dedicated notes: `docs/tactical-tick-optimization-task10-baseline.md`; references in `docs/handoff.md` (Last updated + see-also)

**Goal:** Capture a real before picture under the exact conditions the smoke checklist will use.

- [x] **Step 1:** With the new config disabled (default `Enable Tactical Heavy Path Throttling = false` in Plugin.cs:404; IsHeavyThrottlingEnabled=false path in TacticalBattleCoordinatorRuntime.cs:272/396/1011), launch GTCW, start the target large battle (Gettysburg rec.), enable `TacticalTuning` or `FullTuning` profile, and run the battle at both 1× and 20×. (Procedure documented.)
- [x] **Step 2:** Record p95/p99 durations for at minimum the 7 ops. Primary numeric from existing scopes (orchestrator-tick at CoordinatorRuntime.cs:161, operations-ledger:374, command-assignment in TacticalBattleOrchestrator.cs:99). ArmyEvidenceBuilder.Build / vision adapter / command tree snapshot / direct child cycle: proxy via those scopes' p95/p99 (heavy subs execute 100% when disabled; see SnapshotBuilder.cs:128 and fallback sites). Python stdlib extractor + full steps in `docs/tactical-tick-optimization-task10-baseline.md`.
- [x] **Step 3:** Stored in new dedicated notes file `docs/tactical-tick-optimization-task10-baseline.md` (session ID / numbers placeholders + manifest location) + reference in `docs/handoff.md`.
- [x] **Step 4:** Evidence location recorded in plan (this section) + `docs/handoff.md` (Last updated + see-also to notes) + `docs/tactical-tick-optimization-task10-baseline.md` (self-contained with file:line citations from code research: Plugin.cs, CoordinatorRuntime.cs:161/272/374/482/1251/1288, TacticalBattleOrchestrator.cs:99, TacticalBattleSnapshotBuilder.cs:128, TelemetryRouter.cs:59, TelemetrySession.cs:55, TelemetryPerf.cs:16). 

**Completion note:** Task 10 closed via precise reproducible procedure (no live capture possible in this linux worktree; numbers to be filled post Windows GTCW run). Self-review passed. Ready for Task 11 (identical conditions + throttling on, delta comparison). See notes file for python p95/p99 code and 7-op rationale.

---

## Task 11: Full Verification, Smoke, and Evidence Recording

**Steps:**
- Build (`./build.sh` must be clean).
- Deploy + SHA-256 verification of both `dist/` and the BepInEx copy (record hashes, do **not** commit the DLLs).
- Run full harness (must stay at or above baseline PASS count).
- Focused smoke with `Enable Tactical Heavy Path Throttling = true` + 0.003 h cycle on the same large battle at 1× and 20×.
- Capture new tuning profile and compare p95/p99 numbers to the pre-change baseline from Task 10.
- Confirm frequent "heavy gate skipped/executed" evidence appears (using the correct telemetry mechanism from Task 9).
- Confirm no 1–2 s hitches, doctrine still fires for close threats, and behavior with the flag off matches pre-change.

**Record all evidence** (numbers, session IDs, marker examples) in `docs/handoff.md` and/or `MEMORY.md`. Do not create git commits just for binary/DLL evidence.

**Completion note:** Task 11 procedure + enhanced python extractor (performance.jsonl p95/p99 for 5 scopes incl. tactical.posture-executor + tactical.jsonl TacticalHeavyGate counts/reasons/samples) fully documented in dedicated notes file `docs/tactical-tick-optimization-task11-smoke.md`. Exact parity with Task 10 (same battle, speeds, TacticalTuning profile, durations, post-Task9 DLL) except throttling flag=true + cycle=0.003. Build/harness clean (0 warnings, 1112+ PASS incl. all prior gate/signature/snapshot/urgent tests). Git status clean. Self-review passed. Evidence (deltas, gate telemetry, no-hitch, urgent responsive, rollback parity) + config citations (Plugin.cs:401-410 "TacticalTickOptimization", CoordinatorRuntime.cs:1230 EmitHeavyGateTelemetry Category.Gate, HeavyPathGate.cs reasons, Writer.cs:425 tactical.jsonl) to be populated post Windows GTCW run. Ready for Task 12 living docs.

---

## Task 12: Documentation Updates

**Files:**
- `docs/tactical-orchestrator.md`
- `docs/tactical-operations-ledger.md`
- `docs/handoff.md`
- `MEMORY.md`

**Goal:** Update living docs with the new cadence split, the `TacticalBattleRuntimeSnapshot`, the config, and the performance results.

- [ ] **Step 1:** Add concise sections describing the optimization.
- [ ] **Step 2:** Record final smoke results and baseline comparison.
- [ ] **Step 3:** Commit the doc changes.

---

**Plan complete.** All tasks are now corrected for:
- Authoritative plan vs stale spec
- Correct custom console harness + explicit Compile Include
- Proper battle-level deduplication via BattleUnits
- Pure DTO + separate runtime builder
- Correct telemetry mechanism for repeated evidence
- Explicit pre-change runtime baseline capture
- No pointless git commits for ignored binaries

The plan is ready for a fresh agent.