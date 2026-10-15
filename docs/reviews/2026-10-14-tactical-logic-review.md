# Whiskey Realism: review of tactical decision logic (orchestrator, operations, player orders)

Scope: `src/WhiskeyRealism/Tactical/{.,Orchestrator,Operations,PlayerOrders}`, plus the patch surfaces that consume orchestrator state. No edits were made. Source root: `/home/onebodyamerica/Projects/whiskey-realism-mod/src/WhiskeyRealism/`; all mod file paths below are relative to it.

Vanilla decompile: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`. Abbreviations: `AEB` = `Tactical/Orchestrator/ArmyEvidenceBuilder.cs`, `TBCR` = `Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs`.

## Framing: where decisions are actually made

In the release configuration (`Tactical Commander Mode = Active`), the army-plan layer mostly does not reach the game's write path:

- **What sets `macroai`:** `BattleMacroStrategyPatch.TryApplyOrchestrator` (`Patches/BattleMacroStrategyPatch.cs:79-110`) reads `CurrentMacroAi`, which is derived from `BattlePhase`, only when `OperationMacroAi` returns `int.MinValue`. That happens only when `CommanderMode == Off`, and Off does not allow writes (`TacticalCommanderModePolicy.AllowsWrites` is true only for Active). In Active mode, `macroai` comes from `TacticalOperationsLedgerModel.OperationMacroAi(CurrentOperation)` (`Operations/TacticalOperationsLedgerModel.cs:120-148`). That function looks at the operation's shape and phase, not at `BattlePhase`.
- **Unused plan outputs:** `EmitArmyIntent` has no runtime caller. `ReserveCommitTriggerOdds` has no consumer, so the envelopment cap at `ArmyOrchestrator.cs:180-183` changes nothing.
- **The 18 playbooks:** all of them create the same plan: phase Probe, main-effort sector 0, and null fixing and screening sectors. The plan ID only matters in `DirectChildAllocator.AllowsProvisionalAttackAxis` (`DirectChildAllocator.cs:221-235`), and only for 5 of the IDs.
- **What `BattlePhase` still does:** it gates the main-effort shift, the offensive replan triggers, and the provisional attack axis.
- **The other inputs:** the playbook ID and the inferred enemy intent reach the operations ledger only as telemetry strings (`StrategicBattleIntentSnapshot.CampaignIntent` and `TheaterIntent`).
- **Consequence:** decision quality really depends on the operations director, the `DirectChildAllocator` roles, and the leaf cascade. The whole Probe→MainEffort→Exploit→Consolidate→Withdraw machine and playbook selection mostly feed logs. Documentation that says "phase progression drives attack-objective writes" (`docs/tactical-orchestrator.md`) does not match the code.

## Findings (ordered by severity)

### 1. CRITICAL: readiness reads ammo from a `float[]`, so ammo is always 0
- **Category / confidence:** correctness. The bug is confirmed; how far it reaches is plausible.
- **Mod:** `AEB:192`. `SafeRegimentFloat(unit, "ammo")` calls `Convert.ToSingle` on `Regiment.ammo`.
- **Vanilla:** `Regiment.ammo` is `public float[] ammo;` (decompile `:111498`, `class Regiment` at `:108962`; vanilla uses `ammo[0]`, e.g. `:21031`).
- **What happens:** the cast throws `InvalidCastException`, the code swallows it, and returns 0, so every sampled unit has ammo 0. Effective strength becomes 0, force health is 0 (below 0.25), and readiness returns `HoldFatigued` every time. As a result Probe never reaches MainEffort by "purchase", the budget-elapsed path goes to Consolidate, and Exploit cannot be reached. In Active mode this does not change `macroai`, but a plan stuck in Consolidate switches off the main-effort shift, both offensive replan triggers, and the provisional attack axis. There is a second branch: leaf regiments only get `groupstrengthactive > 0` if `UpdateGroupStrength` runs on them, and vanilla only calls it on groups. If no leaf qualifies, every unit is skipped and readiness runs on the hard-coded 0.2 fatigue / 0.9 ammo. The input is broken in both branches.
- **Fix:** read `ammo[0]`. Work out its scale from how vanilla compares it with `GamePrefs.ammoalertlevel` first, then normalize. Use `strength > 0 && activeInHierarchy` as the "alive" filter. Add a harness test that feeds a `float[]` field.

### 2. HIGH: phase budgets are in battle-seconds while plan age and commit windows are in real seconds
- **Category / confidence:** units / logic. Confirmed.
- **Mod:** `TacticalPhaseProgressionDoctrine.cs:30-32, 250-251`; `ArmyTickCycle.cs:81-85`; `TBCR:382-385`; `ArmyReplanTriggers.cs:88, 127`.
- **What happens:** Probe lasts 30-120 battle-seconds, MainEffort 60-180, Exploit 90. A real engagement runs thousands of battle-seconds. Once `MainEffortBudget` elapses the plan goes unconditionally to Consolidate, even when winning. Getting out of Consolidate needs odds of at least 1.4 and a known reserve fraction below 0.6, so Consolidate is effectively absorbing.
- **The real-time clock:** `PhaseDeadline` resets the plan to Probe every 180 real seconds or more. Plan age advances by the real time between ticks, capped at 5 s per tick, so a slower side-stat cycle stretches the period.
- **Effect by compression:**
  - 1x: the army cycles Probe→MainEffort→Probe roughly every 3 minutes.
  - 20x: it reaches Consolidate within about 10 real seconds, then sits there for about 170 real seconds (about 57 battle-minutes) before the reset.
- **Operations ledger:** it uses wall-clock time too. `CurrentTimeSeconds` is `realtimeSinceStartup` (`TacticalOperationsLedgerRuntime.cs:108-140`, `TBCR:531`). Commit windows of 420/900/1200 s therefore last 15 battle-minutes at 1x but 5 battle-hours at 20x.
- **Fix:** put every doctrine timer on battle time (`currenttimefromstart`). Rescale the budgets to battle-minutes (for example Probe 20-60 minutes, MainEffort 60-180 minutes). Make MainEffort→Consolidate depend on stalled progress or odds rather than elapsed time. Keep wall-clock only for rate-limiting the computation.

### 3. HIGH: "sectors" are list indices, not geography
- **Category / confidence:** logic. Confirmed.
- **Mod:** `AEB:273-290` assigns `new EnemyVisibleSector(sectors.Count, ...)` to every own command group (army, corps, division and brigade alike), in `completeunitlist` order. `TBCR:1272-1299` mirrors that index.
- **Consumers that treat the index as space:**
  - `DirectChildAllocator.IsAdjacentSector` (`|s-main|==1`, `:245`).
  - Left versus right refuse: `PrimarySector < mainSector` (`:85-87`).
  - "Wing" flank buckets: min and max sector index (`TBCR:1216-1221`).
  - `MainEffortSector` (default 0, which is whatever group happens to be first in the list).
- **Effects:**
  - Corps-level sector indices are interleaved with their subordinates, so adjacency almost never holds and SupportMain is rarely assigned.
  - The first and last corps in list order always get Refuse, so a 2-3 corps army commits only one corps.
  - When any earlier group routs (it is filtered out at `AEB:368`), every later ID shifts by one, and `MainEffortSector` silently points at a different formation.
- **Fix:** build sectors from geometry. Project each group's position onto the lateral axis perpendicular to the bearing from the own centroid to the enemy centroid (or to the primary objective), split that into N bands, and key everything by a stable instance ID, not by list position.

### 4. HIGH: the leaf cascade orders children by world X, not across the front
- **Category / confidence:** logic. Confirmed.
- **Mod:** `TacticalCommandTreeProbe.cs:196, 243-246` (`CompareByWorldXAscending`).
- **What happens:** "Left to right" is only correct when the front faces ±Z.
  - If the army faces east or west, X order is depth order. The "anchor" becomes the middle echelon, `RefuseLeft`/`RefuseRight` go to the rear and front units, and DoubleWing "wings" are front and rear.
  - If the army faces south, left and right are swapped.
- **Also:**
  - Envelopment applies at `depth==0` of each *top-level child* (`TacticalLeafBrigadeMap.cs:200`). It splits the single Main corps's divisions into two wings, not the army's corps, which contradicts the comment.
  - `childFlankExposureBucket` is hard-coded to 0 (`:229`), so the cautious-flank branch of the cascade never fires.
- **Fix:** sort by projection onto the lateral axis described in item 3. Decide envelopment at army level from DirectChildAllocator geometry. Feed real flank exposure.

### 5. HIGH: enemy intent inference always runs on a sentinel reserve value
- **Category / confidence:** logic / lifecycle. Confirmed.
- **Mod:** `AEB:294` computes the enemy's reserve-commit fraction from `AIBattle.objectivechain` filtered to the enemy alliance. `AIBattle` exists once per side (vanilla `aibattle[]` / `SetSide`, `:85203-85212`), so its chain contains only its own groups. The lookup matches nothing and returns the sentinel 0.35 (`AEB:531-532`).
- **Why that matters:** `ArmyIntentInference.Build` (`:78-99`) needs ≥0.4 or <0.3 for Attack, Defend-by-fire, Refuse and the Probe sub-branch. With 0.35 it collapses to Defend, or Probe in the single-sector case. `AnyContactBroken` is never set to true (`AEB:271`), so Withdraw intent and `ContactBroken` cannot occur either.
- **Effects:**
  - `EnemyIntentShift` rarely fires.
  - `OpposingCommanderHint` is a constant of about 0.6×confidence.
  - The adversarial loop described for O2 is inert.
- **AI-vs-AI:** `Tick` deduplicates per side-stat cycle, so the first `AIBattle` to fire drives both sides with its own `objectivechain` (`TBCR:201-215, 306, 471-472`). The second side then gets objectives and reserves from the enemy's chain. That is both wrong data and an omniscient read of the enemy's real reserve designation.
- **Fix:** pass each side its own `AIBattle` (look up `bunits.aibattle[i]` by `sideofai`). Infer enemy reserves from what is observed, for example enemy strength seen in contact versus the estimated total.

### 6. HIGH (plausible): the battle-end teardown probably never fires
- **Category / confidence:** lifecycle. Plausible; needs a multi-battle log.
- **Mod:** `Patches/TacticalObserverPatch.cs:270-292, 321-333`. BattleEnd needs two observed zero-unit ticks inside the `CheckGlobalAIStrategy` postfix. `TBCR:103` (`OnBattleStart` returns early when `active`).
- **Vanilla:** `completeunitlist` is only nulled in `BattleUnits.OnDestroy` (`:79219`). After that, `AIBattle` no longer ticks, so a zero-unit tick is never seen.
- **Effects:**
  - Battle 2, and a mid-battle save/reload, may inherit battle 1's orchestrators, plan and personality, plus the static `ArmyTickCycle` clocks and the heavy-gate `_lastHeavyReviewHours`.
  - When the new battle clock is behind the old `lastH`, `TacticalHeavyPathGate.Decide` clamps elapsed time to 0 (`:103-104`). It never runs `max-interval` or `signature-change`, so the old snapshot (objectives, command tree) is reused until the new clock passes the old one.
- **Fix:** in `Tick`/`OnBattleStart`, keep the battle key (the `BattleUnits` instance ID plus `battlepasseddays`). On change, force teardown and start again. Also reset when the `currenttimefromstart` delta goes negative.

### 7. HIGH: replan triggers that never fire or fire continuously
- **Category / confidence:** logic. Confirmed.
- **Mod:** `ArmyTickCycle.cs:209-221`; `ArmyReplanTriggers.cs:127-143`.
- **Dead triggers:**
  - `MainEffortSectorLoss` can never fire, because current and history are both passed `ownMainEffortStrength` (and that value is the whole army's `totalactiveforce`, `AEB:137`).
- **Triggers that act as "level" conditions (true for as long as a state lasts, not on a change):**
  - `ReinforcementArrival` fires whenever own minus enemy `reinforcementarrivalswithin24hrs` exceeds 1 *man* (vanilla sums `strength`, `:84767`).
  - `CasualtyThreshold` fires whenever morale is below 0.4.
  - `ReserveExhaustion` fires whenever reserves are at 0.85 or more.
  - Each of these re-fires every `Min Replan Seconds` (60 real seconds or more; the same 5 s per-tick cap applies).
- **Effect:** every `Replan` resets the phase to Probe (all playbooks instantiate in Probe). Withdraw, which the doctrine calls "absorbing", flips Withdraw→Probe→Withdraw each minute. In the off-mode fallback, `macroai` briefly flips 3→1/-1 between ticks.
- **Also:** `mainEffortOddsHistory` and `mainEffortLocalOddsHistory` are both fed `HistoryGlobalOdds` (`ArmyTickCycle.cs:200, 219`). The 1.35× exploit and breakthrough ratio therefore divides sector odds by global odds.
- **Fix:**
  - Make triggers fire on a change, using a snapshot taken at the last replan.
  - Do not reset the phase on replans whose reason is defensive.
  - Keep per-sector odds history.
  - Express the reinforcement delta as a fraction of own strength.

### 8. HIGH: sector odds are distorted by over-counting
- **Category / confidence:** logic / units. Confirmed.
- **Mod:** `AEB:396-436`. Visible enemy strength sums `enemystrengthwithinangle` over every attached regiment of the group.
- **Vanilla:** each regiment's array already contains *every* enemy within `aidefensivemaxrange` (`:122720-122732`). A brigade of 4 regiments therefore counts the same enemy about 4 times, a corps about 40 times, and the army group counts everything.
- **Also:** `EnemyVisibleSector.Odds` (`EnemyVisibleState.cs:25`) has no minimum enemy strength, so a sector facing a 50-man picket gets odds of about 100.
- **Effects:**
  - The "decisive sector" (`ArmyTickCycle.cs:141-155`) is biased toward small groups or pickets.
  - The 25% hysteresis on main-effort shifts is meaningless at those magnitudes.
  - `ExploitTriggerAbsoluteOdds` (1.8) triggers on trivial contacts.
- **Fix:** dedupe enemies per group (union of `enemyinrangereg`, or the group's own `unitrange`). Apply a minimum enemy strength or confidence weighting before computing odds.

### 9. HIGH: the orchestrator path ignores vanilla's hard overrides for `macroai`
- **Category / confidence:** dual authority. Confirmed.
- **Mod:** `Patches/BattleMacroStrategyPatch.cs:79-110`. Only `vanillaMacro==3` and EndBattle are respected.
- **Vanilla:** `CheckGlobalAIStrategy` treats `GameVars.aistrategy >= 0` (`:6378`) and `sideinformation[sideofai].macroai >= 0` (`:6383`) as hard overrides that return before its own logic. I did not verify where these values come from (a game setting, a scenario script, or something else). The legacy scorer passed `aistrategy >= 0` through. The orchestrator path ignores both and overwrites them every cycle.
- **Thrash:** vanilla reverts `macroai = newmacroaitoset` at `:6390` on every call once its timer has passed, and the postfix rewrites it. The postfix's value is what `AdjustGroupAIStance`/`Formations` and micro-AI see, so there is no visible thrash. But vanilla's `timertosetnewmacroai` state machine runs against its own value.
- **Fix:** return "no opinion" when `GameVars.aistrategy >= 0` or the side's `macroai >= 0`.

### 10. HIGH (fog of war): enemy totals, reinforcement schedules and reserves are read with full knowledge
- **Category / confidence:** fog of war. Confirmed.
- **What is read omnisciently:**
  - Enemy `totalactiveforce` for `CurrentOdds` (`AEB:116-117`).
  - The enemy's `reinforcementarrivalswithin24hrs` (`AEB:126, 296`).
  - The enemy's `scheduledarrival` entries: exact hours and strength (`AEB:786`).
  - The enemy's reserve designation in AI-vs-AI (item 5).
- **Intel confidence:** `BuildForceBalance` sets `intelConfidence = enemyDeployed > 0 ? 0.8 : 0.4` (`AEB:792`), so the 0.5 confidence gate never blocks.
- **The "visible" enemy strength:** `enemystrengthwithinangle` has no fog filter in vanilla (`:122720-122732`). Vanilla's own fog check at `:122699` only covers `enemyinfirerange`, and it is bypassed for player units.
- **Comparison with vanilla:** vanilla's `forcebalance` also cheats on totals. The enemy arrival *schedule* and the reserve read are new knowledge the mod adds.
- **Fix:**
  - Estimate enemy totals from contact history (largest enemy strength seen, decaying).
  - Use only own-side reinforcement schedules; model enemy arrivals from observed entry points.
  - Make intel confidence depend on contact coverage.

### 11. HIGH: `scheduledarrival.durationleft` is in hours but is divided by 3600
- **Category / confidence:** units. Confirmed.
- **Mod:** `AEB:832-834` (`hours = durationSeconds / 3600f`).
- **Vanilla:** `durationleft -= (currenttimefromstart - lastreinforcementarrivalcheck)`, where that difference is in hours (`:87562, 87584`).
- **Effects:**
  - Every arrival appears about 0 hours away. The defeat-in-detail window (parity between `attackWindow` and 24 h) can never open.
  - `ParityHours` collapses to "all reinforcements already here".
  - Readiness returns `HoldForReinforcements` for any scheduled own reinforcement, even one 10 hours out, so Probe holds indefinitely (`TacticalPhaseProgressionDoctrine.cs:207-208`).
- **Fix:** drop the `/3600`. Add a harness test that pins the unit.

### 12. MEDIUM: playbook selection collapses to a few picks, and the pick barely matters
- **Category / confidence:** logic. Confirmed by computation.
- **Mod:** `TacticalPlaybookCatalog.cs:42-89`; terrain is hard-coded `TerrainKind.Open` (`AEB:118, 252`; `TBCR:1330`).
- **Score table:** computed with the shipped weights and the deterministic jitter seeds (7 for alliance 0, 38 for alliance 1):

  | Side | Commanders | Pick |
  |---|---|---|
  | Union, odds ≥ 1.0 | Grant, Hooker, Meade, Thomas, Burnside, Rosecrans, derived vectors | ShermanManeuverFix (McClellan alone gets McClellanPreparedDefense) |
  | CSA | Lee, Jackson, Hood, Beauregard, Longstreet, Bragg, derived vectors | Lee, Hood or ForrestCavalryRaid |
  | CSA | Johnston, Pemberton | GenericCautious (GenericMethodical after a replan) |

- **Why:** Forrest's score is driven by Open terrain 0.9 under the constant Open input. The top-3 margins are 0.005-0.03, comparable to the jitter weight of 0.05. `(dot+3)/6` compresses the personality term to a span of about 0.15.
- **Registry coverage:** about 80 surnames. Unmatched commanders get `Derive`, which yields only a handful of distinct near-zero vectors.
  - Surname collisions: R.E. and Fitzhugh/S.D. Lee, A.S. and J.E. Johnston, A.P. and D.H. Hill, Smith, Anderson.
- **Doctrinal inversion:** envelopment requires ≥1.5x odds, but historically it was the weaker side's tool (Chancellorsville).
- **Fix:**
  - Derive terrain from the battlefield (`BattlefieldSetup` state or a height-variance sample).
  - Make playbooks produce different plans: fixing/screening sectors, phase budgets, reserve trigger, provisional-axis permission.
  - Widen the personality spread (use cosine similarity, not `(dot+3)/6`).
  - Key the registry on commander ID plus first initial.

### 13. MEDIUM: dead roles and phases
- **Category / confidence:** logic. Confirmed.
- **Never assigned at the top tier:** with no fixing or screening sectors in any plan, `DirectChildAllocator` never assigns `Fix` or `Screen` (`:64, 76`). They only appear inside the DoubleWing cascade.
- **Never produced in the operations ledger:** `TacticalOperationPhase.Exploiting`, `Consolidating`, `Aborting` and `Complete` have no producer. `OperationMacroAi` handles them, but `TacticalOperationDirector` only ever emits Committed, Forming, Scouting, SoftAbort or Planning.
- **Stale comment:** the comment in `ArmyOrchestrator.Replan` (`:356-361`) says the allocator does not read the phase. It does (`DirectChildAllocator.cs:223`). This is masked because `ObserveDirectChildEvidenceWithIntent` reallocates on every tick.

### 14. MEDIUM: a committed operation can flicker between Committed and Scouting
- **Category / confidence:** logic. Plausible.
- **Mod:** `Operations/TacticalOperationDirector.cs:96-102, 204`.
- **What happens:** `TryEvaluateCommittedContact` runs *before* the commit-window check. Any tick where the objective's `Confidence01` drops below 0.65 (`MainLineConfidenceFloor`) downgrades Committed to Scouting, which sets `macroai` to -1, handing control back to vanilla. Scouting is not an "active" phase, so the next tick reselects from scratch and can recommit. There is no hysteresis.
- **Odds mismatch:** the director's odds use `_currentForce.AvailableStrength`, which is the whole army's `totalactiveforce`, against the objective-local enemy. Odds come out inflated, so it commits (≥1.35) or parallel-attacks (≥1.65) too easily.
- **Fix:**
  - Respect the commit window for downgrades.
  - Use separate enter and exit thresholds for confidence (for example 0.65 to enter, 0.5 to exit).
  - Compute odds with the strength assigned to the operation, not the whole army.

### 15. MEDIUM: Reserve-role units can never be released, whatever the phase or crisis
- **Category / confidence:** logic. Confirmed in code; impact plausible.
- **Mod:** `TacticalReserveCommitGate.cs:69` (Reserve → Deny unless already engaged).
- **What happens:** the cascade sends many brigades to Reserve: non-anchor SupportMain children, inner children of Refuse units, and outer children when the commander is cautious. Nothing (`ReserveCommitTriggerOdds`, Exploit, a flank-crisis signal) changes that role. Vanilla `CheckUseOfReserves` is then blocked for those units unless #61's own reserve doctrine moves them.
- **Fix:** allow release when the phase is Exploit, when the plan's `ReserveCommitTriggerOdds` is met, or when an adjacent sector's local odds drop below a crisis threshold.

### 16. MEDIUM: top-tier roles have no hysteresis
- **Category / confidence:** logic. Plausible.
- **Mod:** `TBCR:1239` reallocates on every tick. Main selection relies on `ContactFlag`, which is `receivedfire.Count>0`, plus 5-bucket strength.
- **What happens:** the Main role can hop between corps from tick to tick. That changes the leaf-map signature, rebuilds the cascade, and makes #61 reissue orders. The 25% hysteresis protects only `MainEffortSector`, not the roles.
- **Fix:** keep the current Main unless a challenger beats it by a margin for N ticks.

### 17. MEDIUM (plausible): absolute morale floor of 0.4
- **Category / confidence:** logic. Plausible.
- **Mod:** `ArmyTickCycle.cs:202, 214` uses `averagemorale`, which is absolute 0..1 (vanilla `:84771-84792`). Other Whiskey patches normalize by `battlestartmorale`.
- **What happens:** green or low-morale armies can start near or below 0.4. That triggers immediate "morale-floor-breached" → Withdraw, plus the 60-second `CasualtyThreshold` replan loop from item 7.
- **Fix:** use morale relative to the start of battle, or the routed ratio.

### 18. LOW: smaller correctness issues
- **Charge gate** (`TacticalOrchestratorChargeGate.cs`): Refuse and Fix deny every charge, including local counter-charges against an enemy closing on that flank. Allow charges when the target is disordered or routed, or when the unit is defending its own position.
- **Reinforcement doctrine comment** (`TacticalReinforcementOpportunityDoctrine.cs:74-76`): the stated thresholds are wrong; the mid value is actually 1.32, not 1.20.
- **`Plugin.cs` "Verbose Logging"** says "Default ON" but the default is `false`, which breaks the AGENTS description-sync rule.
- **`TacticalOrderSettlementGate`** treats a non-charge unit that is actively marching (paths > 0, not interrupted) as "settled". Duplicate-waypoint suppression in #61 has to carry all of the anti-thrash load.
- **Patches writing orchestrator state:** `UpdateLeafBrigadeMap`, `UpdateReinforcementOpportunity` and `UpdateLiveCommanderInitiative` are called from `BattleCommandPostureExecutorPatch.cs:1955-1985`. This breaks the "patches read; state writes happen in the tick" contract. It also means the envelopment and leaf state update on the `AdjustGroupFormations` cadence, not the coordinator's.

## Strategy gaps and recommended next slices (ranked)

1. **Input-correctness slice:** fixes items 1, 8, 11, 7 (dead and level triggers), and 5 (per-side `AIBattle`). Without these, every higher doctrine runs on garbage. Each needs a harness test that pins units: `ammo[]`, hours, a strength-weighted enemy dedupe.
2. **Geometry slice:** fixes items 3 and 4. Derive a front axis from the own→enemy centroid bearing or the objective bearing. Bucket groups into stable lateral sectors (left, center, right and optionally reserve depth). Use the same projection for the cascade order, refuse side and envelopment wings. This is the precondition for real flank security, refused flanks and envelopment.
3. **Single authority for posture:** either connect `BattlePhase` and the playbook into the operations director (phase → operation phase, playbook → shape, reserve trigger and budgets), or delete the dead plan machinery. Today there are two state machines and only the ledger acts. Respect `GameVars.aistrategy` and the side's forced `macroai` (item 9).
4. **Lifecycle hardening:** detect battle identity changes and a negative clock delta (item 6). Test two battles in one session and a reload in the middle of a battle.
5. **Fog honesty:** build an enemy-knowledge ledger from observed contacts, with decay and a largest-seen estimate, and feed odds, reinforcement and intent from it (item 10).
6. **Battle-time doctrine clock:** move phase, commit-window and replan timers to battle time with Civil-War-scale budgets (item 2). Add end-of-day, night and multi-day handling. Today `eodcycle` only reaches the state signature, so plans and phases carry across nights.
7. **Missing Civil War AI capabilities** (compared with the Scourge-style anchors):
   - Artillery massing at army level, a grand battery supporting the main effort. Today artillery is per-brigade micro.
   - Cavalry roles at army level. The Buford and Forrest playbooks change nothing behaviorally.
   - Timed and coordinated assaults: echelon release is not linked to the primary's contact.
   - Reinforcement-arrival routing: arriving units have no assigned role.
   - Withdrawal and rout rally lines at army level.

## Not reviewed
- `Tactical/PlayerOrders/`: only `PlayerOrderPriority.cs` and `PlayerOrderDedupe.cs` were skimmed, with no findings. The rest of that folder was not read.
- Top-level `Tactical/*.cs` scorers and ledgers were not read.
- Large Operations files were not read: `TacticalScourgeInspiredDoctrine`, `TacticalIntelligencePack`, `CommandPostureExecutor`, `CommandDoctrineAssignment`, `TacticalBattleLinePlanner`, `TacticalNavMeshPlanner`, `TacticalBattlefieldPicture`.
- Writer patches were consulted only to see how they consume orchestrator state.

## Not verified (needs runtime)
- **No runtime evidence available:** BepInEx is not installed at the documented GTCW path. There is no `BepInEx/`, `LogOutput.log` or `tuning-logs` folder, which contradicts the handoff note that says "deployed hash verified". No item above was checked in a running game.
- **Scourge of War comparison:** the SoW Remastered install and SDK are absent at the documented path, so the comparison rests only on `docs/scourge-of-war-ai-anchors.md`.
- **Item 1 branch:** whether leaf regiments have `groupstrengthactive > 0`, which decides between ammo=0 poisoning and the constants path.
- **Item 6:** whether `orch-teardown` and a fresh `orch-bootstrap` appear for every battle.
- **Item 16 and 14:** how often roles and operations actually flicker.
- **`completeunitlist` order** (which affects how the item 3 index sectors fall), the real starting `averagemorale` values (item 17), and Harmony postfix order between `TacticalObserverPatch` and `BattleMacroStrategyPatch` on `CheckGlobalAIStrategy` (a possible one-cycle `macroai` lag).
