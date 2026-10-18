The two armies were never at Richmond. They were the screen between Washington and Richmond: the Army of the Potomac at Manassas and the Army of Virginia at Fredericksburg. Whiskey's own mover sent both into the Shenandoah Valley, aimed at a West Virginia objective. It did so because the CSA planner saw almost none of the Union army and computed zero threat to Richmond. At the same time, a legacy patch (#25) has switched off vanilla's own interception of enemy advances for both sides.

I read everything and edited nothing. The newer live data (a save at 23:59, game date 1861-06-27) is copied to `/tmp/claude-1000/-home-onebodyamerica-Projects-whiskey-realism-mod/df30b0bc-abcb-47a7-b6ee-c8362e2ce5ff/scratchpad/` (`save2/`, `live/campaign2.jsonl`).

## Q1: where everything is, and who moved what

One correction to your reading: in the trace, `30@6` means objective 30 in **sector** 6, not record 6.

**Positions** (from the save's `IIPsTowns.dat`; the capital flags are in the same file):

| Place | Position | Distance to Richmond |
|---|---|---|
| Richmond (CSA capital) | 1263.6, -1010.9 | – |
| Washington D.C. (Union capital) | 1349.9, -631.6 | 389 |
| Manassas Junction | 1245, -681 | 331 (116 from Washington) |
| Fredericksburg | 1247, -803 | 209 |
| Winchester | 1107, -555 | 482 |
| Martinsburg | 1140, -486 | 540 |

**Objective 30** is "West Virginia: Restore Unity in Virginia" (`Config/campaignobjectives.dat:602-611`; towns Charleston, Grafton, Lewisburg, Romney). The mover's sidecar record gives the 30@6 target as (982.6, -515.7), probably Romney (the only one of its towns near that point), 569 from Richmond.

**Who moved what** (sidecar `planner.moves` plus the `Director:trace` positions):

| Formation | Move | Who | Evidence |
|---|---|---|---|
| Army of the Potomac (carries the player) | Manassas → Winchester point (1101, -554) by 06-24 | Whiskey mover, MainEffort | 06-22 main=East/30, `issued=1`; 06-23 the target was out of supply reach (r=0.00), so the target became the reach-limited point (`TheaterPlanner.cs:259-269`) |
| Army of the Potomac | Winchester → Martinsburg, 06-24/25 | **Vanilla** (exact job not identified) | main=none both days, no CSA mover row |
| Army of the Potomac | Martinsburg → toward Romney | Whiskey mover, decision 2 | 06-26 `switch=initial`; sidecar `tx=982.6 tz=-515.7 issuedDay=679527 objectiveId=30` |
| Army of Virginia | Fredericksburg → Winchester (1101.3, -553.8) | Whiskey mover, **DepotBuilder** | sidecar `issuedDay=679524` (06-23), `objectiveId=-1`; 06-23 `depot=1`, `issued=2`; the target is exactly the depot site the plan puts at that reach-limited point (`LogisticsGate.DepotTasks.cs:31`) |

Today nothing stands between Washington and Richmond. Only the Army of the Peninsula (near Yorktown), the Department of Norfolk and the Army of North Carolina remain near Richmond. The player's own intel marks put a Union 1st Division at (1198, -614) on Jun 24: 82 from Manassas, south of the Potomac, in the gap.

## Your follow-up: does detection miss them, or does the threat ignore them?

Both.

- **The threat formula ignores them by distance.** The line that zeroes it is `ReserveRules.cs:55`, `if (live <= 0f || straight >= range) continue;`. The range is not 275: this W&L scenario's own `Campaigns/002/A/campaignprefs.txt:1393` overrides the relocation range to **200** (the game loads the scenario folder's file, decompile 55125). Washington is 389 from Richmond and the Fort Monroe army is 269, so nothing ever counts. Vanilla uses the same 200 (11725), so vanilla's capital defence would not count Washington either.
- **Detection misses what the player sees.** The CSA side's rule reads only units visible at the moment of the daily review (`EnemyDetectionRuntime.cs:427`). What the player sees are vanilla's *spotting marks*: reports created only while a unit is hidden in fog, at its true position plus noise, dated "last spotted" (`SpottedEnemy.UpdateMarks`/`AddMark` 142700-142900, 143026). The save's `spottings.dat` shows them:
  - Army of Occupation, 207 marks, last at (433, -553), Jun 26 — the "10,000 in the West".
  - 1st Division at (1346, -671) on Jun 22, near Alexandria.
  - 1st Division at (1198, -614) on Jun 24.
- **None of those are in the CSA beliefs.** On 06-23 the beliefs held only the 2nd Division (6653, near Winchester) and the Department of Virginia (5214, Fort Monroe). On 06-27 they also hold two Union army HQs at strength 0, because an HQ's direct strength excludes its divisions.

So the foresight design has to cover both: armies within some days' march of the capital, and armies on the axis between the capitals, not only those inside a sector or inside the 200 radius.

## Root causes, ranked by how much of the complaint each explains

**1. One sighting both unlocked the push and dissolved the screen.**
- The 2nd Division sighting is 165 from Manassas, so it counts as fresh enemy information for the Manassas sector. That switches off the "presumed enemy" for the whole sector (`TheaterPlanner.cs:767`), so the unseen Washington army counted for nothing. The hold need fell to about max(0.6 × 6.6k, 5000).
- The same sighting is the nearest believed army to that sector, and it lies 156 from the Romney target. The rule that frees a sector's hold when the target is "the army it faces" (`TheaterPlanner.cs:989`, `1134-1137`; nearest belief from `ReserveRules.NearestLandBelief`) therefore let the 19.4k Army of the Potomac leave.
- The same sighting also marked the target as scouted (line 1290), giving `est=6600 odds=2.94`.
- This is reconstructed from the code and the geometry; the reserve flags are not logged.
- Fix: presume enemy strength per enemy-held objective or anchor. Only a fresh belief within relocation range of *that* anchor displaces it. Release a hold only when every anchor the sector faces is covered.

**2. #25 switches off vanilla's interception of enemy advances, for both sides.**
- Every alliance-1 filter row reads `removed=8|forbidden=8` (25 rows), `9/9` (20), and so on: always the CSA's whole formation count. Alliance 0 reads `removed=15|forbidden=15`.
- `CollectForbiddenIds` (`CheckForDefensiveOperationsCandidateFilterPatch.cs:351-385`) merges suppressions from every coastal response. Those include "local units only" around the Fort Norfolk threat (`StrategicMovementBudget.cs`) and "no cross-map units" for every coastal guard. Those units are then removed from the candidate list for the **whole** `CheckForDefensiveOperations` call (candidates come from that list, 13562).
- That call is vanilla's only reactive interception of an enemy marching onto own soil (13505-13719; scenario values: 350 max distance, 750 with no enemy near, 1.5× strength, CSA probability 60 per year).
- The planner's own Hold design counts on vanilla's defensive operations to react, so Whiskey removed the interception without replacing it.
- Fix: scope each suppression to its own threat, or turn #25's filter into telemetry only while the planner runs and let `PlannerDefensiveAvailabilityPatch` be the only defensive gate. This needs your call.

**3. The depot task stripped the Fredericksburg army.**
- `AssignDepotBuilders`/`SmallestIn` (`ForceAllocator.cs:584-627`) take the smallest free formation in the depot site's sector. `Free()` has no readiness check and no "is this formation screening something" check.
- The legacy ledger's "Recover low-morale-readiness" label suggests the Army of Virginia was unready (readiness below 0.35), yet it was marched 285 away from the Washington–Richmond axis.
- Fix: build depots from the main effort's own mass or a sector's surplus, never from a formation a hold counts on, and apply the readiness rule (step ≥ 2).

**4. The main effort keeps switching on and off, and stranded armies are never brought back.**
- The CSA main effort went 06-22 initial → 06-24 no-candidate (`cap=0`) → 06-26 initial → 06-27 no-candidate.
- A march drops readiness, and the mass requires readiness step ≥ 2 (`TheaterPlanner.cs:739`). Once the mass is empty, keeping the old objective (`Retain`, line 473) cannot help.
- After a drop, every formation becomes "Hold in place" wherever it stands. There is no return to its home sector.
- Fix: a formation already dispatched toward the current objective stays in the mass through readiness dips, as vanilla keeps units in an operation (it relieves only retreating or low-morale units, 14068-14078). A formation whose home sector's hold is short goes back when the main effort ends.

**5. The capital floor has no baseline and contradicts #4's own number.**
- The planner's floor is 1 × the threat sum (`TheaterPlanner.cs:1370`), which is 0 here. The `[CapitalFloor]` row's "vanillaNeed" is Whiskey's own belief-based recomputation (`CapitalFloorPolicy.VanillaNeed`): 2000 baseline + 0 − own units, giving 1999.
- That is positive, but nothing can be pledged because the allocation holds no CapitalGuard.
- Vanilla's own `AssignUnitToDefendCapital` still runs after the Prefix and is omniscient (it reads `enemyunits` at true positions).

**6. Hardcoded constants don't match this scenario.**
- `ForceAllocator.MinGarrisonStrength = 5000` (`ForceAllocator.cs:86`) claims to mirror vanilla's minimum strength for movement, but that is **2000** here (`002/A/campaignprefs.txt:1397`). The snapshot already carries the live value (`s.MinStrengthForMovement`).
- Presumptions (10000), hold floors and `NeedFor` are therefore 2.5× vanilla's.

**7. Recon** (the other agent's area): `tasks=6 unfilled=6` every day, so nobody looked at Washington and only contact or chance sightings fed the beliefs. That made causes 1 and 5 much worse.

## Q2–Q4 and items (e)–(h)

- **Q2 / (a):** vanilla's AI is omniscient: `enemyunits` at true positions, plus enemy destinations for defensive operations. The fog-honest beliefs saw 2 of the roughly 15 Union groups. Unscouted *targets* already get a presumed enemy, but the capital threat and the West's hold get none.
- **Q3:** 19.4k against an estimated 6.5k is the wrong comparison. The estimate is one division, the "est" figure is that same 6.5k sighting, and none of the capital risk is subtracted, because the only defences the capacity deducts (capital floor, sector holds) were 0 or had been released by cause 1. The main effort should be sized after a home-risk reserve; see design (d).
- **Q4:** only the planner's mover (`VanillaMoveHost`/`MoveIssue`) and #25 call `MoveUnitTo`. The legacy Director/FormationDirective ledger moves nothing; its labels are cosmetic. It still matters through #25: its "defensive not allowed" flags feed #25's suppression. Recommend deriving those labels from the planner's allocation, or deleting the ledger.
- **(e)** Yes, the CSA planner should read the player side's spotting marks (`SpottedEnemy.unitsattached[].spottedmarks`). Position as the player sees it; uncertainty bounded by `intelligencedistancevariationfactor`; confidence decaying from the mark's timestamp at the existing 7-day half-life; strength as the tooltip shows it (approximated the same way). Vanilla has no such channel for a non-player side (`AddUnit` skips the player's alliance), so this needs a user decision.
- **(f)** Vanilla capital defence still runs: #4 is a non-skipping Prefix and vanilla's inline candidate filter is untouched. Vanilla's defensive operations are dead because of cause 2.
- **(g)** `RecruitmentPatch.BuildIntent` prefers the main effort's theater (`RecruitmentPatch.cs:142-148`). #125 routes recruits only to listed shortfalls within 300 (1.5 × 200) of the recruiting cell. The CSA had one route, from (1229, -1024) near Richmond. Porterfield's 1,402 men are Recover and count 0. The West has no way to get reinforced: the existing cross-department path (`ReinforcementPick`, line 1016) serves only the main effort.
- **(h)** Nothing covers the gaps between sectors. Sector holds are judged on the sector's centroid and radius. Vanilla's destination-based interception was the only thing covering gaps, and #25 removed it.

## Foresight design (fog-honest)

**(a) What a side can honestly know:**
- The enemy capital and the owner of every objective town (public).
- The enemy's total army: `alliance[e].troopsfielded` is shown exactly for both sides on the campaign strategy screen (`CampaignStrategy.UpdateStats` 180126). The battle field `totalactiveforce` (78486) is battle-only, so don't use it.
- Last-seen positions, with the existing decay (7-day half-life, 30-day expiry).
- For the player's alliance, the spotting marks.
- Historical starting positions only as the existing small prior.

**(b) Presumed-threat picture:**
- Unaccounted = max(0, enemy `troopsfielded` − Σ live believed strength).
- Spread it over anchors, weighted by `Importance01`: the enemy capital (floor share, suggested 0.4), enemy-held objectives, and aged sightings at their last-seen strength.
- Feed it into the capital threat (`VanillaThreat`), each department's hold need (`HoldNeedAt`) and the presumed enemy at targets (`PresumedAt`).
- An anchor counts toward the capital threat when its march time to the capital is within a capital-warning window (suggested `ReinforcementMaxDays` = 10, using `MarchTime.EstimateDays`), not the 200 radius. At this log's march rates (about 140-190 a day), Washington to Richmond is 2-3 review days.
- The West's share comes from the Army of Occupation marks plus the presumption. Fill a department's shortfall with one formation per decision from a department whose hold has surplus: the same rules as the main effort's reinforcement (within 10 days, donor's own hold still met).

**(c) Covering force between the capitals:**
- A new reserve in `ReserveRules`, taken **before** the main effort's capacity is counted (`MassFor`), like the capital reserve.
- Role Hold, new reason "covering-force", withheld from vanilla's offensives, available to vanilla's defence against an enemy within 200 of its post.
- Size: max(0.6 × (believed + presumed within 200 of the enemy capital), 2000 live minimum).
- Post: the own-held town on the Washington–Richmond segment (Manassas Junction or Fredericksburg).
- The main effort may never release it; the only exception is a target at the enemy capital itself.
- Not CapitalGuard: that role marches to the capital's own position (`ForceAllocator.cs:451`). Not Screen: that now means a recon mission.
- W&L rules hold: the army carrying a player below map level may be the covering force; a player who commands his own map group is never its only holder; the player's own command is never moved.

**(d) Main effort odds and mass:** they account for home risk automatically once the capital floor and the covering force are sized from (b), since capacity already subtracts both reserves. Add that a candidate is ineligible if its needed force exceeds the capacity left after them, and the capital term in the score then becomes non-zero.

## Decisions for you

1. **#25:** scope its suppressions per threat, or turn it into telemetry only while the planner runs. Recommend telemetry-only.
2. **Spotting marks as beliefs:** only for the player's own alliance, or also build an equivalent mark channel for the AI side from its own fog grid using vanilla's formula. Recommend both, so the two sides stay symmetric.
3. **Enemy total from the strategy screen as a presumption input:** recommend yes.
4. **Covering-force post:** forward at Manassas or rear at Fredericksburg (inside the 200 capital radius vanilla uses). Recommend Manassas, with Fredericksburg as the fallback when the force is short.
5. **Department-shortfall reinforcement path** for the West: recommend yes, one formation per decision.
6. **Minimum garrison strength:** use the scenario's live value (2000 here) instead of the hardcoded 5000. Recommend live, noting that it shrinks the presumed enemy 2.5× in this scenario, so the presumption from (b) needs to land first.

**Telemetry gaps worth fixing:** `[CampaignMover]` names no formation or target, the trace prints no target coordinates, the reserve flags are never logged, and #25's filter row gives no reasons.