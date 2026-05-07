# Historical Operation Doctrine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add named historical operation doctrine to the existing strategic layer so CIC plans become explicit operation profiles with dynamic contest/recover/exploit behavior, while preserving vanilla objective IDs, vanilla operation-list ownership, and W&L current-order rules.

**Architecture:** Pure catalog and dynamic-rule logic feeds `CIC.Replan(...)` and `PhaseTruthLedger`; `StrategicCoordinator` builds current context from existing ledgers; execution still happens only through existing probe/package/army-area/defense/W&L bridge surfaces.

**Tech Stack:** C# netstandard2.1, BepInEx 5.4.x, HarmonyX, console harness tests in `tests/WhiskeyRealism.Tests`, Grand Tactician vanilla anchors from `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

---

## Non-Negotiable Rules

- No generic plan fallback when historical operation doctrine is enabled.
- No direct movement fallback after a W&L bridge rejection or bridge failure.
- No objective-less phases in this slice; every phase must carry `TargetObjectiveId >= 0`.
- No hidden degrade-to-random behavior. Catalog misses are `NoProfile` logs and no new historical operation plan.
- No direct cancellation of vanilla `unitsinoffensiveoperations` or `unitsindefensiveoperations`.
- No Transpiler and no global `AICampaign.MoveUnitTo(...)` behavior patch.
- No player-alliance steering when the player is CIC.

## Source Anchors

- Active spec: `docs/superpowers/specs/2026-05-06-historical-operation-doctrine-design.md`
- CIC objective scoring and generic plan builder: `src/WhiskeyRealism/Strategic/CIC.cs`
- Daily review ordering and phase truth input: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`
- Plan and phase models: `src/WhiskeyRealism/Strategic/Phase.cs`
- Phase truth: `src/WhiskeyRealism/Strategic/PhaseTruthLedger.cs`
- Persistence DTOs: `src/WhiskeyRealism/Strategic/PersistenceDto.cs`
- Objective metadata table: `src/WhiskeyRealism/Strategic/ObjectiveCatalog.cs`
- Probe/package execution: `src/WhiskeyRealism/Strategic/OperationalProbeLedger.cs`, `src/WhiskeyRealism/Strategic/CoordinatedOperationRuntime.cs`
- W&L order bridge: `src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs`
- Offensive candidate patch: `src/WhiskeyRealism/Patches/CoordinatedOffensiveOperationsPatch.cs`
- Defensive runner: `src/WhiskeyRealism/Strategic/CoastalDefenseCustomOrderRunner.cs`
- Army-area runner: `src/WhiskeyRealism/Strategic/ArmyAreaRuntime.cs`
- Test harness: `tests/WhiskeyRealism.Tests/Program.cs`, `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`

## New File Structure

```text
src/WhiskeyRealism/Strategic/
├── HistoricalOperationModels.cs
├── HistoricalOperationCatalog.cs
├── HistoricalOperationContextBuilder.cs
├── OperationDynamicRuleEvaluator.cs
└── OperationDecisionMemory.cs
```

Add each new strategic source file to `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` with explicit `<Compile Include>` entries.

## Subagent Split

- Worker A: hardening substrate and probe target-coordinate fixes.
- Worker B: historical operation models, catalog, and pure catalog tests.
- Worker C: dynamic rule evaluator, phase truth integration, and pure rule tests.
- Worker D: CIC/coordinator integration, persistence, director ordering, and docs.

Workers are not alone in the codebase. They must not revert or overwrite unrelated edits. Worker write scopes must stay disjoint until integration.

---

## Task 1: Harden Existing Commit Surfaces First

- [ ] Add a fail-closed W&L null-request result.

Files:

- `src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs`
- `tests/WhiskeyRealism.Tests/Program.cs`

Implementation:

```csharp
internal enum WlStrategicOrderResult
{
    InvalidRequest,
    NotWl,
    DirectMovementAllowed,
    IssuedWlCurrentOrder,
    SkippedPlayerControlled,
    SkippedPlayerCic,
    FailedVanillaBridge,
    WlCurrentOrderIneligible,
    ReportOnly
}
```

Both `TryIssue(null)` and `ClassifyOnly(null)` must return:

```csharp
new WlStrategicOrderDecision(
    WlStrategicOrderResult.InvalidRequest,
    wlOrderType: -1,
    mayDirectMove: false,
    mayMutateOperationList: false,
    reason: "null-request");
```

Add harness tests:

```csharp
private static void WlBridgeNullTryIssueFailsClosed()
{
    var decision = WlStrategicOrderBridge.TryIssue(null);
    AssertEqual(WlStrategicOrderResult.InvalidRequest, decision.Result, "result");
    AssertEqual(false, decision.MayDirectMove, "mayDirectMove");
    AssertEqual(false, decision.MayMutateOperationList, "mayMutateOperationList");
}

private static void WlBridgeNullClassifyFailsClosed()
{
    var decision = WlStrategicOrderBridge.ClassifyOnly(null);
    AssertEqual(WlStrategicOrderResult.InvalidRequest, decision.Result, "result");
    AssertEqual(false, decision.MayDirectMove, "mayDirectMove");
    AssertEqual(false, decision.MayMutateOperationList, "mayMutateOperationList");
}
```

- [ ] Fix #38 pre-snapshot clear hazard in `CoordinatedOffensiveOperationsPatch`.

File:

- `src/WhiskeyRealism/Patches/CoordinatedOffensiveOperationsPatch.cs`

Required behavior:

- Register the `ownunits` snapshot before setting any local `blockVanilla` flag that can reach cleanup.
- In exception cleanup, clear/restore only when a snapshot exists for the `_aifaction` key.
- If package selection fails before snapshot creation, leave vanilla candidate lists untouched and log once.

Patch shape:

```csharp
bool snapshotRegistered = false;

// after ownUnits has been read and before any branch can decide to block vanilla:
_snapshots[_aifaction] = new CandidateSnapshot(ownUnits);
snapshotRegistered = true;

// catch/finally cleanup:
if (snapshotRegistered && _snapshots.TryGetValue(_aifaction, out var snapshot))
{
    snapshot.Restore(ownUnits);
    _snapshots.Remove(_aifaction);
}
else
{
    OnceLog.Warning(
        "coord-offensive:no-snapshot:" + _aifaction,
        "[CoordinatedOps] prefix failed before snapshot; vanilla candidate list left unchanged");
}
```

- [ ] Fix `CoastalDefenseCustomOrderRunner` commit semantics.

File:

- `src/WhiskeyRealism/Strategic/CoastalDefenseCustomOrderRunner.cs`

Required behavior:

- `SafeMoveUnitTo(...)` returns `bool`.
- Add a unit to `unitsindefensiveoperations` only when direct `AICampaign.MoveUnitTo(...)` returns true.
- Do not add W&L current-order units to defensive operation lists unless `bridgeDecision.MayMutateOperationList` is true.
- Record the per-tick unit signature after a successful W&L current order so the same runner does not spam repeated dispatch attempts.
- Log skip cases as skip cases; do not log them as successful defensive commits.

Code shape:

```csharp
if (bridgeDecision.Result == WlStrategicOrderResult.IssuedWlCurrentOrder)
{
    thisTick.Add(unitSig);
    Plugin.Log.LogInfo(
        $"[CoastalDefense] alliance={allianceId} action=wl-current-order unit={SafeName(unit)} reason={bridgeDecision.Reason}");
    continue;
}

if (!bridgeDecision.MayDirectMove)
{
    OnceLog.Info(
        $"coastal-defense:wl-skip:{allianceId}:{unitSig}:{bridgeDecision.Result}",
        $"[CoastalDefense] alliance={allianceId} action=skip-direct-move unit={SafeName(unit)} wlResult={bridgeDecision.Result} reason={bridgeDecision.Reason}");
    continue;
}

if (SafeMoveUnitTo(unit, anchor))
{
    if (!defOps.Contains(unit)) defOps.Add(unit);
    thisTick.Add(unitSig);
}
```

- [ ] Fix `ArmyAreaRuntime` theater-position ordering.

File:

- `src/WhiskeyRealism/Strategic/ArmyAreaRuntime.cs`

Required behavior:

- Call `SetTheaterPosition(unit, anchor)` only after `AICampaign.MoveUnitTo(unit, anchor, true)` returns true.
- W&L skip/current-order branches must not write `theaterposition`.

Patch shape:

```csharp
if (MoveUnitTo(unit, anchor))
{
    SetTheaterPosition(unit, anchor);
    issued++;
    // existing log
}
```

- [ ] Fix operational-probe package target coordinates.

Files:

- `src/WhiskeyRealism/Strategic/OperationalProbeLedger.cs`
- `src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs`
- `tests/WhiskeyRealism.Tests/Program.cs`

Implementation:

```csharp
public sealed class OperationalProbeInput
{
    public float TargetX;
    public float TargetZ;
    public bool HasTargetCoordinates;
}
```

`OperationalProbeRuntime.BuildInput(...)` sets these fields from `ObjectiveAdapter.ResolveObjectivePosition(objectiveId)`.

`OperationalProbeLedger.BuildPackage(...)` must use the operation target:

```csharp
TargetX = input.HasTargetCoordinates ? input.TargetX : lead?.X ?? 0f,
TargetZ = input.HasTargetCoordinates ? input.TargetZ : lead?.Z ?? 0f,
```

Add a pure test proving support-distance selection uses target coordinates instead of the selected lead/source assignment position.

Verification for Task 1:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected result:

```text
All tests passed
```

---

## Task 2: Add Historical Operation Models

- [ ] Create `src/WhiskeyRealism/Strategic/HistoricalOperationModels.cs`.

Required model surface:

```csharp
namespace WhiskeyRealism.Strategic
{
    public enum OperationChapterPolicy
    {
        Exact,
        AllowDateDrift
    }

    public enum OperationTempoPreset
    {
        Deliberate,
        Standard,
        Press,
        Exploit,
        Recover
    }

    public enum OperationPosture
    {
        Inherit,
        ProbeAndDevelop,
        ConcentratedAttack,
        ReinforceAndHold,
        Counterstroke,
        ScreenAndDelay,
        ExploitBreakthrough,
        Recover
    }

    public enum HistoricalOperationMatchKind
    {
        NoProfile,
        Matched
    }

    public enum OperationDynamicTrigger
    {
        ObjectiveUnavailable,
        ObjectiveAccomplished,
        TargetEngaged,
        MajorFriendlyVictoryNearTarget,
        MajorFriendlyDefeatNearTarget,
        EnemyThreatensCapitalCorridor,
        EnemyConcentratesInTheater,
        EmptyTarget,
        ForceBelowThreshold,
        ReplanThrash
    }

    public enum OperationDynamicAction
    {
        Continue,
        AdvancePhase,
        CompleteOperation,
        Recover,
        Pause,
        PivotToAlternateOperation,
        AbortOperation,
        Exploit,
        Counterstroke,
        ScreenAndDelay
    }

    public sealed class HistoricalOperationCandidate
    {
        public int ObjectiveId;
        public ObjectiveMetadata Objective;
        public float ObjectiveScore;
    }

    public sealed class HistoricalOperationContext
    {
        public bool ObjectiveAvailable;
        public bool ObjectiveAccomplished;
        public bool TargetPositionResolves;
        public bool TargetEngagedRecently;
        public bool MajorFriendlyVictoryNearTarget;
        public bool MajorFriendlyDefeatNearTarget;
        public bool EnemyThreatensCapitalCorridor;
        public bool EnemyConcentratesInTheater;
        public float TargetSectorOwnStrength;
        public float TargetSectorEnemyStrength;
        public float TargetSectorRatio;
        public float TheaterOwnPressure;
        public float TheaterEnemyPressure;
        public CampaignPace Pace;
        public StrategicIntent DirectorIntent;
        public CollapseRisk CollapseRisk;
        public int RecentReplanCount;
    }

    public sealed class HistoricalOperationProfile
    {
        public string OperationId;
        public string OperationName;
        public int AllianceId;
        public Theater Theater;
        public EraStage Era;
        public int MinChapter;
        public int MaxChapter;
        public int StartMonth;
        public int StartYear;
        public int EndMonth;
        public int EndYear;
        public int PrimaryObjectiveId;
        public int[] ObjectiveAllowList;
        public OperationChapterPolicy ChapterPolicy;
        public int Priority;
        public StrategyTag[] RequiredTags;
        public StrategyTag[] PreferredTags;
        public OperationTempoPreset Tempo;
        public OperationPosture Posture;
        public OperationPhaseTemplate[] Phases;
        public OperationDynamicRule[] DynamicRules;
        public string[] AlternateOperationIds;
        public float NearTargetRadius;
    }

    public sealed class OperationPhaseTemplate
    {
        public string PhaseId;
        public string PhaseName;
        public int TargetObjectiveId;
        public int TargetAreaId;
        public string TargetAreaKey;
        public string TargetSectorKey;
        public PhaseTransition Transition;
        public float ForceFractionRequired;
        public OperationPosture Posture;
        public bool AllowCoordinatedAttack;
        public bool AllowReinforcementPackage;
        public bool AllowProbeOnly;
        public int DeadlineDays;
    }

    public sealed class OperationDynamicRule
    {
        public string RuleId;
        public OperationDynamicTrigger Trigger;
        public OperationDynamicAction Action;
        public int Priority;
        public float MinOwnEnemyRatio;
        public float MaxOwnEnemyRatio;
        public float MinReadiness;
        public int WindowDays;
        public string AlternateOperationId;
        public string Reason;
    }

    public sealed class HistoricalOperationMatch
    {
        public HistoricalOperationMatchKind Kind;
        public HistoricalOperationProfile Profile;
        public float Score;
        public string Reason;
    }
}
```

- [ ] Extend `src/WhiskeyRealism/Strategic/Phase.cs`.

Add to `Phase`:

```csharp
public string PhaseId;
public string PhaseName;
public string TargetAreaKey;
public string TargetSectorKey;
public OperationPosture OperationPosture;
public bool AllowCoordinatedAttack;
public bool AllowReinforcementPackage;
public bool AllowProbeOnly;
public int PhaseStartedDaySerial;
```

Add to `OperationalPlan`:

```csharp
public string OperationId;
public string OperationName;
public OperationTempoPreset OperationTempo;
public OperationPosture OperationPosture;
public int OperationStartedDaySerial;
public int OperationLastDecisionDaySerial;
public bool PendingRetarget;
public string PendingRetargetReason;
```

Do not remove legacy `Phase.Fallback` in this slice; stop using it for new historical-operation decisions.

- [ ] Add test project includes for `HistoricalOperationModels.cs`.

```xml
<Compile Include="..\..\src\WhiskeyRealism\Strategic\HistoricalOperationModels.cs" Link="HistoricalOperationModels.cs" />
```

Verification:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

---

## Task 3: Add Explicit Historical Operation Catalog

- [ ] Create `src/WhiskeyRealism/Strategic/HistoricalOperationCatalog.cs`.

Required contract:

```csharp
public static class HistoricalOperationCatalog
{
    public static HistoricalOperationMatch Resolve(
        int allianceId,
        EraStage era,
        int vanillaChapter,
        int month,
        int year,
        HistoricalOperationCandidate candidate,
        GrandStrategyProfile strategy,
        PersonalityVector cicPersonality,
        DirectorPosture posture,
        HistoricalOperationContext context)
}
```

Catalog return helpers:

```csharp
public static HistoricalOperationMatch NoProfile(string reason)
{
    return new HistoricalOperationMatch
    {
        Kind = HistoricalOperationMatchKind.NoProfile,
        Profile = null,
        Score = 0f,
        Reason = reason
    };
}

private static HistoricalOperationMatch Matched(
    HistoricalOperationProfile profile,
    float score,
    string reason)
{
    return new HistoricalOperationMatch
    {
        Kind = HistoricalOperationMatchKind.Matched,
        Profile = profile,
        Score = score,
        Reason = reason
    };
}
```

Resolution rules:

- Reject null candidate or null metadata as `NoProfile("missing-candidate")`.
- Reject `candidate.ObjectiveId < 0` as `NoProfile("invalid-objective")`.
- Date window is a hard gate.
- Exact chapter profiles reject `vanillaChapter == -1`.
- `AllowDateDrift` profiles may match date/era even when chapter lags and must return reason containing `chapter-drift`.
- `ObjectiveAllowList` is mandatory and must include the candidate objective.
- Required tags must all be present in `candidate.Objective.StrategyTags`.
- Sort matches by:
  1. `Priority` ascending;
  2. calculated score descending rounded to 0.001;
  3. `OperationId` ordinal ascending.

Initial explicit rows:

```text
union-east-pressure       alliance=0 era=EarlyWar theater=East primaryObjective=3  allow=[3,37] chapterPolicy=AllowDateDrift
csa-capital-defense       alliance=1 era=EarlyWar theater=East primaryObjective=4  allow=[4,31,32] chapterPolicy=AllowDateDrift
csa-valley-disruption     alliance=1 era=EarlyWar theater=East primaryObjective=31 allow=[31,32,33] chapterPolicy=AllowDateDrift
union-coastal-pressure    alliance=0 era=EarlyWar theater=Coast primaryObjective=37 allow=[35,37] chapterPolicy=AllowDateDrift
union-western-pressure    alliance=0 era=MidWar theater=West primaryObjective=36 allow=[29,36] chapterPolicy=AllowDateDrift
csa-western-depth         alliance=1 era=MidWar theater=West primaryObjective=36 allow=[30,36] chapterPolicy=AllowDateDrift
union-late-pressure       alliance=0 era=LateWar theater=East primaryObjective=3  allow=[3,31,32,37] chapterPolicy=AllowDateDrift
csa-protraction-defense   alliance=1 era=LateWar theater=East primaryObjective=4  allow=[4,30,31,32,36] chapterPolicy=AllowDateDrift
```

Every row must define at least one phase. Every phase must have `TargetObjectiveId >= 0`. Early phase names should describe campaign intent, not hidden scripts:

```text
develop-contact
concentrate-for-attack
attack-objective
screen-and-delay
reinforce-and-hold
counterstroke
recover-combat-power
```

Every row must include dynamic rules for:

- objective unavailable -> abort;
- objective accomplished -> advance or complete;
- major friendly victory near target -> exploit when ratio allows;
- major friendly defeat near target -> recover;
- empty target -> screen/probe, not mass attack.

- [ ] Add catalog tests in `tests/WhiskeyRealism.Tests/Program.cs`.

Required tests:

```text
Historical catalog exact objective match selects explicit profile
Historical catalog returns NoProfile for unmatched objective
Historical catalog does not match exact chapter when chapter is -1
Historical catalog AllowDateDrift matches stale chapter by date and era
Historical catalog date window blocks chapter match
Historical catalog tiebreak uses priority score operation id
Historical catalog rejects objective-less phase templates
Historical catalog preserves TargetObjectiveId on every phase
```

- [ ] Add test project include.

```xml
<Compile Include="..\..\src\WhiskeyRealism\Strategic\HistoricalOperationCatalog.cs" Link="HistoricalOperationCatalog.cs" />
```

Verification:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

---

## Task 4: Add Dynamic Rule Evaluator And Phase Truth Integration

- [ ] Create `src/WhiskeyRealism/Strategic/OperationDynamicRuleEvaluator.cs`.

Required contract:

```csharp
public static class OperationDynamicRuleEvaluator
{
    public static PhaseTruthOutput Evaluate(
        PhaseTruthOutput baseOutput,
        HistoricalOperationProfile profile,
        HistoricalOperationContext context,
        int allianceId,
        int daySerial)
}
```

- [ ] Extend `PhaseTruthAction` in `PhaseTruthLedger.cs`.

Required enum:

```csharp
public enum PhaseTruthAction
{
    Continue,
    Advance,
    Complete,
    Recover,
    Pause,
    Pivot,
    Abort,
    Exploit,
    Counterstroke,
    ScreenAndDelay,
    Replan
}
```

Do not use `Fallback` for new historical-operation decisions. If existing code still compiles against `Fallback`, update it to `Recover`, `Abort`, or `Replan` according to the actual intent.

- [ ] Extend `PhaseTruthInput`.

```csharp
public HistoricalOperationProfile OperationProfile;
public HistoricalOperationContext OperationContext;
public int AllianceId;
public int DaySerial;
public float TargetSectorEnemyStrength;
```

- [ ] Extend `PhaseTruthOutput`.

```csharp
public string OperationId;
public string RuleId;
public string AlternateOperationId;
```

- [ ] Update `PhaseTruthLedger.Evaluate(...)`.

Required behavior:

1. Build the existing base truth.
2. If `input.OperationProfile == null`, return base truth unchanged.
3. If the hard invalid state is objective unavailable, missing position, or invalid phase target, return that state before dynamic rules.
4. Otherwise pass through `OperationDynamicRuleEvaluator.Evaluate(...)`.

Mapping:

```text
OperationDynamicAction.AdvancePhase             -> PhaseTruthAction.Advance
OperationDynamicAction.CompleteOperation         -> PhaseTruthAction.Complete
OperationDynamicAction.Recover                   -> PhaseTruthAction.Recover
OperationDynamicAction.Pause                     -> PhaseTruthAction.Pause
OperationDynamicAction.PivotToAlternateOperation -> PhaseTruthAction.Pivot when alternate resolves; Abort when alternate is missing
OperationDynamicAction.AbortOperation            -> PhaseTruthAction.Abort
OperationDynamicAction.Exploit                   -> PhaseTruthAction.Exploit
OperationDynamicAction.Counterstroke             -> PhaseTruthAction.Counterstroke
OperationDynamicAction.ScreenAndDelay            -> PhaseTruthAction.ScreenAndDelay
OperationDynamicAction.Continue                  -> PhaseTruthAction.Continue
```

Precedence:

```text
hard invalid target state
explicit abort or pivot
target accomplished advance or complete
explicit exploit, counterstroke, recover, pause, or screen rule
force below threshold or deadline truth
continue
```

- [ ] Update `CIC.ReviewPlanWithTruth(...)`.

Required routing:

```text
Advance      -> AdvancePhase()
Complete     -> mark active plan dirty and return false
Recover      -> mark active plan dirty and return false
Pause        -> keep plan and return true
Pivot        -> mark active plan dirty and return false
Abort        -> clear active plan and return false
Exploit      -> keep plan, stamp OperationLastDecisionDaySerial, return true
Counterstroke -> keep plan, stamp OperationLastDecisionDaySerial, return true
ScreenAndDelay -> keep plan, stamp OperationLastDecisionDaySerial, return true
Replan       -> mark active plan dirty and return false
Continue     -> existing ReviewPlan path
```

`Abort` must not build a generic plan. It clears the active plan and lets the later historical replan path either select an explicit profile or log `NoProfile`.

- [ ] Add dynamic-rule tests.

Required tests:

```text
Dynamic rule hard invalid target beats exploit
Dynamic rule objective accomplishment completes final phase
Dynamic rule major victory exploits when ratio is valid
Dynamic rule major defeat recovers
Dynamic rule empty target screens instead of mass attack
Dynamic rule missing alternate pivots to abort with alternate-missing reason
Dynamic rule precedence returns one final action
```

- [ ] Add test project include.

```xml
<Compile Include="..\..\src\WhiskeyRealism\Strategic\OperationDynamicRuleEvaluator.cs" Link="OperationDynamicRuleEvaluator.cs" />
```

Verification:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

---

## Task 5: Add Context Builder And Replan Memory

- [ ] Create `src/WhiskeyRealism/Strategic/OperationDecisionMemory.cs`.

Required surface:

```csharp
public sealed class OperationDecisionMemory
{
    private readonly int[] _recentReplanDaySerials = new int[16];
    private int _count;

    public void RecordReplan(int daySerial);
    public int CountRecentReplans(int daySerial, int windowDays);
    public int[] SnapshotRecentReplans();
    public void RestoreRecentReplans(int[] daySerials);
}
```

Requirements:

- Only store day serials.
- Drop entries older than the requested window before counting.
- Do not count duplicate same-day replans twice for one alliance.

- [ ] Create `src/WhiskeyRealism/Strategic/HistoricalOperationContextBuilder.cs`.

Required contract:

```csharp
public static class HistoricalOperationContextBuilder
{
    public static HistoricalOperationContext Build(
        int allianceId,
        int daySerial,
        int objectiveId,
        OperationalPlan plan,
        PhaseTruthOutput baseTruth,
        FrontSectorLedger fronts,
        DefenseIntentLedger defense,
        FormationDirectiveLedger formation,
        CampaignMapLedger map,
        TheaterPressureView pressure,
        DirectorPosture posture,
        OperationDecisionMemory memory,
        IReadOnlyList<BattleHistoryRecord> battleHistory)
}
```

Build rules:

- Resolve objective position once through `ObjectiveAdapter.ResolveObjectivePosition(objectiveId)`.
- Sector strength comes from `FrontSectorLedger.GetSector(FrontSectorRuntime.SectorKey(position))`.
- `MajorFriendlyVictoryNearTarget` and `MajorFriendlyDefeatNearTarget` use `BattleHistoryQuery.Near(...)` with a 14-game-day window.
- Near-target radius is `GamePrefs.aimaximumdistancetosearchforunitrelocations` in this slice.
- `RecentReplanCount` is `memory.CountRecentReplans(daySerial, 30)`.
- `Pace`, `DirectorIntent`, and `CollapseRisk` come from the already-published `DirectorPosture`.
- If an input ledger is null, set conservative values and log once through `OnceLog` only when the missing ledger changes behavior.

- [ ] Add test project includes.

```xml
<Compile Include="..\..\src\WhiskeyRealism\Strategic\OperationDecisionMemory.cs" Link="OperationDecisionMemory.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Strategic\HistoricalOperationContextBuilder.cs" Link="HistoricalOperationContextBuilder.cs" />
```

- [ ] Add tests.

Required tests:

```text
OperationDecisionMemory counts recent replans in 30 day window
OperationDecisionMemory does not double count same day
Operation context builder carries director posture without applying tempo twice
Operation context builder marks empty target when enemy strength is zero
```

Verification:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

---

## Task 6: Integrate Catalog Into CIC Planning Without Generic Fallback

- [ ] Add config entry in `src/WhiskeyRealism/Plugin.cs`.

Name:

```text
EnableHistoricalOperationDoctrine
```

Default:

```text
true
```

Description:

```text
Enable named historical operation doctrine for AI CIC planning. When enabled, catalog misses are logged as NoProfile and do not create generic replacement plans.
```

- [ ] Replace `CIC.Replan(...)` signature.

New signature:

```csharp
public void Replan(
    EraStageManager era,
    int currentMonth,
    int currentYear,
    int daySerial,
    int vanillaChapter,
    DirectorPosture posture,
    HistoricalOperationContext context)
```

Existing callers must pass the same-day posture and context from `StrategicCoordinator`.

- [ ] Split current generic builder from historical builder.

Keep:

```csharp
private OperationalPlan BuildLegacyGenericPlan(...)
```

Use it only when `EnableHistoricalOperationDoctrine` is false or an old sidecar plan continues through existing truth.

Add:

```csharp
private OperationalPlan BuildHistoricalOperationPlan(
    object pickedObjective,
    HistoricalOperationCandidate candidate,
    HistoricalOperationMatch match,
    PersonalityVector p,
    GrandStrategyProfile strategy,
    int currentMonth,
    int currentYear,
    int daySerial)
```

Plan creation rules:

- `match.Kind` must be `Matched`.
- `match.Profile` must not be null.
- Every phase template must have `TargetObjectiveId >= 0`.
- Stamp `OperationId`, `OperationName`, `OperationTempo`, `OperationPosture`, `OperationStartedDaySerial`, and `OperationLastDecisionDaySerial`.
- Convert `DeadlineDays` to month/year with current date arithmetic already used by `AddMonths(...)`; for this slice use `Math.Max(1, DeadlineDays / 30)` months.
- Phase posture resolves `Inherit` to profile posture at build time.

- [ ] Replace weighted random pick with deterministic explicit-profile selection while doctrine is enabled.

Required planning loop:

```csharp
var candidates = BuildScoredObjectiveCandidates(...);
candidates.Sort((a, b) => b.Candidate.ObjectiveScore.CompareTo(a.Candidate.ObjectiveScore));

HistoricalOperationMatch bestMatch = null;
ScoredCandidate bestCandidate = null;

foreach (var scored in candidates.Take(5))
{
    var match = HistoricalOperationCatalog.Resolve(...);
    LogProfileMissOrCandidateTrace(...);
    if (match.Kind != HistoricalOperationMatchKind.Matched) continue;
    if (IsBetter(match, bestMatch, scored.Candidate.ObjectiveScore))
    {
        bestMatch = match;
        bestCandidate = scored;
    }
}

if (bestMatch == null)
{
    Plugin.Log.LogInfo(
        $"[HistoricalOperation] alliance={AllianceId} action=no-profile objective={topObjectiveId} reason=no-explicit-profile");
    ActivePlan = null;
    return;
}

ActivePlan = BuildHistoricalOperationPlan(...);
```

No random weighted pick in the historical-operation path.

- [ ] Add CIC planning tests.

Required tests:

```text
CIC historical replan creates operation id and phase id
CIC historical replan logs NoProfile and leaves ActivePlan null when no explicit profile matches
CIC historical replan does not call legacy generic plan when doctrine is enabled
CIC legacy generic plan remains available when doctrine is disabled
CIC historical plan rejects objective-less phase
```

If `CIC.Replan(...)` remains hard to pure-test because objective availability uses reflection, extract the selection/build logic into an internal pure method:

```csharp
internal OperationalPlan SelectHistoricalPlanForTest(
    IList<HistoricalOperationCandidate> candidates,
    EraStageManager era,
    int currentMonth,
    int currentYear,
    int daySerial,
    int vanillaChapter,
    DirectorPosture posture,
    HistoricalOperationContext context)
```

Verification:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

---

## Task 7: Reorder Director Publish Before Historical Planning

- [ ] Update `StrategicCoordinator.RunStrategicReview(...)`.

Current issue:

```text
phase truth and CIC replan run before same-day front/director posture publish
```

Required order inside each alliance loop:

```text
player-CIC guard
build CIC if missing
era transition
succession
fiscal intent
front ledger, when operational runtime is ready
defense intent
formation directive ledger, when operational runtime is ready
campaign pace input
director posture publish or stale-mark
phase truth input using operation context
CIC review/replan
operational probe/package
army-area
construction
heartbeat/perf logs
```

Do not run front ledger twice in the same alliance/day. Preserve existing cadence checks and source signatures.

- [ ] Add per-alliance operation memory fields to `StrategicCoordinator`.

```csharp
private readonly OperationDecisionMemory[] OperationMemories =
{
    new OperationDecisionMemory(),
    new OperationDecisionMemory()
};
```

Whenever `CIC.Replan(...)` creates a new historical operation plan, call:

```csharp
OperationMemories[alliance].RecordReplan(daySerial);
```

- [ ] Feed dynamic context into phase truth.

`BuildPhaseTruth(...)` must:

- build base target evidence;
- build `HistoricalOperationContext`;
- pass the active profile/rules from the plan to `PhaseTruthInput`;
- include `TargetSectorEnemyStrength`.

Because `OperationalPlan` stores compact operation identity but not the full profile, resolve the active profile through:

```csharp
HistoricalOperationCatalog.TryGetById(cic.ActivePlan.OperationId, out var profile)
```

If `OperationId` is empty or profile cannot resolve, phase truth uses base behavior and logs once:

```text
[HistoricalOperation] action=profile-missing operation=<id> reason=phase-truth-profile-missing
```

- [ ] Ensure player-CIC still stands down before catalog/context work.

Required test:

```text
Strategic planning skips historical operation selection when player is CIC
```

Verification:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

---

## Task 8: Connect Operation Posture To Probe And Coordinated Packages

- [ ] Extend `OperationalProbeInput`.

```csharp
public OperationPosture OperationPosture;
public bool AllowCoordinatedAttack = true;
public bool AllowReinforcementPackage = true;
public bool AllowProbeOnly;
```

`OperationalProbeRuntime.BuildInput(...)` copies these from `cic.ActivePlan.CurrentPhase`.

- [ ] Map operation posture to probe/package options once.

Do this in `OperationalProbeRuntime.BuildInput(...)` after `OperationalTempoDoctrine.For(...)` and before director modifications.

Mapping:

```text
ProbeAndDevelop       -> keep doctrine options, reduce MaximumProbeStrengthFraction by 0.05
ConcentratedAttack    -> lower EscalateFriendlyRatio by 0.15, increase MaximumProbeStrengthFraction by 0.10
ReinforceAndHold      -> set AllowCoordinatedAttack=false, AllowReinforcementPackage=true
Counterstroke         -> lower EscalateFriendlyRatio by 0.10 only when ContactEvidence != EnemyAbsent
ScreenAndDelay        -> set AllowCoordinatedAttack=false, AllowProbeOnly=true
ExploitBreakthrough   -> lower MinimumProbeDays by 1 and lower EscalateFriendlyRatio by 0.20
Recover               -> set AllowCoordinatedAttack=false, AllowProbeOnly=false
```

Clamp:

```text
MinimumProbeDays >= 1
0.20 <= MaximumProbeStrengthFraction <= 0.70
1.10 <= EscalateFriendlyRatio <= 2.50
```

Do not re-apply `StrategicResilienceDirector.ApplyTo(...)` inside the selector if `input.Options` already contains director-shaped values. Single source of truth: `OperationalProbeRuntime.BuildInput(...)`.

- [ ] Gate package decisions by phase allow flags.

In `OperationalProbeLedger.FinishWithPackage(...)`:

- If `AllowProbeOnly` is true, return a single-lead probe and do not build a mass package.
- If `AllowCoordinatedAttack` is false and intent is attack, return `CoordinatedOperationDecision.None` with reason `operation-disallows-attack-package`.
- If `AllowReinforcementPackage` is false and decision is reinforce, return `None` with reason `operation-disallows-reinforce-package`.
- Empty-target probes must not produce mass packages unless posture is `ExploitBreakthrough` and `ContactEvidence != EnemyAbsent`.

- [ ] Add tests.

Required tests:

```text
Operation posture ScreenAndDelay prevents attack package
Operation posture ConcentratedAttack lowers escalation threshold once
Operation posture Recover prevents probe package
Empty target probe does not mass package without exploit posture
Director options are not applied twice
```

Verification:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

---

## Task 9: Persist Operation Identity And Replan Memory

- [ ] Extend `src/WhiskeyRealism/Strategic/PersistenceDto.cs`.

Add to `OperationalPlanDto`:

```csharp
public string operationId;
public string operationName;
public string operationTempo;
public string operationPosture;
public int operationStartedDaySerial;
public int operationLastDecisionDaySerial;
public bool pendingRetarget;
public string pendingRetargetReason;
```

Add to `PhaseDto`:

```csharp
public string phaseId;
public string phaseName;
public string targetAreaKey;
public string targetSectorKey;
public string operationPosture;
public bool allowCoordinatedAttack;
public bool allowReinforcementPackage;
public bool allowProbeOnly;
public int phaseStartedDaySerial;
```

Add to `DirectorMemoryDto` or a new sidecar DTO:

```csharp
public int[] recentOperationReplanDaySerials;
```

- [ ] Update save/load mapping in `StrategicCoordinator`.

Rules:

- Empty `operationId` on load means legacy generic plan.
- Legacy generic plans can continue through existing truth.
- If a legacy generic plan becomes dirty, historical doctrine replan must select an explicit profile or leave `ActivePlan = null`.
- Persist only compact identity and day serial arrays; do not persist catalog rows or candidate unit lists.

- [ ] Add persistence tests.

Required tests:

```text
Operation plan persistence round trip keeps operation id phase id and target objective id
Operation replan memory persistence round trip keeps recent day serials
Old sidecar plan with empty operation id loads as legacy generic plan
Dirty legacy generic plan does not create generic replacement when doctrine is enabled
```

Verification:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

---

## Task 10: In-Flight Package Retarget Handling

- [ ] Add active-package retarget guard.

Files:

- `src/WhiskeyRealism/Strategic/CIC.cs`
- `src/WhiskeyRealism/Strategic/CoordinatedOperationRuntime.cs`
- `src/WhiskeyRealism/Strategic/CoordinatedOperationPackageLedger.cs`

Required behavior:

- If `CIC.Replan(...)` selects a different operation target while the previous operation has committed units still in vanilla operation lists, do not remove those units.
- Mark the old/current plan:

```csharp
ActivePlan.PendingRetarget = true;
ActivePlan.PendingRetargetReason = "active-vanilla-operation-list";
```

- Log:

```text
[HistoricalOperation] alliance=<id> action=pending-retarget operation=<old> next=<new> reason=active-vanilla-operation-list
```

- Build the new plan only for future phases after vanilla releases existing operation-list units. If active units remain committed, leave the current plan in place with `PendingRetarget=true`.

- [ ] Add pure retarget test.

Required test:

```text
In-flight package retarget marks pending and does not clear operation-list ownership
```

Use a pure adapter that takes:

```csharp
currentOperationId
nextOperationId
hasActiveVanillaOperationUnits
```

and returns:

```text
KeepCurrentPendingRetarget
BuildNextPlan
```

Verification:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

---

## Task 11: Documentation And Catalog Registration

- [ ] Update `docs/handoff.md`.

Add:

- historical operation doctrine plan status;
- active config key;
- new build/deploy hash after implementation;
- current smoke status;
- known runtime telemetry strings.

- [ ] Update `docs/patch-catalog.md`.

Add a coordinator-driven runtime entry, not a Harmony ordinal:

```text
Coordinator runtime: HistoricalOperationCatalog / OperationDynamicRuleEvaluator
Purpose: named operation doctrine for CIC plans; no generic fallback while enabled.
Patch surface: none directly; consumes existing objective, phase truth, probe/package, W&L bridge surfaces.
```

If Task 1 changes an existing Harmony patch behavior, update the existing `CoordinatedOffensiveOperationsPatch` catalog entry with the #38 snapshot hardening note.

- [ ] Update `MEMORY.md`.

Add one terse line recording:

```text
2026-05-06: Historical operation doctrine active plan adds explicit catalog/no-profile CIC planning, dynamic phase-truth rules, and required hardening; no generic fallback while enabled.
```

- [ ] If implementation ships and smoke passes, archive:

```text
docs/superpowers/specs/2026-05-06-historical-operation-doctrine-design.md
docs/superpowers/plans/2026-05-06-historical-operation-doctrine.md
```

Do not archive before build/deploy/hash and runtime smoke.

---

## Task 12: Verification, Build, Deploy, Smoke

- [ ] Run console harness.

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected:

```text
All tests passed
```

- [ ] Build DLL.

```bash
./build.sh
```

Expected:

```text
Build succeeded
```

- [ ] Deploy DLL.

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
```

If this fails with `Invalid argument`, the game is running. Stop and tell the user to close Grand Tactician; do not claim deployment.

- [ ] Verify deployed DLL timestamp, size, and SHA-256.

```bash
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected:

```text
two equal file sizes
two identical SHA-256 hashes
```

- [ ] Runtime smoke.

Launch Grand Tactician, start or load a W&L career, and tail:

```bash
tail -n 220 "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Required evidence:

```text
[HistoricalOperation] alliance=... action=select operation=...
[HistoricalOperation] alliance=... action=no-profile ...   only if no catalog row matches; this is a visible stop, not success
[OperationalProbe] ...
[CoordinatedOps] ... action=direct-move
or
[CoordinatedOps] ... action=wl-current-order
```

Forbidden evidence:

```text
generic replacement plan created after [HistoricalOperation] action=no-profile
skip-direct-move followed by direct AICampaign.MoveUnitTo for the same W&L unit
package-no-commit logged as successful operation execution
ArgumentOutOfRangeException spam introduced by this slice
NullReferenceException spam introduced by this slice
Harmony patch failure
```

---

## Rollback Plan

- Config rollback: set `EnableHistoricalOperationDoctrine=false`. This permits legacy generic `BuildLegacyGenericPlan(...)` but leaves Task 1 safety hardening in place.
- Code rollback: revert only historical operation files and integration edits. Do not revert W&L fail-closed, #38 snapshot hardening, coastal commit semantics, army-area ordering, or probe target-coordinate fixes unless those specific fixes are proven faulty.
- Save compatibility: old sidecars with empty operation IDs remain loadable. New sidecars with operation IDs must load even when doctrine is disabled; execution then treats the plan as legacy-readable metadata and does not select new profiles.

## Completion Criteria

- All Task 1 hardening is implemented.
- Historical catalog selects explicit profiles and returns visible `NoProfile` misses.
- CIC historical planning never silently creates a generic replacement while doctrine is enabled.
- Dynamic rules produce one final phase-truth action.
- Player-CIC guard still prevents historical operation selection.
- W&L bridge rejection never leads to direct movement.
- Operation posture changes probe/package behavior through existing execution surfaces.
- Operation identity and replan memory persist round trip.
- Tests pass.
- DLL builds, deploys, and deployed SHA-256 matches `dist/WhiskeyRealism.dll`.
- Runtime log smoke shows historical operation selection or visible no-profile stop without new spam.
