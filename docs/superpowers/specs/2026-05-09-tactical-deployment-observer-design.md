# Tactical Deployment Observer Design

## Status

Design approved in chat on 2026-05-09 after the user selected an orchestrator-led deployment direction and then narrowed the immediate slice to observation of the current implementation. This spec repairs the required Superpowers brainstorming artifact before implementation is committed or pushed.

This slice is observer-only. It must not change vanilla deployment, Whiskey tactical doctrine, unit positions, orders, formations, deployment zones, or orchestrator state.

## Problem

Battle deployments can appear chaotic at battle start and after the day ends. The vanilla path is not one isolated random roll; several placement systems stack:

- campaign-generated battle data can omit explicit tactical group coordinates;
- initial positioning can place groups from entry points;
- frame-30 AI deployment can reposition groups from objectives/frontline heuristics;
- end-of-day deployment phase closure can rerun AI deployment for both alliances;
- group placement uses `SetGroupFormation(... immediateplacement: true ...)`;
- a later deployment-zone clamp can move individual units.

Before replacing deployment behavior, Whiskey needs bounded runtime proof of which native call is moving which command, how far, and during which phase.

## Confirmed Vanilla Anchors

All line anchors refer to `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

| Claim | Status | Vanilla anchor |
|---|---|---|
| Campaign battle export can write zero start X/Z when `useunitcoordinates` is false. | Confirmed | `ImportExportUnitData.CreateBattleDataFile`, lines 67710-67718. |
| Campaign battle establishment uses `useunitcoordinates: false`. | Confirmed | `BattleUnits.EstablishCampaignBattle`, line 80900. |
| Battle start frame 30 calls `AllocateUnitsAndObjectivesToAI()` and then `DoPlacementAIUnitsWithinDeploymentzoneNew(...)` for both alliances unless excluded by operation type. | Confirmed | `BattleController.Update`, lines 23988-23995. |
| Native AI deployment owner is `BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew(int foralliance)`. | Confirmed | method starts line 85524. |
| `DoPlacementAIUnitsWithinDeploymentzoneNew` skips the human player's alliance unless `GameVars.ai_vs_ai` is true, and skips unengaged tutorial battles. | Confirmed | lines 85588-85591. |
| Initial deployment is distinguished by `battlepasseddays <= 0`; later-day deployment can follow different branches. | Confirmed | lines 85643-85647 and later checks at 85754-85765. |
| Deployment target selection uses objectives, objective ownership, frontline-zone checks, battle type, morale, attached-unit count, guessed enemy position, and similar-position deconfliction before calling `SetGroupFormation`. | Confirmed | lines 85604-85860. |
| EOD deployment cycle opens deployment after resupply, reinforcement arrival check, and routed withdrawal. | Confirmed | `BattleUnits.CheckTimeIssues`, lines 86440-86470. |
| Closing deployment phase while `BU.eodcycle > 0` reruns `DoPlacementAIUnitsWithinDeploymentzoneNew(...)` for the opposite alliance and player alliance. | Confirmed | `BattleUI.SetActiveDeploymentPhase`, lines 164875-164879. |
| `DoUnitPositioning()` handles explicit start positions and entry-point placement before deployment-zone updates. | Confirmed | `BattleUnits.DoUnitPositioning`, lines 87720-87805. |
| `MoveAllUnitsIntoDeploymentZone()` individually calls `CheckIfPositionIsOutsideDeploymentZone(...)` on active units. | Confirmed | lines 87253-87274. |
| `SetGroupFormation(Regiment, ...)` with `immediateplacement` builds hierarchy positions and calls `SetNewPosition(...)` for the group and attached units. | Confirmed | method starts line 91822; direct group `SetNewPosition` at line 92056 and attached-unit `SetNewPosition` at line 92101. |

## Not Verified Yet

- Which vanilla branch causes the worst scatter in the user's current save: `DoUnitPositioning`, battle-start `DoPlacementAIUnitsWithinDeploymentzoneNew`, deployment-phase open clamp, EOD deployment-phase close, or terrain/deployment-zone containment after formation placement.
- Whether vanilla `GameVars.DebugOwnLog(...)` deployment lines are mirrored into BepInEx `LogOutput.log` in the current runtime.
- Whether the live symptom is dominated by AI-vs-AI placement, W&L player-subordinate handling, or player-alliance EOD redeployment.
- Whether the current Whiskey tactical orchestrator indirectly changes later movement enough to amplify deployment scatter. This observer only proves placement deltas, not causation from downstream tactical decisions.

## Goals

1. Log battle-start placement deltas with enough phase information to distinguish initial positioning from AI deployment.
2. Log EOD deployment deltas with `eodcycle` and `battlepasseddays`.
3. Identify large command moves by group name, alliance, unit type, before/after position, formation, ordered formation, path count, routed state, and active state.
4. Keep logs bounded so focused smoke is readable.
5. Keep the observer read-only and safe to deploy in the current local smoke config.

## Non-Goals

- No command organization rewrite.
- No objective selection changes.
- No deployment-zone changes.
- No replacement or skip of vanilla deployment methods.
- No orchestrator-led placement yet.
- No default-off tactical behavior valve in this slice; this slice is telemetry only.

## Architecture

### `TacticalDeploymentTelemetry`

Pure tactical helper under `src/WhiskeyRealism/Tactical/`.

Responsibilities:

- represent group snapshots independent of Unity objects;
- compare before/after snapshots;
- compute matched groups, moved groups, large moves, new groups, removed groups, max move distance, and average move distance;
- produce stable summary and signature strings for tests and runtime logs.

This is testable in the console harness and has no BepInEx, Harmony, or Unity dependency beyond plain values supplied by the patch adapter.

### `TacticalDeploymentObserverPatch`

Harmony observer under `src/WhiskeyRealism/Patches/`.

Required patch surfaces:

- `BattleUnits.DoUnitPositioning()` Prefix/Postfix.
- `BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew(int foralliance)` Prefix/Postfix.
- `BattleUI.SetActiveDeploymentPhase(bool active = true, bool showsupplybydefault = true, bool calledfromsave = false)` Prefix/Postfix.

The patch snapshots `BattleUnits.grp` before and after each surface, then sends pure snapshots to `TacticalDeploymentTelemetry`. It logs summaries and capped top-move rows. It catches exceptions and emits one-time warnings through `OnceLog`; it must never throw from a hot deployment path.

### Config

Add:

```text
[Tactical Diagnostics]
Enable Tactical Deployment Observer = true
```

Default-on is acceptable because this is read-only. If log noise becomes a problem, the user can disable it without changing any behavior.

## Runtime Log Contract

Expected markers:

```text
[once:tactical-deployment-observer]
[TacticalDeployment]
[TacticalDeploymentPhase]
[TacticalDeploymentMove]
```

`[TacticalDeployment]` fields:

- `surface`
- `phase`
- `alliance`
- `eod`
- `days`
- `beforeGroups`
- `afterGroups`
- `matched`
- `moved`
- `largeMoves`
- `new`
- `removed`
- `maxMove`
- `avgMove`

`[TacticalDeploymentMove]` fields:

- `surface`
- `phase`
- `alliance`
- `unitType`
- `name`
- `distance`
- `from`
- `to`
- `formation`
- `orderedFormation`
- `paths`
- `routed`
- `active`

Large-move detail rows must be capped. Initial cap: 8 rows per observed surface call.

## Safety Rules

- Prefixes must return `void`; they must not return `false`.
- Postfixes must not mutate vanilla state.
- No writes to `BattleUnits.grp`, `BattleUnits.completeunitlist`, `frontline2`, `Regiment` position/order/path/formation fields, or tactical orchestrator state.
- Reflection reads must be guarded and one-time warned on failure.
- Group snapshots must tolerate null groups, null `regref`, inactive objects, routed groups, and missing `BattleUI.BU`.
- No Transpilers.

## Testing

Console harness tests must cover:

- large-move summary math;
- new/removed group accounting;
- summary formatting;
- stable signature fields for phase/surface/new/removed/large-move deltas.

DLL verification must include:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Runtime smoke requires restarting GTCW, starting a battle, and checking `BepInEx/LogOutput.log` for the markers above. EOD proof requires advancing through an end-of-day deployment phase and closing deployment.

## Acceptance Criteria

- Harness passes with the new pure telemetry tests.
- `./build.sh` passes with 0 errors.
- Deployed DLL hash matches `dist/WhiskeyRealism.dll`.
- Runtime battle start produces at least one `[TacticalDeployment]` or one bounded missing-anchor warning.
- EOD deployment close produces `[TacticalDeploymentPhase] action=close` and a deployment delta when the vanilla EOD branch fires.
- No repeated observer exception spam.
- No behavior-changing patch is committed as part of this observer slice.

## Follow-Up Design Boundary

The next design after this observer is **not** automatically implementation. Use the observer logs to write a separate tactical deployment organization spec. That later spec can decide whether Whiskey should preserve positions after EOD, allocate command sectors, or let the orchestrator own deployment templates. Those are behavior changes and require a new design/spec/plan gate.
