# Tactical Terrain And Facing Discipline

Living reference for tactical deployment terrain evidence, AI deployment terrain correction, and final-facing boundaries. Build, deploy and smoke status live in [`docs/handoff.md`](handoff.md).

- **Patch ordinal:** #60 `TacticalDeploymentTerrainDisciplinePatch`
- **Telemetry extension:** #58 `TacticalDeploymentObserverPatch` emits terrain/facing evidence for large deployment moves.

The behavior does not replace Grand Tactician's native pathfinder. It layers evidence and bounded correction around vanilla deployment placement.

## Behavior Boundary

#60 is a Prefix + Postfix pair on vanilla `BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew(int foralliance)`. It skips the player's alliance outside `ai_vs_ai`, and returns while a tutorial is active but not engaged. The Prefix never skips vanilla and writes nothing: it records each group's position before vanilla places it and the W&L current command's AI group (by the Postfix, vanilla has set that command's `permanentlydetached` again, 85886). Every write the Postfix makes is an immediate-placement Move through the battle core's order executor (`OrderExecutorRuntime.TryPlanInline` / `ExecutePlannedInline`, `IntentFlags.ImmediatePlacement`, sources `Deployment` and `DeploymentReserve`); #60 calls no vanilla writer.

Vanilla chooses each group's objective and facing from the true enemy (a scan of the whole unit list excludes any objective with an enemy within 50 m, 85604-85618; day 1 takes the objective nearest any enemy group, 86260-86281; the facing is the bearing to `unitrange.closestenemyunitfar`, 85809-85815). The Postfix replaces those inputs with what the side can know.

**Eligible groups.** A group is acted on only when it is an active, unrouted, vanilla-owned AI group with a `groupposition`, is not the W&L player's current command, vanilla **moved it in this call** (its position changed by more than 1 m since the Prefix), and:

- is not a fleet (`unittyp 17`);
- the deployment zone has at least 2 frontline vectors (otherwise every point reads as out of zone);
- vanilla did not place it on fortification lines (`BattleWriterPolicy.WouldTakeFortifiedDeploymentPath`: a defender, or a shaken attacker, with at least 3 eligible sub-units, which vanilla places with `ignoredeplyomentzone: true` on purpose). A Defender whose groups all take fortifications therefore gets no deployment reserve (recorded residual);
- `TacticalWriteGuard.CanWrite` passes (`Tactical Commander Mode = Active` and W&L/player protection).

**1. Fog-honest re-placement (`Enable Deployment And March Doctrine`; `DeploymentDoctrine`, pure; `DeploymentPlanRuntime`, the vanilla reads).** The assumed enemy point serves the objective choice, the facing and the spacing: under `RangeOnly` perception (every day-1 deployment, `GridNotPainted`) no observation is used, because vanilla's range lists admit enemy units within 150-450 m with no line-of-sight test (122650), so the point is the nearest enemy-owned entry point to the group (public scenario data), else the bearing to the map centre that `DoUnitPositioning` already used (87789-87790). Under `FogGrid` (a later-day deployment with a painted grid) the strength-weighted centroid of the Visible observations comes first. The candidate objectives are the ones vanilla considers (own zone, not enemy zone, not taken earlier in the pass, owner own or neutral; `frontline2.CheckIfWithinZone`), with the 50 m enemy filter applied to Visible observations under `FogGrid` and to enemy-owned entry points under `RangeOnly`. Day 1 takes the candidate nearest the assumed point, later days the nearest to the group (86285-86301). Spacing re-uses `CheckForSimilarPositions` (radius `brigadesinaigroup x 25 m`, 8 tries, 45 degree steps, compared against every group vanilla placed in the call: kept and locked groups stand where they are whatever their place in the order, and a re-placed front is also spaced from the re-placed groups before it (vanilla's list5 holds each objective-path group it placed, 85870). A locked group claims its objective before any other group chooses. A group is re-placed only when its fog-honest objective differs from vanilla's (vanilla's spot is not within its spacing radius of it) or its facing differs by more than 20 degrees; the W&L current command's AI group (and any group the snapshot flags as the player's, command or an ancestor of the command) is never re-placed, never terrain-corrected, and still holds its objective. Every position in the plan is a line front (the `targetpos` frame); the HQ conversion is `DeploymentLineFrontFromHq` / `DeploymentHqFromLineFront`.

**2. Deployment reserve echelon.** With at least three eligible direct children (the army's children from `DirectChildDiscovery`, groups mapped through the snapshot's parent map; none holding a player-owned unit or the W&L command, none cavalry-only or artillery-only), the `floor(N / 3)` children whose placements lie farthest from the assumed enemy point go 425 m behind the line centroid of the others, on lanes of at most 90 m, each child by one translation vector so its groups keep their offsets. Every proposed front is clamped into the zone with `frontline2.GetClosestPointInDeploymentZone`; a child that no longer stands 150 m behind the line (or whose clamp fails) reverts to its fog-honest placement. The executor's outcome for the placement (`Source = DeploymentReserve`, Issued or Moved) is what the direct-child allocator reads as `DirectChildEvidence.DeployedReserve` (a terrain correction of a reserve group keeps that source). The flag ends when a later Move of another source (the release producer on the child node, a posture move of a descendant) appears anywhere in the child's subtree, and `DeployedReserveMemory` keeps a child that `ReleaseReserves` (crisis, Exploit, odds) or any other role change released from falling back when the crisis passes. While it holds, the child gets `Reserve` ahead of the main-effort pick and ahead of play zone roles (a play's rect does not claim it); only the severe-overmatch Fallback check comes first. The executor, not a patch, owns the outcome table.

**3. Terrain discipline (unchanged rule).** It corrects an eligible group only when vanilla (or the re-placement) left a clear terrain or deployment-zone failure:

- center is water;
- a footprint sample is water;
- center is outside the deployment zone;
- a footprint sample is outside the deployment zone.

It never rotates a terrain-safe group for facing alone. `TacticalTerrainFacingDiscipline.Choose` scores bounded candidates (within `Max Correction Meters`, up to `Max Candidates`); the assumed enemy bearing (`TacticalTerrainProbe.GetAssumedEnemyBearing`, the same point as the re-placement; it reads no enemy unit) shapes the final facing only for an accepted correction.

**The write.** `SetGroupFormation` treats its target as the line front and places the HQ behind it by group depth plus back distance, so a corrected HQ point is converted to the line front before the write and the front is re-checked for water and zone. The executor issues `SetGroupFormation(immediateplacement: true, newpath: true, ignoredeplyomentzone: false)` once and then mirrors vanilla's deployment cleanup for the placed tree (85873-85883), clearing only:

- `Regiment.lastsetwaypointposition`
- `Regiment.immediateunitplacement`

It does not clear `lastsetwaypointrotation`, orders, paths, movement modes, command roles, reserve lists, rout state, or tactical orchestrator state. An immediate placement skips the executor's friendly-footprint, dedupe, cooldown and courier rules (the snapshot is stale after the pass's first placement; spacing among the pass's groups is the doctrine's); `WaypointValidator`'s terrain checks and the write guard still apply. Its outcome is judged by where the group stands at the next observation: Moved within 60 m of the placed HQ, else Failed with no retry.

## Config

Existing BepInEx config files override C# defaults. Defaults:

```ini
[Tactical]
Enable Tactical Deployment Terrain Discipline = true
Enable Deployment And March Doctrine = true
Tactical Deployment Terrain Discipline Max Correction Meters = 60
Tactical Deployment Terrain Discipline Max Candidates = 16
Tactical Deployment Facing Preferred Delta Degrees = 90

[Tactical Diagnostics]
Enable Tactical Deployment Observer = true
```

Rollback is config-only: `Enable Deployment And March Doctrine = false` restores vanilla's own objective, facing and placement (no re-placement, no reserve; the road march is off too); `Enable Tactical Deployment Terrain Discipline = false` stops only the terrain correction. The two flags are independent: the patch runs while either is on. Restart the game. Keep `Enable Tactical Deployment Observer = true` if terrain evidence is still useful. `Tactical Commander Mode = MonitorOnly` or `Off` also stops #60 writes.

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
| `BattleUnits.SetGroupFormation(Regiment, ...)` 91815 | Formation placement overload the order executor calls for an immediate placement. |
| `BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew` 85604-85618, 86260-86301, 85809-85815 | The omniscient inputs #60 replaces: the 50 m enemy filter over `completeunitlist`, the day-1 objective rule, the facing from `closestenemyunitfar`. |
| `BattleUnits.CheckForSimilarPositions` 94132 | The spacing rule the doctrine re-uses (substitute base angle: the bearing to the group's old position). |
| `Frontline2.GetClosestPointInDeploymentZone` 102819 | The zone clamp for a re-placed front and for the reserve. |
| `BattleUnits.SetGroupFormation` 92044-92058 | Target is the line front; HQ placed at front + (`groupdepth` + `groupunitdistancetoback`). |
| `BattleUnits.SetGroupFormation` 92056-92067 | Immediate placement writes `lastsetwaypointposition` and `lastsetwaypointrotation`. |
| `BattleUnits.SetGroupFormation` 92075-92205 | Walks attached units and grandchildren during formation placement. |
| `Regiment.AddPath` 130259-130527 | Native NavMesh area-cost and path calculation surface guarded by #53. |
| `Regiment.RegimentSetPath` 131073-131128 | Final tactical target terrain-id-4 clamp and target height. |
| `Regiment.RegimentSetPath` 131167-131188 | Retry loop can step away from target via `Vector3.MoveTowards(..., -0.5f)`. |
| `Regiment.RegimentSetPath` 131211-131260 | Final waypoint rotation uses manual rotation if supplied, otherwise `GetLastWaypointAngle()`. |
| `Regiment.SetNewPosition` 131303-131358 | Position/facing setter used by vanilla formation placement. |
| `Regiment.CheckIfPositionIsOnWater*` 131432-131556 | Vanilla water correction mainly checks terrain id `4`. |
| `BattlefieldSetup.CheckTerrainLine` 27638 | Not used by #60 or `TacticalTerrainProbe` (the probe's call was deleted in `93cab34`); the only Whiskey caller is `WaypointValidatorRuntime` (in-contact withdrawal line, `{4, 8}`, 2 m steps). |
| `Regiment.UpdateUnitRangeFast` 122545-122935 | Builds the per-regiment enemy lists; see [`docs/vanilla-vision-and-movement.md`](vanilla-vision-and-movement.md) for what they do and do not filter. |
| `Regiment.UpdateFlanking` 122959 | Filters out scouts/cavalry/melee-style contacts for comparable flank/enemy reasoning. |
| `BattleUnits.Grp.IsApplicableForDeployment` 78275 | Vanilla deployment eligibility check consulted by #60. |

## Runtime Components

- `TacticalTerrainFacingDiscipline` — pure scorer/decision model.
- `TacticalTerrainFacingTelemetry` — bounded `[TacDeployTerrain]` row formatter and sanitizer.
- `TacticalTerrainProbe` — Unity/vanilla adapter for terrain id (river id 4 as water), deployment-zone, footprint, height, and the assumed-enemy bearing. It has no terrain-line (water-crossing) probe and reads no enemy unit.
- `DeploymentDoctrine` — pure fog-honest placement, spacing, reserve proposal and clamp (`BattleMovementHonestyTests`).
- `DeploymentPlanRuntime` — the vanilla reads (objectives, zones, entry points, map size, snapshot) and the executor placements.
- `BattleWriterPolicy` — pure line-front conversion, fortified-path and eligibility rules.
- `TacticalDeploymentObserverPatch` — read-only terrain/facing evidence extension around large deployment moves.
- `TacticalDeploymentTerrainDisciplinePatch` — the #60 behavior patch.

## Telemetry

Expected markers:

- `[TacDeployTerrain]` from observer evidence on large deployment moves.
- `[TacDeployTerrainAdvice]` from #60 correction decisions, bounded per battle.
- `TacticalDeploymentPlan` Decision rows (alliance, assumed-point source, `day1`, `candidates`, `replaced`, `issued`, `refused`, `reserveGroups`, `reserveChildren`, `clampReverted`), at most 64 per battle; OnceLog lines `[once:tactical-battle-core:deployment-fog-first-fire]` and `[once:tactical-battle-core:deployment-reserve-first-fire]`.

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
- Day 1 `TacticalDeploymentPlan` rows read `EntryPoint` (or `MapCentre`), never `Visible`; the AI brigades face the enemy-owned entry points.
- With three or more eligible direct children, about a third stand 425 m behind the line at battle start and their `[TacticalDirectChildIntent]` rows read `role=Reserve` on the first allocation.
- The W&L player's command and its parent stand where vanilla placed them.
- No player-side or player-subordinate deployment correction.
- No repeated `Exception`, `TargetInvocationException`, Harmony failure, missing-anchor warning, or #60 failure marker.
- #53 pathfinder discipline remains stable.

If smoke fails, disable `Enable Tactical Deployment Terrain Discipline`, keep observer telemetry on, and record the failure in `docs/handoff.md`.
