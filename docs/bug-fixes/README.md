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
| `BUG-TICK-002` | Needs smoke | Economy tick | `BattleUnits.CampaignDataRuns` has an unbounded `while (!Economy.UpdateFilterMaps(initialization:true)) { }` loop at decompile line 79874. | Fresh-launch smoke the deployed #28 economy NRE guard before adding another tick-loop guard. |
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
