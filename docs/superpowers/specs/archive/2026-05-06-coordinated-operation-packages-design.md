# Coordinated Operation Packages Design

Status: archived after implementation. Current behavior lives in `docs/coordinated-operation-packages.md`, shipped code, and `docs/patch-catalog.md`.

Created 2026-05-06 after the user asked for nearby forces to coordinate attacks or reinforce through the strategic/director layer.
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
- No fake W&L multi-order fan-out. Vanilla exposes one current-order surface for the player's chain, guarded by `CheckCurrentOrderUpdate(...)`; this slice must respect that limit instead of pretending every selected support unit can receive a visible W&L current order.
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

`WlStrategicOrderBridge.Classify(...)` mirrors that chain guard today. It issues a W&L current order only for player-alliance, non-player-CIC, non-player-moved, `dlcw_isundercommander` units whose current command is the campaign group and whose parent command is under the target unit: `src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs:140`.

This creates a hard first-slice constraint: a package can select several nearby units, but only committable units may be counted in its execution ratio. If a W&L support unit is chain-ineligible and direct movement is forbidden, it must be suppressed before the package ratio is accepted. The runtime must not claim a coordinated W&L attack while silently skipping most support units.

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

The shared engine is the foundation, but the implementation plan must include the whole B + C slice: pure package engine, operational-probe consumer, runtime commit path, vanilla offensive steering patch, micro-movement interaction guard, tests, docs, build, deploy, and smoke evidence. The plan may split these into tasks, but it must not ship a diagnostic-only placeholder as the final behavior.

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

- decision: `None`, `SingleLead`, `CoordinateAttack`, `Reinforce`, `Delay`, or `Recover`;
- reason;
- lead stable unit id and display key;
- support stable unit ids and display keys;
- suppressed candidates with reasons;
- package effective strength;
- target enemy strength;
- computed ratio;
- deterministic signature for logging and no-spam updates.

The selector must be pure. It should not read Unity objects, Harmony fields, `AICampaign.aifaction`, or `DLC_WL`.

### Candidate Model

Use a dedicated candidate DTO rather than making the selector depend on live `Regiment`.

Required candidate fields:

- stable unit id;
- display unit key;
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
- front posture;
- already in `unitsinoffensiveoperations`;
- already in `unitsindefensiveoperations`;
- already in `unitsconstructingsupplydepots`;
- commit mode: direct movement, W&L current order, or blocked W&L/player-chain.

The runtime adapter can derive these fields from `FormationDirectiveAssignment`, `FrontSectorLedger`, vanilla operation lists, `WlStrategicOrderBridge.Classify(...)`, and the new X/Z data.

Stable unit id is the canonical identity for selection, tie-breaks, and runtime lookup. Display unit key remains useful for logs. A commander change can alter the current `name:commander` key; it must not invalidate a package selected earlier in the same strategic cycle.

### Nearby Definition

"Nearby" should mean local enough to act as the same operational package, not merely in the same broad theater.

The default ordering is:

1. same sector;
2. same army area;
3. within a command-range derived radius;
4. within a bugle-range derived fallback radius.

The exact constants belong in the implementation plan and tests, but the design requirement is clear: support must be spatially local and deterministic. Same-area units that are too far away should not be treated as immediate support.

### Package Decisions

`SingleLead` means the selector found one committable unit but no eligible support package. This is valid for empty-target probes and W&L chain-limited cases, and it must be logged as single-unit execution instead of reported as a coordinated attack.

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
- already present in `unitsinoffensiveoperations`, `unitsindefensiveoperations`, or `unitsconstructingsupplydepots`;
- in a critical understrength sector;
- outside nearby distance limits;
- remote donors when a local adequate package exists.

The selector must also exclude candidates whose commit mode is blocked W&L/player-chain. If excluding blocked candidates reduces the package below its ratio, the result becomes `SingleLead`, `Delay`, or `None`; it must not return `CoordinateAttack` with supports that cannot be committed.

Runtime commit must recheck `OffensiveAvailabilityWrapper.IsAvailable(...)` for every unit before issuing any direct move or W&L current order. The pure selector is advisory, not a substitute for vanilla movement gates.

### Scoring Rules

The scoring should prefer:

- local supports over remote supports;
- adequate smaller packages over oversized packages;
- same-sector support over same-area support;
- rested and supplied units over depleted units;
- deterministic tie-break by stable unit id.

The selector should suppress:

- overmatch additions after the target ratio is already adequate;
- worse-tier remote support once a local package is nearly adequate;
- donors from critical fronts;
- low-readiness or low-supply units even if raw strength is high.
- blocked W&L/player-chain candidates before ratio acceptance.

Suppressed candidates are not globally reserved for the rest of the cycle. Only selected/committed unit ids are reserved. Hard exclusions such as critical-sector, live operation-list membership, low readiness, or blocked W&L remain hard because their candidate facts remain true; soft suppressions such as overmatch or worse-tier may be reconsidered for another target in the same cycle.

### Director Integration

The director layer shapes package thresholds, not package ownership.

Use one posture path. Upstream code builds a `CoordinatedOperationOptions` object once from doctrine plus `DirectorPosture`; the selector consumes only that options object. The selector must not read raw `DirectorPosture` and must not re-apply `StrategicResilienceDirector.ApplyTo(...)`.

Donor cap has a concrete shape:

- `MaxSupportUnits`: maximum support formations selected behind the lead.
- `MaxSupportEffectiveStrength`: maximum cumulative effective support strength selected behind the lead.
- `AllowRemoteTier`: whether same-area but outside immediate command/bugle range support may be considered.

Default first-slice values:

- stable posture: `RequiredAttackRatio = 1.25`, `RequiredReinforceRatio = 0.85`, `MaxSupportUnits = 2`, `MaxSupportEffectiveStrength = desiredStrength * 1.25`, `AllowRemoteTier = false`;
- too quiet or stalled posture: `RequiredAttackRatio = 1.15`, `RequiredReinforceRatio = 0.75`, `MaxSupportUnits = 3`, `MaxSupportEffectiveStrength = desiredStrength * 1.50`, `AllowRemoteTier = true`;
- overheated, overextended, or high-risk posture: `RequiredAttackRatio = 1.40`, `RequiredReinforceRatio = 1.00`, `MaxSupportUnits = 1`, `MaxSupportEffectiveStrength = desiredStrength * 0.75`, `AllowRemoteTier = false`.

These values are test targets for the plan, not runtime folklore. Personality remains clamped through the existing director path; do not add another unbounded personality multiplier.

Empty-target probes follow vanilla's rule at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14374`: if estimated target enemy strength is zero or below, the selector returns `SingleLead` unless the upstream options explicitly set `AllowEmptyTargetPackage = true`. The default is false.

### Operational Probe Consumer

Change the probe path from one selected unit to a package-aware output.

`OperationalProbeLedger` may continue to decide whether the strategic intent is probe, pause, withdraw, or escalate. The coordinated package selector then decides whether that intent can be executed by one lead, a coordinated attack package, or a reinforcement package.

Expected behavior:

- new probe: choose a lead plus nearby local support if support is needed and eligible;
- enemy reaction: prefer `Reinforce` when local support can reach and the ratio is not attack-ready;
- favorable contact/escalation: prefer `CoordinateAttack` when the package clears the attack ratio;
- overmatched contact: remove direct pressure and let the existing pause/withdraw behavior stand.

`FormationDirectiveLedger.ApplyOperationalProbe(...)` must be updated so support units are also marked consistently. A support committed to a package should not remain an unrestricted donor in the same daily cycle. This applies only to selected supports, not every suppressed candidate.

### Vanilla Offensive Consumer

Add a vanilla-offensive steering consumer after the pure package selector and probe consumer pass tests. This is part of the required slice, not a follow-up.

The intended patch surface is `AICampaign.CheckOffensiveMovements(...)` because that is where vanilla builds and commits offensive packages.

This method is per-unit, not per-alliance. Vanilla calls it for the current offensive unit and then increments `currentoffensiveunitrun`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:11319`. Therefore the defensive candidate-filter pattern cannot be copied naively.

The plan must implement a per-cycle cached Prefix/Postfix steering patch, provisionally patch #38 `CoordinatedOffensiveOperationsPatch`:

1. Build the package/candidate decision once per offensive-cycle signature, keyed by alliance/faction, floating date or faction `lastupdate`, current offensive unit id, target area/sector, and formation/director signatures.
2. Use that cached decision for subsequent per-unit Prefix calls in the same cycle.
3. Snapshot/restore `ownunits` only when the cached decision requires filtering for the current lead call.
4. Keep reflection lookups cached.
5. Log one bounded perf line if filtering exceeds the existing slow-frame threshold.

If a full candidate filter proves unsafe during implementation review, the plan must still ship a behavior patch by falling back to a Prefix lead gate that blocks clearly forbidden lead units and lets vanilla build packages for allowed leads. It must not end as diagnostic-only behavior.

The patch must preserve vanilla operation-list semantics:

- non-W&L offensive direct movement may add to `unitsinoffensiveoperations`;
- W&L offensive current orders must not be manually added to `unitsinoffensiveoperations`;
- defensive/intercept semantics remain owned by the existing defensive workstream.

### Offensive Micro-Movement Interaction

Adding non-W&L support units to `unitsinoffensiveoperations` intentionally hands later continuation movement back to vanilla `UpdateMicroMovementInOffensive(...)`. Vanilla skips units with an active transmitted path, so this should not redirect units before their initial package path is consumed: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14004`.

First-slice behavior is:

- Whiskey owns the initial coordinated package target.
- Vanilla owns later offensive continuation after the unit finishes its active path.
- If runtime smoke proves pre-arrival retargeting, the implementation must add an in-memory package-target guard in the same plan before deploy. The guard can filter package-locked units out of `UpdateMicroMovementInOffensive(...)` until their active path clears, but it must not add sidecar persistence.

### Runtime Commit Rules

For every selected package unit:

1. Resolve live `Regiment` by stable unit id.
2. Recheck live availability through `OffensiveAvailabilityWrapper`.
3. Call `WlStrategicOrderBridge.TryIssue(...)`.
4. If result is `IssuedWlCurrentOrder`, log and do not direct-move.
5. If result allows direct movement, call `AICampaign.MoveUnitTo(...)`.
6. If direct movement succeeds for a non-W&L offensive package unit, add it to `unitsinoffensiveoperations` if absent.
7. If bridge result is failed, skipped, player-CIC, moved-by-player, or W&L-chain-ineligible, log and skip. Do not fall back to direct movement.

Support units should use the same target package point as the lead unless the operation is `Reinforce`, where the plan may use the lead position, enemy position, or target objective depending on available runtime evidence.

Add `WlStrategicIntent.Reinforce` to the W&L bridge and map it to vanilla order type 5. Reinforcement is movement to support/rally; if the package is ready to attack an enemy target, it should be `Offensive` type 16 or `EngageEnemy` type 7 instead.

### W&L Dispatch And Objective Fit

Coordinated packages should reuse `WlStrategicOrderBridge`. The bridge already owns player-CIC skips, moved-by-player skips, W&L chain eligibility, current-order type mapping, and no direct fallback for rejected W&L orders.

Before accepting a package ratio, the runtime adapter must classify candidate commit mode. A coordinated W&L package may include:

- one visible W&L current-order unit when vanilla's chain guard permits it;
- directly movable support units that are not W&L player-chain blocked;
- no blocked W&L player-chain support units in the accepted ratio.

If only one unit is committable, the decision is `SingleLead`, not `CoordinateAttack`.

Target names should avoid the old `"Objective"` placeholder when practical:

1. explicit objective name from the CIC current phase;
2. target area key;
3. nearest town or asset name from a new minimal resolver over `CampaignMapLedger.Towns` and `CampaignMapLedger.Assets`;
4. `"Objective"` only as the final fallback.

The existing dispatch sanitizer remains responsible for generic final-waypoint stance text. This package work should not change `GameVars.groupstancename`.

The plan must include a visual/text smoke check that W&L support/order text still passes through the existing dispatch sanitizer and does not reintroduce `"to none"` wording.

### Logging

Add bounded package logs on signature change:

```text
[CoordinatedOps] alliance=1 intent=Attack decision=CoordinateAttack target=Manassas ratio=1.42 lead=Army of the Potomac support=2 reason=attack-ratio-passed
[CoordinatedOps] alliance=1 unit=II Corps action=wl-current-order type=16 package=...
[CoordinatedOps] alliance=1 unit=III Corps action=direct-move package=...
[CoordinatedOps] alliance=1 unit=Reserve Division suppressed=critical-sector
[CoordinatedOps] alliance=1 intent=Attack decision=SingleLead target=Manassas lead=Army of the Potomac reason=wl-chain-single-committable
```

Logs must include enough evidence to answer the user's smoke-test question: "Did nearby forces coordinate or just move independently?"

### Persistence

Do not add new persistent sidecar state in the first slice.

Use recomputed package signatures and existing coordinator ledgers. Vanilla operation lists remain the runtime state for committed offensive/defensive operations.

If implementation proves that package state must survive save/load beyond vanilla lists, stop and write a follow-up spec before adding persistence.

## Acceptance Criteria

- Pure tests cover coordinated attack, reinforcement, delay, recover, donor suppression, local-distance ordering, director threshold modifiers, and deterministic tie-breaks.
- Pure tests cover player-CIC input returning `None`.
- Pure tests cover W&L blocked support causing `SingleLead`, `Delay`, or `None`, never false `CoordinateAttack`.
- Pure tests cover empty-target probes returning `SingleLead` by default.
- `OperationalProbeRuntime` can commit a multi-unit package without direct-moving W&L player-chain units.
- The vanilla offensive consumer ships as behavior: per-cycle cached candidate filtering where safe, or a Prefix lead gate if full filtering is unsafe. Diagnostic-only is not sufficient.
- New strategic source files are explicitly included in `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`.
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
- blocked W&L support is excluded before ratio acceptance;
- player-CIC input returns no steering;
- empty-target probe uses `SingleLead`;
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
