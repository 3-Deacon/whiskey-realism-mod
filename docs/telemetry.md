# Whiskey Realism Telemetry

**Default profile is `TacticalTuning`** (user direction, 2026-05-18): battles produce diagnostic sidecars by default. Disk usage is bounded by `Max Tuning Log MB` (default 250) and `Tuning Log Retained Sessions` (default 2, newest kept), so this won't grow without limit. Set `Logging Profile = Off` in `<GTCW>/BepInEx/config/dev.kyle.whiskey-realism.cfg` for quiet operation.
Tuning profiles write under `<GTCW>/BepInEx/WhiskeyRealism/tuning-logs/<session-id>/`.

Source: `src/WhiskeyRealism/Telemetry/`. Every `[Telemetry]` flag defaults ON (`python3 -I tools/config-audit.py .` reports 0 findings).

## Profiles and config

Profiles: `Off`, `TacticalTuning` (battle rows), `CampaignTuning` (campaign rows), `FullTuning` (both). The profile picks which rows reach the sidecars; it does not change any tactical or campaign behavior.

`[Telemetry]` keys:
- `Logging Profile = TacticalTuning`
- `Max Tuning Log MB = 250` (per-session cap, 25-2000)
- `Tuning Log File Rotate MB = 25` (approximate JSONL rotation size)
- `Tuning Log Retained Sessions = 2`
- `Emit Human Summary = true` (`summary.md` at shutdown)
- `Telemetry Performance Warnings = true` (individual slow-scope rows; see Performance scopes)
- `Create Issue Bundle On Shutdown = true` (`issue-bundle.json` at shutdown)
- `Telemetry Queue Capacity = 8192` detail rows
- `Telemetry Flush Milliseconds = 250`
- `Telemetry Flush Rows = 256`

All of these are copied into each session's `manifest.json` under `configSnapshot`. `Telemetry Queue Capacity` is the detail-queue capacity; protected Health/Failure rows have a separate reserve and can exceed the detail cap.

Output policy:
- Tactical and campaign tuning rows go to JSONL sidecars only when a tuning profile is enabled. Tuning rows (`TacticalDecisionMatrix`, observer, operations-ledger, campaign, strategic) do not use `LogOutput.log` as their evidence stream.
- Serious warning/error conditions can still emit bounded `LogOutput.log` warnings.
- `Off` leaves the install silent and does not create `<GTCW>/BepInEx/WhiskeyRealism/tuning-logs/`.

Sidecar files:
- `tactical.jsonl`
- `campaign.jsonl`
- `performance.jsonl`
- `failures.jsonl`
- `health.jsonl`
- `manifest.json`
- `summary.md` when `Emit Human Summary` is on
- `issue-bundle.json` when `Create Issue Bundle On Shutdown` is on

Validation:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj -- --validate-telemetry "<session-dir>"
```

Issue bundles are a redacted index of the session's own telemetry files. They do not contain save files, copied game DLLs, tokens, unrelated plugin logs, or raw Windows usernames.

## Event tags

Tags are routed by `TelemetryTagPolicy`; unregistered `Tactical*` tags fall back to tactical Trace. Selected tactical events:

| Event | Category | Source |
|---|---|---|
| `TacticalDirectChildIntent` | Decision | `ArmyOrchestrator.EmitDirectChildIntentTelemetry`: per-side role distribution (main/sup/fix/scrn/rL/rR/rsrv/fbck/unk counts, per-child `ChildId=Role:Sector`, plan/phase/mainEffortSector/opportunity/currentRatio). Signature-deduped, 5 s throttle. |
| `TacticalPlayExecution` | Decision | `PlayExecutorRuntime.EmitPlayExecutionTelemetry`: per-side play summary when play assignments change (side, plan, template, rect/assigned counts, army/enemy centroid X/Z, frontage, per-corps `ChildId=RectKind`). Signature-deduped, 10 s throttle. |
| `TacticalMacroDecision` | Decision | #44 `BattleMacroStrategyPatch`: macroai write (`old`/`whiskey`/`ledger`/`fallback`/`source`) and decision lines, 30 s signature throttle. #44 is the only writer of `macroai`; there is no separate macroai write tag. |
| `TacticalStanceWrite` | Write | `TacticalStanceWriter.EnsureStance` via OnceLog (first write/yield/refuse per key; see OnceLog). |
| `TacticalBrigadePosture` | Decision | Posture producer via OnceLog (static key `tactical-brigade-posture:opportunity-override`): first leaf-brigade opportunity override per battle. |
| `TacticalCommandPosture` | Decision | Posture and division-play producers (`PostureTelemetry.Group`): per group decision, task, target, intents submitted (`writeResult=submitted`/`none`); per (source, group) signature, 30 s throttle. |
| `TacticalLeafCascade` | Decision | Posture producer (`PostureTelemetry.Leaf`): leaf brigade role/task/outcome, cascade chain, applied. |
| `TacticalFacingPulse` | Decision / Write | Facing-pulse producer (`PostureTelemetry.Facing`): scope, task, target formation/facing, `writeResult`. |
| `ChargeDemotion` | Decision | ChargeDemotion producer: a vanilla assault (stance 4) demoted, counted once the executor issued it (`ChargeDemotionTracker`): unit, `decision=DemoteToHold`, `reason=charge-demotion`, `fromStance`, `toStance`, `submittedAtHours`; per (side, group, stance) signature, 30 s throttle. |
| `ChargeDemotionCount` | Decision | ChargeDemotion producer: per side, at most every 30 s real time and only after a new demotion: `demoted` (window), `battleTotal`, `windowSeconds`. |
| `BattlePerception` | Decision | `BattlePerceptionProbe`: per-side perception mode change (mode, reason, visible, remembered). The once-per-mode `[BattlePerception]` line also goes to `LogOutput.log`. |
| `TacticalHeavyGate` | Gate | `TacticalBattleCoordinatorRuntime`: heavy snapshot-build gate decision per side (executed/skipped + reason, battle/real-time clocks, pending flag, unit count, objective hash). |

`TacticalHeavyGate` also carries GC pressure per side: `gcCountAbs` (absolute `GC.CollectionCount(0)`) and `gcDelta` (collections since the previous gate decision on that side). Mono Boehm is non-generational, so one counter suffices. Reset per battle in `ResetRuntimeTickState`. Correlate `gcDelta` with `tactical.snapshot-build*` timings: high `gcDelta` points to allocation churn, high duration with low `gcDelta` to algorithmic cost.

## OnceLog

`OnceLog.Info`/`Warning` emit one `[once:<key>] [<Tag>] ...` line per key; `TelemetryTagPolicy` routes on the first non-`once:` tag, so the line inherits that tag's routing.

Keys must be static: a literal, or a literal plus a value from a small fixed set (task, stance, source, reason, vanilla method name). Never put instance ids, unit names, signatures or other unbounded values in a key; the fired set lives for the whole process. Info keys are capped at 4096 (`OnceLog.MaxKeys`); keys starting with `tactical` are released at battle end so first-fire lines re-fire next battle.

| Key shape | Source | Routing |
|---|---|---|
| `tactical-stance-write:<source>:<reason>:<stance>` (also `:yield:` / `:refuse:` variants) | `TacticalStanceWriter.EnsureStance` | `TacticalStanceWrite` (Write) |
| `tactical-combatbehavior-write:<behavior>` | `TacticalCombatBehaviorWriter` | default OnceLog routing |
| `tactical-brigade-posture:opportunity-override` | posture producer, leaf opportunity override | `TacticalBrigadePosture` (Decision) |
| `tactical-battle-core:<event>` (`producers`, `producers-missing`, `arbiter-first-fire`, `charge-first-fire`, `charge-demotion-first-fire`, `recon-first-fire`, `probe-first-fire`, `skirmisher-screen-first-fire`, `ancestry-mismatch`, `snapshot-*`, `blocked-crossings-*`, `terrain-*`, …) and `tactical-battle-core:producer-threw:<producer name>` (twelve fixed names, keys built once by `BattleProducerRegistry`) | battle core | `[BattleCore]` / `[OrderArbiter]` / `[ChargeDoctrine]` / `[ChargeDemotion]` / `[BattleSnapshot]` lines: not a tactical tag, so they reach `LogOutput.log` |
| `tactical.battle-perception:mode:<FogGrid\|RangeOnly\|OwnSideWithinFog>` | `BattlePerceptionProbe` | `[BattlePerception]`, `LogOutput.log` |
| `tactical-feud-guard:<event>` | #42 | `TacticalFeudGuard` (Gate) |

## Performance scopes

`TelemetryPerf.Scope` is pooled and allocation-free. Per scope name, per thread:
- Calls below the threshold only update an accumulator, flushed as one `PerformanceAggregate` row per scope per 5 s of real time (`TelemetryPerf.AggregateWindowMs`). Windows also close from the frame-probe tick, and every thread's final window is flushed at shutdown.
- A call at/above its threshold also emits one `Performance` row (scope, slow, thresholdMs, queueDepth, eventsEmitted/Dropped, bytesWritten, ...), at most 8 per scope per window, and only while `Telemetry Performance Warnings = true`; otherwise it is counted only in the aggregate.
- On the main thread, a depth-0 scope whose layer the profile suppresses (e.g. `campaign.*` under TacticalTuning) is still timed for the frame probe, but emits no rows.

`PerformanceAggregate` row (category Performance): `durationMs` = max call ms in the window; fields `scope`, `aggregate=true`, `count`, `slowCount`, `slow` (slowCount > 0), `totalMs`, `maxMs`, `meanMs`, `thresholdMs`, `windowMs`.

Every Harmony `Prefix`/`Postfix`/`Finalizer` has a scope named `tactical.patch.<short-name>` (battle) or `campaign.patch.<short-name>` (campaign), with a `.<method>` suffix when a file has several entries; e.g. #44 is `tactical.patch.battle-macro-strategy.prefix` / `.postfix`. Threshold is 2.0 ms (5.0 ms for the arbiter patch `tactical.patch.battle-core-arbiter`). The scope name is also the patch's first-fire id (`PatchFirstFire.Mark`, `[first-fire] <id>` in `LogOutput.log`; `tools/smoke-check.py` reports declared ids that never fired). List the current names from code rather than from this doc:

```bash
grep -rhoE 'TelemetryPerf\.Scope\(\s*"[^"]+"' src/WhiskeyRealism | sort -u   # misses multi-line calls; see table
```

Non-patch scopes:

| Scope | Threshold | Wraps |
|---|---|---|
| `tactical.orchestrator-tick` | 2.0 ms | coordinator `Tick()` body (both sides) |
| `tactical.operations-ledger` | 2.0 ms | `DriveOperationsLedger` |
| `tactical.command-assignment` | 2.0 ms | `CommandNodeOperationsRuntime.Build` in `TacticalBattleOrchestrator` |
| `tactical.attach-direct-children` | 2.0 ms | `AttachDirectChildrenIfReady` |
| `tactical.direct-child-discovery-snapshot` | 2.0 ms | inner `DirectChildDiscovery.Snapshot(alliance)` |
| `tactical.attach-command-tree` | 2.0 ms | `AttachCommandTreeIfReady` |
| `tactical.command-tree-runtime-snapshot` | 2.0 ms | inner `CommandTreeRuntime.Snapshot(alliance)` |
| `tactical.snapshot-build` | 5.0 ms | `TacticalBattleSnapshotBuilder.Build` in `EnsureSideSnapshot` (shared by the tick, direct-child and operations-ledger drives) |
| `tactical.snapshot-build.evidence-bundle` | 5.0 ms | `ArmyEvidenceBuilder.Build` inside `Build` |
| `tactical.snapshot-build.objective-records` | 5.0 ms | objective records inside `Build` |
| `tactical.resolved-group-decisions.build` | 5.0 ms | per-group resolved decisions after a heavy build |
| `tactical.observer-pass` | 2.0 ms | `TacticalObserverPatch` observer pass |
| `tactical.play-executor.zone-assignments` | 5.0 ms | per-side play projection + corps assignment (cached for 3 s per side and plan) |
| `tactical.cache-validator.run` | 5.0 ms | `TacticalCacheValidator` run |
| `tactical.snapshot.build` | 5.0 ms | battle-core `BattleSnapshotBuilder.Build` (one per AI side per frame, at 5866 and #60's deployment) |
| `tactical.perception` | 2.0 ms | `BattlePerceptionProbe.Probe` (probe, memory update, slice build) after each snapshot build |
| `tactical.arbiter.resolve` | 0.5 ms | `OrderArbiter.Resolve` in the arbiter tick (#68) |
| `tactical.arbiter.execute` | 2.0 ms | outcomes, retries and winner execution in the arbiter tick |
| `tactical.producer.<name>` | 2.0 ms | each producer's `Produce` (`withdrawal`, `artillery`, `artillery-point-blank`, `group-stance`, `charge-demotion`, `posture` (+ `.nav-plan`), `facing-pulse`, `fire-control`, `play`, `reserve-release`, `charge`) |
| `tactical.dispatch.emit` | 2.0 ms | `TacticalPlayerDispatchRuntime.Emit` (W&L dispatch) |
| `tactical.terrain-grid.build` | 2.0 ms | BC10 `BattleTerrainGridRuntime.Get`: the one-time copy of the terrain-id array |
| `tactical.covered-crossings` | 0.5 ms | BC10 `CoveredCrossings.BuildPoints` once per snapshot when a move carries `AvoidCoveredCrossings` |
| `tactical.perceived-flanking` | 0.5 ms | BC10 `PerceivedFlanking.Apply` per snapshot build |
| `tactical.producer.posture.road-route` | 2.0 ms | BC10 `RoadRoutePlanner.Plan` (one plan per side per arbiter tick) |
| `tactical.patch.deployment-terrain.prefix`, `.postfix` | 5.0 ms | #60 deployment Prefix (positions) and Postfix (re-placement, reserve, terrain discipline) |
| `tactical.patch.arrival-guard.prefix`, `.finalizer` | 2.0 ms | #105 `ReinforcementArrivalGuardPatch` (ordinal assigned at merge to `main`), record-only |
| `campaign.patch.wl-campaign-dispatch.move`, `.flush` | 2.0 ms | #69: `MoveUnitTo` record, `UpdateUnitAI` classify/emit + compliance observation |
| `campaign.patch.cic-faction-ai-guard.*` | 2.0 ms | #70: arm/disarm around `CheckShipConstruction` / `CheckArmyGroupManagement`, `IsCommanderInChief` answer |
| `tactical.scene-cache.populate` | 5.0 ms | `TacticalStableSceneCache.Populate` |
| `campaign.daily-review`, `campaign.construction`, `campaign.defense-intent`, `campaign.fiscal`, `campaign.coordinated-operations`, `campaign.sidecar-json-serialization`, `campaign.sidecar-file-write` | 4.0 ms | strategic coordinator / coordinated-ops / sidecar save |
| `telemetry.flush` | 10.0 ms | telemetry writer batch flush |

## Frame hitch probe

`Plugin.Update` calls `TelemetryFrameProbe.Tick(Time.unscaledDeltaTime)` every frame. A frame of >= 250 ms emits one protected `FrameHitch` row (category Health, `health.jsonl`, at most 2000 per session):
- `durationMs`: frame time = max(unscaledDeltaTime, wall time since the previous tick)
- `severe` (>= 500 ms), `frameIndex`, `wallIntervalMs`
- `gen0Delta`, `gen1Delta`, `gen2Delta`: `GC.CollectionCount` change since the previous frame
- `gcTotalMemoryBytes`; `monoUsedBytes`, `monoHeapBytes` (Unity Profiler; -1 if unavailable)
- `queueDepth`
- `scopeCount` (top-level scope calls on the main thread this frame), `scopeTotalMs`, `scopeNamesEvicted`
- `topScopes`: top 8 scope names by total ms this frame, `name:totalMsxcalls;...` (e.g. `tactical.orchestrator-tick:41.2x2`)

`manifest.json` `frameProbe`: `frameCount`, `hitchCount` (>= 250 ms), `severeHitchCount` (>= 500 ms), `maxFrameMs`, `hitchThresholdMs`, `severeThresholdMs`.

`summary.md` adds:
- `- frameHitches: count=N over500ms=M maxMs=X` (max of FrameHitch rows and the manifest). Judge hitches by max and the >= 500 ms count, not p99.
- `- inputTruncated: true (read B bytes; bound 33554432)` when the summary hit its 32 MB input bound. Summaries stream JSONL, read `failures*`/`health*` first, and keep a running top-8 `slowestScopes`.

## Queue, budget, writer, manifest

- **Queue eviction** when the detail cap is full (first evicted first): Trace, Performance, State, Decision, Gate/Write. An incoming row evicts the newest row of a strictly lower-priority rank only. Health/Failure rows use their own reserve (max(4, capacity)) and never evict detail rows. Drain preserves enqueue order.
- **Budget cut order** (`TelemetryBudget.CutPercent`): a low-priority row is refused once emitted bytes plus the row would pass a share of `Max Tuning Log MB`: Trace 90%, Performance 93%, State 95%, Decision 98%, Gate/Write 100%. Health/Failure rows go to 100%. This is the same sequence as queue eviction, so Performance rows are cut before Decision rows. Rows flagged as protected summaries (`protectedSummary=true` such as `FrameHitch`, `TelemetryShutdown`, `TelemetrySinkFailure`, or Health/Failure/Performance event names containing `summary`/`counter`) draw from a separate reserve of 2% of the cap, clamped to 64 KB-5 MB. `PerformanceAggregate` rows are not protected and follow the 93% cut.
- **Writer:** one open handle per output file; each batch writes one chunk per file. Rows count as written only once their bytes are on disk. A failed/torn write keeps the rows that fully landed, truncates a partial row, and retries the rest after exponential backoff (50 ms doubling to 2 s), without duplicates. A torn row that cannot be truncated is left as its own invalid line and counted in manifest `droppedCounters.tornRows`; the validator tolerates that many fragment lines and reports `tornRows=N`. The file sink is disabled only after 5 consecutive failures. Sink-failure rows use the same path, with one immediate retry; when raised off the writer thread they are queued for the writer.
- **manifest.json** is rewritten when the set of output files changes (new file or rotation), every 5 s while rows flow, and at shutdown. At shutdown: one final manifest, then the issue bundle, then the summary; the manifest is rewritten again only if a late sink failure changed the counters.

## Battle-core rows

`LogOutput.log` (tags outside the tactical routing): `[BattleCore] producers count=12 order=…` and `[OrderArbiter] first vanilla order issued channel=… source=…` once per battle; `[OrderArbiter] alliance=… intents=… winners=… issued=… moved=… failed=… retried=… gaveUp=… skipped=… deduped=… superseded=… topSkip=…` every 30 s real time per AI side while the arbiter runs; `[BattlePerception] alliance=… mode=… reason=… visible=… remembered=…` once per mode per battle; `[ChargeDoctrine] first infantry charge armed: …`, `[ChargeDemotion] first vanilla assault demoted: …` and the BC9 `[BattleRecon] first reconnaissance: …` / `first reconnaissance in force: …` / `first skirmisher screen pushed out: …` once per battle. `TacticalRecon` Decision rows (sidecar): `MissionStarted` (target kind, scout, target), `MissionEnded` (reason), `ProbeNominated`, `ProbeHeld` (a probe ended short of a superior enemy and its group holds the ground; `[BattleRecon] first probe hold: …` once per battle; the held group's `TacticalCommandPosture` rows read reason `probe-hold` while a move back is dropped), each throttled per side and event to one per 30 s real time. Battle-core warnings use `[once:tactical-battle-core:…]` keys. The ordered battle-core smoke checklist is in `docs/superpowers/plans/2026-10-15-battle-core.md` § BC7.

BC10 (deployment and movement honesty): `TacticalDeploymentPlan` Decision rows (`side`, decision source `EntryPoint|MapCentre|Visible`, fields `day1`, `candidates`, `replaced`, `issued`, `refused`, `reserveGroups`, `reserveChildren`, `clampReverted`; at most 64 per battle, `tactical*.jsonl`); `TacticalArrivalRepath` Decision rows (#105, record-only: `unit` = arriving group, fields `arrived`, `queued`, `ordered`, `repathed`, `repathedOrdered`; multi-unit arrivals only, at most 64 per battle). OnceLog lines (per battle): `[once:tactical-battle-core:terrain-grid] [BattleTerrain] grid <x>x<z> m, crossings=…`, `[once:tactical-battle-core:covered-crossings] [OrderExecutor] perceived covered crossings: alliance=… crossings=… coverPoints=…`, `[once:tactical-battle-core:road-route-first-fire] [OrderExecutor] first road march: via=… costRatio=… column=true`, `[once:tactical-battle-core:deployment-fog-first-fire] [Deployment] fog-honest re-placement: …`, `[once:tactical-battle-core:deployment-reserve-first-fire] [Deployment] reserve echelon: …`, `[once:tactical-battle-core:arrival-repath-first] [BattleArrival] an entry re-path overwrote an executor-ordered member: …`. Warning keys: `terrain-grid-missing`, `covered-crossings-failed`, `covered-crossings-truncated`, `tactical-producer-posture:nav-filter-failed`, `tactical-producer-posture:road-route-failed`, `arrival-guard-failed`, `tactical-deployment-terrain:*`. The tag `TacticalDeploymentPlan` is registered in `TelemetryTagPolicy`.

## W&L campaign rows

`LogOutput.log`, under every `Logging Profile`: `[W&LCampaignDispatch] trigger=vanilla-move|whiskey-order type=… unit=… issuer=… target=x,z dest=… zone=none|shown action=issue` for every campaign order Whiskey gives a W&L player (`zone=none` only if subordinate orders are switched to informational; at most one per target per game day over the last four targets). The tag has its own route (`TelemetryTagPolicy`: campaign layer, sidecar and main log), so the issue line, written with `LegacyInfoToMainLogIfAllowed`, reaches the main log, and the campaign sidecar too under `CampaignTuning`. Skips (`action=skip reason=no-op-move|active-duplicate|rate-limit|queued-order-cancelled|position-correction|…`) are written with `LegacyInfo` and go to the campaign sidecar only (written under `Logging Profile = CampaignTuning`), at most 64 distinct signatures before the set resets. `[PlayerCompliance] scope=campaign|battle outcome=complied|missed reliability=… samples=…` per resolved order. `[DefenseIntent]` `skip-revert reason=player-chain` (Gate event) when #25 leaves a commit of the player's own command whole; `[OperationalProbe]` `skip-stop` once when a probe pause/withdraw would have stopped it. `[once:cic-faction-ai-guard:applied]` the first time #70 lets a non-player faction past vanilla's CIC early return. Warnings use `[once:wl-campaign-dispatch:…]`, `[once:player-compliance:…]` and `[once:cic-faction-ai-guard:…]` keys.

## Smoke checklist

- Close GTCW before deploy.
- Build `dist/WhiskeyRealism.dll` and deploy to `<GTCW>/BepInEx/plugins/WhiskeyRealism.dll`.
- Confirm the deployed plugin timestamp is fresh and that `dist/WhiskeyRealism.dll` and the deployed DLL have matching size and `sha256sum`.
- Pick a tuning profile intentionally, usually `FullTuning` for framework smoke.
- Launch GTCW and start a career/battle path that emits tactical and campaign rows.
- Confirm a new session appears under `<GTCW>/BepInEx/WhiskeyRealism/tuning-logs/<session-id>/`.
- Confirm `manifest.json` records `configSnapshot` (`loggingProfile`, `queueCapacity`, `flushMilliseconds`, `flushRows`, `maxTuningLogBytes`, `fileRotateBytes`, `retainedSessions`), `outputFiles`, `droppedCounters` and `frameProbe`.
- Run the telemetry validator against the session directory.
- Scan `LogOutput.log` for repeated `Exception`, `ERROR`, `missing-anchor`, Harmony failures, telemetry sink failures, or log spam.
- Confirm tuning rows moved to sidecars and bounded serious warnings/errors remain visible in `LogOutput.log` where appropriate.

## Evidence

Deployed DLL hash, harness count and runtime smoke state live in [`docs/handoff.md`](handoff.md).
