Produced-by: claude-opus-5-5 (high)

# BC13 — Commander Opportunity and Exploitation (design spec)

**Status:** draft for review (gate: user approval of this spec, then the plan section). **Slice:** BC13 of the battle-core plan (`docs/superpowers/plans/2026-10-15-battle-core.md`). The user placed it after BC12; the plan's former BC13 (battle AI level of detail) becomes **BC14** and the former BC14 (formations, slots and lanes) becomes **BC15**. This spec does not edit the plan; § 13 lists what the plan owes. **Branch:** `feat/battle-next-bc13` from `feat/battle-next` once BC12 passes verify. **Vanilla:** decompile build 14703111 (`/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`); game data `Config/battleprefs.txt`, `Config/unitprefs.txt`, `Config/commanders.txt` (shipped install, read only). Anchors are method names first. Decompile line numbers are only there to help find the code.

**User decisions (2026-10-15, AskUserQuestion, verbatim):**
- Exploit slice: "Add it after BC12 (Recommended)".
- Who decides: "Each commander his own (Recommended)". Corps and division commanders use their own personality for local opportunities. The army commander sets the overall plan.
- Unlisted commanders: "Derive from game stats (Recommended)". The historical list overrides the derived values.
- Vanilla battle bugs: "Fix them (Recommended)". Each fix is tracked as a bug fix with tests and has its own rollback flag, default on.

## Outline

1. Purpose, advantage, measurement
2. Ground truth: what vanilla does, what Whiskey does today, and the three vanilla pieces (steer, extend or leave alone)
3. History as weights
4. (A) Personality plumbing: requirements
5. (B) Fog-honest opening detection: requirements
6. (C) Opportunity decision, exploit lifecycle and reserve release: requirements
7. (D) Vanilla battle bug fixes: one requirement per bug
8. Interfaces (pure types)
9. Roles, lifecycles, shared rows and conflict rules
10. Exceptions per step
11. Performance, configuration, telemetry
12. Fit criteria
13. (E) Tests, mutation plants, smoke items, coverage matrix (the frozen test plan)
14. Decisions and rejected alternatives
15. Amendments owed to the plan (renumbering, BC14 level of detail, BC15 formations)
16. Not verified (runtime-only claims)
17. Open questions
18. Out of scope

---

## 1. Purpose, advantage, measurement

**Purpose.** Today a Whiskey AI side has one personality, the army commander's, and nobody below the army notices a local weakness. A gap in the enemy line, a shaken enemy brigade, a flank in the air or an unsupported battery goes unexploited unless it shows up in the army-level sector odds. BC13 gives each corps and division commander his own temperament. Historical commanders keep the list's values; every other commander gets values derived from his game stats. It adds a fog-honest detector of local openings, and a per-commander decision that weighs the opening, the asset, the risk and the commander's temperament. That decision launches, runs and recalls a bounded exploitation through the existing posture pipeline and order executor. It also stops the army reserve release from committing every reserve at once, and it fixes the vanilla battle bugs found in the 2026-10-15 research.

**Advantage over today.** Personality becomes a real input at the echelon that historically exploited openings. Today's paths that ignore personality or only log it are wired in or deleted (requirement-dead-personality-paths). Cautious behaviour becomes reachable (criterion-cautious-band-reach). The AI gains the five opening kinds vanilla lacks, and stays fog-honest (criterion-opening-fog-honest). Vanilla's two existing opportunity behaviours (isolated clusters, cavalry charges) are extended or disciplined instead of duplicated (§ 2.3).

**Measurement.** Each claim has a `criterion-*` in § 12. Each criterion names its value, its basis, how it is measured, a guardrail and the place the running mod emits the number.

---

## 2. Ground truth

### 2.1 Vanilla (build 14703111)

1. **Commander data.** `GamePrefs.Commander` (class at 58926) has `exp`, `fame`, `leadership`, `initiative`, `administration`, `cunning` (each a file value / 100), `veteran`, `westpoint` and `political`, loaded by `Commander.Load` (59701; field order name, firstname, combinedname, alliance, exp, fame, leadership, initiative, administration, cunning, veteran, westpoint, …, `political` just before the service history at 59965). The live getters `GetCommanderInitiative` / `GetCommanderLeadership` / `GetCommanderCunning` / `GetCommanderAdministration` (61134-61233) return `clamp01(stat × outOfDuty × experienceFactor × parentInfluence + DLC_WL staff influence)`. `outOfDuty` = `GamePrefs.reductionfactorcommanderoutofduty` when `status != 0` (`unitprefs.txt` "Reduction factor of commander abilities … if commander is out of duty (kia, wia, missed)", shipped 0.5). There is no aggression or temperament field. The W&L personality constants (`personality_heroism`, `personality_stresscontrol`, 43010-43019) belong to the player avatar.
2. **A commander hit in battle** changes `Commander.status` (2 captured, 3 wounded, 4 killed: the service-history branch ending in `TriggerCommanderHitMessage`, 126845-126865). `ReplaceCommanderOfUnit` (81389) runs only from campaign paths (for example `UpdateCommanderHistories`, guarded by `!campaignGroup.inbattle`, 80022-80027). So during a battle a group keeps its `commander` index, and its live stats drop by the out-of-duty factor.
3. **Commander stats in battle decisions.** Only initiative enters vanilla's battle AI:
   - `CheckGlobalAIStrategy` (6314) divides its defend, attack and assault force-balance triggers by `1 + initiative × 0.7` (`battleprefs` 649-658) and shortens the macro stance-change time by `1 − 0.7 × initiative` (667-670).
   - `CheckForFeudGroupActions` (4931) uses initiative².
   - `GetVictoryTrigger` uses initiative (`battleprefs` 714-719).
   - The fog estimate noise uses initiative and cunning.

   `CheckFlankMoves`, `AssignReserves`, `AdjustGroupAIStance`, `AdjustCavalryBehavior`, `Regiment.UpdateChargeTarget` and `CheckLineFallbacks` read no commander stat.
4. **Anchored flanks.** `CheckIfFlanksAreAnchored` (6599) marks a chain flank anchored when either of two tests passes:
   - A river cell (terrain id 4) lies within `distancetocheckforanchoredflanksriver` perpendicular to the flank (`CheckTerrainLine`; `battleprefs` 643-644, 80 m).
   - A terrain peak lies within `distancetocheckforanchoredflankhills` (645-646, 50 m).

   This applies to the AI's own lines only. Vanilla has no test of the enemy's flanks.
5. **Isolated enemy clusters.** `CheckForSeparatedEnemy` (8056-8215, micro cycle 26) runs over a heat map, `UpdateAIHotspotUnits` (7975) / `UpdateAIHotspotList` (8006). The map is not fog-honest. It includes every enemy unit, and inactive reinforcements due within 12 h appear at their entry points (7991-7997). Routed units are left out of the map (control: the `UpdateAIHotspotUnits` filter `!…isrouted` at 7999, reads 1, under: decompile; failure-under: decompile).

   A cluster qualifies when both hold:
   - the nearest other cluster is more than 1.3 × the group's distance to it away;
   - its value (strength × morale × (1 + cover) / 1e5) is at most 0.65 × the group's strength × morale (8155).

   The routine returns early when any cluster is within 250 m (8160). It considers only the nearest cluster, and only idle, unlinked, out-of-combat groups that are not on a strategy chain, with `macroai != 3`. The group goes at once (`SetWaypoint(useorderdelay: false)`, 8188), and the routine allocates a `new List` on every call (8133).
6. **Cavalry charges.** In micro cycle 7, `AdjustCavalryBehavior` (5036-5077) checks a cavalry regiment with no charge target, not routed, not in group stance 1 or 2, in movement mode 0 and not evading. It also needs `morale > min(1, moraletriggerforfallbackifenemyclose[stance] × 1.2)`, which is 0.72 at stance 3 and 0.60 at stance 4 (`battleprefs` 748-753). For such a regiment it takes `GetClosestEnemyUnitReg(maxdistancecavalryattack)` (90 m, `battleprefs` 880-881). The target must be artillery, or a non-routed unit with `morale < maxenemymoraleforcavalrychargenonarty`: shipped 0.45 at `battleprefs` 882-883, labelled "Minimum" but used as a maximum, with code default 0.7 at 49128.

   The regiment then calls `AssignChargeTarget` → `SetMovementMode(6)` (126024-126053). That skips the odds test `UpdateChargeTarget` applies in mode 3 (122226-122259). `GetClosestEnemyUnitReg` (123723) reads the regiment's range list, which has no fog test for the AI side.
7. **Feud moves.** `CheckForFeudGroupActions` (4931-4960) moves a group whose commander is in a feud (`ai_feudstance >= 0`, or the feud instance) at the closest enemy within 250 m (`battleprefs` 786-787). The chance is `initiative² × probfeudgroupmovement`, multiplied for political commanders and for volunteer commanders (neither West Point nor political). There is no weakness test and no risk test: this is insubordination, not opportunity. Whiskey's #42 replays and gates it.
8. **Reserves.** `AssignReserves` (7017) extends a weak, unanchored flank with a reserve (triggers at `battleprefs` 659-666), or does so on a 50 % roll when a chain has more than one reserve. `CheckUseOfReserves` (6062) only sends a regiment to an outflanked neighbour. `CheckIfOutflanked` (6188), a retreat when outflanked on both flanks gated on `debug_usedefensivefallback`, has no caller.
9. **Bug sites** (each is analysed in § 7): `AdjustGroupAIStance` 4264 (inverted cooldown); `MicroAICheckForCharges` 4917 (inverted cooldown, inside a dead method); `CheckGlobalAIStrategy` 6432-6434 (wrong variable); `AssignReserves` 7092 (`Random.Range(1, 2)`); `UpdateChargeTarget` 122229-122230 (strength weight cancels); `AdjustCavalryBehavior` 5050 (label mismatch); `nomicroaichargeifunitisoutflanked` (loaded at 54784, never read by vanilla).

### 2.2 Whiskey today (`feat/battle-next` after BC12)

- **One personality per side.**
  - `TacticalBattleCoordinatorRuntime.DiscoverCommandersFromVanilla` builds roster inputs for the two army commanders only.
  - `TacticalCommanderRoster.FromVanilla` (`TacticalCommanderRosterRuntime.cs`) uses the registry vector when a full name matches. Otherwise it uses `ApplyRankBias(FactionProfiles.For(alliance), echelon)` and discards the vector `HistoricalFigureRegistry.Resolve` derived from `westpoint` / `political` / `fame`.
  - Faction defaults (`FactionProfiles`): Union aggression 0, CSA +0.2, which is `agg01` 0.50 and 0.60 (`agg01 = (Aggression + 1) / 2`, `ArmyTickCycle`, `ArmyPostureInputsRuntime`).
- **Personality inputs.**
  - Live consumers of `agg01`: playbook fit, envelopment, the reinforcement-opportunity thresholds, phase budgets, sector readiness, the allocator and role cascade branches, arrival sync and the director.
  - `Caution` is read only by the playbook score and `TacticalOperationDirector`. `Audacity` is read only by the playbook score.
  - `CasualtyTolerance` and `PreserveForceBias` are telemetry only (`BuildStrategicBattleIntentSnapshot`, `TacticalOperationsTelemetry`). `PoliticalResponsiveness` has no battle reader.
- **Dead or constant inputs.**
  - `DirectChildIntent.AggressionBias01` / `CommandNodeIntent.AggressionBias01` copy the army value to every node. They are carried and hashed but never decided on.
  - `TacticalCommanderIntentInput.WeakPointConfirmed` is always `false` (`BattleCommanderIntentObserverPatch`), so its branch in `TacticalCommanderIntentResolver` is dead.
  - `TacticalOrderFriction` gets `commanderInitiative01: 0.50f` (`TacticalObserverPatch`).
- **Attack-threshold cap.** `TacticalReinforcementOpportunityDoctrine.ComputeAttackThreshold` = 1.20 × (1.30 − 0.40 × agg01), which ranges from 1.56 to 1.08. `EffectiveAttackThreshold` caps it at 1.0 for Meeting/Attacker, so personality has no effect on those stances. A Defender keeps the uncapped value but never gets `AttackNow`.
- **Weakness detection today.**
  - The army's decisive band is the best `EnemyVisibleState.SectorOdds`.
  - `TacticalDoctrineScorer.ShouldCommitVisibleWeakPoint` needs confidence ≥ 0.75 and odds ≥ 1.75.
  - The charge doctrine checks target morale, disorder and flanking (`ChargeDoctrine`).
  - The cavalry raid filter is in `PostureIntentProducer.TryCavalryFollow`: the visible target is not in a fort or square and has no visible infantry or artillery within `PostureSense.CloseContactMeters` = 250 m.

  There is no gap, shaken-sector, flank-in-the-air, unsupported-battery or isolated-cluster detector, and no way to detach an asset, strike and recall it.
- **Reserve release.** In `DirectChildAllocator.ReleaseReserves`, the plan's Exploit phase or the reserve-commit odds turn **every** Reserve child into `SupportMain` at once. A crisis turns every Reserve child adjacent to it into `Fix`. There is no floor.
- **Charges.** `ChargeDoctrine` already honours `nomicroaichargeifunitisoutflanked` (`OutflankedBarsCharge`), and it applies vanilla's intended renew back-off (`RefusalBackoffSeconds`, from `timetorenewaichargecheck`) in place of the inverted 4917 test. That test is in `MicroAICheckForCharges`, which never runs: it compares a `GameObject` with a `Regiment` at 4910 (BUG-TAC-016).
- **Stance hold.** `TacticalStanceOwnership` makes vanilla honour a Whiskey stance hold through the inverted 4264 comparison. It writes `lastaistancechangetime` into the future (`VanillaHoldStamp` = now + hold + reset), `VanillaHoldLive` tests the inverted form, and `ReleaseStamp` re-arms vanilla.
- **The posture producer's reach.** The producer drives AI-pool groups (`ArmyPostureInputsRuntime.InAiPool`) through `PostureLedger.TryResolveDoctrineOrder` (the ledger's `CurrentDoctrineOrders`), and leaf brigades through `CurrentLeafBrigadeMap`. BC11's maneuvers reach the ledger orders through `CommandDoctrineAssignment.Build(…, ManeuverNodeTable)`.

### 2.3 The three vanilla pieces: steer, extend or leave alone

| Vanilla piece | What it does | BC13 |
|---|---|---|
| `CheckForSeparatedEnemy` (8056-8215) | Attacks the nearest isolated cluster: value ≤ 0.65 × own, its neighbour more than 1.3 × away, only from ≥ 250 m, idle groups only. Omniscient heat map, reinforcements included | **Extend** its rule into Whiskey's fog-honest `IsolatedCluster` opening, with vanilla's 0.65 and 1.3 constants (requirement-detect-isolated-cluster). **Leave alone** for groups vanilla owns. For Whiskey-owned groups, a steer that keeps vanilla from also sending them would in effect skip vanilla per group, so it is **question-separated-enemy-steer** (default: leave alone; the posture pipeline keeps re-asserting Whiskey's order, as today) |
| `AdjustCavalryBehavior` (5036-5077) | Charges artillery, or a unit with morale below 0.45, if it is the nearest enemy within 90 m. Through `SetMovementMode(6)`: no odds, support or fog test | **Extend:** Whiskey's commanders choose and position the cavalry (requirement-asset-choice), and vanilla's cycle-7 charge stays the executor of the charge itself. **Steer:** a result filter on the target vanilla picks (requirement-fix-cavalry-strike) adds the fog, support, odds and outflank tests |
| `CheckForFeudGroupActions` (4931-4960) | Feud (W&L) groups advance on the closest enemy within 250 m with chance initiative² × prob, scaled for political and volunteer commanders. Insubordination, no weakness or risk test | **Leave alone.** It stays W&L's own mechanic, gated by #42. An exploit never selects a `Feud`-flagged asset (requirement-asset-eligibility). The opportunity decision does not read feud state, and #42 does not read exploits |

---

## 3. History as weights

These patterns set the weights in § 6. They are not scripts. Each line quotes its source (all accessed 2026-10-15).

| Pattern | Source and quote | Weight it sets |
|---|---|---|
| A gap the width of a division, exploited at once by a massed infantry column | battlefields.org, Chickamauga: "immediately moves, creating a division-wide hole in the Union line. This is the chance the Confederates need. Longstreet masses a striking force, led by Gen. Hood, of eight brigades divided into three lines." | `Gap` prefers an infantry push (asset weight 1.0) over cavalry (0.3); the gap must fit the asset |
| Exploitation is limited; a defender rallies on good ground | same page: "begins consolidating the scattered Union forces on Horseshoe Ridge and Snodgrass Hill … the Federals hold firm." | Bounded reach, timeout and recall; no open-ended exploitation |
| Gap width in the record | NPS, Fredericksburg (Prospect Hill): "Jackson had allowed a 600-yard gap to exist along his front and Meade's troops accidentally discovered it." | A gap of a few hundred metres is the scale (`GapMinMeters` 150 is the floor); a gap is found by seeing it (unwatched ground is never a gap) |
| A defender's reserves restore a broken line | same page: "Jackson … calmly directed his vast reserves to move forward and restore the line." | Basis for the Defender local counterstroke (question-defender-counterstroke) |
| An attacker that does not feed an opening loses it | Encyclopedia Virginia, Fredericksburg: "he failed to put enough force into Meade's attack to support or exploit Meade's extraordinary advance." | The risk check allows a whole brigade or division, not a token detachment |
| A flank "in the air" | NPS, Chancellorsville history: "The Union right flank was "in the air" -- that is, resting on no natural or artificial obstacle!" and "Only two small regiments and half a New York battery faced west" | `FlankInAir` = no river or crest anchor and no refused end; infantry asset weight 1.0 |
| Flank attacks stall at dark and in disorder | NPS, Chancellorsville flank attack: "Sunset and the inevitable intermingling of "Stonewall's" brigades compelled Jackson to call a reluctant halt" | Exploit timeout and consolidate; no pursuit by infantry |
| A defender's local counterattack buys time | npshistory.com (NPS Civil War Series, Gettysburg): "Hancock led Col. George L. Willard's brigade of his Second Corps down to meet Barksdale's brigade … drove it back" and "The Minnesotans charged, struck the tired Alabamians, and blunted their attack." | Basis for the Defender local counterstroke (question-defender-counterstroke) |
| Unsupported guns are taken | NPS, First Manassas: "Despite their protestations of this unsupported movement … Griffin and Ricketts' eleven (11) guns moved forward." and npshistory.com: "Volley after volley crashed into them, and they finally fell back, leaving the cannon unprotected. Just now, Jeb Stuart's Virginia cavalrymen came on the scene, putting the retreating zouaves to rout" | `UnsupportedBattery` and `ShakenSector` prefer cavalry (asset weight 1.0) |
| Cavalry against formed infantry fails | historynet.com (Griffith, *Battle Tactics of the Civil War*): "perhaps 1,400 Union cavalrymen … took part in charges on Rebel infantry … suffering 365 casualties approximately a quarter of their strength." Emerging Civil War (Backus): "When infantrymen were not ready to receive an assault, a small cavalry charge could be temporarily successful" | Cavalry never targets formed steady infantry: artillery, shaken (morale below the bar) or routed only, or a flank in the air with audacity01 ≥ 0.6 |
| Dismounted cavalry delays; it does not strike | battlefields.org, McPherson's Ridge: "Buford knew he could not beat an entire Confederate infantry division, but he could slow them down" | Cavalry is not an exploit asset against a `Gap` in a formed line (weight 0.3). BC9 screening stays the cavalry's main job |
| Keep a final reserve | Jomini, *The Art of War* (1862 tr.), Project Gutenberg: "it is always a good plan to keep back, as a final reserve, two or three battalions and five or six squadrons." | Reserve floor (requirement-reserve-floor, requirement-staggered-reserve-release) |
| Pursuit was rare | NPS, First Manassas: "too disorganized to follow up on their success." strategypage.com (Nofi on Frey, *Failure to Pursue*): "the lack of fresh troops (particularly cavalry), poor staff work, difficult communications" | Only cavalry pursues, a short way (`PursuitMeters` 400) and only with audacity01 ≥ 0.6 |

URLs: battlefields.org/learn/civil-war/battles/chickamauga; nps.gov/frsp/learn/historyculture/hist-fburg-prospect.htm; encyclopediavirginia.org/entries/fredericksburg-battle-of/; home.nps.gov/frsp/learn/historyculture/chist.htm; nps.gov/frsp/learn/historyculture/cvillehist-flank.htm; npshistory.com/publications/civil_war_series/16/sec10.htm; nps.gov/mana/learn/historyculture/first-manassas.htm; npshistory.com/publications/civil_war_series/17/sec6.htm; historynet.com/civil-war-cavalry-missed-opportunities/; emergingcivilwar.com/2016/09/12/charge-on-re-examining-civil-war-cavalry-tactics/; battlefields.org/learn/maps/gettysburg-mcphersons-ridge-july-1-1863-930am-1130am; gutenberg.org/cache/epub/13549/pg13549.txt; strategypage.com/bookreviews/1516. The Scourge of War SDK is not installed. Its division think loop (`docs/scourge-of-war-ai-anchors.md` § Division think loop) already sends orders only to brigades with no orders, which is what `requirement-asset-eligibility` asks of an asset.

---

## 4. (A) Personality plumbing

- **requirement-commander-table** — `CommanderPersonalityTable` (pure; one per side; per-battle state registered with `BattleScope`) maps a vanilla commander index (`UnitRecord.Commander`, captured by `BattleSnapshotBuilder.Capture`) to a `PersonalityVector` and a `PersonalitySource` (`Registry`, `Derived`, `Default`).
  - **Create:** the coordinator's battle start, over every own group record (`unittyp >= 14`). New indices are added on a snapshot roster change (reinforcements).
  - **Change:** none during a battle. The vector is a trait and is **pinned** for the battle; capability changes reach decisions through requirement-commander-duty.
  - **Delete:** `BattleScope` reset at battle end, and coordinator restart.
  - **Writer:** the coordinator tick only, never a patch.
  - **Readers:** the opportunity producer, the army orchestrator (its own commander's entry), the friction observer, #44's army-off fallback.

  `DiscoverCommandersFromVanilla` keeps building the army roster for the `[TacticalCommanderRoster]` lines, and the army vector is read from the table.
- **requirement-registry-first** — Resolution order per commander:
  1. `HistoricalFigureRegistry` full-name match (`TryMatch`, unchanged): `Registry`, the list's vector as is (no rank bias, as today).
  2. Else stats readable: `Derived`, requirement-derive-from-stats plus `ApplyRankBias(vector, echelon)`.
  3. Else (index < 0, out of range, or a reflection failure): `Default`, `ApplyRankBias(FactionProfiles.For(alliance), echelon)`, and one bounded warning.

  `FromVanilla` stops discarding the derived vector. `HistoricalFigureRegistry.Derive` (westpoint/political/fame) is replaced by the derivation below and deleted.
- **requirement-derive-from-stats** — `CommanderPersonalityDerivation.Derive(in CommanderStats)` (pure) reads the **raw** fields `initiative`, `cunning`, `leadership`, `administration` (each 0-1), `veteran` and `political`. It does not read the live getters, which fold in experience, parent influence, W&L staff influence and out-of-duty status (§ 2.1 item 1), so a temperament would otherwise change with a staff perk. Each output is clamped to [−1, 1]:

  | Component | Formula | Per-stat justification |
  |---|---|---|
  | Aggression | `3.0 × (initiative − 0.45)` | Initiative is the only stat vanilla's battle AI uses for aggressiveness: it lowers the attack and assault triggers and speeds stance changes (§ 2.1 item 3). 0.45 is the roster centre (shipped `commanders.txt`: Union mean 0.447, CSA mean 0.496). The slope makes `agg01 = 0.5 + 1.5 × (initiative − 0.45)` |
  | Caution | `2.0 × (0.45 − initiative) + 1.0 × (administration − 0.55) + 0.3 × political` | Low initiative is vanilla's hesitant commander. High administration with low initiative is the methodical type (roster exemplars: McClellan 0.20 / 0.90, Huger 0.25 / 0.55). The political +0.3 is kept from the replaced `Derive` |
  | Audacity | `2.5 × (cunning − 0.38) + 1.0 × (initiative − 0.45)` | Cunning is vanilla's deception stat (it widens the enemy's estimate noise); the unexpected stroke needs it and initiative. 0.38 is about the roster cunning median (Union 0.35, CSA 0.40) |
  | CasualtyTolerance | `2.0 × (leadership − 0.55) + 0.2 × veteran` | Leadership is vanilla's morale and rally stat (Whiskey's withdrawal ladder already reads it, `TroopQualityRuntime`). A commander whose men stand under loss accepts loss. Veterans have seen it |
  | PoliticalResponsiveness | `0.4 × political` | Kept from the replaced `Derive`; no battle reader (requirement-dead-personality-paths) |

  No faction vector is added. The stats already carry the alliance difference (initiative mean Union 0.447, CSA 0.496), and adding `FactionProfiles` on top would count it twice.
- **requirement-cautious-reachable** — The constants of requirement-derive-from-stats are calibrated on the shipped roster so that every existing personality branch is reachable (criterion-cautious-band-reach). Measured on `Config/commanders.txt` (1,906 records; derivation before rank bias):
  - `agg01 < 0.30` (the role cascade's cautious branch): Union 17 %, CSA 10 %.
  - `agg01 >= 0.70` (the allocator and arrival-sync bypasses): Union 19 %, CSA 28 %.
  - Mean `agg01`: Union 0.49, CSA 0.56, against today's faction defaults 0.50 and 0.60.
- **requirement-commander-duty** — When the commander of a deciding node has `status != 0`, that node's exploit chance (requirement-exploit-chance) is multiplied by `GamePrefs.reductionfactorcommanderoutofduty` (shipped 0.5). Vanilla already scales the stats of a hit commander by this factor (§ 2.1 items 1-2). The vector itself stays pinned. `status` is read live per decision, through the runtime adapter, into the pure input.
- **requirement-army-vector-from-table** — `ArmyOrchestrator`'s commander personality is the table entry of the side's commanding officer (`GetCommandingOfficerFromSide`, the commander `CheckGlobalAIStrategy` reads at 6316). An unlisted army commander therefore gets `Derived` instead of the faction default. Every existing army-level `agg01` consumer is unchanged in code; its input changes. #44's army-off fallback (`BattleMacroStrategyPatch.CommanderAggression01`) reads the same entry, so army-on and army-off agree.
- **requirement-attack-threshold-band** — `EffectiveAttackThreshold(agg01, stance)` for Meeting/Attacker becomes `1.0 + 0.4 × (0.5 − agg01)`: 1.20 at agg01 0, 1.00 at 0.5 (today's capped value), 0.80 at 1.0. The cap at 1.0 is removed. A Defender keeps `ComputeAttackThreshold`. Vanilla's initiative divisor (the 4-argument overload, BC8) still applies afterwards. For a `Derived` commander, initiative therefore enters twice: once through `agg01`, once through the divisor (§ 14 D-A3). The resulting range is about [0.51, 1.01] against vanilla's [0.51, 0.70] for initiative 1.0 down to 0.2.
- **requirement-personality-live-inputs** — Caution, Audacity and CasualtyTolerance become decision inputs:
  - Caution sets the opening confidence threshold (requirement-exploit-thresholds) and the reserve floor share (requirement-reserve-floor).
  - Audacity sets exploit reach, cavalry flank use, pursuit (requirement-exploit-reach, requirement-asset-choice, requirement-exploit-lifecycle) and the chance factor (requirement-exploit-chance).
  - CasualtyTolerance sets the floor share and the loss abort, and it replaces aggression in `TacticalReinforcementOpportunityDoctrine.ComputeCasualtyEscalationThreshold`, which becomes `0.05 + 0.10 × casualtyTolerance01`. The value at 0.5 is unchanged, and the function's reader keeps the army commander's entry.
- **requirement-dead-personality-paths** — Each dead or telemetry-only path is wired in or deleted:
  - `DirectChildIntent.AggressionBias01`, `CommandNodeIntent.AggressionBias01`, the intent-hash term (`TacticalBattleCoordinator`), `CommandIntentResolver` / `CommandTreeIntentAllocator` copies, and the `CommandNodeIntentSnapshot` field: **deleted**. A copy of the army value is not a per-node fact. The table is the one place the value is defined.
  - `TacticalCommanderIntentInput.WeakPointConfirmed` and its branch in `TacticalCommanderIntentResolver`: **deleted**. The legacy B6c observer is telemetry-only with the army orchestrator on.
  - `TacticalOrderFriction`'s `commanderInitiative01: 0.50f` (two call sites in `TacticalObserverPatch`): **wired** to the group's commander's live `GetCommanderInitiative()` (the value vanilla reads at 6396), through the table's runtime accessor.
  - `StrategicBattleIntentSnapshot.CasualtyTolerance` / `PreserveForceBias`: **kept** as telemetry. They now report the same values that drive decisions.
  - `PoliticalResponsiveness`: no battle reader, by design (a campaign trait).
  - `DifficultyPersonalityModifier`: campaign only (out of scope).

---

## 5. (B) Fog-honest opening detection

All detection is pure (`OpeningDetector`) over the side's `BattleSnapshot` and its front axis (`BattleSnapshot.FrontAxis`, fit by the builder every build). It also reads public terrain: the BC10 `BattleTerrainGrid` (river id 4, frozen state) and the BC12 `TerrainPicture` (crest prominence; when the picture is not ready, the crest test is skipped and only the river and refused-end tests anchor a flank). It runs once per arbiter tick per AI side, inside the opportunity producer (requirement-opportunity-producer).

- **requirement-enemy-samples** — The detector's enemy inputs are, and are only:
  - observations that are `Visible`, or `Remembered` with `Confidence01 >= 0.5` (the BC11/BC13 threshold);
  - not `IsReinforcement`;
  - of type infantry, cavalry or artillery (`TacticalUnitType` 0-2).

  A `Visible` observation's own `EnemyRecords` row may be read (the BC6b precedent) for `UnitWidth`, `Flags.Routed`, `Flags.Garrisoned`, `Formation` and `Morale`. A `Remembered` sample uses the morale, heading and position stored at last sight, and its width falls back to `DefaultEnemyFrontageMeters` = 100 m. No other `EnemyRecords` read is allowed (source pin, test DET-PIN).
- **requirement-watched-ground** — An opening that asserts the **absence** of enemy (a gap, a flank in the air, an unsupported battery) is emitted only when the ground it asserts is watched. The contact ledger's coverage recency over that ground (new read accessor `TacticalEnemyContactLedger.RecencyOver(x0, z0, x1, z1, nowHours)`, the existing private `BandRecency` over the 25 m cells in the ledger's own frame) must be ≥ `WatchedRecency` = 0.5. Otherwise the opening is not emitted, and it is counted as `suppressedUnwatched` on the telemetry row. The opening's confidence is that recency multiplied by the samples' confidences.
- **requirement-detect-gap** — The detector projects the samples on the axis (lateral L, depth D) and sorts them by L in place. For each pair of adjacent front-line samples (a, b) with `|Da − Db| <= LineDepthBandMeters` = 200 m it computes `edgeGap = (Lb − Wb/2) − (La + Wa/2)`. It emits a `Gap` when all of these hold:
  - `edgeGap >= GapMinMeters` = 150 m (§ 3: a few hundred metres is the historical scale; vanilla's own row spacing is at most 10 m, so 150 m is never formation spacing);
  - no enemy sample of any type lies in the gap corridor (lateral strictly between the edges, depth from `min(Da, Db) − 50` to `max(Da, Db) + GapDepthMeters` = 300 m; a second line behind the gap closes it);
  - the corridor is watched (requirement-watched-ground).

  The aim point is the gap centre at depth `max(Da, Db) + 100`. The width is `edgeGap`.
- **requirement-detect-shaken** — For each front sector (`FrontAxis.SectorOf`, five bands) the detector computes three strengths:
  - the visible enemy strength `S`;
  - the shaken strength: visible samples with `Morale < ShakenMorale`, where `ShakenMorale` = `GamePrefs.maxenemymoraleforcavalrychargenonarty`, vanilla's own cavalry-target bar (shipped 0.45);
  - the routed strength: visible samples whose row has `Flags.Routed`.

  It emits a `ShakenSector` when `(shaken + routed) / S >= ShakenShareMin` = 0.4 and at least one such unit is not routed or is within 300 m of a formed enemy (routed-only crowds far behind their line are a pursuit, not an opening). The aim point is the strength-weighted centroid of the shaken and routed samples. Confidence is the visible share of the sector's perceived strength (Visible full plus Remembered × confidence). Remembered morale is stale, so `Remembered` samples count in `S` only.
- **requirement-detect-flank-in-air** — For each end of the perceived enemy line (the outermost front-line sample on the left and on the right), the end is **anchored** if any of these holds:
  1. a river cell (BC10 grid id 4, the map not frozen) lies within `GamePrefs.distancetocheckforanchoredflanksriver` (80 m) beyond the end along the line's outward lateral (vanilla's `CheckIfFlanksAreAnchored` river test, applied to the enemy);
  2. a `TerrainPicture` crest cell (prominence ≥ 3 m, BC12) lies within `GamePrefs.distancetocheckforanchoredflankhills` (50 m) of the end (vanilla's peak test);
  3. the end is refused: its visible heading differs from the strength-weighted mean heading of the line samples within 400 m by at least 45°;
  4. any enemy sample, cavalry included, lies beyond the end within `FlankWatchMeters` = 300 m laterally and 300 m in depth.

  It emits `FlankInAir` for an end that is not anchored and whose ground beyond it (300 m outward) is watched. The aim point is `FlankOffsetMeters` = 150 m beyond the end at the end's depth. The heading is along the line toward its centre.
- **requirement-detect-unsupported-battery** — For each visible enemy artillery sample (`UnitTyp` 2, not `Garrisoned`), it emits `UnsupportedBattery` when no infantry or cavalry sample lies within `SupportMeters` = `PostureSense.CloseContactMeters` (250 m, the raid filter's support radius) and that ring's lateral span is watched. The aim point is the battery.
- **requirement-detect-isolated-cluster** — The detector clusters all enemy samples by single link at `ClusterLinkMeters` = 150 m, on a 60 m bucket grid (the BC10 `PerceivedFlanking` precedent; at most `MaxClusters` = 64). Per cluster it outputs the centroid, the value Σ strength × morale (`Remembered` × confidence) and the nearest other cluster's distance. The per-commander test applies vanilla's rule (§ 2.1 item 5):
  - cluster value ≤ 0.65 × the asset's strength × morale;
  - nearest-neighbour distance > 1.3 × the asset's distance to the cluster;
  - asset distance ≥ 250 m.

  Unlike vanilla, any qualifying cluster may be chosen, not only the nearest.
- **requirement-detector-geometry** — Geometry and measurement rules shared by every pass:
  - **Projection.** Lateral `L = FrontAxis.LateralOf(x, z)`; depth `D` = the position's projection on `FrontAxis.Forward` from the axis origin.
  - **Front line.** An **infantry** sample is front-line when no other infantry sample within ±300 m lateral is more than `LineDepthBandMeters` shallower. Cavalry and artillery are never line samples: they count only as corridor occupants (gap), screens (flank test 4) and support. The gap and flank passes use the front-line samples.
  - **Distances to a unit.** Distances that start "beyond an end" or "between edges" are measured from the unit's edge (`L ± W/2`). Support and odds radii are centre to centre (the shipped `TargetHasCloseSupport` r² test). Every "within" is inclusive (`<=`).
  - **Outward and heading.** "Outward" is `±FrontAxis.Right`, with the sign pointing away from the line's lateral centre. The refused-end mean is a strength-weighted circular mean over the front-line samples within 400 m, excluding the end itself. Remembered samples contribute their stored heading. An end with no such neighbour is not refused.
  - **Watched ground.** `RecencyOver(x0, z0, x1, z1, now)` projects both points on the ledger's own frame (`FrameHeading`), takes the existing `BandRecency` **mean** over the 25 m lateral cells between them (the ledger's convention), and ignores depth: the ledger keeps lateral stamps only. The watched span is:
    - for a gap, its edges;
    - for a flank, the end edge to 300 m outward;
    - for a battery, ±250 m around it.
  - **Sector.** `Opening.Sector` is the sector of the aim point.
- **requirement-opening-identity** — Openings are kept in a pooled array (`MaxOpenings` = 32 per side). A new detection matches the previous tick's opening of the same kind whose aim point is within 100 m. When they match, it keeps the id and `FirstSeenHours` and refreshes `LastSeenHours`. An opening is **confirmed** after `ConfirmHours` = 0.02 doctrine-h of continuous presence, which suppresses one-tick flicker. It is **lost** when it has not been seen for `LostHours` = 0.05 doctrine-h. All times are on the doctrine clock.
- **requirement-detect-penetration** (only if question-defender-counterstroke is approved) — For a Defender side only, it emits `Penetration` for an enemy sample that lies within the own line's lateral extent and at a depth behind the own front-line trace, and within `CounterstrokeMaxMeters` = 400 m of it. Own samples define the trace; no watched-ground test is needed, because the enemy is seen.

---

## 6. (C) Opportunity decision, exploit lifecycle, reserve release

- **requirement-opportunity-producer** — A new producer `OpportunityIntentProducer` (name `"Opportunity"`), added to `BattleProducerRegistry.Order` immediately **before** `"Posture"` and to `BattleCoreTick.CreateProducer`. Per AI side and arbiter tick it does five things:
  1. runs the detector;
  2. steps every live exploit (requirement-exploit-lifecycle);
  3. lets each deciding commander that is due decide (requirement-deciding-commanders);
  4. publishes the side's `ExploitTable`;
  5. submits **no** intents.

  The posture producer, which runs next in the same tick, executes each exploit as an override of the asset's doctrine order (requirement-exploit-override), so every vanilla write still goes through the order executor (§ 14 D-C1). It stands down when the arbiter stands down (`StandDown`: Commander Mode not Active, `macroai` 3, `state` < 5, the master switch), and its stand-down clears the table (requirement-exploit-lifecycle). Scope `tactical.producer.opportunity` (1.0 ms).
- **requirement-deciding-commanders** — The deciding nodes are a side's AI-controlled corps and division command nodes (D5 tree; `unittyp` 16 non-top and 15). When a side has neither, the army node decides for its direct brigade children. Rules:
  - A node that is `PlayerOwned`, or is the W&L player's command, never decides. Its opportunities are the player's.
  - A node decides when `now − lastDecision >= DecisionIntervalHours` = 0.05 doctrine-h (the executor's 180 battle-s recent-order window). The first decision is staggered by `(commanderIndex mod 4) × sidestatupdatecycle`.
  - Order within a tick: deepest node first (divisions before corps; the commander on the spot sees it first), then ascending instance id.
- **requirement-asset-eligibility** — An asset is a **direct child** of the deciding node (a division's brigades; a corps' divisions or attached brigades) that meets all of these:
  - driven by the posture producer (in the AI pool, or a leaf brigade with a leaf task);
  - no `PlayerOwned` / `AncestorOfPlayerCommand` / `Feud` flag on it or any descendant;
  - not routed and not in melee;
  - `GroupMorale >= AssetMoraleMin` = 0.6 (vanilla's minimum group morale for attacking maneuvers, `battleprefs` 639-640);
  - `LossFraction <= 0.3`;
  - settled: not active-moving and no pending order (`PostureSense.ActiveMove` / `PendingOrder`), and no executor Move for it within `OrderExecutor.RecentOrderBattleSeconds`;
  - no BC11 maneuver phase, BC9 recon mission or probe hold, main-arrival hold or live exploit on it, an ancestor or a descendant;
  - no Safety claim in the published arbiter table;
  - not in its post-exploit cooldown (`AssetCooldownHours` = 0.25 doctrine-h).

  A **cavalry asset** is a group whose regiments are all cavalry, with group morale above `min(1, GamePrefs.moraletriggerforfallbackifenemyclose[3] × 1.2)` (0.72 shipped). Below that, vanilla's cycle-7 gate (5047) would never charge at attack stance, and Whiskey never writes stance 4. An **infantry asset** is a group with infantry in its subtree.
- **requirement-asset-choice** — For each confirmed opening within the node's area (its aim point within reach, requirement-exploit-reach, of an eligible asset), the decision scores each eligible asset as `kindWeight(kind, assetType) × localOdds × confidence`. The weights come from § 3:

  | Kind | Infantry | Cavalry |
  |---|---|---|
  | Gap | 1.0 | 0.3 |
  | ShakenSector | 0.8 | 1.0 |
  | FlankInAir | 1.0 | 0.5, only when audacity01 ≥ 0.6 |
  | UnsupportedBattery | 0.6, only within 400 m | 1.0 |
  | IsolatedCluster | 1.0 | 0.6, only when the cluster's mean visible morale < `ShakenMorale` |
  | Penetration (counterstroke) | 1.0 | 0.0 |

  Cavalry never targets formed, steady infantry (§ 3). Its aim must be artillery, a shaken or routed sample, or a flank end. A `Gap` additionally needs `width >= 0.6 × asset GroupWidth`, so the asset fits through. The best (opening, asset) pair by score goes to the threshold and risk checks. On a failure, the next pair is tried, at most `MaxPairsPerDecision` = 4.
- **requirement-exploit-reach** — The pair's distance from the asset to the aim point must be ≤ `reach`:
  - infantry: `600 m × (0.75 + 0.5 × audacity01)` (450-750 m);
  - cavalry: `1200 m × (0.75 + 0.5 × audacity01)` (900-1500 m).
- **requirement-local-odds** — `localOdds` = the asset's `Strength × GroupMorale` divided by the perceived enemy strength within `OddsRadiusMeters` = 300 m of the aim point (Visible × morale, Remembered × confidence × last morale). It must be ≥ `requiredOdds = baseOdds(kind) × (1.2 − 0.4 × agg01)`. The base odds are: Gap 1.0, ShakenSector 0.8, FlankInAir 0.8, UnsupportedBattery 0.5, IsolatedCluster 1 / 0.65 (vanilla's own ratio), Penetration 0.8.
- **requirement-exploit-thresholds** — The opening's confidence must be ≥ `0.5 + 0.3 × (caution01 − 0.5)` (0.35-0.65). The opening must be confirmed and not older than `OpeningMaxAgeHours` = 0.5 doctrine-h.
- **requirement-risk-check** — All of these must pass, and each failure reports its own reason:
  1. **reserve-floor** (requirement-reserve-floor);
  2. **line-integrity**: if the asset stands in the own line (in contact, or a holding or attack task with a frontage), the own-line gap routine (requirement-detect-gap applied to own front-line regiments without the asset's subtree, no watched test) must find no own gap ≥ `GapMinMeters` at the asset's lateral position facing a perceived enemy within 500 m;
  3. **main-effort**: the asset is not the army's Main or SupportMain direct child, and not a node with an AttackObjective or SupportAttack task in the main-effort sector, unless the opening's aim lies in that sector (then the exploit pushes the main effort's own direction);
  4. **recovery-path**: the straight segment from the asset to the aim point, sampled every 2 m on the BC10 grid, crosses no river cell (ids 5 and 6, a ford or bridge, pass; a frozen map passes), so the asset can come back the way it went;
  5. **stance**: the side is not a Defender, unless the kind is `Penetration` and question-defender-counterstroke is approved;
  6. **phase**: the army plan is not in Withdraw, and the side is not in army retreat (`macroai` 3, which also stands the producer down);
  7. **concurrency**: the side has fewer than `MaxConcurrentExploits` = 3 live exploits, the deciding node has none, and the opening is not claimed by another exploit.
- **requirement-reserve-floor** — For a deciding node with `n >= 3` direct children, at least `ceil(n × floorShare)` of them must stay uncommitted after the asset leaves. Uncommitted means: role Reserve, or a ReserveWait / FormUp / Hold task, out of contact, no live exploit. `floorShare = clamp(0.30 + 0.2 × (caution01 − 0.5) − 0.1 × (casualtyTolerance01 − 0.5), 0.15, 0.45)` (Jomini, § 3). With `n <= 2` the floor is 0, and line-integrity carries the risk.
- **requirement-exploit-chance** — After the thresholds and risk checks pass, the decision rolls `p = clamp(baseChance(kind) × (0.5 + agg01) × (0.75 + 0.5 × audacity01) × duty, 0, 0.95)`, with `duty` from requirement-commander-duty. The base chances are: Gap 0.6, ShakenSector 0.6, UnsupportedBattery 0.5, IsolatedCluster 0.5, FlankInAir 0.4, Penetration 0.6. The roll is a pure hash of (battle key, commander index, opening id, decision number) mapped to [0, 1): deterministic, testable, no allocation. A failed roll marks that (commander, opening) pair as passed over for `HesitationHours` = 0.1 doctrine-h. Because of the roll, hesitation is a matter of temperament, not a rule.
- **requirement-decision-contract** — `OpportunityDoctrine.Decide` is pure. It takes one deciding node's input and returns one `DecisionResult`. The producer owns everything around it:
  - **Before `Decide`:** the decision interval and the stagger, asset eligibility (requirement-asset-eligibility), and the hesitation and claim lookups.
  - **After `Decide`:** writing the hesitation mark on `roll-miss`, setting `Opening.Claimed` and creating the exploit entry on a launch, and incrementing the node's decision number.

  Inside `Decide`, pairs are formed and scored (requirement-asset-choice, reach, odds) and sorted by score descending. For the top `MaxPairsPerDecision` pairs, `Decide` applies the odds, confidence and age, and risk checks in § 6 order. The first pair that passes rolls. It launches iff `roll < chance`, else `roll-miss`. If no pair passes, the result carries the last failing reason, or `no-pair` when there was no pair.

  Definitions:
  - **Opening age:** `now − FirstSeenHours`.
  - **Confidence:** a `Visible` sample contributes confidence 1.0, so a gap between two visible units has confidence equal to its watched recency.
  - **`Opening.LocalEnemyValue`:** filled by the detector, Σ over samples whose centre lies within `OddsRadiusMeters` of the aim. A `Visible` sample counts strength × morale; a `Remembered` one counts strength × confidence × last morale.
  - **Heading:** a `Gap`'s heading is the front axis's forward bearing. A `FlankInAir`'s heading points along the line toward its centre. Every other kind's heading is the bearing from the asset to the aim, set at launch.
  - **The roll:** `Roll` is FNV-1a over the four 32-bit ints (battle key, commander index, opening id, decision number), taken as `hash / 2^32`. The battle key is the snapshot's battle key (the `BattleUnits` instance id that `BattleCoreTick` keys snapshots by). The decision number is the node's count of decisions this battle.
  - **The launch reason literal:** `launch`.
  - **Main effort for a nested asset:** a brigade or division asset is in the main effort when its army-level ancestor (the D5 ancestor that is a direct child of the army) has role Main or SupportMain **and** the asset's own task is `AttackObjective`, `SupportAttack` or `FixEnemy`. A reserve or holding brigade inside the main-effort corps is that corps' reserve, and it is eligible.

  `DecisionInput` carries:
  - the node: id, commander index, `status`, its direct child count, and each child's uncommitted flag (for the floor);
  - the eligible assets: id, own index, position, lateral, sector, cavalry flag, `Strength`, `GroupMorale`, `GroupWidth`, `LossFraction`, task, in-contact and has-frontage flags, and the main-effort flag defined above;
  - the openings (a span);
  - the side's stance, the plan phase, `macroai`, the main-effort sector, the live exploit count, and whether the node already has an exploit;
  - the battle key, the decision number and `now`;
  - the terrain (`IOpeningTerrain`) and the snapshot (for `OwnGapAt`);
  - the hesitation lookup (`Func`-free: an `IHesitationLookup` interface implemented by the producer's store);
  - whether the counterstroke is on.
- **requirement-exploit-override** — While an exploit is live, the posture producer asks `ExploitTable.TryOverride(groupId, out CommandDoctrineOrder order, out CommandNodeOperationalState state)` before `PostureLedger.TryResolveDoctrineOrder`. The **asset node** gets the exploit order. Every **descendant** node gets a `follow` override: the posture producer submits no Move or Formation for it, so vanilla's group order re-lays it (`SetGroupFormation` re-paths every child, 92076-92108). Its facing pulse and fire control run as usual. A hit in `ProduceLeaf` goes through `PosturePipeline.Run` with the override's state and order, instead of the leaf slot path (`LeafSlotTarget` / `LeafBrigadeMovePolicy`). The exploit order carries the exploit's task (Advance and Strike: `AttackObjective`; Consolidate: `HoldObjective`; Counterstroke: `CommandTaskType.Counterstroke` if approved), the aim point as `PrimaryTarget`, the heading as `ManeuverHeading`, and the reason literal `exploit-<phase>`. The posture pipeline handles an exploit order as follows:
  - It skips the cavalry follow rewrite (`TryCavalryFollow`), GT recon and the army opportunity override for it: the exploit has already checked its target (§ 14 D-C3).
  - It keeps the local flank emergency. When the emergency rewrites an exploit order's task, the posture producer sets `ExploitFeedback.FlankEmergency` for that asset (per side, read-only to everyone but the posture producer). The next opportunity tick reads it as `flank-threat` (one tick late, by design: the table has one writer). The BC9 probe nomination and probe hold, and the main-arrival hold, do not apply to an exploit order.
  - The Advance and Strike Moves are not delayed: the posture pipeline leaves `IntentFlags.UseOrderDelay` unset for an exploit request, through an `exploit` argument of `CommandWaypointWritePolicy.ShouldUseOrderDelayForExecutorWaypoint`. No new flag is needed, because the executor's `UsesOrderDelay` already reads the intent's flag. This is as vanilla's own isolated-target move does (`CheckForSeparatedEnemy` 8188, `useorderdelay: false`). So the courier cadence never applies, and the settled-asset rule keeps the recent-order cooldown from blocking the launch.
  - `PostureStanceGuard` still runs. An `AttackObjective` override on a Defender is never created (risk check 5), and the guard would turn it into `HoldObjective` if one were.
- **requirement-runtime-wiring** — Runtime details the plan must follow:
  - **Build order.** At battle start the coordinator builds each AI side's snapshot (`BattleCoreTick.EnsureSnapshot`) first, then the personality table, then the `ArmyOrchestrator`, whose personality is a constructor argument.
  - **Army entry.** The army entry comes first: `GetCommandingOfficerFromSide(side)` is resolved and added as echelon Army. An index of −1 gives `Default`.
  - **Extension.** On every coordinator tick, while `Enable Per-Commander Personality` is on, the table is extended for any own group record whose commander index has no entry. This is an O(groups) lookup into a grow-only array indexed by commander index. The snapshot exposes no roster-change flag.
  - **Echelon** for `ApplyRankBias`: `unittyp` 14 brigade, 15 division, 16 non-top corps, the top unit army. The first echelon seen for an index wins.
  - **Name matching.** Subordinate entries pass no name hint, so the registry matches on `combinedname`, then `firstname + name`.
  - **#44's army-off fallback** reads `To01(armyEntry.Aggression)`, or 0.5 without an entry. With the personality flag off it reads today's live initiative.
  - **The stance-cooldown fix** acts only on AI-run sides' instances. It skips the W&L feud instance (`IsFeudInstance`), whose stances are W&L's.
  - **Whiskey's own write wins.** A `TacticalStanceWriter` write after the fix's re-stamp overwrites it (stamp `now + HoldHours + T`), so Whiskey's hold, not vanilla's restored one, governs a Whiskey-owned group. This is intended: § 9.
- **requirement-exploit-lifecycle** — An `ExploitEntry` (pure state machine `ExploitLifecycle.Step`, on the doctrine clock) moves through these phases:
  - **Advance:** the asset moves to a stand-off point. That point is the aim point for infantry. For cavalry it is 60 m short of the target on the approach bearing, so vanilla's 90 m cavalry reach (5049) covers it.
  - **Strike:** entered on arrival within `StrikeMeters` = 150 m of the aim point (infantry) or 90 m (cavalry), or on first visible contact within 250 m. Infantry keeps `AttackObjective`. Vanilla's own assault escalation (`AdjustGroupAIStance` 4240) and the BC6 charge doctrine do the closing. Cavalry stands at attack stance, and vanilla's cycle-7 charge (steered by requirement-fix-cavalry-strike) does the charging.
  - **Consolidate:** entered when the target is routed, gone or no longer perceived at the aim, or when the aim ground is held with no enemy within 150 m. Task `HoldObjective` at the aim point for `ConsolidateHours` = 0.1 doctrine-h.
  - **Pursue:** cavalry only, audacity01 ≥ 0.6, the target routed. The asset follows the routed sample for at most `PursuitMeters` = 400 m from the aim point, then consolidates.
  - **Recall:** the override is dropped. The asset's ledger order (its role, slot or reserve position) takes it back through the posture pipeline, and the asset enters its cooldown.
  - **Abort:** goes straight to Recall, with one reason:
    - `opening-lost` (lost before Strike);
    - `odds-collapsed` (local odds < 0.6 × required);
    - `asset-losses` (`LossFraction` up ≥ `0.10 + 0.10 × casualtyTolerance01` since launch, or morale < `ShakenMorale`);
    - `line-crisis` (the deciding node's line-integrity check fails, or the allocator gives the asset's parent line a crisis release);
    - `safety` (a Safety claim on the asset);
    - `flank-threat` (the posture pipeline's local flank emergency);
    - `timeout` (`MaxExploitHours` = 0.5 doctrine-h since launch);
    - `stand-down` (producer stand-down, flag off, a new battle day, battle end);
    - `withdraw-phase` (the army plan enters Withdraw).

  **Create:** the decision step. **Change:** `Step` each tick. **Unbind:** Recall drops the override, and the asset falls back to its ledger order on that tick. **Delete:** the entry is freed after Recall, and the asset's cooldown stays in the per-asset store. **Migration of in-flight work:** when an entry is dropped mid-move, the next posture tick resolves the ledger order. The executor's dedupe and cooldowns apply to that new Move as to any other; a Safety withdrawal outranks everything by arbiter priority.
- **requirement-lifecycle-contract** — The precise rules `ExploitLifecycle.Step` follows:
  - **Input.** `ExploitStepInput` carries: now; the deciding commander's audacity01 and casualtyTolerance01; `ShakenMorale`; whether the asset is alive, with its position, `LossFraction` and `GroupMorale`; whether the opening is still held by the detector; whether the executor gave up the Advance Move; whether the entry has a target, with the target's perceived, routed and position fields; the nearest Visible enemy to the asset; the nearest enemy sample to the aim; the local odds; the line-integrity result; a crisis release of the asset's parent line; a Safety claim on the asset; the posture pipeline's flank emergency; stand-down; and the plan's Withdraw phase.
  - **Target and aim.** `ExploitEntry` gains `TargetObservation`: the battery, the shaken centroid's nearest shaken sample, the cluster's strongest sample, or the penetrating sample; −1 for `Gap` and `FlankInAir`. While the target is perceived (Visible, or Remembered at ≥ 0.5), the aim follows it, except in Pursue, where the aim stays where the strike was. `Gap` and `FlankInAir` aims are frozen at launch.
  - **The override target.** For cavalry it is the stand-off point, `aim − 60 m` along the approach bearing. For infantry it is the aim point.
  - **Definitions.** Contact means any Visible enemy sample within 250 m of the asset's position. **Held** means the asset is within its strike distance of the aim and no enemy sample is within 150 m of the aim.
  - **One transition per step.**
  - **Abort scope and precedence**, first match wins:
    1. `stand-down`
    2. `withdraw-phase`
    3. `safety`
    4. `flank-threat`
    5. `line-crisis`
    6. `asset-losses` (the asset gone counts here)
    7. `odds-collapsed`, in Advance and Strike only
    8. `opening-lost`, in Advance only: the opening is no longer held, or the executor gave up the Advance Move, or the target is lost while more than 150 m from the aim
    9. `timeout`, in every phase but Consolidate

    A target lost within 150 m of the aim during Advance goes to Consolidate, not to an abort.
  - **Success.** `Success` is set when Consolidate's `ConsolidateHours` elapse. Pursue ends in Consolidate when the target is no longer perceived, no longer routed, or more than `PursuitMeters` from the aim.
  - **Clock.** All times are `double` doctrine hours, and every "elapsed" comparison is `>=`.
- **requirement-staggered-reserve-release** — `DirectChildAllocator.ReleaseReserves`, the army-level reserve release, changes in three ways:
  - **Exploit and odds releases:** at most **one** Reserve child is released per `releaseInterval = 0.15 − 0.1 × agg01` doctrine-h (0.05-0.15), the one nearest by sector to the main effort. A release never brings the held reserves below `max(1, ceil(R0 × floorShare))`, where `R0` is the number of Reserve children when the trigger first held, and `floorShare` is requirement-reserve-floor's formula with the army commander's vector. The exception is `R0 = 1`, where the single reserve may go.
  - **Crisis releases:** one Reserve child per crisis sector (the nearest). Today every adjacent reserve goes. A crisis release may go below the floor (defence first), and the row records `floorBreach=crisis`.
  - **BC11's flank release:** unchanged.

  Plumbing and tie rules:
  - `ReserveReleaseContext` gains: now (`double` doctrine hours); the army commander's agg01, caution01 and casualtyTolerance01; a per-child `Exploiting` flag (`ExploitTable.IsExploiting`); and the state. `ReserveReleaseState` gains `Record(childId, hours)`.
  - **Exploit and odds candidates** are Reserve children that are not exploiting, ordered by sector distance to the main effort, then the lower child index. This is the shipped "lowest index on a tie" rule of `ReleaseReserveToThreatenedWing`.
  - **Crisis releases** stay stateless (recomputed every run, as today) and keep today's ±1 adjacency, with the nearest reserve by sector chosen, then the lower index. A crisis-released child counts as not held for the floor, and it does not move `LastReleaseHours`. While a crisis holds, the floor can therefore block the stagger: defence first.

  The state lives in `ReserveReleaseState` (pure; per side; `BattleScope`): the released ids with their release hours, `R0`, and the last release hour. A released child stays released while its trigger holds, so a re-run of the allocator keeps it released. When the trigger stops, the released children revert as they do today and the state clears. A plan change, a new battle day and a stand-down clear it too.

---

## 7. (D) Vanilla battle bug fixes

Each bug gets a bug id at merge (provisional, after BUG-TAC-026, which BC15 holds), an entry in `docs/bug-fixes/vanilla-tactical-battlefield.md`, a test and its own default-on rollback flag. A fix that needs a Transpiler or a Prefix that in effect skips vanilla is **not** assumed: it is a `question-*` naming the minimal mechanism, and the default when declined is stated.

- **requirement-fix-stance-cooldown** (4264, `AdjustGroupAIStance`). The test `lastaistancechangetime < now + updatetimeforresettingofaistance` is always true for a past stamp. The `battleprefs` 804-805 text ("Time … to wait for resetting of new ai stances once set (for enemy ai only)", 1 game-h) states the intent: wait 1 h after a change.

  The comparison is **not** changed, because Whiskey's stance hold depends on its current form (`TacticalStanceOwnership.VanillaHoldStamp`, `VanillaHoldLive`, `ReleaseStamp`, § 2.2). Instead, a **re-stamp steer**:
  - A non-skipping Prefix on `AIBattle.AdjustGroupAIStance` records each pool group's `ai_stanceordered`.
  - A Postfix finds the groups whose `ai_stanceordered` vanilla changed in this call (stamp equal to the raw clock now). It sets `lastaistancechangetime = now + 2 × T`, so vanilla skips the group until `now + T` (the inverted test skips while `stamp >= t + T`).

  Whiskey's own stamps are untouched. `ReleaseStamp` still re-arms vanilla on a Whiskey yield or army retreat. Patch `VanillaStanceCooldownFixPatch`, flag `Enable Vanilla Stance Cooldown Fix`, scopes `tactical.patch.stance-cooldown.prefix|.postfix` (2.0 ms). Like the other vanilla bug fixes (BC15 A2 part 2, A3), it runs in every Commander Mode. Effect and risk: vanilla's weak-group defend and screen valves now wait up to 1 h after vanilla's own previous change → question-stance-cooldown-valves.
- **requirement-fix-charge-renew** (4917, `MicroAICheckForCharges`). The inverted renew test sits inside a method that never proceeds (4910, BUG-TAC-016). The intended back-off is already applied by `ChargeDoctrine.RefusalBackoffSeconds`. **No new code.** The bug entry records the coverage, and test FIX-RENEW pins that the doctrine reads `timetorenewaichargecheck`.
- **requirement-fix-macro-upgrade** (6432-6434, `CheckGlobalAIStrategy`). Vanilla's upgrade "no flanking possible and balance ≥ 3.0 / (1 + init × 0.7) → assault" writes `newmacroaitoset = 0` only when `newmacroaitoset == 1`, instead of turning this cycle's `num4` from 1 into 0. The tail block (6438-6446) then compares and stores `num4`, so the upgrade takes effect only when the pending value was already attack and the tail does not overwrite it.

  `newmacroaitoset` and `timertosetnewmacroai` are fields, but the tail has already run with the uncorrected `num4` by the time a Postfix sees them. A Postfix-only fix is not possible. Two minimal mechanisms, both **question-macro-upgrade-mechanism**:
  - (a) A Transpiler that rewrites the 6432-6434 site to test and store the local `num4` (a 4-6 instruction change, the `CavalryFlipIl` pattern: exact site count or unchanged plus OnceLog, `new CodeInstruction(ci)` copies, a real-IL test, `TranspilerLabelTests`).
  - (b) A non-skipping Prefix that records `macroai`, `newmacroaitoset` and `timertosetnewmacroai`, plus a Postfix that ports 6396-6446 (about 20 lines, source-pinned) and re-applies the tail with the corrected `num4`.

  The fix runs before #44's Postfix (`Priority.First`). #44 reads `newmacroaitoset` on the W&L player side only (vanilla's pending value), and the fix gives it the corrected pending value. The fix only matters where vanilla's own value stands: no Whiskey macro opinion (DefensiveHold / NoOpportunity), or the army orchestrator off. Flag `Enable Vanilla Macro Assault Upgrade Fix`. If declined: no change, recorded.
- **requirement-fix-artillery-side** (7092, `AssignReserves`). `Random.Range(1, 2)` (an int range with an exclusive max) is always 1, so every artillery group joins the left chain end (`AssignReserveToOperationalGroup` direction 1, `linkedposition` 3). The same class appears at `CheckUseOfReserves` 6116, `Random.Range(0, list.Count − 1)`, which never picks the last endangered regiment (BUG-TAC-008, backlog). Minimal mechanism for both: a Transpiler changing the constant upper bound (the `ldc.i4.2` before the `Random.Range(int, int)` call → `ldc.i4.3`) and the `Count − 1` (remove the `ldc.i4.1; sub` before that call). It follows the same IL rules as above → **question-random-range-mechanism**. Flag `Enable Vanilla Random Range Fixes`. If declined: no change, recorded.
- **requirement-fix-cavalry-strike** (`AdjustCavalryBehavior` 5049-5056, and the odds bypass through `SetMovementMode(6)`). This is a **result-override steer**: no skip and no Transpiler (the `SkirmisherScreenDistancePatch` precedent).
  - A non-skipping Prefix, a `Priority.First` Postfix and a Finalizer on `AIBattle.AdjustCavalryBehavior` set and clear a call-depth flag. The flag records the `aigroup` and the side.
  - A Postfix on `Regiment.GetClosestEnemyUnitReg(float, int[])` acts only while the flag is set, for an AI cavalry regiment (`unittyp` 1) of a side that `TacticalBattleCoordinator.DrivesOrchestrator`, in Commander Mode Active, with the flag on. Pinned: inside `AdjustCavalryBehavior` the only call passes `maxdistancecavalryattack` alone (5049); a source-pin test fails if another call appears.
  - It keeps vanilla's result only when `CavalryStrikeCheck` (pure) passes:
    - the target is `Visible` to the side;
    - it is artillery with no infantry or cavalry sample within 250 m (requirement-detect-unsupported-battery's test), or a non-artillery unit with `Morale < ShakenMorale` whose vanilla-weighted odds (the `UpdateChargeTarget` formula, 122229-122231, through BC8's `TroopQualityRuntime`) exceed `microaitriggerforcharge`;
    - it is not in a square (regiment formation 4) and not garrisoned;
    - the charging regiment is not outflanked (perceived `UnitRecord.Outflanked`, which honours `nomicroaichargeifunitisoutflanked`).
  - If a live exploit's cavalry target lies within `maxdistancecavalryattack` of the regiment and passes, that target replaces vanilla's pick. Otherwise a failing result becomes `null`, so vanilla's caller skips the charge this cycle (5050 tests for null).
  - **Fail-open:** with no snapshot for the side, perception off (`Enable Battle Fog-Honest Perception = false`), or any exception, vanilla's result stands. This is BC2's range-only fallback.

  **Contract:**
  - **Mapping.** The vanilla `Regiment` maps to the side's latest snapshot by `gameObject.GetInstanceID()`, through a linear scan of the observations (bounded, allocation-free). The snapshot is built once per side-stat cycle, and micro cycle 7 runs between builds. A target with no `Visible` observation in the latest snapshot is refused as `hidden`, because the side has not perceived it yet. Fail-open covers only a missing snapshot, perception off, or a charger missing from the snapshot.
  - **Odds inputs.** These are vanilla's `UpdateChargeTarget` formula (122229-122234):
    - the target's strength, perceived experience and morale, from its observation;
    - its fatigue, from the Visible target's own `EnemyRecords` row (the BC6b precedent);
    - the charger's own record, plus its weapon's `meleemounted` multiplier (live), and `weightingfactorsformicroaicharge` and `microaitriggerforcharge` through `TroopQualityRuntime`.
  - **Exploit target.** `ExploitEntry.TargetObservation` is an instance id. Only an exploit whose asset is the charging regiment's ancestor applies. The substitute `Regiment` is taken from the charger's own `unitrange.closestenemyunit` candidates (vanilla's own set). It must pass both `CavalryStrikeCheck` and vanilla's 5050 post-filter (artillery, or morale below the bar, not routed), or it is not substituted.
  - **Player and feud.** The Postfix leaves vanilla's result untouched when the charger fails `TacticalWriteGuard.CanWrite` (player-owned), or when the `AIBattle` is the W&L feud instance (`IsFeudInstance`). This is the `SkirmisherScreenDistancePatch` precedent.
  - **Refusal reasons:** `hidden`, `square`, `garrisoned`, `supported`, `steady`, `odds`, `outflanked`.

  Patch `CavalryStrikeDisciplinePatch`, flag `Enable Cavalry Strike Discipline`, scopes `tactical.patch.cavalry-strike.adjust-prefix|.adjust-postfix|.adjust-finalizer|.target-postfix` (2.0 ms).
- **requirement-fix-charge-weight** (122229-122230, `UpdateChargeTarget`). The strength weight `(1 + w0)` multiplies both sides of the ratio, so it cancels. Any reading of `w0` as the strength ratio's exponent gives today's result with the shipped `w0` = 1 (`battleprefs` 758-759). **No change**, because there is no observable effect on shipped data. Recorded.
- **requirement-fix-cavalry-morale-label** (5050 against `battleprefs` 882). The code uses the value as a maximum, which is the sensible reading: charge shaken targets. The label "Minimum" is wrong. **No code change.** Recorded, and the value is reused as `ShakenMorale`.
- **requirement-fix-outflanked-flag** (`nomicroaichargeifunitisoutflanked`, 54784). Infantry already honours it (`ChargeDoctrine`). Cavalry honours it through requirement-fix-cavalry-strike. Vanilla still never reads it. Recorded.
- **requirement-fix-dead-outflank-retreat** (`CheckIfOutflanked` 6188). It has no caller and is behind a debug switch. Reviving it would add a new, omniscient retreat behaviour, not fix a broken one. **Not revived.** This departs from "Fix them" → question-dead-outflank-retreat.

---

## 8. Interfaces (pure unless marked runtime)

```csharp
namespace WhiskeyRealism.Tactical.Battle {
  public enum PersonalitySource : byte { Default = 0, Registry = 1, Derived = 2 }
  public struct CommanderStats { public float Initiative, Cunning, Leadership, Administration; public bool Veteran, WestPoint, Political, Valid; }
  public static class CommanderPersonalityDerivation {
    public const float InitiativeCentre = 0.45f, AdministrationCentre = 0.55f, CunningCentre = 0.38f, LeadershipCentre = 0.55f;
    public static PersonalityVector Derive(in CommanderStats s);                 // § 4 table, clamped
    public static float To01(float signed);                                       // (v + 1) / 2
  }
  public sealed class CommanderPersonalityTable {                                // per side, BattleScope
    public bool TryGet(int commanderIndex, out PersonalityVector v, out PersonalitySource source);
    public void Add(int commanderIndex, in PersonalityVector v, PersonalitySource source);   // coordinator only; first write wins (pinned)
    public int Count { get; } public int CountBySource(PersonalitySource s);
  }
  public enum OpeningKind : byte { Gap, ShakenSector, FlankInAir, UnsupportedBattery, IsolatedCluster, Penetration }
  public struct Opening { public int Id; public OpeningKind Kind; public float AimX, AimZ, Heading, Width, LocalEnemyValue, Confidence01;
    public int Sector, TargetObservation; public double FirstSeenHours, LastSeenHours; public bool Confirmed, Claimed; }
  public interface ICoverageRecency { float RecencyOver(float x0, float z0, float x1, float z1, double nowHours); }
  public interface IOpeningTerrain { bool RiverWithin(float x, float z, float dirX, float dirZ, float meters); bool CrestWithin(float x, float z, float meters); bool SegmentCrossesRiver(float x0, float z0, float x1, float z1); }
  public struct OpeningRules { public float ShakenMorale, AnchorRiverMeters, AnchorHillMeters; public bool PictureReady; }   // GamePrefs values, read once per battle
  public sealed class OpeningDetector {                                          // per side, pooled, BattleScope
    public const float GapMinMeters = 150f, LineDepthBandMeters = 200f, GapDepthMeters = 300f, WatchedRecency = 0.5f, RememberedMinConfidence = 0.5f,
      DefaultEnemyFrontageMeters = 100f, ShakenShareMin = 0.4f, FlankWatchMeters = 300f, FlankOffsetMeters = 150f, SupportMeters = 250f,
      ClusterLinkMeters = 150f, RefusedEndDegrees = 45f, ConfirmHours = 0.02f, LostHours = 0.05f, CounterstrokeMaxMeters = 400f;
    public const int MaxOpenings = 32, MaxClusters = 64;
    public int Detect(BattleSnapshot s, ICoverageRecency watched, IOpeningTerrain terrain, in OpeningRules rules, bool defender, double nowHours);  // returns count; no allocation after warm-up
    public ReadOnlySpan<Opening> Openings { get; } public int SuppressedUnwatched { get; }
    public bool TryCluster(int id, out float x, out float z, out float value, out float nearestOtherMeters);
    public static float OwnGapAt(BattleSnapshot s, int excludeSubtreeIndex, float lateral);   // the line-integrity check's own-line gap
  }
  public enum ExploitPhase : byte { None, Advance, Strike, Consolidate, Pursue, Recall }
  public enum ExploitEnd : byte { None, Success, OpeningLost, OddsCollapsed, AssetLosses, LineCrisis, Safety, FlankThreat, Timeout, StandDown, WithdrawPhase }
  public struct ExploitEntry { public int Id, CommanderIndex, DecidingNodeId, AssetId, OpeningId, TargetObservation; /* -1 for Gap / FlankInAir */ public OpeningKind Kind; public bool Cavalry;
    public ExploitPhase Phase; public ExploitEnd End; public float AimX, AimZ, Heading, RequiredOdds, LaunchLoss; public double LaunchHours, PhaseSinceHours; }
  public struct OpportunityPersonality { public float Agg01, Caution01, Audacity01, CasualtyTolerance01, Duty; }
  public struct ExploitAsset { public int Id, OwnIndex, Sector; public float X, Z, Lateral, Strength, GroupMorale, GroupWidth, LossFraction;
    public bool Cavalry, InContact, HasFrontage, InMainEffort; public CommandTaskType Task; }
  public interface IHesitationLookup { bool IsHesitating(int commanderIndex, int openingId, double nowHours); }
  public struct DecisionInput {                                                  // requirement-decision-contract
    public int NodeId, CommanderIndex, CommanderStatus, ChildCount, UncommittedChildren;
    public ExploitAsset[] Assets; public int AssetCount;                        // pooled, pre-filtered by the producer
    public Opening[] Openings; public int OpeningCount;
    public BattleStance Stance; public BattlePhase PlanPhase; public int MacroAi, MainEffortSector, SideLiveExploits; public bool NodeHasExploit, CounterstrokeOn;
    public int BattleKey, DecisionNumber; public double NowHours;
    public IOpeningTerrain Terrain; public BattleSnapshot Snapshot; public IHesitationLookup Hesitation; }
  public struct DecisionResult { public bool Launch; public string Reason; /* static literal */ public int AssetId, OpeningId; public float Score, Chance, Roll, Odds, RequiredOdds, Confidence, ConfidenceMin; }
  public static class OpportunityDoctrine {
    public const float DecisionIntervalHours = 0.05f, AssetMoraleMin = 0.6f, AssetLossMax = 0.3f, InfantryReachMeters = 600f, CavalryReachMeters = 1200f,
      OddsRadiusMeters = 300f, OpeningMaxAgeHours = 0.5f, HesitationHours = 0.1f, AssetCooldownHours = 0.25f;
    public const int MaxConcurrentExploits = 3, MaxPairsPerDecision = 4;
    public static float KindWeight(OpeningKind k, bool cavalry, float audacity01, bool clusterShaken);
    public static float RequiredOdds(OpeningKind k, float agg01);
    public static float ConfidenceMin(float caution01);
    public static float FloorShare(float caution01, float casualtyTolerance01);
    public static float Chance(OpeningKind k, in OpportunityPersonality p);
    public static float Roll(int battleKey, int commanderIndex, int openingId, int decisionNumber);     // pure hash → [0, 1)
    public static DecisionResult Decide(in DecisionInput input, in OpportunityPersonality p);
  }
  public struct ExploitStepInput { public double NowHours; public float Audacity01, CasualtyTolerance01, ShakenMorale;
    public bool AssetAlive; public float AssetX, AssetZ, AssetLoss, AssetMorale; public bool OpeningLive, AdvanceGaveUp;
    public bool HasTarget, TargetPerceived, TargetRouted; public float TargetX, TargetZ, NearestVisibleEnemyToAsset, NearestEnemyToAim, LocalOdds;
    public bool LineIntegrityFails, ParentCrisisRelease, SafetyClaim, FlankEmergency, StandDown, PlanWithdraw; }   // requirement-lifecycle-contract
  public static class ExploitLifecycle {
    public const float StrikeMetersInfantry = 150f, StrikeMetersCavalry = 90f, CavalryStandOffMeters = 60f, ConsolidateHours = 0.1f,
      PursuitMeters = 400f, MaxExploitHours = 0.5f, OddsAbortShare = 0.6f;
    public static void Step(ref ExploitEntry e, in ExploitStepInput input);
  }
  public sealed class ExploitTable {                                             // per side, BattleScope; writer: the opportunity producer only
    public bool TryOverride(int groupId, out CommandDoctrineOrder order, out CommandNodeOperationalState state);
    public bool IsExploiting(int groupId);                                       // asset, its ancestors and descendants
    public void Clear(ExploitEnd reason);
  }
  public static class CavalryStrikeCheck { public static bool Allows(in CavalryStrikeInput i, out string reason); }
}
namespace WhiskeyRealism.Tactical.Orchestrator {
  public sealed class ReserveReleaseState { public int InitialReserves; public double LastReleaseHours; public bool IsReleased(string childId); public void Record(string childId, double hours); public void Clear(); }
}
// TacticalEnemyContactLedger.RecencyOver(...) (additive accessor); CommandDoctrineOrder.ExploitId (-1 none, additive);
// TacticalReinforcementOpportunityDoctrine.EffectiveAttackThreshold / ComputeCasualtyEscalationThreshold (changed per § 4);
// AggressionBias01 removed from DirectChildIntent, CommandNodeIntent, CommandNodeIntentSnapshot; TacticalCommanderIntentInput.WeakPointConfirmed removed;
// IntentSource gains nothing (the posture producer submits exploit orders as PostureExecutor, reason literal "exploit-<phase>");
// ExploitFeedback (per side, BattleScope; writer: the posture producer; reader: the opportunity producer): FlankEmergency per asset id.
// Runtime: OpportunityIntentProducer, CommanderPersonalityTableRuntime (stat reads), CavalryStrikeDisciplinePatch, VanillaStanceCooldownFixPatch,
// and, if approved, the IL rewriters MacroUpgradeIl / RandomRangeIl and their patches.
```

---

## 9. Roles, lifecycles, shared rows and conflict rules

**Roles (who may perform each verb).**

| Verb | Role | Never |
|---|---|---|
| Build or extend the personality table | Coordinator tick (battle start, roster change) | Patches; producers |
| Detect openings, decide, step exploits, write the exploit table | `OpportunityIntentProducer`, inside the arbiter tick, AI-run sides only | Patches; any side the coordinator suppresses (a CIC player's alliance, the player alliance outside `ai_vs_ai`) |
| Move an asset | The posture producer, submitting through the arbiter to the executor | The opportunity producer itself; any player-owned unit |
| Charge with cavalry | Vanilla `AdjustCavalryBehavior`, target filtered by `CavalryStrikeDisciplinePatch` (a steer, no write) | Whiskey (no `AssignChargeTarget` call) |
| Release reserves | `DirectChildAllocator` on the heavy path, executed by the posture and reserve-release producers | — |
| Re-stamp vanilla's stance stamp | `VanillaStanceCooldownFixPatch` Postfix, only for groups vanilla itself changed in that call | Groups Whiskey stamped |
| Decide for the W&L player's command | The player | Every Whiskey path |

**Lifecycle table.**

| Entity / binding | Create | Change | Unbind | Delete | Migration of live dependents |
|---|---|---|---|---|---|
| Personality entry (commander → vector) | Battle start; roster change | Never (pinned) | — | `BattleScope` reset | A hit commander keeps the entry, and the duty factor applies (requirement-commander-duty) |
| Opening | `Detect` | Refreshed per tick (matched within 100 m) | Lost after `LostHours` | Slot reused | A live exploit whose opening is lost before Strike aborts `opening-lost`; after Strike it continues on its own state |
| Exploit entry / asset binding | Decision launch | `Step` per tick | Recall drops the override | Freed after Recall; cooldown kept per asset | Asset falls back to its ledger order on the next posture tick |
| Asset cooldown | Recall | — | Expires after 0.25 doctrine-h | `BattleScope` reset | — |
| Reserve release state | First release under a trigger | Each release | Trigger stops: released reserves revert | Plan change, new day, stand-down, `BattleScope` reset | Released reserves revert to Reserve on the next allocation, as today |
| Override binding (group → exploit order) | Same as the exploit entry | Per phase | Recall | With the entry | **Version semantics: latest.** The posture producer reads the table published earlier in the same tick; there is no pinned copy |

**Shared rows and conflict rules.**

| Shared row | Writers | Conflict rule |
|---|---|---|
| A group's Move / Stance / Facing channel | Posture (exploit override or ledger), Withdrawal (Safety), division play, recon, reserve release, charge | Arbiter priority as today (Safety > Executor > Play > Support > Scorer). An exploit order is the posture producer's Executor intent. The division play loses to it (D8). A Safety claim beats it and aborts the exploit (`safety`). Recon and probe never pick a group that is exploiting, and an exploit never picks one under recon (requirement-asset-eligibility) |
| An asset wanted by two deciders | Division and corps commanders | One exploit per subtree (`ExploitTable.IsExploiting` over ancestors and descendants). Deepest node decides first in a tick. Ties go to the lower instance id. Never last-write-wins: the second decision sees the first entry |
| An opening wanted by two deciders | Any | `Opening.Claimed` is set by the first launch, and later deciders skip claimed openings |
| A reserve wanted by the army allocator and a local exploit | Allocator (role) and opportunity producer (order override) | A **crisis** release of the asset's parent line aborts the exploit (`line-crisis`). An Exploit-phase or odds release does not: the exploit runs on, and the allocator's SupportMain applies after Recall. The allocator never releases a group with a live exploit; it skips it and takes the next nearest |
| BC11 maneuver vs exploit | Play executor, opportunity producer | Mutually exclusive by eligibility: an asset with a maneuver phase is never chosen, and the play executor does not assign a zone maneuver to a group with a live exploit (it reads `ExploitTable.IsExploiting`) |
| Feud | #42 (W&L) | No overlap: a `Feud` group is never an asset. #42's inline `FeudReplay` is refused under a higher arbiter claim, as today |
| Vanilla's own movers of the same group (`CheckForSeparatedEnemy`, `CheckFlankMoves`, `AdjustGroupAIStance`) | Vanilla | As today: the #68 arbiter runs last in the macro cycle, and the exploit order is re-asserted through the executor's dedupe. `CheckForSeparatedEnemy` is question-separated-enemy-steer |
| `ExploitTable` / `ExploitFeedback` | Opportunity producer / posture producer, one writer each | No shared writer. Each is read only by the other producer: the table is read the same tick, the feedback the next tick |
| `lastaistancechangetime` | Vanilla, `TacticalStanceWriter`, `VanillaStanceCooldownFixPatch` | The fix writes only groups whose `ai_stanceordered` vanilla changed inside this `AdjustGroupAIStance` call. The writer's stamps are written outside that call (#68 Postfix), and a later writer stamp overwrites the fix's (Whiskey's hold is the intended owner) |

---

## 10. Exceptions per step

| Step | Absent | Stale | Deleted | Failed |
|---|---|---|---|---|
| Commander lookup | Index < 0 or out of range → `Default` vector, one warning per battle | — (pinned by design) | Commander removed from `GameVars.commander` → entry kept; the stat read never repeats | Reflection or read throws → `Default`, OnceLog |
| Snapshot / axis | No snapshot or axis invalid → no detection, no decisions, live exploits keep their last order until the next valid tick, `timeout` bounds them | Remembered observations older than the confidence floor are ignored (§ 5) | An observation dropped → an opening using it is not re-detected → `LostHours` | — |
| Coverage ledger | No ledger (army orchestrator off) → the producer does not run (decisions need the army plan) | Old recency → below 0.5 → suppressed | — | Accessor throws → treated as unwatched |
| Terrain | No BC10 grid → river tests false, and the `recovery-path` check passes only on a frozen map, so it fails closed otherwise (no exploit); picture not ready → crest test skipped | — | — | — |
| Asset | Asset gone (destroyed, merged) → entry ends `asset-losses` | Asset moved by another source → the table still overrides it, and a Safety claim aborts | Group destroyed mid-exploit → freed on the next step | Executor `GaveUp` on the Advance Move → `opening-lost` at the next lost check; its Move retries are the executor's |
| Target | Target observation lost → Consolidate if within 150 m of the aim, else `opening-lost` before Strike | Target Remembered at < 0.5 → treated as lost | — | — |
| Cavalry steer | No snapshot / perception off / exception → vanilla result (fail-open) | — | Regiment not in the snapshot → vanilla result | — |
| Stance-cooldown fix | Group not in `unitsused` → untouched | — | — | Exception → no re-stamp, OnceLog |
| Reserve release | No main-effort sector → nearest reserve by index order | — | A released child destroyed → removed from the state | — |

References are **latest**, never pinned: the opportunity producer reads this tick's snapshot and the ledger's last published state, and the posture producer reads the exploit table published earlier in the same tick.

---

## 11. Performance, configuration, telemetry

- **requirement-frame-budget** — The opportunity producer runs once per AI side per arbiter tick:
  - Detection is O(n log n) in the sample count (one in-place sort by lateral, one bucket-grid pass for clusters and support rings, one pass for sectors).
  - Decisions are at most (due commanders × `MaxPairsPerDecision`).
  - Lifecycle steps are at most 3 per side.
  - Scope `tactical.producer.opportunity` (1.0 ms threshold). Target: criterion-frame-budget.
- **requirement-zero-alloc** — No allocation after warm-up: all arrays pooled and grow-only, sorts in place without a comparer allocation, telemetry strings built only on rows that are emitted (numeric throttles, the BC11 F4 precedent). Pinned by OPP-ALLOC.
- **requirement-config** — `Tactical` section, all default `true`, each with a reader and a truthful description:

  | Flag | Covers | Off |
  |---|---|---|
  | `Enable Per-Commander Personality` | requirement-commander-table, registry-first, derive-from-stats, commander-duty, army-vector-from-table, attack-threshold-band, personality-live-inputs | The army vector is from the roster as today, every node uses it, the threshold cap at 1.0 and the aggression-based casualty threshold return |
  | `Enable Commander Opportunity` | §§ 5-6 except the reserve release | No detection, no exploits; the table is empty |
  | `Enable Staggered Reserve Release` | requirement-staggered-reserve-release | Today's release-all |
  | `Enable Cavalry Strike Discipline` | requirement-fix-cavalry-strike | Vanilla's cavalry target |
  | `Enable Vanilla Stance Cooldown Fix` | requirement-fix-stance-cooldown | No re-stamp |
  | `Enable Vanilla Macro Assault Upgrade Fix` (only if approved) | requirement-fix-macro-upgrade | Vanilla |
  | `Enable Vanilla Random Range Fixes` (only if approved) | requirement-fix-artillery-side | Vanilla |
  | `Enable Defender Counterstroke` (only if approved) | requirement-detect-penetration and the counterstroke task | No Defender exploit |

  Rollback of all battle writes stays `Tactical Commander Mode = MonitorOnly` / `Off`. `requirement-dead-personality-paths` is a cleanup and has no flag.
- **requirement-telemetry** — Through the TelemetryRouter tuning-log sink, bounded:
  - `TacticalCommanderPersonality` (State, once per commander per battle): side, commander index, echelon, `source`, the five components, the `agg01` band.
  - `TacticalOpening` (State, per side every 0.1 doctrine-h): counts by kind, confirmed count, best confidence, `suppressedUnwatched`.
  - `TacticalExploit` (Decision): one row per launch, per declined best pair (`reason` = the failing threshold or risk check, or `roll-miss` with `chance` and `roll`), per phase change and per end (`end`, duration, `lossDelta`, stance, `cavalry`, `playerSkips`).
  - `TacticalReserveRelease` (existing) gains `reason=stagger|crisis|flank`, `held`, `floor`, `floorBreach`.
  - `TacticalCavalryStrike` (Decision, throttled per regiment per 0.05 doctrine-h): `allowed` / `refused`, `reason` (`hidden`, `square`, `garrisoned`, `supported`, `steady`, `odds`, `outflanked`, `exploit-target`).
  - `TacticalStanceCooldown` (State, per side every 0.1 doctrine-h): vanilla changes, re-stamped count.
  - OnceLog (static keys): `[once:tactical-battle-core:opportunity-first-opening] [Opportunity] first opening: kind=…`, `…:opportunity-first-exploit] [Opportunity] first exploit launched: kind=… asset=… commander=… source=…`, `[once:tactical-battle-core:cavalry-strike-first-refusal] [CavalryStrike] first refused: reason=…`, `[once:tactical-battle-core:stance-cooldown-first] [StanceCooldown] first vanilla stance hold restored`.

---

## 12. Fit criteria

- **criterion-personality-coverage** — In a smoke battle, ≥ 95 % of each AI side's corps and division command nodes resolve to `Registry` or `Derived`. Basis: every group carries a commander index (`Capture`); `Default` is only the invalid-index path. Measured: `TacticalCommanderPersonality` rows, the count by `source`.
  - guardrail: the per-alliance mean `agg01` of `Derived` vectors on the shipped roster stays within ±0.07 of today's faction default (0.50 Union, 0.60 CSA; the measured means are in requirement-cautious-reachable), so army-level behaviour does not drift as a side effect.
  - read path: `TacticalCommanderPersonality` rows in the tuning-log sidecar.
- **criterion-cautious-band-reach** — On the shipped `commanders.txt`, derived `agg01 < 0.30` for 8-25 % and `agg01 >= 0.70` for 15-35 % of each alliance (the measured shares are in requirement-cautious-reachable). Basis: the § 4 calibration. Measured: harness test PERS-BANDS over a fixture of the shipped roster's stat rows, regenerated by the integrator if the game data changes.
  - guardrail: every `HistoricalFigureRegistry` entry still resolves to its list vector unchanged (PERS-REG).
  - read path: `TacticalCommanderPersonality` rows carry the band, so the per-battle share is visible in the running mod.
- **criterion-opening-fog-honest** — 0 openings whose inputs include an enemy that is not a sample under requirement-enemy-samples, and 0 absence-claiming openings over ground with recency < 0.5. Basis: the AGENTS fog rule. Measured: DET-HIDDEN, DET-UNWATCHED and the source pin DET-PIN.
  - no-guardrail: a pure exclusion with nothing to trade off. Its cost (fewer openings) is visible as `suppressedUnwatched`.
  - no-read-path: measuring it at runtime needs an omniscient comparison that producers may not make. The instrumentation package in this wave is the source pin plus `suppressedUnwatched` on `TacticalOpening`.
- **criterion-exploit-outcome** — Over at least three smoke battles with contact, ≥ 40 % of launched exploits end `Success`. Basis: an estimate; there is no prior data, and smoke tunes it.
  - guardrail: ≤ 25 % end `asset-losses`, and 0 `line-crisis` ends where the crisis sector is the one the asset left (integrity breach).
  - read path: `TacticalExploit` end rows.
- **criterion-exploit-rate** — Per AI side, 1-12 launches per battle-day with contact. Basis: an estimate (a few local strokes per corps per day; § 3). Measured: `TacticalExploit` launch rows over the smoke battles.
  - guardrail: concurrent live exploits never exceed `MaxConcurrentExploits` (OPP-CONC, and the row's `live` field).
  - read path: `TacticalExploit` launch rows.
- **criterion-reserve-stagger** — 0 Exploit or odds release passes that release more than one Reserve child within one `releaseInterval`, and 0 non-crisis releases below the floor. Basis: requirement-staggered-reserve-release. Measured: `TacticalReserveRelease` rows (`reason`, `held`, `floor`).
  - guardrail: a crisis still releases its nearest reserve on the first allocation that sees it (RES-CRISIS-LATENCY).
  - read path: `TacticalReserveRelease` rows.
- **criterion-cavalry-discipline** — With the steer on, 0 vanilla cavalry charges at a target hidden from the side or at a supported battery. Basis: requirement-fix-cavalry-strike. Measured: `TacticalCavalryStrike` rows (every allowed row has a passing reason).
  - guardrail: the refused share of vanilla's candidates is ≤ 80 % per battle, and at least one allowed charge in any battle where AI cavalry met shaken or unsupported targets, so cavalry does not go passive.
  - read path: `TacticalCavalryStrike` rows.
- **criterion-frame-budget** — `tactical.producer.opportunity` p95 ≤ 0.5 ms and max ≤ 2.0 ms per side per tick in a battle with about 500 regiments a side. Basis: the BC13 (now BC14) estimate of about 2-4 ms for all tiered producers per side per cycle. This producer must stay a small share of that.
  - guardrail: the `tactical.patch.battle-core-arbiter` scope's p95 rises by ≤ 10 % with the flag on against off.
  - read path: `TelemetryPerf` scope records.
- **criterion-zero-alloc** — 0 bytes allocated per opportunity tick after warm-up. Basis: Global Constraints. Measured: OPP-ALLOC (`GC.GetAllocatedBytesForCurrentThread`).
  - no-guardrail: the criterion is itself a bound.
  - no-read-path: Mono exposes no per-call allocation counter in the game. The instrumentation package in this wave is the harness test, run in the gate list.
- **criterion-player-never-moved** — 0 exploit orders planned onto a `PlayerOwned` unit, and 0 W&L player command nodes deciding. Basis: the standing rule. Measured: OPP-PLAYER and the `playerSkips` field (executor `PlayerOwned` skips for `exploit-*` reasons).
  - no-guardrail: a zero bound.
  - read path: `TacticalExploit` rows' `playerSkips`, plus the `[OrderArbiter]` `topSkip`.
- **criterion-defender-no-offense** — 0 launches with a kind other than `Penetration` on a Defender side, and 0 `Penetration` launches unless the counterstroke is approved and its flag is on. Basis: the standing rule. Measured: OPP-DEF and the rows' `stance`.
  - no-guardrail: a zero bound.
  - read path: `TacticalExploit` rows.
- **criterion-stance-cooldown** — With the fix on, vanilla's own stance changes per pool group are ≤ 1 per `updatetimeforresettingofaistance` (1 game-h). Today vanilla can change every macro cycle (0.015 h). Basis: `battleprefs` 804-805. Measured: `TacticalStanceCooldown` rows.
  - guardrail: the count of vanilla weak-group defend or screen changes delayed beyond 0.25 h is reported on the same row, and the answer to question-stance-cooldown-valves bounds it.
  - read path: `TacticalStanceCooldown` rows.

---

## 13. (E) Tests, plants, smoke, coverage matrix (frozen test plan)

Test file `CommanderOpportunityTests.cs` (`internal static class CommanderOpportunityTests { public static void Run() }`, partial files allowed) with `<Compile Include>` lines for each new pure file. Red first: PERS-THRESH, RES-STAGGER, DET-GAP and OPP-LAUNCH must fail against today's code before any implementation.

### Acceptance scenarios and cases

- **S1 Personality.**
  - PERS-DERIVE: the § 4 formulas, at fixture points and at the clamps.
  - PERS-REG: every registry entry returns its list vector.
  - PERS-FALLBACK: an invalid index gives `Default` with rank bias.
  - PERS-PIN: a second `Add` for the same index does not change the entry.
  - PERS-BANDS: the shipped-roster fixture shares (criterion-cautious-band-reach).
  - PERS-EXEMPLARS: derived from the roster's stat rows, McClellan's `agg01` < 0.30 and Jackson's ≥ 0.70 (the registry overrides both in the game; this pins the formula).
  - PERS-DUTY: `status != 0` halves the chance.
  - PERS-THRESH: Meeting/Attacker threshold 1.2 / 1.0 / 0.8 at `agg01` 0 / 0.5 / 1, Defender unchanged, the initiative divisor after.
  - PERS-CASUALTY: the casualty threshold reads CasualtyTolerance, with the 0.5 value unchanged.
  - PERS-DEAD: source pins that `AggressionBias01` and `WeakPointConfirmed` are gone and that no `commanderInitiative01: 0.50f` literal remains.
  - PERS-ARMY: an unlisted army commander gets `Derived`.
  - PERS-NOFACTION: no faction vector added to `Derived`.
- **S2 Detection.**
  - DET-GAP: a 300 m watched gap is emitted, with aim and width.
  - DET-GAP-SECOND-LINE: a unit behind the gap closes it.
  - DET-GAP-NARROW: 140 m is not a gap.
  - DET-UNWATCHED: the same gap at recency 0.4 is suppressed and counted.
  - DET-HIDDEN: a hidden enemy (not an observation) never closes or opens anything.
  - DET-REMEMBERED-LOW: confidence 0.4 is ignored.
  - DET-SHAKEN: 0.4 share at morale 0.44 emits; 0.46 does not.
  - DET-SHAKEN-REMEMBERED: Remembered morale is not counted as shaken.
  - DET-FLANK-RIVER: a river 70 m beyond the end anchors; 90 m does not.
  - DET-FLANK-CREST: a crest within 50 m anchors.
  - DET-FLANK-REFUSED: 45° anchors.
  - DET-FLANK-SCREEN: cavalry beyond the end anchors.
  - DET-BATTERY: unsupported emits; infantry at 240 m blocks it; a garrisoned battery emits nothing.
  - DET-CLUSTER: vanilla's 0.65 and 1.3 rules; any qualifying cluster, not only the nearest.
  - DET-IDENTITY: matched within 100 m keeps id and `FirstSeen`; confirm after 0.02 h; lost after 0.05 h.
  - DET-GEOM: the fixture (five visible infantry at lateral 0-800 m, every 200 m, width 150, depth 600; a visible battery at lateral 1100, depth 700; no river; picture not ready; recency 0.8, and 0.3 beyond lateral 1000). It yields one `FlankInAir` at the left end (aim at lateral −225), a right end anchored by the battery screen, and the battery suppressed as unwatched (mean recency 0.45). Edge distances against centre distances are each pinned by one case.
  - DET-PIN: a source pin that `OpeningDetector` reads `EnemyRecords` only through a Visible observation's row.
  - DET-PEN: only when the counterstroke is approved.
- **S3 Decision.**
  - OPP-LAUNCH: the full path for a gap and an infantry brigade launches.
  - OPP-CAV-STEADY: cavalry never chosen against formed steady infantry.
  - OPP-CAV-MORALE: a cavalry asset at morale 0.70 is ineligible (5047 gate).
  - OPP-GAP-FIT: a gap narrower than 0.6 × the asset's width is rejected.
  - OPP-REACH: audacity scales reach between 450 and 750 m (infantry).
  - OPP-ODDS: required odds scale with `agg01`.
  - OPP-CONF: the confidence threshold scales with caution.
  - OPP-FLOOR: the floor share formula and the `n <= 2` rule.
  - OPP-INTEGRITY: removing a line brigade that leaves a 200 m own gap facing an enemy within 500 m fails.
  - OPP-MAIN: the main-effort rule and its in-sector exception.
  - OPP-RIVER: a river on the segment fails; a ford passes.
  - OPP-DEF: Defender sides never launch other kinds.
  - OPP-CONC: the side cap, the per-node cap and claimed openings.
  - OPP-ORDER: deepest first; ties by id.
  - OPP-ROLL: the hash is deterministic and in [0, 1); a miss marks hesitation for 0.1 h.
  - OPP-PLAYER: a `PlayerOwned` descendant excludes the asset, and the player's command node never decides.
  - OPP-FEUD: a `Feud` asset is never eligible.
  - OPP-ELIG: one case per eligibility rule.
  - OPP-ARMY-DECIDES: with no corps or division nodes, the army node decides.
  - OPP-CONTRACT: launch iff `roll < chance`; the last failing reason is returned, or `no-pair`; the producer writes the hesitation mark and the claim, never `Decide`; the nested-asset main-effort rule; the roll's FNV-1a vector.
- **S4 Lifecycle and override.**
  - LIFE-PHASES: Advance → Strike → Consolidate → Recall on a fixture.
  - LIFE-CAV-STANDOFF: the cavalry aim is 60 m short.
  - LIFE-PURSUE: cavalry with audacity ≥ 0.6 pursues ≤ 400 m; infantry never.
  - LIFE-ABORTS: one case per `ExploitEnd` reason.
  - LIFE-CLOCK: a night-freeze fixture.
  - LIFE-AIM: the aim follows a perceived battery, is frozen for a Gap, and the cavalry override target is the 60 m stand-off.
  - LIFE-PRECEDENCE: two aborts on one tick resolve by the listed order; `opening-lost` never fires after Strike; `timeout` never fires in Consolidate.
  - OVR-POSTURE: the posture producer submits the exploit target for every subtree node (AI pool and leaf), skips the cavalry follow rewrite, keeps the flank emergency as an abort, and the Move has no order delay.
  - OVR-FOLLOW: the asset's descendants get no Move or Formation while the asset exploits.
  - OVR-LEAF: a leaf brigade asset runs through `PosturePipeline.Run` with the override, not the slot path.
  - OVR-FEEDBACK: the flank emergency sets `ExploitFeedback`, and the next opportunity tick aborts with `flank-threat`.
  - OVR-RECALL: after Recall the ledger order is resolved on the next tick.
  - OVR-SAFETY: a withdrawal Safety claim wins and aborts.
  - OVR-PLAY: the division play loses to the exploit order.
  - OVR-MANEUVER: mutual exclusion with BC11.
  - OVR-STANDDOWN: stand-down clears the table.
  - OVR-NEWDAY: a new battle day clears it.
- **S5 Reserve release.**
  - RES-STAGGER: four reserves, Exploit phase, `agg01` 0.5: one release per 0.1 h.
  - RES-FLOOR: `R0` 4 at floor 0.30 keeps 2 → at most 2 released.
  - RES-SINGLE: `R0` 1 may go.
  - RES-CRISIS-ONE: one per crisis sector.
  - RES-CRISIS-LATENCY: released on the first allocation.
  - RES-PERSIST: a released child stays released across re-runs while the trigger holds.
  - RES-REVERT: the trigger stops → revert and clear.
  - RES-EXPLOITING-SKIPPED: the allocator skips a group with a live exploit.
  - RES-TIE: reserves equidistant from the main effort release the lower index first.
  - RES-CRISIS-HELD: a crisis release counts as not held and blocks the stagger at the floor (fixture: four reserves at sectors 0, 1, 3, 4, main effort 2, Exploit at 1.00, crisis in sector 4 at 1.06 → B at 1.00, D crisis at 1.06, none at 1.10 and 1.21).
- **S6 Bug fixes.**
  - FIX-STANCE: a re-stamp of exactly the groups vanilla changed in the call, to `now + 2T`, which holds `T`.
  - FIX-STANCE-TRACE: Vanilla changes to 2 at raw 3.00 with T = 1.0 → re-stamp 5.00, held until 4.00. A Whiskey write at 3.20 → 4.45, which vanilla may re-decide after 3.45.
  - FIX-STANCE-FEUD: the feud instance is untouched.
  - FIX-STANCE-WHISKEY: Whiskey writer stamps are untouched, and `ReleaseStamp` still re-arms.
  - FIX-STANCE-PIN: the 4264 comparison text is unchanged in the decompile pin.
  - FIX-RENEW: `ChargeDoctrine` reads `timetorenewaichargecheck`.
  - FIX-CAV: one case per `CavalryStrikeCheck` reason; fail-open with no snapshot; the exploit target replaces vanilla's pick only within 90 m.
  - FIX-CAV-MAP: a target not Visible in the latest snapshot is refused `hidden`; a player-owned charger and the feud instance are untouched; the exploit target is substituted only from the charger's own candidate list and only when vanilla's 5050 post-filter passes.
  - FIX-CAV-PIN: a single call site at 5049 with `maxdistancecavalryattack` alone.
  - FIX-CAV-SCOPE: the flag is cleared by the `Priority.First` Postfix and by the Finalizer when vanilla throws.
  - If approved: FIX-MACRO (real IL or port: the upgrade turns `num4` 1 → 0; the site count is exact or unchanged), FIX-RANDOM (real IL: both constants, the site count is exact or unchanged), and both rewriters in `TranspilerLabelTests`.
- **S7 Performance.**
  - OPP-ALLOC: 0 bytes after warm-up for detection (400 samples), decisions (12 commanders) and lifecycle (3 exploits).
  - OPP-TIME: a harness timing smoke on 1,000 samples, informational.

### Mutation plants (each must make a listed test fail)

Each plant is listed with the test that must catch it:

- the watched-recency test removed (DET-UNWATCHED);
- the second-line corridor removed (DET-GAP-SECOND-LINE);
- `GapMinMeters` changed to 100 (DET-GAP-NARROW);
- `Remembered` morale counted as shaken (DET-SHAKEN-REMEMBERED);
- the river anchor reach set to 100 (DET-FLANK-RIVER);
- the refused-end test removed (DET-FLANK-REFUSED);
- the 0.65 cluster rule at 0.8 (DET-CLUSTER);
- the cavalry steady-infantry exclusion removed (OPP-CAV-STEADY);
- the 0.72 cavalry morale gate removed (OPP-CAV-MORALE);
- the floor formula sign flipped on caution (OPP-FLOOR);
- the line-integrity check removed (OPP-INTEGRITY);
- the main-effort exception removed (OPP-MAIN);
- the Defender check removed (OPP-DEF);
- the deepest-first order reversed (OPP-ORDER);
- the hesitation marker removed (OPP-ROLL);
- the order-delay exemption removed (OVR-POSTURE);
- the cavalry-follow skip removed (OVR-POSTURE);
- the Safety abort removed (OVR-SAFETY);
- the stagger interval ignored (RES-STAGGER);
- the floor ignored (RES-FLOOR);
- the crisis one-per-sector rule removed (RES-CRISIS-ONE);
- the re-stamp `2T` written as `T` (FIX-STANCE);
- the cavalry support test removed (FIX-CAV);
- fail-open changed to fail-closed (FIX-CAV);
- the duty factor removed (PERS-DUTY);
- the threshold band replaced by the cap (PERS-THRESH).

### Smoke items (added to the BC7 checklist; item numbers assigned when the plan section is written)

1. `[TacticalCommanderRoster]` and `TacticalCommanderPersonality` rows: the share by source (criterion-personality-coverage) and the `agg01` bands.
2. `[Opportunity] first opening` in a contact battle. `TacticalOpening` counts by kind, and the `suppressedUnwatched` count before and after the BC9 recon has run.
3. `[Opportunity] first exploit launched`: watch the asset leave and strike, and confirm it is recalled to its ledger position. `TacticalExploit` end rows (criterion-exploit-outcome, criterion-exploit-rate).
4. A battle in which the W&L player holds a brigade or division: no exploit order touches the player's units (`playerSkips` 0), and the player's own command never decides.
5. A Defender-side battle: no launches other than `Penetration` (criterion-defender-no-offense).
6. `TacticalReserveRelease` rows during the army's Exploit phase: one release per interval, the floor held (criterion-reserve-stagger).
7. `TacticalCavalryStrike` allowed and refused rows, with `[CavalryStrike] first refused` (criterion-cavalry-discipline).
8. `TacticalStanceCooldown` rows (criterion-stance-cooldown): vanilla's change rate on one side with the fix on and off.
9. Scopes `tactical.producer.opportunity` and `tactical.patch.battle-core-arbiter`, with the flag on and off (criterion-frame-budget).
10. Session B (W&L subordinate career): an AI division commander above the player exploits with sibling brigades, and the player receives no order for it.

### Coverage matrix (result column blank until prove)

| Requirement | Spec section | Test cases | Result | Defect / concession |
|---|---|---|---|---|
| requirement-commander-table | § 4 | PERS-PIN, PERS-FALLBACK | | |
| requirement-registry-first | § 4 | PERS-REG, PERS-FALLBACK | | |
| requirement-derive-from-stats | § 4 | PERS-DERIVE, PERS-NOFACTION, PERS-EXEMPLARS | | |
| requirement-cautious-reachable | § 4 | PERS-BANDS | | |
| requirement-commander-duty | § 4 | PERS-DUTY | | |
| requirement-army-vector-from-table | § 4 | PERS-ARMY | | |
| requirement-attack-threshold-band | § 4 | PERS-THRESH | | |
| requirement-personality-live-inputs | § 4 | PERS-CASUALTY, OPP-CONF, OPP-FLOOR, OPP-REACH | | |
| requirement-dead-personality-paths | § 4 | PERS-DEAD | | |
| requirement-enemy-samples | § 5 | DET-HIDDEN, DET-REMEMBERED-LOW, DET-PIN | | |
| requirement-watched-ground | § 5 | DET-UNWATCHED | | |
| requirement-detect-gap | § 5 | DET-GAP, DET-GAP-SECOND-LINE, DET-GAP-NARROW | | |
| requirement-detect-shaken | § 5 | DET-SHAKEN, DET-SHAKEN-REMEMBERED | | |
| requirement-detect-flank-in-air | § 5 | DET-FLANK-RIVER, DET-FLANK-CREST, DET-FLANK-REFUSED, DET-FLANK-SCREEN | | |
| requirement-detect-unsupported-battery | § 5 | DET-BATTERY | | |
| requirement-detect-isolated-cluster | § 5 | DET-CLUSTER | | |
| requirement-detector-geometry | § 5 | DET-GEOM | | |
| requirement-opening-identity | § 5 | DET-IDENTITY | | |
| requirement-detect-penetration | § 5 | DET-PEN | | |
| requirement-opportunity-producer | § 6 | OVR-POSTURE, OVR-STANDDOWN | | |
| requirement-deciding-commanders | § 6 | OPP-ORDER, OPP-PLAYER, OPP-ARMY-DECIDES | | |
| requirement-asset-eligibility | § 6 | OPP-ELIG, OPP-FEUD, OPP-CAV-MORALE, OPP-PLAYER | | |
| requirement-asset-choice | § 6 | OPP-CAV-STEADY, OPP-GAP-FIT, OPP-LAUNCH | | |
| requirement-exploit-reach | § 6 | OPP-REACH | | |
| requirement-local-odds | § 6 | OPP-ODDS | | |
| requirement-exploit-thresholds | § 6 | OPP-CONF | | |
| requirement-risk-check | § 6 | OPP-INTEGRITY, OPP-MAIN, OPP-RIVER, OPP-DEF, OPP-CONC | | |
| requirement-reserve-floor | § 6 | OPP-FLOOR | | |
| requirement-exploit-chance | § 6 | OPP-ROLL, PERS-DUTY | | |
| requirement-decision-contract | § 6 | OPP-CONTRACT, OPP-LAUNCH, OPP-ROLL | | |
| requirement-exploit-override | § 6 | OVR-POSTURE, OVR-PLAY, OVR-MANEUVER, OVR-SAFETY, OVR-FOLLOW, OVR-LEAF, OVR-FEEDBACK | | |
| requirement-runtime-wiring | § 6 | PERS-ARMY, OVR-FOLLOW, OVR-LEAF, FIX-STANCE-TRACE, FIX-STANCE-FEUD | | |
| requirement-exploit-lifecycle | § 6 | LIFE-PHASES, LIFE-CAV-STANDOFF, LIFE-PURSUE, LIFE-ABORTS, LIFE-CLOCK, OVR-RECALL, OVR-NEWDAY | | |
| requirement-lifecycle-contract | § 6 | LIFE-AIM, LIFE-PRECEDENCE, LIFE-PHASES | | |
| requirement-staggered-reserve-release | § 6 | RES-STAGGER, RES-FLOOR, RES-SINGLE, RES-CRISIS-ONE, RES-CRISIS-LATENCY, RES-PERSIST, RES-REVERT, RES-EXPLOITING-SKIPPED, RES-TIE, RES-CRISIS-HELD | | |
| requirement-fix-stance-cooldown | § 7 | FIX-STANCE, FIX-STANCE-WHISKEY, FIX-STANCE-PIN | | |
| requirement-fix-charge-renew | § 7 | FIX-RENEW | | |
| requirement-fix-macro-upgrade | § 7 | FIX-MACRO | | |
| requirement-fix-artillery-side | § 7 | FIX-RANDOM | | |
| requirement-fix-cavalry-strike | § 7 | FIX-CAV, FIX-CAV-PIN, FIX-CAV-SCOPE, FIX-CAV-MAP | | |
| requirement-fix-charge-weight | § 7 | (decision only; no test) | | |
| requirement-fix-cavalry-morale-label | § 7 | DET-SHAKEN (reuses the value) | | |
| requirement-fix-outflanked-flag | § 7 | FIX-CAV (outflanked case) | | |
| requirement-fix-dead-outflank-retreat | § 7 | (decision only; no test) | | |
| requirement-frame-budget | § 11 | OPP-TIME, smoke 9 | | |
| requirement-zero-alloc | § 11 | OPP-ALLOC | | |
| requirement-config | § 11 | config-audit 0 findings | | |
| requirement-telemetry | § 11 | smoke 1-9 | | |

---

## 14. Decisions and rejected alternatives

- **D-A1 Pinned temperament, live duty.**
  - Problem: a commander's stats change mid-battle (out of duty, staff perks).
  - User effect: a commander behaves consistently all day, and a hit commander's division hesitates.
  - Chosen: pin the vector; scale the chance by vanilla's own out-of-duty factor.
  - Rejected: recompute from the live getters each decision. A staff perk or the experience factor would change temperament, and the getters fold in parent influence, which is not this commander's trait.
- **D-A2 No faction vector on derived commanders.**
  - Problem: counting the alliance difference twice.
  - Effect: derived means stay at or a little below today's defaults (the measured means are in requirement-cautious-reachable).
  - Rejected: `Derived + FactionProfiles`, which would push the CSA mean to about 0.66 and shift army behaviour.
- **D-A3 Threshold band centred on today's value.**
  - Problem: the cap made personality inert for attackers.
  - Effect: a mid commander is unchanged, an aggressive one attacks at 0.8 × parity, a cautious one waits for 1.2 ×.
  - Rejected: (a) remove the cap and restore 1.08-1.56. This brings back the near-even meeting deadlock the cap fixed (BR-H1). (b) Keep the cap. Personality stays inert.
  - Known: initiative counts twice for derived commanders; the range stays within [0.51, 1.01].
- **D-C1 The exploit rides the posture pipeline.**
  - Problem: the exploit must move groups with routing, facing, formation, fire control, the level-of-detail tiers and W&L fan-out, through the one write path.
  - Effect: an exploiting brigade moves like any other Whiskey brigade.
  - Chosen: the opportunity producer decides and publishes a table, and the posture producer executes it as an order override (BC11's maneuver override reaches the same orders through `CommandDoctrineAssignment`).
  - Rejected: (a) the opportunity producer submits its own Executor-priority Moves. That makes two Executor sources on one group, a tie broken only by hysteresis, and duplicates the posture pipeline. (b) A new arbiter priority above Executor. That would beat the posture producer's own Safety-adjacent rules and change the ladder for every producer.
- **D-C2 No order delay on exploit Moves.**
  - Problem: courier delay and cadence outlast an opening.
  - Effect: the stroke goes when the commander decides.
  - Chosen: vanilla's own isolated-target move is immediate (8188). Hesitation is modelled by the decision interval and the roll.
  - Rejected: courier delay as friction. It would be counted twice with the roll, and the opening is gone by the time the order lands.
- **D-C3 Skip the cavalry follow rewrite under an exploit.**
  - Problem: `TryCavalryFollow` can turn an `AttackObjective` into `GetAway` or a screen.
  - Effect: a cavalry stroke is not cancelled by a second, coarser filter.
  - Chosen: the exploit already checked its target; the flank emergency still aborts.
  - Rejected: treat a follow `GetAway` as an abort. Two filters with the same 250 m support radius but different target picks (closest visible against the exploit target) would oscillate.
- **D-C4 Vanilla executes the cavalry charge.**
  - Problem: Whiskey has no executor channel for `AssignChargeTarget`.
  - Effect: cavalry charges look and resolve exactly like vanilla's.
  - Chosen: position at 60 m, attack stance, the steered vanilla cycle-7 charge.
  - Rejected: a new executor channel calling `AssignChargeTarget`. That is a new vanilla writer, and the odds bypass would be Whiskey's own.
- **D-C5 Deepest commander decides first.**
  - Problem: two echelons want one asset.
  - Effect: the man on the spot acts, and his superior uses what is left.
  - Rejected: senior first. A corps commander would take divisions' brigades away from local opportunities, against the user's "each commander his own".
- **D-C6 Deterministic hash roll.**
  - Problem: chance must be testable and allocation-free.
  - Rejected: `UnityEngine.Random`, which is not pure, untestable and shared with vanilla's stream.
- **D-C7 Staggered release with a floor, crisis exempt.**
  - Problem: release-all empties the reserve on one trigger.
  - Effect: the army commits reserves one at a time and keeps a final reserve (Jomini).
  - Rejected: a fixed count per phase. It ignores the army's size and the commander.
- **D-D1 Re-stamp rather than fix the comparison.** The inverted comparison carries Whiskey's hold, so a Transpiler fix would break `TacticalStanceOwnership`. The re-stamp gives the intended hold with no skip and no Transpiler.
- **D-D2 The cavalry steer is a result filter, and it fails open.** A filter needs no consent (the `SkirmisherScreenDistancePatch` precedent). Fail-open matches BC2's range-only fallback.
  - Rejected: a Prefix skip of `AdjustCavalryBehavior`. It would also skip the mount and dismount logic (the dismount at 5068 and the remount at 5074, in the same loop).

---

## 15. Amendments owed to the plan (the integrator writes them; this spec does not edit the plan)

1. **Renumbering.** The plan's "BC13 — battle AI level of detail" becomes BC14, and "BC14 — formations, slots and lanes" becomes BC15. These must be updated with it: their cross-references (for example BC15's "BC13's tiers" and "BC13 promotion inputs"), the dependency diagram, the BC10-BC12 rules paragraph naming BC13, and the smoke item ranges. A new "BC13 — commander opportunity and exploitation" section is written from this spec.
2. **BC14 (level of detail).**
   - A group with a live exploit, or under an exploit override, is never `Distant`. `ITaskLookup.HasLatchOrManeuver` returns true for it.
   - Creating or ending an entry is a promotion: trigger (c), the order target changed. The posture producer's resolved target is the exploit aim.
   - `OpportunityIntentProducer` is not tiered: it acts on openings at full rate, and its per-commander interval is its own cadence.
   - BC14's equivalence test gains a case: an exploit launch on a group that was `Distant` the tick before.
3. **BC15 (formations).**
   - `GroupFormationDoctrine.Target` treats an exploit Advance or Strike as `AttackObjective`, its assault-range row included. Consolidate is `HoldObjective`.
   - If the counterstroke is approved, `CommandTaskType.Counterstroke` needs a row and a mapping in `TacticalCommandTaskToStanceMapper` (→ 3).
   - BC15's slot fitness is not used by BC13, which merges first. Asset choice reads BC8 troop quality only through `GroupMorale` and `LossFraction`.
4. **Producer order.** `BattleProducerRegistry.Order` gains `"Opportunity"` before `"Posture"`, and the `[BattleCore] producers count=` line becomes 13.
5. **Patch catalog.** The new patches are named here without ordinals; ordinals and bug ids are assigned at merge to `main`.

---

## 16. Not verified (runtime-only claims; each is a smoke item)

- Vanilla's cycle-7 charge fires on a target 60-90 m from a Whiskey-positioned cavalry regiment at attack stance with morale > 0.72 (smoke 3, 7).
- The posture producer's exploit Moves reach vanilla without a courier and are `Moved` at the next observation (smoke 3).
- The `ExploitTable` override reaches every subtree node, including leaf brigades outside the AI pool (smoke 3).
- `Commander.status` is the in-battle hit state, and `commander` indices do not change in battle (§ 2.1 item 2, read from code; smoke 1 records `status` on the personality row when a commander is hit).
- The re-stamp holds vanilla's group stances for 1 game-h, and Whiskey's yields still release them (smoke 8).
- `GetClosestEnemyUnitReg` returns targets the side cannot see (expected from 123723 reading the range lists; smoke 7 counts `hidden` refusals).
- Frame cost (smoke 9).

---

## 17. Open questions

- **question-defender-counterstroke** (gate: user). Should a Defender side get a bounded local counterstroke against a `Penetration`? The mechanism is a new task `CommandTaskType.Counterstroke` that `TacticalDecisionDoctrine.TaskForStance` passes for a Defender. Today its `switch` turns `AttackObjective` / `SupportAttack` / `FixEnemy` into `HoldObjective` and `Probe` into `Screen`. `PostureStanceGuard` applies that switch. The counterstroke's aim is limited to 400 m behind the own front-line trace, its asset to a second-line or reserve brigade, and it always recalls.
  - Vanilla precedent: `CheckFlankMoves` flanks in defend stance (trigger 2.2, `battleprefs` 676), and `CheckForSeparatedEnemy` attacks isolated clusters at any `macroai` but 3.
  - History: Fredericksburg's reserves restoring Jackson's line, Hancock's Willard and 1st Minnesota (§ 3).
  - Recommendation: approve, behind `Enable Defender Counterstroke`. If declined, `Penetration` is not detected and a Defender never launches.
- **question-stance-cooldown-valves** (gate: user). With requirement-fix-stance-cooldown, vanilla's own weak-group defend or screen change (4244-4246, 4252-4254) can wait up to 1 game-h after vanilla's previous change of that group. That is the `battleprefs` intent, but it makes vanilla's own valves slower. `TacticalStanceOwnership` yields to those valves on the assumption that vanilla re-decides every cycle. Options:
  - (a) apply the intended rule exactly;
  - (b) exempt transitions to defend or screen from the restored hold (no vanilla text supports this exemption).

  Recommendation: (a), with criterion-stance-cooldown's guardrail row measuring the delays in smoke.
- **question-macro-upgrade-mechanism** (gate: user). Approve requirement-fix-macro-upgrade's (a) Transpiler or (b) the non-skipping Prefix plus ported Postfix, or decline. Recommendation: (a). It is a 4-6 instruction change with the BC15 `CavalryFlipIl` safety pattern, against a 20-line port that must track the decompile.
- **question-random-range-mechanism** (gate: user). Approve the one-constant Transpiler for `AssignReserves` 7092 and the `Count − 1` removal at `CheckUseOfReserves` 6116 (BUG-TAC-008), or decline. Recommendation: approve both (the BC15 A3 precedent).
- **question-separated-enemy-steer** (gate: user). For Whiskey-owned groups, keep vanilla's omniscient `CheckForSeparatedEnemy` from sending the group. The minimal mechanism is a non-skipping Prefix that sets `aigroup.aiassaultmove = true` (the routine returns at its first test, 8060), with the value restored in a Finalizer. That is in effect a per-group skip, so it needs consent. Recommendation: approve, because Whiskey's `IsolatedCluster` opening replaces it fog-honestly for those groups. Default if declined: leave vanilla alone (today's tug, re-asserted by the posture pipeline).
- **question-dead-outflank-retreat** (gate: user; a departure from "Fix them"). `CheckIfOutflanked` is a dead routine, not a broken one. This spec does not revive it (requirement-fix-dead-outflank-retreat). Confirm, or ask for revival as new behaviour (omniscient `flankstrength`, so it would need a fog-honest rewrite).
- **question-casualty-tolerance-basis** (gate: crit, then smoke). Leadership-driven CasualtyTolerance (requirement-derive-from-stats) is the weakest mapping: no vanilla code ties leadership to loss acceptance. The alternative is `0.5 × Aggression + 1.0 × (leadership − 0.55)`. Smoke reads the `asset-losses` share by CasualtyTolerance band before either is kept.

---

## 18. Out of scope

- The army-level playbook fit weighting (the `PersonalityFit` squash noted in the research). Army plan selection is a separate tuning item.
- The `DifficultyPersonalityModifier` and the campaign-layer personality.
- Orders or dispatches asking the W&L player to exploit. The player's command never decides, and a W&L suggestion channel would be its own slice.
- Pursuit beyond `PursuitMeters`, and an army-level pursuit doctrine.
- Fog-honest rewrites of vanilla's own AI for groups vanilla owns: the hotspot map and vanilla's cavalry candidates outside the steer.
- Infantry squares.
- New patches on `CheckFlankMoves` or `AssignReserves` beyond the bug fixes in § 7.

<!-- author-sweep: applied purpose-advantage-measurement, fit-criteria, guardrails, read-paths, roles, lifecycle, exceptions, concurrency, decision-records; n/a: none -->
