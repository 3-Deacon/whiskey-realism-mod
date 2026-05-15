# Tactical Operations Ledger Command System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Status as of 2026-05-14:** Implementation is merged to `main`, built,
> deployed, and hash-verified through the tactical completion DLL
> `f2e7705b96c55ea371ca08a3a56d28ebf324bfc114618c184ccba375d17ee1f1`
> (1027072 bytes; 893 PASS). The current operational source of truth is now
> [`docs/tactical-operations-ledger.md`](../../tactical-operations-ledger.md);
> this plan remains active only for Active smoke and final archive closeout. Do
> not use unchecked historical task boxes below to infer missing code without
> checking shipped source and the living docs.

**Goal:** Build the full tactical operations ledger command system so AI armies scout, evaluate objectives, assign corps/division/brigade-like command nodes, commit to operations, keep reserves, recover idle/stuck commands, and release with `Tactical Commander Mode = Active` as the default after smoke verification.

**Architecture:** Add a pure decision core under `Tactical/Operations/` and small runtime adapters under `Tactical/Orchestrator/` / `Patches/`. Harmony patches read immutable orchestrator snapshots and write vanilla state only through bounded, gated surfaces; authoritative Whiskey tactical state is updated only by the orchestrator tick.

**Tech Stack:** C# netstandard2.1 BepInEx plugin, HarmonyX patches, existing console harness `tests/WhiskeyRealism.Tests`, Grand Tactician vanilla anchors from `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

---

## Scope And Sequencing

This plan implements the full system, but preserves the spec's required safety sequence:

1. Anchor verification and typed read APIs.
2. Pure confidence, vision, ledger, operation, task, monitor, and executor decisions.
3. Runtime adapters and MonitorOnly full-loop proof.
4. Active executor writes, smoke, deploy, and release default.

Do not skip the MonitorOnly smoke checkpoint. The released/default config is `Active`; `MonitorOnly` is a pre-release proof mode only.

## Existing Integration Contracts

These constraints are part of the implementation, not advisory notes:

- `BattleMacroStrategyPatch` remains the only Whiskey writer for vanilla `macroai` around `AIBattle.CheckGlobalAIStrategy()`. The ledger may provide the selected operation and pressure, but it must not add a second macro writer.
- Vanilla `AIBattle.AssignReserves()` continues to run. Whiskey observes its output, protects ledger-owned reserves through #57/#59 consumers, and logs reserve-policy drift. Do not Prefix-block or replace `AssignReserves()` in this plan.
- `EchelonOrchestrator` in `src/WhiskeyRealism/Tactical/Orchestrator/EchelonOrchestrator.cs` remains the hierarchy base. This plan extends the shipped stack; it does not introduce a new `EchelonCommandOrchestrator` class.
- Authoritative operations ledger state, command task state, `lastOrderIssuedAt`, `lastProgressTime`, and stuck classifications are written only during the orchestrator tick. Patches may keep patch-local throttles and recent-order guards, but patches do not mutate the ledger.
- Strategic state used by battle logic must be exposed as a read-only snapshot object. Battle patches and runtime adapters cannot write CIC, theater commander, campaign ledger, or strategic sidecar state.
- Existing local dirty code changes in this checkout are outside this plan unless the implementer explicitly takes ownership of them in the relevant task.

## Release Criteria

The implementation is complete only when all of these are true:

- console harness passes.
- `./build.sh` succeeds.
- `dist/WhiskeyRealism.dll` is deployed to the BepInEx plugin folder and the deployed SHA-256 matches `dist/WhiskeyRealism.dll`.
- MonitorOnly smoke proves the full vision/ledger/task/monitor loop without vanilla writes.
- Active smoke proves bounded writes, no repeated exceptions, no player-subordinate retasking, and no illegal idle commands left without a ledger reason.
- config release/default is `Tactical Commander Mode = Active`.

## File Map

Create:

- `src/WhiskeyRealism/Tactical/Operations/TacticalCommanderMode.cs`
- `src/WhiskeyRealism/Tactical/Operations/TacticalObjectiveContracts.cs`
- `src/WhiskeyRealism/Tactical/Operations/TacticalObjectiveSourceModel.cs`
- `src/WhiskeyRealism/Tactical/Operations/TacticalVisionModel.cs`
- `src/WhiskeyRealism/Tactical/Operations/TacticalOperationsLedgerModel.cs`
- `src/WhiskeyRealism/Tactical/Operations/TacticalOperationSelectionModel.cs`
- `src/WhiskeyRealism/Tactical/Operations/CommandNodeOperationalState.cs`
- `src/WhiskeyRealism/Tactical/Operations/CommandNodeTaskPlanner.cs`
- `src/WhiskeyRealism/Tactical/Operations/CommandPostureExecutor.cs`
- `src/WhiskeyRealism/Tactical/Operations/TacticalCommandMonitor.cs`
- `src/WhiskeyRealism/Tactical/Operations/TacticalOperationsTelemetry.cs`
- `src/WhiskeyRealism/Tactical/Orchestrator/TacticalVisionRuntimeAdapter.cs`
- `src/WhiskeyRealism/Tactical/Orchestrator/TacticalOperationsLedgerRuntime.cs`
- `src/WhiskeyRealism/Tactical/Orchestrator/CommandNodeOperationsRuntime.cs`
- `src/WhiskeyRealism/Tactical/Orchestrator/StrategicBattleIntentSnapshot.cs`
- `src/WhiskeyRealism/Patches/BattleCommandPostureExecutorPatch.cs`
- `docs/tactical-operations-ledger.md`

Modify:

- `src/WhiskeyRealism/Plugin.cs`
- `src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs`
- `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleOrchestrator.cs`
- `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinator.cs`
- `src/WhiskeyRealism/Patches/BattleMacroStrategyPatch.cs`
- `src/WhiskeyRealism/Patches/BattleGroupStancePatch.cs`
- `src/WhiskeyRealism/Patches/BattleReserveDoctrinePatch.cs`
- `src/WhiskeyRealism/Patches/BattleReserveCommitGatePatch.cs`
- `src/WhiskeyRealism/Patches/B8CheckLineFallbacksObserverPatch.cs`
- `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs`
- `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- `tests/WhiskeyRealism.Tests/Program.cs`
- `docs/findings.md`
- `docs/patch-catalog.md`
- `docs/tactical-orchestrator.md`
- `docs/handoff.md`

## Task 1: Anchor Verification And Objective Source Contracts

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Operations/TacticalObjectiveContracts.cs`
- Create: `src/WhiskeyRealism/Tactical/Operations/TacticalObjectiveSourceModel.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`
- Modify: `docs/findings.md`

- [ ] **Step 1: Verify vanilla objective/terrain anchors**

Run:

```bash
rg -n "objectivechain|currentsetobjective|BattleMonument|Victory|bridge|ford|road|blockedcrossings|TerrainShape|terrainid|GetTerrain" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
```

Expected:

- Existing anchors for `objectivechain` and `Regiment.currentsetobjective` are found.
- Any bridge/ford/road/choke claim is recorded as confirmed only if an explicit class/field/method exists and can be read without mutation.
- If no clean bridge/ford/road API exists, record that the first implementation must classify those as generic positions, not typed POIs.

- [ ] **Step 2: Append anchor results to `docs/findings.md`**

Add a short section:

```markdown
## Tactical Operations Objective Anchors

- `AIBattle.objectivechain`: confirmed readable through existing #35 observer path; initial generic objective source only.
- `Regiment.currentsetobjective`: confirmed readable through existing #35 reflection path; maps command groups to vanilla objective references when present.
- Bridge/ford/road/choke enumeration: [record exact confirmed anchor or "not found in current decompile"]. Do not use typed POI scoring without a confirmed anchor.
- Terrain safety: use existing #58/#60 terrain/deployment sampling until a broader terrain source is verified.
```

- [ ] **Step 3: Add pure objective contracts**

Create `src/WhiskeyRealism/Tactical/Operations/TacticalObjectiveContracts.cs`:

```csharp
using System;

namespace WhiskeyRealism.Tactical.Operations
{
    public enum TacticalObjectiveType
    {
        UnknownVanillaObjective = 0,
        VictoryPoint = 1,
        Bridge = 2,
        Ford = 3,
        RoadJunction = 4,
        Town = 5,
        Ridge = 6,
        ChokePoint = 7,
        EnemyLine = 8,
        FriendlyLine = 9,
        FallbackLine = 10,
        StagingArea = 11,
    }

    public enum TacticalObjectiveSource
    {
        Unknown = 0,
        ObjectiveChain = 1,
        CurrentSetObjective = 2,
        VisibleEnemyLine = 3,
        FriendlyLineShape = 4,
        TerrainSample = 5,
        VerifiedSceneObject = 6,
    }

    public readonly struct TacticalMapPoint
    {
        public TacticalMapPoint(float x, float z)
        {
            X = Sanitize(x);
            Z = Sanitize(z);
        }

        public float X { get; }
        public float Z { get; }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }

    public readonly struct ObjectiveObservationInput
    {
        public ObjectiveObservationInput(
            string objectiveId,
            TacticalObjectiveType type,
            TacticalObjectiveSource source,
            TacticalMapPoint location,
            float sourceConfidence,
            float value,
            bool typeAnchorVerified)
        {
            ObjectiveId = string.IsNullOrWhiteSpace(objectiveId) ? "objective-unknown" : objectiveId;
            Type = type;
            Source = source;
            Location = location;
            SourceConfidence = Clamp01(sourceConfidence);
            Value = Math.Max(0f, Sanitize(value));
            TypeAnchorVerified = typeAnchorVerified;
        }

        public string ObjectiveId { get; }
        public TacticalObjectiveType Type { get; }
        public TacticalObjectiveSource Source { get; }
        public TacticalMapPoint Location { get; }
        public float SourceConfidence { get; }
        public float Value { get; }
        public bool TypeAnchorVerified { get; }

        private static float Clamp01(float value)
        {
            value = Sanitize(value);
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }
}
```

- [ ] **Step 4: Add pure objective source model**

Create `src/WhiskeyRealism/Tactical/Operations/TacticalObjectiveSourceModel.cs`:

```csharp
using System;

namespace WhiskeyRealism.Tactical.Operations
{
    public static class TacticalObjectiveSourceModel
    {
        public static ObjectiveObservationInput Normalize(ObjectiveObservationInput input)
        {
            TacticalObjectiveType type = input.Type;
            float value = input.Value;
            bool verified = input.TypeAnchorVerified;

            if (!verified && IsSpecificPoi(type))
            {
                type = TacticalObjectiveType.UnknownVanillaObjective;
                value = Math.Min(value, 0.35f);
            }

            return new ObjectiveObservationInput(
                input.ObjectiveId,
                type,
                input.Source,
                input.Location,
                input.SourceConfidence,
                value,
                verified);
        }

        public static bool CanDriveTypedOperationScoring(ObjectiveObservationInput input)
        {
            if (!input.TypeAnchorVerified) return false;
            return IsSpecificPoi(input.Type) || input.Type == TacticalObjectiveType.EnemyLine || input.Type == TacticalObjectiveType.FriendlyLine;
        }

        private static bool IsSpecificPoi(TacticalObjectiveType type)
        {
            switch (type)
            {
                case TacticalObjectiveType.VictoryPoint:
                case TacticalObjectiveType.Bridge:
                case TacticalObjectiveType.Ford:
                case TacticalObjectiveType.RoadJunction:
                case TacticalObjectiveType.Town:
                case TacticalObjectiveType.Ridge:
                case TacticalObjectiveType.ChokePoint:
                    return true;
                default:
                    return false;
            }
        }
    }
}
```

- [ ] **Step 5: Add harness includes**

Add to `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` near other tactical includes:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Operations\TacticalObjectiveContracts.cs" Link="Operations\TacticalObjectiveContracts.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Operations\TacticalObjectiveSourceModel.cs" Link="Operations\TacticalObjectiveSourceModel.cs" />
```

- [ ] **Step 6: Add failing tests**

Add these test registrations to `Program.cs`:

```csharp
("tactical objective unverified bridge downgrades to generic", TacticalObjectiveUnverifiedBridgeDowngrades),
("tactical objective verified bridge drives typed scoring", TacticalObjectiveVerifiedBridgeDrivesTypedScoring),
```

Add test methods:

```csharp
static void TacticalObjectiveUnverifiedBridgeDowngrades()
{
    var input = new ObjectiveObservationInput(
        "bridge-a",
        TacticalObjectiveType.Bridge,
        TacticalObjectiveSource.ObjectiveChain,
        new TacticalMapPoint(10f, 20f),
        0.8f,
        1.0f,
        typeAnchorVerified: false);

    var result = TacticalObjectiveSourceModel.Normalize(input);

    AssertEqual(TacticalObjectiveType.UnknownVanillaObjective, result.Type, "type");
    AssertTrue(result.Value <= 0.35f, "unverified POI value capped");
    AssertFalse(TacticalObjectiveSourceModel.CanDriveTypedOperationScoring(result), "typed scoring");
}

static void TacticalObjectiveVerifiedBridgeDrivesTypedScoring()
{
    var input = new ObjectiveObservationInput(
        "bridge-a",
        TacticalObjectiveType.Bridge,
        TacticalObjectiveSource.VerifiedSceneObject,
        new TacticalMapPoint(10f, 20f),
        0.9f,
        1.0f,
        typeAnchorVerified: true);

    var result = TacticalObjectiveSourceModel.Normalize(input);

    AssertEqual(TacticalObjectiveType.Bridge, result.Type, "type");
    AssertTrue(TacticalObjectiveSourceModel.CanDriveTypedOperationScoring(result), "typed scoring");
}
```

- [ ] **Step 7: Run tests and commit**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass.

Commit:

```bash
git add src/WhiskeyRealism/Tactical/Operations/TacticalObjectiveContracts.cs src/WhiskeyRealism/Tactical/Operations/TacticalObjectiveSourceModel.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs docs/findings.md
git commit -m "feat(tactical): add objective source contracts"
```

## Task 2: Tactical Commander Mode Config And Migration

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Operations/TacticalCommanderMode.cs`
- Modify: `src/WhiskeyRealism/Plugin.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add mode contract**

Create `src/WhiskeyRealism/Tactical/Operations/TacticalCommanderMode.cs`:

```csharp
namespace WhiskeyRealism.Tactical.Operations
{
    public enum TacticalCommanderMode
    {
        Off = 0,
        MonitorOnly = 1,
        Active = 2,
    }

    public static class TacticalCommanderModePolicy
    {
        public static TacticalCommanderMode Parse(string raw, TacticalCommanderMode fallback)
        {
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            switch (raw.Trim().ToLowerInvariant())
            {
                case "off": return TacticalCommanderMode.Off;
                case "monitoronly":
                case "monitor-only":
                case "monitor only": return TacticalCommanderMode.MonitorOnly;
                case "active": return TacticalCommanderMode.Active;
                default: return fallback;
            }
        }

        public static bool RunsLedger(TacticalCommanderMode mode) => mode == TacticalCommanderMode.MonitorOnly || mode == TacticalCommanderMode.Active;
        public static bool AllowsWrites(TacticalCommanderMode mode) => mode == TacticalCommanderMode.Active;
    }
}
```

- [ ] **Step 2: Add test registrations**

Add:

```csharp
("tactical commander mode active allows writes", TacticalCommanderModeActiveAllowsWrites),
("tactical commander mode monitor runs ledger without writes", TacticalCommanderModeMonitorRunsNoWrites),
```

Add:

```csharp
static void TacticalCommanderModeActiveAllowsWrites()
{
    var mode = TacticalCommanderModePolicy.Parse("Active", TacticalCommanderMode.MonitorOnly);
    AssertEqual(TacticalCommanderMode.Active, mode, "mode");
    AssertTrue(TacticalCommanderModePolicy.RunsLedger(mode), "ledger");
    AssertTrue(TacticalCommanderModePolicy.AllowsWrites(mode), "writes");
}

static void TacticalCommanderModeMonitorRunsNoWrites()
{
    var mode = TacticalCommanderModePolicy.Parse("MonitorOnly", TacticalCommanderMode.Active);
    AssertEqual(TacticalCommanderMode.MonitorOnly, mode, "mode");
    AssertTrue(TacticalCommanderModePolicy.RunsLedger(mode), "ledger");
    AssertFalse(TacticalCommanderModePolicy.AllowsWrites(mode), "writes");
}
```

- [ ] **Step 3: Add csproj include**

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Operations\TacticalCommanderMode.cs" Link="Operations\TacticalCommanderMode.cs" />
```

- [ ] **Step 4: Wire plugin config**

In `Plugin.cs`, add:

```csharp
internal ConfigEntry<string> TacticalCommanderModeRaw;
```

Bind in `Awake` near other tactical orchestrator config:

```csharp
TacticalCommanderModeRaw = Config.Bind(
    "Tactical Orchestrator",
    "Tactical Commander Mode",
    "Active",
    "Default Active. Off disables the operations-ledger command system; MonitorOnly runs vision/ledger/tasks/monitor without vanilla writes; Active runs the full tactical command system for AI sides.");
```

Add property:

```csharp
internal WhiskeyRealism.Tactical.Operations.TacticalCommanderMode TacticalCommanderModeValue =>
    WhiskeyRealism.Tactical.Operations.TacticalCommanderModePolicy.Parse(
        TacticalCommanderModeRaw?.Value,
        WhiskeyRealism.Tactical.Operations.TacticalCommanderMode.Active);
```

- [ ] **Step 5: Add legacy flag precedence and migration logging**

Existing BepInEx config files can contain older tactical flags. Add one resolver point in `Plugin.cs` so the new master mode is authoritative when present:

```csharp
internal bool TacticalOperationsLedgerEnabled =>
    TacticalCommanderModePolicy.RunsLedger(TacticalCommanderModeValue);

internal bool TacticalOperationsLedgerAllowsWrites =>
    TacticalCommanderModePolicy.AllowsWrites(TacticalCommanderModeValue);
```

Rules:

- When `Tactical Commander Mode` exists, it overrides scattered legacy tactical behavior flags for the operations-ledger surfaces.
- Legacy flags that still own unrelated shipped patches keep their current behavior until that patch is retargeted in Task 9.
- On first config read, emit one bounded log line:

```text
[TacticalCommanderMode] mode=Active source=config legacyFlags=ignored-for-ledger releaseDefault=Active
```

Do not rewrite the BepInEx cfg file from code. BepInEx owns file persistence; the migration is precedence plus bounded diagnostics.

- [ ] **Step 6: Run tests and commit**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass.

Commit:

```bash
git add src/WhiskeyRealism/Tactical/Operations/TacticalCommanderMode.cs src/WhiskeyRealism/Plugin.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat(tactical): add tactical commander mode"
```

## Task 3: Confidence And Vision Pure Models

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Operations/TacticalVisionModel.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add pure vision model**

Create `TacticalVisionModel.cs`:

```csharp
using System;

namespace WhiskeyRealism.Tactical.Operations
{
    public enum TacticalContactSource
    {
        VisualContact = 0,
        RecentFire = 1,
        ObjectivePressure = 2,
        FriendlyRoutedFromArea = 3,
        InferredMovement = 4,
    }

    public readonly struct ContactObservationInput
    {
        public ContactObservationInput(TacticalContactSource source, float estimatedStrength, float secondsSinceObserved, bool currentlyVisible, bool objectiveLinked, bool scoutTaskLinked)
        {
            Source = source;
            EstimatedStrength = Math.Max(0f, Sanitize(estimatedStrength));
            SecondsSinceObserved = Math.Max(0f, Sanitize(secondsSinceObserved));
            CurrentlyVisible = currentlyVisible;
            ObjectiveLinked = objectiveLinked;
            ScoutTaskLinked = scoutTaskLinked;
        }

        public TacticalContactSource Source { get; }
        public float EstimatedStrength { get; }
        public float SecondsSinceObserved { get; }
        public bool CurrentlyVisible { get; }
        public bool ObjectiveLinked { get; }
        public bool ScoutTaskLinked { get; }

        private static float Sanitize(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }

    public readonly struct EnemyContactReport
    {
        public EnemyContactReport(ContactObservationInput input, float confidence)
        {
            Input = input;
            Confidence = Clamp01(confidence);
        }

        public ContactObservationInput Input { get; }
        public float Confidence { get; }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }

    public static class TacticalVisionModel
    {
        public static EnemyContactReport BuildContact(ContactObservationInput input, float staleAfterSeconds)
        {
            float baseWeight = SourceWeight(input.Source);
            float visible = input.CurrentlyVisible ? 0.10f : 0f;
            float objective = input.ObjectiveLinked ? 0.05f : 0f;
            float scout = input.ScoutTaskLinked ? 0.05f : 0f;
            float staleAfter = Math.Max(1f, Sanitize(staleAfterSeconds));
            float staleness = Clamp01(input.SecondsSinceObserved / staleAfter);
            float confidence = Clamp01((baseWeight + visible + objective + scout) * (1f - staleness));
            return new EnemyContactReport(input, confidence);
        }

        private static float SourceWeight(TacticalContactSource source)
        {
            switch (source)
            {
                case TacticalContactSource.VisualContact: return 0.90f;
                case TacticalContactSource.RecentFire: return 0.65f;
                case TacticalContactSource.ObjectivePressure: return 0.55f;
                case TacticalContactSource.FriendlyRoutedFromArea: return 0.50f;
                case TacticalContactSource.InferredMovement: return 0.35f;
                default: return 0.25f;
            }
        }

        private static float Clamp01(float value)
        {
            value = Sanitize(value);
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private static float Sanitize(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }
}
```

- [ ] **Step 2: Add tests**

Register:

```csharp
("tactical vision visual contact high confidence", TacticalVisionVisualContactHighConfidence),
("tactical vision stale recent fire decays", TacticalVisionStaleRecentFireDecays),
```

Add:

```csharp
static void TacticalVisionVisualContactHighConfidence()
{
    var report = TacticalVisionModel.BuildContact(
        new ContactObservationInput(TacticalContactSource.VisualContact, 1200f, 0f, true, true, true),
        staleAfterSeconds: 600f);

    AssertTrue(report.Confidence > 0.95f, "confidence");
}

static void TacticalVisionStaleRecentFireDecays()
{
    var fresh = TacticalVisionModel.BuildContact(
        new ContactObservationInput(TacticalContactSource.RecentFire, 800f, 0f, false, false, false),
        staleAfterSeconds: 300f);
    var stale = TacticalVisionModel.BuildContact(
        new ContactObservationInput(TacticalContactSource.RecentFire, 800f, 240f, false, false, false),
        staleAfterSeconds: 300f);

    AssertTrue(fresh.Confidence > stale.Confidence, "decay");
    AssertTrue(stale.Confidence < 0.25f, "stale confidence");
}
```

- [ ] **Step 3: Add csproj include, test, commit**

Add:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Operations\TacticalVisionModel.cs" Link="Operations\TacticalVisionModel.cs" />
```

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Commit:

```bash
git add src/WhiskeyRealism/Tactical/Operations/TacticalVisionModel.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat(tactical): add vision confidence model"
```

## Task 4: Operations Ledger And Operation Selection

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Operations/TacticalOperationsLedgerModel.cs`
- Create: `src/WhiskeyRealism/Tactical/Operations/TacticalOperationSelectionModel.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add ledger records**

Create `TacticalOperationsLedgerModel.cs` with enums and records:

```csharp
using System;

namespace WhiskeyRealism.Tactical.Operations
{
    public enum TacticalOperationShape { SingleMainEffort, SequentialObjectives, ParallelObjectives, FixAndFlank, DefensiveNetwork, DelayAndFallback }
    public enum TacticalOperationPhase { Planning, Scouting, Forming, Committed, Exploiting, Consolidating, Aborting, Complete }
    public enum TacticalObjectiveStatus { Unknown, Scouting, WeaklyHeld, StronglyHeld, Contested, Secured, Lost }
    public enum TacticalReassessmentTier { Continue, SoftAbortReview, HardAbort }

    public readonly struct ObjectiveRecord
    {
        public ObjectiveRecord(ObjectiveObservationInput observation, TacticalObjectiveStatus status, float enemyStrength, float friendlyAssignedStrength)
        {
            Observation = TacticalObjectiveSourceModel.Normalize(observation);
            Status = status;
            EnemyStrength = Math.Max(0f, enemyStrength);
            FriendlyAssignedStrength = Math.Max(0f, friendlyAssignedStrength);
        }

        public ObjectiveObservationInput Observation { get; }
        public TacticalObjectiveStatus Status { get; }
        public float EnemyStrength { get; }
        public float FriendlyAssignedStrength { get; }
    }

    public readonly struct OperationRecord
    {
        public OperationRecord(TacticalOperationShape shape, TacticalOperationPhase phase, string primaryObjectiveId, float minimumCommitSeconds)
        {
            Shape = shape;
            Phase = phase;
            PrimaryObjectiveId = string.IsNullOrWhiteSpace(primaryObjectiveId) ? "objective-unknown" : primaryObjectiveId;
            MinimumCommitSeconds = Math.Max(0f, minimumCommitSeconds);
        }

        public TacticalOperationShape Shape { get; }
        public TacticalOperationPhase Phase { get; }
        public string PrimaryObjectiveId { get; }
        public float MinimumCommitSeconds { get; }
    }

    public static class TacticalOperationsLedgerModel
    {
        public static TacticalReassessmentTier ReassessCommittedOperation(float progressStalledSeconds, float confidence, float odds, bool forceCollapsed, bool objectiveSecured)
        {
            if (forceCollapsed || objectiveSecured) return TacticalReassessmentTier.HardAbort;
            if (progressStalledSeconds >= 300f || confidence < 0.35f || odds < 0.65f) return TacticalReassessmentTier.SoftAbortReview;
            return TacticalReassessmentTier.Continue;
        }
    }
}
```

- [ ] **Step 2: Add operation selector**

Create `TacticalOperationSelectionModel.cs`:

```csharp
using System;
using WhiskeyRealism.Strategic;

namespace WhiskeyRealism.Tactical.Operations
{
    public readonly struct ForceAvailabilitySnapshot
    {
        public ForceAvailabilitySnapshot(float availableStrength, float reserveFraction)
        {
            AvailableStrength = Math.Max(0f, availableStrength);
            ReserveFraction = Clamp01(reserveFraction);
        }

        public float AvailableStrength { get; }
        public float ReserveFraction { get; }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }

    public static class TacticalOperationSelectionModel
    {
        public static TacticalOperationShape Select(ObjectiveRecord first, ObjectiveRecord second, ForceAvailabilitySnapshot force, PersonalityVector personality)
        {
            float aggression = (personality.Aggression + 1f) * 0.5f;
            bool firstWeak = first.EnemyStrength <= Math.Max(1f, first.FriendlyAssignedStrength) * 0.75f;
            bool secondWeak = second.EnemyStrength <= Math.Max(1f, second.FriendlyAssignedStrength) * 0.75f;
            bool reserveSafe = force.ReserveFraction >= (aggression > 0.65f ? 0.15f : 0.25f);

            if (firstWeak && secondWeak && reserveSafe && aggression >= 0.45f)
                return TacticalOperationShape.ParallelObjectives;

            if (!firstWeak && secondWeak)
                return TacticalOperationShape.FixAndFlank;

            if (force.AvailableStrength < first.EnemyStrength + second.EnemyStrength)
                return TacticalOperationShape.SequentialObjectives;

            return TacticalOperationShape.SingleMainEffort;
        }
    }
}
```

- [ ] **Step 3: Add tests**

Register:

```csharp
("tactical operations parallel requires per objective advantage", TacticalOperationsParallelRequiresPerObjectiveAdvantage),
("tactical operations strong and weak selects fix and flank", TacticalOperationsStrongWeakSelectsFixAndFlank),
("tactical operations soft abort before collapse", TacticalOperationsSoftAbortBeforeCollapse),
```

Add test methods with two `ObjectiveRecord` inputs. Use `new PersonalityVector(0.4f, 0f, 0f, 0f, 0f)` for aggressive-enough commander and `new ForceAvailabilitySnapshot(8000f, 0.3f)`.

- [ ] **Step 4: Add csproj includes, test, commit**

Add:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Operations\TacticalOperationsLedgerModel.cs" Link="Operations\TacticalOperationsLedgerModel.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Operations\TacticalOperationSelectionModel.cs" Link="Operations\TacticalOperationSelectionModel.cs" />
```

Run harness and commit:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git add src/WhiskeyRealism/Tactical/Operations/TacticalOperationsLedgerModel.cs src/WhiskeyRealism/Tactical/Operations/TacticalOperationSelectionModel.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat(tactical): add operations ledger selection model"
```

## Task 5: Command Node State, Task Planner, And Monitor

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Operations/CommandNodeOperationalState.cs`
- Create: `src/WhiskeyRealism/Tactical/Operations/CommandNodeTaskPlanner.cs`
- Create: `src/WhiskeyRealism/Tactical/Operations/TacticalCommandMonitor.cs`
- Modify: test csproj and `Program.cs`

- [ ] **Step 1: Add state/task contracts**

Create `CommandNodeOperationalState.cs` with:

```csharp
namespace WhiskeyRealism.Tactical.Operations
{
    public enum CommandNodeRole { Unknown, MainEffort, SupportingAttack, FixingForce, ScreeningForce, Reserve, Defender, FallbackGuard, Probe, FlankMarch }
    public enum CommandTaskType { None, Scout, Probe, Screen, FormUp, AdvanceToAssembly, AttackObjective, FixEnemy, SupportAttack, HoldObjective, HoldChoke, GuardFlank, ReserveWait, ReleaseReserve, FallBackToLine, Delay, Consolidate, RecoverStuckOrder }
    public enum CommandTaskState { Planning, MovingToAssembly, Forming, WaitingForCommit, Committed, Engaged, Reorganizing, Complete, Failed }
    public enum CommandEchelonKind { Unknown, ArmyLike, CorpsLike, DivisionLike, BrigadeLike }

    public readonly struct CommandNodeOperationalState
    {
        public CommandNodeOperationalState(string nodeId, CommandEchelonKind echelon, CommandNodeRole role, CommandTaskType task, CommandTaskState taskState)
        {
            NodeId = string.IsNullOrWhiteSpace(nodeId) ? "node-unknown" : nodeId;
            Echelon = echelon;
            Role = role;
            Task = task;
            TaskState = taskState;
        }

        public string NodeId { get; }
        public CommandEchelonKind Echelon { get; }
        public CommandNodeRole Role { get; }
        public CommandTaskType Task { get; }
        public CommandTaskState TaskState { get; }
    }
}
```

- [ ] **Step 2: Add task planner**

Create `CommandNodeTaskPlanner.cs`:

```csharp
namespace WhiskeyRealism.Tactical.Operations
{
    public static class CommandNodeTaskPlanner
    {
        public static CommandTaskType PlanTask(CommandNodeRole role, TacticalOperationShape shape, bool contact, bool atObjective)
        {
            switch (role)
            {
                case CommandNodeRole.Reserve: return CommandTaskType.ReserveWait;
                case CommandNodeRole.Defender: return atObjective ? CommandTaskType.HoldObjective : CommandTaskType.AdvanceToAssembly;
                case CommandNodeRole.FallbackGuard: return CommandTaskType.FallBackToLine;
                case CommandNodeRole.FixingForce: return contact ? CommandTaskType.FixEnemy : CommandTaskType.AdvanceToAssembly;
                case CommandNodeRole.ScreeningForce: return CommandTaskType.Screen;
                case CommandNodeRole.Probe: return CommandTaskType.Probe;
                case CommandNodeRole.MainEffort: return shape == TacticalOperationShape.DefensiveNetwork ? CommandTaskType.HoldObjective : CommandTaskType.AttackObjective;
                case CommandNodeRole.SupportingAttack: return CommandTaskType.SupportAttack;
                default: return CommandTaskType.FormUp;
            }
        }
    }
}
```

- [ ] **Step 3: Add monitor**

Create `TacticalCommandMonitor.cs`:

```csharp
namespace WhiskeyRealism.Tactical.Operations
{
    public enum TacticalIdleClassification { ValidIdle, IllegalIdle, ProtectedNoWrite }

    public readonly struct CommandPhysicalState
    {
        public CommandPhysicalState(bool routed, bool playerProtected, bool pathInterrupted, int paths, bool activeMove, int formation)
        {
            Routed = routed;
            PlayerProtected = playerProtected;
            PathInterrupted = pathInterrupted;
            Paths = paths;
            ActiveMove = activeMove;
            Formation = formation;
        }

        public bool Routed { get; }
        public bool PlayerProtected { get; }
        public bool PathInterrupted { get; }
        public int Paths { get; }
        public bool ActiveMove { get; }
        public int Formation { get; }
    }

    public static class TacticalCommandMonitor
    {
        public static TacticalIdleClassification ClassifyIdle(CommandNodeOperationalState state, CommandPhysicalState physical)
        {
            if (physical.PlayerProtected || physical.Routed) return TacticalIdleClassification.ProtectedNoWrite;
            if (state.Task == CommandTaskType.ReserveWait || state.Task == CommandTaskType.HoldObjective || state.Task == CommandTaskType.HoldChoke || state.Task == CommandTaskType.FallBackToLine) return TacticalIdleClassification.ValidIdle;
            if (physical.PathInterrupted && physical.Paths <= 0 && !physical.ActiveMove) return TacticalIdleClassification.IllegalIdle;
            if (state.Task == CommandTaskType.None) return TacticalIdleClassification.IllegalIdle;
            return TacticalIdleClassification.ValidIdle;
        }
    }
}
```

- [ ] **Step 4: Add includes/tests/commit**

Add csproj includes for the three files. Add tests for reserve idle valid, path interrupted idle illegal, and player protected no-write. Run harness and commit:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git add src/WhiskeyRealism/Tactical/Operations/CommandNodeOperationalState.cs src/WhiskeyRealism/Tactical/Operations/CommandNodeTaskPlanner.cs src/WhiskeyRealism/Tactical/Operations/TacticalCommandMonitor.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat(tactical): add command task monitor"
```

## Task 6: Command Posture Executor Pure Decisions

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Operations/CommandPostureExecutor.cs`
- Modify: test csproj and `Program.cs`

- [ ] **Step 1: Add executor decision model**

Create:

```csharp
namespace WhiskeyRealism.Tactical.Operations
{
    public enum PostureExecutionAction { NoWrite, SetFormation, SetWaypoint, SetFormationAndWaypoint, ChangeStance, ReleaseReserve, FallbackToLine, RecoverInterruptedOrder }

    public readonly struct PostureExecutionDecision
    {
        public PostureExecutionDecision(PostureExecutionAction action, string reason)
        {
            Action = action;
            Reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;
        }

        public PostureExecutionAction Action { get; }
        public string Reason { get; }
    }

    public readonly struct WriteEligibilitySnapshot
    {
        public WriteEligibilitySnapshot(bool modeAllowsWrites, bool playerProtected, bool routed, bool orderPending, bool recentOrder)
        {
            ModeAllowsWrites = modeAllowsWrites;
            PlayerProtected = playerProtected;
            Routed = routed;
            OrderPending = orderPending;
            RecentOrder = recentOrder;
        }

        public bool ModeAllowsWrites { get; }
        public bool PlayerProtected { get; }
        public bool Routed { get; }
        public bool OrderPending { get; }
        public bool RecentOrder { get; }
    }

    public static class CommandPostureExecutor
    {
        public static PostureExecutionDecision Decide(CommandNodeOperationalState state, CommandPhysicalState physical, WriteEligibilitySnapshot eligibility)
        {
            if (!eligibility.ModeAllowsWrites) return new PostureExecutionDecision(PostureExecutionAction.NoWrite, "mode-monitor-only");
            if (eligibility.PlayerProtected) return new PostureExecutionDecision(PostureExecutionAction.NoWrite, "player-protected");
            if (eligibility.Routed) return new PostureExecutionDecision(PostureExecutionAction.NoWrite, "routed");
            if (eligibility.OrderPending) return new PostureExecutionDecision(PostureExecutionAction.NoWrite, "order-pending");
            if (eligibility.RecentOrder) return new PostureExecutionDecision(PostureExecutionAction.NoWrite, "recent-order");

            if (physical.PathInterrupted && physical.Paths <= 0 && !physical.ActiveMove)
                return new PostureExecutionDecision(PostureExecutionAction.RecoverInterruptedOrder, "illegal-idle-path-interrupted");

            switch (state.Task)
            {
                case CommandTaskType.FormUp: return new PostureExecutionDecision(PostureExecutionAction.SetFormationAndWaypoint, "form-up");
                case CommandTaskType.AttackObjective: return new PostureExecutionDecision(PostureExecutionAction.SetFormationAndWaypoint, "attack-objective");
                case CommandTaskType.FallBackToLine: return new PostureExecutionDecision(PostureExecutionAction.FallbackToLine, "fallback-line");
                case CommandTaskType.ReserveWait: return new PostureExecutionDecision(PostureExecutionAction.NoWrite, "valid-reserve-wait");
                default: return new PostureExecutionDecision(PostureExecutionAction.NoWrite, "already-valid");
            }
        }
    }
}
```

- [ ] **Step 2: Test monitor-only and active recovery**

Add tests that assert `ModeAllowsWrites=false` returns `NoWrite/mode-monitor-only`, and `ModeAllowsWrites=true` plus interrupted path returns `RecoverInterruptedOrder`.

- [ ] **Step 3: Add include, run, commit**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git add src/WhiskeyRealism/Tactical/Operations/CommandPostureExecutor.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat(tactical): add posture executor decisions"
```

## Task 7: Runtime Adapters And Orchestrator State Ownership

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalVisionRuntimeAdapter.cs`
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalOperationsLedgerRuntime.cs`
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/CommandNodeOperationsRuntime.cs`
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/StrategicBattleIntentSnapshot.cs`
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs`
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleOrchestrator.cs`
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinator.cs`

- [ ] **Step 1: Add runtime adapters**

Implement adapters as runtime-only files, excluded from harness unless they avoid Unity-only references. They read vanilla data and produce pure inputs:

```csharp
// TacticalVisionRuntimeAdapter.cs
// Build ContactObservationInput[] and ObjectiveObservationInput[] from AIBattle/BattleUnits/Regiment state.
```

```csharp
// TacticalOperationsLedgerRuntime.cs
// Own per-side ledger snapshot; update only from TacticalBattleOrchestrator.Tick.
```

```csharp
// CommandNodeOperationsRuntime.cs
// Map existing EchelonOrchestrator/ArmyOrchestrator command nodes, direct-child intents, and OperationRecord into CommandNodeOperationalState[].
```

Create the strategic read model:

```csharp
// StrategicBattleIntentSnapshot.cs
// Immutable battle-local copy of casualty pressure, time pressure, and theater/campaign intent.
// Built by the orchestrator from strategic read APIs; no battle patch writes strategic state.
```

- [ ] **Step 2: Add orchestrator-owned snapshots**

In `ArmyOrchestrator`, which already derives from `EchelonOrchestrator`, add read-only properties:

```csharp
internal TacticalCommanderMode CommanderMode { get; private set; }
internal IReadOnlyList<CommandNodeOperationalState> CurrentCommandOperations => _commandOperations;
internal OperationRecord CurrentOperation => _currentOperation;
```

Update these only from an orchestrator tick method:

```csharp
internal void UpdateOperationsLedger(/* pure snapshots from runtime adapters */)
{
    // Build vision.
    // Update ledger.
    // Select operation.
    // Assign command node tasks.
    // Replace immutable snapshots atomically.
}
```

Do not create a parallel hierarchy type. If command-node kind needs refinement, extend the existing `EchelonKind` / command-tree mapping and add harness cases against `EchelonOrchestrator` behavior already covered in `tests/WhiskeyRealism.Tests/Program.cs`.

- [ ] **Step 3: Ensure side gate**

In the tick path, enforce:

```csharp
if (!ShouldRunTacticalCommanderForSide(battleSide, GameVars.ai_vs_ai)) return;
```

Use the same semantics as `AIBattle.UpdateAITasks`: AI sides only, both sides only under AI-vs-AI.

- [ ] **Step 4: Build and commit**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Commit runtime adapter work:

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/TacticalVisionRuntimeAdapter.cs src/WhiskeyRealism/Tactical/Orchestrator/TacticalOperationsLedgerRuntime.cs src/WhiskeyRealism/Tactical/Orchestrator/CommandNodeOperationsRuntime.cs src/WhiskeyRealism/Tactical/Orchestrator/StrategicBattleIntentSnapshot.cs src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleOrchestrator.cs src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinator.cs
git commit -m "feat(tactical): wire operations ledger runtime"
```

## Task 8: Telemetry And MonitorOnly Full Loop

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Operations/TacticalOperationsTelemetry.cs`
- Modify: `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs`
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs`

- [ ] **Step 1: Add telemetry formatter and throttle signatures**

Create `TacticalOperationsTelemetry.cs` with pure formatters:

```csharp
namespace WhiskeyRealism.Tactical.Operations
{
    public static class TacticalOperationsTelemetry
    {
        public static string PostureSummary(int side, int validIdle, int illegalIdle, int recoveringStuck, int activeAttacks, int reservesWaiting)
        {
            return "[TacticalPostureSummary] side=" + side
                + " validIdle=" + validIdle
                + " illegalIdle=" + illegalIdle
                + " recoveringStuck=" + recoveringStuck
                + " activeAttacks=" + activeAttacks
                + " reservesWaiting=" + reservesWaiting;
        }

        public static string CommandPosture(int side, string node, CommandTaskType task, PostureExecutionDecision decision)
        {
            return "[TacticalCommandPosture] side=" + side
                + " node=" + TacticalTelemetry.SafeToken(node)
                + " task=" + task
                + " decision=" + decision.Action
                + " reason=" + TacticalTelemetry.SafeToken(decision.Reason);
        }
    }
}
```

- [ ] **Step 2: Emit MonitorOnly full-loop telemetry**

In #35 / orchestrator telemetry path, emit:

- `[TacticalOpsLedger]` on material operation signature change.
- `[TacticalCommandAssignment]` on node role/task/objective change.
- `[TacticalCommandPosture]` for NoWrite decisions in MonitorOnly, throttled per node.
- `[TacticalPostureSummary]` every 15 seconds per side.

Use `TacticalTelemetry.ShouldEmit(...)` or a new equivalent dictionary keyed by side/node/signature.

- [ ] **Step 3: Run local MonitorOnly smoke**

Set local config:

```ini
Tactical Commander Mode = MonitorOnly
Enable Tactical Decision Matrix Logging = true
```

Run a battle and tail:

```bash
tail -f "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Expected:

- ledger/assignment/posture summary lines appear.
- no vanilla write rows from the new executor.
- no repeated exceptions or Harmony anchor failures.
- illegal idle rows are classified, not silently ignored.

- [ ] **Step 4: Commit**

```bash
git add src/WhiskeyRealism/Tactical/Operations/TacticalOperationsTelemetry.cs src/WhiskeyRealism/Patches/TacticalObserverPatch.cs src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs
git commit -m "feat(tactical): add operations monitor telemetry"
```

## Task 9: Macro, Reserve, Stance, And Fallback Boundary Retargeting

**Files:**
- Modify: `src/WhiskeyRealism/Patches/BattleMacroStrategyPatch.cs`
- Modify: `src/WhiskeyRealism/Patches/BattleGroupStancePatch.cs`
- Modify: `src/WhiskeyRealism/Patches/BattleReserveDoctrinePatch.cs`
- Modify: `src/WhiskeyRealism/Patches/BattleReserveCommitGatePatch.cs`
- Modify: `src/WhiskeyRealism/Patches/B8CheckLineFallbacksObserverPatch.cs`

- [ ] **Step 1: Macro boundary**

Update #44 so it reads the selected ledger operation through `ArmyOrchestrator` and remains the only `macroai` writer. Keep vanilla retreat/end-battle paths untouched. Test that dynamic `-1` still preserves vanilla macro.

Required boundary:

```text
CheckGlobalAIStrategy vanilla write -> #44 snapshot/evaluate/postfix adjustment -> ledger operation pressure only through #44
```

Do not add any `macroai` writes to `TacticalObserverPatch`, `BattleCommandPostureExecutorPatch`, runtime adapters, or `ArmyOrchestrator`.

- [ ] **Step 2: Reserve boundary**

Update #57/#59 to consume `ReserveAssignment` and log drift when vanilla `AssignReserves` conflicts with protected reserves. Do not Prefix-block `AssignReserves`.

Required boundary:

```text
AssignReserves vanilla assignment -> #35 observer records output -> #57 reserve-list bias and #59 commit gate consume ledger reserve role -> drift telemetry when vanilla assignment conflicts
```

If `AssignReserves()` output is needed for `LinkReservesToLineGroup()`, preserve vanilla data flow and only gate later reserve movement at the existing #59 surface.

- [ ] **Step 3: Stance boundary**

Update #45 to map `CommandTaskType` to stance pressure:

```text
AttackObjective/SupportAttack -> aggressive stance when eligible
FixEnemy/HoldObjective/HoldChoke -> line/defensive stance
ReserveWait -> defensive/no aggressive flip
FallBackToLine -> fallback-compatible stance
```

- [ ] **Step 4: Fallback boundary**

Update B8 fallback observer to report planned fallback task context. Do not write emergency withdrawal unless existing B8 doctrine and write envelope still allow it.

- [ ] **Step 5: Run and commit**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
git add src/WhiskeyRealism/Patches/BattleMacroStrategyPatch.cs src/WhiskeyRealism/Patches/BattleGroupStancePatch.cs src/WhiskeyRealism/Patches/BattleReserveDoctrinePatch.cs src/WhiskeyRealism/Patches/BattleReserveCommitGatePatch.cs src/WhiskeyRealism/Patches/B8CheckLineFallbacksObserverPatch.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat(tactical): retarget existing consumers to operations ledger"
```

## Task 10: Active Command Posture Executor Patch

**Files:**
- Create: `src/WhiskeyRealism/Patches/BattleCommandPostureExecutorPatch.cs`
- Modify: `docs/patch-catalog.md`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add #61 patch shell**

Create a Postfix patch on `AIBattle.AdjustGroupFormations` or the chosen safest post-vanilla posture surface:

```csharp
[HarmonyPatch(typeof(AIBattle), "AdjustGroupFormations")]
internal static class BattleCommandPostureExecutorPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    internal static void Postfix(AIBattle __instance)
    {
        if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
        if (!TacticalCommanderModePolicy.AllowsWrites(Plugin.Instance.TacticalCommanderModeValue)) return;

        // Build patch-local vanilla snapshots.
        // Resolve command-node operation snapshot from side orchestrator.
        // Call CommandPostureExecutor.Decide.
        // Apply only bounded SetGroupFormation/SetWaypoint/ChangeStance decisions.
    }
}
```

- [ ] **Step 2: Implement write envelope**

Before any write, require:

- AI side gate.
- no W&L/player-subordinate/current-command protection.
- no routed/marked-for-rout group.
- no active order queue/courier pending.
- no recent executor order.
- no active movement making progress.
- decision action is not `NoWrite`.

- [ ] **Step 3: Apply vanilla-safe writes**

Use only:

- `BattleUnits.SetGroupFormation(Regiment, ...)`
- `BattleUnits.SetWaypoint(Regiment, ..., useorderdelay: true, clearinterruptionpaths: true)`
- `BattleUnits.ChangeStance(...)`

Do not manually mutate command-node state from this patch.

- [ ] **Step 4: Add patch catalog entry**

Add ordinal #61 in `docs/patch-catalog.md`:

```markdown
| 61 | `BattleCommandPostureExecutorPatch` | Postfix | `Patches/BattleCommandPostureExecutorPatch.cs` | `AIBattle.AdjustGroupFormations` (:5875), vanilla writes through `BattleUnits.SetWaypoint` (:91232) and `BattleUnits.SetGroupFormation` (:91822) | Active tactical operations ledger posture executor. Runs only when `Tactical Commander Mode = Active`; corrects illegal idle/stuck command groups using ledger task assignments and strict W&L/order/rout/recent-order gates. |
```

- [ ] **Step 5: Run tests/build and commit**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
git add src/WhiskeyRealism/Patches/BattleCommandPostureExecutorPatch.cs docs/patch-catalog.md tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat(tactical): add command posture executor patch"
```

## Task 11: Docs, Build, Deploy, And Active Smoke

**Files:**
- Create: `docs/tactical-operations-ledger.md`
- Modify: `docs/tactical-orchestrator.md`
- Modify: `docs/handoff.md`
- Modify: `docs/patch-catalog.md`

- [ ] **Step 1: Write living doc**

Create `docs/tactical-operations-ledger.md` with:

- system overview.
- config contract: release/default `Tactical Commander Mode = Active`.
- MonitorOnly smoke checkpoint.
- Active smoke checklist.
- known vanilla anchors.
- telemetry markers.
- rollback: set `Tactical Commander Mode = Off`.

- [ ] **Step 2: Update living docs**

Update `docs/tactical-orchestrator.md`, `docs/handoff.md`, and `docs/patch-catalog.md` with:

- #61 entry and current state.
- hash/build state after verification.
- smoke status.
- remaining risks.

- [ ] **Step 3: Run final harness and build**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected:

- console harness passes.
- build has 0 errors.

- [ ] **Step 4: Deploy and verify DLL hash**

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: SHA-256 hashes match.

- [ ] **Step 5: Active smoke**

Set local config:

```ini
Tactical Commander Mode = Active
Enable Tactical Decision Matrix Logging = true
```

Start a battle and confirm:

- `[TacticalOpsLedger]` appears.
- `[TacticalCommandAssignment]` appears.
- `[TacticalCommandPosture]` writes are bounded.
- `[TacticalPostureSummary]` shows illegal idle trending down.
- no repeated `Exception`, `ERROR`, `missing-anchor`, or Harmony patch failure.
- no player-subordinate retasking.
- no repeated non-reserve command nodes remain in `MarchColumn + pathInterrupted=True + paths=0 + activeMove=False` without a valid ledger reason.

- [ ] **Step 6: Commit docs and closeout**

```bash
git add docs/tactical-operations-ledger.md docs/tactical-orchestrator.md docs/handoff.md docs/patch-catalog.md
git commit -m "docs(tactical): document operations ledger smoke"
```

## Task 12: Final Release Closeout

**Files:**
- Modify: `docs/superpowers/plans/archive/README.md`
- Move: this plan to `docs/superpowers/plans/archive/`
- Modify: `docs/superpowers/specs/archive/README.md`
- Move: design spec to `docs/superpowers/specs/archive/` only after Active smoke passes
- Modify: `MEMORY.md`

- [ ] **Step 1: Archive only after smoke**

Do not archive the spec or plan until Active smoke passes on the deployed DLL. After smoke:

```bash
git mv docs/superpowers/plans/2026-05-10-tactical-operations-ledger-command-system-implementation-plan.md docs/superpowers/plans/archive/
git mv docs/superpowers/specs/2026-05-10-tactical-operations-ledger-command-system-design.md docs/superpowers/specs/archive/
```

- [ ] **Step 2: Update indexes and memory**

Update archive README files and `MEMORY.md` with:

- deployed DLL SHA-256.
- smoke log date/time.
- config default `Active`.
- rollback setting `Tactical Commander Mode = Off`.

- [ ] **Step 3: Final verification**

Run:

```bash
git status --short
git log --oneline -5
```

Expected: only intended archive/doc/memory changes.

Commit:

```bash
git add docs/superpowers/plans/archive docs/superpowers/specs/archive MEMORY.md
git commit -m "docs(tactical): archive operations ledger release"
git push origin main
```

## Self-Review Checklist

- Spec coverage:
  - Anchor verification: Task 1.
  - Active default config: Task 2 and Task 11.
  - Confidence model: Task 3.
  - Vision/ledger/operation selection: Tasks 3-4 and 7.
  - Echelon-aware command-node tasks: Task 5 and Task 7.
  - Vanilla boundaries: Task 9.
  - Posture executor: Task 6 and Task 10.
  - Telemetry throttling: Task 8.
  - Smoke/deploy docs: Task 11.
  - Release archive: Task 12.
- No broad Prefix-blocking is planned.
- Harmony patches do not mutate authoritative ledger/task state.
- MonitorOnly is a smoke checkpoint only; released/default mode is Active.
- Every new pure file is added to the explicit test csproj include list.
- DLL-affecting closeout includes build, deploy, stat, and SHA-256 verification.
