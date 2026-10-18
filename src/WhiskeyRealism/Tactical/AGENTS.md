# Tactical Logic Instructions

These rules apply to non-patch logic under `src/WhiskeyRealism/Tactical/` (top-level scorers/ledgers like `TacticalSectorLedger`, `TacticalOddsDoctrine`, `TacticalCommanderIntent`, `TacticalDeploymentTelemetry`) and `src/WhiskeyRealism/Tactical/Orchestrator/` (per-battle echelon stack: `TacticalBattleCoordinator`, `TacticalBattleOrchestrator`, `ArmyOrchestrator`, `DirectChildAllocator`/`Discovery`/`Gate`, `ArmyIntentInference`, `EnemyVisibleState`, etc.).

The rules are identical to [`../Strategic/AGENTS.md`](../Strategic/AGENTS.md) — strategic ledgers and tactical orchestrator code share the same pure-logic-vs-runtime-adapter split, the same cadence/idempotency requirements, and the same harness coverage expectations. This file exists so path-based `AGENTS.md` discovery picks the rules up for sessions opened anywhere under `Tactical/`.

## Design Rules

- Keep scorer, ledger, doctrine, allocator, gate-decision, and catalog logic pure when possible.
- Do not reference Unity or vanilla game types from pure ledger/scorer/allocator classes. `DirectChildAllocator`, `DirectChildEvidenceBuilder`, `TacticalDirectChildGate`, `ArmyIntentInference` are exemplars.
- Put reflection-heavy extraction in runtime adapter classes (e.g. `TacticalBattleCoordinatorRuntime.cs`, `DirectChildDiscoveryRuntime.cs`), not pure logic. Use `partial class` to split a type into a pure half (in-test-csproj) and a runtime half (excluded from test csproj).
- Orchestrator state writes belong in coordinator cadence/event handlers and the per-battle orchestrator tick cycle, not Harmony patches.
- Prefer deterministic inputs/outputs that can be covered by the console test harness.
- Use focused DTOs for telemetry signatures so logs can be bounded and compared. `OnceLog.Info(key, message)` already wraps the message with `[once:KEY]` — do not duplicate the prefix in the message body. OnceLog keys are static strings: never embed instance ids or unit names in a key.
- Per-battle static state (caches, stamps, cooldowns, latches) registers its reset with `BattleScope.Register(name, reset)` from the owner's static constructor (`Util/ScopedState.cs`); per-campaign state uses `CampaignScope.Register`. Coordinator teardown calls `BattleScope.ResetAll()` — never add a hand-wired reset call to a teardown path. Caches hold only immutable data; read live owner/rout/active flags each tick.
- Battle writes go through the battle core's order executor (`Battle/OrderExecutorRuntime`) behind `TacticalWriteGuard`, and the single owners (`Util/TacticalStanceWriter` for `ai_stance`, #44 for `macroai`); see root `AGENTS.md` § Patch hygiene. A battle mover is an `IBattleIntentProducer`: it reads the `BattleSnapshot` only, submits intents, writes nothing, and is added to `BattleProducerRegistry.Order` and `BattleCoreTick.CreateProducer` (no static-constructor registration). Read enemies only through the snapshot's observations (fog-honest), never vanilla's unfiltered enemy fields. The AI reads the victory bar exactly as the player sees it, `GetVictoryPointsRelation`, and no other aggregate of the enemy's true state: user decision D39, pinned by FEAS-PIN-O. The commander opportunity producer (`Producers/OpportunityIntentProducer`, registry position 12 of 13, before Posture) submits no intents: it runs the pure `OpportunityTick` (detector, per-commander decision, exploit lifecycle) and fills the per-alliance `ExploitTable`, which `PostureIntentProducer` reads as an override of the ledger order for the exploit asset (its descendants follow). Counterstroke is named only in `ExploitLifecycle.cs` and `TacticalCommandTaskToStanceMapper.cs`. Its opening perception goes through `PerceivedGroundLattice` (at most 128 `IsPerceived` calls a tick); nothing in the opportunity tick allocates after warm-up (OPP-ALLOC).

## Cadence Rules

- Doctrine timers (phase budgets, windows, cooldowns, hysteresis, holds, ledger decay) read `TacticalDoctrineTime` — battle game-hours from the coordinator, frozen across vanilla's night end-of-day cycle. Never use wall-clock time for doctrine.
- Avoid per-tick allocation: build evidence once per side per tick and reuse scratch buffers; gate heavy work on a content signature (`TacticalBattleStateSignature.SignatureEquals`, `DirectChildEvidence.SignatureEquals`) rather than on reference identity. Where a step must run every tick (e.g. `ArmyOrchestrator.ObserveDirectChildEvidenceWithZones`, whose hysteresis counts consecutive cycles), say so in a comment.
- Runtime adapters degrade to empty/no-op inputs on reflection failure and log a bounded warning. Never throw out of the orchestrator tick path.

## Tests

- Add pure tests for new scorer thresholds, allocator role rules, gate decision branches, ledger outputs, cooldown behavior, and edge cases.
- Include alliance bounds, empty input, stale input, and overmatch/undermatch cases when relevant.
- If the logic feeds a patch, test the pure decision before patch wiring.
- The test project uses explicit `<Compile Include>` entries — see [`../../../tests/WhiskeyRealism.Tests/AGENTS.md`](../../../tests/WhiskeyRealism.Tests/AGENTS.md) for the wiring rule.
