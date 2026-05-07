# Vanilla Tactical Battlefield Bug Queue

Narrow battlefield-layer bug queue for order delivery, W&L current-order handling, tactical fallback/retreat hazards, and battle AI movement defects. This is not the broad Slice B doctrine spec.

| ID | Status | Area | Evidence | Current action |
|---|---|---|---|---|
| `BUG-TAC-001` | Observed; no mismatch proof | Order courier queue | `Regiment.ProcessOrders()` processes queue index `i`, but secondary `AddOrderCourierline(...)` appends to `orderqueue[orderqueue.Count - 1]` at decompile line 125169. Focused smoke emitted five `[TacticalCourierQueue]` lines, all `risk=False` (`single-queue` or `unknown-index`). | Keep telemetry/soak. Patch only after runtime proof of the wrong queue index and explicit approval if a transpiler/replacement is needed. |
| `BUG-TAC-002` | Shipped; focused smoke clean | Fallback/retreat crash guard | `MicroAICheckForRetreats()` and `CheckLineFallbacks()` dereference `allattachedunits[i]` without null guards on hot tactical ticks; decompile anchors: lines 4817 and 5118. Focused smoke had no `[Patch:TacticalFallbackRetreatNullGuard]`, no exceptions, and no stack spam. | #43 `TacticalFallbackRetreatNullGuardPatch` is built/deployed in DLL `9136d14fbea7b2ace5ba034dc673f71b31de2b9d8467c159c49cdbd9052513bd`; keep the shipped default `false`. The local focused-smoke config can enable it, but there is no evidence yet to make it default-on. |
| `BUG-TAC-003` | Observed benign battle-call only | W&L current orders | `CheckCurrentOrderUpdate(... calledfromcampaign:true)` bypasses duplicate suppression and replaces `DLC_WL.givenorder`. Focused smoke emitted one `[TacticalCurrentOrder]` line with `calledFromCampaign=False` and `duplicateRisk=False`. | Need campaign-call proof before any duplicate guard. Do not patch the battle-call path that already has vanilla duplicate suppression. |
| `BUG-TAC-004` | Not observed | Delayed order path drift | `BattleUnits.SetWaypoint(...)` skips `AddToOrderQueue` when order type is active but still writes path intent. Focused smoke emitted no `[TacticalWaypointDrift]` lines. | Widen/prove the caller-specific path mutation before any behavior guard; do not patch generic `SetWaypoint` globally. |
| `BUG-TAC-005` | Runtime-confirmed exposure; needs movement proof | Objective-chain exposure | `UpdateMovingTargets()` checks only center group `dlcw_isundercommander`, not attached player-subordinate units. Focused smoke emitted eleven repeated `[TacticalObjectiveMove]` lines with `center=1st_Brigade#-30060`, `chains=4`, `attachedUnderCommanderCount=1`, and `risk=True`. | Current deployed DLL adds `[TacticalObjectiveMutation]` before/after proof telemetry around the exposed center and attached player-subordinate units. Do not add behavior until mutation proof exists. |
| `BUG-TAC-006` | Not observed | Reserve support | `CheckUseOfReserves()` uses direct `RegimentSetPath(...)`, bypassing order delay. Focused smoke emitted no `[TacticalReserveMove]` lines, although generic reserve observer coverage fired in earlier B0 smoke. | Widen reserve direct-path telemetry before doctrine/fix planning. Behavior change belongs to later reserve doctrine unless runtime proves pathological instant shifts. |
| `BUG-TAC-007` | Needs repro | W&L incident order delay | Incident 40 branch reads incident 38 timers in `AddOrderCourierline(...)`. | Verify incidents can be independently active before any transpiler request. |
| `BUG-TAC-008` | Backlog | Reserve candidate bias | `Random.Range(0, list.Count - 1)` likely excludes last reserve candidate. | Observe candidate counts/selected index first; do not mirror full reserve method for this alone. |
| `BUG-TAC-009` | Backlog | Relief doctrine gap | `CheckReliefOfObjectve(...)` is empty and `CheckReliefOfObjectveDueToLowMorale(...)` discards a boolean. | Later tactical doctrine; not a hotfix. |

## Runtime Evidence - 2026-05-07 Focused Battle Smoke

Focused W&L battle smoke ran with `Enable Tactical Observer = true`, `Enable Tactical Bug Telemetry = true`, `Enable Tactical Fallback Retreat Null Guard = true`, `Enable W&L Tactical Charge Guard = true`, verbose observer logging off, and summary throttle 30 seconds. That smoke ran on DLL SHA-256 `2a7bf702f6408e2a131a43fe6aa1a9ee04b56175ee292375c5167344c29c14c1` (519680 bytes). Current deployed DLL SHA-256 is `9136d14fbea7b2ace5ba034dc673f71b31de2b9d8467c159c49cdbd9052513bd` (524288 bytes) and adds `[TacticalObjectiveMutation]` proof telemetry.

Observed marker counts:

- `[TacticalPlayerOrder]`: 82
- `[TacticalCommand]`: 25
- `[TacticalOrder]`: 116
- `[TacticalCurrentOrder]`: 1
- `[TacticalCourierQueue]`: 5
- `[TacticalObjectiveMove]`: 11
- `[TacticalWaypointDrift]`: 0
- `[TacticalReserveMove]`: 0
- `[Patch:TacticalFallbackRetreatNullGuard]`: 0
- `[TacticalChargeGuard]`: 0
- `[TacticalFeudGuard]`: 0
- old `Regiment.side` Harmony warning: 0
- `Exception`, `TargetInvocationException`, `missing-anchor`, `failed-owned`: 0

Classification:

- `BUG-TAC-005` is the only runtime-confirmed risk from this smoke. It proves objective-chain player-subordinate attachment exposure, not actual movement/path mutation yet.
- `BUG-TAC-001` telemetry fired but did not prove the wrong-queue bug.
- `BUG-TAC-003` telemetry fired only on a benign battle-call path.
- `BUG-TAC-004`, `BUG-TAC-006`, and #43 did not exercise their risk markers.

## Smoke Markers

- `[TacticalCurrentOrder]`
- `[TacticalWaypointDrift]`
- `[TacticalCourierQueue]`
- `[TacticalObjectiveMove]`
- `[TacticalObjectiveMutation]`
- `[TacticalReserveMove]`
- `[Patch:TacticalFallbackRetreatNullGuard]`

## Rules

- Do not add a global behavior replacement or guard for `BattleUnits.SetWaypoint`; telemetry-only observers are allowed.
- Keep observer telemetry default-off behind `Enable Tactical Bug Telemetry`.
- Keep B1 charge/feud behavior under #41/#42.
- Keep B2 command/order friction read-only.
- Any transpiler requires explicit user approval.
