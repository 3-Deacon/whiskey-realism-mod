# Tactical Battlefield Bug Remediation Implementation Plan

Status: implemented, console-tested, built, deployed, hash-verified, and focused-smoke sampled on 2026-05-07. Current deployed DLL is `9136d14fbea7b2ace5ba034dc673f71b31de2b9d8467c159c49cdbd9052513bd` (524288 bytes). Focused W&L battle smoke on the prior telemetry DLL confirmed B2 command/order telemetry and repeated `BUG-TAC-005` objective-chain exposure. It did not exercise #43 fallback/retreat suppression and did not prove courier mismatch, campaign current-order duplication, waypoint drift, reserve direct-path movement, or objective-chain mutation; the current DLL adds `[TacticalObjectiveMutation]` proof telemetry.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the tactical battlefield bug hunt into a safe, evidence-led remediation track for order delivery, W&L current orders, fallback/retreat crash guards, and AI movement/order-decision hazards.

**Architecture:** Split the work into two tracks: read-only telemetry for unproven behavior and narrow runtime guards for decompile-confirmed hot-path hazards. Keep Slice B2 command/order friction read-only, keep #41/#42 as the existing W&L charge/feud guard surface, and do not add global behavior replacements or guards for `BattleUnits.SetWaypoint`; telemetry-only observers are allowed. Any transpiler or broad mirrored Prefix is an explicit approval gate, not an automatic task.

**Review delta:** Final implementation makes #43 default-off because its Harmony Finalizer suppresses method-level `NullReferenceException` inside two large vanilla methods, not only null `allattachedunits` slots. `[TacticalObjectiveMove]` is exposure telemetry until path/position deltas prove movement actually touched player-subordinate attachments.

**Runtime delta - 2026-05-07 focused battle smoke:** The log emitted `[TacticalCommand]` 25 times, `[TacticalOrder]` 116 times, `[TacticalCurrentOrder]` once, `[TacticalCourierQueue]` five times, and `[TacticalObjectiveMove]` eleven times. All courier markers were `risk=False`; the current-order marker was `calledFromCampaign=False duplicateRisk=False`; all objective-move markers showed `attachedUnderCommanderCount=1 risk=True` for `center=1st_Brigade#-30060`. No `[TacticalWaypointDrift]`, `[TacticalReserveMove]`, `[Patch:TacticalFallbackRetreatNullGuard]`, `[TacticalChargeGuard]`, or `[TacticalFeudGuard]` lines fired. No `Exception`, `TargetInvocationException`, `missing-anchor`, `failed-owned`, or old `Regiment.side` warning appeared. The current deployed DLL adds `[TacticalObjectiveMutation]` proof telemetry for the exposed center/attached units.

**Tech Stack:** C# netstandard2.1, BepInEx 5.4.x, HarmonyX, Grand Tactician vanilla `AIBattle` / `BattleUnits` / `Regiment`, console harness at `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`.

---

## Scope And Evidence

This plan covers the battlefield-layer findings from the 2026-05-07 subagent-backed bug hunt:

- `BUG-TAC-001`: secondary courier lines may attach to the wrong order queue.
- `BUG-TAC-002`: tactical fallback/retreat loops can dereference null `allattachedunits` entries.
- `BUG-TAC-003`: `CheckCurrentOrderUpdate(... calledfromcampaign:true)` can overwrite/repeat W&L current orders because duplicate suppression is battle-call-only.
- `BUG-TAC-004`: delayed `SetWaypoint` can write untransmitted path intent even when active order flags skip queue insertion.
- `BUG-TAC-005`: `UpdateMovingTargets()` can expose objective-chain formations with player-subordinate attached units outside the B1 guard boundary; movement contact still needs path/position proof.
- `BUG-TAC-006`: emergency reserve support uses direct `RegimentSetPath(...)` and bypasses courier/order delay.
- `BUG-TAC-007`: W&L incident 40 order-delay logic reads incident 38 timers.
- `BUG-TAC-008`: reserve rescue selection likely excludes the last candidate through `Random.Range(0, list.Count - 1)`.
- `BUG-TAC-009`: vanilla relief methods are dead stubs; this is later doctrine work, not an immediate bug guard.

Verified anchors:

- `AIBattle.MicroAICheckForCharges(...)`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:4905`
- `AIBattle.CheckForFeudGroupActions()`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:4931`
- `AIBattle.UpdateMovingTargets()`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:6870`
- `AIBattle.CheckUseOfReserves(...)`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:6062`
- `AIBattle.MicroAICheckForRetreats(...)`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:4817`
- `AIBattle.CheckLineFallbacks(...)`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:5118`
- `AIBattle.CheckCurrentOrderUpdate(...)` body: duplicate suppression at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:8640`, replacement at `8648`
- `BattleUnits.SetWaypoint(Regiment, ...)`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:91232`
- `Regiment.AddToOrderQueue(...)`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:124917`
- `Regiment.AddOrderCourierline(...)`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:125009`, queue append at `125169`
- `Regiment.ProcessOrders()`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:125173`

Non-negotiable boundaries:

- Do not replace or behavior-guard `BattleUnits.SetWaypoint` globally; default-off telemetry-only observation is allowed.
- Do not add a transpiler without a direct user approval checkpoint.
- Do not convert B2 from observer telemetry into behavior enforcement.
- Do not treat missing vanilla relief behavior as a bug-fix hotfix; it belongs to later tactical doctrine.
- Keep the existing dirty B2 worktree changes intact. If this plan is executed in the current checkout, inspect and preserve the existing modifications to `src/WhiskeyRealism/Tactical/*` and `tests/WhiskeyRealism.Tests/*`.

## File Structure

- Create `docs/bug-fixes/vanilla-tactical-battlefield.md`
  - Tactical battlefield bug queue with `BUG-TAC-*` entries, evidence, current status, and next action.
- Modify `docs/bug-fixes/README.md`
  - Add the tactical battlefield queue to the active queues list.
- Create `src/WhiskeyRealism/Tactical/TacticalBattlefieldBugDiagnostics.cs`
  - Pure diagnostic helpers for current-order signatures, duplicate checks, queue append risk, and exception suppression policy.
- Modify `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
  - Add explicit compile entry for `TacticalBattlefieldBugDiagnostics.cs`.
- Modify `tests/WhiskeyRealism.Tests/Program.cs`
  - Add pure tests for duplicate current-order classification, queue append risk, and fallback/retreat exception policy.
- Modify `src/WhiskeyRealism/Plugin.cs`
  - Add config entries:
    - `Enable Tactical Bug Telemetry` default `false`
    - `Enable Tactical Fallback Retreat Null Guard` default `false`
- Modify `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs`
  - Extend #35 observer telemetry for:
    - `AIBattle.CheckCurrentOrderUpdate(...)`
    - `BattleUnits.SetWaypoint(Regiment, ...)`
    - `Regiment.AddOrderCourierline(...)` secondary courier queue-risk details
    - objective-chain exposure and reserve support summaries where current B0 coverage is too coarse
- Create `src/WhiskeyRealism/Patches/TacticalFallbackRetreatNullGuardPatch.cs`
  - Dedicated #43 bug guard for `MicroAICheckForRetreats` and `CheckLineFallbacks` `NullReferenceException` suppression.
- Modify `docs/patch-catalog.md`
  - Add #43 only after the null guard is implemented.
  - Keep telemetry extensions under #35 unless a new Harmony patch file is added beyond the null guard.
- Modify `docs/handoff.md`
  - Add current tactical bug-remediation state and smoke gates after implementation.

## Task 0: Preflight And Branch Hygiene

**Files:**
- Read: `AGENTS.md`
- Read: `docs/handoff.md`
- Read: `docs/patch-catalog.md`
- Read: `docs/superpowers/plans/2026-05-07-tactical-b2-command-order-friction.md`

- [ ] **Step 1: Confirm branch and dirty worktree**

Run:

```bash
git status --short --branch
```

Expected current shape before execution:

```text
## project-doctrine...origin/project-doctrine [ahead 2]
 M src/WhiskeyRealism/Tactical/TacticalBattleContext.cs
 M src/WhiskeyRealism/Tactical/TacticalTelemetry.cs
 M tests/WhiskeyRealism.Tests/Program.cs
 M tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
?? src/WhiskeyRealism/Tactical/TacticalCommandLedger.cs
?? src/WhiskeyRealism/Tactical/TacticalCommanderProfile.cs
?? src/WhiskeyRealism/Tactical/TacticalOrderFriction.cs
```

If the existing B2 files are still dirty, do not overwrite them. Either continue in the same branch with additive edits or create a separate worktree only after confirming with the user.

- [ ] **Step 2: Re-read active tactical ownership**

Run:

```bash
sed -n '230,255p' docs/handoff.md
sed -n '35,55p' docs/patch-catalog.md
```

Expected: B0 is closed, B1 is built/deployed but denial smoke is deferred, B2 is read-only telemetry, #35 owns tactical observer telemetry, #41/#42 own W&L charge/feud behavior guards.

- [ ] **Step 3: Confirm decompile anchors still match**

Run:

```bash
rg -n "private void MicroAICheckForRetreats\\(|private unsafe void CheckLineFallbacks\\(|private void CheckCurrentOrderUpdate\\(|public unsafe void SetWaypoint\\(Regiment|private void AddOrderCourierline\\(|private void ProcessOrders\\(" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
```

Expected anchors include:

```text
4817: private unsafe void MicroAICheckForRetreats(Regiment aigroup)
5118: private unsafe void CheckLineFallbacks(Regiment aigroup)
91232: public unsafe void SetWaypoint(Regiment reg, Vector3 targetpos, ...)
125009: private void AddOrderCourierline(Regiment sourceunit, Regiment _targetunit, ...)
125173: private void ProcessOrders()
```

If `CheckCurrentOrderUpdate` does not appear because the decompiler omitted the signature in the nearby search, inspect around `8330-8665` and verify duplicate suppression at `8640`.

## Task 1: Document Tactical Bug Queue

**Files:**
- Create: `docs/bug-fixes/vanilla-tactical-battlefield.md`
- Modify: `docs/bug-fixes/README.md`

- [ ] **Step 1: Create the tactical bug queue document**

Create `docs/bug-fixes/vanilla-tactical-battlefield.md`:

```markdown
# Vanilla Tactical Battlefield Bug Queue

Narrow battlefield-layer bug queue for order delivery, W&L current-order handling, tactical fallback/retreat hazards, and battle AI movement defects. This is not the broad Slice B doctrine spec.

| ID | Status | Area | Evidence | Current action |
|---|---|---|---|---|
| `BUG-TAC-001` | Observed; no mismatch proof | Order courier queue | `Regiment.ProcessOrders()` processes queue index `i`, but secondary `AddOrderCourierline(...)` appends to `orderqueue[orderqueue.Count - 1]` at decompile line 125169. | Keep telemetry/soak; patch only after wrong-index proof and approval if a transpiler/replacement is needed. |
| `BUG-TAC-002` | Shipped; focused smoke clean | Fallback/retreat crash guard | `MicroAICheckForRetreats()` and `CheckLineFallbacks()` dereference `allattachedunits[i]` without null guards on hot tactical ticks; decompile anchors: lines 4817 and 5118. | #43 `TacticalFallbackRetreatNullGuardPatch` is built/deployed but default-off; focused smoke did not exercise it. |
| `BUG-TAC-003` | Observed benign battle-call only | W&L current orders | `CheckCurrentOrderUpdate(... calledfromcampaign:true)` bypasses duplicate suppression and replaces `DLC_WL.givenorder`. | Need campaign-call proof before any duplicate guard. |
| `BUG-TAC-004` | Not observed | Delayed order path drift | `BattleUnits.SetWaypoint(...)` skips `AddToOrderQueue` when order type is active but still writes path intent. | Widen/prove the caller-specific path mutation before any behavior guard. |
| `BUG-TAC-005` | Runtime-confirmed exposure; needs movement proof | Objective-chain exposure | `UpdateMovingTargets()` checks only center group `dlcw_isundercommander`, not attached player-subordinate units. | Add path/position delta proof before claiming movement touched a player-subordinate attachment. |
| `BUG-TAC-006` | Not observed | Reserve support | `CheckUseOfReserves()` uses direct `RegimentSetPath(...)`, bypassing order delay. | Widen reserve direct-path telemetry before doctrine/fix planning. |
| `BUG-TAC-007` | Needs repro | W&L incident order delay | Incident 40 branch reads incident 38 timers in `AddOrderCourierline(...)`. | Verify incidents can be independently active before any transpiler request. |
| `BUG-TAC-008` | Backlog | Reserve candidate bias | `Random.Range(0, list.Count - 1)` likely excludes last reserve candidate. | Observe candidate counts/selected index first; do not mirror full reserve method for this alone. |
| `BUG-TAC-009` | Backlog | Relief doctrine gap | `CheckReliefOfObjectve(...)` is empty and `CheckReliefOfObjectveDueToLowMorale(...)` discards a boolean. | Later tactical doctrine; not a hotfix. |

## Smoke Markers

- `[TacticalCurrentOrder]`
- `[TacticalWaypointDrift]`
- `[TacticalCourierQueue]`
- `[TacticalObjectiveMove]`
- `[TacticalReserveMove]`
- `[Patch:TacticalFallbackRetreatNullGuard]`

## Rules

- Do not add a global behavior replacement or guard for `BattleUnits.SetWaypoint`; telemetry-only observers are allowed.
- Keep observer telemetry default-off behind `Enable Tactical Bug Telemetry`.
- Keep B1 charge/feud behavior under #41/#42.
- Keep B2 command/order friction read-only.
- Any transpiler requires explicit user approval.
```

- [ ] **Step 2: Link the new queue from the bug-fix index**

In `docs/bug-fixes/README.md`, add this row to the index table:

```markdown
| `BUG-TAC-001` - `BUG-TAC-009` | Mixed | Tactical battlefield | Subagent-backed decompile review found courier queue, fallback/retreat null, W&L current-order, delayed waypoint, objective-chain, reserve, and W&L incident-order-delay hazards. | Track in `vanilla-tactical-battlefield.md`; telemetry first except confirmed fallback/retreat null guard. |
```

Add this item under **Active Queues**:

```markdown
- [Vanilla tactical battlefield bug queue](vanilla-tactical-battlefield.md)
```

- [ ] **Step 3: Verify doc links**

Run:

```bash
rg -n "vanilla-tactical-battlefield|BUG-TAC" docs/bug-fixes
```

Expected: the new queue file and README references are both found.

## Task 2: Add Pure Diagnostic Helpers

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalBattlefieldBugDiagnostics.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add explicit compile entry**

In `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, add this line next to the tactical compile entries:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalBattlefieldBugDiagnostics.cs" Link="TacticalBattlefieldBugDiagnostics.cs" />
```

- [ ] **Step 2: Add failing pure tests**

In `tests/WhiskeyRealism.Tests/Program.cs`, add these registrations near the tactical tests:

```csharp
("tactical diagnostics detect campaign duplicate current order", TacticalDiagnosticsDetectCampaignDuplicateCurrentOrder),
("tactical diagnostics detect secondary courier queue risk", TacticalDiagnosticsDetectSecondaryCourierQueueRisk),
("tactical diagnostics suppress only tactical null fallback exceptions", TacticalDiagnosticsSuppressOnlyTacticalNullFallbackExceptions),
("tactical diagnostics detect skipped queue path drift", TacticalDiagnosticsDetectSkippedQueuePathDrift),
```

Add these methods near the other tactical test methods:

```csharp
private static void TacticalDiagnosticsDetectCampaignDuplicateCurrentOrder()
{
    var oldOrder = new TacticalCurrentOrderSignature(7, 11, 100f, 200f, 45f);
    var newOrder = new TacticalCurrentOrderSignature(7, 11, 104f, 202f, 47f);
    var decision = TacticalBattlefieldBugDiagnostics.ClassifyCurrentOrder(
        calledFromCampaign: true,
        oldOrder: oldOrder,
        newOrder: newOrder,
        nearDistance: 110f,
        nearRotationDegrees: 35f);

    AssertTrue(decision.IsDuplicateRisk, "campaign duplicate should be flagged");
    AssertEqual("campaign-duplicate-near", decision.Reason, "reason");
}

private static void TacticalDiagnosticsDetectSecondaryCourierQueueRisk()
{
    var decision = TacticalBattlefieldBugDiagnostics.ClassifySecondaryCourier(
        secondaryCourier: true,
        orderQueueCount: 3,
        activeQueueIndex: 0,
        appendQueueIndex: 2);

    AssertTrue(decision.IsRisk, "secondary courier appended to a different queue should be risky");
    AssertEqual("secondary-courier-appended-to-latest", decision.Reason, "reason");
}

private static void TacticalDiagnosticsSuppressOnlyTacticalNullFallbackExceptions()
{
    AssertTrue(
        TacticalBattlefieldBugDiagnostics.ShouldSuppressFallbackRetreatException(
            "MicroAICheckForRetreats",
            new NullReferenceException("null attached unit")),
        "null fallback exception should be suppressed");

    AssertFalse(
        TacticalBattlefieldBugDiagnostics.ShouldSuppressFallbackRetreatException(
            "MicroAICheckForRetreats",
            new InvalidOperationException("not null")),
        "non-null exceptions must propagate");

    AssertFalse(
        TacticalBattlefieldBugDiagnostics.ShouldSuppressFallbackRetreatException(
            "CheckAIBombardment",
            new NullReferenceException("different method")),
        "other tactical methods must propagate");
}

private static void TacticalDiagnosticsDetectSkippedQueuePathDrift()
{
    var decision = TacticalBattlefieldBugDiagnostics.ClassifyWaypointQueueDrift(
        useOrderDelay: true,
        activeMoveOrder: true,
        queueAdded: false,
        pathCountBefore: 1,
        pathCountAfter: 2);

    AssertTrue(decision.IsRisk, "path changed without queue insert should be risky");
    AssertEqual("path-mutated-without-queue", decision.Reason, "reason");
}
```

- [ ] **Step 3: Run tests and confirm failure**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile fails because `TacticalBattlefieldBugDiagnostics`, `TacticalCurrentOrderSignature`, and diagnostic decision types do not exist.

- [ ] **Step 4: Create the pure helper**

Create `src/WhiskeyRealism/Tactical/TacticalBattlefieldBugDiagnostics.cs`:

```csharp
using System;

namespace WhiskeyRealism.Tactical
{
    public readonly struct TacticalCurrentOrderSignature
    {
        public TacticalCurrentOrderSignature(int unitId, int type, float x, float z, float rotation)
        {
            UnitId = unitId;
            Type = type;
            X = Sanitize(x);
            Z = Sanitize(z);
            Rotation = Sanitize(rotation);
        }

        public int UnitId { get; }
        public int Type { get; }
        public float X { get; }
        public float Z { get; }
        public float Rotation { get; }

        public bool IsEmpty => UnitId == 0 && Type < 0;

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }

    public readonly struct TacticalDiagnosticDecision
    {
        public TacticalDiagnosticDecision(bool isRisk, string reason)
        {
            IsRisk = isRisk;
            Reason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
        }

        public bool IsRisk { get; }
        public bool IsDuplicateRisk => IsRisk;
        public string Reason { get; }
    }

    public static class TacticalBattlefieldBugDiagnostics
    {
        public static TacticalDiagnosticDecision ClassifyCurrentOrder(
            bool calledFromCampaign,
            TacticalCurrentOrderSignature oldOrder,
            TacticalCurrentOrderSignature newOrder,
            float nearDistance,
            float nearRotationDegrees)
        {
            if (!calledFromCampaign) return Safe(false, "battle-call-has-vanilla-duplicate-guard");
            if (oldOrder.IsEmpty || newOrder.IsEmpty) return Safe(false, "missing-order");
            if (oldOrder.UnitId != newOrder.UnitId || oldOrder.Type != newOrder.Type) return Safe(false, "different-order");

            float dx = oldOrder.X - newOrder.X;
            float dz = oldOrder.Z - newOrder.Z;
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);
            float rotationDelta = AbsAngleDifference(oldOrder.Rotation, newOrder.Rotation);
            if (distance <= Math.Max(0f, nearDistance) && rotationDelta <= Math.Max(0f, nearRotationDegrees))
                return Safe(true, "campaign-duplicate-near");

            return Safe(false, "campaign-material-change");
        }

        public static TacticalDiagnosticDecision ClassifySecondaryCourier(
            bool secondaryCourier,
            int orderQueueCount,
            int activeQueueIndex,
            int appendQueueIndex)
        {
            if (!secondaryCourier) return Safe(false, "primary-courier");
            if (orderQueueCount <= 1) return Safe(false, "single-queue");
            if (activeQueueIndex < 0 || appendQueueIndex < 0) return Safe(false, "unknown-index");
            if (activeQueueIndex != appendQueueIndex) return Safe(true, "secondary-courier-appended-to-latest");
            return Safe(false, "secondary-courier-active-queue");
        }

        public static bool ShouldSuppressFallbackRetreatException(string methodName, Exception exception)
        {
            if (!(exception is NullReferenceException)) return false;
            return methodName == "MicroAICheckForRetreats" || methodName == "CheckLineFallbacks";
        }

        public static TacticalDiagnosticDecision ClassifyWaypointQueueDrift(
            bool useOrderDelay,
            bool activeMoveOrder,
            bool queueAdded,
            int pathCountBefore,
            int pathCountAfter)
        {
            if (!useOrderDelay) return Safe(false, "delay-disabled");
            if (!activeMoveOrder) return Safe(false, "no-active-move-order");
            if (queueAdded) return Safe(false, "queue-added");
            if (pathCountAfter != pathCountBefore) return Safe(true, "path-mutated-without-queue");
            return Safe(false, "path-stable");
        }

        private static TacticalDiagnosticDecision Safe(bool risk, string reason)
        {
            return new TacticalDiagnosticDecision(risk, reason);
        }

        private static float AbsAngleDifference(float a, float b)
        {
            float delta = Math.Abs((a - b) % 360f);
            return delta > 180f ? 360f - delta : delta;
        }
    }
}
```

- [ ] **Step 5: Run tests and confirm pass**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all console harness tests pass.

## Task 3: Add Tactical Bug Telemetry Config

**Files:**
- Modify: `src/WhiskeyRealism/Plugin.cs`

- [ ] **Step 1: Add config fields**

In `Plugin.cs`, near the tactical config fields, add:

```csharp
internal ConfigEntry<bool> EnableTacticalBugTelemetry;
internal ConfigEntry<bool> EnableTacticalFallbackRetreatNullGuard;
```

- [ ] **Step 2: Bind config values**

In `Awake()`, immediately after `EnableTacticalObserver` / observer throttle bindings, add:

```csharp
EnableTacticalBugTelemetry = Config.Bind(
    "Tactical",
    "Enable Tactical Bug Telemetry",
    false,
    "Default OFF. Emits focused read-only telemetry for tactical order/current-order bug hunts; does not change battlefield behavior.");
EnableTacticalFallbackRetreatNullGuard = Config.Bind(
    "Tactical",
    "Enable Tactical Fallback Retreat Null Guard",
    false,
    "Default OFF. Suppresses NullReferenceException from two vanilla tactical fallback/retreat methods during focused bug-smoke runs; all non-null exceptions still propagate.");
```

- [ ] **Step 3: Build-check config compile**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: console harness passes.

## Task 4: Extend #35 Tactical Observer For Bug Telemetry

**Files:**
- Modify: `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs`

- [ ] **Step 1: Add enable helper**

In `TacticalObserverPatch`, add:

```csharp
private static bool BugTelemetryEnabled()
{
    return Plugin.Instance != null &&
        Plugin.Instance.Enabled.Value &&
        Plugin.Instance.EnableTacticalBugTelemetry.Value;
}
```

Keep existing `Enabled()` for B0/B2 observer output. Bug telemetry must be independently toggleable so focused bug hunts do not require all tactical summaries.

- [ ] **Step 2: Add `CheckCurrentOrderUpdate` Prefix/Postfix observer**

Add a Harmony Prefix/Postfix pair for `AIBattle.CheckCurrentOrderUpdate`. Use reflection only for `DLC_WL.givenorder` fields if direct field access is unavailable. The emitted line must include old type, new type, unit id/name, `calledfromcampaign`, duplicate decision, and session delta.

Required log shape:

```text
[TacticalCurrentOrder] calledFromCampaign=True unit=<name>#<id> oldType=11 newType=11 duplicateRisk=True reason=campaign-duplicate-near sessionBefore=4 sessionAfter=5
```

Implementation sketch:

```csharp
[HarmonyPatch(typeof(AIBattle), "CheckCurrentOrderUpdate")]
[HarmonyPrefix]
internal static void CheckCurrentOrderUpdatePrefix(
    Regiment unit,
    int type,
    Vector3 position,
    string destinationname,
    float rotation,
    float width,
    float depth,
    bool calledfromcampaign,
    out TacticalCurrentOrderSignature __state)
{
    __state = CurrentOrderSignature();
}

[HarmonyPatch(typeof(AIBattle), "CheckCurrentOrderUpdate")]
[HarmonyPostfix]
internal static void CheckCurrentOrderUpdatePostfix(
    Regiment unit,
    int type,
    Vector3 position,
    bool calledfromcampaign,
    TacticalCurrentOrderSignature __state)
{
    if (!BugTelemetryEnabled()) return;
    var next = new TacticalCurrentOrderSignature(SafeInstanceId(unit), type, position.x, position.z, rotation: 0f);
    var decision = TacticalBattlefieldBugDiagnostics.ClassifyCurrentOrder(
        calledfromcampaign,
        __state,
        next,
        nearDistance: 110f,
        nearRotationDegrees: 35f);
    EmitDirect("CurrentOrder", SafeInstanceId(unit) + "|" + type + "|" + decision.Reason,
        "[TacticalCurrentOrder] calledFromCampaign=" + calledfromcampaign +
        " unit=" + SafeUnitName(unit) +
        " oldType=" + __state.Type +
        " newType=" + type +
        " duplicateRisk=" + decision.IsRisk +
        " reason=" + decision.Reason);
}
```

Adjust the exact signature to match the current compiled method parameters. If Harmony reports a missing target, inspect the decompile and update the argument list before proceeding.

- [ ] **Step 3: Add secondary courier queue-risk telemetry**

Extend the existing `AddOrderCourierlinePostfix(...)` observer. Capture queue count before and after with a Prefix state:

```csharp
internal readonly struct CourierQueueState
{
    public CourierQueueState(int queueCount, int appendIndex)
    {
        QueueCount = queueCount;
        AppendIndex = appendIndex;
    }

    public int QueueCount { get; }
    public int AppendIndex { get; }
}
```

Prefix:

```csharp
[HarmonyPatch(typeof(Regiment), "AddOrderCourierline")]
[HarmonyPrefix]
internal static void AddOrderCourierlinePrefix(Regiment __instance, bool secondarycourier, out CourierQueueState __state)
{
    int count = __instance != null && __instance.orderqueue != null ? __instance.orderqueue.Count : 0;
    __state = new CourierQueueState(count, count - 1);
}
```

Postfix decision:

```csharp
var decision = TacticalBattlefieldBugDiagnostics.ClassifySecondaryCourier(
    secondaryCourier,
    __state.QueueCount,
    activeQueueIndex: -1,
    appendQueueIndex: __state.AppendIndex);
if (decision.IsRisk || secondaryCourier)
{
    EmitDirect("CourierQueue", SafeInstanceId(__instance) + "|" + __state.QueueCount + "|" + decision.Reason,
        "[TacticalCourierQueue] owner=" + SafeUnitName(__instance) +
        " secondary=" + secondaryCourier +
        " queueCount=" + __state.QueueCount +
        " appendIndex=" + __state.AppendIndex +
        " risk=" + decision.IsRisk +
        " reason=" + decision.Reason);
}
```

Note: this telemetry cannot know the active `ProcessOrders()` index unless a later task instruments `ProcessOrders`. It still proves whether secondary couriers occur with multiple queues, which is the first runtime gate.

- [ ] **Step 4: Add delayed waypoint path-drift telemetry**

Add Prefix/Postfix for `BattleUnits.SetWaypoint(Regiment, Vector3, ...)`. Capture before path count and active move-order flags; after vanilla, classify path mutation without queue insertion.

Required log shape:

```text
[TacticalWaypointDrift] unit=<name>#<id> useDelay=True activeMoveOrder=True pathBefore=1 pathAfter=2 risk=True reason=path-mutated-without-queue
```

Implementation sketch:

```csharp
internal readonly struct WaypointState
{
    public WaypointState(int pathCount, bool activeMoveOrder)
    {
        PathCount = pathCount;
        ActiveMoveOrder = activeMoveOrder;
    }

    public int PathCount { get; }
    public bool ActiveMoveOrder { get; }
}

[HarmonyPatch(typeof(BattleUnits), "SetWaypoint", new[]
{
    typeof(Regiment), typeof(Vector3), typeof(bool), typeof(bool), typeof(float),
    typeof(bool), typeof(bool), typeof(float), typeof(int), typeof(bool),
    typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool)
})]
[HarmonyPrefix]
internal static void SetWaypointPrefix(Regiment reg, out WaypointState __state)
{
    bool active = reg != null && reg.ordertypeactive != null &&
        reg.ordertypeactive.Length > 1 &&
        (reg.ordertypeactive[0] || reg.ordertypeactive[1]);
    __state = new WaypointState(reg != null ? reg.regimentpaths : -1, active);
}
```

If Harmony cannot bind the full default-parameter signature, use `TargetMethod()` with `AccessTools.Method` and the exact parameter array.

- [ ] **Step 5: Add objective-chain and reserve support telemetry**

Reuse existing #35 Postfixes where possible:

- `AIBattle.CheckUseOfReserves` Postfix logs `[TacticalReserveMove]` with selected group, movement/path delta, and `PerformAIActionDLCWL` relation fields if observable.
- `AIBattle.UpdateMovingTargets` Postfix logs `[TacticalObjectiveMove]` exposure with center group and attached player-subordinate count. Treat it as exposure proof until a later path/position delta proves movement touched the player-subordinate attachment.

If `UpdateMovingTargets` is private and no current observer exists, add a Postfix target inside `TacticalObserverPatch` and keep it read-only.

- [ ] **Step 6: Run harness and build**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected: console harness passes; build completes with 0 errors.

Do not deploy yet if Task 5 null guard is not done and intended for the same DLL.

## Task 5: Add #43 Tactical Fallback/Retreat Null Guard

**Files:**
- Create: `src/WhiskeyRealism/Patches/TacticalFallbackRetreatNullGuardPatch.cs`
- Modify: `docs/patch-catalog.md`
- Modify: `docs/handoff.md`

- [ ] **Step 1: Create the Finalizer guard patch**

Create `src/WhiskeyRealism/Patches/TacticalFallbackRetreatNullGuardPatch.cs`:

```csharp
using System;
using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla tactical fallback/retreat loops dereference allattachedunits slots
    // without null guards. Suppress only NullReferenceException from those two
    // hot methods so one bad slot cannot spam or halt the tactical AI tick.
    internal static class TacticalFallbackRetreatNullGuardPatch
    {
        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableTacticalFallbackRetreatNullGuard.Value;
        }

        private static Exception Handle(string methodName, Exception exception)
        {
            if (!Enabled() || exception == null) return exception;
            if (!TacticalBattlefieldBugDiagnostics.ShouldSuppressFallbackRetreatException(methodName, exception))
                return exception;

            OnceLog.Warning(
                "tactical-fallback-retreat-null:" + methodName,
                "[Patch:TacticalFallbackRetreatNullGuard] suppressed NullReferenceException in " + methodName +
                "; vanilla likely had a null allattachedunits slot.");
            return null;
        }

        [HarmonyPatch(typeof(AIBattle), "MicroAICheckForRetreats")]
        internal static class Retreats
        {
            [HarmonyFinalizer]
            internal static Exception Finalizer(Exception __exception)
            {
                return Handle("MicroAICheckForRetreats", __exception);
            }
        }

        [HarmonyPatch(typeof(AIBattle), "CheckLineFallbacks")]
        internal static class Fallbacks
        {
            [HarmonyFinalizer]
            internal static Exception Finalizer(Exception __exception)
            {
                return Handle("CheckLineFallbacks", __exception);
            }
        }
    }
}
```

- [ ] **Step 2: Verify build**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected: console harness passes; build completes with 0 errors.

- [ ] **Step 3: Add patch catalog entry**

In `docs/patch-catalog.md`, reserve the next ordinal `#43`:

```markdown
| 43 | `TacticalFallbackRetreatNullGuardPatch` | Finalizer | `Patches/TacticalFallbackRetreatNullGuardPatch.cs` | `AIBattle.MicroAICheckForRetreats` (4817), `AIBattle.CheckLineFallbacks` (5118) | Tactical battlefield bug guard. Default-off for focused smoke runs. Suppresses method-level `NullReferenceException` from those two vanilla fallback/retreat loops, logs one bounded warning, and lets all non-null exceptions propagate. |
```

- [ ] **Step 4: Update handoff**

In `docs/handoff.md`, add a tactical bug-remediation note under the active workstream:

```markdown
Tactical bug-remediation plan is active at `docs/superpowers/plans/2026-05-07-tactical-battlefield-bug-remediation.md`. Immediate optional smoke guard is #43 `TacticalFallbackRetreatNullGuardPatch`; because it is method-level NRE containment for two vanilla fallback/retreat loops, keep it default-off until focused logs prove the bounded-warning/no-stack-spam behavior is worth enabling. Other tactical findings are telemetry-first and remain unproven until focused logs show `[TacticalCurrentOrder]`, `[TacticalWaypointDrift]`, `[TacticalCourierQueue]`, `[TacticalObjectiveMove]` exposure lines with path/position follow-up, or `[TacticalReserveMove]` risk lines.
```

## Task 6: Deploy And Smoke Telemetry/Null Guard

**Files:**
- Build output: `dist/WhiskeyRealism.dll`
- Deployed DLL: `/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll`
- Log: `/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log`

- [ ] **Step 1: Build**

Run:

```bash
./build.sh
```

Expected: build completes with 0 errors and writes `dist/WhiskeyRealism.dll`.

- [ ] **Step 2: Deploy**

Close GTCW first. Then run:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
```

Expected: no `Invalid argument` error. If Windows has the DLL locked, close the game and rerun.

- [ ] **Step 3: Verify deployed hash**

Run:

```bash
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: file sizes match and both SHA-256 hashes are identical.

- [ ] **Step 4: Enable focused config**

In `BepInEx/config/dev.kyle.whiskey-realism.cfg`, set:

```ini
[Tactical]
Enable Tactical Observer = true
Enable Tactical Bug Telemetry = true
Enable Tactical Fallback Retreat Null Guard = true
Enable W&L Tactical Charge Guard = true
Tactical Observer Verbose Logging = false
Tactical Observer Min Seconds Between Summaries = 30
```

`Enable Tactical Fallback Retreat Null Guard = true` is for this focused smoke run only. The shipped config default is `false` until runtime evidence proves the method-level NRE containment is worth enabling broadly.

- [ ] **Step 5: Runtime smoke**

Start or continue a W&L subordinate land battle and tail:

```bash
tail -n 240 "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Pass criteria:

- `TacticalObserverPatch wired`
- Existing `[TacticalPlayerOrder]` surface appears.
- If null fallback/retreat hazard occurs: one bounded `[Patch:TacticalFallbackRetreatNullGuard]` line, no repeated stack spam.
- If bug telemetry fires: bounded `[TacticalCurrentOrder]`, `[TacticalWaypointDrift]`, `[TacticalCourierQueue]`, `[TacticalObjectiveMove]`, or `[TacticalReserveMove]` lines.
- No repeated `Exception`, `TargetInvocationException`, Harmony patch failure, or `missing-anchor` warnings.

## Task 7: Decision Gate For Behavior Fixes After Telemetry

**Files:**
- Read: `BepInEx/LogOutput.log`
- Modify later only after approval/proof:
  - `src/WhiskeyRealism/Patches/*.cs`
  - `docs/bug-fixes/vanilla-tactical-battlefield.md`
  - `docs/patch-catalog.md`
  - `docs/handoff.md`

- [ ] **Step 1: Classify telemetry**

Use this matrix:

| Marker | If observed | Next action |
|---|---|---|
| `[TacticalCourierQueue] risk=True` | Not observed. Five markers fired, all `risk=False` (`single-queue` or `unknown-index`). | Keep telemetry/soak. Ask user before transpiler/replacement only after wrong-index proof. |
| `[TacticalCurrentOrder] duplicateRisk=True` with session increment | Not observed. One marker fired with `calledFromCampaign=False duplicateRisk=False`. | Need campaign-call proof before any Prefix duplicate guard. |
| `[TacticalWaypointDrift] risk=True` | Not observed. | Widen/prove caller-specific path mutation; do not patch generic `SetWaypoint` globally. |
| `[TacticalObjectiveMove] attachedUnderCommanderCount>0` | Observed repeatedly. Eleven markers showed `attachedUnderCommanderCount=1 risk=True`, proving player-subordinate attachment exposure but not movement/path mutation. | Use `[TacticalObjectiveMutation]` proof telemetry before considering any narrow `UpdateMovingTargets` guard preserving `CheckCurrentOrderUpdate`. |
| `[TacticalReserveMove]` direct path repeated in active player-subordinate context | Not observed. | Widen reserve direct-path telemetry before reserve doctrine/order-delay conversion. |
| No risk markers | Bugs remain decompile-backed or theoretical. | Keep telemetry default-off and do not add behavior patches. |

- [ ] **Step 2: Approval gate for transpilers**

If `BUG-TAC-001` or `BUG-TAC-007` requires changing private vanilla internals that cannot be safely Prefix/Postfix guarded, stop and ask:

```text
The next fix requires a targeted transpiler/replacement against <method>. Evidence is <log marker / decompile anchor>. Do you want me to implement that patch?
```

Do not proceed without explicit approval.

## Task 8: Closeout

**Files:**
- Modify: `docs/bug-fixes/vanilla-tactical-battlefield.md`
- Modify: `docs/bug-fixes/README.md`
- Modify: `docs/handoff.md`
- Modify: `docs/patch-catalog.md`
- Modify: `MEMORY.md` only if the user explicitly asks for repo memory update; otherwise leave it alone.

- [ ] **Step 1: Run final verification**

Run:

```bash
git diff --check
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected:

- `git diff --check` prints no whitespace errors.
- Console harness passes.
- Build completes with 0 errors.
- SHA-256 hashes match after deploy.

- [ ] **Step 2: Update docs to exact state**

Record:

- whether #43 shipped;
- deployed DLL hash and size;
- whether runtime smoke observed the null guard;
- which telemetry markers fired;
- which bug IDs remain `Needs repro`, `Backlog`, `Confirmed`, or `Shipped`;
- whether any transpiler/replacement work was deferred for approval.

- [ ] **Step 3: Commit**

Use focused commit messages. Suggested split:

```bash
git add docs/bug-fixes/README.md docs/bug-fixes/vanilla-tactical-battlefield.md docs/superpowers/plans/2026-05-07-tactical-battlefield-bug-remediation.md
git commit -m "docs: plan tactical battlefield bug remediation"

git add src/WhiskeyRealism/Tactical/TacticalBattlefieldBugDiagnostics.cs tests/WhiskeyRealism.Tests/Program.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "test: add tactical battlefield bug diagnostics"

git add src/WhiskeyRealism/Plugin.cs src/WhiskeyRealism/Patches/TacticalObserverPatch.cs src/WhiskeyRealism/Patches/TacticalFallbackRetreatNullGuardPatch.cs docs/patch-catalog.md docs/handoff.md docs/bug-fixes/vanilla-tactical-battlefield.md
git commit -m "fix: guard tactical fallback retreat null loops"
```

If existing B2 changes are part of the same dirty worktree, keep them in their existing B2 commits and do not mix unrelated tactical bug-remediation files into the B2 commit.

## Self-Review Notes

- Every bug-hunt finding is represented by a `BUG-TAC-*` item.
- Immediate behavior change is limited to the confirmed fallback/retreat null hazard.
- Existing B1 #41/#42 remains the charge/feud behavior surface; this plan does not duplicate it.
- Unproven objective-chain, current-order, waypoint drift, reserve, incident, and reserve-random findings are telemetry-first.
- Broad `SetWaypoint` and unapproved transpiler work are explicitly excluded.
- Verification includes console harness, build, deploy, hash match, and runtime smoke.
