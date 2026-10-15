# Vanilla perk-id table mismatches

Audit of every `Regiment.HasPerk` / `GetPerkLevel` / `HasPerkParent` / `GetPerkLevelParent` call site in `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` (Steam build 14703111, 2026-10-15), plus the `IncreasePerkExp` ids that matter. Bare numbers are decompile lines. Pure rule: `Strategic/VanillaPerkTables.cs`; tests: `tests/WhiskeyRealism.Tests/PerkIdFixTests.cs`.

## The three tables

Vanilla stores perks as raw ids in `Regiment.perks` (`Regiment.Perk` 110346). `HasPerk` (132765) and `GetPerkLevel` (132719, level clamped to `maxperklevels - 1` = 2) compare raw ids and do no remapping. The table an id belongs to is chosen by unit type, the same way the UI picks tooltips (`PerkRow.GetPerksShown` 208802, `UpdatePerkButton` 208939):

| Unit types | Table | Tooltip file | Slots |
|---|---|---|---|
| `unittyp <= 13` (infantry 0, cavalry 1, artillery 2, skirmishers 3, scouts 4, courier 5, HQ levels 10-13) | battle ("Single") | `Config/PerkBattleTooltips.txt` (19 perks) | 1 (`create_regiment` 89856) |
| 14 division, 15 corps, 16 army, 18 army group | campaign ("Group") | `Config/PerkTooltipsArmyCampaign.txt` (18 perks) | 4 |
| 17 fleet | fleet | `Config/PerkFleetsTooltips.txt` (11 perks) | 4 |

Battle HQs keep the campaign table: `BattleUnits.import_units` (88497) reads the group's four campaign perks (88579-88585) and creates the HQ with `unittyp = 13 + hierarchylevel` and those perks (88632). Battle regiments get their one battle slot (88691-88697). `HasPerkParent` (132748) only answers for `calledfromcampaign && unittyp == 15` (a corps asking its army); for every other unit it is false. Detached skirmishers/scouts copy their parent's battle perk (`DetachCompany` 90444-90453); `ReattachCompany` (90582) merges perk experience back weighted by strength, not levels.

Ids that collide across tables and matter here: **8** = Expert Scouts (battle) / Ambulance Corps (campaign) / Blockade Busters (fleet); **3** = Expert Skirmishers / Bureau of Military Information / Balloon Barge; **6** = Ace of Spade / Foot Cavalry / Sea Raiders; **16** = Grand Battery / Expert Scouts (campaign); **1/2** = Sharpshooters / Zouave (battle).

## Audit

Context: **B** battle regiment (`<= 13`), **G** campaign group (14-16), **HQ** battle HQ carrying campaign perks, **F** fleet.

| Site | Method | Context | Id read as | Effect | Verdict |
|---|---|---|---|---|---|
| 18340-18354 | `Autocalc.UpdateSiegeStrength` | G | 0 Siege Train, 2 Balloon Corps | siege strength | OK |
| 19257-19272, 19394 | `Autocalc.UpdateFortNavyEngagement` | F | 1 Mortar Boats, 3 Balloon Barge, 4 Limelights, 7 Amphibious Attack | fort bombardment / occupation | OK |
| 19589-19690 | `Autocalc.UpdateNavalBattle` | F | 4, 8 Blockade Busters, 10 Port Defender, 0 Torpedoes | firing / ramming | OK |
| 20126 | `Autocalc.UpdateSiegeCombat` | G | 0, 10, 15 | level-up rate | OK |
| 20395-20411 | `Autocalc.UpdateRaiding` | G | 12 Partisan, 13 Bushwhackers | raid casualties / morale | OK |
| 21080 | `Autocalc.FightUnit` | B | **2** (Zouave) | `sharpshooterperkinfluenceautocalc` | **Mismatch** BUG-AI-006: Sharpshooters is 1. Fixed by `AutocalcPerkIdsPatch` |
| 21100-21136 | `Autocalc.FightUnit` | G | 10, 17, 11, 15 | siege assault, skirmish, night | OK |
| 21155 | `Autocalc.FightUnit` | G | **10** (Sappers and Miners) | `fightingstrengthembarkedriverexp` | **Mismatch** BUG-AI-007: River Expedition is 17 (campaignprefs.txt 1363 names it). Fixed by `AutocalcPerkIdsPatch` |
| 21247 | `Autocalc.FightUnit` | B | **1** (Sharpshooters) | no high-fatigue morale hit | **Mismatch** BUG-AI-006: battle gives it to Zouave 2 (`UpdateMorale` 128659). Fixed by `AutocalcPerkIdsPatch` |
| 21368 | `Autocalc.UpdateFatigue` | B | **1** (Sharpshooters) | `perk_zouaves_fatiguefactor` | **Mismatch** BUG-AI-006: Zouave is 2 (`Regiment.UpdateFatigue` 132665). Fixed by `AutocalcPerkIdsPatch` |
| 22570 | `Autocalc.CalculateEngineeringPoints` | B (skips `> 13`) | 0 Engineering | engineering points | OK |
| 83881, 83930 | `BattleUnits.CheckForGroupPerk` / `CalculateCommanderFame` | HQ `>= 15` | 14 Embedded Reporters | fame | Table OK; level read from the unit, not the parent that has the perk, and the search skips the unit itself (BUG-AI-010). Fixed by `CommanderFameReportersPatch` |
| 87435 | `BattleUnits.CalculateAllUnitsOfAlliance` | B (`unittyp == 0`) | 0 Engineering | engineers | OK |
| 96407-96413 | `CBuilding.UpdateManualBuildingPlacement` | G | 7 Engineers & Mechanics | construction speed | OK |
| 101207-101213 | `FogOfWar.UnitIsHidden` | B, G, HQ, F | 3, **8** | expert skirmisher / scout concealment | **Mismatch** BUG-AI-004 on G/HQ/F. Fixed by `FogPerkConcealmentPatch` |
| 101298-101301 | `FogOfWar.UpdateViewRange` | B, G, HQ, F | **8** | `perk_expertscoutsvisibilityrangefactor` | **Mismatch** BUG-AI-003 on G/HQ/F. Fixed by `FogPerkSightPatch` |
| 104110-104137 | `IIP.UpdateBlockades` | F (guarded `== 17`) | 6 Sea Raiders, 5 Blockading Squadron, 8 Blockade Busters | blockade, prices | OK |
| 106280, 107691 | `ObstacleLine` / `Pontoons.IncreaseConstruction` | B | 0 Engineering | build speed | OK |
| 108197 | `Projectile.DestroyBullet` | B artillery | 14 Close Support | canister | OK |
| 113162 | `Regiment.Initialize` | B, HQ | 0 | `hasengineers` | HQ with Siege Train gets `hasengineers`; no engineering-point consumer counts HQs (87428 needs `unittyp == 0`, 22566 skips `> 13`). BUG-AI-009, fixed by `BattleHqPerkPatch` |
| 113255-113259 | `Regiment.CheckAbilityToRun` | B, HQ | 2 Zouave, 11 Stormers | may run | **Mismatch** on HQ (Balloon Corps / Land Torpedoes), BUG-AI-009. Fixed by `BattleHqPerkPatch` |
| 113743-113749 | `Regiment.UpdateRaids` | G | 13 Bushwhackers | raid support | OK |
| 113790, 113813 | `Regiment.UpdateBlockades` | G, F | 6 (no `== 17` guard) | trade-node raid targets, sea-raider XP | Mismatch on G and B: Foot Cavalry / Ace of Spade act as Sea Raiders next to a trade node, BUG-AI-008. Fixed by `SeaRaidersBlockadePatch` |
| 114146-114171 | `Regiment.UpdateIIPSupplyLink` | G (fleets return at 114130-114133) | 9 Pontoon Train, 5 Flying Column | supply | OK |
| 115113-115120 | `Regiment.UpdateCampaignUnitStats` | **B** regiment of a group | **8** (Expert Scouts) | `woundedtokilledambulancecorps` | **Mismatch** BUG-AI-005. Fixed by `CampaignGroupPerkPatch` |
| 115155-115162 | `Regiment.UpdateCampaignUnitStats` | **B** | **6** (Ace of Spade) | `attritionfootcavalry` | **Mismatch** BUG-AI-005. Fixed by `CampaignGroupPerkPatch` |
| 115122, 115370-115376 | `Regiment.UpdateCampaignUnitStats` | `groupunit` (G) | XP 8; XP 3, 4, 16; 11 | level-up | OK (XP goes to the group) |
| 118341 | `Regiment.GetFireRange` | B | 1 Sharpshooters | range | OK |
| 119185-119211 | `Regiment.FireBullet` | B | 10, 15, 16, 1, 11 | accuracy | OK |
| 119643-119665 | `Regiment.UpdateFiringAndReloading` | B | 10, 13, 18, 14, 3, 6 | reload | OK |
| 120594-120624 | `Regiment.UpdateGroupStrength` | G (`> 13`, `!= 17`, stationary) | 2, 3, 4, **16 Expert Scouts** | intelligence gathering | OK |
| 120631 | `Regiment.UpdateGroupStrength` | F | 3 Balloon Barge | intelligence | OK |
| 122953 | `Regiment.CheckIfEnemyIsRevealed` | B scouts (`unittyp == 4`) | XP 8 | Expert Scouts level-up | OK |
| 125051-125065 | `Regiment.AddOrderCourierline` | G, HQ | 1 Field Telegraph, 2 Balloon Corps | order delay | OK |
| 126308-126318 | `Regiment.CalculateMeleeStrength` | B | 4, 9, 14 | melee | OK |
| 126454 | `Regiment.SufferCasualties` | B | 5 Deadly Volley | lethality | OK |
| 128237-128659 | `Regiment.UpdateMorale` (returns for `> 13`) | B | battle ids | morale | OK, except 128617 `HasPerk(8) && GetPerkLevel(9) >= 2`: one battle slot, so never true. Intended Cold Steel III (see below), BUG-AI-010. Fixed by `ColdSteelMoralePatch` |
| 129990 | `Regiment.CalculateCohesion` (returns for `> 13`) | B | 7 Iron Discipline | cohesion | OK |
| 130421-130427 | `Regiment.AddPath` | G | 9 Pontoon Train | small-river cost | OK |
| 132665 | `Regiment.UpdateFatigue` | B, HQ | 2 Zouave | fatigue | A campaign unit or HQ with Balloon Corps gets the Zouave fatigue factor, BUG-AI-009. Fixed by `BattleHqPerkPatch` |
| 133359 | `Regiment.RegimentMove` | B artillery | 17 Flying Battery | speed | OK |
| 133369-133396 | `Regiment.RegimentMove` | G (`!= 17`) | 4, 5, enemy 11 | speed | OK |
| 133405 | `Regiment.RegimentMove` | F | 2 Supply Colliers | speed | OK |
| 133418-133425 | `Regiment.RegimentMove` | G (`!= 17`) | **8 Ambulance Corps** | `speedreductiondisabledsoldiersambulancecorps` | OK (Ambulance Corps speed works) |
| 133484-133490 | `Regiment.RegimentMove` | G | 17 River Expedition | river speed | OK |
| 133518-133524 | `Regiment.RegimentMove` | G, **F** | 6 | `forcedmarchspeedfootcavalry` | **Mismatch** BUG-AI-008: no `!= 17` guard (Sea Raiders fleets get it) and not gated on forced march (applies to every campaign move). Fixed by `RegimentMovePerkPatch` |
| 133734-133743 | `Regiment.RegimentMove` | B, HQ | 2 Zouave | running sound | HQ with Balloon Corps plays the Zouave running sound, BUG-AI-009. Fixed by `RegimentMovePerkPatch` |
| 123411 | `Regiment.ChoosePerk` | B, HQ | 0 | `hasengineers` | As 113162 when an HQ picks Siege Train in battle, BUG-AI-009. Fixed by `BattleHqPerkPatch` |
| 134066 | `Regiment.RegimentMove` | F | 9 Battery Runner | fort bombardment exit | OK |
| 134115-134118 | `Regiment.RegimentMove` | G, **F** | XP 5, XP 6 | level-up | Fleets earn Blockading Squadron / Sea Raiders XP for marching, BUG-AI-008. Fixed by `RegimentMovePerkPatch` |
| 138518-138526 | `Regiment.GetCoverValueFromID` | B, HQ | 13, 18, 6 | cover | HQ with Bushwhackers / Foot Cavalry gets a cover bonus, BUG-AI-009. Fixed by `BattleHqPerkPatch` |
| 142823-142841 | `SpottedEnemy.UpdateMarks` | G (`!= 17`) | 1 Field Telegraph, 3 BMI | information delay | Table OK |
| 144429, 144984 | `Vessel` | F | 2 Supply Colliers, 9 Battery Runner | consumption, fort fighting | OK |

AI perk choice: battle `AIBattle.CheckPerkSelection` (5408) picks from the right battle list per unit type; campaign `AICampaign.CheckPerkSelection` (11871) draws `Random.Range(0, count - 1)` so the AI never takes campaign 17 (River Expedition) or fleet 10 (Port Defender). Whiskey's #7 `PerkSelectionPatch` mirrors that window and uses campaign/fleet ids correctly (`Strategic/PerkSelectionScorer.cs`).

## Ambulance Corps (campaign 8)

Tooltip (`PerkTooltipsArmyCampaign.txt` 100-110): speed reduction due to wounded -25/50/75%, mortality of wounded -10/25/50%.

- **Speed: works.** `RegimentMove` 133415-133431 on a campaign group (`calledfromcampaign && unittyp != 17`): `num12 = groupwounded / (groupstrength + grouplosses)`, times `speedreductiondisabledsoldiersambulancecorps[level]` (campaignprefs.txt 2428-2433: 0.75/0.50/0.25) for the group and its parent army, then `speed *= woundedspeedadjustment × num12 + (1 − num12)`. The wounded share shrinks by 25/50/75%, as promised.
- **Mortality: broken in vanilla, fixed by `CampaignGroupPerkPatch`.** `UpdateCampaignUnitStats` runs on each regiment (`array[m].UpdateCampaignUnitStats(this, …)`, 120409) and calls `HasPerk(8)` / `HasPerkParent(8)` on the regiment (115113-115120). The regiment's 8 is battle Expert Scouts and `HasPerkParent` is false for it, so `woundedtokilledambulancecorps` (2434-2439: 0.9/0.75/0.5) was applied to Expert Scouts cavalry and never to an Ambulance Corps group. The Ambulance Corps did still level up (XP to `groupunit`, 115122). With the patch the regiment's out-of-battle wounded mortality is multiplied by the group's own and parent army's factors.
- Before the fix the campaign Ambulance Corps also widened the group's sight (BUG-AI-003) and concealed it (BUG-AI-004).
- Battle side: there is no battle medical perk (`PerkBattleTooltips.txt` has none). The campaign perk is not used in battle: battle casualties do not read it, and the HQ that carries it into battle only picked up the wrong sight/concealment bonuses, now removed.

## Expert Scouts

- **Campaign (16)**: tooltip promises intelligence gathering +25%/+50%/doubled (`PerkTooltipsArmyCampaign.txt` 196-206). `UpdateGroupStrength` 120618-120624 multiplies `groupintelligencegathering` by `intelligencegatheringexpertscouts` (campaignprefs.txt 2502-2507: 1.25/1.50/2.00) for the group and its parent, inside `regimentpaths <= 0` (stationary groups only; the same holds for Balloon Corps, BMI and Cartographers). XP: `groupunit.IncreasePerkExp(perkexpincreasescoutingcampaign × dt, 16)` while `groupunit.cavalryorders == 1` (115368-115372), once per regiment per update. Works. There is no campaign sight or concealment pref for it, so the fix adds none.
- **Battle (8)**: tooltip promises concealment +10/25/50% and visibility +25/50% at II/III (`PerkBattleTooltips.txt` 100-110). Concealment: `UnitIsHidden` 101211-101213 (`perk_expertscoutsconcealment`, unitprefs.txt 1781-1785: 10/25/50). Sight: `UpdateViewRange` 101298-101301 (`perk_expertscoutsvisibilityrangefactor`, 1787-1791: 1.0/1.25/1.5). Level-up: `CheckIfEnemyIsRevealed` 122953 gives XP only to a detached scouts unit (`unittyp == 4`); the detachment copies the parent's perk (90444) and `ReattachCompany` merges the XP back strength-weighted (90582). Works; untouched by the fix.
- **W&L**: incident 63 (Scouting Ahead) multiplies the W&L player's command's sight by `dlcwl_incidentscoutingaheadrange` (101307-101310) independent of perks; incidents 60/61/64 make every unit visible (101183); `hqinthesaddle` zeroes battle sight outside the player's command (101312). W&L perk interactions: `Camp.battleperkmodifier` scales battle perk XP for the player's command (123293-123296), prestige counts active perks (44078), and the AI never chooses perks for the player's command (11879). None reads ids, so the fix changes nothing W&L-specific; the player's corps gets the same tooltip-faithful rule as every other side.

## Fix

`VanillaPerkTables` holds the rules. First wave (BUG-AI-003..005), behind `Campaign / Fix Perk Id Mismatches`:

- `FogPerkSightPatch` (Prefix + Finalizer on `FogOfWar.UpdateViewRange`): the factor is a local inside the per-unit loop, so a Postfix cannot fix it and the transpiler option was not needed. The Prefix points the perks list of every non-battle-table viewer holding id 8 at an empty list for the call; the Finalizer restores each list (only if still the patch's), even if vanilla throws. `UpdateViewRange` reads nothing else from perks. A reflection failure (`FogOfWar.units`, `FOWUnit.reg`) logs once and leaves vanilla.
- `FogPerkConcealmentPatch` (non-skipping Prefix on `FogOfWar.UnitIsHidden`): `regref` is read only for perks 3/8, so for a non-battle-table unit holding 3 or 8 the Prefix passes `null`; vanilla then computes the same result without the wrong bonus.
- `CampaignGroupPerkPatch` (Prefix + Finalizer on `Regiment.UpdateCampaignUnitStats`): for a battle-table regiment, swaps in a perks list holding only 8/6 at level 0 and points `GamePrefs.woundedtokilledambulancecorps` / `attritionfootcavalry` at arrays filled with the group's own × parent factor (vanilla's own HasPerk × HasPerkParent rule). Nothing else in that call reads those. Finalizer restores all three.

Second wave (user: "fix all of this correctly", 2026-10-15). Intent first, from tooltips, campaignprefs labels and the neighbouring code; every fix is behind a flag and a pattern miss leaves vanilla:

- **BUG-AI-006, BUG-AI-007 — `AutocalcPerkIdsPatch`** (Transpilers on `Autocalc.FightUnit` and `Autocalc.UpdateFatigue`). The autoresolve tooltip line is "-", but the battle meaning is unambiguous: `sharpshooterperkinfluenceautocalc` belongs to Sharpshooters (1), the high-fatigue morale immunity and `perk_zouaves_fatiguefactor` to Zouave (2; battle `UpdateMorale` 128659, `UpdateFatigue` 132665), and `fightingstrengthembarkedriverexp` to River Expedition (17; campaignprefs.txt 1363 "has perk river expedition selected"). The same methods read the right ids on the same units elsewhere (siege-assault `HasPerk(10)` 21100, `IncreasePerkExp(-2)`), so no perks swap can reach them: each wrong constant is passed through a remap hook. The embarked site is told from the siege one by the `fightingstrengthembarkedriverexp` load that follows it.
- **BUG-AI-008 — `RegimentMovePerkPatch`** (Transpiler on `Regiment.RegimentMove`) and **`SeaRaidersBlockadePatch`** (Prefix + Finalizer on `Regiment.UpdateBlockades`). Foot Cavalry's tooltip and campaignprefs.txt 2402 say "forced march speed"; vanilla's forced march is `doublequick | running` (sickness 115153, Foot Cavalry XP 134116), set at 133497-133516 before the speed read, so the speed now needs a land unit (ids) and a forced march (scope). Fleets no longer earn Flying Column / Foot Cavalry XP for marching (their 5/6 level up by blockading). `UpdateBlockades` reads only perk 6, so a non-fleet unit holding 6 has its perks hidden for that call.
- **BUG-AI-009 — `BattleHqPerkPatch`** (Postfixes on `CheckAbilityToRun`, `Initialize(bool)`, `ChoosePerk`; Prefix + Finalizer on `UpdateFatigue` and `GetCoverValueFromID`) plus the sound sites in `RegimentMovePerkPatch`. Rule: a battle-id read applies only to a unit on the battle table (`VanillaPerkTables.UsesBattleTable`). The Postfixes reset `isabletorun` / `hasengineers` to what vanilla computes without the perk; the two local factors are reached by hiding the perks for the call (each method reads no other perk).
- **BUG-AI-010 — `CommanderFameReportersPatch`** (Transpiler on `BattleUnits.CalculateCommanderFame`) and **`ColdSteelMoralePatch`** (Transpiler on `Regiment.UpdateMorale`). Embedded Reporters' tooltip ("reporters follow the unit … deeds of the leaders better known") makes the unit itself the first holder; then vanilla's parent search; the factor uses the holder's level. For 128617: every neighbouring line is `HasPerk(X) && GetPerkLevel(X)`, the level read is 9, and the block is charge determination (enemy Cold Steel / Spirited Charge feed it at 128589-128598), so the intended pair is Cold Steel III (9, 9); Expert Scouts has no melee role. Neither tooltip lists this immunity, so it is recorded as the code's evident intent, not a tooltip promise.

Patch types: Transpilers only where the wrong read is a local or a mid-method call that also sits next to correct reads (user-approved via the coordinator, 2026-10-15); Postfix where the result is a field; Prefix + Finalizer perks swap where the method reads no other perk. The IL edits live in `Patches/PerkIdIlRewrites.cs` (pure; the harness runs them on the real `refs/Assembly-CSharp.dll` IL and checks that a moved or missing site leaves the whole method unchanged); the hooks the IL calls are in `Patches/PerkIdIlHooks.cs`. IL validity: the same edits, applied to a Mono.Cecil copy of the game assembly at the same instruction positions with calls into the real hooks, pass ILVerify for all five methods, and a deliberately wrong hook fails it (StackUnderflow); HarmonyX itself cannot be run under .NET 8 here, so Harmony's own emit is checked only in game (`docs/findings.md` § Perk id tables).

`RegimentMove` cost: no Prefix or Postfix on it. The six edited calls run only where vanilla already made them (the campaign speed/XP path of moving units, the battle running-sound path); each hook adds one pooled `TelemetryPerf` scope (no allocation), one config read and a unit-type compare to vanilla's own `HasPerk` / `IncreasePerkExp`. In-game cost shows as PerformanceAggregate rows for `campaign.patch.perk-ids.regiment-move.foot-cavalry`, `.foot-cavalry-parent`, `.march-exp` and `tactical.patch.perk-ids.regiment-move.zouave-sound` (`docs/telemetry.md`).

Flags: `Campaign / Fix Perk Id Mismatches` (wrong-table ids: 003-009, 010 Cold Steel) and `Campaign / Fix Perk Effect Scope` (Foot Cavalry forced-march gate, Embedded Reporters holder and level). Both default on.

## Smoke

Startup (the transpilers run at plugin load): `[first-fire] campaign.patch.perk-ids.autocalc.fight-unit.transpiler`, `…autocalc.fatigue.transpiler`, `…regiment-move.transpiler`, `tactical.patch.perk-ids.commander-fame.transpiler`, `tactical.patch.perk-ids.cold-steel-morale.transpiler`, each followed by `[once:perk-ids:il:<method>] … perk-id sites rewritten`. A `[Patch:PerkIds] <method>: IL pattern not found` warning means that method runs vanilla (game update) — report it.

Campaign session with the flag on. Expected first-fire lines: `[first-fire] campaign.patch.perk-ids.fog-sight.prefix`, `.fog-sight.finalizer`, `campaign.patch.perk-ids.fog-concealment`, `campaign.patch.perk-ids.group-perks.prefix`, `.group-perks.finalizer`. Once a group with Ambulance Corps (or a fleet with Blockade Busters) exists: `[once:perk-ids:fog-sight] … Expert Scouts sight withheld …`; once a regiment of a group with Ambulance Corps / Foot Cavalry, or an Expert Scouts / Ace of Spade regiment, updates: `[once:perk-ids:group-perks] …`. Campaign: `[first-fire] campaign.patch.perk-ids.sea-raiders.prefix` / `.finalizer`; battle: `tactical.patch.perk-ids.hq.ability-to-run`, `.hq.engineers-init`, `.hq.fatigue.prefix` / `.finalizer` (`.hq.cover.*` and `.hq.engineers-choose` are situational). No `[Patch:PerkIds] … failed` warnings.

On the campaign map: give one of your corps Ambulance Corps. Its fog-of-war reveal circle no longer grows with the perk level (vanilla: ×1.25 at II, ×1.5 at III), and enemy units with Ambulance Corps / BMI are no longer harder to spot. Its regiments' wounded die more slowly out of battle (wounded count in the unit panel recovers faster, fewer losses to attrition), and a Foot Cavalry corps on forced march loses fewer men to sickness. Expert Scouts cavalry in battle keep their sight and concealment.

Second wave, what to watch: a corps with Foot Cavalry moves at normal speed until ordered to forced-march (then +10/20/30%), and a fleet with Sea Raiders is not faster on the campaign map; fleets' Blockading Squadron / Sea Raiders experience bars no longer fill from just sailing; autoresolved battles with Sharpshooter and Zouave regiments, or a River Expedition corps fighting embarked, follow those perks; a battle division/corps/army HQ with Balloon Corps or Land Torpedoes no longer runs; a corps or army whose own unit has Embedded Reporters now gets the fame bonus after a battle (`[once:perk-ids:fame-self]`).
