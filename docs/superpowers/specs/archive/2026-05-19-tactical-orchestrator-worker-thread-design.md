# Tactical Orchestrator Worker-Thread Refactor — Design

**Date:** 2026-05-19
**Workstream:** Tactical orchestrator hot-path optimization (Slice 2)
**Predecessor:** [`2026-05-19-tactical-objective-records-single-pass-design.md`](2026-05-19-tactical-objective-records-single-pass-design.md) (Slice 1, shipped)
**Status:** Approved design, ready for plan

---

## 1. Motivation

User reported sustained 5–7s hitches and "skipping that never goes away" starting ~10 seconds into 20× compression. Slice 1 reduced `BuildObjectiveRecordsFromBattle` from 11ms → 5.86ms p99 (47% reduction in the dominant single cost), but the **structural problem remains**: at 20× compression vanilla fires `CalculateSideStatsAndUpdateAITasks` ~20× per wall-second, each invocation triggers our orchestrator-tick, and the per-tick cost (25–67ms p99) scales linearly with compression — 1340ms of CPU per wall-second consumed by our mod alone.

Diagnostic batch-1 instrumentation revealed which patches contribute:

| Scope | p99 | Notes |
|---|---|---|
| `tactical.orchestrator-tick` | 67ms | Outer; sum of inner work below |
| `tactical.patch.battle-group-stance` | 20.6ms | Per-tick patch, walks per-command-group visibility |
| `tactical.snapshot-build.tick-cycle` | 29ms (gated) → 5.86ms (aggregate) | Improved in Slice 1 |
| `tactical.operations-ledger` | 14.6ms early → 0.1ms stable | Gated; converges |
| `tactical.attach-command-tree` | 12.3ms | Per-tick, ungated |

All of these run on Unity's single main thread. Unity API is main-thread-only (every `Regiment` / `BattleUnits` / `GameObject` / `Transform` access). Mono Boehm GC is stop-the-world: any thread's allocation can pause main. Vanilla AI + rendering + our mod compete for one CPU core.

**The right fix is to decouple our AI's *analysis* from the main thread**, leaving only cheap state-capture and decision-apply on main. This is the same architectural pattern the project already uses successfully for `TelemetryWriter` (Mono background writer thread, single-slot wake event, drop-old buffer).

## 2. Goals & non-goals

**Goals**

- Drop main-thread `tactical.orchestrator-tick` p99 from ~25–67ms to ≤ 10ms steady-state.
- AI decision quality unchanged — worker produces identical decisions to current synchronous path within ≤ 1 main-thread tick of latency (≤ 50ms wall at any compression).
- Per-tick Harmony patches with significant work (`BattleGroupStancePatch`, etc.) refactor to read pre-computed decisions from the worker's published buffer, becoming O(1) lookups + cheap vanilla writes.
- Rollback config flag `EnableTacticalOrchestratorWorker` (default `true` per AGENTS.md).
- Existing parity-window mechanism extends naturally — first 20 main-thread ticks per `(battleSequence, allianceId)` run BOTH worker and synchronous paths, compare outputs, emit `TacticalOrchestratorParityMismatch` on diff.

**Non-goals**

- Move Unity API calls off main thread (impossible — Unity restriction).
- Eliminate event-trigger Harmony patches like `B7CheckAIBombardment` / `B8CheckUseOfReserves` — they react to specific vanilla decisions and stay where they are. They WILL benefit indirectly via the per-frame visibility cache (covered in Slice 3 if still needed).
- Touch the strategic-brain coordinator (campaign-tier, monthly cadence — entirely different cost profile).
- Add a per-frame visibility cache in `TacticalFogOfWarContact` — that's Slice 3.

## 3. Architecture

```
                MAIN THREAD                                    WORKER THREAD
                ===========                                    =============

  Vanilla CalculateSideStatsAndUpdateAITasks
              │
              ▼
  TacticalBattleCoordinator.Tick(battle)
              │
              ├─ if (!_workerEnabled) → fall through to legacy synchronous path
              │
              ├─ Capture: TacticalUnitObservationAggregate.Shared.Capture(allianceId)
              │     (~5ms — existing single-pass walk from Slice 1)
              │
              ├─ Publish snapshot to single-slot _pendingSnapshot atomic ref
              │     (volatile reference swap; worker reads on next wake)
              │
              ├─ Signal worker: _snapshotAvailable.Set()
              │
              └─ Read latest published decisions:
                  TacticalDecisionSnapshot.Current
                     │
                     ▼
                  per-tick patches lookup their (sideId, instanceId) → decision
                                                                    │
                                                                    │  (worker runs continuously)
                                                                    ▼
                                                          while (!_stopRequested) {
                                                            _snapshotAvailable.Wait();
                                                            snap = _pendingSnapshot;
                                                            if (snap == null) continue;

                                                            // Run all current orchestrator
                                                            // analysis: side.Tick, DriveTickCycle,
                                                            // DriveDirectChildCycle, DriveOperations-
                                                            // Ledger — but reading from the snapshot
                                                            // (no live vanilla state).
                                                            decisions = AnalyzeOnWorker(snap);

                                                            // Publish atomically
                                                            Interlocked.Exchange(
                                                              ref _currentDecisions, decisions);
                                                          }
```

### Components

**`TacticalDecisionSnapshot`** — new, immutable. Holds per-unit decisions plus per-side global decisions (current plan id, intent model, command-tree intent map). Indexed by `(allianceId, instanceId)` via inline dictionary. Pooled across cycles.

**`TacticalOrchestratorWorker`** — new. Owns the worker `Thread`, the `_snapshotAvailable` event, the `_stopRequested` flag, and the worker loop. Starts on `OnBattleStart`, stops gracefully on `OnBattleEnd` (signal + join with 250ms timeout, then abort if unresponsive).

**`TacticalDecisionPublisher`** — new, static. Holds the volatile reference to the current published `TacticalDecisionSnapshot`. Read on main thread from per-tick patches. Worker writes via `Interlocked.Exchange`. Reader uses `Volatile.Read` for memory-model safety on ARM (not strictly needed on x64 but cheap defensive practice; matches `volatile` field semantics).

**`TacticalBattleCoordinator.Tick`** (modify) — top-level dispatcher. Captures snapshot → publishes to worker → reads decisions back. The existing `DriveTacticalCommanderSide`, `DriveTickCycle`, etc. methods become **worker-thread internals** invoked from inside the worker loop with a snapshot argument instead of the live `AIBattle`.

**Per-tick patches** (modify):
- `BattleGroupStancePatch` — biggest win. Today: walks per-command-group visibility, builds sectors, runs `TacticalDoctrineScorer.DecideGroupStance` per group. Refactored: look up `TacticalDecisionSnapshot.Current.GetGroupStance(allianceId, instanceId)` and write via vanilla setter. Sub-ms.
- Other patches that currently call into orchestrator state (`ArmyOrchestrator.GetDirectChildRole`, `TacticalBattleCoordinator.GetSideOrchestrator`, etc.) — those reads now route through `TacticalDecisionSnapshot` instead of live orchestrator state. The orchestrator instance itself moves to worker-thread ownership.

### What stays on main thread

- Capture: `TacticalUnitObservationAggregate.Capture(allianceId)` — already optimized to a single-pass ~5ms walk.
- Decision read: `TacticalDecisionSnapshot.Current` — single volatile reference read.
- Decision apply: every Harmony patch that writes vanilla state (stance, movement, reserves, etc.).
- Telemetry emit: existing pattern — main thread enqueues, writer thread flushes (unchanged).

### What moves to worker

- All scoring (`TacticalDoctrineScorer`, `TacticalPlaybookCatalog`, `TacticalCommanderIntent`, `TacticalSectorReadinessDoctrine`).
- Intent inference (`ArmyIntentInference`).
- Plan / replan (`ArmyOrchestrator`, `ArmyReplanTriggers`, `ArmyTickCycle`).
- Role allocation (`CommandTreeIntentAllocator`, `DirectChildAllocator`).
- Operations ledger ticking (`TacticalOperationsLedger`).
- Sector evidence building, force balance, fog-of-war contact computations (over the snapshot).

These are all pure C# operating on the immutable snapshot — no Unity API calls. Thread-safe by construction.

## 4. Data structures

### `TacticalDecisionSnapshot`

```csharp
public sealed class TacticalDecisionSnapshot
{
    // Identifying which battle/cycle these decisions belong to
    public int BattleSequence { get; }
    public long CycleId { get; }       // monotonic worker-cycle counter
    public float CapturedAtBattleHours { get; }
    public float CapturedAtRealtimeSeconds { get; }

    // Per-side global state
    public TacticalSideDecision Side0 { get; }
    public TacticalSideDecision Side1 { get; }

    // Per-unit decisions, indexed by (allianceId, gameObjectInstanceId).
    // Backed by two dictionaries (one per alliance) for O(1) lookup with low allocation.
    // The exact set of TryGet methods is determined by the field-audit done as
    // the first step of the implementation plan: enumerate every per-unit read
    // the main-thread patches currently do that's derived from orchestrator
    // analysis (not from raw vanilla state), and expose each as a TryGet.
    // Initial set known from current patch survey:
    public bool TryGetGroupStance(int allianceId, int instanceId, out TacticalGroupStanceDecision decision);
    public bool TryGetDirectChildRole(int allianceId, int instanceId, out DirectChildRole role);
    public bool TryGetSectorAssessment(int allianceId, int instanceId, out TacticalSectorAssessment sector);
    public bool TryGetDirectChildIntent(int allianceId, int instanceId, out TacticalDirectChildIntent intent);
    public bool TryGetReactionContext(int allianceId, int instanceId, out TacticalReactionContext.Entry reaction);

    // Static "latest published" — read by main-thread patches
    private static TacticalDecisionSnapshot _current = TacticalDecisionSnapshot.Empty;
    public static TacticalDecisionSnapshot Current => Volatile.Read(ref _current);
    internal static void Publish(TacticalDecisionSnapshot next) => Interlocked.Exchange(ref _current, next ?? TacticalDecisionSnapshot.Empty);
    public static TacticalDecisionSnapshot Empty { get; } = new TacticalDecisionSnapshot(/* empty defaults */);
}

public sealed class TacticalSideDecision
{
    public int AllianceId { get; }
    public TacticalBattlePlan CurrentPlan { get; }
    public TacticalIntentModel CurrentIntent { get; }
    public CommandTreeSnapshot CommandTree { get; }   // latest registered tree
    public StrategicBattleIntentSnapshot StrategicIntent { get; }
    public ForceAvailabilitySnapshot ForceAvailability { get; }
    public PersonalityVector CommanderPersonality { get; }
    public ReplanTrigger LastReplanTrigger { get; }
}
```

**Pooling.** `TacticalDecisionSnapshot` instances are pooled per worker — the worker keeps a small free-list (e.g., 2 instances) so each cycle reuses an existing instance instead of allocating. The previous snapshot stays in `_current` until the worker swaps in the next one. Memory cost: bounded at 2 snapshots × ~kilobytes each.

### `TacticalOrchestratorWorker`

```csharp
internal sealed class TacticalOrchestratorWorker
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _snapshotAvailable = new(initialState: false);
    private volatile bool _stopRequested;
    private TacticalUnitObservationAggregate _pendingSnapshot; // single-slot, volatile via field-level memory barriers
    // Internal pool of TacticalDecisionSnapshot instances for reuse
    private readonly Queue<TacticalDecisionSnapshot> _decisionPool = new(2);

    public void Start();
    public void RequestStop();
    public void EnqueueSnapshot(TacticalUnitObservationAggregate snapshot);  // main thread
    private void WorkerLoop();  // worker thread
}
```

**Snapshot enqueue.** Single-slot: main thread overwrites `_pendingSnapshot` if worker hasn't consumed yet. Drop-old behavior. After overwriting, main thread signals `_snapshotAvailable.Set()`.

**Worker wake.** `_snapshotAvailable.Wait()` blocks until signaled. On wake, worker grabs `_pendingSnapshot` (single read), clears the slot, runs analysis, publishes the result. If main thread signaled multiple times while worker was busy, the worker processes only the LATEST `_pendingSnapshot` (older intermediates dropped).

**Worker stop.** `RequestStop()` sets `_stopRequested = true` and signals the event. Worker checks the flag at each loop iteration's top, exits cleanly. `OnBattleEnd` calls `RequestStop()` then `_thread.Join(250ms)`. If join times out, log warning and let the thread die naturally — at battle end the orchestrator state is cleared anyway.

## 5. Thread safety contract

| Variable | Thread that writes | Thread that reads | Protection |
|---|---|---|---|
| `_pendingSnapshot` | Main (Tick) | Worker (loop) | `Volatile.Write` / `Volatile.Read`. Single-slot, last-write-wins. |
| `_snapshotAvailable` | Main (set) + worker (reset) | Worker (wait) | `ManualResetEventSlim` (kernel + spin) |
| `_stopRequested` | Main (OnBattleEnd) | Worker (loop check) | `volatile bool` |
| `TacticalDecisionSnapshot._current` | Worker (Publish) | Main (Current getter) | `Interlocked.Exchange` (write) + `Volatile.Read` (read) |
| `TacticalUnitObservation*` (struct fields) | Captured once on main | Worker reads only | Immutable by struct definition |
| `_decisionPool` | Worker only | Worker only | Single-threaded access, no lock needed |

**No locks on the hot path.** All hand-offs are single-writer-single-reader via atomic reference or signal events.

**GC contention.** Worker allocations are minimized via the decision pool. Per cycle the worker allocates:
- Two `Dictionary<int, …>` (one per alliance) for per-unit decisions — pooled.
- Few primitive boxings (`TacticalGroupStanceDecision` structs put into dictionary).
- Result: per-cycle allocation count target ≤ 10 small objects.

Telemetry path is unchanged — the worker thread can emit telemetry events via `TelemetryRouter.Emit` (already thread-safe per project memory).

## 6. Parity verification

Extends the Slice 1 parity-window mechanism:

```csharp
if (Plugin.SinglePassParityWindowActive || Plugin.TacticalWorkerParityWindowActive)
{
    // Existing: capture snapshot. (already does this)
    var snap = TacticalUnitObservationAggregate.Shared.Capture(allianceId);

    // Run BOTH worker-equivalent analysis on main thread AND publish snapshot to worker.
    // The synchronous-on-main result is what we apply this tick.
    var synchronousDecisions = AnalyzeOnMainSynchronously(snap, allianceId);

    // Also publish to worker; worker produces independent result we COMPARE.
    _worker.EnqueueSnapshot(snap);

    // After N main-thread ticks, compare worker's published decisions vs the synchronous decisions
    // recorded at the same cycle. Mismatch emits TacticalOrchestratorParityMismatch.
}
```

**Parity emit.** New telemetry event `TacticalOrchestratorParityMismatch` (Tactical/Gate). Fields: `cycleId`, `allianceId`, `firstDiffField`, `firstDiffInstanceId`, `synchronousValue`, `workerValue`. Same shape as `TacticalObjectiveRecordsParityMismatch`.

**Window exit.** Per-`(battleSequence, allianceId)` 20 clean cycles → flip to worker-only (synchronous path no longer runs). Mismatch → that key stays on synchronous path for the rest of the battle.

**Test harness.** The worker's analysis pipeline is itself pure (operates on the immutable snapshot). Harness tests:
- Feed a known synthetic snapshot to `AnalyzeOnWorker(snapshot)` directly.
- Assert the resulting `TacticalDecisionSnapshot` has expected per-unit decisions.
- Compare a snapshot-driven run to the legacy synchronous run on the same input; should be bit-identical.

## 7. Rollback config flag

```csharp
EnableTacticalOrchestratorWorker = Config.Bind(
    "Tactical Orchestrator",
    "Enable Tactical Orchestrator Worker",
    true,
    "Background-thread orchestrator analysis. Main thread captures a "
    + "snapshot and reads pre-computed decisions; analysis runs on a "
    + "dedicated worker thread. Default ON per AGENTS.md tactical policy. "
    + "Set false to roll back to synchronous on-main-thread analysis if "
    + "a regression appears. Performance characteristics documented in "
    + "docs/telemetry.md once smoke-verified.");
```

When `false`: `TacticalBattleCoordinator.Tick` calls the existing synchronous path directly. Worker thread is not started. Parity window also short-circuits.

## 8. Files to modify

| File | Status | Responsibility |
|---|---|---|
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalDecisionSnapshot.cs` | **NEW** | Immutable per-cycle decision snapshot + atomic `Current` accessor |
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalSideDecision.cs` | **NEW** | Per-side decision sub-record |
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalOrchestratorWorker.cs` | **NEW** | Worker `Thread`, snapshot intake, loop, decision publish |
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinator.cs` (pure partial) | **MODIFY** | Expose accessor for worker; coordinate lifecycle |
| `src/WhiskeyRealism/Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs` | **MODIFY** | `Tick` becomes capture → enqueue → read-decision-snapshot; `OnBattleStart` starts worker; `OnBattleEnd` stops worker |
| `src/WhiskeyRealism/Patches/BattleGroupStancePatch.cs` | **MODIFY** | Look up decision from `TacticalDecisionSnapshot.Current` instead of computing |
| `src/WhiskeyRealism/Patches/BattleCommandPostureExecutorPatch.cs` | **MODIFY** | Same pattern — read from buffer |
| `src/WhiskeyRealism/Plugin.cs` | **MODIFY** | New `EnableTacticalOrchestratorWorker` ConfigEntry |
| `src/WhiskeyRealism/Telemetry/TelemetryTagPolicy.cs` | **MODIFY** | Register `TacticalOrchestratorParityMismatch` event |
| `tests/WhiskeyRealism.Tests/TacticalDecisionSnapshotTests.cs` | **NEW** | Coverage for snapshot lookup contracts |
| `tests/WhiskeyRealism.Tests/TacticalOrchestratorWorkerTests.cs` | **NEW** | Coverage for worker analysis given synthetic snapshots; parity check against synchronous path |
| `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` | **MODIFY** | `<Compile Include>` for the 3 new production files (test files auto-include per SDK) |
| `docs/telemetry.md` | **MODIFY** | Document new scopes (`tactical.worker.*`) + parity event |
| `docs/tactical-orchestrator.md` | **MODIFY** | Threading architecture section |
| `docs/handoff.md` | **MODIFY** | Post-smoke: shipped DLL hash + metric deltas |

No worker-thread-specific Harmony patches needed — the worker reads what main captured, doesn't touch vanilla directly.

## 9. New telemetry scopes

| Scope | Wraps | Notes |
|---|---|---|
| `tactical.worker.cycle` | Full worker analysis (one cycle) | Reports worker-side cost; doesn't affect main p99 |
| `tactical.worker.cycle.scoring` | Doctrine + playbook scoring | Sub-scope |
| `tactical.worker.cycle.allocation` | Role + intent allocation | Sub-scope |
| `tactical.worker.cycle.publish` | Atomic publish to `_current` | Should be sub-ms |

Plus the existing main-thread scopes continue to work — they now measure cheap capture + buffer-read instead of full work.

## 10. Success criteria

| Metric | Baseline (sha 601aa125) | Target |
|---|---|---|
| `tactical.orchestrator-tick` p99 (main thread) | 67ms | **≤ 10ms** post-window |
| `tactical.patch.battle-group-stance` p99 | 20.6ms | ≤ 3ms (buffer lookup + vanilla setter) |
| `tactical.worker.cycle` p99 (worker thread) | n/a | ≤ 200ms (no main-thread impact) |
| Parity mismatch events | n/a | 0 |
| Telemetry emit gaps > 1s | several per battle | 0 |
| Subjective hitch at 20× | "skipping never goes away" | absent or substantially reduced |
| Harness PASS | 1253 | baseline + 8-12 new tests |

## 11. Risks

| # | Risk | Likelihood | Mitigation |
|---|---|---|---|
| 1 | Boehm GC contention pauses main when worker allocates | M | Per-cycle worker allocations targeted ≤ 10 small objects; decision pool; dictionary reuse |
| 2 | Worker thread starves under heavy compression (decisions go stale) | L | Drop-old enqueue means worker only processes newest; staleness bounded at 1 main-tick |
| 3 | Race condition in decision read during worker's mid-cycle | L | Atomic ref swap on publish; main reads single complete snapshot or empty |
| 4 | Worker thread crashes silently (exception not surfaced) | M | Worker loop catches outer exceptions, emits `TacticalOrchestratorWorkerFault` telemetry, restarts inner loop after backoff |
| 5 | Lifecycle bug — worker leaks across battles | M | `OnBattleEnd` mandatory; `RequestStop` + `Join` with timeout; defensive `RequestStop` in `ClearForFailure` |
| 6 | First-tick decisions empty (worker hasn't produced yet) | L | Every patch has safe fallback (no-op); first ~50ms of battle the mod is passthrough |
| 7 | HarmonyX behavior on non-main thread | L | Worker doesn't invoke Harmony — only reads captured state + writes to decision buffer |
| 8 | Existing parity check infrastructure can't be extended | L | Already proven for ObjectiveRecords; same mechanism reused |

**Highest concern: Risk #1.** Worker allocations during GC trigger STW pauses. Mitigation is rigorous pooling. The harness tests will measure per-cycle allocation count by `GC.CollectionCount(0)` delta — same gcDelta machinery from Slice 1's diagnostic.

**Synchronous fallback path.** `AnalyzeOnMainSynchronously(snapshot, allianceId)` referenced in §6 is the existing `DriveTickCycle` + `DriveDirectChildCycle` + `DriveOperationsLedger` chain that today runs synchronously inside `TacticalBattleCoordinator.Tick`. The refactor doesn't delete this — it extracts those bodies into a single method that takes an `IObservationSource` instead of an `AIBattle` (snapshot-driven), and that method is callable from BOTH the worker thread AND the parity-window synchronous path on main. After one release cycle (no mismatches), the synchronous-on-main caller is removed.

## 12. Known follow-ups (explicit non-scope)

- **Slice 3 (potential):** Per-frame `ClosestVisibleEnemy` cache in `TacticalFogOfWarContact`. Benefits any remaining synchronous patches still doing visibility walks. Defer until smoke data shows whether it's needed.
- **Slice 4 (potential):** Move `ArmyEvidenceBuilder.Build`'s remaining walks to worker too. Currently only `BuildObjectiveRecordsFromBattle` was refactored in Slice 1.
- **Slice 5 (potential):** Remove the parity window code entirely after one release cycle of clean smoke.

## 13. AGENTS.md compliance checklist

- [x] Shipped-code/decompile-first: design built on existing `TacticalBattleSnapshotBuilder.Build`, `TacticalUnitObservationAggregate`, and `TelemetryWriter` worker pattern
- [x] Default-on per tactical policy: `EnableTacticalOrchestratorWorker = true`
- [x] Description string matches code default and does not over-claim measured performance
- [x] Try/catch around worker loop body; never throws past the worker boundary
- [x] Bounded logs (OnceLog for worker faults, bounded by exception type)
- [x] Rollback config flag documented in description + this design doc
- [x] No `transform.parent` walks for command hierarchy
- [x] Living docs to update on ship: `docs/tactical-orchestrator.md`, `docs/telemetry.md`, `docs/handoff.md`, `docs/patch-catalog.md`
- [x] Per-side dedup honored (existing battle-level dedup guard in `Tick` continues to fire before snapshot capture)
- [x] Telemetry on every Harmony patch (Slice 2 inherits the per-patch scope coverage from batches 1-3 of the diagnostic-completeness pass)

## 14. Decision log

| Decision | Choice | Reasoning |
|---|---|---|
| Threading primitive | Dedicated `Thread` | Matches `TelemetryWriter` pattern. Predictable lifecycle. No ThreadPool starvation under GC pressure. |
| Decision buffer | Per-tick atomic snapshot publish | Single-writer single-reader, sub-ns publish, O(1) per-unit lookup via dictionary. Simpler than per-unit concurrent dictionary. |
| Snapshot enqueue | Single-slot, drop-old | Worker always processes the freshest state. AI never blocks main. Maps to `TelemetryWriter`'s bounded-buffer-with-drop philosophy. |
| Parity model | Reuse Slice 1 mechanism extended | `(battleSequence, allianceId)` keys, 20 clean cycles, mismatch emits diff event. Battle-tested infrastructure. |
| Worker lifecycle | Start on `OnBattleStart`, join with timeout on `OnBattleEnd` | Clean ownership boundary; no leaks across battles. |
| Test approach | Pure-function tests against synthetic snapshots + parity comparison against synchronous baseline | Same pattern as Slice 1's `ObjectiveRecordsFromAggregateTests`. Worker analysis is pure and easy to test. |
