# Tactical Tick Optimization Design

**Date:** 2026-05-16  
**Author:** Grok (brainstorming session)  
**Status:** Design approved section-by-section; ready for user spec review before implementation planning  
**Scope:** Performance optimization of the tactical orchestrator tick path (existing O0–O3 + #61 operations-ledger workstream)  
**Constraint:** Must be no worse than vanilla’s own macro AI update latency (`sidestatupdatecycle`) for high-level re-planning

---

## 1. Problem Statement

In large tactical battles (especially at 20× speed), players experience 1–2 second hitches and general lag. The root cause is the per-tick cost of the Whiskey tactical orchestrator:

- `TacticalObserverPatch.CheckGlobalAIStrategyPostfix` (the per-AI-side macro tick) calls `TacticalBattleCoordinator.Tick`.
- This drives `DriveOperationsLedger` on every invocation when `Tactical Commander Mode` is Active or MonitorOnly.
- Each call performs two full `ArmyEvidenceBuilder.Build` + `TacticalVisionRuntimeAdapter.BuildObjectiveRecordsFromBattle` (completeunitlist walks + heavy reflection) + `TacticalOperationDirector` + battle-line / navmesh / doctrine assignment planners.
- At 20× speed the vanilla simulation fires far more `CheckGlobalAIStrategy` invocations per wall-clock second while our CPU cost per call remains constant.

Existing mitigations (signature skips in command tree / direct-child registration, `ArmyTickCycle.MaybeReplan` min-replan seconds) only cover part of the work. The operations-ledger heavy path has no equivalent throttle.

---

## 2. Vanilla Anchors (Decompile-First)

All design decisions are grounded in the following decompiled `Assembly-CSharp` behavior (`/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`):

- `AIBattle.CalculateSideStatsAndUpdateAITasks(bool overridelastupdate = false)` (~84570)  
  ```csharp
  if (GameVars.currenttimefromstart - lastsidestatupdate < GamePrefs.sidestatupdatecycle && !overridelastupdate)
      return;
  lastsidestatupdate = GameVars.currenttimefromstart;
  ...
  UpdateAITasks();
  ```
  Vanilla already throttles the entire macro AI block (side stats + strategy) behind `GameVars.currenttimefromstart` and `GamePrefs.sidestatupdatecycle`.

- `AIBattle.UpdateAITasks()` (~5857) calls the sequence that includes:
  - `CheckGlobalAIStrategy()` — our current #35 hook
  - `AdjustGroupAIStance()`
  - `AdjustGroupFormations()` — #61 hook
  - `CheckForFeudGroupActions()`

- `AIBattle.CheckGlobalAIStrategy()` (~6314) is the “macro-strategy entry” that fires once per AI side per `UpdateAITasks`.

- Pervasive pattern throughout tactical AI: `GameVars.currenttimefromstart - last*Time < GamePrefs.*` guards on `lastaichargetime`, `lastaistancechangetime`, `timeskirmisherwasdeployed`, `timegundetachmentwasdeployed`, retreat timers, macro AI changes, etc.

These anchors prove that Grand Tactician already accepts macro re-evaluation latency on the order of `sidestatupdatecycle` (typically several seconds of battle time) and uses per-entity last-action timestamps for throttling.

---

## 3. Reference Principle (Scourge of War — For Guidance Only)

Scourge of War Remastered and Gettysburg (identical SDK) use an engine-driven hierarchical `Think(int ticks, void*)` callback system (Brig/Div/Corp/Army/Side + dedicated Courier think). The engine calls these frequently; the AI implementation inside gates expensive grand-tactical work behind simulated game minutes using `CXUtil::GetTime()` + per-formation timestamps (`CourTime`, `HelpTime`, `PLAYFTime`) and constants (`TICSPERSEC=60`, `TICKS_WHEEL=15*TICSPERMIN`).

We adopt only the **principle**: frequent hook + internal game-time + signature gates on heavy re-planning. No SOW code or structures are used.

---

## 4. Chosen Approach

**Approach 1 — Signature + Game-Time Gated Heavy Path** (recommended and approved).

The frequent `CheckGlobalAIStrategy` Postfix remains the observation hook (lifecycle, telemetry, urgent recovery). The expensive side-wide planning work is moved behind a combined cheap signature check + `GameVars.currenttimefromstart` time gate modeled directly on vanilla’s `CalculateSideStatsAndUpdateAITasks` pattern.

This satisfies constraint C (“no worse than vanilla’s `sidestatupdatecycle` throttling”) by construction while preserving the responsive character of the shipped #61 operations-ledger system.

---

## 5. Detailed Design

### 5.1 Tick Cadence Split

**Frequent / Cheap Path** (runs on nearly every `CheckGlobalAIStrategy`):
- Lifecycle detection (existing detector)
- Cheap `TacticalBattleStateSignature` evaluation
- Urgent local recovery using the *last published* `OperationsLedger` + `CommandDoctrineOrder` snapshot + live vanilla state
- Lightweight intent observation (non-replan parts)
- All existing `TelemetryPerf` scopes and `OnceLog` markers

**Throttled / Heavy Path** (only when gate allows):
- Both `ArmyEvidenceBuilder.Build` calls
- `TacticalVisionRuntimeAdapter.BuildObjectiveRecordsFromBattle` (full list walks + reflection + approach avenues + visible enemy line + movement anchors)
- `TacticalOperationDirector.Decide`, `TacticalBattleLinePlanner`, `TacticalDefensiveLineAnchorPlanner`, `TacticalNavMeshPlanner`, `CommandDoctrineAssignment.Build`, `TacticalNestedDivisionPlayPlanner`
- Full battlefield picture + fresh doctrine order generation

### 5.2 Battle State Signature (Pure, Testable)

`TacticalBattleStateSignature` (new pure struct):
- `ActiveUnitCount`
- `SideTotalActiveForce[2]`
- `MacroAI[2]`
- `AnySideInRetreatOrEOD`
- `MajorObjectiveAnchorHash` (cheap hash of top 1–2 objectives)
- `AnyInterruptedPathsOrNewContact` (lightweight flag)
- `BattleDayHourBucket`

Implements `SignatureEquals` following the existing `DirectChildEvidence` and command-tree patterns. Computed before any heavy reflection.

### 5.3 Heavy Path Gate (Pure + Runtime)

New pure helper `TacticalHeavyPathGate.ShouldRunHeavyPath(signature, side, currentTime, lastHeavyTime, cycle)` returns true only when:
- Signature differs from last recorded signature for the side, **AND**
- `currentTime - lastHeavyTime[side] >= cycle`

`currentTime` source: `GameVars.currenttimefromstart` (same as vanilla `lastsidestatupdate`).

Storage: `lastHeavyReviewTime[2]` + `lastSignature[2]` per `TacticalBattleCoordinator` (or per `ArmyOrchestrator` if finer granularity is needed).

### 5.4 Config Surface (Default-Off)

```ini
[TacticalTickOptimization]
Enable Tactical Heavy Path Throttling = false
Heavy Ledger Review Cycle Seconds = 12.0   # battle time
```

When disabled, the gate is bypassed (100% current behavior). Cycle is read at runtime.

### 5.5 Telemetry

- Existing `TelemetryPerf.Scope("tactical.orchestrator-tick")`, `"tactical.operations-ledger"`, `"tactical.command-assignment"` (2 ms threshold) remain and will show the improvement.
- New bounded markers (appear in `TacticalTuning` / `FullTuning` profiles):
  - `[TacticalHeavyGate] skipped reason=time+signature side=0 cycle=12.0`
  - `[TacticalHeavyGate] executed heavy path side=1`

### 5.6 Responsiveness Guarantees (Constraint C)

- High-level side-wide doctrine re-planning latency ≤ vanilla `sidestatupdatecycle` (or better when signature is unchanged).
- Local urgent recovery (#61 posture executor, stuck-order handling, close-flank formation fixes, local fallback) remains on the frequent `AdjustGroupFormations` / `CheckGlobalAIStrategy` cadence using the last published snapshot + live vanilla state.
- A command node under sudden pressure can still receive a `GuardFlank`, `FallBackToLine`, or formation correction without waiting for the next full side-wide re-plan.
- At 20× speed the wall-clock CPU cost of the heavy path is bounded by the real-time gate, eliminating 1–2 s hitches.

---

## 6. Implementation Outline

- Add `TacticalBattleStateSignature` and `TacticalHeavyPathGate` as pure classes (testable).
- Add storage and gate calls only in the runtime partials of `TacticalBattleCoordinatorRuntime` and `TacticalOperationsLedgerRuntime`.
- No changes to any `Patches/` file.
- Update `Plugin.cs` with the two new `ConfigEntry` values.
- Add pure unit tests for signature and gate logic.
- Extend existing `TelemetryPerf` usage (no new infrastructure).

---

## 7. Testing & Verification (Mandatory for DLL Changes)

1. `./build.sh` → clean, `0` warnings.
2. Deploy + `sha256sum` verification of both `dist/WhiskeyRealism.dll` and the BepInEx plugins copy.
3. Console harness ≥ current PASS count (new gate/signature tests added).
4. Fresh GTCW launch, `Tactical Commander Mode = Active`, throttling enabled.
5. Large battle at 1× and 20×:
   - No 1–2 s hitches.
   - `performance.jsonl` shows dramatic reduction in `tactical.operations-ledger` / `command-assignment` cost on most ticks.
   - Heavy gate “skipped” markers appear frequently.
   - Doctrine and urgent recovery continue to function for close threats.
6. Focused smoke with throttling on vs off to prove constraint C.

Rollback: set boolean to `false` (or delete section) — zero behavior change.

---

## 8. Risks & Mitigations

- **Risk:** Signature is too noisy → too many heavy executions.  
  **Mitigation:** Coarse fields + explicit noise tolerance tests; can be tuned via cycle value.

- **Risk:** Very sudden global crisis takes longer to receive a brand-new side-wide plan.  
  **Mitigation:** Local urgent recovery still works immediately; constraint C guarantees we are no worse than vanilla.

- **Risk:** 20× speed interacts badly with realtime-based gate.  
  **Mitigation:** Gate uses `GameVars.currenttimefromstart` (battle time), which scales with simulation speed; wall-clock cost is still bounded.

---

## 9. Documentation Updates (Post-Ship)

- `docs/tactical-orchestrator.md`
- `docs/tactical-operations-ledger.md`
- `docs/handoff.md` (active workstream)
- `MEMORY.md`
- `docs/superpowers/specs/archive/` move of this spec after successful smoke

---

## 10. Summary of Design Decisions

- Use vanilla’s own `GameVars.currenttimefromstart` + `last*Time` + `GamePrefs.*` pattern for the time gate.
- Keep urgent reaction on the frequent path (preserves #61 character).
- Signature provides additional early-out on top of time (can only improve on vanilla).
- Default-off until smoke complete.
- Pure + runtime split maintained.
- No new Harmony patches.

This design solves the reported lag while staying strictly inside Grand Tactician’s existing architecture and latency expectations.

---

**End of Design Document**