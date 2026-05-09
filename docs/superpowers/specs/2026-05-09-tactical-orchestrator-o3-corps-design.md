# Tactical Orchestrator O3 Corps Design

Status: active design spec for O3 only. This spec replaces the stale O3 sketch assumptions before any O3 implementation plan is written.

Scope: add the corps echelon as a production authority layer between O2 Army and later O4 Division. O3 builds real corps orchestrators, assigns subordinate command roles, infers enemy corps/frontage intent, and gates command-level movement through existing safe surfaces. O3 does not implement division reserve/artillery authority or brigade stance/charge/fallback execution; those remain O4 and O5.

## Goal

O3 makes corps a real tactical decision layer, not a telemetry-only layer.

The army-level plan from O1/O2 becomes an authoritative per-corps allocation:

- which corps carries the main effort;
- which corps supports the main effort;
- which corps fixes, screens, refuses, reserves, or falls back;
- which confirmed subordinate command nodes inside a corps receive those roles;
- how each corps reacts to enemy intent inside its own frontage.

The design must stay grounded in Grand Tactician's actual battle hierarchy and patch surfaces. It must not depend on guessed vanilla fields.

## Locked Decisions

- O3 is its own phase. Do not collapse O3-O5 into a new mega-plan.
- O3 is full-control corps authority, not telemetry-only.
- O3 creates `CorpsOrchestrator` and emits authoritative `CorpsIntent`.
- O3 owns corps frontage, enemy corps/frontage intent, and per-subordinate command role assignment.
- O3 may influence command-level movement through the existing command-group gate surface.
- O3 must not directly write regiment, brigade, artillery, reserve, fallback, charge, or retreat orders that belong to O4/O5.
- Use a per-side `default-corps` fallback when vanilla hierarchy does not expose clean corps-like children.
- Use shared main effort: one `main` role plus zero or more `support-main` roles.
- Include adversarial corps-level intent inference.
- Emit a full role map for confirmed subordinate command nodes only.
- Do not synthesize pseudo-divisions or brigade clusters in O3.
- Do not use `parentcorps`; it is not verified in vanilla.

## Vanilla Anchors

Confirmed:

- `AIBattle.unitsused` exists as a battle-side list of groups at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:3280`.
- `AIBattle.objectivechain` exists at `:3282` and remains the reserve/objective chain source O2 already uses.
- `Regiment` is the vanilla unit/group type at `:108962`.
- `Regiment.unittyp` exists at `:110834`.
- `Regiment.allattachedunits` exists at `:110988`.
- `Regiment.parentregiment` exists at `:111132`.
- `Regiment.GetAttachedUnitsReg(...)` exists at `:119854`.
- `GetAttachedUnitsReg(... directonly: true ...)` uses direct Unity transform parent equality at `:119889`.
- `BattleUnits.GetHierarchyTree(GameObject, ...)` and `BattleUnits.GetHierarchyTree(Regiment, ...)` exist at `:92714` and `:92720`.
- `BattleUnits.SetWaypoint(Regiment, ..., useorderdelay = true, ...)` exists at `:91232`.

Not found:

- `parentcorps`
- `parentCorps`
- `corpsregiment`
- `corpsRegiment`
- `parent_corps`

Behavior write anchors:

- `AIBattle.CheckGlobalAIStrategy()` at `:6314` owns army/global `macroai`. O1 already rewires this through `BattleMacroStrategyPatch`.
- `AIBattle.CheckForFeudGroupActions()` at `:4931` moves command groups under feud/AI command pressure and calls `BattleUnits.SetWaypoint(... useorderdelay: true ...)` at `:4957`. This is the safe O3 command-level behavior surface.
- `AIBattle.AdjustGroupAIStance()` at `:4221` writes group stance through `bunits.ChangeStance(...)` at `:4272`. This is O5 brigade/group execution, not O3.
- `AIBattle.MicroAICheckForCharges()` at `:4905` starts charge movement through `SetMovementMode(3)` at `:4919`. This is O5.
- `AIBattle.CheckLineFallbacks()` at `:5118` evaluates line fallback on attached combat units. This is O5.
- `AIBattle.CheckUseOfReserves()` at `:6062` directly moves support via `RegimentSetPath(...)` at `:6170`. This is O4 reserve behavior.
- `AIBattle.AssignReserves()` at `:7017` mutates objective-chain reserve assignment. This is O4.
- `AIBattle.CheckAIBombardment()` at `:3869` writes artillery combat behavior. This is O4.

## Existing Whiskey State

Implemented before O3:

- O0 scaffold: `TacticalBattleCoordinator`, `TacticalBattleOrchestrator`, `EchelonOrchestrator`, lifecycle detection, roster.
- O1 army layer: `ArmyOrchestrator`, `ArmyIntent`, playbook catalog, `BattleMacroStrategyPatch` reading army macro intent.
- O2 intent loop: `TacticalIntentModel`, `EnemyVisibleState`, `ArmyIntentInference`, `ArmyTickCycle`, `ArmyEvidenceBuilder`, replan telemetry.
- Existing Slice B patches/scorers: #41/#42/#44/#45/#48/B7/B8 and support scorers.

Not implemented before O3:

- `CorpsOrchestrator`
- `CorpsIntent`
- `CorpsSectorAllocator`
- `CorpsIntentInference`
- `Enable Tactical Orchestrator Corps`
- Runtime corps discovery and hierarchy attachment

## Hierarchy Model

O3 must discover corps-like command nodes from confirmed vanilla hierarchy, not from a guessed field.

The production rule:

1. Use the active side's `ArmyOrchestrator` as the root authority.
2. Find the side's army battle root from vanilla `BattleUnits` / `AIBattle.unitsused` using alliance, command-level unit type, active hierarchy status, and the army side already attached by O1.
3. Use direct attached command children from `Regiment.GetAttachedUnitsReg(... directonly: true ...)` as O3 corps candidates.
4. A corps candidate must be same alliance, active, command-level (`unittyp > TacticalUnitType.MaxCombat`), and not the army root.
5. Record raw `unittyp`, name, stable id, and parent id for telemetry and tests.
6. If no clean command child exists, create a single `default-corps-{alliance}` that covers the army frontage.

This avoids the tactical/strategic naming ambiguity:

- tactical code currently names `14 = BattleGroupBrigade`, `15 = BattleGroupDivision`, `16 = BattleGroupArmy`;
- strategic formation code maps `14 = Division`, `15 = Corps`, `16 = Army`;
- vanilla uses `unittyp` as a hierarchy depth signal, but runtime command role must be inferred from hierarchy position, not the label alone.

## Corps Authority

`CorpsOrchestrator` is an `EchelonOrchestrator` child of `ArmyOrchestrator`.

It owns:

- current `CorpsIntent`;
- corps frontage summary;
- subordinate command role map;
- enemy corps/frontage intent model;
- command-level movement gate decision for #42;
- telemetry proving the active allocation.

It consumes:

- `ArmyIntent` from `ArmyOrchestrator.EmitArmyIntent()`;
- corps hierarchy snapshot;
- corps frontage sectors from existing tactical sector evidence;
- visible enemy state filtered to the corps frontage;
- commander personality from `TacticalCommanderRoster` with corps rank bias.

It emits:

- `CorpsIntent` for O4 Division consumption;
- command-group gate decisions consumed by #42;
- `[TacticalCascade]` and `[TacticalCorpsIntent]` proof lines.

## CorpsIntent Contract

O3 should define `CorpsIntent` as a pure testable contract, with no Unity or vanilla types.

Fields:

- `string CorpsId`
- `int AllianceId`
- `string ParentPlanId`
- `BattlePhase ParentPhase`
- `int PrimarySector`
- `CorpsRole CorpsRole`
- `CorpsAxis Axis`
- `float SupportPriority01`
- `float AggressionBias01`
- `TacticalIntentModel EnemyIntent`
- `IReadOnlyList<CorpsDivisionRole> DivisionRoles`

`CorpsRole` values:

- `Unknown`
- `Main`
- `SupportMain`
- `Fix`
- `Screen`
- `RefuseLeft`
- `RefuseRight`
- `Reserve`
- `Fallback`

`CorpsDivisionRole` fields:

- `string DivisionId`
- `string DisplayName`
- `int RawUnitType`
- `int PrimarySector`
- `CorpsRole Role`
- `float Weight01`

If a corps has no confirmed subordinate command nodes, `DivisionRoles` is empty and the `CorpsIntent.CorpsRole` remains authoritative for the whole corps. O4 can later decide how to map that whole-corps intent to division or group execution.

## Sector Allocation

`CorpsSectorAllocator` turns `ArmyIntent` plus corps frontage into role assignments.

Rules:

- The corps containing the army main-effort sector receives `Main`.
- Adjacent or materially overlapping corps receive `SupportMain` when they can help the main effort without abandoning their own frontage.
- Corps outside the main-effort axis receive `Fix` when they have enemy contact and enough strength to hold.
- Exposed flank corps receive `RefuseLeft` or `RefuseRight` using existing flank/refuse evidence.
- Low-contact, low-priority frontage receives `Screen`.
- A corps with substantial uncommitted subordinate strength and no urgent frontage receives `Reserve`.
- A corps with poor odds, low morale evidence, or enemy push intent in its frontage receives `Fallback` as an intent, not as an immediate retreat order.

Per-subordinate command role map:

- Assign roles only to confirmed direct subordinate command nodes.
- Prefer the subordinate closest to the main-effort sector for `Main`.
- Assign `SupportMain` to adjacent subordinate command nodes with useful strength/contact.
- Assign `Fix` to subordinate command nodes already in meaningful contact.
- Assign `Screen` to low-contact flank/outer nodes.
- Assign `Reserve` to uncommitted nodes not needed for frontage.
- Do not synthesize pseudo-divisions from brigade clusters.

## Corps Intent Inference

`CorpsIntentInference` reuses O2's `TacticalIntentModel` shape, filtered to the corps frontage.

Inputs:

- visible enemy strength by sector in this corps frontage;
- own strength by sector in this corps frontage;
- recent fire/contact flags;
- enemy reserve/command concentration if visible;
- local reinforcement signal if available;
- parent army intent as a bias, not an override.

Outputs:

- enemy `Attack`, `Defend`, `Withdraw`, `Probe`, `Refuse`, or `Unknown`;
- inferred enemy local main-effort sector;
- confidence;
- evidence tags.

Production rule: corps-level inference can bias O3 allocation and #42 command movement gating, but it must not trigger brigade/regiment writes directly.

## Runtime Behavior Surface

O3 must have production behavior impact through command-level authority.

Approved O3 behavior surface:

- `BattleFeudActionGatePatch` on `AIBattle.CheckForFeudGroupActions()` (`:4931`).

Reason:

- vanilla uses this method for command-group movement, not per-regiment stance/charge/fallback execution;
- vanilla already routes movement through `BattleUnits.SetWaypoint(... useorderdelay: true ...)` at `:4957`;
- this preserves order delay and the existing W&L/player-subordinate protections.

O3 should add a corps-authority gate:

- when a command group belongs to a corps with `CorpsRole.Main`, `SupportMain`, `Fix`, `Screen`, `RefuseLeft`, `RefuseRight`, `Reserve`, or `Fallback`, #42 checks whether vanilla's proposed movement is compatible with the current `CorpsIntent`;
- compatible movement is allowed through vanilla;
- incompatible movement is denied or downgraded before movement ownership, preserving existing #42 fallback behavior;
- if O3 has no corps intent for the group, #42 falls back to the existing behavior.

Examples:

- `Main` and `SupportMain`: allow movement toward the corps axis/main sector; deny random feud movement away from the assigned axis.
- `Fix`: allow short pressure/holding movement; deny wide lateral/retreating movement unless enemy pressure requires fallback.
- `Screen`: allow limited contact-maintaining movement; deny all-out advance.
- `Reserve`: deny random movement that commits the reserve unless parent army/corps intent changes.
- `Fallback`: allow withdrawal-compatible movement; deny advance.

Deferred behavior surfaces:

- #45 group stance is O5.
- #41 charge gate is O5.
- B8 fallback/retreat is O5.
- #48 reserve-list and `CheckUseOfReserves` behavior is O4.
- B7 artillery is O4.

## Player And W&L Safety

O3 must preserve the existing player-subordinate protections.

Rules:

- A corps must never direct-retask a regiment.
- A corps intent must flow to subordinate command nodes and later O4/O5 layers.
- W&L player-controlled and player-subordinate checks already used by #42 remain the final authority.
- If W&L ownership is unclear, O3 fails closed and lets existing guard behavior block or vanilla continue according to the current patch contract.
- No corps command movement may bypass `BattleUnits.SetWaypoint(... useorderdelay: true ...)`.

## Telemetry

Telemetry is proof of control, not the feature.

Required lines:

```text
[TacticalCascade] side=1 army=Army_of_Northern_Virginia corps=First_Corps role=Main sector=3 support=0.85 enemyIntent=Defend confidence=0.72
[TacticalCorpsIntent] side=1 corps=First_Corps parent=LeeEnvelopment phase=MainEffort role=Main axis=Sector3 divisions=divA:Main,divB:SupportMain,divC:Reserve
[TacticalCorpsGate] side=1 corps=First_Corps group=First_Corps role=Reserve action=deny reason=reserve-not-committed surface=CheckForFeudGroupActions
[TacticalCommanderUnknown] echelon=corps name=Jubal_Early
```

Telemetry must include raw `unittyp` in verbose or diagnostic output when hierarchy discovery is ambiguous.

## Config

Add:

- `Enable Tactical Orchestrator Corps`

Default:

- true only after O2 smoke is clean and O3 is ready for focused smoke.

Behavior:

- if master orchestrator is disabled, O3 is disabled;
- if army orchestrator is disabled or has no plan, O3 is inert;
- if corps flag is disabled, #42 falls back to current behavior and no corps gate decisions are applied.

## Tests

Pure harness coverage must include:

- corps intent records parent plan, role, sector, aggression, support priority, and enemy intent;
- corps intent sanitizes NaN/infinite values;
- hierarchy discovery maps army direct children to corps candidates;
- hierarchy discovery creates `default-corps` when no clean command children exist;
- hierarchy discovery does not depend on `parentcorps`;
- subordinate role map uses confirmed direct command children only;
- subordinate role map does not synthesize pseudo-divisions;
- main/support-main allocation can assign both roles in the same parent plan;
- reserve role blocks random command movement through the O3 command gate;
- main/support-main allows compatible axis movement;
- fallback role allows withdrawal-compatible command movement;
- W&L/player-subordinate unsafe cases fail closed;
- `TacticalBattleOrchestrator.AttachCorps` is idempotent and attaches corps as children of army;
- corps tick propagates after army tick without requiring O4/O5.

Runtime smoke expectations:

- fresh log after O3 deploy includes `[TacticalCascade]` and `[TacticalCorpsIntent]` for both active AI sides when hierarchy exists;
- hierarchy ambiguity logs raw command names/unit types and uses `default-corps` instead of throwing;
- #42 command-gate logs `[TacticalCorpsGate]` at least once in a battle with command movement pressure;
- no repeated exceptions;
- no Harmony missing-anchor warnings for #42/O3;
- no `parentcorps` warning or lookup;
- no direct regiment retask from corps;
- no player-subordinate bypass.

## Defer Boundaries

Deferred to O4:

- reserve commitment timing;
- reserve-list mutation ownership;
- artillery prioritization;
- division intent cascade.

Deferred to O5:

- group stance writes;
- charge initiation/denial;
- fallback/retreat writes;
- brigade execution.

Deferred to O6:

- player-subordinate `DLC_WL.givenorder` integration.

Deferred to O7:

- cleanup of legacy Slice B decision ownership once orchestrator layers replace it.

## Not Verified

The following claims require runtime smoke and must stay marked unverified until logs prove them:

- which battle scenarios expose clean army direct children versus requiring `default-corps`;
- whether vanilla battle `unittyp` labels are stable across all W&L scenarios;
- how often `CheckForFeudGroupActions()` proposes command movement after O3 deploy;
- whether a single battle session produces enough command movement pressure to see all role gates;
- whether every named corps commander appears in `GameVars.commander` with enough identity to match `HistoricalFigureRegistry`.

## Acceptance

O3 is accepted when:

- harness passes with O3 tests;
- `./build.sh` passes;
- deployed DLL hash matches local `dist/WhiskeyRealism.dll`;
- fresh runtime smoke shows corps discovery and corps intents for both AI sides or explicit `default-corps` fallback;
- #42 command-level gate is active and bounded;
- no direct regiment/brigade behavior writes originate from O3;
- no repeated exceptions or missing vanilla anchor warnings appear;
- docs/handoff and docs/patch-catalog reflect the shipped O3 state.
