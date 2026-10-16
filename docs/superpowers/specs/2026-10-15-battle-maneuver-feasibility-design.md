Produced-by: claude-opus-5-5 (high)

# BC14 — Maneuver Feasibility (design spec)

**Status:** draft for review (gate: user approval of this spec, then the plan section). **Slice:** the new BC14 of the battle-core plan (`docs/superpowers/plans/2026-10-15-battle-core.md`), after BC13 (commander opportunity). The integrator placed it as its own slice right after BC13. The plan's current BC14 (battle AI level of detail) becomes **BC15**, and the current BC15 (formations, slots and lanes) becomes **BC16**. This spec does not edit the plan; § 15 lists what the plan owes. **Branch:** `feat/battle-next-bc14` from `feat/battle-next` once BC13 has merged back into it. **Vanilla:** decompile build 14703111 (`/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`); game data `Config/battleprefs.txt`, `Config/unitprefs.txt` (shipped install, read only). Anchors are method names first. Decompile and pref line numbers are only there to help find the code. Scourge of War is not installed on this machine; its anchors come from `docs/scourge-of-war-ai-anchors.md`.

**User decisions (2026-10-15, verbatim; relayed by the integrator):**
- Tactics: "Civil War pratices a a guide not a script". History sets weights and thresholds (§ 3), never a scripted replay of a battle.
- Scenarios: "There could be lots of scenarios. Not just one. Maybe they out number the enemy. 35,000 to 25,000. They divide their forces and attack from two directiosn, if reasonable." The user's own example: a Perryville-like 35,000 against 35,000 where one side holds a crossing. The attacker should consider pinning that crossing and sending a division over another bridge or ford, instead of diving at the held crossing.
- Placement ("when ever yo uthink is best"): the integrator made it its own slice after BC13.

## Outline

0. Outline (this list)
1. Purpose, advantage, measurement
2. Ground truth: vanilla, Whiskey today, and what is missing
3. History as weights
4. (A) The feasibility evaluation: requirements
5. (B) How feasibility enters selection, and replanning: requirements
6. (C) The scenarios as emergent results (worked numbers)
7. (D) Interaction with BC11 envelopment and BC13 opportunity: requirements
8. Interfaces (pure types)
9. Roles, lifecycles, shared rows and conflict rules
10. Exceptions per step
11. Performance, configuration, telemetry
12. Fit criteria
13. (E) Tests, mutation plants, smoke items, coverage matrix (the frozen test plan)
14. Decisions and rejected alternatives
15. Amendments owed to the plan (renumbering BC14 to BC15 and BC15 to BC16, BC11 and BC13 touch points)
16. Not verified (runtime-only claims)
17. Open questions for the user
18. Out of scope

---

## 1. Purpose, advantage, measurement

**Purpose.** Today the army picks its plan without asking whether the plan can be carried out. `TacticalPlaybookCatalog.Select` scores personality, terrain kind, odds, an opposing-commander hint and jitter. It never asks four questions:
- Is there a route for each wing?
- Does a wing's route cross a river at a crossing the enemy holds?
- Will the wings arrive together, given 1860s couriers and march speeds?
- Can the enemy, on its interior lines, beat one wing before the other arrives?

BC11 then executes the chosen play. A wing whose route is cut by a river, or whose flank is anchored on high ground, is abandoned only after the plan is chosen, and the army does not replan. BC14 asks those four questions before selection, about every candidate the stance allows. The answer is a per-candidate verdict and penalty, and the catalog adds it to its score:
- an impossible candidate is filtered out;
- a risky one loses score;
- a sound one is untouched.

The chosen plan's assumptions are watched while it runs, and the army replans when one breaks.

**Advantage over today.**
1. **Dividing becomes a reasoned choice.** A superior army divides and converges when the arithmetic allows it. At even odds it does not divide, because one wing would stand alone against the enemy's concentrated mass.
2. **Held crossings are not assaulted head-on when there is a better choice.** When the army can afford a separate crossing, the held crossing is pinned and a flank wing is routed through the unwatched one, on the side that has a route. When it cannot, the army holds its own bank and probes, rather than diving at the bridge.
3. **Wings strike together.** Wings are released so that they arrive together, from vanilla's own speeds and courier delays, instead of marching only after the centre's contact (BC11).
4. **Defenders watch their crossings.** A Defender looks at, and guards, the crossings by which it could be turned.

Each of these is an emergent result of one evaluation, not a scripted case (§ 6).

**Measurement.** Each claim has a `criterion-*` in § 12. Each criterion names its value, its basis, how it is measured, a guardrail and the place the running mod emits the number.

---

## 2. Ground truth

### 2.1 Vanilla (build 14703111)

1. **Scale and clock.**
   - One battle world unit is 6.3 m on the ground: `GamePrefs.battlefieldrealdimensionfactor`, `battleprefs` "Calculation factor range to real distances … if terrain size is 2048 and this factor is 6.9 then the total battlefield size is 14150 meters", shipped 6.3. The bullet flight time at 108145 uses it.
   - Whiskey's existing code and docs call world units "meters" (for example `PostureSense.CloseContactMeters` = 250). This spec keeps that naming for constants and calls them **units** in prose: one unit is one Whiskey "meter".
   - Battle time is game hours (`currenttimefromstart`). The day length is `battledaylengthinmins` = 120 real minutes for 12 game hours (`battleprefs` "Day Length in Realtime Minutes (calculated out of a day of 6-18h)"), so one game hour is 10 real minutes at 1×. This matches `OrderExecutor.RealSecondsPerBattleHourAtOneX` = 600.
2. **March speed.** `Regiment.RegimentMove` (133063) computes:
   - **The base:** `num11 = unitbasicspeed[unittyp, mounted]` (133354). Infantry is 1, cavalry is 1 dismounted and 2 mounted, artillery is 0.5 unlimbered and 1 limbered, and groups use mounted (`unitprefs` "Basic Unit Speed Factor …").
   - **Terrain:** `num11 *= GetTerrainSpeedAdjustment() * basicunitspeed` (133525). `basicunitspeed` = 1000 (`unitprefs` "Basic unit speed (all)"), and `GetTerrainSpeedAdjustment` = `1 / terrainspeccost[currentterrain]` (132888-132901). The shipped costs: road 1, bridge 1, grass 2, ford ("River Crossing") 2, field 3, forest 4, fences and walls 4, creek 5, river 5 (impassable to the AI pathfinder unless frozen; BC10 finding 6), swamp 6. Road speed does not depend on formation in battle: the `formation != 3` road test at 133527 changes only a local that the campaign branch reads (133919-133923).
   - **The group:** `num11 *= Min(groupspeed, 1)` (133986). `groupspeed` is the slowest base factor among the group's regiments (`GetSlowestSpeedOfGroup` 91708-91734), so an infantry formation moves at the infantry base.
   - **Fatigue:** `num11 *= Pow(Max(1 − fatigue, 0.01), fatiguefactorspeed)` (133569), with `fatiguefactorspeed` 0.2.
   - **Running and double quick:** double quick is `doublequickspeedfact` (infantry 1.7). Running is `runningspeedfact`. Line infantry cannot run (`abletorun`).

   So formed infantry marches at 1000 units per game hour on a road and 500 on grass. A route's march time in game hours is `Σ (segment length × terrainspeccost) / (basicunitspeed × base)`. This is the same metre-cost sum that `RoadRoutePlanner.PolylineCost` already computes, divided by `basicunitspeed × base`.
3. **Order delay.** When `useorderdelay` is set, `SetWaypoint` queues the order on the unit's parent HQ (91487-91492). The delay is built from four pieces:
   - **Processing:** `AddToOrderQueue` (124917-124998) adds `processingtimegroupstandard` = 0.02 h (`unitprefs` "Standard processing time for group commander to write the order"), × 3 (`additionalprocessingtimefeudsfactor`) for a feuding unit.
   - **The first leg:** `AddOrderCourierline` (125009-125171). Within `standardbuglerange` (75 units) the leg is a bugle with recognition time `buglestandardrecognitiontime` = 0.02 h. Beyond it the leg is a real courier.
   - **Courier speed:** a courier rides mounted and running at `unitbasicspeed[5,1] × runningspeedfact[5] × 1000` = 2.33 × 2.2 × 1000 ≈ 5,126 units per hour on a road, ≈ 2,563 on grass. That is × 0.7 (`slowdowncourieroutsideradius`) when the source unit is detached.
   - **The chain:** at each HQ below, a secondary courier waits `processingtimegrouproute` = 0.02 h before riding on (125163). The group starts moving only when the last line has delivered (125280, `SetOrderStatus(2)` 125353). Movement is blocked while `pathstatus < 2` (134071-134074).

   No commander stat enters the battle delay; the stats are used only on the campaign branch. Vanilla's AI skips the delay entirely for a unit with `combatstatussubordinates > 0` (`debug_aicheat_noorderdelayinfrontofenemy`, default true, 91296-91298): an engaged AI unit moves at once.

   Whiskey already models this delay for W&L orders: `WlCourierDelay.BattleHours` (processing on the first leg, route processing on later legs, a bugle within range, otherwise metres over `WlCourierDelay.SpeedPerHour`). The courier speed it is built with is `GamePrefs.unitbasicspeed[5, 1] * GamePrefs.runningspeedfact[5] * GamePrefs.basicunitspeed` (`WlOrderFlowRuntime`).
4. **Crossings.**
   - **The list:** fords (id 5) and bridges (id 6) are listed in `bfs.rivercrossingpositions`, de-duplicated at 20 units (26610-26640). Pontoons (ids 9-13) are not listed.
   - **The blocked set:** `UpdateBlockedCrossings` (7917-7973) marks a crossing blocked when a non-artillery enemy regiment within 1.5 × its fire range faces it within 45°. It also blocks the point one fire range ahead of a standing enemy in cover. The scan is over every enemy unit, with no fog test.
   - **The obstacles:** `BlockedMovingOrder` (3054-3150) carves NavMesh obstacles on the blocked points, except points within range of the destination (3089), and is used only for moves longer than 150 units (6953-6955).

   This is vanilla's only "use another bridge" behaviour, and it is reactive pathing, not planning.
5. **Flank moves and coordination.**
   - `CheckFlankMoves` (6247-6312) and `FlankMove` (116929-117108) are a local ±45° hook of 0.4 group widths. A hook aborts on river id 4, or on an enemy 5 units above its turn point. Vanilla has no timing.
   - Arrive-together logic exists only in the campaign AI (`aiarrivalfactor`, `AICampaign` 8852 and 10244). No setter of it is in `AIBattle`.
   - Vanilla's isolated-cluster rule (`CheckForSeparatedEnemy` 8056-8215) attacks a cluster of value at most 0.65 × the group's own whose nearest neighbour is more than 1.3 × the group's distance to it away. That is the rule by which a vanilla-run enemy punishes a separated body.
6. **End of day.**
   - The day ends at `dayendhours[month − 1]` (`battleprefs` "End Hour per Month": 17 17 18 18 19 20 21 21 20 19 18 17) against `uniStormSystem.hourCounter`. The game then runs the end-of-day cycle (`eodcycle` 1, then 2; 86407-86434).
   - `RegimentMove` returns at once while `eodcycle > 0`, couriers aside (133238). A march not finished by the day's end hour is not finished that day.
7. **Fog leak (recorded, not used).** `CheckIfUnitIsReinforcement` (87508-87529) returns the enemy's `scheduledarrival` timing with no fog check, and vanilla's hotspot map reads it (7982-7995). Whiskey knows an enemy reinforcement only once its entry point is seen (BC2). BC14 never reads the schedule.

### 2.2 Whiskey today (`feat/battle-next` after BC12; BC13 in build)

- **Selection.** `TacticalPlaybookCatalog.Select` runs the precondition filter (cavalry plays), then the stance filter (`StanceAllowsPlaybook`). It then scores `0.5 × personality + 0.2 × terrain + 0.15 × odds + 0.1 × hint + 0.05 × jitter`, plus three biases: envelopment pressure 0.2 × `EnvelopmentAffinity`, delaying pressure 0.2, and defensive pressure 0.35 × `DelayingAffinity`. The best score wins. Details:
  - The jitter is xorshift32 seeded by `AllianceId × 31 + 7`. It is drawn after the stance filter and before the crest check. BC12 learned that a filter before the draw shifts every later play's jitter (plan § BC12 As built).
  - No ranked list and no loser score is kept or emitted.
  - There are 18 playbooks:
    - 11 Offensive: DoubleEnvelopment, MobileFlankMarch, WideFlankApproach, ManeuverAndFix, ContinuousAttrition, FrontalShockAssault, ForcedFrontalAttack, HesitatedCommit, CavalryRaid, GenericAggressive, GenericDesperate.
    - 5 Defensive: PreparedDefense, ReverseSlopeDefense, DelayingDefense, CavalryScreeningDelay, GenericCautious.
    - 2 Neutral: MeetingEngagement, GenericMethodical.
- **Selection timing.**
  - The initial pick is `AttachArmyIfActive` → `ArmyOrchestrator.PickInitialPlan`.
  - A replan goes `ArmyTickCycle.MaybeReplan` → `ArmyOrchestrator.Replan` → `TryPickPlan` → `Select`. Its triggers are `ArmyReplanTriggers.Evaluate`, under the `Min Replan Seconds` cooldown (default 600 battle-s). A new battle day replans once regardless of the cooldown.
  - **Defect:** the stance and the opportunity flags are written by `ArmyPostureInputsRuntime.Refresh`, which runs after `DriveTickCycle`. The initial pick therefore sees the default `BattleStance.Meeting` and `NoOpportunity`, so the stance filter does not apply to it. A Defender can open with an offensive playbook. Replans see the previous tick's values. (Read from code: `ArmyOrchestrator` default stance, `TacticalBattleCoordinatorRuntime` call order.)
- **Templates.** `PlayTemplateCatalog.Lookup` maps each playbook to rects in army-relative coordinates. `PlayProjection.Project` lays them out: lateral × half the frontage, forward × the army-to-enemy distance; frontage is the units' lateral spread × 1.5, at least 300 units. `PlayCorpsAssignment.Assign` then gives each rect, in descending priority, the closest direct child not yet assigned. A rect left without a child is skipped silently: with two corps, DoubleEnvelopment fills both wings (priority 10) and no child holds its Center (priority 8). Both `Project` and `Assign` allocate, and they run only for the selected plan.
  - Five templates send a body off on its own (`PlayRect.Envelops`): DoubleEnvelopment (two wings), MobileFlankMarch (FlankMarch, 900 s after Center contact), WideFlankApproach (FlankMarch, 600 s), ManeuverAndFix (RightWing, 300 s; its Center fixes and a Demonstration rect screens), CavalryRaid (DeepRaid).
  - HesitatedCommit and GenericMethodical wings only hold for a start delay.
- **BC11 execution.** `EnvelopmentManeuver.Step` runs Hold → March → Turn → Attack per enveloping child:
  - **Hold:** until the offset after the Center child's contact, or after the play's activation with no Center. The Hold has a ceiling of `MaxManeuverHours` = 1.0 from activation.
  - **Launch:** never before the march leg has a NavMesh route result.
  - **Abandon:** `no-route`, `river-on-leg`, `anchored-high-ground`, `lost-strength`, `timeout` (`MaxManeuverHours` from the march start), or `plan-changed`. A lone enveloping rect mirrors once.
  - **Route plans:** one per side per step (`PickRoutePlan`), straight from the child to the anchor through whatever crossing the pathfinder takes.
  - **On Abandoned:** the child returns to its role. **No replan follows**, and maneuver state never reaches `ArmyReplanTriggers`.
  - **The time limit:** `MaxManeuverHours` = 1.0 h is 1,000 units on a road or 500 on grass for infantry (§ 2.1 item 2), so a wider flank march times out before it arrives.
- **Movement infrastructure (BC10, BC12).**
  - `BattleTerrainGrid` (1 unit cells, ids, `SpeedCost`, `SampleLine`, the crossing list).
  - `CoveredCrossings.BuildPoints`: vanilla's two rules over perceived enemies only. It outputs points only, with no crossing index and no strength.
  - `RoadRoutePlanner`: an A* on a 25-unit lattice, `MaxCells` 6,000, one plan per side per snapshot, `RoutePlanCache` 0.25 doctrine-h.
  - The posture probe (`PostureTargets.CreateNavFilter`, `AtTerrain`). The test `NavMeshQueriesSitAtTerrainHeight` pins exactly three `CalculatePath` call sites.
  - `TerrainPicture`: a 20-unit lattice whose cell id prefers bridge, then ford, then river, then creek over land (sampled every 5 units). It also gives `TerrainStrength`, `ApproachDifficulty` and `TryNearestCrossing`. `TacticalTerrainLinePlanner.FlankAnchored` is available.
- **No march-time, ETA or enemy shift-time estimator exists** anywhere in the battle code. The only unit-speed read is the W&L courier (§ 2.1 item 3).
- **Perception.** `BattleSnapshot.Observations` holds enemy regiments only (Visible, or Remembered with `Confidence01`), with no enemy group positions. `TacticalEnemyContactLedger` gives `EstimatedEnemyStrength`, `Confidence` and the enemy entry points with their watch stamps. `ArmyIntentInference` gives the enemy intent (Attack / Defend / Probe / Refuse / Withdraw).
- **BC13 (in build; binding spec `2026-10-15-battle-opportunity-design.md`).**
  - The per-commander `CommanderPersonalityTable`. The army's vector is the table entry of the side's commanding officer.
  - `IPerceivedGround`: a point is perceived when vanilla's AI fog grid would show an infantry regiment standing there, or within `RangeOnlyReach` in RangeOnly mode.
  - `ExploitTable.IsExploiting`. Its rule that a maneuvering group is never an exploit asset, and that the play executor never maneuvers an exploiting group.
  - The `OpportunityIntentProducer`, placed before `Posture`.

### 2.3 What is missing

| Question | Today | BC14 |
|---|---|---|
| A route for each wing | Asked by BC11 after selection, one child at a time | Screened for every candidate before selection; confirmed by NavMesh for the best candidates |
| A held crossing on the route | Vanilla carves it reactively (`BlockedMovingOrder`) | Each crossing is classified Held / Watched / Clear / Unknown; a wing is routed through the best one; the play pins the held one |
| Arrival together | Wing marches after Center contact + offset | Release time computed from vanilla march speed and courier delay so the strike lands at contact + offset |
| Defeat in detail | Not asked | Exposure window against the perceived enemy's interior-lines shift time; isolated odds against the commander's bar |
| Own approach | Not asked at selection | `TerrainPicture.ApproachDifficulty` and `TerrainStrength` at each strike point, and a contested-crossing cost |
| A broken assumption | BC11 abandons the wing; the plan stays | `ManeuverAssumptionBroken` replan trigger with hysteresis |

---

## 3. History as weights

These patterns set the weights and thresholds of §§ 4-7. They are not scripts, and no battle is replayed. Each line quotes its source (all accessed 2026-10-15/16); a figure in the right column that is derived, not quoted, says so.

| Pattern | Source and quote | Weight it sets |
|---|---|---|
| Dividing in the face of a stronger enemy is the textbook error | battlefields.org, 10 facts about Chancellorsville: "Dividing one's force in the face of a numerically superior enemy was considered to be a sure path to defeat." | Detail risk is the default cost of dividing (requirement-detail-risk); a dividing play pays unless the arithmetic clears it |
| Divide only when each part can match what the enemy can concentrate | Jomini, *The Art of War* (1862 tr., Gutenberg 13549), Art. XXI: "A double line is applicable in the case of a decided superiority of force, when each army will be a match for any force the enemy can bring against it." | Isolated odds of each body against the enemy that can reach it (`E_j`), against the commander's `Bar` ≈ 1.0 |
| Concentric attacks need parts that cannot be beaten separately | Jomini Art. XXI: concentric operations are good "When they direct to the same end the efforts of two armies which are in no danger of being beaten separately by a stronger enemy." | The detail term applies per body and the candidate takes the worst body |
| Interior lines are a time race | Jomini Art. XXI: interior lines let "the general … concentrate the masses and maneuver with his whole force in a shorter period of time than it would require for the enemy to oppose to them a greater force." | The exposure window `W_j` against the enemy's shift time `τ_j` (requirement-shift-time) |
| An extended movement is dangerous while it is under way, less so against a passive enemy | Jomini Art. XXXII: "any movement is dangerous which is so extended as to give the enemy an opportunity, while it is taking place, of beating the remainder of the army in position." and "If the opposing general shows little skill and seems inclined to indulge in extended movements, his adversary may be more daring." | Route exposure (requirement-route-exposure); `PassiveShiftFactor` 1.5 for a perceived defending enemy; audacity lowers `Bar` |
| Chancellorsville: a turning march of about 12 miles, all day, against a passive enemy and a flank in the air | NPS, Chancellorsville flank attack: "Jackson's corps, about 30,000 troops, would follow a series of country roads and woods paths to reach the Union right." "The exhausting march, which altogether traversed more, than 12 miles, ended about 3 p.m." "Eleventh Corps headquarters dismissed the reports as frightened exaggerations from alarmists or cowards." battlefields.org: "Hooker's timidity in battle led to poor choices" | A long march released early so it strikes on time (requirement-synchronized-release, D7); woods halve route exposure (the column was seen and ignored); an unanchored enemy end costs nothing extra and an anchored one adds `AnchoredEndPenalty` |
| The risk Lee accepted | NPS, same page: "The Army of Northern Virginia would thus be fractured into three pieces, … any one of which might be subject to rout or annihilation if the Yankees resumed the offensive." | The risk stays choosable: `Risky` is never filtered (D3); a bold commander's `Bar` falls (to 0.6 at caution 0, audacity 1 and aggression 1) |
| Perryville: an attack sized to a wrong picture of the enemy | battlefields.org, Perryville: Bragg "believed that he faced only a small portion of Gen. Buell's army, not the main body." historyofwar.org: "Bragg was still convinced that the force facing Polk was at most the extreme right wing of the Federal army." | `FogPremium` = 0.5 × (1 − confidence) × (0.5 + caution01): the unseen part of the enemy is not assumed absent (requirement-enemy-picture) |
| Support out of earshot is not support | historynet.com, Perryville: "the battle taking place less than three miles from his Dorsey Farm headquarters." historyofwar.org: "the sound of that afternoon's battle never reached Buell." | A body counts as pinning only when its own strike time has come (`Pin` counts engaged bodies only); no body is assumed to "march to the guns" |
| Antietam: a few hundred men held a bridge against a corps for hours | battlefields.org, Burnside's Bridge: "For four hours, about 500 Confederates under Brig. Gen. Robert Toombs held off Burnside's corps," historynet.com: "Burnside proceeded somewhat cautiously with his 13,000." | A Held crossing is not impossible but costly: `HeldCrossingHours` and `contested` = 1 (requirement-held-crossing) |
| Antietam: the ford nobody scouted | NPS, Isaac Rodman: "Rodman found too late that no one had properly reconnoitered the proposed ford, two-thirds of a mile south of the bridge," battlefields.org: "Rodman could not locate a suitable creek crossing until well after noon." | An Unknown crossing costs `contested` 0.25 and raises a look request (requirement-crossing-look); a crossing is Clear only when perceived recently |
| Antietam: piecemeal attacks let the defender shuttle troops | npshistory.com (NPS Handbook 31): "the story of Antietam is one of piecemeal Federal attacks—a corps here, a division there." "Taking advantage of his interior lines, he repeatedly achieved a local advantage of numbers," and "Lee ordered Walker's two brigades north from the Lower Bridge; they too disappeared into the West Woods." | The exposure window grows with the strike spread; synchronized release closes it (§ 6 scenario 2); the Defender's crossing-threat release is Lee's shuttle (requirement-defender-crossing-release) |
| Order transit on the field | historynet.com, Antietam: "Burnside received no order to attack until about 10 a.m." (the order was headed 9:10) | Courier time is a real term (requirement-courier-time); vanilla's own courier model sets its size |
| Converging columns fail on coordination | npshistory.com (Handbook 33), Glendale: "Huger was delayed by obstructions, mostly felled trees, with which the Federals had blocked his path." "Jackson never did show up" "Magruder's forces did not get there in time to help." NPS Richmond: "Four separate Confederate columns angled toward the crossroads." | The `late` term and the monitor's `late` assumption; each body is timed separately and the worst body sets the penalty, so more bodies cannot lower the risk |
| Mechanicsville: the frontal attack went in alone | Encyclopedia Virginia: "At three o'clock in the afternoon, an impatient and impetuous A. P. Hill attacked anyway" and Jackson's men arrived "around five o'clock, but instead of joining the fight"; Emerging Civil War: "Instead, troops attacked as they came up." | Release times are computed from estimates, not from the first contact (D7); the Center fixes, it does not assault alone |
| Staff work and maps | historynet.com, Seven Days: "unbeknownst to Lee, his staff was saddled with inaccurate maps of the area." Emerging Civil War: "the lack of clear communication and good staff work" | Routes are confirmed on the real pathfinder before the plan relies on them (requirement-navmesh-confirm); a missing path is `no-route` |
| Stones River: mirror-image plans, the first blow decides | battlefields.org, Stones River: "Both commanders formulate a nearly identical battleplan for December 31: strike their opponent's right flank." "Bragg versus Rosecrans": Rosecrans "ordered Van Cleve back across the stream to support his threatened flank." | Strike times are planned and watched: the monitor's `late` assumption replans when a body falls an hour behind its planned strike (requirement-assumption-monitor) |
| A superior force divides safely | battlefields.org, Nashville: "hoping to pin the Confederate right while smashing the left." and "Outnumbered by a factor of more than two-to-one" | § 6 scenario 1: a pin plus a turning wing is `Feasible` under a clear advantage |
| Even a superior force's columns can miss | battlefields.org, Chattanooga: "faulty intelligence leads Sherman's men to Billy Goat Hill instead." | A superior army's plan is still judged per body; the `Unknown` crossing and route confirmation apply to it too |
| Bentonville: a divided army nearly beaten in detail | battlefields.org, Bentonville: "Realizing that their forces were divided, Johnston decided to attack one of Sherman's wings" | The enemy's shift is modelled even when we are superior overall; `Pin` counts only engaged bodies |
| Crossing practice | Jomini Art. XXXVII: "It is essential to deceive the enemy as to the point of passage … false attacks must be made near the real one, to divide the attention and means of the enemy." Art. XXVIII: "the army should cross the chain in not more than two masses, and the routes which these follow should not be divergent" | Pin the held crossing with the Center or the Demonstration rect while a wing crosses elsewhere (§ 6 scenario 4); at most two enveloping bodies (the templates already have at most two) |
| Fredericksburg: the unopposed crossing was ready early | npshistory.com (Civil War Series 15): "Well downstream, at Franklin's crossing, the 15th New York Engineers had already completed a bridge" | A Clear crossing costs nothing extra; the Dijkstra prefers it within `HeldCrossingHours` of march |
| March rates and halts | Casey, *Infantry Tactics* (1862): "The length of the direct step, or pace, in common time, will be twenty-eight inches … at the rate of ninety in a minute." Civil War Monitor: "Estimated distance covered in one hour at common time in ideal conditions: 3 miles" and "…in less-than-ideal conditions: 1.5–2 miles" | Not used as a clock (D4): vanilla moves formed infantry at 6.3 km per game hour on a road. History sets only the ratio: off-road and woods march at a half or less, which vanilla's terrain costs already give (grass 2, forest 4 against road 1) |
| Column length | Civil War Monitor: "Estimated amount of roadway that 40,000 soldiers, with artillery and wagon trains, occupied on the march: 16 miles" | Not modelled: vanilla's march column is a group formation, and arrival is judged on the HQ as BC11 does. Derived figure (about 0.4 miles per 1,000 men) recorded only; smoke 4 measures whether the omission matters (§ 16) |

URLs: battlefields.org/learn/articles/10-facts-chancellorsville; gutenberg.org/ebooks/13549; nps.gov/frsp/learn/historyculture/cvillehist-flank.htm; battlefields.org/learn/civil-war/battles/chancellorsville; battlefields.org/learn/civil-war/battles/perryville; historyofwar.org/articles/battles_perryville.html; historynet.com/the-sounds-of-silence/; battlefields.org/learn/maps/antietam-burnsides-bridge-sep-17-1862; historynet.com/battle-of-antietam-controversial-crossing-on-burnsides-bridge/; nps.gov/people/isaac-peace-rodman.htm; npshistory.com/handbooks/historical/31/hh31h.htm; npshistory.com/handbooks/historical/31/hh31l.htm; npshistory.com/handbooks/historical/33/hh33l.htm; nps.gov/rich/learn/historyculture/glendalebull.htm; encyclopediavirginia.org/entries/mechanicsville-battle-of/; emergingcivilwar.com/2016/12/01/general-magruder-why-did-you-attack/; historynet.com/7-days-made-robert-e-lee-icon/; battlefields.org/learn/civil-war/battles/stones-river; battlefields.org/learn/articles/bragg-versus-rosecrans; battlefields.org/learn/civil-war/battles/nashville; battlefields.org/learn/civil-war/battles/chattanooga; battlefields.org/learn/civil-war/battles/bentonville; npshistory.com/publications/civil_war_series/15/sec6.htm; archive.org/download/infantrytacticsf01brig/infantrytacticsf01brig_djvu.txt; civilwarmonitor.com/?p=19266. Scourge of War (not installed here; `docs/scourge-of-war-ai-anchors.md`): its army plays wait on `PLAYFTime` and do not check rivers or heights on the way; its division think loop throttles orders by a courier timer (`CourTime + 15 * TICSPERMIN`). Neither times an arrival; BC14 is Whiskey's reading of those ideas.

---

## 4. (A) The feasibility evaluation

The evaluation is pure (`ManeuverFeasibility`, `Tactical/Orchestrator/Plays/`). It runs on the army's heavy path. It reads the side's `BattleSnapshot`, the BC10 grid, the BC12 picture, the BC14 crossing graph, the contact ledger, the intent inference and BC13's personality table, and publishes one `FeasibilityLedger` per side. It submits no intent and writes no vanilla state (§ 14 D1).

- **requirement-candidate-set** — The candidates are every playbook that passes the catalog's precondition and stance filters for the side's **current effective** stance: `TacticalStanceReconciler.ResolveEffectiveStance`, the stance `TryPickPlan` filters on, refreshed per requirement-stance-before-pick. Each is paired with its template (`PlayTemplateCatalog.Lookup`):
  - A template with exactly one enveloping rect (MobileFlankMarch, WideFlankApproach, ManeuverAndFix, CavalryRaid) is evaluated **twice**: as authored, and mirrored by flipping the sign of every rect's lateral coordinate in the projection frame (the frame `PlayProjection` lays the rects out in, so the mirror matches what the executor will project). The better of the two sides is the candidate's result, and the side is recorded (`Mirrored`). Sides rank by verdict first (Feasible and Risky, then Unknown, then Impossible), then by the lower penalty; a tie keeps the authored side.
  - DoubleEnvelopment is evaluated once: it is symmetric.
  - A template with no enveloping rect is evaluated once, as one body (requirement-bodies).
- **requirement-hypothetical-layout** — Per candidate (and side), the evaluation projects the template and assigns direct children exactly as the play executor would, through allocation-free overloads:
  - `PlayProjection.ProjectInto(template, …, Span<WorldPlayRect>)` and `PlayCorpsAssignment.AssignInto(rects, corps, Span<CorpsAssignment>)`, the same rules as the shipped methods; parity tests pin them.
  - Inputs are the ones `PlayExecutorRuntime.ComputeZoneAssignments` uses: the army centroid, the enemy anchor (`PlayAnchor`), the frontage estimate, the crest line for ReverseSlopeDefense, the direct-child set.
  - The offered children exclude:
    - a BC10 deployed reserve (as today);
    - a child that is itself a live BC13 exploit asset (`ExploitTable.IsExploiting(child)`). A child with an exploiting descendant stays in the layout, at half strength (requirement-bc13-commitment);
    - a child whose subtree strength is wholly player-owned (requirement-player-share).
  - **`wing-unfilled`:** an enveloping rect that gets no child makes the candidate's verdict `Impossible`, reason `wing-unfilled`. The template cannot do what it says.
  - **`no-fixing-force`:** a template whose fixing rect (Center or Demonstration) gets no child while an enveloping rect does is not filtered. Its bodies are evaluated with no fixing force: no enemy strength is pinned (requirement-detail-risk). This is how "divide if reasonable" depends on the number of bodies the army has.
- **requirement-bodies** — A candidate's **bodies** are:
  - each child assigned to an enveloping rect, a separate body moving on its own route to its rect anchor (a DeepRaid to its outflank point first, `EnvelopmentManeuver.OutflankPoint`);
  - every other assigned child together, the **main body**, whose members stand within one frontage of each other and support each other.

  The main body's **route** is evaluated per member (each member to its rect anchor), and its contact time is the latest fixing member's arrival (requirement-strike-time).
  - **Fixing members:** every main-body member on a rect that is not Reserve or ReserveLine. They pin enemy strength (requirement-detail-risk).
  - **Reserve members:** they count in the main body's strength `S_main`, but pin nothing.

  **Detail risk applies only where a body is separate.** In a template with enveloping bodies, every body is judged, the main body included. Its isolated odds measure the fix standing alone while the wings are away. A template with no enveloping rect has one body and no detail term: one line supports itself.

  **Strike points:**
  - an enveloping body's: the perceived enemy line end on its rect's side (`EnvelopmentManeuver.EnemyLineEnd`), or, for a DeepRaid, its outflank point;
  - the main body's: the play's enemy anchor (`PlayAnchor`'s enemy point, the strength-weighted centroid of the perceived enemy).

  **Approach axis:** a body's approach axis at its strike point points from the strike point back toward the body's rect anchor, the ground the body crosses. That is the direction `TerrainPicture.ApproachDifficulty` and `TerrainStrength` expect: their axis points toward the side that has to cross the ground.

  **A target on open river** (a rect anchor that the projection lays on a river cell) snaps to the nearest land cell of the picture. A tie goes to the body's own region.
- **requirement-river-regions** — `RiverRegions` (pure, one per battle, `BattleScope`) splits the map into land regions separated by open river, and links them through the listed crossings.
  - **Build.** After `TerrainPicture.Ready`, a stepped two-pass connected-component labelling (4-connectivity, union-find) runs over the picture's 20-unit lattice. The first pass is one lattice row per unit of work; the second relabels into final ids in lattice order.
    - **Blocking cells:** river (4), ford (5) and bridge (6), when the map is not frozen. The picture's cell id prefers a crossing over the river, so a crossing cell must block too, or it would join the banks.
    - **Land:** creek (3) and every other id.
    - **Joining the banks:** crossings join regions only as links (below).
  - **Crossing links.** Each `BattleTerrainGrid` crossing (fords and bridges, vanilla's own list) links every region with a land cell whose centre lies within `CrossingLinkMeters` = 60 units of the crossing point, at most four regions per crossing. A crossing that touches two or more regions is a **link**. A crossing inside one region (a bridge over a creek) is recorded and links nothing.
  - **Frozen map.** The whole map is one region (`Frozen`), and no crossing logic applies.
  - **Budget.** The build runs inside `TerrainPictureRuntime.Step`'s existing 4 ms step after the picture is ready, one lattice row per unit of work, under the scope `tactical.river-regions.build`. Until it is complete, every consumer treats the map as unknown (requirement-route-screen).
  - **Limits.** At most `MaxRegions` = 64 regions and `MaxCrossings` = 256 links. A map beyond either logs `tactical-battle-core:river-regions-truncated` once and keeps the first in lattice order. A dropped region's cells read −1, as river does.
- **requirement-crossing-state** — `CrossingWatch` (pure, per side, `BattleScope`) classifies every link crossing each heavy tick:
  - **Held:** a perceived enemy regiment covers it by vanilla's rule (a) of `CoveredCrossings`: Visible, or Remembered at confidence ≥ 0.5; non-artillery; within 1.5 × fire range; facing within 45°. The rule is factored out of `CoveredCrossings` into a shared `CoveredCrossings.Covers(in EnemyObservation, float cx, float cz)`, which both callers use. The perceived strength within `HoldRadiusMeters` = 300 units of the crossing is kept with the state (Visible full, Remembered × confidence).
  - **Watched:** not Held, but a perceived enemy regiment (artillery included) stands within its own reach of it: `BattlePerception.RangeOnlyReach(maximumlineofsight[unittyp], distancetoenemytocancelinterruption)`, the reach vanilla's range lists give a unit of that type (BC2). The line-of-sight table is public game data; the regiment is a perceived one. No literal distance is added.
  - **Clear:** neither, and the crossing point was perceived (BC13's `IPerceivedGround.IsPerceived`) within the last `ClearMemoryHours` = 0.25 doctrine-h.
  - **Unknown:** everything else.

  **Probes.** Every link crossing is a probe candidate, oldest probe first, under its own cap: `MaxCrossingProbesPerTick` = 16 per side. Probes go through BC13's perceived-ground runtime (`IPerceivedGround.IsPerceived`; BC13 spec § 8, about 1 ms per 512 points). They run before the tick's classification, so a probe made this tick counts. A probe result is stamped with the doctrine hour. A crossing is never Clear on the strength of a stale probe.
- **requirement-route-screen** — Each body member's route is screened on the crossing graph: from its current position (the child's record) to its target (the rect anchor, or the DeepRaid outflank point and then the anchor), **and** on to its strike point (the inward leg BC11 attacks along). An enveloping body's inward leg is the anchor → enemy line end leg. The main body's members stop at their anchors: a fix does not cross to strike.
  - **Same region** (or a frozen map): one leg, origin to target.
  - **Different regions:** Dijkstra over the link crossings of the regions on the way. A node is a link crossing. An edge is a leg between two points in one region, priced by requirement-leg-cost. A Held crossing adds `HeldCrossingHours` (requirement-held-crossing) to the edge through it. The cheapest path is the route, and its crossings are the route's crossings.
  - **No path** (no chain of link crossings joins the two regions on an unfrozen map): verdict `Impossible`, reason `no-crossing`. On the inward leg the reason is `river-on-leg`, BC11's own abandon reason, so the screen refuses before selection what BC11 would abandon after it.
  - **Map unknown** (the picture or the regions not ready, or no grid): the screen has no result. The candidate's route term is neutral, and the verdict is `Unknown` (requirement-verdict). This is today's behaviour: BC11 still refuses to launch without a route.
- **requirement-leg-cost** — A leg's cost is the BC10 grid sampled every `LegStepMeters` = 10 units along the straight segment (`SampleLine`), summing `step × SpeedCost(id)`. The last sample is pro-rated to the leg's remaining length, so a 1,000-unit road leg costs exactly 1,000.
  - **Beside the leg's own crossing:** a sample on open river (4) within `CrossingLinkMeters` of the crossing the leg starts or ends at is that crossing's footprint. It is priced at the crossing's own cost (ford or bridge id).
  - **A bend:** any other open-river sample is a bend of a river the leg skirts. It is priced at the swamp cost, as a detour proxy, and the leg is marked `estimated` until a NavMesh result replaces it.
  - **March hours:** `cost / (BasicUnitSpeed × base) / fatigueFactor` (requirement-march-time).
  - **Dijkstra:** it runs in the body's hours. A Held crossing adds `HeldCrossingHours` to the edge through it, so the chosen crossing can depend on the body's speed.
- **requirement-navmesh-confirm** — NavMesh confirms screened routes on the evaluation's own budget:
  - **The queue:** the bodies of the top `ConfirmCandidates` = 3 candidates by catalog score without the feasibility term, the selected plan's bodies first.
  - **The query:** each route is confirmed leg by leg, through its chosen crossing (child → crossing, crossing → target), with the posture probe's filter (`PostureTargets.CreateNavFilter`) and ends at terrain height (`PostureTargets.AtTerrain`).
  - **The budget:** at most `ConfirmQueriesPerTick` = 1 `NavMesh.CalculatePath` per side per coordinator tick, separate from the posture producer's `NavPlanBudget` and from BC11's route pick, so contact groups never wait behind planning.
  - **The cache:** each result is cached per (origin 50-unit bucket, target 50-unit bucket, crossing index) for `ConfirmCacheHours` = 0.25 doctrine-h (the `RoutePlanCache` precedent).
  - **Results:**
    - A complete path replaces the leg's estimate with the path's polyline cost (`RoadRoutePlanner.PolylineCost`, the BC10 metre-cost sum).
    - A path that is not `PathComplete` makes the leg `no-path`. If no other crossing gives the body a route, the verdict becomes `Impossible`, reason `no-route`.
  - **The call site:** this is a fourth `CalculatePath` call site. `NavMeshQueriesSitAtTerrainHeight` changes its pinned count from 3 to 4 and pins the new site.
- **requirement-march-time** — A body member's march hours are `Σ legs (cost / (BasicUnitSpeed × base)) / fatigueFactor`:
  - `base` is `GamePrefs.unitbasicspeed[t, mounted]` of the slowest unit type in the member's subtree, read as vanilla's `GetSlowestSpeedOfGroup` does: a group counts as mounted, horse artillery is × `horseartilleryspeedfactor`, and the result is capped at 1 for a mixed group (`Min(groupspeed, 1)`). A cavalry-only member uses 2 (mounted), an infantry-bearing member uses 1.
  - `fatigueFactor = Max(1 − meanFatigue, 0.01) ^ fatiguefactorspeed`, with the mean over the member's regiments (`UnitRecord.Fatigue`).
  - `1000` is `GamePrefs.basicunitspeed`, read once per battle, never typed. The same holds for every vanilla value in this section: `MarchRules` carries them.
- **requirement-courier-time** — A member's courier hours estimate vanilla's delay for an order from the army HQ down to the member and through the member's own chain. The leg rules are `WlCourierDelay.BattleHours`'s, with a terrain-priced ride:
  - **Legs:** army HQ → member HQ, then down the D5 parent map to the member's brigades; the longest branch is taken, because vanilla starts the group only when its last courier has delivered (§ 2.1 item 3).
  - **First leg:** `max(processingtimegroupstandard, buglestandardrecognitiontime)` within `standardbuglerange`. Beyond it: `processingtimegroupstandard` + the ride.
  - **Each later leg:** `buglestandardrecognitiontime` within bugle range. Beyond it: `processingtimegrouproute` + the ride.
  - **The ride:** the straight HQ-to-HQ line's leg cost (requirement-leg-cost) divided by `unitbasicspeed[5, 1] × runningspeedfact[5] × basicunitspeed`. A courier is a mounted running unit whose speed vanilla divides by the terrain cost like any other (`RegimentMove`). On grass, 600 units take 1,200 / 5,126 ≈ 0.23 h. `WlCourierDelay`'s own speed is terrain-free and is not reused for the ride.
  - **Engaged members:** a member with a Visible enemy within `PostureSense.CloseContactMeters` (the snapshot's perceived contact) gets 0 courier hours, because vanilla's AI moves an engaged unit at once (§ 2.1 item 3).
  - **Order delays off:** with `GameVars.useorderdelays` off, every courier time is 0, as vanilla's `SetWaypoint` sends at once.

  Whiskey's executor issues AI moves with the order delay (`ShouldUseOrderDelayForExecutorWaypoint`), so this is the delay its own orders meet.
- **requirement-strike-time** — Each body's time to strike if ordered now is `T = courier + march (+ HeldCrossingHours per Held crossing on its route) + DeployHours`. `DeployHours` = `processingtimegroupstandard` + `EnvelopmentManeuver.TurnHoldHours`: the Turn phase BC11 already runs before the Attack.
  - **The fix's own time:** `T_main` is the latest fixing member's `T`. A main body already in contact (any fixing member with a perceived enemy within `EnvelopmentManeuver.ContactMeters`) has `T_main` = 0.
  - **The common plan:** the fix makes contact at `C* = max(T_main, max over enveloping bodies (T_j − offset_j))`, where `offset_j` is the rect's `StartTimeOffsetSeconds` in hours:
    - MobileFlankMarch 900 s, WideFlankApproach 600 s, ManeuverAndFix 300 s;
    - 0 for DoubleEnvelopment's wings and CavalryRaid's DeepRaid;
    - with no fixing member assigned, `T_main` is omitted.
  - **Each enveloping body strikes at** `S*_j = C* + offset_j`. The main body's strike is `S*_main = C*`.
  - **Releases:** `R_j = S*_j − T_j` for each enveloping body, and `R_m = C* − T_m` for **each** fixing member `m` (so no faster member strikes alone). All are ≥ 0, in hours from the evaluation. A body released at its `R` strikes on time if the estimates hold:
    - the faster wing waits for the slower;
    - a long flank march leaves before the fix moves;
    - the fix waits rather than going in alone. That is the Mechanicsville lesson (§ 3): when the turning column is late, the frontal attack holds.
  - **A fix in contact is not held:** a fixing member in contact has release 0 and no planned release is published for it. `C*` is then the wings' own pace (`max (T_j − offset_j)`), and the fix's exposure is what requirement-detail-risk measures.
  - **Bodies already moving:** a body whose child has left its Hold has `T` measured from now: march from its current position plus `HeldCrossingHours` for each Held crossing not yet passed, plus `DeployHours`, and no courier hours, because vanilla charges the order delay once, when the order is issued. A body still in its Hold has `T` measured from its planned release.
  - **Several rivers:** a route may cross more than one river. `HeldCrossingHours` is added once per Held crossing. `contested_j` takes the worst state over all of them, and the monitor watches every one. The via (requirement-crossing-via) is the **first** crossing on the route; the pathfinder takes the legs after it.
  - **Absolute hours:** a release is published as absolute doctrine hours, `PlannedReleaseHours = EvaluatedHours + R`.

  BC11 uses the planned releases (requirement-synchronized-release).
- **requirement-daylight** — `DaylightHours = dayendhours[month − 1] − hourCounter` (vanilla's own test, `uniStormSystem`), read by the runtime each evaluation. A body whose strike `S*` exceeds `DaylightHours − DaylightMarginHours` (0.25) gets `daylight_j` = 1 and the `after-dark` reason. Its verdict stays `Risky`: a night intervenes, and the night replan (`ReplanForNewBattleDay`) re-evaluates on the next day (question-after-dark).
- **requirement-enemy-picture** — The enemy as the side perceives it, built once per evaluation:
  - **Samples:** observations that are Visible, or Remembered at confidence ≥ 0.5; not reinforcements; infantry, cavalry or artillery. This is BC13's `requirement-enemy-samples`, and it is shared, not copied.
  - **Perceived strength** `P` = Σ Visible strength + Σ Remembered strength × confidence. **Perceived morale** `m_E` = the strength-weighted mean of the samples' `Morale` (last seen while Visible), or 1.0 with no samples.
  - **Effective strength:** `N = max(P, ledger EstimatedEnemyStrength) × (1 + FogPremium)`, with `FogPremium = 0.5 × (1 − confidence) × (0.5 + caution01)`. Here `confidence` is the contact ledger's `Confidence`, and `caution01` is the army commander's BC13 entry. A cautious commander assumes more of the unseen. Perryville is the weight (§ 3).
  - **The enemy mass:** the strength-weighted centroid of the samples.
  - **The local enemy** per own body, `L_j`: the samples within `OddsRadiusMeters` (BC13, 300 units) of the body's strike point (requirement-bodies), scaled by `N / P` when `P > 0`.
  - **No samples:** the enemy mass is BC10's assumed enemy point (`DeploymentDoctrine.AssumedEnemyPoint`, the nearest enemy-owned entry point), and `L_j = N / (bodies + 1)`.
- **requirement-shift-time** — The enemy's shift time to body `j` is `τ_j = EnemyCourierHours + march hours`.
  - **The march:** from the enemy mass (or, for a body whose strike point lies beyond the enemy's line end, from the perceived enemy line end on that side, `EnvelopmentManeuver.EnemyLineEnd`) to the body's strike point. It runs over the same crossing graph with the same leg cost, as infantry, base 1, unfatigued. When both points lie in one region the enemy crosses nothing.
  - **No delay at its own crossings:** a crossing the enemy itself holds costs it no `HeldCrossingHours`, so interior lines across the enemy's own bridges are counted.
  - **`EnemyCourierHours`:** `processingtimegroupstandard` + `processingtimegrouproute` + the ride from the enemy mass to the strike point, priced as requirement-courier-time's ride. The enemy commander's position is not perceived, and his chain is assumed one relay deep.
  - **A perceived passive enemy** (intent `Defend` from `ArmyIntentInference`, confidence ≥ its `ConfidenceFloor`) shifts slower: `τ_j × PassiveShiftFactor` (1.5). Hooker at Chancellorsville is the weight (§ 3).
- **requirement-detail-risk** — Per body `j` of a template with enveloping bodies (requirement-bodies):
  1. **Pinned enemy:** `Pin_j` = Σ over the other bodies `k` with `S*_k ≤ S*_j` of `S_pin,k / DefenseRatio_k`.
     - `S_pin,k` is an enveloping body's strength, or the main body's fixing members' strength (never its reserve members).
     - `DefenseRatio_k = BaseDefenseRatio (1.5) + TerrainStrength` at `k`'s strike point on `k`'s approach axis (requirement-bodies; 1.5 when the picture is not ready). The enemy holds against superior numbers.
     - A body that has not struck yet pins nothing: support out of earshot is not support (§ 3, Perryville).
  2. **Exposure window:** `W_j = (latest S* over the other bodies − S*_j)⁺ + σ_j`. `σ_j` is the **own support time**: the least, over the other bodies `k`, of `k`'s courier hours plus its march hours over the crossing graph (Held crossings at `HeldCrossingHours`) from where `k` stands when it has struck to `j`'s strike point.
     - Where `k` stands: for an enveloping body, its strike point; for the main body, the anchor of each fixing member, the nearest counting. A fix holds where it stands; it does not stand on the enemy.
     - With no other body, `σ_j` = +∞ (no support comes).
     - The window and `τ_j` are Jomini's comparison (§ 3, Art. XXI): who can bring the masses to the point first. A body supported over shorter lines than the enemy's shift is safe; one on exterior lines is not.
  3. **Enemy against `j`:** `E_j = L_j + shiftShare_j × max(0, N − Pin_j − L_j)`, with `shiftShare_j = clamp01((W_j − τ_j) / max(τ_j, 0.1))`. A shift that cannot arrive inside the window adds nothing; one with twice the time adds all.
  4. **Isolated odds:** `odds_j = (S_j × m_j × own_j) / (E_j × m_E)`, BC13's local-odds form with morale on both sides. `m_j` excludes player-owned regiments, as `S_j` does.
     - `S_j` is the body's strength (requirement-player-share) and `m_j` its strength-weighted mean `GroupMorale`.
     - `own_j` is 1 for an enveloping body, which attacks.
     - `own_j` is `BaseDefenseRatio + TerrainStrength` for the main body, which fixes and holds. It is taken at the anchor of its highest-priority fixing member (the Center when filled), on the axis toward the enemy anchor.
     - With `E_j = 0` the odds are `EnemyVisibleSector.MaxKnowableOdds` (10).
  5. **The commander's bar:** `Bar = clamp(1.0 + 0.3 × (caution01 − 0.5) − 0.3 × (audacity01 − 0.5) − 0.2 × (agg01 − 0.5), 0.6, 1.4)` (the army commander's BC13 entry).
  6. **The detail penalty:** `detail_j = clamp01((Bar − odds_j) / (Bar × DetailSlope))`, `DetailSlope` = 0.25. Odds a quarter below the bar give the full penalty.

  A template whose fixing rect is unfilled has no fixing force: its wings pin each other (by the formula above), and nothing else is pinned (requirement-hypothetical-layout).
- **requirement-route-exposure** — For an enveloping body only: the share of its route samples, outside its last `EnvelopmentManeuver.ContactMeters` (250 units), that lie within a perceived enemy sample's own reach (the `RangeOnlyReach` of requirement-crossing-state: where that regiment would see the column). Forest (14) samples count at half weight, because woods screen a column. It gives `exposure_j` in [0, 1]. A flank march seen on its way can be met. Chancellorsville is the weight: Jackson's column was seen and the warning was ignored (§ 3). The main body's route ends in contact by design and carries no exposure term.
- **requirement-held-crossing** — A route through a Held crossing costs time and blood:
  - **Time:** `HeldCrossingHours` = 1.0 is added to the edge through it in the Dijkstra (requirement-leg-cost) and to the body's `T` (requirement-strike-time). It is the time to carry the crossing. The Dijkstra therefore prefers any Clear or Unknown alternative that is less than an hour's march longer.
  - **Blood:** `contested_j` is 1 for a body whose route still uses a Held crossing, 0.5 for a Watched one, 0.25 for an Unknown one and 0 for a Clear one. Antietam's Rohrbach Bridge and Snavely's Ford are the weights (§ 3).
  - **Not impossible:** a route whose crossings are all Held is costly, not impossible. Burnside's bridge was carried.
- **requirement-approach** — Per body, at its strike point and on its approach axis (requirement-bodies):
  - `approach_j = 0.5 × TerrainPicture.ApproachDifficulty + 0.5 × enemyGround`;
  - `enemyGround` is the `TerrainStrength` at the strike point on the approach axis when perceived enemy samples stand within `OddsRadiusMeters` of it, and 0 otherwise;
  - an enveloping body whose strike point (the enemy line end) `TacticalTerrainLinePlanner.FlankAnchored` reports anchored, outward along the front axis, adds `AnchoredEndPenalty` (0.5) to `approach_j`, clamped to 1. A turned flank is no flank if it rests on a river or a crest.

  With the picture not ready, `approach_j` is 0.
- **requirement-player-share** — A body's strength `S_j` excludes the strength of `PlayerOwned` units in its subtree (BC3). The AI cannot count on the W&L player to march where it plans, and the evaluation never assigns a task to a player-owned unit. A child whose whole subtree is player-owned is not offered to the hypothetical layout.
- **requirement-penalty** — Per body: `penalty_j = 1 − (1 − daylight_j)(1 − detail_j)(1 − 0.6 × contested_j)(1 − 0.5 × approach_j)(1 − 0.4 × exposure_j)`, with each term defined above.
  - The main body's `contested`, `approach` and `daylight` are taken per fixing member, the worst member counting.
  - The time a plan takes is not a cost by itself: the synchronized releases make a long march wait-free for the others (requirement-strike-time). Its costs are the night (`daylight`), the enemy's chance to shift (`detail`) and the chance to be seen (`exposure`).

  The candidate's penalty is the **worst body's** penalty: a plan fails at its weakest body. The reason is the verdict reason, or else the name of the largest term above `ReasonThreshold` = 0.2 (`after-dark`, `detail`, `contested`, `approach`, `exposure`).
- **requirement-verdict** — Each candidate (with its side) gets one verdict. The first match wins:
  1. no valid front axis or no direct children → `Unknown`, reason `no-axis` / `no-children`;
  2. the map unknown → `Unknown`, reason `map-unknown`;
  3. `wing-unfilled`, then `no-crossing`, then `river-on-leg`, then `no-route` → `Impossible`;
  4. otherwise `Risky` or `Feasible` from the penalty.

  The verdicts mean:
  - **`Impossible`** (only the reasons of step 3): it carries penalty 1.0 for ranking (requirement-candidate-set, requirement-never-empty).
  - **`Unknown`**: the penalty is 0.
  - **`Risky`**: the penalty is at least `RiskyPenalty` = 0.35.
  - **`Feasible`**: otherwise.
- **requirement-evaluation-cadence** — The ledger re-evaluates every candidate at most every `EvaluateIntervalHours` = 0.05 doctrine-h, on the coordinator's heavy path, before `ArmyTickCycle.MaybeReplan`. It also re-evaluates at once before the initial pick and before the night replan. At battle start the initial pick's evaluation is normally `Unknown` (no direct children attached yet, the picture not ready), and requirement-assumption-monitor's `map-known` screens that pick once the map is known. The selected plan is re-evaluated on every new heavy snapshot (`_snapshotGeneration`) for the monitor. A replan always reads a ledger no older than `EvaluateIntervalHours` (requirement-runtime-wiring).

---

## 5. (B) Selection and replanning

- **requirement-stance-before-pick** (bug fix, outside the flag) — The stance and the opportunity flags are refreshed before every plan pick: `ArmyPostureInputsRuntime.Refresh`'s stance and opportunity writes run before `PickInitialPlan` and before `MaybeReplan` in the same tick. The initial pick therefore applies the stance filter (a Defender never opens with an offensive playbook), and a replan sees this tick's stance. This is a defect in today's ordering (§ 2.2), and it is fixed because the evaluation depends on the stance.
- **requirement-selection-term** — `PlaybookContext` gains `IFeasibilityLookup Feasibility` (null with the flag off). In `Select`, after the jitter draw and after the crest check (so the jitter stream does not depend on feasibility, the BC12 lesson):
  - **`Impossible`:** the candidate is skipped and counted (`feasibilityFiltered`).
  - **Otherwise:** the score gains `− FeasibilityWeight × penalty`, with `FeasibilityWeight` = 0.4 (question-feasibility-weight). It is 0 for `Feasible` with no penalty and for `Unknown`.

  The term only subtracts. A sound plan is scored exactly as today, so the existing selection fixtures without terrain and routes stay unchanged (criterion-selection-regression).
- **requirement-never-empty** — The feasibility filter never empties the catalog:
  - The single-body Neutral templates (MeetingEngagement, GenericMethodical) cannot be `wing-unfilled`. Their main body only fails `no-crossing` / `no-route`.
  - If every candidate is `Impossible`, the filter is lifted for that selection. Every candidate is then scored with its penalty, the row records `allImpossible=true`, and OnceLog `tactical-battle-core:feasibility-all-impossible` fires.
  - With the stance filter, at least the two Neutral playbooks always reach this step.
- **requirement-selected-side** — The winner's evaluated side (`Mirrored`) is published with the plan. When it is set, the play executor projects the template mirrored from activation.
  - **The projection:** `PlayExecutorRuntime.ComputeZoneAssignments` projects the template mirrored, in the same projection frame the evaluation used, so the zone assignment gives the rects to the children the evaluation timed. `ManeuverSpec` carries `ProjectedMirrored`.
  - **The fallback:** BC11's own one mirrored try (`Mirrored`, false at the start) then flips relative to that projection, and so falls back once to the authored side.
  - **After a flip:** the ledger's planned release, time budget and via for that child no longer apply, because they were computed for the other side. BC11's own rule governs the child from then on (no planned release, no time budget, no via), until the next selection.
- **requirement-assumption-monitor** — The selected plan is re-evaluated on every new heavy snapshot. `map-known` applies to every selected plan; the other reasons apply while the plan has enveloping bodies. A **broken assumption** is any of these, for a body (an enveloping body, or a fixing member of the main body) not yet in BC11's Turn or Attack phase or, for a fix, not yet `Released`:
  - `crossing-held`: a crossing on the body's chosen route turned Held, and the body has not passed that crossing (per crossing: the child stands in its far region);
  - `route-missing`: the NavMesh confirmation of a leg returned no path, with no alternative crossing;
  - `late`: the body's strike estimate exceeds its desired strike `S*` (recorded at selection) by more than `LateHours`;
  - `detail`: the body's detail penalty rose above `RiskyPenalty`, and by at least `ReplanPenaltyMargin` = 0.3 above its value at selection;
  - `wing-abandoned`: BC11 abandoned an enveloping body (any reason) while another enveloping body or the main body still depends on it. A template with two enveloping bodies, or a fixing member whose planned release is still ahead or which is still in its start-delay Hold, depends on it.
  - `map-known`: the plan was selected while its verdict was `Unknown` (the picture or the regions not yet ready, typically at the initial pick), and a known verdict has now appeared. It fires once per selection. That way the battle's first pick is screened as soon as the map is known.

  Several in one tick: the first in the order above wins.

  **The facts compared:** the monitor compares the selection snapshot with the current evaluation through `MonitorFacts`:
  - per body: its child id, BC11 phase, the crossings passed (the child's region equals the far region of each crossing on its route), its planned release (absolute);
  - the selection's and the current evaluation's absolute hours.

  `BodyEstimate` carries `ChildId` (the main body: its highest-priority fixing member), and bodies are matched by it.
- **requirement-assumption-replan** — A broken assumption raises the replan trigger `ManeuverAssumptionBroken` (appended to `ArmyReplanTriggers`) with its reason, under the existing `Min Replan Seconds` cooldown:
  - **Hysteresis (this trigger only):** `PlaybookContext` gains `HysteresisPlan` and `HysteresisMargin`. `Select` scores the current plan in the same pass and keeps it unless:
    - it is absent from the candidates or `Impossible`; or
    - the best candidate's final score beats it by `ReplanScoreMargin` = 0.1.

    A kept plan stamps the cooldown, refreshes the selection snapshot, keeps its side and phase, and does not reset the plan's age.
  - **Suppression is named:** a broken assumption that does not replan writes its reason on the `TacticalManeuverBody` row: `cooldown`, `committed`, `margin` (kept by the hysteresis), or `preempted` (an earlier trigger in `ArmyReplanTriggers.Evaluate` order replanned this tick; that replan re-reads the ledger anyway). `ArmyTickCycle.MaybeReplan` returns the suppression literal for the row.
  - **Committed wings:** no `ManeuverAssumptionBroken` fires while every enveloping body is in Turn or Attack. The approach assumptions are spent.
  - **The rest:** the replan keeps the phase and the main-effort sector (`Replan` today). BC11 abandons the old maneuvers on the plan change (`plan-changed`), and the children return to their roles (§ 9 migration).

---

## 6. (C) The scenarios as emergent results

None of these is a branch in the code. Each is what requirement-strike-time, requirement-detail-risk, requirement-held-crossing and requirement-selection-term yield on its inputs. The numbers below are the harness fixtures of § 13 (scenario S6).

**Fixture conventions (scenarios 1-3).**
- **The field:** a river-free map; the picture is ready, with no cover and no crests, so `TerrainStrength` is 0 and `DefenseRatio` is 1.5.
- **Own side:** the centroid is at depth 0. Three corps (two in scenario 3) stand at lateral −300, 0 and +300 (−300 and +300). Infantry (base 1), on grass (cost 2), fresh, morale 1.0.
- **The enemy:** perceived at depth 600 in three equal clusters at lateral −450, 0 and +450, morale 1.0, at confidence 0.8. Its intent is not `Defend`.
- **The commander:** mid (agg01, caution01 and audacity01 all 0.5), so `Bar` = 1.0 and `FogPremium` = 0.5 × 0.2 × 1.0 = 0.1.
- **The rest:** stance Attacker; June, 10:00 (10 h of daylight); cavalry fraction 0; terrain Open; `CurrentOdds` = own / perceived.
- **Routes and courier legs:** set by the fixture so that every child's courier time is 0.32 h and the march lengths are the ones stated. The plan builds the grid and HQ positions to reproduce them.
- **The assertions:** the verdicts, and each `detail` within ±0.05.

1. **A superior force converges (35,000 against 25,000).** Corps of 12,000 (left), 11,000 (middle) and 12,000 (right). `N` = 27,500; each enemy cluster is `L` ≈ 9,200 with the premium.
   - **ManeuverAndFix:** the layout gives the Center to the middle corps (11,000), the RightWing to the right corps (12,000, offset 300 s) and the Demonstration to the left corps (12,000).
   - **Times:**
     - The wing marches 555 units: `T_wing` ≈ 0.32 + 1.11 + 0.07 = 1.50 h.
     - The fix marches 200 units: `T_main` ≈ 0.32 + 0.40 + 0.07 = 0.79 h.
     - `C*` = max(0.79, 1.50 − 0.083) = 1.42 and `S*_wing` = 1.50.
     - The wing leaves at once. The fix waits 0.63 h, then moves, so the two strike 300 s apart and the fix never stands alone for 0.7 h.
   - **The wing's detail risk:**
     - The fix has struck by 1.50, so `Pin` = 23,000 / 1.5 ≈ 15,300.
     - Support: the nearest fixing anchor (the Center's, forward 180) is 615 units from the right line end, so `σ` = 0.32 + 1.23 = 1.55 and `W` = 1.55.
     - The enemy's shift from its mass to that end is 450 units: `τ` = 0.22 + 0.90 = 1.12. So `shiftShare` = (1.55 − 1.12)/1.12 = 0.38.
     - `E` = 9,200 + 0.38 × (27,500 − 15,300 − 9,200) ≈ 10,300 and `odds` = 12,000 / 10,300 = 1.17 ≥ `Bar`: `detail` 0.
   - **The fix's detail risk:** no wing has struck by `C*`, so `Pin` = 0. The enemy is at its strike point (`τ` = 0.04), so `E` = `N`. `odds` = 23,000 × 1.5 / 27,500 = 1.25: `detail` 0.
   - **Verdict:** with the wing's route beyond the enemy's reach until its last 250 units (`exposure` 0) and no crossing, `Feasible`. Today's `EnvelopmentPressure` bias (+0.2 × affinity, at AttackNow with a clear advantage) then selects a dividing play when the commander's personality allows.
   - **DoubleEnvelopment:** wings 12,000 each, Center 11,000, every `T` as above, so `C*` = `S*` = 1.50 for all bodies.
     - Each wing: `Pin` = 11,000/1.5 + 12,000/1.5 ≈ 15,300, `σ` = 1.55 and `odds` 1.17.
     - The fix: `Pin` = 16,000, `E` = 11,500 and `odds` = 16,500 / 11,500 = 1.43.
     - `Feasible`.
2. **The same army attacks piecemeal.** DoubleEnvelopment as in 1, but the left wing's route crosses a creek and a swamp: `T_left` = 2.4 h, `T_right` = 1.2 h, `T_main` = 0.79 h.
   - **With synchronized release:** every body strikes at 2.4 (the right wing waits 1.2 h, the fix 1.61 h). The numbers are those of 1, and every `detail` is 0.
   - **With every body released at once** (no BC14 release): the fix strikes at 0.79, the right wing at 1.2, the left at 2.4.
     - The right wing: `W` = (2.4 − 1.2) + 1.55 = 2.75 > 2 × `τ`, so `shiftShare` = 1. Only the fix has struck (`Pin` 7,300), so `E` = 27,500 − 7,300 ≈ 20,200. `odds` = 0.59 and `detail` = 1.
     - The fix alone: `E` = 27,500 and `odds` = 16,500 / 27,500 = 0.60, so `detail` = 1.
   - That is Antietam's piecemeal attack in numbers, and why requirement-synchronized-release exists.
3. **Even odds (25,000 against 25,000), two corps of 12,500.** `N` = 27,500.
   - **DoubleEnvelopment:** both corps are wings and no child is left for the Center.
     - Each wing's only support is the other wing, from the far line end: `σ` = 0.32 + 1.80 = 2.12.
     - `shiftShare` = (2.12 − 1.12)/1.12 = 0.89. With the other wing pinning 8,300, `E` = 9,200 + 0.89 × (27,500 − 8,300 − 9,200) ≈ 18,100.
     - `odds` = 0.69, so `detail` = 1 and the score term is −0.4.
     - The wings stand on exterior lines, and the enemy's centre reaches either before the other wing can: Jomini's "fatal error" in numbers.
   - **ManeuverAndFix, MobileFlankMarch and WideFlankApproach:** the Center alone fixes, so its `odds` = 12,500 × 1.5 / 27,500 = 0.68: `detail` 1, a term of −0.4.
   - **The single-body plays** (MeetingEngagement, GenericMethodical, HesitatedCommit, ContinuousAttrition, FrontalShockAssault, ForcedFrontalAttack, GenericAggressive, GenericDesperate) carry no detail term. They win unless the commander's personality fit outweighs the penalty.
   - The army then opens in its Probe phase. BC9's reconnaissance in force nominates a probe per empty band: **fix and probe**.
4. **A held crossing with an unwatched alternative (35,000 against 35,000, Perryville-like).** The fixture:
   - **Own side:** three corps of 12,000 (left, lateral −300), 11,000 (middle, 0) and 12,000 (right, +300) at depth 0.
   - **The river:** open, across the front on grid depths [300, 320), aligned with the picture's 20-unit rows. Bridge B at lateral 0 is Held by a Visible 1,500-man brigade at depth 380 facing it. Ford F at lateral −900 is Clear (probed 0.1 h ago, no perceived regiment within reach).
   - **The enemy:** three perceived clusters of 11,000, 11,500 and 11,000 at lateral −400, 0 and +400, depth 620; extent ±450. With the brigade, the enemy anchor is at depth ≈ 610 and `P` = 35,000. The ledger estimate is 35,000, so `N` = 38,500. Its intent is not `Defend`.
   - **The rest:** the scenario 1-3 conventions otherwise, AllianceId 0 (jitter seed 7).
   - **At even odds:**
     - **FrontalShockAssault and MeetingEngagement:** their Center rects (forward 0.5) lie beyond the river, and the cheapest route crosses at B (+1.0 h against a 1,850-unit detour through F, 3.7 h). `contested` = 1, so the penalty is ≥ 0.6.
     - **WideFlankApproach, authored on the right:** the FlankMarch route crosses at B, `contested` = 1, penalty ≥ 0.6.
     - **WideFlankApproach mirrored to the left:**
       - The wing routes through F, `contested` = 0.
       - Its support must re-cross the river (σ ≈ 2.5 h > 2τ ≈ 2.2), so the whole free enemy can reach it. The enemy holds the river against the Center with 11,000 / 1.9 ≈ 5,800 (`DefenseRatio` 1.9: BC12's water term at the enemy anchor). So `E` ≈ 32,700, `odds` ≈ 0.37 and `detail` = 1.
       - Its own fix (Center and Reserve on their own bank, `own` 1.5: the river lies within 100 units of the Center's anchor, outside BC12's 100-300-unit water strip) has `odds` ≈ 0.90 and `detail` ≈ 0.42.
       - Both sides score 1.0, and the authored side is kept by the tie rule. It is not selected either way.
     - **The bank-holding single-body plays** (GenericAggressive, GenericMethodical, HesitatedCommit, ContinuousAttrition, whose rects stand at forward 0.4 or less, short of the river) cross nothing. Their penalty is half the approach term, ≈ 0.1 (the river in front of the enemy anchor). One of them wins.
     - **So, at even odds,** the AI never dives at the held bridge and does not throw one corps over the ford against an equal enemy. It closes to its own bank, probes, and looks at the crossings (requirement-crossing-look).
   - **At 2:1** (corps of 24,000, 22,000 and 24,000 against the same 35,000; `EnvelopmentPressure` on):
     - **ManeuverAndFix mirrored:**
       - The Center and the Demonstration pin 46,000 / 1.9 ≈ 24,200 of the enemy along the river.
       - The wing (24,000) crosses at F. Its `odds` ≈ 1.68: `detail` 0, `contested` 0, penalty ≈ 0.1.
       - It is selected for a mid commander. The Center fixes B by fire, and the wing marches with F as its via point (requirement-crossing-via): **pin and cross**. It emerges when the army can afford a separate crossing: Jomini's "vastly preponderating" condition, and the Fredericksburg-downstream / Antietam-ford pattern of § 3.
     - **DoubleEnvelopment:** its wings' anchors stand on the own bank, but their inward legs to the enemy line ends cross the river. The inward-leg screen (requirement-route-screen) routes them through B (`contested` 1) or the long way round, so the penalty is ≥ 0.6. It is never launched into BC11's `river-on-leg` abandon.
     - **WideFlankApproach mirrored:** only the Center pins (22,000 / 1.9 ≈ 11,600), so the wing's `odds` ≈ 0.89 and penalty ≈ 0.5. That is still below its authored side and FrontalShockAssault (≥ 0.6).
   - **If F were Unknown instead:** `contested` = 0.25, and F becomes a look request (requirement-crossing-look): the Snavely's Ford lesson.
5. **The Defender's options.** A Defender (vanilla battle type 2) is never offered an offensive candidate (the stance filter, now applied at the initial pick by requirement-stance-before-pick). Its candidates are the five Defensive and two Neutral playbooks, all single-body, so the attacker's detail and contested terms do not discriminate between them. Three things act for it:
   - **Turning exposure** (requirement-defender-exposure): the evaluation is run from the enemy's side against the Defender's line. Every link crossing outside the Defender's own covered set is timed: the perceived enemy's march to it, against the Defender's shift from its reserve rect. A line that can be turned before it can be shifted is penalized.
   - **Look requests** for Unknown crossings within the enemy's reach (requirement-crossing-look).
   - **A reserve released to a threatened crossing** (requirement-defender-crossing-release), the interior-lines answer Lee used at Antietam.

   Nothing in it is offensive. BC13's bounded counterstroke stays the Defender's only attack.

---

## 7. (D) Interaction with BC11 envelopment and BC13 opportunity

- **requirement-synchronized-release** — Releases are published only for the bodies of a template with an enveloping rect (single-body templates keep today's behaviour). The new fields carry a flag beside each value, because `EnvelopmentManeuver.Step` resets the state with `default(ManeuverState)`, where 0 is a valid hour:
  - `ManeuverInput` gains `HasPlannedRelease` and `PlannedReleaseHours` (absolute doctrine hours, `EvaluatedHours + R`).
  - `ManeuverState` gains `HasFirstPlannedRelease` and `FirstPlannedReleaseHours`, latched the first time the Hold sees a planned release.
  - `ManeuverState` gains `HasBudget` and `BudgetHours`, `HasVia` and `ViaX`/`ViaZ`, latched at the March transition.

  The rules:
  - **When set:** BC11's Hold keeps today's gate order:
    1. the time test: `NowHours ≥ PlannedReleaseHours` replaces both of today's waits (`await-center-contact` and `hold-offset`). A long march may leave before the fix has moved;
    2. the route-known test;
    3. the checks.

    `ProceedReason` = `planned-release`.
  - **Source:** the runtime fills it from the ledger's body release (requirement-strike-time), recomputed every heavy tick until the child leaves Hold. A release is computed from now and the child's current position. The monitor's `late` test is what measures from the planned release (requirement-strike-time). After the fix's actual contact, its `T` is 0 and `C*` follows.
  - **The Hold ceiling** with a planned release: `max(PlayActivatedHours + MaxManeuverHours, FirstPlannedReleaseHours + MaxManeuverHours)`.
    - It is never earlier than an hour past the release first planned.
    - It is anchored to the latched value, so a release that keeps receding (a fix that stalls) still ends.
    - Without a planned release, today's ceiling stands.
  - **After release:** a released child is never held back again.
  - **Unset:** with no ledger result (flag off, map unknown), BC11's rule stands unchanged.
  - **The fix's hold:** a main-body fixing child with a release in the future (`R_main` > 0) is held through BC11's start-delay path (`StepStartDelay`). Task FormUp at its own position until its `PlannedReleaseHours`, then `Released` to its allocator role.
    - A fix in contact is never held.
    - Neither is a child BC10 placed as a deployed reserve, or one with no ledger entry.
    - The fixing child's `ManeuverSpec` is created for this by the zone assignment (`DirectChildAllocator.ManeuverOf`), as a start-delay spec with no envelopment and a `HoldsForRelease` mark, the HesitatedCommit precedent. It applies only to the fixing rects of a template with an enveloping rect.
    - The fix's hold has the wings' ceiling: `FirstPlannedReleaseHours` + `MaxManeuverHours`, latched the same way. A fix never waits for good on a stalled wing.
- **requirement-maneuver-time-budget** — `ManeuverInput` gains `HasTimeBudget` and `TimeBudgetHours`, latched into `ManeuverState.BudgetHours` (`HasBudget`) at the March transition. When it is set, the `timeout` abandon counts it from `ActiveSinceHours` in place of `MaxManeuverHours`:
  - `TimeBudgetHours = Min(Max(1.5 × (courier + march + HeldCrossingHours × held crossings) + EnvelopmentManeuver.TurnHoldHours, MaxManeuverHours), Max(DaylightHours, EnvelopmentManeuver.TurnHoldHours))`.
  - It is never below today's 1.0 h, except when less daylight than that remains; then the day's end wins.
  - Today's 1.0 h is 500 units of grass for infantry, shorter than many rect anchors' routes (§ 2.2).
- **requirement-crossing-via** — A body whose chosen route crosses a river carries the chosen crossing to BC11:
  - **The order:** `ManeuverOrder` and the doctrine order (`CommandDoctrineOrder.ViaX`, `ViaZ`, additive; NaN none) carry the crossing point for the March. The posture pipeline puts it into `BattleOrderIntent` as `via0` (BC10's `ViaCount` = 1). The executor validates it and issues the two-leg `SetWaypoint` exactly as for a road route (BC10 decision 6).
  - **No road route:** a maneuver March with a crossing via gets no road route on top of it.
  - **No `AvoidCoveredCrossings`:** the via already avoids the held crossings. That avoids BC10's "a covered-crossing move takes no via points" rule, which would drop it.
  - **Which leg:** the via rides on the leg it lies on. For a DeepRaid, that is the outflank leg or the anchor leg, whichever the route crosses on.
  - **BC11's route check:** it plans the march leg as two half-queries (child → via, via → anchor), one per step on its own one-per-step budget. The leg has a result when both halves have one.
  - **Dropped once crossed:** once the child stands in the via's far region (`RiverRegions.RegionAt`), the March order carries no via, so an order re-issued each step never walks it back to the crossing.
  - **When the via turns Held:** the assumption monitor fires `crossing-held` (requirement-assumption-monitor).
  - **Lanes:** the via applies to the child node and its lane nodes (`ManeuverNodeTable`), all of which pass the same crossing.
  - **Pinned at the March:** the via is latched into `ManeuverState` at the March transition and does not change for that leg. A mirror flip (requirement-selected-side) drops it.
- **requirement-wing-abandon-replan** — BC11's abandons reach the army. `PlayExecutorRuntime` queues the side's abandoned enveloping children (child id and reason) in its own per-side queue. The heavy path drains the queue into `MonitorFacts` on its next run, one tick later, because `DriveTickCycle` runs before `DriveDirectChildCycle`. The monitor reads them as `wing-abandoned` (requirement-assumption-monitor). The ledger keeps its one writer, and no new BC11 abandon reason is added.
- **requirement-bc13-commitment** — The opportunity layer and the maneuver layer stay mutually exclusive, as BC13 defined it:
  - A child that is itself a live exploit asset is not offered to the hypothetical layout (requirement-hypothetical-layout).
  - A body in Hold, March or Turn is a BC11 maneuver phase, which BC13's asset eligibility already refuses.
  - **One addition:** a child with an exploiting descendant counts at half its strength in `Pin` and in `S_j`, because that asset is away from its slot for up to `MaxExploitHours`.
  - The evaluation never writes the `ExploitTable` or the `CommitmentLedger`.
- **requirement-bc13-personality** — Every personality input of the evaluation comes from the army commander's entry of BC13's `CommanderPersonalityTable` (`agg01`, `caution01`, `audacity01`; `To01` of the vector). With `Enable Per-Commander Personality` off, it comes from the roster's army vector, as BC13 defines its own off state. Wing commanders' own vectors are not read (question-wing-commander-friction).
- **requirement-crossing-look** — The ledger publishes up to `MaxLookRequests` = 2 crossing look points per side. These are Unknown link crossings that are, in priority order:
  1. on the selected plan's chosen routes;
  2. on the top confirm candidates' routes;
  3. for a Defender, within the perceived enemy's reach, earliest enemy arrival first.

  BC9's recon producer gains a target kind `Crossing`, after its stale-entry-point and stale-band targets and inside its existing cap of 4 starts per side per doctrine hour. One cavalry regiment rides to the crossing's stand point (150 units short, BC9's rule), observes and returns. A crossing look never sends a regiment toward a visible enemy at least as strong (BC9's rule). A request expires when the crossing becomes Clear, Watched or Held.
- **requirement-defender-exposure** — For a Defender, each Defensive or Neutral candidate gets a turning-exposure term from the same graph, run from the enemy's side:
  - **The crossings counted:** every link crossing that is:
    - not covered by an own regiment, by the own-side form of `CoveredCrossings.Covers` (the regiment's `RotationY` and `FireRange`);
    - within the enemy's reach, meaning its arrival there comes before `DaylightHours`;
    - on the Defender's flank, meaning outside the own line's lateral extent or within one frontage of one of its ends.
  - **`enemyArrival`:** the shift-time model from the perceived enemy mass to the crossing, plus the crossing's legs to the flank end of the candidate's projected line. The flank end is the outermost projected rect anchor on the crossing's side.
  - **`ownShift`:** the courier and march time to the same crossing, from the candidate's Reserve or ReserveLine rect anchor. When that rect is unfilled, from the nearest assigned rect anchor.
  - `turn = clamp01((ownShift − enemyArrival) / LateHours)`, the worst crossing taken.
  - It enters `penalty` as `(1 − 0.6 × turn)`, in place of the attacker's `contested` term.
  - A Defender's verdict is never `Impossible` from this term.
- **requirement-defender-crossing-release** (should) — A Defender's allocator gains a fifth reserve release, `crossing-threat`. It fires when the perceived enemy has a body whose estimated arrival at an uncovered link crossing on the Defender's flank is within `ownShift + ThreatLeadHours` (0.25):
  - The nearest Reserve child is released to `GuardFlank` at that crossing (a holding task), one per crossing.
  - It respects BC13's reserve floor (it is not a crisis), and the row reason is `crossing-threat`.
  - It is a role change the posture producer executes, as BC11's flank release is.
  - Absent BC13's staggered release (flag off), it releases one child per crossing without a floor.

- **requirement-runtime-wiring** — Runtime details the plan must follow:
  - **Files.** Pure:
    - `Tactical/Battle/RiverRegions.cs` and `CrossingWatch.cs`;
    - `Tactical/Orchestrator/Plays/ManeuverFeasibility.cs` (the evaluation, the monitor and the types of § 8) and `FeasibilityLedger.cs`.

    Runtime:
    - `Tactical/Battle/RiverRegionsRuntime.cs` (stepped from `TerrainPictureRuntime.Step`);
    - `Tactical/Orchestrator/Plays/FeasibilityRuntime.cs` (`MarchRules`, `DaylightHours`, the confirm queue's NavMesh call, the per-side ledgers, the crossing probes).

    The via's path: `ManeuverNodeEntry` → `CommandDoctrineAssignment` (doctrine order) → `CommandPostureExecutor` / `PostureIntentProducer` (`via0`, no road route, no `AvoidCoveredCrossings`). The crossing look: `ReconIntentProducer` and `BattleRecon`.
  - **Order within the coordinator tick** (`DriveTacticalCommanderSide`):
    1. the pick inputs: `ArmyPostureInputsRuntime.Refresh` is split. `RefreshPickInputs` (stance, live initiative, reinforcement opportunity) runs first, here and in `AttachArmyIfActive` before `PickInitialPlan`. `RefreshPostureCaches` (AI pool, leaf map, which read this tick's intents) stays where `Refresh` runs today;
    2. the crossing probes and classification;
    3. the evaluation, when `now − EvaluatedHours ≥ EvaluateIntervalHours` (a cheap compare every tick; forced before the initial pick and the night replan);
    4. the monitor, on a new snapshot generation;
    5. `MaybeReplan`.
  - **The pure seam for the stance fix:** `TacticalBattleCoordinator.PickInitialPlanInOrder(army, refreshPickInputs, evaluate, evidence)`, in the partial the harness compiles (where `ReplanForNewBattleDay` lives), is the one place the order is written. FEAS-STANCE-INITIAL drives it with a recording fake.
  - **The ledger reaches `Select`:** `FeasibilityRuntime` pushes the side's lookup with `ArmyOrchestrator.SetFeasibility(IFeasibilityLookup)` (null with the flag off), the `SetManeuvers` precedent. `TryPickPlan` copies it into `PlaybookContext`.
  - **The replan:**
    - `ArmyTickCycle.MaybeReplan(..., string assumptionReason)` returns `ReplanOutcome { Trigger, Replanned, Kept, Suppression }`.
    - `ArmyOrchestrator.Replan(evidence, intent, trigger, hysteresis)` reports `kept`.
    - A kept plan stamps the cooldown and records a fresh replan baseline and opportunity snapshot, so edge triggers do not re-fire on an old edge. It keeps the plan, its side, its phase and its age.
    - `preempted` means a reason was supplied and an earlier trigger replanned. `cooldown` means the cooldown blocked whichever trigger fired.
  - **The selection snapshot:** `FeasibilityLedger.CaptureSelection()` copies the selected candidate's results into a pooled second buffer after every pick and kept plan. The monitor compares against it.
  - **Per-child values:** the ledger keeps a pooled per-child table for the selected candidate and side, refreshed at each evaluation: child id → planned release, time budget, via, predicted arrival (`S* − DeployHours`). It covers every fixing member and every enveloping child. `TryPlannedRelease` reads it. BC11 latches the predicted arrival with the via and budget at the March, for the `TacticalEnvelopment` row.
  - **The fix's hold spec:** `CorpsAssignment` gains `TemplateEnvelops` (set by `PlayCorpsAssignment`). `ManeuverOf` returns a start-delay spec with `HoldsForRelease` for the Center and Demonstration rects when it is set. `StepStartDelay` tests `HoldsForRelease` before its offset gate, so a zero offset does not reset the latch. The exemptions (a fix in contact, a deployed reserve, no ledger entry) are resolved in `PlayExecutorRuntime.BuildInput` by leaving `HasPlannedRelease` false: the fix is released at once, today's behaviour.
  - **The mirrored fallback's frame:** a `ProjectedMirrored` spec also carries the authored anchor. BC11's one mirrored try for such a spec goes to that stored anchor, not to `MirrorAcrossAxis`, because the two frames (projection and front axis) need not coincide.
  - **Crossing threats:** the Defender evaluation writes a pooled threat list (crossing point, side, enemy arrival, own shift) into the ledger. `ArmyOrchestrator` reads it through the pushed lookup when it builds `ReserveReleaseContext.CrossingThreats`, and the allocator picks the nearest Reserve child.
---

## 8. Interfaces (pure unless marked runtime)

```csharp
namespace WhiskeyRealism.Tactical.Battle {
  public sealed class RiverRegions {                                   // per battle, BattleScope; built stepwise after TerrainPicture.Ready
    public const float CrossingLinkMeters = 60f; public const int MaxRegions = 64, MaxCrossings = 256;
    public bool Ready { get; } public bool Frozen { get; } public int RegionCount { get; } public int LinkCount { get; }
    public int RegionAt(float x, float z);                             // -1 on open river or outside the map
    public int LinkCrossing(int link, out float x, out float z, out int gridCrossingIndex, out int crossingId);   // ford 5 / bridge 6
    public int RegionsOfLink(int link, Span<int> regions);
    public bool Step(TerrainPicture picture, BattleTerrainGrid grid, long deadlineTicks, Func<long> clock);   // true when Ready
  }
  public enum CrossingState : byte { Unknown = 0, Clear = 1, Watched = 2, Held = 3 }
  public sealed class CrossingWatch {                                  // per side, BattleScope
    public const float HoldRadiusMeters = 300f, ClearMemoryHours = 0.25f; public const int MaxCrossingProbesPerTick = 16;
    public void Classify(BattleSnapshot s, RiverRegions r, BattleTerrainGrid g, IPerceivedGround perceived, ReadOnlySpan<float> reachByUnitType, double nowHours);   // RangeOnlyReach per enemy unittyp
    public CrossingState StateOf(int link); public float HeldStrength(int link); public double LastPerceivedHours(int link);
    public int CountBy(CrossingState state);
  }
  // CoveredCrossings.Covers(in EnemyObservation o, float cx, float cz): rule (a), shared by BuildPoints and CrossingWatch.
}
namespace WhiskeyRealism.Tactical.Orchestrator.Plays {
  public enum FeasibilityVerdict : byte { Unknown = 0, Feasible = 1, Risky = 2, Impossible = 3 }
  public struct MarchRules {                                           // GamePrefs, read once per battle by the runtime
    public float BasicUnitSpeed;              // basicunitspeed (1000)
    public float InfantryBase, CavalryMountedBase, ArtilleryLimberedBase, HorseArtilleryFactor;
    public float FatigueSpeedExponent;        // fatiguefactorspeed
    public float ProcessingHours, RouteProcessingHours, BugleHours, BugleRange, CourierSpeedPerHour;
    public int[] SpeedCost;                   // the grid's table (terrainspeccost)
  }
  public struct BodyEstimate {
    public int Body, ChildCount; public string ChildId; public bool Envelops, Main; public PlayRectKind Kind;
    public float Strength, Morale, TargetX, TargetZ;
    public float RouteCost; public bool RouteEstimated, RouteConfirmed; public int CrossingLink; public CrossingState Crossing;
    public float CourierHours, MarchHours, StrikeHours, DesiredStrikeHours, ReleaseHours, OffsetHours;
    public float EnemyShiftHours, ExposureWindowHours, LocalEnemy, EnemyAgainst, Pin, IsolatedOdds;
    public float Daylight, Detail, Contested, Approach, Exposure, Turn, Penalty, OwnRatio;   // OwnRatio: 1 for an enveloping body, BaseDefenseRatio + TerrainStrength for the main body
  }
  public struct CandidateResult {
    public BattlePlanId Plan; public bool Mirrored; public FeasibilityVerdict Verdict; public float Penalty;
    public string Reason;                     // static literal: the verdict reason or the largest term's name
    public int BodyStart, BodyCount;          // into the ledger's pooled BodyEstimate array
  }
  public interface IFeasibilityLookup { bool TryGet(BattlePlanId plan, out CandidateResult result); }
  public sealed class FeasibilityLedger : IFeasibilityLookup {          // per side, BattleScope; writer: the coordinator's heavy path only
    public const float EvaluateIntervalHours = 0.05f;
    public bool TryGet(BattlePlanId plan, out CandidateResult result);
    public ReadOnlySpan<BodyEstimate> Bodies(in CandidateResult r);
    public double EvaluatedHours { get; }
    public int LookRequests(Span<float> xs, Span<float> zs);           // requirement-crossing-look
    public bool TryPlannedRelease(string childId, out double releaseHours, out double timeBudgetHours, out float viaX, out float viaZ);
  }
  public struct FeasibilityInput {                                     // one evaluation; pooled arrays
    public BattleSnapshot Snapshot; public TacticalFrontAxis Axis; public BattleStance Stance;
    public BattleTerrainGrid Grid; public TerrainPicture Picture; public RiverRegions Regions; public CrossingWatch Crossings;
    public MarchRules Rules; public OpportunityPersonality Army;        // BC13's struct: Agg01, Caution01, Audacity01
    public float EnemyEstimate, Confidence; public TacticalIntentModel EnemyIntent;   // ArmyIntentInference.Build's result: intent and confidence
    public double NowHours, DaylightHours; public BattlePlanId CurrentPlan; public bool HasPlan;
    public IExploitLookup Exploits;                                    // BC13 ExploitTable.IsExploiting
    public IConfirmedLegs Legs;                                        // NavMesh results by (origin bucket, target bucket, crossing)
  }
  public interface IConfirmedLegs { bool TryGet(float x0, float z0, float x1, float z1, int crossing, out float cost, out bool noPath); void Request(float x0, float z0, float x1, float z1, int crossing); }
  public static class ManeuverFeasibility {
    public const float LegStepMeters = 10f, HeldCrossingHours = 1.0f,
      DaylightMarginHours = 0.25f, LateHours = 1.0f, DetailSlope = 0.25f, RiskyPenalty = 0.35f, ReasonThreshold = 0.2f,
      AnchoredEndPenalty = 0.5f, PassiveShiftFactor = 1.5f, BaseDefenseRatio = 1.5f, FeasibilityWeight = 0.4f,
      ReplanPenaltyMargin = 0.3f, ReplanScoreMargin = 0.1f, ThreatLeadHours = 0.25f, ConfirmCacheHours = 0.25f;
    public const int ConfirmCandidates = 3, ConfirmQueriesPerTick = 1, MaxLookRequests = 2, MaxBodies = 6;
    public static void Evaluate(in FeasibilityInput input, FeasibilityLedger into);              // every stance-eligible candidate
    public static void EvaluateSelected(in FeasibilityInput input, FeasibilityLedger into);      // the monitor: the selected plan only
    public static float Bar(in OpportunityPersonality p);
    public static float FogPremium(float confidence, float caution01);
    public static float MarchHours(float cost, float baseSpeed, float meanFatigue, in MarchRules r);
    public static float DetailPenalty(float odds, float bar);
    public static float CombinePenalty(in BodyEstimate b);
    public static bool TryBrokenAssumption(in CandidateResult atSelection, in CandidateResult now, ReadOnlySpan<BodyEstimate> before,
      ReadOnlySpan<BodyEstimate> after, in MonitorFacts facts, out string reason);   // reasons in the order of requirement-assumption-monitor
  }
  public struct MonitorFacts {                                         // per selected plan; pooled arrays indexed like the bodies
    public double SelectedAtHours, NowHours; public ManeuverPhase[] Phase; public int[] CrossingStart, CrossingCount; public bool[] CrossingPassed;   // per crossing on each body's route
    public double[] PlannedReleaseHours;
    public string[] AbandonedChildIds; public int AbandonedCount; public bool SelectedWhileUnknown, MapKnownFired;
  }
}
// PlayProjection.ProjectInto(template, …, Span<WorldPlayRect>) and PlayCorpsAssignment.AssignInto(rects, corps, Span<CorpsAssignment>): allocation-free, parity with Project / Assign.
// PlaybookContext.Feasibility (IFeasibilityLookup, null = off); TacticalPlaybookCatalog.Select: Impossible skipped after the jitter and crest draws, − FeasibilityWeight × penalty;
//   Select reports its candidates (ranked, with the feasibility term) into a pooled SelectionReport for the TacticalPlanSelection row.
// ReplanTrigger.ManeuverAssumptionBroken (appended); ReplanTriggerInput.AssumptionReason (static literal, null none).
// ManeuverInput.HasPlannedRelease/PlannedReleaseHours, HasTimeBudget/TimeBudgetHours; ManeuverState.HasFirstPlannedRelease/FirstPlannedReleaseHours, HasBudget/BudgetHours, HasVia/ViaX/ViaZ (latched); ManeuverSpec.ProjectedMirrored, HoldsForRelease;
//   ManeuverOrder.ViaX, ViaZ; CommandDoctrineOrder.ViaX, ViaZ (NaN none); ManeuverNodeEntry via; the fix's planned release reaches StepStartDelay through a start-delay ManeuverSpec;
// BattleRecon target kind Crossing; DirectChildAllocator release reason crossing-threat (ReserveReleaseContext.CrossingThreats).
// Runtime: FeasibilityRuntime (MarchRules from GamePrefs, DaylightHours from uniStormSystem, the confirm queue's NavMesh call, the per-side ledgers),
//   RiverRegionsRuntime stepping inside TerrainPictureRuntime.Step, CrossingWatch's perceived-ground probe through BC13's PerceivedGroundRuntime.
```

---

## 9. Roles, lifecycles, shared rows and conflict rules

**Roles (who may perform each verb).**

| Verb | Role | Never |
|---|---|---|
| Build the river regions | `TerrainPictureRuntime.Step` after the picture is ready | Patches; producers |
| Classify crossings, evaluate candidates, write the ledger | The coordinator's heavy path, per AI-run side, before `MaybeReplan` | Patches; the arbiter tick; a suppressed side (a CIC player's alliance, the player alliance outside `ai_vs_ai`) |
| Issue a NavMesh confirmation query | `FeasibilityRuntime` on the coordinator tick, `ConfirmQueriesPerTick` per side | Any producer |
| Select a plan | `ArmyOrchestrator` (`Select` reading the ledger) | The ledger itself |
| Raise `ManeuverAssumptionBroken` | `ArmyTickCycle` from the ledger's monitor result | BC11 directly |
| Release, march and turn a wing | BC11 (`PlayExecutorRuntime`, `EnvelopmentManeuver`), its orders executed by the posture producer through the arbiter and executor | The ledger |
| Ride to look at a crossing | BC9's recon producer | The ledger |
| Release a reserve to a threatened crossing | `DirectChildAllocator`, executed by the posture producer | — |
| Move a player-owned unit | Nobody: no feasibility output reaches a `PlayerOwned` unit (the executor's guard as today) | Every Whiskey path |

**Lifecycle table.**

| Entity / binding | Create | Change | Unbind | Delete | Migration of live dependents |
|---|---|---|---|---|---|
| River regions | First step after the picture is ready | Never (terrain is static for the battle; frozen state read once) | — | `BattleScope` reset | Consumers see "map unknown" until ready |
| Crossing state | First heavy tick with regions ready | Every heavy tick | — | `BattleScope` reset | A state change on a selected route raises the monitor |
| Ledger candidate results | Each evaluation | Each evaluation (replaced whole) | — | `BattleScope` reset; a stand-down or new battle day clears it | Selection reads the latest. **Version semantics: latest**; the replan reads results no older than `EvaluateIntervalHours` |
| Selected-plan snapshot (the results at selection, for the monitor) | Each selection | Never | Replan | Replan, `BattleScope` | The monitor compares against it; a replan replaces it |
| Planned release / time budget / via of a child (binding ledger → BC11), wings and the holding fix | Selection of a plan with enveloping bodies | Every heavy tick until the child leaves its Hold (release time); the Hold ceiling is anchored to the first planned release (latched) | Release (the release time is spent); a mirror flip (the ledger values stop applying); plan change; abandon | With the plan | **Pin at release:** once BC11 launches the March, its via and budget are latched in `ManeuverState` for that leg; a later estimate changes nothing except through the monitor's replan |
| Confirmed leg cache | A NavMesh result | — | Expiry after `ConfirmCacheHours` | `BattleScope` reset | An expired leg is re-queued; the estimate stands meanwhile |
| Look request | Each evaluation | — | The crossing turns Clear, Watched or Held | Next evaluation | BC9 ends a mission whose request vanished at its next step |
| Crossing-threat reserve release | The allocator, on the threat | — | The threat stops | As BC13's release state | The released child reverts to Reserve on the next allocation, as BC13 does |

**Shared rows and conflict rules.**

| Shared row | Writers | Conflict rule |
|---|---|---|
| The ledger | The heavy path only | One writer. Readers (the catalog, BC11's runtime, the recon producer, the allocator) read the published state; no reader writes it |
| A wing's Hold release | BC11's own rule and the ledger's planned release | When the ledger has a planned release, its time test replaces both of BC11's waits; the route-known test and the checks then apply in today's order after it (requirement-synchronized-release). Never both: the release is one expression |
| A wing's via and road route | The ledger (via) and BC10's road router | A maneuver March with a via gets no road route (requirement-crossing-via); without a via, BC10's rules as today |
| `AvoidCoveredCrossings` vs a via | Posture pipeline | A maneuver March with a via never sets it (BC10's rule would drop the via) |
| A child wanted by a play rect and a BC13 exploit | BC11, BC13 | Mutually exclusive as BC13 defined; the hypothetical layout honours the same exclusion |
| A Reserve child wanted by an exploit, a crisis, a flank threat and a crossing threat | `DirectChildAllocator` | Order within one allocation: crisis (BC13, defence first), BC11's flank-threat, crossing-threat, then the stagger; one release per reason per sector or crossing; a child already released for one reason is not released again |
| BC9 recon targets | BC9's planner | Crossing looks come after stale entry points and stale bands, inside the same cap; one mission per target |
| The plan | Replan triggers (existing and `ManeuverAssumptionBroken`) | One replan per cooldown; the first trigger evaluated wins, as today (`ArmyReplanTriggers.Evaluate` order; the new trigger is appended last). A pre-empted assumption is named `preempted` on its row; the replan that pre-empted it reads the ledger and so sees the broken assumption anyway |

---

## 10. Exceptions per step

| Step | Absent | Stale | Deleted | Failed |
|---|---|---|---|---|
| Picture / regions | Not ready, or no grid → map unknown, verdicts `Unknown`, BC11 as today | — (static) | — | Build exception → OnceLog `tactical-battle-core:river-regions-failed`, map unknown for the battle |
| Crossing classification | No perceived-ground runtime (BC13 off) → no crossing is ever Clear; Held and Watched still apply | A probe older than `ClearMemoryHours` → not Clear | An observation dropped → re-classified next tick | `IsPerceived` throws → that point unperceived (BC13's rule) |
| Hypothetical layout | No direct children → no candidate has bodies; every verdict `Unknown` | Child positions are this snapshot's | A child destroyed between evaluations → absent next evaluation | — |
| Route screen | No link between regions → `Impossible` `no-crossing` | Estimates replaced by confirmations as they arrive | — | — |
| NavMesh confirmation | Budget spent → the estimate stands, the leg stays queued | Older than `ConfirmCacheHours` → re-queued | — | A query that throws → logged once (`tactical-battle-core:feasibility-nav-failed`), the estimate stands and the leg is not retried for `ConfirmCacheHours` |
| Enemy picture | No samples → the assumed enemy point (BC10) and the ledger estimate | Remembered below 0.5 ignored | — | — |
| Daylight | `uniStormSystem` unreadable → `DaylightHours` = +∞ (no `after-dark`), OnceLog `tactical-battle-core:feasibility-daylight-missing` | — | — | — |
| Selection | Ledger null (flag off) → today's selection | Ledger older than `EvaluateIntervalHours` → an evaluation runs first (requirement-evaluation-cadence) | A candidate missing from the ledger → term 0, not filtered | All `Impossible` → requirement-never-empty |
| Monitor | No enveloping bodies → nothing to watch | — | A body's child destroyed → BC11's `lost-strength` → `wing-abandoned` | — |
| BC11 release | No ledger entry for the child → BC11's rule | The release recomputed every heavy tick until release | — | — |

References are **latest**, never pinned, except the via and time budget pinned at release (§ 9).

---

## 11. Performance, configuration, telemetry

- **requirement-frame-budget** — Cost:
  - **The evaluation** runs at most every `EvaluateIntervalHours` per side on the heavy path. Its work is at most 22 evaluations (18 playbooks less the stance-filtered ones, plus the mirrored side of the four single-flank templates) × `MaxBodies` bodies. Each body costs one Dijkstra over at most `MaxCrossings` link nodes (in practice a handful), with legs priced by `SampleLine` at 10 units. A 2,000-unit leg is 200 samples.
  - **The monitor** is one candidate per heavy tick.
  - **NavMesh:** one query per side per tick at most.
  - **The region build:** inside the picture runtime's 4 ms steps.
  - Scopes: `tactical.feasibility.evaluate` (threshold 2.0 ms), `tactical.feasibility.monitor` (0.5 ms), `tactical.feasibility.navmesh` (2.0 ms), `tactical.river-regions.build` (5.0 ms, as the picture's step). Target: criterion-frame-budget.
- **requirement-zero-alloc** — No allocation after warm-up: pooled ledger arrays (`MaxBodies` per candidate), the pooled projection and assignment overloads, Dijkstra on pooled arrays sized at first use, string literals for reasons, telemetry strings built only on emitted rows. Pinned by FEAS-ALLOC.
- **requirement-config** — `Tactical` section, default `true`, with a reader and a truthful description:

  | Flag | Covers | Off |
  |---|---|---|
  | `Enable Maneuver Feasibility` | §§ 4-7 except requirement-stance-before-pick: the evaluation, the selection term, the side choice, the monitor and its replan trigger, the synchronized release, the time budget, the crossing via, the look requests, the defender exposure and the crossing-threat release | `PlaybookContext.Feasibility` null; BC11's Hold, ceiling, timeout and route exactly as today; no look requests; no crossing-threat release; the ledger, regions and crossing watch are not built |

  requirement-stance-before-pick is a bug fix and is not behind the flag. Rollback of every Whiskey command write stays Commander Mode `MonitorOnly` / `Off`. The plan may split the defender release into its own flag if the smoke asks for it (it is `should`).
- **requirement-telemetry** — Through the TelemetryRouter tuning-log sink, bounded:
  - `TacticalPlanSelection` (Decision, one per selection or kept plan, emitted by `ArmyOrchestrator.TryPickPlan` after `Select`, with the flag off too, so on/off runs compare): `trigger` (`initial`, the replan trigger, `new-day`), `kept`, `stance`, `odds` (`CurrentOdds`), `winner`, `mirrored`, `candidates`, `preconditionFiltered`, `stanceFiltered`, `crestFiltered`, `feasibilityFiltered`, `allImpossible`, and the top three by final score, each with `plan`, `score`, `feasibilityTerm`, `verdict`, `reason`. This also fills today's gap: no row carries candidate scores (control: the plan-selection emitters `[TacticalPlan]`, `[TacticalReplan]`, `TacticalPlayExecution` and `TacticalEnvelopment` and `Select`'s unemitted `preconditionFiltered`, reads 5, under: source; failure-under: source).
  - `TacticalManeuverFeasibility` (Decision, per candidate per evaluation when its verdict or penalty bucket (0.1) changed, throttled per candidate to one per 0.05 doctrine-h): `plan`, `mirrored`, `verdict`, `penalty`, `reason`, `bodies`, `selected`.
  - `TacticalManeuverBody` (Decision, per body of the selected plan at selection and on each monitor change, and of the top confirm candidates per evaluation): `plan`, `body`, `child`, `kind`, `strength`, `routeUnits`, `routeSource` (`estimate` / `navmesh`), `crossing` (link index or −1), `crossingState`, `courierHours`, `marchHours`, `strikeHours`, `desiredStrikeHours`, `releaseHours`, `enemyShiftHours`, `windowHours`, `pin`, `enemyAgainst`, `isolatedOdds`, `bar`, `ownRatio`, the six terms (`daylight`, `detail`, `contested`, `approach`, `exposure`, `turn`), `penalty`, and on a monitor row `assumption` and `suppression` (`cooldown`, `committed`, `margin`, `preempted`, or none).
  - `TacticalReplan` (Decision, structured, one per replan and per kept plan, never deduped): `trigger`, `assumption`, `kept`, `newPlan`, `mirrored`, `doctrineHours`. Today's deduped `[TacticalReplan]` log line stays for the main log.
  - `TacticalCrossingWatch` (State, per side every 0.1 doctrine-h): `held`, `watched`, `clear`, `unknown`, `probes`, `lookRequests`, `regions`, `links`.
  - `TacticalEnvelopment` (existing) gains `plannedReleaseHours`, `timeBudgetHours`, `via` and, on the Turn transition, `arrivedHours` and `predictedArrivalHours` (criterion-arrival-prediction).
  - `TacticalReplan` rows carry `trigger=ManeuverAssumptionBroken` and `assumption=<reason>`.
  - `TacticalReserveRelease` gains `reason=crossing-threat`.
  - OnceLog (static keys):
    - `[once:tactical-battle-core:river-regions] [RiverRegions] regions=<n> links=<n> crossings=<n> frozen=<b> buildMs=<ms>`;
    - `[once:tactical-battle-core:feasibility-first] [Feasibility] first evaluation: candidates=<n> impossible=<n> risky=<n>`;
    - `[once:tactical-battle-core:feasibility-filtered] [Feasibility] first plan filtered: plan=<p> reason=<r>`;
    - `[once:tactical-battle-core:feasibility-replan] [Feasibility] first assumption replan: assumption=<r>`;
    - `[once:tactical-battle-core:feasibility-via] [Feasibility] first wing routed through a chosen crossing: state=<s>`;
    - the warning keys of §§ 4, 5 and 10: `river-regions-truncated`, `river-regions-failed`, `feasibility-all-impossible`, `feasibility-nav-failed`, `feasibility-daylight-missing`.

---

## 12. Fit criteria

- **criterion-no-doomed-launch** — With the flag on, 0 enveloping wings are launched on a route the screen or the NavMesh confirmation found `Impossible`. The BC11 abandons `no-route` and `river-on-leg` on wings of plans selected with feasibility fall to ≤ 25 % of their count with the flag off, over the same smoke saves (a battle with a river across the front). Basis: the screen and confirmation check exactly what BC11 abandons on, before selection. The 25 % allows for crossings that turn Held after launch. Measured: `TacticalEnvelopment` abandon rows, flag on against off, from the same pre-battle save.
  - guardrail: the share of Attacker or Meeting battles with known odds ≥ 1.3 in which a dividing play is selected stays at least 50 % of its flag-off share on the same saves. The AI must not stop maneuvering.
  - read path: `TacticalEnvelopment` (abandon reasons) and `TacticalPlanSelection` (winner) rows.
- **criterion-scenario-outcomes** — In the harness fixtures of § 6 (scenario S6), each emergent outcome holds:
  1. 35,000 against 25,000 selects a dividing play (`Feasible`);
  2. even odds penalizes every dividing play ≥ 0.3 and selects a single-body play with the mid commander;
  3. on the held-crossing map at even odds, no candidate whose route uses the held bridge is selected, and a bank-holding single-body play is; at 2:1 a mirrored ManeuverAndFix (pin and cross) is selected, with the clear ford as its via;
  4. the piecemeal fixture shows `detail` ≥ 0.7 without the synchronized release and 0 with it.

  Basis: § 6, computed. Measured: FEAS-SCN-1 to FEAS-SCN-5.
  - guardrail: with an audacity01 of 0.9 commander (Bar 0.88 at caution 0.5, agg 0.5), the even-odds fixture still keeps at least one dividing play `Risky`, not filtered. Temperament can choose the risk; nothing forbids it.
  - read path: the harness only, with the same fields on `TacticalManeuverBody` rows in the game.
- **criterion-arrival-prediction** — For enveloping wings that reach Turn, the median of `|arrivedHours − predictedArrivalHours| / max(predicted march hours, 0.25)`, where `predictedArrivalHours` = `S* − DeployHours` recorded at release, is ≤ 0.30 over at least five wings in the smoke battles. Basis: vanilla's own speed and courier formulas (§ 2.1); terrain cost along a NavMesh polyline; the unmodelled part is slope (`1 − Δy/0.25`), overlap waits and column length. Measured: `TacticalEnvelopment` Turn rows.
  - guardrail: no wing's Turn arrives after its `TimeBudgetHours` with the release at its planned hour (0 `timeout` abandons on planned releases over the smoke battles), so the budget is never shorter than the prediction's error.
  - read path: `TacticalEnvelopment` rows (`arrivedHours`, `predictedArrivalHours`).
- **criterion-strike-spread** — For DoubleEnvelopment plans with both wings reaching Attack, the median gap between the two wings' Attack transitions is ≤ 0.25 doctrine-h. Today it is the difference of their march times. Basis: requirement-synchronized-release. Measured: `TacticalEnvelopment` Attack rows per plan.
  - guardrail: a fix held for synchronization (requirement-synchronized-release) never waits past its planned release by more than 0.1 doctrine-h, and a fix in contact is never held. The fix's `Released` hour against its `PlannedReleaseHours` is reported on the same rows (start-delay rows of the fixing child).
  - read path: `TacticalEnvelopment` rows.
- **criterion-replan-stability** — `ManeuverAssumptionBroken` replans are ≤ 3 per side per battle day, and never two within `Min Replan Seconds`. Basis: the cooldown and the margins of requirement-assumption-replan. Measured: `TacticalReplan` rows.
  - guardrail: every `crossing-held` assumption on a selected route either raises a replan or names its suppression reason (`cooldown`, `committed`, `margin`, `preempted`) on the row. A broken assumption is never silent.
  - read path: `TacticalReplan` and `TacticalManeuverBody` rows.
- **criterion-selection-regression** — On every existing playbook-selection harness fixture (no picture, no regions), `Select` with the flag on returns the same playbook as with it off. Basis: the term is 0 for `Unknown` and the filter follows the jitter draw. Measured: FEAS-REGRESS over the existing fixtures.
  - no-guardrail: an equality bound.
  - read path: the harness. In the game, the `TacticalPlanSelection` rows written before the river regions are ready (every verdict `Unknown`, every term 0) are the same check.
- **criterion-fog-honest** — 0 evaluations whose enemy inputs include an enemy that is not a sample under BC13's `requirement-enemy-samples` or the ledger estimate. 0 crossings Clear without a perceived probe within `ClearMemoryHours`. 0 reads of `scheduledarrival` or `EnemyRecords` beyond a Visible observation's row. Basis: the AGENTS fog rule. Measured: FEAS-FOG and the source pin FEAS-PIN.
  - no-guardrail: a pure exclusion. Its cost (more `Unknown` crossings) is visible as `unknown` on `TacticalCrossingWatch`.
  - no-read-path: a runtime measurement needs an omniscient comparison that the evaluation may not make. The instrumentation package in this wave is the source pin plus the `unknown` count.
- **criterion-player-never-moved** — 0 tasks, releases, vias or look missions planned onto a `PlayerOwned` unit, and 0 player-owned strength counted in any body. Basis: the standing rule. Measured: FEAS-PLAYER and the executor's `PlayerOwned` skips for maneuver reasons.
  - no-guardrail: a zero bound.
  - read path: `[OrderArbiter]` `topSkip` and `TacticalManeuverBody` `strength` against the snapshot.
- **criterion-defender-no-offense** — 0 offensive playbooks selected for a Defender, the initial pick included. 0 Attack-task orders created by BC14 for a Defender (its outputs are a holding release and recon looks). Basis: the standing rule and requirement-stance-before-pick. Measured: FEAS-DEF and the `TacticalPlanSelection` rows' winner by stance.
  - no-guardrail: a zero bound.
  - read path: `TacticalPlanSelection` rows (the winner and the side's stance).
- **criterion-frame-budget** — Per side: `tactical.feasibility.evaluate` p95 ≤ 2.0 ms and max ≤ 6.0 ms; `tactical.feasibility.monitor` p95 ≤ 0.5 ms; `tactical.feasibility.navmesh` p95 ≤ 2.0 ms. The region build steps stay within the picture's 4 ms deadline (`maxMs` ≤ 5.0). Measured in a battle with a river and about 500 regiments a side. Basis: the evaluation runs at most every 0.05 doctrine-h (≈ 30 real seconds at 1×), so its cost is not per frame. The monitor is per heavy tick, and the NavMesh query count is the BC11 precedent.
  - guardrail: the `tactical.patch.battle-core-arbiter` scope's mean per call does not rise by more than 5 % with the flag on against off. BC14 does no work in the arbiter tick.
  - read path: `PerformanceAggregate` rows of the four scopes. The p95 is within the threshold when `Σ slowCount / Σ count` ≤ 0.05; the max is the largest `maxMs`.
- **criterion-zero-alloc** — 0 bytes allocated per evaluation and per monitor step after warm-up. Basis: the Global Constraints. Measured: FEAS-ALLOC (`AssertNoAllocation`, the BC10 pattern).
  - no-guardrail: the criterion is itself a bound.
  - no-read-path: Mono exposes no per-call allocation counter in the game. The instrumentation package in this wave is the harness test, run in the gate list.

---

## 13. (E) Tests, plants, smoke, coverage matrix (frozen test plan)

Test file `ManeuverFeasibilityTests.cs` (`internal static partial class ManeuverFeasibilityTests { public static void Run() }`, partials allowed), with `<Compile Include>` lines for each new pure file. Red first: FEAS-SCN-3, FEAS-SCN-4, FEAS-STANCE-INITIAL and FEAS-SYNC must fail against today's code before any implementation.

### Acceptance scenarios and cases

- **S1 Regions and crossings.**
  - REG-SPLIT: a river across a fixture picture gives two regions; a bridge and a ford each link them.
  - REG-CREEK: a creek does not split.
  - REG-FROZEN: a frozen map is one region.
  - REG-LINK-RADIUS: a crossing 50 units from land on both banks links; one 70 units from one bank links only the near region.
  - REG-STEP: the build honours the deadline across rows (the BC12 scripted clock); not ready until complete.
  - REG-TRUNC: the 65th region and the 257th link are dropped with the OnceLog.
  - XING-HELD: an enemy regiment within 1.5 × fire range facing the crossing → Held; at 50° → not Held.
  - XING-WATCHED: an enemy infantry regiment at 180 units not facing the crossing → Watched; at 250 → not (shipped infantry reach 200); a perceived battery within its reach → Watched, never Held.
  - XING-CLEAR: no enemy within the watch distance, probed 0.1 h ago → Clear; probed 0.3 h ago → Unknown.
  - XING-REMEMBERED: Remembered at 0.4 confidence neither holds nor watches.
  - XING-SHARED-RULE: `CoveredCrossings.BuildPoints` and `CrossingWatch` agree on rule (a) for 50 random fixtures (the shared `Covers`).
  - XING-PROBE-CAP: 40 crossings → at most 16 probes per tick, oldest first.
- **S2 Routes and times.**
  - ROUTE-SAME-REGION: one leg, its cost the 10-unit sample sum.
  - ROUTE-VIA-CROSSING: different regions → the Dijkstra passes the cheaper crossing.
  - ROUTE-HELD-DETOUR: a Held bridge on the straight line and a Clear ford 600 units away → the ford (bridge + 1.0 h is dearer); with the ford 2,000 units away → the bridge, `contested` = 1.
  - ROUTE-NO-CROSSING: no link → `Impossible`, `no-crossing`.
  - ROUTE-BEND: a river sample inside one region is priced at swamp cost and marked estimated.
  - ROUTE-CONFIRM: a confirmed polyline replaces the estimate; `noPath` with no alternative → `no-route`.
  - ROUTE-CONFIRM-BUDGET: one query per side per tick; selected plan first; cache hit within 0.25 h.
  - MARCH-TIME: 1,000 units of road → exactly 1.0 h for infantry (the last sample pro-rated); 500 of grass → 1.0 h; cavalry-only base 2 halves it; fatigue 0.5 → divided by 0.5^0.2.
  - LEG-FOOTPRINT: open-river samples within 60 units of the leg's own ford are priced as the ford and do not mark the leg estimated; an open-river sample elsewhere in the region is a bend (swamp cost, estimated).
  - TARGET-SNAP: a rect anchor on open river snaps to the nearest land cell; a tie goes to the body's own region.
  - MARCH-RULES-PIN: a source pin that no literal 1000, 0.02, 75, 2.33 or 2.2 appears in `ManeuverFeasibility.cs` (all from `MarchRules`).
  - COURIER-TIME: a first leg within 75 units → max(0.02, 0.02) = 0.02; a 2,563-unit first leg on grass → 0.02 + 5,126 / 5,126 = 1.02 h; the same on road → 0.52 h; a relay leg beyond bugle range → 0.02 + its ride; the longest branch counts; an engaged member → 0; order delays off → 0.
  - STRIKE-OFFSET: MobileFlankMarch (offset 0.25 h) with `T_main` 0.6 and `T_wing` 1.0 → `C*` = max(0.6, 0.75) = 0.75, `S*_wing` = 1.0, releases: wing 0, fix 0.15. With `T_wing` 0.4 → `C*` = 0.6, `S*_wing` = 0.85, releases: wing 0.45, fix 0.
  - STRIKE-FIX-CONTACT: a fix in contact has `T_main` 0 and release 0, and the wing is released at once.
  - STRIKE-ABSOLUTE: `PlannedReleaseHours` = `EvaluatedHours` + R.
  - STRIKE-DOUBLE: DoubleEnvelopment wings at 1.2 h and 2.4 h, fix at 0.79 h → `C*` = `S*` = 2.4 for all; releases: the faster wing 1.2, the fix 1.61, the slower wing 0.
  - STRIKE-HELD: a route through a Held crossing adds 1.0 h to `T`.
  - DAYLIGHT: `S*` past day end − 0.25 → `after-dark`, `daylight` 1, verdict Risky (never Impossible).
- **S3 Enemy and detail.**
  - ENEMY-FOG-PREMIUM: confidence 0.8, caution 0.5 → × 1.1; caution 1.0 → × 1.15.
  - ENEMY-NO-SAMPLES: the assumed enemy point and `N/(bodies + 1)`.
  - SHIFT-PASSIVE: enemy intent Defend at confidence ≥ the floor → τ × 1.5.
  - SHIFT-RIVER: the enemy's shift crosses at its own Held crossing with no penalty.
  - DETAIL-PIN: an engaged fixing body pins `S/(1.5 + TerrainStrength)`.
  - DETAIL-WINDOW: `shiftShare` 0 when τ ≥ W, 1 when W ≥ 2τ, linear between.
  - SUPPORT-TIME: `σ` is the least of the other bodies' courier plus march hours from where each stands when it has struck (an enveloping body's strike point; a fixing member's anchor) to the body's strike point, over the crossing graph, Held crossings at +1.0 h; no other body → +∞.
  - DETAIL-BAR: Bar 1.0 for a mid commander; 0.6 and 1.4 at the clamps; caution raises, audacity and aggression lower.
  - DETAIL-PENALTY: odds 0.91 at bar 1.0 → 0.36; odds 0.75 → 1.
  - DETAIL-NO-FIX: a template whose fixing rect is unfilled pins nothing.
  - EXPOSURE-FOREST: route samples within a perceived regiment's reach count, in forest at half weight; samples in the last 250 units do not count; the main body has no exposure term.
  - ODDS-MORALE: the enemy's perceived morale divides the odds; a main body's odds carry its `own` ratio (1.5 + TerrainStrength), an enveloping body's 1.
  - APPROACH-ANCHORED: an enemy line end anchored on a river adds 0.5.
- **S4 Verdicts and selection.**
  - VERDICT-UNFILLED: DoubleEnvelopment with one child → `Impossible`, `wing-unfilled`.
  - VERDICT-UNKNOWN: picture not ready → `Unknown`, penalty 0.
  - VERDICT-RISKY: penalty 0.35 → Risky; 0.34 → Feasible.
  - VERDICT-ORDER: no axis → `Unknown` before `wing-unfilled`; map unknown → `Unknown` before `wing-unfilled`; `wing-unfilled` before `no-crossing`.
  - SIDE-RANK: a `Feasible` side at penalty 0.1 beats an `Unknown` side; an `Impossible` side loses to both; equal penalties keep the authored side.
  - MULTI-RIVER: a route over two rivers adds `HeldCrossingHours` per Held crossing, takes the worst `contested`, and carries the first crossing as its via.
  - FIX-MEMBER-RELEASE: two fixing members with `T` 0.5 and 0.8 and `C*` 1.0 → releases 0.5 and 0.2.
  - SELECT-FILTER: an `Impossible` winner-by-score is skipped and the next taken; `feasibilityFiltered` counted.
  - SELECT-JITTER: the jitter of every other candidate is identical with and without a filtered candidate (the filter follows the draw).
  - SELECT-NEVER-EMPTY: every candidate `Impossible` → lifted, `allImpossible`.
  - SELECT-SIDE: a single-flank play mirrored when its left side scores better; the zone assignment projects the template mirrored and gives the rect to the child the evaluation timed.
  - INWARD-LEG: a DoubleEnvelopment wing whose anchor stands on its own bank and whose line end lies across an open river with no link → `Impossible`, `river-on-leg`; with a Held link → `contested` 1 and +1.0 h.
  - FEAS-REGRESS: every existing selection fixture returns the same winner with the flag on (criterion-selection-regression).
  - FEAS-STANCE-INITIAL: through `PickInitialPlanInOrder` with a recording fake, the pick inputs are refreshed before the pick, and a Defender's initial pick never returns an offensive playbook (red today: the seam does not exist and the order is pick-first).
  - FEAS-DEF: a Defender's candidates never include offensive playbooks; no Attack task from BC14.
- **S5 Monitor and replan.**
  - MON-HELD: a Clear ford on the selected route turns Held before the wing passes → `crossing-held`; after the wing passed → nothing.
  - MON-LATE: strike estimate past `S*` + 1.0 → `late`.
  - MON-DETAIL: detail rises 0.2 → 0.6 → `detail`; 0.2 → 0.4 → nothing (margin).
  - MON-ABANDON: BC11 abandons one DoubleEnvelopment wing → `wing-abandoned`; a lone FlankMarch abandoned after its mirror with no dependent → nothing.
  - MON-COMMITTED: every enveloping body in Turn or Attack and every fix `Released` → no trigger, even when the main body's detail rises past the margin.
  - MON-ROUTE-MISSING: a NavMesh confirmation returning no path for a selected body's leg, with no other crossing → `route-missing`.
  - MON-MAP-KNOWN: a plan selected while `Unknown` → one `map-known` trigger when a known verdict appears; none at the next evaluation.
  - MON-SUPPRESSION: a broken assumption within the cooldown → `cooldown` on its row; kept by the margin → `margin`; another trigger first → `preempted`.
  - MON-ESTIMATE: a marching body's strike estimate has no courier term and counts only the Held crossings not yet passed; a held body's is measured from its planned release.
  - REPLAN-HYSTERESIS: the new best within 0.1 of the current → the current plan stays; current `Impossible` → switches.
  - REPLAN-COOLDOWN: two broken assumptions within `Min Replan Seconds` → one replan.
- **S6 Scenarios (criterion-scenario-outcomes; the § 6 fixtures).**
  - FEAS-SCN-1: 35,000 against 25,000, three corps (`EnvelopmentPressure` on) → ManeuverAndFix and DoubleEnvelopment `Feasible` with the § 6 numbers (wing `odds` 1.17, fix 1.25 / 1.43); a dividing play selected.
  - FEAS-SCN-2: the piecemeal fixture → every `detail` 0 with the synchronized release; without it the right wing's and the fix's `detail` are 1.
  - FEAS-SCN-3: 25,000 against 25,000, two corps → DoubleEnvelopment's wings `detail` 1 (`σ` 2.12 > `τ`), the single-fix plays' main body `detail` 1; every dividing play penalized ≥ 0.3; a single-body play selected for the mid commander (red today: a dividing play, ManeuverAndFix, has the best base score at odds 1.0).
  - FEAS-SCN-4: the § 6 scenario 4 fixture. At even odds → FrontalShockAssault, MeetingEngagement and the authored WideFlankApproach penalty ≥ 0.6, the mirrored WideFlankApproach wing `detail` 1, a bank-holding single-body play selected (red today: no term sees the bridge). At 2:1 → ManeuverAndFix selected with `Mirrored` true and via = the ford; DoubleEnvelopment penalty ≥ 0.6 (its inward legs through B); the mirrored WideFlankApproach below its authored side and below FrontalShockAssault.
  - FEAS-SCN-5: a Defender holding the river with an uncovered ford in the enemy's reach → turning exposure > 0 for a template whose reserve is far from it; a look request for the ford when Unknown; a crossing-threat release when a perceived enemy body's arrival falls within `ownShift + 0.25`.
  - FEAS-SCN-AUDACITY: the guardrail case of criterion-scenario-outcomes.
- **S7 BC11 and BC13 interaction.**
  - FEAS-SYNC: `PlannedReleaseHours` set → the Hold releases at it, not at contact + offset (red today: the field does not exist).
  - SYNC-UNSET: no ledger entry → BC11's rule unchanged (the BC11 tests stay green).
  - SYNC-PIN-AT-RELEASE: the via and the budget do not change after the March starts.
  - BUDGET: a predicted courier 0.1 h and march 1.9 h → `TimeBudgetHours` 3.05 (1.5 × 2.0 + 0.05); never below 1.0 with daylight to spare; with 0.6 h of daylight left → 0.6; latched at the March.
  - CEILING-LATCH: a planned release that recedes every tick still ends the Hold at `FirstPlannedReleaseHours` + 1.0; a fast wing waiting for a slow one (release 2.1 h after activation) is not launched by the ceiling before its release.
  - FIX-HOLD: a fixing child with a future release holds (FormUp at its position) through the start-delay path and is `Released` at its planned hour; a fix in contact, a deployed reserve or a child with no ledger entry is never held.
  - MIRROR-FALLBACK: a child on a mirrored projection can still mirror once (BC11's own try) to the authored side; after the flip the ledger's release, budget and via are not applied.
  - STATE-FLAGS: a `default(ManeuverState)` has no first planned release, no budget and no via (the `Has…` flags false), whatever the hour fields hold.
  - VIA-PASSED: once the child stands in the via's far region, the March order carries no via, so a re-issued order never walks it back.
  - VIA-ORDER: a March with a via → `BattleOrderIntent.ViaCount` 1 at the crossing, no road route, no `AvoidCoveredCrossings`.
  - VIA-ROUTE-CHECK: BC11's march-leg check runs as two legs through the via.
  - BC13-EXCLUDE: a child with a live exploit is not offered to the layout.
  - BC13-HALF: a main-body child with a live exploit counts half in `Pin` and `S`.
  - BC13-PERSONALITY: the evaluation reads the army entry of the personality table; with that flag off, the roster vector.
  - LOOK-PRIORITY: the selected route's Unknown crossing first; at most two; a look request expires when the crossing turns Clear.
  - LOOK-RECON: the recon producer takes a `Crossing` target after stale entry points and bands, within its cap.
  - RELEASE-CROSSING: one release per crossing, the floor respected, the reason `crossing-threat`, never for a non-Defender.
- **S8 Fog, player, performance.**
  - FEAS-FOG: a hidden enemy (not an observation) changes no crossing state, no `N`, no shift time.
  - FEAS-PIN: a source pin that the evaluation reads `EnemyRecords` only through a Visible observation's row, and never `scheduledarrival` or `durationleft`.
  - FEAS-PLAYER: player-owned strength excluded from `S`; a wholly player-owned child not offered; no via or release on a player-owned unit.
  - FEAS-ALLOC: 0 bytes after warm-up for one full evaluation (22 evaluations, 6 bodies, a 64-link graph) and one monitor step.
  - FEAS-TIME: an informational timing of the full evaluation on the harness; the binding measurement is smoke 6.
  - NAV-SITES: `NavMeshQueriesSitAtTerrainHeight` counts 4 and pins the new site at terrain height.
  - PROJECT-PARITY / ASSIGN-PARITY: the pooled overloads equal `Project` / `Assign` on every template.

### Mutation plants (each must make a listed test fail)

- the held-crossing hours set to 0 (ROUTE-HELD-DETOUR, FEAS-SCN-4);
- the mirrored side not evaluated (SELECT-SIDE, FEAS-SCN-4);
- the filter placed before the jitter draw (SELECT-JITTER);
- the never-empty lift removed (SELECT-NEVER-EMPTY);
- the pin term ignored (DETAIL-PIN, FEAS-SCN-1);
- `shiftShare` fixed at 1 (DETAIL-WINDOW, FEAS-SCN-1);
- the support time `σ` replaced by a constant 0.5 h (SUPPORT-TIME, FEAS-SCN-3);
- the `route-missing` assumption removed (MON-ROUTE-MISSING);
- the `map-known` assumption removed (MON-MAP-KNOWN);
- a suppressed assumption left unnamed on its row (MON-SUPPRESSION);
- the fog premium removed (ENEMY-FOG-PREMIUM);
- the bar's caution sign flipped (DETAIL-BAR);
- the synchronized release ignored in BC11 (FEAS-SYNC, FEAS-SCN-2);
- the time budget below 1.0 h allowed (BUDGET);
- a stale probe counted Clear (XING-CLEAR);
- Remembered at 0.4 counted as holding (XING-REMEMBERED);
- the crossing via dropped when `AvoidCoveredCrossings` is set (VIA-ORDER);
- `after-dark` made `Impossible` (DAYLIGHT);
- the monitor's committed-wings exemption removed (MON-COMMITTED);
- the replan margin removed (REPLAN-HYSTERESIS);
- player-owned strength counted (FEAS-PLAYER);
- an exploiting child offered to the layout (BC13-EXCLUDE);
- the stance refresh moved back after the initial pick (FEAS-STANCE-INITIAL);
- a literal speed in place of `MarchRules` (MARCH-RULES-PIN);
- the crossing-threat release past the floor (RELEASE-CROSSING);
- an allocation in the Dijkstra (FEAS-ALLOC);
- the fix's synchronized hold removed (STRIKE-DOUBLE, FIX-HOLD);
- the Hold ceiling anchored to the live planned release instead of the latched one (CEILING-LATCH);
- the zone assignment projecting the authored template for a mirrored plan (SELECT-SIDE);
- the inward leg left unscreened (INWARD-LEG);
- crossing cells counted as land in the labelling (REG-SPLIT);
- the enemy's perceived morale ignored (ODDS-MORALE);
- the main body's `own` ratio set to 1 (ODDS-MORALE, FEAS-SCN-4);
- open-river samples at the leg's own crossing priced as a bend (LEG-FOOTPRINT).

### Smoke items (added to the BC7 checklist; item numbers assigned when the plan section is written, after 2.54 and 3.11)

1. `[RiverRegions] regions=… links=…` once per battle on a map with a river. `TacticalCrossingWatch` rows show the counts move from `unknown` to `clear` / `held` as BC9 and the look requests see the crossings.
2. `[Feasibility] first evaluation` and `TacticalPlanSelection` rows: candidates, filters, the top three with their feasibility terms. Before the river regions are ready every verdict is `Unknown` and every term 0 (criterion-selection-regression in game); a `map-known` replan follows once they are.
3. A river battle (the BC11 smoke save of item 2.28 or any save with a river across the front), run twice from the same pre-battle save, flag on and off: `TacticalEnvelopment` abandon reasons and `TacticalPlanSelection` winners (criterion-no-doomed-launch and its guardrail).
4. A flank march under MobileFlankMarch, WideFlankApproach or ManeuverAndFix: `TacticalEnvelopment` rows with `plannedReleaseHours`, `via`, `predictedArrivalHours` and `arrivedHours`. Watch the wing leave before the Center's contact when its march is long, and strike about `offset` after it (criterion-arrival-prediction).
5. A DoubleEnvelopment: the two Attack transitions (criterion-strike-spread).
6. Scopes `tactical.feasibility.evaluate|monitor|navmesh`, `tactical.river-regions.build` and `tactical.patch.battle-core-arbiter`, flag on and off (criterion-frame-budget).
7. `[Feasibility] first assumption replan` and `TacticalReplan` rows with `trigger=ManeuverAssumptionBroken` (criterion-replan-stability).
8. A Defender battle on a river map (the player attacks): the AI never opens with an offensive playbook (`TacticalPlanSelection` winner at `trigger=initial`); `TacticalReserveRelease` rows with `reason=crossing-threat` when the player's column heads for a ford; BC9 `Crossing` looks (criterion-defender-no-offense).
9. Session B (W&L): a player whose division is a wing of a dividing plan receives the March by dispatch, and the plan's `TacticalManeuverBody` strength excludes the player's command (criterion-player-never-moved).

### Coverage matrix (result column blank until prove)

| Requirement | Spec section | Test cases | Result | Defect / concession |
|---|---|---|---|---|
| requirement-candidate-set | § 4 | SELECT-SIDE, SIDE-RANK, FEAS-DEF | | |
| requirement-hypothetical-layout | § 4 | VERDICT-UNFILLED, DETAIL-NO-FIX, PROJECT-PARITY, ASSIGN-PARITY, BC13-EXCLUDE, FEAS-PLAYER | | |
| requirement-bodies | § 4 | FEAS-SCN-1, FEAS-SCN-3, TARGET-SNAP, ODDS-MORALE | | |
| requirement-river-regions | § 4 | REG-SPLIT, REG-CREEK, REG-FROZEN, REG-LINK-RADIUS, REG-STEP, REG-TRUNC | | |
| requirement-crossing-state | § 4 | XING-HELD, XING-WATCHED, XING-CLEAR, XING-REMEMBERED, XING-SHARED-RULE, XING-PROBE-CAP | | |
| requirement-route-screen | § 4 | ROUTE-SAME-REGION, ROUTE-VIA-CROSSING, ROUTE-NO-CROSSING, INWARD-LEG, VERDICT-UNKNOWN | | |
| requirement-leg-cost | § 4 | ROUTE-SAME-REGION, ROUTE-BEND, LEG-FOOTPRINT, MARCH-TIME | | |
| requirement-navmesh-confirm | § 4 | ROUTE-CONFIRM, ROUTE-CONFIRM-BUDGET, NAV-SITES | | |
| requirement-march-time | § 4 | MARCH-TIME, MARCH-RULES-PIN | | |
| requirement-courier-time | § 4 | COURIER-TIME | | |
| requirement-strike-time | § 4 | STRIKE-OFFSET, STRIKE-DOUBLE, STRIKE-FIX-CONTACT, STRIKE-ABSOLUTE, STRIKE-HELD, FIX-MEMBER-RELEASE, MULTI-RIVER | | |
| requirement-daylight | § 4 | DAYLIGHT | | |
| requirement-enemy-picture | § 4 | ENEMY-FOG-PREMIUM, ENEMY-NO-SAMPLES, FEAS-FOG, FEAS-PIN | | |
| requirement-shift-time | § 4 | SHIFT-PASSIVE, SHIFT-RIVER | | |
| requirement-detail-risk | § 4 | DETAIL-PIN, DETAIL-WINDOW, SUPPORT-TIME, DETAIL-BAR, DETAIL-PENALTY, DETAIL-NO-FIX, ODDS-MORALE, FEAS-SCN-2, FEAS-SCN-3 | | |
| requirement-route-exposure | § 4 | EXPOSURE-FOREST | | |
| requirement-held-crossing | § 4 | ROUTE-HELD-DETOUR, STRIKE-HELD, FEAS-SCN-4 | | |
| requirement-approach | § 4 | APPROACH-ANCHORED | | |
| requirement-player-share | § 4 | FEAS-PLAYER | | |
| requirement-penalty | § 4 | DETAIL-PENALTY, FEAS-SCN-3, FEAS-SCN-AUDACITY | | |
| requirement-verdict | § 4 | VERDICT-UNFILLED, VERDICT-UNKNOWN, VERDICT-RISKY, VERDICT-ORDER, DAYLIGHT | | |
| requirement-evaluation-cadence | § 4 | ROUTE-CONFIRM-BUDGET, smoke 2 | | |
| requirement-stance-before-pick | § 5 | FEAS-STANCE-INITIAL, smoke 8 | | |
| requirement-selection-term | § 5 | SELECT-FILTER, SELECT-JITTER, FEAS-REGRESS | | |
| requirement-never-empty | § 5 | SELECT-NEVER-EMPTY | | |
| requirement-selected-side | § 5 | SELECT-SIDE, MIRROR-FALLBACK, FEAS-SCN-4 | | |
| requirement-assumption-monitor | § 5 | MON-HELD, MON-LATE, MON-DETAIL, MON-ABANDON, MON-COMMITTED, MON-MAP-KNOWN, MON-ESTIMATE, MON-ROUTE-MISSING | | |
| requirement-assumption-replan | § 5 | REPLAN-HYSTERESIS, REPLAN-COOLDOWN, MON-SUPPRESSION, smoke 7 | | |
| requirement-synchronized-release | § 7 | FEAS-SYNC, SYNC-UNSET, SYNC-PIN-AT-RELEASE, CEILING-LATCH, FIX-HOLD, STATE-FLAGS, FEAS-SCN-2 | | |
| requirement-maneuver-time-budget | § 7 | BUDGET | | |
| requirement-crossing-via | § 7 | VIA-ORDER, VIA-ROUTE-CHECK, VIA-PASSED, FEAS-SCN-4 | | |
| requirement-wing-abandon-replan | § 7 | MON-ABANDON | | |
| requirement-bc13-commitment | § 7 | BC13-EXCLUDE, BC13-HALF | | |
| requirement-bc13-personality | § 7 | BC13-PERSONALITY, DETAIL-BAR | | |
| requirement-crossing-look | § 7 | LOOK-PRIORITY, LOOK-RECON | | |
| requirement-defender-exposure | § 7 | FEAS-SCN-5 | | |
| requirement-defender-crossing-release | § 7 | RELEASE-CROSSING, FEAS-SCN-5, smoke 8 | | |
| requirement-runtime-wiring | § 7 | FEAS-STANCE-INITIAL, MON-SUPPRESSION, FIX-HOLD, MIRROR-FALLBACK, RELEASE-CROSSING, smoke 2, smoke 7 | | |
| requirement-frame-budget | § 11 | FEAS-TIME, smoke 6 | | |
| requirement-zero-alloc | § 11 | FEAS-ALLOC | | |
| requirement-config | § 11 | config-audit 0 findings | | |
| requirement-telemetry | § 11 | smoke 1-9 | | |

---

## 14. Decisions and rejected alternatives

- **D1 Feasibility is a ledger on the heavy path, not a producer and not a patch.**
  - Problem: the evaluation feeds plan selection, which runs on the heavy path. It has nothing to write.
  - User effect: none directly. Its outputs reach units only through BC11's maneuver orders, which the posture producer submits to the executor as today.
  - Rejected: an `IBattleIntentProducer`. It would run every arbiter tick for a decision made every few minutes, and it would have no intent to submit.
- **D2 A river-region graph screens routes; NavMesh confirms the best.**
  - Problem: 18 candidates × 2 sides × several bodies is too many NavMesh queries for one tick, and a NavMesh query cannot be told to avoid a held bridge.
  - User effect: every candidate is judged, and the chosen ones on the real pathfinder.
  - Chosen: regions from the picture once per battle; per-leg line sampling on the grid; Dijkstra over the crossings; NavMesh legs through the chosen crossing, one per side per tick.
  - Rejected:
    - (a) NavMesh for every candidate: tens of queries per replan, and no way to price a held crossing.
    - (b) A grid A* over the whole map per wing: `RoadRoutePlanner`'s 6,000-cell corridor is too narrow for a wide flank march, and a full-map A* per body is the cost (a) avoids.
- **D3 The term only subtracts; only the `Impossible` reasons of requirement-verdict filter.**
  - Problem: a new score term could reshape every tuned selection.
  - User effect: the AI picks as before unless a plan has a reason not to work.
  - Rejected: a symmetric term that also rewards feasible dividing plays. That double-counts `EnvelopmentPressure`, which already rewards a dividing play under a clear advantage.
  - Rejected: filtering `Risky` plans. Lee's risks would become impossible; temperament must be able to choose them.
- **D4 Vanilla's own speed and courier formulas, not historical rates.**
  - Problem: the march estimate must match what the game will do.
  - Effect: predictions within the smoke's tolerance (criterion-arrival-prediction).
  - History sets the weights (§ 3); the clock is the game's. In the game a formed infantry column covers 6.3 km per game hour on a road, faster than the 2-2.5 mph Civil War pace. Using historical rates would mispredict every arrival.
  - Rejected: a fixed rate per formation, which would drift from vanilla's terrain costs.
- **D5 Defeat in detail as a time window against the enemy's shift time.**
  - Problem: "divide if reasonable" needs a measure of when dividing is unreasonable.
  - Effect: divided attacks are taken when the arithmetic holds, and not at even odds or piecemeal.
  - Chosen: the window in which a body stands alone, against the time the perceived enemy needs to bring its free strength over its interior lines; the isolated odds against the commander's bar.
  - Rejected:
    - (a) A flat "never divide below odds X": it ignores the timing that decided Antietam and Chancellorsville.
    - (b) A full battle simulation: unmeasurable, and not fog-honest to tune.
- **D6 Both sides of a single-flank play.**
  - Problem: the template's authored side is arbitrary ("mirror-arbitrary").
  - Effect: the AI turns the flank that has a route, which is how "pin and cross" emerges.
  - Rejected: keep BC11's single mirrored try after a failure, which learns the side only after committing to it.
- **D7 One planned strike time; every body released to meet it, the fix included.**
  - Problem: under BC11's rule a long flank march starts after contact and arrives an hour late, while the fix fights alone.
  - Effect: a long march leaves early, as Jackson's did, and the fix waits until its contact lines up with the wing, rather than going in alone (Mechanicsville).
  - Rejected:
    - (a) Keep BC11's rule. The arrival is then contact + offset + march, and the fix's lone fight is that long.
    - (b) Release only the wings and charge the fix for every hour it fights alone. A wide march would then carry a saturated cost on every side, and the side choice would fall to the tie-break.
  - The fix's hold never applies to a fix already in contact.
- **D8 A per-wing time budget.** BC11's fixed 1.0 h is 500 grass units for infantry. The budget is 1.5 × the prediction, floored at 1.0 h and capped at daylight.
  - Rejected: raise the constant, which would let a wrong maneuver run for hours.
- **D9 The chosen crossing becomes a via point.**
  - Problem: the pathfinder takes the cheapest crossing, which may be the held one.
  - Effect: the wing crosses where the plan said.
  - Rejected: rely on BC10's `AvoidCoveredCrossings` → `BlockedMovingOrder`. It exempts crossings near the destination, decides at issue time rather than plan time, and carries no via.
- **D10 Replan on broken assumptions, with margins and a committed-wings exemption.**
  - Problem: today a wing's failure leaves the plan running without it.
  - Effect: the army changes plan when its premise fails, but not on every flicker, and never pulls back wings already striking.
  - Rejected: replan on every verdict change, which thrashes under the 0.05 h evaluation cadence.
- **D11 The Defender's options are look, score and release, never strike.**
  - Problem: a Defender can be turned by an unwatched crossing.
  - Effect: it watches its crossings and shifts a reserve along its interior lines.
  - Rejected: a Defender flank attack. That is the standing rule; BC13's counterstroke is the only Defender attack.
- **D12 The army commander decides; wing commanders do not add friction (yet).**
  - Problem: the Seven Days failures came from subordinates.
  - Effect: deterministic, testable estimates.
  - Rejected for now: a temperament-scaled delay roll per wing commander (question-wing-commander-friction).
- **D13 The stance is read before the pick (bug fix).**
  - Problem: the initial pick runs on the default `Meeting` stance.
  - Effect: a Defender opens defensively.
  - Rejected: leave it to the first replan, which may be 600 battle-s away.

---

## 15. Amendments owed to the plan (the integrator writes them; this spec does not edit the plan)

1. **Renumbering, the second time.** The plan's "BC14 — battle AI level of detail" becomes **BC15**, and "BC15 — formations, slots and lanes" becomes **BC16**. A new "BC14 — maneuver feasibility" section is written from this spec. These move with them:
   - the dependency diagram in § BC10-BC12 (BC13 → BC14 maneuver feasibility → BC15 level of detail → BC16 formations);
   - the BC10-BC12 rules paragraph naming BC14 and BC15;
   - the "Later slices" line;
   - BC13's own section ("BC14 (level of detail) and BC15 (formations) carry the BC13 amendments");
   - the BC13 spec's § 15 items 2-3, which name "BC14 (level of detail)" and "BC15 (formations)" (an errata note; the spec is not mutated after it ships);
   - the level-of-detail section's "BC15 decision 12 adds triggers (g)-(j)" and "BC13's tiers" references;
   - the smoke item ranges.
2. **Level of detail (now BC15).** BC14 adds no producer, so the "13 producers" count stays. BC14's state that the tiered posture producer reads (a maneuver order's via, a planned release that changes the order's target on release) reaches it through promotion trigger (c), order target. A March with a via is not a static task, so the group is never `Distant` while it marches. No new trigger is owed. The level-of-detail equivalence test gains one case: a planned release on a group that was `Distant` the tick before.
3. **Formations (now BC16).** A maneuver March carrying a crossing via takes the road-route formation reason (`road-route`, March Column) for the legs up to the crossing. BC16's formation table must treat a via March like a routed move.
4. **BC11 code touch points** (BC14 edits them; the BC11 tests stay green with the flag off): `ManeuverInput.PlannedReleaseHours` / `TimeBudgetHours`; the Hold and timeout tests; `ManeuverOrder` and `ManeuverNodeEntry` via; `PlayExecutorRuntime`'s two-leg route check, abandon queue and mirrored projection (`ManeuverSpec.ProjectedMirrored`; BC11's own `Mirrored` keeps its meaning and starts false); `ManeuverOf`'s `HoldsForRelease` spec and `StepStartDelay`'s planned-release branch.
5. **BC9 code touch point.** `BattleRecon` gains the `Crossing` target kind.
6. **BC13 dependency.** BC14 reads `CommanderPersonalityTable`, `IPerceivedGround` (through `PerceivedGroundRuntime`), `ExploitTable.IsExploiting`, `OpportunityPersonality` and the reserve release state. BC14 cannot start before BC13 merges.
7. **`AGENTS.md`.** No write-owner change: BC14 adds no writer and no patch. The quick reference "Battle stance + plays" gains one sentence: plays are screened for feasibility before selection (`ManeuverFeasibility`), and a Defender's initial pick is stance-filtered.
8. **Patch catalog and bug ledger.** No new patch, so no ordinal. requirement-stance-before-pick is a Whiskey defect, not a vanilla bug, and it is recorded in the BC14 section's As built. Vanilla's reinforcement-schedule fog leak (§ 2.1 item 7) is recorded in `docs/bug-fixes/vanilla-tactical-battlefield.md` as a finding for the backlog (question-reinforcement-leak).
9. **Docs.** `docs/tactical-orchestrator.md` (Battle Core: feasibility, river regions, crossing watch; Doctrine Components: playbook feasibility), `docs/scourge-of-war-ai-anchors.md` (plays are screened before selection; Scourge's `PLAYFTime` timing is now an arrival-synchronized release), `docs/telemetry.md` (the new rows), `docs/tactical-operations-ledger.md` (the crossing-threat release).

---

## 16. Not verified (runtime-only claims; each is a smoke item or a harness case)

- The picture's 5-unit sampling marks every open river cell, so regions do not leak through a narrow river. A river narrower than 5 units could be missed (smoke 1 reports the region count; a map with one region and listed crossings is the symptom).
- Vanilla's crossing list holds every bridge and ford a NavMesh path uses. Pontoons (ids 9-13) are not in it (smoke 3 shows a NavMesh path through a crossing that is not a link as a `no-route` mismatch).
- The arrival model's unmodelled terms (slope, overlap waits, column length, `commanderspeedadjustment` for HQs) stay within criterion-arrival-prediction's tolerance (smoke 4).
- Whiskey's executor moves for a marching wing meet vanilla's order delay as modelled. The AI cheat that skips the delay in front of the enemy (`combatstatussubordinates > 0`) is read on vanilla's side and may shorten some delays (smoke 4).
- BC11's two-leg route check through a via is served within BC11's per-step budget (smoke 4).
- The day-end hour read from `uniStormSystem` matches the end-of-day cycle's trigger (smoke 4 near dusk).

---

## 17. Open questions for the user

- **question-feasibility-weight** — `FeasibilityWeight` = 0.4 against today's largest bias (defensive pressure 0.35) and the personality weight 0.5.
  - (a) Ship 0.4 and let smoke 2 and 3 tune it (recommended).
  - (b) Start lower (0.25) so tuned historical selections move less.
- **question-after-dark** — A wing that cannot strike before the day's end hour.
  - (a) `Risky` with the full daylight penalty, so it is rarely chosen but still possible (recommended: the night replan re-evaluates).
  - (b) `Impossible`.
- **question-defender-crossing-release** — The Defender's reserve release to a threatened crossing (`should`).
  - (a) Ship it behind the main flag (recommended).
  - (b) Its own flag.
  - (c) Leave it out; keep only the look requests and the scoring.
- **question-wing-commander-friction** — Seven Days-style lateness from subordinates.
  - (a) Not in BC14; estimates stay deterministic (recommended).
  - (b) Add a delay roll per wing commander, scaled by his BC13 caution and duty (a hit commander), which the monitor then sees as `late`.
- **question-even-odds-crossing** — On the Perryville-like map at even odds (§ 6 scenario 4), the evaluation judges one corps sent over the unwatched ford as `Risky`: its support must re-cross the river, so the enemy can concentrate on it first. The army then holds its own bank and probes. It never dives at the bridge. Pin-and-cross is chosen from about 2:1, or by a bold commander.
  - (a) Keep it: the support time counts the river, as Jomini's interior-lines test does (recommended; smoke tunes `HeldCrossingHours` and `DetailSlope`).
  - (b) Make pin-and-cross likelier at even odds. Count the fix as able to force the held crossing once the enemy has shifted away from it, which shortens the crossed wing's support time.
- **question-reinforcement-leak** — Vanilla's own AI reads the enemy's reinforcement schedule through `CheckIfUnitIsReinforcement` (no fog). BC14 never does.
  - (a) Record it in the bug backlog only (recommended).
  - (b) Plan a fix slice.

---

## 18. Out of scope

- Pontoon bridges laid during the battle (ids 9-13 and their placement) as crossings.
- Engineering a crossing (building a bridge) or bombarding a held one before crossing; the artillery doctrine is unchanged.
- Campaign-level turning movements; vanilla's campaign `aiarrivalfactor` stays vanilla's.
- Per-wing-commander delay friction (question-wing-commander-friction).
- A Defender's offensive maneuvers; BC13's counterstroke stays the only Defender attack.
- Retuning the catalog's existing weights, personality fit squash or affinity tables (BC13's out-of-scope item stands).
- New vanilla bug fixes. requirement-stance-before-pick is a Whiskey defect; the reinforcement-schedule leak is recorded only.
- Orders to the W&L player beyond BC11's existing dispatch of a maneuvering ancestor.

<!-- author-sweep: applied purpose-advantage-measurement, fit-criteria, guardrails, read-paths, roles, lifecycle, exceptions, concurrency, decision-records; n/a: none -->
