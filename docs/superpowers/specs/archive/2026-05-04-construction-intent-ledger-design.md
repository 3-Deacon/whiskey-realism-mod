# Construction Intent Ledger Design

Status: review-corrected draft design spec, grounded in verified vanilla construction behavior.
Scope: Slice A enrichment for AI construction decisions: private economy buildings, supply depots, forts, telegraph stations, and railroads. This spec does not rewrite the economy, edit game install data, or bypass vanilla placement/unit/funding gates.

## Adversarial Review Corrections

An adversarial review confirmed the vanilla anchors and forced several constraints into this spec:

- `bestiipplaces[type]` replacement is the highest-risk private-building write surface. It is allowed only after a full vanilla-validity contract is checked, and it is not part of the first implementation slice.
- "Scanner-level steering" is not a separate safe surface without a Transpiler. Initial steering stays at the consumer-state layer before `UpdateCompanyFoundations` consumes `bestiipplaces`.
- Fort steering is unit-position-bounded. A preferred fort site is unrealizable unless an eligible unit is already within vanilla range and passes every vanilla unit gate.
- Railroad steering cannot be a whole-method Prefix skip. Initial railroad work is observation only; active per-line steering needs a dedicated implementation decision.
- CSA doctrine must include historically plausible early/mid arms-industry overinvestment even when it hurts credit, while still avoiding endless late-war rationalization-free spam.
- CSA railroad construction must be suppressed as doctrine, not merely by fiscal pressure.
- Union fort priority should favor occupied river/logistics hubs and coastal bases before Washington-style capital approaches.
- `ConstructionIntentLedger` is recomputed weekly and is not persisted.

## Source Findings

This spec depends on the verified vanilla deep dive:

- `docs/superpowers/specs/archive/2026-05-04-construction-vanilla-deep-dive.md`
- decompile source: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`

Key vanilla anchors:

- `AICampaign.UpdateCompanyFoundationList()` at line 15082
- `AICampaign.UpdateCompanyFoundations(int, float)` at line 15000
- `AICampaign.CheckSupplyDepotConstruction(int)` at line 14659
- `AICampaign.CheckFortConstruction(int)` at line 16347
- `AICampaign.FortConstructionOrder` at line 9622
- `AICampaign.UpdateRailroadConstruction(int, float)` at line 16052
- `BattleUnits.Railroad.StartConstruction(...)` at line 77818
- `CBuilding.Place(...)` at line 96163
- `CBuilding.AddConstructionWish(...)` at line 97479
- `CBuilding.WorkDownConstructionWishes()` at line 97538
- `CBuilding.id_telegraphstation = 23` at line 95089; `Config/buildingtypes.dat` names building ID 23 as `Telegraph Station`
- `CBuilding.UpdateTelegraphConnections()` at line 95904
- `CBuilding.CheckTelegraphConnection()` at line 95918
- `Regiment.CheckTelegraphConnection()` at line 116179
- campaign order-delay telegraph modifier at line 125037
- campaign morale telegraph modifier at line 127930

## Goal

Make AI construction historically plausible and strategically useful while preserving vanilla mechanics.

The Union should use its economic depth to expand logistics, rail, markets, depots, and industrial capacity for sustained multi-theater pressure. The CSA should remain weaker in raw economy but avoid self-destructive construction choices, prioritizing supply survival, defensive corridors, credit-preserving banks/markets, connected command infrastructure, and selective industry where it supports field-army usefulness.

## Design Principle

Steer vanilla, do not replace it.

Whiskey should preserve:

- vanilla construction costs and subsidy pools;
- vanilla credit-rating gates;
- vanilla building policy requirements;
- vanilla terrain placement validation;
- vanilla unit eligibility for forts and depots;
- vanilla railroad ownership/permitted-line checks;
- vanilla construction progress rules requiring friendly units for forts, depots, and telegraphs.

Whiskey should add:

- weekly construction intent by faction and theater;
- site-aware scoring instead of type-only bias;
- supply/rail/market/telegraph/fiscal coupling;
- historical faction doctrine;
- bounded patch steering with non-spam telemetry.

## Vanilla Contradictions This Spec Must Respect

1. Current #20 only touches private-economy candidate probabilities. It cannot choose a better IIP once vanilla has picked `bestiipplaces[type]`.
2. Supply depots are not proactive in vanilla. They are built only after unit supply drops below `GamePrefs.supplystatedepotconstruction`.
3. Forts are not private construction candidates. They use unit/site/order logic with one active fort order per faction.
4. Telegraph stations are real military `CBuilding` objects (`id_telegraphstation = 23`) with command effects, but they have no verified vanilla AI construction path. Any AI telegraph construction behavior is net-new, not cosmetic.
5. Railroad AI is random and calls `BattleUnits.Railroad.StartConstruction(alliance)` directly. That path appears fiscally lighter than the player UI path, which also adjusts subsidy funding and treasury.
6. Military construction only progresses while a friendly unit remains inside bugle range. Smart construction cannot ignore unit support.
7. `fortconstructionsites` is map-baked from future-appearing fort buildings during scenario initialization; Whiskey does not add new fort sites in Slice A.
8. `CBuilding.AddConstructionWish` can return null when `companyfoundings >= GameVars.debug_maxcompanyfoundings`; implementations must observe this cap before treating missing starts as patch failure.

## New Strategic Type

### ConstructionIntentLedger

A pure strategic ledger computed during weekly strategic review after fiscal intent, formation pressure, front sectors, army areas, and grand strategy are available.

It writes no game state directly. Harmony patches read it. It is recomputed each weekly review from current world state and is not persisted to the sidecar, so save/load cannot revive stale construction intent.

Inputs:

- alliance id;
- current date, `EraStage`, and `Policy.CurrentChapter`;
- active grand strategy and theater plans;
- fiscal posture, defended credit gate, supply protection, force cap warning, and logistics expansion flags;
- tax/subsidy pressure from `FiscalIntentLedger`;
- front-sector pressure: enemy proximity, objective pressure, capital threat, river/port/rail value;
- formation pressure: low supply, low ammo, recover directives, guard/mass/reinforce counts, top supply-starved theater;
- battle-history pressure and casualty/morale state;
- IIP state: owner, state, transport bottleneck, nearby units, nearby enemy units, resource prices, workforce, capital, hospitals, markets, banks, existing construction;
- direct unit wounded concentrations from nearby campaign units, especially `groupwounded` near IIPs; existing Whiskey battle history records battle casualties but does not yet provide per-theater wounded concentrations;
- military sites: vanilla fort construction sites, existing forts, depots, telegraphs, rail lines, ports, capitals, river crossings, frontier/corridor anchors;
- vanilla gates: `HasPolicy`, `IsRatingOkForConstruction`, `UseSubsidyForPurpose`, construction wish queue, unit availability, railroad ownership/permitted checks.

Outputs:

- `ConstructionPosture`: `Infrastructure`, `FieldSupply`, `DefensiveWorks`, `IndustrialExpansion`, `EmergencyHold`.
- `TopConstructionTheater`: stable theater key used by telemetry and patch steering.
- private building intents: type + IIP + score + reason.
- supply depot intents: unit + fallback IIP/site + score + reason.
- fort intents: unit + fort site + score + reason.
- telegraph intents: unit/corridor + connected station site + score + reason.
- railroad intents: line + score + reason.
- suppressions: construction type/site skipped and reason.
- telemetry signature: compact hash/string for no-spam logging.

## Construction Postures

### Infrastructure

Used when credit is acceptable and supply pressure is moderate.

Biases:

- banks where interest or capital weakness hurts credit;
- markets where transport bottlenecks reduce trade/supply;
- rail lines that connect owned IIPs in active theaters;
- hospitals near repeated wounded concentration;
- selective industry tied to faction doctrine.

### FieldSupply

Used when low supply, low ammo, or repeated recovery directives indicate existing armies cannot operate.

Biases:

- markets and rail near supply-starved army areas;
- supply depots near active field-army corridors;
- telegraph corridors only when they support command reach for active formations;
- suppresses expensive discretionary industry.

### DefensiveWorks

Used when a capital, port, river corridor, rail hub, or critical objective is threatened.

Biases:

- forts at vanilla fort sites that protect critical approaches;
- depots behind defensive lines, not isolated in contested pockets;
- connected telegraph stations from capital to defended corridor;
- hospitals near high-casualty defensive theaters.

### IndustrialExpansion

Used when credit is stable, supply coverage is adequate, and faction/era strategy calls for production growth.

Biases:

- Union: factories, iron works, foundries, lumber/brick support, markets/rail around industrial corridors.
- CSA: selective arms-supporting industry, agriculture/salt where historically and militarily useful, ports/rail only when available through vanilla gates. In 1861-1863, CSA arms-class industry may remain positive under stressed credit because that historical overinvestment helped keep field armies fighting; the debt consequence should remain visible in fiscal telemetry.

### EmergencyHold

Used under `EmergencySolvency`, bond-floor risk, severe capital threat, or field-army supply collapse.

Biases:

- preserve minimum supply/depots/markets for existing armies;
- banks only if vanilla gate permits and interest pressure is central;
- forts only for capital/key-port/key-river defense;
- suppress new discretionary industry, vanity naval support, excess rail, and unsupported telegraph expansion;
- allow only survival-linked CSA arms industry when it supports an existing army corridor and the bond floor is not imminent.

## Posture Precedence

Fiscal posture and construction posture interact by precedence, not by independent additive weights.

| Fiscal posture | Construction posture | Result |
|---|---|---|
| `Expansion` | `Infrastructure` / `IndustrialExpansion` | normal doctrine weights; Union expands broadly, CSA remains selective |
| `BalancedWar` | any | construction posture wins unless it would push the faction below the defended credit gate |
| `CreditDefense` | `Infrastructure` | fiscal wins on banks/markets; discretionary industry suppressed except CSA arms-class survival investments in 1861-1863 |
| `CreditDefense` | `FieldSupply` | supply/logistics wins for existing armies; new force-growth support stays suppressed |
| `CreditDefense` | `DefensiveWorks` | forts/depots only for capital, port, river, rail, or active army-corridor defense |
| `EmergencySolvency` | `EmergencyHold` | fiscal floor wins; only minimum supply, critical banks if gate permits, and critical defensive works survive |
| any | impossible vanilla gate | vanilla gate wins; ledger logs an unrealizable intent instead of forcing construction |

## Faction Doctrine

### CSA Doctrine

CSA construction should make fewer, more consequential investments.

Priorities are weighted, not strict. A lower-listed item can win when its score is urgent, vanilla-valid, and the higher-listed item is unavailable or already covered.

Weighted priorities:

| Priority | Trigger | Typical outputs |
|---|---|---|
| Richmond/Virginia army supply and command corridor | active Eastern pressure, Richmond threat, ANV low supply/ammo | market, depot, fort, connected telegraph, bank if credit pressure blocks future construction |
| Tennessee/Georgia supply corridor | western/central pressure, Atlanta/Chattanooga/Nashville corridor relevance | market, depot, selective rail, fort |
| Mississippi river and rail nodes | river-front pressure, Vicksburg/Memphis/New Orleans corridor | depot, fort, market, very selective rail |
| Key ports/blockade-running support | blockade-running/import strategy active and port defensible | port-adjacent market/depot/fort, connected telegraph |
| Banks/markets | `BalancedWar` or early `CreditDefense` before construction gate fails | bank, market |
| Arms survival industry | 1861-1863, arms/ammo shortage, active army support | iron works, foundries, factories, salt where useful |
| Telegraph chains | config enabled, safe corridor, connected chain possible | connected telegraph station |

CSA building-type doctrine:

| Era | Preferred building IDs | Bias | Suppression |
|---|---|---|---|
| 1861 early war | 0 Bank, 13 Market, 10 Iron Works, 8 Foundries, 12 Factories, 20 Saltworks | pre-position credit/logistics and arms survival | broad rail, exposed depots, non-arms industry |
| 1862-1863 war economy | 10 Iron Works, 8 Foundries, 12 Factories, 13 Market, 22 Supply Depot, 7 Fort | arms/supply survival can hurt credit if not near bond floor | vanity industry, ports without import value, isolated telegraph |
| 1864+ attrition | 22 Supply Depot, 7 Fort, 13 Market, 0 Bank only if gate permits | keep existing armies useful and defend critical corridors | new rail, broad industry, discretionary telegraph |

Suppressions:

- more than one CSA railroad line under construction at a time, unless a later implementation proves the line directly supports an arms/supply corridor;
- CSA rail lines not connected to active supply, arms, or economic corridors;
- exposed depots that cannot be protected by nearby formations;
- isolated telegraph stations;
- expensive non-arms industry during credit defense;
- construction that worsens upkeep or force growth while formations are already supply-starved.

### Union Doctrine

Union construction should support sustained pressure across multiple theaters without wasting its advantage.

Priorities are weighted, not strict.

Weighted priorities:

1. Occupied river/logistics hubs that support invasion corridors.
2. Coastal and river bases used for operational logistics.
3. Rail/market/depot depth for Richmond and western pressure.
4. Industrial and arms production when credit and transport are stable.
5. Forts around occupied hubs, ports, depots, river chokepoints, and only then threatened capital approaches.
6. Telegraph/rail support for long-distance command and army concentration.
7. Hospitals near repeated high-casualty operational corridors.

Union building-type doctrine:

| Era | Preferred building IDs | Bias | Suppression |
|---|---|---|---|
| 1861 early war | 13 Market, 0 Bank, 9 Hospital, 21 Military Academy | stabilize logistics and army quality | exposed border-state industry that can be raided/captured |
| 1862-1863 operational expansion | 13 Market, 22 Supply Depot, 9 Hospital, 10 Iron Works, 8 Foundries, 12 Factories | logistics for river/eastern operations plus arms growth | forts away from active/occupied hubs |
| 1864+ simultaneous pressure | 13 Market, rail lines, 22 Supply Depot, 9 Hospital, 12 Factories | sustain multi-theater pressure | rail spam where transport is saturated |

Suppressions:

- unsupported deep depots ahead of field-army coverage;
- forts far from current or expected front pressure;
- rail spam when too many lines are already under construction;
- industrial expansion when transport bottlenecks are already choking supply.

## Private Economy Building Rules

Patch surface:

- keep current #20 `EconomyConstructionPatch` around `AICampaign.UpdateCompanyFoundations`;
- optional consumer-state substitution before `UpdateCompanyFoundations` consumes `bestiipplaces[type]`; do not claim a separate scanner-level patch unless a later plan explicitly accepts a Transpiler.

Rules:

- Do not call `CBuilding.AddConstructionWish` directly from private-economy patches.
- Preserve `GameVars.buildingtypes[type].HasPolicy(alliance)`.
- Preserve subsidy funding checks and construction rating gate.
- Use current #20 type multiplier for low-risk steering.
- Do not replace `bestiipplaces[type]` in the first implementation slice. Start with observation and type weighting.
- Add a second layer only after telemetry proves the need: if vanilla picked a weak IIP for a type and the ledger has a stronger vanilla-valid IIP, replace `bestiipplaces[type]` and `bestiipplacesprob[type]` before `UpdateCompanyFoundations` consumes it.
- Treat replacement as single-shot. `UpdateCompanyFoundations` clears the candidate after consumption, so steering must tolerate one pass per scanner cycle.
- Before substitution, verify the full private-building validity contract:
  - substituted IIP is non-null;
  - `IIP.allianceowner == alliance`;
  - `IIP.currentlyunderconstruction == null`;
  - `GameVars.buildingtypes[type].aiplacement == true`;
  - `GameVars.buildingtypes[type].HasPolicy(alliance) == true`;
  - for general path: `subsidytype < 0`, `needsunitforplacement == false`, and `IsRatingOkForConstruction() == true`;
  - for subsidy path: `subsidytype >= 0`, `UseSubsidyForPurpose(alliance, subsidytype) == 1`, and `GetMissingSubsidyFundingCost(...) >= 0`;
  - POW camp: do not override vanilla's `Policy.CurrentChapter == 0` and `lastmonthscompanyfoundings.Contains(id_powcamp)` suppression;
  - repeated same-type starts must account for vanilla `lastmonthscompanyfoundings` probability penalty.

Scoring:

- Bank: interest pressure, low available capital, credit defense, CSA early pre-positioning.
- Market: transport bottleneck, active supply corridor, rail/port/town linkage.
- Hospital: wounded concentration, active front, secure rear position.
- Military Academy: military experience gap, stable credit, safe rear state.
- News Agency: high drafts/support pressure, state importance, morale/recruitment strategy.
- POW camp: high POW ratio, safe rear, no enemy nearby.
- Subsidized industry/agriculture: faction doctrine, resource price, policy availability, credit posture, supply relevance.

Safe rear definition:

- `frontline2.GetSideOnPosition(position) == alliance` when frontline data is initialized;
- no enemy campaign unit inside the relevant IIP/unit command range;
- distance from known frontline or enemy objective pressure exceeds a config threshold;
- not in a state/corridor the active theater ledger marks as contested unless the construction posture is `DefensiveWorks`.

## Supply Depot Rules

Patch surface:

- `AICampaign.CheckSupplyDepotConstruction(int)`.

Rules:

- Do not force depots for units vanilla considers moving, retreating, fighting, garrisoned, taking a town, or already depot-covered.
- Prefer steering eligible unit/site choice rather than bypassing vanilla's construction method.
- Build behind or beside an active corridor, not ahead of the army in exposed terrain.
- Require a nearby formation capable of remaining within bugle range long enough for progress.
- Emergency supply can outrank credit austerity, but only for existing field armies or critical capital/port defense.

Scoring:

- low supply and low ammo pressure;
- repeated `Recover` directives;
- distance to friendly town/depot/rail/market;
- enemy proximity and front side;
- theater priority;
- unit size and operational importance;
- ability to protect the site while construction progresses.

## Fort Rules

Patch surface:

- `AICampaign.CheckFortConstruction(int)`;
- `AICampaign.FortConstructionOrder.IsUnitAbleToConstructFort(...)` only for read/guard context, not broad override;
- avoid transpilers unless a Prefix/Postfix cannot safely steer site/order selection.

Rules:

- Preserve one active fort order per faction unless a later spec explicitly changes this.
- Preserve unit availability checks.
- Score vanilla `fortconstructionsites`; do not invent arbitrary fort sites in the first slice.
- Fort scoring is only applied to the intersection of `(eligible unit, sites within GamePrefs.rangefortconstructionsite of that unit, vanilla spacing/frontline gates, ledger preference)`.
- When no eligible unit is near a top-priority site, log `intent_unrealizable site=<name> reason=no-unit-in-range` or the specific failed gate; do not steer to an unrelated worse site merely to produce activity.
- Favor forts that protect capitals, ports, river crossings, rail hubs, depots, and fallback lines.
- Avoid forts too far forward, too close to existing forts, or outside supplied/protected corridors.

CSA fort priorities:

- Richmond approaches;
- Mississippi river line;
- Tennessee/Georgia approaches;
- key ports and rail chokepoints.

Union fort priorities:

- river/port logistics bases;
- occupied hubs that support invasion corridors;
- capital approaches only when actually threatened or map-baked sites make them available;
- western river chokepoints.

Implementation must dump or telemetry-record the available `fortconstructionsites` at scenario start before asserting any named fort priority is realizable.

## Telegraph Rules

Verified vanilla behavior:

- Telegraph Station is building ID 23 in `Config/buildingtypes.dat` and `CBuilding.id_telegraphstation`.
- `CBuilding.UpdateTelegraphConnections()` builds a local list of nearby telegraph stations within `GamePrefs.standardtelegraphrange`.
- `CBuilding.CheckTelegraphConnection()` marks a telegraph station connected only when it is complete, owned by the same alliance, and either within telegraph range of the alliance capital or chained through another owned connected station.
- Connected stations store `priortelegraph` and `telegraphconnectionid`, so vanilla models an actual chain back to the capital rather than an isolated aura.
- `Regiment.CheckTelegraphConnection()` gives a campaign unit `hastelegraphconnection` only when the unit is within `GamePrefs.standardtelegraphrange` of an owned connected station.
- Campaign order processing multiplies order delay by `GamePrefs.orderprocessingfactorwithintelegraphrange` when `hastelegraphconnection != null`.
- Campaign morale receives `GamePrefs.moraleimprovementtelegraphconnection` when the parent campaign unit has a telegraph connection.
- Manual placement rejects telegraph stations that do not connect, and military construction progress still depends on friendly unit support.

Strategic interpretation:

- Telegraphs are command-and-control infrastructure. They directly affect campaign order responsiveness and morale for connected field formations.
- Telegraph AI should prioritize active command corridors, not generic map coverage.
- Telegraph value is highest when a connected chain reaches a formation that is massing, guarding, recovering, or operating far from the capital where delayed orders would otherwise slow campaign response.

Patch surface:

- net-new AI path using `CBuilding.AddConstructionWish(CBuilding.id_telegraphstation, ...)` only after conservative validation;
- placement validation must mirror manual rules: station must connect to capital or an owned connected chain within `GamePrefs.standardtelegraphrange`.

Rules:

- Telegraph AI is optional and should be behind a config gate in the first implementation.
- Treat telegraph AI as military command infrastructure. The conservative default-off gate exists because the AI construction path is net-new and placement/unit-support validation is brittle, not because telegraphs are strategically irrelevant.
- Never build isolated stations.
- Build from capital outward to active army corridors.
- Prefer safe rear or defended corridor positions.
- Only build if a friendly unit can support construction progress.
- Do not spam multiple telegraphs. Initial cap is one active telegraph construction per faction; a later plan may raise this if smoke shows chains never complete.

Historical use:

- CSA: command links from Richmond to Virginia/Tennessee/Mississippi defensive corridors.
- Union: command/logistics links from Washington and major bases toward active invasion corridors.

## Railroad Rules

Patch surface:

- `AICampaign.UpdateRailroadConstruction(int, float)`.

Rules:

- Preserve `BattleUnits.Railroad.StartConstruction` ownership and permitted-line checks.
- Do not increase total railroad frequency until the fiscal asymmetry is resolved or accepted.
- First implementation observes and scores railroad starts only. Active railroad steering is disabled until a dedicated plan chooses one per-line mechanism.
- Acceptable active mechanisms are: a Transpiler inside the per-line loop, or a carefully bounded Postfix rollback that detects a non-preferred line whose `constructionprogress` flipped during the call and restores the prior state. A whole-method Prefix skip is not acceptable.
- If multiple rail lines are already under construction, suppress additional starts unless `LogisticsExpansion` is active and credit is stable.
- Explicitly log whether Whiskey is preserving vanilla's AI fiscal behavior or later normalizing it.
- CSA doctrine cap: at most one CSA railroad line under construction at a time, and only when the line supports an arms/supply corridor or critical theater logistics.

Scoring:

- connects owned IIPs in active theater;
- relieves transport bottleneck;
- improves supply to field armies;
- links capital/port/river/industrial hub;
- protected by current front position;
- not redundant with already built/under-construction rail.

## Fiscal Coupling

Construction must never optimize credit by starving armies.

Rules:

- `EmergencyHold` suppresses force-growth and vanity construction before minimum supply.
- `CreditDefense` allows banks/markets and critical logistics if they prevent worse future credit collapse.
- `FieldSupply` can keep depot/market/rail priorities alive even when discretionary industry is suppressed.
- CSA cannot receive free construction parity. Its advantage comes from better timing and less waste.
- Union should still respect transport saturation; more money does not justify unsupported overbuilding.

## Logging And Telemetry

Logging must prove the feature works without spam.

Required log lines:

- first-fire once line per patch;
- weekly or monthly construction intent signature when it changes;
- verbose-only candidate score details;
- actual construction starts observed by type/site/line;
- warning once per reflection/anchor failure.

Example non-verbose lines:

```text
[ConstructionIntent] alliance=1 posture=FieldSupply theater=Virginia top=Depot score=0.82 reason=low-supply-richmond-corridor
[Patch:Construction] alliance=1 surface=Fort site=RichmondApproach score=0.76 action=prefer reason=capital-defense
[Patch:Construction] alliance=0 surface=Railroad line=BaltimoreAndOhio score=0.69 action=allow reason=active-supply-corridor
```

Telemetry fields:

- date;
- alliance;
- construction posture;
- top theater;
- top private building candidate;
- top depot candidate;
- top fort candidate;
- top telegraph candidate;
- top railroad candidate;
- suppressions;
- actual starts since last heartbeat.

## Config

Add conservative config valves:

- `EnableConstructionIntentLedger` default `true`;
- `EnableConstructionSiteSteering` default `false` until observation validates IIP substitution;
- `EnableSupplyDepotSteering` default `false` until ledger/observer slices prove safe candidate selection;
- `EnableFortSteering` default `false` until fort site dumps and unit-range telemetry prove realizable sites;
- `EnableTelegraphAI` default `false` for first release;
- `EnableRailroadSteering` default `false`;
- `ConstructionTelemetry` default `true`;
- `ConstructionVerboseLogging` default `false`;
- `MaxActiveTelegraphConstructionsPerFaction` default `1`;
- `MaxRailroadStartsPerFactionPerMonth` default `1` unless vanilla already started one.

## Safety Rules

- Never throw from a patch.
- Never mutate strategic ledger state from Harmony patches.
- Never edit game install data.
- Never bypass construction rating gates for private/general buildings.
- Never bypass unit readiness/supply/battle/retreat checks for forts or depots.
- Never place telegraphs that fail connection logic.
- Never force construction in enemy-controlled or obviously exposed locations unless the front/theater ledger marks it as a defended hold corridor and a supporting unit is present.

## Acceptance Criteria

Functional:

- CSA still weaker economically than Union, but avoids measurably wasteful early construction.
- CSA maintains at least one plausible Richmond/Virginia logistics/defense focus in early war.
- Union invests in logistics/rail/market/depot support for active pressure instead of random low-value building starts.
- Supply-starved theaters produce depot/market/rail intent before new discretionary industry.
- Fort choices align with capitals, ports, river crossings, rail hubs, or fallback lines.
- Telegraph AI, if enabled, builds only connected chains.
- When active railroad steering is explicitly enabled, railroad starts are no longer random when the ledger has a clear preferred line.

Measurable proxies:

- starts per faction per month by building type and railroad line;
- completion rate for construction starts;
- percentage of completed buildings captured or burned within 90 in-game days;
- ratio of CSA arms-class industry starts to general/discretionary industry by era stage;
- CSA railroad starts capped to doctrine unless explicitly overridden;
- share of depot/market/rail starts in theaters with low-supply or low-ammo pressure;
- count of unrealizable top intents and failed vanilla gates.

Technical:

- Console tests cover pure scoring and posture decisions.
- `./build.sh` passes.
- In-game log shows first-fire lines and at least one construction intent heartbeat.
- No repeated warning spam.
- No direct construction calls from private-economy type scoring.
- No DLL-affecting work is reported ready until build, deploy, and SHA-256 verification complete.

## Implementation Slices

1. Pure ledger and tests: no Harmony behavior change.
2. Telemetry and actual-start observer.
3. Private building type weighting review over #20; no IIP substitution until observer data proves need.
4. Supply depot steering.
5. Fort site steering.
6. Railroad filtering/weighting.
7. Optional telegraph AI behind config.

The first implementation plan should ship slices 1-2 only, then observe vanilla construction starts for at least one campaign month. Slice 3 can be enabled only after the observer shows a repeatable gap between ledger intent and vanilla starts and the full vanilla-validity contract is implemented in tests.
