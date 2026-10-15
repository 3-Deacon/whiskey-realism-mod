# Re-anchor of 2026-10-14 full-mod review onto feat/orchestrator-worker-thread @ 25d64e3

Base = merge-base 262a50d (== review commit). Branch diff touches only: Plugin.cs, Util/ (3 new writers), Telemetry/TelemetryTagPolicy.cs, Tactical/** (69 files), and 7 Patches files (B7, BattleCommandPostureExecutor, BattleFeudActionGate, BattleGroupStance, TacticalReserveOrderDelayGuard + new ArtillerySetWaypointVeto, BattleMacroAIOverride, TacticalArtilleryApplier). **Strategic/**, Telemetry/ (except TagPolicy), Fiscal/Construction, and every Patch not listed above are byte-identical to main, so their file:line anchors equal the review's (marked "=main" below).
All paths relative to `.worktrees/orchestrator-worker-thread/src/WhiskeyRealism/` unless prefixed `tests/` or `docs/`. Status: SP = STILL-PRESENT, CH = CHANGED, FX = FIXED-ON-BRANCH, RN = FILE-GONE/RENAMED.

## §1 Top fixes

| ID | Finding | Status | file:line (25d64e3) | Note |
|---|---|---|---|---|
| R1 | B8 calls SetWithdrawal in battle (onretreat sticks) | SP | Patches/B8CheckUseOfReservesPatch.cs:175 (=main); flag Plugin.cs:481-485 | Unchanged; flag still default true, description still says "Default-off". |
| R2 | #44 writes macroai=3 without startoffallback | SP (+new path) | Patches/BattleMacroStrategyPatch.cs:73,99 (=main); NEW Patches/BattleMacroAIOverridePatch.cs:88 | New default-on override patch writes sideinformation.macroai (incl. 3 via Mapping:WithdrawalToFightLater) with no startoffallback. |
| R3 | Ops stuck in Recover; force check before deadline; f/(1-f) ratio | SP | Strategic/PhaseTruthLedger.cs:90-106; Strategic/CIC.cs:68-70; Strategic/StrategicCoordinator.cs:471-474 | Strategic/ untouched on branch. |
| R4 | Every battle dirties both CICs (reset to phase 0) | SP | Strategic/StrategicCoordinator.cs:1068-1085 (OnEventTrigger at 1082-1083) | Untouched. |
| R5 | Historical catalog mismatches campaignobjectives.dat; PickCampaignObjective blocks vanilla | SP | Strategic/HistoricalOperationCatalog.cs:383-461; Patches/PickCampaignObjectivePatch.cs:31-37 | Untouched; still no catalog-vs-.dat test (tests csproj:78 only compiles it). |
| R6 | Orchestrator evidence corrupt (ammo, ETA/3600, sectors, enemy count, reserves, trigger) | SP (partial elsewhere) | Tactical/Orchestrator/ArmyEvidenceBuilder.cs:192 (ammo), :932 (/3600f), :273-294 (sector=list idx, reserve 293-294), :377-436 (enemy overcount), Tactical/Orchestrator/ArmyTickCycle.cs:210 (MainEffortSectorLoss) | All reproduced; same ammo bug also in TacticalUnitObservationAggregate.Runtime.cs:234. Branch only added stance/hours/casualty inputs. |
| R7 | ImportanceValues overwrites every AIArea target | SP | Patches/ImportanceValuesPatch.cs:53-63 | Untouched. |
| R8 | #61 re-lays brigades in close combat ~5 s | CH (mostly SP) | Patches/BattleCommandPostureExecutorPatch.cs:732-779 (SetGroupFormation 764), :23 (5 s), :269-274 | Waypoint dedupe added (CommandPostureExecutor.cs:176-215, task-hold 1039-1083); formation path and missing combatstatussubordinates guard unchanged. |
| R9 | Facing pulse forces formation 0 (unlimbers artillery) | SP | Patches/BattleCommandPostureExecutorPatch.cs:1448 (targetFormation:0), :1510-1523, IsCombatRegiment incl. artillery :3399-3410 | Unchanged; no artillery/ReserveWait/FallBack exclusion. |
| R10 | #53 strips partial paths, no alliance gate | SP | Patches/TacticalPathfinderDisciplinePatch.cs:67-70 | Untouched. |
| R11 | Hitch = GC/telemetry volume + unscoped bursts | CH (partial mitigation) | Telemetry/TelemetryPerf.cs:115-130; TelemetryWriter.cs:249; TelemetryQueue.cs:77-97,177-189; Patches/TacticalPatchIds.cs:28-33; Patches/BattleCommandPostureExecutorPatch.cs:1978-1985,3926-3932 (GameObject.Find) | Telemetry/NodeIdMatches/Find unchanged. Branch added caches: CommandTreeRuntime, TacticalStableSceneCache, BGS cooldown (Plugin.cs:604), heavy-gate 4.0 s (Plugin.cs:503-506). |
| R12 | Slice-2 worker plan: change required | CH | Plan/spec moved to docs/superpowers/{plans,specs}/archive/2026-05-19-*; implemented: Tactical/Orchestrator/TacticalOrchestratorWorker.cs, TacticalOrchestratorWorkerAnalysis.cs, TacticalOrchestratorWorkerInput.cs:62-110, Coordinator Runtime:438,1778 | Executed anyway (default on, Plugin.cs:582). Observations copied to arrays (immune to Shared reuse); main still builds all heavy inputs. docs/handoff.md:237-238 stale. |
| R13 | Observer default false, described "Default ON"; dependents inert | SP | Plugin.cs:354-356 | Unchanged (name line 354). |
| R14 | Mode=Off not full rollback; MonitorOnly not write-free; bad string -> Active | SP (worse) | Patches/B7CheckAIBombardmentPatch.cs (no mode check), B8CheckUseOfReservesPatch.cs:~120, BattleChargeGatePatch.cs:176-185, Tactical/Operations/TacticalCommanderMode.cs:12-29 | New writers also ignore mode: ArtillerySetWaypointVetoPatch.cs, TacticalArtilleryApplier.cs, BattleMacroAIOverridePatch.cs, Util/Tactical*Writer.cs. |
| R15 | No strategic reset between campaigns; non-atomic sidecar | SP | Strategic/StrategicCoordinator.cs:88-113, 1442 (WriteAllText), 1448-1545 | Untouched. |
| R16 | Director 1-publish/s clamp shared across alliances | SP | Strategic/DirectorPublishClamp.cs; Strategic/StrategicCoordinator.cs:394 | Untouched. |
| R17 | Battle teardown probably never fires | SP (teardown body grown) | Patches/TacticalObserverPatch.cs:270-290 (lifecycle), Tactical/Orchestrator/TacticalBattleCoordinatorRuntime.cs:181 (`if (active) return`), :300-360 (OnBattleEnd) | Trigger (completeunitlist zero) unchanged; OnBattleEnd now resets more caches but still depends on same detector. |

## §2 Campaign / strategic

| ID | Finding | Status | file:line | Note |
|---|---|---|---|---|
| C-ArmyGroup | Ignores vanilla allowarmygroups guard; FindPreferredCommander gaps | SP | Patches/ArmyGroupManagementPatch.cs:30-38, 206-225 | =main. |
| C-CmdrReplace | CommanderReplacement never fires / wrong target | SP | Patches/CommanderReplacementPatch.cs:105,140,172-213 | =main. |
| C-DefOps | DefensiveOps Postfix churn | SP | Patches/DefensiveOpsPatch.cs:102-106 | =main. |
| C-ArmyArea | ArmyArea oscillation | SP | Strategic/ArmyAreaRuntime.cs:180-239 | =main. |
| C-Coastal | CoastalDefense runner no DefensiveMovingOrder | SP | Strategic/CoastalDefenseCustomOrderRunner.cs:111-119 | =main. |
| C-CoordOff | CoordinatedOffensive empties ownunits, eligibility gaps | SP | Patches/CoordinatedOffensiveOperationsPatch.cs:84-147; Strategic/CoordinatedOperationPackageLedger.cs:238-241 | =main. |
| C-Transfer | TransferOfUnits replaces deficit with plan target | SP | Patches/TransferOfUnitsPatch.cs:92-93,150,171 | =main. |
| C-Recruit | Recruitment drafts-only eligible; default East | SP | Strategic/RecruitmentIntentLedger.cs:161-165; Patches/RecruitmentPatch.cs:243-246 | =main. |
| C-Fiscal | FinancialAI reverts vanilla nudges | SP | Patches/FinancialAIPatch.cs:43-46; Strategic/Fiscal/FiscalIntentLedger.cs:474-486 | =main. |
| C-EconConstr | Leaves biased bestiipplacesprob | SP | Patches/EconomyConstructionPatch.cs:121-130 | =main. |
| C-ConstrRuntime | TopSupplyTheater key mismatch; diluted capital threat | SP | Strategic/Construction/ConstructionRuntime.cs:81-104,208,286-292 | =main. |
| C-Probe | OperationalProbe pause/withdraw no recall; flapping | SP | Strategic/OperationalProbeRuntime.cs:147-157 | =main. |
| C-Governor | CampaignAiUpdateGovernor caps passes | SP | Patches/CampaignAiUpdateGovernorPatch.cs:40-81 | =main. |
| C-DefIntent | AmbiguousMatch on GetUnitStrengthInRange | SP | Strategic/DefenseIntentRuntime.cs:272 | =main. |
| C-DeadRefl-men | Regiment.numofmenoriginal dead name | SP | Tactical/Orchestrator/ArmyEvidenceBuilder.cs:1015 | Shifted from 917 (+98 insert); still falls back to count. |
| C-DeadRefl-scene | SceneManagement.currentcampaign/currentsave | SP | Strategic/WlStrategicOrderBridge.cs:513 | Strategic/ file, unchanged on branch. |
| C-DeadRefl-side | Regiment.side | SP | Patches/TacticalObserverPatch.cs:2520 | =main. |
| C-Personality | Personality barely moves decisions; CIC fixed; deterministic ops | SP | Strategic/ObjectiveScoring.cs; Strategic/HistoricalOperationCatalog.cs (Score) | Untouched. |
| C-HardWired | Responsiveness signals hard-wired off | SP | Strategic/HistoricalOperationContextBuilder.cs:83; Strategic/StrategicCoordinator.cs:1051-1052 | =main. |
| C-Perk | Perk selection deterministic | SP | Strategic/PerkSelectionScorer.cs | Untouched. |
| C-CicRouter | CicRouter drifted from CIC.ReviewPlanWithTruth | SP | Strategic/CicReviewRouter.cs vs Strategic/CIC.cs | Untouched; CIC.cs still not in tests csproj. |
| C-GapMap | Vanilla jobs not touched (depots, naval, LinkToFightingUnits...) | SP | n/a (absence) | No new campaign code on branch. |

## §3 Tactical orchestrator logic

| ID | Finding | Status | file:line | Note |
|---|---|---|---|---|
| H-ledger | Ledger/OperationMacroAi drives macroai; CurrentMacroAi only Mode=Off | SP | Patches/BattleMacroStrategyPatch.cs:86,113-118 (OperationMacroAi); Tactical/Operations/TacticalOperationsLedgerModel.cs:120-148 | Plus new second driver: BattleMacroAIOverridePatch (opportunity doctrine -> sideinformation.macroai), Plugin.cs:743 default true. |
| H-dead | Playbooks same shape; EmitArmyIntent no caller; ReserveCommitTriggerOdds unread | CH | ArmyOrchestrator.cs:409 (no callers); TacticalBattlePlan.cs:77,100 (unread); Playbooks/*.cs renamed | EmitArmyIntent and ReserveCommitTriggerOdds still dead. Geometric Plays/** now feeds roles via AllocateWithZones. |
| T1 | Ammo reads 0 (float[] vs scalar) | SP | Tactical/Orchestrator/ArmyEvidenceBuilder.cs:192; also TacticalUnitObservationAggregate.Runtime.cs:234 | Unchanged; worker-path aggregate copy has same bug. |
| T2 | Reinforcement ETA /3600 | SP | ArmyEvidenceBuilder.cs:930-932 | Shifted from 832-834. |
| T3 | Sectors are list indices | CH (partial) | ArmyEvidenceBuilder.cs:273-290; TacticalBattleCoordinatorRuntime.cs:1642-1665,1741-1765; DirectChildAllocator.cs | Index map unchanged. Wing flank bucket 2 < new refuse threshold 3 (no auto-refuse); Plays/ add lateral-axis geometry. |
| T4 | Cascade orders by world X; flank exposure hard-coded 0 | SP | Tactical/Orchestrator/TacticalCommandTreeProbe.cs:196,243-246; TacticalLeafBrigadeMap.cs:200,229 | Files untouched. |
| T5 | Enemy odds over-count; no min enemy strength | SP | ArmyEvidenceBuilder.cs:377-436; EnemyVisibleState.cs:25 | Unchanged. |
| T6 | Enemy intent on sentinel (reserve 0.35; AnyContactBroken never set) | SP | ArmyEvidenceBuilder.cs:271-294 (anyContactBroken), :673 (objectivechain); Coordinator Runtime Tick (~375) | Unchanged. |
| T7 | Replan triggers broken (SectorLoss same value; level conditions) | CH (SP + new trigger) | ArmyTickCycle.cs:202-218; ArmyReplanTriggers.cs:146-161 | History=current still at ArmyTickCycle:210; added OpportunityChange trigger (ArmyReplanTriggers.cs:~200, level-vs-last, cooled by minReplanSeconds). |
| T8 | Timers on wrong clocks | SP | TacticalPhaseProgressionDoctrine.cs:30-32,248-251; ArmyReplanTriggers.cs:107 (180 s); TacticalOperationsLedgerRuntime.cs:105-141 | Unchanged. Branch added SafeReadBattleHours (ArmyEvidenceBuilder.cs:529) for doctrine only. New wallclock timers: hysteresis 6 s, task hold 12 s. |
| T9 | Teardown never fires; stale orchestrators; clamp negative elapsed | SP | TacticalObserverPatch.cs:270-290; CoordinatorRuntime.cs:181; TacticalHeavyPathGate.cs:103-104 | Detector unchanged. OnBattleEnd now also clears worker, scene cache, command-tree cache, resolved cache (:300-360, ~2165-2200). |
| T10 | Ignores vanilla hard macroai overrides | SP (worse) | BattleMacroStrategyPatch.cs:79-110; BattleMacroAIOverridePatch.cs:63-90 | New patch overwrites sideinformation.macroai, skips GameVars.aistrategy/scripted check; NoOpportunity leaves stale value. |
| T11 | Fog of war: AI reads enemy totals/reinforcements | SP | ArmyEvidenceBuilder.cs:113,125-126,293-296,863-873 (intel 0.8), :910-935 (schedule) | Shifted +81 after line 484; unchanged logic. |
| T12 | Playbook selection collapses; terrain always Open; name collisions | CH | ArmyEvidenceBuilder.cs:118,252; TacticalPlaybookCatalog.cs (rewritten); Playbooks/*.cs renamed | FactionAffinity removed; stance hard-filter + preconditions + Defensive bias added. Terrain still Open; registry surname collisions unchanged. |
| T13 | Fix/Screen roles and operation phases never occur | CH (partial) | DirectChildAllocator.cs:168,185; Plays/PlayCorpsAssignment.cs:202-210 | Fix/Screen now reachable via play zones. Operation phases unchanged (TacticalOperationDirector.cs). |
| T14 | Committed ops drop to Scouting below 0.65 conf; no hysteresis | SP | Tactical/Operations/TacticalOperationDirector.cs:96-102,204 | Untouched. |
| T15 | Reserves can never be released | SP (worse) | Tactical/Orchestrator/TacticalReserveCommitGate.cs:69; DirectChildAllocator.cs (Reserve fall-through default) | Gate untouched; allocator now defaults leftover brigades to Reserve. |
| T16 | Corps roles reallocated every tick, no hysteresis | SP | ArmyOrchestrator.cs:~636-651 (ObserveDirectChildEvidenceWithZones forces realloc); CoordinatorRuntime.cs:1708 | Role budgets added (Max Main/Support/Refuse) but no stickiness. |
| T17 | Absolute morale floor 0.4 | SP | ArmyTickCycle.cs:202,214 | Unchanged. |
| T-charge | Charge gate denies counter-charge for Fix/Refuse | SP | Patches/BattleChargeGatePatch.cs | Untouched. |
| T-settle | TacticalOrderSettlementGate treats marching as settled | SP | Tactical/TacticalOrderSettlementGate.cs | Untouched. |
| T-comment | Comment/threshold mismatch in ReinforcementOpportunityDoctrine | SP | Tactical/Orchestrator/TacticalReinforcementOpportunityDoctrine.cs:83-89, ComputeAttackThreshold 362-366 | Comment says 1.20 at aggression 0.5; formula gives 1.32. File rewritten, mismatch persists. |
| T-notreviewed | Un-reviewed files (Scourge doctrine, intelligence pack, etc.) | CH | Operations/TacticalScourgeInspiredDoctrine.cs (+20), CommandPostureExecutor.cs (+33), new Operations/TacticalDoctrineOrderResolver.cs, TacticalLedgerStateResolver.cs | Still unreviewed; new files unreviewed too. |

## §4 Battle writer patches

| ID | Finding | Status | file:line | Note |
|---|---|---|---|---|
| B-leaf | #61 leaf-brigade moves for any non-None task toward parent waypoint | CH (partial) | Patches/BattleCommandPostureExecutorPatch.cs:2794-2886, 2929-3005 | Added movement-in-progress and recent-order gates (2815-2829) and arrival hold (2593-2700); still moves Hold/ReserveWait toward parent waypoint, transform.parent at 2947. |
| B-stance3 | Several writers set ai_stance per UpdateAITasks | CH (worse) | Patches/BattleGroupStancePatch.cs:92-110,1209-1211,1396-1398; BattleCommandPostureExecutorPatch.cs:3866-3881; Util/TacticalStanceWriter.cs; B7:392, BattleFeudActionGatePatch.cs:~108, TacticalReserveOrderDelayGuardPatch.cs:~121,~175 | BGS gained cooldown/OrderFriction gate, but now 5+ callers write stance (#45, #61, B7, feud, reserve guard, veto). |
| B-macro3 | #61 ignores macroai==3 and EOD | SP | Patches/BattleCommandPostureExecutorPatch.cs:94 (`macro < 0` only) | Unchanged. |
| B-divplay | #61 division play overrides Hold; ParentNodeKey uses transform.parent | SP | BattleCommandPostureExecutorPatch.cs:97, 402-445, 3769-3790 | Unchanged. |
| B-60 | #60 shifts whole group (targetpos) | SP | Patches/TacticalDeploymentTerrainDisciplinePatch.cs:275-295 | Untouched. |
| B-wlpred | Inconsistent W&L/player protection; M6 feud aibattle[2] | SP | BattleChargeGatePatch.cs:270; BattleReserveCommitGatePatch.cs:871; B7CheckAIBombardmentPatch.cs:48,322; TacticalReserveOrderDelayGuardPatch.cs:161; BattleCommandPostureExecutorPatch.cs:95 | No shared predicate; new veto patch adds another ad-hoc one (ArtillerySetWaypointVetoPatch.cs:90). |
| B-61state | #61 writes orchestrator state from Postfix | SP | BattleCommandPostureExecutorPatch.cs:2406,2427,2441 | Still UpdateLeafBrigadeMap, UpdateLiveCommanderInitiative, UpdateReinforcementOpportunity. |
| B-61clock | #61 cooldowns real-time; failed SetWaypoint no cooldown; bypass order delay | CH (partial) | BattleCommandPostureExecutorPatch.cs:45-50,1039-1083,2873 | Still realtimeSinceStartup. Leaf path now stamps cooldown unconditionally; main path unchanged. Task-hold adds wallclock 12 s. |
| B-46 | #46 / DefensiveOps filter no Finalizer | SP | Patches/BattleObjectiveChainWlGuardPatch.cs:49,94; Patches/CheckForDefensiveOperationsCandidateFilterPatch.cs:60,166 | Untouched. |
| B-b7 | B7 thrash; never releases counter-battery | SP | Patches/B7CheckAIBombardmentPatch.cs:298-350, 388-400 | Only a stance write added. New TacticalArtilleryApplier/BGS audit add separate artillery writers. |
| B-59 | #59 snapshots every attached unit | SP | Patches/BattleReserveCommitGatePatch.cs:418,432 | Untouched. |
| B-42 | Feud gate drops distance cap; stamps lastfeudactiontime | SP | Patches/BattleFeudActionGatePatch.cs:83,298; BattleChargeGatePatch.cs:89,140,156 | +EnsureStance(3) added. |
| B-34 | #34 gun transfer non-durable | SP | Patches/ArtilleryCombineGunTransferPatch.cs:38 | Untouched. |
| B-62 | #62 dedupe depends on diagnostics | SP | Patches/PlayerSubordinateOrderPatch.cs:65-66,91 | Untouched. |
| B-agentsrule | AGENTS.md transform.parent rule too strong | SP | AGENTS.md:239 | Not narrowed; branch AGENTS.md only added Scope-B/play docs. |

## §5 Performance

| ID | Finding | Status | file:line | Note |
|---|---|---|---|---|
| P-facts | No battle catch-up loop; 2.2 Hz at 20x | SP (doc) | docs/handoff.md:237-238; docs/superpowers/specs/archive/2026-05-19-...worker-thread-design.md | Spec still states 20 Hz; archived, not corrected. |
| P-gc-telemetry | Per-call telemetry rows (PerfScope+snapshot) | SP | Telemetry/TelemetryPerf.cs:115-130 | Untouched; Dispose still emits row unconditionally. |
| P-hier-walk | Per-tick .name hierarchy walks; CommandTreeSignature strings | CH (partial FX) | Tactical/Orchestrator/CommandTreeRuntime.cs (cache hit/miss); TacticalBattleCoordinatorRuntime.cs:1350,1366,1413 (signature strings); BuildInstanceToSectorIndexMap :1741, called 1642,1843-1844 | Tree snapshot cached per battle (FX). Signature strings, sector map walks, DirectChildDiscovery.Snapshot (:1289) still per cycle. |
| P-nodeid | NodeIdMatches allocates "node-"+id | SP | Patches/TacticalPatchIds.cs:28-33 | Untouched. |
| P-dict-growth | Unbounded _lastEmittedAt/_lastLoggedAt/OnceLog dicts | CH (partial) | Util/OnceLog.cs; TacticalBattleCoordinatorRuntime.cs:2165-2210; BattleCommandPostureExecutorPatch.cs:4109-4118 | More resets at battle end; OnceLog and observer dedupe dicts still not cleared. |
| P-61burst | #61 bursts (CalculatePath x3, GameObject.Find, blocker walk), no outer scope | SP | BattleCommandPostureExecutorPatch.cs:62-77 (no scope), :309 (inner only), :1833, :1931, :1978-1985, :3926-3932 | Unchanged; probe cache frame-keyed (:2341-2360) still never hits. |
| P-queue | Writer AppendAllText per row; O(n) queue; manifest per batch | SP | Telemetry/TelemetryWriter.cs:249,191,398; TelemetryQueue.cs:77-104,157-189 | Untouched. |
| P-debuglog | Vanilla Debug.Log stack captures | SP | n/a (vanilla) | Not addressed. |
| P-statsui | Stats UI forces recomputes | SP | n/a (vanilla) | Not addressed. |
| P-probe | Per-frame hitch probe | SP (absent) | none | No probe added; TacticalCacheValidator is cache-parity only. |
| P-order | Fix order 1-7 (scope #61; slow-row telemetry; shared snapshot; cache BFS; int ids; clear dicts; threading last) | CH | see R11,R12 | Branch did #3 partly, #4 scene cache, #7 (threading) first. #1,#2,#5 not done. |
| P-observer-prefix | Observer prefixes build snapshots before enable check; GetNestedType per call | SP | Patches/TacticalObserverPatch.cs:446,521,548,593,625,2183 | Untouched. |
| P-observer-ctx | Observe/BuildContext ~9-10x per cycle; O(K*N) ops telemetry | SP | TacticalObserverPatch.cs:259-300,700-709 | Untouched. |
| P-regdiag | RegimentDiagnostics default watch "Hampton,Tennessee" | SP | Plugin.cs:713 | Unchanged. |
| P-wlretry | WlCareerStartSelectionRetry AccessTools each frame | SP | Patches/WlCareerStartSelectionRetryPatch.cs | Untouched. |
| P-fort | FortConstructionGovernor scores all sites | SP | Patches/FortConstructionGovernorPatch.cs | Untouched. |
| P-shutdown | Manifest 3x, summary 2x, FromDirectory on main | SP | Telemetry/TelemetryRuntime.cs:318-349,467; TelemetrySummary.cs:122 | Untouched. |

## §6 Config, persistence, telemetry

| ID | Finding | Status | file:line | Note |
|---|---|---|---|---|
| CFG-observer | Enable Tactical Observer false/"Default ON" | SP | Plugin.cs:354-356 | |
| CFG-bugtel | Bug Telemetry false/"Default ON" | SP | Plugin.cs:388-390 | |
| CFG-verbose | Orchestrator Verbose Logging false/"Default ON"; never read | SP | Plugin.cs:531 | Still no reader. |
| CFG-mapknow | Map Knowledge Diagnostics false/"Default ON" | SP | Plugin.cs:720-725 | AGENTS says off by design; text unfixed. |
| CFG-construct | Construction Site Steering / Telegraph false/"Default ON" | SP | Plugin.cs:803-805,815-817 | |
| CFG-deadflags | Supply Depot / Fort / Railroad Steering dead flags | SP | Plugin.cs:806-811,818-820 | |
| CFG-b7 | B7 default true / "Default-off" | SP | Plugin.cs:478-480 | |
| CFG-b8 | B8 default true / "Default-off" | SP | Plugin.cs:481-485 | |
| CFG-deploy | Deployment Terrain Discipline true / "Default off" | SP | Plugin.cs:767-769 | |
| CFG-dupbind | Player Order Doctrine double-bound | SP | Plugin.cs:418-420 (first, true), 433-435 (second, false no-op) | |
| CFG-deadmisc | Telemetry Perf Warnings, Fiscal Csv, Max Railroad Starts, "Reserved" wording | SP | Plugin.cs:283-284, 318-320, 321-325 (read at 971 only), 326-330, 830-832 | Perf Warnings now read at Plugin.cs:971; Version "0.2.2" at Plugin.cs:16. |
| CFG-modeparse | Mode string re-parsed each access | SP | Plugin.cs:232-252 | |
| CFG-newflags | (new) Branch flags with defaults | CH | Plugin.cs:582 worker(true), 604 BGS interval, 743 MacroAiSync(true), 751 GeometricPlay(true) | New default-on writers; no mode gate. |
| PER-atomic | Non-atomic sidecar, no .bak | SP | Strategic/StrategicCoordinator.cs:1442 | |
| PER-version | SidecarDto.Version unchecked; ApplyDto not transactional; LastSeen not restored | SP | Strategic/StrategicCoordinator.cs:1448-1545 | |
| PER-notpersisted | Probe/cooldowns/fiscal/SIF not persisted | SP | Strategic/OperationalProbeRuntime.cs etc. | |
| PER-force | ForceAllSuccessionEvents persists forced ids | SP | Strategic/StrategicCoordinator.cs:213 | |
| TEL-sink | One write exception disables sink | SP | Telemetry/TelemetryWriter.cs:33,125 | |
| TEL-queue | O(n) DetailCount/ProtectedCount; needless eviction | SP | Telemetry/TelemetryQueue.cs:77-104,177-189 | |
| TEL-run | Run() no outer try/catch | SP | Telemetry/TelemetryWriter.cs:137-151 | |
| TEL-prio | Performance priority above Decision/State | SP | Telemetry/TelemetryQueue.cs:217-235 | |

## §7 Hygiene, contracts, tests

| ID | Finding | Status | file:line | Note |
|---|---|---|---|---|
| H-scope | Scope coverage < 100% (25 observer entries, #61 Postfix) | SP | Patches/TacticalObserverPatch.cs (25 entries, 2 scopes); BattleCommandPostureExecutorPatch.cs:62 | New patches (veto, macro-override) are scoped. |
| H-dupscope | Duplicate tactical.posture-executor; coordinated-offensive layer | SP | BattleCommandPostureExecutorPatch.cs:309; TacticalObserverPatch.cs:777 | |
| H-rawwarn | Raw LogWarning in per-frame catches | SP | TacticalObserverPatch.cs:297,331 | |
| H-saveload | AICampaignSaveLoadPatch catch unguarded Init | SP | Patches/AICampaignSaveLoadPatch.cs:67-68 | |
| H-finalizer | Finalizer guards swallow own NREs | SP | Patches/Economy*Guard*, Tactical*NullGuard patches | Untouched. |
| H-guards | Vanilla bug guards still needed | n/a | n/a | No change. |
| H-tests | Test gaps (CIC.cs not compiled; catalog not validated vs .dat) | SP | tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj | CIC.cs not in csproj; csproj added branch files and two new test files only. |
| H-hygiene | Untracked error.log, .grok/, jpgs | FX | .gitignore:12-16 | Now ignored (`error.log`, `.grok/`, `*.jpg`); nothing tracked. |
| D-handoff | handoff.md stale lines | SP | docs/handoff.md:237-238 | "67 of 67" and "thread-safe immutable snapshot" unchanged. |
| D-findings | findings.md decompile count | SP | docs/findings.md:23 | Still 266,134. |
| D-agents | AGENTS.md narrow transform.parent rule / rollback set | SP | AGENTS.md:239 | |
| D-catalog | patch-catalog #57/#41 | SP | docs/patch-catalog.md:51,67 | #57 row still says reorders reservegroups. |

## New branch files that interact with review findings
- Patches/BattleMacroAIOverridePatch.cs (R2, T10, R14): sideinformation.macroai write incl. 3; no startoffallback; no scripted/aistrategy check; no mode gate; never resets to -1.
- Patches/ArtillerySetWaypointVetoPatch.cs (R14, B-wlpred): global Postfix on BattleUnits.SetWaypoint; mode ungated; also calls ChangeRegimentFormation(0, mounted 1).
- Patches/TacticalArtilleryApplier.cs + BGS ApplyArtilleryAudit (B-b7): second artillery writer set.
- Util/TacticalStanceWriter.cs / TacticalFormationWriter.cs / TacticalCombatBehaviorWriter.cs (R14, B-stance3): new write funnel without mode check.
- Tactical/Orchestrator/Plays/** (T3, T4, T13): geometric lateral-axis layer; partial mitigation only when EnableGeometricPlayTemplates (true).
- TacticalOrchestratorWorker*.cs, TacticalDecisionSnapshot.cs, WorkerObservationSourceAdapter.cs, TacticalCacheValidator.cs, TacticalStableSceneCache.cs (R11, R12).
- TacticalRegimentalIntelligence*.cs, TacticalFriendlyPathBlockingGate.cs, TacticalFriendlyFireGate.cs, TacticalRegimentalPositioner.cs: new regimental writers, default off.
- TacticalStanceReconciler.cs, TacticalOpportunityHysteresis.cs, TacticalMainArrivalSynchronizer.cs: new wallclock-based hysteresis (T8 flavor).
