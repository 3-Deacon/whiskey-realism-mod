# Strategic Recon And Campaign Sandbox Doctrine

Living reference for Scourge-derived campaign-map reconnaissance, commitment, advance-guard, picket, and supply-base sandbox ideas. This is backlog/current-design context, not shipped runtime behavior. Shipped state and smoke status live in [`docs/handoff.md`](handoff.md).

## Current Status

This work is not runtime-implemented: no sighting state, `ConvergentOperationLedger`, `AdvanceGuard` / `Picket` / `SupplyBase*` / `SupplyPatrol` mission types, or config flag for any of it exists in `src/`. What does ship is the earlier, sighting-blind probe/commit layer these ideas would extend:

- `OperationalProbeRuntime` / `OperationalProbeLedger` (`Strategic/`): per-formation `OperationalProbeDecision` (`Probe`, `Pause`, `Withdraw`, `Escalate`) from readiness, supply, a probe-strength cap and friendly/enemy ratios (`EscalateFriendlyRatio` 1.8, `WithdrawFriendlyRatio` 0.55, `EnemyReactionMultiplier` 1.45), committed through `CoordinatedOperationRuntime`, `WlStrategicOrderBridge` or `AICampaign.MoveUnitTo`.
- `ContactEvidenceLedger`: classifies contact (`NoContact` .. `OvermatchedContact`) from strategic battle history and strength readings, not from vanilla spotting.
- `OperationDecisionMemory`: only a bounded list of recent replan day serials; it holds no commitment or abort state.

Campaign enemy knowledge in `FrontSectorRuntime.AddUnits` and `DefenseIntentRuntime.BuildInput` does not read vanilla spotting either. The roadmap routes sighting-honest campaign knowledge through stage G ([`docs/superpowers/plans/2026-10-15-full-overhaul-roadmap.md`](superpowers/plans/2026-10-15-full-overhaul-roadmap.md), `EnemyArmyModel` re-based on vanilla campaign spotting) and the live theater campaign planner ([`docs/superpowers/specs/2026-10-14-campaign-planner-design.md`](superpowers/specs/2026-10-14-campaign-planner-design.md): fog-honest snapshot, unseen enemies excluded, last-seen decay). Treat this doc's recon/commitment ideas as inputs to that slice, not a parallel one.

The direct tactical Scourge conversions are already implemented as Whiskey-native tactical doctrine and documented in [`docs/scourge-of-war-ai-anchors.md`](scourge-of-war-ai-anchors.md), [`docs/tactical-orchestrator.md`](tactical-orchestrator.md), and [`docs/tactical-operations-ledger.md`](tactical-operations-ledger.md). Campaign-map advance guards, pickets, supply-base security, and sighting-grounded operational commitment remain a separate strategic slice.

The point-in-time specs are archived:

- [`docs/superpowers/specs/archive/2026-05-08-scourge-operational-recon-commitment-design.md`](superpowers/specs/archive/2026-05-08-scourge-operational-recon-commitment-design.md)
- [`docs/superpowers/specs/archive/2026-05-14-scourge-campaign-advance-guard-sandbox-design.md`](superpowers/specs/archive/2026-05-14-scourge-campaign-advance-guard-sandbox-design.md)

## Design Boundary

Scourge of War is comparative doctrine evidence only. Whiskey implementations must be original C# against verified Grand Tactician anchors and current Whiskey strategic ledgers.

Do not:

- patch Scourge;
- copy Scourge code, tables, strings, assets, or play files;
- direct-transform campaign units;
- bypass W&L/player movement ownership;
- create a second campaign objective picker;
- detach arbitrary children without parent/child ledger state and save/load handling.

## Recon-Grounded Commitment

The archived recon/commitment design preserved six decisions (intent, not shipped):

1. Extend existing strategic ledgers with sighting state instead of adding a parallel top-level intel brain.
2. Apply sighting-aware planning to AI alliances while preserving the player's vanilla UI/control visibility.
3. Run on the existing daily strategic-review cadence.
4. Extend `OperationalProbeRuntime` with a Scourge-inspired force-ratio commit/probe/withdraw gate (the ratio gate above exists; the sighting-aware commit does not).
5. Add a `ConvergentOperationLedger` only for multi-arm phasing that existing ledgers cannot express.
6. Track commitment and abort state in existing operation memory infrastructure (`OperationDecisionMemory` does not yet hold any).

Key Grand Tactician anchors to recheck before planning (the vanilla fields exist in the current decompile, e.g. `Regiment.fows` at 111348; none is read by campaign-side Whiskey code today):

- `FogOfWar`, `Regiment.fows`, `Regiment.fow`, `Regiment.oppositefow`, `Regiment.unitrevealedbyenemy`, `Regiment.lastspotted`, and `Regiment.spottedmarks` for sighting truth. (The tactical layer reads `FogOfWar` through `TacticalVisionRuntimeAdapter` / `TacticalFogOfWarContact`; see [`docs/vanilla-vision-and-movement.md`](vanilla-vision-and-movement.md).)
- `Regiment.groupintelligencegathering` and Bureau of Military Information perk arrays for estimate quality and staleness.
- `FrontSectorRuntime.AddUnits`, `ContactEvidenceLedger`, and `DefenseIntentRuntime.BuildInput` as the first sighting-aware consumers.
- `OperationalProbeRuntime`, `FormationDirectiveLedger`, `CoordinatedOperationRuntime`, and `OperationDecisionMemory` as the intended integration points.

## Advance Guard And Picket Sandbox

The campaign sandbox backlog models parent/child mission state before contact:

- `AdvanceGuard`: detached mobile screen ahead of the main body.
- `Picket`: small local security detachment near a town, road junction, or supply point.
- `SupplyBaseAttack` / `SupplyBaseDefense` / `SupplyPatrol`: supply-focused missions that can rejoin, hold, or withdraw based on contact.
- `Rejoin` / `Retreat`: close-contact transitions before battle commitment.

The required model is a pure strategic state machine first:

- never detach below readiness minimum;
- never detach the last viable combat child;
- never move W&L/player-controlled commands;
- reattach when close enemy contact makes a battle decision likely;
- retreat if relative strength plus cavalry/artillery modifiers fail the commitment gate;
- persist active missions in `whiskeyrealism.json`;
- clear missions when parent, child, objective, or battle context disappears.

Preferred writer boundary is `AICampaign.MoveUnitTo` (decompile 14479; it has no W&L guard of its own, so every caller must check W&L/player ownership first, as `WlStrategicOrderBridge` does). `BattleUnits.SetWaypoint` is only a bounded fallback after a fresh anchor review proves the campaign API is unavailable for the case.

## Verification Expectations

Any future implementation plan must start with pure harness coverage for:

- advance guard detachment with spare strength and no close contact;
- picket holding without consuming final reserve;
- supply-base attack only with favorable local strength and objective confidence;
- close enemy forcing rejoin or retreat;
- force-ratio/cavalry/artillery modifiers selecting battle only when the gate passes;
- missing objective or child clearing mission state.

Runtime smoke must prove:

- a rollback config flag for movement writes (repo rule: every flag defaults on, so bounded behavior must be proven in the harness and smoke before it ships; `python3 -I tools/config-audit.py .` must stay at 0 findings);
- no W&L/player-subordinate movement;
- movement goes through vanilla APIs;
- sidecar save/load round-trip;
- battle handoff records mission origin;
- bounded telemetry and no repeated patch exceptions.

## Source Artifacts

- Scourge/GT anchor map and implemented tactical conversion status: [`docs/scourge-of-war-ai-anchors.md`](scourge-of-war-ai-anchors.md)
- Operational recon/commitment spec: [`docs/superpowers/specs/archive/2026-05-08-scourge-operational-recon-commitment-design.md`](superpowers/specs/archive/2026-05-08-scourge-operational-recon-commitment-design.md)
- Advance-guard sandbox spec: [`docs/superpowers/specs/archive/2026-05-14-scourge-campaign-advance-guard-sandbox-design.md`](superpowers/specs/archive/2026-05-14-scourge-campaign-advance-guard-sandbox-design.md)
