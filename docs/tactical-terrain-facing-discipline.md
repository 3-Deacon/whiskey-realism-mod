# Tactical Terrain And Facing Discipline

Living reference for tactical deployment terrain evidence, AI deployment terrain correction, and final-facing boundaries.

## Current State

- **Implementation state:** merged to `main`; current tactical runtime is superseded by the operations-ledger DLL.
- **Patch ordinal:** #60 `TacticalDeploymentTerrainDisciplinePatch`
- **Telemetry extension:** #58 `TacticalDeploymentObserverPatch` emits terrain/facing evidence for large deployment moves.
- **Build/deploy proof:** #60 was merged and hash-deployed in DLL `b00e03bd7e635e981380459e09a0d52a19d635c22c49bd340b403dacfbdf4cf8` (841216 bytes; 717 PASS). Current `main` and deployed BepInEx plugin are now `9e76ce41c4a85cb25fd3ca00536a782eeb49d4922459de3579c25ab31fcb62b8` (888320 bytes; 760 PASS) after #61 operations-ledger integration.
- **Live local config:** `Enable Tactical Deployment Terrain Discipline = true` in `<GTCW>/BepInEx/config/dev.kyle.whiskey-realism.cfg` for focused smoke.
- **Runtime smoke:** pending. Do not mark this slice shipped/archived until a fresh game run proves bounded logs and no repeated exceptions with the flag enabled.

The behavior does not replace Grand Tactician's native pathfinder. It layers evidence and bounded correction around vanilla deployment placement.

## Behavior Boundary

#60 runs after vanilla `BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew(int foralliance)`.

It may correct a group only when vanilla left a clear terrain or deployment-zone failure:

- center is water
- footprint sample is water
- center is outside the deployment zone
- footprint sample is outside the deployment zone

It must not rotate terrain-safe groups for facing alone. Visible enemy bearing can influence final facing only as part of an accepted terrain/deployment correction.

Corrections use vanilla `BattleUnits.SetGroupFormation(... immediateplacement: true ...)` so attached-unit placement remains on the native formation path. After that call, #60 mirrors vanilla deployment cleanup for the corrected group tree by clearing only:

- `Regiment.lastsetwaypointposition`
- `Regiment.immediateunitplacement`

It does not clear `lastsetwaypointrotation`, orders, paths, movement modes, command roles, reserve lists, rout state, or tactical orchestrator state.

## Config

Existing BepInEx config files override C# defaults. For local focused smoke, the live config currently includes:

```ini
[Tactical]
Enable Tactical Deployment Terrain Discipline = true
Tactical Deployment Terrain Discipline Max Correction Meters = 60
Tactical Deployment Terrain Discipline Max Candidates = 16
Tactical Deployment Facing Preferred Delta Degrees = 90
```

C# defaults keep the behavior valve false:

- `Enable Tactical Deployment Terrain Discipline = false`
- `Tactical Deployment Terrain Discipline Max Correction Meters = 60`
- `Tactical Deployment Terrain Discipline Max Candidates = 16`
- `Tactical Deployment Facing Preferred Delta Degrees = 90`

Rollback is config-only: set `Enable Tactical Deployment Terrain Discipline = false` and restart the game. Keep `Enable Tactical Deployment Observer = true` if terrain evidence is still useful.

## Vanilla Anchors

Confirmed against `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`:

| Anchor | Use |
|---|---|
| `BattleUI.CheckPathSetting` 168982-169019 | Manual pathing raycasts `NavTarget` and creates `SetWaypointData(... manualfinalrotation: -1f ...)`. |
| `BattleUnits.SetWaypoint` 91304-91323 | Group deployment waypoint path clamps through `frontline2.GetClosestPointInDeploymentZone(...)`, then calls `RegimentSetPath`. |
| `BattleUnits.SetWaypoint` 91453-91560 | Deployment-mode direct positioning / `SetGroupFormation`, water checks, deployment-zone checks. |
| `BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew` 85524-85872 | Native AI deployment placement surface used by #58/#60. |
| `BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew` 85873-85880 | Vanilla final cleanup clears `lastsetwaypointposition` and `immediateunitplacement` on active units. |
| `BattleUnits.SetGroupFormation(GameObject, ...)` 91815 | Public formation placement overload used by #60. |
| `BattleUnits.SetGroupFormation` 92056-92067 | Immediate placement writes `lastsetwaypointposition` and `lastsetwaypointrotation`. |
| `BattleUnits.SetGroupFormation` 92075-92205 | Walks attached units and grandchildren during formation placement. |
| `Regiment.AddPath` 130259-130527 | Native NavMesh area-cost and path calculation surface already guarded by #53. |
| `Regiment.RegimentSetPath` 131073-131128 | Final tactical target terrain-id-4 clamp and target height. |
| `Regiment.RegimentSetPath` 131167-131188 | Retry loop can step away from target via `Vector3.MoveTowards(..., -0.5f)`. |
| `Regiment.RegimentSetPath` 131211-131260 | Final waypoint rotation uses manual rotation if supplied, otherwise `GetLastWaypointAngle()`. |
| `Regiment.SetNewPosition` 131303-131358 | Position/facing setter used by vanilla formation placement. |
| `Regiment.CheckIfPositionIsOnWater*` 131432-131556 | Vanilla water correction mainly checks terrain id `4`. |
| `BattlefieldSetup.CheckTerrainLine` 27638 | Runtime probe checks terrain lines for water crossing evidence. |
| `Regiment.UpdateUnitRangeFast` 122545-122935 | Visible enemy lists are fog-checked; closest-enemy fields alone are not enough for Whiskey facing. |
| `Regiment.UpdateFlanking` 122989 | Filters out scouts/cavalry/melee-style contacts for comparable flank/enemy reasoning. |
| `BattleUnits.Grp.IsApplicableForDeployment` 78275 | Vanilla deployment eligibility guard mirrored by #60. |

## Runtime Components

- `TacticalTerrainFacingDiscipline` — pure scorer/decision model.
- `TacticalTerrainFacingTelemetry` — bounded `[TacDeployTerrain]` row formatter and sanitizer.
- `TacticalTerrainProbe` — Unity/vanilla adapter for terrain id, deployment-zone, footprint, water-line, height, and visible-enemy bearing evidence.
- `TacticalDeploymentObserverPatch` — read-only terrain/facing evidence extension around large deployment moves.
- `TacticalDeploymentTerrainDisciplinePatch` — default-off behavior patch that corrects clear AI terrain/deployment failures.

## Telemetry

Expected markers:

- `[TacDeployTerrain]` from observer evidence on large deployment moves.
- `[TacDeployTerrainAdvice]` from #60 advice/correction decisions.

Rows should stay bounded, sanitized, and phase-aware. Deployment phase semantics match `TacticalDeploymentTelemetry`: `eodcycle > 0 || battlepasseddays > 0` means `eod`; otherwise `initial`, with `skipped` reserved for vanilla player/tutorial early returns.

## Smoke Checklist

Use this checklist after deploying the current DLL and restarting the game:

1. Confirm config contains `Enable Tactical Deployment Terrain Discipline = true`.
2. Start a fresh battle or load a save that reaches AI deployment/redeployment.
3. Search `BepInEx/LogOutput.log` for:

```bash
rg -n "TacDeployTerrain|TacDeployTerrainAdvice|TacticalDeployment|Exception|TargetInvocationException|missing-anchor|failed" "<GTCW>/BepInEx/LogOutput.log"
```

Pass criteria:

- `[TacDeployTerrain]` or `[TacDeployTerrainAdvice]` appears around deployment placement, not every frame.
- Any correction is tied to a terrain/deployment failure, not facing-only rotation.
- No player-side or player-subordinate deployment correction.
- No repeated `Exception`, `TargetInvocationException`, Harmony failure, missing-anchor warning, or #60 failure marker.
- Existing #53 pathfinder discipline remains stable.

If smoke fails, disable `Enable Tactical Deployment Terrain Discipline`, keep observer telemetry on, and record the failure in `docs/handoff.md` plus this file.

## Documentation Lifecycle

This file is the living source for current runtime behavior, config, smoke expectations, and rollback. The design spec and implementation plan under `docs/superpowers/` are point-in-time artifacts and should not be used as the current operational guide once smoke completes. After focused smoke passes, archive the spec and plan, then update:

- `docs/handoff.md`
- `docs/patch-catalog.md`
- `docs/bug-fixes/vanilla-tactical-battlefield.md`
- `MEMORY.md`
- this file
