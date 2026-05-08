# Vanilla Tactical Battlefield Bug Queue

Narrow battlefield-layer bug queue for order delivery, W&L current-order handling, tactical fallback/retreat hazards, and battle AI movement defects. This is not the broad Slice B doctrine spec.

| ID | Status | Area | Evidence | Current action |
|---|---|---|---|---|
| `BUG-TAC-001` | Observed; no mismatch proof | Order courier queue | `Regiment.ProcessOrders()` processes queue index `i`, but secondary `AddOrderCourierline(...)` appends to `orderqueue[orderqueue.Count - 1]` at decompile line 125169. Focused smoke emitted five `[TacticalCourierQueue]` lines, all `risk=False` (`single-queue` or `unknown-index`). | Keep telemetry/soak. Patch only after runtime proof of the wrong queue index and explicit approval if a transpiler/replacement is needed. |
| `BUG-TAC-002` | Shipped; focused smoke clean | Fallback/retreat crash guard | `MicroAICheckForRetreats()` and `CheckLineFallbacks()` dereference `allattachedunits[i]` without null guards on hot tactical ticks; decompile anchors: lines 4817 and 5118. Focused smoke had no `[Patch:TacticalFallbackRetreatNullGuard]`, no exceptions, and no stack spam. | #43 `TacticalFallbackRetreatNullGuardPatch` remains included in the current #53 deployed DLL; keep the shipped default `false`. The local focused-smoke config can enable it, but there is no evidence yet to make it default-on. |
| `BUG-TAC-003` | Observed benign battle-call only | W&L current orders | `CheckCurrentOrderUpdate(... calledfromcampaign:true)` bypasses duplicate suppression and replaces `DLC_WL.givenorder`. Focused smoke emitted one `[TacticalCurrentOrder]` line with `calledFromCampaign=False` and `duplicateRisk=False`. | Need campaign-call proof before any duplicate guard. Do not patch the battle-call path that already has vanilla duplicate suppression. |
| `BUG-TAC-004` | Not observed | Delayed order path drift | `BattleUnits.SetWaypoint(...)` skips `AddToOrderQueue` when order type is active but still writes path intent. Focused smoke emitted no `[TacticalWaypointDrift]` lines. | Widen/prove the caller-specific path mutation before any behavior guard; do not patch generic `SetWaypoint` globally. |
| `BUG-TAC-005` | Guard implemented; denial smoke pending restart | Objective-chain exposure | `UpdateMovingTargets()` checks only center group `dlcw_isundercommander`, not attached player-subordinate units. Focused smoke emitted repeated `[TacticalObjectiveMove]` lines with `center=1st_Brigade`, `chains=4`, `attachedUnderCommanderCount=1`, and `risk=True`; user observed the attached player flank exposed near the second objective. | #46 `BattleObjectiveChainWlGuardPatch` remains included in the current #53 deployed DLL behind `Enable W&L Tactical Charge Guard`. It filters protected objective-chain entries during vanilla `UpdateMovingTargets` and restores them after the call. Restart smoke should show `[TacticalObjectiveGuard] denied objective-chain advance ... reason=player-subordinate-attached`. |
| `BUG-TAC-006` | Not observed | Reserve support | `CheckUseOfReserves()` uses direct `RegimentSetPath(...)`, bypassing order delay. Focused smoke emitted no `[TacticalReserveMove]` lines, although generic reserve observer coverage fired in earlier B0 smoke. | Widen reserve direct-path telemetry before doctrine/fix planning. Behavior change belongs to later reserve doctrine unless runtime proves pathological instant shifts. |
| `BUG-TAC-007` | Needs repro | W&L incident order delay | Incident 40 branch reads incident 38 timers in `AddOrderCourierline(...)`. | Verify incidents can be independently active before any transpiler request. |
| `BUG-TAC-008` | Backlog | Reserve candidate bias | `Random.Range(0, list.Count - 1)` likely excludes last reserve candidate. | Observe candidate counts/selected index first; do not mirror full reserve method for this alone. |
| `BUG-TAC-009` | Backlog | Relief doctrine gap | `CheckReliefOfObjectve(...)` is empty and `CheckReliefOfObjectveDueToLowMorale(...)` discards a boolean. | Later tactical doctrine; not a hotfix. |
| `BUG-TAC-010` | Implemented; enabled smoke pending | Pathfinder backtrack / excessive route shape | RMB field click reaches `BattleUI.CheckPathSetting()` -> `BattleUnits.SetWaypoint(...)` -> `Regiment.RegimentSetPath(...)`. Vanilla `AddPath(...)` retains non-empty non-target paths, rejects partial/invalid `NavMeshPath.status` only for iterations `<15`, then `RegimentSetPath` can retry from `Vector3.MoveTowards(lastCorner, target, -0.5f)`, i.e. away from the clicked target. Anchors: `AddPath` 130259, `NavMesh.CalculatePath` 130473, partial/invalid handling 130511-130519, retry loop 131167-131188. Live log includes `[TacticalPathShape] ... reason=backward-first-segment` for `7th_South_Carolina_Infantry`. | #53 `TacticalPathfinderDisciplinePatch` is included in deployed DLL `b07bbd39eaaf664d81d5930b42d5dc268b64c43dc98244a17015c49e329ce88a` behind `Enable Tactical Pathfinder Discipline`. It accepts complete endpoints within 5m, removes failed non-target fragments before the retry loop can reuse them, and rejects non-complete NavMesh paths accepted after retry exhaustion. Runtime enabled smoke should show bounded `[TacticalPathfinderDiscipline]` markers and fewer/no risky `[TacticalPathShape]` rows. |

## Runtime Evidence - 2026-05-07 Focused Battle Smoke

Focused W&L battle smoke ran with `Enable Tactical Observer = true`, `Enable Tactical Bug Telemetry = true`, `Enable Tactical Fallback Retreat Null Guard = true`, `Enable W&L Tactical Charge Guard = true`, verbose observer logging off, and summary throttle 30 seconds. That smoke ran on DLL SHA-256 `2a7bf702f6408e2a131a43fe6aa1a9ee04b56175ee292375c5167344c29c14c1` (519680 bytes). Current deployed DLL SHA-256 is `b07bbd39eaaf664d81d5930b42d5dc268b64c43dc98244a17015c49e329ce88a` (637952 bytes) and includes #46 objective-chain filtering, config-gated `[TacticalDecisionMatrix]` rows, B7+B8 artillery/withdrawal runtime wiring, and #53 pathfinder discipline. Live B4/B5 smoke emitted 37,250 matrix rows, 19 macro decisions, and 45 group decisions without exception spam, then fixed the observed no-measured-enemy weak-point inflation, matrix W&L/order-friction label inversion, B4 macro force-balance/no-sector inference, B5 defensive weak-point/probe fallback to hold, and B5 probing during vanilla retreat. Macro scoring now uses actual `AIBattle.unitsused` contact/sector evidence, no-contact tactical odds are zero, B5 maps defensive weak points plus explicit Probe sectors to stance 1 screening/probe outside retreat macro, and `macroai == 3` is skipped as vanilla-owned retreat. B4/B5 remain default-off in C# defaults because they write behavior-changing vanilla battle state. B7/B8 focused smoke currently has `Enable Tactical Artillery Doctrine = true` and `Enable Tactical Withdrawal Doctrine = true`; it confirmed `[once:b7-check-ai-bombardment]`, `[once:b7-counterbattery]`, `[once:b8-check-line-fallbacks]`, `[once:b8-morale-snapshot-sampler]`, `[once:b8-check-reserves]`, and `[once:b8-set-withdrawal]`, with no B7/B8 exception/Harmony/missing-anchor/error markers. The current live config also has `Enable Tactical Pathfinder Discipline = true` for focused #53 smoke. `[once:b8-microai-check-retreats]`, `[once:b7-cancel-bombard]`, `[TacticalObjectiveGuard]`, and `[TacticalPathfinderDiscipline]` remain pending.

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

- `BUG-TAC-005` is guarded in code now because live exposure matched the vanilla center-only W&L gap and the player observed flank exposure in battle. Runtime denial proof is still the key smoke marker.
- `BUG-TAC-001` telemetry fired but did not prove the wrong-queue bug.
- `BUG-TAC-003` telemetry fired only on a benign battle-call path.
- `BUG-TAC-010` now has runtime path-shape proof and #53 deployed behind `Enable Tactical Pathfinder Discipline`; enabled smoke is pending. #43 still did not exercise.

## Smoke Markers

- `[TacticalCurrentOrder]`
- `[TacticalWaypointDrift]`
- `[TacticalCourierQueue]`
- `[TacticalObjectiveMove]`
- `[TacticalObjectiveMutation]`
- `[TacticalObjectiveGuard]`
- `[TacticalDecisionMatrix]`
- `[TacticalReserveMove]`
- `[Patch:TacticalFallbackRetreatNullGuard]`
- `[once:b7-check-ai-bombardment]`
- `[once:b7-cancel-bombard]`
- `[once:b7-counterbattery]`
- `[once:b8-check-line-fallbacks]`
- `[once:b8-microai-check-retreats]`
- `[once:b8-morale-snapshot-sampler]`
- `[once:b8-check-reserves]`
- `[once:b8-set-withdrawal]`
- `[TacticalPathfinderDiscipline]`

## Rules

- Do not add a global behavior replacement or guard for `BattleUnits.SetWaypoint`; telemetry-only observers and the narrow #53 `Regiment.AddPath` discipline patch are allowed.
- Keep observer telemetry default-off behind `Enable Tactical Bug Telemetry`.
- Keep B1 charge/feud behavior under #41/#42.
- Keep B2 command/order friction read-only.
- Any transpiler requires explicit user approval.
