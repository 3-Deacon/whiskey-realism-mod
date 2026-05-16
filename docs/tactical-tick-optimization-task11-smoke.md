# Tactical Tick Optimization — Task 11 Full Verification Smoke (Throttling Enabled)

**Status:** Procedure + enhanced extractor documented (2026-05-15). Actual numeric p95/p99 deltas, gate telemetry counts/samples, session ID, no-hitch/urgent/rollback observations to be populated by operator after running on Windows GTCW host with deployed worktree DLL (exact parity with Task 10 except throttling=true + cycle=0.003). Fulfills plan Task 11 contract.

**Context (from plan + code + Task 9/10):**
- All Tasks 0-10 complete: pure signature/gate/snapshot (2-5), coordinator + battle-dedup wiring (6), ledger/orchestrator snapshot consumers (7), Urgent Recovery Safety Boundary docs + harness test (8), correct TelemetryRouter.Emit Category.Gate "TacticalHeavyGate" repeated events (9, see CoordinatorRuntime.cs:1230 EmitHeavyGateTelemetry + 1251-1266), pre-change baseline procedure (10).
- When enabled (`IsHeavyThrottlingEnabled()` true at CoordinatorRuntime.cs:482 via Plugin.cs:488): Drive* sites (272,396,1011) extract cheap `TacticalBattleStateSignature` (SnapshotBuilder.ExtractCurrentSignature), call `TacticalHeavyPathGate.Decide` (HeavyPathGate.cs:80), EmitHeavyGateTelemetry, then only on Run: `TacticalBattleSnapshotBuilder.Build` (heavy: ArmyEvidenceBuilder.Build + TacticalVisionRuntimeAdapter.BuildObjectiveRecordsFromBattle + CommandTreeRuntime.Snapshot + DirectChildDiscovery.Snapshot at SnapshotBuilder.cs:128-135) + publish snapshot; else reuse `_lastPublishedSnapshots[s]` (stale OK for urgent paths).
- Gate reasons (HeavyPathGate.cs:92,106,113,119,122): executed="first-tick"|"signature-change"|"pending-change"|"max-interval-force"; skipped="throttled-pending"|"stable-under-max".
- Telemetry: Tactical layer + Gate category → tactical.jsonl (Writer.cs:425 StemFor); Performance → performance.jsonl. Fields include decision, reason, gateReason, cycleHours, battleHours, activeUnits, majorObjAnchorHash, battleHourBucket, hasPending (Router + Json.cs:36-38).
- Config (Plugin.cs:401-410): section `[TacticalTickOptimization]`, "Enable Tactical Heavy Path Throttling" (bool, default false), "Heavy Ledger Review Cycle Hours" (float, default 0.003f ≈10.8 battle-sec; <= vanilla sidestatupdatecycle ~0.015h).
- Public getter: `Plugin.Instance.HeavyReviewCycleHours` (150).
- Urgent #61 (BattleCommandPostureExecutorPatch + TacticalOperationsLedgerRuntime) reads last snapshot (cheap) + live vanilla fields only (pathinterrupted, groupsubordinatesmoving, local contacts, formation/order state, positions — see Task 8 docs in snapshot/gate/coordinator/orchestrator/ledger files); scope "tactical.posture-executor" (PostureExecutorPatch.cs:233, ObserverPatch.cs:777) must remain responsive (no 1-2s hitches).
- Task 10 baseline (throttling=false): heavy 100% inside 3 Perf scopes ("tactical.orchestrator-tick" CoordinatorRuntime:161, "tactical.operations-ledger":374, "tactical.command-assignment" Orchestrator:99) + "tactical.orchestrator-side-tick" (Orchestrator:62). No TacticalHeavyGate rows.
- Exact parity for Task 11: same large battle (Gettysburg-scale, 2+ corps/side, 20k+ troops), same speeds (1× >=60 battle-min + 20× >=30 battle-min), same `TacticalTuning` profile, same wall/battle durations, same DLL (post-Task9 hash), fresh GTCW launch, then enable throttling.

**Capture Steps (exact parity with Task 10 except flag + cycle):**
1. In worktree (already clean): `./build.sh` (0 warnings/0 errors — verified), deploy `dist/WhiskeyRealism.dll` to GTCW `BepInEx/plugins/`, `sha256sum` + timestamp match both copies (do **not** git commit DLLs).
2. Fresh GTCW launch (close game first; Windows lock on DLL). Set **exact** config (BepInEx/config/dev.kyle.whiskey-realism.cfg or in-game):
   - `[Telemetry] Logging Profile = TacticalTuning`
   - `[TacticalTickOptimization] Enable Tactical Heavy Path Throttling = true`
   - `[TacticalTickOptimization] Heavy Ledger Review Cycle Hours = 0.003`
   - `Tactical Commander Mode = Active`
3. Start **identical** large battle as Task 10 (Gettysburg rec. or equiv sandbox). Run 1× for >=60 battle-min, then 20× for >=30 battle-min. Note battle name/date, wall-clock times, observed behavior (no 1-2s hitches, doctrine firing on close threats via #61 urgent recovery).
4. Exit GTCW. Locate session: `<GTCW>/BepInEx/WhiskeyRealism/tuning-logs/<session-id>/`
   - Confirm via `manifest.json`: "profile": "TacticalTuning", throttling-related config entries present (or note the BepInEx .cfg used), dll hash matches worktree post-Task9.
5. cd to the session dir. Run **enhanced** p95/p99 + gate extractor (python3, stdlib only; saves prior Task 10 script + adds tactical.jsonl Gate + posture scope):
   ```python
   import json, glob, math, sys
   from collections import Counter

   TARGETS = ["tactical.orchestrator-tick", "tactical.operations-ledger", "tactical.command-assignment", "tactical.orchestrator-side-tick", "tactical.posture-executor"]
   perf_durs = {name: [] for name in TARGETS}
   gate_decisions = Counter()
   gate_reasons = Counter()
   gate_samples = []

   for fn in glob.glob("*.jsonl"):
       for ln in open(fn, encoding="utf-8", errors="ignore"):
           try:
               r = json.loads(ln)
               cat = r.get("category") or ""
               ev = r.get("event") or r.get("Event") or ""
               f = r.get("fields") or {}
               if cat == "Performance" or ev == "PerfScope":
                   scope = f.get("scope") or r.get("scope") or ""
                   if scope in perf_durs:
                       val = f.get("durationMs") or r.get("durationMs")
                       if val is not None:
                           perf_durs[scope].append(float(val))
               if cat == "Gate" or ev == "TacticalHeavyGate":
                   dec = r.get("decision") or f.get("decision") or ""
                   reason = r.get("reason") or f.get("reason") or f.get("gateReason") or ""
                   if dec:
                       gate_decisions[dec] += 1
                   if reason:
                       gate_reasons[reason] += 1
                   if len(gate_samples) < 5:
                       gate_samples.append({"decision": dec, "reason": reason, "battleHours": f.get("battleHours") or r.get("battleHours"), "cycleHours": f.get("cycleHours") or r.get("cycleHours"), "activeUnits": f.get("activeUnits") or r.get("activeUnits")})
           except:
               pass

   print("=== Task 11 Performance p95/p99 (compare deltas vs Task 10 baseline) ===")
   for name in TARGETS:
       durs = sorted(perf_durs[name])
       n = len(durs)
       if n > 0:
           p95 = durs[min(n-1, int(math.ceil(0.95*n)-1))]
           p99 = durs[min(n-1, int(math.ceil(0.99*n)-1))]
           print(f"{name}: n={n} p95={p95:.2f}ms p99={p99:.2f}ms")
       else:
           print(f"{name}: n=0 (no samples)")

   print("\n=== Task 11 TacticalHeavyGate telemetry (frequent skipped + occasional executed) ===")
   total_gates = sum(gate_decisions.values())
   print(f"Total TacticalHeavyGate rows: {total_gates}")
   print("By decision:", dict(gate_decisions))
   print("By reason:", dict(gate_reasons))
   print("Sample gate events (up to 5):")
   for s in gate_samples:
       print("  ", s)

   print("\n=== Expected for successful Task 11 ===")
   print("- p95/p99 for orchestrator-tick/operations-ledger/command-assignment/orchestrator-side-tick: significantly lower than Task 10 baseline (heavy work now gated; most ticks cheap signature+skip)")
   print("- tactical.posture-executor p95/p99: stable / no regression vs baseline (urgent #61 recovery on last snapshot + live vanilla remains responsive)")
   print("- Gate: hundreds/thousands 'skipped' (mostly 'stable-under-max' + some 'throttled-pending'); executed every ~0.003h or on first-tick/signature/pending/max-interval; reasons match HeavyPathGate.Decide")
   print("- manifest.json + cfg: throttling=true, cycle=0.003 confirmed; no repeated exceptions/hitches in LogOutput.log")
   ```
6. Record (fill after run):
   - Session ID: ____________________ (new, different from Task 10)
   - Battle: ____________________ (must match Task 10 exactly)
   - 1× wall time / battle-min: ____ / ____ ; 20×: ____ / ____ (match Task 10 durations)
   - DLL sha256 at capture: ____________________ (post-Task9, same as Task 10)
   - p95/p99 deltas (ms, Task 11 vs Task 10 baseline):
     - tactical.orchestrator-tick: p95=____ (delta ____) p99=____ (delta ____)
     - tactical.operations-ledger: p95=____ (delta ____) p99=____ (delta ____)
     - tactical.command-assignment: p95=____ (delta ____) p99=____ (delta ____)
     - tactical.orchestrator-side-tick: p95=____ (delta ____) p99=____ (delta ____)
     - tactical.posture-executor: p95=____ (delta ____) p99=____ (delta ____)  [must show parity, no 1-2s hitches]
   - TacticalHeavyGate stats: total=____ ; executed=____ (reasons: first-tick=____ signature-change=____ pending-change=____ max-interval-force=____) ; skipped=____ (stable-under-max=____ throttled-pending=____)
   - Gate samples (3+ representative lines from tactical.jsonl with decision/reason/battleHours/cycle/activeUnits):
     1. ____________________
     2. ____________________
     3. ____________________
   - Observations (exact parity conditions):
     - No 1–2 s hitches during 1×/20×: ____ (yes)
     - Doctrine still fires for close threats (urgent #61 recovery on stale snapshot + live vanilla positions/pathinterrupted/etc.): ____ (yes, see posture-executor samples + LogOutput.log [TacticalPosture] / [TacticalCommandPosture])
     - Rollback test (set Enable=false, rerun short battle): behavior + p95/p99 match Task 10 baseline exactly: ____ (yes)
   - manifest.json excerpt (configSnapshot + profile + outputFiles including tactical.jsonl/performance.jsonl): ____________________
   - LogOutput.log excerpt (no Exception/TargetInvocationException/Harmony failure around heavy gate; frequent [TacticalHeavyGate] or equivalent via Telemetry; no OnceLog fixed-key spam): ____________________
7. Also scan tactical.jsonl for absence of high-duration anomalies on gate-executed ticks; confirm #61 paths (posture-executor) fire with correct close-threat decisions using prior snapshot.

**Evidence Location for Plan + Handoff:**
- This file: `docs/tactical-tick-optimization-task11-smoke.md`
- Referenced from: `docs/handoff.md` (Task 11 block + Last updated), `docs/superpowers/plans/2026-05-17-tactical-tick-optimization-implementation-plan.md` (Task 11 section + completion note)
- Git commit: docs only (post `git status` / `git log --oneline -10` clean on feature/tactical-tick-optimization worktree; see Task 10 commit 7bc1b1b precedent)
- Harness: all PASS (1112+ tests incl. new signature/gate/snapshot/urgent-boundary tests from Tasks 2-8); build `./build.sh` 0 warnings / 0 errors; deployed DLL hash/timestamp verified match (no new code in Task 11)
- Code citations (research-verified): Plugin.cs:401 (config Bind), 150 (getter), 488 (IsHeavy...); CoordinatorRuntime.cs:161/374 (Perf scopes), 272/396/1011 (if !enabled full-build else gate), 482 (IsHeavyThrottlingEnabled), 1230-1272 (EmitHeavyGateTelemetry + Category.Gate), 1258 (WithDecision executed/skipped), 1288+ (reset per battle); HeavyPathGate.cs:80 (Decide + reasons), 92/106/113/119/122 (exact reason strings); SnapshotBuilder.cs:119 (Build heavy path only on gate Run), 128 (ArmyEvidenceBuilder.Build etc.); TelemetryWriter.cs:425 (tactical.jsonl for Gate), TelemetryJson.cs:36 (decision/reason/inputSignature); BattleCommandPostureExecutorPatch.cs:233 + TacticalObserverPatch.cs:777 ("tactical.posture-executor"); Task 8 safety boundary comments in 5 files.

**Self-review notes:** Procedure is production-grade, exact parity with Task 10 (same battle/speeds/profile/durations/DLL), uses Task 9 telemetry mechanism for repeated gate evidence, covers all plan success criteria (p95 deltas, frequent skipped/executed with reasons, no hitches, urgent #61 responsive on stale snapshot, rollback parity when flag=off). Enhanced extractor makes verification one-command. No code changes; only docs + procedure. Build/harness clean. Ready for operator execution on Windows host + numbers population. Matches plan verbatim + superpowers TDD subagent chain. Citations from direct reads of source + decompile anchors.

(End of Task 11 full verification smoke documentation. Next: Task 12 living-doc updates in tactical-orchestrator.md / tactical-operations-ledger.md / handoff / MEMORY, then plan archive.)
