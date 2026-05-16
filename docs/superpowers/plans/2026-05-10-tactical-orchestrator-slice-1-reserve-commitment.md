# Tactical Orchestrator Slice 1 Reserve Commitment Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Status as of 2026-05-14:** implementation is merged to `main` and now
> superseded by the current tactical completion DLL
> `cfdb9018bc0cb7c0fcb7ba1e28acac0b1b119243856ef3a027716f8b9b930e75`
> (1245184 bytes; 1075 PASS). Slice 1 remains here only for focused reserve-gate
> smoke traceability and final archive closeout. Current runtime truth lives in
> [`docs/tactical-orchestrator.md`](../../tactical-orchestrator.md) and
> [`docs/tactical-operations-ledger.md`](../../tactical-operations-ledger.md).

**Goal:** Make the smoke-confirmed command-node tree influence tactical reserve commitment by denying premature vanilla reserve movement for command nodes resolved as `Reserve`, while preserving vanilla movement, #56 order-delay conversion, and #57 reserve-list mutation when the orchestrator role allows it.

**Architecture:** Add a pure `TacticalReserveCommitGate` decision helper under `Tactical/Orchestrator/`, then add a default-off Harmony Prefix/Postfix on `AIBattle.CheckUseOfReserves(Regiment)` that snapshots attached-unit paths before vanilla, resolves the group's command-node intent after vanilla, and rolls back newly-created direct reserve paths only when the resolved role says to hold reserves. Extend existing #57 `BattleReserveDoctrinePatch` so reserve-list bias also consults command-node intent before moving a reserve to the front of `objectivechain[i].reservegroups`.

**Tech Stack:** C# netstandard2.1, BepInEx 5.4.x, HarmonyX, Grand Tactician `AIBattle` / `Regiment` / `BattleUnits` runtime anchors, console harness tests in `tests/WhiskeyRealism.Tests`.

---

## Source Anchors

- Slice spec: `docs/superpowers/specs/2026-05-09-tactical-orchestrator-remaining-patches-design.md`
- Slice 0 command resolver: `src/WhiskeyRealism/Tactical/Orchestrator/CommandIntentResolver.cs`
- Slice 0 army state: `src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs`
- Runtime side lookup: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinator.cs`
- Existing direct reserve path conversion: `src/WhiskeyRealism/Patches/TacticalReserveOrderDelayGuardPatch.cs`
- Existing reserve-list mutation: `src/WhiskeyRealism/Patches/BattleReserveDoctrinePatch.cs`
- Existing withdrawal/help-request observer on same vanilla method: `src/WhiskeyRealism/Patches/B8CheckUseOfReservesPatch.cs`
- Config owner: `src/WhiskeyRealism/Plugin.cs`
- Test harness registration: `tests/WhiskeyRealism.Tests/Program.cs`
- Test project explicit compile list: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`

## Vanilla Anchors

- `AIBattle.CheckUseOfReserves(Regiment aigroup)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:6062`
  - Requires `aigroup.unittyp > 13`.
  - Selects an outflanked attached combat unit.
  - Selects an unengaged attached infantry/cavalry reserve.
  - Issues direct movement at line `6170` through `Regiment.RegimentSetPath(...)`.
  - Sets `regiment2.doublequick = true`.
- `AIBattle.AssignReserves()` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:7017`
  - Mutates `objectivechain[i].reservegroups`.
  - Existing #57 already snapshots and restores list membership around reorder bias.

## Non-Goals

- Do not force reserves to commit earlier than vanilla. Slice 1 is a gate, not a new reserve dispatcher.
- Do not remove #56. If Slice 1 allows vanilla reserve movement, #56 may still convert the direct path into a delayed order when its flag is enabled.
- Do not mutate player-CIC or player-subordinate groups.
- Do not create hard-coded corps/division/brigade orchestrator classes.
- Do not construct command trees inside Harmony patches. Patches read the latest `ArmyOrchestrator` state.

## File Map

- Create `src/WhiskeyRealism/Tactical/Orchestrator/TacticalReserveCommitGate.cs`
  - Pure decision helper for role-based reserve-commit allow/deny.
- Create `src/WhiskeyRealism/Patches/BattleReserveCommitGatePatch.cs`
  - Default-off Prefix/Postfix on `AIBattle.CheckUseOfReserves`.
  - Snapshots attached-unit path state before vanilla.
  - Resolves command-node intent after vanilla.
  - Rolls back only the paths/direct movement that vanilla added when the pure gate denies.
- Modify `src/WhiskeyRealism/Plugin.cs`
  - Add `EnableTacticalOrchestratorReserveCommitGate` config entry.
- Modify `src/WhiskeyRealism/Patches/BattleReserveDoctrinePatch.cs`
  - Before #57 reorders reserve groups, consult command-node intent for each reserve candidate.
  - Never prioritize a candidate whose resolved command role is `Reserve`.
- Modify `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
  - Add explicit compile entry for `TacticalReserveCommitGate.cs`.
- Modify `tests/WhiskeyRealism.Tests/Program.cs`
  - Add pure gate tests and register them in the test list.
- Modify docs after implementation:
  - `docs/patch-catalog.md`
  - `docs/handoff.md`
  - `MEMORY.md`

## Implementation Tasks

### Task 1: Add Pure Reserve Commit Gate

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalReserveCommitGate.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add failing harness registrations**

In `tests/WhiskeyRealism.Tests/Program.cs`, add these entries near the other tactical orchestrator tests:

```csharp
("tactical reserve commit gate observes when vanilla did not move", TacticalReserveCommitGateObservesWhenNoVanillaMove),
("tactical reserve commit gate denies reserve role movement", TacticalReserveCommitGateDeniesReserveRoleMovement),
("tactical reserve commit gate allows main understrength movement", TacticalReserveCommitGateAllowsMainUnderstrengthMovement),
("tactical reserve commit gate allows fallback screen movement", TacticalReserveCommitGateAllowsFallbackScreenMovement),
("tactical reserve commit gate observes player controlled group", TacticalReserveCommitGateObservesPlayerControlledGroup),
("tactical reserve commit gate allows already engaged reserve", TacticalReserveCommitGateAllowsAlreadyEngagedReserve),
("tactical reserve list bias rejects reserve role candidate", TacticalReserveListBiasRejectsReserveRoleCandidate),
```

Add these test methods near the existing command-tree tests:

```csharp
private static TacticalReserveCommitGate.Input ReserveGateInput(
    bool vanillaCommitted = true,
    bool resolved = true,
    DirectChildRole role = DirectChildRole.Reserve,
    bool playerControlled = false,
    bool committedUnitAlreadyEngaged = false,
    float ownStrengthRatio = 1.0f,
    float localOdds = 1.0f)
{
    return new TacticalReserveCommitGate.Input(
        vanillaCommitted,
        new CommandIntentResolution(
            resolved,
            new CommandNodeIntent(
                "node-200",
                "node-200",
                role,
                DirectChildAxis.Hold,
                primarySector: 2,
                supportPriority: 50,
                aggressionBias01: 0.5f,
                depth: 1),
            resolved ? "exact-command-node" : "command-node-not-found"),
        playerControlled,
        committedUnitAlreadyEngaged,
        ownStrengthRatio,
        localOdds);
}

private static void TacticalReserveCommitGateObservesWhenNoVanillaMove()
{
    var d = TacticalReserveCommitGate.Decide(ReserveGateInput(vanillaCommitted: false));
    AssertEqual(TacticalReserveCommitGate.Action.Observe, d.Action, "action");
    AssertEqual("no-vanilla-commit", d.Reason, "reason");
}

private static void TacticalReserveCommitGateDeniesReserveRoleMovement()
{
    var d = TacticalReserveCommitGate.Decide(ReserveGateInput(role: DirectChildRole.Reserve));
    AssertEqual(TacticalReserveCommitGate.Action.Deny, d.Action, "action");
    AssertEqual("role-reserve-hold", d.Reason, "reason");
}

private static void TacticalReserveCommitGateAllowsMainUnderstrengthMovement()
{
    var d = TacticalReserveCommitGate.Decide(ReserveGateInput(role: DirectChildRole.Main, ownStrengthRatio: 0.60f));
    AssertEqual(TacticalReserveCommitGate.Action.Allow, d.Action, "action");
    AssertEqual("main-understrength-release", d.Reason, "reason");
}

private static void TacticalReserveCommitGateAllowsFallbackScreenMovement()
{
    var d = TacticalReserveCommitGate.Decide(ReserveGateInput(role: DirectChildRole.Fallback, localOdds: 0.70f));
    AssertEqual(TacticalReserveCommitGate.Action.Allow, d.Action, "action");
    AssertEqual("fallback-screen-retreat", d.Reason, "reason");
}

private static void TacticalReserveCommitGateObservesPlayerControlledGroup()
{
    var d = TacticalReserveCommitGate.Decide(ReserveGateInput(playerControlled: true));
    AssertEqual(TacticalReserveCommitGate.Action.Observe, d.Action, "action");
    AssertEqual("player-controlled", d.Reason, "reason");
}

private static void TacticalReserveCommitGateAllowsAlreadyEngagedReserve()
{
    var d = TacticalReserveCommitGate.Decide(ReserveGateInput(role: DirectChildRole.Reserve, committedUnitAlreadyEngaged: true));
    AssertEqual(TacticalReserveCommitGate.Action.Allow, d.Action, "action");
    AssertEqual("already-committed-contact", d.Reason, "reason");
}

private static void TacticalReserveListBiasRejectsReserveRoleCandidate()
{
    var reserve = new CommandIntentResolution(
        true,
        new CommandNodeIntent("node-200", "node-200", DirectChildRole.Reserve, DirectChildAxis.Hold, 2, 50, 0.5f, 1),
        "exact-command-node");
    var main = new CommandIntentResolution(
        true,
        new CommandNodeIntent("node-201", "node-201", DirectChildRole.Main, DirectChildAxis.SectorAxis, 2, 90, 0.8f, 1),
        "exact-command-node");

    AssertFalse(TacticalReserveCommitGate.PermitReserveListBias(reserve), "reserve role is not list-bias eligible");
    AssertTrue(TacticalReserveCommitGate.PermitReserveListBias(main), "main role can be list-bias eligible");
}
```

- [ ] **Step 2: Register the new source file in the test project**

In `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, add the compile line next to the other orchestrator files:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\TacticalReserveCommitGate.cs" Link="Orchestrator\TacticalReserveCommitGate.cs" />
```

- [ ] **Step 3: Run the harness and verify it fails for the missing type**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile failure naming `TacticalReserveCommitGate` as missing.

- [ ] **Step 4: Create the pure helper**

Create `src/WhiskeyRealism/Tactical/Orchestrator/TacticalReserveCommitGate.cs`:

```csharp
using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    internal static class TacticalReserveCommitGate
    {
        public enum Action
        {
            Observe = 0,
            Allow = 1,
            Deny = 2,
        }

        public readonly struct Input
        {
            public Input(
                bool vanillaCommitted,
                CommandIntentResolution resolution,
                bool playerControlled,
                bool committedUnitAlreadyEngaged,
                float ownStrengthRatio,
                float localOdds)
            {
                VanillaCommitted = vanillaCommitted;
                Resolution = resolution;
                PlayerControlled = playerControlled;
                CommittedUnitAlreadyEngaged = committedUnitAlreadyEngaged;
                OwnStrengthRatio = SanitizeRatio(ownStrengthRatio, 1f);
                LocalOdds = SanitizeRatio(localOdds, 1f);
            }

            public bool VanillaCommitted { get; }
            public CommandIntentResolution Resolution { get; }
            public bool PlayerControlled { get; }
            public bool CommittedUnitAlreadyEngaged { get; }
            public float OwnStrengthRatio { get; }
            public float LocalOdds { get; }
        }

        public readonly struct Decision
        {
            public Decision(Action action, string reason, DirectChildRole role)
            {
                Action = action;
                Reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
                Role = role;
            }

            public Action Action { get; }
            public string Reason { get; }
            public DirectChildRole Role { get; }
        }

        public static Decision Decide(Input input)
        {
            if (!input.VanillaCommitted)
                return Observe("no-vanilla-commit", DirectChildRole.Unknown);
            if (input.PlayerControlled)
                return Observe("player-controlled", DirectChildRole.Unknown);
            if (!input.Resolution.Found)
                return Observe(input.Resolution.Reason, DirectChildRole.Unknown);

            DirectChildRole role = input.Resolution.Intent.Role;
            if (input.CommittedUnitAlreadyEngaged)
                return Allow("already-committed-contact", role);

            switch (role)
            {
                case DirectChildRole.Reserve:
                    return new Decision(Action.Deny, "role-reserve-hold", role);
                case DirectChildRole.Main:
                    return input.OwnStrengthRatio < 0.75f
                        ? Allow("main-understrength-release", role)
                        : Allow("main-vanilla-release", role);
                case DirectChildRole.Fallback:
                    return input.LocalOdds < 0.85f
                        ? Allow("fallback-screen-retreat", role)
                        : Allow("fallback-vanilla-release", role);
                case DirectChildRole.SupportMain:
                case DirectChildRole.Fix:
                case DirectChildRole.Screen:
                case DirectChildRole.RefuseLeft:
                case DirectChildRole.RefuseRight:
                    return Allow("role-vanilla-release", role);
                case DirectChildRole.Unknown:
                default:
                    return Observe("unknown-role", role);
            }
        }

        public static bool PermitReserveListBias(CommandIntentResolution resolution)
        {
            if (!resolution.Found) return true;
            switch (resolution.Intent.Role)
            {
                case DirectChildRole.Reserve:
                    return false;
                default:
                    return true;
            }
        }

        private static Decision Observe(string reason, DirectChildRole role) =>
            new Decision(Action.Observe, reason, role);

        private static Decision Allow(string reason, DirectChildRole role) =>
            new Decision(Action.Allow, reason, role);

        private static float SanitizeRatio(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            if (value < 0f) return 0f;
            if (value > 10f) return 10f;
            return value;
        }
    }
}
```

- [ ] **Step 5: Run the harness and verify the new tests pass**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass. The total should be `689 PASS / 0 FAIL` if starting from the Slice 0 `682 PASS / 0 FAIL` baseline.

- [ ] **Step 6: Commit Task 1**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/TacticalReserveCommitGate.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "test(orchestrator): add reserve commit gate policy"
```

### Task 2: Add Default-Off Reserve Commit Config

**Files:**
- Modify: `src/WhiskeyRealism/Plugin.cs`

- [ ] **Step 1: Add the config field**

Near the other tactical orchestrator fields in `Plugin.cs`, add:

```csharp
internal ConfigEntry<bool> EnableTacticalOrchestratorReserveCommitGate;
```

- [ ] **Step 2: Bind the config default-off**

Near `EnableTacticalOrchestratorDirectChildGate`, add:

```csharp
EnableTacticalOrchestratorReserveCommitGate = Config.Bind(
    "Tactical Orchestrator",
    "Enable Tactical Orchestrator Reserve Commit Gate",
    false,
    "Default OFF. Slice 1: when true, AIBattle.CheckUseOfReserves consults " +
    "the command-node intent for the calling command group and rolls back new " +
    "vanilla reserve support paths when the group resolves to a Reserve role. " +
    "Allowed vanilla reserve movement remains eligible for the separate order-delay guard.");
```

- [ ] **Step 3: Build to verify config compiles**

Run:

```bash
./build.sh
```

Expected: build succeeds with `0 Error(s)`.

- [ ] **Step 4: Commit Task 2**

```bash
git add src/WhiskeyRealism/Plugin.cs
git commit -m "feat(orchestrator): add reserve commit gate flag"
```

### Task 3: Add `BattleReserveCommitGatePatch`

**Files:**
- Create: `src/WhiskeyRealism/Patches/BattleReserveCommitGatePatch.cs`

- [ ] **Step 1: Create the patch shell**

Create `src/WhiskeyRealism/Patches/BattleReserveCommitGatePatch.cs`:

```csharp
using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Tactical orchestrator Slice 1. Vanilla AIBattle.CheckUseOfReserves
    // directly issues RegimentSetPath for a reserve support unit at decompile
    // line 6170. This patch snapshots attached-unit path state before vanilla,
    // then rolls back only newly-added paths when the command-node role says the
    // calling command group is still a Reserve.
    [HarmonyPatch(typeof(AIBattle), "CheckUseOfReserves")]
    internal static class BattleReserveCommitGatePatch
    {
        internal sealed class ReserveCommitState
        {
            public UnitState[] Units = Array.Empty<UnitState>();
        }

        internal readonly struct UnitState
        {
            public UnitState(Regiment unit, int paths, int queueCount, bool doubleQuick, bool inEngagement)
            {
                Unit = unit;
                Paths = Math.Max(0, paths);
                QueueCount = Math.Max(0, queueCount);
                DoubleQuick = doubleQuick;
                InEngagement = inEngagement;
            }

            public Regiment Unit { get; }
            public int Paths { get; }
            public int QueueCount { get; }
            public bool DoubleQuick { get; }
            public bool InEngagement { get; }
        }

        [HarmonyPrefix]
        internal static void Prefix(Regiment aigroup, out ReserveCommitState __state)
        {
            __state = Enabled() ? Snapshot(aigroup) : null;
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.High)]
        internal static void Postfix(AIBattle __instance, Regiment aigroup, ReserveCommitState __state)
        {
            if (!Enabled()) return;

            try
            {
                if (__state == null || __state.Units == null || aigroup == null) return;

                var changed = FindChangedUnits(__state);
                if (changed.Length == 0)
                {
                    Log(aigroup, TacticalReserveCommitGate.Action.Observe, DirectChildRole.Unknown, "no-vanilla-commit", 0);
                    return;
                }

                CommandIntentResolution resolution = ResolveIntent(aigroup);
                var input = new TacticalReserveCommitGate.Input(
                    vanillaCommitted: true,
                    resolution: resolution,
                    playerControlled: HasPlayerOwnership(aigroup),
                    committedUnitAlreadyEngaged: AnyAlreadyEngaged(changed),
                    ownStrengthRatio: OwnStrengthRatio(aigroup),
                    localOdds: LocalOdds(aigroup));
                var decision = TacticalReserveCommitGate.Decide(input);

                if (decision.Action == TacticalReserveCommitGate.Action.Deny)
                {
                    RollBackChangedUnits(changed);
                }

                Log(aigroup, decision.Action, decision.Role, decision.Reason, changed.Length);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-reserve-commit-gate:failed",
                    "[TacticalReserveCommitGate] failed; vanilla reserve movement remains active: " + ex.Message);
            }
        }
    }
}
```

- [ ] **Step 2: Fill in runtime helpers**

In the same patch file, add these private helpers inside `BattleReserveCommitGatePatch`:

```csharp
private static bool Enabled()
{
    return Plugin.Instance != null
        && Plugin.Instance.Enabled != null
        && Plugin.Instance.Enabled.Value
        && Plugin.Instance.EnableTacticalBattleOrchestrator != null
        && Plugin.Instance.EnableTacticalBattleOrchestrator.Value
        && Plugin.Instance.EnableTacticalOrchestratorReserveCommitGate != null
        && Plugin.Instance.EnableTacticalOrchestratorReserveCommitGate.Value;
}

private static ReserveCommitState Snapshot(Regiment group)
{
    try
    {
        if (group == null || group.allattachedunits == null)
            return new ReserveCommitState();

        var units = new UnitState[group.allattachedunits.Length];
        for (int i = 0; i < group.allattachedunits.Length; i++)
        {
            Regiment unit = group.allattachedunits[i];
            units[i] = new UnitState(
                unit,
                SafePathCount(unit),
                SafeQueueCount(unit),
                SafeDoubleQuick(unit),
                SafeInEngagement(unit));
        }

        return new ReserveCommitState { Units = units };
    }
    catch
    {
        return new ReserveCommitState();
    }
}

private static UnitState[] FindChangedUnits(ReserveCommitState state)
{
    var changed = new System.Collections.Generic.List<UnitState>();
    for (int i = 0; i < state.Units.Length; i++)
    {
        UnitState before = state.Units[i];
        Regiment unit = before.Unit;
        if (unit == null) continue;

        int afterPaths = SafePathCount(unit);
        int afterQueue = SafeQueueCount(unit);
        if (afterPaths > before.Paths && afterQueue <= before.QueueCount)
            changed.Add(before);
    }

    return changed.ToArray();
}

private static void RollBackChangedUnits(UnitState[] changed)
{
    for (int i = 0; i < changed.Length; i++)
    {
        UnitState before = changed[i];
        Regiment unit = before.Unit;
        if (unit == null) continue;

        RemoveAddedPaths(unit, before.Paths, SafePathCount(unit));
        unit.doublequick = before.DoubleQuick;
    }
}

private static CommandIntentResolution ResolveIntent(Regiment group)
{
    try
    {
        int alliance = group != null ? group.alliance : -1;
        var side = TacticalBattleCoordinator.GetSideOrchestrator(alliance);
        if (side == null || side.Army == null)
            return new CommandIntentResolution(false, default, "no-side-orchestrator");

        return side.Army.ResolveCommandIntentForGroup(group.GetInstanceID());
    }
    catch (Exception ex)
    {
        return new CommandIntentResolution(false, default, "resolve-error:" + ex.GetType().Name);
    }
}

private static bool HasPlayerOwnership(Regiment group)
{
    if (group == null) return true;
    if (group.dlcw_isundercommander) return true;
    if (group.allattachedunits == null) return false;

    for (int i = 0; i < group.allattachedunits.Length; i++)
    {
        Regiment unit = group.allattachedunits[i];
        if (unit != null && unit.dlcw_isundercommander) return true;
    }

    return false;
}

private static bool AnyAlreadyEngaged(UnitState[] changed)
{
    for (int i = 0; i < changed.Length; i++)
    {
        Regiment unit = changed[i].Unit;
        if (unit != null && (changed[i].InEngagement || SafeInEngagement(unit)))
            return true;
    }

    return false;
}

private static float OwnStrengthRatio(Regiment group)
{
    if (group == null) return 1f;
    float active = Sanitize(group.groupstrengthactive);
    float total = Math.Max(1f, Sanitize(group.groupstrength));
    return active / total;
}

private static float LocalOdds(Regiment group)
{
    if (group == null) return 1f;
    float own = Math.Max(Sanitize(group.groupowninrange), Sanitize(group.groupstrengthactive));
    float enemy = 0f;
    try
    {
        if (group.unitrange != null)
            enemy = Math.Max(0f, 0f - group.unitrange.enemytotalstrength);
    }
    catch { }

    return enemy <= 0f ? 1f : own / Math.Max(1f, enemy);
}

private static void RemoveAddedPaths(Regiment unit, int before, int after)
{
    int safeBefore = Math.Max(0, before);
    int safeAfter = Math.Max(safeBefore, after);
    if (unit.regimentpath != null)
    {
        int max = Math.Min(safeAfter, unit.regimentpath.Length);
        for (int i = safeBefore; i < max; i++)
            unit.regimentpath[i] = new NavMeshPath();
    }

    if (unit.pathstatus != null)
    {
        int max = Math.Min(safeAfter, unit.pathstatus.Length);
        for (int i = safeBefore; i < max; i++)
            unit.pathstatus[i] = 0;
    }

    unit.regimentpaths = safeBefore;
}

private static int SafePathCount(Regiment unit)
{
    try { return unit != null ? Math.Max(0, unit.regimentpaths) : 0; }
    catch { return 0; }
}

private static int SafeQueueCount(Regiment unit)
{
    try { return unit != null && unit.orderqueue != null ? unit.orderqueue.Count : 0; }
    catch { return 0; }
}

private static bool SafeDoubleQuick(Regiment unit)
{
    try { return unit != null && unit.doublequick; }
    catch { return false; }
}

private static bool SafeInEngagement(Regiment unit)
{
    try { return unit != null && unit.inengagement; }
    catch { return false; }
}

private static float Sanitize(float value)
{
    if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
    return Math.Max(0f, value);
}

private static void Log(
    Regiment group,
    TacticalReserveCommitGate.Action action,
    DirectChildRole role,
    string reason,
    int changedUnits)
{
    try
    {
        string groupName = TacticalCurrentOrderSignature.Safe(group != null ? group.name : "-");
        string key = "tactical-reserve-commit-gate:"
            + (group != null ? group.GetInstanceID().ToString() : "null")
            + ":" + action + ":" + role + ":" + reason;
        OnceLog.Info(
            key,
            "[TacticalReserveCommitGate] group=" + groupName
            + " action=" + action
            + " role=" + role
            + " reason=" + reason
            + " changedUnits=" + changedUnits);
    }
    catch { }
}
```

- [ ] **Step 3: Build to catch missing usings or accessibility errors**

Run:

```bash
./build.sh
```

Expected: build succeeds with `0 Error(s)`.

- [ ] **Step 4: Commit Task 3**

```bash
git add src/WhiskeyRealism/Patches/BattleReserveCommitGatePatch.cs
git commit -m "feat(orchestrator): gate reserve commitments by command role"
```

### Task 4: Make #57 Reserve-List Bias Command-Aware

**Files:**
- Modify: `src/WhiskeyRealism/Patches/BattleReserveDoctrinePatch.cs`

- [ ] **Step 1: Add orchestrator using**

At the top of `BattleReserveDoctrinePatch.cs`, add:

```csharp
using WhiskeyRealism.Tactical.Orchestrator;
```

- [ ] **Step 2: Change strongest-reserve ranking to skip `Reserve` role candidates**

Replace this line inside `StrongestValidReserveIndex`:

```csharp
if (!ValidReserveForRanking(group)) continue;
```

with:

```csharp
if (!ValidReserveForRanking(group)) continue;
if (!TacticalReserveCommitGate.PermitReserveListBias(ResolveCommandIntent(group))) continue;
```

- [ ] **Step 3: Add the resolver helper**

Add this helper near the other private helpers in `BattleReserveDoctrinePatch.cs`:

```csharp
private static CommandIntentResolution ResolveCommandIntent(Regiment group)
{
    try
    {
        if (group == null)
            return new CommandIntentResolution(false, default, "null-group");

        var side = TacticalBattleCoordinator.GetSideOrchestrator(group.alliance);
        if (side == null || side.Army == null)
            return new CommandIntentResolution(false, default, "no-side-orchestrator");

        return side.Army.ResolveCommandIntentForGroup(group.GetInstanceID());
    }
    catch (Exception ex)
    {
        return new CommandIntentResolution(false, default, "resolve-error:" + ex.GetType().Name);
    }
}
```

- [ ] **Step 4: Build and run the harness**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected: harness passes; build succeeds with `0 Error(s)`.

- [ ] **Step 5: Commit Task 4**

```bash
git add src/WhiskeyRealism/Patches/BattleReserveDoctrinePatch.cs
git commit -m "feat(orchestrator): respect command role in reserve list bias"
```

### Task 5: Document Patch Catalog and Handoff

**Files:**
- Modify: `docs/patch-catalog.md`
- Modify: `docs/handoff.md`
- Modify: `MEMORY.md`

- [ ] **Step 1: Add the new patch catalog row**

In `docs/patch-catalog.md`, add next ordinal `#59`:

```markdown
| 59 | `BattleReserveCommitGatePatch` | Prefix/Postfix | `Patches/BattleReserveCommitGatePatch.cs` | private `AIBattle.CheckUseOfReserves` (6062), direct reserve move at 6170 | Tactical orchestrator Slice 1 default-off reserve commitment gate under `Enable Tactical Orchestrator Reserve Commit Gate`. Snapshots attached-unit path state before vanilla reserve-use logic, resolves the calling command group's command-node intent after vanilla, and rolls back newly-created direct reserve support paths when the command role is `Reserve`. Allows vanilla movement for Main/Fallback/other non-reserve roles; #56 may still convert allowed direct paths into delayed orders under its own flag. No player-side writes. Build/deploy/smoke status belongs in `docs/handoff.md`. |
```

- [ ] **Step 2: Update handoff before smoke**

In `docs/handoff.md`, add a "What just shipped" paragraph after Slice 0:

```markdown
> **Tactical orchestrator Slice 1 — reserve commitment gate implemented locally (2026-05-10):** Adds #59 `BattleReserveCommitGatePatch` behind default-off `Enable Tactical Orchestrator Reserve Commit Gate`. The patch snapshots `CheckUseOfReserves` attached-unit paths, resolves command-node intent for the calling group, and rolls back newly-created vanilla reserve support paths only when the resolved command role is `Reserve`; allowed vanilla movement remains eligible for #56 order-delay conversion. #57 reserve-list bias now skips candidates whose command role resolves to `Reserve`. Harness/build status: update after verification. Runtime smoke is pending until the DLL is deployed and focused gate-OFF/gate-ON battle logs are captured.
```

- [ ] **Step 3: Update `MEMORY.md` current priorities**

Replace "starting with reserves" with "Slice 1 reserve commitment gate implemented locally; smoke pending" after implementation, and keep the remaining order as stance / charge / fallback / artillery.

- [ ] **Step 4: Commit Task 5**

```bash
git add docs/patch-catalog.md docs/handoff.md MEMORY.md
git commit -m "docs(orchestrator): describe slice 1 reserve gate"
```

### Task 6: Verification, Deploy, and Focused Smoke

**Files:**
- No code changes expected unless verification fails.

- [ ] **Step 1: Run full console harness**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass. Expected count starts from `689 PASS / 0 FAIL` after Task 1.

- [ ] **Step 2: Run build**

Run:

```bash
./build.sh
```

Expected: build succeeds with `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 3: Deploy DLL**

Run:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
```

Expected: command succeeds. If it fails with `Invalid argument`, close Grand Tactician and rerun.

- [ ] **Step 4: Verify deployed hash**

Run:

```bash
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: same size and same SHA-256 for both files.

- [ ] **Step 5: Gate-OFF smoke**

Config:

```ini
Enable Tactical Orchestrator Reserve Commit Gate = false
```

Start a battle and scan:

```bash
rg -n "TacticalReserveCommitGate|TacticalReserveOrderDelayGuard|TacticalCommandTree|Exception|ERROR|missing-anchor|failed" "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Expected:
- `[TacticalCommandTree]` still appears for both AI sides.
- No `[TacticalReserveCommitGate] action=deny`.
- No repeated exceptions, `ERROR`, `missing-anchor`, or `failed` rows from the new patch.

- [ ] **Step 6: Gate-ON focused smoke**

Config:

```ini
Enable Tactical Orchestrator Reserve Commit Gate = true
```

Start or reload a battle with active reserves and scan:

```bash
rg -n "TacticalReserveCommitGate|TacticalReserveOrderDelayGuard|TacticalReserveMutation|TacticalCommandTree|Exception|ERROR|missing-anchor|failed" "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Expected:
- `[TacticalReserveCommitGate]` rows are bounded by OnceLog signature.
- `action=deny role=Reserve reason=role-reserve-hold` appears only when vanilla actually added a reserve support path for a Reserve-role command group.
- Allowed rows for non-reserve roles do not block #56; if `Enable Tactical Reserve Order Delay Guard = true`, allowed reserve movement may still produce `[TacticalReserveOrderDelayGuard] converted direct reserve path to delayed order`.
- No player-side reserve movement writes.
- No repeated exceptions, `ERROR`, `missing-anchor`, or `failed` rows from #59, #56, #57, or the command tree.

- [ ] **Step 7: Update smoke docs with final hash**

After smoke, update:
- `docs/handoff.md` with test count, build result, deployed DLL SHA-256, and gate-OFF/gate-ON smoke results.
- `docs/patch-catalog.md` #59 row with the verified DLL hash and smoke result.
- `MEMORY.md` current checkpoint with Slice 1 status.

- [ ] **Step 8: Commit verification closeout**

```bash
git add docs/handoff.md docs/patch-catalog.md MEMORY.md
git commit -m "docs(orchestrator): record slice 1 smoke"
```

## Rollback and Defer Boundaries

- If `CheckUseOfReserves` signature or `aigroup.allattachedunits` assumptions fail, leave the new config default-off and record the missing anchor in `docs/handoff.md`; do not attempt a transpiler.
- If #59 and #56 interact badly, keep #59's Postfix at `Priority.High` so deny happens before #56's delayed-order conversion. If that still fails in runtime, disable #59 and keep #56 as the safer existing bug guard.
- If gate-ON smoke never naturally triggers reserve movement, accept only gate engagement telemetry as partial proof and mark deny-path runtime smoke pending; do not fabricate reserve conditions through saved-game mutation.
- If player-subordinate movement appears in any #59 write path, revert the patch or hard-disable the config before continuing to Slice 2.

## Plan Self-Review

- Spec coverage: covers Slice 1 reserve commitment, both vanilla anchors (`CheckUseOfReserves`, `AssignReserves`), default-off config, command-node resolver fallback, gate-OFF and gate-ON smoke, no player-side writes, and no hard-coded echelon class tower.
- Red-flag scan: the plan has no deferred-work markers or vague implementation steps.
- Type consistency: all new names use `TacticalReserveCommitGate`, `BattleReserveCommitGatePatch`, `EnableTacticalOrchestratorReserveCommitGate`, and existing `CommandIntentResolution` / `DirectChildRole` contracts from Slice 0.
