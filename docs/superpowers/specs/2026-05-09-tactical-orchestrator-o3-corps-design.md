# Tactical Orchestrator O3 — ArmyIntent Direct-Child Enrichment + #42 Gate Extension

Status: active design spec. Replaces the prior O3 "corps authority" sketch after adversarial review. O3 is one workstream that lands in v0.3.0 between O2 (intent inference) and O4 (deferred division/reserve writes).

## Rescope rationale

The prior O3 sketch introduced a `CorpsOrchestrator` echelon between Army and Division. Adversarial review surfaced three blocking facts:

1. **Vanilla tactical battles have no corps tier.** Decompile constants are `unittyp 14 = Brigade`, `unittyp 15 = Division`, `unittyp 16 = Army` (mod's `TacticalUnitType` mirrors this in `src/WhiskeyRealism/Tactical/TacticalUnitType.cs:11-14`). Strategic-AI code references "corps" as a logical entity (`/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:13036`, `:17134`) but battle-tactical code uses only the three-tier `microaitaskupdatecycle == 14/15/16` (`:5557, :5581, :5585`). There is no `parentcorps`, `corpsregiment`, or equivalent field — confirmed by exhaustive decompile grep.
2. **`commandhierarchyshift` shifts effective `unittyp` at runtime.** `GamePrefs.commandhierarchyshift` (`:48456`) is loaded from `Config/unithierarchydescr{shift}.txt` (`:40363`), persisted in saves (`:55314, :69273`), and applied via `unittyp += GamePrefs.commandhierarchyshift` (`:67019`). UI strings use `unittypename[16 + commandhierarchyshift]` (`:23348`). Vanilla in-game text confirms: "Early armies may only have division levels. Military Act II allows raising grand armies with independant corps" (`:198605`). Hierarchy depth is era-dependent.
3. **Patch #42 already owns the gate surface.** `src/WhiskeyRealism/Patches/BattleFeudActionGatePatch.cs` is a Prefix replacement that fully mirrors `AIBattle.CheckForFeudGroupActions` (`:4931`) and owns the `bunits.SetWaypoint(group, …, useorderdelay: true, …)` call (line 89 of #42, mirroring `:4957`). Extending #42's existing decision before SetWaypoint requires no new patch surface, no Prefix-block of an unpatched method, no Transpiler, and no `BattleUnits.SetWaypoint` filter.

Consequence: collapse the planned `CorpsOrchestrator` into the existing `ArmyOrchestrator` as **per-direct-child intent enrichment**, and extend #42 to consult that intent. "Direct child" means whatever `Regiment.GetAttachedUnitsReg(directonly: true)` returns from the army root — usually divisions (unittyp 15), sometimes brigades (unittyp 14) under shifted hierarchy, occasionally a single deeper command. O3 makes no claim about the strategic-vs-tactical naming of those children; it operates on raw hierarchy position.

## Goal

O3 makes the army CO's plan authoritative over each direct-child command group's vanilla feud movement, and exposes per-direct-child enemy-intent inference as input to O4.

The army-level plan from O1/O2 becomes a per-direct-child role allocation:

- which direct child carries the main effort;
- which direct children support the main effort;
- which direct children fix, screen, refuse, reserve, or fall back;
- how each direct child's frontage interprets visible enemy intent;
- which vanilla feud movements are compatible with the assigned role and which are denied.

## Locked decisions

- O3 ships as one slice. No `CorpsOrchestrator` is created. `ArmyOrchestrator` is enriched in place.
- `ArmyIntent` gains `IReadOnlyList<DirectChildIntent>` and remains the single contract emitted to O4.
- Direct-child discovery is hierarchy-position-based, not unittyp-name-based. `commandhierarchyshift` is read once at battle start and used to compute the effective command-min threshold.
- The gate enforcement surface is the existing `BattleFeudActionGatePatch` (#42). No new patch is added.
- Allocation outputs are signature-bucketed and only recompute when bucketed evidence changes (mirrors `FrontSectorRuntime.Signature` 0.5-bucket pattern from the strategic Defense Intent Ledger).
- Gate decisions apply on AI-controlled sides only. Telemetry runs on all sides.
- O3 does not write regiment, brigade, artillery, reserve, fallback, charge, or retreat orders. Those remain O4/O5/O6.
- O3 does not create division/brigade child orchestrators. O4 may attach them later.
- The gate is default-off behind a new config flag. ArmyIntent enrichment fields are additive and on by default under the master orchestrator flag.

## Vanilla anchors (verified in `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`)

Discovery and traversal:
- `AIBattle.unitsused : List<Regiment>` `:3280` — per-side command groups; `AIBattle.sideofai` field at `:3287`.
- `Regiment` class `:108962`, `Regiment.unittyp` `:110834`, `Regiment.allattachedunits` `:110988`, `Regiment.parentregiment : GameObject` `:111132`.
- `Regiment.GetAttachedUnitsReg(bool excludedechainedunits = true, bool excludeskirmishers = true, int searchonlytype = -1, bool directonly = true, …)` `:119854`. Direct-only branch uses `transform.parent` equality at `:119889` against the calling regiment's `gameObject`.
- `BattleUnits.GetHierarchyTree(GameObject, …)` `:92714`, `BattleUnits.GetHierarchyTree(Regiment, …)` `:92720`, `HierarchyTree` shape at `:78349`.

Hierarchy-shift inputs:
- `GamePrefs.commandhierarchyshift : int` `:48456`, save-loaded at `:55314, :69273`, applied at `:67017-67019` and `:67043-67045`. Display indexing at `:23348, :23366`.

Existing gate surface to extend:
- `AIBattle.CheckForFeudGroupActions()` `:4931` calls `bunits.SetWaypoint(allgroupsassigned[i], closestEnemyUnit.transform.position, …, useorderdelay: true, …)` at `:4957`.
- Eligibility predicate at `:4940`: `unittyp > 13 && (ai_feudstance >= 0 | isplayeraiorfeud == 2) && regimentpaths <= 0 && !pathinterrupted && IsGroupStillAbleToFight(…)`.
- Mod's existing Prefix replacement: `src/WhiskeyRealism/Patches/BattleFeudActionGatePatch.cs` (#42) — already owns SetWaypoint, already takes/yields ownership safely, already integrates with `TacticalWlActionGuard`.

Movement primitive:
- `BattleUnits.SetWaypoint(Regiment, Vector3 targetpos, …, bool useorderdelay = true, …)` `:91232`. Order-delay preserves the W&L courier model; #42's existing call already passes `useorderdelay: true`.

Surfaces explicitly NOT touched by O3 (citations carry forward to O4/O5):
- `AdjustGroupAIStance() :4221` / `bunits.ChangeStance(…) :4272` — O5 brigade stance.
- `MicroAICheckForCharges() :4905` / `SetMovementMode(3) :4919` — O5 charge.
- `CheckLineFallbacks() :5118` — O5 fallback.
- `CheckUseOfReserves() :6062` / `RegimentSetPath(…) :6170`, `AssignReserves() :7017` — O4 reserves.
- `CheckAIBombardment() :3869` — O4 artillery.
- `CheckGlobalAIStrategy() :6314` — already rewired by #44 macro patch under O1.

## Existing whiskey state to build on

Implemented before O3:
- O0 scaffold: `TacticalBattleCoordinator`, `TacticalBattleOrchestrator`, `EchelonOrchestrator`, lifecycle detection, roster.
- O1 army layer: `ArmyOrchestrator`, `ArmyIntent`, `TacticalPlaybookCatalog`, `BattleMacroStrategyPatch` reading army macro intent.
- O2 intent loop: `TacticalIntentModel`, `EnemyVisibleState`, `ArmyIntentInference`, `ArmyTickCycle`, `ArmyEvidenceBuilder`, replan telemetry.
- Existing default-off Slice B writers/scorers: #41/#42/#44/#45/#48/B7/B8 + supporting evidence.

Not implemented before O3:
- Per-direct-child role allocation inside `ArmyOrchestrator`.
- Per-direct-child enemy-intent inference using `TacticalIntentModel` filtered to a child's frontage.
- Direct-child runtime registry seeded from `unitsused` + `GetAttachedUnitsReg(directonly: true)`.
- `BattleFeudActionGatePatch` consultation of orchestrator role before SetWaypoint.
- `Enable Tactical Orchestrator Direct-Child Gate` config flag.

## Architecture

Pure-logic side (testable, no Unity types):

- `ArmyOrchestrator` gains:
  - `void RegisterDirectChildren(IReadOnlyList<DirectChildSnapshot> snapshot)` — called once per battle and on hierarchy-change ticks.
  - `void ObserveDirectChildEvidence(string childId, DirectChildEvidence evidence)` — bucketed; signature-equal calls are no-ops.
  - `DirectChildRole GetDirectChildRole(string childId)` — read by #42 and tests; returns `Unknown` when no plan or no registration.
  - `IReadOnlyList<DirectChildIntent> CurrentDirectChildIntents` — emitted as part of `ArmyIntent`.
- `ArmyIntent` gains `IReadOnlyList<DirectChildIntent> DirectChildIntents`. All other fields unchanged.
- `DirectChildIntent` (record): `string ChildId`, `int RawUnitTyp`, `int EffectiveCommandLevel`, `string DisplayName`, `int PrimarySector`, `DirectChildRole Role`, `DirectChildAxis Axis`, `float SupportPriority01`, `float AggressionBias01`, `TacticalIntentModel EnemyIntent`.
- `DirectChildRole`: `Unknown | Main | SupportMain | Fix | Screen | RefuseLeft | RefuseRight | Reserve | Fallback`.
- `DirectChildAxis`: `None | Sector(int) | Withdraw | Hold`. New type — there was no shipped `CorpsAxis`; the prior O3 sketch named one but never implemented it.
- `DirectChildEvidence` (input bucket): `int OwnStrengthBucket`, `int EnemyStrengthBucket`, `bool ContactFlag`, `int PrimarySector`, `int FlankExposureBucket`, `float Confidence01`. Buckets use 0.5 ratios on strength and integer sector indices to match the strategic-layer signature pattern.
- `DirectChildAllocator` (pure): given current `ArmyIntent` plan + commander personality + ordered list of `DirectChildEvidence`, returns the role map. Deterministic, signature-stable, side-free.

Runtime / vanilla-touching side:

- `TacticalBattleCoordinatorRuntime.AttachDirectChildrenIfReady(ArmyOrchestrator, AIBattle)` — runs after `AttachArmyIfActive`, defers if `unitsused` has zero command-level entries, retries each tick until either populated or the battle ends. One-time `[once:o3-defer-discovery]` log on first deferral per side.
- `DirectChildDiscovery.Snapshot(AIBattle, GamePrefs.commandhierarchyshift)` — produces the `IReadOnlyList<DirectChildSnapshot>` consumed by `RegisterDirectChildren`. Pure aside from reading vanilla state.
- `DirectChildEvidenceBuilder.BuildAll(AIBattle, snapshot, EnemyVisibleState)` — produces the bucketed evidence list each tick; reuses O2's `EnemyVisibleState` filtering by sector.
- `BattleFeudActionGatePatch` extension (no new patch): inside the existing for-loop, *after* the W&L `TacticalWlActionGuard.Decide(...)` call and *before* the SetWaypoint call, consult the orchestrator. New helper `TacticalDirectChildGate.Decide(armyOrchestrator, group, configEnabled, sideIsAi, intendedTarget) → DirectChildGateDecision { Allow, Reason, Role }`. The W&L decision and the direct-child decision are AND'ed; either denial denies. Order: W&L first (existing behavior preserved), direct-child second (only consulted when W&L allows). Both denials log distinctly.

Why no new patch: #42 is already the Prefix replacement for `CheckForFeudGroupActions`. It already owns the SetWaypoint call, already takes/yields ownership in a try/catch, and already mirrors vanilla eligibility. Adding a second decision before SetWaypoint is a one-method change inside the existing Prefix.

## Direct-child discovery

The discovery rule must work under any `commandhierarchyshift` and must not assume a corps tier exists.

Inputs:
- `AIBattle battle` for one side (vanilla creates one `AIBattle` per side, keyed by `sideofai` `:3287`).
- `int commandHierarchyShift` read once via reflection on `GamePrefs.commandhierarchyshift` at battle start. Cached for the battle's lifetime.

Procedure:
1. Compute `effectiveCommandMin = TacticalUnitType.MaxCombat + 1 + commandHierarchyShift` (i.e. the smallest `unittyp` that vanilla treats as command-level for this battle, post-shift). Floor at 1; cap at 18.
2. Iterate `battle.unitsused`. For each `Regiment r`:
   - skip null; skip `!gameObject.activeInHierarchy`; skip if `r.unittyp < effectiveCommandMin`.
   - retain candidate as a potential army root.
3. The army root is the candidate with the highest `r.unittyp`. If multiple share the max (multi-army deployment), iterate each as its own root and emit one `DirectChildSnapshot` per army.
4. For each army root `rArmy`, call `rArmy.GetAttachedUnitsReg(directonly: true, includenonactiveunits: false)` (vanilla `:119854`). Result is the direct-child set.
5. For each child `c` in the result:
   - skip if `c.unittyp < effectiveCommandMin`. Combat regiments cannot be direct children of a command unit at this tier.
   - record `ChildId = "child-" + c.GetInstanceID().ToString()` (stable per battle, unique across multi-army cases).
   - record `RawUnitTyp = c.unittyp`, `EffectiveCommandLevel = c.unittyp - commandHierarchyShift`, `DisplayName = ((Object)c.gameObject).name`, `ParentArmyId = rArmy.GetInstanceID()`.
6. If the army root has zero direct children meeting the rule, emit a single synthetic `DirectChildSnapshot` with `ChildId = "synth-army-{instance-id}"`, `RawUnitTyp = rArmy.unittyp`, `Role` later allocated from whole-army intent. The synthetic case is normal under shallow hierarchy and must be silent (no warning).

Multi-army-per-side: yes, supported. Each army root produces its own children list. The role map covers every army on the side.

`unitsused` empty at attach: defer discovery until the next tick. Log `[once:o3-defer-discovery] side=N reason=empty-unitsused` once per battle. Do not register a synthetic until at least one command-level group appears.

`commandhierarchyshift` re-read: only at battle start. The shift is a save-time constant; it does not change mid-battle.

## Allocation rules (`DirectChildAllocator`)

Inputs: `ArmyIntent` plan + commander personality + ordered `IReadOnlyList<DirectChildEvidence>` (one per registered child, in registration order).

Outputs: ordered `IReadOnlyList<DirectChildIntent>` with role assignments.

Rules:
1. The child whose `PrimarySector` matches `plan.MainEffortSector` and has the highest `OwnStrengthBucket * (1 - FlankExposureBucket/maxBucket)` weight receives `Main`. Tie-break: registration order (first-registered wins). All deterministic-order claims in this section use registration order; lexicographic `ChildId` order is reserved for telemetry only.
2. Adjacent children (sector ± 1 from main) with `OwnStrengthBucket >= 1` and not on a refused flank receive `SupportMain`.
3. Children whose sector is in `plan.FixingSectors` and have `ContactFlag = true` receive `Fix`.
4. Children whose sector is in `plan.ScreeningSectors` or have low `OwnStrengthBucket` and low `EnemyStrengthBucket` receive `Screen`.
5. Every child with `FlankExposureBucket >= 2` receives a Refuse role: `RefuseLeft` if the child's `PrimarySector` is less than the chosen Main effort's sector (or `plan.MainEffortSector` when no Main was picked), `RefuseRight` otherwise. This means each exposed flank position holds its sector individually rather than picking only the extremes. Threshold = `2` on a 0-3 bucket scale.
6. Children with `OwnStrengthBucket >= 2`, `ContactFlag = false`, and not yet assigned receive `Reserve`.
7. Children with adverse-odds evidence (`EnemyStrengthBucket > OwnStrengthBucket + 1`) and inferred enemy `Attack` intent in their frontage receive `Fallback`. Fallback is intent-only at O3; it does not trigger any retreat write.
8. Anything still `Unknown` after the above stays `Unknown` and is treated by #42 as "no opinion → defer to W&L decision only".

Per-child enemy intent: each child gets its own `TacticalIntentModel` from `ArmyIntentInference.InferForFrontage(childPrimarySector, EnemyVisibleState, ownEvidenceBucket)`. Reuses the O2 inference function with a sector mask; no new model surface.

Stability: allocation runs only when the bucketed input vector for any child changes. The orchestrator stores the last evidence signature per child; a `DirectChildEvidence` whose tuple equals the cached signature is a no-op. This mirrors `FrontSectorRuntime.Signature` and `DefenseCooldownTable` from the strategic Defense Intent Ledger.

Minimum role-stability window: an allocated role cannot change more than once per `MinimumRoleHoldSeconds`. Default `8.0` pending smoke calibration against the live `ArmyTickCycle` cadence; tunable via private const for now (no config exposure until smoke shows the right value). Role-change attempts inside the window log `[once:o3-role-hold-skip] child=X requested=Y holdRemaining=Zs` once per child per battle.

## #42 gate extension

Pseudocode for the new branch inside `BattleFeudActionGatePatch.Prefix`, inserted between the existing W&L decision and the existing SetWaypoint call (around the current line 76 of `BattleFeudActionGatePatch.cs`):

```csharp
// existing W&L decision (unchanged)
var wlDecision = TacticalWlActionGuard.Decide(...);
tookOwnership = true;
group.lastfeudactiontime = CurrentBattleHour(bunits);

if (!wlDecision.Allow)
{
    LogDenied(group, wlDecision.Reason);
    continue;
}

// O3 direct-child gate (new)
var orchDecision = TacticalDirectChildGate.Decide(
    plugin: Plugin.Instance,
    coordinator: TacticalBattleCoordinator.Instance,
    battle: __instance,
    group: group,
    intendedTarget: closestEnemy.transform.position);

if (!orchDecision.Allow)
{
    LogDeniedOrch(group, orchDecision.Reason, orchDecision.Role);
    continue;
}

// existing SetWaypoint call (unchanged)
GameVars.DebugOwnLog("AI: group ...");
bunits.SetWaypoint(group, closestEnemy.transform.position, ...);
```

`TacticalDirectChildGate.Decide` rules:
- if `Plugin.Instance.EnableTacticalOrchestratorDirectChildGate.Value == false`: return `Allow=true, Reason="gate-disabled"`. No deny.
- if `coordinator == null` or no army orchestrator attached for this side: `Allow=true, Reason="no-orchestrator"`.
- if side is player-controlled (resolved via existing `bunits.alliance[sideofai] == GameVars.playeralliance && !GameVars.ai_vs_ai`): `Allow=true, Reason="player-side"`. Telemetry only, no deny on the player's own side.
- resolve `childId` from `group.GetInstanceID()`. If the group is a synthetic-army root or unregistered: `Allow=true, Reason="not-registered"`.
- read `role = ArmyOrchestrator.GetDirectChildRole(childId)`.
- decision per role:
  - `Main`, `SupportMain`: `Allow=true` if `intendedTarget` direction matches the assigned axis sector within ±60°. Otherwise `Allow=false, Reason="off-axis"`.
  - `Fix`: `Allow=true` if `intendedTarget` is within `GamePrefs.neededdistancefeudgroupmovement * 0.7f` of the group's current position (short pressure). Else `Allow=false, Reason="fix-no-wide"`.
  - `Screen`: `Allow=true` if the move keeps the group inside its frontage sector. Else `Allow=false, Reason="screen-out-of-sector"`.
  - `Reserve`: `Allow=false, Reason="reserve-not-committed"`.
  - `Fallback`: `Allow=true` if `intendedTarget` direction is away from the group's nearest enemy bearing (within ±90° of away). Else `Allow=false, Reason="fallback-not-withdraw"`.
  - `RefuseLeft`, `RefuseRight`: `Allow=true` if move stays inside the refused-flank sector. Else `Allow=false, Reason="refuse-out-of-sector"`.
  - `Unknown`: `Allow=true, Reason="role-unknown"`. No deny when O3 has no opinion.

Try/catch: any exception inside `TacticalDirectChildGate.Decide` returns `Allow=true, Reason="gate-exception"` and logs `OnceLog.Warning("tactical-direct-child-gate:exception", ...)`. Failures never block vanilla; #42's existing ownership/fallback contract is preserved.

`LogDeniedOrch`: emits `[TacticalDirectChildGate] side=N child=X role=Reserve action=deny reason=reserve-not-committed surface=CheckForFeudGroupActions` exactly once per (child, reason) tuple per battle via `OnceLog.Info`.

## Player-side and W&L safety

- The orchestrator gate runs only on AI-controlled sides. Player-controlled side: gate returns `Allow=true, Reason="player-side"` and emits telemetry only. The existing W&L decision in #42 continues to authorize player-subordinate movement on the player's side.
- W&L decision is consulted first; orchestrator gate is consulted second. Either denial denies.
- The orchestrator never direct-retasks a regiment. All movement still flows through the existing `bunits.SetWaypoint(... useorderdelay: true ...)` call inside #42's Prefix replacement.
- Any `dlcw_isundercommander` group continues to be protected by the existing `ContainsAttachedUnderCommander` path in #42.
- `TacticalDirectChildGate.Decide` never mutates orchestrator state. Allocation writes happen on the orchestrator tick; the gate is read-only.

## Telemetry

Required lines (all gated by master orchestrator flag; all `OnceLog.Info` keyed for one-emission-per-battle uniqueness):

```text
[TacticalDirectChildIntent] side=1 army=ANV child=child-12345 raw=15 effective=16 role=Main sector=3 axis=Sector3 support=0.85 enemyIntent=Defend confidence=0.72
[TacticalDirectChildGate] side=1 child=child-12345 role=Reserve action=deny reason=reserve-not-committed surface=CheckForFeudGroupActions
[TacticalDirectChildAlloc] side=1 army=ANV children=4 main=child-12345 supportMain=2 fix=1 screen=0 reserve=1 refuse=0 fallback=0
[TacticalDirectChildDiscovery] side=1 army=ANV root=ArmyANV rawUnittyp=16 shift=0 children=4 synthetic=false
[once:o3-defer-discovery] side=1 reason=empty-unitsused
[once:o3-role-hold-skip] child=child-12345 requested=SupportMain holdRemaining=4.2s
[TacticalDirectChildSyntheticArmy] side=0 army=ArmyEarly raw=15 reason=no-direct-children
```

Telemetry runs on both AI and player sides for visibility. Gate denial lines are emitted only when the gate actually fires (AI sides, master+gate flags on, role decided to deny).

## Config

New config entry only:

- `Enable Tactical Orchestrator Direct-Child Gate` — default **false**. Section: same as other O0-O2 orchestrator flags. Master orchestrator flag (`Enable Tactical Battle Orchestrator`) gates this transitively: gate is inert when master is off.

ArmyIntent enrichment fields (`DirectChildIntents`, allocator output, telemetry) are unconditionally on under the master flag — they are additive read-only outputs and harmless when no consumer reads them.

Default-flag promotion to `true` happens only after focused smoke proves: bounded gate decision rate (<10/minute baseline; <60/minute under contested feud), zero player-subordinate retasking on either side, zero repeated exceptions, zero missing vanilla-anchor warnings, and stable role assignments (no flicker >1/minute per child) across at least one full battle. Promotion lives in a follow-up commit, not in O3's initial deploy.

Rollback: if focused smoke shows any of the above acceptance criteria fail, the flag default stays `false` and the slice ships gate-as-telemetry-only until a follow-up addresses the regression.

## Tests (pure harness)

`tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` must add explicit `<Compile Include>` entries for each new source file under `src/WhiskeyRealism/Tactical/Orchestrator/` (per repo testing rule).

Required cases:

Discovery + threshold:
- direct-child discovery returns 0 entries when `unitsused` is empty.
- direct-child discovery returns 0 entries when no group meets `effectiveCommandMin`.
- direct-child discovery returns the army root's direct children at `commandhierarchyshift = 0` with army `unittyp = 16`.
- direct-child discovery returns the army root's direct children at `commandhierarchyshift = -1` with army `unittyp = 15` (early-war Confederate scenario).
- multi-army-per-side: two `unittyp = 16` roots produce two snapshots with non-colliding `ChildId`s.
- synthetic-army registration triggers when an army root has zero qualifying direct children.

Contract:
- `ArmyIntent.DirectChildIntents` is empty by default before any registration.
- `DirectChildIntent` sanitizes NaN/infinite values in `SupportPriority01`, `AggressionBias01`, `EnemyIntent.Confidence01`.
- registration is idempotent: re-registering the same `ChildId` with the same evidence is a no-op (signature-equal).

Allocator:
- main/support-main allocation assigns one `Main` and ≥0 `SupportMain` from a 4-child army where evidence concentrates strength on the main sector.
- `Fix` allocates only when `ContactFlag = true` and child sector ∈ `plan.FixingSectors`.
- `Reserve` allocates to high-strength low-contact uncommitted children.
- `Fallback` allocates only when `EnemyStrengthBucket > OwnStrengthBucket + 1` AND inferred enemy intent is `Attack`.
- `RefuseLeft`/`RefuseRight` allocate only to flank children with `FlankExposureBucket >= 2`.
- allocation is deterministic for equal inputs (golden test on a fixed evidence vector).
- allocation does not change when the bucketed evidence is identical (signature-stable no-op).
- minimum role-hold window suppresses role-change requests inside `MinimumRoleHoldSeconds` and emits the hold-skip log key once.

Gate decision:
- `TacticalDirectChildGate.Decide` returns `Allow=true reason="gate-disabled"` when the flag is off.
- returns `Allow=true reason="player-side"` when the side is player-controlled in single-player.
- returns `Allow=true reason="not-registered"` for an unregistered group.
- `Reserve` role denies with `reserve-not-committed`.
- `Main`/`SupportMain` allow on-axis movement and deny off-axis.
- `Fix` allows short pressure and denies wide lateral.
- `Screen` allows in-sector, denies out-of-sector.
- `Fallback` allows away-bearing, denies toward-enemy.
- `RefuseLeft`/`RefuseRight` allow in-flank, deny out-of-flank.
- `Unknown` always allows.
- gate exception path returns `Allow=true reason="gate-exception"` and increments the warning counter.

Coordinator wiring:
- `TacticalBattleCoordinatorRuntime.AttachDirectChildrenIfReady` is idempotent across multiple ticks.
- empty-`unitsused` at attach defers and logs the once-key `o3-defer-discovery` once.
- after deferral resolves, registration runs cleanly without a duplicate log.

## Smoke expectations (DLL deploy)

After `./build.sh` + deploy + hash verify:

- fresh `BepInEx/LogOutput.log` shows `[TacticalDirectChildDiscovery] side=0` and `side=1` once per battle when both sides have command groups.
- `[TacticalDirectChildIntent]` lines appear for each registered child with non-`Unknown` role at least once per battle.
- `[TacticalDirectChildAlloc]` summary appears at least once per battle per AI side.
- with `Enable Tactical Orchestrator Direct-Child Gate = false`, no `[TacticalDirectChildGate] action=deny` lines appear.
- with the gate flag forced on for a focused smoke run, deny lines appear for at least one role across a contested battle, with bounded volume (<60/minute on a single side).
- no repeated exceptions; no `tactical-direct-child-gate:exception` warning.
- no missing-vanilla-anchor warnings for #42 or O3 discovery.
- no direct regiment retask from O3.
- on the player side (single-player), no `[TacticalDirectChildGate] action=deny` lines appear regardless of the flag.
- `[once:o3-defer-discovery]` may appear at most once per side per battle and never on every tick.

## Defer boundaries

Deferred to O4:
- division-level child orchestrators below the army's direct children.
- reserve commitment timing and reserve-list mutation ownership.
- `CheckUseOfReserves` and `AssignReserves` rewrites.
- artillery prioritization (`CheckAIBombardment`).

Deferred to O5:
- group stance writes (`AdjustGroupAIStance`).
- charge initiation/denial (`MicroAICheckForCharges`).
- fallback/retreat writes (`CheckLineFallbacks`).
- brigade-level execution.

Deferred to O6:
- player-subordinate `DLC_WL.givenorder` integration.

Deferred to O7:
- cleanup of legacy Slice B decision ownership once orchestrator layers replace it.

## Not verified (must be confirmed in smoke)

- frequency of synthetic-army registration across `commandhierarchyshift ∈ {-1, 0, +1}` scenarios.
- whether `Regiment.GetInstanceID()` is stable for the lifetime of a single battle (assumed yes; vanilla rebuilds Regiment GameObjects per battle from save state).
- whether `unitsused` is stable enough across consecutive ticks that signature-bucketed allocation visibly settles within 8 seconds.
- whether the existing `Plugin.Instance.EnableWlTacticalChargeGuard` (B1) flag composes cleanly with the new gate flag in the same #42 prefix without log spam.

## Acceptance

O3 is accepted when:
- `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` passes with the new cases.
- `./build.sh` passes with 0 warnings, 0 errors.
- deployed `<GTCW>/BepInEx/plugins/WhiskeyRealism.dll` SHA-256 matches local `dist/WhiskeyRealism.dll`.
- fresh runtime smoke shows direct-child discovery, allocation, intent telemetry, and (if the gate flag is forced on for the smoke run) bounded gate denials.
- no direct regiment/brigade behavior writes originate from O3.
- no repeated exceptions or missing vanilla-anchor warnings.
- `docs/handoff.md` and `docs/patch-catalog.md` reflect the shipped O3 state, including the rescope explanation in handoff "What just shipped".
- the orchestrator umbrella spec (referenced from handoff) is updated to remove the `CorpsOrchestrator` echelon and to reflect the rescoped O3.
