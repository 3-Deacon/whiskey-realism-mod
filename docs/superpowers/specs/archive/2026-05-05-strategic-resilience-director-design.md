# Strategic Resilience Director Design

Status: design approved (revised 2026-05-05 after adversarial review of native systems and current code); implementation plan not written.
Date: 2026-05-05
Scope: strategic layer only. Required fixes and Director ship as one slice with explicit in-slice ordering — see "Required Fixes In Same Slice" and the Operational Probes integration invariant.

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
    Stable,          // rolling-window battle/contact within historical band
    TooQuiet,        // no meaningful battles/probes/contact across the 14-day window
    Overheated,      // too many major fights or casualties inside the 14-day window
    TooFastCollapse, // capital/army/morale collapse before enabling evidence
    Stalemated,      // sustained engagement without front movement
    LateWarPressure  // 1864+ Union pressure floor, chapter-anchored (see "Era And Chapter")
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
    Low,        // alliance.nationalmorale > breakmoraletrigger × 2.0
    Elevated,   // alliance.nationalmorale ≤ breakmoraletrigger × 1.5
    Critical    // alliance.nationalmorale ≤ breakmoraletrigger × 1.15
                // OR vanilla surrender thresholds approached
                //    (minnationalmoralesurrender, minavgarmymoralesurrender)
}
```

`CampaignPace` carries the battle-cadence dimension directly — `Stable` means active-but-historical, `TooQuiet` and `Overheated` are the explicit cadence outliers. There is no separate `BattleCadence` enum; the rolling-window counts feed `CampaignPace`.

The output should include:

- `CampaignPace`
- `StrategicIntent`
- `TheaterPriority`
- `CollapseRisk`
- bounded threshold modifiers (capped per "Personality" clamp)
- source signature
- `stale` marker when a slice deferred because of budget OR the one-publish-per-real-second clamp fired

Persist only compact Director memory:

- last published posture per alliance
- rolling 14-day counters
- recent event summaries
- source signatures
- last full refresh day

Do not persist raw unit lists or large snapshots.

## Inputs

The Director consumes existing summaries first:

- `FrontSectorLedger` — per-sector posture and `StrengthRatio`; also supplies theater pressure balance via per-theater aggregation (see "Required Fixes In Same Slice" item 6 if the helper does not yet exist)
- `FormationDirectiveLedger` — per-formation directives and pressure summary
- `DefenseIntentLedger` — per-asset defense response, capital corridor signals
- `FiscalIntentLedger` — treasury/credit/supply pressure
- `ConstructionIntentLedger` — private-building/telegraph/recovery posture
- `BattleHistory` — ring buffer; spatial position via `PositionX/PositionZ`, theater via `Theater`, severity via `IsMajorResult`
- `Policy.CurrentChapter` — vanilla chapter; **authoritative for late-war pressure floor**
- `EraStage` — Whiskey era; composes with chapter, never overrides
- `OperationalProbeOutput` per alliance
- `CampaignMapLedger` — active-map signature
- active CIC plan and objective
- vanilla collapse scalars: `GameVars.alliance[allianceId].nationalmorale`, `GamePrefs.breakmoraletrigger`, `GamePrefs.minnationalmoralesurrender`, `GamePrefs.minavgarmymoralesurrender`

If `FrontSectorLedger` does not already expose per-theater pressure aggregation, the Director adds a thin `TheaterPressureView` over the existing sector list — no new full-map scan; aggregation runs once per `Front` ledger refresh and is cached. New ledgers are allowed only when they clarify ownership or avoid repeated raw scans, never to re-implement a vanilla scan that exists.

## New Ledgers

### PhaseTruthLedger

Owns whether the active plan phase is still valid.

Inputs:

- active plan and current phase
- `CampaignObjective.GetAvailableObjectives` (filtered list — same call `CIC.Replan` already uses)
- `CampaignObjective.accomplished` state for the current phase target
- target position resolution via `ObjectiveAdapter.ResolveObjectivePosition(targetObjectiveId)`
- battle history near target: any `BattleHistoryRecord` whose `(PositionX, PositionZ)` is within `GamePrefs.aimaximumdistancetosearchforunitrelocations` of the resolved objective position in the last 14 game days
- front/formation force threshold from `FrontSectorLedger.GetSector(targetSectorKey).OwnStrength` against `Phase.ForceFractionRequired`
- phase deadline (`Phase.DeadlineMonth/Year`)

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

Owns whether an operational probe has real contact. Closes the "ratio-only escalation" hole in `OperationalProbeLedger.EvaluateExistingProbe` where a probe targeting an empty sector escalates after `MinimumProbeDays` because `friendly / max(1, 0)` always passes the escalation ratio.

Inputs (each grounded in an existing summary or vanilla scalar):

- previous `OperationalProbeState` (`StartedDaySerial`, `LastObservedEnemyStrength`, `LastObservedFriendlyStrength`, `TargetAreaKey`, `SourceSectorKey`)
- current target-sector strength: `FrontSectorLedger.GetSector(targetSectorKey).EnemyStrength` and `.OwnStrength`
- enemy-strength delta since probe start: `current.EnemyStrength - previous.LastObservedEnemyStrength`
- nearby battle evidence: any `BattleHistoryRecord` whose `(PositionX, PositionZ)` is within `GamePrefs.aimaximumdistancetosearchforunitrelocations` of the resolved objective position AND whose `Year/Month/Day` falls inside the last 7 game days
- probe age (`daySerial - previous.StartedDaySerial`)

Outputs:

- `NoContact` — `current.EnemyStrength ≤ 0` AND no nearby battle in last 7 days
- `EnemyPresent` — `current.EnemyStrength > 0` AND delta within ±25% of probe start
- `EnemyReacted` — `current.EnemyStrength ≥ previous.LastObservedEnemyStrength × OperationalProbeOptions.EnemyReactionMultiplier` (already exists in tempo doctrine)
- `SkirmishObserved` — nearby `BattleHistoryRecord` with `IsMajorResult == false` in last 7 days
- `BattleObserved` — nearby `BattleHistoryRecord` with `IsMajorResult == true` in last 7 days
- `FavorableContact` — (`EnemyPresent` OR `SkirmishObserved`) AND friendly/enemy ratio ≥ `EscalateFriendlyRatio`
- `OvermatchedContact` — friendly/enemy ratio ≤ `WithdrawFriendlyRatio` OR `BattleObserved` lost by us

Rules:

- Probe escalation requires `FavorableContact`. `NoContact` cannot become mass commitment regardless of strength ratio.
- `NoContact` permits continued probing, redirect, or recover; never `Escalate`.
- `EnemyReacted` produces `Pause`.
- `OvermatchedContact` produces `Withdraw`.
- `FavorableContact` permits `Escalate`, still subject to vanilla `IsUnitAvailableForOffensiveOperations` (see "Operational Probes" integration).

The `ContactEvidenceLedger` is the dependency that lets `OperationalProbeLedger.EvaluateExistingProbe` stop relying solely on `friendly / max(1, enemy)` and instead treat zero-enemy as `NoContact` → continue or recover, never escalate.

### CampaignPaceLedger

Owns full-war pacing. Publishes `CampaignPace`, `CollapseRisk`, theater priority pressure, and qualitative reason strings for telemetry.

Inputs:

- rolling 14-day battle/probe/contact history (filter `BattleHistory` by date)
- quarterly battle cadence (90-day battle count from same source)
- major/minor battle mix (`BattleHistoryRecord.IsMajorResult`)
- capital danger streaks (consecutive days `DefenseIntentLedger` carries `ActiveInvasion` against any `AssetStrategicRole.Capital`)
- objective churn (count of plan replans in last 30 days, tracked on `StrategicCoordinator`)
- vanilla morale: `GameVars.alliance[allianceId].nationalmorale` against `GamePrefs.breakmoraletrigger`, `GamePrefs.minnationalmoralesurrender`, `GamePrefs.minavgarmymoralesurrender`
- field army state: top formations seen in `FormationDirectiveLedger.Assignments` then absent ≥ 14 days (destroyed) vs `FormationDirective.Recover` cleared (recovered)
- theater pressure balance: per-theater aggregation over `FrontSectorLedger.SectorsByTheater` (or the new `TheaterPressureView` helper if not already present)
- `EraStage`, `Policy.CurrentChapter`, campaign month

Outputs:

- `CampaignPace` classification with reason string
- `CollapseRisk` (vanilla-bound thresholds — see "Director Output")
- theater priority pressure (per-theater normalized score)
- one-line `[CampaignPace]` telemetry on classification change

Rules (in evaluation order — first match wins):

1. `TooFastCollapse`: `nationalmorale ≤ breakmoraletrigger × 1.15` AND `year ≤ 1863`. Vanilla will end the war shortly via `CampaignObjective.CheckIfAccomplished` (decompile 179015–179055); raise the dampers regardless of battle history.
2. `LateWarPressure`: `Policy.CurrentChapter ≥ 3` OR (`year ≥ 1864` AND alliance == Union AND CSA `nationalmorale > breakmoraletrigger × 1.5`). **Authoritative from vanilla chapter; rolling-window cadence cannot suppress it.**
3. `Overheated`: ≥ 4 major battles OR ≥ 2 catastrophic-casualty battles in the 14-day window.
4. `TooQuiet`: zero battles AND zero probe contact across the full 14-day window AND no active capital danger streak. **Suppressed in chapter 1 winter (months 12, 1, 2)** — both armies refitting is the historically correct state.
5. `Stalemated`: `Policy.CurrentChapter == 2` AND ≥ 60 days since front sector signature changed AND ≥ 2 quarters of similar battle counts on both sides.
6. `Stable`: default.

`CollapseRisk` is computed independently from `CampaignPace` (both publish; rule 1 above requires `CollapseRisk == Critical` to fire `TooFastCollapse`):

- `Critical`: `nationalmorale ≤ breakmoraletrigger × 1.15` OR `nationalmorale ≤ minnationalmoralesurrender × 1.10`
- `Elevated`: `nationalmorale ≤ breakmoraletrigger × 1.5`
- `Low`: otherwise

**1864 collapse floor:** if `year ≥ 1864` AND alliance == CSA AND `CollapseRisk ≥ Elevated`, the Director's `StrategicIntent` for CSA cannot be `Preserve`. Vanilla will end the war within months either way; let CSA fight to the end instead of strategic-stalling.

## Cadence

Monthly is too coarse for campaign feel at 20x/50x. Use a rolling weekly/fortnightly cadence keyed on **advanced game day**, not real-time and not vanilla AI frame.

```text
Per advanced game day: cheap signature check + event intake + ONE rolling slice
7-day rolling cycle: one subsystem slice per advanced game day
14-day full posture window: complete campaign posture refresh
Event-triggered: immediate narrow refresh for battles/objectives/capital danger/chapter change
```

Trigger contract:

- Slices fire only when `StrategicCoordinator.NotifyDateAdvanced` reports a new game day (the same source that drives `DailyOps`). If multiple game days advance in one rendered frame at 50×, **one slice fires for the first advance and remaining advances coalesce** — the rolling cycle indexes on `daySerial % 7`, so slot K runs once per pass through the cycle.
- Director publish is hard-clamped to **at most one full posture publish per real second** across all alliances combined, at any game speed. Subsequent publish attempts within the same real second mark posture `stale=true` and reuse the previous payload. This composes with #26 — the Director never re-enters a frame #26 has capped.
- Event-triggered narrow refresh runs on the same advanced-game-day pulse and obeys the same one-publish-per-real-second clamp.

Daily work:

- compare source signatures (front, formation, defense, fiscal, construction, campaign map, operational probe, battle history, chapter, era, season)
- ingest new battle/probe/objective/defense events
- mark dirty components
- publish previous posture if nothing material changed

7-day rolling cycle (slot index = `daySerial % 7`):

1. Phase truth and objective validity
2. Contact evidence and probe outcomes
3. Battle cadence and casualty tempo
4. Theater pressure balance
5. Collapse risk and capital danger
6. Fiscal plus construction pressure synthesis
7. Final posture publish and threshold modifiers

14-day window:

- "too quiet" means no meaningful contact across this window.
- "overheated" means too many major fights or casualties inside this window.
- collapse risk uses streaks across the window, not one-day noise.
- probe outcomes decay over the window.

Event-triggered narrow refresh:

- battle result recorded (#5 `BattleResultObserverPatch`)
- objective accomplished/unavailable (`PhaseTruthLedger`)
- capital threatened (`DefenseIntentLedger` reports `ActiveInvasion` against `AssetStrategicRole.Capital`)
- major army destroyed or retreating (formation absent ≥ 14 days OR `FormationDirective.Recover` ≥ 14 days)
- chapter/era change (`Policy.CheckForChapterUpdate`, `EraStageManager.CheckTransition`)
- morale crash (`nationalmorale` crossed `breakmoraletrigger × 1.5` downward)
- probe escalates or withdraws

The event refresh updates only affected fields. Full recompute stays on the rolling cycle unless required.

## Existing Strategic Layer Composition

### Era And Chapter

`EraStage` remains Whiskey's broad war-development model. `Policy.CurrentChapter` remains vanilla's campaign chapter model. **Vanilla chapter is authoritative for late-war pressure floors** — the Director's rolling-window cadence cannot suppress `LateWarPressure` once `Policy.CurrentChapter ≥ 3`.

Director composition:

- early 1861 / chapter 1: more caution, slower escalation, smaller probes
- 1862-63 / chapter 2: normal operational tempo
- 1864+ / chapter 3: Union sustained pressure, CSA preservation/protraction (subject to the 1864 collapse floor in `CampaignPaceLedger`)

Primacy rules:

- `Policy.CurrentChapter ≥ 3` forces `CampaignPace.LateWarPressure` for Union (rule 2 of `CampaignPaceLedger`); rolling-window quietness cannot demote it.
- If chapter and era disagree (e.g., chapter 1 but `EraStage.Decisive1863`), prefer the more conservative pressure setting unless explicit event evidence justifies escalation.
- `OperationalTempoDoctrine.For(...)` already composes chapter + era + faction + personality + season. The Director adjusts thresholds *on top* of that composition; it does not re-derive era or chapter pacing.

### Personality

CIC personality still drives flavor.

- Audacious commanders tolerate faster probe escalation.
- Cautious commanders extend recovery/delay and require stronger contact evidence.
- Politically responsive commanders protect capitals and morale-sensitive theaters.
- Casualty-tolerant commanders accept more sustained pressure.
- Aggressive commanders push `TooQuiet` states harder.

**Personality-preservation clamp (load-bearing):** for any threshold that `OperationalTempoDoctrine.ApplyPersonality` already adjusts (`MaximumProbeStrengthFraction`, `EscalateFriendlyRatio`, `MinimumProbeDays`, `EnemyReactionMultiplier`, `WithdrawFriendlyRatio`), the Director's per-posture modifier on the same field is capped at **±50% of the absolute personality delta on that field**. Concretely, if personality contributed `+0.05` to `MaximumProbeStrengthFraction`, the Director can add at most `±0.025`. This guarantees an audacious-Lee never collapses into a cautious-McClellan because of `CampaignPace.Overheated`, and the personality space stays the dominant signal across all six `CampaignPace` values.

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

- vanilla movement availability — see the `IsUnitAvailableForOffensiveOperations` wrapper in "Required Fixes In Same Slice"; until that wrapper lands, the probe runtime bypasses several of these gates, so Director posture **must not** be allowed to start permitting probes any earlier than today's runtime does
- front hold budgets (`FrontSectorLedger` / `StrategicMovementBudget`)
- formation direct-movement eligibility (`FormationDirectiveLedger`)
- defense forbidden reasons (`DefenseIntentLedger`, #25 filter)
- player-CIC standdown (`StrategicCoordinator.IsPlayerCICOf`)
- W&L startup deferral (`OperationalStartupGate`, `WlCareerStartGate`)

It can only tighten or loosen thresholds inside bounded ranges (per "Personality" clamp).

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

- no contact -> continue probe, recover, or redirect (never `Escalate`)
- contact but bad odds -> pause/withdraw
- contact and favorable odds -> allow escalation
- escalation permission comes from Director posture, gated by `ContactEvidenceLedger`
- runtime must call or faithfully mirror vanilla `IsUnitAvailableForOffensiveOperations` before `MoveUnitTo` — the wrapper is shipped in this slice (see "Required Fixes In Same Slice")

Escalation should mean permission to form a bounded multi-formation mass package where safe, not just relabeling the original probe unit.

**In-slice ordering invariant:** the Director's posture-driven probe-permission changes must not load before the `IsUnitAvailableForOffensiveOperations` wrapper, the `RecomputePressure` reset, the `PhaseTruthLedger`, and the `ContactEvidenceLedger` are all in place. Even though everything ships as one slice, build/test order in the implementation plan must wire the four required fixes first, smoke-test the existing probe surface against them, then layer the Director on top. The implementation plan must call this out as an explicit task ordering, not a code-level dependency.

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
7. At 20x/50x, prefer deferred rolling evaluation over blocking the frame. **Hard cap: at most one full Director posture publish per real second** across all alliances combined. Subsequent publish attempts within the same real second reuse the previous payload with `stale=true`.
8. #26 remains the owner of high-speed vanilla AI scheduling. Director must not re-enable #21 extra catch-up while #26 is active.
9. Persist compact Director memory so save/load does not force immediate full recompute.
10. Slice trigger is "advanced game day" (`StrategicCoordinator.NotifyDateAdvanced`), never raw rendered frame. Coalesced multi-day advances at 50× run one slice for the first advance; remaining advances are absorbed.

Performance acceptance:

- Director adds no repeated daily spike above the configured `DailyOps:Perf` threshold.
- At 20x/50x, logs show rolling slices spread across 7/14 days.
- No repeated reflection warnings.
- No posture-check log spam.
- Existing #26 governor remains active and authoritative.

## Required Fixes In Same Slice

These are not optional because they contaminate the Director's inputs. Implementation order matters even though everything ships as one slice (see "Operational Probes" in-slice ordering invariant).

1. Fix `FormationDirectiveLedger.RecomputePressure()` so it resets `Pressure` (`LowSupplyCount`, `LowAmmoCount`, `RecoverCount`, `GuardCount`, `MassCount`, `TopSupplyAreaKey`) to defaults before counting. Today the field is incremented in place across `Build` + repeated `ApplyOperationalProbe` calls and leaks monotonically into Director inputs.

2. Add `PhaseTruthLedger` so stale objectives stop driving current plans. `CIC.ReviewPlan` (`CIC.cs:36-53`) today only checks `IsDirty` and deadline year/month; it never reads `accomplished`, never re-checks `GetAvailableObjectives`, and never inspects force-below-threshold.

3. `ContactEvidenceLedger` must run before any probe escalation decision. `OperationalProbeLedger.EvaluateExistingProbe` (`OperationalProbeLedger.cs:123-175`) currently allows a zero-enemy probe to escalate to mass commitment after `MinimumProbeDays` because `friendly / max(1, 0)` always passes the escalation ratio.

4. Add a vanilla offensive availability wrapper for probe movement. `OperationalProbeRuntime.Run` (`OperationalProbeRuntime.cs:95-98`) today only checks `inbattle / onretreat / garrisonreference + unitsindefensiveoperations`. The wrapper must mirror **all** vanilla gates from `AICampaign.IsUnitAvailableForOffensiveOperations` (decompile line 14080):
    - `CampaignArmyPanel.GetReadinessStep(unit) ≥ 2`
    - `!RaidForce.IsRaidUnit(unit)`
    - `unit.groupstrengthactive > GamePrefs.aiminimumstrengthformovement`
    - `unit.groupmorale > GamePrefs.aiminimummoraleformovement`
    - not in `unitsinoffensiveoperations` or `unitsindefensiveoperations`
    - not in `groupstodefendcapital` (ours via #4)
    - not in `unitsconstructingsupplydepots`
    - `!FortConstructionOrder.UnitAlreadyConstructing(unit)` (ours via #27)
    - `SeaInvasionForce.GetSeaInvasionForceReference(unit) == null`
    - `UnitIsFightingForce(unit)`
    - `IsWithinOperationsTheater(unit, operationposition)` (the vanilla theater box)
    - weather check via `weather.CheckWeatherLine(...)` if `operationposition` provided
    - `!IsUnitTakingTown(unit)`

   Prefer calling vanilla directly through reflection (`AICampaign.IsUnitAvailableForOffensiveOperations`) so the wrapper stays forward-compatible to small renames. Fall back to mirroring only if the reflection lookup fails.

5. Add campaign-level telemetry needed to validate battle cadence and survival pacing — at minimum a `[CampaignPace]` line on classification change, a `[CollapseRisk]` line on level change, and the existing `[OperationalProbe]` and `[BattleHistory]` lines unchanged.

6. Add per-theater pressure aggregation over `FrontSectorLedger`. If the existing ledger does not already group sectors by theater, add a `TheaterPressureView` helper that runs once per `Front` ledger refresh and is cached; the Director never scans sectors directly.

## Architectural Cleanup In Same Slice

Refactors surfaced by the adversarial review. They ship in the same slice because the Director's input contract assumes them.

1. **Delete `TheaterCommander`.** 44-line class (`Strategic/TheaterCommander.cs`) with six public methods (`GetZoneRelevance`, `GetForceConsolidationUrgency`, `GetDefensiveOpsThreshold`, `GetPerkPreference`, `GetRecruitmentTheaterWeight`, `GetChargeRestraint`) and **zero callers anywhere in `src/`**. The "two-tier CIC + theater commander" hierarchy from Slice A's original design did not materialize — every per-theater decision is made by ledgers (`FrontSectorLedger`, `ArmyAreaLedger`, `FormationDirectiveLedger`, `DefenseIntentLedger`). Delete:
    - `Strategic/TheaterCommander.cs`
    - `CIC.Theaters` field and the constructor wiring in `StrategicCoordinator.BuildCICForAlliance`
    - `PersistenceDto.TheaterCommanderDto` and the `theaterCommanders` JSON field
    - the save/load loops in `StrategicCoordinator` that walk `cic.Theaters` (lines around 1069 and 1116)

   Newtonsoft tolerates the missing field on load of pre-cleanup saves (unrecognized fields default to ignored on read; missing fields default to empty list). The Director's `TheaterPriority` output supersedes `TheaterCommander.GetZoneRelevance`. **`Theater` (the enum) stays** — it earns its keep as a narrative tag in 87 references across 31 files (`BattleHistoryRecord`, `ObjectiveMetadata`, `ArmyAreaDoctrine`, `FactionProfiles`, telemetry).

2. **Single force-availability source.** `CombatAvailability` (in `FormationDirectiveLedger`), `DefenseForceSizer`, and the ad-hoc `inbattle / onretreat / garrisonreference` checks in `OperationalProbeRuntime.Run` (`OperationalProbeRuntime.cs:95-98`) currently encode subtly different gates. The new wrapper from Required Fix #4 becomes the canonical "can this unit move offensively?" answer. Offensive-eligibility callers route through the wrapper; defensive-eligibility paths (which need different criteria — defenders can be in `groupstodefendcapital`, for example) remain separate but documented as such.

3. **Consolidate operational probe state.** `OperationalProbeOutput.State` and `StrategicCoordinator._operationalProbeStates[alliance]` today hold the same struct in two places (`StrategicCoordinator.cs:54` + `OperationalProbeLedger.cs:25-34`). `ContactEvidenceLedger` would add a third. Make `StrategicCoordinator._operationalProbeStates[alliance]` the single source of truth; `OperationalProbeOutput.State` becomes a transient publish payload only. `ContactEvidenceLedger` reads from the coordinator, never from a probe-output snapshot.

4. **One `BattleHistoryQuery` helper.** Three consumers (`ContactEvidenceLedger`, `PhaseTruthLedger`, `CampaignPaceLedger`) need spatial + date filtering of the 64-entry ring buffer. Write a single helper alongside `BattleHistoryRecord`:

   ```csharp
   internal static class BattleHistoryQuery
   {
       public static IEnumerable<BattleHistoryRecord> Near(
           IReadOnlyList<BattleHistoryRecord> history,
           Vector3 position,
           float maxDistance,
           int currentDaySerial,
           int withinDays);
   }
   ```

   Distance check uses squared distance (no `Math.Sqrt` in the hot path). Date math reuses the `daySerial = year*372 + month*31 + day` convention already in `StrategicCoordinator.UpdateOperationalProbe` (line 725).

## Testing

Pure tests:

- phase advances when target is accomplished
- phase replans when objective unavailable
- phase pauses/recovers when force below threshold
- probe cannot escalate with zero enemy/contact evidence
- probe can escalate with valid contact and favorable ratio
- probe runtime respects vanilla-offensive-gate wrapper result
- probe runtime refuses a unit in `groupstodefendcapital` (#4 capital defender)
- probe runtime refuses a unit in `FortConstructionOrder` (#27 fort builder)
- probe runtime refuses a unit outside `IsWithinOperationsTheater`
- repeated `ApplyOperationalProbe()` does not inflate pressure counters (`RecomputePressure` is idempotent across 100 repeated calls)
- `RecomputePressure` resets all counters and `TopSupplyAreaKey` before counting
- Director classifies `TooQuiet`, `Stable`, `Overheated`, `TooFastCollapse`, `LateWarPressure`
- `CollapseRisk.Critical` triggers at `nationalmorale ≤ breakmoraletrigger × 1.15`
- `CollapseRisk.Elevated` triggers at `nationalmorale ≤ breakmoraletrigger × 1.5`
- `Policy.CurrentChapter == 3` forces `CampaignPace.LateWarPressure` for Union regardless of rolling-window cadence
- `year ≥ 1864` AND CSA `CollapseRisk ≥ Elevated` blocks `StrategicIntent.Preserve` for CSA
- `EnemyReacted` fires when target-sector enemy strength rises by `EnemyReactionMultiplier`
- `NoContact` is never a valid input to `Escalate`
- `BattleHistory` spatial lookup matches battles within `aimaximumdistancetosearchforunitrelocations` of objective position and within last 7 game days
- chapter 1 winter (months 12, 1, 2) does not classify as `TooQuiet` even with zero battles
- Director threshold modifier on `MaximumProbeStrengthFraction` is bounded by ±50% of the personality delta on the same field (audacious-Lee with `Overheated` posture still has higher fraction than cautious-McClellan with `TooQuiet`)
- chapter/era/personality/winter modifiers compose without overriding hard safety gates
- 7-day rolling cycle runs one bounded slice per advanced game day; coalesced multi-day advances run one slice
- one-publish-per-real-second clamp: rapid same-second posture-change requests reuse the previous payload with `stale=true`
- 14-day window decays old events
- `BattleHistoryQuery.Near` returns matches inside both spatial and date windows; rejects matches outside either; uses squared-distance check (no `Math.Sqrt` on the hot path)
- pre-cleanup sidecar JSON containing `theaterCommanders` field loads cleanly (Newtonsoft ignores the unknown field; `CIC.Theaters` no longer exists)
- new sidecar JSON written after cleanup omits `theaterCommanders` and round-trips through save/reload
- offensive-eligibility paths (operational probe, transfer, perk role) all return the same answer for the same unit (single force-availability source)
- probe state has a single source of truth on the coordinator; `OperationalProbeOutput.State` reads through, never owns

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
