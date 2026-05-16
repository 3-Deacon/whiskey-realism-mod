# Whiskey Realism Telemetry

Default profile is `Off`; customer installs should not create tuning sidecars.
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
- `Off` should leave customer installs quiet and should not create `<GTCW>/BepInEx/WhiskeyRealism/tuning-logs/`.

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

Task 10 deploy/hash proof was recorded on 2026-05-15 after `telemetry-framework-plan` was fast-forwarded into `main` at `feffbfc`: `dist/WhiskeyRealism.dll` and the deployed BepInEx plugin were both `1245184` bytes and matched SHA-256 `cfdb9018bc0cb7c0fcb7ba1e28acac0b1b119243856ef3a027716f8b9b930e75`.

Runtime smoke is still pending. The current `LogOutput.log` timestamp (`2026-05-15 07:54:28.457939800 -0500`) predates the `20:20` deploy, no `tuning-logs` directory existed in the read-only check, and new `[Telemetry]` config entries will not be proven until the freshly deployed plugin is launched.
