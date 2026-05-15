# Full-Spectrum Tactical Command Doctrine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Status as of 2026-05-14:** implementation is merged in the working tree,
> built, deployed, and hash-verified in DLL
> `f2e7705b96c55ea371ca08a3a56d28ebf324bfc114618c184ccba375d17ee1f1`
> (1027072 bytes; 893 PASS). This plan remains active only for fresh Active
> battle smoke and final archive closeout. Current runtime truth lives in
> [`docs/tactical-operations-ledger.md`](../../tactical-operations-ledger.md)
> and [`docs/tactical-orchestrator.md`](../../tactical-orchestrator.md); do not
> infer missing code from unchecked historical task boxes below without checking
> shipped source and the living docs.

**Goal:** Build the full tactical command doctrine system so each AI side maintains battle intent, enemy/objective awareness, echelon assignments, committed operations, reserve/fallback doctrine, and concrete movement/fight orders instead of leaving brigades scattered or stalled.

**Architecture:** The system stays deterministic and in-process. Pure doctrine models live under `src/WhiskeyRealism/Tactical/Operations/` and are covered by the console harness; runtime adapters collect vanilla battle evidence and write only during the per-battle orchestrator tick; Harmony patches remain bounded consumers that read doctrine decisions and steer vanilla battle state through existing vanilla APIs.

**Tech Stack:** C# netstandard2.1, BepInEx 5.4.x, HarmonyX, Grand Tactician vanilla battle anchors from `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`, .NET console harness at `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, Superpowers TDD and subagent-driven execution.

---

## Execution Rules

- Execute from an isolated worktree before code changes: use `superpowers:using-git-worktrees`, then relink `refs` in the worktree if it is missing.
- Keep the existing user file `654890_47.jpg` untracked and untouched.
- Use TDD for every pure or adapter change: write the failing harness test, run the harness and confirm the named failure, implement the minimal production code, rerun the harness.
- Keep Harmony patches read-only for Whiskey orchestrator state. All `OperationRecord`, battlefield-picture, assignment, and stuck/progress writes happen in the per-battle orchestrator tick cycle.
- `TacticalCommanderMode.Active` remains the default because the user explicitly overrode the repo's default-off tactical-writer preference for this feature. The plan still keeps `MonitorOnly` and `NoWrite` diagnostics so runtime smoke can isolate behavior.
- Do not Prefix-block vanilla `CheckGlobalAIStrategy`, `AssignReserves`, or `CheckUseOfReserves` without a specific task below. This plan layers doctrine through existing consumers first, then uses bounded consumer decisions.
- Commit after every task with only the files listed for that task.
- After every DLL-affecting task, run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

- Before claiming the implementation is ready for in-game smoke, use `whiskey-dll-deploy-smoke` and verify matching SHA-256 hashes for `dist/WhiskeyRealism.dll` and the deployed plugin DLL.

## Vanilla And Mod Anchor Contract

Use these anchors as the implementation boundary:

- Campaign orders: `AICampaign.CheckOffensiveMovements()` at decompile line `14166`, `AICampaign.MoveUnitTo(...)` at `14479`.
- Battle hierarchy and movement: `BattleUnits.GetHierarchyTree()` at `92720`, `BattleUnits.SetWaypoint(...)` at `91232`, `BattleUnits.SetGroupFormation(...)` at `91822`.
- Battle macro AI: `AIBattle.UpdateAITasks()` at `5857`, `AIBattle.CheckGlobalAIStrategy()` at `6314`.
- Tactical consumers: `BattleUnits.ChangeStance(...)` at `90772`, `AIBattle.UpdateMovingTargets()` at `6870`, `AIBattle.CheckCurrentOrderUpdate()` at `8233`, `Regiment.AddOrderCourierline(...)` at `125009`, `Regiment.ProcessOrders()` at `125173`.
- Whiskey orchestrator: `TacticalBattleCoordinator`, `TacticalBattleOrchestrator`, `ArmyOrchestrator`, `CommandTreeRuntime`, `CommandNodeOperationsRuntime`, `TacticalVisionRuntimeAdapter`, `TacticalOperationsLedgerRuntime`, `CommandPostureExecutor`.
- Whiskey patches: `BattleCommandPostureExecutorPatch`, `BattleGroupStancePatch`, `BattleChargeGatePatch`, `TacticalReserveOrderDelayGuardPatch`, `B8CheckUseOfReservesPatch`, `TacticalObserverPatch`.

## File Structure

Create focused pure-model files:

- `src/WhiskeyRealism/Tactical/Operations/DoctrineTargetPoint.cs`  
  Tiny value type for optional X/Z points used by doctrine decisions and executor targets.
- `src/WhiskeyRealism/Tactical/Operations/CommandDoctrineOrder.cs`  
  Per-command-node order contract: node id, role, task, objective, target points, allowed-idle reason, commit/release timestamps, and order validity checks.
- `src/WhiskeyRealism/Tactical/Operations/TacticalBattlefieldPicture.cs`  
  Pure contact/objective confidence model that turns visible enemy, recent fire, objective-chain inputs, and formed/skirmisher classification into objective estimates.
- `src/WhiskeyRealism/Tactical/Operations/TacticalOperationDirector.cs`  
  Pure operation selector and commitment model: single effort, sequential attack, parallel attack, fix-and-flank, defensive network, delay/fallback, soft abort, hard abort.
- `src/WhiskeyRealism/Tactical/Operations/CommandDoctrineAssignment.cs`  
  Pure echelon/order assignment model that maps operation, command tree, battlefield picture, and strategic intent into per-node doctrine orders.
- `src/WhiskeyRealism/Tactical/Operations/DoctrineConsumerDecisions.cs`  
  Pure consumer helpers for stance, charge, reserve release, fallback relief, and artillery bias so Harmony patches read one policy source.

Modify runtime and existing models:

- `src/WhiskeyRealism/Tactical/Operations/TacticalOperationsLedgerModel.cs`
- `src/WhiskeyRealism/Tactical/Operations/CommandNodeOperationalState.cs`
- `src/WhiskeyRealism/Tactical/Operations/CommandPostureExecutor.cs`
- `src/WhiskeyRealism/Tactical/Orchestrator/TacticalVisionRuntimeAdapter.cs`
- `src/WhiskeyRealism/Tactical/Orchestrator/TacticalOperationsLedgerRuntime.cs`
- `src/WhiskeyRealism/Tactical/Orchestrator/CommandNodeOperationsRuntime.cs`
- `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs`
- `src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs`
- `src/WhiskeyRealism/Patches/BattleCommandPostureExecutorPatch.cs`
- `src/WhiskeyRealism/Patches/BattleGroupStancePatch.cs`
- `src/WhiskeyRealism/Patches/BattleChargeGatePatch.cs`
- `src/WhiskeyRealism/Patches/TacticalReserveOrderDelayGuardPatch.cs`
- `src/WhiskeyRealism/Patches/B8CheckUseOfReservesPatch.cs`
- `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs`
- `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- `tests/WhiskeyRealism.Tests/Program.cs`
- `docs/handoff.md`
- `docs/patch-catalog.md`
- `docs/findings.md`
- `MEMORY.md`

---

### Task 0: Worktree, Baseline, And Test Harness Registration

**Files:**
- Modify: none in this task

- [ ] **Step 1: Create or confirm isolated worktree**

Run:

```bash
pwd -P
git status --short --branch
git worktree list
```

Expected:

```text
## main...origin/main [ahead 1]
?? 654890_47.jpg
```

If the session is still in the main checkout, create a feature worktree with the Superpowers worktree skill. Use a branch name like `feature/full-spectrum-tactical-command-doctrine`.

- [ ] **Step 2: Relink game references if the worktree lacks them**

Run:

```bash
test -e refs || ln -s ../../refs refs
test -e refs/Assembly-CSharp.dll
```

Expected: both commands exit `0`.

- [ ] **Step 3: Run baseline harness**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all existing tests pass. At the time this plan was written the active harness had tactical operation tests already registered, so any baseline failure must be investigated before Task 1.

- [ ] **Step 4: Run baseline build**

Run:

```bash
./build.sh
```

Expected: `dist/WhiskeyRealism.dll` is produced.

---

### Task 1: Doctrine Order Contract

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Operations/DoctrineTargetPoint.cs`
- Create: `src/WhiskeyRealism/Tactical/Operations/CommandDoctrineOrder.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Register new production files in the test project**

Add these explicit compile entries near the other `Tactical/Operations` entries:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Operations\DoctrineTargetPoint.cs" Link="Tactical\Operations\DoctrineTargetPoint.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Operations\CommandDoctrineOrder.cs" Link="Tactical\Operations\CommandDoctrineOrder.cs" />
```

- [ ] **Step 2: Write failing doctrine-order tests**

In `tests/WhiskeyRealism.Tests/Program.cs`, add these test registrations beside the other tactical operation tests:

```csharp
("doctrine order sanitizes ids and exposes purpose", DoctrineOrderSanitizesIdsAndPurpose),
("doctrine order requires target for movement tasks", DoctrineOrderRequiresTargetForMovementTasks),
("doctrine order classifies legal idle reasons", DoctrineOrderClassifiesLegalIdleReasons),
```

Add these test methods near the existing command-posture tests:

```csharp
private static void DoctrineOrderSanitizesIdsAndPurpose()
{
    CommandDoctrineOrder order = CommandDoctrineOrder.Create(
        nodeId: "",
        role: CommandNodeRole.MainEffort,
        task: CommandTaskType.AttackObjective,
        objectiveId: "",
        primaryTarget: DoctrineTargetPoint.From(100f, 200f),
        supportTarget: DoctrineTargetPoint.None,
        fallbackTarget: DoctrineTargetPoint.None,
        allowedIdle: DoctrineAllowedIdleReason.None,
        minCommitUntilSeconds: 900f,
        issuedAtSeconds: 12f,
        confidence01: 1.25f,
        reason: "");

    AssertEqual("node-unknown", order.NodeId, "node id");
    AssertEqual("objective-unknown", order.ObjectiveId, "objective id");
    AssertEqual(CommandNodeRole.MainEffort, order.Role, "role");
    AssertEqual(CommandTaskType.AttackObjective, order.Task, "task");
    AssertTrue(order.HasPurpose, "attack order has purpose");
    AssertTrue(order.PrimaryTarget.HasValue, "primary target");
    AssertEqual(1f, order.Confidence01, "confidence clamps high");
    AssertEqual("unspecified", order.Reason, "reason");
}

private static void DoctrineOrderRequiresTargetForMovementTasks()
{
    CommandDoctrineOrder missingTarget = CommandDoctrineOrder.Create(
        "node-1",
        CommandNodeRole.SupportingAttack,
        CommandTaskType.SupportAttack,
        "ridge-a",
        DoctrineTargetPoint.None,
        DoctrineTargetPoint.None,
        DoctrineTargetPoint.None,
        DoctrineAllowedIdleReason.None,
        600f,
        0f,
        0.8f,
        "support");

    CommandDoctrineOrder withTarget = CommandDoctrineOrder.Create(
        "node-1",
        CommandNodeRole.SupportingAttack,
        CommandTaskType.SupportAttack,
        "ridge-a",
        DoctrineTargetPoint.From(50f, 75f),
        DoctrineTargetPoint.None,
        DoctrineTargetPoint.None,
        DoctrineAllowedIdleReason.None,
        600f,
        0f,
        0.8f,
        "support");

    AssertTrue(!missingTarget.HasConcreteMovementTarget, "missing movement target");
    AssertTrue(withTarget.HasConcreteMovementTarget, "movement target");
}

private static void DoctrineOrderClassifiesLegalIdleReasons()
{
    CommandDoctrineOrder reserve = CommandDoctrineOrder.Create(
        "node-r",
        CommandNodeRole.Reserve,
        CommandTaskType.ReserveWait,
        "ridge-a",
        DoctrineTargetPoint.None,
        DoctrineTargetPoint.None,
        DoctrineTargetPoint.None,
        DoctrineAllowedIdleReason.HeldReserve,
        1200f,
        10f,
        0.7f,
        "reserve");

    CommandDoctrineOrder stalled = reserve.WithAllowedIdle(DoctrineAllowedIdleReason.None);

    AssertTrue(reserve.AllowsIdle, "reserve wait is legal idle");
    AssertTrue(!stalled.AllowsIdle, "no idle reason is illegal idle");
}
```

- [ ] **Step 3: Run harness and confirm failure**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile failure naming missing `CommandDoctrineOrder`, `DoctrineTargetPoint`, and `DoctrineAllowedIdleReason`.

- [ ] **Step 4: Create `DoctrineTargetPoint.cs`**

Create the value type with this public surface:

```csharp
namespace WhiskeyRealism.Tactical.Operations
{
    public readonly struct DoctrineTargetPoint
    {
        public DoctrineTargetPoint(bool hasValue, float x, float z)
        {
            HasValue = hasValue && IsFinite(x) && IsFinite(z);
            X = HasValue ? x : 0f;
            Z = HasValue ? z : 0f;
        }

        public bool HasValue { get; }
        public float X { get; }
        public float Z { get; }

        public static DoctrineTargetPoint None { get { return new DoctrineTargetPoint(false, 0f, 0f); } }
        public static DoctrineTargetPoint From(float x, float z) { return new DoctrineTargetPoint(true, x, z); }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
```

- [ ] **Step 5: Create `CommandDoctrineOrder.cs`**

Create the order contract with this public surface and logic:

```csharp
using System;

namespace WhiskeyRealism.Tactical.Operations
{
    public enum DoctrineAllowedIdleReason
    {
        None = 0,
        HeldReserve = 1,
        FormingUp = 2,
        WaitingForCommitWindow = 3,
        DefendingObjective = 4,
        RecoveringAfterFallback = 5,
        PlayerProtected = 6
    }

    public readonly struct CommandDoctrineOrder
    {
        private CommandDoctrineOrder(
            string nodeId,
            CommandNodeRole role,
            CommandTaskType task,
            string objectiveId,
            DoctrineTargetPoint primaryTarget,
            DoctrineTargetPoint supportTarget,
            DoctrineTargetPoint fallbackTarget,
            DoctrineAllowedIdleReason allowedIdle,
            float minCommitUntilSeconds,
            float issuedAtSeconds,
            float confidence01,
            string reason)
        {
            NodeId = SanitizeId(nodeId, "node-unknown");
            Role = role;
            Task = task;
            ObjectiveId = SanitizeId(objectiveId, "objective-unknown");
            PrimaryTarget = primaryTarget;
            SupportTarget = supportTarget;
            FallbackTarget = fallbackTarget;
            AllowedIdle = allowedIdle;
            MinCommitUntilSeconds = Math.Max(0f, minCommitUntilSeconds);
            IssuedAtSeconds = Math.Max(0f, issuedAtSeconds);
            Confidence01 = Clamp01(confidence01);
            Reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim();
        }

        public string NodeId { get; }
        public CommandNodeRole Role { get; }
        public CommandTaskType Task { get; }
        public string ObjectiveId { get; }
        public DoctrineTargetPoint PrimaryTarget { get; }
        public DoctrineTargetPoint SupportTarget { get; }
        public DoctrineTargetPoint FallbackTarget { get; }
        public DoctrineAllowedIdleReason AllowedIdle { get; }
        public float MinCommitUntilSeconds { get; }
        public float IssuedAtSeconds { get; }
        public float Confidence01 { get; }
        public string Reason { get; }

        public bool HasPurpose { get { return Role != CommandNodeRole.Unknown || Task != CommandTaskType.FormUp; } }
        public bool AllowsIdle { get { return AllowedIdle != DoctrineAllowedIdleReason.None; } }
        public bool HasConcreteMovementTarget { get { return PrimaryTarget.HasValue || SupportTarget.HasValue || FallbackTarget.HasValue; } }

        public static CommandDoctrineOrder Create(
            string nodeId,
            CommandNodeRole role,
            CommandTaskType task,
            string objectiveId,
            DoctrineTargetPoint primaryTarget,
            DoctrineTargetPoint supportTarget,
            DoctrineTargetPoint fallbackTarget,
            DoctrineAllowedIdleReason allowedIdle,
            float minCommitUntilSeconds,
            float issuedAtSeconds,
            float confidence01,
            string reason)
        {
            return new CommandDoctrineOrder(nodeId, role, task, objectiveId, primaryTarget, supportTarget, fallbackTarget, allowedIdle, minCommitUntilSeconds, issuedAtSeconds, confidence01, reason);
        }

        public CommandDoctrineOrder WithAllowedIdle(DoctrineAllowedIdleReason reason)
        {
            return new CommandDoctrineOrder(NodeId, Role, Task, ObjectiveId, PrimaryTarget, SupportTarget, FallbackTarget, reason, MinCommitUntilSeconds, IssuedAtSeconds, Confidence01, Reason);
        }

        private static string SanitizeId(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
```

- [ ] **Step 6: Run harness**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 7: Commit**

Run:

```bash
git add src/WhiskeyRealism/Tactical/Operations/DoctrineTargetPoint.cs src/WhiskeyRealism/Tactical/Operations/CommandDoctrineOrder.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: add tactical doctrine order contract"
```

---

### Task 2: Battlefield Picture And Confidence Model

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Operations/TacticalBattlefieldPicture.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Register the new model in the test project**

Add:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Operations\TacticalBattlefieldPicture.cs" Link="Tactical\Operations\TacticalBattlefieldPicture.cs" />
```

- [ ] **Step 2: Write failing picture tests**

Register:

```csharp
("battlefield picture visible formed enemy raises objective confidence", BattlefieldPictureVisibleFormedEnemyRaisesObjectiveConfidence),
("battlefield picture skirmisher contact does not expose main line", BattlefieldPictureSkirmisherContactDoesNotExposeMainLine),
("battlefield picture stale contacts decay but do not vanish instantly", BattlefieldPictureStaleContactsDecay),
```

Add:

```csharp
private static void BattlefieldPictureVisibleFormedEnemyRaisesObjectiveConfidence()
{
    BattlefieldContactInput[] contacts =
    {
        new BattlefieldContactInput("hampton", "ridge-a", TacticalContactKind.FormedLine, 800f, 0f, true, true, 100f, 200f)
    };
    BattlefieldObjectiveInput[] objectives =
    {
        new BattlefieldObjectiveInput("ridge-a", TacticalObjectiveType.Ridge, 0.8f, 100f, 200f, 0.4f, 0.2f, 0.2f)
    };

    BattlefieldPictureSnapshot picture = TacticalBattlefieldPicture.Build(contacts, objectives, nowSeconds: 20f);

    AssertEqual(1, picture.Objectives.Length, "objective count");
    AssertEqual("ridge-a", picture.Objectives[0].ObjectiveId, "objective id");
    AssertTrue(picture.Objectives[0].EnemyStrength > 0f, "enemy strength visible");
    AssertTrue(picture.Objectives[0].Confidence01 >= 0.75f, "confidence from visual formed line");
    AssertTrue(picture.Objectives[0].MainLineExposed, "main line exposed");
}

private static void BattlefieldPictureSkirmisherContactDoesNotExposeMainLine()
{
    BattlefieldContactInput[] contacts =
    {
        new BattlefieldContactInput("screen", "ridge-a", TacticalContactKind.SkirmisherScreen, 80f, 5f, true, false, 100f, 200f)
    };
    BattlefieldObjectiveInput[] objectives =
    {
        new BattlefieldObjectiveInput("ridge-a", TacticalObjectiveType.Ridge, 0.8f, 100f, 200f, 0.4f, 0.2f, 0.2f)
    };

    BattlefieldPictureSnapshot picture = TacticalBattlefieldPicture.Build(contacts, objectives, nowSeconds: 20f);

    AssertTrue(picture.Objectives[0].EnemyStrength > 0f, "screen strength visible");
    AssertTrue(!picture.Objectives[0].MainLineExposed, "screen does not expose main line");
    AssertTrue(picture.Objectives[0].Confidence01 < 0.75f, "screen lower confidence");
}

private static void BattlefieldPictureStaleContactsDecay()
{
    BattlefieldContactInput[] contacts =
    {
        new BattlefieldContactInput("old", "ridge-a", TacticalContactKind.FormedLine, 600f, 0f, true, true, 100f, 200f)
    };
    BattlefieldObjectiveInput[] objectives =
    {
        new BattlefieldObjectiveInput("ridge-a", TacticalObjectiveType.Ridge, 0.8f, 100f, 200f, 0.4f, 0.2f, 0.2f)
    };

    BattlefieldPictureSnapshot fresh = TacticalBattlefieldPicture.Build(contacts, objectives, nowSeconds: 20f);
    BattlefieldPictureSnapshot stale = TacticalBattlefieldPicture.Build(contacts, objectives, nowSeconds: 260f);

    AssertTrue(fresh.Objectives[0].Confidence01 > stale.Objectives[0].Confidence01, "confidence decays");
    AssertTrue(stale.Objectives[0].EnemyStrength > 0f, "stale contact retained");
}
```

- [ ] **Step 3: Run harness and confirm failure**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile failure naming missing battlefield picture types.

- [ ] **Step 4: Create `TacticalBattlefieldPicture.cs`**

Implement:

```csharp
using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Operations
{
    public enum TacticalContactKind
    {
        Unknown = 0,
        SkirmisherScreen = 1,
        FormedLine = 2,
        Artillery = 3,
        CavalryScreen = 4
    }

    public readonly struct BattlefieldContactInput
    {
        public BattlefieldContactInput(string contactId, string objectiveId, TacticalContactKind kind, float estimatedStrength, float lastSeenSeconds, bool visible, bool recentlyFired, float x, float z)
        {
            ContactId = string.IsNullOrWhiteSpace(contactId) ? "contact-unknown" : contactId.Trim();
            ObjectiveId = string.IsNullOrWhiteSpace(objectiveId) ? "objective-unknown" : objectiveId.Trim();
            Kind = kind;
            EstimatedStrength = SanitizeNonNegative(estimatedStrength);
            LastSeenSeconds = SanitizeNonNegative(lastSeenSeconds);
            Visible = visible;
            RecentlyFired = recentlyFired;
            X = SanitizeFinite(x);
            Z = SanitizeFinite(z);
        }

        public string ContactId { get; }
        public string ObjectiveId { get; }
        public TacticalContactKind Kind { get; }
        public float EstimatedStrength { get; }
        public float LastSeenSeconds { get; }
        public bool Visible { get; }
        public bool RecentlyFired { get; }
        public float X { get; }
        public float Z { get; }
    }

    public readonly struct BattlefieldObjectiveInput
    {
        public BattlefieldObjectiveInput(string objectiveId, TacticalObjectiveType type, float value, float x, float z, float terrainStrength, float approachDifficulty, float sourceConfidence)
        {
            ObjectiveId = string.IsNullOrWhiteSpace(objectiveId) ? "objective-unknown" : objectiveId.Trim();
            Type = type;
            Value = Clamp01(value);
            X = SanitizeFinite(x);
            Z = SanitizeFinite(z);
            TerrainStrength = Clamp01(terrainStrength);
            ApproachDifficulty = Clamp01(approachDifficulty);
            SourceConfidence01 = Clamp01(sourceConfidence);
        }

        public string ObjectiveId { get; }
        public TacticalObjectiveType Type { get; }
        public float Value { get; }
        public float X { get; }
        public float Z { get; }
        public float TerrainStrength { get; }
        public float ApproachDifficulty { get; }
        public float SourceConfidence01 { get; }
    }

    public readonly struct BattlefieldObjectiveEstimate
    {
        public BattlefieldObjectiveEstimate(string objectiveId, TacticalObjectiveType type, float enemyStrength, float confidence01, bool mainLineExposed, float value, float x, float z, float terrainStrength, float approachDifficulty)
        {
            ObjectiveId = objectiveId;
            Type = type;
            EnemyStrength = SanitizeNonNegative(enemyStrength);
            Confidence01 = Clamp01(confidence01);
            MainLineExposed = mainLineExposed;
            Value = Clamp01(value);
            X = SanitizeFinite(x);
            Z = SanitizeFinite(z);
            TerrainStrength = Clamp01(terrainStrength);
            ApproachDifficulty = Clamp01(approachDifficulty);
        }

        public string ObjectiveId { get; }
        public TacticalObjectiveType Type { get; }
        public float EnemyStrength { get; }
        public float Confidence01 { get; }
        public bool MainLineExposed { get; }
        public float Value { get; }
        public float X { get; }
        public float Z { get; }
        public float TerrainStrength { get; }
        public float ApproachDifficulty { get; }
    }

    public readonly struct BattlefieldPictureSnapshot
    {
        public BattlefieldPictureSnapshot(BattlefieldObjectiveEstimate[] objectives)
        {
            Objectives = objectives ?? new BattlefieldObjectiveEstimate[0];
        }

        public BattlefieldObjectiveEstimate[] Objectives { get; }
    }

    public static class TacticalBattlefieldPicture
    {
        private const float VisualFreshSeconds = 90f;
        private const float StaleSeconds = 360f;

        public static BattlefieldPictureSnapshot Build(BattlefieldContactInput[] contacts, BattlefieldObjectiveInput[] objectives, float nowSeconds)
        {
            contacts = contacts ?? new BattlefieldContactInput[0];
            objectives = objectives ?? new BattlefieldObjectiveInput[0];

            var results = new List<BattlefieldObjectiveEstimate>();
            for (int i = 0; i < objectives.Length; i++)
            {
                BattlefieldObjectiveInput objective = objectives[i];
                float enemyStrength = 0f;
                float bestConfidence = objective.SourceConfidence01;
                bool mainLineExposed = false;

                for (int j = 0; j < contacts.Length; j++)
                {
                    BattlefieldContactInput contact = contacts[j];
                    if (!string.Equals(contact.ObjectiveId, objective.ObjectiveId, StringComparison.Ordinal)) continue;

                    float age = Math.Max(0f, nowSeconds - contact.LastSeenSeconds);
                    float freshness = Clamp01(1f - (age / StaleSeconds));
                    float baseConfidence = contact.Visible ? 0.8f : 0.35f;
                    if (contact.RecentlyFired) baseConfidence += 0.1f;
                    if (contact.Kind == TacticalContactKind.SkirmisherScreen) baseConfidence -= 0.25f;
                    if (contact.Kind == TacticalContactKind.FormedLine) baseConfidence += age <= VisualFreshSeconds ? 0.1f : 0f;

                    float confidence = Clamp01(baseConfidence * Math.Max(0.15f, freshness));
                    bestConfidence = Math.Max(bestConfidence, confidence);
                    enemyStrength += contact.EstimatedStrength * Math.Max(0.2f, freshness);
                    mainLineExposed = mainLineExposed || contact.Kind == TacticalContactKind.FormedLine && confidence >= 0.55f;
                }

                results.Add(new BattlefieldObjectiveEstimate(objective.ObjectiveId, objective.Type, enemyStrength, bestConfidence, mainLineExposed, objective.Value, objective.X, objective.Z, objective.TerrainStrength, objective.ApproachDifficulty));
            }

            return new BattlefieldPictureSnapshot(results.ToArray());
        }

        private static float SanitizeNonNegative(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return 0f;
            return value;
        }

        private static float SanitizeFinite(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
```

- [ ] **Step 5: Run harness**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

Run:

```bash
git add src/WhiskeyRealism/Tactical/Operations/TacticalBattlefieldPicture.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: model tactical battlefield confidence"
```

---

### Task 3: Operation Director, Commit Windows, And Abort Tiers

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Operations/TacticalOperationDirector.cs`
- Modify: `src/WhiskeyRealism/Tactical/Operations/TacticalOperationsLedgerModel.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Register the director in the test project**

Add:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Operations\TacticalOperationDirector.cs" Link="Tactical\Operations\TacticalOperationDirector.cs" />
```

- [ ] **Step 2: Write failing director tests**

Register:

```csharp
("operation director picks parallel attack only with advantage", OperationDirectorParallelRequiresAdvantage),
("operation director preserves committed push through noise", OperationDirectorPreservesCommittedPush),
("operation director soft aborts before catastrophic loss", OperationDirectorSoftAbortsBeforeCatastrophe),
```

Add:

```csharp
private static void OperationDirectorParallelRequiresAdvantage()
{
    TacticalOperationDirectorInput strong = TacticalOperationDirectorInput.ForTest(
        current: OperationRecord.Noop,
        currentTimeSeconds: 120f,
        ownStrength: 5000f,
        reserveFraction: 0.35f,
        aggression01: 0.8f,
        caution01: 0.1f,
        objectives: new[]
        {
            new BattlefieldObjectiveEstimate("left", TacticalObjectiveType.Ridge, 1000f, 0.85f, true, 0.8f, 100f, 100f, 0.3f, 0.2f),
            new BattlefieldObjectiveEstimate("right", TacticalObjectiveType.Town, 900f, 0.85f, true, 0.9f, 400f, 120f, 0.4f, 0.3f)
        });

    TacticalOperationDirectorDecision decision = TacticalOperationDirector.Decide(strong);
    AssertEqual(TacticalOperationShape.ParallelObjectives, decision.Operation.Shape, "parallel shape");

    TacticalOperationDirectorDecision weak = TacticalOperationDirector.Decide(strong.WithOwnStrength(2200f));
    AssertTrue(weak.Operation.Shape != TacticalOperationShape.ParallelObjectives, "weak force cannot parallel attack");
}

private static void OperationDirectorPreservesCommittedPush()
{
    OperationRecord current = OperationRecord.CreateCommittedForTest(TacticalOperationShape.FixAndFlank, "ridge-a", minCommitUntilSeconds: 1800f);
    TacticalOperationDirectorInput input = TacticalOperationDirectorInput.ForTest(
        current,
        currentTimeSeconds: 600f,
        ownStrength: 3000f,
        reserveFraction: 0.25f,
        aggression01: 0.5f,
        caution01: 0.5f,
        objectives: new[]
        {
            new BattlefieldObjectiveEstimate("ridge-b", TacticalObjectiveType.Ridge, 700f, 0.55f, true, 0.9f, 250f, 300f, 0.2f, 0.2f)
        });

    TacticalOperationDirectorDecision decision = TacticalOperationDirector.Decide(input);
    AssertEqual("ridge-a", decision.Operation.PrimaryObjectiveId, "commit preserves primary objective");
    AssertEqual(TacticalOperationPhase.Committed, decision.Operation.Phase, "phase");
    AssertEqual("commit-window", decision.Reason, "reason");
}

private static void OperationDirectorSoftAbortsBeforeCatastrophe()
{
    OperationRecord current = OperationRecord.CreateCommittedForTest(TacticalOperationShape.SingleMainEffort, "ridge-a", minCommitUntilSeconds: 300f);
    TacticalOperationDirectorInput input = TacticalOperationDirectorInput.ForTest(
        current,
        currentTimeSeconds: 500f,
        ownStrength: 1000f,
        reserveFraction: 0.02f,
        aggression01: 0.4f,
        caution01: 0.7f,
        objectives: new[]
        {
            new BattlefieldObjectiveEstimate("ridge-a", TacticalObjectiveType.Ridge, 2200f, 0.9f, true, 0.8f, 100f, 100f, 0.5f, 0.6f)
        });

    TacticalOperationDirectorDecision decision = TacticalOperationDirector.Decide(input);
    AssertEqual(TacticalOperationPhase.SoftAbort, decision.Operation.Phase, "soft abort phase");
    AssertEqual("odds-collapse", decision.Reason, "reason");
}
```

- [ ] **Step 3: Run harness and confirm failure**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile failure naming missing director types and missing `SoftAbort` or test factory helpers.

- [ ] **Step 4: Extend `TacticalOperationsLedgerModel.cs` without breaking existing callers**

Add `SoftAbort` to `TacticalOperationPhase`. Add factory helpers rather than changing every existing constructor call:

```csharp
public static OperationRecord CreateCommittedForTest(TacticalOperationShape shape, string primaryObjectiveId, float minCommitUntilSeconds)
{
    return new OperationRecord(shape, TacticalOperationPhase.Committed, primaryObjectiveId, minCommitUntilSeconds);
}
```

If `OperationRecord.Noop` does not exist, add:

```csharp
public static OperationRecord Noop
{
    get { return new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Planning, "objective-unknown", 0f); }
}
```

- [ ] **Step 5: Create `TacticalOperationDirector.cs`**

Implement:

```csharp
using System;

namespace WhiskeyRealism.Tactical.Operations
{
    public readonly struct TacticalOperationDirectorInput
    {
        private TacticalOperationDirectorInput(OperationRecord current, float currentTimeSeconds, float ownStrength, float reserveFraction, float aggression01, float caution01, BattlefieldObjectiveEstimate[] objectives)
        {
            Current = current;
            CurrentTimeSeconds = Math.Max(0f, currentTimeSeconds);
            OwnStrength = Math.Max(0f, ownStrength);
            ReserveFraction = Clamp01(reserveFraction);
            Aggression01 = Clamp01(aggression01);
            Caution01 = Clamp01(caution01);
            Objectives = objectives ?? new BattlefieldObjectiveEstimate[0];
        }

        public OperationRecord Current { get; }
        public float CurrentTimeSeconds { get; }
        public float OwnStrength { get; }
        public float ReserveFraction { get; }
        public float Aggression01 { get; }
        public float Caution01 { get; }
        public BattlefieldObjectiveEstimate[] Objectives { get; }

        public static TacticalOperationDirectorInput ForTest(OperationRecord current, float currentTimeSeconds, float ownStrength, float reserveFraction, float aggression01, float caution01, BattlefieldObjectiveEstimate[] objectives)
        {
            return new TacticalOperationDirectorInput(current, currentTimeSeconds, ownStrength, reserveFraction, aggression01, caution01, objectives);
        }

        public TacticalOperationDirectorInput WithOwnStrength(float ownStrength)
        {
            return new TacticalOperationDirectorInput(Current, CurrentTimeSeconds, ownStrength, ReserveFraction, Aggression01, Caution01, Objectives);
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }

    public readonly struct TacticalOperationDirectorDecision
    {
        public TacticalOperationDirectorDecision(OperationRecord operation, string reason)
        {
            Operation = operation;
            Reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;
        }

        public OperationRecord Operation { get; }
        public string Reason { get; }
    }

    public static class TacticalOperationDirector
    {
        public static TacticalOperationDirectorDecision Decide(TacticalOperationDirectorInput input)
        {
            if (input.Current.Phase == TacticalOperationPhase.Committed && input.CurrentTimeSeconds < input.Current.MinimumCommitSeconds)
                return new TacticalOperationDirectorDecision(input.Current, "commit-window");

            BattlefieldObjectiveEstimate primary = PickBestObjective(input.Objectives);
            if (primary.ObjectiveId == null)
                return new TacticalOperationDirectorDecision(OperationRecord.Noop, "no-objective");

            float enemyStrength = Math.Max(1f, primary.EnemyStrength);
            float odds = input.OwnStrength / enemyStrength;
            if (input.Current.Phase == TacticalOperationPhase.Committed && (odds < 0.75f || input.ReserveFraction < 0.05f))
                return new TacticalOperationDirectorDecision(new OperationRecord(input.Current.Shape, TacticalOperationPhase.SoftAbort, input.Current.PrimaryObjectiveId, input.Current.MinimumCommitSeconds), "odds-collapse");

            if (CanParallelAttack(input))
                return new TacticalOperationDirectorDecision(new OperationRecord(TacticalOperationShape.ParallelObjectives, TacticalOperationPhase.Committed, primary.ObjectiveId, input.CurrentTimeSeconds + 1200f), "parallel-advantage");

            TacticalOperationShape shape = odds >= 1.35f ? TacticalOperationShape.SingleMainEffort : TacticalOperationShape.DefensiveNetwork;
            TacticalOperationPhase phase = odds >= 1.35f ? TacticalOperationPhase.Committed : TacticalOperationPhase.Forming;
            return new TacticalOperationDirectorDecision(new OperationRecord(shape, phase, primary.ObjectiveId, input.CurrentTimeSeconds + 900f), "selected");
        }

        private static bool CanParallelAttack(TacticalOperationDirectorInput input)
        {
            if (input.Objectives.Length < 2) return false;
            if (input.ReserveFraction < 0.15f) return false;
            float aggressionDiscount = input.Aggression01 >= 0.7f ? 0.15f : 0f;
            float requiredOdds = 1.65f - aggressionDiscount + input.Caution01 * 0.2f;
            float committedEnemy = 0f;
            int usable = 0;
            for (int i = 0; i < input.Objectives.Length; i++)
            {
                if (!input.Objectives[i].MainLineExposed || input.Objectives[i].Confidence01 < 0.7f) continue;
                committedEnemy += Math.Max(1f, input.Objectives[i].EnemyStrength);
                usable++;
            }
            return usable >= 2 && input.OwnStrength / Math.Max(1f, committedEnemy) >= requiredOdds;
        }

        private static BattlefieldObjectiveEstimate PickBestObjective(BattlefieldObjectiveEstimate[] objectives)
        {
            BattlefieldObjectiveEstimate best = default(BattlefieldObjectiveEstimate);
            float bestScore = -1f;
            for (int i = 0; i < objectives.Length; i++)
            {
                BattlefieldObjectiveEstimate objective = objectives[i];
                float score = objective.Value + objective.Confidence01 - objective.TerrainStrength - objective.ApproachDifficulty;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = objective;
                }
            }
            return best;
        }
    }
}
```

- [ ] **Step 6: Run harness**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 7: Commit**

Run:

```bash
git add src/WhiskeyRealism/Tactical/Operations/TacticalOperationDirector.cs src/WhiskeyRealism/Tactical/Operations/TacticalOperationsLedgerModel.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: direct committed tactical operations"
```

---

### Task 4: Command Doctrine Assignment

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Operations/CommandDoctrineAssignment.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Register assignment model**

Add:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Operations\CommandDoctrineAssignment.cs" Link="Tactical\Operations\CommandDoctrineAssignment.cs" />
```

- [ ] **Step 2: Write failing assignment tests**

Register:

```csharp
("doctrine assignment high odds attack weak point attacks", DoctrineAssignmentHighOddsAttackWeakPointAttacks),
("doctrine assignment reserve gets legal idle", DoctrineAssignmentReserveGetsLegalIdle),
("doctrine assignment fallback guard pulls toward fallback line", DoctrineAssignmentFallbackGuardPullsBack),
```

Add:

```csharp
private static void DoctrineAssignmentHighOddsAttackWeakPointAttacks()
{
    CommandNodeOperationalState[] nodes =
    {
        CommandNodeOperationalState.Create("brigade-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 0f, 0f, 0f)
    };
    OperationRecord operation = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Committed, "ridge-a", 900f);
    BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
    {
        new BattlefieldObjectiveEstimate("ridge-a", TacticalObjectiveType.Ridge, 400f, 0.9f, true, 0.8f, 100f, 250f, 0.2f, 0.2f)
    });

    CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(nodes, operation, picture, ownStrength: 1600f, nowSeconds: 100f);

    AssertEqual(1, orders.Length, "order count");
    AssertEqual(CommandTaskType.AttackObjective, orders[0].Task, "task");
    AssertEqual("ridge-a", orders[0].ObjectiveId, "objective");
    AssertTrue(orders[0].PrimaryTarget.HasValue, "target");
    AssertTrue(!orders[0].AllowsIdle, "attacker should not idle");
}

private static void DoctrineAssignmentReserveGetsLegalIdle()
{
    CommandNodeOperationalState[] nodes =
    {
        CommandNodeOperationalState.Create("reserve-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.Reserve, CommandTaskType.FormUp, 0f, 0f, 0f)
    };
    OperationRecord operation = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Forming, "ridge-a", 900f);
    BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
    {
        new BattlefieldObjectiveEstimate("ridge-a", TacticalObjectiveType.Ridge, 400f, 0.9f, true, 0.8f, 100f, 250f, 0.2f, 0.2f)
    });

    CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(nodes, operation, picture, ownStrength: 1600f, nowSeconds: 100f);

    AssertEqual(CommandTaskType.ReserveWait, orders[0].Task, "task");
    AssertEqual(DoctrineAllowedIdleReason.HeldReserve, orders[0].AllowedIdle, "idle reason");
    AssertTrue(orders[0].AllowsIdle, "reserve may idle");
}

private static void DoctrineAssignmentFallbackGuardPullsBack()
{
    CommandNodeOperationalState[] nodes =
    {
        CommandNodeOperationalState.Create("fallback-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.FallbackGuard, CommandTaskType.FormUp, 200f, 300f, 0f)
    };
    OperationRecord operation = new OperationRecord(TacticalOperationShape.DelayAndFallback, TacticalOperationPhase.SoftAbort, "ridge-a", 900f);
    BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
    {
        new BattlefieldObjectiveEstimate("ridge-a", TacticalObjectiveType.Ridge, 2000f, 0.9f, true, 0.8f, 100f, 250f, 0.2f, 0.2f)
    });

    CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(nodes, operation, picture, ownStrength: 1000f, nowSeconds: 100f);

    AssertEqual(CommandTaskType.FallBackToLine, orders[0].Task, "task");
    AssertTrue(orders[0].FallbackTarget.HasValue, "fallback target");
    AssertEqual(DoctrineAllowedIdleReason.None, orders[0].AllowedIdle, "fallback should move");
}
```

- [ ] **Step 3: Run harness and confirm failure**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile failure for missing `CommandDoctrineAssignment` or missing `CommandNodeOperationalState.Create` overload.

- [ ] **Step 4: Add test factory to `CommandNodeOperationalState.cs` if absent**

Add a test-friendly factory that preserves the existing constructor contract:

```csharp
public static CommandNodeOperationalState Create(string nodeId, CommandEchelonKind echelon, CommandNodeRole role, CommandTaskType task, float x, float z, float facingDegrees)
{
    return new CommandNodeOperationalState(nodeId, echelon, role, task, x, z, facingDegrees, 0f, 0f, string.Empty);
}
```

If the existing constructor signature differs, add this factory with the actual local constructor arguments and keep all values sanitized the same way existing code does.

- [ ] **Step 5: Create `CommandDoctrineAssignment.cs`**

Implement a deterministic assignment model:

```csharp
using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Operations
{
    public static class CommandDoctrineAssignment
    {
        public static CommandDoctrineOrder[] Build(CommandNodeOperationalState[] nodes, OperationRecord operation, BattlefieldPictureSnapshot picture, float ownStrength, float nowSeconds)
        {
            nodes = nodes ?? new CommandNodeOperationalState[0];
            BattlefieldObjectiveEstimate objective = ResolveObjective(operation.PrimaryObjectiveId, picture.Objectives);
            var orders = new List<CommandDoctrineOrder>();

            for (int i = 0; i < nodes.Length; i++)
            {
                CommandNodeOperationalState node = nodes[i];
                CommandTaskType task = ResolveTask(node.Role, operation, objective, ownStrength);
                DoctrineAllowedIdleReason idle = ResolveIdle(node.Role, task, operation);
                DoctrineTargetPoint primary = ResolvePrimaryTarget(task, objective);
                DoctrineTargetPoint fallback = ResolveFallbackTarget(task, node, objective);

                orders.Add(CommandDoctrineOrder.Create(
                    node.NodeId,
                    node.Role,
                    task,
                    objective.ObjectiveId,
                    primary,
                    DoctrineTargetPoint.None,
                    fallback,
                    idle,
                    operation.MinimumCommitSeconds,
                    nowSeconds,
                    objective.Confidence01,
                    "doctrine-assignment"));
            }

            return orders.ToArray();
        }

        private static CommandTaskType ResolveTask(CommandNodeRole role, OperationRecord operation, BattlefieldObjectiveEstimate objective, float ownStrength)
        {
            float odds = ownStrength / Math.Max(1f, objective.EnemyStrength);
            if (operation.Phase == TacticalOperationPhase.SoftAbort || operation.Shape == TacticalOperationShape.DelayAndFallback)
            {
                if (role == CommandNodeRole.FallbackGuard || odds < 0.85f) return CommandTaskType.FallBackToLine;
            }
            if (role == CommandNodeRole.Reserve) return CommandTaskType.ReserveWait;
            if (role == CommandNodeRole.MainEffort && objective.MainLineExposed && objective.Confidence01 >= 0.65f && odds >= 1.25f) return CommandTaskType.AttackObjective;
            if (role == CommandNodeRole.SupportingAttack && objective.MainLineExposed && odds >= 1.15f) return CommandTaskType.SupportAttack;
            if (role == CommandNodeRole.FixingForce && objective.MainLineExposed) return CommandTaskType.FixEnemy;
            if (role == CommandNodeRole.ScreeningForce) return CommandTaskType.Screen;
            return CommandTaskType.FormUp;
        }

        private static DoctrineAllowedIdleReason ResolveIdle(CommandNodeRole role, CommandTaskType task, OperationRecord operation)
        {
            if (role == CommandNodeRole.Reserve && task == CommandTaskType.ReserveWait) return DoctrineAllowedIdleReason.HeldReserve;
            if (task == CommandTaskType.HoldObjective) return DoctrineAllowedIdleReason.DefendingObjective;
            if (operation.Phase == TacticalOperationPhase.Forming && task == CommandTaskType.FormUp) return DoctrineAllowedIdleReason.FormingUp;
            return DoctrineAllowedIdleReason.None;
        }

        private static DoctrineTargetPoint ResolvePrimaryTarget(CommandTaskType task, BattlefieldObjectiveEstimate objective)
        {
            if (task == CommandTaskType.AttackObjective || task == CommandTaskType.SupportAttack || task == CommandTaskType.FixEnemy || task == CommandTaskType.Screen)
                return DoctrineTargetPoint.From(objective.X, objective.Z);
            return DoctrineTargetPoint.None;
        }

        private static DoctrineTargetPoint ResolveFallbackTarget(CommandTaskType task, CommandNodeOperationalState node, BattlefieldObjectiveEstimate objective)
        {
            if (task != CommandTaskType.FallBackToLine) return DoctrineTargetPoint.None;
            float dx = node.X - objective.X;
            float dz = node.Z - objective.Z;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < 1f) return DoctrineTargetPoint.From(node.X, node.Z - 300f);
            return DoctrineTargetPoint.From(node.X + dx / length * 300f, node.Z + dz / length * 300f);
        }

        private static BattlefieldObjectiveEstimate ResolveObjective(string objectiveId, BattlefieldObjectiveEstimate[] objectives)
        {
            objectives = objectives ?? new BattlefieldObjectiveEstimate[0];
            for (int i = 0; i < objectives.Length; i++)
                if (string.Equals(objectives[i].ObjectiveId, objectiveId, StringComparison.Ordinal))
                    return objectives[i];
            if (objectives.Length > 0) return objectives[0];
            return new BattlefieldObjectiveEstimate("objective-unknown", TacticalObjectiveType.UnknownVanillaObjective, 0f, 0f, false, 0f, 0f, 0f, 0f, 0f);
        }
    }
}
```

- [ ] **Step 6: Run harness**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 7: Commit**

Run:

```bash
git add src/WhiskeyRealism/Tactical/Operations/CommandDoctrineAssignment.cs src/WhiskeyRealism/Tactical/Operations/CommandNodeOperationalState.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: assign tactical doctrine orders"
```

---

### Task 5: Runtime Ledger Wiring

**Files:**
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalVisionRuntimeAdapter.cs`
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalOperationsLedgerRuntime.cs`
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/CommandNodeOperationsRuntime.cs`
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs`
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Write failing runtime-storage tests**

Register:

```csharp
("operations ledger stores battlefield picture and doctrine orders", OperationsLedgerStoresPictureAndDoctrineOrders),
("army orchestrator clears doctrine orders when commander off", ArmyOrchestratorClearsDoctrineOrdersWhenOff),
```

Add:

```csharp
private static void OperationsLedgerStoresPictureAndDoctrineOrders()
{
    TacticalOperationsLedgerRuntime ledger = new TacticalOperationsLedgerRuntime();
    BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
    {
        new BattlefieldObjectiveEstimate("ridge-a", TacticalObjectiveType.Ridge, 300f, 0.9f, true, 0.8f, 100f, 200f, 0.2f, 0.2f)
    });
    CommandDoctrineOrder[] orders =
    {
        CommandDoctrineOrder.Create("node-1", CommandNodeRole.MainEffort, CommandTaskType.AttackObjective, "ridge-a", DoctrineTargetPoint.From(100f, 200f), DoctrineTargetPoint.None, DoctrineTargetPoint.None, DoctrineAllowedIdleReason.None, 900f, 100f, 0.9f, "test")
    };

    ledger.StoreDoctrine(picture, orders);

    AssertEqual(1, ledger.CurrentBattlefieldPicture.Objectives.Length, "picture objective count");
    AssertEqual(1, ledger.CurrentDoctrineOrders.Count, "doctrine order count");
    AssertEqual("node-1", ledger.CurrentDoctrineOrders[0].NodeId, "node id");
}

private static void ArmyOrchestratorClearsDoctrineOrdersWhenOff()
{
    ArmyOrchestrator army = new ArmyOrchestrator();
    army.ApplyTacticalCommanderMode(TacticalCommanderMode.Off);

    AssertEqual(0, army.CurrentDoctrineOrders.Count, "orders cleared");
}
```

- [ ] **Step 2: Run harness and confirm failure**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile failure for missing `StoreDoctrine`, `CurrentBattlefieldPicture`, or `CurrentDoctrineOrders`.

- [ ] **Step 3: Extend `TacticalOperationsLedgerRuntime`**

Add state and a tick-cycle writer:

```csharp
private readonly List<CommandDoctrineOrder> _currentDoctrineOrders = new List<CommandDoctrineOrder>();
private BattlefieldPictureSnapshot _currentBattlefieldPicture = new BattlefieldPictureSnapshot(new BattlefieldObjectiveEstimate[0]);

public BattlefieldPictureSnapshot CurrentBattlefieldPicture { get { return _currentBattlefieldPicture; } }
public IReadOnlyList<CommandDoctrineOrder> CurrentDoctrineOrders { get { return _currentDoctrineOrders; } }

public void StoreDoctrine(BattlefieldPictureSnapshot picture, CommandDoctrineOrder[] orders)
{
    _currentBattlefieldPicture = picture;
    _currentDoctrineOrders.Clear();
    if (orders == null) return;
    for (int i = 0; i < orders.Length; i++) _currentDoctrineOrders.Add(orders[i]);
}
```

- [ ] **Step 4: Extend `ArmyOrchestrator` read model**

Add a `CurrentDoctrineOrders` read-only property, forward from the per-side operations ledger, and clear it when `TacticalCommanderMode.Off` is applied. Keep writes inside the existing army tick/update path.

Code shape:

```csharp
public IReadOnlyList<CommandDoctrineOrder> CurrentDoctrineOrders
{
    get { return _operationsLedger == null ? EmptyDoctrineOrders : _operationsLedger.CurrentDoctrineOrders; }
}

private static readonly CommandDoctrineOrder[] EmptyDoctrineOrders = new CommandDoctrineOrder[0];
```

- [ ] **Step 5: Wire runtime tick**

In the existing per-battle orchestrator tick, after command-node states are built:

```csharp
BattlefieldPictureSnapshot picture = TacticalBattlefieldPicture.Build(contactInputs, objectiveInputs, nowSeconds);
TacticalOperationDirectorDecision operation = TacticalOperationDirector.Decide(operationInput);
CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(commandStates, operation.Operation, picture, ownStrength, nowSeconds);
operationsLedger.StoreDoctrine(picture, orders);
```

Use local variables already available in `TacticalBattleCoordinatorRuntime` and `ArmyOrchestrator`. If a runtime value is not available, use an adapter method in `TacticalVisionRuntimeAdapter` rather than reflection inside the pure model.

- [ ] **Step 6: Run harness and build**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected: tests pass and build succeeds.

- [ ] **Step 7: Commit**

Run:

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/TacticalVisionRuntimeAdapter.cs src/WhiskeyRealism/Tactical/Orchestrator/TacticalOperationsLedgerRuntime.cs src/WhiskeyRealism/Tactical/Orchestrator/CommandNodeOperationsRuntime.cs src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: wire tactical doctrine ledger runtime"
```

---

### Task 6: Command Posture Executor Uses Doctrine Targets

**Files:**
- Modify: `src/WhiskeyRealism/Tactical/Operations/CommandPostureExecutor.cs`
- Modify: `src/WhiskeyRealism/Patches/BattleCommandPostureExecutorPatch.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Write failing executor tests**

Register:

```csharp
("posture executor sends attack order to doctrine target", PostureExecutorUsesDoctrineAttackTarget),
("posture executor clears stalled order when interrupted and inactive", PostureExecutorClearsInterruptedInactiveOrder),
("posture executor does not rewrite legal reserve idle", PostureExecutorPreservesLegalReserveIdle),
```

Add:

```csharp
private static void PostureExecutorUsesDoctrineAttackTarget()
{
    CommandDoctrineOrder order = CommandDoctrineOrder.Create("node-1", CommandNodeRole.MainEffort, CommandTaskType.AttackObjective, "ridge-a", DoctrineTargetPoint.From(100f, 200f), DoctrineTargetPoint.None, DoctrineTargetPoint.None, DoctrineAllowedIdleReason.None, 900f, 10f, 0.9f, "attack");
    CommandPhysicalState physical = new CommandPhysicalState(false, false, false, 0, 0f, 0f, 0f, 0f);

    PostureExecutionDecision decision = CommandPostureExecutor.Decide(order, physical, nowSeconds: 20f);

    AssertEqual(PostureExecutionAction.MoveToObjective, decision.Action, "action");
    AssertEqual(PostureExecutionTarget.DoctrinePrimaryTarget, decision.Target, "target");
    AssertTrue(decision.ShouldWriteVanillaState, "write state");
}

private static void PostureExecutorClearsInterruptedInactiveOrder()
{
    CommandDoctrineOrder order = CommandDoctrineOrder.Create("node-1", CommandNodeRole.MainEffort, CommandTaskType.AttackObjective, "ridge-a", DoctrineTargetPoint.From(100f, 200f), DoctrineTargetPoint.None, DoctrineTargetPoint.None, DoctrineAllowedIdleReason.None, 900f, 10f, 0.9f, "attack");
    CommandPhysicalState physical = new CommandPhysicalState(false, true, false, 0, 0f, 0f, 0f, 0f);

    PostureExecutionDecision decision = CommandPostureExecutor.Decide(order, physical, nowSeconds: 20f);

    AssertEqual(PostureExecutionAction.RecoverInterruptedPath, decision.Action, "action");
    AssertTrue(decision.ClearInterruptedPaths, "clear interrupted paths");
}

private static void PostureExecutorPreservesLegalReserveIdle()
{
    CommandDoctrineOrder order = CommandDoctrineOrder.Create("node-r", CommandNodeRole.Reserve, CommandTaskType.ReserveWait, "ridge-a", DoctrineTargetPoint.None, DoctrineTargetPoint.None, DoctrineTargetPoint.None, DoctrineAllowedIdleReason.HeldReserve, 900f, 10f, 0.9f, "reserve");
    CommandPhysicalState physical = new CommandPhysicalState(false, false, false, 0, 0f, 0f, 0f, 0f);

    PostureExecutionDecision decision = CommandPostureExecutor.Decide(order, physical, nowSeconds: 20f);

    AssertEqual(PostureExecutionAction.Hold, decision.Action, "action");
    AssertTrue(!decision.ShouldWriteVanillaState, "no vanilla write");
}
```

- [ ] **Step 2: Run harness and confirm failure**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile failure for missing `CommandPostureExecutor.Decide(CommandDoctrineOrder, ...)`, `DoctrinePrimaryTarget`, or `ShouldWriteVanillaState`.

- [ ] **Step 3: Extend pure executor**

Add `DoctrinePrimaryTarget`, `DoctrineSupportTarget`, and `DoctrineFallbackTarget` to `PostureExecutionTarget`.

Add an overload:

```csharp
public static PostureExecutionDecision Decide(CommandDoctrineOrder order, CommandPhysicalState physical, float nowSeconds)
{
    if (physical.PathInterrupted && !physical.ActiveMove)
        return PostureExecutionDecision.Write(PostureExecutionAction.RecoverInterruptedPath, PostureExecutionTarget.CurrentPosition, "interrupted-inactive", clearInterruptedPaths: true);

    if (order.AllowsIdle && order.Task == CommandTaskType.ReserveWait)
        return PostureExecutionDecision.NoWrite(PostureExecutionAction.Hold, PostureExecutionTarget.CurrentPosition, "legal-idle");

    switch (order.Task)
    {
        case CommandTaskType.AttackObjective:
            return PostureExecutionDecision.Write(PostureExecutionAction.MoveToObjective, PostureExecutionTarget.DoctrinePrimaryTarget, "doctrine-attack", clearInterruptedPaths: false);
        case CommandTaskType.SupportAttack:
        case CommandTaskType.FixEnemy:
        case CommandTaskType.Screen:
            return PostureExecutionDecision.Write(PostureExecutionAction.MoveToObjective, PostureExecutionTarget.DoctrinePrimaryTarget, "doctrine-pressure", clearInterruptedPaths: false);
        case CommandTaskType.FallBackToLine:
            return PostureExecutionDecision.Write(PostureExecutionAction.FallbackToLine, PostureExecutionTarget.DoctrineFallbackTarget, "doctrine-fallback", clearInterruptedPaths: true);
        default:
            return Decide(CommandNodeOperationalState.FromDoctrine(order), physical);
    }
}
```

If `PostureExecutionDecision.Write` and `NoWrite` do not exist, add static factories that preserve the current constructor behavior.

- [ ] **Step 4: Resolve doctrine target in `BattleCommandPostureExecutorPatch`**

Add a lookup that resolves the order for the group command node from `ArmyOrchestrator.CurrentDoctrineOrders`. Extend target resolution:

```csharp
case PostureExecutionTarget.DoctrinePrimaryTarget:
    return TryDoctrineTarget(order.PrimaryTarget, out target);
case PostureExecutionTarget.DoctrineSupportTarget:
    return TryDoctrineTarget(order.SupportTarget, out target);
case PostureExecutionTarget.DoctrineFallbackTarget:
    return TryDoctrineTarget(order.FallbackTarget, out target);
```

Add:

```csharp
private static bool TryDoctrineTarget(DoctrineTargetPoint point, out Vector3 target)
{
    target = default(Vector3);
    if (!point.HasValue) return false;
    target = new Vector3(point.X, 0f, point.Z);
    return true;
}
```

The patch still uses vanilla `BattleUnits.SetWaypoint(...)` or the existing local wrapper. The patch does not mutate Whiskey ledger state.

- [ ] **Step 5: Run harness and build**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected: tests pass and build succeeds.

- [ ] **Step 6: Commit**

Run:

```bash
git add src/WhiskeyRealism/Tactical/Operations/CommandPostureExecutor.cs src/WhiskeyRealism/Patches/BattleCommandPostureExecutorPatch.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: execute tactical doctrine targets"
```

---

### Task 7: Doctrine Consumer Decisions For Stance And Charge

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Operations/DoctrineConsumerDecisions.cs`
- Modify: `src/WhiskeyRealism/Patches/BattleGroupStancePatch.cs`
- Modify: `src/WhiskeyRealism/Patches/BattleChargeGatePatch.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Register consumer model**

Add:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Operations\DoctrineConsumerDecisions.cs" Link="Tactical\Operations\DoctrineConsumerDecisions.cs" />
```

- [ ] **Step 2: Write failing stance/charge tests**

Register:

```csharp
("doctrine consumer attack order permits assault stance", DoctrineConsumerAttackOrderPermitsAssaultStance),
("doctrine consumer reserve order denies charge", DoctrineConsumerReserveOrderDeniesCharge),
("doctrine consumer skirmisher-only contact holds formed regiments", DoctrineConsumerSkirmisherOnlyContactHoldsFormedRegiments),
```

Add:

```csharp
private static void DoctrineConsumerAttackOrderPermitsAssaultStance()
{
    CommandDoctrineOrder order = CommandDoctrineOrder.Create("node-1", CommandNodeRole.MainEffort, CommandTaskType.AttackObjective, "ridge-a", DoctrineTargetPoint.From(100f, 200f), DoctrineTargetPoint.None, DoctrineTargetPoint.None, DoctrineAllowedIdleReason.None, 900f, 0f, 0.9f, "attack");
    DoctrineStanceDecision decision = DoctrineConsumerDecisions.DecideStance(order, enemyMainLineExposed: true, localOdds: 1.6f);

    AssertEqual(DoctrineConsumerAction.Allow, decision.Action, "stance action");
    AssertEqual("doctrine-attack", decision.Reason, "reason");
}

private static void DoctrineConsumerReserveOrderDeniesCharge()
{
    CommandDoctrineOrder order = CommandDoctrineOrder.Create("node-r", CommandNodeRole.Reserve, CommandTaskType.ReserveWait, "ridge-a", DoctrineTargetPoint.None, DoctrineTargetPoint.None, DoctrineTargetPoint.None, DoctrineAllowedIdleReason.HeldReserve, 900f, 0f, 0.9f, "reserve");
    DoctrineChargeDecision decision = DoctrineConsumerDecisions.DecideCharge(order, enemyMainLineExposed: true, localOdds: 3f, targetRouted: false);

    AssertEqual(DoctrineConsumerAction.Deny, decision.Action, "charge action");
    AssertEqual("reserve-held", decision.Reason, "reason");
}

private static void DoctrineConsumerSkirmisherOnlyContactHoldsFormedRegiments()
{
    CommandDoctrineOrder order = CommandDoctrineOrder.Create("node-1", CommandNodeRole.MainEffort, CommandTaskType.AttackObjective, "ridge-a", DoctrineTargetPoint.From(100f, 200f), DoctrineTargetPoint.None, DoctrineTargetPoint.None, DoctrineAllowedIdleReason.None, 900f, 0f, 0.9f, "attack");
    DoctrineChargeDecision decision = DoctrineConsumerDecisions.DecideCharge(order, enemyMainLineExposed: false, localOdds: 3f, targetRouted: false);

    AssertEqual(DoctrineConsumerAction.Observe, decision.Action, "screen-only action");
    AssertEqual("main-line-not-exposed", decision.Reason, "reason");
}
```

- [ ] **Step 3: Run harness and confirm failure**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile failure for missing consumer decision types.

- [ ] **Step 4: Create `DoctrineConsumerDecisions.cs`**

Implement:

```csharp
namespace WhiskeyRealism.Tactical.Operations
{
    public enum DoctrineConsumerAction
    {
        Observe = 0,
        Allow = 1,
        Deny = 2
    }

    public readonly struct DoctrineStanceDecision
    {
        public DoctrineStanceDecision(DoctrineConsumerAction action, string reason)
        {
            Action = action;
            Reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;
        }

        public DoctrineConsumerAction Action { get; }
        public string Reason { get; }
    }

    public readonly struct DoctrineChargeDecision
    {
        public DoctrineChargeDecision(DoctrineConsumerAction action, string reason)
        {
            Action = action;
            Reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;
        }

        public DoctrineConsumerAction Action { get; }
        public string Reason { get; }
    }

    public static class DoctrineConsumerDecisions
    {
        public static DoctrineStanceDecision DecideStance(CommandDoctrineOrder order, bool enemyMainLineExposed, float localOdds)
        {
            if (order.Task == CommandTaskType.ReserveWait) return new DoctrineStanceDecision(DoctrineConsumerAction.Deny, "reserve-held");
            if (order.Task == CommandTaskType.AttackObjective && enemyMainLineExposed && localOdds >= 1.2f) return new DoctrineStanceDecision(DoctrineConsumerAction.Allow, "doctrine-attack");
            if (order.Task == CommandTaskType.FallBackToLine) return new DoctrineStanceDecision(DoctrineConsumerAction.Deny, "fallback");
            return new DoctrineStanceDecision(DoctrineConsumerAction.Observe, "no-doctrine-opinion");
        }

        public static DoctrineChargeDecision DecideCharge(CommandDoctrineOrder order, bool enemyMainLineExposed, float localOdds, bool targetRouted)
        {
            if (order.Task == CommandTaskType.ReserveWait) return new DoctrineChargeDecision(DoctrineConsumerAction.Deny, "reserve-held");
            if (order.Task == CommandTaskType.FallBackToLine) return new DoctrineChargeDecision(DoctrineConsumerAction.Deny, "fallback");
            if (!enemyMainLineExposed && !targetRouted) return new DoctrineChargeDecision(DoctrineConsumerAction.Observe, "main-line-not-exposed");
            if ((order.Task == CommandTaskType.AttackObjective || order.Task == CommandTaskType.SupportAttack) && localOdds >= 1.5f) return new DoctrineChargeDecision(DoctrineConsumerAction.Allow, "doctrine-charge");
            return new DoctrineChargeDecision(DoctrineConsumerAction.Observe, "odds-not-ready");
        }
    }
}
```

- [ ] **Step 5: Retarget `BattleGroupStancePatch`**

Where #45 currently reads role/task or command operation state, resolve the doctrine order first. Use `DoctrineConsumerDecisions.DecideStance(...)` before falling back to existing logic. Log through existing throttled/OnceLog helpers only on signature change:

```csharp
DoctrineStanceDecision doctrine = DoctrineConsumerDecisions.DecideStance(order, enemyMainLineExposed, localOdds);
if (doctrine.Action == DoctrineConsumerAction.Deny) return deny existing stance escalation;
if (doctrine.Action == DoctrineConsumerAction.Allow) return allow existing stance escalation;
```

- [ ] **Step 6: Retarget `BattleChargeGatePatch`**

Where #41 currently reads direct-child role or command operation state, resolve the doctrine order first. Apply:

```csharp
DoctrineChargeDecision doctrine = DoctrineConsumerDecisions.DecideCharge(order, enemyMainLineExposed, localOdds, targetRouted);
if (doctrine.Action == DoctrineConsumerAction.Deny) deny charge with doctrine.Reason;
if (doctrine.Action == DoctrineConsumerAction.Allow) allow charge with doctrine.Reason;
```

Keep W&L ownership checks and player-subordinate protections intact.

- [ ] **Step 7: Run harness and build**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected: tests pass and build succeeds.

- [ ] **Step 8: Commit**

Run:

```bash
git add src/WhiskeyRealism/Tactical/Operations/DoctrineConsumerDecisions.cs src/WhiskeyRealism/Patches/BattleGroupStancePatch.cs src/WhiskeyRealism/Patches/BattleChargeGatePatch.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: route stance and charge through doctrine orders"
```

---

### Task 8: Reserve, Fallback, And Artillery Consumers

**Files:**
- Modify: `src/WhiskeyRealism/Tactical/Operations/DoctrineConsumerDecisions.cs`
- Modify: `src/WhiskeyRealism/Patches/TacticalReserveOrderDelayGuardPatch.cs`
- Modify: `src/WhiskeyRealism/Patches/B8CheckUseOfReservesPatch.cs`
- Modify: `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs`
- Modify: `src/WhiskeyRealism/Tactical/TacticalReservePolicyLedger.cs`
- Modify: `src/WhiskeyRealism/Tactical/TacticalArtilleryDoctrine.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Write failing reserve/fallback tests**

Register:

```csharp
("doctrine reserve releases to support endangered main effort", DoctrineReserveReleasesToEndangeredMainEffort),
("doctrine fallback relief beats stale held order", DoctrineFallbackReliefBeatsStaleHeldOrder),
("doctrine artillery supports committed main effort", DoctrineArtillerySupportsCommittedMainEffort),
```

Add:

```csharp
private static void DoctrineReserveReleasesToEndangeredMainEffort()
{
    CommandDoctrineOrder reserve = CommandDoctrineOrder.Create("reserve-1", CommandNodeRole.Reserve, CommandTaskType.ReserveWait, "ridge-a", DoctrineTargetPoint.None, DoctrineTargetPoint.From(100f, 200f), DoctrineTargetPoint.None, DoctrineAllowedIdleReason.HeldReserve, 900f, 0f, 0.9f, "reserve");
    DoctrineReserveDecision decision = DoctrineConsumerDecisions.DecideReserve(reserve, mainEffortOdds: 0.8f, reserveFraction: 0.3f, currentTimeSeconds: 1000f);

    AssertEqual(DoctrineConsumerAction.Allow, decision.Action, "reserve release");
    AssertEqual(CommandTaskType.ReleaseReserve, decision.Task, "task");
    AssertEqual("main-effort-under-pressure", decision.Reason, "reason");
}

private static void DoctrineFallbackReliefBeatsStaleHeldOrder()
{
    CommandDoctrineOrder fallback = CommandDoctrineOrder.Create("line-1", CommandNodeRole.FallbackGuard, CommandTaskType.FallBackToLine, "ridge-a", DoctrineTargetPoint.None, DoctrineTargetPoint.None, DoctrineTargetPoint.From(0f, 100f), DoctrineAllowedIdleReason.None, 900f, 0f, 0.9f, "fallback");
    DoctrineReserveDecision decision = DoctrineConsumerDecisions.DecideReserve(fallback, mainEffortOdds: 0.6f, reserveFraction: 0.2f, currentTimeSeconds: 1000f);

    AssertEqual(DoctrineConsumerAction.Allow, decision.Action, "relief");
    AssertEqual(CommandTaskType.FallBackToLine, decision.Task, "task");
    AssertEqual("fallback-relief", decision.Reason, "reason");
}

private static void DoctrineArtillerySupportsCommittedMainEffort()
{
    CommandDoctrineOrder attack = CommandDoctrineOrder.Create("node-1", CommandNodeRole.MainEffort, CommandTaskType.AttackObjective, "ridge-a", DoctrineTargetPoint.From(100f, 200f), DoctrineTargetPoint.None, DoctrineTargetPoint.None, DoctrineAllowedIdleReason.None, 900f, 0f, 0.9f, "attack");
    DoctrineArtilleryDecision decision = DoctrineConsumerDecisions.DecideArtillery(attack, enemyMainLineExposed: true, friendlyCloseRange: false);

    AssertEqual(DoctrineConsumerAction.Allow, decision.Action, "artillery");
    AssertEqual("support-main-effort", decision.Reason, "reason");
}
```

- [ ] **Step 2: Run harness and confirm failure**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile failure for missing reserve/artillery decision types.

- [ ] **Step 3: Extend `DoctrineConsumerDecisions.cs`**

Add:

```csharp
public readonly struct DoctrineReserveDecision
{
    public DoctrineReserveDecision(DoctrineConsumerAction action, CommandTaskType task, string reason)
    {
        Action = action;
        Task = task;
        Reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;
    }

    public DoctrineConsumerAction Action { get; }
    public CommandTaskType Task { get; }
    public string Reason { get; }
}

public readonly struct DoctrineArtilleryDecision
{
    public DoctrineArtilleryDecision(DoctrineConsumerAction action, string reason)
    {
        Action = action;
        Reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;
    }

    public DoctrineConsumerAction Action { get; }
    public string Reason { get; }
}

public static DoctrineReserveDecision DecideReserve(CommandDoctrineOrder order, float mainEffortOdds, float reserveFraction, float currentTimeSeconds)
{
    if (order.Task == CommandTaskType.FallBackToLine)
        return new DoctrineReserveDecision(DoctrineConsumerAction.Allow, CommandTaskType.FallBackToLine, "fallback-relief");
    if (order.Role == CommandNodeRole.Reserve && order.Task == CommandTaskType.ReserveWait && mainEffortOdds < 0.9f && reserveFraction >= 0.15f)
        return new DoctrineReserveDecision(DoctrineConsumerAction.Allow, CommandTaskType.ReleaseReserve, "main-effort-under-pressure");
    if (order.Role == CommandNodeRole.Reserve)
        return new DoctrineReserveDecision(DoctrineConsumerAction.Deny, CommandTaskType.ReserveWait, "reserve-held");
    return new DoctrineReserveDecision(DoctrineConsumerAction.Observe, order.Task, "no-doctrine-opinion");
}

public static DoctrineArtilleryDecision DecideArtillery(CommandDoctrineOrder order, bool enemyMainLineExposed, bool friendlyCloseRange)
{
    if (friendlyCloseRange) return new DoctrineArtilleryDecision(DoctrineConsumerAction.Deny, "friendly-close-range");
    if (enemyMainLineExposed && (order.Task == CommandTaskType.AttackObjective || order.Task == CommandTaskType.SupportAttack || order.Task == CommandTaskType.FixEnemy))
        return new DoctrineArtilleryDecision(DoctrineConsumerAction.Allow, "support-main-effort");
    return new DoctrineArtilleryDecision(DoctrineConsumerAction.Observe, "no-doctrine-opinion");
}
```

- [ ] **Step 4: Retarget reserve and fallback patches**

In `TacticalReserveOrderDelayGuardPatch` and `B8CheckUseOfReservesPatch`, resolve the current doctrine order for the group and call `DoctrineConsumerDecisions.DecideReserve(...)`.

Behavior:

- `Deny` prevents stale reserve movement and preserves the held-reserve order.
- `Allow + ReleaseReserve` lets vanilla reserve support proceed or biases the existing #59 reserve assignment.
- `Allow + FallBackToLine` clears dead/stalled held orders when `pathInterrupted == true` and `activeMove == false`.
- `Observe` keeps existing shipped behavior.

- [ ] **Step 5: Retarget artillery doctrine**

In `TacticalArtilleryDoctrine` and the patch/runtime adapter that calls it, add an optional doctrine decision input. Use `DoctrineConsumerDecisions.DecideArtillery(...)` so artillery supports the current committed main effort when enemy main line is exposed and friendly close-range risk is false.

- [ ] **Step 6: Run harness and build**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected: tests pass and build succeeds.

- [ ] **Step 7: Commit**

Run:

```bash
git add src/WhiskeyRealism/Tactical/Operations/DoctrineConsumerDecisions.cs src/WhiskeyRealism/Patches/TacticalReserveOrderDelayGuardPatch.cs src/WhiskeyRealism/Patches/B8CheckUseOfReservesPatch.cs src/WhiskeyRealism/Patches/TacticalObserverPatch.cs src/WhiskeyRealism/Tactical/TacticalReservePolicyLedger.cs src/WhiskeyRealism/Tactical/TacticalArtilleryDoctrine.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: route reserves fallback and artillery through doctrine"
```

---

### Task 9: Runtime Telemetry, Config, And Log Discipline

**Files:**
- Modify: `src/WhiskeyRealism/Plugin.cs`
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalOperationsLedgerRuntime.cs`
- Modify: `src/WhiskeyRealism/Patches/BattleCommandPostureExecutorPatch.cs`
- Modify: `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Write failing telemetry tests**

Register:

```csharp
("doctrine telemetry throttles repeated signatures", DoctrineTelemetryThrottlesRepeatedSignatures),
("tactical commander active remains configured default", TacticalCommanderActiveRemainsDefault),
```

Add:

```csharp
private static void DoctrineTelemetryThrottlesRepeatedSignatures()
{
    var emittedAt = new System.Collections.Generic.Dictionary<string, float>();
    AssertTrue(TacticalOperationsTelemetry.ShouldEmitChangedAfterInterval(emittedAt, "doctrine:node-1", "a", 0f, 15f), "first emits");
    AssertTrue(!TacticalOperationsTelemetry.ShouldEmitChangedAfterInterval(emittedAt, "doctrine:node-1", "a", 1f, 15f), "same throttled");
    AssertTrue(TacticalOperationsTelemetry.ShouldEmitChangedAfterInterval(emittedAt, "doctrine:node-1", "b", 2f, 15f), "changed emits");
}

private static void TacticalCommanderActiveRemainsDefault()
{
    AssertEqual(TacticalCommanderMode.Active, TacticalCommanderMode.Active, "active mode expected by plan");
}
```

- [ ] **Step 2: Run harness and confirm failure if telemetry helper is missing**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: pass if the telemetry helper already exists; otherwise compile failure naming the missing helper.

- [ ] **Step 3: Keep config default active and document precedence in code comments**

In `Plugin.cs`, ensure Tactical Commander Mode default remains active. Add a comment at the config entry:

```csharp
// User-approved default for the full doctrine feature: Active. Existing config files still take precedence over this default.
```

Do not delete `MonitorOnly`; use it for no-write diagnostics and smoke comparison.

- [ ] **Step 4: Gate telemetry output**

For every new doctrine executor line, log only on signature change or interval:

```csharp
if (TacticalOperationsTelemetry.ShouldEmitChangedAfterInterval(_emittedAt, key, signature, nowSeconds, 15f))
{
    Plugin.Log.LogInfo("[TacticalDoctrine] " + signature);
}
```

Summary lines may emit on the existing per-tick summary cadence. Per-node lines must not emit every tick with an unchanged signature.

- [ ] **Step 5: Run harness and build**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected: tests pass and build succeeds.

- [ ] **Step 6: Commit**

Run:

```bash
git add src/WhiskeyRealism/Plugin.cs src/WhiskeyRealism/Tactical/Orchestrator/TacticalOperationsLedgerRuntime.cs src/WhiskeyRealism/Patches/BattleCommandPostureExecutorPatch.cs src/WhiskeyRealism/Patches/TacticalObserverPatch.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: throttle tactical doctrine telemetry"
```

---

### Task 10: In-Game Smoke, Living Docs, Archive Prep

**Files:**
- Modify: `docs/handoff.md`
- Modify: `docs/patch-catalog.md`
- Modify: `docs/findings.md`
- Modify: `MEMORY.md`
- Modify after smoke only: `README.md`

- [ ] **Step 1: Run final harness and build**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected: all tests pass and build succeeds.

- [ ] **Step 2: Deploy and hash-verify the DLL**

Use `whiskey-dll-deploy-smoke`.

Run:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: both SHA-256 hashes match.

- [ ] **Step 3: Smoke with a current battle**

Start or resume a battle with Tactical Commander Mode active. Tail:

```bash
tail -f "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Expected smoke markers:

```text
[TacticalDoctrine]
[TacticalCommandPosture]
[TacticalOperations]
```

Reject smoke if any repeated exception appears, if the log grows with unchanged per-node lines every tick, or if unintended player-chain battle-state retasking appears while `GameVars.ai_vs_ai` is false. Player-facing W&L current-order popups are owned by #62.

- [ ] **Step 4: Smoke the Hampton-style case**

Use the scenario that previously showed Hampton's Legion and nearby brigades failing to attack or fall back.

Required observations:

- Visible formed enemy contact raises objective confidence above `0.65`.
- A high-odds committed attack order maps to `AttackObjective` or `SupportAttack`, not `HoldObjective`.
- A reserve order is idle only when `DoctrineAllowedIdleReason.HeldReserve` is present.
- A stalled command with `pathInterrupted=True` and `activeMove=False` receives `RecoverInterruptedPath` or `FallBackToLine`.
- A skirmisher-only contact does not trigger a formed-regiment charge unless main-line exposure or routed-target evidence exists.

- [ ] **Step 5: Update living docs**

Update `docs/handoff.md` with:

```markdown
## Full-Spectrum Tactical Command Doctrine

Status: implemented and deployed after hash verification on <DATE>.
Default mode: Tactical Commander Mode Active; existing config files still override C# defaults.
Smoke evidence: <DLL SHA-256>, log markers <markers>, scenario notes <short note>.
Primary anchors: AIBattle.UpdateAITasks, AIBattle.CheckGlobalAIStrategy, BattleUnits.SetWaypoint, BattleUnits.GetHierarchyTree, Regiment.ProcessOrders.
```

Update `docs/patch-catalog.md` entries for touched patches:

```markdown
- #61 BattleCommandPostureExecutorPatch: now consumes `CommandDoctrineOrder` targets from the per-battle doctrine ledger.
- #45 BattleGroupStancePatch: now consults doctrine stance decisions before legacy role/task fallback.
- #41 BattleChargeGatePatch: now consults doctrine charge decisions before legacy role/task fallback.
- B8 / reserve guard: now consult doctrine reserve/fallback decisions for held reserve, relief, and interrupted-path recovery.
```

Update `docs/findings.md` with any new decompile coordinates verified while implementing. Keep unverified bridge/ford/road/town typed enumeration marked unverified unless implementation proves it.

Update `MEMORY.md` with a short pointer to the new shipped doctrine surface and smoke evidence.

- [ ] **Step 6: Run doc hygiene**

Run:

```bash
rg "[T]BD|[T]ODO|implement [l]ater|fill in [d]etails|Similar to [T]ask" docs/superpowers/plans/2026-05-11-full-spectrum-tactical-command-doctrine-implementation-plan.md docs/handoff.md docs/patch-catalog.md docs/findings.md MEMORY.md
git diff --check
git status --short
```

Expected: no placeholder hits from the plan or changed docs; `git diff --check` exits `0`; only intended files are modified.

- [ ] **Step 7: Commit docs and smoke closeout**

Run:

```bash
git add docs/handoff.md docs/patch-catalog.md docs/findings.md MEMORY.md README.md
git commit -m "docs: close full tactical doctrine smoke"
```

---

## Final Verification Before Merge Or Push

- [ ] Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
git log --oneline -10
git status --short --branch
```

- [ ] Confirm:

```text
All harness tests pass.
Build succeeds.
Deployed DLL hash matches dist DLL hash.
No repeated exceptions in BepInEx LogOutput.log.
No unintended player-chain battle-state retasking while ai_vs_ai is false.
Doctrine logs are throttled.
Active mode is documented as user-approved default.
```

- [ ] Merge and push only after the user asks or the active workflow requires it:

```bash
git checkout main
git merge --ff-only feature/full-spectrum-tactical-command-doctrine
git push origin main
```

Expected: `main` fast-forwards and `origin/main` contains the full task series.
