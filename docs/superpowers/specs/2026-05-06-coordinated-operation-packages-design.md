# Coordinated Operation Packages Design

Status: active design spec, docs-only. Created 2026-05-06 after the user asked for nearby forces to coordinate attacks or reinforce through the strategic/director layer.
Scope: strategic campaign-map coordination for offensive packages and reinforcement packages. This spec covers design and boundaries only. It is not an implementation plan.

## Goal

Make Whiskey Realism coordinate nearby campaign-map formations instead of pushing one isolated unit at a time.

When the strategic layer chooses an attack, probe, or reinforcement response, it should decide whether nearby eligible forces should:

- attack the same target together;
- reinforce the lead formation before or during the attack;
- delay because the local package is too weak;
- recover or skip because the formation is not fit to move.

The result should fit the current strategic/director layer, preserve vanilla operation-list semantics, and route Whiskey & Lemons player-chain units through the W&L current-order bridge instead of direct campaign movement.

## Non-Goals

- No tactical battle AI changes.
- No custom W&L order UI.
- No global `AICampaign.MoveUnitTo(...)` behavior patch.
- No Transpiler.
- No direct movement fallback for W&L player-chain units when the W&L bridge rejects or fails.
- No player-alliance steering when the player is CIC.
- No cross-theater donor pulls except where an existing strategic policy explicitly allows them.
- No new persistent sidecar state for package decisions in the first slice.
- No implementation from this spec alone; write and review a focused implementation plan first.

## Source Findings

### Confirmed Vanilla: Offensive Packages

Vanilla already has a primitive campaign-map coordinated offensive routine: `AICampaign.CheckOffensiveMovements(int _aifaction, Regiment unit, float timediff)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14166`.

The method rejects units already in offensive, defensive, or supply-depot operations: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14200`.

It builds a package from the ordered unit's group plus eligible nearby relocation candidates:

- group units: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14232`;
- nearby relocation candidates: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14257`;
- enemy strength, own strength, and initiative/aggressiveness scaling: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14229`;
- overlarge package pruning: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14280`;
- winter, chapter, W&L career aggressiveness, and area-value gates: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14308`.

When vanilla commits the package, it splits W&L and non-W&L behavior:

- non-W&L units call `AICampaign.MoveUnitTo(...)` and are added to `unitsinoffensiveoperations`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14378`;
- W&L under-commander units use `AIBattle.CheckCurrentOrderUpdate(... calledfromcampaign: true)` type 5 or 16 and are not added to `unitsinoffensiveoperations` in that branch: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14449`.

This is important: Whiskey should not add W&L current-order units to `unitsinoffensiveoperations` unless fresh vanilla evidence proves that is safe.

### Confirmed Vanilla: Defensive And Reinforcement Packages

Vanilla also has a campaign-map defensive/intercept package routine: `AICampaign.CheckForDefensiveOperations(int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:13505`.

It sorts threats, filters own candidates, accumulates a sufficient package, and commits multiple units:

- threat sorting: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:13578`;
- own-unit filtering: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:13608`;
- package accumulation until the strength ratio clears: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:13635`;
- W&L under-commander defensive current order type 7 plus list mutation: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:13711`;
- secondary intercept branch using nearby dominance and type 7: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:13744`.

Unlike the offensive branch, vanilla defensive W&L current-order units are still moved between operation lists: remove from `unitsinoffensiveoperations`, add to `unitsindefensiveoperations`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:13716`.

### Confirmed Vanilla: Operation Lists Are Real State

`AIFaction` owns persistent operation lists:

- `unitsinoffensiveoperations`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:10356`;
- `unitsindefensiveoperations`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:10358`;
- initialization: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:10405`;
- load: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:16487`;
- save: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:16658`.

Vanilla cleans defensive operations through `CheckForEndOfDefensiveOperations(...)` around `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:13794`, and cleans offensive morale/retreat failures around `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14068`.

`AICampaign.UpdateMicroMovementInOffensive(int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:13968` iterates `unitsinoffensiveoperations`, chooses the next objective, direct-moves non-W&L units, and emits W&L type 6 current orders for W&L units: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:13995` through `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14057`.

### Confirmed Vanilla: Scheduler

The vanilla campaign AI scheduler calls the relevant routines from `UpdateUnitAI`:

- `CheckOffensiveMovements(...)`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:11321`;
- `UpdateMicroMovementInOffensive(...)`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:11440`;
- `CheckForDefensiveOperations(...)`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:11497`;
- `CheckForEndOfDefensiveOperations(...)`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:11500`.

Whiskey should work with these surfaces rather than replacing the scheduler.

### Confirmed Vanilla: W&L Current Orders

`AIBattle.CheckCurrentOrderUpdate(...)` is the vanilla W&L order bridge: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:8233`.

It rejects non-W&L games, missing current command, EOD cycle, null units, and non-player-alliance units: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:8251`.

For `calledfromcampaign: true`, it applies a narrow campaign chain guard: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:8366`. Peer formations and sibling formations can be strategically relevant but still be rejected silently by vanilla.

If an order is accepted, vanilla writes `DLC_WL.givenorder`, increments the order session, and shows the order panel: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:8648`.

### Confirmed Whiskey: Current Probe Is One Unit

`StrategicCoordinator.UpdateOperationalProbe(...)` builds the probe input, applies `StrategicResilienceDirector.ApplyTo(input.Options, posture)`, overlays the formation directive, and then calls `OperationalProbeRuntime.Run(...)`: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs:803`.

`OperationalProbeLedger.Build(...)` currently selects one same-area probe formation: `src/WhiskeyRealism/Strategic/OperationalProbeLedger.cs:74`.

`OperationalProbeRuntime.Run(...)` currently resolves only `SelectedUnitKey`, checks `OffensiveAvailabilityWrapper`, issues a W&L current order if eligible, or direct-moves a non-W&L unit and adds it to `unitsinoffensiveoperations`: `src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs:79`.

This is the main Whiskey surface that should start producing coordinated packages.

### Confirmed Whiskey: Director Layer Already Owns Tempo Pressure

The Strategic Resilience Director already publishes posture and threshold modifiers, and `UpdateOperationalProbe(...)` already applies that posture to probe options before the probe ledger runs: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs:833`.

The package design should use that existing posture. It should not create a second director, a second sidecar state source, or independent "campaign mood" calculations.

### Confirmed Whiskey: Nearby Data Gap

`FormationDirectiveRuntime.SnapshotUnit(...)` has live access to `unit.transform.position` and already derives area and sector keys from that position: `src/WhiskeyRealism/Strategic/FormationDirectiveRuntime.cs:102`.

`FormationDirectiveRuntime.PopulateLocalPressure(...)` already uses same-area buckets plus `Vector3.Distance(...)`, command range, and bugle range to compute support and enemy pressure: `src/WhiskeyRealism/Strategic/FormationDirectiveRuntime.cs:224`.

But `FormationSnapshot` and `FormationDirectiveAssignment` do not currently persist X/Z positions: `src/WhiskeyRealism/Strategic/FormationSnapshot.cs:5`, `src/WhiskeyRealism/Strategic/FormationDirectiveLedger.cs:15`.

The package selector needs stable pure-testable position data. Add X/Z to the snapshot/assignment or create a dedicated package candidate DTO populated from the snapshot. Do not make the pure selector depend on Unity `Vector3`.

### Confirmed Whiskey: Existing Package Pattern

`DefensePackageAggregator.Select(...)` already provides a working pattern for deterministic package selection: score candidates, accumulate effective strength, suppress overmatch, avoid worse-tier additions, and return both selected and suppressed candidates: `src/WhiskeyRealism/Strategic/DefensePackageAggregator.cs:15`.

The offensive/reinforcement package selector should reuse that style, not copy defense-specific semantics blindly.

## Design

### Approach

Implement B + C from the design discussion:

- C: create a shared strategic coordinated-operation package engine.
- B: consume it from Whiskey's operational-probe path and from a vanilla-offensive steering patch.

The shared engine is the foundation. The vanilla-offensive consumer is the riskier part because it touches a live Harmony patch surface. The implementation plan must keep those as separate tasks so the package engine and probe consumer can be tested before the vanilla offensive patch is enabled.

### Coordinated Operation Engine

Add a pure strategic package selector under `src/WhiskeyRealism/Strategic/`, provisionally named `CoordinatedOperationPackageLedger`.

The selector input should include:

- alliance id;
- target area/sector;
- target X/Z position;
- estimated target enemy strength;
- optional lead unit key;
- current formation directive assignments;
- current front sector ledger;
- director-shaped options;
- operation intent: probe, attack, reinforce, or continuation.

The selector output should include:

- decision: `None`, `CoordinateAttack`, `Reinforce`, `Delay`, or `Recover`;
- reason;
- lead unit key;
- support unit keys;
- suppressed candidates with reasons;
- package effective strength;
- target enemy strength;
- computed ratio;
- deterministic signature for logging and no-spam updates.

The selector must be pure. It should not read Unity objects, Harmony fields, `AICampaign.aifaction`, or `DLC_WL`.

### Candidate Model

Use a dedicated candidate DTO rather than making the selector depend on live `Regiment`.

Required candidate fields:

- unit key;
- alliance id;
- level;
- directive;
- area key;
- sector key;
- X/Z position;
- combat availability;
- exchange pressure;
- local friendly support;
- local enemy strength;
- readiness, morale, ammo, supply, fatigue;
- offensive allowed;
- defensive allowed;
- transfer donor allowed;
- direct movement allowed;
- inherits from parent;
- critical sector;
- front posture.

The runtime adapter can derive these fields from `FormationDirectiveAssignment`, `FrontSectorLedger`, and the new X/Z data.

### Nearby Definition

"Nearby" should mean local enough to act as the same operational package, not merely in the same broad theater.

The default ordering is:

1. same sector;
2. same army area;
3. within a command-range derived radius;
4. within a bugle-range derived fallback radius.

The exact constants belong in the implementation plan and tests, but the design requirement is clear: support must be spatially local and deterministic. Same-area units that are too far away should not be treated as immediate support.

### Package Decisions

`CoordinateAttack` means the lead and supports commit to the same target in the same strategic cycle.

`Reinforce` means one or more support formations move to reinforce a lead formation or contested target, but the package is not strong enough or doctrinally ready for an attack.

`Delay` means the local package is coherent enough to screen or hold contact but should not be moved into an attack.

`Recover` means the lead or candidate pool is below health/supply/readiness floors and should not receive a new offensive movement order.

`None` means no eligible package or no target.

### Eligibility Rules

The selector should exclude candidates that are:

- attached children inheriting a parent directive;
- not allowed to receive direct movement;
- below readiness, morale, ammo, or supply floors;
- already assigned to `Guard`, `Hold`, `Recover`, or `Concede`;
- in a critical understrength sector;
- outside nearby distance limits;
- remote donors when a local adequate package exists.

Runtime commit must recheck `OffensiveAvailabilityWrapper.IsAvailable(...)` for every unit before issuing any direct move or W&L current order. The pure selector is advisory, not a substitute for vanilla movement gates.

### Scoring Rules

The scoring should prefer:

- local supports over remote supports;
- adequate smaller packages over oversized packages;
- same-sector support over same-area support;
- rested and supplied units over depleted units;
- deterministic tie-break by unit key or stable instance id.

The selector should suppress:

- overmatch additions after the target ratio is already adequate;
- worse-tier remote support once a local package is nearly adequate;
- donors from critical fronts;
- low-readiness or low-supply units even if raw strength is high.

### Director Integration

The director layer shapes package thresholds, not package ownership.

Use the current `DirectorPosture` path already feeding `OperationalProbeOptions`:

- Too quiet or stalled posture may lower the attack ratio slightly and allow one extra local support.
- Overheated, overextended, or high-risk posture may raise the attack ratio, lower donor caps, and prefer `Reinforce` or `Delay`.
- Stable posture should use neutral doctrine.
- Personality remains clamped through the existing director path; do not add another unbounded personality multiplier.

The plan must define concrete option fields and tests for these modifiers.

### Operational Probe Consumer

Change the probe path from one selected unit to a package-aware output.

`OperationalProbeLedger` may continue to decide whether the strategic intent is probe, pause, withdraw, or escalate. The coordinated package selector then decides whether that intent can be executed by one lead, a coordinated attack package, or a reinforcement package.

Expected behavior:

- new probe: choose a lead plus nearby local support if support is needed and eligible;
- enemy reaction: prefer `Reinforce` when local support can reach and the ratio is not attack-ready;
- favorable contact/escalation: prefer `CoordinateAttack` when the package clears the attack ratio;
- overmatched contact: remove direct pressure and let the existing pause/withdraw behavior stand.

`FormationDirectiveLedger.ApplyOperationalProbe(...)` must be updated so support units are also marked consistently. A support committed to a package should not remain an unrestricted donor in the same daily cycle.

### Vanilla Offensive Consumer

Add a vanilla-offensive steering consumer only after the pure package selector and probe consumer pass tests.

The intended patch surface is `AICampaign.CheckOffensiveMovements(...)` because that is where vanilla builds and commits offensive packages.

The plan must evaluate and choose one of these non-Transpiler options:

1. Prefix/Postfix candidate filter with snapshot/restore of `aifaction[_aifaction].ownunits`, matching the existing defensive candidate-filter pattern.
2. Prefix advisory gate that skips only clearly forbidden lead units and lets vanilla build its own package.
3. Postfix diagnostic/advisory first, with no behavior change, if the candidate-filter risk is too high after code review.

The design preference is option 1 only if the implementation review proves snapshot/restore is safe for this method. Otherwise, ship option 2 or 3 and keep the behavior change in the Whiskey probe/runtime path.

The patch must preserve vanilla operation-list semantics:

- non-W&L offensive direct movement may add to `unitsinoffensiveoperations`;
- W&L offensive current orders must not be manually added to `unitsinoffensiveoperations`;
- defensive/intercept semantics remain owned by the existing defensive workstream.

### Runtime Commit Rules

For every selected package unit:

1. Resolve live `Regiment` by unit key.
2. Recheck live availability through `OffensiveAvailabilityWrapper`.
3. Call `WlStrategicOrderBridge.TryIssue(...)`.
4. If result is `IssuedWlCurrentOrder`, log and do not direct-move.
5. If result allows direct movement, call `AICampaign.MoveUnitTo(...)`.
6. If direct movement succeeds for a non-W&L offensive package unit, add it to `unitsinoffensiveoperations` if absent.
7. If bridge result is failed, skipped, player-CIC, moved-by-player, or W&L-chain-ineligible, log and skip. Do not fall back to direct movement.

Support units should use the same target package point as the lead unless the operation is `Reinforce`, where the plan may use the lead position, enemy position, or target objective depending on available runtime evidence.

### W&L Dispatch And Objective Fit

Coordinated packages should reuse `WlStrategicOrderBridge`. The bridge already owns player-CIC skips, moved-by-player skips, W&L chain eligibility, current-order type mapping, and no direct fallback for rejected W&L orders.

Target names should avoid the old `"Objective"` placeholder when practical:

1. explicit objective name from the CIC current phase;
2. target area key;
3. nearest town/IIP/asset name from the campaign map ledger;
4. `"Objective"` only as the final fallback.

The existing dispatch sanitizer remains responsible for generic final-waypoint stance text. This package work should not change `GameVars.groupstancename`.

### Logging

Add bounded package logs on signature change:

```text
[CoordinatedOps] alliance=1 intent=Attack decision=CoordinateAttack target=Virginia-Eastern ratio=1.42 lead=Army of the Potomac support=2 reason=attack-ratio-passed
[CoordinatedOps] alliance=1 unit=II Corps action=wl-current-order type=16 package=...
[CoordinatedOps] alliance=1 unit=III Corps action=direct-move package=...
[CoordinatedOps] alliance=1 unit=Reserve Division suppressed=critical-sector
```

Logs must include enough evidence to answer the user's smoke-test question: "Did nearby forces coordinate or just move independently?"

### Persistence

Do not add new persistent sidecar state in the first slice.

Use recomputed package signatures and existing coordinator ledgers. Vanilla operation lists remain the runtime state for committed offensive/defensive operations.

If implementation proves that package state must survive save/load beyond vanilla lists, stop and write a follow-up spec before adding persistence.

## Acceptance Criteria

- Pure tests cover coordinated attack, reinforcement, delay, recover, donor suppression, local-distance ordering, director threshold modifiers, and deterministic tie-breaks.
- `OperationalProbeRuntime` can commit a multi-unit package without direct-moving W&L player-chain units.
- The vanilla offensive consumer either safely steers candidate eligibility or ships as diagnostic/advisory if behavior steering is not safe.
- W&L current-order units in offensive packages are not manually added to `unitsinoffensiveoperations`.
- Non-W&L direct-moved offensive package units are added to `unitsinoffensiveoperations` only after `MoveUnitTo(...)` succeeds.
- Player-CIC alliances receive no Whiskey steering.
- Logs show package decisions and per-unit commit actions without repeated warnings or exception spam.
- `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` passes.
- `./build.sh` passes.
- DLL deploy is verified by timestamp, size, and matching SHA-256 between `dist/WhiskeyRealism.dll` and the BepInEx plugin DLL before smoke-test claims.

## Test Strategy

Add pure console-harness tests for:

- coordinated attack selects a lead plus local support when the ratio passes;
- unsupported division refuses to attack an enemy army;
- enemy reaction chooses reinforcement before attack when the ratio is not attack-ready;
- same-sector support beats remote same-area support;
- critical hold donors are refused;
- low readiness/supply support is ignored;
- overmatch support is suppressed;
- director "too quiet" posture relaxes the attack threshold within clamp;
- director high-risk posture tightens donor caps or prefers reinforcement;
- ties are deterministic;
- empty/null inputs no-op.

Add runtime-adapter tests where possible for DTO construction, but keep live Unity/Harmony calls out of the pure selector tests.

## Documentation And Closeout

If this ships, update:

- `docs/handoff.md` with shipped behavior, deploy hash, and smoke status;
- `docs/patch-catalog.md` if a new Harmony patch is added;
- `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` compile includes for new strategic files;
- archive indexes when this spec and its implementation plan are shipped and smoke-verified.

## Not Verified Yet

- The exact safe Harmony technique for steering `CheckOffensiveMovements(...)` is not verified. The implementation plan must choose the least risky non-Transpiler surface after reading the full method body.
- Runtime proof that coordinated W&L packages show intelligible current-order text is still pending.
- The exact numeric attack/reinforcement thresholds need tests and review.
- The correct reinforcement target point is runtime-dependent: lead position, enemy position, objective position, or a rally point may be best in different cases. The implementation plan must choose a first-slice rule and document the tradeoff.
- No current evidence supports adding W&L offensive current-order units to `unitsinoffensiveoperations`; the design forbids it until proven otherwise.
