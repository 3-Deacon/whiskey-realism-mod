# Grand Strategy and Research Tree Design

Date: 2026-05-03
Status: proposed design for v0.2.2/v0.2.3 sequencing
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

This requires a monthly `FrontSectorLedger` built from vanilla data:

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

The ledger is recomputed on monthly tick and remains read-only to Harmony patches between ticks.

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

This also addresses early-campaign succession: if no `ArmyGroup` exists when a historical event needs one, bootstrap should create/assign a command container only when vanilla prerequisites allow it or the design explicitly approves an early-campaign exception.

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

## Recommended sequence

1. Finish current v0.2.2 runtime smoke for #4 `DefensiveOpsPatch`.
2. Implement reserved #7 and #8 if still cheap and bounded.
3. Add `GrandStrategyProfile` plus objective-tag metadata table. This is pure strategic core and can be tested without new Harmony patch risk.
4. Add `FrontSectorLedger` and per-army roles before any broader objective/project steering. This prevents plan-target concentration from hollowing out the rest of the front.
5. Add `ProjectSelectionPatch`. It is lower risk because project choice is random-weighted in vanilla and we can replace only the next project slot.
6. Add `PolicySelectionPatch`. Policy selection is ordered and chapter-gated, so this needs a tighter safety check.
7. Add defensive/offensive front-posture steering only after the ledger logs plausible `Hold/Delay/Concede/Exploit` decisions in-game.

## Acceptance criteria

- Build passes with 0 warnings / 0 errors.
- DLL deployed after every code change and SHA-256 verified against `dist/WhiskeyRealism.dll`.
- First-fire markers appear for new patches after game restart.
- Override logs are bounded and only appear when Whiskey actually changes a vanilla choice.
- Front-budget/concession logs are bounded and only appear when a sector posture changes, a transfer is blocked, or a concession is made.
- With `Verbose Logging = false`, normal campaign log volume remains low.
- Sidecar does not need new persistence unless active grand-strategy profile becomes user-visible or externally configurable.

## Explicit non-goals

- No tactical battle AI changes.
- No deterministic historical script that forces exact campaigns.
- No direct mutation of strategic mod state from Harmony patches.
- No data-file editing inside the game install.
- No replacing the whole vanilla policy/project tree; the mod should steer, not wholesale fork, the game data.
