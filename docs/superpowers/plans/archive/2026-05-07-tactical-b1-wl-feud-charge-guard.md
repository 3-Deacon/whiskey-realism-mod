# Tactical B1 W&L Feud Charge Guard Implementation Plan

Status: implemented, console-tested, built, deployed, and hash-verified on 2026-05-07. Fresh B1 runtime denial smoke was explicitly deferred by user direction; keep it as a useful follow-up, not a blocker for B2/B3.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a config-gated W&L tactical guard that prevents vanilla AI feud/charge movement from issuing new movement intent to player-subordinate units while preserving vanilla cancellation, timing, and AI-vs-AI behavior.

**Architecture:** B1 is a narrow behavior slice, not the tactical-brain doctrine layer. It adds one pure W&L guard helper under `src/WhiskeyRealism/Tactical/` and two small Harmony Prefix replacements for the two verified ungated vanilla methods. The charge Prefix mirrors the small vanilla body so it can block only the initiation branch while still allowing cancellation; the feud Prefix mirrors the vanilla group-move body because there is no safe Postfix-only way to prevent `BattleUnits.SetWaypoint(...)` after it has queued delayed orders.

**Tech Stack:** BepInEx 5.4.x x64, HarmonyX, C# netstandard2.1, Unity 2021 Mono, console harness in `tests/WhiskeyRealism.Tests`, vanilla anchors from `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

---

## B0 Evidence Gate

B0 observer smoke has enough evidence to start this B1 plan.

Observed on 2026-05-07 in `BepInEx/LogOutput.log` with `Enable Tactical Observer = true`:

- `2016` total `[Tactical*]` observer lines.
- `[TacticalMacro]`, `[TacticalGroup]`, `[TacticalSector]`, `[TacticalOrder]`, `[TacticalFeud]`, `[TacticalCharge]`, `[TacticalReserve]`, `[TacticalArtillery]`, `[TacticalFallback]`, and `[TacticalPlayerOrder]` all fired.
- Player-subordinate control surface was observed:
  - line 1938: `[TacticalPlayerOrder] event=delivery relation=ai-to-player-subordinate ... targetUnderCommander=True ...`
  - line 1939: `[TacticalPlayerOrder] event=queued relation=ai-to-player-subordinate ... targetUnderCommander=True orderType=move-new ...`
  - line 2280 and 2281 repeated the same control surface.
- No `Tactical observer ... failed`, `TargetInvocationException`, `ERROR`, or repeated tactical warning lines were observed. The only warning was the known startup `CommunityHotfix` type lookup warning.

Do not broaden this plan because B0 produced artillery, reserve, fallback, and sector data. Those are for B2+ and the weapons/ammunition adjunct.

## Vanilla Anchors

Re-run before coding:

```bash
rg -n "private void MicroAICheckForCharges\(|private void CheckForFeudGroupActions\(|private static bool PerformAIActionDLCWL\(" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
```

Expected current output:

```text
4905:	private void MicroAICheckForCharges(Regiment aigroup, int restrictunittypes = 13)
4931:	private void CheckForFeudGroupActions()
5101:	private static bool PerformAIActionDLCWL(Regiment unit, Regiment groupforstancecheck = null)
```

Read the full method bodies before implementation:

```bash
nl -ba /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs | sed -n '4905,4960p'
nl -ba /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs | sed -n '5101,5116p'
```

Load-bearing vanilla facts:

- `MicroAICheckForCharges(...)` sets `movementmode = 3` only when group `ai_stance == 4`.
- The same method cancels an already charging unit by calling parameterless `SetMovementMode()` when the group is no longer in charge stance.
- Both charge initiation and cancellation write `aigroup.lastfeudactiontime`.
- `CheckForFeudGroupActions()` can call `bunits.SetWaypoint(... useorderdelay: true ...)` on a feuding formation.
- Neither method calls `PerformAIActionDLCWL(...)`.
- Many other tactical methods already call `PerformAIActionDLCWL(...)`; B1 must not double-gate unrelated surfaces.

## File Ownership

- Create: `src/WhiskeyRealism/Tactical/TacticalWlActionGuard.cs`
  - Pure decision logic for W&L tactical action permission.
  - No Unity, Harmony, BepInEx, `Regiment`, or `AIBattle` references.
- Create: `src/WhiskeyRealism/Patches/BattleChargeGatePatch.cs`
  - Prefix replacement for `AIBattle.MicroAICheckForCharges(...)`.
  - Mirrors vanilla branch conditions, blocks only new charge initiation for protected player-subordinate units, always preserves cancellation branch.
- Create: `src/WhiskeyRealism/Patches/BattleFeudActionGatePatch.cs`
  - Prefix replacement for `AIBattle.CheckForFeudGroupActions()`.
  - Mirrors vanilla group-move conditions, blocks the `SetWaypoint(...)` call only for formations that contain player-subordinate units.
- Modify: `src/WhiskeyRealism/Plugin.cs`
  - Add default-off config `Enable W&L Tactical Charge Guard`.
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`
  - Add pure guard tests.
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
  - Add explicit compile entry for `TacticalWlActionGuard.cs`.
- Modify after smoke: `docs/patch-catalog.md`, `docs/handoff.md`, `MEMORY.md`
  - Record #41/#42 if those remain the next ordinals at implementation time.

## Task 1: Add Pure Guard Tests

**Files:**
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Create later in Task 2: `src/WhiskeyRealism/Tactical/TacticalWlActionGuard.cs`

- [ ] **Step 1: Add test registrations**

In `tests/WhiskeyRealism.Tests/Program.cs`, add these entries near the existing tactical telemetry tests:

```csharp
("tactical wl guard allows non wl action", TacticalWlGuardAllowsNonWlAction),
("tactical wl guard allows when config disabled", TacticalWlGuardAllowsWhenConfigDisabled),
("tactical wl guard denies player subordinate charge initiation", TacticalWlGuardDeniesPlayerSubordinateChargeInitiation),
("tactical wl guard allows charge cancellation", TacticalWlGuardAllowsChargeCancellation),
("tactical wl guard denies feud move with attached subordinate", TacticalWlGuardDeniesFeudMoveWithAttachedSubordinate),
("tactical wl guard allows ai chain feud move", TacticalWlGuardAllowsAiChainFeudMove),
```

- [ ] **Step 2: Add failing test methods**

In `tests/WhiskeyRealism.Tests/Program.cs`, add these methods near the existing tactical test methods:

```csharp
private static void TacticalWlGuardAllowsNonWlAction()
{
    var decision = TacticalWlActionGuard.Decide(
        configEnabled: true,
        dlcScenarioActive: false,
        action: TacticalWlGuardAction.ChargeInitiation,
        unitUnderCommander: true,
        groupUnderCommander: true,
        attachedUnitUnderCommander: true);

    AssertTrue(decision.Allow, "non-W&L scenarios must remain vanilla");
    AssertEqual("wl-inactive", decision.Reason, "reason");
}

private static void TacticalWlGuardAllowsWhenConfigDisabled()
{
    var decision = TacticalWlActionGuard.Decide(
        configEnabled: false,
        dlcScenarioActive: true,
        action: TacticalWlGuardAction.ChargeInitiation,
        unitUnderCommander: true,
        groupUnderCommander: false,
        attachedUnitUnderCommander: false);

    AssertTrue(decision.Allow, "disabled config must leave vanilla behavior alone");
    AssertEqual("config-disabled", decision.Reason, "reason");
}

private static void TacticalWlGuardDeniesPlayerSubordinateChargeInitiation()
{
    var decision = TacticalWlActionGuard.Decide(
        configEnabled: true,
        dlcScenarioActive: true,
        action: TacticalWlGuardAction.ChargeInitiation,
        unitUnderCommander: true,
        groupUnderCommander: false,
        attachedUnitUnderCommander: false);

    AssertTrue(!decision.Allow, "player-subordinate charge initiation should be denied");
    AssertEqual("player-subordinate", decision.Reason, "reason");
}

private static void TacticalWlGuardAllowsChargeCancellation()
{
    var decision = TacticalWlActionGuard.Decide(
        configEnabled: true,
        dlcScenarioActive: true,
        action: TacticalWlGuardAction.ChargeCancellation,
        unitUnderCommander: true,
        groupUnderCommander: true,
        attachedUnitUnderCommander: true);

    AssertTrue(decision.Allow, "charge cancellation must always be preserved");
    AssertEqual("preserve-cancellation", decision.Reason, "reason");
}

private static void TacticalWlGuardDeniesFeudMoveWithAttachedSubordinate()
{
    var decision = TacticalWlActionGuard.Decide(
        configEnabled: true,
        dlcScenarioActive: true,
        action: TacticalWlGuardAction.FeudMovement,
        unitUnderCommander: false,
        groupUnderCommander: false,
        attachedUnitUnderCommander: true);

    AssertTrue(!decision.Allow, "feud movement should be denied when the group contains a player-subordinate unit");
    AssertEqual("player-subordinate-attached", decision.Reason, "reason");
}

private static void TacticalWlGuardAllowsAiChainFeudMove()
{
    var decision = TacticalWlActionGuard.Decide(
        configEnabled: true,
        dlcScenarioActive: true,
        action: TacticalWlGuardAction.FeudMovement,
        unitUnderCommander: false,
        groupUnderCommander: false,
        attachedUnitUnderCommander: false);

    AssertTrue(decision.Allow, "AI-chain feud movement should remain vanilla");
    AssertEqual("ai-chain", decision.Reason, "reason");
}
```

- [ ] **Step 3: Add explicit compile entry**

In `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, add this line next to the other tactical compile entries:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalWlActionGuard.cs" Link="TacticalWlActionGuard.cs" />
```

- [ ] **Step 4: Run tests and verify failure**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: build fails because `TacticalWlActionGuard`, `TacticalWlGuardAction`, and `TacticalWlGuardDecision` are not defined yet.

## Task 2: Implement Pure Guard Helper

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalWlActionGuard.cs`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Create the guard helper**

Create `src/WhiskeyRealism/Tactical/TacticalWlActionGuard.cs`:

```csharp
namespace WhiskeyRealism.Tactical
{
    public enum TacticalWlGuardAction
    {
        ChargeInitiation = 0,
        ChargeCancellation = 1,
        FeudMovement = 2
    }

    public readonly struct TacticalWlGuardDecision
    {
        public TacticalWlGuardDecision(bool allow, string reason)
        {
            Allow = allow;
            Reason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
        }

        public bool Allow { get; }
        public string Reason { get; }
    }

    public static class TacticalWlActionGuard
    {
        public static TacticalWlGuardDecision Decide(
            bool configEnabled,
            bool dlcScenarioActive,
            TacticalWlGuardAction action,
            bool unitUnderCommander,
            bool groupUnderCommander,
            bool attachedUnitUnderCommander)
        {
            if (!configEnabled) return new TacticalWlGuardDecision(true, "config-disabled");
            if (!dlcScenarioActive) return new TacticalWlGuardDecision(true, "wl-inactive");
            if (action == TacticalWlGuardAction.ChargeCancellation)
                return new TacticalWlGuardDecision(true, "preserve-cancellation");

            if (unitUnderCommander) return new TacticalWlGuardDecision(false, "player-subordinate");
            if (groupUnderCommander) return new TacticalWlGuardDecision(false, "player-subordinate-group");
            if (attachedUnitUnderCommander) return new TacticalWlGuardDecision(false, "player-subordinate-attached");

            return new TacticalWlGuardDecision(true, "ai-chain");
        }
    }
}
```

- [ ] **Step 2: Run tests and verify pass**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass, including:

```text
PASS tactical wl guard allows non wl action
PASS tactical wl guard allows when config disabled
PASS tactical wl guard denies player subordinate charge initiation
PASS tactical wl guard allows charge cancellation
PASS tactical wl guard denies feud move with attached subordinate
PASS tactical wl guard allows ai chain feud move
```

## Task 3: Add Config Gate

**Files:**
- Modify: `src/WhiskeyRealism/Plugin.cs`

- [ ] **Step 1: Add config field**

In `src/WhiskeyRealism/Plugin.cs`, near the tactical observer config fields, add:

```csharp
internal ConfigEntry<bool> EnableWlTacticalChargeGuard;
```

- [ ] **Step 2: Bind config in `Awake()`**

In `src/WhiskeyRealism/Plugin.cs`, directly after `TacticalObserverMinSecondsBetweenSummaries = Config.Bind(...)`, add:

```csharp
EnableWlTacticalChargeGuard = Config.Bind(
    "Tactical",
    "Enable W&L Tactical Charge Guard",
    false,
    "Default OFF for Slice B1. When enabled, blocks new ungated W&L AI feud/charge movement for player-subordinate units while preserving charge cancellation and AI-vs-AI behavior.");
```

- [ ] **Step 3: Build to confirm config code compiles**

Run:

```bash
./build.sh
```

Expected: build succeeds with `0 Warning(s)` and `0 Error(s)`.

## Task 4: Implement Charge Guard Patch

**Files:**
- Create: `src/WhiskeyRealism/Patches/BattleChargeGatePatch.cs`

- [ ] **Step 1: Create Prefix replacement patch**

Create `src/WhiskeyRealism/Patches/BattleChargeGatePatch.cs`:

```csharp
using System;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // B1 W&L guard for AIBattle.MicroAICheckForCharges. Vanilla owns charge
    // initiation and cancellation in one small method; this Prefix mirrors that
    // body so player-subordinate charge initiation can be blocked without
    // skipping the cancellation branch.
    [HarmonyPatch(typeof(AIBattle), "MicroAICheckForCharges")]
    internal static class BattleChargeGatePatch
    {
        private static FieldInfo _bunitsField;
        private static FieldInfo _isPlayerAiOrFeudField;

        [HarmonyPrefix]
        internal static bool Prefix(AIBattle __instance, Regiment aigroup, int restrictunittypes)
        {
            if (!Enabled()) return true;

            try
            {
                if (aigroup == null || aigroup.allattachedunits == null) return false;

                var bunits = BattleUnits(__instance);
                int isPlayerAiOrFeud = IsPlayerAiOrFeud(__instance);
                if (bunits == null) return true;

                var allattachedunits = aigroup.allattachedunits;
                for (int i = 0; i < allattachedunits.Length; i++)
                {
                    var unit = allattachedunits[i];
                    if (unit == null || unit.groupaiobject != aigroup) continue;

                    bool chargeStance = aigroup.ai_stance == 4;
                    bool typeAllowed = ((unit.unittyp <= 13 && restrictunittypes == 13) | (unit.unittyp == restrictunittypes));
                    bool feudAllowed = ((aigroup.ai_feudstance == -1) | (isPlayerAiOrFeud == 2));

                    if (!unit.permanentlydetached &&
                        chargeStance &&
                        !unit.isrouted &&
                        !unit.markedforrout &&
                        unit.movementmode != 4 &&
                        unit.movementmode != 5 &&
                        unit.movementmode != 6 &&
                        unit.movementmode != 1 &&
                        unit.movementmode != 3 &&
                        unit.movementmode != 2 &&
                        unit.unittyp != 5 &&
                        typeAllowed &&
                        feudAllowed &&
                        unit.lastaichargetime < GameVars.currenttimefromstart + GamePrefs.timetorenewaichargecheck)
                    {
                        var decision = TacticalWlActionGuard.Decide(
                            configEnabled: Plugin.Instance.EnableWlTacticalChargeGuard.Value,
                            dlcScenarioActive: DLC_WL.dlc_scenarioactive,
                            action: TacticalWlGuardAction.ChargeInitiation,
                            unitUnderCommander: unit.dlcw_isundercommander,
                            groupUnderCommander: aigroup.dlcw_isundercommander,
                            attachedUnitUnderCommander: false);

                        if (decision.Allow)
                        {
                            unit.SetMovementMode(3);
                            aigroup.lastfeudactiontime = uniStormSystem.Hour + (float)(bunits.battlepasseddays * 24);
                        }
                        else
                        {
                            aigroup.lastfeudactiontime = uniStormSystem.Hour + (float)(bunits.battlepasseddays * 24);
                            LogDenied(unit, aigroup, decision.Reason);
                        }
                    }

                    if (!unit.permanentlydetached &&
                        !unit.isrouted &&
                        !unit.markedforrout &&
                        unit.movementmode == 3 &&
                        !chargeStance &&
                        unit.unittyp != 5 &&
                        typeAllowed &&
                        feudAllowed)
                    {
                        unit.SetMovementMode();
                        aigroup.lastfeudactiontime = uniStormSystem.Hour + (float)(bunits.battlepasseddays * 24);
                    }
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-charge-guard:failed", "BattleChargeGatePatch failed; falling back to vanilla next call: " + ex.Message);
                return true;
            }

            return false;
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableWlTacticalChargeGuard.Value;
        }

        private static BattleUnits BattleUnits(AIBattle battle)
        {
            if (_bunitsField == null)
                _bunitsField = AccessTools.Field(typeof(AIBattle), "bunits");
            return _bunitsField != null ? _bunitsField.GetValue(battle) as BattleUnits : null;
        }

        private static int IsPlayerAiOrFeud(AIBattle battle)
        {
            if (_isPlayerAiOrFeudField == null)
                _isPlayerAiOrFeudField = AccessTools.Field(typeof(AIBattle), "isplayeraiorfeud");
            if (_isPlayerAiOrFeudField == null) return 0;
            object value = _isPlayerAiOrFeudField.GetValue(battle);
            return value is int result ? result : 0;
        }

        private static void LogDenied(Regiment unit, Regiment group, string reason)
        {
            OnceLog.Info("tactical-charge-guard", "BattleChargeGatePatch wired");
            Plugin.Log.LogInfo("[TacticalChargeGuard] action=deny reason=" + reason +
                " unit=" + SafeName(unit) +
                " group=" + SafeName(group));
        }

        private static string SafeName(Regiment unit)
        {
            if (unit == null) return "<null>";
            try { return ((UnityEngine.Object)((UnityEngine.Component)unit).gameObject).name; }
            catch { return unit.GetHashCode().ToString(); }
        }
    }
}
```

- [ ] **Step 2: Build and resolve compile issues only inside B1 files**

Run:

```bash
./build.sh
```

Expected: build succeeds. Resolve compile issues only inside `BattleChargeGatePatch.cs` unless the error proves the pure guard API needs adjustment.

## Task 5: Implement Feud Action Guard Patch

**Files:**
- Create: `src/WhiskeyRealism/Patches/BattleFeudActionGatePatch.cs`

- [ ] **Step 1: Create Prefix replacement patch**

Create `src/WhiskeyRealism/Patches/BattleFeudActionGatePatch.cs`:

```csharp
using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // B1 W&L guard for AIBattle.CheckForFeudGroupActions. Vanilla can move a
    // feuding formation toward the closest enemy through delayed SetWaypoint
    // without PerformAIActionDLCWL; this Prefix mirrors the vanilla method and
    // blocks only protected player-subordinate group movement.
    [HarmonyPatch(typeof(AIBattle), "CheckForFeudGroupActions")]
    internal static class BattleFeudActionGatePatch
    {
        private static FieldInfo _allGroupsAssignedField;
        private static FieldInfo _bunitsField;
        private static FieldInfo _isPlayerAiOrFeudField;
        private static MethodInfo _isGroupStillAbleToFightMethod;

        [HarmonyPrefix]
        internal static bool Prefix(AIBattle __instance)
        {
            if (!Enabled()) return true;

            try
            {
                var groups = AllGroupsAssigned(__instance);
                var bunits = BattleUnits(__instance);
                int isPlayerAiOrFeud = IsPlayerAiOrFeud(__instance);
                if (groups == null || bunits == null) return true;

                for (int i = 0; i < groups.Count; i++)
                {
                    var group = groups[i] as Regiment;
                    if (group == null) continue;

                    bool feudEligible =
                        group.unittyp > 13 &&
                        ((group.ai_feudstance >= 0) | (isPlayerAiOrFeud == 2)) &&
                        group.regimentpaths <= 0 &&
                        !group.pathinterrupted &&
                        IsGroupStillAbleToFight(__instance, group);

                    if (!feudEligible) continue;

                    float commanderInitiative = GameVars.commander[group.commander].GetCommanderInitiative();
                    float probability = Mathf.Pow(commanderInitiative, 2f) * GamePrefs.probfeudgroupmovement;
                    if (GameVars.commander[group.commander].political)
                        probability *= GamePrefs.chanceoffeudspoliticalcommanders;
                    if (!GameVars.commander[group.commander].westpoint && !GameVars.commander[group.commander].political)
                        probability *= GamePrefs.chanceoffeudsvolunteercommanders;

                    GameObject closestEnemy = group.GetClosestEnemyUnit(GamePrefs.neededdistancefeudgroupmovement);
                    if (UnityEngine.Random.Range(0f, 1f) > probability || closestEnemy == null) continue;

                    bool attachedUnderCommander = ContainsAttachedUnderCommander(group);
                    var decision = TacticalWlActionGuard.Decide(
                        configEnabled: Plugin.Instance.EnableWlTacticalChargeGuard.Value,
                        dlcScenarioActive: DLC_WL.dlc_scenarioactive,
                        action: TacticalWlGuardAction.FeudMovement,
                        unitUnderCommander: group.dlcw_isundercommander,
                        groupUnderCommander: group.dlcw_isundercommander,
                        attachedUnitUnderCommander: attachedUnderCommander);

                    group.lastfeudactiontime = uniStormSystem.Hour + (float)(bunits.battlepasseddays * 24);

                    if (decision.Allow)
                    {
                        bunits.SetWaypoint(group, closestEnemy.transform.position, newpath: true, doublequick: false, -1f, modifylastwaypoint: false, useorderdelay: true, -1f, -1, showmovementoptions: false);
                    }
                    else
                    {
                        LogDenied(group, decision.Reason);
                    }
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-feud-guard:failed", "BattleFeudActionGatePatch failed; falling back to vanilla next call: " + ex.Message);
                return true;
            }

            return false;
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableWlTacticalChargeGuard.Value;
        }

        private static IList AllGroupsAssigned(AIBattle battle)
        {
            if (_allGroupsAssignedField == null)
                _allGroupsAssignedField = AccessTools.Field(typeof(AIBattle), "allgroupsassigned");
            return _allGroupsAssignedField != null ? _allGroupsAssignedField.GetValue(battle) as IList : null;
        }

        private static BattleUnits BattleUnits(AIBattle battle)
        {
            if (_bunitsField == null)
                _bunitsField = AccessTools.Field(typeof(AIBattle), "bunits");
            return _bunitsField != null ? _bunitsField.GetValue(battle) as BattleUnits : null;
        }

        private static int IsPlayerAiOrFeud(AIBattle battle)
        {
            if (_isPlayerAiOrFeudField == null)
                _isPlayerAiOrFeudField = AccessTools.Field(typeof(AIBattle), "isplayeraiorfeud");
            if (_isPlayerAiOrFeudField == null) return 0;
            object value = _isPlayerAiOrFeudField.GetValue(battle);
            return value is int result ? result : 0;
        }

        private static bool IsGroupStillAbleToFight(AIBattle battle, Regiment group)
        {
            if (_isGroupStillAbleToFightMethod == null)
                _isGroupStillAbleToFightMethod = AccessTools.Method(typeof(AIBattle), "IsGroupStillAbleToFight");
            if (_isGroupStillAbleToFightMethod == null) return true;
            object value = _isGroupStillAbleToFightMethod.Invoke(battle, new object[] { group });
            return value is bool result && result;
        }

        private static bool ContainsAttachedUnderCommander(Regiment group)
        {
            if (group == null || group.allattachedunits == null) return false;
            for (int i = 0; i < group.allattachedunits.Length; i++)
            {
                var unit = group.allattachedunits[i];
                if (unit != null && unit.dlcw_isundercommander) return true;
            }
            return false;
        }

        private static void LogDenied(Regiment group, string reason)
        {
            OnceLog.Info("tactical-feud-guard", "BattleFeudActionGatePatch wired");
            Plugin.Log.LogInfo("[TacticalFeudGuard] action=deny reason=" + reason +
                " group=" + SafeName(group));
        }

        private static string SafeName(Regiment unit)
        {
            if (unit == null) return "<null>";
            try { return ((UnityEngine.Object)((UnityEngine.Component)unit).gameObject).name; }
            catch { return unit.GetHashCode().ToString(); }
        }
    }
}
```

- [ ] **Step 2: Build and resolve compile issues only inside B1 files**

Run:

```bash
./build.sh
```

Expected: build succeeds. Resolve compile issues only inside `BattleFeudActionGatePatch.cs` unless the error proves the pure guard API needs adjustment.

## Task 6: Verify B1 Locally

**Files:**
- All B1 source/test files.

- [ ] **Step 1: Run console harness**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected:

```text
PASS tactical wl guard allows non wl action
PASS tactical wl guard allows when config disabled
PASS tactical wl guard denies player subordinate charge initiation
PASS tactical wl guard allows charge cancellation
PASS tactical wl guard denies feud move with attached subordinate
PASS tactical wl guard allows ai chain feud move
```

No `FAIL` lines.

- [ ] **Step 2: Run build**

Run:

```bash
./build.sh
```

Expected: `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 3: Run diff whitespace check**

Run:

```bash
git diff --check
```

Expected: no output.

- [ ] **Step 4: Inspect B1 patch diff**

Run:

```bash
git diff -- src/WhiskeyRealism/Plugin.cs src/WhiskeyRealism/Tactical/TacticalWlActionGuard.cs src/WhiskeyRealism/Patches/BattleChargeGatePatch.cs src/WhiskeyRealism/Patches/BattleFeudActionGatePatch.cs tests/WhiskeyRealism.Tests/Program.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected:

- `Plugin.cs` only adds the default-off config gate.
- `TacticalWlActionGuard.cs` has no Unity or vanilla references.
- `BattleChargeGatePatch.cs` only targets `MicroAICheckForCharges`.
- `BattleFeudActionGatePatch.cs` only targets `CheckForFeudGroupActions`.
- No tactical state is written to `StrategicCoordinator` or `whiskeyrealism.json`.

## Task 7: Deploy And Smoke B1

**Files:**
- Runtime DLL: `dist/WhiskeyRealism.dll`
- Deployed DLL: `/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll`
- Config: `/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/config/dev.kyle.whiskey-realism.cfg`
- Log: `/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log`

- [ ] **Step 1: Deploy DLL**

Run:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
```

Expected: command exits `0`. If it fails with `Invalid argument`, the game is running and Windows has locked the DLL; close GTCW and rerun the copy.

- [ ] **Step 2: Verify deployed DLL**

Run:

```bash
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: timestamps are current enough for the deploy, sizes match, and both SHA-256 hashes are identical.

- [ ] **Step 3: Enable B1 for focused smoke**

Edit `/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/config/dev.kyle.whiskey-realism.cfg`:

```text
Enable Tactical Observer = true
Enable W&L Tactical Charge Guard = true
Tactical Observer Verbose Logging = false
Tactical Observer Min Seconds Between Summaries = 30
```

Restart GTCW after changing the config.

- [ ] **Step 4: Run W&L subordinate battle smoke**

Use a W&L land battle where the player has a subordinate command. Let the battle run until the B0 player-subordinate telemetry appears again.

Required log command:

```bash
rg -n "TacticalChargeGuard|TacticalFeudGuard|TacticalPlayerOrder|TacticalCharge|TacticalFeud|sourceUnderCommander=True|targetUnderCommander=True|Tactical observer .*failed|TargetInvocationException|ERROR|WARN|Exception" "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Pass criteria:

- `[TacticalPlayerOrder]` still shows a player-subordinate surface with `targetUnderCommander=True` or `sourceUnderCommander=True`.
- `[TacticalChargeGuard] action=deny` fires if a player-subordinate unit reaches the charge-initiation branch.
- `[TacticalFeudGuard] action=deny` fires if a protected group reaches the feud-movement branch.
- No repeated `Tactical observer ... failed`, `TargetInvocationException`, `ERROR`, or Harmony failure.
- AI-chain tactical movement still appears with `sourceUnderCommander=False targetUnderCommander=False`.
- If no guard denial fires, classify the smoke as "B1 wired, denial path not exercised" and run a charge/feud-heavy subordinate scenario before default-on discussion.

## Task 8: Documentation Closeout

**Files:**
- Modify: `docs/patch-catalog.md`
- Modify: `docs/handoff.md`
- Modify: `MEMORY.md`
- Optional modify: `docs/superpowers/plans/archive/2026-05-05-tactical-brain-master-sequencing.md`

- [ ] **Step 1: Patch catalog**

If `docs/patch-catalog.md` still says next ordinal is #41, add these rows after #40:

```markdown
| 41 | `BattleChargeGatePatch` | Prefix replacement | `Patches/BattleChargeGatePatch.cs` | `AIBattle.MicroAICheckForCharges` (4905) | Slice B1 default-off W&L tactical charge guard. Mirrors vanilla charge initiation/cancellation, blocks new player-subordinate charge initiation when `Enable W&L Tactical Charge Guard` is true, preserves charge cancellation and `lastfeudactiontime`, and leaves non-W&L / AI-chain behavior vanilla. |
| 42 | `BattleFeudActionGatePatch` | Prefix replacement | `Patches/BattleFeudActionGatePatch.cs` | `AIBattle.CheckForFeudGroupActions` (4931) | Slice B1 default-off W&L feud movement guard. Mirrors vanilla feud movement probability and delayed `SetWaypoint` behavior, blocks protected player-subordinate group movement when `Enable W&L Tactical Charge Guard` is true, preserves feud timing, and leaves non-W&L / AI-chain behavior vanilla. |
```

If another patch claimed #41 first, use the next two stable ordinals and update the "Pending" line accordingly.

- [ ] **Step 2: Handoff**

In `docs/handoff.md`, update Slice B and "What just shipped" with:

```markdown
- **2026-05-07 — Slice B0 tactical observer smoke closed; B1 plan written.** B0 emitted all required `[Tactical*]` families and proved the W&L player-subordinate control surface with `[TacticalPlayerOrder] relation=ai-to-player-subordinate ... targetUnderCommander=True` at log lines 1938/1939 and 2280/2281. No tactical observer failures, `TargetInvocationException`, or repeated tactical warnings were observed. B1 remains a default-off behavior slice targeting only `AIBattle.MicroAICheckForCharges(...)` and `AIBattle.CheckForFeudGroupActions()`.
```

After B1 implementation smoke, append the deployed SHA-256 and whether `[TacticalChargeGuard]` / `[TacticalFeudGuard]` denial paths fired.

- [ ] **Step 3: MEMORY**

In `MEMORY.md`, replace the stale B0 pending bullet with:

```markdown
- **Slice B0 tactical observer smoke closed on 2026-05-07.** A focused W&L land-battle run with `Enable Tactical Observer = true` emitted every required `[Tactical*]` family, including `[TacticalPlayerOrder] relation=ai-to-player-subordinate ... targetUnderCommander=True`, with no tactical observer failures. B1 is now allowed to proceed as a default-off narrow W&L charge/feud guard plan targeting only `AIBattle.MicroAICheckForCharges(...)` and `AIBattle.CheckForFeudGroupActions()`.
```

- [ ] **Step 4: Master sequencing plan**

In `docs/superpowers/plans/archive/2026-05-05-tactical-brain-master-sequencing.md`, mark B0 as smoke-closed in prose and link this plan:

```markdown
B0 tactical observer smoke closed on 2026-05-07. B1 plan lives at `docs/superpowers/plans/archive/2026-05-07-tactical-b1-wl-feud-charge-guard.md`.
```

## Task 9: Commit

**Files:**
- `docs/superpowers/plans/archive/2026-05-07-tactical-b1-wl-feud-charge-guard.md`
- B1 source/test files if implementation has been executed.
- B1 closeout docs if implementation has been smoke-verified.

- [ ] **Step 1: For plan-only commit**

If only this plan was added, run:

```bash
git add docs/superpowers/plans/archive/2026-05-07-tactical-b1-wl-feud-charge-guard.md
git commit -m "docs: plan tactical b1 wl feud charge guard"
```

Expected: one docs-only commit.

- [ ] **Step 2: For implementation commit**

After Tasks 1-8 are implemented and smoke-verified, run:

```bash
git add src/WhiskeyRealism/Plugin.cs src/WhiskeyRealism/Tactical/TacticalWlActionGuard.cs src/WhiskeyRealism/Patches/BattleChargeGatePatch.cs src/WhiskeyRealism/Patches/BattleFeudActionGatePatch.cs tests/WhiskeyRealism.Tests/Program.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj docs/patch-catalog.md docs/handoff.md MEMORY.md docs/superpowers/plans/archive/2026-05-05-tactical-brain-master-sequencing.md
git commit -m "feat: add tactical b1 wl feud charge guard"
```

Expected: one focused implementation commit with tests, build, deploy/hash evidence, and runtime smoke recorded in docs.

## Rollback

To disable without reverting code:

```text
Enable W&L Tactical Charge Guard = false
```

Restart GTCW after changing the config.

To revert before commit:

- remove `src/WhiskeyRealism/Tactical/TacticalWlActionGuard.cs`;
- remove `src/WhiskeyRealism/Patches/BattleChargeGatePatch.cs`;
- remove `src/WhiskeyRealism/Patches/BattleFeudActionGatePatch.cs`;
- remove `EnableWlTacticalChargeGuard` config field/bind from `Plugin.cs`;
- remove the six test registrations and methods from `tests/WhiskeyRealism.Tests/Program.cs`;
- remove the `TacticalWlActionGuard.cs` compile entry from `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`;
- remove unshipped #41/#42 catalog rows and B1 closeout prose.

Do not use `git reset --hard` because unrelated work may be present.

## Self-Review Checklist

- Spec coverage: this plan covers only B1 W&L charge/feud control safety from the tactical master sequence.
- Out of scope: macro stance scoring, group sector stance, reserve relief, artillery doctrine, withdrawal doctrine, weapons/ammunition, and persistence.
- Vanilla side effects: charge cancellation is always allowed; `lastfeudactiontime` is updated on denied charge/feud attempts to prevent repeated immediate retries; allowed AI-chain actions still use vanilla `SetMovementMode` / `SetWaypoint`.
- Config safety: behavior is default-off and requires `Enable W&L Tactical Charge Guard = true`.
- Runtime acceptance: B1 is not complete unless deployed DLL hash matches `dist` and W&L subordinate battle smoke is checked.
