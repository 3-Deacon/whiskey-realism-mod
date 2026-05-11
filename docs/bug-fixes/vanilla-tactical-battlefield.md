# Vanilla Tactical Battlefield Bug Queue

Narrow battlefield-layer bug queue for order delivery, W&L current-order handling, tactical fallback/retreat hazards, and battle AI movement defects. This is not the broad Slice B doctrine spec.

| ID | Status | Area | Evidence | Current action |
|---|---|---|---|---|
| `BUG-TAC-001` | Observed; no mismatch proof | Order courier queue | `Regiment.ProcessOrders()` processes queue index `i`, but secondary `AddOrderCourierline(...)` appends to `orderqueue[orderqueue.Count - 1]` at decompile line 125169. Focused smoke emitted five `[TacticalCourierQueue]` lines, all `risk=False` (`single-queue` or `unknown-index`). | Keep telemetry/soak. Patch only after runtime proof of the wrong queue index and explicit approval if a transpiler/replacement is needed. |
| `BUG-TAC-002` | Shipped; focused smoke clean | Fallback/retreat crash guard | `MicroAICheckForRetreats()` and `CheckLineFallbacks()` dereference `allattachedunits[i]` without null guards on hot tactical ticks; decompile anchors: lines 4817 and 5118. Focused smoke had no `[Patch:TacticalFallbackRetreatNullGuard]`, no exceptions, and no stack spam. | #43 `TacticalFallbackRetreatNullGuardPatch` remains included in the current post-#53 deployed DLL; keep the shipped default `false`. The local focused-smoke config can enable it, but there is no evidence yet to make it default-on. |
| `BUG-TAC-003` | Observed benign battle-call only | W&L current orders | `CheckCurrentOrderUpdate(... calledfromcampaign:true)` bypasses duplicate suppression and replaces `DLC_WL.givenorder`. Focused smoke emitted one `[TacticalCurrentOrder]` line with `calledFromCampaign=False` and `duplicateRisk=False`. | Need campaign-call proof before any duplicate guard. Do not patch the battle-call path that already has vanilla duplicate suppression. |
| `BUG-TAC-004` | Not observed | Delayed order path drift | `BattleUnits.SetWaypoint(...)` skips `AddToOrderQueue` when order type is active but still writes path intent. Focused smoke emitted no `[TacticalWaypointDrift]` lines. | Widen/prove the caller-specific path mutation before any behavior guard; do not patch generic `SetWaypoint` globally. |
| `BUG-TAC-005` | Guard implemented; denial smoke pending restart | Objective-chain exposure | `UpdateMovingTargets()` checks only center group `dlcw_isundercommander`, not attached player-subordinate units. Focused smoke emitted repeated `[TacticalObjectiveMove]` lines with `center=1st_Brigade`, `chains=4`, `attachedUnderCommanderCount=1`, and `risk=True`; user observed the attached player flank exposed near the second objective. | #46 `BattleObjectiveChainWlGuardPatch` remains included in the current post-#53 deployed DLL behind `Enable W&L Tactical Charge Guard`. It filters protected objective-chain entries during vanilla `UpdateMovingTargets` and restores them after the call. Restart smoke should show `[TacticalObjectiveGuard] denied objective-chain advance ... reason=player-subordinate-attached`. |
| `BUG-TAC-006` | Observed; guard shipped default-off | Reserve support | `AIBattle.CheckUseOfReserves()` selects a reserve then calls `regiment2.RegimentSetPath(...)` directly at decompile line 6170, bypassing `BattleUnits.SetWaypoint(... useorderdelay:true)`. Live log on 2026-05-08 emitted `[TacticalReserveMove] group=2nd_Division ... risk=True reason=reserve-direct-path-bypasses-delay`. | #56 `TacticalReserveOrderDelayGuardPatch` is deployed behind `Enable Tactical Reserve Order Delay Guard`. It snapshots attached units, removes the immediate path created by vanilla, and reissues the same target through delayed `BattleUnits.SetWaypoint`. Enabled runtime proof pending. |
| `BUG-TAC-007` | Needs repro | W&L incident order delay | Incident 40 branch reads incident 38 timers in `AddOrderCourierline(...)`. | Verify incidents can be independently active before any transpiler request. |
| `BUG-TAC-008` | Backlog | Reserve candidate bias | `Random.Range(0, list.Count - 1)` likely excludes last reserve candidate. | Observe candidate counts/selected index first; do not mirror full reserve method for this alone. |
| `BUG-TAC-009` | Backlog | Relief doctrine gap | `CheckReliefOfObjectve(...)` is empty and `CheckReliefOfObjectveDueToLowMorale(...)` discards a boolean. | Later tactical doctrine; not a hotfix. |
| `BUG-TAC-010` | Implemented; enabled smoke pending | Pathfinder backtrack / excessive route shape | RMB field click reaches `BattleUI.CheckPathSetting()` -> `BattleUnits.SetWaypoint(...)` -> `Regiment.RegimentSetPath(...)`. Vanilla `AddPath(...)` retains non-empty non-target paths, rejects partial/invalid `NavMeshPath.status` only for iterations `<15`, then `RegimentSetPath` can retry from `Vector3.MoveTowards(lastCorner, target, -0.5f)`, i.e. away from the clicked target. Anchors: `AddPath` 130259, `NavMesh.CalculatePath` 130473, partial/invalid handling 130511-130519, retry loop 131167-131188. Live log includes `[TacticalPathShape] ... reason=backward-first-segment` for `7th_South_Carolina_Infantry`. | #53 `TacticalPathfinderDisciplinePatch` is included in deployed DLL `a5a6e1fd099d11d2ff5dc6fd460d91e4e98a26a6f405df9d4b5dbfc808ed0d38` behind `Enable Tactical Pathfinder Discipline`. It accepts complete endpoints within 5m, removes failed non-target fragments before the retry loop can reuse them, and rejects non-complete NavMesh paths accepted after retry exhaustion. Runtime enabled smoke should show bounded `[TacticalPathfinderDiscipline]` markers and fewer/no risky `[TacticalPathShape]` rows. |
| `BUG-TAC-011` | Guard shipped default-on | W&L operation cleanup | `DLC_WL.CommanderRelations.Operation.UpdateOperation()` reads `usedtopgroup.transform.position` before its null cleanup branch. Anchor: decompile lines 41142-41149. A missing operation unit can throw before `FinishOperation()` runs. | #54 `WlOperationNullGuardPatch` finalizes only that null-before-transform fault, invokes vanilla private `FinishOperation()`, sets the method result to complete, and suppresses the exception. Config `Enable Operation Null Guard` defaults true. |
| `BUG-TAC-012` | Guard shipped default-off | HQ/group follow links | `Regiment.MoveNonAIUnits()` auto-links idle group units (`unittyp > 13`) to `unitrange.closestownunit[0]` without checking command hierarchy. Anchor: decompile lines 129026-129049. Direct `UpdateUnitLink()` then moves the linked group toward that unit. | #55 `TacticalHqAutoLinkGuardPatch` snapshots newly-created auto links and clears cross-command group/HQ links while preserving same-hierarchy, same non-root parent, and same AI-group links. Config `Enable Tactical HQ Link Guard` defaults false until focused smoke proves no unintended command-link loss. |
| `BUG-TAC-013` | Implemented; enabled smoke pending | AI deployment terrain/facing | Vanilla `BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew` places AI groups through immediate formation placement, then runs deployment-zone and water correction surfaces that can still leave in-bounds water/edge/odd placements. Vanilla final facing is caller/path-tail driven unless a manual rotation is supplied; closest-enemy fields are not sufficient Whiskey evidence without visible-enemy filtering. Anchors: `DoPlacementAIUnitsWithinDeploymentzoneNew` 85524-85872, vanilla cleanup 85873-85880, `SetGroupFormation` 91815/92056, water checks 131432, visible enemy lists 122545. | #58 now emits bounded `[TacDeployTerrain]` evidence. #60 `TacticalDeploymentTerrainDisciplinePatch` was verified in DLL `b00e03bd7e635e981380459e09a0d52a19d635c22c49bd340b403dacfbdf4cf8` (841216 bytes) and is included in the current `main` DLL `d634f46e74aeae205b3a8b4763e556bc8782214c423cfcef72cdd27dac3b5330` (887808 bytes), C# default-off behind `Enable Tactical Deployment Terrain Discipline`, and enabled in the live local config for focused smoke. It corrects only clear AI terrain/deployment-zone failures through vanilla `SetGroupFormation`, then mirrors vanilla cleanup for the corrected group tree. Living reference: `../tactical-terrain-facing-discipline.md`. |

## Runtime Evidence - 2026-05-08 HQ/Reserve Follow Investigation

Live `LogOutput.log` mtime `2026-05-08 15:31:59 -0500`, size `37,858,853` bytes, proved the current tactical build was active and captured a reserve delay bypass after the prior smoke had missed it:

- `[TacticalReserveMove] group=2nd_Division#-26790 pathBefore=1 pathAfter=2 queueBefore=3 queueAfter=3 risk=True reason=reserve-direct-path-bypasses-delay`
- `[TacticalPathfinderDiscipline] reason=navmesh-noncomplete ... navStatus=PathPartial`
- `[TacticalPathfinderDiscipline] reason=endpoint-within-tolerance ... navStatus=PathComplete`

No operation-null exception was present in the searched log window; `BUG-TAC-011` is fixed from decompile proof because the null branch is unreachable after the transform read.

## Runtime Evidence - 2026-05-07 Focused Battle Smoke

Focused W&L battle smoke ran with `Enable Tactical Observer = true`, `Enable Tactical Bug Telemetry = true`, `Enable Tactical Fallback Retreat Null Guard = true`, `Enable W&L Tactical Charge Guard = true`, verbose observer logging off, and summary throttle 30 seconds. That smoke ran on DLL SHA-256 `2a7bf702f6408e2a131a43fe6aa1a9ee04b56175ee292375c5167344c29c14c1` (519680 bytes). The current `main` deployed DLL SHA-256 is `d634f46e74aeae205b3a8b4763e556bc8782214c423cfcef72cdd27dac3b5330` (887808 bytes) and includes #46 objective-chain filtering, config-gated `[TacticalDecisionMatrix]` rows, B6c local reaction/reserve intent runtime wiring, B7+B8 artillery/withdrawal runtime wiring, B5 queued/pending-order settlement gating, tiny-contact weak-point suppression, B5 defensive-hold/brigade-scope discipline, #53 pathfinder discipline, #54 W&L operation null guard, #55 HQ auto-link guard, #56 reserve order-delay guard, #57 reserve-list bias, #58 deployment terrain/facing telemetry, #59 reserve commitment gate, #41 orchestrator charge gate, #60 deployment terrain discipline, and #61 operations-ledger posture execution. Live B4/B5 smoke emitted 37,250 matrix rows, 19 macro decisions, and 45 group decisions without exception spam, then fixed the observed no-measured-enemy weak-point inflation, matrix W&L/order-friction label inversion, B4 macro force-balance/no-sector inference, B5 probing during vanilla retreat, B5 stance-order stacking while vanilla had queued/pending order state, B5 defensive weak-point/fix probes under defensive macro, and B5 writes to division/army top groups. Macro scoring now uses actual `AIBattle.unitsused` contact/sector evidence, no-contact tactical odds are zero, B5 maps defensive `AttackWeakPoint`, `Fix`, and `EconomyOfForce` sectors to stance 2 `defend-hold`, B5 only writes local stance to battle brigade groups (`unittyp == 14`), `macroai == 3` is skipped as vanilla-owned retreat, B5 stance retasks fail closed on unsettled vanilla order state, and tiny angle-slice contacts cannot become decisive weak points. B4/B5 remain default-off in C# defaults because they write behavior-changing vanilla battle state, while #61 operations-ledger command mode defaults `Active` with `MonitorOnly` for diagnostics and `Off` for rollback. B6a/B6c telemetry, B7 artillery, B8 withdrawal, W&L tactical guard, bug telemetry, decision matrix, fallback guard, pathfinder discipline, HQ link guard, reserve order-delay guard, operation null guard, and #60 deployment terrain discipline can still be enabled in local focused-smoke config. B7/B8 focused smoke confirmed `[once:b7-check-ai-bombardment]`, `[once:b7-counterbattery]`, `[once:b8-check-line-fallbacks]`, `[once:b8-morale-snapshot-sampler]`, `[once:b8-check-reserves]`, and `[once:b8-set-withdrawal]`, with no B7/B8 exception/Harmony/missing-anchor/error markers. Fresh runtime proof on the current DLL is still needed for `[TacticalOpsLedger]`, `[TacticalCommandAssignment]`, `[TacticalCommandPosture]`, `[TacticalPostureSummary]`, B5 settlement skips, tiny-contact suppression, `defend-hold` / `command-scope` rows, `[TacticalPathfinderDiscipline]`, `[TacticalLocalReaction]`, `[TacticalReserveIntent]`, `[TacticalHqLinkGuard]`, `[TacticalReserveOrderDelayGuard]`, `[TacDeployTerrain]`, `[TacDeployTerrainAdvice]`, `[TacticalOrchestratorChargeGate]`, conditional `[Patch:WLOperationNullGuard]`, risky `[TacticalPathShape]` deltas, `[TacticalIntent]`/`[TacticalPlaybook]`, `[once:b8-microai-check-retreats]`, `[once:b7-cancel-bombard]`, and `[TacticalObjectiveGuard]`.

Observed marker counts:

- `[TacticalPlayerOrder]`: 82
- `[TacticalCommand]`: 25
- `[TacticalOrder]`: 116
- `[TacticalCurrentOrder]`: 1
- `[TacticalCourierQueue]`: 5
- `[TacticalObjectiveMove]`: 11
- `[TacticalWaypointDrift]`: 0
- `[TacticalReserveMove]`: 0 during this smoke; later 2026-05-08 log produced a `risk=True` reserve-delay bypass marker.
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
- `[TacticalReserveOrderDelayGuard]`
- `[TacticalHqLinkGuard]`
- `[TacDeployTerrain]`
- `[TacDeployTerrainAdvice]`
- `[Patch:WLOperationNullGuard]`
- `[Patch:TacticalFallbackRetreatNullGuard]`
- `[once:b7-check-ai-bombardment]`
- `[once:b7-cancel-bombard]`
- `[once:b7-counterbattery]`
- `[once:b8-check-line-fallbacks]`
- `[once:b8-microai-check-retreats]`
- `[once:b8-morale-snapshot-sampler]`
- `[once:b8-check-reserves]`
- `[once:b8-set-withdrawal]`

## Rules

- Do not add a global behavior replacement or guard for `BattleUnits.SetWaypoint`; telemetry-only observers are allowed.
- Keep observer telemetry default-off behind `Enable Tactical Bug Telemetry`.
- Keep B1 charge/feud behavior under #41/#42.
- Keep B2 command/order friction read-only.
- Any transpiler requires explicit user approval.
