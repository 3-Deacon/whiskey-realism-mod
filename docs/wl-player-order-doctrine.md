# W&L Player Order Doctrine

Living reference for #62 `PlayerSubordinateOrderPatch`, Whiskey's W&L current-order doctrine for a player who is a subordinate in the W&L chain, and the campaign current-order path in `WlStrategicOrderBridge`. Build, deploy and smoke status live in [`docs/handoff.md`](handoff.md). Player-model background: [`docs/wl-player-model.md`](wl-player-model.md).

The doctrine translates existing tactical-orchestrator intent into vanilla W&L current orders for the player's command. It does not replace vanilla movement, campaign movement or the W&L order UI. It asks the static vanilla `AIBattle.CheckCurrentOrderUpdate(...)` to deliver an order only after Whiskey's dedupe and safety gates predict the request is valid.

Since the W&L order flow (plan [`docs/superpowers/plans/2026-10-15-wl-order-flow.md`](superpowers/plans/2026-10-15-wl-order-flow.md), branch `fix/wl-order-flow`) every order bound for the player's panel, vanilla's and Whiskey's, battle and campaign, passes one capture Prefix on `CheckCurrentOrderUpdate`, rides a courier queue for vanilla's own order delay, and is given to vanilla by one delivery transaction. See § Order flow below; #62 is now one producer into it.

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
- Postfix compares the snapshot, treats a changed active order as vanilla-owned, and composes a Whiskey candidate from the tactical orchestrator. It runs on the player-side `AIBattle` only (`isplayeraiorfeud == 1`).
- The candidate is queued, not called: `TacticalPlayerDispatchRuntime.EmitP62` puts it on the battle courier queue with `unit` = the player's **parent unit** (vanilla's gate at 8337 returns for a subordinate's own command, so #62's orders never reached a subordinate before the order flow: `BUG-WL-023`). The delivery transaction (below) calls vanilla and tells #62 the outcome (`OnP62Outcome`): an accepted order is recorded as accepted, a refusal or policy drop as an attempt so the throttle applies, a structurally gone entry (superseded, unit gone) only clears the pending signature.
- The tactical patch never passes `calledfromcampaign: true`. Outside the delivery transaction it never writes `DLC_WL.givenorder` (see § Order flow, shared-row rule).

Dedupe state is cleared when the battle identity, the player's current command or the Commander-in-Chief status changes.

The campaign side is `WlStrategicOrderBridge` + `WlCampaignDispatchRuntime` (stage F, `docs/wl-dispatch-objective-bridge.md`): it no longer uses `PlayerOrderDedupe` or vanilla's gated `calledfromcampaign: true` call; it builds vanilla's campaign order itself with its own dedupe (`WlCampaignDispatchPolicy`), and since the order flow its orders ride the campaign courier queue.

## Order flow

One path for every order bound for the player's panel. Vanilla anchors are decompile build 14703111.

**Capture** (`WlOrderCapturePatch`, a skipping Prefix at `Priority.Last` plus a Finalizer on `AIBattle.CheckCurrentOrderUpdate`, `tactical.patch.wl-order-capture`). `WlOrderFlowRuntime.Capture` asks vanilla's own gates first (`WlOrderCaptureGate`, a transliteration of 8251-8369; the enemy side's calls end at the cheap tests with no allocation), then:

- battle: `WlOrderFlow.DecideCapture` with `PlayerOrderReadmitPolicy` (below). A call that would be dropped by vanilla's dedupe (8640-8646) and that the policy drops too is dropped here; one the policy re-admits, or that vanilla would accept, goes on a courier. Vanilla's own 13/14 (the player's contact state) and a type-11 refresh pass straight through; while the player's command is routed (N1) 13/14 are dropped so the released hold stays. Any exception passes the call to vanilla.
- campaign (`calledfromcampaign: true`): `WlCampaignCourierRuntime.Capture`, never the re-admit policy (vanilla does not dedupe campaign calls, 8640). A call that only re-pops the order already on the panel (same type, point within 110, flat XZ, inclusive) is dropped (`repop`: the capital-defense bug, `BUG-WL-018`); the rest rides a campaign courier.
- Type 15 made while the enemy's or the feud's `AIBattle` is inside `CheckRemovalOfOrders` is dropped (`BUG-WL-019`, `WlRemovalOrderSourcePatch` stamp + `EnemyRetreatGuard`), independent of the flow's switches and of Commander Mode.

**Re-admit policy** (`PlayerOrderReadmitPolicy`, pure). `PredictVanilla` mirrors the seven clauses (a)-(g) of 8643; `Decide` returns `Admit`, `AdmitWithClear` (the slot is emptied for the delivery), `Defer` or `Drop`. Clauses (e) 14 over anything but 12, (f) a top-level 2 over 0-4 and (g) anything over an active 15 are never re-admitted. Whiskey re-admits: a retreat over anything (15 over 13 clears the slot); the same type 1-4 or 12 at a place more than 110 m away once the active order is 0.25 doctrine hours old (`MinHoldBattleHours`; inside the hold it is deferred, never dropped); a defend over an attack once the objective is held or the army turned defensive; the release (below); the follow-up (below). Engage-at-will (13) otherwise blocks everything. A type-11 refresh is never touched. The order age is on the doctrine clock (`TacticalDoctrineTime`), an order present when the battle was first looked at counts as past the hold.

**Couriers** (`WlCourierQueue`, `WlCourierDelay`, pure). A fixed pool of 8 entries per scope. Cadence: the same issuer, type and origin inside the hold window (battle 0.25 h; campaign one game day) is dropped `rate-limited` (a retreat is exempt; a campaign key includes the target); the same type within 110 of a pending entry is `en-route`; a full queue discards the oldest, never a retreat. The delay is vanilla's order-delay model read from `GamePrefs` at runtime: battle, along the snapshot's command chain from the issuing headquarters to the player's command, a bugle leg `max(processing, recognition)` inside the sender's bugle range, a mounted courier `processing + d / v` beyond it, each relay leg `recognition` or `route + d / v` (`v = unitbasicspeed[5, mounted] x runningspeedfact[5] x basicunitspeed`); campaign, `recognition x max(10, sqrt(d) x 5) / 1000` times the issuer's telegraph, winter-quarters, entrenchment, army-group, readiness and perk factors, plus the processing time, in campaign years. `GameVars.useorderdelays` off gives 0. Vanilla itself gives the W&L player's command no delay at all (125017-125019); the flow replaces that. An entry older than the last accepted delivery is superseded; the queue is cleared at the end-of-day cycle, when the player's command or CIC status changes, and (battle) at battle end.

**Delivery** (`WlOrderDeliveryPatch`, a `Priority.Low` Postfix on `UpdateDLCPlayerOrders`, player-side instance only; the campaign delivery runs in #69's `UpdateUnitAI` Postfix right after `Flush`). `WlOrderFlow.RunPass` takes everything due into a buffer, then for each entry: discards it if superseded, its unit is gone, it is cross-scope (a battle entry over a campaign order, the reverse), a stale release or a stale follow-up; decides it against the slot as it stands now; and delivers it. **Shared-row rule:** the slot is read immediately before any write; if its object changed since the decision nothing is written and the entry is looked at again next pass. A delivery sets the bypass (`WlOrderBypass`), calls vanilla's `CheckCurrentOrderUpdate` with the entry's original arguments, and in a `finally` restores the saved order into a slot Whiskey emptied when vanilla refused the new one, and clears the bypass. A throw inside vanilla discards the entry (`vanilla-threw`). This replaces the old contract that the doctrine never writes the slot: the slot is written only inside this transaction. The other writes are field-level bookkeeping on vanilla's order and on the player's units, named in `AGENTS.md` § Patch hygiene: the order's `commanderid` (stale issuer) and `content` (formation name, condition lines), the player-owned top units' `lastcombatzonecount` (non-paying holds) and the capital-defense restore of their `running` / `doublequick`. Nothing moves, stops or re-tasks a player-owned unit.

**Derived orders.** Retreat: vanilla sends a 15 only into a non-null slot (6788), so after a load, at battle start or after the end-of-day clear it never comes; while the player-side instance's `macroai == 3` the pass builds vanilla's own call (6794-6798) and delivers it on a courier. Release: engage-at-will (13) is released to a type-12 hold at the player's position when both a fog-honest test (no Visible enemy within a regiment's fire range on the side's snapshot) and vanilla's own 13 trigger (6800) have been false for 0.1 h, or at once while the command is routed (at least half its regimental strength routed or retreating). Follow-up (`Enable W&L Order Follow-Up`): when vanilla has set `firstunitreached` on a 1-4 order, 0.25 h have passed and no newer order is pending, a type 12 at the zone ("hold the position you reached"). Release and follow-up pay no prestige: the pass marks their session by setting `lastcombatzonecount` of the player-owned top units to it, so vanilla's first-entry branch (123484-123492) never fires; they also start no compliance sample.

**Text** (`WlOrderText`, `WlOrderTextRuntime`, `WlGivenOrderText`; `Enable W&L Order Text`). No Whiskey key reaches vanilla's `[VAR49]`: a place is the nearest vanilla objective within 150 m, else "about N yards ahead / to the right of your position" (eight sectors, yards rounded to 100, "your present position" under 150 yards), an optional slot clause after it, 80 characters, no line break. Whiskey's orders carry a condition line (intent, "move at first light" before the day starts, the nearest same-echelon neighbours); vanilla's "My Division" becomes the formation's name on delivered vanilla orders too (campaign included). After a content change the panel is shown again in the same frame. A stale issuer (the stored commander no longer commands) is replaced by the order group's commander (`BUG-WL-022`). Battle type mapping is `TacticalDispatchTypeMapper` only (probe/screen/guard 1, support 2, release-reserve 11 onto an own-held target, 0 onto an enemy-held one).

**Campaign** (`WlCampaignCourierRuntime`, `WlCampaignCourierPolicy`; `docs/wl-dispatch-objective-bridge.md`). Vanilla campaign calls to a top-level player and Whiskey's orders (bridge, #69) ride the campaign queue; pending entries are saved in the sidecar (`campaignCouriers`) with the unit as GameObject name plus commander id. A reached type 5/6/7/16 order is acknowledged with a message (`ShowNewInfo` only). `AICampaign.AssignUnitToDefendCapital` no longer sets forced march on the player's own group (`CapitalDefensePlayerSpeedFixPatch`, `BUG-WL-018`).

**Compliance.** A battle order starts a compliance sample only when the player's command is outside its zone when it reaches him (`ComplianceSampleGate`, the order mark's rotated box); otherwise `[PlayerCompliance] scope=battle skip reason=inside-at-issue`. No penalty path exists or was added.

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

`DLC_WL.givenorder` is one global vanilla slot, so Whiskey keeps its own per-unit shadow (request and accepted signatures, tick, battle identity) instead of treating every active order as a Whiskey order. #62 no longer models vanilla's dedupe itself (the old `VanillaWouldBlock` and its ratchet are gone): what vanilla would drop and what Whiskey re-admits is `PlayerOrderReadmitPolicy`'s, decided where the order is captured. `PlayerOrderDedupe.Decide`, in order:

1. Vanilla transition: an active vanilla/unknown type 13 or 15, or vanilla type 14, makes Whiskey yield, except for a valid retreat (a retreat goes through engage-at-will or a clear-hold transition).
2. An active vanilla/unknown type 7 or 12 with no matching Whiskey shadow blocks, unless the candidate is a type-15 retreat with a valid exit.
3. An active 15 blocks tactical candidates (`retreat-terminal`, vanilla's clause g).
4. An active vanilla type-11 support request blocks candidates of equal or lower priority.
5. A higher-priority active order of the same scope blocks only inside the hold (`MinHoldBattleHours`, doctrine hours; a retreat excepted).
6. Cross-scope: a tactical candidate replaces a campaign order only with priority at least 40 above it and a campaign order that is no longer actionable (a type-15 retreat needs only a valid exit).
7. Same request signature as the shadow is suppressed; otherwise a throttle on the doctrine clock applies after the last attempt. A queued candidate keeps its signature pending until the delivery callback.

The priority model is a request model, not a force model: Whiskey never bypasses vanilla dedupe by switching tactical calls to `calledfromcampaign: true`.

## Telemetry

Decision rows are the `TacticalPlayerOrder` telemetry event (Tactical layer, Decision category) with `scope`, action, reason, `type`, `intent`, `priority` and `target` fields. Actions:

- `skip`: W&L inactive, missing battle or current command, player Commander-in-Chief, no side or army orchestrator, no candidate.
- `suppress`: a dedupe reason, `writes-disabled`, or `vanilla-bridge-no-match`.
- `issue`: vanilla accepted the current-order call.

Every event in the life of an order bound for the panel is one `TacticalPlayerOrderFlow` row (battle) or `CampaignPlayerOrderFlow` row (campaign): `capture`, `drop`, `deliver`, `discard`, `defer`, `requeue`, `prestige-delta`, `speed-restore`; fields and reasons are in [`docs/telemetry.md`](telemetry.md) § W&L order flow rows.

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
- the order-flow smoke items S1-S12 of the plan ([`docs/superpowers/plans/2026-10-15-wl-order-flow.md`](superpowers/plans/2026-10-15-wl-order-flow.md)) read from the `TacticalPlayerOrderFlow` / `CampaignPlayerOrderFlow` rows; none has been run on a deployed DLL yet.

## Rollback

```ini
[W&L]
Enable Player Order Doctrine = false
```

Diagnostics keep running. To silence them too:

```ini
Enable Player Order Doctrine Diagnostics = false
```

`Tactical Commander Mode = MonitorOnly` or `Off` also stops #62 writes and every battle capture and slot write of the order flow, together with every other tactical write (the bug fixes `Enable W&L Enemy Retreat Order Guard`, `Fix Lost Orders Fog Grid`, `Fix Capital Defense Player Order` stay).

Order-flow switches, all in `[W&L]` and default on: `Enable W&L Order Readmit` (off: vanilla's order dedupe), `Enable W&L Order Couriers` (off: orders are delivered at the next pass; the campaign queue is bypassed), `Enable W&L Order Text` (off: vanilla texts), `Enable W&L Order Follow-Up`, `Fix Capital Defense Player Order`, `Enable W&L Enemy Retreat Order Guard`, `Fix Lost Orders Fog Grid`.

## Source Files

- Patch: `src/WhiskeyRealism/Patches/PlayerSubordinateOrderPatch.cs`
- Doctrine: `src/WhiskeyRealism/Tactical/PlayerOrders/`
- Order flow, battle: `Patches/WlOrderCapturePatch.cs`, `WlOrderDeliveryPatch.cs`, `WlRemovalOrderSourcePatch.cs`; `Tactical/PlayerOrders/WlOrderFlow.cs` (pure pass and host seam), `WlOrderFlowRuntime.cs`, `PlayerOrderReadmitPolicy.cs`, `WlOrderCaptureGate.cs`, `WlCourierQueue.cs`, `WlCourierDelay.cs`, `WlOrderText.cs`, `WlOrderTextRuntime.cs`, `WlGivenOrderText.cs`, `WlOrderBugFixPolicy.cs`
- Order flow, campaign: `Strategic/WlCampaignCourierPolicy.cs`, `WlCampaignCourierRuntime.cs`, `Patches/CapitalDefensePlayerSpeedFixPatch.cs`, `Patches/LostOrdersFowFixPatch.cs`
- Campaign bridge and dispatcher: `src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs`, `WlCampaignDispatchRuntime.cs`
- Patch catalog: `docs/patch-catalog.md`
