# Naval & River War — Design

**Status:** approved direction (user, 2026-10-14: include naval, built last). Built after the campaign planner (`2026-10-14-campaign-planner-design.md`) because it consumes the planner's theater axes.

## Vanilla model (decompile, build 14703111)

- **Fleets** are `Regiment` with `unittyp == 17` + `Fleet` (`fleetref`, 100229): `attachedships`, `abletomoveonsea/river` (all ships must allow, 100519-100535), condition, `daysatsea`. Ship types from `Config/shiptypes.dat` (41 types: river-only 21/22/23/25/27; river+sea 0-5/9/19/24/26/28/38; rest sea-only).
- **Ports** = `IIP` with `IIPType == 1`, `isseaport`/`isriverport` (103946-103963); `AIFaction.UpdatePorts` (10425) builds `ownports`, `enemyportssea/river` and parallel `portvaluessea/river = GetTransportCapacity()·(1-blocked)`.
- **Navmesh:** fleets path on Rivers (area 4) if river-capable and Sea (8) if sea-capable; everything else costs 500000 (130395-130465).
- **Fleet AI** `CheckFleetMovements` (13148-13387, job 7): **one dispatched fleet per faction at a time** (`portoperation` throttle 13203-13210); samples 5 random own fleets passing `FleetIsReadyForAction` (16825: condition ≥ 0.75, `daysatsea` ≥ 20, idle, readiness ≥ 2, not an escort); target = nearest own blocked port (> 0.1, order 3 raid) else enemy port with lowest distance ÷ value (order 1 blockade); station 0.9 × combat range off the port, re-offset from forts within 1.25 × range; sea moves via `DefensiveMovingOrder`, river moves via `SetWaypoint`; abort if the path leaves water (13348). Orders: 0 none, 1 blockade, 2 patrol (never set by AI), 3 raid.
- **Blockade** (`IIP.UpdateBlockades` 104086-104157): enemy order-1 fleets within commander range (and raiding cavalry) add `blockadeefficencyvalue` (113775-113833: √(raid − guard efficiency) × 0.004; raid = slowest speed × combat value × 5 × cunning); reduced by Blockade Busters, organized blockade running, project 106. Effects: trade flow × (1 − blocked) (104418), weapon imports toward 0.1× (64120), shipbuilding × (1 − blocked) (66658), embarkation time × (1 + blocked)².
- **Ship construction** `CheckShipConstruction` (11998-12089, job 12): probability from own blockade ratio (+ offensive term when own ≤ 0.1), fleet expense target 0.20, river roll ÷ 5; the "fast" flag is never set → only ships < 10 kn; W&L CIC gate has no alliance test (bug, handled in `2026-10-14-wl-chain-of-command-design.md` §F).
- **Amphibious** `CheckForSeaInvasion` (16407, job 22; `debug_seainvasionsactive` hard-coded true 64453): probability per year = chapter factor [0, .2, .7, .8, .9] × faction factor (USA 2.0, **CSA 0.0**, GB 3.0); `SeaInvasionForce` (9214-9294) **raises a brand-new division** at a random safe own sea port, picks a scene `SeaInvasion` spot with enemy objectives and enemy-minus-own < 10000 (nearest + up to 5000 random); `UpdateSeaInvasionForces` (9466-9601) states 1 raise → 2 move (sea-only order + escort ≥ 8 ships) → 3 landed (escort bombards nearest sea fort; force marches on spot objectives).
- **River war:** no gunboat participation in land battles (`EstablishCampaignBattle` fleet-vs-land only for transport interception, autocalc 5, or fort bombardment, autocalc 4 — 80700-80744). River forts are ordinary forts; river fleets path past them via `SetWaypoint` and get engaged (fort-navy autocalc). AI land units ride rivers by default (`userivers = true`, 112862).

## Goals

Make both navies pursue the real naval war through vanilla's own machinery: an Anaconda blockade that concentrates on the ports that matter, a Union river war that advances with the western armies and reduces river forts, CSA blockade running/raiding, and Union amphibious operations aimed by the campaign plan using existing troops — never writing to player-owned fleets (W&L: only a CIC player owns fleets; `dlcw_isundercommander`) and never to alliance 2.

## Design

All runtimes run on the daily strategic tick (coordinator-driven, not from patches). Patches stay read-only except the two argument/value biases below. Gates on every write: AI-owned unit (`TacticalWriteGuard`-equivalent campaign check: alliance ≠ player alliance or `ai_vs_ai`, not `dlcw_isundercommander`, not `IsMovedByPlayer`), alliance < 2, not the player-CIC alliance.

1. **`NavalTargetScorer` (pure) + `NavalPortValueBiasPatch` (Postfix on `AIFaction.UpdatePorts`):** multiply `portvaluessea/river[i]` by a daily weight keyed by IIP instance id. Weight = f(asset role from `AssetRoleCatalog` — BlockadeRunnerPort, RiverControlHub, KeyFort; campaign-planner theater priority; current blocked value; era). Effect: vanilla's own value/distance pick prefers the ports that matter. No orders written.
2. **`BlockadeStationLedger` (pure) + `NavalOrderRunner` (runtime):** each day rank enemy sea ports by `NavalTargetScorer`; assign additional ready idle fleets (orders 0, not `portoperation`, not escort) to the top uncovered ports using vanilla's own station recipe and movement call (`DefensiveMovingOrder` for sea, `SetWaypoint` for river) + `Regiment.SetFleetOrders(1)`. Cap N assignments per side per day (default 2). Release a fleet (orders 0) when condition < 0.75 or `daysatsea` < 20 so vanilla resupply resumes. CSA variant: fast fleets → order 3 near own blocked ports (blockade busting); Sea Raider (perk 6) fleets → enemy trade nodes.
3. **`RiverFlotillaRuntime`:** pair river-capable AI fleets with the planner's River-theater axis; advance them to the next enemy river port/fort on that axis (`SetWaypoint` on Rivers navmesh, vanilla path check); order 1 at enemy river ports; escort embarked friendly armies on the axis with `LinkUnitTo` (vanilla escort pattern 9316-9355) to deter transport interception. No in-battle gunboat support (vanilla has none).
4. **`AmphibiousOperationRuntime`:** when the campaign planner selects a coastal objective for the Union (a `SeaInvasion` spot whose `ObjectivesInRange` match the planned objective), assemble a `SeaInvasionForce` from an existing idle Union corps near a friendly sea port instead of vanilla's fresh-division spawn, set it to state 2 and add it to `AICampaign.seainvasionforce` (vanilla constructor/state machine drives movement, escort, landing, bombardment, march). Respect vanilla's cap of 2 live invasions. CSA: none (vanilla factor 0; no change).
5. **`ShipTypeDoctrinePatch` (Prefix rewriting arguments of static `GrabShipType`):** set `fast = true` for CSA raider/runner construction and Union blockade cruisers when the doctrine calls for them; never blocks the call.

## Components

| Unit | Kind |
|---|---|
| `Strategic/Naval/NavalTargetScorer.cs` | pure |
| `Strategic/Naval/BlockadeStationLedger.cs` | pure |
| `Strategic/Naval/NavalOrderRunner.cs` | runtime |
| `Strategic/Naval/RiverFlotillaPlanner.cs` | pure |
| `Strategic/Naval/RiverFlotillaRuntime.cs` | runtime |
| `Strategic/Naval/AmphibiousOperationPlanner.cs` | pure |
| `Strategic/Naval/AmphibiousOperationRuntime.cs` | runtime |
| `Patches/NavalPortValueBiasPatch.cs` | patch (Postfix, value bias) |
| `Patches/ShipTypeDoctrinePatch.cs` | patch (Prefix, argument rewrite) |

Config: one default-on flag per runtime (`Enable Naval Port Targeting`, `Enable Blockade Stations`, `Enable River Flotillas`, `Enable Planned Amphibious Operations`, `Enable Ship Type Doctrine`) with honest descriptions. Telemetry: daily `[Naval]` summary (average blockade ratio per side, fleets on station, river flotilla positions, live invasions).

## Testing

Pure scorers/ledgers/planners harness-tested (port ranking by era and role, assignment caps and release thresholds, river axis next-target, amphibious corps/spot selection incl. "no suitable spot" and "player-owned corps excluded"). Runtime verified by build + smoke (`[Naval]` summary; fleets holding stations; Union flotilla advancing on Henry/Donelson axis; invasion force built from an existing corps).

## Out of scope / not addressed

Vanilla `Random.Range(0, Count-1)` off-by-one in `GrabShipType`/`GrabFleet`/`RaiseFleet` (fixing needs a Transpiler — not done without user consent); in-battle gunboat support; CSA amphibious offense; changing blockade math.
