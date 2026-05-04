# Construction Intent Ledger Design

Status: draft design spec, grounded in verified vanilla construction behavior.
Scope: Slice A enrichment for AI construction decisions: private economy buildings, supply depots, forts, telegraph stations, and railroads. This spec does not rewrite the economy, edit game install data, or bypass vanilla placement/unit/funding gates.

## Source Findings

This spec depends on the verified vanilla deep dive:

- `docs/superpowers/specs/2026-05-04-construction-vanilla-deep-dive.md`
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
- `CBuilding.UpdateTelegraphConnections()` at line 95904
- `CBuilding.CheckTelegraphConnection()` at line 95918

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
4. Telegraph stations have no verified vanilla AI construction path. Any AI telegraph behavior is net-new.
5. Railroad AI is random and calls `BattleUnits.Railroad.StartConstruction(alliance)` directly. That path appears fiscally lighter than the player UI path, which also adjusts subsidy funding and treasury.
6. Military construction only progresses while a friendly unit remains inside bugle range. Smart construction cannot ignore unit support.

## New Strategic Type

### ConstructionIntentLedger

A pure strategic ledger computed during weekly strategic review after fiscal intent, formation pressure, front sectors, army areas, and grand strategy are available.

It writes no game state directly. Harmony patches read it.

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
- CSA: selective arms-supporting industry, agriculture/salt where historically and militarily useful, ports/rail only when available through vanilla gates.

### EmergencyHold

Used under `EmergencySolvency`, bond-floor risk, severe capital threat, or field-army supply collapse.

Biases:

- preserve minimum supply/depots/markets for existing armies;
- banks only if vanilla gate permits and interest pressure is central;
- forts only for capital/key-port/key-river defense;
- suppress new discretionary industry, vanity naval support, excess rail, and unsupported telegraph expansion.

## Faction Doctrine

### CSA Doctrine

CSA construction should make fewer, more consequential investments.

Priorities:

1. Richmond/Virginia army supply and command corridor.
2. Tennessee/Georgia supply corridor.
3. Mississippi river and rail nodes.
4. Key ports and blockade-running support when available.
5. Defensive forts on capital, river, rail, and port approaches.
6. Banks/markets early enough to protect credit and trade flow.
7. Selective industry only where supply, manpower, and credit can sustain it.
8. Telegraph chains from capital to active defensive/field-army corridors.

Suppressions:

- rail lines not connected to active supply or economic corridors;
- exposed depots that cannot be protected by nearby formations;
- isolated telegraph stations;
- expensive industry during credit defense unless tied to arms/supply survival;
- construction that worsens upkeep or force growth while formations are already supply-starved.

### Union Doctrine

Union construction should support sustained pressure across multiple theaters without wasting its advantage.

Priorities:

1. Rail/market/depot depth for Richmond pressure.
2. Western river logistics, including Tennessee/Cumberland/Mississippi approaches.
3. Industrial and arms production when credit is stable.
4. Forts around threatened capitals, ports, depots, and river chokepoints.
5. Telegraph/rail support for long-distance command and army concentration.
6. Hospitals near repeated high-casualty operational corridors.

Suppressions:

- unsupported deep depots ahead of field-army coverage;
- forts far from current or expected front pressure;
- rail spam when too many lines are already under construction;
- industrial expansion when transport bottlenecks are already choking supply.

## Private Economy Building Rules

Patch surface:

- keep current #20 `EconomyConstructionPatch` around `AICampaign.UpdateCompanyFoundations`;
- add scanner-level steering only if needed around `AICampaign.UpdateCompanyFoundationList`.

Rules:

- Do not call `CBuilding.AddConstructionWish` directly from private-economy patches.
- Preserve `GameVars.buildingtypes[type].HasPolicy(alliance)`.
- Preserve subsidy funding checks and construction rating gate.
- Use current #20 type multiplier for low-risk steering.
- Add a second layer only when the ledger needs site selection: if vanilla picked a weak IIP for a type and the ledger has a stronger vanilla-valid IIP, replace `bestiipplaces[type]` and `bestiipplacesprob[type]` before `UpdateCompanyFoundations` consumes it.

Scoring:

- Bank: interest pressure, low available capital, credit defense, CSA early pre-positioning.
- Market: transport bottleneck, active supply corridor, rail/port/town linkage.
- Hospital: wounded concentration, active front, secure rear position.
- Military Academy: military experience gap, stable credit, safe rear state.
- News Agency: high drafts/support pressure, state importance, morale/recruitment strategy.
- POW camp: high POW ratio, safe rear, no enemy nearby.
- Subsidized industry/agriculture: faction doctrine, resource price, policy availability, credit posture, supply relevance.

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
- Favor forts that protect capitals, ports, river crossings, rail hubs, depots, and fallback lines.
- Avoid forts too far forward, too close to existing forts, or outside supplied/protected corridors.

CSA fort priorities:

- Richmond approaches;
- Mississippi river line;
- Tennessee/Georgia approaches;
- key ports and rail chokepoints.

Union fort priorities:

- Washington and exposed northern approaches;
- river/port logistics bases;
- occupied hubs that support invasion corridors;
- western river chokepoints.

## Telegraph Rules

Patch surface:

- net-new AI path using `CBuilding.AddConstructionWish(CBuilding.id_telegraphstation, ...)` only after conservative validation;
- placement validation must mirror manual rules: station must connect to capital or an owned connected chain within `GamePrefs.standardtelegraphrange`.

Rules:

- Telegraph AI is optional and should be behind a config gate in the first implementation.
- Never build isolated stations.
- Build from capital outward to active army corridors.
- Prefer safe rear or defended corridor positions.
- Only build if a friendly unit can support construction progress.
- Do not spam multiple telegraphs per month; one per faction per month is the initial ceiling.

Historical use:

- CSA: command links from Richmond to Virginia/Tennessee/Mississippi defensive corridors.
- Union: command/logistics links from Washington and major bases toward active invasion corridors.

## Railroad Rules

Patch surface:

- `AICampaign.UpdateRailroadConstruction(int, float)`.

Rules:

- Preserve `BattleUnits.Railroad.StartConstruction` ownership and permitted-line checks.
- Do not increase total railroad frequency until the fiscal asymmetry is resolved or accepted.
- Initial steering should filter random starts toward ledger-preferred lines and away from irrelevant lines.
- If multiple rail lines are already under construction, suppress additional starts unless `LogisticsExpansion` is active and credit is stable.
- Explicitly log whether Whiskey is preserving vanilla's AI fiscal behavior or later normalizing it.

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
- `EnableConstructionSiteSteering` default `true`;
- `EnableSupplyDepotSteering` default `true`;
- `EnableFortSteering` default `true`;
- `EnableTelegraphAI` default `false` for first release;
- `EnableRailroadSteering` default `true`;
- `ConstructionTelemetry` default `true`;
- `ConstructionVerboseLogging` default `false`;
- `MaxTelegraphsPerFactionPerMonth` default `1`;
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

- CSA still weaker economically than Union, but avoids obviously wasteful early construction.
- CSA maintains at least one plausible Richmond/Virginia logistics/defense focus in early war.
- Union invests in logistics/rail/market/depot support for active pressure instead of random low-value building starts.
- Supply-starved theaters produce depot/market/rail intent before new discretionary industry.
- Fort choices align with capitals, ports, river crossings, rail hubs, or fallback lines.
- Telegraph AI, if enabled, builds only connected chains.
- Railroad starts are no longer random when ledger has a clear preferred line.

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
3. Private building site/type scoring extension over #20.
4. Supply depot steering.
5. Fort site steering.
6. Railroad filtering/weighting.
7. Optional telegraph AI behind config.

The first implementation plan should ship slices 1-3 only unless runtime evidence shows depot/fort/rail steering can be added safely in the same pass.
