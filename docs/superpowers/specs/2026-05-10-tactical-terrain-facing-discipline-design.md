# Tactical Terrain And Facing Discipline Design

Status: draft design spec, not implemented.
Date: 2026-05-10.
Owner: Whiskey Realism tactical AI workstream.

## Goal

Make tactical deployment and tactical movement feel less arbitrary by making Whiskey's AI-aware surfaces account for terrain validity, deployment-zone validity, and spotted enemy locations before applying final positions or final facing.

The first shipped slice should improve evidence and bounded discipline around vanilla behavior. It should not replace Grand Tactician's native NavMesh pathfinder, formation placement system, or battle AI wholesale.

## User Problem

Current tactical movement can produce three visible failures:

- Units can deploy or settle in water, on bad terrain edges, or in other weird positions after vanilla immediate placement.
- Units can face along a path tail or stale formation direction instead of naturally facing the enemy they have actually spotted.
- The existing observer logs deployment movement, but not enough terrain/facing evidence to prove why a position was accepted or rejected.

## Existing Whiskey Context

Already shipped behavior that this spec must build on:

- `TacticalPathfinderDisciplinePatch` (#53) guards `Regiment.AddPath` and removes failed non-target fragments from tactical land paths.
- `TacticalDeploymentObserverPatch` (#58) observes `BattleUnits.DoUnitPositioning`, `BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew`, and deployment phase transitions without changing vanilla state.
- `TacticalObserverPatch` already emits path-shape telemetry for tactical path diagnostics.
- `EnemyVisibleState`, `ArmyEvidenceBuilder`, and `TacticalDirectChildGate` already carry visible-enemy and sector evidence for orchestrator decisions.

This spec extends those shipped surfaces instead of adding a second tactical AI stack.

## Confirmed Vanilla Anchors

### Native Path Flow

Manual tactical pathing enters at `BattleUI.CheckPathSetting()`, which raycasts the terrain target and calls `BattleUnits.SetWaypoint(...)`.

`BattleUnits.SetWaypoint(Regiment, ...)` decides whether to call `Regiment.RegimentSetPath(...)` or, in deployment/immediate placement paths, directly moves and rotates the unit.

`Regiment.RegimentSetPath(...)` adjusts tactical final targets with `BattlefieldSetup.CheckIfFinalWaypointIsOnTerrain(..., 4)`, sets target height through `GetTerrainHeight`, decides road usage through `useroads` / `skiproads`, and calls `AddPath(...)` in a retry loop.

`Regiment.AddPath(...)` is the native NavMesh call site. It sets tactical NavMesh area costs for rivers, roads, bridges, and crossings, then calls `NavMesh.CalculatePath(...)`.

### Final Facing

`Regiment.RegimentSetPath(...)` uses `manualfinalrotation` when provided. Otherwise it assigns `lastwaypointrotation[i] = GetLastWaypointAngle()`.

That means final facing is caller-driven when a caller provides a manual rotation, and path-tail-driven when it does not. There is no general vanilla rule that says "face the nearest visible enemy at the end of a tactical move."

### Deployment Placement

`BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew(int foralliance)` computes AI deployment placements, sometimes uses a closest-enemy position to derive `manualfinalrotation`, calls `SetGroupFormation(... immediateplacement: true ...)`, then runs `MoveAllUnitsIntoDeploymentZone()` and `Regiment.RestrictTerrainPosition()` over units.

`BattleUnits.DoUnitPositioning()` can also place groups from scenario start positions or entry points with immediate placement.

`Regiment.SetNewPosition(...)` sets position and rotation, applies cover placement when requested, optionally clamps outside deployment zones, then checks land units for water center and water block positions.

`Regiment.CheckIfPositionIsOnWater(...)`, `CheckIfPositionIsOnWaterBlocks(...)`, and `CheckIfWaypointEndsOnWater(...)` mostly avoid terrain id `4`. These are reactive corrections, not a full candidate scorer for "good deployment terrain."

`Regiment.RestrictTerrainPosition(...)` primarily clamps out-of-bounds positions. Its water checks only run after an out-of-bounds correction path, so it is not sufficient as a general in-bounds deployment validator.

### Enemy Visibility

`Regiment.UpdateUnitRangeFast(...)` populates visible enemy lists such as `unitrange.enemyinrangereg` and fire-range lists with fog-of-war checks. It also populates closest-enemy fields.

The closest-enemy fields are useful but should not be treated as definitively spotted without a visibility check. Whiskey-facing logic should prefer visible enemy lists, existing orchestrator `EnemyVisibleState`, or other already-filtered evidence.

## Design Principles

1. Stay layered on vanilla. Use native placement, formation, and NavMesh systems; do not rewrite pathfinding.
2. Separate evidence from behavior. Ship telemetry first or together with default-off behavior so failures are auditable.
3. Prefer visible enemy evidence. Do not use omniscient enemy positions to rotate or place units.
4. Correct only bounded failures. If a safe candidate is not found quickly, leave vanilla untouched and log the reason once.
5. Keep tactical state ownership intact. Harmony patches may read orchestrator and ledger state, but tactical state writes remain in orchestrator tick cycles and existing tactical runtime owners.
6. Protect player control. Do not retask player-side units or player-subordinate formations outside existing W&L/order-gate rules.

## Proposed Slice

### Slice TFD-0: Terrain/Facing Telemetry

Extend deployment/path telemetry before behavior is enabled.

Add deployment snapshot fields:

- terrain id at group center
- terrain ids for sampled footprint points, when available
- whether center is water
- whether any sampled block point is water
- deployment-zone status
- current facing
- nearest visible enemy bearing and distance, when available
- facing delta to nearest visible enemy
- reason code for accepted, rejected, corrected, or unchanged

This can extend `TacticalDeploymentTelemetry` and `TacticalDeploymentObserverPatch` (#58). If implementation creates a new patch file, it should still be cataloged as an observer extension unless it changes vanilla state.

Expected bounded log marker:

```text
[TacDeployTerrain]
```

The log should be sparse: deployment phase start/end, changed groups, rejected/corrected candidates, and watch-list rows. No per-frame terrain spam.

### Slice TFD-1: Pure Terrain Candidate Scoring

Add a pure scoring model that can be tested without Unity:

- `TacticalTerrainCandidate`
- `TacticalTerrainSample`
- `TacticalEnemyBearingEvidence`
- `TacticalTerrainCandidateScore`
- `TacticalTerrainDisciplineRules`

Runtime adapters may gather terrain and enemy evidence from vanilla objects, but candidate scoring should consume plain values so the test harness can cover the decision rules.

Hard rejects:

- NaN or non-finite coordinates
- land unit center on water terrain
- any mandatory footprint sample on water terrain
- outside deployment zone when deployment-zone compliance is required
- correction distance exceeds the configured maximum
- no visible enemy evidence when the candidate depends on enemy-facing confidence

Soft penalties:

- farther from vanilla target
- worse facing delta to visible enemy
- terrain line crosses water or impassable river without a bridge/crossing intent
- too close to another friendly deployment center
- excessive rotation change when the unit already has a stable visible target

Rewards:

- closest safe point to vanilla placement
- same deployment zone as vanilla intended point
- facing delta under the configured threshold
- preserves vanilla road/formation intent when not in immediate deployment

### Slice TFD-2: Runtime Terrain Probe

Add a small tactical runtime adapter that reads vanilla battlefield data:

- `BattlefieldSetup.GetCurrentTerrainOnPos(...)`
- `BattlefieldSetup.GetTerrainHeight(...)`
- `BattlefieldSetup.CheckTerrainLine(...)`
- `BattlefieldSetup.CheckIfFinalWaypointIsOnTerrain(...)`
- `Frontline2.CheckIfWithinZone(...)` / `GetClosestPointInDeploymentZone(...)` where available

This adapter is the only place that should know about Unity `Vector3`, vanilla `Regiment`, or vanilla battlefield singletons.

It must degrade safely:

- catch reflection or null failures
- return "unknown" samples instead of throwing
- never write vanilla state
- log a first-fire warning through Whiskey logging when the runtime probe is unavailable

### Slice TFD-3: Default-Off Deployment Terrain Discipline

Add a default-off config flag:

```text
Enable Tactical Deployment Terrain Discipline = false
```

When enabled, a Postfix around AI deployment placement may inspect the just-placed AI groups and correct only clear terrain failures.

Patch surface:

- Primary: after `BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew(int foralliance)`
- Secondary observer-only: `BattleUnits.DoUnitPositioning()` for scenario start-position evidence

Behavior boundary:

- AI side only.
- No player-side movement unless the battle is explicitly AI-vs-AI and vanilla already treats both sides as AI controlled.
- Do not run during active player deployment interaction.
- Do not correct groups that already pass terrain and facing thresholds.
- Do not call path-order APIs for deployment corrections.
- Use native immediate-placement surfaces for corrections so vanilla formation and cover placement still run.
- If no safe candidate is found within the configured radius and attempt budget, leave vanilla unchanged.

Candidate search:

- Start with the vanilla placed center.
- Sample short-radius candidates around the vanilla point.
- Clamp candidates back into the deployment zone before scoring.
- Sample center and formation footprint points.
- Prefer the closest valid point that faces visible enemy evidence naturally.

Config knobs should be conservative:

```text
Tactical Deployment Terrain Discipline Max Correction Meters = 60
Tactical Deployment Terrain Discipline Max Candidates = 16
Tactical Facing Discipline Max Rotation Delta = 120
Tactical Facing Discipline Preferred Enemy Distance Meters = 1500
```

Exact names can be adjusted during implementation to match existing config style.

### Slice TFD-4: Default-Off Final Facing Discipline

Add a default-off config flag:

```text
Enable Tactical Final Facing Discipline = false
```

The first behavior version should not patch every `SetWaypoint(...)` call generically. It should feed manual final rotation only from Whiskey-owned tactical decisions, where the orchestrator already has role, axis, and visible-enemy evidence.

Preferred integration points:

- orchestrator-generated movement/deployment orders
- future command-node/reserve/brigade-stance writers
- AI deployment correction from TFD-3

Avoid:

- rotating player-issued orders
- rotating units with no visible enemy evidence
- rotating based solely on omniscient closest-enemy fields
- overriding vanilla final rotation every tick

Facing selection:

- Prefer the nearest visible enemy on the unit or formation's assigned axis.
- If there is no axis-consistent visible enemy, use the strongest visible enemy sector from `EnemyVisibleState`.
- If there is no visible enemy, preserve vanilla rotation.
- If the proposed rotation changes too much from a stable current facing and there is no urgent contact, preserve vanilla rotation.

## Patch Catalog Expectations

Implementation should use the next available patch ordinal at the time it lands. Do not assume an ordinal in this spec if another tactical patch ships first.

Likely implementation files:

- `src/WhiskeyRealism/Tactical/TacticalTerrainCandidateScorer.cs`
- `src/WhiskeyRealism/Tactical/TacticalTerrainCandidate.cs`
- `src/WhiskeyRealism/Tactical/TacticalTerrainProbe.cs` or equivalent runtime-only adapter
- `src/WhiskeyRealism/Patches/TacticalDeploymentTerrainDisciplinePatch.cs`
- extensions to `src/WhiskeyRealism/Patches/TacticalDeploymentObserverPatch.cs`
- extensions to `src/WhiskeyRealism/Tactical/TacticalDeploymentTelemetry.cs`

Keep runtime-only vanilla adapters out of the pure test project unless they are explicitly shimmed.

## Tests

Add pure harness coverage for:

- water center is rejected for land units
- footprint water sample is rejected
- out-of-zone candidate is rejected when deployment-zone compliance is required
- closest safe candidate wins over farther safe candidates
- facing to a visible enemy beats path-tail facing when both terrain candidates are valid
- no visible enemy evidence preserves vanilla facing
- correction beyond max distance preserves vanilla placement
- unknown terrain degrades to telemetry-only / unchanged behavior
- player-side or player-subordinate inputs are not corrected by the discipline scorer

If telemetry formatting is extended, add tests for stable reason codes and bounded row content.

## Verification Gates

Design/spec gate:

```bash
git diff --check
```

Implementation gate:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

DLL-affecting smoke gate:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

In-game smoke expectations:

- With telemetry enabled and behavior disabled, `[TacDeployTerrain]` logs show terrain/facing evidence without movement changes.
- With deployment terrain discipline enabled, AI deployment groups that initially land on water or outside zone are either corrected once or left unchanged with a bounded reason.
- No repeated Harmony exceptions.
- No player-issued movement or player-side deployment correction.
- No new `AddPath` fragment retention regressions.
- Units with visible enemy evidence finish facing within the configured threshold more often than vanilla baseline.

## Rollback

Each behavior slice must be independently disabled by config. Telemetry-only extensions can remain enabled if bounded and non-mutating.

If a runtime smoke shows unstable placement, disable `Enable Tactical Deployment Terrain Discipline` and keep `[TacDeployTerrain]` observer evidence for diagnosis.

If final-facing behavior rotates the wrong units, disable `Enable Tactical Final Facing Discipline` and preserve terrain validation.

## Not Verified Yet

- Exact terrain-id semantics beyond the path/deployment anchors already confirmed in vanilla, especially non-water terrain quality labels.
- Whether all `closestenemyunitfar*` assignments are visibility-safe in every branch. Treat them as suspect unless cross-checked against visible lists.
- The best formation-footprint sample pattern for every unit type.
- Live save examples that reproduce the user's worst water/weird-location deployments.
- Whether scenario-defined `battledata.startposition` rows should ever be corrected, or only observed.
