# W&L Dispatch Objective Bridge

Living reference for Whiskey's W&L campaign-map dispatch/order bridge. Source of truth remains shipped code first, then `docs/patch-catalog.md`, then this note. The archived design and plan are historical context only:

- `docs/superpowers/specs/archive/2026-05-06-wl-dispatch-objective-bridge-design.md`
- `docs/superpowers/plans/archive/2026-05-06-wl-dispatch-objective-bridge.md`

## Purpose

Vanilla Whiskey & Lemons already generates campaign dispatches and current orders for the command chain. The player can begin as a subordinate with no campaign-map control, later become an independent map unit, and eventually command divisions, corps, or the army. Whiskey should use vanilla's dispatch/current-order system where that chain is valid, not direct-move player-chain units behind the player's back.

This bridge exists for that boundary:

- sanitize vanilla "to none" / stance-0 W&L dispatch text;
- route eligible player-chain strategic orders through `AIBattle.CheckCurrentOrderUpdate(..., calledfromcampaign:true)`;
- make rejected W&L player-chain movement visible as a skip, not a hidden direct movement fallback;
- leave non-W&L and non-player-alliance movement on the normal vanilla movement path;
- share current-order signature, provenance, and cross-scope suppression with #62 W&L player-order doctrine.

## Implementation

Core files:

- `src/WhiskeyRealism/Patches/DispatchStanceSanitizerPatch.cs`
- `src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs`
- callers in `OperationalProbeRuntime`, `ArmyAreaRuntime`, `CoastalDefenseCustomOrderRunner`, and `CoordinatedOperationRuntime`

Patch catalog entries:

- #36 `DispatchStanceSanitizerPatch`
- unnumbered runtime row `WlStrategicOrderBridge`

## Invariants

- No global `MoveUnitTo` patch.
- No Transpiler.
- No edit to `GameVars.groupstancename[0]`.
- No direct movement fallback for W&L player-chain/player-involved units when the bridge rejects, fails, or cannot prove the vanilla current-order chain.
- Null requests fail closed: no direct movement and no operation-list mutation.
- `IsMovedByPlayer`, player-CIC, and `IsPlayerPartOfUnit` cases block Whiskey movement.
- `IssuedWlCurrentOrder` does not mutate vanilla offensive/defensive operation lists.
- Failed bridge calls log and skip; callers must not reattempt direct movement for that unit in the same path.
- Campaign signatures are cached so identical bridge requests do not refresh every caller cadence.
- Fresh tactical player-order shadows are not replaced by campaign orders unless the shared cross-scope priority/stale-order rule allows it.
- Failed vanilla bridge attempts are recorded for cooldown; failure is not an invitation to fall back to direct movement.

## Runtime Evidence

The bridge implementation was build/deploy/hash verified in the historical-operation DLL `c90a5873e23ad1e7c0ac34e9c9b5cbad5554c0a5a2ee3fcc2aef299394366e0b` (481280 bytes). Current player-order worktree DLL is `5398831614e855f90b156c734fee50348d0bb6b10c8f0390b7a3d2499ba57740` (977920 bytes; 898 PASS), which includes later tactical orchestrator, deployment, terrain/facing, operations-ledger, and #62 W&L player-order work.

Fresh gameplay smoke for newly generated `[W&LDispatch]` sanitizer lines, bridge-order lines, and absence of newly generated `"to none"` text remains pending on the current DLL. Do not claim runtime text/order success until `BepInEx/LogOutput.log` comes from a restart after the current deploy.

Related current-order doctrine: [`docs/wl-player-order-doctrine.md`](wl-player-order-doctrine.md).
