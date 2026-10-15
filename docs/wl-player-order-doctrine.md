# W&L Player Order Doctrine

Living reference for #62 `PlayerSubordinateOrderPatch`, Whiskey's W&L current-order doctrine for a player who is a subordinate in the W&L chain, and the campaign current-order path in `WlStrategicOrderBridge`. Build, deploy and smoke status live in [`docs/handoff.md`](handoff.md). Player-model background: [`docs/wl-player-model.md`](wl-player-model.md).

The doctrine translates existing tactical-orchestrator intent into vanilla W&L current orders for the player's command. It does not replace vanilla movement, campaign movement or the W&L order UI. It asks the static vanilla `AIBattle.CheckCurrentOrderUpdate(...)` to deliver an order only after Whiskey's dedupe and safety gates predict the request is valid.

## Config

Existing BepInEx config files override C# defaults. Check `<GTCW>/BepInEx/config/dev.kyle.whiskey-realism.cfg` before interpreting smoke. Defaults:

```ini
[W&L]
Enable Player Order Doctrine = true
Enable Player Order Doctrine Diagnostics = true

[Tactical Orchestrator]
Tactical Commander Mode = Active
```

A write requires all of:

- plugin master `Enabled`;
- `Enable Player Order Doctrine`;
- `TacticalWriteGuard.ModeAllowsWrites()`, i.e. `Tactical Commander Mode = Active`;
- an evaluable W&L state (scenario active, a current command, not Commander-in-Chief, a side and army orchestrator);
- a candidate that passes Whiskey dedupe.

With writes off, `Enable Player Order Doctrine Diagnostics` still composes, classifies and logs candidates.

## Runtime Behavior

#62 wraps `AIBattle.UpdateDLCPlayerOrders()` with a Prefix/Postfix pair:

- Prefix snapshots the active vanilla order (`DLC_WL.givenorder`, `DLC_WL.GivenOrders.givenorderssession`).
- Vanilla runs its own W&L player-order coordinator, which can self-issue transitions 13/14/15 in `CheckRemovalOfOrders`.
- Postfix compares the snapshot, treats a changed active order as vanilla-owned, and composes a Whiskey candidate from the tactical orchestrator.
- After a call to `CheckCurrentOrderUpdate`, the active order is re-read; a match records the order as accepted, otherwise the attempt is recorded so the throttle applies.
- The tactical patch never passes `calledfromcampaign: true` and never writes `DLC_WL.givenorder` directly.

Dedupe state is cleared when the battle identity, the player's current command or the Commander-in-Chief status changes.

The campaign side stays in `WlStrategicOrderBridge`, which runs the same `PlayerOrderDedupe.Decide` rules with its own state and campaign throttle and issues its orders with `calledfromcampaign: true`.

## Composer

`PlayerOrderComposer` produces no candidate unless the side orchestrator exists, the player is a subordinate (not Commander-in-Chief), and either a command intent or a command task is found. Candidates, in the order the composer tries them:

| Intent | Vanilla type | Target |
|---|---|---|
| `RetreatToExit` | 15 | valid exit point only |
| `FallBackToLine` | 12 | fallback point |
| `HoldObjective` | 12 | objective point, else the unit's current position |
| `SupportMainEffort` | 11 | objective point |
| `AttackObjective` | 0 | objective point |
| `ProbeObjective` | 5 | objective point |
| `AdvanceToAssemblyArea` | 1 | objective point |

Objective points resolve from the command target, then the current objective, then (when allowed) the closest visible enemy (`PlayerOrderTargetPolicy`). Without a concrete target the composer fails closed, except that a hold intent falls back to a type-12 order at the unit's current position.

Type 15 is not a generic withdraw. Vanilla derives it in `CheckRemovalOfOrders(...)` from `bunits.SearchForClosestEntryPoint(...)`; Whiskey uses the same lookup and skips the candidate when no valid exit exists.

Orchestrator surfaces read by `PlayerOrderRuntimeAdapter`: `TacticalBattleCoordinator.GetSideOrchestrator(...)`, `TacticalBattleOrchestrator.Army`, `ArmyOrchestrator.ResolveCommandIntentForGroup(...)`, `CurrentDirectChildIntents`, `CurrentCommandOperations`, `CurrentDoctrineOrders`, `CurrentOperation` and `CurrentStrategicBattleIntent`.

## Dedupe And Provenance

`DLC_WL.givenorder` is one global vanilla slot, so Whiskey keeps its own per-unit shadow (request and accepted signatures, tick, battle identity) instead of treating every active order as a Whiskey order. `PlayerOrderDedupe.Decide`, in order:

1. Vanilla transition: an active vanilla/unknown type 13 or 15, or vanilla type 14, makes Whiskey yield.
2. An active vanilla/unknown type 7 or 12 with no matching Whiskey shadow blocks, unless the candidate is a type-15 retreat with a valid exit.
3. Vanilla dedupe model (`VanillaWouldBlock`): active 15 blocks tactical candidates; candidate 12 over active 0-5 or 13 is rejected; active 13 blocks non-13; candidate 14 is accepted only over active 12; same type is a duplicate. Stale or battle-ended orders never block.
4. An active vanilla type-11 support request blocks candidates of equal or lower priority.
5. A higher-priority active order of the same scope blocks (the 14-over-12 clear-hold transition excepted).
6. Cross-scope: a tactical candidate replaces a campaign order only with priority at least 40 above it and a campaign order that is no longer actionable (a type-15 retreat needs only a valid exit); a campaign candidate replaces a tactical order only when that order is stale, battle-ended or 40 below.
7. Same request signature as the shadow is suppressed; otherwise a 120-tick throttle applies after the last attempt.

The priority model is a request model, not a force model: Whiskey never bypasses vanilla dedupe by switching tactical calls to `calledfromcampaign: true`.

## Telemetry

Decision rows are the `TacticalPlayerOrder` telemetry event (Tactical layer, Decision category) with `scope`, action, reason, `type`, `intent`, `priority` and `target` fields. Actions:

- `skip`: W&L inactive, missing battle or current command, player Commander-in-Chief, no side or army orchestrator, no candidate.
- `suppress`: a dedupe reason, `writes-disabled`, or `vanilla-bridge-no-match`.
- `issue`: vanilla accepted the current-order call.

Rows are deduplicated per unit and signature (`PlayerOrderDiagnostics`, capacity 128). Failures log once as `[PlayerOrderIntent] ... failed` warnings.

## Smoke Checklist

Pass criteria with the defaults above:

- `TacticalPlayerOrder` rows are bounded and explain classification or suppression.
- #62 issues orders only for W&L player-subordinate commands; player Commander-in-Chief and non-W&L battles produce skips.
- accepted writes advance the vanilla current-order session.
- vanilla same-cycle transition orders are preserved.
- active campaign orders are not displaced unless the cross-scope rule allows it.
- type 15 orders use a valid closest-entry-point target.
- rejected candidates do not retry every tactical cadence.
- no repeated `[PlayerOrderIntent]` warning or Harmony failure.

## Rollback

```ini
[W&L]
Enable Player Order Doctrine = false
```

Diagnostics keep running. To silence them too:

```ini
Enable Player Order Doctrine Diagnostics = false
```

`Tactical Commander Mode = MonitorOnly` or `Off` also stops #62 writes, together with every other tactical write.

## Source Files

- Patch: `src/WhiskeyRealism/Patches/PlayerSubordinateOrderPatch.cs`
- Doctrine: `src/WhiskeyRealism/Tactical/PlayerOrders/`
- Campaign bridge: `src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs`
- Patch catalog: `docs/patch-catalog.md`
