# Vanilla Construction Deep Dive

Date: 2026-05-04

Scope: verified vanilla construction, building, telegraph, supply depot, fort, and railroad logic from `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` plus shipped `Config/buildingtypes.dat`. This is a findings document for the later `ConstructionIntentLedger`; it is not an implementation plan.

## Bottom Line

The current Whiskey `EconomyConstructionPatch` is useful but narrow. It only biases vanilla private-economy candidates in `AICampaign.UpdateCompanyFoundations`. It does not decide where to build, does not touch forts, does not touch supply-depot unit/site selection, does not build telegraph stations, and does not steer railroad construction.

If we want smart building, the next slice needs a real `ConstructionIntentLedger` that scores:

- building type;
- exact site / IIP / railroad line / fort site;
- theater role;
- supply and transport pressure;
- front security;
- fiscal posture and credit gates;
- faction doctrine.

Vanilla already has separate construction systems. One patch cannot responsibly cover all of them.

## Building Type Data

`GameVars.ImportBuildingTypes` reads `Config/buildingtypes.dat` into `GameVars.buildingtypes` at decompile line 65605. The important fields are:

- `aiplacement`: whether the AI/private placement scanner can create this building;
- `subsidytype`: `-1` means private/general construction; `>= 0` means subsidy-funded lane;
- `needsunitforplacement`: requires an army/unit placement path;
- `requiredpolicies`: packed policy IDs checked by `BuildingType.HasPolicy`.

Relevant shipped building IDs:

| ID | Name | AI placement | Subsidy | Needs unit |
|---:|---|---|---:|---|
| 0 | Bank | yes | -1 | no |
| 7 | Fort | no | -1 | yes |
| 9 | Hospital | yes | -1 | no |
| 13 | Market | yes | -1 | no |
| 18 | News Agency | yes | -1 | no |
| 19 | Prison Camp | yes | -1 | no |
| 21 | Military Academy | yes | -1 | no |
| 22 | Supply Depot | yes | -1 | yes |
| 23 | Telegraph Station | no | -1 | yes |

Industrial/farm buildings are mostly `aiplacement=true` and subsidy-funded. Mines and ports are not AI-placed in this path.

## Private Economy Buildings

Primary anchors:

- `AICampaign.UpdateCompanyFoundationList()` at line 15082;
- `AICampaign.UpdateCompanyFoundations(int, float)` at line 15000;
- `CBuilding.AddConstructionWish(...)` at line 97479;
- `CBuilding.WorkDownConstructionWishes()` at line 97538;
- `CBuilding.Place(...)` at line 96163.

Vanilla uses a two-stage pipeline:

1. `UpdateCompanyFoundationList()` scans IIPs over time and records one best IIP plus probability per building type in `GameVars.alliance[alliance].bestiipplaces[type]` and `bestiipplacesprob[type]`.
2. `UpdateCompanyFoundations()` consumes those cached candidates and may start one construction wish, then returns.
3. `AddConstructionWish()` creates an off-map placeholder building and queues an `AIPlacement`.
4. `WorkDownConstructionWishes()` searches terrain around the origin, places the real building, swaps references, and removes the placeholder.

Candidate selection in `UpdateCompanyFoundationList()` is not strategic. It is mostly local-economy heuristics:

- high relative resource price maps through `Economy.resources[resource].buildingtoproduceid`;
- transport bottleneck can override to `Market`;
- POW pressure can override to `Prison Camp`;
- nearby wounded units can override to `Hospital`;
- military experience gap can override to `Military Academy`;
- worse interest plus weak local capital can override to `Bank`;
- high drafts can override to `News Agency`.

Important limitation: the scanner stores only the current best IIP per building type. A later patch that only changes `bestiipplacesprob` can change which type vanilla chooses, but cannot choose a better site for that type unless it influences the scanner earlier.

## Funding And Rating Gates

`UpdateCompanyFoundations()` has two distinct paths:

- subsidy path: loops subsidy lanes first and can start subsidy-funded construction when `GetMissingSubsidyFundingCost(...) >= 0`;
- general/private path: only runs if `GameVars.alliance[alliance].IsRatingOkForConstruction()` is true and the building does not require unit placement.

This matters for credit realism:

- banks, markets, hospitals, military academies, news agencies, and POW camps are rating-gated through the general path;
- subsidy-funded industry can still start if subsidy funding is sufficient;
- forts, depots, and telegraphs are outside this private path despite being in `buildingtypes.dat`.

`CBuilding.Place(... newlycreated:true, pay:true)` charges costs immediately on the placeholder. When the terrain-search replacement is placed, `WorkDownConstructionWishes()` calls `Place(... pay:false)`.

## Supply Depots

Primary anchors:

- `AICampaign.CheckSupplyDepotConstruction(int)` at line 14659;
- AI update job order at lines 11499-11513;
- `CBuilding.Update` military-construction progress check at line 96626.

Vanilla supply-depot construction is reactive, not planned theater logistics.

The AI only considers a depot when a unit:

- has group supply below `GamePrefs.supplystatedepotconstruction`;
- has no connected supply depot;
- is not moving, retreating, in battle, garrisoned, or taking a town;
- has no nearby relevant construction/depot coverage;
- has a `closestiipforsupply`.

It first tries to move the unit toward a nearby friendly town or depot. Only if there is no usable nearby installation does it call:

`CBuilding.AddConstructionWish(CBuilding.id_supplydepot, unit.position, unit.closestiipforsupply, alliance)`

Then it marks one construction as already done for the pass and assigns current order type `10` ("Supply Depot").

Construction progress for forts, depots, and telegraphs requires a friendly unit inside bugle range. If no friendly unit remains close, the construction timer does not progress and the building is eventually removed after `GamePrefs.timetoremovemilitarybuildings`.

Implication: Whiskey should not treat current depot logic as a proactive supply network. It waits until a unit is already supply-stressed.

## Forts

Primary anchors:

- `AICampaign.FortConstructionOrder` at line 9622;
- `AICampaign.CheckFortConstruction(int)` at line 16347;
- AI update job order at lines 11514-11523.

Vanilla fort construction is also reactive and constrained.

Major constraints:

- one active fort construction order per faction via `IsFactionAlreadyConstructingFort`;
- candidate unit must be supplied, idle, not retreating, not in battle, not garrisoned, not in offensive/defensive operations, not building a depot, not a raid unit, and not under W&L player commander;
- candidate is usually a smaller field unit, or a capital-defense unit;
- site must come from vanilla `fortconstructionsites`;
- site must be near the frontline and not too close to another fort/order;
- once the unit reaches the site, `UpdateFortConstructionOrders()` starts `CBuilding.AddConstructionWish(CBuilding.id_fort, ...)`.

Implication: "smart forts" should not just boost construction probability. There is no probability lane here. We need to score vanilla fort sites by theater role, capital/river/rail/port value, enemy approach, and supply state, then steer the site/order selection without bypassing unit feasibility.

## Telegraph Stations

Primary anchors:

- `CBuilding.id_telegraphstation = 23` at line 95089;
- manual placement validation at lines 96345-96415;
- `CBuilding.UpdateTelegraphConnections()` at line 95904;
- `CBuilding.CheckTelegraphConnection()` at line 95918;
- player construct button path at line 206208;
- W&L `DLC_WL.ConstructBuilding(...)` at line 45977 only handles forts and supply depots.

I found no vanilla AI construction path for telegraph stations. They are manual-player military buildings.

Telegraph behavior:

- a station connects if it is intact, owned, and either within `GamePrefs.standardtelegraphrange` of the capital or chained through another owned connected station;
- placement validation rejects a telegraph station that would not connect;
- regiments periodically search nearby connected owned telegraph stations;
- connected units get order-processing and morale effects.

Implication: AI telegraph construction would be net-new AI behavior. It should be conservative and historical: build connected chains from capital to active army corridors, not isolated stations.

## Railroads

Primary anchors:

- `AICampaign.UpdateRailroadConstruction(int, float)` at line 16052;
- macro loop call at line 81741;
- `BattleUnits.Railroad.StartConstruction(...)` at line 77818;
- `BattleUnits.Railroad.UpdateConstruction()` at line 77859;
- player `RailroadList.StartConstruction(...)` at line 214858.

Vanilla AI railroad construction is random over eligible railroad lines:

- base probability is `GamePrefs.probrailroadconstructionperyear`;
- probability increases with industrialization policies;
- each unbuilt line rolls independently;
- `StartConstruction` only checks all required IIPs are owned and the line is permitted.

Important asymmetry: the AI path calls `BattleUnits.railroad[j].StartConstruction(alliance)` directly. The player UI path calls `RailroadList.StartConstruction`, which also adjusts transport subsidy funding and treasury. In the decompiled path reviewed here, AI railroad starts do not go through the same treasury/subsidy charge.

Implication: if Whiskey steers AI railroads, it is steering a vanilla AI path that appears fiscally lighter than the player path. We should either leave vanilla's fiscal behavior intact but avoid over-triggering it, or explicitly decide to normalize railroad costs later. Do not accidentally turn random free rail into precise free rail spam.

## Current Whiskey Coverage

Current #20 `EconomyConstructionPatch`:

- patches `AICampaign.UpdateCompanyFoundations`;
- biases `bestiipplacesprob[type]`;
- only touches already-valid vanilla candidates;
- does not call construction APIs directly;
- cannot choose a different site once `UpdateCompanyFoundationList()` has selected the best IIP for a type;
- does not affect supply depots, forts, telegraphs, or railroads.

This is the right low-risk fiscal patch, but it is not a full building AI.

## Recommended Next Slice

Create a `ConstructionIntentLedger` before adding more patches.

Inputs:

- fiscal posture and credit gates from `FiscalIntentLedger`;
- formation pressure: low supply, low ammo, recovery counts, top supply-starved theater;
- front/theater role from army-area and front-sector ledgers;
- IIP data: owner, state, transport bottleneck, resource prices, available capital/workforce, nearby hospitals/markets/banks, exposed-front distance;
- military sites: fort construction sites, existing forts, supply depots, telegraph stations, rail lines, ports, capitals, river crossings;
- vanilla eligibility: `HasPolicy`, rating gate, unit availability, construction wish queue, ownership/permitted line checks.

Outputs:

- economy building weights by type and IIP;
- depot priorities by unit/theater/site;
- fort priorities by vanilla fort site and unit;
- telegraph chain priorities by connected corridor;
- railroad line priorities;
- emergency suppressions.

Patch strategy:

1. Keep #20 for private building type weighting, but add scanner-level site scoring if we need to replace vanilla's best-IIP choice.
2. Add a supply-depot steering patch only after the ledger can prove which theater/site is starved; do not force depot construction for units that vanilla says are moving, fighting, retreating, or already covered.
3. Add a fort steering patch around fort site/order selection, not around construction completion.
4. Add AI telegraphs only as a conservative connected-chain feature; this is net-new behavior and must be logged.
5. Add railroad steering by filtering or weighting `UpdateRailroadConstruction` candidates; be explicit about whether vanilla AI's direct `StartConstruction` fiscal asymmetry is preserved.

Historical defaults:

- CSA: Richmond/Virginia supply, Tennessee/Georgia corridor, Mississippi river/rail nodes, key ports, connected telegraph corridors, defensive forts at capital/river/port approaches, markets/rail only where they keep field armies useful.
- Union: logistics depth for simultaneous pressure, rail/market expansion in active invasion corridors, depot coverage for deep drives, forts around exposed capitals/ports/river keys, telegraph/rail support for command reach.

Acceptance telemetry:

- per-month top construction intent by faction and theater;
- actual vanilla construction starts by type/site;
- suppressed opportunities and reason;
- railroad starts by line;
- fort/depot/telegraph starts by site and owning unit;
- no repeated log spam: one first-fire line per patch, signature-change summaries, and verbose-only candidate detail.
