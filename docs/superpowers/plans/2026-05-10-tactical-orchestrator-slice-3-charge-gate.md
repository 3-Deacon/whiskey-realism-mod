# Tactical Orchestrator Slice 3 Charge Gate Implementation Plan

Status: implemented and merged to `main`. Slice 3 was hash-deployed in DLL `b00e03bd7e635e981380459e09a0d52a19d635c22c49bd340b403dacfbdf4cf8` (841216 bytes; 717 PASS), now superseded by the current operations-ledger `main` DLL `9e76ce41c4a85cb25fd3ca00536a782eeb49d4922459de3579c25ab31fcb62b8` (888320 bytes; 760 PASS). Focused gate-OFF/gate-ON in-game smoke is still pending. Living status now lives in [`docs/tactical-orchestrator.md`](../../tactical-orchestrator.md).

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make vanilla charge initiation respect the current command-node role so Main-role commands can charge under favorable conditions while Fix, Reserve, Fallback, Refuse, and Screen roles preserve cohesion.

**Architecture:** Extend the existing #41 `BattleChargeGatePatch` owner for `AIBattle.MicroAICheckForCharges` instead of adding a second Harmony patch. Add a pure `TacticalOrchestratorChargeGate` decision helper, add a default-off orchestrator charge flag, then thread the helper into #41 after the existing W&L ownership gate and before `SetMovementMode(3)`. The gate reads the latest command-node intent through `TacticalBattleCoordinator.GetSideOrchestrator(...).Army.ResolveCommandIntentForGroup(...)`, fails open when the command tree is unavailable, and never writes orchestrator state from the patch.

**Tech Stack:** C# netstandard2.1, BepInEx 5.4.x, HarmonyX Prefix replacement, Grand Tactician `AIBattle.MicroAICheckForCharges` / `Regiment.SetMovementMode`, existing Whiskey tactical orchestrator command-node state, console harness tests in `tests/WhiskeyRealism.Tests`.

---

## Preconditions

- Execute implementation from an isolated worktree created with `superpowers:using-git-worktrees`.
- If the implementation worktree does not inherit the ignored `refs` symlink, run `ln -s ../../refs refs` before building.
- Read these nested instructions before code edits:
  - `src/WhiskeyRealism/Patches/AGENTS.md`
  - `src/WhiskeyRealism/Tactical/AGENTS.md`
  - `tests/WhiskeyRealism.Tests/AGENTS.md`
- Keep the active slice spec open: `docs/superpowers/specs/2026-05-09-tactical-orchestrator-remaining-patches-design.md`.
- This plan intentionally skips Slice 2 stance implementation. Slice 3 can be planned and implemented independently because #41 already owns the charge initiation surface and Slice 0 command-node state is merged.

## Vanilla Anchors Confirmed For This Plan

- `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:4905-4927` — `AIBattle.MicroAICheckForCharges(Regiment aigroup, int restrictunittypes = 13)` loops `aigroup.allattachedunits`.
- `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:4917-4920` — charge initiation branch requires `aigroup.ai_stance == 4`, unit not routed/marked, movement mode not already fallback/retreat/charge, unit type allowed by `restrictunittypes`, feud gate allowed, and cooldown check passes; then calls `SetMovementMode(3)` and updates `aigroup.lastfeudactiontime`.
- `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:4922-4926` — charge cancellation branch clears `movementmode == 3` when group stance is no longer charge; Slice 3 must preserve this branch.
- `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:5101` — `AIBattle.PerformAIActionDLCWL(...)` exists but is not called by `MicroAICheckForCharges`; existing #41 mirrors vanilla to provide W&L player-subordinate protection.
- `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:122229-122230` — vanilla unit charge score weighting uses strength, experience, morale, fatigue, and `GamePrefs.weightingfactorsformicroaicharge`.
- `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:122257` — vanilla regiment charge scoring checks `GamePrefs.maxchargeradius`.
- `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:49128`, `:51284`, `:51738`, `:52480` — vanilla charge knobs include `maxenemymoraleforcavalrychargenonarty`, `timetorenewaichargecheck`, `maxchargeradius`, and `weightingfactorsformicroaicharge`.

## Current Whiskey Anchors

- Existing patch owner: `src/WhiskeyRealism/Patches/BattleChargeGatePatch.cs`
  - Prefix replacement for `AIBattle.MicroAICheckForCharges`.
  - Preserves vanilla cancellation branch.
  - Existing gates: `Enable W&L Tactical Charge Guard` and `Enable Tactical Charge Denial`.
  - Existing bounded logs: `[TacticalChargeGuard]` and `[TacticalChargeDeny]`.
- Existing pure charge scorer: `src/WhiskeyRealism/Tactical/TacticalChargeViability.cs`
  - Useful for future target-specific scoring.
  - Not sufficient by itself for Slice 3 because `MicroAICheckForCharges` does not provide a target to the charge-initiation branch.
- Command-node resolver: `src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs`
  - `ResolveCommandIntentForGroup(int regimentInstanceId)` returns `CommandIntentResolution`.
- Runtime side lookup: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinator.cs`
  - `GetSideOrchestrator(int allianceId)` returns the active per-side orchestrator or `null`.

## Non-Goals

- Do not create a second patch on `MicroAICheckForCharges`; #41 remains the owner.
- Do not block vanilla charge cancellation.
- Do not use `TacticalChargeViability` as a target scorer until the patch has target evidence. The charge-initiation branch only toggles movement mode.
- Do not issue movement orders, set waypoints, assign charge targets, or modify `ai_stance`.
- Do not mutate command-tree, ArmyOrchestrator, or tactical ledger state from the patch.
- Do not retask player-side or W&L player-subordinate units.
- Do not make the orchestrator charge gate default-on before focused runtime smoke.
- Do not implement the Screen chase-routed exception in the first behavior pass unless exact target-routed evidence is available at the #41 call site. Without target evidence, Screen denies with `screen-no-routed-target`.

## File Map

- Create `src/WhiskeyRealism/Tactical/Orchestrator/TacticalOrchestratorChargeGate.cs`
  - Pure role-based decision helper for charge initiation.
- Modify `src/WhiskeyRealism/Plugin.cs`
  - Add `EnableTacticalOrchestratorChargeGate` config entry under `Tactical Orchestrator`.
- Modify `src/WhiskeyRealism/Patches/BattleChargeGatePatch.cs`
  - Add orchestrator gate into the existing charge-initiation branch after W&L guard and before B6c/local-reaction denial and `SetMovementMode(3)`.
  - Keep cancellation behavior identical to current #41.
- Modify `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
  - Add explicit compile entry for `TacticalOrchestratorChargeGate.cs`.
- Modify `tests/WhiskeyRealism.Tests/Program.cs`
  - Add pure harness tests for every role branch.
- Modify after implementation:
  - `docs/patch-catalog.md`
  - `docs/handoff.md`
  - `MEMORY.md`

## Task 1: Add Pure Orchestrator Charge Gate

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalOrchestratorChargeGate.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add test registrations**

In `tests/WhiskeyRealism.Tests/Program.cs`, add these entries near the other tactical orchestrator tests:

```csharp
("tactical orchestrator charge gate observes when vanilla would not charge", TacticalOrchestratorChargeGateObservesWhenNoVanillaCharge),
("tactical orchestrator charge gate preserves vanilla cancellation", TacticalOrchestratorChargeGatePreservesCancellation),
("tactical orchestrator charge gate fails open without command intent", TacticalOrchestratorChargeGateFailsOpenWithoutIntent),
("tactical orchestrator charge gate observes player controlled group", TacticalOrchestratorChargeGateObservesPlayerControlled),
("tactical orchestrator charge gate allows main with favorable odds", TacticalOrchestratorChargeGateAllowsMainFavorableOdds),
("tactical orchestrator charge gate denies main with poor odds", TacticalOrchestratorChargeGateDeniesMainPoorOdds),
("tactical orchestrator charge gate allows support main with support evidence", TacticalOrchestratorChargeGateAllowsSupportMainWithEvidence),
("tactical orchestrator charge gate denies support main without support evidence", TacticalOrchestratorChargeGateDeniesSupportMainWithoutEvidence),
("tactical orchestrator charge gate denies fix reserve fallback refuse and screen", TacticalOrchestratorChargeGateDeniesHoldRoles),
```

- [ ] **Step 2: Add failing tests**

Add these helpers and tests near the existing command-node / reserve-gate tests:

```csharp
private static TacticalOrchestratorChargeGate.Input ChargeGateInput(
    bool vanillaWouldCharge = true,
    bool chargeCancellation = false,
    bool resolved = true,
    DirectChildRole role = DirectChildRole.Main,
    bool playerControlled = false,
    float localOdds = 1.25f,
    bool mainEffortSupportAvailable = false,
    bool screenRoutedTargetVisible = false)
{
    return new TacticalOrchestratorChargeGate.Input(
        vanillaWouldCharge,
        chargeCancellation,
        new CommandIntentResolution(
            resolved,
            new CommandNodeIntent(
                "node-200",
                "node-200",
                role,
                DirectChildAxis.SectorAxis,
                primarySector: 2,
                supportPriority: 70,
                aggressionBias01: 0.6f,
                depth: 1),
            resolved ? "exact-command-node" : "command-node-not-found"),
        playerControlled,
        localOdds,
        mainEffortSupportAvailable,
        screenRoutedTargetVisible);
}

private static void TacticalOrchestratorChargeGateObservesWhenNoVanillaCharge()
{
    var d = TacticalOrchestratorChargeGate.Decide(ChargeGateInput(vanillaWouldCharge: false));
    AssertEqual(TacticalOrchestratorChargeGate.Action.Observe, d.Action, "action");
    AssertEqual("no-vanilla-charge", d.Reason, "reason");
}

private static void TacticalOrchestratorChargeGatePreservesCancellation()
{
    var d = TacticalOrchestratorChargeGate.Decide(ChargeGateInput(chargeCancellation: true, role: DirectChildRole.Reserve));
    AssertEqual(TacticalOrchestratorChargeGate.Action.Allow, d.Action, "action");
    AssertEqual("charge-cancellation", d.Reason, "reason");
}

private static void TacticalOrchestratorChargeGateFailsOpenWithoutIntent()
{
    var d = TacticalOrchestratorChargeGate.Decide(ChargeGateInput(resolved: false, role: DirectChildRole.Reserve));
    AssertEqual(TacticalOrchestratorChargeGate.Action.Allow, d.Action, "action");
    AssertEqual("no-command-intent", d.Reason, "reason");
}

private static void TacticalOrchestratorChargeGateObservesPlayerControlled()
{
    var d = TacticalOrchestratorChargeGate.Decide(ChargeGateInput(playerControlled: true, role: DirectChildRole.Reserve));
    AssertEqual(TacticalOrchestratorChargeGate.Action.Observe, d.Action, "action");
    AssertEqual("player-controlled", d.Reason, "reason");
}

private static void TacticalOrchestratorChargeGateAllowsMainFavorableOdds()
{
    var d = TacticalOrchestratorChargeGate.Decide(ChargeGateInput(role: DirectChildRole.Main, localOdds: 1.20f));
    AssertEqual(TacticalOrchestratorChargeGate.Action.Allow, d.Action, "action");
    AssertEqual("main-favorable-odds", d.Reason, "reason");
}

private static void TacticalOrchestratorChargeGateDeniesMainPoorOdds()
{
    var d = TacticalOrchestratorChargeGate.Decide(ChargeGateInput(role: DirectChildRole.Main, localOdds: 0.95f));
    AssertEqual(TacticalOrchestratorChargeGate.Action.Deny, d.Action, "action");
    AssertEqual("main-unfavorable-odds", d.Reason, "reason");
}

private static void TacticalOrchestratorChargeGateAllowsSupportMainWithEvidence()
{
    var d = TacticalOrchestratorChargeGate.Decide(ChargeGateInput(
        role: DirectChildRole.SupportMain,
        localOdds: 1.05f,
        mainEffortSupportAvailable: true));
    AssertEqual(TacticalOrchestratorChargeGate.Action.Allow, d.Action, "action");
    AssertEqual("support-main-charge-support", d.Reason, "reason");
}

private static void TacticalOrchestratorChargeGateDeniesSupportMainWithoutEvidence()
{
    var d = TacticalOrchestratorChargeGate.Decide(ChargeGateInput(role: DirectChildRole.SupportMain, mainEffortSupportAvailable: false));
    AssertEqual(TacticalOrchestratorChargeGate.Action.Deny, d.Action, "action");
    AssertEqual("support-main-no-main-charge", d.Reason, "reason");
}

private static void TacticalOrchestratorChargeGateDeniesHoldRoles()
{
    AssertEqual(TacticalOrchestratorChargeGate.Action.Deny,
        TacticalOrchestratorChargeGate.Decide(ChargeGateInput(role: DirectChildRole.Fix)).Action,
        "fix denies");
    AssertEqual(TacticalOrchestratorChargeGate.Action.Deny,
        TacticalOrchestratorChargeGate.Decide(ChargeGateInput(role: DirectChildRole.Reserve)).Action,
        "reserve denies");
    AssertEqual(TacticalOrchestratorChargeGate.Action.Deny,
        TacticalOrchestratorChargeGate.Decide(ChargeGateInput(role: DirectChildRole.Fallback)).Action,
        "fallback denies");
    AssertEqual(TacticalOrchestratorChargeGate.Action.Deny,
        TacticalOrchestratorChargeGate.Decide(ChargeGateInput(role: DirectChildRole.RefuseLeft)).Action,
        "refuse-left denies");
    AssertEqual(TacticalOrchestratorChargeGate.Action.Deny,
        TacticalOrchestratorChargeGate.Decide(ChargeGateInput(role: DirectChildRole.RefuseRight)).Action,
        "refuse-right denies");

    var screen = TacticalOrchestratorChargeGate.Decide(ChargeGateInput(role: DirectChildRole.Screen));
    AssertEqual(TacticalOrchestratorChargeGate.Action.Deny, screen.Action, "screen denies without routed target");
    AssertEqual("screen-no-routed-target", screen.Reason, "screen reason");

    var screenRouted = TacticalOrchestratorChargeGate.Decide(ChargeGateInput(
        role: DirectChildRole.Screen,
        screenRoutedTargetVisible: true));
    AssertEqual(TacticalOrchestratorChargeGate.Action.Allow, screenRouted.Action, "screen routed target allows");
    AssertEqual("screen-chase-routed-target", screenRouted.Reason, "screen routed reason");
}
```

- [ ] **Step 3: Register the planned source file in the test project**

In `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, add this line next to the other `Tactical/Orchestrator` compile entries:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\TacticalOrchestratorChargeGate.cs" Link="Orchestrator\TacticalOrchestratorChargeGate.cs" />
```

- [ ] **Step 4: Run the harness and verify the red state**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile failure naming `TacticalOrchestratorChargeGate` as missing.

- [ ] **Step 5: Create the pure helper**

Create `src/WhiskeyRealism/Tactical/Orchestrator/TacticalOrchestratorChargeGate.cs`:

```csharp
using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    internal static class TacticalOrchestratorChargeGate
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
                bool vanillaWouldCharge,
                bool chargeCancellation,
                CommandIntentResolution resolution,
                bool playerControlled,
                float localOdds,
                bool mainEffortSupportAvailable,
                bool screenRoutedTargetVisible)
            {
                VanillaWouldCharge = vanillaWouldCharge;
                ChargeCancellation = chargeCancellation;
                Resolution = resolution;
                PlayerControlled = playerControlled;
                LocalOdds = SanitizeOdds(localOdds);
                MainEffortSupportAvailable = mainEffortSupportAvailable;
                ScreenRoutedTargetVisible = screenRoutedTargetVisible;
            }

            public bool VanillaWouldCharge { get; }
            public bool ChargeCancellation { get; }
            public CommandIntentResolution Resolution { get; }
            public bool PlayerControlled { get; }
            public float LocalOdds { get; }
            public bool MainEffortSupportAvailable { get; }
            public bool ScreenRoutedTargetVisible { get; }

            private static float SanitizeOdds(float value)
            {
                if (float.IsNaN(value) || float.IsInfinity(value)) return 1f;
                return Math.Max(0f, value);
            }
        }

        public readonly struct Decision
        {
            public Decision(Action action, DirectChildRole role, string reason)
            {
                Action = action;
                Role = role;
                Reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
            }

            public Action Action { get; }
            public DirectChildRole Role { get; }
            public string Reason { get; }
            public bool AllowsCharge => Action != TacticalOrchestratorChargeGate.Action.Deny;
        }

        public static Decision Decide(Input input)
        {
            if (input.ChargeCancellation)
                return new Decision(Action.Allow, DirectChildRole.Unknown, "charge-cancellation");

            if (!input.VanillaWouldCharge)
                return new Decision(Action.Observe, DirectChildRole.Unknown, "no-vanilla-charge");

            if (input.PlayerControlled)
                return new Decision(Action.Observe, DirectChildRole.Unknown, "player-controlled");

            if (!input.Resolution.Found)
                return new Decision(Action.Allow, DirectChildRole.Unknown, "no-command-intent");

            DirectChildRole role = input.Resolution.Intent.Role;
            switch (role)
            {
                case DirectChildRole.Main:
                    return input.LocalOdds >= 1.10f
                        ? new Decision(Action.Allow, role, "main-favorable-odds")
                        : new Decision(Action.Deny, role, "main-unfavorable-odds");

                case DirectChildRole.SupportMain:
                    return input.MainEffortSupportAvailable
                        ? new Decision(Action.Allow, role, "support-main-charge-support")
                        : new Decision(Action.Deny, role, "support-main-no-main-charge");

                case DirectChildRole.Screen:
                    return input.ScreenRoutedTargetVisible
                        ? new Decision(Action.Allow, role, "screen-chase-routed-target")
                        : new Decision(Action.Deny, role, "screen-no-routed-target");

                case DirectChildRole.Fix:
                    return new Decision(Action.Deny, role, "role-fix-hold");
                case DirectChildRole.Reserve:
                    return new Decision(Action.Deny, role, "role-reserve-hold");
                case DirectChildRole.Fallback:
                    return new Decision(Action.Deny, role, "role-fallback-no-charge");
                case DirectChildRole.RefuseLeft:
                    return new Decision(Action.Deny, role, "role-refuse-left-no-charge");
                case DirectChildRole.RefuseRight:
                    return new Decision(Action.Deny, role, "role-refuse-right-no-charge");
                case DirectChildRole.Unknown:
                default:
                    return new Decision(Action.Allow, role, "unknown-role");
            }
        }
    }
}
```

- [ ] **Step 6: Run the harness and verify green**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass; new tests print `PASS tactical orchestrator charge gate ...`.

- [ ] **Step 7: Commit Task 1**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/TacticalOrchestratorChargeGate.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "test(orchestrator): add charge gate policy"
```

## Task 2: Add Default-Off Orchestrator Charge Config

**Files:**
- Modify: `src/WhiskeyRealism/Plugin.cs`

- [ ] **Step 1: Add the config field**

Near the other tactical-orchestrator config fields in `Plugin.cs`, add:

```csharp
internal ConfigEntry<bool> EnableTacticalOrchestratorChargeGate;
```

- [ ] **Step 2: Bind the default-off config value**

Near `EnableTacticalOrchestratorReserveCommitGate`, add:

```csharp
EnableTacticalOrchestratorChargeGate = Config.Bind(
    "Tactical Orchestrator",
    "Enable Tactical Orchestrator Charge Gate",
    false,
    "Default OFF. Slice 3: when true, AIBattle.MicroAICheckForCharges consults " +
    "the command-node intent for the calling command group before allowing vanilla " +
    "SetMovementMode(3) charge initiation. Main charges require favorable local odds; " +
    "SupportMain requires main-effort support evidence; Fix/Reserve/Fallback/Refuse/Screen " +
    "roles deny charge initiation unless the Screen routed-target exception is proven.");
```

- [ ] **Step 3: Build to verify the config field compiles**

Run:

```bash
./build.sh
```

Expected: build succeeds with `0 Error(s)`.

- [ ] **Step 4: Commit Task 2**

```bash
git add src/WhiskeyRealism/Plugin.cs
git commit -m "feat(orchestrator): add charge gate flag"
```

## Task 3: Wire #41 To Command-Node Intent

**Files:**
- Modify: `src/WhiskeyRealism/Patches/BattleChargeGatePatch.cs`

- [ ] **Step 1: Add orchestrator namespace**

At the top of `BattleChargeGatePatch.cs`, add:

```csharp
using WhiskeyRealism.Tactical.Orchestrator;
```

- [ ] **Step 2: Extend the patch enable check**

Replace `Enabled()` with:

```csharp
private static bool Enabled()
{
    return Plugin.Instance != null &&
        Plugin.Instance.Enabled.Value &&
        (Plugin.Instance.EnableWlTacticalChargeGuard.Value ||
            Plugin.Instance.EnableTacticalChargeDenial.Value ||
            Plugin.Instance.EnableTacticalOrchestratorChargeGate.Value);
}
```

- [ ] **Step 3: Insert the orchestrator gate in the charge-initiation branch**

Inside the existing `if (decision.Allow)` block, immediately after:

```csharp
aigroup.lastfeudactiontime = CurrentBattleHour(bunits);
```

insert:

```csharp
TacticalOrchestratorChargeGate.Decision orchestratorDecision =
    DecideOrchestratorCharge(unit, aigroup);
if (orchestratorDecision.Action == TacticalOrchestratorChargeGate.Action.Deny)
{
    LogDeniedOrchestrator(unit, aigroup, orchestratorDecision);
    continue;
}
```

The resulting branch must keep this order:

```csharp
if (decision.Allow)
{
    tookOwnership = true;
    aigroup.lastfeudactiontime = CurrentBattleHour(bunits);

    TacticalOrchestratorChargeGate.Decision orchestratorDecision =
        DecideOrchestratorCharge(unit, aigroup);
    if (orchestratorDecision.Action == TacticalOrchestratorChargeGate.Action.Deny)
    {
        LogDeniedOrchestrator(unit, aigroup, orchestratorDecision);
        continue;
    }

    if (TryB6cDeny(unit, aigroup)) continue;

    unit.SetMovementMode(3);
}
```

This preserves the existing W&L safety gate as the first behavior gate and preserves B6c defense-in-depth after the orchestrator gate.

- [ ] **Step 4: Add the orchestrator decision helper**

Add these methods below `TryB6cDeny(...)`:

```csharp
private static TacticalOrchestratorChargeGate.Decision DecideOrchestratorCharge(Regiment unit, Regiment group)
{
    if (Plugin.Instance == null || !Plugin.Instance.EnableTacticalOrchestratorChargeGate.Value)
    {
        return new TacticalOrchestratorChargeGate.Decision(
            TacticalOrchestratorChargeGate.Action.Allow,
            DirectChildRole.Unknown,
            "orchestrator-charge-gate-disabled");
    }

    CommandIntentResolution resolution = ResolveIntent(group);
    bool playerControlled = HasPlayerOwnership(group, unit);
    float localOdds = LocalOdds(group);
    bool mainEffortSupportAvailable = MainEffortSupportAvailable(group, resolution);
    bool screenRoutedTargetVisible = ScreenRoutedTargetVisible(unit);

    return TacticalOrchestratorChargeGate.Decide(
        new TacticalOrchestratorChargeGate.Input(
            vanillaWouldCharge: true,
            chargeCancellation: false,
            resolution: resolution,
            playerControlled: playerControlled,
            localOdds: localOdds,
            mainEffortSupportAvailable: mainEffortSupportAvailable,
            screenRoutedTargetVisible: screenRoutedTargetVisible));
}

private static CommandIntentResolution ResolveIntent(Regiment group)
{
    try
    {
        if (group == null)
            return new CommandIntentResolution(false, default, "no-group");

        TacticalBattleOrchestrator side = TacticalBattleCoordinator.GetSideOrchestrator(group.alliance);
        if (side == null || side.Army == null)
            return new CommandIntentResolution(false, default, "no-side-orchestrator");

        return side.Army.ResolveCommandIntentForGroup(group.GetInstanceID());
    }
    catch (Exception ex)
    {
        return new CommandIntentResolution(false, default, "resolve-error:" + ex.GetType().Name);
    }
}

private static bool HasPlayerOwnership(Regiment group, Regiment unit)
{
    try
    {
        if (group == null) return true;
        if (!SafeAiVsAi() && group.alliance == SafePlayerAlliance()) return true;
        if (group.dlcw_isundercommander) return true;
        if (unit != null && unit.dlcw_isundercommander) return true;
        if (group.allattachedunits == null) return false;

        for (int i = 0; i < group.allattachedunits.Length; i++)
        {
            Regiment attached = group.allattachedunits[i];
            if (attached != null && attached.dlcw_isundercommander) return true;
        }

        return false;
    }
    catch
    {
        return true;
    }
}

private static float LocalOdds(Regiment group)
{
    try
    {
        if (group == null) return 1f;
        float own = Math.Max(Sanitize(group.groupowninrange), Sanitize(group.groupstrengthaigroup));
        float enemy = Math.Max(Sanitize(group.groupenemiesinrange), SumEnemyStrengthWithinAngle(group));
        return enemy <= 0f ? 1f : own / Math.Max(1f, enemy);
    }
    catch
    {
        return 1f;
    }
}

private static float SumEnemyStrengthWithinAngle(Regiment group)
{
    try
    {
        if (group == null || group.unitrange == null || group.unitrange.enemystrengthwithinangle == null)
            return 0f;

        float total = 0f;
        for (int i = 0; i < group.unitrange.enemystrengthwithinangle.Length; i++)
            total += Math.Max(0f, group.unitrange.enemystrengthwithinangle[i]);
        return total;
    }
    catch
    {
        return 0f;
    }
}

private static bool MainEffortSupportAvailable(Regiment group, CommandIntentResolution resolution)
{
    try
    {
        if (group == null || !resolution.Found) return false;
        TacticalBattleOrchestrator side = TacticalBattleCoordinator.GetSideOrchestrator(group.alliance);
        if (side == null || side.Army == null || side.Army.CurrentCommandNodeIntents == null) return false;

        int sector = resolution.Intent.PrimarySector;
        var intents = side.Army.CurrentCommandNodeIntents;
        for (int i = 0; i < intents.Count; i++)
        {
            CommandNodeIntent intent = intents[i];
            if (intent.Role != DirectChildRole.Main) continue;
            if (Math.Abs(intent.PrimarySector - sector) <= 1)
                return true;
        }

        return false;
    }
    catch
    {
        return false;
    }
}

private static bool ScreenRoutedTargetVisible(Regiment unit)
{
    try
    {
        if (unit == null || unit.unitrange == null || unit.unitrange.enemyinrangereg == null)
            return false;

        for (int i = 0; i < unit.unitrange.enemyinrangereg.Count; i++)
        {
            Regiment enemy = unit.unitrange.enemyinrangereg[i];
            if (enemy != null && enemy.isrouted)
                return true;
        }

        return false;
    }
    catch
    {
        return false;
    }
}

private static int SafePlayerAlliance()
{
    try { return GameVars.playeralliance; }
    catch { return -99; }
}

private static bool SafeAiVsAi()
{
    try { return GameVars.ai_vs_ai; }
    catch { return false; }
}

private static float Sanitize(float value)
{
    if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
    return Math.Max(0f, value);
}
```

- [ ] **Step 5: Add orchestrator denial logging**

Add this method below `LogDeniedB6c(...)`:

```csharp
private static void LogDeniedOrchestrator(
    Regiment unit,
    Regiment group,
    TacticalOrchestratorChargeGate.Decision decision)
{
    OnceLog.Info("tactical-orchestrator-charge-gate", "BattleChargeGatePatch orchestrator branch wired");
    OnceLog.Info(
        "tactical-orchestrator-charge-gate:deny:" + SafeName(unit),
        "[TacticalOrchestratorChargeGate] action=deny" +
        " role=" + decision.Role +
        " reason=" + decision.Reason +
        " unit=" + SafeName(unit) + "#" + SafeInstanceId(unit) +
        " group=" + SafeName(group) + "#" + SafeInstanceId(group));
}
```

- [ ] **Step 6: Build to catch runtime-only compile errors**

Run:

```bash
./build.sh
```

Expected: build succeeds with `0 Error(s)`.

- [ ] **Step 7: Run the harness**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 8: Commit Task 3**

```bash
git add src/WhiskeyRealism/Patches/BattleChargeGatePatch.cs
git commit -m "feat(orchestrator): gate charges by command role"
```

## Task 4: Add Focused Runtime Safeguard Tests

**Files:**
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add tests for exact reason strings**

Add these registrations near the Task 1 charge-gate tests:

```csharp
("tactical orchestrator charge gate reason strings are stable", TacticalOrchestratorChargeGateReasonStringsStable),
("tactical orchestrator charge gate unknown role fails open", TacticalOrchestratorChargeGateUnknownRoleFailsOpen),
```

Add these test methods:

```csharp
private static void TacticalOrchestratorChargeGateReasonStringsStable()
{
    var cases = new[]
    {
        TacticalOrchestratorChargeGate.Decide(ChargeGateInput(role: DirectChildRole.Fix)).Reason,
        TacticalOrchestratorChargeGate.Decide(ChargeGateInput(role: DirectChildRole.Reserve)).Reason,
        TacticalOrchestratorChargeGate.Decide(ChargeGateInput(role: DirectChildRole.Fallback)).Reason,
        TacticalOrchestratorChargeGate.Decide(ChargeGateInput(role: DirectChildRole.RefuseLeft)).Reason,
        TacticalOrchestratorChargeGate.Decide(ChargeGateInput(role: DirectChildRole.RefuseRight)).Reason,
        TacticalOrchestratorChargeGate.Decide(ChargeGateInput(role: DirectChildRole.Screen)).Reason,
    };

    AssertEqual("role-fix-hold", cases[0], "fix reason");
    AssertEqual("role-reserve-hold", cases[1], "reserve reason");
    AssertEqual("role-fallback-no-charge", cases[2], "fallback reason");
    AssertEqual("role-refuse-left-no-charge", cases[3], "refuse-left reason");
    AssertEqual("role-refuse-right-no-charge", cases[4], "refuse-right reason");
    AssertEqual("screen-no-routed-target", cases[5], "screen reason");
}

private static void TacticalOrchestratorChargeGateUnknownRoleFailsOpen()
{
    var d = TacticalOrchestratorChargeGate.Decide(ChargeGateInput(role: DirectChildRole.Unknown));
    AssertEqual(TacticalOrchestratorChargeGate.Action.Allow, d.Action, "action");
    AssertEqual("unknown-role", d.Reason, "reason");
}
```

- [ ] **Step 2: Run the harness**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass with the new reason-string tests.

- [ ] **Step 3: Commit Task 4**

```bash
git add tests/WhiskeyRealism.Tests/Program.cs
git commit -m "test(orchestrator): lock charge gate reasons"
```

## Task 5: Update Living Docs

**Files:**
- Modify: `docs/patch-catalog.md`
- Modify: `docs/handoff.md`
- Modify: `MEMORY.md`

- [ ] **Step 1: Update patch catalog row #41**

In `docs/patch-catalog.md`, extend row `41 | BattleChargeGatePatch` with this text:

```markdown
Slice 3 orchestrator extension: when `Enable Tactical Orchestrator Charge Gate` is true, the existing #41 Prefix also resolves the calling command group's command-node role before `SetMovementMode(3)`. Main roles require favorable local odds, SupportMain requires adjacent main-effort support evidence, Fix/Reserve/Fallback/Refuse roles deny charge initiation, and Screen denies unless routed visible target evidence is present. Charge cancellation remains vanilla-mirrored and is never blocked. Missing command-tree state fails open.
```

- [ ] **Step 2: Update handoff active workstream**

In `docs/handoff.md`, update the tactical orchestrator active section with:

```markdown
Slice 3 charge gate plan exists at `docs/superpowers/plans/2026-05-10-tactical-orchestrator-slice-3-charge-gate.md`. Implementation extends existing #41 `BattleChargeGatePatch`; it must preserve vanilla charge cancellation and ship default-off behind `Enable Tactical Orchestrator Charge Gate`.
```

After implementation and deploy, replace the sentence with final harness/build/deploy/smoke evidence and the final DLL hash.

- [ ] **Step 3: Update repo memory checkpoint**

In `MEMORY.md`, add a short current-priority note:

```markdown
- **Tactical orchestrator Slice 3 charge gate plan drafted (2026-05-10):** Implementation should extend existing #41 `BattleChargeGatePatch`, not add another charge patch. The vanilla `MicroAICheckForCharges` initiation branch only toggles `SetMovementMode(3)` and does not pass a target, so Screen routed-target exceptions must remain conservative unless visible target evidence is read safely at runtime. Default-off flag: `Enable Tactical Orchestrator Charge Gate`.
```

- [ ] **Step 4: Check whitespace**

Run:

```bash
git diff --check
```

Expected: no output.

- [ ] **Step 5: Commit Task 5**

```bash
git add docs/patch-catalog.md docs/handoff.md MEMORY.md
git commit -m "docs(orchestrator): document slice 3 charge gate"
```

## Task 6: Build, Deploy, And Runtime Smoke

**Files:**
- No source changes unless verification exposes a defect.

- [ ] **Step 1: Run the full harness**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 2: Run diff hygiene**

```bash
git diff --check
git status --short --branch
```

Expected: no whitespace errors. Status is clean after commits or shows only intentional uncommitted smoke-doc updates.

- [ ] **Step 3: Build the DLL**

```bash
./build.sh
```

Expected: `Build succeeded`, `0 Warning(s)`, `0 Error(s)`, and `dist/WhiskeyRealism.dll` exists.

- [ ] **Step 4: Deploy the DLL**

Close Grand Tactician first, then run:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
```

Expected: copy succeeds. If it fails with `Invalid argument`, the game is still holding the loaded DLL; close the game and rerun the same command.

- [ ] **Step 5: Verify deployed hash**

```bash
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: sizes match and SHA-256 hashes match exactly.

- [ ] **Step 6: Gate-OFF smoke**

Set in `BepInEx/config/dev.kyle.whiskey-realism.cfg`:

```text
Enable Tactical Battle Orchestrator = true
Enable Tactical Orchestrator Charge Gate = false
Enable W&L Tactical Charge Guard = false
Enable Tactical Charge Denial = false
Enable Tactical Decision Matrix Logging = true
```

Launch a battle and inspect:

```bash
rg -n "TacticalOrchestratorChargeGate|TacticalChargeGuard|TacticalChargeDeny|TacticalCommandTree|Exception|missing-anchor|failed" "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Expected:
- `[TacticalCommandTree]` still appears when the orchestrator is active.
- No `[TacticalOrchestratorChargeGate] action=deny` lines.
- No repeated exceptions.
- No `missing-anchor` or `failed-owned` lines from #41.

- [ ] **Step 7: Gate-ON smoke**

Set:

```text
Enable Tactical Battle Orchestrator = true
Enable Tactical Orchestrator Charge Gate = true
Enable W&L Tactical Charge Guard = true
Enable Tactical Charge Denial = false
Enable Tactical Decision Matrix Logging = true
```

Launch a battle with AI charge opportunities and inspect:

```bash
rg -n "TacticalOrchestratorChargeGate|TacticalChargeGuard|TacticalChargeDeny|TacticalCommandTree|MicroAICheckForCharges|Exception|missing-anchor|failed" "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Expected:
- Denies, when they appear, are role-keyed: `role=Fix`, `role=Reserve`, `role=Fallback`, `role=RefuseLeft`, `role=RefuseRight`, or `role=Screen`.
- Main-role charge opportunities with favorable local odds are not denied by the orchestrator branch.
- Player-side or W&L player-subordinate charge attempts remain protected by the W&L gate.
- Vanilla charge cancellation still clears `movementmode == 3` when group stance changes away from charge.
- No repeated exceptions, no missing anchors, no `failed-owned`.

- [ ] **Step 8: Record smoke state**

If smoke passes, update:

```bash
git add docs/handoff.md docs/patch-catalog.md MEMORY.md
git commit -m "docs(orchestrator): record slice 3 charge smoke"
```

If smoke exposes a defect, leave `Enable Tactical Orchestrator Charge Gate = false`, record the exact log evidence in `docs/handoff.md`, and fix before enabling further behavior tests.

## Rollback

- Set `Enable Tactical Orchestrator Charge Gate = false` to disable the new orchestrator branch while keeping existing #41 W&L and B6c behavior available.
- Set `Enable W&L Tactical Charge Guard = false` to return #41 to vanilla unless `Enable Tactical Charge Denial` remains enabled.
- If #41 logs `failed-owned`, treat it as a blocker because the Prefix took ownership of the vanilla body and skipped vanilla to avoid duplicate side effects.

## Defer Boundaries

- The Screen chase-routed exception is allowed only when `ScreenRoutedTargetVisible(unit)` reads a routed visible enemy safely from `unit.unitrange.enemyinrangereg`; otherwise Screen denies with `screen-no-routed-target`.
- Do not add target-priority scoring to #41 in this slice. The vanilla initiation branch lacks a specific target, so target scoring belongs in a future charge-target assignment or stance/target-selection surface.
- Do not change `TacticalChargeViability` unless implementation proves #41 can provide exact target evidence.
- Do not make `Enable Tactical Orchestrator Charge Gate` default-on before a focused gate-ON smoke passes.

## Self-Review Checklist

- Spec coverage:
  - Uses Slice 3 vanilla owner `AIBattle.MicroAICheckForCharges`.
  - Extends existing #41 rather than adding a second patch.
  - Main permits only with favorable local odds.
  - SupportMain requires main-effort support evidence.
  - Fix/Reserve/Fallback/Refuse deny.
  - Screen denies without routed visible target evidence.
  - Cancellation branch remains allowed.
  - Player/W&L protection remains first.
- Placeholder scan:
  - No implementation step depends on undefined file paths.
  - No code step uses placeholder method names.
  - Runtime smoke commands and expected markers are explicit.
- Type consistency:
  - Pure helper type is `TacticalOrchestratorChargeGate`.
  - Config field is `EnableTacticalOrchestratorChargeGate`.
  - Config display key is `Enable Tactical Orchestrator Charge Gate`.
  - Runtime log marker is `[TacticalOrchestratorChargeGate]`.
