# Scourge Campaign Advance-Guard Sandbox Design

Status: design/spec only. Not implemented in runtime yet.
Date: 2026-05-14

This spec converts the campaign-map ideas in Scourge of War Remastered into
Whiskey Realism terms. It is not a request to patch the Scourge engine. It uses
Scourge as a doctrine reference and Grand Tactician vanilla anchors as the
runtime boundary.

## Problem

Whiskey's strategic layer can score objectives, build coordinated packages,
publish formation directives, and carry strategic battle intent into the
tactical ledger. It does not yet split an army column into campaign-map
advance guards, pickets, or supply-base security before contact.

Scourge has a concrete campaign sandbox loop that does this: split, probe,
guard, picket, attack/defend a supply base, and reattach when contact changes.
Whiskey needs the same behavior expressed through existing strategic ledgers and
vanilla Grand Tactician movement APIs.

## Scourge Anchors

Verified from:

`/mnt/c/Program Files (x86)/Steam/steamapps/common/Scourge Of War - Remastered/sdk/SowCampAI/campai.cpp`

| Anchor | Scourge behavior | Whiskey conversion |
|---|---|---|
| `CCampThink`, around `:1247` | Main campaign AI think function. | Spec owner for the sandbox state machine analog. |
| `:1252-1271` | Determines attacker/defender/supply-zone behavior from commander and campaign type. | Input to Whiskey strategic posture: attack, defend, supply-security, raider response. |
| `:1289-1318` | Initial split-up, direction selection, town target selection, and movement order. | Formation directive can split a column into main body plus advance guard. |
| `SplitUp` calls | Detaches subordinate bodies from the parent column. | Whiskey should publish child formation directives, not mutate hierarchy blindly. |
| `:1329-1338` | Enemy near causes merge/reattach. | Contact evidence must cancel detached screen behavior and rejoin before battle or retreat. |
| `:1375-1381` | Major-town defender can become a picket. | Defense package can assign local picket posture to important towns. |
| `:1384-1421` | Destroy/chase/town/advance-guard branches. | Objective metadata should choose raid, chase, town probe, or advance guard mission. |
| `CorpAdvanceGuard` | Sends a corps-level detached guard ahead of the main command. | New Whiskey mission type: campaign advance guard. |
| `CorpPicket` | Holds or patrols a picket around an objective or base. | New Whiskey mission type: campaign picket. |
| `:1426-1481` | Supply-base attack/defense, picket, and patrol branches. | Supply-base packages need attack, defend, picket, and patrol outputs. |
| `:1493-1508` | Enemy seen forces battle/retreat choice using `AttackEnemy`. | Whiskey must use contact evidence and strength ratio to battle, rejoin, or retreat. |
| `AttackEnemy` | Chooses battle vs withdrawal from relative divisions and arms modifiers. | Use current TheaterPressureView, ContactEvidenceLedger, and formation strength signals. |
| `DivisionNumbers` | Counts relative divisions with cavalry/artillery modifiers. | Use existing formation strength/readiness plus cavalry/artillery tags. |
| `MassiveUnderUnitMovOrder` | Pushes movement to detached subordinate unit. | Use `AICampaign.MoveUnitTo` or existing movement package APIs, never direct transform edits. |
| `AttackerSubDeployment` / `DefenderSubDeployment` | Subordinate deployment shape changes by posture. | Strategic package should carry posture-specific child roles. |
| `EnemyNear` / `GetEnemyNear` | Contact-sensitive state transition. | Use contact evidence plus campaign proximity instead of pure objective scoring. |

## Grand Tactician Anchors

Verified against `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

| Anchor | Vanilla behavior | Boundary |
|---|---|---|
| `AICampaign.CheckOffensiveMovements`, around `:14166` | Builds offensive movement packages from area strength, objectives, weather/readiness, aggression, and chapter. | Candidate hook/read source for campaign package intent. |
| `AICampaign.MoveUnitTo`, around `:14479` | Vanilla campaign movement writer, including around-enemy defensive movement. | Preferred writer for campaign sandbox movement. |
| `BattleUnits.SetWaypoint(Regiment, ...)`, around `:91232` | Handles waypoint/order-delay/readiness logic across campaign/battle. | Lower-level fallback writer only when campaign API is not available. |
| `AICampaign.CheckAICommanderReplacements` | Daily/strategic cadence anchor already used by Slice A. | Do not overload this with per-column movement; use ledgers published on cadence. |
| `SceneManagement.SaveAll` / `AICampaign.Load` | Existing save/load state path for Whiskey sidecar. | Detached sandbox missions must persist in `whiskeyrealism.json`. |
| `CampaignObjective.GetAvailableObjectives` | Objective availability source. | Advance guard/picket/supply-base targets must be objective-backed or explicitly synthetic. |

## Whiskey Anchors

| Surface | Current role | Needed extension |
|---|---|---|
| `FormationDirectiveLedger` | Publishes strategic movement/hold directives. | Add child-detachment directives: main body, advance guard, picket, supply patrol. |
| `CoordinatedOperationPackageLedger` | Builds multi-formation packages. | Add screen/picket/supply-base child slots and rejoin rules. |
| `OperationalProbeLedger` | Tracks probing behavior. | Reuse for advance guard contact reports. |
| `DefenseIntentLedger` | Defends ports/capitals/fronts against threats. | Add supply-base and town picket mission shape. |
| `ContactEvidenceLedger` | Records contact/no-contact/evidence changes. | Drives split, battle, reattach, and retreat transitions. |
| `StrategicBattleIntentSnapshot` | Carries campaign intent into battle. | Include whether battle began from advance guard, picket, or supply-base contact. |
| `whiskeyrealism.json` sidecar | Persists strategic state. | Persist active detached sandbox missions and parent/child relationships. |

## Proposed Model

Add a pure strategic model first:

```text
CampaignSandboxMission
  missionId
  parentFormationId
  childFormationId
  missionType: AdvanceGuard | Picket | SupplyBaseAttack | SupplyBaseDefense | SupplyPatrol | Rejoin | Retreat
  targetObjectiveId / targetPoint
  parentPoint
  contactState
  strengthRatio
  issuedDaySerial
  expiresDaySerial
  reason
```

Add a decision function:

```text
CampaignSandboxDoctrine.Decide(input) -> mission decision
```

Inputs:

- parent posture: attack, defend, recover, supply-security;
- commander aggression/caution/initiative;
- objective type: town, major town, depot/supply base, rail/river choke, enemy army;
- contact state: no contact, suspected, visible enemy, battle offered, enemy gone;
- friendly and enemy strength, cavalry share, artillery share, readiness, fatigue;
- current package cohesion and whether detached child can safely rejoin.

Outputs:

- detach advance guard ahead of main body;
- post picket around major town/supply base;
- patrol between main body and supply base;
- attack exposed supply base;
- reattach on close enemy;
- retreat rather than fight when `AttackEnemy` analog fails.

## State Machine

```text
Idle
  -> SplitAdvanceGuard
  -> AdvanceGuardMoving
  -> ContactReported
  -> RejoinForBattle | RetreatScreen | ContinueProbe

Idle
  -> EstablishPicket
  -> PicketHolding
  -> RejoinForBattle | MaintainPicket | WithdrawPicket

Idle
  -> SupplyBaseAttack | SupplyBaseDefense
  -> PatrolOrPicket
  -> RejoinForBattle | HoldSupplyBase | AbandonSupplyBase
```

Rules:

- Never detach if parent readiness is below campaign movement minimum.
- Never detach the last viable combat child.
- Never move a W&L/player-controlled command.
- Reattach when enemy contact is close enough to force a battle decision.
- Retreat if relative strength plus cavalry/artillery modifiers fail the
  `AttackEnemy` analog.
- Persist active missions and clear them on parent destroyed, child missing,
  objective unavailable, or battle transition.

## Implementation Slices

1. Pure model and tests:
   `CampaignSandboxMission`, `CampaignSandboxDoctrine`, strength/contact inputs,
   and state transitions.

2. Strategic ledger:
   persist active missions, expose current child mission by formation id, and
   emit bounded telemetry.

3. Package integration:
   coordinated operation packages can reserve one child as advance guard or
   supply picket when strength/readiness permits.

4. Movement bridge:
   apply mission outputs through `AICampaign.MoveUnitTo` where available; use
   `BattleUnits.SetWaypoint` only as a bounded fallback.

5. Battle handoff:
   if contact escalates into battle, include origin mission in
   `StrategicBattleIntentSnapshot` so tactical doctrine knows whether contact
   came from a screen, picket, supply-base attack, or main-body collision.

## Verification

Pure harness:

- advance guard detaches only with spare strength and no close contact;
- picket holds major town/supply target and does not consume final reserve;
- supply-base attack requires favorable local strength and objective confidence;
- close enemy forces rejoin or retreat;
- relative strength plus cavalry/artillery modifiers selects battle only when
  the `AttackEnemy` analog passes;
- missing objective or child clears the mission.

Runtime smoke:

- enable behind default-off config until proven;
- confirm no W&L/player-subordinate movement;
- confirm movement writes go through vanilla APIs;
- confirm sidecar round-trip after save/load;
- confirm battle handoff includes mission origin;
- scan `BepInEx/LogOutput.log` for bounded telemetry and no repeated patch
  exceptions.

## Non-Goals

- Do not directly edit Scourge code.
- Do not direct-transform campaign units.
- Do not create a second campaign objective picker.
- Do not detach arbitrary regiments without parent/child ledger state.
- Do not enable runtime movement by default before a focused campaign smoke.
