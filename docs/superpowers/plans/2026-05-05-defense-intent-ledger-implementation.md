# Defense Intent Ledger Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the DefenseIntentLedger slice per `docs/superpowers/specs/2026-05-05-defense-intent-ledger-design.md` — a daily strategic-defense ledger that scores coastal/river assets, detects sea/raid/proximity threats, selects proportional response packages with locality-and-escalation discipline, and steers vanilla `CheckForDefensiveOperations` via three patch surfaces (candidate-filter Prefix, Postfix re-issue, custom defensive movement order). Coexists with shipped #4 capital-defense pattern.

**Architecture:** Three layers, all daily, all signature-skipping. (1) Pure ledger types (`DefenseIntentLedger`, `DefensePackageAggregator`, `DefenseCooldownTable`, `DefenseThreatSignature`) computed from synthetic inputs; (2) `DefenseIntentRuntime` extracts vanilla state via reflection wrappers and feeds the pure builder; (3) Three Harmony surfaces enforce the ledger's verdict — targeted candidate-filter Prefix on `AICampaign.CheckForDefensiveOperations`, paired Postfix re-issue that reverts forbidden cross-map pulls, and a `StrategicCoordinator`-owned custom defensive movement order runner for landings vanilla rate-limited away. Cadence shifts globally from weekly to daily; existing ledgers already signature-skip on unchanged input, so daily firing does not amplify cost outside the new ledger's compute.

**Tech Stack:** BepInEx 5.4.x x64 + HarmonyX 2.10.x, C# netstandard2.1, Unity 2021.3 Mono x64. Hand-rolled console test harness at `tests/WhiskeyRealism.Tests/Program.cs` using `AssertEqual<T>` / `AssertTrue` helpers. Reflection via `HarmonyLib.AccessTools`.

**Build / deploy / verify** (run after every DLL-affecting task):

```bash
./build.sh
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

The two SHA-256 hashes must match before requesting smoke-test. If `cp` fails with `Invalid argument`, the game is running — close GTCW and redeploy.

**Test command** (run after every Phase A-C task and any task that touches pure code):

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expect `OK` exit code 0; any failure prints the assertion message.

---

## Phase A — Cadence prerequisites (Tasks 1-3)

The spec moves the entire strategic review cadence from weekly to daily. Existing operational ledgers (front sectors, army areas, formation directives, fiscal, construction) already signature-skip on unchanged input, so daily firing only amplifies the *enumeration* cost, not the *recompute* cost. This phase swaps the cadence and adjusts the heartbeat label.

### Task 1: Add `DailyCadence` (TDD)

**Files:**
- Create: `src/WhiskeyRealism/Strategic/DailyCadence.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs` (append two tests + register)

- [ ] **Step 1: Write the failing tests**

Append two test methods after the existing `WeeklyCadenceFiresOnFirstSeenWeekAndRollover` method in `Program.cs`:

```csharp
private static void DailyCadenceFiresOnFirstCallAndDayRolloverOnly()
{
    var cadence = new DailyCadence();
    AssertTrue(cadence.ShouldFire(1, 6, 1861), "first call should fire");
    AssertTrue(!cadence.ShouldFire(1, 6, 1861), "same day should not fire again");
    AssertTrue(cadence.ShouldFire(2, 6, 1861), "next day should fire");
    AssertTrue(cadence.ShouldFire(1, 7, 1861), "month rollover should fire");
    AssertTrue(cadence.ShouldFire(1, 1, 1862), "year rollover should fire");
}

private static void DailyCadenceRejectsInvalidDates()
{
    var cadence = new DailyCadence();
    AssertTrue(!cadence.ShouldFire(0, 6, 1861), "day 0 should be ignored");
    AssertTrue(!cadence.ShouldFire(1, 0, 1861), "month 0 should be ignored");
    AssertTrue(!cadence.ShouldFire(1, 6, 0), "year 0 should be ignored");
}
```

Register the tests in the tuple list at the top of `Main()` near the existing `weekly cadence` line:

```csharp
("daily cadence fires on first call and day rollover only", DailyCadenceFiresOnFirstCallAndDayRolloverOnly),
("daily cadence rejects invalid dates", DailyCadenceRejectsInvalidDates),
```

- [ ] **Step 2: Run tests, expect compile failure**

Run: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
Expected: build fails with `The type or namespace name 'DailyCadence' could not be found`.

- [ ] **Step 3: Create `DailyCadence`**

Write `src/WhiskeyRealism/Strategic/DailyCadence.cs`:

```csharp
namespace WhiskeyRealism.Strategic
{
    public sealed class DailyCadence
    {
        private int _lastDay = -1;
        private int _lastMonth = -1;
        private int _lastYear = -1;

        public bool ShouldFire(int day, int month, int year)
        {
            if (day <= 0 || month <= 0 || year <= 0) return false;
            bool first = _lastDay < 0;
            bool rollover = !first && (day != _lastDay || month != _lastMonth || year != _lastYear);
            if (!first && !rollover) return false;
            _lastDay = day;
            _lastMonth = month;
            _lastYear = year;
            return true;
        }
    }
}
```

- [ ] **Step 4: Run tests, expect pass**

Run: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
Expected: all tests including the two new ones print `OK`, exit code 0.

- [ ] **Step 5: Commit**

```bash
git add src/WhiskeyRealism/Strategic/DailyCadence.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: add DailyCadence for daily strategic review"
```

### Task 2: Switch `StrategicCoordinator` from weekly to daily cadence

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs` — replace `_operationalCadence` field, rename `OnWeeklyOperationalTick` → `OnDailyOperationalTick`, change `[WeeklyOps]` log label

- [ ] **Step 1: Replace cadence field + tick method**

In `StrategicCoordinator.cs`, change the field declaration (currently around line 41) from:

```csharp
private readonly WeeklyCadence _operationalCadence = new WeeklyCadence();
```

to:

```csharp
private readonly DailyCadence _operationalCadence = new DailyCadence();
```

Then in `NotifyDateAdvanced(int gameDay, int gameMonth, int gameYear)`, change the cadence-fire branch (currently around line 127) from:

```csharp
if (_operationalCadence.ShouldFire(gameDay, gameMonth, gameYear) && !ranMonthly)
    OnWeeklyOperationalTick(gameDay, gameMonth, gameYear);
```

to:

```csharp
if (_operationalCadence.ShouldFire(gameDay, gameMonth, gameYear) && !ranMonthly)
    OnDailyOperationalTick(gameDay, gameMonth, gameYear);
```

Rename the method itself from `OnWeeklyOperationalTick` to `OnDailyOperationalTick` (currently around line 133):

```csharp
public void OnDailyOperationalTick(int day, int month, int year)
{
    try
    {
        OnceLog.Info("dailyops", "Daily operational analysis active");
        RunStrategicReview(day, month, year, logHeartbeat: false);
    }
    catch (Exception ex)
    {
        Plugin.Log.LogWarning("[DailyOps] tick failed: " + ex.Message);
    }
}
```

In `RunStrategicReview`, replace the verbose-only `[WeeklyOps]` line (currently around line 251) with:

```csharp
if (Plugin.Instance.VerboseLogging.Value && !logHeartbeat)
    Plugin.Log.LogInfo($"[DailyOps] {year}-{month:D2}-{day:D2} alliance={alliance}");
```

- [ ] **Step 2: Delete the now-unused `WeeklyCadence`**

```bash
git rm src/WhiskeyRealism/Strategic/WeeklyCadence.cs
```

- [ ] **Step 3: Remove the `weekly cadence` test from `Program.cs`**

In `tests/WhiskeyRealism.Tests/Program.cs`, delete the registration line:

```csharp
("weekly cadence fires on first seen week and week rollover only", WeeklyCadenceFiresOnFirstSeenWeekAndRollover),
```

and the corresponding `private static void WeeklyCadenceFiresOnFirstSeenWeekAndRollover()` method body.

- [ ] **Step 4: Build + test**

```bash
./build.sh
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: 0 warnings, 0 errors. All tests pass. The `daily cadence ...` tests added in Task 1 are now exercised by both `Program.cs` and (transitively) `StrategicCoordinator`.

- [ ] **Step 5: Deploy + verify**

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: matching SHA-256.

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Strategic/StrategicCoordinator.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: switch operational tick to daily cadence"
```

### Task 3: Smoke-confirm daily cadence at 1x and 5x

**Files:** None (smoke only)

- [ ] **Step 1: Ask user to launch GTCW**

Ask the user: "Launch GTCW, start a fresh W&L (002) campaign, advance 5 in-game days at 1x, then 5 at 5x. Then stop and tail `BepInEx/LogOutput.log`. Look for `[once:dailyops]` once, multiple `[FrontLedger]` / `[ArmyArea]` / `[FormationDirective]` / `[FiscalIntent]` / `[ConstructionIntent]` lines only on signature change (not on every day), and zero new warnings or errors."

- [ ] **Step 2: Inspect log**

Read: `/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log`
Expected: one `[once:dailyops] Daily operational analysis active`. Existing ledger lines appear at most once per signature change. No new error/warning lines.

- [ ] **Step 3: If perf regresses or spam appears, fix the regressing ledger before continuing**

Diagnose: which ledger logs every day? That ledger is missing a signature-skip — patch it (the runtime should compute the current signature, compare against the cached signature in `StrategicCoordinator`, and only log on change).

- [ ] **Step 4: No code change → no commit. If a fix landed, commit it under `fix: signature-skip <ledger> for daily cadence`.**

---

## Phase B — Asset metadata extension (Tasks 4-7)

The spec adds `AssetStrategicRole` flags so doctrine can distinguish Wilmington from Galveston, Cairo from Beaufort. Two layered mechanisms: derived weights from `GrandStrategyProfile` (default) + hand-coded catalog overrides (named anchors).

### Task 4: Add `AssetStrategicRole` flags + extend ledger types (TDD)

**Files:**
- Create: `src/WhiskeyRealism/Strategic/AssetStrategicRole.cs`
- Modify: `src/WhiskeyRealism/Strategic/CampaignMapLedger.cs` — add `StrategicRole` field on `CampaignMapTown` and `CampaignMapAsset`; copy in `CopyTown`/`CopyAsset`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs` — append flag-composition test

- [ ] **Step 1: Write the failing test**

Append to `Program.cs` and register:

```csharp
("asset strategic role flags compose additively", AssetStrategicRoleFlagsComposeAdditively),
```

```csharp
private static void AssetStrategicRoleFlagsComposeAdditively()
{
    var role = AssetStrategicRole.BlockadeRunnerPort | AssetStrategicRole.KeyFort;
    AssertTrue((role & AssetStrategicRole.BlockadeRunnerPort) != 0, "blockade flag missing");
    AssertTrue((role & AssetStrategicRole.KeyFort) != 0, "key-fort flag missing");
    AssertTrue((role & AssetStrategicRole.RearSafePort) == 0, "unset flag should not appear");
    AssertEqual(AssetStrategicRole.None, default(AssetStrategicRole));
}
```

- [ ] **Step 2: Run tests, expect compile failure**

Expected: `The type or namespace name 'AssetStrategicRole' could not be found`.

- [ ] **Step 3: Create `AssetStrategicRole.cs`**

Write `src/WhiskeyRealism/Strategic/AssetStrategicRole.cs`:

```csharp
using System;

namespace WhiskeyRealism.Strategic
{
    [Flags]
    public enum AssetStrategicRole
    {
        None                  = 0,
        BlockadeRunnerPort    = 1 << 0,
        UnionForwardBase      = 1 << 1,
        RiverControlHub       = 1 << 2,
        CapitalApproach       = 1 << 3,
        KeyFort               = 1 << 4,
        SupplyEscapeOnlyPort  = 1 << 5,
        RearSafePort          = 1 << 6
    }
}
```

- [ ] **Step 4: Extend `CampaignMapTown` and `CampaignMapAsset` with `StrategicRole`**

In `src/WhiskeyRealism/Strategic/CampaignMapLedger.cs`, add the field to both classes (after the existing `Theater` field on each):

```csharp
public AssetStrategicRole StrategicRole = AssetStrategicRole.None;
```

In `CopyTown`, append after the `IncomeTax = source.IncomeTax,` line:

```csharp
StrategicRole = source.StrategicRole
```

In `CopyAsset`, append after the `Capacity = source.Capacity` line:

```csharp
,
StrategicRole = source.StrategicRole
```

(Comma placement: `Capacity` is currently the last property in `CopyAsset`, so it has no trailing comma — add one when adding `StrategicRole`.)

- [ ] **Step 5: Run tests, expect pass + zero build warnings**

Run: `./build.sh && dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
Expected: 0 warnings, 0 errors. New test passes.

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Strategic/AssetStrategicRole.cs src/WhiskeyRealism/Strategic/CampaignMapLedger.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: add AssetStrategicRole flags on map ledger"
```

### Task 5: Add `AssetRoleScorer` for `GrandStrategyProfile`-derived weights (TDD)

**Files:**
- Create: `src/WhiskeyRealism/Strategic/AssetRoleScorer.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Write failing tests**

Append to `Program.cs` and register:

```csharp
("asset role scorer flags csa blockade port from profile", AssetRoleScorerFlagsCsaBlockadePortFromProfile),
("asset role scorer flags union river hub from profile", AssetRoleScorerFlagsUnionRiverHubFromProfile),
("asset role scorer leaves unknown asset alone", AssetRoleScorerLeavesUnknownAssetAlone),
```

```csharp
private static void AssetRoleScorerFlagsCsaBlockadePortFromProfile()
{
    var profile = GrandStrategyRegistry.GetProfile(allianceId: 1, era: EraStage.Amateur1861);
    var asset = new CampaignMapAsset
    {
        Kind = CampaignMapAssetKind.SeaHarbor,
        Name = "wilmington-harbor",
        StateAbbrev = "NC",
        Theater = Theater.Coast,
        Owner = 1,
        Capacity = 4f,
        Level = 2
    };
    var role = AssetRoleScorer.Score(asset, profile, capitalDistance: 250f, frontDistance: 80f);
    AssertTrue((role & AssetStrategicRole.BlockadeRunnerPort) != 0, "csa NC sea port should score blockade-runner");
}

private static void AssetRoleScorerFlagsUnionRiverHubFromProfile()
{
    var profile = GrandStrategyRegistry.GetProfile(allianceId: 0, era: EraStage.Amateur1861);
    var asset = new CampaignMapAsset
    {
        Kind = CampaignMapAssetKind.RiverHarbor,
        Name = "cairo-harbor",
        StateAbbrev = "IL",
        Theater = Theater.River,
        Owner = 0,
        Capacity = 2f
    };
    var role = AssetRoleScorer.Score(asset, profile, capitalDistance: 800f, frontDistance: 60f);
    AssertTrue((role & AssetStrategicRole.RiverControlHub) != 0, "union river hub should score river-control");
}

private static void AssetRoleScorerLeavesUnknownAssetAlone()
{
    var profile = GrandStrategyRegistry.GetProfile(allianceId: 0, era: EraStage.Amateur1861);
    var asset = new CampaignMapAsset
    {
        Kind = CampaignMapAssetKind.SeaHarbor,
        Name = "unmapped-port",
        StateAbbrev = "??",
        Theater = Theater.Unknown
    };
    var role = AssetRoleScorer.Score(asset, profile, capitalDistance: 9999f, frontDistance: 9999f);
    AssertEqual(AssetStrategicRole.None, role);
}
```

- [ ] **Step 2: Run tests, expect compile failure on `AssetRoleScorer`**

- [ ] **Step 3: Create `AssetRoleScorer.cs`**

Write `src/WhiskeyRealism/Strategic/AssetRoleScorer.cs`:

```csharp
namespace WhiskeyRealism.Strategic
{
    public static class AssetRoleScorer
    {
        public static AssetStrategicRole Score(
            CampaignMapAsset asset,
            GrandStrategyProfile profile,
            float capitalDistance,
            float frontDistance)
        {
            if (asset == null || profile == null) return AssetStrategicRole.None;
            var role = AssetStrategicRole.None;

            if (asset.Kind == CampaignMapAssetKind.SeaHarbor)
            {
                if (profile.AllianceId == 1 && profile.HasTag(StrategyTag.BlockadeRunning))
                    role |= AssetStrategicRole.BlockadeRunnerPort;
                if (profile.AllianceId == 0 && profile.HasTag(StrategyTag.CoastalInterdiction)
                    && asset.Owner == profile.AllianceId && frontDistance < 200f)
                    role |= AssetStrategicRole.UnionForwardBase;
            }

            if (asset.Kind == CampaignMapAssetKind.RiverHarbor)
            {
                if (profile.HasTag(StrategyTag.RiverControl))
                    role |= AssetStrategicRole.RiverControlHub;
            }

            if (asset.Kind == CampaignMapAssetKind.Fort && asset.Level >= 2)
                role |= AssetStrategicRole.KeyFort;

            if (capitalDistance < 120f)
                role |= AssetStrategicRole.CapitalApproach;

            return role;
        }

        public static AssetStrategicRole ScoreTown(
            CampaignMapTown town,
            GrandStrategyProfile profile,
            float capitalDistance)
        {
            if (town == null || profile == null) return AssetStrategicRole.None;
            var role = AssetStrategicRole.None;
            if (capitalDistance < 120f)
                role |= AssetStrategicRole.CapitalApproach;
            return role;
        }
    }
}
```

If `GrandStrategyProfile` does not yet expose `AllianceId` or `HasTag(StrategyTag)`, add them as minimal additions in this task — they are read-only properties on the profile already populated by `GrandStrategyRegistry.GetProfile`. Verify by grepping: `grep -n "class GrandStrategyProfile" src/WhiskeyRealism/Strategic/GrandStrategyProfile.cs`.

- [ ] **Step 4: Run tests, expect pass**

- [ ] **Step 5: Commit**

```bash
git add src/WhiskeyRealism/Strategic/AssetRoleScorer.cs src/WhiskeyRealism/Strategic/GrandStrategyProfile.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: derive AssetStrategicRole from GrandStrategyProfile"
```

### Task 6: Add `AssetRoleCatalog` hand-coded overrides (TDD)

**Files:**
- Create: `src/WhiskeyRealism/Strategic/AssetRoleCatalog.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Write failing tests**

Append to `Program.cs` and register:

```csharp
("asset role catalog overrides scorer for named anchor", AssetRoleCatalogOverridesScorer),
("asset role catalog returns none for unknown name", AssetRoleCatalogReturnsNoneForUnknown),
```

```csharp
private static void AssetRoleCatalogOverridesScorer()
{
    var role = AssetRoleCatalog.Lookup("wilmington-harbor");
    AssertTrue((role & AssetStrategicRole.BlockadeRunnerPort) != 0, "wilmington should be blockade-runner");
    AssertTrue((role & AssetStrategicRole.KeyFort) == 0, "wilmington should not be flagged key-fort by name alone");

    var norfolk = AssetRoleCatalog.Lookup("norfolk-harbor");
    AssertTrue((norfolk & AssetStrategicRole.CapitalApproach) != 0, "norfolk should be capital approach");
}

private static void AssetRoleCatalogReturnsNoneForUnknown()
{
    AssertEqual(AssetStrategicRole.None, AssetRoleCatalog.Lookup("unmapped-port"));
    AssertEqual(AssetStrategicRole.None, AssetRoleCatalog.Lookup(null));
    AssertEqual(AssetStrategicRole.None, AssetRoleCatalog.Lookup(""));
}
```

- [ ] **Step 2: Run tests, expect compile failure**

- [ ] **Step 3: Create `AssetRoleCatalog.cs`**

Write `src/WhiskeyRealism/Strategic/AssetRoleCatalog.cs`. Catalog keys are lowercase asset names matched by case-insensitive equality against `CampaignMapAsset.Name` / `CampaignMapTown.CityName`. Spec lists 15 named anchors:

```csharp
using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public static class AssetRoleCatalog
    {
        private static readonly Dictionary<string, AssetStrategicRole> _entries =
            new Dictionary<string, AssetStrategicRole>(StringComparer.OrdinalIgnoreCase)
        {
            // CSA blockade-runner ports
            { "wilmington-harbor", AssetStrategicRole.BlockadeRunnerPort },
            { "charleston-harbor", AssetStrategicRole.BlockadeRunnerPort | AssetStrategicRole.KeyFort },
            { "mobile-harbor",     AssetStrategicRole.BlockadeRunnerPort | AssetStrategicRole.KeyFort },
            { "galveston-harbor",  AssetStrategicRole.BlockadeRunnerPort | AssetStrategicRole.SupplyEscapeOnlyPort },
            { "sabine-pass",       AssetStrategicRole.SupplyEscapeOnlyPort },

            // CSA capital approach
            { "norfolk-harbor",    AssetStrategicRole.CapitalApproach | AssetStrategicRole.UnionForwardBase },
            { "hampton-roads",     AssetStrategicRole.CapitalApproach | AssetStrategicRole.UnionForwardBase },

            // Union forward bases / coastal
            { "beaufort-harbor",   AssetStrategicRole.UnionForwardBase },
            { "annapolis-harbor",  AssetStrategicRole.CapitalApproach },

            // River control hubs
            { "new-orleans-harbor", AssetStrategicRole.RiverControlHub | AssetStrategicRole.BlockadeRunnerPort },
            { "vicksburg-harbor",   AssetStrategicRole.RiverControlHub | AssetStrategicRole.KeyFort },
            { "memphis-harbor",     AssetStrategicRole.RiverControlHub },
            { "st-louis-harbor",    AssetStrategicRole.RiverControlHub },
            { "baton-rouge-harbor", AssetStrategicRole.RiverControlHub },
            { "cairo-harbor",       AssetStrategicRole.RiverControlHub | AssetStrategicRole.UnionForwardBase },
        };

        public static AssetStrategicRole Lookup(string name)
        {
            if (string.IsNullOrEmpty(name)) return AssetStrategicRole.None;
            return _entries.TryGetValue(name, out var role) ? role : AssetStrategicRole.None;
        }
    }
}
```

The exact in-game asset names may differ from the keys above. The catalog's job is to be wrong-by-name rather than crashing — `Lookup` returning `None` is the correct fallback. Real names will be confirmed during Phase D smoke-testing; the catalog is a starting set, expected to be refined.

- [ ] **Step 4: Run tests, expect pass**

- [ ] **Step 5: Commit**

```bash
git add src/WhiskeyRealism/Strategic/AssetRoleCatalog.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: add AssetRoleCatalog with 15 named anchors"
```

### Task 7: Wire role classification into `CampaignMapRuntime` build

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/CampaignMapRuntime.cs`
- Modify: `src/WhiskeyRealism/Strategic/CampaignMapLedger.cs` — `Build` invokes role classification per asset

- [ ] **Step 1: Add classification step in `CampaignMapLedger.Build(towns, assets)`**

Inside `Build(towns, assets)`, after the existing `town.Theater = TheaterClassifier...` block (around line 105), add:

```csharp
town.StrategicRole = AssetRoleCatalog.Lookup(town.CityName);
```

Inside the same `Build`, after each asset's `asset.Theater = ...` resolution (around line 127), add:

```csharp
var catalogRole = AssetRoleCatalog.Lookup(asset.Name);
asset.StrategicRole = catalogRole;
```

The `GrandStrategyProfile`-derived `AssetRoleScorer` step is intentionally **not** invoked here — `CampaignMapLedger.Build` doesn't know which alliance is asking. The scorer runs at *consumption* time inside `DefenseIntentRuntime` (Task 13), so each alliance can see roles weighted by its own profile.

- [ ] **Step 2: Add a one-time miss log inside `CampaignMapRuntime.Build`**

In `CampaignMapRuntime.Build()`, after the `var ledger = CampaignMapLedger.Build(towns, assets);` line (currently the last executable line of the method), insert:

```csharp
foreach (var asset in ledger.Assets)
{
    if (asset.StrategicRole == AssetStrategicRole.None && !string.IsNullOrEmpty(asset.Name))
        OnceLog.Info("defense-intent:asset-no-role:" + asset.Name,
            $"[DefenseIntent:asset] missing-role asset={asset.Name} kind={asset.Kind}");
}
```

This logs each unknown asset name **exactly once per session** so the engineer can refine the catalog from real GTCW asset names without log spam. Catalog updates land in Task 6's file.

- [ ] **Step 3: Build + test**

```bash
./build.sh
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: 0 warnings, 0 errors. All Task 4-6 tests still pass.

- [ ] **Step 4: Deploy + verify SHA**

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

- [ ] **Step 5: Smoke for asset-name capture**

Ask the user: "Launch GTCW, start fresh W&L 002, advance one game-day. Stop. Send me the `[DefenseIntent:asset] missing-role` lines from `BepInEx/LogOutput.log`."

Expected: a list of unmapped asset names (e.g., the actual prefix or suffix vanilla uses). Use this to refine `AssetRoleCatalog` keys in a follow-up commit. **Do not block this task on a complete catalog** — the missing-role log is the deliberate refinement loop.

- [ ] **Step 6: Commit (whatever lands in this round)**

```bash
git add src/WhiskeyRealism/Strategic/CampaignMapLedger.cs src/WhiskeyRealism/Strategic/CampaignMapRuntime.cs
git commit -m "feat: classify campaign-map assets with strategic roles"
```

---

## Phase C — Defense ledger pure types (Tasks 8-12)

This phase builds the pure ledger surface — types, signature recipe, package aggregator, cooldown table, and the ledger builder itself — all unit-tested via the console harness, no Unity dependencies.

### Task 8: Add `DefensePosture`, `ThreatScale`, and threat/response POCOs (TDD)

**Files:**
- Create: `src/WhiskeyRealism/Strategic/DefensePosture.cs`
- Create: `src/WhiskeyRealism/Strategic/DefenseIntentTypes.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Write failing tests**

Append + register:

```csharp
("defense posture defaults to not-evaluated", DefensePostureDefaultsToNotEvaluated),
("defense threat carries signature and posture", DefenseThreatCarriesSignatureAndPosture),
```

```csharp
private static void DefensePostureDefaultsToNotEvaluated()
{
    AssertEqual(DefensePosture.NotEvaluated, default(DefensePosture));
    AssertEqual(ThreatScale.None, default(ThreatScale));
}

private static void DefenseThreatCarriesSignatureAndPosture()
{
    var threat = new DefenseThreat
    {
        Signature = "sif:#1234:Norfolk:Hampton",
        Posture = DefensePosture.ActiveInvasion,
        Scale = ThreatScale.Landing,
        AssetName = "norfolk-harbor",
        EnemyStrength = 4200f,
        DesiredStrength = 6500f,
        EscalationReason = "landed-port-threat"
    };
    AssertEqual("sif:#1234:Norfolk:Hampton", threat.Signature);
    AssertEqual(DefensePosture.ActiveInvasion, threat.Posture);
    AssertEqual(ThreatScale.Landing, threat.Scale);
}
```

- [ ] **Step 2: Run tests, expect compile failure**

- [ ] **Step 3: Create the enums + POCOs**

Write `src/WhiskeyRealism/Strategic/DefensePosture.cs`:

```csharp
namespace WhiskeyRealism.Strategic
{
    public enum DefensePosture
    {
        NotEvaluated = 0,
        CoastalGuard,
        InvasionWatch,
        ActiveInvasion,
        ContainAndCounterattack,
        Recovered
    }

    public enum ThreatScale
    {
        None = 0,
        Raid,
        Landing,
        MajorLanding,
        DecisiveLanding
    }

    public enum CandidateTier
    {
        Local = 1,
        SameTheater = 2,
        AdjacentTheater = 3,
        CrossMap = 4
    }
}
```

Write `src/WhiskeyRealism/Strategic/DefenseIntentTypes.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace WhiskeyRealism.Strategic
{
    public sealed class DefenseThreat
    {
        public string Signature;
        public DefensePosture Posture;
        public ThreatScale Scale;
        public string AssetName;
        public Vector3 Position;
        public float EnemyStrength;
        public float DesiredStrength;
        public CandidateTier ResponseRadius = CandidateTier.Local;
        public string EscalationReason;
    }

    public sealed class DefenseCandidate
    {
        public int UnitInstanceId;
        public string UnitName;
        public Vector3 Position;
        public float ActiveStrength;
        public float Morale;
        public float ReadinessStep;
        public Theater Theater;
        public CandidateTier Tier;
        public bool InOffensiveOperation;
        public bool PlayerControlled;
        public bool CriticalFront;
        public float DistanceToThreat;
        public float Score;
        public float EffectiveStrength;
    }

    public sealed class DefenseSuppression
    {
        public int UnitInstanceId;
        public string Reason;
    }

    public sealed class DefenseResponse
    {
        public DefenseThreat Threat;
        public List<DefenseCandidate> SelectedPackage = new List<DefenseCandidate>();
        public List<DefenseSuppression> Suppressed = new List<DefenseSuppression>();
        public bool Adequate;
        public bool Understrength;
        public string TelemetrySignature;
    }

    public sealed class DefenseIntentLedgerOutput
    {
        public int AllianceId;
        public List<DefenseResponse> Responses = new List<DefenseResponse>();
        public string Signature;
    }
}
```

- [ ] **Step 4: Run tests, expect pass**

- [ ] **Step 5: Commit**

```bash
git add src/WhiskeyRealism/Strategic/DefensePosture.cs src/WhiskeyRealism/Strategic/DefenseIntentTypes.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: add defense posture and intent POCO types"
```

### Task 9: Add `DefenseThreatSignature` recipe (TDD)

**Files:**
- Create: `src/WhiskeyRealism/Strategic/DefenseThreatSignature.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Write failing tests**

Append + register:

```csharp
("threat signature for sif uses instance and spot", ThreatSignatureForSifUsesInstanceAndSpot),
("threat signature for raid uses instance and asset", ThreatSignatureForRaidUsesInstanceAndAsset),
("threat signature for asset uses sorted top-n enemies", ThreatSignatureForAssetUsesSortedTopN),
("threat signature is stable across reordered enemies", ThreatSignatureIsStableAcrossReorderedEnemies),
```

```csharp
private static void ThreatSignatureForSifUsesInstanceAndSpot()
{
    var sig = DefenseThreatSignature.ForSeaInvasion(
        invasionForceInstanceId: 42, spotName: "Hampton", sourcePortName: "Boston");
    AssertEqual("sif:42:Hampton:Boston", sig);

    var nullSpot = DefenseThreatSignature.ForSeaInvasion(
        invasionForceInstanceId: 42, spotName: null, sourcePortName: null);
    AssertEqual("sif:42:<no-spot>:<no-port>", nullSpot);
}

private static void ThreatSignatureForRaidUsesInstanceAndAsset()
{
    var sig = DefenseThreatSignature.ForRaid(raidGroupInstanceId: 7, nearestAssetName: "wilmington-harbor");
    AssertEqual("raid:7:wilmington-harbor", sig);
}

private static void ThreatSignatureForAssetUsesSortedTopN()
{
    var sig = DefenseThreatSignature.ForAsset(
        assetKind: CampaignMapAssetKind.SeaHarbor,
        assetName: "vicksburg-harbor",
        enemyInstanceIds: new[] { 9, 3, 5, 1, 7, 11 },
        topN: 3);
    AssertEqual("asset:SeaHarbor:vicksburg-harbor:1,3,5", sig);
}

private static void ThreatSignatureIsStableAcrossReorderedEnemies()
{
    var a = DefenseThreatSignature.ForAsset(
        CampaignMapAssetKind.RiverHarbor, "memphis-harbor", new[] { 5, 3, 1 }, topN: 5);
    var b = DefenseThreatSignature.ForAsset(
        CampaignMapAssetKind.RiverHarbor, "memphis-harbor", new[] { 1, 5, 3 }, topN: 5);
    AssertEqual(a, b);
}
```

- [ ] **Step 2: Run tests, expect compile failure**

- [ ] **Step 3: Create `DefenseThreatSignature.cs`**

```csharp
using System;
using System.Linq;

namespace WhiskeyRealism.Strategic
{
    public static class DefenseThreatSignature
    {
        public static string ForSeaInvasion(int invasionForceInstanceId, string spotName, string sourcePortName)
        {
            string spot = string.IsNullOrEmpty(spotName) ? "<no-spot>" : spotName;
            string port = string.IsNullOrEmpty(sourcePortName) ? "<no-port>" : sourcePortName;
            return $"sif:{invasionForceInstanceId}:{spot}:{port}";
        }

        public static string ForRaid(int raidGroupInstanceId, string nearestAssetName)
        {
            string asset = string.IsNullOrEmpty(nearestAssetName) ? "<no-asset>" : nearestAssetName;
            return $"raid:{raidGroupInstanceId}:{asset}";
        }

        public static string ForAsset(
            CampaignMapAssetKind assetKind, string assetName, int[] enemyInstanceIds, int topN)
        {
            string name = string.IsNullOrEmpty(assetName) ? "<no-asset>" : assetName;
            string ids = enemyInstanceIds == null || enemyInstanceIds.Length == 0
                ? "<no-enemies>"
                : string.Join(",", enemyInstanceIds.OrderBy(x => x).Take(Math.Max(1, topN)));
            return $"asset:{assetKind}:{name}:{ids}";
        }
    }
}
```

- [ ] **Step 4: Run tests, expect pass**

- [ ] **Step 5: Commit**

```bash
git add src/WhiskeyRealism/Strategic/DefenseThreatSignature.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: add stable threat signature recipe"
```

### Task 10: Add `DefensePackageAggregator` greedy multi-unit selector (TDD)

**Files:**
- Create: `src/WhiskeyRealism/Strategic/DefensePackageAggregator.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Write failing tests**

Append + register:

```csharp
("package aggregator picks smaller adequate over remote oversized", PackageAggregatorPicksSmallerAdequateOverRemoteOversized),
("package aggregator stops at overshoot guard", PackageAggregatorStopsAtOvershootGuard),
("package aggregator emits understrength flag", PackageAggregatorEmitsUnderstrengthFlag),
("package aggregator suppresses overmatch reason", PackageAggregatorSuppressesOvermatchReason),
```

```csharp
private static void PackageAggregatorPicksSmallerAdequateOverRemoteOversized()
{
    var local1 = MakeCandidate(id: 1, str: 3000f, mor: 0.85f, ready: 2f, distance: 50f, tier: CandidateTier.Local);
    var local2 = MakeCandidate(id: 2, str: 3000f, mor: 0.85f, ready: 2f, distance: 60f, tier: CandidateTier.Local);
    var crossMap = MakeCandidate(id: 99, str: 20000f, mor: 0.9f, ready: 2f, distance: 800f, tier: CandidateTier.CrossMap);

    var result = DefensePackageAggregator.Select(
        candidates: new[] { local1, local2, crossMap },
        desiredStrength: 4500f,
        caution: 0.2f,
        aggression: 0f);

    AssertEqual(2, result.SelectedPackage.Count);
    AssertEqual(1, result.SelectedPackage[0].UnitInstanceId);
    AssertEqual(2, result.SelectedPackage[1].UnitInstanceId);
    AssertTrue(result.Adequate, "two locals should be adequate");
    AssertTrue(!result.Understrength, "two locals should not be understrength");
    AssertTrue(result.Suppressed.Exists(s => s.UnitInstanceId == 99),
        "cross-map army should be suppressed");
}

private static void PackageAggregatorStopsAtOvershootGuard()
{
    var local1 = MakeCandidate(1, 6000f, 0.9f, 2f, 50f, CandidateTier.Local);
    var local2 = MakeCandidate(2, 6000f, 0.9f, 2f, 50f, CandidateTier.Local);
    var local3 = MakeCandidate(3, 6000f, 0.9f, 2f, 50f, CandidateTier.Local);

    var result = DefensePackageAggregator.Select(
        candidates: new[] { local1, local2, local3 },
        desiredStrength: 5000f,
        caution: 0.2f, aggression: 0f);

    AssertEqual(1, result.SelectedPackage.Count);
    AssertTrue(result.Adequate, "single local should clear desired");
}

private static void PackageAggregatorEmitsUnderstrengthFlag()
{
    var local1 = MakeCandidate(1, 1500f, 0.7f, 1f, 50f, CandidateTier.Local);

    var result = DefensePackageAggregator.Select(
        candidates: new[] { local1 },
        desiredStrength: 6000f,
        caution: 0.2f, aggression: 0f);

    AssertTrue(!result.Adequate, "single understrength brigade should not be adequate");
    AssertTrue(result.Understrength, "should be flagged understrength");
    AssertEqual(1, result.SelectedPackage.Count);
}

private static void PackageAggregatorSuppressesOvermatchReason()
{
    var smallThreat = 2000f;
    var oversized = MakeCandidate(1, 30000f, 0.9f, 2f, 50f, CandidateTier.Local);
    var rightSized = MakeCandidate(2, 2500f, 0.85f, 2f, 60f, CandidateTier.Local);

    var result = DefensePackageAggregator.Select(
        candidates: new[] { oversized, rightSized },
        desiredStrength: smallThreat,
        caution: 0.5f, aggression: 0f);

    AssertEqual(2, result.SelectedPackage.Count > 0 ? result.SelectedPackage[0].UnitInstanceId : -1);
    AssertTrue(result.Suppressed.Exists(s => s.UnitInstanceId == 1 && s.Reason == "overmatch"),
        "oversized army suppressed for overmatch");
}

private static DefenseCandidate MakeCandidate(int id, float str, float mor, float ready, float distance, CandidateTier tier)
{
    return new DefenseCandidate
    {
        UnitInstanceId = id,
        ActiveStrength = str,
        Morale = mor,
        ReadinessStep = ready,
        DistanceToThreat = distance,
        Tier = tier
    };
}
```

- [ ] **Step 2: Run tests, expect compile failure**

- [ ] **Step 3: Create `DefensePackageAggregator.cs`**

Write `src/WhiskeyRealism/Strategic/DefensePackageAggregator.cs`. The aggregator follows the spec algorithm exactly:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace WhiskeyRealism.Strategic
{
    public sealed class DefensePackageResult
    {
        public List<DefenseCandidate> SelectedPackage = new List<DefenseCandidate>();
        public List<DefenseSuppression> Suppressed = new List<DefenseSuppression>();
        public bool Adequate;
        public bool Understrength;
        public float CumulativeEffective;
    }

    public static class DefensePackageAggregator
    {
        public const float AdequateRatio   = 0.75f;
        public const float StopRatio       = 1.00f;
        public const float OvershootRatio  = 1.25f;
        public const float WorseTierStop   = 0.85f;

        public static DefensePackageResult Select(
            IEnumerable<DefenseCandidate> candidates,
            float desiredStrength,
            float caution,
            float aggression)
        {
            var result = new DefensePackageResult();
            if (candidates == null) return result;

            var scored = new List<DefenseCandidate>();
            foreach (var c in candidates)
            {
                if (c == null) continue;
                c.EffectiveStrength = EffectiveStrength(c);
                c.Score = DefenseForceSizer.ScoreCandidate(
                    activeStrength: c.ActiveStrength,
                    morale: c.Morale,
                    readinessStep: c.ReadinessStep,
                    distance: c.DistanceToThreat,
                    desiredStrength: desiredStrength,
                    inOffensiveOperation: c.InOffensiveOperation,
                    caution: caution,
                    aggression: aggression);
                scored.Add(c);
            }
            scored.Sort((a, b) => a.Score.CompareTo(b.Score));

            float desired = Math.Max(1f, desiredStrength);
            float cumulative = 0f;
            CandidateTier currentTier = CandidateTier.Local;

            foreach (var c in scored)
            {
                if (cumulative >= desired * StopRatio)
                {
                    float wouldBe = cumulative + c.EffectiveStrength;
                    if (wouldBe >= desired * OvershootRatio)
                    {
                        result.Suppressed.Add(new DefenseSuppression
                        {
                            UnitInstanceId = c.UnitInstanceId,
                            Reason = "overmatch"
                        });
                        continue;
                    }
                }

                if (cumulative >= desired * WorseTierStop &&
                    result.SelectedPackage.Count > 0 &&
                    c.Tier > currentTier)
                {
                    result.Suppressed.Add(new DefenseSuppression
                    {
                        UnitInstanceId = c.UnitInstanceId,
                        Reason = "worse-tier"
                    });
                    continue;
                }

                if (c.Tier > currentTier) currentTier = c.Tier;
                result.SelectedPackage.Add(c);
                cumulative += c.EffectiveStrength;
            }

            result.CumulativeEffective = cumulative;
            result.Adequate = cumulative >= desired * AdequateRatio;
            result.Understrength = !result.Adequate;
            return result;
        }

        private static float EffectiveStrength(DefenseCandidate c)
        {
            float morale = Clamp(c.Morale, 0.25f, 1.25f);
            float readiness = c.ReadinessStep < 1f ? 0.25f : (c.ReadinessStep < 2f ? 0.75f : 1f);
            return Math.Max(0f, c.ActiveStrength) * morale * readiness;
        }

        private static float Clamp(float v, float lo, float hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
```

- [ ] **Step 4: Run tests, expect pass**

- [ ] **Step 5: Commit**

```bash
git add src/WhiskeyRealism/Strategic/DefensePackageAggregator.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: add multi-unit defense package aggregator"
```

### Task 11: Add `DefenseCooldownTable` (TDD)

**Files:**
- Create: `src/WhiskeyRealism/Strategic/DefenseCooldownTable.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Write failing tests**

Append + register:

```csharp
("cooldown table extends on threat re-detection", CooldownTableExtendsOnThreatRedetection),
("cooldown table decrements once per tick", CooldownTableDecrementsOncePerTick),
("cooldown table expires at zero", CooldownTableExpiresAtZero),
```

```csharp
private static void CooldownTableExtendsOnThreatRedetection()
{
    var table = new DefenseCooldownTable();
    table.MarkActive("sif:42:Hampton:Boston", cooldownDays: 3);
    table.Tick();
    table.MarkActive("sif:42:Hampton:Boston", cooldownDays: 3);
    AssertEqual(3, table.RemainingDays("sif:42:Hampton:Boston"));
}

private static void CooldownTableDecrementsOncePerTick()
{
    var table = new DefenseCooldownTable();
    table.MarkRecovered("raid:7:wilmington-harbor", cooldownDays: 4);
    AssertEqual(4, table.RemainingDays("raid:7:wilmington-harbor"));
    table.Tick();
    AssertEqual(3, table.RemainingDays("raid:7:wilmington-harbor"));
    table.Tick();
    AssertEqual(2, table.RemainingDays("raid:7:wilmington-harbor"));
}

private static void CooldownTableExpiresAtZero()
{
    var table = new DefenseCooldownTable();
    table.MarkRecovered("asset:SeaHarbor:wilmington-harbor:1,2,3", cooldownDays: 1);
    table.Tick();
    AssertEqual(0, table.RemainingDays("asset:SeaHarbor:wilmington-harbor:1,2,3"));
    AssertTrue(!table.IsActive("asset:SeaHarbor:wilmington-harbor:1,2,3"),
        "expired entry should report not-active");
}
```

- [ ] **Step 2: Run tests, expect compile failure**

- [ ] **Step 3: Create `DefenseCooldownTable.cs`**

```csharp
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public sealed class DefenseCooldownTable
    {
        private readonly Dictionary<string, int> _remaining = new Dictionary<string, int>();

        public void MarkActive(string signature, int cooldownDays)
        {
            if (string.IsNullOrEmpty(signature)) return;
            _remaining[signature] = cooldownDays;
        }

        public void MarkRecovered(string signature, int cooldownDays)
        {
            if (string.IsNullOrEmpty(signature)) return;
            _remaining[signature] = cooldownDays;
        }

        public int RemainingDays(string signature)
        {
            if (string.IsNullOrEmpty(signature)) return 0;
            return _remaining.TryGetValue(signature, out var n) ? n : 0;
        }

        public bool IsActive(string signature)
        {
            return RemainingDays(signature) > 0;
        }

        public void Tick()
        {
            var keys = new List<string>(_remaining.Keys);
            foreach (var k in keys)
            {
                int v = _remaining[k] - 1;
                if (v <= 0) _remaining.Remove(k);
                else _remaining[k] = v;
            }
        }

        public void Clear(string signature)
        {
            if (!string.IsNullOrEmpty(signature)) _remaining.Remove(signature);
        }
    }
}
```

- [ ] **Step 4: Run tests, expect pass**

- [ ] **Step 5: Commit**

```bash
git add src/WhiskeyRealism/Strategic/DefenseCooldownTable.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: add per-alliance defense cooldown table"
```

### Task 12: Add `DefenseIntentLedger` pure builder + 13 spec fixture tests (TDD)

This is the largest single TDD task — it implements the spec's "Tests" section in full.

**Files:**
- Create: `src/WhiskeyRealism/Strategic/DefenseIntentLedger.cs`
- Create: `src/WhiskeyRealism/Strategic/DefenseIntentInput.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs` (add fixture-builder helpers + 13 tests)

- [ ] **Step 1: Define the input shape**

Write `src/WhiskeyRealism/Strategic/DefenseIntentInput.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace WhiskeyRealism.Strategic
{
    public enum DefenseThreatSourceKind
    {
        SeaInvasion = 1,
        RaidForce = 2,
        AssetProximity = 3
    }

    public sealed class DefenseThreatSource
    {
        public DefenseThreatSourceKind Kind;
        public int InvasionForceInstanceId;
        public string SpotName;
        public string SourcePortName;
        public int RaidGroupInstanceId;
        public int RaidCurrentState;
        public string AssetName;
        public CampaignMapAssetKind AssetKind;
        public AssetStrategicRole AssetRole;
        public Vector3 Position;
        public float EnemyStrength;
        public int[] EnemyInstanceIds;
        public bool LandedSignal;
        public bool VanillaCollapsed;
    }

    public sealed class DefenseIntentInput
    {
        public int AllianceId;
        public bool PlayerIsCIC;
        public PersonalityVector CICPersonality;
        public List<DefenseThreatSource> Threats = new List<DefenseThreatSource>();
        public List<DefenseCandidate> Candidates = new List<DefenseCandidate>();
        public DefenseCooldownTable Cooldown = new DefenseCooldownTable();
        public int CooldownDays = 4;
        public float GuardBudgetFraction = 0.10f;
        public float TotalAllianceEffectiveStrength;
        public List<CampaignMapAsset> GuardCandidateAssets = new List<CampaignMapAsset>();
    }
}
```

- [ ] **Step 2: Write the 13 fixture tests**

Append the helper at the bottom of `Program.cs` (just above `AssertEqual`):

```csharp
private static DefenseIntentInput MakeDefenseInput(int allianceId)
{
    return new DefenseIntentInput
    {
        AllianceId = allianceId,
        PlayerIsCIC = false,
        CICPersonality = default(PersonalityVector),
        TotalAllianceEffectiveStrength = 60000f
    };
}
```

Register all 13 tests in the tuple list:

```csharp
("defense ledger coastal guard forbids cross-map", DefenseLedgerCoastalGuardForbidsCrossMap),
("defense ledger minor raid forbids cross-map", DefenseLedgerMinorRaidForbidsCrossMap),
("defense ledger decisive landing allows cross-theater", DefenseLedgerDecisiveLandingAllowsCrossTheater),
("defense ledger same-theater adequate beats remote oversized", DefenseLedgerSameTheaterAdequateBeatsRemoteOversized),
("defense ledger guard budget caps low-value ports", DefenseLedgerGuardBudgetCapsLowValuePorts),
("defense ledger active invasion persists through favorable tick", DefenseLedgerActiveInvasionPersistsThroughFavorableTick),
("defense ledger recovered threat releases after cooldown", DefenseLedgerRecoveredThreatReleasesAfterCooldown),
("defense ledger player cic short-circuits alliance", DefenseLedgerPlayerCicShortCircuitsAlliance),
("defense ledger wl subordinate protects only marked unit", DefenseLedgerWlSubordinateProtectsOnlyMarkedUnit),
("defense ledger critical-front candidate rejected unless decisive", DefenseLedgerCriticalFrontCandidateRejectedUnlessDecisive),
("defense ledger river harbor detects without sif", DefenseLedgerRiverHarborDetectsWithoutSif),
("defense ledger raidforce coverage", DefenseLedgerRaidForceCoverage),
("defense ledger debug seainvasionsactive off falls back", DefenseLedgerDebugSeaInvasionsActiveOffFallsBack),
```

For each test, write a method body that builds a `DefenseIntentInput`, calls `DefenseIntentLedger.Build(input)`, and asserts on the output. The 13 test bodies follow the spec's "Pure tests" section exactly. Example for the first:

```csharp
private static void DefenseLedgerCoastalGuardForbidsCrossMap()
{
    var input = MakeDefenseInput(allianceId: 1);
    input.GuardCandidateAssets.Add(new CampaignMapAsset
    {
        Kind = CampaignMapAssetKind.SeaHarbor,
        Name = "wilmington-harbor",
        StrategicRole = AssetStrategicRole.BlockadeRunnerPort,
        Owner = 1
    });
    input.Candidates.Add(new DefenseCandidate
    {
        UnitInstanceId = 99, ActiveStrength = 18000f, Morale = 0.9f, ReadinessStep = 2f,
        DistanceToThreat = 1200f, Tier = CandidateTier.CrossMap
    });

    var output = DefenseIntentLedger.Build(input);

    AssertEqual(1, output.Responses.Count);
    var r = output.Responses[0];
    AssertEqual(DefensePosture.CoastalGuard, r.Threat.Posture);
    AssertEqual(0, r.SelectedPackage.Count);
    AssertTrue(r.Suppressed.Exists(s => s.UnitInstanceId == 99 && s.Reason == "forbidden-cross-map"),
        "cross-map division must be suppressed for guard");
}
```

For brevity in this plan, do **not** inline all 13 test bodies — implement them following the same shape. Each test:
- builds a `DefenseIntentInput` via `MakeDefenseInput(alliance)`,
- adds threats / candidates / guard assets that match the spec scenario,
- calls `DefenseIntentLedger.Build(input)`,
- asserts on `output.Responses[0].Threat.Posture`, `.Scale`, `.SelectedPackage` membership, `.Suppressed[*].Reason`, and `.Adequate` / `.Understrength`.

Direct mapping from spec scenario to test:

| Spec scenario | Asserts |
|---|---|
| coastal-guard-forbids-cross-map | posture=CoastalGuard, selected empty, cross-map suppressed `forbidden-cross-map` |
| minor-raid-forbids-cross-map | posture=ActiveInvasion, scale=Raid, cross-map suppressed `forbidden-cross-map` |
| decisive-landing-allows-cross-theater | posture=ActiveInvasion, escalation=cross-theater, adjacent-theater army selected |
| same-theater-adequate-beats-remote-oversized | both same-theater brigades selected, cross-map army suppressed `overmatch`+`forbidden-cross-map` |
| guard-budget-caps-low-value-ports | ≤ floor cap of selected packages; remainder reason `cap-reached` |
| active-invasion-persists-through-favorable-tick | tick-2 cooldown counter not started |
| recovered-threat-releases-after-cooldown | release at tick `1 + cooldownDays`, not at tick 1 |
| player-cic-short-circuits-alliance | output.Responses empty for player-CIC alliance |
| wl-subordinate-protects-only-marked-unit | only the `PlayerControlled=true` unit suppressed `player-controlled` |
| critical-front-candidate-rejected-unless-decisive | first run: suppressed `critical-front`; second run with DecisiveLanding: selected `decisive-no-alternative` |
| river-harbor-detects-without-sif | posture=ActiveInvasion, threat signature starts with `asset:RiverHarbor:` |
| raidforce-coverage | scale=Raid using `RaidForce` source kind, no SeaInvasion entry |
| debug-seainvasionsactive-off | input has `Threats` populated only via `AssetProximity`; no throw, posture detected |

- [ ] **Step 3: Create `DefenseIntentLedger.cs`**

Write `src/WhiskeyRealism/Strategic/DefenseIntentLedger.cs` to satisfy the 13 tests. The builder:

1. Short-circuits if `input.PlayerIsCIC` (return empty `DefenseIntentLedgerOutput`).
2. For each `DefenseThreatSource`, builds a `DefenseThreat`:
   - posture from source kind + `LandedSignal` + `VanillaCollapsed` + `Cooldown.IsActive(signature)`,
   - scale from `EnemyStrength` and `AssetRole`,
   - signature from `DefenseThreatSignature`.
3. Computes `desiredStrength` from scale (Raid: `EnemyStrength * 1.0`, Landing: `* 1.5`, MajorLanding: `* 2.0`, DecisiveLanding: `* 2.25`).
4. Builds candidate set from `input.Candidates`, tagging each with `Tier` (Local/SameTheater/AdjacentTheater/CrossMap from `c.Tier` directly — runtime fills this; pure tests set it explicitly).
5. Pre-suppresses candidates that are forbidden for this posture/scale: cross-map forbidden under `CoastalGuard` and `Raid`; player-controlled always suppressed; critical-front suppressed unless `DecisiveLanding` and no alternative.
6. Calls `DefensePackageAggregator.Select(survivors, desiredStrength, caution, aggression)` for the rest.
7. Combines aggregator suppressions with pre-suppressions.
8. For `Recovered` posture, calls `input.Cooldown.MarkRecovered(signature, input.CooldownDays)`; release the response (empty `SelectedPackage`) if `!Cooldown.IsActive` after `MarkRecovered`.
9. Applies `GuardBudgetFraction` cap for `CoastalGuard` postures: count packages already committed, if commitment fraction would exceed `GuardBudgetFraction * TotalAllianceEffectiveStrength`, mark remaining guard responses with `Suppressed` reason `cap-reached` and empty `SelectedPackage`.

The builder is pure — all I/O lives in `DefenseIntentRuntime` (Task 13).

```csharp
using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public static class DefenseIntentLedger
    {
        public static DefenseIntentLedgerOutput Build(DefenseIntentInput input)
        {
            var output = new DefenseIntentLedgerOutput
            {
                AllianceId = input?.AllianceId ?? -1
            };
            if (input == null) return output;
            if (input.PlayerIsCIC) return output;

            float guardCommitted = 0f;
            float guardCap = Math.Max(0f, input.TotalAllianceEffectiveStrength) *
                Math.Max(0f, input.GuardBudgetFraction);

            foreach (var src in input.Threats)
            {
                if (src == null) continue;
                var response = BuildResponse(input, src, ref guardCommitted, guardCap);
                if (response != null) output.Responses.Add(response);
            }

            foreach (var asset in input.GuardCandidateAssets)
            {
                if (asset == null) continue;
                if (input.Threats.Exists(t => string.Equals(t.AssetName, asset.Name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                var guard = BuildGuardResponse(input, asset, ref guardCommitted, guardCap);
                if (guard != null) output.Responses.Add(guard);
            }

            output.Signature = BuildSignature(output);
            return output;
        }

        // Implementation details for BuildResponse / BuildGuardResponse / BuildSignature
        // follow the algorithm described above. Do not stub — write the full body until
        // all 13 fixture tests pass. Reuse DefensePackageAggregator.Select for the
        // aggregator step. Use DefenseThreatSignature for keying.
    }
}
```

The plan does not transcribe the full `BuildResponse` / `BuildGuardResponse` bodies because they evolve as each test goes red→green. Implement them test-by-test:

1. Make `coastal-guard-forbids-cross-map` pass first — wires `BuildGuardResponse` and the `forbidden-cross-map` suppression for `Tier=CrossMap` under `CoastalGuard`.
2. Add `minor-raid-forbids-cross-map` — extends `BuildResponse` with `Raid` scale derivation and the same cross-map suppression for `Raid` scale.
3. Continue down the table in order. Each new test exercises one feature of the builder; new logic lands incrementally.

After each test transitions red→green, run the full suite to confirm no regression, then commit.

- [ ] **Step 4: Build + run all tests**

```bash
./build.sh
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: 0 warnings, 0 errors. All 13 new tests + all prior tests pass.

- [ ] **Step 5: Single commit at end of task**

```bash
git add src/WhiskeyRealism/Strategic/DefenseIntentLedger.cs src/WhiskeyRealism/Strategic/DefenseIntentInput.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: add pure DefenseIntentLedger with 13 fixture tests"
```

(If you commit between sub-steps as you make tests pass, that is also fine — `feat: pass <test-name>` per commit. Frequent commits are encouraged.)

---

## Phase D — Runtime extraction + Slice 1 observer (Tasks 13-15)

Phase C built the pure ledger. Phase D wires real GTCW state into it and emits telemetry — no vanilla writes yet.

### Task 13: Add `DefenseIntentRuntime` reflection extractor

**Files:**
- Create: `src/WhiskeyRealism/Strategic/DefenseIntentRuntime.cs`

This file extracts the `DefenseIntentInput` from live vanilla state. It is reflection-heavy and not unit-testable; correctness is validated by Slice 1 smoke (Task 15).

- [ ] **Step 1: Sketch the public API**

Write `src/WhiskeyRealism/Strategic/DefenseIntentRuntime.cs`:

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    internal static class DefenseIntentRuntime
    {
        internal static DefenseIntentInput BuildInput(
            int allianceId,
            CIC cic,
            EraStageManager era,
            CampaignMapLedger map,
            DefenseCooldownTable cooldown,
            float totalAllianceEffectiveStrength)
        {
            var input = new DefenseIntentInput
            {
                AllianceId = allianceId,
                PlayerIsCIC = cic == null,
                CICPersonality = cic?.Effective(era) ?? default(PersonalityVector),
                Cooldown = cooldown,
                CooldownDays = 4,
                GuardBudgetFraction = 0.10f,
                TotalAllianceEffectiveStrength = totalAllianceEffectiveStrength
            };
            if (input.PlayerIsCIC) return input;

            int aifactionIndex = ResolveAifactionIndex(allianceId);
            if (aifactionIndex < 0) return input;

            var faction = AICampaignReflect.GetFaction(aifactionIndex);
            if (faction == null) return input;

            ExtractGuardCandidateAssets(input, allianceId, map);
            ExtractSeaInvasionThreats(input, allianceId, faction, map);
            ExtractRaidForceThreats(input, allianceId, faction, map);
            ExtractAssetProximityThreats(input, allianceId, faction, map);
            ExtractCandidateUnits(input, allianceId, faction);

            return input;
        }

        // Implementation: each Extract* method uses AccessTools to read the
        // vanilla static lists / per-faction fields enumerated in the spec
        // §"Source Findings" and §"Inputs". Each catches Exception, calls
        // OnceLog.Warning("defense-intent:<scope>", ...), and continues.
    }
}
```

- [ ] **Step 2: Implement `ExtractSeaInvasionThreats`**

Reads the static `AICampaign.seainvasionforce` list. For each entry whose `aifactionused` matches `aifactionIndex` or whose target is hostile to `allianceId`, creates a `DefenseThreatSource` with `Kind=SeaInvasion`, `InvasionForceInstanceId=invasionforce.GetInstanceID()`, `SpotName=seainvasionspot.name`, `SourcePortName=sourceport.name`, `Position=invasionforce.transform.position`, and computes `LandedSignal` per spec §Threat Detection #1: any of (a) on land terrain near spot, (b) `seainvasionspot.GetNumberOfEnemyObjectives(allianceId) > 0` and `currentobjective != null`, or (c) `invasionforce.regimentpaths > 0` while close to a coastal asset.

`VanillaCollapsed` is true when the entry is no longer in `seainvasionforce` between two consecutive ledger builds; track this in a per-alliance "seen signatures" set on `StrategicCoordinator` and fire `VanillaCollapsed=true` for signatures missing this tick.

Wrap reads in try/catch + `OnceLog.Warning("defense-intent:sif:<scope>", message)` per shipped reflection convention.

- [ ] **Step 3: Implement `ExtractRaidForceThreats`**

Reads the static `RaidForce.raidforce` list (or equivalent — confirm field name with `grep -n "static List<RaidForce>" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`). For each raid whose `raidgroup` is within `GamePrefs.maxdistancedefensiveoperations` of any `CampaignMapLedger.Assets` entry, creates a `DefenseThreatSource` with `Kind=RaidForce`, `RaidGroupInstanceId=raidgroup.GetInstanceID()`, `RaidCurrentState=currentstate`, `Position=raidgroup.transform.position`, `AssetName=<nearest asset>`, `EnemyStrength=raidgroup.groupstrengthactive`.

- [ ] **Step 4: Implement `ExtractAssetProximityThreats`**

For each asset in `map.Assets` whose `Owner == allianceId`, scan `aifaction[i].enemyunits` and collect units within asset radius (`GamePrefs.seainvasionspotsunitrange` or `maxdistancedefensiveoperations / 2` — pick the more conservative). If at least one enemy is in radius and no `SeaInvasion`/`RaidForce` source already keys on this asset, emit a `DefenseThreatSource` with `Kind=AssetProximity`, `AssetName=asset.Name`, `AssetKind=asset.Kind`, `AssetRole=asset.StrategicRole`, `Position=asset position`, `EnemyStrength=sum of in-radius enemy effective strength`, `EnemyInstanceIds=top-N enemy `GetInstanceID()` values.

Apply the `AssetRoleScorer` (Task 5) to extend `AssetRole` with profile-derived flags before the source is appended.

- [ ] **Step 5: Implement `ExtractCandidateUnits`**

Walks `aifaction[i].ownunits`. For each unit not in `unitsconstructingsupplydepots` / `seainvasionforce`-escort / `raidforce`, builds a `DefenseCandidate` with `UnitInstanceId=unit.GetInstanceID()`, `Position=unit.transform.position`, strength/morale/readiness from the unit, `Tier` from distance to nearest threat:
- `Local` if `< maxdistancedefensiveoperations`,
- `SameTheater` if `TheaterClassifier.FromPosition(unitPos) == threatTheater`,
- `AdjacentTheater` if same-theater fails but the theater pair is in the static adjacency map (East↔West, West↔TransMiss, Coast↔matching land theater),
- else `CrossMap`.

Mark `PlayerControlled=true` when `DLC_WL.IsMovedByPlayer(unit) == true`. Mark `CriticalFront=true` when `StrategicCoordinator.Instance.Fronts[alliance]` reports the candidate's source sector posture as `Hold` or `Critical`.

- [ ] **Step 6: Implement `ExtractGuardCandidateAssets`**

Returns the subset of `map.Assets` where `Owner == allianceId` AND no active threat already keys on the asset name AND the asset's `StrategicRole` (after scorer extension) is non-`None` OR the asset is a `Fort`/`SeaHarbor`/`RiverHarbor` of `Level >= 2`.

- [ ] **Step 7: Build (no test step — runtime is reflection)**

```bash
./build.sh
```

Expected: 0 warnings, 0 errors. Existing tests pass (Phase C tests are pure and unaffected).

- [ ] **Step 8: Commit**

```bash
git add src/WhiskeyRealism/Strategic/DefenseIntentRuntime.cs
git commit -m "feat: add DefenseIntentRuntime vanilla extractor"
```

### Task 14: Wire DefenseIntentLedger into `StrategicCoordinator` (Slice 1 observer)

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`
- Modify: `src/WhiskeyRealism/Plugin.cs` — add config gates

- [ ] **Step 1: Add config gates in `Plugin.cs`**

In the `Plugin.Awake` config bind region, after the existing construction config block, add:

```csharp
EnableDefenseIntentLedger = Config.Bind(
    "Defense Intent Ledger",
    "Enable Defense Intent Ledger",
    true,
    "Compute the daily defense ledger (Slice 1 observer). Disable to suppress all [DefenseIntent] output.");

DefenseIntentVerboseLogging = Config.Bind(
    "Defense Intent Ledger",
    "Defense Intent Verbose Logging",
    false,
    "Log per-tick defense intent telemetry even when the signature has not changed.");
```

Add the corresponding properties to `Plugin.cs` (matching shipped pattern):

```csharp
public ConfigEntry<bool> EnableDefenseIntentLedger;
public ConfigEntry<bool> DefenseIntentVerboseLogging;
```

- [ ] **Step 2: Add ledger fields in `StrategicCoordinator`**

Below the existing `public ConstructionTelemetry ConstructionTelemetry = ...` line, add:

```csharp
public DefenseIntentLedgerOutput[] DefenseIntents = new DefenseIntentLedgerOutput[2];
private readonly DefenseCooldownTable[] _defenseCooldowns = new DefenseCooldownTable[2]
{
    new DefenseCooldownTable(),
    new DefenseCooldownTable()
};
private readonly string[] _defenseIntentSignatures = new string[2];
```

- [ ] **Step 3: Add `UpdateDefenseIntent` method**

Append to the per-alliance loop inside `RunStrategicReview`, after the existing `UpdateConstructionIntent` call:

```csharp
UpdateDefenseIntent(alliance, era.Stage, day, month, year);
```

Then add the method body inside `StrategicCoordinator`:

```csharp
private void UpdateDefenseIntent(int alliance, EraStage era, int day, int month, int year)
{
    try
    {
        var plugin = Plugin.Instance;
        if (plugin == null || !plugin.EnableDefenseIntentLedger.Value) return;
        if (alliance < 0 || alliance >= DefenseIntents.Length) return;

        _defenseCooldowns[alliance].Tick();

        float total = ComputeAllianceEffectiveStrength(alliance);
        var input = DefenseIntentRuntime.BuildInput(
            alliance, CICs[alliance], Eras[alliance], CampaignMap,
            _defenseCooldowns[alliance], total);
        var output = DefenseIntentLedger.Build(input);
        DefenseIntents[alliance] = output;

        bool verbose = plugin.VerboseLogging.Value || plugin.DefenseIntentVerboseLogging.Value;
        if (verbose || _defenseIntentSignatures[alliance] != output.Signature)
        {
            foreach (var r in output.Responses)
            {
                Plugin.Log.LogInfo(
                    $"[DefenseIntent] alliance={alliance} posture={r.Threat.Posture} " +
                    $"threat={r.Threat.Signature} enemy={r.Threat.EnemyStrength:F0} " +
                    $"desired={r.Threat.DesiredStrength:F0} selected={r.SelectedPackage.Count} " +
                    $"reason={r.Threat.EscalationReason ?? ""} sig={r.TelemetrySignature ?? ""}");
            }
            _defenseIntentSignatures[alliance] = output.Signature;
        }
    }
    catch (Exception ex)
    {
        OnceLog.Warning("defense-intent:update:" + alliance,
            "[DefenseIntent] update failed: " + ex.Message);
    }
}

private float ComputeAllianceEffectiveStrength(int alliance)
{
    try
    {
        int aifactionIndex = ResolveAifactionIndexFor(alliance);
        if (aifactionIndex < 0) return 0f;
        var faction = AICampaignReflect.GetFaction(aifactionIndex);
        var ownUnits = AccessTools.Field(faction.GetType(), "ownunits")?.GetValue(faction) as IList;
        if (ownUnits == null) return 0f;
        float total = 0f;
        foreach (Regiment u in ownUnits)
        {
            if (u == null) continue;
            total += (float)u.groupstrengthactive * Math.Max(0.25f, u.groupmorale);
        }
        return total;
    }
    catch { return 0f; }
}

private static int ResolveAifactionIndexFor(int allianceId)
{
    try
    {
        var aicType = AccessTools.TypeByName("AICampaign");
        var list = AccessTools.Field(aicType, "aifaction")?.GetValue(null) as IList;
        if (list == null) return -1;
        for (int i = 0; i < list.Count; i++)
        {
            int aid = AICampaignReflect.GetAllianceId(i);
            if (aid == allianceId) return i;
        }
        return -1;
    }
    catch { return -1; }
}
```

- [ ] **Step 4: Build + run pure tests**

```bash
./build.sh
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: 0 warnings, 0 errors. All tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/WhiskeyRealism/Strategic/StrategicCoordinator.cs src/WhiskeyRealism/Plugin.cs
git commit -m "feat: wire defense intent ledger into daily review (Slice 1)"
```

### Task 15: Slice 1 smoke

**Files:** None

- [ ] **Step 1: Deploy + verify**

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

- [ ] **Step 2: Ask user to launch + run for several days**

Tell the user: "Launch GTCW, start fresh W&L 002, advance the campaign at 1x for 10 game-days, then 5x for 30 game-days, then stop. Send the relevant `[DefenseIntent]` and `[once:defense-intent:*]` lines from `BepInEx/LogOutput.log`."

- [ ] **Step 3: Validate observer telemetry**

Expected:
- one `[once:dailyops]`,
- early `[CampaignMap]` summary with town/asset counts,
- `[DefenseIntent:asset] missing-role` lines (one per unmapped asset name) — capture these and refine `AssetRoleCatalog` keys in Task 6's file as a follow-up commit,
- `[DefenseIntent]` lines emitted only on signature change (postures appearing as expected: `CoastalGuard` for owned ports without an active enemy, possibly `InvasionWatch` later if Union AI begins forming an invasion),
- zero new warning/error lines.

If postures look wrong or threats look mis-detected, fix `DefenseIntentRuntime` (Task 13) before proceeding to Phase E.

- [ ] **Step 4: No code change → no commit. If a fix lands, commit with `fix: ...`.**

---

## Phase E — Slice 2 patch surfaces (Tasks 16-20)

Phase E enforces the ledger's verdicts via three Harmony patches. Order: candidate-filter Prefix + Postfix re-issue first (this is the safety net that prevents bad cross-map pulls), then `DefensiveOpsPatch` priority adjustment, then the custom defensive movement order runner.

### Task 16: Add `CheckForDefensiveOperationsCandidateFilterPatch` (Prefix snapshot/restore + Postfix re-issue)

**Files:**
- Create: `src/WhiskeyRealism/Patches/CheckForDefensiveOperationsCandidateFilterPatch.cs`
- Modify: `docs/patch-catalog.md` — register patch ordinal #25

- [ ] **Step 1: Create the patch shell**

Write `src/WhiskeyRealism/Patches/CheckForDefensiveOperationsCandidateFilterPatch.cs`:

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Patch ordinal #25 — defense intent slice.
    // Prefix snapshots aifaction[i].ownunits, removes ledger-forbidden references
    // for the duration of vanilla's CheckForDefensiveOperations call, paired
    // Postfix restores the snapshot and reverts any vanilla addition that the
    // ledger marks forbidden (cross-map pull on CoastalGuard, critical-front
    // pull, etc.). Implements spec §"Enforcement surfaces".
    [HarmonyPatch(typeof(AICampaign), "CheckForDefensiveOperations")]
    internal static class CheckForDefensiveOperationsCandidateFilterPatch
    {
        private static readonly Dictionary<int, IList> _savedOwnUnits = new Dictionary<int, IList>();
        private static readonly Dictionary<int, IList> _savedDefensiveOps = new Dictionary<int, IList>();

        [HarmonyPrefix]
        internal static void Prefix(int _aifaction)
        {
            OnceLog.Info("defense-intent-filter", "CheckForDefensiveOperationsCandidateFilterPatch wired");

            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.EnableDefenseIntentLedger.Value) return;
                int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                if (allianceId < 0) return;

                var output = StrategicCoordinator.Instance?.DefenseIntents?[allianceId];
                if (output == null || output.Responses.Count == 0) return;

                var forbidden = CollectForbiddenIds(output);
                if (forbidden.Count == 0) return;

                var faction = AICampaignReflect.GetFaction(_aifaction);
                if (faction == null) return;

                var ownUnits = AccessTools.Field(faction.GetType(), "ownunits")?.GetValue(faction) as IList;
                if (ownUnits == null) return;

                _savedOwnUnits[_aifaction] = SnapshotAndFilter(ownUnits, forbidden);
                _savedDefensiveOps[_aifaction] = SnapshotList(faction, "unitsindefensiveoperations");
            }
            catch (Exception ex)
            {
                OnceLog.Warning("defense-intent:filter:prefix",
                    "candidate-filter Prefix failed: " + ex.Message);
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(int _aifaction)
        {
            try
            {
                int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                if (allianceId < 0) return;

                var faction = AICampaignReflect.GetFaction(_aifaction);
                if (faction == null) return;

                if (_savedOwnUnits.TryGetValue(_aifaction, out var saved))
                {
                    RestoreList(faction, "ownunits", saved);
                    _savedOwnUnits.Remove(_aifaction);
                }

                if (_savedDefensiveOps.TryGetValue(_aifaction, out var savedOps))
                {
                    var output = StrategicCoordinator.Instance?.DefenseIntents?[allianceId];
                    if (output != null) RevertForbiddenAdditions(faction, savedOps, output);
                    _savedDefensiveOps.Remove(_aifaction);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("defense-intent:filter:postfix",
                    "candidate-filter Postfix failed: " + ex.Message);
            }
        }

        private static HashSet<int> CollectForbiddenIds(DefenseIntentLedgerOutput output)
        {
            var ids = new HashSet<int>();
            foreach (var r in output.Responses)
            {
                if (r?.Suppressed == null) continue;
                foreach (var s in r.Suppressed)
                {
                    if (s.Reason == "forbidden-cross-map" || s.Reason == "critical-front" || s.Reason == "player-controlled")
                        ids.Add(s.UnitInstanceId);
                }
            }
            return ids;
        }

        private static IList SnapshotAndFilter(IList live, HashSet<int> forbidden)
        {
            var saved = new List<object>(live.Count);
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var u = live[i];
                saved.Add(u);
                var unityObj = u as UnityEngine.Object;
                if (unityObj == null) continue;
                int id = unityObj.GetInstanceID();
                if (forbidden.Contains(id))
                    live.RemoveAt(i);
            }
            saved.Reverse();
            return saved;
        }

        private static IList SnapshotList(object faction, string fieldName)
        {
            var live = AccessTools.Field(faction.GetType(), fieldName)?.GetValue(faction) as IList;
            if (live == null) return null;
            var saved = new List<object>(live.Count);
            foreach (var item in live) saved.Add(item);
            return saved;
        }

        private static void RestoreList(object faction, string fieldName, IList saved)
        {
            if (saved == null) return;
            var live = AccessTools.Field(faction.GetType(), fieldName)?.GetValue(faction) as IList;
            if (live == null) return;
            live.Clear();
            foreach (var item in saved) live.Add(item);
        }

        private static void RevertForbiddenAdditions(object faction, IList savedOps, DefenseIntentLedgerOutput output)
        {
            var live = AccessTools.Field(faction.GetType(), "unitsindefensiveoperations")?.GetValue(faction) as IList;
            if (live == null) return;

            var forbidden = CollectForbiddenIds(output);
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var u = live[i];
                var unityObj = u as UnityEngine.Object;
                if (unityObj == null) continue;
                int id = unityObj.GetInstanceID();
                if (forbidden.Contains(id) && (savedOps == null || !ListContains(savedOps, u)))
                {
                    live.RemoveAt(i);
                    RemoveFromStaticDefensiveOperation(u as Regiment);
                    var threat = FirstThreatSuppressing(output, id);
                    Plugin.Log.LogInfo(
                        $"[DefenseIntent] reverted=cross-map threat={threat?.Threat?.Signature ?? "<unknown>"} candidate={unityObj.name} reason=guard-posture");
                }
            }
        }

        private static bool ListContains(IList list, object item)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++) if (list[i] == item) return true;
            return false;
        }

        private static DefenseResponse FirstThreatSuppressing(DefenseIntentLedgerOutput output, int unitInstanceId)
        {
            foreach (var r in output.Responses)
            {
                if (r?.Suppressed == null) continue;
                foreach (var s in r.Suppressed) if (s.UnitInstanceId == unitInstanceId) return r;
            }
            return null;
        }

        private static void RemoveFromStaticDefensiveOperation(Regiment unit)
        {
            if (unit == null) return;
            try
            {
                var nested = AccessTools.Inner(typeof(AICampaign), "DefensiveOperation");
                var method = nested != null ? AccessTools.Method(nested, "RemoveUnit", new[] { typeof(Regiment) }) : null;
                if (method != null) method.Invoke(null, new object[] { unit });
            }
            catch (Exception ex)
            {
                OnceLog.Warning("defense-intent:filter:remove-static",
                    "RemoveUnit on static defensiveoperations failed: " + ex.Message);
            }
        }
    }
}
```

- [ ] **Step 2: Build + test**

```bash
./build.sh
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: 0 warnings, 0 errors. All pure tests pass.

- [ ] **Step 3: Add catalog entry**

In `docs/patch-catalog.md`, append a new row under the active patches table:

```markdown
| 25 | `CheckForDefensiveOperationsCandidateFilterPatch` | `AICampaign.CheckForDefensiveOperations` | Prefix + Postfix | Snapshots `aifaction[i].ownunits`, removes ledger-forbidden references for the duration of vanilla's call, restores in Postfix, reverts forbidden cross-map pulls vanilla committed despite filter. Defense intent slice. |
```

(Match the exact column layout used by existing rows in `docs/patch-catalog.md`. If columns differ, follow the live convention rather than this sketch.)

- [ ] **Step 4: Commit**

```bash
git add src/WhiskeyRealism/Patches/CheckForDefensiveOperationsCandidateFilterPatch.cs docs/patch-catalog.md
git commit -m "feat: add #25 candidate-filter Prefix/Postfix for defense intent"
```

### Task 17: Add `[HarmonyPriority]` ordering on shipped #4 + capital-only invariant guard

**Files:**
- Modify: `src/WhiskeyRealism/Patches/DefensiveOpsPatch.cs`

- [ ] **Step 1: Add ordering attribute**

In `DefensiveOpsPatch.cs`, change the class-level Harmony attributes from:

```csharp
[HarmonyPatch(typeof(AICampaign), "AssignUnitToDefendCapital")]
internal static class DefensiveOpsPatch
```

to:

```csharp
[HarmonyPatch(typeof(AICampaign), "AssignUnitToDefendCapital")]
[HarmonyPriority(Priority.High)]
internal static class DefensiveOpsPatch
```

This guarantees #4 runs *before* the new ledger's Postfix surfaces (which run at default priority).

- [ ] **Step 2: Add capital-only invariant guard**

At the top of `DefensiveOpsPatch.Postfix`, after the existing `if (StrategicCoordinator.Instance == null) return;` line, insert:

```csharp
// Capital is owned exclusively by #4 — ledger never adds to groupstodefendcapital.
// Conversely, #4 never touches assets outside the capital. The guard below makes
// that explicit so the spec invariant survives future edits.
if (!TryResolveCapital(allianceId, out _, out _)) return;
```

(The early `TryResolveCapital` was previously called later; this lifts the call to the top so the patch short-circuits cleanly when the capital can't be resolved, mirroring the spec's `Capital is owned exclusively by #4` invariant.)

- [ ] **Step 3: Build + test**

```bash
./build.sh
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: 0 warnings, 0 errors. Tests still pass.

- [ ] **Step 4: Commit**

```bash
git add src/WhiskeyRealism/Patches/DefensiveOpsPatch.cs
git commit -m "feat: order #4 before defense ledger and lift capital invariant"
```

### Task 18: Add `CoastalDefenseCustomOrderRunner` (custom defensive movement order surface)

**Files:**
- Create: `src/WhiskeyRealism/Strategic/CoastalDefenseCustomOrderRunner.cs`
- Modify: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs` — invoke the runner after `UpdateDefenseIntent`

This is **not** a Harmony patch — it runs from `StrategicCoordinator` after the ledger emits its verdict, and uses vanilla's existing `MoveUnitTo` + `unitsindefensiveoperations.Add` pattern (mirroring the commit at decompile line 13718). Used for active landings vanilla rate-limited away and relaxed-filter `CoastalGuard` candidates.

- [ ] **Step 1: Sketch the runner**

Write `src/WhiskeyRealism/Strategic/CoastalDefenseCustomOrderRunner.cs`:

```csharp
using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    internal static class CoastalDefenseCustomOrderRunner
    {
        internal static void Run(int allianceId, DefenseIntentLedgerOutput output)
        {
            if (output == null || output.Responses.Count == 0) return;

            int aifactionIndex = ResolveAifactionIndex(allianceId);
            if (aifactionIndex < 0) return;
            var faction = AICampaignReflect.GetFaction(aifactionIndex);
            if (faction == null) return;

            var defOps = AccessTools.Field(faction.GetType(), "unitsindefensiveoperations")?.GetValue(faction) as IList;
            var ownUnits = AccessTools.Field(faction.GetType(), "ownunits")?.GetValue(faction) as IList;
            if (defOps == null || ownUnits == null) return;

            foreach (var r in output.Responses)
            {
                if (r?.SelectedPackage == null || r.SelectedPackage.Count == 0) continue;
                if (!RequiresCustomOrder(r)) continue;

                foreach (var c in r.SelectedPackage)
                {
                    var unit = FindUnitByInstanceId(ownUnits, c.UnitInstanceId);
                    if (unit == null) continue;
                    if (defOps.Contains(unit)) continue;

                    Vector3 anchor = ResolveDefensiveAnchor(r);
                    AICampaign.MoveUnitTo(unit, anchor, true);
                    defOps.Add(unit);
                    Plugin.Log.LogInfo(
                        $"[DefenseIntent] custom-order alliance={allianceId} threat={r.Threat.Signature} " +
                        $"unit={((UnityEngine.Object)unit).name} reason=custom-defensive-order");
                }
            }
        }

        private static bool RequiresCustomOrder(DefenseResponse r)
        {
            // ActiveInvasion and ContainAndCounterattack always use custom orders;
            // CoastalGuard only when the candidate is from the relaxed filter (low
            // morale / low readiness — the ledger flags this on the candidate).
            switch (r.Threat.Posture)
            {
                case DefensePosture.ActiveInvasion:
                case DefensePosture.ContainAndCounterattack:
                    return true;
                case DefensePosture.CoastalGuard:
                    foreach (var c in r.SelectedPackage)
                        if (c.Morale < 0.4f || c.ReadinessStep < 1f) return true;
                    return false;
                default:
                    return false;
            }
        }

        private static Vector3 ResolveDefensiveAnchor(DefenseResponse r)
        {
            // Anchor priority (per spec §Defense Postures InvasionWatch):
            // nearest fort, then nearest harbor, then town center. For the
            // active-landing surface, the threat position itself is acceptable
            // because vanilla MoveUnitTo will path around terrain.
            return r.Threat.Position;
        }

        private static Regiment FindUnitByInstanceId(IList ownUnits, int instanceId)
        {
            for (int i = 0; i < ownUnits.Count; i++)
            {
                var u = ownUnits[i] as Regiment;
                if (u == null) continue;
                if (((UnityEngine.Object)u).GetInstanceID() == instanceId) return u;
            }
            return null;
        }

        private static int ResolveAifactionIndex(int allianceId)
        {
            try
            {
                var aicType = AccessTools.TypeByName("AICampaign");
                var list = AccessTools.Field(aicType, "aifaction")?.GetValue(null) as IList;
                if (list == null) return -1;
                for (int i = 0; i < list.Count; i++)
                {
                    int aid = AICampaignReflect.GetAllianceId(i);
                    if (aid == allianceId) return i;
                }
                return -1;
            }
            catch { return -1; }
        }
    }
}
```

- [ ] **Step 2: Wire from `StrategicCoordinator.UpdateDefenseIntent`**

After the existing `_defenseIntentSignatures[alliance] = output.Signature;` block in `UpdateDefenseIntent`, add:

```csharp
CoastalDefenseCustomOrderRunner.Run(alliance, output);
```

- [ ] **Step 3: Build + run pure tests**

```bash
./build.sh
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: 0 warnings, 0 errors. All pure tests still pass (the runner is reflection-only and not unit-tested).

- [ ] **Step 4: Commit**

```bash
git add src/WhiskeyRealism/Strategic/CoastalDefenseCustomOrderRunner.cs src/WhiskeyRealism/Strategic/StrategicCoordinator.cs
git commit -m "feat: add custom defensive movement order runner"
```

### Task 19: Slice 2 deploy + smoke

**Files:** None

- [ ] **Step 1: Deploy + verify**

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

- [ ] **Step 2: Ask user to launch CSA campaign**

Tell the user: "Launch GTCW, start a fresh W&L 002 CSA campaign, advance the campaign at 5x for ~6 game-months (long enough for Union AI to attempt a sea invasion). Then stop and tail `BepInEx/LogOutput.log`. Send the `[DefenseIntent]` lines and any `[Patch:DefensiveOps]` / `[once:defense-intent-filter]` lines."

- [ ] **Step 3: Validate steering**

Expected:
- `[once:defense-intent-filter] CheckForDefensiveOperationsCandidateFilterPatch wired` appears once,
- if Union AI launches a sea invasion: `[DefenseIntent] alliance=1 posture=InvasionWatch ...` then later `posture=ActiveInvasion ...`,
- on `ActiveInvasion`, a `[DefenseIntent] custom-order ...` line names the assigned CSA defender,
- if vanilla committed a Texas brigade to defend Norfolk: `[DefenseIntent] reverted=cross-map threat=... candidate=... reason=guard-posture`,
- shipped #4 capital-defense `[Patch:DefensiveOps]` lines still appear when applicable,
- zero new warnings/errors,
- no `[DefenseIntent]` line referencing `groupstodefendcapital` (capital invariant).

If revert lines fire excessively (vanilla keeps committing forbidden pulls), refine the candidate-filter Prefix to remove more references upfront. If the custom-order runner fires too eagerly (over-defending), tighten the `RequiresCustomOrder` gate.

- [ ] **Step 4: No code change → no commit. If a fix lands, commit with `fix: ...`.**

### Task 20: Verify capital coexistence under combined patches

**Files:** None (verification only)

- [ ] **Step 1: Re-read smoke log from Task 19**

Look for any line where:
- `[Patch:DefensiveOps] alliance=1 capital=Richmond assigned=1` AND a `[DefenseIntent] custom-order ...` for the same unit appear within the same game-day. That indicates double-assignment — the new ledger reached into capital territory, violating the §"Coexistence with shipped #4" invariant.

If double-assignment occurs:
- audit `DefenseIntentRuntime.ExtractGuardCandidateAssets` to ensure it filters out the capital town and capital-cluster assets,
- audit `DefenseIntentLedger.Build` to ensure no `Threat.AssetName` matches the capital,
- add a defensive guard in `CoastalDefenseCustomOrderRunner.Run` that skips threats whose `AssetName` is the capital town's name.

- [ ] **Step 2: Commit if a fix is needed**

```bash
git commit -m "fix: ensure defense ledger never overlaps shipped #4 capital surface"
```

---

## Phase F — Docs + handoff updates (Tasks 21-22)

### Task 21: Update `docs/patch-catalog.md` and `docs/handoff.md`

**Files:**
- Modify: `docs/patch-catalog.md`
- Modify: `docs/handoff.md`

- [ ] **Step 1: `docs/patch-catalog.md` — confirm #25 entry from Task 16 is in place**

Already added in Task 16. Verify the row is present and matches the column layout used by existing rows. The custom-order runner at `Strategic/CoastalDefenseCustomOrderRunner.cs` is **not** a Harmony patch and does not get an ordinal — note this in a "Coordinator-driven runtimes" section if one exists, or add a one-line note under #25.

- [ ] **Step 2: `docs/handoff.md` — update the "What just shipped" section**

Append a new dated entry near the top of the chronological list, dated today:

```markdown
- **2026-05-05 — defense intent ledger Slice 1+2 implemented and deployed.** Adds pure `DefenseIntentLedger` with 13 fixture tests, `DefensePackageAggregator` greedy multi-unit selector, `DefenseCooldownTable`, `DefenseThreatSignature` recipe, `AssetStrategicRole` flags + `AssetRoleCatalog` (15 named anchors) + `AssetRoleScorer` (GrandStrategyProfile-derived weights), `DefenseIntentRuntime` vanilla extractor, daily strategic review cadence (replaces weekly `WeeklyCadence` with `DailyCadence`), `[DefenseIntent]` observer telemetry on signature change, #25 `CheckForDefensiveOperationsCandidateFilterPatch` (Prefix snapshot/filter + Postfix re-issue), shipped #4 `DefensiveOpsPatch` priority/invariant tightening, and `CoastalDefenseCustomOrderRunner` for active landings + relaxed-filter guards. Capital defense remains exclusively owned by #4. Console tests pass; `./build.sh` passes with 0 warnings / 0 errors. DLL deployed and SHA-256 verified (`<insert hash>`). Runtime smoke confirmed `[once:defense-intent-filter]`, `[DefenseIntent]` posture lines on signature change, `custom-order` and `reverted=cross-map` lines under realistic Union sea-invasion attempts.
```

(Replace `<insert hash>` with the actual `sha256sum` from Task 19.)

- [ ] **Step 3: `docs/handoff.md` — update "Active workstream" and "Next concrete action"**

Replace the current "Active workstream" line under the "At a glance" table with:

```markdown
| **Active workstream** | Defense intent ledger Slice 1+2 shipped on `main`. Defense responses are scored daily, coastal/river assets carry strategic-role tags, three patch surfaces (#25 candidate-filter Prefix/Postfix, custom defensive movement order runner) enforce locality discipline, and capital defense remains owned by shipped #4. Next slices: Slice 3 guard-budget tuning from runtime telemetry, refined `AssetRoleCatalog` keys from observed asset names, and a longer-running smoke pass to confirm Recovered cooldown behavior across multiple invasions. |
```

Replace "Next concrete action" with the new backlog:

```markdown
**Next concrete action**: Defense intent ledger Slice 1+2 shipped. Strategic core verified working in-game. Deployed DLL SHA-256 is `<insert hash>`. Backlog:

1. **Slice 3 guard-budget tuning** — observe `[DefenseIntent]` telemetry across a full campaign, then adjust `GuardBudgetFraction` (default 0.10), `cooldownDays` (default 4), and aggregator thresholds (0.75 / 1.0 / 1.25) per faction/era from real telemetry.
2. **Catalog refinement** — collect `[DefenseIntent:asset] missing-role` lines from runtime smoke and add the actual GTCW asset names to `AssetRoleCatalog`.
3. **Recovered persistence soak** — long campaign run to confirm cooldown behavior across multiple sequential invasions, including overlapping threats keyed by different signatures.
4. (existing items continue here)
```

- [ ] **Step 4: Commit**

```bash
git add docs/patch-catalog.md docs/handoff.md
git commit -m "docs: ship defense intent ledger Slice 1+2"
```

### Task 22: Update strategic-brain umbrella spec + MEMORY.md cadence reference

**Files:**
- Modify: `docs/superpowers/specs/2026-05-02-strategic-brain-design.md`
- Modify: `MEMORY.md`

- [ ] **Step 1: Update umbrella spec cadence**

In `docs/superpowers/specs/2026-05-02-strategic-brain-design.md`, find the line that reads "Weekly + event-triggered cadence" (or similar) and replace with:

```markdown
**Daily + event-triggered cadence.** CIC strategic review runs on first valid date and every in-game day. Event triggers mark plans dirty; the next daily review processes the dirty bit. Monthly remains only the visible heartbeat/checkpoint boundary. (Defense Intent Ledger slice migrated the cadence from weekly to daily on 2026-05-05; see `docs/superpowers/specs/2026-05-05-defense-intent-ledger-design.md`.)
```

If multiple "weekly" references exist in the spec, update each so internal references remain coherent. Skim the spec end-to-end after edits to confirm.

- [ ] **Step 2: Update `MEMORY.md`**

In `MEMORY.md`, find the "Cross-cutting standing rules" section and update the time-base line if it mentions weekly:

Locate:

```markdown
- **Time-base discipline:** anything that re-evaluates strategy uses real game-month boundaries, not unscaled wallclock.
```

(unchanged — month-boundaries discipline is still correct for the visible heartbeat).

If a line mentions "weekly" cadence, replace with "daily" — but this MEMORY.md was written before the cadence change, so a global grep is the safest check:

```bash
grep -n "weekly\|Weekly" MEMORY.md
```

For each hit that refers to operational cadence (not the reasoning about weeks-as-time), update to daily. Leave commentary about why the previous weekly cadence was wrong intact for historical context.

- [ ] **Step 3: Commit**

```bash
git add docs/superpowers/specs/2026-05-02-strategic-brain-design.md MEMORY.md
git commit -m "docs: align umbrella spec and memory with daily cadence"
```

---

## Self-review summary

- **Spec coverage:** Each spec section maps to at least one task: cadence (Tasks 1-3), asset metadata (Tasks 4-7), pure types (Tasks 8-12), runtime extraction (Task 13), Slice 1 observer (Task 14), Slice 2 patch surfaces (Tasks 16-18), coexistence with #4 (Task 17, 20), tests (Task 12), logging (Task 14, 16, 18), acceptance criteria (Task 19 smoke), open implementation questions (resolved during Tasks 13-19 smoke).
- **Placeholders:** None. The 13 fixture tests in Task 12 are described by spec scenario + assertion table rather than each body inline; the executor implements them red-by-red. This is explicit, not a placeholder.
- **Type consistency:** `DefenseIntentInput`, `DefenseThreatSource`, `DefenseCandidate`, `DefenseSuppression`, `DefenseResponse`, `DefenseIntentLedgerOutput`, `DefenseCooldownTable`, `DefensePackageResult`, `AssetStrategicRole`, `DefensePosture`, `ThreatScale`, `CandidateTier` — all defined in Tasks 4, 8, 10, 11, 12 and used consistently in Tasks 13-18. `DefenseThreatSignature` static methods (`ForSeaInvasion` / `ForRaid` / `ForAsset`) referenced consistently. Patch ordinal #25 is single-claimed by Task 16.
- **TDD discipline:** Phase A-C tasks all start with failing tests. Phase D-E tasks are reflection/Harmony-heavy and rely on smoke (Tasks 15, 19) since Unity-bound code is not console-testable; this matches the project's existing convention.
- **Frequent commits:** every task ends in a commit. Task 12 explicitly permits per-fixture commits.
