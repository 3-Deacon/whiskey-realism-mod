# Tactical Operations Ledger

Living reference for the tactical operations-ledger command system, active command assignments, posture execution, smoke checks, and rollback.

## Current State

- **Implementation state:** merged to `main`; release/default config is `Tactical Commander Mode = Active`.
- **Patch ordinal:** #61 `BattleCommandPostureExecutorPatch`.
- **Config contract:** `Active` is the release/default mode; `MonitorOnly` is for smoke and diagnostics; rollback is `Off`.
- **Build/deploy proof:** console harness `769 PASS / 0 FAIL`; `./build.sh` passed with `0 Warning(s)` / `0 Error(s)`; local `dist/WhiskeyRealism.dll` and deployed BepInEx plugin match SHA-256 `9c5217a9430a0f03bf2cbbae68328719324801a3e0d9099bf039bf69f95b6b6f` (891904 bytes).
- **Runtime smoke:** pending. Current `LogOutput.log` mtime `2026-05-10 22:13:39 -0500` predates the deployed plugin timestamp `2026-05-10 22:15:50 -0500`, so it cannot prove Active operations-ledger runtime behavior.

The system turns the tactical orchestrator's command tree into a per-side operations ledger. The ledger classifies the current battle operation, assigns command-node tasks, monitors whether assigned commands are validly idle or illegally stuck, and lets #61 issue bounded vanilla commands only when the mode is `Active`.

The 2026-05-10 log review first found `1st_Brigade#-27662` repeatedly in `MarchColumn` with `pathInterrupted=True`, `paths=0`, `activeMove=False`, and `queue=0` while the old idle classifier still treated `HoldObjective` as valid idle. That fix makes interrupted non-reserve hold/fallback tasks illegal idle, lets the posture executor recover them with a bounded `RecoveryPath` waypoint, emits ledger telemetry in both `Active` and `MonitorOnly`, and falls back from missing exact command-operation snapshots to `ArmyOrchestrator.ResolveCommandIntentForGroup(...)` plus the current operations ledger before deciding a write. A later 2026-05-10 log review of the `1st Brigade` / `38th New York` courier traffic found the next blocker: command-tree nodes were keyed by the `Regiment.gameObject.GetInstanceID()` value, while #61, #41, #57, #59, B8 fallback observation, #35 monitor lookup, and #45 stance lookup were resolving with the `Regiment` component `GetInstanceID()` value. Current code resolves command intent and operations-ledger nodes by GameObject id first and component id as fallback, so live command consumers can attach to the ledger rows they are supposed to execute. Hampton's Legion / 8th Brigade then exposed an allocator-ordering blocker: the direct-child allocator checked fixing sectors before severe local overmatch, so a badly outmatched command could receive `Fix` instead of `Fallback`. Severe overmatch now wins before `Fix`, causing isolated commands under active pressure to withdraw toward fallback behavior instead of continuing an unsupported pin. The 1st/3rd Brigade facing trace exposed a #61 formation-state blocker: `groupformation` could already be `Line` while the visible `formation` was still `MarchColumn`, so the executor could misclassify defensive formation work or wait behind recent-order cooldowns while units remained visibly exposed. Formation correction now checks visible, ordered, and group formation, close defensive/fallback refreshes use the visible threat bearing as `manualfinalrotation`, and urgent visible-mismatch retries use a 5-second cooldown. The Hampton flank cluster exposed the mirror problem on the Confederate side: a close-engaged attacking command with `flanksthreated` / `outflanked` evidence could stay in `AttackObjective` and be blocked by courier `order-pending`. #61 now treats that as a local flank emergency, temporarily executes the posture as `GuardFlank`, and bypasses pending courier state only for the non-moving current-position formation/facing correction.

## System Overview

The operations ledger sits inside the existing tactical orchestrator runtime:

- `TacticalBattleCoordinator` detects the battle lifecycle and ticks each active side.
- `TacticalBattleOrchestrator` owns side-level tactical state.
- `ArmyOrchestrator` owns command-tree snapshots and the operations-ledger runtime.
- `TacticalOperationsLedgerRuntime` records operation shape, phase, command assignments, last-order/progress timing, and posture summaries.
- `CommandPostureExecutor` is the pure decision model for whether a command needs a formation, waypoint, reserve release, fallback, or stuck-order recovery.
- #61 `BattleCommandPostureExecutorPatch` is the only new write surface. It runs after vanilla `AIBattle.AdjustGroupFormations` and writes through vanilla `BattleUnits.ChangeStance`, `BattleUnits.SetWaypoint`, and `BattleUnits.SetGroupFormation`.

Harmony patches do not write ledger state. Ledger state is written during the orchestrator tick. #61 reads ledger assignments and current vanilla physical state, then either does nothing or issues one bounded vanilla posture correction for eligible AI command groups.

Eligibility is ledger-first. #61 does not prefilter command nodes out solely because `unittyp <= 13`; if the orchestrator ledger assigned a vanilla `Regiment` component as a command node, the executor can consider waypoint, stance, reserve, fallback, or recovery actions for it. `unittyp > 13` is used only as the guard for `BattleUnits.SetGroupFormation`, because vanilla returns immediately for lower `unittyp` values on that API. AI-issued `SetWaypoint` / `SetGroupFormation` calls keep `showmovementoptions: false` so Whiskey does not open player movement UI while correcting AI posture.

Ledger resolution is tolerant but still ledger-bound. #61 first looks for an exact `CurrentCommandOperations` node id using the command node's GameObject instance id, then the `Regiment` component instance id as compatibility fallback. If that tick snapshot is missing the group, it resolves the command intent through `ArmyOrchestrator.ResolveCommandIntentForGroup(...)` and builds a single operational state from the current operation/objective ledger. If both paths fail, it writes nothing.

## Config Contract

Existing BepInEx config files override C# defaults. The release/default contract is:

```ini
[Tactical Orchestrator]
Tactical Commander Mode = Active
Enable Tactical Battle Orchestrator = true
Enable Tactical Orchestrator Army = true
Enable Tactical Orchestrator Intent Inference = true

[Tactical]
Enable Tactical Decision Matrix Logging = true
```

Modes:

| Mode | Behavior |
|---|---|
| `Off` | Disables the operations-ledger command system. Use this for rollback. |
| `MonitorOnly` | Runs vision, operation selection, command assignments, idle/stuck monitoring, and telemetry, but suppresses posture writes. Use this for diagnostics and pre-active smoke. |
| `Active` | Runs the full tactical command system for AI sides. This is the release/default mode. |

`Tactical Commander Mode` overrides scattered legacy tactical behavior flags for operations-ledger surfaces. Legacy default-off tactical flags still control their own older patch surfaces, but they are not the release switch for the operations ledger.

Rollback is config-only: set `Tactical Commander Mode = Off` and restart the game. If the failure is limited to write behavior, `MonitorOnly` keeps ledger evidence alive while suppressing writes.

Current machine-state note: the local BepInEx config has `Tactical Commander Mode = Active`, `Enable Tactical Decision Matrix Logging = true`, `Enable Tactical Battle Orchestrator = true`, `Enable Tactical Orchestrator Army = true`, and `Enable Tactical Orchestrator Intent Inference = true`. The mode row was added explicitly after deploy so the next smoke run does not depend on first-load default persistence.

## Vanilla Anchors

Known anchors for this system, confirmed against `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` and current patch/docs references:

| Anchor | Use |
|---|---|
| `AIBattle.CheckGlobalAIStrategy` 6314 | Battle-level macro strategy cadence and tactical lifecycle evidence used by existing orchestrator flow. |
| `AIBattle.AdjustGroupFormations` 5875 | #61 Postfix anchor; vanilla chooses group formation after stance updates. |
| `AIBattle.AssignReserves` 7017 | Reserve-assignment surface observed by earlier reserve doctrine and drift telemetry. |
| `AIBattle.CheckLineFallbacks` 5118 | Vanilla line-fallback surface; operations-ledger fallback work must not bypass its ownership without a specific patch. |
| `BattleUnits.ChangeStance` 90772 | Vanilla stance API used by #61 for bounded stance corrections. |
| `BattleUnits.SetWaypoint` 91232 | Vanilla movement-order API used by #61 with order delay and native movement guards. |
| `BattleUnits.SetGroupFormation` 91822 | Vanilla formation API used by #61 for command posture changes. |
| `BattleUI.CheckPathSetting` 168980 -> `BattleUI.CheckGroupRotation` 166042 -> `BattleUnits.SetWaypoint` 91232 | Regular non-W&L campaign/battle right-click movement path. Campaign formations are represented by the `Regiment` component; UI echelon labels come from `overridesymbol`, while `unittyp > 13` is the confirmed `SetGroupFormation` command/group-formation guard. |
| `BattleUnits.GetHierarchyTree` 92720 | Vanilla hierarchy reader used by `SetGroupFormation` to walk attached command nodes. |
| `AIBattle.CheckCurrentOrderUpdate` 8233 | W&L current-order/message bridge only. It hard-gates on `DLC_WL.dlc_scenarioactive`; do not treat it as the regular campaign movement API. |

The executor must not add broad replacements for these anchors. It only uses the vanilla APIs as bounded outputs after ledger and safety gates pass.

## Telemetry

Expected operations-ledger markers:

- `[TacticalOpsLedger]` from side-level operation signature changes.
- `[TacticalCommandAssignment]` from command-node task assignment changes.
- `[TacticalCommandPosture]` from #61 posture decisions and writes.
- `[TacticalPostureSummary]` from valid-idle, illegal-idle, stuck-recovery, attack, and reserve-wait summaries.
- `[TacticalReserveDrift]` from reserve-list drift inspection around `AssignReserves`.
- `[once:tactical-command-posture-executor]` first-fire marker when #61 wires.

Rows should be signature-gated or interval-bounded. Repeated `missing-anchor`, Harmony failure, `Exception`, or `ERROR` lines are smoke failures until proven unrelated.

## MonitorOnly Smoke Checkpoint

Use this checkpoint before or after an Active run when you need proof that the ledger is reading the battle correctly without writing vanilla state:

```ini
[Tactical Orchestrator]
Tactical Commander Mode = MonitorOnly
Enable Tactical Battle Orchestrator = true
Enable Tactical Orchestrator Army = true
Enable Tactical Orchestrator Intent Inference = true

[Tactical]
Enable Tactical Decision Matrix Logging = true
```

Pass criteria:

- `[TacticalCommanderMode] mode=MonitorOnly` appears after plugin load.
- `[TacticalOpsLedger]` appears for at least one AI side.
- `[TacticalCommandAssignment]` appears when command-node assignments materialize.
- `[TacticalPostureSummary]` appears and reports interpretable command counts.
- `[TacticalCommandPosture]` lines may appear for diagnostics, but none should report `applied=True`.
- No repeated `Exception`, `ERROR`, `missing-anchor`, or Harmony failure lines.

## Active Smoke Checklist

Use this checklist after deploying the current DLL and restarting the game:

1. Confirm config contains:

```ini
[Tactical Orchestrator]
Tactical Commander Mode = Active

[Tactical]
Enable Tactical Decision Matrix Logging = true
```

2. Start a fresh battle or load a save that reaches active AI tactical command ticks.
3. Search `BepInEx/LogOutput.log` for:

```bash
rg -n "TacticalOpsLedger|TacticalCommandAssignment|TacticalCommandPosture|TacticalPostureSummary|TacticalReserveDrift|tactical-command-posture-executor|Exception|ERROR|missing-anchor|Harmony|failed" "<GTCW>/BepInEx/LogOutput.log"
```

Pass criteria:

- `[TacticalOpsLedger]` appears.
- `[TacticalCommandAssignment]` appears.
- `[TacticalCommandPosture]` writes are bounded and explain action/reason/target.
- `[TacticalPostureSummary]` shows illegal idle trending down or staying explainably bounded during the run.
- `[TacticalReserveDrift]` has no repeated drift-failure warning.
- No player-side or player-subordinate retasking is observed.
- No repeated non-reserve command nodes remain in `MarchColumn + pathInterrupted=True + paths=0 + activeMove=False` without a valid ledger reason.
- No repeated `Exception`, `ERROR`, `missing-anchor`, Harmony failure, or #61 failure marker.

If the active smoke fails, set `Tactical Commander Mode = Off` for rollback. If evidence is needed before a fix, set `MonitorOnly` to keep ledger telemetry while suppressing writes.

Current Active smoke boundary: not passed. The only current log hit for the smoke/error pattern is an unrelated HarmonyX warning, `AccessTools.TypeByName: Could not find type named CommunityHotfix`; no fresh operations-ledger markers exist because the log predates the deployed DLL.

## Risks

- #61 writes vanilla battle state when `Active`, so it must remain bounded by player/W&L/rout/order-pending/recent-order/close-engagement gates.
- The command system depends on command-tree discovery. Missing or unstable command nodes should fail open rather than invent writes.
- `SetWaypoint` calls must keep vanilla order-delay semantics; broad movement replacement remains out of scope.
- Fallback and reserve ownership still intersect older vanilla anchors and existing patches. Drift markers need review during smoke, not after release tagging.
- Existing config files can preserve old values. Always inspect the live BepInEx config before interpreting smoke.

## Documentation Lifecycle

This file is the living source for operations-ledger behavior, config, smoke expectations, and rollback. The implementation plan under `docs/superpowers/` is a point-in-time execution artifact. After Active smoke passes, update:

- `docs/handoff.md`
- `docs/tactical-orchestrator.md`
- `docs/patch-catalog.md`
- `MEMORY.md`
- this file
