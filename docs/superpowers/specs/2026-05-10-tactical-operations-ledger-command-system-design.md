# Tactical Operations Ledger Command System Design

Status: approved design direction. This spec defines the full integrated tactical
command system target for Whiskey Realism battle AI. It supersedes any narrower
"retarget one remaining patch" framing for the current idle/scattered-unit
problem, but does not archive
[`2026-05-09-tactical-orchestrator-remaining-patches-design.md`](2026-05-09-tactical-orchestrator-remaining-patches-design.md);
that document remains useful for existing shipped slice status and patch
ownership until this design is implemented.

## Problem

Current tactical AI can produce army plans, direct-child roles, charge/reserve
gates, stance pressure, pathfinder guards, deployment terrain corrections, and
decision-matrix telemetry. It still allows the observed bad state:

- command groups have an attack/defend/fix role.
- command groups remain scattered or in march column.
- `pathInterrupted=True`, `paths=0`, and `activeMove=False` can persist.
- no ledger record explains whether the command is holding, forming, waiting in
  reserve, pulling back, scouting, or preparing to attack.

That means the missing layer is not another isolated scorer. Whiskey needs a
full tactical command system: battlefield vision, operations ledger, objective
assignment, echelon task planning, bounded vanilla-safe order execution, and
continuous monitoring.

The governing rule is:

> Every AI command group must have a current purpose. Idle is legal only when
> the ledger says idle is correct.

## Design Goal

Both AI sides should behave like Civil War armies:

- scout and update an incomplete battlefield picture.
- identify valuable objectives, choke points, roads, bridges, fallback lines,
  staging areas, and enemy-held lines when the game data supports it.
- estimate enemy strength and confidence per objective.
- choose single, sequential, parallel, fix-and-flank, defensive, or delay
  operations.
- assign corps/division/brigade-like command nodes to objectives and roles.
- commit to pushes long enough to avoid thrash.
- preserve reserves with explicit covered objectives and release triggers.
- reform, pull back, hold, stage, or attack instead of sitting unexplained.
- dynamically react to strong new evidence without omniscient RTS twitching.

The intended operating mode is active, not dormant:

```text
TacticalCommanderMode = Active
```

The system may keep one emergency kill switch, but the normal target is that
vision, ledger, echelon command, task planning, execution, and monitoring run
together for AI sides.

## Confirmed Vanilla Anchors

These anchors were checked against
`/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` on 2026-05-10. Line numbers are
decompile coordinates, not source symbols shipped by the game.

| Area | Vanilla anchor | Confirmed behavior | Design implication |
|---|---|---|---|
| Tactical loop | `AIBattle.UpdateAITasks()` `:5857` | Calls `AssignReserves`, `UpdateMovingTargets`, flank calculations, `CheckGlobalAIStrategy`, `CheckEndOfActions`, `AdjustGroupAIStance`, `AdjustGroupFormations`, and `CheckForFeudGroupActions`. | Whiskey command monitoring should tick from existing orchestrator lifecycle and consume these surfaces rather than replacing the whole battle loop. |
| Stance | `AIBattle.AdjustGroupAIStance()` `:4221` | Strength/macro based stance writer through `BattleUnits.ChangeStance`; respects `PerformAIActionDLCWL`. | Existing #45 is a valid stance-consumer surface, but stance alone cannot create objective/task discipline. |
| Formation | `AIBattle.AdjustGroupFormations()` `:5875` | Converts ordered stance into group formation only when stance matches ordered stance, stance > 0, paths are clear, and subordinates are not moving/engaged. | Vanilla formation logic skips many stuck/moving cases; Whiskey must monitor illegal idle and use bounded group-formation corrections when eligible. |
| Objective-chain movement | `AIBattle.UpdateMovingTargets()` `:6900` | Moves objective-chain center groups with `BattleUnits.SetWaypoint`; skips when `pathinterrupted`, blocked order, active order, active combat, or subordinates moving. | The observed `pathInterrupted=True` state can prevent vanilla movement. Whiskey must detect and recover stalled objective movement instead of assuming vanilla will fix it. |
| Line fallback | `AIBattle.CheckLineFallbacks(Regiment)` `:5118` | Reactive per-attached-unit fallback on morale/outflank pressure; writes paths and movement/combat behavior. | Existing fallback surfaces are reactive. Ledger-driven fallback lines must be planned, not only panic retreat. |
| Reserve local support | `AIBattle.CheckUseOfReserves(Regiment)` `:6062` | Moves nearby attached units to support outflanked attached units through direct `RegimentSetPath`. | #59 can gate reserve misuse, but the full system also needs reserve areas, covered objectives, and release triggers. |
| Reserve assignment | `AIBattle.AssignReserves()` `:7017` | Assigns reserve groups into objective-chain line/flank/artillery groups based on vanilla objective chains and flank strength. | Whiskey can read and monitor vanilla reserve/objective-chain state, but should not rely on it as the whole operations ledger. |
| Waypoint writer | `BattleUnits.SetWaypoint(Regiment, ...)` `:91232` | Applies readiness/W&L/order-delay/retreat guards, may queue orders, clears interruption paths by default, and calls `SetGroupFormation` for command groups (`unittyp > 13`). | Primary movement writer. Use with strict eligibility and order-cooldown gates; respect order delay by default. |
| Group formation writer | `BattleUnits.SetGroupFormation(Regiment, ...)` `:91822` | For command groups, builds hierarchy offsets, sets group formation/order, and writes subordinate paths through vanilla formation machinery. | Primary formation/reform writer. Use for brigade/division/corps-like posture corrections instead of manual subordinate path surgery. |
| Hierarchy read | `BattleUnits.GetHierarchyTree(Regiment, ...)` `:92720` | Builds direct-child hierarchy tree from attached units. | Confirms generic command-node tree is a valid read model for variable GT hierarchies. |
| Direct attached read | `Regiment.GetAttachedUnitsReg(... directonly: true ...)` `:119854` | Filters attached units by direct parent, skirmishers, active/routed state, garrisons, and type. | Existing command-node tree and echelon assignment should continue using this vanilla hierarchy path. |
| Order delay | `Regiment.AddOrderCourierline(...)` `:125009`, `Regiment.ProcessOrders()` `:125173` | Models bugle/courier order delay, secondary courier lines, order status, formation orders, and lost couriers. | Command execution must treat active order queues/couriers as valid waiting, not illegal idle. |

Not verified as a clean vanilla API: battle-local bridge/road/town/choke-point
enumeration. The spec treats those as Whiskey battlefield-intelligence products
derived from available vanilla objects, objective-chain targets, terrain samples,
unit positions, movement corridors, and future verified anchors. Do not claim
Grand Tactician exposes a complete POI graph until a follow-up anchor review
proves the exact source.

## Current Whiskey Mod Anchors

Shipped or current-main surfaces this design must fit:

- `TacticalBattleCoordinator` and `TacticalBattleOrchestrator` own battle
  lifecycle and per-side orchestrator state.
- `ArmyOrchestrator` owns current army plan, command tree, command-node intents,
  intent inference, and direct-child fallback lookup.
- `CommandTreeRuntime`, `CommandTreeBuilder`, and `CommandIntentResolver` already
  provide a read-only generic command-node tree from vanilla hierarchy.
- #35 `TacticalObserverPatch` owns tactical lifecycle/telemetry and observes
  objective-chain movement/mutation and decision-matrix rows.
- #41 `BattleChargeGatePatch` owns charge initiation gating.
- #42 `BattleFeudActionGatePatch` owns feud movement gating.
- #45 `BattleGroupStancePatch` owns stance pressure and currently does not issue
  movement, reserve, fallback, artillery, or charge orders.
- #57 `BattleReserveDoctrinePatch` owns reserve-list bias.
- #59 `BattleReserveCommitGatePatch` owns reserve-commit rollback/gating.
- B7/B8 artillery and withdrawal/fallback patches exist, but are not command
  ledger driven.
- #60 `TacticalDeploymentTerrainDisciplinePatch` corrects deployment terrain and
  facing only during deployment placement, not continuous battle posture.

The new system should not duplicate those patches. It should promote the current
orchestrator from plan/role/gate substrate into an operations-ledger command
system, then retarget the existing patch consumers to that state.

## Architecture

```text
TacticalVisionOrchestrator
  -> BattlefieldPicture

TacticalOperationsLedger
  -> objectives, scouting reports, operations, reserves, commitment windows

ArmyCommandOrchestrator
  -> operation shape, main effort, supporting/fixing objectives, reserve policy

EchelonCommandOrchestrator
  -> corps/division/brigade-like node assignments from generic command tree

BrigadeTaskPlanner
  -> concrete task records for command nodes

CommandPostureExecutor
  -> bounded vanilla-safe formation/movement/stance/fallback corrections

TacticalCommandMonitor
  -> continuous purpose/progress/stuck/idle validation and correction requests
```

Implementation may keep generic command-node types internally because Grand
Tactician command hierarchy varies by scenario and `GamePrefs.commandhierarchyshift`.
The behavior must still be echelon-aware. "Corps-like", "division-like", and
"brigade-like" are runtime roles assigned to generic command nodes, not a
requirement to hard-code brittle class towers.

## Tactical Vision Orchestrator

`TacticalVisionOrchestrator` owns the shared battlefield picture for one side.
It does not issue orders. It turns vanilla observations into confidence-weighted
intelligence.

```text
BattlefieldPicture
  side
  version
  updatedAt
  objectiveReports[]
  enemyContactReports[]
  threatSectors[]
  friendlyPosture[]
  recommendedScoutingNeeds[]
  recommendedFallbackLines[]
  recommendedStagingAreas[]
```

Enemy contact reports:

```text
EnemyContactReport
  contactId
  location
  estimatedStrengthMin
  estimatedStrengthMax
  unitTypeGuess
  source:
    VisualContact
    RecentFire
    ScoutReport
    ObjectivePressure
    FriendlyRoutedFromArea
    InferredReserveMovement
  confidence
  firstSeenAt
  lastSeenAt
  staleAfter
  associatedObjectiveId
```

Objective intelligence:

```text
ObjectiveIntel
  objectiveId
  type:
    VictoryPoint
    Bridge
    Ford
    RoadJunction
    Town
    Ridge
    ChokePoint
    EnemyLine
    FriendlyLine
    FallbackLine
    StagingArea
    UnknownVanillaObjective
  location
  radius
  control:
    Unknown
    Friendly
    Enemy
    Contested
    Neutral
  enemyDefenseEstimate:
    Empty
    Light
    Moderate
    Strong
    Unknown
  confidence
  lastConfirmedAt
  approachThreat
  flankingOpportunity
  isolationRisk
  terrainStrength
  approachDifficulty
  connectedObjectives[]
```

Initial objective discovery must be conservative:

- confirmed vanilla objective-chain objects and `Regiment.currentsetobjective`
  references are valid sources.
- deployment observer terrain/facing data can inform staging/fallback safety.
- visible enemy bearings and recent fire can create enemy-line objectives.
- bridge/road/choke classification is allowed only when derived from verified
  scene objects, route geometry, terrain samples, or explicit future anchors.
- otherwise classify as `UnknownVanillaObjective` and still assign formation,
  holding, scouting, or attack tasks around the known position.

The AI is not omniscient. Reports carry confidence and staleness. Aggressive
commanders may act on lower confidence; cautious/methodical commanders require
more scouting before commitment.

## Tactical Operations Ledger

The ledger is the side's tactical memory and commitment system.

```text
TacticalOperationsLedger
  side
  battleId
  currentBattlePhase
  battlefieldPictureVersion
  objectives[]
  operations[]
  reserveAssignments[]
  scoutingAssignments[]
  commandNodeStates[]
  lastMajorReplanTime
  replanPressure
```

Objective record:

```text
ObjectiveRecord
  objectiveId
  intel
  value
  enemyKnownStrength
  enemyEstimatedStrength
  enemyConfidence
  enemyLastSeenAt
  friendlyAssignedStrength
  friendlyNearbyStrength
  status:
    Unknown
    Scouting
    WeaklyHeld
    StronglyHeld
    Contested
    Secured
    Lost
```

Operation record:

```text
OperationRecord
  operationId
  operationShape:
    SingleMainEffort
    SequentialObjectives
    ParallelObjectives
    FixAndFlank
    DefensiveNetwork
    DelayAndFallback
  targetObjectives[]
  supportingObjectives[]
  assignedCommandNodes[]
  reserveCommandNodes[]
  phase:
    Planning
    Scouting
    Forming
    Committed
    Exploiting
    Consolidating
    Aborting
    Complete
  commitmentStartedAt
  minimumCommitUntil
  nextReviewAt
  abortConditions
  successConditions
  reserveReleasePolicy
```

Scouting assignment:

```text
ScoutingAssignment
  scoutNode
  targetObjective
  task:
    Probe
    Screen
    Observe
    ConfirmWeakness
    ConfirmEnemyMainBody
    WatchRoad
    CoverFallbackLine
  confidenceGoal
  avoidDecisiveEngagement
  reportDeadline
  fallbackTask
```

Reserve assignment:

```text
ReserveAssignment
  commandNode
  reserveArea
  coveredObjectives[]
  releaseTriggers[]
  minimumHoldUntil
  reserveFractionProtected
```

Anti-thrash rule: once an operation is `Committed`, it should not switch targets
until a commitment window expires or an explicit abort/success condition fires.
Major replans require strong evidence:

- objective secured or lost.
- assigned force routed, badly mauled, or no longer combat-effective.
- enemy strength estimate changes materially with sufficient confidence.
- high-confidence enemy main body appears in a threatening position.
- reserve exhausted or flank/rear threat becomes critical.
- commander personality permits faster improvisation.

Local posture recovery does not imply a major replan. A brigade fixing a stuck
path should not rewrite the army operation.

## Operation Selection

The army chooses one operation shape at a time, with subordinate operations
inside it:

```text
SingleMainEffort
SequentialObjectives
ParallelObjectives
FixAndFlank
DefensiveNetwork
DelayAndFallback
```

Inputs:

```text
availableFriendlyStrength
estimatedEnemyStrengthByObjective
objectiveValue
objectiveDistance
road/terrain connectivity
confidence in reports
current battle phase
reserve requirement
commander personality
army morale/fatigue
casualty pressure
time pressure
```

Parallel attacks are allowed, but require a fair strength advantage at each
target, not merely total-map superiority:

```text
per-objective odds advantage
reserve still protected
sufficient scouting confidence
objectives not too isolated
roads/terrain support simultaneous movement
commander personality allows risk
```

Personality modifiers:

- aggressive/high-initiative commanders accept lower confidence and thinner
  reserves.
- methodical commanders prefer scouting and synchronized forming.
- cautious commanders require clearer advantage and a larger reserve.
- reckless commanders may over-split, but still need some strength basis.
- defensive commanders prioritize choke points, ridges, bridges, fallback lines,
  and reserve preservation.

Example decisions the ledger must support:

```text
Enemy strongly holds Objective A.
Objective B is weakly held.
Assign one division to fix A.
Assign stronger force to attack B.
Keep central reserve.
Commit for 20-30 minutes unless abort conditions fire.
```

```text
Objectives A and B are both weak.
Friendly force has per-objective advantage and connected roads.
Launch parallel attacks.
Reserve remains central and unreleased.
```

## Echelon Command Orchestration

Each command node receives persistent operational state:

```text
CommandNodeOperationalState
  nodeId
  echelonKind:
    ArmyLike
    CorpsLike
    DivisionLike
    BrigadeLike
  parentNodeId
  childNodeIds[]
  assignedOperationId
  assignedObjectiveId
  assignedRole:
    MainEffort
    SupportingAttack
    FixingForce
    ScreeningForce
    Reserve
    Defender
    FallbackGuard
    Probe
    FlankMarch
  currentTask
  taskState
  assignedArea
  formationTarget
  movementTarget
  fallbackTarget
  supportTarget
  lastOrderTime
  lastProgressTime
  stuckReason
```

Responsibilities:

```text
ArmyLike
  chooses operation shape and reserve policy.

CorpsLike
  owns a sector or objective group and coordinates multiple divisions/brigades.

DivisionLike
  assigns brigades to concrete tasks, support timing, and local reserve.

BrigadeLike
  executes one concrete task: form, advance, hold, screen, probe, fall back.
```

Reaction level:

```text
Brigade:
  fix path interruption, reform line, face local threat, halt if unsupported.

Division:
  shift support brigade, commit local reserve, pull back to fallback line.

Corps:
  redirect division from secondary objective, reinforce main attack, refuse flank.

Army:
  change operation shape, abandon objective, launch parallel attack, order withdrawal.
```

This prevents one local sighting from causing the whole army to abandon a
committed push.

## Task Planning

Every active command node gets one primary task:

```text
CommandTaskRecord
  taskId
  commandNodeId
  parentOperationId
  taskType:
    Scout
    Probe
    Screen
    FormUp
    AdvanceToAssembly
    AttackObjective
    FixEnemy
    SupportAttack
    HoldObjective
    HoldChoke
    GuardFlank
    ReserveWait
    ReleaseReserve
    FallBackToLine
    Delay
    Consolidate
    RecoverStuckOrder
  targetObjectiveId
  targetLocation
  formationPreference
  movementModePreference
  requiredSupportState
  startAfter
  minimumHoldUntil
  expireAfter
  successCondition
  abortCondition
  currentState:
    Planning
    MovingToAssembly
    Forming
    WaitingForCommit
    Committed
    Engaged
    Reorganizing
    Complete
    Failed
  lastProgressAt
  lastOrderIssuedAt
```

Support readiness gates:

- main force formed.
- support force near enough.
- reserve still available.
- enemy estimate acceptable.
- objective not already lost/impossible.
- order delay/courier state settled.

Progress evidence:

- distance to target improving.
- formation reached.
- enemy contact made.
- objective contested/secured.
- strength/morale still viable.
- path interrupted.
- order queue stuck.
- no progress for N minutes.

If progress stalls, the task planner can emit `RecoverStuckOrder`, `FormUp`,
`ShiftToAlternateApproach`, `RequestSupport`, or `AbortReview`. Aborting a
committed operation remains explicit and ledger-driven.

## Command Posture Executor

The executor is the only layer that writes vanilla battle state for this system.
It does not invent strategy. It enforces current ledger assignments with the
smallest vanilla-safe correction.

```text
CommandPostureExecutor
  input:
    CommandNodeOperationalState
    TacticalOperationsLedger
    BattlefieldPicture
    current vanilla group state
  output:
    PostureExecutionDecision
      NoWrite
      SetFormation
      SetWaypoint
      SetFormationAndWaypoint
      ChangeStance
      ReleaseReserve
      FallbackToLine
      RecoverInterruptedOrder
```

Write eligibility gate:

```text
W&L/player-subordinate protected -> NoWrite
group routed/marked for rout -> NoWrite
order queue active/courier pending -> NoWrite or WaitForCourier
recent order issued -> NoWrite
currently engaged at close range -> limited stance/formation only
movement path active and making progress -> NoWrite
vanilla already doing correct task -> NoWrite
missing ledger assignment -> NoWrite with telemetry
```

Task-to-execution examples:

```text
ReserveWait:
  If not near reserve area, SetWaypoint(reserveArea).
  Else SetFormation(defensive/line/column by terrain) and hold.

FormUp:
  If in march column near enemy/objective, SetGroupFormation(line/column).
  If not at assembly area, SetWaypoint(assemblyArea).

AttackObjective:
  If committed and support ready, SetWaypoint(approach point) and set attack
  formation.
  If support not ready, hold/form at assembly area.

FixEnemy:
  Set line formation and hold/slightly advance to maintain contact. Do not chase
  unless parent operation permits.

FallbackToLine:
  SetWaypoint(fallback target), set defensive formation, and use emergency
  withdrawal only when doctrine says the force is collapsing.

RecoverInterruptedOrder:
  If pathInterrupted && paths == 0 && no active move, reissue through
  SetWaypoint/SetGroupFormation when eligible or assign a nearby hold/form-up
  target.
```

The executor should be implemented through Postfix consumers or existing
write-surface extensions where possible. Prefixes require explicit justification
because repo policy treats Prefix-blocking and Transpilers as brittle.

## Tactical Command Monitor

Monitoring is part of active mode. It is not optional debug-only behavior.

The monitor checks each tick/window:

- every command node has an objective or valid no-objective reason.
- every command node has a task.
- each command is moving, forming, holding, fighting, scouting, falling back, or
  valid reserve.
- path interruption/stuck state is being recovered.
- committed pushes are not abandoned prematurely.
- force splitting remains above safe strength thresholds.
- reserves remain intact unless release triggers fire.
- ledger intelligence is not stale past its review window.

Allowed idle:

- `ReserveWait` at assigned reserve area.
- `HoldObjective`, `HoldChoke`, or `FallbackGuard` at assigned location.
- `FormUp` with active formation target/order delay.
- `WaitingForCommit` under parent operation.
- scouting/screening in assigned zone.
- pinned, routed, courier-delayed, or player-protected.

Illegal idle:

- no operation or task.
- no target.
- march column near enemy/objective.
- `pathInterrupted=True`, `paths=0`, `activeMove=False`, and no valid ledger
  reason.
- active combat command outside assigned area doing nothing.

Illegal idle should create a correction request, not only a log line.

## Telemetry

Telemetry must explain what the army knows, what it decided, and why each
command is moving, holding, waiting, or left alone.

Ledger:

```text
[TacticalOpsLedger]
side=1 phase=MainEffort operation=ParallelObjectives
objectives=BridgeA,RoadJunctionB reserveFraction=0.22 confidence=0.71
reason=objective-b-weak-enough-objective-a-fixable commitUntil=14:35
```

Objective intelligence:

```text
[TacticalObjectiveIntel]
side=1 objective=BridgeA type=Bridge status=StronglyHeld
enemyEstimate=2800-4200 confidence=0.82 source=visual+recent-fire
```

Operation:

```text
[TacticalOperation]
side=1 operationId=op-12 shape=FixAndFlank phase=Committed
mainObjective=RoadJunctionB fixObjective=BridgeA assignedMain=Division_2
assignedFix=Division_1 reserve=Brigade_4
reason=bridge-strong-road-junction-weak minimumCommitUntil=14:35
```

Assignment:

```text
[TacticalCommandAssignment]
side=1 node=3rd_Division echelon=DivisionLike operation=op-12
role=MainEffort objective=RoadJunctionB task=AttackObjective state=Forming
reason=best-strength-near-weak-objective
```

Executor:

```text
[TacticalCommandPosture]
side=1 node=5th_Brigade task=FormUp decision=SetFormationAndWaypoint
reason=illegal-idle-march-column-path-interrupted currentFormation=MarchColumn
targetFormation=Line paths=0 pathInterrupted=True activeMove=False
```

Summary:

```text
[TacticalPostureSummary]
side=1 validIdle=6 illegalIdle=0 recoveringStuck=2 activeAttacks=3
holdingObjectives=4 reservesWaiting=1 writesThisMinute=5 deniedPlayerProtected=2
```

If a screenshot shows units sitting, the logs must identify one of:

- holding an assigned point.
- reserve at assigned reserve area.
- forming for a committed operation.
- waiting for support or order delay.
- stuck and recovery fired.
- protected by W&L/player-subordinate guard.

If the log cannot explain the idle state, the behavior is wrong.

## Config Contract

The normal intended mode is active:

```ini
[Tactical Orchestrator]
Tactical Commander Mode = Active
```

Mode semantics:

```text
Off:
  existing tactical observers/gates may still run by their legacy flags, but the
  operations ledger command system is disabled.

MonitorOnly:
  vision, ledger, assignments, tasks, monitor, and telemetry run; executor emits
  NoWrite diagnostics.

Active:
  full loop runs for AI sides and executor may issue bounded vanilla-safe writes.
```

This master mode should reduce reliance on many scattered behavior toggles. The
implementation may preserve legacy flags for migration and emergency debugging,
but the design target is one active tactical commander system, not dormant
feature fragments.

## Integration Boundaries

Required safety boundaries:

- AI sides only unless `GameVars.ai_vs_ai` makes both sides AI.
- W&L player-subordinate/current-command protection before any write.
- no patch-time command-tree construction inside hot write surfaces.
- no direct edits to game install or managed game DLLs.
- no Prefix-blocking or Transpiler patches without a separate approval.
- no writes to strategic mod state from Harmony battle patches.
- all executor decisions must be idempotent and cooldown-gated.
- missing anchors or missing ledger state fail closed to NoWrite for the new
  executor, while legacy gates keep their existing fail-open behavior where
  already shipped.

Existing patch retargeting direction:

- #35 remains lifecycle/observer/decision-matrix host and may host monitor
  telemetry.
- #45 stance writer should consume command tasks instead of standalone sector
  scorer output.
- #57/#59 reserve behavior should consume `ReserveAssignment` and operation
  release triggers.
- #41 charge gate should remain role/task aware and deny non-attack roles.
- B8 fallback/withdrawal should consume planned fallback tasks before emergency
  withdrawal.
- #60 remains deployment-only; continuous posture correction belongs to the new
  executor, not deployment terrain discipline.

## Verification Expectations

Even though active mode is the target, this is a broad vanilla-state writer and
must be verified as such.

Pure harness coverage should lock:

- objective confidence decay and stale report handling.
- operation selection for single, sequential, parallel, fix-and-flank, defense,
  and delay.
- parallel attack thresholds with personality modifiers.
- anti-thrash commitment windows.
- reserve release triggers and reserve-area validity.
- command-node task assignment across shallow and deep command trees.
- illegal-idle classification.
- executor write eligibility gates.
- W&L/player-subordinate NoWrite decisions.
- path-interrupted recovery decisions.

Runtime smoke success for the observed failure class:

```text
No repeated non-reserve command nodes in:
  MarchColumn
  pathInterrupted=True
  paths=0
  activeMove=False
  no valid ledger/task reason
```

Accepted exceptions:

- valid reserve wait.
- holding assigned objective/choke/fallback line.
- forming with active order/courier.
- waiting for support/commitment window.
- pinned/routed.
- player-subordinate protected.

DLL-affecting completion still requires the repo gate: console harness, build,
deploy, and SHA-256 match between `dist/WhiskeyRealism.dll` and the deployed
BepInEx plugin before asking for in-game smoke.

## Open Anchor Work

Before implementing objective classification beyond generic objective positions,
verify and document exact vanilla sources for:

- battle monuments / victory locations / objective scene objects.
- bridges, fords, roads, and rail/road junction objects if exposed.
- terrain type IDs beyond the water/deployment samples already used by #58/#60.
- blocked crossings / route graph access, if any.
- whether objective-chain lists can be safely read as the initial objective set
  without mutating vanilla ownership.

If any of those are not exposed cleanly, Whiskey should synthesize conservative
objectives from known unit contact, objective-chain target positions, terrain
sampling, and movement corridor observations, and label the source explicitly in
telemetry.

## Approval

Approved user direction on 2026-05-10:

- build the full system, not a symptom-only first slice.
- include a ledger.
- the army must scout, see enemy positions, assign corps/divisions/brigades to
  objectives, decide between splitting forces or sequential attacks, keep
  reserves, and commit without changing mid-push.
- parallel attacks are allowed when there is a fair strength advantage and
  personality supports the risk.
- both sides should act dynamically and period-appropriately.
- the system should be on and working in active mode, with monitoring always
  part of the loop.
