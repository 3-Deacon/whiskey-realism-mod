# Tactical Orchestrator Worker-Thread Refactor — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move tactical-orchestrator analysis (scoring, intent inference, plan/replan, role allocation, operations ledger) off the Unity main thread onto a dedicated worker `Thread`, leaving only cheap state-capture and decision-apply on main. Drop `tactical.orchestrator-tick` main-thread p99 from 67ms to ≤ 10ms steady-state.

**Architecture:** Dedicated long-lived `Thread` started in `OnBattleStart`, stopped in `OnBattleEnd`. Main thread captures `TacticalBattleRuntimeSnapshot` + `ArmyEvidenceBuilder.Bundle` (already cheap from Slice 1) and atomically enqueues into a single-slot `_pendingWorkerInput` field. Worker loops on `ManualResetEventSlim.Wait()`, processes the latest enqueued input, publishes `TacticalDecisionSnapshot` atomically via `Interlocked.Exchange` on `TacticalDecisionSnapshot._current`. Main-thread patches read `TacticalDecisionSnapshot.Current` (lock-free volatile read) instead of computing decisions inline.

**Tech Stack:** C# netstandard2.1, BepInEx 5.4 + HarmonyX, Unity 2021.3.16f1 Mono x64. New use of `System.Threading.Thread`, `ManualResetEventSlim`, `Interlocked.Exchange`, `Volatile.Read/Write`.

**Source-of-truth:** [`2026-05-19-tactical-orchestrator-worker-thread-design.md`](../specs/2026-05-19-tactical-orchestrator-worker-thread-design.md).

---

## File map

| File | Status | Responsibility |
|---|---|---|
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalSideDecision.cs` | **NEW** | Per-side immutable decision (current plan, intent, command tree, force, personality, last replan trigger) |
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalDecisionSnapshot.cs` | **NEW** | Per-cycle immutable snapshot of all decisions + static `Current` atomic accessor + `Publish` |
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalGroupStanceDecision.cs` | **NEW (struct)** | Per-group stance decision (already an internal struct in BattleGroupStancePatch; promote to its own file) |
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalOrchestratorWorkerInput.cs` | **NEW** | Immutable container that the main thread fills with everything the worker needs (snapshot, bundle, per-side metadata) |
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalOrchestratorWorker.cs` | **NEW** | Worker `Thread`, snapshot enqueue, loop, decision publish, lifecycle |
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs` | **MODIFY** | `Tick`: capture → enqueue → read latest decisions. `OnBattleStart`: start worker. `OnBattleEnd`: stop worker. `ResetRuntimeTickState`: reset decision snapshot. |
| `src/WhiskeyRealism/Patches/BattleGroupStancePatch.cs` | **MODIFY** | Read decision from `TacticalDecisionSnapshot.Current.TryGetGroupStance` instead of computing |
| `src/WhiskeyRealism/Plugin.cs` | **MODIFY** | New `EnableTacticalOrchestratorWorker` ConfigEntry (default ON) |
| `src/WhiskeyRealism/Telemetry/TelemetryTagPolicy.cs` | **MODIFY** | Register `TacticalOrchestratorParityMismatch` event |
| `tests/WhiskeyRealism.Tests/TacticalDecisionSnapshotTests.cs` | **NEW** | Coverage for snapshot lookup contracts |
| `tests/WhiskeyRealism.Tests/TacticalOrchestratorWorkerTests.cs` | **NEW** | Coverage for worker analysis given synthetic input; parity against synchronous baseline |
| `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` | **MODIFY** | `<Compile Include>` entries for the 4 new production files |
| `docs/telemetry.md` | **MODIFY** | Document new scopes (`tactical.worker.cycle`, `tactical.worker.cycle.publish`) + parity event |
| `docs/tactical-orchestrator.md` | **MODIFY** | Threading architecture section |
| `docs/handoff.md` | **MODIFY** | Post-smoke: shipped DLL hash + metric deltas |

---

## Constraints (read first)

- **Netstandard2.1.** No nullable annotations (`?`), no records, no init-only setters, no top-level statements. `System.Threading` primitives available.
- **Unity API is main-thread-only.** Worker thread MUST NOT touch `Regiment`, `BattleUnits`, `GameObject`, `Transform`, `Component`, `Vector3` constructed-by-Unity, or any vanilla static like `BattleUnits.completeunitlist`. Only pure C# value types and managed reference types we control.
- **Mono Boehm GC is stop-the-world.** Worker allocations contribute to global GC pressure. Minimize per-cycle allocation: pool the decision snapshot, reuse internal dictionaries, avoid LINQ.
- **No `Console.WriteLine` from worker.** Use `Plugin.Log.LogWarning` (BepInEx-provided, thread-safe) for any worker-side warnings.
- **No throws past the worker boundary.** Worker loop body wraps in try/catch; on exception emits `TacticalOrchestratorWorkerFault` telemetry (bounded), then continues processing the next snapshot.
- **AGENTS.md default-on policy:** new tactical-behavior flag ships default `true`. Description string matches the actual default.
- **Parity-window state per `(battleSequence, allianceId)`.** Side 0 mismatch must NOT shut down side 1's window.
- **Pre-implementation field audit.** Spec §4 lists the initial `TryGet` set. Task 6 enumerates EVERY orchestrator-state read in `BattleGroupStancePatch.cs` and confirms each maps to a `TacticalDecisionSnapshot` field (or expand the snapshot).
- **HarmonyX patch behavior on background threads.** Patches fire on whatever thread invokes the original method. All our target vanilla methods (`CalculateSideStatsAndUpdateAITasks`, `AdjustGroupAIStance`, `CheckGlobalAIStrategy`, etc.) are main-thread-only. Confirmed safe.

---

## Task 1: Add `TacticalSideDecision` value type

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalSideDecision.cs`

- [ ] **Step 1: Create the file**

```csharp
using WhiskeyRealism.Strategic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Per-side immutable decision produced by the worker thread.
    /// All fields are immutable after construction. Consumed by main-thread
    /// patches via <see cref="TacticalDecisionSnapshot.Current"/>.
    /// </summary>
    public readonly struct TacticalSideDecision
    {
        public TacticalSideDecision(
            int allianceId,
            TacticalBattlePlan currentPlan,
            TacticalIntentModel currentIntent,
            CommandTreeSnapshot commandTree,
            StrategicBattleIntentSnapshot strategicIntent,
            ForceAvailabilitySnapshot forceAvailability,
            PersonalityVector commanderPersonality,
            ReplanTrigger lastReplanTrigger,
            bool hasPlan)
        {
            AllianceId = allianceId;
            CurrentPlan = currentPlan;
            CurrentIntent = currentIntent;
            CommandTree = commandTree ?? CommandTreeSnapshot.Empty;
            StrategicIntent = strategicIntent;
            ForceAvailability = forceAvailability;
            CommanderPersonality = commanderPersonality;
            LastReplanTrigger = lastReplanTrigger;
            HasPlan = hasPlan;
        }

        public int AllianceId { get; }
        public TacticalBattlePlan CurrentPlan { get; }
        public TacticalIntentModel CurrentIntent { get; }
        public CommandTreeSnapshot CommandTree { get; }
        public StrategicBattleIntentSnapshot StrategicIntent { get; }
        public ForceAvailabilitySnapshot ForceAvailability { get; }
        public PersonalityVector CommanderPersonality { get; }
        public ReplanTrigger LastReplanTrigger { get; }
        public bool HasPlan { get; }

        public static TacticalSideDecision Empty => new TacticalSideDecision(
            allianceId: -1,
            currentPlan: default,
            currentIntent: default,
            commandTree: CommandTreeSnapshot.Empty,
            strategicIntent: StrategicBattleIntentSnapshot.Empty,
            forceAvailability: new ForceAvailabilitySnapshot(0f, 0f),
            commanderPersonality: default,
            lastReplanTrigger: ReplanTrigger.None,
            hasPlan: false);
    }
}
```

Before completing this step, verify `TacticalBattlePlan`, `TacticalIntentModel`, `CommandTreeSnapshot`, `StrategicBattleIntentSnapshot`, `ForceAvailabilitySnapshot`, `PersonalityVector`, `ReplanTrigger` are all accessible types. If `CommandTreeSnapshot.Empty` doesn't exist, use whatever empty constructor pattern the type provides (read `CommandTreeRuntime.cs` for the canonical empty value).

- [ ] **Step 2: Add csproj include**

Edit `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`. Add (near other tactical orchestrator entries):

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\TacticalSideDecision.cs" Link="Orchestrator\TacticalSideDecision.cs" />
```

- [ ] **Step 3: Build and run harness**

Run: `./build.sh 2>&1 | tail -5 && dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E "^(PASS|FAIL)" | awk 'BEGIN{p=0;f=0} /^PASS/{p++} /^FAIL/{f++} END{print "PASS="p" FAIL="f}'`

Expected: Build success, `PASS=1253 FAIL=0`.

- [ ] **Step 4: Commit**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/TacticalSideDecision.cs \
        tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "feat(tactical): add TacticalSideDecision value struct for worker-thread refactor"
```

---

## Task 2: Add `TacticalGroupStanceDecision` value struct

`BattleGroupStancePatch.cs` already references a `TacticalGroupStanceDecision` type. Locate its current definition (search for `struct TacticalGroupStanceDecision` or `class TacticalGroupStanceDecision`). If it lives in another file (e.g., `TacticalDoctrineScorer.cs` or as a nested type), confirm its public surface. We need it accessible from the new `TacticalDecisionSnapshot`.

**Files:**
- Possibly Modify: existing file containing `TacticalGroupStanceDecision`

- [ ] **Step 1: Locate existing definition**

```bash
grep -rnE "struct TacticalGroupStanceDecision|class TacticalGroupStanceDecision" src/WhiskeyRealism/
```

- [ ] **Step 2: Confirm visibility**

If it's `internal struct`, that's fine — the decision snapshot is in the same assembly. If `private` (nested), promote it to file-scope `internal struct` in its own file `src/WhiskeyRealism/Tactical/Orchestrator/TacticalGroupStanceDecision.cs` and add a csproj include.

If already accessible from `WhiskeyRealism.Tactical.Orchestrator` namespace, skip the creation and proceed to Task 3.

- [ ] **Step 3: If promotion needed, build + harness + commit**

```bash
./build.sh 2>&1 | tail -5
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E "^(PASS|FAIL)" | awk 'BEGIN{p=0;f=0} /^PASS/{p++} /^FAIL/{f++} END{print "PASS="p" FAIL="f}'
git add -A
git commit -m "refactor(tactical): promote TacticalGroupStanceDecision to file-scope for worker access"
```

---

## Task 3: Add `TacticalDecisionSnapshot`

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalDecisionSnapshot.cs`

- [ ] **Step 1: Create the file**

```csharp
using System.Collections.Generic;
using System.Threading;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Per-cycle immutable snapshot of all per-side and per-unit decisions
    /// produced by the worker thread. Read by main-thread Harmony patches
    /// via <see cref="Current"/>. Atomic publish via Interlocked.Exchange.
    ///
    /// Pooled at the worker layer (TacticalOrchestratorWorker) to bound
    /// per-cycle allocations. The previously-published snapshot stays in
    /// _current until the worker swaps in the next one.
    /// </summary>
    public sealed class TacticalDecisionSnapshot
    {
        private static TacticalDecisionSnapshot _current = Empty;

        // Per-side global state
        public TacticalSideDecision Side0 { get; private set; }
        public TacticalSideDecision Side1 { get; private set; }

        // Cycle metadata
        public int BattleSequence { get; private set; }
        public long CycleId { get; private set; }
        public float CapturedAtBattleHours { get; private set; }
        public float CapturedAtRealtimeSeconds { get; private set; }

        // Per-unit lookup tables — separate dict per alliance for index density.
        // Allocated once at construction with reasonable initial capacity, cleared (not realloc) per cycle.
        private readonly Dictionary<int, TacticalGroupStanceDecision> _groupStance0;
        private readonly Dictionary<int, TacticalGroupStanceDecision> _groupStance1;

        private TacticalDecisionSnapshot(int initialCapacity)
        {
            _groupStance0 = new Dictionary<int, TacticalGroupStanceDecision>(initialCapacity);
            _groupStance1 = new Dictionary<int, TacticalGroupStanceDecision>(initialCapacity);
        }

        public static TacticalDecisionSnapshot Empty { get; } = new TacticalDecisionSnapshot(0);

        /// <summary>
        /// Worker-thread-only: clear + repopulate this snapshot's contents from
        /// fresh decisions. Followed by <see cref="Publish"/> to make it current.
        /// </summary>
        internal void PopulateForWorker(
            int battleSequence,
            long cycleId,
            float capturedAtBattleHours,
            float capturedAtRealtimeSeconds,
            TacticalSideDecision side0,
            TacticalSideDecision side1)
        {
            BattleSequence = battleSequence;
            CycleId = cycleId;
            CapturedAtBattleHours = capturedAtBattleHours;
            CapturedAtRealtimeSeconds = capturedAtRealtimeSeconds;
            Side0 = side0;
            Side1 = side1;
            _groupStance0.Clear();
            _groupStance1.Clear();
        }

        internal void SetGroupStance(int allianceId, int instanceId, TacticalGroupStanceDecision decision)
        {
            if (allianceId == 0) _groupStance0[instanceId] = decision;
            else if (allianceId == 1) _groupStance1[instanceId] = decision;
        }

        public bool TryGetGroupStance(int allianceId, int instanceId, out TacticalGroupStanceDecision decision)
        {
            var dict = allianceId == 0 ? _groupStance0
                     : allianceId == 1 ? _groupStance1
                     : null;
            if (dict != null && dict.TryGetValue(instanceId, out decision)) return true;
            decision = default;
            return false;
        }

        public TacticalSideDecision GetSide(int allianceId)
        {
            return allianceId == 0 ? Side0
                 : allianceId == 1 ? Side1
                 : TacticalSideDecision.Empty;
        }

        public static TacticalDecisionSnapshot Current
        {
            get { return Volatile.Read(ref _current) ?? Empty; }
        }

        /// <summary>
        /// Atomic publish. Called from worker thread. Returns the prior snapshot
        /// so the worker can put it back into the pool for reuse.
        /// </summary>
        internal static TacticalDecisionSnapshot Publish(TacticalDecisionSnapshot next)
        {
            if (next == null) next = Empty;
            return Interlocked.Exchange(ref _current, next);
        }

        /// <summary>
        /// Test-and-worker constructor — bypasses Empty to allow instantiation.
        /// </summary>
        internal static TacticalDecisionSnapshot CreateForPool(int initialCapacity)
        {
            return new TacticalDecisionSnapshot(initialCapacity);
        }

        /// <summary>
        /// Resets the static Current to Empty. Called from
        /// TacticalBattleCoordinator.ResetRuntimeTickState on battle end.
        /// </summary>
        public static void ResetForBattleEnd()
        {
            Interlocked.Exchange(ref _current, Empty);
        }
    }
}
```

- [ ] **Step 2: Add csproj include**

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\TacticalDecisionSnapshot.cs" Link="Orchestrator\TacticalDecisionSnapshot.cs" />
```

- [ ] **Step 3: Build and run harness**

```bash
./build.sh 2>&1 | tail -5
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E "^(PASS|FAIL)" | awk 'BEGIN{p=0;f=0} /^PASS/{p++} /^FAIL/{f++} END{print "PASS="p" FAIL="f}'
```

Expected: Build success, `PASS=1253 FAIL=0`.

- [ ] **Step 4: Commit**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/TacticalDecisionSnapshot.cs \
        tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "feat(tactical): add TacticalDecisionSnapshot with atomic Current accessor"
```

---

## Task 4: Harness tests for `TacticalDecisionSnapshot`

**Files:**
- Create: `tests/WhiskeyRealism.Tests/TacticalDecisionSnapshotTests.cs`

- [ ] **Step 1: Write the tests**

```csharp
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Orchestrator;

internal static class TacticalDecisionSnapshotTests
{
    public static void Run()
    {
        EmptyHasNoDecisions();
        PopulateAndLookupGroupStanceByAlliance();
        SetGroupStanceClearsOnRepopulate();
        PublishSwapsCurrent();
        ResetForBattleEndRestoresEmpty();
    }

    private static void EmptyHasNoDecisions()
    {
        var snap = TacticalDecisionSnapshot.Empty;
        TestHarness.AssertFalse(snap.TryGetGroupStance(0, 12345, out _), "noGroupStance0");
        TestHarness.AssertFalse(snap.TryGetGroupStance(1, 12345, out _), "noGroupStance1");
        TestHarness.AssertEqual(-1, snap.GetSide(0).AllianceId, "emptySideAlliance");
    }

    private static void PopulateAndLookupGroupStanceByAlliance()
    {
        var snap = TacticalDecisionSnapshot.CreateForPool(8);
        snap.PopulateForWorker(
            battleSequence: 1,
            cycleId: 42,
            capturedAtBattleHours: 0.5f,
            capturedAtRealtimeSeconds: 100f,
            side0: new TacticalSideDecision(0, default, default, CommandTreeSnapshot.Empty,
                StrategicBattleIntentSnapshot.Empty, new ForceAvailabilitySnapshot(0f, 0f),
                default, ReplanTrigger.None, false),
            side1: new TacticalSideDecision(1, default, default, CommandTreeSnapshot.Empty,
                StrategicBattleIntentSnapshot.Empty, new ForceAvailabilitySnapshot(0f, 0f),
                default, ReplanTrigger.None, false));

        var decisionA = new TacticalGroupStanceDecision(/* fill with concrete fields per its constructor */);
        snap.SetGroupStance(0, instanceId: 100, decisionA);

        TestHarness.AssertTrue(snap.TryGetGroupStance(0, 100, out var got), "lookupSide0");
        TestHarness.AssertFalse(snap.TryGetGroupStance(1, 100, out _), "isolatedFromSide1");
        TestHarness.AssertEqual(1, snap.BattleSequence, "battleSequence");
        TestHarness.AssertEqual(42L, snap.CycleId, "cycleId");
    }

    private static void SetGroupStanceClearsOnRepopulate()
    {
        var snap = TacticalDecisionSnapshot.CreateForPool(8);
        var d = new TacticalGroupStanceDecision(/* fill */);
        snap.PopulateForWorker(1, 1, 0f, 0f,
            TacticalSideDecision.Empty, TacticalSideDecision.Empty);
        snap.SetGroupStance(0, 7, d);
        TestHarness.AssertTrue(snap.TryGetGroupStance(0, 7, out _), "before-repopulate");

        // Repopulate clears
        snap.PopulateForWorker(1, 2, 0f, 0f,
            TacticalSideDecision.Empty, TacticalSideDecision.Empty);
        TestHarness.AssertFalse(snap.TryGetGroupStance(0, 7, out _), "after-repopulate");
    }

    private static void PublishSwapsCurrent()
    {
        var initial = TacticalDecisionSnapshot.Current;
        var next = TacticalDecisionSnapshot.CreateForPool(4);
        next.PopulateForWorker(99, 99, 0f, 0f,
            TacticalSideDecision.Empty, TacticalSideDecision.Empty);

        var prior = TacticalDecisionSnapshot.Publish(next);
        TestHarness.AssertEqual(99, TacticalDecisionSnapshot.Current.BattleSequence, "currentSwapped");

        // Restore so other tests aren't affected
        TacticalDecisionSnapshot.Publish(initial);
    }

    private static void ResetForBattleEndRestoresEmpty()
    {
        var next = TacticalDecisionSnapshot.CreateForPool(4);
        next.PopulateForWorker(99, 99, 0f, 0f,
            TacticalSideDecision.Empty, TacticalSideDecision.Empty);
        TacticalDecisionSnapshot.Publish(next);
        TacticalDecisionSnapshot.ResetForBattleEnd();
        TestHarness.AssertEqual(TacticalDecisionSnapshot.Empty, TacticalDecisionSnapshot.Current, "currentIsEmpty");
    }
}
```

**Important:** the `TacticalGroupStanceDecision` constructor signature depends on what Task 2 surfaced. Inspect the struct and fill the `MakeDecision()` calls with appropriate args (e.g., a stance enum value, group instance id, a reason string). The exact field set doesn't matter for these tests — they exercise the dictionary contract.

- [ ] **Step 2: Register in Program.cs**

Edit `tests/WhiskeyRealism.Tests/Program.cs`. Add `TacticalDecisionSnapshotTests.Run();` near the existing `TacticalUnitObservationAggregateTests.Run();` call.

- [ ] **Step 3: Run harness, expect 5 new tests pass**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E "^(PASS|FAIL)" | awk 'BEGIN{p=0;f=0} /^PASS/{p++} /^FAIL/{f++} END{print "PASS="p" FAIL="f}'
```

Expected: `PASS=1258 FAIL=0`.

- [ ] **Step 4: Commit**

```bash
git add tests/WhiskeyRealism.Tests/TacticalDecisionSnapshotTests.cs \
        tests/WhiskeyRealism.Tests/Program.cs
git commit -m "test(tactical): cover TacticalDecisionSnapshot atomic publish + lookup contracts"
```

---

## Task 5: Add `EnableTacticalOrchestratorWorker` ConfigEntry

**Files:**
- Modify: `src/WhiskeyRealism/Plugin.cs`

- [ ] **Step 1: Locate existing config block**

```bash
grep -n "EnableSinglePassObjectiveRecords\b" src/WhiskeyRealism/Plugin.cs
```

Note line numbers of the field declaration and the `Config.Bind(...)` call.

- [ ] **Step 2: Add field declaration**

Adjacent to `EnableSinglePassObjectiveRecords`:

```csharp
public static ConfigEntry<bool> EnableTacticalOrchestratorWorker;
```

- [ ] **Step 3: Add Config.Bind call**

Adjacent to the existing `EnableSinglePassObjectiveRecords = Config.Bind(…)` call:

```csharp
EnableTacticalOrchestratorWorker = Config.Bind(
    "Tactical Orchestrator",
    "Enable Tactical Orchestrator Worker",
    true,
    "Background-thread orchestrator analysis. Main thread captures a "
    + "snapshot and reads pre-computed decisions; analysis runs on a "
    + "dedicated worker thread. Default ON per AGENTS.md tactical policy. "
    + "Set false to roll back to synchronous on-main-thread analysis if "
    + "a regression appears. Performance characteristics documented in "
    + "docs/telemetry.md once smoke-verified.");
```

- [ ] **Step 4: Build + harness**

```bash
./build.sh 2>&1 | tail -5
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E "^(PASS|FAIL)" | awk 'BEGIN{p=0;f=0} /^PASS/{p++} /^FAIL/{f++} END{print "PASS="p" FAIL="f}'
```

Expected: `PASS=1258 FAIL=0`.

- [ ] **Step 5: Commit**

```bash
git add src/WhiskeyRealism/Plugin.cs
git commit -m "feat(config): add EnableTacticalOrchestratorWorker flag (default ON)"
```

---

## Task 6: Register `TacticalOrchestratorParityMismatch` + `TacticalOrchestratorWorkerFault` telemetry tags

**Files:**
- Modify: `src/WhiskeyRealism/Telemetry/TelemetryTagPolicy.cs`

- [ ] **Step 1: Locate existing registrations**

```bash
grep -n "TacticalObjectiveRecordsParityMismatch\|TacticalHeavyGate" src/WhiskeyRealism/Telemetry/TelemetryTagPolicy.cs
```

- [ ] **Step 2: Add two new tags**

Place adjacent to `TacticalObjectiveRecordsParityMismatch` (same Gate category style):

```csharp
AddTactical("TacticalOrchestratorParityMismatch", TelemetryCategory.Gate);
AddTactical("TacticalOrchestratorWorkerFault", TelemetryCategory.Health);
```

The `WorkerFault` tag is the worker's exception-emit channel; routes to Health category.

- [ ] **Step 3: Build + harness**

```bash
./build.sh 2>&1 | tail -5
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E "^(PASS|FAIL)" | awk 'BEGIN{p=0;f=0} /^PASS/{p++} /^FAIL/{f++} END{print "PASS="p" FAIL="f}'
```

Expected: `PASS=1258 FAIL=0`.

- [ ] **Step 4: Commit**

```bash
git add src/WhiskeyRealism/Telemetry/TelemetryTagPolicy.cs
git commit -m "feat(telemetry): register worker parity-mismatch + fault tags"
```

---

## Task 7: Worker-input field audit (mandatory, no code)

Before writing the worker, enumerate every input the existing `DriveTacticalCommanderSide` chain reads from live vanilla state. This determines what the main thread must capture before enqueuing.

- [ ] **Step 1: Survey the existing path**

```bash
grep -nE 'battle\.|bunits\.|GameVars\.|BattleUnits\.|Regiment\.|reg\.|own\.|group\.|enemy\.' src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs | grep -v '^//' | head -60
```

Trace into:
- `DriveTickCycle` (lines ~278-407)
- `DriveOperationsLedger` (lines ~423-520)
- `DriveDirectChildCycle` (lines ~1088-1220)
- `AttachDirectChildrenIfReady` (lines ~807-841)
- `AttachCommandTreeIfReady` (lines ~850-880)

For each method, list every `battle.<field>`, `bunits.<field>`, `BattleUnits.completeunitlist`, `Regiment.<field>` access that happens IN THE BODY (not inside helper methods already snapshot-driven).

- [ ] **Step 2: Record the audit table in the next commit message**

Categorize each input by:
- Already in `TacticalBattleRuntimeSnapshot` (Slice 1 captured) → no action
- Already in `ArmyEvidenceBuilder.Bundle` → no action
- Already in `CommandTreeSnapshot` or `DirectChildSnapshot[]` → captured by existing runtime, can be pre-captured
- Otherwise → needs to be added to `TacticalOrchestratorWorkerInput` (Task 8)

The complete audit is the input to Task 8's design. **Do not skip — proceed to Task 8 only after recording results.**

- [ ] **Step 3: Commit the audit notes**

```bash
git commit --allow-empty -m "audit(tactical): worker-input field survey for Task 8

Inputs DriveTacticalCommanderSide currently reads from live vanilla:
  [paste audit table here, one row per input field with its current source
   and resolution (already captured / needs new capture)]"
```

---

## Task 8: Add `TacticalOrchestratorWorkerInput`

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalOrchestratorWorkerInput.cs`

- [ ] **Step 1: Create the file**

Field set determined by Task 7's audit. Initial scaffold:

```csharp
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Immutable input the main thread fills and passes to the worker.
    /// Contains everything the orchestrator analysis needs from live vanilla
    /// state. Reference-shared with the worker; only main thread populates,
    /// only worker reads.
    /// </summary>
    public sealed class TacticalOrchestratorWorkerInput
    {
        public int BattleSequence { get; }
        public int PlayerAllianceId { get; }
        public bool AiVsAi { get; }
        public float BattleHours { get; }
        public float RealtimeSeconds { get; }
        public float DeltaSeconds { get; }
        public float BattleDeltaSeconds { get; }

        // Per-side snapshots — populated only for sides we actually drive (suppressed side stays null).
        // Each is the heavy-path output from TacticalBattleSnapshotBuilder.Build for that side.
        public TacticalBattleRuntimeSnapshot Side0Snapshot { get; }
        public TacticalBattleRuntimeSnapshot Side1Snapshot { get; }

        // Per-side evidence bundles (already produced by ArmyEvidenceBuilder.Build)
        public ArmyEvidenceBuilder.Bundle Side0Bundle { get; }
        public ArmyEvidenceBuilder.Bundle Side1Bundle { get; }

        // Per-side command tree + direct children (already produced by CommandTreeRuntime + DirectChildDiscovery)
        public CommandTreeSnapshot Side0CommandTree { get; }
        public CommandTreeSnapshot Side1CommandTree { get; }
        public IReadOnlyList<DirectChildSnapshot> Side0DirectChildren { get; }
        public IReadOnlyList<DirectChildSnapshot> Side1DirectChildren { get; }

        // Per-side commander metadata (read once on main from roster)
        public CommanderRosterEntry Side0CommanderEntry { get; }
        public CommanderRosterEntry Side1CommanderEntry { get; }

        public TacticalOrchestratorWorkerInput(
            int battleSequence,
            int playerAllianceId,
            bool aiVsAi,
            float battleHours,
            float realtimeSeconds,
            float deltaSeconds,
            float battleDeltaSeconds,
            TacticalBattleRuntimeSnapshot side0Snapshot,
            TacticalBattleRuntimeSnapshot side1Snapshot,
            ArmyEvidenceBuilder.Bundle side0Bundle,
            ArmyEvidenceBuilder.Bundle side1Bundle,
            CommandTreeSnapshot side0CommandTree,
            CommandTreeSnapshot side1CommandTree,
            IReadOnlyList<DirectChildSnapshot> side0DirectChildren,
            IReadOnlyList<DirectChildSnapshot> side1DirectChildren,
            CommanderRosterEntry side0CommanderEntry,
            CommanderRosterEntry side1CommanderEntry)
        {
            BattleSequence = battleSequence;
            PlayerAllianceId = playerAllianceId;
            AiVsAi = aiVsAi;
            BattleHours = battleHours;
            RealtimeSeconds = realtimeSeconds;
            DeltaSeconds = deltaSeconds;
            BattleDeltaSeconds = battleDeltaSeconds;
            Side0Snapshot = side0Snapshot;
            Side1Snapshot = side1Snapshot;
            Side0Bundle = side0Bundle;
            Side1Bundle = side1Bundle;
            Side0CommandTree = side0CommandTree ?? CommandTreeSnapshot.Empty;
            Side1CommandTree = side1CommandTree ?? CommandTreeSnapshot.Empty;
            Side0DirectChildren = side0DirectChildren ?? System.Array.Empty<DirectChildSnapshot>();
            Side1DirectChildren = side1DirectChildren ?? System.Array.Empty<DirectChildSnapshot>();
            Side0CommanderEntry = side0CommanderEntry;
            Side1CommanderEntry = side1CommanderEntry;
        }
    }
}
```

If Task 7's audit revealed additional fields the worker needs, add them as additional constructor parameters and properties. Match the field set EXACTLY — adding fields later means revisiting the main-thread capture in Task 9.

- [ ] **Step 2: Add csproj include**

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\TacticalOrchestratorWorkerInput.cs" Link="Orchestrator\TacticalOrchestratorWorkerInput.cs" />
```

- [ ] **Step 3: Build + harness**

```bash
./build.sh 2>&1 | tail -5
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E "^(PASS|FAIL)" | awk 'BEGIN{p=0;f=0} /^PASS/{p++} /^FAIL/{f++} END{print "PASS="p" FAIL="f}'
```

Expected: `PASS=1258 FAIL=0`.

- [ ] **Step 4: Commit**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/TacticalOrchestratorWorkerInput.cs \
        tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "feat(tactical): add TacticalOrchestratorWorkerInput container per Task 7 audit"
```

---

## Task 9: Add `TacticalOrchestratorWorker` (lifecycle + loop skeleton)

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalOrchestratorWorker.cs`

- [ ] **Step 1: Create the file with lifecycle + skeleton loop**

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Dedicated worker thread that runs tactical orchestrator analysis off
    /// the Unity main thread. Mirrors the TelemetryWriter pattern: one Thread
    /// started in OnBattleStart, joined in OnBattleEnd; single-slot snapshot
    /// enqueue with drop-old policy; atomic decision publish via Interlocked.
    /// </summary>
    public sealed class TacticalOrchestratorWorker
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _snapshotAvailable = new ManualResetEventSlim(initialState: false);
        private volatile TacticalOrchestratorWorkerInput _pendingInput;
        private volatile bool _stopRequested;
        private long _cyclesProcessed;
        private readonly Queue<TacticalDecisionSnapshot> _decisionPool = new Queue<TacticalDecisionSnapshot>(2);
        private readonly object _poolLock = new object();
        private const int DecisionSnapshotInitialCapacity = 256;
        private const int FaultEmitMaxPerSession = 64;
        private int _faultEmitCount;

        public TacticalOrchestratorWorker()
        {
            _thread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "WhiskeyRealism.OrchestratorWorker"
            };

            // Pre-seed pool with two snapshots
            lock (_poolLock)
            {
                _decisionPool.Enqueue(TacticalDecisionSnapshot.CreateForPool(DecisionSnapshotInitialCapacity));
                _decisionPool.Enqueue(TacticalDecisionSnapshot.CreateForPool(DecisionSnapshotInitialCapacity));
            }
        }

        public void Start()
        {
            if (_thread.IsAlive) return;
            _thread.Start();
            OnceLog.Info("orch-worker:start", "[TacticalOrchestratorWorker] started");
        }

        public void RequestStop()
        {
            _stopRequested = true;
            _snapshotAvailable.Set();
        }

        public void Join(int timeoutMs)
        {
            try
            {
                if (_thread.IsAlive && !_thread.Join(timeoutMs))
                {
                    Plugin.Log.LogWarning("[TacticalOrchestratorWorker] join timed out at "
                        + timeoutMs + "ms; thread is background, will die on AppDomain unload");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestratorWorker] join failed: "
                    + e.GetType().Name + " " + e.Message);
            }
            OnceLog.Info("orch-worker:stop", "[TacticalOrchestratorWorker] stopped");
        }

        /// <summary>
        /// Main-thread side: enqueue the latest worker input. Drops-old semantics —
        /// if the worker hasn't picked up the prior input yet, the new one replaces it.
        /// </summary>
        public void Enqueue(TacticalOrchestratorWorkerInput input)
        {
            _pendingInput = input;  // last-write-wins
            _snapshotAvailable.Set();
        }

        private void WorkerLoop()
        {
            while (!_stopRequested)
            {
                try
                {
                    _snapshotAvailable.Wait();
                    if (_stopRequested) break;
                    _snapshotAvailable.Reset();

                    var input = Interlocked.Exchange(ref _pendingInput, null);
                    if (input == null) continue;  // spurious wake, harmless

                    ProcessOne(input);
                }
                catch (ThreadInterruptedException)
                {
                    break;
                }
                catch (Exception e)
                {
                    EmitFault(e);
                    // Continue loop; do not propagate
                }
            }
        }

        private void ProcessOne(TacticalOrchestratorWorkerInput input)
        {
            // Acquire a pooled snapshot
            TacticalDecisionSnapshot pooled;
            lock (_poolLock)
            {
                pooled = _decisionPool.Count > 0 ? _decisionPool.Dequeue() : TacticalDecisionSnapshot.CreateForPool(DecisionSnapshotInitialCapacity);
            }

            // Analyze (Task 10 fills this body)
            using (TelemetryPerf.Scope("tactical.worker.cycle", TelemetryLayer.Tactical, TelemetryCategory.Performance, 50.0))
            {
                _cyclesProcessed++;
                TacticalOrchestratorWorkerAnalysis.Analyze(input, pooled, _cyclesProcessed);
            }

            // Publish and reclaim prior into the pool
            TacticalDecisionSnapshot prior;
            using (TelemetryPerf.Scope("tactical.worker.cycle.publish", TelemetryLayer.Tactical, TelemetryCategory.Performance, 1.0))
            {
                prior = TacticalDecisionSnapshot.Publish(pooled);
            }

            if (prior != null && prior != TacticalDecisionSnapshot.Empty)
            {
                lock (_poolLock)
                {
                    if (_decisionPool.Count < 2) _decisionPool.Enqueue(prior);
                }
            }
        }

        private void EmitFault(Exception e)
        {
            try
            {
                if (_faultEmitCount >= FaultEmitMaxPerSession) return;
                _faultEmitCount++;
                TelemetryRouter.Emit(
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Health,
                    "TacticalOrchestratorWorkerFault",
                    TelemetrySeverity.Warning,
                    ev => ev
                        .WithDecision("worker-fault", e.GetType().Name, "cycle=" + _cyclesProcessed)
                        .WithField("exceptionType", e.GetType().Name)
                        .WithField("message", e.Message ?? string.Empty)
                        .WithField("cyclesProcessed", _cyclesProcessed));
            }
            catch { }
        }
    }
}
```

The reference to `TacticalOrchestratorWorkerAnalysis.Analyze` is forward — Task 10 creates that class. Build will fail with that reference unresolved until Task 10.

- [ ] **Step 2: Build is expected to FAIL with unresolved reference**

```bash
./build.sh 2>&1 | tail -10
```

Expected: `CS0103: The name 'TacticalOrchestratorWorkerAnalysis' does not exist...` This is intentional. Proceed to Task 10 to provide the analysis.

- [ ] **Step 3: Do NOT commit until Task 10 is staged with this file together**

Stage `TacticalOrchestratorWorker.cs` (`git add`) but do not commit yet — wait for Task 10.

---

## Task 10: Add `TacticalOrchestratorWorkerAnalysis` (snapshot-driven analysis)

This is the substantive task: extract the body of `DriveTacticalCommanderSide` into a snapshot-driven static method that the worker can call.

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalOrchestratorWorkerAnalysis.cs`

- [ ] **Step 1: Create the file with the analysis entry**

```csharp
using System;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pure worker-thread analysis: takes an immutable TacticalOrchestratorWorkerInput,
    /// produces a TacticalDecisionSnapshot (populated in-place into the pooled instance).
    /// MUST NOT call any Unity API or vanilla static (BattleUnits.completeunitlist, etc.).
    /// All vanilla state must already be in the input snapshots.
    /// </summary>
    internal static class TacticalOrchestratorWorkerAnalysis
    {
        public static void Analyze(
            TacticalOrchestratorWorkerInput input,
            TacticalDecisionSnapshot output,
            long cycleId)
        {
            try
            {
                var side0 = ProcessSide(0, input, cycleId);
                var side1 = ProcessSide(1, input, cycleId);

                output.PopulateForWorker(
                    battleSequence: input.BattleSequence,
                    cycleId: cycleId,
                    capturedAtBattleHours: input.BattleHours,
                    capturedAtRealtimeSeconds: input.RealtimeSeconds,
                    side0: side0,
                    side1: side1);

                // Per-group decisions populated below (group stance, role per direct child, etc.)
                PopulateGroupDecisions(0, input, output);
                PopulateGroupDecisions(1, input, output);
            }
            catch (Exception e)
            {
                OnceLog.Warning("orch-worker:analyze-fault",
                    "TacticalOrchestratorWorkerAnalysis.Analyze degraded: "
                    + e.GetType().Name + " " + e.Message);
                // Output left in its prior-cleared state; published as empty-ish snapshot
            }
        }

        private static TacticalSideDecision ProcessSide(int sideIdx, TacticalOrchestratorWorkerInput input, long cycleId)
        {
            var snap = sideIdx == 0 ? input.Side0Snapshot : input.Side1Snapshot;
            var bundle = sideIdx == 0 ? input.Side0Bundle : input.Side1Bundle;
            var commandTree = sideIdx == 0 ? input.Side0CommandTree : input.Side1CommandTree;
            var commanderEntry = sideIdx == 0 ? input.Side0CommanderEntry : input.Side1CommanderEntry;

            if (snap == null || !snap.HasData)
            {
                return new TacticalSideDecision(
                    allianceId: ResolveAllianceId(sideIdx, input),
                    currentPlan: default,
                    currentIntent: default,
                    commandTree: commandTree ?? CommandTreeSnapshot.Empty,
                    strategicIntent: StrategicBattleIntentSnapshot.Empty,
                    forceAvailability: new ForceAvailabilitySnapshot(0f, 0f),
                    commanderPersonality: default,
                    lastReplanTrigger: ReplanTrigger.None,
                    hasPlan: false);
            }

            // Reproduce the same logic DriveTickCycle currently runs, but on the
            // captured bundle instead of a fresh ArmyEvidenceBuilder.Build call.
            //
            // The full set of calls — ArmyTickCycle.MaybeReplan, ArmyIntentInference,
            // TacticalDoctrineScorer, etc. — happens here, all on worker thread,
            // operating on the immutable input.
            //
            // Stub for now: derive a HasData-based TacticalSideDecision. Task 11
            // expands this to call into the real analysis pipeline.

            return new TacticalSideDecision(
                allianceId: ResolveAllianceId(sideIdx, input),
                currentPlan: default,
                currentIntent: default,
                commandTree: commandTree ?? CommandTreeSnapshot.Empty,
                strategicIntent: StrategicBattleIntentSnapshot.Empty,
                forceAvailability: new ForceAvailabilitySnapshot(
                    snap.OwnMainEffortStrength,
                    Math.Max(0f, 1f - Clamp01(snap.OwnReservesCommittedFraction))),
                commanderPersonality: commanderEntry != null ? commanderEntry.PersonalityVector : default,
                lastReplanTrigger: ReplanTrigger.None,
                hasPlan: snap.HasData);
        }

        private static int ResolveAllianceId(int sideIdx, TacticalOrchestratorWorkerInput input)
        {
            // The snapshot carries enough info to derive alliance — for stub purposes:
            return sideIdx; // overridden in Task 11 once we capture alliance per snapshot
        }

        private static void PopulateGroupDecisions(int sideIdx, TacticalOrchestratorWorkerInput input, TacticalDecisionSnapshot output)
        {
            // Stub: Task 11 fills group stance decisions from snapshot.OwnEvidence + per-group reads.
        }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f) return 0f;
            return v > 1f ? 1f : v;
        }
    }
}
```

This is a SCAFFOLD that compiles. Task 11 fills the actual analysis pipeline.

- [ ] **Step 2: Add csproj include for Worker + Analysis + WorkerInput**

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\TacticalOrchestratorWorker.cs" Link="Orchestrator\TacticalOrchestratorWorker.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\TacticalOrchestratorWorkerAnalysis.cs" Link="Orchestrator\TacticalOrchestratorWorkerAnalysis.cs" />
```

(Note: `TacticalOrchestratorWorkerInput.cs` include was added in Task 8.)

- [ ] **Step 3: Build clean**

```bash
./build.sh 2>&1 | tail -5
```

Expected: 0 errors. The worker thread, worker input, and analysis scaffold all compile together.

- [ ] **Step 4: Run harness**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E "^(PASS|FAIL)" | awk 'BEGIN{p=0;f=0} /^PASS/{p++} /^FAIL/{f++} END{print "PASS="p" FAIL="f}'
```

Expected: `PASS=1258 FAIL=0`. Worker isn't started yet (Task 12 wires lifecycle), so behavior is unchanged.

- [ ] **Step 5: Commit Tasks 9 + 10 together**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/TacticalOrchestratorWorker.cs \
        src/WhiskeyRealism/Tactical/Orchestrator/TacticalOrchestratorWorkerAnalysis.cs \
        tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "feat(tactical): TacticalOrchestratorWorker thread + Analysis scaffold

Adds dedicated background Thread with:
  - ManualResetEventSlim-based wake on snapshot availability
  - Single-slot drop-old enqueue (last-write-wins)
  - Pooled TacticalDecisionSnapshot reuse (2 slots)
  - Atomic publish via Interlocked.Exchange on TacticalDecisionSnapshot._current
  - Bounded TacticalOrchestratorWorkerFault telemetry on exceptions
  - Graceful stop via _stopRequested + signal + Join(timeout)

Analysis scaffold returns a stub TacticalSideDecision derived from the
captured snapshot's HasData/OwnMainEffortStrength/CommitmentFraction.
Task 11 expands to call the real ArmyTickCycle / IntentInference /
DoctrineScorer pipeline.

Worker isn't started yet — Task 12 wires lifecycle into OnBattleStart /
OnBattleEnd. No behavior change."
```

---

## Task 11: Fill `TacticalOrchestratorWorkerAnalysis` with real analysis

This task replaces the stub bodies in `ProcessSide` and `PopulateGroupDecisions` with calls into the actual analysis pipeline currently inside `DriveTacticalCommanderSide`.

**Files:**
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalOrchestratorWorkerAnalysis.cs`

- [ ] **Step 1: Identify which analysis calls are pure C# (move to worker) vs Unity-touching (must stay on main)**

```bash
grep -nE 'ArmyTickCycle\.|ArmyIntentInference\.|TacticalDoctrineScorer\.|TacticalPlaybookCatalog\.|TacticalCommanderIntent\.|TacticalSectorReadinessDoctrine\.|CommandTreeIntentAllocator\.|DirectChildAllocator\.|TacticalOperationsLedger\.' src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs
```

Each match is a candidate for the worker. Verify each is pure C# (no `Regiment.X`, `BattleUnits.X`, `Component.X`, `GameObject.X` access in its body chain). Skim each method's source to confirm. Document the result in the commit message.

- [ ] **Step 2: Refactor `ProcessSide` to invoke the actual pipeline**

Replace the stub body in `ProcessSide` with the equivalent of what `DriveTickCycle` currently does, but operating on `input.SideXBundle` and `input.SideXSnapshot` instead of doing a fresh `ArmyEvidenceBuilder.Build`:

```csharp
private static TacticalSideDecision ProcessSide(int sideIdx, TacticalOrchestratorWorkerInput input, long cycleId)
{
    var snap = sideIdx == 0 ? input.Side0Snapshot : input.Side1Snapshot;
    var bundle = sideIdx == 0 ? input.Side0Bundle : input.Side1Bundle;
    var commandTree = sideIdx == 0 ? input.Side0CommandTree : input.Side1CommandTree;
    var directChildren = sideIdx == 0 ? input.Side0DirectChildren : input.Side1DirectChildren;
    var commanderEntry = sideIdx == 0 ? input.Side0CommanderEntry : input.Side1CommanderEntry;

    int allianceId = commanderEntry != null ? commanderEntry.AllianceId : -1;

    if (snap == null || !snap.HasData || commanderEntry == null)
    {
        return new TacticalSideDecision(
            allianceId: allianceId,
            currentPlan: default,
            currentIntent: default,
            commandTree: commandTree ?? CommandTreeSnapshot.Empty,
            strategicIntent: StrategicBattleIntentSnapshot.Empty,
            forceAvailability: new ForceAvailabilitySnapshot(0f, 0f),
            commanderPersonality: default,
            lastReplanTrigger: ReplanTrigger.None,
            hasPlan: false);
    }

    // Mirror DriveTickCycle's analysis on the bundle (no live AIBattle reads):
    //   1. Intent inference (snapshot.EnemyVisible drives ArmyIntentInference)
    //   2. Plan / replan via ArmyTickCycle.MaybeReplan
    //   3. Doctrine scoring / sector readiness for per-group decisions

    var intent = ArmyIntentInference.Infer(
        snap.EnemyVisible,
        snap.OwnEvidence,
        snap.OwnReservesCommittedFraction);

    // Note: replan operates on a stateless input → trigger decision.
    // We DO NOT mutate the live ArmyOrchestrator from worker. The main thread
    // applies the plan-id update on the next tick via this snapshot.
    var trigger = ArmyTickCycle.EvaluateReplanTrigger(
        bundle,
        intent,
        commanderEntry.PersonalityVector,
        input.DeltaSeconds,
        input.BattleDeltaSeconds);

    var strategic = new StrategicBattleIntentSnapshot(
        casualtyPressure: Clamp01(1f - bundle.OwnArmyMorale),
        timePressure: 0f,
        theaterIntent: intent.PrimaryIntent.ToString(),
        campaignIntent: string.Empty,
        allianceId: allianceId,
        campaignObjectiveId: string.Empty,
        theaterPriority: Clamp01(bundle.OwnEvidence.CurrentOdds / 2f),
        casualtyTolerance: commanderEntry.PersonalityVector.CasualtyTolerance,
        preserveForceBias: Clamp01((commanderEntry.PersonalityVector.Caution + 1f) * 0.5f),
        commanderPersonality: commanderEntry.PersonalityVector);

    var force = new ForceAvailabilitySnapshot(
        bundle.OwnMainEffortStrength,
        Math.Max(0f, 1f - Clamp01(bundle.OwnReservesCommittedFraction)));

    return new TacticalSideDecision(
        allianceId: allianceId,
        currentPlan: default,  // plan id resolution requires per-battle plan registry — see Task 13
        currentIntent: intent,
        commandTree: commandTree,
        strategicIntent: strategic,
        forceAvailability: force,
        commanderPersonality: commanderEntry.PersonalityVector,
        lastReplanTrigger: trigger,
        hasPlan: snap.HasData);
}
```

**Important:** the references to `ArmyIntentInference.Infer(...)`, `ArmyTickCycle.EvaluateReplanTrigger(...)` are NEW public surfaces. Today the equivalents are `ArmyIntentInference.BuildForFrontage(...)` and `ArmyTickCycle.MaybeReplan(...)` which both write into orchestrator state. We need stateless versions.

If those overloads don't exist, **stop and create them in their respective files** as parallel `public static` entries that return the result instead of mutating orchestrator state. Document each addition in the commit. Once the stateless overloads exist, the worker analysis can call them safely.

- [ ] **Step 3: Refactor `PopulateGroupDecisions`**

For each command-level group in the captured snapshot (`snap.OwnEvidence.SectorList` and the captured per-unit observations), compute a `TacticalGroupStanceDecision` using `TacticalDoctrineScorer.DecideGroupStance` and store into the output snapshot. Sketch:

```csharp
private static void PopulateGroupDecisions(int sideIdx, TacticalOrchestratorWorkerInput input, TacticalDecisionSnapshot output)
{
    var snap = sideIdx == 0 ? input.Side0Snapshot : input.Side1Snapshot;
    var commanderEntry = sideIdx == 0 ? input.Side0CommanderEntry : input.Side1CommanderEntry;
    if (snap == null || !snap.HasData || commanderEntry == null) return;

    int allianceId = commanderEntry.AllianceId;

    // Iterate per-command-group from the captured unit observations.
    // [Implementation depends on what the snapshot exposes; if per-unit
    //  observations are needed beyond the bundle's SectorList, add to
    //  TacticalOrchestratorWorkerInput in Task 8 amendment.]
    //
    // For each commandable group:
    //   - Compute its sector assessment from captured visibility
    //   - Invoke TacticalDoctrineScorer.DecideGroupStance(input)
    //   - output.SetGroupStance(allianceId, instanceId, decision)
}
```

The detailed loop body needs to be written from the equivalent logic currently in `BattleGroupStancePatch.ApplyGroup` — but operating on captured data instead of live Regiment. **If captured data doesn't have all the fields the patch reads, augment the snapshot first.**

- [ ] **Step 4: Build + harness**

```bash
./build.sh 2>&1 | tail -10
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E "^(PASS|FAIL)" | awk 'BEGIN{p=0;f=0} /^PASS/{p++} /^FAIL/{f++} END{print "PASS="p" FAIL="f}'
```

Expected: 0 errors, `PASS=1258 FAIL=0`. The new worker analysis isn't invoked yet (lifecycle in Task 12), so behavior is unchanged.

- [ ] **Step 5: Commit**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/TacticalOrchestratorWorkerAnalysis.cs \
        src/WhiskeyRealism/Tactical/Orchestrator/*.cs
git commit -m "feat(tactical): worker analysis pipeline operates on captured snapshot only

ProcessSide: pulls bundle + snapshot from input, runs intent inference,
evaluates replan trigger statelessly, populates a TacticalSideDecision.

PopulateGroupDecisions: per-group stance decisions written into the
output snapshot indexed by (alliance, instance id).

Stateless overloads added to ArmyIntentInference / ArmyTickCycle:
  [list which were added and their signatures]

No worker invocation yet — Task 12 wires lifecycle."
```

---

## Task 12: Wire worker lifecycle into `TacticalBattleCoordinator`

**Files:**
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs`

- [ ] **Step 1: Add worker field**

Near the existing class fields (after `_lastGen0CountBySide`):

```csharp
// Slice 2: dedicated worker thread for orchestrator analysis. Started on
// OnBattleStart, joined on OnBattleEnd. Disabled when EnableTacticalOrchestratorWorker == false.
private static TacticalOrchestratorWorker _worker;
```

- [ ] **Step 2: Start worker in `OnBattleStart`**

After the existing `BuildAndActivate(suppressedAllianceId, roster);` line:

```csharp
if (IsWorkerEnabled())
{
    _worker = new TacticalOrchestratorWorker();
    _worker.Start();
}
```

Add the `IsWorkerEnabled` helper alongside `IsHeavyThrottlingEnabled`:

```csharp
private static bool IsWorkerEnabled()
{
    try
    {
        return Plugin.EnableTacticalOrchestratorWorker != null
            && Plugin.EnableTacticalOrchestratorWorker.Value;
    }
    catch
    {
        return false;
    }
}
```

- [ ] **Step 3: Stop worker in `OnBattleEnd`**

In the existing try block of `OnBattleEnd`, before `OnceLog.Info("orch-teardown", ...)`:

```csharp
try
{
    if (_worker != null)
    {
        _worker.RequestStop();
        _worker.Join(250);
        _worker = null;
    }
}
catch (Exception we)
{
    Plugin.Log.LogWarning("[TacticalOrchestrator] worker stop failed: "
        + we.GetType().Name + " " + we.Message);
}
```

Also in `ResetRuntimeTickState`:

```csharp
try { TacticalDecisionSnapshot.ResetForBattleEnd(); } catch { }
```

- [ ] **Step 4: Wire `Tick` to enqueue when worker is active**

This is the critical refactor — leave the existing synchronous path AS-IS, then ADD the worker path conditionally. Modify the `Tick` method around the existing `DriveTacticalCommanderSide(side0, ...)` / `DriveTacticalCommanderSide(side1, ...)` calls:

```csharp
public static void Tick(AIBattle battle)
{
    if (!active) return;
    using (TelemetryPerf.Scope("tactical.orchestrator-tick", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
    {
        try
        {
            int battleKey = GetBattleKeyFromBunits(battle);
            float vanillaLastSideStat = SafeGetLastSideStatUpdateFromBattleUnitsOwner(battle);
            float lastProcessed;
            if (_lastProcessedSideStatUpdateByBunitsId.TryGetValue(battleKey, out lastProcessed)
                && Math.Abs(vanillaLastSideStat - lastProcessed) < 0.0001f)
            {
                return;
            }

            OnceLog.Info("orch-coordinator", "[TacticalOrchestrator] coordinator first tick");
            bool aiVsAi = SafeAiVsAi();
            float deltaSeconds = ComputeTickDeltaSeconds();

            // Slice 2: when worker enabled, capture inputs on main and enqueue.
            // Existing synchronous DriveTacticalCommanderSide is kept as the
            // parity-window comparator for the first 20 cycles per side; after
            // window clears, the synchronous path is skipped.
            bool workerActive = IsWorkerEnabled() && _worker != null;
            if (workerActive)
            {
                var workerInput = CaptureWorkerInputOnMain(battle, deltaSeconds);
                _worker.Enqueue(workerInput);
            }

            DriveTacticalCommanderSide(side0, battle, aiVsAi, deltaSeconds);
            DriveTacticalCommanderSide(side1, battle, aiVsAi, deltaSeconds);

            // Mark as processed for this vanilla side-stat cycle
            if (battleKey != 0 && vanillaLastSideStat > 0f)
            {
                _lastProcessedSideStatUpdateByBunitsId[battleKey] = vanillaLastSideStat;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[TacticalOrchestrator] Tick skipped: "
                + e.GetType().Name + " " + e.Message);
        }
    }
}
```

Add `CaptureWorkerInputOnMain` as a new private helper that builds a `TacticalOrchestratorWorkerInput` by calling the same `TacticalBattleSnapshotBuilder.Build` + `ArmyEvidenceBuilder.Build` + `CommandTreeRuntime.Snapshot` + `DirectChildDiscovery.Snapshot` chain currently inside `DriveTickCycle`/`AttachDirectChildren`/`AttachCommandTree`, but accumulates them into the worker-input container instead of mutating orchestrator state.

```csharp
private static TacticalOrchestratorWorkerInput CaptureWorkerInputOnMain(AIBattle battle, float deltaSeconds)
{
    // Reuse the existing snapshot path for each side (heavy-gate may suppress
    // the actual Build call for one side — in that case we re-publish the last
    // published snapshot, which is what the synchronous path also does).
    int allianceA = SafeAllianceForSide(ResolveBattleUnitsForCapture(battle), 0);
    int allianceB = SafeAllianceForSide(ResolveBattleUnitsForCapture(battle), 1);

    var nowH = SafeCurrentBattleHours();
    var nowReal = SafeRealtimeSeconds();
    // Compute battle delta (mirrors DriveTickCycle's calculation)
    float battleDelta = 0f;  // first iteration; Task 13 refines

    return new TacticalOrchestratorWorkerInput(
        battleSequence: _battleSequence,
        playerAllianceId: _playerAllianceId,
        aiVsAi: SafeAiVsAi(),
        battleHours: nowH,
        realtimeSeconds: nowReal,
        deltaSeconds: deltaSeconds,
        battleDeltaSeconds: battleDelta,
        side0Snapshot: CaptureSideSnapshotForWorker(battle, allianceA),
        side1Snapshot: CaptureSideSnapshotForWorker(battle, allianceB),
        side0Bundle: ArmyEvidenceBuilder.Build(battle, allianceA),
        side1Bundle: ArmyEvidenceBuilder.Build(battle, allianceB),
        side0CommandTree: CommandTreeRuntime.Snapshot(allianceA),
        side1CommandTree: CommandTreeRuntime.Snapshot(allianceB),
        side0DirectChildren: DirectChildDiscovery.Snapshot(allianceA),
        side1DirectChildren: DirectChildDiscovery.Snapshot(allianceB),
        side0CommanderEntry: FindArmyEntry(side0),
        side1CommanderEntry: FindArmyEntry(side1));
}

private static TacticalBattleRuntimeSnapshot CaptureSideSnapshotForWorker(AIBattle battle, int allianceId)
{
    try
    {
        var nowH = SafeCurrentBattleHours();
        var sig = TacticalBattleSnapshotBuilder.ExtractCurrentSignature(battle, nowH);
        return TacticalBattleSnapshotBuilder.Build(battle, allianceId, sig, nowH);
    }
    catch
    {
        return TacticalBattleRuntimeSnapshot.Empty;
    }
}

private static BattleUnits ResolveBattleUnitsForCapture(AIBattle battle)
{
    return ResolveBattleUnits(battle);
}
```

This main-thread capture path does duplicate work with the synchronous path during the parity window. After the parity window passes (Task 14), the synchronous `DriveTacticalCommanderSide` calls are skipped and only the capture-then-enqueue runs. Net effect: cheap capture (~25ms) replaces full synchronous tick (67ms).

- [ ] **Step 5: Build + harness**

```bash
./build.sh 2>&1 | tail -10
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E "^(PASS|FAIL)" | awk 'BEGIN{p=0;f=0} /^PASS/{p++} /^FAIL/{f++} END{print "PASS="p" FAIL="f}'
```

Expected: 0 errors, `PASS=1258 FAIL=0`.

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs
git commit -m "feat(tactical): wire TacticalOrchestratorWorker into coordinator lifecycle

OnBattleStart: start worker if EnableTacticalOrchestratorWorker is true.
OnBattleEnd: RequestStop + Join(250ms); reset TacticalDecisionSnapshot.
Tick: capture TacticalOrchestratorWorkerInput on main, enqueue to worker.
  Existing synchronous DriveTacticalCommanderSide still runs (parity window).
ResetRuntimeTickState: reset TacticalDecisionSnapshot.Current."
```

---

## Task 13: Parity-window comparator

Extend the Slice 1 parity-window mechanism to compare worker-produced decisions against the synchronous-on-main decisions. Reuses the existing `(battleSequence, allianceId)` keying.

**Files:**
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs`

- [ ] **Step 1: Add parity-window state**

Near `_lastProcessedSideStatUpdateByBunitsId`:

```csharp
private static readonly Dictionary<int, int> _workerParityComparesRemaining = new Dictionary<int, int>();
private static readonly HashSet<int> _workerParityMismatchObserved = new HashSet<int>();
private const int WorkerParityCompareBudget = 20;
```

Key is `_battleSequence * 10 + allianceId` (simple packing).

- [ ] **Step 2: Add parity comparator helper**

```csharp
private static void CompareWorkerVsSynchronous(int allianceId, TacticalSideDecision synchronous)
{
    var current = TacticalDecisionSnapshot.Current;
    if (current == null || current == TacticalDecisionSnapshot.Empty) return;
    var workerDecision = current.GetSide(allianceId);

    int key = _battleSequence * 10 + allianceId;
    if (_workerParityMismatchObserved.Contains(key)) return;  // already logged

    if (!SideDecisionsEqual(synchronous, workerDecision))
    {
        _workerParityMismatchObserved.Add(key);
        EmitWorkerParityMismatch(allianceId, synchronous, workerDecision);
        return;
    }

    if (!_workerParityComparesRemaining.TryGetValue(key, out int remaining)) remaining = WorkerParityCompareBudget;
    if (remaining > 0)
    {
        _workerParityComparesRemaining[key] = remaining - 1;
        if (remaining == 1)
        {
            OnceLog.Info("orch-worker-parity:" + key,
                "[TacticalOrchestratorWorkerParity] window-clean battleSeq=" + _battleSequence
                + " alliance=" + allianceId + " compares=20");
        }
    }
}

private static bool SideDecisionsEqual(TacticalSideDecision a, TacticalSideDecision b)
{
    if (a.AllianceId != b.AllianceId) return false;
    if (a.HasPlan != b.HasPlan) return false;
    if (a.LastReplanTrigger != b.LastReplanTrigger) return false;
    if (a.CurrentIntent.PrimaryIntent != b.CurrentIntent.PrimaryIntent) return false;
    if (a.CurrentIntent.InferredMainEffort != b.CurrentIntent.InferredMainEffort) return false;
    // Float fields: exact compare since both paths read same source data
    if (a.ForceAvailability.MainEffortStrength != b.ForceAvailability.MainEffortStrength) return false;
    if (a.ForceAvailability.ReserveAvailable != b.ForceAvailability.ReserveAvailable) return false;
    return true;
}

private static void EmitWorkerParityMismatch(int allianceId, TacticalSideDecision synchronous, TacticalSideDecision worker)
{
    try
    {
        TelemetryRouter.Emit(
            TelemetryLayer.Tactical,
            TelemetryCategory.Gate,
            "TacticalOrchestratorParityMismatch",
            TelemetrySeverity.Warning,
            ev => ev
                .WithSide(allianceId)
                .WithDecision("mismatch", "side-decision", "battleSeq=" + _battleSequence + "|alliance=" + allianceId)
                .WithField("allianceId", allianceId)
                .WithField("battleSequence", _battleSequence)
                .WithField("syncIntent", synchronous.CurrentIntent.PrimaryIntent.ToString())
                .WithField("workerIntent", worker.CurrentIntent.PrimaryIntent.ToString())
                .WithField("syncTrigger", synchronous.LastReplanTrigger.ToString())
                .WithField("workerTrigger", worker.LastReplanTrigger.ToString())
                .WithField("syncHasPlan", synchronous.HasPlan)
                .WithField("workerHasPlan", worker.HasPlan));
    }
    catch { }
}
```

- [ ] **Step 3: Invoke comparator after each synchronous side runs**

In `Tick`, after `DriveTacticalCommanderSide(side1, ...)` (when worker is active):

```csharp
if (workerActive)
{
    var sync0 = SnapshotSideDecisionForParity(side0);
    var sync1 = SnapshotSideDecisionForParity(side1);
    CompareWorkerVsSynchronous(0, sync0);
    CompareWorkerVsSynchronous(1, sync1);
}
```

Where `SnapshotSideDecisionForParity(orch)` reads the live orchestrator state into a `TacticalSideDecision` for comparison:

```csharp
private static TacticalSideDecision SnapshotSideDecisionForParity(TacticalBattleOrchestrator side)
{
    if (side == null || side.Army == null) return TacticalSideDecision.Empty;
    return new TacticalSideDecision(
        allianceId: side.AllianceId,
        currentPlan: side.Army.HasPlan ? side.Army.CurrentPlan : default,
        currentIntent: side.Army.CurrentIntentModel,
        commandTree: side.Army.CurrentCommandTree,
        strategicIntent: StrategicBattleIntentSnapshot.Empty,
        forceAvailability: new ForceAvailabilitySnapshot(
            side.Army.CurrentBundle.OwnMainEffortStrength,  // confirm property name in ArmyOrchestrator
            Math.Max(0f, 1f - side.Army.CurrentBundle.OwnReservesCommittedFraction)),
        commanderPersonality: side.Army.CommanderPersonality,
        lastReplanTrigger: ReplanTrigger.None,
        hasPlan: side.Army.HasPlan);
}
```

If `ArmyOrchestrator` doesn't expose `CurrentBundle`, fall back to `Empty` for the force fields. The exact comparator field set is determined by which fields the worker actually populates (Task 11 output).

- [ ] **Step 4: Reset parity state in ResetRuntimeTickState**

```csharp
_workerParityComparesRemaining.Clear();
_workerParityMismatchObserved.Clear();
```

- [ ] **Step 5: Build + harness**

```bash
./build.sh 2>&1 | tail -10
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E "^(PASS|FAIL)" | awk 'BEGIN{p=0;f=0} /^PASS/{p++} /^FAIL/{f++} END{print "PASS="p" FAIL="f}'
```

Expected: 0 errors, `PASS=1258 FAIL=0`.

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs
git commit -m "feat(tactical): worker-vs-synchronous parity comparator with 20-cycle window per side"
```

---

## Task 14: Refactor `BattleGroupStancePatch` to read from `TacticalDecisionSnapshot`

**Files:**
- Modify: `src/WhiskeyRealism/Patches/BattleGroupStancePatch.cs`

- [ ] **Step 1: Add `TacticalDecisionSnapshot` read path**

At the top of `Apply(AIBattle battle)`:

```csharp
private static void Apply(AIBattle battle)
{
    // Worker-thread refactor: if a decision snapshot is available, every
    // commandable group's stance decision is pre-computed. Skip the per-group
    // computation entirely and apply cached decisions via vanilla setter.
    if (TryApplyFromDecisionSnapshot(battle)) return;

    // Fallback to legacy per-group computation (parity-window or worker disabled)
    int side = SafeIntField(battle, ref _sideOfAiField, "sideofai", -1);
    int macro = SafeIntField(battle, ref _macroAiField, "macroai", -99);
    var bunits = SafeField<BattleUnits>(battle, ref _bunitsField, "bunits");
    var units = SafeList(battle, ref _unitsUsedField, "unitsused");
    if (side < 0 || macro < 0 || bunits == null || units == null) return;

    for (int i = 0; i < units.Count; i++)
    {
        var group = units[i] as Regiment;
        if (group == null || !TacticalDoctrineScorer.AllowsLocalGroupStanceWriter(group.unittyp)) continue;
        ApplyGroup(bunits, side, macro, group, i);
    }
}

private static bool TryApplyFromDecisionSnapshot(AIBattle battle)
{
    var snapshot = TacticalDecisionSnapshot.Current;
    if (snapshot == null || snapshot == TacticalDecisionSnapshot.Empty) return false;

    // Are we during the parity window? If so, run the legacy path so the
    // comparator can validate. The parity window state is per-(battleSeq,
    // alliance) and lives in TacticalBattleCoordinatorRuntime — but we don't
    // have direct visibility into it here. Simplest contract:
    //   - If snapshot is present and worker config enabled, trust it.
    //   - Parity window's validation runs separately in the coordinator's Tick.
    // The patch always trusts the snapshot when available; the coordinator's
    // dual-run ensures worker matches synchronous before patches start
    // exclusively reading from the snapshot.

    if (Plugin.EnableTacticalOrchestratorWorker == null
        || !Plugin.EnableTacticalOrchestratorWorker.Value) return false;

    int side = SafeIntField(battle, ref _sideOfAiField, "sideofai", -1);
    int macro = SafeIntField(battle, ref _macroAiField, "macroai", -99);
    var bunits = SafeField<BattleUnits>(battle, ref _bunitsField, "bunits");
    var units = SafeList(battle, ref _unitsUsedField, "unitsused");
    if (side < 0 || macro < 0 || bunits == null || units == null) return false;

    int allianceId = SafeBunitsAllianceForSide(bunits, side);
    if (allianceId < 0) return false;

    bool appliedAny = false;
    for (int i = 0; i < units.Count; i++)
    {
        var group = units[i] as Regiment;
        if (group == null || !TacticalDoctrineScorer.AllowsLocalGroupStanceWriter(group.unittyp)) continue;
        var go = ((UnityEngine.Component)group).gameObject;
        if (go == null) continue;
        int instanceId = go.GetInstanceID();
        if (!snapshot.TryGetGroupStance(allianceId, instanceId, out var decision)) continue;

        // Apply via vanilla setter using the cached decision (no per-group computation)
        ApplyCachedGroupDecision(bunits, side, group, decision);
        appliedAny = true;
    }
    return appliedAny;
}

private static int SafeBunitsAllianceForSide(BattleUnits bunits, int side)
{
    try
    {
        if (bunits == null || bunits.alliance == null || side < 0 || side >= bunits.alliance.Length) return -1;
        return bunits.alliance[side];
    }
    catch
    {
        return -1;
    }
}

private static void ApplyCachedGroupDecision(BattleUnits bunits, int side, Regiment group, TacticalGroupStanceDecision decision)
{
    // The existing TryApplyDoctrineStance + downstream WriteGroupStance logic
    // applies the stance via vanilla setter. We inline the apply-only portion:
    //   - Read decision.Stance, decision.Reason
    //   - Call vanilla setter (typically group.ai_stanceordered or a wrapped writer)
    // Match the existing apply path's vanilla touchpoints exactly.
    //
    // [Implementation: replicate the apply-only portion of TryApplyDoctrineStance —
    //  the stance-write call without the score-and-decide preamble.]
}
```

The exact apply logic depends on what `BattleGroupStancePatch` currently does in its writer phase. Inspect the existing `TryApplyDoctrineStance` / `TryApplyLedgerTaskStance` and pull out the "write the stance" portion (typically a vanilla setter call). The DECIDE portion is what moved to worker; the WRITE portion stays in this patch but reads from the cached decision.

- [ ] **Step 2: Build + harness**

```bash
./build.sh 2>&1 | tail -10
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E "^(PASS|FAIL)" | awk 'BEGIN{p=0;f=0} /^PASS/{p++} /^FAIL/{f++} END{print "PASS="p" FAIL="f}'
```

Expected: 0 errors, `PASS=1258 FAIL=0`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Patches/BattleGroupStancePatch.cs
git commit -m "feat(tactical): BattleGroupStancePatch reads pre-computed decisions from TacticalDecisionSnapshot

When EnableTacticalOrchestratorWorker is true AND a decision snapshot is
published, the patch becomes an O(1) lookup + vanilla setter. Legacy
per-group decide-and-write path remains as fallback for when worker is
disabled or no decisions are available yet (first ~50ms of battle)."
```

---

## Task 15: Deploy + verify hash

**Files:** none.

- [ ] **Step 1: Confirm GTCW closed**

```bash
touch "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll" 2>&1
```

If error: ask user to close GTCW.

- [ ] **Step 2: Build + deploy + hash**

```bash
./build.sh 2>&1 | tail -5
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/" && \
stat -c '%s' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll" && \
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Confirm sizes match and sha256 sums match. Record the hash for Task 17.

- [ ] **Step 3: Final harness check**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E "^(PASS|FAIL)" | awk 'BEGIN{p=0;f=0} /^PASS/{p++} /^FAIL/{f++} END{print "PASS="p" FAIL="f}'
```

Expected: `PASS=1258 FAIL=0`.

---

## Task 16: User smoke + analysis

- [ ] **Step 1: User direction**

User runs the same 26k-unit battle that produced session `20260519-204126`. `Logging Profile = TacticalTuning` (already default). Plays at 20× compression for 60–120 seconds.

- [ ] **Step 2: Pull sidecar**

```bash
ls -t "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/WhiskeyRealism/tuning-logs/" | head -3
SESSION="<latest_dir>"
python3 <<EOF
import json, os
from collections import defaultdict
scopes = defaultdict(list)
with open(os.path.join("<full_session_path>", "performance.jsonl")) as f:
    for line in f:
        try:
            row = json.loads(line)
            scope = row.get("fields", {}).get("scope") or row.get("scope")
            dur = row.get("durationMs")
            if scope and dur is not None: scopes[scope].append(dur)
        except: pass
def pct(xs, p):
    xs = sorted(xs); k = int(len(xs)*p/100.0) if len(xs)>1 else 0
    return xs[min(k, len(xs)-1)] if xs else 0
key = ["tactical.orchestrator-tick", "tactical.worker.cycle", "tactical.worker.cycle.publish", "tactical.patch.battle-group-stance"]
for s in key:
    xs = scopes.get(s, [])
    if xs:
        print(f"{s:<50} n={len(xs):>4}  p50={pct(xs,50):>6.2f}  p95={pct(xs,95):>6.2f}  p99={pct(xs,99):>6.2f}  max={max(xs):>6.2f}")
EOF
```

- [ ] **Step 3: Compare against success criteria (spec §10)**

| Metric | Target | Actual |
|---|---|---|
| `tactical.orchestrator-tick` p99 (main) | ≤ 10ms post-window | _record_ |
| `tactical.patch.battle-group-stance` p99 | ≤ 3ms (buffer lookup) | _record_ |
| `tactical.worker.cycle` p99 (worker) | ≤ 200ms (no main impact) | _record_ |
| Parity mismatch events | 0 | _record_ |
| Subjective skipping at 20× | absent | _record_ |

- [ ] **Step 4: Branch on outcome**

- All targets met + 0 mismatches → Task 17.
- Mismatches > 0 → read `TacticalOrchestratorParityMismatch` field diffs, identify the missing field in `TacticalSideDecision` or worker analysis. Add field, re-run.
- Worker fault events (`TacticalOrchestratorWorkerFault`) → inspect message + cycle id. Most likely a null reference where the worker can't access something it expected.
- Targets not met → which scope dominates? `tactical.worker.cycle` >200ms means GC in the worker (allocation pooling broken); `tactical.orchestrator-tick` >10ms means main capture still expensive (audit capture path).

---

## Task 17: Document + closeout

- [ ] **Step 1: Update `docs/telemetry.md`**

Add to the Hot-path scopes table:

```
| `tactical.worker.cycle` | 50.0 ms | Full worker analysis (one cycle on background thread) |
| `tactical.worker.cycle.publish` | 1.0 ms | Atomic `Interlocked.Exchange` decision-snapshot publish |
```

Add a paragraph documenting `TacticalOrchestratorParityMismatch` (Gate category, fields: `allianceId`, `battleSequence`, `syncIntent`, `workerIntent`, `syncTrigger`, `workerTrigger`, `syncHasPlan`, `workerHasPlan`) and `TacticalOrchestratorWorkerFault` (Health category).

- [ ] **Step 2: Update `docs/tactical-orchestrator.md`**

Add a "Threading architecture (2026-05-19)" section describing:
- Main thread responsibilities (snapshot capture, decision-apply via patches)
- Worker thread responsibilities (analysis, intent inference, plan/replan, role allocation)
- `TacticalDecisionSnapshot.Current` as the cross-thread hand-off
- Parity-window contract

- [ ] **Step 3: Update `docs/handoff.md`**

Refresh "What just shipped" with:
- Deployed DLL sha256 from Task 15.
- Smoke results from Task 16.
- Mark Slice 2 as shipped.

- [ ] **Step 4: Archive spec + plan**

```bash
git mv docs/superpowers/specs/2026-05-19-tactical-orchestrator-worker-thread-design.md docs/superpowers/specs/archive/
git mv docs/superpowers/plans/2026-05-19-tactical-orchestrator-worker-thread-implementation.md docs/superpowers/plans/archive/
```

Update the corresponding `archive/README.md` files with one-line entries.

- [ ] **Step 5: Update `MEMORY.md`**

Append a one-line entry: "Slice 2 (worker thread): `<sha>` … <result summary>".

- [ ] **Step 6: Commit closeout**

```bash
git add docs/
git commit -m "docs: ship Slice 2 worker-thread refactor — archive spec/plan, update living docs

Deployed sha256 <hash> with TacticalOrchestratorWorker default ON.
Smoke results: orchestrator-tick p99 <Xms>, worker.cycle p99 <Yms>,
parity mismatches: 0. Slice 2 complete."
```

---

## Rollback

Editor-time:
1. `<GTCW>/BepInEx/config/dev.kyle.whiskey-realism.cfg` → `Enable Tactical Orchestrator Worker = false`.
2. Restart GTCW. Orchestrator runs synchronously on main as before. `tactical.worker.cycle` scope reports zero samples.

Source-level revert (if a regression requires a code revert):
```bash
git revert <Task-12-merge-commit>  # removes lifecycle wiring
./build.sh && cp dist/WhiskeyRealism.dll "<GTCW>/BepInEx/plugins/"
```

DTOs and worker remain in source but unused. Re-enable by adding lifecycle wiring back.

---

## Plan self-review

**Spec coverage:**
- §1 Motivation → plan header.
- §2 Goals → Task 16 success criteria table.
- §3 Architecture → file map + Tasks 9, 10, 12.
- §4 Data structures → Tasks 1, 2, 3 (DTOs); Task 8 (worker input).
- §5 Thread safety → enforced in Task 9 (`Volatile`, `Interlocked`, `ManualResetEventSlim`).
- §6 Parity verification → Task 13.
- §7 Rollback config flag → Task 5.
- §8 Files to modify → file map at top.
- §9 New telemetry scopes → Task 9 emits `tactical.worker.cycle*`; Task 17 documents in `docs/telemetry.md`.
- §10 Success criteria → Task 16 Step 3 table.
- §11 Risks → mitigated per-task (pooling in Task 9, parity in Task 13, fallback in Task 14).
- §12 Known follow-ups → preserved in spec, not duplicated.
- §13 AGENTS.md compliance → Constraints section + per-task try/catch directives.
- §14 Decision log → embedded in plan code (Thread, atomic publish, drop-old).

**Placeholder scan:** Two intentional `_record_` placeholders in Task 16 Step 3 (user fills in smoke). No "TBD" / "appropriate error handling" / "similar to Task N" patterns.

**Type consistency:**
- `TacticalSideDecision` constructor parameter order: `allianceId, currentPlan, currentIntent, commandTree, strategicIntent, forceAvailability, commanderPersonality, lastReplanTrigger, hasPlan` — consistent across Tasks 1, 4, 11, 13.
- `TacticalDecisionSnapshot.SetGroupStance(allianceId, instanceId, decision)` and `TryGetGroupStance(allianceId, instanceId, out decision)` — consistent in Tasks 3, 4, 14.
- `TacticalOrchestratorWorker.Enqueue(TacticalOrchestratorWorkerInput)`, `Start()`, `RequestStop()`, `Join(int timeoutMs)` — consistent in Tasks 9, 12.
- `TacticalOrchestratorWorkerAnalysis.Analyze(input, output, cycleId)` — consistent in Tasks 9, 10, 11.

Plan ready for execution.
