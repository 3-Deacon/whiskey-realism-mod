# Whiskey Realism — Full Mod Review (2026-10-14)

Review-only pass over `main` @ `262a50d` (360 source files, ~80k lines) against a freshly regenerated vanilla decompile of the **current** game build. Nine review agents (Opus: campaign, tactical logic, battle writers, performance; Sonnet: game-update compat, observers/guards, config/persistence/telemetry, vanilla cadence; Haiku: test coverage) plus sub-audits. Every critical/high finding marked **[verified]** was re-checked by hand against code + decompile before inclusion.

Confidence key: **confirmed** = read in code/decompile; **plausible** = code path exists, runtime frequency unknown; **unverified** = needs a smoke run.

---

## 0. Environment status (read first)

| Item | State |
|---|---|
| Game build | Steam updated GTCW today: buildid **14703111**, files stamped 2026-10-14 21:07-21:08. Unity still 2021.3.16f1. |
| BepInEx | **Gone.** No `BepInEx/`, `winhttp.dll`, `doorstop_config.ini`, plugin DLL, cfg, or tuning logs. Reinstall BepInEx 5.4.23.5 x64 + redeploy before anything runs. **The old cfg is gone, so C# defaults are now what applies** — see §6 default mismatches. |
| Build | `./build.sh` against the new `Assembly-CSharp.dll`: 0 warnings / 0 errors. |
| Tests | Harness **1253 PASS / 0 FAIL**. |
| Patch-target compat | **Clean.** 125/125 attribute patches bind (names, overloads, parameter names/types). 758 string-literal reflection sites resolved. No transpilers exist. Update caused no breakage that static analysis can see. |
| Update diff | Not computable — no pre-update DLL or depot manifest survives. Method *bodies* were not compared. |
| Decompile | Regenerated at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` (266,136 lines). The ±1 anchor drift in `docs/findings.md` is doc provenance noise (May-02 vs May-03 anchors), **not** a game change; a real +2 line shift exists somewhere in decompile lines 151348–160992 but is undatable. |
| Runtime data | None (wiped with BepInEx). Performance conclusions are static analysis. |

---

## 1. Top fixes (Critical first, then High)

| # | Sev | Area | Finding | Where |
|---|---|---|---|---|
| 1 | Critical | Battle | **B8 calls the campaign `SetWithdrawal` API inside battle → unit becomes permanently un-orderable.** `Regiment.SetWithdrawal` ends with `onretreat = true` (decompile 116175); `BattleUnits.SetWaypoint` returns early on `reg.onretreat` (91361); the only clears are campaign/depot paths (`MoveUnitTo` 14509, `SetRetreatToDepot`, `WorkDownRetreatOrders`). Re-fires when paths drain. Default **true**, description says "Default-off". **[verified]** | `Patches/B8CheckUseOfReservesPatch.cs:175` |
| 2 | Critical | Battle | **#44 writes `macroai = 3` without setting `startoffallback`.** Vanilla only stamps `startoffallback` on its own transition (6355); the retreat check at 6362 is `currenttimefromstart >= startoffallback + timetostartairetreattimer`, which is immediately true when the field is 0 → `SetEndOfBattle` / retreat timer. Both the scorer path and orchestrator path write 3. **[verified]** | `Patches/BattleMacroStrategyPatch.cs:73, 99` |
| 3 | Critical | Campaign | **Operations get stuck in Recover forever.** PhaseTruth checks `ForceBelowThreshold` *before* the deadline; `Recover` returns `true` so deadlines/replan never run. `RequiredForce = f × (own+enemy)` theater-wide is really an own:enemy ratio of f/(1−f) — 0.8 ⇒ 4:1 across the whole East. **[verified]** | `Strategic/PhaseTruthLedger.cs:90-106`, `CIC.cs:68-70`, `StrategicCoordinator.cs:473` |
| 4 | Critical | Campaign | **Every land battle resets both sides' operations to phase 0.** `RecordBattleOutcome` → `OnEventTrigger` dirties both CICs regardless of size/theater; next review rebuilds the same op from phase 0 with fresh deadlines. In an active theater, ops likely never pass develop-contact. **[verified]** | `Strategic/StrategicCoordinator.cs:1061-1085` |
| 5 | Critical | Campaign | **Historical catalog doesn't match `Config/campaignobjectives.dat`**, and with doctrine on `PickCampaignObjectivePatch` blocks vanilla. Objectives 29-37 are W&L-scenario-only; 31/36 are Union-owned yet targeted by CSA ops; no 1862-63 profiles for the grand campaign (001) → NoProfile for both sides, `followedcampaignobjective` frozen on the 1861 target. Profiles whose phase targets are unavailable cause a daily select→abort→reselect loop. CSA "defense" ops target objective 4 = *Capture Washington*. | `Strategic/HistoricalOperationCatalog.cs:383-461`, `Patches/PickCampaignObjectivePatch.cs:31-37` |
| 6 | Critical | Battle AI inputs | **Orchestrator evidence is corrupted at the source:** ammo always 0 (`Regiment.ammo` is `float[]`), reinforcement ETAs ÷3600 too small, "sectors" are list indices not ground, enemy strength over-counted ~4-40×, enemy reserves a constant sentinel, `MainEffortSectorLoss` trigger can never fire. **[verified: ammo, ÷3600, sector index, trigger]**. Every doctrine built on top (phase progression, readiness, envelopment, refuse flanks, replan) is operating on garbage. See §3 T1-T7. | `Tactical/Orchestrator/ArmyEvidenceBuilder.cs:192, 273-294, 396-436, 832-834`, `ArmyTickCycle.cs:209-210` |
| 7 | High | Campaign | **`ImportanceValuesPatch` overwrites every AIArea's offensive target with the single plan target**, bypassing vanilla's supply-capacity and distance gates (`CalculateMostValueableAIZones` 10964). Local counterattacks on other fronts vanish; units with no `theaterposition` can be sent across the map. | `Patches/ImportanceValuesPatch.cs:53-63` |
| 8 | High | Battle | **#61 re-lays whole brigades in close combat ~every 5 s.** Calls `SetGroupFormation` directly (→ `ClearTargets`, `ChangeRegimentFormation`, `RegimentSetPath` on every regiment) while vanilla `AdjustGroupFormations` skips groups with `combatstatussubordinates > 0`. Cooldown drops to 5 s real when close-engaged and the pending-order gate is bypassed. | `Patches/BattleCommandPostureExecutorPatch.cs:704, 932` |
| 9 | High | Battle | **#61 facing pulse forces formation 0, which unlimbers artillery** (vanilla `ChangeRegimentFormation` 135354, `unittyp==2 && num==0 && mounted==1`). Fires on any visible enemy at any range, including ReserveWait/FallBack tasks and while moving under flank risk. | `BattleCommandPostureExecutorPatch.cs:1001, 1070` |
| 10 | High | Battle | **#53 pathfinder discipline strips partial paths vanilla deliberately keeps** (vanilla accepts partials once `iterations >= 15`, 130511). After 20 retries the unit has zero paths. No alliance gate → affects the **player's** right-click orders. | `Patches/TacticalPathfinderDisciplinePatch.cs:67-70` |
| 11 | High | Perf | **Hitch root cause is almost certainly allocation-driven Boehm GC + unscoped bursts, not the orchestrator tick.** Every `TelemetryPerf.Scope` emits a full row (not just slow ones) **[verified]**, including per-regiment scopes on `MoveNonAIUnits` and up to 20× per `AddPath`; writer does `File.AppendAllText` per row; queue is O(n) per enqueue under lock. Plus per-tick `.name` hierarchy walks, `NodeIdMatches` string allocation, per-path-corner `GameObject.Find("GameController")` **[verified]**. See §5. | `Telemetry/TelemetryPerf.cs:115-130`, `TelemetryQueue.cs`, `TelemetryWriter.cs`, `TacticalPatchIds.cs:30-32`, `BattleCommandPostureExecutorPatch.cs:3169` |
| 12 | High | Perf/plan | **Slice 2 (worker thread) plan: CHANGE REQUIRED — do not execute as written.** It is sized on a false 20 Hz premise (actual ≈2.2 Hz at 20x — see §5), adds main-thread work rather than removing it, moves the cheap pure-C# work while the Unity-bound cost stays put, and relies on a snapshot that is mutable (`TacticalUnitObservationAggregate.Shared.Capture()` clears and refills in place). | `docs/superpowers/plans/2026-05-19-tactical-orchestrator-worker-thread-implementation.md` |
| 13 | High | Config | **Fresh install: `Enable Tactical Observer` = false but described "Default ON"** **[verified]**. Several "Default ON" behavior flags (B6c local reaction/charge denial, reserve-list mutation, decision matrix, intent telemetry) silently depend on it and are inert. | `Plugin.cs:266-270` |
| 14 | High | Config | **`Tactical Commander Mode = Off` is not a full rollback, and `MonitorOnly` is not write-free.** B7, B8, #56, #57, #60, the #41 charge gate deny paths and the #42 feud gate never check the mode. Unknown mode strings silently parse to `Active`. | `B7CheckAIBombardmentPatch.cs:31`, `B8CheckUseOfReservesPatch.cs:120`, `BattleChargeGatePatch.cs:176-185`, `TacticalCommanderMode.cs:12-29` |
| 15 | High | Persistence | **No strategic-state reset between campaigns / saves without a sidecar.** Singleton `StrategicCoordinator` only fills nulls; previous campaign's ActivePlan, era, battle history, fired/applied succession events carry over (scripted commander swaps then wrongly skipped). Sidecar write is non-atomic `File.WriteAllText` (a crash leaves an empty/partial file → silent state reset). | `Strategic/StrategicCoordinator.cs:88-113, 1442, 1448-1545` |
| 16 | High | Campaign | **Director rate limit is one shared 1-publish/second clamp across both alliances** **[verified]**; alliance 0 publishes first each review, so alliance 1's posture is frozen. Outside W&L the player's own faction also gets a CIC and takes the slot. | `Strategic/DirectorPublishClamp.cs`, `StrategicCoordinator.cs:394` |
| 17 | High | Battle AI lifecycle | Battle teardown probably never fires → second battle / reload inherits stale orchestrators (§3 T9). | `Patches/TacticalObserverPatch.cs:270-333` |

---

## 2. Campaign / strategic layer

### Correctness
- **ArmyGroupManagement ignores vanilla's early-return guard** (`!GameVars.allowarmygroups || !AllowsArmyGroups()`, vanilla 17723). The Postfix creates army groups when the game option is off or before the reform is researched. **[verified]** — `Patches/ArmyGroupManagementPatch.cs:30-38`. `FindPreferredCommander` also skips `IsAvailable`/dead/captured/already-army-group-commander checks and the W&L player-group exclusion (`:206-225`).
- **CommanderReplacement basically never fires, and when it does it targets the wrong group.** It requires the displaced commander's `currentcommand != null`, but `AppointCommander` → `RemoveFromCommand` (60830) nulls it; displaced officer is the first army-group commander by index (`ReplacedRole` is never used — "Bragg → Western" can unseat Lee); `AssignCommando` on a Regiment never changes `ArmyGroup.commanderid`; last-name lookup is ambiguous (Johnston, Lee). Fix: resolve by role/area, use `ArmyGroup.AppointCommander`, filter with `CanBePickedForAIReplacement`. — `Patches/CommanderReplacementPatch.cs:105, 140, 172-213`
- **DefensiveOps Postfix churn:** adds the capital-defense candidate after vanilla's move loop ran; next tick vanilla removes it as "farthest defender" and the mod re-adds it — the unit is stripped from other ops each cycle and never moves. Also strips units the CoastalDefense runner just committed. Fix: Prefix pre-pledge. — `Patches/DefensiveOpsPatch.cs:102-106`
- **ArmyArea oscillation:** `IsIdleForAreaOrder` doesn't exclude capital defenders / depot / fort / raid / invasion units; vanilla sends them back → infinite ping-pong. The `theaterposition = anchor` write also distorts vanilla's `CheckCombinationOfUnits` for that cycle. — `Strategic/ArmyAreaRuntime.cs:180-239`
- **CoastalDefense runner** adds units to `unitsindefensiveoperations` without a `DefensiveMovingOrder` (vanilla stops/removes them when no enemy is within bugle range → daily stop/go) and never removes them from offensive ops. — `CoastalDefenseCustomOrderRunner.cs:111-119`
- **CoordinatedOffensiveOperations empties `ownunits` even on a failed commit**, and eligibility doesn't mirror vanilla `IsUnitAvailableForOffensiveOperations` (theater, weather, min-strength, capital-defender, raid). Never checks `recruitingzonesupdated` (can commit every call vs. vanilla's once per job cycle); skips dominance/aggression/winter tests; any eligible lead becomes `SingleLead` at any ratio. — `Patches/CoordinatedOffensiveOperationsPatch.cs:84-147`, `CoordinatedOperationPackageLedger.cs:238-241`
- **TransferOfUnits** replaces vanilla's heat-map deficit with the plan target on every pass (`requiredAtTarget` up to 0.95× total strength, so the guard rarely clears) — the actually-threatened zone gets no transfers. No Finalizer restore. — `Patches/TransferOfUnitsPatch.cs:92-93, 150, 171`
- **Recruitment:** drafts-only states count as eligible when strength ratio > 1, so vanilla aborts recruitment; with no plan `DefaultRecruitmentTheater` returns East for both sides instead of falling back to vanilla. — `RecruitmentIntentLedger.cs:161-165`, `RecruitmentPatch.cs:243-246`
- **FinancialAI reverts vanilla's tax/subsidy nudges** (target band is a point snapshot `min=max=current`), freezing lanes 1-4 in Balanced/Expansion — vanilla can't raise taxes in deficit. King Cotton lane 0 target is never applied. — `Patches/FinancialAIPatch.cs:43-46`, `Fiscal/FiscalIntentLedger.cs:474-486`
- **EconomyConstruction leaves its biased `bestiipplacesprob` stored**, blocking better same-type sites and raising build rate. — `Patches/EconomyConstructionPatch.cs:121-130`
- **ConstructionRuntime:** `TopSupplyTheater` is overwritten with an area key then compared to `Theater` enum names (never matches); capital threat is a sector average (diluted). — `Strategic/Construction/ConstructionRuntime.cs:81-104, 208, 286-292`
- **OperationalProbe:** Pause/Withdraw remove the unit from the offensive list but issue no stop/recall; probe state isn't persisted; target change keeps the old `TargetAreaKey`; one formation is compared against whole-sector enemy strength → probable daily Probe↔Withdraw flapping. — `Strategic/OperationalProbeRuntime.cs:147-157`
- **CampaignAiUpdateGovernor** (default on) caps 50x at 3 passes (vanilla 7), often 1 under the 3 ms budget, and disables `FastForwardAiCatchUpPatch` → job-cycle work runs 2.3-7× less per game day; `day%7` weekly gates can be skipped; monthly tick hardcodes `day=1`. — `Patches/CampaignAiUpdateGovernorPatch.cs:40-81`
- **DefenseIntentRuntime** `AccessTools.Method(SeaInvasion, "GetUnitStrengthInRange")` without arg types hits two overloads → `AmbiguousMatchException` swallowed → coastal landing signal (condition 3) is dead. — `Strategic/DefenseIntentRuntime.cs:272`
- **Dead reflection names (pre-existing):** `Regiment.numofmenoriginal` (ArmyEvidenceBuilder falls back to a *count*, `groupowninrange`; use `groupstrengthactive`) `Tactical/Orchestrator/ArmyEvidenceBuilder.cs:917`; `SceneManagement.currentcampaign/currentsave` `WlStrategicOrderBridge.cs:513`; `Regiment.side` `TacticalObserverPatch.cs:2520`.

### Strategy quality
- **Personality barely moves decisions.** Caution term is identical for every objective; force-ratio/distance terms are constants; the only objective-specific personality input is ≈±0.1. CIC personality is fixed (Lincoln/Davis); `SwapOfficer` only emits telemetry; there is no TheaterCommander tier despite AGENTS.md describing one. **Operations are a deterministic function of alliance + date — a player can read the script.** — `Strategic/ObjectiveScoring.cs`, `HistoricalOperationCatalog.Score`
- **Responsiveness signals hard-wired off:** `EnemyThreatensCapitalCorridor = false`, `CapitalDangerStreakDays = 0`, `DaysSinceFrontSignatureChange = 0` → Stalemated and capital-streak branches never fire. Sectors are whole theaters (6 buckets), so all ratios are theater-wide. — `HistoricalOperationContextBuilder.cs:83`, `StrategicCoordinator.cs:1051-1052`
- **Perk selection is deterministic** (every General army takes Siege Train then Sappers; Scouting/Amphibious roles never produced). — `Strategic/PerkSelectionScorer.cs`
- `CicReviewRouter` (tested) has drifted from `CIC.ReviewPlanWithTruth` (runtime) — tests don't cover the runtime path.

### Vanilla AI jobs the mod doesn't touch (gap map)
Supply-depot construction (ledger picks a site, nothing builds it), all naval jobs (`CheckFleetMovements`, ship construction, blockade ratios), sea-invasion **offense**, siege assaults, `LinkToFightingUnits` (battle reinforcement), raids, occupation of lost cities, operation tempo, campaign stance, winter (vanilla-only). For a realism AI the ones that matter most: proactive depots along the plan axis, Anaconda (blockade + amphibious), and marching to the guns.

---

## 3. Tactical orchestrator logic

**Headline: the army-plan layer barely reaches the game in the release config, and the inputs feeding the parts that do are broken.**

**What actually drives battle behavior.** In Active mode, `macroai` comes from `TacticalOperationsLedgerModel.OperationMacroAi(CurrentOperation)` (`Operations/TacticalOperationsLedgerModel.cs:120-148`). The phase-based `CurrentMacroAi` is only read when Mode=Off — which can't write. The 18 playbooks all produce the same plan shape (phase Probe, main-effort sector 0, no fix/screen sectors); the plan ID matters only in `DirectChildAllocator.AllowsProvisionalAttackAxis` for 5 IDs; playbook name and inferred enemy intent reach the operations ledger only as telemetry strings. `EmitArmyIntent` has no runtime caller; `ReserveCommitTriggerOdds` is never read. `docs/tactical-orchestrator.md`'s claim that phase progression drives attack-objective writes does not match the code. The real decisions come from the operations ledger, the direct-child role allocator, and the leaf-brigade cascade.

| # | Sev | Finding | Where |
|---|---|---|---|
| T1 | Critical | **Ammo always reads 0.** Vanilla `Regiment.ammo` is `float[]` (111498) **[verified]**; the mod reads it as a scalar float → swallowed cast failure → 0. Effective strength = raw×(1-fatigue)×ammo×morale = 0 → readiness always `HoldFatigued` → Probe never advances on odds, timeout goes to Consolidate, Exploit unreachable. | `Tactical/Orchestrator/ArmyEvidenceBuilder.cs:192` |
| T2 | High | **Reinforcement ETA off by 3600×.** `durationleft` is decremented by battle-hour deltas (vanilla 87562/87584) **[verified]**; the mod divides by 3600 → every arrival looks ~0 h away; defeat-in-detail window never opens; readiness holds Probe for any scheduled reinforcement. | `ArmyEvidenceBuilder.cs:832-834` |
| T3 | High | **"Sectors" are list indices, not ground** **[verified]**. Allocator treats them as geography (adjacency `|s-main|==1`, refuse-left/right by `<`, wings = min/max id). First/last corps in list order always refuse a flank; a rout shifts every later id so the main effort silently re-targets another formation. | `ArmyEvidenceBuilder.cs:273-290`, `TacticalBattleCoordinatorRuntime.cs:1216-1299`, `DirectChildAllocator.cs:85-87, 245` |
| T4 | High | **Brigade cascade orders by world X.** East/west-facing fronts make "flanks"/"wings" front and rear; south-facing swaps left/right. Envelopment splits the single Main corps rather than the army. `childFlankExposureBucket` hard-coded 0. | `TacticalCommandTreeProbe.cs:196, 243-246`, `TacticalLeafBrigadeMap.cs:200, 229` |
| T5 | High | **Sector odds over-count the enemy ~4× per brigade, ~40× per corps** (sums each regiment's `enemystrengthwithinangle`, which already includes every enemy in range, vanilla 122720-122732); no minimum enemy strength → a 50-man picket shows odds ≈100. Exploit threshold 1.8 fires on trivial contacts. | `ArmyEvidenceBuilder.cs:396-436`, `EnemyVisibleState.cs:25` |
| T6 | High | **Enemy intent inference runs on a sentinel.** Enemy reserve fraction is computed from *our* `objectivechain` filtered to the enemy alliance → never matches → constant 0.35 → mostly Defend/Probe. `AnyContactBroken` never set → Withdraw intent impossible. In AI-vs-AI the first-ticking `AIBattle` drives both sides. | `ArmyEvidenceBuilder.cs:271, 294, 531-532`, `TacticalBattleCoordinatorRuntime.cs:201-215` |
| T7 | High | **Replan triggers broken.** `MainEffortSectorLoss` can never fire (current and history get the same value **[verified]**). `ReinforcementArrival` (>1 man), `CasualtyThreshold` (morale<0.4 absolute), `ReserveExhaustion` are level conditions re-firing every ≥60 s; every replan resets phase to Probe, so Withdraw flickers. "Main-effort history" is actually global odds history. | `ArmyTickCycle.cs:200-219` |
| T8 | High | **Timers on the wrong clocks.** Phase budgets 30-180 battle-seconds; MainEffort timeout → Consolidate even when winning, and Consolidate is near-inescapable. `PhaseDeadline` resets to Probe every ≥180 *real* s. Operations-ledger commit windows (420/900/1200 s) are wall-clock: 15 battle-min at 1x, 5 battle-hours at 20x. | `TacticalPhaseProgressionDoctrine.cs:30-32, 250-251`, `ArmyReplanTriggers.cs:127`, `TacticalOperationsLedgerRuntime.cs:108-140` |
| T9 | High | **Battle teardown probably never fires.** Requires two zero-unit ticks inside the `CheckGlobalAIStrategy` postfix, but `completeunitlist` is only cleared in `BattleUnits.OnDestroy` (79219), after which the postfix no longer runs. `OnBattleStart` returns early while still active → battle 2 / mid-battle reload inherits old orchestrators, plan and clocks; heavy-gate clamps negative elapsed to 0 → stale snapshot reused. | `Patches/TacticalObserverPatch.cs:270-333`, `TacticalBattleCoordinatorRuntime.cs:103`, `TacticalHeavyPathGate.cs:103-104` |
| T10 | High | **Orchestrator ignores vanilla's hard `macroai` overrides** (`GameVars.aistrategy >= 0` at 6378, `sideinformation[].macroai >= 0` at 6383 — scenario-scripted stances). The legacy scorer respected them. | `Patches/BattleMacroStrategyPatch.cs:79-110` |
| T11 | High | **Fog of war: the AI reads what it shouldn't** — enemy `totalactiveforce`, enemy reinforcements within 24 h, the full enemy reinforcement schedule (times + strengths); intel confidence fixed 0.8 so the 0.5 gate never blocks. (Vanilla `forcebalance` already uses true totals; the schedule and reserve reads are new omniscience the mod adds.) | `ArmyEvidenceBuilder.cs:116, 126, 296, 786, 792` |
| T12 | Medium | **Playbook selection collapses.** Terrain always `Open` (constant bias). With shipped weights/seeds: Union odds≥1 → ShermanManeuverFix for Grant/Hooker/Meade/Thomas/Burnside/Rosecrans/unlisted (McClellan alone differs); CSA → Lee/Hood/ForrestCavalryRaid (Forrest wins for infantry armies via the Open-terrain score); Johnston/Pemberton → GenericCautious. Top-3 margins 0.005-0.03 vs 0.05 jitter; `(dot+3)/6` squeezes personality spread to ~0.15. ~80 registry surnames; collisions (R.E./Fitzhugh/S.D. Lee, A.S./J.E. Johnston, A.P./D.H. Hill). Envelopment needs odds ≥1.5, which inverts history. | `ArmyEvidenceBuilder.cs:118, 252`, `TacticalPlaybookCatalog` |
| T13 | Medium | Roles/phases that can never occur: Fix and Screen roles (no plan defines those sectors); operation phases Exploiting/Consolidating/Aborting/Complete never produced by the director. | `DirectChildAllocator.cs:64, 76` |
| T14 | Medium | Committed operations can drop to Scouting on any tick objective confidence dips below 0.65 (contact check runs before commit-window check, no hysteresis) → `macroai` handed back to vanilla, recommit next tick. Director odds compare the whole army vs the local enemy. | `Operations/TacticalOperationDirector.cs:96-102, 204` |
| T15 | Medium | **Reserves can never be released** — `TacticalReserveCommitGate` always denies Reserve-role units and nothing ever changes that role (not Exploit, not reserve-commit odds, not a flank crisis). | `TacticalReserveCommitGate.cs:69` |
| T16 | Medium | Corps roles reallocated every tick with no hysteresis → Main can hop between corps → cascade rebuild → #61 re-issues orders. | `TacticalBattleCoordinatorRuntime.cs:1239` |
| T17 | Medium | Absolute morale floor 0.4 (other code normalizes against `battlestartmorale`) — low-morale armies may Withdraw immediately, then hit the T7 loop. | `ArmyTickCycle.cs:202, 214` |

Lower: charge gate denies every counter-charge for Fix/Refuse roles; `TacticalOrderSettlementGate` treats a still-marching unit as settled; comment/threshold mismatch in `TacticalReinforcementOpportunityDoctrine.cs:74-76`.

**Not reviewed in this pass:** top-level `Tactical/*.cs` scorers; large Operations files (`TacticalScourgeInspiredDoctrine`, `TacticalIntelligencePack`, `CommandPostureExecutor`, `CommandDoctrineAssignment`, `TacticalBattleLinePlanner`, `TacticalNavMeshPlanner`, `TacticalBattlefieldPicture`); most of `PlayerOrders/`. The Scourge of War SDK was not found at the documented path.

---

## 4. Battle writer patches (beyond §1)

- **#61 leaf-brigade pass** moves brigades for *any* non-None task (incl. Hold, ReserveWait) toward the parent's last waypoint → brigades stack on the division centre; no safe-waypoint, duplicate or close-engagement gates; every `SetWaypoint` → `ResetMovementMode` cancels charges. — `BattleCommandPostureExecutorPatch.cs:2057-2259`
- **Three writers set `ai_stance` per `UpdateAITasks`:** vanilla `AdjustGroupAIStance` (X, queued courier order) → #45 (Y) → #61 (Z). X/Y/Z alternation of queued stance orders. Needs a single owner. — `BattleGroupStancePatch.cs:151/173/315`, `BattleCommandPostureExecutorPatch.cs:899`
- **#61 ignores vanilla retreat (`macroai == 3`) and EOD** — can order attack waypoints into a retreating army. — `:82`
- **#61 division play overrides defensive Hold tasks** whenever any sibling sees any enemy; `ParentNodeKey` falls back to `transform.parent`, possibly collapsing the whole army into one "play". — `:118-134, 3028`
- **#60 deployment correction shifts the whole group** (passes the HQ point as `SetGroupFormation` `targetpos`, which is the formation-line centre) and can undo vanilla's fortified-line placement. — `TacticalDeploymentTerrainDisciplinePatch.cs:275-295`
- **Inconsistent W&L/player protection:** #59 `HasPlayerOwnership` still uses the pre-05-19 whole-alliance check; B7/B8 rely only on `PerformAIActionDLCWL` (permits under-commander units at `ai_stance>=1`); #56 `TryClearStalledFallbackOrder` has no ownership check. Needs one shared predicate. Note W&L sets `ai_vs_ai = true`, so all three AIBattles run `UpdateAITasks` and per-unit checks are the only protection. M6: #61/#44 also run for the feud `aibattle[2]` (side 0) — skip `isplayeraiorfeud == 2`.
- **#61 writes orchestrator state from a Postfix** (`UpdateLeafBrigadeMap`, `UpdateLiveCommanderInitiative`, `UpdateReinforcementOpportunity`) — violates the read-only-to-patches contract; move to the coordinator tick.
- **#61 cooldowns are real-time** (`Time.realtimeSinceStartup`): 900 s courier interval = 5 game-hours at 20x and is keyed per parent, starving siblings. Failed `SetWaypoint` never stamps a cooldown → retry every cycle. Movement orders bypass courier order delay (AI immune to order delay — the same bypass #56 exists to fix).
- **#46 / CheckForDefensiveOperationsCandidateFilter** remove entries from vanilla lists in a Prefix and restore only in a Postfix — no Finalizer, so a vanilla throw loses the entries for the session. **[verified]**
- **B7 artillery micro thrashes** limber/retreat/unlimber with no path/movement guard and never releases counter-battery (8) back to 7.
- **#59 snapshots every attached unit's full state** (incl. up to 75×200 waypoint corners, ~38 delegates/unit) on every `CheckUseOfReserves` call even though vanilla usually exits early; rollback misses `StopRegiment` side effects.
- **#42 feud gate** drops vanilla's `neededdistancefeudgroupmovement` cap and stamps `lastfeudactiontime` on deny paths (catalog says it preserves it). Same stamping in #41.
- **#34 gun transfer** is non-durable (`guns` recomputed from strength at `Initialize`, 112681) — only makes the UI disagree.
- **#62 dedupe depends on diagnostics** being on.
- AGENTS.md's absolute "never use `transform.parent`" rule is too strong: vanilla `GrabNextParentUnit` (86886), the `SetWaypoint` order-queue sender and `IsUnitUnderCommander` use the transform parent as command parent for regiment→brigade. The sibling problem is at corps/division level. Narrow the rule.

---

## 5. Performance

### Facts established (decompile + game config)
- **No catch-up loop in battle.** `Time.timeScale` is never set. 20x only shortens the UniStorm day (`SetGameSpeed` 66391: `dayLength = battledaylengthinmins / gamespeed`). One sim step per frame.
- **Macro AI cycle** (`UpdateAITasks`: AssignReserves, UpdateMovingTargets, LinkReserves, CheckGlobalAIStrategy, AdjustGroupAIStance, AdjustGroupFormations, CheckForFeudGroupActions — and the orchestrator tick hung off CheckGlobalAIStrategy) is gated by `sidestatupdatecycle = 0.015` game-hours (`Config/battleprefs.txt:1034`). With `battledaylengthinmins = 120` over a 12-hour battle day: **every ~9 s at 1x, ~0.45 s (≈2.2 Hz) at 20x.** The Slice 2 spec's "20 Hz / 1340 ms CPU per wall-second" is ~10× too high.
- **Micro AI** (charges, retreats, fallbacks, reserves, bombardment, DLC player orders) is frame-based round-robin (one group/task per frame, 30 task slots) — **independent of game speed**.
- **Per-regiment:** `Regiment.MoveNonAIUnits` every 45 frames per regiment; `AddPath` up to 20× per `RegimentSetPath`.
- **Campaign AI does not run during a campaign-launched battle** (scene rebuilt), so governor/fast-forward patches are not implicated in battle hitches.

### Ranked hitch hypotheses
1. **Full stop-the-world Boehm GC** driven by mod allocation rate (game's `boot.config` has incremental GC, which falls back to a blocking full mark when outpaced). Drivers: per-call telemetry rows (each = PerfScope + Stopwatch + closure + TelemetryEvent + SortedDictionary ≈16 nodes, two `DiagnosticsSnapshot()` ≈7 locks each); per-tick hierarchy walks reading `.name` and `GetAttachedUnitsReg` for every unit, ungated (`CommandTreeRuntime.Snapshot`, `DirectChildDiscovery.Snapshot`, `BuildInstanceToSectorIndexMap`), plus `CommandTreeSignature` StringBuilder twice per side per tick; `NodeIdMatches` allocating `"node-"+id` twice per comparison across ~12 patches; unbounded `_lastEmittedAt`/`_lastLoggedAt`/OnceLog dictionaries growing the live heap.
2. **#61 posture-executor bursts, unmeasured.** Per eligible group: 3× synchronous `NavMesh.CalculatePath`, per path corner `SampleHeight` → `GameObject.Find("GameController")` + `GetComponent`, `EstimateFriendlyFrontBlocker` full `completeunitlist` walk; then vanilla `AddPath` re-solves up to 20×. Real-time cooldowns expire together → burst. The Postfix has **no outer scope** **[verified]** (the only scope wraps `Decide()`), so this never appears in p99. Its probe cache is keyed on `Time.frameCount` and never hits.
3. **Steady telemetry queue saturation.** Writer does `File.AppendAllText` per row (≈1.2k rows/s capacity, rewrites `manifest.json` per batch); per-regiment scopes alone can exceed that. When full, every main-thread enqueue is O(8192) under the shared lock.
4. **Vanilla `Debug.Log` stack captures** (`GameVars.DebugOwnLog` on every `SetWaypoint`, etc.) multiplied by mod-issued orders.
5. **Stats UI can force side-stat + `UpdateAITasks` recomputes several times in one frame** (218963-220334) — check whether hitches correlate with UI use.

**Discriminating measurement (one smoke):** a per-frame probe that, for frames > 250/500 ms, records `GC.CollectionCount(0)` delta, `GC.GetTotalMemory`, Mono heap size/used, top-level scopes entered that frame with durations, `CalculatePath`/`GameObject.Find`/`Debug.Log` counts, and telemetry queue depth. Report **max and count > 500 ms**, not p99 — a 5 s stall every 30 s is beyond p99 and a GC pause freezes the telemetry writer too.

### Fix order (replaces Slice 2)
1. Scope the #61 Postfix + `TryApplyNavPlan`; add the per-frame hitch probe; smoke once.
2. Telemetry: emit only slow rows, aggregate the rest per scope per second; ring-buffer queue with O(1) counters; one open `StreamWriter` per stem; manifest on timer/rotation only; volatile profile read; make `OnceLog` check-before-concat.
3. One shared per-tick hierarchy snapshot, rebuilt only on roster change; int hash instead of signature strings; cache names by instance id.
4. Cache `BattlefieldSetup` per battle (the `BattleUnits.bfs` static is already used elsewhere); budget NavMesh work to K groups/frame with a resumable queue; spatial grid for friendly-blocker.
5. Int node IDs (store `InstanceId` on ledger entries or build a `Dictionary<int,…>` once per ledger update); per-cycle contact cache shared by stance/posture/charge/reserve patches.
6. Clear every dedupe dictionary on battle start/end.
7. Revisit threading only if pure-C# analysis is still ≥10 ms/tick afterward — with immutable per-cycle inputs/outputs, no pooled snapshots, worker-owned ledgers with copy-on-publish, and live-precondition revalidation on main before applying.

### Other hot spots
- `TacticalObserverPatch` prefixes on `SetWaypoint`, `CheckUseOfReserves`, `ChangeRegimentFormation`, `AddOrderCourierline`, `CheckCurrentOrderUpdate` build snapshots before any enable check (bug-telemetry defaults false → computed and discarded); `CheckCurrentOrderUpdatePrefix` does uncached `GetNestedType`+`GetField` per call.
- `TacticalObserverPatch.Observe/BuildContext` runs ~9-10× per cycle per side before the dedupe check; `EmitOperationsMonitorTelemetryForSide` is O(K×N) per call.
- `TacticalRegimentDiagnosticsPatch` (default on, watch-list "Hampton,Tennessee" — a developer-specific default shipped to public users) walks all units reading `.name` every `CheckGlobalAIStrategy`.
- `WlCareerStartSelectionRetryPatch` does `AccessTools` lookups every `CampaignController.Update` frame while the panel exists.
- `FortConstructionGovernorPatch` scores every site against all forts/units/enemies per call before vanilla's early returns (job 21 calls it once per own unit).
- Telemetry shutdown: manifest written 3× and summary 2× (merge duplicate); `TelemetrySummary.FromDirectory` parses every JSONL row on the main thread at quit (up to 250 MB).

---

## 6. Config, persistence, telemetry

### Config default/description mismatches (fresh install now uses these)
| Plugin.cs | Setting | Default | Description says |
|---|---|---|---|
| 266 | Enable Tactical Observer | false | Default ON — **gates several Default-ON behaviors** |
| 300 | Enable Tactical Bug Telemetry | false | Default ON |
| 445 | Tactical Orchestrator Verbose Logging | false | Default ON (flag never read) |
| 512 | Enable Tactical Map Knowledge Diagnostics | false | Default ON (AGENTS says off by design → fix text) |
| 580/592 | Construction Site Steering / Telegraph AI | false | Default ON |
| 583/586/595 | Supply Depot / Fort / Railroad Steering | false | Default ON (dead flags) |
| 390 | Enable Tactical Artillery Doctrine (B7) | **true** | Default-off |
| 395 | Enable Tactical Withdrawal Doctrine (B8) | **true** | Default-off |
| 542 | Enable Tactical Deployment Terrain Discipline | **true** | Default off |
| 330/345 | "Enable Player Order Doctrine" (+ Diagnostics) | **bound twice** **[verified]** | BepInEx returns the first binding → effective default `true`; the second "default-off named exception" bind is a no-op (handoff still says default-off) |

Also dead/stale: `Telemetry Performance Warnings` (never read), `Fiscal Telemetry Csv`, `Max Railroad Starts`, "Reserved/future" wording on implemented `Emit Human Summary` and `Create Issue Bundle`. Version string frozen at 0.2.2. `TacticalCommanderModeValue` re-parses the string (Trim+ToLower) on every access in per-group hot paths.

### Persistence
- Non-atomic sidecar write; no `.bak`. Use temp + `File.Replace`.
- `SidecarDto.Version` written, never checked; no save identity; enum renames silently default; `ApplyDto` not transactional (null `Succession`/`FiredEvents` throws mid-apply, then `InitializeFromGameState` runs over half-applied state); `LastSeenMonth/Year` written but not restored.
- Not persisted: operational-probe state, defense cooldowns, fiscal memory, SIF signatures. After load, probe units sit in vanilla's (saved) offensive list with no mod owner.
- `ForceAllSuccessionEvents` writes forced "fired" ids into the sidecar permanently.

### Telemetry
- One write exception permanently disables the file sink for the session (AV/indexer sharing violation is enough). Retry with backoff.
- Queue: O(n) `DetailCount()`/`ProtectedCount()` per enqueue; protected row evicts a detail row needlessly when detail count is at capacity.
- `TelemetryWriter.Run()` has no outer try/catch — a throw silently kills the thread.
- Performance rows have higher queue priority than Decision/State, so under pressure they evict the doctrine rows the TacticalTuning profile exists to capture.

---

## 7. Hygiene, contracts, tests

- **Scope coverage is 106/132 patch entry methods, not "67/67".** The handoff counted files. Unscoped: all 25 non-anchor entries in `TacticalObserverPatch` (including `CheckGlobalAIStrategyPostfix`, which anchors the orchestrator tick) and the #61 Postfix. Duplicate scope name `tactical.posture-executor` used by two call sites; Coordinated-offensive scopes are named/layered `Tactical` though they patch `AICampaign` (mis-gated by profile).
- Raw `Plugin.Log.LogWarning` in per-frame catch blocks (`TacticalObserverPatch.cs:297, 331`, regiment/map diagnostics) — use keyed `OnceLog`.
- `AICampaignSaveLoadPatch.cs:67-68`: catch block calls `StrategicCoordinator.Instance.InitializeFromGameState()` unguarded — if Bootstrap threw, NRE escapes into vanilla `Load`.
- Finalizer guards swallow every NRE in the method, including NREs from the mod's own Postfixes (fallback/retreat), and `EconomyFilterMapInitializationGuardPatch` forces completion on any exception type.
- **All vanilla bug guards are still needed on the new build** (fallback/retreat/out-of-fire-range null derefs, W&L operation null-before-transform, economy alliance data, filter-map init, AssignFilters bootstrap, HQ auto-link, career-start retry, diary). Diary cycle-4 preflight is stricter than vanilla (treats null `groupsupplystate` as unsafe).
- **Test gaps:** `Strategic/CIC.cs` (719 lines — the core CIC logic) is **not compiled into the test project**; also `SuccessionScheduler`, `EraStageManager`, `ConstructionTelemetry` not compiled; `OperationDecisionMemory`, `DynamicArmyAreaDoctrine`, `GrandStrategyProfile` untested. Thin: `HistoricalOperationCatalog` (581 lines, 4 refs — and no test validates it against `campaignobjectives.dat`, which is how finding #5 shipped), `TacticalBattleLinePlanner`, `TacticalNestedDivisionPlayPlanner`, `TelemetrySummary`, `TelemetryWriter`. `Tactical/AGENTS.md` says runtime adapters are excluded from the test csproj; 10 are included.
- **Repo hygiene:** untracked `error.log` (contains a ChatGPT account_id), `.grok/`, `654890_47.jpg`, `notperfect.jpg` in a **public** repo, not covered by `.gitignore`.

---

## 8. Recommended slice order

1. **Battle-breaking fixes (small, surgical):** B8 → tactical fallback (no `SetWithdrawal`); #44 stamp `startoffallback` on transition to 3; #53 keep partials after iteration 15 + alliance gate; #61 artillery exclusion + no re-lay under fire + respect `macroai==3`/EOD; Finalizers on #46 and the candidate filter; Commander Mode enforced in every writer; fix Observer default + decouple behavior flags; dedupe the double Bind; fix description strings.
2. **Tactical evidence inputs (§3 T1-T7), each with a harness test that pins units/types:** ammo `float[]`, drop ÷3600, de-duplicate enemy counting + minimum enemy strength, per-side `AIBattle` for enemy reserves, replan triggers as change detectors that preserve phase, real main-effort history. Then geometry: project groups onto a lateral front axis for sectors, cascade order, refuse flanks and envelopment (T3/T4). Then battle-key lifecycle (T9) and battle-time clocks (T8). Then decide: wire phase/playbook into the operations director, or delete the dead machinery.
3. **Hitch measurement + telemetry/allocation fixes** (§5 fix order 1-6). Rewrite or retire the Slice 2 plan.
4. **Strategic plan lifecycle:** deadline-before-force in PhaseTruth with local ratios; battle-dirtying only for major/in-theater battles; per-alliance director clamp; coordinator reset on new campaign; atomic sidecar + version check + persist probe/cooldown/fiscal state.
5. **Catalog/data validation:** harness test validating every profile against `campaignobjectives.dat` (alliance, scenario, chapters); 1862-63 profiles for 001; NoProfile → generic plan fallback instead of blocking vanilla; CSA defensive ops target own-held objectives.
6. **Campaign write-surface corrections:** ImportanceValues as bonus not override; ArmyGroup guard; CommanderReplacement rewrite; DefensiveOps Prefix; ArmyArea exclusions; Coordinated-offensive commit gating; Fiscal directional bands; Transfer alternation.
7. **Strategy depth:** live-state objective scoring, theater-commander personality tier from vanilla commanders, bounded randomness among close scores, capital-threat streaks, sub-theater sectors.
8. **Gap slices:** supply-depot executor, naval/blockade + amphibious offense, march-to-the-guns (`LinkToFightingUnits`).

---

## 9. Not verified (needs runtime)

- Every hitch hypothesis (no telemetry survives); real `completeunitlist` size (docs disagree: 26,585 vs "200+").
- Whether B8's `onretreat` also blocks player right-click orders, and whether it persists across save/reload.
- Actual frequency of: #61 re-lay under fire, stance X/Y/Z oscillation, waypoint retry loop, plan resets from battles, probe flapping, cross-map pulls from ImportanceValues.
- Whether #44's scorer/orchestrator ever emits 3 in practice (Withdraw phase).
- Whether the feud `aibattle[2]` reaches `state >= 5` in W&L.
- Per-battlefield `battleprefs.txt` overrides of `sidestatupdatecycle` / day length.
- Method-body changes in today's game update (no pre-update DLL).

## 10. Doc corrections needed

- `docs/handoff.md`: "67 of 67 patches have scopes" → 106/132 entry methods; "`TacticalUnitObservationAggregate` is already a thread-safe immutable snapshot" → false; Slice 2 spec §1 "20 Hz" → ≈2.2 Hz at 20x; "#62 default-off" → effective default true; deployed-DLL hash is moot (BepInEx wiped).
- `docs/findings.md`: decompile line count 266,136; ±1 anchors are provenance noise.
- `AGENTS.md`: narrow the `transform.parent` rule; describe the actual rollback flag set until Mode=Off is a true rollback.
- `docs/patch-catalog.md`: #57 is telemetry-only (no longer reorders `reservegroups`); #41/#42 do stamp `lastfeudactiontime`.
