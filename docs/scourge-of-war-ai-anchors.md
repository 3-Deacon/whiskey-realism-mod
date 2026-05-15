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

- Implemented analogs: command-node roles, tactical massing cycle, playbook selection, reserve policy, order friction classification, W&L player-order bridge, and now `TacticalDivisionPlayExecutor`.
- Current conversion: #61 builds side/parent sibling groups at runtime, chooses the best engaged child as the play anchor, assigns complementary support/reserve/screen/fallback sibling tasks, uses courier delivery metadata when doctrine orders are present, and suppresses duplicate pending outbound command signatures while allowing materially changed commands.
- Still partial: this is a bounded #61 conversion, not a full Scourge play-slot language. It does not import Scourge's left/right play files or create a new movement writer.

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

- Implemented analogs: `Screen`, `Probe`, `Scout`, `FixEnemy`, `ReserveWait`, flank and fallback command roles, tactical contact kind `CavalryScreen`, strategic cavalry-capable formation tagging, and now `TacticalCavalryFollowDoctrine`.
- Current conversion: #61 maps cavalry-capable commands into guard/scout/screen/raid follow modes, filters raid targets that are hidden/officer/fort/square/supported/unsafe, sends screens away from close enemies, and maps guard-behind/raid/screen decisions back into vanilla-safe command tasks.
- Still partial: guard "behind target" uses the existing command target/fallback machinery rather than a bespoke Scourge `GetLocBehind` coordinate writer.

### Campaign sandbox movement

`SowCampAI/campai.cpp` has campaign sandbox logic that:

- picks attacker/defender posture from commander and leader personality;
- splits/reattaches subordinate commands;
- moves to town, major town, destroy, and supply-base objectives;
- detaches corps advance guards and pickets;
- on enemy contact, must choose battle or retreat based on relative divisions plus cavalry/artillery modifiers.

Whiskey status:

- Implemented analogs: historical operation profiles, coordinated operation packages, formation directives, operational probes, defensive packages, supply/logistics pressure, and campaign objective scoring.
- Spec created: [`docs/superpowers/specs/2026-05-14-scourge-campaign-advance-guard-sandbox-design.md`](superpowers/specs/2026-05-14-scourge-campaign-advance-guard-sandbox-design.md).
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
3. Cavalry-specific guard/scout/screen/raid follow modes: implemented in `TacticalCavalryFollowDoctrine` with raid filters and screen-away behavior.
4. Artillery limber/unlimber and per-battery wheel/fallback behavior: implemented in `TacticalArtilleryMicroDoctrine` and B7 vanilla-safe writes.
5. Campaign advance-guard/picket/supply-base sandbox movement: specified only; runtime implementation remains a separate strategic slice.

The already-implemented Whiskey analogs are: frontage/echelon/artillery-line battle geometry, runtime path-quality sampling, tactical massing/endurance gates, operational reserve percentage/partial/final reserve policy, fallback ladder mapping, artillery weak-point/ammo/reposition mission planning, SOP authority gates, and default-off W&L player-subordinate order bridge.

The lower-level candidates above are now partly converted: melee fear is wired into #41 charge doctrine, friendly front blockers are wired into #61 path-quality planning, and infantry fire control is wired into #61/#41 through verified vanilla combat-behavior orders. Skirmisher recall and the battlefield attribute matrix remain pure tested doctrine. Anti-cavalry square analogs and scenario phase templates were intentionally skipped in this pass. Fresh Active runtime smoke is still required for the new fire-control write surface.
