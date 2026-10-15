# Tactical Terrain And Facing Discipline

Living reference for tactical deployment terrain evidence, AI deployment terrain correction, and final-facing boundaries. Build, deploy and smoke status live in [`docs/handoff.md`](handoff.md).

- **Patch ordinal:** #60 `TacticalDeploymentTerrainDisciplinePatch`
- **Telemetry extension:** #58 `TacticalDeploymentObserverPatch` emits terrain/facing evidence for large deployment moves.

The behavior does not replace Grand Tactician's native pathfinder. It layers evidence and bounded correction around vanilla deployment placement.

## Behavior Boundary

#60 is a Postfix on vanilla `BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew(int foralliance)`. It skips the player's alliance outside `ai_vs_ai`, and returns while a tutorial is active but not engaged.

A group is eligible only when it is an active, unrouted, vanilla-owned AI group with a `groupposition`, is not the W&L player's current command, and:

- is not a fleet (`unittyp 17`);
- the deployment zone has at least 2 frontline vectors (otherwise every point reads as out of zone);
- vanilla did not place it on fortification lines (`BattleWriterPolicy.WouldTakeFortifiedDeploymentPath`: a defender, or a shaken attacker, with at least 3 eligible sub-units, which vanilla places with `ignoredeplyomentzone: true` on purpose);
- `TacticalWriteGuard.CanWrite` passes (`Tactical Commander Mode = Active` and W&L/player protection).

It corrects an eligible group only when vanilla left a clear terrain or deployment-zone failure:

- center is water;
- a footprint sample is water;
- center is outside the deployment zone;
- a footprint sample is outside the deployment zone.

It never rotates a terrain-safe group for facing alone. `TacticalTerrainFacingDiscipline.Choose` scores bounded candidates (within `Max Correction Meters`, up to `Max Candidates`); visible enemy bearing shapes the final facing only for an accepted correction, and with no visible enemy the vanilla facing is kept.

Corrections use vanilla `BattleUnits.SetGroupFormation(... immediateplacement: true ...)` so attached-unit placement stays on the native formation path. `SetGroupFormation` treats its target as the line front and places the HQ behind it by group depth plus back distance, so the corrected HQ point is converted to the line front (`BattleWriterPolicy.DeploymentLineFrontFromHq`) and the front is re-checked for water and zone before the write. After the call, #60 mirrors vanilla's deployment cleanup for the corrected group tree by clearing only:

- `Regiment.lastsetwaypointposition`
- `Regiment.immediateunitplacement`

It does not clear `lastsetwaypointrotation`, orders, paths, movement modes, command roles, reserve lists, rout state, or tactical orchestrator state.

## Config

Existing BepInEx config files override C# defaults. Defaults:

```ini
[Tactical]
Enable Tactical Deployment Terrain Discipline = true
Tactical Deployment Terrain Discipline Max Correction Meters = 60
Tactical Deployment Terrain Discipline Max Candidates = 16
Tactical Deployment Facing Preferred Delta Degrees = 90

[Tactical Diagnostics]
Enable Tactical Deployment Observer = true
```

Rollback is config-only: set `Enable Tactical Deployment Terrain Discipline = false` and restart the game. Keep `Enable Tactical Deployment Observer = true` if terrain evidence is still useful. `Tactical Commander Mode = MonitorOnly` or `Off` also stops #60 writes.

## Vanilla Anchors

Confirmed against `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`:

| Anchor | Use |
|---|---|
| `BattleUI.CheckPathSetting` 168982-169019 | Manual pathing raycasts `NavTarget` and creates `SetWaypointData(... manualfinalrotation: -1f ...)`. |
| `BattleUnits.SetWaypoint` 91304-91323 | Group deployment waypoint path clamps through `frontline2.GetClosestPointInDeploymentZone(...)`, then calls `RegimentSetPath`. |
| `BattleUnits.SetWaypoint` 91453-91560 | Deployment-mode direct positioning / `SetGroupFormation`, water checks, deployment-zone checks. |
| `BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew` 85524-85872 | Native AI deployment placement surface used by #58/#60. |
| `BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew` 85684-85751 | Fortification-line placement (`num3 == 1`) that #60 leaves alone. |
| `BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew` 85873-85880 | Vanilla final cleanup clears `lastsetwaypointposition` and `immediateunitplacement` on active units. |
| `BattleUnits.SetGroupFormation(GameObject, ...)` 91815 | Public formation placement overload used by #60. |
| `BattleUnits.SetGroupFormation` 92044-92058 | Target is the line front; HQ placed at front + (`groupdepth` + `groupunitdistancetoback`). |
| `BattleUnits.SetGroupFormation` 92056-92067 | Immediate placement writes `lastsetwaypointposition` and `lastsetwaypointrotation`. |
| `BattleUnits.SetGroupFormation` 92075-92205 | Walks attached units and grandchildren during formation placement. |
| `Regiment.AddPath` 130259-130527 | Native NavMesh area-cost and path calculation surface guarded by #53. |
| `Regiment.RegimentSetPath` 131073-131128 | Final tactical target terrain-id-4 clamp and target height. |
| `Regiment.RegimentSetPath` 131167-131188 | Retry loop can step away from target via `Vector3.MoveTowards(..., -0.5f)`. |
| `Regiment.RegimentSetPath` 131211-131260 | Final waypoint rotation uses manual rotation if supplied, otherwise `GetLastWaypointAngle()`. |
| `Regiment.SetNewPosition` 131303-131358 | Position/facing setter used by vanilla formation placement. |
| `Regiment.CheckIfPositionIsOnWater*` 131432-131556 | Vanilla water correction mainly checks terrain id `4`. |
| `BattlefieldSetup.CheckTerrainLine` 27638 | Runtime probe checks terrain lines for water crossing evidence. |
| `Regiment.UpdateUnitRangeFast` 122545-122935 | Builds the per-regiment enemy lists; see [`docs/vanilla-vision-and-movement.md`](vanilla-vision-and-movement.md) for what they do and do not filter. |
| `Regiment.UpdateFlanking` 122959 | Filters out scouts/cavalry/melee-style contacts for comparable flank/enemy reasoning. |
| `BattleUnits.Grp.IsApplicableForDeployment` 78275 | Vanilla deployment eligibility check consulted by #60. |

## Runtime Components

- `TacticalTerrainFacingDiscipline` — pure scorer/decision model.
- `TacticalTerrainFacingTelemetry` — bounded `[TacDeployTerrain]` row formatter and sanitizer.
- `TacticalTerrainProbe` — Unity/vanilla adapter for terrain id, deployment-zone, footprint, water-line, height, and visible-enemy bearing evidence.
- `BattleWriterPolicy` — pure line-front conversion, fortified-path and eligibility rules.
- `TacticalDeploymentObserverPatch` — read-only terrain/facing evidence extension around large deployment moves.
- `TacticalDeploymentTerrainDisciplinePatch` — the #60 behavior patch.

## Telemetry

Expected markers:

- `[TacDeployTerrain]` from observer evidence on large deployment moves.
- `[TacDeployTerrainAdvice]` from #60 correction decisions, bounded per battle.

Deployment phase semantics match `TacticalDeploymentTelemetry`: `eodcycle > 0 || battlepasseddays > 0` means `eod`; otherwise `initial`, with `skipped` reserved for vanilla player/tutorial early returns.

## Smoke Checklist

1. Confirm the live config has `Enable Tactical Deployment Terrain Discipline = true` and `Tactical Commander Mode = Active`.
2. Start a fresh battle or load a save that reaches AI deployment/redeployment.
3. Search `BepInEx/LogOutput.log` and the tuning sidecars for:

```bash
rg -n "TacDeployTerrain|TacDeployTerrainAdvice|TacticalDeployment|Exception|TargetInvocationException|missing-anchor|failed" "<GTCW>/BepInEx/LogOutput.log"
```

Pass criteria:

- `[TacDeployTerrain]` or `[TacDeployTerrainAdvice]` appears around deployment placement, not every frame.
- Any correction is tied to a terrain/deployment failure, not facing-only rotation.
- Groups on fortification lines keep their vanilla placement.
- No player-side or player-subordinate deployment correction.
- No repeated `Exception`, `TargetInvocationException`, Harmony failure, missing-anchor warning, or #60 failure marker.
- #53 pathfinder discipline remains stable.

If smoke fails, disable `Enable Tactical Deployment Terrain Discipline`, keep observer telemetry on, and record the failure in `docs/handoff.md`.
