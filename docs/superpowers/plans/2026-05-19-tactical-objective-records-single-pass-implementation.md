# Tactical ObjectiveRecords Single-Pass Refactor — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cut `BuildObjectiveRecordsFromBattle` wall time by walking `BattleUnits.completeunitlist` once into a pooled `TacticalUnitObservationAggregate`, feeding six refactored sub-builders via `IObservationSource`. Steady-state target: `tactical.snapshot-build.objective-records.aggregate` p99 ≤ 3 ms (down from 11 ms outer-scope baseline).

**Architecture:** Additive new types (`TacticalUnitObservation`, `IObservationSource`, `TacticalUnitObservationAggregate`) introduced first with harness coverage. New `BuildObjectiveRecordsFromAggregate` path runs alongside the existing `BuildObjectiveRecordsFromBattle` legacy implementation during a 20-Build per-`(battleSequence, allianceId)` parity window. Two new performance scopes (`.aggregate`, `.legacy`) isolate measurements so the parity dual-run doesn't poison success-metric p99. Rollback config flag `EnableSinglePassObjectiveRecords` (default ON) reverts to legacy path if needed.

**Tech Stack:** C# netstandard2.1, BepInEx 5.4 + HarmonyX, Unity 2021.3.16f1 Mono x64. Existing infrastructure: `TelemetryPerf.Scope`, `TelemetryRouter.Emit`, `TacticalBattleCoordinatorRuntime.ResetRuntimeTickState`, `TelemetryTagPolicy`.

**Source-of-truth:** `docs/superpowers/specs/2026-05-19-tactical-objective-records-single-pass-design.md`

---

## File map

| File | Status | Responsibility |
|---|---|---|
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalUnitObservation.cs` | **NEW** | Value struct holding one unit's captured fields |
| `src/WhiskeyRealism/Tactical/Orchestrator/IObservationSource.cs` | **NEW** | Read-only view interface consumed by sub-builders |
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalUnitObservationAggregate.cs` | **NEW** | Pooled `IObservationSource` impl + single-walk `Capture(allianceId)` |
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalVisionRuntimeAdapter.cs` | **MODIFY** | Add `BuildObjectiveRecordsFromAggregate` + aggregate-source sub-builder variants; preserve legacy path |
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleSnapshotBuilder.cs` | **MODIFY** | Capture aggregate once, dispatch parity window or aggregate-only path |
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs` | **MODIFY** | Clear aggregate + parity state in `ResetRuntimeTickState` |
| `src/WhiskeyRealism/Plugin.cs` | **MODIFY** | New `EnableSinglePassObjectiveRecords` ConfigEntry |
| `src/WhiskeyRealism/Telemetry/TelemetryTagPolicy.cs` | **MODIFY** | Register `TacticalObjectiveRecordsParityMismatch` event |
| `tests/WhiskeyRealism.Tests/TacticalUnitObservationAggregateTests.cs` | **NEW** | Harness coverage of `Capture` field-by-field correctness |
| `tests/WhiskeyRealism.Tests/ObjectiveRecordsFromAggregateTests.cs` | **NEW** | Harness coverage of aggregate-source sub-builder outputs |
| `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` | **MODIFY** | `<Compile Include Link>` for the 3 new production files only (test files auto-include via SDK defaults) |
| `docs/telemetry.md` | **MODIFY** | Document new scopes + parity event + measured p99 post-smoke |
| `docs/tactical-orchestrator.md` | **MODIFY** | Living-doc update: aggregate module + parity window contract |
| `docs/handoff.md` | **MODIFY** | Post-smoke: shipped DLL hash, baseline+target deltas |

---

## Constraints (read first)

- **Netstandard2.1.** No nullable annotations syntax (`?`), no records, no init-only setters, no top-level statements.
- **No Unity API on non-main threads.** Capture happens on main thread inside Harmony postfix call chain. Safe.
- **Try/catch around every reflection touch in capture.** `Regiment.lastsetwaypointposition`, `unitrange.enemyin*rangereg`, etc. — degrade to default values on failure, never throw.
- **AGENTS.md default-on policy:** new tactical-behavior flag ships default `true`. Description string matches the actual default.
- **Parity state per `(battleSequence, allianceId)`.** Side 0 mismatch must NOT shut down side 1's window.
- **Field audit before capture code.** Spec §4a. Task 6 enumerates every `Regiment` member access; any field discovered during impl that's missing gets added to the struct in the same task.

---

## Task 1: Add `TacticalUnitObservation` value struct

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalUnitObservation.cs`

- [ ] **Step 1: Create the struct file**

```csharp
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Per-unit observation captured ONCE per <see cref="TacticalUnitObservationAggregate.Capture"/>
    /// call. All fields are immutable after capture. Alliance-aware:
    /// visibility / objective / waypoint fields are populated only when the
    /// captured unit's alliance matches the capture's <c>allianceId</c>;
    /// enemy units get cheap fields (position, strength, unittyp, routed
    /// flag) only. This matches the pre-refactor cost profile, which never
    /// invoked the visibility walk for enemy units.
    /// </summary>
    public readonly struct TacticalUnitObservation
    {
        public TacticalUnitObservation(
            int instanceId,
            int unittyp,
            int alliance,
            bool isRouted,
            float worldX,
            float worldZ,
            float strength,
            float groupOwnInRange,
            float groupAiGroup,
            bool hasCurrentSetObjective,
            int currentSetObjectiveId,
            float objectiveX,
            float objectiveZ,
            TacticalObjectiveType objectiveType,
            bool hasLastWaypoint,
            float lastWaypointX,
            float lastWaypointZ,
            float visibleEnemyStrength,
            bool hasVisibleEnemy,
            float fatigue01,
            float ammo01,
            int effectiveCommandLevel)
        {
            InstanceId = instanceId;
            Unittyp = unittyp;
            Alliance = alliance;
            IsRouted = isRouted;
            WorldX = worldX;
            WorldZ = worldZ;
            Strength = strength;
            GroupOwnInRange = groupOwnInRange;
            GroupAiGroup = groupAiGroup;
            HasCurrentSetObjective = hasCurrentSetObjective;
            CurrentSetObjectiveId = currentSetObjectiveId;
            ObjectiveX = objectiveX;
            ObjectiveZ = objectiveZ;
            ObjectiveType = objectiveType;
            HasLastWaypoint = hasLastWaypoint;
            LastWaypointX = lastWaypointX;
            LastWaypointZ = lastWaypointZ;
            VisibleEnemyStrength = visibleEnemyStrength;
            HasVisibleEnemy = hasVisibleEnemy;
            Fatigue01 = fatigue01;
            Ammo01 = ammo01;
            EffectiveCommandLevel = effectiveCommandLevel;
        }

        public int InstanceId { get; }
        public int Unittyp { get; }
        public int Alliance { get; }
        public bool IsRouted { get; }
        public float WorldX { get; }
        public float WorldZ { get; }
        public float Strength { get; }
        public float GroupOwnInRange { get; }
        public float GroupAiGroup { get; }
        public bool HasCurrentSetObjective { get; }
        public int CurrentSetObjectiveId { get; }
        public float ObjectiveX { get; }
        public float ObjectiveZ { get; }
        public TacticalObjectiveType ObjectiveType { get; }
        public bool HasLastWaypoint { get; }
        public float LastWaypointX { get; }
        public float LastWaypointZ { get; }
        public float VisibleEnemyStrength { get; }
        public bool HasVisibleEnemy { get; }
        public float Fatigue01 { get; }
        public float Ammo01 { get; }
        public int EffectiveCommandLevel { get; }
    }
}
```

- [ ] **Step 2: Add csproj include for the test assembly**

Edit `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`. Find the `<ItemGroup>` containing the `<Compile Include="..\..\src\WhiskeyRealism\Strategic\…">` entries and add (alphabetically near other tactical entries):

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\TacticalUnitObservation.cs" Link="TacticalUnitObservation.cs" />
```

- [ ] **Step 3: Run harness to confirm clean build**

Run: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E "^(PASS|FAIL)" | awk 'BEGIN{p=0;f=0} /^PASS/{p++} /^FAIL/{f++} END{print "PASS="p" FAIL="f}'`

Expected: `PASS=1245 FAIL=0` (baseline; struct compiles, no behavior change).

- [ ] **Step 4: Commit**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/TacticalUnitObservation.cs \
        tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "feat(tactical): add TacticalUnitObservation value struct"
```

---

## Task 2: Add `IObservationSource` interface

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/IObservationSource.cs`

- [ ] **Step 1: Create the interface file**

```csharp
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Read-only view onto a captured set of <see cref="TacticalUnitObservation"/>.
    /// Consumed by the aggregate-based variants of the six ObjectiveRecord
    /// sub-builders. Implementations: <see cref="TacticalUnitObservationAggregate"/>
    /// for runtime, harness stubs for tests.
    /// </summary>
    public interface IObservationSource
    {
        /// <summary>
        /// Total number of units captured (allied + enemy combined).
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Alliance the aggregate was captured for. Visibility / objective /
        /// waypoint fields are populated only for units where
        /// <c>Alliance == CapturedForAlliance</c>.
        /// </summary>
        int CapturedForAlliance { get; }

        /// <summary>
        /// All captured units in <see cref="BattleUnits.completeunitlist"/>
        /// iteration order. Index is stable within a single capture.
        /// </summary>
        IReadOnlyList<TacticalUnitObservation> AllUnits { get; }

        /// <summary>
        /// Indices into <see cref="AllUnits"/> for units where
        /// <c>Alliance == CapturedForAlliance</c>. Empty when no own-side units captured.
        /// </summary>
        IReadOnlyList<int> AlliedIndices { get; }

        /// <summary>
        /// Indices into <see cref="AllUnits"/> for units where
        /// <c>Alliance != CapturedForAlliance</c>. Empty when no enemy units captured.
        /// </summary>
        IReadOnlyList<int> EnemyIndices { get; }
    }
}
```

- [ ] **Step 2: Add csproj include**

Edit `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, add after the `TacticalUnitObservation.cs` line:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\IObservationSource.cs" Link="IObservationSource.cs" />
```

- [ ] **Step 3: Run harness to confirm clean build**

Run: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E "^(PASS|FAIL)" | awk 'BEGIN{p=0;f=0} /^PASS/{p++} /^FAIL/{f++} END{print "PASS="p" FAIL="f}'`

Expected: `PASS=1245 FAIL=0`.

- [ ] **Step 4: Commit**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/IObservationSource.cs \
        tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "feat(tactical): add IObservationSource interface"
```

---

## Task 3: Add `TacticalUnitObservationAggregate` (pure, no Unity)

The runtime capture (which touches `BattleUnits.completeunitlist`) is split into a separate partial in Task 6 so the harness can construct an aggregate from in-memory observations without referencing Unity types.

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalUnitObservationAggregate.cs`

- [ ] **Step 1: Create the aggregate file (pure portion)**

```csharp
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pooled <see cref="IObservationSource"/> implementation. Reused
    /// across Build calls — instance is per-battle (cleared on battle
    /// end via <see cref="ClearForBattleEnd"/>); steady-state per-Build
    /// allocations are zero after the underlying lists grow into their
    /// working size.
    ///
    /// The runtime-only <see cref="Capture"/> entry (which touches
    /// <c>BattleUnits.completeunitlist</c>) lives in
    /// <c>TacticalUnitObservationAggregate.Runtime.cs</c>; harness tests
    /// build aggregates via <see cref="LoadForTest"/>.
    /// </summary>
    public sealed partial class TacticalUnitObservationAggregate : IObservationSource
    {
        private static readonly TacticalUnitObservationAggregate _shared = new TacticalUnitObservationAggregate();

        private readonly List<TacticalUnitObservation> _units = new List<TacticalUnitObservation>(512);
        private readonly List<int> _alliedIndices = new List<int>(64);
        private readonly List<int> _enemyIndices = new List<int>(64);
        private int _capturedForAlliance = -1;

        public static TacticalUnitObservationAggregate Shared
        {
            get { return _shared; }
        }

        public int Count
        {
            get { return _units.Count; }
        }

        public int CapturedForAlliance
        {
            get { return _capturedForAlliance; }
        }

        public IReadOnlyList<TacticalUnitObservation> AllUnits
        {
            get { return _units; }
        }

        public IReadOnlyList<int> AlliedIndices
        {
            get { return _alliedIndices; }
        }

        public IReadOnlyList<int> EnemyIndices
        {
            get { return _enemyIndices; }
        }

        /// <summary>
        /// Clears the aggregate. Called from
        /// <c>TacticalBattleCoordinator.ResetRuntimeTickState</c> on
        /// battle end so the next battle's first capture starts clean.
        /// </summary>
        public void ClearForBattleEnd()
        {
            _units.Clear();
            _alliedIndices.Clear();
            _enemyIndices.Clear();
            _capturedForAlliance = -1;
        }

        /// <summary>
        /// Harness-only loader. Replaces the captured contents with the
        /// supplied observations and rebuilds index views from the
        /// supplied alliance id. Not called from runtime code.
        /// </summary>
        public void LoadForTest(int allianceId, IReadOnlyList<TacticalUnitObservation> units)
        {
            _units.Clear();
            _alliedIndices.Clear();
            _enemyIndices.Clear();
            _capturedForAlliance = allianceId;
            if (units == null) return;
            for (int i = 0; i < units.Count; i++)
            {
                _units.Add(units[i]);
                if (units[i].Alliance == allianceId) _alliedIndices.Add(i);
                else _enemyIndices.Add(i);
            }
        }
    }
}
```

- [ ] **Step 2: Add csproj include**

Edit `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\TacticalUnitObservationAggregate.cs" Link="TacticalUnitObservationAggregate.cs" />
```

- [ ] **Step 3: Build and run harness**

Run: `./build.sh 2>&1 | tail -5 && dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E "^(PASS|FAIL)" | awk 'BEGIN{p=0;f=0} /^PASS/{p++} /^FAIL/{f++} END{print "PASS="p" FAIL="f}'`

Expected: Build success, `PASS=1245 FAIL=0`.

- [ ] **Step 4: Commit**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/TacticalUnitObservationAggregate.cs \
        tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "feat(tactical): add TacticalUnitObservationAggregate pure portion"
```

---

## Task 4: Harness tests for `LoadForTest` + index views

**Files:**
- Create: `tests/WhiskeyRealism.Tests/TacticalUnitObservationAggregateTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using System;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tests
{
    internal static class TacticalUnitObservationAggregateTests
    {
        public static void Run()
        {
            LoadForTestPartitionsAlliedAndEnemyIndices();
            LoadForTestClearsPriorCapture();
            ClearForBattleEndResetsAlliance();
            EmptyLoadProducesEmptyViews();
        }

        private static TacticalUnitObservation MakeUnit(int instanceId, int alliance, float worldX = 0f, float worldZ = 0f)
        {
            return new TacticalUnitObservation(
                instanceId: instanceId,
                unittyp: 0,
                alliance: alliance,
                isRouted: false,
                worldX: worldX,
                worldZ: worldZ,
                strength: 100f,
                groupOwnInRange: 100f,
                groupAiGroup: 100f,
                hasCurrentSetObjective: false,
                currentSetObjectiveId: 0,
                objectiveX: 0f,
                objectiveZ: 0f,
                objectiveType: TacticalObjectiveType.UnknownVanillaObjective,
                hasLastWaypoint: false,
                lastWaypointX: 0f,
                lastWaypointZ: 0f,
                visibleEnemyStrength: 0f,
                hasVisibleEnemy: false,
                fatigue01: 0.2f,
                ammo01: 0.9f,
                effectiveCommandLevel: 0);
        }

        private static void LoadForTestPartitionsAlliedAndEnemyIndices()
        {
            var agg = new TacticalUnitObservationAggregate();
            var units = new[]
            {
                MakeUnit(1, alliance: 0),
                MakeUnit(2, alliance: 1),
                MakeUnit(3, alliance: 0),
                MakeUnit(4, alliance: 1)
            };
            agg.LoadForTest(allianceId: 0, units: units);
            TestHarness.AssertEqual(agg.Count, 4, "count");
            TestHarness.AssertEqual(agg.CapturedForAlliance, 0, "capturedForAlliance");
            TestHarness.AssertEqual(agg.AlliedIndices.Count, 2, "alliedCount");
            TestHarness.AssertEqual(agg.EnemyIndices.Count, 2, "enemyCount");
            TestHarness.AssertEqual(agg.AllUnits[agg.AlliedIndices[0]].InstanceId, 1, "alliedFirst");
            TestHarness.AssertEqual(agg.AllUnits[agg.EnemyIndices[0]].InstanceId, 2, "enemyFirst");
            TestHarness.Pass("tactical unit observation aggregate load partitions allied and enemy indices");
        }

        private static void LoadForTestClearsPriorCapture()
        {
            var agg = new TacticalUnitObservationAggregate();
            agg.LoadForTest(0, new[] { MakeUnit(1, 0), MakeUnit(2, 1) });
            agg.LoadForTest(1, new[] { MakeUnit(3, 1) });
            TestHarness.AssertEqual(agg.Count, 1, "count");
            TestHarness.AssertEqual(agg.CapturedForAlliance, 1, "alliance");
            TestHarness.AssertEqual(agg.AlliedIndices.Count, 1, "alliedCount");
            TestHarness.AssertEqual(agg.EnemyIndices.Count, 0, "enemyCount");
            TestHarness.Pass("tactical unit observation aggregate load clears prior capture");
        }

        private static void ClearForBattleEndResetsAlliance()
        {
            var agg = new TacticalUnitObservationAggregate();
            agg.LoadForTest(0, new[] { MakeUnit(1, 0) });
            agg.ClearForBattleEnd();
            TestHarness.AssertEqual(agg.Count, 0, "count");
            TestHarness.AssertEqual(agg.CapturedForAlliance, -1, "alliance");
            TestHarness.AssertEqual(agg.AlliedIndices.Count, 0, "alliedCount");
            TestHarness.AssertEqual(agg.EnemyIndices.Count, 0, "enemyCount");
            TestHarness.Pass("tactical unit observation aggregate clear for battle end resets alliance");
        }

        private static void EmptyLoadProducesEmptyViews()
        {
            var agg = new TacticalUnitObservationAggregate();
            agg.LoadForTest(0, Array.Empty<TacticalUnitObservation>());
            TestHarness.AssertEqual(agg.Count, 0, "count");
            TestHarness.AssertEqual(agg.AlliedIndices.Count, 0, "alliedCount");
            TestHarness.AssertEqual(agg.EnemyIndices.Count, 0, "enemyCount");
            TestHarness.Pass("tactical unit observation aggregate empty load produces empty views");
        }
    }
}
```

- [ ] **Step 2: Register the test entry-point**

Edit `tests/WhiskeyRealism.Tests/Program.cs`. Find the section that lists `…Tests.Run()` calls (consistent with the existing pattern of one `Run()` per test file) and add `TacticalUnitObservationAggregateTests.Run();` in tactical alphabetical order. If unsure where, search the file for `OrchestratorTickCycle` or similar tactical test invocations and insert nearby.

- [ ] **Step 3: Run harness — expect new tests PASS, total grows by 4**

Run: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E "^(PASS|FAIL)" | awk 'BEGIN{p=0;f=0} /^PASS/{p++} /^FAIL/{f++} END{print "PASS="p" FAIL="f}'`

Expected: `PASS=1249 FAIL=0` (baseline 1245 + 4 new tests).

- [ ] **Step 4: Commit**

```bash
git add tests/WhiskeyRealism.Tests/TacticalUnitObservationAggregateTests.cs \
        tests/WhiskeyRealism.Tests/Program.cs
git commit -m "test(tactical): cover TacticalUnitObservationAggregate load + clear contract"
```

---

## Task 5: Register `TacticalObjectiveRecordsParityMismatch` telemetry tag

**Files:**
- Modify: `src/WhiskeyRealism/Telemetry/TelemetryTagPolicy.cs`

- [ ] **Step 1: Find the registration block**

Run: `grep -n "TacticalHeavyGate\|TacticalCommandTreeProbeHealth" src/WhiskeyRealism/Telemetry/TelemetryTagPolicy.cs | head -5`

Note the line numbers where existing tactical tags are registered. The new tag goes alphabetically near `TacticalObserver*` or after `TacticalMacroDecision`, in the `TelemetryCategory.Gate` block (it's an A/B parity check, conceptually a gate decision).

- [ ] **Step 2: Add the tag registration**

Add (in the appropriate block):

```csharp
Register("TacticalObjectiveRecordsParityMismatch", TelemetryLayer.Tactical, TelemetryCategory.Gate);
```

If the existing pattern uses a different registration call shape (look at neighboring lines for the exact form — e.g., a builder, a dictionary `Add`, or an attribute), match that pattern. The tag must end up routed to the Gate category so it appears in `tactical.jsonl`.

- [ ] **Step 3: Build to confirm**

Run: `./build.sh 2>&1 | tail -5`

Expected: `0 Error(s)`.

- [ ] **Step 4: Run harness**

Run: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E "^(PASS|FAIL)" | awk 'BEGIN{p=0;f=0} /^PASS/{p++} /^FAIL/{f++} END{print "PASS="p" FAIL="f}'`

Expected: `PASS=1249 FAIL=0`.

- [ ] **Step 5: Commit**

```bash
git add src/WhiskeyRealism/Telemetry/TelemetryTagPolicy.cs
git commit -m "feat(telemetry): register TacticalObjectiveRecordsParityMismatch tag"
```

---

## Task 6: Add `EnableSinglePassObjectiveRecords` ConfigEntry

**Files:**
- Modify: `src/WhiskeyRealism/Plugin.cs`

- [ ] **Step 1: Find the existing config-bind block**

Run: `grep -n "EnableTacticalHeavyPathThrottling\|EnableTacticalOrchestrator " src/WhiskeyRealism/Plugin.cs | head -5`

Note the section pattern — there's a field declaration near line 89 and a `Config.Bind` call deeper in the file.

- [ ] **Step 2: Add the field declaration**

Add near the other `EnableTactical*` field declarations (search for `EnableTacticalHeavyPathThrottling` and add adjacent):

```csharp
public static ConfigEntry<bool> EnableSinglePassObjectiveRecords;
```

- [ ] **Step 3: Add the `Config.Bind` call**

In the Awake method's config-binding section, near the `EnableTacticalHeavyPathThrottling = Config.Bind(…)` call, add:

```csharp
EnableSinglePassObjectiveRecords = Config.Bind(
    "Tactical Orchestrator",
    "Enable Single-Pass Objective Records",
    true,
    "Single-pass aggregation for ObjectiveRecord building. Walks "
    + "BattleUnits.completeunitlist once instead of six times. Default ON. "
    + "Set false to roll back to the legacy per-walk path if a regression "
    + "appears. Default-on per AGENTS.md tactical policy. Measured "
    + "performance characteristics are documented in docs/telemetry.md "
    + "once smoke-verified.");
```

- [ ] **Step 4: Build to confirm**

Run: `./build.sh 2>&1 | tail -5`

Expected: `0 Error(s)`.

- [ ] **Step 5: Run harness**

Run: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E "^(PASS|FAIL)" | awk 'BEGIN{p=0;f=0} /^PASS/{p++} /^FAIL/{f++} END{print "PASS="p" FAIL="f}'`

Expected: `PASS=1249 FAIL=0`.

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Plugin.cs
git commit -m "feat(config): add EnableSinglePassObjectiveRecords flag (default ON)"
```

---

## Task 7: Field audit + runtime `Capture` partial

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalUnitObservationAggregate.Runtime.cs`

- [ ] **Step 1: Conduct the field audit**

Run these greps and record the output for the plan execution record (paste into commit message or a temporary note):

```bash
grep -nE 'Regiment[^A-Za-z_]|reg\.|regiment\.|unit\.unittyp|unit\.alliance|unit\.transform|unit\.fatigue|unit\.ammo|unit\.isrouted|unit\.markedforrout|unit\.lastsetwaypoint|unit\.unitrange|unit\.groupstrength|unit\.groupown' \
     src/WhiskeyRealism/Tactical/Orchestrator/TacticalVisionRuntimeAdapter.cs
```

Cross-check the discovered field reads against the audit table in spec §4a. Any new field (one not in the spec's audit table and not in `TacticalUnitObservation`) must be added to the struct in Task 1 — go back, add the field, re-run the test from Task 4 (with a corresponding new MakeUnit parameter), and re-commit. **Do not proceed past this step with a known-missing field.** Document the audit outcome in the next commit message.

- [ ] **Step 2: Create the runtime partial**

```csharp
using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Runtime portion of <see cref="TacticalUnitObservationAggregate"/>.
    /// Holds the Unity-touching <see cref="Capture"/> entry plus the
    /// reflection caches used by <see cref="CaptureUnit"/>. Excluded from
    /// the harness test compile (tests use <see cref="LoadForTest"/>).
    /// </summary>
    public sealed partial class TacticalUnitObservationAggregate
    {
        private static FieldInfo _commandHierarchyShiftField;
        private static FieldInfo _fatigueField;
        private static FieldInfo _ammoField;
        private static FieldInfo _groupStrengthActiveField;
        private static FieldInfo _lastWaypointField;
        private static int _captureFailureLogged;  // OnceLog gate

        /// <summary>
        /// Single-walk capture. Walks <c>BattleUnits.completeunitlist</c>
        /// once, populating allied-specific fields (visibility, objective,
        /// waypoint, fatigue/ammo) only when the candidate's
        /// <c>reg.alliance == allianceId</c>. Enemy units get cheap
        /// fields (position, strength, unittyp, routed flag) — matching
        /// the pre-refactor cost profile where visibility logic was
        /// gated by <c>IsUsableOwnUnit</c>.
        /// </summary>
        public IObservationSource Capture(int allianceId)
        {
            _units.Clear();
            _alliedIndices.Clear();
            _enemyIndices.Clear();
            _capturedForAlliance = allianceId;
            try
            {
                int shift = ReadCommandHierarchyShift();
                var raw = BattleUnits.completeunitlist as IList;
                if (raw == null) return this;
                for (int i = 0; i < raw.Count; i++)
                {
                    var reg = raw[i] as Regiment;
                    if (reg == null) continue;
                    bool isOwn = reg.alliance == allianceId;
                    var obs = CaptureUnit(reg, isOwn, shift);
                    _units.Add(obs);
                    int idx = _units.Count - 1;
                    if (isOwn) _alliedIndices.Add(idx);
                    else _enemyIndices.Add(idx);
                }
            }
            catch (Exception e)
            {
                if (_captureFailureLogged == 0)
                {
                    _captureFailureLogged = 1;
                    Plugin.Log.LogWarning("[TacticalUnitObservationAggregate.Capture] degraded: "
                        + e.GetType().Name + " " + e.Message);
                }
            }
            return this;
        }

        private TacticalUnitObservation CaptureUnit(Regiment reg, bool isOwn, int commandHierarchyShift)
        {
            int instanceId = 0;
            float worldX = 0f, worldZ = 0f;
            try
            {
                var go = ((Component)reg).gameObject;
                if (go != null)
                {
                    instanceId = go.GetInstanceID();
                    if (go.transform != null)
                    {
                        var p = go.transform.position;
                        worldX = p.x;
                        worldZ = p.z;
                    }
                }
            }
            catch { }

            int unittyp = SafeUnittyp(reg);
            int alliance = SafeAlliance(reg);
            bool isRouted = SafeIsRouted(reg);
            float strength = SafeFloat(reg, ref _groupStrengthActiveField, "groupstrengthactive");
            float groupOwnInRange = SafeOwnInRange(reg);
            float groupAiGroup = SafeAiGroup(reg);

            bool hasObj = false;
            int objId = 0;
            float objX = 0f, objZ = 0f;
            var objType = TacticalObjectiveType.UnknownVanillaObjective;
            bool hasLastWaypoint = false;
            float lastWaypointX = 0f, lastWaypointZ = 0f;
            float visibleEnemyStrength = 0f;
            bool hasVisibleEnemy = false;
            float fatigue01 = 0.2f;
            float ammo01 = 0.9f;

            if (isOwn)
            {
                // Current-set objective
                try
                {
                    var obj = TacticalVisionRuntimeAdapter.SafeCurrentSetObjective(reg);
                    if (obj != null)
                    {
                        var pt = TacticalVisionRuntimeAdapter.SafeObjectivePoint(obj);
                        if (TacticalVisionRuntimeAdapter.IsUsableMapPoint(pt))
                        {
                            hasObj = true;
                            objId = TacticalVisionRuntimeAdapter.SafeObjectiveIdHash(obj);
                            objX = pt.X;
                            objZ = pt.Z;
                            objType = TacticalObjectiveType.UnknownVanillaObjective;
                        }
                    }
                }
                catch { }

                // Last waypoint (TryMovementAnchorLine fallback)
                try
                {
                    Vector3 wp = reg.lastsetwaypointposition;
                    if (!(wp.x == 0f && wp.y == 0f && wp.z == 0f))
                    {
                        // Reject within √625m (25m) of current position to match TryLastWaypointPoint.
                        float dx = worldX - wp.x;
                        float dz = worldZ - wp.z;
                        if ((dx * dx) + (dz * dz) >= 625f)
                        {
                            hasLastWaypoint = true;
                            lastWaypointX = wp.x;
                            lastWaypointZ = wp.z;
                        }
                    }
                }
                catch { }

                // Visibility (own-side only — never call this for enemy units)
                try
                {
                    visibleEnemyStrength = TacticalFogOfWarContact.VisibleEnemyStrength(reg);
                    hasVisibleEnemy = TacticalFogOfWarContact.HasVisibleEnemy(reg);
                }
                catch { }

                fatigue01 = ClampUnit(SafeFloat(reg, ref _fatigueField, "fatigue"), 0.2f);
                ammo01 = ClampUnit(SafeFloat(reg, ref _ammoField, "ammo"), 0.9f);
            }

            int effective = unittyp - commandHierarchyShift;
            return new TacticalUnitObservation(
                instanceId: instanceId,
                unittyp: unittyp,
                alliance: alliance,
                isRouted: isRouted,
                worldX: worldX,
                worldZ: worldZ,
                strength: strength,
                groupOwnInRange: groupOwnInRange,
                groupAiGroup: groupAiGroup,
                hasCurrentSetObjective: hasObj,
                currentSetObjectiveId: objId,
                objectiveX: objX,
                objectiveZ: objZ,
                objectiveType: objType,
                hasLastWaypoint: hasLastWaypoint,
                lastWaypointX: lastWaypointX,
                lastWaypointZ: lastWaypointZ,
                visibleEnemyStrength: visibleEnemyStrength,
                hasVisibleEnemy: hasVisibleEnemy,
                fatigue01: fatigue01,
                ammo01: ammo01,
                effectiveCommandLevel: effective);
        }

        private static int SafeUnittyp(Regiment reg) { try { return reg.unittyp; } catch { return 0; } }
        private static int SafeAlliance(Regiment reg) { try { return reg.alliance; } catch { return -1; } }
        private static bool SafeIsRouted(Regiment reg)
        {
            try { return reg.isrouted || reg.markedforrout; }
            catch { return false; }
        }
        private static float SafeOwnInRange(Regiment reg) { try { return reg.groupowninrange; } catch { return 0f; } }
        private static float SafeAiGroup(Regiment reg) { try { return reg.groupstrengthaigroup; } catch { return 0f; } }

        private static float SafeFloat(Regiment reg, ref FieldInfo cache, string fieldName)
        {
            try
            {
                if (cache == null) cache = AccessTools.Field(typeof(Regiment), fieldName);
                if (cache == null) return 0f;
                var v = cache.GetValue(reg);
                return v is float f ? f : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        private static float ClampUnit(float v, float fallback)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return fallback;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static int ReadCommandHierarchyShift()
        {
            try
            {
                if (_commandHierarchyShiftField == null)
                    _commandHierarchyShiftField = AccessTools.Field(typeof(GamePrefs), "commandhierarchyshift");
                if (_commandHierarchyShiftField == null) return 0;
                var v = _commandHierarchyShiftField.GetValue(null);
                if (v is int shift) return shift;
                return 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
```

- [ ] **Step 3: Build to confirm**

Run: `./build.sh 2>&1 | tail -10`

If unresolved references to `TacticalVisionRuntimeAdapter.SafeCurrentSetObjective` / `SafeObjectivePoint` / `IsUsableMapPoint` / `SafeObjectiveIdHash` appear, those are currently `private` inside `TacticalVisionRuntimeAdapter`. Promote each to `internal static` (visibility is OK — they're already-tested helpers, and `internal` keeps them assembly-private). `SafeObjectiveIdHash` is a new helper — add as `internal static int SafeObjectiveIdHash(object objective) { try { return (objective?.GetHashCode() ?? 0); } catch { return 0; } }`.

Run `./build.sh` again until 0 errors.

- [ ] **Step 4: Run harness**

Run: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E "^(PASS|FAIL)" | awk 'BEGIN{p=0;f=0} /^PASS/{p++} /^FAIL/{f++} END{print "PASS="p" FAIL="f}'`

Expected: `PASS=1249 FAIL=0` (runtime partial isn't in test compile, but its visibility promotions must not break existing tests).

- [ ] **Step 5: Commit with field-audit notes**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/TacticalUnitObservationAggregate.Runtime.cs \
        src/WhiskeyRealism/Tactical/Orchestrator/TacticalVisionRuntimeAdapter.cs
git commit -m "feat(tactical): TacticalUnitObservationAggregate.Capture single-pass walk

Field audit recorded against spec §4a:
  - Main loop accesses: alliance, IsUsableOwnUnit (alliance+active+rout),
    SafeCurrentSetObjective, EstimateVisibleEnemyStrength, SafeStrength
  - TryMovementAnchorLine accesses: lastsetwaypointposition (with 25m reject)
  - BuildApproachAvenueObservations: own + enemy position/strength + visibility
  - TryVisibleEnemyLine: enemy filter + HasVisibleEnemy
  - AddMapObjectiveObservations / AddObjectiveChainObservations: no unit deps

Aggregate captures union of own-side reads with alliance-aware gating
(visibility/objective/waypoint only for own units). Reflection helpers
in TacticalVisionRuntimeAdapter promoted internal so the aggregate can
reuse them."
```

---

## Task 8: Refactor sub-builders to take `IObservationSource`

The sub-builders currently take `(AIBattle battle, int allianceId)` and walk `BattleUnits.completeunitlist`. We add new sibling functions that take `(IObservationSource source, int allianceId)` and read from the aggregate. The existing functions are KEPT (parity window needs both paths). No existing callers are touched in this task.

**Files:**
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalVisionRuntimeAdapter.cs`

- [ ] **Step 1: Add `BuildObjectiveRecordsFromAggregate` entry point**

Find the existing `BuildObjectiveRecordsFromBattle` function (~line 324). After its closing brace, add the new sibling:

```csharp
internal static ObjectiveRecord[] BuildObjectiveRecordsFromAggregate(
    IObservationSource source,
    AIBattle battle,
    int allianceId)
{
    try
    {
        if (source == null) return Array.Empty<ObjectiveRecord>();

        var observations = new List<ObjectiveObservationInput>();
        var statuses = new List<TacticalObjectiveStatus>();
        var enemyStrengths = new List<float>();
        var friendlyStrengths = new List<float>();
        var seenObjectiveIds = new HashSet<string>(StringComparer.Ordinal);

        AddObjectiveChainObservations(
            battle,
            observations,
            statuses,
            enemyStrengths,
            friendlyStrengths,
            seenObjectiveIds);

        // Main loop — own units with current-set objective.
        for (int ai = 0; ai < source.AlliedIndices.Count; ai++)
        {
            int idx = source.AlliedIndices[ai];
            var own = source.AllUnits[idx];
            if (own.IsRouted) continue;
            if (!own.HasCurrentSetObjective) continue;
            var point = new TacticalMapPoint(own.ObjectiveX, own.ObjectiveZ);
            if (!IsUsableMapPoint(point)) continue;

            string objectiveId = own.CurrentSetObjectiveId.ToString();
            if (!seenObjectiveIds.Add(objectiveId)) continue;

            observations.Add(new ObjectiveObservationInput(
                objectiveId,
                TacticalObjectiveType.UnknownVanillaObjective,
                TacticalObjectiveSource.CurrentSetObjective,
                point,
                sourceConfidence: 0.65f,
                value: 0.5f,
                typeAnchorVerified: false));
            statuses.Add(TacticalObjectiveStatus.Scouting);
            enemyStrengths.Add(own.VisibleEnemyStrength);
            friendlyStrengths.Add(own.Strength);
        }

        AddMapObjectiveObservations(
            allianceId,
            observations,
            statuses,
            enemyStrengths,
            friendlyStrengths,
            seenObjectiveIds);

        ApplyApproachAvenues(
            observations,
            allianceId,
            BuildApproachAvenueObservationsFromAggregate(source, allianceId));

        bool haveVisibleEnemy = TryVisibleEnemyLineFromAggregate(
            source,
            allianceId,
            out TacticalMapPoint visibleEnemyPoint,
            out float visibleEnemyStrength,
            out float visibleFriendlyStrength);
        TacticalMapPoint? visibleEnemyLine = haveVisibleEnemy ? visibleEnemyPoint : (TacticalMapPoint?)null;

        bool haveMovementAnchor = TryMovementAnchorLineFromAggregate(
            source,
            allianceId,
            out TacticalMapPoint movementAnchorPoint,
            out float movementAnchorFriendlyStrength);
        TacticalMapPoint? movementAnchor = haveMovementAnchor ? movementAnchorPoint : (TacticalMapPoint?)null;

        return BuildObjectiveRecordsWithMovementFallback(
            observations,
            statuses,
            enemyStrengths,
            friendlyStrengths,
            visibleEnemyLine,
            visibleEnemyStrength,
            visibleFriendlyStrength,
            movementAnchor,
            movementAnchorFriendlyStrength,
            allianceId);
    }
    catch (Exception e)
    {
        OnceLog.Warning("tactical-orch:objective-records-aggregate",
            "BuildObjectiveRecordsFromAggregate degraded: "
            + e.GetType().Name + " " + e.Message);
        return Array.Empty<ObjectiveRecord>();
    }
}
```

- [ ] **Step 2: Add `BuildApproachAvenueObservationsFromAggregate`**

Find the existing `BuildApproachAvenueObservations(int allianceId)` (search the file). Add a sibling immediately after it:

```csharp
private static List<TacticalApproachAvenueObservation> BuildApproachAvenueObservationsFromAggregate(
    IObservationSource source,
    int allianceId)
{
    var observations = new List<TacticalApproachAvenueObservation>();
    try
    {
        for (int ei = 0; ei < source.EnemyIndices.Count; ei++)
        {
            int idx = source.EnemyIndices[ei];
            var enemy = source.AllUnits[idx];
            if (enemy.IsRouted) continue;
            observations.Add(new TacticalApproachAvenueObservation(
                new TacticalMapPoint(enemy.WorldX, enemy.WorldZ),
                enemyStrength: enemy.Strength,
                friendlyStrength: 0f,
                origin: TacticalApproachAvenueOrigin.EnemyContact));
        }
    }
    catch (Exception e)
    {
        OnceLog.Warning("tactical-orch:approach-avenue-aggregate",
            "BuildApproachAvenueObservationsFromAggregate degraded: "
            + e.GetType().Name + " " + e.Message);
    }
    return observations;
}
```

(Field set may need expansion if the original walks more enemy data — confirm during Task 9 parity-test failure analysis.)

- [ ] **Step 3: Add `TryVisibleEnemyLineFromAggregate`**

Find the existing `TryVisibleEnemyLine(IList units, int allianceId, …)` and add a sibling:

```csharp
private static bool TryVisibleEnemyLineFromAggregate(
    IObservationSource source,
    int allianceId,
    out TacticalMapPoint point,
    out float enemyStrength,
    out float friendlyStrength)
{
    point = default;
    enemyStrength = 0f;
    friendlyStrength = 0f;
    float bestStrength = 0f;
    float bestX = 0f;
    float bestZ = 0f;
    bool any = false;
    try
    {
        for (int ai = 0; ai < source.AlliedIndices.Count; ai++)
        {
            int idx = source.AlliedIndices[ai];
            var own = source.AllUnits[idx];
            if (!own.HasVisibleEnemy) continue;
            float vs = own.VisibleEnemyStrength;
            if (vs > bestStrength)
            {
                bestStrength = vs;
                bestX = own.WorldX;
                bestZ = own.WorldZ;
                friendlyStrength = own.Strength;
                any = true;
            }
        }
        if (!any) return false;
        var p = new TacticalMapPoint(bestX, bestZ);
        if (!IsUsableMapPoint(p)) return false;
        point = p;
        enemyStrength = bestStrength;
        return true;
    }
    catch
    {
        return false;
    }
}
```

- [ ] **Step 4: Add `TryMovementAnchorLineFromAggregate`**

```csharp
private static bool TryMovementAnchorLineFromAggregate(
    IObservationSource source,
    int allianceId,
    out TacticalMapPoint point,
    out float friendlyStrength)
{
    point = default;
    friendlyStrength = 0f;
    try
    {
        // Pick the strongest own unit with a valid last waypoint
        // (already reject-filtered within 25m of current position during capture).
        float bestStrength = 0f;
        float bestX = 0f, bestZ = 0f;
        float bestFriendly = 0f;
        bool any = false;
        for (int ai = 0; ai < source.AlliedIndices.Count; ai++)
        {
            int idx = source.AlliedIndices[ai];
            var own = source.AllUnits[idx];
            if (!own.HasLastWaypoint) continue;
            if (own.IsRouted) continue;
            if (own.Strength > bestStrength)
            {
                bestStrength = own.Strength;
                bestX = own.LastWaypointX;
                bestZ = own.LastWaypointZ;
                bestFriendly = own.Strength;
                any = true;
            }
        }
        if (!any) return false;
        var p = new TacticalMapPoint(bestX, bestZ);
        if (!IsUsableMapPoint(p)) return false;
        point = p;
        friendlyStrength = bestFriendly;
        return true;
    }
    catch
    {
        return false;
    }
}
```

- [ ] **Step 5: Build to confirm**

Run: `./build.sh 2>&1 | tail -10`

If compile errors reference missing field or method, audit the corresponding pre-existing function and either (a) promote private helper to `internal` or (b) inline the small helper into the aggregate-source variant. Do not create new public surface.

- [ ] **Step 6: Run harness**

Run: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E "^(PASS|FAIL)" | awk 'BEGIN{p=0;f=0} /^PASS/{p++} /^FAIL/{f++} END{print "PASS="p" FAIL="f}'`

Expected: `PASS=1249 FAIL=0`.

- [ ] **Step 7: Commit**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/TacticalVisionRuntimeAdapter.cs
git commit -m "feat(tactical): aggregate-source variants of ObjectiveRecord sub-builders

Adds BuildObjectiveRecordsFromAggregate + 3 sibling sub-builders
(approach avenues, visible enemy line, movement anchor) that consume
IObservationSource. Existing legacy walks preserved unchanged; new
path is unused until Task 10 wires it into Build with parity window."
```

---

## Task 9: Harness parity test — aggregate vs legacy output

This proves that on a controlled small fixture, the aggregate-source path produces the exact same `ObjectiveRecord[]` as the legacy path would on equivalent inputs.

The complication: the legacy path takes `AIBattle` and walks `BattleUnits.completeunitlist`. The harness can't construct an `AIBattle`. Instead, we exercise sub-builders that consume only `IObservationSource` and use `List<…>` outputs (`BuildApproachAvenueObservationsFromAggregate`, `TryVisibleEnemyLineFromAggregate`, `TryMovementAnchorLineFromAggregate`). For each, we assert the function returns deterministic output for a known input. The full end-to-end `BuildObjectiveRecordsFromAggregate` is covered by the runtime parity window in Task 10.

**Files:**
- Create: `tests/WhiskeyRealism.Tests/ObjectiveRecordsFromAggregateTests.cs`

- [ ] **Step 1: Write the test cases**

```csharp
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tests
{
    internal static class ObjectiveRecordsFromAggregateTests
    {
        public static void Run()
        {
            // Note: full BuildObjectiveRecordsFromAggregate end-to-end is covered
            // by the runtime parity window in production. These tests lock the
            // sub-builder contracts that the aggregate-source variants must honor.
            VisibleEnemyLinePicksStrongestEnemyContact();
            VisibleEnemyLineReturnsFalseWhenNoContact();
            MovementAnchorPicksStrongestOwnWithWaypoint();
            MovementAnchorReturnsFalseWhenAllOwnHaveNoWaypoint();
            ApproachAvenueObservationsExcludeRoutedEnemies();
        }

        private static TacticalUnitObservation MakeUnit(
            int instanceId,
            int alliance,
            float x = 0f,
            float z = 0f,
            float strength = 100f,
            bool hasVisibleEnemy = false,
            float visibleEnemyStrength = 0f,
            bool hasLastWaypoint = false,
            float waypointX = 0f,
            float waypointZ = 0f,
            bool isRouted = false)
        {
            return new TacticalUnitObservation(
                instanceId: instanceId,
                unittyp: 0,
                alliance: alliance,
                isRouted: isRouted,
                worldX: x,
                worldZ: z,
                strength: strength,
                groupOwnInRange: strength,
                groupAiGroup: strength,
                hasCurrentSetObjective: false,
                currentSetObjectiveId: 0,
                objectiveX: 0f,
                objectiveZ: 0f,
                objectiveType: TacticalObjectiveType.UnknownVanillaObjective,
                hasLastWaypoint: hasLastWaypoint,
                lastWaypointX: waypointX,
                lastWaypointZ: waypointZ,
                visibleEnemyStrength: visibleEnemyStrength,
                hasVisibleEnemy: hasVisibleEnemy,
                fatigue01: 0.2f,
                ammo01: 0.9f,
                effectiveCommandLevel: 0);
        }

        private static void VisibleEnemyLinePicksStrongestEnemyContact()
        {
            var agg = new TacticalUnitObservationAggregate();
            agg.LoadForTest(allianceId: 0, units: new[]
            {
                MakeUnit(1, alliance: 0, x: 100f, z: 100f, strength: 200f, hasVisibleEnemy: true,  visibleEnemyStrength: 50f),
                MakeUnit(2, alliance: 0, x: 200f, z: 200f, strength: 300f, hasVisibleEnemy: true,  visibleEnemyStrength: 80f),
                MakeUnit(3, alliance: 0, x: 300f, z: 300f, strength: 100f, hasVisibleEnemy: false, visibleEnemyStrength: 0f),
                MakeUnit(4, alliance: 1, x: 999f, z: 999f, strength: 999f, hasVisibleEnemy: false, visibleEnemyStrength: 0f),
            });
            bool ok = TacticalVisionRuntimeAdapter.TryVisibleEnemyLineFromAggregate_ForTest(
                agg, 0, out var point, out float enemyStrength, out float friendlyStrength);
            TestHarness.AssertTrue(ok, "ok");
            TestHarness.AssertEqual(point.X, 200f, "x");
            TestHarness.AssertEqual(point.Z, 200f, "z");
            TestHarness.AssertEqual(enemyStrength, 80f, "enemyStrength");
            TestHarness.AssertEqual(friendlyStrength, 300f, "friendlyStrength");
            TestHarness.Pass("objective records from aggregate visible enemy line picks strongest");
        }

        private static void VisibleEnemyLineReturnsFalseWhenNoContact()
        {
            var agg = new TacticalUnitObservationAggregate();
            agg.LoadForTest(0, new[] { MakeUnit(1, 0), MakeUnit(2, 1) });
            bool ok = TacticalVisionRuntimeAdapter.TryVisibleEnemyLineFromAggregate_ForTest(
                agg, 0, out _, out _, out _);
            TestHarness.AssertFalse(ok, "ok");
            TestHarness.Pass("objective records from aggregate visible enemy line returns false when no contact");
        }

        private static void MovementAnchorPicksStrongestOwnWithWaypoint()
        {
            var agg = new TacticalUnitObservationAggregate();
            agg.LoadForTest(0, new[]
            {
                MakeUnit(1, 0, x: 0f, z: 0f, strength: 100f, hasLastWaypoint: true, waypointX: 50f, waypointZ: 50f),
                MakeUnit(2, 0, x: 0f, z: 0f, strength: 200f, hasLastWaypoint: true, waypointX: 75f, waypointZ: 75f),
                MakeUnit(3, 0, x: 0f, z: 0f, strength: 300f, hasLastWaypoint: false),
                MakeUnit(4, 1, x: 0f, z: 0f, strength: 999f, hasLastWaypoint: true, waypointX: 1f, waypointZ: 1f),
            });
            bool ok = TacticalVisionRuntimeAdapter.TryMovementAnchorLineFromAggregate_ForTest(
                agg, 0, out var point, out float friendlyStrength);
            TestHarness.AssertTrue(ok, "ok");
            TestHarness.AssertEqual(point.X, 75f, "x");
            TestHarness.AssertEqual(point.Z, 75f, "z");
            TestHarness.AssertEqual(friendlyStrength, 200f, "friendlyStrength");
            TestHarness.Pass("objective records from aggregate movement anchor picks strongest own with waypoint");
        }

        private static void MovementAnchorReturnsFalseWhenAllOwnHaveNoWaypoint()
        {
            var agg = new TacticalUnitObservationAggregate();
            agg.LoadForTest(0, new[] { MakeUnit(1, 0), MakeUnit(2, 0), MakeUnit(3, 1) });
            bool ok = TacticalVisionRuntimeAdapter.TryMovementAnchorLineFromAggregate_ForTest(
                agg, 0, out _, out _);
            TestHarness.AssertFalse(ok, "ok");
            TestHarness.Pass("objective records from aggregate movement anchor returns false when all own have no waypoint");
        }

        private static void ApproachAvenueObservationsExcludeRoutedEnemies()
        {
            var agg = new TacticalUnitObservationAggregate();
            agg.LoadForTest(0, new[]
            {
                MakeUnit(1, 1, x: 100f, z: 100f, strength: 100f, isRouted: false),
                MakeUnit(2, 1, x: 200f, z: 200f, strength: 200f, isRouted: true),
                MakeUnit(3, 0, x: 50f,  z: 50f,  strength: 50f),
            });
            var list = TacticalVisionRuntimeAdapter.BuildApproachAvenueObservationsFromAggregate_ForTest(agg, 0);
            TestHarness.AssertEqual(list.Count, 1, "count");
            TestHarness.AssertEqual(list[0].EnemyStrength, 100f, "strength");
            TestHarness.Pass("objective records from aggregate approach avenue excludes routed enemies");
        }
    }
}
```

- [ ] **Step 2: Add the `_ForTest` accessors**

The new aggregate-source sub-builders are `private static`. The tests need access. Add three `internal static` thin wrappers at the bottom of `TacticalVisionRuntimeAdapter.cs` (before the closing class brace), each forwarding to the real `private static` impl:

```csharp
internal static bool TryVisibleEnemyLineFromAggregate_ForTest(
    IObservationSource source, int allianceId,
    out TacticalMapPoint point, out float enemyStrength, out float friendlyStrength)
{
    return TryVisibleEnemyLineFromAggregate(source, allianceId, out point, out enemyStrength, out friendlyStrength);
}

internal static bool TryMovementAnchorLineFromAggregate_ForTest(
    IObservationSource source, int allianceId,
    out TacticalMapPoint point, out float friendlyStrength)
{
    return TryMovementAnchorLineFromAggregate(source, allianceId, out point, out friendlyStrength);
}

internal static List<TacticalApproachAvenueObservation> BuildApproachAvenueObservationsFromAggregate_ForTest(
    IObservationSource source, int allianceId)
{
    return BuildApproachAvenueObservationsFromAggregate(source, allianceId);
}
```

- [ ] **Step 3: Register the test entry-point**

Edit `tests/WhiskeyRealism.Tests/Program.cs`, add `ObjectiveRecordsFromAggregateTests.Run();` near the other tactical test calls.

- [ ] **Step 4: Run harness, expect new tests pass**

Run: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E "^(PASS|FAIL)" | awk 'BEGIN{p=0;f=0} /^PASS/{p++} /^FAIL/{f++} END{print "PASS="p" FAIL="f}'`

Expected: `PASS=1254 FAIL=0` (1249 + 5 new tests).

- [ ] **Step 5: Commit**

```bash
git add tests/WhiskeyRealism.Tests/ObjectiveRecordsFromAggregateTests.cs \
        tests/WhiskeyRealism.Tests/Program.cs \
        src/WhiskeyRealism/Tactical/Orchestrator/TacticalVisionRuntimeAdapter.cs
git commit -m "test(tactical): aggregate-source sub-builder behavior contracts"
```

---

## Task 10: Wire aggregate + parity window into `TacticalBattleSnapshotBuilder.Build`

**Files:**
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleSnapshotBuilder.cs`

- [ ] **Step 1: Add parity state fields**

Find the field declarations near the top of the `TacticalBattleSnapshotBuilder` class. Add:

```csharp
// Parity window state (Task 10 — single-pass refactor).
// Keyed by (battleSequence, allianceId) so each side gets its own 20-clean
// budget; mismatch on one side does not shut down the other.
private static readonly Dictionary<(int battleSequence, int allianceId), int> _parityComparesRemaining
    = new Dictionary<(int, int), int>();
private static readonly HashSet<(int battleSequence, int allianceId)> _parityMismatchObserved
    = new HashSet<(int, int)>();
private const int ParityCompareBudget = 20;
```

If `ValueTuple` is not available on netstandard2.1 by default, add `using System;` and consider a simple `struct ParityKey { int BattleSequence; int AllianceId; }` instead with `Equals` / `GetHashCode`. Verify by trying to build first.

- [ ] **Step 2: Add `ClearParityState` helper**

Below the field declarations:

```csharp
/// <summary>
/// Called from TacticalBattleCoordinator.ResetRuntimeTickState on battle
/// end so the next battle's parity window starts fresh. Public so the
/// coordinator can reach it across files.
/// </summary>
public static void ClearParityState()
{
    _parityComparesRemaining.Clear();
    _parityMismatchObserved.Clear();
}
```

- [ ] **Step 3: Replace `Build` body with aggregate capture + parity branch**

Locate the current `Build` method (`TacticalBattleSnapshotBuilder.cs:119`). Replace the body inside the try block. New body:

```csharp
public static TacticalBattleRuntimeSnapshot Build(
    AIBattle battle,
    int allianceId,
    TacticalBattleStateSignature signatureAtBuild,
    float buildBattleHours)
{
    try
    {
        ArmyEvidenceBuilder.Bundle bundle;
        using (TelemetryPerf.Scope("tactical.snapshot-build.evidence-bundle", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
        {
            bundle = ArmyEvidenceBuilder.Build(battle, allianceId);
        }

        ObjectiveRecord[] objectives;
        using (TelemetryPerf.Scope("tactical.snapshot-build.objective-records", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
        {
            objectives = BuildObjectiveRecordsWithParityWindow(battle, allianceId);
        }

        var commandTree = CommandTreeRuntime.Snapshot(allianceId);
        IReadOnlyList<DirectChildSnapshot> directChildren = DirectChildDiscovery.Snapshot(battle);

        return new TacticalBattleRuntimeSnapshot(
            signatureAtBuild,
            buildBattleHours,
            bundle.OwnEvidence,
            bundle.EnemyVisible,
            bundle.OwnMainEffortStrength,
            bundle.OwnArmyMorale,
            bundle.OwnReservesCommittedFraction,
            bundle.ReinforcementsArrivingDelta,
            objectives,
            commandTree,
            directChildren,
            bundle.OwnAvgFatigue01,
            bundle.OwnAvgAmmo01,
            bundle.NearestReinforcementHours,
            bundle.NearestReinforcementStrength);
    }
    catch (Exception e)
    {
        OnceLog.Warning("tactical-orch:snapshot-builder",
            "[TacticalOrchestrator] TacticalBattleSnapshotBuilder.Build degraded for alliance=" + allianceId
            + ": " + e.GetType().Name + " " + e.Message);
        return TacticalBattleRuntimeSnapshot.Empty;
    }
}

private static ObjectiveRecord[] BuildObjectiveRecordsWithParityWindow(AIBattle battle, int allianceId)
{
    var plugin = Plugin.Instance;
    bool flagOn = plugin != null
        && Plugin.EnableSinglePassObjectiveRecords != null
        && Plugin.EnableSinglePassObjectiveRecords.Value;
    if (!flagOn)
    {
        // Rollback path — legacy only, original measurement.
        ObjectiveRecord[] legacyOnly;
        using (TelemetryPerf.Scope("tactical.snapshot-build.objective-records.legacy", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
        {
            legacyOnly = TacticalVisionRuntimeAdapter.BuildObjectiveRecordsFromBattle(battle, allianceId);
        }
        return legacyOnly;
    }

    int battleSeq = TacticalBattleCoordinator.BattleSequenceForParity;
    var key = (battleSeq, allianceId);

    bool inWindow = false;
    if (!_parityComparesRemaining.TryGetValue(key, out int remaining)) remaining = ParityCompareBudget;
    if (remaining > 0) inWindow = true;
    if (_parityMismatchObserved.Contains(key)) inWindow = true;

    IObservationSource source;
    using (TelemetryPerf.Scope("tactical.snapshot-build.aggregate-capture", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
    {
        source = TacticalUnitObservationAggregate.Shared.Capture(allianceId);
    }

    if (!inWindow)
    {
        ObjectiveRecord[] aggregateOnly;
        using (TelemetryPerf.Scope("tactical.snapshot-build.objective-records.aggregate", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
        {
            aggregateOnly = TacticalVisionRuntimeAdapter.BuildObjectiveRecordsFromAggregate(source, battle, allianceId);
        }
        return aggregateOnly;
    }

    // Parity-window: run both, compare, emit mismatch on diff.
    ObjectiveRecord[] legacyResult;
    using (TelemetryPerf.Scope("tactical.snapshot-build.objective-records.legacy", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
    {
        legacyResult = TacticalVisionRuntimeAdapter.BuildObjectiveRecordsFromBattle(battle, allianceId);
    }
    ObjectiveRecord[] aggregateResult;
    using (TelemetryPerf.Scope("tactical.snapshot-build.objective-records.aggregate", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
    {
        aggregateResult = TacticalVisionRuntimeAdapter.BuildObjectiveRecordsFromAggregate(source, battle, allianceId);
    }

    if (!ObjectiveRecordsEqual(legacyResult, aggregateResult))
    {
        _parityMismatchObserved.Add(key);
        EmitParityMismatch(battleSeq, allianceId, legacyResult, aggregateResult);
        return legacyResult;  // safety: never publish drifted output
    }

    _parityComparesRemaining[key] = remaining - 1;
    return aggregateResult;
}

private static bool ObjectiveRecordsEqual(ObjectiveRecord[] a, ObjectiveRecord[] b)
{
    if (ReferenceEquals(a, b)) return true;
    if (a == null || b == null) return false;
    if (a.Length != b.Length) return false;
    for (int i = 0; i < a.Length; i++)
    {
        var x = a[i];
        var y = b[i];
        if (x.ObjectiveId != y.ObjectiveId) return false;
        if (x.Type != y.Type) return false;
        if (x.Point.X != y.Point.X || x.Point.Z != y.Point.Z) return false;
        if (x.EnemyStrengthSum != y.EnemyStrengthSum) return false;
        if (x.FriendlyStrengthSum != y.FriendlyStrengthSum) return false;
        // Note: float compare is exact here — both paths read the same Regiment
        // fields, so identical floats are expected. If a future change introduces
        // computed-then-compared floats, switch to small-epsilon compare.
    }
    return true;
}

private static void EmitParityMismatch(int battleSeq, int allianceId, ObjectiveRecord[] legacy, ObjectiveRecord[] aggregate)
{
    try
    {
        TelemetryRouter.Emit(
            TelemetryLayer.Tactical,
            TelemetryCategory.Gate,
            "TacticalObjectiveRecordsParityMismatch",
            TelemetrySeverity.Warning,
            ev => ev
                .WithSide(allianceId)
                .WithDecision("mismatch", "objective-records", "battleSeq=" + battleSeq + "|alliance=" + allianceId)
                .WithField("legacyCount", legacy?.Length ?? -1)
                .WithField("aggregateCount", aggregate?.Length ?? -1)
                .WithField("battleSequence", battleSeq)
                .WithField("allianceId", allianceId));
    }
    catch { }
}
```

The reference `TacticalBattleCoordinator.BattleSequenceForParity` requires a small accessor on `TacticalBattleCoordinator` — add it in Task 11.

- [ ] **Step 4: Build**

Run: `./build.sh 2>&1 | tail -10`

Likely failure: `BattleSequenceForParity` doesn't exist yet. Skip to Task 11 to add the accessor, then return here.

If `ObjectiveRecord` fields don't match exactly the names I used (`ObjectiveId`, `Type`, `Point.X`, `Point.Z`, `EnemyStrengthSum`, `FriendlyStrengthSum`), inspect the actual `ObjectiveRecord` definition and update `ObjectiveRecordsEqual` to use the real names. The contract is: compare every field that would meaningfully differ.

- [ ] **Step 5: (Deferred to after Task 11) Run harness**

After Task 11 lands the accessor and this builds: run harness.

- [ ] **Step 6: Commit (with Task 11)**

Defer commit until Task 11 lands so the build is clean. Stage these files but don't commit yet.

---

## Task 11: Expose `BattleSequenceForParity` + clear parity state on battle end

**Files:**
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinator.cs` (pure partial — see existing `partial class` declarations in `TacticalBattleCoordinatorRuntime.cs`)
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs`

- [ ] **Step 1: Find an existing public/internal accessor pattern on `TacticalBattleCoordinator`**

Run: `grep -nE 'public static |internal static ' src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinator.cs src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs | head -20`

Look for a small public/internal accessor pattern (e.g., something like `GetSideOrchestrator`) — the new accessor mirrors that style.

- [ ] **Step 2: Add the accessor in the pure partial**

Find the partial class declaration in `TacticalBattleCoordinator.cs`. Add:

```csharp
/// <summary>
/// Battle sequence integer used as the parity-window key in
/// TacticalBattleSnapshotBuilder. Increments on each OnBattleStart;
/// reset to 0 in ClearForFailure.
/// </summary>
public static int BattleSequenceForParity
{
    get { return _battleSequence; }
}
```

(Field `_battleSequence` already exists in `TacticalBattleCoordinatorRuntime.cs:60`. Confirm with grep before adding.)

- [ ] **Step 3: Wire ResetRuntimeTickState to clear aggregate + parity state**

In `TacticalBattleCoordinatorRuntime.cs`, find `ResetRuntimeTickState`. Inside the try block, add:

```csharp
try { TacticalUnitObservationAggregate.Shared.ClearForBattleEnd(); } catch { }
try { TacticalBattleSnapshotBuilder.ClearParityState(); } catch { }
```

Place these alongside the other `try { … } catch { }` resets at the end of the block, before the per-side `for (int i = 0; i < 2; i++)` loop.

- [ ] **Step 4: Build**

Run: `./build.sh 2>&1 | tail -10`

Iterate on compile errors until clean. Common issues:
- `ObjectiveRecord` field names mismatch in `ObjectiveRecordsEqual` — update to actual names.
- Tuple `(int, int)` not available — fall back to a `ParityKey` struct.
- `BuildObjectiveRecordsFromBattle` made `internal` rather than `private` — confirm in the file, no change needed.

- [ ] **Step 5: Run harness**

Run: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E "^(PASS|FAIL)" | awk 'BEGIN{p=0;f=0} /^PASS/{p++} /^FAIL/{f++} END{print "PASS="p" FAIL="f}'`

Expected: `PASS=1254 FAIL=0`.

- [ ] **Step 6: Commit (Task 10 + Task 11 together)**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleSnapshotBuilder.cs \
        src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinator.cs \
        src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs
git commit -m "feat(tactical): wire single-pass aggregate path with parity window

Build now branches on EnableSinglePassObjectiveRecords:
  - flag OFF: legacy path only, original measurement
  - flag ON, key (battleSeq, alliance) in window: run both, compare,
    emit TacticalObjectiveRecordsParityMismatch on diff, return legacy
  - flag ON, key cleared (20 clean compares): aggregate-only

Parity state lives in TacticalBattleSnapshotBuilder; coordinator clears
it via ClearParityState() in ResetRuntimeTickState, alongside the
aggregate pool clear. BattleSequenceForParity exposes the existing
_battleSequence counter.

Telemetry scopes: .aggregate, .legacy, .aggregate-capture, plus the
outer scope continues to measure end-to-end. Success metric is the
.aggregate scope — parity dual-run during the window doesn't poison
its p99."
```

---

## Task 12: Deploy + verify hash

**Files:** none (build artifact + game install).

- [ ] **Step 1: Confirm GTCW is closed**

Run: `ls "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll" 2>&1`

Then attempt a probe write: `touch "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll" 2>&1`

If `Invalid argument` returns, GTCW is running — ask user to close before continuing.

- [ ] **Step 2: Build clean**

Run: `./build.sh 2>&1 | tail -5`

Expected: `0 Error(s)`, `Built plugin: …dist/WhiskeyRealism.dll`.

- [ ] **Step 3: Deploy and hash-verify**

Run:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/" && \
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll" && \
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: two identical sizes, two identical sha256 hashes.

- [ ] **Step 4: Final harness check**

Run: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E "^(PASS|FAIL)" | awk 'BEGIN{p=0;f=0} /^PASS/{p++} /^FAIL/{f++} END{print "PASS="p" FAIL="f}'`

Expected: `PASS=1254 FAIL=0`.

- [ ] **Step 5: Note the hash for handoff doc**

Record the deployed sha256 hash. It goes into `docs/handoff.md` after smoke confirms behavior.

No commit at this task — build artifacts are gitignored.

---

## Task 13: Smoke + measurement + parity verification

User-driven. Document the procedure here so the next session can re-run.

- [ ] **Step 1: User smokes**

User direction: load the same 26k-unit small battle that produced session `20260519-173901`. Confirm `Logging Profile = TacticalTuning` in `<GTCW>/BepInEx/config/dev.kyle.whiskey-realism.cfg`. Play at 20x compression for ~3 minutes (matching baseline session length). Close GTCW.

- [ ] **Step 2: Identify the session directory**

Run: `ls -t "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/WhiskeyRealism/tuning-logs/" | head -3`

The newest directory is the smoke session. Confirm its name includes the first 12 chars of the deployed DLL sha256.

- [ ] **Step 3: Pull metrics**

Run this Python in Bash:

```bash
SESSION="/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/WhiskeyRealism/tuning-logs/<SESSION_DIR>"
python3 <<EOF
import json, os
from collections import defaultdict
scopes = defaultdict(list)
with open(os.path.join("$SESSION", "performance.jsonl")) as f:
    for line in f:
        try:
            row = json.loads(line)
            scope = row.get("fields", {}).get("scope") or row.get("scope")
            dur = row.get("durationMs")
            if scope and dur is not None: scopes[scope].append(dur)
        except: pass

def pct(xs, p):
    xs = sorted(xs)
    if not xs: return 0
    k = int(len(xs) * p / 100.0)
    if k >= len(xs): k = len(xs) - 1
    return xs[k]

print(f"{'scope':<55} {'n':>5} {'p50':>8} {'p95':>8} {'p99':>8} {'max':>8} {'sum':>10}")
print("-" * 110)
for scope, xs in sorted(scopes.items(), key=lambda kv: -sum(kv[1])):
    print(f"{scope:<55} {len(xs):>5} {pct(xs,50):>8.2f} {pct(xs,95):>8.2f} {pct(xs,99):>8.2f} {max(xs):>8.2f} {sum(xs):>10.1f}")

# Parity mismatch count
mismatches = 0
with open(os.path.join("$SESSION", "tactical.jsonl")) as f:
    for line in f:
        try:
            row = json.loads(line)
            if row.get("event") == "TacticalObjectiveRecordsParityMismatch": mismatches += 1
        except: pass
print(f"\\nParity mismatches: {mismatches}")
EOF
```

- [ ] **Step 4: Compare against success criteria (spec §11)**

| Metric | Target | Actual |
|---|---|---|
| `tactical.snapshot-build.objective-records.aggregate` p99 | ≤ 3 ms | _record_ |
| `tactical.snapshot-build.objective-records` p99 (outer) | ≤ 7 ms post-window | _record_ |
| `tactical.snapshot-build.tick-cycle` p99 | ≤ 7 ms | _record_ |
| `tactical.orchestrator-tick` p99 | ≤ 5 ms | _record_ |
| Parity mismatches | 0 | _record_ |

- [ ] **Step 5: Outcome branch**

- **All targets met + 0 mismatches:** proceed to Task 14.
- **Mismatches > 0:** read the `TacticalObjectiveRecordsParityMismatch` records (full diff in fields). Identify the missing aggregate field (or off-by-one filter). Re-open Task 1 (add field) → Task 7 (capture field) → Task 8 (consume in aggregate sub-builder). Re-deploy, re-smoke. Do not proceed.
- **Targets not met:** the aggregate path is correct but slower than expected. Investigate which inner scope dominates. Most likely culprits: visibility-walk allocation churn (consider caching `enemyin*rangereg` length) or unbounded list growth. File as follow-up slice.

- [ ] **Step 6: Commit smoke notes (no code change)**

Append a brief smoke result to `docs/handoff.md` after the existing "What just shipped" section. No code change in this step.

---

## Task 14: Document + closeout

- [ ] **Step 1: Update `docs/telemetry.md`**

In the "Hot-path performance scopes" table, add four new rows alphabetically:

```
| `tactical.snapshot-build.aggregate-capture` | 5.0 ms | `TacticalUnitObservationAggregate.Capture` single completeunitlist walk |
| `tactical.snapshot-build.objective-records.aggregate` | 5.0 ms | aggregate-path `BuildObjectiveRecordsFromAggregate` — steady-state success metric |
| `tactical.snapshot-build.objective-records.legacy` | 5.0 ms | legacy-path `BuildObjectiveRecordsFromBattle` — fires during parity window or when `EnableSinglePassObjectiveRecords=false` |
```

Below the table, add a one-paragraph note documenting `TacticalObjectiveRecordsParityMismatch` (Gate category, `legacyCount`/`aggregateCount`/`battleSequence`/`allianceId` fields, fires when aggregate path diverges from legacy during the 20-build parity window per `(battleSequence, allianceId)`).

- [ ] **Step 2: Update `docs/tactical-orchestrator.md`**

Add a section "Single-pass ObjectiveRecord aggregation (2026-05-19)" describing:
- The `TacticalUnitObservationAggregate` shared instance and its lifecycle (per-battle, cleared in `ResetRuntimeTickState`).
- The parity window contract (20 clean compares per `(battleSequence, allianceId)`, mismatch keeps that key on legacy for the battle).
- The `EnableSinglePassObjectiveRecords` rollback flag.
- The smoke measurement table from Task 13.

- [ ] **Step 3: Update `docs/handoff.md`**

Refresh the "What just shipped" / "Active workstream" sections with:
- Deployed DLL sha256 from Task 12.
- Harness count post-this-slice.
- Smoke results from Task 13.
- Mark this slice as shipped.

- [ ] **Step 4: Archive spec + plan**

```bash
git mv docs/superpowers/specs/2026-05-19-tactical-objective-records-single-pass-design.md docs/superpowers/specs/archive/
git mv docs/superpowers/plans/2026-05-19-tactical-objective-records-single-pass-implementation.md docs/superpowers/plans/archive/
```

Update `docs/superpowers/specs/archive/README.md` and `docs/superpowers/plans/archive/README.md` with one-line entries pointing to the archived files.

- [ ] **Step 5: Update `MEMORY.md`**

Append a one-line index entry pointing to the spec archive path and a short hook describing the slice outcome.

- [ ] **Step 6: Commit closeout**

```bash
git add docs/
git commit -m "docs: ship single-pass ObjectiveRecord refactor — archive spec/plan, update living docs

Deployed sha256 <hash> with TacticalUnitObservationAggregate +
EnableSinglePassObjectiveRecords default ON. Smoke results:
<paste measurement table summary>. Parity mismatches: 0.

Slice 1 of hot-path optimization complete. ArmyEvidenceBuilder.Build
follow-up deferred per spec §12 — re-evaluate from post-fix metrics."
```

- [ ] **Step 7: Hand off the branch**

```
git log --oneline feat/hotpath-measurement
```

Branch is ready for merge into main. Use the `superpowers:finishing-a-development-branch` skill to merge / PR / cleanup.

---

## Rollback

If smoke reveals a regression that the parity window didn't catch (e.g., performance regression below targets or behavioral drift the parity comparator missed):

1. Edit `<GTCW>/BepInEx/config/dev.kyle.whiskey-realism.cfg` → set `Enable Single-Pass Objective Records = false`.
2. Restart GTCW. Behavior reverts to the legacy path. The aggregate code remains in the binary but is unused; `tactical.snapshot-build.objective-records.legacy` becomes the dominant scope; `.aggregate` drops to zero samples.
3. File a follow-up slice to diagnose the regression on data captured post-rollback.

If the regression is critical and a code revert is preferable:

```bash
git revert <Task-10/11-merge-commit>
./build.sh && cp dist/WhiskeyRealism.dll "<GTCW>/BepInEx/plugins/"
```

The aggregate types remain (additive, no Unity references in pure part) but the wiring is removed.

---

## Plan self-review

**Spec coverage:**
- §1 Motivation → context in plan header.
- §2 Goals → Task 13 §11 success criteria table.
- §3 Architecture → file map + Tasks 1–3, 7, 10.
- §4 Aggregate shape → Task 1 struct definition.
- §4a Field audit → Task 7 Step 1.
- §5 Aggregate impl → Task 3 (pure) + Task 7 (runtime).
- §6 Sub-builder transformation → Task 8.
- §7 Parity verification → harness (Task 9) + runtime (Task 10).
- §7a Telemetry scope structure → Task 10 Step 3 scopes.
- §8 Rollback flag → Task 6 + Rollback section.
- §9 Files to modify → file map at top.
- §10 Testing → Tasks 4, 9, 12, 13.
- §11 Success criteria → Task 13 Step 4 table.
- §12 Known follow-ups → preserved in spec, not re-listed here.
- §13 AGENTS.md compliance → Constraints section + per-task try/catch directives.
- §14 Adversarial review responses → addressed in the corresponding tasks they apply to.

**Placeholder scan:** searched the plan text for "TBD", "TODO", "fill in", "appropriate error handling", "similar to". None found. One `_record_` placeholder in Task 13 Step 4 is the user filling in measurement — intentional, not a planning placeholder.

**Type consistency:**
- `TacticalUnitObservation` field set in Task 1 is the same field set referenced in Task 4 test helper, Task 7 `CaptureUnit`, Task 8 sub-builder reads, Task 9 test helper.
- `IObservationSource` members (`Count`, `CapturedForAlliance`, `AllUnits`, `AlliedIndices`, `EnemyIndices`) used consistently in Task 3, 7, 8, 9, 10.
- `TacticalUnitObservationAggregate.Shared.Capture(int)` / `.LoadForTest(int, IReadOnlyList<…>)` / `.ClearForBattleEnd()` member shape stable across Tasks 3, 4, 7, 11.
- Parity key `(int battleSequence, int allianceId)` consistent in Task 10 across declaration, lookup, mismatch tracking, and clear.
- Telemetry scope names spelled identically in Task 10 Step 3 and Task 14 docs update.

Plan ready for execution.
