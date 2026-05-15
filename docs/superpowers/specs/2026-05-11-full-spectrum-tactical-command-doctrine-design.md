# Full-Spectrum Tactical Command Doctrine Design

Status: implemented/hash-deployed design artifact; fresh Active smoke pending
before archive. Current runtime truth lives in
[`docs/tactical-operations-ledger.md`](../../tactical-operations-ledger.md),
[`docs/tactical-orchestrator.md`](../../tactical-orchestrator.md), and
[`docs/patch-catalog.md`](../../patch-catalog.md). Do not implement directly
from this spec or use it to infer missing work without checking shipped source.
Date: 2026-05-11

This umbrella spec defines the next tactical command-doctrine layer for Whiskey
Realism. It is intentionally broader than a single patch. Implementation must
proceed through Superpowers TDD plans with bounded slices, pure harness coverage,
runtime adapters, and focused smoke gates before release-closeout.

Source-of-truth order remains shipped code, `docs/patch-catalog.md`, living
tactical docs, per-patch specs, then this umbrella spec. If this document
disagrees with shipped code or `docs/tactical-operations-ledger.md`, the
implementation plan must resolve the drift before code changes.

## Problem

Vanilla Grand Tactician already has useful command mechanics:

- campaign offensive movement builds packages in `AICampaign.CheckOffensiveMovements`;
- regular movement flows through `BattleUnits.SetWaypoint`;
- command formations can propagate movement through `BattleUnits.SetGroupFormation`;
- active battles use `AIBattle.UpdateAITasks`, objective chains, macro strategy,
  group stance, reserves, fallback, and formation logic.

Whiskey has already shipped a tactical orchestrator, command-node tree, direct
child roles, reserve/charge gates, deployment discipline, and #61 operations-ledger
posture execution. The remaining gap is doctrine translation:

- operation phase and commit state are too thin;
- command-node tasks are too generic;
- visible contact can fail to become sector/objective confidence;
- screen/skirmisher contact is not cleanly separated from formed-line commitment;
- support, reserve, fallback, artillery, charge, and stance consumers still make
  partial decisions instead of reading one doctrine order;
- campaign attack intent is not yet a durable battle-start objective/axis input.

The goal is not to replace vanilla movement. The goal is to decide what parent
command nodes should do, when, and why, then let vanilla hierarchy propagation
move attached troops wherever possible.

## Design Goal

Both AI sides should fight like Civil War armies with plans:

- carry campaign attack/defense intent into the battle ledger when available;
- scout and screen before large commitment when the enemy is uncertain;
- estimate local enemy strength from visible/recent/inferred evidence;
- choose single, sequential, parallel, fix-and-flank, defensive, delay/fallback,
  orderly-withdrawal, or counterstroke operations;
- assign division/brigade/corps-like command nodes to concrete objectives, roles,
  support lanes, reserve areas, and fallback lines;
- commit formed regiments when visible enemy line, odds, support, and mission
  justify it;
- keep reserves explicit and release them through doctrine triggers;
- stage fallback and relief instead of letting isolated commands fight to rout;
- preserve order delay, W&L player-subordinate boundaries, and vanilla movement
  ownership.

Governing invariant:

```text
Every AI command node must have a current purpose.
Idle is legal only when the doctrine ledger says why.
```

## Confirmed Vanilla Anchors

Anchors are verified against `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`
and current living docs. Line numbers are decompile coordinates.

| Area | Anchor | Confirmed behavior | Doctrine implication |
|---|---:|---|---|
| Campaign offensive packages | `AICampaign.CheckOffensiveMovements` `:14166` | Builds offensive packages, evaluates enemy area strength, chapter/aggression/weather/readiness, and commits movement through `MoveUnitTo`. | Campaign attack intent can seed battle objective/axis, but it is not enough for battle doctrine. |
| Campaign movement API | `AICampaign.MoveUnitTo` `:14479` | Calls `BattleUnits.SetWaypoint` when eligible or creates defensive moving orders when moving around enemies. | Whiskey should reuse the vanilla movement API rather than hand-moving subordinates. |
| Regular waypoint API | `BattleUnits.SetWaypoint(Regiment, ...)` `:91232` | Handles readiness, order delay, W&L control, path clearing, campaign/battle branches, and calls group formation for command nodes. | Primary movement writer for executor slices. |
| Group formation API | `BattleUnits.SetGroupFormation(Regiment, ...)` `:91822` | Returns for `unittyp <= 13`, builds hierarchy offsets, and writes paths for attached command/subordinate units. | Primary parent-command formation/movement propagation surface. |
| Hierarchy read | `BattleUnits.GetHierarchyTree(Regiment, ...)` `:92720` | Builds attached hierarchy used by group formation. | Command-node doctrine should target parent nodes and let hierarchy propagation work. |
| Tactical AI loop | `AIBattle.UpdateAITasks` `:5857` | Runs reserve assignment, objective movement, flank calculations, macro strategy, group stance, group formations, feud actions. | Whiskey consumers must deconflict with existing vanilla cadence. |
| Macro strategy | `AIBattle.CheckGlobalAIStrategy` `:6314` | Sets battle-level `macroai` from force balance, battle type, victory pressure, reinforcements, commander initiative, and strategy overrides. | Operation Director should advise/retarget existing macro consumer, not create a second macro writer. |
| Stance writer | `BattleUnits.ChangeStance` `:90772` | Applies group stance changes used by vanilla `AdjustGroupAIStance` and Whiskey #61. | Stance remains a consumer of doctrine, not the doctrine source. |
| Objective movement | `AIBattle.UpdateMovingTargets` `:6870` | Moves objective-chain center groups to current objective when eligible and sets `currentsetobjective`. | Objective-chain/current-objective anchors are valid battle objective inputs. |
| Current-order messaging | `AIBattle.CheckCurrentOrderUpdate` `:8233` | Hard-gates on `DLC_WL.dlc_scenarioactive` and creates W&L `GivenOrders` / career-panel messages. | Not a regular campaign movement anchor; only W&L messaging/current-order bridge. |
| Order delay | `Regiment.AddOrderCourierline` `:125009`, `Regiment.ProcessOrders` `:125173` | Models bugle/courier order delivery, secondary courier lines, order status, and formation orders. | Active courier/order state is valid waiting, not illegal idle. |

## Current Whiskey Mod Anchors

These are current shipped or main-branch mod surfaces the design must fit. They
are implementation anchors, not new vanilla claims.

| Area | Whiskey anchor | Current behavior | Design implication |
|---|---|---|---|
| Battle lifecycle | `TacticalBattleCoordinator` / `TacticalBattleOrchestrator` | Owns per-battle/per-side orchestrator ticks. | Authoritative doctrine writes belong in the orchestrator tick, not Harmony patches. |
| Army plan and command resolution | `ArmyOrchestrator` | Owns army plan, command tree state, direct-child intent fallback, and `ResolveCommandIntentForGroup(...)`. | New doctrine order resolution should extend this ownership rather than create a second battle brain. |
| Command tree | `CommandTreeRuntime`, `CommandTreeBuilder`, `CommandIntentResolver` | Builds generic command-node snapshots from vanilla hierarchy and resolves command intent by node id. | Doctrine must remain generic over GT's variable hierarchy; do not hard-code perfect corps/division/brigade classes. |
| Existing operations ledger | `TacticalOperationsLedgerRuntime` | Records current objectives, strategic battle intent, and operation shape; current implementation leaves phase/commit state thin. | This is the natural home for Operation Director state after pure models are added. |
| Vision/objective adapter | `TacticalVisionRuntimeAdapter` | Reads objective-chain/current-objective anchors and visible-enemy fallback records. | Confidence and formed-line/skirmisher distinction should extend this adapter through typed pure inputs. |
| Command operations | `CommandNodeOperationsRuntime`, `CommandNodeTaskPlanner` | Maps command intents to operational states and simple task names. | Replace vague role/task inference with concrete `CommandDoctrineOrder` records. |
| Posture decision model | `CommandPostureExecutor` | Pure decision model for no-write, formation, waypoint, reserve release, fallback, and recovery. | #61 should stay the bounded write executor but consume richer doctrine orders. |
| Active write surface | #61 `BattleCommandPostureExecutorPatch` | Runs after `AIBattle.AdjustGroupFormations`; writes through `ChangeStance`, `SetWaypoint`, and `SetGroupFormation` only when `Tactical Commander Mode = Active`. | Do not add a second broad movement writer. Expand #61 through pure executor inputs and strict gates. |
| Stance consumer | #45 `BattleGroupStancePatch` | Existing B5 stance scorer writer; not yet fully retargeted to command-node doctrine. | Retarget stance to consume `CommandDoctrineOrder`. |
| Charge consumer | #41 `BattleChargeGatePatch` | Owns charge initiation gate with W&L/player protection and command-role charge gating. | Keep #41 as sole charge gate owner; doctrine supplies permission/denial inputs. |
| Reserve consumers | #57 `BattleReserveDoctrinePatch`, #59 `BattleReserveCommitGatePatch` | Reserve-list bias and reserve-commit rollback/gating exist but are not full reserve-command doctrine. | Doctrine owns reserve areas, covered objectives, release triggers, and relief targets. |
| Fallback/artillery consumers | B8 fallback/withdrawal patches, B7 artillery patches | Existing doctrine surfaces exist but are not yet command-ledger driven. | Retarget them to operation and command-node doctrine orders after #61 smoke and pure tests. |

## Not Verified

- A clean typed battle-local bridge/ford/road/town/choke-point graph. Existing
  crossing/terrain signals are partial and must not drive typed POI scoring until
  a dedicated anchor review verifies the source.
- Reliable vanilla strongpoint-quality scoring beyond existing terrain/deployment
  samples, objective ownership, cover, fire, and line contact evidence.
- A dormant vanilla policy for contact-aware stale-order reassessment, reserve
  relief timing, or staged withdrawal. Those are Whiskey doctrine.
- Whether every battle/scenario exposes enough objective-chain data to avoid
  visible-enemy-line fallback. Runtime adapters must fail closed when anchors are
  missing.

## System Spine

The full command system sits above #61:

```text
Campaign Intent / Battle State
  -> Battlefield Picture
  -> Operation Director
  -> Objective Assignment Ledger
  -> Command-Node Doctrine Orders
  -> Patch Consumers / #61 Executor
  -> Vanilla SetWaypoint / SetGroupFormation / ChangeStance
```

Whiskey owns intent and tasking. Vanilla owns movement and formation machinery
wherever possible.

Authoritative doctrine state writes happen only during the orchestrator tick.
Harmony patches may read immutable snapshots, call pure decision functions, write
approved vanilla battle state when eligible, and emit bounded telemetry. Harmony
patches must not mutate the doctrine ledger directly.

## Core Doctrine Output

The missing bridge is a concrete command order:

```text
CommandDoctrineOrder
  commandNodeId
  parentNodeId
  echelonKind
  role
  task
  taskState
  objectiveId
  targetPoint
  assemblyPoint
  fallbackPoint
  supportOfNodeId
  coveredObjectives[]
  releaseTrigger
  confidence
  odds
  commitUntil
  softReviewAt
  allowedIdleReason
  urgency
```

This order replaces vague `role + task` inference at consumer time. #61 and the
other tactical consumers should receive concrete targets, phase, confidence,
support/fallback/reserve data, and eligibility.

## Battlefield Picture And Confidence

The battlefield picture is a per-side shared truth source. It does not issue
orders.

Inputs:

- objective-chain objectives;
- current-set-objective references;
- visible closest enemy;
- recent fire / received-fire evidence;
- formed-line contact vs skirmisher/detachment contact;
- friendly/enemy command-node position and strength;
- rout, flank threat, outflank, combat status, and morale/fatigue/ammo when
  available;
- path, order, active move, and courier state;
- strategic/campaign battle intent when available.

Core records:

```text
EnemyContactReport
  contactId
  location
  estimatedStrength
  minStrength
  maxStrength
  source:
    VisualLineContact
    VisualSkirmisherContact
    RecentFire
    ObjectivePressure
    InferredMovement
  currentlyVisible
  formedEnemy
  skirmisherOnly
  associatedObjectiveId
  associatedCommandNodeId
  confidence
  firstSeenAt
  lastSeenAt
  staleAfter
```

```text
ObjectiveIntel
  objectiveId
  source:
    CampaignIntent
    ObjectiveChain
    CurrentSetObjective
    VisibleEnemyLine
    FallbackLine
    StagingArea
  type:
    UnknownVanillaObjective
    EnemyLine
    FriendlyLine
    VictoryPoint
    Bridge
    Ford
    RoadJunction
    Town
    Ridge
    ChokePoint
  location
  value
  enemyStrength
  friendlyAssignedStrength
  status:
    Unknown
    Scouting
    WeaklyHeld
    StronglyHeld
    Contested
    Secured
    Lost
  confidence
  typeAnchorVerified
```

Confidence rules:

- visible formed enemy line gives high confidence immediately;
- visible skirmisher-only contact gives contact confidence but does not justify
  main-body commitment by itself;
- recent fire gives medium confidence and decays with time;
- objective-chain anchors give objective confidence but not enemy strength by
  themselves;
- current-set-objective anchors are valid fallback anchors, lower-confidence than
  objective-chain anchors;
- visible enemy-line centroid is allowed only as a generic fallback objective
  when map/current objective anchors are missing;
- unverified bridge/road/town/choke types remain generic until anchor review;
- if closest visible enemy exists inside a command node's sector, sector/objective
  enemy strength cannot remain zero.

Doctrine distinction:

```text
screen contact != enemy line exposed
```

Screen/skirmisher contact can produce Scout, Screen, Probe, or FixEnemy. Formed
enemy line contact with favorable odds can produce AttackObjective, SupportAttack,
or Counterstroke.

## Campaign-To-Battle Continuity

When a campaign operation creates or implies a battle objective, the battle
ledger should consume that as initial intent:

```text
StrategicBattleIntentSnapshot
  alliance
  campaignOperationId
  phaseId
  targetObjectiveId
  targetPoint
  targetAreaKey
  posture
  allowParallel
  allowProbeOnly
  allowReinforcementPackage
  priority
  confidence
```

Rules:

- campaign attack intent seeds the primary battle objective/axis;
- campaign defensive/guard/recover intent seeds defensive network, delay, or
  fallback posture;
- battle evidence can invalidate campaign intent only through explicit
  reassessment reasons;
- player-CIC and W&L subordinate protections remain hard boundaries;
- absence of campaign intent is allowed and falls back to battle-local objective
  and contact evidence.

## Operation Director

The Operation Director publishes one stable operation record per AI side.

Operation shapes:

```text
SingleMainEffort
SequentialObjectives
ParallelObjectives
FixAndFlank
DefensiveNetwork
DelayAndFallback
OrderlyWithdrawal
Counterstroke
```

Operation phases:

```text
Planning
Scouting
Forming
WaitingForSupport
Committed
Exploiting
Consolidating
SoftAbort
Fallback
Complete
```

Selection rules:

- if campaign intent supplies an objective/axis, use it as the initial primary
  objective unless battle evidence invalidates it;
- uncertain enemy starts Scouting or Forming, not instant attack;
- one strong objective plus one weak objective favors FixAndFlank or Sequential;
- multiple weak objectives with reserves can allow ParallelObjectives;
- outmatched defenders choose DefensiveNetwork or DelayAndFallback when ground is
  useful;
- overextended enemy attack plus ready support can allow Counterstroke;
- likely force collapse shifts to OrderlyWithdrawal or Fallback.

Parallel attack rules:

- require objective-specific strength advantage, not global advantage;
- require a reserve fraction and flank guard;
- aggressive commanders lower but do not remove confidence/reserve thresholds;
- cautious/methodical commanders require stronger confidence, support, and reserve;
- parallel attack creates distinct command-node/objective assignments, not a
  nearest-enemy free-for-all.

Commitment state:

```text
commitUntil
softReviewAt
hardAbortConditions
lastMeaningfulProgressAt
```

Review windows scale by phase:

- Scouting: short review;
- Forming: enough time for couriers and formation movement;
- Committed: longer no-thrash window, but support failure can trigger soft abort;
- Exploiting: short urgent review;
- Fallback: stable until safe line reached or blocked.

Abort tiers:

```text
Continue
SoftAbortReview
HardAbort
```

Soft abort may pause attack, shift support, convert main effort to fix, release
reserve, shorten review, redirect to weaker objective, or begin staged fallback.
Hard abort is for rout, collapse, catastrophic flank exposure, objective
impossibility, or no support/fallback remaining.

Era/personality behavior:

- 1861/amateur armies form slower and pause more often;
- 1862-63 armies execute more coherent main/support/fix behavior;
- 1864+ Union can sustain pressure and sequential attacks more aggressively;
- late CSA is more conservative with reserves and favors delay/counterstroke
  unless odds are strong.

## Objective Assignment Ledger

The Objective Assignment Ledger converts operation intent into node-specific
orders.

Inputs:

- command tree nodes and parent/child relationships;
- effective echelon;
- objective intel and contact confidence;
- local friendly/enemy strength;
- support distance/time;
- reserve availability;
- flank exposure;
- morale/fatigue/ammo/rout evidence when available;
- campaign intent target/axis;
- operation shape and phase.

Roles:

```text
MainEffort
SupportingAttack
FixingForce
ScreeningForce
Reserve
Defender
FallbackGuard
FlankGuard
Counterstroke
Reformer
```

Tasks:

```text
Scout
Screen
Probe
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
RelieveLine
FallBackToLine
Delay
Consolidate
RecoverStuckOrder
```

Echelon rules:

- army/corps-like nodes own operation shape, main effort, reserve policy, and
  full fallback/withdrawal;
- division-like nodes own sector mission;
- brigade-like nodes own concrete maneuver, formation, skirmisher deployment,
  support, relief, fallback, and local counterstroke;
- regiment-level behavior remains vanilla/local unless the regiment is a command
  node or is directly stuck/unsafe.

Doctrine rules:

- main effort goes to the best suitable node, not the first node;
- support attack gets a concrete flank/support lane or enemy-line segment;
- fixing force pressures/pins without becoming unsupported assault;
- screening force locates enemy and guards flanks, then falls back behind main
  line when pressured;
- reserve gets area, covered objectives, release triggers, and relief targets;
- fallback guard gets fallback line and rear-guard behavior;
- flank guard faces the biggest flank threat and protects the operation;
- reformer catches scattered, interrupted, march-column, or unready commands.

Main-body commitment requires:

- enemy line or objective sufficiently located;
- contact confidence high enough;
- odds favorable for the mission;
- support ready or explicitly waived by aggressive/desperate personality;
- no critical unresolved flank exposure;
- reserve policy satisfied.

This rule directly prevents the observed failure where brigades form but only
skirmishers attack. Once the ledger says visible enemy line, favorable odds, and
support/main readiness, formed command nodes must receive AttackObjective or
SupportAttack rather than passive HoldObjective.

## Consumer Retargeting

All tactical consumers should read the same `CommandDoctrineOrder`.

### Stance Consumer (#45)

`BattleGroupStancePatch` should become a doctrine consumer:

- Screen/Probe -> screen/cautious stance;
- HoldObjective/Defender/FallbackGuard -> defend stance;
- AttackObjective/SupportAttack/Counterstroke -> attack stance when commit gates
  pass;
- ReserveWait/Reformer -> no aggressive stance until release/ready;
- FallBackToLine/Delay -> defensive/fallback posture.

It must preserve player/W&L protection, vanilla retreat ownership, and the
skirmisher-only versus formed-line distinction.

### Posture Executor (#61)

#61 remains the primary write executor and consumes concrete targets:

- FormUp -> SetGroupFormation at assembly/facing;
- AdvanceToAssembly -> SetWaypoint to assembly area;
- AttackObjective -> SetGroupFormation + SetWaypoint to approach point;
- SupportAttack -> SetWaypoint to support lane/flank point;
- FixEnemy -> face/hold or bounded pressure move, not unsupported assault;
- Screen/Probe -> bounded scout/screen target and fallback behind main line;
- ReserveWait -> reserve area;
- ReleaseReserve/RelieveLine -> relief/support point;
- FallBackToLine -> fallback point and rear-guard timing;
- RecoverStuckOrder -> recovery path.

Executor writes should target parent command nodes where vanilla supports it.
Subordinate movement should normally come from vanilla hierarchy propagation.

### Reserve Consumers (#57/#59)

Reserve assignment becomes doctrine-owned:

```text
ReserveAssignment
  nodeId
  reserveArea
  coveredObjectives[]
  releaseTriggers[]
  minimumHoldUntil
  reliefTargets[]
```

Release triggers include:

- main effort stalled but viable;
- friendly line weakened;
- enemy flank attack detected;
- weak point created;
- objective close to capture;
- fallback line needs rear guard;
- parent operation enters SoftAbort or Exploiting.

#59 should continue blocking premature vanilla reserve movement for true reserves.
Released reserves become eligible. #57 reserve-list bias ranks by doctrine
release priority.

### Fallback / Withdrawal Consumer (B8)

Fallback is staged:

- LocalFallback;
- RelieveAndFallback;
- DelayAndFallback;
- OrderlyWithdrawal.

Fallback distinguishes:

- bad odds but coherent command -> fallback to line;
- flank emergency -> face/guard or short fallback;
- routed/collapsed command -> vanilla retreat/rout behavior;
- unsupported exposed command -> fallback and request support.

### Artillery Consumer (B7)

Artillery supports operation shape:

- bombard strongpoint before assault;
- counterbattery when threatened or enemy artillery dominates;
- support main effort target;
- avoid displacement during critical support unless threatened;
- cover fallback line or reserve release when possible.

### Charge Gate (#41)

Charge requires doctrine permission:

Allowed only for AttackObjective, SupportAttack, or Counterstroke when local odds,
morale, target weakness/disruption, support readiness, and objective pressure
support it.

Denied for reserve, fallback, screen, fix, defender, reformer, skirmisher-only
contact, unsuppressed strongpoint, unresolved flank, stale order, or failed
support.

## Anti-Thrash And Order Friction

Order friction is doctrine input, not an annoyance to bypass:

- active courier/order queue is valid waiting;
- no repeated replacement of undelivered orders unless the old order is dangerous
  or impossible;
- delayed orders can become stale if contact changes materially;
- high-initiative commanders reduce friction thresholds but do not make orders
  instant;
- support/fallback/release transitions must account for time-to-form and
  time-to-arrive.

Commitment prevents tick-by-tick replan. Soft abort prevents suicidal lock-in.

## TDD Harness Map

Every implementation slice must start with pure tests before runtime adapters or
Harmony patch changes.

### Battlefield Picture

Tests:

- visible formed enemy raises contact confidence;
- skirmisher-only contact does not permit main-body attack;
- recent fire decays;
- closest visible enemy cannot leave sector enemy strength at zero;
- objective-chain anchor outranks synthetic enemy-line fallback;
- unverified POI types do not drive typed bridge/road/choke scoring.

### Operation Director

Tests:

- campaign attack intent seeds primary objective;
- weak objective plus strong objective selects FixAndFlank or sequential;
- two weak objectives with reserve advantage allow ParallelObjectives;
- cautious personality blocks parallel attack without stronger odds;
- aggressive personality lowers but does not remove thresholds;
- poor support triggers WaitingForSupport or SoftAbort;
- committed operation does not thrash before review window;
- catastrophic collapse triggers HardAbort.

### Objective Assignment Ledger

Tests:

- main effort goes to best suitable command node;
- support node gets adjacent support lane;
- outmatched isolated node becomes fallback, not fix/main;
- visible enemy line with favorable odds creates attack/support task;
- skirmisher-only contact creates screen/probe/fix, not formed assault;
- reserve assignment includes covered objective and release trigger;
- fallback guard gets fallback point;
- reformer assigned for scattered/interrupted/march-column command.

### Command Doctrine

Tests:

- Screen -> FormUp -> AttackObjective transition after confidence/support;
- support failure holds or soft-aborts main effort;
- flank threat converts local task to GuardFlank without army-wide replan;
- reserve release produces RelieveLine when line is battered;
- fallback sequence keeps rear guard while main body withdraws.

### Executor Decisions

Tests:

- every movement task has a concrete target;
- FixEnemy does not become unsupported assault;
- SupportAttack has support lane/point;
- ReserveWait moves to reserve area but does not release early;
- stale/interrupted order can recover;
- close engaged local reform does not create fresh march-column path;
- player/W&L protected units are no-write.

### Consumer Retargeting

Tests:

- #45 maps doctrine tasks to stance;
- #41 denies charge for screen/fix/reserve/fallback;
- #57/#59 hold unreleased reserves and allow released reserves;
- B8 fallback consumes fallback doctrine;
- B7 artillery consumes operation support target.

## Runtime Adapter Contract

Runtime adapters are excluded from pure tests except through typed snapshots.
Each adapter must publish a pure input shape:

| Pure input | Runtime adapter |
|---|---|
| `BattlefieldPictureInput` | `TacticalVisionRuntimeAdapter` / new picture adapter |
| `OperationDirectorInput` | `TacticalOperationsLedgerRuntime` |
| `CommandTreePlanningSnapshot` | `CommandTreeRuntime` |
| `CommandReadinessSnapshot` | new command-readiness adapter |
| `DoctrineOrderInput` | new command-doctrine runtime |
| `ExecutorPhysicalSnapshot` | #61 patch-local physical-state adapter |

No implementation plan may add a runtime-only decision without a corresponding
pure input shape or a documented reason the behavior is smoke-only telemetry.

## Implementation Slice Map

This spec is an umbrella. Do not implement it directly as one change.

1. Slice 0: typed pure models and harness inputs.
2. Slice 1: battlefield picture confidence.
3. Slice 2: operation director and commit windows.
4. Slice 3: objective assignment and command-node doctrine.
5. Slice 4: #61 concrete order executor expansion.
6. Slice 5: #45 stance and #41 charge retargeting.
7. Slice 6: reserve, fallback, and artillery consumers.
8. Slice 7: runtime smoke, docs, archive, and release closeout.

Each slice needs a separate Superpowers implementation plan under
`docs/superpowers/plans/` with patch surfaces, tests, smoke expectations, and
rollback/defer boundaries.

## Active Smoke Gates

Fresh battle smoke must prove:

- `[TacticalOpsLedger]` reports operation shape and phase;
- `[TacticalCommandAssignment]` reports objective, role, task, state, target, and
  allowed idle reason;
- `[TacticalCommandPosture]` shows bounded writes with reason and target;
- `[TacticalPostureSummary]` shows illegal idle falling or explainably bounded;
- visible line contact produces nonzero sector/objective enemy estimate;
- main/support formed commands attack when odds and confidence say they should;
- skirmisher-only contact does not force main-body charge;
- reserves hold until release, then move when release trigger fires;
- fallback has resolvable target or explicit no-write reason;
- no player-side or W&L player-subordinate retasking;
- no repeated `Exception`, `ERROR`, `missing-anchor`, Harmony failure, or #61
  failure marker.

Smoke failure should use `Tactical Commander Mode = MonitorOnly` for diagnostics
or `Off` for rollback, depending on whether write behavior is implicated.

## Config Contract

The release/default user direction for this command system remains:

```ini
[Tactical Orchestrator]
Tactical Commander Mode = Active
Enable Tactical Battle Orchestrator = true
Enable Tactical Orchestrator Army = true
Enable Tactical Orchestrator Intent Inference = true
```

Existing config-file precedence remains important: once the BepInEx config file
exists, C# defaults do not override it. Implementation plans that add or migrate
keys must state precedence and migration behavior explicitly.

## Documentation Contract

When slices ship, update living docs:

- `docs/handoff.md`;
- `docs/tactical-orchestrator.md`;
- `docs/tactical-operations-ledger.md`;
- `docs/patch-catalog.md`;
- `MEMORY.md` when durable project routing changes are needed.

Archive point-in-time specs/plans only after matching runtime smoke passes and
the living docs carry the current behavior.
