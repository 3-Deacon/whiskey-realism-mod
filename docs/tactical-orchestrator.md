# Tactical Orchestrator

Living status for the Grand Tactician tactical orchestrator workstream. This file is the current operational reference; slice specs and plans under `docs/superpowers/` are design/execution artifacts and may describe earlier checkpoints.

## Current State

- **Released game-facing version:** `v0.2.2` is still the latest public release.
- **Current main tactical orchestrator state:** O0/O1/O2/O3, #58 deployment observer, Slice 0 command-node tree, Slice 1 reserve commitment gate, Slice 3 #41 charge gate, #60 deployment terrain/facing discipline, #61 operations-ledger posture executor, and the full-spectrum doctrine consumers for stance/charge/reserve/fallback/artillery are implemented for `main`.
- **Current verification:** console harness `844 PASS / 0 FAIL`; `./build.sh` passed with `0 Warning(s)` / `0 Error(s)`; local `dist/WhiskeyRealism.dll` and deployed BepInEx plugin match SHA-256 `8eff50e672d70d6a10aa6a8a4c90d37408958026001eba236231e48618eea4dd` (939520 bytes).
- **Runtime smoke boundary:** Active full-spectrum doctrine smoke is pending because current `LogOutput.log` mtime `2026-05-12 14:35:02 -0500` predates the deployed plugin timestamp `2026-05-12 15:00:49 -0500`. A fresh battle log must prove `[TacticalCommanderMode] mode=Active`, `[TacticalOpsLedger]`, `[TacticalCommandAssignment]`, `[TacticalCommandPosture]`, `[TacticalPostureSummary]`, `[TacticalDoctrineCharge]`, `[TacticalOrchestratorChargeGate]`, `[TacticalGroupDecision]`, bounded reserve/fallback lines, and no repeated errors before the doctrine system is release-smoke-verified.
- **Latest log-driven fix:** the observed `1st Brigade` / `38th New York` courier case showed command nodes keyed by `Regiment.gameObject.GetInstanceID()` while tactical consumers resolved by the `Regiment` component id, so #61 could wire but not attach to the live ledger rows. Command intent and ledger lookup now prefer GameObject id and fall back to component id across #61, #41, #57, #59, B8 fallback observation, #35 monitor lookup, and #45 stance lookup. Hampton's Legion / 8th Brigade exposed allocator-ordering defects: a fixing-sector assignment, and then a main-effort assignment, could outrank severe local overmatch, leaving an isolated brigade fighting a superior force instead of falling back. Severe overmatch now produces a `Fallback` direct-child role before both `Fix` and `Main`. The 1st/3rd Brigade facing trace then exposed a #61 execution defect: command groups could report `groupformation=Line` while the visible `formation` remained `MarchColumn`, causing defensive/fallback correction to stall behind recent-order cooldowns; defensive/fallback corrections now compare visible, ordered, and group formation, retry urgent visible-mismatch formation corrections after 5 seconds, and pass a manual threat-facing rotation for close formation refreshes. The Hampton flank cluster exposed a separate #61 task-boundary issue: close-engaged defensive/fallback commands could still be blocked by pending courier state, and close-engaged formation correction was creating a fresh vanilla formation path that could return the group to interrupted `MarchColumn` movement. Close local flank emergencies still execute as `GuardFlank`; close defensive/fallback local reform can now bypass pending courier state for the non-moving correction, and close formation/facing corrections avoid `SetGroupFormation(newpath:true)` so groups reform in place instead of opening another path. The full-spectrum doctrine follow-up adds a battle picture, operation director, command doctrine orders, and consumer decisions so visible formed enemy contact raises objective confidence, high-odds visible weak points can produce attacks even under defensive macro, skirmisher/screen-only contact does not expose the main line, stale reserve/fallback paths are rolled back through #59 movement-state restore, and artillery remains conservative around friendly-close fire. The Active smoke prep fixes make observer posture telemetry use the side ledger's real commander mode instead of hardcoded monitor-only simulation, preserve committed high-odds attacks when reserves are low but odds remain favorable, and set the live local `Enable Tactical Orchestrator Charge Gate = true` config row. The latest Hampton-specific fix addresses the `8th_Brigade#-28534` loop where `pathInterrupted=True`, `activeMove=False`, stale path segments, and `orderState=1` kept replaying `RecoverInterruptedOrder target=RecoveryPath`; stalled interrupted stale paths no longer block retasking, and fallback doctrine now replaces the stale recovery waypoint with the doctrine fallback target. The latest Active-log fix raises visible enemy-line fallback confidence, appends visible enemy-line objectives alongside vanilla anchors, makes the operation director prefer exposed enemy-line objectives over inert generic anchors when odds and confidence support commitment, lets committed main-effort enemy-line attacks proceed at fallback confidence when exposed and favorable, treats named formed detachments and battle command groups as real line contact instead of zero-strength screen evidence, and reissues stale #61 attack waypoints when a duplicate target has `paths=0`, `activeMove=False`, and the group is still far from the target.

## Architecture

The tactical orchestrator is a read-owned per-battle brain layered over vanilla `AIBattle` methods:

- `TacticalBattleCoordinator` owns battle lifecycle and per-side orchestrators.
- `TacticalBattleOrchestrator` owns one side in the battle.
- `ArmyOrchestrator` owns the current army plan, enemy-intent observation, command-node tree, and command intent resolution.
- Generic command-node snapshots model the vanilla `Regiment` hierarchy at runtime. There are no separate `CorpsOrchestrator`, `DivisionOrchestrator`, or `BrigadeOrchestrator` classes.
- Harmony patches remain the only vanilla write surfaces. Patches read orchestrator state; they do not write orchestrator state.

The hierarchy is Scourge-inspired but Grand Tactician-native: runtime command nodes are built from `BattleUnits.completeunitlist`, `Regiment.GetAttachedUnitsReg(... directonly: true ...)`, `Regiment.parentregiment`, `Regiment.unittyp`, and `GamePrefs.commandhierarchyshift`.

## Shipped Slices

| Slice | State | Runtime proof |
|---|---|---|
| O0 scaffold | Shipped on `main` | Bootstrap/coordinator markers observed after lifecycle fix |
| O1 army layer | Shipped on `main` | Harness/build/deploy verified |
| O2 intent inference + adversarial replan | Shipped on `main` | `[TacticalIntent]` / `[TacticalReplan]` smoke observed on prior branch DLL; merged state still needs fresh broad smoke |
| O3 direct-child roles + #42 gate | Shipped on `main` | Gate-OFF and gate-ON smoke verified; deny path harness-verified |
| #58 deployment observer | Shipped on `main` | Read-only observer integrated; runtime first-fire still opportunistic |
| Slice 0 command-node tree | Shipped on `main` | `[TacticalCommandTree]` markers observed for both AI sides, no focused error hits |
| Slice 1 reserve commitment gate | Shipped on `main` | Build/deploy/hash verified; focused gate-OFF/gate-ON battle smoke pending |
| Slice 3 charge gate | Shipped on `main` | Build/deploy/hash verified; focused gate-OFF/gate-ON battle smoke pending |
| #60 terrain/facing discipline | Shipped on `main` | Build/deploy/hash verified; focused enabled terrain-correction smoke pending |
| #61 operations-ledger posture executor + doctrine consumers | Shipped on `main` | Harness/build/deploy/hash verified at `8eff50e672d70d6a10aa6a8a4c90d37408958026001eba236231e48618eea4dd`; Active smoke pending fresh battle log |

## Patch Consumers

| Surface | Patch | Current owner | Gate |
|---|---|---|---|
| Battle lifecycle / tactical telemetry | #35 `TacticalObserverPatch` | Existing observer + O0 lifecycle hook | `Enable Tactical Observer`, `Enable Tactical Decision Matrix Logging` |
| Feud movement gate | #42 `BattleFeudActionGatePatch` | W&L guard plus O3 direct-child gate | `Enable W&L Tactical Charge Guard`, `Enable Tactical Orchestrator Direct-Child Gate` |
| Deployment telemetry | #58 `TacticalDeploymentObserverPatch` | Read-only observer | `Enable Tactical Deployment Observer` |
| Reserve commitment | #59 `BattleReserveCommitGatePatch` | Slice 1 command-role gate | `Enable Tactical Orchestrator Reserve Commit Gate` |
| Reserve-list bias | #57 `BattleReserveDoctrinePatch` | B6c reserve intent plus Slice 1 command-role skip | `Enable Tactical Reserve List Mutation` |
| Charge initiation | #41 `BattleChargeGatePatch` | W&L guard, B6c charge denial, Slice 3 command-role gate | `Enable W&L Tactical Charge Guard`, `Enable Tactical Charge Denial`, `Enable Tactical Orchestrator Charge Gate` |
| Deployment terrain/facing correction | #60 `TacticalDeploymentTerrainDisciplinePatch` | Deployment terrain/facing discipline | `Enable Tactical Deployment Terrain Discipline` |
| Operations-ledger posture execution | #61 `BattleCommandPostureExecutorPatch` | Active command assignments and stuck/idle recovery | `Tactical Commander Mode = Active` |
| Brigade stance under contact | #45 `BattleGroupStancePatch` | Doctrine stance consumer backed by command orders and battlefield picture; legacy scorer remains fallback evidence | `Enable Tactical Group Sector Stance` plus `Tactical Commander Mode` |
| Line fallback / reserve release | B8 fallback/withdrawal patches | Doctrine reserve/fallback consumer with full deny rollback and stale-order recovery | `Enable Tactical Withdrawal Doctrine` plus `Tactical Commander Mode` |
| Artillery priority | B7 bombardment patch | Doctrine artillery consumer; friendly-close cancels, support-main-effort emits bounded suppress-strongpoint telemetry/no-write | `Enable Tactical Artillery Doctrine` plus `Tactical Commander Mode` |

## Operations Ledger Contract

#61 is the first active operations-ledger writer. `Tactical Commander Mode = Active` is the release/default mode; `MonitorOnly` runs operation selection, command assignments, idle/stuck monitoring, and telemetry without vanilla writes; `Off` is rollback.

#61 runs after vanilla `AIBattle.AdjustGroupFormations` and writes only through vanilla `BattleUnits.ChangeStance`, `BattleUnits.SetWaypoint`, and `BattleUnits.SetGroupFormation`. It must stay behind player/W&L/rout/order-pending/recent-order/close-engagement gates, and patches must not mutate operations-ledger state directly.

Living reference: [`docs/tactical-operations-ledger.md`](tactical-operations-ledger.md).

## Slice 3 Charge Gate Contract

Slice 3 keeps #41 as the sole owner of `AIBattle.MicroAICheckForCharges`. When `Enable Tactical Orchestrator Charge Gate` is true, #41 resolves command-node intent before vanilla `SetMovementMode(3)` charge initiation:

- `Main` allows only when local odds are favorable.
- `SupportMain` allows only with main-effort support evidence.
- `Fix`, `Reserve`, `Fallback`, `RefuseLeft`, and `RefuseRight` deny charge initiation.
- `Screen` denies unless the exact vanilla charge-target candidate `unitrange.enemyinrangereg[0]` is routed.
- missing command-tree state fails open.
- W&L/player-subordinate protection still runs before orchestrator logic.
- B6c explicit `DenyCharge` still runs after orchestrator logic as defense-in-depth.
- vanilla charge cancellation remains mirrored and is not blocked.

Deny telemetry is `[TacticalOrchestratorChargeGate] action=deny` with role, reason, resolution reason, primary sector, local odds, support evidence, routed-target evidence, unit, and group.

## Smoke Checklist

Gate-OFF battle smoke:

```ini
Enable Tactical Battle Orchestrator = true
Enable Tactical Orchestrator Charge Gate = false
Enable W&L Tactical Charge Guard = false
Enable Tactical Charge Denial = false
Enable Tactical Decision Matrix Logging = true
```

Expected:

- command-tree/orchestrator markers still fire.
- no `[TacticalOrchestratorChargeGate] action=deny` lines.
- no repeated `Exception`, Harmony failure, missing-anchor warning, or #41 failure marker.

Gate-ON battle smoke:

```ini
Enable Tactical Battle Orchestrator = true
Enable Tactical Orchestrator Charge Gate = true
Enable W&L Tactical Charge Guard = true
Enable Tactical Charge Denial = false
Enable Tactical Decision Matrix Logging = true
```

Expected:

- role-keyed charge deny lines appear only for non-charge roles when vanilla attempts charge initiation.
- Main-role favorable charges remain allowed.
- player-side and W&L player-subordinate groups are not retasked.
- charge cancellation still clears `movementmode == 3`.
- no repeated `Exception`, Harmony failure, missing-anchor warning, or #41 failure marker.

## Remaining Work

1. Run Active full-spectrum doctrine battle smoke on deployed DLL `8eff50e672d70d6a10aa6a8a4c90d37408958026001eba236231e48618eea4dd` and prove `[TacticalCommanderMode] mode=Active`, `[TacticalOpsLedger]`, `[TacticalCommandAssignment]`, `[TacticalCommandPosture]`, `[TacticalPostureSummary]`, `[TacticalGroupDecision]`, `[TacticalDoctrineCharge]`, `[TacticalOrchestratorChargeGate]`, bounded reserve/fallback rows, no repeated errors, and no player-subordinate retasking.
2. Run Slice 1, Slice 3, and #60 focused battle smoke on the latest deployed tactical DLL.
3. If smoke passes, archive the operations-ledger plan and update this file plus `docs/handoff.md`, `docs/patch-catalog.md`, and `MEMORY.md` with runtime proof.
4. Implement remaining retargeted consumers only after #61 Active smoke: line fallback, artillery priority, and any surviving brigade-stance handoff.

## Source Files

- Command/orchestrator logic: `src/WhiskeyRealism/Tactical/Orchestrator/`
- Tactical patch surfaces: `src/WhiskeyRealism/Patches/`
- Harness: `tests/WhiskeyRealism.Tests/`
- Patch catalog: `docs/patch-catalog.md`
- Master handoff: `docs/handoff.md`
- Operations-ledger living reference: `docs/tactical-operations-ledger.md`
- Active remaining-slices design: `docs/superpowers/specs/2026-05-09-tactical-orchestrator-remaining-patches-design.md`
- Operations-ledger execution plan: `docs/superpowers/plans/2026-05-10-tactical-operations-ledger-command-system-implementation-plan.md` (point-in-time traceability; current behavior lives in `docs/tactical-operations-ledger.md`)
