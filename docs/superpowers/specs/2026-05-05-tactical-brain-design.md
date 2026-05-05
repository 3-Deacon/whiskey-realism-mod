# Tactical Brain Design

Status: draft umbrella design spec for Slice B.
Scope: battlefield tactical AI for land battles. This spec covers doctrine, scoring, state, patch surfaces, telemetry, and implementation order. It does not implement code.

## Source Findings

This spec is grounded in current Whiskey code, prior Slice B subagent research, and verified vanilla anchors from `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

Current Whiskey anchors:

- `PersonalityVector` and `CIC.Effective(...)` provide the existing faction, era, and commander personality inputs.
- `FrontSectorLedger`, `ArmyAreaLedger`, `FormationDirectiveLedger`, `CampaignMapLedger`, and formation snapshots provide strategic intent that tactical AI can read.
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
- make general personality matter as threshold pressure, not as deterministic scripting.

## Non-Goals

- No custom battle renderer, movement engine, or total replacement of `AIBattle.UpdateAITasks`.
- No broad Prefix that skips vanilla battle AI wholesale.
- No omniscient perfect enemy information.
- No tactical AI for the player's units when W&L hierarchy says vanilla/player control should apply.
- No attempt to make every historical battle replay a scripted historical outcome.
- No deterministic retreat whenever outnumbered. Odds are one input; terrain, objectives, morale, ammo, casualties, reinforcements, and commander profile also matter.
- No game-ruining retreat loop. Withdrawal needs hysteresis, cooldowns, and staged execution.

## Design Summary

Slice B should be built as a tactical doctrine layer around vanilla, not a battle-AI replacement.

The core addition is a runtime-only tactical brain that produces three read-only outputs for patches:

- `TacticalBattlePlan`: the side's current high-level idea for the battle.
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

## Macro Stance Scoring

Patch surface: `AIBattle.CheckGlobalAIStrategy()`.

Vanilla macro stances are too coarse and partly data-driven. Whiskey should add a score layer that biases or clamps vanilla stance transitions:

- `Assault`: only when enemy is weak, disorganized, exposed, low on ammo/morale, or the objective clock demands risk.
- `Attack`: normal offensive pressure, preferably by sectors.
- `Defend`: hold ground, recover cohesion, use artillery, prepare reserves.
- `Retreat`: staged withdrawal or full retreat when defeat risk is sustained and relief is unlikely.

The score should include:

- current odds and projected odds including reinforcements;
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
- keep at least one reserve uncommitted unless battle plan or emergency requires full commitment;
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
- high casualty tolerance delays withdrawal and relief slightly, but cannot ignore rout/loss collapse;
- poor competence increases uncertainty penalties and over/under-reaction risk within bounded limits.

Personality should be applied as small threshold modifiers after material factors are scored. It should not create scripted personalities that always attack or always retreat.

## Integration Architecture

Proposed new namespace:

- `src/WhiskeyRealism/Tactical/TacticalBattleContext.cs`
- `src/WhiskeyRealism/Tactical/TacticalCommanderProfile.cs`
- `src/WhiskeyRealism/Tactical/TacticalContactLedger.cs`
- `src/WhiskeyRealism/Tactical/TacticalSectorLedger.cs`
- `src/WhiskeyRealism/Tactical/TacticalBattlePlan.cs`
- `src/WhiskeyRealism/Tactical/TacticalDoctrineScorer.cs`
- `src/WhiskeyRealism/Tactical/TacticalRetreatDoctrine.cs`
- `src/WhiskeyRealism/Tactical/TacticalTelemetry.cs`

Patch candidates:

- `BattleMacroStrategyPatch` on `AIBattle.CheckGlobalAIStrategy()`.
- `BattleGroupStancePatch` on `AIBattle.AdjustGroupAIStance()`.
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

1. `B0 Tactical Observer`: read-only battle context, sector/contact ledger, telemetry, no behavior changes.
2. `B1 W&L Feud And Charge Guard`: narrow guard for player-subordinate auto-charge/feud actions.
3. `B2 Macro Stance Scorer`: Postfix/clamp global strategy with odds, losses, reinforcements, terrain, and personality.
4. `B3 Group Sector Stance`: sector-aware hold/screen/fix/probe/attack stance decisions.
5. `B4 Reserve Relief And Flank Doctrine`: reserve roles, relief triggers, flank guard/refuse behavior.
6. `B5 Artillery And Strongpoint Doctrine`: bombardment before assault, counterbattery, avoid/attack weak points.
7. `B6 Withdrawal Doctrine`: staged fallback, rear guard, full retreat safeguards.
8. `B7 Tuning And Telemetry Soak`: battleprefs validation, bounded logs, runtime smoke matrix.

Each slice should have pure scorer tests before Harmony patch wiring.

## Telemetry

Telemetry must be bounded and useful:

- one first-fire line per patch;
- per-side battle-plan summary on plan change;
- sector summary only when signature changes;
- retreat decision line only when posture escalates or de-escalates;
- charge denial/permission summary sampled or OnceLog-gated;
- reserve relief line only when a reserve assignment changes.

Example log shape:

```text
[TacticalPlan] side=CSA plan=DelayAndPreserve odds=0.34 projected=0.72 terrain=strong relief=82m commander=Johnston
[TacticalSector] side=USA sector=right mission=Probe own=4200 enemy=2100 conf=0.62 terrain=open flank=open artillery=2
[TacticalRetreat] side=CSA stage=Screen reason=odds-losses-noRelief current=0.33 projected=0.39 losses=0.18 morale=0.42
```

## Testing

Pure tests should cover:

- enemy strength estimate from confirmed/recent/inferred contacts;
- no-contact plan chooses scout/hold, not assault;
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
- W&L player-subordinate battle to confirm auto-charge is gated;
- outnumbered battle to confirm retreat happens but is staged.

## Acceptance Criteria

Slice B is successful when:

- AI no longer commonly opens with all formations rushing the same point without contact.
- Scout/screen behavior creates small early skirmishes before main commitment.
- Only selected sectors attack while others hold, fix, screen, or support.
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

Do not rely only on this spec's line anchors when implementing. The shipped game DLL and current decompile remain the source of truth.
