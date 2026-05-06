# W&L Camp Realism Slice 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship W&L Camp Realism Slice 1 from `docs/superpowers/specs/archive/2026-05-05-wl-camp-realism-slice1-design.md`: correct vanilla short-camp undercrediting, make safe camp bonuses respond faster, and soften command-count dilution for Drill, Motivate, Recruitment, and Readiness.

**Architecture:** Put arithmetic in one pure strategic helper and keep all native/Harmony work in one isolated patch concern. The patch preserves vanilla camp save/history shape, companion history, diary/event thresholds, and existing station/action data. Responsive weighting applies to safe gameplay/UI payoff paths but is suppressed while native diary/event threshold methods run.

**Tech Stack:** C# netstandard2.1, BepInEx 5.4.x x64, HarmonyX. Pure arithmetic tests run in `tests/WhiskeyRealism.Tests`; native reflection and camp runtime are verified by build, deploy/hash, and W&L career smoke.

---

## Validated Systems Map

Subagents confirmed these native surfaces and constraints:

| System | Native anchor | Implementation decision |
|---|---:|---|
| Daily camp accounting | `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:172034` | Patch `Camp.EvaluateCampTime()` Prefix/Postfix; do not replace or skip vanilla. |
| Short-camp bug | `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:172085` | Replace only the last station `timehistory` entries after vanilla undercredits them. |
| Status caps | `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:172062` and `:172272` | Refresh `Camp.currentstatus` in Prefix, but skip if private `battlefieldsetupref` is missing. |
| Bonus math | `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:171473` | Patch `Camp.Station.GetCurrentBonus(bool)` for responsive safe stations only; preserve unavailable-station zero. |
| Diary/event thresholds | `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:171548` and `:183155` | Suppress responsive weighting while these methods run, preserving vanilla long-average thresholds. |
| Unit modifiers | `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:172522` | Patch `Camp.GetModifier(int, bool)` for station IDs `6/7/8/11` only when `dividebycommandedunits == true`. |
| Logistics | `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:114351` and `:114419` | Keep station `5` hard-excluded; supply polarity is a separate proof task. |
| Command count | `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:47890` | Clamp cached count `-1/0` to `1` before applying `pow(count, power)`. |

Current worktree warning: there may be unrelated/parallel changes in `AGENTS.md`, `MEMORY.md`, `docs/handoff.md`, `docs/superpowers/README.md`, `src/WhiskeyRealism/Patches/FinancialAIPatch.cs`, `src/WhiskeyRealism/Strategic/Fiscal/FiscalIntentLedger.cs`, and `docs/bug-fixes/`. Do not stage or revert them unless the user explicitly asks.

---

## File Map

Create:

- `src/WhiskeyRealism/Strategic/WlCampRealism.cs` — pure arithmetic and station inclusion/exclusion policy.
- `src/WhiskeyRealism/Patches/WlCampRealismPatch.cs` — all Harmony surfaces for this slice.

Modify:

- `src/WhiskeyRealism/Plugin.cs` — add bounded W&L Camp config entries.
- `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` — explicit compile include for the helper.
- `tests/WhiskeyRealism.Tests/Program.cs` — pure helper tests.
- `docs/patch-catalog.md` — add patch ordinal after implementation ships, `#29` because `#28` is already `EconomyAllianceDataGuardPatch`.
- `docs/handoff.md` — record shipped DLL hash and smoke boundary after deploy/hash/runtime check.

---

## Task 1: Add Pure Camp Arithmetic Helper And Tests

**Files:**
- Create: `src/WhiskeyRealism/Strategic/WlCampRealism.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add the test project include**

In `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, add near the other `Strategic` compile includes:

```xml
    <Compile Include="..\..\src\WhiskeyRealism\Strategic\WlCampRealism.cs" Link="WlCampRealism.cs" />
```

- [ ] **Step 2: Add failing test registrations**

In `tests/WhiskeyRealism.Tests/Program.cs`, add these entries to the `tests` array near the other W&L helper tests:

```csharp
("wl camp short camp credits normal rest", WlCampShortCampCreditsNormalRest),
("wl camp short camp credits wounded rest", WlCampShortCampCreditsWoundedRest),
("wl camp short camp credits preserve minimum proportions", WlCampShortCampCreditsPreserveMinimumProportions),
("wl camp short camp enough time no correction", WlCampShortCampEnoughTimeNoCorrection),
("wl camp short camp zero minimum no correction", WlCampShortCampZeroMinimumNoCorrection),
("wl camp responsive bonus weights recent included station", WlCampResponsiveBonusWeightsRecentIncludedStation),
("wl camp responsive bonus includes companion recent average", WlCampResponsiveBonusIncludesCompanionRecentAverage),
("wl camp responsive bonus excluded stations stay vanilla", WlCampResponsiveBonusExcludedStationsStayVanilla),
("wl camp responsive bonus use average false stays vanilla", WlCampResponsiveBonusUseAverageFalseStaysVanilla),
("wl camp unit divisor clamps invalid cached counts", WlCampUnitDivisorClampsInvalidCachedCounts),
("wl camp unit divisor default power softens four and nine units", WlCampUnitDivisorDefaultPowerSoftensFourAndNineUnits),
("wl camp unit modifier clamps negative to zero", WlCampUnitModifierClampsNegativeToZero),
("wl camp unit power one is vanilla equivalent", WlCampUnitPowerOneIsVanillaEquivalent),
("wl camp unit payoff excluded or undivided returns vanilla", WlCampUnitPayoffExcludedOrUndividedReturnsVanilla),
```

- [ ] **Step 3: Add failing test methods**

In `Program.cs`, add this helper near the existing assertion helpers:

```csharp
private static void AssertNear(float expected, float actual, float tolerance, string label)
{
    if (Math.Abs(expected - actual) > tolerance)
        throw new Exception(label + ": expected " + expected + " got " + actual);
}
```

Add these test methods near other W&L strategic helper tests:

```csharp
private static void WlCampShortCampCreditsNormalRest()
{
    var corrected = new float[1];
    float minimumTotal;
    bool changed = WlCampRealism.TryCorrectShortCampMinimumCredits(
        2f, new[] { 3f }, corrected, out minimumTotal);
    AssertTrue(changed, "expected correction for 2h actual below 3h minimum");
    AssertNear(3f, minimumTotal, 0.0001f, "minimum total");
    AssertNear(2f, corrected[0], 0.0001f, "rest credit");
}

private static void WlCampShortCampCreditsWoundedRest()
{
    var corrected = new float[1];
    float minimumTotal;
    bool changed = WlCampRealism.TryCorrectShortCampMinimumCredits(
        2f, new[] { 9f }, corrected, out minimumTotal);
    AssertTrue(changed, "expected correction for 2h actual below 9h wounded rest minimum");
    AssertNear(9f, minimumTotal, 0.0001f, "minimum total");
    AssertNear(2f, corrected[0], 0.0001f, "wounded rest credit");
}

private static void WlCampShortCampCreditsPreserveMinimumProportions()
{
    var corrected = new float[4];
    float minimumTotal;
    bool changed = WlCampRealism.TryCorrectShortCampMinimumCredits(
        3f, new[] { 3f, 1f, 2f, 0f }, corrected, out minimumTotal);
    AssertTrue(changed, "expected correction for 3h actual below 6h minimum");
    AssertNear(6f, minimumTotal, 0.0001f, "minimum total");
    AssertNear(1.5f, corrected[0], 0.0001f, "station 0");
    AssertNear(0.5f, corrected[1], 0.0001f, "station 1");
    AssertNear(1.0f, corrected[2], 0.0001f, "station 2");
    AssertNear(0f, corrected[3], 0.0001f, "station 3");
    AssertNear(3f, corrected[0] + corrected[1] + corrected[2] + corrected[3], 0.0001f, "sum");
}

private static void WlCampShortCampEnoughTimeNoCorrection()
{
    var corrected = new[] { -99f };
    float minimumTotal;
    bool changed = WlCampRealism.TryCorrectShortCampMinimumCredits(
        3f, new[] { 3f }, corrected, out minimumTotal);
    AssertTrue(!changed, "expected no correction when actual covers minimum");
    AssertNear(3f, minimumTotal, 0.0001f, "minimum total");
    AssertNear(-99f, corrected[0], 0.0001f, "sentinel unchanged");
}

private static void WlCampShortCampZeroMinimumNoCorrection()
{
    var corrected = new[] { -99f, -88f };
    float minimumTotal;
    bool changed = WlCampRealism.TryCorrectShortCampMinimumCredits(
        2f, new[] { 0f, 0f }, corrected, out minimumTotal);
    AssertTrue(!changed, "expected no correction when minimum total is zero");
    AssertNear(0f, minimumTotal, 0.0001f, "minimum total");
    AssertNear(-99f, corrected[0], 0.0001f, "sentinel 0 unchanged");
    AssertNear(-88f, corrected[1], 0.0001f, "sentinel 1 unchanged");
}

private static void WlCampResponsiveBonusWeightsRecentIncludedStation()
{
    float vanilla = (56f / 30f - 3f) / 5f;
    float result = WlCampRealism.ComputeResponsiveBonus(
        6, true, vanilla, 56f / 30f, 0f,
        new[] { 8f, 8f, 8f, 8f, 8f, 8f, 8f },
        new float[0],
        3f, 8f, 7, 0.35f);
    AssertTrue(result > vanilla, "responsive bonus should exceed long-average vanilla");
    AssertNear(0.202666f, result, 0.0005f, "responsive bonus");
}

private static void WlCampResponsiveBonusIncludesCompanionRecentAverage()
{
    float vanilla = 0f;
    float result = WlCampRealism.ComputeResponsiveBonus(
        1, true, vanilla, 2f, 0f,
        new[] { 2f, 2f, 2f, 2f, 2f, 2f, 2f },
        new[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f },
        2f, 6f, 7, 0.35f);
    AssertTrue(result > vanilla, "recent companion time should lift bonus");
    AssertNear(0.0875f, result, 0.0005f, "companion responsive bonus");
}

private static void WlCampResponsiveBonusExcludedStationsStayVanilla()
{
    foreach (var stationId in new[] { 2, 5, 9, 12 })
    {
        float result = WlCampRealism.ComputeResponsiveBonus(
            stationId, true, 0.42f, 0f, 0f,
            new[] { 99f, 99f, 99f, 99f, 99f, 99f, 99f },
            new[] { 99f, 99f, 99f, 99f, 99f, 99f, 99f },
            0f, 1f, 7, 0.35f);
        AssertNear(0.42f, result, 0.0001f, "excluded station " + stationId);
    }
}

private static void WlCampResponsiveBonusUseAverageFalseStaysVanilla()
{
    float result = WlCampRealism.ComputeResponsiveBonus(
        6, false, -0.25f, 0f, 0f,
        new[] { 99f, 99f, 99f, 99f, 99f, 99f, 99f },
        new float[0],
        0f, 1f, 7, 0.35f);
    AssertNear(-0.25f, result, 0.0001f, "useaverage=false should stay vanilla");
}

private static void WlCampUnitDivisorClampsInvalidCachedCounts()
{
    AssertNear(1f, WlCampRealism.EffectiveCommandedUnitDivisor(-1, 0.5f), 0.0001f, "count -1");
    AssertNear(1f, WlCampRealism.EffectiveCommandedUnitDivisor(0, 0.5f), 0.0001f, "count 0");
    AssertNear(1f, WlCampRealism.EffectiveCommandedUnitDivisor(1, 0.5f), 0.0001f, "count 1");
}

private static void WlCampUnitDivisorDefaultPowerSoftensFourAndNineUnits()
{
    AssertNear(2f, WlCampRealism.EffectiveCommandedUnitDivisor(4, 0.5f), 0.0001f, "count 4 divisor");
    AssertNear(3f, WlCampRealism.EffectiveCommandedUnitDivisor(9, 0.5f), 0.0001f, "count 9 divisor");
    AssertNear(1.5f, WlCampRealism.ComputeUnitPayoffModifier(6, true, 1.25f, 1f, 1f, 4, 0.5f), 0.0001f, "count 4 modifier");
    AssertNear(1.333333f, WlCampRealism.ComputeUnitPayoffModifier(6, true, 1.111f, 1f, 1f, 9, 0.5f), 0.0005f, "count 9 modifier");
}

private static void WlCampUnitModifierClampsNegativeToZero()
{
    float result = WlCampRealism.ComputeUnitPayoffModifier(7, true, 1f, -1f, 1000f, 1, 0.5f);
    AssertNear(0f, result, 0.0001f, "negative modifier clamp");
}

private static void WlCampUnitPowerOneIsVanillaEquivalent()
{
    AssertNear(9f, WlCampRealism.EffectiveCommandedUnitDivisor(9, 1.0f), 0.0001f, "power one divisor");
    float result = WlCampRealism.ComputeUnitPayoffModifier(8, true, 0f, 1f, 1f, 9, 1.0f);
    AssertNear(1.111111f, result, 0.0005f, "power one modifier");
}

private static void WlCampUnitPayoffExcludedOrUndividedReturnsVanilla()
{
    AssertNear(0.77f, WlCampRealism.ComputeUnitPayoffModifier(5, true, 0.77f, 1f, 1f, 9, 0.5f), 0.0001f, "station 5 excluded");
    AssertNear(0.77f, WlCampRealism.ComputeUnitPayoffModifier(9, true, 0.77f, 1f, 1f, 9, 0.5f), 0.0001f, "station 9 excluded");
    AssertNear(0.77f, WlCampRealism.ComputeUnitPayoffModifier(12, true, 0.77f, 1f, 1f, 9, 0.5f), 0.0001f, "station 12 excluded");
    AssertNear(0.77f, WlCampRealism.ComputeUnitPayoffModifier(6, false, 0.77f, 1f, 1f, 9, 0.5f), 0.0001f, "undivided included station");
}
```

- [ ] **Step 4: Run the harness and confirm it fails**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile failure because `WlCampRealism` does not exist.

- [ ] **Step 5: Add the pure helper**

Create `src/WhiskeyRealism/Strategic/WlCampRealism.cs`:

```csharp
using System;

namespace WhiskeyRealism.Strategic
{
    internal static class WlCampRealism
    {
        public static bool TryCorrectShortCampMinimumCredits(
            float actualCampHours,
            float[] stationMinimumHours,
            float[] correctedCredits,
            out float minimumTotal)
        {
            minimumTotal = 0f;
            if (stationMinimumHours == null || correctedCredits == null) return false;
            if (correctedCredits.Length < stationMinimumHours.Length) return false;

            for (int i = 0; i < stationMinimumHours.Length; i++)
                if (stationMinimumHours[i] > 0f)
                    minimumTotal += stationMinimumHours[i];

            if (minimumTotal <= 0f || actualCampHours >= minimumTotal) return false;

            float ratio = Math.Max(0f, actualCampHours) / minimumTotal;
            for (int i = 0; i < stationMinimumHours.Length; i++)
                correctedCredits[i] = stationMinimumHours[i] > 0f ? stationMinimumHours[i] * ratio : 0f;

            return true;
        }

        public static bool UsesResponsiveBonusWeighting(int stationId)
        {
            switch (stationId)
            {
                case 0:
                case 1:
                case 3:
                case 4:
                case 6:
                case 7:
                case 8:
                case 10:
                case 11:
                    return true;
                default:
                    return false;
            }
        }

        public static float ComputeResponsiveBonus(
            int stationId,
            bool useAverage,
            float vanillaBonus,
            float longStationAverage,
            float longCompanionAverage,
            float[] stationHistory,
            float[] companionHistory,
            float minTimeBonus,
            float maxTimeBonus,
            int recentWindowDays,
            float recentWeight)
        {
            if (!useAverage || !UsesResponsiveBonusWeighting(stationId)) return vanillaBonus;

            int window = ClampInt(recentWindowDays, 3, 14);
            float weight = Clamp(recentWeight, 0f, 0.5f);
            float recentStation = RecentAverage(stationHistory, window);
            float recentCompanion = RecentAverage(companionHistory, window);
            float stationHours = longStationAverage * (1f - weight) + recentStation * weight;
            float companionHours = longCompanionAverage * (1f - weight) + recentCompanion * weight;

            return ComputeBonus(stationHours, companionHours, minTimeBonus, maxTimeBonus);
        }

        public static bool UsesUnitPayoffTuning(int stationId, bool divideByCommandedUnits)
        {
            if (!divideByCommandedUnits) return false;
            return stationId == 6 || stationId == 7 || stationId == 8 || stationId == 11;
        }

        public static float EffectiveCommandedUnitDivisor(int commandedUnitCount, float divisorPower)
        {
            int count = Math.Max(1, commandedUnitCount);
            float power = Clamp(divisorPower, 0.5f, 1.0f);
            return Math.Max(1f, (float)Math.Pow(count, power));
        }

        public static float ComputeUnitPayoffModifier(
            int stationId,
            bool divideByCommandedUnits,
            float vanillaModifier,
            float bonus,
            float maxBonusMalus,
            int commandedUnitCount,
            float divisorPower)
        {
            if (!UsesUnitPayoffTuning(stationId, divideByCommandedUnits)) return vanillaModifier;

            float divisor = EffectiveCommandedUnitDivisor(commandedUnitCount, divisorPower);
            float modifier = 1f + bonus * maxBonusMalus / divisor;
            return modifier < 0f ? 0f : modifier;
        }

        private static float ComputeBonus(float stationHours, float companionHours, float minTimeBonus, float maxTimeBonus)
        {
            float denominator = Math.Max(0.001f, maxTimeBonus - minTimeBonus);
            float bonus = (stationHours + companionHours - minTimeBonus) / denominator;
            return Clamp(bonus, -1f, 1f);
        }

        private static float RecentAverage(float[] history, int window)
        {
            if (history == null || history.Length == 0) return 0f;
            int count = Math.Min(window, history.Length);
            float sum = 0f;
            for (int i = history.Length - count; i < history.Length; i++)
                sum += history[i];
            return sum / Math.Max(1, window);
        }

        private static float Clamp(float value, float min, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
```

- [ ] **Step 6: Run the harness and build**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected: harness passes; build succeeds.

- [ ] **Step 7: Commit helper and tests**

```bash
git add src/WhiskeyRealism/Strategic/WlCampRealism.cs tests/WhiskeyRealism.Tests/Program.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "feat: add W&L camp realism math helper"
```

---

## Task 2: Add W&L Camp Config Entries

**Files:**
- Modify: `src/WhiskeyRealism/Plugin.cs`

- [ ] **Step 1: Add config fields**

In `Plugin.cs`, add these fields after the campaign governor fields:

```csharp
internal ConfigEntry<bool> EnableWlCampAccountingFix;
internal ConfigEntry<bool> EnableWlCampResponsiveBonusWeighting;
internal ConfigEntry<int> WlCampRecentBonusWindowDays;
internal ConfigEntry<float> WlCampRecentBonusWeight;
internal ConfigEntry<bool> EnableWlCampUnitPayoffTuning;
internal ConfigEntry<float> WlCampUnitEffectDivisorPower;
internal ConfigEntry<bool> EnableWlCampVerboseTrace;
```

- [ ] **Step 2: Bind config entries**

In `Awake()`, after the campaign governor config binds and before vanilla-settings binds, add:

```csharp
EnableWlCampAccountingFix = Config.Bind(
    "W&L Camp", "Enable W&L Camp Accounting Fix", true,
    "Default ON. Corrects vanilla short-camp minimum allocation so credited station time sums to actual camp time.");
EnableWlCampResponsiveBonusWeighting = Config.Bind(
    "W&L Camp", "Enable W&L Camp Responsive Bonus Weighting", true,
    "Default ON. Blends safe camp stations with recent station history so allocation payoff is less delayed. Diary/event thresholds remain vanilla long-average.");
WlCampRecentBonusWindowDays = Config.Bind(
    "W&L Camp", "W&L Camp Recent Bonus Window Days", 7,
    new ConfigDescription(
        "Recent station-history window used for responsive camp bonus weighting.",
        new AcceptableValueRange<int>(3, 14)));
WlCampRecentBonusWeight = Config.Bind(
    "W&L Camp", "W&L Camp Recent Bonus Weight", 0.35f,
    new ConfigDescription(
        "Blend weight for recent camp history. 0 disables responsiveness; 0.5 is the maximum Slice 1 weighting.",
        new AcceptableValueRange<float>(0f, 0.5f)));
EnableWlCampUnitPayoffTuning = Config.Bind(
    "W&L Camp", "Enable W&L Camp Unit Payoff Tuning", true,
    "Default ON. Softens command-count dilution for Drill, Motivate, Recruitment, and Readiness camp modifiers.");
WlCampUnitEffectDivisorPower = Config.Bind(
    "W&L Camp", "W&L Camp Unit Effect Divisor Power", 0.5f,
    new ConfigDescription(
        "Power applied to commanded-unit count for unit-facing camp effects. 0.5 uses square-root scaling; 1.0 is vanilla-equivalent.",
        new AcceptableValueRange<float>(0.5f, 1.0f)));
EnableWlCampVerboseTrace = Config.Bind(
    "W&L Camp", "Enable W&L Camp Verbose Trace", false,
    "Emit bounded W&L camp accounting and modifier trace lines for focused smoke tests.");
```

- [ ] **Step 3: Build**

```bash
./build.sh
```

Expected: build succeeds. If `ConfigDescription` or `AcceptableValueRange` names fail, confirm `using BepInEx.Configuration;` remains at the top of `Plugin.cs`.

- [ ] **Step 4: Commit config**

```bash
git add src/WhiskeyRealism/Plugin.cs
git commit -m "feat: add W&L camp realism config gates"
```

---

## Task 3: Add Harmony Patch For Camp Accounting And Payoff

**Files:**
- Create: `src/WhiskeyRealism/Patches/WlCampRealismPatch.cs`

- [ ] **Step 1: Create the patch file**

Create `src/WhiskeyRealism/Patches/WlCampRealismPatch.cs` with this structure:

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla Camp.EvaluateCampTime() converts observed W&L camp time into
    // station histories; Camp.Station.GetCurrentBonus() turns those histories
    // into station payoff; Camp.GetModifier() applies several unit-facing
    // station effects. This patch corrects short-camp minimum undercrediting,
    // adds bounded responsive weighting for safe stations, and softens command
    // count dilution for the unit-facing stations only.
    [HarmonyPatch]
    internal static class WlCampRealismPatch
    {
        private static readonly FieldInfo CampTimeHistoryField = AccessTools.Field(typeof(Camp), "camptimehistory");
        private static readonly FieldInfo BattlefieldSetupRefField = AccessTools.Field(typeof(Camp), "battlefieldsetupref");
        private static int _vanillaThresholdDepth;
        private static string _lastCorrectionSignature;
        private static string _lastModifierSignature;
        private static string _lastBonusSignature;

        [HarmonyPatch(typeof(Camp), "EvaluateCampTime")]
        internal static class EvaluateCampTimePatch
        {
            [HarmonyPrefix]
            internal static void Prefix()
            {
                TryRefreshCurrentStatus();
            }

            [HarmonyPostfix]
            internal static void Postfix()
            {
                TryCorrectShortCampHistory();
            }
        }

        [HarmonyPatch(typeof(Camp.Station), "GetCurrentBonus")]
        internal static class StationBonusPatch
        {
            [HarmonyPostfix]
            internal static void Postfix(Camp.Station __instance, bool useaverage, ref float __result)
            {
                TryApplyResponsiveBonus(__instance, useaverage, ref __result);
            }
        }

        [HarmonyPatch(typeof(Camp), "GetModifier")]
        internal static class ModifierPatch
        {
            [HarmonyPostfix]
            internal static void Postfix(int stationid, bool dividebycommandedunits, ref float __result)
            {
                TryApplyUnitPayoffTuning(stationid, dividebycommandedunits, ref __result);
            }
        }

        [HarmonyPatch(typeof(Camp.Station), "CheckEventTriggers")]
        internal static class CampEventThresholdScopePatch
        {
            [HarmonyPrefix]
            internal static void Prefix()
            {
                _vanillaThresholdDepth++;
            }

            [HarmonyFinalizer]
            internal static Exception Finalizer(Exception __exception)
            {
                if (_vanillaThresholdDepth > 0) _vanillaThresholdDepth--;
                return __exception;
            }
        }

        [HarmonyPatch(typeof(Diary), "UpdateEvents")]
        internal static class DiaryThresholdScopePatch
        {
            [HarmonyPrefix]
            internal static void Prefix()
            {
                _vanillaThresholdDepth++;
            }

            [HarmonyFinalizer]
            internal static Exception Finalizer(Exception __exception)
            {
                if (_vanillaThresholdDepth > 0) _vanillaThresholdDepth--;
                return __exception;
            }
        }

        private static void TryRefreshCurrentStatus()
        {
            try
            {
                if (!AccountingEnabled()) return;
                if (Camp.stations == null) return;
                if (BattlefieldSetupRefField == null || BattlefieldSetupRefField.GetValue(null) == null) return;
                Camp.currentstatus = Camp.PlayerUnitStatus();
                OnceLog.Info("wl-camp-realism", "[W&LCamp] camp realism patch active");
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-camp-realism:status", "[W&LCamp] status refresh failed: " + ex.Message);
            }
        }

        private static void TryCorrectShortCampHistory()
        {
            try
            {
                if (!AccountingEnabled()) return;
                if (Camp.stations == null || Camp.stations.Count == 0) return;
                var campHistory = CampTimeHistoryField?.GetValue(null) as List<float>;
                if (campHistory == null || campHistory.Count == 0) return;

                float actual = campHistory[campHistory.Count - 1];
                var minimums = new float[Camp.stations.Count];
                for (int i = 0; i < Camp.stations.Count; i++)
                    minimums[i] = Camp.stations[i] != null ? Camp.stations[i].GetMinTime() : 0f;

                var corrected = new float[minimums.Length];
                float minimumTotal;
                if (!WlCampRealism.TryCorrectShortCampMinimumCredits(actual, minimums, corrected, out minimumTotal)) return;

                for (int i = 0; i < Camp.stations.Count; i++)
                {
                    var station = Camp.stations[i];
                    if (station == null || station.timehistory == null || station.timehistory.Count == 0) continue;
                    int last = station.timehistory.Count - 1;
                    float old = station.timehistory[last];
                    station.timehistory[last] = corrected[i];
                    TraceCorrection(i, actual, minimumTotal, old, corrected[i]);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-camp-realism:correct", "[W&LCamp] short-camp correction failed: " + ex.Message);
            }
        }

        private static void TryApplyResponsiveBonus(Camp.Station station, bool useAverage, ref float result)
        {
            try
            {
                if (!ResponsiveEnabled()) return;
                if (_vanillaThresholdDepth > 0) return;
                if (!useAverage || station == null || Camp.stations == null) return;
                int stationId = Camp.stations.IndexOf(station);
                if (stationId < 0) return;
                if (!WlCampRealism.UsesResponsiveBonusWeighting(stationId)) return;
                if (!Camp.IsCampStationAvailable(station)) return;

                float old = result;
                result = WlCampRealism.ComputeResponsiveBonus(
                    stationId,
                    useAverage,
                    result,
                    station.averagetimespent,
                    station.companionaveragetimespent,
                    ToArray(station.timehistory),
                    ToArray(station.companiontimehistory),
                    station.GetMinTimeBonus(),
                    station.GetMaxTimeBonus(),
                    Plugin.Instance.WlCampRecentBonusWindowDays.Value,
                    Plugin.Instance.WlCampRecentBonusWeight.Value);
                TraceBonus(stationId, old, result);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-camp-realism:bonus", "[W&LCamp] responsive bonus failed: " + ex.Message);
            }
        }

        private static void TryApplyUnitPayoffTuning(int stationId, bool divideByCommandedUnits, ref float result)
        {
            try
            {
                if (!UnitPayoffEnabled()) return;
                if (!WlCampRealism.UsesUnitPayoffTuning(stationId, divideByCommandedUnits)) return;
                if (!DLC_WL.dlc_scenarioactive || Camp.stations == null) return;
                if (stationId < 0 || stationId >= Camp.stations.Count) return;
                var station = Camp.stations[stationId];
                if (station == null) return;

                int commanded = DLC_WL.GetNumberOfCommandedUnits();
                float old = result;
                float bonus = station.GetCurrentBonus();
                result = WlCampRealism.ComputeUnitPayoffModifier(
                    stationId,
                    divideByCommandedUnits,
                    result,
                    bonus,
                    station.maxbonusmalus,
                    commanded,
                    Plugin.Instance.WlCampUnitEffectDivisorPower.Value);
                TraceModifier(stationId, commanded, old, result);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-camp-realism:modifier", "[W&LCamp] unit payoff tuning failed: " + ex.Message);
            }
        }

        private static bool AccountingEnabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableWlCampAccountingFix != null &&
                Plugin.Instance.EnableWlCampAccountingFix.Value;
        }

        private static bool ResponsiveEnabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableWlCampResponsiveBonusWeighting != null &&
                Plugin.Instance.EnableWlCampResponsiveBonusWeighting.Value;
        }

        private static bool UnitPayoffEnabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableWlCampUnitPayoffTuning != null &&
                Plugin.Instance.EnableWlCampUnitPayoffTuning.Value;
        }

        private static float[] ToArray(List<float> values)
        {
            return values == null ? new float[0] : values.ToArray();
        }

        private static void TraceCorrection(int stationId, float actual, float minimumTotal, float oldCredit, float newCredit)
        {
            if (!VerboseTrace()) return;
            string sig = stationId + ":" + actual.ToString("0.00") + ":" + oldCredit.ToString("0.00") + ":" + newCredit.ToString("0.00");
            if (_lastCorrectionSignature == sig) return;
            _lastCorrectionSignature = sig;
            Plugin.Log.LogInfo($"[W&LCamp] station={stationId} actual={actual:F2} minimumTotal={minimumTotal:F2} vanillaCredit={oldCredit:F2} correctedCredit={newCredit:F2}");
        }

        private static void TraceBonus(int stationId, float oldBonus, float newBonus)
        {
            if (!VerboseTrace()) return;
            if (Math.Abs(oldBonus - newBonus) < 0.01f) return;
            string sig = stationId + ":" + oldBonus.ToString("0.00") + ":" + newBonus.ToString("0.00");
            if (_lastBonusSignature == sig) return;
            _lastBonusSignature = sig;
            Plugin.Log.LogInfo($"[W&LCamp] station={stationId} vanillaBonus={oldBonus:F2} responsiveBonus={newBonus:F2}");
        }

        private static void TraceModifier(int stationId, int commanded, float oldModifier, float newModifier)
        {
            if (!VerboseTrace()) return;
            if (Math.Abs(oldModifier - newModifier) < 0.01f) return;
            string sig = stationId + ":" + commanded + ":" + oldModifier.ToString("0.00") + ":" + newModifier.ToString("0.00");
            if (_lastModifierSignature == sig) return;
            _lastModifierSignature = sig;
            Plugin.Log.LogInfo($"[W&LCamp] station={stationId} commanded={commanded} vanillaModifier={oldModifier:F2} tunedModifier={newModifier:F2}");
        }

        private static bool VerboseTrace()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.EnableWlCampVerboseTrace != null &&
                Plugin.Instance.EnableWlCampVerboseTrace.Value;
        }
    }
}
```

- [ ] **Step 2: Build**

```bash
./build.sh
```

Expected: build succeeds. If Harmony cannot resolve a private method through the attribute, replace the affected nested class attribute with `[HarmonyPatch]` plus:

```csharp
private static MethodBase TargetMethod()
{
    return AccessTools.Method(typeof(Camp), "EvaluateCampTime");
}
```

- [ ] **Step 3: Run pure tests**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: full harness passes.

- [ ] **Step 4: Commit patch**

```bash
git add src/WhiskeyRealism/Patches/WlCampRealismPatch.cs
git commit -m "fix: correct W&L camp accounting and payoff tuning"
```

---

## Task 4: Update Living Docs And Patch Catalog

**Files:**
- Modify: `docs/patch-catalog.md`
- Modify: `docs/handoff.md`

- [ ] **Step 1: Add patch catalog row**

In `docs/patch-catalog.md`, add the next free ordinal after `#27`:

```markdown
| 29 | `WlCampRealismPatch` | Prefix/Postfix/Finalizer | `Patches/WlCampRealismPatch.cs` | `Camp.EvaluateCampTime` (172034), `Camp.Station.GetCurrentBonus` (171473), `Camp.GetModifier` (172522), `Camp.Station.CheckEventTriggers` (171534), `Diary.UpdateEvents` (183155) | W&L camp Slice 1. Corrects vanilla short-camp minimum allocation by replacing only the last station-history credit entries, refreshes camp status before siege/field caps when safe, adds responsive bonus weighting for safe station/UI/payoff paths while preserving diary/event thresholds, and softens command-count dilution for Drill/Motivate/Recruitment/Readiness only. |
```

If another parallel branch has already claimed `#29`, use the next free ordinal and preserve ordinal stability.

- [ ] **Step 2: Update handoff after verification**

After build/deploy/hash/runtime smoke, update `docs/handoff.md` "At a glance" and/or the current workstream notes with a sentence that states: W&L Camp Realism Slice 1 shipped on 2026-05-05; `WlCampRealismPatch` corrects short-camp minimum undercrediting, preserves diary/event threshold semantics, adds safe responsive camp bonus weighting, and softens command-count dilution for unit-facing camp effects; the latest deployed DLL SHA-256 is the exact matching hash printed by `sha256sum` in Task 5 Step 4.

Do not write a deployed SHA until `sha256sum` proves `dist/WhiskeyRealism.dll` and deployed `WhiskeyRealism.dll` match.

- [ ] **Step 3: Commit docs**

```bash
git add docs/patch-catalog.md docs/handoff.md
git commit -m "docs: catalog W&L camp realism patch"
```

---

## Task 5: Full Verification, Deploy, And Runtime Smoke

**Files:**
- No planned source edits.
- Runtime evidence: game install log/config files.

- [ ] **Step 1: Run pure tests**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: full harness passes.

- [ ] **Step 2: Build DLL**

```bash
./build.sh
```

Expected: `dist/WhiskeyRealism.dll` exists and build exits 0.

- [ ] **Step 3: Deploy DLL**

Close Grand Tactician first, then run:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
```

If this fails with `Invalid argument`, the game is still running and Windows is holding the DLL lock. Close the game and retry.

- [ ] **Step 4: Verify deployed DLL matches dist**

```bash
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: both SHA-256 hashes match exactly.

- [ ] **Step 5: Runtime smoke**

Start a W&L career and check:

- `BepInEx/LogOutput.log` contains `[once:wl-camp-realism]` or `[W&LCamp] camp realism patch active`.
- No repeated `[W&LCamp] ... failed` warnings.
- With `Enable W&L Camp Verbose Trace = true`, a short-camp day logs corrected credits that sum to actual camp hours.
- Drill, Motivate, Recruitment, and Readiness still use W&L under-command paths without exceptions.
- Station `5` logistics remains excluded; no tuning trace appears for station `5`.
- Diary/event camp threshold behavior remains long-average vanilla; no new immediate threshold spam appears after a short allocation change.

- [ ] **Step 6: Final commit if smoke docs changed**

```bash
git add docs/handoff.md
git commit -m "docs: record W&L camp realism smoke"
```

---

## Rollback / Defer Boundaries

- If `Camp.EvaluateCampTime` patching is brittle, keep Task 1 helper/tests and disable only `Enable W&L Camp Accounting Fix` while reassessing the method target.
- If responsive bonus weighting affects diary/event thresholds despite scope guards, turn off `Enable W&L Camp Responsive Bonus Weighting` and ship only accounting fix + unit payoff tuning.
- If unit payoff tuning is too strong in runtime smoke, set `W&L Camp Unit Effect Divisor Power = 1.0` for vanilla-equivalent dilution while preserving the code path.
- Do not touch station `5` logistics in this slice. Open a separate proof/telemetry spec for the suspected supply polarity issue.
- Do not edit installed `Config/camp.dat`, `Config/actions.dat`, or `Config/dlcwl_config.dat`.

---

## Plan Self-Review

- Spec coverage: short-camp correction is Task 1/3; status refresh is Task 3; responsive safe-station weighting and diary/event suppression are Task 1/3; unit payoff tuning is Task 1/3; config gates are Task 2; docs and verification are Task 4/5.
- Native anchor coverage: plan includes every confirmed camp anchor and the downstream call-site boundary for diary/event suppression.
- Placeholder scan: no unresolved plan markers or vague implementation steps remain.
- Type consistency: helper name is `WlCampRealism`; patch name is `WlCampRealismPatch`; config fields use `EnableWlCamp...` / `WlCamp...` consistently.
