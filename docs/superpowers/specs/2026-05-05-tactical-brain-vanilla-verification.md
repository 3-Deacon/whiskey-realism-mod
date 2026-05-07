# Tactical Brain Vanilla Verification

Status: reopened verification reference for `2026-05-05-tactical-brain-design.md`. Keep this as vanilla/runtime evidence; implement through slice plans, not directly from this file.
Scope: vanilla code confirmation only. This document distinguishes confirmed vanilla surfaces from Whiskey doctrine that still has to be implemented.

Primary source: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

## Status Legend

- `CONFIRMED SURFACE`: vanilla exposes the method, data, or behavior needed by the spec.
- `PARTIAL`: vanilla exposes part of the surface, but the spec's desired doctrine is not already implemented.
- `WHISKEY DOCTRINE`: the idea is a new Whiskey tactical decision layer, not an existing vanilla behavior.
- `NOT FOUND`: the checked vanilla anchors do not contain the claimed behavior.

## Summary

No major Slice B requirement is blocked by missing vanilla data. Vanilla already exposes battle macro stance, group stance, charge, reserve, skirmisher, bombardment, flank, fallback, retreat, terrain, fog-of-war, unit-condition, hierarchy, courier/order-delay, and reinforcement surfaces.

The important boundary is that vanilla does not already contain the historical tactical doctrine requested by the spec. Sector missions, local-superiority doctrine, scout-before-commitment, strongpoint avoidance, reserve relief timing, covered staged withdrawal, and command-tier intent are Whiskey additions that must be built on the confirmed surfaces.

Runtime addendum from focused 2026-05-07 W&L battle smoke: B0/B2/#35 telemetry confirmed live command/order surfaces with `[TacticalCommand]`, `[TacticalOrder]`, `[TacticalPlayerOrder]`, `[TacticalCourierQueue]`, and `[TacticalCurrentOrder]` markers. The same smoke confirmed objective-chain player-subordinate exposure through eleven `[TacticalObjectiveMove]` `risk=True` lines, but did not prove path/position mutation. It did not observe `[TacticalWaypointDrift]`, `[TacticalReserveMove]`, `[Patch:TacticalFallbackRetreatNullGuard]`, `[TacticalChargeGuard]`, or `[TacticalFeudGuard]`.

Spec corrections that matter before implementation:

- `macroai = -1 dynamic` is a real vanilla battle-level macro state. B0 telemetry and the macro scorer must handle it explicitly.
- Battle-level `macroai` and group-level `ai_stance` are separate ladders. `ai_stance == 4` is group charge, not battle macro assault.
- `AIBattle.MicroAICheckForCharges(...)` does not itself call `PerformAIActionDLCWL`; the W&L guard must cover this charge method explicitly.
- `MicroAICheckForCharges(...)` also cancels charge movement and writes `lastfeudactiontime`; a guard patch must preserve those side effects.
- Vanilla can detect delivered versus undelivered order state, but no contact-aware "stale delayed order" reassessment was found. That stale-order policy is new Whiskey doctrine.

## Verification Matrix

| Spec area | Status | Vanilla anchors | Finding |
|---|---:|---|---|
| Tactical battle plan | `PARTIAL` | `AIBattle.CheckGlobalAIStrategy()` 6314-6455; `AIBattle.AdjustGroupAIStance()` 4221; `BattleUnits.sideinformation` reinforcement/force-balance fields 78524-78568, 83504-83572, 84614-84832 | Vanilla has macro stance, group stance, battle type, force balance, objective/victory pressure, losses, and reinforcement inputs. It does not have `TacticalBattlePlan` or the requested plan taxonomy. |
| Command tiers | `CONFIRMED SURFACE` | `BattleUnits.GetHierarchyTree(...)` 92714-92749; `Regiment.GetAttachedUnitsReg(...)` 119854-119903; fields `allattachedunits`, `parentregiment`, `unitlinkedto` 110988-111136 | Vanilla has a real hierarchy and attached-unit model that can represent army/corps, division, brigade, and regiment command relationships. |
| Command propagation | `CONFIRMED SURFACE` | `BattleUnits.PerformActionForGroupForAllUnitsOnLevel(...)` 92563-92628; `Regiment.ProcessOrders()` 125173-125431 | Vanilla can walk hierarchy levels and propagate delivered parent orders to attached subordinate units. |
| Courier/order delay | `CONFIRMED SURFACE` | `Regiment.AddToOrderQueue(...)` 124917-124974; `Regiment.AddOrderCourierline(...)` 125009-125170; `Regiment.ProcessOrders()` 125173-125431 | Vanilla queues orders, applies processing time, uses bugle delivery in range, creates couriers outside range, and waits for courier/queue completion before applying orders. |
| Transmitted versus intended orders | `CONFIRMED SURFACE` | `Regiment.GetLastTransmittedPathPos(...)` 127552; `Regiment.GetLastTransmittedPath(...)` 127591-127623; `Regiment.SetOrderStatus(...)` 125484; `LaunchGoCommand(...)` 125510; `OrderTimedMovement(...)` 125524 | Vanilla distinguishes latest intended paths from transmitted path state when `GameVars.useorderdelays` is true. |
| Contact-aware stale-order policy | `NOT FOUND` as vanilla behavior | Delivery/failure state in `ProcessOrders()` 125214-125329; interrupted-path references 113854-113895 | Vanilla tracks undelivered, delivered, failed, and interrupted orders, but no checked order-delay block reassesses a delayed order against changed enemy contact. This must be Whiskey doctrine. |
| Order-friction prefs | `PARTIAL` | Loads at 53336-53344; uses at 124966, 125032-125036, 125163, 133559-133562 | `processingtimegroupstandard`, `processingtimegrouproute`, `buglestandardrecognitiontime`, and `slowdowncourieroutsideradius` are loaded and used. `orderdelayforbugles` is declared and loaded but never read in the current decompile. |
| Commander range / bugle range | `PARTIAL` | Battle init 112993-113008; campaign init 113017-113036; campaign recalculation 125827-125855 | Battle units initialize from `GamePrefs.standardbuglerange` / `commanderstandardrange[unittyp]`. The readiness/initiative recalculation exists under the campaign path, not as a universal battlefield recalculation. |
| Forced order delays in Whiskey | `CONFIRMED SURFACE` | `RealismCheckboxesLockPatch.cs` lines 13, 68, 76 | Whiskey already locks `OrderDelaysCB` / `GameVars.useorderdelays = true` when realism settings are enforced. |
| Fog of war and scouting | `CONFIRMED SURFACE` | `FogOfWar` 100570; `UnitIsHidden(...)` 101179; `GetIntelligenceOnPos(...)` 101231; `PositionIsWithinFog(...)` 101246; `Regiment.CreateFOWObjects()` 116505; `AssignUnitToFOW()` 116520; `GetSpottedEnemyPosition()` 116536; `CheckEnemyContact()` 117140; `CheckScoutMovingTargets()` 124616 | Vanilla has FOW objects, hidden-unit checks, intelligence values, spotted-position memory, enemy-contact checks, and scout-moving-target logic. |
| Enemy strength and contact estimates | `CONFIRMED SURFACE` | `Regiment.UpdateUnitRangeFast(...)` 122545-122940; `Regiment.UnitRange` fields 109446-109550; group visible fields 110896-110902 | Vanilla populates visible enemy lists, fire-range enemy lists, closest enemy, own support in range, enemy strength by defensive angle slice, retreat angle, and flank-strength data while respecting fog-of-war visibility. |
| Scout-before-commit doctrine | `WHISKEY DOCTRINE` | Same FOW/contact anchors plus `AIBattle.CheckSkirmishing(...)` 5343, called at 5599 | Vanilla can deploy/reattach skirmishers and track contact, but the spec's "no reliable contact means probe/hold instead of all-line assault" policy is a new Whiskey scorer. |
| Skirmisher and screening mechanics | `CONFIRMED SURFACE` | `AIBattle.CheckSkirmishing(...)` 5343-5400; `GamePrefs.aiminmoraletodeployskirmishers` 52606, load 54806; `GamePrefs.aimoraletriggertoreattachskirmishers` 52608, load 54808; `Regiment.CheckNewTargetsOfSkirmishers()` 128847 | Vanilla has skirmisher detach/reattach behavior, morale gates, target adjustment, and related battleprefs. |
| Sector doctrine | `PARTIAL` | `Regiment.UpdateUnitRangeFast(...)` enemy angle slices 122545-122940; `CalculateFlankData()` 6468; `CheckIfFlanksAreAnchored()` 6599; `AdjustGroupAIStance()` 4221 | Vanilla has angle slices, objective chain flank/center groups, and group stance. It does not assign named missions such as `Screen`, `Fix`, `Refuse`, `Relieve`, or `RearGuard`. |
| Odds doctrine | `PARTIAL` | `BattleUnits.sideinformation.forcebalance` used in `CheckGlobalAIStrategy()` 6396-6416; `AIBattle.GetGroupStrength(...)` 6025-6053; `UpdateUnitRangeFast(...)` enemy/own strength in range 122545-122940 | Vanilla computes global force balance and group strength with morale/experience/enemy-surrounding options. Local-superiority doctrine, economy-of-force sectors, and inferior-force preservation posture are new Whiskey scoring. |
| Macro stance | `CONFIRMED SURFACE` | `AIBattle.CheckGlobalAIStrategy()` 6314-6455; debug/UI stance mapping 188941-188942; `GamePrefs.forcebalancetrigger*` loads 54672-54690 | Vanilla battle-level `macroai` exists. `macroai + 1` maps to dynamic/assault/attack/defend/retreat, so values are `-1 dynamic`, `0 assault`, `1 attack`, `2 defend`, `3 retreat`. Transitions use battle type, force balance, losses, reinforcements, victory/end-battle pressure, flanking factor, and commander initiative. |
| Group stance ladder | `PARTIAL` | `AIBattle.AdjustGroupAIStance()` 4221-4275; group stance names 65061 | Vanilla group-level `ai_stance` is a different ladder from `macroai`. It is strength-centered but not literally strength-only: it also uses macro stance, battle type, OOB symbol id, force-balance screening flag, DLC gates, and stance-change timing. Sector-aware stance doctrine is new. |
| Charge behavior | `CONFIRMED SURFACE` | `AIBattle.MicroAICheckForCharges(...)` 4905-4929 | Vanilla sets charge movement when group `ai_stance == 4`, cancels charge when movement mode is charge and the group is no longer in charge stance, gates both branches on feud state, and writes `lastfeudactiontime` on both initiate and cancel. This is the correct patch surface for charge gating, but a guard must not break cancellation. |
| W&L charge gate | `NOT FOUND` in charge method | `MicroAICheckForCharges(...)` 4905-4929; `PerformAIActionDLCWL` definition 5101; full-method grep found existing calls at 2766, 3514, 3640, 3759, 3834, 3890, 4080, 4232, 4315, 4515, 4566, 4617, 4696, 4842, 4891, 4896, 5043, 5137, 5249, 5303, 5360, 5381, 5400, 5417, 5458, 6129, 6201 | The charge method itself does not call `PerformAIActionDLCWL`, while many other tactical surfaces already do. Patch authors must run `rg "PerformAIActionDLCWL"` before adding guards so they do not double-gate unrelated surfaces. |
| Feud auto-advance risk | `PARTIAL` | `AIBattle.CheckForFeudGroupActions()` 4931-4958 | Confirmed: the method moves formation-level feud groups toward closest enemy via delayed `SetWaypoint(... useorderdelay: true ...)` and does not call `PerformAIActionDLCWL`. Runtime equivalence to the reported "auto-charge" symptom remains an inference until smoke-tested; "auto-advance under feud" is the more precise vanilla description. |
| Reserves | `CONFIRMED SURFACE` | `CheckUseOfReserves(...)` 6062-6172; `LinkReservesToLineGroup()` 6642-6743; `AssignReserves()` 7017-7118 | Vanilla has reserve assignment, reserve linkage to line/center/flank/artillery/screening groups, and emergency use for outflanked/endangered units. |
| Reserve relief doctrine | `PARTIAL` | Reserve anchors above; `FindExchangeUnitForUnit(...)` around 5088 | Vanilla has reserve and unit-exchange surfaces, but the spec's "relieve battered brigades/divisions before rout" timing and anti-stacking discipline are Whiskey doctrine. |
| Flank security | `CONFIRMED SURFACE` | `GroupIsOutflanked()` 6175; `CheckIfOutflanked(...)` 6188; `CheckFlankMoves()` 6247; `CalculateFlankData()` 6468; `CheckIfFlanksAreAnchored()` 6599; `UpdateUnitRangeFast(...)` flank fields 122545-122940; fields `outflanked`, `ownonflank`, `firefromflank` 111488-111492 | Vanilla calculates flank risk, flank moves, anchored flanks, and per-regiment flank fields. Refuse/deny/exploit doctrine is new scoring on top. |
| Terrain and strong points | `CONFIRMED SURFACE` | `BattlefieldSetup` 24119; `ReadInTerrainSpecs()` 26550; `GetCurrentTerrainOnPos(...)` 26727; `SearchTerrainInRangePos(...)` 26823; `GetAngleTerrainPos(...)` 26884; `CheckTerrainPeak(...)` 27068; `CheckPathPointsForTerrain(...)` 27596; `CheckTerrainLine(...)` 27638, 27650; `Regiment` fields `currentterrain`, `covervalue`, `coverobject`, `fortinrange` 110610, 111306, 111404, 111408 | Vanilla exposes terrain type, cover, fort-in-range, terrain search, line checks, peak checks, and path terrain checks. Strongpoint/weakpoint classification is Whiskey doctrine. |
| Flank anchoring on terrain | `CONFIRMED SURFACE` | `CheckIfFlanksAreAnchored()` uses `CheckTerrainLine` for rivers at 6621/6625 and `CheckTerrainPeak` for hills at 6629/6634 | Vanilla already has some terrain-aware flank anchoring. Whiskey can extend this to sector missions and strongpoint avoidance. |
| Artillery bombardment | `CONFIRMED SURFACE` | `AIBattle.CheckAIBombardment(...)` 3869-3906; `UpdateMicroAI` calls `UnlimberArtilleryAIMicro` 5658-5661, `CheckArtyFallback` 5662-5665, `CheckCounterBatteryFire` 5670-5673, and `CheckAIBombardment` 5690-5692; `Regiment` fields `combatbehaviorordered`, `bombardrange`, `bombardposition`, `targetedenemyunit` 111262, 111810-111814 | Vanilla can order bombardment for artillery in attack/assault stances when path/interruption/contact/ammo gates pass, and has counterbattery, unlimber, and artillery fallback helpers. The full bombard-strongpoint-before-assault doctrine is new. |
| Reinforcements | `CONFIRMED SURFACE` for active battle state; `PARTIAL` for `Autocalc.CheckUnitArrivals` reachability | `Autocalc.CheckUnitArrivals()` 20878; `Regiment.GetArrivalTimeToBF(...)` 138862; `BattleUnits.sideinformation` fields `strengthtoarrive`, `corpstoarrive`, `reinforcementarrivalswithin24hrs` 78524-78568; updates 84614-84832; UI text 169871-169874 | Vanilla tracks active-battle force still marching to the battle, corps still to arrive, and reinforcement strength inside the AI retreat window. `Autocalc.CheckUnitArrivals()` marks top units arrived in the autocalc loop; verify its active-battle call path before targeting it directly. |
| Reinforcement-aware retreat | `PARTIAL` | `CheckGlobalAIStrategy()` retreat branch 6414-6416 | Vanilla suppresses one retreat mechanism when `reinforcementarrivalswithin24hrs > 0`, but does not implement the spec's projected-odds/arrival-window doctrine. |
| Withdrawal and full retreat | `CONFIRMED SURFACE` | `CheckGlobalAIStrategy()` 6314-6455; `CheckLineFallbacks(...)` 5118-5180; `MicroAICheckForRetreats(...)` 4817-4900; `TimePanel.SetRetreatTimer(...)` called at 6336-6349 and 6362-6373 | Vanilla has fallback, local retreat, macro retreat, and battle retreat timer surfaces. Staged rear-guard withdrawal is not already implemented as a coherent doctrine. |
| Anti-retreat-loop policy | `WHISKEY DOCTRINE` | Existing timers: `startoffallback`, `timetostartairetreattimer`, macro stance change timers 6336-6449 | Vanilla has some timers and gates. The spec's hysteresis/cooldown/stage progression policy must be implemented by Whiskey. |
| Loss, morale, ammo, fatigue | `CONFIRMED SURFACE` | Regiment fields: strength/losses/morale/ammo/fatigue/rout data at 110836-110904, 111146, 111494-111498; group UI/status uses at 219232-219285 and 220080-220137; `GetGroupStrength(...)` morale input 6025-6053 | Vanilla exposes own-unit and group condition data needed for condition-aware scoring. |
| Received fire and flank fire | `CONFIRMED SURFACE` | `Regiment.ReceivedFire` type 109371-109392; field `receivedfire` 111858; `firefromflank` 111492; checks around 4417, 4515, 4561, 5307-5316 | Vanilla records recent incoming fire, source unit, angle, distance, and casualties, and exposes flank-fire fields. |
| Commander personality | `PARTIAL` | Vanilla commander initiative used in `CheckGlobalAIStrategy()` 6396-6407 and stance timer 6438-6449; feud action chance uses initiative/political/volunteer modifiers 4938-4952; Whiskey `CIC.Effective(...)` in `src/WhiskeyRealism/Strategic/CIC.cs` 18 | Vanilla already lets initiative affect some battle decisions. Whiskey's broader aggression/caution/competence/casualty-tolerance mapping is not a vanilla tactical personality system. |
| Battleprefs tuning | `CONFIRMED SURFACE` | `GamePrefs.forcebalancetrigger*` loads 54672-54690; skirmisher prefs 54806-54812; order-delay prefs 53336-53344; many AI fallback/charge/flank/reserve prefs loaded in `GamePrefs` | Vanilla has data-side tuning hooks. They are global pressure knobs, not enough for scouting, sector doctrine, reserve relief, personality, or staged withdrawal. |

## Implementation Boundary

Implementation should start with `B0 Tactical Observer` because most desired behavior is new doctrine rather than existing vanilla policy. The observer should log the confirmed surfaces first:

- macro stance and group stance;
- current and transmitted order state;
- courier/bugle delivery mode;
- visible/recent/inferred contact;
- sector strength and flank state;
- terrain/cover/fortification signals;
- artillery ammo/behavior;
- reserve availability and committed state;
- reinforcement arrival window;
- retreat/fallback triggers.

Only after those observer logs match runtime expectations should behavior patches steer the vanilla methods.

## Corrections To Carry Into The Spec

- Replace "raw strength-only" wording for `AdjustGroupAIStance()` with "strength-centered"; vanilla uses additional gates.
- Treat `PerformAIActionDLCWL` as a guard to add around charge and feud behavior, not as something already present in `MicroAICheckForCharges(...)`.
- Treat stale delayed orders as new Whiskey doctrine. Vanilla order state makes it detectable, but no contact-aware stale-order behavior was found.
- Treat command range / bugle range modifier claims carefully. Battlefield initialization uses static prefs; the readiness/initiative recalculation was found on the campaign path.
- Do not list `orderdelayforbugles` as an active behavior hook unless a later implementation pass finds a real use site. Current full-decompile grep found only declaration/load.

## Not Verified

- Runtime reproduction of the W&L "auto-charge" symptom. Vanilla evidence supports an ungated auto-advance-toward-enemy path in `CheckForFeudGroupActions()`, but the visible player symptom needs smoke.
- Runtime proof that objective-chain exposure actually changes a player-subordinate attachment path or position. Exposure is confirmed; movement impact is not.
- Runtime proof of campaign-call `CheckCurrentOrderUpdate(... calledfromcampaign:true)` duplication, risky courier queue mismatch, delayed waypoint drift, reserve direct-path movement, or fallback/retreat NRE suppression. Focused smoke did not exercise those risk markers.
- Live-battle reachability of `Autocalc.CheckUnitArrivals()`. Active battle reinforcement state is confirmed through `BattleUnits.sideinformation`; the `Autocalc` method should not be the first patch target until its call path is proven.
- Final sector geometry quality. Vanilla objective-chain center/flank/reserve/artillery/screening structures are confirmed, but the Whiskey seven-sector projection still needs B0 telemetry before behavior changes.
- Historical doctrine tuning values. The vanilla surfaces are confirmed; actual thresholds for scouting, withdrawal, reserves, and strongpoint avoidance require runtime soak.
