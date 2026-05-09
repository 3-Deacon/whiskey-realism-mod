# Tactical Orchestrator O2 — Intent Inference + Adversarial Loop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Each side's `ArmyOrchestrator` builds a `TacticalIntentModel` of the opposing army's plan from visible state and feeds it into both playbook selection (`OpposingCommanderHint`) and replan trigger evaluation (`EnemyMainEffortShiftConfidenceWeighted`). A new `ArmyTickCycle` driver wires per-tick evidence refresh + replan-loop into the existing `TacticalBattleCoordinator.Tick()`. Both sides' plans become responses to inferred opposing plans; phase advances and replans actually fire.

**Architecture:** `TacticalIntentModel` is a pure value type — built each tick by `ArmyIntentInference.Build(ownEvidence, enemyVisible)` from existing-ledger inputs (no omniscient reads). `ArmyTickCycle` is a pure driver: refreshes evidence, builds the intent model, evaluates replan triggers (rate-limited by `MinReplanSeconds`), and replans when triggered. The runtime partial of `TacticalBattleCoordinator` adds `ArmyEvidenceBuilder` to extract evidence from vanilla `BattleUnits` each tick. `ArmyOrchestrator` extends with history-tracking fields so triggers can compare current vs prior odds/strength. Patches are unchanged from O1 — #44 still reads `ArmyOrchestrator.CurrentMacroAi`; replans flip the plan id which the macro mapping naturally picks up.

**Tech Stack:** C# netstandard2.1, BepInEx 5.4.x x64 + HarmonyX (NuGet), Unity 2021.3.16f1 Mono x86-64. Pure types under `src/WhiskeyRealism/Tactical/Orchestrator/`. Tests live in `tests/WhiskeyRealism.Tests/Program.cs` as static methods registered in `Main()`'s tuple — **no NUnit**. Helpers: `AssertEqual<T>(T,T,string)`, `AssertTrue`, `AssertFalse`, `AssertNear`, `AssertContains`, `AssertThrows`. Existing test seam `SeedCatalog.AllHistoricalAndGeneric()` provides a fully-seeded catalog. Run: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`.

---

## Source-of-truth citations

- **Umbrella spec:** `docs/superpowers/specs/2026-05-08-tactical-battle-orchestrator-design.md` — O2 row in §"Phasing", §"Adversarial intent inference + personality", §"Decision flow + cadence".
- **O2 sketch (precursor):** `docs/superpowers/plans/2026-05-08-tactical-orchestrator-o2-intent-sketch.md` — sketch this plan promotes from.
- **O1 archived plan (foundation):** `docs/superpowers/plans/archive/2026-05-08-tactical-orchestrator-o1-army.md` — defines the entities O2 builds on.
- **Slice A personality stack:** `docs/superpowers/specs/archive/2026-05-02-strategic-brain-design.md` — `PersonalityVector` semantics.

---

## Pre-flight verification

- [ ] **Step P1: Confirm O1 is merged on main.**

```bash
git log --oneline -5
ls src/WhiskeyRealism/Tactical/Orchestrator/
```

Expected: HEAD includes the O1 merge (`684698f`) and follow-up commits. Orchestrator dir contains `ArmyOrchestrator.cs`, `ArmyReplanTriggers.cs`, `BuiltInPlaybooks.cs`, `Playbooks/` subdir with 14 files, etc.

- [ ] **Step P2: Confirm harness baseline.**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -cE "^PASS "
```

Expected: `584` (post-O1 baseline).

- [ ] **Step P3: Confirm deployed DLL hash matches main DLL.**

```bash
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: both `979f4d64...` (matching O1 deploy). If they differ, rebuild and redeploy from main before starting O2.

---

## File structure

### New files

```
src/WhiskeyRealism/Tactical/Orchestrator/
├── TacticalIntentModel.cs              (Task 1 — intent struct + InferredIntent + EvidenceTag enums)
├── EnemyVisibleState.cs                (Task 2 — input bundle for inference)
├── ArmyIntentInference.cs              (Task 3 — pure scorer: visible state → intent model)
├── ArmyTickCycle.cs                    (Task 5 — pure driver: refresh + replan loop)
└── ArmyEvidenceBuilder.cs              (Task 6 — runtime partial; extracts vanilla evidence)
```

```
tests/WhiskeyRealism.Tests/Program.cs   (extended in Tasks 1, 2, 3, 4, 5)
```

### Modified files

```
src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs                     — Task 4 (history fields + accept intent on Replan)
src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs    — Task 7 (drive ArmyTickCycle from Tick())
src/WhiskeyRealism/Plugin.cs                                                     — Task 7 (Enable Tactical Orchestrator Intent Inference flag)
tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj                           — Tasks 1, 2, 3, 5 (4 new Compile Include entries)
docs/handoff.md, MEMORY.md                                                       — Task 9 (post-smoke updates)
```

### Untouched

- All Patches/* files. O1's #44 rewire continues to work; #47 demotion stays in place.
- `BuiltInPlaybooks`, `TacticalPlaybook`, `TacticalPlaybookCatalog`, all 14 concrete playbooks. The catalog reads `OpposingCommanderHint` from `PlaybookContext`, which O2 populates from the inferred intent.
- `ArmyReplanTriggers`. Already has `EnemyMainEffortShiftConfidenceWeighted` field — O2 just feeds it real data.

---

## Implementation tasks

### Task 1: TacticalIntentModel + supporting enums

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalIntentModel.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs` — add 3 tests + 3 tuple entries.
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` — 1 `<Compile Include>` entry.

The intent model is a pure read-only struct. `InferredIntent` and `EvidenceTag` enums live alongside in the same file (small, tightly coupled — same precedent as `BattlePlanId` + `BattlePhase` colocated in `TacticalBattlePlan.cs`).

- [ ] **Step 1: Write failing tests.**

```csharp
// In Program.cs, append three new test methods at the end of the class
// (after the Task 13 follow-up tests from O1) and three tuple entries
// in Main()'s array alongside the army orchestrator tests.

private static void TacticalIntentModelRecordsAllFields()
{
    var model = new TacticalIntentModel(
        primaryIntent: InferredIntent.Attack,
        inferredMainEffort: 3,
        confidence01: 0.62f,
        ageSeconds: 12.5f,
        supportingEvidence: new[] { EvidenceTag.SectorConcentration, EvidenceTag.ReserveUncommitted });
    AssertEqual(InferredIntent.Attack, model.PrimaryIntent, "primary intent");
    AssertEqual(3, model.InferredMainEffort, "main effort");
    AssertNear(0.62f, model.Confidence01, 1e-5f, "confidence");
    AssertNear(12.5f, model.AgeSeconds, 1e-5f, "age");
    AssertEqual(2, model.SupportingEvidence.Length, "evidence length");
    AssertEqual(EvidenceTag.SectorConcentration, model.SupportingEvidence[0], "evidence[0]");
    AssertEqual(EvidenceTag.ReserveUncommitted, model.SupportingEvidence[1], "evidence[1]");
}

private static void TacticalIntentModelClampsConfidenceAndAge()
{
    var clampedHigh = new TacticalIntentModel(InferredIntent.Defend, 0, confidence01: 1.5f, ageSeconds: -3f, supportingEvidence: null);
    AssertNear(1.0f, clampedHigh.Confidence01, 1e-5f, "confidence clamped to 1");
    AssertNear(0f, clampedHigh.AgeSeconds, 1e-5f, "age clamped to 0");
    AssertEqual(0, clampedHigh.SupportingEvidence.Length, "null evidence coerces to empty");

    var clampedLow = new TacticalIntentModel(InferredIntent.Defend, 0, confidence01: -0.2f, ageSeconds: float.NaN, supportingEvidence: null);
    AssertNear(0f, clampedLow.Confidence01, 1e-5f, "confidence clamped to 0");
    AssertNear(0f, clampedLow.AgeSeconds, 1e-5f, "NaN age sanitized to 0");
}

private static void TacticalIntentModelUnknownPrimaryIntentSentinel()
{
    var unknown = new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, null);
    AssertEqual(InferredIntent.Unknown, unknown.PrimaryIntent, "Unknown is the no-evidence sentinel");
    AssertEqual(-1, unknown.InferredMainEffort, "main effort -1 = no preference");
    AssertNear(0f, unknown.Confidence01, 1e-5f, "zero confidence on Unknown");
}
```

Tuple entries in `Main()`:

```
("tactical intent model records all fields", TacticalIntentModelRecordsAllFields),
("tactical intent model clamps confidence and age", TacticalIntentModelClampsConfidenceAndAge),
("tactical intent model unknown primary intent sentinel", TacticalIntentModelUnknownPrimaryIntentSentinel),
```

- [ ] **Step 2: Run tests, verify fail.**

```bash
dotnet test tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E "(error|FAIL)" | head -10
```

Expected: build error — `TacticalIntentModel`, `InferredIntent`, `EvidenceTag` not found.

- [ ] **Step 3: Implement.**

```csharp
// src/WhiskeyRealism/Tactical/Orchestrator/TacticalIntentModel.cs
using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    public enum InferredIntent
    {
        Unknown = 0,
        Attack,
        Defend,
        Withdraw,
        Probe,
        Refuse,
    }

    public enum EvidenceTag
    {
        Unknown = 0,
        SectorConcentration,        // own sees enemy concentrating in a specific sector
        ReserveCommitted,           // visible enemy reserves committed forward
        ReserveUncommitted,         // visible enemy reserves still held back
        ContactSpotted,             // new enemy unit just visible
        ContactBroken,              // previously-visible enemy unit no longer visible
        ReceivingFire,              // own units receiving fire from this sector
        ForceImbalanceDownward,     // global odds dropped past hysteresis floor
        ForceImbalanceUpward,       // global odds rose past hysteresis ceiling
        ReinforcementsArriving,     // enemy or own reinforcements within 24h
        FlankExposure,              // enemy main-effort vector aligned with own flank
    }

    /// <summary>
    /// What this echelon infers about the opposing echelon's plan, built from
    /// visible state only (existing ledger filters preserve this — no omniscient
    /// reads). Returned by <see cref="ArmyIntentInference.Build"/>; consumed by
    /// <see cref="ArmyOrchestrator"/> for replan trigger evaluation and by
    /// <see cref="PlaybookContext.OpposingCommanderHint"/> for selection biasing.
    /// </summary>
    public readonly struct TacticalIntentModel
    {
        public TacticalIntentModel(
            InferredIntent primaryIntent,
            int inferredMainEffort,
            float confidence01,
            float ageSeconds,
            EvidenceTag[] supportingEvidence)
        {
            PrimaryIntent = primaryIntent;
            InferredMainEffort = inferredMainEffort;
            Confidence01 = Clamp01(confidence01);
            AgeSeconds = Math.Max(0f, Sanitize(ageSeconds));
            SupportingEvidence = supportingEvidence ?? Array.Empty<EvidenceTag>();
        }

        public InferredIntent PrimaryIntent { get; }
        public int InferredMainEffort { get; }
        public float Confidence01 { get; }
        public float AgeSeconds { get; }

        /// <summary>
        /// Tags backing the inferred intent. Treat as read-only — re-issued
        /// per tick rather than mutated.
        /// </summary>
        public EvidenceTag[] SupportingEvidence { get; }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static float Sanitize(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            return v;
        }
    }
}
```

- [ ] **Step 4: Add csproj entry.**

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\TacticalIntentModel.cs" Link="Orchestrator\TacticalIntentModel.cs" />
```

- [ ] **Step 5: Run tests, verify pass.**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -cE "^PASS "
```

Expected: 587 (was 584 + 3 new).

- [ ] **Step 6: Commit.**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/TacticalIntentModel.cs tests/WhiskeyRealism.Tests/Program.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "$(cat <<'EOF'
feat(orchestrator): add TacticalIntentModel + InferredIntent + EvidenceTag (O2.1)

Pure value type. InferredIntent.Unknown is the no-evidence sentinel
returned when confidence falls below the 0.3 floor. EvidenceTag set
covers the visible-state signals the army-echelon inference reads
from existing ledgers.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: EnemyVisibleState input bundle

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/EnemyVisibleState.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs` — add 2 tests + 2 tuple entries.
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` — 1 `<Compile Include>` entry.

The input bundle that `ArmyIntentInference.Build` reads from. Wraps existing-ledger outputs into one struct so tests don't need to construct full ledger graphs to exercise inference.

- [ ] **Step 1: Write failing tests.**

```csharp
private static void EnemyVisibleStateRecordsSectorAndContactFields()
{
    var sectors = new[]
    {
        new EnemyVisibleSector(sectorId: 0, ownStrength: 5000f, enemyStrength: 7500f, recentFire: true),
        new EnemyVisibleSector(sectorId: 1, ownStrength: 3000f, enemyStrength: 1500f, recentFire: false),
    };
    var state = new EnemyVisibleState(
        sectors: sectors,
        enemyReserveCommitFraction: 0.4f,
        anyContactSpotted: true,
        anyContactBroken: false,
        enemyReinforcementStrength24h: 2000f);
    AssertEqual(2, state.Sectors.Length, "sector count");
    AssertEqual(0, state.Sectors[0].SectorId, "sector[0] id");
    AssertNear(7500f, state.Sectors[0].EnemyStrength, 1e-5f, "sector[0] enemy");
    AssertTrue(state.Sectors[0].RecentFire, "sector[0] received fire");
    AssertNear(0.4f, state.EnemyReserveCommitFraction, 1e-5f, "reserve commit frac");
    AssertTrue(state.AnyContactSpotted, "contact spotted flag");
    AssertFalse(state.AnyContactBroken, "no contact broken");
    AssertNear(2000f, state.EnemyReinforcementStrength24h, 1e-5f, "reinforcement strength");
}

private static void EnemyVisibleStateClampsAndCoercesNullSectors()
{
    var state = new EnemyVisibleState(
        sectors: null,
        enemyReserveCommitFraction: 1.5f,
        anyContactSpotted: false,
        anyContactBroken: false,
        enemyReinforcementStrength24h: float.NaN);
    AssertEqual(0, state.Sectors.Length, "null sectors coerce to empty");
    AssertNear(1.0f, state.EnemyReserveCommitFraction, 1e-5f, "reserve commit frac clamped to 1");
    AssertNear(0f, state.EnemyReinforcementStrength24h, 1e-5f, "NaN reinforcement sanitized to 0");
}
```

Tuple entries:

```
("enemy visible state records sector and contact fields", EnemyVisibleStateRecordsSectorAndContactFields),
("enemy visible state clamps and coerces null sectors", EnemyVisibleStateClampsAndCoercesNullSectors),
```

- [ ] **Step 2: Run tests, verify fail.**

- [ ] **Step 3: Implement.**

```csharp
// src/WhiskeyRealism/Tactical/Orchestrator/EnemyVisibleState.cs
using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Per-sector own-vs-enemy strength as seen through visibility filters,
    /// plus a recent-fire flag for the contact gate. Built per tick by the
    /// runtime partial of TacticalBattleCoordinator from BattleUnits.unitsused.
    /// </summary>
    public readonly struct EnemyVisibleSector
    {
        public EnemyVisibleSector(int sectorId, float ownStrength, float enemyStrength, bool recentFire)
        {
            SectorId = sectorId;
            OwnStrength = Sanitize(ownStrength);
            EnemyStrength = Sanitize(enemyStrength);
            RecentFire = recentFire;
        }

        public int SectorId { get; }
        public float OwnStrength { get; }
        public float EnemyStrength { get; }
        public bool RecentFire { get; }

        public float Odds => EnemyStrength <= 0f ? 0f : OwnStrength / Math.Max(1f, EnemyStrength);

        private static float Sanitize(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            return Math.Max(0f, v);
        }
    }

    /// <summary>
    /// Bundle the army-echelon inference reads from. All values are visible
    /// state — no omniscient reads. Constructed by ArmyEvidenceBuilder per tick
    /// and consumed by ArmyIntentInference.Build.
    /// </summary>
    public readonly struct EnemyVisibleState
    {
        public EnemyVisibleState(
            EnemyVisibleSector[] sectors,
            float enemyReserveCommitFraction,
            bool anyContactSpotted,
            bool anyContactBroken,
            float enemyReinforcementStrength24h)
        {
            Sectors = sectors ?? Array.Empty<EnemyVisibleSector>();
            EnemyReserveCommitFraction = Clamp01(enemyReserveCommitFraction);
            AnyContactSpotted = anyContactSpotted;
            AnyContactBroken = anyContactBroken;
            EnemyReinforcementStrength24h = Sanitize(enemyReinforcementStrength24h);
        }

        /// <summary>
        /// Per-sector visible state. Treat as read-only.
        /// </summary>
        public EnemyVisibleSector[] Sectors { get; }
        public float EnemyReserveCommitFraction { get; }
        public bool AnyContactSpotted { get; }
        public bool AnyContactBroken { get; }
        public float EnemyReinforcementStrength24h { get; }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static float Sanitize(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            return Math.Max(0f, v);
        }
    }
}
```

- [ ] **Step 4: Add csproj entry.**

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\EnemyVisibleState.cs" Link="Orchestrator\EnemyVisibleState.cs" />
```

- [ ] **Step 5: Run tests, verify pass.**

Expected: 589 PASS / 0 FAIL (587 + 2).

- [ ] **Step 6: Commit.**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/EnemyVisibleState.cs tests/WhiskeyRealism.Tests/Program.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "$(cat <<'EOF'
feat(orchestrator): add EnemyVisibleState input bundle (O2.2)

Per-sector own/enemy strength + reserve-commit fraction + contact
spotted/broken flags + reinforcement strength. Built by the runtime
partial each tick from BattleUnits and consumed by ArmyIntentInference.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: ArmyIntentInference scorer

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/ArmyIntentInference.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs` — add 6 tests + 6 tuple entries.
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` — 1 `<Compile Include>` entry.

Pure scorer. Consumes `ArmyEvidence` (own side's view) plus `EnemyVisibleState` (filtered enemy view) and produces `TacticalIntentModel`. Confidence floor 0.3 — below it returns `Unknown` with empty evidence; above 0.6 the intent is "actionable" for replan triggers.

- [ ] **Step 1: Write failing tests.**

```csharp
private static void ArmyIntentInferenceUnknownWhenNoVisibleSectors()
{
    var ownEvidence = new ArmyEvidence(currentOdds: 1.0f, terrain: TerrainKind.Open, defaultMainEffortSector: 0);
    var enemy = new EnemyVisibleState(System.Array.Empty<EnemyVisibleSector>(), 0f, false, false, 0f);
    var model = ArmyIntentInference.Build(ownEvidence, enemy);
    AssertEqual(InferredIntent.Unknown, model.PrimaryIntent, "no sectors → Unknown intent");
    AssertNear(0f, model.Confidence01, 1e-5f, "no sectors → 0 confidence");
    AssertEqual(0, model.SupportingEvidence.Length, "no evidence");
}

private static void ArmyIntentInferenceConcentrationInOneSectorImpliesAttack()
{
    var ownEvidence = new ArmyEvidence(1.0f, TerrainKind.Open, 0);
    var sectors = new[]
    {
        new EnemyVisibleSector(0, ownStrength: 5000f, enemyStrength: 8500f, recentFire: false),  // enemy concentrated here
        new EnemyVisibleSector(1, ownStrength: 5000f, enemyStrength: 1200f, recentFire: false),
        new EnemyVisibleSector(2, ownStrength: 5000f, enemyStrength: 1300f, recentFire: false),
    };
    var enemy = new EnemyVisibleState(sectors, enemyReserveCommitFraction: 0.7f, anyContactSpotted: true, anyContactBroken: false, enemyReinforcementStrength24h: 0f);
    var model = ArmyIntentInference.Build(ownEvidence, enemy);
    AssertEqual(InferredIntent.Attack, model.PrimaryIntent, "concentration + reserves committed → Attack");
    AssertEqual(0, model.InferredMainEffort, "main effort is sector 0");
    AssertTrue(model.Confidence01 >= 0.5f, "concentration is reasonably confident");
}

private static void ArmyIntentInferenceUnconcentratedReservesUncommittedImpliesProbe()
{
    var ownEvidence = new ArmyEvidence(1.0f, TerrainKind.Open, 0);
    var sectors = new[]
    {
        new EnemyVisibleSector(0, 5000f, 2000f, false),
        new EnemyVisibleSector(1, 5000f, 2200f, false),
        new EnemyVisibleSector(2, 5000f, 2100f, false),
    };
    var enemy = new EnemyVisibleState(sectors, enemyReserveCommitFraction: 0.1f, anyContactSpotted: true, anyContactBroken: false, enemyReinforcementStrength24h: 0f);
    var model = ArmyIntentInference.Build(ownEvidence, enemy);
    AssertEqual(InferredIntent.Probe, model.PrimaryIntent, "even spread + reserves uncommitted → Probe");
}

private static void ArmyIntentInferenceContactBrokenImpliesWithdraw()
{
    var ownEvidence = new ArmyEvidence(1.0f, TerrainKind.Open, 0);
    var sectors = new[]
    {
        new EnemyVisibleSector(0, 5000f, 1000f, false),
    };
    var enemy = new EnemyVisibleState(sectors, enemyReserveCommitFraction: 0.0f, anyContactSpotted: false, anyContactBroken: true, enemyReinforcementStrength24h: 0f);
    var model = ArmyIntentInference.Build(ownEvidence, enemy);
    AssertEqual(InferredIntent.Withdraw, model.PrimaryIntent, "contact broken with shrinking visible enemy → Withdraw");
}

private static void ArmyIntentInferenceReceivingFireImpliesDefend()
{
    var ownEvidence = new ArmyEvidence(1.0f, TerrainKind.Open, 0);
    var sectors = new[]
    {
        new EnemyVisibleSector(0, 4000f, 6000f, recentFire: true),
        new EnemyVisibleSector(1, 4000f, 6500f, recentFire: true),
    };
    var enemy = new EnemyVisibleState(sectors, enemyReserveCommitFraction: 0.6f, anyContactSpotted: true, anyContactBroken: false, enemyReinforcementStrength24h: 0f);
    var model = ArmyIntentInference.Build(ownEvidence, enemy);
    AssertEqual(InferredIntent.Defend, model.PrimaryIntent, "receiving fire across multiple sectors → Defend (enemy is engaging us along the line)");
}

private static void ArmyIntentInferenceConfidenceFloorBelowThreshold()
{
    var ownEvidence = new ArmyEvidence(1.0f, TerrainKind.Open, 0);
    // Single sector with tiny enemy strength — barely enough to register but
    // not enough confidence to be actionable.
    var sectors = new[]
    {
        new EnemyVisibleSector(0, 5000f, 100f, false),
    };
    var enemy = new EnemyVisibleState(sectors, 0f, false, false, 0f);
    var model = ArmyIntentInference.Build(ownEvidence, enemy);
    AssertTrue(model.Confidence01 < 0.3f, "tiny enemy footprint → confidence below floor");
    AssertEqual(InferredIntent.Unknown, model.PrimaryIntent, "below floor → Unknown");
}
```

Tuple entries:

```
("army intent inference unknown when no visible sectors", ArmyIntentInferenceUnknownWhenNoVisibleSectors),
("army intent inference concentration in one sector implies attack", ArmyIntentInferenceConcentrationInOneSectorImpliesAttack),
("army intent inference unconcentrated reserves uncommitted implies probe", ArmyIntentInferenceUnconcentratedReservesUncommittedImpliesProbe),
("army intent inference contact broken implies withdraw", ArmyIntentInferenceContactBrokenImpliesWithdraw),
("army intent inference receiving fire implies defend", ArmyIntentInferenceReceivingFireImpliesDefend),
("army intent inference confidence floor below threshold", ArmyIntentInferenceConfidenceFloorBelowThreshold),
```

- [ ] **Step 2: Run tests, verify fail.**

- [ ] **Step 3: Implement.**

```csharp
// src/WhiskeyRealism/Tactical/Orchestrator/ArmyIntentInference.cs
using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pure scorer: visible state → inferred enemy intent + confidence + evidence.
    /// Confidence floor 0.3 — below it the model is Unknown with empty evidence.
    /// Above 0.6 the intent is "actionable" for personality-modulated replan
    /// triggers in ArmyTickCycle.
    /// </summary>
    public static class ArmyIntentInference
    {
        public const float ConfidenceFloor = 0.3f;
        public const float ConfidenceActionable = 0.6f;

        public static TacticalIntentModel Build(ArmyEvidence ownEvidence, EnemyVisibleState enemy)
        {
            if (enemy.Sectors.Length == 0)
                return new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, null);

            float totalEnemy = 0f;
            int sectorWithMaxEnemy = -1;
            float maxEnemyStrength = 0f;
            bool anyRecentFire = false;
            int recentFireSectorCount = 0;
            for (int i = 0; i < enemy.Sectors.Length; i++)
            {
                float strength = enemy.Sectors[i].EnemyStrength;
                totalEnemy += strength;
                if (strength > maxEnemyStrength)
                {
                    maxEnemyStrength = strength;
                    sectorWithMaxEnemy = enemy.Sectors[i].SectorId;
                }
                if (enemy.Sectors[i].RecentFire)
                {
                    anyRecentFire = true;
                    recentFireSectorCount++;
                }
            }

            // Concentration ratio: how much of the visible enemy strength is in
            // its hottest sector. 1.0 = all in one sector; 1/N = perfectly even.
            float concentration = totalEnemy <= 0f ? 0f : maxEnemyStrength / totalEnemy;

            // Confidence weights total visible strength, concentration, and
            // contact signals. Tiny footprints (totalEnemy < 500) collapse to
            // sub-floor confidence regardless of concentration.
            float strengthSignal = Math.Min(1f, totalEnemy / 5000f);
            float concentrationSignal = Math.Max(0f, (concentration - 1f / Math.Max(1, enemy.Sectors.Length)) /
                (1f - 1f / Math.Max(1, enemy.Sectors.Length)));
            float contactSignal = enemy.AnyContactSpotted ? 1f : (enemy.AnyContactBroken ? 0.4f : 0.2f);
            float confidence = 0.5f * strengthSignal + 0.3f * concentrationSignal + 0.2f * contactSignal;
            confidence = Math.Max(0f, Math.Min(1f, confidence));

            if (confidence < ConfidenceFloor)
                return new TacticalIntentModel(InferredIntent.Unknown, -1, confidence, 0f, null);

            var evidence = new List<EvidenceTag>(4);
            if (concentration > 0.55f && enemy.Sectors.Length > 1)
                evidence.Add(EvidenceTag.SectorConcentration);
            if (enemy.EnemyReserveCommitFraction >= 0.5f)
                evidence.Add(EvidenceTag.ReserveCommitted);
            else if (enemy.EnemyReserveCommitFraction <= 0.2f)
                evidence.Add(EvidenceTag.ReserveUncommitted);
            if (enemy.AnyContactSpotted) evidence.Add(EvidenceTag.ContactSpotted);
            if (enemy.AnyContactBroken) evidence.Add(EvidenceTag.ContactBroken);
            if (anyRecentFire) evidence.Add(EvidenceTag.ReceivingFire);
            if (enemy.EnemyReinforcementStrength24h > 1f) evidence.Add(EvidenceTag.ReinforcementsArriving);

            // Inference rules — simple, ordered. First match wins.
            InferredIntent intent;
            int mainEffort = sectorWithMaxEnemy;

            if (enemy.AnyContactBroken && totalEnemy < 2000f && !enemy.AnyContactSpotted)
                intent = InferredIntent.Withdraw;
            else if (anyRecentFire && recentFireSectorCount >= 2 && enemy.EnemyReserveCommitFraction >= 0.4f)
                intent = InferredIntent.Defend;
            else if (concentration > 0.55f && enemy.EnemyReserveCommitFraction >= 0.4f)
                intent = InferredIntent.Attack;
            else if (concentration > 0.55f && enemy.EnemyReserveCommitFraction < 0.3f)
                intent = InferredIntent.Refuse;
            else if (concentration < 0.45f && enemy.EnemyReserveCommitFraction < 0.3f)
                intent = InferredIntent.Probe;
            else
                intent = InferredIntent.Defend;

            return new TacticalIntentModel(intent, mainEffort, confidence, 0f, evidence.ToArray());
        }
    }
}
```

- [ ] **Step 4: Add csproj entry.**

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\ArmyIntentInference.cs" Link="Orchestrator\ArmyIntentInference.cs" />
```

- [ ] **Step 5: Run tests, verify pass.**

Expected: 595 PASS / 0 FAIL (589 + 6).

If a test fails because the inference rules don't match the test expectations, **escalate as DONE_WITH_CONCERNS** rather than tweaking parameters silently. The rule order and thresholds were chosen for clarity; some edge cases may need iteration with the orchestrator owner.

- [ ] **Step 6: Commit.**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/ArmyIntentInference.cs tests/WhiskeyRealism.Tests/Program.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "$(cat <<'EOF'
feat(orchestrator): add ArmyIntentInference scorer (O2.3)

Pure scorer: visible state (sector strengths + reserve commit fraction +
contact flags) -> TacticalIntentModel. Confidence floor 0.3 below which
intent is Unknown. Six rule branches (Withdraw / Defend / Attack /
Refuse / Probe / fallback Defend) ordered by hard-evidence priority.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Extend ArmyOrchestrator with history + intent

**Files:**
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs` — add 4 tests + 4 tuple entries.

`ArmyOrchestrator` needs:
1. **History fields** — record of own strength + global odds at last replan, so `ReplanTriggerInput` can compare current vs prior.
2. **Accept `TacticalIntentModel`** on `Replan(ArmyEvidence, TacticalIntentModel?)` — feeds `OpposingCommanderHint` into `PlaybookContext` and increments the plan age tracker.
3. **PlanAgeSeconds tracker** — incremented externally by `ArmyTickCycle` each tick; reset to 0 on Replan.
4. **CurrentIntentModel accessor** — last consumed model (debug/telemetry).

Existing `Replan(ArmyEvidence)` keeps working (calls the new overload with `null` intent), so callers from O1 don't break.

- [ ] **Step 1: Write failing tests.**

```csharp
private static void ArmyOrchestratorRecordsHistoryOnInitialPlan()
{
    var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
    var orch = new ArmyOrchestrator(0, SeedCatalog.AllHistoricalAndGeneric(), lee);
    orch.PickInitialPlan(new ArmyEvidence(1.4f, TerrainKind.Wooded, 0));
    AssertNear(1.4f, orch.HistoryGlobalOdds, 1e-5f, "history odds = current at pick");
    AssertNear(0f, orch.PlanAgeSeconds, 1e-5f, "plan age starts at 0");
}

private static void ArmyOrchestratorTickAdvancesAgeWithoutReplanning()
{
    var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
    var orch = new ArmyOrchestrator(0, SeedCatalog.AllHistoricalAndGeneric(), lee);
    orch.PickInitialPlan(new ArmyEvidence(1.4f, TerrainKind.Wooded, 0));
    orch.AdvancePlanAge(15f);
    orch.AdvancePlanAge(20f);
    AssertNear(35f, orch.PlanAgeSeconds, 1e-5f, "plan age accumulates");
}

private static void ArmyOrchestratorReplanWithIntentResetsAgeAndUpdatesHistory()
{
    var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
    var orch = new ArmyOrchestrator(0, SeedCatalog.AllHistoricalAndGeneric(), lee);
    orch.PickInitialPlan(new ArmyEvidence(1.4f, TerrainKind.Wooded, 0));
    orch.AdvancePlanAge(60f);

    var enemyIntent = new TacticalIntentModel(InferredIntent.Defend, 1, 0.7f, 0f, null);
    orch.Replan(new ArmyEvidence(0.8f, TerrainKind.Open, 1), enemyIntent);

    AssertNear(0f, orch.PlanAgeSeconds, 1e-5f, "age reset to 0 on replan");
    AssertNear(0.8f, orch.HistoryGlobalOdds, 1e-5f, "history updated to new odds");
    AssertEqual(InferredIntent.Defend, orch.CurrentIntentModel.PrimaryIntent, "current intent stored");
}

private static void ArmyOrchestratorReplanWithoutIntentLeavesIntentUnknown()
{
    var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
    var orch = new ArmyOrchestrator(0, SeedCatalog.AllHistoricalAndGeneric(), lee);
    orch.PickInitialPlan(new ArmyEvidence(1.4f, TerrainKind.Wooded, 0));
    orch.Replan(new ArmyEvidence(1.0f, TerrainKind.Wooded, 0));  // legacy 1-arg overload
    AssertEqual(InferredIntent.Unknown, orch.CurrentIntentModel.PrimaryIntent, "no-intent overload yields Unknown");
}
```

Tuple entries:

```
("army orchestrator records history on initial plan", ArmyOrchestratorRecordsHistoryOnInitialPlan),
("army orchestrator tick advances age without replanning", ArmyOrchestratorTickAdvancesAgeWithoutReplanning),
("army orchestrator replan with intent resets age and updates history", ArmyOrchestratorReplanWithIntentResetsAgeAndUpdatesHistory),
("army orchestrator replan without intent leaves intent unknown", ArmyOrchestratorReplanWithoutIntentLeavesIntentUnknown),
```

- [ ] **Step 2: Run tests, verify fail.**

Expected: build error — `HistoryGlobalOdds`, `PlanAgeSeconds`, `CurrentIntentModel`, `AdvancePlanAge`, `Replan(ArmyEvidence, TacticalIntentModel)` not found.

- [ ] **Step 3: Modify `ArmyOrchestrator.cs`.**

The existing class is at `src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs`. Add:
- Private fields: `_planAgeSeconds`, `_historyGlobalOdds`, `_currentIntentModel`.
- Public properties: `PlanAgeSeconds`, `HistoryGlobalOdds`, `CurrentIntentModel`.
- Public method: `AdvancePlanAge(float deltaSeconds)`.
- New `Replan(ArmyEvidence evidence, TacticalIntentModel enemyIntent)` overload.
- Existing `Replan(ArmyEvidence)` becomes a forwarder to the new overload with `Unknown` intent.
- `PickInitialPlan` records `_historyGlobalOdds = evidence.CurrentOdds` and resets age.

```csharp
using System;
using WhiskeyRealism.Strategic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    public readonly struct ArmyEvidence
    {
        public ArmyEvidence(float currentOdds, TerrainKind terrain, int defaultMainEffortSector)
        {
            CurrentOdds = currentOdds;
            Terrain = terrain;
            DefaultMainEffortSector = defaultMainEffortSector;
        }
        public float CurrentOdds { get; }
        public TerrainKind Terrain { get; }
        public int DefaultMainEffortSector { get; }
    }

    public sealed class ArmyOrchestrator : EchelonOrchestrator
    {
        private readonly TacticalPlaybookCatalog _catalog;
        private readonly PersonalityVector _commanderPersonality;
        private TacticalBattlePlan _plan;
        private float _planAgeSeconds;
        private float _historyGlobalOdds;
        private TacticalIntentModel _currentIntentModel;

        public ArmyOrchestrator(int allianceId, TacticalPlaybookCatalog catalog, PersonalityVector commanderPersonality)
            : base(EchelonKind.Army, allianceId)
        {
            _catalog = catalog;
            _commanderPersonality = commanderPersonality;
            HasPlan = false;
            _planAgeSeconds = 0f;
            _historyGlobalOdds = 1f;
            _currentIntentModel = new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, null);
        }

        public bool HasPlan { get; private set; }
        public TacticalBattlePlan CurrentPlan => _plan;
        public PersonalityVector CommanderPersonality => _commanderPersonality;
        public float PlanAgeSeconds => _planAgeSeconds;
        public float HistoryGlobalOdds => _historyGlobalOdds;
        public TacticalIntentModel CurrentIntentModel => _currentIntentModel;

        public int CurrentMacroAi
        {
            get
            {
                if (!HasPlan) return -1;
                switch (_plan.Phase)
                {
                    case BattlePhase.Probe:        return _commanderPersonality.Aggression > 0.3f ? 1 : -1;
                    case BattlePhase.MainEffort:   return _commanderPersonality.Aggression > 0.0f ? 1 : 0;
                    case BattlePhase.Exploit:      return 0;
                    case BattlePhase.Consolidate:  return 2;
                    case BattlePhase.Withdraw:     return 3;
                    default:                       return -1;
                }
            }
        }

        public void PickInitialPlan(ArmyEvidence evidence)
        {
            PickPlanInternal(evidence, opposingHint: 0f);
            _historyGlobalOdds = evidence.CurrentOdds;
            _planAgeSeconds = 0f;
        }

        public void AdvancePhase(BattlePhase next)
        {
            if (!HasPlan) return;
            _plan = _plan.WithPhase(next);
            _planAgeSeconds = 0f;
        }

        public void AdvancePlanAge(float deltaSeconds)
        {
            if (deltaSeconds <= 0f || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds)) return;
            _planAgeSeconds += deltaSeconds;
        }

        /// <summary>
        /// Re-pick a plan using current evidence and the inferred enemy intent.
        /// Updates history fields and resets plan age. Use this overload from
        /// the runtime tick cycle.
        /// </summary>
        public void Replan(ArmyEvidence evidence, TacticalIntentModel enemyIntent)
        {
            float opposingHint = OpposingCommanderHintFromIntent(enemyIntent);
            PickPlanInternal(evidence, opposingHint);
            _historyGlobalOdds = evidence.CurrentOdds;
            _planAgeSeconds = 0f;
            _currentIntentModel = enemyIntent;
        }

        /// <summary>
        /// Legacy 1-arg overload for callers (tests / runtime) that don't yet
        /// have intent inference. Equivalent to <see cref="Replan(ArmyEvidence, TacticalIntentModel)"/>
        /// with an Unknown intent.
        /// </summary>
        public void Replan(ArmyEvidence evidence)
        {
            Replan(evidence, new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, null));
        }

        public ArmyIntent EmitArmyIntent()
        {
            return new ArmyIntent(
                _plan.PlanId,
                _plan.Phase,
                _plan.MainEffortSector,
                _plan.FixingSectors,
                _plan.ScreeningSectors,
                _plan.ReserveCommitTriggerOdds,
                aggressionBias01: (_commanderPersonality.Aggression + 1f) * 0.5f);
        }

        public ReplanTrigger CheckReplanTriggers(ReplanTriggerInput input) => ArmyReplanTriggers.Evaluate(input);

        private void PickPlanInternal(ArmyEvidence evidence, float opposingHint)
        {
            var ctx = new PlaybookContext(
                _commanderPersonality,
                evidence.Terrain,
                evidence.CurrentOdds,
                opposingCommanderHint: opposingHint,
                defaultMainEffortSector: evidence.DefaultMainEffortSector,
                jitterSeed: AllianceId * 31 + 7);
            var pb = _catalog?.Select(ctx);
            if (pb == null)
            {
                HasPlan = false;
                return;
            }
            _plan = pb.Instantiate(ctx);
            HasPlan = true;
        }

        /// <summary>
        /// Converts an inferred enemy intent + confidence into a [0, 1] hint
        /// the playbook scorer can use to bias selection. Defensive enemy
        /// intent biases own toward attack-style playbooks; aggressive enemy
        /// intent biases toward refuse/defend playbooks. Confidence multiplies
        /// the bias.
        /// </summary>
        private static float OpposingCommanderHintFromIntent(TacticalIntentModel m)
        {
            if (m.PrimaryIntent == InferredIntent.Unknown) return 0f;
            float baseBias;
            switch (m.PrimaryIntent)
            {
                case InferredIntent.Defend:
                case InferredIntent.Refuse:
                case InferredIntent.Withdraw:
                    baseBias = 0.6f;  // own bias toward Attack/Maneuver playbooks
                    break;
                case InferredIntent.Attack:
                    baseBias = 0.2f;  // own bias toward Defend/Refuse playbooks
                    break;
                case InferredIntent.Probe:
                    baseBias = 0.4f;  // neutral-leaning
                    break;
                default:
                    baseBias = 0f;
                    break;
            }
            return baseBias * m.Confidence01;
        }
    }
}
```

- [ ] **Step 4: Run tests, verify pass.**

Expected: 599 PASS / 0 FAIL (595 + 4).

- [ ] **Step 5: Commit.**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "$(cat <<'EOF'
feat(orchestrator): extend ArmyOrchestrator with intent + history (O2.4)

Adds PlanAgeSeconds + HistoryGlobalOdds + CurrentIntentModel tracking,
AdvancePlanAge(delta) for the tick driver, and a new Replan(evidence,
intent) overload that feeds OpposingCommanderHint into PlaybookContext.
Existing Replan(evidence) overload preserved as a forwarder with
Unknown intent.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: ArmyTickCycle driver

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/ArmyTickCycle.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs` — add 5 tests + 5 tuple entries.
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` — 1 `<Compile Include>` entry.

Pure logic class. Per tick: advance plan age, build intent model, evaluate replan triggers, replan if any fires AND `MinReplanSeconds` has elapsed since last replan.

Architecture: `ArmyTickCycle.MaybeReplan(orchestrator, deltaSeconds, ownEvidence, enemyVisible, ownStrengthHistory, minReplanSeconds)` returns `ReplanTrigger.None` if nothing happened, or the trigger that fired. Telemetry emit lives in the runtime caller (Task 7), not here — keep this class testable without Plugin.Log.

- [ ] **Step 1: Write failing tests.**

```csharp
private static void ArmyTickCycleNoTriggerWhenAllConditionsNormal()
{
    var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
    var orch = new ArmyOrchestrator(0, SeedCatalog.AllHistoricalAndGeneric(), lee);
    orch.PickInitialPlan(new ArmyEvidence(1.0f, TerrainKind.Open, 0));

    var enemy = new EnemyVisibleState(
        new[] { new EnemyVisibleSector(0, 5000f, 5000f, false) },
        enemyReserveCommitFraction: 0.5f, anyContactSpotted: true, anyContactBroken: false, enemyReinforcementStrength24h: 0f);
    var trigger = ArmyTickCycle.MaybeReplan(
        orch,
        deltaSeconds: 5f,
        ownEvidence: new ArmyEvidence(1.0f, TerrainKind.Open, 0),
        enemyVisible: enemy,
        ownMainEffortStrength: 5000f,
        ownArmyMorale: 1.0f,
        ownReservesCommittedFraction: 0.5f,
        reinforcementsArrivingDelta: 0f,
        minReplanSeconds: 60);
    AssertEqual(ReplanTrigger.None, trigger, "normal conditions → no trigger");
    AssertNear(5f, orch.PlanAgeSeconds, 1e-5f, "age advances even when no replan");
}

private static void ArmyTickCyclePhaseDeadlineFires()
{
    var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
    var orch = new ArmyOrchestrator(0, SeedCatalog.AllHistoricalAndGeneric(), lee);
    orch.PickInitialPlan(new ArmyEvidence(1.0f, TerrainKind.Open, 0));
    orch.AdvancePlanAge(190f);  // already past PhaseBudgetSeconds (180)

    var enemy = new EnemyVisibleState(
        new[] { new EnemyVisibleSector(0, 5000f, 5000f, false) },
        0.5f, true, false, 0f);
    var trigger = ArmyTickCycle.MaybeReplan(
        orch, 5f,
        new ArmyEvidence(1.0f, TerrainKind.Open, 0), enemy,
        ownMainEffortStrength: 5000f, ownArmyMorale: 1.0f, ownReservesCommittedFraction: 0.5f, reinforcementsArrivingDelta: 0f,
        minReplanSeconds: 60);
    AssertEqual(ReplanTrigger.PhaseDeadline, trigger, "age past 180s → PhaseDeadline trigger");
    AssertNear(0f, orch.PlanAgeSeconds, 1e-5f, "plan age reset to 0 after replan");
}

private static void ArmyTickCycleRateLimitsReplanWithinMinReplanSeconds()
{
    var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
    var orch = new ArmyOrchestrator(0, SeedCatalog.AllHistoricalAndGeneric(), lee);
    orch.PickInitialPlan(new ArmyEvidence(1.0f, TerrainKind.Open, 0));
    orch.AdvancePlanAge(200f);  // would trigger PhaseDeadline

    // First call replans (age was past budget).
    var enemy = new EnemyVisibleState(
        new[] { new EnemyVisibleSector(0, 5000f, 5000f, false) },
        0.5f, true, false, 0f);
    var first = ArmyTickCycle.MaybeReplan(
        orch, 5f,
        new ArmyEvidence(1.0f, TerrainKind.Open, 0), enemy,
        5000f, 1.0f, 0.5f, 0f, 60);
    AssertEqual(ReplanTrigger.PhaseDeadline, first, "first replan fires");

    // Force the age past the budget again immediately. Without rate-limit
    // we'd replan again. With rate-limit (lastReplan was just set), no replan.
    orch.AdvancePlanAge(200f);
    var second = ArmyTickCycle.MaybeReplan(
        orch, 5f,
        new ArmyEvidence(1.0f, TerrainKind.Open, 0), enemy,
        5000f, 1.0f, 0.5f, 0f, 60);
    AssertEqual(ReplanTrigger.None, second, "rate-limit blocks immediate second replan");
}

private static void ArmyTickCycleEnemyIntentShiftFiresWhenConfidentEnemyAttacks()
{
    var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
    var orch = new ArmyOrchestrator(0, SeedCatalog.AllHistoricalAndGeneric(), lee);
    orch.PickInitialPlan(new ArmyEvidence(1.0f, TerrainKind.Open, 0));
    orch.AdvancePlanAge(70f);  // past min-replan, well under phase deadline

    // Strongly-confident enemy attack signal: high concentration + reserves committed + contact.
    var enemy = new EnemyVisibleState(
        new[]
        {
            new EnemyVisibleSector(0, 5000f, 9000f, true),
            new EnemyVisibleSector(1, 5000f, 1500f, false),
            new EnemyVisibleSector(2, 5000f, 1500f, false),
        },
        enemyReserveCommitFraction: 0.8f, anyContactSpotted: true, anyContactBroken: false, enemyReinforcementStrength24h: 0f);
    var trigger = ArmyTickCycle.MaybeReplan(
        orch, 5f,
        new ArmyEvidence(1.0f, TerrainKind.Open, 0), enemy,
        5000f, 1.0f, 0.5f, 0f, 60);
    AssertEqual(ReplanTrigger.EnemyIntentShift, trigger, "confident enemy attack signal → EnemyIntentShift");
}

private static void ArmyTickCycleNoReplanIfOrchestratorHasNoPlan()
{
    var orch = new ArmyOrchestrator(0, SeedCatalog.AllHistoricalAndGeneric(), default);
    // No PickInitialPlan call — HasPlan is false.
    var enemy = new EnemyVisibleState(
        new[] { new EnemyVisibleSector(0, 5000f, 5000f, false) },
        0.5f, true, false, 0f);
    var trigger = ArmyTickCycle.MaybeReplan(
        orch, 5f,
        new ArmyEvidence(1.0f, TerrainKind.Open, 0), enemy,
        5000f, 1.0f, 0.5f, 0f, 60);
    AssertEqual(ReplanTrigger.None, trigger, "no plan → no replan trigger fires");
}
```

Tuple entries:

```
("army tick cycle no trigger when all conditions normal", ArmyTickCycleNoTriggerWhenAllConditionsNormal),
("army tick cycle phase deadline fires", ArmyTickCyclePhaseDeadlineFires),
("army tick cycle rate limits replan within min replan seconds", ArmyTickCycleRateLimitsReplanWithinMinReplanSeconds),
("army tick cycle enemy intent shift fires when confident enemy attacks", ArmyTickCycleEnemyIntentShiftFiresWhenConfidentEnemyAttacks),
("army tick cycle no replan if orchestrator has no plan", ArmyTickCycleNoReplanIfOrchestratorHasNoPlan),
```

- [ ] **Step 2: Run tests, verify fail.**

- [ ] **Step 3: Implement.**

```csharp
// src/WhiskeyRealism/Tactical/Orchestrator/ArmyTickCycle.cs
using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pure per-tick driver. Advances plan age, builds the intent model from
    /// visible state, evaluates replan triggers, and replans the orchestrator
    /// when a trigger fires AND the rate-limit window has elapsed since the
    /// last replan.
    ///
    /// No Plugin.Log dependency — runtime caller (TacticalBattleCoordinatorRuntime)
    /// handles telemetry emit. Test-friendly.
    /// </summary>
    public static class ArmyTickCycle
    {
        // Tracked separately per orchestrator to apply MinReplanSeconds.
        private static readonly System.Collections.Generic.Dictionary<int, float> _lastReplanByAlliance =
            new System.Collections.Generic.Dictionary<int, float>();

        // Test-friendly clock — incremented externally via deltaSeconds. Each
        // alliance tracks its own last-replan stamp so player CIC suppression
        // doesn't pollute the AI side.
        private static float _virtualClock = 0f;

        public static void ResetForTest()
        {
            _lastReplanByAlliance.Clear();
            _virtualClock = 0f;
        }

        public static ReplanTrigger MaybeReplan(
            ArmyOrchestrator orchestrator,
            float deltaSeconds,
            ArmyEvidence ownEvidence,
            EnemyVisibleState enemyVisible,
            float ownMainEffortStrength,
            float ownArmyMorale,
            float ownReservesCommittedFraction,
            float reinforcementsArrivingDelta,
            int minReplanSeconds)
        {
            if (orchestrator == null) return ReplanTrigger.None;

            // Advance virtual clock + plan age. Always do this even if no
            // replan fires — phase-deadline trigger relies on accumulated age.
            _virtualClock += deltaSeconds;
            orchestrator.AdvancePlanAge(deltaSeconds);

            if (!orchestrator.HasPlan) return ReplanTrigger.None;

            // Build intent model from visible state.
            var intent = ArmyIntentInference.Build(ownEvidence, enemyVisible);

            // Build replan trigger input from current evidence + history.
            var triggerInput = new ReplanTriggerInput(
                planAgeSeconds: orchestrator.PlanAgeSeconds,
                currentPhase: orchestrator.CurrentPlan.Phase,
                mainEffortOwnStrength: ownMainEffortStrength,
                mainEffortHistoryOwnStrength: ownMainEffortStrength,  // O2 doesn't yet track per-sector history
                globalOddsCurrent: ownEvidence.CurrentOdds,
                globalOddsHistory: orchestrator.HistoryGlobalOdds,
                armyMoraleCurrent: ownArmyMorale,
                armyMoraleFloor: 0.4f,
                reservesCommittedFraction: ownReservesCommittedFraction,
                reinforcementsArrivingDelta: reinforcementsArrivingDelta,
                enemyMainEffortShiftConfidenceWeighted: ConfidenceWeightedShift(intent));

            var trigger = orchestrator.CheckReplanTriggers(triggerInput);
            if (trigger == ReplanTrigger.None) return ReplanTrigger.None;

            // Rate-limit: only replan if MinReplanSeconds has elapsed since last replan on this alliance.
            float last;
            if (_lastReplanByAlliance.TryGetValue(orchestrator.AllianceId, out last))
            {
                if (_virtualClock - last < minReplanSeconds) return ReplanTrigger.None;
            }

            orchestrator.Replan(ownEvidence, intent);
            _lastReplanByAlliance[orchestrator.AllianceId] = _virtualClock;
            return trigger;
        }

        /// <summary>
        /// Maps the inferred-enemy model into the "enemy main effort shifted"
        /// signal that ArmyReplanTriggers reads. Strongly-confident Attack
        /// produces the largest shift signal; Unknown / low-confidence
        /// produces zero.
        /// </summary>
        private static float ConfidenceWeightedShift(TacticalIntentModel intent)
        {
            if (intent.PrimaryIntent == InferredIntent.Unknown) return 0f;
            if (intent.Confidence01 < ArmyIntentInference.ConfidenceFloor) return 0f;
            float base_;
            switch (intent.PrimaryIntent)
            {
                case InferredIntent.Attack: base_ = 1.0f; break;
                case InferredIntent.Defend: base_ = 0.5f; break;
                case InferredIntent.Refuse: base_ = 0.5f; break;
                case InferredIntent.Withdraw: base_ = 0.7f; break;
                case InferredIntent.Probe: base_ = 0.3f; break;
                default: base_ = 0f; break;
            }
            return Math.Min(1f, base_ * intent.Confidence01);
        }
    }
}
```

- [ ] **Step 4: Add csproj entry.**

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\ArmyTickCycle.cs" Link="Orchestrator\ArmyTickCycle.cs" />
```

- [ ] **Step 5: Run tests, verify pass.**

Expected: 604 PASS / 0 FAIL (599 + 5).

If `ArmyTickCycleRateLimitsReplanWithinMinReplanSeconds` fails because the static `_lastReplanByAlliance` dict is contaminated by a prior test, add `ArmyTickCycle.ResetForTest()` calls at the top of every Task 5 test. Better: insert a single `ResetForTest()` call at the very start of each test method. The dict is global state by design — tests must reset between invocations.

- [ ] **Step 6: Commit.**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/ArmyTickCycle.cs tests/WhiskeyRealism.Tests/Program.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "$(cat <<'EOF'
feat(orchestrator): add ArmyTickCycle replan-loop driver (O2.5)

Pure logic. Per-tick: advance plan age, build intent model from visible
state, evaluate replan triggers, replan when triggered AND
MinReplanSeconds has elapsed since last replan. Rate-limit per-alliance
via static dict; ResetForTest seam for harness.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: ArmyEvidenceBuilder runtime partial

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/ArmyEvidenceBuilder.cs`
- (No test changes — runtime partial is excluded from the test assembly.)

This file extracts evidence from vanilla `BattleUnits` per side: `ArmyEvidence` (own odds + terrain), `EnemyVisibleState` (per-sector strengths + reserve commit + contact flags), plus the strength inputs `ArmyTickCycle.MaybeReplan` needs (own main-effort strength, morale, reserves committed fraction, reinforcement delta).

Wraps every vanilla call in try/catch. Returns sentinel "no evidence" structs on failure rather than throwing.

- [ ] **Step 1: Implement.**

```csharp
// src/WhiskeyRealism/Tactical/Orchestrator/ArmyEvidenceBuilder.cs
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Runtime-only — depends on vanilla AIBattle / BattleUnits / Regiment +
    /// Plugin.Log. Excluded from the test assembly; covered indirectly via
    /// in-game smoke.
    ///
    /// Per-tick extractor: vanilla state -> ArmyEvidence + EnemyVisibleState +
    /// ReplanTriggerInput strength signals. All vanilla calls wrapped in
    /// try/catch; degraded on failure rather than thrown.
    /// </summary>
    internal static class ArmyEvidenceBuilder
    {
        /// <summary>
        /// Per-side bundle returned by Build. The runtime caller passes these
        /// fields into ArmyTickCycle.MaybeReplan.
        /// </summary>
        internal readonly struct Bundle
        {
            public Bundle(
                ArmyEvidence ownEvidence,
                EnemyVisibleState enemyVisible,
                float ownMainEffortStrength,
                float ownArmyMorale,
                float ownReservesCommittedFraction,
                float reinforcementsArrivingDelta)
            {
                OwnEvidence = ownEvidence;
                EnemyVisible = enemyVisible;
                OwnMainEffortStrength = ownMainEffortStrength;
                OwnArmyMorale = ownArmyMorale;
                OwnReservesCommittedFraction = ownReservesCommittedFraction;
                ReinforcementsArrivingDelta = reinforcementsArrivingDelta;
            }

            public ArmyEvidence OwnEvidence { get; }
            public EnemyVisibleState EnemyVisible { get; }
            public float OwnMainEffortStrength { get; }
            public float OwnArmyMorale { get; }
            public float OwnReservesCommittedFraction { get; }
            public float ReinforcementsArrivingDelta { get; }
        }

        public static Bundle Build(AIBattle battle, int allianceId)
        {
            var fallback = new Bundle(
                new ArmyEvidence(1f, TerrainKind.Open, 0),
                new EnemyVisibleState(Array.Empty<EnemyVisibleSector>(), 0f, false, false, 0f),
                ownMainEffortStrength: 1f,
                ownArmyMorale: 1f,
                ownReservesCommittedFraction: 0f,
                reinforcementsArrivingDelta: 0f);

            try
            {
                var bunits = ResolveBattleUnits(battle);
                if (bunits == null) return fallback;

                int side = ResolveSideFromAlliance(bunits, allianceId);
                if (side < 0) return fallback;

                float own = SafeSideInfoFloat(bunits, side, "totalactiveforce");
                float enemyTotal = 0f;
                if (bunits.sideinformation != null)
                {
                    for (int s = 0; s < 2 && s < bunits.sideinformation.Length; s++)
                        if (s != side) enemyTotal += SafeSideInfoFloat(bunits, s, "totalactiveforce");
                }
                float odds = enemyTotal <= 0f ? 1f : own / Math.Max(1f, enemyTotal);

                var ownEvidence = new ArmyEvidence(odds, TerrainKind.Open, 0);
                var enemyVisible = BuildEnemyVisibleState(bunits, side);
                float ownMainEffortStrength = Math.Max(1f, own);
                float ownMorale = SafeSideInfoFloat(bunits, side, "averagemorale");
                if (ownMorale <= 0f) ownMorale = 1f;
                if (ownMorale > 1f) ownMorale = 1f;
                float ownReservesCommitted = SafeSideInfoFloat(bunits, side, "reservescommittedfraction");
                if (ownReservesCommitted < 0f) ownReservesCommitted = 0f;
                if (ownReservesCommitted > 1f) ownReservesCommitted = 1f;
                float reinforcementsDelta = SafeSideInfoFloat(bunits, side, "reinforcementarrivalswithin24hrs");

                return new Bundle(
                    ownEvidence, enemyVisible,
                    ownMainEffortStrength, ownMorale, ownReservesCommitted, reinforcementsDelta);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] ArmyEvidenceBuilder.Build degraded: "
                    + e.GetType().Name + " " + e.Message);
                return fallback;
            }
        }

        private static EnemyVisibleState BuildEnemyVisibleState(BattleUnits bunits, int side)
        {
            try
            {
                var sectors = new List<EnemyVisibleSector>();
                IList units = SafeGetUnits(bunits);
                if (units == null) return new EnemyVisibleState(Array.Empty<EnemyVisibleSector>(), 0f, false, false, 0f);

                int sectorId = 0;
                bool anyContactSpotted = false;
                bool anyContactBroken = false;
                for (int i = 0; i < units.Count; i++)
                {
                    var group = units[i] as Regiment;
                    if (group == null || group.unittyp <= 13) continue;
                    if (group.team != side) continue;

                    float ownStrength = Math.Max(1f, group.groupstrengthaigroup);
                    float enemyStrength = 0f;
                    bool recentFire = false;
                    try
                    {
                        if (group.unitrange != null)
                        {
                            enemyStrength = Math.Max(0f, group.groupenemiesinrange);
                            if (group.unitrange.closestenemyunitfar != null) anyContactSpotted = true;
                        }
                    }
                    catch { }
                    sectors.Add(new EnemyVisibleSector(sectorId++, ownStrength, enemyStrength, recentFire));
                }

                float reserveCommitFraction = SafeSideInfoFloat(bunits, OppositeSide(side), "reservescommittedfraction");
                if (reserveCommitFraction < 0f) reserveCommitFraction = 0f;
                if (reserveCommitFraction > 1f) reserveCommitFraction = 1f;
                float enemyReinforce = SafeSideInfoFloat(bunits, OppositeSide(side), "reinforcementarrivalswithin24hrs");

                return new EnemyVisibleState(
                    sectors.ToArray(), reserveCommitFraction, anyContactSpotted, anyContactBroken, enemyReinforce);
            }
            catch
            {
                return new EnemyVisibleState(Array.Empty<EnemyVisibleSector>(), 0f, false, false, 0f);
            }
        }

        private static BattleUnits ResolveBattleUnits(AIBattle battle)
        {
            try
            {
                if (battle == null) return null;
                var field = AccessTools.Field(typeof(AIBattle), "bunits");
                return field?.GetValue(battle) as BattleUnits;
            }
            catch
            {
                return null;
            }
        }

        private static int ResolveSideFromAlliance(BattleUnits bunits, int allianceId)
        {
            try
            {
                if (bunits == null || bunits.alliance == null) return -1;
                for (int s = 0; s < 2 && s < bunits.alliance.Length; s++)
                    if (bunits.alliance[s] == allianceId) return s;
                return -1;
            }
            catch { return -1; }
        }

        private static int OppositeSide(int side)
        {
            return side == 0 ? 1 : 0;
        }

        private static float SafeSideInfoFloat(BattleUnits bunits, int side, string fieldName)
        {
            try
            {
                if (bunits == null || bunits.sideinformation == null) return 0f;
                if (side < 0 || side >= bunits.sideinformation.Length) return 0f;
                var info = bunits.sideinformation[side];
                var field = AccessTools.Field(info.GetType(), fieldName);
                if (field == null) return 0f;
                object value = field.GetValue(info);
                return value == null ? 0f : Convert.ToSingle(value);
            }
            catch
            {
                return 0f;
            }
        }

        private static IList SafeGetUnits(BattleUnits bunits)
        {
            try { return bunits?.completeunitlist as IList; } catch { return null; }
        }
    }
}
```

Key behaviors:
- Vanilla anchor `BattleUnits.bunits` accessed via reflection (matches O1's idiom).
- `BattleUnits.completeunitlist` is the same vanilla anchor the lifecycle detector uses (per O0 smoke fix `73e998d`).
- Field names like `averagemorale`, `reservescommittedfraction`, `reinforcementarrivalswithin24hrs` are guessed from naming conventions in `SideInformation`. **If any of these field names don't exist in the current decompile**, the `SafeSideInfoFloat` returns 0 and the orchestrator gets a degraded but non-crashing input. Verify after first smoke run by grepping the log for `Tactical … failed` warnings. If a field is absent, the harness still passes (test side doesn't exercise this path); only smoke surfaces it.

- [ ] **Step 2: Build to confirm.**

```bash
./build.sh 2>&1 | tail -10
```

Expected: 0 warnings / 0 errors. (No test changes; harness count unchanged from Task 5.)

- [ ] **Step 3: Commit.**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/ArmyEvidenceBuilder.cs
git commit -m "$(cat <<'EOF'
feat(orchestrator): add ArmyEvidenceBuilder runtime partial (O2.6)

Per-tick vanilla evidence extractor. Reads BattleUnits.sideinformation,
completeunitlist, sideinformation[side].totalactiveforce/averagemorale/
reservescommittedfraction/reinforcementarrivalswithin24hrs to build the
ArmyEvidence + EnemyVisibleState + replan-trigger strength signals
ArmyTickCycle.MaybeReplan needs.

All vanilla calls wrapped in try/catch; missing fields return 0f rather
than throwing. Excluded from test assembly.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Plugin config flag + wire ArmyTickCycle into runtime tick

**Files:**
- Modify: `src/WhiskeyRealism/Plugin.cs` — add 1 new config flag.
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs` — extend `Tick(AIBattle battle)` to drive `ArmyTickCycle.MaybeReplan` per side; emit `[TacticalIntent]` and `[TacticalReplan]` telemetry.

The existing `Tick()` method currently calls `side0?.Tick()` and `side1?.Tick()`. After this task it ALSO calls `ArmyTickCycle.MaybeReplan` for each side's army (when present), feeding evidence built by `ArmyEvidenceBuilder`. To do this, `Tick()` needs the `AIBattle` battle, so its signature changes from `Tick()` to `Tick(AIBattle battle)`. The single production caller — `TacticalObserverPatch.cs` line 280 — gets a one-line update to pass `__instance`.

- [ ] **Step 1: Add config flag in `Plugin.cs`.**

Field declaration block (~line 51-54, alongside the O1 flags):

```csharp
public static ConfigEntry<bool> EnableTacticalOrchestratorIntentInference;
```

`Config.Bind` call (after the O1 `Verbose Logging` flag in section "Tactical Orchestrator"):

```csharp
EnableTacticalOrchestratorIntentInference = Config.Bind(
    "Tactical Orchestrator",
    "Enable Tactical Orchestrator Intent Inference",
    true,
    "Default ON. O2: per-tick TacticalIntentModel built from visible enemy " +
    "state, fed into ArmyOrchestrator's replan trigger evaluator and " +
    "playbook selection bias. Disable to keep O1 initial-pick-only behavior " +
    "(plans never advance phase or replan during a battle).");
```

- [ ] **Step 2: Modify `TacticalBattleCoordinatorRuntime.Tick`.**

Change signature from `public static void Tick()` to `public static void Tick(AIBattle battle)`. Inside, after the existing `side0?.Tick()` / `side1?.Tick()` cascades, drive `ArmyTickCycle.MaybeReplan` per side using `ArmyEvidenceBuilder.Build`. Telemetry is emitted at this layer (not inside `ArmyTickCycle`) so the pure type stays Plugin.Log-free.

```csharp
public static void Tick(AIBattle battle)
{
    if (!active) return;
    try
    {
        OnceLog.Info("orch-coordinator", "[TacticalOrchestrator] coordinator first tick");
        side0?.Tick();
        side1?.Tick();

        if (Plugin.EnableTacticalOrchestratorIntentInference != null
            && Plugin.EnableTacticalOrchestratorIntentInference.Value)
        {
            DriveTickCycle(side0, battle);
            DriveTickCycle(side1, battle);
        }
    }
    catch (Exception e)
    {
        Plugin.Log.LogWarning("[TacticalOrchestrator] Tick skipped: "
            + e.GetType().Name + " " + e.Message);
    }
}

private static float _lastTickTimeSeconds = 0f;

private static void DriveTickCycle(TacticalBattleOrchestrator side, AIBattle battle)
{
    if (side?.Army == null || !side.Army.HasPlan) return;

    float now = UnityEngine.Time.realtimeSinceStartup;
    float delta = _lastTickTimeSeconds <= 0f ? 1f : Math.Max(0f, now - _lastTickTimeSeconds);
    _lastTickTimeSeconds = now;

    var bundle = ArmyEvidenceBuilder.Build(battle, side.AllianceId);
    int minReplanSeconds = (Plugin.TacticalOrchestratorMinReplanSeconds != null)
        ? Plugin.TacticalOrchestratorMinReplanSeconds.Value : 60;

    var trigger = ArmyTickCycle.MaybeReplan(
        side.Army,
        deltaSeconds: delta,
        ownEvidence: bundle.OwnEvidence,
        enemyVisible: bundle.EnemyVisible,
        ownMainEffortStrength: bundle.OwnMainEffortStrength,
        ownArmyMorale: bundle.OwnArmyMorale,
        ownReservesCommittedFraction: bundle.OwnReservesCommittedFraction,
        reinforcementsArrivingDelta: bundle.ReinforcementsArrivingDelta,
        minReplanSeconds: minReplanSeconds);

    var intent = side.Army.CurrentIntentModel;
    if (intent.PrimaryIntent != InferredIntent.Unknown)
    {
        OnceLog.Info("orch-intent:" + side.AllianceId + ":" + intent.PrimaryIntent + ":" + intent.InferredMainEffort,
            "[TacticalIntent] side=" + side.AllianceId
            + " seesEnemy=" + intent.PrimaryIntent
            + " mainEffort=" + intent.InferredMainEffort
            + " confidence=" + intent.Confidence01.ToString("0.00"));
    }

    if (trigger != ReplanTrigger.None)
    {
        OnceLog.Info("orch-replan:" + side.AllianceId + ":" + trigger + ":" + side.Army.CurrentPlan.PlanId,
            "[TacticalReplan] side=" + side.AllianceId
            + " trigger=" + trigger
            + " newPlan=" + side.Army.CurrentPlan.PlanId
            + " phase=" + side.Army.CurrentPlan.Phase);
    }
}
```

- [ ] **Step 3: Update the call site at `TacticalObserverPatch.cs`** (around line 280) from `TacticalBattleCoordinator.Tick();` to `TacticalBattleCoordinator.Tick(__instance);`.

- [ ] **Step 4: Build to confirm.**

```bash
./build.sh 2>&1 | tail -10
```

Expected: 0 warnings / 0 errors.

- [ ] **Step 5: Run harness, no regressions.**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -cE "^PASS "
```

Expected: 604 (unchanged from Task 5 — runtime partial isn't tested).

- [ ] **Step 6: Commit.**

```bash
git add src/WhiskeyRealism/Plugin.cs src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs src/WhiskeyRealism/Patches/TacticalObserverPatch.cs
git commit -m "$(cat <<'EOF'
feat(orchestrator): wire ArmyTickCycle into per-tick runtime + add intent inference flag (O2.7)

Tick(AIBattle battle) now drives ArmyTickCycle.MaybeReplan for each
non-suppressed side after the O1 cascade. ArmyEvidenceBuilder extracts
own and enemy visible state per tick; the cycle advances plan age,
builds the intent model, evaluates replan triggers, and replans when
triggered AND MinReplanSeconds elapsed.

Emits [TacticalIntent side=… seesEnemy=… confidence=…] and [TacticalReplan
side=… trigger=… newPlan=… phase=…] via OnceLog so transitions log once
per signature.

New config: Enable Tactical Orchestrator Intent Inference (default ON).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: csproj sweep + build/deploy/hash verify

- [ ] **Step 1: Sweep csproj for new files.**

```bash
for f in $(git diff --name-only main..HEAD -- src/WhiskeyRealism/Tactical/Orchestrator/); do
  base=$(basename "$f")
  grep -q "Link=.*$base" tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj && echo "OK: $f" || echo "MISSING: $f"
done
```

Expected: `OK` for `TacticalIntentModel.cs`, `EnemyVisibleState.cs`, `ArmyIntentInference.cs`, `ArmyTickCycle.cs`. `MISSING` is acceptable for `ArmyEvidenceBuilder.cs` only (runtime partial; intentionally excluded from tests). Anything else missing is a bug.

- [ ] **Step 2: Final harness run.**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -cE "^PASS "
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -cE "^FAIL "
```

Expected: 604 PASS / 0 FAIL.

- [ ] **Step 3: Build + deploy + hash verify.**

```bash
./build.sh 2>&1 | tail -10                                                              # 0/0
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

If `cp` fails with "Invalid argument," GTCW is running and holding the DLL. Close the game and retry. The two SHA-256s must match before claiming the DLL is deployed.

- [ ] **Step 4: Record the new DLL hash.** Save it for the Task 9 handoff update.

---

### Task 9: In-game smoke test (user-driven)

Two smoke scenarios. Both should surface `[TacticalIntent]` lines and (over time) `[TacticalReplan]` events.

- [ ] **Smoke A — W&L subordinate role (most common):**
  - Launch GTCW, advance to a battle where you are a subordinate of the AI CIC.
  - Let the battle run ~3 minutes (long enough for at least one phase deadline at 180s).
  - Quit to desktop.
  - Expected log:
    - O0/O1 markers still fire (`bootstrap`, `coordinator`, `[TacticalCommanderRoster]`, `[TacticalPlan]`, `[TacticalMacroDecision]`)
    - One or more `[TacticalIntent side=… seesEnemy=… mainEffort=… confidence=…]` lines per side
    - At least one `[TacticalReplan side=… trigger=… newPlan=… phase=…]` line (PhaseDeadline at minimum, EnemyIntentShift if the inference detects a confident shift)
  - No new `Tactical … failed` or `Tactical … skipped` warnings.

- [ ] **Smoke B — CIC role (if accessible):**
  - Same as A but you are CIC of your side. Suppression still hides one side; the AI side runs full cycle. `[TacticalIntent]` and `[TacticalReplan]` appear only for the AI side.

- [ ] **Failure modes to watch for:**
  - **Spam:** if `[TacticalIntent]` fires more than ~5 times per battle for the same signature, the OnceLog key is too narrow. Grep `[TacticalIntent]` count and report.
  - **Missing fields:** if `ArmyEvidenceBuilder.Build degraded: NullReferenceException` appears, one of the `SideInformation` field-name guesses (`averagemorale`, `reservescommittedfraction`) is wrong. Re-decompile and check field names; update `Build` accordingly.
  - **No replan ever:** if no `[TacticalReplan]` appears even after 5+ minutes, either MinReplanSeconds is too aggressive or the inference confidence never crosses 0.6. Likely the latter — collect a longer log sample.

- [ ] **If smoke fails:** capture the failure into `docs/handoff.md` with a "What just happened" callout block. Do not advance to Task 10.

- [ ] **If smoke passes:** record the deployed DLL hash + the smoke evidence (number of `[TacticalIntent]` lines, number of `[TacticalReplan]` lines, observed triggers, any unique plan-id transitions) for Task 10.

---

### Task 10: Update handoff + MEMORY.md, archive O2 plan

- [ ] **Step 1: Update `docs/handoff.md`** with the post-O2 active workstream row pointing to O3 (Corps echelon), the new deployed DLL hash, and a brief summary of observed smoke evidence.

- [ ] **Step 2: Update `MEMORY.md`'s "Active workstream" line** to reflect O2 merged + smoke verified.

- [ ] **Step 3: Archive this plan.**

```bash
git mv docs/superpowers/plans/2026-05-08-tactical-orchestrator-o2-intent.md docs/superpowers/plans/archive/
```

- [ ] **Step 4: Append an entry to `docs/superpowers/plans/archive/README.md`.**

```
| [`2026-05-08-tactical-orchestrator-o2-intent.md`](2026-05-08-tactical-orchestrator-o2-intent.md) | Tactical orchestrator O2 — intent inference + adversarial loop. TacticalIntentModel, EnemyVisibleState, ArmyIntentInference scorer, ArmyTickCycle replan-loop driver, ArmyEvidenceBuilder runtime partial, ArmyOrchestrator history-tracking + intent-aware Replan, [TacticalIntent] / [TacticalReplan] telemetry. Plans now advance phase and replan during battles. |
```

- [ ] **Step 5: Optionally remove the O2 sketch** if it's now redundant with the archived full plan.

```bash
git rm docs/superpowers/plans/2026-05-08-tactical-orchestrator-o2-intent-sketch.md
```

- [ ] **Step 6: Commit.**

```bash
git commit -m "$(cat <<'EOF'
docs(handoff): record O2 ship + archive plan + advance handoff to O3

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Cross-cutting invariants verified at every commit

- Console harness PASS count never decreases. Baseline 584 → 604 by Task 8.
- `./build.sh` produces 0 warnings / 0 errors before any commit.
- `dist/WhiskeyRealism.dll` SHA-256 == BepInEx plugin SHA-256 before any smoke claim.
- W&L player-control invariant: O2 doesn't touch any per-unit gate. Only writes are to orchestrator state (the patches it influences — #44 only — already gate properly).
- Read-only-mod-state invariant: `ArmyTickCycle` mutates orchestrator state only inside `MaybeReplan`. Patches only READ from the orchestrator.
- Master flag fallback: when `EnableTacticalBattleOrchestrator = false`, no orchestrator instantiates → `Tick(battle)` early-exits → no intent inference runs.
- Per-phase valve: when `EnableTacticalOrchestratorIntentInference = false`, the per-tick tick cycle is skipped but the orchestrator stays alive (initial-pick-only O1 behavior).

## Deferred to later phases

- **Per-sector strength history.** O2 sets `mainEffortHistoryOwnStrength = mainEffortOwnStrength` so the `MainEffortSectorLoss` trigger never fires. Real history-tracking ships in O3 alongside per-corps frontage tracking.
- **Personality-modulated confidence thresholds.** The umbrella spec describes "high caution lowers defensive replan trigger threshold" — O2 ships with a single 0.6 actionable threshold; per-personality modulation is a tuning slice.
- **Corps/Division/Brigade intent models.** Only army-echelon intent inference in O2. Per-echelon evidence pipelines ship with their respective phases.
- **Replan trigger reasoning telemetry.** O2 emits `[TacticalReplan trigger=…]` but doesn't break down WHY the trigger fired (e.g., which sector lost strength). Useful but deferrable.

## Self-review checklist

After implementing through Task 10, before claiming O2 done:

1. **Spec coverage:** Re-read the umbrella spec O2 row and §"Adversarial intent inference + personality":
   - [x] `TacticalIntentModel` per opposing echelon — Task 1
   - [x] Evidence pipelines (visible-state filters) — Tasks 2 + 6 (`EnemyVisibleState` + `ArmyEvidenceBuilder`)
   - [x] Confidence-weighted personality consumption — Task 4 (`OpposingCommanderHintFromIntent`) + Task 5 (`ConfidenceWeightedShift`)
   - [x] Replan-on-intent-shift trigger — Task 5 (`ArmyTickCycle.MaybeReplan` populates `EnemyMainEffortShiftConfidenceWeighted`)
   - [x] Both army orchestrators see and react to each other — Task 7 drives the cycle for both `side0?.Army` and `side1?.Army`

2. **Smoke gate from §"Phasing" O2 row:**
   - AI-vs-AI battle log shows `[TacticalIntent]` lines on both sides with non-zero confidence — verify in smoke.
   - One or more `[TacticalReplan trigger=enemy-intent-shift]` events observed — verify in smoke.
   - Personality bias visible: McClellan-archetype CO triggers defensive replan at lower confidence than Lee-archetype — **deferred to a tuning slice**; O2 ships with uniform thresholds and the test for that requires controlled scenarios.

3. **Type-name consistency:** `TacticalIntentModel`, `InferredIntent`, `EvidenceTag`, `EnemyVisibleSector`, `EnemyVisibleState`, `ArmyIntentInference`, `ArmyTickCycle`, `ArmyEvidenceBuilder`, `Bundle`, `OpposingCommanderHintFromIntent`, `ConfidenceWeightedShift` are defined exactly once each.

4. **No placeholders.** Re-grep:

```bash
grep -nE "TODO|TBD|similar to Task|implement later" docs/superpowers/plans/2026-05-08-tactical-orchestrator-o2-intent.md
```

Expected: empty output (the only matches are in this self-review checklist text describing what to grep for).

5. **Harness baseline:** 604 PASS / 0 FAIL. Build clean.
