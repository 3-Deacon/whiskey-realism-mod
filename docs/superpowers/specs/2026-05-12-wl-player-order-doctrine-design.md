# W&L Player Order Doctrine Design

Status: active design spec; implementation plan not written.
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

- `PlayerSubordinateOrderPatch`, Postfix on `AIBattle.UpdateDLCPlayerOrders()`.

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

Confirmed-current mod behavior is kept separate from proposed implementation below. `PlayerSubordinateOrderPatch`, `PlayerOrderIntent`, `PlayerOrderComposer`, `PlayerOrderPriority`, `PlayerOrderDedupe`, `PlayerOrderVanillaMapper`, and `PlayerOrderDiagnostics` are proposed surfaces, not shipped source.

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
- The tactical orchestrator workstream already identified `PlayerSubordinateOrderPatch` as the intended O6 player-order surface. That patch is planned, not shipped.

## Doctrine Design

### One Broad Owner

Whiskey adds one order-doctrine owner for player-visible W&L orders.

The owner consumes tactical and campaign intent, picks the highest-value current player order, maps it to vanilla order arguments, and calls `CheckCurrentOrderUpdate(...)` only when the order should be visible to the player.

This owner does not move units. It only requests vanilla to display or update `DLC_WL.givenorder`.

### Tactical Flow

`PlayerSubordinateOrderPatch` runs after vanilla `AIBattle.UpdateDLCPlayerOrders()`.

The Postfix:

1. exits when W&L is inactive, player is CIC, current command is missing, battle is ending, the new doctrine valve is disabled, or tactical commander mode is `Off`;
2. composes read-only diagnostics in `MonitorOnly` only when bounded diagnostics are enabled, but issues/replaces visible orders only when `TacticalCommanderModePolicy.AllowsWrites(mode)` is true;
3. resolves the player's current command and superior command state through current orchestrator accessors: `TacticalBattleCoordinator.GetSideOrchestrator(allianceId)`, `TacticalBattleOrchestrator.Army`, `ArmyOrchestrator.ResolveCommandIntentForGroup(...)`, `ArmyOrchestrator.CurrentDirectChildIntents`, `ArmyOrchestrator.CurrentCommandOperations`, `ArmyOrchestrator.CurrentDoctrineOrders`, `ArmyOrchestrator.CurrentOperation`, and `ArmyOrchestrator.CurrentStrategicBattleIntent`;
4. asks `PlayerOrderComposer` for the best order intent from operations-ledger state, command assignments, visible battle picture, fallback/reserve state, and current vanilla objective data;
5. maps the selected intent to vanilla `CheckCurrentOrderUpdate(...)` arguments;
6. runs dedupe/cooldown/priority checks against the active `DLC_WL.givenorder`;
7. calls vanilla `CheckCurrentOrderUpdate(...)` if the candidate should replace or refresh the order;
8. emits bounded `[PlayerOrderIntent]` diagnostics for issue, skip, suppress, and replace decisions.

The Postfix does not replace vanilla's own type 11/13/14/15 helper behavior. It runs after vanilla and may leave the vanilla order untouched if vanilla already selected an equal-or-higher priority order.

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

## Priority Model

`PlayerOrderPriority` ranks player-visible orders before a vanilla call is attempted.

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
- A higher-priority candidate may replace immediately.
- Type 15 suppresses all lower-priority candidates while active unless the candidate is a confirmed cancellation/recovery order.
- Campaign calls use the same priority model but with longer cooldowns than tactical calls.

## Vanilla Mapping

`PlayerOrderVanillaMapper` maps Whiskey intent to vanilla arguments:

| Whiskey intent | Type | Position | Destination | Rotation | Zone |
|---|---:|---|---|---:|---|
| HoldObjective | 12 | objective or current defensive anchor | objective name | enemy-facing or current | command width/depth |
| AttackObjective | 0 or 16 | objective or visible enemy-line anchor | objective/enemy name | enemy-facing | command width/depth |
| MovementRedirect | 1 | ordered-contact or waypoint-derived target | objective/enemy/area name | waypoint rotation | command width/depth |
| Probe | 5 or 6 | bounded probe target | objective/area name | -1 | 20x20 unless command zone is known |
| SupportMainEffort | 11 | supported command target or last waypoint | supported command name | supported command facing | 100x50 default |
| FallBackToLine | 12 or 15 | fallback line / visible-enemy withdrawal point | fallback objective name | enemy-facing | command width/depth |
| RefuseFlank | 12 | flank anchor | left/right flank label or objective | flank-facing | command width/depth |
| EngageEnemy | 7 | visible enemy anchor | enemy unit name | -1 | enemy width/depth |
| DefendCapital | 8 | capital/town anchor | town/capital name | -1 | 20x20 |
| BuildFort | 9 | unit/current construction anchor | Fort | -1 | 20x20 |
| BuildSupplyDepot | 10 | unit/current construction anchor | Supply Depot | -1 | 20x20 |
| CancelOrNoLongerValid | 13 or 14 | current command position | empty | 0 | no zone |

Argument quality is part of the feature. The mapper should prefer real objective names, visible enemy names, and command-width/depth zones over generic `"Objective"` when safe.

## Dedupe And Cooldown

`PlayerOrderDedupe` compares candidates against:

- last Whiskey-issued tactical order;
- last Whiskey-issued campaign order;
- active `DLC_WL.givenorder` when readable;
- vanilla `givenorderssession` before/after a call.

Material fields:

- scope: tactical or campaign;
- unit instance id;
- source system;
- intent;
- vanilla order type;
- x/z target bucket;
- destination name;
- priority bucket.

Defaults for implementation plan:

- tactical unchanged refresh floor: 10 in-game minutes;
- campaign unchanged refresh floor: 1 in-game day;
- target bucket: 50m tactical, 500m campaign;
- immediate replacement allowed for priority increase of 20 or more;
- emergency retreat/fallback can bypass refresh floor.

Exact constants may move to config if runtime smoke shows churn.

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
- urgent orders replace weak orders.

Optional diagnostics:

```text
[W&L]
Enable Player Order Doctrine Diagnostics = true
```

Diagnostics may be enabled independently only if they are read-only and bounded.

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
- bridge failure remains fail-closed and does not allow direct movement;
- lower-priority campaign order cannot replace higher-priority tactical order;
- stale active order can be replaced by equal-priority material change.

Runtime smoke:

- W&L battle with player as subordinate, not CIC;
- `[W&L] Enable Player Order Doctrine = true`;
- confirm bounded `[PlayerOrderIntent]` rows;
- confirm `DLC_WL.givenorder` updates only when intent changes or priority demands;
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

- `PlayerSubordinateOrderPatch` returns before composing or issuing;
- `WlStrategicOrderBridge` keeps existing safety behavior and may still emit its pre-existing diagnostics;
- vanilla `UpdateDLCPlayerOrders()` and vanilla campaign current-order calls continue unchanged;
- no new state should persist into saves.

## Not Verified

- Exact runtime frequency of tactical order churn after doctrine composition. Vanilla cadence is confirmed, but real popup rate needs smoke.
- Whether current full-spectrum tactical command smoke has already produced enough player-subordinate context for a meaningful O6 test. Current living docs still mark fresh Active smoke pending.
- Whether all campaign sources that should route through `WlStrategicOrderBridge` already do. The implementation plan must audit each current Whiskey strategic caller before coding.
- Final cooldown values. The values above are design defaults for tests and first smoke, not historical claims.

## Open Implementation Questions

- Whether `[W&L] Enable Player Order Doctrine Diagnostics` should default on when the main valve is off. Recommended: yes, if the diagnostics are read-only and capped.
- Whether campaign and tactical caches should live in one shared static helper or separate scope-specific helpers. Recommended: one helper with explicit scope in the key to keep priority comparison centralized.
- Whether exact target buckets should be configurable after smoke. Recommended: keep constants internal until log evidence shows churn.
