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

Campaign planner invariants (stage G): the planner stands down for a CIC player's alliance; the map-group player is neither moved nor ordered by the planner (smoke amendments 2026-10-16 D3 removed his Support order: outside the mass a formation holds, and he is never mass, MainEffort, picket or recon mission; he counts toward the hold of the sector his last waypoint lies in); an AI group above a player below map level is movable and its player is told nothing (vanilla sends him no campaign order, 8366); the player's own command and units under it are never moved, appointed or re-tasked by the mover, the recon executor or the appointment planner (`WlChainRules.MayMove`).

## Order flow

One path for every order bound for the player's panel. Vanilla anchors are decompile build 14703111.

**Capture** (`WlOrderCapturePatch`, a skipping Prefix at `Priority.Last` plus a Finalizer on `AIBattle.CheckCurrentOrderUpdate`, `tactical.patch.wl-order-capture`). `WlOrderFlowRuntime.Capture` asks vanilla's own gates first (`WlOrderCaptureGate`, a transliteration of 8251-8369; the enemy side's calls end at the cheap tests with no allocation), then:

- battle: `WlOrderFlow.DecideCapture` with `PlayerOrderReadmitPolicy` (below). A call that would be dropped by vanilla's dedupe (8640-8646) and that the policy drops too is dropped here and never queued (vanilla's continuous callers repeat it every pass, and queued copies would evict real entries from the eight-entry queue); one the policy re-admits or defers, or that vanilla would accept, goes on a courier. A Whiskey emitter's type 11 onto an active 11 is vanilla's in-place reserve-zone refresh and is given to vanilla at once. Vanilla's own 13/14 (the player's contact state) and a type-11 refresh pass straight through; while the player's command is routed (N1) 13/14 are dropped so the released hold stays. Any exception passes the call to vanilla.
- campaign (`calledfromcampaign: true`): `WlCampaignCourierRuntime.Capture`, never the re-admit policy (vanilla does not dedupe campaign calls, 8640). A call that only re-pops the order already on the panel (same type, point within 110, flat XZ, inclusive) is dropped (`repop`: the capital-defense bug, `BUG-WL-018`); the rest rides a campaign courier.
- Type 15 made while the enemy's or the feud's `AIBattle` is inside `CheckRemovalOfOrders` is dropped (`BUG-WL-019`, `WlRemovalOrderSourcePatch` stamp + `EnemyRetreatGuard`), independent of the flow's switches and of Commander Mode.

**Re-admit policy** (`PlayerOrderReadmitPolicy`, pure). `PredictVanilla` mirrors the seven clauses (a)-(g) of 8643; `Decide` returns `Admit`, `AdmitWithClear` (the slot is emptied for the delivery), `Defer` or `Drop`. Clauses (e) 14 over anything but 12, (f) a top-level 2 over 0-4 and (g) anything over an active 15 are never re-admitted. Whiskey re-admits: a retreat over anything (15 over 13 clears the slot); the same type 1-4 or 12 at a place more than 110 m away once the active order is 0.25 doctrine hours old (`MinHoldBattleHours`; inside the hold it is deferred, never dropped); a defend over an attack once the objective is held or the army turned defensive; the release (below); the follow-up (below). Engage-at-will (13) otherwise blocks everything. A type-11 refresh is never touched. The order age is on the doctrine clock (`TacticalDoctrineTime`), an order present when the battle was first looked at counts as past the hold.

**Couriers** (`WlCourierQueue`, `WlCourierDelay`, pure). A fixed pool of 8 entries per scope. Cadence: the same issuer, type and origin inside the hold window (battle 0.25 h; campaign one game day) is dropped `rate-limited` (a retreat is exempt; a campaign key includes the target); the same type within 110 of a pending entry is `en-route`; a full queue discards the oldest, never a retreat. The delay is vanilla's order-delay model read from `GamePrefs` at runtime: battle, along the snapshot's command chain from the issuing headquarters to the player's command, a bugle leg `max(processing, recognition)` inside the sender's bugle range, a mounted courier `processing + d / v` beyond it, each relay leg `recognition` or `route + d / v` (`v = unitbasicspeed[5, mounted] x runningspeedfact[5] x basicunitspeed`); campaign, `recognition x max(10, sqrt(d) x 5) / 1000` times the issuer's telegraph, winter-quarters, entrenchment, army-group, readiness and perk factors, plus the processing time, in campaign years. `GameVars.useorderdelays` off gives 0. Vanilla itself gives the W&L player's command no delay at all (125017-125019); the flow replaces that. An entry older than the last accepted delivery is superseded; the queue is cleared at the end-of-day cycle, when the player's command or CIC status changes, and (battle) at battle end.

**Delivery** (`WlOrderDeliveryPatch`, a `Priority.Low` Postfix on `UpdateDLCPlayerOrders`, player-side instance only; the campaign delivery runs in #69's `UpdateUnitAI` Postfix, the campaign order pass, after it clears a carried player's leftover campaign order). `WlOrderFlow.RunPass` takes everything due into a buffer, then for each entry: discards it if superseded, its unit is gone, it is cross-scope (a battle entry over a campaign order, the reverse), a stale release or a stale follow-up; decides it against the slot as it stands now; and delivers it. **Shared-row rule:** the slot is read immediately before any write; if its object changed since the decision nothing is written and the entry is looked at again next pass. A delivery sets the bypass (`WlOrderBypass`), calls vanilla's `CheckCurrentOrderUpdate` with the entry's original arguments, and in a `finally` restores the saved order into a slot Whiskey emptied when vanilla refused the new one, and clears the bypass. A throw inside vanilla discards the entry (`vanilla-threw`). This replaces the old contract that the doctrine never writes the slot: the slot is written only inside this transaction. The other writes are field-level bookkeeping on vanilla's order and on the player's units, named in `AGENTS.md` § Patch hygiene: the order's `commanderid` (stale issuer) and `content` (formation name, condition lines), the player-owned top units' `lastcombatzonecount` (non-paying holds) and the capital-defense restore of their `running` / `doublequick`. Nothing moves, stops or re-tasks a player-owned unit.

**Move gate.** The delay above is timed from the issuing headquarters, and an executor dispatch names the unit it moves: the brigade, not the army above it. Vanilla gives that unit its move by the same courier system (`SetWaypoint` queues it on the unit's scene parent, `AddToOrderQueue` 124917, 91461-91504, behind every earlier entry: each adds `processingtimegroupstandard`, 0.02 h, to the latest queued `processingtime`, and a mounted courier does not ride until then, 125159-125202), while the player's share is one bugle leg from the unit. The 2026-10-16 smoke showed the gap: the brigade's move was the 22nd entry on the army's queue (+0.43 h), the panel showed it at +0.02 h, and the player marched on his own for more than half an hour of game time before his brigade moved.

The executor knows which units it moved and reads the queue entry vanilla made for each (`WlMoveQueue.TryPending`, after `IssueMove`, for a unit that holds a path); the one carried out last (processing time) is the dispatch's carrier (`WlMoveCarrier`: the ordered group itself, or the latest of the children a fan-out around a non-detached player ordered, whose moves ride the brigade's own queue), recorded with that entry's creation stamp (`lastupdate`), its ordertype and the point the unit was sent to. `Emit` hands it to the flow and `Route` arms the entry (`AwaitsMove`, `Carrier`, `LegHours` = the superior-to-player delay) while `Enable W&L Order Couriers` is on. #62's and the derived orders are not armed. Vanilla's own orders of type 0, 2, 3 and 4 are armed differently (§ Vanilla orders the chain abandons below). `WlOrderFlow.ReleaseOnMove` looks at an armed entry on every pass (`WlMoveQueue.StateOf` and `WlMoveState.Classify`):

- an entry of the carrier unit that `WlMoveState.Carries` (the carrier's own ordertype, 0 or 1, and a creation stamp not before the carrier's) is queued: the order waits (`defer move-in-flight`, one row). The match is by ordertype and stamp, not by processing time: a permanently detached pool brigade's second `SetWaypoint` replaces its entry without the 0.02 h standard time (124929-124931, 124966), so the replacement can be earlier in processing time but never earlier in creation; an older move of the same unit is another order's. The stamp alone does not separate it: `ProcessOrders` rewrites `lastupdate` on every pass once an entry's processing time has passed (125331), so an older entry that is being processed can carry a stamp later than the carrier's. Two entries of one unit and one ordertype cannot coexist (a second replaces the first, 124929-124933), so an older entry is of the other move ordertype, and requiring the carrier's own ordertype keeps it out. A path added while a move entry is pending rides that entry (`SetOrderStatus` transmits every path the unit holds, 125484-125508);
- the entry is gone and the move was carried out (`WlMoveState.BasisAfterGone`), each reading tied to the TARGET (2026-10-17 smoke, order 6: a fall-back to a point 2 km to the rear was released because the brigade held a re-laid path and was later in contact, neither checked against the target): the unit stands within 60 m (`OrderArbiter.DedupeMeters`, the executor's own at-the-target distance) of the point it was sent to (`arrived`), or it holds a path that ends within 60 m of it (`path`; the path's end is the unit's `lastsetwaypointposition`, which vanilla stamps on every group move, 92067), or it is in contact (`combatstatussubordinates > 0`) and stands within `WlMoveState.ContactNearMeters` (310 m: a regiment's sight 200-250 m plus the arrival distance) of it (`contact`). The verdict is the `gate` field of the flow row. The order is due one leg later (`DueAt = max(DueAt, now + LegHours)`, so a policy deferral stays). The delivery pass runs at most once per 30 AI frames (`microaitaskupdatecycle == 28`, 4970, 5682-5685), so a unit that carried its move out and finished it between two passes has no path left when the entry is first seen gone; an arrival is read from position, not only from the path. A unit that met the enemy halts (`CheckEnemyContact`: `SetWaypoint` 117372, `StopRegiment` 117380, `CheckCurrentOrderUpdate` type 1 at 117383, which clears its paths) far from its target; vanilla itself treats `combatstatussubordinates > 0` as a group busy with combat (3593, 8090), so such a unit executed the move up to the fight and the order is shown;
- the entry is gone and none of the three readings holds (no path to the target, farther than 60 m, no contact within 310 m), or it is queued with an active mounted courier line to the unit that has no courier (vanilla's lost-order test, 125282-125286): the move will not be made and the order is discarded (`discard move-lost`). A lost order is mostly gone from the queue before a pass looks: vanilla zeroes the line, sets `orderstate` 3 and clears the untransmitted waypoints (125282, 125286), then carries out and deletes the entry one or two `ProcessOrders` passes later (125425), which resets `orderstate`. Neither the line nor `orderstate` outlives that; the unit's path does. A carrier must hold a path when the dispatch is captured, or the dispatch is not armed.

Vanilla also carries an entry out without its courier once it is more than 0.2 h past its processing time and the line's courier was never released (125214), so an order held for such an entry waits at most that long beyond its processing time; a released courier can ride longer.

**A newer move voids the old order (`superseded-intent`).** When the executor issues a newer Move for a group, that group's queued executor-origin orders are discarded before the new move's own order is queued (a fan-out also voids the queued executor orders of every child it moved, each against its own target: a brigade whose regiments march to B while its own waypoint still reads A would otherwise count as pursuing A; the voiding is by queue, not by reading the children's paths; a vanilla-origin order of such a brigade is not voided this way) (a Maintenance move voids none: an interrupted path's recovery can aim at a fresh assembly point, `PostureIntentProducer.TryRecoveryPath`, and would otherwise void an order the brigade is still carrying out) (`WlCourierQueue.DiscardSupersededIntents`, one `discard` row each), unless the queued order is the same place again (within 110 m: it keeps its place and the en-route test drops the repeat). Smoke 2026-10-17 order 6 sat on the queue after the executor had moved the brigade elsewhere.

**The executor's dispatch decision.** Whether a move of an ancestor of the player's command becomes an order for him is decided at three points. (1) OrderExecutor step 9 sends no dispatch for a Maintenance move (`IntentFlags.Maintenance` (bit 524288, distinct from every other flag, pinned): `RecoverInterruptedOrder`'s move and a leaf brigade's formation-slot move); a formation-only re-lay is a Formation intent and never had a dispatch. A `drop reason=maintenance` row says so. (2) `PlayerDispatchMemory` (battle-scoped, `TacticalPlayerDispatchRuntime.Memory`): a dispatch for a target within 110 m of the group's last dispatch is the same order again, whatever its type (`drop reason=repeat-target`); a dispatch that never reached the panel (any executor-origin `discard`) is forgotten, and so is every remembered target when another order takes the panel (the panel's session changes: `NotePanel`, noted at every delivery pass and at every accepted order; the executor dispatch that opened the session keeps its own target), so a reissue to a place after vanilla's order replaced it is a new order, not a repeat. (3) A queued dispatch carries the group's task (`CourierEntry.DispatchTask`); at delivery, after the move gate, it is discarded when the group has another task (`discard reason=task-changed`) and waits until its task has held for the task-hold time (`defer reason=task-hold`; `Plugin.BrigadeTaskHoldSeconds`, the executor's own same-task hold). The group's task is noted on every executed write except the works hold's stance, which names HoldObjective whatever the group does (`PlayerDispatchMemory.NoteTask`), so a task that flips every few seconds announces nothing. Smoke 2026-10-17 order 13 was an interrupted path re-issued as FormUp and shown as "Deploy to engage"; the division's task flipped every 18-36 s.

**Vanilla orders the chain abandons (`chain-abandoned`).** Vanilla's callers set the called unit's waypoint just before the order of type 0 and 12 (`UpdateMovingTargets`, 6958-7002: one waypoint, then 0 for an enemy-held objective and 12 for an own-held one; the other type-12 caller, 6463 (`CheckGlobalAIStrategy`, the defend order when the macro turns defensive), sets none and names the closest objective or the parent's centre, so a type 12 is armed only when its point lies within the slack of the unit's last waypoint at capture, `WlMoveState.ArmsVanillaOrder`; any other 12 stays unarmed), 2 (`MarchToSoundOfGuns`, 3759-3766), 3 (assault on an isolated target, 8185-8210) and 4 (assault move, 3640-3646), but outside the test that guards the waypoint (`PerformAIActionDLCWL`, `dlcw_isundercommander`): the order is sent whether or not the unit moved. Those `SetWaypoint` calls pass `useorderdelay: false`, so no queue entry is waited for; type 0 is queued behind the order delay, or, for a unit more than 150 m from its objective, goes through `BlockedMovingOrder`, which sets the waypoint two frames after the order is given (6950-6958, 3382), so a waypoint read at capture can still be the previous move's. The capture therefore arms a vanilla-origin order of those types when the called unit holds a path (`regimentpaths > 0`), (the unit is on a march when it, or its regiments, hold paths: `WlMoveState.IsMoving`, `paths > 0 || groupsubordinatesmoving > 0`; smoke14's brigade read paths 0 on 6 of 18 marching rows; the player's own command is never armed) with the unit as carrier and the ORDER's position as the target, read with `WlMoveState.VanillaWaypointSlackMeters` (220 m = the 70 m terrain offset of `UpdateMovingTargets`, 6941, plus the 150 m shift of `CheckForSimilarPositions`, 8700-8705) added to every distance of the reading (`CourierEntry.Pursuit`, `WlMoveCarrier.NoQueueEntry`, `SlackMeters`; the executor's carriers are exact). When the order is due, `WlOrderFlow.StillPursued` asks the same reading as the move gate (`WlMoveState.BasisAfterGone`): path ending at the point, standing at it, or in contact within 310 m (each plus the slack). Otherwise the order is discarded `chain-abandoned` (a queue discard in the existing flow: vanilla's call has been made and is not skipped). Type 1 (117383) is the halt itself (the caller sets the waypoint on the unit's own position, stops its units, then orders) and is not armed; 11, 12 and 13-15 describe no move of the called unit. A vanilla order a newer one overtakes before it is due is `superseded` by the pass's existing rule.

**End of day.** The queue is also cleared when the coordinator sees the battle enter its end-of-day sequence (`TacticalBattleCoordinatorRuntime`, `EndOfDayPhase.Suspended` -> `WlOrderFlowRuntime.OnEndOfDay`), because the delivery pass only clears it when vanilla's order pass happens to run during the sequence (smoke 2026-10-17 order 8, a type 11 captured at the end of day one, reached the panel after the night).

**Attached players (user decision, left as it is).** An attached player (his command not detached from its parent, so the executor fans the order out around him) is never carried by Whiskey's group order: nothing moves a player-owned unit (AGENTS.md § Patch hygiene). Vanilla's group move carries him when attached; detached, the brigade leaves without him and the order tells him to follow. This stays so.

The player ends with the latest order the chain gave. A held order that a later order overtakes is superseded by the pass's existing rule (`discard superseded`, a row, never silent) and the later order is shown with its own timing; orders released together are shown in issue order; a lost latest move leaves the player with the last order that can be carried out (`NoOrderIsSilentlyLostAndTheLatestIsShownLast`). Nothing in the gate moves, stops or re-tasks a unit; it reads `orderqueue` only.

**Derived orders.** Retreat: vanilla sends a 15 only into a non-null slot (6788), so after a load, at battle start or after the end-of-day clear it never comes; while the player-side instance's `macroai == 3` the pass builds vanilla's own call (6794-6798) and delivers it on a courier. Release: engage-at-will (13) is released to a type-12 hold at the player's position when both a fog-honest test (no Visible enemy within a regiment's fire range on the side's snapshot) and vanilla's own 13 trigger (6800) have been false for 0.1 h, or at once while the command is routed (at least half its regimental strength routed or retreating). Follow-up (`Enable W&L Order Follow-Up`): when vanilla has set `firstunitreached` on a 1-4 order, 0.25 h have passed and no newer order is pending, a type 12 at the zone ("hold the position you reached"). While the follow-up is the order on the panel the flow remembers the order it replaced (type and point): a repeat of that type within 110 m (an assault re-issued by an idle ancestor) is dropped, `follow-up-holds`, at capture and at delivery, so the hold does not reopen the order, open a paying session or churn the panel; any other order follows the normal rules, and the memory ends with the follow-up. Release and follow-up pay no prestige: the pass marks their session by setting `lastcombatzonecount` of the player-owned top units to it, so vanilla's first-entry branch (123484-123492) never fires; they also start no compliance sample.

**Text** (`WlOrderText`, `WlOrderTextRuntime`, `WlGivenOrderText`; `Enable W&L Order Text`). No Whiskey key reaches vanilla's `[VAR49]`: a place is the nearest vanilla objective within 150 m, else "about N yards ahead / to the right of your position" (eight sectors, yards rounded to 100, "your present position" under 150 yards), an optional slot clause after it, 80 characters, no line break. Whiskey's orders carry a condition line (intent, "move at first light" before the day starts, the nearest same-echelon neighbours); vanilla's "My Division" becomes the formation's name on delivered vanilla orders too (campaign included). After a content change the panel is shown again in the same frame. A stale issuer (the stored commander no longer commands) is replaced by the order group's commander (`BUG-WL-022`). Battle type mapping is `TacticalDispatchTypeMapper` only (probe/screen/guard 1, support 2, release-reserve 11 onto an own-held target, 0 onto an enemy-held one).

**Campaign** (`WlCampaignCourierRuntime`, `WlCampaignCourierPolicy`; `docs/wl-dispatch-objective-bridge.md`). Vanilla campaign calls to a top-level player and Whiskey's orders (the strategic order bridge) ride the campaign queue; a player carried by an AI corps gets neither, and a campaign order he still holds from before 2026-10-16 is cleared (`ClearStaleCarriedOrder`); pending entries are saved in the sidecar (`campaignCouriers`) with the unit as GameObject name plus commander id. A reached type 5/6/7/16 order is acknowledged with a message (`ShowNewInfo` only). `AICampaign.AssignUnitToDefendCapital` no longer sets forced march on the player's own group (`CapitalDefensePlayerSpeedFixPatch`, `BUG-WL-018`).

**Compliance.** A battle order starts a compliance sample only when it is not an automatic hold and the player's command is outside its zone when it reaches him. The origin decides first (`ComplianceSampleGate.OriginStartsSample`: `Release` and `FollowUp` never; the order flow records the origin of each session, `WlOrderFlowRuntime.OriginOfSession`), then the position test (the order mark's rotated box): `[PlayerCompliance] scope=battle skip reason=automatic-hold` / `skip reason=inside-at-issue`. Why the automatic hold of the 2026-10-17 smoke (#12) was scored: the hold is built at the player's position when it is decided and shown a courier leg later (0.060 and 0.077 h in the rows), while an attached player marches with his brigade, so the zone was behind him and the position test started a sample that could only be missed. A sample is then judged against the order's carrier (the group the order was addressed to), not against the clock from issue (`PlayerComplianceLedger.Tick(scope, now, ComplianceChain)`): the 0.5 h grace runs from the carrier's arrival in the zone; a carrier halted by contact or stopped short before arriving ends the sample neutral, and so does one still marching after `ChainWaitHours` (3 h); the player's own command is never his own chain, and campaign orders keep the clock from issue. A newer order before the deadline is neutral (`OnDispatch` returns Neutral). The stored estimate carries `measure` = `PlayerComplianceLedger.MeasureVersion` (2); a sidecar estimate without it (the 0.10 taken with the old measure) is not adopted, once. `[PlayerCompliance]` lines carry `session`, `type` and `origin`: `start`, `outcome=complied|missed`, `outcome=neutral chain=halted|abandoned|marching`, `skip reason=...`. No penalty path exists or was added.

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
- Order flow, battle: `Tactical/Battle/PlayerDispatchMemory.cs` (the executor's dispatch memory), `Tactical/PlayerOrders/WlMoveQueue.cs` (move gate reads), `Patches/WlOrderCapturePatch.cs`, `WlOrderDeliveryPatch.cs`, `WlRemovalOrderSourcePatch.cs`; `Tactical/PlayerOrders/WlOrderFlow.cs` (pure pass and host seam), `WlOrderFlowRuntime.cs`, `PlayerOrderReadmitPolicy.cs`, `WlOrderCaptureGate.cs`, `WlCourierQueue.cs`, `WlCourierDelay.cs`, `WlOrderText.cs`, `WlOrderTextRuntime.cs`, `WlGivenOrderText.cs`, `WlOrderBugFixPolicy.cs`
- Order flow, campaign: `Strategic/WlCampaignCourierPolicy.cs`, `WlCampaignCourierRuntime.cs`, `Patches/CapitalDefensePlayerSpeedFixPatch.cs`, `Patches/LostOrdersFowFixPatch.cs`
- Campaign bridge and dispatcher: `src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs`, `WlCampaignDispatchRuntime.cs`
- Patch catalog: `docs/patch-catalog.md`
