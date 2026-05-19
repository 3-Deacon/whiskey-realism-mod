# Tactical Orchestrator

Living status for the Grand Tactician tactical orchestrator workstream. This file is the current operational reference; slice specs and plans under `docs/superpowers/` are design/execution artifacts and may describe earlier checkpoints.

## Current State

- **Released game-facing version:** `v0.2.2` is still the latest public release.
- **Current main tactical orchestrator state:** O0/O1/O2/O3, #58 deployment observer, Slice 0 command-node tree, Slice 1 reserve commitment gate, Slice 3 #41 charge gate, #60 deployment terrain/facing discipline, #61 operations-ledger posture executor, #62 W&L player-subordinate order bridge (default-on as of 2026-05-19), the full-spectrum doctrine consumers, and the **2026-05-19 cumulative session additions**: depth-agnostic role cascade with envelopment modes, sector readiness doctrine, phase progression driver, sector-driven main-effort shift with hysteresis, offensive replan triggers, 4 new playbooks (catalog now 18), faction affinity bias, time-compression-aware heavy-gate cycle scaling, BUG-TAC-015 CheckOutOfFireRange null guard, IsPlayerProtected blanket-alliance fix (unblocks AI commanding player's brigade in W&L), and three new diagnostic events (TacticalCommandTreeProbeHealth, TacticalPlaybookFactionBias, TacticalPlayerProtected). The DirectChildDiscovery synth-army root cause fix (vanilla command hierarchy via GetAttachedUnitsReg reverse-map, not transform.parent) is the keystone — without it the depth-agnostic cascade silently degraded to synthetic placeholders for any command structure where corps/divisions are scene-graph siblings, which is most of GTCW.
- **Current verification:** console harness `1244 PASS / 0 FAIL`; `./build.sh` passed with `0 Warning(s)` / `0 Error(s)`; local `dist/WhiskeyRealism.dll` and deployed BepInEx plugin match SHA-256 `7be7f596e341a548cf6cd590493ee38dd40eeb0fe5cd9e5c85e9f000e2423a6a`.
- **Runtime smoke boundary:** Fresh smoke on the cumulative `7be7f596…` build is required. Verify via the three new diagnostic events: (a) `TacticalCommandTreeProbeHealth` reports `vanillaParent` count dominating `transformParent`/`noParent` (proves synth-army root cause fix engages — without this, the cascade still degrades and no `attack-objective` writes appear); (b) `TacticalPlayerProtected` fires only for the player's directly-commanded unit + W&L chain, NOT every CSA brigade (proves the IsPlayerProtected blanket-alliance fix unblocks AI from commanding the player's brigade); (c) `TacticalPlaybookFactionBias` shows `match=true` for both sides (CSA picking CSA-historical playbooks Lee/Jackson/Hood/etc., Union picking Sherman/Grant/McClellan/etc.). Beyond the diagnostics: `[TacticalCommandPosture]` writes should finally include `attack-objective` reason (not just probe/screen/guard-flank) — that proves phase progression actually advancing Probe → MainEffort → Exploit AND cascade producing Main role to real combat brigades. At 20x compression the `tactical.orchestrator-tick` p99 should drop vs the prior session's 45ms and the `max-interval-force` heavy-gate inter-arrival should stretch per the cycle scaler.
- **Latest log-driven fix:** the 2026-05-19 TacticalTuning session showed objective orders issuing but many command positions stalling after short moves. The decisive pattern was `activeMove=true` while group/subordinate path counts were `0`, because stale `groupsubordinatesmoving` flags survived after real paths had cleared. `CommandPostureExecutor` and #61 now count live group/subordinate movement paths before treating movement as active; stale movement flags without paths no longer block duplicate waypoint reissue when the group is still far from its objective. Earlier log-driven fixes remain in force: command intent and ledger lookup prefer GameObject id and fall back to component id; severe local overmatch produces `Fallback` before `Fix`/`Main`; visible, ordered, and group formation are all considered for urgent reform; close defensive/fallback reform can bypass pending courier state for non-moving corrections; and stale interrupted fallback/recovery paths can be retasked to doctrine fallback targets.
- **Latest doctrine/planning pass:** `TacticalDecisionDoctrine` now gates reconnaissance, downgrade, contact loss, and fix-and-flank commitment before the operation director commits the main effort. `TacticalNavMeshPlanner` turns doctrine targets into standoff, breakoff, and covered-lane approach points; #61 now enriches those candidates with runtime vanilla path-corner, terrain-height, terrain-id, slope, congestion, choke, bridge, dead-ground, threat-exposure, route-continuity, reservation-pressure, fallback-lane, artillery-danger, and friendly-blocker samples before delegating to vanilla `SetWaypoint`. `TacticalMovementCostField` scores those samples so approach routes avoid crowded/reserved bridge chokes, fallback routes avoid contested attack lanes, and current-corridor continuity reduces waypoint thrash. `TacticalBattleLinePlanner` now returns true frontage endpoints, objective lanes, terrain-aware flank anchors, echelon depth, artillery line, and role targets instead of stacking every child on one point. `TacticalReserveAssemblyPlanner` scores reserve rally candidates by behind-objective depth, threat distance, reachability, congestion, cover, and lateral bounds; `OperationalReserveDoctrine` protects final reserve, partial-commits above the held floor, and separates line relief, flank shift, exploit reserve, and counterattack. Fallback ladder, artillery weak-point/reposition/ammo mission, and commander-level endurance gates now feed runtime consumers. #62 maps player-subordinate doctrine to W&L current-order intent behind a default-off bridge. Close defensive flank emergencies still pass vanilla `refuseflank` left/right parameters through `SetGroupFormation`.
- **Latest battle-line assembly fix:** #61 now consumes doctrine primary targets for `FormUp` and `AdvanceToAssembly`, so assembly orders use the battle-line planner's lane targets instead of the generic objective-center `AssemblyArea`. The planner also distributes forming commands across the frontage and adds bounded per-command offsets to main/support/fix/screen role lanes, preventing four-division formations from repeating the same two assembly points.
- **Latest log-driven anchor fix:** `TacticalVisionRuntimeAdapter` now resolves `AIBattle.objectivechain`, private `Regiment.currentsetobjective`, Component/GameObject/Transform/vector objective anchors, and live scene `Objectives` map anchors before falling back to weighted vanilla `Regiment.lastsetwaypointposition` as `movement-anchor-<alliance>` when fog/no-contact leaves the side with no objective records. This prevents `TacticalOpsLedger primary=objective-unknown` from cascading into `battle-line-no-objective` and `target-unresolved` while still avoiding hidden-enemy leakage.
- **Latest Scourge tactical conversion:** `TacticalDivisionPlayExecutor`, `TacticalOutboundCourierCadence`, `TacticalOutboundOrderLedger`, `TacticalCavalryFollowDoctrine`, and `TacticalArtilleryMicroDoctrine` convert the Scourge SDK ideas into Whiskey-native gates. #61 now anchors runtime play execution on the best engaged subordinate, throttles courier-delivered child orders, suppresses duplicate pending outbound command signatures while allowing changed commands, and maps cavalry guard/scout/screen/raid decisions back into vanilla-safe command tasks. B7 now owns artillery limber/unlimber/fallback/conserve/wheel micro-decisions under the existing artillery doctrine flag. Campaign advance-guard/picket/supply-base sandbox movement is not runtime-enabled; current backlog guidance lives in [`docs/strategic-recon-commitment.md`](strategic-recon-commitment.md).

## Architecture

The tactical orchestrator is a read-owned per-battle brain layered over vanilla `AIBattle` methods:

- `TacticalBattleCoordinator` owns battle lifecycle and per-side orchestrators.
- `TacticalBattleOrchestrator` owns one side in the battle.
- `ArmyOrchestrator` owns the current army plan, enemy-intent observation, command-node tree, and command intent resolution.
- Generic command-node snapshots model the vanilla `Regiment` hierarchy at runtime. There are no separate `CorpsOrchestrator`, `DivisionOrchestrator`, or `BrigadeOrchestrator` classes.
- Harmony patches remain the only vanilla write surfaces. Patches read orchestrator state; they do not write orchestrator state.

The hierarchy is reference-inspired but Grand Tactician-native: runtime command nodes are built from `BattleUnits.completeunitlist`, `Regiment.GetAttachedUnitsReg(... directonly: true ...)`, `Regiment.parentregiment`, `Regiment.unittyp`, and `GamePrefs.commandhierarchyshift`.

## Tactical Tick Optimization (Heavy Path Throttling)

The tactical tick path (O0 `TacticalBattleCoordinator` + per-side `TacticalBattleOrchestrator` + #61 operations-ledger) uses **Approach 1** (signature + battle-hours gated heavy path; authoritative plan `docs/superpowers/plans/archive/2026-05-17-tactical-tick-optimization-implementation-plan.md` + design `docs/superpowers/specs/archive/2026-05-16-tactical-tick-optimization-design.md`).

**Frequent (cheap) path** (every `CheckGlobalAIStrategy` Postfix / vanilla `CalculateSideStatsAndUpdateAITasks` cycle): cheap `TacticalBattleStateSignature` extraction + `TacticalHeavyPathGate.Decide` + reuse of last published `TacticalBattleRuntimeSnapshot` (or Empty) + full live vanilla per-group reads. All `TelemetryPerf.Scope` ("tactical.orchestrator-tick" at CoordinatorRuntime.cs:161, "tactical.operations-ledger":374, "tactical.command-assignment" at TacticalBattleOrchestrator.cs:99) and urgent recovery remain responsive.

**Throttled (heavy) path** (only when gate returns Run): `TacticalBattleSnapshotBuilder.Build` (expensive: `ArmyEvidenceBuilder.Build` + `TacticalVisionRuntimeAdapter.BuildObjectiveRecordsFromBattle` + command tree + direct-child snapshots) then publish the atomic snapshot. Reused by DriveTickCycle, DriveDirectChildCycle, DriveOperationsLedger, doctrine assignment, and #61 high-level targets.

**Components (pure + runtime split per Tactical/AGENTS.md):**
- `TacticalBattleStateSignature` (pure struct, Task 2; `TacticalBattleStateSignature.cs:30`): `ActiveUnitCount`, `Side[0/1]ActiveForce`, `Side[0/1]MacroAI`, `AnySideInRetreatOrEOD`, `MajorObjectiveAnchorHash`, `AnyInterruptedPathsOrNewContact`, `BattleHourBucket`. `SignatureEquals` (excludes bucket, follows DirectChildEvidence pattern at DirectChildContracts.cs:89).
- `TacticalHeavyPathGate` (pure static, Task 3; `TacticalHeavyPathGate.cs:33`): `Input`/`Decision`; `Decide` (HeavyPathGate.cs:80) — first-tick always Run; effective change (sig diff or pending) after cycle floor; max-interval force even on stable sig; real-time floor blocks compressed-time heavy passes before the wall-clock minimum; 7 reasons (`"first-tick"|"signature-change"|"pending-change"|"max-interval-force"|"throttled-pending"|"stable-under-max"|"realtime-floor"`).
- `TacticalBattleRuntimeSnapshot` (pure immutable DTO, Task 4; `TacticalBattleRuntimeSnapshot.cs:80`): atomic reusable unit (signature, build hours, OwnEvidence, EnemyVisibleState, scalars, Objectives with avenues, CommandTreeSnapshot, DirectChildSnapshots); `Empty` singleton; degrade-safe.
- `TacticalBattleSnapshotBuilder` (runtime-only, Task 5; `TacticalBattleSnapshotBuilder.cs:42`, excluded from test csproj): `ExtractCurrentSignature` (cheap) + `Build` (heavy only on gate Run; SnapshotBuilder.cs:119).
- Coordinator wiring + dedup (Task 6; `TacticalBattleCoordinatorRuntime.cs:42-47` comments, 272/396/1011 Drive* sites, 482 `IsHeavyThrottlingEnabled`, 1230 `EmitHeavyGateTelemetry` Category.Gate, 1288 per-battle reset): per-side caches for last sig/time/published/pending; battle-level dedup via `BattleUnits` owner `lastsidestatupdate` (not AIBattle); conditional Build+publish vs reuse `_lastPublishedSnapshots[s]`.
- Orchestrator/ledger consumers (Task 7): synthesize from snapshot (or degrade); see TacticalBattleOrchestrator.cs, TacticalOperationsLedgerRuntime.cs.
- Urgent Recovery Safety Boundary (Task 8): #61 (`BattleCommandPostureExecutorPatch` on `AdjustGroupFormations`) + local fixes (CommandFormationCorrection, posture executor, RecoverInterruptedOrder, etc.) **never** call heavy Build or gate. Always last published snapshot (HasData guard) + live vanilla (pathinterrupted, groupsubordinatesmoving, local FOW contacts via TacticalFogOfWarContact, formation/order state, cooldowns, macroai, positions — full list in Snapshot.cs:44-56 boundary comments). Guarantees no 1-2 s hitch for close threats even under throttle. Harness regression test + comments in 5 files.
- Telemetry (Task 9): correct repeated `TelemetryRouter.Emit` (not OnceLog fixed-key) for `[TacticalHeavyGate]` executed/skipped + reasons + inputSig fields in tactical.jsonl (Writer.cs:425 Category.Gate); Performance scopes unchanged. Frequent "skipped", occasional "executed" visible in TacticalTuning/FullTuning.

**Config** (`Plugin.cs` Bind in "TacticalTickOptimization" section after Tactical Commander Mode; getter at 150; `IsHeavy...` at CoordinatorRuntime:482/487): default-off for safe rollout. "Heavy Ledger Review Cycle Hours" = 0.003f (≈10.8 battle seconds; must be ≤ vanilla sidestatupdatecycle from decompile ~84570 / GamePrefs). "Heavy Ledger Review Min Realtime Seconds" = 2.0f clamps heavy snapshot frequency under time compression while urgent recovery keeps using last snapshot + live vanilla reads.

**Performance results** (Task 10 pre-change baseline with throttling disabled vs Task 11 smoke with enabled + 0.003 h cycle; identical large battle e.g. Gettysburg rec., 1× ≥60 battle-min + 20× ≥30 battle-min, TacticalTuning profile, same post-Task9 DLL): see dedicated `docs/tactical-tick-optimization-task10-baseline.md` and `docs/tactical-tick-optimization-task11-smoke.md` (python stdlib extractor for p95/p99 on 5 scopes incl. `tactical.posture-executor`; TacticalHeavyGate counts/reasons/samples from tactical.jsonl; manifest + cfg verification; no-hitch/urgent/rollback observations). Expected: significant p95/p99 reduction in orchestrator/ledger/command scopes (heavy now gated); posture-executor parity (urgent responsive on stale snap + live vanilla); gate: hundreds/thousands skipped (mostly stable-under-max), executed on first-tick/sig/pending/max-interval; no repeated exceptions; rollback (flag=false) matches baseline exactly. The 2026-05-16 time-compression tuning run showed battle-time-only gating still allowed `tactical.orchestrator-tick` spikes up to 42.633 ms with `TacticalHeavyGate` max-interval refreshes; current DLL adds the real-time floor and emits `realtimeSeconds`, `lastHeavyRealtimeSeconds`, `minRealtimeSeconds`, and `elapsedRealtimeSeconds` fields for the next smoke.

**Rollback parity**: `Enable Tactical Heavy Path Throttling = false` (or delete section) restores 100% heavy every tick with identical behavior and p95 numbers to pre-Task6.

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
| #61 operations-ledger posture executor + doctrine consumers | Shipped on `main` | Harness/build/deploy/hash verified at `562a61b5cd0cbbedc6d6002a349cd3d68ebf50ea1d60c941e3a5a9deeaafc57a`; Active smoke pending fresh battle log on that loaded DLL |
| #62 W&L player-subordinate order bridge | Shipped on `main`, default-off | Included in current harness/build/deploy/hash verified DLL `562a61b5cd0cbbedc6d6002a349cd3d68ebf50ea1d60c941e3a5a9deeaafc57a`; focused enabled smoke pending |
| Tactical tick optimization (Approach 1 internal) | Shipped in worktree (Task 12) | Gate telemetry + p95/p99 improvement under TacticalTuning/FullTuning; urgent #61 parity on stale snapshot + live vanilla; harness 1112+ PASS (Tasks 2-8 tests); see `docs/tactical-tick-optimization-task10-baseline.md` + task11-smoke.md + CoordinatorRuntime.cs:272/482/1230 + HeavyPathGate.cs:80 |
| Depth-agnostic role cascade (`TacticalRoleCascade` + `TacticalLeafBrigadeMap`) | Shipped 2026-05-19 | Pure logic + telemetry `TacticalLeafCascade` with cascade chain string; harness covers 5-/7-child anchor layouts, envelopment DoubleWing/DoubleWingEchelon mode, hysteresis on shift |
| `DirectChildDiscovery` synth-army root cause fix | Shipped 2026-05-19 | Probe builds `directChildToParent` reverse map from vanilla `GetAttachedUnitsReg` walk so `parentInstanceId` reflects command hierarchy, not Unity scene-graph. New `TacticalCommandTreeProbeHealth` event reports per-side counts of vanilla-resolved vs transform-fallback vs no-parent probes for smoke verification |
| Reinforcement-opportunity doctrine | Shipped 2026-05-19 (AttackNow standing-advantage fix) | `TacticalReinforcementOpportunityDoctrine` emits AttackNow / WaitAndConsolidate / DefensiveHold / WithdrawalToFightLater. AttackNow now fires when current ratio clears threshold AND (defeat-in-detail window open OR enemy not growing) — prior gate required enemy parity-within-24h which suppressed advantage-static-enemy AttackNow firings |
| Envelopment cascade (`DoubleWing` + `DoubleWingEchelon`) | Shipped 2026-05-19 | When AttackNow + ratio ≥ 1.5 fires, top-tier cascade enters DoubleWing (aggressive commanders ≥ 0.6) or DoubleWingEchelon (methodical commanders < 0.6). Two anchor children get Main (or Main + SupportMain in echelon), pinning children get Fix, outer flanks Refuse. ArmyOrchestrator + reserve-commit cap at 1.1 odds for envelopment-affinity playbooks |
| Sector readiness doctrine (`TacticalSectorReadinessDoctrine`) | Shipped 2026-05-19 | Effective force = raw × (1-fatigue) × ammo × morale. Four outcomes: PushReady (cover ratio), HoldForReinforcements (relief tips balance), PushDegraded (aggressive, no relief), HoldFatigued (cautious, no relief OR force-health < 25%). Gates Probe→MainEffort and MainEffort→Exploit transitions in phase progression |
| Phase progression driver (`TacticalPhaseProgressionDoctrine`) | Shipped 2026-05-19 | Drives Probe → MainEffort → Exploit → Consolidate → Withdraw based on plan age, global/main-effort odds, morale, reserves, readiness. Wired into `ArmyTickCycle.MaybeReplan`. Phase age decoupled from plan age — plan-age in real-time (replan rate limit), phase-age in battle-time (commander-pace budgets correctly burn 20x faster at 20x compression) |
| Sector-driven main-effort shift | Shipped 2026-05-19 | `ArmyOrchestrator.ConsiderMainEffortShift` swaps `_plan.MainEffortSector` to the decisive sector with 25% hysteresis margin (prevents flicker at 20x compression). Pass-2 ordering in `ArmyTickCycle` recomputes offensive-trigger inputs post-shift to avoid spurious BreakthroughOpportunity loops |
| Offensive replan triggers | Shipped 2026-05-19 | `BreakthroughOpportunity` fires when a non-main-effort sector exceeds current axis by ≥ 0.5 ratio margin. `MainEffortLocalBreakthrough` fires when local odds spike ≥ 1.35× history OR ≥ 1.8 absolute. Both gated to Probe/MainEffort; defensive triggers still take precedence |
| Playbooks ×4 new + scoring biases | Shipped 2026-05-19 | New playbooks: `BufordCavalryScreenDelay`, `ForrestCavalryRaid`, `MeetingEngagement`, `JohnstonFabianDelay` — total catalog now 18. Envelopment bias adds up to 0.20 to score when AttackNow + ratio ≥ 1.5 active. Faction affinity bias subtracts 0.10 from mismatched playbooks (CSA picking Sherman, etc.) but leaves Either/Generic alone. New `TacticalPlaybookFactionBias` diagnostic event reports chosen playbook + match status |
| Time-compression-aware heavy-gate cycle scaling | Shipped 2026-05-19 | `TacticalHeavyPathGate.ScaleCycleForCompression` returns base cycle at 1x, 1.25× at 5x, 5× at 20x. Wired in 3 `DriveTickCycle` paths via `ComputeCompressionAdjustedCycle`. Reduces 20x-compression force-rebuild rate from ~every 2s real-time to ~every 10s real-time |
| BUG-TAC-015 `CheckOutOfFireRange` null guard | Shipped 2026-05-19, default-on | Extension of existing `TacticalFallbackRetreatNullGuardPatch`. Same vanilla unguarded-GameObject dereference pattern as MicroAICheckForRetreats / CheckLineFallbacks (`unitrange.closestenemyunitfar` GameObject can be null while `closestenemyunitfarreg` Regiment is set during unit destruction mid-fallback). NRE in `CheckOutOfFireRange` killed the per-side microai tick before this fix |
| `IsPlayerProtected` blanket-alliance fix | Shipped 2026-05-19 | Prior `if (group.alliance == GameVars.playeralliance && !ai_vs_ai) return true;` treated every CSA brigade as player-protected when player was a CSA regiment. AI never wrote orders to any brigade including the player's immediate superior (1st Brigade). Fix removes the blanket; the three W&L-specific checks (`WlOwnershipSafe`, `IsWlCurrentCommandOrChain`, `SafeDlcTakenOver`) correctly target only the player's own commanded unit + chain. New `TacticalPlayerProtected` diagnostic event names which check fires |

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

1. Run Active full-spectrum doctrine battle smoke on deployed DLL `562a61b5cd0cbbedc6d6002a349cd3d68ebf50ea1d60c941e3a5a9deeaafc57a` and prove the loaded manifest/hash, `[TacticalCommanderMode] mode=Active`, `[TacticalOpsLedger]` with a real objective or `movement-anchor-*`, refreshed side-0 `[TacticalDirectChildDiscovery]`, at least one opposing-side `[TacticalDirectChildIntent] role=Main` under an attack plan, `[TacticalCommandAssignment]` without broad `battle-line-no-objective`, sustained `[TacticalCommandPosture]` objective movement beyond the first short leg, no stale `activeMove=true paths=0` suppression, `[TacticalPostureSummary]`, `[TacticalGroupDecision]`, `[TacticalDoctrineCharge]`, `[TacticalOrchestratorChargeGate]`, bounded reserve/fallback rows, duplicate outbound orders suppressed only when pending, no repeated errors, and no player-subordinate retasking.
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
- Archived remaining-slices design: `docs/superpowers/specs/archive/2026-05-09-tactical-orchestrator-remaining-patches-design.md` (traceability only)
- Archived operations-ledger execution plan: `docs/superpowers/plans/archive/2026-05-10-tactical-operations-ledger-command-system-implementation-plan.md` (traceability only; current behavior lives in `docs/tactical-operations-ledger.md`)
