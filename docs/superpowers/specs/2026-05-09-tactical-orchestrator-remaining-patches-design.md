# Tactical Orchestrator — Remaining Patches Design

Status: implemented/superseded design artifact for tactical-orchestrator behavior
consumers; fresh Active smoke is still pending before archive. Living
implementation status is in
[`docs/tactical-orchestrator.md`](../../tactical-orchestrator.md) and
[`docs/tactical-operations-ledger.md`](../../tactical-operations-ledger.md).
This spec replaces the obsolete `2026-05-08-tactical-battle-orchestrator-design.md`
umbrella and supersedes the first 2026-05-09 "flat direct-child forever" version
of this spec.

The shipped architecture is still `TacticalBattleOrchestrator` -> `ArmyOrchestrator` with O3 `DirectChildIntent` role allocation. A 2026-05-09 decompile refresh confirmed that Grand Tactician has enough vanilla hierarchy and order-delay machinery to support a Scourge-inspired command tree, but not Scourge-style per-officer AI callbacks. The new target is therefore a **dynamic command-node hierarchy** built from vanilla `Regiment` command groups (`parentregiment`, `allattachedunits`, `GetAttachedUnitsReg(directonly: true)`, and `BattleUnits.GetHierarchyTree`). Command-node eligibility must use the shifted threshold `TacticalUnitType.MaxCombat + 1 + GamePrefs.commandhierarchyshift`, clamped to the shipped pure model's bounds; `unittyp > 13` is only a vanilla formation-API guard. All behavior writes still happen through vanilla `AIBattle` / `BattleUnits` decision surfaces.

There are still no hard-coded `CorpsOrchestrator`, `DivisionOrchestrator`, or `BrigadeOrchestrator` classes. Corps/division/brigade are runtime properties of vanilla command nodes, not separate mod-side class towers.

## Current implementation status

This spec is not the deployment ledger. The current ledger is [`docs/tactical-orchestrator.md`](../../tactical-orchestrator.md). As of 2026-05-14:

- Slice 0 command-node tree is shipped on `main` and smoke-confirmed.
- Slice 1 reserve commitment gate is shipped on `main`, build/deploy/hash verified, and focused battle smoke is pending.
- Slice 3 charge gate is shipped on `main`, build/deploy/hash verified, and focused battle smoke is pending.
- #60 deployment terrain/facing discipline is shipped on `main`, build/deploy/hash verified, and focused battle smoke is pending.
- #61 operations-ledger posture execution is shipped on `main` with `Tactical Commander Mode = Active`, build/deploy/hash verified, and Active battle smoke is pending. Current behavior lives in [`docs/tactical-operations-ledger.md`](../../tactical-operations-ledger.md).
- #62 W&L player-subordinate order bridge is implemented behind default-off `Enable Player Order Doctrine`, build/deploy/hash verified, and focused enabled smoke is pending.
- The older Slice 2 brigade stance, Slice 4 line fallback, and Slice 5 artillery priority labels are superseded by the full-spectrum doctrine consumers: #45 stance, `DoctrineConsumerDecisions.DecideFallback`, B7 artillery mission assignment, path-quality scoring, reserve policy, and #62 player-order bridge. Retune from fresh smoke evidence, not from the historical slice labels below.

## What's already shipped on `main` (no work needed)

- **O0 scaffold** — `TacticalBattleCoordinator`, per-side `TacticalBattleOrchestrator`, commander roster, battle lifecycle detection. Default-on, telemetry-only.
- **O1 army layer** — `ArmyOrchestrator`, `ArmyIntent`, `TacticalPlaybookCatalog` (14 playbooks), `BattleMacroStrategyPatch` (#44) reads `ArmyOrchestrator.CurrentMacroAi`.
- **O2 intent inference + adversarial loop** — `TacticalIntentModel`, `EnemyVisibleState`, `ArmyIntentInference`, `ArmyTickCycle`, replan-trigger evaluator. Each side reacts to the other.
- **O3 direct-child enrichment + #42 gate** — `DirectChildIntent`, `DirectChildAllocator` (Main / SupportMain / Fix / Screen / Reserve / Refuse / Fallback role assignment), alliance-keyed direct-child discovery from `BattleUnits.completeunitlist`, `TacticalDirectChildGate` consulted by #42 between the W&L decision and `bunits.SetWaypoint`. Default-off behind `Enable Tactical Orchestrator Direct-Child Gate`. This is the shipped substrate, not the final hierarchy model.
- **#58 deployment observer** — `TacticalDeploymentObserverPatch`, `TacticalDeploymentTelemetry`. Read-only telemetry of vanilla deployment surfaces.
- **Slice 0 command-node tree** — generic runtime command-node snapshots and intent resolver from vanilla `Regiment` hierarchy. Read-only; smoke-confirmed.
- **Slice 1 reserve commitment gate** — #59 `BattleReserveCommitGatePatch` plus #57 reserve-list command-role skip. Default-off; build/deploy/hash verified on `main`, focused smoke pending.

The command-node tree is now the current read model. The vanilla method that decides each below-army surface (reserves, stance, charge, fallback, artillery) remains the integration point.

## Decompile-backed design correction

The initial O3 rescope was right that Grand Tactician does not expose Scourge of War's AI plugin model. Searches for Scourge-style names such as `SowUnit`, `ArmyThink`, `CorpThink`, `DivThink`, `BrigThink`, `OffThink`, `NumSubs`, `SendOrdersByCour`, `RunPlay`, `TACType`, and `ERank` found no equivalent in `Assembly-CSharp.decompiled.cs`.

The correction is that Grand Tactician does expose enough hierarchy and order machinery to model command intent more deeply than one flat direct-child list:

- `AIBattle.Update()` centralizes the tactical loop, then calls `UpdateMicroAIAll()`.
- `AIBattle.UpdateAITasks()` runs reserves, moving targets, global macro strategy, stance, formation, and feud-action checks.
- `Regiment` exposes `unittyp`, `groupaiobject`, `allattachedunits`, `parentregiment`, `commander`, `dlcw_isundercommander`, group strength/morale/movement aggregates, and `GetAttachedUnitsReg(...)`.
- `BattleUnits.GetHierarchyTree(...)` creates a direct-child hierarchy record from vanilla attached units.
- `GamePrefs.commandhierarchyshift` shifts rank/unit interpretation, so the runtime tree must not assume fixed Army/Corps/Division/Brigade depths.
- `Regiment.AddOrderCourierline(...)` and `Regiment.ProcessOrders()` implement bugle/courier/order-delay propagation to command groups and attached subordinates. Whiskey can respect and steer this machinery through existing vanilla order surfaces.

Design implication: build a Scourge-inspired command **tree**, not a Scourge-style callback API clone.

## Options considered

1. **Keep the flat O3 direct-child map.** Lowest risk, but it leaves valid vanilla hierarchy unused and cannot express division/brigade intent when the battle has deeper command topology.
2. **Restore hard-coded echelon classes.** Familiar on paper, but brittle because `commandhierarchyshift` and scenario structure make "corps" or "division" a runtime fact, not a stable class boundary.
3. **Use generic dynamic command nodes.** Recommended. It uses vanilla hierarchy data where present, collapses cleanly when the tree is shallow, and lets patches resolve the nearest applicable command intent without inventing fixed class layers.

## Target architecture

### Command node runtime

Add a pure tactical model, tentatively:

- `CommandNodeId` — stable runtime id (`node-<instanceId>`), preserving negative Unity instance IDs.
- `CommandNode` — vanilla command group snapshot: instance id, name, alliance, raw `unittyp`, shifted hierarchy level, commander id, parent id, direct child ids, strength/morale/movement aggregates, primary sector, flank exposure, and W&L under-commander flag.
- `CommandNodeIntent` — role + sector + local constraints: Main / SupportMain / Fix / Screen / Reserve / RefuseLeft / RefuseRight / Fallback, with optional reserve/fallback/artillery/charge hints.
- `CommandTreeSnapshot` — one per side per orchestrator tick, built from `BattleUnits.completeunitlist` and `Regiment.GetAttachedUnitsReg(directonly: true)`.
- `CommandIntentResolver` — maps a vanilla group instance id to the nearest applicable `CommandNodeIntent`, falling back to O3 direct-child intent when the full tree is unavailable.

This is still a single generic hierarchy model. Do not create separate concrete classes for corps, division, and brigade.

### Tree construction rules

1. Root at the side's best available command group. Prefer the existing O3 army root if resolved; otherwise synthesize the same `synth-army-{id}` fallback O3 already uses.
2. Include vanilla command groups where `rawUnitTyp >= TacticalUnitType.MaxCombat + 1 + GamePrefs.commandhierarchyshift` after the same clamp used by `CommandTreeBuilder`, active, same alliance, not routed, and present in `BattleUnits.completeunitlist`. Do not use `unittyp > 13` as a complete echelon classifier; keep it only where the vanilla `SetGroupFormation` API itself requires it.
3. Build edges from `GetAttachedUnitsReg(excludedechainedunits: true, excludeskirmishers: true, searchonlytype: -1, directonly: true, includenonactiveunits: false, ...)`.
4. Keep raw `unittyp` and derived `EchelonKind` as data only. If hierarchy shift or scenario structure makes a top group a division, it is still the root command node.
5. Rebuild on the orchestrator tick, not inside Harmony patches. Patches read the latest immutable snapshot.

### Intent propagation

`ArmyOrchestrator` remains the owning brain. It publishes the root army intent, then allocates intent recursively through the command tree:

1. Root node receives current `ArmyIntent`.
2. Direct children receive O3-compatible roles using the existing `DirectChildAllocator` logic.
3. Deeper command nodes inherit parent role by default, then refine it using local sector, flank exposure, strength bucket, morale, movement state, and enemy intent.
4. Leaf command groups expose final role/hints to patches through `CommandIntentResolver`.

If the tree is shallow, this collapses to today's O3 behavior. If vanilla exposes corps → division → brigade, it acts like a real command cascade without a hard-coded class tower.

## The pattern (all remaining behavior slices use this)

Every remaining slice follows three steps:

1. **Identify the vanilla method** that decides the surface.
2. **Resolve the command-node intent** for the affected vanilla group by instance id. Use the new `CommandIntentResolver`; fall back to O3 `ArmyOrchestrator.GetDirectChildRole(childId)` if the tree is unavailable.
3. **Add or retarget a patch** (Postfix preferred; Prefix only with explicit user consult per AGENTS.md) that gates/informs vanilla's decision based on the resolved role.
4. **Ship default-off** behind a new config flag until smoke proves bounded behavior.

The `childId` lookup uses `"child-" + group.gameObject.GetInstanceID()` first, falling back to `"synth-army-" + …` (this is already implemented in #42's `DecideDirectChildGate` and is the reference pattern). Negative `GetInstanceID()` values are routine — the parser is in `TacticalBattleCoordinator.ParseInstanceIdFromChildId` and is harness-locked.

No hard-coded echelon class proliferation. The generic command tree is orchestrator state; behavior patches are read-only consumers. Each patch still owns one vanilla surface.

## Slice status and remaining work

Each slice below was independently shippable in the original plan. Slice 0, Slice 1, Slice 3, #60, #61, and #62 are now shipped to `main`; the remaining historical Slice 2/4/5 descriptions are traceability notes only and are superseded by the living doctrine consumers.

### Slice 0 — Dynamic command-node tree

**Status:** shipped on `main`; runtime `[TacticalCommandTree]` smoke confirmed both AI sides. Living proof is in [`docs/tactical-orchestrator.md`](../../tactical-orchestrator.md).

**Vanilla anchors:**
- `BattleUnits.completeunitlist` — vanilla battle regiment inventory populated from the battle scene.
- `Regiment.GetAttachedUnitsReg(...)` `:119854` — canonical attached-unit traversal and direct-child filter.
- `BattleUnits.GetHierarchyTree(...)` `:92720` — vanilla hierarchy wrapper over direct children.
- `Regiment.unittyp` `:110834`, `Regiment.allattachedunits` `:110988`, `Regiment.parentregiment` `:111132`.
- `GamePrefs.commandhierarchyshift` rank/unit interpretation `:67017`.

**Work:**
- Add pure command-node contracts and allocator/resolver tests.
- Add runtime snapshot builder under `Tactical/Orchestrator/` that reads vanilla data outside patch write paths.
- Retain O3 direct-child map as compatibility fallback.
- Emit bounded `[TacticalCommandTree]` telemetry: side, root id, node count, max depth, raw unittyp distribution, missing-parent count.

**Default flag:** no behavior-writing flag. The tree may be default-on behind `Enable Tactical Battle Orchestrator` because it is read-only telemetry + pure intent state.

**Smoke expectations:**
- Both AI sides produce a tree or explicit synth-root fallback.
- Tree depth varies by battle/scenario without exceptions.
- O3 direct-child roles remain identical when the tree has only root + direct children.
- No player-side behavior writes.

**Why first:** all remaining Scourge-inspired behavior needs a stable command-node lookup before it can consume deeper hierarchy intent.

### Slice 1 — Reserve commitment

**Status:** shipped on `main`; console/build/deploy/hash verified; focused gate-OFF/gate-ON battle smoke pending.

**Vanilla anchors:**
- `AIBattle.CheckUseOfReserves()` `:6062` — moves a reserve via `RegimentSetPath(...)` at `:6170`.
- `AIBattle.AssignReserves()` `:7017` — mutates `ObjectiveChain.reservegroups`.

**Role consumption:**
- `Reserve` node → defer commitment.
- `Main` node + own-strength below threshold → permit early reserve release toward main effort.
- `Fallback` node + adverse odds → permit reserve release for screening retreat.
- Parent node `Reserve` should dominate child-local aggression unless vanilla has already committed the child into contact.

**Patch:** new `BattleReserveCommitGatePatch` (or extension to existing #48 if it's compatible). Prefix that snapshots `ObjectiveChain.reservegroups`, runs vanilla, then in Postfix uses orchestrator role to confirm or roll back the commitment. Try/finally-safe.

**Default flag:** `Enable Tactical Orchestrator Reserve Commitment Gate`. Off until smoke.

**Smoke expectations:**
- `[TacticalReserveCommitGate]` deny lines appear when `Reserve` role children attempt early commitment.
- Reserve commitment lag ≥ baseline vanilla; never accelerates uncontrollably.
- Player-controlled side: telemetry only, no gate writes.

**Why first:** the `Reserve` role is already produced and ignored every tick. This is the smallest delta to make O3 actually do something visible in-game.

### Slice 2 — Brigade stance under contact

**Status:** superseded by #45 full-spectrum stance doctrine and #61 operations-ledger posture execution. The historical patch shape below is traceability only.

**Vanilla anchor:** `AIBattle.AdjustGroupAIStance()` `:4221` writes group stance via `bunits.ChangeStance(...)` at `:4272`.

**Role consumption (per resolved command-node intent):**
- `Main` / `SupportMain` → permit aggressive stance (line, attack).
- `Fix` → bias toward holding line; deny disengagement.
- `Screen` → permit defensive stance only.
- `Reserve` → force defensive (no contact-driven stance flips).
- `Fallback` → permit fall-back stance even if vanilla wouldn't trigger it.
- `Refuse{Left,Right}` → bias toward refused-flank line.

**Patch:** Postfix on `AdjustGroupAIStance`. The shipped #45 (`BattleGroupStancePatch`) is default-off and currently feeds from `TacticalDoctrineScorer`. Re-target it to read `CommandIntentResolver`, with O3 direct-child fallback. Enables the smoke-observed "5th brigade not forming line under fire" symptom to be addressed by a Reserve/Screen role override.

**Default flag:** existing `Enable Tactical Group Sector Stance` (already in `Plugin.cs`) — re-target the writer rather than add a new flag.

**Smoke expectations:**
- `[TacticalGroupStance]` lines reflect orchestrator-driven stance changes.
- Stance flip rate stays below baseline (no thrash).

### Slice 3 — Charge gate

**Status:** implemented and merged to `main`; console/build/deploy/hash verified; focused gate-OFF/gate-ON battle smoke pending before release claims.

**Vanilla anchor:** `AIBattle.MicroAICheckForCharges()` `:4905` initiates charges via `SetMovementMode(3)` at `:4919`.

**Role consumption:**
- `Main` → permit if local odds favorable.
- `SupportMain` → permit only when supporting an adjacent `Main` charge.
- `Fix` → deny (preserve line cohesion).
- `Reserve` / `Fallback` / `Refuse{Left,Right}` → deny.
- `Screen` → deny except chase-routed-enemy edge case.

**Patch:** existing #41 (`BattleChargeGatePatch`) is shipped default-off and already gates charges. Extend it to consult the resolved command-node role in addition to the W&L safety check (same pattern as #42's W&L + orchestrator AND).

**Default flag:** existing `Enable W&L Tactical Charge Guard` covers the W&L portion; add `Enable Tactical Orchestrator Charge Gate` for the orchestrator branch (or compose into one — pick one config taxonomy).

**Smoke expectations:**
- Charges by `Main` brigades supported, charges by `Reserve`/`Fallback` denied with role-keyed reason strings.
- No player-subordinate retasking.

### Slice 4 — Line fallback

**Status:** superseded by `DoctrineConsumerDecisions.DecideFallback`, B8 fallback consumers, and #61 fallback target execution. The historical patch shape below is traceability only.

**Vanilla anchor:** `AIBattle.CheckLineFallbacks()` `:5118` — evaluates per-attached-unit fallback under enemy pressure.

**Role consumption:**
- `Fallback` → permit fallback at lower morale/pressure threshold than vanilla.
- `Main` → suppress fallback (hold the line longer).
- `Fix`/`Screen` → vanilla default.
- `Reserve` → suppress fallback (reserves shouldn't be in contact yet; if they are, vanilla's normal threshold applies).

**Patch:** new `BattleLineFallbackPatch` (or extension to B8a observer if it's structured for write extension). Postfix on `CheckLineFallbacks`. Snapshot/restore pattern if vanilla's fallback decision needs to be overridden.

**Default flag:** `Enable Tactical Orchestrator Line Fallback Gate`. Off until smoke.

**Smoke expectations:**
- `Fallback`-role brigades fall back earlier; `Main`-role brigades hold longer.
- Fallback-rate per minute remains bounded.

### Slice 5 — Artillery target priority

**Status:** superseded by B7 `TacticalArtilleryMissionPlanner` weak-point assignment, ammo mission selection, field-of-fire checks, and safe reposition intent. The historical patch shape below is traceability only.

**Vanilla anchor:** `AIBattle.CheckAIBombardment()` `:3869` — writes artillery combat behavior.

**Role consumption (read per-node enemy intent + role):**
- Node-level `EnemyIntent.PrimaryIntent == Attack` → priority target = enemy in main-effort sector.
- `Refuse{Left,Right}` role → priority target = nearest enemy on the refused flank.
- `Main` role → priority target = enemy facing the main-effort sector.

**Patch:** existing B7 (`B7CheckAIBombardmentPatch`) is shipped default-off. Extend to consult resolved command-node role + per-node enemy intent for target priority instead of the doctrine scorer.

**Default flag:** existing `Enable Tactical Artillery Doctrine` re-targeted, OR new `Enable Tactical Orchestrator Artillery Prio`.

**Smoke expectations:**
- Artillery targets shift toward orchestrator-flagged main-effort sectors when commanders are aggressive.
- No own-side fratricide; no rear-area target selection.

## Out of scope

- ❌ `CorpsOrchestrator` class.
- ❌ `DivisionOrchestrator` class.
- ❌ `BrigadeOrchestrator` class.
- ❌ Per-echelon intent structs cascading down a tower (`CorpsIntent`, `DivisionIntent`, `BrigadeDecision`).
- ❌ Scourge-style callback imitation (`ArmyThink`, `CorpThink`, `DivThink`, `BrigThink`) — GT does not expose those entry points.
- ❌ Patch-time tree construction inside Harmony write surfaces.

Generic command nodes are allowed. Hard-coded echelon class towers are not. Vanilla's per-method decision logic is the only execution surface. Patches are the only write integration point.

## Vanilla anchors (verified in `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`)

All anchors below are decompile-confirmed and in-use by shipped or proposed patches. Same anchors O3 documented in its rescoped spec.

- `AIBattle.AdjustGroupAIStance` `:4221`, `bunits.ChangeStance` `:4272`
- `AIBattle.MicroAICheckForCharges` `:4905`, `SetMovementMode(3)` `:4919`
- `AIBattle.CheckForFeudGroupActions` `:4931` (already owned by #42)
- `AIBattle.CheckLineFallbacks` `:5118`
- `AIBattle.CheckUseOfReserves` `:6062`, `RegimentSetPath` `:6170`
- `AIBattle.AssignReserves` `:7017`
- `AIBattle.CheckAIBombardment` `:3869`
- `AIBattle.CheckGlobalAIStrategy` `:6314` (already owned by #44)
- `BattleUnits.SetWaypoint(Regiment, …, useorderdelay = true, …)` `:91232`
- `BattleUnits.completeunitlist` (already used for alliance-keyed discovery)
- `Regiment.GetAttachedUnitsReg(directonly: true)` `:119854`
- `BattleUnits.GetHierarchyTree` `:92720`
- `Regiment.unittyp` `:110834`, `Regiment.allattachedunits` `:110988`, `Regiment.parentregiment` `:111132`
- `Regiment.AddOrderCourierline` `:125009`, `Regiment.ProcessOrders` `:125173`
- `GamePrefs.commandhierarchyshift` rank/unit interpretation `:67017`

## Order-of-work and priority

Original recommended order was 0 (command-node tree) -> 1 (reserves) -> 2 (stance) -> 3 (charge) -> 4 (line fallback) -> 5 (artillery). Actual execution shipped 0 and 1, then implemented 3 as an independent #41 consumer because the charge surface already had a narrow default-off owner. The later full-spectrum doctrine pass superseded the remaining order by wiring stance, fallback, artillery, endurance, reserve, path-quality, battle-line geometry, and player-order gates through the living operations-ledger consumers.

Reasoning: the command-node tree is the required foundation for the Scourge-inspired correction. Reserves are the cleanest first behavior slice because the `Reserve` role is already being assigned and ignored. Stance is the highest-impact for visible AI quality (the "5th brigade not forming line" symptom). Charge already has half a patch in #41. Line fallback completes the defensive picture. Artillery is the most isolated and lowest-risk last.

Each behavior slice depends on Slice 0 unless explicitly scoped as an O3 direct-child-only stopgap. Slices can be reordered by user preference, parallelized across worktrees, or skipped after Slice 0 lands.

## Acceptance per slice

A slice is accepted when:
- New/extended patch ships behind a default-off config flag.
- Console harness covers the command-node or role-consumption logic (deterministic decision-helper tests, same pattern as `TacticalDirectChildGate.Decide`'s 9 cases).
- `./build.sh` clean.
- Deployed DLL hash matches `dist/WhiskeyRealism.dll`.
- Gate-OFF smoke (default) shows zero behavior writes from the new patch.
- Gate-ON focused smoke shows bounded behavior writes with role-keyed telemetry, zero exceptions, zero player-side writes, zero missing-anchor warnings.
- `docs/handoff.md` "What just shipped" records the slice + DLL hash.
- `docs/patch-catalog.md` records the new patch ordinal or the extension to the existing patch.

## Locked decisions (carry-over from the old umbrella that still hold)

- Master orchestrator flag `Enable Tactical Battle Orchestrator` gates everything below it.
- Per-feature default-off flags for any patch that writes vanilla state.
- Patches are read-only to orchestrator state. Orchestrator writes happen on the per-battle tick cycle and on event-driven role re-allocation.
- Never throw from a patch — outer try/catch with bounded `OnceLog.Warning`.
- Player-controlled side: orchestrator emits telemetry; gates do not write on the player's own side.
- W&L-protected groups remain protected by the existing #42-style W&L decision; orchestrator gate AND's with W&L, never overrides it.
- Tree construction is read-only and happens on orchestrator ticks, not inside behavior patches.
- Raw `unittyp` and shifted echelon labels are evidence. They do not determine class type.

## Doc lifecycle

- This spec is no longer the active design source for remaining tactical-orchestrator work. Current implementation/deploy/smoke state lives in [`docs/tactical-orchestrator.md`](../../tactical-orchestrator.md) and [`docs/tactical-operations-ledger.md`](../../tactical-operations-ledger.md). The previous umbrella spec (`docs/superpowers/specs/archive/2026-05-08-tactical-battle-orchestrator-design.md`) is archived as superseded; refer to it only for historical context on the original hard-coded four-echelon design vision.
- Each slice produces its own implementation plan under `docs/superpowers/plans/` when it's promoted to active work, then archives to `docs/superpowers/plans/archive/` post-ship.
- Updates to this spec when slices ship: tick the "shipped" line in the corresponding slice section. Do not rewrite the spec on every ship; archive it only when ALL slices are complete.
