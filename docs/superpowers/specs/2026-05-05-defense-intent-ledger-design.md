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

- `AICampaign.SeaInvasionForce` at line 9188 tracks `invasionforce`, `invasionforcediv`, `assignedescort`, `seainvasionspot`, `sourceport`, and `currentstate`.
- `SeaInvasionForce.GetSuitableLocation(...)` at line 9411 chooses landing spots with enemy objectives and insufficient friendly strength in range.
- `SeaInvasionForce.UpdateSeaInvasionForces()` at line 9466 moves invasion forces by sea, then orders them toward nearby enemy objectives after landing.
- `AICampaign.CheckForSeaInvasion(int)` at line 16407 creates invasion forces by chapter/faction probability.
- `SeaInvasion` at line 140972 exposes `ObjectivesInRange`, local `unitsinrange`, `GetUnitStrengthInRange(...)`, `GetNumberOfEnemyObjectives(...)`, and closest enemy objective/sea-fort helpers.
- `AICampaign.CheckForDefensiveOperations(int)` at line 13505 is the vanilla land-defense response surface. It sorts enemy units and nearby friendly candidates, then starts defensive operations.
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

A pure ledger computed during weekly strategic review after campaign map, front sectors, army areas, formation directives, fiscal intent, and construction intent are available.

It writes no vanilla state directly. Harmony patches and runtime helpers read it. It is recomputed weekly and is not persisted to the save sidecar.

Inputs:

- alliance id;
- date, `EraStage`, `Policy.CurrentChapter`, active CIC plan, and faction doctrine;
- `CampaignMapLedger` towns and assets;
- vanilla `AICampaign.aifaction` lists: own units, enemy units, own fleets, enemy fleets, defensive-operation lists, capital defenders, supply-construction lists;
- vanilla sea-invasion state: `AICampaign.seainvasionforce`, `SeaInvasionForce.currentstate`, source port, target spot, invasion force, escort, and objectives in range;
- `SeaInvasion` local unit-strength balance and enemy-objective count;
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

- local idle or low-commitment formations;
- same-theater defensive formations;
- exhausted or offensive-unsuitable units that can still defend.

Forbidden:

- cross-map pulls;
- player command;
- committed main armies;
- stripping critical fronts.

### InvasionWatch

An enemy `SeaInvasionForce` exists or enemy naval/invasion pressure is approaching a landing zone, but no confirmed landing has happened.

Allowed behavior:

- raise local priority;
- move or hold nearby reserve candidates closer only if they remain inside the local/theater radius;
- prepare a response package but do not escalate across theaters.

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

- release extra response units gradually;
- keep a short cooldown before returning to normal posture so the AI does not oscillate if the enemy remains nearby.

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

## Threat Detection

The ledger should detect threats from three sources:

1. **Vanilla sea-invasion state**
   - `SeaInvasionForce.currentstate == 1`: forming.
   - `currentstate == 2`: moving by sea.
   - `currentstate >= 3`: landed/objective phase.

2. **Enemy unit proximity**
   - enemy land units near coastal asset, sea-invasion objective, or port/fort;
   - enemy units on friendly side of frontline near a coastal objective;
   - enemy strength in `SeaInvasion.GetUnitStrengthInRange(...)`.

3. **Asset ownership/frontline change**
   - coastal asset owner/frontline side flips;
   - enemy controls an objective in a coastal guard zone;
   - key port/fort condition/ownership changes.

Threats should be keyed by stable threat identity:

- sea-invasion force id/signature when available;
- otherwise asset id/name plus position and enemy cluster signature.

## Pre-Invasion Guard Doctrine

Coastal guard is a budgeted posture, not a cordon.

Guard scoring:

- asset value: capital-adjacent, port capacity, fort level/condition, town value, workforce/economic role;
- exposure: near hostile frontline, near enemy sea access, previously raided/contested, low friendly strength nearby;
- doctrine: faction and era priorities;
- opportunity: existing local low-commitment unit can cover without harming active plans.

CSA guard priority:

- capital-adjacent coast and Richmond/Norfolk-style approaches;
- key ports and forts tied to blockade-running/import/supply strategy;
- coastal towns that anchor supply or defensive corridors;
- exposed secondary ports only if local forces are available.

Union guard priority:

- Washington-adjacent coast and key supply bases;
- occupied ports supporting invasion logistics;
- coastal/river hubs that support blockade, river control, or deep operations;
- safe rear ports only when cheap local coverage exists.

Guard caps:

- one guard package per high-value coastal cluster unless an active threat escalates;
- guard package usually division/detachment scale;
- no cross-map movement;
- no more than a small faction-wide share of available field strength committed to `CoastalGuard` unless active threats exist.

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

- response remains active until enemy is routed, retreating, removed, re-embarked, no longer near objectives, or the objective is secure for a cooldown period;
- if the enemy splits, keep pressure on the highest-value coastal objective rather than chasing every detachment;
- do not release immediately after one favorable frame.

## Locality And Escalation Rule

This rule is load-bearing.

Default response radius is local/theater-bound.

Candidate tiers:

1. formations already near the threatened port, fort, town, or landing spot;
2. same-theater formations not committed to a higher-value front;
3. adjacent-theater reserves only if local forces cannot clear minimum response ratio;
4. cross-map formations only under emergency escalation.

Cross-map pulls are allowed only when all are true:

- threatened asset is high-value: capital-adjacent, major port, key fort, or only viable local supply/escape port;
- local and same-theater forces are insufficient;
- threat is active or imminent, not merely a guard posture;
- candidate is not player-controlled;
- candidate is not holding a critical front or executing a higher-value plan;
- selected package is the smallest package that clears the need.

For `CoastalGuard`, cross-map pulls are forbidden.

For minor raids, cross-map pulls are forbidden.

Every cross-theater or cross-map escalation must log once per signature:

```text
[DefenseIntent] escalation=cross-theater reason=local-insufficient threat=... candidate=...
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

Package rule:

- pick the smallest adequate package, not the strongest possible package;
- prefer several local adequate formations over one remote oversized army;
- if no adequate package exists, pick the least-bad local containment force and log `understrength=true`.

## Patch Surface

### Slice 1: Observer + Ledger

Implement pure ledger and runtime extraction first.

Telemetry:

```text
[DefenseIntent] alliance=1 posture=ActiveInvasion threat=Norfolk enemy=4200 desired=6500 local=7200 selected=1 reason=landed-port-threat
```

No movement or vanilla-state writes in this sub-slice.

### Slice 2: Guard/Response Steering

Preferred steering surface:

- a bounded patch around `AICampaign.CheckForDefensiveOperations(int)` that affects target/candidate preference only when the ledger has an active coastal threat or guard intent.

Avoid:

- whole-method Prefix skip;
- custom movement for all defenders;
- mutating strategic ledger state from the patch.

If vanilla refuses to act after ledger-targeted candidate steering, add a smaller explicit order surface only for active landing response, not for guard posture.

### Slice 3: Guard Budget Tuning

Tune guard budget from runtime logs:

- too many guard packages means field armies are weakened;
- too few guard packages means the ledger is just reactive;
- use telemetry to adjust faction/era caps before adding more patch power.

## Logging

Logging must be non-spammy.

Log when:

- threat posture changes;
- selected response package changes;
- escalation tier changes;
- response is understrength;
- threat recovers/releases;
- a patch cannot steer because vanilla gates reject every candidate.

Do not log every frame.

Verbose mode may add candidate-score details, but default logs must stay compact.

## Acceptance Criteria

- `CoastalGuard` creates bounded, local guard intent for high-value exposed ports/forts/coastal towns.
- Guard posture never pulls a formation halfway across the map.
- Active landing response assembles a proportional local/theater package.
- A 4,000-man landing does not pull a 20,000-man field army if a sufficient smaller local/same-theater force exists.
- A major landing near a decisive port, fort, or capital-adjacent coast can escalate beyond the local theater only when local forces are insufficient.
- Response persists until the landing is actually resolved.
- Player command is never pulled.
- No broad vanilla defensive-operation shutdown.
- Logs are bounded and explain escalations.

## Tests

Pure tests:

- coastal guard forbids cross-map candidate;
- minor raid forbids cross-map candidate;
- major decisive landing allows cross-theater escalation when local strength is insufficient;
- same-theater adequate package beats remote oversized army;
- guard budget caps multiple low-value ports;
- active invasion persists through one recovery frame;
- recovered threat releases after cooldown;
- player-command candidate is rejected;
- critical-front candidate is rejected unless decisive escalation and no alternative.

Runtime smoke:

- start a campaign and confirm `[DefenseIntent]` summary appears only on signature change or verbose heartbeat;
- if a sea invasion appears, confirm `InvasionWatch` or `ActiveInvasion` posture;
- confirm selected candidate count and desired strength look proportional;
- confirm no repeated warnings or per-frame spam.

## Open Implementation Questions

- Whether `AICampaign.seainvasionforce` is safely accessible by reflection at all times or only after `AICampaign.aifaction` initializes.
- Whether `SeaInvasion.unitsinrange` is updated reliably enough for weekly strategic review, or whether runtime should compute its own enemy cluster around the spot.
- Whether vanilla `CheckForDefensiveOperations` can be steered with Postfix/Prefix state edits, or whether a small explicit active-landing movement order is needed after observer proof.

These questions should be answered in the implementation plan before code changes.
