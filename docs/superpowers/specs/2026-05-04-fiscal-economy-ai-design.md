# Fiscal Economy AI Design

Status: approved direction, design spec.
Scope: Slice A enrichment for economy, building, policy, credit-rating, and fiscal decision quality. This does not rewrite the game economy, edit game install data, or create hidden CSA parity bonuses.

## Goal

Make the AI use the existing Grand Tactician economy intelligently enough that the Union wins by sustained industrial pressure and the CSA can compete by making historically plausible asymmetric choices.

The CSA should not become an economic peer of the North. It should avoid wasting scarce credit and manpower, protect access to recruitment and construction, prioritize imports and credit tools, and invest in survival infrastructure. The Union should use its stronger fiscal base to build the blockade, rail/logistics, industry, recruitment capacity, and late-war simultaneous pressure.

## Vanilla Budget Model

Primary anchors:

- `AICampaign.UpdateFinancialAI(int alliance)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:15352`
- `Economy.UpdateEconomyEstimates(float, float)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:32488`
- `Economy.UpdateMacroEconomy(float)` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:32549`
- `GameVars.Alliance.GetBalance()` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:63285`
- `GameVars.Alliance.IsRatingOkForRecruitment/Construction/WeaponPurchases()` at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:62772`
- `AICampaign.UpdateCompanyFoundations(int, float)` around `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:15090`

Vanilla calculates annual balance as:

```
tax revenue
+ admin cost
+ subsidy cost
+ interest cost
+ army upkeep
+ navy upkeep
+ recruitment cost
+ supply depot purchases
```

Costs are negative values. A negative balance means the state is spending beyond annual revenue.

Treasury and debt:

- If treasury is below zero, the game automatically issues fixed bonds.
- Current config bond issue/repay amount is `$25,000,000`.
- If treasury is at least `$50,000,000`, the game automatically repays bonds.
- Debt increases interest cost, which worsens future balance.

Credit rating:

- Rating is derived from debt plus deficit pressure:
  - `(-surplus * 1.25) + debt * 4`
- This pressure is scaled by year-specific rating thresholds.
- Policy rating modifiers and project 97 modify the result.
- Higher rating index means worse credit.
- Worse credit increases interest.
- Banks reduce interest through funding-improvement effects.

Hard gates:

- Recruitment is blocked when rating falls below the recruitment threshold.
- Construction is blocked when rating falls below the construction threshold unless enough subsidy funding is already available.
- Weapon purchases are blocked when rating falls below the weapon-order threshold.
- Ship construction also checks rating through recruitment/ship chooseability paths.

Vanilla AI gap:

- `UpdateFinancialAI` is reactive and blunt.
- It adjusts one tax lane by one step when balance is below/above thresholds.
- It picks one random subsidy lane and raises/cuts it based mostly on whether construction rating is okay.
- It does not protect a specific credit-rating floor.
- It does not connect fiscal choices to grand strategy, faction asymmetry, theater posture, or upcoming recruitment/construction needs.

## Design Principle

Steer vanilla, do not replace it.

Whiskey should preserve:

- automatic debt issuance and repayment,
- vanilla tax revenue formulas,
- vanilla subsidy funding pools,
- vanilla policy/project availability gates,
- vanilla building placement feasibility,
- vanilla rating gates for recruitment, construction, weapons, and ships.

Whiskey should add:

- weekly fiscal posture,
- policy priority by faction/era/fiscal stress,
- target tax and subsidy ranges,
- credit-rating guardrails,
- construction priorities,
- bounded overrides only when vanilla picks an obviously bad fiscal move.

## New Strategic Types

### FiscalIntentLedger

Pure strategic ledger generated during weekly review.

Inputs:

- alliance id,
- era stage and vanilla chapter,
- current `GrandStrategyProfile`,
- treasury, debt, balance, interest, current rating,
- rating thresholds for recruitment, construction, and weapon purchases,
- tax rates and tax caps,
- subsidy rates and subsidy caps,
- activated policies and projects,
- army/navy/recruitment/upkeep costs,
- active plan and front posture,
- battle-history pressure and war-state gates.

Outputs:

- `FiscalPosture`: `Expansion`, `BalancedWar`, `CreditDefense`, `EmergencySolvency`.
- minimum acceptable rating band.
- desired tax ranges per lane.
- desired subsidy ranges per lane.
- policy priorities.
- project priorities.
- construction priorities.
- spending suppressions.
- one-line signature for bounded logging.

Posture definitions:

- `Expansion`: credit is healthy and military situation allows investment.
- `BalancedWar`: normal wartime spending with rating protected above gates.
- `CreditDefense`: rating is near a gate; cut low-value subsidies and prioritize credit tools.
- `EmergencySolvency`: recruitment/construction/weapon gates are threatened or already blocked; stop discretionary spending and pursue funding policies/projects.

### FiscalPolicyPriority

A pure ranking table that scores policy IDs by profile, era, and fiscal posture.

Union priorities:

- early: Government Funding, Legal Blockade, Industrialization, Military, rail/logistics support.
- mid: recruitment bounties, industry/logistics, river/naval policy support, emancipation/USCT when war-state and chapter make it plausible.
- late: manpower, hard-war pressure, industry, occupation/administration, public morale support.

CSA priorities:

- early: Government Funding, King Cotton, Diplomacy, Free Trade / blockade-running setup, arms-import support, War Bonds / Bank Act when credit pressure rises.
- mid: Military, conscription timing, recruitment bounties if manpower is failing, Organized Blockade Running, Letters of Marque, trade warfare, banks/credit.
- late: emergency manpower, credit defense, army preservation, trade warfare, defensive logistics, selective industry.

CSA timing guardrails:

- Avoid early conscription if volunteer pool and strength ratio are adequate.
- Prefer credit and import policy before expensive domestic parity spending.
- Avoid tariff choices that damage foreign-recognition/import strategy unless fiscal emergency requires it.
- Use War Bonds and Bank Act as credit-protection tools before rating gates collapse.

### FiscalTarget

Target ranges for AI finance sliders.

Tax lanes:

- tariffs,
- sales,
- income,
- corporate,
- land sales.

Subsidy lanes:

- politics,
- economy,
- agriculture,
- industry,
- military,
- diplomacy,
- transport/trade-war/civil-order lanes as exposed by the game.

Rules:

- Tax changes should be bounded to vanilla's step size.
- Corporate tax should stay conservative while the AI is trying to grow private industry.
- Income/sales taxes can rise under `CreditDefense`.
- Tariffs are faction-strategy dependent: Union can use them more freely; CSA must weigh tariffs against diplomacy/import/cotton strategy.
- Subsidies should not be random. Raise the lane that supports the current fiscal and military plan.
- Cut low-priority subsidies first when rating nears a gate.

### EconomyConstructionIntent

A weekly construction priority signal consumed by a bounded patch around company/building candidate selection.

Inputs:

- best IIP candidates from vanilla,
- building type,
- current fiscal posture,
- rating gate status,
- front posture,
- local supply pressure,
- local wounded pressure,
- transport bottlenecks,
- military experience gap,
- local capital/bank availability,
- policy/project status.

Outputs:

- preferred building types by alliance and posture,
- suppressions when credit is stressed,
- high-confidence candidate boosts.

Preferred priorities:

Union:

- banks when interest/rating is poor,
- markets and rail/transport around logistics bottlenecks,
- factories/foundries/industry in secure high-workforce regions,
- hospitals near sustained active fronts,
- military schools if military experience lags,
- naval/port infrastructure where blockade and river plans need it.

CSA:

- banks early when interest/rating is dangerous,
- markets/transport where supply corridors matter,
- supply depots near Richmond/Virginia, Tennessee/Georgia, Mississippi, and key ports,
- hospitals near high-casualty defensive fronts,
- military schools selectively, not everywhere,
- factories/foundries only where secure and strategically needed,
- shipyards/naval infrastructure only for blockade running, river defense, imports, and selective ironclads/gunboats.

Suppression rules:

- Do not force construction when rating blocks it and subsidy funding is absent.
- Do not build expensive discretionary industry under `EmergencySolvency`.
- Do not let CSA chase Union-style naval parity.
- Do not build in exposed areas unless the front posture says `Hold` or `Exploit` and supply/security are adequate.

## Patch Surfaces

### PolicySelectionPatch

Target:

- `Policies.CheckAIPolicyChange(int alliance)`.

Shape:

- Prefix only when FiscalIntent has a clear higher-priority available policy.
- Reuse vanilla availability gates:
  - chapter,
  - prerequisites,
  - scenario availability,
  - deactivation,
  - blocking,
  - max active policies.
- Add research through vanilla `Policies.AddResearch`.
- Fall through when no safe policy wins.

Logging:

- `[once:policy-selection] PolicySelectionPatch wired`
- `[Patch:PolicySelection] alliance=1 policy=... profile="..." posture=CreditDefense reason=credit-gate`

### FinancialAIPatch

Target:

- `AICampaign.UpdateFinancialAI(int alliance)`.

Preferred shape:

- Postfix after vanilla runs.
- Read FiscalIntent target ranges.
- Move only one or two lanes per call by vanilla step size.
- Clamp to target range.
- Do not alter player-controlled finances unless automanage or AI-vs-AI allows it.

Reasons to use Postfix:

- Vanilla bond issue/repay behavior remains intact.
- Vanilla baseline tax/subsidy logic still executes.
- Whiskey only corrects random or strategically harmful moves.

Logging:

- `[once:financial-ai] FinancialAIPatch wired`
- `[FiscalIntent] alliance=1 posture=CreditDefense rating=BBB- balance=-... debt=... tax=... subsidy=...`
- `[Patch:FinancialAI] alliance=1 lane=industry old=0.70 new=0.65 reason=protect-credit`

### EconomyConstructionPatch

Candidate targets:

- `AICampaign.UpdateCompanyFoundations(...)`
- `AICampaign.UpdateCompanyFoundations_x(...)` if the active code path differs by scenario/version.
- `AICampaign.UpdateCompanyFoundations` should be verified in runtime first-fire before patching deeper.

Shape:

- Postfix or small Prefix/Postfix pair around candidate probability.
- Bias `bestiipplacesprob[buildingType]` only when candidate is vanilla-valid.
- Do not call `CBuilding.AddConstructionWish` directly except through vanilla flow.
- Preserve `GameVars.buildingtypes[type].HasPolicy(alliance)`.

Logging:

- `[once:economy-construction] EconomyConstructionPatch wired`
- `[Patch:EconomyConstruction] alliance=1 building=Bank oldProb=... newProb=... reason=interest-pressure`

## CSA Competitiveness Model

CSA competes through decision quality:

- preserve credit so recruitment and construction remain possible,
- avoid excessive early drafts,
- preserve high-support volunteer states,
- prioritize imports and blockade-running over domestic parity,
- keep armies supplied rather than overbuilding unsupported forces,
- invest in banks/markets/supply/hospitals before vanity industry,
- build enough military capacity to keep field armies dangerous,
- accept strategic concessions when holding everything would bankrupt the state.

Explicit non-goal:

- No hidden CSA money tap.
- No equalized Northern/Southern industrial base.
- No automatic CSA replacement-rate buff detached from policy, supply, and fiscal state.

Optional later safety valve:

- A config-gated `CSA Fiscal Assist` can slightly widen fiscal target margins only if campaign telemetry shows the CSA collapses before making meaningful choices.
- Default should be off or conservative.

## Union Model

Union competes by using its actual advantages:

- tolerate more debt when rating remains safe,
- invest in blockade and river control,
- build logistics and transport capacity,
- increase industry and weapon production,
- expand recruitment policy when needed,
- fund simultaneous pressure after 1863,
- maintain enough fiscal health to keep armies supplied.

The Union AI should not sit on a healthy balance while failing to apply pressure.

## Data Flow

Weekly review:

1. `StrategicCoordinator` updates era, front ledgers, army-area ledgers, and grand-strategy profile.
2. `FiscalIntentLedger` computes posture and targets.
3. Patches read ledger output during vanilla AI cycles.
4. Patches apply bounded corrections.
5. Logs emit only on posture-signature changes or actual overrides.

Harmony patches remain read-only with respect to strategic state. The ledger writes happen in weekly review only.

## Testing

Pure tests:

- CSA early healthy credit prefers `BalancedWar` or `Expansion`, not emergency conscription.
- CSA rating near construction/recruitment gate enters `CreditDefense`.
- CSA gate-blocked state enters `EmergencySolvency`.
- Union healthy credit accepts higher industry/naval/logistics spending.
- Corporate tax stays lower when industry growth is a priority.
- CSA diplomacy/import profile suppresses tariff-heavy choices unless emergency.
- Construction scoring prefers banks under high interest, hospitals near wounded pressure, and markets under transport bottlenecks.

Runtime smoke:

- New first-fire markers appear after restart.
- Policy override appears only when a better available policy exists.
- Financial patch adjusts no more than bounded lane count per call.
- CSA does not drop below recruitment/construction gates from random subsidy escalation in early smoke.
- Construction patch logs only candidate boosts, not direct forced construction.

Required DLL-affecting verification:

- `./build.sh`
- deploy `dist/WhiskeyRealism.dll`
- verify deployed DLL timestamp, size, and SHA-256 match `dist/WhiskeyRealism.dll`
- restart GTCW and confirm first-fire markers.

## Acceptance Criteria

- No game install config files are edited.
- No direct mutation of economy state outside vanilla finance/building APIs.
- No player finance control is changed unless automanage or AI-vs-AI allows it.
- CSA remains weaker in raw economy but avoids obviously self-destructive fiscal choices.
- Union invests its advantage into blockade, logistics, industry, and manpower.
- Normal log volume stays low with verbose logging off.

## Recommended Implementation Sequence

1. Add `FiscalIntentLedger` and pure tests.
2. Add policy scoring and `PolicySelectionPatch`.
3. Add target tax/subsidy ranges and `FinancialAIPatch`.
4. Add construction scoring and `EconomyConstructionPatch`.
5. Connect recruitment/replenishment intent to fiscal posture.
6. Rebalance after campaign telemetry, not before.
