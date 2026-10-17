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
| `TacticalCommandPosture` | Decision | Posture and division-play producers (`PostureTelemetry.Group`): per group decision, task, target, intents submitted (`writeResult=submitted`/`none`), BC11 `facingSource` (pressure, closest or none); per (source, group) signature, 30 s throttle. |
| `TacticalLeafCascade` | Decision | Posture producer (`PostureTelemetry.Leaf`): leaf brigade role/task/outcome, cascade chain, applied. |
| `TacticalFacingPulse` | Decision / Write | Facing-pulse producer (`PostureTelemetry.Facing`): scope, task, target formation/facing, `writeResult`; BC11 adds `facingSource` (pressure, closest, entry or none). |
| `TacticalRefuseInPlace` | Write | BC11 refused flank in combat (`PostureTelemetry.RefuseInPlace`): `side` (Left / Right), `rotated` regiments, `skippedByMicroTurn`; throttled per group. |
| `TacticalEnvelopment` | Decision | BC11 `EnvelopmentManeuver` phase changes (`PlayExecutorRuntime`): `plan`, `child`, `kind`, `phase` (Hold, March, Turn, Attack, Released, Abandoned), `reason` (`river-on-leg`, `no-route`, `anchored-high-ground`, `lost-strength`, `timeout`, `plan-changed`, `mirror-flank`, ...), `proceed` (why the Hold ended: `center-contact`, `offset`, `hold-ceiling` when the Hold reached `MaxManeuverHours` from the play's activation, `mirror-flank`), `mirrored`, `anchorX/Z`, `targetX/Z`. One row per change. BC14 adds `doctrineHours` (the clock at the transition), `plannedReleaseHours` and `timeBudgetHours` (the ledger's values the maneuver latched; -1 none), `via` / `attackVia` with `viaX/Z` and `attackViaX/Z`, and on the Turn row `arrivedHours`, `predictedArrivalHours` and `predictedMarchHours` (criterion-arrival-prediction); `proceed` can read `planned-release`. |
| `TacticalReserveRelease` | Decision | BC11: a Reserve child became `RefuseLeft` / `RefuseRight` (`reason=flank-threat`, `wingSector`). BC13 adds `reason=stagger|crisis`, `held`, `floor`, `floorBreach` (`crisis` on a crisis release that leaves `held` below `floor`, else `none`) and `nowHours` (doctrine hours). BC14 adds `reason=crossing-threat` with `crossing` (link index), `guardX/Z`, `guardHeading`, `ownShiftHours`, `enemyArrivalHours`, `held`, `floor`, and `reverted=true` when the guard ends. |
| `TacticalPlanSelection` | Decision | BC14, one per pick or kept plan, with the flag off too: `trigger` (`initial`, the replan trigger, `new-day`), `kept`, `stance`, `odds`, `winner`, `mirrored`, `candidates`, `preconditionFiltered`, `stanceFiltered`, `crestFiltered`, `feasibilityFiltered`, `allImpossible`, `ledgerAgeHours` (-1 with no ledger) and the top three by final score (`top1Plan`, `top1Score`, `top1FeasibilityTerm`, `top1Verdict`, `top1Reason`, and 2, 3). |
| `TacticalManeuverFeasibility` | Decision | BC14, per candidate per published evaluation when its verdict or penalty bucket (0.1) changed, at most one per 0.05 doctrine-h: `plan`, `mirrored`, `verdict`, `penalty`, `reason`, `bodies`, `selected`; plus one `plan=all` summary per published evaluation: `candidates`, `impossible`, `risky`, `slices`, `forced`, `queries` (path searches), `legsSampled`, `legOverflow`, `confirmQueued`. |
| `TacticalManeuverBody` | Decision | BC14, per body of the selected plan and of the best-scored others when its penalty bucket or route source (`estimate` / `navmesh`) changed, and when the monitor names an assumption: `plan`, `body`, `child`, `kind`, `strength`, `routeUnits`, `routeSource`, `marchVia` / `attackVia` (link or -1) with their `...State`, `strikeContested`, `courierHours`, `marchHours`, `inwardSurplusHours`, `strikeHours`, `desiredStrikeHours`, `releaseHours`, `enemyShiftHours`, `windowHours`, `pin`, `enemyAgainst`, `isolatedOdds`, `bar`, `ownRatio`, the six terms and `penalty`, and `assumption` / `suppression` (`cooldown`, `committed`, `margin`, `preempted`, `none`). |
| `TacticalReplan` | Decision | BC14, structured and never deduped, one per replan and per kept plan (the deduped `[TacticalReplan]` log line stays): `trigger` (`ManeuverAssumptionBroken` carries `assumption`), `kept`, `newPlan`, `mirrored`, `doctrineHours`. |
| `TacticalCrossingWatch` | State | BC14, per side every 0.1 doctrine-h: `held`, `maxHeldStrength` (the most perceived strength standing at any one Held crossing; per crossing, not summed), `watched`, `clear`, `unknown`, `probes`, `lookRequests`, `regions`, `links`, `regionsTruncated`, `linksTruncated`, `oldestPerceivedAgeHours`, `neverPerceived`. |
| `TacticalCommanderPersonality` | State | BC13: once per commander per battle: `commander`, `echelon`, `source` (Default / Registry / Derived), `aggression`, `caution`, `audacity`, `casualtyTolerance`, `politicalResponsiveness`, `agg01Band` (`low` < 0.30, `mid`, `high` >= 0.70). |
| `TacticalOpening` | State | BC13 opening detector, per side every 0.1 doctrine-h: counts by kind (`gap`, `shaken`, `flank`, `battery`, `cluster`, `penetration`), `confirmed`, `bestConfidence`, `suppressedUnperceived`, `probePoints`, `probeCached`, `deferred`, `latticesStarted`, `latticesEnded`, `deferOverBound` (0 expected), `live`, `subtreeTruncated`, `tableClears`, `feedbackDropped`, `tableRefusals`. |
| `TacticalExploit` | Decision | BC13: one row per launch, per declined best pair (`reason`, or `roll-miss` with `chance` and `roll`), per phase change and per end (`row` = launch / decline / phase / end; `exploit`, `kind`, `asset`, `commander`, `source`, `stance`, `cavalry`, `phase`, `live`, `casualtyTolerance01`; an end adds `end` literal or `success`, `durationHours`, `lossDelta`, `playerSkips`, `leftSector`). `playerSkips` counts what the exploit's own Move, Formation and Stance intents left out because of the player: the executor's `PlayerOwned` skips plus the player-owned children the chain fan-out excluded (`ChainResolution.PlayerExcluded`), keyed by alliance and exploit id and drained into the entry each tick (0 expected, criterion-player-never-moved). |
| `TacticalCavalryStrike` | Decision | BC13 #120, throttled per regiment per 0.05 doctrine-h: `outcome` (`allowed` / `refused`), `reason` (`hidden`, `square`, `garrisoned`, `supported`, `steady`, `odds`, `outflanked`, `exploit-target`). |
| `TacticalStanceCooldown` | State | BC13 #119, per side every 0.1 doctrine-h, whatever the flag and the macro stance (the instance is counted in every case): `fix` (the re-stamp is acting), `reason` (`restamp`, `flag-off` or `retreat`), `vanillaChanges`, `restamped`, `maxChangesPerGroupHour`, `released`, and the delay histogram (`delayUnder025`, `delay025to05`, `delay05to1`, `delayOver1`, doctrine hours) smoke item 11 reads. |
| `TacticalSeparatedEnemySteer` | State | BC13 #121, per side every 0.1 doctrine-h: `steer`, `suppressed`, `launchesWhiskey`, `launchesVanilla`. |
| `TacticalVanillaFlankMove` | State | BC11 measurement (`BattleCoreTick`, `Enable Tactical Observer`): a group's `lastaiorder` went 0 to 4 or 5, i.e. vanilla started a `FlankMove` (`direction` 0 left / 1 right, `macroai`, `stance`, `playerOwned`, `tallyFix`). Smoke item 2.33 compares the rate with `Enable Vanilla Flank Threat Tally Fix` on and off. |
| `ChargeDemotion` | Decision | ChargeDemotion producer: a vanilla assault (stance 4) demoted, counted once the executor issued it (`ChargeDemotionTracker`): unit, `decision=DemoteToHold`, `reason=charge-demotion`, `fromStance`, `toStance`, `submittedAtHours`; per (side, group, stance) signature, 30 s throttle. |
| `ChargeDemotionCount` | Decision | ChargeDemotion producer: per side, at most every 30 s real time and only after a new demotion: `demoted` (window), `battleTotal`, `windowSeconds`. |
| `BattlePerception` | Decision | `BattlePerceptionProbe`: per-side perception mode change (mode, reason, visible, remembered). The once-per-mode `[BattlePerception]` line also goes to `LogOutput.log`. |
| `TacticalHeavyGate` | Gate | `TacticalBattleCoordinatorRuntime`: heavy snapshot-build gate decision per side (executed/skipped + reason, battle/real-time clocks, pending flag, unit count, objective hash). |
| `TacticalDecisionMatrix` | State | `TacticalObserverPatch` (perf slice F1): built only when the profile writes tactical rows. Side-wide: one matrix per AIBattle per frame, written at `AdjustGroupAIStance`; the battle row carries `events` (the side-wide events observed in that frame up to and including the stance pass, e.g. `Reserve|Macro|Odds|Group`; Sector, Order and Feud are observed after the write in vanilla's `UpdateAITasks` order, so they never appear there and write no side-wide matrix of their own, while their observer summaries, Feud's `delta=` included, are unchanged) and keeps the per-signature throttle (`Tactical Decision Matrix Min Seconds Between Snapshots`); a group row is written only when its decision inputs (the input signature's fields without the event, floats in 0.5 buckets, plus the decision) changed, or every 30 s. Micro-AI calls write the battle row and the focus group's row under the same rules. A Statistics-panel replay in the same frame writes no second matrix. The side-wide odds are evaluated once per AIBattle per frame and shared; counts and the feud `delta=` stay per call. |

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
- Name lookup is two-tier (perf slice F3): an identity alias table (`ReferenceStringComparer`, one reference hash: scope names are literals) in front of the canonical ordinal per-thread map. A name built per call still lands on its one aggregate (the miss goes through the ordinal map), and aliases stop at `MaxAliasesPerThread` (2048), so it can neither split an aggregate nor use up `MaxScopesPerThread` (1024) and get later scopes dropped. The frame probe's per-frame table and `PatchFirstFire` use the same identity-first, ordinal-fallback rule (first-fire aliases capped at 2048). Every call site passes a literal or a choice between two literals (`PerfHotPathTests` pins it). Entry cost on net8: ~127 -> ~71 ns per tactical scope + Mark under TacticalTuning, ~105 -> ~61 ns for a record-only campaign scope, ~19 -> ~7.5 ns under `Off` (Mono roughly 1.5-3x).
- Unity Profiler markers (perf slice B5): each scope also opens a marker of the same name (category Scripts) while a development player's Profiler records, so Whiskey's scopes appear in the Unity Profiler like any game system. `TelemetryProfilerMarkers` calls the icalls behind `ProfilerMarker` directly (`ProfilerMarker.Begin/End` are `[Conditional("ENABLE_PROFILER")]` and would be compiled out of a plugin). `Profiler.supported` is false in the shipped non-development GTCW player, so there a scope pays one bool read; `Profiler.supported && Profiler.enabled` is re-read once per frame in the frame-probe tick. Markers are independent of the `Logging Profile`. Any icall failure disables them for the session.

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
| `tactical.patch.arrival-guard.prefix`, `.finalizer` | 2.0 ms | #112 `ReinforcementArrivalGuardPatch`, record-only |
| `campaign.patch.wl-campaign-order-pass` | 2.0 ms | #69: `UpdateUnitAI` order pass (leftover-order clear, courier delivery, acknowledgement, compliance observation) |
| `campaign.patch.cic-faction-ai-guard.*` | 2.0 ms | #70: arm/disarm around `CheckShipConstruction` / `CheckArmyGroupManagement`, `IsCommanderInChief` answer |
| `tactical.scene-cache.populate` | 5.0 ms | `TacticalStableSceneCache.Populate` |
| `campaign.daily-review` | 4.0 ms | each slice of the time-sliced daily review (perf slice M2): the first on the date-change frame, each later frame's slice, and any drain before a new day or a campaign save; with `Time-Slice Daily Review` off, the whole review. `[DailyOps:Perf]` adds `slices=<n> maxSliceMs=<ms>`; its `totalMs` is the summed slice time |
| `campaign.construction`, `campaign.defense-intent`, `campaign.fiscal`, `campaign.sidecar-json-serialization`, `campaign.sidecar-file-write` | 4.0 ms | strategic coordinator / sidecar save (`campaign.coordinated-operations` retired with the package layer, G6) |
| `campaign.planner.snapshot`, `.detect`, `.recon`, `.plan`, `.allocate`, `.appoint`, `.construction`, `.steering`, `.mover`, `.recon-execute`, `.terrain-drain`, `.review`, `.execute` | per scope | stage G planner stages: the Plan job (`.review`: snapshot, detection, recon planning, plan, allocation, publish) and the Execute job (`.execute`: appointments, construction steering, steering, mover, recon executor); each stage is its own scope so a slow stage names itself |
| `campaign.patch.planner-availability.offensive`, `.defensive` | 2.0 ms | #127: the availability Postfixes on `IsUnitAvailableForOffensiveOperations` / the defensive twin |
| `campaign.patch.railroad-steering` | 2.0 ms | #128: `RailroadLineSteeringPatch` (the one user-approved Prefix-skip) |
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

- **Shedding order under load** (user direction 2026-10-15, perf slice B6): Trace rows first, then Info rows (State, then Decision, then Gate/Write); Performance rows and Warning rows of any category are never shed before the hard cap. Debug severity sheds like Info. Queue and budget use the same order.
- **Queue eviction** when the detail cap is full (first evicted first): Trace Info, State Info, Decision Info, Gate/Write Info. An incoming row evicts the newest row of a strictly lower-priority rank only, so Performance and Warning rows are never evicted by another detail row (when the queue holds nothing else, an incoming row is dropped and counted). Health/Failure rows use their own reserve (max(4, capacity)) and never evict detail rows. Drain preserves enqueue order.
- **Budget cut order** (`TelemetryBudget.CutPercent`): a low-priority row is refused once emitted bytes plus the row would pass a share of `Max Tuning Log MB`: Trace (Info) 90%, State Info 93%, Decision Info 95%, Gate/Write Info 98%, Performance and Warning rows 100%. Health/Failure rows go to 100%. Rows flagged as protected summaries (`protectedSummary=true` such as `FrameHitch`, `TelemetryShutdown`, `TelemetrySinkFailure`, or Health/Failure/Performance event names containing `summary`/`counter`) draw from a separate reserve of 2% of the cap, clamped to 64 KB-5 MB. `PerformanceAggregate` rows are not protected; they now run to the total cap, so a full budget keeps the performance evidence and sheds the Info doctrine rows first.
- **Writer:** one open handle per output file; each batch writes one chunk per file. Rows count as written only once their bytes are on disk. A failed/torn write keeps the rows that fully landed, truncates a partial row, and retries the rest after exponential backoff (50 ms doubling to 2 s), without duplicates. A torn row that cannot be truncated is left as its own invalid line and counted in manifest `droppedCounters.tornRows`; the validator tolerates that many fragment lines and reports `tornRows=N`. The file sink is disabled only after 5 consecutive failures. Sink-failure rows use the same path, with one immediate retry; when raised off the writer thread they are queued for the writer.
- **manifest.json** is rewritten when the set of output files changes (new file or rotation), every 5 s while rows flow, and at shutdown. At shutdown: one final manifest, then the issue bundle, then the summary; the manifest is rewritten again only if a late sink failure changed the counters.

## Battle-core rows

`LogOutput.log` (tags outside the tactical routing): `[BattleCore] producers count=13 order=…` and `[OrderArbiter] first vanilla order issued channel=… source=…` once per battle; `[OrderArbiter] alliance=… intents=… winners=… issued=… moved=… failed=… retried=… gaveUp=… skipped=… deduped=… superseded=… topSkip=…` every 30 s real time per AI side while the arbiter runs; `[BattlePerception] alliance=… mode=… reason=… visible=… remembered=…` once per mode per battle; `[ChargeDoctrine] first infantry charge armed: …`, `[ChargeDemotion] first vanilla assault demoted: …` and the BC9 `[BattleRecon] first reconnaissance: …` / `first reconnaissance in force: …` / `first skirmisher screen pushed out: …` once per battle. `TacticalRecon` Decision rows (sidecar): `MissionStarted` (target kind, scout, target), `MissionEnded` (reason), `ProbeNominated`, `ProbeHeld` (a probe ended short of a superior enemy and its group holds the ground; `[BattleRecon] first probe hold: …` once per battle; the held group's `TacticalCommandPosture` rows read reason `probe-hold` while a move back is dropped), each throttled per side and event to one per 30 s real time. Battle-core warnings use `[once:tactical-battle-core:…]` keys. The ordered battle-core smoke checklist is in `docs/superpowers/plans/2026-10-15-battle-core.md` § BC7.

BC10 (deployment and movement honesty): `TacticalDeploymentPlan` Decision rows (`side`, decision source `EntryPoint|MapCentre|Visible`, fields `day1`, `candidates`, `replaced`, `issued`, `refused`, `reserveGroups`, `reserveChildren`, `clampReverted`; at most 64 per battle, `tactical*.jsonl`); `TacticalArrivalRepath` Decision rows (#112, record-only: `unit` = arriving group, fields `arrived`, `queued`, `ordered`, `repathed`, `repathedOrdered`; multi-unit arrivals only, at most 64 per battle). OnceLog lines (per battle): `[once:tactical-battle-core:terrain-grid] [BattleTerrain] grid <x>x<z> m, crossings=…`, `[once:tactical-battle-core:covered-crossings] [OrderExecutor] perceived covered crossings: alliance=… crossings=… coverPoints=…`, `[once:tactical-battle-core:road-route-first-fire] [OrderExecutor] first road march: via=… costRatio=… column=true`, `[once:tactical-battle-core:deployment-fog-first-fire] [Deployment] fog-honest re-placement: …`, `[once:tactical-battle-core:deployment-reserve-first-fire] [Deployment] reserve echelon: …`, `[once:tactical-battle-core:arrival-repath-first] [BattleArrival] an entry re-path overwrote an executor-ordered member: …`. Warning keys: `terrain-grid-missing`, `covered-crossings-failed`, `covered-crossings-truncated`, `tactical-producer-posture:nav-filter-failed`, `tactical-producer-posture:road-route-failed`, `arrival-guard-failed`, `tactical-deployment-terrain:*`. The tag `TacticalDeploymentPlan` is registered in `TelemetryTagPolicy`.

BC12 (terrain picture): `[once:tactical-battle-core:terrain-picture] [BattleTerrain] picture ready: cells=<n> crests=<n> coverLines=<n> waterCells=<n> crossings=<n> towns=<n> junctions=<n> buildMs=<ms> steps=<n> maxStepMs=<ms>` once per battle; `[once:tactical-battle-core:terrain-line] [BattleTerrain] first terrain-line anchor: terrain-line:<crest|wall|sunken|fence|embankment|river>[:anchored-left][:anchored-right]` at the first terrain-line anchor (the reason also appears inside the `battle-line-*` doctrine reasons, with `:defensive-side-terrain-line`); `[TacticalIntent]` evidence can carry `EnemyOnStrongGround`; the telemetry playbook label (`TacticalPlaybookKind`) is `LineHold` unless at least half the held groups stand on crest cells (`HighGroundDefense`). Performance scopes `tactical.terrain-picture.build` (5.0 ms per step) and `tactical.terrain-line` (1.0 ms per ledger tick). Absent: `tactical-battle-core:terrain-picture-failed`, `terrain-heights-missing`.

BC11 (flank response and envelopment): `TacticalRefuseInPlace` Write rows (`side`, `rotated`, `skippedByMicroTurn`), `TacticalEnvelopment` Decision rows (one per phase change: `plan`, `child`, `kind`, `phase`, `reason`, `proceed`, `mirrored`, `anchorX/Z`, `targetX/Z`), `TacticalReserveRelease` Decision rows (`reason=flank-threat`), `TacticalVanillaFlankMove` State rows (`direction`, `macroai`, `stance`, `playerOwned`, `tallyFix`; under `Enable Tactical Observer`), and `facingSource` on `TacticalCommandPosture` / `TacticalFacingPulse`. OnceLog lines (per battle): `[once:tactical-battle-core:refuse-first-fire] [OrderExecutor] first refused flank: mode=<Wheel|InPlace> flank=<Left|Right> regiments=<n> skipped=<n>`, `[once:tactical-battle-core:envelopment-first-fire] [Envelopment] first flank march: plan=<plan> rect=<kind> child=<id>`, `[once:tactical-flank-threat:first-correction] [FlankThreatTally] group flanks threatened re-tallied per subordinate: vanilla=<v> corrected=<c>`; warnings `tactical-flank-threat:failed` / `:bui-missing` (absent in a healthy session). First-fire / scope id `tactical.patch.flank-threat-tally` (every battle), scope `tactical.envelopment` (1.0 ms per side per direct-child cycle).

BC14 (maneuver feasibility): OnceLog `[once:tactical-battle-core:river-regions] [RiverRegions] regions=<n> links=<n> crossings=<n> frozen=<b> buildMs=<ms>`, `…:feasibility-first] [Feasibility] first evaluation: candidates=<n> impossible=<n> risky=<n>`, `…:feasibility-filtered] [Feasibility] first plan filtered: plan=<p> reason=<r>`, `…:feasibility-replan] [Feasibility] first assumption replan: assumption=<r>`, `…:feasibility-via] [Feasibility] first wing routed through a chosen crossing: state=<s>`; warnings `river-regions-truncated`, `river-regions-failed`, `feasibility-all-impossible`, `feasibility-nav-failed`, `feasibility-daylight-missing`, `feasibility-tick`; scopes `tactical.feasibility.evaluate` (2.0 ms), `tactical.feasibility.monitor` (0.5 ms), `tactical.feasibility.navmesh` (2.0 ms), `tactical.river-regions.build` (5.0 ms).

BC13 (commander opportunity): OnceLog `[once:tactical-battle-core:opportunity-first-opening] [Opportunity] first opening: kind=…`, `…:opportunity-first-exploit] [Opportunity] first exploit launched: kind=… asset=… commander=… source=…`, `[once:tactical-battle-core:cavalry-strike-first-refusal] [CavalryStrike] first refused: reason=…`, `[once:tactical-battle-core:stance-cooldown-first] [StanceCooldown] first vanilla stance hold restored`; patch-time IL lines `[once:tactical-il:macro-upgrade:applied] [MacroUpgradeIl] applied blocks=1`, `[once:tactical-il:artillery-side:applied] [ArtillerySideIl] applied sites=1`, `[once:tactical-il:reserve-support-pick:applied] [ReserveSupportPickIl] applied sites=1` (a mismatch writes `tactical-il:<site>:pattern` with the count, an exception `tactical-il:<site>:transpiler`, and the method stays vanilla). Scopes: `tactical.producer.opportunity`, `tactical.patch.cavalry-strike.*`, `tactical.patch.stance-cooldown.*`, `tactical.patch.separated-enemy.*` (the Transpiler hooks carry first-fire marks only).

## Campaign planner rows (stage G)

Registered together in `TelemetryTagPolicy` (plan G0). Everything is campaign layer (sidecar under `CampaignTuning`/`FullTuning`) except `[CampaignPlanner]`, which also reaches `LogOutput.log` under every profile. Rows are written on change, never per tick.

| Event | Category | Source |
|---|---|---|
| `CampaignPlanner` | State | `[CampaignPlanner] first review alliance=<n> ...` once per alliance per campaign (the G0 runtime proof); `saved state rebound ...` after a load; OnceLog warnings `campaign-planner:*` (snapshot degraded, review failed, executors failed, pre-war idle, appointment/construction failures) |
| `CampaignPlan` | Decision | one row per alliance per decision: `main=<theater>/<objective>`, `prior=<id>`, chapter, trigger; with `Plan Trace Logging` the per-candidate scores |
| `EnemyBeliefs` | State | belief counts, expiries and detections per review |
| `Recon`, `ReconMission` | State, Write | recon task counts per alliance on change; per-mission start, hold, recall and the queued cavalry order |
| `Allocation` | Decision | one row per change of roles or needs (MainEffort, Support, Hold, Recon, Reserve counts, unreliable-days run) |
| `CampaignMover` | Write | issued, confirmed, failed, retried, given-up counts per alliance |
| `CampaignTempo` | Decision | the chapter tempo roll on a decision's first review and its release: `main`, `decision`, `chapter`, `gap`, `p`, `roll`, `released`, `forced` (`none` for p/roll when no roll was needed); `undispatchable=true` once per decision |
| `CampaignSteer` | Write | `followedcampaignobjective` / `theaterposition` writes by the steering executor |
| `Appointment` | Write | commander appointments and succession reseats (one a day at most) |
| `DepotTask`, `RailroadSteer`, `CapitalFloor`, `ReplacementFlow` | Write | depot construction wishes, the rail line steer, the capital floor and the replacement-flow routing |

`[DefenseIntent]` (Decision, the existing capital/asset defense ledger, unchanged by the planner) carries two fields the smoke reads: `scale` is the threat's `ThreatScale` (`None`, `Raid`, `Landing`, `MajorLanding`, `DecisiveLanding`; `-` with no threat) and `assetRole` is the threatened asset's `AssetStrategicRole` flags (`-` with no threat), next to `posture`, `sourceKind`, `threat`, `enemy`, `desired`, `selected`, `adequate`, `understrength`, `held` and `responseSignature`.

Retired with the phased-plan CIC (G6): the tags `HistoricalOperation`, `ArmyArea`, `OperationalProbe` and `CoordinatedOps` (and the `campaign.coordinated-operations` scope); `ArmyGroup` (Write) replaces `ArmyArea` for the historical army-group steering that remains. A smoke log must show none of the retired tags.

## W&L order flow rows

`TacticalPlayerOrderFlow` Decision rows (tactical layer, sidecar), one per event in the life of an order bound for the W&L player's panel. Decision `action` and `reason`; fields `type`, `origin` (`Vanilla|P62|Executor|Release|FollowUp|Retreat`), `issuer` (the issuing headquarters' instance id), `issuedH`, `dueH`, `deliveredH` (doctrine hours), `legs` and `meters` (the courier path the delay was computed over), `dest`, `session`. Actions: `capture` (queued on a courier), `drop` (not queued: `rate-limited`, `en-route`, `courier-overflow`, or a re-admit policy reason such as `engage-at-will`, `retarget-near`, `defend-over-attack`, `vanilla-drops`; bounded by a ring of the last 128 signatures because vanilla's continuous callers repeat), `deliver` (`accepted`, or `refreshed` for vanilla's in-place type-11 refresh), `discard` (`superseded`, `unit-gone`, `cross-scope`, `release-stale`, `relay-took-slot`, `vanilla-refused`, `vanilla-threw`, `courier-overflow`, `eod-clear`, `command-change`, `battle-end`, `mode`, or a policy drop reason), `defer` (re-admittable after the hold; `retarget-deferred`, `defend-deferred`), `requeue` (`slot-changed`). Criteria read from these rows: orders after first contact, retreat arrival, courier delay (`deliveredH - issuedH`), no thrash. The tag is registered in `TelemetryTagPolicy`.

More `TacticalPlayerOrderFlow` rows: `action=prestige-delta` (one per ended order session, `origin`, `session`, `delta`; a Release or FollowUp session always gets one, the control for "no automatic prestige"), `drop reason=enemy-side-retreat` (the enemy's or feud's type 15, BUG-WL-019), `discard reason=follow-up-stale`, `deliver` for `origin=FollowUp`/`Release`. `CampaignPlayerOrderFlow` Decision rows (campaign layer): fields `type`, `origin` (`Vanilla|CampaignBridge|C69`), `issuer`, `issuedY`, `dueY`, `deliveredY` (campaign years since 1860, `GameVars.currenttimefromstart`), `meters`, `unit`, `dest`, `session`; actions `capture` (`queued`), `drop` (`repop`, `rate-limited`, `en-route`, `courier-overflow`; bounded to one row per change of signature because vanilla's capital defense repeats every pass), `deliver` (`accepted`), `discard` (`superseded`, `unit-gone`, `cross-scope`, `repop`, `rate-limit`, `active-duplicate`, `command-change`, `vanilla-refused`, `vanilla-threw`, `failed`, `restore`, `campaign-end`), `speed-restore` (BUG-WL-018: the player's own march mode written back). The tag is registered in `TelemetryTagPolicy` (campaign layer). Compliance: `[PlayerCompliance] scope=battle skip reason=inside-at-issue type=… session=…` (main log if allowed): an order that reached the player inside its zone started no sample.

`TacticalPlayerOrderFlow` move-gate rows (2026-10-16, branch `fix/battle-stuck-parent-order`): `defer reason=move-in-flight` is written once for an executor dispatch whose carrying move (the ordered group's, or the latest child's in a fan-out) was still in its superior's order queue when the dispatch was captured; the order is looked at on every delivery pass and is shown one superior-to-player leg after the move is carried out, so `deliveredH - issuedH` on its `deliver` row is the superior's delay plus the leg and not the leg alone. `discard reason=move-lost` is the same order when the move will not be made: the queued entry's own mounted courier line is active with no courier (vanilla's lost-order test, 125282-125286), or, the usual case, the entry is already gone (vanilla clears a lost line's waypoints at 125282 and carries out and deletes the entry one or two passes later, 125425) and the carrier unit holds no path. A carried-out move leaves the unit holding its transmitted path. The player is not told of a move the superior will not make; a move so short that the unit arrived between two passes reads as lost.

The vanilla observer rows around it (`TacticalObserverPatch`, sidecar `tactical.jsonl`): `TacticalPlayerOrder event=queued` is a Postfix on `Regiment.AddToOrderQueue` (`source` is the queue owner, the unit's scene parent, `orderType` `stance-N` = `ai_stance` N, `formation-N`, `combat-N`, `move-new`/`move-append`, `queueCount` the owner's queue length, `delayHrs` its processing time from now; every order chains behind the latest `processingtime` in the queue, 0.02 h each), and `TacticalPlayerOrder event=delivery` / `TacticalOrder event=delivery` are a Postfix on `Regiment.AddOrderCourierline`, written when the courier line is created and not when the order arrives. `processHrs` is the line's `processtime`: 0 or 0.02 for a bugle, and vanilla's own sentinel `9999999` for a mounted courier (`AddOrderCourierline` 125148), which `ProcessOrders` zeroes when the courier is within `unittotargetmaxdistance` of its target (125240-125242), or, as a fallback, once the entry is more than 0.2 h past its processing time and the line's courier is still waiting for its release (`timedmovement` 999, 125214): the entry is then carried out without the courier having arrived. `9999999` is therefore "a courier is riding", not a failure; `friction=Pending pathLag=true` on the same row says the path is not transmitted yet.

`LogOutput.log` OnceLog lines: `[once:wl-order:first-capture] [W&LOrder] first courier queued type=… origin=…`, `[once:wl-order:first-delivery] [W&LOrder] first order delivered type=… origin=…`. OnceLog lines added by the follow-up, campaign and bug-fix parts: `[once:wl-order:first-follow-up] [W&LOrder] reached order followed up type=…`, `[once:wl-order:first-release]`, `[once:wl-order:first-enemy-retreat-dropped] [W&LOrder] the enemy side's retreat order was not sent to the player`, `[once:wl-order:first-stale-issuer]`, `[once:wl-campaign-courier:first-capture|first-delivery|first-acknowledgement|restored] [W&LCourier] …`, `[once:capital-defense-speed:restored] [W&LCapitalDefense] restored running/doublequick on the player's group`, `[once:lost-orders-fow:restored] [W&LLostOrders] fog grid restored after a non-applicable incident`. Warning keys: `wl-order:capture-failed`, `wl-order:delivery-failed`, `wl-order:vanilla-threw`, `wl-order:no-snapshot`, `wl-order:bunits-field`, `vanilla-terrain:unreadable`.

## W&L campaign rows

`LogOutput.log`, under every `Logging Profile`: `[W&LCampaignDispatch] trigger=whiskey-order type=… unit=… issuer=… target=x,z dest=… action=issue` for every campaign order Whiskey gives a map-group W&L player (never a player carried by an AI corps; at most one per target per game day over the last four targets). A line with `trigger=vanilla-move` or a courier row with `origin=C69` comes from a build before 2026-10-16. The tag has its own route (`TelemetryTagPolicy`: campaign layer, sidecar and main log), so the issue line, written with `LegacyInfoToMainLogIfAllowed`, reaches the main log, and the campaign sidecar too under `CampaignTuning`. Skips (`action=skip reason=no-op-move|active-duplicate|rate-limit|…`) are written with `LegacyInfo` and go to the campaign sidecar only (written under `Logging Profile = CampaignTuning`), at most 64 distinct signatures before the set resets. `[PlayerCompliance] scope=campaign|battle outcome=complied|missed reliability=… samples=…` per resolved order. `[DefenseIntent]` `skip-revert reason=player-chain` (Gate event) when #25 leaves a commit of the player's own command whole; `[once:cic-faction-ai-guard:applied]` the first time #70 lets a non-player faction past vanilla's CIC early return. `CampaignPlayerOrderFlow` `action=drop reason=carried-player` (a queued Whiskey order whose player was moved under a corps while it rode; never shown) and `action=clear reason=carried-player-order` (and `[W&LCourier] cleared a campaign order held by a player carried by his corps`) when a leftover campaign order of a carried player is emptied. Warnings use `[once:wl-campaign-dispatch:…]`, `[once:wl-campaign-order-pass:…]`, `[once:player-compliance:…]` and `[once:cic-faction-ai-guard:…]` keys.

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
