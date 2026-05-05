# Tactical Weapons And Ammunition Design

Status: paused draft focused design spec for Slice B adjunct work. Do not implement from this spec unless the user explicitly reopens tactical work.
Scope: live-battle infantry weapons, artillery ammunition, projectile behavior, fire discipline, smoke, and autoresolve parity. This spec does not implement code and does not replace the Slice B tactical-brain umbrella spec.

Related specs:

- [`2026-05-05-tactical-brain-design.md`](2026-05-05-tactical-brain-design.md) remains the Slice B umbrella.
- [`2026-05-05-tactical-brain-vanilla-verification.md`](2026-05-05-tactical-brain-vanilla-verification.md) remains the tactical AI vanilla-surface verification.

## Goal

Make Civil War weapons and ammunition matter tactically without replacing the battle engine.

The desired behavior is not simply "longer rifle range" or "more casualties." The goal is historically plausible fire behavior:

- infantry ammunition constrains sustained fire and assault timing;
- rifle-muskets have long theoretical range but degraded battlefield effectiveness under smoke, fatigue, morale, formation, and command friction;
- smoothbores remain dangerous at close range, especially in early-war and buck-and-ball contexts;
- breechloaders and repeaters create short-term fire superiority but burn ammunition quickly;
- artillery uses solid shot, shell/spherical case, and canister according to range, target, ammunition supply, and tactical mission;
- smoke degrades visibility and fire control instead of staying purely visual;
- autoresolve reflects any live-battle weapon doctrine changes.

## Non-Goals

- No custom ballistic engine.
- No total replacement of `Regiment.FireBullet`.
- No broad Prefix that skips vanilla fire, reload, projectile, or casualty logic wholesale.
- No tactical behavior changes before B0 observer logs prove the runtime shape.
- No direct edits to the game install's config files as the shipped mod path.
- No deterministic historical weapon outcomes. Weapon availability, doctrine, commander behavior, morale, and supply should create pressure, not scripts.
- No claim that every weapon data field is active until grep or runtime proves it.
- No assumption that buck-and-ball exists as a vanilla data flag. Whiskey approximates it through smoothbore doctrine unless a later verified weapon-data field appears.
- No passive "ammo regenerates after N minutes" behavior while a regiment remains in active contact.
- No Community Hotfix supersession in this slice. If later needed, fold it into a separate Slice E plan.

## Source Findings

Primary source: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

Confirmed vanilla anchors:

- `GameVars.WeaponGroup` around line 63725 defines weapon-group inventories and aggregate strength.
- `GameVars.Weapon` around line 63790 defines weapon fields: class, restrictions, year, range, RPM, caliber, artillery flags, projectile weight, velocity, trajectory, penetration, firepower bands, accuracy, reload speed, smoke, ammo usage, melee, artillery model, frontloader, and sound purpose.
- `GameVars.WeaponClass` around line 64193 defines weapon class names.
- `GameVars.ImportWeapons()` around line 65773 loads `Config/weaponclasses.dat`, `Config/weapons.txt`, and `Config/weapongroups.dat`.
- `Regiment.GetFireRange()` around line 118292 converts weapon range into battlefield fire range and modifies it for behavior, building assignment, elevation, sharpshooter perk, and lying down.
- `Regiment.FireBullet(int)` around line 118807 is a private `bool` method. It returns `false` when no shot is actually issued, and when successful it selects targets, artillery projectile type, smoke, accuracy, ammunition consumption, projectile creation, canister extra projectiles, and firing sounds.
- `Regiment.UpdateFiringAndReloading()` around line 119281 drives per-sprite/per-gun fire and reload state.
- `Regiment.SetAmmoVariable(int, float)` around line 117803 computes ammo state from supply and adds positive artillery resupply to all three artillery ammo pools.
- `Projectile.Initialize(...)`, `SetTargetNoise()`, `CheckHitsOnWay()`, and `DestroyBullet()` around line 108014 handle endpoint noise, trajectory raycasts, explosive/shrapnel hits, bouncing, building damage, and casualty dispatch.
- `Regiment.SufferCasualties(...)` around line 126396 applies casualties, cover, explosive effects, morale, experience, and received-fire state.
- `AIBattle.CheckCounterBatteryFire()` around line 3827 switches artillery into counterbattery behavior.
- `AIBattle.CheckAIBombardment()` around line 3869 switches artillery into bombardment behavior.
- `Autocalc.FightUnit()` and `Autocalc.GetROF()` around lines 21025-21250 run a separate autoresolve weapon model.
- `BattleUnits.CheckTimeIssues()` around line 86379 is the live battle day/night lifecycle. At end of day it calls `LevelAmmo()`, `ResupplyAllUnits()`, `AdjustConditionOfArmies()`, shows the troops-resupplied prompt, advances to the next day, and opens the deployment phase.
- `BattleUnits.SetTimeVariables()` around line 93543 sets `GameVars.currenttimefromstart` from `uniStormSystem.Hour + battlepasseddays * 24`. In live battle this is an hour counter; minutes are represented as fractions of an hour.
- `BattleUnits.CheckCeaseFireAllowance()` around line 83523 gates the player cease-fire option. The vanilla cease-fire path ends fighting for the day rather than refilling troops during active combat.
- `BattleUnits.CheckAINightWithdrawal()` around line 83491 and `CheckRetreatAtNightAllowance()` around line 83546 already decide whether a side withdraws at night. Night withdrawal can end the battle before resupply if conditions are bad.
- `BattleUnits.SetWithdrawal(...)` around line 92821 and `Regiment.SetWithdrawal(...)` around line 116116 are existing withdrawal movement surfaces, but they are tied to battle withdrawal/monument status and should not be reused blindly as a local ammo-run order.
- `TimePanel.SetRetreatTimer(...)` around line 221271 and `TimePanel.UpdateTime()` around line 221304 use `GameVars.currenttimefromstart` deltas, so retreat timers advance in battle-hours.
- `Autocalc` has a night "units are resupplying..." log path around line 20676, but `Autocalc.UpdateUnitsSupply()` around line 20953 is empty in the current decompile. Do not assume autoresolve has a working mid-battle supply implementation until runtime proves it.
- `AIBattle.CheckReliefOfObjectve(...)` around line 7008 and `CheckReliefOfObjectveDueToLowMorale(...)` around line 7012 are effectively empty in the current decompile. Vanilla does not appear to ship a live low-ammo or low-morale line-relief doctrine.
- `AIBattle.CheckLineFallbacks(...)` around line 5118 is a usable fallback model: it gates on infantry/cavalry type, no charge/melee/path, W&L command permission, morale/outflank pressure, then calls `RegimentSetPath(...)`, sets movement mode `2`, and writes fallback combat behavior.
- `AIBattle.MicroAICheckForRetreats(...)` around line 4817 is not a relief surface. It runs under macro retreat state, clears charge targets, sends units toward retreat positions or entry points, and should be reserved for battle-retreat behavior.
- `AIBattle.GetSubstituteFromSecondLine(...)` around line 5079 finds a same-type second-line unit with better morale, but no active call site was found in the current pass. Treat it as a vanilla clue, not a working relief mechanic.
- `AIBattle.AssignReserves(...)`, `AssignReserveToOperationalGroup(...)`, and `LinkReservesToLineGroup(...)` around lines 7017, 7153, and 6642 show vanilla reserve-group structure, but they mutate objective-chain membership and should be observed before direct Whiskey writes.
- `BattleUnits.SetWaypoint(...)` around line 91225 is the safer deliberate-order surface for brigade/group relief because it flows through vanilla W&L control, readiness, retreat/rout guards, order-delay queues, link cleanup, and group formation pathing before reaching `RegimentSetPath(...)`.

Important runtime facts:

- `Regiment` is the live battle authority. There is no separate soldier-level weapon brain. Visible soldiers are `Regiment.UnitSprite`; hit colliders are `Coy` objects tied back to parent regiments.
- Infantry, cavalry, skirmishers, and similar small-arms units use `ammo[0]`.
- Artillery uses three ammo slots: `ammo[0]` solid/ball, `ammo[1]` explosive/shell, `ammo[2]` canister.
- Artillery combat behavior meanings for this spec: `7` default/open artillery fire, `8` counterbattery, `9` bombardment.
- Artillery canister is selected when `distance / firerange <= GamePrefs.canisterrange`; `canisterrange` is a fraction of current fire range, not yards. Vanilla also allows fallback canister selection when total ammo is empty.
- Explosive artillery rounds are selected by configured probabilities for open-field, counterbattery, bombardment, covered, and long-distance targets.
- Low and empty ammo throttle reload/fire rate through `GamePrefs.rofonlowammo` and `GamePrefs.rofonammoout`; empty ammo is not a guaranteed hard no-fire condition.
- Artillery resupply with `SetAmmoVariable(1, positiveAmount)` adds the same amount to all three artillery ammo slots. It is not a separate shell-pool resupply surface.
- `Projectile` accuracy is endpoint noise plus raycasts, not a simple hit-roll.
- Canister creates three projectile traces: center, `startfireangle - 15`, and `startfireangle + 15`.
- `Config/weapons.txt` fields `reloadspeed[]` and `ammousage` are loaded, but this spec does not treat them as live tactical fire controls until implementation grep proves active use.
- `GameVars.weapon[weapon].smoke` is actively used when `Regiment.FireBullet(int)` animates smoke, so smoke doctrine has a vanilla weapon input to read.
- `battleprefs.txt` exposes key global knobs: standard rounds per unit type/ammo type, canister range, canister loading speed, explosive probabilities, low/out-of-ammo ROF, smoke probability, and visual smoke duration.
- Autoresolve uses its own ROF/firepower abstraction and must be kept in parity with live-battle changes.
- `Regiment.FireBullet(int)`, `Regiment.UpdateFiringAndReloading()`, and `Regiment.UpdateAutoTargets()` are private. Harmony patches must resolve them with string-name `[HarmonyPatch(typeof(Regiment), "MethodName")]` or `AccessTools.Method(...)`; do not rely on `nameof(...)` for private members.

## Historical Doctrine Inputs

Use these as design constraints, not exact numeric constants.

- Infantrymen commonly carried a 40-round cartridge box, with extra ammunition in reserve/trains. Sustained action depended on resupply, not unlimited fire.
- Major actions could involve extra cartridges on the soldier and deeper ammunition reserves in brigade/division/corps trains. The design implication is pressure and rotation, not instant exhaustion after one cartridge-box load.
- Three aimed rounds per minute is a reasonable trained muzzle-loader reference, but battlefield fire should degrade under smoke, fatigue, fear, poor command, and formation disorder.
- Rifle-muskets had much longer theoretical range than smoothbores, but common battle effectiveness was much lower than paper range because target visibility, training, smoke, and fire control mattered.
- Smoothbore muskets were still tactically relevant at close range, especially with buck-and-ball.
- Breechloaders and repeaters should give high short-term fire volume, especially to skirmishers/cavalry/elite formations, but require stronger ammunition discipline.
- Black powder smoke should reduce target identification and long-range fire control over sustained firing.
- Civil War artillery doctrine should distinguish solid shot, shell/spherical case, and canister by range and target. Canister is close-range defense; shell/spherical case is medium-range anti-personnel and covered-target work; solid shot is longer-range/direct-fire and counterbattery-friendly.
- Confederate weapon and ammunition supply should be more fragile, especially as war stage, blockade, standardization, and theater isolation worsen.
- Buck-and-ball doctrine is approximated as a smoothbore close-range effect. Vanilla weapon data exposes class, caliber, firepower bands, and year, but no verified buck-and-ball flag.
- Relief by fresh or less-depleted formations was a historical answer to exhausted ammunition. Units could hold with reduced fire, be relieved, fall back, replenish, and return later.
- Battlefield replenishment should include degraded emergency behavior such as scavenging cartridges from casualties or waiting for runners, but this should be weak compared with orderly relief to a reserve/supply posture.
- Darkness and exhaustion often created pauses for reorganization, casualty collection, ammunition replenishment, and renewed deployment. This supports battle-lull and end-of-day mechanics more than active-contact timer refills.

Reference sources:

- U.S. Army CMH Second Bull Run Staff Ride: https://history.army.mil/Portals/143/Images/Publications/Staff%20Rides/PNG/staffRide_SecondBullRun.pdf?ver=3S2u6tnaDqTPtK3VCkSh-Q%3D%3D
- NPS, Civil War Weapons in the Shenandoah Valley: https://www.nps.gov/articles/000/civil-war-weapons-in-the-shenandoah-valley.htm
- NPS, Gunpowder: https://www.nps.gov/casa/learn/historyculture/gunpowder.htm?fullweb=1
- NPS Antietam Artillery: https://home.nps.gov/anti/learn/historyculture/arty.htm?mobile-app=true&theme=wiki
- NPS Antietam Artillery Part 2: https://www.nps.gov/anti/learn/historyculture/arty2.htm
- Library of Congress, Ordnance Manual: https://www.loc.gov/item/ltf91083082/
- NPS Springfield Armory, Arms of the Confederacy: https://home.nps.gov/spar/learn/historyculture/arm-confederacy.htm
- NPS Gettysburg, Official Report of Richard Coulter: https://www.nps.gov/gett/learn/historyculture/official-report-of-richard-coulter.htm
- NPS Gettysburg, Official Report of Lt. Colonel John H. S. Funk: https://www.nps.gov/gett/learn/historyculture/official-report-of-lt-colonel-john-h-s-funk.htm
- NPS Antietam, Battle Report of Capt. John A. Tompkins: https://home.nps.gov/anti/learn/historyculture/tompkins-rpt.htm

## Current Vanilla Weapon Data Snapshot

The live game config already distinguishes weapon families well enough for a first doctrine pass:

| Weapon | Vanilla class | Observed vanilla traits |
|---|---:|---|
| Springfield Rifle-Musket | 2 rifled musket | 400 yd range, practical RPM 3, .58 caliber |
| Enfield Rifle-Musket | 2 rifled musket | 400 yd range, practical RPM 3 |
| Lorenz Rifle | 2 rifled musket | 400 yd range, practical RPM 3 |
| Mississippi Rifle | rifle | 500 yd range, practical RPM 2.5, higher accuracy |
| Springfield Musket | 1 smoothbore musket | 250 yd range, practical RPM 2.5, higher close firepower, lower accuracy |
| Sharps Rifle / Carbine | 3 breech loader | practical RPM 9 |
| Henry / Spencer repeaters | 4 repeating rifle | practical RPM 16 |
| 12-pounder Napoleon | 7 smoothbore gun | 1619 yd range, practical RPM 3, strong close canister profile |
| 10-pounder Parrott / 3-inch Ordnance | rifled guns | longer range, lower practical RPM, better accuracy |
| Whitworth | rifled gun | very long range, high accuracy |
| 30-pounder Parrott | heavy rifled artillery | very long range, low practical RPM |

This argues for behavior work before large data rewrites. The data can express range, RPM, accuracy, projectile, and firepower differences, but not ammunition discipline, smoke-impaired command, or historical shell-choice doctrine by itself.

## Design Summary

Build this as a tactical weapons doctrine layer with five read-only runtime outputs:

- `WeaponFireTelemetry`: observed fire events, weapon class, ammo slot, range band, hit/casualty outcome, target type, and smoke/contact context.
- `AmmunitionDoctrineState`: per-regiment ammunition pressure, projected endurance, weapon-family expenditure risk, and resupply confidence.
- `FireDisciplineDecision`: whether a unit should fire freely, conserve ammunition, hold fire, prefer close fire, or shift target.
- `ReliefAndFallbackDecision`: whether a regiment/brigade should hold, be relieved, fall back to reserve, fall back to supply, defend in place, or return to line.
- `ArtilleryDoctrineDecision`: projectile preference, bombardment permission, counterbattery priority, canister reserve threshold, and displacement/cease-fire pressure.

The first implementation plan must be observer-only. Behavior patches should come after logs prove:

- which weapon ids/classes appear in real W&L battles;
- how quickly small-arms and artillery ammo pools deplete;
- how often artillery reaches empty-pool fallback selection;
- what ranges casualties are actually produced at;
- how often smoke-heavy fights create visibility/contact ambiguity;
- how often AI bombardment burns shell before assault;
- how autoresolve differs from live battle results.

Tactical doctrine state is runtime-only. It is not written to the campaign sidecar. Telemetry may collect raw observations at fire cadence, but doctrine decision objects must be written only on a throttled tactical coordinator cadence or on verified AIBattle mode-transition surfaces, not inside `FireBullet` or reload Postfixes.

## B0 Tactical Weapons Observer

B0 is mandatory before behavior changes.

Observer logs should be bounded and config-gated. They should not write tactical state into campaign persistence.

Required event families:

- `[WeaponFire:first]`: `OnceLog` first-fire marker per patched surface.
- `[WeaponFireBucket]`: aggregate wall-clock or game-time bucket, not one line per shot. Include side, regiment count, weapon class, weapon id, range band, target type, ammo slot, combat behavior, projectile type, smoke type, shots attempted, shots fired, and ammo delta.
- `[AmmoState]`: sampled or bucketed group/regiment ammo, supply in stock, low/out-of-ammo state, projected volleys or minutes remaining, and whether the unit continues firing when empty.
- `[ReliefState]`: sampled line/reserve state, ammo pressure, reserve availability, recent fire/contact, relief request, relief success/failure, fallback target, and return-to-line status.
- `[ArtilleryAmmoBucket]`: aggregate solid/shell/canister pool, selected projectile counts, combat behavior 7/8/9, target cover, target artillery flag, distance fraction, and bombardment cancellation.
- `[ProjectileOutcomeBucket]`: sampled projectile outcome bucket with projectile type, range band, hit count, building hit, casualty amount, and target cover/formation when available.
- `[SmokeContactBucket]`: repeated firing volume in a local area, smoke animation type, recent-contact confidence, and target reacquisition delay if discoverable.
- `[AutocalcWeapon]`: autoresolve ROF, fighting distance, firepower band, ammo pressure, and casualties for comparison.

Rate-limit rules:

- Do not log one line per `FireBullet` call in normal observer mode.
- Use per-regiment cooldown for detailed `[WeaponFire]` samples, default no more than one sample per regiment per 30 in-game seconds.
- Use 1-in-K projectile sampling for projectile outcomes, with K configurable and defaulting to an aggressive reduction such as 25.
- Emit aggregate bucket lines on a bounded interval, default no more than once every 30 wall-clock seconds or 60 in-game seconds per side.
- `Verbose Tactical Weapon Logging` may lower these limits for a focused smoke run only; default observer mode must be safe for large W&L battles.

Observer acceptance criteria:

- A fresh W&L battle emits first-fire markers for infantry and artillery fire without log spam.
- At least one bucketed battle log shows weapon id/class, ammo delta, projectile type, target class, and range band.
- Observer overhead does not materially change battle performance.
- Logs distinguish live battle from autoresolve.
- Logs prove whether empty-ammo fire and artillery empty-pool fallback happen in normal play.
- Log volume remains bounded in a large battle; target maximum is single-digit lines per minute per event family unless verbose logging is explicitly enabled.

## Ammunition Doctrine

### Small-Arms Ammunition

Desired doctrine:

- Treat full ammo as local freedom to fire.
- Treat low ammo as a tactical constraint: conserve at long range, prefer close/decisive fire, avoid skirmisher overuse, and request relief or resupply.
- Treat empty ammo as a hard or near-hard fire stop for most small-arms units unless vanilla requires a minimal fallback to avoid AI deadlock.
- Repeaters and breechloaders should create faster ammunition pressure than muzzle-loaders.
- Skirmishers should conserve ammunition when screening, then increase fire only when delaying, covering withdrawal, or exploiting exposed targets.

Candidate behavior surfaces:

- `Regiment.FireBullet(int)` for ammo consumption and hard-empty gating.
- `Regiment.UpdateFiringAndReloading()` for reload/ROF throttles by weapon family and ammo state.
- `Regiment.GetFireRange()` for fire-discipline range compression under low ammo, smoke, fatigue, or poor morale.
- `Regiment.SetAmmoVariable(int, float)` and battle supply redistribution for resupply behavior.
- `AIBattle` stance/charge/reserve surfaces from Slice B for deciding whether low-ammo units should hold, withdraw, or be relieved.

Rules to avoid:

- Do not make empty regiments useless in melee or movement.
- Do not create a permanent no-fire state if vanilla UI/AI cannot recover from it.
- Do not drain repeaters so aggressively that they become worse than muzzle-loaders in all contexts.
- Do not change weapon purchase/production in this spec; that belongs to campaign weapon-economy work unless required for battle parity.

### Artillery Ammunition

Desired doctrine:

- Track solid, shell, and canister as distinct tactical pools.
- Preserve canister for close defense unless the battery is safe, supplied, and specifically executing close support.
- Prefer shell/spherical case against troops in cover, troops at medium range, and bombardment targets.
- Prefer solid shot for long-range direct fire, counterbattery, and exposed target lines where appropriate.
- Stop or reduce bombardment when shell supply is low.
- Prevent empty artillery pools from selecting canister/shell as if ammunition remains.

Candidate behavior surfaces:

- `Regiment.FireBullet(int)` for projectile selection and ammo subtraction.
- `AIBattle.CheckCounterBatteryFire()` for counterbattery target priority and ammo conservation.
- `AIBattle.CheckAIBombardment()` for bombardment permission and shell conservation.
- `Regiment.UpdateAutoTargets()` for artillery target selection during counterbattery/bombardment.
- `BattleUnits.LevelAmmo()` and battle supply redistribution for battery resupply behavior; `LevelAmmo()` already levels artillery ammo per slot and is the safer candidate for separate-pool behavior.
- A dedicated resupply patch may write `ammo[0..2]` / `supplyinstock[1]` directly only inside a tightly scoped resupply surface with clamping and logging. Do not use `SetAmmoVariable(1, positiveAmount)` for separate-pool resupply because vanilla refills all three artillery pools together.

Rules to avoid:

- Do not leave batteries unable to defend themselves at close range because the doctrine over-conserved canister.
- Do not allow all artillery resupply to refill all shell types equally unless observer logs prove vanilla balance depends on it.
- Do not patch projectile physics before projectile telemetry proves a specific problem.

## Battle Time, Fallback, And Resupply Doctrine

Vanilla already has a strong historical abstraction: ammunition redistribution and supply occur during the end-of-day/night cycle, not continuously during a firefight. Whiskey should preserve that structure and add tactical choices around reaching it.

Design conclusion:

- Do not call `ResupplyAllUnits()` on a simple timer while units are under fire.
- Do not directly advance `uniStormSystem` time during active contact to force resupply.
- Do make relief and fallback the primary response to low ammunition.
- Do make cease-fire the army-level consequence of widespread exhaustion, not the first response.
- Do let vanilla end-of-day resupply handle full redistribution after a battle lull or night transition.

### Ammo Pressure States

Use a four-step state machine:

- `Adequate`: normal fire doctrine.
- `Low`: conserve poor-value fire, prefer close/decisive targets, and start checking reserve availability.
- `Critical`: request relief; hold fire except close defense, assault support, or high-value targets.
- `Exhausted`: no routine fire; apply morale/cohesion pressure; seek relief, fallback defense, or battle-lull escalation.

State transitions must include hysteresis so units do not flicker between line and reserve. A unit that just returned from replenishment should require a minimum time and meaningful ammo recovery before it can be selected for another relief cycle.

### Line Relief

Relief is the preferred mechanic when reserves exist. It creates tactical value for second lines and avoids magic ammunition.

Relief should be attempted before fallback-to-supply when:

- the spent regiment/brigade is `Critical` or `Exhausted`;
- a nearby reserve or second-line unit has better ammo, morale, and cohesion;
- the spent unit is not in melee, routing, charging, or pinned by close enemy threat;
- the reserve can path to the line without crossing through enemy close range or breaking another critical sector;
- commander quality, stance, and order-delay rules allow coordinated movement.

Relief sequence:

1. `ReliefRequested`: spent unit suppresses low-value fire and holds position.
2. `ReliefMovingForward`: reserve moves to the line through `BattleUnits.SetWaypoint(... useorderdelay: true ...)` or the safest equivalent order surface.
3. `ReliefPassage`: spent unit remains until the reserve is close enough to cover the position.
4. `RelievedToReserve`: spent unit falls back behind the line and enters recovery/replenishment checks.
5. `ReturnToLine`: recovered unit can be committed again only after a cooldown and meaningful ammo/cohesion recovery.

The first behavior implementation should avoid direct writes to vanilla `objectivechain.reservegroups` or `AssignReserveToOperationalGroup(...)` unless B0/B5 observer logs prove that reserve membership mutation is needed. Start with Whiskey-side relief state and vanilla-compatible movement orders.

### Local Fallback Resupply

Low-ammunition regiments need a tactical path short of full retreat when relief is unavailable or fails:

- `HoldFireConserve`: unit stays in line but suppresses low-value fire.
- `ReliefRequested`: unit waits for a reserve to pass forward.
- `FallbackToReserve`: unit pulls behind the firing line when relief or reserve space exists.
- `FallbackToSupply`: unit moves toward parent/top-supply-unit, entry-point, or safe rear area when ammunition is critical.
- `FallbackDefend`: unit takes a rear/covered defensive posture and fires only for close defense.
- `Replenishing`: unit is behind the line, not recently engaged, and can receive partial supply.
- `ReturnToLine`: unit can re-enter after partial resupply, restored morale, and command delay.

Eligibility should require:

- small-arms ammo or relevant artillery pool below threshold;
- no charge/assault/close-defense emergency;
- not routed, surrendering, or already in full retreat;
- supply line not interrupted;
- a usable parent/top-supply unit or entry-point direction;
- a reserve or adjacent unit can cover the gap when possible;
- no open objective or flank condition makes the move worse than staying.

Refill should require actual disengagement:

- no firing for a configurable in-game duration;
- no received-fire or close enemy contact for a configurable in-game duration;
- regiment is stopped or in a designated reserve/supply area;
- `supplystate` above a threshold;
- parent/top-supply stock can pay the refill.

Use `GameVars.currenttimefromstart` for timers. Since the unit is battle-hours, `30` in-game minutes is `0.5f`.

Resupply should be partial and slow:

- small arms refill only a fraction per successful supply interval;
- artillery refill respects solid/shell/canister pools separately in Whiskey code;
- refill amount scales by `supplystate`, commander administration, distance to supply, fatigue/disorder, and faction/era scarcity;
- emergency scavenging from casualties or local runners may provide a very small degraded recovery while holding, but must not replace proper relief or end-of-day resupply;
- failed fallback leaves the unit low on ammunition and more likely to withdraw or be relieved.

Avoid using vanilla full-battle withdrawal APIs as the first implementation surface. `SetWithdrawal(...)` is useful evidence for movement behavior, but it also touches battle mark state and can remove/alter battle monuments. Local ammo fallback should use tactical movement/order surfaces from the Slice B brain unless a later vanilla read proves a safer withdrawal-specific overload.

Recommended surface split:

- Deliberate brigade/regiment relief: `BattleUnits.SetWaypoint(...)` or a higher-level Slice B order helper that preserves order delay and W&L command friction.
- Emergency unit fallback: vanilla-style `RegimentSetPath(...)` modeled after `CheckLineFallbacks(...)`, with strict guards and cooldowns.
- Full retreat/withdrawal: `TimePanel.SetRetreatTimer(...)`, `BattleUnits.SetEndOfBattle(...)`, or `BattleUnits.SetWithdrawal(...)` only for battle-level withdrawal decisions, not ammo relief.

### Battle Lull And End-Of-Day Resupply

When both armies are exhausted, the better mechanic is not magic refill. It is a lull:

- both sides reduce fire when ammunition, fatigue, morale, and visibility are bad;
- cautious commanders become more willing to accept cease-fire;
- aggressive commanders press if local superiority, objective pressure, or enemy disorder is high;
- if cease-fire/end-of-day triggers, vanilla `CheckTimeIssues()` accelerates time and the existing `LevelAmmo()` + `ResupplyAllUnits()` night sequence runs.

Candidate triggers:

- one or both sides have many `Critical`/`Exhausted` frontline regiments;
- reserves are depleted, committed, or too disorganized to relieve the line;
- casualty and fatigue pressure are high;
- smoke/contact uncertainty is high;
- objective pressure is low or no decisive attack is underway;
- reinforcement arrival within 24 hours changes incentive to break off or continue;
- commander personality shifts the threshold.

This should be implemented as AI willingness and player-facing option pressure, not as a hidden forced pause. The player should still be able to attack into a tired, low-ammo enemy if they accept the risk.

Rules to avoid:

- Do not use battle lulls to erase an imminent rout.
- Do not let the AI cease-fire when it is clearly winning an active assault unless night, ammunition, or command personality justifies it.
- Do not resupply routed, surrendered, permanently detached, or supply-line-interrupted units.
- Do not grant full top-off supply during a short local fallback. Full redistribution remains an end-of-day/night behavior.
- Do not let relief loops commit the last reserve when the flank/security score says it must remain held back.

## Fire Range And Accuracy Doctrine

Vanilla weapon range should not be treated as effective kill range in all conditions.

Desired range bands:

- `PointBlank`: close volley/canister/melee-adjacent danger.
- `Close`: smoothbore and buck-and-ball remain dangerous; rifle-muskets are effective.
- `CommonBattle`: main line-fire band where smoke, command, morale, and fatigue decide effectiveness.
- `Long`: rifle-muskets and selected rifles can fire, but effectiveness should require good visibility, steadiness, and target density.
- `Extreme`: special use only: sharpshooters, artillery, or deliberate harassment; not routine AI line fire.

Doctrine modifiers:

- smoke reduces long-range effectiveness and contact confidence;
- fatigue reduces fire rate and accuracy;
- morale and experience modify fire discipline;
- formation and cover modify target vulnerability;
- commander competence modifies when to conserve or open fire;
- low ammunition suppresses low-value long-range fire;
- skirmishers and sharpshooters receive different rules than dense line infantry;
- repeaters/breechloaders receive higher fire volume but faster ammo pressure.

Candidate behavior surfaces:

- `Regiment.GetFireRange()` for effective fire range caps.
- `Regiment.FireBullet(int)` for accuracy and ammo gating.
- `Projectile.SetTargetNoise()` only if live telemetry shows range/accuracy tuning cannot be achieved before projectile initialization.
- Global `GamePrefs` overrides for crude floor/ceiling knobs only. Per-weapon-class doctrine must run through per-regiment logic keyed from `GameVars.weapon[weapon].wclass` or a Whiskey-side doctrine map.

## Smoke Doctrine

Vanilla smoke currently appears to be mostly visual, with `GamePrefs.smokefireprobability`, weapon `smoke`, and smoke duration in `battleprefs.txt`.

Desired doctrine:

- sustained black-powder firing creates a local visibility penalty;
- smoke reduces long-range target confidence before it reduces close-range fire;
- smoke-heavy sectors should slow assault commitment until scouts/contact confirm enemy condition;
- wind/weather may later modify smoke persistence if vanilla exposes stable inputs;
- smoke should affect both AI and player-facing realism consistently where feasible.

Candidate surfaces:

- B0 observer first: count local fire events and smoke animations by area.
- `TacticalContactLedger` from Slice B can consume smoke pressure as contact uncertainty after sector/contact ledger work lands.
- `Regiment.GetFireRange()` and `FireDisciplineDecision` can reduce fire at low-confidence long range.
- Projectile physics should not be patched for smoke unless range/accuracy surfaces are insufficient.

## Artillery Doctrine

Artillery behavior must be mission-aware:

- `DefensiveCloseSupport`: conserve canister, fire at close threats, displace/fallback if overrun.
- `CounterBattery`: prefer enemy guns when visible and worth the ammunition.
- `BombardStrongPoint`: use shell at covered/fortified troop targets before assault.
- `HarassingLongRange`: low-rate fire only when ammunition is ample and objective pressure justifies it.
- `CeaseFireConserve`: hold fire when ammunition is low, target value is poor, or friendly units mask fire.

Decision inputs:

- current combat behavior: `7` default/open artillery fire, `8` counterbattery, `9` bombardment;
- target type, cover, fortification, and artillery flag;
- range fraction relative to fire range;
- shell/canister/solid ammo levels;
- own battery condition, morale, and gun count;
- friendly infantry assault timing from Slice B sector doctrine;
- threat of close enemy approach.

Acceptance criteria:

- Batteries do not waste shell on low-value long-range bombardment while infantry is not preparing an assault.
- Batteries keep canister for close defense unless explicitly in close support.
- Counterbattery remains possible but not automatic when ammunition is critically low.
- Bombardment cancels or downgrades when shell supply falls below a configurable threshold.

## Autoresolve Parity

Any behavior change that affects live battle firepower must include an autoresolve review.

Candidate surfaces:

- `Autocalc.FightUnit()` for combat casualty and ammo behavior.
- `Autocalc.GetROF()` for stance/weather/weapon-class ROF effects.
- Autocalc firepower band selection for artillery and non-artillery weapons.

Parity rules:

- If small-arms empty ammo becomes hard or near-hard in live battle, autoresolve must not keep producing full firepower from empty units.
- If repeaters consume ammo faster in live battle, autoresolve must reflect their expenditure pressure.
- If artillery shell pools become distinct in live battle, autoresolve must not treat artillery ammunition as one unlimited generic pool.
- If smoke reduces live-battle long-range effectiveness, autoresolve should get a simplified equivalent or the divergence must be documented.

## Modding Surfaces

Vanilla weapon data is a modding surface, but Whiskey should not require users to edit the game install to use this doctrine.

Rules:

- Whiskey does not directly edit `Config/weapons.txt`, `Config/weaponclasses.dat`, or `Config/weapongroups.dat` in the game install.
- Whiskey reads vanilla-loaded weapon data through `GameVars.weapon[]`, `GameVars.weaponclass[]`, and `GameVars.weapongroup[]`.
- If a user or another mod introduces a new weapon through vanilla data, Whiskey doctrine falls through by `Weapon.wclass` first.
- Unknown weapon classes use conservative vanilla behavior plus telemetry until explicitly mapped.
- Buck-and-ball and other historical subtypes without vanilla flags are represented in a Whiskey-side doctrine map keyed by weapon class, caliber/year heuristics, and optional weapon-name overrides.
- A later implementation may add a Whiskey-side override file under BepInEx config, such as `dev.kyle.whiskey-realism.weapons.json`, for class and weapon-id doctrine tuning. That file must be optional, schema-versioned, and validated with fallback to defaults on parse errors.
- End-user override files may tune doctrine thresholds and class mappings; they must not be required for the base mod to run.

## Configuration And Compatibility

Add Whiskey config only after B0 proves which knobs are needed.

Global config entries:

- `Enable Tactical Weapon Observer` default `false`.
- `Enable Tactical Ammunition Doctrine` default `false` for first behavior build.
- `Enable Artillery Shell Doctrine` default `false` for first behavior build.
- `Enable Smoke Fire Discipline` default `false` until smoke telemetry proves stable.
- `Low Ammo Threshold Small Arms` default based on observed vanilla low-ammo marker.
- `Critical Ammo Threshold Small Arms`.
- `Canister Reserve Fraction`.
- `Bombardment Shell Reserve Fraction`.
- `Enable Tactical Fallback Resupply` default `false` for first behavior build.
- `Enable Tactical Line Relief` default `false` for first behavior build.
- `Enable Battle Lull Ceasefire Doctrine` default `false` until observer logs prove cease-fire/night behavior.
- `Low Ammo Relief Threshold`.
- `Critical Ammo Relief Threshold`.
- `Relief Minimum Reserve Ammo Advantage`.
- `Relief Minimum Reserve Morale Advantage`.
- `Relief Cooldown Minutes`.
- `Fallback Resupply Delay Minutes`.
- `Fallback Resupply Fraction Per Interval`.
- `Fallback Resupply Minimum SupplyState`.
- `Verbose Tactical Weapon Logging` default `false`.

Configuration tiers:

- Global `GamePrefs` overrides are allowed only for broad floor/ceiling behavior, such as smoke probability or low-ammo ROF. They cannot deliver smoothbore-vs-rifle-vs-breechloader-vs-repeater doctrine.
- Per-class doctrine belongs in Whiskey scoring keyed off `GameVars.weapon[weapon].wclass`.
- Per-weapon overrides belong in an optional Whiskey-side config file, not hardcoded scattered conditionals.

Compatibility rules:

- Existing user config overrides C# defaults after first plugin load. Do not rely on changing defaults alone for existing installs.
- Keep every behavior patch individually config-gated during early Slice B.
- Do not mutate strategic persisted state from tactical patches.
- Tactical telemetry buffers may write at fire cadence, but tactical doctrine decisions should update only on throttled tactical coordinator ticks or verified battle-mode transition hooks.
- Tactical doctrine state is in-memory only and is not written to `whiskeyrealism.json`.
- Decide at plan time whether tactical doctrine applies to all land battles or W&L-only. Default recommendation: observer applies to all land battles, behavior doctrine starts W&L-only until smoke-tested, then can be expanded behind config.
- Use bounded `OnceLog`-style logging for first-fire and anomaly markers.
- Wrap reflection failures in warnings and return to vanilla behavior.

## Implementation Boundaries

This spec should be decomposed into separate implementation plans:

1. `B0 Tactical Weapons Observer`: logs only.
2. `B2 Ammunition Doctrine`: small-arms ammo pressure, low/empty fire discipline, repeater expenditure.
3. `B3 Artillery Ammunition Doctrine`: shell selection, canister preservation, bombardment conservation.
4. `B4 Smoke And Effective Range`: ship a degraded per-regiment range/fire-discipline form if the Slice B sector/contact ledger is not available; full smoke-to-contact uncertainty waits for that ledger.
5. `B5 Line Relief, Fallback, And Resupply`: ammo pressure states, reserve relief, fallback defense, partial replenishment, and battle-lull escalation; no active-contact timer refill.
6. `B6 Autoresolve Weapon Parity`: align `Autocalc` with shipped live-battle doctrine.

`B1 W&L Feud And Charge Guard` from the umbrella tactical-brain work remains separate. Do not mix the W&L charge safety fix with ammunition doctrine.

Pure scoring code for `FireDisciplineDecision`, `ReliefAndFallbackDecision`, and `ArtilleryDoctrineDecision` must live outside Unity-dependent patch code and be testable through `tests/WhiskeyRealism.Tests/`.

## Acceptance Criteria For The Full Spec

- Observer logs prove real weapon/ammo behavior before behavior patches ship.
- Infantry weapon families behave differently in sustained battles, not only in paper stats.
- Low ammunition affects fire discipline and tactical decisions.
- Empty ammunition no longer behaves like unlimited fire unless a minimal vanilla fallback is explicitly required and documented.
- Artillery shell type selection responds to target, range, mission, and ammunition supply.
- Canister is preserved for close defense and does not appear from an empty pool.
- Smoke-heavy firefights reduce long-range confidence and reckless assault commitment.
- Live battle and autoresolve do not diverge in obvious ammunition/firepower outcomes.
- Every behavior change is config-gated, bounded, and reversible to vanilla behavior.
- No patch throws repeatedly or creates per-frame log spam.

## Open Questions For B0

- How often does vanilla fire when `ammo[]` is already zero?
- Does artillery empty-pool fallback happen in normal battles or only edge cases?
- Which weapon ids dominate W&L early-war battles by faction and theater?
- How fast do Sharps, Henry, and Spencer-equipped units actually drain ammo in live battle?
- Does low ammo currently change AI stance, reserve relief, or retreat behavior in practice?
- How much casualty production happens beyond 250 yards for rifle-muskets in live battle?
- Does smoke correlate with reduced contact/target acquisition anywhere in vanilla, or is it purely visual?
- Which artillery projectile type produces most casualties by range band?
- How different are live battle and autoresolve outcomes for the same weapon mix?
