# Strategic Resilience Director Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the Strategic Resilience Director per `docs/superpowers/specs/2026-05-05-strategic-resilience-director-design.md` — a cached, low-frequency posture publisher that turns the strategic system into a war-length pressure model with vanilla-bound collapse thresholds, contact-backed probe escalation, and personality-preserved threshold modifiers.

**Architecture:** Pure-logic ledgers in `src/WhiskeyRealism/Strategic/` consumed by a single `StrategicResilienceDirector` that publishes one `DirectorPosture` per alliance. Director rides on top of existing executors (CIC, ledgers, vanilla). Read-only-from-Harmony-patches invariant preserved. Required Fixes (RecomputePressure reset, PhaseTruthLedger, ContactEvidenceLedger, vanilla offensive-availability wrapper, theater pressure helper) wire first; architectural cleanup (delete TheaterCommander, single force-availability source, single probe-state source, BattleHistoryQuery helper) lands next; Director on top.

**Tech Stack:** C# netstandard2.1, BepInEx 5.4.x x64, HarmonyX, Newtonsoft.Json. Pure tests run in console harness (`tests/WhiskeyRealism.Tests/`); reflection-heavy paths smoke in-game.

## Integration Surfaces (all in scope this slice)

The Director publishes one posture per alliance and modulates these subsystems via small bias fields:

| Surface | Knob | Wired in |
|---|---|---|
| `OperationalProbeOptions` | 5 personality-clamped modifiers | Task 12 + 14 |
| `CIC.ReviewPlan` | `PhaseTruthLedger` consultation | Task 13 |
| `FrontLedgerOptions` (transfer/min-hold) | `MinimumHoldRatioModifier`, `ConcessionRatioModifier` | Task 17 |
| `FormationDirectiveLedger` directive gates | `RecoverFloorModifier`, `MassRatioModifier` | Task 18 |
| Fiscal + construction scorers | `SupplyConstructionBias`, `LogisticsBias`, `ExpansionDamper` | Task 19 |
| `DefenseIntentLedger` + #4 capital path | `GuardBudgetFractionModifier`, `CapitalDefenseBudgetModifier` | Task 20 |
| Telemetry | `[CampaignPace]`, `[CollapseRisk]` | Task 16 |

Each bias is bounded: where personality already adjusts the same field, ±50% of the absolute personality delta (per spec's "Personality" clamp); where personality does not, a fixed cap documented in the relevant task.

---

## Operating Reference (read once before starting)

- **Build:** `./build.sh` → `dist/WhiskeyRealism.dll`
- **Pure tests:** `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- **Test naming:** lowercase descriptor in `tests` array → PascalCase method. Use `AssertEqual<T>(expected, actual, label)`, `AssertTrue(condition, message)`, `AssertContains(haystack, needle, label)`.
- **csproj:** every new file under `src/WhiskeyRealism/Strategic/` consumed by tests requires a matching `<Compile Include="..\..\src\WhiskeyRealism\Strategic\X.cs" Link="X.cs" />` line in `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`. Same for deletions.
- **Deploy** (game must be closed):
  ```bash
  cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
  sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
  ```
- **Spec:** `docs/superpowers/specs/2026-05-05-strategic-resilience-director-design.md` (582 lines, source of truth for rules)
- **Decompile:** `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`. Regenerate via `docs/findings.md` if `/tmp` was wiped. Key anchor: `AICampaign.IsUnitAvailableForOffensiveOperations` at line 14080.
- **Conventions:** one concern per file. Pure ledger types in `Strategic/`. Reflection wrapped in try/catch + `Plugin.Log.LogWarning` or `OnceLog.Warning`. Never throw from a Harmony patch. Strategic mod state read-only to patches.
- **Commit style:** match existing `git log --oneline -10` (Conventional Commits — `feat:`, `fix:`, `refactor:`).

---

## Task 1: BattleHistoryQuery helper

**Files:**
- Create: `src/WhiskeyRealism/Strategic/BattleHistoryQuery.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add csproj include**

In `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, add alongside existing `Strategic/*.cs` Compile Include lines (alphabetical with neighbours is fine):

```xml
    <Compile Include="..\..\src\WhiskeyRealism\Strategic\BattleHistoryQuery.cs" Link="BattleHistoryQuery.cs" />
```

- [ ] **Step 2: Write failing tests**

In `tests/WhiskeyRealism.Tests/Program.cs`, add three test names to the `tests` array:

```csharp
("battle history query matches inside spatial and date window", BattleHistoryQueryMatchesInsideSpatialAndDateWindow),
("battle history query rejects outside spatial window", BattleHistoryQueryRejectsOutsideSpatialWindow),
("battle history query rejects outside date window", BattleHistoryQueryRejectsOutsideDateWindow),
```

Add the methods (place near other Strategic test methods):

```csharp
private static void BattleHistoryQueryMatchesInsideSpatialAndDateWindow()
{
    var history = new List<BattleHistoryRecord>
    {
        new BattleHistoryRecord { BattleName = "near", PositionX = 100f, PositionZ = 100f, Day = 5, Month = 6, Year = 1862 }
    };
    int currentDay = 1862 * 372 + 6 * 31 + 8;
    var hits = new List<BattleHistoryRecord>(BattleHistoryQuery.Near(
        history, new UnityEngine.Vector3(105f, 0f, 105f), 50f, currentDay, withinDays: 7));
    AssertEqual(1, hits.Count, "expected 1 in-window hit");
}

private static void BattleHistoryQueryRejectsOutsideSpatialWindow()
{
    var history = new List<BattleHistoryRecord>
    {
        new BattleHistoryRecord { BattleName = "far", PositionX = 1000f, PositionZ = 1000f, Day = 5, Month = 6, Year = 1862 }
    };
    int currentDay = 1862 * 372 + 6 * 31 + 6;
    var hits = new List<BattleHistoryRecord>(BattleHistoryQuery.Near(
        history, new UnityEngine.Vector3(0f, 0f, 0f), 50f, currentDay, withinDays: 7));
    AssertEqual(0, hits.Count, "expected 0 hits beyond spatial window");
}

private static void BattleHistoryQueryRejectsOutsideDateWindow()
{
    var history = new List<BattleHistoryRecord>
    {
        new BattleHistoryRecord { BattleName = "old", PositionX = 100f, PositionZ = 100f, Day = 5, Month = 6, Year = 1862 }
    };
    int currentDay = 1862 * 372 + 7 * 31 + 5; // ~30 days later
    var hits = new List<BattleHistoryRecord>(BattleHistoryQuery.Near(
        history, new UnityEngine.Vector3(105f, 0f, 105f), 50f, currentDay, withinDays: 7));
    AssertEqual(0, hits.Count, "expected 0 hits beyond date window");
}
```

- [ ] **Step 3: Run tests, confirm three new tests fail**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```
Expected: FAIL with "BattleHistoryQuery type not found" (or similar — type does not exist yet).

- [ ] **Step 4: Implement BattleHistoryQuery**

Create `src/WhiskeyRealism/Strategic/BattleHistoryQuery.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace WhiskeyRealism.Strategic
{
    internal static class BattleHistoryQuery
    {
        public static IEnumerable<BattleHistoryRecord> Near(
            IReadOnlyList<BattleHistoryRecord> history,
            Vector3 position,
            float maxDistance,
            int currentDaySerial,
            int withinDays)
        {
            if (history == null || history.Count == 0) yield break;
            float maxDistSq = maxDistance * maxDistance;
            for (int i = 0; i < history.Count; i++)
            {
                var record = history[i];
                if (record == null) continue;
                int recordDay = record.Year * 372 + record.Month * 31 + record.Day;
                if (currentDaySerial - recordDay > withinDays || recordDay > currentDaySerial) continue;
                float dx = record.PositionX - position.x;
                float dz = record.PositionZ - position.z;
                if (dx * dx + dz * dz > maxDistSq) continue;
                yield return record;
            }
        }
    }
}
```

- [ ] **Step 5: Run tests, confirm pass**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```
Expected: all three new tests PASS, full suite green.

- [ ] **Step 6: Build the plugin**

```bash
./build.sh
```
Expected: 0 warnings, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/WhiskeyRealism/Strategic/BattleHistoryQuery.cs tests/WhiskeyRealism.Tests/Program.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "feat: add BattleHistoryQuery spatial+date helper for Director ledgers"
```

---

## Task 2: TheaterPressureView helper

**Files:**
- Create: `src/WhiskeyRealism/Strategic/TheaterPressureView.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Read FrontSectorLedger to confirm sector→theater mapping shape**

```bash
grep -n "Theater\|Posture\|StrengthRatio\|public" src/WhiskeyRealism/Strategic/FrontSectorLedger.cs | head -30
```
Note: sector records carry a `Theater` field (or are mapped via `TheaterClassifier`). The view aggregates by theater bucket; if a sector has no theater, treat as `Theater.Unknown`.

- [ ] **Step 2: Add csproj include**

```xml
    <Compile Include="..\..\src\WhiskeyRealism\Strategic\TheaterPressureView.cs" Link="TheaterPressureView.cs" />
```

- [ ] **Step 3: Write failing test**

Add to `tests` array:
```csharp
("theater pressure view sums own and enemy strength per theater", TheaterPressureViewSumsOwnAndEnemyPerTheater),
```

Add the method:
```csharp
private static void TheaterPressureViewSumsOwnAndEnemyPerTheater()
{
    var ledger = new FrontSectorLedger();
    ledger.UpsertSector(new FrontSector { Key = "RichmondCorridor", Theater = Theater.East, OwnStrength = 8000f, EnemyStrength = 6000f });
    ledger.UpsertSector(new FrontSector { Key = "ShenandoahValley", Theater = Theater.East, OwnStrength = 2000f, EnemyStrength = 1000f });
    ledger.UpsertSector(new FrontSector { Key = "Vicksburg",        Theater = Theater.West, OwnStrength = 4000f, EnemyStrength = 5000f });

    var view = TheaterPressureView.From(ledger);

    AssertEqual(10000f, view.OwnStrengthByTheater[Theater.East], "east own");
    AssertEqual(7000f,  view.EnemyStrengthByTheater[Theater.East], "east enemy");
    AssertEqual(4000f,  view.OwnStrengthByTheater[Theater.West], "west own");
    AssertEqual(5000f,  view.EnemyStrengthByTheater[Theater.West], "west enemy");
}
```

> Note: `FrontSectorLedger.UpsertSector` and the `FrontSector` shape may differ in shipped code. If the test setup helpers in Program.cs (e.g. `BuildLedger()` near line 218) already produce sectors, prefer those instead of inventing a new constructor surface. **Do not introduce new public mutators on `FrontSectorLedger` — read its current API and adapt the test.**

- [ ] **Step 4: Run, confirm fail**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```
Expected: FAIL with "TheaterPressureView not found" or similar.

- [ ] **Step 5: Implement**

Create `src/WhiskeyRealism/Strategic/TheaterPressureView.cs`:

```csharp
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public sealed class TheaterPressureView
    {
        public Dictionary<Theater, float> OwnStrengthByTheater = new Dictionary<Theater, float>();
        public Dictionary<Theater, float> EnemyStrengthByTheater = new Dictionary<Theater, float>();

        public float NormalizedPressure(Theater theater)
        {
            EnemyStrengthByTheater.TryGetValue(theater, out float enemy);
            OwnStrengthByTheater.TryGetValue(theater, out float own);
            float total = own + enemy;
            return total <= 1f ? 0f : enemy / total;
        }

        public static TheaterPressureView From(FrontSectorLedger ledger)
        {
            var view = new TheaterPressureView();
            if (ledger == null) return view;
            foreach (var sector in ledger.Sectors)
            {
                if (sector == null) continue;
                Accumulate(view.OwnStrengthByTheater, sector.Theater, sector.OwnStrength);
                Accumulate(view.EnemyStrengthByTheater, sector.Theater, sector.EnemyStrength);
            }
            return view;
        }

        private static void Accumulate(Dictionary<Theater, float> bucket, Theater theater, float value)
        {
            bucket.TryGetValue(theater, out float existing);
            bucket[theater] = existing + value;
        }
    }
}
```

> If `FrontSectorLedger` does not expose `Sectors` as `IEnumerable<FrontSector>`, add a `public IEnumerable<FrontSector> Sectors => _sectors;` accessor *only* if no equivalent exists. Do not duplicate.

- [ ] **Step 6: Run, confirm pass**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```
Expected: all PASS.

- [ ] **Step 7: Build**

```bash
./build.sh
```

- [ ] **Step 8: Commit**

```bash
git add src/WhiskeyRealism/Strategic/TheaterPressureView.cs tests/WhiskeyRealism.Tests/Program.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "feat: add TheaterPressureView aggregating FrontSectorLedger by theater"
```

---

## Task 3: Fix RecomputePressure leak (Required Fix #1)

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/FormationDirectiveLedger.cs:301-330`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Write failing idempotency test**

Add to `tests` array:
```csharp
("recompute pressure resets counters before counting", RecomputePressureResetsCountersBeforeCounting),
```

Add the method (model on existing `FormationDirectiveSummaryChangesWhenAssignmentChanges` setup):

```csharp
private static void RecomputePressureResetsCountersBeforeCounting()
{
    var snap = new FormationSnapshot
    {
        UnitKey = "U1",
        AllianceId = 0,
        Level = FormationLevel.Army,
        AreaKey = "RichmondCorridor",
        SectorKey = "RichmondCorridor",
        IsTopStrategicFormation = true,
        CanReceiveDirectDirective = true,
        CanReceiveDirectMovement = true,
        Morale = 0.2f, Readiness = 0.2f, Supply = 0.2f, MinimumAmmo = 0.2f, // forces Recover
        GroupStrengthActive = 8000f
    };
    var ledger = FormationDirectiveLedger.Build(new[] { snap }, EraStage.Decisive1863, "RichmondCorridor");

    int recoverAfterBuild = ledger.Pressure.RecoverCount;
    AssertEqual(1, recoverAfterBuild, "recover after build");

    // Apply 100 probe overlays. Each calls RecomputePressure internally.
    for (int i = 0; i < 100; i++)
    {
        ledger.ApplyOperationalProbe(new OperationalProbeOutput
        {
            Decision = OperationalProbeDecision.Probe,
            SelectedUnitKey = "U1",
            Reason = "test"
        });
    }

    AssertTrue(ledger.Pressure.RecoverCount <= 1,
        "RecoverCount must be bounded by Assignments.Count after 100 overlays — was " + ledger.Pressure.RecoverCount);
    AssertTrue(ledger.Pressure.LowSupplyCount <= 1, "LowSupplyCount bounded");
    AssertTrue(ledger.Pressure.LowAmmoCount <= 1,   "LowAmmoCount bounded");
}
```

- [ ] **Step 2: Run, confirm fail**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```
Expected: FAIL with "RecoverCount must be bounded ... was 101" or similar (counter is monotonically incrementing).

- [ ] **Step 3: Fix RecomputePressure**

In `src/WhiskeyRealism/Strategic/FormationDirectiveLedger.cs`, replace the body of `RecomputePressure` (currently at line 301) so it resets `Pressure` first:

```csharp
private void RecomputePressure()
{
    Pressure = new FormationPressureSummary();
    var areaScores = new Dictionary<string, int>();
    foreach (var assignment in _ordered)
    {
        if (assignment.Supply < 0.35f)
        {
            Pressure.LowSupplyCount++;
            AddAreaPressure(areaScores, assignment.AreaKey);
        }
        if (assignment.Ammo < 0.35f)
        {
            Pressure.LowAmmoCount++;
            AddAreaPressure(areaScores, assignment.AreaKey);
        }
        if (assignment.Directive == FormationDirective.Recover) Pressure.RecoverCount++;
        if (assignment.Directive == FormationDirective.Guard)   Pressure.GuardCount++;
        if (assignment.Directive == FormationDirective.Mass)    Pressure.MassCount++;
    }

    int best = 0;
    foreach (var kv in areaScores)
    {
        if (kv.Value > best)
        {
            best = kv.Value;
            Pressure.TopSupplyAreaKey = kv.Key;
        }
    }
}
```

- [ ] **Step 4: Run, confirm pass**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```
Expected: all PASS, including the new idempotency test.

- [ ] **Step 5: Build**

```bash
./build.sh
```

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Strategic/FormationDirectiveLedger.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "fix: reset FormationPressureSummary at the start of RecomputePressure"
```

---

## Task 4: PhaseTruthLedger (Required Fix #2)

**Files:**
- Create: `src/WhiskeyRealism/Strategic/PhaseTruthLedger.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add csproj include**

```xml
    <Compile Include="..\..\src\WhiskeyRealism\Strategic\PhaseTruthLedger.cs" Link="PhaseTruthLedger.cs" />
```

- [ ] **Step 2: Write failing tests**

Add to `tests` array:
```csharp
("phase truth advances when target accomplished", PhaseTruthAdvancesWhenTargetAccomplished),
("phase truth replans when objective unavailable", PhaseTruthReplansWhenObjectiveUnavailable),
("phase truth recovers when force below threshold", PhaseTruthRecoversWhenForceBelowThreshold),
("phase truth deadline expired advances or replans", PhaseTruthDeadlineExpiredAdvancesOrReplans),
("phase truth no contact stays continue", PhaseTruthNoContactStaysContinue),
```

Add the methods:

```csharp
private static void PhaseTruthAdvancesWhenTargetAccomplished()
{
    var input = new PhaseTruthInput
    {
        Plan = new OperationalPlan { Phases = { new Phase { TargetObjectiveId = 29, DeadlineMonth = 12, DeadlineYear = 1862 } } },
        TargetAccomplished = true,
        ObjectiveAvailable = true,
        TargetSectorOwnStrength = 10000f,
        RequiredForce = 5000f,
        CurrentMonth = 6, CurrentYear = 1862
    };
    var output = PhaseTruthLedger.Evaluate(input);
    AssertEqual(PhaseTruthVerdict.TargetAccomplished, output.Verdict);
    AssertEqual(PhaseTruthAction.Advance, output.RecommendedAction);
}

private static void PhaseTruthReplansWhenObjectiveUnavailable()
{
    var input = new PhaseTruthInput
    {
        Plan = new OperationalPlan { Phases = { new Phase { TargetObjectiveId = 29, DeadlineMonth = 12, DeadlineYear = 1862 } } },
        TargetAccomplished = false,
        ObjectiveAvailable = false,
        TargetSectorOwnStrength = 10000f,
        RequiredForce = 5000f,
        CurrentMonth = 6, CurrentYear = 1862
    };
    var output = PhaseTruthLedger.Evaluate(input);
    AssertEqual(PhaseTruthVerdict.ObjectiveUnavailable, output.Verdict);
    AssertEqual(PhaseTruthAction.Replan, output.RecommendedAction);
}

private static void PhaseTruthRecoversWhenForceBelowThreshold()
{
    var input = new PhaseTruthInput
    {
        Plan = new OperationalPlan { Phases = { new Phase { TargetObjectiveId = 29, DeadlineMonth = 12, DeadlineYear = 1862 } } },
        TargetAccomplished = false,
        ObjectiveAvailable = true,
        TargetSectorOwnStrength = 1000f,
        RequiredForce = 5000f,
        CurrentMonth = 6, CurrentYear = 1862
    };
    var output = PhaseTruthLedger.Evaluate(input);
    AssertEqual(PhaseTruthVerdict.ForceBelowThreshold, output.Verdict);
    AssertEqual(PhaseTruthAction.Recover, output.RecommendedAction);
}

private static void PhaseTruthDeadlineExpiredAdvancesOrReplans()
{
    var input = new PhaseTruthInput
    {
        Plan = new OperationalPlan { Phases = { new Phase { TargetObjectiveId = 29, DeadlineMonth = 1, DeadlineYear = 1862 } } },
        TargetAccomplished = false,
        ObjectiveAvailable = true,
        TargetSectorOwnStrength = 10000f,
        RequiredForce = 5000f,
        CurrentMonth = 6, CurrentYear = 1862
    };
    var output = PhaseTruthLedger.Evaluate(input);
    AssertEqual(PhaseTruthVerdict.DeadlineExpired, output.Verdict);
    AssertTrue(output.RecommendedAction == PhaseTruthAction.Advance ||
               output.RecommendedAction == PhaseTruthAction.Replan,
               "deadline expired should advance or replan");
}

private static void PhaseTruthNoContactStaysContinue()
{
    var input = new PhaseTruthInput
    {
        Plan = new OperationalPlan { Phases = { new Phase { TargetObjectiveId = 29, DeadlineMonth = 12, DeadlineYear = 1862 } } },
        TargetAccomplished = false,
        ObjectiveAvailable = true,
        TargetSectorOwnStrength = 10000f,
        RequiredForce = 5000f,
        TargetEngagedRecently = false,
        CurrentMonth = 6, CurrentYear = 1862
    };
    var output = PhaseTruthLedger.Evaluate(input);
    AssertEqual(PhaseTruthVerdict.Valid, output.Verdict);
    AssertEqual(PhaseTruthAction.Continue, output.RecommendedAction);
}
```

- [ ] **Step 3: Run, confirm fail**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```
Expected: FAIL with type-not-found for `PhaseTruthInput` / `PhaseTruthLedger`.

- [ ] **Step 4: Implement**

Create `src/WhiskeyRealism/Strategic/PhaseTruthLedger.cs`:

```csharp
namespace WhiskeyRealism.Strategic
{
    public enum PhaseTruthVerdict
    {
        Valid,
        TargetAccomplished,
        ObjectiveUnavailable,
        TargetEngaged,
        ForceBelowThreshold,
        DeadlineExpired,
        MissingTargetPosition
    }

    public enum PhaseTruthAction
    {
        Continue,
        Advance,
        Recover,
        Fallback,
        Replan
    }

    public sealed class PhaseTruthInput
    {
        public OperationalPlan Plan;
        public bool TargetAccomplished;
        public bool ObjectiveAvailable;
        public bool TargetPositionResolves = true;
        public bool TargetEngagedRecently;
        public float TargetSectorOwnStrength;
        public float RequiredForce;
        public int CurrentMonth;
        public int CurrentYear;
    }

    public sealed class PhaseTruthOutput
    {
        public PhaseTruthVerdict Verdict;
        public PhaseTruthAction RecommendedAction;
        public string Reason;
    }

    public static class PhaseTruthLedger
    {
        public static PhaseTruthOutput Evaluate(PhaseTruthInput input)
        {
            var output = new PhaseTruthOutput();
            if (input?.Plan?.CurrentPhase == null)
            {
                output.Verdict = PhaseTruthVerdict.MissingTargetPosition;
                output.RecommendedAction = PhaseTruthAction.Replan;
                output.Reason = "no-active-phase";
                return output;
            }

            var phase = input.Plan.CurrentPhase;

            if (input.TargetAccomplished)
            {
                output.Verdict = PhaseTruthVerdict.TargetAccomplished;
                output.RecommendedAction = PhaseTruthAction.Advance;
                output.Reason = "target-accomplished";
                return output;
            }

            if (!input.ObjectiveAvailable || !input.TargetPositionResolves)
            {
                output.Verdict = input.ObjectiveAvailable
                    ? PhaseTruthVerdict.MissingTargetPosition
                    : PhaseTruthVerdict.ObjectiveUnavailable;
                output.RecommendedAction = PhaseTruthAction.Replan;
                output.Reason = output.Verdict.ToString();
                return output;
            }

            if (input.RequiredForce > 0f && input.TargetSectorOwnStrength < input.RequiredForce)
            {
                output.Verdict = PhaseTruthVerdict.ForceBelowThreshold;
                output.RecommendedAction = PhaseTruthAction.Recover;
                output.Reason = "force-below-threshold";
                return output;
            }

            bool deadlinePassed =
                input.CurrentYear > phase.DeadlineYear ||
                (input.CurrentYear == phase.DeadlineYear && input.CurrentMonth > phase.DeadlineMonth);

            if (deadlinePassed)
            {
                output.Verdict = PhaseTruthVerdict.DeadlineExpired;
                bool hasNextPhase = input.Plan.CurrentPhaseIndex + 1 < input.Plan.Phases.Count;
                output.RecommendedAction = hasNextPhase ? PhaseTruthAction.Advance : PhaseTruthAction.Replan;
                output.Reason = "deadline-expired";
                return output;
            }

            if (input.TargetEngagedRecently)
            {
                output.Verdict = PhaseTruthVerdict.TargetEngaged;
                output.RecommendedAction = PhaseTruthAction.Continue;
                output.Reason = "target-engaged-let-contact-decide";
                return output;
            }

            output.Verdict = PhaseTruthVerdict.Valid;
            output.RecommendedAction = PhaseTruthAction.Continue;
            output.Reason = "phase-valid";
            return output;
        }
    }
}
```

- [ ] **Step 5: Run, confirm pass**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```
Expected: all PASS.

- [ ] **Step 6: Build**

```bash
./build.sh
```

- [ ] **Step 7: Commit**

```bash
git add src/WhiskeyRealism/Strategic/PhaseTruthLedger.cs tests/WhiskeyRealism.Tests/Program.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "feat: add PhaseTruthLedger so stale objectives stop driving plans"
```

---

## Task 5: ContactEvidenceLedger (Required Fix #3)

**Files:**
- Create: `src/WhiskeyRealism/Strategic/ContactEvidenceLedger.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add csproj include**

```xml
    <Compile Include="..\..\src\WhiskeyRealism\Strategic\ContactEvidenceLedger.cs" Link="ContactEvidenceLedger.cs" />
```

- [ ] **Step 2: Write failing tests**

Add to `tests` array:
```csharp
("contact evidence no contact when zero enemy and no battles", ContactEvidenceNoContactWhenZeroEnemyAndNoBattles),
("contact evidence enemy reacted on strength rise", ContactEvidenceEnemyReactedOnStrengthRise),
("contact evidence skirmish observed near target", ContactEvidenceSkirmishObservedNearTarget),
("contact evidence battle observed lost is overmatched", ContactEvidenceBattleObservedLostIsOvermatched),
("contact evidence favorable contact requires presence and ratio", ContactEvidenceFavorableRequiresPresenceAndRatio),
```

Add the methods:

```csharp
private static void ContactEvidenceNoContactWhenZeroEnemyAndNoBattles()
{
    var input = new ContactEvidenceInput
    {
        TargetPosition = new UnityEngine.Vector3(100f, 0f, 100f),
        CurrentEnemyStrength = 0f,
        CurrentFriendlyStrength = 8000f,
        PreviousObservedEnemyStrength = 0f,
        EnemyReactionMultiplier = 1.45f,
        EscalateFriendlyRatio = 1.8f,
        WithdrawFriendlyRatio = 0.55f,
        BattleHistory = new List<BattleHistoryRecord>(),
        SpatialMaxDistance = 50f,
        CurrentDaySerial = 1862 * 372 + 6 * 31 + 6
    };
    var output = ContactEvidenceLedger.Build(input);
    AssertEqual(ContactEvidence.NoContact, output.Evidence);
    AssertTrue(!output.AllowsEscalation, "no-contact must not allow escalation");
}

private static void ContactEvidenceEnemyReactedOnStrengthRise()
{
    var input = new ContactEvidenceInput
    {
        TargetPosition = new UnityEngine.Vector3(100f, 0f, 100f),
        CurrentEnemyStrength = 6000f,
        CurrentFriendlyStrength = 7000f,
        PreviousObservedEnemyStrength = 3000f,
        EnemyReactionMultiplier = 1.45f,
        EscalateFriendlyRatio = 1.8f,
        WithdrawFriendlyRatio = 0.55f,
        BattleHistory = new List<BattleHistoryRecord>(),
        SpatialMaxDistance = 50f,
        CurrentDaySerial = 1862 * 372 + 6 * 31 + 6
    };
    var output = ContactEvidenceLedger.Build(input);
    AssertEqual(ContactEvidence.EnemyReacted, output.Evidence);
}

private static void ContactEvidenceSkirmishObservedNearTarget()
{
    var input = new ContactEvidenceInput
    {
        TargetPosition = new UnityEngine.Vector3(100f, 0f, 100f),
        CurrentEnemyStrength = 1000f,
        CurrentFriendlyStrength = 2000f,
        PreviousObservedEnemyStrength = 1000f,
        EnemyReactionMultiplier = 1.45f,
        EscalateFriendlyRatio = 1.8f,
        WithdrawFriendlyRatio = 0.55f,
        BattleHistory = new List<BattleHistoryRecord>
        {
            new BattleHistoryRecord {
                BattleName = "skirmish", PositionX = 105f, PositionZ = 105f,
                Day = 4, Month = 6, Year = 1862, BattleResultType = 0 // not major
            }
        },
        SpatialMaxDistance = 50f,
        CurrentDaySerial = 1862 * 372 + 6 * 31 + 6
    };
    var output = ContactEvidenceLedger.Build(input);
    AssertEqual(ContactEvidence.SkirmishObserved, output.Evidence);
}

private static void ContactEvidenceBattleObservedLostIsOvermatched()
{
    int daySerial = 1862 * 372 + 6 * 31 + 6;
    var input = new ContactEvidenceInput
    {
        TargetPosition = new UnityEngine.Vector3(100f, 0f, 100f),
        ObservingAllianceId = 0,
        CurrentEnemyStrength = 5000f,
        CurrentFriendlyStrength = 6000f,
        PreviousObservedEnemyStrength = 5000f,
        EnemyReactionMultiplier = 1.45f,
        EscalateFriendlyRatio = 1.8f,
        WithdrawFriendlyRatio = 0.55f,
        BattleHistory = new List<BattleHistoryRecord>
        {
            new BattleHistoryRecord {
                BattleName = "majorlost", PositionX = 105f, PositionZ = 105f,
                Day = 4, Month = 6, Year = 1862, BattleResultType = 1 /* major */,
                AllianceWon = 1 // observer is alliance 0, so this is a loss
            }
        },
        SpatialMaxDistance = 50f,
        CurrentDaySerial = daySerial
    };
    var output = ContactEvidenceLedger.Build(input);
    AssertEqual(ContactEvidence.OvermatchedContact, output.Evidence);
    AssertTrue(!output.AllowsEscalation, "overmatched must not allow escalation");
}

private static void ContactEvidenceFavorableRequiresPresenceAndRatio()
{
    int daySerial = 1862 * 372 + 6 * 31 + 6;
    var input = new ContactEvidenceInput
    {
        TargetPosition = new UnityEngine.Vector3(100f, 0f, 100f),
        ObservingAllianceId = 0,
        CurrentEnemyStrength = 1000f,
        CurrentFriendlyStrength = 2500f,
        PreviousObservedEnemyStrength = 1000f,
        EnemyReactionMultiplier = 1.45f,
        EscalateFriendlyRatio = 1.8f,
        WithdrawFriendlyRatio = 0.55f,
        BattleHistory = new List<BattleHistoryRecord>(),
        SpatialMaxDistance = 50f,
        CurrentDaySerial = daySerial
    };
    var output = ContactEvidenceLedger.Build(input);
    AssertEqual(ContactEvidence.FavorableContact, output.Evidence);
    AssertTrue(output.AllowsEscalation, "favorable contact allows escalation");
}
```

- [ ] **Step 3: Run, confirm fail**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```
Expected: type-not-found.

- [ ] **Step 4: Implement**

Create `src/WhiskeyRealism/Strategic/ContactEvidenceLedger.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WhiskeyRealism.Strategic
{
    public enum ContactEvidence
    {
        NoContact,
        EnemyPresent,
        EnemyReacted,
        SkirmishObserved,
        BattleObserved,
        FavorableContact,
        OvermatchedContact
    }

    public sealed class ContactEvidenceInput
    {
        public int ObservingAllianceId;
        public Vector3 TargetPosition;
        public float CurrentEnemyStrength;
        public float CurrentFriendlyStrength;
        public float PreviousObservedEnemyStrength;
        public float EnemyReactionMultiplier;
        public float EscalateFriendlyRatio;
        public float WithdrawFriendlyRatio;
        public IReadOnlyList<BattleHistoryRecord> BattleHistory;
        public float SpatialMaxDistance;
        public int CurrentDaySerial;
        public int WithinDays = 7;
    }

    public sealed class ContactEvidenceOutput
    {
        public ContactEvidence Evidence;
        public bool AllowsEscalation;
        public string Reason;
    }

    public static class ContactEvidenceLedger
    {
        public static ContactEvidenceOutput Build(ContactEvidenceInput input)
        {
            var output = new ContactEvidenceOutput();
            if (input == null) return Reject(output, ContactEvidence.NoContact, "missing-input");

            float ratio = input.CurrentFriendlyStrength /
                          Math.Max(1f, input.CurrentEnemyStrength);

            BattleHistoryRecord majorNearby = null;
            BattleHistoryRecord minorNearby = null;
            if (input.BattleHistory != null)
            {
                foreach (var record in BattleHistoryQuery.Near(
                    input.BattleHistory,
                    input.TargetPosition,
                    input.SpatialMaxDistance,
                    input.CurrentDaySerial,
                    input.WithinDays))
                {
                    if (record.IsMajorResult) { majorNearby = record; break; }
                    if (minorNearby == null) minorNearby = record;
                }
            }

            if (ratio <= input.WithdrawFriendlyRatio)
                return Reject(output, ContactEvidence.OvermatchedContact, "ratio-overmatched");

            if (majorNearby != null && majorNearby.AllianceWon != input.ObservingAllianceId)
                return Reject(output, ContactEvidence.OvermatchedContact, "battle-lost");

            if (input.CurrentEnemyStrength <= 0f && majorNearby == null && minorNearby == null)
                return Reject(output, ContactEvidence.NoContact, "no-enemy-no-battles");

            float prior = Math.Max(1f, input.PreviousObservedEnemyStrength);
            if (input.CurrentEnemyStrength >= prior * input.EnemyReactionMultiplier &&
                ratio < input.EscalateFriendlyRatio)
                return Reject(output, ContactEvidence.EnemyReacted, "enemy-reaction");

            if (majorNearby != null && majorNearby.AllianceWon == input.ObservingAllianceId)
            {
                output.Evidence = ContactEvidence.BattleObserved;
                output.AllowsEscalation = ratio >= input.EscalateFriendlyRatio;
                output.Reason = output.AllowsEscalation ? "battle-won-favorable" : "battle-won-need-ratio";
                return output;
            }

            bool enemyPresent = input.CurrentEnemyStrength > 0f;
            if (enemyPresent && ratio >= input.EscalateFriendlyRatio)
            {
                output.Evidence = ContactEvidence.FavorableContact;
                output.AllowsEscalation = true;
                output.Reason = "favorable-presence";
                return output;
            }

            if (minorNearby != null)
            {
                output.Evidence = ContactEvidence.SkirmishObserved;
                output.AllowsEscalation = ratio >= input.EscalateFriendlyRatio;
                output.Reason = output.AllowsEscalation ? "skirmish-favorable" : "skirmish-need-ratio";
                return output;
            }

            if (enemyPresent)
            {
                output.Evidence = ContactEvidence.EnemyPresent;
                output.AllowsEscalation = false;
                output.Reason = "enemy-present-need-ratio-or-skirmish";
                return output;
            }

            return Reject(output, ContactEvidence.NoContact, "fallthrough");
        }

        private static ContactEvidenceOutput Reject(
            ContactEvidenceOutput output, ContactEvidence evidence, string reason)
        {
            output.Evidence = evidence;
            output.AllowsEscalation = false;
            output.Reason = reason;
            return output;
        }
    }
}
```

- [ ] **Step 5: Run, confirm pass**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```
Expected: all PASS.

- [ ] **Step 6: Build**

```bash
./build.sh
```

- [ ] **Step 7: Commit**

```bash
git add src/WhiskeyRealism/Strategic/ContactEvidenceLedger.cs tests/WhiskeyRealism.Tests/Program.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "feat: add ContactEvidenceLedger so probes need real contact to escalate"
```

---

## Task 6: OffensiveAvailabilityWrapper (Required Fix #4)

**Files:**
- Create: `src/WhiskeyRealism/Strategic/OffensiveAvailabilityWrapper.cs`
- Modify: `src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs`
- Test: smoke only (reflection into vanilla; pure unit tests can't exercise it)

> The wrapper must mirror **all** vanilla gates from `AICampaign.IsUnitAvailableForOffensiveOperations` (decompile 14080). Spec section "Required Fixes In Same Slice" item 4 lists every gate. Prefer reflection into the vanilla method itself — fall back to the mirror only if reflection fails.

- [ ] **Step 1: Create the wrapper**

Create `src/WhiskeyRealism/Strategic/OffensiveAvailabilityWrapper.cs`:

```csharp
using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    internal static class OffensiveAvailabilityWrapper
    {
        private static System.Reflection.MethodInfo _vanillaMethod;
        private static bool _vanillaLookupAttempted;

        public static bool IsAvailable(int aifactionIndex, Regiment unit, Vector3 operationPosition)
        {
            if (unit == null || aifactionIndex < 0) return false;

            try
            {
                var method = ResolveVanillaMethod();
                if (method != null)
                {
                    return (bool)method.Invoke(null, new object[]
                    {
                        aifactionIndex, unit, operationPosition, /*checkexistingoperations*/ true, /*checkforbadweather*/ true
                    });
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("offensive-availability:vanilla",
                    "[OffensiveAvailability] vanilla call failed, falling back to mirror: " + ex.Message);
            }

            return MirrorVanillaGates(aifactionIndex, unit, operationPosition);
        }

        private static System.Reflection.MethodInfo ResolveVanillaMethod()
        {
            if (_vanillaMethod != null) return _vanillaMethod;
            if (_vanillaLookupAttempted) return null;
            _vanillaLookupAttempted = true;
            _vanillaMethod = AccessTools.Method(
                typeof(AICampaign), "IsUnitAvailableForOffensiveOperations",
                new[] { typeof(int), typeof(Regiment), typeof(Vector3), typeof(bool), typeof(bool) });
            if (_vanillaMethod == null)
                OnceLog.Warning("offensive-availability:lookup",
                    "[OffensiveAvailability] AICampaign.IsUnitAvailableForOffensiveOperations not found via reflection — using mirror");
            return _vanillaMethod;
        }

        private static bool MirrorVanillaGates(int aifactionIndex, Regiment unit, Vector3 operationPosition)
        {
            // Mirror of decompile 14080-14157. Every gate must remain in sync if vanilla changes.
            try
            {
                if (CampaignArmyPanel.GetReadinessStep(unit) < 2) return false;
                if (RaidForce.IsRaidUnit(unit)) return false;
                if ((float)unit.groupstrengthactive <= GamePrefs.aiminimumstrengthformovement) return false;
                if (unit.groupmorale <= GamePrefs.aiminimummoraleformovement) return false;

                var faction = AICampaignReflect.GetFaction(aifactionIndex);
                if (faction == null) return false;
                var ftype = faction.GetType();

                if (ListContains(faction, ftype, "unitsinoffensiveoperations", unit)) return false;
                if (ListContains(faction, ftype, "unitsindefensiveoperations", unit)) return false;
                if (ListContains(faction, ftype, "groupstodefendcapital", unit)) return false;
                if (ListContains(faction, ftype, "unitsconstructingsupplydepots", unit)) return false;

                if (FortConstructionOrder.UnitAlreadyConstructing(unit)) return false;
                if (SeaInvasionForce.GetSeaInvasionForceReference(unit) != null) return false;
                if (!AICampaign.UnitIsFightingForce(unit)) return false;

                if (operationPosition != default(Vector3) &&
                    !AICampaign.IsWithinOperationsTheater(unit, operationPosition))
                    return false;

                if (AICampaign.IsUnitTakingTown(unit)) return false;

                // Weather gate intentionally omitted from mirror — accessing the static `weather`
                // field by name is brittle. Vanilla path covers it; if vanilla path fails we accept
                // weather risk.
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("offensive-availability:mirror",
                    "[OffensiveAvailability] mirror failed: " + ex.Message);
                return false;
            }
        }

        private static bool ListContains(object faction, Type ftype, string fieldName, object element)
        {
            var f = AccessTools.Field(ftype, fieldName);
            if (f == null) return false;
            var list = f.GetValue(faction) as System.Collections.IList;
            return list != null && list.Contains(element);
        }
    }
}
```

- [ ] **Step 2: Replace ad-hoc checks in OperationalProbeRuntime.Run**

In `src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs`, the body around lines 95-100 currently reads:

```csharp
                if (output.Decision != OperationalProbeDecision.Probe &&
                    output.Decision != OperationalProbeDecision.Escalate)
                    return;
                if (!target.HasValue) return;
                if (defensive != null && defensive.Contains(unit)) return;
                if (ReadBool(unit, "inbattle") || ReadBool(unit, "onretreat")) return;
                if (ReadObject(unit, "garrisonreference") != null) return;

                if (AICampaign.MoveUnitTo(unit, target.Value, true) && !offensive.Contains(unit))
```

Replace with the wrapper call:

```csharp
                if (output.Decision != OperationalProbeDecision.Probe &&
                    output.Decision != OperationalProbeDecision.Escalate)
                    return;
                if (!target.HasValue) return;
                if (!OffensiveAvailabilityWrapper.IsAvailable(aifactionIndex, unit, target.Value))
                {
                    OnceLog.Info("operational-probe:gate-blocked:" + allianceId,
                        $"[OperationalProbe] alliance={allianceId} unit={SafeName(unit)} blocked-by-availability");
                    return;
                }

                if (AICampaign.MoveUnitTo(unit, target.Value, true) && !offensive.Contains(unit))
```

The local variable `defensive` is now unused; delete its assignment line further up too:
```csharp
var defensive = AccessTools.Field(factionType, "unitsindefensiveoperations")?.GetValue(faction) as IList;
```
(The wrapper consults `unitsindefensiveoperations` itself.)

- [ ] **Step 3: Build**

```bash
./build.sh
```
Expected: 0 warnings, 0 errors. The wrapper compiles against shipped vanilla types.

- [ ] **Step 4: Run pure tests**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```
Expected: existing operational-probe tests still PASS (they don't exercise `Run`, only `OperationalProbeLedger.Build`).

- [ ] **Step 5: Commit**

```bash
git add src/WhiskeyRealism/Strategic/OffensiveAvailabilityWrapper.cs src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs
git commit -m "feat: route operational probe through OffensiveAvailabilityWrapper mirroring vanilla gates"
```

---

## Task 7: Wire ContactEvidenceLedger into OperationalProbeLedger

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/OperationalProbeLedger.cs`
- Modify: `src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs`
- Modify: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

The current `EvaluateExistingProbe` accepts a `ratio = friendly / max(1, enemy)` even when enemy is 0, so a probe targeting an empty sector always escalates after `MinimumProbeDays`. ContactEvidenceLedger fixes this by short-circuiting on `NoContact`.

- [ ] **Step 1: Write failing test for the regression**

Add to `tests` array:
```csharp
("operational probe stays continuing on no contact even after minimum days", OperationalProbeStaysContinuingOnNoContactAfterMinimumDays),
```

Add the method (model on `OperationalProbeEscalatesAfterFavorableContact`):

```csharp
private static void OperationalProbeStaysContinuingOnNoContactAfterMinimumDays()
{
    var fronts = BuildLedger();
    var formation = BuildSimpleFormationLedger(planTargetAreaKey: "RichmondCorridor");
    var prior = new OperationalProbeState
    {
        ProbeId = "test",
        UnitKey = formation.Assignments[0].UnitKey,
        TargetAreaKey = "RichmondCorridor",
        SourceSectorKey = formation.Assignments[0].SectorKey,
        StartedDaySerial = 1862 * 372 + 6 * 31 + 1,
        LastObservedEnemyStrength = 0f,
        LastObservedFriendlyStrength = 8000f
    };
    var input = new OperationalProbeInput
    {
        AllianceId = 0,
        DaySerial = 1862 * 372 + 6 * 31 + 8, // 7 days later, past MinimumProbeDays
        PlanTargetAreaKey = "RichmondCorridor",
        Fronts = fronts,
        FormationDirectives = formation,
        Previous = prior,
        CurrentEnemyStrength = 0f,
        CurrentFriendlyStrength = 8000f,
        Options = new OperationalProbeOptions { MinimumProbeDays = 3, EscalateFriendlyRatio = 1.8f, WithdrawFriendlyRatio = 0.55f },
        ContactEvidence = ContactEvidence.NoContact // new field
    };
    var output = OperationalProbeLedger.Build(input);
    AssertTrue(output.Decision != OperationalProbeDecision.Escalate, "no-contact must not escalate");
    AssertEqual(OperationalProbeDecision.Probe, output.Decision);
}
```

> If `BuildSimpleFormationLedger` doesn't exist, copy the helper pattern from existing operational-probe tests (look near `OperationalProbeEscalatesAfterFavorableContact` in Program.cs).

- [ ] **Step 2: Run, confirm fail**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```
Expected: FAIL — current code escalates because `0 / max(1, 0) = 0` skipped, ratio uses friendly>0 instead.

Wait — re-check current behavior. In `EvaluateExistingProbe`: `ratio = friendly / Math.Max(1f, enemy)`. With friendly=8000, enemy=0 → `8000/1 = 8000`. That's well above `EscalateFriendlyRatio=1.8` → after MinimumProbeDays → Escalate. Confirmed regression target.

- [ ] **Step 3: Add ContactEvidence input + gate to OperationalProbeLedger**

In `src/WhiskeyRealism/Strategic/OperationalProbeLedger.cs`, add field to `OperationalProbeInput`:

```csharp
        public ContactEvidence ContactEvidence = ContactEvidence.EnemyPresent;
```

And gate the escalation branch in `EvaluateExistingProbe`:

```csharp
            if (age >= options.MinimumProbeDays && ratio >= options.EscalateFriendlyRatio &&
                input.ContactEvidence != ContactEvidence.NoContact &&
                input.ContactEvidence != ContactEvidence.OvermatchedContact)
            {
                output.Decision = OperationalProbeDecision.Escalate;
                output.Reason = "favorable-contact";
                output.RequiresMassCommitment = true;
                return output;
            }
```

Also gate fresh-probe Escalate path if needed (currently fresh probes only return `Probe`, so no change there — verify by reading `Build(...)`).

- [ ] **Step 4: Build ContactEvidence in OperationalProbeRuntime.BuildInput**

In `src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs`, extend `BuildInput(...)` signature to accept `IReadOnlyList<BattleHistoryRecord> battleHistory` and call `ContactEvidenceLedger.Build(...)`. Sketch:

```csharp
internal static OperationalProbeInput BuildInput(
    int allianceId,
    CIC cic,
    FrontSectorLedger fronts,
    FormationDirectiveLedger formation,
    OperationalProbeState previous,
    int daySerial,
    EraStage era,
    int policyChapter,
    int campaignMonth,
    PersonalityVector personality,
    IReadOnlyList<BattleHistoryRecord> battleHistory)
{
    // ... existing body ...

    var input = new OperationalProbeInput { /* existing fields */ };

    if (target.HasValue)
    {
        var contactInput = new ContactEvidenceInput
        {
            ObservingAllianceId = allianceId,
            TargetPosition = target.Value,
            CurrentEnemyStrength = input.CurrentEnemyStrength,
            CurrentFriendlyStrength = input.CurrentFriendlyStrength,
            PreviousObservedEnemyStrength = previous?.LastObservedEnemyStrength ?? 0f,
            EnemyReactionMultiplier = input.Options.EnemyReactionMultiplier,
            EscalateFriendlyRatio = input.Options.EscalateFriendlyRatio,
            WithdrawFriendlyRatio = input.Options.WithdrawFriendlyRatio,
            BattleHistory = battleHistory,
            SpatialMaxDistance = GamePrefs.aimaximumdistancetosearchforunitrelocations,
            CurrentDaySerial = daySerial
        };
        input.ContactEvidence = ContactEvidenceLedger.Build(contactInput).Evidence;
    }
    return input;
}
```

- [ ] **Step 5: Pass battle history through StrategicCoordinator.UpdateOperationalProbe**

In `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`, around line 731 the call to `OperationalProbeRuntime.BuildInput(...)` needs the new `battleHistory` argument. Pass `BattleHistory`:

```csharp
                var input = OperationalProbeRuntime.BuildInput(
                    alliance,
                    cic,
                    fronts,
                    formation,
                    _operationalProbeStates[alliance],
                    daySerial,
                    era,
                    SafePolicyChapter(),
                    month,
                    personality,
                    BattleHistory);
```

- [ ] **Step 6: Run, confirm pass**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```
Expected: all PASS, including the new `OperationalProbeStaysContinuingOnNoContactAfterMinimumDays`.

- [ ] **Step 7: Build**

```bash
./build.sh
```

- [ ] **Step 8: Commit**

```bash
git add src/WhiskeyRealism/Strategic/OperationalProbeLedger.cs src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs src/WhiskeyRealism/Strategic/StrategicCoordinator.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "fix: gate operational-probe escalation on ContactEvidence (no zero-enemy escalation)"
```

---

## Task 8: Delete TheaterCommander (Architectural Cleanup #1)

**Files:**
- Delete: `src/WhiskeyRealism/Strategic/TheaterCommander.cs`
- Modify: `src/WhiskeyRealism/Strategic/CIC.cs`
- Modify: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`
- Modify: `src/WhiskeyRealism/Strategic/PersistenceDto.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Confirm zero callers**

```bash
grep -rn "TheaterCommander\|\.Theaters\b\|theaterCommanders\|TheaterCommanderDto" src/ tests/
```
Expected matches only in `TheaterCommander.cs` itself, `CIC.cs` (`Theaters` field), `StrategicCoordinator.cs` (save/load loops), `PersistenceDto.cs` (DTO + JSON property). No callers of any of TheaterCommander's six methods.

- [ ] **Step 2: Write a forward-compat test for sidecar load**

Add to `tests` array:
```csharp
("persistence dto load tolerates legacy theater commanders field", PersistenceDtoLoadToleratesLegacyTheaterCommanders),
```

Add the method:
```csharp
private static void PersistenceDtoLoadToleratesLegacyTheaterCommanders()
{
    string legacyJson = @"{
        ""schemaVersion"":1,
        ""factions"":[
            {""allianceId"":0,""officerName"":""Lincoln"",""theaterCommanders"":[{""theaterId"":1,""officerName"":""Grant""}]}
        ]
    }";
    var dto = Newtonsoft.Json.JsonConvert.DeserializeObject<PersistenceDto>(legacyJson);
    AssertTrue(dto != null, "dto should deserialize");
    AssertTrue(dto.Factions != null && dto.Factions.Count == 1, "one faction loaded");
    // The theaterCommanders field is unknown to the new schema and Newtonsoft must ignore it.
    AssertEqual("Lincoln", dto.Factions[0].OfficerName);
}
```

> Confirm `PersistenceDto.cs` namespaces and shape against current code before writing — adapt property names if needed.

- [ ] **Step 3: Run, confirm test currently passes (Newtonsoft ignores unknown properties by default)**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```
Expected: PASS — but the test is meaningful only after we remove the `TheaterCommanders` property. Keep it; it documents the contract.

- [ ] **Step 4: Delete file and references**

```bash
git rm src/WhiskeyRealism/Strategic/TheaterCommander.cs
```

In `src/WhiskeyRealism/Strategic/CIC.cs`, remove:
```csharp
        public List<TheaterCommander> Theaters = new List<TheaterCommander>();
```
And remove any reference to `Theaters[0].TheaterId` in `BuildPlan`. The line currently reads:
```csharp
                AssignedTheaterId    = (Theaters.Count > 0 ? Theaters[0].TheaterId : 0),
```
Replace with:
```csharp
                AssignedTheaterId    = 0,
```

In `src/WhiskeyRealism/Strategic/PersistenceDto.cs`, remove the `TheaterCommanders` property from `FactionDto` (line ~21) and the `TheaterCommanderDto` class entirely (line ~31).

In `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`, remove the save loop around line 1069:
```csharp
                foreach (var tc in CICs[alliance].Theaters)
                {
                    f.TheaterCommanders.Add(new TheaterCommanderDto { ... });
                }
```
And the load loop around line 1116:
```csharp
                foreach (var tc in f.TheaterCommanders)
                {
                    cic.Theaters.Add(new TheaterCommander { ... });
                }
```

- [ ] **Step 5: Drop csproj include if present**

In `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, remove if present:
```xml
    <Compile Include="..\..\src\WhiskeyRealism\Strategic\TheaterCommander.cs" Link="TheaterCommander.cs" />
```

- [ ] **Step 6: Run all tests, confirm pass**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```
Expected: all PASS, including `PersistenceDtoLoadToleratesLegacyTheaterCommanders`.

- [ ] **Step 7: Build**

```bash
./build.sh
```
Expected: 0 warnings, 0 errors.

- [ ] **Step 8: Commit**

```bash
git add -A src/WhiskeyRealism/Strategic/ src/WhiskeyRealism/Strategic/PersistenceDto.cs src/WhiskeyRealism/Strategic/StrategicCoordinator.cs src/WhiskeyRealism/Strategic/CIC.cs tests/WhiskeyRealism.Tests/
git commit -m "refactor: delete unused TheaterCommander class and legacy DTO field"
```

---

## Task 9: Consolidate operational probe state (Architectural Cleanup #3)

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/OperationalProbeLedger.cs`
- Modify: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

> The coordinator becomes the single source of truth. `OperationalProbeOutput.State` is treated as a transient publish-payload reference into `_operationalProbeStates[alliance]`, not an independent owner.

- [ ] **Step 1: Write failing test asserting single source**

Add to `tests` array:
```csharp
("operational probe state has single source on coordinator", OperationalProbeStateHasSingleSourceOnCoordinator),
```

Add the method:
```csharp
private static void OperationalProbeStateHasSingleSourceOnCoordinator()
{
    // After Build returns, mutating output.State.LastObservedEnemyStrength must NOT change
    // the coordinator's stored state, because output.State is a reference *into* the
    // coordinator's owned struct (or the test fails for the right reason if it's a separate copy).
    // We model this by asserting that two consecutive Build calls feeding the same prior produce
    // identical state references when the coordinator owns the slot.

    var fronts = BuildLedger();
    var formation = BuildSimpleFormationLedger(planTargetAreaKey: "RichmondCorridor");
    var input = new OperationalProbeInput
    {
        AllianceId = 0,
        DaySerial = 1,
        PlanTargetAreaKey = "RichmondCorridor",
        Fronts = fronts,
        FormationDirectives = formation,
        Previous = null,
        Options = new OperationalProbeOptions()
    };
    var first = OperationalProbeLedger.Build(input);
    AssertTrue(first.State != null, "fresh probe should publish a state");

    // Mutating .State.LastObservedEnemyStrength is now allowed by ledger contract; the
    // coordinator's _operationalProbeStates[alliance] must not retain a stale snapshot.
    // We can't reach the coordinator from a pure test, so we verify the surface contract:
    // OperationalProbeOutput.State is the same object that the next call's Previous should accept.
    var second = OperationalProbeLedger.Build(new OperationalProbeInput
    {
        AllianceId = 0,
        DaySerial = 2,
        PlanTargetAreaKey = "RichmondCorridor",
        Fronts = fronts,
        FormationDirectives = formation,
        Previous = first.State, // pass the same reference
        CurrentEnemyStrength = 1000f,
        CurrentFriendlyStrength = 4000f,
        Options = new OperationalProbeOptions()
    });
    AssertTrue(second.State != null, "continuing probe publishes state");
    AssertEqual(first.State.ProbeId, second.State.ProbeId);
}
```

- [ ] **Step 2: Run, confirm pass (likely already true)**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

If it passes, the contract is already aligned at the ledger level — the consolidation work is in `StrategicCoordinator`.

- [ ] **Step 3: Update StrategicCoordinator to be sole owner**

In `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`, the current `UpdateOperationalProbe` (around line 714) does:

```csharp
                var output = OperationalProbeLedger.Build(input);
                OperationalProbes[alliance] = output;
                if (output.State != null)
                    _operationalProbeStates[alliance] = output.State;
                if (output.Decision == OperationalProbeDecision.None ||
                    output.Decision == OperationalProbeDecision.Withdraw ||
                    output.Decision == OperationalProbeDecision.Escalate)
                    _operationalProbeStates[alliance] = null;
```

Refactor so the coordinator's slot is the canonical reference and `output.State` is set to the slot:

```csharp
                var output = OperationalProbeLedger.Build(input);
                OperationalProbes[alliance] = output;

                bool clearState =
                    output.Decision == OperationalProbeDecision.None ||
                    output.Decision == OperationalProbeDecision.Withdraw ||
                    output.Decision == OperationalProbeDecision.Escalate;

                _operationalProbeStates[alliance] = clearState ? null : output.State;
                output.State = _operationalProbeStates[alliance]; // publish payload references the canonical slot
```

- [ ] **Step 4: Run all tests**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```
Expected: all PASS.

- [ ] **Step 5: Build**

```bash
./build.sh
```

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Strategic/StrategicCoordinator.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "refactor: make StrategicCoordinator the single owner of operational probe state"
```

---

## Task 10: DirectorPosture types and DirectorMemory DTO

**Files:**
- Create: `src/WhiskeyRealism/Strategic/DirectorPosture.cs`
- Create: `src/WhiskeyRealism/Strategic/DirectorMemory.cs`
- Modify: `src/WhiskeyRealism/Strategic/PersistenceDto.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`

- [ ] **Step 1: Add csproj includes**

```xml
    <Compile Include="..\..\src\WhiskeyRealism\Strategic\DirectorPosture.cs" Link="DirectorPosture.cs" />
    <Compile Include="..\..\src\WhiskeyRealism\Strategic\DirectorMemory.cs" Link="DirectorMemory.cs" />
```

- [ ] **Step 2: Write the types**

Create `src/WhiskeyRealism/Strategic/DirectorPosture.cs`:

```csharp
namespace WhiskeyRealism.Strategic
{
    public enum CampaignPace
    {
        Stable,
        TooQuiet,
        Overheated,
        TooFastCollapse,
        Stalemated,
        LateWarPressure
    }

    public enum StrategicIntent
    {
        Probe,
        Concentrate,
        Preserve,
        Delay,
        Exploit,
        Recover
    }

    public enum CollapseRisk
    {
        Low,
        Elevated,
        Critical
    }

    public sealed class DirectorPosture
    {
        public int AllianceId;
        public CampaignPace Pace;
        public StrategicIntent Intent;
        public CollapseRisk Risk;
        public Theater TheaterPriority = Theater.Unknown;
        public string Reason;
        public string SourceSignature;
        public bool Stale;

        // Threshold modifiers — applied on top of OperationalTempoDoctrine output.
        // Each is bounded to ±50% of the personality delta on the same field.
        public float MinimumProbeDaysModifier;
        public float MaximumProbeStrengthFractionModifier;
        public float EscalateFriendlyRatioModifier;
        public float EnemyReactionMultiplierModifier;
        public float WithdrawFriendlyRatioModifier;
    }
}
```

Create `src/WhiskeyRealism/Strategic/DirectorMemory.cs`:

```csharp
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public sealed class DirectorMemory
    {
        public DirectorPosture LastPosture;
        public int LastFullRefreshDay = -1;
        public int CapitalDangerStreakDays;
        public int DaysSinceLastBattle;
        public List<string> RecentEventSummaries = new List<string>();
        public string LastSourceSignature;
    }
}
```

- [ ] **Step 3: Add Director memory persistence to PersistenceDto**

In `src/WhiskeyRealism/Strategic/PersistenceDto.cs`, add a new DTO and a field on `FactionDto`:

```csharp
    internal class DirectorMemoryDto
    {
        [JsonProperty("pace")]            public int Pace;
        [JsonProperty("intent")]          public int Intent;
        [JsonProperty("risk")]            public int Risk;
        [JsonProperty("theaterPriority")] public int TheaterPriority;
        [JsonProperty("lastFullRefresh")] public int LastFullRefreshDay = -1;
        [JsonProperty("capitalStreak")]   public int CapitalDangerStreakDays;
        [JsonProperty("daysSinceBattle")] public int DaysSinceLastBattle;
        [JsonProperty("sourceSig")]       public string LastSourceSignature;
        [JsonProperty("recentEvents")]    public List<string> RecentEventSummaries = new List<string>();
    }
```

And on `FactionDto`:
```csharp
        [JsonProperty("directorMemory")] public DirectorMemoryDto DirectorMemory;
```

- [ ] **Step 4: Build**

```bash
./build.sh
```

- [ ] **Step 5: Run all tests**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```
Expected: green (no behavior changed yet).

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Strategic/DirectorPosture.cs src/WhiskeyRealism/Strategic/DirectorMemory.cs src/WhiskeyRealism/Strategic/PersistenceDto.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "feat: add DirectorPosture types and DirectorMemory persistence DTO"
```

---

## Task 11: CampaignPaceLedger

**Files:**
- Create: `src/WhiskeyRealism/Strategic/CampaignPaceLedger.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add csproj include**

```xml
    <Compile Include="..\..\src\WhiskeyRealism\Strategic\CampaignPaceLedger.cs" Link="CampaignPaceLedger.cs" />
```

- [ ] **Step 2: Write failing tests covering each rule**

Add to `tests` array:
```csharp
("campaign pace too fast collapse on early national morale crash", CampaignPaceTooFastCollapseOnEarlyMoraleCrash),
("campaign pace late war pressure on chapter three", CampaignPaceLateWarPressureOnChapterThree),
("campaign pace overheated on heavy 14-day battle volume", CampaignPaceOverheatedOnHeavy14DayBattles),
("campaign pace too quiet only outside chapter one winter", CampaignPaceTooQuietSuppressedInChapterOneWinter),
("campaign pace stalemated when chapter two front static", CampaignPaceStalematedWhenChapterTwoFrontStatic),
("campaign pace stable default", CampaignPaceStableDefault),
("collapse risk thresholds bound to break morale trigger", CollapseRiskThresholdsBoundToBreakMoraleTrigger),
("director cannot publish preserve for late csa under elevated risk", DirectorCannotPublishPreserveForLateCsaUnderElevatedRisk),
("campaign pace publishes theater priority from highest pressure theater", CampaignPacePublishesTheaterPriorityFromHighestPressureTheater),
```

Add the methods:

```csharp
private static CampaignPaceInput BuildPaceInput(
    int allianceId, int year, int month, int chapter,
    float ownNationalMorale, float enemyNationalMorale,
    int battlesIn14Days, int majorBattlesIn14Days,
    int capitalStreak, int daysSinceFrontChange,
    bool winter)
{
    return new CampaignPaceInput
    {
        AllianceId = allianceId,
        Year = year, Month = month,
        PolicyChapter = chapter,
        OwnNationalMorale = ownNationalMorale,
        EnemyNationalMorale = enemyNationalMorale,
        BreakMoraleTrigger = 30f, // arbitrary stable test value
        MinNationalMoraleSurrender = 18f,
        BattlesIn14Days = battlesIn14Days,
        MajorBattlesIn14Days = majorBattlesIn14Days,
        CapitalDangerStreakDays = capitalStreak,
        DaysSinceFrontSignatureChange = daysSinceFrontChange,
        IsWinter = winter
    };
}

private static void CampaignPaceTooFastCollapseOnEarlyMoraleCrash()
{
    var input = BuildPaceInput(allianceId: 1, year: 1862, month: 6, chapter: 2,
        ownNationalMorale: 30f * 1.10f, enemyNationalMorale: 90f,
        battlesIn14Days: 0, majorBattlesIn14Days: 0,
        capitalStreak: 0, daysSinceFrontChange: 5, winter: false);
    var output = CampaignPaceLedger.Build(input);
    AssertEqual(CampaignPace.TooFastCollapse, output.Pace);
    AssertEqual(CollapseRisk.Critical, output.Risk);
}

private static void CampaignPaceLateWarPressureOnChapterThree()
{
    var input = BuildPaceInput(allianceId: 0, year: 1864, month: 6, chapter: 3,
        ownNationalMorale: 80f, enemyNationalMorale: 60f,
        battlesIn14Days: 0, majorBattlesIn14Days: 0,
        capitalStreak: 0, daysSinceFrontChange: 60, winter: false);
    var output = CampaignPaceLedger.Build(input);
    AssertEqual(CampaignPace.LateWarPressure, output.Pace);
}

private static void CampaignPaceOverheatedOnHeavy14DayBattles()
{
    var input = BuildPaceInput(allianceId: 0, year: 1862, month: 8, chapter: 2,
        ownNationalMorale: 80f, enemyNationalMorale: 80f,
        battlesIn14Days: 6, majorBattlesIn14Days: 4,
        capitalStreak: 0, daysSinceFrontChange: 5, winter: false);
    var output = CampaignPaceLedger.Build(input);
    AssertEqual(CampaignPace.Overheated, output.Pace);
}

private static void CampaignPaceTooQuietSuppressedInChapterOneWinter()
{
    var input = BuildPaceInput(allianceId: 0, year: 1861, month: 12, chapter: 1,
        ownNationalMorale: 95f, enemyNationalMorale: 90f,
        battlesIn14Days: 0, majorBattlesIn14Days: 0,
        capitalStreak: 0, daysSinceFrontChange: 30, winter: true);
    var output = CampaignPaceLedger.Build(input);
    AssertTrue(output.Pace != CampaignPace.TooQuiet,
        "chapter 1 winter is the historically correct quiet state and must not be flagged");
}

private static void CampaignPaceStalematedWhenChapterTwoFrontStatic()
{
    var input = BuildPaceInput(allianceId: 0, year: 1862, month: 6, chapter: 2,
        ownNationalMorale: 80f, enemyNationalMorale: 80f,
        battlesIn14Days: 1, majorBattlesIn14Days: 0,
        capitalStreak: 0, daysSinceFrontChange: 75, winter: false);
    var output = CampaignPaceLedger.Build(input);
    AssertEqual(CampaignPace.Stalemated, output.Pace);
}

private static void CampaignPaceStableDefault()
{
    var input = BuildPaceInput(allianceId: 0, year: 1862, month: 6, chapter: 2,
        ownNationalMorale: 80f, enemyNationalMorale: 80f,
        battlesIn14Days: 2, majorBattlesIn14Days: 1,
        capitalStreak: 0, daysSinceFrontChange: 10, winter: false);
    var output = CampaignPaceLedger.Build(input);
    AssertEqual(CampaignPace.Stable, output.Pace);
}

private static void CollapseRiskThresholdsBoundToBreakMoraleTrigger()
{
    AssertEqual(CollapseRisk.Critical, CampaignPaceLedger.RiskFor(ownMorale: 30f * 1.10f, breakMoraleTrigger: 30f, minSurrender: 18f));
    AssertEqual(CollapseRisk.Elevated, CampaignPaceLedger.RiskFor(ownMorale: 30f * 1.40f, breakMoraleTrigger: 30f, minSurrender: 18f));
    AssertEqual(CollapseRisk.Low,      CampaignPaceLedger.RiskFor(ownMorale: 30f * 2.50f, breakMoraleTrigger: 30f, minSurrender: 18f));
}

private static void DirectorCannotPublishPreserveForLateCsaUnderElevatedRisk()
{
    var input = BuildPaceInput(allianceId: 1, year: 1864, month: 6, chapter: 3,
        ownNationalMorale: 30f * 1.40f, enemyNationalMorale: 80f,
        battlesIn14Days: 1, majorBattlesIn14Days: 0,
        capitalStreak: 0, daysSinceFrontChange: 5, winter: false);
    var output = CampaignPaceLedger.Build(input);
    AssertTrue(output.Risk >= CollapseRisk.Elevated, "elevated risk expected");
    AssertTrue(output.IntentBlockedFromPreserve,
        "1864 CSA under elevated risk must not publish StrategicIntent.Preserve");
}

private static void CampaignPacePublishesTheaterPriorityFromHighestPressureTheater()
{
    var view = new TheaterPressureView();
    view.OwnStrengthByTheater[Theater.East] = 10000f;
    view.EnemyStrengthByTheater[Theater.East] = 4000f;
    view.OwnStrengthByTheater[Theater.West] = 4000f;
    view.EnemyStrengthByTheater[Theater.West] = 8000f; // West is the hot theater for us
    var input = BuildPaceInput(allianceId: 0, year: 1862, month: 6, chapter: 2,
        ownNationalMorale: 80f, enemyNationalMorale: 80f,
        battlesIn14Days: 2, majorBattlesIn14Days: 0,
        capitalStreak: 0, daysSinceFrontChange: 10, winter: false);
    input.TheaterPressure = view;
    var output = CampaignPaceLedger.Build(input);
    AssertEqual(Theater.West, output.TheaterPriority);
}
```

- [ ] **Step 3: Run, confirm fail**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```
Expected: type-not-found.

- [ ] **Step 4: Implement**

Create `src/WhiskeyRealism/Strategic/CampaignPaceLedger.cs`:

```csharp
namespace WhiskeyRealism.Strategic
{
    public sealed class CampaignPaceInput
    {
        public int AllianceId;
        public int Year;
        public int Month;
        public int PolicyChapter;
        public float OwnNationalMorale;
        public float EnemyNationalMorale;
        public float BreakMoraleTrigger;
        public float MinNationalMoraleSurrender;
        public int BattlesIn14Days;
        public int MajorBattlesIn14Days;
        public int CapitalDangerStreakDays;
        public int DaysSinceFrontSignatureChange;
        public bool IsWinter;
        public TheaterPressureView TheaterPressure;
    }

    public sealed class CampaignPaceOutput
    {
        public CampaignPace Pace;
        public CollapseRisk Risk;
        public bool IntentBlockedFromPreserve;
        public string Reason;
        public Theater TheaterPriority = Theater.Unknown;
    }

    public static class CampaignPaceLedger
    {
        public static CollapseRisk RiskFor(float ownMorale, float breakMoraleTrigger, float minSurrender)
        {
            if (ownMorale <= breakMoraleTrigger * 1.15f) return CollapseRisk.Critical;
            if (ownMorale <= minSurrender * 1.10f)        return CollapseRisk.Critical;
            if (ownMorale <= breakMoraleTrigger * 1.50f) return CollapseRisk.Elevated;
            return CollapseRisk.Low;
        }

        public static CampaignPaceOutput Build(CampaignPaceInput input)
        {
            var output = new CampaignPaceOutput();
            if (input == null) { output.Pace = CampaignPace.Stable; output.Reason = "missing-input"; return output; }

            output.Risk = RiskFor(input.OwnNationalMorale, input.BreakMoraleTrigger, input.MinNationalMoraleSurrender);

            // 1864 CSA collapse floor: cannot publish Preserve when risk ≥ Elevated.
            output.IntentBlockedFromPreserve =
                input.AllianceId == 1 && input.Year >= 1864 && output.Risk >= CollapseRisk.Elevated;

            // Rule 1 — TooFastCollapse: Critical risk before 1864.
            if (output.Risk == CollapseRisk.Critical && input.Year <= 1863)
            {
                output.Pace = CampaignPace.TooFastCollapse;
                output.Reason = "critical-morale-pre-1864";
                return output;
            }

            // Rule 2 — LateWarPressure: chapter 3 OR (year >= 1864 + Union when CSA still has runway).
            bool unionLateRunway = input.AllianceId == 0 && input.Year >= 1864 &&
                                   input.EnemyNationalMorale > input.BreakMoraleTrigger * 1.5f;
            if (input.PolicyChapter >= 3 || unionLateRunway)
            {
                output.Pace = CampaignPace.LateWarPressure;
                output.Reason = "chapter3-or-late-union-runway";
                return output;
            }

            // Rule 3 — Overheated.
            if (input.MajorBattlesIn14Days >= 4 || (input.MajorBattlesIn14Days >= 2 && input.BattlesIn14Days >= 5))
            {
                output.Pace = CampaignPace.Overheated;
                output.Reason = "heavy-14d-battle-volume";
                return output;
            }

            // Rule 4 — TooQuiet (suppressed in chapter 1 winter).
            bool chapter1Winter = input.PolicyChapter <= 1 && input.IsWinter;
            if (!chapter1Winter &&
                input.BattlesIn14Days == 0 &&
                input.CapitalDangerStreakDays == 0)
            {
                output.Pace = CampaignPace.TooQuiet;
                output.Reason = "no-battles-no-streak-outside-winter1";
                return output;
            }

            // Rule 5 — Stalemated: chapter 2 + static front + similar quarterly counts.
            if (input.PolicyChapter == 2 && input.DaysSinceFrontSignatureChange >= 60)
            {
                output.Pace = CampaignPace.Stalemated;
                output.Reason = "chapter2-front-static";
                return output;
            }

            output.Pace = CampaignPace.Stable;
            output.Reason = "default";
            return output;
        }
    }
}
```

- [ ] **Step 5: Run, confirm pass**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```
Expected: all PASS.

- [ ] **Step 6: Build**

```bash
./build.sh
```

- [ ] **Step 7: Commit**

```bash
git add src/WhiskeyRealism/Strategic/CampaignPaceLedger.cs tests/WhiskeyRealism.Tests/Program.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "feat: add CampaignPaceLedger bound to vanilla nationalmorale + chapter scalars"
```

---

## Task 12: StrategicResilienceDirector (composition + personality clamp)

**Files:**
- Create: `src/WhiskeyRealism/Strategic/StrategicResilienceDirector.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add csproj include**

```xml
    <Compile Include="..\..\src\WhiskeyRealism\Strategic\StrategicResilienceDirector.cs" Link="StrategicResilienceDirector.cs" />
```

- [ ] **Step 2: Write failing tests for personality clamp + intent blocking**

Add to `tests` array:
```csharp
("director clamps threshold modifier to half personality delta", DirectorClampsThresholdModifierToHalfPersonalityDelta),
("director maps overheated pace to recover-leaning intent", DirectorMapsOverheatedToRecoverLeaning),
("director blocks preserve intent for late csa under elevated risk", DirectorBlocksPreserveForLateCsaUnderElevatedRisk),
```

Add the methods:

```csharp
private static void DirectorClampsThresholdModifierToHalfPersonalityDelta()
{
    var personality = new PersonalityVector { Audacity = 0.5f, Caution = 0.0f };
    // Personality contributes: MaximumProbeStrengthFraction += 0.05*audacity - 0.04*caution = +0.025
    float personalityDeltaOnFraction = 0.05f * personality.Audacity - 0.04f * personality.Caution;
    var posture = StrategicResilienceDirector.ProposePosture(
        allianceId: 0,
        pace: new CampaignPaceOutput { Pace = CampaignPace.Overheated, Risk = CollapseRisk.Low, IntentBlockedFromPreserve = false },
        personality: personality);
    AssertTrue(System.Math.Abs(posture.MaximumProbeStrengthFractionModifier) <= 0.5f * System.Math.Abs(personalityDeltaOnFraction) + 1e-6f,
        "director modifier must be ≤50% of personality delta — was " + posture.MaximumProbeStrengthFractionModifier);
}

private static void DirectorMapsOverheatedToRecoverLeaning()
{
    var posture = StrategicResilienceDirector.ProposePosture(
        allianceId: 0,
        pace: new CampaignPaceOutput { Pace = CampaignPace.Overheated, Risk = CollapseRisk.Low, IntentBlockedFromPreserve = false },
        personality: new PersonalityVector());
    AssertTrue(posture.Intent == StrategicIntent.Recover || posture.Intent == StrategicIntent.Delay,
        "overheated pace should propose recover/delay intent");
}

private static void DirectorBlocksPreserveForLateCsaUnderElevatedRisk()
{
    var posture = StrategicResilienceDirector.ProposePosture(
        allianceId: 1,
        pace: new CampaignPaceOutput { Pace = CampaignPace.LateWarPressure, Risk = CollapseRisk.Elevated, IntentBlockedFromPreserve = true },
        personality: new PersonalityVector { Caution = 0.6f });
    AssertTrue(posture.Intent != StrategicIntent.Preserve,
        "1864 CSA under elevated risk cannot publish Preserve");
}
```

- [ ] **Step 3: Run, confirm fail**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

- [ ] **Step 4: Implement**

Create `src/WhiskeyRealism/Strategic/StrategicResilienceDirector.cs`:

```csharp
using System;

namespace WhiskeyRealism.Strategic
{
    public static class StrategicResilienceDirector
    {
        public static DirectorPosture ProposePosture(
            int allianceId,
            CampaignPaceOutput pace,
            PersonalityVector personality)
        {
            var posture = new DirectorPosture
            {
                AllianceId = allianceId,
                Pace = pace?.Pace ?? CampaignPace.Stable,
                Risk = pace?.Risk ?? CollapseRisk.Low,
                Reason = pace?.Reason ?? "no-pace-input"
            };

            posture.Intent = ProposeIntent(posture.Pace, posture.Risk, personality);

            if (pace != null && pace.IntentBlockedFromPreserve && posture.Intent == StrategicIntent.Preserve)
                posture.Intent = StrategicIntent.Delay;

            ApplyThresholdModifiers(posture, personality);
            return posture;
        }

        private static StrategicIntent ProposeIntent(CampaignPace pace, CollapseRisk risk, PersonalityVector personality)
        {
            switch (pace)
            {
                case CampaignPace.TooFastCollapse: return StrategicIntent.Recover;
                case CampaignPace.Overheated:      return StrategicIntent.Recover;
                case CampaignPace.TooQuiet:        return StrategicIntent.Probe;
                case CampaignPace.LateWarPressure: return risk >= CollapseRisk.Elevated ? StrategicIntent.Delay : StrategicIntent.Concentrate;
                case CampaignPace.Stalemated:      return StrategicIntent.Probe;
                default:                            return StrategicIntent.Concentrate;
            }
        }

        // Personality contributions from OperationalTempoDoctrine.ApplyPersonality:
        //   MaximumProbeStrengthFraction += 0.05*audacity - 0.04*caution
        //   EscalateFriendlyRatio        += 0.15*caution  - 0.10*audacity
        //   MinimumProbeDays             ±1 on |audacity|/|caution| > 0.35
        // Director modifiers are bounded to ±50% of the absolute personality delta on the same field.
        private static void ApplyThresholdModifiers(DirectorPosture posture, PersonalityVector personality)
        {
            float audacity = personality?.Audacity ?? 0f;
            float caution = personality?.Caution ?? 0f;

            float pFraction = Math.Abs(0.05f * audacity - 0.04f * caution);
            float pEscalate = Math.Abs(0.15f * caution - 0.10f * audacity);
            float pReaction = 0.10f; // doctrine doesn't adjust this from personality; cap at 0.10
            float pWithdraw = 0.08f; // same — small fixed cap
            float pDays     = (Math.Abs(audacity) > 0.35f || Math.Abs(caution) > 0.35f) ? 1f : 0.5f;

            float fractionMod = 0f, escalateMod = 0f, reactionMod = 0f, withdrawMod = 0f, daysMod = 0f;

            switch (posture.Pace)
            {
                case CampaignPace.Overheated:
                    fractionMod = -0.5f * pFraction;
                    escalateMod = +0.5f * pEscalate;
                    daysMod     = +0.5f * pDays;
                    break;
                case CampaignPace.TooQuiet:
                    fractionMod = +0.5f * pFraction;
                    escalateMod = -0.5f * pEscalate;
                    daysMod     = -0.5f * pDays;
                    break;
                case CampaignPace.LateWarPressure:
                    if (posture.AllianceId == 0)
                    {
                        fractionMod = +0.5f * pFraction;
                        escalateMod = -0.5f * pEscalate;
                    }
                    else
                    {
                        withdrawMod = +0.5f * pWithdraw;
                    }
                    break;
                case CampaignPace.TooFastCollapse:
                    fractionMod = -0.5f * pFraction;
                    daysMod     = +0.5f * pDays;
                    reactionMod = -0.5f * pReaction;
                    break;
                case CampaignPace.Stalemated:
                    daysMod = -0.5f * pDays;
                    break;
            }

            posture.MaximumProbeStrengthFractionModifier = fractionMod;
            posture.EscalateFriendlyRatioModifier        = escalateMod;
            posture.EnemyReactionMultiplierModifier      = reactionMod;
            posture.WithdrawFriendlyRatioModifier        = withdrawMod;
            posture.MinimumProbeDaysModifier             = daysMod;
        }

        public static void ApplyTo(OperationalProbeOptions options, DirectorPosture posture)
        {
            if (options == null || posture == null) return;
            options.MaximumProbeStrengthFraction = Clamp(options.MaximumProbeStrengthFraction + posture.MaximumProbeStrengthFractionModifier, 0.15f, 0.55f);
            options.EscalateFriendlyRatio        = Clamp(options.EscalateFriendlyRatio        + posture.EscalateFriendlyRatioModifier,        1.35f, 2.60f);
            options.EnemyReactionMultiplier      = Clamp(options.EnemyReactionMultiplier      + posture.EnemyReactionMultiplierModifier,      1.15f, 1.85f);
            options.WithdrawFriendlyRatio        = Clamp(options.WithdrawFriendlyRatio        + posture.WithdrawFriendlyRatioModifier,        0.35f, 0.85f);
            options.MinimumProbeDays             = ClampInt(options.MinimumProbeDays + (int)Math.Round(posture.MinimumProbeDaysModifier), 1, 9);
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
        private static int   ClampInt(int v, int lo, int hi)    => v < lo ? lo : (v > hi ? hi : v);
    }
}
```

- [ ] **Step 5: Run, confirm pass**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

- [ ] **Step 6: Build**

```bash
./build.sh
```

- [ ] **Step 7: Commit**

```bash
git add src/WhiskeyRealism/Strategic/StrategicResilienceDirector.cs tests/WhiskeyRealism.Tests/Program.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "feat: add StrategicResilienceDirector with personality-clamped threshold modifiers"
```

---

## Task 13: Wire CIC.ReviewPlan to consult PhaseTruthLedger

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/CIC.cs`
- Modify: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Write failing test**

Add to `tests` array:
```csharp
("cic review plan replans when phase truth says target accomplished", CicReviewPlanReplansWhenPhaseTruthSaysAccomplished),
```

Add the method:
```csharp
private static void CicReviewPlanReplansWhenPhaseTruthSaysAccomplished()
{
    var cic = new CIC
    {
        AllianceId = 0,
        OfficerName = "TestCIC",
        OfficerPersonality = new PersonalityVector(),
        ActivePlan = new OperationalPlan
        {
            CICFactionAllianceId = 0,
            CurrentPhaseIndex = 0,
            PlanDeadlineMonth = 12, PlanDeadlineYear = 1862,
            Phases = { new Phase { TargetObjectiveId = 29, DeadlineMonth = 12, DeadlineYear = 1862 } }
        }
    };
    var truth = new PhaseTruthOutput
    {
        Verdict = PhaseTruthVerdict.TargetAccomplished,
        RecommendedAction = PhaseTruthAction.Advance
    };
    bool kept = cic.ReviewPlanWithTruth(currentMonth: 6, currentYear: 1862, truth: truth);
    // Single-phase plan: Advance falls through to dirty replan.
    AssertTrue(!kept, "single-phase plan should be replanned after target accomplished");
    AssertTrue(cic.ActivePlan == null || cic.ActivePlan.IsDirty, "plan should be cleared or dirty");
}
```

- [ ] **Step 2: Run, confirm fail**

`ReviewPlanWithTruth` does not exist yet.

- [ ] **Step 3: Add the method to CIC**

In `src/WhiskeyRealism/Strategic/CIC.cs`, add alongside `ReviewPlan(...)`:

```csharp
public bool ReviewPlanWithTruth(int currentMonth, int currentYear, PhaseTruthOutput truth)
{
    if (ActivePlan == null) return false;
    if (truth == null) return ReviewPlan(currentMonth, currentYear);

    switch (truth.RecommendedAction)
    {
        case PhaseTruthAction.Replan:
            ActivePlan.IsDirty = true;
            return false;
        case PhaseTruthAction.Advance:
            return AdvancePhase();
        case PhaseTruthAction.Recover:
        case PhaseTruthAction.Fallback:
            ActivePlan.IsDirty = true;
            return false;
        case PhaseTruthAction.Continue:
        default:
            return ReviewPlan(currentMonth, currentYear);
    }
}
```

- [ ] **Step 4: Wire from StrategicCoordinator**

In `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs` around line 274 (the `cic.ReviewPlan(...)` call inside the daily tick), build the `PhaseTruthInput` from `cic`, ledgers, and `BattleHistory`, then call `ReviewPlanWithTruth(...)` instead:

```csharp
                var phaseTruth = BuildPhaseTruth(cic, alliance, day, month, year);
                if (!cic.ReviewPlanWithTruth(month, year, phaseTruth))
                    cic.Replan(era, month, year);
```

Add a private helper near other ledger helpers:

```csharp
private PhaseTruthOutput BuildPhaseTruth(CIC cic, int alliance, int day, int month, int year)
{
    if (cic?.ActivePlan?.CurrentPhase == null) return null;
    var phase = cic.ActivePlan.CurrentPhase;
    var fronts = alliance < Fronts.Length ? Fronts[alliance] : null;
    var targetPos = ObjectiveAdapter.ResolveObjectivePosition(phase.TargetObjectiveId);
    string sectorKey = targetPos.HasValue ? FrontSectorRuntime.SectorKey(targetPos.Value) : null;
    var sector = fronts?.GetSector(sectorKey);
    int daySerial = year * 372 + month * 31 + day;
    bool engagedRecently = false;
    if (targetPos.HasValue)
    {
        foreach (var _ in BattleHistoryQuery.Near(BattleHistory, targetPos.Value, GamePrefs.aimaximumdistancetosearchforunitrelocations, daySerial, 14))
        {
            engagedRecently = true; break;
        }
    }
    var input = new PhaseTruthInput
    {
        Plan = cic.ActivePlan,
        TargetAccomplished = ObjectiveAdapter.IsAccomplished(phase.TargetObjectiveId),
        ObjectiveAvailable = ObjectiveAdapter.IsAvailable(phase.TargetObjectiveId, alliance),
        TargetPositionResolves = targetPos.HasValue,
        TargetEngagedRecently = engagedRecently,
        TargetSectorOwnStrength = sector?.OwnStrength ?? 0f,
        RequiredForce = phase.ForceFractionRequired * (sector?.OwnStrength + sector?.EnemyStrength ?? 0f),
        CurrentMonth = month,
        CurrentYear = year
    };
    return PhaseTruthLedger.Evaluate(input);
}
```

> Add `ObjectiveAdapter.IsAccomplished(int)` and `ObjectiveAdapter.IsAvailable(int, int)` if not present — both reflect into vanilla `CampaignObjective.accomplished` and `CampaignObjective.GetAvailableObjectives(...)`. Keep them try/catch + `OnceLog.Warning` per project conventions.

- [ ] **Step 5: Run, confirm pass**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

- [ ] **Step 6: Build**

```bash
./build.sh
```

- [ ] **Step 7: Commit**

```bash
git add src/WhiskeyRealism/Strategic/CIC.cs src/WhiskeyRealism/Strategic/StrategicCoordinator.cs src/WhiskeyRealism/Strategic/ObjectiveAdapter.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: route CIC plan review through PhaseTruthLedger"
```

---

## Task 14: Wire Director into operational probe options + persist DirectorMemory

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`
- Modify: `src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs`
- Test: smoke + console assertion of save/load round-trip

- [ ] **Step 1: Write failing round-trip test**

Add to `tests` array:
```csharp
("director memory round trips through dto", DirectorMemoryRoundTripsThroughDto),
```

Add the method:
```csharp
private static void DirectorMemoryRoundTripsThroughDto()
{
    var memory = new DirectorMemory
    {
        LastFullRefreshDay = 12345,
        CapitalDangerStreakDays = 3,
        DaysSinceLastBattle = 7,
        LastSourceSignature = "sig-abc",
        LastPosture = new DirectorPosture
        {
            AllianceId = 0,
            Pace = CampaignPace.LateWarPressure,
            Intent = StrategicIntent.Concentrate,
            Risk = CollapseRisk.Low,
            TheaterPriority = Theater.East
        }
    };
    memory.RecentEventSummaries.Add("battle:east:1864-06-15");

    var dto = StrategicResilienceDirector.MemoryToDto(memory);
    var rebuilt = StrategicResilienceDirector.MemoryFromDto(dto);

    AssertEqual(memory.LastFullRefreshDay, rebuilt.LastFullRefreshDay);
    AssertEqual(memory.CapitalDangerStreakDays, rebuilt.CapitalDangerStreakDays);
    AssertEqual(memory.LastSourceSignature, rebuilt.LastSourceSignature);
    AssertEqual(memory.LastPosture.Pace, rebuilt.LastPosture.Pace);
    AssertEqual(memory.LastPosture.Intent, rebuilt.LastPosture.Intent);
    AssertEqual(memory.LastPosture.Risk, rebuilt.LastPosture.Risk);
    AssertEqual(memory.LastPosture.TheaterPriority, rebuilt.LastPosture.TheaterPriority);
    AssertEqual(1, rebuilt.RecentEventSummaries.Count);
}
```

- [ ] **Step 2: Run, confirm fail**

`MemoryToDto` / `MemoryFromDto` don't exist yet.

- [ ] **Step 3: Implement DTO converters**

Add to `StrategicResilienceDirector.cs`:

```csharp
public static DirectorMemoryDto MemoryToDto(DirectorMemory memory)
{
    if (memory == null) return null;
    var dto = new DirectorMemoryDto
    {
        LastFullRefreshDay = memory.LastFullRefreshDay,
        CapitalDangerStreakDays = memory.CapitalDangerStreakDays,
        DaysSinceLastBattle = memory.DaysSinceLastBattle,
        LastSourceSignature = memory.LastSourceSignature,
        RecentEventSummaries = new System.Collections.Generic.List<string>(memory.RecentEventSummaries ?? new System.Collections.Generic.List<string>())
    };
    if (memory.LastPosture != null)
    {
        dto.Pace = (int)memory.LastPosture.Pace;
        dto.Intent = (int)memory.LastPosture.Intent;
        dto.Risk = (int)memory.LastPosture.Risk;
        dto.TheaterPriority = (int)memory.LastPosture.TheaterPriority;
    }
    return dto;
}

public static DirectorMemory MemoryFromDto(DirectorMemoryDto dto)
{
    var memory = new DirectorMemory();
    if (dto == null) return memory;
    memory.LastFullRefreshDay = dto.LastFullRefreshDay;
    memory.CapitalDangerStreakDays = dto.CapitalDangerStreakDays;
    memory.DaysSinceLastBattle = dto.DaysSinceLastBattle;
    memory.LastSourceSignature = dto.LastSourceSignature;
    memory.RecentEventSummaries = dto.RecentEventSummaries ?? new System.Collections.Generic.List<string>();
    memory.LastPosture = new DirectorPosture
    {
        Pace = (CampaignPace)dto.Pace,
        Intent = (StrategicIntent)dto.Intent,
        Risk = (CollapseRisk)dto.Risk,
        TheaterPriority = (Theater)dto.TheaterPriority
    };
    return memory;
}
```

- [ ] **Step 4: Wire DirectorMemory into StrategicCoordinator save/load**

In `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`, add a parallel array:

```csharp
internal readonly DirectorMemory[] DirectorMemories = new DirectorMemory[2] { new DirectorMemory(), new DirectorMemory() };
```

Inside the save loop (`BuildSaveDto` or equivalent, around the per-faction section), add:
```csharp
                f.DirectorMemory = StrategicResilienceDirector.MemoryToDto(DirectorMemories[alliance]);
```

In the load loop, add:
```csharp
                DirectorMemories[alliance] = StrategicResilienceDirector.MemoryFromDto(f.DirectorMemory);
```

- [ ] **Step 5: Wire Director into UpdateOperationalProbe**

In `StrategicCoordinator.UpdateOperationalProbe(...)`, after `OperationalProbeRuntime.BuildInput(...)` produces the input but before `OperationalProbeLedger.Build(input)`, apply the Director's threshold modifiers:

```csharp
                var posture = DirectorMemories[alliance]?.LastPosture;
                if (posture != null)
                    StrategicResilienceDirector.ApplyTo(input.Options, posture);

                var output = OperationalProbeLedger.Build(input);
```

- [ ] **Step 6: Run all tests**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

- [ ] **Step 7: Build**

```bash
./build.sh
```

- [ ] **Step 8: Commit**

```bash
git add src/WhiskeyRealism/Strategic/StrategicResilienceDirector.cs src/WhiskeyRealism/Strategic/StrategicCoordinator.cs src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: persist DirectorMemory and apply posture threshold modifiers to probe options"
```

---

## Task 15: Daily Director publish + rolling 7-day cycle + real-second clamp

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`
- Modify: `src/WhiskeyRealism/Plugin.cs` (add config flag for Director if it should be tunable)
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Write failing test for the clamp**

Add to `tests` array:
```csharp
("director publish clamp suppresses second publish in same real second", DirectorPublishClampSuppressesSecondPublishInSameRealSecond),
```

Add the method:
```csharp
private static void DirectorPublishClampSuppressesSecondPublishInSameRealSecond()
{
    var clamp = new DirectorPublishClamp();
    var stamp = new System.DateTime(2026, 5, 5, 12, 0, 0);
    AssertTrue(clamp.TryPublish(stamp), "first publish in second should succeed");
    AssertTrue(!clamp.TryPublish(stamp.AddMilliseconds(50)), "second publish 50ms later should be suppressed");
    AssertTrue(clamp.TryPublish(stamp.AddSeconds(1).AddMilliseconds(1)), "publish past 1s boundary should succeed");
}
```

- [ ] **Step 2: Run, confirm fail**

`DirectorPublishClamp` not defined.

- [ ] **Step 3: Add the clamp helper**

Append to `src/WhiskeyRealism/Strategic/StrategicResilienceDirector.cs` (or new file `Strategic/DirectorPublishClamp.cs`):

```csharp
namespace WhiskeyRealism.Strategic
{
    public sealed class DirectorPublishClamp
    {
        private System.DateTime _lastPublishUtc = System.DateTime.MinValue;

        public bool TryPublish(System.DateTime nowUtc)
        {
            if ((nowUtc - _lastPublishUtc).TotalSeconds < 1.0) return false;
            _lastPublishUtc = nowUtc;
            return true;
        }
    }
}
```

If you put it in a new file, also add the csproj include.

- [ ] **Step 4: Wire the rolling cycle into the coordinator**

In `StrategicCoordinator.cs`, add a per-alliance clamp and a daily Director slice. Inside the per-alliance daily loop (around line 274 onward), after the existing front/formation/probe/etc updates, append:

```csharp
                if (operationalRuntimeReady)
                {
                    int daySerial = year * 372 + month * 31 + day;
                    int slot = ((daySerial % 7) + 7) % 7;
                    bool shouldRunSlice = StrategicCadencePolicy.ShouldRunWeeklyOrSourceChanged(
                        day,
                        DirectorSourceSignature(alliance),
                        _directorSourceSignatures[alliance],
                        forceRefresh: false);
                    bool sliceFired = false;

                    if (shouldRunSlice || slot < 7) // slot semantics: index by daySerial%7 — see spec §Cadence
                    {
                        var paceInput = BuildCampaignPaceInput(alliance, day, month, year);
                        var paceOutput = CampaignPaceLedger.Build(paceInput);
                        var personality = CICs[alliance]?.Effective(Eras[alliance]) ?? new PersonalityVector();
                        var newPosture = StrategicResilienceDirector.ProposePosture(alliance, paceOutput, personality);
                        newPosture.SourceSignature = DirectorSourceSignature(alliance);

                        if (_directorClamps[alliance].TryPublish(System.DateTime.UtcNow))
                        {
                            DirectorMemories[alliance].LastPosture = newPosture;
                            DirectorMemories[alliance].LastFullRefreshDay = daySerial;
                            DirectorMemories[alliance].LastSourceSignature = newPosture.SourceSignature;
                            sliceFired = true;
                            string previousSig = _directorPostureSignatures[alliance];
                            string newSig = newPosture.Pace + "/" + newPosture.Intent + "/" + newPosture.Risk;
                            if (newSig != previousSig)
                            {
                                Plugin.Log.LogInfo($"[CampaignPace] alliance={alliance} pace={newPosture.Pace} intent={newPosture.Intent} risk={newPosture.Risk} reason={newPosture.Reason}");
                                _directorPostureSignatures[alliance] = newSig;
                            }
                        }
                        else
                        {
                            newPosture.Stale = true;
                            DirectorMemories[alliance].LastPosture = DirectorMemories[alliance].LastPosture ?? newPosture;
                        }

                        _directorSourceSignatures[alliance] = newPosture.SourceSignature;
                    }
                }
```

Add fields near the other per-alliance signature fields:
```csharp
private readonly DirectorPublishClamp[] _directorClamps = new DirectorPublishClamp[2] { new DirectorPublishClamp(), new DirectorPublishClamp() };
private readonly string[] _directorSourceSignatures = new string[2];
private readonly string[] _directorPostureSignatures = new string[2];
```

Add helper methods `DirectorSourceSignature(int alliance)` (concatenates front/formation/defense/fiscal/construction/probe/era/chapter sigs) and `BuildCampaignPaceInput(int alliance, int day, int month, int year)` (reads vanilla `GameVars.alliance[i].nationalmorale` and `GamePrefs.breakmoraletrigger` via reflection guarded with try/catch + `OnceLog.Warning`).

- [ ] **Step 5: Run all tests**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

- [ ] **Step 6: Build**

```bash
./build.sh
```

- [ ] **Step 7: Commit**

```bash
git add src/WhiskeyRealism/Strategic/StrategicResilienceDirector.cs src/WhiskeyRealism/Strategic/StrategicCoordinator.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: wire Director publish clamp + advanced-game-day rolling cycle"
```

---

## Task 16: [CollapseRisk] telemetry + verbose Director trace gate

**Files:**
- Modify: `src/WhiskeyRealism/Plugin.cs`
- Modify: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`

- [ ] **Step 1: Add config flag**

In `src/WhiskeyRealism/Plugin.cs`, add alongside other ConfigEntry definitions:

```csharp
        public ConfigEntry<bool> DirectorVerboseTrace;
```

In `Awake` (or wherever `Config.Bind(...)` calls live):
```csharp
            DirectorVerboseTrace = Config.Bind(
                "Telemetry",
                "Director Verbose Trace",
                false,
                "When true, logs detailed Director slice traces every advanced game day. Default off — only [CampaignPace] and [CollapseRisk] level-change lines emit.");
```

- [ ] **Step 2: Emit `[CollapseRisk]` on level change**

In the Director slice block from Task 15, add after the posture is built:

```csharp
                        var prevRisk = _directorRiskLevels[alliance];
                        if (newPosture.Risk != prevRisk)
                        {
                            Plugin.Log.LogInfo($"[CollapseRisk] alliance={alliance} risk={newPosture.Risk} pace={newPosture.Pace}");
                            _directorRiskLevels[alliance] = newPosture.Risk;
                        }
                        if (Plugin.Instance.DirectorVerboseTrace.Value)
                        {
                            Plugin.Log.LogInfo($"[Director:trace] alliance={alliance} signature={newPosture.SourceSignature} stale={newPosture.Stale}");
                        }
```

Add field:
```csharp
private readonly CollapseRisk[] _directorRiskLevels = new CollapseRisk[2] { CollapseRisk.Low, CollapseRisk.Low };
```

- [ ] **Step 3: Build**

```bash
./build.sh
```

- [ ] **Step 4: Run all tests**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

- [ ] **Step 5: Commit**

```bash
git add src/WhiskeyRealism/Plugin.cs src/WhiskeyRealism/Strategic/StrategicCoordinator.cs
git commit -m "feat: emit [CampaignPace]/[CollapseRisk] telemetry and gate verbose Director trace"
```

---

## Task 17: Director-modulated transfer budget

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/DirectorPosture.cs`
- Modify: `src/WhiskeyRealism/Strategic/StrategicResilienceDirector.cs`
- Modify: `src/WhiskeyRealism/Strategic/FrontSectorLedger.cs` (option pass-through; add modifiers to `FrontLedgerOptions` if not present)
- Modify: `src/WhiskeyRealism/Patches/TransferOfUnitsPatch.cs`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

> **Knob bounds (no personality delta exists for these — fixed caps):** `MinimumHoldRatioModifier ∈ [-0.05, +0.10]`, `ConcessionRatioModifier ∈ [-0.05, +0.10]`. Posture intent: `TooFastCollapse` and `Overheated` raise CSA hold thresholds (`+0.10`); `LateWarPressure` for Union sources lowers concession willingness (`+0.05`); `TooQuiet` slightly relaxes (`-0.03`).

- [ ] **Step 1: Extend DirectorPosture**

In `src/WhiskeyRealism/Strategic/DirectorPosture.cs`, add:

```csharp
        public float MinimumHoldRatioModifier;
        public float ConcessionRatioModifier;
```

- [ ] **Step 2: Write failing test**

Add to `tests` array:
```csharp
("director raises csa hold ratio under too fast collapse", DirectorRaisesCsaHoldRatioUnderTooFastCollapse),
```

Add the method:
```csharp
private static void DirectorRaisesCsaHoldRatioUnderTooFastCollapse()
{
    var posture = StrategicResilienceDirector.ProposePosture(
        allianceId: 1,
        pace: new CampaignPaceOutput { Pace = CampaignPace.TooFastCollapse, Risk = CollapseRisk.Critical, IntentBlockedFromPreserve = false },
        personality: new PersonalityVector());
    AssertTrue(posture.MinimumHoldRatioModifier > 0f,
        "TooFastCollapse for CSA must raise MinimumHoldRatio — was " + posture.MinimumHoldRatioModifier);
    AssertTrue(posture.MinimumHoldRatioModifier <= 0.10f,
        "MinimumHoldRatioModifier capped at +0.10");
}
```

- [ ] **Step 3: Run, confirm fail**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

- [ ] **Step 4: Populate the modifier in ProposePosture**

In `StrategicResilienceDirector.cs`, extend the switch in `ApplyThresholdModifiers(...)`:

```csharp
            float holdMod = 0f, concessionMod = 0f;
            switch (posture.Pace)
            {
                case CampaignPace.TooFastCollapse:
                    holdMod = +0.10f;
                    concessionMod = +0.10f;
                    break;
                case CampaignPace.Overheated:
                    holdMod = +0.05f;
                    break;
                case CampaignPace.LateWarPressure:
                    if (posture.AllianceId == 0) concessionMod = -0.03f; // Union: easier concentration from safe sources
                    else                          holdMod = +0.05f;       // CSA late: hold harder
                    break;
                case CampaignPace.TooQuiet:
                    holdMod = -0.03f;
                    break;
            }
            posture.MinimumHoldRatioModifier = Clamp(holdMod, -0.05f, +0.10f);
            posture.ConcessionRatioModifier = Clamp(concessionMod, -0.05f, +0.10f);
```

(Place this *before* the existing per-pace switch's other modifier writes, or merge into the same switch — keep it readable.)

- [ ] **Step 5: Wire FrontSectorLedger.Build to accept the modifier**

In `src/WhiskeyRealism/Strategic/FrontSectorLedger.cs`, the existing `FrontLedgerOptions` class already has `MinimumHoldRatio` (line ~35) and `ConcessionRatio` (line ~37). The runtime `FrontSectorRuntime.Build(...)` (or wherever options are constructed) should add the modifiers from the active posture:

```csharp
var posture = StrategicCoordinator.Instance?.DirectorMemories?[alliance]?.LastPosture;
var options = new FrontLedgerOptions
{
    MinimumHoldRatio = Clamp(0.9f + (posture?.MinimumHoldRatioModifier ?? 0f), 0.5f, 1.0f),
    ConcessionRatio = Clamp(0.55f + (posture?.ConcessionRatioModifier ?? 0f), 0.4f, 0.7f),
    CriticalHoldRatioBonus = 0.2f
};
```

(Add static `Clamp` helper if not present in the runtime, mirroring the one in `StrategicResilienceDirector`.)

- [ ] **Step 6: Run all tests**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```
Expected: existing front-budget tests still PASS (default modifier is 0). New test PASS.

- [ ] **Step 7: Build**

```bash
./build.sh
```

- [ ] **Step 8: Commit**

```bash
git add src/WhiskeyRealism/Strategic/DirectorPosture.cs src/WhiskeyRealism/Strategic/StrategicResilienceDirector.cs src/WhiskeyRealism/Strategic/FrontSectorLedger.cs src/WhiskeyRealism/Strategic/FrontSectorRuntime.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: Director modulates FrontSectorLedger transfer/hold thresholds"
```

---

## Task 18: Director-modulated formation directive gates

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/DirectorPosture.cs`
- Modify: `src/WhiskeyRealism/Strategic/StrategicResilienceDirector.cs`
- Modify: `src/WhiskeyRealism/Strategic/FormationDirectiveLedger.cs`
- Modify: `src/WhiskeyRealism/Strategic/FormationDirectiveRuntime.cs`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

> **Knob bounds (no personality delta exists — fixed caps):** `RecoverFloorModifier ∈ [-0.05, +0.10]` (raises the morale/readiness floor below which a formation flips to `Recover`); `MassRatioModifier ∈ [-0.10, +0.10]` (adjusts the friendly/enemy ratio gate for `Counterstroke`/`Mass`). Posture intent: `Overheated` raises Recover floor; `LateWarPressure` for Union lowers Mass ratio; `TooFastCollapse` for CSA raises Recover floor + raises Mass ratio (don't gamble).

- [ ] **Step 1: Extend DirectorPosture**

```csharp
        public float RecoverFloorModifier;
        public float MassRatioModifier;
```

- [ ] **Step 2: Add an Options struct on FormationDirectiveLedger**

In `src/WhiskeyRealism/Strategic/FormationDirectiveLedger.cs`, add a public class near the top:

```csharp
    public sealed class FormationDirectiveOptions
    {
        public float RecoverMoraleFloor = 0.35f;
        public float RecoverReadinessFloor = 0.35f;
        public float DivisionAttackRatio = 1.5f;
        public float CorpsAttackRatio = 1.2f;
        public float ArmyAttackRatio = 1.05f;
    }
```

Extend the static `Build(...)` to accept an optional `FormationDirectiveOptions options = null` parameter. Default-construct if null. Replace the hardcoded `0.35f` and ratio constants in `ResolveTopDirective` and `AttackRiskPasses` with `options.X`.

- [ ] **Step 3: Write failing tests**

Add to `tests` array:
```csharp
("director raises recover floor under overheated", DirectorRaisesRecoverFloorUnderOverheated),
("director relaxes union mass ratio under late war pressure", DirectorRelaxesUnionMassRatioUnderLateWarPressure),
```

Add the methods:
```csharp
private static void DirectorRaisesRecoverFloorUnderOverheated()
{
    var posture = StrategicResilienceDirector.ProposePosture(
        allianceId: 0,
        pace: new CampaignPaceOutput { Pace = CampaignPace.Overheated, Risk = CollapseRisk.Low },
        personality: new PersonalityVector());
    AssertTrue(posture.RecoverFloorModifier > 0f, "overheated must raise recover floor");
}

private static void DirectorRelaxesUnionMassRatioUnderLateWarPressure()
{
    var posture = StrategicResilienceDirector.ProposePosture(
        allianceId: 0,
        pace: new CampaignPaceOutput { Pace = CampaignPace.LateWarPressure, Risk = CollapseRisk.Low },
        personality: new PersonalityVector());
    AssertTrue(posture.MassRatioModifier < 0f, "Union late-war pressure must lower mass ratio gate");
}
```

- [ ] **Step 4: Run, confirm fail**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

- [ ] **Step 5: Populate modifiers in ProposePosture**

Append to the `ApplyThresholdModifiers` switch:

```csharp
            float recoverMod = 0f, massRatioMod = 0f;
            switch (posture.Pace)
            {
                case CampaignPace.Overheated:
                    recoverMod = +0.07f;
                    massRatioMod = +0.05f;
                    break;
                case CampaignPace.TooFastCollapse:
                    recoverMod = +0.10f;
                    massRatioMod = +0.10f;
                    break;
                case CampaignPace.LateWarPressure:
                    if (posture.AllianceId == 0) massRatioMod = -0.10f;
                    else                          recoverMod = +0.05f;
                    break;
                case CampaignPace.TooQuiet:
                    recoverMod = -0.03f;
                    break;
            }
            posture.RecoverFloorModifier = Clamp(recoverMod, -0.05f, +0.10f);
            posture.MassRatioModifier   = Clamp(massRatioMod, -0.10f, +0.10f);
```

- [ ] **Step 6: Wire FormationDirectiveRuntime to pass posture-derived options**

In `src/WhiskeyRealism/Strategic/FormationDirectiveRuntime.cs`, when calling `FormationDirectiveLedger.Build(...)`, build an options object from the active posture:

```csharp
var posture = StrategicCoordinator.Instance?.DirectorMemories?[alliance]?.LastPosture;
var options = new FormationDirectiveOptions
{
    RecoverMoraleFloor    = Clamp(0.35f + (posture?.RecoverFloorModifier ?? 0f), 0.20f, 0.50f),
    RecoverReadinessFloor = Clamp(0.35f + (posture?.RecoverFloorModifier ?? 0f), 0.20f, 0.50f),
    DivisionAttackRatio   = Clamp(1.5f + (posture?.MassRatioModifier ?? 0f), 1.20f, 1.80f),
    CorpsAttackRatio      = Clamp(1.2f + (posture?.MassRatioModifier ?? 0f), 1.00f, 1.50f),
    ArmyAttackRatio       = Clamp(1.05f + (posture?.MassRatioModifier ?? 0f), 0.90f, 1.30f)
};
var ledger = FormationDirectiveLedger.Build(snapshots, era.Stage, planTargetAreaKey, options);
```

- [ ] **Step 7: Run all tests**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```
Expected: existing formation-directive tests still PASS (defaults preserved). New tests PASS.

- [ ] **Step 8: Build**

```bash
./build.sh
```

- [ ] **Step 9: Commit**

```bash
git add src/WhiskeyRealism/Strategic/DirectorPosture.cs src/WhiskeyRealism/Strategic/StrategicResilienceDirector.cs src/WhiskeyRealism/Strategic/FormationDirectiveLedger.cs src/WhiskeyRealism/Strategic/FormationDirectiveRuntime.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: Director modulates formation Recover floor + Mass ratio gates"
```

---

## Task 19: Director-modulated fiscal + construction biases

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/DirectorPosture.cs`
- Modify: `src/WhiskeyRealism/Strategic/StrategicResilienceDirector.cs`
- Modify: `src/WhiskeyRealism/Strategic/Fiscal/FiscalConstructionScorer.cs`
- Modify: `src/WhiskeyRealism/Strategic/Construction/ConstructionSteeringScorer.cs`
- Modify: `src/WhiskeyRealism/Strategic/Fiscal/FiscalRuntime.cs`
- Modify: `src/WhiskeyRealism/Strategic/Construction/ConstructionRuntime.cs`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

> **Knob bounds (no personality delta exists — fixed caps):** `SupplyConstructionBias ∈ [-0.20, +0.40]`, `LogisticsBias ∈ [-0.20, +0.40]`, `ExpansionDamper ∈ [0.0, +0.50]` (multiplier suppression for private-economy expansion in stressed states). Posture intent: `CollapseRisk.Critical` strongly favors supply (+0.40); `Overheated` favors recovery/supply (+0.25); `TooQuiet` healthy fiscal favors logistics (+0.30); `TooFastCollapse` adds heavy expansion damper (+0.50).

- [ ] **Step 1: Extend DirectorPosture**

```csharp
        public float SupplyConstructionBias;
        public float LogisticsBias;
        public float ExpansionDamper;
```

- [ ] **Step 2: Write failing tests**

Add to `tests` array:
```csharp
("director critical risk strongly favors supply construction", DirectorCriticalRiskFavorsSupplyConstruction),
("director too quiet healthy fiscal favors logistics", DirectorTooQuietFavorsLogistics),
("director too fast collapse damps expansion", DirectorTooFastCollapseDampsExpansion),
```

Add the methods:
```csharp
private static void DirectorCriticalRiskFavorsSupplyConstruction()
{
    var posture = StrategicResilienceDirector.ProposePosture(
        allianceId: 1,
        pace: new CampaignPaceOutput { Pace = CampaignPace.TooFastCollapse, Risk = CollapseRisk.Critical },
        personality: new PersonalityVector());
    AssertTrue(posture.SupplyConstructionBias >= 0.30f,
        "Critical risk must strongly favor supply — was " + posture.SupplyConstructionBias);
}

private static void DirectorTooQuietFavorsLogistics()
{
    var posture = StrategicResilienceDirector.ProposePosture(
        allianceId: 0,
        pace: new CampaignPaceOutput { Pace = CampaignPace.TooQuiet, Risk = CollapseRisk.Low },
        personality: new PersonalityVector());
    AssertTrue(posture.LogisticsBias >= 0.20f, "TooQuiet must favor logistics");
}

private static void DirectorTooFastCollapseDampsExpansion()
{
    var posture = StrategicResilienceDirector.ProposePosture(
        allianceId: 1,
        pace: new CampaignPaceOutput { Pace = CampaignPace.TooFastCollapse, Risk = CollapseRisk.Critical },
        personality: new PersonalityVector());
    AssertTrue(posture.ExpansionDamper >= 0.30f, "TooFastCollapse must damp expansion");
}
```

- [ ] **Step 3: Run, confirm fail**

- [ ] **Step 4: Populate modifiers in ProposePosture**

Append:
```csharp
            float supplyBias = 0f, logisticsBias = 0f, expansionDamper = 0f;
            if (posture.Risk == CollapseRisk.Critical) supplyBias += 0.40f;
            if (posture.Risk == CollapseRisk.Elevated) supplyBias += 0.20f;
            switch (posture.Pace)
            {
                case CampaignPace.Overheated:
                    supplyBias += 0.25f;
                    expansionDamper += 0.20f;
                    break;
                case CampaignPace.TooQuiet:
                    logisticsBias += 0.30f;
                    break;
                case CampaignPace.TooFastCollapse:
                    expansionDamper += 0.50f;
                    break;
                case CampaignPace.LateWarPressure:
                    if (posture.AllianceId == 0) logisticsBias += 0.20f;
                    break;
            }
            posture.SupplyConstructionBias = Clamp(supplyBias, -0.20f, +0.40f);
            posture.LogisticsBias          = Clamp(logisticsBias, -0.20f, +0.40f);
            posture.ExpansionDamper        = Clamp(expansionDamper, 0f, +0.50f);
```

- [ ] **Step 5: Apply biases in scorers**

In `FiscalConstructionScorer.cs` and `ConstructionSteeringScorer.cs`, the score computation already weights candidate categories. Add a multiplicative pass at the end of score computation:

```csharp
// Apply Director biases (caller passes posture; scorers stay pure).
public static float ApplyDirectorBiases(float baseScore, ConstructionCandidate candidate, DirectorPosture posture)
{
    if (posture == null) return baseScore;
    float multiplier = 1f;
    if (candidate.IsSupplyOrRecovery) multiplier += posture.SupplyConstructionBias;
    if (candidate.IsLogisticsOrRail)  multiplier += posture.LogisticsBias;
    if (candidate.IsPrivateExpansion) multiplier *= Math.Max(0.05f, 1f - posture.ExpansionDamper);
    return Math.Max(0f, baseScore * multiplier);
}
```

> The exact `ConstructionCandidate` type and its `IsSupplyOrRecovery`/`IsLogisticsOrRail`/`IsPrivateExpansion` flags may not exist by those names in shipped code. Read `ConstructionModels.cs` and the existing scorer first; adapt the predicates to the candidate-classification fields that already exist (e.g., `IIPCategory`, `BuildingType`). Do not invent fields without grepping for an equivalent.

- [ ] **Step 6: Wire FiscalRuntime / ConstructionRuntime to pass posture**

In `FiscalRuntime.UpdateFiscalIntent` and `ConstructionRuntime.Update*`, retrieve `StrategicCoordinator.Instance.DirectorMemories[alliance].LastPosture` and pass to scorer's bias-application call.

- [ ] **Step 7: Run all tests**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```
Expected: all PASS.

- [ ] **Step 8: Build**

```bash
./build.sh
```

- [ ] **Step 9: Commit**

```bash
git add src/WhiskeyRealism/Strategic/DirectorPosture.cs src/WhiskeyRealism/Strategic/StrategicResilienceDirector.cs src/WhiskeyRealism/Strategic/Fiscal/ src/WhiskeyRealism/Strategic/Construction/ tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: Director biases fiscal supply/logistics scoring + dampens expansion under collapse"
```

---

## Task 20: Director-modulated defense biases

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/DirectorPosture.cs`
- Modify: `src/WhiskeyRealism/Strategic/StrategicResilienceDirector.cs`
- Modify: `src/WhiskeyRealism/Strategic/DefenseIntentInput.cs`
- Modify: `src/WhiskeyRealism/Strategic/DefenseIntentRuntime.cs`
- Modify: `src/WhiskeyRealism/Patches/DefensiveOpsPatch.cs`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

> **Knob bounds (no personality delta exists — fixed caps):** `GuardBudgetFractionModifier ∈ [-0.05, +0.05]` (default fraction is 0.10, so range is effectively [0.05, 0.15]); `CapitalDefenseBudgetModifier ∈ [-0.05, +0.10]` (raises the strength fraction reserved for capital defense). Posture intent: `TooFastCollapse` raises capital defense budget (+0.10); `Overheated` raises guard budget (+0.05); `LateWarPressure` Union slightly lowers guard for source sectors (-0.03).

- [ ] **Step 1: Extend DirectorPosture**

```csharp
        public float GuardBudgetFractionModifier;
        public float CapitalDefenseBudgetModifier;
```

- [ ] **Step 2: Write failing tests**

Add to `tests` array:
```csharp
("director raises capital defense budget under too fast collapse", DirectorRaisesCapitalDefenseBudgetUnderTooFastCollapse),
("director lowers union guard budget under late war pressure", DirectorLowersUnionGuardUnderLateWarPressure),
```

Add the methods:
```csharp
private static void DirectorRaisesCapitalDefenseBudgetUnderTooFastCollapse()
{
    var posture = StrategicResilienceDirector.ProposePosture(
        allianceId: 1,
        pace: new CampaignPaceOutput { Pace = CampaignPace.TooFastCollapse, Risk = CollapseRisk.Critical },
        personality: new PersonalityVector());
    AssertTrue(posture.CapitalDefenseBudgetModifier >= 0.05f,
        "TooFastCollapse must raise capital defense budget");
}

private static void DirectorLowersUnionGuardUnderLateWarPressure()
{
    var posture = StrategicResilienceDirector.ProposePosture(
        allianceId: 0,
        pace: new CampaignPaceOutput { Pace = CampaignPace.LateWarPressure, Risk = CollapseRisk.Low },
        personality: new PersonalityVector());
    AssertTrue(posture.GuardBudgetFractionModifier <= 0f,
        "Union late-war pressure can slightly lower guard for source-sector concentration");
}
```

- [ ] **Step 3: Run, confirm fail**

- [ ] **Step 4: Populate in ProposePosture**

```csharp
            float guardMod = 0f, capitalMod = 0f;
            switch (posture.Pace)
            {
                case CampaignPace.TooFastCollapse:
                    capitalMod = +0.10f;
                    guardMod = +0.03f;
                    break;
                case CampaignPace.Overheated:
                    guardMod = +0.05f;
                    break;
                case CampaignPace.LateWarPressure:
                    if (posture.AllianceId == 0) guardMod = -0.03f;
                    else                          capitalMod = +0.05f;
                    break;
            }
            posture.GuardBudgetFractionModifier   = Clamp(guardMod, -0.05f, +0.05f);
            posture.CapitalDefenseBudgetModifier  = Clamp(capitalMod, -0.05f, +0.10f);
```

- [ ] **Step 5: Apply guard modifier in DefenseIntentRuntime**

In `DefenseIntentRuntime.Build(...)` (or wherever the `DefenseIntentInput` is constructed before calling `DefenseIntentLedger.Build`), add:

```csharp
var posture = StrategicCoordinator.Instance?.DirectorMemories?[allianceId]?.LastPosture;
input.GuardBudgetFraction = Clamp(0.10f + (posture?.GuardBudgetFractionModifier ?? 0f), 0.05f, 0.15f);
```

- [ ] **Step 6: Apply capital modifier in DefensiveOpsPatch**

In `src/WhiskeyRealism/Patches/DefensiveOpsPatch.cs`, where the patch reads the existing `CapitalDefenseBudgetFraction` config or constant, add the modifier:

```csharp
var posture = StrategicCoordinator.Instance?.DirectorMemories?[allianceId]?.LastPosture;
float capitalFraction = Clamp(baseCapitalFraction + (posture?.CapitalDefenseBudgetModifier ?? 0f), 0.05f, 0.30f);
```

> Look at how DefensiveOpsPatch currently sizes its capital defense package (around the `[Patch:DefensiveOps]` log line). The modifier composes multiplicatively or additively depending on the existing math — match the existing shape rather than inventing one.

- [ ] **Step 7: Run all tests**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

- [ ] **Step 8: Build**

```bash
./build.sh
```

- [ ] **Step 9: Commit**

```bash
git add src/WhiskeyRealism/Strategic/DirectorPosture.cs src/WhiskeyRealism/Strategic/StrategicResilienceDirector.cs src/WhiskeyRealism/Strategic/DefenseIntentInput.cs src/WhiskeyRealism/Strategic/DefenseIntentRuntime.cs src/WhiskeyRealism/Patches/DefensiveOpsPatch.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: Director modulates defense guard/capital budgets per pace and risk"
```

---

## Task 21: Build, deploy, verify SHA-256

**Files:** none (verification step)

- [ ] **Step 1: Run full test suite one last time**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```
Expected: all PASS, no skipped tests.

- [ ] **Step 2: Build**

```bash
./build.sh
```
Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Confirm game is closed**

```bash
pgrep -fa "Grand Tactician" || echo "game not running"
```
Expected: "game not running". If a process is found, ask the user to close it.

- [ ] **Step 4: Deploy**

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
```
If this fails with `Invalid argument`, the game is still running — ask the user to close it.

- [ ] **Step 5: Verify SHA-256 matches**

```bash
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```
Expected: identical SHA-256 lines.

- [ ] **Step 6: Record the deployed hash**

Append to the implementation log at the bottom of this plan (or to `docs/handoff.md` "What just shipped"):
```
Director slice deployed DLL SHA-256: <paste output>
```

---

## Task 22: Runtime smoke

**Files:** none (in-game observation)

The game must be launched. The user runs the smoke; this task documents what to look for in `BepInEx/LogOutput.log`.

- [ ] **Step 1: Launch game, start a fresh CSA W&L career**

Confirm the W&L command-selection popup behaves as before — #22 must continue to work.

- [ ] **Step 2: Tail the log and watch for first-fire markers**

```bash
tail -F "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Expected within the first in-game month:
- `[once:strategic-cadence]` — coordinator wired
- `[CampaignPace] alliance=0 pace=Stable ...`
- `[CampaignPace] alliance=1 pace=Stable ...`
- `[OperationalProbe] alliance=... decision=Probe ...` (still works, now gated by ContactEvidence + posture-modulated thresholds)
- `[BattleHistory] ...` once the first battle resolves
- `[FrontLedger]`, `[FormationDirective]`, `[DefenseIntent]` summaries continue to emit normally (defaults preserved when posture is `Stable`, modulated under non-Stable paces)
- `[Patch:DefensiveOps]` capital-defense lines reflect posture-modulated capital fraction when posture is non-Stable

Must NOT see:
- `IndexOutOfRangeException` against any per-alliance array
- `[OperationalProbe] runner failed:` lines
- repeated Harmony reflection warnings
- `[DefenseIntent] custom-order ... threat=asset:` (the anti-zerg invariant from prior slices)
- transfer-budget thrash from posture flipping every cycle (posture changes should be rare; signature stability is part of the contract)

- [ ] **Step 3: Trigger a battle, confirm Pace/Risk telemetry**

After a real battle: confirm a `[CollapseRisk]` line emits when alliance morale crosses `breakmoraletrigger × 1.5` downward (this may not happen in a short smoke run; document as deferred to long-run smoke).

- [ ] **Step 4: Save and reload, confirm DirectorMemory round-trips**

Save the campaign, exit to menu, reload. Confirm `DirectorMemories[0]` and `[1]` reload from the sidecar without warning lines about missing `directorMemory` field. Confirm next `[CampaignPace]` line emits with the same pace/risk it had before save.

- [ ] **Step 5: Verify against pre-cleanup save**

Take an existing pre-Director save (before this slice). Load it. Confirm:
- No exception on load
- One-time `OnceLog.Info` (acceptable) about missing `directorMemory` field, OR clean reset to defaults
- Campaign continues to advance time

- [ ] **Step 6: Update handoff.md**

Add a "What just shipped" entry to `docs/handoff.md` summarizing:
- DLL SHA-256
- Patches added/changed
- Smoke results (which markers appeared, which didn't)
- Any bugs caught and fixed during smoke

- [ ] **Step 7: Final commit (if any docs changed)**

```bash
git add docs/handoff.md docs/patch-catalog.md docs/superpowers/plans/2026-05-05-strategic-resilience-director.md
git commit -m "docs: record Director slice ship + smoke results"
```

- [ ] **Step 8: Archive the spec and plan**

After smoke confirms everything works, follow `AGENTS.md` "Doc lifecycle":

```bash
git mv docs/superpowers/specs/2026-05-05-strategic-resilience-director-design.md docs/superpowers/specs/archive/
git mv docs/superpowers/plans/2026-05-05-strategic-resilience-director.md docs/superpowers/plans/archive/
```

Update the corresponding `archive/README.md` indexes.

```bash
git add docs/superpowers/specs/archive/README.md docs/superpowers/plans/archive/README.md
git commit -m "docs: archive Director spec and plan after ship"
```

---

## Implementation Log

### 2026-05-05 — Tasks 1–21 implemented and deployed

**Branch:** `feat/strategic-resilience-director` (20 commits: `2349d54` → `8b7b94a`)

**Deployed DLL SHA-256:** `47549752bff914a7ddb32e5ef98869bd0a5f47eca4c7acc0904e0d62082eac65` (357376 bytes; both `dist/` and `BepInEx/plugins/` files match).

**Tests:** 234 PASS / 0 FAIL.

**Build:** 0 warnings / 0 errors.

**Commits in implementation order:**

1. `2349d54` feat: add BattleHistoryQuery spatial+date helper for Director ledgers
2. `d53b706` feat: add TheaterPressureView aggregating FrontSectorLedger by theater
3. `8d86829` fix: reset FormationPressureSummary at the start of RecomputePressure
4. `49148c7` feat: add PhaseTruthLedger so stale objectives stop driving plans
5. `d484b5a` feat: add ContactEvidenceLedger so probes need real contact to escalate
6. `bc1ed40` feat: route operational probe through OffensiveAvailabilityWrapper mirroring vanilla gates
7. `e06fd7c` fix: gate operational-probe escalation on ContactEvidence (no zero-enemy escalation)
8. `b2fd5b7` refactor: delete unused TheaterCommander class and legacy DTO field
9. `beb23c4` refactor: make StrategicCoordinator the single owner of operational probe state
10. `36e97e1` feat: add DirectorPosture types and DirectorMemory persistence DTO
11. `308f7ff` feat: add CampaignPaceLedger bound to vanilla nationalmorale + chapter scalars
12. `95b3f33` feat: add StrategicResilienceDirector with personality-clamped threshold modifiers
13. `d88b799` feat: route CIC plan review through PhaseTruthLedger
14. `e222dd7` feat: persist DirectorMemory and apply posture threshold modifiers to probe options
15. `cba7062` feat: wire Director publish clamp + advanced-game-day rolling cycle
16. `b74ea00` feat: emit [CampaignPace]/[CollapseRisk] telemetry and gate verbose Director trace
17. `aa421b6` feat: Director modulates FrontSectorLedger transfer/hold thresholds
18. `e0ffb84` feat: Director modulates formation Recover floor + Mass ratio gates
19. `7a88c34` feat: Director biases fiscal supply/logistics scoring + dampens expansion under collapse
20. `8b7b94a` feat: Director modulates defense guard/capital budgets per pace and risk

**Notable in-flight decisions:**

- Task 6 (OffensiveAvailabilityWrapper) intentionally drops the old `inbattle`/`onretreat`/`garrisonreference` ad-hoc guards in favor of vanilla parity. `Escalate` decisions on units already in `unitsinoffensiveoperations` will now be silently blocked by gate 5 of `IsUnitAvailableForOffensiveOperations` — watch smoke for any visible regression in escalation cadence; if observed, the directive overlay still flags `Mass`/`Counterstroke` even when MoveUnitTo is suppressed.
- Task 13 split the `ReviewPlanWithTruth` switch out into a new `CicReviewRouter` static class so the routing logic could be tested in the console harness without dragging BepInEx/HarmonyLib reflection into test compile.
- Task 15 used a single shared `DirectorPublishClamp` (not per-alliance) per spec — at most one full publish per real second across all alliances combined.
- Task 19's bias-classification predicates ("supply", "logistics", "private expansion") use existing `buildingName` substring matching ("depot"/"hospital", "market"/"rail", "bank"/"factory"/"foundry"/"industrial"/"shipyard"/"naval"). No new fields added to candidate types.

**Pending: Task 22 (runtime smoke).** Game launch + log inspection per plan checklist.
