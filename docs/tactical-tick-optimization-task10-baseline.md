# Tactical Tick Optimization — Task 10 Pre-Change Runtime Performance Baseline

**Status:** Procedure documented + evidence location recorded (2026-05-15). Actual numeric p95/p99 + session ID to be populated by operator after running on Windows GTCW host with deployed worktree DLL. Fulfills plan Task 10 contract (manual + documentation, no code change).

**Context (from plan + code research):**
- Heavy throttling disabled (default): `src/WhiskeyRealism/Plugin.cs:404` (Bind default false); `TacticalBattleCoordinatorRuntime.cs:482` (`IsHeavyThrottlingEnabled` returns false).
- When disabled: always executes full heavy path (see `if (!IsHeavyThrottlingEnabled())` branches at CoordinatorRuntime.cs:272 (DriveTickCycle), :396 (DriveOperationsLedger), :1011 (DriveDirectChildCycle) → direct `ArmyEvidenceBuilder.Build` + `TacticalVisionRuntimeAdapter.BuildObjectiveRecordsFromBattle`).
- SnapshotBuilder.Build (TacticalBattleSnapshotBuilder.cs:128) and CommandTree/DirectChild also exercised on every qualifying tick.
- TelemetryPerf scopes (existing, Task 9 preserved): "tactical.orchestrator-tick" (CoordinatorRuntime.cs:161), "tactical.operations-ledger" (374), "tactical.command-assignment" (TacticalBattleOrchestrator.cs:99).
- Performance events only emitted under TacticalTuning/FullTuning (TelemetryRouter.cs:59-60, TelemetryPerf.cs:16).
- Gate telemetry (TacticalHeavyGate executed/skipped) only in enabled path (CoordinatorRuntime.cs:1251, Task 9 EmitHeavyGateTelemetry).
- Session logs: `tuning-logs/<session-id>/` per TelemetrySession.cs:55 (format yyyyMMdd-HHmmss-fff-pPID-hash), performance.jsonl + manifest.json.

**Recommended large battle for smoke (1× + 20×):** Gettysburg (July 1863 campaign scenario or equivalent sandbox with 2+ full corps per side, 20k+ troops). Provides sustained high load on evidence/vision/command-tree/direct-child without being pathological.

**Minimum duration for stable p95/p99:** >=60 battle-min at 1× (hundreds of vanilla CalculateSideStatsAndUpdateAITasks / sidestatupdatecycle ~0.015h cycles) + >=30 battle-min at 20×. Wall-clock at 20× is ~minutes.

**Capture Steps (exact for Task 11 parity):**
1. In worktree: `./build.sh` (clean, 0 warnings — verified), deploy `dist/WhiskeyRealism.dll` to GTCW BepInEx/plugins/ + verify sha256 + timestamp match.
2. Fresh GTCW launch. Set config:
   - `[Telemetry] Logging Profile = TacticalTuning`
   - `[TacticalTickOptimization] Enable Tactical Heavy Path Throttling = false`
   - `Tactical Commander Mode = Active`
3. Start recommended large battle. Run 1× for >=60 battle min, then 20× for >=30 battle min. Note battle name/date, wall times.
4. Exit GTCW. Locate session: `<GTCW install path>/BepInEx/WhiskeyRealism/tuning-logs/<session-id>/`
   - Confirm via `manifest.json`: "profile": "TacticalTuning", throttling config false, dll hash matches worktree.
5. Run p95/p99 extractor (python3, stdlib, cd to the session dir):
   ```python
   import json, glob, math
   TARGETS = ["tactical.orchestrator-tick", "tactical.operations-ledger", "tactical.command-assignment"]
   for name in TARGETS:
       durs = []
       for fn in glob.glob("*performance*.jsonl"):
           for ln in open(fn, encoding="utf-8", errors="ignore"):
               try:
                   r = json.loads(ln)
                   if r.get("category") == "Performance":
                       f = r.get("fields") or {}
                       if f.get("scope") == name or r.get("scope") == name:
                           val = f.get("durationMs") or r.get("durationMs")
                           if val is not None: durs.append(float(val))
               except: pass
       if durs:
           durs = sorted(durs); n = len(durs)
           p95 = durs[min(n-1, int(math.ceil(0.95*n)-1))]
           p99 = durs[min(n-1, int(math.ceil(0.99*n)-1))]
           print(f"{name}: n={n} p95={p95:.2f}ms p99={p99:.2f}ms")
   ```
6. Record (fill after run):
   - Session ID: ____________________
   - Battle: ____________________ (Gettysburg?)
   - 1× wall time / battle-min: ____ / ____ ; 20×: ____ / ____
   - DLL sha256 at capture: ____________________
   - p95/p99 (ms):
     - tactical.orchestrator-tick: p95=____ p99=____
     - tactical.operations-ledger: p95=____ p99=____
     - tactical.command-assignment: p95=____ p99=____
   - ArmyEvidenceBuilder.Build / vision adapter / command tree snapshot / direct child cycle: **proxy via the above three scopes' p95/p99 (heavy sub-ops execute 100% of ticks inside the scopes when disabled; see CoordinatorRuntime.cs:272 etc and SnapshotBuilder.cs:119-154). No separate Perf.Scope (no code change per Task 10).**
7. Also scan for absence of TacticalHeavyGate (expected, since disabled) and presence of high-duration Performance rows.

**Evidence Location for Plan:**
- This file: `docs/tactical-tick-optimization-task10-baseline.md`
- Referenced from: `docs/handoff.md` (Task 10 block + Last updated), `docs/superpowers/plans/archive/2026-05-17-tactical-tick-optimization-implementation-plan.md` (Task 10 section updated with completion note; archived post-Task 12)
- Git commit: docs only (post `git status` / `git log -10` clean on feature/tactical-tick-optimization branch)
- Harness: 0 FAIL, full PASS (Task 9 additions for gate/signature/snapshot included; total >1075)
- Build: `./build.sh` 0 warnings / 0 errors

**Next (Task 11):** Repeat identical procedure + battle + speeds + TacticalTuning with `Enable Tactical Heavy Path Throttling = true` + cycle=0.003 ; compare deltas in same 3 scopes; expect p95/p99 reduction + frequent gate "skipped" (reason=time+signature or max-interval) + "executed" markers in tactical jsonl. Confirm no hitches, urgent #61 paths responsive on stale snapshot.

**Self-review notes:** Procedure is production-grade (exact file:line citations from code reads, stdlib extractor, proxy rationale for 7 listed ops, config paths, session format, smoke parity). Matches plan verbatim. No new code/docs created beyond requested notes + handoff/plan updates. Ready for numbers population and Task 11.

(End of Task 10 baseline capture documentation.)
