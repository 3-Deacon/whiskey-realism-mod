# W&L Player Order Doctrine

Living reference for #62, Whiskey's W&L player-facing current-order doctrine for player-subordinate tactical commands and the shared campaign current-order cache in `WlStrategicOrderBridge`.

## Current State

- **Implementation state:** implemented in the current player-order worktree and hash-deployed locally; runtime smoke is still pending.
- **Patch ordinal:** #62 `PlayerSubordinateOrderPatch`.
- **Default behavior:** diagnostics are on by default, writes are off by default.
- **Verification:** console harness `898 PASS / 0 FAIL`; `./build.sh` passed with `0 Warning(s)` / `0 Error(s)`; local `dist/WhiskeyRealism.dll` and deployed BepInEx plugin match SHA-256 `5398831614e855f90b156c734fee50348d0bb6b10c8f0390b7a3d2499ba57740` (977920 bytes).
- **Lifecycle:** the design spec and implementation plan remain active until fresh in-game smoke proves bounded diagnostics and focused write behavior. Do not archive them yet.

The doctrine translates existing tactical-orchestrator intent into vanilla W&L current orders for the player when the player is a subordinate in the W&L chain. It does not replace vanilla movement, campaign movement, or the W&L order UI. It asks vanilla `AIBattle.CheckCurrentOrderUpdate(...)` to deliver an order only after Whiskey dedupe and safety gates predict the request is valid.

## Config

Existing BepInEx config files override C# defaults. Check `<GTCW>/BepInEx/config/dev.kyle.whiskey-realism.cfg` before interpreting smoke.

```ini
[W&L]
Enable W&L Player Order Doctrine = false
Enable W&L Player Order Doctrine Diagnostics = true

[Tactical Orchestrator]
Tactical Commander Mode = Active
```

Writes require all of these:

- plugin master enable is true;
- W&L player-order doctrine is true;
- `Tactical Commander Mode = Active`;
- vanilla W&L scenario/current-command gates accept the current battle state;
- Whiskey dedupe/provenance accepts the candidate order.

Diagnostics can compose, classify, and log without writes when `Enable W&L Player Order Doctrine Diagnostics = true`.

## Runtime Behavior

#62 wraps `AIBattle.UpdateDLCPlayerOrders()` with a Prefix/Postfix snapshot pair:

- Prefix snapshots vanilla `DLC_WL.givenorder`, `DLC_WL.GivenOrders.givenorderssession`, and whether vanilla had an active current order before the tick.
- Vanilla runs its own W&L player-order coordinator.
- Postfix detects vanilla same-cycle self-orders and either yields or composes a Whiskey candidate from the tactical orchestrator.
- Whiskey never calls `CheckCurrentOrderUpdate(..., calledfromcampaign:true)` from the tactical patch.
- Whiskey never mutates `DLC_WL.givenorder` directly.

The campaign side remains centralized in `WlStrategicOrderBridge`. Strategic callers that already route through the bridge now share the same signature/provenance discipline, so repeated campaign signatures are suppressed and campaign orders do not replace fresh tactical orders unless the cross-scope rule allows it.

## Dedupe And Provenance

`DLC_WL.givenorder` is one global vanilla slot, so Whiskey keeps its own shadow state instead of treating every active slot as a Whiskey order.

Rules:

- Accepted Whiskey orders record scope, unit, vanilla type, signature, priority, tick/time, and session.
- Failed vanilla bridge attempts are also recorded for cooldown so a rejected candidate does not retry every tactical cadence.
- Static caches clear when battle context changes, when the target unit/scene is no longer active, or when the active order can no longer be tied to the Whiskey shadow.
- Stale or battle-ended vanilla scene orders are ignored for priority suppression.
- Vanilla same-cycle transition orders type 13 and type 15 make the tactical Postfix yield.
- Vanilla type 14 is protected as a same-cycle transition, not as a permanent lock.
- Valid emergency retreat type 15 can override ambiguous active type 7 or type 12 orders when the scene and target are still valid.

Whiskey models vanilla tactical dedupe before issuing:

- active type 15 blocks further non-campaign tactical orders;
- candidate type 12 over active type 0, 1, 2, 3, 4, 5, or 13 is expected to be rejected;
- active type 13 blocks non-13 candidates;
- candidate type 14 is accepted only over active type 12;
- same-type duplicates are suppressed unless vanilla's positional/rotation checks require a material refresh;
- type 11 support requests stay owned by vanilla recursion inside `CheckCurrentOrderUpdate(...)`.

The priority model is therefore a request model, not a force model. Whiskey does not bypass vanilla dedupe by switching tactical calls to `calledfromcampaign:true`.

## Scope Boundary

Campaign and tactical orders share one vanilla current-order slot, but they are not the same commitment.

- Tactical orders may not replace fresh campaign-scope orders unless the tactical order exceeds the active campaign priority by the configured cross-scope gap and the campaign order is stale or no longer valid for the current orchestrator context.
- Campaign orders may refresh through `WlStrategicOrderBridge` only when their signature materially changes or their cooldown expires.
- Tactical diagnostics may still classify a suppressed candidate to explain why the slot was left alone.

## Tactical Inputs

The composer reads only existing orchestrator surfaces and safe vanilla scene data:

- `TacticalBattleCoordinator.GetSideOrchestrator(...)`
- `TacticalBattleOrchestrator.Army`
- `ArmyOrchestrator.ResolveCommandIntentForGroup(...)`
- `ArmyOrchestrator.CurrentDirectChildIntents`
- `ArmyOrchestrator.CurrentCommandOperations`
- `ArmyOrchestrator.CurrentDoctrineOrders`
- `ArmyOrchestrator.CurrentOperation`
- `ArmyOrchestrator.CurrentStrategicBattleIntent`

The consumed `CommandIntentResolution` shape is `Found`, `Intent`, and `Reason`. The consumed `CommandNodeIntent` fields are `NodeId`, `SourceNodeId`, `Role`, `Axis`, `PrimarySector`, `SupportPriority`, `AggressionBias01`, and `Depth`.

All vanilla object reads go through safe runtime helpers. Missing objectives, group geometry, current waypoints, entry points, or target names downgrade to a bounded warning or a skipped candidate; patches must not throw.

The composer fails closed without a concrete target. Direct-child role alone is not enough to invent a player order at the current unit position.

## Type 15

Type 15 is not a generic "withdraw somewhere" order. Vanilla derives it through `CheckRemovalOfOrders(...)` using `bunits.SearchForClosestEntryPoint(...)`.

Whiskey mirrors that entry-point lookup for tactical retreat/leave-field orders. If the lookup cannot produce a concrete exit target, #62 skips instead of issuing type 15 with an invented fallback point.

## Telemetry

Expected marker:

```text
[PlayerOrderIntent]
```

Rows should include scope, action, reason, unit, type, priority, signature, and whether vanilla accepted the bridge call when a write was attempted. Common actions:

- `classify` for diagnostics-only classification;
- `suppress` for dedupe, disabled writes, cross-scope, vanilla transition, missing target, or cooldown;
- `issue` for accepted vanilla current-order calls;
- `skip` for inactive W&L, missing orchestrator, unsupported command, or unsafe target state.

Rows must be signature-gated or interval-bounded. Repeated `Exception`, `ERROR`, `missing-anchor`, Harmony failure, or unbounded `[PlayerOrderIntent]` spam is a smoke failure.

## Smoke Checklist

First run diagnostics-only:

```ini
[W&L]
Enable W&L Player Order Doctrine = false
Enable W&L Player Order Doctrine Diagnostics = true

[Tactical Orchestrator]
Tactical Commander Mode = Active
```

Pass criteria:

- `[PlayerOrderIntent]` rows are bounded and explain classification/suppression.
- no vanilla current-order write is attempted by #62 while writes are disabled.
- no repeated warning/error/Harmony failure appears.
- player-CIC or non-subordinate states produce skips, not writes.
- same-cycle vanilla transition orders are preserved.

Focused write smoke:

```ini
[W&L]
Enable W&L Player Order Doctrine = true
Enable W&L Player Order Doctrine Diagnostics = true

[Tactical Orchestrator]
Tactical Commander Mode = Active
```

Pass criteria:

- #62 issues orders only for W&L player-subordinate commands.
- accepted writes produce a matching vanilla current-order session advance.
- active campaign orders are not displaced unless the cross-scope rule allows it.
- type 15 orders use a valid closest-entry-point target.
- stale/rejected candidates do not retry every tactical cadence.
- no player-CIC, unrelated player-side, or non-W&L battle receives a Whiskey player order.

## Rollback

Set:

```ini
[W&L]
Enable W&L Player Order Doctrine = false
```

If diagnostics are noisy, also set:

```ini
Enable W&L Player Order Doctrine Diagnostics = false
```

`Tactical Commander Mode = Off` remains the broad tactical-command rollback, but #62 also has its own W&L valve so player-order writes can be disabled without losing the rest of tactical diagnostics.

## Source Files

- Patch: `src/WhiskeyRealism/Patches/PlayerSubordinateOrderPatch.cs`
- Pure doctrine: `src/WhiskeyRealism/Tactical/PlayerOrders/`
- Campaign bridge: `src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs`
- Patch catalog: `docs/patch-catalog.md`
- Spec: `docs/superpowers/specs/2026-05-12-wl-player-order-doctrine-design.md`
- Plan: `docs/superpowers/plans/2026-05-12-wl-player-order-doctrine-implementation.md`
