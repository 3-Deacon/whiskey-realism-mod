# Tactical Brain Design

Status: draft umbrella design spec for Slice B.
Scope: battlefield tactical AI for land battles. This spec covers doctrine, scoring, state, patch surfaces, telemetry, and implementation order. It does not implement code.

## Source Findings

This spec is grounded in current Whiskey code, prior Slice B subagent research, and verified vanilla anchors from `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

Current Whiskey anchors:

- `PersonalityVector` and `CIC.Effective(...)` provide the existing faction, era, and commander personality inputs.
- `FrontSectorLedger`, `ArmyAreaLedger`, `FormationDirectiveLedger`, `CampaignMapLedger`, and formation snapshots provide strategic intent that tactical AI can read.
- `RealismCheckboxesLockPatch` already forces vanilla order delays on when realism settings are locked, so Slice B should treat delayed orders as a required realism constraint rather than an optional UI feature.
- Harmony patches must remain surgical and bounded. Patches may read Whiskey state and steer vanilla decisions, but tactical patches must not mutate strategic mod state.
- The tactical brain is explicitly deferred in the current handoff/memory unless the user redirects. This spec is that redirect into Slice B design.

Verified vanilla anchors:

- `AIBattle.CheckGlobalAIStrategy()` at line 6314 owns macro stance transitions. Vanilla stances are `0 assault`, `1 attack`, `2 defend`, and `3 retreat`.
- `AIBattle.AdjustGroupAIStance()` at line 4221 owns the group stance ladder.
- `AIBattle.MicroAICheckForCharges(...)` at line 4905 initiates charge behavior, including the `ai_stance == 4` charge path.
- `AIBattle.CheckForFeudGroupActions(...)` was previously found to skip the W&L `PerformAIActionDLCWL` gate, matching the brigade auto-charge bug.
- `AIBattle.CheckUseOfReserves(...)` at line 6062, `LinkReservesToLineGroup()` at line 6642, and `AssignReserves()` at line 7017 are the reserve surfaces.
- `AIBattle.CheckLineFallbacks(...)` at line 5118 and `AIBattle.MicroAICheckForRetreats(...)` at line 4817 are the local fallback and retreat surfaces.
- `AIBattle.CheckAIBombardment(...)` at line 3869 is the artillery bombardment surface.
- `Autocalc.CheckUnitArrivals()` at line 20878 and `Regiment.GetArrivalTimeToBF(...)` at line 138862 show that reinforcements can arrive into active battles.
- `TimePanel.SetRetreatTimer(...)` at line 221271 controls the battle retreat timer after retreat is chosen.
- Prior decompile review found `BattleUnits` tracks strength still to arrive and reinforcement arrivals within the AI retreat decision window. Vanilla already considers reinforcements in some global-retreat logic, but not as a full tactical doctrine.
- `Regiment.AddOrderCourierline(...)` at line 125009 models bugle and courier order delivery, including horse courier creation when targets are outside bugle range.
- `Regiment.ProcessOrders()` at line 125173 propagates orders through courier lines and then down to subordinates, so a high-level order can cascade from parent formation to attached units rather than arriving everywhere instantly.
- `Regiment.GetLastTransmittedPathPos(...)` at line 127552 and `GetLastTransmittedPath(...)` at line 127591 expose the difference between intended paths and transmitted orders while order delay is active.
- `Regiment.SetOrderStatus(...)` at line 125484, `LaunchGoCommand(...)` at line 125510, and `OrderTimedMovement(...)` at line 125524 show existing order-state timing and execution gates.
- `Regiment` recalculates `commanderrange` and `buglerange` from unit type, readiness, and commander initiative around line 125838.
- `GamePrefs.processingtimegroupstandard`, `processingtimegrouproute`, `buglestandardrecognitiontime`, `orderdelayforbugles`, and `slowdowncourieroutsideradius` load around lines 53336-53344 and already provide data-side order-friction hooks.
- `BattleUI` passes `useorderdelay: true` for normal player movement orders at line 166163, confirming that tactical movement is expected to respect the order-delay path.

Historical doctrine inputs:

- Jomini's useful tactical translation is "mass at the decisive point", not "attack everywhere because total force is larger." For Whiskey, superior AI should seek local superiority in one or two sectors while other sectors fix, hold, or demonstrate.
- Clausewitz's useful tactical translation is that defense is the stronger form because it uses ground, preparation, and waiting, but a good defense still looks for a counterstroke after the attacker is spent or exposed.
- Casey/Hardee-era Civil War infantry practice supports skirmishers, screening, successive lines, sector command, and echelon/flank protection. The AI should probe and screen before main-body commitment, then attack through selected sectors with support nearby.
- Cavalry outpost doctrine emphasizes videttes, patrols, observation, reporting enemy strength/direction, and slow skirmishing withdrawal. This maps directly to battle scouting, flank security, and rear-guard behavior.
- Civil War assault examples such as Pickett's Charge show the failure mode this slice must avoid: large frontal commitment over exposed ground into a prepared position after inadequate confirmation that the enemy was actually broken.
- Civil War command was layered: army and corps commanders set intent and committed reserves, division commanders managed sectors, brigade commanders maneuvered regiments, and regimental officers executed local fire/movement. The AI should model these tiers instead of allowing a single omniscient battle brain to retask every regiment every tick.
- Civil War staff officers and couriers carried orders, guided formations into place, and converted commander intent into execution. Signals, bugles, and flags could accelerate local communication, but distance, terrain, staff quality, and headquarters position still imposed delay and friction.

Reference sources:

- Jomini, `The Art of War`: https://www.gutenberg.org/files/13549/13549-h/13549-h.htm
- Clausewitz, `On War`: https://www.gutenberg.org/ebooks/1946.html.images
- Casey, `Infantry Tactics`: https://commons.wikimedia.org/wiki/File:Infantry_tactics,_for_the_instruction,_exercise,_and_man%C5%93uvres_of_the_soldier,_a_company,_line_of_skirmishers,_battalion,_brigade,_or_corps_d%27arm%C3%A9e_(IA_infantrytacticsf02brig).pdf
- Hardee, `Rifle and Light Infantry Tactics`: https://openlibrary.org/works/OL5804461W/Rifle_and_light_infantry_tactics
- Cavalry outpost doctrine: https://www.gutenberg.org/ebooks/54515.html.images
- Civil War infantry tactics summary: https://en.wikipedia.org/wiki/Infantry_in_the_American_Civil_War
- Pickett's Charge example: https://www.battlefields.org/learn/articles/picketts-charge
- Civil War command hierarchy, National Park Service: https://www.nps.gov/articles/from-regiment-to-president-the-structure-and-command-of-civil-war-armies.htm
- Civil War army structure, National Park Service: https://home.nps.gov/kemo/learn/historyculture/army-structure.htm
- Civil War military staff, American Battlefield Trust: https://www.battlefields.org/learn/articles/military-staff
- Civil War signal corps, National Park Service: https://home.nps.gov/anti/learn/historyculture/signal.htm

## Goal

Make battlefield AI fight like a commander with a plan instead of collapsing into a blob.

The AI should:

- scout and screen before committing large formations;
- estimate enemy strength and strong points from contact, spotting, terrain, and uncertainty instead of acting omniscient;
- hold when contact is uncertain or terrain is favorable;
- attack by sector, probe, bombard, flank, or refuse instead of sending one mass forward;
- secure and reinforce flanks;
- bring reserves forward to relieve battered brigades or divisions before they rout;
- decide when artillery should bombard rather than infantry immediately attacking;
- avoid fortifications or attack weak points when avoidance is impossible;
- recognize bad odds such as 4,000 against 12,000 and withdraw if no realistic relief or terrain advantage exists;
- conduct staged withdrawals with covering troops, not instant all-unit routs;
- issue tactical intent through army/corps, division, brigade, and regimental command tiers, with realistic order delay and subordinate initiative;
- make general personality matter as threshold pressure, not as deterministic scripting.

## Non-Goals

- No custom battle renderer, movement engine, or total replacement of `AIBattle.UpdateAITasks`.
- No broad Prefix that skips vanilla battle AI wholesale.
- No omniscient perfect enemy information.
- No tactical AI for the player's units when W&L hierarchy says vanilla/player control should apply.
- No attempt to make every historical battle replay a scripted historical outcome.
- No deterministic retreat whenever outnumbered. Odds are one input; terrain, objectives, morale, ammo, casualties, reinforcements, and commander profile also matter.
- No game-ruining retreat loop. Withdrawal needs hysteresis, cooldowns, and staged execution.
- No instant whole-army retasking that bypasses vanilla order-delay/courier mechanics.

## Design Summary

Slice B should be built as a tactical doctrine layer around vanilla, not a battle-AI replacement.

The core addition is a runtime-only tactical brain that produces five read-only outputs for patches:

- `TacticalBattlePlan`: the side's current high-level idea for the battle.
- `TacticalCommandLedger`: hierarchy-aware commander intent, order authority, order age, and communication friction.
- `TacticalOddsDoctrine`: current/projected odds, local-superiority opportunities, and inferior-force preservation posture.
- `TacticalSectorLedger`: sector-by-sector contact, strength, terrain, flank, and mission assessment.
- `TacticalDoctrineDecision`: bounded decisions for macro stance, group stance, reserve relief, bombardment, charge gating, flank security, and withdrawal.

Harmony patches should steer existing vanilla decision points with Postfixes where possible. Prefixes are allowed only for narrow bug guards, mainly W&L action gating and dangerous charge suppression where vanilla exposes no clean Postfix surface.

## Tactical Battle Plan

At battle start and after material changes, each AI side forms a broad plan:

- `DefendObjective`: hold key ground, anchor flanks, use artillery, counterattack only local weak points.
- `DelayAndPreserve`: screen, trade space for time, keep reserves intact, withdraw if enemy pressure becomes decisive.
- `ProbeAndFix`: scout with skirmishers or cavalry, find enemy line, avoid major commitment until strong points are known.
- `BombardAndAssaultWeakPoint`: deploy artillery, suppress a selected sector, then commit limited assault formations.
- `TurnFlank`: refuse or hold the center while mobile formations probe around an open flank.
- `AvoidStrongpoint`: do not attack fortifications or heavy-cover positions directly unless objective pressure forces it.
- `OrderlyWithdrawal`: disengage in phases while covering troops delay.

The plan is not persisted. It is recomputed from current battle state and stabilized by cooldowns so it does not flap every tick.

Inputs:

- visible and recently spotted enemy units;
- own strength, morale, fatigue, ammo, casualties, rout ratio, and reserve availability;
- projected reinforcements and arrival time;
- terrain, cover, elevation, fortifications, artillery lines, roads, and objective locations;
- formation directives and strategic theater posture;
- commander personality and competence;
- battle type, victory bar, objective stakes, and time already held.

Outputs:

- macro stance preference;
- sector missions;
- reserve policy;
- artillery policy;
- attack/charge permission;
- withdrawal posture.

## Command Hierarchy And Order Friction

The tactical brain should not command every regiment as if it had a radio. It should model layered authority and the delay created by messengers, horses, bugles, flags, staff work, terrain, and distance.

Create a `TacticalCommandLedger` that tracks command tiers:

- `ArmyOrCorpsCommander`: owns battle plan, main effort, reserve release, full retreat, and major flank/turning decisions.
- `DivisionCommander`: owns sector mission, division frontage, support line, local reserve, and whether a sector holds, probes, assaults, refuses, or withdraws.
- `BrigadeCommander`: owns brigade-level maneuver, formation, skirmisher deployment, relief, immediate counterstroke, and local fallback.
- `RegimentalLeadership`: owns local fire discipline, cover use, immediate morale reaction, charge follow-through, and short fallback inside current orders.

Authority rules:

- army/corps intent changes should be infrequent and high impact;
- division orders translate the plan into sector missions and should not be rewritten every tick;
- brigade orders adapt to local contact within the division mission;
- regiments can react to immediate danger but should not create a new operational plan alone;
- artillery commanders can displace or choose targets inside the current artillery policy, but major battery relocation should require higher intent unless the guns are threatened.

Order-friction rules:

- orders outside bugle/signal range should be treated as delayed until courier/orderqueue state says they are delivered;
- when `GameVars.useorderdelays` is true, tactical scoring should use `GetLastTransmittedPathPos(false)` for what a unit has actually been told, and `GetLastTransmittedPathPos(true)` only for intended/future-path analysis;
- do not repeatedly replace an undelivered order with a new one unless the previous order has become dangerous or impossible;
- stale orders should be detectable: if an order arrives after contact has changed materially, the subordinate may pause, hold, or ask for new orders instead of blindly executing a suicidal move;
- high initiative/competence commanders should issue clearer intent and tolerate subordinate local action; low competence or wounded/disrupted commanders should create longer delays, poorer coordination, and more piecemeal execution;
- staff, telegraph/balloon/perk effects, command range, readiness, and W&L incidents should modify order delay only through existing vanilla surfaces where possible.

Desired command behavior:

- army/corps commander chooses `mainEffort`, `reservePolicy`, and `retreatPolicy`;
- division commanders assign sectors and coordinate adjacent brigades so attacks are not a blob;
- brigade commanders deploy skirmishers, form successive lines, relieve battered regiments, and execute limited local attacks;
- regiments preserve their own frontage, cover, ammo, and morale under current brigade orders;
- subordinate initiative can save a unit from obvious disaster but cannot freely override strategic intent unless the commander is routed, killed, detached, or out of command.

Acceptance criteria:

- A corps-level plan change should not instantly move every regiment at once.
- Division and brigade orders should show staggered execution when formations are far apart.
- Units inside bugle/signal range can react faster than units requiring couriers.
- A reserve release should propagate from higher command to the reserve formation, then to attached brigades/regiments.
- A staged withdrawal should issue main-body orders first and rear-guard orders later, with order-delay-aware timing.

## Intelligence And Scouting

The AI must not immediately dive into unseen enemies.

Create a `TacticalContactLedger` that separates:

- `Confirmed`: visible enemy units with current strength/morale/ammo if available.
- `RecentContact`: enemies recently spotted or fired from but not currently visible.
- `InferredStrongPoint`: concentrated fire, repeated sightings, forts, entrenchments, artillery flashes, high cover, or known objective defense.
- `Unknown`: likely enemy approach sectors not yet scouted.

Enemy strength estimates should be ranges:

- lower bound from confirmed visible units;
- expected value from confirmed plus recent contacts;
- upper bound from visible units plus inferred nearby strength;
- uncertainty penalty when scouting is poor.

Scouting behavior:

- cavalry/skirmish-capable units screen ahead of the main line when the enemy is not fixed;
- screeners should probe sectors, not all converge on the same enemy;
- screeners fall back behind the main line when enemy pressure exceeds their morale/strength/ammo tolerance;
- main line should hold or advance cautiously until at least one enemy sector is classified.

Acceptance criteria:

- A side with no reliable enemy contact should prefer `ProbeAndFix` or `DefendObjective`, not immediate all-line assault.
- Contact reports should age out so stale sightings do not freeze the battle forever.
- Strong-point inference must be confidence-scored so one musket volley does not create a permanent no-go zone.

## Sector Doctrine

The battlefield should be partitioned into sectors relative to the AI side:

- left flank;
- left-center;
- center;
- right-center;
- right flank;
- reserve/rear;
- artillery line.

Each sector gets a mission:

- `Screen`: scout, delay, reveal, avoid decisive engagement.
- `Hold`: defend ground, maintain line, preserve cohesion.
- `Fix`: engage enough to pin enemy without full assault.
- `Probe`: limited attack to test enemy strength.
- `Assault`: committed attack against a chosen weak point.
- `Refuse`: bend back or hold defensively to deny an enemy flank.
- `Relieve`: reserve replaces a battered unit or division.
- `Bombard`: artillery suppresses before movement.
- `Withdraw`: fall back to a selected line.
- `RearGuard`: cover the withdrawal of the main body.

Only selected sectors should attack. Adjacent sectors may fix or support. This is the primary anti-blob mechanism.

Sector scoring should consider:

- own/enemy strength ratio in that sector;
- morale, fatigue, ammo, casualties, and rout ratio;
- cover, elevation, fortification, water/obstacle, and road access;
- artillery support and line of sight;
- flank exposure and friendly support;
- objective value;
- commander personality.

## Odds And Local Superiority Doctrine

The tactical brain should reason from local superiority, not just total battle strength. A larger army should not blob. A smaller army should not passively die in place.

Create a `TacticalOddsDoctrine` that computes:

- `currentGlobalOdds`: active own strength versus estimated active enemy strength.
- `projectedGlobalOdds`: current odds plus likely own/enemy reinforcements inside the tactical window.
- `localSectorOdds`: own/enemy strength by sector, with confidence ranges.
- `decisivePoint`: the sector where local advantage, terrain, objective value, and enemy weakness make action worthwhile.
- `economyOfForceSectors`: sectors that should hold, fix, screen, or demonstrate with minimum necessary force.
- `inferiorForcePosture`: whether the weaker side should defend, delay, counterstroke, or withdraw.

Suggested posture bands:

| Estimated odds | Default posture | Doctrine |
|---|---|---|
| `>= 2.5:1` local advantage | `DecisiveAttack` | Fix most sectors, assault the decisive sector, keep a reserve, exploit only after collapse. |
| `1.5:1` to `2.5:1` | `LimitedAttack` | Probe, bombard, flank if safe, attack by sector. |
| `0.9:1` to `1.5:1` | `Balanced` | Scout, hold good ground, attack only confirmed weak sectors. |
| `0.6:1` to `0.9:1` | `DefensiveDelay` | Shorten line, use artillery, refuse flanks, counterattack locally. |
| `< 0.6:1` | `OrderlyWithdrawal` | Cover retreat, preserve artillery, use rear guard, full retreat if no relief. |
| `< 0.6:1` with strong terrain or near relief | `DelayAndPreserve` | Hold prepared line, avoid charges, buy time for reinforcements or night/objective relief. |

Superior-force behavior:

- choose one main-effort sector and at most one supporting sector;
- assign non-main sectors to `Hold`, `Fix`, `Screen`, or `Demonstrate`;
- keep a reserve fraction unless enemy collapse or own flank crisis justifies commitment;
- prefer flank, gap, exposed artillery, low-morale enemy, or poor-cover sector over a prepared front;
- bombard or probe strong points before assault;
- pursue with cavalry/fresh troops only after enemy withdrawal or rout is confirmed.

Inferior-force behavior:

- find a shorter, better defensive line using cover, height, woods, rivers, fortifications, or map edge;
- refuse threatened flanks and avoid long thin lines;
- screen and delay with cavalry/skirmishers while the main body forms or withdraws;
- launch only local counterstrokes against overextended or disordered attackers;
- relieve battered units before rout when reserves are available;
- choose staged withdrawal when projected odds, morale, ammo, casualties, and reserve state all point toward decisive defeat.

Hard rules:

- never translate global superiority directly into all-sector attack;
- never translate global inferiority directly into instant full retreat;
- local action needs confidence and a route, not just a ratio;
- personality can shift thresholds but cannot erase hard collapse or hard opportunity.

## Macro Stance Scoring

Patch surface: `AIBattle.CheckGlobalAIStrategy()`.

Vanilla macro stances are too coarse and partly data-driven. Whiskey should add a score layer that biases or clamps vanilla stance transitions:

- `Assault`: only when enemy is weak, disorganized, exposed, low on ammo/morale, or the objective clock demands risk.
- `Attack`: normal offensive pressure, preferably by sectors.
- `Defend`: hold ground, recover cohesion, use artillery, prepare reserves.
- `Retreat`: staged withdrawal or full retreat when defeat risk is sustained and relief is unlikely.

The score should include:

- current odds and projected odds including reinforcements;
- local sector odds and decisive-point confidence;
- battle objective stakes;
- own losses and rout risk;
- enemy weakness or strong-point confidence;
- artillery advantage;
- terrain advantage;
- flank security;
- commander aggressiveness, caution, initiative, and casualty tolerance;
- elapsed time since last major stance change.

Safeguards:

- use hysteresis so stance does not bounce between attack and retreat;
- never block vanilla hard retreat/end-battle safety;
- do not force attack if the W&L player-control gate says the AI should not act.

## Group Stance Ladder

Patch surface: `AIBattle.AdjustGroupAIStance()`.

Group-level stance must become sector- and condition-aware.

Replace raw strength-only behavior with a weighted ladder:

- `Screen` when contact is uncertain, unit is light/cavalry/skirmish-capable, or assigned to flank security.
- `Hold` when defending good terrain, guarding artillery, protecting flank, or low ammo/fatigue makes attack unsound.
- `Fix` when a sector must pin the enemy while another sector maneuvers.
- `Attack` when sector odds and terrain are acceptable.
- `Charge` only after enemy weakness is confirmed and charge gates pass.
- `Fallback` when local morale/ammo/casualties/flank exposure are dangerous.
- `Relieve` when the unit should be replaced by reserves rather than left to rout.

This should reduce one-big-blob attacks by allowing only selected sectors to receive attack/charge stance while nearby sectors hold or fix.

## Charge And Feud Gating

Patch surfaces:

- `AIBattle.MicroAICheckForCharges(...)`;
- `AIBattle.CheckForFeudGroupActions(...)`;
- existing `PerformAIActionDLCWL` behavior.

Charge should be treated as a late tactical decision, not the default way to close distance.

Charge permission requires:

- visible or confidently fixed target;
- favorable local morale and cohesion;
- enough ammo/fatigue margin or a clear melee opportunity;
- target weakness: low morale, routed neighbor, flank exposure, low cover, disrupted line, artillery overrun opportunity, or major local odds advantage;
- flank security or acceptable risk;
- not attacking into strong fort/entrenchment/cover unless objective pressure overrides;
- commander profile accepts the risk.

Charge denial triggers:

- W&L `PerformAIActionDLCWL` says the AI should not command that unit;
- unit is outflanked, exhausted, low morale, low ammo, or already under heavy fire;
- target is a strong point with no suppression;
- the sector mission is `Screen`, `Hold`, `Fix`, `Withdraw`, or `RearGuard`;
- another nearby unit is already charging the same target and would create a blob.

The W&L auto-charge bug should be a narrow guard patch, not a broad rewrite.

## Reserves And Relief

Patch surfaces:

- `AIBattle.AssignReserves()`;
- `AIBattle.LinkReservesToLineGroup()`;
- `AIBattle.CheckUseOfReserves(...)`.

Reserve doctrine must distinguish reinforcing success from rescuing collapse.

Reserve roles:

- `LineRelief`: replace a battered brigade/division before rout.
- `FlankGuard`: hold behind an exposed flank.
- `Counterattack`: wait for enemy overextension.
- `Exploit`: move through a confirmed weak point.
- `WithdrawalCover`: form the rear guard for staged retreat.
- `ArtilleryGuard`: protect batteries and supply line.

Relief trigger:

- frontline unit/division has high casualties, falling morale, low ammo, severe fatigue, or rout neighbors;
- sector remains important enough to hold;
- reserve can arrive before local collapse;
- reserve commitment will not expose a more dangerous flank.

Reserve discipline:

- do not magnetize reserves into melee simply because a nearby line group is fighting;
- avoid stacking multiple reserves on the same target;
- keep at least one reserve uncommitted unless battle plan, breakthrough exploitation, rear-guard duty, or emergency requires full commitment;
- preserve a reserve fraction when superior so the AI can exploit success without stripping flank security;
- preserve a reserve fraction when inferior so the AI can relieve a collapsing sector or cover withdrawal;
- route reserves through safer rear/road paths where possible.

## Flank Security And Denial

Patch surfaces:

- vanilla flank calculations around `CalculateFlankData`, `CheckIfFlanksAreAnchored`, `CheckFlankMoves`, `GroupIsOutflanked`, and `CheckIfOutflanked`;
- reserve and group stance patches above.

The AI should understand both its own flank risk and enemy flank opportunities.

Own-flank behavior:

- refuse a threatened flank instead of always charging;
- bring reserves or cavalry to cover exposed endpoints;
- anchor on terrain, fortifications, woods, rivers, or map edge when practical;
- fall back one sector if holding would create encirclement.

Enemy-flank behavior:

- probe open flank with cavalry/skirmishers first;
- use artillery or fixing attacks to pin the center;
- commit larger assault only after the flank path is not a trap;
- stop flanking movement if enemy reserves close the opening.

Acceptance criteria:

- The AI should not abandon center/objectives just because a theoretical flank route exists.
- The AI should not keep feeding a flank attack after the enemy has anchored it.

## Terrain, Strong Points, And Weak Points

Vanilla exposes terrain/cover APIs through `BattlefieldSetup` and per-regiment terrain fields. Slice B should use those instead of ad hoc coordinate guesses.

Strong-point signals:

- fortification or entrenchment;
- high cover value;
- elevation advantage;
- artillery overwatch;
- repeated strong fire from a sector;
- high confirmed enemy strength;
- protected flank or obstacle;
- objective with prepared defenders.

Weak-point signals:

- low confirmed enemy strength;
- low morale/rout evidence;
- low ammo or reduced fire;
- exposed flank;
- poor cover;
- unsupported artillery;
- gap between enemy sectors;
- objective not mutually supported by nearby defenders.

Decision rules:

- avoid direct attack on high-confidence strong points unless time/objective pressure demands it;
- bombard strong points before assault;
- attack weak points with one or two committed sectors while neighboring sectors fix;
- choose flank/route-around when fortifications block the direct path and the route is feasible;
- if no weak point is known, scout or bombard instead of blob-attacking.

## Artillery Doctrine

Patch surface: `AIBattle.CheckAIBombardment(...)`, artillery fallback/unlimber helpers, and group stance interaction.

Artillery should decide between:

- `Deploy`: take a firing position with line of sight and protection.
- `BombardStrongPoint`: suppress fortification, high-cover line, or objective.
- `CounterBattery`: target enemy artillery when it threatens infantry or friendly guns.
- `SupportAssault`: bombard the chosen assault sector before infantry closes.
- `Displace`: move when masked, threatened, or out of useful range.
- `Fallback`: withdraw when cavalry/infantry closes and protection is gone.

Bombard instead of attacking when:

- target is a strong point;
- enemy is visible but infantry odds are poor;
- own infantry is low morale/fatigue/ammo;
- artillery has good line of sight and ammunition;
- reinforcement arrival or flank maneuver needs time.

Do not let artillery doctrine create passive forever-battles:

- bombardment gets a time/effectiveness review;
- if bombardment has no effect and objective pressure rises, switch to probe/withdraw/reposition;
- if enemy retreats or exposes a flank, infantry can advance by sector.

## Reinforcements And Battle Participation

Vanilla campaign-to-battle participation can pull nearby units into a battle, and active battle units can have arrival times. Slice B should treat reinforcement state as central to attack and retreat decisions.

Projected odds:

- `currentOdds`: active own strength versus estimated active enemy strength.
- `nearFutureOdds`: active plus likely arrivals inside the tactical decision window.
- `lateFutureOdds`: later reinforcements that may matter for a deliberate defense but not for an immediate assault.

Doctrine:

- if outnumbered now but relief is close and ground is good, hold/delay instead of immediate retreat;
- if outnumbered badly and no relief is close, choose orderly withdrawal;
- if reinforcements are close, use existing forces to screen and preserve a line for arrival;
- do not attack merely because reinforcements exist if they cannot arrive before local collapse;
- if enemy reinforcements are likely, avoid overcommitting into a trap.
- if superior now but enemy reinforcements are close, prefer limited attack, flank security, and reserve preservation over full commitment.

Example rule:

- 4,000 versus 12,000 with no strong terrain and no timely reinforcements should move toward `OrderlyWithdrawal`.
- 4,000 versus 12,000 in prepared terrain with 8,000 arriving soon may become `DelayAndPreserve`.
- 4,000 versus 12,000 while guarding a decisive objective may hold longer, but should still plan a covered fallback.

## Withdrawal Doctrine

Patch surfaces:

- `AIBattle.CheckGlobalAIStrategy()`;
- `AIBattle.CheckLineFallbacks(...)`;
- `AIBattle.MicroAICheckForRetreats(...)`;
- `TimePanel.SetRetreatTimer(...)` only as an observed downstream effect, not a first target.

Withdrawal should be staged:

1. `Stabilize`: stop new assaults, form a defensive line, protect artillery.
2. `Screen`: cavalry/skirmishers and selected units slow enemy contact.
3. `BulkWithdraw`: main body falls back by sectors toward a safer line or battle exit.
4. `RearGuard`: covering troops hold briefly while the bulk disengages.
5. `RearGuardWithdraw`: cover troops fall back last.
6. `FullRetreat`: commit to vanilla retreat/end-battle path when preservation requires it.

Retreat triggers:

- sustained severe odds disadvantage;
- high losses relative to force size;
- morale/rout cascade risk;
- low ammo across committed line;
- reserves exhausted or unable to relieve;
- both flanks exposed or one flank collapsing toward encirclement;
- enemy strong point cannot be cracked and objective is no longer worth losses;
- projected reinforcements do not improve odds soon enough.

Anti-retreat-loop safeguards:

- require sustained danger over a minimum evaluation window unless collapse is immediate;
- use hysteresis before re-entering attack after retreat/posture change;
- personality modifies thresholds but cannot suppress obvious collapse forever;
- objective importance can delay retreat, not cancel it;
- prepared terrain and near reinforcements can delay full retreat, but should shift to delay/defense rather than blind attack;
- after a full retreat decision, do not instantly re-engage the same fight unless campaign-layer conditions changed materially.

## Loss, Morale, Ammo, And Fatigue Awareness

Every tactical decision should include own-unit condition:

- morale;
- ammo;
- fatigue;
- casualties since battle start;
- wounded/killed/missing/captured if available;
- rout ratio and routed neighbors;
- received fire and flank fire;
- artillery ammunition and threat proximity.

Condition effects:

- low ammo favors hold, relief, fallback, or bayonet charge only if target is already broken and close;
- low morale favors relief, hold in cover, or withdrawal;
- high fatigue blocks long flanking moves and charges;
- high casualties lower assault score and increase relief score;
- routed neighbors increase local collapse risk;
- fresh reserves increase hold/counterattack options.

## Commander Personality

General personality should matter, but not dominate reality.

Suggested modifier mapping:

- high aggression lowers attack/charge thresholds and raises willingness to exploit;
- high caution raises scouting, bombardment, reserve, and withdrawal preference;
- high initiative increases flank/probe/counterattack likelihood;
- high competence reduces blob behavior, improves reserve timing, and reduces bad charges;
- high leadership/administration/staff quality reduces order friction and improves cross-division timing;
- high casualty tolerance delays withdrawal and relief slightly, but cannot ignore rout/loss collapse;
- poor competence increases uncertainty penalties and over/under-reaction risk within bounded limits.

Personality should be applied as small threshold modifiers after material factors are scored. It should not create scripted personalities that always attack or always retreat.

## Integration Architecture

Proposed new namespace:

- `src/WhiskeyRealism/Tactical/TacticalBattleContext.cs`
- `src/WhiskeyRealism/Tactical/TacticalCommanderProfile.cs`
- `src/WhiskeyRealism/Tactical/TacticalCommandLedger.cs`
- `src/WhiskeyRealism/Tactical/TacticalOrderFriction.cs`
- `src/WhiskeyRealism/Tactical/TacticalContactLedger.cs`
- `src/WhiskeyRealism/Tactical/TacticalOddsDoctrine.cs`
- `src/WhiskeyRealism/Tactical/TacticalSectorLedger.cs`
- `src/WhiskeyRealism/Tactical/TacticalBattlePlan.cs`
- `src/WhiskeyRealism/Tactical/TacticalDoctrineScorer.cs`
- `src/WhiskeyRealism/Tactical/TacticalRetreatDoctrine.cs`
- `src/WhiskeyRealism/Tactical/TacticalTelemetry.cs`

Patch candidates:

- `BattleMacroStrategyPatch` on `AIBattle.CheckGlobalAIStrategy()`.
- `BattleGroupStancePatch` on `AIBattle.AdjustGroupAIStance()`.
- `BattleOrderFrictionPatch` only if vanilla AI movement bypasses order delays in a place that Slice B must steer; otherwise prefer scorer discipline and existing `SetWaypoint(... useorderdelay: true ...)` calls.
- `BattleChargeGatePatch` on `AIBattle.MicroAICheckForCharges(...)`.
- `BattleFeudActionGatePatch` on `AIBattle.CheckForFeudGroupActions(...)`.
- `BattleReserveDoctrinePatch` on reserve assignment/use methods.
- `BattleBombardmentPatch` on `AIBattle.CheckAIBombardment(...)`.
- `BattleFallbackDoctrinePatch` on fallback/retreat methods.

Patch order should start with telemetry and guards, then scoring, then behavior steering.

## Battleprefs And Data Tuning

Some behavior can be influenced by `Config/battleprefs.txt`, including macro thresholds, micro stance triggers, reserves/flanking thresholds, charge triggers, feud movement, skirmisher morale, and retreat settings.

Whiskey should not edit the Steam install's config directly. Use battleprefs as research and either:

- mirror safe values into Whiskey config/defaults;
- adjust in memory after vanilla load if safe;
- or prefer code scoring when a setting is too global to tune without side effects.

Data tuning is useful for broad pressure. It is not enough for scouting, sector doctrine, reserve relief, personality, or staged withdrawal.

## Implementation Slices

This umbrella spec should be implemented in bounded steps:

1. `B0 Tactical Observer`: read-only battle context, command/contact/sector ledgers, order-friction telemetry, no behavior changes.
2. `B1 W&L Feud And Charge Guard`: narrow guard for player-subordinate auto-charge/feud actions.
3. `B2 Command Hierarchy And Order Friction`: army/corps, division, brigade, and regiment intent cascade with delayed-order rules.
4. `B3 Tactical Odds Doctrine`: local-superiority, decisive-point, economy-of-force, and inferior-force posture scorer.
5. `B4 Macro Stance Scorer`: Postfix/clamp global strategy with odds, losses, reinforcements, terrain, and personality.
6. `B5 Group Sector Stance`: sector-aware hold/screen/fix/probe/attack stance decisions.
7. `B6 Reserve Relief And Flank Doctrine`: reserve roles, relief triggers, flank guard/refuse behavior.
8. `B7 Artillery And Strongpoint Doctrine`: bombardment before assault, counterbattery, avoid/attack weak points.
9. `B8 Withdrawal Doctrine`: staged fallback, rear guard, full retreat safeguards.
10. `B9 Tuning And Telemetry Soak`: battleprefs validation, bounded logs, runtime smoke matrix.

Each slice should have pure scorer tests before Harmony patch wiring.

## Telemetry

Telemetry must be bounded and useful:

- one first-fire line per patch;
- per-side battle-plan summary on plan change;
- command summary on material hierarchy/order-delay changes;
- sector summary only when signature changes;
- retreat decision line only when posture escalates or de-escalates;
- charge denial/permission summary sampled or OnceLog-gated;
- reserve relief line only when a reserve assignment changes.

Example log shape:

```text
[TacticalPlan] side=CSA plan=DelayAndPreserve odds=0.34 projected=0.72 terrain=strong relief=82m commander=Johnston
[TacticalCommand] side=CSA tier=division unit=Hood mission=RefuseRight order=delayed method=courier eta=18m parent=Longstreet
[TacticalSector] side=USA sector=right mission=Probe own=4200 enemy=2100 conf=0.62 terrain=open flank=open artillery=2
[TacticalRetreat] side=CSA stage=Screen reason=odds-losses-noRelief current=0.33 projected=0.39 losses=0.18 morale=0.42
```

## Testing

Pure tests should cover:

- enemy strength estimate from confirmed/recent/inferred contacts;
- no-contact plan chooses scout/hold, not assault;
- army/corps intent maps to division sector missions without directly retasking every regiment;
- division mission maps to brigade actions while preserving subordinate local reaction;
- orders outside bugle/signal range are delayed and do not affect transmitted path state until delivery;
- stale delayed orders are paused or downgraded when they arrive into materially changed contact;
- high initiative/competence reduces friction without making orders instant;
- superior global odds do not cause all sectors to attack;
- superior local odds choose one decisive sector and assign economy-of-force missions elsewhere;
- inferior global odds choose defense/delay before retreat when terrain or relief justifies it;
- inferior global odds choose orderly withdrawal when terrain, morale, ammo, reserves, and projected relief are all bad;
- sector attack chooses one weak sector while neighbors fix/hold;
- strong point favors bombard/avoid;
- 4,000 versus 12,000 with no relief chooses withdrawal;
- 4,000 versus 12,000 with strong terrain and near relief chooses delay;
- low morale/ammo/casualties trigger relief before rout;
- exhausted reserves prevent false hold confidence;
- commander aggression changes thresholds without overriding impossible odds;
- charge denied against fortified strong point without suppression;
- rear-guard sequence progresses in order.

Runtime smoke should include:

- meeting engagement with no initial contact;
- defensive battle against superior force;
- attack against fortified/entrenched objective;
- reinforcement arrival during battle;
- artillery-heavy battle;
- large multi-division battle with order delays on;
- delayed reserve-release scenario where courier timing matters;
- W&L player-subordinate battle to confirm auto-charge is gated;
- outnumbered battle to confirm retreat happens but is staged.

## Acceptance Criteria

Slice B is successful when:

- AI no longer commonly opens with all formations rushing the same point without contact.
- Scout/screen behavior creates small early skirmishes before main commitment.
- Only selected sectors attack while others hold, fix, screen, or support.
- Army/corps, division, brigade, and regimental command tiers produce different decisions at the proper scale.
- Orders propagate with realistic delay and do not create instant whole-army pivots.
- Superior AI concentrates force at a decisive point instead of attacking everywhere.
- Inferior AI uses terrain, shortened lines, local counterstrokes, and staged withdrawal instead of standing to annihilation.
- Reserves relieve damaged units before the line routs when reserves are available.
- Artillery bombards strong points and supports assaults instead of always moving with infantry.
- AI avoids or works around fortifications when a feasible weak point exists.
- Flanks are protected, refused, reinforced, or exploited based on local conditions.
- Badly outnumbered forces preserve themselves through orderly withdrawal when relief and terrain do not justify holding.
- Retreat does not become a constant game-ruining escape behavior.
- Commander personality creates recognizable differences without breaking material tactical logic.

## Open Verification Before Implementation

Before writing patches, re-read exact current decompile bodies for:

- `AIBattle.CheckGlobalAIStrategy()`;
- `AIBattle.AdjustGroupAIStance()`;
- `AIBattle.MicroAICheckForCharges(...)`;
- `AIBattle.CheckForFeudGroupActions(...)`;
- `AIBattle.AssignReserves()`;
- `AIBattle.LinkReservesToLineGroup()`;
- `AIBattle.CheckUseOfReserves(...)`;
- `AIBattle.CheckAIBombardment(...)`;
- `AIBattle.CheckLineFallbacks(...)`;
- `AIBattle.MicroAICheckForRetreats(...)`.
- `Regiment.AddOrderCourierline(...)`;
- `Regiment.ProcessOrders()`;
- `Regiment.GetLastTransmittedPathPos(...)`;
- `Regiment.SetOrderStatus(...)`;
- `BattleUnits.SetWaypoint(...)` call sites used by candidate patches.

Do not rely only on this spec's line anchors when implementing. The shipped game DLL and current decompile remain the source of truth.
