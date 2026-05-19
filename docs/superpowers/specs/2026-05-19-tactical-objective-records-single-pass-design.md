# Tactical ObjectiveRecords Single-Pass Refactor — Design

**Date:** 2026-05-19
**Workstream:** Tactical orchestrator hot-path optimization
**Slice:** 1 (of a potential 2–3 if data warrants follow-ons)
**Status:** Approved design, ready for plan

---

## 1. Motivation (data-grounded)

User-reported hitch at 20x compression on a small battle. The permanent
hot-path diagnostic infrastructure (added in commits `6f46aaa` and
`9501783` on `feat/hotpath-measurement`) captured two smoke sessions and
isolated the cost definitively:

| Sub-scope inside `TacticalBattleSnapshotBuilder.Build` | n | p50 | p99 | sum (ms) | % of Build |
|---|---|---|---|---|---|
| `tactical.snapshot-build.objective-records` | 64 | **4.80** | **11.03** | **318** | **88%** |
| `tactical.snapshot-build.evidence-bundle` | 64 | 0.40 | 4.02 | 27 | 7% |
| `tactical.snapshot-build.tick-cycle` (containing Build) | 64 | 5.45 | 15.81 | 362 | 100% |

The four `Attach*` walks I originally suspected are **negligible**
(sub-millisecond p99 each, <1ms total per Tick). The advisor's pushback
on the per-Tick cache hypothesis was correct: the real cost lives in
`TacticalVisionRuntimeAdapter.BuildObjectiveRecordsFromBattle`.

Root cause inspection of `BuildObjectiveRecordsFromBattle`
(`src/WhiskeyRealism/Tactical/Orchestrator/TacticalVisionRuntimeAdapter.cs:324`):
six logical passes over `BattleUnits.completeunitlist` (≈26,585 units in
the smoke battle), each with reflection-based per-unit field reads
(`SafeCurrentSetObjective`, `EstimateVisibleEnemyStrength`,
`SafeStrength`, etc.), plus five fresh `List<T>` allocations per call.

## 2. Goals & non-goals

**Goals**

- Drop `tactical.snapshot-build.objective-records` p99 from ~11 ms to ≤ 3 ms
- Drop `tactical.snapshot-build.tick-cycle` p99 from ~16 ms to ≤ 7 ms
- Reduce per-Build allocation count (the five fresh `List<T>` + `HashSet<string>` per call)
- Preserve the `ObjectiveRecord[]` output exactly — no behavior drift
- Stay default-on per AGENTS.md tactical default-on policy

**Non-goals**

- Refactor `ArmyEvidenceBuilder.Build` (only 7% of Build cost — separate slice if still hitching after this lands)
- Extend the heavy-gate to throttle `Attach*` walks (Attach walks are sub-ms, not a problem)
- Background-thread `Build` (slice 2 candidate if main-thread fix is not enough)
- Investigate the ~22 ms unaccounted outlier in `orchestrator-tick` max (likely GC-pause-driven; the aggregate's allocation reduction should help, re-measure after this lands)
- Single-pass refactor of `ArmyEvidenceBuilder.Build`'s `BuildEnemyVisibleState` + `EstimateOwnAverages` (same anti-pattern, lower impact, defer)

## 3. Architecture

Two-unit split:

```
                                  TacticalBattleSnapshotBuilder.Build
                                              │
                          ┌───────────────────┴───────────────────┐
                          │                                       │
                          ▼                                       ▼
              ArmyEvidenceBuilder.Build               BuildObjectiveRecordsFromBattle  (refactored)
                          │                                       │
                          │ (unchanged this slice)                ▼
                          │                       TacticalUnitObservationAggregate.Capture(allianceId)
                          │                                       │  one walk of completeunitlist
                          │                                       ▼
                          │                       ┌──────────────┴──────────────┐
                          │                       ▼     ▼     ▼     ▼     ▼     ▼
                          │              ObjChain  MainLoop  MapObj  Approach  EnemyLine  MoveAnchor
                          │                       (all read from aggregate via IObservationSource)
                          ▼                                       │
                  Bundle (unchanged)                              ▼
                                                         ObjectiveRecord[]
```

**New types**

- `TacticalUnitObservation` — value struct, per-unit captured fields (no Unity refs)
- `TacticalUnitObservationAggregate` — pooled container + `IObservationSource` impl + the single capture walk
- `IObservationSource` — interface the sub-builders consume; harness tests can mock cleanly

**Existing types changed**

- `TacticalVisionRuntimeAdapter` — `BuildObjectiveRecordsFromBattle` and the six sub-builders are refactored to take `IObservationSource` (or read-only views thereof) instead of walking `completeunitlist` directly
- `TacticalBattleCoordinator` (runtime partial) — reset aggregate pool in `ResetRuntimeTickState`
- `Plugin.cs` — new `EnableSinglePassObjectiveRecords` ConfigEntry (default `true`)

## 4. Aggregate shape

`TacticalUnitObservation` (value struct):

```csharp
public readonly struct TacticalUnitObservation
{
    public int InstanceId;          // GameObject.GetInstanceID()
    public int Unittyp;             // raw, NOT shifted
    public int Alliance;
    public bool IsRouted;           // isrouted || markedforrout

    public float WorldX, WorldZ;    // transform.position.x/z (cached once)

    public float Strength;          // groupstrengthactive (combat presence)
    public float GroupOwnInRange;   // groupowninrange
    public float GroupAiGroup;      // groupstrengthaigroup

    // Current set objective (if any)
    public bool HasCurrentSetObjective;
    public int CurrentSetObjectiveId;
    public float ObjectiveX, ObjectiveZ;
    public TacticalObjectiveType ObjectiveType;  // anchor-resolved at capture

    // Visibility-derived
    public float VisibleEnemyStrength;
    public bool HasVisibleEnemy;

    // Readiness (used by snapshot fields downstream; cheap to capture once)
    public float Fatigue01;
    public float Ammo01;

    public int EffectiveCommandLevel;  // unittyp - commandhierarchyshift
}
```

Field selection is the union of what each sub-builder currently calls on
a `Regiment`. If a sub-builder needs a field not in this struct, the
parity check will fire and we add it. The parity window catches missing
fields on real 26k-unit data before they ship. The list above reflects
the audit-as-of-design; final field set is fixed during implementation
by enumerating every `Regiment` member access inside the six sub-builders
and ensuring each maps to an aggregate field (or is rederivable from
fields already captured).

## 5. `TacticalUnitObservationAggregate`

Pooled, lifetime-managed by battle:

```csharp
public sealed class TacticalUnitObservationAggregate : IObservationSource
{
    private static readonly TacticalUnitObservationAggregate _shared = new();

    private readonly List<TacticalUnitObservation> _units = new(512);
    private readonly List<int> _alliedIndices = new(64);
    private readonly List<int> _enemyIndices = new(64);
    private int _capturedForAlliance = -1;
    private long _capturedAtFrame = -1;

    public static TacticalUnitObservationAggregate Shared => _shared;

    public void ClearForBattleEnd()  // called from ResetRuntimeTickState
    {
        _units.Clear();
        _alliedIndices.Clear();
        _enemyIndices.Clear();
        _capturedForAlliance = -1;
        _capturedAtFrame = -1;
    }

    public IObservationSource Capture(int allianceId)
    {
        _units.Clear();
        _alliedIndices.Clear();
        _enemyIndices.Clear();
        _capturedForAlliance = allianceId;

        var raw = BattleUnits.completeunitlist as IList;
        if (raw == null) return this;

        for (int i = 0; i < raw.Count; i++)
        {
            var reg = raw[i] as Regiment;
            if (reg == null) continue;
            var obs = CaptureUnit(reg);  // ONE reflection burst per unit
            _units.Add(obs);
            int idx = _units.Count - 1;
            if (obs.Alliance == allianceId) _alliedIndices.Add(idx);
            else _enemyIndices.Add(idx);
        }
        return this;
    }

    // IObservationSource methods: AllUnits, AlliedUnits(allianceId), EnemyUnits(allianceId),
    // GetByInstanceId(id), AlliedCount, EnemyCount, etc.
}
```

Steady-state allocation count: zero per Build (lists already sized).
First-call growth allocs once per battle. Cleared on battle end. The
aggregate is a per-battle singleton; battles never overlap so locking
is unnecessary, but every public method tolerates an uncaptured
aggregate (returns empty views).

## 6. Sub-builder transformation

For each of the six sub-builders, the change is mechanical:

| Sub-builder | Before | After |
|---|---|---|
| `AddObjectiveChainObservations` | reads `battle.objectivechain` (no unit walk) | unchanged |
| Main loop (`for i over units.Count`) | walks `completeunitlist`, filters by `IsUsableOwnUnit`, calls 3 reflection helpers per unit | iterates `source.AlliedUnits(allianceId)`, reads pre-captured fields |
| `AddMapObjectiveObservations` | reads `BattleObjectives` data (not units) | unchanged |
| `BuildApproachAvenueObservations` | walks units twice (own + enemy) | iterates `source.AlliedUnits` + `source.EnemyUnits` |
| `TryVisibleEnemyLine` | walks units, filters enemy + visibility | iterates `source.EnemyUnits`, reads `HasVisibleEnemy` |
| `TryMovementAnchorLine` | walks units, filters movement state | iterates `source.AlliedUnits`, reads position + group-state fields from the observation |

The sub-builders' downstream logic (point fusion, approach scoring,
record fabrication) is **unchanged**. Only the data source flips. This
keeps the diff focused and reviewable.

## 7. Parity verification (defense-in-depth)

The 2026-05-19 synth-army bug ate a week because it shipped on a code
path that no test exercised and no runtime check caught. We're not
repeating that.

**Harness layer (new tests, build-time):**

- `TacticalUnitObservationAggregateTests` — feed `Capture` a stub of `BattleUnits.completeunitlist`; assert field-by-field that the aggregate captures every field the sub-builders read.
- `ObjectiveRecordsFromAggregateTests` — feed a known synthetic aggregate, assert `ObjectiveRecord[]` output matches an oracle (frozen expected output computed from the same fixtures).
- Add `<Compile Include>` entries to `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` per AGENTS.md rule (explicit includes, no globs).

**Runtime layer (first 20 Builds per battle — fixed window, not sliding; opt-out config):**

```csharp
// in BuildObjectiveRecordsFromBattle:
if (Plugin.SinglePassParityWindowActive && _parityComparesRemaining > 0)
{
    var oldResult = BuildObjectiveRecordsFromBattle_Legacy(battle, allianceId);
    var newResult = BuildObjectiveRecordsFromBattle_Aggregate(source);
    // ObjectiveRecordsEqual: array length + per-element ObjectiveId/Type/Point/StatusCounts/StrengthSums
    // (no float-tolerance for strengths — capture is from the same regiment fields, identical floats expected)
    if (!ObjectiveRecordsEqual(oldResult, newResult))
    {
        EmitParityMismatch(allianceId, oldResult, newResult);
        // safety: return legacy result while parity window catches up; do NOT silently drift
        return oldResult;
    }
    _parityComparesRemaining--;
    return newResult;  // proven equal, save the comparison work going forward
}
return BuildObjectiveRecordsFromBattle_Aggregate(source);
```

Parity counter is per-battle (reset in `ResetRuntimeTickState`). Mismatch
emits a bounded `TacticalObjectiveRecordsParityMismatch` telemetry event
(Gate category, full diff fields). After 20 clean Builds the window
closes for the rest of the battle.

**Telemetry tag registration:** new event name `TacticalObjectiveRecordsParityMismatch` in `TelemetryTagPolicy.cs`.

**Window exit conditions:**
- 20 clean compares → flip to aggregate-only for this battle, log `"[ObjectiveRecordsParity] window-clean battle=… alliance=… compares=20"`
- 1 mismatch → continue running both paths, return legacy, alert via telemetry; window stays open all battle so we get a full diff record. Player-visible behavior unchanged because we return legacy.

After one release cycle (no mismatches observed in normal play), the legacy path + parity-window code is removed.

## 8. Rollback config flag

```csharp
EnableSinglePassObjectiveRecords = Config.Bind(
    "Tactical Orchestrator",
    "Enable Single-Pass Objective Records",
    true,
    "Single-pass ObjectiveRecord aggregation. Cuts BuildObjectiveRecordsFromBattle "
    + "p99 from ~11ms to ~2ms by walking completeunitlist once instead of 6 times. "
    + "Default ON. Set false to roll back to the legacy per-walk path. "
    + "Default-on per AGENTS.md tactical policy.");
```

When `false`: `BuildObjectiveRecordsFromBattle` skips the aggregate path entirely and uses the unchanged legacy implementation. Parity window also short-circuits.

## 9. Files to modify

| File | Change |
|---|---|
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalUnitObservation.cs` | **NEW** value struct |
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalUnitObservationAggregate.cs` | **NEW** pooled aggregate + `IObservationSource` |
| `src/WhiskeyRealism/Tactical/Orchestrator/IObservationSource.cs` | **NEW** interface |
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalVisionRuntimeAdapter.cs` | `BuildObjectiveRecordsFromBattle` + 6 sub-builders refactored to consume `IObservationSource`; legacy path retained under `EnableSinglePassObjectiveRecords=false` and parity-window |
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleSnapshotBuilder.cs` | `Build` calls `TacticalUnitObservationAggregate.Shared.Capture(allianceId)` once, passes result into `BuildObjectiveRecordsFromBattle` |
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs` | `ResetRuntimeTickState` calls `TacticalUnitObservationAggregate.Shared.ClearForBattleEnd()` |
| `src/WhiskeyRealism/Plugin.cs` | new `EnableSinglePassObjectiveRecords` ConfigEntry |
| `src/WhiskeyRealism/Telemetry/TelemetryTagPolicy.cs` | register `TacticalObjectiveRecordsParityMismatch` event |
| `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` | explicit `<Compile Include>` for the 3 new source files + 2 new test files |
| `tests/WhiskeyRealism.Tests/TacticalUnitObservationAggregateTests.cs` | **NEW** harness coverage of capture |
| `tests/WhiskeyRealism.Tests/ObjectiveRecordsFromAggregateTests.cs` | **NEW** harness coverage of sub-builder parity |
| `docs/telemetry.md` | document new parity event + (eventually) the steady-state Build p99 drop |
| `docs/tactical-orchestrator.md` | living-doc update with new module + parity window contract |

No new Harmony patches — this is pure orchestrator-internal refactor.

## 10. Testing & verification

**Harness (build-time, must pass before deploy):**

- All existing 1245 tests still pass
- New aggregate-capture tests pass
- New sub-builder parity tests pass
- Target: ≥ 1252 PASS / 0 FAIL

**DLL build/deploy/hash (mandatory per AGENTS.md):**

- `./build.sh` clean
- `cp` to `<GTCW>/BepInEx/plugins/`
- `sha256sum` parity dist vs deployed

**Runtime smoke (mandatory):**

- Same 26k-unit battle that produced session `20260519-173901`
- 20x compression
- Look for: `TacticalObjectiveRecordsParityMismatch` count = 0; `tactical.snapshot-build.objective-records` p99 ≤ 3 ms; subjective hitch reduction

**Rollback test:**

- Set `Enable Single-Pass Objective Records = false`, restart, verify behavior matches legacy + the orchestrator-tick scope cost reverts to ~16 ms p99 (sanity check that the flag actually flips the path)

## 11. Success criteria

| Metric | Baseline (sha 6407edfc) | Target |
|---|---|---|
| `snapshot-build.objective-records` p99 | 11.03 ms | ≤ 3 ms |
| `snapshot-build.tick-cycle` p99 | 15.81 ms | ≤ 7 ms |
| `orchestrator-tick` p99 | 12.67 ms | ≤ 5 ms |
| `gcDelta` p99 | 1 | ≤ 1 (unchanged or lower) |
| Parity mismatch events | n/a | 0 |
| Harness PASS | 1245 | ≥ 1252 |
| Subjective hitch at 20x small-medium battle | present | absent or substantially reduced |

If `gcDelta` p99 *rises*, the pool design is broken — investigate before
declaring done.

## 12. Known follow-ups (explicit non-scope, tracked for slice 2 decisions)

- `ArmyEvidenceBuilder.Build` has the same anti-pattern (2-3 walks of `completeunitlist` in `BuildEnemyVisibleState` + `EstimateOwnAverages`). Only 27 ms over the smoke session. Defer; if `snapshot-build.tick-cycle` p99 still > 7 ms after this slice lands, fold this in as slice 2.
- The unaccounted 22 ms outlier in `orchestrator-tick` max (42 ms tick vs ~20 ms sum-of-inner-scopes) is most plausibly a GC pause coinciding with the tick. The aggregate's allocation reduction should help. Re-measure post-fix; if outliers persist at the same magnitude, instrument the gap.
- Move `Build` to a background worker thread (snapshot pattern) is the natural slice if main-thread refactors hit a floor.

## 13. AGENTS.md compliance checklist

- [x] Shipped-code/decompile-first: read `BuildObjectiveRecordsFromBattle` source before designing
- [x] Default-on per tactical policy: `EnableSinglePassObjectiveRecords = true`
- [x] Description string matches code default
- [x] Try/catch around reflection lookups in `CaptureUnit`; never throws from the capture walk
- [x] Bounded logs (OnceLog on parity mismatch with full-diff, bounded by parity window count = 20 max emissions per battle)
- [x] Rollback config flag documented in description + this design doc
- [x] No transform.parent walks (synth-army bug pattern) — aggregate captures only the data sub-builders need; command hierarchy is not consumed by `BuildObjectiveRecordsFromBattle`
- [x] Living docs to update on ship: `docs/tactical-orchestrator.md`, `docs/telemetry.md`, `docs/handoff.md`
- [x] Per-side dedup not applicable (BuildObjectiveRecordsFromBattle is per-alliance; the aggregate is captured fresh per Build call)
