# Scourge Of War AI Anchors For Whiskey Tactical Doctrine

**Purpose:** Evidence map for converting useful Scourge of War AI ideas into Whiskey Realism without pretending the engines are identical.

**Reference install:** `/mnt/c/Program Files (x86)/Steam/steamapps/common/Scourge Of War - Remastered/`

**Gettysburg install:** `/mnt/c/Program Files (x86)/Steam/steamapps/common/SOWGBx64/`

**Gettysburg Steam manifest:** `/mnt/c/Program Files (x86)/Steam/steamapps/appmanifest_3142400.acf`

**Gettysburg investigation note, 2026-05-14:** Steam reports app `3142400` as `Scourge Of War - Gettysburg` with `installdir` `SOWGBx64`. The Gettysburg install ships an SDK at `SOWGBx64/sdk/` with the same AI source directories used for Remastered: `SowAiInf`, `SowCampAI`, and `SowMod`. `diff -qr` found no differences between Remastered and Gettysburg for those three SDK source directories. Ghidra was not needed for these AI anchors because source is present; the binary surface is `sowgbx64.exe` plus native DLLs and should only be disassembled if a future question needs closed-engine behavior not exposed by the SDK. Gettysburg-specific evidence is primarily the Civil War data pack under `BaseGB/`: logistics CSVs, Gettysburg OOBs, maps, scenarios, sandbox files, and `_SBCamp` campaign data.

**SDK files read on 2026-05-14:**

- Remastered: `sdk/SowAiInf/offai.cpp`, `sdk/SowAiInf/artyai.cpp`, `sdk/SowAiInf/cavai.cpp`, `sdk/SowCampAI/campai.cpp`
- Gettysburg: `sdk/SowAiInf/offai.cpp`, `sdk/SowAiInf/offcmds.cpp`, `sdk/SowAiInf/unitai.cpp`, `sdk/SowAiInf/artyai.cpp`, `sdk/SowAiInf/cavai.cpp`, `sdk/SowCampAI/campai.cpp`, `sdk/SowMod/xunitdef.h`, `sdk/SowAiInf/xtables.inl`

## Gettysburg Decompile / Extraction Pass

Verified on 2026-05-14 from `/mnt/c/Program Files (x86)/Steam/steamapps/common/SOWGBx64/`.

Available local tooling was native binary inspection only: `file`, `strings`, `objdump`, `nm`, and `dotnet`. No `ghidraRun`, `analyzeHeadless`, `r2`, `rizin`, `retdec-decompiler`, `wine`, `monodis`, `ikdasm`, `ildasm`, or `ilspycmd` was present in `PATH`.

Native extraction artifacts were written to `/tmp/sowgbx64-analysis/`:

- `sowgbx64.objdump-p.txt`
- `sowgbx64.objdump-d.txt`
- `sowgbx64.strings.txt`
- `SowAiInf.objdump-p.txt`
- `SowAiInf.objdump-d.txt`
- `SowAiInf.strings.txt`
- `SowCampAI.objdump-p.txt`
- `SowCampAI.objdump-d.txt`
- `SowCampAI.strings.txt`
- `NorbSoftDev.SOW.strings.txt`
- `NorbSoftDev.SOW.Utils.strings.txt`
- `ScenarioEditor.strings.txt`

The key binaries are native PE32+ x64 except for the scenario editor assemblies, which are .NET/Mono metadata assemblies. Installed AI DLLs are stripped, but keep PDB path strings:

- `BaseGB/Modules/SowAiInf.dll` -> `E:\NorbSoftDev\SOWGBx64\BaseGB\Modules\SowAIInf.pdb`
- `BaseGB/Modules/SowCampAI.dll` -> `E:\NorbSoftDev\SOWGBx64\BaseGB\Modules\SowCampAI.pdb`

Export tables confirm the engine loads the same AI callback surface as the SDK:

- `SowAiInf.dll`: `SowInit`, `SowInfAIFunc`, `SowCavAIFunc`, `SowArtAIFunc`, `SowUnitBrigThink`, `SowUnitDivThink`, `SowUnitCorpThink`, `SowUnitArmyThink`, `SowUnitSideThink`, `SowArtyOffThink`, `SowCavOffThink`, `SowSoldAmmoThink`, `SowSoldCourThink`.
- `SowCampAI.dll`: `SowInit`, `SowAIFunc`.
- `sowgbx64.exe` strings include dynamic lookup markers for `SowCampAI.dll` and `SowAIFunc`, plus the failure text `Could not find SowAIFunc %s for %s`.

Managed scenario-editor assemblies could not be IL-decompiled with the currently installed tools, but strings expose useful type and method surfaces:

- `NorbSoftDev.SOW.dll`: `ReadCommandTemplates`, `ReadEventTemplates`, `ReadGameIni`, `createFromOOB`, `ExportAsOOB`, `ReadBattleScript`, `WriteBattleScript`, `ReadMapLocations`, `GetScenarios`, `GetMaps`, `UnitMoveToCommand`, `UnitOrderCommand`, `CourierCommand`.
- `NorbSoftDev.SOW.Utils.dll`: `ParseCSV`, `MapArea`, `ScenarioEchelon*Rule`, `ScenarioUnit`, `UnitTools`, `MapTools`, `ReadFromCsv`.
- `ScenarioEditor.exe`: `AddOrUpdateUnitPositionEvent`, `UnitMoveToCommandFootprint`, `ScenarioObjectiveFootprint`, `MapObjectiveFootprint`, `Print Scenario CoC`, `Print Scenario Csv`, `Dump Csv Headers to SOWWL Dir`.

Gettysburg-specific content inventory:

- `BaseGB/Scenarios/`: 52 scenario directories.
- `BaseGB/OOBs/`: 19 OOB CSVs, including campaign and Gettysburg Day 1/2/3 variants.
- `BaseGB/Maps/`: 18 map-related ini/csv files.
- `BaseGB/Campaign/_SBCamp/`: 27 campaign sandbox files.
- `BaseGB/Campaign/_SBCamp/maplocations.csv` carries map objective metadata: name/id/priority/type/AI/location/radius/men/points/fatigue/morale/ammo/occupancy and active-window fields.
- `BaseGB/OOBs/OOB_SB_Gettysburg_Campaign.csv` carries the campaign OOB authority fields: command echelon IDs, class, weapon, ammo, flags, formation, head count, ability, command, control, leadership, style, experience, fatigue, morale, terrain values, weapon skill, horsemanship, surgeon, and calisthenics.
- `BaseGB/Logistics/courier.csv` is the most actionable command-vocabulary anchor for Whiskey conversion.
- `BaseGB/Logistics/unitattributes.csv` is the most actionable data-driven threshold anchor for morale, fallback, retreat, ammo, support distance, charge range, fatigue, artillery panic, and limber behavior.

Use this extraction pass as a source index, not as a replacement for the SDK source. Without Ghidra or an IL decompiler installed, full native pseudocode and managed C# bodies were not recovered in this pass.

## Confirmed Scourge Patterns

### Division think loop and plays

`SowAiInf/offai.cpp` has a division-level loop that:

- throttles subordinate orders with a courier timer (`CourTime + 15 * TICSPERMIN`);
- sends play/order/assigned-order messages only to brigades with no orders;
- counts brigades in trouble and sends a help request by courier every 30 minutes;
- selects the best engaged brigade or artillery battery as the play anchor;
- calls `RunPlay(best, targ)`, then executes left/right play slots and finally calls `CheckReserves()`.

Whiskey status:

- Implemented analogs: command-node roles, tactical massing cycle, playbook selection, reserve policy, order friction classification, W&L player-order bridge, `TacticalDivisionPlayExecutor`, and now (2026-05-18) **SoW-aligned pre-contact movement** — `TacticalDoctrineScorer.DecideGroupStance` returns `Apply(stance=2, pre-contact-hold)` when sector confidence is between 0.40 and 0.55, instead of skipping silently. Mirrors `offai.cpp:507-546` (brigade marches to tactical objective when no enemy in range). `TacticalGroupSectorEstimator` no-enemy floor lifted from 0.45 to 0.55 to make this branch reachable.
- Current conversion: #61 builds side/parent sibling groups at runtime, chooses the best engaged child as the play anchor, assigns complementary support/reserve/screen/fallback sibling tasks, uses courier delivery metadata when doctrine orders are present, and suppresses duplicate pending outbound command signatures while allowing materially changed commands. Plus pre-contact movement now flows through the existing role-assigned waypoint executor instead of waiting for line-of-sight contact.
- Still partial: this is a bounded #61 conversion, not a full Scourge play-slot language. It does not import Scourge's left/right play files or create a new movement writer. SoW's per-side Army/Corps think loops (`offai.cpp:1115` Army think with rectangle-based corps assignment from grand-tactics table) remain partly translated through Whiskey's playbook system but without literal rect-based corps assignment.

### Artillery limber, target, ammo, and fallback micro-logic

`SowAiInf/artyai.cpp` has artillery behavior that:

- asks the leader for an artillery fallback distance;
- limbers or retreats when unsupported and enemy is inside panic distance;
- forces unlimber before firing;
- chooses ammo by target distance;
- wheels to the most threatening quadrant unless the current frontal threat is still close enough;
- asks the leader for shooting/no-ammo/no-target artillery commands.

Whiskey status:

- Implemented analogs: artillery mission planning for support, counterbattery, conserve ammo, danger-close, weak-point assignment, ammo mission, safe reposition intent, and now `TacticalArtilleryMicroDoctrine`.
- Current conversion: B7 can command limber, unlimber, fallback movement, conserve-ammo cancellation, and target-facing wheel through vanilla `ChangeRegimentFormation`, `SetWaypoint`, `SetMovementMode`, and `RotateRegiment` under the existing artillery doctrine flag and W&L/player gates.
- Still partial: quadrant scoring is derived from live closest-target / in-fire-range evidence, not Scourge's full quadrant table.

### Cavalry follow, guard, screen, scout, and raid

`SowAiInf/cavai.cpp` has cavalry-specific follow modes:

- guard follows behind a target and faces the target direction;
- scout/screen/raid request new scout locations after reaching a follow location;
- raid clears invalid targets if they are hidden, officers, in forts, in square, cannot be charged, or are protected by nearby infantry/artillery;
- screen gets away from close enemies instead of standing into contact.

Whiskey status:

- Implemented analogs: `Screen`, `Probe`, `Scout`, `FixEnemy`, `ReserveWait`, flank and fallback command roles, tactical contact kind `CavalryScreen`, strategic cavalry-capable formation tagging, and `TacticalCavalryFollowDoctrine`.
- What ships (BC9 correction, 2026-10-15): the posture producer maps a command group with cavalry in its subtree into guard/scout/screen/raid follow modes; the raid filter reads the visible target's own record (in square = regiment formation 4, an officer = a command group or a courier, in a fort, supported by visible infantry/artillery); screens get away from close enemies. `RequestScoutLocation` resolves to the recon planner's look point (`BattleRecon.LookPoint`), and "hidden target" is gone (the follow target is the closest visible enemy, never hidden). Actual scouting is done by single cavalry regiments sent by the recon producer (`Battle/BattleRecon.cs`), not by the follow modes, and the follow modes act on a whole group.
- Still partial: guard "behind target" uses the existing command target/fallback machinery rather than a bespoke Scourge `GetLocBehind` coordinate writer; the follow modes still steer whole groups (a group with a single cavalry regiment counts), unlike Scourge's per-unit cavalry AI.

### Campaign sandbox movement

`SowCampAI/campai.cpp` has campaign sandbox logic that:

- picks attacker/defender posture from commander and leader personality;
- splits/reattaches subordinate commands;
- moves to town, major town, destroy, and supply-base objectives;
- detaches corps advance guards and pickets;
- on enemy contact, must choose battle or retreat based on relative divisions plus cavalry/artillery modifiers.

Whiskey status:

- Implemented analogs: historical operation profiles, coordinated operation packages, formation directives, operational probes, defensive packages, supply/logistics pressure, and campaign objective scoring.
- Current backlog guidance: [`docs/strategic-recon-commitment.md`](strategic-recon-commitment.md). Archived source spec: [`docs/superpowers/specs/archive/2026-05-14-scourge-campaign-advance-guard-sandbox-design.md`](superpowers/specs/archive/2026-05-14-scourge-campaign-advance-guard-sandbox-design.md).
- Still not runtime-implemented: campaign-map advance-guard/picket/supply-base detachment remains a strategic slice because it needs sidecar persistence, campaign movement API smoke, and W&L/player movement guards before shipping.

## Additional Transfer Candidates From Lower-Level Gettysburg Pass

Verified on 2026-05-14 against the Gettysburg SDK/source, Gettysburg data tables, and the GTCW decompile. These are new lower-level ideas, not claims that Whiskey already has equivalent shipped behavior.

1. Regimental close-combat fear gate.
   - Scourge anchors: `sdk/SowAiInf/offcmds.cpp:364` (`FearCheck`), `sdk/SowAiInf/unitai.cpp:373` (`BayonetSituation`), `unitai.cpp:425` (`ChargedByCavFaceSituation`), `unitai.cpp:595` (`CavalryDangerCheck`), `unitai.cpp:1343` (`StandTargetAmmoAI`).
   - What it adds: attacker/defender odds are not just headcount. Scourge weights cavalry, skirmishers, artillery, morale, unit/officer modifiers, leader personality, high ground, defensive terrain, flank/rear angle, recent fire, and charge/run state before allowing charge, bayonet, raid, or retreat decisions.
   - GTCW anchor: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:4905` (`AIBattle.MicroAICheckForCharges`) is broad stance-based charge initiation; Whiskey gates it, but does not yet have this full fear-ratio model.
   - Whiskey status: implemented as pure `TacticalMeleeFearDoctrine` and wired into #41 doctrine charge approval from live charge-target evidence. Cavalry raid, fallback ladder escalation, and rear-guard consumers remain future integration points.

2. Anti-cavalry formation discipline, translated without literal squares.
   - Scourge anchors: `unitai.cpp:27-34` (`CAVALRY_DANGER_DISTANCE`, `SQUARE_MEN_MINIMAL`, `BAYONETBREAK`), `unitai.cpp:595` (`CavalryDangerCheck`), `offcmds.cpp:316` (`SquareFriendOverlap`).
   - What it adds: infantry checks strength, morale, nearby cavalry angle/distance, square overlap, and friend spacing before forming square or retreating from cavalry.
   - GTCW anchor: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:5036` (`AIBattle.AdjustCavalryBehavior`) already handles cavalry charge/dismount/remount. No verified GTCW infantry square command surface was found in the decompile.
   - Whiskey translation: do not implement a fake square order. Translate this into an anti-cavalry hold/refuse/charge-denial gate that preserves formed infantry, avoids unsupported retreats into cavalry, and biases cavalry-threat responses by morale, headcount, and friendly spacing.

3. Friend-ahead and same-line occupancy gates.
   - Scourge anchors: `offcmds.cpp:39` (`CheckAdvancePath`), `offcmds.cpp:76` (`CheckHaltLine`), `offcmds.cpp:180` (`CanGunRedeploy`), `offcmds.cpp:204` (`CanRedeployLine`), `offcmds.cpp:448` (`FriendAheadMe`).
   - What it adds: before advancing, wheeling, halting, or redeploying, Scourge samples the proposed line and identifies friendly blockers ahead, left, right, and behind.
   - Whiskey status: implemented as `TacticalPathQualitySample.FriendlyBlocker01`; #61 estimates friendly front blockers from `BattleUnits.completeunitlist` and feeds the score into `TacticalNavMeshPlanner`.

4. Skirmisher parent-distance, fatigue, and recall discipline.
   - Scourge anchors: `offcmds.cpp:541` (`ReturnSkirmParent`), `offcmds.cpp:572` (`SplitCompany`), `unitai.cpp:1073` (`FollowScreen`).
   - What it adds: large units split skirmishers only under constraints, then recall them for low ammo, fatigue, excessive distance, lack of target, danger, or parent movement.
   - GTCW anchor: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:5343` (`AIBattle.CheckSkirmishing`) already has detach/reattach logic, so Whiskey should not replace it blindly.
   - Whiskey status: implemented as pure/tested `TacticalSkirmisherDoctrine` for parent-distance, ammo, fatigue, morale, and close-threat recall pressure. No live skirmisher write surface is enabled yet.

5. Infantry volley and bad-shot discipline.
   - Scourge anchors: `unitai.cpp:1343` (`StandTargetAmmoAI`), `offcmds.cpp:619` (`GetInfCommand`).
   - What it adds: hold/release volley behavior, bad-shot reacquisition, avoid wasting fire while misaligned or against poor lateral/fort targets, and choose advance/charge only after fire-distance and fear checks.
   - GTCW anchor: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:118292` (`Regiment.GetFireRange`) applies vanilla short/medium/long fire-range behavior; UI buttons call `BattleUnits.ChangeCombatBehavior` for infantry short/medium/long as 0/1/2 and cavalry evade/neutral/charge as 4/5/6.
   - Whiskey status: implemented as `TacticalFireControlDoctrine` plus `TacticalInfantryFireDoctrine`. #61 now issues vanilla `ChangeCombatBehavior` fire-control writes when no movement/formation posture write is needed, and #41 consumes the same fire discipline before permitting charge initiation. Formed infantry prefers medium/short while engaging; probe/screen/scout/delay/fallback/forming/assembly/guard tasks may keep long fire because they are not ready to close.

6. Scenario-script phase templates.
   - Gettysburg anchors: `BaseGB/Scenarios/*/battlescript.csv`, `BaseGB/Scenarios/*/maplocations.csv`, `BaseGB/Logistics/courier.csv`.
   - What it adds: the Gettysburg scenarios repeatedly use staged artillery preparation, column approach, line deployment, timed attack release, objective activation windows, hide/show arrivals, detachment, courier events, death events, and randomized event branches.
   - Whiskey translation: create scenario-inspired operation templates for artillery prep -> approach column -> deploy line -> support/reserve release -> objective hold/dwell. This belongs in playbook/operation-director doctrine, not as a one-off scenario script importer.

7. Data-driven battlefield attribute matrix.
   - Gettysburg anchor: `BaseGB/Logistics/unitattributes.csv`.
   - What it adds: experience, fatigue, morale, and officer style directly modify fallback chance, retreat chance, fire/load/melee performance, support distance, rally behavior, volley distance, charge range, artillery hold/panic, limber time, and fatigue loss.
   - Whiskey status: implemented as pure/tested `TacticalBattlefieldAttributeMatrix`, ready to feed future endurance, fallback, fire, and melee threshold tuning once the exact live GTCW source fields are selected per consumer.

## Current Honest Gap List

After the 2026-05-14 Scourge conversion pass, the direct tactical Scourge gaps are implemented as Whiskey-native bounded doctrine:

1. Runtime division-play execution anchored on the best engaged subordinate: implemented in `TacticalDivisionPlayExecutor` and #61 runtime sibling grouping.
2. Commander-to-subordinate outbound order cadence: implemented as `TacticalOutboundCourierCadence`, `TacticalOutboundOrderLedger`, #61 courier delivery throttling, and duplicate-pending command suppression for doctrine/runtime play orders.
3. Cavalry-specific guard/scout/screen/raid follow modes: `TacticalCavalryFollowDoctrine` with raid filters and screen-away behavior, applied to whole command groups by the posture producer; real reconnaissance (single cavalry regiments to unwatched ground and entry points, probes in force, a farther skirmisher screen) is the BC9 recon producer (see [`tactical-orchestrator.md`](tactical-orchestrator.md) § Battle Core).
4. Artillery limber/unlimber and per-battery wheel/fallback behavior: implemented in `TacticalArtilleryMicroDoctrine` and B7 vanilla-safe writes.
5. Campaign advance-guard/picket/supply-base sandbox movement: specified only; runtime implementation remains a separate strategic slice.

The already-implemented Whiskey analogs are: frontage/echelon/artillery-line battle geometry, runtime path-quality sampling, tactical massing/endurance gates, operational reserve percentage/partial/final reserve policy, fallback ladder mapping, artillery weak-point/ammo/reposition mission planning, SOP authority gates, and the W&L player-subordinate order bridge (#62).

The lower-level candidates above are now partly converted: melee fear is wired into #41 charge doctrine, friendly front blockers are wired into #61 path-quality planning, and infantry fire control is wired into #61/#41 through verified vanilla combat-behavior orders. Skirmisher recall and the battlefield attribute matrix remain pure tested doctrine. Anti-cavalry square analogs and scenario phase templates were intentionally skipped in this pass. Fresh Active runtime smoke is still required for the new fire-control write surface.

## 2026-05-21 Scourge regimental-AI deep dive

**Investigation goal:** map Scourge's per-regiment tactical decision tree (flanking, isolation, retreat) and identify the corresponding vanilla GTCW W&L hook points so Whiskey can extend rather than fight vanilla's regimental logic.

### Scourge's regimental decision tree (`sdk/SowAiInf/unitai.cpp`)

The master tactical function is `StandTargetAmmoAI()`. Decision priority (per the `Think()` ladder, line 2284-2483):

1. Routed → exit AI
2. MoraleAI — routed/broken/morale-drop triggers retreat or fallback
3. Melee/charge resolution
4. Follow/guard reattach (if `morale < AI_CANTFOLLOW = 3`)
5. Target check + tactical no-target routines
6. ChargedByCavFaceSituation / BayonetSituation
7. CheckAllTargets — builds enemy[4] quadrant array
8. Fort logic / cavalry-danger square decision
9. Covering troops AI
10. Skirmisher logic
11. Charge eligibility
12. Advance/fallback
13. Standing without ammo
14. **Standing with ammo — StandTargetAmmoAI: the wheel/flank/retreat decision tree (line 1410-1640)**
15. Marching to target

### Quadrant threat model (`StandTargetAmmoAI`, line 1410-1450)

```cpp
for ( i = 0; i < eQuadMax; i++ ) {            // 4 quadrants: front/left/rear/right
    enemy[i].threat = enemy[i].men * 100 - enemy[i].dist - enemy[i].cdist;
    enemy[i].threat = WheelingThreatEvaluation(i, base, enemy, myquad);
    if (enemy[i].men > 0 && (quad == -1 || enemy[i].threat > threat)) {
        quad = i;  threat = enemy[i].threat;
        if (enemy[i].cdist < base.GetMod(eGameWheelDist))
            pressed += 1;
    }
}
if (!bMustWheel && quad != myquad) bMustWheel = true;
```

- Threat = `men × 100 − dist − cavDist`
- `WheelingThreatEvaluation` applies 50% reduction for enemies in fallback/melee/fort/skirmish state
- `pressed >= 2` → unit is boxed in, can't wheel safely → retreat (Scourge's `bMustWheel + !bCanWheel`)
- `IsFlanked()` flag triggers wheel when flank threat exceeds front

### 3-tier morale retreat (`MoraleAI`, line 929-1032)

```cpp
if (base.Morale() <= MORAL_ROUTED) base.ICommand(eComRoute, 1);          // priority 1
else if (base.Morale() <= MORAL_BROKEN + danger) base.ICommand(eComRetreat, 1);  // priority 2
else if (PrevMor > Morale && danger) {                                    // priority 3
    if (Ran(0,100) <= eGameFallbackPct) base.ICommand(eComFallback, 1);  // 15-20yd fallback
    else if (Ran(0,100) <= eGameRetreatPct) base.ICommand(eComRetreat, 1);
}
```

Retreat direction = `base.DeP()` (brigade commander's rally point), NOT "away from enemy vector."

### Vanilla GTCW W&L regimental hook points (`/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`)

| Vanilla method | Line | Purpose | Patch potential |
|---|---|---|---|
| `PerformAIActionDLCWL(unit, groupforstancecheck)` | 5101 | **Master AI gate. Called 50+ times. Returns false when `dlcw_isundercommander=true` → blocks all vanilla micro-AI for that regiment.** | **HIGH-VALUE TARGET** — selective injection point. Patch to allow conditional regimental override without re-patching every call site. |
| `CheckCurrentOrderUpdate(unit, type, pos, ...)` | 8233 | Order validation + propagation. Re-attributes orders to player commander when `dlcw_isundercommander=true`. | Augmentation point for "feed suggested orders to player" workflows. |
| `CheckLineFallbacks(aigroup)` | 5140 | Per-regiment fallback. Player-commanded regiments in non-offensive parent stance skip fallback. | Mirror semantics for our regimental fallback doctrine. |
| `UpdateMicroAI(unit)` | 5543 | Per-regiment tactical decisions. Runs sub-task cycle (`microaitaskupdatecycle=30`). | Per-regiment per-frame work happens here. |
| `CalculateSideStatsAndUpdateAITasks()` | 84570 | Master battle update. | Already our heavy-gate anchor. |

### Vanilla GTCW regimental decision triggers

- `morale < GamePrefs.moraletriggerforfallbackifenemyclose[ai_stance]` — fallback threshold (per stance)
- `groupstrengthdirect < GamePrefs.minstrengthbattlewithdrawal` — battle-avoidance threshold (~450 men typical)
- `groupmorale >= GamePrefs.minimummoraleforunitstoparticipateinbattles` — minimum-morale to engage
- `lastretreatiswithdrawal && unittyp != 17` — independent withdrawal continuation flag

### Player-command semantics (`dlcw_isundercommander`)

**Binary, not augmented:** when `true`, vanilla AI is fully blocked. Player input is the ONLY decision source for that regiment. No partial-control scenarios.

Whiskey takes option (b): it never writes to player-commanded units. `TacticalWriteGuard.IsPlayerProtected` covers `dlcw_isundercommander`, the player's command and chain, and DLC-taken-over units, failing closed. (The regimental-intelligence doctrine that this section originally planned for was removed on 2026-10-15; per-regiment control is not part of the current design.)

## 2026-05-22 artillery panic audit

**Investigation goal:** verify Whiskey's artillery panic/retreat logic against both Scourge of War SDK and vanilla GT decompile, after smoke showed batteries at 0-50m from enemy infantry with support count 14-27.

### Scourge artyai.cpp findings

- **`DistArtyPanic()` (line 180)** — dynamic per-unit method, default likely 150-200yd. Leader can override via `eSTStandArtilleryFallBack` command (line 192).
- **Support detection (line 199-205)** — RADIAL check, NOT directional. Counts any inf/cav within `EnemyCloseYds()` of artillery in any direction (including behind). Filtered by `CanFight()` (excludes routed).
- **Retreat cascade (line 209-224)** — fires when `EnemyCloseYds() < panicdistance` AND (`MorUnitBon == 0` OR `sup == 0`) AND enemy not routed/retreating. Unlimbered → limber first; limbered → halt + retreat.
- **No anti-cavalry special case** — same panic threshold for cavalry as infantry.
- **No forward-movement on own initiative** — Scourge artillery is purely reactive. `AttackMarch()` defers to externally-issued orders only.

### Vanilla GT decompile findings

- **`CheckArtyFallback` (line 3499)** — reads `unitrange.closestownunitnonrouted` for support. **Counts ANY non-routed friendly including other artillery** — fundamentally broken: a battery 50m from another battery thinks it has support and won't retreat.
- **`CheckAIBombardment` (line 3869)** — bombardment decision, only fires for `ai_stance == 3 || 4`.
- **`Regiment.ammo` array** — float[3] indexed by ammo type. GT slot 2 = canister (not Scourge's slot 0).
- **`Regiment.mounted`** — 1 = limbered, 0 = unlimbered. No explicit unlimbering-transition state exposed.
- **`unitrange.enemyinfirerangereg`** — `List<Regiment>` of enemies in artillery's fire range, populated by vanilla's range scan.
- **`CheckCounterBatteryFire` (line 3827)** — yes, vanilla has counter-battery logic; integrated into bombardment cascade.
- **Forward-movement source**: vanilla has multiple paths that can advance artillery (objective-driven movement, post-firing repositioning via `aritimetowaitbeforemovingcloser` at line 3789). No single trigger; the cumulative effect is artillery ending up forward.

### Whiskey alignment + corrections

| Layer | Scourge | Vanilla GT | Whiskey | Verdict |
|---|---|---|---|---|
| Panic distance | `DistArtyPanic()` ~150-200yd | none (vanilla doesn't retreat properly) | 100m default + 60m point-blank floor | More conservative, with explicit floor |
| Support detection | Radial (any inf/cav within EnemyCloseYds) | Broken (any friendly inc. artillery) | Geometric: on-axis interposition only | More correct than both |
| Anti-cavalry | No special case | No special case | No special case | Match |
| Forward-movement initiation | None | Multiple paths | None (Whiskey is reactive) | Match Scourge |
| Forward-movement veto | n/a | None | SetWaypoint Postfix veto (2026-05-22); since the battle core, `WaypointValidator` point-blank refusal + `ArtilleryPointBlank` producer | Closes vanilla's gap |

### `ArtillerySetWaypointVetoPatch` (shipped 2026-05-22; removed by the battle core, 2026-10-15)

**Removed.** Battle-core slice BC5b deleted the patch, its `Enable Artillery Waypoint Veto` flag, the `TacticalArtilleryWaypointVeto` row and `ApplyArtilleryAudit`. The point-blank rule now lives in two places. `WaypointValidator` refuses an artillery target near a visible enemy. The `ArtilleryPointBlank` producer (`ArtilleryIntentProducer`) pulls back a battery that vanilla's own move sent to point blank, as a Safety intent through the order executor (`docs/superpowers/plans/2026-10-15-battle-core.md` § BC5b). The text below records the shipped design.

Postfix on `BattleUnits.SetWaypoint(Regiment, ...)`:
- Filtered to artillery (unittyp == 2)
- If proposed target is within 60m of any visible enemy → overwrite with 200m retreat away from enemy
- Recursion guard via `[ThreadStatic] _inVetoWrite`
- Default-on behind `Enable Artillery Waypoint Veto`
- Emits `TacticalArtilleryWaypointVeto` telemetry

This closes the timing gap between vanilla's forward-move and Whiskey's 1.5s `ApplyArtilleryAudit` cycle — artillery can no longer end up at point-blank between audit fires.

### `TacticalArtilleryMicroDoctrine.Decide` updated cascade (2026-05-22)

Priority order (descending urgency):
1. **No enemy** → Preserve
2. **Point-blank ≤60m** → Limber or Retreat (NEW — unconditional, ignores support count)
3. **Low ammo <10%** → ConserveAmmo
4. **Close ≤PanicDistance + no support + no morale bonus** → Limber or Retreat
5. **Limbered with target in range** → Unlimber
6. **Can wheel + better quadrant elsewhere** → WheelToBestThreat
7. **Target in range** → Fire
8. Else → Preserve

The point-blank branch ahead of low-ammo ensures a battery at <60m doesn't sit and conserve ammo while being charged.

### `CountFriendlyInfantryCavInterposed` (2026-05-22 refactor)

Replaces prior `CountFriendlyInfantryCavWithinEnemyDistance`. Uses geometric projection:
- Friend must be on enemy-side of artillery (positive projection onto artillery→enemy axis)
- Friend's projection length < distance to enemy (interposed, not past)
- Lateral offset from axis ≤ 100m (on-axis, regiment-frontage scale)

Falls back to original 200m radial check when enemy is null or distance is invalid (sanity floor).

## 2026-05-23 Scourge grand-strategy + play-template architecture

### Play templates (`offai.cpp:1144-1320`)

Scourge's army-level offensive AI maps **grand tactics** to **plays** to **rects**:

```cpp
// offai.cpp:1144
if ( base.ReEval() )   // re-evaluation triggered — NOT every tick
{
    base.ReEvalTactics();
    if ( aplay == NULL && base.NumComplete() == 0 && !base.PlyrCtrl() )
    {
        type = CXUtil::OpenType();                   // scenario-determined battle type

        if ( type == eTypeAtt1 || type == eTypeAtt2 )
        {
            if ( base.Rank(eRankSide) == type )      // I'm the attacker?
                aplay = CXUtil::GetAttackPlay();
            else
                aplay = CXUtil::GetDefendPlay();
        }
        else if ( type == eTypeMeet )
            aplay = CXUtil::GetMeetingPlay( rect );
        // implicit: defend if not specified
    }
}
```

**Key invariants:**
1. **Battle type is scenario-set, not per-tick.** `CXUtil::OpenType()` is set by the SCENARIO (`eTypeAtt1`, `eTypeAtt2`, `eTypeMeet`, implicit defend). Not derived from per-tick force balance.
2. **Side identity matches battle type.** `base.Rank(eRankSide) == type` checks if THIS side is the attacker designation.
3. **Play is cached across ticks.** `base.ArmyPlay()` holds it. Only re-picked on `ReEval()` triggers.

**Each play is a TABLE of rects with corps assignments (`offai.cpp:1191`):**
- `PLAYRect(aplay, i)` — zone (rectangle) for slot i
- `PLAYCorpNum(aplay, i)` — preferred corps for slot i (1-based)
- `PLAYFTime(aplay, i)` — start time for slot i (echelon sequencing)
- `PLAYSize(aplay)` — slot count

Quadrant rotation (`offai.cpp:1199`) maps canonical play coords into world coords via `base.StartQuad()`.

Corps assignment (`offai.cpp:1289`): VP-override (corps tracking major VP gets the rect containing it), else closest-corps by `Dist2Yds(rect.Mid())`.

Per-corps internal slots (`offai.cpp:1030`): `PSTATL*` (left side) and `PSTATR*` (right side) indexed by `eSpotBot/Mid/Top × Left/Right`. Divisions fill slots as they become ready.

### Battle-type stance gate (`offai.cpp:1151-1159`)

The "am I attacking or defending" decision is set ONCE by the scenario, then STAYS STABLE. Force balance does NOT flip a side from defend to attack mid-battle. Personality + force balance influence the SECONDARY pick (which OF several defend or attack plays) but never cross the attack/defend boundary.

This invariant prevents doctrine-vs-playbook mismatches: the playbook layer can't decide on tick 47 that it wants to attack if the scenario says it's the defender.

### Vanilla GTCW equivalent: `BattleUnits.GetBattleType` (decompile line 86095)

Vanilla exposes the same signal Scourge gets from `OpenType()`:

```csharp
public int GetBattleType(int alliance, bool usereadinesswhendraw = false)
{
    // Compare objective-points-held ratio to GamePrefs.objectiveratiodefend
    if (sideinformation[sideFromAlliance].objectivepointsheld / ... >= ratio) return 2; // defend
    if (sideinformation[num].objectivepointsheld / ... >= ratio) return 1;              // attack
    return 0;  // meeting engagement
}
```

Return codes: **0** = meeting / **1** = we attack / **2** = we defend. Vanilla itself uses this gate at `CheckGlobalAIStrategy` line 6396 to force `macroai = 2` when battleType==2 and force balance is below defensive trigger.

### Whiskey integration (2026-05-23 L — stance gating)

`ArmyEvidenceBuilder.ReadBattleStance` reads `GetBattleType` and stamps `BattleStance` (Meeting / Attacker / Defender) onto `TacticalForceBalanceEvidence`. Two hard gates fire:

1. **Doctrine gate** (`TacticalReinforcementOpportunityDoctrine.Decide`): `BattleStance.Defender` blocks the `AttackNow` branch regardless of force ratio. Force balance can still output DefensiveHold / DelayingDefense / WithdrawalToFightLater for the defender side.
2. **Catalog hard filter** (`TacticalPlaybookCatalog.Select`): `StanceOf(BattlePlanId)` classifies each playbook (Offensive / Defensive / Neutral). Defender stance skips Offensive playbooks at the precondition stage. Attacker stance skips Defensive playbooks. Meeting allows all.

The Whiskey play-template ↔ Scourge play translation:

| Scourge | Whiskey |
|---|---|
| `SXArmyPlay aplay` | `PlayTemplate template` |
| `PLAYRect(aplay, i)` | `PlayTemplate.Rects[i]` (army-relative coords) |
| `PLAYCorpNum(aplay, i)` | implicit — closest-corps in `PlayCorpsAssignment.Assign` |
| `PLAYFTime(aplay, i)` | `PlayRect.StartTimeOffsetSeconds` |
| `base.StartQuad()` rotation | `PlayProjection.Project` (army↔enemy axis, not quadrant cases) |
| `RunArmyPlay(corps, rect)` | `DirectChildAllocator.AllocateWithZones` |

The 18 Whiskey templates correspond one-to-one with the 18 BattlePlanId archetypes (after the 2026-05-23 generic rename — `LeeEnvelopment → DoubleEnvelopment` etc.). Offensive templates only fire when stance allows attack; defensive templates only on Defender stance.

### Whiskey ↔ vanilla macroai

#44 `BattleMacroStrategyPatch` (Postfix on `AIBattle.CheckGlobalAIStrategy`, line 6314, plus a value-restoring Prefix) is the single owner of the AIBattle instance `macroai`. Vanilla's `AdjustGroupAIStance` (4221) then drives per-group `ai_stance` for brigades Whiskey has no explicit role for, so the line of battle forms through vanilla's own cascade. The 2026-05-23 `BattleMacroAIOverridePatch` Prefix that wrote the persisted `sideinformation[side].macroai` was removed on 2026-10-15; its reinforcement-opportunity opinion is now #44's fallback when the operations ledger has none.

Reinforcement-opportunity mapping (`TacticalVanillaMacroAiMapping`):
- `AttackNow` + ratio ≥ envelopment threshold → macroai 0 (assault: by the battleprefs parse order 54685-54700, index 0 is assault, the macro stance with the lowest flank-order trigger 1.0, not a separate flank mode); below it → 1 (frontal attack)
- `WaitAndConsolidate` / `DelayingDefense` / `WithdrawalToFightLater` → 2 (defend). Whiskey never writes 3: a Whiskey-written retreat with `startoffallback == 0` ends the battle on the next cycle (6362).
- `DefensiveHold` / `NoOpportunity` → no opinion (vanilla's own value stands)
