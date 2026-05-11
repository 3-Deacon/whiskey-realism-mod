# Tactical Operations Ledger

Living reference for the tactical operations-ledger command system, active command assignments, posture execution, smoke checks, and rollback.

## Current State

- **Implementation state:** active on `implement/tactical-ops-ledger`; release/default config is `Tactical Commander Mode = Active`.
- **Patch ordinal:** #61 `BattleCommandPostureExecutorPatch`.
- **Config contract:** `Active` is the release/default mode; `MonitorOnly` is for smoke and diagnostics; rollback is `Off`.
- **Build/deploy proof:** Task 11 console harness `756 PASS / 0 FAIL`; `./build.sh` passed with `0 Warning(s)` / `0 Error(s)`; local `dist/WhiskeyRealism.dll` and deployed BepInEx plugin match SHA-256 `38a39fece3b970b4542beb702177a171709ae790a550a3ec62f0d82496df5414` (886272 bytes).
- **Runtime smoke:** pending. Current `LogOutput.log` mtime `2026-05-10 13:48:24 -0500` predates the Task 11 deployed plugin timestamp `2026-05-10 18:58:28 -0500`, so it cannot prove Active operations-ledger runtime behavior.

The system turns the tactical orchestrator's command tree into a per-side operations ledger. The ledger classifies the current battle operation, assigns command-node tasks, monitors whether assigned commands are validly idle or illegally stuck, and lets #61 issue bounded vanilla commands only when the mode is `Active`.

## System Overview

The operations ledger sits inside the existing tactical orchestrator runtime:

- `TacticalBattleCoordinator` detects the battle lifecycle and ticks each active side.
- `TacticalBattleOrchestrator` owns side-level tactical state.
- `ArmyOrchestrator` owns command-tree snapshots and the operations-ledger runtime.
- `TacticalOperationsLedgerRuntime` records operation shape, phase, command assignments, last-order/progress timing, and posture summaries.
- `CommandPostureExecutor` is the pure decision model for whether a command needs a formation, waypoint, reserve release, fallback, or stuck-order recovery.
- #61 `BattleCommandPostureExecutorPatch` is the only new write surface. It runs after vanilla `AIBattle.AdjustGroupFormations` and writes through vanilla `BattleUnits.ChangeStance`, `BattleUnits.SetWaypoint`, and `BattleUnits.SetGroupFormation`.

Harmony patches do not write ledger state. Ledger state is written during the orchestrator tick. #61 reads ledger assignments and current vanilla physical state, then either does nothing or issues one bounded vanilla posture correction for eligible AI command groups.

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

Task 11 machine-state note: the current local BepInEx config already has `Enable Tactical Decision Matrix Logging = true`, `Enable Tactical Battle Orchestrator = true`, `Enable Tactical Orchestrator Army = true`, and `Enable Tactical Orchestrator Intent Inference = true`. It did not yet contain an explicit `Tactical Commander Mode` row at deploy time because the new plugin had not been launched; the code default is `Active` and BepInEx should persist the key on next load.

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
- No `[TacticalCommandPosture]` lines show applied active writes.
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

Current Task 11 smoke boundary: not passed. The only current log hit for the smoke/error pattern is an unrelated HarmonyX warning, `AccessTools.TypeByName: Could not find type named CommunityHotfix`; no fresh operations-ledger markers exist because the log predates the deployed DLL.

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
