# Scourge Operational Recon and Commitment Design

Status: active design supplement. Slice C — operational/campaign-map recon and commitment doctrine. Companion to [`2026-05-08-scourge-tactical-adaptation-design.md`](2026-05-08-scourge-tactical-adaptation-design.md), which owns tactical-battle adaptation. This document does not authorize new runtime writes by itself; per-component implementation plans gate all writes.

Scope: the AI on the campaign map plans against what its alliance can actually see (recon-grounded), commits force based on Scourge-derived ratio doctrine, concentrates before contact, assigns advance-guard / picket roles asymmetrically by attacker/defender posture, coordinates convergent multi-arm attacks, and tracks per-operation commitment with abort criteria. Tactical-battle behavior remains owned by Slice B.

## Decision

Six locked design choices for this slice:

1. **Sighting bridge — extend, don't add.** A new `SightingObserver` plugs into the existing `StrategicCoordinator.RunStrategicReview()` pipeline at `StrategicCoordinator.cs:267` after `WarStateObserver`. It produces a `SightedEnemySnapshot` per AI alliance per tick. Three existing ledgers consume the snapshot as an input: `FrontSectorRuntime.AddUnits()` filters strength accumulation by sighting state; `ContactEvidenceInput` gains a `SightingQuality` field; `DefenseIntentRuntime.BuildInput()` filters enemy-position iteration. **No new top-level ledger.** This is the architectural foundation for everything else in this slice.
2. **Default-on for AI alliances; player keeps vanilla.** AI alliances always plan against fog/sighting state. Player-aligned forces retain vanilla omniscient controls. The asymmetry is a deliberate design choice, not a bug: the design intent is that the AI behaves like Lee/Meade with imperfect information, while the player retains the full UI they already have.
3. **Daily campaign-map cadence.** Sighting diff and downstream commit/retreat decisions run on the existing daily strategic-review cycle (Slice A's daily migration on 2026-05-04 is the reference cadence). No new Harmony patches; the existing `MonthlyTickHookPatch` continues to drive the loop. Within-day sighting changes are not modeled — operational tempo is days, not hours.
4. **Force-ratio commit/retreat extends `OperationalProbeRuntime`.** Scourge's `AttackEnemy(base, attacker)` rank-aware threshold and combined-arms penalty become a `ForceRatioGate` scorer inside the existing probe runtime, not a new ledger. Output: `Commit / Probe / Withdraw / NoContact`. Consumed via the existing probe overlay path into formation directives.
5. **Convergent multi-arm phasing — net-new ledger.** Nothing existing models "arm A fixes while arm B converges; phase 2 fires only when both arms reach line of departure." A new `ConvergentOperationLedger` plumbs into `CoordinatedOperationRuntime` as a new operation type. This is the only new top-level ledger this slice authorizes.
6. **Commitment / abort doctrine extends `OperationDecisionMemory`.** Per-operation commitment tracking (force-spent, force-committed, force-uncommitted, time-since-progress) lives in existing memory infrastructure. Abort handoff fires when stall exceeds N hours with M% loss without territorial gain.

## Source Boundary

Reviewed local sources to derive this spec:

- Scourge of War SDK source: `/mnt/c/Program Files (x86)/Steam/steamapps/common/Scourge Of War - Remastered/sdk/SowCampAI/campai.cpp`, `SowAiInf/offai.cpp`, and shared headers `SowMod/xunit.h`, `xunitdef.h`, `xlink.h`.
- Grand Tactician decompile: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`. Regenerate per the steps in `docs/findings.md` if `/tmp` was wiped.
- Whiskey existing strategic surface: full enumeration of `src/WhiskeyRealism/Strategic/` (100 files as of this writing).
- Audit reports embedded in this spec under "Current State" reflect a structured Whiskey intel-surface audit and a campaign-layer cadence audit.

Scourge is comparative design evidence only; do not copy code, tables, constants, strings, or assets. Whiskey implements original C# logic against verified Grand Tactician methods and fields. This spec also does not require Scourge to be installed at build or runtime.

## Current State (Baseline From Audit)

Structured audits before this design confirm the gaps the Slice C work must close.

### Whiskey strategic intel surfaces — current behavior

None of the seven core campaign-map ledgers consumes vanilla sighting/fog state today. They aggregate omniscient unit lists from `aifaction[].ownunits` and `.enemyunits`:

| Ledger | What it actually reads | Consumes vanilla sighting? |
|---|---|---|
| `WarStateObserver` (`WarStateObserver.cs:35-57`) | Town owner, commander status, alliance morale, battle history | No |
| `StrategicCadencePolicy` (`StrategicCadencePolicy.cs:5-32`) | Day counters and source-signature change detection | No |
| `CicReviewRouter` (`CicReviewRouter.cs:12-62`) | `PhaseTruthOutput` action recommendations | No |
| `ContactEvidenceLedger` (`ContactEvidenceLedger.cs:43-124`) | Hardcoded floats injected via `ContactEvidenceInput` (CurrentEnemyStrength, CurrentFriendlyStrength) | No |
| `CampaignMapLedger` / `CampaignMapRuntime` (`CampaignMapLedger.cs:77-322`, `CampaignMapRuntime.cs:8-263`) | `BattleUnits.towns`, `.harbors`, `.fort` (static catalog) | No |
| `FrontSectorRuntime` (`FrontSectorRuntime.cs:140-171`) | `aifaction[].ownunits` and `.enemyunits` lists; reads `groupstrengthactive`, `groupstrengthdirect`, `groupstrength`, `strength` | No |
| `DefenseIntentRuntime` (`DefenseIntentRuntime.cs:51-170`) | `aifaction[].enemyunits` via reflection at line 141; iterates without visibility filter at 144-149 | No |

The strategic-review pipeline at `StrategicCoordinator.cs:221-366` invokes these ledgers in a fixed order on every daily tick. The chokepoint for omniscient-vs-sighted enemy strength is `FrontSectorRuntime.AddUnits()` at `FrontSectorRuntime.cs:157` — once strength flows past that line, every downstream decision (operational probes, formation directives, theater pressure, CIC plan reviews) inherits the omniscient view.

### Scourge equivalent — recon-driven campaign event model

Scourge campaign AI is event-driven on visibility (`SowCampAI/campai.cpp` consuming `ESBCCampAI` enum from `SowMod/xunitdef.h:72-82`):

| Event | Scourge handler | Behavior |
|---|---|---|
| `eSBCEnemySeen` | `campai.cpp:1493-1510` | Concentrate via `MergeWithSubordinate`, then `AttackEnemy(base, attacker)` force-ratio gate — commit (`eComSBCBattle`) or pull back (`eComSBCRetreat`). |
| `eSBCEnemyGone` | `campai.cpp:1512-1700` | Hunt via `ChaseEnemy`, switch to occupied-town pursuit, or fall back to `AttackerSubDeployment`. Behavior branches by campaign objective type (`eSBCDestroy / eSBCOneTown / eSBCMajTown / eSBCBegTown`). |
| `eSBCArrive` | `campai.cpp:1323-1490` | On town arrival: merge subordinates, detach picket via `CorpPicket`, or detach advance guard via `CorpAdvanceGuard`. |
| `eSBCThink` | `campai.cpp:1957-2255` | 30-minute heartbeat reassessment — Scourge's tactical-tempo equivalent of Whiskey's daily review. |
| `eSBCInit` | `campai.cpp:1276-1322` | Initial split-up at corps level; `AttackerSubDeployment` vs `DefenderSubDeployment` selects asymmetric layout. |

This is the model Whiskey is adapting. The Whiskey versions of these events fire from the daily diff in `SightingObserver`, not from Harmony patches.

## Grand Tactician Anchor Map

| Concept | Scourge evidence | Grand Tactician anchor | Whiskey integration point |
|---|---|---|---|
| Per-alliance fog of war truth | (Scourge has no campaign FOW; `eSBCEnemySeen/Gone` is the visibility-change event itself.) | `FogOfWar` class at 100570; `Regiment.fows` (111348), `fow` (111350), `oppositefow` (111352); `Regiment.unitrevealedbyenemy` (110946); `Regiment.lastspotted` (142604), `Regiment.spottedmarks` (142606). | `SightingObserver` reads these per AI alliance to build `SightedEnemySnapshot`. |
| Intel gathering rate per unit | `xunit.h` has `MorUnitBon()` only; no campaign-intel field. | `Regiment.groupintelligencegathering` (110886), modulated by `GamePrefs.intelligencegatheringbureauofmilitaryinformation[GetPerkLevel(3)]` (50422, 120604/120608). | `SightingObserver` weights detection probability per unit by intel rate. |
| Information delay (BMI perk) | (Scourge has no analog.) | `GamePrefs.informationdelaybureauofmilitaryinformation[GetPerkLevel(3)]` (50424, 142837/142841); 3-tier perk. | `SightingObserver` discounts strength estimates older than the perk-derived staleness threshold. |
| Force-ratio commit/retreat | `campai.cpp:1030-1090` `AttackEnemy(base, attacker)` with rank-aware thresholds and combined-arms penalty (`OnlyCavalry` halves). | No direct vanilla method; Whiskey computes from `aifaction[].ownunits` and the sighted enemy list via `Regiment.groupstrengthactive` / `groupstrengthdirect`. | `OperationalProbeRuntime.ForceRatioGate` scorer (extension). |
| Pre-battle concentration | `campai.cpp:1495 / 1529 / 1606` `MergeWithSubordinate(base)`. | Vanilla `aiorders` plus `Regiment.unitlinkedto`; movement orders flow through `WlStrategicOrderBridge`. | `FormationDirectiveLedger` gains `ConcentrationIntent` field. |
| Advance guard detachment | `campai.cpp:931 CorpAdvanceGuard` (mobile, ranks ahead of main body). | `AssetRoleScorer` already classifies assets; vanilla `Regiment.permanentlydetached` and `groupaiobject` provide hierarchy. | `AssetRoleScorer` gains `AdvanceGuard` role tag. |
| Picket detachment | `campai.cpp:1461 CorpPicket` (small detachment on town arrival). | Same vanilla anchors; town occupancy via `BattleUnits.towns` and `CBuilding.Owner`. | `AssetRoleScorer` gains `Picket` role tag. |
| Asymmetric attacker/defender doctrine | `campai.cpp:699-910` `AttackerSubDeployment` vs `DefenderSubDeployment`; `SplitUp` doctrine differs by side. | Whiskey already has `OperationPosture` and `GrandStrategyProfile`; no direct vanilla anchor needed. | `AssetRoleScorer` and the new `ConvergentOperationLedger` consume posture to bias roles. |
| Convergent multi-arm phasing | (Scourge has no analog. Net-new for Whiskey.) | Whiskey existing `Phase` (`Phase.cs`) and `PhaseTruthLedger`; `CoordinatedOperationPackageLedger` already models multi-army packages. | New `ConvergentOperationLedger` ledger; consumed by `CoordinatedOperationRuntime`. |
| Per-operation commitment tracking | (Scourge has no explicit ledger; commitment is implicit in `eSBCRetreat` on force-ratio failure.) | Whiskey existing `OperationDecisionMemory`; vanilla provides battle-history outcomes via `BattleHistoryQuery`. | `OperationDecisionMemory` extension. |

## Components

### A. SightingObserver (foundation)

Purpose: produce the `SightedEnemySnapshot` that downstream ledgers filter on.

Position in pipeline: invoked from `StrategicCoordinator.RunStrategicReview()` at the existing line currently between `WarStateObserver.Observe()` (line 267) and `PublishDirectorPosture()` (line 282). The exact insertion line is determined at implementation time; the ordering invariant is "after WarStateObserver, before any ledger that reads enemy strength."

Inputs:

- per AI alliance: enumerate `aifaction[allianceId].enemyunits`;
- for each enemy unit: read `unitrevealedbyenemy[allianceId]` (110946), `fow[allianceId]` or equivalent fog grid lookup (111348-111352), `lastspotted` timestamp (142604) and `spottedmarks` history (142606);
- per AI alliance, for the observing side: per-perk intel gathering rate `GetPerkLevel(3)` and `groupintelligencegathering` (110886), and BMI delay `informationdelaybureauofmilitaryinformation[GetPerkLevel(3)]` (50424).

Output struct (logical shape; concrete C# defined in implementation plan):

```
SightedEnemySnapshot {
    int AllianceId;
    DateTime SnapshotDay;
    Dictionary<int, SightedUnit> Units;          // keyed by Regiment.GetInstanceID()
    HashSet<int> NewlySeen;                      // entered visibility this tick
    HashSet<int> NewlyHidden;                    // left visibility this tick
}

SightedUnit {
    int UnitId;
    SightingQuality Quality;                     // None / Stale / Partial / Confirmed
    Vector3 EstimatedPosition;                   // last known
    float EstimatedStrength;                     // staleness-discounted
    DateTime LastSeenDay;
    int StalenessDays;                           // computed from BMI delay
}
```

`SightingQuality` ladder:

- `None`: never sighted, OR last sighted more than `BMI_FORGET_DAYS` ago (default 7 days, perk-modified).
- `Stale`: sighted but `lastspotted` is older than `BMI_STALE_DAYS` (default 2 days, perk-modified). Strength estimate uses last-known value with an aging discount.
- `Partial`: sighted within `BMI_STALE_DAYS` but with low intel rate (`groupintelligencegathering < 0.5`). Position is reliable; strength is approximate.
- `Confirmed`: sighted within current tick, high intel rate. Use vanilla strength fields directly.

`NewlySeen` and `NewlyHidden` are the diff between this tick's observation and the prior tick's snapshot — the Whiskey analogues of `eSBCEnemySeen` / `eSBCEnemyGone`.

Persistence: in-memory only, rebuilt per tick. The prior snapshot is retained for one tick to compute the diff and is then discarded. No JSON sidecar entry.

### B. SightingQuality consumers (existing-ledger extensions)

Three existing ledgers gain a sighting-aware path. None duplicates the snapshot; each accepts the snapshot reference as a constructor or method parameter.

#### B1. `FrontSectorRuntime.AddUnits` filters strength

File: `FrontSectorRuntime.cs`. Method: `AddUnits()` at line 140-171. Insertion point: before the strength read at line 157.

Change semantics: when `AddUnits` is invoked for the enemy side, it accepts the `SightedEnemySnapshot` for the observing alliance. For each enemy unit, look up the unit in `snapshot.Units`; if `Quality == None`, skip (do not add to sector strength); if `Stale`, scale strength by an aging factor (e.g., `1.0 - (StalenessDays / BMI_FORGET_DAYS)`); if `Partial` or `Confirmed`, use full strength. Own-side `AddUnits` is unchanged.

Downstream consumers inherit the change automatically: theater pressure (`TheaterPressureView`), CIC phase truth, formation directives, operational probes.

#### B2. `ContactEvidenceInput` gains `SightingQuality` field

File: `ContactEvidenceLedger.cs`. Change: extend the input struct with `SightingQuality EnemyVisibility`. `ContactEvidenceLedger.Evaluate(...)` (line 43-124) gates escalation: enemy-strength surge with `Quality == None` produces `NoContact` regardless of the raw delta; with `Stale`, escalation requires a higher delta threshold than `Confirmed`. The existing strength-comparison logic at line 76-79 stays; the gate is upstream of it.

#### B3. `DefenseIntentRuntime.BuildInput` filters enemy positions

File: `DefenseIntentRuntime.cs`. Method: `BuildInput()` enemy iteration at line 137-152. Insertion point: inside the for loop at line 144, after the active-check, before the position add. Skip the unit if `snapshot.Units[unit.id].Quality == None`. Include with cached estimated position (not live position) if `Stale` or `Partial`.

Threat scorers (`ProximityThreat`, `AssetRoleScorer`) consume the filtered list unchanged.

### C. ForceRatioGate (Scourge `AttackEnemy` translation)

File: `OperationalProbeRuntime.cs` (extension; current line count 126). Adds a new scorer alongside the existing escalation logic.

Inputs:

- own-side strength from `FrontSectorLedger[allianceId]`;
- enemy strength from the sighting-filtered `FrontSectorLedger[opposingAllianceId]` (B1 ensures this is recon-grounded);
- combined-arms composition from `aifaction[allianceId].ownunits` (count infantry / cavalry / artillery formations);
- own posture: attacker or defender, derived from `OperationPosture` and grand-strategy profile;
- own-rank vs opposing-rank: derived from formation-level depth (`FormationLevel.cs` enum);
- commander initiative from `commander.GetCommanderInitiative()`.

Force-ratio decision logic (Scourge `AttackEnemy` translated; `campai.cpp:1030-1090`):

```
ratio = ownEffectiveStrength / max(1, enemyEffectiveStrength)

ownEffectiveStrength = sumOwnStrength * (mono-arm penalty: 0.5 if cavalry-only OR artillery-only)
enemyEffectiveStrength = sumSightedEnemyStrength * (same mono-arm penalty if observable)

# Same-rank parity:
if attacker AND ratio >= 0.85:    Commit       # Scourge: mydivisions >= enemydivisions - 1
elif ratio >= 1.00:               Commit       # parity-or-better

# Superior rank (own formation level > enemy):
elif own.rank > enemy.rank AND attacker AND ratio >= (1.0 - 0.15 * rankDifference):
                                  Commit
elif own.rank > enemy.rank AND ratio >= 1.0:
                                  Commit

# Inferior rank:
elif own.rank < enemy.rank AND attacker AND ratio >= 1.0:
                                  Commit
elif own.rank < enemy.rank AND ratio > 1.0:
                                  Commit       # strict inequality

# Otherwise:
elif ratio >= 0.7:                Probe        # closer-look sortie
elif ratio >= 0.5:                Withdraw     # pull back, do not engage
else:                             NoContact    # too weak; rely on screen
```

The thresholds are configurable via Plugin config and tunable per faction profile. Commander initiative shifts the `Commit` threshold by ±0.05 within a band (`CommanderInitiative > 0.7` → more aggressive; `< 0.3` → more conservative), mirroring the personality modifier referenced in Slice A.

Output: `ForceRatioDecision { Commit, Probe, Withdraw, NoContact }` plus the underlying ratio for telemetry.

Consumers: `FormationDirectiveLedger` (existing) reads the decision via the same probe-overlay path it currently uses for `OperationalProbeLedger` outputs. No new write surface beyond what `OperationalProbeRuntime` already feeds.

### D. ConcentrationIntent (Scourge `MergeWithSubordinate` translation)

File: `FormationDirective.cs` and `FormationDirectiveLedger.cs` extension. No new ledger.

Trigger: when `SightedEnemySnapshot.NewlySeen` contains an enemy formation within the directive's theater AND the directive's brigade is currently dispersed (sub-units not in the same `groupaiobject`), the directive sets `ConcentrationIntent = MergeWithParent` for the relevant unit. The existing `WlStrategicOrderBridge` (line 245+ in `CoordinatedOperationRuntime.cs`) issues attach orders via vanilla `aiorders`.

Concentration completes when all sub-units share `groupaiobject` with the parent or are within a merge distance derived from `GamePrefs.aidefensivemaxrange` (122719). If reflection on `aidefensivemaxrange` fails, the implementation falls back to a Whiskey-side default of 1500 game units (matching Slice A's existing army-area attach radius); the fallback is logged once via `OnceLog`. On completion, the directive clears the intent.

Player-subordinate gate: any unit failing `(ai_feudstance == -1) | (isplayeraiorfeud == 2)` is skipped — concentration writes only happen on AI-controlled forces.

### E. Operational role tags (Scourge advance guard / picket translation)

File: `AssetRoleCatalog.cs` and `AssetRoleScorer.cs` extension. Two new tags added to `AssetStrategicRole`:

- `AdvanceGuard`: mobile, lighter formation, posted ahead of the main body; accepts contact and calls back to the parent. Scoring favors cavalry and light infantry with high `Regiment.GetCommanderInitiative()`.
- `Picket`: small detachment posted at a town or road junction; falls back on contact. Scoring favors infantry detachments with adequate fatigue and morale; rejects artillery-only detachments per Scourge's `OnlyArtillery` exclusion at `campai.cpp:819, 894`.

Asymmetric assignment (Scourge `AttackerSubDeployment` vs `DefenderSubDeployment` doctrine, `campai.cpp:699-910`):

- when `OperationPosture == Offensive`: prefer `AdvanceGuard` for mobile sub-units; minimize pickets except at supply nodes.
- when `OperationPosture == Defensive`: prefer `Picket` for forward sub-units; place advance guards only on cavalry screens.

These are role tags in the existing scorer; downstream consumers (`RecruitmentIntentLedger`, `FormationDirectiveLedger`) already read role tags to bias selection. No new ledger.

### F. ConvergentOperationLedger (net-new)

File: new `src/WhiskeyRealism/Strategic/ConvergentOperationLedger.cs`. The only new top-level ledger this slice authorizes.

Concept: a convergent operation is a multi-arm package where one arm fixes an enemy formation while a second arm converges to the enemy's flank or rear. Phase 2 (the decisive blow) fires only when both arms have reached their respective lines of departure (LoD). If one arm is fixed or destroyed before the other reaches its LoD, the operation aborts.

Phase model:

| Phase | Trigger | Exit | Abort criteria |
|---|---|---|---|
| `Approach` | Operation created, both arms moving toward LoDs | Both arms within `LoDArrivalRadius` of their LoD | Either arm > `MaxApproachLossPct` or sighted enemy strength on either arm > 1.5× planned ratio |
| `ArmsAtLineOfDeparture` | Both arms at LoD (sighting and snapshot confirm) | After `LoDDwellHours` (default 6 game-hours), or earlier if main effort signals ready | Either arm sighted enemy contact lost (`NewlyHidden`) OR fixing arm's force ratio drops below 0.6 |
| `Convergence` | Both arms move from LoD toward objective | Either arm reaches the objective OR engagement begins | Sighting-loss on the converging arm; CIC ordered abort |
| `Decision` | Engagement begins; battle handled by Slice B | Battle resolves | (Slice B owns) |

Inputs:

- `CoordinatedOperationPackageLedger` provides the multi-army package (existing);
- `SightedEnemySnapshot` provides per-arm enemy contact;
- `FrontSectorRuntime` (sighting-filtered) provides per-arm strength estimates;
- `OperationDecisionMemory` provides the abort handoff target.

Output: `ConvergentOperation { OperationId, Phase, Arms[FixingArm, ConvergingArm], LoDArrivalRadius, NextEvalDay, AbortReason? }` per active convergent operation.

Consumers: `CoordinatedOperationRuntime.AddCommitPlan(...)` (line 245 in `CoordinatedOperationRuntime.cs`) gains a new operation type that routes through `ConvergentOperationLedger` instead of the standard single-arm package. CIC reads the active phase to decide whether to allow new commitments.

Configuration: convergent operations are default-off behind `EnableConvergentOperations` config until in-game smoke verifies bounded telemetry, no repeated exceptions, no player-subordinate retasking, and no infinite-loop in the phase advance. The default-off boundary mirrors B7/B8's default-off discipline.

Persistence: active convergent operations persist in the JSON sidecar via the existing `PersistenceDto` pattern. Phase plus arm references plus next-eval day are sufficient to re-hydrate.

### G. Commitment / abort doctrine (existing ledger extension)

File: `OperationDecisionMemory.cs` extension. No new ledger.

New per-operation fields:

- `ForceCommitted`: total strength of all formations assigned to the operation;
- `ForceLost`: strength casualties since operation start (computed via `BattleHistoryQuery`);
- `ForceUncommitted`: strength of the operation's reserve;
- `LastProgressDay`: most recent day the operation made measurable progress (territorial gain, objective captured, or enemy formation routed);
- `AbortThreshold`: configured per operation type (default: stall ≥ 5 days AND `ForceLost / ForceCommitted >= 0.30`).

Daily evaluation: in `RunStrategicReview()`, after `OperationalProbeRuntime` and before `ConvergentOperationLedger`, iterate active operations. If `(currentDay - LastProgressDay) >= AbortThreshold.StallDays` AND `(ForceLost / ForceCommitted) >= AbortThreshold.LossPct`, fire abort. Abort routes to `WlStrategicOrderBridge` to issue `RetreatTo` orders for the operation's formations and clears the operation from active CIC plans.

Player-subordinate gate: operations involving player-subordinate forces never auto-abort; commitment ledger surfaces the recommendation for the player to consume but does not write.

## Slice Integration

### Slice A (shipped)

No retroactive change. Slice A's strategic brain (era × faction × officer scoring, CIC + theater commanders, daily ledgers) is unchanged. Slice C plugs into the existing pipeline at three named insertion points (sighting observer after WarStateObserver; force-ratio gate inside operational probe; ConvergentOperationLedger after probe). All three are additive.

The migration to daily cadence (Slice A handoff 2026-05-04) is the cadence Slice C inherits. Monthly tick remains the rollover boundary; Slice C does not introduce a new tick.

### Slice B (in flight — tactical brain)

Slice C is operational, Slice B is tactical. The boundary:

- Slice C makes the campaign-map decision to commit (or not) to a battle, which arm fixes, which arm converges, when to abort.
- Slice B owns everything inside the battle once it starts: charge gates, fallback discipline, support screens, morale pressure, destination discipline.

When a battle begins, Slice C passes the operation context (which arm is fixing, which arm is converging, abort threshold) to the tactical brain via `TacticalBattleContext.cs`. The tactical brain reads this as additional doctrine input for B6 commander intent and B6c local reactions but does not modify the operation context.

When a battle ends, Slice C reads the outcome via `BattleHistoryQuery` and updates `OperationDecisionMemory.LastProgressDay` and `ForceLost`. Convergent operations advance phase or abort based on outcome.

### Slice D (deferred — implied by this work)

Slice C creates clean hooks for future slices:

- a strategic intel UI / diplomacy layer would consume `SightedEnemySnapshot` directly;
- a courier / order-delay layer would extend `OperationDecisionMemory` with per-order arrival timestamps;
- a railroad / supply-line operational layer would extend `ConvergentOperationLedger` with logistics arms.

None of these are authorized by this spec.

## Cadence and Tick Budget

All Slice C work runs on the existing daily strategic-review tick. No new tick, no new Harmony patch.

Cost budget per tick (worst case, full campaign):

- `SightingObserver`: O(units × 2) — one pass for current visibility, one diff against prior snapshot. Bounded by `aifaction[].enemyunits.Count` per alliance.
- `FrontSectorRuntime` extension: O(1) per unit (dictionary lookup added before existing `ReadFloat` call). No structural change to iteration.
- `ForceRatioGate`: O(formations) per alliance. Trivial relative to existing scorer cost.
- `ConcentrationIntent`: O(brigades) per alliance. Same.
- `ConvergentOperationLedger`: O(active convergent operations). Operations are bounded by `CoordinatedOperationPackageLedger`'s existing limits.
- `OperationDecisionMemory` extension: O(active operations).

Total added cost: well under existing daily-review budget (Slice A's existing daily ledgers dominate by an order of magnitude).

## Cross-Cutting Gates

Every Slice C component must apply these gates before producing output or writing state:

- **W&L ownership gate.** Replicate the public predicate `(ai_feudstance == -1) | (isplayeraiorfeud == 2)` (verified in Slice B spec — used at 3490, 3789, 3834, 3890, 4080, 4515, 4842, 4917, 4922, 5137, 5249, 5307, 5360, 5400). Do not call `PerformAIActionDLCWL` (private static at 5101).
- **Alliance bounds gate.** Per-alliance arrays must be bound-checked. `allianceId == 2` (Europe) is a real value `AICampaignReflect.GetAllianceId(...)` can return; AGENTS.md flags this trap.
- **Player-subordinate gate.** Any unit whose feud-stance gate fails is read-only — never write a movement order, role tag, or commitment update.
- **Engagement gate.** Slice C ledgers run only between battles (campaign map active). When a battle is active, Slice B owns the tactical brain; Slice C ledgers may read state but should not emit new operational decisions until the battle resolves.
- **Sighting unknown gate.** When `SightingQuality == None` for a target, every consumer treats the target as nonexistent. Whiskey's plans should not act on intel that the AI alliance cannot legitimately have.

## Verification Expectations

Before implementation planning, re-run anchor checks against the GT decompile and the Whiskey strategic surface:

```bash
# GT vanilla intel anchors
rg -n "public class FogOfWar|public bool unitrevealedbyenemy|public FogOfWar\[\] fows|public FogOfWar fow|public FogOfWar oppositefow|public float groupintelligencegathering|public string lastspotted|public List<SpottedMarks>|GamePrefs.intelligencegatheringbureauofmilitaryinformation|GamePrefs.informationdelaybureauofmilitaryinformation|public.*GetPerkLevel\(" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs

# Whiskey strategic surfaces this spec extends
rg -n "class StrategicCoordinator|RunStrategicReview|class WarStateObserver|class FrontSectorRuntime|public.*AddUnits\(|class ContactEvidenceLedger|class DefenseIntentRuntime|BuildInput|class OperationalProbeRuntime|class FormationDirective|class FormationDirectiveLedger|class AssetRoleScorer|class AssetStrategicRole|class CoordinatedOperationRuntime|AddCommitPlan|class OperationDecisionMemory|class CoordinatedOperationPackageLedger" src/WhiskeyRealism/Strategic/
```

Both grep blocks must resolve cleanly before any implementation plan is drafted.

Pure harness coverage expected when Slice C is implemented:

- `SightingObserver` produces a non-empty snapshot for each AI alliance with at least one visible enemy unit;
- `SightedEnemySnapshot` correctly classifies a stale unit (last sighted > BMI staleness) as `Stale`;
- `FrontSectorRuntime` excludes a unit with `SightingQuality == None` from sector strength;
- `ForceRatioGate` returns `Withdraw` for `ratio < 0.5`;
- `ForceRatioGate` returns `Commit` for an attacker at `ratio == 0.85` with same-rank;
- `ConcentrationIntent` fires when `NewlySeen` contains an enemy in the directive's theater;
- `ConvergentOperationLedger` aborts when one arm's sighted enemy strength exceeds 1.5× planned;
- `OperationDecisionMemory` aborts an operation after stall ≥ AbortThreshold.StallDays AND loss ≥ AbortThreshold.LossPct;
- All scorers respect the W&L gate and alliance bounds;
- Player-subordinate units are never written to.

Runtime smoke expectations (post-build): default-off configuration for `ConvergentOperationLedger`; bounded telemetry; no repeated exceptions; no player-subordinate retasking; deployed DLL hash matches `dist/WhiskeyRealism.dll`.

## Non-Goals

This spec does not:

- patch any new vanilla method (uses only the existing `MonthlyTickHookPatch`);
- introduce a new top-level ledger except `ConvergentOperationLedger`;
- model courier travel times, supply-line logistics, weather, or railroads;
- modify Slice A's daily cadence or strategic profiles;
- modify Slice B's tactical scorers, charge gates, or movement-write surfaces;
- affect player-subordinate units beyond surfacing recommendations;
- expose a UI for sighting state or commitment ledger (telemetry only via `BepInEx/LogOutput.log`);
- alter the JSON sidecar schema beyond adding `OperationDecisionMemory` commitment fields and `ConvergentOperation` records;
- change vanilla unit visibility or fog-of-war behavior in any direction (Whiskey reads, never writes);
- introduce a new top-level Harmony patch surface.

## Not Verified

- `FogOfWar` per-alliance lookup pattern: `Regiment.fows[]` is an array but the index semantics (per-alliance? per-side?) require empirical confirmation. Implementation plan must verify before relying on the structure.
- `Regiment.unitrevealedbyenemy` is a single bool, not per-alliance. The actual sighting-per-alliance state may live in `fows[]` rather than this scalar. Treated as suggestive evidence; verify in implementation.
- `lastspotted` is declared as `string` (142604), not a timestamp. The format is unverified; may be a unit name, a serialized log entry, or a free-text field. Implementation must inspect at runtime; if unparsable, fall back to the `spottedmarks` list at 142606 or to the diff between consecutive `unitrevealedbyenemy` snapshots.
- `groupintelligencegathering` (110886) modulator behavior is partially documented at 120604/120608 (multiplied by BMI perk array) but the absolute scale is unknown. Treat as a relative weighting input; do not assume specific numeric ranges.
- BMI perk index `3` is consistent across `GetPerkLevel(3)` and `GetPerkLevelParent(3)` calls but the perk's name in the UI is not confirmed. Implementation should not display the name; surface BMI-derived effects as "intel quality" in telemetry.
- `Regiment.GetInstanceID()` stability across save/load is not verified for this slice (Slice B spec already flags this). The snapshot ledger uses InstanceID + name fallback as a defensive pair.
- The Scourge → Whiskey rank mapping (`base.Rank()` ↔ `FormationLevel`) is approximate. Scourge has six ranks (`eRankSide / eRankArmy / eRankCorp / eRankDiv / eRankBrig / eRankReg`); Whiskey's `FormationLevel` may not have the exact same depth. Implementation must pin which Whiskey level pairs with which Scourge rank for the rank-difference math in `ForceRatioGate`.
- The convergent operation phase model is novel and has no direct vanilla or Scourge anchor. The thresholds (`LoDArrivalRadius`, `LoDDwellHours`, `MaxApproachLossPct`) are starting estimates and require in-game tuning.
- Aborted operations may leave formations in transitional states (en route between LoDs) when the abort fires. The recovery path (issue `RetreatTo` orders, clear directives, re-attach to parent formation) is the implementation plan's responsibility; this spec specifies the abort decision, not the cleanup choreography.

## Open Questions for the Implementation Plan

These are deliberately deferred from this spec; the implementation plan must answer each before writing code:

1. Does `Regiment.fows[]` index by alliance or by some other discriminator? Confirm via a one-shot in-game smoke that prints `fows.Length` and `fows[i] != null` per alliance.
2. What is the format of `Regiment.lastspotted`? Print at runtime to characterize.
3. Which Whiskey `FormationLevel` value pairs with which Scourge `ERank`? The implementation plan provides the table.
4. What is the actual time-budget impact of `SightingObserver` on a campaign with 200+ regiments per alliance? Measured during smoke; may require chunking the sighting scan if it exceeds 50ms per tick.
5. Do `ConvergentOperationLedger` aborts compose cleanly with `OperationDecisionMemory` aborts when both fire on the same tick? If so, which takes precedence and how does the cleanup choreography deduplicate?
