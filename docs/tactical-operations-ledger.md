# Tactical Operations Ledger

Living reference for the tactical operations-ledger command system, command assignments, posture execution, smoke checks and rollback. Build, deploy and smoke status live in [`docs/handoff.md`](handoff.md); orchestrator architecture, the doctrine clock, intel confidence and battle write ownership are in [`docs/tactical-orchestrator.md`](tactical-orchestrator.md).

- **Patch ordinal:** #61 `BattleCommandPostureExecutorPatch`; #62 `PlayerSubordinateOrderPatch` is the W&L player-subordinate order bridge ([`docs/wl-player-order-doctrine.md`](wl-player-order-doctrine.md)).
- **Config contract:** `Tactical Commander Mode = Active` is the release default; `MonitorOnly` runs the ledger and telemetry without vanilla writes; `Off` is rollback.

The system turns the orchestrator's command tree into a per-side operations ledger. The ledger classifies the battle operation, assigns command-node tasks and doctrine orders, monitors whether commands are validly idle or illegally stuck, and lets #61 issue bounded vanilla commands when the mode is `Active`.

## System Overview

- `TacticalBattleCoordinator` ticks each active side; with heavy-path throttling on (default) the ledger re-ticks only on a new heavy snapshot, changed command-node intents, a Commander Mode change, or 60 battle seconds. Its clock is the doctrine clock (frozen overnight), so director commit windows span the same battle time at any compression.
- `ArmyOrchestrator` owns command-tree snapshots and the `TacticalOperationsLedgerRuntime` (operation shape, phase, command assignments, last-order/progress timing, posture summaries).
- `TacticalOperationDirector` selects and commits battle operations; `CommandDoctrineAssignment` and `CommandDoctrineOrder` publish per-command task, primary/support/fallback targets and SOP.
- `TacticalSopDoctrine` stamps each order with explicit authority (`Scout`, `Probe`, `Screen`, `Attack`, `Assault`, `Hold`, `Fallback`, `Reserve`, ...), risk budget, reacquire cadence, support-before-charge and fallback-if-pressed requirements.
- `TacticalDecisionDoctrine` classifies contact before commitment (`ClassifyObjective`, `ShouldCommitFixAndFlank`, `ShouldBreakOffRecon`).
- `TacticalIntelligencePack.cs` holds the battlefront geometry (`TacticalBattlefrontGeometry`), massing cycle (`TacticalMassingCycle`), reserve mission (`OperationalReserveDoctrine`), fallback ladder (`TacticalFallbackLadder`) and artillery mission (`TacticalArtilleryMissionPlanner`) gates.
- `TacticalApproachAvenuePlanner` scores Grand Tactician entrypoints, scheduled arrivals and deployment-group starts against each objective and publishes an enemy-origin to objective axis with confidence.
- `TacticalDefensiveLineAnchorPlanner` turns confident road/crossing approach evidence into an intercept line in front of the map objective (reasons `approach-intercept`, `road`, `crossing`, `objective-lane`).
- `TacticalBattleLinePlanner` produces frontage endpoints, objective lanes, flank anchors, echelon depth, artillery line and main/support/fix/screen/reserve/fallback targets, spreading forming commands across the frontage.
- `TacticalReserveAssemblyPlanner` scores reserve rally candidates: behind the objective, at least 325 units from the threat, at most 520 lateral units from the line, reachable, congestion below 0.80.
- `TacticalNavMeshPlanner` (with `TacticalMovementCostField`) turns ledger targets into recon standoff, covered-lane approach and fallback breakoff points; #61 feeds it runtime `NavMesh.CalculatePath` samples taken under vanilla `AddPath` area costs.
- `TacticalNestedDivisionPlayPlanner` expands a single parent command with at least three direct brigade/battery children into flank / main / depth-reserve brigade states, keeping the parent as HQ anchor.
- `CommandPostureExecutor` is the pure decision model for formation, waypoint, reserve, fallback and stuck-order recovery; #61 is its runtime writer.

Harmony patches do not write ledger state. Ledger state is written during the orchestrator tick; #61 reads ledger assignments plus live vanilla state, then does nothing or issues one bounded vanilla correction.

## Objective Anchors

`TacticalVisionRuntimeAdapter` builds objective records from vanilla state before synthetic evidence: `AIBattle.objectivechain`, then per-group `Regiment.currentsetobjective`, then live scene `Objectives` components. Visible formed enemy-line evidence is appended as a `VisibleEnemyLine` objective even when vanilla anchors exist. Approach avenues come from `BattleUnits.entrypoints` (`entrypointposition` / `trooptargetposition`), `BattleUnits.scheduledarrival` and deployment groups, with crossing evidence from bounded `BattlefieldSetup.SearchTerrainInRangePos` checks. With no map objective and no legal visible line, weighted vanilla `Regiment.lastsetwaypointposition` becomes `movement-anchor-<alliance>`.

The doctrine rule: screen contact is not a target for a formed assault. `TacticalOperationDirector` transitions:

- `recon-contact` -> initial `Scouting`
- `fix-and-flank` -> committed attack only after formed-line exposure, odds and reserve gates pass
- `contact-downgraded` -> `Scouting`
- `contact-lost` -> `SoftAbort`

Wide command sets (six or more nodes) convert the first and last forming slots into `GuardFlank` and keep near-wing slots as depth (`scourge-slot-flank-guard`, `scourge-slot-depth`, `scourge-slot-line`).

## #61 Posture Executor

Postfix on vanilla `AIBattle.AdjustGroupFormations`. Battle gate (`PostureExecutorBattleGate.Allows`): no action before battle state 5, with `macroai < 0`, during vanilla army retreat (`macroai 3`), during the end-of-day phase (`eodcycle > 0`), on the W&L feud instance, or on the player-side instance outside `ai_vs_ai`. Every write passes `TacticalWriteGuard.CanWrite`.

Write surfaces:

- stance through `TacticalStanceWriter` with `StanceWriteSource.Executor` (task to stance by `TacticalCommandTaskToStanceMapper`; ownership rules in [`docs/tactical-orchestrator.md`](tactical-orchestrator.md));
- movement through `BattleUnits.SetWaypoint`, with vanilla courier order delay except for a withdrawal under contact or flank risk;
- formation through `BattleUnits.SetFormation` (courier), facing/refuse through `BattleUnits.SetGroupFormation(newpath: false, modifylastwaypoint: true, refuseflank: ...)`, and in-contact facing through `Regiment.RotateRegiment`;
- fire control through `TacticalCombatBehaviorWriter` (`BattleUnits.ChangeCombatBehavior`).

AI-issued writes keep `showmovementoptions: false`.

Rules:

- **Ledger resolution:** exact `CurrentCommandOperations` node by GameObject id, then component id; otherwise `ArmyOrchestrator.ResolveCommandIntentForGroup(...)` plus the current ledger. If both fail, nothing is written. `unittyp > 13` is required only for `SetGroupFormation`.
- **Formation writes** (`CommandFormationWritePolicy`): never while the group or its subordinates move; in combat only rotate settled non-artillery regiments in place, never re-lay; out of contact use the courier `SetFormation`; wheel/refuse only with no enemies in range; cooldown 0.05 battle hours, 0.1 under fire.
- **Movement:** stale movement flags without paths do not count as an active move; a target within 60 m of the current waypoint is a duplicate and skipped, also while vanilla reports the path interrupted; a write that does not materialize backs off 0.05 battle hours; a queued courier order counts as issued.
- **Outbound orders:** courier cadence per child node (`TacticalOutboundCourierCadence`), and a still-pending identical order is suppressed (`outbound-duplicate-pending-order`) while changed orders pass.
- **Leaf brigades** nested in an AI group move only for movement tasks, only to their slot in the parent's formation (parent via `BattleUnits.GrabNextParentUnit`, clamped to vanilla map bounds), and never while the parent has an order in flight. No stance write.
- **Main-arrival hold** (`MainArrivalHoldLatch`): arms once per role/task, releases after supports close, 15 battle-minutes, or an aggressive commander, and stays released until the role or task changes.
- **Opportunity override** (`TacticalOpportunityTaskOverride`): `DefensiveHold` is no opinion; withdrawal, wait-and-consolidate and attack-now adjust tasks.
- **Local flank emergency:** a close-engaged attacking, supporting, fixing, advancing or released-reserve command with flank risk executes as `GuardFlank` (`CommandFormationCorrection.TaskForLocalFlankEmergency`).
- **Fallback targets** (`CommandFallbackTargetResolver`): step away from the objective by the fallback standoff, else away from the closest visible threat, else fail closed.
- **Scourge conversions:** `TacticalDivisionPlayExecutor` (division play anchored on the best engaged subordinate), `TacticalCavalryFollowDoctrine` and `TacticalGrandTacticianReconDoctrine` (cavalry guard/scout/screen/raid; no blind assault without FOW contact), `TacticalFireControlDoctrine` (infantry long/medium/short and cavalry behavior from task, range and readiness).
- **Facing pulse** (`Enable Tactical Contact Facing Pulse`) rotates regiments only and never touches artillery.

## Other Doctrine Consumers

| Gate | Whiskey owner | Write anchor | Behavior |
|---|---|---|---|
| Group stance | `DoctrineConsumerDecisions.DecideStance`, ledger task stance | #45 -> `TacticalStanceWriter` | Reserve-held and fallback orders deny (defend), an exposed-line attack at local odds >= 1.2 allows (attack); otherwise no opinion. |
| Charge | `TacticalChargeDenial` | #45 `DemoteCharge` | Vanilla assault is demoted only on doctrine Deny (to 2) or, with the orchestrator army off, legacy DenyCharge (to 3). |
| Reserve commitment | `TacticalReserveCommitGate` | #59 on `AIBattle.CheckUseOfReserves` | A Reserve-role group is denied unless released (role no longer Reserve, or the allocator re-roled it). Denied paths are rolled back from a snapshot, except for units in pontoon/obstacle construction or with a pending re-form, which fail open to vanilla. |
| Reserve mission | `DoctrineConsumerDecisions.DecideReserve` -> `OperationalReserveDoctrine` | B8, #56 | Reserve-held orders deny vanilla reserve movement; relieve-line/counterattack release, seal-flank and refuse-reserve guard a flank. |
| Withdrawal | `TacticalWithdrawalDoctrine.PlanTacticalFallback` | B8 | RearGuard/FullRetreat issue vanilla's own tactical fallbacks (`CheckLineFallbacks` / `MicroAICheckForRetreats` geometry); never the campaign `SetWithdrawal`. |
| Artillery | `DoctrineConsumerDecisions.DecideArtillery` -> `TacticalArtilleryMissionPlanner`; `TacticalArtilleryMicroDoctrine` | B7, #45 artillery audit | Friendly-close denies bombardment; support-main-effort and counterbattery allow; danger-close, conserve-ammo, displace and defensive-fallback deny. Micro limber/unlimber/face/retreat follows vanilla battery rules and never writes stance. |
| Feud movement | direct-child gate, W&L guard | #42 on `CheckForFeudGroupActions` | FOW-visible target within vanilla's feud distance cap; vanilla runs unless Commander Mode is `Active`. |

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
| `AIBattle.AdjustGroupAIStance` 4221 | #45 Postfix; vanilla per-group stance rules ported in `TacticalStanceOwnership`. |
| `AIBattle.AdjustGroupFormations` 5875 | #61 Postfix anchor. |
| `AIBattle.CheckUseOfReserves` 6062 | #59 gate, #56 delay guard, B8 reserve deny and fallback. |
| `AIBattle.AssignReserves` 7017 | #57 reserve-drift telemetry. |
| `AIBattle.CheckLineFallbacks` 5118 | Vanilla line fallback; B8 reuses its fallback geometry. |
| `BattleUnits.ChangeStance` 90772 | Stance API behind `TacticalStanceWriter`; stance > 0 sets `permanentlydetached`. |
| `BattleUnits.ChangeCombatBehavior` 90944; `Regiment.GetFireRange` 118292 | Fire-control API and effective range. `TacticalFireControlDoctrine` uses infantry short/medium/long = 0/1/2 and cavalry evade/neutral/charge = 4/5/6. |
| `BattleUnits.SetFormation` 90971 | Courier-delivered formation change used by #61 out of contact. |
| `BattleUnits.SetWaypoint` 91232 | Movement API used by #61 with courier order delay. |
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
- `[TacticalCommandPosture]` from #61 decisions and writes.
- `[TacticalPostureSummary]` valid-idle / illegal-idle / stuck-recovery / attack / reserve-wait summaries.
- `[TacticalGroupDecision]` from #45 doctrine stance decisions; `[TacticalStanceWrite]` and `[TacticalChargeDeny]` from the stance writer and charge denial.
- `[TacticalReserveDrift]` from #57; `[B8]` and `[TacticalReserveOrderDelayGuard]` reserve/fallback decisions.
- `TacticalPlayerProtected` when a write is skipped for W&L/player protection.
- `[once:tactical-command-posture-executor]` when #61 wires.

Rows are signature-gated or interval-bounded. Repeated `missing-anchor`, Harmony failure, `Exception` or `ERROR` lines are smoke failures until proven unrelated.

## MonitorOnly Smoke Checkpoint

Set `Tactical Commander Mode = MonitorOnly` with the other defaults. Pass criteria:

- `[TacticalCommanderMode] mode=MonitorOnly` after plugin load.
- `[TacticalOpsLedger]`, `[TacticalCommandAssignment]` and `[TacticalPostureSummary]` appear for at least one AI side.
- `[TacticalCommandPosture]` rows may appear, but no vanilla write is applied.
- No repeated `Exception`, `ERROR`, `missing-anchor` or Harmony failure lines.

## Active Smoke Checklist

With `Tactical Commander Mode = Active`, start a battle that reaches AI tactical command ticks and search the log and tuning sidecars:

```bash
rg -n "TacticalOpsLedger|TacticalCommandAssignment|TacticalCommandPosture|TacticalPostureSummary|TacticalReserveDrift|TacticalStanceWrite|tactical-command-posture-executor|Exception|ERROR|missing-anchor|Harmony|failed" "<GTCW>/BepInEx/LogOutput.log"
```

Pass criteria:

- ledger, assignment, posture and summary markers appear; posture writes explain action/reason/target and stay bounded.
- objective movement continues past the first short leg; no stale `activeMove=true` with zero paths blocking reissue.
- illegal idle trends down or stays explainably bounded.
- no player-side or player-subordinate retasking; no #61 writes during the end-of-day phase or vanilla army retreat.
- groups in contact are rotated in place, not re-laid.
- formed regiments commit only when the enemy line is exposed; screen/skirmisher contact does not pull the main line into an assault.
- doctrine reserve deny removes the reserve path rather than leaving a stale active path.
- corridor defense shows `approach-intercept` / `objective-lane`; wide or nested lines show `scourge-slot-*` reasons and a retained parent/HQ anchor.

If Active smoke fails, set `Tactical Commander Mode = Off`; set `MonitorOnly` to keep ledger telemetry while suppressing writes.

## Risks

- #61 writes vanilla battle state when `Active`; it must stay behind the write guard, battle gate, order-pending/settlement, recent-order and contact gates.
- The command system depends on command-tree discovery; missing or unstable command nodes produce no writes.
- `SetWaypoint` calls keep vanilla courier order-delay semantics; broad movement replacement is out of scope.
- Existing config files can preserve old values. Inspect the live BepInEx config before interpreting smoke.
