# Tactical Orchestrator O0 Scaffold Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the first phase of the tactical battle orchestrator: scaffold-only — coordinator singleton, abstract echelon orchestrator base, per-side root, commander roster, battle lifecycle detection wired through `TacticalObserverPatch` (#35), and telemetry markers proving the coordinator instantiates and tears down cleanly. No decision authority. No vanilla behavior changes. No new patch surfaces.

**Baseline (verified 2026-05-08 post-merge):** main is on commit `82a1fe7 docs: record merged tactical closeout`. Build clean (0 warnings / 0 errors). Console harness `517 PASS / 0 FAIL`. Deployed DLL SHA-256 `a5a6e1fd099d11d2ff5dc6fd460d91e4e98a26a6f405df9d4b5dbfc808ed0d38` (663040 bytes). Patches #54 (W&L operation null guard, default-on), #55 (HQ auto-link guard, default-off), #56 (reserve order-delay guard, default-off), and #57 (reserve-list bias, default-off) shipped post-spec — all orthogonal NRE/HQ/reserve bug-fix guards, none on Slice B doctrine surfaces, none affected by the orchestrator scope.

**Architecture:** Add a small `WhiskeyRealism.Tactical.Orchestrator` namespace with the coordinator + abstract base + per-side root + commander roster. Extend the existing `TacticalObserverPatch` (#35) to detect battle start/end from `Regiment.inbattle` transitions and call coordinator lifecycle methods. The master config flag `Enable Tactical Battle Orchestrator` (default-on per umbrella spec locked Q6) gates everything; when off, the observer patch is identical to today.

**Tech Stack:** BepInEx 5.4.x x64, HarmonyX, C# netstandard2.1, Unity 2021 Mono, console harness in `tests/WhiskeyRealism.Tests`, vanilla anchors from `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` (re-verified 2026-05-08).

---

## Pre-Flight

The merge that was open at brainstorm time has been resolved (post-merge state above). Before any task runs, the execution agent must:

1. Run `git status`. Expected: clean working tree (only the spec + this plan as untracked files until they are committed). If `unmerged paths` appears, stop and surface to the user.
2. Run `./build.sh`. Expected: 0 warnings, 0 errors.
3. Run `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` and confirm the final summary line is `517 PASS / 0 FAIL` (or higher — never lower than 517).

If any of those baseline checks fails, stop and surface to the user before continuing.

Then follow the worktree convention:

```bash
cd ~/Projects/whiskey-realism-mod
git worktree add ../whiskey-realism-mod-orch-o0 -b feature/tactical-orchestrator-o0
cd ../whiskey-realism-mod-orch-o0
ln -s ../whiskey-realism-mod/refs refs    # refs/ is gitignored; symlink in
./build.sh                                 # confirm clean baseline before edits
```

## Source Inputs

Read these before implementation:

- `AGENTS.md` (root)
- `docs/handoff.md`
- `docs/patch-catalog.md`
- `docs/superpowers/AGENTS.md`
- `docs/superpowers/specs/archive/2026-05-08-tactical-battle-orchestrator-design.md` ← THIS PLAN'S SPEC
- `docs/superpowers/specs/archive/2026-05-05-tactical-brain-design.md` (Slice B umbrella)
- `src/WhiskeyRealism/Patches/AGENTS.md`
- `src/WhiskeyRealism/Strategic/AGENTS.md` (singleton pattern reference: `StrategicCoordinator.cs`)
- `tests/WhiskeyRealism.Tests/AGENTS.md`
- `~/.claude/projects/-home-onebodyamerica-Projects-whiskey-realism-mod/memory/MEMORY.md` (BepInEx Config.Bind brackets gotcha; section names cannot contain `[` or `]`)

## Non-Negotiable Boundaries

O0 is **scaffold + telemetry only.**

Do not:
- write `macroai`, `ai_stance`, `ai_stanceordered`, charge orders, reserve list mutations, artillery prio, fallback state, retreat state, movement orders, or `SetWaypoint`;
- mutate `DLC_WL.givenorder`;
- modify any of the existing default-off behavior patches (#41 / #42 / #44 / #45 / #48 / B7 / B8) — those rewires happen in later phases (O1-O5);
- add `PlayerSubordinateOrderPatch` — that ships in O6;
- delete `TacticalCommanderIntent.cs` or `TacticalPlaybookLedger.cs` — that happens in O7 cleanup;
- write to the campaign sidecar `whiskeyrealism.json`;
- add per-echelon valves (`Enable Tactical Orchestrator Army`, etc.) — those ship with their corresponding O1-O5 phase plans.

O0 may:
- add the master `Enable Tactical Battle Orchestrator` config flag (default true);
- add new files under `src/WhiskeyRealism/Tactical/Orchestrator/`;
- rename `src/WhiskeyRealism/Tactical/TacticalBattlePlan.cs` → `TacticalDoctrineDecisionContracts.cs` (the file holds decision input/output structs, not a plan; rename frees the name for the actual `TacticalBattlePlan` entity in O1);
- extend `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs` to detect `inbattle` transitions and call coordinator lifecycle methods;
- add new telemetry markers (`[once:orch-coordinator]`, `[once:orch-bootstrap]`, `[once:orch-teardown]`, `[TacticalCommanderRoster]`).

## Verified Vanilla Anchors (re-verify before code edits)

```bash
rg -n "regiment.inbattle = |\.inbattle = true|\.inbattle = false" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs | head -10
```

Expected hits as of 2026-05-08:

- Line 22781 / 22889 — `regimentFromArguments.inbattle = true;` in `BattleUnits` battle init paths
- Line 80708 / 80791 / 80792 / 94062 — battle engagement triggers (`callingunit.inbattle = true; foundenemy.inbattle = true;`)
- Line 21535 / 21995 / 81086 — battle teardown paths
- Line 114950 — `inbattleuntil = 0f;` (related but not the lifecycle flag)

```bash
rg -n "public class HistoricalFigureRegistry|public class FactionProfiles|public class EraStageManager" /home/onebodyamerica/Projects/whiskey-realism-mod/src/WhiskeyRealism/Strategic
```

Expected: each class found exactly once. These are O0's commander-personality data sources.

```bash
rg -n "public static (class )?StrategicCoordinator" /home/onebodyamerica/Projects/whiskey-realism-mod/src/WhiskeyRealism/Strategic
```

Expected: confirm singleton pattern shape (this O0 coordinator follows the same pattern).

If any expected anchor has drifted, stop and update this plan before continuing.

## File Structure

| File | Action | Responsibility |
|---|---|---|
| `src/WhiskeyRealism/Plugin.cs` | modify | Add `EnableTacticalBattleOrchestrator` ConfigEntry<bool> default true. |
| `src/WhiskeyRealism/Tactical/TacticalBattlePlan.cs` | rename → `TacticalDoctrineDecisionContracts.cs` | Holds existing decision input/output structs. Frees the name `TacticalBattlePlan` for the O1 plan entity. |
| `src/WhiskeyRealism/Tactical/Orchestrator/EchelonOrchestrator.cs` | create | Abstract base for echelon-specific orchestrators. Owns echelon kind, parent/children, virtual `Tick()` and `PropagateIntent()`. |
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalCommanderRoster.cs` | create | Discovers all battle commanders per side; resolves to `HistoricalFigureRegistry` where possible; falls back to era × faction defaults; logs `[TacticalCommanderUnknown]` for gaps. |
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleOrchestrator.cs` | create | Per-side root container. Holds alliance id, commander roster snapshot, empty children list (Army/Corps/etc. ship in later phases). `Tick()` iterates children — empty in O0. |
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinator.cs` | create | Static singleton (matches `StrategicCoordinator` pattern). `OnBattleStart()` / `OnBattleEnd()` / `Tick()` lifecycle methods. Suppresses player's-side orchestrator when player is CIC. Fires `[once:orch-coordinator]` / `[once:orch-bootstrap]` / `[once:orch-teardown]`. |
| `src/WhiskeyRealism/Tactical/TacticalTelemetry.cs` | modify | Add log helpers for orchestrator events (`LogOrchestratorBootstrap`, `LogOrchestratorTeardown`, `LogCommanderRosterSummary`, `LogCommanderUnknown`). |
| `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs` | modify | Track per-side `inbattle` counts across ticks; on transition no-units → some-units call `TacticalBattleCoordinator.OnBattleStart()`; on sustained-2-ticks transition some-units → no-units call `OnBattleEnd()`; per tick call `TacticalBattleCoordinator.Tick()` when active. All calls wrapped in try/catch with `Plugin.Log.LogWarning(...)` per project convention (never throw from a patch). |
| `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` | modify | Add explicit `<Compile Include>` entries for the 4 new orchestrator files (per CLAUDE.md test-project gotcha). |
| `tests/WhiskeyRealism.Tests/Tactical/Orchestrator/EchelonOrchestratorTests.cs` | create | Tests: instantiation, parent/children wiring, virtual `Tick()` dispatch. |
| `tests/WhiskeyRealism.Tests/Tactical/Orchestrator/TacticalCommanderRosterTests.cs` | create | Tests: roster discovery from synthesized commander list, HistoricalFigureRegistry match, era × faction fallback, unknown-commander logging. |
| `tests/WhiskeyRealism.Tests/Tactical/Orchestrator/TacticalBattleOrchestratorTests.cs` | create | Tests: per-side instantiation, W&L CIC suppression gating, empty-children tick. |
| `tests/WhiskeyRealism.Tests/Tactical/Orchestrator/TacticalBattleCoordinatorTests.cs` | create | Tests: singleton pattern, OnBattleStart/OnBattleEnd lifecycle invariants, double-start no-op, teardown clears state, helpRequests cleared. |
| `tests/WhiskeyRealism.Tests/Tactical/Orchestrator/TacticalObserverLifecycleDetectionTests.cs` | create | Tests: per-side `inbattle` transition detection (extracted to a pure helper so harness can run without Unity). |

After O0 ships, the test project Compile entries must also be added for any later-phase files; that is each later phase's responsibility.

---

## Tasks

### Task 1: Add master config flag

**Files:**
- Modify: `src/WhiskeyRealism/Plugin.cs`

- [ ] **Step 1: Open `src/WhiskeyRealism/Plugin.cs` and find the existing `Config.Bind` block.** Identify where existing tactical config flags are bound (e.g., `EnableTacticalObserver`, `EnableTacticalChargeGuard`).

- [ ] **Step 2: Add the master config field declaration.**

```csharp
public static ConfigEntry<bool> EnableTacticalBattleOrchestrator;
```

Place it adjacent to the other `EnableTactical*` declarations.

- [ ] **Step 3: Add the Config.Bind call inside `Awake()`.**

```csharp
EnableTacticalBattleOrchestrator = Config.Bind(
    "Tactical Orchestrator",
    "Enable Tactical Battle Orchestrator",
    true,
    "Master switch for the multi-echelon tactical battle orchestrator. " +
    "Default on per orchestrator umbrella spec. Disable to revert to vanilla " +
    "+ existing default-off Slice B scorer paths."
);
```

**CRITICAL:** the section name `"Tactical Orchestrator"` must contain no brackets — see MEMORY.md `bepinex_gotchas.md`. A section name like `"Tactical [Orchestrator]"` causes a silent Awake abort with no log line.

- [ ] **Step 4: Build and verify.**

```bash
./build.sh
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 5: Commit.**

```bash
git add src/WhiskeyRealism/Plugin.cs
git commit -m "feat(orchestrator): add master config flag (default-on)

Master switch for the tactical battle orchestrator umbrella. Default true
per umbrella spec locked Q6. No behavior change yet — coordinator and
related types ship in subsequent O0 tasks."
```

---

### Task 2: Rename TacticalBattlePlan.cs → TacticalDoctrineDecisionContracts.cs

The existing `Tactical/TacticalBattlePlan.cs` is misnamed — it holds decision input/output structs (`TacticalDoctrineDecisionKind`, `TacticalMacroDecisionInput`, `TacticalMacroDecision`, `TacticalGroupStanceDecisionInput`, `TacticalGroupStanceDecision`), not a plan entity. Rename frees the name `TacticalBattlePlan` for the actual O1 plan entity.

**Files:**
- Rename: `src/WhiskeyRealism/Tactical/TacticalBattlePlan.cs` → `src/WhiskeyRealism/Tactical/TacticalDoctrineDecisionContracts.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` (update Compile entry)

- [ ] **Step 1: Verify no consumer references the filename.** The struct names don't change; only the file moves. Confirm nothing depends on the file path.

```bash
rg -n "TacticalBattlePlan\.cs" /home/onebodyamerica/Projects/whiskey-realism-mod
```

Expected: only matches in csproj/build files (filenames), not in C# source. If any C# source references the path, stop and re-plan.

- [ ] **Step 2: Rename the file.**

```bash
git mv src/WhiskeyRealism/Tactical/TacticalBattlePlan.cs src/WhiskeyRealism/Tactical/TacticalDoctrineDecisionContracts.cs
```

- [ ] **Step 3: Update test project Compile entry.** Open `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`. Find the entry `<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalBattlePlan.cs" Link="TacticalBattlePlan.cs" />` and replace with:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalDoctrineDecisionContracts.cs" Link="TacticalDoctrineDecisionContracts.cs" />
```

- [ ] **Step 4: Build and run tests.**

```bash
./build.sh && dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: build clean (0 warnings, 0 errors); test count unchanged from baseline (`517 PASS / 0 FAIL` per current handoff).

- [ ] **Step 5: Commit.**

```bash
git add -A
git commit -m "refactor(tactical): rename TacticalBattlePlan.cs to TacticalDoctrineDecisionContracts.cs

The file holds decision input/output structs, not a plan entity. Rename
frees the name TacticalBattlePlan for the actual O1 plan entity. Struct
names unchanged; only the filename and test-project Compile entry move."
```

---

### Task 3: Add EchelonOrchestrator abstract base + tests

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/EchelonOrchestrator.cs`
- Create: `tests/WhiskeyRealism.Tests/Tactical/Orchestrator/EchelonOrchestratorTests.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs` (register new tests in harness runner)

- [ ] **Step 1: Create the directory.**

```bash
mkdir -p src/WhiskeyRealism/Tactical/Orchestrator tests/WhiskeyRealism.Tests/Tactical/Orchestrator
```

- [ ] **Step 2: Write the failing test first (TDD).**

Create `tests/WhiskeyRealism.Tests/Tactical/Orchestrator/EchelonOrchestratorTests.cs`:

```csharp
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tests.Tactical.Orchestrator
{
    public static class EchelonOrchestratorTests
    {
        public static IEnumerable<TestCase> All()
        {
            yield return new TestCase(
                "EchelonOrchestratorEmptyTickIsNoOp",
                () => {
                    var stub = new StubEchelonOrchestrator(EchelonKind.Army, allianceId: 0);
                    stub.Tick();
                    Assert.Equal(1, stub.TickCount);
                });

            yield return new TestCase(
                "EchelonOrchestratorPropagateIntentDispatchesToChildren",
                () => {
                    var parent = new StubEchelonOrchestrator(EchelonKind.Army, allianceId: 0);
                    var child = new StubEchelonOrchestrator(EchelonKind.Corps, allianceId: 0);
                    parent.AddChild(child);
                    parent.PropagateIntent();
                    Assert.Equal(1, child.PropagateCount);
                });

            yield return new TestCase(
                "EchelonOrchestratorParentChildLinkBidirectional",
                () => {
                    var parent = new StubEchelonOrchestrator(EchelonKind.Army, allianceId: 0);
                    var child = new StubEchelonOrchestrator(EchelonKind.Corps, allianceId: 0);
                    parent.AddChild(child);
                    Assert.Same(parent, child.Parent);
                    Assert.Equal(1, parent.Children.Count);
                });
        }

        private sealed class StubEchelonOrchestrator : EchelonOrchestrator
        {
            public StubEchelonOrchestrator(EchelonKind kind, int allianceId) : base(kind, allianceId) { }
            public int TickCount { get; private set; }
            public int PropagateCount { get; private set; }
            public override void Tick() { TickCount++; base.Tick(); }
            public override void PropagateIntent() { PropagateCount++; base.PropagateIntent(); }
        }
    }
}
```

- [ ] **Step 3: Run tests to confirm they fail compilation.**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile error — `EchelonOrchestrator` and `EchelonKind` do not exist. This is the failing-test step.

- [ ] **Step 4: Implement `EchelonOrchestrator.cs`.**

Create `src/WhiskeyRealism/Tactical/Orchestrator/EchelonOrchestrator.cs`:

```csharp
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    public enum EchelonKind
    {
        Unknown = 0,
        Army = 1,
        Corps = 2,
        Division = 3,
        Brigade = 4,
    }

    public abstract class EchelonOrchestrator
    {
        protected EchelonOrchestrator(EchelonKind kind, int allianceId)
        {
            Kind = kind;
            AllianceId = allianceId;
            Children = new List<EchelonOrchestrator>();
        }

        public EchelonKind Kind { get; }
        public int AllianceId { get; }
        public EchelonOrchestrator Parent { get; private set; }
        public List<EchelonOrchestrator> Children { get; }

        public void AddChild(EchelonOrchestrator child)
        {
            if (child == null) return;
            child.Parent = this;
            Children.Add(child);
        }

        public virtual void Tick()
        {
            for (int i = 0; i < Children.Count; i++)
            {
                Children[i]?.Tick();
            }
        }

        public virtual void PropagateIntent()
        {
            for (int i = 0; i < Children.Count; i++)
            {
                Children[i]?.PropagateIntent();
            }
        }
    }
}
```

- [ ] **Step 5: Update test csproj + Program.cs and verify.**

In `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, add inside the existing `<ItemGroup>` of Compile entries:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\EchelonOrchestrator.cs" Link="Orchestrator\EchelonOrchestrator.cs" />
<Compile Include="Tactical\Orchestrator\EchelonOrchestratorTests.cs" />
```

In `tests/WhiskeyRealism.Tests/Program.cs`, find where existing tactical test suites are registered (look for `TacticalSectorLedgerTests.All()` or similar) and add:

```csharp
foreach (var t in WhiskeyRealism.Tests.Tactical.Orchestrator.EchelonOrchestratorTests.All()) yield return t;
```

Run:

```bash
./build.sh && dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: build clean; tests pass; test count went from `517 PASS / 0 FAIL` baseline to `520 PASS / 0 FAIL`.

- [ ] **Step 6: Commit.**

```bash
git add -A
git commit -m "feat(orchestrator): add EchelonOrchestrator abstract base

Abstract base for echelon-specific orchestrators (Army/Corps/Division/
Brigade ship in O1+). Owns echelon kind, alliance, parent/children
wiring, and virtual Tick/PropagateIntent dispatch. No behavior surface
yet. Adds 3 harness tests."
```

---

### Task 4: Add TacticalCommanderRoster + tests

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalCommanderRoster.cs`
- Create: `tests/WhiskeyRealism.Tests/Tactical/Orchestrator/TacticalCommanderRosterTests.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`
- Modify: `src/WhiskeyRealism/Tactical/TacticalTelemetry.cs` (add `LogCommanderRosterSummary`, `LogCommanderUnknown` helpers)

- [ ] **Step 1: Read existing telemetry helpers to match the project's logging style.**

```bash
sed -n '1,80p' src/WhiskeyRealism/Tactical/TacticalTelemetry.cs
```

Note the existing one-line marker pattern (e.g., `[TacticalCommand] ...`, `[TacticalSector] ...`, `[TacticalOdds] ...`). New markers must follow the same shape.

- [ ] **Step 2: Read `HistoricalFigureRegistry`, `FactionProfiles`, `EraStageManager` to understand the personality fallback chain.**

```bash
rg -n "public class HistoricalFigureRegistry|public.*GetByName|public.*PersonalityVector Default" src/WhiskeyRealism/Strategic
```

Confirm the API for: lookup by officer name, era × faction default vector. The roster will use these in fallback order: registry name match → era × faction default + rank-tier bias.

- [ ] **Step 3: Write the failing test first.**

Create `tests/WhiskeyRealism.Tests/Tactical/Orchestrator/TacticalCommanderRosterTests.cs`:

```csharp
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tests.Tactical.Orchestrator
{
    public static class TacticalCommanderRosterTests
    {
        public static IEnumerable<TestCase> All()
        {
            yield return new TestCase(
                "RosterResolvesNamedHistoricalCommander",
                () => {
                    var roster = TacticalCommanderRoster.BuildFromSynthetic(
                        new[] {
                            new SyntheticCommanderInput("Robert E. Lee", EchelonKind.Army, allianceId: 1, year: 1862)
                        });
                    var entry = roster.GetByName("Robert E. Lee");
                    Assert.NotNull(entry);
                    Assert.True(entry.MatchedHistoricalRegistry);
                    Assert.Equal(EchelonKind.Army, entry.Echelon);
                });

            yield return new TestCase(
                "RosterFallsBackToEraFactionDefaultsForUnknown",
                () => {
                    var roster = TacticalCommanderRoster.BuildFromSynthetic(
                        new[] {
                            new SyntheticCommanderInput("Some Obscure Brigadier", EchelonKind.Brigade, allianceId: 0, year: 1864)
                        });
                    var entry = roster.GetByName("Some Obscure Brigadier");
                    Assert.NotNull(entry);
                    Assert.False(entry.MatchedHistoricalRegistry);
                    Assert.NotNull(entry.PersonalityVector);
                });

            yield return new TestCase(
                "RosterPartitionsBySide",
                () => {
                    var roster = TacticalCommanderRoster.BuildFromSynthetic(
                        new[] {
                            new SyntheticCommanderInput("Lee", EchelonKind.Army, allianceId: 1, year: 1862),
                            new SyntheticCommanderInput("Grant", EchelonKind.Army, allianceId: 0, year: 1864)
                        });
                    Assert.Equal(1, roster.GetSide(0).Count);
                    Assert.Equal(1, roster.GetSide(1).Count);
                });

            yield return new TestCase(
                "RosterRankTierBiasFallsTowardMethodicalForCorps",
                () => {
                    var roster = TacticalCommanderRoster.BuildFromSynthetic(
                        new[] {
                            new SyntheticCommanderInput("Unknown Corps CO", EchelonKind.Corps, allianceId: 0, year: 1863)
                        });
                    var entry = roster.GetByName("Unknown Corps CO");
                    Assert.True(entry.PersonalityVector.Methodical >= 0.0f);
                });
        }
    }
}
```

- [ ] **Step 4: Run tests to confirm compile failure.**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile errors — `TacticalCommanderRoster`, `SyntheticCommanderInput` do not exist.

- [ ] **Step 5: Implement `TacticalCommanderRoster.cs`.**

Create `src/WhiskeyRealism/Tactical/Orchestrator/TacticalCommanderRoster.cs`:

```csharp
using System.Collections.Generic;
using WhiskeyRealism.Strategic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    public sealed class CommanderRosterEntry
    {
        public string Name { get; set; }
        public EchelonKind Echelon { get; set; }
        public int AllianceId { get; set; }
        public bool MatchedHistoricalRegistry { get; set; }
        public PersonalityVector PersonalityVector { get; set; }
    }

    public readonly struct SyntheticCommanderInput
    {
        public SyntheticCommanderInput(string name, EchelonKind echelon, int allianceId, int year)
        {
            Name = name; Echelon = echelon; AllianceId = allianceId; Year = year;
        }
        public string Name { get; }
        public EchelonKind Echelon { get; }
        public int AllianceId { get; }
        public int Year { get; }
    }

    public sealed class TacticalCommanderRoster
    {
        private readonly Dictionary<string, CommanderRosterEntry> byName = new Dictionary<string, CommanderRosterEntry>();
        private readonly Dictionary<int, List<CommanderRosterEntry>> bySide = new Dictionary<int, List<CommanderRosterEntry>>();

        public static TacticalCommanderRoster BuildFromSynthetic(IEnumerable<SyntheticCommanderInput> inputs)
        {
            var roster = new TacticalCommanderRoster();
            foreach (var input in inputs)
            {
                roster.Add(input);
            }
            return roster;
        }

        public void Add(SyntheticCommanderInput input)
        {
            if (string.IsNullOrEmpty(input.Name)) return;

            var entry = new CommanderRosterEntry
            {
                Name = input.Name,
                Echelon = input.Echelon,
                AllianceId = input.AllianceId,
            };

            // Try HistoricalFigureRegistry match first.
            var historical = HistoricalFigureRegistry.GetByName(input.Name);
            if (historical != null)
            {
                entry.MatchedHistoricalRegistry = true;
                entry.PersonalityVector = historical.PersonalityVector;
            }
            else
            {
                entry.MatchedHistoricalRegistry = false;
                entry.PersonalityVector = ResolveFallbackVector(input.AllianceId, input.Year, input.Echelon);
            }

            byName[input.Name] = entry;
            if (!bySide.TryGetValue(input.AllianceId, out var sideList))
            {
                sideList = new List<CommanderRosterEntry>();
                bySide[input.AllianceId] = sideList;
            }
            sideList.Add(entry);
        }

        public CommanderRosterEntry GetByName(string name)
        {
            return name != null && byName.TryGetValue(name, out var entry) ? entry : null;
        }

        public IReadOnlyList<CommanderRosterEntry> GetSide(int allianceId)
        {
            return bySide.TryGetValue(allianceId, out var list)
                ? (IReadOnlyList<CommanderRosterEntry>)list
                : new List<CommanderRosterEntry>();
        }

        public int Count => byName.Count;
        public int UnknownCount {
            get {
                int n = 0;
                foreach (var kv in byName) if (!kv.Value.MatchedHistoricalRegistry) n++;
                return n;
            }
        }

        private static PersonalityVector ResolveFallbackVector(int allianceId, int year, EchelonKind echelon)
        {
            // Era × faction defaults from existing Slice A.
            var basis = FactionProfiles.GetDefaultPersonality(allianceId, year);

            // Rank-tier bias (additive, then clamped — same convention as Slice A locked design choice 3).
            switch (echelon)
            {
                case EchelonKind.Corps:
                    basis = basis.AddBias(methodical: 0.10f);
                    break;
                case EchelonKind.Brigade:
                    basis = basis.AddBias(aggression: 0.05f);
                    break;
            }
            return basis.Clamp();
        }
    }
}
```

**NOTE:** This implementation references `HistoricalFigureRegistry.GetByName(...)`, `FactionProfiles.GetDefaultPersonality(...)`, `PersonalityVector.AddBias(...)`, and `PersonalityVector.Clamp()`. Before writing this file, verify these APIs exist in the current Slice A code:

```bash
rg -n "public static.*GetByName|public static.*GetDefaultPersonality|public.*AddBias|public.*Clamp" src/WhiskeyRealism/Strategic
```

If any of these APIs is missing, **STOP**. Either add the missing helper to the appropriate Strategic file as a small, focused addition (and add a regression test for it), or pick the closest existing API and adapt the roster code. Do not stub-out missing APIs.

- [ ] **Step 6: Update test csproj + Program.cs and verify.**

In `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, add:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\TacticalCommanderRoster.cs" Link="Orchestrator\TacticalCommanderRoster.cs" />
<Compile Include="Tactical\Orchestrator\TacticalCommanderRosterTests.cs" />
```

In `tests/WhiskeyRealism.Tests/Program.cs`, add:

```csharp
foreach (var t in WhiskeyRealism.Tests.Tactical.Orchestrator.TacticalCommanderRosterTests.All()) yield return t;
```

Run:

```bash
./build.sh && dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: build clean; tests pass at `524 PASS / 0 FAIL` (was 520 from Task 3; 4 new tests).

- [ ] **Step 7: Commit.**

```bash
git add -A
git commit -m "feat(orchestrator): add TacticalCommanderRoster

Discovers battle commanders per side; resolves personality via
HistoricalFigureRegistry name match, with era × faction + rank-tier
bias fallback for unknowns. Synthetic-input builder enables harness
testing without Unity. Adds 4 tests."
```

---

### Task 5: Add TacticalBattleOrchestrator (per-side root) + tests

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleOrchestrator.cs`
- Create: `tests/WhiskeyRealism.Tests/Tactical/Orchestrator/TacticalBattleOrchestratorTests.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Write the failing test first.**

Create `tests/WhiskeyRealism.Tests/Tactical/Orchestrator/TacticalBattleOrchestratorTests.cs`:

```csharp
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tests.Tactical.Orchestrator
{
    public static class TacticalBattleOrchestratorTests
    {
        public static IEnumerable<TestCase> All()
        {
            yield return new TestCase(
                "PerSideOrchestratorOwnsAllianceAndRoster",
                () => {
                    var roster = TacticalCommanderRoster.BuildFromSynthetic(
                        new[] {
                            new SyntheticCommanderInput("Lee", EchelonKind.Army, allianceId: 1, year: 1862)
                        });
                    var orch = new TacticalBattleOrchestrator(allianceId: 1, roster: roster);
                    Assert.Equal(1, orch.AllianceId);
                    Assert.Same(roster, orch.Roster);
                });

            yield return new TestCase(
                "PerSideOrchestratorEmptyChildrenInO0",
                () => {
                    var orch = new TacticalBattleOrchestrator(allianceId: 0, roster: TacticalCommanderRoster.BuildFromSynthetic(System.Array.Empty<SyntheticCommanderInput>()));
                    Assert.Equal(0, orch.Echelons.Count);
                });

            yield return new TestCase(
                "PerSideOrchestratorEmptyTickIsNoOp",
                () => {
                    var orch = new TacticalBattleOrchestrator(allianceId: 0, roster: TacticalCommanderRoster.BuildFromSynthetic(System.Array.Empty<SyntheticCommanderInput>()));
                    orch.Tick();
                    Assert.Equal(1, orch.TickCount);
                });
        }
    }
}
```

- [ ] **Step 2: Run tests to confirm compile failure.**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile error — `TacticalBattleOrchestrator` does not exist.

- [ ] **Step 3: Implement `TacticalBattleOrchestrator.cs`.**

Create `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleOrchestrator.cs`:

```csharp
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

        public void Tick()
        {
            TickCount++;
            for (int i = 0; i < Echelons.Count; i++)
            {
                Echelons[i]?.Tick();
            }
        }

        public void PropagateIntent()
        {
            for (int i = 0; i < Echelons.Count; i++)
            {
                Echelons[i]?.PropagateIntent();
            }
        }
    }
}
```

- [ ] **Step 4: Update test csproj + Program.cs and verify.**

In `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, add:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\TacticalBattleOrchestrator.cs" Link="Orchestrator\TacticalBattleOrchestrator.cs" />
<Compile Include="Tactical\Orchestrator\TacticalBattleOrchestratorTests.cs" />
```

In `tests/WhiskeyRealism.Tests/Program.cs`, add:

```csharp
foreach (var t in WhiskeyRealism.Tests.Tactical.Orchestrator.TacticalBattleOrchestratorTests.All()) yield return t;
```

Run:

```bash
./build.sh && dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: build clean; tests pass at `527 PASS / 0 FAIL` (was 524 from Task 4; 3 new tests).

- [ ] **Step 5: Commit.**

```bash
git add -A
git commit -m "feat(orchestrator): add TacticalBattleOrchestrator per-side root

Per-side container holding alliance id, commander roster snapshot, and
echelon children list. Empty in O0 (Army/Corps/etc. concretes ship in
O1+). Tick cascades to children. Adds 3 tests."
```

---

### Task 6: Add TacticalBattleCoordinator singleton + lifecycle methods + telemetry helpers

**Files:**
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinator.cs`
- Create: `tests/WhiskeyRealism.Tests/Tactical/Orchestrator/TacticalBattleCoordinatorTests.cs`
- Modify: `src/WhiskeyRealism/Tactical/TacticalTelemetry.cs` (add orchestrator telemetry helpers)
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Read the existing `StrategicCoordinator` to mirror its singleton pattern.**

```bash
sed -n '1,80p' src/WhiskeyRealism/Strategic/StrategicCoordinator.cs
```

Note: instance field, public static accessor, lifecycle methods, OnceLog usage. Mirror this pattern.

- [ ] **Step 2: Write the failing tests first.**

Create `tests/WhiskeyRealism.Tests/Tactical/Orchestrator/TacticalBattleCoordinatorTests.cs`:

```csharp
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tests.Tactical.Orchestrator
{
    public static class TacticalBattleCoordinatorTests
    {
        public static IEnumerable<TestCase> All()
        {
            yield return new TestCase(
                "CoordinatorStartsInactive",
                () => {
                    TacticalBattleCoordinator.ResetForTest();
                    Assert.False(TacticalBattleCoordinator.IsActive);
                });

            yield return new TestCase(
                "CoordinatorActivatesOnBattleStartWithSyntheticInputs",
                () => {
                    TacticalBattleCoordinator.ResetForTest();
                    TacticalBattleCoordinator.OnBattleStartForTest(
                        playerCicAllianceId: -1, // no player CIC
                        commanders: new[] {
                            new SyntheticCommanderInput("Lee", EchelonKind.Army, allianceId: 1, year: 1862),
                            new SyntheticCommanderInput("McClellan", EchelonKind.Army, allianceId: 0, year: 1862),
                        });
                    Assert.True(TacticalBattleCoordinator.IsActive);
                    Assert.NotNull(TacticalBattleCoordinator.GetSideOrchestrator(0));
                    Assert.NotNull(TacticalBattleCoordinator.GetSideOrchestrator(1));
                });

            yield return new TestCase(
                "CoordinatorSuppressesPlayerCicSide",
                () => {
                    TacticalBattleCoordinator.ResetForTest();
                    TacticalBattleCoordinator.OnBattleStartForTest(
                        playerCicAllianceId: 0,
                        commanders: new[] {
                            new SyntheticCommanderInput("Lee", EchelonKind.Army, allianceId: 1, year: 1862),
                            new SyntheticCommanderInput("McClellan", EchelonKind.Army, allianceId: 0, year: 1862),
                        });
                    Assert.True(TacticalBattleCoordinator.IsActive);
                    Assert.Null(TacticalBattleCoordinator.GetSideOrchestrator(0)); // player-CIC side suppressed
                    Assert.NotNull(TacticalBattleCoordinator.GetSideOrchestrator(1)); // enemy side present
                });

            yield return new TestCase(
                "CoordinatorOnBattleEndClearsState",
                () => {
                    TacticalBattleCoordinator.ResetForTest();
                    TacticalBattleCoordinator.OnBattleStartForTest(
                        playerCicAllianceId: -1,
                        commanders: new[] {
                            new SyntheticCommanderInput("Lee", EchelonKind.Army, allianceId: 1, year: 1862),
                        });
                    Assert.True(TacticalBattleCoordinator.IsActive);
                    TacticalBattleCoordinator.OnBattleEnd();
                    Assert.False(TacticalBattleCoordinator.IsActive);
                    Assert.Null(TacticalBattleCoordinator.GetSideOrchestrator(1));
                });

            yield return new TestCase(
                "CoordinatorDoubleStartIsNoOp",
                () => {
                    TacticalBattleCoordinator.ResetForTest();
                    TacticalBattleCoordinator.OnBattleStartForTest(playerCicAllianceId: -1, commanders: new[] {
                        new SyntheticCommanderInput("Lee", EchelonKind.Army, allianceId: 1, year: 1862),
                    });
                    var firstSide1 = TacticalBattleCoordinator.GetSideOrchestrator(1);
                    TacticalBattleCoordinator.OnBattleStartForTest(playerCicAllianceId: -1, commanders: new[] {
                        new SyntheticCommanderInput("Grant", EchelonKind.Army, allianceId: 0, year: 1864),
                    });
                    Assert.Same(firstSide1, TacticalBattleCoordinator.GetSideOrchestrator(1));
                });

            yield return new TestCase(
                "CoordinatorTickWhenInactiveIsNoOp",
                () => {
                    TacticalBattleCoordinator.ResetForTest();
                    TacticalBattleCoordinator.Tick();
                    Assert.False(TacticalBattleCoordinator.IsActive);
                });
        }
    }
}
```

- [ ] **Step 3: Run tests to confirm compile failure.**

Expected: compile errors for `TacticalBattleCoordinator`.

- [ ] **Step 4: Add telemetry helpers.**

Open `src/WhiskeyRealism/Tactical/TacticalTelemetry.cs`. Add these methods (matching the existing helper signature style):

```csharp
public static void LogOrchestratorBootstrap(int sidesActive, int sidesSuppressed)
{
    OnceLog.Log("once:orch-bootstrap",
        "[TacticalOrchestrator] bootstrap sidesActive=" + sidesActive +
        " sidesSuppressed=" + sidesSuppressed);
}

public static void LogOrchestratorTeardown()
{
    OnceLog.Log("once:orch-teardown",
        "[TacticalOrchestrator] teardown");
}

public static void LogOrchestratorTickFirstFire()
{
    OnceLog.Log("once:orch-coordinator",
        "[TacticalOrchestrator] coordinator first tick");
}

public static void LogCommanderRosterSummary(int allianceId, int total, int matched, int unknown)
{
    Plugin.Log.LogInfo(
        "[TacticalCommanderRoster] alliance=" + allianceId +
        " total=" + total +
        " matched=" + matched +
        " unknown=" + unknown);
}

public static void LogCommanderUnknown(EchelonKind echelon, string name)
{
    Plugin.Log.LogInfo(
        "[TacticalCommanderUnknown] echelon=" + echelon +
        " name=" + (string.IsNullOrEmpty(name) ? "<null>" : name));
}
```

`OnceLog` and `Plugin.Log` are existing project utilities; `EchelonKind` requires a `using WhiskeyRealism.Tactical.Orchestrator;` at the top of the file.

- [ ] **Step 5: Implement `TacticalBattleCoordinator.cs`.**

Create `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinator.cs`:

```csharp
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    public static class TacticalBattleCoordinator
    {
        // Two alliance slots (0 = Union, 1 = CSA in vanilla; alliance 2 Europe is intentionally
        // ignored — orchestrator does not steer Europe armies. See TacticalGateHelpers for the
        // alliance-bounds convention used elsewhere in Whiskey.)
        private static TacticalBattleOrchestrator side0;
        private static TacticalBattleOrchestrator side1;
        private static bool active;

        public static bool IsActive => active;

        public static TacticalBattleOrchestrator GetSideOrchestrator(int allianceId)
        {
            switch (allianceId)
            {
                case 0: return side0;
                case 1: return side1;
                default: return null;
            }
        }

        /// <summary>
        /// Real runtime entry point — called by TacticalObserverPatch (#35) when a battle starts.
        /// Discovers commanders from vanilla state (BattleUnits, Regiment, etc.) and instantiates
        /// per-side orchestrators. Suppresses the player's-side orchestrator when player is CIC.
        /// </summary>
        public static void OnBattleStart()
        {
            if (active) return; // double-start is a no-op
            if (!Plugin.EnableTacticalBattleOrchestrator.Value) return;

            try
            {
                int playerCicAllianceId = ResolvePlayerCicAllianceId();
                var commanders = DiscoverCommandersFromVanilla();
                BuildAndActivate(playerCicAllianceId, commanders);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] OnBattleStart skipped: " + e.GetType().Name + " " + e.Message);
                ClearForFailure();
            }
        }

        /// <summary>
        /// Real runtime entry point — called by TacticalObserverPatch (#35) when battle ends
        /// (sustained 2 ticks no-units-in-battle).
        /// </summary>
        public static void OnBattleEnd()
        {
            if (!active) return;
            try
            {
                ClearLedgersBetweenBattles();
                side0 = null;
                side1 = null;
                active = false;
                TacticalTelemetry.LogOrchestratorTeardown();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] OnBattleEnd partial: " + e.GetType().Name + " " + e.Message);
                ClearForFailure();
            }
        }

        /// <summary>
        /// Per-tick entry point — called by TacticalObserverPatch (#35) every observer cycle when
        /// the coordinator is active. Empty in O0 beyond firing the first-tick marker.
        /// </summary>
        public static void Tick()
        {
            if (!active) return;
            try
            {
                TacticalTelemetry.LogOrchestratorTickFirstFire();
                side0?.Tick();
                side1?.Tick();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] Tick skipped: " + e.GetType().Name + " " + e.Message);
            }
        }

        // ---- Test seams (no Unity / no vanilla calls; safe for harness) ----

        public static void ResetForTest()
        {
            side0 = null;
            side1 = null;
            active = false;
        }

        public static void OnBattleStartForTest(int playerCicAllianceId, IEnumerable<SyntheticCommanderInput> commanders)
        {
            if (active) return;
            BuildAndActivate(playerCicAllianceId, commanders);
        }

        // ---- Internals ----

        private static void BuildAndActivate(int playerCicAllianceId, IEnumerable<SyntheticCommanderInput> commanders)
        {
            var roster = TacticalCommanderRoster.BuildFromSynthetic(commanders);

            int suppressed = 0;
            int activated = 0;

            if (playerCicAllianceId != 0)
            {
                side0 = new TacticalBattleOrchestrator(allianceId: 0, roster: roster);
                activated++;
            }
            else
            {
                side0 = null;
                suppressed++;
            }

            if (playerCicAllianceId != 1)
            {
                side1 = new TacticalBattleOrchestrator(allianceId: 1, roster: roster);
                activated++;
            }
            else
            {
                side1 = null;
                suppressed++;
            }

            // Per-side roster summary for diagnostics.
            TacticalTelemetry.LogCommanderRosterSummary(0, roster.GetSide(0).Count, MatchedCount(roster, 0), UnknownCount(roster, 0));
            TacticalTelemetry.LogCommanderRosterSummary(1, roster.GetSide(1).Count, MatchedCount(roster, 1), UnknownCount(roster, 1));

            // Surface unknown-commander gaps once per battle.
            foreach (var entry in roster.GetSide(0)) if (!entry.MatchedHistoricalRegistry) TacticalTelemetry.LogCommanderUnknown(entry.Echelon, entry.Name);
            foreach (var entry in roster.GetSide(1)) if (!entry.MatchedHistoricalRegistry) TacticalTelemetry.LogCommanderUnknown(entry.Echelon, entry.Name);

            active = true;
            TacticalTelemetry.LogOrchestratorBootstrap(activated, suppressed);
        }

        private static int MatchedCount(TacticalCommanderRoster roster, int alliance)
        {
            int n = 0;
            foreach (var e in roster.GetSide(alliance)) if (e.MatchedHistoricalRegistry) n++;
            return n;
        }

        private static int UnknownCount(TacticalCommanderRoster roster, int alliance)
        {
            int n = 0;
            foreach (var e in roster.GetSide(alliance)) if (!e.MatchedHistoricalRegistry) n++;
            return n;
        }

        private static int ResolvePlayerCicAllianceId()
        {
            // Returns -1 when there is no player CIC; otherwise returns the alliance id of the player's CIC side.
            // Real runtime: read DLC_WL.IsCommanderInChief() + GameVars.commander[DLC_WL.dlc_chosencommander].alliance.
            // Wrap in try/catch; on failure return -1 (treat as no player CIC, run both sides).
            try
            {
                if (!DLC_WL.dlc_scenarioactive) return -1;
                if (!DLC_WL.IsCommanderInChief()) return -1;
                int chosen = DLC_WL.dlc_chosencommander;
                if (chosen < 0 || chosen >= GameVars.commander.Count) return -1;
                return GameVars.commander[chosen].alliance;
            }
            catch
            {
                return -1;
            }
        }

        private static IEnumerable<SyntheticCommanderInput> DiscoverCommandersFromVanilla()
        {
            // O0 stub: real commander discovery happens in O1 when ArmyOrchestrator ships and
            // we know what commander tiers we actually need. For O0 we only need the bootstrap
            // path to instantiate per-side roots; emitting an empty roster is fine. The empty
            // roster will still drive bootstrap telemetry and prove the lifecycle works.
            return System.Array.Empty<SyntheticCommanderInput>();
        }

        private static void ClearLedgersBetweenBattles()
        {
            // Existing Slice B follow-up note flagged the need to clear these between battles.
            // The orchestrator's teardown is the canonical clear point.
            try { TacticalSectorLedger.ClearHelpRequests(); } catch { /* helper may not exist yet — see Step 6 */ }
            try { TacticalMoraleSnapshotLedger.Clear(); } catch { /* helper may not exist yet */ }
        }

        private static void ClearForFailure()
        {
            side0 = null;
            side1 = null;
            active = false;
        }
    }
}
```

- [ ] **Step 6: Verify or add the inter-battle clear helpers.**

The orchestrator's `OnBattleEnd()` calls `TacticalSectorLedger.ClearHelpRequests()` and `TacticalMoraleSnapshotLedger.Clear()`. The umbrella spec (and prior B7+B8 follow-up notes) says these need to exist. Verify:

```bash
rg -n "public static.*ClearHelpRequests|public static.*Clear\(" src/WhiskeyRealism/Tactical/TacticalSectorLedger.cs src/WhiskeyRealism/Tactical/TacticalMoraleSnapshotLedger.cs
```

If either is missing, add a small focused helper (idempotent dictionary/list clear, no behavior change) and one regression test. Do NOT extend scope beyond a single-line clear method per file. The try/catch around each call in `ClearLedgersBetweenBattles` keeps the orchestrator safe even if the helpers don't exist yet, but landing them now means O7 cleanup doesn't have to revisit this surface.

- [ ] **Step 7: Update test csproj + Program.cs and verify.**

In `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, add:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\TacticalBattleCoordinator.cs" Link="Orchestrator\TacticalBattleCoordinator.cs" />
<Compile Include="Tactical\Orchestrator\TacticalBattleCoordinatorTests.cs" />
```

In `tests/WhiskeyRealism.Tests/Program.cs`, add:

```csharp
foreach (var t in WhiskeyRealism.Tests.Tactical.Orchestrator.TacticalBattleCoordinatorTests.All()) yield return t;
```

Run:

```bash
./build.sh && dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: build clean; tests pass at `533 PASS / 0 FAIL` (was 527; 6 new tests).

**Note:** The test seam `OnBattleStartForTest` accepts synthetic commander inputs and bypasses the real `DiscoverCommandersFromVanilla` and `ResolvePlayerCicAllianceId` paths. The harness cannot exercise the Unity-dependent vanilla paths; those get exercised by the in-game smoke gate at Task 8.

- [ ] **Step 8: Commit.**

```bash
git add -A
git commit -m "feat(orchestrator): add TacticalBattleCoordinator singleton

Static singleton owning per-side orchestrators. OnBattleStart discovers
commanders, instantiates per-side roots, suppresses player-CIC side.
OnBattleEnd clears state and inter-battle ledgers. Tick fires the
first-tick once-marker and cascades to per-side roots. All real-runtime
entry points wrapped in try/catch (never throw from a patch). Test seams
allow harness coverage without Unity. Adds 6 tests."
```

---

### Task 7: Extend TacticalObserverPatch (#35) to detect battle lifecycle

**Files:**
- Modify: `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs`
- Create: `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleLifecycleDetector.cs` (pure helper extracted for harness testability)
- Create: `tests/WhiskeyRealism.Tests/Tactical/Orchestrator/TacticalObserverLifecycleDetectionTests.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

The lifecycle detection logic is extracted to a pure helper (`TacticalBattleLifecycleDetector`) so it can be tested in the harness without Unity. The patch itself is a thin Postfix that calls into the helper and the coordinator.

- [ ] **Step 1: Read the existing TacticalObserverPatch to understand its current shape.**

```bash
sed -n '1,80p' src/WhiskeyRealism/Patches/TacticalObserverPatch.cs
```

Note the existing Harmony attribute (`[HarmonyPatch(typeof(...), nameof(...))]`), the Postfix signature, and how it currently uses `OnceLog` / `TacticalTelemetry`. The new logic adds at the END of the existing Postfix, after current observation work completes.

- [ ] **Step 2: Write the failing test first.**

Create `tests/WhiskeyRealism.Tests/Tactical/Orchestrator/TacticalObserverLifecycleDetectionTests.cs`:

```csharp
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tests.Tactical.Orchestrator
{
    public static class TacticalObserverLifecycleDetectionTests
    {
        public static IEnumerable<TestCase> All()
        {
            yield return new TestCase(
                "DetectorReturnsNoneWhenNoUnitsAcrossTicks",
                () => {
                    var det = new TacticalBattleLifecycleDetector();
                    Assert.Equal(BattleLifecycleEvent.None, det.Observe(unitsInBattleThisTick: 0));
                    Assert.Equal(BattleLifecycleEvent.None, det.Observe(unitsInBattleThisTick: 0));
                });

            yield return new TestCase(
                "DetectorReturnsBattleStartOnFirstUnitsTick",
                () => {
                    var det = new TacticalBattleLifecycleDetector();
                    Assert.Equal(BattleLifecycleEvent.None, det.Observe(unitsInBattleThisTick: 0));
                    Assert.Equal(BattleLifecycleEvent.BattleStart, det.Observe(unitsInBattleThisTick: 5));
                });

            yield return new TestCase(
                "DetectorRequiresTwoConsecutiveZeroTicksForBattleEnd",
                () => {
                    var det = new TacticalBattleLifecycleDetector();
                    det.Observe(0); det.Observe(3); // arm
                    Assert.Equal(BattleLifecycleEvent.None, det.Observe(0)); // 1st zero after units
                    Assert.Equal(BattleLifecycleEvent.BattleEnd, det.Observe(0)); // 2nd zero
                });

            yield return new TestCase(
                "DetectorIgnoresTransientZeroTickBetweenUnitsTicks",
                () => {
                    var det = new TacticalBattleLifecycleDetector();
                    det.Observe(0); det.Observe(3); // arm + start
                    Assert.Equal(BattleLifecycleEvent.None, det.Observe(0)); // 1st zero
                    Assert.Equal(BattleLifecycleEvent.None, det.Observe(2)); // back to units — no end fires
                    Assert.Equal(BattleLifecycleEvent.None, det.Observe(0)); // counter reset; need two more zeros
                    Assert.Equal(BattleLifecycleEvent.BattleEnd, det.Observe(0));
                });

            yield return new TestCase(
                "DetectorDoesNotFireDoubleStartOnSubsequentUnitsTicks",
                () => {
                    var det = new TacticalBattleLifecycleDetector();
                    det.Observe(0);
                    Assert.Equal(BattleLifecycleEvent.BattleStart, det.Observe(3));
                    Assert.Equal(BattleLifecycleEvent.None, det.Observe(5));
                    Assert.Equal(BattleLifecycleEvent.None, det.Observe(2));
                });
        }
    }
}
```

- [ ] **Step 3: Run tests to confirm compile failure.**

Expected: compile errors for `TacticalBattleLifecycleDetector` and `BattleLifecycleEvent`.

- [ ] **Step 4: Implement `TacticalBattleLifecycleDetector.cs`.**

Create `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleLifecycleDetector.cs`:

```csharp
namespace WhiskeyRealism.Tactical.Orchestrator
{
    public enum BattleLifecycleEvent
    {
        None = 0,
        BattleStart = 1,
        BattleEnd = 2,
    }

    /// <summary>
    /// Pure helper that detects battle start/end transitions from per-tick "units in battle" counts.
    /// BattleStart fires on the first tick where units > 0 after a tick where units == 0.
    /// BattleEnd fires after two consecutive units == 0 ticks following any units > 0 tick.
    /// The two-tick hysteresis prevents single-tick flapping from spurious empty observations.
    /// Independent of Unity / vanilla — safe for harness testing.
    /// </summary>
    public sealed class TacticalBattleLifecycleDetector
    {
        private bool inBattle;
        private int consecutiveZeroTicks;

        public BattleLifecycleEvent Observe(int unitsInBattleThisTick)
        {
            if (unitsInBattleThisTick > 0)
            {
                consecutiveZeroTicks = 0;
                if (!inBattle)
                {
                    inBattle = true;
                    return BattleLifecycleEvent.BattleStart;
                }
                return BattleLifecycleEvent.None;
            }

            // unitsInBattleThisTick == 0
            if (!inBattle) return BattleLifecycleEvent.None;
            consecutiveZeroTicks++;
            if (consecutiveZeroTicks >= 2)
            {
                inBattle = false;
                consecutiveZeroTicks = 0;
                return BattleLifecycleEvent.BattleEnd;
            }
            return BattleLifecycleEvent.None;
        }
    }
}
```

- [ ] **Step 5: Wire detector into TacticalObserverPatch (#35).**

Open `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs`. At the top of the class (alongside other static fields), add:

```csharp
private static readonly TacticalBattleLifecycleDetector lifecycleDetector = new TacticalBattleLifecycleDetector();
```

Add `using WhiskeyRealism.Tactical.Orchestrator;` to the file's using directives if not already present.

At the END of the existing Postfix method (after all existing observation logic completes), add:

```csharp
// Tactical orchestrator lifecycle wiring (O0 scaffold).
// Detects battle start/end from inbattle transitions; calls coordinator lifecycle methods.
// Wrapped in try/catch — never throw from a patch.
try
{
    if (Plugin.EnableTacticalBattleOrchestrator.Value)
    {
        int unitsInBattle = CountUnitsInBattleAcrossSides(__instance);
        var ev = lifecycleDetector.Observe(unitsInBattle);
        switch (ev)
        {
            case BattleLifecycleEvent.BattleStart:
                TacticalBattleCoordinator.OnBattleStart();
                break;
            case BattleLifecycleEvent.BattleEnd:
                TacticalBattleCoordinator.OnBattleEnd();
                break;
        }
        if (TacticalBattleCoordinator.IsActive)
        {
            TacticalBattleCoordinator.Tick();
        }
    }
}
catch (System.Exception e)
{
    Plugin.Log.LogWarning("[TacticalOrchestrator] observer wiring skipped: " + e.GetType().Name + " " + e.Message);
}
```

Add the helper method to the class (private, inside the same patch class):

```csharp
private static int CountUnitsInBattleAcrossSides(object aiBattleInstance)
{
    // Reads BattleUnits.bunits.everyunit (or the closest available collection) to count
    // Regiment instances with inbattle == true. Reflection-wrapped so a vanilla rename
    // downgrades to a logged warning instead of a crash, per project convention.
    try
    {
        var bunitsField = typeof(AIBattle).GetField("bunits",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var bunits = bunitsField?.GetValue(null) as BattleUnits;
        if (bunits == null) return 0;

        int count = 0;
        var everyunit = bunits.everyunit; // public field per existing Tactical/* reflection
        if (everyunit == null) return 0;
        for (int i = 0; i < everyunit.Count; i++)
        {
            var r = everyunit[i];
            if (r != null && r.inbattle) count++;
        }
        return count;
    }
    catch (System.Exception e)
    {
        Plugin.Log.LogWarning("[TacticalOrchestrator] CountUnitsInBattle: " + e.GetType().Name + " " + e.Message);
        return 0;
    }
}
```

If `bunits.everyunit` does not exist or has been renamed, look at how existing scorer files (`TacticalSectorLedger.cs`, `TacticalContactLedger.cs`) iterate vanilla units and follow the same pattern. Do not add a new vanilla collection-iteration convention.

- [ ] **Step 6: Update test csproj + Program.cs and verify.**

In `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, add:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\Orchestrator\TacticalBattleLifecycleDetector.cs" Link="Orchestrator\TacticalBattleLifecycleDetector.cs" />
<Compile Include="Tactical\Orchestrator\TacticalObserverLifecycleDetectionTests.cs" />
```

In `tests/WhiskeyRealism.Tests/Program.cs`, add:

```csharp
foreach (var t in WhiskeyRealism.Tests.Tactical.Orchestrator.TacticalObserverLifecycleDetectionTests.All()) yield return t;
```

Run:

```bash
./build.sh && dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: build clean; tests pass at `538 PASS / 0 FAIL` (was 533; 5 new tests).

- [ ] **Step 7: Commit.**

```bash
git add -A
git commit -m "feat(orchestrator): wire TacticalObserverPatch to coordinator lifecycle

Adds a pure TacticalBattleLifecycleDetector that converts per-tick
inbattle counts into BattleStart/BattleEnd events with two-tick
hysteresis to prevent single-tick flapping. TacticalObserverPatch (#35)
calls the detector at the end of its existing Postfix, then drives
TacticalBattleCoordinator.OnBattleStart/OnBattleEnd/Tick. All wrapped in
try/catch — never throw from a patch. Adds 5 tests."
```

---

### Task 8: Build, deploy, hash-verify, smoke gate documentation

**Files:**
- (no source changes; verification + deploy only)

- [ ] **Step 1: Final clean build.**

```bash
./build.sh
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. If any warnings appear, fix them — clean build is mandatory per CLAUDE.md.

- [ ] **Step 2: Final harness pass.**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: `538 PASS / 0 FAIL` (or higher if Tasks 4-7 added more tests than estimated; the hard rule is never decreasing from baseline 517).

- [ ] **Step 3: Deploy to GTCW.**

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
```

If this fails with `cp: cannot create regular file ...: Invalid argument`, GTCW is currently running — Windows holds an exclusive lock on loaded DLLs. Close the game first and retry.

- [ ] **Step 4: Hash-verify deployment.**

```bash
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: both files have identical mtime, size, and SHA-256. **Do not proceed past this step without matching hashes** — the recurring failure mode in current handoff entries is claiming smoke evidence from a stale prior build. If hashes don't match, redeploy.

- [ ] **Step 5: Document the smoke gate the user must verify.**

Append a section to `docs/handoff.md` (after the merge resolves) with the smoke gate. Until the merge resolves, leave the entry in this plan's "Smoke Gate" section below for the user to copy in.

The user must launch GTCW, enter a campaign with at least one battle (AI-vs-AI or AI-vs-player works), and confirm in `BepInEx/LogOutput.log`:

| Marker | When | Required |
|---|---|---|
| `[TacticalOrchestrator] bootstrap sidesActive=N sidesSuppressed=M` | First battle start after launch | Yes — proves OnBattleStart fires through the observer wiring |
| `[TacticalCommanderRoster] alliance=0 total=… matched=… unknown=…` | First battle start | Yes — proves roster discovery completes (counts may be 0 in O0; that is correct because `DiscoverCommandersFromVanilla` is an O0 stub) |
| `[TacticalCommanderRoster] alliance=1 total=… matched=… unknown=…` | First battle start | Yes |
| `[TacticalOrchestrator] coordinator first tick` | First tick after OnBattleStart | Yes — proves Tick fires |
| `[TacticalOrchestrator] teardown` | First battle end (both sides empty for 2 observer ticks) | Yes — proves OnBattleEnd fires |
| `[TacticalCommanderUnknown] echelon=… name=…` | Per unknown commander encountered | Conditional — fires only if vanilla discovery surfaces named unknowns |

**Negative-evidence requirements** (must NOT appear):

- Any `[TacticalOrchestrator] OnBattleStart skipped: …` (means real-runtime path threw)
- Any `[TacticalOrchestrator] OnBattleEnd partial: …`
- Any `[TacticalOrchestrator] Tick skipped: …`
- Any `[TacticalOrchestrator] observer wiring skipped: …`
- Any `[TacticalOrchestrator] CountUnitsInBattle: …` warning (means reflection lookup failed)
- Any `TargetInvocationException` or repeated Harmony failure adjacent to TacticalObserverPatch

If any negative-evidence marker appears more than once per session, treat O0 as failed; capture the surrounding log context, file an issue against `TacticalBattleLifecycleDetector` or `TacticalBattleCoordinator.OnBattleStart`/`Tick`/`OnBattleEnd`, and do NOT proceed to O1.

- [ ] **Step 6: Mid-battle save/reload smoke.**

Inside a battle (after `[TacticalOrchestrator] coordinator first tick` has fired), save the game, exit to main menu, reload the same save. Expected log behavior:

- The `[once:orch-coordinator]` / `[once:orch-bootstrap]` markers do NOT fire again (they are once-per-session via OnceLog).
- The coordinator continues ticking without exception spam.
- No `[TacticalOrchestrator] OnBattleStart skipped: …` warnings appear from the reload.

If any of these fail, the runtime-only persistence assumption (per spec §"Persistence") needs revisiting — but only after confirming the failure is reproducible, not a one-off.

- [ ] **Step 7: User reports smoke results; mark O0 complete in handoff.md.**

Once the user confirms the positive markers fire and no negative markers appear, the assistant updates `docs/handoff.md` with the O0 ship entry (deploy DLL hash, harness PASS count, observed marker list, any unknown-commander gaps to feed back into HistoricalFigureRegistry expansion) and the spec's "Phasing" table O0 row gets a "shipped + verified end-to-end YYYY-MM-DD" annotation.

Then O1 planning can begin (separate brainstorm/plan cycle for the army echelon + plan + playbooks phase).

---

## Smoke Gate (copy to handoff.md after merge resolves)

```
[O0 — Tactical Orchestrator Scaffold] shipped YYYY-MM-DD.
- Build: 0 warnings / 0 errors
- Harness: NNN PASS / 0 FAIL
- Deployed DLL SHA-256: <hash> (<bytes> bytes; dist + BepInEx plugin matched)
- Smoke markers observed: [TacticalOrchestrator] bootstrap, [TacticalCommanderRoster] alliance=0/1, [TacticalOrchestrator] coordinator first tick, [TacticalOrchestrator] teardown
- Negative evidence: no [TacticalOrchestrator] *skipped|partial|wiring* warnings; no TargetInvocationException adjacent to TacticalObserverPatch
- Mid-battle save/reload: coordinator continues ticking; once-markers do not refire
- Master config flag: Enable Tactical Battle Orchestrator = true (default)
```

## Self-Review

After writing this plan, the assistant ran the writing-plans self-review checklist:

1. **Spec coverage:**
   - Master config flag → Task 1 ✓
   - `TacticalBattlePlan.cs` rename → Task 2 ✓
   - `EchelonOrchestrator` abstract base → Task 3 ✓
   - `TacticalCommanderRoster` → Task 4 ✓
   - `TacticalBattleOrchestrator` per-side root → Task 5 ✓
   - `TacticalBattleCoordinator` singleton + lifecycle → Task 6 ✓
   - `TacticalObserverPatch` extension → Task 7 ✓
   - Smoke gate (`[once:orch-coordinator]` / `[once:orch-bootstrap]` / `[once:orch-teardown]`) → Task 8 ✓
   - Mid-battle save/reload survival → Task 8 Step 6 ✓

2. **Placeholder scan:** No "TBD" / "TODO" / "implement later" / "add appropriate error handling" patterns. Vanilla-API verification gates are explicit (Task 4 Step 5, Task 6 Step 6, Task 7 Step 5) with what to do if APIs are missing.

3. **Type consistency:** `EchelonKind` enum used consistently across all tasks. `SyntheticCommanderInput` struct shape consistent (Task 4 definition matches Task 5/6 usage). `TacticalCommanderRoster.GetByName(string)` / `GetSide(int)` / `Count` / `UnknownCount` shape consistent. `TacticalBattleCoordinator.OnBattleStart()` / `OnBattleEnd()` / `Tick()` / `IsActive` / `GetSideOrchestrator(int)` / `ResetForTest()` / `OnBattleStartForTest(int, IEnumerable<SyntheticCommanderInput>)` shape consistent. `BattleLifecycleEvent` enum values (`None` / `BattleStart` / `BattleEnd`) consistent across detector and observer wiring.
