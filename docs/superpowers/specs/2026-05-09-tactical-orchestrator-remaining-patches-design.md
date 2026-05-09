# Tactical Orchestrator — Remaining Patches Design

Status: active design spec. Replaces the obsolete `2026-05-08-tactical-battle-orchestrator-design.md` umbrella, which described a four-echelon orchestrator-class hierarchy (Army → Corps → Division → Brigade) that was rescoped away during the O3 ship. The actual shipped architecture is two echelons (`TacticalBattleOrchestrator` → `ArmyOrchestrator`); below-army intent lives as a flat list on the army orchestrator. This spec documents that reality and lays out the remaining work as patch slices, not echelon classes.

## What's already shipped on `main` (no work needed)

- **O0 scaffold** — `TacticalBattleCoordinator`, per-side `TacticalBattleOrchestrator`, commander roster, battle lifecycle detection. Default-on, telemetry-only.
- **O1 army layer** — `ArmyOrchestrator`, `ArmyIntent`, `TacticalPlaybookCatalog` (14 playbooks), `BattleMacroStrategyPatch` (#44) reads `ArmyOrchestrator.CurrentMacroAi`.
- **O2 intent inference + adversarial loop** — `TacticalIntentModel`, `EnemyVisibleState`, `ArmyIntentInference`, `ArmyTickCycle`, replan-trigger evaluator. Each side reacts to the other.
- **O3 direct-child enrichment + #42 gate** — `DirectChildIntent`, `DirectChildAllocator` (Main / SupportMain / Fix / Screen / Reserve / Refuse / Fallback role assignment), alliance-keyed direct-child discovery from `BattleUnits.completeunitlist`, `TacticalDirectChildGate` consulted by #42 between the W&L decision and `bunits.SetWaypoint`. Default-off behind `Enable Tactical Orchestrator Direct-Child Gate`.
- **#58 deployment observer** — `TacticalDeploymentObserverPatch`, `TacticalDeploymentTelemetry`. Read-only telemetry of vanilla deployment surfaces.

The role map produced by `DirectChildAllocator` is the **single source of truth for below-army intent**. There is no `CorpsOrchestrator`, `DivisionOrchestrator`, or `BrigadeOrchestrator` class. There never will be. The vanilla method that decides each below-army surface (reserves, stance, charge, fallback, artillery) is the integration point — not a parallel mod-side echelon.

## The pattern (all remaining slices use this)

Every remaining slice follows three steps:

1. **Identify the vanilla method** that decides the surface.
2. **Add a patch** (Postfix preferred; Prefix only with explicit user consult per AGENTS.md) that consults `ArmyOrchestrator.GetDirectChildRole(childId)` for the affected group and gates/informs vanilla's decision based on the role.
3. **Ship default-off** behind a new config flag until smoke proves bounded behavior.

The `childId` lookup uses `"child-" + group.gameObject.GetInstanceID()` first, falling back to `"synth-army-" + …` (this is already implemented in #42's `DecideDirectChildGate` and is the reference pattern). Negative `GetInstanceID()` values are routine — the parser is in `TacticalBattleCoordinator.ParseInstanceIdFromChildId` and is harness-locked.

No echelon class proliferation. No new orchestrator state. The army orchestrator's role map is read by N patches; each patch owns its vanilla surface.

## Remaining slices

Each slice below is one patch addition consuming the existing role map. Listed in priority order; can be reordered by user preference.

### Slice 1 — Reserve commitment

**Vanilla anchors:**
- `AIBattle.CheckUseOfReserves()` `:6062` — moves a reserve via `RegimentSetPath(...)` at `:6170`.
- `AIBattle.AssignReserves()` `:7017` — mutates `ObjectiveChain.reservegroups`.

**Role consumption:**
- `Reserve` role on a child → defer commitment (deny `RegimentSetPath` call from feud or auto-commit triggers).
- `Main` role on a child + own-strength below threshold → permit early reserve release toward main effort.
- `Fallback` role + adverse odds → permit reserve release for screening retreat.

**Patch:** new `BattleReserveCommitGatePatch` (or extension to existing #48 if it's compatible). Prefix that snapshots `ObjectiveChain.reservegroups`, runs vanilla, then in Postfix uses orchestrator role to confirm or roll back the commitment. Try/finally-safe.

**Default flag:** `Enable Tactical Orchestrator Reserve Commitment Gate`. Off until smoke.

**Smoke expectations:**
- `[TacticalReserveCommitGate]` deny lines appear when `Reserve` role children attempt early commitment.
- Reserve commitment lag ≥ baseline vanilla; never accelerates uncontrollably.
- Player-controlled side: telemetry only, no gate writes.

**Why first:** the `Reserve` role is already produced and ignored every tick. This is the smallest delta to make O3 actually do something visible in-game.

### Slice 2 — Brigade stance under contact

**Vanilla anchor:** `AIBattle.AdjustGroupAIStance()` `:4221` writes group stance via `bunits.ChangeStance(...)` at `:4272`.

**Role consumption (per direct-child role of the group's parent army):**
- `Main` / `SupportMain` → permit aggressive stance (line, attack).
- `Fix` → bias toward holding line; deny disengagement.
- `Screen` → permit defensive stance only.
- `Reserve` → force defensive (no contact-driven stance flips).
- `Fallback` → permit fall-back stance even if vanilla wouldn't trigger it.
- `Refuse{Left,Right}` → bias toward refused-flank line.

**Patch:** Postfix on `AdjustGroupAIStance`. The shipped #45 (`BattleGroupStancePatch`) is default-off and currently feeds from `TacticalDoctrineScorer`. Re-target it to read `ArmyOrchestrator.GetDirectChildRole`. Enables the smoke-observed "5th brigade not forming line under fire" symptom to be addressed by a Reserve/Screen role override.

**Default flag:** existing `Enable Tactical Group Sector Stance` (already in `Plugin.cs`) — re-target the writer rather than add a new flag.

**Smoke expectations:**
- `[TacticalGroupStance]` lines reflect orchestrator-driven stance changes.
- Stance flip rate stays below baseline (no thrash).

### Slice 3 — Charge gate

**Vanilla anchor:** `AIBattle.MicroAICheckForCharges()` `:4905` initiates charges via `SetMovementMode(3)` at `:4919`.

**Role consumption:**
- `Main` → permit if local odds favorable.
- `SupportMain` → permit only when supporting an adjacent `Main` charge.
- `Fix` → deny (preserve line cohesion).
- `Reserve` / `Fallback` / `Refuse{Left,Right}` → deny.
- `Screen` → deny except chase-routed-enemy edge case.

**Patch:** existing #41 (`BattleChargeGatePatch`) is shipped default-off and already gates charges. Extend it to consult the orchestrator role in addition to the W&L safety check (same pattern as #42's W&L + orchestrator AND).

**Default flag:** existing `Enable W&L Tactical Charge Guard` covers the W&L portion; add `Enable Tactical Orchestrator Charge Gate` for the orchestrator branch (or compose into one — pick one config taxonomy).

**Smoke expectations:**
- Charges by `Main` brigades supported, charges by `Reserve`/`Fallback` denied with role-keyed reason strings.
- No player-subordinate retasking.

### Slice 4 — Line fallback

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

**Vanilla anchor:** `AIBattle.CheckAIBombardment()` `:3869` — writes artillery combat behavior.

**Role consumption (read per-child enemy intent + role):**
- Per-child `EnemyIntent.PrimaryIntent == Attack` → priority target = enemy in main-effort sector.
- `Refuse{Left,Right}` role → priority target = nearest enemy on the refused flank.
- `Main` role → priority target = enemy facing the main-effort sector.

**Patch:** existing B7 (`B7CheckAIBombardmentPatch`) is shipped default-off. Extend to consult orchestrator role + per-child enemy intent for target priority instead of the doctrine scorer.

**Default flag:** existing `Enable Tactical Artillery Doctrine` re-targeted, OR new `Enable Tactical Orchestrator Artillery Prio`.

**Smoke expectations:**
- Artillery targets shift toward orchestrator-flagged main-effort sectors when commanders are aggressive.
- No own-side fratricide; no rear-area target selection.

## Out of scope (forever — these were the misnamed echelon items)

- ❌ `CorpsOrchestrator` class.
- ❌ `DivisionOrchestrator` class.
- ❌ `BrigadeOrchestrator` class.
- ❌ Per-echelon intent structs cascading down a tower (`CorpsIntent`, `DivisionIntent`, `BrigadeDecision`).
- ❌ Multi-tier orchestrator state machines.

The army orchestrator's role map is the only mod-side intent state for tactical decisions. Vanilla's per-method decision logic is the only execution surface. Patches are the only integration point. Nothing else is needed and adding more layers would be over-engineering.

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
- `Regiment.unittyp` `:110834`, `Regiment.alliance`
- `GamePrefs.commandhierarchyshift` `:48456`

## Order-of-work and priority

Recommend order: 1 (reserves) → 2 (stance) → 3 (charge) → 4 (line fallback) → 5 (artillery).

Reasoning: Reserves are the cleanest first slice because the `Reserve` role is already being assigned and ignored. Stance is the highest-impact for visible AI quality (the "5th brigade not forming line" symptom). Charge already has half a patch in #41. Line fallback completes the defensive picture. Artillery is the most isolated and lowest-risk last.

Each slice is independently shippable. None depend on each other beyond the shared role-map contract. Slices can be reordered by user preference, parallelized across worktrees, or skipped.

## Acceptance per slice

A slice is accepted when:
- New/extended patch ships behind a default-off config flag.
- Console harness covers the role-consumption logic (deterministic decision-helper tests, same pattern as `TacticalDirectChildGate.Decide`'s 9 cases).
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

## Doc lifecycle

- This spec is the active source of truth for remaining tactical-orchestrator work. The previous umbrella spec (`docs/superpowers/specs/archive/2026-05-08-tactical-battle-orchestrator-design.md`) is archived as superseded; refer to it only for historical context on the original four-echelon design vision.
- Each slice produces its own implementation plan under `docs/superpowers/plans/` when it's promoted to active work, then archives to `docs/superpowers/plans/archive/` post-ship.
- Updates to this spec when slices ship: tick the "shipped" line in the corresponding slice section. Do not rewrite the spec on every ship; archive it only when ALL slices are complete.
