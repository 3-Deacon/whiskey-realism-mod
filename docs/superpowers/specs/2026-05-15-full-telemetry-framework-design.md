# Full Telemetry Framework Design

Status: approved full-scope design artifact; implementation plan required
before code changes.
Date: 2026-05-15
Owner: Whiskey Realism telemetry / AI tuning workstream

This spec defines the professional telemetry framework for Whiskey Realism. It
is not a patch implementation plan. It should be converted into one execution
plan that includes exact file paths, tests, runtime smoke, deployment/hash
verification, and rollback boundaries before implementation starts.

## Problem

Whiskey Realism needs tuning-grade evidence for both tactical battle AI and
campaign AI. The current logging surface has useful data, but it is fragmented:

- high-volume tactical rows write directly to BepInEx `LogOutput.log`;
- "verbose" means different things in different modules;
- tactical, campaign, performance, and failure rows use ad hoc formats;
- row throttling lives in several unrelated dictionaries and log gates;
- failure paths can be once-logged without later summary counts;
- performance impact from telemetry itself is not visible enough;
- customers can accidentally get huge logs if diagnostic flags are left enabled.

The current local log proves the failure mode. A stale runtime log contained
`251020` lines and about `133 MB` of output, including more than `134000`
`[TacticalDecisionMatrix]` rows, more than `26000` `[TacticalPlayerOrder]`
rows, and more than `15000` `[TacticalFormationChange]` rows. That volume is
useful for development only when structured, scoped, and separated from the
customer log.

## Goals

- Default customer installs are quiet: no tuning sidecars and no tactical or
  campaign decision firehose in `LogOutput.log`.
- Tuning profiles produce professional, machine-readable JSONL plus a compact
  human summary.
- Tactical and campaign AI tuning have equal support.
- AI decision rows explain inputs, scores, chosen action, rejected alternatives
  where practical, gate outcomes, and vanilla write/no-write results.
- Performance telemetry is first-class: slow scopes, emitted rows, dropped
  rows, queue depth, file bytes, flush cost, and capped categories are visible.
- Telemetry failures are not silent. Bounded main-log warnings report sidecar
  write failures, schema failures, cap/rotation events, dropped rows, missing
  anchors, and repeated exceptions.
- Telemetry must never throw through Harmony patches or block game logic when
  sinks fail.

## Non-Goals

- Do not replace BepInEx logging globally.
- Do not add new AI behavior as part of the telemetry framework.
- Do not rewrite vanilla movement, campaign movement, or W&L order delivery.
- Do not make customer support logs verbose by default.
- Do not store binary game data or copied game DLLs in telemetry output.

## Approved Decisions

- Implement the full telemetry framework in one execution session, not the
  smaller wrapper/event-bus-only slice.
- Customer default profile is `Off`.
- Tuning output lives under:

```text
<GTCW>/BepInEx/WhiskeyRealism/tuning-logs/<session-id>/
```

- Sidecars are JSONL-first with a human `summary.md`.
- Retention keeps the last `2` tuning sessions.
- Total tuning cap is `250 MB`.
- Individual JSONL files rotate around `25 MB`.
- When caps are hit, low-priority detail stops first while failures,
  performance counters, and summaries continue.
- Telemetry uses a bounded in-memory queue plus a dedicated writer thread for
  sidecar sinks. Harmony patches and AI runtime paths enqueue events and return;
  they do not synchronously append high-volume JSONL rows on the Unity thread.
- Crash durability is explicit: sidecars are append-only JSONL, flushed at a
  fixed cadence and on shutdown. A partial final line after a hard crash is
  acceptable; silent loss of whole buffered batches is not.
- `inputSignature` is mandatory on migrated decision rows. It is computed at the
  event adapter from the same inputs already visible to that decision surface;
  it must not change AI behavior or require a separate behavior refactor.
- Manifest build provenance uses a runtime SHA-256 of
  `Assembly.GetExecutingAssembly().Location` on day one. Build-time generated
  metadata is not required for the first implementation.
- FullTuning is expected to rotate files and may hit cap-cut behavior in long
  sessions. That is designed behavior, not an error, as long as summary counts
  and failure/performance rows survive.

## Architecture

Add a new `Telemetry/` subsystem under `src/WhiskeyRealism/`.

Core components:

| Component | Responsibility |
|---|---|
| `TelemetryProfile` | Configured profile: `Off`, `TacticalTuning`, `CampaignTuning`, `FullTuning`. |
| `TelemetryEvent` | Typed event object with stable schema and safe field bag. |
| `TelemetryRouter` | Single entrypoint. Applies profile gates, category budgets, severity rules, and sink routing. |
| `TelemetryBudget` | Session cap, file rotation, category limits, dropped-row counters, and cap-state decisions. |
| `TelemetrySession` | Session identity, output directory, manifest state, retention cleanup. |
| `TelemetryHealth` | Bounded main-log warnings for failures, caps, dropped rows, and disabled tuning. |
| `TelemetryPerf` | Stopwatch helpers and counters for game scopes and telemetry sinks. |
| `TelemetryJson` | JSONL serialization with safe escaping and null/finite-value handling. |
| `TelemetrySummary` | Human `summary.md` aggregation: decisions, gates, writes, failures, and slowest scopes. |

Sinks:

| Sink | Output |
|---|---|
| `MainLogSink` | Startup identity, profile state, sidecar path, serious warnings/errors, cap/dropped-row warnings, and summary-written line only. |
| `JsonlTuningSink` | Structured tactical/campaign/performance/health/failure rows. |
| `SummarySink` | Session counters and `summary.md`. |
| `IssueBundleManifest` | Build/config/profile/session/files/caps manifest for later bug-report bundling. |

All new telemetry should flow through `TelemetryRouter`. Direct
`Plugin.Log.LogInfo(...)` remains acceptable for one-time startup messages and
bounded compatibility warnings only.

### Threading And Durability Model

The implementation must use this model, not leave it to the plan:

- `Off` profile starts no writer thread and allocates no session folder.
- Tuning profiles create a `TelemetrySession`, bounded `TelemetryQueue`, and one
  background writer thread.
- Producers may be Harmony patches, orchestrator ticks, strategic cadence, or
  helper runtimes. They call `TelemetryRouter.Emit(...)`, which performs cheap
  profile/category checks, builds a safe event envelope, and enqueues it.
- The queue is multi-producer/single-consumer. Use a small lock-protected queue
  or equivalent netstandard2.1-compatible primitive; do not introduce a
  dependency just to get a lock-free queue.
- Queue capacity is bounded. When full, drop categories in this order:
  `Trace`, low-priority `State`, low-priority `Decision`, then low-priority
  `Gate`/`Write`. Never drop `Failure`, `Health`, or telemetry self-failure
  summaries.
- Dropped rows increment counters by layer/category/event and produce bounded
  main-log warnings plus summary rows.
- The writer thread drains queue batches, writes append-only JSONL, and flushes
  at least every `250 ms` or `256` rows, whichever comes first.
- Shutdown hooks must flush on `OnDestroy` / application quit with a bounded
  timeout. If shutdown flush times out, emit a final main-log warning when
  possible and record the unflushed count in `manifest.json` if possible.
- JSONL writes must be atomic at the row-construction level: construct the full
  line before enqueue; writer appends complete lines. A hard crash may leave a
  partial final line, which validators must tolerate.

### Patch-State Carve-Out

Repo policy says strategic and tactical-orchestrator mod state is read-only to
Harmony patches. Telemetry counters, queue state, drop counts, and writer health
are explicitly outside strategic/orchestrator doctrine state. They live only in
the `Telemetry/` subsystem, must not feed AI decisions, and must follow the
threading model above.

## Profiles

### `Off`

Customer default. It emits no tuning sidecars and no decision/trace rows.
`LogOutput.log` receives only:

- plugin loaded/profile active;
- telemetry disabled confirmation;
- severe Harmony or telemetry framework failures;
- bounded missing-anchor or repeated-exception summaries.

### `TacticalTuning`

Enables tactical sidecars and tactical performance telemetry:

- battle/session identity;
- tactical operation selection;
- command assignment;
- posture execution;
- movement/path/formation diagnostics;
- charge, reserve, fallback, artillery, fire-control, W&L player-order gates;
- vanilla tactical write attempts and results;
- tactical patch timings and event counts.

Campaign rows stay off except health/failure rows needed to interpret a battle
session.

### `CampaignTuning`

Enables campaign sidecars and campaign performance telemetry:

- campaign heartbeat and date;
- objective selection and historical operation decisions;
- defense intent;
- formation/front/army-area ledgers;
- fiscal and construction steering;
- coordinated operation movement;
- W&L campaign/diary guard decisions;
- campaign write attempts and results.

Tactical battle rows stay off unless a tactical battle actually starts and the
row is necessary to report a cross-layer failure.

### `FullTuning`

Enables both tactical and campaign telemetry with stricter category budgets.
This is for end-to-end investigation and must not bypass the `250 MB` total cap
or `2` session retention.

## Config Contract

Extend the existing `[Telemetry]` config section with:

```ini
[Telemetry]
Logging Profile = Off
Max Tuning Log MB = 250
Tuning Log File Rotate MB = 25
Tuning Log Retained Sessions = 2
Emit Human Summary = true
Telemetry Performance Warnings = true
Create Issue Bundle On Shutdown = false
```

Existing diagnostic booleans become subordinate gates under the selected
profile. They must not create high-volume `LogOutput.log` spam when
`Logging Profile = Off`.

Existing behavior gates remain behavior gates. `Logging Profile = Off` must
never disable shipped tactical/campaign behavior, safety fixes, W&L behavior
gates, or performance governors.

Compatibility rule:

- If old flags are enabled but `Logging Profile = Off`, write one bounded
  main-log warning explaining that tuning telemetry is disabled by profile.
- If a tuning profile is enabled, old flags may expand detail inside that
  profile, but still route to sidecars and budgets.

Diagnostic/detail gates that telemetry may subordinate:

| Existing config | Telemetry treatment |
|---|---|
| `VerboseLogging` | Detail expansion only; no main-log firehose under `Off`. |
| `PlanTrace` | Campaign decision detail under `CampaignTuning` / `FullTuning`. |
| `SuccessionTrace` | Campaign trace under `CampaignTuning` / `FullTuning`. |
| `FiscalTrace` | Campaign fiscal detail under `CampaignTuning` / `FullTuning`. |
| `FiscalTelemetryCsv` | Compatibility switch; JSONL is authoritative for this framework. |
| `DirectorVerboseTrace` | Campaign trace under `CampaignTuning` / `FullTuning`; it already lives in `[Telemetry]`. |
| `TacticalObserverVerboseLogging` | Tactical trace expansion under `TacticalTuning` / `FullTuning`. |
| `EnableTacticalDecisionMatrixLogging` | Tactical state/decision matrix detail under `TacticalTuning` / `FullTuning`. |
| `TacticalDecisionMatrixMinSecondsBetweenSnapshots` | Detail throttle for migrated matrix events. |
| `TacticalDecisionMatrixMaxRows` | Detail row cap for migrated matrix events. |
| `EnableTacticalBugTelemetry` | Diagnostic only; route to sidecars under tactical profiles. |
| `EnableTacticalReserveIntentTelemetry` | Read-only tactical diagnostic; route to sidecars under tactical profiles. |
| `TacticalOrchestratorVerboseLogging` | Tactical orchestrator trace expansion under tactical profiles. |
| `EnableTacticalRegimentDiagnostics` | Tactical trace/state diagnostic under tactical profiles. |
| `TacticalRegimentDiagnosticNames` | Watchlist for migrated regiment trace rows. |
| `EnableTacticalDeploymentObserver` | Read-only tactical diagnostic under tactical profiles. |
| `EnableWlPlayerOrderDoctrineDiagnostics` / `EnablePlayerOrderDoctrineDiagnostics` | W&L diagnostic detail under tactical/campaign profiles. |
| `DefenseIntentVerboseLogging` | Campaign defense detail under campaign profiles. |
| `ConstructionTelemetryEnabled` | Campaign construction summary/detail under campaign profiles. |
| `ConstructionVerboseLogging` | Campaign construction trace under campaign profiles. |
| `EnableWlCampVerboseTrace` | W&L campaign/camp trace under campaign profiles. |

Behavior gates that telemetry must not subordinate or override:

| Existing config | Boundary |
|---|---|
| `Enabled` | Master mod behavior switch; telemetry does not reinterpret it. |
| `TacticalCommanderModeRaw` / `TacticalCommanderModeValue` | Owns tactical command behavior mode. Telemetry observes, never changes it. |
| `EnableTacticalBattleOrchestrator`, `EnableTacticalOrchestratorArmy`, `EnableTacticalOrchestratorIntentInference`, `EnableTacticalOrchestratorDirectChildGate`, `EnableTacticalOrchestratorReserveCommitGate`, `EnableTacticalOrchestratorChargeGate` | Tactical behavior gates; logging profile cannot disable or enable behavior. |
| `EnableTacticalMacroStanceScorer`, `EnableTacticalGroupSectorStance`, `EnableTacticalCommanderIntentDoctrine`, `EnableTacticalLocalReactionDoctrine`, `EnableTacticalChargeDenial`, `EnableTacticalReserveListMutation`, `EnableTacticalArtilleryDoctrine`, `EnableTacticalWithdrawalDoctrine` | Tactical behavior gates; telemetry only records their decisions/results. |
| `EnableTacticalFallbackRetreatNullGuard`, `EnableTacticalPathfinderDiscipline`, `EnableTacticalHqLinkGuard`, `EnableTacticalReserveOrderDelayGuard`, `EnableTacticalDeploymentTerrainDiscipline`, deployment terrain numeric settings | Tactical guard/behavior settings; telemetry does not alter them. |
| `EnableWlTacticalChargeGuard`, `EnableWlPlayerSubordinateOrderBridge`, `EnablePlayerOrderDoctrine`, `EnableWlOperationNullGuard` | W&L behavior/safety gates; telemetry only records outcomes. |
| `EnableConstructionIntentLedger`, `EnableHistoricalOperationDoctrine`, `EnableDefenseIntentLedger`, `EnableConstructionSiteSteering`, `EnableSupplyDepotSteering`, `EnableFortSteering`, `FortConstructionGovernorEnabled`, `EnableTelegraphAI`, `EnableRailroadSteering` | Campaign behavior/ledger gates; telemetry only records decisions/results. |
| `FastForwardAiCatchUp`, `FastForwardAiFrameBudgetMs`, `FastForwardAi20xExtraPasses`, `FastForwardAi50xExtraPasses`, `FastForwardAiSlowFrameThresholdMs`, `FastForwardAiSlowFrameCooldownFrames`, `CampaignAiGovernorEnabled`, `CampaignAiGovernorMaxPasses20x`, `CampaignAiGovernorMaxPasses50x`, `CampaignAiGovernorFrameBudgetMs` | Performance behavior governors; telemetry records timings and slow paths without changing policy. |
| W&L camp realism/tuning settings and vanilla settings locks | Gameplay behavior/config; telemetry records only. |
| `ForceAllSuccessionEvents` | Test behavior switch; telemetry records that it is active but does not gate it. |

## Event Schema

Each JSONL row uses schema `wr.telemetry.v1`.

Required stable fields:

```json
{
  "schema": "wr.telemetry.v1",
  "ts": "2026-05-15T13:22:01.123Z",
  "sessionId": "20260515-081653-ec00120f",
  "profile": "TacticalTuning",
  "layer": "Tactical",
  "category": "Decision",
  "event": "CommandAssignment",
  "severity": "Info",
  "battleId": "stafford-ch-1861-06-01-001",
  "campaignDate": "1861-06-01",
  "side": 0,
  "alliance": 0,
  "unit": "2nd_Division#-229220",
  "phase": "Scouting",
  "decision": "Probe",
  "reason": "support-required",
  "durationMs": 0.18,
  "fields": {}
}
```

Rules:

- Required fields are always present.
- Unknown text values serialize as `"-"`.
- Unknown numeric ids serialize as `-1`.
- Invalid floats serialize as `0.0` plus a `fields.invalidFloat=true` marker
  when the value matters.
- Free-form messages are allowed only under `fields.message`; tuning logic must
  prefer stable field names.
- Exception events include type, message, owner, first count, total count, and
  a bounded stack summary when available.

## Categories

| Category | Purpose |
|---|---|
| `Health` | Startup, profile, config, sidecar path, retention, cap/rotation state. |
| `Failure` | Exceptions, missing anchors, reflection failures, Harmony failures, schema failures, sidecar failures, dropped-event warnings. |
| `Performance` | Campaign/tactical tick cost, patch cost, router/sink cost, queue depth, emitted/dropped counts. |
| `Decision` | AI choices: objective, operation, command task, stance, charge, reserve, fallback, artillery, construction, fiscal, defense. |
| `Gate` | Safety and doctrine gates: W&L, player-subordinate, reserve, charge, path, fallback, support-required, endurance. |
| `Write` | Vanilla write attempts and outcomes: `SetWaypoint`, `SetGroupFormation`, `ChangeStance`, `ChangeCombatBehavior`, `SetWithdrawal`, campaign `MoveUnitTo`, W&L `CheckCurrentOrderUpdate`. |
| `State` | Bounded battlefield/campaign snapshots, not raw every-frame dumps. |
| `Trace` | Deep-dive detail rows under tuning only; first category cut when caps are hit. |

Current `Trace` homes include `TacticalRegimentTrace`, `TacticalWaypointDrift`,
`TacticalPathfinderDiscipline` per-attempt rows, `TacticalCascade`,
`Director:trace`, succession trace, construction candidate rejection trace, and
W&L camp verbose trace.

`State` is bounded by signature-change gates, interval gates, and per-scope row
caps. The implementation may reuse existing `OnceLog`, signature-gated emitters,
and min-seconds settings, but state rows must route through the central budget
so caps and dropped counters are visible in the summary.

## AI Decision Rows

Tuning rows must answer:

- what did the AI see;
- what options were considered;
- what option won;
- what was rejected and why;
- what gates allowed or denied the action;
- whether a vanilla write was attempted;
- whether the vanilla write succeeded, failed, was suppressed, or was skipped.

Minimum decision fields:

```text
decision
reason
confidence
score
selectedTarget
gateResult
gateReason
writeAction
writeResult
inputSignature
```

`inputSignature` must be generated by telemetry adapters from the current
decision input values already visible at the emission site. For example, a
tactical command assignment signature may combine side, operation phase, command
node id, role, objective id, target bucket, confidence bucket, reserve fraction
bucket, and contact confidence bucket. A campaign objective signature may
combine alliance, date bucket, objective id, operation id, score bucket, posture,
and reason. If a migrated event can only build a coarse signature, it still
emits `inputSignature` plus `fields.inputSignatureSource="coarse"`. It must not
silently omit the field or change AI decision APIs merely to make a prettier
hash.

When practical, include rejected alternatives as a compact list:

```text
fields.rejected=AttackObjective:low-support|Fallback:odds-favorable|Reserve:held-reserve
```

## Performance Telemetry

Performance is part of tuning, not an afterthought.

Scopes to instrument:

- strategic/campaign daily review;
- front, army-area, formation, fiscal, construction, defense, historical
  operation, and coordinated-operation slices;
- tactical observer pass;
- tactical orchestrator tick;
- command assignment;
- posture executor;
- high-cost patch surfaces;
- JSONL serialization;
- file write/flush;
- summary update.

Performance event fields:

```text
scope
durationMs
slow
thresholdMs
eventsEmitted
eventsDropped
queueDepth
bytesWritten
category
layer
```

Disabled-profile overhead must be close to a cheap profile check. Telemetry
failures or slow sinks must never delay or throw through gameplay logic.

## Sidecar Files

Each session folder contains:

| File | Content |
|---|---|
| `manifest.json` | Plugin version, runtime assembly SHA-256, profile, start/end, config snapshot, output files, cap/rotation state, dropped counters. |
| `health.jsonl` | Profile, startup, config, sidecar state, cap/rotation, sink health. |
| `performance.jsonl` | Timing, queue, bytes, emitted/dropped rows, slow scopes. |
| `tactical.jsonl` | Tactical decisions, gates, writes, state, traces. |
| `campaign.jsonl` | Campaign decisions, gates, writes, state, traces. |
| `failures.jsonl` | Exceptions, missing anchors, Harmony/reflection/schema/sink failures. |
| `summary.md` | Human summary for quick inspection. |
| `issue-bundle.json` | Redacted manifest for user-attached bug reports; present when bundle creation is enabled. |

When `Create Issue Bundle On Shutdown = true`, shutdown also creates an issue
bundle archive in the session folder containing `manifest.json`, `summary.md`,
`issue-bundle.json`, failures, health, performance, and rotated tuning JSONL
files still present after retention/cap rules. The bundle must include a
redacted telemetry config snapshot and must not include copied game DLLs, save
files, tokens, Windows usernames, or arbitrary BepInEx logs from other plugins.
Whiskey telemetry rows are intended to contain unit names, dates, battle ids,
configuration state, and decision evidence, not player-identifying data.

Rotated files use suffixes:

```text
tactical.001.jsonl
tactical.002.jsonl
campaign.001.jsonl
```

When the total session cap is hit:

1. stop `Trace`;
2. stop low-priority `State`;
3. keep `Health`, `Failure`, `Performance`, and aggregate summary counters;
4. emit one bounded main-log warning per cap transition.

Per-category budget policy:

- `Failure`, `Health`, summary counters, cap/dropped counters, and final
  manifest updates are protected.
- `Performance` detail is protected until the total cap is reached; after that,
  keep slow-scope summaries and aggregate counters only.
- `Trace` has the smallest soft budget and is cut first.
- High-volume matrix/state rows have independent row and byte budgets so they
  cannot starve `Decision`, `Gate`, or `Write` rows.
- `FullTuning` is allowed and expected to rotate and hit cap-cut behavior during
  long sessions. The pass condition is not "no cap"; it is "cap behavior is
  visible, bounded, and summaries remain useful."

Session retention:

- Session ids use UTC timestamp with milliseconds plus process id and runtime
  assembly hash prefix:
  `yyyyMMdd-HHmmss-fff-p<pid>-<hash12>`.
- Retention runs at session start after the new session directory is created and
  again at clean shutdown.
- Sort by `manifest.startUtc`, then directory name, then mtime fallback.
- Keep the newest two tuning session directories, including the current one.
- If two sessions tie, lexicographic directory order is the tiebreaker.

## Summary Format

`summary.md` includes:

- session id, profile, start/end, plugin version, DLL hash if available;
- observed campaign dates and battles;
- event totals by layer/category/event;
- top tactical decisions by count and reason;
- top campaign decisions by count and reason;
- denied gates and rejection reasons;
- vanilla writes attempted/succeeded/failed/suppressed;
- slowest scopes and telemetry overhead;
- dropped/capped rows;
- repeated failures, missing anchors, and exceptions;
- recommended next inspection queries.

The summary must still be useful when detail rows were capped.

## Migration Scope

Default migration policy: every Whiskey tuning/diagnostic log tag not on the
main-log allowlist routes to sidecars under the active profile. The
implementation plan must grep the repo for bracketed tags and close every tag
as either sidecar-routed, main-log-allowlisted, or removed.

Main-log allowlist:

- plugin loaded/profile active;
- tuning disabled/profile compatibility warning;
- sidecar path;
- severe Harmony or telemetry failures;
- repeated-exception summaries;
- missing-anchor first occurrence and count summary;
- cap/rotation/dropped-event warnings;
- issue-bundle created/failed;
- summary written;
- explicit master-disabled message.

Known tactical tags to migrate or route by default:

- `[TacticalDecisionMatrix]`;
- `[TacticalCommandAssignment]`;
- `[TacticalCommandPosture]`;
- `[TacticalPostureSummary]`;
- `[TacticalOpsLedger]`;
- `[TacticalCommandTree]`, `[TacticalCommanderRoster]`,
  `[TacticalCommanderUnknown]`, `[TacticalCommanderMode]`;
- `[TacticalPlan]`, `[TacticalPlaybook]`, `[TacticalIntent]`,
  `[TacticalLocalReaction]`, `[TacticalReplan]`, `[TacticalCascade]`;
- `[TacticalMacro]`, `[TacticalMacroDecision]`, `[TacticalGroup]`,
  `[TacticalGroupDecision]`, `[TacticalSector]`, `[TacticalOdds]`;
- `[TacticalFormationChange]`;
- `[TacticalPathShape]`;
- `[TacticalWaypointDrift]`;
- `[TacticalOrder]`;
- `[TacticalCommand]`;
- `[TacticalPlayerOrder]`;
- `[TacticalCurrentOrder]`;
- `[TacticalCourierQueue]`;
- `[TacticalReserve]`, `[TacticalFallback]`, `[TacticalCharge]`,
  `[TacticalArtillery]`;
- `[TacticalReserveIntent]`, `[TacticalReserveMove]`,
  `[TacticalReserveDrift]`, `[TacticalReserveCommitGate]`,
  `[TacticalReserveOrderDelayGuard]`;
- `[TacticalChargeDeny]`, `[TacticalChargePreserved]`,
  `[TacticalChargeGuard]`, `[TacticalDoctrineCharge]`,
  `[TacticalOrchestratorChargeGate]`;
- `[TacticalFeud]`, `[TacticalFeudGuard]`;
- `[TacticalDirectChildDiscovery]`, `[TacticalDirectChildIntent]`,
  `[TacticalDirectChildGate]`;
- `[TacticalDeploymentPhase]` plus deployment observer rows;
- `[TacticalRegimentDiagnostics]`, `[TacticalRegimentTrace]`;
- `[TacticalObjectiveGuard]`, `[TacticalObjectiveMove]`,
  `[TacticalObjectiveMutation]`;
- `[TacticalOrchestrator]`;
- `[TacticalPathfinderDiscipline]`;
- `[TacticalHqLinkGuard]`;
- `[TacticalDiagnostic]`.

Known campaign/W&L/strategic tags to migrate or route by default:

- `[Heartbeat]`;
- `[DailyOps]`, `[DailyOps:Perf]`;
- `[HistoricalOperation]`;
- `[DefenseIntent]`, `[DefenseIntent:asset]`;
- `[FiscalIntent]`, `[FiscalTelemetry]`;
- `[ConstructionIntent]`, `[ConstructionTelemetry]`;
- `[FormationDirective]`, `[FrontLedger]`, `[ArmyArea]`;
- `[CampaignMap]`;
- `[CampaignPace]`, `[CollapseRisk]`, `[Director]`, `[Director:trace]`;
- `[OperationalProbe]`;
- `[CoordinatedOps]`, `[CoordinatedOps:Perf]`;
- `[ProjectDoctrine]`, `[ProjectAppointed]`, `[ProjectUnlock]`;
- `[W&LStartSelection]`, `[W&LCamp]`;
- W&L diary/campaign guard diagnostics and any future Whiskey W&L campaign
  tuning tag not on the main-log allowlist.

## Failure Handling

No silent failures:

- Router catches serialization and sink exceptions.
- Harmony patches catch telemetry exceptions separately from behavior logic.
- Sidecar write failure disables that sink, increments counters, and emits a
  bounded main-log warning.
- Missing anchors produce one main-log warning plus per-session failure counts.
- Dropped rows are counted by layer/category/event and reported in summary.
- Schema failures write to `failures.jsonl` if possible; if not, main-log
  warning reports the count.

No failure path should emit a warning every frame. Use per-signature windows and
summary counts.

Data sensitivity:

- Telemetry may contain unit names, commander names, battle identifiers,
  campaign dates, config values, and local mod/plugin file names.
- Telemetry must not include save files, copied DLLs, secret tokens, personal
  account identifiers, arbitrary user documents, or raw logs from unrelated
  plugins.
- Issue bundles must redact OS-user-specific paths where they appear in config
  snapshots or exception text.

## Validation

Add console harness coverage:

- `TelemetrySchemaTests`: required fields, JSON escaping, null handling, finite
  numeric handling, field bag safety.
- `TelemetryBudgetTests`: file rotation, total cap, category cut order,
  retained sessions equals `2`, dropped counters.
- `TelemetryProfileTests`: `Off`, `TacticalTuning`, `CampaignTuning`,
  `FullTuning`, legacy diagnostic flag compatibility, and behavior-gate
  independence from logging profile.
- `TelemetryPerformanceTests`: disabled-profile overhead, slow sink warning,
  telemetry exception isolation.
- `TelemetrySummaryTests`: summary includes counts, failures, drops, slowest
  scopes, and write results.
- `TelemetryThreadingTests`: queue overflow ordering, writer drain, flush
  cadence, shutdown flush timeout, and sink failure isolation.
- `TelemetryManifestTests`: runtime assembly hash, session id ordering, retention
  tiebreaks, redacted issue bundle manifest.

Runtime smoke gates:

1. `Off` smoke: launch game, confirm no tuning sidecar and no tactical/campaign
   decision firehose in `LogOutput.log`.
2. `TacticalTuning` smoke: start a battle, confirm tactical/performance/health
   files, decision/gate/write rows, and summary.
3. `CampaignTuning` smoke: advance campaign cadence, confirm campaign rows and
   no unrelated tactical firehose.
4. `FullTuning` smoke: confirm both layers write, rotation/caps work, and
   summary remains useful.
5. Budget-path smoke: temporarily lower rotate/cap settings in a tuning run so
   at least one rotation and one cap-cut transition occur. Confirm main-log
   warnings, dropped counters, protected failure/performance summaries, and
   valid JSONL after a partial-line-tolerant validation pass.

Implementation closeout must still follow repo policy:

- run console tests;
- run `./build.sh`;
- deploy `dist/WhiskeyRealism.dll`;
- verify local/deployed SHA-256 match;
- restart GTCW for fresh runtime smoke;
- inspect `LogOutput.log`;
- validate JSONL and `summary.md`.

## Implementation Boundaries

The implementation plan should be one session, but it must remain internally
ordered:

1. create telemetry core and tests;
2. add config and `Off` default behavior;
3. add sidecar session/retention/rotation;
4. migrate tactical firehose emitters;
5. migrate campaign tuning emitters;
6. add performance scopes;
7. add summaries and validation;
8. run build/deploy/hash and runtime smoke.

Every phase above is required for the first implementation. The plan may order
the work into checkpoints, but threading, flag ownership, sidecars, summaries,
performance telemetry, cap/rotation behavior, issue bundle manifest, and
tactical/campaign migration all ship in the same implementation session.

Do not archive existing tactical/campaign specs or plans because this framework
ships. This is infrastructure that supports future tuning work; living docs
should record the final config and smoke state after implementation.
