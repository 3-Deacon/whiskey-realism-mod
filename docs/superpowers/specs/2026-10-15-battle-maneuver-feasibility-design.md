Produced-by: claude-opus-5-5 (high)

# BC14 — Maneuver Feasibility (design spec)

**Status:** draft for review (gate: user approval of this spec, then the plan section). **Slice:** the new BC14 of the battle-core plan (`docs/superpowers/plans/2026-10-15-battle-core.md`), after BC13 (commander opportunity). The integrator placed it as its own slice right after BC13. The plan's current BC14 (battle AI level of detail) becomes **BC15**, and the current BC15 (formations, slots and lanes) becomes **BC16**. This spec does not edit the plan; § 15 lists what the plan owes. **Branch:** `feat/battle-next-bc14` from `feat/battle-next` once BC13 has merged back into it. **Vanilla:** decompile build 14703111 (`/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`); game data `Config/battleprefs.txt`, `Config/unitprefs.txt` (shipped install, read only). Anchors are method names first. Decompile and pref line numbers are only there to help find the code. Scourge of War is not installed on this machine; its anchors come from `docs/scourge-of-war-ai-anchors.md`.

**User decisions (2026-10-15, verbatim; relayed by the integrator):**
- Tactics: "Civil War pratices a a guide not a script". History sets weights and thresholds (§ 3), never a scripted replay of a battle.
- Scenarios: "There could be lots of scenarios. Not just one. Maybe they out number the enemy. 35,000 to 25,000. They divide their forces and attack from two directiosn, if reasonable." The user's own example: a Perryville-like 35,000 against 35,000 where one side holds a crossing. The attacker should consider pinning that crossing and sending a division over another bridge or ford, instead of diving at the held crossing.
- Placement ("when ever yo uthink is best"): the integrator made it its own slice after BC13.

**Decisions on this spec's open questions (2026-10-16):** recorded in § 14, D14-D19.

## Outline

0. Outline (this list)
1. Purpose, advantage, measurement
2. Ground truth: vanilla, Whiskey today, and what is missing
3. History as weights
4. (A) The feasibility evaluation: requirements
5. (B) How feasibility enters selection, and replanning: requirements
6. (C) The scenarios as emergent results (worked numbers)
7. (D) Interaction with BC11 envelopment and BC13 opportunity: requirements
7A. (F) Enemy works: turn them first, assault a focal point only when no turn will do
7B. (G) Objectives: their value, the victory clock, one main effort, and the historical chains
8. Interfaces (pure types)
9. Roles, lifecycles, shared rows and conflict rules
10. Exceptions per step
11. Performance, configuration, telemetry
12. Fit criteria
13. (E) Tests, mutation plants, smoke items, coverage matrix (the frozen test plan)
14. Decisions and rejected alternatives
15. Amendments owed to the plan (renumbering BC14 to BC15 and BC15 to BC16, BC11 and BC13 touch points)
16. Not verified (runtime-only claims)
17. Open questions for the user (none; decided in § 14 D14-D19)
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
5. **Works are turned, not stormed, when a turn is there.** Against an entrenched enemy the army leaves a force in front of the works to hold and sends its main body around an open end. When both ends are anchored, an aggressive commander concentrates on one focal point of the works (a salient, the weakest held stretch or a seam between two commands) with a column, its supports and what artillery can reach; a cautious one holds. A Defender in works keeps a reserve behind its most threatened point.
6. **The army strikes for what wins the battle.** The main effort goes to the most valuable objective it can feasibly take, by vanilla's own points, not to the nearest; a side behind on points as the day runs out presses harder and accepts more risk, a side ahead holds unless its commander is aggressive; a battle's historical chain is a strong prior, not a script.

Each of these is an emergent result of one evaluation, not a scripted case (§ 6).

**Measurement.** Each claim has a `criterion-*` in § 12. Each criterion names its value, its basis, how it is measured, a guardrail and the place the running mod emits the number.

---

## 2. Ground truth

### 2.1 Vanilla (build 14703111)

1. **Scale and clock.**
   - One battle world unit is 6.3 m on the ground: `GamePrefs.battlefieldrealdimensionfactor`, `battleprefs` "Calculation factor range to real distances … if terrain size is 2048 and this factor is 6.9 then the total battlefield size is 14150 meters", shipped 6.3. The bullet flight time at 108145 uses it.
   - Whiskey's existing code and docs call world units "meters" (for example `PostureSense.CloseContactMeters` = 250). This spec keeps that naming for constants and calls them **units** in prose: one unit is one Whiskey "meter".
   - Battle time is game hours (`currenttimefromstart`). The day length is `battledaylengthinmins` = 120 real minutes for 12 game hours (`battleprefs` "Day Length in Realtime Minutes (calculated out of a day of 6-18h)"), so one game hour is 10 real minutes at 1×. This matches `TacticalBattleClock.RealSecondsPerBattleHourAtOneX` = 600 (`OrderExecutor.cs`).
2. **March speed.** `Regiment.RegimentMove` (133063) computes:
   - **The base:** `num11 = unitbasicspeed[unittyp, mounted]` (133354). Infantry is 1, cavalry is 1 dismounted and 2 mounted, artillery is 0.5 unlimbered and 1 limbered, and groups use mounted (`unitprefs` "Basic Unit Speed Factor …").
   - **Terrain:** `num11 *= GetTerrainSpeedAdjustment() * basicunitspeed` (133525). `basicunitspeed` = 1000 (`unitprefs` "Basic unit speed (all)"), and `GetTerrainSpeedAdjustment` = `1 / terrainspeccost[currentterrain]` (132888-132901). The shipped costs: road 1, bridge 1, grass 2, ford ("River Crossing") 2, field 3, forest 4, fences and walls 4, creek 5, river 5 (impassable to the AI pathfinder unless frozen; BC10 finding 6), swamp 6. Road speed does not depend on formation in battle: the `formation != 3` road test at 133527 changes only a local that the campaign branch reads (133919-133923).
   - **The group:** `num11 *= Min(groupspeed, 1)` (133986). `groupspeed` is the slowest base factor among the group's regiments (`GetSlowestSpeedOfGroup` 91708-91734), so an infantry formation moves at the infantry base.
   - **Fatigue:** `num11 *= Pow(Max(1 − fatigue, 0.01), fatiguefactorspeed)` (133569), with `fatiguefactorspeed` 0.2.
   - **Running and double quick:** double quick is `doublequickspeedfact` (infantry 1.7). Running is `runningspeedfact`. Line infantry cannot run (`abletorun`).

   So formed infantry marches at 1000 units per game hour on a road and 500 on grass. A route's march time in game hours is `Σ (segment length × terrainspeccost) / (basicunitspeed × base)`. This is the same metre-cost sum that `RoadRoutePlanner.PolylineCost` already computes, divided by `basicunitspeed × base`.
3. **Order delay.** When `useorderdelay` is set, `SetWaypoint` queues the order on the unit's parent HQ (91487-91492). The delay is built from four pieces:
   - **Processing:** `AddToOrderQueue` (124917-124998) adds `processingtimegroupstandard` = 0.02 h (`unitprefs` "Standard processing time for group commander to write the order"). The × 3 (`additionalprocessingtimefeudsfactor`) applies when the queueing HQ's own `ai_feudstance` is set (124949-124953). Orders queued on one HQ are processed one after another (`queueprocessingtime`, 124954-124962), so a second order to the same HQ waits for the first.
   - **The first leg:** `AddOrderCourierline` (125009-125171). Within `standardbuglerange` (75 units) the leg is a bugle with recognition time `buglestandardrecognitiontime` = 0.02 h. Beyond it the leg is a real courier.
   - **Courier speed:** a courier rides mounted and running at `unitbasicspeed[5,1] × runningspeedfact[5] × 1000` = 2.33 × 2.2 × 1000 ≈ 5,126 units per hour on a road, ≈ 2,563 on grass. That is × 0.7 (`slowdowncourieroutsideradius`) when the source unit is detached.
   - **The chain:** at each HQ below, a secondary courier waits `processingtimegrouproute` = 0.02 h before riding on (125163). The group starts moving only when the last line has delivered (125280, `SetOrderStatus(2)` 125353). Movement is blocked while `pathstatus < 2` (134071-134074).

   No commander stat enters the battle delay; the stats are used only on the campaign branch. Vanilla's AI skips the delay entirely for a unit with `combatstatussubordinates > 0` (`debug_aicheat_noorderdelayinfrontofenemy`, default true, 91296-91298): an engaged AI unit moves at once. Vanilla's own long AI moves also skip it: the `BlockedMovingOrder` replay (3139), the two `FlankMove` waypoints (117089-117090) and `CheckForSeparatedEnemy` (8188) all call `SetWaypoint(useorderdelay: false)`. A vanilla-run enemy's shift may therefore arrive sooner than requirement-shift-time's courier hours allow (§ 16).

   Whiskey already models this delay for W&L orders: `WlCourierDelay.BattleHours` (processing on the first leg, route processing on later legs, a bugle within range, otherwise metres over `BattleCourierPrefs.SpeedPerHour`, `WlCourierDelay.cs`). The courier speed it is built with is `GamePrefs.unitbasicspeed[5, 1] * GamePrefs.runningspeedfact[5] * GamePrefs.basicunitspeed` (`WlOrderFlowRuntime`).
4. **Crossings.**
   - **The list:** fords (id 5) and bridges (id 6) are listed in `bfs.rivercrossingpositions`, de-duplicated at 20 units (26610-26640). Pontoons (ids 9-13) are not listed.
   - **The blocked set:** `UpdateBlockedCrossings` (7917-7973) marks a crossing blocked when a non-artillery enemy regiment within 1.5 × its fire range faces it within 45°. It also blocks the point one fire range ahead of a standing enemy in cover. The scan is over every enemy unit, with no fog test.
   - **The obstacles:** `BlockedMovingOrder` (3054-3150) carves NavMesh obstacles on the blocked points, except points within range of the destination (3089), and is used only for moves longer than 150 units (6953-6955).

   This is vanilla's only "use another bridge" behaviour, and it is reactive pathing, not planning.
5. **Flank moves and coordination.**
   - `CheckFlankMoves` (6247-6312) and `FlankMove` (116929-117108) are a local ±45° hook whose length is the flank unit's own `width` × `rangeofflankingmove` (117008-117009); the `groupwidth` read at 116988 is a discarded expression. A hook aborts on river id 4, or on an enemy 5 units above its turn point. Vanilla has no timing.
   - Arrive-together logic exists only in the campaign AI (`aiarrivalfactor`, `AICampaign` 8852 and 10244). No setter of it is in `AIBattle`.
   - Vanilla's isolated-cluster rule (`CheckForSeparatedEnemy` 8056-8215) attacks a cluster of value at most 0.65 × the group's own whose nearest neighbour is more than 1.3 × the group's distance to it away. That is the rule by which a vanilla-run enemy punishes a separated body.
6. **End of day.**
   - The day ends at `dayendhours[month − 1]` (`battleprefs` "End Hour per Month": 17 17 18 18 19 20 21 21 20 19 18 17) against `uniStormSystem.hourCounter`. The game then runs the end-of-day cycle (`eodcycle` 1, then 2; 86407-86434).
   - `RegimentMove` returns at once while `eodcycle > 0`, couriers aside (133238). A march not finished by the day's end hour is not finished that day.
7. **Works.**
   - **The unit scan** (`AIBattle`, 7962-7968): an enemy regiment with `covervalue` > 0.15, `coverobject` ≠ 3 (forest), not routed and with no path puts a point one fire range in front of itself into `blockedcrossings`.
   - **The marches:** line-group moves longer than 150 units use `BlockedMovingOrder` (6955) with those points as NavMesh obstacles. Vanilla therefore routes its marches around the field of fire in front of covered enemies, and does nothing more.
   - **The hotspot map** weights an enemy by strength × morale × (1 + `covervalue`) (8024).
   - **Cover:** `CheckCoyCovers` sets `covervalue` as a sum over the regiment's companies of 1 / (blockobjects − 1) × `coverobjectvalues[id]` (`GetCoverValueFromID`, 138514-138529), so full cover reads n / (n − 1) times the cover's value, and `coverobject` as the most common id. The ids (`GetCoverIDFromName`, 138490): fence 1, stone wall 2, forest 3, embankment (railroad) 4, creek 5, road 6, trenches 7-10, log wall (breastworks) 11, sunken road 15, unfinished railroad 16. The shipped values (`battleprefs` "COVER VALUES"): stone wall 0.35, earth parapet 0.40, reinforced parapet 0.45, parapet and abatis 0.50, parapet and obstacles 0.55, breastworks 0.15, sunken road 0.3. Breastworks clear the `> 0.15` test only at full cover (the n / (n − 1) factor).
   - **Entrenching at deployment** (85632-85642) picks the AI's fortification tier from `enemyentrenchmentlevel`, which `CalculateAllUnitsOfAlliance` averages from the groups' campaign `entrenchmentlevel` (87381-87399; vanilla divides the AI side's sum by the player side's count, which Whiskey's shipped `Enable Battle Entrenchment Average Fix` corrects). `FortificationGroups.Create(type, level, …)` drops its `level` (24364), so the AI's deployment works are only of two kinds: a trench line (id 7) or a log wall (id 11). The fortification groups (`BattlefieldSetup.Fortification`, 24315-24660, read in from `Battlemap/Map/AIFortifications`, 24512-24526) are occupied at deployment (85675-85733) and cleared again by the second side's pass.
   - **Every works line is an `ObstacleLine`:** the AI's (added to a fortification group's object by `Create`), the player's drawn at deployment (169201) and those loaded from a save (107103). Each keeps its pieces in the public `modelobjects` (each with `modelobj` and `completion`); `Create` stops adding pieces at `lengthneeded`, and `FinishImmediately` destroys those it could not afford (106906-106929).
   - **No plan:** vanilla has no plan to fix works and turn them, and no deliberate assault on a chosen point of them.
9. **Objectives.**
   - **Dynamic chains** (`CalculateNextObjectives`, 7425-7526) take the nearest objective not held by the side, by distance only (and not one another chain already set), skipping one whose path is blocked on the first run. They never read `Objectives.points` (105222).
   - **Defend** (`macroai` 2) re-takes an own objective with an enemy within 250 and no enemy within 250 of the group (7473-7481).
   - **Designer chains:** a historical battle's groups carry a `groupai` id (85262, `AddUnitToPool`); `CreateObjectiveChain` (7797-7822) loads that strategy's objectives and timed moves from `Config/aistrategies.dat` (`objectivetimetomove`); a chain group moves to its next objective only once its hour has come (6928); a chain that runs out switches to dynamic (7406-7410).
   - **Points** feed only the victory bar (`UpdatePoints` 84092-84175: the share of points held, `objectivepointsheld`, and the victory points, `pointsfromobjectives` from `alliancepoints` or `objectivepoints`), which drives the AI's retreat (`CheckGlobalAIStrategy`, 6336-6360, through `VictoryBarBelowTrigger`), and the deployment sort of own objectives by points (85619-85626).
10. **Fog leak (recorded, not used).** `CheckIfUnitIsReinforcement` (87508-87529) returns the enemy's `scheduledarrival` timing with no fog check, and vanilla's hotspot map reads it (7982-7995). Whiskey knows an enemy reinforcement only once its entry point is seen (BC2). BC14 never reads the schedule.

### 2.2 Whiskey today (`feat/battle-next` with BC13 merged from `main` at `578bd13`)

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
- **Objectives in Whiskey today.** The vision adapter gives every map objective a fixed value, 0.45 when the side holds it and 0.80 otherwise (`TacticalVisionRuntimeAdapter`); the director adds the BC12 terrain sign (`TacticalOperationDirector.ObjectiveSelectionScore`) and picks the best (`TryPickBestObjective`), and may open a second, parallel objective (`ParallelObjectives`). The plan's main-effort sector is the front axis's default (`ArmyEvidenceBuilder.DefaultMainEffortSector`). Nothing reads objective points, the victory bar, the time left or the designer chains.
- **Cover in Whiskey today.** `EnemyObservation` carries `CoverValue`, `CoverObject` and `Standing`, last seen while Visible (BC10), read only by `CoveredCrossings`. Own cover is read by the withdrawal inputs (`TacticalWithdrawalInputAdapter`), the morale pressure (`TacticalMoralePressure`), the artillery state and telemetry (`TacticalMapKnowledgeTelemetry` counts fortification groups). BC13's opening detector reads no cover and no works. Nothing plans against works.
- **No march-time, ETA or enemy shift-time estimator exists** anywhere in the battle code. The only unit-speed read is the W&L courier (§ 2.1 item 3).
- **Perception.** `BattleSnapshot.Observations` holds enemy regiments only (Visible, or Remembered with `Confidence01`), with no enemy group positions. `TacticalEnemyContactLedger` gives `EstimatedEnemyStrength`, `Confidence(nowHours)` (a method) and the enemy entry points with their watch stamps. `ArmyIntentInference` gives the enemy intent (Attack / Defend / Probe / Refuse / Withdraw).
- **BC13 (on `main` at `578bd13`; spec `2026-10-15-battle-opportunity-design.md`).**
  - The per-commander `CommanderPersonalityTable`. The army's vector is the table entry of the side's commanding officer.
  - `IPerceivedGround`: a point is perceived when vanilla's AI fog grid would show an infantry regiment standing there, or within `RangeOnlyReach` in RangeOnly mode.
  - `ExploitTable.ForAlliance(a).IsExploiting(int instanceId)`. Its rule that a maneuvering group is never an exploit asset, and that the play executor never maneuvers an exploiting group.
  - `OpeningDetector`: a Defender runs only `PenetrationPass`; `ClusterPass` (single link at `ClusterLinkMeters` 150) runs only on the non-Defender branch, over private arrays that surface only as `IsolatedCluster` openings.
  - BC13's enemy-sample filter (Visible, or Remembered at `Confidence01` ≥ 0.5; not a reinforcement; infantry, cavalry or artillery) is not a shared function: it is written inline in `OpeningDetector.BuildSamples`, `OpportunityTick` and `CavalryStrikeDisciplinePatch`.
  - `DirectChildAllocator.ReleaseReserves` runs, in this order: the crisis and stagger releases (`StaggeredRelease` with Enable Staggered Reserve Release, otherwise today's per-child loop), then BC11's flank release (`ReleaseReserveToThreatenedWing`). `ReserveReleaseState` is a per-`ArmyOrchestrator` instance (a child-id → release-hour map, R0, `LastReleaseHours`), cleared by `Replan` and `BattleCoreTick.StandDown`; the context carries it only with the staggered flag on.
  - BC10's deployed reserve: a child the executor placed as a deployment reserve (`DeploymentDoctrine.IsDeployedReserve`) keeps role `Reserve`; a play's rect never claims it (`DirectChildEvidence.DeployedReserve`), and `DeployedReserveMemory` remembers its release.
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
| Fredericksburg: works assaulted frontally | battlefields.org, Fredericksburg: "Wave after wave of Federal soldiers advance over open ground to take the road" (the sunken lane at the foot of Marye's Heights), and "Burnside's “diversion” against Longstreet's veteran Confederate soldiers produce horrific Union casualties." | A strike point on held works is a works assault: its bar is three times the commander's (requirement-works-pricing) |
| Cold Harbor: an assault into angled works | battlefields.org, Cold Harbor: "Angles in the Confederate works allow Lee's men to easily enfilade the Federal ranks as they advance." "An estimated 7,000 men are killed or wounded within the first 30 minutes of the assault" | `AssaultHours` 1.0: a break is made within the hour or not at all; the works hold gate keeps a hopeless assault from being ordered (requirement-works-hold) |
| Kennesaw Mountain: Sherman's one frontal attack of the campaign | battlefields.org, Kennesaw Mountain: "Sherman tried to avoid pitched battles that necessitated head-on assaults against fortified positions." The attack was "the first major frontal attack he ordered during the campaign." "By noon, the Union attack had failed." Then "Sherman maneuvered a force around Johnston's left flank" | Turn first: a strike beyond an open end pays no works bar, and a turning plan's fix holds rather than assaults (requirement-turn-first); a focal assault is offered only when no turn is feasible (requirement-focal-assault) |
| Franklin: a frontal charge against works over the objections of the commander's subordinates | battlefields.org, Franklin: "Unmoved by his lieutenants’ objections, Hood orders the charge." "The Confederates retreat, reform, and renew the attack as many as six times, but cannot dislodge the Union defenders." | Personality decides willingness, the model prices the odds: an aggressive commander's lower bar can accept an assault the model still scores; a hopeless one is held by the gate |
| Spotsylvania: a column at a salient, and the support that was missing | battlefields.org, Spotsylvania Court House: "Emory Upton briefly breaches the Confederate line with a tightly packed, fast-moving column of 12 regiments." His men "were left unsupported by other corps and forced to withdraw in the face of enemy artillery and mounting reinforcements." "Grant masses 20,000 men of the Second Corps opposite the tip of the salient." | The focal assault's column in March Column, its echeloned support released 0.1 h after the strike, and the salient factor 0.5 in the focal-point score (requirement-focal-execution, requirement-works-shape) |
| About three to one at the point of attack | The Dupuy Institute, "The U.S. Army Three-to-One Rule": "Various Three-to-one rules of thumbs have existed in the U.S. Army" "in writings possibly as early as the American Civil War (1861-1865)." | `WorksOddsFactor` = 3.0. The source dates the rule only to "possibly" the Civil War and tests it on 49 Civil War battles with too few 3:1 cases to confirm it, so 3.0 is a weight for smoke tuning, not a law |
| Throw the mass on the decisive point | Jomini Art. XIX maxims: "On the battle-field, to throw the mass of the forces upon the decisive point, or upon that portion of the hostile line which it is of the first importance to overthrow." and "To so arrange that these masses shall not only be thrown upon the decisive point, but that they shall engage at the proper times and with energy." | The main effort goes to the highest-valued feasible objective, valued by vanilla's own points, not the nearest (requirement-main-effort-objective) |
| One mass, no needless detachment | Clausewitz, *On War* (Graham tr., Gutenberg 1946), Bk. III ch. 11: "there is no more imperative and no simpler law for Strategy than to _keep the forces concentrated_.—No portion is to be separated from the main body unless called away by some urgent necessity." | One main effort per army; a secondary objective gets one screen, never a second main effort (`ParallelObjectives` closed under the flag) |
| The side ahead may hold | Clausewitz, *On War*, Notice: "the defensive form, with a negative object, is the stronger form, the attack, with the positive object, the weaker" | `H = ahead × (1 − agg01)`: a side ahead on points leans to holding what it has |
| Holding a won field can lose it | battlefields.org, Shiloh: "Believing his army victorious, Beauregard calls a halt to the attacks as darkness approaches." "He is unaware that overnight Buell arrives with reinforcements for Grant." | An aggressive commander's hold weight falls to near 0 (`1 − agg01`); the hold is a lean, not a rule |
| Behind as the light goes | battlefields.org, Gettysburg (July 2): "That afternoon Lee launches a heavy assault commanded by Lieut. Gen. James Longstreet on the Union left flank." "Confederate demonstrations escalate into full-scale assaults on East Cemetery Hill and Culp's Hill," and the Union defenders "hold strong positions as darkness falls." | Urgency `U` rises with the deficit and the lateness, and the day's end still caps what can be taken (requirement-victory-pressure, D16) |
| Column length | Civil War Monitor: "Estimated amount of roadway that 40,000 soldiers, with artillery and wagon trains, occupied on the march: 16 miles" | Not modelled: vanilla's march column is a group formation, and arrival is judged on the HQ as BC11 does. Derived figure (about 0.4 miles per 1,000 men) recorded only; smoke 4 measures whether the omission matters (§ 16) |

URLs: battlefields.org/learn/articles/10-facts-chancellorsville; gutenberg.org/ebooks/13549; nps.gov/frsp/learn/historyculture/cvillehist-flank.htm; battlefields.org/learn/civil-war/battles/chancellorsville; battlefields.org/learn/civil-war/battles/perryville; historyofwar.org/articles/battles_perryville.html; historynet.com/the-sounds-of-silence/; battlefields.org/learn/maps/antietam-burnsides-bridge-sep-17-1862; historynet.com/battle-of-antietam-controversial-crossing-on-burnsides-bridge/; nps.gov/people/isaac-peace-rodman.htm; npshistory.com/handbooks/historical/31/hh31h.htm; npshistory.com/handbooks/historical/31/hh31l.htm; npshistory.com/handbooks/historical/33/hh33l.htm; nps.gov/rich/learn/historyculture/glendalebull.htm; encyclopediavirginia.org/entries/mechanicsville-battle-of/; emergingcivilwar.com/2016/12/01/general-magruder-why-did-you-attack/; historynet.com/7-days-made-robert-e-lee-icon/; battlefields.org/learn/civil-war/battles/stones-river; battlefields.org/learn/articles/bragg-versus-rosecrans; battlefields.org/learn/civil-war/battles/nashville; battlefields.org/learn/civil-war/battles/chattanooga; battlefields.org/learn/civil-war/battles/bentonville; npshistory.com/publications/civil_war_series/15/sec6.htm; archive.org/download/infantrytacticsf01brig/infantrytacticsf01brig_djvu.txt; civilwarmonitor.com/?p=19266; battlefields.org/learn/civil-war/battles/fredericksburg; battlefields.org/learn/civil-war/battles/cold-harbor; battlefields.org/learn/civil-war/battles/kennesaw-mountain; battlefields.org/learn/civil-war/battles/franklin; battlefields.org/learn/civil-war/battles/spotsylvania-court-house; dupuyinstitute.org/2019/10/22/the-u-s-army-three-to-one-rule/ (works rows accessed 2026-10-16); gutenberg.org/ebooks/1946; battlefields.org/learn/civil-war/battles/shiloh; battlefields.org/learn/civil-war/battles/gettysburg (objective rows accessed 2026-10-16). Scourge of War (not installed here; `docs/scourge-of-war-ai-anchors.md`): its army plays wait on `PLAYFTime` and do not check rivers or heights on the way; its division think loop throttles orders by a courier timer (`CourTime + 15 * TICSPERMIN`). Neither times an arrival; BC14 is Whiskey's reading of those ideas.

---

## 4. (A) The feasibility evaluation

The evaluation is pure (`ManeuverFeasibility`, `Tactical/Orchestrator/Plays/`). It runs on the army's heavy path. It reads the side's `BattleSnapshot`, the BC10 grid, the BC12 picture, the BC14 crossing graph, the contact ledger, the intent inference and BC13's personality table, and publishes one `FeasibilityLedger` per side. It submits no intent and writes no vanilla state (§ 14 D1).

- **requirement-candidate-set** — The candidates are every playbook that passes the catalog's precondition and stance filters for the side's **current effective** stance: `TacticalStanceReconciler.ResolveEffectiveStance`, the stance `TryPickPlan` filters on, refreshed per requirement-stance-before-pick. Each is paired with its template (`PlayTemplateCatalog.Lookup`):
  - A template with exactly one enveloping rect off the centre line (MobileFlankMarch, WideFlankApproach, ManeuverAndFix) is evaluated **twice**: as authored, and mirrored by flipping the sign of every rect's lateral coordinate in the projection frame (the frame `PlayProjection` lays the rects out in, so the mirror matches what the executor will project). The better of the two sides is the candidate's result, and the side is recorded (`Mirrored`). Sides rank by verdict first (Feasible and Risky, then Unknown, then Impossible), then by the lower penalty; a tie keeps the authored side.
  - CavalryRaid is evaluated once, as authored. Its only enveloping rect (DeepRaid) lies at lateral 0, so the mirror would be the same layout; its side comes from `EnvelopmentManeuver.OutflankPoint`, as BC11 chooses it. `Mirrored` is false for it.
  - DoubleEnvelopment is evaluated once: it is symmetric. So is FocalAssault, which is centred on its focal point (requirement-focal-assault).
  - A template with no enveloping rect is evaluated once, as one body (requirement-bodies).
- **requirement-hypothetical-layout** — Per candidate (and side), the evaluation projects the template and assigns direct children exactly as the play executor would, through allocation-free overloads:
  - `PlayProjection.ProjectInto(template, …, Span<WorldPlayRect>)` and `PlayCorpsAssignment.AssignInto(rects, corps, Span<CorpsAssignment>)`, the same rules as the shipped methods; parity tests pin them.
  - Inputs are the ones `PlayExecutorRuntime.ComputeZoneAssignments` uses: the army centroid and the frontage estimate from **every active own unit**, unweighted (`PlayAnchor.CollectOwn`; a deployed reserve's units included), the enemy anchor (`PlayAnchor`, Visible observations), the crest line for ReverseSlopeDefense, and the direct-child set the coordinator passes, from which a deployed reserve is left out (its id is passed empty). For FocalAssault the enemy point is the focal point, so its forward axis, frontage and projection all run toward it.
  - The offered children exclude:
    - a BC10 deployed reserve (as today);
    - a child that is itself a live BC13 exploit asset (`ExploitTable.IsExploiting(child)`). A child with an exploiting descendant stays in the layout, at half strength (requirement-bc13-commitment);
    - a child whose subtree strength is wholly player-owned (requirement-player-share).
  - **`wing-unfilled`:** an enveloping rect that gets no child makes the candidate's verdict `Impossible`, reason `wing-unfilled`. The template cannot do what it says.
  - **`no-fixing-force`:** a template whose fixing rect (Center or Demonstration) gets no child while an enveloping rect does is not filtered. Its bodies are evaluated with no fixing force: no enemy strength is pinned (requirement-detail-risk). This is how "divide if reasonable" depends on the number of bodies the army has.
- **requirement-bodies** — A candidate's **bodies** are:
  - each child assigned to an enveloping rect, a separate body moving on its own route to its rect anchor (a DeepRaid to its outflank point first, `EnvelopmentManeuver.OutflankPoint`). FocalAssault's `AssaultColumn` rect is enveloping in this sense (`Envelops`: a separate body on its own route), and the child on its `AssaultSupport` rect joins the column's body, the assault body. The assault body's `T` and `S*` are the column's; the support's strength counts in `S_j` because it is released at the column's strike plus `EchelonDelayHours`, inside `AssaultHours`;
  - every other assigned child together, the **main body**, whose members stand within one frontage of each other and support each other.

  The main body's **route** is evaluated per member (each member to its rect anchor), and its contact time is the latest fixing member's arrival (requirement-strike-time).
  - **Fixing members:** every main-body member on a rect that is not Reserve or ReserveLine. They pin enemy strength (requirement-detail-risk).
  - **Reserve members:** they count in the main body's strength `S_main`, but pin nothing.

  **Detail risk applies only where a body is separate.** In a template with enveloping bodies, every body is judged, the main body included. Its isolated odds measure the fix standing alone while the wings are away. A template with no enveloping rect has one body and no detail term, unless that body assaults held works (requirement-works-pricing): one line supports itself, but not against works.

  **Strike points:**
  - an enveloping body's: the perceived enemy line end on its rect's side (`EnvelopmentManeuver.EnemyLineEnd`), or, for a DeepRaid, its outflank point, or, for an `AssaultColumn`, the focal point (requirement-works-shape);
  - the main body's: the play's enemy anchor (`PlayAnchor`'s enemy point, the strength-weighted centroid of the **Visible** enemy observations).

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

  **Probes.** Every link crossing is a probe candidate, oldest probe first, under its own cap of 16 probes per side (`MaxCrossingProbesPerTick`). Probes go through BC13's perceived-ground runtime (`IPerceivedGround.IsPerceived`). That runtime has no budget of its own (BC13's 128-point cap is enforced only in `PerceivedGroundLattice.Serve`), so BC14 opens its own handle (`PerceivedGroundRuntime.Open`) for the tick's probes and disposes it after them, and the probes run inside the `tactical.feasibility.evaluate` scope and count against its slice (requirement-frame-budget). They run before the tick's classification, so a probe made this tick counts. A probe result is stamped with the doctrine hour. A crossing is never Clear on the strength of a stale probe.
- **requirement-route-screen** — Every body member has two legs, and both are screened on the crossing graph by one rule, single-body plays included:
  - **The march leg:** from the member's current position (the child's record) to its target: the rect anchor, or for a DeepRaid the outflank point and then the anchor.
  - **The strike leg:** from the target on to the member's strike point (requirement-bodies). For an enveloping body that is the inward leg BC11 attacks along (anchor → enemy line end, or the outflank point for a DeepRaid). For a member of the main body it is its anchor → the main body's strike point (the enemy anchor). A fix that holds its anchor is still screened on the leg it would attack along, because a frontal attack across a river is the plan's own risk and must be priced like any other.

  Each leg is routed alike:
  - **Same region** (or a frozen map): one leg, origin to target.
  - **Different regions:** the cheapest path over the link crossings (requirement-link-legs). A node is a link crossing. An edge is a leg between two points in one region, priced by requirement-leg-cost. A Held crossing adds `HeldCrossingHours` (requirement-held-crossing) to the edge through it. Its crossings are the leg's crossings, and its **first** crossing is the leg's via (requirement-crossing-via): a march via for the march leg, an attack via for an enveloping body's inward leg.
  - **No path** (no chain of link crossings joins the two regions on an unfrozen map):
    - on a march leg: verdict `Impossible`, reason `no-crossing`;
    - on an enveloping body's inward leg: verdict `Impossible`, reason `river-on-leg`. BC11 would abandon it for that reason (requirement-crossing-via), so the screen refuses it before selection;
    - on a main-body member's strike leg: not `Impossible`. The member can still hold its anchor; its `contested` term is 1 at full weight and the term's reason is `no-strike-crossing` (requirement-held-crossing).
  - **A linked river on an inward leg is not `Impossible`.** The leg is routed through its chosen crossing and the attack via carries that crossing to BC11 (requirement-crossing-via), whose river check then judges the two sub-legs (§ 15 item 4). This keeps pin-and-cross (D14) launchable.
  - **Map unknown** (the picture or the regions not ready, or no grid): the screen has no result. The candidate's route term is neutral, and the verdict is `Unknown` (requirement-verdict). This is today's behaviour: BC11 still refuses to launch without a route.
- **requirement-leg-cost** — A leg's cost is the BC10 grid sampled every `LegStepMeters` = 10 units along the straight segment (`SampleLine`), summing `step × SpeedCost(id)`. The last sample is pro-rated to the leg's remaining length, so a 1,000-unit road leg costs exactly 1,000.
  - **Beside the leg's own crossing:** a sample on open river (4) within `CrossingLinkMeters` of the crossing the leg starts or ends at is that crossing's footprint. It is priced at the crossing's own cost (ford or bridge id).
  - **A bend:** any other open-river sample is a bend of a river the leg skirts. It is priced at the swamp cost, as a detour proxy, and the leg is marked `estimated` until a NavMesh result replaces it.
  - **March hours:** `cost / (BasicUnitSpeed × base) / fatigueFactor` (requirement-march-time).
  - **Dijkstra:** it runs in the body's hours. A Held crossing adds `HeldCrossingHours` to the edge through it, so the chosen crossing can depend on the body's speed.
- **requirement-link-legs** — Leg costs are computed once and reused:
  - **The link table (per battle).** When the regions are built, the leg cost between every pair of link crossings that share a region is computed by requirement-leg-cost (the footprint rule at both ends) and stored in cost units, which do not depend on the mover's speed. It is built in the same stepped build as the regions, one pair per unit of work, under `tactical.river-regions.build`; the map is unknown until it is complete. At most `MaxRoutedLinks` = 64 links are routed: beyond, the links farthest from the own centroid at build time are kept as crossings but not routed, with the `river-regions-truncated` OnceLog. That bounds the table at 2,016 pairs.
  - **The leg cache (per evaluation).** A leg from a point to a link of its region is keyed by (the point's 50-unit bucket, link); a leg between two points of one region by (bucket, bucket). The first point that fills an entry is the one priced, so two points in one bucket share a cost to within one bucket's diagonal (71 units). The cache is a pooled open-addressing table of `MaxLegCache` = 4,096 entries, cleared when an evaluation starts. On overflow a leg is computed and not stored, and the `TacticalManeuverFeasibility` row counts it (`legOverflow`). A NavMesh-confirmed leg (requirement-navmesh-confirm) replaces the cached estimate.
  - **The path search.** One query is a Dijkstra over the routed links of the two regions plus its two end points, in the mover's hours: cost / (`BasicUnitSpeed` × base) / fatigueFactor, plus `HeldCrossingHours` per Held crossing passed (for the enemy: per own-covered crossing, requirement-shift-time). Its arrays are pooled and sized `MaxRoutedLinks` + 2. Every route of every requirement (march, strike, `σ`, `τ`, the Defender's crossing arrivals and shifts) goes through this one query. A courier ride is a straight leg (requirement-courier-time) read from the same cache, with no path search.
- **requirement-navmesh-confirm** — NavMesh confirms screened routes on the evaluation's own budget:
  - **The queue:** the bodies of the top `ConfirmCandidates` = 3 candidates by catalog score without the feasibility term, the selected plan's bodies first.
  - **The query:** each route is confirmed leg by leg, through its chosen crossing (child → crossing, crossing → target), with the posture probe's filter (`PostureTargets.CreateNavFilter`) and ends at terrain height (`PostureTargets.AtTerrain`).
  - **The budget:** at most `ConfirmQueriesPerTick` = 1 `NavMesh.CalculatePath` per side per coordinator tick, separate from the posture producer's `NavPlanBudget` and from BC11's route pick, so contact groups never wait behind planning.
  - **The cache:** each result is cached per (origin 50-unit bucket, target 50-unit bucket, crossing index) for `ConfirmCacheHours` = 0.25 doctrine-h (the `RoutePlanCache` precedent).
  - **Results:**
    - A complete path replaces the leg's estimate with the path's polyline cost (`RoadRoutePlanner.PolylineCost`, the BC10 metre-cost sum; private today, made `internal` by BC14, § 15).
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
  - **Engaged members:** a member with a Visible enemy within `PostureSense.CloseContactMeters` (the snapshot's perceived contact) gets 0 courier hours for its own order to strike, because vanilla's AI moves an engaged unit at once (§ 2.1 item 3). This applies to `T` only. A support ride (requirement-detail-risk's `σ`) and the enemy's shift (requirement-shift-time) always carry their courier hours: each is a new order, sent after the evaluation can see its need, to a body whose engagement at that later hour is not known now. Both sides are charged alike, so the comparison of `σ` with `τ` stays even (§ 16 lists the vanilla cheat as unverified for these orders).
  - **Order delays off:** with `GameVars.useorderdelays` off, every courier time is 0, as vanilla's `SetWaypoint` sends at once.

  Whiskey's executor issues AI moves with the order delay (`CommandWaypointWritePolicy.ShouldUseOrderDelayForExecutorWaypoint`), so this is the delay its own orders meet.
- **requirement-strike-time** — Each body's time to strike if ordered now is `T = courier + march (+ HeldCrossingHours per Held crossing on its march leg) + DeployHours (+ inward surplus)`. `DeployHours` = `processingtimegroupstandard` + `EnvelopmentManeuver.TurnHoldHours`: the Turn phase BC11 already runs before the Attack.
  - **What `T` measures:** the hour a body launches its attack from its anchor. The straight inward leg is not in `T`: BC11 attacks from the Turn along it, and every template sets it short.
  - **The inward surplus** (enveloping bodies only): when the inward leg crosses a river, `T` adds `max(0, inward route hours − straight inward leg hours)`, both priced by requirement-leg-cost (the straight leg's open-river samples are bends at swamp cost), plus `HeldCrossingHours` per Held crossing on the route. A detour to a crossing delays the strike, and synchronization must see it.
  - **The main body's strike leg is not in `T`.** A fix pins by standing at its anchor; its own attack is a later decision of the plan's phase. Its strike leg is priced in the penalty (requirement-held-crossing).
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
  - **Several rivers:** a leg may cross more than one river. `HeldCrossingHours` is added once per Held crossing. `contested_j` takes the worst weighted state over all of them (requirement-held-crossing), and the monitor watches every one. Each leg's via (requirement-crossing-via) is its **first** crossing; the pathfinder takes the legs after it.
  - **Absolute hours:** a release is published as absolute doctrine hours, `PlannedReleaseHours = EvaluatedHours + R`.

  BC11 uses the planned releases (requirement-synchronized-release).
- **requirement-daylight** — `DaylightHours = dayendhours[month − 1] − hourCounter` (vanilla's own test, `uniStormSystem`), read by the runtime each evaluation. A body whose strike `S*` exceeds `DaylightHours − DaylightMarginHours` (0.25) gets `daylight_j` = 1 and the `after-dark` reason. Its verdict stays `Risky`: a night intervenes, and the night replan (`ReplanForNewBattleDay`) re-evaluates on the next day (D16).
- **requirement-enemy-picture** — The enemy as the side perceives it, built once per evaluation:
  - **Samples:** observations that are Visible, or Remembered at confidence ≥ 0.5; not reinforcements; infantry, cavalry or artillery. This is BC13's `requirement-enemy-samples`. BC13 writes that filter inline in three places (§ 2.2), so BC14 extracts it into one pure predicate, `EnemySamples.Accepts(in EnemyObservation)`, and the three BC13 sites call it (§ 15). It is one rule, not a fourth copy.
  - **Perceived strength** `P` = Σ Visible strength + Σ Remembered strength × confidence. **Perceived morale** `m_E` = the strength-weighted mean of the samples' `Morale` (last seen while Visible), or 1.0 with no samples.
  - **Effective strength:** `N = max(P, ledger EstimatedEnemyStrength) × (1 + FogPremium)`, with `FogPremium = 0.5 × (1 − confidence) × (0.5 + caution01)`. Here `confidence` is the contact ledger's `Confidence(nowHours)`, and `caution01` is the army commander's BC13 entry. A cautious commander assumes more of the unseen. Perryville is the weight (§ 3).
  - **The enemy mass:** the strength-weighted centroid of the samples.
  - **The local enemy** per own body, `L_j`: the samples within `OddsRadiusMeters` (BC13, 300 units) of the body's strike point (requirement-bodies), scaled by `N / P` when `P > 0`.
  - **Enemy bodies** (the Defender run, for requirement-defender-exposure, requirement-defender-crossing-release and requirement-defender-works-reserve; and the Attacker run when works exist, for the junctions of requirement-works-shape): the samples linked singly at `OpeningDetector.ClusterLinkMeters` (150), each body with its strength-weighted centroid and its strength. BC14 builds them itself, with the labelling extracted from `OpeningDetector.ClusterPass` into a pure `SingleLinkClusters.Label` that both call (§ 15, D24). At most `MaxEnemyBodies` = 16; beyond, the weakest are merged into the nearest kept body.
  - **No samples:** the enemy mass, and the one enemy body, is BC10's assumed enemy point (`DeploymentDoctrine.AssumedEnemyPoint`: the Visible enemy centroid outside RangeOnly when there is one, otherwise the nearest enemy-owned entry point), and `L_j = N / (bodies + 1)`.
- **requirement-shift-time** — The enemy's shift time to body `j` is `τ_j = EnemyCourierHours + march hours`.
  - **The march:** from the enemy mass (or, for a body whose strike point lies beyond the enemy's line end, from the perceived enemy line end on that side, `EnvelopmentManeuver.EnemyLineEnd`) to the body's strike point. It runs over the same crossing graph with the same leg cost, as infantry, base 1, unfatigued. When both points lie in one region the enemy crosses nothing.
  - **Crossing costs for the enemy:** a crossing costs the enemy `HeldCrossingHours` only when an own regiment covers it, by the own-side form of `CoveredCrossings.Covers` (the regiment's `RotationY` and `FireRange`; the form requirement-defender-exposure uses). Every other crossing costs it nothing extra: one it holds itself, so interior lines across the enemy's own bridges are counted, and one that is Watched, Clear or Unknown to the own side. The same rule applies in the attacker's run and in the Defender's (requirement-defender-exposure).
  - **`EnemyCourierHours`:** `processingtimegroupstandard` + `processingtimegrouproute` + the ride from the enemy mass to the strike point, priced as requirement-courier-time's ride. The enemy commander's position is not perceived, and his chain is assumed one relay deep. It is charged whether or not the enemy is engaged, as `σ`'s courier is (requirement-courier-time).
  - **A perceived passive enemy** (intent `Defend` from `ArmyIntentInference`, confidence ≥ its `ConfidenceFloor`) shifts slower: `τ_j × PassiveShiftFactor` (1.5). Hooker at Chancellorsville is the weight (§ 3).
- **requirement-detail-risk** — Per body `j` of a template with enveloping bodies (requirement-bodies), and of any plan whose body assaults held enemy works (requirement-works-pricing):
  1. **Pinned enemy:** `Pin_j` = Σ over the other bodies `k` with `S*_k ≤ S*_j` of `S_pin,k / DefenseRatio_k`.
     - `S_pin,k` is an enveloping body's strength, or the main body's fixing members' strength (never its reserve members).
     - `DefenseRatio_k = BaseDefenseRatio (1.5) + TerrainStrength` at `k`'s strike point on `k`'s approach axis (requirement-bodies; 1.5 when the picture is not ready). The enemy holds against superior numbers. When `k`'s strike point lies on a held works segment, `DefenseRatio_k` is at least `WorksDefenseRatio` = 3.0 (requirement-works-pricing): works hold a fix with a third of its strength.
     - A body that has not struck yet pins nothing: support out of earshot is not support (§ 3, Perryville).
  2. **Exposure window:** `W_j = (latest S* over the other bodies − S*_j)⁺ + σ_j`. `σ_j` is the **own support time**: the least, over the other bodies `k`, of `k`'s courier hours plus its march hours over the crossing graph (Held crossings at `HeldCrossingHours`) from where `k` stands when it has struck to `j`'s strike point.
     - `k`'s courier hours are those requirement-courier-time computes for an order from the army HQ to `k` at its current position, never zeroed for engagement (requirement-courier-time's engaged rule applies to `T` only). For the main body, they are the fixing member's that gives the least `σ`.
     - Where `k` stands: for an enveloping body, its strike point; for the main body, the anchor of each fixing member, the nearest counting. A fix holds where it stands; it does not stand on the enemy.
     - With no other body, `σ_j` = +∞ (no support comes).
     - The window and `τ_j` are Jomini's comparison (§ 3, Art. XXI): who can bring the masses to the point first. A body supported over shorter lines than the enemy's shift is safe; one on exterior lines is not.
  3. **Enemy against `j`:** `E_j = L_j + shiftShare_j × max(0, N − Pin_j − L_j)`, with `shiftShare_j = clamp01((W_j − τ_j) / max(τ_j, 0.1))`. A shift that cannot arrive inside the window adds nothing; one with twice the time adds all.
  4. **Isolated odds:** `odds_j = (S_j × m_j × own_j) / (E_j × m_E)`, BC13's local-odds form with morale on both sides. `m_j` excludes player-owned regiments, as `S_j` does.
     - `S_j` is the body's strength (requirement-player-share) and `m_j` its strength-weighted mean `GroupMorale`.
     - `own_j` is 1 for an enveloping body, and for any body that assaults held works (requirement-works-pricing), the main body of a single-body plan included: they attack.
     - `own_j` is `BaseDefenseRatio + TerrainStrength` for a main body that fixes and holds. It is taken at the anchor of its highest-priority fixing member (the Center when filled), on the axis toward the enemy anchor.
     - With `E_j = 0` the odds are `EnemyVisibleSector.MaxKnowableOdds` (10).
  5. **The commander's bar:** `Bar = clamp(1.0 + 0.3 × (caution01 − 0.5) − 0.3 × (audacity01 − 0.5) − 0.2 × (agg01 − 0.5), 0.6, 1.4)` (the army commander's BC13 entry). A body that assaults held works is held to `Bar_j = WorksOddsFactor × Bar` (3.0 × `Bar`, requirement-works-pricing); every other body to `Bar_j = Bar`.
  6. **The detail penalty:** `detail_j = clamp01((Bar_j − odds_j) / (Bar_j × DetailSlope))`, `DetailSlope` = 0.25. Odds a quarter below the bar give the full penalty.

  A template whose fixing rect is unfilled has no fixing force: its wings pin each other (by the formula above), and nothing else is pinned (requirement-hypothetical-layout).

  **The strike times are an input.** Steps 1-6 are one pure function, `ManeuverFeasibility.DetailRisk`, that takes the bodies and their strike hours as a span (`ReadOnlySpan<float> strikeHours`, one per body). `Evaluate` passes the synchronized `S*` of requirement-strike-time. The harness passes other strike hours to measure a plan without the synchronized release (FEAS-SCN-2: every body at its own `T`). It is the same function, so the comparison needs no test-only path.
- **requirement-route-exposure** — For an enveloping body only: the share of its route samples, outside its last `EnvelopmentManeuver.ContactMeters` (250 units), that lie within a perceived enemy sample's own reach (the `RangeOnlyReach` of requirement-crossing-state: where that regiment would see the column). Forest (14) samples count at half weight, because woods screen a column. It gives `exposure_j` in [0, 1]. A flank march seen on its way can be met. Chancellorsville is the weight: Jackson's column was seen and the warning was ignored (§ 3). The main body's route ends in contact by design and carries no exposure term.
- **requirement-held-crossing** — A route through a Held crossing costs time and blood:
  - **Time:** `HeldCrossingHours` = 1.0 is added to the edge through it in the path search (requirement-link-legs) and, on a march leg or an enveloping body's inward leg, to the body's `T` (requirement-strike-time). It is the time to carry the crossing. The search therefore prefers any Clear or Unknown alternative that is less than an hour's march longer.
  - **Blood:** a crossing's state value is 1 for Held, 0.5 for Watched, 0.25 for Unknown and 0 for Clear. Antietam's Rohrbach Bridge and Snavely's Ford are the weights (§ 3). `contested_j` is the largest **weighted** value over the crossings the body's members still have to pass:
    - a crossing on a march leg or on an enveloping body's inward leg counts at full weight: the body takes it whatever happens next, the march now and the wing's attack at its planned hour;
    - a crossing on a main-body member's strike leg counts at `StrikeLegWeight` = 0.5: that attack is ordered only when the plan's phase later reaches it, a decision taken on the odds of that moment, and the evaluation re-reads the crossing's state every `EvaluateIntervalHours` until then;
    - a main-body strike leg with no path counts 1 at full weight (`no-strike-crossing`, requirement-route-screen).
  - **Not impossible:** a route whose crossings are all Held is costly, not impossible. Burnside's bridge was carried.
- **requirement-approach** — Per body, at its strike point and on its approach axis (requirement-bodies):
  - `approach_j = 0.5 × TerrainPicture.ApproachDifficulty + 0.5 × enemyGround`;
  - `enemyGround` is the `TerrainStrength` at the strike point on the approach axis when perceived enemy samples stand within `OddsRadiusMeters` of it, and 0 otherwise;
  - an enveloping body whose strike point (the enemy line end) `TacticalTerrainLinePlanner.FlankAnchored` reports anchored, outward along the front axis, adds `AnchoredEndPenalty` (0.5) to `approach_j`, clamped to 1. A turned flank is no flank if it rests on a river or a crest.

  With the picture not ready, `approach_j` is 0.
- **requirement-player-share** — A body's strength `S_j` excludes the strength of `PlayerOwned` units in its subtree (BC3). The AI cannot count on the W&L player to march where it plans, and the evaluation never assigns a task to a player-owned unit. A child whose whole subtree is player-owned is not offered to the hypothetical layout (W&L discovery already drops it, `DirectChildDiscoveryRuntime`). A child that only contains player-owned units is offered and may take any rect but FocalAssault's `AssaultColumn` and `AssaultSupport` (requirement-focal-execution: one rule for the column); on every rect the executor's chain fan-out keeps each write, formation included, off the player's units.
- **requirement-penalty** — Per body: `penalty_j = 1 − (1 − daylight_j)(1 − detail_j)(1 − 0.6 × contested_j)(1 − 0.5 × approach_j)(1 − 0.4 × exposure_j)`, with each term defined above.
  - The main body's `contested`, `approach` and `daylight` are taken per fixing member, the worst member counting.
  - **For a Defender** (stance Defender, the candidates of requirement-defender-exposure): `penalty = 1 − (1 − 0.6 × turn)`. Its `daylight`, `detail`, `approach` and `exposure` are 0, and `contested` is replaced by `turn`: it attacks no one, so its strike time, the ground in front of the enemy and its own route's crossings are no costs. The route screen's `Impossible` reasons (requirement-verdict step 3) still apply, because a rect anchor across an unbridged river cannot be reached in either stance.
  - The time a plan takes is not a cost by itself: the synchronized releases make a long march wait-free for the others (requirement-strike-time). Its costs are the night (`daylight`), the enemy's chance to shift (`detail`) and the chance to be seen (`exposure`).

  The candidate's penalty is the **worst body's** penalty: a plan fails at its weakest body. The reason is the verdict reason, or else the name of the largest term above `ReasonThreshold` = 0.2 (`after-dark`, `detail`, `contested`, `approach`, `exposure`); a `contested` that comes from a strike leg with no path reads `no-strike-crossing`.
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
- **requirement-evaluation-cadence** — The ledger re-evaluates every candidate at most every `EvaluateIntervalHours` = 0.05 doctrine-h, on the coordinator's heavy path, before `ArmyTickCycle.MaybeReplan`. It also re-evaluates at once before the initial pick and before the night replan. At battle start the initial pick's evaluation is normally `Unknown` (no direct children attached yet, the picture not ready), and requirement-assumption-monitor's `map-known` screens that pick once the map is known. The selected plan is re-evaluated on every new heavy snapshot (`_snapshotGeneration`) for the monitor.
  - **Slices.** A scheduled evaluation is a resumable job: it runs in slices of at most `EvaluateSliceMs` on successive heavy ticks (requirement-frame-budget), on a second buffer, and the ledger publishes it whole when its last candidate is done, so a reader never sees a half-evaluated ledger. A new evaluation starts when the previous one is published and `EvaluateIntervalHours` has passed since it started.
  - **Forced evaluations** (before the initial pick and the night replan) run to completion in one tick, under the same scope, marked `forced` on the row.
  - **Freshness:** a replan reads the latest published ledger, at most `EvaluateIntervalHours` plus one evaluation's slices old; `TacticalPlanSelection` reports its age (`ledgerAgeHours`).

---

## 5. (B) Selection and replanning

- **requirement-stance-before-pick** (bug fix, outside the flag) — The stance and the opportunity flags are refreshed before every plan pick: `ArmyPostureInputsRuntime.Refresh`'s stance and opportunity writes run before `PickInitialPlan` and before `MaybeReplan` in the same tick. The initial pick therefore applies the stance filter (a Defender never opens with an offensive playbook), and a replan sees this tick's stance. This is a defect in today's ordering (§ 2.2), and it is fixed because the evaluation depends on the stance.
- **requirement-selection-term** — `PlaybookContext` gains `IFeasibilityLookup Feasibility` (null with the flag off). In `Select`, after the jitter draw and after the crest check (so the jitter stream does not depend on feasibility, the BC12 lesson):
  - **`Impossible`:** the candidate is skipped and counted (`feasibilityFiltered`).
  - **Otherwise:** the score gains `− FeasibilityWeight × penalty`, with `FeasibilityWeight` = 0.4 (D15). It is 0 for `Feasible` with no penalty and for `Unknown`.

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
- **The enemy:** perceived at depth 600 in three equal clusters at lateral −450, 0 and +450, each one Visible infantry sample (reach 200, the shipped infantry reach), morale 1.0. Its intent is not `Defend`. The contact ledger's `Confidence` is 0.8 and its `EstimatedEnemyStrength` is the perceived total (25,000 in scenarios 1-3), so `N = P × 1.1`.
- **The commander:** mid (agg01, caution01 and audacity01 all 0.5), so `Bar` = 1.0 and `FogPremium` = 0.5 × 0.2 × 1.0 = 0.1. `OpposingCommanderHint` 0.
- **The rest:** stance Attacker; June, 10:00 (10 h of daylight); cavalry fraction 0; terrain Open; `CurrentOdds` = own / perceived; AllianceId 0 (jitter seed 7). The direct children are listed left to right, so a tie in the zone assignment goes to the left-most child.
- **Courier legs:** the army HQ stands at depth −700, lateral 0; each corps HQ stands at its corps' position, with its brigades within bugle range (75) of it. That gives courier hours of 0.31-0.34 h, written 0.32 below.
- **March legs:** the fixture's `IConfirmedLegs` fake (the NavMesh seam of requirement-navmesh-confirm) returns every body member's leg to its rect anchor as confirmed, at the length stated below on grass (cost 2 per unit), so the stated `T` values hold. Every other leg (support, shift, inward) is priced from the grid, as in the game. The asserted values do not depend on the members' marches except through `S*`, which is what the injection fixes.
- **The assertions:** the verdicts, and each `detail` within ±0.05. The courier spread of ±0.02 h moves no asserted `detail` across its tolerance: each asserted `detail` is saturated at 0 or 1, and the closest, the scenario 1 wing, has odds 0.17 above its `Bar`, where 0.02 h of `σ` moves `E` by about 50 men.

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
2. **The same army attacks piecemeal.** DoubleEnvelopment as in 1, but the left wing's route crosses a creek and a swamp: `T_left` = 2.4 h, `T_right` = 1.2 h, `T_main` = 0.79 h. The creek and the swamp lie only on the left wing's own route: the fixture injects the three members' march legs (march hours 2.01, 0.81 and 0.40), and places no terrain, so every support and shift leg is the open grass of scenario 1.
   - **With synchronized release:** every body strikes at 2.4 (the right wing waits 1.2 h, the fix 1.61 h). The numbers are those of 1, and every `detail` is 0.
   - **With every body released at once** (no BC14 release; the harness calls `DetailRisk` with each body's strike hours at its own `T`, requirement-detail-risk): the fix strikes at 0.79, the right wing at 1.2, the left at 2.4.
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
   - **Own side:** three corps of 12,000 (left, lateral −300), 11,000 (middle, 0) and 12,000 (right, +300) at depth 0. The projection's half frontage is 450 (spread 600 × 1.5).
   - **The river:** open, across the front on grid depths [240, 260), one 20-unit picture row. Bridge B at (0, 250) is Held by a Visible 1,500-man brigade at (0, 300) facing it. Ford F at (−900, 250) is Clear (probed 0.1 h ago, no perceived regiment within reach). The speed-cost table is the shipped one (grass 2, ford 2, bridge 1, river 5).
   - **The enemy:** three perceived clusters of 11,000, 11,500 and 11,000 at lateral −400, 0 and +400, depth 556; its line extent ±450. With the brigade, `P` = 35,000 and the enemy anchor (`PlayAnchor`, Visible observations) is (0, 545). The ledger estimate is 35,000, so `N` = 38,500. Its intent is not `Defend`.
   - **Where the anchors fall:** forward fractions × 545. Every rect at forward 0.4 or less (depth ≤ 218) stands on the own bank, at least 22 units short of the river; every rect at forward 0.5 or more (depth ≥ 272.5) stands on the far bank, at least 12.5 units past it. No anchor falls in the river row, so TARGET-SNAP moves none.
   - **The ground at the enemy anchor:** the BC12 transect 300 units in front (depth 245) lies in the river row, so `TerrainStrength` there is 0.4 (`DefenseRatio` 1.9). `ApproachDifficulty` is 0.05 (one river sample of fifteen). A main body's approach is 0.5 × 0.05 + 0.5 × 0.4 = 0.225.
   - **The rest:** the scenario 1-3 conventions otherwise, AllianceId 0 (jitter seed 7).
   - **At even odds:**
     - **Every dividing play pays `detail` 1.** The mirrored ManeuverAndFix wing (left corps, 12,000) marches to (−382.5, 272.5) through F (2.34 h, `T` 2.73), and the Center and Demonstration fix on the own bank (`T` 0.72; they wait 1.93 h). The wing's support must re-cross the river through B (`σ` = 0.32 + 2.25 = 2.57 h against `τ` 1.12), so the whole free enemy can reach it: `Pin` = 23,000 / 1.9 ≈ 12,100, `E` ≈ 26,400, `odds` 0.45. The mirrored WideFlankApproach wing: `Pin` ≈ 5,800, `E` ≈ 32,700, `odds` 0.37. Every authored side marches through B. DoubleEnvelopment's right wing attacks through B on its inward leg (`contested` 1).
     - **The plays that cross at once pay the held bridge in full.** FrontalShockAssault, MeetingEngagement, ForcedFrontalAttack and GenericDesperate put their Center at forward 0.5, on the far bank. Its march leg takes B (+1.0 h; F is a 1,834-unit detour, 3.7 h): `contested` 1, penalty 1 − 0.4 × (1 − 0.5 × 0.225) ≈ 0.645, a term of about −0.26.
     - **The bank-holding single-body plays** (GenericAggressive, ContinuousAttrition, HesitatedCommit, GenericMethodical; every rect at forward 0.4 or less) cross nothing on the march. Each fixing member's strike leg to the enemy anchor takes B, which counts at `StrikeLegWeight` 0.5: penalty 1 − 0.7 × (1 − 0.5 × 0.225) ≈ 0.379, a term of about −0.15.
     - **So, at even odds,** the hold outcome comes from the model: dividing costs −0.4 (the detail term), crossing at once costs about −0.26, holding the bank costs about −0.15. With the flag off, the shipped scores pick ManeuverAndFix (0.618 against GenericAggressive's 0.591, the crit probe of the shipped catalog at odds 1.0); with it on, GenericAggressive (≈ 0.44) wins, and every bridge-crossing play trails every bank-holder by at least 0.1 in the term, so the result does not rest on the base scores' order among single-body plays. The army closes to its own bank, probes, and looks at the crossings (requirement-crossing-look). FEAS-SCN-HOLD pins it.
   - **At 2:1** (corps of 24,000, 22,000 and 24,000 against the same 35,000; `EnvelopmentPressure` on):
     - **ManeuverAndFix mirrored:** the Center and the Demonstration pin 46,000 / 1.9 ≈ 24,200 along the river. The wing (24,000) crosses at F on its march leg, so its march via is F and its inward leg stays on the far bank: `E` ≈ 14,300, `odds` 1.68, `detail` 0, `contested` 0, penalty ≈ 0.01. The fix's own odds are 46,000 × 1.5 / 38,500 = 1.79, `detail` 0; its strike legs take B at half weight, penalty ≈ 0.379. The candidate's penalty is 0.379 (`Risky`), the field's least: every bank-holder shares it, and every other candidate is higher (the authored side and DoubleEnvelopment through B, ≥ 0.6; WideFlankApproach and MobileFlankMarch mirrored, wing `detail` ≈ 0.43). So the term cannot move it below any rival, and its base lead under `EnvelopmentPressure` selects it (crit probe: 0.711 against ContinuousAttrition's 0.596). The Center fixes B by fire, and the wing marches with F as its via: **pin and cross**. It emerges when the army can afford a separate crossing: Jomini's "vastly preponderating" condition, and the Fredericksburg-downstream / Antietam-ford pattern of § 3.
   - **A bold commander needs less of an edge** (D14). The mirrored ManeuverAndFix wing's odds rise with own strength. Scaling the three corps together against the fixed enemy, its `detail` reaches 0 at a strength ratio of 1.60 for the mid commander (`Bar` 1.0) and 1.50 for one with audacity01 0.9 (`Bar` 0.88). FEAS-SCN-BOLD finds both ratios by bisection.
   - **If F were Unknown instead:** `contested` = 0.25 on the wing's march, and F becomes a look request (requirement-crossing-look): the Snavely's Ford lesson.
5. **The Defender's options.** A Defender (vanilla battle type 2) is never offered an offensive candidate (the stance filter, now applied at the initial pick by requirement-stance-before-pick). Its candidates are the five Defensive and two Neutral playbooks, all single-body, so the attacker's detail and contested terms do not discriminate between them. Three things act for it:
   - **Turning exposure** (requirement-defender-exposure): the evaluation is run from the enemy's side against the Defender's line. Every uncovered link crossing on the Defender's flank is timed: the earliest enemy body's march to it and on to the line's flank end, against the Defender's fastest reserve. A line that can be turned before it can be shifted is penalized. A crossing in front of the line's middle half is not a turning threat; the line faces it.
   - **Look requests** for Unknown crossings within the enemy's reach (requirement-crossing-look).
   - **A reserve released to a threatened crossing** (requirement-defender-crossing-release), the interior-lines answer Lee used at Antietam. The fastest real Reserve child guards the crossing from its own bank and stays while the threat holds.

   **The fixture (FEAS-SCN-5).** Stance Defender.
   - **Own side:** three corps of 11,000 in line at (−300, 200), (0, 200) and (+300, 200), facing +z, and a fourth child of 8,000, a BC10 deployed reserve, at (0, −100). Every child, the deployed reserve included, has the same number of regiments, each standing at its corps HQ, except the middle corps' lead regiment at (0, 250), facing the bridge, with `FireRange` 150. The army HQ stands at (0, −500).
   - **The projection, as the executor's** (requirement-hypothetical-layout): the centroid and frontage come from every active own unit, the deployed reserve's included (`PlayAnchor.CollectOwn`, unweighted): centroid (0, 125), lateral extent −300 to +300, half frontage 450. The deployed reserve is not offered to the zone assignment.
   - **The river:** open on grid depths [300, 320). Bridge B at (0, 310) is covered by the lead regiment (own-side `Covers`). Ford F at (+1,000, 310) is uncovered (the nearest own regiment is 708 units away), on the flank (lateral 1,000 > +150, the outer quarter's edge) and Clear.
   - **The enemy:** three Visible bodies on the far bank: 22,000 at (0, 700), and two columns of 5,000 at (±1,000, 420). Their centroid is (0, 612.5), so the projection distance is 487.5.
   - **The rest:** grass, fresh infantry, mid commander, June 10:00. Courier hours are requirement-courier-time's from the army HQ to each child's current position: 0.33-0.36 h to the line corps, 0.20 h to the deployed reserve (400 units).
   - **The candidates** with these inputs (no crest, cavalry fraction 0): PreparedDefense, DelayingDefense, GenericCautious, MeetingEngagement and GenericMethodical.
   - **The threat at F:** the right column is the earliest body: 110 units of march plus its courier, `enemyAtCrossing` = 0.30 h.
   - **DelayingDefense** (ForwardScreen (0, 368.75) → middle, CenterHold (0, 173.75) → left, ReserveLine (0, −70) → right): the flank end is CenterHold, so `enemyAtFlank` = 0.30 + 2.02 = 2.32 h. Its fastest reserve source is the deployed reserve from (0, −100): `ownShift` = 2.36 h (the ReserveLine child from its anchor, 2.48). `turn` ≈ 0.04.
   - **PreparedDefense** (CenterHold (0, 125) → middle, LeftFlank → left, RightFlank (+360, 125) → right; its ReserveLine is empty): the flank end is RightFlank, so `enemyAtFlank` = 0.30 + 1.33 = 1.64 h. Its only reserve source is the deployed reserve, `ownShift` 2.36 h: `turn` ≈ 0.72, penalty ≈ 0.43. A wide line whose only reserve stands behind its centre can be turned at F before the reserve gets there.
   - **Without the deployed reserve** (centroid (0, 200), distance 412.5): PreparedDefense has no reserve source and falls back to its RightFlank anchor (+360, 200): `ownShift` 1.64, `enemyAtFlank` 1.60, `turn` ≈ 0.03. It has no Reserve child at all, so no crossing release is possible.
   - **The release** (DelayingDefense selected): the eligible Reserve children are the ReserveLine child (the right corps, at its current position (+300, 200): `ownShift_c` = 1.75 h) and the deployed reserve (2.36 h). The right corps is chosen; 0.30 ≤ 1.75 + 0.25, so it is released to `RefuseRight` with its guard point at (1,000, 210), facing F. With the right column at (1,000, 1,500) instead, the earliest body at F is the main body at (0, 700) (`enemyAtCrossing` 2.61 h > 2.00), and nothing is released.

   Nothing in it is offensive. BC13's bounded counterstroke stays the Defender's only attack.
6. **An entrenched line with an open flank (35,000 against 25,000): turn it.** The scenario 1 armies and conventions, but the enemy's left and centre clusters stand in earth parapets (Visible, `CoverObject` 7, `CoverValue` 0.40, standing), on a held segment from lateral −600 to +150 at depth 600, with a built works point every 50 units along it that the side has perceived. The picture's default cover table holds fence, wall, railroad, creek, sunken road and unfinished railroad only (control: `TerrainPicture.BuildDefaultCoverValues`'s six `table[...]` entries, reads 6, under: source; failure-under: source), so the parapets enter only through the works picture; the same holds in scenarios 7 and 8, whose works points also stand every 50 units along their profiles. Its left end is anchored on a river (a river column beyond lateral −620); its right end is open, and the right cluster (450, 600) stands in the open beyond it.
   - **ManeuverAndFix, authored on the right:** the wing strikes the right line end (450, 600), 300 units beyond the open end, so it is not a works assault. The fix, facing the works, pins at the works defence ratio: `Pin` = 23,000 / 3.0 ≈ 7,700 (15,300 on open ground). With scenario 1's `σ` 1.55 and `τ` 1.12, `E` ≈ 9,200 + 0.38 × 10,700 ≈ 13,300 and `odds` ≈ 0.90: `detail` ≈ 0.38, `Risky`. The fix's own exposure counts only the enemy outside works (the open cluster, `N_open` ≈ 9,200, `τ` 1.12 from it): with its window ≈ 1.30 h, `shiftShare` ≈ 0.16, `E` ≈ 1,470 and `odds` ≈ 23, `detail` 0.
   - **Every works assault pays the bar of three:** the mirrored side and DoubleEnvelopment's left wing strike (−450, 600) on the works; the single-body plays' main body strikes the enemy anchor on the works with `W` = 1.0, `own` 1 and the whole free enemy against it (`odds` 35,000 / 27,500 ≈ 1.27 against `Bar_j` 3.0): `detail` 1.
   - **FocalAssault** is `Impossible`, `turn-available`.
   - **So** the authored ManeuverAndFix (≈ 0.38) is the least-penalized candidate and is selected; its Center holds in front of the works with `FixEnemy` while the wing goes around the open end, and the works hold gate keeps the main effort uncommitted until the wing strikes. FEAS-SCN-6 pins it.
7. **An anchored entrenched line (51,000 against 25,000).** Three corps of 17,000 at lateral −300, 0 and +300, depth 0; the scenario 1 conventions otherwise. The enemy holds one segment from −600 to +600 at depth 600, both ends anchored (river columns beyond ±620), in earth parapets: bodies of 6,000 at −450, 5,000 at −150, 5,000 at +150 and 7,000 at +450 on the works, and 2,000 at (0, 680) in a second parapet 80 behind them (a works sample, so a holder). `N` = 27,500; line extent ±450. Within `OddsRadiusMeters` means ≤ 300.
   - **No turn will do:** every line end lies on the works, so every enveloping body is a works assault; FocalAssault is offered.
   - **The focal point:** the junction (−300, 600) between the −450 and −150 bodies scores 11,000 × 1.4 / 600 × 0.8 ≈ 20.5 per unit, the lowest (next, the point (−500, 600) at 21.0).
   - **The assault:** column and support 34,000 against the focal point; the demonstration (17,000) pins 17,000 / 3.0 ≈ 5,700. `L` = 11,000 × 1.1 = 12,100; the enemy's shift from its mass (18, 606) is `τ` ≈ 0.80 h, so within `AssaultHours` 1.0 `shiftShare` = 0.25 and `E` ≈ 14,500: `odds` ≈ 2.34.
   - **An aggressive commander** (agg01 0.9, caution01 0.1, audacity01 0.9: `Bar` 0.68, willingness 0.85): `Bar_j` 2.04, `detail` 0. FocalAssault is `Feasible` and selected over the whole army's frontal assault (`own` 1, as for every works assault: `odds` 51,000 / 27,500 ≈ 1.85, `detail` ≈ 0.36). The batteries in range open on the focal point from the plan's activation; the column marches formed in column and goes in no earlier than 0.25 h after activation; the support follows 0.1 h behind it, and the demonstration pins.
   - **A cautious commander** (agg01 0.2, caution01 0.9, audacity01 0.2: willingness −0.25): FocalAssault is `Impossible`, `declined`. Every other candidate is a works assault with `detail` 1, and whichever wins, `WorksHold` is `hopeless-assault`: the posture guard turns its works-assaulting children's attack tasks to `FixEnemy`, BC11 holds any works-assaulting maneuver, and the phase stays at `Probe`. He holds.
   - **A mid commander:** willing (0.25), but `Bar_j` 3.0 gives FocalAssault `detail` ≈ 0.88. With `EnvelopmentPressure` on, the shipped scores select ManeuverAndFix (base ≈ 0.71, term −0.4: its wings strike the line ends, on the works, `detail` 1); without it, FocalAssault (≈ 0.52 − 0.35). Either way the selected plan's works-assaulting body has `detail` ≥ `RiskyPenalty`, so `WorksHold` is `hopeless-assault`: BC11 keeps the wings or the column in Hold (`HoldForWorks`, no planned release, no ceiling), the posture guard keeps every other works-assaulting child on `FixEnemy`, and the phase stays at `Probe`. The mid commander holds. Both selections are thin under the shipped jitter (seed 7: ManeuverAndFix 0.308 against DoubleEnvelopment 0.300; FocalAssault 0.172 against ContinuousAttrition 0.156), and the outcome does not depend on them: every candidate's works-assaulting body has `detail` ≥ `RiskyPenalty` for this commander, so whichever wins is held.
   - FEAS-SCN-7 pins all three.
8. **An entrenched salient (51,000 against 25,000).** Scenario 7's armies and anchored ends, but the works bulge to an apex at (0, 350): the profile runs at depth 600 except within 250 units of lateral 0, where it rises linearly to the apex. Bodies: 7,000 at (−450, 600), 6,000 at (−200, 600) and (+200, 600) (50 behind the profile there, so on the works), 3,000 at the apex and 3,000 at (+450, 600).
   - **The focal point** is the apex: only its own 3,000 stand within 300 of it (the ±200 bodies are 320 units away), so it scores 3,000 × 1.4 / 600 × 0.5 = 3.5 per unit, half the next.
   - **The assault:** `L` = 3,300; the enemy's mass (−72, 570) is 230 units off, `τ` ≈ 0.59, `shiftShare` ≈ 0.69, `E` ≈ 16,000 and `odds` ≈ 2.12: `detail` 0 for the aggressive commander (`Bar_j` 2.04), selected; 1 for the mid one, held.
   - FEAS-SCN-8 pins it. The Mule Shoe is the weight (§ 3).
9. **An entrenched player (W&L or the human side) with an open flank.** Scenario 6's armies and geometry, but the works are the player's: drawn at deployment as one `ObstacleLine` whose pieces stand every 20 units from lateral −600 to +150 at depth 600, completed, with the player's two left clusters standing in them (their regiments read `CoverObject` 7). Before any probe has perceived the line, the works picture holds only the works samples: the two clusters' samples, 450 units apart, are two one-sample segments. As the probes perceive the pieces (16 per tick, oldest first), the pieces, 20 units apart, chain into one segment over the line's built extent (each piece is outside the AI's zone, so with no works sample within 150 of it the zone test gives it to the enemy), and nothing beyond its last piece counts. The outcome is scenario 6's: the authored ManeuverAndFix turns the open end. No piece the side has not perceived contributes, and the player's units are never ordered (they are the enemy here). FEAS-SCN-9 pins it.
10. **Behind on points as the day runs out.** An Attacker (mid commander) holds no objective; the enemy holds A (70 points), and B (30 points) is neutral; the victory bar is even. So `s_enemy` = 0.7, `s_own` 0, `behind` = 0.7, and `v_A` = 1, `v_B` ≈ 0.43: `Value_A` = 0.80, `Value_B` ≈ 0.46. The fixture gives the feasibility penalties directly to the pure scorer: `r_A` = 0.95 (A is strongly held), `r_B` = 0.
   - **At 10:00** (`DaylightHours` 10, `late` 0, `U` 0), a first choice (no current objective): A scores 0.80 − 0.4 × 0.95 = 0.42, B ≈ 0.46. The main effort goes to B.
   - **At 19:00 with one hour of daylight left** (`DaylightHours` 1.0, `late` 0.75, `U` = 0.7 × 0.75 × 1.0 = 0.525), with B the current objective: A scores 0.80 × 1.525 − 0.4 × 0.7375 × 0.95 ≈ 0.94, B ≈ 0.46 × 1.525 ≈ 0.70. A leads by about 0.24, past `ObjectiveSwitchMargin`: the main effort moves to A. Behind as the light goes, the army takes the risk for the points that decide the day.
   - FEAS-SCN-10 pins both.
11. **Ahead.** The side holds A (65 points) with an enemy within 250 of it; the enemy holds B (35 points); the victory bar is even; the fixture gives `r_A` = `r_B` = 0, and each commander's run is a first choice (no current objective). So `ahead` = 0.3, `Value_A` (own-held) = 0.5625 × 0.80 = 0.45, `v_B` ≈ 0.54 and `Value_B` ≈ 0.52.
   - **A mid commander:** `H` = 0.3 × 0.5 = 0.15. A scores 0.45 + 1.0 × 0.15 = 0.60, B 0.52: the main effort holds A. The holding plays get `HoldBiasWeight × H` = +0.03 in `Select`.
   - **An aggressive commander** (agg01 0.9): `H` = 0.03. A scores 0.48, B 0.52: he attacks B.
   - FEAS-SCN-11 pins both.
12. **A high-value distant objective against a low-value near one.** A holds 100 points 900 units away; B holds 20 points 200 units away; both lightly held, daylight ample; the fixture gives `r_A` = 0.1 and `r_B` = 0. Vanilla's dynamic chain takes B (the nearest). Whiskey scores A 0.80 − 0.04 = 0.76 and B 0.2 + 0.6 × 0.2 = 0.32: the main effort goes to A, and B, enemy-held, draws one `Screen` child. FEAS-SCN-12 pins it.

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
- **requirement-crossing-via** — An enveloping body whose chosen legs cross a river carries each leg's chosen crossing to BC11. There are two vias, one per leg, and each is the leg's first crossing (requirement-route-screen):
  - **The march via** rides on the March order; **the attack via** rides on the Attack order (anchor → line end, or the outflank point for a DeepRaid). `ManeuverOrder` gains `HasVia`, `ViaX`, `ViaZ` for whichever phase it carries, and the doctrine order (`CommandDoctrineOrder.ViaX`, `ViaZ`, additive; NaN none) passes it on. The posture pipeline puts it into `BattleOrderIntent` as `via0` (BC10's `ViaCount` = 1). The executor validates it and issues the two-leg `SetWaypoint` exactly as for a road route (BC10 decision 6). The Attack task keeps its target, the line end; the via only routes the move to it.
  - **No road route:** a maneuver March or Attack with a crossing via gets no road route on top of it.
  - **No `AvoidCoveredCrossings`:** the via already avoids the held crossings. That avoids BC10's "a covered-crossing move takes no via points" rule, which would drop it.
  - **Which leg:** for a DeepRaid, the march via is the first crossing of its route, on the outflank leg when that leg crosses; a crossing on the later anchor leg is priced and watched but left to the pathfinder (`ViaCount` is 1).
  - **A via's far region:** the region the route continues in after the via (a crossing may link up to four regions). VIA-PASSED and the monitor's "crossings passed" use it.
  - **BC11's route check:** it plans the march leg as two half-queries (child → via, via → anchor), one per step on its own one-per-step budget. The leg has a result when both halves have one.
  - **BC11's river check (`FlankMoveCheck`, `river-on-leg`).** With an attack via, `PlayExecutorRuntime` samples the inward leg as two sub-legs, anchor → via and via → line end, at `InwardStepMeters`. A river sample (id 4) within `RiverRegions.CrossingLinkMeters` (60) of the via is the crossing's own footprint (the requirement-leg-cost rule) and does not count. `InwardLegHitsRiver` is true only when a sub-leg samples open river elsewhere, and only then does BC11 abandon with `river-on-leg`. Without an attack via, today's straight check stands. This is § 15 item 4's BC11 change.
  - **Dropped once crossed:** once the child stands in a via's far region (`RiverRegions.RegionAt`), the order carries no via, so an order re-issued each step never walks it back to the crossing.
  - **When a via turns Held:** the assumption monitor fires `crossing-held` for a body not yet in Turn or Attack (requirement-assumption-monitor). After the Turn the wing is committed; the attack via stands.
  - **Lanes:** a via applies to the child node and its lane nodes (`ManeuverNodeTable`), all of which pass the same crossing.
  - **Pinned at the March:** both vias are latched into `ManeuverState` at the March transition (`HasVia`/`ViaX`/`ViaZ`, `HasAttackVia`/`AttackViaX`/`AttackViaZ`) and do not change after it. A mirror flip (requirement-selected-side) drops both.
  - **A main-body member gets no via.** Its strike leg is priced (requirement-held-crossing), not routed: its attack, when the plan's phase orders it, is today's posture order.
- **requirement-wing-abandon-replan** — BC11's abandons reach the army. `PlayExecutorRuntime` queues the side's abandoned enveloping children (child id and reason) in its own per-side queue. The heavy path drains the queue into `MonitorFacts` on its next run, one tick later, because `DriveTickCycle` runs before `DriveDirectChildCycle`. The monitor reads them as `wing-abandoned` (requirement-assumption-monitor). The ledger keeps its one writer, and no new BC11 abandon reason is added.
- **requirement-bc13-commitment** — The opportunity layer and the maneuver layer stay mutually exclusive, as BC13 defined it:
  - A child that is itself a live exploit asset is not offered to the hypothetical layout (requirement-hypothetical-layout).
  - A body in Hold, March or Turn is a BC11 maneuver phase, which BC13's asset eligibility already refuses.
  - **One addition:** a child with an exploiting descendant counts at half its strength in `Pin` and in `S_j`, because that asset is away from its slot for up to `MaxExploitHours`.
  - The evaluation never writes the `ExploitTable` or the `CommitmentLedger`.
- **requirement-bc13-personality** — Every personality input of the evaluation comes from the army commander's entry of BC13's `CommanderPersonalityTable` (`agg01`, `caution01`, `audacity01`; `To01` of the vector). With `Enable Per-Commander Personality` off, it comes from the roster's army vector, as BC13 defines its own off state. Wing commanders' own vectors are not read (D18).
- **requirement-crossing-look** — The ledger publishes up to `MaxLookRequests` = 2 crossing look points per side. These are Unknown link crossings that are, in priority order:
  1. on the selected plan's chosen routes;
  2. on the top confirm candidates' routes;
  3. for a Defender, within the perceived enemy's reach, earliest enemy arrival first.

  BC9's recon producer gains a target kind `Crossing`, after its stale-entry-point and stale-band targets and inside its existing cap of 4 starts per side per doctrine hour. One cavalry regiment rides to the crossing's stand point, 150 units short of it on the own side, observes and returns. BC9 applies that stand-off only to its EntryPoint and RememberedEnemy targets today; the `Crossing` kind adopts it (§ 15). A crossing look never sends a regiment toward a visible enemy at least as strong (BC9's rule). A request expires when the crossing becomes Clear, Watched or Held.
- **requirement-defender-exposure** — For a Defender, each Defensive or Neutral candidate gets a turning-exposure term from the same graph, run from the enemy's side:
  - **The crossings counted:** every link crossing that is:
    - not covered by an own regiment, by the own-side form of `CoveredCrossings.Covers` (the regiment's `RotationY` and `FireRange`). A child bound to guard that crossing (requirement-defender-crossing-release) and its subtree are left out of this test;
    - within the enemy's reach, meaning the earliest enemy body's arrival at the crossing comes before `DaylightHours`;
    - on the Defender's flank. In the front-axis frame of the own units (`TacticalFrontAxis`, lateral and depth): its lateral lies in the outer quarter of the own line or beyond it (lateral < `LateralMin + 0.25 × width` or > `LateralMax − 0.25 × width`, `width = LateralMax − LateralMin`), and its depth lies between one frontage behind the own centroid and the enemy anchor. A crossing in front of the line's middle half is a frontal crossing, which the line already faces; one far behind the line is not on the way to a flank.
  - **Two arrivals per crossing** (both on the threat list, requirement-runtime-wiring), from the enemy bodies of requirement-enemy-picture:
    - `enemyAtCrossing`: the earliest arrival at the crossing over the enemy bodies that stand in another region than the own bank's, each by the shift-time model (requirement-shift-time) from the body's centroid, its courier ride priced from there too;
    - `enemyAtFlank`: `enemyAtCrossing` plus the crossing's legs to the flank end of the candidate's projected line. The flank end is the filled, non-Reserve rect anchor with the outermost lateral on the crossing's side; a tie goes to the smaller depth (the line, not a forward screen).
  - **`ownShift`:** the fastest of the candidate's reserve sources to reach the crossing, each by requirement-courier-time and requirement-march-time over the path search (requirement-link-legs):
    - each BC10 deployed reserve (`DirectChildEvidence.DeployedReserve`), from its current position with its own `base` and fatigue. The layout never claims it (requirement-hypothetical-layout), so it is a reserve under every candidate;
    - each child the candidate assigns to a Reserve or ReserveLine rect, from that rect's anchor, infantry base 1, unfatigued.

    With no reserve source, from the nearest filled rect anchor to the crossing: a line child would have to leave its place.
  - `turn = clamp01((ownShift − enemyAtFlank) / LateHours)`, the worst crossing taken.
  - **At most `MaxThreatCrossings` = 8 crossings** are counted per evaluation, the earliest `enemyAtCrossing` first; the rest wait for a later evaluation.
  - It enters `penalty` as `(1 − 0.6 × turn)`, in place of the attacker's `contested` term (requirement-penalty).
  - A Defender's verdict is never `Impossible` from this term.
- **requirement-defender-crossing-release** (D17) — A Defender's allocator gains a reserve release with reason `crossing-threat`. It reads the threat list the ledger writes for the **selected** candidate only (requirement-runtime-wiring), and it chooses among the side's real Reserve children, never among template rects.
  - **Its place in the shipped order** (`DirectChildAllocator.ReleaseReserves`): crisis and stagger (`StaggeredRelease`, or today's per-child loop with the staggered flag off), then BC11's flank release (`ReleaseReserveToThreatenedWing`), **then the crossing release** (`ReleaseReserveToCrossings`), appended last. A crisis is contact now and goes first (defence first); BC11's flank release answers a wing already in contact; a crossing threat is a predicted one, with the enemy not yet across, so it takes what the two contact releases leave. Appending it changes none of the shipped releases, so the BC11 and BC13 allocator tests stay green. The stagger runs first and may take a reserve in an Exploit phase or with the odds reached; a Defender at that point is committing its reserves anyway, and the floor below still holds.
  - **Who may go.** A child whose role is `Reserve` when the crossing release runs: a BC10 deployed reserve, a child on a Reserve or ReserveLine rect, or one the allocator left in reserve. Never one that is a live BC13 exploit asset (`ReserveReleaseContext.Exploiting`), and never one with player-owned strength in its subtree (`ReserveReleaseContext.PlayerOwned`, from BC3's ownership): the W&L player's command is not released.
  - **Travel time is BC14's own.** The ledger computes `ownShift_c` on the heavy path for every direct child with no player-owned strength and every counted crossing: courier hours from the army HQ (requirement-courier-time) plus march hours from the child's current position with its own `base` and fatigue (requirement-march-time), over the path search (requirement-link-legs). These are the functions `T` uses. It writes them into the threat list (`OwnShiftByChild`); the allocator only compares, with no grid and no snapshot.
  - **The trigger,** per counted crossing: among the eligible Reserve children, the one with the least `ownShift_c` (ties to the lower index) is chosen, and it is released when `enemyAtCrossing` ≤ `ownShift_c + ThreatLeadHours` (0.25). With no eligible Reserve child, no release (for example PreparedDefense with three corps and no deployed reserve: every child holds a line rect).
  - **An enemy already across:** per crossing, take the earliest arrival over **all** enemy bodies, each counted from its own region. When that earliest body already stands in the own bank's region, the crossing is not a crossing threat: it is contact, and BC13's crisis release answers it (a sector in contact and outmatched draws the nearest reserve). No new crossing release is made toward that crossing; a guard already bound there stays bound (its arrival test below holds at 0).
  - **The release.** The child gets role `RefuseLeft` or `RefuseRight` by the crossing's lateral side (task `GuardFlank` for a Defender node, as BC11's flank release), the crossing's lateral band as its sector override, and a guard point (`DirectChildIntent.HasGuardPoint`, `GuardX`, `GuardZ`, `GuardHeading`):
    - **where:** `GuardStandOffMeters` = 100 behind the crossing along the own front axis (the crossing point minus 100 × forward). When that point is not land of the crossing's own-bank region (its linked region that holds the own centroid, or else the one whose nearest land cell is nearest to it), it snaps to that region's nearest land cell, by TARGET-SNAP's rule;
    - **facing:** toward the crossing point;
    - the posture producer moves the child there and holds. The intent carries the reason, so the `TacticalReserveRelease` row reads `reason=crossing-threat`, not BC11's `flank-threat`. It does not count against the Refuse caps, as BC11's flank release does not.
  - **One per crossing,** crossings taken in order of the earliest `enemyAtCrossing`.
  - **The floor.** A crossing release never leaves the held reserves (Reserve role, not exploiting) below `FloorOf(R0x, floorShare)`, BC13's floor with the army commander's caution and casualty tolerance, where `R0x` = the Reserve children before the crossing release plus the children already bound to a crossing. `R0x` is recomputed each allocation and stays stable while the bindings hold; it touches neither BC13's `InitialReserves` nor `LastReleaseHours`, so the stagger's R0 and interval are untouched. With `R0x` = 1 the single reserve may go (BC13's rule). The floor applies with BC13's staggered flag on or off.
  - **The binding.** `ReserveReleaseState` gains a crossing binding per released child, `CrossingBinding { ChildId, Link, OwnShiftAtRelease, ReleasedAtHours }`, in a pooled table of `MaxCrossingGuards` = 8, separate from the stagger's map (BC13's `ClearTrigger` does not touch it). While bound, the child keeps its role and guard point as long as the threat holds: `enemyAtCrossing` ≤ `OwnShiftAtRelease + ThreatLeadHours + ThreatHoldMarginHours` (0.25). The guard's own cover does not end the threat, because the guard is left out of the cover test (requirement-defender-exposure). The binding ends when the threat stops (the arrival recedes past that bound, or no enemy body is perceived), when the child is gone, becomes an exploit asset or holds player-owned strength, and on a plan change, a new battle day and a stand-down (§ 9). The child then reverts to Reserve on the next allocation.
  - **What the evaluation knows of the guards:** `FeasibilityInput.BoundGuardChildIds` lists the children bound to a crossing at the previous allocation (from `ReserveReleaseState.ForAlliance(a)`), so the exposure's cover test can leave them out.
  - **The context's personality:** whenever BC14's flag is on, `ArmyOrchestrator` builds the release context with the army commander's agg01, caution01 and casualty tolerance (BC13's full constructor), so the crossing floor has its share with BC13's staggered flag off too.
  - **The state's scope.** `ReserveReleaseState` becomes one instance per alliance (`ReserveReleaseState.ForAlliance(int)`, the `ExploitTable.ForAlliance` precedent), registered with `BattleScope` from its static constructor, and `ArmyOrchestrator` uses its alliance's instance. `ReplanForNewBattleDay` clears it before it replans, whether or not a plan is picked; `Replan` and `BattleCoreTick.StandDown` clear it as today. The release context carries the state, the exploit and player-owned flags and the threats whenever BC13's staggered flag or BC14's flag is on; `ReserveReleaseContext.Staggered` becomes its own flag instead of `State != null` (§ 15 item 6).

- **requirement-runtime-wiring** — Runtime details the plan must follow:
  - **Files.** Pure:
    - `Tactical/Battle/RiverRegions.cs` (the regions and the link table), `CrossingWatch.cs`, `EnemySamples.cs` (the extracted sample filter) and `SingleLinkClusters.cs` (the extracted labelling);
    - `Tactical/Orchestrator/Plays/ManeuverFeasibility.cs` (the evaluation, the monitor and the types of § 8), `FeasibilityLedger.cs` and `LegCostCache.cs` (requirement-link-legs).

    These pure files, with the two runtime files below, are **the BC14 file set** the source pins read (MARCH-RULES-PIN, FEAS-PIN).

    Runtime:
    - `Tactical/Battle/RiverRegionsRuntime.cs` (stepped from `TerrainPictureRuntime.Step`);
    - `Tactical/Orchestrator/Plays/FeasibilityRuntime.cs` (`MarchRules`, `DaylightHours`, the confirm queue's NavMesh call, the per-side ledgers, the crossing probes).

    The via's path: `ManeuverNodeEntry` → `CommandDoctrineAssignment` (doctrine order) → `CommandPostureExecutor` / `PostureIntentProducer` (`via0`, no road route, no `AvoidCoveredCrossings`). The crossing look: `ReconIntentProducer` and `BattleRecon`.
  - **Order within the coordinator tick** (`DriveTacticalCommanderSide`):
    1. the pick inputs: `ArmyPostureInputsRuntime.Refresh` is split. `RefreshPickInputs` (stance, live initiative, reinforcement opportunity) runs first, here and in `AttachArmyIfActive` before `PickInitialPlan`. `RefreshPostureCaches` (AI pool, leaf map, which read this tick's intents) stays where `Refresh` runs today;
    2. the crossing probes and classification;
    3. the evaluation: the next slice of a running evaluation, or a new one when the last was published and `now − EvaluatedHours ≥ EvaluateIntervalHours` (a cheap compare every tick; forced to completion before the initial pick and the night replan);
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
  - **Crossing threats:** the Defender evaluation writes a pooled threat list for the selected candidate only, one entry per counted crossing: link index, crossing point, lateral side, own-bank region, the guard point and heading, `enemyAtCrossing` and `enemyAtFlank`, the candidate's `ownShift`, and `OwnShiftByChild` (one value per direct child, NaN for a child with player-owned strength). `ArmyOrchestrator` reads it through the pushed lookup when it builds `ReserveReleaseContext.CrossingThreats`. The allocator compares and writes the guard point on the `DirectChildIntent`; the posture producer reads `HasGuardPoint` as the `GuardFlank` target and heading in place of the sector's flank point.
---

## 7A. (F) Enemy works: turn them first, assault a focal point only when no turn will do

The same flag, evaluation and selection serve entrenched enemies (integrator scope addition, 2026-10-16, from the user's question). The user's intent: go around the works when that is feasible, leaving a division or brigade in front so the enemy cannot shift; when going around is not feasible, charge a focal point of the works. Vanilla routes its marches around the field of fire in front of covered enemies and weights covered enemies in its hotspot map, and that is all (§ 2.1 item 7).

- **requirement-works-picture** — The enemy's works as the side perceives them, built once per evaluation from two fog-honest sources:
  - **Works samples:** enemy samples (`EnemySamples.Accepts`) whose fields last seen while Visible (BC10's `EnemyObservation.CoverValue`, `CoverObject`, `Standing`) show `Standing`, `CoverValue` > 0.15 (vanilla's own threshold at 7962, `CoveredCrossings.MinCoverValue`) and `CoverObject` in `WorksCoverIds` = {2 stone wall, 4 embankment, 7 earth parapet, 8 reinforced parapet, 9 parapet and abatis, 10 parapet and obstacles, 11 breastworks, 15 sunken road, 16 unfinished railroad} (`GetCoverIDFromName`, the `battleprefs` cover list). Forest (3), creek (5), road (6), fence (1) and buildings (12-14) are not works: BC12's `TerrainStrength` already prices woods and towns.
  - **Works points:** the model pieces of every `ObstacleLine` in the battle, whatever made it (§ 2.1 item 7): the AI's deployment works (`Fortification.FortificationGroups.Create` adds an `ObstacleLine` to the group's object, 24364-24388), the works the player draws at deployment (an `ObstacleLine` added per drawing, 169201) and works loaded from a save (107103). The runtime enumerates every `ObstacleLine` (`FindObjectsOfType<ObstacleLine>`) at battle start and again when deployment ends (the AI's lines live only on its groups' objects), and at each evaluation re-reads `BattleUI.obstaclelines` (public, 163943; the player's lines, drawn at any time, and the loaded ones, which `LoadObstacles` puts there, 73337-73345) together with the lines already known, into a pooled array of at most `MaxWorksPoints` = 2,048 pieces (beyond it, the pieces farthest from the own centroid are dropped and counted on `TacticalEnemyWorks`). It reads each line's public `modelobjects`: a piece counts as a works point at its `modelobj` position when the object exists and its `completion` is at least `WorksMinCompletion` = 0.5. `Create` stops adding pieces at the group's `lengthneeded`, and `FinishImmediately` destroys the pieces it could not afford when the line is a trench or the caller asks it to (the AI's `Create` does, `removeallmodels`); a player's other works keep their unaffordable pieces at `completion` 0, below `WorksMinCompletion`. So no works point stands past what was built (control: decompile `Create` 24364-24388 and `FinishImmediately` 106906-106929, reads 2 sites, under: vanilla source; failure-under: vanilla source). `BattlefieldSetup.fortifications`' own `fortificationpoints` are placement candidates and are never read.
  - **Seen only:** a works point counts once the side has perceived it (`IPerceivedGround.IsPerceived`), and stays known for the battle: works do not move. The probe stamp is keyed by the piece's `modelobj` instance id, so it survives a re-read. A piece whose object is gone, or whose `completion` fell below `WorksMinCompletion`, is dropped at the next re-read.
  - **Whose works:** a perceived works point counts for the enemy when a perceived enemy works sample stands within `WorksGapMeters` of it, or, with no perceived enemy works sample within `WorksGapMeters` of it, when it lies outside the side's own deployment zone (`IFortificationPoints.InOwnZone`, computed by the runtime at enumeration with vanilla's `Frontline2.CheckIfWithinZone`, 102922). For the Defender's own works (requirement-defender-works-reserve), a works point within `WorksGapMeters` of an own regiment in works cover, or inside the own zone. Vanilla's `Fortification.occupiedby` is not read (it is cleared again after deployment, so it says nothing reliable). Probes: at most `MaxWorksProbesPerTick` = 16 per side, oldest first, in the same handle, scope and slice as the crossing probes.
  - **No hidden unit is read.** An enemy regiment that is not a sample contributes nothing; a works point not yet perceived contributes nothing.
- **requirement-works-shape** — The perceived works become segments with ends and focal points, in the enemy's front frame (`TacticalFrontAxis`, lateral and depth):
  - **Segments:** works samples and works points sorted by lateral; neighbours within `WorksGapMeters` = 150 laterally join one segment. A segment carries its lateral extent, its **depth profile** and its **holders**. The profile is defined per `FocalSampleMeters` (50-unit) lateral bin as the works point or sample in that bin nearest the own side; a point up to 100 units behind it in the same bin (a second line) is on the segment but does not move the profile. The holders are the works samples in it, with their strength (Visible full, Remembered × confidence), morale and cover. A segment with no holder is empty works: it shapes the picture but is not **held**.
  - **On a segment:** a point lies on a segment when its lateral is within the segment's extent ± `WorksEndMeters` (50) and its depth within 100 (≤) of the profile there. Every "within" distance of this section is inclusive (≤), the `OpeningDetector.ClusterPass` precedent.
  - **Ends:** each end of a held segment is **anchored** when `TacticalTerrainLinePlanner.FlankAnchored` reports it anchored outward along the front axis, or another held segment begins within `WorksGapMeters` beyond it; otherwise it is **open**.
  - **Salients:** a profile point that stands at least `SalientDepthMeters` = 150 nearer the own side than the chord between the profile points 300 units either side of it is a salient apex.
  - **Junctions:** the midpoint between two enemy bodies that are adjacent in lateral order among the bodies whose centroids lie on held works (requirement-enemy-picture's single-link clusters, built in the Attacker run too when works exist). It stands for the seam between two commands: Whiskey sees regiments, not their chain, so two separate bodies are the honest stand-in.
  - **Focal points:** every profile point of a held segment every `FocalSampleMeters` = 50, every salient apex and every junction. Each scores `Σ holders within OddsRadiusMeters (strength × morale × (1 + cover)) / (works frontage within ±300 laterally) × shape`, with `shape` = `SalientFactor` 0.5 at an apex (the Mule Shoe: the defenders of a salient are hit from three sides and their supports must cross its base), `JunctionFactor` 0.8 at a junction, 1 elsewhere. The lowest score is the chosen focal point; a tie goes to the point nearer the own centroid, then the lower lateral.
- **requirement-works-pricing** — Assaulting held works takes about three times the odds:
  - **Who assaults works:** an enveloping body whose strike point lies on a held segment; the main body of a single-body plan whose strike point (the enemy anchor) lies on one; the assault body of a focal assault (requirement-focal-assault).
  - **Its bar:** `Bar_j = WorksOddsFactor × Bar`, `WorksOddsFactor` = 3.0: the rule of thumb of about three to one at the point of attack (§ 3). Its detail term (requirement-detail-risk) runs against that bar.
  - **Its window:** every body that assaults works, in any plan, takes `W_j = (latest S* over the other bodies − S*_j)⁺ + AssaultHours` (`AssaultHours` = 1.0) in place of step 2's support time: the time a break must be made in before the defender's shift closes it, its own supports being inside its body. `Pin` still counts the bodies that have struck by `S*_j`.
  - **A single-body plan** that assaults works gets the detail term too, with no other body: `W_j` = 1.0 and `Pin` 0.
  - **The defence ratio:** a body whose strike point lies on held works pins enemy at `DefenseRatio_k ≥ WorksDefenseRatio` = 3.0.
  - **A body whose strike lands beyond an open end, or on a flank or the rear of the works** (its strike point on no held segment), is priced exactly as on open ground. That is the turn's advantage, and it emerges: nothing scores the turn as such.
  - The reason literal is `works` when a works-assaulting body's detail is the largest term.
- **requirement-turn-first** — In a plan with an enveloping body or a focal assault, a fixing member whose strike point lies on held works **holds, it does not assault**:
  - **It is not a works assault:** its bar stays `Bar`, and it pays no works term.
  - **Its exposure:** an enemy holding works gives up their value to come out, so the enemy against such a fixing body counts only the perceived enemy **outside** works: `E_j = L_j^open + shiftShare_j × max(0, N_open − Pin_j − L_j^open)`, with `N_open = N × P_open / P`, and `τ_j` measured from the mass of the samples outside works (with none, `E_j` is zero). It still pins at the works defence ratio, which is why a turn against works is harder than on open ground.
  - **Its task:** the zone assignment gives it the Fix role's `FixEnemy` at its anchor, as today; BC14 creates no `AttackObjective` for it, and the phase gate below holds the plan's assault until the turn has struck.
  - This is the user's "a brigade or division in front, the main body around the edges"; Sherman against Johnston in 1864 is the weight (§ 3).
- **requirement-works-hold** — One predicate, three readers. `FeasibilityLedger.WorksHold(out reason)` is computed from the selected plan's entry each heavy tick (false with the flag off). It is true while:
  - `hopeless-assault`: any of the selected plan's works-assaulting bodies (a single-body plan's main body, an enveloping body whose strike point lies on held works, a focal assault's assault body) has a detail term of at least `RiskyPenalty`; or
  - `turn-not-struck`: the plan has an enveloping body that faces no held works and has not yet reached BC11's Attack phase, and its fixing members face held works.

  **Which children assault works.** The ledger publishes, per direct child of the selected plan, `AssaultsWorks(childId)`: true for the members of a works-assaulting body (requirement-works-pricing's three cases), carried on `BodyEstimate.AssaultsWorks`.

  **The posture guard reads it (the mechanism that stops the assault).** No shipped task reads the phase: a `Main` child's leaf task is `AttackObjective` (`TacticalRoleCascade.RoleToLeafTask`) and the operations director commits on its own odds rule. So a pure `PostureWorksGuard`, modelled on the shipped `PostureStanceGuard` and applied at the same two places (the leaf cascade's task and the node path's `Apply` on the state and its doctrine order), rewrites `AttackObjective` and `SupportAttack` to `FixEnemy` (reason `works-hold`, the same target) for a node whose top-level child `AssaultsWorks` while `WorksHold` is true. A node named in the side's `ManeuverNodeTable` is exempt (BC11 holds it, below), and so is a BC13 exploit asset or a node following one (`ExploitOverrideKind` other than `None`: an exploit goes through a breach already made). `Probe` and every holding task pass unchanged.

  **BC11 reads it.** While the reason is `hopeless-assault`, `PlayExecutorRuntime.StepManeuvers` sets a new `ManeuverInput.HoldForWorks` on every maneuver spec of a child that `AssaultsWorks` and on every start-delay spec of the same plan (the focal assault's `AssaultSupport`), and publishes no planned release for them. `EnvelopmentManeuver.Step` keeps such a body in Hold, and `StepStartDelay` keeps such a start delay unreleased, while the flag is set; for a maneuver it suppresses the Hold ceiling and clears the latched `HasFirstPlannedRelease` and `HasBudget`. When the flag clears, `ManeuverState.HasWorksClear`/`WorksClearHours` latches that hour, the ceiling becomes `max(PlayActivatedHours, FirstPlannedReleaseHours, WorksClearHours) + MaxManeuverHours`, and the planned release and budget latch again from the next published release. `turn-not-struck` does not set it: the turning wing must march. So a hopeless works assault never launches, whichever plan carries it.

  **The phase machine reads it** through `TacticalPhaseProgressionDoctrine.Input.WorksHold`:

  While `WorksHold` is true, every transition **into** `MainEffort` is blocked: from `Probe` the recommendation stays `Probe`, past the probe budget too, and from `Consolidate` it stays `Consolidate`. It never forces a step back out of `MainEffort` or `Exploit`. The phase keeps its rows honest (the plan reads `Probe` while it probes); the posture guard and BC11 are what keep the assault from going in. The army closes, fixes and probes, and does not commit its main effort against the works. When it turns false, the shipped rules apply.
- **requirement-focal-assault** — A new offensive playbook, `FocalAssault` (`BattlePlanId.FocalAssault`, appended last to the catalog), offered only when no turn will do:
  - **Its stance class is Offensive** (`TacticalPlaybookCatalog.StanceOf`), so `StanceAllowsPlaybook` never offers it to a Defender, whatever works its ledger sees; a Meeting side may take it, like any offensive play. Its other catalog readers get explicit values (§ 15 item 7's reader table): `DelayingAffinity` 0, `EnvelopmentAffinity` 0.10 (a frontal assault, not a turn), `ResponseTargetFor` 0.6 (offensive), `AllowsProvisionalAttackAxis` true (the frontal plays' rule).
  - **Precondition (before the jitter draw):** `PlaybookContext.Feasibility` is set and the ledger has a focal point. With the flag off, or with no held works, it is never offered and the jitter stream is unchanged.
  - **Feasibility (after the jitter draw, as every candidate's):** `Impossible`, reason `turn-available`, when any other enveloping candidate has a verdict other than `Impossible`, no `after-dark` body, and every enveloping body's strike point off held works. That is, a turn is feasible: both ends anchored, a river, no route or no daylight are what leave it unavailable. `Impossible`, reason `declined`, when the army commander's willingness `0.5 × agg01 + 0.5 × audacity01 − 0.5 × caution01` is below `FocalWillingness` = 0.2: a cautious commander declines, and the works hold (requirement-works-hold) keeps his army probing. Otherwise its penalty is the model's, so a hopeless assault scores low and, if it still wins, is held by the gate.
  - **Personality:** `PersonalityFit(aggression: 0.6, caution: −0.6, audacity: 0.5)`, `TerrainPreference(open: 0.6, wooded: 0.5, river: 0.4, mountain: 0.4)`, `OddsRange(min: 1.2, max: 4.0)`, reserve commit trigger 1.2: Grant at Spotsylvania and Upton, not Burnside's schedule.
  - **Its template** (`PlayTemplateCatalog`, projected toward the focal point instead of the enemy anchor, a plan branch at the executor's enemy-anchor read, `PlayExecutorRuntime`'s `PlayAnchor.EnemyAnchor` call in `ComputeZoneAssignmentsCore`; centred, so never mirrored, and BC11 gives an `AssaultColumn` no mirrored try: `canMirror` is false for it), as `PlayRect(kind, lateral, forward, halfWidth, halfDepth, offsetSeconds, priority, envelops)`: `AssaultColumn` (0, +0.6, 0.3, 0.4, 0, 10, envelops: true), `AssaultSupport` (0, +0.45, 0.3, 0.3, 0, 9), `Demonstration` (−0.7, +0.5, 0.4, 0.4, 0, 7) and (+0.7, +0.5, 0.4, 0.4, 0, 7), `Reserve` (0, −0.3, 0.3, 0.3, 0, 5). The extents follow the shipped templates' (PlayTemplateCatalog's DoubleEnvelopment and frontal entries); the column's is narrow and deep; no rect has a start offset, the timing being the releases'. The column and the support form one **assault body** striking the focal point; the demonstrations are the main body and pin.
  - **Its bodies** are priced like any other: the assault body by requirement-works-pricing, the demonstrations by requirement-turn-first.
- **requirement-focal-execution** — The assault runs through BC11's maneuver path and the executor; its one new write is the bombard start:
  - **The column:** the `AssaultColumn` child is a BC11 maneuver (`ManeuverSpec` kind `AssaultColumn`), timed and watched like an enveloping body (synchronized release, time budget, vias, the assumption monitor): Hold until its synchronized release (requirement-synchronized-release), March to its rect anchor (the form-up point, outside the works' fire), Turn, then Attack on the focal point. Its March, Turn **and** Attack orders carry `Formation` = March Column (group formation 4, `RoadRoutePlanner.ColumnGroupFormation`, the formation BC10's road route issues; BC16 records it as `assault-column`), so the formation is asked for from the March on and lands whenever `CommandFormationWritePolicy` admits a write (it refuses a moving group: before the move starts or at a halt), at the latest during the Turn's wait below. The formation travels with the order: `ManeuverOrder.HasFormation`/`Formation` → `ManeuverNodeEntry.Formation` → `CommandDoctrineOrder.Formation` (an `int`, −1 none, set by the constructors' default argument; `ManeuverOrder` is built by object initializers, so it carries the `HasFormation` flag). The posture pipeline's formation for a node whose doctrine order carries a formation is that formation: `CommandFormationCorrection.TargetFormationForTask` gains the order's formation as an argument and returns it when it is set, in place of the line it returns today for `AdvanceToAssembly` and `AttackObjective`. The executor issues it on the Formation channel (`BattleUnits.SetFormation`, under `CommandFormationWritePolicy`), and the Attack moves by waypoint. **The column waits to form:** for this kind the Turn ends, besides at first contact, only when the group reads formation 4 or `RoadRoutePlanner.ColumnWaitHours` (0.1) has passed since the Turn began (BC10's `DecideColumn` precedent; `ManeuverInput.GroupFormation` carries the group's formation from the snapshot), and not before the preparation floor below. BC11's `anchored-high-ground` check is skipped for this kind: the works are the target and the model has priced them. **Its inward leg is the focal point's:** `PlayExecutorRuntime.BuildInput` takes the focal point as the inward target (and as `EnemyEndX/Z` for the row) when `HasFocal` is set, so BC11's corridor check, its river check and INWARD-VIA-BC11's sub-legs judge the leg the evaluation priced (anchor → focal point).
  - **The specs:** `DirectChildAllocator.ManeuverOf` gains two branches: `AssaultColumn` → a full maneuver spec of kind `AssaultColumn` (it is enveloping); `AssaultSupport` → a start-delay spec with `HoldsForRelease`, whose planned release is the column's strike plus `EchelonDelayHours`.
  - **The support:** the `AssaultSupport` child holds through the start-delay path (`HoldsForRelease`) until the column's strike plus `EchelonDelayHours` = 0.1, then is released to `SupportMain` behind the column, to exploit a break: Upton's breach failed for want of it (§ 3).
  - **The demonstrations** hold their anchors (`Screen`), released with the column so the pin lands with the assault; while the column holds under `HoldForWorks` they keep holding.
  - **The focal point reaches BC11** as `ManeuverInput.HasFocal`/`FocalX`/`FocalZ` and is latched into `ManeuverState.HasFocal`/`FocalX`/`FocalZ` at the column's March, with its vias; until then each evaluation's focal point applies. While FocalAssault is the selected plan, the ledger keeps publishing its last focal point when an evaluation finds none (the works abandoned: the column then strikes an empty point, an outcome the assault wanted). `EnvelopmentManeuver.LineEnd` returns the focal point when one is set, so the Turn heading and the Attack target are the focal point. A plan change drops it.
  - **Artillery preparation is a new executor write path.** Whiskey has never started a bombardment: its artillery doctrine acts only on one already running, and its battery writes are cancel (7) and counter-battery (8). Vanilla's start (`CheckAIBombardment`, the writes at 3897-3900) is inline, not a callable method: it calls `BattleUnits.ChangeCombatBehavior(go, 9)` and then writes `bombardposition` and `bombardrange` = `GamePrefs.bombardmentmaxrangesize`; its target filter then drops targets outside that radius (118405, 118433). So:
    - from the plan's activation until the column's Attack, the doctrine order of the focal assault's children carries `PreparationX/Z` = the focal point (NaN none), except while `HoldForWorks` holds the column (no preparation for an assault that is not going in);
    - the artillery doctrine producer (`ArtilleryDoctrine`) submits, for each own battery within its fire range of the point that vanilla's own per-battery start test would admit (the battery `if` at 3890 and the start `if` at 3894: artillery, not routed or marked for rout, guns > 0, not permanently detached, not already on counter-battery 8 or bombardment 9, mean ammo `Σ ammo / 3` above 0.2 as vanilla computes it, no path; the point within fire range stands in for vanilla's closest enemy within it), a new intent kind `BombardStart` on the CombatBehavior channel carrying the point. It does so only with `Enable Tactical Artillery Doctrine` on as well (the producer returns early without it);
    - a battery whose group order carries a preparation point is exempt from the doctrine's cancel (7) and counter-battery (8) rules until the column's Attack, except the safety ones it keeps (ammo below 0.10, an unsupported enemy within 0.2 × fire range, friendly danger close), so the doctrine does not stop the bombardment it started;
    - the executor executes it as vanilla does, and only so: `TacticalCombatBehaviorWriter`'s `ChangeCombatBehavior(go, 9)`, then `bombardposition` = the point and `bombardrange` = `GamePrefs.bombardmentmaxrangesize` on the same regiment. Vanilla also stamps the group's `lastfeudactiontime` there, the W&L feud action timer, whose only reader is the feud action button of a group with `ai_feudstance` ≥ 0 (121676); Whiskey does not write it, since it never acts for the feud instance (`IsFeudInstance`). The two field writes are new; they sit in one runtime `OrderExecutorRuntime.StartBombard(Regiment unit, float x, float z)` beside the shipped `bombardstarttime` reset (`IssueOther`), gated by `TacticalWriteGuard.CanWrite` and Commander Mode Active (the harness compiles `BattleWriterPolicy.cs` but not `TacticalCombatBehaviorWriter`, so only the pure `TryBombardStartIntent` lives there); `BattleWriteGateTests`' pattern gains `\.bombardposition\s*=` and `\.bombardrange\s*=`, the BC7 writer grep list gains them, and they are no Prefix and no Transpiler (§ 15 item 8, D34);
    - **the preparation floor:** when the side has an own artillery battery that is not player-owned, the column's planned release, and its Turn → Attack, is no earlier than the plan's activation plus `PreparationHours` = 0.25 (a pure floor: no executor outcome feeds back); with no battery there is no floor. Vanilla's own stop rule (no enemy in range, ammo, a path) and the doctrine's safety rules end the bombardment; the order's preparation point is cleared at the column's Attack, so no new start follows.
  - **Never the player's column:** one rule. A direct child that contains a player-owned unit is never assigned the `AssaultColumn` or the `AssaultSupport` rect: `CorpsPosition` gains `ContainsPlayerOwned`, which `PlayAnchor.CollectOwn` fills from the snapshot's `PlayerOwned` flags over the child's subtree (`BattleSnapshot.DirectChildren`, the D5 tree) and the evaluation fills from its per-child player-owned strength (requirement-player-share), and `PlayCorpsAssignment.Assign`/`AssignInto` skip such a child for those two rect kinds (§ 15). It may hold a `Demonstration` or `Reserve` rect, where requirement-player-share's rule applies: its player-owned strength is not counted, and the executor's chain fan-out keeps every write off the player's units. The player decides nothing in the assault.
- **requirement-defender-works-reserve** — A Defender whose own line holds works keeps a reserve behind its most threatened focal point:
  - **Its own works:** the same shape functions over its own regiments (their `CoverValue` and `CoverObject`, standing; own data, no fog) and the works pieces it owns (`InOwnZone`, or within `WorksGapMeters` of an own regiment in works cover).
  - **The most threatened focal point:** among its own focal points (requirement-works-shape), the one with the earliest enemy-body arrival (the shift-time model, as `enemyAtCrossing`), within `FocalThreatHours` = 1.0; a tie goes to the larger perceived enemy strength within `OddsRadiusMeters` of it.
  - **The station:** the eligible Reserve child (as requirement-defender-crossing-release: not exploiting, no player-owned strength) with the least shift time to the station is given a guard point `ReserveStationMeters` = 150 behind that focal point along the own front axis, snapped to own land, facing it, with role `Reserve` and task `ReserveWait`. The posture producer moves a Reserve child that carries a guard point to it and faces it. It is a station, not a release: the floor does not apply, and the role stays `Reserve` for every later release.
  - **Its memory:** `ReserveReleaseState.ForAlliance(a)` gains `StationBinding { ChildId, FocalX, FocalZ, ArrivalAtChoice }`, cleared by `Clear()` (plan change, new day, stand-down, `BattleScope`) and not by `ClearTrigger()`; the hysteresis compares against `ArrivalAtChoice`.
  - **Hysteresis:** the station moves to a new focal point only when that point's arrival is earlier by `StationSwitchHours` = 0.25.
  - **Order:** after the crossing release, among the children still `Reserve`. One child is stationed per side.
  - **The ledger computes it:** the own focal points, their arrivals and, for the most threatened one, the station point and a per-child shift-time vector to it (`TryStation`), by the functions `T` uses. The allocator compares, as for the crossing release.

---

## 7B. (G) Objectives: their value, the victory clock, one main effort, and the historical chains

The same flag and selection machinery decide **where** the army strikes (integrator scope addition, 2026-10-16, from the user's question on strategy and objectives). Vanilla's dynamic chains take the nearest objective not held, by distance only; its points feed only the victory bar and the deployment sort (§ 2.1 item 9). Whiskey's director scores objectives with fixed values (§ 2.2). Nothing reads the points, the bar or the time left.

- **requirement-objective-value** — An objective's value comes from vanilla's own points:
  - **Which objectives:** the active map objectives (`MapObjectiveCapture`, type `VictoryPoint`), identified by the director's own string `ObjectiveId` (`MapObjectiveId`, which the capture already maps from the vanilla reference); the director's other entries (the enemy line, unknown vanilla objectives, recon contacts) carry no points and keep their shipped value and score.
  - **Ownership:** own-held when `Objectives.owner` is the side's alliance (the capture's `Owner == allianceId`, as shipped); enemy-held when `owner` ≥ 0 and another alliance; neutral when `owner` < 0.
  - **The points:** `Objectives.alliancepoints[own alliance]` when vanilla's victory points read per-alliance values (`UpdatePoints`, not `continuousobjectivepoints`, 84101-84108), otherwise `Objectives.points` (105222). Both are public, shown by the victory bar, and the same for both sides to see: no fog applies.
  - **Normalised:** `v_i = points_i / max over the map's live objectives (active) of points`; with every live objective at 0 points, `v_i` = 0.5.
  - **The value** replaces the fixed 0.45 / 0.80 (`TacticalVisionRuntimeAdapter`): `Value_i = 0.2 + 0.6 × v_i` for an objective the side does not hold (the highest-valued one keeps today's 0.80), and `0.5625 × (0.2 + 0.6 × v_i)` for one it holds (today's 0.45 / 0.80 ratio).
  - **The terrain sign stays:** `TacticalOperationDirector.ObjectiveSelectionScore` keeps its BC12 terms (an enemy-held objective is harder on strong ground; an own or neutral one is worth more on it).
- **requirement-victory-pressure** — The victory clock, read from public state each evaluation:
  - **Inputs:** both sides' share of objective points held (`BattleUnits.sideinformation[side].objectivepointsheld`, the normalised share `UpdatePoints` computes, 84118-84128), the victory bar (`BattleUnits.GetVictoryPointsRelation(side)`, 0.5 even), and the hours left before the day ends (`DaylightHours`, requirement-daylight).
  - **Behind and ahead:** `behind = max(clamp01(s_enemy − s_own), 2 × max(0, 0.5 − bar))`; `ahead = max(clamp01(s_own − s_enemy), 2 × max(0, bar − 0.5))`.
  - **Lateness:** `late = clamp01(1 − DaylightHours / LateDayHours)`, `LateDayHours` = 4.0.
  - **Urgency:** `U = clamp01(behind × late × (0.5 + agg01))`: a side behind with the day running out, more so under an aggressive commander.
  - **Hold:** `H = ahead × (1 − agg01)`: a side ahead holds what it has unless its commander is aggressive.
  - **Their effects:**
    - the objective score (requirement-main-effort-objective) multiplies value by `(1 + U)` and adds `HoldWeight × H` (1.0) to an own-held objective under threat: a perceived enemy sample (`EnemySamples.Accepts`) within `ThreatRadiusMeters` = 250 of it. The radius is vanilla's defend test's (7473-7481), but vanilla's test reads every enemy's true position (`GetDistanceOfClosestUnitOfAlliance`, 7476), so Whiskey uses the samples instead;
    - more risk is accepted: the feasibility weight in the objective score and in `Select` becomes `FeasibilityWeight × (1 − 0.5 × U)`;
    - the side ahead leans to holding plays: `Select` adds `HoldBiasWeight × H` (0.2) to a candidate whose `StanceOf` is Defensive or Neutral, where the stance filter allows it.
  - History sets the weights, not a script (§ 3): Gettysburg's evening assaults on both ends of the line, Beauregard's halt at Shiloh believing the day won.
- **requirement-objective-feasibility** — Each live objective the side does not hold (and each own-held one under threat) gets a feasibility penalty `r_i` for the army's main body to take (or hold) it, by the model's own functions:
  - **The main body here** is one hypothetical body of every direct child that is not a Reserve rect's or the deployed reserve and holds no player-owned strength (requirement-player-share), whatever plan is selected: its strength, morale and members as requirement-bodies computes them; its courier is its latest member's, as `T_main`'s.
  - **Route:** the route screen from the main body's members to the objective (requirement-route-screen); an `Impossible` reason makes the objective infeasible for the main effort.
  - **Time:** `T_i` (requirement-strike-time) against `DaylightHours − DaylightMarginHours`: past it, `daylight_i` = 1 (never infeasible, D16).
  - **Odds:** the detail term with the objective as strike point, `W` = `AssaultHours` and `Pin` 0, `own` 1 to take it and `BaseDefenseRatio + TerrainStrength` to hold an own one, `L` the perceived enemy within `OddsRadiusMeters` of it and `τ` from the enemy mass (requirement-detail-risk's step 3) (the works bar when the objective lies on held works).
  - **Contested:** `contested_i` is the main body's legs to the objective, valued as requirement-detail-risk's strike legs at `StrikeLegWeight`.
  - `r_i = 1 − (1 − daylight_i)(1 − detail_i)(1 − ContestedWeight × contested_i)`, `ContestedWeight` = 0.6.
- **requirement-main-effort-objective** — Concentration: one main effort per army, on the best feasible objective, not the nearest.
  - **The score:** `score_i = Value_i × (1 + U) − FeasibilityWeight × (1 − 0.5 × U) × r_i + Prior_i + Hold_i + T_i^dir`, over the feasible objectives, where `T_i^dir` is every term of the shipped `ObjectiveSelectionScore` except `Value` (`Confidence01`, the BC12 terrain and approach terms, +0.35 main line exposed, +0.20 enemy line, −0.25 unknown). An entry without points (not in the ledger's table) scores `Value × (1 + U) + T_i^dir`, as today but for `U`. Distance enters only through `T_i` and its daylight term.
  - **One chooser:** `ObjectiveScoring.PickMainEffort` over the ledger's table and the director's terms; the director's `TryPickBestObjective` calls it, and `ArmyEvidenceBuilder` reads the director's current primary objective. Nothing else picks a main-effort objective.
  - **The plan follows it:** under the flag, `PlaybookContext.DefaultMainEffortSector` is the lateral band (`TacticalFrontAxis` sector) of the chosen objective instead of the front axis's default, so the allocator's Main and the plan's main effort sector point at it. When the chosen objective changes while a plan runs, `ArmyTickCycle` moves the plan's sector to the new objective's band through a new `ArmyOrchestrator.SetObjectiveMainEffort(sector)` (`WithMainEffortSector`, refused in `Consolidate` and `Withdraw` as `ConsiderMainEffortShift` refuses); under the flag, with a main-effort objective, the shipped odds-based `ConsiderMainEffortShift` is not called (with none, it runs as today), and a FocalAssault plan's main effort is its focal point whatever the band.
  - **One main effort:** under the flag the director does not open its `ParallelObjectives` shape; a second objective is a secondary.
  - **Secondary objectives get economy of force:** the allocator (`DirectChildAllocator.ScreenSecondaryObjectives`, after the zone assignment and the reserve releases) gives each other enemy-held or threatened objective, best score first and at most `MaxSecondaryScreens` = 2, the child the play already holds in the `Screen` role (a Demonstration or ForwardScreen rect's child) nearest to it laterally, through a new `DirectChildIntent.HasScreenTarget`/`ScreenX`/`ScreenZ`: the objective itself when own-held, else the point `ReserveStationMeters` (150) short of it along the own front axis. The posture producer keeps that child's task `Screen` and moves it to the screen target, facing the enemy. One child per objective, a child screens one objective, and no Main, Fix, Support or Reserve child is drawn. A child with player-owned strength, and a FocalAssault demonstration (it pins for the assault), is not drawn. The next evaluation leaves a screening child out of its body, as it leaves out a bound crossing guard (`FeasibilityInput.ScreenChildIds`, from the previous allocation).
  - **Hysteresis:** the chosen objective changes only when another scores `ObjectiveSwitchMargin` = 0.1 more, so the main effort does not flicker. The latch (`ObjectiveLatch`, per alliance, `BattleScope`) clears at a plan change and in `ReplanForNewBattleDay`.
- **requirement-historical-chain** — A battle's designer chain is a strong prior, not an override:
  - **Reading it:** the side's own `AIBattle.objectivechain` (public): each chain with `usedstrategyid` ≥ 0 (a designer chain from `Config/aistrategies.dat`, created by `CreateObjectiveChain`, 7797-7822, for the group's `groupai` id, 85262) names its next objective `objectives[0]` and the hour it is due, `objectivetimetomove[0]`, a battle hour (the hour of day plus 24 × days passed, the form vanilla compares it with). A null `objectives[0]` is a pure wait entry (`CreateObjectiveChain` adds one for a timed pause, 7814-7818): it gives no prior, and the prior returns when vanilla moves past it (7398-7404) and a non-null entry is next. These are the side's own orders, not enemy information.
  - **The prior:** from the hour it is due (vanilla's own test: `objectivetimetomove[0] ≤ Hour + 24 × battlepasseddays`, 6928), that objective's score gains `Prior` = `HistoricalPriorWeight` (0.3), the size of half the value range. Before it is due the prior is 0: Whiskey neither commits its main effort there early on the chain's account nor holds back from it if the objective wins on its own merits.
  - **The timed moves:** vanilla moves a chain's groups at the due hour only while it commands them. A group Whiskey commands follows the executor, so the chain's timing reaches it only as this prior; a chain that runs out switches to dynamic (`usedstrategyid` = −1, 7406-7410) and the prior ends with it.
  - **Several chains:** each due chain's next objective gets the prior once; an objective named by several does not stack. The runtime maps a chain's `Objectives` reference to the capture's `ObjectiveId` by reference against the capture's list.

## 8. Interfaces (pure unless marked runtime)

```csharp
namespace WhiskeyRealism.Tactical.Battle {
  public sealed class RiverRegions {                                   // per battle, BattleScope; built stepwise after TerrainPicture.Ready
    public const float CrossingLinkMeters = 60f; public const int MaxRegions = 64, MaxCrossings = 256;
    public bool Ready { get; } public bool Frozen { get; } public int RegionCount { get; } public int LinkCount { get; }
    public int RegionAt(float x, float z);                             // -1 on open river or outside the map
    public int LinkCrossing(int link, out float x, out float z, out int gridCrossingIndex, out int crossingId);   // ford 5 / bridge 6
    public int RegionsOfLink(int link, Span<int> regions);
    public bool Step(TerrainPicture picture, BattleTerrainGrid grid, long deadlineTicks, Func<long> clock);   // true when Ready (regions, then the link table)
    public const int MaxRoutedLinks = 64;
    public bool IsRouted(int link);
    public float LinkLegCost(int linkA, int linkB);                    // cost units (speed-independent); NaN when the two share no region
  }
  public static class EnemySamples {                                   // BC13's requirement-enemy-samples, extracted; BC13's three inline copies call it
    public const float RememberedMinConfidence = 0.5f;
    public static bool Accepts(in EnemyObservation o);
  }
  public sealed class SingleLinkClusters {                             // extracted from OpeningDetector.ClusterPass; pooled scratch, grown at first use
    public int Label(ReadOnlySpan<float> xs, ReadOnlySpan<float> zs, int count, float linkMeters, Span<int> labels);   // returns the cluster count
  }
  public enum CrossingState : byte { Unknown = 0, Clear = 1, Watched = 2, Held = 3 }
  public sealed class CrossingWatch {                                  // per side, BattleScope
    public const float HoldRadiusMeters = 300f, ClearMemoryHours = 0.25f; public const int MaxCrossingProbesPerTick = 16;
    public void Classify(BattleSnapshot s, RiverRegions r, BattleTerrainGrid g, IPerceivedGround perceived, ReadOnlySpan<float> reachByUnitType, double nowHours);   // RangeOnlyReach per enemy unittyp
    public CrossingState StateOf(int link); public float HeldStrength(int link); public double LastPerceivedHours(int link);
    public int CountBy(CrossingState state);
  }
  // CoveredCrossings.Covers(in EnemyObservation o, float cx, float cz): rule (a), shared by BuildPoints and CrossingWatch;
  // CoveredCrossings.Covers(in UnitRecord u, float cx, float cz): the own-side form (RotationY, FireRange), for the exposure and the enemy's shift costs.
  // EnvelopmentManeuver.InwardLegHitsRiver(BattleTerrainGrid g, float ax, float az, bool hasVia, float vx, float vz, float ex, float ez, float footprintMeters, int[] scratch):
  //   pure, the RouteHitsRiver precedent; PlayExecutorRuntime calls it in place of its straight SampleLine (INWARD-VIA-BC11).
  // PostureIntentPlan.RouteFlagsFor(CommandTaskType task, bool hasManeuverVia, out bool useRoadRoute, out bool avoidCoveredCrossings): pure, called by PostureIntentProducer (VIA-ORDER).
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
    public float RouteCost; public bool RouteEstimated, RouteConfirmed;
    public int MarchVia, AttackVia; public CrossingState MarchCrossing, AttackCrossing;   // link indexes, -1 none
    public float InwardSurplusHours, StrikeContested;   // StrikeContested: the main-body strike legs' weighted value
    public float CourierHours, MarchHours, StrikeHours, DesiredStrikeHours, ReleaseHours, OffsetHours;
    public float EnemyShiftHours, ExposureWindowHours, LocalEnemy, EnemyAgainst, Pin, IsolatedOdds;
    public float Daylight, Detail, Contested, Approach, Exposure, Turn, Penalty, OwnRatio;   // OwnRatio: 1 for an enveloping body, BaseDefenseRatio + TerrainStrength for the main body
    public bool AssaultsWorks;                // requirement-works-pricing's three cases
  }
  public struct CandidateResult {
    public BattlePlanId Plan; public bool Mirrored; public FeasibilityVerdict Verdict; public float Penalty;
    public string Reason;                     // static literal: the verdict reason or the largest term's name
    public int BodyStart, BodyCount;          // into the ledger's pooled BodyEstimate array
  }
  public interface IFeasibilityLookup {                               // what ArmyOrchestrator, the allocator, BC11's runtime and the phase doctrine read
    bool TryGet(BattlePlanId plan, out CandidateResult result);
    bool TryPlannedRelease(string childId, out double releaseHours, out double timeBudgetHours, out float viaX, out float viaZ, out float attackViaX, out float attackViaZ);
    int ThreatCount { get; } bool TryThreat(int index, out CrossingThreat threat, Span<float> ownShiftByChild);
    bool TryFocalPoint(out FocalPoint focal); bool WorksHold(out string reason);   // reason: hopeless-assault / turn-not-struck, null when false
    bool AssaultsWorks(string childId);                                // the child is a member of a works-assaulting body of the selected plan
    ObjectivePressure Pressure { get; } int ObjectiveCount { get; } bool TryObjective(int index, out ObjectiveEstimate estimate);
    bool TryStation(out float stationX, out float stationZ, out float heading, Span<float> shiftByChild);
  }
  public sealed class FeasibilityLedger : IFeasibilityLookup {          // per side, BattleScope; writer: the coordinator's heavy path only
    public const float EvaluateIntervalHours = 0.05f;
    public bool TryGet(BattlePlanId plan, out CandidateResult result);
    public ReadOnlySpan<BodyEstimate> Bodies(in CandidateResult r);
    public double EvaluatedHours { get; }
    public int LookRequests(Span<float> xs, Span<float> zs);           // requirement-crossing-look
    public bool TryPlannedRelease(string childId, out double releaseHours, out double timeBudgetHours, out float viaX, out float viaZ,
      out float attackViaX, out float attackViaZ);   // NaN vias: none
    public int ThreatCount { get; }                                   // the selected Defender candidate's counted crossings
    public bool TryThreat(int index, out CrossingThreat threat, Span<float> ownShiftByChild);   // NaN: a child with player-owned strength
    public int LegOverflow { get; }
    // and the IFeasibilityLookup members TryFocalPoint, WorksHold, AssaultsWorks, TryStation, Pressure, ObjectiveCount, TryObjective
  }
  public interface IExploitLookup { bool IsExploiting(int instanceId); }   // new BC14 seam; runtime adapter over ExploitTable.ForAlliance(a)
  public sealed class LegCostCache {                                   // per side, pooled; cleared when an evaluation starts
    public const int MaxLegCache = 4096;
    public bool TryGet(int bucketKey, int otherKey, out float cost); public void Put(int bucketKey, int otherKey, float cost); public int Overflow { get; }
  }
  public sealed class FeasibilityJob { public bool Running { get; } public int NextCandidate { get; } }   // the resumable evaluation, double-buffered into the ledger
  public struct FeasibilityInput {                                     // one evaluation; pooled arrays
    public BattleSnapshot Snapshot; public TacticalFrontAxis Axis; public BattleStance Stance;
    public BattleTerrainGrid Grid; public TerrainPicture Picture; public RiverRegions Regions; public CrossingWatch Crossings;
    public MarchRules Rules; public OpportunityPersonality Army;        // BC13's struct: Agg01, Caution01, Audacity01
    public float EnemyEstimate, Confidence; public TacticalIntentModel EnemyIntent;   // ArmyIntentInference.Build's result: intent and confidence
    public double NowHours, DaylightHours; public BattlePlanId CurrentPlan; public bool HasPlan;
    public IExploitLookup Exploits;                                    // new BC14 seam over BC13's ExploitTable.ForAlliance(a).IsExploiting(int)
    public IConfirmedLegs Legs;                                        // NavMesh results by (origin bucket, target bucket, crossing)
    public bool[] DeployedReserve;                                     // per direct child: BC10's DirectChildEvidence.DeployedReserve
    public LegCostCache Cache; public SingleLinkClusters Clusters;
    public string[] BoundGuardChildIds; public int BoundGuardCount;     // ReserveReleaseState.ForAlliance(a) at the previous allocation
    public ObjectiveFact[] Objectives; public int ObjectiveCount;       // live objectives, at most MaxObjectives; ObjectiveRuntime
    public VictoryClock Clock; public bool HasClock;                    // false: no pressure (U = H = 0)
    public string[] DueChainObjectiveIds; public int DueChainCount;     // each due designer chain's next objective (ObjectiveId), de-duplicated
    public string[] ScreenChildIds; public int ScreenChildCount;       // children with a screen target at the previous allocation
  }
  public interface IConfirmedLegs { bool TryGet(float x0, float z0, float x1, float z1, int crossing, out float cost, out bool noPath); void Request(float x0, float z0, float x1, float z1, int crossing); }
  public static class ManeuverFeasibility {
    public const float LegStepMeters = 10f, HeldCrossingHours = 1.0f,
      DaylightMarginHours = 0.25f, LateHours = 1.0f, DetailSlope = 0.25f, RiskyPenalty = 0.35f, ReasonThreshold = 0.2f,
      AnchoredEndPenalty = 0.5f, PassiveShiftFactor = 1.5f, BaseDefenseRatio = 1.5f, FeasibilityWeight = 0.4f,
      ReplanPenaltyMargin = 0.3f, ReplanScoreMargin = 0.1f, ThreatLeadHours = 0.25f, ThreatHoldMarginHours = 0.25f, ConfirmCacheHours = 0.25f,
      StrikeLegWeight = 0.5f, GuardStandOffMeters = 100f, EvaluateSliceMs = 1.5f;
    public const int ConfirmCandidates = 3, ConfirmQueriesPerTick = 1, MaxLookRequests = 2, MaxBodies = 6, MaxEnemyBodies = 16,
      MaxChildren = 64, MaxThreatCrossings = 8, MaxObjectives = 16;
    public static void Evaluate(in FeasibilityInput input, FeasibilityLedger into);              // every stance-eligible candidate, to completion (forced)
    public static bool EvaluateSlice(in FeasibilityInput input, FeasibilityJob job, FeasibilityLedger into, long deadlineTicks, Func<long> clock);   // true when published
    public static void EvaluateSelected(in FeasibilityInput input, FeasibilityLedger into);      // the monitor: the selected plan only
    public static float Bar(in OpportunityPersonality p);
    public static float FogPremium(float confidence, float caution01);
    public static float MarchHours(float cost, float baseSpeed, float meanFatigue, in MarchRules r);
    public static float DetailPenalty(float odds, float bar);
    public static void DetailRisk(Span<BodyEstimate> bodies, ReadOnlySpan<float> strikeHours, in EnemyPicture enemy, in CrossingGraphView graph, float bar);   // steps 1-6 of requirement-detail-risk; Evaluate passes S*
    public static float CombinePenalty(in BodyEstimate b);
    public static bool TryBrokenAssumption(in CandidateResult atSelection, in CandidateResult now, ReadOnlySpan<BodyEstimate> before,
      ReadOnlySpan<BodyEstimate> after, in MonitorFacts facts, out string reason);   // reasons in the order of requirement-assumption-monitor
  }
  public static class WorksRules {
    public const float WorksGapMeters = 150f, WorksEndMeters = 50f, SalientDepthMeters = 150f, FocalSampleMeters = 50f,
      SalientFactor = 0.5f, JunctionFactor = 0.8f, WorksOddsFactor = 3.0f, WorksDefenseRatio = 3.0f, AssaultHours = 1.0f,
      FocalWillingness = 0.2f, PreparationHours = 0.25f, EchelonDelayHours = 0.1f, ReserveStationMeters = 150f,
      FocalThreatHours = 1.0f, StationSwitchHours = 0.25f, WorksMinCompletion = 0.5f;
    public const int MaxWorksPoints = 2048, MaxWorksProbesPerTick = 16, MaxSegments = 32, MaxFocalPoints = 256;
    public static bool IsWorksCover(int coverObject);                  // {2, 4, 7, 8, 9, 10, 11, 15, 16}
    public static float Willingness(in OpportunityPersonality p);
  }
  public struct WorksSegment { public float LateralMin, LateralMax; public int ProfileStart, ProfileCount; public float HeldStrength, MeanCover, MeanMorale; public bool Held, MinEndAnchored, MaxEndAnchored; }
  public enum FocalKind : byte { Point = 0, Salient = 1, Junction = 2 }
  public struct FocalPoint { public float X, Z, Score; public FocalKind Kind; public int Segment; }
  public sealed class WorksPicture {                                   // per side, rebuilt per evaluation into pooled arrays; the enemy's or (Defender) the own works
    public int SegmentCount { get; } public ref readonly WorksSegment Segment(int i);
    public bool OnHeldWorks(float x, float z, out int segment);
    public bool TryFocal(out FocalPoint best);                         // the lowest score
    public void BuildEnemy(BattleSnapshot s, in TacticalFrontAxis axis, ReadOnlySpan<int> enemyBodyLabels, IFortificationPoints points, TerrainPicture picture);
    public void BuildOwn(BattleSnapshot s, in TacticalFrontAxis axis, IFortificationPoints points, TerrainPicture picture);
  }
  public interface IFortificationPoints { int Count { get; } void Get(int i, out float x, out float z); bool Perceived(int i); bool InOwnZone(int i); }   // runtime: the battle's ObstacleLine pieces (modelobj present, completion ≥ WorksMinCompletion),
                                                                       //   every ObstacleLine at battle start and deployment end, BattleUI.obstaclelines and the known lines re-read each evaluation, at most MaxWorksPoints;
                                                                       //   probe stamps keyed by modelobj instance id; InOwnZone from Frontline2.CheckIfWithinZone at enumeration
  public struct MonitorFacts {                                         // per selected plan; pooled arrays indexed like the bodies
    public double SelectedAtHours, NowHours; public ManeuverPhase[] Phase; public int[] CrossingStart, CrossingCount; public bool[] CrossingPassed;   // per crossing on each body's route
    public double[] PlannedReleaseHours;
    public string[] AbandonedChildIds; public int AbandonedCount; public bool SelectedWhileUnknown, MapKnownFired;
  }
  public struct ObjectiveFact {                                        // one live objective (public state: no fog), read once per evaluation
    public string Id; public float X, Z, Points; public bool OwnHeld, EnemyHeld, Threatened;   // Id: the capture's ObjectiveId; Points: alliancepoints[own] or points; Threatened: a perceived sample within 250
  }
  public struct VictoryClock { public float ShareOwn, ShareEnemy, Bar, DaylightHours; }   // objectivepointsheld per side, GetVictoryPointsRelation(own side), requirement-daylight
  public struct ObjectivePressure { public float Behind, Ahead, Late, U, H; }
  public struct ObjectiveEstimate {
    public string Id; public float V, Value, Penalty, Prior, Hold, Score; public bool Feasible; public string Reason;   // Score without the director's terms (T_i^dir)
  }
  public static class ObjectiveScoring {
    public const float ValueFloor = 0.2f, ValueSpan = 0.6f, OwnHeldFactor = 0.5625f, LateDayHours = 4f, HoldWeight = 1f, HoldBiasWeight = 0.2f,
      HistoricalPriorWeight = 0.3f, ObjectiveSwitchMargin = 0.1f, ThreatRadiusMeters = 250f, ContestedWeight = 0.6f;
    public const int MaxSecondaryScreens = 2;
    public static float Normalised(float points, float maxPoints);    // 0.5 when maxPoints ≤ 0
    public static float Value(float v, bool ownHeld);
    public static ObjectivePressure Pressure(in VictoryClock clock, float agg01);
    public static float Penalty(float daylight, float detail, float contested);
    public static float Score(in ObjectiveEstimate e, in ObjectivePressure p);   // Value·(1+U) − FeasibilityWeight·(1 − 0.5U)·Penalty + Prior + Hold
    public static int PickMainEffort(ReadOnlySpan<ObjectiveEstimate> table, ReadOnlySpan<float> directorTerms, string currentId);   // index; feasible only; hysteresis on currentId (null none); −1 none
    public static float SelectRiskWeight(in ObjectivePressure p);     // FeasibilityWeight × (1 − 0.5 × U)
    public static float HoldBias(in ObjectivePressure p, TacticalPlaybookCatalog.PlaybookStance stance);   // HoldBiasWeight × H for Defensive and Neutral, else 0
  }
}
// PlayProjection.ProjectInto(template, …, Span<WorldPlayRect>) and PlayCorpsAssignment.AssignInto(rects, corps, Span<CorpsAssignment>): allocation-free, parity with Project / Assign.
// PlaybookContext.Feasibility (IFeasibilityLookup, null = off); TacticalPlaybookCatalog.Select: Impossible skipped after the jitter and crest draws, − FeasibilityWeight × penalty;
//   Select reports its candidates (ranked, with the feasibility term) into a pooled SelectionReport for the TacticalPlanSelection row.
// ReplanTrigger.ManeuverAssumptionBroken (appended); ReplanTriggerInput.AssumptionReason (static literal, null none).
// ManeuverInput.HasPlannedRelease/PlannedReleaseHours, HasTimeBudget/TimeBudgetHours, HasVia/ViaX/ViaZ, HasAttackVia/AttackViaX/AttackViaZ;
//   ManeuverState.HasFirstPlannedRelease/FirstPlannedReleaseHours, HasBudget/BudgetHours, HasVia/ViaX/ViaZ, HasAttackVia/AttackViaX/AttackViaZ (latched at the March); ManeuverSpec.ProjectedMirrored, HoldsForRelease;
//   ManeuverOrder.HasVia/ViaX/ViaZ (the March's or the Attack's via); CommandDoctrineOrder.ViaX, ViaZ (NaN none); ManeuverNodeEntry via; the fix's planned release reaches StepStartDelay through a start-delay ManeuverSpec;
//   PlayExecutorRuntime samples the inward leg as anchor→attack via→line end, the via's 60-unit footprint excluded (EnvelopmentManeuver.InwardLegClear unchanged; its input changes);
// BattleRecon target kind Crossing (150-unit stand-off on the own side); DirectChildAllocator.ReleaseReserveToCrossings after ReleaseReserveToThreatenedWing, reason crossing-threat.
// public struct CrossingThreat { int Link; float X, Z; bool Left; int OwnBankRegion; float GuardX, GuardZ, GuardHeading; float EnemyAtCrossing, EnemyAtFlank, CandidateOwnShift; }
// ReserveReleaseContext gains CrossingThreats (pooled CrossingThreat[] plus a pooled OwnShiftByChild matrix, crossing × child), PlayerOwned (bool per child),
//   and an explicit Staggered flag (State and Exploiting are passed whenever BC13's staggered flag or BC14's flag is on).
// DirectChildIntent.HasGuardPoint/GuardX/GuardZ/GuardHeading and its release reason.
// ReserveReleaseState.ForAlliance(int) (per alliance, BattleScope.Register in its static constructor);
//   public struct CrossingBinding { string ChildId; int Link; double OwnShiftAtRelease, ReleasedAtHours; } in a pooled table of MaxCrossingGuards = 8:
//   BindCrossing(in CrossingBinding), TryCrossingOf(string childId, out CrossingBinding), UnbindCrossing(string childId), BoundCount;
//   public struct StationBinding { string ChildId; float FocalX, FocalZ; double ArrivalAtChoice; } (one per side): SetStation, TryStation, ClearStation;
//   Clear() clears both, ClearTrigger() neither.
// Works: BattlePlanId.FocalAssault and FocalAssaultPlaybook (appended last; precondition: Feasibility set and a focal point); PlayTemplateCatalog "FocalAssault" projected toward IFeasibilityLookup.TryFocalPoint;
//   PlayRectKind.AssaultColumn (role Main, a BC11 maneuver of ManeuverSpec kind AssaultColumn: Attack on the focal point, March, Turn and Attack carry ManeuverOrder.HasFormation/Formation = March Column, Turn → Attack waits for formation 4 or ColumnWaitHours and the preparation floor,
//   ManeuverInput.GroupFormation; BuildInput's inward target is the focal point; no anchored-high-ground check)
//   and PlayRectKind.AssaultSupport (role SupportMain, start-delay HoldsForRelease until the column's strike + EchelonDelayHours); ManeuverState.HasWorksClear/WorksClearHours;
//   CorpsPosition.ContainsPlayerOwned (PlayAnchor.CollectOwn; Assign/AssignInto skip it for AssaultColumn and AssaultSupport);
//   CommandDoctrineOrder.PreparationX/Z (NaN none), read by ArtilleryIntentProducer into BattleArtilleryState.HasPreparation/PreparationX/PreparationZ (resolved for a battery whose
//   group order carries a point, whether or not it bombards); BattleWriterPolicy.TryBombardStartIntent(snapshot, index, in state, out BattleOrderIntent) (pure: vanilla's per-battery start test
//   at 3889-3893 over the snapshot and state, with the point within FireRange in place of vanilla's closest enemy within it); the intent: Channel CombatBehavior, Value 9 (CombatBehaviorBombard), TargetX/Z the point,
//   new IntentFlags.BombardStart = 2048, Source ArtilleryDoctrine, Priority Executor; OrderExecutorRuntime, for that flag, calls the runtime OrderExecutorRuntime.StartBombard(Regiment unit,
//   float x, float z) after the guard (ChangeCombatBehavior 9 through TacticalCombatBehaviorWriter, then bombardposition and bombardrange = GamePrefs.bombardmentmaxrangesize);
//   BattleArtilleryState.AmmoMean (Σ ammo / 3, vanilla's form); the preparation exemption in BattleWriterPolicy.ArtilleryDoctrineIntents while HasPreparation;
//   PostureWorksGuard.Task(task, worksHold, assaultsWorks, inManeuverTable, ExploitOverrideKind exploitKind) and .Apply(worksHold, assaultsWorks, inManeuverTable, exploitKind,
//   ref state, hasOrder, ref order) (pure, beside PostureStanceGuard in CommandPostureExecutor.cs; exploitKind other than None passes unchanged);
//   TacticalPhaseProgressionDoctrine.Input.WorksHold; ManeuverInput.HoldForWorks (set by PlayExecutorRuntime.StepManeuvers from WorksHold's hopeless-assault for works-assaulting bodies);
//   ManeuverOrder.Formation → ManeuverNodeEntry.Formation → CommandDoctrineOrder.Formation (int, −1 none); CommandFormationCorrection.TargetFormationForTask(task, current, orderFormation)
//   returns orderFormation when ≥ 0, else today's result;
//   IFeasibilityLookup.TryFocalPoint, WorksHold(out reason), AssaultsWorks, TryStation (station point and heading, per-child shift to it); DirectChildAllocator.StationReserveBehindFocalPoint after ReleaseReserveToCrossings.
// Objectives: DirectChildAllocator.ScreenSecondaryObjectives (after the releases) and DirectChildIntent.HasScreenTarget/ScreenX/ScreenZ; ArmyOrchestrator.SetObjectiveMainEffort(int sector);
//   ObjectiveLatch.ForAlliance(int) (the current main-effort ObjectiveId, BattleScope; cleared at a plan change and in ReplanForNewBattleDay); ObjectiveRuntime (runtime).
// Test seams (pure): PlayExecutorRules.CanMirror(PlayTemplate); PlayAnchor.PlayTarget(BattlePlanId plan, bool hasFocal, float fx, float fz, float enemyX, float enemyZ, out float x, out float z);
//   EnvelopmentManeuver.HoldForWorksFor(string worksHoldReason, bool assaultsWorks, bool startDelay); TacticalPlaybookCatalog.ResponseTargetFor and DirectChildAllocator.AllowsProvisionalAttackAxis become internal.
// EnemyPicture (samples, P, N, m_E, mass, L_j, and in the Defender run the SingleLinkClusters bodies) and CrossingGraphView (regions, the link table, states, own-side covers) are the evaluation's pooled internal views.
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
| Release a reserve to a threatened crossing | `DirectChildAllocator.ReleaseReserveToCrossings` (after the flank release), comparing the ledger's precomputed `OwnShiftByChild`; executed by the posture producer | The ledger; a player-owned or exploiting child is never the one released |
| Build the works picture and choose the focal point | The coordinator's heavy path (the evaluation), per AI-run side | Patches; producers; any reader of a hidden unit |
| Hold the works assault (`WorksHold`) | `FeasibilityLedger.WorksHold`, read by `PostureWorksGuard` (attack tasks of works-assaulting children → `FixEnemy`), by `PlayExecutorRuntime.StepManeuvers` (`HoldForWorks`) and by `TacticalPhaseProgressionDoctrine` | The allocator |
| Give a secondary objective a screen | `DirectChildAllocator.ScreenSecondaryObjectives` (`DirectChildIntent` screen target); the posture producer moves the child | The director; producers other than the posture producer |
| Choose the main-effort objective | `ObjectiveScoring.PickMainEffort`, called by `TacticalOperationDirector.TryPickBestObjective` over the ledger's objective table; `ArmyTickCycle` moves the plan's sector (`SetObjectiveMainEffort`) | Patches; producers; `ArmyEvidenceBuilder` (it reads the director's choice) |
| Form the column, march it and launch the assault | BC11 (`AssaultColumn` maneuver), its orders executed by the posture producer through the arbiter and executor (formation intent, waypoints) | The ledger; any new writer |
| Start a bombardment on the focal point | The artillery doctrine producer submits `BombardStart`; the executor writes it through `OrderExecutorRuntime.StartBombard` (`ChangeCombatBehavior` 9, `bombardposition`, `bombardrange`) | The ledger; any patch; a player-owned battery; Commander Mode other than Active |
| Station a Defender's reserve behind a focal point | `DirectChildAllocator.StationReserveBehindFocalPoint`, executed by the posture producer | The ledger; a player-owned or exploiting child is never stationed |
| Move a player-owned unit | Nobody: no feasibility output reaches a `PlayerOwned` unit (the executor's guard as today) | Every Whiskey path |

**Lifecycle table.**

| Entity / binding | Create | Change | Unbind | Delete | Migration of live dependents |
|---|---|---|---|---|---|
| River regions and the link table | First steps after the picture is ready (regions, then link pairs) | Never (terrain is static for the battle; frozen state read once) | — | `BattleScope` reset | Consumers see "map unknown" until both are ready |
| Crossing state | First heavy tick with regions ready | Every heavy tick | — | `BattleScope` reset | A state change on a selected route raises the monitor |
| Ledger candidate results | Each evaluation | Each evaluation (replaced whole) | — | `BattleScope` reset; a stand-down or new battle day clears it | Selection reads the latest. **Version semantics: latest**; the replan reads results no older than `EvaluateIntervalHours` |
| Selected-plan snapshot (the results at selection, for the monitor) | Each selection | Never | Replan | Replan, `BattleScope` | The monitor compares against it; a replan replaces it |
| Planned release / time budget / via of a child (binding ledger → BC11), wings and the holding fix | Selection of a plan with enveloping bodies | Every heavy tick until the child leaves its Hold (release time); the Hold ceiling is anchored to the first planned release (latched) | Release (the release time is spent); a mirror flip (the ledger values stop applying); plan change; abandon | With the plan | **Pin at release:** once BC11 launches the March, its via and budget are latched in `ManeuverState` for that leg; a later estimate changes nothing except through the monitor's replan |
| Confirmed leg cache | A NavMesh result | — | Expiry after `ConfirmCacheHours` | `BattleScope` reset | An expired leg is re-queued; the estimate stands meanwhile |
| Leg cost cache | A leg first priced in an evaluation | Never within the evaluation | — | Cleared when the next evaluation starts | A confirmed leg replaces its estimate; overflow is priced uncached and counted |
| Evaluation job (second buffer) | A scheduled evaluation's first slice | Each slice adds candidates | — | Published whole into the ledger at its last candidate, then reused | A replan reads the last published ledger; a forced evaluation completes the job at once |
| Look request | Each evaluation | — | The crossing turns Clear, Watched or Held | Next evaluation | BC9 ends a mission whose request vanished at its next step |
| Crossing-threat binding (child → crossing, in `ReserveReleaseState.ForAlliance(a)`) | The allocator's crossing release, when `enemyAtCrossing` ≤ the chosen child's `ownShift_c` + `ThreatLeadHours` | Never: the crossing and `OwnShiftAtRelease` are latched at the release | The threat stops (arrival beyond `OwnShiftAtRelease + ThreatLeadHours + ThreatHoldMarginHours`, or no enemy body perceived); the child gone, exploiting or holding player-owned strength | A plan change (`Replan`), a new battle day (`ReplanForNewBattleDay`, before it replans), a stand-down (`BattleCoreTick.StandDown`), and `BattleScope` at battle end | The child reverts to Reserve on the next allocation. The guard's own cover never ends the threat (it is left out of the cover test), so release and revert cannot alternate. BC13's `ClearTrigger` leaves it alone |
| Works picture (segments, ends, focal points) | Each evaluation | Each evaluation (replaced whole); perceived works points accumulate for the battle | — | `BattleScope` reset | **Latest** until the column's March latches the focal point into BC11's state; while FocalAssault is selected an evaluation with no focal point republishes the last one. The monitor re-reads the current one: a focal point that loses its holders' advantage raises `detail` like any body |
| Objective table (values, `r`, prior) and the pressure | Each evaluation | Each evaluation (replaced whole) | — | `BattleScope` reset | **Latest**; the chosen main-effort objective (`ObjectiveLatch`) changes only past `ObjectiveSwitchMargin`, and the latch clears at a plan change and in `ReplanForNewBattleDay` |
| Screen target (child → secondary objective) | The allocator, each allocation | Each allocation (recomputed) | The objective no longer enemy-held or threatened; the child no longer `Screen`, gone or player-owned | Each allocation; `BattleScope` | The allocator's last write; a child screens one objective |
| Focal-assault plan state (the column, support and demonstration maneuvers) | Selection of FocalAssault | As BC11's maneuvers | Plan change, abandon | With the plan | As BC11's; the support's planned release follows the column's strike |
| Reserve station (child → focal point) | The allocator, on the most threatened own focal point | Moves only for an arrival earlier by `StationSwitchHours` | No enemy body within `FocalThreatHours` of any own focal point; the child gone, exploiting or player-owned | Plan change, new battle day, stand-down (with `ReserveReleaseState`) | The child keeps role `Reserve` throughout; every release still sees it |

**Shared rows and conflict rules.**

| Shared row | Writers | Conflict rule |
|---|---|---|
| The ledger | The heavy path only | One writer. Readers (the catalog, BC11's runtime, the recon producer, the allocator) read the published state; no reader writes it |
| A wing's Hold release | BC11's own rule and the ledger's planned release | When the ledger has a planned release, its time test replaces both of BC11's waits; the route-known test and the checks then apply in today's order after it (requirement-synchronized-release). Never both: the release is one expression |
| A wing's via and road route | The ledger (via) and BC10's road router | A maneuver March with a via gets no road route (requirement-crossing-via); without a via, BC10's rules as today |
| `AvoidCoveredCrossings` vs a via | Posture pipeline | A maneuver March with a via never sets it (BC10's rule would drop the via) |
| A child wanted by a play rect and a BC13 exploit | BC11, BC13 | Mutually exclusive as BC13 defined; the hypothetical layout honours the same exclusion |
| A Reserve child wanted by an exploit, a crisis, a flank threat and a crossing threat | `DirectChildAllocator.ReleaseReserves` | The shipped order, then BC14's: crisis and stagger (`StaggeredRelease`, or today's loop), BC11's flank release, then the crossing release (crossings by earliest `enemyAtCrossing`). Each step takes only children still `Reserve`, so a child is released once. The crossing release keeps the held reserves at or above `FloorOf(R0x, floorShare)` and touches neither BC13's `InitialReserves` nor `LastReleaseHours`. An exploiting or player-owned child is never released to a crossing |
| A Reserve child's position (works station) | `DirectChildAllocator` | After the crossing release; only a child still `Reserve`; a stationed child stays `Reserve`, so a later crisis, stagger, flank or crossing release takes it first and the station is then re-chosen |
| The phase against works | `TacticalPhaseProgressionDoctrine` (shipped rules) and `WorksHold` | `WorksHold` wins while true; the shipped rules otherwise |
| A works-assaulting child's task | The leaf cascade or doctrine order, BC9's `PostureStanceGuard`, BC13's opportunity override, and `PostureWorksGuard` | The works guard applies where the stance guard applies, after it (a Defender's attack is already a hold). It does not apply to a BC13 exploit asset or a node following one (`ExploitOverrideKind` other than `None`): a live exploit through a breach still advances |
| The plan's main-effort sector | The shipped odds-based `ConsiderMainEffortShift` and BC14's `SetObjectiveMainEffort` | Under the flag with a main-effort objective, only `SetObjectiveMainEffort`; otherwise the shipped shift |
| The column's Turn → Attack | BC11's Turn rules, the column wait and the preparation floor | First contact ends the Turn at once; otherwise the later of formation 4 (or `ColumnWaitHours`) and the preparation floor |
| A preparing battery's combat behavior | The doctrine's cancel and counter-battery rules and the bombard start | The start, until the column's Attack; the doctrine's safety rules (ammo below 0.10, an unsupported close enemy, friendly danger close) always win |
| BC9 recon targets | BC9's planner | Crossing looks come after stale entry points and stale bands, inside the same cap; one mission per target |
| The plan | Replan triggers (existing and `ManeuverAssumptionBroken`) | One replan per cooldown; the first trigger evaluated wins, as today (`ArmyReplanTriggers.Evaluate` order; the new trigger is appended last). A pre-empted assumption is named `preempted` on its row; the replan that pre-empted it reads the ledger and so sees the broken assumption anyway |

---

## 10. Exceptions per step

| Step | Absent | Stale | Deleted | Failed |
|---|---|---|---|---|
| Picture / regions | Not ready, or no grid → map unknown, verdicts `Unknown`, BC11 as today | — (static) | — | Build exception → OnceLog `tactical-battle-core:river-regions-failed`, map unknown for the battle |
| Crossing classification | No perceived-ground runtime (BC13 off) → no crossing is ever Clear; Held and Watched still apply | A probe older than `ClearMemoryHours` → not Clear | An observation dropped → re-classified next tick | `IsPerceived` throws → that point unperceived (BC13's rule) |
| Hypothetical layout | No direct children → no candidate has bodies; every verdict `Unknown` | Child positions are this snapshot's | A child destroyed between evaluations → absent next evaluation | — |
| Route screen | No link between regions → march leg `Impossible` `no-crossing`; inward leg `river-on-leg`; main-body strike leg `contested` 1 (`no-strike-crossing`) | Estimates replaced by confirmations as they arrive | — | — |
| NavMesh confirmation | Budget spent → the estimate stands, the leg stays queued | Older than `ConfirmCacheHours` → re-queued | — | A query that throws → logged once (`tactical-battle-core:feasibility-nav-failed`), the estimate stands and the leg is not retried for `ConfirmCacheHours` |
| Enemy picture | No samples → the assumed enemy point (BC10), the one enemy body there, and the ledger estimate | Remembered below 0.5 ignored | — | — |
| Daylight | `uniStormSystem` unreadable → `DaylightHours` = +∞ (no `after-dark`), OnceLog `tactical-battle-core:feasibility-daylight-missing` | — | — | — |
| Selection | Ledger null (flag off) → today's selection | Ledger older than `EvaluateIntervalHours` → an evaluation runs first (requirement-evaluation-cadence) | A candidate missing from the ledger → term 0, not filtered | All `Impossible` → requirement-never-empty |
| Monitor | No enveloping bodies → only `map-known` is watched (requirement-assumption-monitor) | — | A body's child destroyed → BC11's `lost-strength` → `wing-abandoned` | — |
| Crossing release | No threat list (flag off, not a Defender, map unknown) → no crossing release; no eligible Reserve child → none | The threat list is the last published evaluation's | The bound child destroyed or detached → binding ends | — |
| Works picture | No works sample and no perceived works point → no works: no bar, no gate, FocalAssault not offered | Remembered works samples below 0.5 ignored; a works point once perceived stays | A holder destroyed → gone from the next picture; a piece destroyed or below `WorksMinCompletion` → dropped at the next re-read | `ObstacleLine` enumeration or `BattleUI.obstaclelines` unreadable → OnceLog `tactical-battle-core:works-points-missing`, works from samples only; past `MaxWorksPoints` → the farthest pieces dropped and counted |
| Focal assault | No focal point → not offered; no battery in range → no preparation | The focal point is the latest evaluation's until the column's March latches it | The column's child destroyed → BC11's `lost-strength` → `wing-abandoned` | BC11 abandons the column → the monitor replans as for any wing |
| Objectives | No live objective → no objective table, the director's shipped fallback; all at 0 points → `v` 0.5; no secondary objective or no `Screen` child → no screen | Points and ownership are read each evaluation; `Threatened` reads the samples (Remembered ones by BC13's confidence rule) | An objective deactivated → gone from the next table, its screen and, if it was the main effort, the latch (the next choice is fresh) | `BattleUnits` or `AIBattle.objectivechain` unreadable → OnceLog `tactical-battle-core:objectives-missing`, no pressure and no prior |
| BC11 release | No ledger entry for the child → BC11's rule | The release recomputed every heavy tick until release | — | — |

References are **latest**, never pinned, except the via and time budget pinned at release (§ 9).

---

## 11. Performance, configuration, telemetry

- **requirement-frame-budget** — The work, counted, and how it is spread:
  - **Path searches per evaluation.** A candidate's members are bounded by its rects: at most `m` = 5 members (no template has more than five rects), in at most `b` = 3 bodies (DoubleEnvelopment's two wings and the main body; the other templates have fewer). Its path searches are at most: `m` march legs + `m` strike legs + `b` shift times `τ` + `b(b − 1)` support times `σ` between bodies + `m(b − 1)` more for `σ` from each fixing member's anchor = 5 + 5 + 3 + 6 + 10 = **29 path searches per candidate**. With at most 22 candidates (19 playbooks, FocalAssault included, less the stance-filtered ones, plus the mirrored side of the three off-centre single-flank templates) that is at most **638 path searches per evaluation**. The direct children are not bounded by the templates (BC13 sizes its per-child arrays at `MaxChildren` = 64 children), so the per-child vectors are sized 64 children: a Defender's `OwnShiftByChild` takes at most 64 children × `MaxThreatCrossings` = 8 crossings = 512 path searches per evaluation, the station vector at most 64 more, and the objective table (§ 7B) at most 5 members × `MaxObjectives` = 16 objectives = 80 more.
  - **Each query** is a Dijkstra over at most `MaxRoutedLinks` + 2 nodes. With the routed links `k` of the two regions on the way (a handful on real maps; 10 in FEAS-TIME), it relaxes at most `(k + 2)²` = 144 edges: about 92,000 relaxations for the 638 path searches of an Attacker's evaluation.
  - **Sampled legs** are the dominant cost and are cached (requirement-link-legs). Link-to-link legs are in the per-battle table and are never sampled during an evaluation. A point's legs to its region's links are sampled once per (50-unit bucket, link): with `U` distinct point buckets (children, rect anchors, strike points, enemy points and crossings; at most 64 + 6 × 22 + 4 + 16 + 16 = 232 point buckets) that is at most `U × k` = 2,320 sampled legs at `k` = 10 links, plus one point-to-point leg per same-region path search (≤ 638 legs): 2,958 legs, under `MaxLegCache` (4,096 entries).
  - **The works picture** (requirement-works-picture, -shape) is built once per evaluation: one sort of the works samples and perceived works points by lateral, one pass for segments, ends and salients, and one score per focal point over the holders within 300 (at most `MaxFocalPoints` × `MaxEnemyBodies` = 4,096 distance tests). The works probes are at most 16 per side per tick, in the slice. Each leg is at most 300 samples at `LegStepMeters` (3,000 units).
  - **Slices.** A scheduled evaluation runs in slices of at most 1.5 ms (`EvaluateSliceMs`) per side per heavy tick, the deadline checked between legs (`EvaluateSlice`, the BC12 stepped-clock pattern). The 16 crossing probes of the tick run first, inside the same scope and against the same slice (requirement-crossing-state). A forced evaluation completes in one tick; at battle start the map is normally unknown and it costs only the verdict pass. The expected count, an estimate to be read on `TacticalManeuverFeasibility`'s `slices`: about 0.4 million grid samples and 0.1 million relaxations at 5-20 ns each make 2.5-10 ms, so 2-7 slices of 1.5 ms per evaluation at 10 links (`k`); at one heavy tick per vanilla side-stat cycle that is well inside `EvaluateIntervalHours`.
  - **The monitor** is one candidate per heavy tick (≤ 41 queries, its legs mostly cached), not sliced.
  - **NavMesh:** one query per side per tick at most.
  - **The region and link-table build:** inside the picture runtime's 4 ms steps; at most 2,016 link pairs.
  - **Scopes:** `tactical.feasibility.evaluate` (threshold 2.0 ms; one slice, the probes included), `tactical.feasibility.monitor` (0.5 ms), `tactical.feasibility.navmesh` (2.0 ms), `tactical.river-regions.build` (5.0 ms, as the picture's step). Target: criterion-frame-budget.
- **requirement-zero-alloc** — No allocation after warm-up: pooled ledger arrays (`MaxBodies` per candidate) on both buffers, the pooled projection and assignment overloads, the Dijkstra's pooled arrays (`MaxRoutedLinks` + 2), the `LegCostCache` open-addressing table (`MaxLegCache`, allocated once), the link table (allocated once per battle at the build), `SingleLinkClusters`' scratch (grown at first use), the resumable job's state, the threat list and the `OwnShiftByChild` matrix, the `ReserveReleaseState` crossing table (`MaxCrossingGuards`), string literals for reasons, telemetry strings built only on emitted rows. Pinned by FEAS-ALLOC.
- **requirement-config** — `Tactical` section, default `true`, with a reader and a truthful description:

  | Flag | Covers | Off |
  |---|---|---|
  | `Enable Maneuver Feasibility` | §§ 4-7B except requirement-stance-before-pick (the works picture, the works bar, turn-first, the works hold gate and `HoldForWorks`, FocalAssault and its execution with the bombard start, the Defender's works station, and the objective value, victory pressure, main-effort choice and historical prior included): the evaluation, the selection term, the side choice, the monitor and its replan trigger, the synchronized release, the time budget, the crossing via, the look requests, the defender exposure and the crossing-threat release | `PlaybookContext.Feasibility` null; BC11's Hold, ceiling, timeout and route exactly as today; no look requests; no crossing-threat release, no FocalAssault (its precondition fails), no works hold, no bombard start, no reserve station, today's 0.45 / 0.80 objective values, the director's shipped best-score choice with `ParallelObjectives`, the shipped odds-based main-effort shift and no secondary screens; the ledger, regions and crossing watch are not built |

  requirement-stance-before-pick is a bug fix and is not behind the flag. Rollback of every Whiskey command write stays Commander Mode `MonitorOnly` / `Off`.
- **requirement-telemetry** — Through the TelemetryRouter tuning-log sink, bounded:
  - `TacticalPlanSelection` (Decision, one per selection or kept plan, emitted by `ArmyOrchestrator.TryPickPlan` after `Select`, with the flag off too, so on/off runs compare): `trigger` (`initial`, the replan trigger, `new-day`), `kept`, `stance`, `odds` (`CurrentOdds`), `winner`, `mirrored`, `candidates`, `preconditionFiltered`, `stanceFiltered`, `crestFiltered`, `feasibilityFiltered`, `allImpossible`, `ledgerAgeHours`, and the top three by final score, each with `plan`, `score`, `feasibilityTerm`, `verdict`, `reason`. This also fills today's gap: no row carries candidate scores (control: the plan-selection emitters `[TacticalPlan]`, `[TacticalReplan]`, `TacticalPlayExecution` and `TacticalEnvelopment` and `Select`'s unemitted `preconditionFiltered`, reads 5, under: source; failure-under: source).
  - `TacticalManeuverFeasibility` (Decision, per candidate per evaluation when its verdict or penalty bucket (0.1) changed, throttled per candidate to one per 0.05 doctrine-h): `plan`, `mirrored`, `verdict`, `penalty`, `reason`, `bodies`, `selected`; and once per published evaluation a summary row (`plan=all`) with `slices`, `forced`, `queries`, `legsSampled`, `legOverflow`.
  - `TacticalManeuverBody` (Decision, per body of the selected plan at selection and on each monitor change, and of the top confirm candidates per evaluation): `plan`, `body`, `child`, `kind`, `strength`, `routeUnits`, `routeSource` (`estimate` / `navmesh`), `marchVia` and `attackVia` (link index or −1) with their states, `strikeContested`, `courierHours`, `marchHours`, `inwardSurplusHours`, `strikeHours`, `desiredStrikeHours`, `releaseHours`, `enemyShiftHours`, `windowHours`, `pin`, `enemyAgainst`, `isolatedOdds`, `bar`, `ownRatio`, the six terms (`daylight`, `detail`, `contested`, `approach`, `exposure`, `turn`), `penalty`, and on a monitor row `assumption` and `suppression` (`cooldown`, `committed`, `margin`, `preempted`, or none).
  - `TacticalReplan` (Decision, structured, one per replan and per kept plan, never deduped): `trigger`, `assumption`, `kept`, `newPlan`, `mirrored`, `doctrineHours`. Today's deduped `[TacticalReplan]` log line stays for the main log.
  - `TacticalCrossingWatch` (State, per side every 0.1 doctrine-h): `held`, `watched`, `clear`, `unknown`, `probes`, `lookRequests`, `regions`, `links`.
  - `TacticalEnvelopment` (existing) gains `doctrineHours` (the doctrine clock at the row's transition, on every row: Hold, March, Turn, Attack, abandon, and a holding fix's start-delay rows), `plannedReleaseHours`, `timeBudgetHours`, `via`, `attackVia` and, on the Turn transition, `arrivedHours`, `predictedArrivalHours` and `predictedMarchHours` (the body's march hours latched at the March; criterion-arrival-prediction). A holding fix's `Released` row carries `plannedReleaseHours` beside its `doctrineHours` (criterion-strike-spread's guardrail).
  - `TacticalReplan` rows carry `trigger=ManeuverAssumptionBroken` and `assumption=<reason>`.
  - `TacticalReserveRelease` gains `reason=crossing-threat`, with `crossing` (link index), `guardX`, `guardZ`, `guardHeading`, `ownShiftHours`, `enemyArrivalHours`, `held` and `floor` on those rows, and a revert row (`reverted=true`) when the threat stops.
  - `TacticalEnemyWorks` (State, per side every 0.1 doctrine-h, works picture present): `segments`, `heldSegments`, `openEnds`, `anchoredEnds`, `salients`, `junctions`, `worksSamples`, `worksPointsPerceived`, `worksProbes`, `worksPointsDropped` (past `MaxWorksPoints`), and the focal point (`focalX`, `focalZ`, `focalKind`, `focalScore`); the Defender's own-works row carries the station's focal point and child.
  - `TacticalWorksHold` (Decision, on each change of the gate per side, and every 0.1 doctrine-h while it holds): `hold`, `reason` (`hopeless-assault` / `turn-not-struck`), `plan`, `detail`, `barUsed`, `assaultChildren`, `guarded` (attack tasks the posture guard turned to `FixEnemy` since the last row), `attackTasksOnWorks` (attack tasks the posture producer issued to works-assaulting children since the last row), `doctrineHours`.
  - `TacticalBombardStart` (Decision, one per executed start): `battery`, `pointX`, `pointZ`, `range`, `plan`, `outcome` (`issued` / the guard's refusal).
  - `TacticalObjectivePressure` (State, per side every 0.1 doctrine-h, with live objectives): `sOwn`, `sEnemy`, `bar`, `daylightHours`, `behind`, `ahead`, `late`, `U`, `H`, `dueChains`.
  - `TacticalMainEffort` (Decision, on each change of the main-effort objective and at each selection): `objective`, `points`, `value`, `r`, `prior`, `hold`, `U`, `H`, `score`, `runnerUp`, `runnerUpScore`, `maxFeasibleValueObjective` and `maxFeasibleValue` (the highest `Value` among objectives with `r` below `RiskyPenalty`), `nearestObjective`, `band`, `secondaryScreens`.
  - `TacticalManeuverBody` gains `assaultsWorks`, `barUsed` and `enemyOpen` (the outside-works enemy of a holding fix); `TacticalPlanSelection`'s top three carry FocalAssault's reason when it is filtered (`turn-available`, `declined`) and its `willingness`; `TacticalEnvelopment` rows of an `AssaultColumn` carry `focalKind`, `preparationHours` and `formation`; `TacticalReserveRelease` gains `reason=works-station` rows (child, focal point, station).
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
  3. on the held-crossing map at even odds, no dividing play and no play that marches across the held bridge is selected; a bank-holding single-body play is, every bridge-crossing play's term is at least 0.1 below every bank-holder's, and with the flag off a dividing play is selected instead (the hold comes from the model). At 2:1 a mirrored ManeuverAndFix (pin and cross) is selected, with the clear ford as its march via;
  4. the piecemeal fixture shows `detail` ≥ 0.7 without the synchronized release and 0 with it;
  5. on the held-crossing fixture, a bold commander (audacity01 0.9) reaches pin-and-cross `detail` 0 at a lower own-to-enemy ratio than the mid commander (D14);
  6. against an entrenched line with an open end, the turn around the open end is selected and its fix holds (FEAS-SCN-6);
  7. against an anchored entrenched line, an aggressive commander assaults the focal point, a cautious one declines and holds, a mid one holds a hopeless assault (FEAS-SCN-7);
  8. against an entrenched salient, the focal point is the apex (FEAS-SCN-8);
  9. against an entrenched player, only perceived pieces of the drawn line count, and the outcome is scenario 6's (FEAS-SCN-9);
  10. behind on points, the main effort goes to the safe objective at 10:00 and moves to the decisive held one at 19:00, past the hysteresis (FEAS-SCN-10);
  11. ahead, the mid commander holds his threatened objective and the aggressive one attacks (FEAS-SCN-11);
  12. the high-value distant objective is the main effort over the low-value near one, which draws one screen (FEAS-SCN-12).

  Basis: § 6, computed. Measured: FEAS-SCN-1 to FEAS-SCN-12, FEAS-SCN-HOLD and FEAS-SCN-BOLD.
  - guardrail: temperament moves the term. With an audacity01 0.9 commander (`Bar` 0.88 at caution 0.5, agg 0.5) in the even-odds fixture, every dividing play's worst-body `detail` is below 1 (≈ 0.87-0.90, against the mid commander's 1), so its penalty is below the mid commander's, and none is filtered. On the held-crossing fixture, the ratio at which pin-and-cross reaches `detail` 0 is lower for him than for the mid commander (D14). Temperament can choose the risk; nothing forbids it.
  - read path: the harness only, with the same fields on `TacticalManeuverBody` rows in the game.
- **criterion-works-no-hopeless-assault** — Against held works, 0 attack tasks (`AttackObjective`, `SupportAttack`) issued to works-assaulting children while `WorksHold` is true (`attackTasksOnWorks` zero on every `TacticalWorksHold` row) (uncontrolled: a target, not a reading; WORKS-GUARD and smoke 11's cautious run settle it), and 0 focal-assault columns launched (BC11 Attack) whose works-bar `detail` at release was at least `RiskyPenalty`. The phase rows are not the measure: no shipped task reads the phase. Basis: requirement-works-hold and requirement-focal-assault: a hopeless assault is never ordered, whatever won the selection. Measured: `TacticalWorksHold` rows (`attackTasksOnWorks`, `guarded`), and `TacticalEnvelopment` `AssaultColumn` rows with their `detail`, over the smoke battles against entrenched enemies (smoke 10 and 11).
  - guardrail: the army does not freeze. In every such battle where a focal assault priced `detail` < `RiskyPenalty` was selected, its column is launched (at least one `AssaultColumn` Attack row), and where a turn was available (`turn-available` on the FocalAssault row) the selected plan's wing launches (a March row).
  - read path: `TacticalWorksHold`, `TacticalPlanSelection` (FocalAssault's reason), `TacticalEnvelopment` rows.
- **criterion-main-effort-value** — In smoke battles whose live objectives carry different points (smoke 13 and 14), in at least 80 % of the `TacticalMainEffort` decisions where the nearest objective (`nearestObjective`) is not the highest-valued feasible one (`maxFeasibleValueObjective`), the chosen objective is not the nearest. Today's director has no points to tell them apart, and vanilla's dynamic chains take the nearest. Basis: requirement-main-effort-objective's score, in which distance enters only through the daylight term, so the nearer objective wins only on value, risk, `U`, `H`, the prior or the director's kept terms; the 20 % allows for those. Measured: `TacticalMainEffort` rows.
  - guardrail: the main effort does not flicker: at most 3 changes of main-effort objective per side per battle day (the 0.1 hysteresis), counted on the same rows.
  - read path: `TacticalMainEffort` and `TacticalObjectivePressure` rows.
- **criterion-arrival-prediction** — For enveloping wings that reach Turn, the median of `|arrivedHours − predictedArrivalHours| / max(predictedMarchHours, 0.25)`, where `predictedArrivalHours` = `S* − DeployHours` recorded at release, is ≤ 0.30 over at least five wings in the smoke battles. Basis: vanilla's own speed and courier formulas (§ 2.1); terrain cost along a NavMesh polyline; the unmodelled part is slope (`1 − Δy/0.25`), overlap waits and column length. Measured: `TacticalEnvelopment` Turn rows.
  - guardrail: no wing's Turn arrives after its `TimeBudgetHours` with the release at its planned hour (0 `timeout` abandons on planned releases over the smoke battles), so the budget is never shorter than the prediction's error.
  - read path: `TacticalEnvelopment` Turn rows (`arrivedHours`, `predictedArrivalHours`, `predictedMarchHours`).
- **criterion-strike-spread** — For DoubleEnvelopment plans with both wings reaching Attack, the median gap between the two wings' Attack transitions is ≤ 0.25 doctrine-h. Today it is the difference of their march times. Basis: requirement-synchronized-release. Measured: the `doctrineHours` of the two wings' `TacticalEnvelopment` Attack rows per plan.
  - guardrail: a fix held for synchronization (requirement-synchronized-release) never waits past its planned release by more than 0.1 doctrine-h, and a fix in contact is never held. The fix's `Released` hour against its `PlannedReleaseHours` is reported on the same rows (start-delay rows of the fixing child).
  - read path: `TacticalEnvelopment` rows (`doctrineHours` on Attack rows; `doctrineHours` and `plannedReleaseHours` on the fix's `Released` rows).
- **criterion-replan-stability** — `ManeuverAssumptionBroken` replans are ≤ 3 per side per battle day, and never two within `Min Replan Seconds`. Basis: the cooldown and the margins of requirement-assumption-replan. Measured: `TacticalReplan` rows.
  - guardrail: every `crossing-held` assumption on a selected route either raises a replan or names its suppression reason (`cooldown`, `committed`, `margin`, `preempted`) on the row. A broken assumption is never silent.
  - read path: `TacticalReplan` and `TacticalManeuverBody` rows.
- **criterion-selection-regression** — On every existing playbook-selection harness fixture (no picture, no regions), `Select` with the flag on returns the same playbook as with it off. Basis: the term is 0 for `Unknown` and the filter follows the jitter draw. Measured: FEAS-REGRESS over the existing fixtures.
  - no-guardrail: an equality bound.
  - read path: the harness. In the game, the `TacticalPlanSelection` rows written before the river regions are ready (every verdict `Unknown`, every term 0) are the same check.
- **criterion-fog-honest** — 0 evaluations whose enemy inputs include an enemy that is not a sample under BC13's `requirement-enemy-samples` or the ledger estimate. 0 crossings Clear without a perceived probe within `ClearMemoryHours`. 0 reads of `scheduledarrival` or `EnemyRecords` beyond a Visible observation's row. 0 works segments, focal points or bars built from a regiment that is not a sample or from a works piece not yet perceived. Basis: the AGENTS fog rule. Measured: FEAS-FOG, WORKS-FOG and the source pin FEAS-PIN.
  - no-guardrail: a pure exclusion. Its cost (more `Unknown` crossings) is visible as `unknown` on `TacticalCrossingWatch`.
  - no-read-path: a runtime measurement needs an omniscient comparison that the evaluation may not make. The instrumentation package in this wave is the source pin plus the `unknown` count.
- **criterion-player-never-moved** — 0 tasks, releases, vias or look missions planned onto a `PlayerOwned` unit, and 0 player-owned strength counted in any body. Basis: the standing rule. Measured: FEAS-PLAYER and the executor's `PlayerOwned` skips for maneuver reasons.
  - no-guardrail: a zero bound.
  - read path: `[OrderArbiter]` `topSkip` and `TacticalManeuverBody` `strength` against the snapshot.
- **criterion-defender-no-offense** — 0 offensive playbooks selected for a Defender, the initial pick included. 0 Attack-task orders created by BC14 for a Defender (its outputs are a holding release and recon looks). Basis: the standing rule and requirement-stance-before-pick. Measured: FEAS-DEF and the `TacticalPlanSelection` rows' winner by stance.
  - no-guardrail: a zero bound.
  - read path: `TacticalPlanSelection` rows (the winner and the side's stance), and the `TacticalEnvelopment` rows of that side: BC14's only route to an Attack task is a BC11 maneuver, so a Defender side has 0 Attack rows (uncontrolled: a target, not a reading; smoke 8's Defender battle and FEAS-DEF settle it). Its releases are visible as `TacticalReserveRelease` `reason=crossing-threat` rows, a holding task.
- **criterion-frame-budget** — Per side: `tactical.feasibility.evaluate` p95 ≤ 2.0 ms and max ≤ 6.0 ms; `tactical.feasibility.monitor` p95 ≤ 0.5 ms; `tactical.feasibility.navmesh` p95 ≤ 2.0 ms. The region build steps stay within the picture's 4 ms deadline (`maxMs` ≤ 5.0). Measured in a battle with a river and about 500 regiments a side. Basis: requirement-frame-budget's counts and slices. A slice stops at 1.5 ms between legs and the longest leg is 300 samples, so a slice ends within about 0.5 ms of its deadline; the probes run inside it. The monitor is per heavy tick over cached legs, and the NavMesh query count is the BC11 precedent.
  - guardrail: the `tactical.patch.battle-core-arbiter` scope's mean per call does not rise by more than 5 % with the flag on against off. BC14 does no work in the arbiter tick.
  - read path: `PerformanceAggregate` rows of the four scopes. The p95 is within the threshold when `Σ slowCount / Σ count` ≤ 0.05; the max is the largest `maxMs`.
- **criterion-zero-alloc** — 0 bytes allocated per evaluation and per monitor step after warm-up. Basis: the Global Constraints. Measured: FEAS-ALLOC (`AssertNoAllocation`, the BC10 pattern).
  - no-guardrail: the criterion is itself a bound.
  - no-read-path: Mono exposes no per-call allocation counter in the game. The instrumentation package in this wave is the harness test, run in the gate list.

---

## 13. (E) Tests, plants, smoke, coverage matrix (frozen test plan)

Test file `ManeuverFeasibilityTests.cs` (`internal static partial class ManeuverFeasibilityTests { public static void Run() }`, partials allowed), with `<Compile Include>` lines for each new pure file. Red first: FEAS-SCN-3, FEAS-SCN-4, FEAS-SCN-6, FEAS-SCN-7, FEAS-SCN-12, FEAS-SCN-HOLD, FEAS-STANCE-INITIAL, FEAS-SYNC, INWARD-VIA-BC11, WORKS-GUARD, WORKS-HOLD-BC11, FORMATION-CARRY and every READER-* test must fail against today's code before any implementation. Where a test names a member that does not exist yet (`BattlePlanId.FocalAssault`, the two `PlayRectKind` members, `ManeuverInput.HoldForWorks`, the three-argument `TargetFormationForTask`, the pure seams of § 8), the member is added first with today's behaviour (the enum members with no reader edit, the field unread, the argument ignored, the two private methods made `internal`), so the test compiles and fails on its assertion.

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
  - MARCH-RULES-PIN: a source pin over **the BC14 file set** (requirement-runtime-wiring, every file by name) that no literal 1000, 0.02, 75, 2.33 or 2.2 appears (all from `MarchRules`). The test first runs its scanner over a planted line holding `* 1000f` and asserts it fires, so its zero is shown to detect.
  - COURIER-TIME: a first leg within 75 units → max(0.02, 0.02) = 0.02; a 2,563-unit first leg on grass → 0.02 + 5,126 / 5,126 = 1.02 h; the same on road → 0.52 h; a relay leg beyond bugle range → 0.02 + its ride; the longest branch counts; an engaged member → 0 for its own `T`; order delays off → 0.
  - STRIKE-OFFSET: MobileFlankMarch (offset 0.25 h) with `T_main` 0.6 and `T_wing` 1.0 → `C*` = max(0.6, 0.75) = 0.75, `S*_wing` = 1.0, releases: wing 0, fix 0.15. With `T_wing` 0.4 → `C*` = 0.6, `S*_wing` = 0.85, releases: wing 0.45, fix 0.
  - STRIKE-FIX-CONTACT: a fix in contact has `T_main` 0 and release 0, and the wing is released at once.
  - STRIKE-ABSOLUTE: `PlannedReleaseHours` = `EvaluatedHours` + R.
  - STRIKE-DOUBLE: DoubleEnvelopment wings at 1.2 h and 2.4 h, fix at 0.79 h → `C*` = `S*` = 2.4 for all; releases: the faster wing 1.2, the fix 1.61, the slower wing 0.
  - STRIKE-HELD: a march leg through a Held crossing adds 1.0 h to `T`.
  - INWARD-SURPLUS: an enveloping body whose inward leg crosses a river through a Clear ford 300 units off the straight line adds max(0, route hours − straight hours, the straight leg's river samples at swamp cost) to `T`; a ford on the straight line adds 0, never less; through a Held one, + 1.0 h more; a main-body strike leg through a Held crossing adds nothing to `T`.
  - STRIKE-LEG: a main-body member's strike leg through a Held crossing gives `contested` 0.5 (`StrikeLegWeight`), through a Watched one 0.25; with no path, `contested` 1 and reason `no-strike-crossing`, verdict not `Impossible`; a march leg through the same Held crossing gives 1.
  - DAYLIGHT: `S*` past day end − 0.25 → `after-dark`, `daylight` 1, verdict Risky (never Impossible).
- **S3 Enemy and detail.**
  - ENEMY-FOG-PREMIUM: confidence 0.8, caution 0.5 → × 1.1; caution 1.0 → × 1.15.
  - ENEMY-NO-SAMPLES: the assumed enemy point and `N/(bodies + 1)`.
  - SHIFT-PASSIVE: enemy intent Defend at confidence ≥ the floor → τ × 1.5.
  - SHIFT-RIVER: the enemy's shift crosses at its own Held crossing with no penalty.
  - SHIFT-COVERED: the enemy's shift through a crossing an own regiment covers (own-side `Covers`) costs +1.0 h; through a Watched, Clear or Unknown one, nothing; the same in the attacker's and the Defender's run.
  - SHIFT-COURIER: `EnemyCourierHours` is charged with the enemy in contact as without.
  - DETAIL-PIN: an engaged fixing body pins `S/(1.5 + TerrainStrength)`.
  - DETAIL-WINDOW: `shiftShare` 0 when τ ≥ W, 1 when W ≥ 2τ, linear between.
  - SUPPORT-TIME: `σ` is the least of the other bodies' courier plus march hours from where each stands when it has struck (an enveloping body's strike point; a fixing member's anchor) to the body's strike point, over the crossing graph, Held crossings at +1.0 h; no other body → +∞; a supporting body that is engaged keeps its courier hours in `σ` (0.32 h in the fixture, never 0).
  - ENEMY-BODIES: in the Defender run, BC14 builds its own bodies with `SingleLinkClusters` at 150 units: two clusters 400 units apart give two arrivals at a ford, and the earlier triggers; it works with BC13's opportunity flag on (a Defender's `OpeningDetector` runs no `ClusterPass`) and off alike; no samples → one body at the assumed enemy point.
  - CLUSTER-PARITY: `OpeningDetector`'s isolated-cluster openings are identical before and after `ClusterPass` calls the extracted `SingleLinkClusters.Label` (BC13's DET-CLUSTER stays green), on 50 random sample sets.
  - SAMPLES-PARITY: `EnemySamples.Accepts` agrees with each of BC13's three former inline filters on 200 random observations.
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
  - CANDIDATE-CENTRED: CavalryRaid and FocalAssault are evaluated once with `Mirrored` false; MobileFlankMarch, WideFlankApproach and ManeuverAndFix twice; a Defender's candidates are each evaluated once.
  - SELECT-SIDE: a single-flank play mirrored when its left side scores better; the zone assignment projects the template mirrored and gives the rect to the child the evaluation timed.
  - INWARD-LEG: a DoubleEnvelopment wing whose anchor stands on its own bank and whose line end lies across an open river with no link → `Impossible`, `river-on-leg`; with a Clear link → not `Impossible`, its attack via the link and its inward surplus in `T`; with a Held link → `contested` 1 and + 1.0 h in `T`.
  - INWARD-VIA-BC11 (red today): `EnvelopmentManeuver.InwardLegHitsRiver` with an attack via at a ford samples the inward leg as anchor → via → line end, and `PlayExecutorRuntime` feeds its result to `FlankMoveCheck`; river samples within 60 units of the via do not count, so the wing is not abandoned and its Attack order carries the via; a sub-leg that crosses open river elsewhere still abandons `river-on-leg`; without an attack via, today's straight check abandons as before. Red today: `ManeuverInput` has no attack via and the straight check abandons.
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
  - FEAS-SCN-2: the piecemeal fixture → every `detail` 0 with the synchronized release (`Evaluate`); `DetailRisk` called with every body's strike hours at its own `T` → the right wing's and the fix's `detail` are 1.
  - FEAS-SCN-3: 25,000 against 25,000, two corps → DoubleEnvelopment's wings `detail` 1 (`σ` 2.12 > `τ`), the single-fix plays' main body `detail` 1; every dividing play penalized ≥ 0.3; a single-body play selected for the mid commander (red today: a dividing play, ManeuverAndFix, has the best base score at odds 1.0).
  - FEAS-SCN-4: the § 6 scenario 4 fixture. At even odds → every dividing play's worst-body `detail` 1 (mirrored ManeuverAndFix wing `odds` 0.45, mirrored WideFlankApproach wing 0.37); FrontalShockAssault, MeetingEngagement, ForcedFrontalAttack and GenericDesperate penalty 0.645 ± 0.05 (Center march through B); GenericAggressive, ContinuousAttrition, HesitatedCommit and GenericMethodical penalty 0.379 ± 0.05 (strike legs through B at half weight); a bank-holding single-body play selected (red today: ManeuverAndFix is selected). At 2:1 → ManeuverAndFix selected with `Mirrored` true, march via = F, no attack via, wing `odds` 1.68 ± 0.05, candidate penalty 0.379 ± 0.05 and no candidate lower; DoubleEnvelopment and the authored ManeuverAndFix ≥ 0.6.
  - FEAS-SCN-HOLD: on the scenario 4 fixture at even odds, (1) with the flag off the selected play divides (ManeuverAndFix); (2) with it on a bank-holding single-body play is selected; (3) every candidate that marches across B has a feasibility term at least 0.1 below every bank-holding candidate's, and every dividing play's term is −0.4. The hold therefore does not rest on the base-score order among the single-body plays.
  - FEAS-SCN-5: the § 6 scenario 5 fixture → the projection's centroid (0, 125) with the deployed reserve's units in it and the reserve not assigned; DelayingDefense `turn` 0.04 ± 0.05 and PreparedDefense `turn` 0.72 ± 0.05; B (covered, lateral 0) counts for neither; a look request for F when Unknown; with DelayingDefense selected, the right corps (the ReserveLine child, `ownShift_c` 1.75 ± 0.05) is released to `RefuseRight` with its guard point at (1,000, 210) facing F; with the right column at (1,000, 1,500), the main body is the earliest body (2.61 h) and nothing is released; without the deployed reserve, PreparedDefense `turn` 0.03 ± 0.05 and, selected, no crossing release (no Reserve child).
  - FEAS-SCN-AUDACITY: the guardrail case of criterion-scenario-outcomes: at even odds, an audacity01 0.9 commander's dividing plays have worst-body `detail` < 1 (DoubleEnvelopment ≈ 0.87, the single-fix plays' Center ≈ 0.90) where the mid commander's are 1; none filtered.
  - FEAS-SCN-BOLD: on the scenario 4 fixture with the three corps scaled together against the fixed enemy, bisection to 0.01 on the own-to-enemy strength ratio finds where the mirrored ManeuverAndFix wing's `detail` reaches 0: 1.60 ± 0.05 for the mid commander and 1.50 ± 0.05 for audacity01 0.9; the bold ratio is strictly below the mid one, and the mid one is at most 2.0 (D14).
- **S7 BC11 and BC13 interaction.**
  - FEAS-SYNC: `PlannedReleaseHours` set → the Hold releases at it, not at contact + offset (red today: the field does not exist).
  - SYNC-UNSET: no ledger entry → BC11's rule unchanged (the BC11 tests stay green).
  - SYNC-PIN-AT-RELEASE: the march via, the attack via and the budget do not change after the March starts.
  - BUDGET: a predicted courier 0.1 h and march 1.9 h → `TimeBudgetHours` 3.05 (1.5 × 2.0 + 0.05); never below 1.0 with daylight to spare; with 0.6 h of daylight left → 0.6; latched at the March.
  - CEILING-LATCH: a planned release that recedes every tick still ends the Hold at `FirstPlannedReleaseHours` + 1.0; a fast wing waiting for a slow one (release 2.1 h after activation) is not launched by the ceiling before its release.
  - FIX-HOLD: a fixing child with a future release holds (FormUp at its position) through the start-delay path and is `Released` at its planned hour; a fix in contact, a deployed reserve or a child with no ledger entry is never held.
  - MIRROR-FALLBACK: a child on a mirrored projection can still mirror once (BC11's own try) to the authored side; after the flip the ledger's release, budget and both vias are not applied.
  - STATE-FLAGS: a `default(ManeuverState)` has no first planned release, no budget and no via (the `Has…` flags false), whatever the hour fields hold.
  - VIA-PASSED: once the child stands in the via's far region, the March order carries no via, so a re-issued order never walks it back.
  - VIA-ORDER: `PostureIntentPlan.RouteFlagsFor` gives a maneuver task with a via no road route and no `AvoidCoveredCrossings`; a March with a via → `BattleOrderIntent.ViaCount` 1 at the crossing; an Attack with an attack via → the same, its target still the line end; a main-body member's posture order carries no via.
  - VIA-ROUTE-CHECK: BC11's march-leg check runs as two legs through the via.
  - BC13-EXCLUDE: a child with a live exploit is not offered to the layout.
  - BC13-HALF: a main-body child with a live exploit counts half in `Pin` and `S`.
  - BC13-PERSONALITY: the evaluation reads the army entry of the personality table; with that flag off, the roster vector.
  - LOOK-PRIORITY: the selected route's Unknown crossing first; at most two; a look request expires when the crossing turns Clear.
  - LOOK-RECON: the recon producer takes a `Crossing` target after stale entry points and bands, within its cap.
  - RELEASE-CROSSING: one release per crossing, crossings ordered by the earliest `enemyAtCrossing`; the reason `crossing-threat`; never for a non-Defender; with no eligible Reserve child, no release; the child chosen is the eligible one with the least `ownShift_c` (ties to the lower index), read from the ledger's `OwnShiftByChild`, which equals courier plus march over the path search for that child (the functions `T` uses).
  - RELEASE-ORDER: with BC13's staggered flag on, and a crisis, a threatened wing in contact and a crossing threat in one allocation, the crisis takes the nearest reserve, then BC11's flank release, then the crossing release takes from what remains; the shipped BC11 and BC13 allocator tests stay green.
  - RELEASE-ELIGIBLE: a Reserve child that is a live exploit asset, or holds player-owned strength, is never released to a crossing; a BC10 deployed reserve is eligible.
  - RELEASE-ACROSS: when the earliest arrival at a crossing over all enemy bodies is a body already in the own bank's region, no new release is made toward that crossing and a guard already bound there stays bound; a body that crossed at a distant ford does not suppress the release at this one.
  - RELEASE-TARGET: the released child gets `RefuseRight` for a crossing on the right, task `GuardFlank`, the crossing's band as sector, and its guard point 100 units behind the crossing along the own front axis, facing the crossing; when that point is open river or the far bank, it snaps to the nearest land cell of the own-bank region; a crossing beyond the line's end is guarded there, not at the end band.
  - RELEASE-LATCH: a guard listed in `FeasibilityInput.BoundGuardChildIds` that reaches the crossing and covers it stays released while the enemy's arrival stays within `ownShiftAtRelease + 0.5`; it reverts once the arrival recedes beyond that or no body is perceived; across 40 allocation runs with a stationary enemy, release and revert never alternate; a plan change clears it.
  - RELEASE-FLOOR: `R0x` = the Reserve children before the crossing release plus the bound guards; with `R0x` 4 at floor share 0.30 two stay held; `R0x` 1 may go; a crossing release leaves BC13's `InitialReserves` and `LastReleaseHours` unchanged, so the next stagger release is not delayed; the floor applies with BC13's staggered flag off too.
  - RELEASE-STATE-SCOPE: `ReserveReleaseState.ForAlliance(a)` is one instance per alliance; `BattleScope.ResetAll` clears its crossing bindings; `ReplanForNewBattleDay` clears it even when no plan is picked; BC13's `ClearTrigger` leaves the bindings alone; a stand-down clears them.
  - DEF-FLANK: with the own line at lateral ±300, a crossing at lateral 0 or ±100 does not count; one at ±200 (the outer quarter) or ±700 does; one 2,000 units behind the line (beyond one frontage) does not.
  - DEF-PENALTY: a Defender candidate whose main body cannot strike before dark, and faces ground of `ApproachDifficulty` 1 at the enemy anchor, has penalty `1 − (1 − 0.6 × turn)` exactly: `daylight`, `detail`, `approach` and `exposure` 0.
  - DEF-ARRIVALS: the threat list carries `enemyAtCrossing` (the earliest body not in the own bank's region), `enemyAtFlank` = `enemyAtCrossing` + the legs to the flank end (the outermost filled non-Reserve rect; a tie to the smaller depth), the candidate's `ownShift` from its reserve sources (deployed reserves from their positions, Reserve and ReserveLine rects from their anchors; none → the nearest filled rect), and `OwnShiftByChild`; `turn` reads `enemyAtFlank`.
- **S8 Fog, player, performance.**
  - FEAS-FOG: a hidden enemy (not an observation) changes no crossing state, no `N`, no shift time.
  - FEAS-PIN: a source pin over **the BC14 file set** that `EnemyRecords` is read only through a Visible observation's row, and that `scheduledarrival` and `durationleft` never appear. The test first runs its scanner over a planted `scheduledarrival` read and asserts it fires.
  - FEAS-PLAYER: player-owned strength excluded from `S`; a wholly player-owned child not offered; no via or release on a player-owned unit.
  - FEAS-ALLOC: 0 bytes after warm-up for one full evaluation (22 evaluations, 6 bodies, a 64-link graph, a works picture of 32 segments and 256 focal points), one evaluation run in slices, one monitor step and one crossing release.
  - LINK-TABLE: the per-battle table holds every link pair of a region at the requirement-leg-cost sum; it is built across steps under a scripted clock and the map is unknown until it is complete; a 65th link to be routed is kept as a crossing, not routed, with the OnceLog.
  - LEG-CACHE: a second query from the same 50-unit bucket samples no new leg; a confirmed leg replaces the estimate; past `MaxLegCache` a leg is priced uncached and `legOverflow` counts it.
  - FEAS-SLICE: under a scripted clock an evaluation stops at the slice deadline between legs, resumes on the next call and publishes a ledger identical to the forced `Evaluate` result; no reader sees a half-published ledger.
  - FEAS-TIME: a 10-link river, 22 candidates, 8 children: the evaluation issues at most 638 path searches and samples at most 2,398 legs (`U × k` + same-region legs), counted, as asserted bounds; the slice times and the slice count are recorded (the harness machine is not the game's; the binding ms measurement is smoke 6).
  - NAV-SITES: `NavMeshQueriesSitAtTerrainHeight` counts 4 and pins the new site at terrain height.
  - PROJECT-PARITY / ASSIGN-PARITY: the pooled overloads equal `Project` / `Assign` on every template.

- **S9 Works.**
  - WORKS-SAMPLES: an enemy sample standing with `CoverObject` 7 and `CoverValue` 0.40 is a works sample; with `CoverValue` 0.15, or moving, or `CoverObject` 3 (forest), 5, 6, 1 or 12, it is not; a Remembered one at confidence 0.4 is not; a regiment that is not an observation contributes nothing.
  - WORKS-POINTS: a works piece counts only at `completion` ≥ 0.5 and after a probe found it perceived, and then for the rest of the battle (the stamp keyed by its `modelobj` instance id survives a re-read); it counts for the enemy with a perceived enemy works sample within 150, or, with none within 150 of it, when `InOwnZone` is false; `occupiedby` is never read; at most 16 works probes per side per tick, oldest first; a piece drawn after deployment appears at the next re-read of `BattleUI.obstaclelines`, a destroyed one is dropped there; past 2,048 pieces the farthest are dropped and counted.
  - WORKS-SEGMENTS: works 150 units apart laterally join one segment, 160 apart make two; a segment with no holder is not held.
  - WORKS-ENDS: an end that `FlankAnchored` reports anchored, or that another held segment continues within 150, is anchored; otherwise open.
  - WORKS-SALIENT: a profile point 250 nearer the own side than its ±300 chord is an apex; 100 nearer is not.
  - WORKS-JUNCTION: two enemy bodies with centroids on held works give a junction at their midpoint; with one of them off works, none.
  - FOCAL-SCORE: per-frontage scoring with the salient (0.5) and junction (0.8) factors and the tie rules; the scenario 7 junction (−300, 600) at ≈ 20.5 per unit ahead of (−500, 600) at 21.0; the scenario 8 apex at 3.5.
  - WORKS-BAR: a body whose strike point lies on held works is held to 3 × `Bar`; one striking 300 units beyond the open end, to `Bar`.
  - WORKS-PIN: a body striking on held works pins at a defence ratio of at least 3.0.
  - WORKS-SINGLE: a single-body plan whose strike point lies on held works gets a detail term with `W` = 1.0, `Pin` 0 and `own` 1; off works, none; a FocalAssault assault body's `W` is `AssaultHours` plus the strike spread, not step 2's support time.
  - TURN-FIX-OPEN: a fixing member of a turning plan facing held works keeps `Bar`, and its `E` counts only the enemy outside works (`N_open`).
  - WORKS-GUARD (red today): `PostureWorksGuard` with `WorksHold` true turns `AttackObjective` and `SupportAttack` into `FixEnemy` (reason `works-hold`, same target) for a node whose top-level child `AssaultsWorks`, in the leaf-cascade form and in the `Apply` form (state and doctrine order alike); `Probe` and holding tasks pass; a node in the `ManeuverNodeTable`, an exploit asset or a follower, and a child that does not assault works pass; with `WorksHold` false nothing changes; a single-body plan against works (scenario 7's cautious fall-back) issues no attack task to its works-assaulting children.
  - WORKS-HOLD-GATE: with `WorksHold` true the phase recommendation stays `Probe` past the probe budget and `Consolidate` does not resume into `MainEffort`; a plan already in `MainEffort` or `Exploit` is not stepped back; it lifts when the turn's wing reaches Attack, or when the assaulting body's works detail falls below `RiskyPenalty`; with the flag off `WorksHold` is false.
  - FOCAL-OFFER: with the flag off, or with no held works, FocalAssault fails its precondition and every other candidate's jitter is unchanged; with a feasible turn it is `Impossible`, `turn-available`; with willingness below 0.2, `Impossible`, `declined`; otherwise it carries the model's penalty and is never mirrored.
  - FOCAL-WILLINGNESS: willingness 0.85 for agg01 0.9, caution01 0.1, audacity01 0.9; 0.25 for the mid commander; −0.25 for agg01 0.2, caution01 0.9, audacity01 0.2.
  - FOCAL-EXEC: the `AssaultColumn` child runs Hold → March → Turn → Attack with `ManeuverInput.HasFocal` set, its March, Turn and Attack orders carry `HasFormation` and `Formation` = March Column, and its Attack targets the focal point latched at the March; its Turn ends at first contact, or when the group reads formation 4 or 0.1 h have passed, and not before the preparation floor; `BuildInput`'s inward target, corridor and river checks run on anchor → focal point; no `anchored-high-ground` abandon for it; the `AssaultSupport` child is released at the column's strike + 0.1 h; the demonstrations are released with the column; under `HoldForWorks` the support's start delay and the demonstrations hold, with no release; every write is a `BattleOrderIntent` through the executor (formation intent, waypoint).
  - FOCAL-PREP: with an own non-player battery, the children's doctrine orders carry `PreparationX/Z` from the plan's activation until the column's Attack (not while `HoldForWorks` holds it), and the column's planned release and its Turn → Attack are at least activation + 0.25 h; with no battery, no floor; a battery whose group order carries the point keeps bombarding through the doctrine's cancel and counter-battery rules until the Attack, and is stopped by its safety rules (ammo below 0.10, an unsupported enemy within 0.2 × fire range, friendly danger close); with `Enable Tactical Artillery Doctrine` off, no `BombardStart` is submitted.
  - FOCAL-PLAYER: a direct child that contains a player-owned unit (the W&L player's division inside a corps) carries `CorpsPosition.ContainsPlayerOwned` from `PlayAnchor.CollectOwn` (a subtree scan of the snapshot) and is never assigned `AssaultColumn` or `AssaultSupport` by `Assign` or `AssignInto`, in the evaluation and in the executor's zone assignment alike; it may hold a `Demonstration` or `Reserve` rect, its player-owned strength is not counted, and no intent, formation or preparation reaches a player-owned unit (the chain fan-out).
  - DEF-WORKS-STATION: a Defender holding works (its own built points and regiments in works cover) stations the fastest eligible Reserve child (by `TryStation`'s per-child vector) 150 units behind the focal point with the earliest enemy arrival within 1.0 h, role `Reserve`, task `ReserveWait`, facing it; the station moves only for an arrival 0.25 h earlier than the `StationBinding`'s `ArrivalAtChoice`; `ClearTrigger` leaves the binding; an exploiting or player-owned child is not stationed; the floor is not consulted; one child per side.
  - WORKS-FOG: a hidden entrenched regiment and an unperceived works piece change no segment, focal point, bar or gate.
  - FEAS-SCN-6: the § 6 scenario 6 fixture → the authored ManeuverAndFix wing off the works, `Pin` ≈ 7,700, wing `detail` 0.38 ± 0.05, fix `detail` 0 from the outside-works enemy; the mirrored side, DoubleEnvelopment and the single-body plays at `detail` 1 under the works bar; FocalAssault `turn-available`; the authored ManeuverAndFix selected; `WorksHold` true (`turn-not-struck`) until the wing's Attack (red today: no term sees the works, and the shipped scores pick by base).
  - FEAS-SCN-7: the § 6 scenario 7 fixture → focal point the junction (−300, 600); assault `odds` 2.34 ± 0.1; the aggressive commander's FocalAssault `detail` 0, selected, and its column launched; the cautious commander's FocalAssault `Impossible` `declined` and `WorksHold` `hopeless-assault` for whatever is selected; the mid commander, with `EnvelopmentPressure` on, selects ManeuverAndFix whose wings strike the works (`detail` 1) and, without it, FocalAssault (`detail` 0.88 ± 0.05); in both runs `WorksHold` is `hopeless-assault`, `HoldForWorks` is set, and the assaulting child never leaves Hold over 3.0 doctrine-h (past `MaxManeuverHours` and the latched ceiling), with no planned release published; in the cautious and mid runs the posture guard issues no attack task to a works-assaulting child.
  - FEAS-SCN-8: the § 6 scenario 8 fixture → the apex (0, 350) is the focal point (salient); assault `odds` 2.12 ± 0.1; the aggressive commander's FocalAssault `detail` 0 and selected.
  - FEAS-SCN-9: the § 6 scenario 9 fixture → before the drawn line's pieces are perceived, the works picture is two one-sample segments; after 16 probes per tick have perceived them, it spans the pieces' built extent and no further; the outcome is scenario 6's (the authored ManeuverAndFix selected); an unperceived piece changes nothing.
  - WORKS-LINES: works points come from every `ObstacleLine`'s `modelobjects` (an AI group's, a player-drawn one, a loaded one), each piece counting at its `modelobj` position when the object exists and its `completion` ≥ 0.5; a piece destroyed by `FinishImmediately` or later is absent; `BattlefieldSetup.fortifications`' `fortificationpoints` are never read.
  - WORKS-HOLD-BC11: with `WorksHold` `hopeless-assault`, `EnvelopmentManeuver.HoldForWorksFor` sets `ManeuverInput.HoldForWorks` on every maneuver of a child that `AssaultsWorks` and on the plan's start delays: the maneuver stays in Hold past `MaxManeuverHours` and past `FirstPlannedReleaseHours + MaxManeuverHours`, its `HasFirstPlannedRelease` and `HasBudget` clear, the start delay stays unreleased, no planned release is published; when the flag clears, `WorksClearHours` latches and the ceiling is `max(PlayActivatedHours, FirstPlannedReleaseHours, WorksClearHours) + MaxManeuverHours`, and a release latched before the hold does not launch the wing at the first tick; `turn-not-struck` sets no flag and the turning wing launches (red today: BC11 releases at its ceiling whatever the phase).
  - BOMBARD-START: a `BombardStart` intent for a battery within its fire range of the focal point → `ChangeCombatBehavior(go, 9)`, then `bombardposition` = the point and `bombardrange` = `GamePrefs.bombardmentmaxrangesize`, in that order, on the same regiment; not submitted (`TryBombardStartIntent` false) for a routed, marked-for-rout, gunless, permanently detached, counter-battery or bombarding, pathing battery, with `Σ ammo / 3` at or below 0.2, or with the point beyond its fire range; refused by the executor with Commander Mode not Active, with `TacticalWriteGuard.CanWrite` false, or for a player-owned battery; `lastfeudactiontime` is not written; `BattleWriteGateTests`' pattern matches `.bombardposition =` and `.bombardrange =` and finds them only in `OrderExecutorRuntime.StartBombard` behind its gate.
  - FORMATION-CARRY: `CommandFormationCorrection.TargetFormationForTask(task, current, orderFormation)` returns `orderFormation` when it is ≥ 0 and today's value otherwise; `ManeuverOrder.Formation` 4 on the column's March, Turn and Attack reaches `ManeuverNodeEntry.Formation` and `CommandDoctrineOrder.Formation` through `ExpandToNodes` and `CommandDoctrineAssignment.Build`; a `CommandDoctrineOrder` built without one reads −1; for the column's Attack order the function returns 4 (red today: `AttackObjective` gives line).
  - READER-STANCE (red today): `StanceOf(FocalAssault)` is Offensive, and a Defender's candidate set never contains FocalAssault, also with a focal point in its ledger.
  - READER-AFFINITY: FocalAssault's `DelayingAffinity` 0, `EnvelopmentAffinity` 0.10, `ResponseTargetFor` 0.6 (red today: 0.20, 0.20, 0.4).
  - READER-AXIS: `AllowsProvisionalAttackAxis` is true for FocalAssault (red today).
  - READER-MANEUVEROF: `ManeuverOf` gives an `AssaultColumn` zone a full spec of kind `AssaultColumn` and an `AssaultSupport` zone a start-delay spec with `HoldsForRelease` (red today: none for the support).
  - READER-ROLE: `RoleForRectKind` maps `AssaultColumn` to `Main` and `AssaultSupport` to `SupportMain` (red today: `Unknown`).
  - READER-MIRROR: `PlayExecutorRules.CanMirror(FocalAssault's template)` is false though it holds one enveloping rect, and `PlayExecutorRuntime` reads it (red today: the seam returns true, today's rule of one enveloping rect).
  - READER-FOCAL-ANCHOR: `PlayAnchor.PlayTarget(FocalAssault, hasFocal: true, …)` returns the focal point, the executor's `ComputeZoneAssignmentsCore` and the evaluation's layout both call it, and their rects are equal (red today: the seam returns the enemy anchor).
  - OBJ-VALUE: `Value_i` = 0.2 + 0.6 × `v_i` for an objective not held and 0.5625 × that for an own one, with `v_i` normalised over live objectives; all at 0 points → `v_i` 0.5; with the flag off, today's 0.45 / 0.80.
  - OBJ-ALLIANCEPOINTS: with per-alliance points in force (`continuousobjectivepoints` false), the side's own `alliancepoints` value is used; otherwise `points`.
  - OBJ-PRESSURE: `behind`, `ahead`, `late`, `U` and `H` by their formulas, the victory bar's deficit counting as well as the points share; `U` 0 with the day young; `H` 0 for agg01 1.
  - OBJ-FEASIBILITY: an objective behind a river with no crossing is infeasible for the main effort; one reached after dark has `daylight` 1 and stays feasible; one on held works uses the works bar.
  - OBJ-MAIN-EFFORT: the director takes the highest score among feasible objectives through `PickMainEffort`, not the nearest, with every `ObjectiveSelectionScore` term but `Value` kept; an entry without points keeps its shipped score times `(1 + U)` on its value; the plan's main-effort band is that objective's, and a later change moves the plan's sector through `SetObjectiveMainEffort` (refused in `Consolidate` and `Withdraw`) while the odds-based `ConsiderMainEffortShift` is not called; under the flag no `ParallelObjectives` operation opens; the risk weight is `FeasibilityWeight × (1 − 0.5 × U)` both there and in `Select`; the hold bias `HoldBiasWeight × H` reaches only Defensive and Neutral candidates.
  - OBJ-HYSTERESIS: the main-effort objective changes only for a rival scoring 0.1 more; the latch clears at a plan change, in `ReplanForNewBattleDay` and when its objective is deactivated.
  - OBJ-SCREEN: with two Screen-role children and an enemy-held secondary objective, `ScreenSecondaryObjectives` gives the laterally nearest one a screen target 150 short of it on the own side (at it when own-held); at most two objectives are screened, one child each; no Main, Fix, Support or Reserve child, no child with player-owned strength and no FocalAssault demonstration is drawn; the posture producer keeps its task `Screen` and moves it there; the next evaluation leaves it out of its body.
  - OBJ-THREAT: `Threatened` is true only with a perceived enemy sample within 250 of the objective; an enemy regiment within 250 that is not a sample does not threaten it.
  - OBJ-PENALTY: `r_i` uses the one hypothetical main body (non-Reserve, non-deployed-reserve, no player-owned strength), `own` 1 to take and `BaseDefenseRatio + TerrainStrength` to hold, `W` = `AssaultHours`, `Pin` 0, `contested` at `StrikeLegWeight`, and `ContestedWeight` 0.6.
  - HIST-PRIOR: a designer chain's next objective gets `HistoricalPriorWeight` 0.3 from its due hour (`objectivetimetomove[0] ≤ Hour + 24 × battlepasseddays`) and 0 before it; two chains naming one objective add it once; a chain switched to dynamic (`usedstrategyid` −1) gives no prior; a dynamic chain never does; a null `objectives[0]` (a timed wait) gives no prior; a chain's `Objectives` reference maps to the capture's `ObjectiveId`.
  - FEAS-SCN-10: the § 6 scenario 10 fixture → B at 10:00 as a first choice (A 0.42, B ≈ 0.46); at 19:00 with B current, A (≈ 0.94 against ≈ 0.70), past the 0.1 margin.
  - FEAS-SCN-11: the § 6 scenario 11 fixture, each run a first choice → the mid commander holds A (0.60 against 0.52); the aggressive commander attacks B (0.48 against 0.52).
  - FEAS-SCN-12: the § 6 scenario 12 fixture → the scorer gives the main effort to A (0.76 against 0.32) where vanilla's dynamic rule takes B; with an allocator fixture of one Main child and one Screen-role child (a Demonstration rect's), B, enemy-held, draws that child's screen target.

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
- the crossing-threat release past the floor (RELEASE-FLOOR);
- an allocation in the Dijkstra (FEAS-ALLOC);
- the fix's synchronized hold removed (STRIKE-DOUBLE, FIX-HOLD);
- the Hold ceiling anchored to the live planned release instead of the latched one (CEILING-LATCH);
- the zone assignment projecting the authored template for a mirrored plan (SELECT-SIDE);
- the inward leg left unscreened (INWARD-LEG);
- crossing cells counted as land in the labelling (REG-SPLIT);
- the enemy's perceived morale ignored (ODDS-MORALE);
- the main body's `own` ratio set to 1 (ODDS-MORALE, FEAS-SCN-4);
- open-river samples at the leg's own crossing priced as a bend (LEG-FOOTPRINT);
- `Bar` read as a constant 1.0 instead of from the personality (FEAS-SCN-AUDACITY, FEAS-SCN-BOLD, DETAIL-BAR);
- the support time's courier zeroed for an engaged supporting body (SUPPORT-TIME, FEAS-SCN-3);
- the guard counted in the crossing's cover test (RELEASE-LATCH);
- the crossing-threat release sent to the wing's end band instead of the crossing point (RELEASE-TARGET);
- a crossing release moving `LastReleaseHours` (RELEASE-FLOOR);
- the middle-half exclusion removed from the flank test (DEF-FLANK);
- a Defender's `daylight` or `approach` term left in its penalty (DEF-PENALTY);
- the release triggered from the mass instead of the earliest cluster (ENEMY-BODIES, DEF-ARRIVALS);
- CavalryRaid evaluated mirrored (CANDIDATE-CENTRED);
- the attack via dropped from the Attack order (INWARD-VIA-BC11, VIA-ORDER);
- BC11's sub-leg check counting the via's own footprint as river (INWARD-VIA-BC11);
- the inward leg screened as `Impossible` whenever it crosses a river (INWARD-LEG, FEAS-SCN-4);
- `StrikeLegWeight` set to 1 (STRIKE-LEG, FEAS-SCN-HOLD);
- the main-body strike leg left unscreened (STRIKE-LEG, FEAS-SCN-4, FEAS-SCN-HOLD);
- the inward surplus left out of `T` (INWARD-SURPLUS);
- the crossing release placed before the flank release (RELEASE-ORDER);
- an exploiting or player-owned reserve released to a crossing (RELEASE-ELIGIBLE);
- a body already across triggering a release (RELEASE-ACROSS);
- the crossing bindings cleared by BC13's `ClearTrigger`, or the new-day clear left to `Replan` (RELEASE-STATE-SCOPE);
- the guard point set on the crossing itself (RELEASE-TARGET);
- the deployed reserve left out of a Defender candidate's reserve sources (FEAS-SCN-5, DEF-ARRIVALS);
- the link table bypassed, each link leg sampled per query (LEG-CACHE, FEAS-TIME);
- the slice deadline ignored (FEAS-SLICE);
- a half-evaluated ledger published (FEAS-SLICE);
- `WorksOddsFactor` set to 1 (WORKS-BAR, FEAS-SCN-7);
- the open-end turn charged the works bar (WORKS-BAR, FEAS-SCN-6);
- a holding fix's `E` counting the enemy in works (TURN-FIX-OPEN, FEAS-SCN-6);
- the works hold gate removed (WORKS-HOLD-GATE, FEAS-SCN-7);
- FocalAssault offered while a turn is available (FOCAL-OFFER, FEAS-SCN-6);
- the willingness check removed (FOCAL-WILLINGNESS, FEAS-SCN-7);
- the salient factor set to 1 (FOCAL-SCORE, FEAS-SCN-8);
- the support released with the column (FOCAL-EXEC);
- the column abandoned on `anchored-high-ground` (FOCAL-EXEC);
- an unperceived works piece counted (WORKS-POINTS, WORKS-FOG);
- forest counted as works (WORKS-SAMPLES);
- the reserve station run through the floor as a release (DEF-WORKS-STATION);
- `HoldForWorks` dropped, so BC11 releases a hopeless works assault at its ceiling (WORKS-HOLD-BC11, FEAS-SCN-7);
- works points read from `fortificationpoints` instead of the `ObstacleLine` pieces (WORKS-LINES, FEAS-SCN-9);
- an unperceived drawn piece counted (FEAS-SCN-9, WORKS-FOG);
- `bombardrange` left unwritten, or the bombard start issued without the Commander Mode gate (BOMBARD-START);
- `TargetFormationForTask` ignoring the order's formation (FORMATION-CARRY);
- `StanceOf` left at its Neutral default for FocalAssault (READER-STANCE);
- `ManeuverOf` without the `AssaultSupport` branch (READER-MANEUVEROF, FOCAL-EXEC);
- the column given its mirrored try (READER-MIRROR);
- a corps containing the player's division assigned the column (FOCAL-PLAYER);
- objective value left at the fixed 0.45 / 0.80 under the flag (OBJ-VALUE, FEAS-SCN-12);
- urgency without the lateness factor (OBJ-PRESSURE, FEAS-SCN-10);
- the hold term without `1 − agg01` (OBJ-PRESSURE, FEAS-SCN-11);
- the nearest objective taken instead of the best score (OBJ-MAIN-EFFORT, FEAS-SCN-12);
- `ParallelObjectives` left open under the flag (OBJ-MAIN-EFFORT);
- the historical prior applied before its due hour (HIST-PRIOR);
- a literal `1000f` written into `FeasibilityRuntime.cs` (MARCH-RULES-PIN), and a `scheduledarrival` read written into `CrossingWatch.cs` (FEAS-PIN): the pins read the whole BC14 file set.
- the enemy's shift charged `HeldCrossingHours` at its own crossings, or not at own-covered ones (SHIFT-RIVER, SHIFT-COVERED).
- `PostureWorksGuard` applied only in the node path, not the leaf cascade (WORKS-GUARD, FEAS-SCN-7);
- the guard applied to a node in the `ManeuverNodeTable` or to an exploit follower (WORKS-GUARD);
- `HasFirstPlannedRelease` kept through a works hold, so the wing launches at the first tick after the clear (WORKS-HOLD-BC11);
- the support's start delay released while `HoldForWorks` holds the column (FOCAL-EXEC);
- the column's Turn ending at `TurnHoldHours` without the formation wait (FOCAL-EXEC);
- `BuildInput`'s inward target left at the enemy line end for the column (FOCAL-EXEC);
- the preparation floor dropped, or the preparing battery cancelled by the doctrine's Deny (FOCAL-PREP);
- `lastfeudactiontime` written by the bombard start (BOMBARD-START);
- `BattleUI.obstaclelines` not re-read, so a line drawn after deployment is never seen (WORKS-POINTS);
- the probe stamp keyed by piece index, so a re-read loses it (WORKS-POINTS);
- a works piece attributed by the zone test while an enemy works sample stands within 150 of it (WORKS-POINTS);
- `Threatened` from vanilla's omniscient distance test (OBJ-THREAT, FEAS-FOG);
- the main-effort hysteresis without its margin, or never cleared at a new battle day (OBJ-HYSTERESIS, FEAS-SCN-10);
- the odds-based `ConsiderMainEffortShift` still called under the flag with a main-effort objective (OBJ-MAIN-EFFORT);
- a Main or Fix child drawn to screen a secondary objective (OBJ-SCREEN, FEAS-SCN-12);
- `r_i`'s `own` left at 1 for holding an own objective (OBJ-PENALTY);
- a null chain entry given the prior (HIST-PRIOR).

### Smoke items (added to the BC7 checklist; item numbers assigned when the plan section is written, after 2.54 and 3.11)

1. `[RiverRegions] regions=… links=…` once per battle on a map with a river. `TacticalCrossingWatch` rows show the counts move from `unknown` to `clear` / `held` as BC9 and the look requests see the crossings.
2. `[Feasibility] first evaluation` and `TacticalPlanSelection` rows: candidates, filters, the top three with their feasibility terms. Before the river regions are ready every verdict is `Unknown` and every term 0 (criterion-selection-regression in game); a `map-known` replan follows once they are.
3. A river battle (the BC11 smoke save of item 2.28 or any save with a river across the front), run twice from the same pre-battle save, flag on and off: `TacticalEnvelopment` abandon reasons and `TacticalPlanSelection` winners (criterion-no-doomed-launch and its guardrail).
4. A flank march under MobileFlankMarch, WideFlankApproach or ManeuverAndFix: `TacticalEnvelopment` rows with `plannedReleaseHours`, `via`, `predictedArrivalHours` and `arrivedHours`. Watch the wing leave before the Center's contact when its march is long, and strike about `offset` after it (criterion-arrival-prediction).
5. A DoubleEnvelopment: the two Attack transitions (criterion-strike-spread).
6. Scopes `tactical.feasibility.evaluate|monitor|navmesh`, `tactical.river-regions.build` and `tactical.patch.battle-core-arbiter`, flag on and off (criterion-frame-budget).
7. `[Feasibility] first assumption replan` and `TacticalReplan` rows with `trigger=ManeuverAssumptionBroken` (criterion-replan-stability).
8. A Defender battle on a river map (the player attacks), from a save whose AI army has five or more direct children or selects DelayingDefense, so that a Reserve child exists: the AI never opens with an offensive playbook (`TacticalPlanSelection` winner at `trigger=initial`); `TacticalReserveRelease` rows with `reason=crossing-threat` when the player's column heads for a ford, the guard point at the ford, and one revert row when the column turns away (no release and revert pairs in between); BC9 `Crossing` looks (criterion-defender-no-offense).
9. Session B (W&L): a player whose division is a wing of a dividing plan receives the March by dispatch, and the plan's `TacticalManeuverBody` strength excludes the player's command (criterion-player-never-moved).
10. An entrenched enemy with an open end (a save whose enemy army holds campaign entrenchments above the trench tier, so vanilla entrenches it at deployment, or a battle map with fortification groups): `TacticalEnemyWorks` rows (segments, open and anchored ends, focal point), a turning plan selected, `TacticalWorksHold` `turn-not-struck` until the wing's Attack row, no `MainEffort` before it (criterion-works-no-hopeless-assault).
11. An anchored entrenched line, run twice from the same pre-battle save with the army commander's personality aggressive and then cautious (BC13's table): FocalAssault selected and its `AssaultColumn` launched after its preparation, the support 0.1 h behind; then FocalAssault `declined` and `TacticalWorksHold` `hopeless-assault` with no `MainEffort` (the criterion and its guardrail).
12. A Defender in works: `TacticalReserveRelease` `reason=works-station` rows, the station behind the focal point nearest the attacker's approach.
13. A battle whose objectives carry different points, run late into the day while behind (a save where the AI side holds no objective at 17:00 or later): `TacticalObjectivePressure` rows (`behind`, `late`, `U`) rising, and `TacticalMainEffort` moving to the higher-valued objective (criterion-main-effort-value).
14. A historical battle with designer chains (`Config/aistrategies.dat`, a battle whose AI groups carry `groupai` ids): `TacticalMainEffort` rows with `prior` > 0 only from each chain's due hour, and the vanilla-commanded chain groups moving at that hour as before.
15. The W&L player's corps in an AI assault on works: the player's command is never the column, and `[OrderArbiter]` shows no write to a player-owned unit (FOCAL-PLAYER).
16. A battle where the player entrenches at deployment against an AI Attacker (draws works lines): `TacticalEnemyWorks` rows with `worksPointsPerceived` rising as the AI's probes see the pieces and the segment extent never past the drawn line's last piece; no write to a player-owned unit (FEAS-SCN-9, WORKS-LINES).

### Coverage matrix (result column blank until prove)

| Requirement | Spec section | Test cases | Result | Defect / concession |
|---|---|---|---|---|
| requirement-candidate-set | § 4 | SELECT-SIDE, SIDE-RANK, CANDIDATE-CENTRED, FEAS-DEF | | |
| requirement-hypothetical-layout | § 4 | VERDICT-UNFILLED, DETAIL-NO-FIX, PROJECT-PARITY, ASSIGN-PARITY, BC13-EXCLUDE, FEAS-PLAYER | | |
| requirement-bodies | § 4 | FEAS-SCN-1, FEAS-SCN-3, TARGET-SNAP, ODDS-MORALE | | |
| requirement-river-regions | § 4 | REG-SPLIT, REG-CREEK, REG-FROZEN, REG-LINK-RADIUS, REG-STEP, REG-TRUNC | | |
| requirement-crossing-state | § 4 | XING-HELD, XING-WATCHED, XING-CLEAR, XING-REMEMBERED, XING-SHARED-RULE, XING-PROBE-CAP | | |
| requirement-route-screen | § 4 | ROUTE-SAME-REGION, ROUTE-VIA-CROSSING, ROUTE-NO-CROSSING, INWARD-LEG, VERDICT-UNKNOWN, STRIKE-LEG, INWARD-VIA-BC11 | | |
| requirement-leg-cost | § 4 | ROUTE-SAME-REGION, ROUTE-BEND, LEG-FOOTPRINT, MARCH-TIME | | |
| requirement-link-legs | § 4 | LINK-TABLE, LEG-CACHE, FEAS-TIME, ROUTE-VIA-CROSSING | | |
| requirement-navmesh-confirm | § 4 | ROUTE-CONFIRM, ROUTE-CONFIRM-BUDGET, NAV-SITES | | |
| requirement-march-time | § 4 | MARCH-TIME, MARCH-RULES-PIN | | |
| requirement-courier-time | § 4 | COURIER-TIME, SUPPORT-TIME, SHIFT-COURIER | | |
| requirement-strike-time | § 4 | STRIKE-OFFSET, STRIKE-DOUBLE, STRIKE-FIX-CONTACT, STRIKE-ABSOLUTE, STRIKE-HELD, FIX-MEMBER-RELEASE, MULTI-RIVER, INWARD-SURPLUS | | |
| requirement-daylight | § 4 | DAYLIGHT | | |
| requirement-enemy-picture | § 4 | ENEMY-FOG-PREMIUM, ENEMY-NO-SAMPLES, FEAS-FOG, FEAS-PIN, ENEMY-BODIES, CLUSTER-PARITY, SAMPLES-PARITY | | |
| requirement-shift-time | § 4 | SHIFT-PASSIVE, SHIFT-RIVER, SHIFT-COVERED, SHIFT-COURIER | | |
| requirement-detail-risk | § 4 | DETAIL-PIN, DETAIL-WINDOW, SUPPORT-TIME, DETAIL-BAR, DETAIL-PENALTY, DETAIL-NO-FIX, ODDS-MORALE, FEAS-SCN-2, FEAS-SCN-3, FEAS-SCN-BOLD | | |
| requirement-route-exposure | § 4 | EXPOSURE-FOREST | | |
| requirement-held-crossing | § 4 | ROUTE-HELD-DETOUR, STRIKE-HELD, FEAS-SCN-4, STRIKE-LEG, FEAS-SCN-HOLD | | |
| requirement-approach | § 4 | APPROACH-ANCHORED | | |
| requirement-player-share | § 4 | FEAS-PLAYER, FOCAL-PLAYER | | |
| requirement-penalty | § 4 | DETAIL-PENALTY, DEF-PENALTY, FEAS-SCN-3, FEAS-SCN-AUDACITY, FEAS-SCN-HOLD | | |
| requirement-verdict | § 4 | VERDICT-UNFILLED, VERDICT-UNKNOWN, VERDICT-RISKY, VERDICT-ORDER, DAYLIGHT | | |
| requirement-evaluation-cadence | § 4 | ROUTE-CONFIRM-BUDGET, smoke 2, FEAS-SLICE | | |
| requirement-stance-before-pick | § 5 | FEAS-STANCE-INITIAL, smoke 8 | | |
| requirement-selection-term | § 5 | SELECT-FILTER, SELECT-JITTER, FEAS-REGRESS | | |
| requirement-never-empty | § 5 | SELECT-NEVER-EMPTY | | |
| requirement-selected-side | § 5 | SELECT-SIDE, MIRROR-FALLBACK, FEAS-SCN-4 | | |
| requirement-assumption-monitor | § 5 | MON-HELD, MON-LATE, MON-DETAIL, MON-ABANDON, MON-COMMITTED, MON-MAP-KNOWN, MON-ESTIMATE, MON-ROUTE-MISSING | | |
| requirement-assumption-replan | § 5 | REPLAN-HYSTERESIS, REPLAN-COOLDOWN, MON-SUPPRESSION, smoke 7 | | |
| requirement-synchronized-release | § 7 | FEAS-SYNC, SYNC-UNSET, SYNC-PIN-AT-RELEASE, CEILING-LATCH, FIX-HOLD, STATE-FLAGS, FEAS-SCN-2 | | |
| requirement-maneuver-time-budget | § 7 | BUDGET | | |
| requirement-crossing-via | § 7 | VIA-ORDER, VIA-ROUTE-CHECK, VIA-PASSED, FEAS-SCN-4, INWARD-VIA-BC11, SYNC-PIN-AT-RELEASE | | |
| requirement-wing-abandon-replan | § 7 | MON-ABANDON | | |
| requirement-bc13-commitment | § 7 | BC13-EXCLUDE, BC13-HALF | | |
| requirement-bc13-personality | § 7 | BC13-PERSONALITY, DETAIL-BAR, FEAS-SCN-AUDACITY, FEAS-SCN-BOLD | | |
| requirement-crossing-look | § 7 | LOOK-PRIORITY, LOOK-RECON | | |
| requirement-defender-exposure | § 7 | FEAS-SCN-5, DEF-FLANK, DEF-PENALTY, DEF-ARRIVALS, SHIFT-COVERED | | |
| requirement-defender-crossing-release | § 7 | RELEASE-CROSSING, RELEASE-TARGET, RELEASE-LATCH, RELEASE-FLOOR, ENEMY-BODIES, DEF-ARRIVALS, FEAS-SCN-5, smoke 8, RELEASE-ORDER, RELEASE-ELIGIBLE, RELEASE-ACROSS, RELEASE-STATE-SCOPE | | |
| requirement-runtime-wiring | § 7 | FEAS-STANCE-INITIAL, MON-SUPPRESSION, FIX-HOLD, MIRROR-FALLBACK, RELEASE-CROSSING, RELEASE-TARGET, DEF-ARRIVALS, smoke 2, smoke 7 | | |
| requirement-frame-budget | § 11 | FEAS-TIME, smoke 6, FEAS-SLICE, LEG-CACHE | | |
| requirement-zero-alloc | § 11 | FEAS-ALLOC | | |
| requirement-config | § 11 | config-audit 0 findings | | |
| requirement-telemetry | § 11 | smoke 1-16 | | |
| requirement-works-picture | § 7A | WORKS-SAMPLES, WORKS-POINTS, WORKS-FOG, FEAS-SCN-6, WORKS-LINES, FEAS-SCN-9, smoke 16 | | |
| requirement-works-shape | § 7A | WORKS-SEGMENTS, WORKS-ENDS, WORKS-SALIENT, WORKS-JUNCTION, FOCAL-SCORE, FEAS-SCN-7, FEAS-SCN-8 | | |
| requirement-works-pricing | § 7A | WORKS-BAR, WORKS-PIN, WORKS-SINGLE, FEAS-SCN-6, FEAS-SCN-7 | | |
| requirement-turn-first | § 7A | TURN-FIX-OPEN, FEAS-SCN-6 | | |
| requirement-works-hold | § 7A | WORKS-GUARD, WORKS-HOLD-GATE, FEAS-SCN-6, FEAS-SCN-7, smoke 10, smoke 11, WORKS-HOLD-BC11 | | |
| requirement-focal-assault | § 7A | FOCAL-OFFER, FOCAL-WILLINGNESS, CANDIDATE-CENTRED, FEAS-SCN-7, FEAS-SCN-8, READER-STANCE, READER-AFFINITY, READER-AXIS, READER-MIRROR, READER-FOCAL-ANCHOR | | |
| requirement-focal-execution | § 7A | FOCAL-EXEC, FOCAL-PREP, FOCAL-PLAYER, smoke 11, BOMBARD-START, FORMATION-CARRY, READER-MANEUVEROF, READER-ROLE, smoke 15 | | |
| requirement-defender-works-reserve | § 7A | DEF-WORKS-STATION, smoke 12 | | |
| requirement-objective-value | § 7B | OBJ-VALUE, OBJ-ALLIANCEPOINTS, FEAS-SCN-12 | | |
| requirement-victory-pressure | § 7B | OBJ-PRESSURE, OBJ-THREAT, FEAS-SCN-10, FEAS-SCN-11, smoke 13 | | |
| requirement-objective-feasibility | § 7B | OBJ-FEASIBILITY, OBJ-PENALTY, FEAS-SCN-10 | | |
| requirement-main-effort-objective | § 7B | OBJ-MAIN-EFFORT, OBJ-HYSTERESIS, OBJ-SCREEN, FEAS-SCN-12, smoke 13 | | |
| requirement-historical-chain | § 7B | HIST-PRIOR, smoke 14 | | |

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
  - Rejected: a temperament-scaled delay roll per wing commander (D18).
- **D13 The stance is read before the pick (bug fix).**
  - Problem: the initial pick runs on the default `Meeting` stance.
  - Effect: a Defender opens defensively.
  - Rejected: leave it to the first replan. `PickInitialPlan` never stamps the replan cooldown, so when the refreshed inputs carry an opportunity other than `NoOpportunity`, the `OpportunityChange` trigger replans on the second coordinator tick. But when the doctrine stays at `NoOpportunity`, or its flag is off, nothing fires and the wrong opening stands until some other trigger does.

- **D14 Even odds across a held river (question-even-odds-crossing).** Decided by the user, 2026-10-16: "keep as designed".
  - Pin-and-cross is taken only with a real edge: about 2:1, or less for a bold commander, whose `Bar` is lower.
  - At even odds the army holds its own bank and probes.
  - Implemented by requirement-detail-risk: the support time `σ` counts the river. § 6 scenario 4 and FEAS-SCN-4 pin the outcome.
- **D15 Feasibility weight (question-feasibility-weight).** Decided by the user, 2026-10-16: `FeasibilityWeight` = 0.4, tuned in smoke (items 2 and 3). Implemented by requirement-selection-term.
- **D16 A wing that cannot strike before dark (question-after-dark).** The recommendation, accepted by the user (relayed by the coordinator, 2026-10-16).
  - Rated `Risky` with the full daylight penalty, never `Impossible`.
  - Implemented by requirement-daylight and test DAYLIGHT.
- **D17 The Defender's reserve released to a threatened crossing (question-defender-crossing-release).** Decided by the user, 2026-10-16: yes, under the main BC14 flag (`Enable Maneuver Feasibility`). Implemented by requirement-defender-crossing-release and requirement-config.
- **D18 Lateness caused by subordinate commanders (question-wing-commander-friction).** The recommendation, accepted by the user (relayed by the coordinator, 2026-10-16).
  - Not in BC14. The estimates stay deterministic.
  - Implemented by requirement-bc13-personality and § 18.
- **D19 Vanilla's AI reads the enemy reinforcement schedule through fog (question-reinforcement-leak).** Decided by the user, 2026-10-16: backlog only.
  - Recorded as a Backlog entry in `docs/bug-fixes/vanilla-tactical-battlefield.md`, with its decompile coordinates, in the same commit as this decision.
  - BC14 never reads the schedule (criterion-fog-honest, FEAS-PIN).

- **D20 Support and shift orders carry their courier hours.**
  - Problem: a supporting body has struck, so it is engaged, and vanilla's AI skips the order delay of an engaged unit; zeroing its courier would shorten `σ` while the enemy's `τ` keeps its own.
  - Effect: `σ` and `τ` are priced alike, and § 6's even-odds verdict stands (wings `detail` 1).
  - Rejected: zero `σ`'s courier for an engaged body. With `τ` charged, that biases the evaluation toward dividing (scenario 3's wing `detail` falls to 0.73).
- **D21 The crossing guard latches, and is not its own cover.**
  - Problem: the guard covers the crossing on arrival, which ends the threat, which reverts the guard, which uncovers the crossing.
  - Effect: a reserve sent to a ford stays there while the enemy's approach holds, and returns when it recedes.
  - Rejected: a fixed minimum hold time. It ends a real guard on a clock rather than on the threat.
- **D22 A flank crossing lies in the outer quarter of the line or beyond, between one frontage behind and the enemy.**
  - Problem: a band of one frontage beyond each end of the line would reach across most of the line's own laterals, the frontal crossings included, and nothing would bound the depth.
  - Effect: the turning term and the release answer crossings that can turn the line, not the ones it faces.
  - Rejected: "outside the line's extent only". A ford just inside the end, where the line is thinnest, would not count.
- **D23 A Defender's penalty is its turning term alone.**
  - Problem: the attacker's daylight, detail and approach terms would rate a Defensive play by an attack it never makes.
  - Effect: Defensive and Neutral candidates differ only by how well each guards its flank crossings.
  - Rejected: keep the attacker's terms for a Defender's main body. That penalizes holding a line in front of a strong enemy, the line's purpose.

- **D24 A Defender's enemy bodies are BC14's own clustering, over the shared labelling.**
  - Problem: the release needs the enemy's separate bodies, and BC13's detector runs no `ClusterPass` for a Defender; its clusters live in private per-tick arrays that surface only as isolated-cluster openings.
  - Effect: a column heading for a ford is timed as itself, with BC13's opportunity flag on or off.
  - Chosen: BC14 labels its own samples with `SingleLinkClusters.Label`, extracted from `ClusterPass` so there is one labelling rule, at BC13's 150-unit link.
  - Rejected: a public read-only interface on BC13's detector. It would have to run `ClusterPass` for a Defender, which changes BC13's opening set and its cost per tick, and it would tie BC14's cadence to the detector's.
- **D25 The inward leg gets its own crossing via, and BC11 judges the two sub-legs.**
  - Problem: BC11 abandons a wing whose straight inward leg crosses open river, so a wing whose attack must cross is abandoned after it was selected.
  - Effect: a wing attacks over the crossing the plan chose, and pin-and-cross (D14) can launch wherever its ford lies relative to the anchor.
  - Rejected: rate every inward leg that crosses a river `Impossible`. That would kill the pin-and-cross the user approved whenever the ford lies on the attack leg.
  - Rejected: leave BC11's straight check unchanged. The screen would select plans BC11 then abandons (criterion-no-doomed-launch).
- **D26 Every strike leg is priced; a main body's at half weight.**
  - Problem: a single-body attack's strike leg was never screened, so "never dives at the held bridge" depended on where a river fell against the rect fractions.
  - Effect: a frontal attack across a held river is priced by the model. Crossing at once costs more than holding the bank and attacking later, so at even odds the army holds and probes by the model, not by the base scores' order (§ 6 scenario 4, FEAS-SCN-HOLD).
  - Rejected: the strike leg at full weight. Holding the bank and crossing at once would cost the same, and the choice between them would fall back to the base scores.
  - Rejected: a strike leg that is `Impossible` without a crossing. An army can still hold its bank against an enemy it cannot reach.
- **D27 The crossing release is appended after the flank release and draws on the real reserve.**
  - Problem: the release must fit the shipped order and must send a unit the army has.
  - Effect: contact releases come first, a predicted threat takes what is left, the floor holds, and the guard is a real Reserve child timed by the same functions as every other body.
  - Rejected: release from the template's reserve rect. No shipped template puts its reserve anywhere but lateral 0, and most three-corps Defender layouts leave it empty.
  - Rejected: fold it into BC13's stagger. The stagger's `R0` and interval would move with predicted threats, and BC13's tests would change.
- **D28 Leg costs are tabled per battle, cached per evaluation, and the evaluation runs in slices.**
  - Problem: every candidate's routes, supports and shifts sample the same legs again; priced on demand, an evaluation samples far past its budget.
  - Effect: link-to-link legs are sampled once per battle, point legs once per evaluation, and no tick spends more than its slice.
  - Rejected: evaluate one candidate per tick. A full evaluation would lag the battle by 21 ticks whatever its cost.
- **D29 Works are priced by the same model: a bar of three at the point of attack (integrator scope addition, 2026-10-16).**
  - Problem: the user asked how the AI meets an entrenched enemy; vanilla only routes its marches around the fire zone in front of covered enemies.
  - Effect: an assault on works needs about three to one where it strikes; a turn beyond an open end pays nothing extra; the choice between them emerges from the evaluation.
  - Rejected: a fixed penalty per entrenched candidate. It would not see where a plan strikes, so it would punish a turn as much as a frontal assault.
  - Rejected: an `Impossible` filter for works assaults. Grant and Hood did assault works; temperament must be able to choose it (D3).
- **D30 A turning plan's fix holds in front of the works, and one works-hold predicate keeps the tasks, BC11 and the phase from committing against them.**
  - Problem: a `Main` child's task is an attack whatever the phase, the director commits on its own odds rule, BC11 launches a maneuver at its release or its Hold ceiling, and a fix facing works would assault them.
  - Effect: "a brigade or division in front, the main body around the edges"; a hopeless works assault is never ordered or launched, whatever won the selection (`WorksHold` read by `PostureWorksGuard`, by BC11 as `HoldForWorks`, and by the phase machine).
  - Chosen: a task guard beside the shipped stance guard, at the same two places; it is where every attack task passes (dry run 7 found the phase read by no task).
  - Rejected: re-scoring the plan every tick to force a replan. The monitor already replans on broken assumptions; one predicate is the one rule that holds the assault.
  - Rejected: a gate on the phase machine alone. No task reads the phase and BC11's maneuver launch does not either, so the assault would go in anyway (crit r2 M1; dry run 7).
  - Rejected: a gate on the director's `Committed` transition. The leaf cascade gives `Main` an attack without the director.
- **D31 FocalAssault is a playbook, offered only when no turn will do, and willingness is the commander's.**
  - Problem: a deliberate assault on one point needs its own layout (column, support, demonstrations) and must not crowd out a feasible turn.
  - Effect: the catalog weighs it like any play, the model prices it, BC11 executes it, and a cautious commander declines it.
  - Rejected: a mode switch inside the existing frontal plays. Their rects are laid out on the enemy anchor, not on a chosen point, and they have no column or echeloned support.
  - Rejected: always offering it. It would compete with a feasible turn on base score alone.
- **D32 The focal point is the lowest defence per frontage, with salients and seams discounted.**
  - Problem: "a salient, the weakest held stretch or a junction" must become one choice.
  - Effect: one score ranks every point: a salient's defenders are hit from three sides (0.5), a seam between commands is weaker than its numbers (0.8).
  - Rejected: a fixed priority (salient first, then junction, then weakest). A strongly held salient would be assaulted ahead of a thin stretch.
- **D33 A Defender in works stations a reserve behind its most threatened point, without releasing it.**
  - Problem: works hold a line cheaply, and their weak point is where a reserve must stand.
  - Effect: the reserve is where it is needed and remains a reserve for every release.
  - Rejected: a release to the focal point (the crossing release's form). It would spend the floor before any assault comes.
- **D34 The focal assault's artillery preparation is a new executor write path, the bombard start.**
  - Problem: a column sent in without preparation meets works at full strength, and Whiskey has never started a bombardment (its doctrine only cancels or redirects one, its battery writes are 7 and 8). Vanilla's start is inline in `CheckAIBombardment` (the writes at 3897-3900), not a callable method.
  - Effect: batteries in range open on the focal point from the plan's activation, as vanilla's own bombardment, through the one guarded write path; the column goes no earlier than `PreparationHours` after activation when the side has artillery.
  - Chosen: a `BombardStart` intent the executor carries out as vanilla does: `ChangeCombatBehavior(go, 9)` through `TacticalCombatBehaviorWriter`, then `bombardposition` and `bombardrange` in `OrderExecutorRuntime.StartBombard`, gated by `TacticalWriteGuard.CanWrite` and Commander Mode Active, on the BC7 grep list and in `BattleWriteGateTests`. It is stated as a new writer (§ 15 item 8).
  - Rejected: requesting support through the doctrine order and waiting for vanilla's start. Vanilla starts only when its group's path is interrupted while non-artillery subordinates move and fewer than half are engaged (3880-3887), and on the closest enemy, not the focal point; the preparation would not reliably happen, or would hit the wrong place.
  - Rejected: a Prefix or Transpiler on `CheckAIBombardment`. Forbidden without the user's consent, and vanilla's test would still choose the target.
- **D35 An objective's value is vanilla's own points, normalised across the live objectives.**
  - Problem: Whiskey values every objective 0.45 or 0.80 whatever the scenario designer weighted, and vanilla's dynamic chains take the nearest.
  - Effect: an objective worth more on the victory bar is worth more to the army; the range stays today's (the top one keeps 0.80, an own one keeps the 0.45 / 0.80 ratio), so the director's other terms keep their weight.
  - Rejected: raw points. Maps use different scales; the director's terms would swing with the map.
  - Rejected: keeping the fixed values and adding points as a separate term. Two value terms would double-count the same thing.
- **D36 The victory clock moves urgency and risk, scaled by the commander's aggression.**
  - Problem: an army behind on points at dusk and one ahead at dawn choose alike today.
  - Effect: behind with the day running out, the decisive objective wins over the safe one and more risk is accepted; ahead, the army holds unless its commander is aggressive. History sets the weights (Gettysburg, July 2; Shiloh, Beauregard's halt).
  - Rejected: a scripted switch at a fixed hour. It ignores how far behind the side is and who commands it.
  - Rejected: reading the enemy's reinforcement schedule for the clock. A fog leak (§ 2.1 item 10).
- **D37 One main effort per army, on the best feasible objective; secondaries get a screen.**
  - Problem: the director can open parallel objectives and the main effort follows the front axis's default band, so an army splits its weight or strikes the nearest point.
  - Effect: Clausewitz's and Jomini's concentration (§ 3): the plan's main-effort band is the chosen objective's, and other objectives cost at most one screening child each.
  - Rejected: keeping `ParallelObjectives` under the flag. It divides the army before the feasibility model can price the division.
  - Rejected: ignoring secondary objectives. An unwatched enemy-held objective can be reinforced unseen and an own one lost to a raid.
- **D38 A designer chain is a historical prior from its due hour, not an override.**
  - Problem: scenario designers wrote historical timetables into `aistrategies.dat`, and a Whiskey-commanded group never follows vanilla's timed moves.
  - Effect: the chain's next objective is favoured from the hour vanilla would have moved (+0.3, half the value range), so the historical plan usually wins and a clearly better or infeasible alternative still decides.
  - Rejected: an override. It would march into a river with no crossing or a held line because the script said so (D3: history is a weight).
  - Rejected: ignoring the chains. It throws away the designer's history.
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
   - **The attack via and the sub-leg river check (D25).** `ManeuverInput` gains `HasAttackVia`/`AttackViaX`/`AttackViaZ`, latched into `ManeuverState` at the March with the march via; the Attack order (`ManeuverOrder.HasVia`/`ViaX`/`ViaZ`) carries it. `PlayExecutorRuntime` (the inward-leg sampling today at its `SampleLine(anchor → line end, InwardStepMeters)` call) calls a new pure `EnvelopmentManeuver.InwardLegHitsRiver` (the `RouteHitsRiver` precedent), which samples anchor → attack via and attack via → line end and drops river samples within `RiverRegions.CrossingLinkMeters` of the via before `InwardLegClear`; `FlankMoveCheck` then abandons `river-on-leg` only when a sub-leg crosses open river. Without an attack via the straight check is unchanged. Test INWARD-VIA-BC11 is red against today's code.
5. **BC9 code touch point.** `BattleRecon` gains the `Crossing` target kind, and its 150-unit stand-off (today applied only to EntryPoint and RememberedEnemy) applies to it, on the own side of the crossing.
6. **BC13 dependency and touch points** (BC13 is on `main` at `578bd13`; its allocator, detector and opportunity tests stay green):
   - BC14 reads `CommanderPersonalityTable`, `IPerceivedGround` (through `PerceivedGroundRuntime`, opening and disposing its own handle), `ExploitTable.ForAlliance(a).IsExploiting(int)` (behind BC14's new `IExploitLookup` seam), `OpportunityPersonality` and `OpportunityDoctrine.FloorShare`.
   - **Extractions (one rule each):** BC13's enemy-sample filter, written inline in `OpeningDetector.BuildSamples`, `OpportunityTick` and `CavalryStrikeDisciplinePatch`, becomes `EnemySamples.Accepts`, which the three call; `OpeningDetector.ClusterPass`'s union-find labelling becomes `SingleLinkClusters.Label`, which `ClusterPass` calls (CLUSTER-PARITY, SAMPLES-PARITY).
   - **The allocator:** `ReleaseReserves` gains `ReleaseReserveToCrossings` after `ReleaseReserveToThreatenedWing`; `ReserveReleaseContext` gains `CrossingThreats`, `PlayerOwned` and an explicit `Staggered` flag (so `State` and `Exploiting` are passed whenever BC13's staggered flag or BC14's flag is on); `DirectChildIntent` gains the guard point and heading; the posture producer reads them for `GuardFlank`; `ArmyOrchestrator`'s release row emits `reason=crossing-threat`.
   - **The state:** `ReserveReleaseState` becomes `ReserveReleaseState.ForAlliance(int)` with a `BattleScope` registration and gains the crossing-binding table and the station binding; `BuildReserveReleaseContext` uses BC13's full constructor (state, exploit flags, personality) whenever BC14's flag is on; `ArmyOrchestrator` holds its alliance's instance; `TacticalBattleCoordinator.ReplanForNewBattleDay` clears it before its `HasPlan` check. These change BC13's shipped shape, so BC13's reserve and allocator tests are re-run unchanged and must stay green: `CommanderOpportunityTests.Reserve.cs` (ResStagger, ResFloor, ResSingle, ResCrisisOne, ResCrisisLatency, ResCrisisHeld, ResPersist, ResRevert, ResExploitingSkipped, ResTie, ResOff, ResOutcomes) and the BC11 flank-release tests.
7. **BC10 and play-layer touch points.** `RoadRoutePlanner.PolylineCost` becomes `internal` (private today) for the confirmation's polyline cost.
   - **Works (§ 7A): every shipped reader of the widened enums, and its edit.** `BattlePlanId` gains `FocalAssault`; `PlayRectKind` gains `AssaultColumn` and `AssaultSupport`. No persistence and no enum-sized array reads either. Each row has a test that fails on today's code (READER-*):

     | Reader | Today's default for a new member | Edit | Test |
     |---|---|---|---|
     | `TacticalPlaybookCatalog.StanceOf` | Neutral (its `default`), so `StanceAllowsPlaybook` would offer FocalAssault to a Defender | `FocalAssault` → Offensive | READER-STANCE: a Defender's candidates never include FocalAssault |
     | `TacticalPlaybookCatalog.DelayingAffinity` | 0.20 | `FocalAssault` → 0 | READER-AFFINITY |
     | `TacticalPlaybookCatalog.EnvelopmentAffinity` | 0.20 | `FocalAssault` → 0.10 (a frontal assault, not a turn) | READER-AFFINITY |
     | `TacticalPlaybookCatalog.ResponseTargetFor` (private; made `internal` for the test) | 0.4 | `FocalAssault` → 0.6 (offensive) | READER-AFFINITY |
     | `DirectChildAllocator.AllowsProvisionalAttackAxis` (private; made `internal`) | false | `FocalAssault` → true (the frontal plays' rule) | READER-AXIS |
     | `DirectChildAllocator.ManeuverOf` | no spec for `AssaultSupport`; `AssaultColumn` only through `Envelops` | `AssaultColumn` → a full spec of kind `AssaultColumn`; `AssaultSupport` → a start-delay spec with `HoldsForRelease` | READER-MANEUVEROF |
     | `PlayCorpsAssignment.RoleForRectKind` | `Unknown` | `AssaultColumn` → `Main`; `AssaultSupport` → `SupportMain` | READER-ROLE |
     | `PlayCorpsAssignment.AssignInto` / `Assign` | any child may take any rect | skip a child whose `CorpsPosition.ContainsPlayerOwned` (new, filled by `PlayAnchor.CollectOwn`) for `AssaultColumn` and `AssaultSupport` (requirement-focal-execution) | FOCAL-PLAYER |
     | `PlayExecutorRuntime` `canMirror = envelopRects == 1` | true for FocalAssault's lone enveloping rect | read from the pure `PlayExecutorRules.CanMirror(template)`, false when the rect is an `AssaultColumn` | READER-MIRROR |
     | `PlayExecutorRuntime.ComputeZoneAssignmentsCore`'s `PlayAnchor.EnemyAnchor` | the enemy anchor | through the pure `PlayAnchor.PlayTarget`: for FocalAssault, the ledger's focal point (the ReverseSlopeDefense crest branch is the precedent) | READER-FOCAL-ANCHOR |
     | tests `FlankResponseTests.Maneuver.cs` `EnvelopsSetOnExactlySixRects` | red with a seventh enveloping rect | expect `FocalAssault:AssaultColumn` too (seven) | itself |
     | tests `TestFixtures.SeedCatalog.AllHistoricalAndGeneric` and `TacticalPlaybookTests.SeedCatalogRegistersAllEighteenPlaybooks` | 18 | register `FocalAssaultPlaybook`; expect 19 | itself |

   - **Works, the rest:** `FocalAssaultPlaybook` (appended last to `BuiltInPlaybooks`, so the jitter stream of the 18 is unchanged) and its `PlayTemplateCatalog` entry; BC11's `ManeuverSpec` kind `AssaultColumn` (`ManeuverInput`/`ManeuverState` `HasFocal`/`FocalX`/`FocalZ`, `LineEnd` returning the focal point, `ManeuverOrder.Formation` on the Turn and the Attack, no `anchored-high-ground`); **`ManeuverInput.HoldForWorks`** (requirement-works-hold: `EnvelopmentManeuver.Step` keeps Hold, suppresses its ceiling and clears the latched release and budget while it is set, and latches `ManeuverState.WorksClearHours` when it clears; `StepStartDelay` holds a start delay under it; `PlayExecutorRuntime.StepManeuvers` sets it through `EnvelopmentManeuver.HoldForWorksFor` from `WorksHold` and `AssaultsWorks` and publishes no planned release); **`PostureWorksGuard`** beside `PostureStanceGuard` in `CommandPostureExecutor.cs`, called at the stance guard's two `PostureIntentProducer` sites; for the column, `ManeuverInput.GroupFormation`, the Turn's formation wait (`RoadRoutePlanner.ColumnWaitHours`) and preparation floor, and `BuildInput`'s focal inward target; `CorpsPosition.ContainsPlayerOwned`; **the formation path** (`ManeuverOrder.HasFormation`/`Formation`, `ManeuverNodeEntry.Formation`, `CommandDoctrineOrder.Formation` (−1 by the constructors' default argument), and `CommandFormationCorrection.TargetFormationForTask(task, current, orderFormation)` returning the order's formation when set, read at its two `PostureIntentProducer` call sites); `PostureIntentPlan.RouteFlagsFor` and `CoveredCrossings.Covers(in UnitRecord, …)` are new methods (added; `PostureIntentPlan` and the private `AddCoveredCrossings` are what exist today); `CommandDoctrineOrder.PreparationX/Z`; the artillery doctrine producer's `BombardStart` intent (item 8), `BattleArtilleryState.HasPreparation`/`PreparationX`/`PreparationZ`/`AmmoMean`, and the preparation exemption in `ArtilleryDoctrineIntents`; the works re-read of `BattleUI.obstaclelines` and `IFortificationPoints.InOwnZone`; `TacticalPhaseProgressionDoctrine.Input.WorksHold`; `DirectChildAllocator.StationReserveBehindFocalPoint`. The level-of-detail slice (now BC15) promotes a group whose order gains a formation (trigger (c), order target and formation). The formations slice (now BC16) records `assault-column` (March Column) as the column's formation reason.
   - **Objectives (§ 7B):** `MapObjectiveCapture` gains the objective's points (`points`, or `alliancepoints[own alliance]` per requirement-objective-value) and `TacticalVisionRuntimeAdapter` sets `ObjectiveObservationInput.value` from requirement-objective-value under the flag (0.45 / 0.80 with it off); `TacticalOperationDirector.TryPickBestObjective` calls `ObjectiveScoring.PickMainEffort` with every `ObjectiveSelectionScore` term but `Value` as the director's terms, and does not open `ParallelObjectives` under the flag; `ArmyEvidenceBuilder` gives `PlaybookContext.DefaultMainEffortSector` the director's chosen objective's band; `ArmyTickCycle` calls the new `ArmyOrchestrator.SetObjectiveMainEffort` on a change and skips `ConsiderMainEffortShift` under the flag with a main-effort objective; `ObjectiveLatch.ForAlliance(a)` (`BattleScope`), cleared by `Replan` and `TacticalBattleCoordinator.ReplanForNewBattleDay`; `DirectChildAllocator.ScreenSecondaryObjectives` and `DirectChildIntent`'s screen target, read by the posture producer; `TacticalPlaybookCatalog.Select` reads `U` and `H` (the risk scale and the hold bias); a runtime reader (`ObjectiveRuntime`) reads `BattleUnits.sideinformation[].objectivepointsheld`, `GetVictoryPointsRelation` and the side's `AIBattle.objectivechain` (public fields), once per evaluation.
8. **`AGENTS.md` and the write owners.** BC14 adds **one new executor write path**, honestly a new writer: the bombard start (D34). The artillery doctrine producer submits a `BombardStart` intent; the executor, through `OrderExecutorRuntime.StartBombard`, calls `TacticalCombatBehaviorWriter`'s `ChangeCombatBehavior(go, 9)` and writes the regiment's `bombardposition` and `bombardrange` exactly as vanilla's `CheckAIBombardment` does (3897-3900, without the feud timer), gated by `TacticalWriteGuard.CanWrite` and Commander Mode Active; no Prefix, no Transpiler. The Battle write owners paragraph and the BC7 grep gate list gain it, and `BattleWriteGateTests`' pattern gains `\.bombardposition\s*=` and `\.bombardrange\s*=` (today it matches only method calls and `.macroai =`). Everything else BC14 does goes through existing writers. The quick reference "Battle stance + plays" gains: plays are screened for feasibility before selection (`ManeuverFeasibility`), and a Defender's initial pick is stance-filtered; against perceived enemy works the AI turns an open end first and assaults a focal point only when no turn will do (`FocalAssault`, the 19th playbook, Offensive); the main effort goes to the most valuable feasible objective by vanilla's points.
9. **Patch catalog and bug ledger.** No new patch, so no ordinal. requirement-stance-before-pick is a Whiskey defect, not a vanilla bug, and it is recorded in the BC14 section's As built. Vanilla's reinforcement-schedule fog leak (§ 2.1 item 10) is a Backlog entry in `docs/bug-fixes/vanilla-tactical-battlefield.md` (D19), added with this spec's decisions.
10. **Docs.** `docs/tactical-orchestrator.md` (Battle Core: feasibility, river regions, crossing watch; Doctrine Components: playbook feasibility), `docs/scourge-of-war-ai-anchors.md` (plays are screened before selection; Scourge's `PLAYFTime` timing is now an arrival-synchronized release), `docs/telemetry.md` (the new rows), `docs/tactical-operations-ledger.md` (the crossing-threat release).

---

## 16. Not verified (runtime-only claims; each is a smoke item or a harness case)

- The picture's 5-unit sampling marks every open river cell, so regions do not leak through a narrow river. A river narrower than 5 units could be missed (smoke 1 reports the region count; a map with one region and listed crossings is the symptom).
- Vanilla's crossing list holds every bridge and ford a NavMesh path uses. Pontoons (ids 9-13) are not in it (smoke 3 shows a NavMesh path through a crossing that is not a link as a `no-route` mismatch).
- The arrival model's unmodelled terms (slope, overlap waits, column length, `commanderspeedadjustment` for HQs) stay within criterion-arrival-prediction's tolerance (smoke 4).
- Whiskey's executor moves for a marching wing meet vanilla's order delay as modelled. The AI cheat that skips the delay in front of the enemy (`combatstatussubordinates > 0`) is read on vanilla's side and may shorten some delays (smoke 4).
- BC11's two-leg route check through a via is served within BC11's per-step budget (smoke 4).
- The day-end hour read from `uniStormSystem` matches the end-of-day cycle's trigger (smoke 4 near dusk).
- Whether vanilla's no-delay cheat for engaged AI units (`combatstatussubordinates > 0`) also skips the order delay of a support order or of the enemy's shift order. BC14 charges both their courier hours alike (requirement-courier-time), so the comparison of `σ` with `τ` is even either way; smoke 4's prediction error would show an engaged body arriving early.
- That every works line on the field is an `ObstacleLine` whose pieces reach `completion` 1 when built (the AI's at `FinishImmediately`, the player's as its construction finishes), and that the cover ids of trenches the AI digs read 7 or 11 on its regiments (smoke 10 reads `worksSamples` and `worksPointsPerceived`; smoke 16's entrenched player reads the drawn line's pieces).
- That the column's formation order is admitted on the march (`CommandFormationWritePolicy` refuses a moving group, so it lands at a halt, at the latest at the Turn, where the column waits up to 0.1 h), and that vanilla's March Column keeps the column together through the Attack waypoint (smoke 11 watches `formation` on the column's rows).
- That a bombardment started by `BombardStart` behaves as vanilla's own: `CheckAIBombardment`'s stop rule (3901-3905) runs only for a group at `ai_stance` 3 or 4, so a battery of a group at another stance keeps bombarding until the doctrine cancels it or its ammo runs low through vanilla's fire code (smoke 11 reads the battery's combat behavior after the column's release).
- That `BattleUnits.GetVictoryPointsRelation` is free of side effects at evaluation cadence (it may write a debug log line; smoke 13 checks the log volume), and that `objectivepointsheld` is current between vanilla's `UpdatePoints` calls (smoke 13 reads `sOwn`/`sEnemy` against the victory bar).
- That BC11's Hold under `HoldForWorks` keeps a works-assaulting wing in place for the whole hold, with its posture producer giving it no forward move (smoke 11's cautious run reads the wings' Hold rows).
- Whether a vanilla-run enemy's shift pays any courier hours. Its long moves call `SetWaypoint(useorderdelay: false)` (`BlockedMovingOrder` 3139, `FlankMove` 117089-117090, `CheckForSeparatedEnemy` 8188), so `τ`'s `EnemyCourierHours` may overstate it by up to 0.04 h plus a ride. Against a Whiskey-commanded enemy (AI against AI) the executor's moves do pay it. Smoke 3 and 8 read `enemyShiftHours` against the enemy's observed arrival.

---

## 17. Open questions for the user

None. The review draft's questions are decided: § 14, D14-D19 (2026-10-16).

---

## 18. Out of scope

- Pontoon bridges laid during the battle (ids 9-13 and their placement) as crossings.
- Engineering a crossing (building a bridge) or bombarding a held one before crossing; the artillery doctrine is unchanged except the focal assault's bombard start (§ 7A).
- Campaign-level turning movements; vanilla's campaign `aiarrivalfactor` stays vanilla's.
- Per-wing-commander delay friction (D18).
- A Defender's offensive maneuvers; BC13's counterstroke stays the only Defender attack.
- Retuning the catalog's existing weights, personality fit squash or affinity tables (BC13's out-of-scope item stands).
- New vanilla bug fixes. requirement-stance-before-pick is a Whiskey defect; the reinforcement-schedule leak is a backlog entry only (D19).
- Orders to the W&L player beyond BC11's existing dispatch of a maneuvering ancestor.
- Siege operations against works: approaches, mines (the Crater), parallels; digging own works during the battle; artillery positioning for the preparation beyond the batteries already in range.

<!-- author-sweep: applied purpose-advantage-measurement, fit-criteria, guardrails, read-paths, roles, lifecycle, exceptions, concurrency, decision-records; n/a: none -->
