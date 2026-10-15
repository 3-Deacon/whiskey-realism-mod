# Whiskey Realism Telemetry

**Default profile is `TacticalTuning`** as of 2026-05-18 (user direction): battles produce diagnostic sidecars by default. Disk usage is bounded by `Max Tuning Log MB` (default 250) and retention is capped to the newest 2 sessions, so this won't grow without limit. Set `Logging Profile = Off` in `<GTCW>/BepInEx/config/dev.kyle.whiskey-realism.cfg` if you want quiet operation.
Tuning profiles write under `<GTCW>/BepInEx/WhiskeyRealism/tuning-logs/<session-id>/`.

## Event tag inventory (2026-05-23)

Three new event tags registered in `TelemetryTagPolicy` for the F + G-Stage2 work:

| Event | Category | Source |
|---|---|---|
| `TacticalDirectChildIntent` | Decision | `ArmyOrchestrator.EmitDirectChildIntentTelemetry` — per-side role distribution (main/sup/fix/scrn/rL/rR/rsrv/fbck/unk counts + per-child `ChildId=Role:Sector` detail + plan/phase/opportunity/currentRatio). Signature-deduped + 5s throttle. |
| `TacticalMacroAiOverride` | Write | `BattleMacroAIOverridePatch.EmitWriteTelemetry` — fires when F sync writes a NEW vanilla `sideinformation[side].macroai` value (skip-if-correct otherwise). Includes previous + new value + opportunity + currentRatio + attackThreshold. Signature-deduped + 5s throttle. |
| `TacticalPlayExecution` | Decision | `PlayExecutorRuntime.EmitPlayExecutionTelemetry` — per-side play summary when geometric play assignments change. Fields: side, plan, template name, rect count, assigned count, army/enemy centroid X/Z, frontage, per-corps `ChildId=RectKind` assignments. Signature-deduped + 10s throttle. |

Performance-scope additions (visible in `performance.jsonl`):

| Scope | Threshold | Source |
|---|---|---|
| `tactical.patch.macroai-override` | 2.0ms | F — Harmony Prefix on `AIBattle.CheckGlobalAIStrategy` |
| `tactical.play-executor.zone-assignments` | 5.0ms | G-Stage2 — per-side play projection + corps assignment (gated by 3s recompute interval) |

## Event tag inventory (2026-05-22)

Earlier tags registered for the Scope B refactor — all route to `tactical.jsonl`:

| Event | Category | Source |
|---|---|---|
| `TacticalStanceWrite` | Write | `TacticalStanceWriter.EnsureStance` — every vanilla `ChangeStance` write by mod |
| `TacticalArtilleryDecision` | Decision | `TacticalArtilleryApplier` + B7 + all-stance audit — per-artillery cycle decision (Limber/Unlimber/Retreat/Wheel/ConserveAmmo/Fire/Preserve) |
| `TacticalBrigadeTaskHold` | Gate | `BattleCommandPostureExecutorPatch.EmitTaskHoldSuppressedTelemetry` — same-task re-issuance suppressed within hold window |
| `TacticalBrigadePosture` | Decision | Brigade-posture opportunity override telemetry |
| `TacticalRegimentalIntel` | Decision | `BattleGroupStancePatch.EmitRegimentalIntelTelemetry` — per-regiment non-NoAction decisions (FallBack/RetreatToBrigade/Wheel) |
| `TacticalRegimentalControl` | Decision | `BattleGroupStancePatch.EmitRegimentalControlEvent` — applied regimental actions (Scope B: `transition=applied`, no more claim/release pattern) |
| `TacticalRegimentalGate` | Trace | Reserved tag (was for the removed `PerformAIActionDLCWLPatch`; retained for compatibility) |
| `TacticalFriendlyPathBlocked` | Gate | Path-block events. Reason `destination-overlap-sibling-waypoint` indicates the sibling-scoped fix (2026-05-22) is firing — cross-brigade overlaps no longer block. |
| `TacticalFriendlyFireBlocked` | Gate | Friendly in firing-line corridor (telemetry-only, no behavioral writes) |

OnceLog-routed entries (emit via `Plugin.Log.LogInfo` with `[once:...]` prefix; the first non-once tag is parsed by `TelemetryTagPolicy.Route`):

| Once tag prefix | Source | Routing |
|---|---|---|
| `tactical-stance-write` | `TacticalStanceWriter.EnsureStance` | Inherits `TacticalStanceWrite` (Write) routing |
| `tactical-formation-write` | `TacticalFormationWriter` | Default OnceLog |
| `tactical-combatbehavior-write` | `TacticalCombatBehaviorWriter` | Default OnceLog |
| `tactical-brigade-task-hold:suppressed` | Task-hold suppressions | Inherits `TacticalBrigadeTaskHold` (Gate) routing |
| `tactical-orch:frame-coalesce` | Heavy-gate Layer 2 frame coalescing | Default OnceLog |



Profiles:
- `Off`
- `TacticalTuning`
- `CampaignTuning`
- `FullTuning`

Runtime controls:
- `Telemetry Queue Capacity = 8192` detail rows
- `Telemetry Flush Milliseconds = 250`
- `Telemetry Flush Rows = 256`

All three runtime controls are bounded in the BepInEx `[Telemetry]` config and are copied into each session `manifest.json` under `configSnapshot`. `Telemetry Queue Capacity` is the detail queue capacity; protected health/failure rows have a separate reserve and can temporarily exceed the detail cap.

Retention and caps:
- Retained sessions: newest two.
- Session cap: 250 MB.
- JSONL rotation: about 25 MB per file.

Output policy:
- Tactical and campaign tuning rows route to JSONL sidecars only when a tuning profile is enabled.
- `TacticalDecisionMatrix`, tactical observer, operations-ledger, campaign operation, and strategic tuning rows should not use `LogOutput.log` as their normal evidence stream after migration.
- Serious warning/error conditions can still emit bounded `LogOutput.log` warnings where the player or maintainer needs immediate visibility.
- `Off` (opt-in for users who want quiet operation) leaves the install silent and does not create `<GTCW>/BepInEx/WhiskeyRealism/tuning-logs/`.

Sidecar files:
- `tactical.jsonl`
- `campaign.jsonl`
- `performance.jsonl`
- `failures.jsonl`
- `health.jsonl`
- `manifest.json`
- `summary.md` when human summaries are enabled
- `issue-bundle.json` when issue bundle creation is enabled

Validation:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj -- --validate-telemetry "<session-dir>"
```

Issue bundles contain redacted telemetry files only. They do not contain save files, copied game DLLs, tokens, unrelated plugin logs, or raw Windows usernames.

## Hot-path performance scopes

`TelemetryPerf.Scope` instruments per-Tick cost into `performance.jsonl` (Performance category, gated by profile — `Off` is a noop, `TacticalTuning` / `FullTuning` emit). Permanent diagnostic infrastructure as of 2026-05-19; no separate config flag.

Per-side scopes wrapped around the orchestrator hot path:

| Scope | Threshold | Wraps |
|---|---|---|
| `tactical.orchestrator-tick` | 2.0 ms | full `Tick()` body (both sides) |
| `tactical.operations-ledger` | 2.0 ms | `DriveOperationsLedger` body |
| `tactical.attach-direct-children` | 2.0 ms | `AttachDirectChildrenIfReady` (full) |
| `tactical.direct-child-discovery-snapshot` | 2.0 ms | inner `DirectChildDiscovery.Snapshot(alliance)` only |
| `tactical.attach-command-tree` | 2.0 ms | `AttachCommandTreeIfReady` (full) |
| `tactical.command-tree-runtime-snapshot` | 2.0 ms | inner `CommandTreeRuntime.Snapshot(alliance)` only |
| `tactical.snapshot-build.tick-cycle` | 5.0 ms | `TacticalBattleSnapshotBuilder.Build` from `DriveTickCycle` |
| `tactical.snapshot-build.operations-ledger` | 5.0 ms | `TacticalBattleSnapshotBuilder.Build` from `DriveOperationsLedger` |
| `tactical.snapshot-build.direct-child-cycle` | 5.0 ms | `TacticalBattleSnapshotBuilder.Build` from `DriveDirectChildCycle` |
| `tactical.snapshot-build.evidence-bundle` | 5.0 ms | `ArmyEvidenceBuilder.Build` inside `Build` (walks `completeunitlist`) |
| `tactical.snapshot-build.objective-records` | 5.0 ms | `BuildObjectiveRecordsFromBattle` inside `Build` (walks `completeunitlist`) |
| `tactical.worker.cycle` | 50.0 ms | Full `TacticalOrchestratorWorkerAnalysis.Analyze` body on the worker thread (off main). Wide threshold because worker time doesn't affect frame budget — only flags genuinely-pathological cycles. |
| `tactical.worker.cycle.publish` | 1.0 ms | Atomic `Interlocked.Exchange` decision-snapshot publish from worker. Should be sub-millisecond; any spike here indicates contention on the static `_current` field. |

Each emission carries `durationMs` plus standard counters. `slow=true` fires when `durationMs >= threshold`.

`TacticalHeavyGate` emissions (Gate category) additionally carry GC pressure data per side, captured at every gate decision: `gcCountAbs` (absolute `GC.CollectionCount(0)` at decision time) and `gcDelta` (collections since last gate decision on this side). Mono uses Boehm (non-generational) so a single counter suffices; if the runtime ever migrates, restore per-generation tracking. Reset per battle in `ResetRuntimeTickState`. Use `gcDelta` correlated with `snapshot-build.*` `durationMs` to determine whether frame hitches at high compression are allocation churn (high `gcDelta`) or algorithmic cost (high `durationMs` with low `gcDelta`).

## Worker-thread events

The Slice 2 orchestrator worker (see [`docs/tactical-orchestrator.md`](tactical-orchestrator.md) § "Threading architecture (2026-05-19)") emits two tactical event tags:

| Event | Category | When it fires | Fields |
|---|---|---|---|
| `TacticalOrchestratorParityMismatch` | Gate | Worker-published `TacticalSideDecision` diverges from the synchronous orchestrator state for one of the first 20 cycles per `(battleSeq, alliance)`. Emits once per key, then comparator stops for that key. | `allianceId`, `battleSequence`, `syncIntent`, `workerIntent`, `syncMainEffort`, `workerMainEffort` |
| `TacticalOrchestratorWorkerFault` | Health | Worker loop body caught an exception. Bounded to ≤64 emissions per session. | `exceptionType`, `message`, `cyclesProcessed` |

`[TacticalOrchestratorWorkerParity] window-clean` is a non-event `OnceLog.Info` line written to `LogOutput.log` (not telemetry) when a side passes 20 clean comparisons.

## Smoke Checklist

Task 10 owns deploy, hash verification, and runtime smoke. Do not claim runtime smoke from Task 9 build or harness output.

For a telemetry smoke run:
- Close GTCW before deploy.
- Build `dist/WhiskeyRealism.dll`.
- Deploy to `<GTCW>/BepInEx/plugins/WhiskeyRealism.dll`.
- Verify the deployed plugin timestamp is fresh, and confirm `dist/WhiskeyRealism.dll` and the deployed plugin DLL have matching size and `sha256sum`.
- Enable a tuning profile intentionally, usually `FullTuning` for framework smoke.
- Launch GTCW and start a career/battle path that emits tactical and campaign rows.
- Confirm a new session appears under `<GTCW>/BepInEx/WhiskeyRealism/tuning-logs/<session-id>/`.
- Confirm `manifest.json` records `loggingProfile`, detail `queueCapacity`, `flushMilliseconds`, `flushRows`, session cap, rotation cap, retained sessions, and output files.
- Run the telemetry validator against the session directory.
- Scan `LogOutput.log` for repeated `Exception`, `ERROR`, `missing-anchor`, Harmony failures, telemetry sink failures, or log spam.
- Confirm tuning rows moved to sidecars and bounded serious warnings/errors remain visible in `LogOutput.log` where appropriate.

## Evidence Boundary

The telemetry framework was merged after `telemetry-framework-plan` fast-forwarded into `main` at `feffbfc`. Current artifact proof is the later deployed `562a61b5cd0cbbedc6d6002a349cd3d68ebf50ea1d60c941e3a5a9deeaafc57a` DLL (1327104 bytes; `1110 PASS`; clean build; matching deployed hash).

Runtime proof is partial. The prior loaded `695be770...` TacticalTuning session created tuning sidecars and wrote Active tactical rows without observed queue drops or sink failures, but it did not prove the current `562a61b5...` DLL loaded, the `Off` / `CampaignTuning` / `FullTuning` profile paths, validator output on a real session, cap transition, or production-cap restore. Fresh telemetry smoke must restart GTCW on `562a61b5...` and close those items.
