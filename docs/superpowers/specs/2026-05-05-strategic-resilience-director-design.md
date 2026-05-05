# Strategic Resilience Director Design

Status: design approved; implementation plan not written.
Date: 2026-05-05
Scope: strategic layer only.

## Goal

Add a cached, low-frequency Strategic Resilience Director that makes the strategic system behave like a war-length pressure model instead of isolated local ledgers. The target is a balanced campaign: historical pressure and theater logic, with explicit safety rails against unfun early collapse.

The war can still end early, but early collapse should require evidence such as major field-army defeats, broken capital corridors, logistics losses, morale collapse, or inability to recover armies. Without that evidence, the system should dampen runaway snowballs while preserving Union pressure and CSA protraction flavor.

## Current Problem

The current strategic layer has strong components but does not yet prove whole-campaign composition:

- Operational plan phases define `TargetTaken`, `TargetEngaged`, `DeadlineExpired`, and `ForceBelowThreshold`, but shipped review logic mostly advances on deadlines.
- Operational probes can escalate from a high strength ratio without requiring positive contact evidence.
- Probe runtime uses vanilla `MoveUnitTo` and `unitsinoffensiveoperations`, but does not call or mirror vanilla `IsUnitAvailableForOffensiveOperations`.
- Formation pressure is recomputed by incrementing existing counters, which can contaminate construction pressure over time.
- Runtime acceptance is still local: console tests, build/deploy/hash, and short smoke markers. It does not prove battle cadence, theater pivots, or campaign survival pacing through 1865.

## Design Choice

Use a Strategic Resilience Director, not a full override AI.

The Director owns campaign-level interpretation and cached posture. Existing systems remain executors:

- CIC still chooses plans.
- Front, formation, defense, fiscal, construction, and probe ledgers remain the local truth.
- Vanilla still owns hard movement and campaign operation gates.
- Director posture adjusts weights, thresholds, and stale-plan invalidation only.

Load-bearing rule:

```text
Vanilla hard gates
+ Whiskey ledgers
+ era / chapter / season / personality
+ Director posture modifier
= bounded action
```

The Director must never mean:

```text
Director says so -> force action
```

## Director Output

The Director publishes one compact posture per alliance.

```csharp
public enum CampaignPace
{
    Stable,
    TooQuiet,
    Overheated,
    TooFastCollapse,
    Stalemated,
    LateWarPressure
}

public enum StrategicIntent
{
    Probe,
    Concentrate,
    Preserve,
    Delay,
    Exploit,
    Recover
}

public enum CollapseRisk
{
    Low,
    Elevated,
    Critical
}

public enum BattleCadence
{
    Quiet,
    Active,
    Overheated
}
```

The output should include:

- `CampaignPace`
- `StrategicIntent`
- `TheaterPriority`
- `CollapseRisk`
- `BattleCadence`
- bounded threshold modifiers
- source signature
- `stale` marker when a slice deferred because of budget

Persist only compact Director memory:

- last published posture per alliance
- rolling 14-day counters
- recent event summaries
- source signatures
- last full refresh day

Do not persist raw unit lists or large snapshots.

## Inputs

The Director consumes existing summaries first:

- `FrontSectorLedger`
- `FormationDirectiveLedger`
- `DefenseIntentLedger`
- `FiscalIntentLedger`
- `ConstructionIntentLedger`
- `BattleHistory`
- `Policy.CurrentChapter`
- `EraStage`
- active CIC plan and objective
- operational probe output
- campaign map signature

New ledgers are allowed when they clarify ownership or avoid repeated raw scans.

## New Ledgers

### PhaseTruthLedger

Owns whether the active plan phase is still valid.

Inputs:

- active plan and current phase
- `CampaignObjective.GetAvailableObjectives`
- objective accomplished state
- target position resolution
- battle history near target
- front/formation force threshold
- phase deadline

Outputs:

- `Valid`
- `TargetAccomplished`
- `ObjectiveUnavailable`
- `TargetEngaged`
- `ForceBelowThreshold`
- `DeadlineExpired`
- `MissingTargetPosition`
- recommended action: continue, advance, recover, fallback, replan

Rules:

- If target is accomplished, advance phase or replan.
- If objective unavailable or position cannot resolve, replan.
- If target was recently engaged but not taken, mark engaged and let contact/pacing decide continue vs escalate.
- If assigned force fell below threshold, prefer recover/delay before blind pressure.
- If deadline expired, advance, fallback, or replan.
- `PickCampaignObjectivePatch` must not keep forcing a stale phase target.

### ContactEvidenceLedger

Owns whether an operational probe has real contact.

Inputs:

- previous probe state
- current target-sector enemy and friendly strength
- enemy-strength delta since probe start
- nearby battle/skirmish evidence if available
- recent battle history in target sector
- probe age and location/target area

Outputs:

- `NoContact`
- `EnemyPresent`
- `EnemyReacted`
- `SkirmishObserved`
- `BattleObserved`
- `FavorableContact`
- `OvermatchedContact`

Rules:

- Probe escalation requires positive contact evidence.
- Zero enemy/contact evidence cannot become mass commitment.
- No-contact can continue probing, recover, or redirect.
- Contact with bad odds pauses or withdraws.
- Contact with favorable odds can request escalation, subject to Director posture and vanilla offensive availability.

### CampaignPaceLedger

Owns full-war pacing.

Inputs:

- rolling 14-day battle/probe/contact history
- quarterly battle cadence
- major/minor battle mix
- capital danger streaks
- objective churn
- morale collapse risk
- field army destruction/recovery signals
- theater pressure balance
- era/chapter/season

Outputs:

- campaign pace classification
- collapse risk
- battle cadence
- theater priority pressure
- “too quiet” and “overheated” reasons

Rules:

- `TooQuiet`: no meaningful battles/probes/contact for too long and no active theater pressure.
- `Overheated`: too many major fights or casualties too quickly.
- `TooFastCollapse`: capital/army/morale collapse before enough enabling evidence.
- `LateWarPressure`: 1864+ Union pressure should be sustained unless Union is exhausted.
- `Stable`: operations and friction are within expected historical/gameplay bounds.

## Cadence

Monthly is too coarse for campaign feel at 20x/50x. Use a rolling weekly/fortnightly cadence.

```text
Daily: cheap signature check + event intake
7-day rolling cycle: one subsystem slice per day
14-day full posture window: complete campaign posture refresh
Event-triggered: immediate narrow refresh for battles/objectives/capital danger/chapter change
```

Daily work:

- compare source signatures
- ingest new battle/probe/objective/defense events
- mark dirty components
- publish previous posture if nothing material changed

7-day rolling cycle:

1. Phase truth and objective validity
2. Contact evidence and probe outcomes
3. Battle cadence and casualty tempo
4. Theater pressure balance
5. Collapse risk and capital danger
6. Fiscal plus construction pressure synthesis
7. Final posture publish and threshold modifiers

14-day window:

- “too quiet” means no meaningful contact across this window.
- “overheated” means too many major fights or casualties inside this window.
- collapse risk uses streaks across the window, not one-day noise.
- probe outcomes decay over the window.

Event-triggered narrow refresh:

- battle result recorded
- objective accomplished/unavailable
- capital threatened
- major army destroyed or retreating
- chapter/era change
- morale crash
- probe escalates or withdraws

The event refresh updates only affected fields. Full recompute stays on the rolling cycle unless required.

## Existing Strategic Layer Composition

### Era And Chapter

`EraStage` remains Whiskey’s broad war-development model. `Policy.CurrentChapter` remains vanilla’s campaign chapter model.

Director composition:

- early 1861 / chapter 1: more caution, slower escalation, smaller probes
- 1862-63 / chapter 2: normal operational tempo
- 1864+ / chapter 3: Union sustained pressure, CSA preservation/protraction

If chapter and era disagree, use the more conservative pressure setting unless event evidence justifies escalation.

### Personality

CIC personality still drives flavor.

- Audacious commanders tolerate faster probe escalation.
- Cautious commanders extend recovery/delay and require stronger contact evidence.
- Politically responsive commanders protect capitals and morale-sensitive theaters.
- Casualty-tolerant commanders accept more sustained pressure.
- Aggressive commanders push `TooQuiet` states harder.

Director modifiers should be small clamps around personality, not replacements.

### Season

Winter remains a strategic brake:

- longer probe minimums
- smaller probe packages
- higher escalation evidence requirement
- more recover/delay posture
- construction/fiscal preparation for spring pressure

Winter should not freeze the campaign completely. In `TooQuiet`, safe probes or logistics preparation can still happen.

### Grand Strategy Profiles

Existing Union/CSA grand-strategy profiles remain the baseline.

Director adds a context multiplier:

- Union Anaconda, river, rail, and late total-war pressure stay profile-driven.
- CSA cordon, foreign-recognition, and protraction stay profile-driven.
- Director decides whether to press, delay, preserve, recover, or shift priority based on campaign state.

### Hard Safety Blocks

Director cannot override hard safety blocks from:

- vanilla movement availability
- front hold budgets
- formation direct-movement eligibility
- defense forbidden reasons
- player-CIC standdown
- W&L startup deferral

It can only tighten or loosen thresholds inside bounded ranges.

## Integration Points

### CIC And Plan Selection

Add phase truth before `CIC.ReviewPlan()` returns true.

- stale/accomplished/unavailable objective -> mark dirty and replan
- engaged phase -> continue, probe again, or escalate based on contact evidence
- force below threshold -> recover/delay instead of blind objective pressure
- deadline expired -> advance, fallback, or replan
- Director can bias `ObjectiveScoring`, but CIC still picks plans

### Operational Probes

Replace ratio-only escalation with contact-backed rules.

- no contact -> continue probe, recover, or redirect
- contact but bad odds -> pause/withdraw
- contact and favorable odds -> allow escalation
- escalation permission comes from Director posture
- runtime must call or faithfully mirror vanilla `IsUnitAvailableForOffensiveOperations` before `MoveUnitTo`

Escalation should mean permission to form a bounded multi-formation mass package where safe, not just relabeling the original probe unit.

### Formation / Front / Transfer

Director posture adjusts thresholds only.

- `TooFastCollapse`: raise CSA hold thresholds and reduce donor/export willingness
- `TooQuiet`: slightly lower probe friction in safe theaters
- `LateWarPressure`: allow Union broader concentration if source sectors stay safe
- `Overheated`: prefer recover/delay and reduce repeated massing

### Fiscal / Construction

Director should not pick buildings directly.

- `CollapseRisk.Critical`: protect supply and recovery construction
- `TooQuiet` with healthy fiscal state: favor logistics, rail, and industry that enable future operations
- `Overheated`: favor recovery/supply and reduce expansion bias

### Defense

Director adds context to defense, not a replacement for `DefenseIntentLedger`.

- capital danger streaks become visible
- early-collapse dampers block excessive capital-chain collapse
- late-war Union pressure can still punch through if battle/logistics evidence supports it

## Performance Contract

The Director must compose with existing performance work:

- #21 `FastForwardAiCatchUpPatch`
- #26 `CampaignAiUpdateGovernorPatch`
- `StrategicCadencePolicy`
- optimized daily coordinator cadence
- `DailyOps:Perf` diagnostics
- cached reflection in strategic patches
- front/formation signatures to suppress jitter

Rules:

1. No raw full-map scan in the hot path. Consume existing ledger summaries and signatures.
2. No same-day full recompute unless a major event requires it.
3. Each Director slice has a configurable budget. If budget is exceeded, defer and publish previous posture with `stale=true`.
4. Alternate alliance-heavy work so Union and CSA do not run the heaviest slice on the same frame.
5. Reuse existing source signatures for front, formation, defense, fiscal, construction, campaign map, operational probe, battle history, chapter, era, and season.
6. Default telemetry is one compact line on posture change. Detailed Director traces require config opt-in.
7. At 20x/50x, prefer deferred rolling evaluation over blocking the frame.
8. #26 remains the owner of high-speed vanilla AI scheduling. Director must not re-enable #21 extra catch-up while #26 is active.
9. Persist compact Director memory so save/load does not force immediate full recompute.

Performance acceptance:

- Director adds no repeated daily spike above the configured `DailyOps:Perf` threshold.
- At 20x/50x, logs show rolling slices spread across 7/14 days.
- No repeated reflection warnings.
- No posture-check log spam.
- Existing #26 governor remains active and authoritative.

## Required Fixes In Same Slice

These are not optional because they contaminate the Director’s inputs.

1. Fix `FormationDirectiveLedger.RecomputePressure()` so it resets `Pressure` before counting.
2. Add phase truth so stale objectives stop driving current plans.
3. Require contact evidence before probe escalation.
4. Add a vanilla offensive availability wrapper for probe movement.
5. Add campaign-level telemetry needed to validate battle cadence and survival pacing.

## Testing

Pure tests:

- phase advances when target is accomplished
- phase replans when objective unavailable
- phase pauses/recovers when force below threshold
- probe cannot escalate with zero enemy/contact evidence
- probe can escalate with valid contact and favorable ratio
- probe runtime respects vanilla-offensive-gate wrapper result
- repeated `ApplyOperationalProbe()` does not inflate pressure counters
- Director classifies `TooQuiet`, `Stable`, `Overheated`, `TooFastCollapse`, `LateWarPressure`
- chapter/era/personality/winter modifiers compose without overriding hard safety gates
- 7-day rolling cycle runs one bounded slice per day
- 14-day window decays old events

Runtime smoke:

- restart game with deployed DLL
- confirm `[StrategicDirector]` first-fire
- confirm one rolling slice per day, not all slices every day
- confirm no `[DailyOps:Perf]` spike from Director
- confirm phase truth lines for current active objectives
- confirm probes do not escalate without contact
- confirm no warnings/errors/Harmony failures

Long-run smoke at 20x/50x:

- date/chapter/era/season
- posture per alliance
- objective id and phase state
- probe decision and contact evidence
- battle count over last 14 days and quarter
- major vs minor battle mix
- capital danger streak
- field-army recovery state
- CSA collapse risk
- Union pressure state
- slow-frame timings

Balanced historical resilience acceptance:

- 1861: lower escalation, fewer mass commitments, but some probing/contact
- 1862-63: active operations and theater pivots
- 1864+: stronger Union sustained pressure
- CSA collapse damped unless field army/logistics/morale evidence supports it
- minor battles/probes fill quiet periods
- no cross-map zerg regressions
- no repeated stale-objective pressure
- no Director-driven performance spikes

## Not Verified

- Runtime battle cadence over a full campaign.
- Whether current battle-history coordinates are sufficient for target-sector contact evidence.
- Exact cost of Director slices under 20x/50x; this must be measured with `DailyOps:Perf`.
- Whether vanilla objective accomplishment and availability checks cover every W&L abstract objective shape without additional adapters.

## Defer

- Tactical battle AI changes.
- W&L hierarchy AI.
- Direct unit command from the Director.
- Raw game-state cache snapshots persisted to sidecar.
- Full campaign scripting to force historical battle names or outcomes.
