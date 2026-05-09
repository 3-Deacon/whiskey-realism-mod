# Tactical Logic Instructions

These rules apply to non-patch logic under `src/WhiskeyRealism/Tactical/` (top-level scorers/ledgers like `TacticalSectorLedger`, `TacticalOddsDoctrine`, `TacticalCommanderIntent`, `TacticalDeploymentTelemetry`) and `src/WhiskeyRealism/Tactical/Orchestrator/` (per-battle echelon stack: `TacticalBattleCoordinator`, `TacticalBattleOrchestrator`, `ArmyOrchestrator`, `DirectChildAllocator`/`Discovery`/`Gate`, `ArmyIntentInference`, `EnemyVisibleState`, etc.).

The rules are identical to [`../Strategic/AGENTS.md`](../Strategic/AGENTS.md) — strategic ledgers and tactical orchestrator code share the same pure-logic-vs-runtime-adapter split, the same cadence/idempotency requirements, and the same harness coverage expectations. This file exists so path-based `AGENTS.md` discovery picks the rules up for sessions opened anywhere under `Tactical/`.

## Design Rules

- Keep scorer, ledger, doctrine, allocator, gate-decision, and catalog logic pure when possible.
- Do not reference Unity or vanilla game types from pure ledger/scorer/allocator classes. `DirectChildAllocator`, `DirectChildEvidenceBuilder`, `TacticalDirectChildGate`, `ArmyIntentInference` are exemplars.
- Put reflection-heavy extraction in runtime adapter classes (e.g. `TacticalBattleCoordinatorRuntime.cs`, `DirectChildDiscoveryRuntime.cs`), not pure logic. Use `partial class` to split a type into a pure half (in-test-csproj) and a runtime half (excluded from test csproj).
- Orchestrator state writes belong in coordinator cadence/event handlers and the per-battle orchestrator tick cycle, not Harmony patches.
- Prefer deterministic inputs/outputs that can be covered by the console test harness.
- Use focused DTOs for telemetry signatures so logs can be bounded and compared. `OnceLog.Info(key, message)` already wraps the message with `[once:KEY]` — do not duplicate the prefix in the message body.

## Cadence Rules

- Per-tick allocations must signature-skip unchanged input. `DirectChildEvidence.SignatureEquals` and the orchestrator's idempotent `ObserveDirectChildEvidence` short-circuit are the reference pattern; `ReferenceEquals` on the cached intent list is part of the contract.
- Replan-style transitions invalidate stale evidence caches (`ArmyOrchestrator.Replan` sets `_hasObservedEvidence = false`).
- Runtime adapters degrade to empty/no-op inputs on reflection failure and log a bounded warning. Never throw out of the orchestrator tick path.

## Tests

- Add pure tests for new scorer thresholds, allocator role rules, gate decision branches, ledger outputs, cooldown behavior, and edge cases.
- Include alliance bounds, empty input, stale input, and overmatch/undermatch cases when relevant.
- If the logic feeds a patch, test the pure decision before patch wiring.
- The test project uses explicit `<Compile Include>` entries — see [`../../../tests/WhiskeyRealism.Tests/AGENTS.md`](../../../tests/WhiskeyRealism.Tests/AGENTS.md) for the wiring rule.
