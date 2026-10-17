# W&L Dispatch Objective Bridge

Living reference for Whiskey's W&L campaign-map dispatch/order bridge. Source of truth remains shipped code first, then `docs/patch-catalog.md`, then this note. The archived design and plan are historical context only:

- `docs/superpowers/specs/archive/2026-05-06-wl-dispatch-objective-bridge-design.md`
- `docs/superpowers/plans/archive/2026-05-06-wl-dispatch-objective-bridge.md`

## Purpose

Vanilla Whiskey & Lemons already generates campaign dispatches and current orders for the command chain. The player can begin as a subordinate with no campaign-map control, later become an independent map unit, and eventually command divisions, corps, or the army. Whiskey should use vanilla's dispatch/current-order system where that chain is valid, not direct-move player-chain units behind the player's back.

This bridge exists for that boundary:

- sanitize vanilla "to none" / stance-0 W&L dispatch text;
- give a player-owned top-level map group Whiskey's strategic order as a W&L dispatch, never a move;
- make rejected W&L player-chain movement visible as a skip, not a hidden direct movement fallback;
- leave non-W&L and non-player-alliance movement on the normal vanilla movement path.

A subordinate player (a brigade or division inside a corps: his command is not its own map group) is sent no campaign order, by vanilla (gate 8366) or by Whiskey: the corps that carries him marches without a word. Stage F's #69 once dispatched the carrying group's marches to him; that was removed 2026-10-16 (user report: orders on the campaign map for a player who cannot move himself).

## Implementation

Core files:

- `src/WhiskeyRealism/Patches/DispatchStanceSanitizerPatch.cs`
- `src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs` (classification)
- `src/WhiskeyRealism/Strategic/WlCampaignDispatchPolicy.cs` (pure: the rate limit and duplicate rule) and `WlCampaignDispatchRuntime.cs` (the one campaign dispatcher, Whiskey's order to a map-group player)
- `src/WhiskeyRealism/Patches/WlCampaignOrderPassPatch.cs` (#69; its `UpdateUnitAI` Postfix clears a carried player's leftover campaign order, runs the courier delivery and the acknowledgement, and feeds the compliance observation)
- `src/WhiskeyRealism/Strategic/WlCampaignCourierPolicy.cs` (pure) and `WlCampaignCourierRuntime.cs` (campaign courier queue, delivery, sidecar), `Patches/CapitalDefensePlayerSpeedFixPatch.cs`
- callers: the campaign planner's `CampaignMoverRuntime` and `ReconExecutorRuntime` (through `VanillaMoveHost`), `CommanderAppointmentRuntime` and `CoastalDefenseCustomOrderRunner` (the probe, army-area and package runtimes were deleted in stage G6)

Patch catalog entries:

- #36 `DispatchStanceSanitizerPatch`
- #69 `WlCampaignOrderPassPatch`
- unnumbered runtime rows `WlStrategicOrderBridge` + `WlCampaignDispatchRuntime`, `PlayerComplianceRuntime`

Dispatcher (chain-of-command spec §D, decompile build 14703111):

- **Who receives what.** Vanilla's campaign gate (`AIBattle.CheckCurrentOrderUpdate` 8366) passes only orders to the player's own campaign-map group. The bridge therefore dispatches only to such a player (`IsUnderCommander` + own map group + the ordered unit at or above his parent). A subordinate gets nothing: the group that carries him is moved by vanilla (`AICampaign.MoveUnitTo`, 14479) or by a Whiskey mover, and no order follows (removed 2026-10-16).
- **Type.** The bridge's `WlOrderTypeForIntent` (Redeploy/Probe/Reinforce 5, OffensiveContinuation 6, EngageEnemy 7, DefendCapital 8, ConstructFort 9, ConstructSupplyDepot 10, Offensive 16).
- **Emission.** Vanilla's own tail (8398-8664): group unit = the player's parent unit, zone widened to it and to `dlcwl_minimumobjectivezone`, the type's tooltip text, `new GivenOrders(...)`, `givenorderssession++`, `CareerInformationPanel.ShowNewOrder`. Issuer: `DLC_WL.GetSeniorCommander()`. Zone, compliance and prestige stay vanilla's (`CheckDLCWLTopics`).
- **Couriers (order flow, branch `fix/wl-order-flow`).** `Emit` decides as above and then queues the order on the campaign courier queue (`WlCampaignCourierRuntime`, origin `CampaignBridge`; a saved `C69` entry from a build before 2026-10-16 is dropped on load) instead of showing it: the delay is vanilla's campaign order delay (`recognition x max(10, sqrt(d) x 5) / 1000` times the sending headquarters' telegraph, winter-quarters, entrenchment, army-group, readiness and perk factors, plus the processing time; campaign years) from the senior commander's command to the player's group; `Enable W&L Order Couriers` off shows the order at once, as before. `ShowQueued` builds and shows the order when the courier arrives (the `UpdateUnitAI` Postfix, #69's order pass), re-deciding it with `WlCampaignDispatchPolicy.Decide`, and only then writes the dispatch history, so an entry is never rate-limited against its own target. The queue (cadence keyed by issuer, type, origin **and target**, window one game day; 110 en-route test) is saved in the sidecar (`campaignCouriers`, unit as GameObject name plus commander id) so a save inside the delay loses no order, a battle round trip included (the return from a battle is a sidecar load). Each entry also carries the player's command it was queued for (unit GameObject name + chosen commander id): a promotion voids it (`command-change`), at save time as well as after a load; a Whiskey order is also dropped (`carried-player`, never shown) when the player was transferred under a corps while it rode (`PreDeliveryDropReason`); an entry from an older sidecar is stamped with the current command on the first delivery pass. Vanilla's own campaign calls to a top-level player ride the same queue and a re-pop of the order already on the panel is dropped. A reached type 5/6/7/16 order is acknowledged through `ShowNewInfo` (`Enable W&L Order Follow-Up`). See [`wl-player-order-doctrine.md`](wl-player-order-doctrine.md) § Order flow.
- **Dedupe.** Same type and target (110) as the active order, or any of the last four targets within one game day (`WlDispatchHistory`), is skipped; a target within 20 of the group is no order.

## Invariants

- One classifier, `WlChainRules` (stage G decision C4, applied in stage F 2026-10-15): a CIC side → Whiskey stands down; the player's own command and units under it (player-owned, in his operation, inside the group carrying him, an army-group player's units) → never moved, stopped or re-tasked; the player's command is itself a map group → the planner sends him no move and no order (smoke amendments 2026-10-16 D3), never pledged, packaged or given a must-hold role (`MayMove` false), yet counted toward the coverage of the area its last waypoint lies in (`CountsTowardCoverage`); the AI map group above a player below map-group level → moved as an AI asset (`DirectMovementAllowed`, reason `carrying-group`) through `AICampaign.MoveUnitTo`, with no order to the player it carries.
- Planner invariants (stage G): the planner stands down for a CIC player's alliance; a map-group player is neither moved nor ordered by the planner (smoke amendments 2026-10-16 D3 removed his Support order: he holds where he stands, never MainEffort, picket or recon); the AI group above a player below map level is movable as any AI formation and its player is told nothing; the player's own command and units under it are never in a recon mission or an appointment.
- No Transpiler.
- No edit to `GameVars.groupstancename[0]`.
- No direct movement fallback for W&L player-chain/player-involved units when the bridge rejects, fails, or the dispatcher skips.
- Null requests fail closed: no direct movement and no operation-list mutation.
- `IsMovedByPlayer`, player-CIC, player-owned and `IsPlayerPartOfUnit` cases block Whiskey movement, except the carrying group itself (`IsPlayerPartOfUnit` and `GetCampaignGroup(command) == unit` while the command is below map-group level). The same classifier gates #4's capital pre-pledge, #25's forbidden-commit revert, the coastal runner, the planner's mover, recon executor and appointments, and defense coverage (`MayMove` for what Whiskey may commit; `CountsTowardCoverage` for what already covers an area).
- Coverage decision (integrator, user delegated 2026-10-15), verbatim: "The W&L map-group player's group counts at full strength (strength x morale) toward coverage/holding of the area where it actually is or where its last waypoint lies, as vanilla does. It is never pre-pledged (#4), never assigned a must-hold role or moved, and is never counted as a package member Whiskey commits; if the player marches away, the next review sees the gap. Compliance reliability stays telemetry/battle-evidence only."
- `IssuedWlCurrentOrder` does not mutate vanilla offensive/defensive operation lists.
- Failed or skipped dispatches log and skip; callers must not reattempt direct movement for that unit in the same path.
- Rollback: `[W&L] Enable Campaign Chain Dispatches = false` (no Whiskey campaign orders; the bridge reports `campaign-dispatch:dispatch-disabled`).

## Runtime Evidence

Deployed build, harness count and smoke status live in [`handoff.md`](handoff.md). The 2026-10-14 Steam update removed the previous deployment, so no runtime evidence from earlier DLLs carries over.

Fresh gameplay smoke for newly generated `[W&LDispatch]` sanitizer lines, `[W&LCampaignDispatch] ... action=issue` lines (subordinate career: the corps moves and no order appears; top-level career: a Whiskey order to the player's corps), and absence of newly generated `"to none"` text remains pending on the current DLL. Do not claim runtime text/order success until `BepInEx/LogOutput.log` comes from a restart after the current deploy.
