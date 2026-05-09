# Tactical Deployment Observer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a read-only tactical deployment observer that correctly separates vanilla initial positioning, AI deployment-zone placement, and end-of-day redeployment telemetry.

**Architecture:** Keep behavior pure telemetry: Harmony patches take before/after snapshots, `TacticalDeploymentTelemetry` computes deterministic deltas, and logs summarize bounded movement evidence. Match groups by `Regiment.GetInstanceID()` only because vanilla `DoUnitPositioning()` reorders `BattleUnits.grp[]`. Use cached `AccessTools.Field` reads for vanilla state and degrade to one-time warnings on missing anchors.

**Tech Stack:** BepInEx 5 + HarmonyX, C# netstandard2.1, Grand Tactician vanilla decompile anchors in `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`, console harness at `tests/WhiskeyRealism.Tests/`.

---

## File Structure

- `src/WhiskeyRealism/Tactical/TacticalDeploymentTelemetry.cs`: pure snapshot/delta/log-format helper. Owns phase strings, stable group matching, bounded top-move formatting, and no Unity dependencies.
- `src/WhiskeyRealism/Patches/TacticalDeploymentObserverPatch.cs`: Harmony runtime observer for `BattleUnits.DoUnitPositioning`, `BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew`, and `BattleUI.SetActiveDeploymentPhase`.
- `src/WhiskeyRealism/Plugin.cs`: config entry and patch registration only.
- `tests/WhiskeyRealism.Tests/Program.cs`: harness coverage for delta math, stable reorder matching, skipped phase output, and summary signature fields.
- `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`: explicit compile include for the new tactical helper.
- `docs/patch-catalog.md`, `docs/handoff.md`: patch ordinal #58, marker names, smoke expectations, and deployed hash after verification.

## Vanilla Anchors

- `BattleController.Update` frame-30 AI placement calls: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:23988`.
- `BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew(int)` method: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:85524`.
- DoPlacement player/tutorial early return: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:85588`.
- DoPlacement initial-placement flag: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:85643`.
- `BattleUnits.CheckTimeIssues` `eodcycle == 4` branch: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:86440`.
- `BattleUI.SetActiveDeploymentPhase(active:false)` nested DoPlacement calls and `BU.eodcycle = 0`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:164875`.
- `BattleUnits.DoUnitPositioning()` method and `grp` reorder: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:87720`.
- `BattleUnits.SetGroupFormation(...)` direct group and attached-unit movement: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:91822`.

---

### Task 1: Lock Telemetry Tests First

**Files:**
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Test target: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`

- [ ] **Step 1: Add tests for stable reorder matching and skipped phase output**

Add two harness tests next to the existing tactical deployment tests:

```csharp
private static void TestTacticalDeploymentStableMatchAcrossReorder()
{
    var before = new TacticalDeploymentTelemetry.Snapshot(
        "before",
        0,
        0,
        0,
        new[]
        {
            new TacticalDeploymentTelemetry.GroupSnapshot("101", "A", 0, 10f, 10f, 0, 0, 0, 0f, 0f),
            new TacticalDeploymentTelemetry.GroupSnapshot("202", "B", 0, 20f, 20f, 0, 0, 0, 0f, 0f),
        },
        TacticalDeploymentTelemetry.PhaseInitialPositioning);

    var after = new TacticalDeploymentTelemetry.Snapshot(
        "after",
        0,
        0,
        0,
        new[]
        {
            new TacticalDeploymentTelemetry.GroupSnapshot("202", "B", 0, 20f, 20f, 0, 0, 0, 0f, 0f),
            new TacticalDeploymentTelemetry.GroupSnapshot("101", "A", 0, 110f, 10f, 0, 0, 0, 0f, 0f),
        },
        TacticalDeploymentTelemetry.PhaseInitialPositioning);

    var delta = TacticalDeploymentTelemetry.ComputeDelta(before, after, 50f);
    AssertEqual(2, delta.MatchedGroups, "reorder should still match both groups");
    AssertEqual(0, delta.NewGroups, "reorder should not create new groups");
    AssertEqual(0, delta.RemovedGroups, "reorder should not remove groups");
    AssertEqual(1, delta.MovedGroups, "only the physically moved group should count as moved");
    AssertEqual(1, delta.LargeMoves, "large move threshold should count the moved group");
}

private static void TestTacticalDeploymentSkippedPhaseSummary()
{
    var before = new TacticalDeploymentTelemetry.Snapshot(
        "before",
        1,
        0,
        0,
        new[]
        {
            new TacticalDeploymentTelemetry.GroupSnapshot("303", "C", 1, 50f, 50f, 0, 0, 0, 0f, 0f),
        },
        TacticalDeploymentTelemetry.PhaseSkipped);

    var after = new TacticalDeploymentTelemetry.Snapshot(
        "after",
        1,
        0,
        0,
        new[]
        {
            new TacticalDeploymentTelemetry.GroupSnapshot("303", "C", 1, 50f, 50f, 0, 0, 0, 0f, 0f),
        },
        TacticalDeploymentTelemetry.PhaseSkipped);

    var delta = TacticalDeploymentTelemetry.ComputeDelta(before, after, 100f);
    var summary = TacticalDeploymentTelemetry.FormatSummary("DoPlacementAIUnitsWithinDeploymentzoneNew", delta);

    AssertContains(summary, "[TacDeployObs]", "summary marker");
    AssertContains(summary, "phase=skipped", "skipped phase");
    AssertContains(summary, "surface=DoPlacementAIUnitsWithinDeploymentzoneNew", "surface");
    AssertContains(summary, "matched=1", "matched count");
    AssertContains(summary, "moved=0", "no movement on skipped vanilla return");
}
```

Call both tests from `Main()` after the existing tactical deployment telemetry tests. Ensure `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` contains:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalDeploymentTelemetry.cs" Link="Tactical\TacticalDeploymentTelemetry.cs" />
```

- [ ] **Step 2: Run harness and confirm the new tests fail before implementation**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected before Task 2: compile failure for missing `PhaseInitialPositioning` / `PhaseSkipped`, or assertion failure if marker and phase semantics still use the old contract.

- [ ] **Step 3: Commit test-only changes**

```bash
git add tests/WhiskeyRealism.Tests/Program.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "test(tactical): cover deployment observer phases"
```

---

### Task 2: Fix Pure Telemetry Semantics

**Files:**
- Modify: `src/WhiskeyRealism/Tactical/TacticalDeploymentTelemetry.cs`
- Test target: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`

- [ ] **Step 1: Add explicit phase constants and constructor support**

In `TacticalDeploymentTelemetry`, define:

```csharp
public const string PhaseInitialPositioning = "initial-positioning";
public const string PhaseInitial = "initial";
public const string PhaseEod = "eod";
public const string PhaseSkipped = "skipped";

public static string PhaseFromPrefix(bool skipped, bool initialPositioning, int eodCycle, int battlePassedDays)
{
    if (skipped)
    {
        return PhaseSkipped;
    }

    if (initialPositioning)
    {
        return PhaseInitialPositioning;
    }

    return eodCycle > 0 || battlePassedDays > 0 ? PhaseEod : PhaseInitial;
}
```

Update `Snapshot` so callers pass the phase captured in the Prefix:

```csharp
public Snapshot(string label, int alliance, int eodCycle, int battlePassedDays, IEnumerable<GroupSnapshot> groups, string phase = null)
{
    Label = label ?? string.Empty;
    Alliance = alliance;
    EodCycle = eodCycle;
    BattlePassedDays = battlePassedDays;
    Phase = NormalizePhase(phase, eodCycle, battlePassedDays);
    Groups = groups?.ToArray() ?? Array.Empty<GroupSnapshot>();
}
```

`NormalizePhase` must accept only the four legal phase strings and fall back to `eod` when `eodCycle > 0 || battlePassedDays > 0`, otherwise `initial`.

- [ ] **Step 2: Rename log markers and keep signature stable**

Update `FormatSummary` and move formatting:

```csharp
return string.Format(
    CultureInfo.InvariantCulture,
    "[TacDeployObs] surface={0} phase={1} alliance={2} eod={3} days={4} before={5} after={6} matched={7} moved={8} largeMoves={9} new={10} removed={11} maxMove={12:0.0} avgMove={13:0.0} signature={14}",
    surface,
    delta.Phase,
    delta.Alliance,
    delta.EodCycle,
    delta.BattlePassedDays,
    delta.BeforeGroups,
    delta.AfterGroups,
    delta.MatchedGroups,
    delta.MovedGroups,
    delta.LargeMoves,
    delta.NewGroups,
    delta.RemovedGroups,
    delta.MaxMove,
    delta.AverageMove,
    delta.Signature);
```

Move rows must start with `[TacDeployObsMove]`. The summary signature must continue to include surface-independent fields that identify counts and movement totals.

- [ ] **Step 3: Run harness and fix only telemetry-level failures**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected after this task: all pure telemetry tests pass, including reorder matching and skipped phase summary.

- [ ] **Step 4: Commit telemetry changes**

```bash
git add src/WhiskeyRealism/Tactical/TacticalDeploymentTelemetry.cs tests/WhiskeyRealism.Tests/Program.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "fix(tactical): stabilize deployment telemetry deltas"
```

---

### Task 3: Fix Harmony Patch Surfaces and Runtime Anchors

**Files:**
- Modify: `src/WhiskeyRealism/Patches/TacticalDeploymentObserverPatch.cs`
- Modify: `src/WhiskeyRealism/Plugin.cs`
- Build target: `./build.sh`

- [ ] **Step 1: Add the missing `DoUnitPositioning` patch**

Add a Harmony patch class:

```csharp
[HarmonyPatch(typeof(BattleUnits), "DoUnitPositioning")]
internal static class BattleUnitsDoUnitPositioningObserverPatch
{
    private const string Surface = "DoUnitPositioning";

    private static void Prefix(BattleUnits __instance, ref TacticalDeploymentObserverPatch.ObservationState __state)
    {
        __state = TacticalDeploymentObserverPatch.CaptureState(
            __instance,
            Surface,
            -1,
            TacticalDeploymentTelemetry.PhaseInitialPositioning,
            false);
    }

    private static void Postfix(BattleUnits __instance, TacticalDeploymentObserverPatch.ObservationState __state)
    {
        TacticalDeploymentObserverPatch.EmitDelta(
            __instance,
            Surface,
            -1,
            TacticalDeploymentTelemetry.PhaseInitialPositioning,
            __state,
            false);
    }
}
```

- [ ] **Step 2: Fix snapshot keys to ignore unstable `grp[]` index**

`SnapshotGroup` must create the key exactly this way:

```csharp
var key = regiment.GetInstanceID().ToString(CultureInfo.InvariantCulture);
return new TacticalDeploymentTelemetry.GroupSnapshot(
    key,
    name,
    alliance,
    x,
    z,
    formation,
    formationOrdered,
    hierarchy,
    morale,
    condition);
```

Do not append array index. Keep hierarchy as a field, not as identity.

- [ ] **Step 3: Use cached reflection for vanilla `BattleUnits` fields**

Add cached fields:

```csharp
private static readonly FieldInfo GrpField = AccessTools.Field(typeof(BattleUnits), "grp");
private static readonly FieldInfo EodCycleField = AccessTools.Field(typeof(BattleUnits), "eodcycle");
private static readonly FieldInfo BattlePassedDaysField = AccessTools.Field(typeof(BattleUnits), "battlepasseddays");
```

Read with guarded helpers:

```csharp
private static Grp[] ReadGroups(BattleUnits battleUnits)
{
    if (battleUnits == null || GrpField == null)
    {
        OnceLog.Warning("tactical-deployment-observer:missing-grp", "Missing BattleUnits.grp; tactical deployment observer snapshot disabled.");
        return Array.Empty<Grp>();
    }

    try
    {
        return GrpField.GetValue(battleUnits) as Grp[] ?? Array.Empty<Grp>();
    }
    catch (Exception ex)
    {
        OnceLog.Warning("tactical-deployment-observer:read-grp", "Failed to read BattleUnits.grp: " + ex.GetType().Name);
        return Array.Empty<Grp>();
    }
}
```

Use equivalent guarded integer readers for `eodcycle` and `battlepasseddays`; missing fields return `0` with one-time warnings.

- [ ] **Step 4: Use Prefix snapshot state for phase and EOD metadata**

Define:

```csharp
internal sealed class ObservationState
{
    public TacticalDeploymentTelemetry.Snapshot Before { get; set; }
    public string Phase { get; set; }
    public bool SuppressOuterDelta { get; set; }
    public bool Noop { get; set; }
}
```

`CaptureState` returns `Before` with phase already assigned. `EmitDelta` captures the Postfix snapshot using `state.Phase` and then formats the delta. Summary `phase`, `eod`, and `days` must come from `Before`, not from a Postfix read after vanilla zeroes `eodcycle`.

- [ ] **Step 5: Detect vanilla DoPlacement early-return**

Implement:

```csharp
private static bool IsDoPlacementSkipped(int forAlliance)
{
    try
    {
        return (GameVars.playeralliance == forAlliance && !GameVars.ai_vs_ai)
            || (GameVars.tutorialactive && !Tutorial.engaged);
    }
    catch (Exception ex)
    {
        OnceLog.Warning("tactical-deployment-observer:skip-detect", "Failed to evaluate DoPlacement skip guard: " + ex.GetType().Name);
        return false;
    }
}
```

Use `TacticalDeploymentTelemetry.PhaseFromPrefix(skipped, false, before.EodCycle, before.BattlePassedDays)` for DoPlacement state. Still emit one `[TacDeployObs]` summary row when skipped so player-alliance calls are visible as skipped, not misclassified as converged deployment.

- [ ] **Step 6: Suppress outer SetActive EOD movement delta and skip campaign-map calls**

For `BattleUI.SetActiveDeploymentPhase`, Prefix must no-op when `__instance.IsCampaign` is true. When `active == false` and Prefix snapshot has `EodCycle > 0`, set `SuppressOuterDelta = true`.

Postfix must always emit the phase row for non-campaign calls:

```csharp
Plugin.Log.LogInfo(string.Format(
    CultureInfo.InvariantCulture,
    "[TacticalDeploymentPhase] action={0} calledFromSave={1} eod={2} days={3} groups={4} outerDeltaSuppressed={5}",
    active ? "open" : "close",
    calledfromsave,
    before.EodCycle,
    before.BattlePassedDays,
    before.Groups.Length,
    state.SuppressOuterDelta));
```

If `SuppressOuterDelta` is true, do not emit an outer `[TacDeployObs]` movement summary; the two nested DoPlacement Postfixes own movement deltas.

- [ ] **Step 7: Build and commit runtime patch changes**

Run:

```bash
./build.sh
```

Expected: `Build succeeded` and `dist/WhiskeyRealism.dll` created.

Then:

```bash
git add src/WhiskeyRealism/Patches/TacticalDeploymentObserverPatch.cs src/WhiskeyRealism/Plugin.cs
git commit -m "fix(tactical): observe deployment surfaces correctly"
```

---

### Task 4: Documentation, Deploy Verification, and Closeout

**Files:**
- Modify: `docs/patch-catalog.md`
- Modify: `docs/handoff.md`
- Verify: `dist/WhiskeyRealism.dll` and deployed plugin DLL

- [ ] **Step 1: Update docs for the final observer contract**

`docs/patch-catalog.md` must list patch ordinal `#58` for the tactical deployment observer and name all three surfaces. `docs/handoff.md` must state the observer is read-only, uses `[TacDeployObs]`, `[TacDeployObsMove]`, and `[TacticalDeploymentPhase]`, and requires runtime smoke evidence before behavioral deployment work begins.

- [ ] **Step 2: Run full harness and build**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected: harness passes and build succeeds.

- [ ] **Step 3: Deploy DLL and verify hash match**

Run with the game closed:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: identical sizes and identical SHA-256 hashes.

- [ ] **Step 4: Commit docs and verified implementation**

```bash
git add docs/patch-catalog.md docs/handoff.md
git commit -m "docs(tactical): document deployment observer smoke gate"
```

- [ ] **Step 5: Final diff review**

Run:

```bash
git status --short --branch
git log --oneline --decorate -5
```

Expected: branch contains the plan/test/implementation/doc commits and no unrelated unstaged edits. Do not push or merge until the user asks for the remote checkpoint after reviewing the final status.

