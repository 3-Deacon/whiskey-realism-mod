# Tactical Battle Orchestrator Design

Status: umbrella design spec for the multi-echelon tactical battle orchestrator. Do not implement directly from this spec; per-phase implementation plans (O0-O7 + OC) live alongside under `docs/superpowers/plans/`.

Scope: replaces the current Slice B scorer-driven decision authority with a hierarchical per-side battle orchestrator that owns tactical decisions at army, corps, division, and brigade echelons; reads adversarial intent from the opposing echelons; and selects personality-keyed historical doctrine playbooks. Subsumes Slice C (W&L hierarchy AI) into the same engine via vanilla's existing `DLC_WL.givenorder` surface.

Source-of-truth order: shipped code > [`docs/patch-catalog.md`](../../patch-catalog.md) > this spec > prior Slice B umbrella spec [`2026-05-05-tactical-brain-design.md`](2026-05-05-tactical-brain-design.md). The Slice B umbrella spec is not retired by this spec — it remains the authoritative description of doctrine inputs, scorer evidence, and vanilla anchors for the layers this orchestrator builds on top of.

This spec resolves five open questions inline rather than deferring them; see "Locked decisions" below.

## Goal

Make each side fight like a thinking commander with a coherent battle plan, against another side with its own coherent plan, biased by historical commander personalities.

The orchestrator should:

- pick a coherent army-level battle plan per side (envelopment, prepared defense, maneuver-fix, attrition, defensive overslope, etc.) keyed by commanding general's personality + terrain + force odds;
- propagate that plan as intent down through corps → division → brigade echelons, with each tier translating intent into the decisions appropriate for its level;
- observe the opposing side's apparent plan from visible state (sector concentration, force shifts, contact zones) and bias its own decisions in response;
- replan on assumption-invalidating events (main-effort sector loss, decisive enemy intent shift, force imbalance shift, casualty/morale floor breach, reserve exhaustion, reinforcement arrival);
- drive the player's `DLC_WL.givenorder` intelligently when player is a subordinate, by translating the orchestrator-at-echelon-above's intent into vanilla's existing `CheckCurrentOrderUpdate(...)` surface;
- preserve the W&L player-control invariant: orchestrator decisions never bypass `TacticalGateHelpers.IsPlayerControlled` for player-commanded units;
- preserve the read-only-mod-state invariant: Harmony patches only READ orchestrator output; the orchestrator is the only writer, runs from the existing `TacticalObserverPatch` (#35) tick anchor.

## Non-Goals

- No custom battle renderer, movement engine, pathfinder, or replacement of vanilla `AIBattle.UpdateAITasks`.
- No Prefix that skips vanilla battle AI wholesale.
- No omniscient enemy reads; all inference uses visible state via existing `TacticalSectorLedger` / `TacticalContactLedger` filters.
- No new vanilla state writes — orchestrator decisions flow through the same existing default-off patch surfaces (#41 / #42 / #44 / #45 / #46 / #47 / #48 / B7 / B8) plus one new player-subordinate hook.
- No new persistence sidecar fields. The orchestrator is runtime-only; battles are short enough that mid-battle save/reload rebuilds from current vanilla state on next tick.
- No deletion of existing scorer files (except `TacticalCommanderIntent.cs` and `TacticalPlaybookLedger.cs`, wholesale subsumed). Scorers demote from decision authorities to evidence inputs.
- No replacement of vanilla's `CheckCurrentOrderUpdate` — orchestrator calls it with new arguments; vanilla's internal dedup (line 8643) handles the no-op case.

## Source Findings

This spec is grounded in current Whiskey code (`src/WhiskeyRealism/Tactical/`), the prior Slice B umbrella spec, and verified vanilla anchors from `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

### Current Whiskey anchors

- 11 Harmony patches under `src/WhiskeyRealism/Patches/` named `Battle*` or `Tactical*` exist today, with #41/#42/#44/#45/#48 + B7/B8 default-off behind per-patch enable flags awaiting smoke verification.
- ~33 files under `src/WhiskeyRealism/Tactical/` include the existing scorer set (`TacticalChargeViability`, `TacticalSupportScreen`, `TacticalRefuseFlankIntent`, `TacticalDestinationDiscipline`, `TacticalQuadrantThreatScorer`, `TacticalMoralePressure`, `TacticalHelpRequest`, `TacticalFatigueState`), evidence ledgers (`TacticalSectorLedger`, `TacticalContactLedger`, `TacticalOddsDoctrine`, `TacticalCommandLedger`, `TacticalCommanderProfile`, `TacticalReservePolicyLedger`, `TacticalMoraleSnapshotLedger`), shared infrastructure (`TacticalGateHelpers`, `TacticalScoreCache<T>`, `TacticalUnitType`, `TacticalOrderSettlementGate`, `TacticalTelemetry`, `TacticalWlActionGuard`), placeholder/B6 types (`TacticalCommanderIntent`, `TacticalPlaybookLedger`, `TacticalReactionContext`, `TacticalLocalReactionScorer`), B7/B8 doctrine + adapters (`TacticalArtilleryDoctrine`, `TacticalArtilleryInputAdapter`, `TacticalWithdrawalDoctrine`, `TacticalWithdrawalInputAdapter`), the misnamed `TacticalBattlePlan.cs` (which actually holds decision input/output structs, not a plan), and `TacticalBattleContext` + `TacticalBattlefieldBugDiagnostics`.
- `PersonalityVector` and `CIC.Effective(...)` from Slice A provide the existing era × faction × officer personality stack at army echelon. `HistoricalFigureRegistry` covers 25 named officers (army-tier coverage; corps/division/brigade rely on era × faction defaults + rank-tier biases).
- `TacticalObserverPatch` (#35) is the existing tick anchor used for read-only telemetry; this spec extends it to also drive the orchestrator's per-tick cycle.

### Verified vanilla anchors (decompile-confirmed 2026-05-08)

- `AIBattle.CheckGlobalAIStrategy()` line 6314 — battle-level `macroai` transitions (`-1 dynamic / 0 assault / 1 attack / 2 defend / 3 retreat`).
- `AIBattle.AdjustGroupAIStance()` line 4221 — group-level `ai_stance` ladder; `ai_stance == 4` is group charge stance.
- `AIBattle.MicroAICheckForCharges(...)` line 4905 — per-unit charge initiation/cancellation; gates on feud state; writes `lastfeudactiontime` on both branches; does NOT call `PerformAIActionDLCWL`.
- `AIBattle.CheckForFeudGroupActions(...)` line 4931 — feud-group action surface; W&L gate gap from prior Slice B work.
- `AIBattle.CheckUseOfReserves(...)` line 6062, `LinkReservesToLineGroup()` line 6642, `AssignReserves()` line 7017 — reserve commit surfaces.
- `AIBattle.CheckLineFallbacks(...)` line 5118, `MicroAICheckForRetreats(...)` line 4817 — local fallback / retreat surfaces.
- `AIBattle.CheckAIBombardment(...)` line 3869 — artillery bombardment surface.
- `AIBattle.CheckCurrentOrderUpdate(Regiment unit, int type, Vector3 position, string destinationname, float rotation, float width, float depth, bool calledfromcampaign = false)` line 8233 — central order-issuing function. Skips when `bunits.eodcycle > 0`. Tolerates duplicate orders via the line 8643 dedup conditions. Replaces or skip-updates `DLC_WL.givenorder` and calls `careerinformationpanel.ShowNewOrder(...)`.
- `AIBattle.UpdateDLCPlayerOrders()` line 6747 — W&L player-order coordinator; calls private helpers `CheckRemovalOfOrders` (line 6777) and `CheckReserveOrder` (line 6813). Both helpers issue `CheckCurrentOrderUpdate` for the player's `currentcommand` at lines 6798 (`type=15`), 6804 (`type=13`), 6808 (`type=14`), 6841 (`type=11`). Guarded by `DLC_WL.IsCommanderInChief()` short-circuit at line 6788.
- `DLC_WL.givenorder` static field line 43191 (type `DLC_WL.GivenOrders` defined line 41712). Saved/loaded by vanilla at lines 45100-45120 / 45454-45490.
- `Regiment.inbattle` is the per-unit battle flag. Set true at lines 22781 / 22889 (battle init from BattleUnits) and 80791-80792 (`callingunit.inbattle = true; foundenemy.inbattle = true;` — engagement trigger). Set false at lines 21535 / 21995 (fleet/sea engagement teardown), 81086 (battle teardown). No higher-level `StartBattle`/`EndBattle` named methods exist; battle lifecycle is detected from `inbattle` transitions across the side's units.
- `BattleUnits.eodcycle` non-zero indicates end-of-day cycle; vanilla `CheckCurrentOrderUpdate` short-circuits when set.
- `CommanderRelations` class line 41027; per-commander `influenceval[cause]` floats clamped to ±1; `commanderid` keys; multi-cause accumulator `accumulatedcommanderrelations` static line 43145 saved/loaded with vanilla saves (lines 45143 / 45525).
- `DLC_WL.IsCommanderInChief()`, `DLC_WL.GetPlayerCommandHierarchy()` line 11368, `DLC_WL.dlc_chosencommander`, `GameVars.commander[…].currentcommand` — player-rank discovery surface.

### Implementation boundaries from review

- This spec is the umbrella; per-phase implementation plans O0-O7 + OC (described in §"Phasing") each get their own `docs/superpowers/plans/` document.
- Per-phase plans must verify their vanilla anchors against current decompile before patching; line numbers above are 2026-05-08 verified.
- `TacticalGateHelpers.IsPlayerControlled` and `TacticalWlActionGuard` are the canonical W&L gates; orchestrator decisions never bypass them.
- `TacticalOrderSettlementGate` and `TacticalOrderFriction` are the canonical order-friction gates from prior B5 settlement work; brigade-level orchestrator writes pass through them.
- `TacticalSectorLedger.helpRequests` is the upward request channel; the existing B7+B8 follow-up note about adding `ClearHelpRequests()` between battles is satisfied by this spec's battle-end teardown.

## Locked decisions (finalized during brainstorm 2026-05-08)

The brainstorm produced five open questions; each is locked here rather than deferred.

1. **Battle lifecycle anchor: no new bootstrap/teardown patches.** `TacticalObserverPatch` (#35) detects `inbattle` transitions across each side's units (was-no-units-in-battle → now-some-units-in-battle, and inverse) and calls `TacticalBattleCoordinator.OnBattleStart()` / `OnBattleEnd()`. This avoids two new patch surfaces.

2. **Player order surface: `PlayerSubordinateOrderPatch` is a Postfix on `AIBattle.UpdateDLCPlayerOrders`.** After vanilla has set whatever it set, the Postfix calls `CheckCurrentOrderUpdate(playerCommand, orchestratorType, orchestratorPos, orchestratorZone, …)` with the orchestrator-derived intent. Vanilla's line 8643 dedup conditions auto-skip when intents match. No Prefix; no replacement of vanilla helpers.

3. **Per-side enable order: simultaneous.** Each phase's per-echelon valve flips default-on as soon as that phase's smoke gate passes; AI side and player's side enable together because both run identical code paths. No staggered per-side rollout. (This is moot given master flag default-on; recorded for completeness.)

4. **`commanderrelations` consumption: deferred to phase OC.** O6 ships orchestrator-driven `givenorder` plumbing only. Phase OC (after O7) implements relations-driven compliance/refusal effects: reads `CommanderRelations.influenceval[cause]` (already saved/loaded by vanilla) to bias whether AI subordinates execute the orchestrator's order immediately, with delay, or dispute it; writes back to a single new "obeyed/ignored orders" cause column. OC subsumes what was previously planned as Slice C, scoped as the orchestrator's relations layer.

5. **Telemetry retention: `[TacticalDecisionMatrix]` removed at O7 cleanup.** Until O7, the decision-matrix rows coexist with new `[TacticalPlan]` / `[TacticalIntent]` / `[TacticalCascade]` / `[TacticalReplan]` markers behind the existing `Enable Tactical Decision Matrix Logging` flag. At O7 the flag becomes a no-op and the matrix code path is deleted.

6. **Master flag default state: ON.** `Enable Tactical Battle Orchestrator = true` default. O0 ships first with orchestrator instantiated but emitting no decisions (telemetry-only). Each subsequent phase's per-echelon valve flips default-on as it ships smoke-verified. Fallback-to-scorers paths inside each rewired patch stay in code as per-patch debug switches (for regression triage), removed at O7 cleanup.

## Design Summary

The orchestrator is a per-side hierarchical decision tree with army at the root and brigades at the leaves. Each echelon's orchestrator owns one tier of decisions: army picks plans and reads strategic intent; corps allocates sector roles; division manages group stance and reserve commit; brigade executes (line/screen/probe/hold/charge). Intent flows top-down each tick; subordinate orchestrators emit help requests upward through `TacticalSectorLedger.helpRequests`. Plans themselves are picked from a personality-keyed playbook catalog and re-picked on assumption-invalidating triggers.

Adversarial dynamics emerge: each echelon maintains a `TacticalIntentModel` of the matching opposing echelon, built from visible state (sector concentration, force balance shifts, contact zones, vanilla `unitsused`). The model carries confidence; commander personality biases how low-confidence signals get consumed (cautious COs replan defensively at lower confidence; aggressive COs commit reserves on weaker evidence). When both sides run this engine, plans and replans become responses to inferred opposing plans.

W&L player handling: when the player is at echelon E (army / corps / division / brigade — vanilla `DLC_WL.GetPlayerCommandHierarchy()` + `commander.currentcommand.unittyp` discovery), the orchestrator at E swaps to `PlayerEchelonShim` which (a) does not autonomously decide at E, (b) forwards player's direct orders down as intent to subordinate orchestrators, and (c) receives the orchestrator-above-player's intent and translates it into vanilla `CheckCurrentOrderUpdate(...)` calls — the existing `DLC_WL.givenorder` surface, mapped onto orchestrator intent via a fixed table (see §"Player givenorder mapping"). When the player is CIC, the player's-side orchestrator is suppressed entirely (per locked Q3 of `2026-05-05-tactical-brain-design.md` umbrella); enemy side always runs the full hierarchy.

Vanilla integration: existing default-off Slice B writer patches (#41 / #42 / #44 / #45 / #48 / B7 / B8) are rewired to read orchestrator output instead of scorer output. Existing scorer files demote to evidence inputs (read by orchestrators). One new patch surface — `PlayerSubordinateOrderPatch` Postfix on `AIBattle.UpdateDLCPlayerOrders` — handles the player-as-subordinate case. No new bootstrap/teardown patches; `TacticalObserverPatch` detects battle lifecycle from `inbattle` transitions.

## Architecture

### Hierarchy

```
TacticalBattleCoordinator (singleton, runtime-only)
└── TacticalBattleOrchestrator [side]                   (0 or 2 active)
    └── ArmyOrchestrator                                (1 per side)
        └── CorpsOrchestrator                           (~3 per army; varies)
            └── DivisionOrchestrator                    (~3 per corps)
                └── BrigadeOrchestrator                 (~3-4 per division)
                    └── (regiments / batteries execute via vanilla)
```

`TacticalBattleCoordinator` is a runtime-only singleton (peer of `StrategicCoordinator`, not nested) created on detected battle start, torn down on detected battle end. Owns up to two `TacticalBattleOrchestrator` instances — one per alliance, with W&L suppression of the player's-side orchestrator when the player is CIC of that side.

### Echelon orchestrator types

`EchelonOrchestrator` is the abstract base. Concrete types:

- **ArmyOrchestrator** — picks `TacticalBattlePlan` (intent + phase + main-effort sector); reads army-level intent inference; emits `ArmyIntent` down to its corps. Personality stack: full `PersonalityVector` from `HistoricalFigureRegistry` for named army COs, era × faction defaults for unknowns.
- **CorpsOrchestrator** — receives `ArmyIntent`; picks corps approach (push, fix, refuse, screen); allocates its divisions to sector roles; emits `CorpsIntent` down. Personality biases tempo (aggression dimension) and audacity.
- **DivisionOrchestrator** — receives `CorpsIntent`; manages sector group stance (line / screen / probe / hold); commits brigades; issues reinforcement requests upward via `TacticalSectorLedger.helpRequests`; emits `DivisionIntent` down.
- **BrigadeOrchestrator** — receives `DivisionIntent`; executes — picks line / screen / probe / hold / charge under division intent + local conditions; commits to specific targets; emits `BrigadeDecision` (consumed by rewired patches).
- **PlayerEchelonShim** — swaps in at the player's current rank tier (discovered via `DLC_WL.GetPlayerCommandHierarchy()` + `currentcommand.unittyp`). Forwards player orders down as intent; receives orders from above and translates to `CheckCurrentOrderUpdate(...)` calls. No autonomous decisions at the player's tier.

### Plan and intent entities

All read-only to Harmony patches; written only by orchestrators inside the per-tick cycle.

- **`TacticalBattlePlan`** — army-level intent: plan id (e.g., `lee_envelopment`), phase (`probe` / `main_effort` / `exploit` / `consolidate` / `withdraw`), main-effort sector, fixing sectors, screening sectors, reserve allocation policy, replan trigger set, source playbook reference, age, jitter seed.
- **`TacticalIntentModel`** — observed/inferred opposing-echelon plan: primary intent (`Attack` / `Defend` / `Withdraw` / `Probe` / `Refuse`), inferred main-effort sector, confidence in `[0, 1]`, age in seconds, supporting evidence tags. One per opposing echelon (army reads enemy army; corps reads enemy corps within own frontage; etc.).
- **`TacticalPlaybookCatalog`** — N historical doctrine playbooks parameterizing `TacticalBattlePlan`. See §"Playbooks" for the seed catalog.
- **`TacticalCommanderRoster`** — discovers all battle commanders at all echelons on each side; ties each to `HistoricalFigureRegistry` where possible; derives `PersonalityVector` for each (with fallback to era × faction defaults + rank-tier biases for unknowns).
- **`ArmyIntent`** / **`CorpsIntent`** / **`DivisionIntent`** / **`BrigadeDecision`** — intent structs cascading down the hierarchy.

```csharp
struct ArmyIntent {
    BattlePlanId   PlanId;
    BattlePhase    Phase;
    SectorId       MainEffortSector;
    SectorId[]     FixingSectors;
    SectorId[]     ScreeningSectors;
    float          ReserveCommitTriggerOdds;
    float          AggressionBias01;        // from playbook + commander
}
struct CorpsIntent { /* sector role, divisional task, weight */ }
struct DivisionIntent { /* group role, axis of advance, support priority */ }
struct BrigadeDecision { /* line/screen/probe/hold/charge, target, reserve flag */ }
```

### File layout

```
src/WhiskeyRealism/Tactical/Orchestrator/
├── TacticalBattleCoordinator.cs          (singleton)
├── TacticalBattleOrchestrator.cs         (per-side root)
├── EchelonOrchestrator.cs                (abstract base)
├── ArmyOrchestrator.cs
├── CorpsOrchestrator.cs
├── DivisionOrchestrator.cs
├── BrigadeOrchestrator.cs
├── PlayerEchelonShim.cs
├── TacticalBattlePlan.cs                 (the actual plan entity)
├── TacticalIntentModel.cs
├── TacticalPlaybookCatalog.cs
├── TacticalPlaybook.cs                   (one doctrine template)
├── TacticalCommanderRoster.cs
└── Playbooks/
    ├── LeeEnvelopmentPlaybook.cs
    ├── JacksonValleyShufflePlaybook.cs
    ├── McClellanPreparedDefensePlaybook.cs
    ├── ShermanManeuverFixPlaybook.cs
    ├── GrantContinuousAttritionPlaybook.cs
    ├── LongstreetDefensiveOverslopePlaybook.cs
    ├── HookerFlankDeparturePlaybook.cs
    ├── HoodFrontalAssaultPlaybook.cs
    ├── BurnsideForcedAssaultPlaybook.cs
    ├── BraggIndecisiveCommitPlaybook.cs
    ├── GenericAggressivePlaybook.cs
    ├── GenericCautiousPlaybook.cs
    ├── GenericMethodicalPlaybook.cs
    └── GenericDesperatePlaybook.cs
```

The current `src/WhiskeyRealism/Tactical/TacticalBattlePlan.cs` (which holds decision input/output structs, not a plan) is renamed to `TacticalDoctrineDecisionContracts.cs` to free the name for the actual plan entity.

Existing scorer files stay where they are under `src/WhiskeyRealism/Tactical/`; they become evidence/ledger inputs that orchestrators query. Two files are deleted at O7 cleanup as wholesale subsumed: `TacticalCommanderIntent.cs`, `TacticalPlaybookLedger.cs`.

### Lifecycle

- **Bootstrap**: `TacticalObserverPatch` (#35) detects "no units in battle on this side at tick T-1, some units in battle at tick T" → calls `TacticalBattleCoordinator.OnBattleStart()`. Coordinator instantiates per-side orchestrators (skipping player's side if player is CIC), discovers commander roster, picks initial playbook per army.
- **Tick**: `TacticalObserverPatch` (#35) per-tick handler calls `TacticalBattleCoordinator.Tick()` after current observation work. See §"Decision flow + cadence".
- **Teardown**: `TacticalObserverPatch` (#35) detects "all units off battle for two consecutive ticks on a side" → calls `TacticalBattleCoordinator.OnBattleEnd()`. Coordinator clears per-side orchestrators, `TacticalSectorLedger.helpRequests`, `TacticalMoraleSnapshotLedger`. (The `helpRequests` clear satisfies the existing B7+B8 follow-up note about needing a between-battles cleanup.)
- **Mid-battle save/reload**: orchestrator state is runtime-only. On reload, the next tick rebuilds commander roster, re-picks plan from current vanilla state, rebuilds sector evidence. No new sidecar fields. The existing `whiskeyrealism.json` campaign sidecar stays campaign-only.

## Decision flow + cadence

### Two cadences

1. **Per-tick** (every `TacticalObserverPatch` cycle): evidence refresh, intent inference update, cascade of currently-active intent down the hierarchy. Cheap.
2. **On replan trigger**: full plan re-evaluation (re-score playbooks, possibly switch). Expensive; rate-limited to minimum 60 game seconds between replans per army (configurable via `Tactical Orchestrator Min Replan Seconds`).

### Per-tick cycle

```
1. TacticalBattleCoordinator.Tick()
   → discovers/refreshes TacticalCommanderRoster (cheap; only on hierarchy changes)

2. For each side's TacticalBattleOrchestrator:
   a. Refresh evidence ledgers (existing TacticalSectorLedger,
      TacticalContactLedger, TacticalOddsDoctrine,
      TacticalMoraleSnapshotLedger, TacticalCommandLedger,
      TacticalReservePolicyLedger).
   b. Update TacticalIntentModel for each opposing echelon:
      army reads enemy army; corps reads enemy corps within own
      frontage; division reads enemy division in its sector;
      brigade reads adjacent enemy units.
   c. ArmyOrchestrator.CheckReplanTriggers() — if any trigger
      fires AND last replan ≥ MinReplanSeconds ago, re-pick
      playbook and instantiate a new TacticalBattlePlan.
   d. ArmyOrchestrator.PropagateIntent() emits ArmyIntent down
      to each CorpsOrchestrator.
   e. Each CorpsOrchestrator translates ArmyIntent → CorpsIntent;
      emits down to DivisionOrchestrators.
   f. Each DivisionOrchestrator translates CorpsIntent →
      DivisionIntent; emits down to BrigadeOrchestrators.
   g. Each BrigadeOrchestrator decides this tick's action and
      writes BrigadeDecision (read-only output).

3. Patches consume BrigadeDecision (and intermediate intent
   structs for telemetry) to write vanilla state at their
   existing surfaces.
```

### Replan triggers

| Trigger | Origin | Effect |
|---|---|---|
| **Battle start** | new battle detected (lifecycle hook) | Initial plan selection. |
| **Phase deadline** | plan's current-phase clock | Advance to next phase or pick new plan. |
| **Main-effort sector loss** | sector ledger: own force in main-effort sector falls below threshold (config) | Replan. |
| **Decisive enemy intent shift** | intent model: confidence-weighted enemy main-effort vector moves more than configured threshold | Replan. |
| **Force imbalance shift** | odds doctrine: side's overall force ratio crosses 1.4× / 0.7× hysteresis bands | Replan. |
| **Casualty threshold** | morale snapshot: army-level fatigue/morale crosses configured floor | Replan toward defensive/withdrawal playbook. |
| **Reserve exhaustion** | reserve policy ledger: army committed reserves drop below 15% | Replan toward consolidation. |
| **Reinforcement arrival** | vanilla `BattleUnits.sideinformation.strengthtoarrive` crosses zero | Replan to reflect new force level. |

Triggers are checked per tick; replan itself is rate-limited.

### Vanilla integration map

| Vanilla anchor | Existing patch | New input source under orchestrator |
|---|---|---|
| `AIBattle.CheckGlobalAIStrategy` (line 6314) | #44 `BattleMacroStrategyPatch` | `ArmyOrchestrator.CurrentMacroAi` |
| `AIBattle.AdjustGroupAIStance` (line 4221) | #45 `BattleGroupStancePatch` | `BrigadeOrchestrator.GroupStance` |
| `AIBattle.MicroAICheckForCharges` (line 4905) | #41 `BattleChargeGatePatch` | `BrigadeOrchestrator.ChargeDecision` |
| `AIBattle.CheckUseOfReserves` (line 6062) | #48 + B8 reserve patches | `DivisionOrchestrator.ReserveCommit` + `ArmyOrchestrator.WithdrawalIntent` |
| `AIBattle.CheckLineFallbacks` (line 5118) | B8 fallback patches | `BrigadeOrchestrator.FallbackDecision` |
| `AIBattle.CheckAIBombardment` (line 3869) | B7 `CheckAIBombardment` | `DivisionOrchestrator.ArtilleryPrio` |
| `AIBattle.CheckForFeudGroupActions` (line 4931) | #42 `BattleFeudActionGatePatch` | `BrigadeOrchestrator.FeudActionGate` |
| `AIBattle.UpdateDLCPlayerOrders` (line 6747) → `CheckCurrentOrderUpdate` (line 8233) | NEW `PlayerSubordinateOrderPatch` (Postfix) | `PlayerEchelonShim` translates orchestrator-above-player intent into `(type, position, zone, name)` |

The new `PlayerSubordinateOrderPatch` is the only behavior-net-new patch surface. Everything else is rewiring of existing default-off patches.

### Player givenorder mapping

When player is at echelon E, the orchestrator at E+1 emits a `*Intent` for the player's command. `PlayerEchelonShim` at E translates that intent into the closest matching `CheckCurrentOrderUpdate(...)` call:

| Intent content | `CheckCurrentOrderUpdate` args |
|---|---|
| Hold sector | `type=2 hold`, position=sector center, zone=sector bounds |
| Advance to objective X | `type=0/1 move`, position=X, zone=corridor |
| Support flanking unit at sector S | `type=5 support`, position=S center |
| Support fire on target T | `type=7 support fire`, position=T |
| Withdraw via entry point E | `type=11 retreat`, position=E, rotation=retreat angle |
| Engage closest enemy in zone | `type=12 engage`, position from `closestenemyunitfar` |
| Pursue / chase | `type=13 pursue`, position=enemy current |
| Cover / overwatch | `type=14 cover`, position=current command |
| Build fort at P | `type=9 fort`, position=P |
| Build supply depot at P | `type=10 depot`, position=P |
| Defend named position (no specific zone) | `type=15 defend named`, position=intent position |
| Take capital | `type=8 take capital`, position=enemy capital |
| Take objective in area | `type=5/16 area objective`, position=closest objective center |

Vanilla `CheckCurrentOrderUpdate` (line 8233) handles dedup at line 8643 — when the orchestrator's intent matches the existing `DLC_WL.givenorder` (within position tolerance and same type), the call returns early without modifying state. Re-issuing the same intent each tick is therefore safe and cheap; the orchestrator only mutates `givenorder` when intent actually changes.

### Order latency and friction

Orchestrator decisions don't bypass vanilla's order-delay path. `TacticalCommandLedger` + `TacticalOrderFriction` from prior B2 work model courier / bugle / transmitted-path delays. When an army-level intent shift cascades to a brigade-level decision, brigade execution still respects vanilla's order-delay path. A flank surprise produces an army-level "we should be withdrawing" decision within seconds, but brigades start moving only after vanilla couriers carry the order. `TacticalOrderSettlementGate` from prior B5 settlement work prevents stacking orders while vanilla queues are still pending.

## Adversarial intent inference + personality

### Inference principle: visible state only

Each echelon's `TacticalIntentModel` is built from what that echelon could plausibly know — vanilla's existing visibility / spotting / contact state, not omniscient field reads. The current `TacticalSectorLedger` and `TacticalContactLedger` already filter to what own units have seen; the orchestrator inherits that filter.

### Per-echelon evidence

| Echelon | Reads | Infers about opposing echelon |
|---|---|---|
| **Army** | Side-wide sector force concentrations; total enemy strength visible; enemy reinforcement arrivals; force balance shifts; opposing army CO identity | Enemy main-effort vector; enemy phase (attacking / defending / withdrawing); enemy reserve commitment level |
| **Corps** | Sector-level enemy concentrations within own corps frontage; flank gaps; enemy column-vs-deployed posture | Enemy corps's task in this sector (push / fix / refuse); axis of advance |
| **Division** | Group-level contacts; enemy formation shapes; local force ratios | Enemy division intent in this sector |
| **Brigade** | Adjacent enemy units; visible enemy stance flags; receiving fire | Enemy brigade's immediate action |

Lower echelons see more detail, less context. Higher echelons see less detail, more context.

### `TacticalIntentModel` content

```csharp
struct TacticalIntentModel {
    InferredIntent  PrimaryIntent;        // Attack / Defend / Withdraw / Probe / Refuse
    SectorId        InferredMainEffort;
    float           Confidence01;         // 0 = no evidence; 1 = unambiguous
    float           AgeSeconds;
    EvidenceTag[]   SupportingEvidence;
}
```

### Personality consumption

Three `PersonalityVector` dimensions shape how an orchestrator consumes its `TacticalIntentModel`:

| Personality dimension | Effect |
|---|---|
| **Aggression** | High aggression: low-confidence "enemy is weak" signals get treated as actionable. Low aggression: same signal needs higher confidence before triggering an attack. |
| **Caution** | High caution: low-confidence "enemy is concentrating against me" signals trigger defensive replan. Low caution: same signal ignored until confirmed. |
| **Audacity** | High audacity: orchestrator commits reserves on plausible flank exposure. Low audacity: reserves held until threat confirmed. |

Character difference emerges naturally: McClellan (cautious, low audacity) over-weights low-confidence enemy concentration signals → endlessly probes, never commits. Lee (aggressive, high audacity) acts on low-confidence flank-gap signals → commits early, sometimes catastrophically. No new personality state — uses the existing 5-dimensional `PersonalityVector` from Slice A.

### Adversarial loop (illustrative)

When both sides run this engine:

1. Side A's `ArmyOrchestrator` picks `LeeEnvelopment` based on Lee's personality + terrain.
2. Side B's `ArmyOrchestrator` builds `TacticalIntentModel(SideA)` — observes A's force concentration on south sector → infers `PrimaryIntent=Attack, InferredMainEffort=south, Confidence=0.6`.
3. B's CO (McClellan) consumes the model: caution-weighted → triggers defensive replan → picks `McClellanPreparedDefense` → emits intent down: divisions on south sector hold, reserves reinforce south.
4. A's `ArmyOrchestrator` builds `TacticalIntentModel(SideB)` — observes B's reserve shift → infers `PrimaryIntent=Defend, ReserveCommitted=high` → audacity-weighted Lee picks `JacksonValleyShuffle`-style flank departure to north sector.
5. And so on. Plans are partly responses to inferred opposing plans; emergent, not scripted.

## Playbooks

### Playbook structure

A playbook is a plan template that parameterizes `TacticalBattlePlan` rather than scripting specific moves.

```csharp
abstract class TacticalPlaybook {
    string                  Id;                   // "lee_envelopment"
    string                  HistoricalLabel;
    PersonalityFit          MatchProfile;         // which CO archetypes pick this
    TerrainPreference       TerrainFit;           // open/wooded/river/mountain weights
    OddsRange               PreferredOddsBand;    // works at 1.0-1.5×, etc.
    PhasePlan[]             Phases;               // probe → main → exploit, etc.
    SectorRoleAllocator     AllocateSectors(...); // turns map into main/fix/screen
    ReservePolicy           ReserveCommitRules;
    ReplanTriggerSet        ReplanTriggers;       // playbook-specific overrides
}
```

### Seed catalog (14 playbooks)

| Playbook | Profile | Selected when |
|---|---|---|
| `LeeEnvelopment` | aggressive, audacious, methodical | CO matches Lee/Jackson archetype; favorable terrain; odds 0.8-1.4× |
| `JacksonValleyShuffle` | aggressive, audacious, fast tempo | small force; mountainous; odds 0.5-0.9× |
| `McClellanPreparedDefense` | cautious, methodical, low audacity | CO matches McClellan archetype; defensive posture |
| `ShermanManeuverFix` | aggressive, audacious, low caution | open terrain; favorable odds; CO matches Sherman |
| `GrantContinuousAttrition` | methodical, aggressive, high reserve commit | favorable odds 1.3×+; long campaign |
| `LongstreetDefensiveOverslope` | methodical, low audacity, defensive | Lee's senior corps CO when defending |
| `HookerFlankDeparture` | aggressive, methodical, low audacity (loses nerve) | matches Hooker archetype; favorable initial odds |
| `HoodFrontalAssault` | aggressive, low caution, low methodical | desperate force position; CO matches Hood |
| `BurnsideForcedAssault` | low caution, low methodical, externally pressured | political/CIC pressure tag set |
| `BraggIndecisiveCommit` | methodical, low audacity, low aggression | CO matches Bragg; mid-odds |
| `GenericAggressive` | fallback for unknown aggressive COs | high aggression vector, no specific match |
| `GenericCautious` | fallback for unknown cautious COs | high caution vector, no specific match |
| `GenericMethodical` | fallback for unknown methodical COs | high methodical, mid-odds |
| `GenericDesperate` | fallback when own force broken | own army morale below threshold + odds < 0.6× |

Generic fallbacks always score above zero so a playbook is always selected.

### Selection algorithm

Playbook selection runs at battle start and on every replan. Each playbook scored against:

```
score(playbook) =
    personalityFit(playbook.MatchProfile, armyCommander.PersonalityVector) * 0.5
    + terrainFit(playbook.TerrainFit, currentTerrain) * 0.2
    + oddsFit(playbook.PreferredOddsBand, currentOdds) * 0.15
    + opposingCommanderHint(playbook, enemyArmyCommander) * 0.1
    + jitter(seed) * 0.05                                       // breaks ties
```

Highest-scoring playbook wins.

### Personality data coverage

`HistoricalFigureRegistry` (25 officers) covers army echelon. For corps / division / brigade commanders not in the registry, `PersonalityVector` derives from era × faction defaults (`FactionProfiles` + `EraStageManager`) plus rank-tier biases (corps trend methodical; brigade trend aggressive). Coverage gap is acceptable because the personality stack is additive (Slice A locked design choice #3): missing-data commanders get reasonable defaults, not crashes. Telemetry `[TacticalCommanderUnknown] echelon=corps name=…` flags gaps for future registry expansion.

## Vanilla integration: patches

### Inventory

Most are rewires of existing default-off Slice B patches; one is genuinely new.

| # | Patch | Status | Change |
|---|---|---|---|
| #41 | `BattleChargeGatePatch` | rewired | Reads `BrigadeOrchestrator.ChargeDecision` instead of `TacticalReactionContext.DenyCharge`. W&L player-control guard preserved verbatim. |
| #42 | `BattleFeudActionGatePatch` | rewired | Reads `BrigadeOrchestrator.FeudActionGate`. |
| #44 | `BattleMacroStrategyPatch` | rewired | Reads `ArmyOrchestrator.CurrentMacroAi`; vanilla retreat / debug / save-restore short-circuits preserved. |
| #45 | `BattleGroupStancePatch` | rewired | Reads `BrigadeOrchestrator.GroupStance`; charge-stance preservation rule from current code preserved verbatim. |
| #46 | `BattleObjectiveChainWlGuardPatch` | unchanged | Already orthogonal — protects player-subordinate units from objective-chain mutation regardless of who's deciding. |
| #47 | `BattleCommanderIntentObserverPatch` | demoted to telemetry-only at O1, removed at O7 | Stops populating `TacticalReactionContext` (orchestrator does); keeps `[TacticalLocalReaction]` / `[TacticalReserveIntent]` markers until O7 cleanup. |
| #48 | `BattleReserveDoctrinePatch` | rewired | Reads `DivisionOrchestrator.ReserveCommit` + `ArmyOrchestrator.WithdrawalIntent`; snapshot/restore semantics preserved. |
| B7 | `BattleAIBombardmentPatch` | rewired | Reads `DivisionOrchestrator.ArtilleryPrio`. |
| B8a | `BattleLineFallbacksObserverPatch` | rewired | Reads `BrigadeOrchestrator.FallbackDecision`. |
| B8b | `BattleMicroAICheckForRetreatsObserverPatch` | rewired | Reads `BrigadeOrchestrator.RetreatDecision`. |
| B8c | `BattleMicroAICheckForChargesMoraleSnapshotPatch` | unchanged | Pure snapshot writer; orchestrator reads same snapshot. |
| B8d | `BattleUseOfReservesPatch` | rewired | Reads `ArmyOrchestrator.WithdrawalIntent` for help-request/withdrawal triggers. |
| #43 | `TacticalFallbackRetreatNullGuardPatch` | unchanged | NRE guard. |
| #53 | `TacticalPathfinderDisciplinePatch` | unchanged | Pathfinder behavior. |
| #35 | `TacticalObserverPatch` | extended | Calls `TacticalBattleCoordinator.Tick()` and detects battle lifecycle from `inbattle` transitions. Existing `[TacticalDecisionMatrix]` rows behind their config flag until O7. |
| **NEW** | `PlayerSubordinateOrderPatch` | new | Postfix on `AIBattle.UpdateDLCPlayerOrders` (line 6747). After vanilla helpers run, calls `CheckCurrentOrderUpdate(...)` with orchestrator-derived intent. Skipped entirely when `DLC_WL.IsCommanderInChief()`. |

**Net: 1 new patch, 9 rewired, 5 unchanged.** Patch ordinals assigned in implementation plans per the project's stable-ordinal convention.

### Scorer demotion

Existing scorer files stay on disk and stay tested; their callers shift.

| Scorer file | Today | Under orchestrator |
|---|---|---|
| `TacticalDoctrineScorer` | called by #44/#45 | called by `BrigadeOrchestrator` as one input among many |
| `TacticalChargeViability` | called by `TacticalReactionContext` populator | called by `BrigadeOrchestrator.EvaluateCharge` |
| `TacticalSupportScreen` | called by B7 input adapter | called by `DivisionOrchestrator.AllocateArtillerySupport` |
| `TacticalRefuseFlankIntent` | called by intent observer | called by `CorpsOrchestrator.AllocateSectors` |
| `TacticalQuadrantThreatScorer`, `TacticalDestinationDiscipline`, `TacticalMoralePressure`, `TacticalHelpRequest`, `TacticalFatigueState` | called by various scorer aggregators | called by appropriate echelon orchestrator as evidence |
| `TacticalReservePolicyLedger` | called by #48 | called by `DivisionOrchestrator.ReserveCommit` |
| `TacticalArtilleryDoctrine`, `TacticalWithdrawalDoctrine` | called by B7/B8 input adapters | called by `DivisionOrchestrator.ArtilleryPrio` / `ArmyOrchestrator.WithdrawalIntent` |
| `TacticalCommanderIntent`, `TacticalPlaybookLedger` | populated by #47 | **subsumed by orchestrator's playbook + plan subsystems; deleted at O7** |
| `TacticalLocalReactionScorer` | called by `TacticalReactionContext` | called by `BrigadeOrchestrator` |
| `TacticalGateHelpers` | shared W&L gate / alliance bounds | unchanged |
| `TacticalScoreCache<T>` | per-call caching | unchanged |
| `TacticalOrderSettlementGate` | order-friction gate | unchanged; brigade writes pass through |

Net code impact: ~18 scorer files retained as evidence inputs; 2 deleted at O7 (`TacticalCommanderIntent`, `TacticalPlaybookLedger`); ~14 new files under `Tactical/Orchestrator/`.

### Safety + rollback

- **Master flag default-on** (`Enable Tactical Battle Orchestrator = true`). New users get the orchestrator from first launch. Per-phase valves still exist during O0-O6 and ship default-on as each phase passes smoke. At O7 the per-phase valves get collapsed.
- **Per-patch debug switch**: each rewired patch retains a fallback to its current scorer-driven path behind a debug-only switch (e.g., `Force Vanilla #44 Macro Strategy = false` default). For regression triage only; removed at O7.
- **Master flag off**: orchestrator never instantiates; bootstrap detection in `TacticalObserverPatch` no-ops; rewired patches fall back to scorer-driven paths. Vanilla + current Slice B behavior identical to today. This is the rollback boundary.
- **W&L invariants preserved at every writer surface**: every rewired patch's existing W&L guard (`TacticalWlActionGuard`, `TacticalGateHelpers.IsPlayerControlled`) stays in place. Orchestrator decisions flow through those guards, not around them. Denied writes log the orchestrator's intent for telemetry but do not apply.
- **Read-only-mod-state invariant preserved**: Harmony patches still only READ orchestrator state. Orchestrator is the only writer; runs from `TacticalObserverPatch`'s tick.
- **Alliance 2 (Europe) bounds**: existing `TacticalGateHelpers` alliance-bounds checks already cover this.
- **Vanilla short-circuits preserved**: every rewired patch's vanilla-state short-circuits (vanilla retreat / debug / save-restore windows) stay verbatim.

### Build / test impact

- `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` needs explicit `<Compile Include>` entries for new orchestrator files (per the test-project gotcha noted in `CLAUDE.md`).
- New harness coverage required:
  - Per-echelon orchestrator decision tests (per playbook + intent permutations)
  - Intent inference tests (evidence → inferred intent + confidence)
  - Playbook selection tests (personality + terrain + odds → expected playbook)
  - Cascade tests (army intent → corps intent → division intent → brigade decision flow)
  - Replan trigger tests (each trigger fires under correct conditions)
  - Player-subordinate `givenorder` mapping tests (intent → `CheckCurrentOrderUpdate` args)
- Existing harness tests for demoted scorers stay valid — scorers still produce the same outputs; their callers shift.
- Estimated harness growth: from current 509 PASS → ~700-750 PASS once orchestrator is fully covered.

### Vanilla anchor verification gates

- Before O0: re-confirm `inbattle = true / false` setter line numbers (currently 22781 / 22889 / 80708 / 80791-80792 / 81086 / 21535 / 21995). Confirm `TacticalObserverPatch` (#35) tick fires reliably for both AI-vs-AI and AI-vs-player battles.
- Before O6: re-confirm `AIBattle.UpdateDLCPlayerOrders` (line 6747) signature and that its private helpers (`CheckRemovalOfOrders` line 6777, `CheckReserveOrder` line 6813) still issue `CheckCurrentOrderUpdate` calls at lines 6798 / 6804 / 6808 / 6841.
- Before any O*: re-confirm `AIBattle.MicroAICheckForCharges` (line 4905), `AdjustGroupAIStance` (line 4221), `CheckGlobalAIStrategy` (line 6314) line numbers match current decompile (last verified 2026-05-08).

## Phasing

The orchestrator ships as one umbrella spec (this document) with internally phased implementation plans. Each phase ships behind its own per-echelon valve, smoke-verified before the next phase's valve flips default-on.

| Phase | Plan name | Ships | Valve default | Smoke gate before next phase |
|---|---|---|---|---|
| **O0 — Scaffold** | `2026-MM-DD-tactical-orchestrator-scaffold.md` | `TacticalBattleCoordinator`; `EchelonOrchestrator` abstract base; `TacticalCommanderRoster`; rename of current `TacticalBattlePlan.cs` → `TacticalDoctrineDecisionContracts.cs`; `TacticalObserverPatch` (#35) extended to detect battle lifecycle and call `Tick()`. Empty orchestrators (no decisions, telemetry only). | n/a (only telemetry) | `[once:orch-coordinator]`, `[once:orch-bootstrap]`, `[once:orch-teardown]` fire on battle start/end across at least one AI-vs-AI battle and one AI-vs-player battle. No exceptions. Coordinator survives mid-battle save/reload. |
| **O1 — Army echelon + plan + playbooks** | `2026-MM-DD-tactical-orchestrator-army.md` | `ArmyOrchestrator`; `TacticalPlaybookCatalog` with 14 seeded playbooks; playbook selection; #44 macro stance rewired to read army; replan trigger logic. #47 demoted to telemetry-only. | `Enable Tactical Orchestrator Army = true` (after smoke) | `[TacticalPlan]` lines fire with valid playbook IDs at battle start; `[TacticalReplan]` fires on at least one trigger; macro stance writes match orchestrator's `CurrentMacroAi`; no regressions in current Slice B default-off scorer behavior when army valve off. |
| **O2 — Intent inference + adversarial loop** | `2026-MM-DD-tactical-orchestrator-intent.md` | `TacticalIntentModel` per opposing echelon; evidence pipelines (visible-state filters); confidence-weighted personality consumption; replan-on-intent-shift trigger. Both army orchestrators see and react to each other. | `Enable Tactical Orchestrator Intent Inference = true` (after smoke) | AI-vs-AI battle log shows `[TacticalIntent]` lines on both sides with non-zero confidence; one or more `[TacticalReplan] trigger=enemy-intent-shift` events observed; personality bias visible (McClellan-archetype CO triggers defensive replan at lower confidence than Lee-archetype). |
| **O3 — Corps echelon** | `2026-MM-DD-tactical-orchestrator-corps.md` | `CorpsOrchestrator`; corps intent cascade from army; sector role allocation; corps-level intent inference; #46 / #42 W&L guards re-validated under corps authority. | `Enable Tactical Orchestrator Corps = true` (after smoke) | `[TacticalCascade] army→corps` lines fire for every corps; sector role allocation produces non-empty main-effort/fix/screen sets; W&L player-subordinate units never receive corps-issued orders that bypass the W&L gate. |
| **O4 — Division echelon** | `2026-MM-DD-tactical-orchestrator-division.md` | `DivisionOrchestrator`; division intent cascade from corps; group stance allocation; reserve commit decisions; #48 + B7 + B8 reserve/artillery rewired to read division. | `Enable Tactical Orchestrator Division = true` (after smoke) | `[TacticalCascade] corps→division` lines fire; reserve commits trigger at orchestrator-decided times rather than vanilla; artillery prioritization shifts visibly with division intent change. |
| **O5 — Brigade echelon** | `2026-MM-DD-tactical-orchestrator-brigade.md` | `BrigadeOrchestrator`; brigade decision struct; #45 group stance + #41 charge gate + B8 fallback/retreat patches rewired to read brigade. | `Enable Tactical Orchestrator Brigade = true` (after smoke) | `[TacticalCascade] division→brigade` lines fire for every brigade; group stance writes flow from brigade decision; charge gates and fallbacks remain bounded. |
| **O6 — Player subordinate integration** | `2026-MM-DD-tactical-orchestrator-player-subordinate.md` | `PlayerEchelonShim`; new `PlayerSubordinateOrderPatch` Postfix on `AIBattle.UpdateDLCPlayerOrders`; intent-to-`CheckCurrentOrderUpdate` mapping table; `commanderrelations` plumbing exposed but not yet consumed. | `Enable Tactical Orchestrator Player Orders = true` (after smoke) | Player-subordinate test campaign (player at brigade level) shows `DLC_WL.givenorder` updating from orchestrator-derived intent on every replan; orders make tactical sense relative to division/corps/army intent; player can comply or ignore without crashes; intent change cadence matches orchestrator replan cadence (no spam). |
| **O7 — Cleanup** | `2026-MM-DD-tactical-orchestrator-cleanup.md` | Delete `TacticalCommanderIntent.cs` + `TacticalPlaybookLedger.cs` + #47 patch; remove per-patch fallback-to-scorers debug switches; collapse per-echelon valves; remove `[TacticalDecisionMatrix]` code path from #35; remove `Enable Tactical Decision Matrix Logging` flag (no-op). | n/a | Multi-battle smoke (≥3 AI-vs-AI, ≥3 AI-vs-player at varying ranks) shows orchestrator-driven decisions producing stable, coherent battles with no exception spam, no Harmony failures, and no regressions in win/loss patterns. |
| **OC — Relations-driven order compliance** | `2026-MM-DD-tactical-orchestrator-relations.md` | Reads `CommanderRelations.influenceval[cause]` to bias whether AI subordinates execute orchestrator's order immediately, with delay, or dispute it; writes back to one new "obeyed/ignored orders" cause column. Subsumes the prior Slice C release as the orchestrator's relations layer. | `Enable Tactical Orchestrator Relations Compliance = true` (after smoke) | AI subordinates with low-relations COs visibly delay orchestrator orders; dispute telemetry `[TacticalRelations] subordinate=… order=… action=delay/dispute` fires; player-side relations effects mirror; no order-loss regressions. |

### Cross-phase invariants

Verified at every gate:
- Console harness PASS count never decreases.
- `./build.sh` produces 0 warnings / 0 errors at every commit.
- `dist/WhiskeyRealism.dll` and deployed BepInEx plugin SHA-256 match before every smoke claim (the recurring failure mode in current handoff entries).
- `LogOutput.log` mtime confirms smoke evidence is from the deployed DLL, not a stale prior build.
- W&L player-subordinate units never receive orchestrator orders that bypass `TacticalGateHelpers.IsPlayerControlled`.

### Risk register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Vanilla `inbattle` transition detection unreliable | Med | Bootstrap fires wrong / not at all | O0 must verify via decompile + first-fire smoke before any other phase |
| Intent inference produces wild plans on sparse evidence | High | Orchestrator picks bad playbook → bad battles | Confidence floor on inference; generic-fallback playbooks always score above zero; replan rate-limit prevents thrash |
| Player-subordinate `givenorder` spam | Med | Player UI flooded with order changes | Vanilla's existing line-8643 dedup conditions handle this for free; reuse as the only re-issue guard |
| Personality coverage gaps for non-historical commanders | Med | Some armies fight identically | Era × faction defaults from `FactionProfiles` + rank-tier biases; explicit `[TacticalCommanderUnknown]` telemetry to identify gaps |
| O3-O5 cascade introduces order-friction violations | Med | Brigades execute orders before vanilla courier delay completes | Existing `TacticalOrderFriction` + `TacticalOrderSettlementGate` from prior B5 settlement work gate every brigade write |
| Mega-slice timeline | High | Multi-month workstream, easy to stall | Each phase ships independently; if O3 stalls, O0-O2 still represent shippable progress; OC can be deferred indefinitely without breaking O0-O7 |
| Master-flag default-on regression | Med | New users hit orchestrator bug from first launch | Per-patch fallback-to-scorers debug switch retained through O6; users can revert via config without rebuild; removed only at O7 after multi-battle smoke |
| Slice B + C scope collapse breaks downstream Slice D dependencies | Low | Historical-flavor slice depends on having clear B/C surfaces to weight | Spec explicitly notes Slice D inherits orchestrator's plan/intent surfaces as its weighting hooks |

## Telemetry

One-line markers, gated by an observer config; replace much of the current `[TacticalDecisionMatrix]` chatter once orchestrator is live.

```
[TacticalPlan] side=union plan=mcclellan_defense phase=probe mainEffort=southSector confidence=0.62
[TacticalIntent] side=union seesEnemy=lee_envelopment confidence=0.58 evidence=south-concentration,reserve-uncommitted
[TacticalReplan] side=csa trigger=enemy-intent-shift from=lee_envelopment to=jackson_valley_shuffle
[TacticalCascade] side=csa army→corps=anv-1st intent=fix-center weight=0.4
[TacticalCommanderUnknown] echelon=corps name=Jubal_Early
[TacticalRelations] subordinate=Hood order=charge-center action=dispute relations=-0.6   (OC only)
```

## Scope notes and what this spec replaces

- **Subsumes Slice C (W&L hierarchy AI)** into the orchestrator + OC. Prior `2026-05-05-tactical-brain-design.md` umbrella spec remains authoritative for Slice B's evidence/scorer layers but is no longer the authority on tactical decision authority.
- **Does not replace** `2026-05-05-tactical-brain-design.md`, `2026-05-05-tactical-brain-vanilla-verification.md`, `2026-05-05-tactical-weapons-ammunition-design.md`, `2026-05-07-tactical-b3-b5-odds-macro-sector-design.md`, `2026-05-07-tactical-b6-commander-intent-local-reaction-design.md`, `2026-05-08-scourge-tactical-adaptation-design.md`, `2026-05-08-scourge-operational-recon-commitment-design.md` — those remain the authoritative descriptions of evidence inputs the orchestrator consumes.
- **Per-phase implementation plans** (O0-O7 + OC) get their own `docs/superpowers/plans/` documents and are not pre-written here.
- **CLAUDE.md / `docs/handoff.md` updates** required when this spec lands: note that Slice B (active) now folds Slice C (deferred) into the orchestrator workstream; v0.3.0 ship target includes O0-O5 (or whichever phases are smoke-verified by then); v0.4.0 includes O6-OC.

## Open issues (none deferred)

All five brainstorm-stage open questions are locked in §"Locked decisions". Future per-phase plans may surface new open issues; those are tracked there, not here.

## Known follow-ups (post-O1 smoke, 2026-05-08)

These are tuning concerns observed during O1 in-game smoke. They do not block O2-O5 but should be addressed before any release that depends on plan-selection quality.

1. **`HistoricalFigureRegistry` coverage too sparse.** O1 smoke battle paired David Hunter (Union) vs P.G.T. Beauregard (CSA) — both notable Civil War commanders, neither in the registry. Both fell back to `FactionProfiles.For(allianceId)` defaults plus rank-tier biases. The 25-officer registry from Slice A covers iconic commanders but misses many corps/army-tier commanders the player will encounter in W&L scenarios. Expand registry with at least: Beauregard, Hunter, Pope, Rosecrans, Thomas, Sheridan, Hancock, Reynolds, Sedgwick, A. P. Hill, D. H. Hill, Polk, Hardee, Cleburne, Ewell, Stuart. ~20 entries; mechanical work.

2. **`PersonalityFit` formula doesn't differentiate sharply when commander vector is mid-magnitude.** O1 smoke had Beauregard (CSA, unmatched, faction-default vector) selecting `ShermanManeuverFix` despite Sherman being a Union playbook. Hunter (Union) selected `GenericCautious` despite his historical aggression. Root cause: `(dot + 3) / 6` normalization in `PersonalityFit.Score` collapses faction-default vectors (mid-magnitude in all axes) into similar score bands, so terrain + odds tiebreakers dominate selection. Two candidate fixes:
   - Adopt true cosine similarity (`dot / (||fit|| × ||commander||)`) — naturally penalizes magnitude mismatches.
   - Add a faction-fit term to playbook score: each playbook tagged Union/Confederate/either; mismatched faction multiplies the personality score by ~0.5.

   Either fix will require updating tests in Tasks 4-6 of the archived O1 plan since current expected selections rely on the existing formula. Best done as a single targeted slice rather than mixed into O2/O3.

3. **`AggressionBias01` -> `CurrentMacroAi` may be too binary at Probe phase.** Current logic in `ArmyOrchestrator.CurrentMacroAi` returns `-1` (dynamic) at Probe phase when `Aggression <= 0.3`, otherwise `1` (attack). O1 smoke had both sides emit `[once:orch-macro-write:…->-1]` because both COs' faction-default Aggression was at or below the threshold. That's a sensible no-op outcome but the cliff at 0.3 is arbitrary. Consider a smoother mapping or letting the playbook's aggression bias drive the macro decision rather than the commander's raw vector. Address as part of the personality formula slice (#2) or as a small follow-up after O5 ships.

4. **No replan loop wired in O1 runtime.** `ArmyOrchestrator.CheckReplanTriggers` is implemented and unit-tested, but no runtime caller feeds it inputs each tick. O2 (intent inference + adversarial loop) is the natural home for `ArmyTickCycle.MaybeReplan` — see the O2 sketch. Without it, plans never advance phase or re-pick during a battle.
