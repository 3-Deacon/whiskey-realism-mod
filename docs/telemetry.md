# Whiskey Realism Telemetry

**Default profile is `TacticalTuning`** as of 2026-05-18 (user direction): battles produce diagnostic sidecars by default. Disk usage is bounded by `Max Tuning Log MB` (default 250) and retention is capped to the newest 2 sessions, so this won't grow without limit. Set `Logging Profile = Off` in `<GTCW>/BepInEx/config/dev.kyle.whiskey-realism.cfg` if you want quiet operation.
Tuning profiles write under `<GTCW>/BepInEx/WhiskeyRealism/tuning-logs/<session-id>/`.

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

Each emission carries `durationMs` plus standard counters. `slow=true` fires when `durationMs >= threshold`.

`TacticalHeavyGate` emissions (Gate category) additionally carry GC pressure data per side, captured at every gate decision: `gcCountAbs` (absolute `GC.CollectionCount(0)` at decision time) and `gcDelta` (collections since last gate decision on this side). Mono uses Boehm (non-generational) so a single counter suffices; if the runtime ever migrates, restore per-generation tracking. Reset per battle in `ResetRuntimeTickState`. Use `gcDelta` correlated with `snapshot-build.*` `durationMs` to determine whether frame hitches at high compression are allocation churn (high `gcDelta`) or algorithmic cost (high `durationMs` with low `gcDelta`).

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
