# Strategic Recon And Campaign Sandbox Doctrine

Living reference for campaign-map reconnaissance, fog-honest enemy knowledge and picket/advance-guard missions. Shipped state and smoke status live in [`docs/handoff.md`](handoff.md); the design of record is the campaign planner spec ([`docs/superpowers/specs/2026-10-14-campaign-planner-design.md`](superpowers/specs/2026-10-14-campaign-planner-design.md), components 4 and 4b) and its plan ([`docs/superpowers/plans/2026-10-15-campaign-planner.md`](superpowers/plans/2026-10-15-campaign-planner.md), slices G1 and G1b).

## Current Status (stage G)

Beliefs and active recon are shipped (pending the stage G in-game smoke). The earlier sighting-blind probe/commit layer (`OperationalProbeRuntime`, `OperationalProbeLedger`, `ContactEvidenceLedger`, `OperationDecisionMemory`, `CoordinatedOperationRuntime`) is deleted; see [`operational-tempo-doctrine.md`](operational-tempo-doctrine.md) and [`coordinated-operation-packages.md`](coordinated-operation-packages.md).

- **Beliefs (`Strategic/Planner/EnemyArmyModel.cs`, `EnemyDetectionRuntime`).** Enemies enter the planner only through what the side's own viewers see (vanilla `FogOfWar`: `Regiment.fows`, `Regiment.fow`, `oppositefow`, `unitrevealedbyenemy`, spotted marks), each as a belief with a last-seen position and strength, an uncertainty radius that grows with the plausible march since last sight (`PlausibleDaysCap` 7), and a confidence that halves every `HalfLifeDays` (7). A belief expires at `ExpireDays` (30) or below `MinConfidence` (0.05). Unseen enemies are excluded, never read from vanilla's own destination fields. Player intelligence incidents 60/61/64 are masked when the AI asks for a fog-grid probe (`Util/PlayerIntelIncidents`).
- **Recon planning (`ReconPlanner`).** Tasks, in priority order: `screen-posture`, `main-axis`, `probe`, `capital-approach`, `advance-guard`, `front-approach`, `belief`, `picket`; at most `MaxScreensPerTheater` (2) screens per theater and `MaxMissionsPerAlliance` (6) tasks. Hold points keep `StandoffFraction` (0.8) of the sight radius from every live belief. Formation choice prefers cavalry-majority groups, never a busy, below-minimum or W&L-player-chain (`WlChainRules.MayMove`) formation; an advance guard or probe is at most `AdvanceGuardMaxStrengthShare` (25 %) of the main body, a picket never a sector's final reserve.
- **Recon execution (`ReconExecutorRuntime`, `ReconMissionLedger`).** Missions persist in the sidecar (v3, unit-key rebinding), last at most `MaxMissionDays` (20), and are recalled when the group is needed or the task disappears. Vanilla's campaign AI never scouts and the scouting order does not widen sight, so recon is viewer placement: orders go through vanilla `ChangeCampaignOrders` and `MoveUnitTo`.
- **Commitment.** The allocator sizes the main effort against believed strength with a required ratio and releases the launch through the chapter tempo gate; there is no separate `ConvergentOperationLedger` (the planner's one allocation expresses multi-arm phasing).

The direct tactical Scourge conversions are Whiskey-native tactical doctrine documented in [`docs/scourge-of-war-ai-anchors.md`](scourge-of-war-ai-anchors.md), [`docs/tactical-orchestrator.md`](tactical-orchestrator.md) and [`docs/tactical-operations-ledger.md`](tactical-operations-ledger.md).

## Still backlog

Supply-base missions (`SupplyBaseAttack` / `SupplyBaseDefense` / `SupplyPatrol`), detaching arbitrary children from a parent and `Rejoin`/`Retreat` transitions are not implemented; the depot and rail work shipped in G5 covers supply construction, not supply-base raids. Treat the section below as the backlog design.

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

## Advance Guard And Picket Sandbox

The supply-base backlog models parent/child mission state before contact:

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
