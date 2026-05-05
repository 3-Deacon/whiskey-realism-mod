# Defense Intent Ledger Design

Status: approved draft design spec for the next strategic-defense slice.
Scope: land-side coastal defense for pre-invasion guard posture and detected naval landing response. This spec does not rewrite fleet AI, fort/depot construction, tactical battle AI, or vanilla sea-invasion creation.

## Source Findings

This spec is grounded in current Whiskey code and verified vanilla anchors from `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

Current Whiskey anchors:

- `CampaignMapLedger` captures active-map towns, represented states, forts, sea harbors, and river harbors.
- `CampaignMapRuntime` builds the map ledger from vanilla `BattleUnits.towns`, `BattleUnits.harbors`, and `BattleUnits.fort`.
- `DefenseForceSizer` scores proportional response candidates so a small threat does not automatically pull an oversized army.
- `DefensiveOpsPatch` currently only augments `AICampaign.AssignUnitToDefendCapital`; it does not yet steer non-capital coastal defense or landing response.

Vanilla anchors:

- Top-level class `SeaInvasionForce` at line 9188 tracks `invasionforce`, `invasionforcediv`, `assignedescort`, `seainvasionspot`, `sourceport`, and `currentstate`. It is **not** nested in `AICampaign` — reflection lookups must use `AccessTools.TypeByName("SeaInvasionForce")`, not `AccessTools.Inner(typeof(AICampaign), "SeaInvasionForce")`.
- The static list field `AICampaign.seainvasionforce` (`public static List<SeaInvasionForce>`) lives at line 11089. Vanilla itself null-checks this field; ledger code must do the same.
- `SeaInvasionForce.GetSuitableLocation(...)` at line 9411 chooses landing spots with enemy objectives and insufficient friendly strength in range.
- `SeaInvasionForce.UpdateSeaInvasionForces()` at line 9466 moves invasion forces by sea and removes invasion entries when the invasion force retreats or `seainvasionspot.GetNumberOfEnemyObjectives` reaches zero (line 9523-9525). The "removing invasion of ..." debug log is the canonical end-of-threat signal.
- `SeaInvasionForce.currentstate` only takes values `1` (raising/forming, line 9291) and `2` (moving by sea, set at lines 9371/9385 once `groupstrength > minimumgroupstrengthforseainvasions`). There is **no** vanilla `currentstate >= 3`. The `1..6` ladder seen in the decompile around lines 9069-9125 belongs to the unrelated top-level class `RaidForce` at line 8869. Landed-phase detection must be derived from other vanilla signals (see Threat Detection).
- `AICampaign.CheckForSeaInvasion(int)` at line 16407 creates invasion forces by chapter/faction probability.
- `SeaInvasion` at line 140972 exposes the public `ObjectivesInRange` array and the public `GetUnitStrengthInRange(int allianceid, bool returnbalance, int shipweight = 1000)` and static `GetUnitStrengthInRange(List<Regiment>, ...)` helpers. The `unitsinrange` field is **private** and should not be reached by reflection. `SeaInvasion.Update` is gated by `GameVars.debug_seainvasionsactive` and by `GameVars.frame >= 50`; if the global gate is off, `unitsinrange` is never populated regardless of campaign state. `UpdateUnitsInRange` advances one element of `BattleUnits.campaignunitlist` per Unity frame and only commits the temporary list on full cycle, so the public strength helper can lag many game-hours at high campaign speed.
- Top-level class `RaidForce` at line 8869 owns its own `currentstate` (1..6) and is checked across vanilla via `RaidForce.IsRaidUnit(unit)`. Coastal raids that arrive via `RaidForce` (not `SeaInvasionForce`) are still defense-relevant and must be covered by the ledger.
- `AICampaign.CheckForDefensiveOperations(int)` at line 13505 is the vanilla land-defense response surface. It is rate-gated by `aifaction[i].lastdefensiveoperationcheck` and the `probdefensiveop[allianceid]` × `aiagressiveness[Policy.CurrentChapter]` × `35f` × `usedcampaignagressiveness` random gate, and commits at most one defensive operation per call before `return;`. A Postfix can ADD a defender to existing state but cannot make vanilla pick a different enemy or candidate.
- `AICampaign.AssignUnitToDefendCapital(int)` at line 11668 is the capital-defense surface; shipped patch #4 already Postfixes this method to add one extra defender when the personality strength gate is stricter.
- The static list `AICampaign.defensiveoperations` (private, see line 13505 body) is owned by the **private nested class** `AICampaign.DefensiveOperation` at line 10055, which exposes `AddUnit(Regiment, Vector3)`, `RemoveUnit(Regiment)`, `UnitPartOf(Regiment)`, and `CheckIfDefensiveStanceExistsForUnit(Regiment)`. All access is via `AccessTools.Inner(typeof(AICampaign), "DefensiveOperation")` plus reflection on the enclosing static field.
- The per-faction lists relevant to defense live on `aifaction[i]`: `unitsindefensiveoperations`, `unitsinoffensiveoperations`, `groupstodefendcapital`, `unitsconstructingsupplydepots`, `ownunits`, `enemyunits`, `ownfleets`, `enemyfleets`, `ownports`. The static `defensiveoperations` list is **not** on `aifaction`.
- `AICampaign.GetClosestDefensiveOperation(...)` at line 17810 exposes active defensive operations for UI/diary use.

## Goal

Make AI factions defend coastal assets and respond to naval landings without turning the whole campaign into a static cordon.

The AI should:

- keep proportional guard forces near important exposed coastal assets before a landing;
- detect active or imminent naval landing threats;
- assemble a challenging land response to landed enemies;
- persist pressure until the landing threat is actually resolved;
- avoid pulling major armies halfway across the map unless losing the threatened asset would be decisive and no local answer exists.

The CSA must be able to protect key ports, coastal approaches, Richmond/Norfolk-style corridors, and forts without stripping field armies by default. The Union must protect Washington-adjacent/coastal supply bases and occupied ports without over-garrisoning safe rear harbors.

## Non-Goals

- No fleet interception rewrite in this slice.
- No custom invasion-creation logic.
- No fort, depot, railroad, or telegraph construction steering.
- No broad Prefix that skips all vanilla defensive operations.
- No permanent full-coast garrison network.
- No custom movement engine unless telemetry proves vanilla defensive operations cannot move the selected response.

## New Strategic Type

### DefenseIntentLedger

A pure ledger computed during daily strategic review after campaign map, front sectors, army areas, formation directives, fiscal intent, and construction intent are available.

The whole strategic review cadence is daily for this slice. Coastal threats can flip from forming to landed in under a game-week, and a multi-day wait loses ports. All operational ledgers (campaign map, front sectors, army areas, formation directives, fiscal intent, construction intent) move from weekly to daily so the defense ledger reads same-tick context instead of last-known weekly snapshots. Implementation must keep daily strategic review inside the existing `StrategicCoordinator` frame budget — each ledger should signature-skip when nothing material has changed and only do real work on signature change or event-trigger dirty marks. The high-speed AI throttle (#21) cooldown still applies; if a daily strategic pass exceeds the slow-frame threshold the next day's pass should be allowed to run a partial subset rather than blocking the frame.

It writes no vanilla state directly. Harmony patches and runtime helpers read it. It is recomputed daily and is not persisted to the save sidecar; any cooldown / threat-tracking state required for non-oscillation lives on `StrategicCoordinator` next to existing per-alliance ledger state, not in the ledger itself.

Inputs:

- alliance id;
- date, `EraStage`, `Policy.CurrentChapter`, active CIC plan, faction doctrine, and active `GrandStrategyProfile`;
- `CampaignMapLedger` towns and assets;
- per-faction vanilla lists from `AICampaign.aifaction[i]`: `ownunits`, `enemyunits`, `ownfleets`, `enemyfleets`, `unitsindefensiveoperations`, `unitsinoffensiveoperations`, `groupstodefendcapital`, `unitsconstructingsupplydepots`, `ownports`;
- the static `AICampaign.defensiveoperations` list (read-only, via reflection on the private nested `DefensiveOperation` type) for already-committed defensive responses;
- vanilla sea-invasion state via the static `AICampaign.seainvasionforce` list — for each entry, read `invasionforce`, `invasionforcediv`, `assignedescort`, `seainvasionspot`, `sourceport`, and `currentstate` (1=forming, 2=at-sea; landed-phase derived separately);
- vanilla `RaidForce` state via `RaidForce.IsRaidUnit(unit)` and the per-raid `currentstate` ladder for any raiding column near a coastal asset;
- `SeaInvasion.GetUnitStrengthInRange(allianceid, returnbalance)` and `GetNumberOfEnemyObjectives(allianceid)` only when `GameVars.debug_seainvasionsactive` is true and the spot has been initialized; otherwise compute the local enemy cluster ourselves from `aifaction[i].enemyunits` filtered by distance to the spot/asset;
- front side from `bunits.frontline2.GetSideOnPosition`;
- formation snapshots: strength, morale, readiness, supplies, ammo, player-command gate, directive, active plan commitment, current theater, distance to threatened asset;
- current construction/fiscal pressure only as a modifier, not as a blocker for emergency defense.

Outputs:

- `DefensePosture` per threat or guard zone;
- threatened asset/site identity and position;
- threat strength, desired response strength, response radius tier, and escalation reason;
- ranked response candidates and selected response package;
- suppressed candidates with reason;
- compact telemetry signature for no-spam logs.

## Defense Postures

### CoastalGuard

No active landing exists, but a valuable exposed coastal asset should have proportional local cover.

Allowed force source:

- local idle or low-commitment formations meeting the **standard** eligibility filter (mirrors `DefensiveOpsPatch.EligibleForCapitalDefense`: morale > `aiminimummoraleformovement`, `ReadinessStep >= 1`, not retreating, not in battle, not constructing depot/fort, not in `seainvasionforce`/`raidforce`, not player-controlled, not taking a town);
- same-theater defensive formations under the standard filter;
- under the **relaxed** filter (allowed only for `CoastalGuard` and only when no standard-filter candidate is in radius): exhausted or offensive-unsuitable units that can still defend — `morale > aiminimummoraleformovement * 0.6`, `ReadinessStep >= 0` (recovering allowed). Vanilla's downstream `IsUnitAvailableForDefensiveOperation` will reject relaxed-filter candidates, so guard assignments using relaxed candidates **must** route through the custom defensive movement order surface (see Patch Surface §Slice 2), not through `CheckForDefensiveOperations`.

Forbidden:

- cross-map pulls;
- player command;
- committed main armies;
- stripping critical fronts.

### InvasionWatch

An enemy `SeaInvasionForce` exists or enemy naval/invasion pressure is approaching a landing zone, but no confirmed landing has happened.

Allowed behavior (concrete actions only — no vanilla-state writes outside this list):

- mark the threatened asset as `priority=high` in our own ledger so guard scoring favors it on the next daily tick;
- if a same-theater reserve candidate is already within the response radius and is not committed to a higher-value plan, hold it in place via the custom defensive movement order (cancel any active `MoveUnitTo` away from the asset);
- if a candidate is just outside the response radius but inside the local/theater radius, issue a single `MoveUnitTo` toward the asset's defensive anchor (anchor = nearest fort, then nearest harbor, then town center);
- compute and store the would-be active-landing response package, but do not commit any unit beyond the hold/single-move described above;
- never escalate across theaters during `InvasionWatch`.

### ActiveInvasion

Enemy troops are landed, near a `SeaInvasion` objective, near a coastal town/fort/port, or the invasion force has reached/started objective movement.

Allowed behavior:

- assemble enough nearby/same-theater forces to challenge the landing;
- use `DefenseForceSizer` to prefer sufficient smaller formations over oversized armies;
- persist assignment while the threat remains.

### ContainAndCounterattack

Enemy has captured or is actively threatening a high-value port, fort, capital-adjacent coast, or major coastal town.

Allowed behavior:

- escalate response ratio;
- use adjacent-theater reserves if local forces cannot clear the minimum response ratio;
- allow army-level response only for decisive assets or major enemy landings.

### Recovered

Threat is gone or no longer dangerous.

Behavior:

- release extra response units gradually across daily ticks rather than dropping the whole package on the first clear day;
- keep a cooldown of several days (default 3-5 game-days, tunable) before returning to normal posture so the AI does not oscillate if the enemy remains nearby. Cooldown counters live on `StrategicCoordinator`, keyed by stable threat signature, and decrement once per daily strategic review.

## What Counts As Defended Terrain

Primary assets:

- sea harbors and river harbors from `CampaignMapLedger`;
- forts from `CampaignMapLedger`;
- active-map towns tagged as capital, major city, economic, workforce, or border;
- vanilla `SeaInvasion.ObjectivesInRange`;
- town/asset clusters near capitals and ports.

Secondary assets:

- nearby depots, telegraphs, markets, rail hubs, and construction sites when they support an existing primary asset;
- supply corridors only when tied to an active coastal threat.

Secondary assets cannot create a cross-theater response by themselves.

### Asset Strategic Role Classification

Current `CampaignMapLedger` only carries `CampaignTownRole` flags (`Capital | MajorCity | Workforce | Economic | Border`) and `CampaignMapAssetKind` (`Fort | SeaHarbor | RiverHarbor`). That is not enough to honor the CSA/Union doctrine asymmetry below — Wilmington vs Galveston and Hampton Roads vs Beaufort look identical to the current ledger.

This slice extends asset metadata with an explicit `AssetStrategicRole` (additive flags), populated by **two layered mechanisms** and read by `Pre-Invasion Guard Doctrine`:

1. **`GrandStrategyProfile`-derived weights.** The active CSA / Union grand-strategy profile already encodes faction priorities (blockade running, river control, coastal interdiction, deep operations). Compute a per-asset weight from the profile's strategy tags and the asset's geometry (state, theater, distance to alliance capital, distance to enemy frontline). This is the default mechanism and works for any active-map asset without hand-coded data.
2. **Hand-coded catalog overrides** (mirrors `ObjectiveCatalog`). For named anchors that the doctrine specifically calls out — Wilmington, Charleston, Mobile, Galveston, Sabine Pass, Norfolk, Hampton Roads, Beaufort, NOLA, Vicksburg, Memphis, St. Louis, Baton Rouge, Cairo, Annapolis — store explicit `AssetStrategicRole` flags so doctrine matches author intent even if the heuristic diverges. Catalog hits override profile-derived defaults.

`AssetStrategicRole` flags include at minimum: `BlockadeRunnerPort`, `UnionForwardBase`, `RiverControlHub`, `CapitalApproach`, `KeyFort`, `SupplyEscapeOnlyPort`, `RearSafePort`. Flags are additive; an asset can carry several. v1 is allowed to ship with an incomplete catalog as long as the profile-derived fallback is implemented and the doctrine never crashes on missing tags. If the catalog is empty and the profile fallback returns no useful weight, the asset is treated as a generic coastal town/harbor (no doctrine bonus) and the ledger logs `[DefenseIntent:asset] missing-role asset=<name>` once per asset.

## Threat Detection

The ledger detects threats from four sources. River-harbor threats (Vicksburg, Memphis, NOLA, Baton Rouge) do **not** flow through `SeaInvasionForce` — they arrive via vanilla land-front advance and the `RiverHarbor` asset class — so they only show up in sources 2-4 below.

1. **Vanilla `SeaInvasionForce` state** (sea invasions only).
   - `currentstate == 1` → posture `InvasionWatch` (forming at `sourceport`, target `seainvasionspot` already chosen).
   - `currentstate == 2` → posture `InvasionWatch` while at sea; transitions to `ActiveInvasion` once the landed-phase signal below trips.
   - **Landed-phase signal** (no `currentstate >= 3` exists in vanilla): treat as landed when any of (a) `invasionforce` is on land terrain near `seainvasionspot` (within `GamePrefs.seainvasionspotsunitrange`), (b) `seainvasionspot.GetNumberOfEnemyObjectives(ourAlliance) > 0` and the invasion's `currentobjective` is non-null, or (c) `invasionforce.regimentpaths > 0` while close to a coastal town/fort/port. Once a `seainvasionforce[i]` entry is removed by vanilla (`UpdateSeaInvasionForces` `RemoveAt` calls at lines 9495/9500/9505 or the explicit retreat/no-objective removal at 9523), recovery starts.

2. **Vanilla `RaidForce` state** (raids that may land or come ashore).
   - any `RaidForce` whose `raidgroup` is within asset radius of a coastal/river asset surfaces as posture `ActiveInvasion` with threat scale `Raid`;
   - `RaidForce.currentstate >= 5` indicates the raid is winding down — start the Recovered cooldown.

3. **Enemy unit proximity** (catch-all, including river landings and rear-area cavalry).
   - enemy land units near coastal/river asset, sea-invasion objective, or port/fort, computed from `aifaction[i].enemyunits` filtered by distance — do not depend on `SeaInvasion.unitsinrange` because of the rotation lag and the `debug_seainvasionsactive` gate;
   - enemy units on friendly side of `bunits.frontline2.GetSideOnPosition` within asset radius of a coastal objective;
   - when the public `SeaInvasion.GetUnitStrengthInRange(allianceid, returnbalance)` is available and recent, use it as a corroborating signal, never as the sole signal.

4. **Asset ownership / frontline change**.
   - coastal/river asset owner flips against us;
   - enemy controls an objective in a coastal guard zone;
   - key port/fort `Condition` drops below the doctrine threshold or ownership changes.

Threats are keyed by stable threat identity. The signature recipe (used both for log dedup and for cooldown tracking) is:

- for `SeaInvasionForce` entries: `("sif", invasionforce.GetInstanceID(), seainvasionspot != null ? seainvasionspot.name : "<no-spot>", sourceport != null ? sourceport.name : "<no-port>")`;
- for `RaidForce` entries: `("raid", raidgroup.GetInstanceID(), nearestAsset.Name)`;
- for proximity/ownership threats with no vanilla force entry: `("asset", asset.Kind, asset.Name, enemyClusterSignature)` where `enemyClusterSignature` is the sorted concatenation of `Regiment.GetInstanceID()` for the top-N enemies in radius (N tunable, default 5) so transient enemy churn does not mint a new threat every daily tick.

When vanilla collapses a `seainvasionforce[i]` entry, the ledger keeps the threat signature alive for the Recovered cooldown so persistence requirements are honored even after the vanilla force object is gone.

## Pre-Invasion Guard Doctrine

Coastal guard is a budgeted posture, not a cordon.

Guard scoring (each input is a normalized `[0,1]` term combined additively, then weighted by faction doctrine):

- asset value: capital-adjacent, `CampaignMapAsset.Capacity`/`Level`/`Condition` for harbors/forts, `CampaignMapTown.CitySize`/`RepresentingPopulation`/role flags for towns, plus the `AssetStrategicRole` flags from §"Asset Strategic Role Classification";
- exposure: near hostile frontline (distance to `bunits.frontline2` boundary), near enemy sea access (closest enemy fleet or port owned by a hostile alliance), previously raided/contested (battle-history hits within asset radius), low friendly strength nearby (sum of `groupstrengthactive * groupmorale` for `aifaction[i].ownunits` in radius);
- doctrine: faction and era priorities derived from the active `GrandStrategyProfile`;
- opportunity: a standard-filter candidate is already in radius and not committed to a higher-value plan.

CSA guard priority — drives a strategy-tag bonus when the asset's `AssetStrategicRole` matches:

- `CapitalApproach` (Richmond/Norfolk corridor);
- `BlockadeRunnerPort` and `KeyFort` tied to the active blockade-running profile;
- `SupplyEscapeOnlyPort` (last-port-out condition for the local theater);
- exposed secondary ports only if a standard-filter local candidate is available.

Union guard priority — drives a strategy-tag bonus when the asset's `AssetStrategicRole` matches:

- `CapitalApproach` (Washington/Annapolis);
- `UnionForwardBase` (port supporting an active offensive — e.g., Hampton Roads in '62, Hilton Head, Pensacola);
- `RiverControlHub` (Cairo, Memphis-when-held, NOLA-when-held);
- `RearSafePort` only when cheap local coverage exists.

Guard caps:

- one guard package per high-value coastal cluster (asset cluster keyed by within-radius proximity) unless an active threat escalates;
- guard package usually division/detachment scale (target effective strength = `aiminimumstrengthformovement * 1.5` to `* 3.0`, scaled by asset value);
- no cross-map movement (enforced by §Locality And Escalation Rule);
- faction-wide cap: `CoastalGuard` posture must not commit more than `GuardBudgetFraction` (default 0.10, tunable per faction/era via `GrandStrategyProfile`) of the alliance's available field strength while no active threat exists. Commitments above that cap are downgraded to "watch only" with no force assigned.

## Detected Landing Response Doctrine

When an active landing threat exists, response sizing is based on enemy strength and asset value.

Threat scale:

- `Raid`: small force near low/medium-value coastal objective.
- `Landing`: meaningful force near port, fort, or town.
- `MajorLanding`: large force or multiple enemy formations near a high-value asset.
- `DecisiveLanding`: capital-adjacent, major port, key fort, or only viable local supply/escape port.

Desired response:

- `Raid`: local containment force; do not abandon critical operations.
- `Landing`: response package that clears minimum strength ratio using local/same-theater forces.
- `MajorLanding`: allow adjacent-theater reserves.
- `DecisiveLanding`: allow army-level or cross-theater escalation if no local package can challenge it.

Persistence:

- response remains active across daily ticks until enemy is routed, retreating, removed, re-embarked, no longer near objectives, or the objective is secure for the Recovered cooldown period;
- if the enemy splits, keep pressure on the highest-value coastal objective rather than chasing every detachment;
- do not release immediately after one favorable daily tick — require the Recovered cooldown to elapse first.

## Locality And Escalation Rule

This rule is load-bearing.

Default response radius is local/theater-bound.

Candidate tiers (distance is `Tools.GetXZDistance(unit, threatPosition)` in vanilla map units; defaults below are anchored to existing `GamePrefs` and `Theater` boundaries so the same scale used by vanilla `CheckForDefensiveOperations` is preserved):

1. **Local** — distance ≤ `GamePrefs.maxdistancedefensiveoperations` (the same radius vanilla uses for defensive ops, currently ~the same scale as `commanderrange * 1.5`). Always considered first.
2. **Same-theater** — `Theater` membership equal to the threat's theater (per `TheaterClassifier.FromPosition`/`FromStateName`), regardless of raw distance, AND not flagged `Hold`/`Critical` by the `FrontSectorLedger` for the source sector.
3. **Adjacent-theater** — `Theater` adjacency by the existing theater-graph the registry encodes (East ↔ West via the Appalachian gap, West ↔ TransMiss across the Mississippi, Coast adjacent to the matching land theater). Used only when the cumulative tier-1+tier-2 effective strength is below the desired ratio (see §Force Package Scoring multi-unit aggregator).
4. **Cross-map** — anything else; allowed only under emergency escalation per the all-true checklist below.

Cross-map pulls are allowed only when all are true:

- threatened asset carries `AssetStrategicRole.CapitalApproach`, `BlockadeRunnerPort` (CSA), `UnionForwardBase` (Union), `KeyFort`, or `SupplyEscapeOnlyPort`;
- aggregated tier-1 + tier-2 + tier-3 effective strength is below `desiredStrength * 0.75` (the package-scoring threshold below);
- threat posture is `ActiveInvasion` or `ContainAndCounterattack`, never `CoastalGuard` or `InvasionWatch`;
- candidate is not player-controlled (`DLC_WL.IsMovedByPlayer(unit) == false` and the alliance is not the player's CIC);
- candidate's source `FrontSectorLedger` posture is not `Hold`/`Critical`, and the candidate is not in `aifaction[i].unitsinoffensiveoperations` for an active CIC plan whose objective ranks above the threatened asset (compared via `ObjectiveScoring`);
- selected package is the smallest package that clears `desiredStrength` per the §Force Package Scoring aggregator.

For `CoastalGuard`, cross-map pulls are forbidden — enforced by the candidate-filter Prefix surface below, not just by ledger output.

For minor raids (`Raid` threat scale), cross-map pulls are forbidden.

### Enforcement surfaces (load-bearing)

The ledger is read-only by definition; enforcement of "no cross-map pulls" requires actively shaping vanilla decisions. Use these surfaces in order:

1. **Targeted Prefix on `AICampaign.CheckForDefensiveOperations(int)`** that, only when the ledger has marked specific candidates as forbidden for an active threat in this faction, removes those `Regiment` references from the local snapshot of `aifaction[i].ownunits` that vanilla iterates. The Prefix MUST restore the original list in a Postfix (try/finally) so other vanilla code paths keep seeing the full list. This is targeted candidate filtering, not whole-method skip — it is permitted under the spec's Non-Goals.
2. **Postfix re-issue on `AICampaign.CheckForDefensiveOperations(int)`**: after vanilla returns, walk `aifaction[i].unitsindefensiveoperations` for newly-added units; if any new addition is a forbidden cross-map pull, immediately call `MoveUnitTo` to send the unit back to its prior `theaterposition` and remove it from `unitsindefensiveoperations` plus the static `defensiveoperations` entry (via the private nested `RemoveUnit`). This is the safety net for Prefix gaps.
3. **Custom defensive movement order** owned by the new patch for active landing response (and for relaxed-filter `CoastalGuard` candidates that vanilla's downstream gates reject). This adds units directly to `aifaction[i].unitsindefensiveoperations` and issues `MoveUnitTo` toward the asset's defensive anchor without touching `defensiveoperations` membership.

Every cross-theater or cross-map escalation must log once per signature:

```text
[DefenseIntent] escalation=cross-theater reason=local-insufficient threat=... candidate=...
```

Every Postfix re-issue (forbidden pull undone) must log once per `(threatId, candidateId)` pair:

```text
[DefenseIntent] reverted=cross-map threat=... candidate=... reason=guard-posture
```

## Force Package Scoring

Use and extend `DefenseForceSizer`.

Candidate positive factors:

- enough effective strength to help clear the desired ratio;
- high readiness;
- good morale;
- acceptable provisions/ammunition;
- same theater or local radius;
- already in defensive/recover/guard posture;
- not useful for immediate offensive pressure.

Candidate penalties:

- player command;
- active main plan commitment;
- critical-front hold posture;
- low readiness/supply/ammo;
- gross overmatch for small threats;
- long distance;
- cross-theater or cross-map movement;
- current fort/depot construction support;
- sea-invasion force/escort reference;
- active retreat/in-battle/routed state.

### Multi-unit package aggregator

`DefenseForceSizer.ScoreCandidate` is single-candidate. Package selection is a separate step that operates on the scored candidate set:

1. Compute per-candidate effective strength: `effective = groupstrengthactive * clamp(groupmorale, 0.25, 1.25) * readinessMultiplier(ReadinessStep)` (mirrors the existing `DefenseForceSizer.ReadinessMultiplier` curve).
2. Sort candidates by `DefenseForceSizer.ScoreCandidate` ascending (lower is better — vanilla convention preserved).
3. Greedy add candidates in score order, accumulating `cumulativeEffective`, until either:
   - `cumulativeEffective >= desiredStrength * 1.0` and the next candidate would push `cumulativeEffective >= desiredStrength * 1.25` (overshoot guard) → stop;
   - `cumulativeEffective >= desiredStrength * 0.85` and the next candidate is from a strictly worse tier (e.g., the next add would be tier-3 when current package is all tier-1/2) → stop and accept the slight understrength.
4. If the final package satisfies `cumulativeEffective >= desiredStrength * 0.75`, mark it `adequate=true`. Otherwise, mark `adequate=false`, log `[DefenseIntent] understrength=true threat=... package=...`, and return the best-effort package anyway (containment force).
5. Emit the unselected-but-eligible candidates as `suppressed candidates` in the ledger output, each with the reason that prevented selection (`overmatch`, `worse-tier`, `cap-reached`, `forbidden-cross-map`, `relaxed-filter-only-no-custom-order-needed`).

The 0.75 / 1.0 / 1.25 thresholds are tunable via `DefenseTuningProfile` config block; defaults above are starting points for smoke-testing.

Package rule:

- pick the smallest adequate package per the aggregator above, not the strongest possible package;
- prefer several local adequate formations over one remote oversized army;
- if no adequate package exists, pick the best-effort local containment force per step 4 and log `understrength=true`.

## Patch Surface

### Slice 1: Observer + Ledger

Implement pure ledger and runtime extraction first. The ledger runs daily, computes posture / threat / package / suppressed candidates, but writes nothing to vanilla state.

Telemetry:

```text
[DefenseIntent] alliance=1 posture=ActiveInvasion threat=sif:#1234:Norfolk enemy=4200 desired=6500 local=7200 selected=1 reason=landed-port-threat sig=ActiveInvasion|sif#1234|6500b|1u|local
```

No movement or vanilla-state writes in this sub-slice. Slice 1 ships before any of the surfaces below land — observer telemetry must validate that detected postures and threat scales match author intent under several days of campaign smoke before steering is enabled.

### Slice 2: Guard/Response Steering

Three concrete patch surfaces, used in priority order. Each is gated by the ledger having an active threat or guard intent for the candidate's faction; none of them mutate the ledger.

1. **Targeted candidate-filter Prefix** on `AICampaign.CheckForDefensiveOperations(int)`. When the ledger marks specific candidates as forbidden for a current threat (cross-map pull on `CoastalGuard`, critical-front pull, etc.), the Prefix snapshots `aifaction[i].ownunits`, removes the forbidden references from the live list for the duration of vanilla's call, and a paired Postfix restores the snapshot. Implemented with try/finally; never leaves the list mutated if the patch throws.
2. **Postfix re-issue** on the same method (and on `AICampaign.AssignUnitToDefendCapital(int)` for symmetry with shipped #4). After vanilla returns, walk new additions to `aifaction[i].unitsindefensiveoperations`/`groupstodefendcapital`. Any addition the ledger marks forbidden (e.g., a Texas brigade vanilla committed to defend Norfolk despite our `CoastalGuard` ban) is reverted: `MoveUnitTo(unit, prior_theaterposition)`, remove from the per-faction list, and remove from the static `defensiveoperations` entry via `AccessTools.Inner(typeof(AICampaign), "DefensiveOperation").GetMethod("RemoveUnit").Invoke(...)`.
3. **Custom defensive movement order** owned by the new patch (sibling to existing #4 capital pattern). Used for:
   - active landing response that vanilla's rate-limit prevented from firing in time;
   - relaxed-filter `CoastalGuard` candidates that vanilla's downstream `IsUnitAvailableForDefensiveOperation` would reject;
   - `InvasionWatch` single-move pre-positioning per §Defense Postures.
   Adds the unit to `aifaction[i].unitsindefensiveoperations` (mirroring vanilla's commit pattern at line 13718), issues `MoveUnitTo` toward the asset's defensive anchor, and registers the assignment under the threat signature so the daily ledger can release it at Recovered + cooldown. Does **not** create a `DefensiveOperation` entry — the static `defensiveoperations` list is left to vanilla so vanilla's own end-of-defense bookkeeping (`CheckForEndOfDefensiveOperations` at line 13782) keeps working.

Avoid:

- whole-method Prefix skip on `CheckForDefensiveOperations`;
- custom movement for all defenders (custom orders are only for the cases enumerated above);
- mutating strategic ledger state from the patches (ledger remains read-only to Harmony patches per the project-wide invariant).

### Coexistence with shipped #4 `DefensiveOpsPatch`

Both patches Postfix `AICampaign.AssignUnitToDefendCapital`. To prevent double-assignment and overlapping concerns:

- **Capital is owned exclusively by #4.** The new ledger does **not** add a defender to `aifaction[i].groupstodefendcapital`, does **not** issue custom orders against the capital town/state, and does not classify the capital town as a `CoastalGuard` asset. The capital's `AssetStrategicRole.CapitalApproach` flag still applies to *other* assets in the capital cluster (e.g., Norfolk under Richmond), which the new ledger may steer.
- **Non-capital coastal/river assets are owned exclusively by the new ledger.** #4 never steers anything outside `groupstodefendcapital`.
- Patch ordering: when both are present on `AssignUnitToDefendCapital` (#4 + new-ledger Postfix re-issue surface), the new-ledger Postfix runs **after** #4 (declared via `[HarmonyPriority(Priority.Low)]` or explicit `[HarmonyAfter("dev.kyle.whiskey-realism.defensiveops")]`) so #4's add-extra-defender decision is visible before the new ledger considers reverting forbidden cross-map pulls.

### Slice 3: Guard Budget Tuning

Tune guard budget from runtime logs:

- too many guard packages means field armies are weakened;
- too few guard packages means the ledger is just reactive;
- use telemetry to adjust `GuardBudgetFraction` and per-faction/era caps before adding more patch power.

## Logging

Logging must be non-spammy. Daily strategic review will run this code seven times more often than the previous weekly cadence, so signature-on-change logging is mandatory, not optional.

Log when (each gated by signature change vs the prior daily tick):

- threat posture changes;
- selected response package changes;
- escalation tier changes;
- response is understrength;
- threat recovers/releases;
- a patch cannot steer because vanilla gates reject every candidate.

The compact telemetry signature includes at minimum: `posture | threatId | desiredStrength bucket | selected unit count | escalation tier`. Log lines must be suppressed unless the signature has changed since the last logged line for that threat (or `Verbose Logging` is enabled).

Do not log every daily tick.

Verbose mode may add candidate-score details and per-tick traces, but default logs must stay compact even at high campaign speed.

## Acceptance Criteria

- `CoastalGuard` creates bounded, local guard intent for high-value exposed ports/forts/coastal towns.
- Guard posture never pulls a formation halfway across the map (enforced by the candidate-filter Prefix and the Postfix re-issue surface, not just by ledger output).
- Active landing response assembles a proportional local/theater package per the multi-unit aggregator.
- A 4,000-man landing does not pull a 20,000-man field army if a sufficient smaller local/same-theater package exists.
- A major landing near a decisive port, fort, or capital-adjacent coast can escalate beyond the local theater only when local forces are insufficient.
- Response persists across daily ticks until the landing is actually resolved (vanilla `seainvasionforce` entry collapsed AND no enemy land units in radius for the Recovered cooldown period).
- **Player command is never pulled.** Two cases: (a) when the player is CIC of an alliance, the entire defense ledger short-circuits for that alliance via `StrategicCoordinator.IsPlayerCICOf`; (b) when the player is a W&L subordinate of an AI CIC, only `DLC_WL.IsMovedByPlayer(unit) == true` units are protected, and the rest of the alliance's `ownunits` remain steerable.
- No broad vanilla defensive-operation shutdown — the candidate-filter Prefix only removes specific forbidden references; vanilla's own selection still runs over the remaining list.
- Capital defense is owned exclusively by shipped #4; the new ledger never writes to `groupstodefendcapital`.
- Logs are bounded and explain escalations and reverts.

## Tests

All pure tests use synthetic `DefenseIntentLedger` fixtures — no Unity, no game state. A fixture specifies: an alliance id, an `AssetStrategicRole`-tagged asset list, an enemy unit list (each with position, strength, morale), an own-unit list (each with position, strength, morale, readiness, theater, current commitment), an active `GrandStrategyProfile`, and an optional pre-existing threat-signature cooldown table. Each test asserts the ledger's `posture`, `selectedPackage`, `escalationTier`, and `suppressed candidates` against the fixture's expected output.

Pure tests:

- **coastal-guard-forbids-cross-map**: fixture has one `BlockadeRunnerPort` with no enemy in `SeaInvasionForce` and one fully-eligible cross-map division. Expect `posture=CoastalGuard`, `selectedPackage` empty (no local candidate), cross-map division in `suppressed` with reason `forbidden-cross-map`.
- **minor-raid-forbids-cross-map**: fixture has a `RaidForce` near a low-value coastal town and one cross-map army. Expect `posture=ActiveInvasion`, `threatScale=Raid`, cross-map army suppressed with reason `forbidden-cross-map`.
- **decisive-landing-allows-cross-theater**: fixture has a `SeaInvasionForce` (`currentstate=2`, landed-phase signal tripped) near `CapitalApproach`-tagged Norfolk, no local force, one same-theater understrength division, one adjacent-theater army. Expect `posture=ActiveInvasion`, `escalation=cross-theater`, adjacent-theater army selected.
- **same-theater-adequate-beats-remote-oversized**: fixture has a 4,000-man landing, two same-theater 3,000-man brigades, one cross-map 20,000-man army. Expect `selectedPackage` = the two brigades, cross-map army suppressed with reason `overmatch` AND `forbidden-cross-map`.
- **guard-budget-caps-multiple-low-value-ports**: fixture has 6 `RearSafePort` assets and one `GrandStrategyProfile` with `GuardBudgetFraction=0.10`. Expect at most `floor(0.10 * totalEffectiveStrength / minPackageStrength)` packages assigned; remainder flagged "watch only" with reason `cap-reached`.
- **active-invasion-persists-through-one-favorable-tick**: run the fixture twice with the same threat — second tick has the enemy momentarily 1 unit short. Expect the response to stay assigned, cooldown counter not started.
- **recovered-threat-releases-after-cooldown**: run the fixture across N+1 ticks where threat clears at tick 1; expect release at tick `1 + cooldownDays`, not at tick 1.
- **player-cic-short-circuits-alliance**: fixture marks alliance 0 as player-CIC. Expect `DefenseIntentLedger` for alliance 0 to be empty (`posture=NotEvaluated`) regardless of threats.
- **wl-subordinate-protects-only-marked-unit**: fixture marks alliance 1 as AI-CIC with one `DLC_WL.IsMovedByPlayer == true` unit. Expect only that unit suppressed with reason `player-controlled`; other alliance-1 units remain steerable.
- **critical-front-candidate-rejected-unless-decisive**: fixture has a critical-front-tagged division and a `Landing`-scale threat. Expect division suppressed. Re-run with `DecisiveLanding`-scale threat and no other adequate package. Expect division selected with reason `decisive-no-alternative`.
- **river-harbor-detects-without-sif**: fixture has Vicksburg `RiverHarbor` with enemy units in radius and no `SeaInvasionForce`. Expect `posture=ActiveInvasion`, threat keyed via the `("asset", ...)` signature recipe.
- **raidforce-coverage**: fixture has a `RaidForce` (no `SeaInvasionForce`) near Hampton. Expect `threatScale=Raid` posture without depending on `seainvasionforce`.
- **debug-seainvasionsactive-off**: fixture has `seainvasionforce` populated but the global gate disabled. Expect ledger to fall back to `aifaction[i].enemyunits`-based proximity detection without throwing.

Runtime smoke:

- start a campaign and confirm `[DefenseIntent]` summary appears only on signature change or verbose heartbeat, even though the ledger now runs daily;
- if a sea invasion appears, confirm `InvasionWatch` or `ActiveInvasion` posture within one to two game-days, not the previous weekly worst case;
- confirm selected candidate count and desired strength look proportional via the multi-unit aggregator;
- confirm no repeated warnings or per-day spam at 1x, 5x, 20x, and 50x campaign speed (daily cadence interacts with #21 fast-forward catch-up — the strategic pass must signature-skip cleanly under the high-speed throttle);
- confirm Recovered cooldown holds the response in place across at least three favorable daily ticks before release;
- confirm the candidate-filter Prefix and Postfix re-issue surfaces never leave `aifaction[i].ownunits` mutated after a vanilla call (assert via reflection at end of vanilla pass under verbose mode);
- confirm capital-defense add-on remains owned by shipped #4 — search log for any `[DefenseIntent]` line referencing `groupstodefendcapital` and expect zero hits.

## Open Implementation Questions

- Whether `AICampaign.seainvasionforce` is safely accessible by reflection at all times. Vanilla itself null-checks the field (`if (seainvasionforce == null) return null;` at line 9298), so a tolerant read with null-skip is correct; the open question is whether the field can be non-null before `AICampaign.aifaction` initializes (likely no — `CheckForSeaInvasion` is the only writer and reads `aifaction[_aifaction]`). Implementation plan should confirm by reading the field unconditionally during the deferred-startup window and logging `OnceLog.Info` on first non-null observation.
- The previous "rotation lag on `SeaInvasion.unitsinrange`" question is resolved by §Threat Detection — runtime always computes its own enemy cluster from `aifaction[i].enemyunits` and treats `GetUnitStrengthInRange` as a corroborating signal only.
- Whether the candidate-filter Prefix on `CheckForDefensiveOperations` can safely mutate the live `aifaction[i].ownunits` for the duration of vanilla's call without breaking concurrent vanilla code paths that may iterate the same list. Slice 1 must verify by observation: log every `aifaction[i].ownunits.Count` immediately before and after a synthetic Prefix snapshot/restore round-trip under several days of smoke.
- Whether vanilla's `lastdefensiveoperationcheck` rate-limit makes the Prefix surface effectively unavailable for time-sensitive landing response (because vanilla bails before our Prefix-shaped list is iterated). If so, the custom defensive movement order surface becomes primary for `ActiveInvasion`/`ContainAndCounterattack`, and the Prefix is reserved for `CoastalGuard` enforcement.
- The exact `cooldownDays` default and `GuardBudgetFraction` default should be tuned from runtime telemetry in Slice 3, not pinned in the spec. Spec defaults (3-5 days, 0.10 fraction) are starting points only.
- Whether `Theater` adjacency for §Locality candidate tier 3 should be derived from existing `TheaterClassifier` neighborhood data or hand-coded as a static map. Hand-coded is simpler for v1 (East ↔ West, West ↔ TransMiss, Coast ↔ matching land theater) and avoids regressing existing theater-classification behavior.

These questions should be answered in the implementation plan before code changes.
