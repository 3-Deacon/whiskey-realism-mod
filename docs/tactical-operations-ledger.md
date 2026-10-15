# Tactical Operations Ledger

Living reference for the tactical operations-ledger command system, command assignments, posture execution, smoke checks and rollback. Build, deploy and smoke status live in [`docs/handoff.md`](handoff.md); orchestrator architecture, the doctrine clock, intel confidence and battle write ownership are in [`docs/tactical-orchestrator.md`](tactical-orchestrator.md).

- **Execution:** the battle core's posture producers (`Tactical/Battle/Producers/`, run by #68 `BattleCoreArbiterPatch`; #61 `BattleCommandPostureExecutorPatch` was deleted in BC5a); #62 `PlayerSubordinateOrderPatch` is the W&L player-subordinate order bridge ([`docs/wl-player-order-doctrine.md`](wl-player-order-doctrine.md)).
- **Config contract:** `Tactical Commander Mode = Active` is the release default; `MonitorOnly` runs the ledger and telemetry without vanilla writes; `Off` is rollback.

The system turns the orchestrator's command tree into a per-side operations ledger. The ledger classifies the battle operation, assigns command-node tasks and doctrine orders, monitors whether commands are validly idle or illegally stuck, and lets the posture producers submit bounded orders the battle-core executor issues when the mode is `Active`.

## System Overview

- `TacticalBattleCoordinator` ticks each active side; with heavy-path throttling on (default) the ledger re-ticks only on a new heavy snapshot, changed command-node intents, a Commander Mode change, or 60 battle seconds. Its clock is the doctrine clock (frozen overnight), so director commit windows span the same battle time at any compression.
- `ArmyOrchestrator` owns command-tree snapshots and the `TacticalOperationsLedgerRuntime` (operation shape, phase, command assignments, last-order/progress timing, posture summaries).
- `TacticalOperationDirector` selects and commits battle operations; `CommandDoctrineAssignment` and `CommandDoctrineOrder` publish per-command task, primary/support/fallback targets and SOP.
- `TacticalSopDoctrine` stamps each order with explicit authority (`Scout`, `Probe`, `Screen`, `Attack`, `Assault`, `Hold`, `Fallback`, `Reserve`, ...), risk budget, reacquire cadence, support-before-charge and fallback-if-pressed requirements.
- `TacticalDecisionDoctrine` classifies contact before commitment (`ClassifyObjective`, `ShouldCommitFixAndFlank`, `ShouldBreakOffRecon`). In `Scouting`, `CommandDoctrineAssignment.ResolveTask` gives the recon tasks (`ReconnaissanceTaskFor`: Main effort and the Probe role probe, supporting/fixing/screening forces screen) on reconnaissance contact and, since BC9, with no contact at all, except for the `DefensiveNetwork` / `DelayAndFallback` shapes. The operation director takes the side's `BattleStance` (`TacticalOperationDirectorInput.WithStance`, from `ArmyEvidenceBuilder.ReadBattleStance` via the army's cached force-balance stance): a Defender is never committed to, nor left in, an offensive shape whatever path selected it (every shape but `DefensiveNetwork` / `DelayAndFallback` becomes `DefensiveNetwork`, a `Committed` / `Exploiting` phase becomes `Forming`; reasons `defender-screen` in Scouting, `defender-hold` otherwise). `TacticalDecisionDoctrine.TaskForStance` guards every task assignment (node task planning in `CommandNodeOperationsRuntime`, so every reader of the army's command operations; the doctrine assignment; GT recon with or without a visible enemy; the posture pipeline after all its rewrites and the leaf cascade, through the pure `PostureStanceGuard`): a Defender's Probe becomes Screen and its Attack/Support/Fix tasks HoldObjective. #45 never gives a Defender attack stance: its ledger stance is capped at defend (2), a visible weak point included, and its default sector scorer's `attack-weak-point` / `defensive-counterstroke` become holds at 2. The stance is `ArmyOrchestrator.CurrentBattleStance`, the side's one stance reader, refreshed by `ArmyPostureInputsRuntime.Refresh` on every coordinator tick independent of `Enable Tactical Reinforcement Opportunity Doctrine`; the division play reads it too. Charges stay vanilla's (only groups vanilla escalated to assault are armed). A Defender's cavalry still scouts (recon producer). `CommandNodeRole.Probe` is assigned only by the BC9 probe rewrite in the posture pipeline (the allocator's `DirectChildRole` has no Probe).
- `TacticalIntelligencePack.cs` holds the battlefront geometry (`TacticalBattlefrontGeometry`), massing cycle (`TacticalMassingCycle`), fallback ladder (`TacticalFallbackLadder`) and artillery mission (`TacticalArtilleryMissionPlanner`) gates. (The reserve mission `OperationalReserveDoctrine` / `DoctrineConsumerDecisions.DecideReserve` was deleted in BC5c: #59 decides from the Reserve role alone.)
- `TacticalApproachAvenuePlanner` scores Grand Tactician entrypoints, scheduled arrivals and deployment-group starts against each objective and publishes an enemy-origin to objective axis with confidence.
- `TacticalDefensiveLineAnchorPlanner` turns confident road/crossing approach evidence into an intercept line in front of the map objective (reasons `approach-intercept`, `road`, `crossing`, `objective-lane`).
- `TacticalBattleLinePlanner` produces frontage endpoints, objective lanes, flank anchors, echelon depth, artillery line and main/support/fix/screen/reserve/fallback targets, spreading forming commands across the frontage.
- `TacticalReserveAssemblyPlanner` scores reserve rally candidates: behind the objective, at least 325 units from the threat, at most 520 lateral units from the line, reachable, congestion below 0.80.
- `TacticalNavMeshPlanner` (with `TacticalMovementCostField`) turns ledger targets into covered-lane approach and fallback breakoff points (Scout and Probe tasks no longer go to a recon stand-off at the objective: since BC9 their target is the recon planner's look point, `BattleRecon.LookPoint`, a 200 m bound along the front); the posture producer feeds it runtime `NavMesh.CalculatePath` samples taken under vanilla `AddPath` area costs (`PostureNavAreaCosts`: Roads 1, Bridges 1, FencesWalls 2, Rivers 500000, R5).
- `TacticalNestedDivisionPlayPlanner` expands a single parent command with at least three direct brigade/battery children into flank / main / depth-reserve brigade states, keeping the parent as HQ anchor.
- `CommandPostureExecutor` is the pure decision model for formation, waypoint, reserve, fallback and stuck-order recovery; the posture producer turns its decisions into intents.

Harmony patches do not write ledger state. Ledger state is written during the orchestrator tick; the posture producers read ledger assignments plus the per-tick battle snapshot, then submit nothing or bounded intents.

## Objective Anchors

`TacticalVisionRuntimeAdapter` builds objective records from vanilla state before synthetic evidence: `AIBattle.objectivechain`, then per-group `Regiment.currentsetobjective`, then live scene `Objectives` components. Visible formed enemy-line evidence is appended as a `VisibleEnemyLine` objective even when vanilla anchors exist. Approach avenues come from `BattleUnits.entrypoints` (`entrypointposition` / `trooptargetposition`), `BattleUnits.scheduledarrival` and deployment groups, with crossing evidence from bounded `BattlefieldSetup.SearchTerrainInRangePos` checks. With no map objective and no legal visible line, weighted vanilla `Regiment.lastsetwaypointposition` becomes `movement-anchor-<alliance>`.

The doctrine rule: screen contact is not a target for a formed assault. `TacticalOperationDirector` transitions:

- `no-contact-scout` -> initial `Scouting` when a newly selected objective shows no enemy at all (BC9; before BC9 own / max(1, 0) odds committed the operation); an operation already active on that objective keeps the rules below
- `defender-screen` / `defender-hold` -> a Defender side's (vanilla `GetBattleType` 2) decision with its offensive shape made `DefensiveNetwork` (Scouting / any other phase; Committed and Exploiting become Forming) (BC9)
- `recon-contact` -> initial `Scouting`
- `fix-and-flank` -> committed attack only after formed-line exposure, odds and reserve gates pass
- `contact-downgraded` -> `Scouting`
- `contact-lost` -> `SoftAbort`

Wide command sets (six or more nodes) convert the first and last forming slots into `GuardFlank` and keep near-wing slots as depth (`scourge-slot-flank-guard`, `scourge-slot-depth`, `scourge-slot-line`).

## Posture Producers (formerly #61)

Run in the battle-core arbiter tick (#68, Postfix on vanilla `AIBattle.AdjustGroupFormations` 5870). Battle gate (`PostureExecutorBattleGate.Allows`): no action before battle state 5, with `macroai < 0`, during vanilla army retreat (`macroai 3`), during the end-of-day phase (`eodcycle > 0`), on the W&L feud instance, or on the player-side instance outside `ai_vs_ai`. The producers read the per-tick `BattleSnapshot` (unit state as `PostureSense` proxies, perception, the D5 parent map) and the side's ledger; they write nothing. The order executor checks every target with `TacticalWriteGuard` and issues:

- stance through `TacticalStanceWriter` (task to stance by `TacticalCommandTaskToStanceMapper`; ownership rules in [`docs/tactical-orchestrator.md`](tactical-orchestrator.md));
- movement through `BattleUnits.SetWaypoint`, with vanilla courier order delay except for an in-contact withdrawal; Attack/Support/Fix/AdvanceToAssembly group moves over 150 m through `AIBattle.BlockedMovingOrder` (see below);
- formation through `BattleUnits.SetFormation` (courier), facing/refuse through `BattleUnits.SetGroupFormation(newpath: false, modifylastwaypoint: true, refuseflank: ...)`, and in-contact facing through `Regiment.RotateRegiment` (per regiment, skipping detached or player-owned subtrees, 119865);
- fire control through `TacticalCombatBehaviorWriter` (`BattleUnits.ChangeCombatBehavior`, per regiment).

AI-issued writes keep `showmovementoptions: false`.

Producers: `Posture` (Executor priority: AI-pool groups and the leaf brigades nested under them), `FacingPulse` (Scorer; `Enable Tactical Contact Facing Pulse`), `FireControl` (Executor, CombatBehavior per regiment), `DivisionPlay` (Play).

Rules:

- **Ledger resolution:** exact `CurrentCommandOperations` node by GameObject id, then component id; otherwise `ArmyOrchestrator.ResolveCommandIntentForGroup(...)` plus the current ledger. If both fail, nothing is submitted.
- **Formation writes** (`CommandFormationWritePolicy`, applied by the executor): never while the group or its subordinates move; in combat only rotate settled non-artillery regiments in place, never re-lay; out of contact use the courier `SetFormation`; wheel/refuse only with no perceived enemies in the group's own reach (`BattlePerception.GroupRangeReach`); cooldown 0.05 battle hours, 0.1 under fire.
- **Movement** (executor): an interrupted path or stale movement flags without paths do not count as an active move; a target within 60 m of the current waypoint is a duplicate (`OrderArbiter.DedupeMeters`); recent-order cooldown 180 battle seconds per (channel, source); a write that does not materialize backs off 0.05 battle hours and gets one ±30 degree retry; a queued courier order counts as issued (D6).
- **Outbound orders:** one courier move per group per 0.25 battle hours (`OrderExecutor.CourierIntervalBattleHours`, was `TacticalOutboundCourierCadence`); a same-task re-issue waits `Brigade Task Hold Seconds`.
- **Leaf brigades** nested in an AI group move only for movement tasks, only to their slot in the parent's formation (`LeafSlotTarget`: the parent from the snapshot's D5 map, its `lastsetwaypointposition`/`rotation` and the leaf's layout offset, 92050-92086; the refused slot only for the flank the executor last refused), clamped to vanilla map bounds, and never while the parent has an order in flight. No stance write.
- **Main-arrival hold** (`MainArrivalHoldLatch`): arms once per role/task, releases after supports close, 15 battle-minutes, or an aggressive commander, and stays released until the role or task changes.
- **Opportunity override** (`TacticalOpportunityTaskOverride`): `DefensiveHold` is no opinion; withdrawal, wait-and-consolidate and attack-now adjust tasks.
- **Local flank emergency:** a close-engaged attacking, supporting, fixing, advancing or released-reserve command with flank risk executes as `GuardFlank` (`CommandFormationCorrection.TaskForLocalFlankEmergency`).
- **Fallback targets** (`CommandFallbackTargetResolver`): step away from the objective by the fallback standoff, else away from the closest visible threat, else fail closed.
- **Scourge conversions:** `TacticalDivisionPlayExecutor` (division play anchored on the best engaged subordinate), `TacticalCavalryFollowDoctrine` and `TacticalGrandTacticianReconDoctrine` (cavalry guard/scout/screen/raid; no blind assault without FOW contact), `TacticalFireControlDoctrine` (infantry long/medium/short and cavalry behavior from task, range and readiness).
- **Facing pulse** (`Enable Tactical Contact Facing Pulse`) rotates regiments only and never touches artillery.
- **Close engagement and flank risk** are snapshot proxies: melee in the subtree or a visible enemy within 250 m (`PostureSense.CloseContactMeters`), and #44's perceived flank-risk rule (`BattleWriterPolicy.PerceivedFlankRisk`) instead of vanilla's unfiltered `groupenemiesinrange` / `flanksthreated`.

**Division play vs the executor (plan D8):** the spec ladder is binding, Executor beats Play. #61 used to replace a child's ledger task with the division-play task before deciding (the play overrode the ledger). Now the division play submits its own intents at `Play` priority, and a play order wins a channel only where the posture producer submitted nothing for that group on that channel. **Covered crossings:** an Attack/Support/Fix/AdvanceToAssembly group move farther than 150 m carries `IntentFlags.AvoidCoveredCrossings`, and the executor issues it through vanilla `AIBattle.BlockedMovingOrder` (3054-3150) with the side's own `blockedcrossings`, as vanilla's line-group moves do (6953-6956) and shipped #61 did. `UpdateBlockedCrossings` (7917-7973) only lists the crossings the enemy covers; the obstacle carving and the delayed `SetWaypoint` (two frames later, `useorderdelay:false`, from `UpdateOrders` 3382) are `BlockedMovingOrder`'s. On a map with no river crossings the list stays null and the move is a plain `SetWaypoint`.

## Other Doctrine Consumers

| Gate | Whiskey owner | Write anchor | Behavior |
|---|---|---|---|
| Group stance | `DoctrineConsumerDecisions.DecideStance`, ledger task stance (cached per brigade by `PopulateResolvedDecisionsForSide`) | GroupStance producer (Scorer) -> executor -> `TacticalStanceWriter` | Reserve-held and fallback orders deny (defend), an exposed-line attack at local odds >= 1.2 allows (attack); otherwise no opinion. A change waits for a settled group; a refresh of the held stance does not. |
| Charge | `TacticalChargeDenial` | ChargeDemotion producer (Safety, Stance channel) | Vanilla assault is demoted only on doctrine Deny (to 2) or, with the orchestrator army off, legacy DenyCharge (to 3); the infantry charge doctrine then reverts its charges. |
| Reserve commitment | `TacticalReserveCommitGate` | #59 on `AIBattle.CheckUseOfReserves` | A Reserve-role group is denied unless released (role no longer Reserve, or the allocator re-roled it). Denied paths are rolled back from a snapshot, except for units in pontoon/obstacle construction or with a pending re-form, which fail open to vanilla. |
| Reserve release | `TacticalReserveCommitGate.TryReleasedDirectChild` / `TryPlanRelease` | ReserveRelease producer (Support) | A group the ledger still holds in Reserve but the allocator released gets a courier move to stand behind its own line in the sector the allocator gave it. |
| Reserve courier parity | `TacticalReserveCommitGate.PlanCourierParity` | #56, executor inline | Vanilla's direct reserve path becomes a courier order when the executor plan executes; otherwise vanilla's move stays. |
| Withdrawal | `TacticalWithdrawalDoctrine.PlanTacticalFallback` | Withdrawal producer (Safety) | RearGuard/FullRetreat issue vanilla's own tactical fallback shapes (`CheckLineFallbacks` / `MicroAICheckForRetreats` geometry) away from the closest visible enemy, ending in movement mode 2; a stalled ledger `FallBackToLine` move is re-issued by courier; never the campaign `SetWithdrawal`. |
| Artillery | `DoctrineConsumerDecisions.DecideArtillery` -> `TacticalArtilleryMissionPlanner`; `TacticalArtilleryMicroDoctrine` | ArtilleryDoctrine / ArtilleryPointBlank producers | Friendly-close denies bombardment; support-main-effort and counterbattery allow; danger-close, conserve-ammo, displace and defensive-fallback deny. Micro limber/unlimber/face/retreat follows vanilla battery rules for every group stance and never writes stance; a battery vanilla sends within 60 m of a visible enemy is pulled back. |
| Feud movement | direct-child gate, W&L guard | #42 on `CheckForFeudGroupActions`, executor inline (`FeudReplay`) | Visible target within vanilla's feud distance cap; vanilla runs unless Commander Mode is `Active`. |

## Config Contract

Existing BepInEx config files override C# defaults. Release defaults:

```ini
[Tactical Orchestrator]
Tactical Commander Mode = Active
Enable Tactical Battle Orchestrator = true
Enable Tactical Orchestrator Army = true
Enable Tactical Orchestrator Intent Inference = true
Enable Tactical Orchestrator Direct-Child Gate = true
Enable Tactical Orchestrator Reserve Commit Gate = true
Brigade Task Hold Seconds = 12

[Tactical]
Enable Tactical Decision Matrix Logging = true
Enable Tactical Contact Facing Pulse = true
```

| Mode | Behavior |
|---|---|
| `Off` | No ledger and no writes (`TacticalCommanderModePolicy.RunsLedger` false). Rollback. |
| `MonitorOnly` | Runs vision, operation selection, command assignments, idle/stuck monitoring and telemetry; `TacticalWriteGuard.ModeAllowsWrites` is false, so no battle writer writes. |
| `Active` | Full tactical command system for AI sides. Release default. |

Every gameplay and diagnostic flag defaults on (`python3 -I tools/config-audit.py .` reports 0 findings). Rollback is config-only: `Tactical Commander Mode = Off` and restart; `MonitorOnly` keeps ledger evidence while suppressing writes.

## Vanilla Anchors

Confirmed against `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`:

| Anchor | Use |
|---|---|
| `AIBattle.CheckGlobalAIStrategy` 6314 | Battle macro cadence; coordinator lifecycle/Tick (#35) and #44 macroai owner. |
| `AIBattle.AdjustGroupAIStance` 4221 | Vanilla per-group stance rules ported in `TacticalStanceOwnership`; the GroupStance producer runs right after it (5869 -> 5870). |
| `AIBattle.AdjustGroupFormations` 5875 (called at 5870) | #68 arbiter Postfix anchor (the posture producers run here). |
| `AIBattle.CheckUseOfReserves` 6062 | #59 gate, #56 courier parity. |
| `AIBattle.AssignReserves` 7017 | #57 reserve-drift telemetry. |
| `AIBattle.CheckLineFallbacks` 5118 | Vanilla line fallback; the Withdrawal producer reuses its fallback geometry. |
| `BattleUnits.ChangeStance` 90772 | Stance API behind `TacticalStanceWriter`; stance > 0 sets `permanentlydetached`. |
| `BattleUnits.ChangeCombatBehavior` 90944; `Regiment.GetFireRange` 118292 | Fire-control API and effective range. `TacticalFireControlDoctrine` uses infantry short/medium/long = 0/1/2 and cavalry evade/neutral/charge = 4/5/6. |
| `BattleUnits.SetFormation` 90971 | Courier-delivered formation change the executor uses out of contact. |
| `BattleUnits.SetWaypoint` 91232 | Movement API the executor uses, with courier order delay. |
| `AIBattle.BlockedMovingOrder` 3054-3150 (`UpdateOrders` 3382) | Long objective-type group moves around enemy-covered crossings. |
| `BattleUnits.SetGroupFormation` 91822 | Wheel/refuse with `newpath: false`, `modifylastwaypoint: true`, `refuseflank`. |
| `Regiment.RotateRegiment` 130159 | In-contact rotate-in-place facing. |
| `NavMesh.CalculatePath` (UnityEngine.AI) | Reachability/path-quality probe; vanilla still owns the order and path execution. |
| `BattleUI.CheckPathSetting` 168982 -> `BattleUI.CheckGroupRotation` 166050 -> `BattleUnits.SetWaypoint` 91232 | Regular non-W&L right-click movement path. |
| `BattleUnits.GetHierarchyTree` 92720 | Vanilla hierarchy reader used by `SetGroupFormation`. |
| `AIBattle.CheckCurrentOrderUpdate` 8233 | W&L current-order bridge only (#62); not the regular movement API. |

## Telemetry

Expected operations-ledger markers:

- `[TacticalCommanderMode] mode=...` at plugin load.
- `[TacticalOpsLedger]` on side-level operation signature changes.
- `[TacticalCommandAssignment]` on command-node assignment changes (doctrine task, SOP authority, risk budget, reacquire seconds, support/fallback requirements, planner reason, target buckets, idle reason, confidence).
- `TacticalCommandPosture` rows from the posture and division-play producers (`writeResult=submitted` when intents went to the arbiter), `TacticalLeafCascade` and `TacticalFacingPulse` rows.
- `[TacticalPostureSummary]` valid-idle / illegal-idle / stuck-recovery / attack / reserve-wait summaries.
- `[TacticalStanceWrite]` from the stance writer.
- `[TacticalReserveDrift]` from #57 and #59; `[TacticalReserveOrderDelayGuard]` courier-parity decisions.
- `[BattleCore] producers count=12 order=…` once per battle, `[OrderArbiter]` rows every 30 s per side, `[OrderArbiter] first vanilla order issued …` once per battle (see [`docs/tactical-orchestrator.md`](tactical-orchestrator.md) § Battle Core).
- Player protection shows in `TacticalDecisionMatrix` group rows (`wlGuard=deny:<reason>`); the executor's skips are counted per reason in `[OrderArbiter] topSkip`.

Rows are signature-gated or interval-bounded. Repeated `missing-anchor`, Harmony failure, `Exception` or `ERROR` lines are smoke failures until proven unrelated.

## MonitorOnly Smoke Checkpoint

Set `Tactical Commander Mode = MonitorOnly` with the other defaults. Pass criteria:

- `[TacticalCommanderMode] mode=MonitorOnly` after plugin load.
- `[TacticalOpsLedger]`, `[TacticalCommandAssignment]` and `[TacticalPostureSummary]` appear for at least one AI side.
- No `TacticalCommandPosture` rows and no `[OrderArbiter]` rows: the arbiter and its producers stand down, so no vanilla write is applied.
- No repeated `Exception`, `ERROR`, `missing-anchor` or Harmony failure lines.

## Active Smoke Checklist

With `Tactical Commander Mode = Active`, start a battle that reaches AI tactical command ticks and search the log and tuning sidecars:

```bash
rg -n "BattleCore|OrderArbiter|Exception|ERROR|missing-anchor|Harmony|failed" "<GTCW>/BepInEx/LogOutput.log"
rg -n "TacticalOpsLedger|TacticalCommandAssignment|TacticalCommandPosture|TacticalPostureSummary|TacticalReserveDrift|TacticalStanceWrite" "<GTCW>/BepInEx/WhiskeyRealism/tuning-logs/<session-id>/"
```

The full ordered battle-core smoke checklist is in the battle-core plan (`docs/superpowers/plans/2026-10-15-battle-core.md` § BC7 smoke checklist). Pass criteria here:

- ledger, assignment, posture and summary markers appear; posture writes explain action/reason/target and stay bounded.
- objective movement continues past the first short leg; no stale `activeMove=true` with zero paths blocking reissue.
- illegal idle trends down or stays explainably bounded.
- no player-side or player-subordinate retasking; no arbiter writes during the end-of-day phase or vanilla army retreat.
- groups in contact are rotated in place, not re-laid.
- formed regiments commit only when the enemy line is exposed; screen/skirmisher contact does not pull the main line into an assault.
- doctrine reserve deny removes the reserve path rather than leaving a stale active path.
- corridor defense shows `approach-intercept` / `objective-lane`; wide or nested lines show `scourge-slot-*` reasons and a retained parent/HQ anchor.

If Active smoke fails, set `Tactical Commander Mode = Off`; set `MonitorOnly` to keep ledger telemetry while suppressing writes.

## Risks

- The posture producers' orders reach vanilla battle state when `Active`; they stay behind the arbiter's battle gate and the executor's write guard, settlement, dedupe, recent-order and contact gates.
- The command system depends on command-tree discovery; missing or unstable command nodes produce no writes.
- `SetWaypoint` calls keep vanilla courier order-delay semantics; broad movement replacement is out of scope.
- Existing config files can preserve old values. Inspect the live BepInEx config before interpreting smoke.
