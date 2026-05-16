# W&L Player Order Doctrine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Status as of 2026-05-14:** tactical #62 `PlayerSubordinateOrderPatch`
> is implemented in the working tree behind default-off `Enable Player Order
> Doctrine`, built, deployed, and hash-verified in DLL
> `cfdb9018bc0cb7c0fcb7ba1e28acac0b1b119243856ef3a027716f8b9b930e75`
> (1245184 bytes; 1075 PASS). This plan remains active only for focused enabled
> #62 smoke and final archive closeout. Current runtime truth lives in
> [`docs/tactical-operations-ledger.md`](../../tactical-operations-ledger.md)
> and [`docs/patch-catalog.md`](../../patch-catalog.md).

**Goal:** Implement the W&L player-order doctrine as one cohesive, default-off feature that lets Whiskey translate existing campaign and tactical doctrine into player-facing vanilla W&L orders without spamming, overriding vanilla transition orders, or crossing campaign/tactical scopes unsafely.

**Status (2026-05-15):** implemented and merged to `main`, console harness `1075 PASS / 0 FAIL`, `./build.sh` clean, local and deployed DLLs hash-match SHA-256 `cfdb9018bc0cb7c0fcb7ba1e28acac0b1b119243856ef3a027716f8b9b930e75` (1245184 bytes). Fresh W&L player-subordinate runtime smoke is still pending, so this plan remains active and is not archived. Current behavior, config, smoke checklist, and rollback live in `docs/wl-player-order-doctrine.md`.

**Architecture:** Add a pure `Tactical/PlayerOrders/` doctrine layer for intent, priority, mapping, dedupe, provenance, and diagnostics decisions; add one runtime adapter that reads vanilla objects through safe reflection; add one `PlayerSubordinateOrderPatch` around `AIBattle.UpdateDLCPlayerOrders()`; and extend `WlStrategicOrderBridge` with the same signature/provenance discipline for campaign calls already routed through the bridge.

**Tech Stack:** C# netstandard2.1, BepInEx 5.4.x, HarmonyX, existing console harness under `tests/WhiskeyRealism.Tests`, vanilla anchors from `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`, no new packages.

---

## Source Anchors

Read these before editing:

- Spec: `docs/superpowers/specs/2026-05-12-wl-player-order-doctrine-design.md`
- Patch rules: `src/WhiskeyRealism/Patches/AGENTS.md`
- Tactical pure/runtime split: `src/WhiskeyRealism/Tactical/AGENTS.md`
- Test harness rules: `tests/WhiskeyRealism.Tests/AGENTS.md`
- Vanilla tactical writer: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:8233` `AIBattle.CheckCurrentOrderUpdate(...)`
- Vanilla tactical player-order cadence: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:5682` through `5684`, `UpdateDLCPlayerOrders()` at `6747`
- Vanilla removal/transition self-writes: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:6798`, `6804`, `6808`, `6841`
- Vanilla dedupe branch: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:8640` through `8646`
- Existing tactical config pattern: `src/WhiskeyRealism/Plugin.cs`
- Existing tactical snapshot patch pattern: `src/WhiskeyRealism/Patches/BattleReserveCommitGatePatch.cs`
- Existing command-posture execution patch pattern: `src/WhiskeyRealism/Patches/BattleCommandPostureExecutorPatch.cs`
- Existing campaign W&L bridge: `src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs`
- Orchestrator accessors: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinator.cs`, `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleOrchestrator.cs`, `src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs`

## Worktree Gate

- [ ] Run `git status --short --branch` and `git log --oneline -5`.
- [ ] If executing this plan from the main checkout, create an isolated worktree before code edits:

```bash
git worktree add ../whiskey-realism-mod-player-orders -b feature/wl-player-order-doctrine
cd ../whiskey-realism-mod-player-orders
ln -s ../whiskey-realism-mod/refs refs
```

- [ ] If already in an isolated worktree, confirm `refs` exists and points to the main repo symlink set.
- [ ] Keep the existing untracked `654890_47.jpg` out of all staging and commits if it is present in the main checkout.

## Non-Goals

- Do not patch `AICampaign.MoveUnitTo`, `BattleUnits.SetWaypoint`, or `AIBattle.CheckCurrentOrderUpdate`.
- Do not call `AIBattle.CheckCurrentOrderUpdate(..., calledfromcampaign: true)` from the tactical Postfix.
- Do not make tactical doctrine the authoritative writer over vanilla. The tactical patch requests an order only when Whiskey preflight says vanilla is expected to accept it.
- Do not persist player-order caches into saves.
- Do not enable player-order writes by default. Diagnostics may be default-on.
- Do not mutate tactical orchestrator state from Harmony patches.

## File Map

Create:

- `src/WhiskeyRealism/Tactical/PlayerOrders/PlayerOrderContracts.cs`
- `src/WhiskeyRealism/Tactical/PlayerOrders/PlayerOrderPriority.cs`
- `src/WhiskeyRealism/Tactical/PlayerOrders/PlayerOrderVanillaMapper.cs`
- `src/WhiskeyRealism/Tactical/PlayerOrders/PlayerOrderDedupe.cs`
- `src/WhiskeyRealism/Tactical/PlayerOrders/PlayerOrderComposer.cs`
- `src/WhiskeyRealism/Tactical/PlayerOrders/PlayerOrderDiagnostics.cs`
- `src/WhiskeyRealism/Tactical/PlayerOrders/PlayerOrderRuntimeAdapter.cs`
- `src/WhiskeyRealism/Tactical/PlayerOrders/PlayerOrderVanillaScene.cs`
- `src/WhiskeyRealism/Patches/PlayerSubordinateOrderPatch.cs`

Modify:

- `src/WhiskeyRealism/Plugin.cs`
- `src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs`
- `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- `tests/WhiskeyRealism.Tests/Program.cs`
- `docs/patch-catalog.md`
- `docs/handoff.md`
- `MEMORY.md`

Only include pure files in the test project. Exclude `PlayerOrderRuntimeAdapter.cs` and `PlayerSubordinateOrderPatch.cs` from the console harness because they touch Unity, Harmony, or vanilla runtime objects. `PlayerOrderVanillaScene.cs` is the pure scene-active/classification helper and is included in the console harness.

## Task 0: Baseline And Campaign Caller Audit

- [ ] Run the current tests before changing code:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: process exits `0`, all registered tests print pass status, and no unhandled exception appears.

- [ ] Run a clean build before changing code:

```bash
./build.sh
```

Expected: `WhiskeyRealism.dll` is produced under `dist/` with `0 Error(s)`.

- [ ] Audit strategic W&L call sites:

```bash
rg -n "TryIssue|ClassifyOnly|CheckCurrentOrderUpdate|MoveUnitTo|givenorder|DLC_WL" src docs
```

Expected: every shipped strategic source that intentionally emits player-facing W&L orders is either already routed through `WlStrategicOrderBridge.TryIssue` / `ClassifyOnly` or is listed in the implementation notes as converted by this patch. Do not proceed with campaign signature-cache work until this audit is closed.

- [ ] Re-open the vanilla anchors and confirm they still match the spec:

```bash
sed -n '5680,5688p' /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
sed -n '6740,6860p' /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
sed -n '8230,8668p' /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
```

Expected: `UpdateDLCPlayerOrders()` is still called when `microaitaskupdatecycle == 28`; `CheckRemovalOfOrders()` still self-issues type `15`, `13`, `14`, and `11`; `CheckCurrentOrderUpdate(...)` still contains the dedupe rules at `8640` through `8646`.

## Task 1: Pure Contracts, Priority, And Mapper

### Tests First

- [ ] Add pure files to `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` after the tactical compile entries:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\PlayerOrders\PlayerOrderContracts.cs" Link="Tactical\PlayerOrders\PlayerOrderContracts.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Tactical\PlayerOrders\PlayerOrderPriority.cs" Link="Tactical\PlayerOrders\PlayerOrderPriority.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Tactical\PlayerOrders\PlayerOrderVanillaMapper.cs" Link="Tactical\PlayerOrders\PlayerOrderVanillaMapper.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Tactical\PlayerOrders\PlayerOrderDedupe.cs" Link="Tactical\PlayerOrders\PlayerOrderDedupe.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Tactical\PlayerOrders\PlayerOrderComposer.cs" Link="Tactical\PlayerOrders\PlayerOrderComposer.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Tactical\PlayerOrders\PlayerOrderDiagnostics.cs" Link="Tactical\PlayerOrders\PlayerOrderDiagnostics.cs" />
```

- [ ] Register these tests in `tests/WhiskeyRealism.Tests/Program.cs`:

```csharp
("player order priority ranks retreat above fallback", PlayerOrderPriorityRanksRetreatAboveFallback),
("player order mapper keeps type fifteen retreat only", PlayerOrderMapperKeepsTypeFifteenRetreatOnly),
("player order mapper maps fallback to type twelve", PlayerOrderMapperMapsFallbackToTypeTwelve),
("player order active priority is conservative for ambiguous vanilla types", PlayerOrderActivePriorityIsConservativeForAmbiguousVanillaTypes),
```

- [ ] Add test methods:

```csharp
static void PlayerOrderPriorityRanksRetreatAboveFallback()
{
    AssertTrue(
        PlayerOrderPriority.ForIntent(PlayerOrderIntent.RetreatToExit) >
        PlayerOrderPriority.ForIntent(PlayerOrderIntent.FallBackToLine),
        "retreat-to-exit must outrank fallback");
}

static void PlayerOrderMapperKeepsTypeFifteenRetreatOnly()
{
    var retreat = PlayerOrderVanillaMapper.Map(PlayerOrderIntent.RetreatToExit);
    var fallback = PlayerOrderVanillaMapper.Map(PlayerOrderIntent.FallBackToLine);
    AssertEqual(15, retreat.Type, "retreat-to-exit vanilla type");
    AssertTrue(fallback.Type != 15, "fallback must not map to vanilla retreat type 15");
}

static void PlayerOrderMapperMapsFallbackToTypeTwelve()
{
    var mapped = PlayerOrderVanillaMapper.Map(PlayerOrderIntent.FallBackToLine);
    AssertEqual(12, mapped.Type, "fallback vanilla type");
}

static void PlayerOrderActivePriorityIsConservativeForAmbiguousVanillaTypes()
{
    AssertEqual(80, PlayerOrderPriority.ForActiveVanillaType(12, PlayerOrderScope.Tactical, PlayerOrderProvenance.Vanilla), "vanilla type 12 active priority");
    AssertEqual(90, PlayerOrderPriority.ForActiveVanillaType(7, PlayerOrderScope.Campaign, PlayerOrderProvenance.Vanilla), "campaign type 7 active priority");
    AssertEqual(100, PlayerOrderPriority.ForActiveVanillaType(15, PlayerOrderScope.Tactical, PlayerOrderProvenance.Vanilla), "active type 15 priority");
}
```

The first test run should fail to compile until the new pure types exist.

### Implementation

- [ ] Create `PlayerOrderContracts.cs` with compact immutable contracts:

```csharp
namespace WhiskeyRealism.Tactical.PlayerOrders
{
    internal enum PlayerOrderScope { Tactical = 0, Campaign = 1 }
    internal enum PlayerOrderProvenance { Unknown = 0, Vanilla = 1, WhiskeyTactical = 2, WhiskeyCampaign = 3 }

    internal enum PlayerOrderIntent
    {
        None = 0,
        BuildSupplyDepot,
        BuildFort,
        DefendCapital,
        AdvanceToAssemblyArea,
        ProbeObjective,
        AttackObjective,
        SupportMainEffort,
        HoldObjective,
        FallBackToLine,
        RetreatToExit,
        RecoverFromCombat,
        ClearHoldTransition
    }

    internal readonly struct PlayerOrderPoint
    {
        public readonly float X;
        public readonly float Z;
        public PlayerOrderPoint(float x, float z) { X = x; Z = z; }
        public int BucketX(int bucketMeters) => bucketMeters <= 0 ? 0 : (int)System.Math.Round(X / bucketMeters);
        public int BucketZ(int bucketMeters) => bucketMeters <= 0 ? 0 : (int)System.Math.Round(Z / bucketMeters);
    }

    internal readonly struct PlayerOrderCandidate
    {
        public readonly PlayerOrderScope Scope;
        public readonly PlayerOrderIntent Intent;
        public readonly int VanillaType;
        public readonly int Priority;
        public readonly int UnitKey;
        public readonly PlayerOrderPoint Position;
        public readonly float RotationY;
        public readonly string Reason;
        public PlayerOrderCandidate(PlayerOrderScope scope, PlayerOrderIntent intent, int vanillaType, int priority, int unitKey, PlayerOrderPoint position, float rotationY, string reason)
        {
            Scope = scope;
            Intent = intent;
            VanillaType = vanillaType;
            Priority = priority;
            UnitKey = unitKey;
            Position = position;
            RotationY = rotationY;
            Reason = reason ?? string.Empty;
        }
    }

    internal readonly struct PlayerOrderActiveSnapshot
    {
        public readonly bool HasOrder;
        public readonly PlayerOrderScope Scope;
        public readonly PlayerOrderProvenance Provenance;
        public readonly int VanillaType;
        public readonly int UnitKey;
        public readonly int GivenOrderSession;
        public readonly PlayerOrderPoint Position;
        public PlayerOrderActiveSnapshot(bool hasOrder, PlayerOrderScope scope, PlayerOrderProvenance provenance, int vanillaType, int unitKey, int givenOrderSession, PlayerOrderPoint position)
        {
            HasOrder = hasOrder;
            Scope = scope;
            Provenance = provenance;
            VanillaType = vanillaType;
            UnitKey = unitKey;
            GivenOrderSession = givenOrderSession;
            Position = position;
        }
    }
}
```

- [ ] Create `PlayerOrderPriority.cs` with the spec priority table:

```csharp
internal static int ForIntent(PlayerOrderIntent intent)
{
    switch (intent)
    {
        case PlayerOrderIntent.RetreatToExit:
            return 100;
        case PlayerOrderIntent.FallBackToLine:
            return 90;
        case PlayerOrderIntent.HoldObjective:
            return 80;
        case PlayerOrderIntent.SupportMainEffort:
            return 70;
        case PlayerOrderIntent.AttackObjective:
        case PlayerOrderIntent.AdvanceToAssemblyArea:
            return 60;
        case PlayerOrderIntent.ProbeObjective:
            return 50;
        case PlayerOrderIntent.BuildSupplyDepot:
        case PlayerOrderIntent.BuildFort:
        case PlayerOrderIntent.DefendCapital:
            return 30;
        case PlayerOrderIntent.RecoverFromCombat:
        case PlayerOrderIntent.ClearHoldTransition:
            return 20;
        default:
            return 0;
    }
}
```

- [ ] Add `ForActiveVanillaType(int type, PlayerOrderScope scope, PlayerOrderProvenance provenance)` using conservative inference:
  - `15` returns `100`.
  - `12` returns `80` for vanilla/unknown active orders because type `12` is ambiguous and vanilla blocks many type `12` transitions.
  - `7` returns `90` in campaign scope and `80` in tactical scope.
  - `13` and `14` return `100` for same-cycle vanilla self-issued transition protection when provenance is `Vanilla` or `Unknown`; return `20` only when Whiskey shadow proves Whiskey authored the transition.
  - `0`, `1`, `2`, `3`, `4`, `5` return `60` unless shadow state proves a lower-priority Whiskey probe.
  - `6`, `8`, `9`, `10`, `16` return `30`.
  - Unknown types return `int.MaxValue` so Whiskey suppresses rather than overwrites.

- [ ] Create `PlayerOrderVanillaMapper.cs`:
  - `RetreatToExit` maps to type `15`.
  - `FallBackToLine` maps to type `12`.
  - `HoldObjective` maps to type `12`.
  - `SupportMainEffort` maps to type `7` only when the runtime adapter has a campaign-capable target; otherwise maps to type `4` or `2` according to existing vanilla tactical call semantics.
  - `AttackObjective` maps to type `0` or `1` based on mapper input when objective identity is available; default to `1` only with a named objective.
  - `ProbeObjective` maps to type `5`.
  - `AdvanceToAssemblyArea` maps to type `4`.
  - `BuildSupplyDepot`, `BuildFort`, and `DefendCapital` map to their campaign type rows from the spec.
  - `RecoverFromCombat` and `ClearHoldTransition` map to `13` and `14` only for Whiskey-authored cleanup, never as a higher-priority tactical replacement.

- [ ] Keep the mapper pure. It returns type and semantic requirements, not vanilla object references.

## Task 2: Dedupe, Provenance, And Cache Lifetime

### Tests First

- [ ] Register these tests:

```csharp
("player order dedupe blocks tactical over active type fifteen", PlayerOrderDedupeBlocksTacticalOverActiveTypeFifteen),
("player order dedupe protects vanilla transition orders", PlayerOrderDedupeProtectsVanillaTransitionOrders),
("player order dedupe blocks hold over active attack per vanilla", PlayerOrderDedupeBlocksHoldOverActiveAttackPerVanilla),
("player order dedupe enforces campaign tactical scope gap", PlayerOrderDedupeEnforcesCampaignTacticalScopeGap),
("player order dedupe allows emergency retreat over invalid campaign order", PlayerOrderDedupeAllowsEmergencyRetreatOverInvalidCampaignOrder),
("player order cache clears by battle identity", PlayerOrderCacheClearsByBattleIdentity),
("player order signature cache suppresses repeated material match", PlayerOrderSignatureCacheSuppressesRepeatedMaterialMatch),
("player order issuance throttle suppresses rapid changed candidates", PlayerOrderIssuanceThrottleSuppressesRapidChangedCandidates),
```

- [ ] Add assertions covering these rules:
  - Active type `15` blocks all tactical candidates, including `RetreatToExit`, unless the active order is stale by battle identity.
  - Vanilla/unknown active type `13` or `14` yields for one tactical cycle and is not replaced by `HoldObjective` or `SupportMainEffort`.
  - Active type `0`, `1`, `2`, `3`, `4`, `5`, or `13` blocks candidate type `12`.
  - Active type `13` blocks candidate non-`13`.
  - Candidate type `14` is blocked unless active type is `12`.
  - Tactical candidate must exceed active campaign priority by at least `40` and campaign validity must be false.
  - Campaign candidate must not replace an active tactical order unless the tactical order is stale, battle ended, or campaign exceeds by at least `40`.
  - Unknown active provenance suppresses replacement when type-to-intent inversion is ambiguous.
  - Per-unit throttle suppresses changed candidates inside the configured tick window.
  - Signature dedupe suppresses same type, unit, scope, and bucketed target.
  - Clearing by battle identity removes stale signatures and provenance shadows.

### Implementation

- [ ] Create `PlayerOrderDedupe.cs` with:

```csharp
internal enum PlayerOrderDecisionKind
{
    Issue = 0,
    SuppressSignature = 1,
    SuppressThrottle = 2,
    YieldVanillaTransition = 3,
    BlockedByVanillaDedupe = 4,
    BlockedByScopePriority = 5,
    BlockedByUnknownActiveOrder = 6,
    BlockedByDisabledWrites = 7,
    NoCandidate = 8
}
```

- [ ] Add an immutable `PlayerOrderDedupeDecision` with `Kind`, `ShouldIssue`, and `Reason`.
- [ ] Add `PlayerOrderSignature` with fields:
  - `PlayerOrderScope Scope`
  - `PlayerOrderIntent Intent`
  - `int VanillaType`
  - `int UnitKey`
  - `int TargetBucketX`
  - `int TargetBucketZ`
  - `int RotationBucket`
  - `string ObjectiveKey`
- [ ] Add `PlayerOrderShadow` with fields:
  - `PlayerOrderScope Scope`
  - `PlayerOrderProvenance Provenance`
  - `PlayerOrderIntent Intent`
  - `int VanillaType`
  - `int UnitKey`
  - `int GivenOrderSession`
  - `int BattleIdentity`
  - `long IssuedTick`
  - `PlayerOrderSignature Signature`
- [ ] Add `PlayerOrderDedupeState` as a runtime-owned mutable cache:
  - latest shadow by unit key
  - latest signature by unit key and scope
  - latest issuance tick by unit key
  - current battle identity
  - current player command key
  - current player CIC flag
- [ ] Provide methods:

```csharp
internal static bool VanillaWouldBlock(PlayerOrderActiveSnapshot active, PlayerOrderCandidate candidate, bool campaignGroupFlag);
internal static PlayerOrderDedupeDecision Decide(PlayerOrderCandidate candidate, PlayerOrderActiveSnapshot active, PlayerOrderDedupeState state, PlayerOrderDedupeOptions options);
internal static void RecordIssued(PlayerOrderCandidate candidate, PlayerOrderActiveSnapshot afterIssue, PlayerOrderDedupeState state, long tick);
internal static void ClearForBattleBoundary(PlayerOrderDedupeState state, int battleIdentity);
internal static void ClearForPlayerCommandChange(PlayerOrderDedupeState state, int playerCommandKey, bool isCommanderInChief);
```

- [ ] Implement vanilla tactical preflight exactly from decompiled lines `8640` through `8646`:
  - Same type blocks most repeats.
  - Type `11` repeats block only when nearby/facing equivalent.
  - Candidate type `12` is blocked by active `0`, `1`, `2`, `3`, `4`, `5`, or `13`.
  - Active type `13` blocks candidate non-`13`.
  - Candidate type `14` is blocked unless active type is `12`.
  - Campaign-group type `2` is blocked by active `0`, `1`, `2`, `3`, or `4`.
  - Active type `15` blocks every tactical candidate.
- [ ] Implement same-cycle vanilla self-write protection:
  - If active order provenance is vanilla or unknown and type is `13`, `14`, or `15`, return `YieldVanillaTransition`.
  - Yield protection lasts at least one tactical Postfix cycle even when candidate priority is higher.
- [ ] Implement cross-scope policy:
  - Tactical may replace campaign only when `candidate.Priority >= activePriority + 40` and the runtime says active campaign order is no longer actionable.
  - `RetreatToExit` may replace campaign with valid exit-point data.
  - Campaign may replace tactical only when the tactical order is stale, battle ended, or `candidate.Priority >= activePriority + 40`.
  - Unknown provenance plus ambiguous active type suppresses replacement.
- [ ] Implement throttle separate from material signature:
  - Use a default of one tactical order issue per unit per `120` vanilla micro-AI cycles.
  - Treat this default as an implementation constant unless runtime smoke shows excessive delay; expose the value only if smoke evidence requires user tuning.
- [ ] Implement cache clearing:
  - Clear all tactical caches when battle identity changes.
  - Clear all tactical caches when player current command changes.
  - Clear all tactical caches when player CIC status changes.
  - Clear all tactical caches through the battle-end observer if an existing observer hook is available; otherwise clear on next battle identity mismatch.
  - Clear campaign caches when campaign/save context changes.
- [ ] Keep all state process-memory only.

## Task 3: Tactical Composer

### Tests First

- [ ] Register these tests:

```csharp
("player order composer returns none without orchestrator", PlayerOrderComposerReturnsNoneWithoutOrchestrator),
("player order composer prefers retreat from strategic withdrawal intent", PlayerOrderComposerPrefersRetreatFromStrategicWithdrawalIntent),
("player order composer maps hold doctrine to hold objective", PlayerOrderComposerMapsHoldDoctrineToHoldObjective),
("player order composer maps support role to support main effort", PlayerOrderComposerMapsSupportRoleToSupportMainEffort),
("player order composer emits reason for diagnostics", PlayerOrderComposerEmitsReasonForDiagnostics),
```

- [ ] Use pure stub inputs, not Unity objects. The tests should instantiate a `PlayerOrderComposerInput` shape that mirrors the consumed orchestrator fields.

### Implementation

- [ ] Create `PlayerOrderComposer.cs` with pure input and output types:

```csharp
internal readonly struct PlayerOrderComposerInput
{
    public readonly bool HasSideOrchestrator;
    public readonly bool IsPlayerSubordinate;
    public readonly bool IsCommanderInChief;
    public readonly int UnitKey;
    public readonly CommandIntentResolutionSnapshot CommandIntent;
    public readonly ArmyDoctrineSnapshot Doctrine;
    public readonly PlayerOrderPoint UnitPosition;
    public readonly PlayerOrderPoint ObjectivePosition;
    public readonly bool HasValidObjective;
    public readonly bool HasValidExitPoint;
}
```

- [ ] Create pure snapshots for consumed orchestrator shape:
  - `CommandIntentResolutionSnapshot.Found`
  - `CommandIntentResolutionSnapshot.Intent`
  - `CommandIntentResolutionSnapshot.Reason`
  - `CommandNodeIntentSnapshot.NodeId`
  - `CommandNodeIntentSnapshot.SourceNodeId`
  - `CommandNodeIntentSnapshot.Role`
  - `CommandNodeIntentSnapshot.Axis`
  - `CommandNodeIntentSnapshot.PrimarySector`
  - `CommandNodeIntentSnapshot.SupportPriority`
  - `CommandNodeIntentSnapshot.AggressionBias01`
  - `CommandNodeIntentSnapshot.Depth`
  - `ArmyDoctrineSnapshot.CurrentStrategicBattleIntent`
  - `ArmyDoctrineSnapshot.CurrentOperation`
  - `ArmyDoctrineSnapshot.CurrentDoctrineOrders`
- [ ] Implement composer order of precedence:
  1. No candidate if not player subordinate.
  2. No candidate if player is CIC.
  3. `RetreatToExit` only when orchestrator intent indicates emergency withdrawal and runtime has valid vanilla exit-point data.
  4. `FallBackToLine` from fallback/defensive withdrawal intents with non-exit fallback target.
  5. `HoldObjective` from hold/defend/guard role or doctrine order.
  6. `SupportMainEffort` from support role and valid supported objective.
  7. `AttackObjective` from attack/seize objective.
  8. `ProbeObjective` from probe/screen/recon.
  9. `AdvanceToAssemblyArea` from movement to non-contact assembly.
- [ ] Include a concise reason string in every non-empty candidate. Diagnostics and tests depend on this string.
- [ ] Keep the composer pure. It must not read vanilla objects, static game state, `DLC_WL`, `Plugin.Config`, or `TacticalCommanderModePolicy`.

## Task 4: Diagnostics And Config

### Tests First

- [ ] Register these tests:

```csharp
("player order diagnostics signatures cap repeated logs", PlayerOrderDiagnosticsSignaturesCapRepeatedLogs),
("player order diagnostics works while writes disabled", PlayerOrderDiagnosticsWorksWhileWritesDisabled),
```

- [ ] Test diagnostics as pure signature formatting and rate-limiting decisions. Do not assert against `Plugin.Log` in the console harness.

### Implementation

- [ ] Add config entries to `Plugin.cs`:

```csharp
internal static ConfigEntry<bool> EnablePlayerOrderDoctrine { get; private set; }
internal static ConfigEntry<bool> EnablePlayerOrderDoctrineDiagnostics { get; private set; }
```

- [ ] Bind them under the existing `W&L` section:

```csharp
EnablePlayerOrderDoctrine = Config.Bind(
    "W&L",
    "Enable Player Order Doctrine",
    false,
    "Default-off write valve for Whiskey-authored player-facing W&L orders. Diagnostics can run while this is disabled.");

EnablePlayerOrderDoctrineDiagnostics = Config.Bind(
    "W&L",
    "Enable Player Order Doctrine Diagnostics",
    true,
    "Logs bounded classify/dedupe/issue decisions for the player-order doctrine.");
```

- [ ] Create `PlayerOrderDiagnostics.cs` only if it owns reusable rate-limiting:
  - signature cap by decision kind, unit key, vanilla type, and reason
  - formatted lines for `classify`, `issue`, `suppress`, and `yield`
  - no direct `Plugin.Log` dependency in pure code
- [ ] In runtime/patch code, emit diagnostics only through `OnceLog` or the diagnostics signature cap.
- [ ] When `Enable Player Order Doctrine = false` and diagnostics are enabled, still compose, map, and dedupe; log classify/suppress decisions; never call vanilla order methods.

## Task 5: Runtime Adapter And Tactical Patch

### Runtime Adapter

- [ ] Create `PlayerOrderRuntimeAdapter.cs` with all Unity/vanilla object access isolated here.
- [ ] Use `AccessTools` or existing reflection helpers for fields and methods. Every lookup must be wrapped in try/catch and log `Plugin.Log.LogWarning(...)` once or with signature caps. Missing fields downgrade to no-candidate or no-issue.
- [ ] Runtime adapter responsibilities:
  - identify battle identity
  - identify player current command
  - identify player CIC status
  - identify subordinate unit key
  - read active `DLC_WL.givenorder`
  - read `givenorderssession` or nearest available session marker
  - classify active order provenance using Whiskey shadow state
  - resolve `TacticalBattleCoordinator.GetSideOrchestrator(...)`
  - read `TacticalBattleOrchestrator.Army`
  - read `ArmyOrchestrator.CurrentDirectChildIntents`
  - read `ArmyOrchestrator.CurrentCommandOperations`
  - read `ArmyOrchestrator.CurrentDoctrineOrders`
  - read `ArmyOrchestrator.CurrentOperation`
  - read `ArmyOrchestrator.CurrentStrategicBattleIntent`
  - call `ArmyOrchestrator.ResolveCommandIntentForGroup(...)`
  - mirror vanilla `bunits.SearchForClosestEntryPoint(...)` for type `15`
  - resolve objective name, target point, width/depth, and rotation from vanilla order arguments
- [ ] If `SearchForClosestEntryPoint(...)` cannot be mirrored, suppress `RetreatToExit` and log a bounded warning. Do not fall back to generic type `15`.

### Patch

- [ ] Create `src/WhiskeyRealism/Patches/PlayerSubordinateOrderPatch.cs`.
- [ ] Add a header comment explaining:
  - vanilla `UpdateDLCPlayerOrders()` runs on tactical micro-AI cycle `28`
  - vanilla may self-issue type `13`, `14`, and `15` during removal/transition checks
  - Whiskey runs after vanilla and yields to these transitions
  - Whiskey never calls campaign bypass mode from tactical
- [ ] Patch `AIBattle.UpdateDLCPlayerOrders()` with Prefix and Postfix:
  - Prefix snapshots active order type/session/position before vanilla executes.
  - Postfix snapshots active order again after vanilla executes.
  - If vanilla changed the active order in the same call, mark provenance as vanilla/unknown and apply transition-yield rules before composing.
  - If `TacticalCommanderModePolicy.AllowsWrites` is false, diagnostics may classify but vanilla calls are suppressed.
  - If `Enable Player Order Doctrine` is false, diagnostics may classify but vanilla calls are suppressed.
  - If candidate survives dedupe, call the existing vanilla `CheckCurrentOrderUpdate` instance method with `calledfromcampaign` left false/default.
  - After the call, read active order/session and record Whiskey shadow only when the vanilla active order matches the expected issued type/unit signature.
  - Catch all exceptions and log bounded warnings. Never throw from Prefix or Postfix.
- [ ] Ensure the patch exits immediately for:
  - missing battle instance
  - missing player current command
  - player is CIC
  - no side orchestrator
  - no army orchestrator
  - invalid unit key
- [ ] Add patch registration in the existing patch bootstrap path if registration is not automatic.

## Task 6: Campaign Bridge Signature Cache

### Tests First

- [ ] Register these tests:

```csharp
("strategic order bridge suppresses repeated campaign signature", StrategicOrderBridgeSuppressesRepeatedCampaignSignature),
("strategic order bridge does not replace active tactical without gap", StrategicOrderBridgeDoesNotReplaceActiveTacticalWithoutGap),
("strategic order bridge clears cache for campaign context", StrategicOrderBridgeClearsCacheForCampaignContext),
```

- [ ] Keep bridge tests pure by extracting cache and replacement policy into methods that accept primitive snapshots.

### Implementation

- [ ] Extend `WlStrategicOrderBridge` to use the shared signature and cross-scope replacement policy from `PlayerOrderDedupe`.
- [ ] Add a campaign/save context key to campaign cache state. Clear campaign signature/provenance caches when the context key changes.
- [ ] Preserve existing `TryIssue` and `ClassifyOnly` public shape unless the audit proves call sites need a new options object.
- [ ] Convert any audited strategic W&L source that bypasses the bridge so the campaign half of the doctrine receives signature cache, cooldown, and scope protection.
- [ ] Keep campaign calls default behavior unchanged except for duplicate suppression and scope protection.

## Task 7: Documentation And Catalog Updates

- [ ] Update `docs/patch-catalog.md` with a new patch ordinal for `PlayerSubordinateOrderPatch`, including:
  - target method `AIBattle.UpdateDLCPlayerOrders()`
  - Prefix/Postfix ownership
  - default-off write valve
  - diagnostics default
  - rollback config
- [ ] Update `docs/handoff.md` with:
  - what changed
  - config keys
  - verification commands and results
  - smoke-test instructions
  - current deployed DLL hash only after deploy/hash verification succeeds
- [ ] Update `MEMORY.md` with a terse durable index entry if the implementation ships.
- [ ] Do not archive the spec or plan until the DLL is built, deployed, hash-verified, and smoke-tested.

## Task 8: Verification

Run after implementation:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: process exits `0`; player-order tests pass; no pre-existing tests regress.

```bash
./build.sh
```

Expected: `0 Error(s)` and `dist/WhiskeyRealism.dll` updated.

Deploy only after the build succeeds and the game is closed:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: source and deployed DLL sizes match; SHA-256 hashes are identical.

Runtime smoke:

- [ ] Launch GTCW with BepInEx.
- [ ] Confirm config file contains:
  - `Enable Player Order Doctrine = false`
  - `Enable Player Order Doctrine Diagnostics = true`
- [ ] Start or load a battle where the player commands a subordinate force, not CIC.
- [ ] With writes disabled, confirm logs show bounded classify/suppress diagnostics and no Whiskey-issued player order call.
- [ ] Set `Enable Player Order Doctrine = true`, restart, and repeat the same battle smoke.
- [ ] Confirm no repeated exceptions in `BepInEx/LogOutput.log`.
- [ ] Confirm diagnostics stay bounded during at least five battle minutes.
- [ ] Confirm vanilla type `13`, `14`, and `15` transitions are yielded when they occur.
- [ ] Confirm no retasking occurs when the player is CIC.
- [ ] Confirm changing current command clears caches and does not suppress the first legitimate order for the new command.

Log scan:

```bash
rg -n "PlayerSubordinateOrderPatch|PlayerOrder|Exception|Error" "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Expected: player-order logs are bounded and informative; no repeated patch exception stream.

## Rollback

- Set `[W&L] Enable Player Order Doctrine = false` to stop all Whiskey-authored writes while retaining diagnostics.
- Set `[W&L] Enable Player Order Doctrine Diagnostics = false` to silence player-order diagnostics.
- Set `Tactical Commander Mode = Off` to disable tactical orchestrator write behavior globally if smoke reveals broader tactical interference.
- Revert only the cohesive player-order implementation commit if runtime smoke shows unsafe behavior that config rollback cannot contain.

## Commit Plan

Use focused commits:

1. Pure contracts, priority, mapper, dedupe, composer, diagnostics, and tests.
2. Runtime adapter, tactical patch, plugin config, and build verification.
3. Campaign bridge cache/audit conversions and tests.
4. Documentation and smoke evidence.

Before each commit:

```bash
git diff --check
git status --short
```

Stage only files listed in this plan. Do not stage unrelated local files.
