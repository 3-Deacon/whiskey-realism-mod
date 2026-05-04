# Formation Directive Design

Date: 2026-05-04
Status: design ready for implementation planning
Scope: Slice A enrichment. Land campaign formations only: divisions, corps, armies, and army groups. This does not open tactical-battle AI, battlefield skirmisher micro, naval AI, policy steering, or recruitment steering except as later consumers/producers of the same strategic posture.

## Why this exists

Whiskey Realism now has historical operating-area steering and army-group steering, but the current implementation is too coarse for the campaign map. The game can field independent divisions, corps, and armies as campaign-map formations. Our #15/#16 steering currently sees corps and armies but excludes top-level divisions, which means early-war independent divisions are not first-class strategic actors.

The AI needs to use formations with purpose:

- a division can guard, screen, probe, or delay;
- a corps can hold a sector, reinforce, or counterstroke;
- an army can mass for a theater objective or hold a major line;
- an army group can coordinate adjacent top formations without micromanaging every division.

The core rule is that small formations should not be mindlessly sent into hopeless attacks. A lone division should not attack a 50,000-man army unless local friendly support, terrain, morale, commander intent, and strategic reward make the operation plausible. Otherwise it should screen, fall back, guard a key point, or request reinforcement through vanilla transfer/defensive-operation surfaces.

## Historical findings

Civil War formations were hierarchical but not static. Divisions were several brigades; corps were several divisions; armies were multiple corps; command sizes and titles varied between sides and over time. Confederate divisions were often larger than Union divisions, so "formation level" must be combined with actual strength, morale, readiness, and commander context.

Early-war command was fragmented. Both sides improvised structures, mixed arms, and used small or semi-autonomous commands. This supports early-game behavior where divisions and corps often operate independently, especially along the Virginia frontier, Shenandoah Valley, Missouri/Arkansas, Kentucky/Tennessee, the coast, and the Mississippi approaches.

The armies professionalized as the war continued. By 1863, artillery and cavalry were increasingly centralized instead of being scattered as small attachments. Cavalry shifted from local courier/scout use toward coordinated reconnaissance, raids, screens, and pursuit. This supports later-game behavior where corps/armies do more massing and army groups coordinate adjacent top formations, while divisions become subordinate maneuver/local-defense pieces instead of independent strategic wanderers.

Union grand strategy evolved from blockade, Mississippi control, Richmond pressure, and coastal operations into 1864 simultaneous pressure on all Confederate armies. In game terms, Union formations should become more willing to mass corps and armies across theaters as era/stage, research, strength, and leadership improve.

Confederate strategy centered on independence and survival. Davis leaned defensive and attempted a broad cordon defense that posted smaller armies across vulnerable fronts, but this overstretched Confederate resources. Confederate AI should defend Richmond/Virginia, Tennessee/Georgia, the Mississippi, ports, and the Trans-Mississippi with historical weighting, but should explicitly choose when to thin or concede a sector instead of accidentally stripping it.

Sources:

- National Park Service, "Army Structure": https://www.nps.gov/kemo/learn/historyculture/army-structure.htm
- National Park Service, "From Regiment to President": https://www.nps.gov/articles/from-regiment-to-president-the-structure-and-command-of-civil-war-armies.htm
- National Park Service, "The Military Experience": https://www.nps.gov/articles/the-military-experience.htm
- Britannica, "The military background of the war": https://www.britannica.com/event/American-Civil-War/The-military-background-of-the-war
- American Battlefield Trust, "Civil War Army Organization": https://www.battlefields.org/learn/articles/civil-war-army-organization
- U.S. Army Center of Military History, "The Civil War in the Trans-Mississippi Theater, 1861-1865": https://history.army.mil/Publications/Publications-Catalog/The-Civil-War-in-The-Trans-Mississippi-Theater/

## Vanilla game findings

The decompile is authoritative for campaign formation levels:

- `unittyp <= 13`: regiment/brigade/battery-level tactical or lower campaign units.
- `unittyp == 14`: division-level campaign group.
- `unittyp == 15`: corps-level campaign group.
- `unittyp == 16`: army-level campaign group.
- `ArmyGroup`: W&L/top command coordination object stored separately in `BattleUnits.armygroups`.

This supersedes the stale mapping in `docs/superpowers/plans/2026-05-03-historical-army-areas-implementation.md`, which described `14/15/16` one level too low. The later grand-strategy spec and decompile agree with the mapping above.

Primary decompile anchors:

- `AICampaign.Update()` job sequence at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:11159`
- `AICampaign.RaiseNewCampaignGroup(...)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:12940`
- `AICampaign.GrabExistingGroup(...)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:12739`
- `AICampaign.GrabSubordinateType(...)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:12854`
- `AICampaign.UpdateCampaignTheaters(int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:17034`
- `AICampaign.CheckCombinationOfUnits(int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:17112`
- `AICampaign.GrandArmyStructure(int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:17166`
- `AICampaign.CheckForDefensiveOperations(int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:13505`
- `AICampaign.CheckOffensiveMovements(int, Regiment, float)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14166`
- `AICampaign.CheckTransferOfUnits(int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:17232`
- `AICampaign.CheckCombinationOfBrigades(int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:17615`
- `AICampaign.CheckArmyGroupManagement(int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:17705`
- `BattleUnits.CheckBattleParticipation(...)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:80414`
- `BattleUnits.EstablishCampaignBattle(...)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:80526`
- `BattleUnits.MoveUnitsToBFLocation(...)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:94038`
- `Regiment.CheckEnemyContactCampaign()` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:115560`
- `Autocalc.StartSkirmishing(...)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:20598`
- `Autocalc.EnvolvedUnits.GetTotalStrength(...)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:18514`
- `Autocalc.UpdateLandBattleCycle(...)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:20648`
- `Autocalc.GetROF(...)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:21060`
- `Autocalc.FightUnit(...)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:21084`
- `Autocalc.CheckWithdrawal(...)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:20922`
- `Autocalc.FinishLandBattle(...)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:21735`
- `CampaignArmyPanel.GetReadinessStep(...)` tooltip behavior at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:173590`

Relevant vanilla behavior:

- `RaiseNewCampaignGroup` creates/fills divisions and corps, and creates full army/corps/division hierarchy when `GrandArmyStructure(alliance)` is true.
- `GrandArmyStructure` is gated by researched project ID `90`.
- `CheckCombinationOfUnits` moves large independent corps into nearby or newly created army structures once conditions are met.
- Movement is not parent-only. Vanilla iterates `AIFaction.ownunits`, evaluates eligible campaign groups, and sends individual formations through `AICampaign.MoveUnitTo(...)`.
- `UpdateCampaignTheaters` gives formations a loose `theaterposition`; offensive, defensive, transfer, and army-group logic consume that theater boundary.
- `CheckForDefensiveOperations` already uses probability, winter, owned-territory threat, theater, morale, readiness, weather, supply, local strength, and commander/aggro gates before reacting to enemy threats.
- `CheckOffensiveMovements` already builds local force packages from area strength, morale, weapon-strength, nearby support, commander initiative, and dominance thresholds before attacking.
- `CheckArmyGroupManagement` creates or attaches army groups from nearby top units with theater positions. Vanilla checks `istopunit`, non-garrison status, `theaterposition`, and `groupstrengthdirect > 1000`; it does not restrict this to corps/armies. The create/pair pass also requires physical range below `GamePrefs.aiarmygroupmaxrange` and theater-position range below `GamePrefs.aiarmygroupmaxtheaterrange`.
- `CheckBattleParticipation` pulls nearby friendly campaign units into a battle if they are inside `commanderrange` for normal field battles or `buglerange` for siege warfare, have sufficient morale, are not retreating/in battle, are not fort garrisons, match land/naval category, and can reach through terrain restrictions.
- For corps-level callers (`unittyp == 15`), `CheckBattleParticipation` can also pull in the parent formation and sibling corps under the same parent when they are inside the parent's `commanderrange`. Independent divisions (`unittyp == 14`) and armies (`unittyp == 16`) do not receive that parent/sibling fallback through this method.
- `MoveUnitsToBFLocation` marks participating units as `participatinginbattle`/`inbattle`, stops their current movement, records battle-trigger references, and paths them toward the battlefield.
- `Regiment.CheckEnemyContactCampaign` can start real campaign-map skirmishing during retreat/withdrawal. The trigger is inside `if (onretreat)`: if a retreating non-fleet, non-garrison land unit with an active path remains close to a non-retreating enemy inside `enemy.buglerange * GamePrefs.rangefactorskirmishing`, vanilla starts an `Autocalc` skirmishing engine. It does not require `lastretreatiswithdrawal` when the unit is out of battle cooldown.
- `Regiment.CheckEnemyContactCampaign` also starts vanilla raiding when `cavalryorders == 2`, no raid engine is running, and the unit is not already in battle.
- `Autocalc.StartSkirmishing` creates an autocalc engagement of type `6`; the skirmish engine applies casualty/morale effects until range breaks or neither top unit remains in retreat.
- `Autocalc.EnvolvedUnits.GetTotalStrength(usefirepower: true)` weights unit strength by weapon firepower, artillery gun/firepower ratios, and fort weapon strength.
- `UpdateLandBattleCycle` only puts infantry/cavalry-style fighting units into the active line when they are present, not routed, arrived, not withdrawn, and have ammo. Artillery actions require non-routed artillery with ammunition.
- `FightUnit` uses rate of fire, weapon firepower, stance pairings, weather, experience, cohesion, fatigue, defender cavalry/cunning effects, commander/perk modifiers, fort/terrain/embarkation factors, and elapsed time to produce casualties.
- `FightUnit` consumes small-arms or artillery ammunition and supply stock, applies morale penalties for low/out-of-ammo, high fatigue, low cohesion, flanking, and unbearable losses, and can rout sub-units.
- `CheckWithdrawal` ends battles based on points/force performance, reinforcements still to arrive, day/night, and commander initiative.
- `FinishLandBattle` persists losses, morale, supply averages, service history, weapons captured, commander fame/defame/KIA, national morale effects, and state-support casualty effects.

Current Whiskey gap:

- `ArmyAreaRuntime.IsTopStrategicUnit` includes only `unittyp 15..16`, excluding independent divisions.
- `ArmyGroupManagementPatch.IsEligibleTopUnit` also includes only `unittyp 15..16`.
- `HistoricalArmyAreaRegistry` mainly maps army names, so fallback divisions/corps get weak historical identity.
- `TheaterCommander` remains mostly a persistence/planning scaffold and is not yet the live runtime actor for per-formation behavior.

Design consequence:

Whiskey should not invent a parallel formation engine. Vanilla already supports independent divisions, corps, and armies moving, supporting nearby battles, withdrawing, and skirmishing. The mod should classify and weight formations, then use vanilla's movement, support, battle, transfer, and skirmish systems more intelligently.

## Design goal

Add a weekly `FormationDirectiveLedger` that classifies top campaign formations, assigns historically weighted directives, and exposes read-only steering signals to Harmony patches.

The ledger does not replace vanilla command movement. It provides bounded guidance to existing vanilla surfaces:

- historical theater position and return-area corrections,
- defensive operation eligibility and priority,
- offensive operation risk gates,
- transfer-budget protection,
- army-group coordination,
- later recruitment/policy/naval integrations through grand-strategy posture.

Patches remain read-only with respect to strategic mod state. `StrategicCoordinator` builds or refreshes the ledger during weekly strategic review. Harmony patches consume the current ledger and may steer vanilla fields/orders only inside their own patch surface.

## Formation model

`FormationSnapshot` should capture:

| Field | Source |
|---|---|
| `UnitKey` | stable local key from unit name + commander + instance fallback |
| `AllianceId` | `AIFaction.allianceid` |
| `Level` | `Regiment.unittyp`: division/corps/army |
| `IsTopUnit` | `Regiment.istopunit` |
| `ParentLevel` | transform parent / attached hierarchy where available |
| `AssignedArmyGroupKey` | `Regiment.assignedarmygroup` |
| `CommanderId` / name | `Regiment.commander`, `GameVars.commander` |
| `AreaKey` | `ArmyAreaRuntime.AreaKey(position)` |
| `SectorKey` | `FrontSectorRuntime.SectorKey(position)` |
| `TheaterPosition` | `Regiment.theaterposition` |
| `Strength` | `groupstrengthactive` for combat posture; `groupstrengthdirect` for vanilla eligibility checks |
| `Morale` | `groupmorale` |
| `Readiness` | `Regiment.readiness` primary; `CampaignArmyPanel.GetReadinessStep` only if safely callable on the campaign thread |
| `Supply` | `groupsupplystate` / supply depots where safely readable |
| `Ammo` | `groupammo`, `ammo[]`, `groupsupplystate[0]` rifle ammo, `groupsupplystate[1]` artillery ammo |
| `Provisions` / `Forage` | `groupsupplystate[2]`, `groupsupplystate[3]` |
| `Fatigue` / condition | `groupfatigue`, `groupcondition`, child `fatigue`, `conditionmen`, `conditionhorses` where safely readable |
| `Weapons` / firepower | child `weapon`, `GameVars.weapon[weapon].firepower`, `groupstatsguns`, `groupstatsgunsactive` |
| `CommandRange` | `Regiment.commanderrange` |
| `BugleRange` | `Regiment.buglerange` |
| `InBattle` / retreat / pathing | `inbattle`, `onretreat`, `regimentpaths` |
| `LocalEnemyStrength` | nearby enemy units / `AIArea` strength when available |
| `LocalFriendlySupport` | nearby friendly top formations and subordinates inside support range |
| `CanReachSupport` | cached terrain-restricted reachability for the nearest plausible supporters only |

The directive ledger should not compare headcount alone, but it also should not pretend to replace vanilla's battle solver. Use two scores:

```
CombatAvailability =
    groupstrengthactive
  * MoraleGate
  * ReadinessGate
  * SupplyAmmoGate
  * FatigueConditionGate

ExchangePressure =
    sqrt(max(1, ActiveStrength))
  * sqrt(max(1, WeaponFirepower))
  * MoraleFactor
  * ReadinessFactor
  * SupplyAmmoFactor
  * FatigueConditionFactor
  * CommanderFactor
```

`CombatAvailability` is for posture and eligibility. `ExchangePressure` is for attack/counterstroke risk checks and should mirror vanilla's square-root strength/firepower shape closely enough to avoid overvaluing very large but depleted formations. Defensive comparisons should also respect vanilla's morale exponent behavior where the relevant `GamePrefs` value is available. Exhausted, hungry, low-ammo, low-readiness formations should not be treated as full-strength simply because their paper manpower is high.

These scores are advisory gates and weights. They must steer existing vanilla eligibility/scoring surfaces, not become a parallel campaign-combat model that overrides `CheckForDefensiveOperations`, `CheckOffensiveMovements`, or `Autocalc`.

`FormationLevel`:

- `Division`
- `Corps`
- `Army`
- `ArmyGroup`

`FormationDirective`:

- `Hold`: remain in assigned area/sector; favor defense; do not donate below minimum budget.
- `Screen`: cover a front or approach; avoid decisive engagement against superior force.
- `Delay`: trade space for time through controlled withdrawal, rearguard action, and supported skirmishing without seeking decisive battle.
- `Guard`: protect rail, town, port, supply depot, crossing, capital approach, or objective anchor.
- `Probe`: limited forward movement only when enemy strength is low/unknown and support is close.
- `Reserve`: stay behind the front, available for transfer/counterstroke.
- `Reinforce`: move toward a threatened friendly sector or parent formation.
- `Counterstroke`: local attack/reaction allowed because risk gates pass.
- `Mass`: concentrate for CIC plan target or major theater objective.
- `RaidSupport`: cavalry/fast/small-force support for raid logic without committing the main line.
- `Recover`: avoid new offensive/defensive commitments until morale/readiness improves.
- `Concede`: allow thinning or withdrawal because CIC/front ledger explicitly chose economy-of-force or abandonment.

Support should be measured in vanilla terms first. A formation is not "supported" merely because another friendly formation is in the same named theater. Support means nearby, eligible friendly combat power inside command/battle participation range or close enough to be transferred/reinforced before decisive contact.

## Directive rules by level

### Division

Independent top-level divisions are local actors, not miniature armies. The discriminator is not `unittyp == 14` alone. A direct division directive applies only when:

- `unittyp == 14`;
- `istopunit == true`;
- `garrisonreference == null`;
- `groupstrengthdirect > 1000` for army-group/area behavior that mirrors vanilla's top-unit floor.

Attached subordinate divisions also have `unittyp == 14`, but they inherit parent corps/army posture and must not be independently yanked by area steering.

Independent divisions may receive:

- `Hold`
- `Screen`
- `Delay`
- `Guard`
- `Probe`
- `Reserve`
- `RaidSupport`
- `Recover`
- `Reinforce`

They should receive `Counterstroke` only when all of these are true:

- local friendly effective strength is at least the configured division counterstroke ratio against the target;
- another friendly formation can support inside command/battle participation range, or can reinforce before decisive contact;
- morale and readiness are above threshold;
- the target is not an enemy army/corps-sized concentration unless friendly strength is aggregated;
- the sector is not marked `Hold` with minimum budget already at risk.

They should not receive `Mass` directly unless they are independent and the CIC plan target is nearby. Division risk gates must account for vanilla's support asymmetry: independent divisions can receive nearby command-range support, but `CheckBattleParticipation` does not give them the corps-only parent/sibling reinforcement fallback.

### Corps

Corps are the primary operational maneuver layer. They may receive:

- `Hold`
- `Screen`
- `Delay`
- `Reserve`
- `Reinforce`
- `Counterstroke`
- `Mass`
- `Recover`
- `Concede`

Corps can counterstroke or contest objectives when local ratio gates pass. Corps should also be the normal unit for "defend here, concede elsewhere" choices because they have enough combat power to matter without committing an entire army.

### Army

Armies express theater intent. They may receive:

- `Hold`
- `Reserve`
- `Reinforce`
- `Mass`
- `Counterstroke`
- `Recover`
- `Concede`

Armies should not chase every nearby opportunity. Their directive should mostly set theater position, transfer posture, and offensive/defensive permission for subordinate corps/divisions.

### ArmyGroup

Army groups coordinate adjacent top formations. They may receive:

- `CoordinateHold`
- `CoordinateMass`
- `CoordinateReserve`
- `CoordinateConcede`

Army groups should not issue direct per-division movement. They influence grouping, commander appointment, theater-area commitment, and whether nearby armies/corps share the same strategic posture.

## Risk gates

A directive that permits attack or forward movement must pass a formation-level risk model.

This model must be a pre-filter or weight modifier over vanilla behavior. It must not bypass vanilla `IsUnitAvailableForOffensiveOperations`, `IsUnitAvailableForDefensiveOperation`, theater, weather, readiness, morale, supply, and commander gates.

Required inputs:

- own effective strength: strength adjusted by morale/readiness/supply;
- nearby friendly support: support inside `commanderrange`/battle participation range when possible, then theater radius as a weaker fallback;
- enemy effective strength: enemy group strength and morale/readiness where visible;
- enemy level: division/corps/army estimate from `unittyp`;
- ammo and supply state: rifle ammo, artillery ammo, provisions, forage, and overall supply ratio;
- readiness and movement state: readiness step, pathing, retreat/withdrawal, and whether further operations are legal on hostile terrain;
- fatigue/condition state: fatigue, cohesion/condition if safely available, and whether the formation has just fought or skirmished;
- weapon/firepower state: child weapon firepower and gun strength where available, with artillery not counted as useful if artillery ammo is exhausted;
- terrain/control context: friendly state, enemy state, contested objective, supply/capital/rail/river/port tags;
- commander profile: aggression, caution/casualty tolerance, initiative where available;
- faction/era grand strategy;
- current `FrontSectorLedger` posture and minimum-hold budget.

Default safety behavior:

- Division vs enemy army: block attack; choose `Screen`, `Delay`, `Guard`, `Reinforce`, or `Recover`.
- Division vs enemy corps: allow only with support and favorable ratio.
- Corps vs enemy army: allow only as part of aggregated theater force or defensive counterstroke.
- Army vs enemy army: allow if plan/sector/ratio/commander gates pass.
- Any low-morale or low-readiness formation: prefer `Recover`, `Hold`, or `Guard`.
- Any low-ammo or low-supply formation: prefer `Recover`, `Guard`, or `Delay`; block `Mass`/`Counterstroke` unless the target is critical and support is near.
- Artillery-heavy formations with poor artillery ammunition should not get inflated offensive value from paper gun counts.
- Fresh but outnumbered CSA formations may still `Delay`/`Screen`; exhausted, low-readiness, or low-ammo CSA formations should not be asked to hold contact just because history says "defensive."
- Any sector below minimum hold budget: block donation/attack unless sector is explicitly `Concede`.
- Retreating/withdrawing formations near enemy contact: prefer `Screen`, `Delay`, `Recover`, or `Reinforce` logic that respects vanilla campaign skirmishing risk instead of repeatedly ordering the unit back into decisive contact.

The risk model should be intentionally conservative first. It is better to under-steer and let vanilla act than to create suicidal deterministic behavior.

Terrain reachability is too expensive for unbounded pairwise checks. A weekly snapshot should cache reachability per formation for only the nearest plausible support candidates, or compute it lazily for the top-K supporters used by the risk gate.

## Campaign battle/autocalc state mechanics

Campaign engagements are not resolved from manpower alone.

`Autocalc.EnvolvedUnits.GetTotalStrength` can weight strength by firepower. For infantry/cavalry it reads the active weapon's firepower. For artillery it uses weapon firepower and gun count relative to manpower. For fort/garrison combat it can use fort weapon groups and fort condition.

`UpdateLandBattleCycle` divides units into active fighting, reserve, flanking, and artillery buckets. Infantry/cavalry-style units need ammunition to fight. Artillery needs ammunition to fire. Routed, withdrawn, not-yet-arrived, and non-ammo units are excluded from active fighting. That means a formation's campaign-map "strength" can be strategically misleading if ammo, arrival, readiness, morale, or fatigue are bad.

`FightUnit` then applies:

- rate of fire from stance pairings and weather, reduced by low ammo;
- weapon firepower, artillery firepower by range/guns, and fort/terrain modifiers;
- experience, cohesion, fatigue, cavalry defender cunning, commander/perk modifiers, and night/embarkation effects;
- ammo and supply consumption for the firing unit;
- morale loss from casualties, flanking, low/out-of-ammo, high fatigue, low cohesion, and unbearable losses;
- rout checks when morale cracks or loss thresholds are exceeded.

`CheckWithdrawal` does not just compare raw headcount. It checks battle points, reinforcement arrivals, day/night, and commander initiative before deciding a side withdraws. `FinishLandBattle` then writes durable consequences: casualties, morale/supply averages, weapons captured, commander fame/defame/KIA, national morale, service history, and state-support effects.

Design consequence:

The formation directive ledger must treat readiness, ammo, supply, fatigue/condition, morale, weapons, and support timing as strategic state. A corps with 20,000 hungry men, low readiness, and depleted ammunition is not equivalent to a fresh 20,000-man corps. A smaller CSA division with good morale, ammunition, nearby support, and a high-cunning commander may be useful as a delaying screen even if it should not seek decisive battle.

## Campaign skirmishing mechanics

`Autocalc.StartSkirmishing` is a narrow campaign-map attrition engine for withdrawal and pursuit. It is not a generic battle substitute.

Start conditions verified in `Regiment.CheckEnemyContactCampaign`:

- the withdrawing/retreating unit has an active path (`regimentpaths > 0`);
- the unit is land, not fleet, not garrisoned, and not already in a disqualifying battle state;
- a non-retreating enemy land unit is within `enemy.buglerange * GamePrefs.rangefactorskirmishing`;
- the enemy has no existing `skirmishingcalculationengine`;
- vanilla attaches an `Autocalc` component to the enemy unit and calls `StartSkirmishing(enemy, withdrawingUnit)`.

Cycle behavior verified in `Autocalc.UpdateSkirmishing`:

- it runs on `GamePrefs.cycleupdateforautocalcs`;
- the withdrawing side suffers casualties, morale hits, and supply loss against randomly selected infantry/cavalry/artillery sub-units;
- withdrawing-side casualties scale with elapsed time, sub-unit strength, `GamePrefs.withdrawal_skirmishingcasualties`, readiness weighting, whether the top unit is in withdrawal, commander cunning, and low morale (`1 - morale`);
- the pursuing side can also suffer casualties and morale loss, especially when its selected sub-units have low morale;
- pursuing-side losses are affected by defender guard efficiency and pursuer `groupraidingefficiency`;
- the skirmish ends when range breaks beyond `buglerange * rangefactorskirmishing`, when neither top unit remains in retreat, or when no suitable sub-units remain;
- active skirmishing consumes readiness through `GamePrefs.readinessconsumptionskirmishing`.

Design consequence:

Outnumbered does not mean "always retreat." For the CSA especially, the correct behavior is often `Delay` or `Screen`: keep a force in being, make the stronger Union formation spend time/readiness, use high-cunning/high-morale commanders and cavalry-capable formations for controlled contact, and fall back before the skirmish becomes destructive. A low-morale or exhausted unit should disengage; a fresh, supported, historically defensive formation can deliberately delay.

## Historical weighting matrix

Project ID `90` is the structural hinge for full army hierarchy. Before `GrandArmyStructure(alliance)` is true, vanilla should be expected to operate mostly with independent divisions and corps. The historical weighting tables below describe desired behavior by era, but formation availability must be constrained by that researched hierarchy state: early-war "army" intent may need to express itself as corps/division coordination until project `90` is researched.

### Union

| Era | Formation behavior |
|---|---|
| 1861 amateur | protect Washington, organize around river/coastal objectives, probe Virginia and border states, avoid overcommitting isolated divisions; expect divisions/corps until project `90` enables armies. |
| 1862 operational | corps pressure against Richmond and western river objectives, with armies only after `GrandArmyStructure`; divisions guard rail, depots, and frontier approaches. |
| 1863 decisive | heavier Mississippi/Tennessee/Georgia pressure; corps counterstrokes more common when supported; divisions screen rail and occupation corridors. |
| 1864 total war | simultaneous theater pressure; armies mass; corps maintain pressure; divisions guard logistics, rail, ports, and occupation lines. |

### Confederacy

| Era | Formation behavior |
|---|---|
| 1861 amateur | cordon defense with divisions/corps until project `90`; protect Richmond, Valley, Tennessee/Kentucky, Mississippi approaches, and ports. |
| 1862 operational | offensive-defensive opportunities allowed for strong commanders; divisions screen and delay; corps counterstroke if support exists; armies only after `GrandArmyStructure`. |
| 1863 decisive | preserve armies while contesting key corridors; defend Vicksburg/Chattanooga/Atlanta approaches; raids and probes instead of broad assaults when outmatched. |
| 1864 total war | economy-of-force, entrenched defense, local counterstroke only with favorable odds, protect remaining armies from annihilation. |

## Theater examples

### East / Virginia

- Union Army of the Potomac: army/corps mass against Richmond/Virginia objectives; divisions guard Washington, B&O, and approaches.
- Confederate Army of Northern Virginia: army holds Richmond/Virginia corridor; corps counterstroke locally; divisions screen Valley/coastal/rail approaches.

### Shenandoah / Maryland / Pennsylvania

- Union divisions/corps guard B&O, Harpers Ferry, Winchester, Washington approaches, and later Valley suppression.
- Confederate divisions/corps screen, raid, or threaten northward only when Army of Northern Virginia posture and local risk allow it.

### Tennessee / Georgia / Cumberland

- Union armies/corps pressure Nashville/Chattanooga/Atlanta line as the war matures.
- Confederate corps/armies hold Tennessee/Georgia corridors, counterstroke when favorable, and avoid stripping Atlanta/Chattanooga approaches without explicit concession.

### Mississippi River / Gulf

- Union formations prioritize river control and port/coastal support, with divisions guarding captured logistics and corps/armies massing for Vicksburg/New Orleans-type objectives.
- Confederate formations guard river crossings, ports, and supply corridors; weak divisions screen/delay rather than attack superior river armies.

### Trans-Mississippi

- Both sides use smaller formations and wider autonomy. Directives should tolerate more independent division/corps behavior but use stricter risk gates because support is sparse.

## Integration points

### Weekly coordinator

`StrategicCoordinator` should build `FormationDirectiveLedger[alliance]` after `FrontSectorLedger` and `ArmyAreaLedger`, because directives depend on both:

1. refresh CIC plan/era/profile;
2. refresh front-sector posture;
3. refresh historical army-area assignment;
4. build formation snapshots;
5. resolve directives;
6. log only if the directive signature changed.

### Army-area steering (#15)

Include independent `unittyp == 14` divisions as top strategic units, but do not apply the same return-area behavior to every attached subordinate division. Only top independent divisions should receive direct area movement.

Area movement should respect support range. A division can be nudged back toward a historical area, but not through or adjacent to a superior enemy concentration unless friendly support is close enough to participate or reinforce under vanilla command-range rules. If the safer historical behavior is to screen or fall back toward a nearby depot/rail/town, the directive should prefer that over a direct return-area correction.

### Army-group steering (#16)

Do not create army groups from division spam. Army groups should primarily coordinate armies/corps. Independent divisions can attach only when the group is otherwise valid and the division directive is `Reinforce`, `Reserve`, `Guard`, or `Mass` in the same operating area.

This is intentionally narrower than vanilla's raw `istopunit` army-group eligibility. Vanilla can group any strong top unit, but Whiskey should not create ahistorical or noisy army groups from weak independent divisions. The safe rule is: use divisions as attachments to an otherwise coherent command, not as the primary reason to create the command. Where Whiskey follows vanilla grouping range, it must respect both `aiarmygroupmaxrange` and `aiarmygroupmaxtheaterrange`.

### Defensive operations (#4 / future enhancement)

Directive effects:

- `Hold`, `Guard`, `Screen`: raise defensive eligibility and lower response threshold.
- `Delay`: allow defensive movement or controlled withdrawal when direct battle risk is bad but abandoning the sector would be worse.
- `Reserve`, `Reinforce`: allow commitment if destination risk is higher than source risk.
- `Recover`: block unless capital/critical objective is threatened.
- `Concede`: do not spend scarce units defending the sector unless extraction is impossible.
- `Screen`: may intentionally stay close enough to watch/delay but should avoid triggering decisive battle unless support can join under `CheckBattleParticipation`.

### Offensive movements / counterstroke

Future patch should avoid replacing vanilla offensive logic. Preferred shape is a bounded Prefix/Postfix around candidate scoring or operation eligibility:

- block or down-weight attacks that violate the directive risk gate by steering existing eligibility/candidate weights;
- up-weight `Counterstroke` and `Mass` formations when vanilla already sees a plausible target;
- use AIArea/importance or operation-threshold steering where possible instead of inventing a second attack selector;
- never force a division to attack a superior corps/army by itself.
- treat `commanderrange` and cached/top-K terrain reachability as first-class support checks before treating nearby formations as part of the same attack package.

### Campaign skirmishing

Campaign-map skirmishing is a vanilla system, not a tactical-battle placeholder. Directives should use it indirectly:

- `Screen` and `Guard` can allow controlled contact near friendly support, rail, towns, forts, or river/crossing anchors.
- `Delay` can accept limited skirmish attrition when it preserves a critical sector, buys time for reinforcement, or forces a stronger enemy to spend readiness.
- `Recover` should avoid repeated retreat/skirmish loops by not reissuing aggressive movement to low-morale withdrawing units.
- `Probe` should be limited to cases where the formation can disengage or be supported; otherwise it risks becoming an accidental skirmish or battle.
- `Counterstroke` should consider whether the target is already withdrawing; chasing can create skirmish attrition, which may be desirable for cavalry/strong commanders but bad for exhausted infantry.

This spec does not patch `Autocalc.StartSkirmishing` directly. The first implementation should steer formation posture and movement eligibility so vanilla's skirmish engine emerges from better campaign choices.

### Raids

`RaidSupport` must map to vanilla's existing raid surface rather than a parallel raid engine. The first implementation should treat it as intent to allow or request `cavalryorders == 2` only for formations that are cavalry-capable, not already in battle, sufficiently supplied/readied, and operating in a sector where raiding supports the CIC/front posture. `Regiment.CheckEnemyContactCampaign` then starts `Autocalc.StartRaiding` through vanilla behavior.

### Transfers (#3)

`FormationDirectiveLedger` should feed the existing transfer-budget guard:

- do not strip `Hold`/`Guard` sectors below minimum;
- allow `Reserve` and `Concede` formations as donors;
- prefer moving divisions as reinforcement packets while preserving corps/army command centers;
- prefer corps/army movement only when the CIC plan calls for `Mass` or theater-level `Reinforce`.

### Recruitment / policy / naval

This spec does not implement those surfaces. Later work can use formation directives as demand signals:

- repeated `Guard` gaps create recruitment intent for local infantry/artillery;
- repeated `Screen`/`RaidSupport` gaps create cavalry intent;
- repeated `Mass`/`Reinforce` gaps create logistics/rail/project pressure;
- repeated `Recover` caused by low ammo/supply creates logistics, depot, railroad, and project pressure;
- coastal `Guard`/river `Mass` connects to naval/project strategy.

## Data structures

Proposed pure strategic files:

- `Strategic/FormationLevel.cs`
- `Strategic/FormationDirective.cs`
- `Strategic/FormationSnapshot.cs`
- `Strategic/FormationDirectiveLedger.cs`
- `Strategic/FormationDirectiveRuntime.cs`
- tests in `tests/WhiskeyRealism.Tests/`

`FormationDirectiveAssignment`:

```csharp
public sealed class FormationDirectiveAssignment
{
    public string UnitKey;
    public int AllianceId;
    public FormationLevel Level;
    public string AreaKey;
    public string SectorKey;
    public FormationDirective Directive;
    public string Reason;
    public float OwnEffectiveStrength;
    public float LocalFriendlySupport;
    public float LocalEnemyStrength;
    public float Readiness;
    public float Morale;
    public float Ammo;
    public float Supply;
    public float Fatigue;
    public float WeaponFirepower;
    public bool OffensiveAllowed;
    public bool DefensiveAllowed;
    public bool TransferDonorAllowed;
}
```

The pure ledger should be testable without Unity by consuming `FormationSnapshot` inputs. Runtime extraction should stay in `FormationDirectiveRuntime`.

## Logging

Logging must be bounded:

- `[FormationDirective] alliance=... summary=...` only when weekly directive signature changes or verbose logging is enabled.
- `[Coordinator] operational ledgers deferred until AICampaign factions initialize` once if the immediate campaign-start heartbeat runs before vanilla AI factions exist.
- `[FormationDirective] build skipped: ...` / `[FrontLedger] build skipped: ...` / `[ArmyArea] build skipped: ...` only through `OnceLog.Warning` for runtime extraction boundaries.
- `[Patch:FormationDirective] ... action=block-attack ...` once per unit/directive/target signature when an unsafe action is blocked.
- `[Patch:FormationDirective] ... action=allow-counterstroke ...` only when Whiskey changes vanilla's behavior.
- warnings only for reflection failures, missing fields, or impossible state.

Do not log every formation every tick. Do not log unchanged weekly assignments unless verbose logging is enabled.

Startup sequencing constraint: the first valid campaign date may arrive before vanilla `AICampaign.aifaction` is initialized. CIC heartbeat/objective planning should still log immediately, but `FrontSectorLedger`, `ArmyAreaLedger`, and `FormationDirectiveLedger` must defer until `aifaction` exists. The cadence hook should permit one same-day review once that runtime appears so operational analysis starts immediately instead of waiting a month.

## Tests

Pure tests should cover:

- independent top-level division is included in snapshots and can receive `Screen`/`Guard`;
- independent top-level division classification requires `unittyp == 14`, `istopunit`, and the relevant vanilla strength floor for area/army-group behavior;
- attached division inherits parent posture and is not directly moved by army-area steering;
- division refuses attack against enemy army without support;
- independent division risk is stricter than corps risk because it lacks the corps-only parent/sibling reinforcement fallback in `CheckBattleParticipation`;
- division support uses command-range/proximity inputs, not just same-area membership;
- support reachability uses cached/top-K terrain checks rather than unbounded pairwise terrain-line calls;
- retreating/withdrawing formation near enemy contact prefers `Screen`/`Delay`/`Recover`/`Reinforce` over renewed attack;
- skirmishing test requires `onretreat` plus path/range/enemy gates; it is not a generic pathing-unit trigger;
- CSA outnumbered-but-coherent formation chooses `Delay`/`Screen` instead of automatic retreat when morale/readiness/support permit;
- low-ammo formation blocks `Mass`/`Counterstroke` and prefers `Recover`/`Guard`/`Delay`;
- paper-strength advantage is rejected when readiness, morale, supply, ammo, fatigue, and vanilla-shaped exchange pressure make combat posture unfavorable;
- artillery-heavy formation with low artillery ammo is not scored as full offensive power;
- `groupstrengthactive` is used for combat posture while `groupstrengthdirect` is used for vanilla eligibility floors;
- `RaidSupport` maps to cavalry-capable vanilla raid intent (`cavalryorders == 2`) and does not create a separate raid engine;
- project `90` / `GrandArmyStructure` state prevents early rules from assuming army hierarchy exists;
- corps can counterstroke when supported and ratio gates pass;
- army receives `Mass` for active CIC plan target;
- CSA early profile favors `Hold`/`Screen`/`Guard` over broad offensive action;
- Union 1864 profile tolerates coordinated `Mass` across multiple theaters;
- `Concede` allows transfer donation while `Hold` blocks below minimum budget;
- directive signature logging changes only when assignment changes.

## Non-goals

- No battlefield skirmisher, bridge, cavalry-charge, or tactical stance changes. Those belong to Slice B.
- No direct rewrite of vanilla campaign movement.
- No deterministic historical script that forces exact real-world campaigns.
- No direct mutation of CIC/TheaterCommander state from Harmony patches.
- No global "always attack/always defend" behavior by faction.
- No army-group creation from arbitrary weak divisions just because they share a label.

## Implementation sequence

Recommended first implementation plan:

1. Add pure formation directive model and tests. **Done.**
2. Add runtime snapshot extraction with `unittyp 14/15/16` classification, `istopunit`/parent discrimination, project `90` hierarchy state, readiness, morale, ammo, supply, fatigue/condition, weapon/firepower, `commanderrange`, and `buglerange` inputs. **Done.**
3. Add `StrategicCoordinator.FormationDirectives[alliance]` weekly refresh and bounded summary logging. **Done.**
4. Update #15 army-area steering to include independent top divisions while avoiding attached-division spam. **Done.**
5. Update #16 army-group steering to use directive-aware eligibility. **Done.**
6. Add defensive/screening behavior that respects vanilla campaign battle participation and skirmishing. **Partially done through directive assignments and #15/#16 consumption.**
7. Add an offensive safety gate only after the pure ledger and #15/#16 corrections are verified. **Deferred for more runtime soak; this is Prefix-blocking.**
8. Update docs/handoff and patch catalog after code lands. **Done.**

## Acceptance criteria

- Independent campaign-map divisions are visible to the strategic ledger.
- Attached divisions are not independently yanked away from parent corps/armies.
- Early campaigns do not assume army hierarchy until `GrandArmyStructure` / project `90` is available.
- Division-level attacks against much larger enemy forces are blocked or down-weighted unless support and ratio gates pass.
- Support logic accounts for `commanderrange`, `buglerange`, corps-only parent/sibling support asymmetry, and bounded/cached terrain reachability where vanilla exposes them.
- Engagement logic accounts for readiness, morale, ammo, supply, fatigue/condition, weapon/firepower, and support timing rather than raw headcount alone.
- Campaign-map skirmishing is treated as an intentional risk/opportunity of retreating/withdrawing screening, delay, and pursuit behavior, with `onretreat` as a required vanilla trigger.
- `RaidSupport` uses vanilla cavalry raid intent instead of a new raid engine.
- Outnumbered CSA formations can delay or screen intelligently instead of always retreating, but low-morale or unsupported units still disengage.
- Corps and armies still use vanilla campaign movement surfaces.
- CSA early behavior reads as defensive, screening, and force-preserving unless a favorable opportunity appears.
- Union later-war behavior reads as coordinated pressure rather than isolated local attacks.
- Logs are useful for smoke testing and not spammy.
- Build passes and runtime smoke confirms first-fire markers plus at least one directive summary after a campaign tick.
