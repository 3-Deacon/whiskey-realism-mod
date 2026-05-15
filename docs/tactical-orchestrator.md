# Tactical Orchestrator

Living status for the Grand Tactician tactical orchestrator workstream. This file is the current operational reference; slice specs and plans under `docs/superpowers/` are design/execution artifacts and may describe earlier checkpoints.

## Current State

- **Released game-facing version:** `v0.2.2` is still the latest public release.
- **Current main tactical orchestrator state:** O0/O1/O2/O3, #58 deployment observer, Slice 0 command-node tree, Slice 1 reserve commitment gate, Slice 3 #41 charge gate, #60 deployment terrain/facing discipline, #61 operations-ledger posture executor, #62 default-off W&L player-subordinate order bridge, and the full-spectrum doctrine consumers for stance/charge/reserve/fallback/artillery/player-order intent are implemented for `main`.
- **Current verification:** console harness `986 PASS / 0 FAIL`; `./build.sh` passed with `0 Warning(s)` / `0 Error(s)`; local `dist/WhiskeyRealism.dll` and deployed BepInEx plugin match SHA-256 `ec00120fb8f8e08d729ec6f99418910d76356edd8e5b642f50e903d9d468c526` (1121792 bytes).
- **Runtime smoke boundary:** Active full-spectrum doctrine smoke is pending because current `LogOutput.log` mtime `2026-05-15 07:54:28 -0500` predates the deployed plugin timestamp `2026-05-15 08:16:53 -0500`. A fresh battle log must prove `[TacticalCommanderMode] mode=Active`, `[TacticalOpsLedger]` with a real vanilla objective or `movement-anchor-*` instead of `objective-unknown`, `[TacticalCommandAssignment]` without broad `battle-line-no-objective`, `[TacticalCommandPosture]`, `[TacticalPostureSummary]`, `[TacticalDoctrineCharge]`, `[TacticalOrchestratorChargeGate]`, `[TacticalGroupDecision]`, bounded reserve/fallback lines, bounded `outbound-duplicate-pending-order` suppression only for repeated pending commands, and no repeated errors before the doctrine system is release-smoke-verified. #62 smoke requires explicitly enabling `Enable Player Order Doctrine`.
- **Latest log-driven fix:** the observed `1st Brigade` / `38th New York` courier case showed command nodes keyed by `Regiment.gameObject.GetInstanceID()` while tactical consumers resolved by the `Regiment` component id, so #61 could wire but not attach to the live ledger rows. Command intent and ledger lookup now prefer GameObject id and fall back to component id across #61, #41, #57, #59, B8 fallback observation, #35 monitor lookup, and #45 stance lookup. Hampton's Legion / 8th Brigade exposed allocator-ordering defects: a fixing-sector assignment, and then a main-effort assignment, could outrank severe local overmatch, leaving an isolated brigade fighting a superior force instead of falling back. Severe overmatch now produces a `Fallback` direct-child role before both `Fix` and `Main`. The 1st/3rd Brigade facing trace then exposed a #61 execution defect: command groups could report `groupformation=Line` while the visible `formation` remained `MarchColumn`, causing defensive/fallback correction to stall behind recent-order cooldowns; defensive/fallback corrections now compare visible, ordered, and group formation, retry urgent visible-mismatch formation corrections after 5 seconds, and pass a manual threat-facing rotation for close formation refreshes. The Hampton flank cluster exposed a separate #61 task-boundary issue: close-engaged defensive/fallback commands could still be blocked by pending courier state, and close-engaged formation correction was creating a fresh vanilla formation path that could return the group to interrupted `MarchColumn` movement. Close local flank emergencies still execute as `GuardFlank`; close defensive/fallback local reform can now bypass pending courier state for the non-moving correction, and close formation/facing corrections avoid `SetGroupFormation(newpath:true)` so groups reform in place instead of opening another path. The full-spectrum doctrine follow-up adds a battle picture, operation director, command doctrine orders, and consumer decisions so visible formed enemy contact raises objective confidence, high-odds visible weak points can produce attacks even under defensive macro, skirmisher/screen-only contact does not expose the main line, stale reserve/fallback paths are rolled back through #59 movement-state restore, and artillery remains conservative around friendly-close fire. The Active smoke prep fixes make observer posture telemetry use the side ledger's real commander mode instead of hardcoded monitor-only simulation, preserve committed high-odds attacks when reserves are low but odds remain favorable, and set the live local `Enable Tactical Orchestrator Charge Gate = true` config row. The latest Hampton-specific fix addresses the `8th_Brigade#-28534` loop where `pathInterrupted=True`, `activeMove=False`, stale path segments, and `orderState=1` kept replaying `RecoverInterruptedOrder target=RecoveryPath`; stalled interrupted stale paths no longer block retasking, and fallback doctrine now replaces the stale recovery waypoint with the doctrine fallback target. The latest Active-log fix raises visible enemy-line fallback confidence, appends visible enemy-line objectives alongside vanilla anchors, makes the operation director prefer exposed enemy-line objectives over inert generic anchors when odds and confidence support commitment, lets committed main-effort enemy-line attacks proceed at fallback confidence when exposed and favorable, treats named formed detachments and battle command groups as real line contact instead of zero-strength screen evidence, and reissues stale #61 attack waypoints when a duplicate target has `paths=0`, `activeMove=False`, and the group is still far from the target.
- **Latest doctrine/planning pass:** `TacticalDecisionDoctrine` now gates reconnaissance, downgrade, contact loss, and fix-and-flank commitment before the operation director commits the main effort. `TacticalNavMeshPlanner` turns doctrine targets into standoff, breakoff, and covered-lane approach points; #61 now enriches those candidates with runtime vanilla path-corner, terrain-height, terrain-id, slope, congestion, choke, bridge, dead-ground, threat-exposure, route-continuity, reservation-pressure, fallback-lane, artillery-danger, and friendly-blocker samples before delegating to vanilla `SetWaypoint`. `TacticalMovementCostField` scores those samples so approach routes avoid crowded/reserved bridge chokes, fallback routes avoid contested attack lanes, and current-corridor continuity reduces waypoint thrash. `TacticalBattleLinePlanner` now returns true frontage endpoints, objective lanes, terrain-aware flank anchors, echelon depth, artillery line, and role targets instead of stacking every child on one point. `TacticalReserveAssemblyPlanner` scores reserve rally candidates by behind-objective depth, threat distance, reachability, congestion, cover, and lateral bounds; `OperationalReserveDoctrine` protects final reserve, partial-commits above the held floor, and separates line relief, flank shift, exploit reserve, and counterattack. Fallback ladder, artillery weak-point/reposition/ammo mission, and commander-level endurance gates now feed runtime consumers. #62 maps player-subordinate doctrine to W&L current-order intent behind a default-off bridge. Close defensive flank emergencies still pass vanilla `refuseflank` left/right parameters through `SetGroupFormation`.
- **Latest battle-line assembly fix:** #61 now consumes doctrine primary targets for `FormUp` and `AdvanceToAssembly`, so assembly orders use the battle-line planner's lane targets instead of the generic objective-center `AssemblyArea`. The planner also distributes forming commands across the frontage and adds bounded per-command offsets to main/support/fix/screen role lanes, preventing four-division formations from repeating the same two assembly points.
- **Latest log-driven anchor fix:** `TacticalVisionRuntimeAdapter` now resolves `AIBattle.objectivechain`, private `Regiment.currentsetobjective`, Component/GameObject/Transform/vector objective anchors, and live scene `Objectives` map anchors before falling back to weighted vanilla `Regiment.lastsetwaypointposition` as `movement-anchor-<alliance>` when fog/no-contact leaves the side with no objective records. This prevents `TacticalOpsLedger primary=objective-unknown` from cascading into `battle-line-no-objective` and `target-unresolved` while still avoiding hidden-enemy leakage.
- **Latest Scourge tactical conversion:** `TacticalDivisionPlayExecutor`, `TacticalOutboundCourierCadence`, `TacticalOutboundOrderLedger`, `TacticalCavalryFollowDoctrine`, and `TacticalArtilleryMicroDoctrine` convert the Scourge SDK ideas into Whiskey-native gates. #61 now anchors runtime play execution on the best engaged subordinate, throttles courier-delivered child orders, suppresses duplicate pending outbound command signatures while allowing changed commands, and maps cavalry guard/scout/screen/raid decisions back into vanilla-safe command tasks. B7 now owns artillery limber/unlimber/fallback/conserve/wheel micro-decisions under the existing artillery doctrine flag. Campaign advance-guard/picket/supply-base sandbox movement remains spec-only in [`docs/superpowers/specs/2026-05-14-scourge-campaign-advance-guard-sandbox-design.md`](superpowers/specs/2026-05-14-scourge-campaign-advance-guard-sandbox-design.md).

## Architecture

The tactical orchestrator is a read-owned per-battle brain layered over vanilla `AIBattle` methods:

- `TacticalBattleCoordinator` owns battle lifecycle and per-side orchestrators.
- `TacticalBattleOrchestrator` owns one side in the battle.
- `ArmyOrchestrator` owns the current army plan, enemy-intent observation, command-node tree, and command intent resolution.
- Generic command-node snapshots model the vanilla `Regiment` hierarchy at runtime. There are no separate `CorpsOrchestrator`, `DivisionOrchestrator`, or `BrigadeOrchestrator` classes.
- Harmony patches remain the only vanilla write surfaces. Patches read orchestrator state; they do not write orchestrator state.

The hierarchy is reference-inspired but Grand Tactician-native: runtime command nodes are built from `BattleUnits.completeunitlist`, `Regiment.GetAttachedUnitsReg(... directonly: true ...)`, `Regiment.parentregiment`, `Regiment.unittyp`, and `GamePrefs.commandhierarchyshift`.

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
| #61 operations-ledger posture executor + doctrine consumers | Shipped on `main` | Harness/build/deploy/hash verified at `ec00120fb8f8e08d729ec6f99418910d76356edd8e5b642f50e903d9d468c526`; Active smoke pending fresh battle log |
| #62 W&L player-subordinate order bridge | Shipped on `main`, default-off | Harness/build/deploy/hash verified in `ec00120fb8f8e08d729ec6f99418910d76356edd8e5b642f50e903d9d468c526`; focused enabled smoke pending |

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

1. Run Active full-spectrum doctrine battle smoke on deployed DLL `ec00120fb8f8e08d729ec6f99418910d76356edd8e5b642f50e903d9d468c526` and prove `[TacticalCommanderMode] mode=Active`, `[TacticalOpsLedger]` with a real objective or `movement-anchor-*`, refreshed side-0 `[TacticalDirectChildDiscovery]`, at least one CSA/opposing-side `[TacticalDirectChildIntent] role=Main` under `HoodFrontalAssault`, `[TacticalCommandAssignment]` without broad `battle-line-no-objective`, `[TacticalCommandPosture]`, `[TacticalPostureSummary]`, `[TacticalGroupDecision]`, `[TacticalDoctrineCharge]`, `[TacticalOrchestratorChargeGate]`, bounded reserve/fallback rows, duplicate outbound orders suppressed only when pending, no repeated errors, and no player-subordinate retasking.
2. Run Slice 1, Slice 3, #60, and #62 focused battle smoke on the latest deployed tactical DLL.
3. If smoke passes, archive the operations-ledger plan and update this file plus `docs/handoff.md`, `docs/patch-catalog.md`, and `MEMORY.md` with runtime proof.
4. Retune remaining consumers only from fresh smoke evidence; line fallback, artillery priority, path-quality scoring, reserve policy, and #62 are implemented but not runtime-proven on the current DLL.

## Source Files

- Command/orchestrator logic: `src/WhiskeyRealism/Tactical/Orchestrator/`
- Tactical patch surfaces: `src/WhiskeyRealism/Patches/`
- Harness: `tests/WhiskeyRealism.Tests/`
- Patch catalog: `docs/patch-catalog.md`
- Master handoff: `docs/handoff.md`
- Operations-ledger living reference: `docs/tactical-operations-ledger.md`
- Active remaining-slices design: `docs/superpowers/specs/2026-05-09-tactical-orchestrator-remaining-patches-design.md`
- Operations-ledger execution plan: `docs/superpowers/plans/2026-05-10-tactical-operations-ledger-command-system-implementation-plan.md` (point-in-time traceability; current behavior lives in `docs/tactical-operations-ledger.md`)
