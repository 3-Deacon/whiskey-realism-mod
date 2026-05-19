# Tactical Orchestrator O1 — Army Echelon + Plan + Playbooks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the Army echelon of the multi-echelon tactical battle orchestrator: a per-side `ArmyOrchestrator` that picks a personality-keyed historical battle playbook at battle start, owns a `TacticalBattlePlan`, evaluates replan triggers each tick, and exposes its current macro decision to the rewired `BattleMacroStrategyPatch` (#44). After this phase, the macro stance written to vanilla `AIBattle.macroai` reflects an army-level plan rather than a per-tick global-odds heuristic.

**Architecture:** Per-side `ArmyOrchestrator` attaches as the single root echelon under `TacticalBattleOrchestrator.Echelons` (slot prepared in O0). It reads the existing `TacticalCommanderRoster` for the army CO's personality vector, scores 14 seeded playbooks against personality + terrain + odds + opposing-CO hints, picks the highest, and instantiates a `TacticalBattlePlan`. Each tick, the orchestrator refreshes evidence from existing ledgers (`TacticalOddsDoctrine`, `TacticalSectorLedger`, etc.) and checks replan triggers; if any fire and the rate limit allows, it re-picks. Patch #44 reads `ArmyOrchestrator.CurrentMacroAi` instead of running the doctrine scorer when `Enable Tactical Orchestrator Army = true`. Patch #47 (`BattleCommanderIntentObserverPatch`) demotes to telemetry-only — it stops writing `TacticalReactionContext` because that is now the orchestrator's job.

**Tech Stack:** C# netstandard2.1, BepInEx 5.4.x x64 + HarmonyX (NuGet), Unity 2021.3.16f1 Mono x86-64. Existing `Strategic/PersonalityVector` (5-D), `HistoricalFigureRegistry.Resolve`, `FactionProfiles.For`, `TacticalCommanderRoster.FromVanilla` (O0). All new tactical orchestrator types live under `src/WhiskeyRealism/Tactical/Orchestrator/`. Tests live under `tests/WhiskeyRealism.Tests/` with explicit `<Compile Include>` entries per file (test csproj does NOT use globs — see `CLAUDE.md`).

---

## Source-of-truth citations

This plan implements one phase of the umbrella spec; do not deviate from the umbrella without amending it.

- **Umbrella spec:** `docs/superpowers/specs/archive/2026-05-08-tactical-battle-orchestrator-design.md` — O1 row in §"Phasing", §"Architecture", §"Playbooks", §"Decision flow + cadence".
- **O0 scaffold (already merged):** `docs/superpowers/plans/archive/2026-05-08-tactical-orchestrator-o0-scaffold.md` defines the bootstrap + lifecycle detector + empty `TacticalBattleOrchestrator.Echelons` slot O1 fills.
- **Slice A personality stack:** `docs/superpowers/specs/archive/2026-05-02-strategic-brain-design.md` for `PersonalityVector` semantics.

---

## Pre-flight verification

Before any code change, confirm O0 ground truth.

- [ ] **Step P1: Confirm O0 has merged on main and DLL is current.**

```bash
git log --oneline -5
ls -la src/WhiskeyRealism/Tactical/Orchestrator/
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: orchestrator merge commit visible (`92bee55` or descendant), seven files in `Tactical/Orchestrator/` (`EchelonOrchestrator.cs`, `TacticalBattleCoordinator.cs`, `TacticalBattleCoordinatorRuntime.cs`, `TacticalBattleLifecycleDetector.cs`, `TacticalBattleOrchestrator.cs`, `TacticalCommanderRoster.cs`, `TacticalCommanderRosterRuntime.cs`), and the two SHA-256s match.

- [ ] **Step P2: Confirm decision contracts already renamed.**

```bash
ls src/WhiskeyRealism/Tactical/TacticalDoctrineDecisionContracts.cs
test ! -f src/WhiskeyRealism/Tactical/TacticalBattlePlan.cs && echo "OK: name freed for O1 plan entity"
```

Expected: the contracts file exists at the new path; no `TacticalBattlePlan.cs` exists at the legacy path. (The umbrella spec described a rename; O0 already performed it.)

- [ ] **Step P3: Run console harness baseline.**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | tail -5
```

Expected: `539 PASS / 0 FAIL` (or whatever main currently reports — record the number, every later task's harness step must not decrease it).

---

## File structure

### New files

```
src/WhiskeyRealism/Tactical/Orchestrator/
├── TacticalBattlePlan.cs               (the plan entity — Task 1)
├── ArmyIntent.cs                       (intent struct cascading down — Task 1)
├── TacticalPlaybook.cs                 (abstract base + parameter types — Task 2)
├── TacticalPlaybookCatalog.cs          (registry + selection algorithm — Task 3)
├── ArmyOrchestrator.cs                 (concrete echelon — Task 7)
├── ArmyReplanTriggers.cs               (trigger evaluation — Task 8)
└── Playbooks/
    ├── GenericAggressivePlaybook.cs    (Task 4)
    ├── GenericCautiousPlaybook.cs      (Task 4)
    ├── GenericMethodicalPlaybook.cs    (Task 4)
    ├── GenericDesperatePlaybook.cs     (Task 4)
    ├── LeeEnvelopmentPlaybook.cs       (Task 5)
    ├── JacksonValleyShufflePlaybook.cs (Task 5)
    ├── McClellanPreparedDefensePlaybook.cs (Task 5)
    ├── ShermanManeuverFixPlaybook.cs   (Task 5)
    ├── GrantContinuousAttritionPlaybook.cs (Task 5)
    ├── LongstreetDefensiveOverslopePlaybook.cs (Task 6)
    ├── HookerFlankDeparturePlaybook.cs (Task 6)
    ├── HoodFrontalAssaultPlaybook.cs   (Task 6)
    ├── BurnsideForcedAssaultPlaybook.cs (Task 6)
    └── BraggIndecisiveCommitPlaybook.cs (Task 6)
```

```
tests/WhiskeyRealism.Tests/
├── TacticalBattlePlanTests.cs          (Task 1)
├── TacticalPlaybookTests.cs            (Task 2)
├── TacticalPlaybookCatalogTests.cs     (Task 3)
├── GenericPlaybookTests.cs             (Task 4)
├── HistoricalPlaybookSelectionTests.cs (Tasks 5+6, parameterized)
├── ArmyOrchestratorTests.cs            (Task 7)
└── ArmyReplanTriggersTests.cs          (Task 8)
```

### Modified files

```
src/WhiskeyRealism/Plugin.cs                                      — Task 11 (config flags)
src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleOrchestrator.cs — Task 9 (attach Army)
src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs — Task 10 (commander discovery)
src/WhiskeyRealism/Patches/BattleMacroStrategyPatch.cs            — Task 12 (rewire to orchestrator)
src/WhiskeyRealism/Patches/BattleCommanderIntentObserverPatch.cs  — Task 13 (demote to telemetry-only)
tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj            — Task 14 (add Compile entries)
```

### Untouched

- All other Patches/* files. The only patch surfaces O1 modifies are #44 and #47.
- `TacticalDoctrineScorer`, `TacticalSectorLedger`, `TacticalOddsDoctrine`, `TacticalQuadrantThreatScorer`, etc. — these become **evidence inputs**; their behavior is unchanged.
- `EchelonOrchestrator` abstract base — already shipped in O0; do not modify.

---

## Implementation tasks

### Task 1: TacticalBattlePlan + ArmyIntent entities

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattlePlan.cs`
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/ArmyIntent.cs`
- Test: `tests/WhiskeyRealism.Tests/TacticalBattlePlanTests.cs`

The plan entity is what the army orchestrator owns and replans. The intent struct is what the army emits down to corps each tick.

- [ ] **Step 1: Write the failing tests.**

```csharp
// tests/WhiskeyRealism.Tests/TacticalBattlePlanTests.cs
using NUnit.Framework;
using WhiskeyRealism.Tactical.Orchestrator;

[TestFixture]
public class TacticalBattlePlanTests
{
    [Test]
    public void Plan_records_id_phase_main_effort_and_age()
    {
        var plan = new TacticalBattlePlan(
            BattlePlanId.LeeEnvelopment,
            BattlePhase.Probe,
            mainEffortSector: 3,
            fixingSectors: new[] { 0, 1 },
            screeningSectors: new[] { 4 },
            reserveCommitTriggerOdds: 1.4f,
            ageSeconds: 0f,
            jitterSeed: 17);

        Assert.AreEqual(BattlePlanId.LeeEnvelopment, plan.PlanId);
        Assert.AreEqual(BattlePhase.Probe, plan.Phase);
        Assert.AreEqual(3, plan.MainEffortSector);
        CollectionAssert.AreEqual(new[] { 0, 1 }, plan.FixingSectors);
        CollectionAssert.AreEqual(new[] { 4 }, plan.ScreeningSectors);
        Assert.AreEqual(1.4f, plan.ReserveCommitTriggerOdds, 1e-5f);
        Assert.AreEqual(0f, plan.AgeSeconds, 1e-5f);
        Assert.AreEqual(17, plan.JitterSeed);
    }

    [Test]
    public void Plan_with_phase_returns_new_instance_with_phase_advanced_and_age_reset()
    {
        var plan = new TacticalBattlePlan(BattlePlanId.GenericMethodical, BattlePhase.Probe, 0, null, null, 1.2f, 12.5f, 1)
            .WithPhase(BattlePhase.MainEffort);
        Assert.AreEqual(BattlePhase.MainEffort, plan.Phase);
        Assert.AreEqual(0f, plan.AgeSeconds, 1e-5f);
    }

    [Test]
    public void Plan_with_age_returns_new_instance_with_age_only_changed()
    {
        var plan = new TacticalBattlePlan(BattlePlanId.GenericMethodical, BattlePhase.Probe, 2, null, null, 1.0f, 0f, 1).WithAge(45.5f);
        Assert.AreEqual(45.5f, plan.AgeSeconds, 1e-5f);
        Assert.AreEqual(BattlePhase.Probe, plan.Phase);
        Assert.AreEqual(2, plan.MainEffortSector);
    }

    [Test]
    public void ArmyIntent_carries_plan_id_phase_and_aggression_bias()
    {
        var intent = new ArmyIntent(
            BattlePlanId.ShermanManeuverFix,
            BattlePhase.MainEffort,
            mainEffortSector: 1,
            fixingSectors: new[] { 2, 3 },
            screeningSectors: System.Array.Empty<int>(),
            reserveCommitTriggerOdds: 1.3f,
            aggressionBias01: 0.65f);
        Assert.AreEqual(BattlePlanId.ShermanManeuverFix, intent.PlanId);
        Assert.AreEqual(BattlePhase.MainEffort, intent.Phase);
        Assert.AreEqual(1, intent.MainEffortSector);
        Assert.AreEqual(0.65f, intent.AggressionBias01, 1e-5f);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail.**

```bash
dotnet test tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj --filter "TacticalBattlePlanTests"
```

Expected: build error, type `TacticalBattlePlan` / `BattlePlanId` / `BattlePhase` / `ArmyIntent` not found.

- [ ] **Step 3: Implement the types.**

```csharp
// src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattlePlan.cs
using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    public enum BattlePlanId
    {
        Unknown = 0,
        LeeEnvelopment,
        JacksonValleyShuffle,
        McClellanPreparedDefense,
        ShermanManeuverFix,
        GrantContinuousAttrition,
        LongstreetDefensiveOverslope,
        HookerFlankDeparture,
        HoodFrontalAssault,
        BurnsideForcedAssault,
        BraggIndecisiveCommit,
        GenericAggressive,
        GenericCautious,
        GenericMethodical,
        GenericDesperate,
    }

    public enum BattlePhase
    {
        Probe = 0,
        MainEffort = 1,
        Exploit = 2,
        Consolidate = 3,
        Withdraw = 4,
    }

    /// <summary>
    /// The army-echelon plan: source-playbook id, current phase, sector allocation,
    /// reserve commit trigger, and age. Read-only struct; orchestrator replaces it
    /// wholesale via WithPhase/WithAge or a fresh instance on replan.
    /// </summary>
    public readonly struct TacticalBattlePlan
    {
        public TacticalBattlePlan(
            BattlePlanId planId,
            BattlePhase phase,
            int mainEffortSector,
            int[] fixingSectors,
            int[] screeningSectors,
            float reserveCommitTriggerOdds,
            float ageSeconds,
            int jitterSeed)
        {
            PlanId = planId;
            Phase = phase;
            MainEffortSector = mainEffortSector;
            FixingSectors = fixingSectors ?? Array.Empty<int>();
            ScreeningSectors = screeningSectors ?? Array.Empty<int>();
            ReserveCommitTriggerOdds = Sanitize(reserveCommitTriggerOdds);
            AgeSeconds = Math.Max(0f, Sanitize(ageSeconds));
            JitterSeed = jitterSeed;
        }

        public BattlePlanId PlanId { get; }
        public BattlePhase Phase { get; }
        public int MainEffortSector { get; }
        public int[] FixingSectors { get; }
        public int[] ScreeningSectors { get; }
        public float ReserveCommitTriggerOdds { get; }
        public float AgeSeconds { get; }
        public int JitterSeed { get; }

        public TacticalBattlePlan WithPhase(BattlePhase phase) =>
            new TacticalBattlePlan(PlanId, phase, MainEffortSector, FixingSectors, ScreeningSectors, ReserveCommitTriggerOdds, 0f, JitterSeed);

        public TacticalBattlePlan WithAge(float ageSeconds) =>
            new TacticalBattlePlan(PlanId, Phase, MainEffortSector, FixingSectors, ScreeningSectors, ReserveCommitTriggerOdds, ageSeconds, JitterSeed);

        private static float Sanitize(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            return v;
        }
    }
}
```

```csharp
// src/WhiskeyRealism/Tactical/Orchestrator/ArmyIntent.cs
using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    public readonly struct ArmyIntent
    {
        public ArmyIntent(
            BattlePlanId planId,
            BattlePhase phase,
            int mainEffortSector,
            int[] fixingSectors,
            int[] screeningSectors,
            float reserveCommitTriggerOdds,
            float aggressionBias01)
        {
            PlanId = planId;
            Phase = phase;
            MainEffortSector = mainEffortSector;
            FixingSectors = fixingSectors ?? Array.Empty<int>();
            ScreeningSectors = screeningSectors ?? Array.Empty<int>();
            ReserveCommitTriggerOdds = float.IsNaN(reserveCommitTriggerOdds) ? 1.0f : reserveCommitTriggerOdds;
            AggressionBias01 = Clamp01(aggressionBias01);
        }

        public BattlePlanId PlanId { get; }
        public BattlePhase Phase { get; }
        public int MainEffortSector { get; }
        public int[] FixingSectors { get; }
        public int[] ScreeningSectors { get; }
        public float ReserveCommitTriggerOdds { get; }
        public float AggressionBias01 { get; }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0.5f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
```

- [ ] **Step 4: Add Compile Include entries to the test csproj** (see Task 14 for the full list, but add THESE TWO now so this task's tests compile):

```xml
<!-- in tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj, alongside existing Strategic includes -->
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\TacticalBattlePlan.cs" Link="TacticalBattlePlan.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\ArmyIntent.cs" Link="ArmyIntent.cs" />
```

- [ ] **Step 5: Run tests to verify pass.**

```bash
dotnet test tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj --filter "TacticalBattlePlanTests"
```

Expected: 4 PASS / 0 FAIL.

- [ ] **Step 6: Commit.**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattlePlan.cs src/WhiskeyRealism/Tactical/Orchestrator/ArmyIntent.cs tests/WhiskeyRealism.Tests/TacticalBattlePlanTests.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "$(cat <<'EOF'
feat(orchestrator): add TacticalBattlePlan + ArmyIntent entities

Plan entity owned by ArmyOrchestrator (O1) and intent struct cascaded
down to corps. Both are immutable; WithPhase/WithAge produce new instances.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: TacticalPlaybook abstract base + parameter types

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalPlaybook.cs`
- Test: `tests/WhiskeyRealism.Tests/TacticalPlaybookTests.cs`

The base class concrete playbooks inherit from. Defines the score-vs-context method and the parameter types (`PersonalityFit`, `TerrainPreference`, `OddsRange`).

- [ ] **Step 1: Write failing tests.**

```csharp
// tests/WhiskeyRealism.Tests/TacticalPlaybookTests.cs
using NUnit.Framework;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Orchestrator;

[TestFixture]
public class TacticalPlaybookTests
{
    private sealed class StubPlaybook : TacticalPlaybook
    {
        public StubPlaybook() : base(
            BattlePlanId.GenericMethodical,
            "stub",
            new PersonalityFit(aggression: 0.0f, caution: 0.0f, audacity: 0.0f),
            new TerrainPreference(open: 0.5f, wooded: 0.5f, river: 0.5f, mountain: 0.5f),
            new OddsRange(min: 0.8f, max: 1.4f),
            reserveCommitTriggerOdds: 1.0f) { }

        public override TacticalBattlePlan Instantiate(PlaybookContext ctx) =>
            new TacticalBattlePlan(BattlePlanId.GenericMethodical, BattlePhase.Probe, ctx.DefaultMainEffortSector, null, null, ReserveCommitTriggerOdds, 0f, ctx.JitterSeed);
    }

    [Test]
    public void PersonalityFit_score_peaks_at_exact_match_and_decays()
    {
        var fit = new PersonalityFit(aggression: 0.8f, caution: -0.4f, audacity: 0.6f);
        var matched = new PersonalityVector(0.8f, -0.4f, 0.6f, 0f, 0f);
        var off = new PersonalityVector(-0.2f, 0.2f, -0.4f, 0f, 0f);
        Assert.Greater(fit.Score(matched), 0.95f);
        Assert.Less(fit.Score(off), 0.5f);
    }

    [Test]
    public void TerrainPreference_score_returns_dominant_terrain_weight()
    {
        var pref = new TerrainPreference(open: 1.0f, wooded: 0.4f, river: 0.0f, mountain: 0.0f);
        Assert.AreEqual(1.0f, pref.Score(TerrainKind.Open), 1e-5f);
        Assert.AreEqual(0.4f, pref.Score(TerrainKind.Wooded), 1e-5f);
        Assert.AreEqual(0.0f, pref.Score(TerrainKind.River), 1e-5f);
    }

    [Test]
    public void OddsRange_score_is_one_inside_band_and_decays_outside()
    {
        var band = new OddsRange(min: 0.8f, max: 1.4f);
        Assert.AreEqual(1.0f, band.Score(1.0f), 1e-5f);
        Assert.AreEqual(1.0f, band.Score(0.8f), 1e-5f);
        Assert.AreEqual(1.0f, band.Score(1.4f), 1e-5f);
        Assert.Less(band.Score(0.4f), 0.5f);
        Assert.Less(band.Score(2.0f), 0.5f);
    }

    [Test]
    public void Stub_playbook_instantiates_plan_with_phase_probe()
    {
        var pb = new StubPlaybook();
        var ctx = new PlaybookContext(
            commanderPersonality: new PersonalityVector(0, 0, 0, 0, 0),
            terrain: TerrainKind.Open,
            currentOdds: 1.0f,
            opposingCommanderHint: 0f,
            defaultMainEffortSector: 2,
            jitterSeed: 5);
        var plan = pb.Instantiate(ctx);
        Assert.AreEqual(BattlePlanId.GenericMethodical, plan.PlanId);
        Assert.AreEqual(BattlePhase.Probe, plan.Phase);
        Assert.AreEqual(2, plan.MainEffortSector);
    }
}
```

- [ ] **Step 2: Run tests, verify fail.**

```bash
dotnet test tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj --filter "TacticalPlaybookTests"
```

Expected: build error — types not found.

- [ ] **Step 3: Implement.**

```csharp
// src/WhiskeyRealism/Tactical/Orchestrator/TacticalPlaybook.cs
using System;
using WhiskeyRealism.Strategic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    public enum TerrainKind
    {
        Open = 0,
        Wooded = 1,
        River = 2,
        Mountain = 3,
    }

    public readonly struct PersonalityFit
    {
        public PersonalityFit(float aggression, float caution, float audacity)
        {
            Aggression = Clamp(aggression);
            Caution = Clamp(caution);
            Audacity = Clamp(audacity);
        }
        public float Aggression { get; }
        public float Caution { get; }
        public float Audacity { get; }

        /// <summary>Cosine-ish similarity in 3 dims, mapped to [0,1].</summary>
        public float Score(PersonalityVector v)
        {
            float dot = Aggression * v.Aggression + Caution * v.Caution + Audacity * v.Audacity;
            // each axis is in [-1,1]; max possible dot in 3 dims is 3, min is -3.
            float normalized = (dot + 3f) / 6f;
            if (normalized < 0f) return 0f;
            if (normalized > 1f) return 1f;
            return normalized;
        }
        private static float Clamp(float x) => Math.Max(-1f, Math.Min(1f, x));
    }

    public readonly struct TerrainPreference
    {
        public TerrainPreference(float open, float wooded, float river, float mountain)
        {
            Open = Clamp01(open);
            Wooded = Clamp01(wooded);
            River = Clamp01(river);
            Mountain = Clamp01(mountain);
        }
        public float Open { get; }
        public float Wooded { get; }
        public float River { get; }
        public float Mountain { get; }
        public float Score(TerrainKind k)
        {
            switch (k)
            {
                case TerrainKind.Open: return Open;
                case TerrainKind.Wooded: return Wooded;
                case TerrainKind.River: return River;
                case TerrainKind.Mountain: return Mountain;
                default: return 0f;
            }
        }
        private static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);
    }

    public readonly struct OddsRange
    {
        public OddsRange(float min, float max) { Min = min; Max = max; }
        public float Min { get; }
        public float Max { get; }
        public float Score(float odds)
        {
            if (odds >= Min && odds <= Max) return 1f;
            float distance = odds < Min ? (Min - odds) : (odds - Max);
            return 1f / (1f + distance * 2f);
        }
    }

    public readonly struct PlaybookContext
    {
        public PlaybookContext(
            PersonalityVector commanderPersonality,
            TerrainKind terrain,
            float currentOdds,
            float opposingCommanderHint,
            int defaultMainEffortSector,
            int jitterSeed)
        {
            CommanderPersonality = commanderPersonality;
            Terrain = terrain;
            CurrentOdds = currentOdds;
            OpposingCommanderHint = opposingCommanderHint;
            DefaultMainEffortSector = defaultMainEffortSector;
            JitterSeed = jitterSeed;
        }
        public PersonalityVector CommanderPersonality { get; }
        public TerrainKind Terrain { get; }
        public float CurrentOdds { get; }
        public float OpposingCommanderHint { get; }
        public int DefaultMainEffortSector { get; }
        public int JitterSeed { get; }
    }

    public abstract class TacticalPlaybook
    {
        protected TacticalPlaybook(
            BattlePlanId id,
            string historicalLabel,
            PersonalityFit fit,
            TerrainPreference terrainFit,
            OddsRange preferredOdds,
            float reserveCommitTriggerOdds)
        {
            Id = id;
            HistoricalLabel = historicalLabel ?? "";
            Fit = fit;
            TerrainFit = terrainFit;
            PreferredOdds = preferredOdds;
            ReserveCommitTriggerOdds = reserveCommitTriggerOdds;
        }

        public BattlePlanId Id { get; }
        public string HistoricalLabel { get; }
        public PersonalityFit Fit { get; }
        public TerrainPreference TerrainFit { get; }
        public OddsRange PreferredOdds { get; }
        public float ReserveCommitTriggerOdds { get; }

        public abstract TacticalBattlePlan Instantiate(PlaybookContext ctx);
    }
}
```

- [ ] **Step 4: Add test csproj entries.**

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\TacticalPlaybook.cs" Link="TacticalPlaybook.cs" />
```

- [ ] **Step 5: Run tests, verify pass.**

```bash
dotnet test tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj --filter "TacticalPlaybookTests"
```

Expected: 4 PASS / 0 FAIL.

- [ ] **Step 6: Commit.**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/TacticalPlaybook.cs tests/WhiskeyRealism.Tests/TacticalPlaybookTests.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "$(cat <<'EOF'
feat(orchestrator): add TacticalPlaybook abstract base + scoring types

PersonalityFit (3-D dot in agg/caut/aud), TerrainPreference, OddsRange,
and PlaybookContext. Concrete playbooks override Instantiate(ctx).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: TacticalPlaybookCatalog (registry + selection)

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalPlaybookCatalog.cs`
- Test: `tests/WhiskeyRealism.Tests/TacticalPlaybookCatalogTests.cs`

The catalog registers playbooks and runs the weighted selection algorithm from the umbrella spec §"Selection algorithm".

- [ ] **Step 1: Write failing tests.**

```csharp
// tests/WhiskeyRealism.Tests/TacticalPlaybookCatalogTests.cs
using NUnit.Framework;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Orchestrator;

[TestFixture]
public class TacticalPlaybookCatalogTests
{
    private sealed class FakePlaybook : TacticalPlaybook
    {
        public FakePlaybook(BattlePlanId id, PersonalityFit fit, TerrainPreference terrain, OddsRange odds)
            : base(id, "fake-" + id, fit, terrain, odds, 1.0f) { }
        public override TacticalBattlePlan Instantiate(PlaybookContext ctx) =>
            new TacticalBattlePlan(Id, BattlePhase.Probe, ctx.DefaultMainEffortSector, null, null, ReserveCommitTriggerOdds, 0f, ctx.JitterSeed);
    }

    [Test]
    public void Empty_catalog_returns_null()
    {
        var cat = new TacticalPlaybookCatalog();
        var ctx = new PlaybookContext(default, TerrainKind.Open, 1f, 0f, 0, 0);
        Assert.IsNull(cat.Select(ctx));
    }

    [Test]
    public void Highest_scoring_playbook_wins()
    {
        var cat = new TacticalPlaybookCatalog();
        cat.Register(new FakePlaybook(BattlePlanId.GenericAggressive,
            new PersonalityFit(1f, -1f, 1f),
            new TerrainPreference(1, 1, 1, 1),
            new OddsRange(0.5f, 2f)));
        cat.Register(new FakePlaybook(BattlePlanId.GenericCautious,
            new PersonalityFit(-1f, 1f, -1f),
            new TerrainPreference(1, 1, 1, 1),
            new OddsRange(0.5f, 2f)));

        var aggressive = new PlaybookContext(new PersonalityVector(1f, -1f, 1f, 0, 0), TerrainKind.Open, 1f, 0f, 0, 1);
        var cautious = new PlaybookContext(new PersonalityVector(-1f, 1f, -1f, 0, 0), TerrainKind.Open, 1f, 0f, 0, 1);

        Assert.AreEqual(BattlePlanId.GenericAggressive, cat.Select(aggressive).Id);
        Assert.AreEqual(BattlePlanId.GenericCautious, cat.Select(cautious).Id);
    }

    [Test]
    public void Score_weights_match_umbrella_spec()
    {
        // Umbrella: personality 0.5, terrain 0.2, odds 0.15, hint 0.1, jitter 0.05.
        // Construct two playbooks: one perfect on personality (dominant weight),
        // one perfect on terrain only. Personality should win.
        var cat = new TacticalPlaybookCatalog();
        cat.Register(new FakePlaybook(BattlePlanId.LeeEnvelopment,
            new PersonalityFit(1f, -1f, 1f), new TerrainPreference(0, 0, 0, 0), new OddsRange(0, 0)));
        cat.Register(new FakePlaybook(BattlePlanId.GenericMethodical,
            new PersonalityFit(0, 0, 0), new TerrainPreference(1, 1, 1, 1), new OddsRange(0, 0)));
        var ctx = new PlaybookContext(new PersonalityVector(1f, -1f, 1f, 0, 0), TerrainKind.Open, 5f, 0f, 0, 1);
        Assert.AreEqual(BattlePlanId.LeeEnvelopment, cat.Select(ctx).Id);
    }

    [Test]
    public void Jitter_is_deterministic_for_same_seed()
    {
        var cat = new TacticalPlaybookCatalog();
        cat.Register(new FakePlaybook(BattlePlanId.GenericMethodical, new PersonalityFit(0, 0, 0), new TerrainPreference(1, 1, 1, 1), new OddsRange(0.5f, 2f)));
        var ctx = new PlaybookContext(default, TerrainKind.Open, 1f, 0f, 0, 42);
        var first = cat.Select(ctx).Id;
        var second = cat.Select(ctx).Id;
        Assert.AreEqual(first, second);
    }
}
```

- [ ] **Step 2: Run tests, verify fail.**

```bash
dotnet test tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj --filter "TacticalPlaybookCatalogTests"
```

Expected: build error — `TacticalPlaybookCatalog` not found.

- [ ] **Step 3: Implement.**

```csharp
// src/WhiskeyRealism/Tactical/Orchestrator/TacticalPlaybookCatalog.cs
using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Registers playbooks and runs the weighted selection algorithm.
    ///
    /// score(playbook) = 0.5*personality + 0.2*terrain + 0.15*odds + 0.1*hint + 0.05*jitter
    ///
    /// Per umbrella spec §"Selection algorithm".
    /// </summary>
    public sealed class TacticalPlaybookCatalog
    {
        private readonly List<TacticalPlaybook> _playbooks = new List<TacticalPlaybook>();

        public void Register(TacticalPlaybook playbook)
        {
            if (playbook == null) return;
            _playbooks.Add(playbook);
        }

        public int Count => _playbooks.Count;

        public TacticalPlaybook Select(PlaybookContext ctx)
        {
            if (_playbooks.Count == 0) return null;

            TacticalPlaybook best = null;
            float bestScore = float.NegativeInfinity;
            // Deterministic per-seed jitter via mulberry32-style step.
            uint state = unchecked((uint)ctx.JitterSeed | 1u);
            for (int i = 0; i < _playbooks.Count; i++)
            {
                var pb = _playbooks[i];
                float personalityScore = pb.Fit.Score(ctx.CommanderPersonality);
                float terrainScore = pb.TerrainFit.Score(ctx.Terrain);
                float oddsScore = pb.PreferredOdds.Score(ctx.CurrentOdds);
                float hintScore = ctx.OpposingCommanderHint;
                state = NextRand(state);
                float jitter = (state & 0xFFFF) / 65535f;

                float score = 0.5f * personalityScore + 0.2f * terrainScore + 0.15f * oddsScore + 0.1f * hintScore + 0.05f * jitter;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = pb;
                }
            }
            return best;
        }

        private static uint NextRand(uint x)
        {
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            return x;
        }
    }
}
```

- [ ] **Step 4: Add test csproj entry.**

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\TacticalPlaybookCatalog.cs" Link="TacticalPlaybookCatalog.cs" />
```

- [ ] **Step 5: Run tests, verify pass.**

```bash
dotnet test tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj --filter "TacticalPlaybookCatalogTests"
```

Expected: 4 PASS / 0 FAIL.

- [ ] **Step 6: Commit.**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/TacticalPlaybookCatalog.cs tests/WhiskeyRealism.Tests/TacticalPlaybookCatalogTests.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "$(cat <<'EOF'
feat(orchestrator): add TacticalPlaybookCatalog selection engine

Weighted scoring per umbrella §"Selection algorithm":
0.5 personality + 0.2 terrain + 0.15 odds + 0.1 hint + 0.05 jitter.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Generic playbooks (Aggressive, Cautious, Methodical, Desperate)

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/Playbooks/GenericAggressivePlaybook.cs`
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/Playbooks/GenericCautiousPlaybook.cs`
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/Playbooks/GenericMethodicalPlaybook.cs`
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/Playbooks/GenericDesperatePlaybook.cs`
- Test: `tests/WhiskeyRealism.Tests/GenericPlaybookTests.cs`

Generic fallbacks. Per umbrella §"Seed catalog", they always score above zero so something is always selected. Each is a very small concrete class — `Instantiate` produces a plan id matching its class.

- [ ] **Step 1: Write failing tests.**

```csharp
// tests/WhiskeyRealism.Tests/GenericPlaybookTests.cs
using NUnit.Framework;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Orchestrator;

[TestFixture]
public class GenericPlaybookTests
{
    [Test]
    public void Aggressive_prefers_high_aggression_personality()
    {
        var pb = new GenericAggressivePlaybook();
        var aggressive = new PersonalityVector(0.8f, -0.4f, 0.6f, 0, 0);
        var passive = new PersonalityVector(-0.8f, 0.4f, -0.6f, 0, 0);
        Assert.Greater(pb.Fit.Score(aggressive), pb.Fit.Score(passive));
    }

    [Test]
    public void Cautious_prefers_high_caution_personality()
    {
        var pb = new GenericCautiousPlaybook();
        var cautious = new PersonalityVector(-0.5f, 0.8f, -0.3f, 0, 0);
        var aggressive = new PersonalityVector(0.8f, -0.4f, 0.6f, 0, 0);
        Assert.Greater(pb.Fit.Score(cautious), pb.Fit.Score(aggressive));
    }

    [Test]
    public void Methodical_scores_neutral_personality_well()
    {
        var pb = new GenericMethodicalPlaybook();
        var neutral = new PersonalityVector(0, 0, 0, 0, 0);
        Assert.Greater(pb.Fit.Score(neutral), 0.4f);
    }

    [Test]
    public void Desperate_prefers_extreme_negative_caution()
    {
        var pb = new GenericDesperatePlaybook();
        var desperate = new PersonalityVector(0.3f, -0.9f, 0.3f, 0, 0);
        var cautious = new PersonalityVector(0, 0.9f, 0, 0, 0);
        Assert.Greater(pb.Fit.Score(desperate), pb.Fit.Score(cautious));
    }

    [Test]
    public void Each_generic_instantiates_with_matching_plan_id()
    {
        var ctx = new PlaybookContext(default, TerrainKind.Open, 1f, 0f, 0, 1);
        Assert.AreEqual(BattlePlanId.GenericAggressive, new GenericAggressivePlaybook().Instantiate(ctx).PlanId);
        Assert.AreEqual(BattlePlanId.GenericCautious,   new GenericCautiousPlaybook().Instantiate(ctx).PlanId);
        Assert.AreEqual(BattlePlanId.GenericMethodical, new GenericMethodicalPlaybook().Instantiate(ctx).PlanId);
        Assert.AreEqual(BattlePlanId.GenericDesperate,  new GenericDesperatePlaybook().Instantiate(ctx).PlanId);
    }
}
```

- [ ] **Step 2: Run tests, verify fail.**

```bash
dotnet test tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj --filter "GenericPlaybookTests"
```

- [ ] **Step 3: Implement all four generic playbooks.**

The pattern: minimal concrete class, parameters from the table below, `Instantiate` returns a plan with `Phase=Probe`, three sectors set to ctx default, no fixing/screening yet (corps echelon will allocate those in O3).

| Playbook | Aggression | Caution | Audacity | Open | Wooded | River | Mountain | Odds min/max | ReserveTrigger |
|---|---|---|---|---|---|---|---|---|---|
| GenericAggressive | +0.7 | -0.4 | +0.5 | 0.8 | 0.6 | 0.5 | 0.4 | 0.7 / 1.6 | 1.2 |
| GenericCautious | -0.5 | +0.7 | -0.4 | 0.6 | 0.7 | 0.7 | 0.7 | 0.6 / 1.4 | 1.5 |
| GenericMethodical | +0.0 | +0.3 | +0.0 | 0.7 | 0.7 | 0.6 | 0.6 | 0.8 / 1.5 | 1.3 |
| GenericDesperate | +0.4 | -0.8 | +0.3 | 0.5 | 0.5 | 0.5 | 0.5 | 0.3 / 0.8 | 0.9 |

Code template (one file shown; the other three follow the identical pattern with their parameter row above):

```csharp
// src/WhiskeyRealism/Tactical/Orchestrator/Playbooks/GenericAggressivePlaybook.cs
namespace WhiskeyRealism.Tactical.Orchestrator
{
    public sealed class GenericAggressivePlaybook : TacticalPlaybook
    {
        public GenericAggressivePlaybook() : base(
            BattlePlanId.GenericAggressive,
            "generic-aggressive",
            new PersonalityFit(aggression: 0.7f, caution: -0.4f, audacity: 0.5f),
            new TerrainPreference(open: 0.8f, wooded: 0.6f, river: 0.5f, mountain: 0.4f),
            new OddsRange(min: 0.7f, max: 1.6f),
            reserveCommitTriggerOdds: 1.2f)
        { }

        public override TacticalBattlePlan Instantiate(PlaybookContext ctx) =>
            new TacticalBattlePlan(
                Id,
                BattlePhase.Probe,
                ctx.DefaultMainEffortSector,
                null,
                null,
                ReserveCommitTriggerOdds,
                0f,
                ctx.JitterSeed);
    }
}
```

Repeat for the other three with their parameters from the table; each file gets a corresponding class name and `BattlePlanId.<value>`.

- [ ] **Step 4: Add Compile Include entries (4 lines, one per file).**

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\Playbooks\GenericAggressivePlaybook.cs" Link="GenericAggressivePlaybook.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\Playbooks\GenericCautiousPlaybook.cs" Link="GenericCautiousPlaybook.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\Playbooks\GenericMethodicalPlaybook.cs" Link="GenericMethodicalPlaybook.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\Playbooks\GenericDesperatePlaybook.cs" Link="GenericDesperatePlaybook.cs" />
```

- [ ] **Step 5: Run tests, verify pass.**

```bash
dotnet test tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj --filter "GenericPlaybookTests"
```

Expected: 5 PASS / 0 FAIL.

- [ ] **Step 6: Commit.**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/Playbooks/Generic*.cs tests/WhiskeyRealism.Tests/GenericPlaybookTests.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "$(cat <<'EOF'
feat(orchestrator): add 4 generic fallback playbooks

GenericAggressive / Cautious / Methodical / Desperate. Always score
above zero so the catalog never returns null even when no historical
playbook matches.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Major historical playbooks (Lee, Jackson, McClellan, Sherman, Grant)

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/Playbooks/LeeEnvelopmentPlaybook.cs`
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/Playbooks/JacksonValleyShufflePlaybook.cs`
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/Playbooks/McClellanPreparedDefensePlaybook.cs`
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/Playbooks/ShermanManeuverFixPlaybook.cs`
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/Playbooks/GrantContinuousAttritionPlaybook.cs`
- Test: `tests/WhiskeyRealism.Tests/HistoricalPlaybookSelectionTests.cs`

Same shape as Task 4. Each playbook is parameter-only — its identity is the values, not the class body.

| Playbook | Aggression | Caution | Audacity | Open | Wooded | River | Mountain | Odds min/max | ReserveTrigger |
|---|---|---|---|---|---|---|---|---|---|
| LeeEnvelopment | +0.8 | -0.4 | +0.7 | 0.7 | 0.8 | 0.5 | 0.4 | 0.8 / 1.4 | 1.3 |
| JacksonValleyShuffle | +0.7 | -0.5 | +0.9 | 0.5 | 0.7 | 0.7 | 0.9 | 0.5 / 0.9 | 1.0 |
| McClellanPreparedDefense | -0.6 | +0.8 | -0.7 | 0.6 | 0.7 | 0.7 | 0.7 | 0.6 / 1.5 | 1.6 |
| ShermanManeuverFix | +0.7 | -0.3 | +0.6 | 0.9 | 0.5 | 0.5 | 0.4 | 0.9 / 1.6 | 1.2 |
| GrantContinuousAttrition | +0.6 | +0.2 | +0.3 | 0.7 | 0.6 | 0.6 | 0.5 | 1.3 / 2.5 | 1.4 |

- [ ] **Step 1: Write the parameterized test (one fixture, one TestCase per playbook).**

```csharp
// tests/WhiskeyRealism.Tests/HistoricalPlaybookSelectionTests.cs
using NUnit.Framework;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Orchestrator;

[TestFixture]
public class HistoricalPlaybookSelectionTests
{
    [Test]
    public void Lee_personality_selects_lee_envelopment()
    {
        var cat = SeedCatalog.AllHistoricalAndGeneric();
        var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
        var ctx = new PlaybookContext(lee, TerrainKind.Wooded, currentOdds: 1.1f, opposingCommanderHint: 0f, defaultMainEffortSector: 0, jitterSeed: 1);
        Assert.AreEqual(BattlePlanId.LeeEnvelopment, cat.Select(ctx).Id);
    }

    [Test]
    public void McClellan_personality_selects_mcclellan_defense()
    {
        var cat = SeedCatalog.AllHistoricalAndGeneric();
        var mcc = new PersonalityVector(-0.6f, 0.8f, -0.7f, 0.7f, 0.4f);
        var ctx = new PlaybookContext(mcc, TerrainKind.Open, currentOdds: 1.2f, opposingCommanderHint: 0f, defaultMainEffortSector: 0, jitterSeed: 1);
        Assert.AreEqual(BattlePlanId.McClellanPreparedDefense, cat.Select(ctx).Id);
    }

    [Test]
    public void Jackson_personality_in_mountains_at_low_odds_selects_valley_shuffle()
    {
        var cat = SeedCatalog.AllHistoricalAndGeneric();
        var jackson = new PersonalityVector(0.7f, -0.5f, 0.9f, 0.5f, 0.0f);
        var ctx = new PlaybookContext(jackson, TerrainKind.Mountain, currentOdds: 0.7f, opposingCommanderHint: 0f, defaultMainEffortSector: 0, jitterSeed: 1);
        Assert.AreEqual(BattlePlanId.JacksonValleyShuffle, cat.Select(ctx).Id);
    }

    [Test]
    public void Grant_personality_at_favorable_odds_selects_attrition()
    {
        var cat = SeedCatalog.AllHistoricalAndGeneric();
        var grant = new PersonalityVector(0.6f, 0.2f, 0.3f, 0.6f, 0.4f);
        var ctx = new PlaybookContext(grant, TerrainKind.Open, currentOdds: 1.6f, opposingCommanderHint: 0f, defaultMainEffortSector: 0, jitterSeed: 1);
        Assert.AreEqual(BattlePlanId.GrantContinuousAttrition, cat.Select(ctx).Id);
    }

    [Test]
    public void Sherman_personality_in_open_terrain_selects_maneuver_fix()
    {
        var cat = SeedCatalog.AllHistoricalAndGeneric();
        var sherman = new PersonalityVector(0.7f, -0.3f, 0.6f, 0.4f, 0.5f);
        var ctx = new PlaybookContext(sherman, TerrainKind.Open, currentOdds: 1.3f, opposingCommanderHint: 0f, defaultMainEffortSector: 0, jitterSeed: 1);
        Assert.AreEqual(BattlePlanId.ShermanManeuverFix, cat.Select(ctx).Id);
    }
}

internal static class SeedCatalog
{
    public static TacticalPlaybookCatalog AllHistoricalAndGeneric()
    {
        var cat = new TacticalPlaybookCatalog();
        cat.Register(new LeeEnvelopmentPlaybook());
        cat.Register(new JacksonValleyShufflePlaybook());
        cat.Register(new McClellanPreparedDefensePlaybook());
        cat.Register(new ShermanManeuverFixPlaybook());
        cat.Register(new GrantContinuousAttritionPlaybook());
        // Task 6 adds Longstreet/Hooker/Hood/Burnside/Bragg.
        cat.Register(new GenericAggressivePlaybook());
        cat.Register(new GenericCautiousPlaybook());
        cat.Register(new GenericMethodicalPlaybook());
        cat.Register(new GenericDesperatePlaybook());
        return cat;
    }
}
```

- [ ] **Step 2: Run tests, verify fail (types not found).**

- [ ] **Step 3: Implement each playbook with the parameter row from the table above.** Use the `GenericAggressivePlaybook.cs` template from Task 4 verbatim, swapping class name, `BattlePlanId.<value>`, the four `PersonalityFit`/`TerrainPreference`/`OddsRange`/reserve parameters, and the historical label string ("lee-envelopment", "jackson-valley-shuffle", "mcclellan-prepared-defense", "sherman-maneuver-fix", "grant-continuous-attrition").

- [ ] **Step 4: Add 5 Compile Include entries to test csproj.**

- [ ] **Step 5: Run tests, verify 5 PASS.**

- [ ] **Step 6: Commit.**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/Playbooks/LeeEnvelopmentPlaybook.cs src/WhiskeyRealism/Tactical/Orchestrator/Playbooks/JacksonValleyShufflePlaybook.cs src/WhiskeyRealism/Tactical/Orchestrator/Playbooks/McClellanPreparedDefensePlaybook.cs src/WhiskeyRealism/Tactical/Orchestrator/Playbooks/ShermanManeuverFixPlaybook.cs src/WhiskeyRealism/Tactical/Orchestrator/Playbooks/GrantContinuousAttritionPlaybook.cs tests/WhiskeyRealism.Tests/HistoricalPlaybookSelectionTests.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "$(cat <<'EOF'
feat(orchestrator): seed 5 major historical playbooks (Lee, Jackson, McClellan, Sherman, Grant)

Parameter-only concretes; selection algorithm picks them when the
army CO's personality vector matches their historical fit.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Secondary historical playbooks (Longstreet, Hooker, Hood, Burnside, Bragg)

Identical shape to Task 5. Add the rows below as new files; extend `HistoricalPlaybookSelectionTests` with one selection-confirmation test per playbook; register them in `SeedCatalog.AllHistoricalAndGeneric()`.

| Playbook | Aggression | Caution | Audacity | Open | Wooded | River | Mountain | Odds min/max | ReserveTrigger |
|---|---|---|---|---|---|---|---|---|---|
| LongstreetDefensiveOverslope | -0.2 | +0.5 | -0.5 | 0.5 | 0.7 | 0.6 | 0.7 | 0.7 / 1.2 | 1.5 |
| HookerFlankDeparture | +0.6 | -0.2 | -0.4 | 0.7 | 0.6 | 0.5 | 0.4 | 1.0 / 1.5 | 1.3 |
| HoodFrontalAssault | +0.9 | -0.7 | +0.6 | 0.7 | 0.6 | 0.5 | 0.4 | 0.5 / 1.2 | 1.0 |
| BurnsideForcedAssault | +0.5 | -0.5 | -0.3 | 0.6 | 0.6 | 0.5 | 0.5 | 0.6 / 1.3 | 1.1 |
| BraggIndecisiveCommit | +0.0 | +0.3 | -0.4 | 0.6 | 0.6 | 0.6 | 0.6 | 0.8 / 1.4 | 1.4 |

- [ ] **Steps 1-6:** Same pattern as Task 5. Each new test calls `SeedCatalog.AllHistoricalAndGeneric()` (which Task 6 must extend to register the 5 new playbooks) and asserts the matching `BattlePlanId`.

Commit message:

```
feat(orchestrator): seed 5 secondary historical playbooks (Longstreet, Hooker, Hood, Burnside, Bragg)
```

---

### Task 7: ArmyOrchestrator skeleton + initial plan pick

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs`
- Test: `tests/WhiskeyRealism.Tests/ArmyOrchestratorTests.cs`

The army echelon. Holds a `TacticalCommanderRoster` reference (from O0), a `TacticalPlaybookCatalog`, the current `TacticalBattlePlan`, and a `CurrentMacroAi` that the rewired #44 patch reads.

- [ ] **Step 1: Write failing tests.**

```csharp
// tests/WhiskeyRealism.Tests/ArmyOrchestratorTests.cs
using NUnit.Framework;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Orchestrator;

[TestFixture]
public class ArmyOrchestratorTests
{
    private static TacticalPlaybookCatalog SeededCatalog() => SeedCatalog.AllHistoricalAndGeneric();

    [Test]
    public void New_orchestrator_has_no_plan_until_picked()
    {
        var orch = new ArmyOrchestrator(allianceId: 0, catalog: SeededCatalog(), commanderPersonality: default);
        Assert.IsFalse(orch.HasPlan);
        Assert.AreEqual(-1, orch.CurrentMacroAi);
    }

    [Test]
    public void PickInitialPlan_with_lee_personality_assigns_lee_envelopment()
    {
        var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
        var orch = new ArmyOrchestrator(allianceId: 0, catalog: SeededCatalog(), commanderPersonality: lee);
        orch.PickInitialPlan(new ArmyEvidence(currentOdds: 1.1f, terrain: TerrainKind.Wooded, defaultMainEffortSector: 0));
        Assert.IsTrue(orch.HasPlan);
        Assert.AreEqual(BattlePlanId.LeeEnvelopment, orch.CurrentPlan.PlanId);
        Assert.AreEqual(BattlePhase.Probe, orch.CurrentPlan.Phase);
    }

    [Test]
    public void CurrentMacroAi_attack_when_plan_phase_is_main_effort_with_aggressive_personality()
    {
        var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
        var orch = new ArmyOrchestrator(allianceId: 0, catalog: SeededCatalog(), commanderPersonality: lee);
        orch.PickInitialPlan(new ArmyEvidence(1.2f, TerrainKind.Open, 0));
        orch.AdvancePhase(BattlePhase.MainEffort);
        Assert.AreEqual(1 /* AIBattle.macroai attack */, orch.CurrentMacroAi);
    }

    [Test]
    public void CurrentMacroAi_defend_when_plan_phase_is_consolidate_with_cautious_personality()
    {
        var mcc = new PersonalityVector(-0.6f, 0.8f, -0.7f, 0.7f, 0.4f);
        var orch = new ArmyOrchestrator(allianceId: 0, catalog: SeededCatalog(), commanderPersonality: mcc);
        orch.PickInitialPlan(new ArmyEvidence(1.0f, TerrainKind.Open, 0));
        orch.AdvancePhase(BattlePhase.Consolidate);
        Assert.AreEqual(2 /* AIBattle.macroai defend */, orch.CurrentMacroAi);
    }

    [Test]
    public void EmitArmyIntent_returns_intent_matching_current_plan()
    {
        var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
        var orch = new ArmyOrchestrator(allianceId: 0, catalog: SeededCatalog(), commanderPersonality: lee);
        orch.PickInitialPlan(new ArmyEvidence(1.1f, TerrainKind.Wooded, 2));
        var intent = orch.EmitArmyIntent();
        Assert.AreEqual(BattlePlanId.LeeEnvelopment, intent.PlanId);
        Assert.AreEqual(BattlePhase.Probe, intent.Phase);
        Assert.AreEqual(2, intent.MainEffortSector);
        Assert.Greater(intent.AggressionBias01, 0.5f);
    }
}
```

- [ ] **Step 2: Run tests, verify fail.**

- [ ] **Step 3: Implement.**

```csharp
// src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs
using WhiskeyRealism.Strategic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Evidence the orchestrator needs at decision time. Built by the runtime
    /// partial of TacticalBattleCoordinator from existing ledgers; passed into
    /// PickInitialPlan and Replan. Test-friendly (no Unity types).
    /// </summary>
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

    /// <summary>
    /// Army echelon orchestrator. Owns the army CO's plan, exposes CurrentMacroAi
    /// for the rewired BattleMacroStrategyPatch to read, emits ArmyIntent down to
    /// corps each tick.
    ///
    /// AIBattle.macroai values: -1 dynamic / 0 assault / 1 attack / 2 defend / 3 retreat.
    /// </summary>
    public sealed class ArmyOrchestrator : EchelonOrchestrator
    {
        private readonly TacticalPlaybookCatalog _catalog;
        private readonly PersonalityVector _commanderPersonality;
        private TacticalBattlePlan _plan;

        public ArmyOrchestrator(int allianceId, TacticalPlaybookCatalog catalog, PersonalityVector commanderPersonality)
            : base(EchelonKind.Army, allianceId)
        {
            _catalog = catalog;
            _commanderPersonality = commanderPersonality;
            HasPlan = false;
        }

        public bool HasPlan { get; private set; }
        public TacticalBattlePlan CurrentPlan => _plan;
        public PersonalityVector CommanderPersonality => _commanderPersonality;

        /// <summary>
        /// AIBattle.macroai derived from current plan + phase + personality.
        /// Returns -1 (dynamic) when no plan picked yet, signaling the rewired
        /// patch to leave vanilla's macroai alone.
        /// </summary>
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
            var ctx = new PlaybookContext(
                _commanderPersonality,
                evidence.Terrain,
                evidence.CurrentOdds,
                opposingCommanderHint: 0f,
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

        public void AdvancePhase(BattlePhase next)
        {
            if (!HasPlan) return;
            _plan = _plan.WithPhase(next);
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
    }
}
```

- [ ] **Step 4: Add Compile Include entry.**

- [ ] **Step 5: Run tests, verify 5 PASS.**

- [ ] **Step 6: Commit.**

```
feat(orchestrator): add ArmyOrchestrator with initial plan pick
```

---

### Task 8: Replan trigger evaluation

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/ArmyReplanTriggers.cs`
- Test: `tests/WhiskeyRealism.Tests/ArmyReplanTriggersTests.cs`
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs` — add `CheckReplanTriggers(...)` and `Replan(...)` methods.

Per umbrella spec §"Replan triggers" — 7 triggers + a rate-limit guard.

- [ ] **Step 1: Write failing tests** (one test per trigger).

```csharp
// tests/WhiskeyRealism.Tests/ArmyReplanTriggersTests.cs
using NUnit.Framework;
using WhiskeyRealism.Tactical.Orchestrator;

[TestFixture]
public class ArmyReplanTriggersTests
{
    [Test]
    public void Phase_deadline_fires_when_age_exceeds_phase_budget()
    {
        var input = new ReplanTriggerInput(planAgeSeconds: 200f, currentPhase: BattlePhase.Probe,
            mainEffortOwnStrength: 5000f, mainEffortHistoryOwnStrength: 5000f,
            globalOddsCurrent: 1.0f, globalOddsHistory: 1.0f,
            armyMoraleCurrent: 1.0f, armyMoraleFloor: 0.4f,
            reservesCommittedFraction: 0.5f, reinforcementsArrivingDelta: 0f,
            enemyMainEffortShiftConfidenceWeighted: 0f);
        Assert.AreEqual(ReplanTrigger.PhaseDeadline, ArmyReplanTriggers.Evaluate(input));
    }

    [Test]
    public void Main_effort_sector_loss_fires_below_threshold()
    {
        var input = new ReplanTriggerInput(planAgeSeconds: 30f, currentPhase: BattlePhase.MainEffort,
            mainEffortOwnStrength: 1500f, mainEffortHistoryOwnStrength: 5000f,  // 30% of historic
            globalOddsCurrent: 1.0f, globalOddsHistory: 1.0f,
            armyMoraleCurrent: 1.0f, armyMoraleFloor: 0.4f,
            reservesCommittedFraction: 0.5f, reinforcementsArrivingDelta: 0f,
            enemyMainEffortShiftConfidenceWeighted: 0f);
        Assert.AreEqual(ReplanTrigger.MainEffortSectorLoss, ArmyReplanTriggers.Evaluate(input));
    }

    [Test]
    public void Force_imbalance_shift_fires_when_odds_cross_hysteresis()
    {
        var below = new ReplanTriggerInput(30f, BattlePhase.MainEffort, 5000, 5000, 0.65f, 1.5f, 1f, 0.4f, 0.5f, 0f, 0f);
        var above = new ReplanTriggerInput(30f, BattlePhase.MainEffort, 5000, 5000, 1.5f, 1.0f, 1f, 0.4f, 0.5f, 0f, 0f);
        Assert.AreEqual(ReplanTrigger.ForceImbalanceShift, ArmyReplanTriggers.Evaluate(below));
        Assert.AreEqual(ReplanTrigger.ForceImbalanceShift, ArmyReplanTriggers.Evaluate(above));
    }

    [Test]
    public void Casualty_threshold_fires_when_morale_below_floor()
    {
        var input = new ReplanTriggerInput(30f, BattlePhase.MainEffort, 5000, 5000, 1.0f, 1.0f, 0.3f, 0.4f, 0.5f, 0f, 0f);
        Assert.AreEqual(ReplanTrigger.CasualtyThreshold, ArmyReplanTriggers.Evaluate(input));
    }

    [Test]
    public void Reserve_exhaustion_fires_below_85_percent_committed()
    {
        var input = new ReplanTriggerInput(30f, BattlePhase.MainEffort, 5000, 5000, 1.0f, 1.0f, 1f, 0.4f, 0.9f, 0f, 0f);
        Assert.AreEqual(ReplanTrigger.ReserveExhaustion, ArmyReplanTriggers.Evaluate(input));
    }

    [Test]
    public void Reinforcement_arrival_fires_on_nonzero_delta()
    {
        var input = new ReplanTriggerInput(30f, BattlePhase.MainEffort, 5000, 5000, 1.0f, 1.0f, 1f, 0.4f, 0.5f, reinforcementsArrivingDelta: 2500f, 0f);
        Assert.AreEqual(ReplanTrigger.ReinforcementArrival, ArmyReplanTriggers.Evaluate(input));
    }

    [Test]
    public void Enemy_intent_shift_fires_when_confidence_weighted_shift_exceeds_threshold()
    {
        var input = new ReplanTriggerInput(30f, BattlePhase.MainEffort, 5000, 5000, 1.0f, 1.0f, 1f, 0.4f, 0.5f, 0f, enemyMainEffortShiftConfidenceWeighted: 0.55f);
        Assert.AreEqual(ReplanTrigger.EnemyIntentShift, ArmyReplanTriggers.Evaluate(input));
    }

    [Test]
    public void No_trigger_when_all_conditions_normal()
    {
        var input = new ReplanTriggerInput(30f, BattlePhase.MainEffort, 5000, 5000, 1.0f, 1.0f, 1f, 0.4f, 0.5f, 0f, 0f);
        Assert.AreEqual(ReplanTrigger.None, ArmyReplanTriggers.Evaluate(input));
    }
}
```

- [ ] **Step 2: Run, verify fail.**

- [ ] **Step 3: Implement.**

```csharp
// src/WhiskeyRealism/Tactical/Orchestrator/ArmyReplanTriggers.cs
namespace WhiskeyRealism.Tactical.Orchestrator
{
    public enum ReplanTrigger
    {
        None = 0,
        PhaseDeadline,
        MainEffortSectorLoss,
        EnemyIntentShift,
        ForceImbalanceShift,
        CasualtyThreshold,
        ReserveExhaustion,
        ReinforcementArrival,
    }

    public readonly struct ReplanTriggerInput
    {
        public ReplanTriggerInput(
            float planAgeSeconds,
            BattlePhase currentPhase,
            float mainEffortOwnStrength,
            float mainEffortHistoryOwnStrength,
            float globalOddsCurrent,
            float globalOddsHistory,
            float armyMoraleCurrent,
            float armyMoraleFloor,
            float reservesCommittedFraction,
            float reinforcementsArrivingDelta,
            float enemyMainEffortShiftConfidenceWeighted)
        {
            PlanAgeSeconds = planAgeSeconds;
            CurrentPhase = currentPhase;
            MainEffortOwnStrength = mainEffortOwnStrength;
            MainEffortHistoryOwnStrength = mainEffortHistoryOwnStrength;
            GlobalOddsCurrent = globalOddsCurrent;
            GlobalOddsHistory = globalOddsHistory;
            ArmyMoraleCurrent = armyMoraleCurrent;
            ArmyMoraleFloor = armyMoraleFloor;
            ReservesCommittedFraction = reservesCommittedFraction;
            ReinforcementsArrivingDelta = reinforcementsArrivingDelta;
            EnemyMainEffortShiftConfidenceWeighted = enemyMainEffortShiftConfidenceWeighted;
        }
        public float PlanAgeSeconds { get; }
        public BattlePhase CurrentPhase { get; }
        public float MainEffortOwnStrength { get; }
        public float MainEffortHistoryOwnStrength { get; }
        public float GlobalOddsCurrent { get; }
        public float GlobalOddsHistory { get; }
        public float ArmyMoraleCurrent { get; }
        public float ArmyMoraleFloor { get; }
        public float ReservesCommittedFraction { get; }
        public float ReinforcementsArrivingDelta { get; }
        public float EnemyMainEffortShiftConfidenceWeighted { get; }
    }

    public static class ArmyReplanTriggers
    {
        // Per umbrella §"Replan triggers". Thresholds are seed values; future
        // tuning may move them into config flags.
        public const float PhaseBudgetSeconds = 180f;
        public const float MainEffortLossFraction = 0.5f;
        public const float OddsLowHysteresis = 0.7f;
        public const float OddsHighHysteresis = 1.4f;
        public const float ReservesAlmostSpent = 0.85f;
        public const float EnemyShiftConfidenceFloor = 0.5f;

        public static ReplanTrigger Evaluate(ReplanTriggerInput i)
        {
            // Order matters — phase-deadline checked first, intent-shift last so
            // hard battlefield events take precedence over soft inference.
            if (i.PlanAgeSeconds >= PhaseBudgetSeconds) return ReplanTrigger.PhaseDeadline;
            if (i.MainEffortHistoryOwnStrength > 0f &&
                i.MainEffortOwnStrength / i.MainEffortHistoryOwnStrength <= MainEffortLossFraction)
                return ReplanTrigger.MainEffortSectorLoss;
            if (i.GlobalOddsCurrent <= OddsLowHysteresis && i.GlobalOddsHistory > OddsLowHysteresis)
                return ReplanTrigger.ForceImbalanceShift;
            if (i.GlobalOddsCurrent >= OddsHighHysteresis && i.GlobalOddsHistory < OddsHighHysteresis)
                return ReplanTrigger.ForceImbalanceShift;
            if (i.ArmyMoraleCurrent < i.ArmyMoraleFloor) return ReplanTrigger.CasualtyThreshold;
            if (i.ReservesCommittedFraction >= ReservesAlmostSpent) return ReplanTrigger.ReserveExhaustion;
            if (i.ReinforcementsArrivingDelta > 1f) return ReplanTrigger.ReinforcementArrival;
            if (i.EnemyMainEffortShiftConfidenceWeighted >= EnemyShiftConfidenceFloor) return ReplanTrigger.EnemyIntentShift;
            return ReplanTrigger.None;
        }
    }
}
```

Then extend `ArmyOrchestrator`:

```csharp
// In ArmyOrchestrator.cs, add:
public ReplanTrigger CheckReplanTriggers(ReplanTriggerInput input) => ArmyReplanTriggers.Evaluate(input);

public void Replan(ArmyEvidence evidence)
{
    PickInitialPlan(evidence);  // re-runs selection; new plan replaces old
}
```

- [ ] **Step 4: Add Compile Include entry.**

- [ ] **Step 5: Run, verify 8 PASS.**

- [ ] **Step 6: Commit.**

```
feat(orchestrator): add ArmyOrchestrator replan trigger evaluation
```

---

### Task 9: Wire ArmyOrchestrator into TacticalBattleOrchestrator

**Files:**
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleOrchestrator.cs`

Currently `Echelons` is empty per O0. Add a typed accessor for the army echelon and ensure `Tick()` cascades into it.

- [ ] **Step 1: Modify `TacticalBattleOrchestrator.cs`.**

```csharp
// Replace the body of TacticalBattleOrchestrator with:
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    public sealed class TacticalBattleOrchestrator
    {
        public TacticalBattleOrchestrator(int allianceId, TacticalCommanderRoster roster)
        {
            AllianceId = allianceId;
            Roster = roster;
            Echelons = new List<EchelonOrchestrator>();
        }

        public int AllianceId { get; }
        public TacticalCommanderRoster Roster { get; }
        public List<EchelonOrchestrator> Echelons { get; }
        public int TickCount { get; private set; }
        public ArmyOrchestrator Army { get; private set; }

        public void AttachArmy(ArmyOrchestrator army)
        {
            Army = army;
            if (army != null && !Echelons.Contains(army)) Echelons.Add(army);
        }

        public void Tick()
        {
            TickCount++;
            for (int i = 0; i < Echelons.Count; i++) Echelons[i]?.Tick();
        }

        public void PropagateIntent()
        {
            for (int i = 0; i < Echelons.Count; i++) Echelons[i]?.PropagateIntent();
        }
    }
}
```

- [ ] **Step 2: No new test file** (existing O0 tests still pass; integration validated by Task 10's runtime test).

- [ ] **Step 3: Run full harness, verify no regression.**

```bash
dotnet test tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | tail -5
```

Expected: PASS count >= baseline from Step P3.

- [ ] **Step 4: Commit.**

```
feat(orchestrator): expose Army echelon on TacticalBattleOrchestrator
```

---

### Task 10: Vanilla commander discovery + army instantiation in runtime

**Files:**
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs` (replace the empty `DiscoverCommandersFromVanilla()` stub and add ArmyOrchestrator instantiation per side after `BuildAndActivate`).

Currently `DiscoverCommandersFromVanilla` returns `Array.Empty<SyntheticCommanderInput>()` (O0 stub). Replace with vanilla `BattleUnits.GetCommandingOfficerFromSide(side)` + `GameVars.commander[id]` lookup; create one synthetic input per side at `EchelonKind.Army`. After `BuildAndActivate`, instantiate an `ArmyOrchestrator` per non-suppressed side using the roster's army-tier entry, attach via `TacticalBattleOrchestrator.AttachArmy`, and call `PickInitialPlan` with current evidence.

- [ ] **Step 1: Modify `TacticalBattleCoordinatorRuntime.cs`.**

Replace the `DiscoverCommandersFromVanilla` stub:

```csharp
private static IEnumerable<SyntheticCommanderInput> DiscoverCommandersFromVanilla()
{
    var inputs = new List<SyntheticCommanderInput>();
    try
    {
        var bunits = ResolveBattleUnits();
        if (bunits == null) return inputs;
        for (int side = 0; side < 2; side++)
        {
            int allianceId = SafeAllianceForSide(bunits, side);
            if (allianceId < 0 || allianceId >= 2) continue;
            int commanderId = SafeCommanderId(bunits, side);
            string name = SafeCommanderName(commanderId);
            if (string.IsNullOrEmpty(name)) name = "ArmyCO_side" + side;
            inputs.Add(new SyntheticCommanderInput(name, EchelonKind.Army, allianceId));
        }
    }
    catch (Exception e)
    {
        Plugin.Log.LogWarning("[TacticalOrchestrator] DiscoverCommandersFromVanilla degraded: " + e.GetType().Name + " " + e.Message);
    }
    return inputs;
}

private static BattleUnits ResolveBattleUnits()
{
    try
    {
        if (GameVars.activebattle == null) return null;
        var field = HarmonyLib.AccessTools.Field(typeof(AIBattle), "bunits");
        return field?.GetValue(GameVars.activebattle) as BattleUnits;
    }
    catch
    {
        return null;
    }
}

private static int SafeAllianceForSide(BattleUnits bunits, int side)
{
    try
    {
        if (bunits == null || bunits.alliance == null || side < 0 || side >= bunits.alliance.Length) return -1;
        return bunits.alliance[side];
    }
    catch { return -1; }
}

private static int SafeCommanderId(BattleUnits bunits, int side)
{
    try { return bunits.GetCommandingOfficerFromSide(side); } catch { return -1; }
}

private static string SafeCommanderName(int commanderId)
{
    try
    {
        if (GameVars.commander == null || commanderId < 0 || commanderId >= GameVars.commander.Count) return null;
        var c = GameVars.commander[commanderId];
        return string.IsNullOrEmpty(c.name) ? null : c.name;
    }
    catch { return null; }
}
```

Then extend `OnBattleStart` (in the same file) to build ArmyOrchestrators per non-suppressed side after `BuildAndActivate`:

```csharp
// After BuildAndActivate(...) inside OnBattleStart, before the LogInfo telemetry:
if (Plugin.EnableTacticalOrchestratorArmy.Value)
{
    AttachArmyIfActive(side0);
    AttachArmyIfActive(side1);
}

// New helper:
private static void AttachArmyIfActive(TacticalBattleOrchestrator side)
{
    if (side == null) return;
    var armyEntry = FindArmyEntry(side);
    if (armyEntry == null) return;
    var army = new ArmyOrchestrator(side.AllianceId, BuiltInPlaybooks.SeedCatalog(), armyEntry.PersonalityVector);
    side.AttachArmy(army);
    var evidence = BuildArmyEvidenceForSide(side.AllianceId);
    army.PickInitialPlan(evidence);
    Plugin.Log.LogInfo("[TacticalPlan] side=" + side.AllianceId + " plan=" + army.CurrentPlan.PlanId + " phase=" + army.CurrentPlan.Phase + " mainEffort=" + army.CurrentPlan.MainEffortSector);
}

private static CommanderRosterEntry FindArmyEntry(TacticalBattleOrchestrator side)
{
    if (side?.Roster == null) return null;
    foreach (var e in side.Roster.GetSide(side.AllianceId))
        if (e.Echelon == EchelonKind.Army) return e;
    return null;
}

private static ArmyEvidence BuildArmyEvidenceForSide(int allianceId)
{
    // O1 baseline evidence: terrain unknown → Open default; odds via SideInfo.
    // O2 will replace this with TacticalIntentModel-driven evidence.
    var bunits = ResolveBattleUnits();
    int side = -1;
    try
    {
        for (int s = 0; s < 2; s++)
            if (bunits != null && bunits.alliance != null && s < bunits.alliance.Length && bunits.alliance[s] == allianceId)
            { side = s; break; }
    }
    catch { }
    float own = 0f, enemyTotal = 0f;
    try
    {
        if (bunits != null && bunits.sideinformation != null && side >= 0 && side < bunits.sideinformation.Length)
            own = System.Math.Max(1f, bunits.sideinformation[side].totalactiveforce);
        for (int s = 0; s < 2 && bunits?.sideinformation != null; s++)
            if (s != side && s < bunits.sideinformation.Length) enemyTotal += System.Math.Max(0f, bunits.sideinformation[s].totalactiveforce);
    }
    catch { }
    float odds = enemyTotal <= 0f ? 1f : own / enemyTotal;
    return new ArmyEvidence(odds, TerrainKind.Open, defaultMainEffortSector: 0);
}
```

The `BuiltInPlaybooks.SeedCatalog()` factory is a small helper I'll add in this same task (it builds the catalog the same way `SeedCatalog.AllHistoricalAndGeneric()` does in tests but lives in the runtime partial of the orchestrator namespace). Add as a static method in a new file:

```csharp
// src/WhiskeyRealism/Tactical/Orchestrator/BuiltInPlaybooks.cs
namespace WhiskeyRealism.Tactical.Orchestrator
{
    internal static class BuiltInPlaybooks
    {
        public static TacticalPlaybookCatalog SeedCatalog()
        {
            var c = new TacticalPlaybookCatalog();
            c.Register(new LeeEnvelopmentPlaybook());
            c.Register(new JacksonValleyShufflePlaybook());
            c.Register(new McClellanPreparedDefensePlaybook());
            c.Register(new ShermanManeuverFixPlaybook());
            c.Register(new GrantContinuousAttritionPlaybook());
            c.Register(new LongstreetDefensiveOverslopePlaybook());
            c.Register(new HookerFlankDeparturePlaybook());
            c.Register(new HoodFrontalAssaultPlaybook());
            c.Register(new BurnsideForcedAssaultPlaybook());
            c.Register(new BraggIndecisiveCommitPlaybook());
            c.Register(new GenericAggressivePlaybook());
            c.Register(new GenericCautiousPlaybook());
            c.Register(new GenericMethodicalPlaybook());
            c.Register(new GenericDesperatePlaybook());
            return c;
        }
    }
}
```

- [ ] **Step 2: Add Compile Include for `BuiltInPlaybooks.cs`** (test csproj). The runtime partial of `TacticalBattleCoordinator` is excluded from tests, so `OnBattleStart` itself isn't tested directly here.

- [ ] **Step 3: Run full harness.** Expected: no regression.

- [ ] **Step 4: Commit.**

```
feat(orchestrator): instantiate ArmyOrchestrator from vanilla CO at battle start
```

---

### Task 11: Plugin config flags

**Files:**
- Modify: `src/WhiskeyRealism/Plugin.cs`

Add three flags following the existing `Config.Bind` pattern (section `"Tactical Orchestrator"` already exists for the master flag from O0). Place near `EnableTacticalBattleOrchestrator` block (~line 247).

- [ ] **Step 1: Add field declarations** (in the field block ~line 49-52, near `EnableTacticalBattleOrchestrator`):

```csharp
public static ConfigEntry<bool> EnableTacticalOrchestratorArmy;
public static ConfigEntry<int> TacticalOrchestratorMinReplanSeconds;
public static ConfigEntry<bool> TacticalOrchestratorVerboseLogging;
```

- [ ] **Step 2: Add Config.Bind calls** (after the existing `EnableTacticalBattleOrchestrator` Bind ~line 247-253):

```csharp
EnableTacticalOrchestratorArmy = Config.Bind(
    "Tactical Orchestrator",
    "Enable Tactical Orchestrator Army",
    true,
    "Default ON. O1: instantiate the per-side ArmyOrchestrator at battle start, " +
    "pick a personality-keyed playbook, and let BattleMacroStrategyPatch read " +
    "ArmyOrchestrator.CurrentMacroAi instead of running the doctrine scorer. " +
    "Disable to fall back to scorer-driven macro behavior for regression triage.");
TacticalOrchestratorMinReplanSeconds = Config.Bind(
    "Tactical Orchestrator",
    "Min Replan Seconds",
    60,
    new ConfigDescription(
        "Minimum game seconds between army replan events. Triggers may detect " +
        "earlier; orchestrator rate-limits actual plan re-pick to avoid thrash.",
        new AcceptableValueRange<int>(10, 600)));
TacticalOrchestratorVerboseLogging = Config.Bind(
    "Tactical Orchestrator",
    "Verbose Logging",
    false,
    "Default OFF. When true, emit per-tick [TacticalCascade] and per-trigger " +
    "[TacticalReplan] lines instead of just first-fire and on-change markers.");
```

- [ ] **Step 3: Build to confirm compile.**

```bash
./build.sh 2>&1 | tail -20
```

Expected: 0 warnings / 0 errors.

- [ ] **Step 4: Commit.**

```
feat(orchestrator): add Tactical Orchestrator config flags (Army valve, MinReplanSeconds, VerboseLogging)
```

---

### Task 12: Rewire #44 BattleMacroStrategyPatch to read ArmyOrchestrator

**Files:**
- Modify: `src/WhiskeyRealism/Patches/BattleMacroStrategyPatch.cs`

When `EnableTacticalOrchestratorArmy = true` AND a coordinator-active side has an `ArmyOrchestrator` with `CurrentMacroAi >= 0`, the patch should write the orchestrator's macro instead of running its current scorer logic. When the flag is off, current scorer behavior is preserved verbatim (regression-triage fallback).

- [ ] **Step 1: Modify `Apply(AIBattle battle)`** in `BattleMacroStrategyPatch.cs`. Replace the body with:

```csharp
private static void Apply(AIBattle battle)
{
    int side = SafeIntField(battle, ref _sideOfAiField, "sideofai", -1);
    int vanillaMacro = SafeIntField(battle, ref _macroAiField, "macroai", -99);
    var bunits = SafeField<BattleUnits>(battle, ref _bunitsField, "bunits");
    var units = SafeList(battle, ref _unitsUsedField, "unitsused");
    if (side < 0 || bunits == null) return;

    int allianceId = ResolveAllianceId(bunits, side);
    if (TryApplyOrchestrator(battle, vanillaMacro, allianceId)) return;

    // Fallback: existing scorer path (preserved verbatim)
    var odds = BuildRuntimeOdds(bunits, side, units);
    var decision = TacticalDoctrineScorer.DecideMacro(new TacticalMacroDecisionInput(
        vanillaMacro,
        GameVars.aistrategy >= 0,
        SideInfoMacro(bunits, side) >= 0,
        vanillaMacro == 3 || EndBattleActive(bunits),
        CommanderAggression01(bunits, side),
        odds));

    if (decision.Kind != TacticalDoctrineDecisionKind.Apply) return;
    if (decision.MacroAi == vanillaMacro) return;
    if (decision.MacroAi < -1 || decision.MacroAi > 3) return;
    if (_macroAiField == null) return;

    _macroAiField.SetValue(battle, decision.MacroAi);
    LogDecision(side, vanillaMacro, decision, odds);
}

private static bool TryApplyOrchestrator(AIBattle battle, int vanillaMacro, int allianceId)
{
    if (Plugin.EnableTacticalOrchestratorArmy == null || !Plugin.EnableTacticalOrchestratorArmy.Value) return false;
    if (allianceId < 0 || allianceId >= 2) return false;
    var sideOrch = WhiskeyRealism.Tactical.Orchestrator.TacticalBattleCoordinator.GetSideOrchestrator(allianceId);
    if (sideOrch?.Army == null || !sideOrch.Army.HasPlan) return false;

    int macro = sideOrch.Army.CurrentMacroAi;
    if (macro < -1 || macro > 3) return true;        // orchestrator says skip via -1 → don't fall back
    if (macro == vanillaMacro) return true;            // no-op; orchestrator agrees
    if (_macroAiField == null) return true;

    _macroAiField.SetValue(battle, macro);
    OnceLog.Info("orch-macro-write:" + allianceId,
        "[TacticalMacroDecision] side=" + allianceId +
        " old=" + TacticalTelemetry.MacroName(vanillaMacro) +
        " orchestrator=" + TacticalTelemetry.MacroName(macro) +
        " plan=" + sideOrch.Army.CurrentPlan.PlanId +
        " phase=" + sideOrch.Army.CurrentPlan.Phase);
    return true;
}

private static int ResolveAllianceId(BattleUnits bunits, int side)
{
    try
    {
        if (bunits == null || bunits.alliance == null || side < 0 || side >= bunits.alliance.Length) return -1;
        return bunits.alliance[side];
    }
    catch { return -1; }
}
```

- [ ] **Step 2: Build to confirm.**

```bash
./build.sh 2>&1 | tail -20
```

Expected: 0 warnings / 0 errors.

- [ ] **Step 3: Run harness, no regressions.**

```bash
dotnet test tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | tail -5
```

- [ ] **Step 4: Commit.**

```
fix(patches): rewire #44 BattleMacroStrategyPatch to read ArmyOrchestrator when valve on
```

---

### Task 13: Demote #47 BattleCommanderIntentObserverPatch to telemetry-only

**Files:**
- Modify: `src/WhiskeyRealism/Patches/BattleCommanderIntentObserverPatch.cs`

Per umbrella spec §"Inventory" row #47: "Stops populating `TacticalReactionContext` (orchestrator does); keeps `[TacticalLocalReaction]` / `[TacticalReserveIntent]` markers until O7 cleanup."

- [ ] **Step 1: Read the current patch.** (Do this in the executing session — the demotion is small but the surface needs re-confirmation before edit.)

```bash
wc -l src/WhiskeyRealism/Patches/BattleCommanderIntentObserverPatch.cs
```

- [ ] **Step 2: Identify the `TacticalReactionContext` populate calls.** They're the lines that mutate the shared `TacticalReactionContext` instance (typically a `Set` or `Push` method on the static `TacticalReactionContext.Shared`).

- [ ] **Step 3: Wrap those mutating calls in a flag check that disables them when the orchestrator-army valve is on**:

```csharp
// Replace, e.g., TacticalReactionContext.Shared.Set(...) with:
if (Plugin.EnableTacticalOrchestratorArmy == null || !Plugin.EnableTacticalOrchestratorArmy.Value)
    TacticalReactionContext.Shared.Set(...);
```

Telemetry calls (`Plugin.Log.LogInfo("[TacticalLocalReaction] ...")` and `[TacticalReserveIntent]` lines) stay unconditional.

- [ ] **Step 4: Build, run harness.**

```bash
./build.sh 2>&1 | tail -10
dotnet test tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | tail -5
```

- [ ] **Step 5: Commit.**

```
chore(patches): demote #47 BattleCommanderIntentObserverPatch to telemetry-only when orchestrator-army on
```

---

### Task 14: Test csproj sweep

**Files:**
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`

Each prior task added Compile Include entries incrementally; this task confirms the full set is present and ordered consistently.

- [ ] **Step 1: Confirm every new file has an entry**:

```bash
for f in $(find src/WhiskeyRealism/Tactical/Orchestrator -name '*.cs' -newer src/WhiskeyRealism/Tactical/Orchestrator/EchelonOrchestrator.cs 2>/dev/null); do
  base=$(basename "$f")
  grep -q "Link=\"$base\"" tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj || echo "MISSING: $f"
done
```

Expected: empty output.

- [ ] **Step 2: Build and run full harness.**

```bash
./build.sh 2>&1 | tail -5
dotnet test tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | tail -5
```

Expected: 0 warnings, 0 errors, harness PASS count >= baseline + ~22 (4 plan + 4 playbook abstract + 4 catalog + 5 generic + 10 historical + 5 army + 8 replan = ~40 new tests, count may differ slightly).

- [ ] **Step 3: Commit (only if csproj actually changed).**

```
test(orchestrator): confirm Compile Include sweep for O1
```

---

### Task 15: Build, deploy, verify deployed DLL hash

- [ ] **Step 1: Build.**

```bash
./build.sh 2>&1 | tail -15
```

Expected: `0 Warning(s) 0 Error(s)`.

- [ ] **Step 2: Deploy.**

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
```

If the `cp` fails with `Invalid argument`, GTCW is running and holding the DLL. Close the game and retry.

- [ ] **Step 3: Hash verify.**

```bash
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: identical SHA-256, identical size, mtimes seconds apart. Record the new hash for the handoff update.

- [ ] **Step 4: No commit yet — smoke evidence comes next.**

---

### Task 16: In-game smoke test

Two scenarios. Record observations before claiming O1 done.

- [ ] **Smoke A — AI-vs-AI battle:**
  - Start career, advance to a battle where the player is CIC of (say) Confederate side and is observing only.
  - Let the battle run ~2 minutes.
  - Quit to desktop after the battle ends.
  - Expected log markers in `BepInEx/LogOutput.log`:
    - `[once:orch-bootstrap]` (from O0)
    - `[TacticalCommanderRoster] alliance=0 total=…` (the side with army CO)
    - `[TacticalPlan] side=0 plan=<plan_id> phase=Probe mainEffort=…`
    - `[TacticalMacroDecision] side=… orchestrator=… plan=…` on at least one tick where vanilla wanted a different macro than the orchestrator
    - `[once:orch-teardown]` after battle ends
  - No `[TacticalOrchestrator] ... skipped` or `Tactical … failed` warning lines.

- [ ] **Smoke B — AI-vs-player battle (player at CIC):**
  - Same scenario shape but the player commands their side (so player CIC suppresses one orchestrator).
  - Expected: only one `[TacticalPlan]` line (for the AI-side army); player side suppressed (line `bootstrap sidesActive=1 sidesSuppressed=1`).
  - Battle plays normally; no exceptions.

- [ ] **If either smoke fails:** STOP. Capture the failure into `docs/handoff.md`, do not advance to a final commit.

- [ ] **If both smokes pass:** record the deployed DLL hash + smoke evidence in the next handoff update task.

---

### Task 17: Update handoff + MEMORY.md, archive O1 plan

- [ ] **Step 1:** Update `docs/handoff.md` with:
  - O1 ship status (date, deployed DLL hash, smoke status)
  - New harness PASS count
  - Active workstream now points to O2 (Intent inference + adversarial loop)

- [ ] **Step 2:** Update `MEMORY.md` "Active workstream" line.

- [ ] **Step 3:** Move this plan to archive.

```bash
git mv docs/superpowers/plans/archive/2026-05-08-tactical-orchestrator-o1-army.md docs/superpowers/plans/archive/
```

- [ ] **Step 4:** Append an entry to `docs/superpowers/plans/archive/README.md`.

- [ ] **Step 5:** Commit.

```
docs(handoff): record O1 ship + archive O1 plan + advance handoff to O2
```

---

## Cross-cutting invariants verified at every commit

- Console harness PASS count never decreases. Record it after Task 14 and gate every subsequent task on `>= recorded`.
- `./build.sh` produces 0 warnings / 0 errors before any commit.
- `dist/WhiskeyRealism.dll` SHA-256 == BepInEx plugin SHA-256 before any smoke claim. Don't ask the user to test until they match.
- W&L player-control invariant: `BattleMacroStrategyPatch.TryApplyOrchestrator` does NOT bypass `TacticalGateHelpers.IsPlayerControlled` — player units are unaffected because `macroai` is a battle-level field, not a per-unit field. (Per-unit gates are O3+ territory.)
- Read-only-mod-state invariant: `BattleMacroStrategyPatch` only WRITES vanilla `macroai` (a battle-level field whose write surface vanilla itself uses); orchestrator state is read by patches but mutated only inside the runtime partial of `TacticalBattleCoordinator`.
- Master flag fallback: `EnableTacticalBattleOrchestrator = false` short-circuits orchestrator instantiation in O0; #44 falls back to its scorer path. This must remain true after O1.

## Deferred to later phases

- Corps/Division/Brigade echelons (O3/O4/O5).
- Adversarial intent inference (`TacticalIntentModel`) — O2.
- `OpposingCommanderHint` is wired into `PlaybookContext` but always passed `0f` in O1; populated in O2.
- Replan loop in the runtime tick: Task 8 implements `CheckReplanTriggers` evaluation, but the runtime caller that feeds inputs and triggers `Replan` lives in O2 alongside the intent model. O1 ships with initial-pick-only behavior plus a phase-clock advance via `AdvancePhase` from a simple time-based heartbeat (Task 7's `AdvancePhase` is exposed but no runtime call site is wired in O1; the replan loop ships in O2).
- Personality coverage telemetry beyond what O0 already emits.

---

## Self-review checklist

After implementing through Task 17, before claiming O1 done:

1. **Spec coverage:** Re-read the umbrella spec O1 row in §"Phasing" and §"Architecture". Confirm every named element ships:
   - [x] `ArmyOrchestrator` — Task 7
   - [x] `TacticalPlaybookCatalog` with 14 seeded playbooks — Tasks 3, 4, 5, 6
   - [x] Playbook selection — Task 3
   - [x] #44 macro stance rewired to read army — Task 12
   - [x] Replan trigger logic — Task 8
   - [x] #47 demoted to telemetry-only — Task 13
   - [x] `Enable Tactical Orchestrator Army = true` (after smoke) — Task 11

2. **Smoke gate:** Both AI-vs-AI and AI-vs-player smokes from §"Phasing" must pass.

3. **Harness coverage:** ~40 new tests, no regressions.

4. **Type-name consistency:** every type referenced across tasks (`BattlePlanId`, `BattlePhase`, `TacticalBattlePlan`, `ArmyIntent`, `TacticalPlaybook`, `PersonalityFit`, `TerrainPreference`, `OddsRange`, `PlaybookContext`, `TacticalPlaybookCatalog`, `ArmyOrchestrator`, `ArmyEvidence`, `ReplanTriggerInput`, `ReplanTrigger`, `ArmyReplanTriggers`, `BuiltInPlaybooks`) should be defined exactly once and used consistently.

5. **No placeholders.** This plan should contain no "TBD"/"TODO"/"similar to TaskN" — re-grep before declaring done.

```bash
grep -nE "TODO|TBD|similar to Task|implement later" docs/superpowers/plans/archive/2026-05-08-tactical-orchestrator-o1-army.md
```

Expected: empty output.
