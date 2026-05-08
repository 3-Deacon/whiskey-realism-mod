# Scourge Tactical Adaptation Design

Status: active design supplement. This is a Slice B tactical design artifact, not an implementation plan.

Scope: adapt transferable tactical ideas observed in the local Scourge of War Remastered SDK and decompiled binaries into original Whiskey Realism doctrine for Grand Tactician: The Civil War. Scourge is comparative design evidence only. Grand Tactician's decompile and current Whiskey code own all implementation surfaces.

## Decision

Adopt four Scourge-informed concepts because Grand Tactician exposes usable tactical anchors:

- commander arbitration over local impulses;
- artillery support-screen and fallback awareness;
- destination discipline before movement writes;
- staged morale-pressure response and help-request telemetry.

Do not copy Scourge code, tables, constants, strings, assets, SDK structures, or binary output. Whiskey will implement original C# logic against verified Grand Tactician methods and fields.

This spec changes Slice B doctrine inputs. It does not authorize new runtime writes by itself. Existing plans remain the execution boundary:

- B6c owns commander intent/runtime application and local-reaction gating.
- B7 owns artillery bombardment/counterbattery/cancel-bombard decisions.
- B8 owns fallback, withdrawal, rear guard, and full-retreat staging.

## Source Boundary

Reviewed local Scourge install:

- `/mnt/c/Program Files (x86)/Steam/steamapps/common/Scourge Of War - Remastered/sdk/SowAiInf/`
- `/mnt/c/Program Files (x86)/Steam/steamapps/common/Scourge Of War - Remastered/sdk/SowCampAI/`
- `/tmp/sow-ghidra/SowAiInf.decompiled.c`
- `/tmp/sow-ghidra/SowCampAI.decompiled.c`

Scourge is native x64, not managed Unity. The SDK source is readable design evidence; Ghidra output only verified binary/source shape. Whiskey must not redistribute Scourge files or require Scourge to build/run.

## Grand Tactician Anchor Map

Current decompile: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

| Scourge idea | Scourge evidence | Grand Tactician anchor | Whiskey adaptation |
|---|---|---|---|
| Unit action asks commander stance/orders before local behavior. | `SowAiInf/offcmds.cpp:619` `GetInfCommand`; `SowAiInf/artyai.cpp:186-193` artillery asks leader for fallback distance. | `AIBattle.AdjustGroupAIStance()` at 4221 changes group stance; `AIBattle.MicroAICheckForCharges(Regiment,int)` at 4905 reads stance for charge; `Regiment.lastaistancechangetime` and `AIBattle.PerformAIActionDLCWL(...)` guard ownership. | B6c must keep local reactions subordinate to B6 intent. Charge denial remains a stance/permission decision, not a movement rewrite. |
| Artillery should fall back when enemy is close and no friendly screen is closer to the threat. | `SowAiInf/artyai.cpp:172-224` counts friendly inf/cav support closer than enemy before retreating guns. | `AIBattle.CheckArtyFallback(Regiment)` at 3499 already checks `unitrange.closestownunitnonrouted`, enemy proximity, `RegimentSetPath(...)`, and `SetMovementMode(4)`; `AIBattle.CheckAIBombardment(Regiment)` at 3869 owns bombardment; `AIBattle.CheckCounterBatteryFire(Regiment)` at 3827 owns counterbattery. | B7 adds a pure `TacticalSupportScreen` input. Unsupported close-threat guns may cancel/preserve fire in B7; movement remains B8 or vanilla fallback, not B7. |
| Avoid stacking multiple units/guns onto the same destination. | `SowAiInf/offcmds.cpp:183-247` rejects redeploy destinations already occupied by same-command units/guns. | `AIBattle.CheckForSimilarPositions(Vector3,Regiment)` at 8669 deconflicts move destinations; `AIBattle.CheckExpandingFrontline(Regiment)` around 4308 uses `unitrange.closestownunitdestination` and `closestenemyontargetdest`; `UnitRange.closestownunitdestination` at 109474 and `closestenemyontargetdest` at 109478 expose destination crowding evidence. | Add `TacticalDestinationDiscipline` as a pure scorer. B8 and any later reserve movement plan must call/replicate destination-discipline checks before emitting movement writes. |
| Morale drop plus danger should trigger fallback before rout. | `SowAiInf/unitai.cpp:929-1015` stages rout, retreat, fallback, and skirmisher recovery from morale danger. | `Regiment.morale`, `lastmoraleupdate`, and `battlestartmorale` fields at 111146-111148 and 110756; `Regiment` morale update around 128154-128232; `AIBattle.CheckLineFallbacks(Regiment)` at 5118 writes fallback paths/mode based on morale, enemy proximity, outflanked state, cover, and W&L guard; `AIBattle.MicroAICheckForRetreats(Regiment)` at 4817 writes retreat paths/mode. | B8 adds `TacticalMoralePressure`. Exact morale delta is not a vanilla field; Whiskey must snapshot morale over time if it wants true drop detection. Until then, use current morale, `battlestartmorale`, routed-neighbor, fire/contact, and flank evidence. |
| Units in trouble request help upward; higher commander chooses main effort and reserves. | `SowAiInf/offai.cpp:850-944` sends courier orders and help requests; `SowAiInf/offai.cpp:947-1069` selects best engaged subordinate, runs play, then checks reserves. | `AIBattle.MarchToSoundOfGuns(Regiment)` at 3663 moves idle groups toward engaged groups; `AIBattle.CheckUseOfReserves(Regiment)` at 6062 sends an unengaged unit to support an outflanked unit; `ObjectiveChain.reservegroups` at 2972 and line-group fields at 2992-2996 identify center/left/right/reserve. | B6 adds `TacticalHelpRequest` telemetry and playbook input. Do not synthesize courier orders. Reserve movement remains B6c/B8 gated and default-off. |
| Attacker concentrates while defender screens gaps and preserves a central mass. | `SowCampAI/campai.cpp:768-910` splits detachments differently for attacker/defender and avoids artillery-only detachments. | Strategic/tactical bridge exists through B6 `OperationPosture`, `ObjectiveChain` L/C/R fields, `reservegroups`, and B3 sector evidence. No direct Grand Tactician detachment API is verified for battle-level freeform split/merge in this spec. | Use as doctrine for future playbook role assignment only: attacker main effort/fix sectors; defender screen/refuse/held center. No detachment patch from this spec. |
| Personality shifts retreat tolerance. | `SowAiInf/offai.cpp:275-307` adds officer personality into retreat percentage. | B6 already reads commander profile/initiative; GT retreat/fallback methods do not expose a single personality tolerance knob. | Use commander profile as a pure scoring modifier in B8. Do not patch vanilla retreat thresholds globally. |

## New Whiskey Models

### TacticalSupportScreen

Purpose: tell B7/B8 whether a vulnerable unit is covered by a friendly screen.

Confirmed GT inputs:

- vulnerable unit `unittyp`, `guns`, `isrouted`, `markedforrout`, `regimentpaths`;
- closest enemy via `Regiment.GetClosestEnemyUnit(...)` and `UnitRange.closestenemyunitfarreg`;
- closest non-routed friendly via `UnitRange.closestownunitnonrouted`;
- distances via `Tools.GetXZDistance(...)`;
- artillery-specific fallback evidence from `CheckArtyFallback(...)`.

Output:

- `Screened`: friendly non-artillery combat unit is closer to the threat than the protected unit and is not routed.
- `Unsupported`: enemy is inside danger range and no qualifying friendly screen exists.
- `Unknown`: runtime field access fails or no reliable closest-enemy evidence exists.

B7 may use `Unsupported` to cancel or preserve bombardment. B7 must not call `RegimentSetPath(...)` or `SetMovementMode(...)`; B8 or vanilla fallback owns movement.

### TacticalDestinationDiscipline

Purpose: prevent Whiskey movement slices from creating stacking, backtracking, or same-destination crowding.

Confirmed GT inputs:

- `AIBattle.CheckForSimilarPositions(...)` destination deconfliction;
- `UnitRange.closestownunitdestination`;
- `UnitRange.closestenemyontargetdest`;
- `Regiment.GetLastTransmittedPathPos(ignoreorderdelay:true)`;
- `Regiment.width`, `depth`, `lastsetwaypointposition`, and `lastsetwaypointrotation`;
- `BUG-TAC-010` path-risk boundary from active tactical plans.

Output:

- `ClearDestination`;
- `CrowdedDestination`;
- `EnemyOnDestination`;
- `PathRiskUnknown`.

Any B8 movement branch or later reserve-relief movement branch must run this scorer before writing `RegimentSetPath(...)`, `BattleUnits.SetWaypoint(...)`, `SetWithdrawal(...)`, or direct movement-mode changes.

### TacticalMoralePressure

Purpose: stage fallback/withdrawal from accumulated morale danger instead of snapping to retreat.

Confirmed GT inputs:

- current `Regiment.morale`;
- `Regiment.battlestartmorale`;
- `Regiment.lastmoraleupdate`;
- `Regiment.friendlyroutednear` and `enemyroutednear`;
- `Regiment.outflanked`, `ownonflank`, `covervalue`, `coverobject`;
- `UnitRange.closestenemyunitfarreg`, `closestenemyunitfardistance`, and `retreatangle`;
- `receivedfire` evidence where already available in vanilla nearby logic;
- `CheckLineFallbacks(...)` and `MicroAICheckForRetreats(...)` side effects.

Not confirmed: a vanilla previous-morale field comparable to Scourge `PrevMor()`. `lastmoraleupdate` is a timestamp, not prior morale. True morale-delta scoring requires a Whiskey snapshot ledger keyed by unit identity.

Output:

- `Stable`;
- `UnderPressure`;
- `FallbackCandidate`;
- `WithdrawalCandidate`;
- `CollapseCandidate`.

B8 owns all runtime writes from this model.

### TacticalHelpRequest

Purpose: capture “this sector needs help” without creating courier/order writes.

Confirmed GT inputs:

- `AIBattle.CheckUseOfReserves(...)` outflanked-unit support logic;
- `AIBattle.MarchToSoundOfGuns(...)` engaged-group help logic;
- `ObjectiveChain.linegroup_centerunit`, `linegroup_leftunits`, `linegroup_rightunits`, `reservegroups`;
- B3 contact/sector odds and B6 playbook role.

Output:

- `RequestReserveScreen`;
- `RequestLineRelief`;
- `RequestArtillerySupport`;
- `RequestMainEffortShift`;
- `NoRequest`.

This is telemetry/playbook input first. Any reserve movement implementation remains default-off and must preserve W&L ownership gates.

## Slice Integration

### B6c

Use Scourge only to reinforce B6's existing rule: local action is subordinate to commander intent.

Required B6c spec/plan updates before implementation:

- classify local reactions against B6 playbook role and `TacticalHelpRequest`;
- treat `TacticalDestinationDiscipline` as a blocker for any reserve or local movement branch;
- keep charge permission on the existing B1/#41 charge gate surface;
- do not synthesize courier orders or call movement APIs from B6c unless a named plan task quotes the exact GT surface and rollback.

### B7

Extend B7 artillery doctrine inputs:

- add `TacticalSupportScreen` result;
- add enemy artillery visibility from `UnitRange.enemyinfirerangereg`;
- add current bombardment/counterbattery state from `combatbehaviorordered`;
- add ammo ratio from `Tools.SumUp(ammo) / 3f`;
- add W&L ownership-safety gate.

B7 decisions remain:

- `PreserveFire`;
- `SuppressStrongpoint`;
- `CounterBattery`;
- `CancelBombard`;
- `DefensiveFallback` telemetry only.

Important correction: Grand Tactician already has `CheckArtyFallback(...)`. B7 should not duplicate it. B7 may only influence bombard/counterbattery/cancel decisions at `CheckAIBombardment(...)` / `CheckCounterBatteryFire(...)`; artillery movement belongs to vanilla or B8.

### B8

Extend B8 staged withdrawal doctrine:

- derive fallback pressure from `TacticalMoralePressure`;
- use `TacticalSupportScreen` to distinguish covered withdrawal from exposed flight;
- use `TacticalDestinationDiscipline` before any selected withdrawal or waypoint branch;
- use commander profile as a tolerance modifier, not as a global vanilla threshold patch.

B8 must keep the existing plan boundary: no artillery APIs, no reserve-list mutation, no full-retreat timer except `FullRetreat`.

### Later Strategic/Tactical Bridge

Use Scourge campaign detachment only as high-level doctrine:

- attacker: main effort plus fix/screen sectors;
- defender: screen gaps, refuse threatened flank, preserve central mass;
- artillery-only formations should not be detached as screens.

No detachment or split/merge runtime patch is specified here.

## Non-Goals

This spec does not:

- port Scourge code or data;
- create any dependency on Scourge files;
- patch `RegimentSetPath(...)`, `BattleUnits.SetWaypoint(...)`, or order queues directly;
- bypass `PerformAIActionDLCWL(...)` or player-subordinate ownership gates;
- make B7/B8 default-on;
- add Napoleonic square, limber-state, or Scourge-only mechanics that GT does not expose cleanly;
- change vanilla morale thresholds globally;
- authorize reserve-list mutation beyond existing B6c/B8 plan gates.

## Verification Expectations

Before implementation planning, re-run anchor checks:

```bash
rg -n "private unsafe void CheckArtyFallback\\(|private void CheckCounterBatteryFire\\(|private void CheckAIBombardment\\(|private unsafe void CheckLineFallbacks\\(|private unsafe void MicroAICheckForRetreats\\(|private unsafe void MarchToSoundOfGuns\\(|private unsafe void CheckUseOfReserves\\(|public unsafe Vector3 CheckForSimilarPositions\\(" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
rg -n "public class UnitRange|closestownunitdestination|closestenemyontargetdest|closestenemyunitfardistance|closestownunitnonrouted|retreatangle|public float morale|lastmoraleupdate|battlestartmorale" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
```

Pure harness coverage expected when these concepts are implemented:

- support screen detects friendly cover between guns and close enemy;
- unsupported guns cancel/preserve bombardment but do not move in B7;
- destination discipline rejects crowded/enemy-held destinations;
- morale pressure distinguishes fallback candidate from collapse candidate;
- help request is emitted as telemetry without movement writes;
- W&L player-subordinate safety blocks all writes.

Runtime smoke remains the per-slice gate from B6c/B7/B8 plans: default-off config, bounded telemetry, no repeated exceptions, no player-subordinate retasking, and deployed DLL hash match before user smoke.

## Not Verified

- Exact prior-morale storage in vanilla was not found. `lastmoraleupdate` is a timestamp; use a Whiskey snapshot if true morale-delta behavior is required.
- Strongpoint detection quality is still owned by B7 and requires a runtime check against GT terrain/fort/cover fields before runtime writes.
- Safe conversion of vanilla reserve help into order-delay-preserving movement is not verified here. Treat reserve movement as a separate default-off plan task.
- No Grand Tactician equivalent for Scourge courier order synthesis was verified. Do not create courier-like orders from this spec.
