# Bug Fixes

Cross-cutting workstream for vanilla bug fixes and narrow runtime guards that do not belong to a new realism slice.

Use this area when the goal is to correct a concrete vanilla failure mode, crash hazard, pathological AI loop, broken one-shot UI flow, or unsafe economic/construction behavior. Use `docs/superpowers/specs/` instead when the work adds new doctrine, broad behavior, or multiple interacting systems.

## Index

| ID | Status | Area | Evidence | Current action |
|---|---|---|---|---|
| `BUG-ECO-001` | In progress | Fiscal AI | Live `LogOutput.log` showed `[Patch:FinancialAI] alliance=1 subsidyLane=3 ... new=-1.00`; surface is #18 `FinancialAIPatch` over vanilla `AICampaign.UpdateFinancialAI` at decompile line 15352. Regression tests cover disabled focus and already-negative saved subsidy values. | Build/deploy/hash verified in DLL `ce17af99...`; fresh-launch smoke before marking shipped. |
| `BUG-ECO-002` | Needs repro | Policy AI | Vanilla `Policies.CheckAIPolicyChange` dereferences `aIPersonality.id` without a null guard at decompile lines 211023-211024. | Reproduce or add a no-op safe guard only if startup evidence shows risk. |
| `BUG-ECO-003` | In progress | Economy tick | Current Player.log has 27,100 `Economy.UpdateEconomyAllianceData` NRE hits at `[0x00a30]` through `Economy.UpdateFilterMaps` / `BattleUnits.Update`; owner method starts at decompile line 32344. | #28 Finalizer suppresses only the vanilla NRE so `UpdateFilterMaps` can advance its iterator; build/deploy/hash verified in DLL `ce17af99...`; fresh-launch smoke before marking shipped. |
| `BUG-ECO-004` | Backlog | Supply depots | Vanilla `AICampaign.CheckSupplyDepotConstruction` can move low-supply units or call `CBuilding.AddConstructionWish` directly at decompile lines 14659 and 14772. | Add telemetry/design before steering; preserve vanilla unit eligibility and construction queue. |
| `BUG-ECO-005` | Backlog | Railroads | Vanilla `AICampaign.UpdateRailroadConstruction` randomly attempts every unstarted railroad at decompile lines 16052-16072; `Railroad.StartConstruction` only checks ownership/permitted state at 77818-77835. | Consider a filter/observer after construction telemetry proves bad starts. |
| `BUG-TICK-001` | In progress | Campaign tick | Vanilla `AICampaign.Update` still runs one `UpdateUnitAI()` pass while paused because `Mathf.Max(1, floor(sqrt(GameVars.gamespeed)))` returns at least one pass at speed zero. | #26 `CampaignAiUpdateGovernorPatch` now skips post-initialization paused updates while preserving vanilla initialization. Build/deploy/hash verified in DLL `4e01274b...`; fresh-launch smoke pending. |
| `BUG-TICK-002` | Shipped | Economy tick | `BattleUnits.CampaignDataRuns` has an unbounded `while (!Economy.UpdateFilterMaps(initialization:true)) { }` loop at decompile line 79874, runtime `BattleUnits.Update` calls `Economy.UpdateFilterMaps(false)` at line 79341, and Player.log showed `BattlefieldSetup.AssignFilters` throwing before `productionmapsymbols` was created. | #31 bounds repeated no-progress initialization returns and plausible-state runtime `NullReferenceException` spam. #37 bootstraps missing economy filter maps before vanilla `BattlefieldSetup.AssignFilters` so vanilla can create `productionmapsymbols` for the later full-cycle reset. Build/deploy/hash verified in DLL `08c72fc6...`; fresh-launch smoke confirmed one bootstrap line and no `AssignFilters` / `UpdateFilterMaps` recurrence. |
| `BUG-TICK-003` | Shipped / smoke refresh pending | W&L diary tick | Post-#37 Player.log showed early `Diary.DMD<Diary::UpdateEvents>() -> Diary.UpdateEventQueue() -> BattleUnits.Update()` NREs, and 2026-05-11 Player.log showed three recurrences after the first readiness skip. Vanilla `Diary.UpdateEventQueue` starts at frame 50, but `Diary.UpdateEvents` immediately dereferences W&L player command, campaign-group lookup, and imported diary dependencies without readiness guards. | #29 now skips only W&L `Diary.UpdateEvents` until the selected commander/current command, safe `BattleUnits.GetCampaignGroup(currentcommand)` lookup, and diary dependencies are ready, then falls through to vanilla. Readiness-check exceptions fail closed for that tick. Build/deploy/hash verified in DLL `4ebd3545...`; fresh-launch smoke refresh pending. |
| `BUG-CMD-001` | In progress | Commander assignment | `GameVars.Commander.AssignCommando` clears the old commander from the target unit but not the newly assigned commander's previous unit at decompile lines 60343 and 60380-60390. | #30 `CommanderAssignmentPreviousCommandPatch`; build/deploy/hash verified in DLL `7da618bf...`; fresh-launch smoke pending. |
| `BUG-POL-001` | In progress | Policy state handover | Policy 36 state ownership handover in `Policies.LaunchPolicyEffect` assigns states by simple majority at decompile lines 210050-210064. | #32 `MilitiaActStateHandoverGuardPatch` requires `>=65%` friendly and `<=35%` opposing support for policy-36 transfers; build/deploy/hash verified in DLL `7da618bf...`; fresh-launch smoke pending. |
| `BUG-FLT-001` | In progress | AI fleets | `Regiment.StopRegiment` restores `fleetorders == 2` patrol waypoints at decompile lines 132392-132395, while `AICampaign.CheckFleetMovements` only selects `fleetorders == 0`. | #33 `FleetPatrolResetPatch`; build/deploy/hash verified in DLL `7da618bf...`; fresh-launch smoke pending. |
| `BUG-BAT-001` | In progress | Unit combine | `BattleUnits.CombineUnits` transfers artillery crews but does not add source guns to `unitto.guns` at decompile lines 93153 and 93211-93253. | #34 `ArtilleryCombineGunTransferPatch`; build/deploy/hash verified in DLL `7da618bf...`; fresh-launch smoke pending. |
| `BUG-TAC-001` - `BUG-TAC-013` | Mixed | Tactical battlefield | Subagent-backed decompile review found courier queue, fallback/retreat null, W&L current-order, delayed waypoint, objective-chain, reserve, W&L incident-order-delay, pathfinder-shape hazards, and AI deployment terrain/facing hazards. `BUG-TAC-005` objective-chain exposure has #46 guard implemented; `BUG-TAC-010` pathfinder backtrack has live `[TacticalPathShape] reason=backward-first-segment` proof and #53 pathfinder discipline implemented; `BUG-TAC-013` deployment water/weird-location correction has #58 terrain evidence plus #60 default-off behavior implemented and enabled in the local smoke config. | Track in `vanilla-tactical-battlefield.md`; tactical terrain/facing runtime guidance lives in `../tactical-terrain-facing-discipline.md`. Telemetry first except approved focused behavior guards. |
| `BUG-BLD-001` | Shipped | Fort construction | Vanilla `AICampaign.CheckFortConstruction` had spacing but no durable area/capital saturation cap; fixed by #27 `FortConstructionGovernorPatch`. | Tune only from `[Patch:FortGovernor]` telemetry. |
| `BUG-UI-001` | Shipped | W&L command picker | Vanilla command-selection popup is a fragile one-shot at campaign frame 50; fixed by #22 `WlCareerStartSelectionRetryPatch`. | Keep frame-50 and selectable-row guards intact. |
| `BUG-PERF-001` | Shipped | High-speed campaign AI | Vanilla 20x/50x runs multiple `UpdateUnitAI` passes per rendered frame; mitigated by #26 `CampaignAiUpdateGovernorPatch`. | Future work should be perf evidence-led, not broad AI rewrite. |

## Rules

- Keep entries small and surgical.
- Prefer Postfix guards and Prefix snapshot/filter/restore over broad replacement.
- If the fix becomes a doctrine change, split a real spec/plan under `docs/superpowers/` and link back here.
- When a fix ships, update `docs/patch-catalog.md`, `docs/handoff.md`, and this index in the same closeout.

## Active Queues

- [Vanilla AI economy, policy, and construction queue](vanilla-ai-economy.md)
- [Vanilla tick-system queue](vanilla-tick-system.md)
- [Vanilla hotfix-parity queue](vanilla-hotfix-parity.md)
- [Vanilla tactical battlefield bug queue](vanilla-tactical-battlefield.md)
