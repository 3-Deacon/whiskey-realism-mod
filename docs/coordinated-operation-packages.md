# Coordinated Operation Packages

**Superseded by the campaign planner (stage G6/G7).** The package layer (`CoordinatedOperationPackageLedger`, `CoordinatedOperationRuntime`, #38 `CoordinatedOffensiveOperationsPatch` and its micro-movement companion) was deleted with the phased-plan CIC. Source of truth is the shipped code, then `docs/patch-catalog.md`. The archived design and plan are historical context only:

- `docs/superpowers/specs/archive/2026-05-06-coordinated-operation-packages-design.md`
- `docs/superpowers/plans/archive/2026-05-06-coordinated-operation-packages.md`

## What replaced it

The need (nearby formations attacking together, not vanilla's per-call broadening) is met by the planner's allocation instead of a package decision on top of vanilla's offensive call:

- The **planned mass** is the allocation's MainEffort set (`TheaterPlan.MassInstanceIds`, `ForceAllocator`): own movable formations within the march horizon of the main target, net of the capital floor and of every other sector's hold.
- The mover (`CampaignMoverRuntime`) is the only launcher of that mass; `MoveUnitTo` is vanilla's own call and the W&L player's chain classes are never moved (the map-group player is sent neither a move nor an order).
- Vanilla's offensive cannot spend the mass behind the planner's back: the availability Postfix (#127) answers "not available" for MainEffort members and for the Support of a decision whose launch the chapter tempo roll has not released (see [`operational-tempo-doctrine.md`](operational-tempo-doctrine.md)). Vanilla's defensive operations may still take them; the next snapshot sees the formation as busy.
- A move vanilla refuses or that fails is judged at the next review, retried once, then given up for 14 days and the role reassigned (`MoveTracker`, `CampaignMover`). Nothing is silently replaced by random broad movement.

The invariants that survive: no partial package commits, no W&L player-chain movement, restore-in-Finalizer for every temporary vanilla-state edit.

Runtime proof lives in the stage G smoke checklist in [`docs/handoff.md`](handoff.md); the `[CoordinatedOps]` event no longer exists (`[CampaignMover]`, `[CampaignTempo]`, `[Allocation]` replace it).
