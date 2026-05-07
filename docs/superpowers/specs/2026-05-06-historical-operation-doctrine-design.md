# Historical Operation Doctrine Design

Status: active design spec, docs-only. Created 2026-05-06 after the user approved approach 3: historical operation profiles integrated into CIC planning, with dynamic handling for player-caused outcomes and intelligent enemy contest behavior.
Scope: strategic campaign-map doctrine. This spec designs the missing layer between objective scoring and coordinated movement execution. It is not an implementation plan.

## Goal

Add historical operation doctrine to Whiskey Realism without turning the campaign into a script.

The strategic AI should be able to form recognizable Civil War campaign concepts such as Peninsula-style pressure, Valley disruption, Vicksburg river operations, Atlanta pressure, capital defense, or CSA offensive-defensive counterstrokes. Those concepts should shape target choice, phase sequence, force posture, tempo, eligible formations, reinforcement behavior, and abort/exploit logic.

The system must also react when the player changes the war. If the player captures a target early, destroys a field army, holds a threatened corridor, or causes a major defeat, the AI must use that intelligence and adjust. The operation doctrine is a living envelope, not a fixed script.

## Non-Goals

- No deterministic "history must happen" campaign script.
- No chapter-only operation table.
- No replacement of vanilla `CampaignObjective` data.
- No bypass of vanilla operation-list semantics.
- No global `AICampaign.MoveUnitTo(...)` patch.
- No Transpiler.
- No tactical battle AI changes.
- No player-alliance steering when the player is CIC.
- No direct movement fallback for W&L player-chain units when the W&L bridge rejects or fails.
- No silent fallback. A doctrine pivot, recovery, abort, or alternate operation must be an explicit operation decision with a logged reason.

## Current Strategic Layer

Current authority chain:

```text
StrategicCoordinator
-> CIC.Replan / CIC.ReviewPlanWithTruth
-> OperationalPlan / Phase
-> FrontSectorLedger
-> DefenseIntentLedger
-> FormationDirectiveLedger
-> OperationalProbeLedger
-> CoordinatedOperationRuntime / WlStrategicOrderBridge / vanilla operations
```

Relevant shipped anchors:

- `StrategicCoordinator.RunStrategicReview(...)` evaluates phase truth and calls `cic.Replan(...)` when the active plan is missing, dirty, invalid, or finished: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs:278`.
- `CIC.Replan(...)` gets vanilla-available objectives, resolves Whiskey objective metadata, scores with `GrandStrategyRegistry`, picks an objective, and calls `BuildPlan(...)`: `src/WhiskeyRealism/Strategic/CIC.cs:91`.
- `CIC.BuildPlan(...)` currently creates generic one-or-two objective-backed phases: `src/WhiskeyRealism/Strategic/CIC.cs:155`.
- `OperationalPlan` currently has phases, deadlines, rationale, and dirty state but no stable operation identity: `src/WhiskeyRealism/Strategic/Phase.cs:24`.
- `Phase` currently has `TargetObjectiveId`, force fraction, transition, deadline, and fallback only: `src/WhiskeyRealism/Strategic/Phase.cs:13`.
- `ObjectiveAdapter.Resolve(...)` enriches vanilla `CampaignObjective` objects with Whiskey metadata: `src/WhiskeyRealism/Strategic/ObjectiveAdapter.cs:11`.
- `ObjectiveCatalog` is a narrow objective metadata table, not a campaign operation catalog: `src/WhiskeyRealism/Strategic/ObjectiveCatalog.cs:5`.
- `GrandStrategyRegistry` is an era/faction tag and project-bias profile, not a named operation catalog: `src/WhiskeyRealism/Strategic/GrandStrategyRegistry.cs:5`.
- `OperationalProbeRuntime.BuildInput(...)` already uses active CIC plan objective position, front state, formation directives, era, vanilla chapter, campaign month, and CIC personality: `src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs:13`.
- `CoordinatedOperationPackageLedger` and `CoordinatedOperationRuntime` now provide the campaign-map package execution layer: `src/WhiskeyRealism/Strategic/CoordinatedOperationPackageLedger.cs:142`, `src/WhiskeyRealism/Strategic/CoordinatedOperationRuntime.cs:129`.
- `WlStrategicOrderBridge` is the player-chain dispatch bridge and must remain the only W&L current-order path: `src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs:104`.

## Confirmed Vanilla Behavior

Vanilla chapters and objectives are useful anchors, but they are not named operations.

- `Policy.CurrentChapter` starts at `-1`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:211206`.
- `Policy.CheckForChapterUpdate()` owns chapter advancement: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:211604`.
- In W&L scenario `002`, vanilla starts chapter 1 and advances later by date/objective gates: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:211667`.
- `CampaignObjective` stores objective identity, display fields, scenario/chapter/date gates, target objects, prerequisites, and accomplishment state: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:178484`.
- `CampaignObjective.GetAvailableObjectives(...)` filters objectives by alliance, scenario, deactivation, accomplishment, and minimum enemy-owned target count; it does not score or plan operations: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:178825`.
- `AICampaign.PickCampaignObjective(...)` randomly picks from available objectives when vanilla owns the choice: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:17769`.
- `AICampaign.CheckOffensiveMovements(...)` creates local offensive movement packages from current map state; it is not a named operation planner: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14166`.
- Offensive commitment splits non-W&L direct moves from W&L current orders: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14383`, `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14449`.
- `AICampaign.CheckForDefensiveOperations(...)` creates unnamed threat-response packages: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:13505`.
- `AICampaign.UpdateMicroMovementInOffensive(...)` continues existing offensive operations and can retarget offensive units toward local objectives: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:13968`.

Conclusion: vanilla supplies objective gates, target objects, chapter/era pacing, and operation-list machinery. Whiskey owns named operation doctrine.

## Design Choice

Use integrated operation profiles inside CIC planning.

Do not add a separate upstream operation AI that competes with `CIC.ActivePlan`. The named operation is metadata and phase structure attached to `OperationalPlan` and `Phase`.

```text
Vanilla available objectives
-> Whiskey objective scoring
-> HistoricalOperationCatalog resolves operation profile
-> CIC builds OperationalPlan with operation identity and phase templates
-> PhaseTruthLedger checks reality
-> Director and ledgers shape posture and support
-> Coordinated operation runtime executes
```

Rejected alternatives:

- Objective-only flavor names. This would add logs/text without changing strategy.
- Separate operation selector above CIC. This creates a second strategic authority and risks fighting `ActivePlan`, phase truth, and formation ledgers.
- Chapter-only operation table. Vanilla chapter is too coarse and can lag or stall behind objective gates.

## Historical Operation Catalog

Add a pure catalog beside the existing strategic catalogs:

```csharp
HistoricalOperationCatalog.Resolve(
    int allianceId,
    EraStage era,
    int vanillaChapter,
    int month,
    int year,
    int objectiveId,
    ObjectiveMetadata objective,
    GrandStrategyProfile strategy,
    PersonalityVector cicPersonality,
    DirectorPosture posture,
    HistoricalOperationContext context)
```

The catalog must not read Unity objects, `AICampaign`, `DLC_WL`, or Harmony fields. Runtime adapters build the context.

### Operation Profile

```csharp
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
    public StrategyTag[] RequiredTags;
    public StrategyTag[] PreferredTags;
    public OperationTempoPreset Tempo;
    public OperationPosture Posture;
    public OperationPhaseTemplate[] Phases;
    public OperationDynamicRule[] DynamicRules;
    public string[] FallbackOperationIds;
}
```

Initial posture vocabulary:

- `ProbeAndDevelop` - find contact, force reaction, avoid immediate mass commitment.
- `ConcentratedAttack` - build a package and attack when local ratio and contact evidence pass.
- `ReinforceAndHold` - move support toward an engaged/threatened formation.
- `Counterstroke` - attack a vulnerable enemy concentration after enemy overextension.
- `ScreenAndDelay` - contest space without committing the main body.
- `ExploitBreakthrough` - follow up after major victory, target collapse, or enemy withdrawal.
- `Recover` - rebuild readiness and avoid aggressive movement.

### Phase Template

```csharp
public sealed class OperationPhaseTemplate
{
    public string PhaseId;
    public string PhaseName;
    public int TargetObjectiveId;
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
```

Every phase must still expose a valid `TargetObjectiveId` when one exists. Existing downstream systems depend on `ActivePlan.CurrentPhase.TargetObjectiveId`:

- `PickCampaignObjectivePatch` writes the active phase target into vanilla followed objective state.
- `ImportanceValuesPatch` steers area importance toward that objective.
- `PhaseTruthLedger` checks accomplishment, availability, position resolution, engagement, and force threshold.
- `FrontSectorRuntime`, `ArmyAreaRuntime`, `FormationDirectiveRuntime`, and `OperationalProbeRuntime` derive area/sector target context from that objective.

### Operation Context

The runtime adapter should build a compact context from existing ledgers:

```csharp
public sealed class HistoricalOperationContext
{
    public bool PlayerIsCic;
    public bool ObjectiveAvailable;
    public bool ObjectiveAccomplished;
    public bool TargetPositionResolves;
    public bool TargetEngagedRecently;
    public bool MajorFriendlyVictoryNearTarget;
    public bool MajorFriendlyDefeatNearTarget;
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
```

Inputs should come from:

- `PhaseTruthLedger`;
- `FrontSectorLedger`;
- `TheaterPressureView`;
- `ContactEvidenceLedger`;
- `BattleHistoryQuery`;
- `CampaignPaceLedger`;
- `StrategicResilienceDirector`;
- `FormationDirectiveLedger`;
- `DefenseIntentLedger`;
- `CampaignMapLedger`.

Do not re-scan the map where an existing ledger already has the information.

## Intelligent Contest Behavior

The AI must not simply beeline the selected objective.

Named operations should choose a campaign concept, then let the ledgers decide how to contest that concept against real enemy behavior.

Required intelligence rules:

1. **Threat before target.** If the enemy threatens a capital corridor, key supply hub, active landing, or decisive field army, the operation may pause, reinforce, or counterstroke instead of advancing on the objective.
2. **Contact before mass.** A mass attack requires contact evidence or a known enemy concentration. Empty-target objectives should prefer probe, screen, or single-lead movement.
3. **Contest routes and support areas.** The operation may assign phases to intermediate sectors, army areas, river/rail hubs, or support positions instead of only the final objective.
4. **Exploit real success.** Major victory near the target, enemy withdrawal, or the player breaking a corridor can advance or escalate the operation.
5. **Recover from real failure.** Major defeat, low readiness, low supply, or overmatch should produce recover, delay, reinforcement, or explicit abort.
6. **Preserve theaters.** Support pulls must respect existing formation directives, front budgets, critical-sector guards, capital-defense needs, and cross-theater donor caps.
7. **Use enemy inferred intent.** If enemy pressure clusters against the same theater/objective, the director and formation ledger should bias toward contesting the enemy operation, not racing past it.

This means two AI sides should fight over the campaign, not just race to separate objective dots.

## Dynamic Player Handling

The player can change the campaign. The operation doctrine must respond.

Examples:

- Player captures the operation target early: phase advances or operation completes.
- Player wins a major battle near the target: exploit or shorten the next phase if forces are fit.
- Player loses badly: recover, reinforce, or switch to fallback operation.
- Player holds a threatened area: enemy may redirect, counterstroke elsewhere, or reinforce before attacking.
- Player destroys local enemy strength: avoid sending a huge package into an empty target; probe or redirect.
- Player becomes CIC: Whiskey stands down for that alliance.
- Player-chain unit receives orders: use `WlStrategicOrderBridge` when vanilla current-order chain is eligible; otherwise skip and log. Never direct-move the player's W&L chain behind the player's back.

Dynamic changes are handled through phase truth and operation rules, not by mutating vanilla objectives.

## Plan And Phase Model Changes

Extend `OperationalPlan` minimally:

```csharp
public string OperationId;
public string OperationName;
public OperationTempoPreset OperationTempo;
public OperationPosture OperationPosture;
```

Extend `Phase` minimally:

```csharp
public string PhaseId;
public string PhaseName;
public string TargetAreaKey;
public string TargetSectorKey;
public OperationPosture OperationPosture;
public bool AllowCoordinatedAttack;
public bool AllowReinforcementPackage;
public bool AllowProbeOnly;
```

Persist the new compact fields in `PersistenceDto`. Do not persist operation candidate unit lists, raw snapshots, or map scans.

## Planning Flow

1. `StrategicCoordinator` forces/observes vanilla chapter update before objective reads, preserving the existing `CurrentChapter == -1` guard.
2. `CIC.Replan(...)` gets vanilla-available objectives with `mintownobjectives=0`, as it does today.
3. Each objective is scored with current `ObjectiveScoring`.
4. For top candidate objectives, `HistoricalOperationCatalog.Resolve(...)` returns an exact or fallback profile.
5. Objective score is adjusted by operation profile fit:
   - matching theater pressure;
   - active director intent;
   - enemy inferred intent;
   - CIC personality;
   - available force posture;
   - player-caused recent battle evidence.
6. `CIC.BuildPlan(...)` converts the selected operation profile into `OperationalPlan` and `Phase` records.
7. `PhaseTruthLedger` validates the active phase each daily tick.
8. Director posture and ledgers shape the execution thresholds.
9. `OperationalProbeLedger` and `CoordinatedOperationPackageLedger` execute through existing runtime surfaces.

Fallback resolution order:

```text
alliance + era + chapter + date window + objective
-> alliance + era + objective
-> alliance + objective
-> alliance + theater + strategy tags
-> current generic BuildPlan behavior
```

This is a catalog-resolution fallback, not a movement fallback. It selects an explicit operation profile or generic profile before planning; it must log which profile was selected and why.

## Director Ordering Requirement

Current review found director posture is published after some consumers use `DirectorMemories[alliance].LastPosture`. Historical operation doctrine should fix this ordering before operation profiles depend on same-day posture.

Required direction:

```text
Build phase truth and base front signal
-> publish/refresh director posture
-> build formation/probe/package decisions from current posture
```

The implementation plan must define the minimal reorder that avoids double-running expensive ledgers.

## Execution Boundaries

Historical operations do not move units directly.

Use existing execution surfaces:

- offensive/probe/escalation packages: `CoordinatedOperationRuntime.CommitPackage(...)`;
- W&L player-chain current orders: `WlStrategicOrderBridge.TryIssue(...)`;
- vanilla offensive interception: `CoordinatedOffensiveOperationsPatch`;
- operation continuation guard: `CoordinatedOffensiveMicroMovementPatch`;
- army-area redeploy only through `ArmyAreaRuntime` after its commit semantics are hardened;
- defensive/coastal response only after `CoastalDefenseCustomOrderRunner` commit semantics are hardened.

No direct movement fallback for selected W&L player-chain units.

## Required Same-Slice Hardening

The implementation plan for this spec must include these fixes before claiming named operations are a safe commit substrate:

1. Fix #38 pre-snapshot clear hazard. `CoordinatedOffensiveOperationsPatch` must never clear `ownunits` after package selection without a restorable snapshot.
2. Fix `CoastalDefenseCustomOrderRunner` so direct defensive commits only add to `unitsindefensiveoperations` after `AICampaign.MoveUnitTo(...)` succeeds.
3. Fix `CoastalDefenseCustomOrderRunner` W&L semantics so it does not mutate operation lists for bridge results whose decision says list mutation is not allowed, unless fresh vanilla evidence and a deliberate design exception prove otherwise.
4. Fix `ArmyAreaRuntime` theater-position ordering so bookkeeping changes do not claim a move that failed.
5. Fix operational-probe package target coordinates so support distance can be evaluated against the operation target, not only the lead/source position.
6. Change `WlStrategicOrderBridge` null-request classification to fail closed for future catalog integration.

## Initial Operation Families

Initial catalog rows should be small and inspectable. They should use current objective IDs and known theater metadata rather than inventing a large external data system.

Recommended first families:

- Union early East pressure: Richmond/Washington corridor and Peninsula-style pressure when Richmond-related objective metadata is active.
- CSA early East defense/counterstroke: capital defense, Shenandoah/B&O disruption, Maryland/Pennsylvania opportunity only when enemy pressure and commander audacity justify it.
- Union river/West pressure: river-control and supply-hub objectives when available by chapter/date.
- CSA western defensive depth: reinforce-and-hold or counterstroke against Union pressure.
- Late Union pressure: sustained exploit/attack posture when era/chapter/director evidence supports it.
- Late CSA protraction: recover, delay, defend capital corridors, and local counterstroke rather than broad beeline attacks.

The implementation plan should list exact objective IDs for the first rows after rechecking live `ObjectiveCatalog`, vanilla `CampaignObjective` data, and W&L scenario availability.

## Telemetry

Add bounded operation logs:

```text
[HistoricalOperation] alliance=0 selected=UnionEastPressure objective=3 phase=ApproachRichmond reason=exact-match score=...
[HistoricalOperation] alliance=1 action=pivot from=EastCounterstroke to=CapitalDefense reason=capital-corridor-threat
[HistoricalOperation] alliance=0 action=exploit operation=RiverPressure reason=major-victory-near-target
[HistoricalOperation] alliance=1 action=abort operation=MarylandOpportunity reason=objective-unavailable
```

Runtime movement evidence still comes from existing surfaces:

- `[CoordinatedOps] ... action=direct-move`;
- `[CoordinatedOps] ... action=wl-current-order`;
- `[CoordinatedOps] ... action=preflight-failed`;
- `[CoordinatedOps] ... action=package-no-commit`;
- `[OperationalProbe] ...`;
- `[Patch:ArmyArea] ...`;
- `[DefenseIntent] ...`;
- `[W&LDispatch] sanitized ...` for text quality only.

## Tests

Pure tests must cover:

- exact operation match by alliance/era/chapter/objective;
- fallback profile when chapter is `-1` or stale;
- no operation selection when player is CIC;
- player-caused target accomplishment advances or completes operation;
- major friendly victory permits exploit only when force posture is valid;
- major friendly defeat produces recover/fallback/abort;
- empty target does not produce mass package;
- enemy threat near capital corridor pauses or redirects objective pressure;
- operation profile preserves `TargetObjectiveId` for every phase;
- operation persistence round trip;
- W&L player-chain rejected bridge never direct-moves;
- director posture applies once, not as multiple stacked tempo multipliers.

Runtime smoke must include:

- `[HistoricalOperation] selected=...`;
- at least one explicit pivot/recover/abort/exploit decision in a controlled test campaign or fixture-driven harness;
- no package-no-commit hidden as successful operation execution;
- no exception, Harmony failure, repeated warning spam, or operation-list corruption.

## Acceptance Criteria

- Named operation profile is selected during CIC replanning and stored on `OperationalPlan`.
- Active phases still expose vanilla `TargetObjectiveId`.
- Operation profile changes behavior through phase templates, posture, tempo, allowed package modes, and dynamic rules.
- Both sides can contest the same theater intelligently through threat, contact, pressure, and director evidence; they do not simply race to objective points.
- Player-caused outcomes can advance, exploit, recover, abort, or replan the operation.
- W&L player-chain units are never direct-moved after bridge rejection.
- Existing vanilla operation lists remain coherent.
- Console harness covers pure catalog/phase/dynamic rules.
- Build/deploy/hash verification and runtime log smoke are required for the implementation plan, per root `AGENTS.md`.

## Not Verified Yet

- Exact first-row objective IDs for every named operation family need a fresh W&L scenario objective inventory before implementation.
- Runtime proof for coordinated packages is still pending `[CoordinatedOps]` in-game smoke after a fresh AI offensive opportunity.
- The safest defensive/coastal named-operation commit path depends on hardening `CoastalDefenseCustomOrderRunner`.
- Director reorder needs implementation-level measurement so the fix does not reintroduce expensive daily duplicate scans.
