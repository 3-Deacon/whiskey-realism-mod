# Tactical Orchestrator

Living reference for the Grand Tactician tactical orchestrator. It describes current behaviour; build, deploy, harness count and smoke status live in [`docs/handoff.md`](handoff.md). Slice specs and plans under `docs/superpowers/` are point-in-time artifacts.

## Architecture

The tactical orchestrator is a per-battle brain layered over vanilla `AIBattle` methods:

- `TacticalBattleCoordinator` owns battle lifecycle, the doctrine clock and the two per-side orchestrators.
- `TacticalBattleOrchestrator` owns one side.
- `ArmyOrchestrator` owns the army plan, enemy-intent observation, the command-node tree, direct-child roles, command intent resolution and the operations ledger.
- Generic command-node snapshots model the vanilla `Regiment` hierarchy. There are no `CorpsOrchestrator`, `DivisionOrchestrator` or `BrigadeOrchestrator` classes.
- Harmony patches are the only vanilla write surfaces. Patches read orchestrator state; they do not write it. #61's orchestrator inputs (leaf brigade map, live commander initiative, reinforcement opportunity) are refreshed by `ArmyPostureInputsRuntime.Refresh` from the coordinator tick, not from the patch.

Command nodes are built from `BattleUnits.completeunitlist`, `Regiment.GetAttachedUnitsReg(... directonly ...)`, `Regiment.parentregiment`, `Regiment.unittyp` and `GamePrefs.commandhierarchyshift`. `DirectChildDiscoveryRuntime` builds a child-to-parent map from the `GetAttachedUnitsReg` walk; in GTCW corps and divisions are scene-graph siblings, so `transform.parent` is never the command parent.

## Lifecycle

- #35 `TacticalObserverPatch` (Postfix on `AIBattle.CheckGlobalAIStrategy`) drives the coordinator when `Enable Tactical Battle Orchestrator` is on: a battle-identity detector issues Start / End / Restart, then the coordinator `Tick` runs. Battle end is also observed on `BattleUnits.OnDestroy`.
- The W&L feud instance (`isplayeraiorfeud == 2`) never drives lifecycle or `Tick` (`TacticalBattleCoordinator.DrivesOrchestrator`).
- At start the coordinator suppresses one alliance (`ResolveSuppressedAllianceId`): a W&L Commander-in-Chief player's alliance, otherwise the player's alliance outside `ai_vs_ai`. A W&L subordinate career suppresses nothing; its units are protected per unit by `TacticalWriteGuard`.
- `Tick` runs once per vanilla side-stat cycle per battle (the second side's `CheckGlobalAIStrategy` in the same cycle is skipped) and drives each side against that side's own `AIBattle` (`ArmyEvidenceBuilder.ResolveAiBattleForAlliance` / `SelectAiBattleIndexForSide`: `sideofai` match, feud instance skipped). Each `AIBattle.objectivechain` holds only its own side's groups.
- `OnBattleEnd` calls `BattleScope.ResetAll()` and drops the orchestrators.

## Scoped State

Per-battle and per-campaign statics register their own reset with `Util/ScopedState.cs`: `BattleScope.Register(name, reset)` / `CampaignScope.Register(name, reset)`, normally from the owner's static constructor. Battle teardown calls `BattleScope.ResetAll()` and `StrategicCoordinator.ResetAll` calls `CampaignScope.ResetAll()`. Each reset runs in its own try/catch and a failure logs one `[ScopedState]` warning per name. No teardown path hand-wires individual resets.

## Doctrine Clock

`TacticalBattleCoordinator.DoctrineBattleHours` (exposed as `TacticalDoctrineTime.NowBattleSeconds` / `NowBattleHours`) is the one clock for tactical doctrine timers. It follows `GameVars.currenttimefromstart` (battle game hours), samples at most once per frame, never runs backwards, restarts for a new `BattleUnits` instance, is 0 outside a battle, and is frozen while `BattleUnits.eodcycle != 0` (vanilla's end-of-day sequence, where the raw clock jumps about 9 hours).

The night is the coordinator's: while the end-of-day gate reports `Suspended`, `Tick` returns without doctrine work. On `Resumed` it forces a heavy snapshot rebuild for both sides and flags a night replan; on that side's next tick the night replan (`ReplanForNewBattleDay`: re-infer intent, replan) runs instead of the cadence replan, and it counts against `Min Replan Seconds`. Orchestrators, ledgers and plan memory are kept across the night.

Readers: plan age, phase age and the replan cooldown (`ArmyTickCycle.MaybeReplan`), phase budgets and main-effort/exploit windows, opportunity hysteresis, the reinforcement-opportunity update, contact-ledger decay, the operations-ledger clock (director commit windows), the #61 timers (recent order, task hold, courier cadence, fire control, facing pulse, write backoff, main-arrival hold), and the #45 artillery audit hold. The ai_stance hold in `TacticalStanceWriter` deliberately reads the raw `currenttimefromstart`, because it is compared with vanilla's own `lastaistancechangetime`.

## Front Axis And Sectors

`TacticalFrontAxis` is one side's front geometry: forward from the own centroid toward the visible-enemy centroid (or an objective), lateral = forward rotated 90 degrees clockwise, so lower sector ids are the army's left. Sectors are `DefaultBandCount = 5` equal-width lateral bands over the own army's lateral extent (padded to 100 world units; units beyond it clamp into the edge bands), so a sector is ground, not a list position. `Stabilize` keeps the previous heading unless it changes by at least 10 degrees. The axis also records the visible enemy extent for flank overlap, and `FlankExposureBucket` (0-3) combines wing position, enemy overlap and the group's mean vanilla outflanked weight.

## Intel Confidence And Odds

Enemy figures come only from vanilla's per-regiment sight lists of the side's own regiments (range-limited AI perception; see [`docs/vanilla-vision-and-movement.md`](vanilla-vision-and-movement.md)). `TacticalEnemyContactLedger` keeps the largest enemy strength seen (fading 10% per battle hour without re-sighting) and:

```
confidence = coverage x entryFactor x contactRecency
coverage      = mean over the current coverage bands of the observation recency of the ground each spans
entryFactor   = 0.5 + 0.5 x (watched share of enemy-owned map entry points; 1 when there are none)
recency(t)    = clamp(1 - t / 2 h, 0, 1)
```

Coverage bands span own units plus known enemy positions; observation stamps are kept on 25 m lateral cells of the front axis (re-based when the heading changes by more than 1 degree), so recency lives on the ground, not on band indices. Enemy size plays no part.

Odds blend monotonically toward unknown (1) with confidence and are capped at `EnemyVisibleSector.MaxKnowableOdds = 10`:

- army: `ArmyEvidenceBuilder.KnownEnemyOdds = 1 + confidence x (min(own / estimate, 10) - 1)`;
- band: `EnemyVisibleState.SectorOdds = 1 + confidence x (bandOdds - 1)`.

Under low confidence army odds therefore read as unknown. `TacticalReinforcementOpportunityDoctrine` returns `NoOpportunity` ("intel-confidence-too-low") below `MinIntelConfidenceForAction = 0.35`, and the probe commit in `TacticalPhaseProgressionDoctrine` uses the same constant. (`ArmyIntentInference.ConfidenceFloor = 0.3` is the separate intent-inference confidence.)

## Doctrine Components

- **Reinforcement opportunity** (`TacticalReinforcementOpportunityDoctrine`): `AttackNow`, `WaitAndConsolidate`, `DefensiveHold`, `DelayingDefense`, `WithdrawalToFightLater` or `NoOpportunity`. `AttackNow` needs the ratio over the commander's threshold and either a known enemy below us that will not catch up or, in a Meeting/Attacker battle, parity that holds through the attack window. There is no defeat-in-detail window and no enemy arrival schedule: enemy reinforcements count once they are in range. A `Defender` side (vanilla `GetBattleType`) never gets `AttackNow`.
- **Phase progression** (`TacticalPhaseProgressionDoctrine`, wired into `ArmyTickCycle.MaybeReplan`): Probe, MainEffort, Exploit, Consolidate, Withdraw, from plan/phase age, odds, morale, reserves and sector readiness.
- **Sector readiness** (`TacticalSectorReadinessDoctrine`): effective force = strength x (1 - fatigue) x ammo x morale; outcomes PushReady, PushDegraded, HoldForReinforcements, HoldFatigued (below 25% effectiveness).
- **Main-effort shift** (`ArmyOrchestrator.ConsiderMainEffortShift`): moves the main-effort sector only when the candidate's odds beat the current sector by 25%.
- **Offensive replan triggers** (`ArmyReplanTriggers`): `BreakthroughOpportunity` (another sector beats the main effort by 0.5 odds) and `MainEffortLocalBreakthrough` (local odds at 1.35x history or 1.8 absolute).
- **Role cascade** (`TacticalRoleCascade`, `TacticalLeafBrigadeMap`): depth-agnostic role propagation down the command tree, with `DoubleWing` / `DoubleWingEchelon` envelopment modes chosen by commander aggression (0.6 threshold); under envelopment pressure a playbook's reserve-commit trigger odds are lowered to at most 1.1.
- **Playbooks** (`TacticalPlaybookCatalog`, 18 seeded by `BuiltInPlaybooks`): stance precondition filter (`StanceAllowsPlaybook`: offensive playbooks never for a Defender, defensive never for an Attacker) plus envelopment and defensive-pressure score biases. Geometric play templates live in `Orchestrator/Plays/` (`Enable Geometric Play Templates`).

## Battle Write Ownership

- **Write guard:** every Whiskey write into vanilla battle state passes `TacticalWriteGuard`. `CanWrite(unit) = ModeAllowsWrites() && !IsPlayerProtected(unit)`. A unit is protected when it is player-owned (not AI-controlled), carries or contains a `dlcw_isundercommander` unit in an active W&L scenario, is the W&L player's current command or in its chain, or is DLC-taken-over. Any exception means "do not write". Battle-level writers also skip the feud instance (`IsFeudInstance`).
- **Commander Mode** (`Tactical Commander Mode`, default `Active`): `Active` runs the ledger and allows writes; `MonitorOnly` runs the ledger and telemetry with no writes; `Off` runs neither (rollback).
- **ai_stance:** written only by `Util/TacticalStanceWriter`, on AI pool groups only (`unittyp > 13`, in `aibattle[0]/[1].unitsused`), never 0 or 4. Rules are in `Tactical/TacticalStanceOwnership.cs`: a write or agreeing claim stamps vanilla's `lastaistancechangetime` so `AdjustGroupAIStance` leaves the group alone for `HoldHours = 0.25`; the executor (#61) outranks the scorer (#45), and the same source waits `MinSelfChangeHours = 0.05` before changing its own opinion. Whiskey yields (releases the stamp and drops its record) to vanilla's per-group weak-group defend/screen, to vanilla's assault escalation only when Whiskey's own target is attack, and to army retreat (`macroai 3`). A lower-priority source cannot yield a higher-priority hold. Vanilla's side-level lines (4248 `macroai 2` defend-all, 4256 outnumbered screen) are outside the yield: the side-level answer belongs to #44.
- **Charge denial:** vanilla assault (stance 4) is replaced only by `TacticalChargeDenial` through #45 (`StanceWriteSource.ChargeDemotion`, which precedes the yields): doctrine Deny demotes to defend (2), doctrine Allow keeps the assault, and with the orchestrator army off a legacy local-reaction DenyCharge demotes to attack (3). Behind `Enable Tactical Charge Denial`.
- **macroai:** #44 `BattleMacroStrategyPatch` is the single owner of the instance `AIBattle.macroai` (Postfix on `CheckGlobalAIStrategy`). It never writes retreat (3), defers to scripted `GameVars.aistrategy` / `sideinformation.macroai`, an end-of-battle state and the feud instance, and takes no opinion from an army in Commander Mode `Off` or a missing side orchestrator. The operations-ledger opinion wins over the fallback (reinforcement-opportunity mapping behind `Enable Vanilla Macro AI Sync`, or the legacy scorer when the orchestrator army is off). On the W&L player side only a ledger opinion that differs from vanilla's current and pending value is written, never for a Commander-in-Chief player. A non-blocking, value-restoring Prefix puts vanilla's own value back before vanilla runs, so vanilla's timers never see Whiskey's value.

## Tactical Tick Optimization

The coordinator gates its heavy work per side (design and plan archived under `docs/superpowers/{specs,plans}/archive/2026-05-1[67]-tactical-tick-optimization-*`):

- **Cheap path, every Tick:** `TacticalBattleSnapshotBuilder.ExtractCurrentSignature` + `TacticalHeavyPathGate.Decide`, evaluated once per side per Tick (`EnsureSideSnapshot`). Reasons: `first-tick`, `signature-change`, `pending-change`, `max-interval-force`, `throttled-pending`, `stable-under-max`, `realtime-floor`, `clock-rewind`.
- **Heavy path, on Run:** `TacticalBattleSnapshotBuilder.Build` (army evidence, objective records with approach avenues, command tree, direct children) publishes an immutable `TacticalBattleRuntimeSnapshot`, reused by the tick cycle, direct-child cycle, operations ledger and #61 targets. Builds alternate between the sides on consecutive Ticks when both have a cached snapshot.
- **Operations ledger:** re-ticks only on a new snapshot, changed command-node intents, a Commander Mode change, or 60 battle seconds (`ShouldTickOperationsLedger`).
- **Compression:** the review cycle is scaled by `ScaleCycleForCompression` (unchanged up to 4x, linear to 5x at 20x), and a real-time floor bounds heavy builds under compression.
- **Urgent boundary:** #61 and local recovery never call the gate or the heavy build; they pair the last published snapshot with live vanilla reads.

Config (`[TacticalTickOptimization]`): `Enable Tactical Heavy Path Throttling = true`, `Heavy Ledger Review Cycle Hours = 0.003`, `Heavy Ledger Review Min Realtime Seconds = 4`. Setting the throttle false builds every Tick. Gate decisions are `[TacticalHeavyGate]` telemetry rows. Measurement notes: `docs/tactical-tick-optimization-task10-baseline.md`, `docs/tactical-tick-optimization-task11-smoke.md`.

## Patch Consumers

| Surface | Patch | Owner | Gate |
|---|---|---|---|
| Battle lifecycle / tactical telemetry | #35 `TacticalObserverPatch` | Coordinator lifecycle + observer telemetry | `Enable Tactical Battle Orchestrator`, `Enable Tactical Observer`, `Enable Tactical Decision Matrix Logging` |
| Feud movement gate | #42 `BattleFeudActionGatePatch` | W&L guard plus direct-child gate; vanilla runs unless Commander Mode is `Active` | `Enable W&L Tactical Charge Guard`, `Enable Tactical Orchestrator Direct-Child Gate` |
| Side macro strategy | #44 `BattleMacroStrategyPatch` | Single `AIBattle.macroai` owner | `Enable Tactical Macro Stance Scorer`, Commander Mode |
| Group stance and charge denial | #45 `BattleGroupStancePatch` | Doctrine/ledger stance through `TacticalStanceWriter`; `TacticalChargeDenial`; artillery all-stance audit | `Enable Tactical Group Sector Stance` or Commander Mode `Active`; `Enable Tactical Charge Denial`; `Enable Artillery All-Stance Audit` |
| Reserve order delay | #56 `TacticalReserveOrderDelayGuardPatch` | Courier delay for reserve paths where `SetWaypoint` can delay | `Enable Tactical Reserve Order Delay Guard` |
| Reserve drift telemetry | #57 `BattleReserveDoctrinePatch` | Telemetry only; never changes reserve lists | `Enable Tactical Observer`, `Enable Tactical Reserve Intent Telemetry` |
| Deployment telemetry | #58 `TacticalDeploymentObserverPatch` | Read-only observer | `Enable Tactical Deployment Observer` |
| Reserve commitment | #59 `BattleReserveCommitGatePatch` | Reserve-role deny with snapshot rollback | `Enable Tactical Orchestrator Reserve Commit Gate` |
| Deployment terrain/facing correction | #60 `TacticalDeploymentTerrainDisciplinePatch` | See [`docs/tactical-terrain-facing-discipline.md`](tactical-terrain-facing-discipline.md) | `Enable Tactical Deployment Terrain Discipline` |
| Operations-ledger posture execution | #61 `BattleCommandPostureExecutorPatch` | See [`docs/tactical-operations-ledger.md`](tactical-operations-ledger.md) | Commander Mode `Active` |
| W&L player-subordinate orders | #62 `PlayerSubordinateOrderPatch` | See [`docs/wl-player-order-doctrine.md`](wl-player-order-doctrine.md) | `Enable Player Order Doctrine`, Commander Mode `Active` |
| Artillery priority/micro | B7 `B7CheckAIBombardmentPatch` | Doctrine artillery consumer | `Enable Tactical Artillery Doctrine` |
| Reserve deny / tactical fallback | B8 `B8CheckUseOfReservesPatch` | Doctrine reserve deny rollback; vanilla tactical fallbacks for RearGuard/FullRetreat | `Enable Tactical Withdrawal Doctrine` |

Every row that writes vanilla state also passes `TacticalWriteGuard`. All flags default on; key names match `Plugin.cs`.

## Telemetry

Lifecycle and plan lines: `[TacticalOrchestrator] bootstrap battle=... sidesActive=... sidesSuppressed=...`, `[TacticalOrchestrator] teardown battle=...`, `[TacticalOrchestrator] new battle day: ...`, `[TacticalCommanderRoster]`, `[TacticalIntent]`, `[TacticalReplan]` (including `trigger=NewBattleDay`). Diagnostic events include `TacticalCommandTreeProbeHealth`, `TacticalPlayerProtected` and `TacticalLeafCascade`. Write lines: `[TacticalMacroDecision]`, `[TacticalStanceWrite]`, `[TacticalChargeDeny] surface=stance`. Operations-ledger markers are listed in [`docs/tactical-operations-ledger.md`](tactical-operations-ledger.md).

## Source Files

- Command/orchestrator logic: `src/WhiskeyRealism/Tactical/Orchestrator/`
- Write guard and stance ownership: `src/WhiskeyRealism/Tactical/TacticalWriteGuard.cs`, `src/WhiskeyRealism/Tactical/TacticalStanceOwnership.cs`, `src/WhiskeyRealism/Util/TacticalStanceWriter.cs`
- Tactical patch surfaces: `src/WhiskeyRealism/Patches/`
- Harness: `tests/WhiskeyRealism.Tests/`
- Patch catalog: `docs/patch-catalog.md`
- Master handoff: `docs/handoff.md`
