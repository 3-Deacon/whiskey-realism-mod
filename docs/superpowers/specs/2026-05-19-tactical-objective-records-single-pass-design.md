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

- Drop `tactical.snapshot-build.objective-records.aggregate` p99 to ≤ 3 ms (this is the parity-window-immune scope; see §7a). Outer scope drops to ≤ 7 ms post-parity-window.
- Drop `tactical.snapshot-build.tick-cycle` p99 from ~16 ms to ≤ 7 ms post-parity-window
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

`TacticalUnitObservation` (value struct). **All capture is alliance-aware
— visibility fields are populated ONLY for own-alliance units** (matching
the current code's `IsUsableOwnUnit` filter); enemy units get cheap
position + strength only. Computing `VisibleEnemyStrength` /
`HasVisibleEnemy` per unit walks `unit.unitrange.enemyinfirerangereg`
and `unit.unitrange.enemyinrangereg` and allocates a `HashSet<int>`
(see `TacticalFogOfWarContact.cs:24-58`); eager per-raw-unit visibility
would be slower than the current code, not faster.

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

    // Current set objective (if any) — own-alliance only
    public bool HasCurrentSetObjective;
    public int CurrentSetObjectiveId;
    public float ObjectiveX, ObjectiveZ;
    public TacticalObjectiveType ObjectiveType;  // anchor-resolved at capture

    // Last set waypoint (used by TryMovementAnchorLine fallback,
    // TacticalVisionRuntimeAdapter.cs:1063 — rejects within √625m of
    // current position). Own-alliance only.
    public bool HasLastWaypoint;
    public float LastWaypointX, LastWaypointZ;

    // Visibility-derived — POPULATED ONLY FOR OWN-ALLIANCE UNITS.
    // Set to zero/false for enemy units; sub-builders that need enemy
    // visibility (TryVisibleEnemyLine) walk from the own-unit side.
    public float VisibleEnemyStrength;
    public bool HasVisibleEnemy;

    // Readiness (used by snapshot fields downstream; cheap to capture once)
    public float Fatigue01;
    public float Ammo01;

    public int EffectiveCommandLevel;  // unittyp - commandhierarchyshift
}
```

### 4a. Pre-implementation field audit (mandatory)

Before writing capture code, **enumerate every `Regiment` member access
inside the six sub-builders** and confirm each maps to an aggregate
field or is rederivable from captured fields. The implementation plan's
first task is this audit, recorded as a table in the plan doc. Fields
known from initial audit:

| Sub-builder | Regiment accesses identified |
|---|---|
| Main loop (`for i over units.Count`) | `SafeCurrentSetObjective` (reads regiment objective ref), `EstimateVisibleEnemyStrength` (→ `VisibleEnemyStrength`), `SafeStrength` (→ `groupstrengthactive`), `IsUsableOwnUnit` (alliance + active + non-rout) |
| `BuildApproachAvenueObservations` | own + enemy iteration with `SafePosition`, `SafeStrength`, visibility derivation |
| `TryVisibleEnemyLine` | enemy filter + `HasVisibleEnemy` derivation, `SafePosition`, `SafeStrength` |
| `TryMovementAnchorLine` | own filter + `SafePosition`, `TryLastWaypointPoint` (→ `lastsetwaypointposition`), `SafeStrength` |
| `AddMapObjectiveObservations` | reads `BattleObjectives` data (not units) — no aggregate dependency |
| `AddObjectiveChainObservations` | reads `battle.objectivechain` — no aggregate dependency |

Any field discovered during implementation that wasn't in the audit
gets the same treatment as the `LastWaypoint` fields here: explicit
add to the struct, documented justification, and a parity-test case.

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
            // CaptureUnit is alliance-aware: visibility + objective +
            // waypoint fields populated ONLY when reg.alliance == allianceId.
            // Enemy units get cheap fields (position, strength, unittyp,
            // routed flag) only, matching the cost profile of the current
            // code which never calls visibility logic for enemy units.
            var obs = CaptureUnit(reg, allianceId);
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
- Test csproj inclusion: new test `.cs` files live physically under `tests/WhiskeyRealism.Tests/` and are picked up by the SDK's default-compile-items behavior — **no explicit `<Compile Include>` entry needed** for them. The explicit `<Compile Include="..\..\src\…" Link="…">` pattern is reserved for production source files referenced from outside the test project directory (the strategic types linked into the test assembly). Adding explicit entries for in-project test files creates duplicate compile items.

**Runtime layer (first 20 Builds per *(battleSequence, allianceId)* — fixed window, not sliding; opt-out config):**

```csharp
// in BuildObjectiveRecordsFromBattle:
// Parity counter is keyed by (battleSequence, allianceId). DriveTickCycle
// builds per-side snapshots (s = side.AllianceId) so each alliance must
// get its own 20 clean compares; a global counter could let one side eat
// the window while the other is never verified.
ParityKey key = new ParityKey(battleSequence, allianceId);
int remaining = _parityComparesRemaining.TryGetValue(key, out var r) ? r : 20;
bool mismatchObservedForKey = _parityMismatchObserved.Contains(key);

if (Plugin.SinglePassParityWindowActive && (remaining > 0 || mismatchObservedForKey))
{
    ObjectiveRecord[] oldResult, newResult;
    using (TelemetryPerf.Scope("tactical.snapshot-build.objective-records.legacy", ...))
        oldResult = BuildObjectiveRecordsFromBattle_Legacy(battle, allianceId);
    using (TelemetryPerf.Scope("tactical.snapshot-build.objective-records.aggregate", ...))
        newResult = BuildObjectiveRecordsFromBattle_Aggregate(source);

    // ObjectiveRecordsEqual: array length + per-element ObjectiveId/Type/Point/StatusCounts/StrengthSums
    // (no float-tolerance for strengths — capture is from the same regiment fields, identical floats expected)
    if (!ObjectiveRecordsEqual(oldResult, newResult))
    {
        _parityMismatchObserved.Add(key);
        EmitParityMismatch(battleSequence, allianceId, oldResult, newResult);
        // safety: stay on legacy for THIS (battle, alliance) for the rest of the battle.
        return oldResult;
    }
    _parityComparesRemaining[key] = remaining - 1;
    return newResult;  // proven equal
}

// Post-window steady-state path — measured by the outer scope
// `tactical.snapshot-build.objective-records` and additionally by the
// dedicated `.aggregate` scope so success metrics are derived from
// aggregate-only timings (not poisoned by parity-window dual-runs).
using (TelemetryPerf.Scope("tactical.snapshot-build.objective-records.aggregate", ...))
    return BuildObjectiveRecordsFromBattle_Aggregate(source);
```

Parity state (counter + mismatch set) is keyed by `(battleSequence, allianceId)`
and cleared in `ResetRuntimeTickState`. Mismatch on side 0 does **not**
shut down parity tracking for side 1, and vice versa.

**Telemetry tag registration:** new event name `TacticalObjectiveRecordsParityMismatch` in `TelemetryTagPolicy.cs`.

**Window exit conditions (per `(battleSequence, allianceId)` key):**
- 20 clean compares on this key → flip to aggregate-only for this key for the rest of the battle, log `"[ObjectiveRecordsParity] window-clean battle=… alliance=… compares=20"`
- 1 mismatch on this key → continue running both paths for this key, return legacy, alert via telemetry; window stays open all battle so we get a full diff record. Player-visible behavior unchanged because we return legacy. The OTHER alliance's parity window is unaffected.

After one release cycle (no mismatches observed in normal play), the legacy path + parity-window code is removed.

### 7a. Telemetry scope structure (addresses parity-poisoning of p99)

The current `tactical.snapshot-build.objective-records` scope wraps the
entire function call (`TacticalBattleSnapshotBuilder.cs:139`). During
the parity window it would include legacy + aggregate dual-run time,
which on a 64-sample distribution would *be* the p99 — defeating the
purpose of the metric. We add three scopes:

| Scope | Wraps | Use |
|---|---|---|
| `tactical.snapshot-build.objective-records` | the whole function (existing) | end-to-end ground truth — includes parity overhead when active |
| `tactical.snapshot-build.objective-records.aggregate` | aggregate path only | **the success metric** — measures steady-state cost |
| `tactical.snapshot-build.objective-records.legacy` | legacy path only (parity window or rollback) | comparison baseline; drops to zero samples post-window |

The success criteria below reference the `.aggregate` scope, not the
outer one, so parity dual-run during the first 20 builds doesn't
poison the headline number.

## 8. Rollback config flag

```csharp
EnableSinglePassObjectiveRecords = Config.Bind(
    "Tactical Orchestrator",
    "Enable Single-Pass Objective Records",
    true,
    "Single-pass aggregation for ObjectiveRecord building. Walks "
    + "BattleUnits.completeunitlist once instead of six times. Default ON. "
    + "Set false to roll back to the legacy per-walk path if a regression "
    + "appears. Default-on per AGENTS.md tactical policy. Measured "
    + "performance characteristics are documented in docs/telemetry.md "
    + "once smoke-verified.");
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
| `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` | explicit `<Compile Include Link>` for the 3 new production source files only; new test files are auto-included by SDK default-compile-items |
| `tests/WhiskeyRealism.Tests/TacticalUnitObservationAggregateTests.cs` | **NEW** harness coverage of capture |
| `tests/WhiskeyRealism.Tests/ObjectiveRecordsFromAggregateTests.cs` | **NEW** harness coverage of sub-builder parity |
| `docs/telemetry.md` | document new parity event + (eventually) the steady-state Build p99 drop |
| `docs/tactical-orchestrator.md` | living-doc update with new module + parity window contract |

No new Harmony patches — this is pure orchestrator-internal refactor.

## 10. Testing & verification

**Harness (build-time, must pass before deploy):**

- All existing tests still pass (baseline at branch creation: 1245 PASS / 0 FAIL; AGENTS.md / handoff.md docs say 1244 — they're one test stale and get refreshed at closeout)
- New aggregate-capture tests pass
- New sub-builder parity tests pass
- Target: baseline + 5–8 new tests, 0 FAIL

**DLL build/deploy/hash (mandatory per AGENTS.md):**

- `./build.sh` clean
- `cp` to `<GTCW>/BepInEx/plugins/`
- `sha256sum` parity dist vs deployed

**Runtime smoke (mandatory):**

- Same 26k-unit battle that produced session `20260519-173901`
- 20x compression
- Look for: `TacticalObjectiveRecordsParityMismatch` count = 0; `tactical.snapshot-build.objective-records.aggregate` p99 ≤ 3 ms (steady-state metric, parity-window-immune); subjective hitch reduction

**Rollback test:**

- Set `Enable Single-Pass Objective Records = false`, restart, verify behavior matches legacy + the orchestrator-tick scope cost reverts to ~16 ms p99 (sanity check that the flag actually flips the path)

## 11. Success criteria

| Metric | Baseline (sha 6407edfc) | Target |
|---|---|---|
| **`snapshot-build.objective-records.aggregate` p99** | n/a (new scope) | **≤ 3 ms** ← primary success metric, parity-window-immune |
| `snapshot-build.objective-records` p99 (outer, end-to-end) | 11.03 ms | ≤ 7 ms post-parity-window; up to 2× during window is acceptable |
| `snapshot-build.tick-cycle` p99 | 15.81 ms | ≤ 7 ms (post-parity-window) |
| `orchestrator-tick` p99 | 12.67 ms | ≤ 5 ms (post-parity-window) |
| `gcDelta` p99 | 1 | ≤ 1 (unchanged or lower) |
| Parity mismatch events | n/a | 0 |
| Harness PASS | baseline + 5–8 new tests | 0 FAIL, count grows by new test additions |
| Subjective hitch at 20x small–medium battle | present | absent or substantially reduced |

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

## 14. Adversarial review findings + responses (2026-05-19)

Spec went through an adversarial review pass after first commit. All 7 findings accepted; cited code anchors verified.

| # | Severity | Finding | Resolution |
|---|---|---|---|
| 1 | P1 | Parity window poisons the p99 success metric — outer scope sees dual-path cost during first 20 Builds | §7a adds `.aggregate` and `.legacy` sub-scopes; success criteria reference `.aggregate`, parity-immune |
| 2 | P1 | Aggregate field list missed `Regiment.lastsetwaypointposition` used by `TryMovementAnchorLine` fallback (`TacticalVisionRuntimeAdapter.cs:1063`) | Added `HasLastWaypoint`, `LastWaypointX`, `LastWaypointZ` to `TacticalUnitObservation`; §4a mandates pre-implementation field audit |
| 3 | P1 | Eager visibility capture for raw `completeunitlist` would make hot path slower (visibility walks `enemyin{fire,}rangereg` + allocates `HashSet<int>` per call, `TacticalFogOfWarContact.cs:24-58`) | §4 + §5 now specify alliance-aware capture: visibility populated only for own-alliance units, matching current code |
| 4 | P1 | Per-battle parity counter could be exhausted by one alliance, leaving the other unverified | Parity state keyed by `(battleSequence, allianceId)`; mismatch on one alliance does not shut down the other |
| 5 | P2 | Plan to add `<Compile Include>` for new test files conflicts with SDK-style csproj default-include behavior | §7 corrected: new test `.cs` files in `tests/WhiskeyRealism.Tests/` are auto-included; explicit linked includes are for external production source files only |
| 6 | P2 | Harness baseline (1245) drifts from AGENTS.md text (1244) | Phrased as "baseline + N new tests" rather than absolute number; closeout refreshes the docs |
| 7 | P2 | Config description claimed unverified "~2ms" — violates AGENTS.md description-string-sync rule for player-facing text | Rewrote description to describe intent only; performance numbers go in `docs/telemetry.md` after smoke-verification |
