# Grand Strategy and Research Tree Design

Date: 2026-05-03
Status: implemented through the post-v0.2.2 main checkpoint for objective/project steering, policy timing, recruitment intent, and role-aware campaign perk steering. Naval runtime movement/construction patches remain design work and should wait for smoke evidence that policy/project steering is insufficient.
Scope: Slice A enrichment only. This is strategic-layer input to CIC planning, policy selection, and project selection. It does not open Slice B tactical behavior.

## Why this exists

Slice A currently gives each faction a strategic personality and theater bias, but it still treats "grand strategy" mostly as a few scalar weights. That is too thin for Grand Tactician because the game already exposes a policy tree and project tree that model the same war aims we are trying to make the AI express.

The mod should include:

- historical grand-strategy profiles for Union and Confederate AI,
- game-policy and game-project weighting that supports those profiles,
- bounded Harmony steering of vanilla policy/project picks,
- objective scoring tags that let campaigns express Anaconda, Richmond/Virginia defense, Mississippi control, foreign-recognition gambits, blockade-running, industrial mobilization, and late-war exhaustion.

## Historical strategy findings

### Union

The Union political objective began as restoring federal authority and preserving the Union; emancipation later became a second war aim. The practical grand-strategy pattern was:

- blockade Southern ports and constrain cotton/trade,
- control the Mississippi River and its tributaries to split the Confederacy,
- apply pressure against Virginia/Richmond and the major Confederate armies,
- use industrial, rail, naval, and manpower advantages to sustain simultaneous pressure,
- by 1864, destroy Confederate armies, logistics, resources, and public will through mutually supporting advances.

Sources used:

- Essential Civil War Curriculum, "Civil War Strategy 1861-1865": https://www.essentialcivilwarcurriculum.com/civil-war-strategy-1861-1865.html
- National Park Service, Lower Mississippi Delta Region: https://home.nps.gov/locations/lowermsdeltaregion/concept-vii-the-civil-war-in-the-delta.htm
- National Park Service, Vicksburg campaign: https://home.nps.gov/vick/learn/historyculture/campaign-for-vicksburg.htm
- American Battlefield Trust, naval strategy summary: https://www.battlefields.org/learn/articles/navies-civil-war

### Confederacy

The Confederate political objective was independence. Its practical grand-strategy problem was that victory only required Union failure, but politics pushed Davis toward defending too much territory. The result was tension between static defense and offensive-defensive operations:

- defend territory, Richmond/Virginia, ports, rivers, and state interests,
- use opportunistic offensives to disrupt Union plans and morale,
- protract the war until Northern public will or political support failed,
- pursue foreign recognition through cotton leverage, diplomacy, imports, and blockade-running,
- compensate for weaker industry and navy through arms imports, local production, commerce raiding, ports, rivers, and ironclads.

Sources used:

- Essential Civil War Curriculum, "Union and Confederate Military Leadership": https://www.essentialcivilwarcurriculum.com/union-and-confederate-military-leadership.html
- American Battlefield Trust, "Cotton is King": https://www.battlefields.org/learn/articles/cotton-king
- U.S. Office of the Historian, "Preventing Diplomatic Recognition of the Confederacy, 1861-1865": https://history.state.gov/milestones/1861-1865/confederacy
- American Battlefield Trust, "Commerce Raiders": https://www.battlefields.org/learn/articles/commerce-raiders
- Encyclopedia Virginia, "Desertion (Confederate) during the Civil War": https://encyclopediavirginia.org/entries/desertion-confederate-during-the-civil-war/
- House Divided, "Confederate president Jefferson Davis signs the first Conscription Act in American history": https://hd.housedivided.dickinson.edu/node/28514

## Game data findings

### Campaign-map command and front AI

Primary decompile anchors:

- `AICampaign.Update()` job sequence at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:11480`
- `AICampaign.CheckForDefensiveOperations(int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:13510`
- `AICampaign.CheckOffensiveMovements(int, Regiment, float)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:14095`
- `AICampaign.UpdateMicroMovementInOffensive(int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:13965`
- `AICampaign.RollUpEnemyObjectivesInZone(int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:13905`
- `AICampaign.CheckTransferOfUnits(int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:17232`
- `AICampaign.UpdateCampaignTheaters(int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:17074`
- `AICampaign.CheckCombinationOfUnits(int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:17141`
- `AICampaign.CheckArmyGroupManagement(int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:17705`
- `AIArea.CalculateMostValueableAIZones(int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:10965`
- `AIFaction.TransferData` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:10301`
- `ArmyGroup` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:75007`
- `GameVars.Alliance.AllowsArmyGroups()` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:62545`
- `GameVars.Commander.IsArmyGroupCommander()` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:60650`

Vanilla structure:

- `unittyp <= 13`: brigades/regiments/batteries.
- `unittyp == 14`: division-level campaign group.
- `unittyp == 15`: corps-level campaign group.
- `unittyp == 16`: army-level campaign group.
- `ArmyGroup`: extra W&L/top command layer above armies, stored in `BattleUnits.armygroups`.

Vanilla campaign AI works through an `AIFaction` snapshot with `ownunits`, `enemyunits`, `neutralunits`, `ownfleets`, `enemyfleets`, `unitsinoffensiveoperations`, `unitsindefensiveoperations`, `unitstobetransfered`, `groupstodefendcapital`, `positiondeficit`, and `positionsurplus`.

Relevant vanilla behavior:

- `UpdateAreaPoints` populates each `AIArea` with own/enemy points, distance pressure, and nearby campaign strength.
- `AIArea.CalculateMostValueableAIZones` picks one nearby "best" area from importance + force + distance values.
- `CheckOffensiveMovements` builds a local force package from a campaign group and nearby eligible units, checks strength dominance, weather, readiness, morale, supply, and commander initiative, then sends units toward the chosen area objective.
- `UpdateMicroMovementInOffensive` keeps offensive units rolling to nearby objectives while morale/supply/readiness remain acceptable.
- `CheckForDefensiveOperations` sorts enemy threats by proximity/front pressure, gathers nearby friendly units within theater limits, and launches a defensive operation only if local strength ratio and morale gates pass.
- `CheckTransferOfUnits` strips subordinate units from a surplus area and moves them to a deficit area, capped by `maximumunittransferstrength`.
- `UpdateCampaignTheaters` gives each unit a loose `theaterposition` near the closest enemy contact; `IsWithinOperationsTheater` then blocks operations outside that box when theater logic is enabled.
- `CheckCombinationOfUnits` and `RaiseNewCampaignGroup` create division/corps/army structures around theater positions.
- `CheckArmyGroupManagement` creates or attaches W&L army groups when armies are close enough and have theater positions.

Gap:

Vanilla has local threat response and local opportunity seeking, but it does not appear to maintain a true front-sector ledger. It can see a deficit/surplus and transfer strength, but it does not explicitly reason "I am weakening the Valley to reinforce Richmond" or "the Mississippi line may be conceded so Atlanta/Virginia survives." That means Whiskey's current plan-target steering can over-concentrate if we do not add a front-budget layer.

### Required front behavior

The strategic AI should defend the whole front unless the CIC makes an explicit concession. "Heroic but intelligent" means:

- Armies can take bold operational risks when the reward is high and the commander/profile supports it.
- Armies should not abandon a critical sector just because one target looks attractive.
- A weak army can delay, screen, fall back to a defensible line, or request transfer instead of charging.
- A strong/renowned commander may counterpunch locally, but only if sector risk stays under budget.
- Concessions should be logged as strategy decisions, not accidental side effects of transfer or objective steering.

This requires a weekly `FrontSectorLedger` built from vanilla data:

| Field | Source |
|---|---|
| sector id/theater/tag | `AIArea`, objective metadata, world-position bucketing |
| own/enemy strength | `AIArea.campaignunitsstrength`, `enemycampaignunitsstrength`, `enemycampaignunitsstrengthclose` |
| local morale/supply/readiness | `Regiment.groupmorale`, `groupsupplystate`, `CampaignArmyPanel.GetReadinessStep` |
| command level | `Regiment.unittyp`, `ArmyGroup.GetArmyGroup`, commander id |
| importance | current `ObjectiveMetadata`, campaign objective, capital/ports/rail/river tags |
| minimum garrison ratio | faction/era/profile + objective tags |
| concession cost | capital, river, rail, port, state support, foreign-recognition impact |

The ledger should produce:

- `Hold`: keep minimum force, defensive operations favored.
- `Delay`: hold if cheap, avoid offensive movements, accept retreat to supply/defensible line.
- `EconomyOfForce`: sector may be thinned but not emptied.
- `Concede`: sector may be weakened or abandoned to fund higher-priority plan.
- `Counterstroke`: local offensive is allowed because strength, commander, and reward support it.
- `Exploit`: plan-target sector receives extra offensive budget.

### Policy tree

Primary data file:

- `Config/policies.dat`

Primary decompile anchors:

- `Policies.CheckAIPolicyChange(int alliance)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:211020`
- `Policy.CheckForChapterUpdate()` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:211604`
- `GameVars.Alliance.AIPersonality` import from `aipersonalities.dat` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:66141`

Vanilla policy AI is not random at the moment of selection. It walks the active `AIPersonality.policies` list and chooses the first available policy whose chapter, prerequisite, scenario, deactivation, and blocking gates pass. That means Whiskey Realism can steer policy in two sane ways:

1. Reorder or filter the active AI personality policy list before `Policies.CheckAIPolicyChange` runs.
2. Prefix `Policies.CheckAIPolicyChange` only when the strategic profile has a clear higher-priority available policy, then add research and skip vanilla.

Key policy surfaces parsed from `policies.dat`:

| Side | IDs | Surface |
|---|---:|---|
| Union | 0-2 | Government Funding I-III |
| Union | 3-6 | Bread Basket I-IV |
| Union | 7-10 | Industrialization I-IV |
| Union | 11-14 | Military I-IV |
| Union | 15-19 | Diplomacy I-V |
| Union | 30-32 | Northern Routes / Feed Europe I-II |
| Union | 33-34, 44 | USCT / Emancipation / Abolition |
| Union | 35, 41 | Arming Civilian Ships / Legal Blockade |
| Union | 39-40, 45-46 | Enrollment / Recruitment Bounties |
| CSA | 100-102 | Government Funding I-III |
| CSA | 103-106 | King Cotton I-IV |
| CSA | 107-110 | Industrialization I-IV |
| CSA | 111-114 | Military I-IV |
| CSA | 115-119 | Diplomacy I-V |
| CSA | 130-131 | Burn Cotton / Restrict Cotton Trade |
| CSA | 141-144 | Free Trade / Organized Blockade Running / Letters of Marque / Support Mexican Intervention |
| CSA | 139-140, 145-146 | Conscription / Recruitment Bounties |

### Project tree

Primary data file:

- `Config/projects.dat`

Primary decompile anchors:

- `AICampaign.UpdateProjects(int alliance)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:17487`
- `GameVars.Alliance.AIPersonality.GetNextProjectRandom(int alliance, int subsidytype)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:62048`

Vanilla project AI is random-weighted. `UpdateProjects` asks the current AI personality for a project per subsidy type; `GetNextProjectRandom` filters by subsidy type, appointability, alliance, scenario, and DLC, then picks randomly using `projectsprob` as repeated weights.

That is a better patch target than policies because a bounded Postfix or Prefix can replace only the `nextprojecttoresearch[subsidytype]` slot when Whiskey has a clearly better strategy-aligned project.

Key project surfaces parsed from `projects.dat`:

| IDs | Side | Category | Surface |
|---:|---|---|---|
| 0-4 | both/CSA gated | Diplomacy | Austrian/British/British Artillery/French/Prussian arms imports |
| 5-19 | both/faction-specific | Military | rifles, artillery, carbines, repeaters, Springfield, Confederate rifles/guns |
| 30-41 | both/faction-specific | Military/Diplomacy | monitors, gunboats, rebuilt ironclads, British/French warships, Gloire/Warrior |
| 91-95 | both | Politics | propaganda, occupation administration, administration reform |
| 96-99 | both | Economy | banks, credit, markets, infrastructure |
| 100-106 | both | Military/Industry/Agriculture | logistics, military railroad, weapons production, industry, agriculture, trade warfare |
| 103 | both | Diplomacy | send envoys |
| 108-112 | both | Military | agents, recruitment, cavalry, artillery |
| 113-120 | both/faction-specific | Agriculture/Politics | farm/cotton/corn, training, railroad construction, improvised shipyards |
| 123-124 | both | Military | horse artillery, 6-gun batteries |

### Recruitment and formation AI

Primary decompile anchors:

- `AICampaign.Update()` job sequence at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:11299`
- `AICampaign.CalculateZoneRecruitingProbs(int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:13078`
- `AICampaign.ZoneRecruiting(int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:12405`
- `AIArea.GetBestRecruitingState(int, int, bool, bool)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:10722`
- `AICampaign.RaiseNewCampaignGroup(...)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:12940`
- `AICampaign.CheckCombinationOfBrigades(int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:17644`
- `AICampaign.CheckCombinationOfUnits(int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:17141`
- `AICampaign.CheckArmyGroupManagement(int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:17705`

Vanilla behavior:

- `CalculateZoneRecruitingProbs` computes own/enemy campaign strength, nearby strength, and average morale per `AIArea`.
- `ZoneRecruiting` finds the weakest owned-town heatmap cell, writes `positiondeficit` / `positionsurplus`, checks global strength ratio against `targetrecruitingratio + usedcampaignagressiveness * targetrecruitingmultiplier`, and recruits only when the faction is below target.
- `ZoneRecruiting` prefers volunteers over drafts when possible. It sets `num13=1` for draft only when volunteers in the selected state cannot satisfy the unit size, and it returns without drafting if global strength ratio is already above 1.
- `AIArea.GetBestRecruitingState` chooses from states in/near the target area using recruit pool, state support, ownership/exclusion flags, and distance.
- `RaiseNewCampaignGroup` creates divisions/corps/armies using vanilla hierarchy rules. If `GrandArmyStructure` is enabled, it creates army -> corps -> division; otherwise corps -> division. It also reuses nearby existing top units when possible.
- `CheckCombinationOfBrigades` merges weak same-type brigades/regiments under campaign groups, excluding W&L player-commanded units.
- `CheckCombinationOfUnits`, `UpdateCampaignTheaters`, and #16 `ArmyGroupManagementPatch` are already the safest surfaces for formation shape. Do not bypass them with direct tree surgery.

Gap:

Vanilla recruiting is reactive and local. It answers "where is the weakest heatmap cell?" and "which nearby state has enough volunteers/drafts?" It does not answer "what kind of army is the CSA trying to build in early 1861?" or "should Tennessee receive smaller defensive divisions while Virginia receives a field army?" That is where Whiskey should add weighting.

Whiskey should not force one giant army or tiny scattered detachments. The better model is:

- use vanilla group hierarchy for physical formation,
- use `ArmyAreaLedger` and `FrontSectorLedger` to decide where top formations belong,
- use a new recruitment intent layer to bias state and unit-type choices,
- keep separate defensive divisions only where the ledger marks `Hold`, `Delay`, or `Screen`,
- form larger field armies only where the ledger marks `Exploit` or `Counterstroke`,
- never pull W&L player-commanded units into AI structural reshuffles.

CSA early-war recruitment should be "responsible" rather than passive:

- prefer volunteers and high-support home states while national strength is near parity,
- avoid drafts until the CSA is under-strength, after major defeats, or after policy gates such as conscription/recruitment bounties are active,
- prioritize Virginia, Tennessee, Mississippi River, and port-defense sectors according to threat,
- avoid exhausting a single state if another viable state can support the same operating area,
- bias artillery/cavalry only when the operating area or army role justifies it; otherwise preserve infantry-heavy line strength.

Patch surface for #8:

- Keep #8 anchored on `AIArea.GetBestRecruitingState` as the catalog says, but only alter `__result` when a scoped `ZoneRecruiting` context is active. That avoids corrupting raid-force and sea-invasion recruitment calls that also use the same helper.
- Add a `RecruitmentIntentLedger` during weekly strategic review. It should output preferred states, avoid states, draft tolerance, unit-type bias, and max active recruiting pressure for each alliance.
- Add bounded logs only when Whiskey changes the selected state or blocks draft pressure:
  - `[once:recruitment] Recruitment steering active`
  - `[Patch:Recruitment] alliance=1 area=VirginiaCapitalCorridor oldState=... newState=... reason=volunteer-high-support`
  - `[Patch:Recruitment] alliance=1 action=blocked-draft reason=strength-near-parity`

### Naval AI

Primary decompile/config anchors:

- `AICampaign.CheckFleetMovements(int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:13148`
- `AICampaign.CheckShipConstruction(int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:11998`
- `AICampaign.GrabShipType(int, bool, bool, bool, int)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:12244`
- `AICampaign.RaiseFleet(int, int, bool, bool)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:12091`
- `ShipRecruitingList.IsShipChooseable(...)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:218703`
- `Config/projects.dat` naval projects around ironclads/gunboats/imports/trade warfare, including `Ironclad Monitors`, `Ironclad Gunboats`, `Rebuilt Ironclads`, `Confederate Gunboats`, `Armored Gunboats`, British/French ironclad imports, `Trade Warfare`, and `Improvised Shipyards`.
- `Config/CampaignTips.txt` confirms blockades use fleet command radius, fleet size, distance, opposing fleets, trade-warfare projects, and commander administration.

Vanilla behavior:

- Fleet movement is local and opportunistic. `CheckFleetMovements` picks a ready fleet, first responds to blocked own ports, otherwise chooses enemy sea/river ports by distance divided by port value.
- Ship construction is probabilistic. `CheckShipConstruction` derives sea/river construction chance from own and enemy blockade ratios, fleet expense share, target fleet expense prefs, under-construction cap, and elapsed time.
- Ship type is random from chooseable ships matching sea/river/fast/armor filters. It respects technology/import availability, port level, available port, debt rating, and fleet capacity.
- Vanilla already has `fleetorders` for blockade/raid/patrol behavior; the mod should steer target and composition, not replace fleet movement mechanics.

Historical implication:

- Union should lean into blockade + brown-water river control from the start, then expand monitors/gunboats/logistics as projects and ports allow.
- CSA should not try to match the Union ship-for-ship. It should defend key ports/rivers, use blockade running/imports/trade warfare, commerce raiding, local gunboats/cottonclads, and selective ironclads.

Patch surfaces:

- `ProjectSelectionPatch` should handle most naval preparation first by biasing the research/project tree:
  - Union early: `Arming Civilian Ships`, `Legal Blockade`, monitors/gunboats, logistics/trade warfare, river capacity.
  - CSA early: `King Cotton`, diplomacy/imports, `Organized Blockade Running`, `Letters of Marque`, `Confederate Gunboats`, `Improvised Shipyards`, arms imports.
- Add a later `NavalIntentPatch` only after project/policy steering is verified. Preferred target: `CheckFleetMovements` Postfix/Prefix pair that changes a fleet target only when the strategy profile has a stronger port/river objective than vanilla's distance/value pick.
- Add a later `ShipConstructionPatch` only if project steering is not enough. Preferred target: `GrabShipType` Postfix under a scoped `CheckShipConstruction` context so CSA can prefer cheap local river/coastal defense and Union can prefer blockade/river types without breaking manual ship construction.

### Initial-war behavior

At campaign start, weekly strategic review should build a first-war readiness package before vanilla has drifted into random local choices:

| Alliance | Early profile | What Whiskey should prepare |
|---|---|---|
| Union | Anaconda baseline | Legal blockade / naval capacity / river gunboats, Washington and Virginia defense, Mississippi pressure, industry/logistics, cautious AoP build-up before major offensive pressure. |
| CSA | Cordon plus foreign recognition | Defend Richmond/Virginia, hold Tennessee/Mississippi/ports, use King Cotton/diplomacy/imports, prepare local defensive divisions and one or two field-army concentrations, avoid early draft overuse while volunteers suffice. |

The first 4-8 weekly reviews should therefore populate:

- `GrandStrategyProfile` for the current alliance/era/chapter,
- `PolicyIntent` and `ProjectIntent` queues,
- `RecruitmentIntentLedger`,
- `FrontSectorLedger` posture and minimum hold budgets,
- `ArmyAreaLedger` assignments,
- `ArmyGroupDoctrine` grouping plan,
- bounded one-line logs when any intent signature changes.

## Design

### 1. Add `GrandStrategyProfile`

Create a small strategic model under `src/WhiskeyRealism/Strategic/`.

Fields:

- `AllianceId`
- `EraStage`
- `Name`
- `ObjectiveTagWeights`
- `PolicyWeights`
- `ProjectWeights`
- `TheaterOverrides`
- `CategoryOverrides`

Initial profiles:

- Union Early: Anaconda baseline - blockade, Mississippi, logistics, industry, river/coast pressure.
- Union Mid: Coordinated pressure - Mississippi/Vicksburg, Chattanooga, Richmond pressure, manpower, rail/logistics.
- Union Late: Exhaustion and army destruction - Atlanta/Georgia, Richmond/Virginia, rail hubs, occupation, recruitment, hard-war policy.
- CSA Early: Cordon plus foreign recognition - defend Virginia/Richmond, hold ports/rivers, King Cotton, diplomacy, arms imports.
- CSA Mid: Offensive-defensive survival - counter Union penetrations, preserve armies, blockade-running/imports, conscription/logistics.
- CSA Late: Protraction and preservation - avoid catastrophic losses, defend Atlanta/Richmond, conserve armies, emergency manpower/credit, trade warfare.

### 2. Extend objective metadata with strategy tags

Keep existing scalar fields for compatibility, but add strategy tags:

- `Blockade`
- `RiverControl`
- `CapitalThreat`
- `RailHub`
- `ForeignRecognition`
- `IndustrialBase`
- `Agriculture`
- `Manpower`
- `ArmyDestruction`
- `PortAccess`
- `DefensiveDepth`

This lets objective scoring represent more than geography. Example: Vicksburg should be `RiverControl + DefensiveDepth`; Richmond should be `CapitalThreat`; Atlanta/Chattanooga should be `RailHub + IndustrialBase + DefensiveDepth`; coastal objectives should be `Blockade + PortAccess`.

### 3. Add policy steering after current v0.2.2 patch cleanup

Proposed patch:

- `PolicySelectionPatch`
- Target: `Policies.CheckAIPolicyChange(int alliance)`
- Preferred type: Prefix only when the strategy profile selects a concrete available policy; otherwise fall through to vanilla.

Rules:

- Never enqueue more than vanilla's one non-act policy in research.
- Do not touch player policy control unless automanage or AI-vs-AI is active, matching vanilla.
- If a policy cannot be found or `AddResearch` reflection fails, log one warning and fall through.
- Bound logging: `[once:policy-selection]` first-fire, plus `[Patch:PolicySelection] alliance=... policy=... profile=... reason=...` only when we override vanilla.

### 4. Add project steering first

Proposed patch:

- `ProjectSelectionPatch`
- Target: `AICampaign.UpdateProjects(int alliance)`
- Preferred type: Postfix or narrow Prefix/Postfix pair.

Behavior:

- Let vanilla perform normal subsidy/building logic.
- If `nextprojecttoresearch[subsidytype]` is empty or points to a low-weight project, evaluate appointable projects in that subsidy type.
- Replace the slot only when strategy weight exceeds vanilla candidate by a clear margin.
- Do not appoint projects directly; leave funding and appointment to vanilla.

Bound logging:

- `[once:project-selection]` first-fire.
- `[Patch:ProjectSelection] alliance=... subsidy=... old=... new=... profile=... reason=...` only on replacement.
- Detailed candidate scoring behind `Verbose Logging`.

### 5. Integrate with CIC scoring

Update `CIC.ScoreObjective` so it composes:

- current personality,
- current era stage,
- faction profile,
- active grand-strategy profile,
- objective metadata tags.

The goal is not to script outcomes. It is to make the AI more likely to pursue historically coherent choices when the board state supports them.

### 6. Add front/army posture before wider steering

Add strategic core types:

- `FrontSector`
- `FrontPosture`
- `FrontBudget`
- `ArmyRole`
- `ConcessionDecision`

The ledger is recomputed during weekly strategic review and remains read-only to Harmony patches between review ticks.

Inputs:

- active CIC plan,
- grand-strategy profile,
- objective tags,
- faction/era/personality,
- observed battle history,
- vanilla `AIArea` strength/importance fields,
- army/corps/division positions and command hierarchy.

Outputs:

- sector minimum force ratios,
- sector offensive budget,
- allowed concession list,
- preferred reserve source sectors,
- preferred reinforcement destination sectors,
- per-army role: `Hold`, `Screen`, `Reserve`, `Exploit`, `Counterstroke`, `Recover`.

Force concentration must pay a real price. Before `TransferOfUnitsPatch` redirects `positiondeficit` to the active plan target, check the source sector:

- Do not strip below `minimum force ratio` unless the CIC profile has issued a `Concede` or `EconomyOfForce` decision.
- Prefer taking from `Reserve` or `EconomyOfForce` sectors before `Hold` sectors.
- Log only when a transfer is blocked or redirected:
  - `[Patch:TransferBudget] alliance=... from=... to=... action=blocked reason=min-hold`
  - `[Patch:TransferBudget] alliance=... from=... to=... action=concession reason=...`

Defensive posture rules:

- `Hold`: lower threshold for local defensive response; favor nearby units and current theater units.
- `Delay`: allow defensive operations but prefer supply depot / fallback-line targets.
- `EconomyOfForce`: defend against direct threats only; do not pull reserves from high-priority sectors.
- `Concede`: do not launch expensive defensive operations unless capital/army-destruction risk is high.
- `Counterstroke`: allow the second branch in `CheckForDefensiveOperations` to attack enemy units on occupied friendly soil only when strength dominance and commander initiative justify it.

Offensive posture rules:

- `Exploit` armies may attack the plan target or adjacent objective chain.
- `Counterstroke` armies may attack local exposed enemies or recover a key lost town.
- `Hold` armies should not be selected for distant offensive operations.
- `Recover` armies should be excluded until morale/readiness/supply improves.
- `Screen` armies should move toward defensive depth or block routes, not launch deep attacks.

Army groups should become command-intent containers, not just UI/coordination objects:

- map `ArmyGroup` and its attached armies to a `FrontSector`,
- assign a role to the army group first,
- let attached armies inherit the role unless their local state forces `Recover` or `Screen`,
- use army-group commander personality in sector risk tolerance once available.

This also addresses early-campaign succession: #16 `ArmyGroupManagementPatch` now creates/assigns a command container only when vanilla prerequisites and the weekly historical area ledger identify at least two eligible top formations in the same operating command.

Heroism should be controlled risk, not suicidal movement:

- `HeroicDefense`: stand/defend longer at capitals, river keys, rail hubs, and army-preservation moments if supply and morale are not collapsing.
- `OperationalAudacity`: accept a wider force-ratio band for counterstrokes or exploitation when commander audacity/initiative is high.
- `ArmyPreservation`: if casualties, morale, supply, or battle-history losses cross thresholds, downgrade from `Counterstroke` to `Delay` or `Recover`.
- `PoliticalStakes`: CSA overweights Richmond/Virginia and foreign-recognition opportunities; Union overweights Mississippi, blockade, rail/industry, and simultaneous pressure.

No army should be marked heroic if:

- readiness is below vanilla movement gate,
- morale is below cancel-defensive-operation thresholds,
- supply state is below offensive target threshold,
- the source sector would fall below its minimum hold budget,
- the action would violate W&L player-command restrictions.

### 7. Add `RecruitmentIntentLedger`

Create pure strategic types:

- `RecruitmentIntentLedger`
- `RecruitmentStatePreference`
- `RecruitmentPressure`
- `UnitCompositionBias`

Inputs:

- alliance, era, vanilla chapter, active grand-strategy profile,
- front posture and army-area assignments,
- vanilla state recruit pools/support/ownership,
- global strength ratio from `AICampaign.GetStrengthRatio`,
- battle-history losses and current morale,
- policy status for conscription/recruitment bounties.

Outputs:

- preferred state list by operating area,
- avoid-state list,
- volunteer/draft tolerance,
- preferred unit-type weights,
- group-size preference: independent screen/division vs field-army reinforcement.

Rules:

- Preserve vanilla's no-recruitment gates when debt/rating blocks recruitment.
- Do not force drafts when vanilla would not recruit at all.
- Do not override state selection if the Whiskey candidate lacks enough volunteers/drafts or support.
- Do not override recruitment calls outside `ZoneRecruiting` context.
- Keep output read-only to patches between weekly reviews.

### 8. Add naval intent after policy/project steering

Create pure strategic types:

- `NavalIntentLedger`
- `NavalTheaterIntent`
- `ShipConstructionBias`
- `PortOperationPriority`

Inputs:

- own/enemy port blockade ratios,
- own/enemy fleets,
- strategy profile,
- project/policy status,
- port value and river/sea flags,
- fleet readiness and ship composition.

Outputs:

- blockade targets,
- own-port relief targets,
- raid-vs-blockade preference,
- ship-type bias by sea/river/fast/armor,
- maximum naval spending pressure by era/faction.

Rules:

- Union: blockade and river pressure are allowed to become major spending priorities.
- CSA: naval spending should be asymmetric and survival-oriented unless foreign-recognition/import strategy is succeeding.
- Do not bypass `ShipRecruitingList.IsShipChooseable`; only bias among chooseable ships.
- Keep fleet movement patch bounded: one override only when vanilla chooses a low-value target and the ledger has a high-confidence target.

## Recommended sequence

1. Runtime-smoke #4 `DefensiveOpsPatch`, #15 `ArmyAreaTheaterPatch`, and #16 `ArmyGroupManagementPatch` after a GTCW restart.
2. Add `GrandStrategyProfile` plus objective-tag metadata table. This is pure strategic core and can be tested without new Harmony patch risk.
3. Add `ProjectSelectionPatch`. It is lower risk because project choice is random-weighted in vanilla and we can replace only the next project slot. This is the first place to express naval and industrial preparation.
4. Add `PolicySelectionPatch`. Policy selection is ordered and chapter-gated, so this needs a tighter safety check. This is the first place to express CSA King Cotton / blockade-running / conscription timing and Union blockade / mobilization timing.
5. Implement #8 `RecruitmentPatch` as a scoped `AIArea.GetBestRecruitingState` Postfix under `ZoneRecruiting` context, driven by `RecruitmentIntentLedger`.
6. #7 `PerkSelectionPatch` is implemented on post-v0.2.2 main with role-aware army/fleet campaign perk scoring.
7. Add `NavalIntentLedger`; only patch `CheckFleetMovements` / `GrabShipType` if project/policy steering is not enough.
8. Add defensive/offensive front-posture steering only after the weekly ledgers log plausible `Hold/Delay/Concede/Exploit` decisions in-game.

## Acceptance criteria

- Build passes with 0 warnings / 0 errors.
- DLL deployed after every code change and SHA-256 verified against `dist/WhiskeyRealism.dll`.
- First-fire markers appear for new patches after game restart.
- Override logs are bounded and only appear when Whiskey actually changes a vanilla choice.
- Front-budget/concession logs are bounded and only appear when a sector posture changes, a transfer is blocked, or a concession is made.
- Recruitment/naval logs are bounded and only appear when Whiskey changes a vanilla selection or when an intent signature changes.
- With `Verbose Logging = false`, normal campaign log volume remains low.
- Sidecar does not need new persistence unless active grand-strategy profile becomes user-visible or externally configurable.

## Explicit non-goals

- No tactical battle AI changes.
- No deterministic historical script that forces exact campaigns.
- No direct mutation of strategic mod state from Harmony patches.
- No data-file editing inside the game install.
- No replacing the whole vanilla policy/project tree; the mod should steer, not wholesale fork, the game data.
