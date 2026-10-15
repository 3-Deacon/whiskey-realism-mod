# W&L Dispatch Objective Bridge

Living reference for Whiskey's W&L campaign-map dispatch/order bridge. Source of truth remains shipped code first, then `docs/patch-catalog.md`, then this note. The archived design and plan are historical context only:

- `docs/superpowers/specs/archive/2026-05-06-wl-dispatch-objective-bridge-design.md`
- `docs/superpowers/plans/archive/2026-05-06-wl-dispatch-objective-bridge.md`

## Purpose

Vanilla Whiskey & Lemons already generates campaign dispatches and current orders for the command chain. The player can begin as a subordinate with no campaign-map control, later become an independent map unit, and eventually command divisions, corps, or the army. Whiskey should use vanilla's dispatch/current-order system where that chain is valid, not direct-move player-chain units behind the player's back.

This bridge exists for that boundary:

- sanitize vanilla "to none" / stance-0 W&L dispatch text;
- give a player-owned top-level map group Whiskey's strategic order as a W&L dispatch, never a move;
- tell a subordinate player (brigade/division inside a corps) about the moves vanilla makes with the group that carries him (stage F, #69);
- make rejected W&L player-chain movement visible as a skip, not a hidden direct movement fallback;
- leave non-W&L and non-player-alliance movement on the normal vanilla movement path.

## Implementation

Core files:

- `src/WhiskeyRealism/Patches/DispatchStanceSanitizerPatch.cs`
- `src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs` (classification)
- `src/WhiskeyRealism/Strategic/WlCampaignDispatchPolicy.cs` (pure: trigger, type, dedupe) and `WlCampaignDispatchRuntime.cs` (the one campaign dispatcher)
- `src/WhiskeyRealism/Patches/WlCampaignMoveDispatchPatch.cs` (#69)
- callers in `OperationalProbeRuntime`, `ArmyAreaRuntime`, `CoastalDefenseCustomOrderRunner`, and `CoordinatedOperationRuntime`

Patch catalog entries:

- #36 `DispatchStanceSanitizerPatch`
- #69 `WlCampaignMoveDispatchPatch`
- unnumbered runtime rows `WlStrategicOrderBridge` + `WlCampaignDispatchRuntime`, `PlayerComplianceRuntime`

Dispatcher (chain-of-command spec §D, decompile build 14703111):

- **Who receives what.** Vanilla's campaign gate (`AIBattle.CheckCurrentOrderUpdate` 8366) passes only orders to the player's own campaign-map group. The bridge therefore dispatches only to such a player (`IsUnderCommander` + own map group + the ordered unit at or above his parent); #69 covers the subordinate, whose group vanilla moves with `AICampaign.MoveUnitTo` (14479).
- **Type (#69).** Classified after the vanilla caller listed the unit (an `UpdateUnitAI` Postfix): defensive operation → 7, capital defense → 8 (capital position and name), continuing offensive → 6, new offensive → 16 with enemy strength in the target area else 5, anything else → 5. The bridge uses `WlOrderTypeForIntent`.
- **Emission.** Vanilla's own tail (8398-8664): group unit = the player's parent unit, zone widened to it and to `dlcwl_minimumobjectivezone`, the type's tooltip text, `new GivenOrders(...)`, `givenorderssession++`, `CareerInformationPanel.ShowNewOrder`. Issuer: the moving formation's commander (#69) or `DLC_WL.GetSeniorCommander()` (bridge). Compliance and prestige stay vanilla's (`CheckDLCWLTopics`).
- **Dedupe.** Same type and target (110) as the active order, or any order to the same target within one game day, is skipped; a move shorter than 20 is no order. This also absorbs the second `MoveUnitTo` call of a queued move (10048).

## Invariants

- No direct movement of a W&L player-owned unit, and no movement of the subordinate player's parent group by Whiskey (`IsPlayerPartOfUnit` → ineligible).
- No Transpiler.
- No edit to `GameVars.groupstancename[0]`.
- No direct movement fallback for W&L player-chain/player-involved units when the bridge rejects, fails, or the dispatcher skips.
- Null requests fail closed: no direct movement and no operation-list mutation.
- `IsMovedByPlayer`, player-CIC, and `IsPlayerPartOfUnit` cases block Whiskey movement.
- `IssuedWlCurrentOrder` does not mutate vanilla offensive/defensive operation lists.
- Failed or skipped dispatches log and skip; callers must not reattempt direct movement for that unit in the same path.
- Rollback: `[W&L] Enable Campaign Chain Dispatches = false` (no Whiskey campaign orders; the bridge reports `campaign-dispatch:dispatch-disabled`).

## Runtime Evidence

Deployed build, harness count and smoke status live in [`handoff.md`](handoff.md). The 2026-10-14 Steam update removed the previous deployment, so no runtime evidence from earlier DLLs carries over.

Fresh gameplay smoke for newly generated `[W&LDispatch]` sanitizer lines, `[W&LCampaignDispatch] ... action=issue` lines (subordinate career: the corps moves; top-level career: a Whiskey order to the player's corps), and absence of newly generated `"to none"` text remains pending on the current DLL. Do not claim runtime text/order success until `BepInEx/LogOutput.log` comes from a restart after the current deploy.
