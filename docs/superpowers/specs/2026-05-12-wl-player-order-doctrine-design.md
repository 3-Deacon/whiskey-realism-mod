# W&L Player Order Doctrine Design

Status: tactical #62 implemented/hash-deployed behind default-off
`Enable Player Order Doctrine`; focused enabled smoke pending before archive.
Current runtime truth lives in
[`docs/tactical-operations-ledger.md`](../../tactical-operations-ledger.md) and
[`docs/patch-catalog.md`](../../patch-catalog.md). This spec is no longer the
implementation source of truth.
Date: 2026-05-12.
Owner: Whiskey Realism W&L / tactical command workstream.

## Goal

Replace vanilla's incidental AI-to-player W&L order behavior with one broad Whiskey order-doctrine slice that decides, prioritizes, deduplicates, and explains orders to the player while still delivering them through vanilla `AIBattle.CheckCurrentOrderUpdate(...)`.

The slice should make player-subordinate orders feel like they come from the superior commander's actual operational intent, not from whichever vanilla tactical or campaign subsystem happened to fire first.

## User Problem

Vanilla already has a usable W&L current-order UI: `DLC_WL.givenorder`, the career order popup, and the map zone marker. The problem is the decision layer feeding it:

- Campaign calls can replace current orders without the same duplicate suppression vanilla applies to battle calls.
- Tactical orders are trigger-driven and scattered across objective, reserve, retreat, assault, and cleanup helpers.
- There is one active `DLC_WL.givenorder`, so weak movement updates can overwrite more urgent intent.
- The order text and zone are only as good as the type, destination name, position, rotation, width, and depth passed into vanilla.
- Silent guard returns make it hard to know whether Whiskey tried to issue an order and vanilla rejected it.

## Scope

This is one broad implementation slice, not multiple staggered fixes.

It may add one net-new Harmony patch:

- `PlayerSubordinateOrderPatch`, Prefix/Postfix snapshot pair on `AIBattle.UpdateDLCPlayerOrders()`.

It may also add pure helpers and extend existing runtime helpers:

- `PlayerOrderIntent`
- `PlayerOrderComposer`
- `PlayerOrderPriority`
- `PlayerOrderDedupe`
- `PlayerOrderVanillaMapper`
- `PlayerOrderDiagnostics`
- `WlStrategicOrderBridge` campaign dedupe/cooldown support

It must not add broad patches on `AICampaign.MoveUnitTo(...)`, `BattleUnits.SetWaypoint(...)`, or `AIBattle.CheckCurrentOrderUpdate(...)`.

## Anchor Verification Status

This spec was verified against:

- vanilla decompile: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`;
- current Whiskey patch catalog and handoff;
- current `WlStrategicOrderBridge`;
- current tactical orchestrator surfaces;
- current `Plugin.cs` config naming and `TacticalCommanderMode` semantics.

The 2026-05-12 verification pass included independent read-only reviews of vanilla decompile anchors and mod/repo anchors, then a local integration pass before this spec was corrected.

This spec keeps the original proposed design separate from shipped behavior. `PlayerSubordinateOrderPatch`, `PlayerOrderIntent`, `PlayerOrderComposer`, `PlayerOrderPriority`, `PlayerOrderDedupe`, `PlayerOrderVanillaMapper`, and `PlayerOrderDiagnostics` are now shipped on `main`; current runtime truth lives in the living docs above.

## Confirmed Vanilla Anchors

Primary source: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

### Order Delivery

`AIBattle.CheckCurrentOrderUpdate(Regiment unit, int type, Vector3 position, string destinationname, float rotation, float width, float depth, bool calledfromcampaign = false)` at line 8233 is the central W&L player-order bridge.

Confirmed behavior:

- rejects when W&L is inactive, chosen commander/current command is missing, EOD is running, unit is null, unit has no parent, or unit is not player-alliance: lines 8251-8261;
- rejects battle calls for commanding-officer ownership and invalid player-chain relationships: lines 8327-8365;
- rejects campaign calls when the player is CIC or the current-command chain is not the narrow W&L subordinate chain: lines 8366-8368;
- builds order headline/content from order type and call arguments: lines 8430-8637;
- applies duplicate suppression only when `!calledfromcampaign`: lines 8640-8646;
- writes `DLC_WL.givenorder`, increments `DLC_WL.GivenOrders.givenorderssession`, and calls `CareerInformationPanel.ShowNewOrder(...)`: lines 8648-8664.

### Tactical Player Order Coordinator

`AIBattle.UpdateDLCPlayerOrders()` at line 6747 is the tactical W&L player-order coordinator.

Confirmed behavior:

- runs only when W&L is active;
- rotates through `allgroupsassigned` via `reserveordercheck`;
- calls `CheckRemovalOfOrders(...)` and `UpdateReserveOrder(...)`;
- helper calls can issue current-order types 15, 13, 14, and 11 through `CheckCurrentOrderUpdate(...)`: lines 6777-6841.

### Tactical Order Attempts

Known non-coordinator tactical/current-order call sites include:

- assault move: type 4 at line 3646;
- march to sound of guns: type 2 at line 3766;
- macro defensive-order path when the player's alliance enters defensive macro state: type 12 at line 6463;
- objective movement / hold: types 0 and 12 at lines 6998-7002;
- isolated-target assault: type 3 at line 8210;
- contact/triggered movement after waypoint/contact/rotation handling: type 1 at line 117383;
- recursive support request inside `CheckCurrentOrderUpdate(...)` itself: type 11 at lines 8333-8335 when a battle call targets a subordinate linked under the parent/current campaign group.

### Campaign Order Attempts

Campaign AI can call `CheckCurrentOrderUpdate(... calledfromcampaign:true)` when vanilla would otherwise directly move an AI unit:

- fort construction: type 9 at line 9674;
- capital-defense movement: type 8 at lines 11860-11864;
- defensive response / engage enemy: type 7 at lines 13715 and 13775;
- offensive continuation: type 6 at line 14052;
- offensive movement: type 5 or 16 at lines 14451 and 14455;
- supply depot construction: type 10 at line 14775;
- debug key-input paths for types 5, 6, 7, 8, 9, and 10 also exist at lines 189552-189580, but production implementation should use real AI runtime callers, not debug input code.

Nuance for campaign callers: vanilla often skips direct `MoveUnitTo(...)` for W&L-under-commander units and uses `CheckCurrentOrderUpdate(... calledfromcampaign:true)` instead, but some production campaign paths still mutate operation lists around the W&L call. Whiskey must avoid inappropriate operation-list mutation when the bridge rejects, fails, or suppresses a current-order call.

### Existing Whiskey Boundaries

- `docs/patch-catalog.md` records `AIBattle.CheckCurrentOrderUpdate` as W&L-only current-order/message machinery, not regular campaign movement.
- `WlStrategicOrderBridge` is the existing central campaign W&L bridge. Current source fails closed for null requests, player-CIC, player-controlled, player-unit, ineligible chain, and failed vanilla-bridge cases. It calls `AIBattle.CheckCurrentOrderUpdate(... calledfromcampaign:true)` only through `TryIssue(...)`, and detects bridge failure by comparing `givenorderssession`/`givenorder` before and after the vanilla call. It does not yet have campaign signature cache, material-change detection, refresh cooldown, active-order priority comparison, or full suppressed/issued diagnostics.
- `TacticalObserverPatch` already observes `AIBattle.CheckCurrentOrderUpdate` and can emit `[TacticalCurrentOrder]` / `[TacticalPlayerOrder]` telemetry when enabled. It is read-only telemetry, not the doctrine owner.
- The tactical orchestrator workstream already identified `PlayerSubordinateOrderPatch` as the intended O6 player-order surface. That patch is planned, not shipped. The original O6 surface was described as Postfix-only, but this spec requires a Prefix/Postfix snapshot pair on the same method so Whiskey can detect vanilla self-issued orders before deciding whether to compose or write.
- `ArmyOrchestrator.ResolveCommandIntentForGroup(...)` currently returns `CommandIntentResolution` with `Found`, `Intent`, and `Reason`. The consumed `CommandNodeIntent` fields are `NodeId`, `SourceNodeId`, `Role`, `Axis`, `PrimarySector`, `SupportPriority`, `AggressionBias01`, and `Depth`. `PlayerOrderComposer` may consume those fields only; changing that contract requires an implementation-plan update.

## Doctrine Design

### One Broad Owner

Whiskey adds one order-doctrine owner for player-visible W&L orders.

The owner consumes tactical and campaign intent, picks the highest-value current player order, maps it to vanilla order arguments, and calls `CheckCurrentOrderUpdate(...)` only when the order should be visible to the player.

This owner does not move units. It only requests vanilla to display or update `DLC_WL.givenorder`.

### Tactical Flow

`PlayerSubordinateOrderPatch` wraps vanilla `AIBattle.UpdateDLCPlayerOrders()` with a Prefix/Postfix pair.

The Prefix:

1. exits when W&L is inactive, player is CIC, current command is missing, battle is ending, tactical commander mode is `Off`, or both the doctrine valve and diagnostics valve are disabled;
2. snapshots `DLC_WL.givenorder`, `DLC_WL.GivenOrders.givenorderssession`, current battle identity, and a coarse tick/cycle key before vanilla runs.

The Postfix:

1. compares the Prefix snapshot to the active `DLC_WL.givenorder` / session after vanilla runs;
2. records vanilla provenance when the session/order changed and no Whiskey call happened in the snapshot window;
3. composes read-only diagnostics in `MonitorOnly` only when bounded diagnostics are enabled, but issues/replaces visible orders only when the new doctrine valve is enabled and `TacticalCommanderModePolicy.AllowsWrites(mode)` is true;
4. resolves the player's current command and superior command state through current orchestrator accessors: `TacticalBattleCoordinator.GetSideOrchestrator(allianceId)`, `TacticalBattleOrchestrator.Army`, `ArmyOrchestrator.ResolveCommandIntentForGroup(...)`, `ArmyOrchestrator.CurrentDirectChildIntents`, `ArmyOrchestrator.CurrentCommandOperations`, `ArmyOrchestrator.CurrentDoctrineOrders`, `ArmyOrchestrator.CurrentOperation`, and `ArmyOrchestrator.CurrentStrategicBattleIntent`;
5. asks `PlayerOrderComposer` for the best order intent from operations-ledger state, command assignments, visible battle picture, fallback/reserve state, and current vanilla objective data;
6. maps the selected intent to vanilla `CheckCurrentOrderUpdate(...)` arguments;
7. runs provenance, dedupe/cooldown, cross-scope, priority, and vanilla-dedupe preflight checks against the active `DLC_WL.givenorder`;
8. calls vanilla `CheckCurrentOrderUpdate(...)` if the candidate should replace or refresh the order and vanilla's own tactical dedupe is expected to allow it;
9. emits bounded `[PlayerOrderIntent]` diagnostics for issue, skip, suppress, and replace decisions.

The patch does not replace vanilla's own type 11/13/14/15 helper behavior. It yields to vanilla self-issued orders before applying orchestrator candidates:

- active type 15 blocks all non-campaign tactical candidates under vanilla dedupe, so Whiskey must yield until vanilla clears it or a campaign-scope bridge call is explicitly selected by the campaign flow;
- active type 13 blocks all non-13 tactical candidates under vanilla dedupe, so Whiskey treats it as a hard vanilla transition lock rather than trying to overwrite it;
- vanilla type 14 is not broadly sticky, so Whiskey must explicitly yield for at least one eligible `UpdateDLCPlayerOrders()` cycle after detecting a vanilla self-issued type 14;
- vanilla type 11 support requests should be preserved unless Whiskey has a material higher-priority candidate that passes the same vanilla-dedupe preflight.

### Campaign Flow

Campaign order improvements live inside `WlStrategicOrderBridge`, not as a new global campaign patch.

Existing strategic runtime conversions route W&L player-chain safety through `WlStrategicOrderBridge`: `TryIssue(...)` where they intend to issue a visible current order, and `ClassifyOnly(...)` where they only need movement/list-mutation gating.

The implementation should add:

- a per-unit campaign order signature cache;
- material-change detection on order type, target bucket, intent, source, and destination name;
- a minimum refresh interval for unchanged campaign orders;
- priority comparison against the active `DLC_WL.givenorder`;
- bounded diagnostics for rejected, suppressed, issued, and failed bridge calls.

The bridge must not direct-move player-chain units or mutate operation-list membership when vanilla rejects, fails, or dedupe suppresses the current-order call.

Before campaign signature-cache implementation lands, the implementation plan must enumerate every current Whiskey strategic caller that touches player-chain movement/order safety, including the shipped `TryIssue(...)` and `ClassifyOnly(...)` users, and either confirm it already routes through `WlStrategicOrderBridge` or schedule that conversion first.

## Priority Model

`PlayerOrderPriority` ranks player-visible orders before a vanilla call is attempted.

Priority is a preflight request model, not a force-write model. The tactical path still calls vanilla `CheckCurrentOrderUpdate(...)` with `calledfromcampaign:false`, so the final write is constrained by vanilla's tactical duplicate-suppression branch at decompile lines 8640-8646. The implementation must log a bounded `reason=vanilla-dedupe-predicted` skip when a higher Whiskey priority cannot be written through vanilla's own branch.

| Priority | Intent | Vanilla type candidates |
|---:|---|---|
| 100 | Retreat / leave field / emergency withdrawal | 15 |
| 90 | Fall back to line / refuse threatened flank / avoid encirclement | 12, 14, 7 |
| 80 | Defend immediate objective or engage immediate enemy | 7, 12 |
| 70 | Support main effort / reserve support request | 11 |
| 60 | Attack exposed objective / offensive commitment | 0, 4, 16 |
| 50 | Probe / move to objective / continue operation / ordered contact movement | 1, 2, 3, 5, 6 |
| 30 | Construct fort, defend capital, or build supply depot | 8, 9, 10 |
| 20 | Status or cancellation | 13, 14 |

Rules:

- A lower-priority candidate must not overwrite a higher-priority active order unless the active order is stale or no longer valid.
- Equal-priority candidates replace only when the target bucket, order type, or destination name materially changes.
- A higher-priority candidate may replace only if provenance, scope, cooldown, and vanilla-dedupe preflight allow it.
- Type 15 is exclusively `RetreatToExit` / leave-field intent in Whiskey doctrine. Generic fallback uses type 12 or 14; it must not map to type 15 unless the mapper has a valid retreat/exit destination.
- Campaign calls use the same priority model but with longer cooldowns than tactical calls and scope protection.

### Vanilla Dedupe Preflight

Before a tactical Whiskey call, `PlayerOrderDedupe` must predict vanilla's non-campaign duplicate branch from lines 8640-8646:

| Active `DLC_WL.givenorder` | Candidate | Expected vanilla result | Whiskey rule |
|---|---|---|---|
| same type | same type, except type 11 refresh nuance | blocked for most repeated orders | suppress unless material refresh is type 11 and angle/distance rules allow |
| any type | type 11 same nearby support zone and same facing | blocked | suppress as duplicate support |
| 0, 1, 2, 3, 4, 5, 13 | type 12 | blocked | do not claim HoldObjective replaced attack/probe/movement/status |
| 13 | any non-13 | blocked | treat as vanilla transition lock |
| not 12 | type 14 | blocked | only type 12 can transition to type 14 through vanilla |
| 0, 1, 2, 3, 4 while current-command campaign-group flag is true | type 2 | blocked | suppress march/stop-style candidate |
| 15 | any tactical candidate | blocked | yield until retreat order clears |

This spec intentionally does not use `calledfromcampaign:true` from the tactical Postfix as a dedupe bypass. That flag enters the campaign-chain guard at lines 8366-8368 and has different semantics.

### Cross-Scope Replacement

`DLC_WL.givenorder` is one global slot, so scope is part of priority.

- A tactical candidate may not replace an active campaign-scope Whiskey order unless tactical priority is at least 40 points higher and the composer proves the campaign order is no longer immediately actionable because the command is now in battle, under immediate threat, or outside the campaign order's target bucket.
- Emergency tactical retreat priority 100 may replace any campaign-scope order when a valid retreat/exit destination exists.
- A campaign candidate may not replace an active tactical-scope Whiskey order unless the tactical order is stale, the battle/order context has ended, or campaign priority is at least 40 points higher.
- Vanilla-issued orders without Whiskey provenance are classified best-effort from type and scope evidence. If classification is ambiguous, Whiskey must prefer suppress/yield over replacement.

## Vanilla Mapping

`PlayerOrderVanillaMapper` maps Whiskey intent to vanilla arguments:

| Whiskey intent | Type | Position | Destination | Rotation | Zone |
|---|---:|---|---|---:|---|
| HoldObjective | 12 | objective or current defensive anchor | objective name | enemy-facing or current | command width/depth |
| AttackObjective | 0 or 16 | objective or visible enemy-line anchor | objective/enemy name | enemy-facing | command width/depth |
| MovementRedirect | 1 | ordered-contact or waypoint-derived target | objective/enemy/area name | waypoint rotation | command width/depth |
| Probe | 5 or 6 | bounded probe target | objective/area name | -1 | 20x20 unless command zone is known |
| SupportMainEffort | 11 | supported command target or last waypoint | supported command name | supported command facing | 100x50 default |
| FallBackToLine | 12 or 14 | fallback line / visible-enemy withdrawal point | fallback objective name | enemy-facing | command width/depth |
| RetreatToExit | 15 | nearest valid retreat entry point from `BattleUnits.SearchForClosestEntryPoint(...)` | empty | -1 | 50x50 |
| RefuseFlank | 12 | flank anchor | left/right flank label or objective | flank-facing | command width/depth |
| EngageEnemy | 7 | visible enemy anchor | enemy unit name | -1 | enemy width/depth |
| DefendCapital | 8 | capital/town anchor | town/capital name | -1 | 20x20 |
| BuildFort | 9 | unit/current construction anchor | Fort | -1 | 20x20 |
| BuildSupplyDepot | 10 | unit/current construction anchor | Supply Depot | -1 | 20x20 |
| CancelOrNoLongerValid | 13 or 14 | current command position | empty | 0 | no zone |

Argument quality is part of the feature. The mapper should prefer real objective names, visible enemy names, and command-width/depth zones over generic `"Objective"` when safe. Public compile-visible vanilla fields may be read directly behind null checks; private, fragile, or version-sensitive reads must go through small wrappers that catch exceptions, emit bounded warnings, and downgrade to conservative defaults rather than throwing from a patch.

`RetreatToExit` must mirror vanilla's type-15 destination shape: vanilla `CheckRemovalOfOrders(...)` derives the displayed retreat point from `bunits.SearchForClosestEntryPoint(currentcommand.unitrange.flankposition[2], alliance, ignoreneutral:false, retreatangle, 30f)` before calling type 15. Whiskey must not use type 15 for a generic fallback line.

## Dedupe And Cooldown

`PlayerOrderDedupe` compares candidates against:

- last Whiskey-issued tactical order;
- last Whiskey-issued campaign order;
- active `DLC_WL.givenorder` when readable;
- vanilla `givenorderssession` before/after a call;
- vanilla self-issued order detected by the `UpdateDLCPlayerOrders()` Prefix/Postfix snapshot.

Material fields:

- scope: tactical or campaign;
- unit instance id;
- source system;
- intent;
- vanilla order type;
- x/z target bucket;
- destination name;
- priority bucket;
- provenance: Whiskey tactical, Whiskey campaign, vanilla tactical, vanilla campaign, or unknown.

Defaults for implementation plan:

- tactical compose/issue throttle: at most one write attempt per player-command unit per eligible `UpdateDLCPlayerOrders()` cycle, plus a minimum 10 in-game minutes for unchanged candidates;
- campaign unchanged refresh floor: 1 in-game day;
- target bucket: 50m tactical, 500m campaign;
- immediate replacement allowed for priority increase of 20 or more;
- emergency retreat/fallback can bypass refresh floor.

Exact constants may move to config if runtime smoke shows churn.

Whiskey must keep a shadow provenance record for its own successful calls: scope, unit instance id, order type, intent, target bucket, priority, destination, vanilla session before/after, and battle/campaign context key. If `givenorderssession` advances or `DLC_WL.givenorder` changes without a matching Whiskey call, the active order is treated as vanilla-provenance and any stale Whiskey shadow entry for that unit/context is invalidated. Active-order priority inference from vanilla-provenance `givenorder.type` is best-effort only; ambiguous many-to-one types such as 7 and 12 must bias toward suppress/yield unless the new candidate is emergency priority.

Static tactical caches must be cleared on battle end, battle identity change, player current-command change, and player CIC transition. Campaign caches must be keyed by campaign/save context plus unit identity and cleared when campaign context changes. No cache state may persist to saves for this slice.

## Diagnostics

Add bounded doctrine logs. These are distinct from existing read-only `TacticalObserverPatch` telemetry such as `[TacticalCurrentOrder]` and `[TacticalPlayerOrder]`.

```text
[PlayerOrderIntent] action=issue scope=tactical type=12 intent=HoldObjective priority=80 unit=... target=... reason=...
[PlayerOrderIntent] action=replace oldType=5 newType=15 oldPriority=50 newPriority=100 reason=priority
[PlayerOrderIntent] action=suppress scope=campaign type=5 reason=duplicate-cooldown
[PlayerOrderIntent] action=skip scope=tactical reason=player-cic
[PlayerOrderIntent] action=skip scope=campaign reason=vanilla-bridge-failed
```

Logging must be capped by signature and should not emit every frame for the same unchanged decision.

## Config

Add one broad feature valve:

```text
[W&L]
Enable Player Order Doctrine = false
```

Default off until smoke proves:

- no repeated exceptions;
- no order-popup spam;
- no direct movement of player-chain units;
- no player-CIC orders;
- tactical and campaign dedupe work;
- urgent valid orders replace weak orders when provenance, scope, cooldown, and vanilla-dedupe preflight allow; otherwise bounded skip diagnostics explain why they did not.

Optional diagnostics:

```text
[W&L]
Enable Player Order Doctrine Diagnostics = true
```

Diagnostics may be enabled independently only if they are read-only and bounded. With the main doctrine valve off, diagnostics may compose and classify a candidate but must not call `CheckCurrentOrderUpdate(...)`; otherwise the patch exits before write evaluation.

These config keys are new proposed keys. They are independent of the existing `Tactical Orchestrator` / `Tactical Commander Mode` key, whose current default is `Active`; the new player-order doctrine remains default-off even when tactical commander mode is Active.

## Safety Boundaries

Do not:

- patch or replace `AIBattle.CheckCurrentOrderUpdate(...)`;
- patch broad `AICampaign.MoveUnitTo(...)`;
- patch broad `BattleUnits.SetWaypoint(...)`;
- mutate `DLC_WL.givenorder` directly except through vanilla `CheckCurrentOrderUpdate(...)`;
- issue direct movement to player-chain units when the bridge rejects or dedupe suppresses an order;
- mutate operation-list membership for a player-chain unit when the bridge rejects, fails, or dedupe suppresses an order;
- rewrite vanilla tooltip content after `CareerInformationPanel.ShowNewOrder(...)`;
- rely on omniscient enemy locations for order targets when visible evidence is required;
- change AI-to-AI command behavior as part of this slice.

## Testing

Pure harness coverage must come before runtime patches:

- priority ordering: retreat beats objective, fallback beats support, support beats movement;
- equal-priority material-change rules;
- tactical dedupe suppresses unchanged repeat inside cooldown;
- campaign dedupe suppresses unchanged repeat inside longer cooldown;
- emergency priority bypasses cooldown;
- vanilla mapping for each supported `PlayerOrderIntent`;
- vanilla dedupe preflight table, including sticky type 13, type 14 only after type 12, and type 15 blocking all tactical candidates;
- provenance invalidation when `givenorderssession` advances through a vanilla path;
- cross-scope replacement gap and campaign-validity checks;
- type 15 mapper uses a valid retreat entry point and generic fallback does not use type 15;
- cache clearing on battle identity/current-command/CIC transitions;
- bridge failure remains fail-closed and does not allow direct movement;
- lower-priority campaign order cannot replace higher-priority tactical order;
- stale active order can be replaced by equal-priority material change.

Runtime smoke:

- W&L battle with player as subordinate, not CIC;
- `[W&L] Enable Player Order Doctrine = true`;
- confirm bounded `[PlayerOrderIntent]` rows;
- confirm `DLC_WL.givenorder` updates only when intent changes, priority demands, and vanilla-dedupe preflight allows;
- confirm blocked high-priority candidates log bounded suppress/skip reasons instead of silently implying success;
- confirm vanilla popup/zone still appears through `CheckCurrentOrderUpdate(...)`;
- confirm no player-subordinate movement is created by Whiskey;
- confirm no repeated `Exception`, `ERROR`, `missing-anchor`, or Harmony failure.

Campaign smoke:

- W&L campaign with player-chain command;
- trigger or wait for operational probe/offensive/defensive opportunity;
- confirm campaign duplicate suppression for unchanged orders;
- confirm failed/ineligible bridge calls log skip and do not direct-move.

## Rollback

Set `[W&L] Enable Player Order Doctrine = false`.

When disabled:

- `PlayerSubordinateOrderPatch` returns before issuing; if diagnostics are also disabled, it returns before composing;
- `WlStrategicOrderBridge` keeps existing safety behavior and may still emit its pre-existing diagnostics;
- vanilla `UpdateDLCPlayerOrders()` and vanilla campaign current-order calls continue unchanged;
- no new state should persist into saves.

## Not Verified

- Whether current tactical completion smoke has already produced enough player-subordinate context for a meaningful #62/O6 test. Current living docs still mark fresh Active smoke pending.
- The final campaign-caller audit outcome. The implementation plan must enumerate each current Whiskey strategic caller before campaign signature-cache work.
- Exact real-world frequency of `microaitaskupdatecycle == 28` per battle minute. Vanilla cadence anchor is confirmed at lines 5682-5684, but runtime frequency needs a log sample.
- Whether `ArmyOrchestrator.CurrentStrategicBattleIntent`, `CurrentOperation`, and `CurrentDoctrineOrders` are populated before the first eligible `UpdateDLCPlayerOrders()` cycle in a fresh battle.
- Player command-chain transition behavior if the player becomes CIC or changes current command mid-battle.
- Final cooldown values. The values above are design defaults for tests and first smoke, not historical claims.

## Open Implementation Questions

- Whether `[W&L] Enable Player Order Doctrine Diagnostics` should default on when the main valve is off. Recommended: yes, because the updated flow allows read-only composition/classification with writes disabled.
- Whether campaign and tactical caches should live in one shared static helper or separate scope-specific helpers. Recommended: one helper with explicit scope in the key to keep priority comparison centralized.
- Whether exact target buckets should be configurable after smoke. Recommended: keep constants internal until log evidence shows churn.
- Whether `PlayerOrderDiagnostics` remains a separate helper. Recommended: keep it only if it owns signature caps, provenance labels, and dedupe skip reasons; otherwise fold logging into composer/dedupe.
