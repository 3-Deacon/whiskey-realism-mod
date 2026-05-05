# Tactical B0 Observer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the first Slice B tactical observer: read-only, bounded tactical battle telemetry that proves vanilla runtime shape before any tactical behavior patch lands.

**Architecture:** Add a small `WhiskeyRealism.Tactical` model/telemetry layer plus one Harmony observer patch file. The patch file reads selected `AIBattle`, `Regiment`, and `BattleUnits` state after vanilla methods run, formats stable signature-gated log lines, and never writes `macroai`, `ai_stance`, movement orders, reserve lists, artillery behavior, fallback state, retreat state, or strategic/persistent state.

**Tech Stack:** BepInEx 5.4.x x64, HarmonyX, C# netstandard2.1, Unity 2021 Mono, console harness in `tests/WhiskeyRealism.Tests`, vanilla anchors from `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

---

## Source Inputs

Read these before implementation:

- `AGENTS.md`
- `docs/handoff.md`
- `docs/patch-catalog.md`
- `docs/superpowers/AGENTS.md`
- `docs/superpowers/specs/2026-05-05-tactical-brain-design.md`
- `docs/superpowers/specs/2026-05-05-tactical-brain-vanilla-verification.md`
- `docs/superpowers/plans/2026-05-05-tactical-brain-master-sequencing.md`
- `src/WhiskeyRealism/Patches/AGENTS.md`
- `tests/WhiskeyRealism.Tests/AGENTS.md`

## Non-Negotiable Boundary

B0 is telemetry only.

Do not:

- change `macroai`;
- change `ai_stance` or `ai_stanceordered`;
- call `SetMovementMode`;
- call `BattleUnits.SetWaypoint`;
- call `bunits.ChangeStance`;
- mutate `objectivechain`, `reservegroups`, `linegroup_*`, `artillerygroups`, `screeninggroups`, `allgroupsassigned`, or `unitsused`;
- call `TimePanel.SetRetreatTimer`;
- write to `StrategicCoordinator` tactical state;
- write tactical state into `whiskeyrealism.json`;
- add behavior configs for B1 or later slices.

B0 may:

- read method inputs and selected private fields by reflection;
- compare Prefix and Postfix snapshots to infer what vanilla did;
- log bounded summaries;
- warn once and return to vanilla on extraction failures.

## Verified Vanilla Anchors

These anchors were rechecked on 2026-05-05. Re-run the command before code changes and update this plan if line numbers drift.

Run:

```bash
rg -n "private void CheckGlobalAIStrategy\(|private void AdjustGroupAIStance\(|private void MicroAICheckForCharges\(|private void CheckForFeudGroupActions\(|private unsafe void CheckUseOfReserves\(|private void LinkReservesToLineGroup\(|private void AssignReserves\(|private void CheckAIBombardment\(|private unsafe void CheckLineFallbacks\(|private unsafe void MicroAICheckForRetreats\(|private static bool PerformAIActionDLCWL\(|public void SetWaypoint\(" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
```

Expected current output:

```text
3869:	private void CheckAIBombardment(Regiment aigroup)
4221:	private void AdjustGroupAIStance()
4817:	private unsafe void MicroAICheckForRetreats(Regiment aigroup)
4905:	private void MicroAICheckForCharges(Regiment aigroup, int restrictunittypes = 13)
4931:	private void CheckForFeudGroupActions()
5101:	private static bool PerformAIActionDLCWL(Regiment unit, Regiment groupforstancecheck = null)
5118:	private unsafe void CheckLineFallbacks(Regiment aigroup)
6062:	private unsafe void CheckUseOfReserves(Regiment aigroup)
6314:	private void CheckGlobalAIStrategy()
6642:	private void LinkReservesToLineGroup()
7017:	private void AssignReserves()
91225:	public void SetWaypoint(GameObject unit, Vector3 targetpos, bool newpath = true, bool doublequick = false, float manualfinalrotation = -1f, bool modifylastwaypoint = false, bool useorderdelay = true, float timetomove = -1f, int direction = -1, bool showmovementoptions = true, bool ignorebattlemonuments = false, bool groupmoveonly = false, bool ignoredisabledships = false, bool checkforreadiness = true, bool clearinterruptionpaths = true)
```

Key side effects to preserve:

- `MicroAICheckForCharges(...)` sets movement mode `3` when group `ai_stance == 4`, cancels charge by calling parameterless `SetMovementMode()`, and writes `aigroup.lastfeudactiontime` on both branches.
- `CheckForFeudGroupActions()` can call `bunits.SetWaypoint(... useorderdelay: true ...)` for a feuding group. B0 only observes this; B1 decides whether to gate it.
- `CheckGlobalAIStrategy()` owns hard retreat/end-battle paths and debug/save-state macro overrides. B0 only logs the resulting state.
- `AdjustGroupAIStance()` already calls `PerformAIActionDLCWL(unitsused[i])`. B0 only logs group stance changes.

## File Structure

Create:

- `src/WhiskeyRealism/Tactical/TacticalBattleContext.cs`
- `src/WhiskeyRealism/Tactical/TacticalTelemetry.cs`
- `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs`

Modify:

- `src/WhiskeyRealism/Plugin.cs`
- `src/WhiskeyRealism/WhiskeyRealism.csproj`
- `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- `tests/WhiskeyRealism.Tests/Program.cs`
- `docs/patch-catalog.md`
- `docs/handoff.md`
- `MEMORY.md`

No nested `AGENTS.md` is needed because the existing patch/test guidance already covers these paths.

## Task 1: Add Tactical Config Gates

**Files:**

- Modify: `src/WhiskeyRealism/Plugin.cs`

- [ ] **Step 1: Add config fields**

Add these fields near the existing diagnostic config entries:

```csharp
internal ConfigEntry<bool> EnableTacticalObserver;
internal ConfigEntry<bool> TacticalObserverVerboseLogging;
internal ConfigEntry<int> TacticalObserverMinSecondsBetweenSummaries;
```

- [ ] **Step 2: Bind config values**

Bind them in `Awake()` after the existing diagnostics entries:

```csharp
EnableTacticalObserver = Config.Bind(
    "Tactical",
    "Enable Tactical Observer",
    true,
    "Default ON for Slice B B0. Emits bounded read-only battle telemetry; does not change tactical AI behavior.");
TacticalObserverVerboseLogging = Config.Bind(
    "Tactical",
    "Tactical Observer Verbose Logging",
    false,
    "Emit lower-throttle tactical observer detail for focused smoke runs. Default observer mode remains signature-gated.");
TacticalObserverMinSecondsBetweenSummaries = Config.Bind(
    "Tactical",
    "Tactical Observer Min Seconds Between Summaries",
    30,
    "Minimum wall-clock seconds between repeated tactical observer summaries with the same signature.");
```

- [ ] **Step 3: Verify no behavior config was added**

Search:

```bash
rg -n "Enable Tactical .*Doctrine|Enable W&L Tactical Charge Guard|Enable Tactical Macro|Enable Tactical Group|Enable Tactical Reserve|Enable Tactical Withdrawal" src/WhiskeyRealism/Plugin.cs
```

Expected: no matches. B0 must not add behavior gates for B1+.

## Task 2: Create Pure Tactical Telemetry Models

**Files:**

- Create: `src/WhiskeyRealism/Tactical/TacticalBattleContext.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalTelemetry.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`

- [ ] **Step 1: Add test compile entries**

Add these entries to `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalBattleContext.cs" Link="TacticalBattleContext.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalTelemetry.cs" Link="TacticalTelemetry.cs" />
```

- [ ] **Step 2: Create tactical context models**

Create `src/WhiskeyRealism/Tactical/TacticalBattleContext.cs`:

```csharp
using System;

namespace WhiskeyRealism.Tactical
{
    public enum TacticalSectorSource
    {
        None = 0,
        ObjectiveChain = 1,
        AngleSlice = 2
    }

    public enum TacticalObservedEvent
    {
        Macro = 0,
        Group = 1,
        Charge = 2,
        Feud = 3,
        Sector = 4,
        Order = 5,
        Reserve = 6,
        Artillery = 7,
        Fallback = 8
    }

    public sealed class TacticalBattleContext
    {
        public int Side { get; set; }
        public int Alliance { get; set; }
        public int MacroAi { get; set; }
        public int GroupCount { get; set; }
        public int ChargingCount { get; set; }
        public int FeudGroupCount { get; set; }
        public int ReserveGroupCount { get; set; }
        public int ArtilleryGroupCount { get; set; }
        public int FallbackCount { get; set; }
        public int RetreatingCount { get; set; }
        public int VisibleEnemyCount { get; set; }
        public int ObjectiveChainCount { get; set; }
        public TacticalSectorSource SectorSource { get; set; }
        public string SectorSignature { get; set; }
        public string OrderSignature { get; set; }
        public float ForceBalance { get; set; }
        public float ReinforcementsWithin24Hours { get; set; }

        public static TacticalBattleContext Empty()
        {
            return new TacticalBattleContext
            {
                Side = -1,
                Alliance = -1,
                MacroAi = -99,
                SectorSource = TacticalSectorSource.None,
                SectorSignature = "",
                OrderSignature = ""
            };
        }
    }

    public sealed class TacticalObserverSnapshot
    {
        public int GroupCount { get; set; }
        public int ChargingCount { get; set; }
        public int FeudGroupCount { get; set; }
        public int ReserveGroupCount { get; set; }
        public int ArtilleryGroupCount { get; set; }
        public int FallbackCount { get; set; }
        public int RetreatingCount { get; set; }
        public string Signature { get; set; }

        public static TacticalObserverSnapshot Empty()
        {
            return new TacticalObserverSnapshot { Signature = "" };
        }
    }
}
```

- [ ] **Step 3: Create telemetry formatter**

Create `src/WhiskeyRealism/Tactical/TacticalTelemetry.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical
{
    public static class TacticalTelemetry
    {
        public static string MacroName(int macroAi)
        {
            switch (macroAi)
            {
                case -1: return "dynamic";
                case 0: return "assault";
                case 1: return "attack";
                case 2: return "defend";
                case 3: return "retreat";
                default: return "unknown";
            }
        }

        public static string SectorSourceName(TacticalSectorSource source)
        {
            switch (source)
            {
                case TacticalSectorSource.ObjectiveChain: return "objective-chain";
                case TacticalSectorSource.AngleSlice: return "angle-slice";
                default: return "none";
            }
        }

        public static string Summary(TacticalObservedEvent eventType, TacticalBattleContext context)
        {
            if (context == null) context = TacticalBattleContext.Empty();

            return Prefix(eventType) +
                   " side=" + context.Side +
                   " alliance=" + context.Alliance +
                   " macro=" + MacroName(context.MacroAi) +
                   " groups=" + ClampCount(context.GroupCount) +
                   " charging=" + ClampCount(context.ChargingCount) +
                   " feud=" + ClampCount(context.FeudGroupCount) +
                   " reserves=" + ClampCount(context.ReserveGroupCount) +
                   " artillery=" + ClampCount(context.ArtilleryGroupCount) +
                   " fallback=" + ClampCount(context.FallbackCount) +
                   " retreating=" + ClampCount(context.RetreatingCount) +
                   " visibleEnemy=" + ClampCount(context.VisibleEnemyCount) +
                   " chains=" + ClampCount(context.ObjectiveChainCount) +
                   " sectorSource=" + SectorSourceName(context.SectorSource) +
                   " forceBalance=" + FormatFloat(context.ForceBalance) +
                   " reinf24h=" + FormatFloat(context.ReinforcementsWithin24Hours) +
                   " sectorSig=" + Safe(context.SectorSignature) +
                   " orderSig=" + Safe(context.OrderSignature);
        }

        public static string Delta(TacticalObserverSnapshot before, TacticalObserverSnapshot after)
        {
            if (before == null) before = TacticalObserverSnapshot.Empty();
            if (after == null) after = TacticalObserverSnapshot.Empty();

            return "groups=" + before.GroupCount + "->" + after.GroupCount +
                   " charging=" + before.ChargingCount + "->" + after.ChargingCount +
                   " feud=" + before.FeudGroupCount + "->" + after.FeudGroupCount +
                   " reserves=" + before.ReserveGroupCount + "->" + after.ReserveGroupCount +
                   " artillery=" + before.ArtilleryGroupCount + "->" + after.ArtilleryGroupCount +
                   " fallback=" + before.FallbackCount + "->" + after.FallbackCount +
                   " retreating=" + before.RetreatingCount + "->" + after.RetreatingCount;
        }

        public static string Signature(TacticalObservedEvent eventType, TacticalBattleContext context)
        {
            if (context == null) context = TacticalBattleContext.Empty();

            return ((int)eventType).ToString() +
                   "|" + context.Side +
                   "|" + context.Alliance +
                   "|" + context.MacroAi +
                   "|" + context.GroupCount +
                   "|" + context.ChargingCount +
                   "|" + context.FeudGroupCount +
                   "|" + context.ReserveGroupCount +
                   "|" + context.ArtilleryGroupCount +
                   "|" + context.FallbackCount +
                   "|" + context.RetreatingCount +
                   "|" + context.VisibleEnemyCount +
                   "|" + context.ObjectiveChainCount +
                   "|" + ((int)context.SectorSource) +
                   "|" + Bucket(context.ForceBalance) +
                   "|" + Bucket(context.ReinforcementsWithin24Hours) +
                   "|" + Safe(context.SectorSignature) +
                   "|" + Safe(context.OrderSignature);
        }

        public static bool ShouldEmit(
            IDictionary<string, float> lastEmittedAt,
            string key,
            string signature,
            float nowSeconds,
            float minSeconds,
            bool verbose)
        {
            string fullKey = key + "|" + Safe(signature);
            if (verbose) return true;
            if (lastEmittedAt == null) return true;
            if (!lastEmittedAt.TryGetValue(fullKey, out var last))
            {
                lastEmittedAt[fullKey] = nowSeconds;
                return true;
            }

            if (nowSeconds - last >= minSeconds)
            {
                lastEmittedAt[fullKey] = nowSeconds;
                return true;
            }

            return false;
        }

        private static string Prefix(TacticalObservedEvent eventType)
        {
            switch (eventType)
            {
                case TacticalObservedEvent.Macro: return "[TacticalMacro]";
                case TacticalObservedEvent.Group: return "[TacticalGroup]";
                case TacticalObservedEvent.Charge: return "[TacticalCharge]";
                case TacticalObservedEvent.Feud: return "[TacticalFeud]";
                case TacticalObservedEvent.Sector: return "[TacticalSector]";
                case TacticalObservedEvent.Order: return "[TacticalOrder]";
                case TacticalObservedEvent.Reserve: return "[TacticalReserve]";
                case TacticalObservedEvent.Artillery: return "[TacticalArtillery]";
                case TacticalObservedEvent.Fallback: return "[TacticalFallback]";
                default: return "[Tactical]";
            }
        }

        private static int ClampCount(int value)
        {
            return value < 0 ? 0 : value;
        }

        private static string FormatFloat(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0.00";
            return value.ToString("0.00");
        }

        private static string Bucket(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0.0";
            return (Math.Round(value * 2f) / 2f).ToString("0.0");
        }

        private static string Safe(string value)
        {
            return string.IsNullOrEmpty(value) ? "-" : value.Replace(' ', '_');
        }
    }
}
```

## Task 3: Add Pure Telemetry Tests

**Files:**

- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add using**

Add:

```csharp
using WhiskeyRealism.Tactical;
```

- [ ] **Step 2: Register tests**

Add these test entries near the other tactical/strategic utility tests:

```csharp
("tactical telemetry maps macro names", TacticalTelemetryMapsMacroNames),
("tactical telemetry summary handles null", TacticalTelemetrySummaryHandlesNull),
("tactical telemetry signature changes on material fields", TacticalTelemetrySignatureChangesOnMaterialFields),
("tactical telemetry throttle suppresses repeated signature", TacticalTelemetryThrottleSuppressesRepeatedSignature),
("tactical telemetry delta formats before after counts", TacticalTelemetryDeltaFormatsBeforeAfterCounts),
```

- [ ] **Step 3: Add test methods**

Add these methods to `Program.cs`:

```csharp
private static void TacticalTelemetryMapsMacroNames()
{
    AssertEqual("dynamic", TacticalTelemetry.MacroName(-1), "macro -1");
    AssertEqual("assault", TacticalTelemetry.MacroName(0), "macro 0");
    AssertEqual("attack", TacticalTelemetry.MacroName(1), "macro 1");
    AssertEqual("defend", TacticalTelemetry.MacroName(2), "macro 2");
    AssertEqual("retreat", TacticalTelemetry.MacroName(3), "macro 3");
    AssertEqual("unknown", TacticalTelemetry.MacroName(99), "macro unknown");
}

private static void TacticalTelemetrySummaryHandlesNull()
{
    string summary = TacticalTelemetry.Summary(TacticalObservedEvent.Macro, null);
    AssertContains(summary, "[TacticalMacro]", "prefix");
    AssertContains(summary, "side=-1", "empty side");
    AssertContains(summary, "macro=unknown", "empty macro");
    AssertContains(summary, "sectorSource=none", "empty sector source");
}

private static void TacticalTelemetrySignatureChangesOnMaterialFields()
{
    var baseline = new TacticalBattleContext
    {
        Side = 1,
        Alliance = 0,
        MacroAi = -1,
        GroupCount = 4,
        SectorSource = TacticalSectorSource.ObjectiveChain,
        SectorSignature = "chains=2"
    };
    var changed = new TacticalBattleContext
    {
        Side = 1,
        Alliance = 0,
        MacroAi = 1,
        GroupCount = 4,
        SectorSource = TacticalSectorSource.ObjectiveChain,
        SectorSignature = "chains=2"
    };

    string a = TacticalTelemetry.Signature(TacticalObservedEvent.Macro, baseline);
    string b = TacticalTelemetry.Signature(TacticalObservedEvent.Macro, changed);
    if (a == b) throw new Exception("expected tactical signature to change when macro changes");
}

private static void TacticalTelemetryThrottleSuppressesRepeatedSignature()
{
    var emitted = new Dictionary<string, float>();
    bool first = TacticalTelemetry.ShouldEmit(emitted, "macro", "sig", 10f, 30f, verbose: false);
    bool second = TacticalTelemetry.ShouldEmit(emitted, "macro", "sig", 20f, 30f, verbose: false);
    bool third = TacticalTelemetry.ShouldEmit(emitted, "macro", "sig", 41f, 30f, verbose: false);
    if (!first) throw new Exception("expected first tactical signature emit");
    if (second) throw new Exception("expected repeated tactical signature to be throttled");
    if (!third) throw new Exception("expected tactical signature to emit after throttle window");
}

private static void TacticalTelemetryDeltaFormatsBeforeAfterCounts()
{
    string delta = TacticalTelemetry.Delta(
        new TacticalObserverSnapshot { GroupCount = 2, ChargingCount = 0, ReserveGroupCount = 1 },
        new TacticalObserverSnapshot { GroupCount = 2, ChargingCount = 1, ReserveGroupCount = 2 });

    AssertContains(delta, "groups=2->2", "group delta");
    AssertContains(delta, "charging=0->1", "charging delta");
    AssertContains(delta, "reserves=1->2", "reserve delta");
}
```

If the harness does not already contain `AssertContains`, add:

```csharp
private static void AssertContains(string value, string expected, string label)
{
    if (value == null || !value.Contains(expected))
        throw new Exception(label + ": expected '" + value + "' to contain '" + expected + "'");
}
```

If the harness does not already contain `AssertEqual`, add:

```csharp
private static void AssertEqual<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception(label + ": expected " + expected + " got " + actual);
}
```

- [ ] **Step 4: Run tests and verify failure before implementation if tests were added first**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected before Task 2 implementation: compile fails because `WhiskeyRealism.Tactical` types do not exist. If Task 2 already created the files, expected result is pass.

## Task 4: Add Tactical Observer Patch

**Files:**

- Create: `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs`

- [ ] **Step 1: Create patch file with nested observers**

Create `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs`:

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Slice B0 observer. Reads vanilla AIBattle tactical state after selected
    // battle-AI methods run. This patch logs bounded telemetry only; it does not
    // alter macroai, ai_stance, movement, reserves, artillery, fallback, retreat,
    // or persistent strategic state.
    [HarmonyPatch]
    internal static class TacticalObserverPatch
    {
        private static readonly Dictionary<string, float> _lastEmittedAt = new Dictionary<string, float>();
        private static readonly Dictionary<int, TacticalObserverSnapshot> _chargeBefore = new Dictionary<int, TacticalObserverSnapshot>();
        private static TacticalObserverSnapshot _feudBefore = TacticalObserverSnapshot.Empty();

        private static FieldInfo _macroAiField;
        private static FieldInfo _sideOfAiField;
        private static FieldInfo _bunitsField;
        private static FieldInfo _unitsUsedField;
        private static FieldInfo _allGroupsAssignedField;
        private static FieldInfo _objectiveChainField;

        [HarmonyPatch(typeof(AIBattle), "CheckGlobalAIStrategy")]
        [HarmonyPostfix]
        internal static void CheckGlobalAIStrategyPostfix(AIBattle __instance)
        {
            Observe(__instance, TacticalObservedEvent.Macro, null, null);
        }

        [HarmonyPatch(typeof(AIBattle), "AdjustGroupAIStance")]
        [HarmonyPostfix]
        internal static void AdjustGroupAIStancePostfix(AIBattle __instance)
        {
            Observe(__instance, TacticalObservedEvent.Group, null, null);
            Observe(__instance, TacticalObservedEvent.Sector, null, null);
            Observe(__instance, TacticalObservedEvent.Order, null, null);
        }

        [HarmonyPatch(typeof(AIBattle), "MicroAICheckForCharges")]
        [HarmonyPrefix]
        internal static void MicroAICheckForChargesPrefix(AIBattle __instance, Regiment aigroup)
        {
            if (!Enabled()) return;
            try
            {
                int key = SafeInstanceId(aigroup);
                if (key != 0) _chargeBefore[key] = SnapshotGroup(aigroup);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-observer:charge-prefix", "Tactical charge observer Prefix failed: " + ex.Message);
            }
        }

        [HarmonyPatch(typeof(AIBattle), "MicroAICheckForCharges")]
        [HarmonyPostfix]
        internal static void MicroAICheckForChargesPostfix(AIBattle __instance, Regiment aigroup)
        {
            TacticalObserverSnapshot before = null;
            try
            {
                int key = SafeInstanceId(aigroup);
                if (key != 0 && _chargeBefore.TryGetValue(key, out before)) _chargeBefore.Remove(key);
            }
            catch { before = null; }
            Observe(__instance, TacticalObservedEvent.Charge, before, aigroup);
        }

        [HarmonyPatch(typeof(AIBattle), "CheckForFeudGroupActions")]
        [HarmonyPrefix]
        internal static void CheckForFeudGroupActionsPrefix(AIBattle __instance)
        {
            if (!Enabled()) return;
            try
            {
                _feudBefore = SnapshotBattle(__instance);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-observer:feud-prefix", "Tactical feud observer Prefix failed: " + ex.Message);
            }
        }

        [HarmonyPatch(typeof(AIBattle), "CheckForFeudGroupActions")]
        [HarmonyPostfix]
        internal static void CheckForFeudGroupActionsPostfix(AIBattle __instance)
        {
            Observe(__instance, TacticalObservedEvent.Feud, _feudBefore, null);
        }

        [HarmonyPatch(typeof(AIBattle), "CheckUseOfReserves")]
        [HarmonyPostfix]
        internal static void CheckUseOfReservesPostfix(AIBattle __instance)
        {
            Observe(__instance, TacticalObservedEvent.Reserve, null, null);
        }

        [HarmonyPatch(typeof(AIBattle), "LinkReservesToLineGroup")]
        [HarmonyPostfix]
        internal static void LinkReservesToLineGroupPostfix(AIBattle __instance)
        {
            Observe(__instance, TacticalObservedEvent.Reserve, null, null);
        }

        [HarmonyPatch(typeof(AIBattle), "AssignReserves")]
        [HarmonyPostfix]
        internal static void AssignReservesPostfix(AIBattle __instance)
        {
            Observe(__instance, TacticalObservedEvent.Reserve, null, null);
        }

        [HarmonyPatch(typeof(AIBattle), "CheckAIBombardment")]
        [HarmonyPostfix]
        internal static void CheckAIBombardmentPostfix(AIBattle __instance)
        {
            Observe(__instance, TacticalObservedEvent.Artillery, null, null);
        }

        [HarmonyPatch(typeof(AIBattle), "CheckLineFallbacks")]
        [HarmonyPostfix]
        internal static void CheckLineFallbacksPostfix(AIBattle __instance)
        {
            Observe(__instance, TacticalObservedEvent.Fallback, null, null);
        }

        [HarmonyPatch(typeof(AIBattle), "MicroAICheckForRetreats")]
        [HarmonyPostfix]
        internal static void MicroAICheckForRetreatsPostfix(AIBattle __instance)
        {
            Observe(__instance, TacticalObservedEvent.Fallback, null, null);
        }

        private static void Observe(AIBattle battle, TacticalObservedEvent eventType, TacticalObserverSnapshot before, Regiment group)
        {
            if (!Enabled()) return;

            try
            {
                OnceLog.Info("tactical-observer", "TacticalObserverPatch wired");

                var context = BuildContext(battle, group);
                string signature = TacticalTelemetry.Signature(eventType, context);
                bool verbose = Plugin.Instance != null && Plugin.Instance.TacticalObserverVerboseLogging.Value;
                float minSeconds = Plugin.Instance != null ? Mathf.Max(1, Plugin.Instance.TacticalObserverMinSecondsBetweenSummaries.Value) : 30f;
                float now = Time.realtimeSinceStartup;
                string key = eventType.ToString();

                if (!TacticalTelemetry.ShouldEmit(_lastEmittedAt, key, signature, now, minSeconds, verbose))
                    return;

                string message = TacticalTelemetry.Summary(eventType, context);
                if (before != null)
                {
                    var after = group != null ? SnapshotGroup(group) : SnapshotBattle(battle);
                    message += " delta=" + TacticalTelemetry.Delta(before, after);
                }

                Plugin.Log.LogInfo(message);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-observer:" + eventType, "Tactical observer " + eventType + " failed: " + ex.Message);
            }
        }

        private static TacticalBattleContext BuildContext(AIBattle battle, Regiment group)
        {
            var context = TacticalBattleContext.Empty();
            if (battle == null) return context;

            int side = SafeIntField(battle, ref _sideOfAiField, "sideofai", -1);
            int macro = SafeIntField(battle, ref _macroAiField, "macroai", -99);
            var bunits = SafeField<BattleUnits>(battle, ref _bunitsField, "bunits");
            var unitsUsed = SafeList(battle, ref _unitsUsedField, "unitsused");
            var allGroups = SafeList(battle, ref _allGroupsAssignedField, "allgroupsassigned");
            var objectiveChain = SafeList(battle, ref _objectiveChainField, "objectivechain");

            context.Side = side;
            context.MacroAi = macro;
            context.Alliance = SafeAlliance(bunits, side);
            context.GroupCount = CountList(allGroups);
            context.ObjectiveChainCount = CountList(objectiveChain);
            context.SectorSource = context.ObjectiveChainCount > 0 ? TacticalSectorSource.ObjectiveChain : TacticalSectorSource.AngleSlice;
            context.SectorSignature = "chains=" + context.ObjectiveChainCount + ",groups=" + context.GroupCount;
            context.OrderSignature = BuildOrderSignature(unitsUsed);
            context.ForceBalance = SafeForceBalance(bunits, side);
            context.ReinforcementsWithin24Hours = SafeReinforcements(bunits, side);

            CountUnits(unitsUsed, context);
            if (group != null) MergeGroupCounts(group, context);

            return context;
        }

        private static TacticalObserverSnapshot SnapshotBattle(AIBattle battle)
        {
            var context = BuildContext(battle, null);
            return new TacticalObserverSnapshot
            {
                GroupCount = context.GroupCount,
                ChargingCount = context.ChargingCount,
                FeudGroupCount = context.FeudGroupCount,
                ReserveGroupCount = context.ReserveGroupCount,
                ArtilleryGroupCount = context.ArtilleryGroupCount,
                FallbackCount = context.FallbackCount,
                RetreatingCount = context.RetreatingCount,
                Signature = TacticalTelemetry.Signature(TacticalObservedEvent.Macro, context)
            };
        }

        private static TacticalObserverSnapshot SnapshotGroup(Regiment group)
        {
            var snapshot = TacticalObserverSnapshot.Empty();
            if (group == null || group.allattachedunits == null) return snapshot;

            snapshot.GroupCount = 1;
            for (int i = 0; i < group.allattachedunits.Length; i++)
            {
                var unit = group.allattachedunits[i];
                if (unit == null) continue;
                if (unit.movementmode == 3) snapshot.ChargingCount++;
                if (unit.movementmode == 2) snapshot.FallbackCount++;
                if (unit.movementmode == 5 || unit.movementmode == 6) snapshot.RetreatingCount++;
                if (unit.unittyp == 2) snapshot.ArtilleryGroupCount++;
            }

            snapshot.Signature = "g=" + SafeInstanceId(group) + "|c=" + snapshot.ChargingCount + "|f=" + snapshot.FallbackCount;
            return snapshot;
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                   Plugin.Instance.Enabled.Value &&
                   Plugin.Instance.EnableTacticalObserver.Value;
        }

        private static void CountUnits(IList units, TacticalBattleContext context)
        {
            if (units == null || context == null) return;
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i] as Regiment;
                if (unit == null) continue;
                CountUnit(unit, context);
            }
        }

        private static void MergeGroupCounts(Regiment group, TacticalBattleContext context)
        {
            if (group == null || group.allattachedunits == null || context == null) return;
            for (int i = 0; i < group.allattachedunits.Length; i++)
                CountUnit(group.allattachedunits[i], context);
        }

        private static void CountUnit(Regiment unit, TacticalBattleContext context)
        {
            if (unit == null || context == null) return;
            if (unit.movementmode == 3) context.ChargingCount++;
            if (unit.movementmode == 2) context.FallbackCount++;
            if (unit.movementmode == 5 || unit.movementmode == 6) context.RetreatingCount++;
            if (unit.ai_feudstance >= 0) context.FeudGroupCount++;
            if (unit.unittyp == 2) context.ArtilleryGroupCount++;
            if (unit.unittyp > 13 && unit.ai_stanceordered == 1) context.ReserveGroupCount++;
            if (unit.unitrange != null)
            {
                if (unit.unitrange.closestenemyunitfarreg != null) context.VisibleEnemyCount++;
                else if (unit.unitrange.closestenemyunit != null) context.VisibleEnemyCount++;
            }
        }

        private static string BuildOrderSignature(IList units)
        {
            if (units == null) return "-";
            int moving = 0;
            int waiting = 0;
            int interrupted = 0;
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i] as Regiment;
                if (unit == null) continue;
                if (unit.regimentpaths > 0) moving++;
                if (unit.pathinterrupted) interrupted++;
                if (unit.regimentpaths <= 0 && unit.movementmode == 0) waiting++;
            }

            return "moving=" + moving + ",waiting=" + waiting + ",interrupted=" + interrupted;
        }

        private static int SafeAlliance(BattleUnits bunits, int side)
        {
            try
            {
                if (bunits == null || bunits.alliance == null) return -1;
                if (side < 0 || side >= bunits.alliance.Length) return -1;
                return bunits.alliance[side];
            }
            catch { return -1; }
        }

        private static float SafeForceBalance(BattleUnits bunits, int side)
        {
            try
            {
                if (bunits == null || bunits.sideinformation == null) return 0f;
                if (side < 0 || side >= bunits.sideinformation.Length) return 0f;
                return bunits.sideinformation[side].forcebalance;
            }
            catch { return 0f; }
        }

        private static float SafeReinforcements(BattleUnits bunits, int side)
        {
            try
            {
                if (bunits == null || bunits.sideinformation == null) return 0f;
                if (side < 0 || side >= bunits.sideinformation.Length) return 0f;
                return bunits.sideinformation[side].reinforcementarrivalswithin24hrs;
            }
            catch { return 0f; }
        }

        private static int SafeIntField(object instance, ref FieldInfo cache, string name, int fallback)
        {
            try
            {
                if (instance == null) return fallback;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                if (cache == null) return fallback;
                return (int)cache.GetValue(instance);
            }
            catch { return fallback; }
        }

        private static T SafeField<T>(object instance, ref FieldInfo cache, string name) where T : class
        {
            try
            {
                if (instance == null) return null;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                return cache != null ? cache.GetValue(instance) as T : null;
            }
            catch { return null; }
        }

        private static IList SafeList(object instance, ref FieldInfo cache, string name)
        {
            try
            {
                if (instance == null) return null;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                return cache != null ? cache.GetValue(instance) as IList : null;
            }
            catch { return null; }
        }

        private static int CountList(IList list)
        {
            return list == null ? 0 : list.Count;
        }

        private static int SafeInstanceId(UnityEngine.Object obj)
        {
            try { return obj != null ? obj.GetInstanceID() : 0; }
            catch { return 0; }
        }
    }
}
```

- [ ] **Step 2: Verify patch remains read-only**

Search the new patch:

```bash
rg -n "SetMovementMode|SetWaypoint|ChangeStance|SetRetreatTimer|macroai\\s*=|ai_stance\\s*=|ai_stanceordered\\s*=|Add\\(|Remove\\(|Clear\\(" src/WhiskeyRealism/Patches/TacticalObserverPatch.cs
```

Expected:

- no movement/stance/retreat assignments;
- `Remove` may appear only for `_chargeBefore.Remove(key)`;
- no mutation of vanilla lists.

If the search finds a vanilla-state write, remove it from B0.

## Task 5: Wire Project Compile

**Files:**

- Modify: `src/WhiskeyRealism/WhiskeyRealism.csproj`

- [ ] **Step 1: Check whether explicit compile entries are used**

Run:

```bash
sed -n '1,220p' src/WhiskeyRealism/WhiskeyRealism.csproj
```

Expected current project uses SDK default compile globs for `src`, so no source compile entry is needed. If explicit includes were added by concurrent work, add the three new tactical/patch files consistently.

- [ ] **Step 2: Verify project file only changes if necessary**

Run:

```bash
git diff -- src/WhiskeyRealism/WhiskeyRealism.csproj
```

Expected if SDK globs remain active: no diff.

## Task 6: Run Console Verification

**Files:**

- Uses all files above.

- [ ] **Step 1: Run tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected:

- all existing tests pass;
- new tactical telemetry tests print `PASS tactical telemetry ...`;
- no compile errors from explicit test compile entries.

- [ ] **Step 2: Fix only B0-owned failures**

If tests fail:

- fix tactical model/telemetry code if the failure is in the new tests;
- fix explicit compile entries if the test project cannot see tactical files;
- do not edit unrelated campaign-governor or strategic files unless they are the actual compile blocker from concurrent work.

## Task 7: Build Plugin

**Files:**

- Uses all source files.

- [ ] **Step 1: Build**

Run:

```bash
./build.sh
```

Expected:

- restore succeeds;
- build succeeds;
- output includes `Built plugin: /home/onebodyamerica/Projects/whiskey-realism-mod/dist/WhiskeyRealism.dll`.

- [ ] **Step 2: Inspect warnings**

If warnings appear from `TacticalObserverPatch.cs`, fix them before deploy. If warnings come from unrelated dirty work, record them in the handoff and do not mask them.

## Task 8: Deploy And Hash Verify

**Files:**

- `dist/WhiskeyRealism.dll`
- `/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll`

- [ ] **Step 1: Deploy**

Run:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
```

Expected:

- command exits `0`.

If it fails with `Invalid argument`, the game is running and Windows has locked the DLL. Stop and ask the user to close the game.

- [ ] **Step 2: Verify timestamp, size, hash**

Run:

```bash
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected:

- timestamps are close;
- sizes match exactly;
- SHA-256 hashes match exactly.

Do not report B0 as ready for smoke until hashes match.

## Task 9: Runtime Smoke

**Files:**

- Runtime log: `/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log`
- Config: `/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/config/dev.kyle.whiskey-realism.cfg`

- [ ] **Step 1: Confirm config**

After first launch with the B0 build, confirm:

```bash
rg -n "Enable Tactical Observer|Tactical Observer Verbose Logging|Tactical Observer Min Seconds" "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/config/dev.kyle.whiskey-realism.cfg"
```

Expected:

```text
Enable Tactical Observer = true
Tactical Observer Verbose Logging = false
Tactical Observer Min Seconds Between Summaries = 30
```

Existing config values override C# defaults. If `Enable Tactical Observer = false`, set it true manually for smoke.

- [ ] **Step 2: Smoke scenario**

Ask the user to launch a fresh W&L land battle. Preferred scenario:

- W&L career/subordinate command;
- order delays on through Whiskey realism lock;
- normal speed until battle starts;
- let battle run until at least one AI tactical update cycle fires.

- [ ] **Step 3: Inspect log**

Run:

```bash
rg -n "once:tactical-observer|TacticalMacro|TacticalGroup|TacticalFeud|TacticalCharge|TacticalSector|TacticalOrder|TacticalReserve|TacticalArtillery|TacticalFallback|Exception|TargetInvocationException|ERROR|WARN" "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Expected:

- one `[once:tactical-observer] TacticalObserverPatch wired`;
- at least one `[TacticalMacro]`;
- at least one `[TacticalGroup]`;
- at least one `[TacticalSector]`;
- at least one `[TacticalOrder]`;
- at least one of `[TacticalCharge]` or `[TacticalFeud]` if W&L feud/charge paths run;
- no repeated `Tactical observer ... failed` warnings;
- no `TargetInvocationException` loop;
- log volume is bounded, not per-frame/per-unit spam.

- [ ] **Step 4: Classify missing families**

If a family is missing:

- `[TacticalCharge]` missing: acceptable only if no group entered charge-check path in smoke; record as needing a charge-heavy smoke.
- `[TacticalFeud]` missing: acceptable only if feud path did not run; record as needing W&L feud reproduction.
- `[TacticalReserve]` missing: acceptable only if reserve methods did not run; record as needing larger battle smoke.
- `[TacticalArtillery]` missing: acceptable only if no artillery bombardment path ran; record as needing artillery-heavy smoke.
- `[TacticalFallback]` missing: acceptable only if no fallback/retreat path ran; record as needing outnumbered smoke.

B1 cannot start until `[TacticalCharge]` and `[TacticalFeud]` are either observed or explicitly deferred by the user.

## Task 10: Documentation Closeout

**Files:**

- Modify: `docs/patch-catalog.md`
- Modify: `docs/handoff.md`
- Modify: `MEMORY.md`

- [ ] **Step 1: Patch catalog**

Add a catalog row after #26. Use the next stable ordinal unless maintainers decide observer-only tactical patches should be unnumbered. Recommended row:

```markdown
| 27 | `TacticalObserverPatch` | Postfix observers | `Patches/TacticalObserverPatch.cs` | `AIBattle.CheckGlobalAIStrategy` (6314), `AdjustGroupAIStance` (4221), `MicroAICheckForCharges` (4905), `CheckForFeudGroupActions` (4931), `CheckUseOfReserves` (6062), `LinkReservesToLineGroup` (6642), `AssignReserves` (7017), `CheckAIBombardment` (3869), `CheckLineFallbacks` (5118), `MicroAICheckForRetreats` (4817) | Slice B0 observer. Emits bounded read-only tactical telemetry for macro/group stance, charge/feud paths, sector/order signatures, reserves, artillery, fallback, and retreat. Does not alter vanilla battle behavior. |
```

- [ ] **Step 2: Handoff**

Update `docs/handoff.md`:

- active workstream says B0 observer implemented if smoke passed;
- "What just shipped" includes tests/build/deploy/hash and smoke result;
- "Next concrete action" says B1 W&L feud/charge guard plan is next only after B0 charge/feud evidence is adequate.

- [ ] **Step 3: MEMORY**

Update `MEMORY.md` only after smoke:

- Slice B B0 observer shipped;
- deployed hash lives in handoff, not memory;
- record whether charge/feud runtime evidence was observed or still pending.

## Task 11: Commit

**Files:**

- All B0 source/test/doc files.

- [ ] **Step 1: Review B0 diff**

Run:

```bash
git diff -- src/WhiskeyRealism/Plugin.cs src/WhiskeyRealism/Tactical src/WhiskeyRealism/Patches/TacticalObserverPatch.cs tests/WhiskeyRealism.Tests docs/patch-catalog.md docs/handoff.md MEMORY.md
```

Expected:

- only B0 observer changes in the listed files;
- no unrelated campaign-governor cleanup;
- no behavior writes in `TacticalObserverPatch.cs`.

- [ ] **Step 2: Diff hygiene**

Run:

```bash
git diff --check
```

Expected: no whitespace errors.

- [ ] **Step 3: Commit**

After tests, build, deploy, hash verify, smoke, and docs closeout:

```bash
git add src/WhiskeyRealism/Plugin.cs src/WhiskeyRealism/Tactical/TacticalBattleContext.cs src/WhiskeyRealism/Tactical/TacticalTelemetry.cs src/WhiskeyRealism/Patches/TacticalObserverPatch.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs docs/patch-catalog.md docs/handoff.md MEMORY.md
git commit -m "feat: add tactical b0 observer"
```

Expected: one focused B0 commit. Do not include unrelated dirty source files.

## Rollback

To disable without reverting code:

- set `Enable Tactical Observer = false` in `BepInEx/config/dev.kyle.whiskey-realism.cfg`;
- restart the game.

To revert code before commit:

- remove `TacticalObserverPatch.cs`;
- remove `TacticalBattleContext.cs`;
- remove `TacticalTelemetry.cs`;
- remove B0 config fields/binds from `Plugin.cs`;
- remove tactical compile entries and tests;
- remove B0 docs updates.

Do not use `git reset --hard` in this repo because unrelated work may be in flight.

## Acceptance Criteria

B0 is complete only when all are true:

- console harness passes;
- `./build.sh` passes;
- `git diff --check` passes;
- deployed DLL timestamp/size/hash matches `dist/WhiskeyRealism.dll`;
- runtime log has `[once:tactical-observer]`;
- runtime log has bounded `[TacticalMacro]`, `[TacticalGroup]`, `[TacticalSector]`, and `[TacticalOrder]` lines;
- runtime smoke either observes or explicitly records why `[TacticalCharge]`, `[TacticalFeud]`, `[TacticalReserve]`, `[TacticalArtillery]`, or `[TacticalFallback]` did not fire;
- no repeated tactical observer warnings/errors;
- docs record the deployed hash and smoke evidence;
- no tactical behavior changed.

## Next Slice Gate

After B0 ships:

- If `[TacticalCharge]` and `[TacticalFeud]` were observed in W&L runtime smoke, write `docs/superpowers/plans/2026-05-05-tactical-b1-wl-feud-charge-guard.md`.
- If they were not observed, run a focused W&L feud/charge reproduction smoke before writing B1 behavior code.
- Do not start B2/B3 before B0 telemetry proves sector/order extraction quality.
