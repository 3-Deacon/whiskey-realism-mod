# Tactical Operations Ledger

Living reference for the tactical operations-ledger command system, active command assignments, posture execution, smoke checks, and rollback.

## Current State

- **Implementation state:** full-spectrum tactical command doctrine is implemented and intended for `main`; release/default config is `Tactical Commander Mode = Active`.
- **Patch ordinal:** #61 `BattleCommandPostureExecutorPatch`; #62 `PlayerSubordinateOrderPatch` is the default-off W&L player-subordinate order bridge.
- **Config contract:** `Active` is the release/default mode; `MonitorOnly` is for smoke and diagnostics; rollback is `Off`.
- **Build/deploy proof:** console harness exits 0 across `1110` registered tests; `./build.sh` passed with `0 Warning(s)` / `0 Error(s)`; local `dist/WhiskeyRealism.dll` and deployed BepInEx plugin match SHA-256 `562a61b5cd0cbbedc6d6002a349cd3d68ebf50ea1d60c941e3a5a9deeaafc57a` (1327104 bytes).
- **Runtime smoke:** pending for the current DLL. The freshest loaded TacticalTuning session was the older `695be770...` DLL; it proved Active mode, sidecar output, and order issuance, but exposed stale `groupsubordinatesmoving`/`activeMove` movement classification. Only a fresh GTCW restart with a loaded `562a61b5...` manifest/log can prove Active operations-ledger runtime behavior for this build.

The system turns the tactical orchestrator's command tree into a per-side operations ledger. The ledger classifies the current battle operation, assigns command-node tasks, monitors whether assigned commands are validly idle or illegally stuck, and lets #61 issue bounded vanilla commands only when the mode is `Active`.

The objective anchor path now consumes real map objectives before synthetic movement evidence. `TacticalVisionRuntimeAdapter` first consumes `AIBattle.objectivechain`, then `Regiment.currentsetobjective`, then live scene `Objectives : MonoBehaviour` instances (`objectivename`, `owner`, transform position). It also reads vanilla `BattleUnits.entrypoints`, `BattleUnits.scheduledarrival`, and `BattleUnits.grp` to build `TacticalApproachAvenueEstimate` route predictions from entrypoint `entrypointposition` / `trooptargetposition`, scheduled reinforcement entry points, and deployment start/objective positions. Runtime avenue extraction now marks crossing anchors from bounded `BattlefieldSetup.SearchTerrainInRangePos` checks around the origin, target, and midpoint. If map objectives are unavailable and no visible enemy line is legal under FOW, it promotes weighted vanilla `Regiment.lastsetwaypointposition` to `movement-anchor-<alliance>`. The battle-line planner can then form and advance on a verified objective or vanilla movement objective instead of returning `battle-line-no-objective`, and when an approach avenue exists it orients frontage perpendicular to enemy-origin -> objective instead of own-node -> objective.

The Scourge-style outbound order layer is now live in #61. Successful posture writes record a stable command signature per group; if the child still has the same pending order, later duplicate writes are suppressed with `outbound-duplicate-pending-order`. Materially changed commands still pass through, so the ledger does not freeze a unit on stale intent.

The 2026-05-19 objective-continuation fix closes the latest runtime blocker. TacticalTuning sidecars showed repeated posture writes and `macro=attack` intent, but positions often stopped after the first short leg. The false blocker was `activeMove=true` with no group or subordinate paths, caused by stale `groupsubordinatesmoving` state. #61 and `CommandPostureExecutor` now pass the total group/subordinate path count into duplicate suppression and active-move classification; stale movement flags without paths no longer count as a real active move, so probe/screen/scout/attack tasks can reissue objective movement when still far from the target.

The 2026-05-10 log review first found `1st_Brigade#-27662` repeatedly in `MarchColumn` with `pathInterrupted=True`, `paths=0`, `activeMove=False`, and `queue=0` while the old idle classifier still treated `HoldObjective` as valid idle. That fix makes interrupted non-reserve hold/fallback tasks illegal idle, lets the posture executor recover them with a bounded `RecoveryPath` waypoint, emits ledger telemetry in both `Active` and `MonitorOnly`, and falls back from missing exact command-operation snapshots to `ArmyOrchestrator.ResolveCommandIntentForGroup(...)` plus the current operations ledger before deciding a write. A later 2026-05-10 log review of the `1st Brigade` / `38th New York` courier traffic found the next blocker: command-tree nodes were keyed by the `Regiment.gameObject.GetInstanceID()` value, while #61, #41, #57, #59, B8 fallback observation, #35 monitor lookup, and #45 stance lookup were resolving with the `Regiment` component `GetInstanceID()` value. Current code resolves command intent and operations-ledger nodes by GameObject id first and component id as fallback, so live command consumers can attach to the ledger rows they are supposed to execute. Hampton's Legion / 8th Brigade then exposed allocator-ordering blockers: the direct-child allocator could assign `Fix`, and later `Main`, before testing severe local overmatch. A badly outmatched command now receives `Fallback` before either `Fix` or `Main`, causing isolated commands under active pressure to withdraw toward fallback behavior instead of continuing an unsupported pin or main-effort attack. The 1st/3rd Brigade facing trace exposed a #61 formation-state blocker: `groupformation` could already be `Line` while the visible `formation` was still `MarchColumn`, so the executor could misclassify defensive formation work or wait behind recent-order cooldowns while units remained visibly exposed. Formation correction now checks visible, ordered, and group formation, close defensive/fallback refreshes use the visible threat bearing as `manualfinalrotation`, and urgent visible-mismatch retries use a 5-second cooldown. The Hampton flank cluster exposed the mirror problem on the Confederate side: a close-engaged attacking command with `flanksthreated` / `outflanked` evidence could stay in `AttackObjective` and be blocked by courier `order-pending`. #61 now treats that as a local flank emergency and temporarily executes the posture as `GuardFlank`. The fresh Hampton log also showed close defensive/fallback formation corrections could still be blocked by pending courier state, then create a fresh vanilla formation path that left units visually in interrupted `MarchColumn`; close defensive/fallback local reform now bypasses pending courier state for the non-moving correction and avoids `SetGroupFormation(newpath:true)` while close engaged so line/facing refreshes reform in place.

The 2026-05-11 full-spectrum doctrine implementation extends the ledger from posture recovery into battle command. `TacticalBattlefieldPicture` raises objective confidence from visible formed infantry/cavalry/battle-command contact and keeps skirmisher, permanently-detached, and cavalry-screen evidence from being treated as an exposed main line. Named detachments are no longer screened out by name alone; if vanilla reports a formed infantry/cavalry detachment or battle command group with strength and it is not marked permanently detached, it can raise sector confidence. `TacticalOperationDirector` selects and commits battle operations, `CommandDoctrineOrder` and `CommandDoctrineAssignment` publish primary/support/fallback targets, and `DoctrineConsumerDecisions` retargets #45 stance, #41 charge, B8 reserve/fallback, and B7 artillery consumers to that ledger. Known objective-id misses fail closed; coordinate fallback is allowed only for unknown-objective doctrine targets. A doctrine charge allow cannot override W&L/player gates, the orchestrator charge gate, or B6c explicit denial. Reserve deny now owns a full #59-style movement-state rollback in B8, while #56 order-delay conversion deliberately skips doctrine-denied movement so rollback ownership is deterministic. Artillery support-main-effort currently emits bounded `SuppressStrongpoint` telemetry without inventing a new bombardment write, while friendly-close doctrine remains conservative and cancels active bombardment.

The 2026-05-11 post-deploy log review found two additional defects in the Active smoke surface. `TacticalObserverPatch` was still simulating posture telemetry with `modeAllowsWrites=false`, causing Active ledger rows to report `reason=mode-monitor-only`; the observer now uses the side ledger's actual `CommanderMode`. `TacticalOperationDirector` also soft-aborted committed operations solely when `ReserveFraction < 0.05`, even if the primary objective had decisive odds; low reserves now trigger a soft abort only when odds are no longer favorable, while true odds collapse still aborts immediately. The 2026-05-12 log review added two more narrow fixes: battle command groups (`unittyp` 14-16) count as visible formed-line contact for sector confidence, and duplicate waypoint suppression no longer hides a stale attack order when the group is still far from the target with `paths=0` and `activeMove=False`.

The later Hampton's Legion live-log review found 8th Brigade repeatedly reporting `pathInterrupted=True`, `activeMove=False`, `orderState=1`, stale path segments, and waypoint `x=1617,z=-1481` while #61 kept replaying `RecoverInterruptedOrder target=RecoveryPath`. That made Hampton's command crawl along the old path instead of replacing it with the current fallback/formation task. `TacticalOrderSettlementGate` now treats stalled interrupted orders as retaskable even when vanilla still reports stale path segments, and doctrine `FallBackToLine` overrides interrupted stale recovery with the doctrine fallback target instead of replaying the last safe waypoint.

The next Active log review found CSA still in `macro=attack` with favorable odds while command rows degraded to `mission=Hold enemy=0 confidence=0.5` and repeated assembly-area `move-new` orders. The fixes now raise visible enemy-line fallback objective confidence, append visible enemy-line objectives alongside vanilla objective-chain/current-objective anchors, prefer exposed enemy-line objectives over inert generic anchors during operation selection, allow committed main-effort enemy-line attacks at fallback confidence when the line is exposed and odds are favorable, classify named formed detachments and vanilla battle command groups as real line contact instead of zeroing the sector, and skip duplicate #61 waypoint writes only while the command still has an active path or is already at the target. Stale no-path/no-active-move attack orders are reissued. This is intended to stop committed attacks from collapsing into perpetual `FormUp`/assembly churn while preserving screen/skirmisher and permanently detached safeguards.

The 2026-05-14 command-doctrine pass adds side-wide battle-line planning instead of giving every child the same objective point. `TacticalDecisionDoctrine` classifies contact before commitment, so screen/skirmisher contact produces scouting/probing and a committed attack downgrades or aborts when formed-line evidence disappears. `TacticalSopDoctrine` stamps every `CommandDoctrineOrder` with explicit authority (`Scout`, `Probe`, `Screen`, `Attack`, `Assault`, `Hold`, `Fallback`, `Reserve`, etc.), risk budget, target reacquisition cadence, support-before-charge requirements, and fallback-if-pressed requirements. That means a probe can move and report without authorizing a major assault, and a thin attack can keep pressure while denying charge until support is present. `TacticalBattleLinePlanner` spaces main effort, support, fix, screen/probe, reserve rally, assembly, and fallback targets around the verified Grand Tactician objective or visible-line anchor. `TacticalNavMeshPlanner` turns those ledger targets into bounded standoff, breakoff, and covered-lane approach points before #61 delegates to vanilla pathing. Reserves remain bounded by the `HeldReserve` doctrine gate, but if the ledger has a rally point and the command is not at its assigned reserve area, #61 moves the reserve to that rally point instead of treating all reserve idle as complete. Reserve rally placement is candidate-scored through `TacticalReserveAssemblyPlanner`: candidates must be behind the objective, far enough from the threat, reachable, not crowded, and within bounded lateral/depth limits; the scorer favors cover, low congestion, adequate threat distance, and a usable reinforcement distance. Close local flank emergencies now translate the existing flank-risk gate into vanilla `SetGroupFormation(... refuseflank: 0/1)` so the game engine's own skewed-position formation code can refuse the threatened flank. Assignment telemetry includes doctrine task, SOP authority, risk budget, reacquire seconds, support/fallback requirements, planner reason, primary/support/fallback target buckets, idle reason, and confidence so tuning can distinguish missing anchors, bad target spacing, held reserves, reserve-area movement, and failed fallback resolution.

The follow-up battle-line assembly fix closes the remaining clump path between the planner and #61. `CommandPostureExecutor` now sends `FormUp` and `AdvanceToAssembly` doctrine orders to `DoctrinePrimaryTarget`, so the executor uses the planner's Grand-Tactician-anchored lane instead of resolving the old generic `AssemblyArea` from the objective center. `TacticalBattleLinePlanner` also distributes forming commands across the frontage instead of alternating all commands between two assembly lanes; main attacks get modest spread when multiple commands are assigned, while support/fix/screen keep their role lane plus a bounded per-command offset. Regression coverage: `posture executor uses doctrine assembly target` and `tactical battle line planner spreads forming commands across frontage`.

The Scourge slot-grid follow-up prevents wide command sets from becoming one uninterrupted objective-front line. When there are at least six command nodes, `CommandDoctrineAssignment` converts the first and last forming slots into `GuardFlank`, keeps the near-wing slots as rear depth/echelon positions, and leaves the middle commands to form the battle line. `CommandPostureExecutor` now sends `GuardFlank` to `DoctrinePrimaryTarget`, so the flank guards move to planner-generated wing anchors instead of idling or joining the center line. Unknown command roles no longer get `doctrine=none`: they receive conservative `FormUp` doctrine targets, but still do not count as legally idle without a task. Telemetry reasons include `scourge-slot-flank-guard`, `scourge-slot-depth`, and `scourge-slot-line`.

The nested-division follow-up covers the opposite scale problem. `TacticalNestedDivisionPlayPlanner` runs inside `TacticalBattleOrchestrator` after the command operations runtime builds the side ledger. If the command tree exposes one active parent command with at least three direct brigade/battery children, and the side-wide command surface is only that parent plus those children, the planner expands the child commands into brigade-level operational states while retaining the parent command as a reserve/HQ anchor. Three children become left flank / main / right flank; four or more keep first and last children as flank guards, assign a non-reserve interior child as the main body, hold the near-rear interior child as depth reserve, and keep the parent commander with that main-body reserve instead of dropping it from the ledger. This mirrors Scourge's division-play idea without hijacking full multi-division battles, where the side-wide slot grid remains in charge.

The approach-avenue pass closes the next no-contact battlefield problem. `TacticalApproachAvenuePlanner` prefers enemy-owned or neutral vanilla entrypoint/reinforcement evidence over friendly entrypoints, preserves road-snap confidence from `EntryPoint.CalculateTransform`, and attaches the selected avenue to objective estimates. `TacticalBattleLinePlanner` uses that avenue axis for screens/probes, assembly lanes, frontage endpoints, artillery line, reserve rally, and fallback targets: screens delay forward along the guessed enemy route, while reserves and fallback targets sit behind the objective away from that route. Regression coverage: `tactical approach avenue planner prefers enemy entry route`, `tactical battle line planner uses predicted avenue axis`, and `tactical scouting avenue sends screen forward and reserve rear`.

The defensive corridor anchor fix handles maps like Stafford C.H. where the objective point is behind the tactically important road/creek approach. `TacticalDefensiveLineAnchorPlanner` shifts defensive battle-line geometry forward from the objective onto the predicted road/crossing intercept when the approach avenue is confident enough, narrows frontage for crossing corridors, and moves reserve/artillery depth behind that defended line rather than behind the raw victory-point coordinate. Strong verified terrain anchors such as ridges and towns are preserved unless crossing/chokepoint evidence is present. Planner reasons include `approach-intercept`, `road`, `crossing`, and `objective-lane` so Active smoke can distinguish corridor defense from generic objective frontage. Regression coverage: `tactical defensive line anchor shifts road corridor forward` and `tactical battle line planner defends corridor with reserve and artillery depth`.

The same pass now includes `TacticalIntelligencePack`, a pure gate layer that captures the missing commander-level decisions before any Harmony writer is allowed to act. It models battlefront frontage and unsupported gaps, the massing cycle from reconnaissance through support assembly and commitment, reserve missions, the fallback ladder, artillery missions, ammunition/fatigue/morale endurance, deduplicated support requests, and W&L player-subordinate order intent mapping. `CommandDoctrineAssignment` consumes the battlefront and massing gates today: a committed main effort with exposed contact but no support remains an `Attack` with `support-required` until support exists or odds are decisive, and a wide unsupported battlefront blocks a major attack until the line is closed.

The Scourge-of-War conversion pass maps its useful order concepts into those Whiskey gates instead of importing foreign runtime code. B7 artillery now feeds `TacticalArtilleryMissionPlanner` with support request, counterbattery visibility, ammo ratio, target range, friendly danger-close, close-enemy threat, displacement ability, field-of-fire quality, weak-point assignment target, and safe reposition target. B8 reserve protection plus #56 reserve-delay conversion now feed `OperationalReserveDoctrine` with reserve fraction, minimum-held fraction, partial-commit allowance, main-effort odds, flank threat, reserve endurance, assault authorization, exploit opportunity, and fallback pressure, so final-reserve protection, flank sealing, line relief, exploit reserve, and spent-reserve refusal are distinct mission outcomes. #41 charge initiation now feeds `TacticalEnduranceGate` with infantry/artillery ammo, fatigue, morale, and casualty pressure before doctrine can allow an assault, and the massing cycle also consumes endurance before assigning major assault commitment. `TacticalBattleLinePlanner` now returns frontage endpoints, echelon depth, artillery line, and terrain/objective-lane anchored task targets. `TacticalNavMeshPlanner` consumes runtime `NavMesh.CalculatePath` corner samples from #61 and scores road preference, slope cost, congestion, choke/bridge risk, dead-ground cover, threat exposure, route continuity, reservation pressure, fallback-lane conflict, artillery danger, and friendly front blockers before choosing approach/breakoff/fallback points. `DoctrineConsumerDecisions.DecideFallback` maps the fallback ladder into live order authority. #62 `PlayerSubordinateOrderPatch` is present behind `Enable Player Order Doctrine = false`: when enabled it runs after vanilla `AIBattle.UpdateDLCPlayerOrders`, yields to active vanilla current orders, dedupes by signature, maps doctrine intent to vanilla W&L current-order types through `AIBattle.CheckCurrentOrderUpdate`, and never emits direct movement writes or campaign-style calls.

The later Scourge tactical micro pass adds four direct battle-runtime conversions. `TacticalDivisionPlayExecutor` mirrors Scourge's division loop by anchoring play execution on the best engaged subordinate, assigning sibling support/reserve/screen/fallback tasks, and marking idle child orders as courier-delivered. #61 groups live command children by parent transform/parent-regiment evidence, applies those runtime play orders, and throttles outbound AI-to-AI courier delivery through `TacticalOutboundCourierCadence` before writing vanilla commands. `TacticalCavalryFollowDoctrine` maps cavalry-capable commands into guard, scout, screen, and raid follow modes; guard becomes flank-guard positioning, scout/screen request scout/screen movement, raid filters invalid targets, and screens get away from close enemy. `TacticalGrandTacticianReconDoctrine` is the Grand Tactician FOW adapter for that behavior: without FOW-visible contact or received-fire evidence, cavalry-capable commands scout/screen, while commands without cavalry downgrade blind assault into infantry probe/screen-by-bounds instead of chasing a guessed enemy line. `TacticalArtilleryMicroDoctrine` extends B7 with Scourge-style limber, unlimber, fallback, conserve-ammo, and wheel-to-target-facing decisions through vanilla `ChangeRegimentFormation`, `SetWaypoint`, `SetMovementMode`, `ChangeCombatBehavior`, and `RotateRegiment` under the existing artillery doctrine flag and W&L/player gates. In AI-vs-AI W&L smoke, #61 no longer treats `dlcw_isundercommander` as player ownership when `GameVars.ai_vs_ai` is true; non-AI-vs-AI player and player-subordinate protection is unchanged. Campaign advance-guard/picket/supply-base movement is not runtime-enabled; current backlog guidance lives in [`docs/strategic-recon-commitment.md`](strategic-recon-commitment.md).

The lower-level Gettysburg/Scourge pass adds translated doctrine surfaces while deliberately skipping anti-cavalry square analogs and scenario phase templates for now. `TacticalMeleeFearDoctrine` models Scourge-style close-combat pressure from strength, unit type, morale, leader risk, high ground, flank/rear pressure, recent fire, and routed targets; #41 now feeds it from live charge-target evidence before doctrine can permit a formed charge. `TacticalNavMeshPlanner` now accepts `FriendlyBlocker01`, and #61 estimates front-arc friendly blockers from `BattleUnits.completeunitlist` so approach candidates are penalized when they would drive into the existing line. The May 2026 route-intelligence pass adds `TacticalMovementCostField` scoring on top of the same vanilla path-corner samples: it rewards route continuity to reduce waypoint thrash, penalizes reserved/crowded bridge and choke lanes, and lets fallback choose a safe intermediate corridor instead of blindly reusing an attack lane. `TacticalFireControlDoctrine` maps verified Grand Tactician short/medium/long infantry behavior and cavalry evade/neutral/charge behavior into live #61 fire-control writes through vanilla `BattleUnits.ChangeCombatBehavior`. Formed infantry leaves long-range fire for medium once it is actually engaging, closes to short for decisive volleys inside roughly 100 yards, but probe/screen/scout/delay/fallback/forming/assembly/guard tasks may stay on long fire because they are not ready to close. #41 charge doctrine also consumes the same infantry fire discipline so a loaded/bad-angle volley can block premature charge initiation. `TacticalSkirmisherDoctrine` and `TacticalBattlefieldAttributeMatrix` remain pure/tested only.

## System Overview

The operations ledger sits inside the existing tactical orchestrator runtime:

- `TacticalBattleCoordinator` detects the battle lifecycle and ticks each active side.
- `TacticalBattleOrchestrator` owns side-level tactical state.
- `ArmyOrchestrator` owns command-tree snapshots and the operations-ledger runtime.
- `TacticalOperationsLedgerRuntime` records operation shape, phase, command assignments, last-order/progress timing, and posture summaries.
- `TacticalSopDoctrine` records the order authority and risk/support/fallback gates for each command order, separating probe/screen/hold/attack/assault semantics instead of inferring all behavior from one task enum.
- `TacticalIntelligencePack` records the broader commander gates: battlefront geometry, massing cycle, reserve mission, fallback ladder, artillery mission, endurance gate, support-request ledger, and W&L player-subordinate intent. Runtime consumers now use these gates for charge/endurance, reserve/fallback, artillery assignment, and the default-off W&L order bridge.
- `CommandPostureExecutor` is the pure decision model for whether a command needs a formation, waypoint, reserve release, fallback, or stuck-order recovery.
- `TacticalApproachAvenuePlanner` is the pure route-prediction layer. It scores Grand Tactician entrypoints, scheduled arrivals, and deployment-group starts against each objective, then publishes an enemy-origin -> objective axis with confidence and source flags.
- `TacticalDefensiveLineAnchorPlanner` is the pure defensive corridor layer. It turns confident road/crossing approach evidence into an intercept-line objective in front of the map objective, while preserving strong verified terrain anchors unless the corridor evidence is stronger.
- `TacticalBattleLinePlanner` is the pure command-spacing planner for the doctrine ledger. It produces frontage endpoints, objective lanes, terrain-aware flank anchors, echelon depth, artillery line, and main/support/fix/screen/reserve/fallback targets for #61. When an approach avenue exists, line geometry forms across the predicted enemy route instead of collapsing around the objective center; when a defensive corridor anchor exists, reserve and artillery depth are measured behind the defended intercept line rather than the raw objective coordinate.
- `TacticalReserveAssemblyPlanner` is the pure reserve-area scorer. It accepts generated or runtime-provided candidates and rejects unsafe/crowded/frontline reserve positions before selecting the rally point.
- `TacticalNavMeshPlanner` is the pure doctrine-target planner for movement writes. It turns ledger targets into recon standoff bounds, covered-lane assault offsets, and fallback breakoff points, then #61 enriches candidate choice with runtime vanilla path-corner, terrain, choke, bridge, congestion, dead-ground, threat-exposure, route-continuity, reservation-pressure, fallback-lane, artillery-danger, and friendly front-blocker samples before handing the destination to vanilla pathing.
- #61 `BattleCommandPostureExecutorPatch` is the only new command-posture write surface. It runs after vanilla `AIBattle.AdjustGroupFormations` and writes through vanilla `BattleUnits.ChangeStance`, `BattleUnits.SetWaypoint`, `BattleUnits.SetGroupFormation`, and fire-control `BattleUnits.ChangeCombatBehavior`.

Harmony patches do not write ledger state. Ledger state is written during the orchestrator tick. #61 reads ledger assignments and current vanilla physical state, then either does nothing or issues one bounded vanilla posture correction for eligible AI command groups.

Eligibility is ledger-first. #61 does not prefilter command nodes out solely because `unittyp <= 13`; if the orchestrator ledger assigned a vanilla `Regiment` component as a command node, the executor can consider waypoint, stance, reserve, fallback, or recovery actions for it. `unittyp > 13` is used only as the guard for `BattleUnits.SetGroupFormation`, because vanilla returns immediately for lower `unittyp` values on that API. AI-issued `SetWaypoint` / `SetGroupFormation` calls keep `showmovementoptions: false` so Whiskey does not open player movement UI while correcting AI posture.

Ledger resolution is tolerant but still ledger-bound. #61 first looks for an exact `CurrentCommandOperations` node id using the command node's GameObject instance id, then the `Regiment` component instance id as compatibility fallback. If that tick snapshot is missing the group, it resolves the command intent through `ArmyOrchestrator.ResolveCommandIntentForGroup(...)` and builds a single operational state from the current operation/objective ledger. If both paths fail, it writes nothing.

Objective anchors come from vanilla battle state before synthetic evidence. The operations ledger now reads `AIBattle.objectivechain[i].objectives` first, deduplicates any per-group `Regiment.currentsetobjective` anchors, then scans live scene `Objectives` components so real map objectives win before movement-anchor fallback. When visible formed enemy-line evidence exists, it is appended as a `VisibleEnemyLine` objective even if vanilla objective-chain/current-objective anchors already exist; this keeps real contact from being hidden behind inert `UnknownVanillaObjective` records. The old no-objective fallback used `enemy-line-{side}` at `(0,0)`, which made #61 reject the target as default/unsafe; the current fallback uses a weighted visible enemy-line centroid and the operation director gives exposed enemy-line objectives enough weight to beat generic anchor rows when odds and confidence support commitment.

Fallback target resolution is bounded and threat-aware. When a `FallBackToLine` task has a current or primary objective point, #61 steps away from that objective by the configured fallback standoff. If the objective anchor is unavailable but a closest visible enemy unit is available, #61 derives the fallback waypoint by stepping away from that enemy bearing instead of failing with `target-unresolved`. If neither objective nor visible threat can be resolved, the movement write still fails closed.

## Tactical Tick Optimization (Heavy Path Throttling)

#61 operations-ledger posture execution and doctrine consumers now ride the split tick cadence (Approach 1; see plan `docs/superpowers/plans/archive/2026-05-17-tactical-tick-optimization-implementation-plan.md` Task 12 + archived design).

**DriveOperationsLedger** (CoordinatorRuntime.cs:396 and 416) follows the same gate as DriveTick/DirectChild: cheap signature + `TacticalHeavyPathGate.Decide` (HeavyPathGate.cs:80); on Run, heavy `TacticalBattleSnapshotBuilder.Build` (SnapshotBuilder.cs:119 — evidence + full objectives with approach avenues + command tree + direct children) + publish; otherwise reuse `_lastPublishedSnapshots[side]` (or Empty) for director + `CommandDoctrineAssignment`.

**Frequent path for #61** (`BattleCommandPostureExecutorPatch` Postfix on `AdjustGroupFormations`, TacticalOperationsLedgerRuntime.Update, CommandPostureExecutor, local formation/fallback fixes): always uses last published snapshot (HasData guard, degrade to live vanilla only) + fresh per-Regiment vanilla reads (`pathinterrupted`, `groupsubordinatesmoving`, local FOW contacts, formation state, cooldowns, etc. — full allowed list in TacticalBattleRuntimeSnapshot.cs:44-56 Task 8 boundary comments). The gate and heavy Build are **never** called from #61 or urgent recovery paths. This preserves the responsive character of posture execution, RecoverInterruptedOrder, close-flank `GuardFlank`/`FallBackToLine`, and formation corrections even when side-wide heavy planning is throttled.

**Heavy path impact on ledger**: full `TacticalOperationDirector`, battle-line/nav/defensive planners, `CommandDoctrineOrder` generation, and assignment only when gate allows (first-tick, signature/pending change after cycle floor, or max-interval force). The snapshot is the single source of truth for high-level doctrine targets and command tree/direct-child roles consumed by #61.

**Config, gate reasons, telemetry, urgent boundary, and performance evidence** are identical to the orchestrator description (Plugin.cs "TacticalTickOptimization", CoordinatorRuntime Category.Gate repeated events, 7 reasons in `TacticalHeavyPathGate`, Task 8 safety in 5 files + harness test, Task 10/11 dedicated notes for p95 deltas on `tactical.posture-executor` + gate counts/reasons/samples showing frequent skipped + occasional executed, no-hitch + rollback parity). The current time-compression hardening adds `Heavy Ledger Review Min Realtime Seconds = 2` so battle-hour max refreshes cannot execute heavy snapshots faster than the real-time floor during compressed play.

Rollback (`Enable Tactical Heavy Path Throttling = false`) restores 100% heavy ledger passes on every tick with exact pre-optimization behavior.

## Decision Gate Translation

The tactical command system uses gates, not a single aggression value. The reference SDK patterns that matter are: scouts/screens move ahead until contact; screens break off or reform when too close; commanders run a play only after real contact; attacks require a target, odds, morale, and reserve support; artillery falls back or cancels fire when unsupported; and fallback/retreat comes from morale, flank, danger, and target-validity failures. Whiskey translates those ideas into Grand Tactician-owned anchors instead of copying foreign code or data.

Current gate ownership:

| Gate | Evidence input | Whiskey owner | Vanilla/write anchor | Behavior |
|---|---|---|---|---|
| Contact classification | `TacticalBattlefieldPicture` contact kind, visible/recent-fire freshness, objective confidence, `MainLineExposed` | `TacticalDecisionDoctrine.ClassifyObjective` | Read-only ledger input from `TacticalVisionRuntimeAdapter`; #61 consumes orders | No formed main line means `ReconnaissanceContact`, even at high confidence. It probes/screens instead of committing the main line. |
| Recon/probe/screen | Objective has enemy strength but no formed-line evidence | `TacticalOperationDirector`, `CommandDoctrineAssignment`, `CommandPostureExecutor` | #61 `BattleCommandPostureExecutorPatch` -> `BattleUnits.SetWaypoint` / `SetGroupFormation` | Operation phase becomes `Scouting`; main/probe roles get `Probe`, screen/fix/support roles get `Screen`, each with a primary contact target and breakoff fallback point. |
| SOP authority | Command role, task, operation phase, objective exposure/confidence, odds, reserve fraction | `TacticalSopDoctrine`; `CommandDoctrineOrder.Sop` | Read by #41/#45/B7/B8/#61 doctrine consumers; telemetry in `[TacticalCommandAssignment]` | Each command gets explicit `Scout`/`Probe`/`Screen`/`Attack`/`Assault`/`Hold`/`Fallback`/`Reserve` authority, risk budget, reacquire cadence, support-before-charge requirement, and fallback-if-pressed requirement. Thin attacks can move but deny charge with `support-required`; probes/screens cannot authorize a major assault. |
| Battlefront / massing | Command-node spread, main/support/reserve counts, formed-line exposure, confidence, odds, predicted approach avenue, defensive corridor anchor | `TacticalBattlefrontGeometry`; `TacticalApproachAvenuePlanner`; `TacticalDefensiveLineAnchorPlanner`; `TacticalMassingCycle`; consumed by `CommandDoctrineAssignment` | Read-only assignment gate before #41/#45/B7/B8/#61 consume orders | A committed main effort does not become a major assault just because an objective exists. The line must either have support assembled or decisive odds, and wide unsupported gaps force `support-required` until the front closes. When vanilla entrypoint or reinforcement evidence predicts the enemy route, frontage forms across that route; when the route is a road/crossing corridor, the line shifts forward to defend the intercept and reserves/artillery sit behind that defended line. |
| Screen breakoff | Recon task is close engaged and has a fallback target | `TacticalDecisionDoctrine.ShouldBreakOffRecon` via `CommandPostureExecutor` | #61 -> doctrine fallback target | A screen/probe that is too close does not sit and die; it reforms and steps back toward its breakoff line. |
| Route target planning | Doctrine task, own position, target point, fallback point, closest visible threat, stale waypoint evidence, runtime NavMesh corners, terrain, slope, congestion, choke/bridge, dead-ground cover, threat exposure, route continuity, reservation pressure, fallback-lane conflict, artillery danger, friendly front blockers | `TacticalNavMeshPlanner`; `TacticalMovementCostField`; #61 target resolver | #61 -> vanilla `BattleUnits.SetWaypoint`; vanilla `RegimentSetPath` / NavMesh path calculation remains the route owner | Recon/probe/screen stop short of contact, close screens break off, attacks/support/fix choose bounded approach points and lateral offsets, fallback prefers safer route-corridor samples over contested attack lanes, and unsafe path-quality/friendly-blocked/reserved candidates are penalized before vanilla pathing owns execution. |
| Attack / weak-point commit | Formed-line evidence, odds, and maneuver reserve | `TacticalDecisionDoctrine.ShouldCommitFixAndFlank`; `TacticalOperationDirector` | #61 posture writes; #45 stance; #41 charge gate | Exposed weak point with reserve becomes committed `FixAndFlank`; otherwise no formed assault. |
| Charge | Doctrine task, formed main-line exposure, local odds, routed-target screen exception, W&L/player protection, endurance, melee-fear pressure | `DoctrineConsumerDecisions.DecideCharge`; `TacticalOrchestratorChargeGate`; `TacticalMeleeFearDoctrine` | #41 `BattleChargeGatePatch` -> `AIBattle.MicroAICheckForCharges` | Main/support can charge only with exposure, odds, endurance, and close-combat pressure. Fix/reserve/fallback/refuse/screen are denied except screen chase of an already routed target. |
| Fire control | Vanilla effective fire range, target distance, ammo, morale, fatigue, cover, alignment, loaded volley, current doctrine task | `TacticalFireControlDoctrine`; `TacticalInfantryFireDoctrine` | #61 -> vanilla `BattleUnits.ChangeCombatBehavior`; #41 charge gate consumes fire-discipline decision | Engaging formed infantry prefers historical medium/close behavior over max-range fire; decisive volleys inside roughly 100 yards use short behavior; probe/screen/scout/delay/fallback/forming/assembly/guard tasks may use long behavior while not ready to close; loaded or bad-angle volleys can block premature charge initiation. |
| Reserve release | Reserve role, main-effort pressure, reserve fraction, vanilla reserve movement candidate | `DoctrineConsumerDecisions.DecideReserve`; `TacticalReserveCommitGate` | #59 `BattleReserveCommitGatePatch` -> `AIBattle.CheckUseOfReserves` | Reserve stays held unless the main effort is under pressure or fallback relief is needed; denied vanilla reserve paths are rolled back. |
| Reserve mission | Reserve fraction, minimum-held fraction, main-effort odds, flank threat, reserve endurance, assault authorization, fallback pressure, exploit opportunity | `OperationalReserveDoctrine` | Gate for #59/B8 reserve movement decisions | Reserves partial-commit only above the held-reserve floor, seal threatened flanks before exploiting, relieve a pressured line before counterattacking, and refuse spent or last-ditch reserves instead of burning the final reserve by default. |
| Reserve assembly | Objective, threat bearing, candidate cover, congestion, reachability, lateral/depth bounds | `TacticalReserveAssemblyPlanner`; `TacticalBattleLinePlanner` | #61 -> vanilla `BattleUnits.SetWaypoint` only when reserve has a rally target | Reserve rally candidates must be behind the objective, at least 325 units from threat, no more than 520 lateral units from the line, reachable, and below 0.80 congestion. Scoring favors cover, low congestion, threat distance, and reinforcement distance near the target band. |
| Fallback / withdrawal | Odds collapse, lost contact, no formed target, morale pressure, outflanked tiers, rear pressure, fatigue | `TacticalDecisionDoctrine`, `DirectChildAllocator`, `TacticalMoralePressure`, `TacticalWithdrawalDoctrine`, `TacticalFallbackLadder` | B8 `CheckUseOfReserves`/fallback integration; #61 fallback target | Committed attacks soft-abort on lost contact or true odds collapse; downgraded contact returns to scouting; severe local overmatch and morale/flank collapse produce ordered fallback, rear-guard, or full-retreat behavior instead of one generic retreat. |
| Artillery support / panic | Support screen, morale/fallback threshold, ammo, close enemy, friendly close range, visible enemy artillery, weak-point target, field of fire, safe reposition target | `TacticalSupportScreen`, `TacticalArtilleryDoctrine`, `DoctrineConsumerDecisions.DecideArtillery`, `TacticalArtilleryMissionPlanner` | B7 `B7CheckAIBombardmentPatch`; vanilla bombardment/counterbattery state | Supported guns preserve or support main effort; unsupported close-threat guns cancel bombardment or request defensive fallback; friendly-close denies bombardment; low ammo and out-of-range targets conserve fire; weak-point assignment, ammo mission, and reposition intent are explicit doctrine outputs. |
| Endurance / relief | Infantry ammo, artillery ammo, fatigue, morale, casualty pressure | `TacticalEnduranceGate`; `TacticalMassingCycle`; `TacticalSupportRequestLedger` | Command doctrine, #41 charge gate, reserve relief, and support requests | Low ammo, exhaustion, shaken morale, or high casualties block assault at both commander assignment and charge initiation while still allowing fallback and line-relief requests. Duplicate requests are coalesced and highest priority wins. |
| Destination discipline | Same-destination crowding, enemy on target destination, unit type, skirmisher-in-motion exemption | `TacticalDestinationDiscipline`; #61 duplicate/stale waypoint policy | Vanilla `AIBattle.CheckForSimilarPositions` evidence plus #61 `SetWaypoint` | Movement writers avoid stacking, stale duplicate waypoints, and enemy-occupied destinations instead of repeatedly issuing bad paths. |
| Formation/facing | Task family, visible formation, ordered formation, group formation, flank emergency, close engagement | `CommandFormationCorrection` | #61 -> `BattleUnits.SetGroupFormation` / `ChangeStance` | Attack/support/fix/hold/fallback tasks reform to line and face threat when needed; close-engaged corrections avoid creating new march-column paths. |
| Refused flank | Local flank-risk decision, threat facing, close defensive/fallback task family | `CommandFormationCorrection.RefuseDecisionForThreatFacing` | #61 -> `BattleUnits.SetGroupFormation(... refuseflank: 0/1)` | Close defensive tasks with a clear local flank emergency pass the vanilla left/right refusal parameter instead of inventing a new formation system. |
| Player-subordinate orders | W&L scenario state, player command relationship, existing order freshness, command doctrine task | `PlayerSubordinateOrderDoctrine`; #62 `PlayerSubordinateOrderPatch` | Default-off `AIBattle.UpdateDLCPlayerOrders` Postfix -> vanilla `AIBattle.CheckCurrentOrderUpdate`; no direct movement writes | Maps higher-command doctrine to W&L current-order intent only when explicitly enabled, yields to active vanilla orders, dedupes repeated signatures, avoids campaign-order calls, and never bypasses the player's command chain with direct movement. |

The important doctrine rule is that screen contact is not a target for a formed assault. A high-confidence screen is still a screen until `TacticalBattlefieldPicture` verifies formed-line evidence. Once a committed attack loses that evidence, `TacticalOperationDirector` now breaks commitment before the commit window can keep the AI charging at a vanished or downgraded target:

- `contact-lost` -> `SoftAbort`
- `contact-downgraded` -> `Scouting`
- `recon-contact` -> initial `Scouting`
- `fix-and-flank` -> committed attack only after formed-line exposure, odds, and reserve gates pass

## Config Contract

Existing BepInEx config files override C# defaults. The release/default contract is:

```ini
[Tactical Orchestrator]
Tactical Commander Mode = Active
Enable Tactical Battle Orchestrator = true
Enable Tactical Orchestrator Army = true
Enable Tactical Orchestrator Intent Inference = true

[Tactical]
Enable Tactical Decision Matrix Logging = true
```

Modes:

| Mode | Behavior |
|---|---|
| `Off` | Disables the operations-ledger command system. Use this for rollback. |
| `MonitorOnly` | Runs vision, operation selection, command assignments, idle/stuck monitoring, and telemetry, but suppresses posture writes. Use this for diagnostics and pre-active smoke. |
| `Active` | Runs the full tactical command system for AI sides. This is the release/default mode. |

`Tactical Commander Mode` overrides scattered legacy tactical behavior flags for operations-ledger surfaces. Legacy default-off tactical flags still control their own older patch surfaces, but they are not the release switch for the operations ledger.

Rollback is config-only: set `Tactical Commander Mode = Off` and restart the game. If the failure is limited to write behavior, `MonitorOnly` keeps ledger evidence alive while suppressing writes.

Current machine-state note: the local BepInEx config has `Tactical Commander Mode = Active`, `Enable Tactical Decision Matrix Logging = true`, `Enable Tactical Battle Orchestrator = true`, `Enable Tactical Orchestrator Army = true`, `Enable Tactical Orchestrator Intent Inference = true`, `Enable Tactical Orchestrator Reserve Commit Gate = true`, `Enable Tactical Orchestrator Direct-Child Gate = true`, and `Enable Tactical Orchestrator Charge Gate = true`. The mode row was added explicitly after deploy so the next smoke run does not depend on first-load default persistence; the charge-gate row was corrected after log review found it was the only tactical orchestrator gate still disabled.

## Vanilla Anchors

Known anchors for this system, confirmed against `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` and current patch/docs references:

| Anchor | Use |
|---|---|
| `AIBattle.CheckGlobalAIStrategy` 6314 | Battle-level macro strategy cadence and tactical lifecycle evidence used by existing orchestrator flow. |
| `AIBattle.AdjustGroupFormations` 5875 | #61 Postfix anchor; vanilla chooses group formation after stance updates. |
| `AIBattle.AssignReserves` 7017 | Reserve-assignment surface observed by earlier reserve doctrine and drift telemetry. |
| `AIBattle.CheckLineFallbacks` 5118 | Vanilla line-fallback surface; operations-ledger fallback work must not bypass its ownership without a specific patch. |
| `BattleUnits.ChangeStance` 90772 | Vanilla stance API used by #61 for bounded stance corrections. |
| `BattleUnits.ChangeCombatBehavior` 90944; `Regiment.GetFireRange(bool calcusedfirerange=false)` 118292 | Vanilla infantry/cavalry fire-control API and effective range calculation used by #61 and #41 fire discipline. UI buttons map infantry short/medium/long to combat behavior 0/1/2 and cavalry evade/neutral/charge to 4/5/6. |
| `BattleUnits.SetWaypoint` 91232 | Vanilla movement-order API used by #61 with order delay and native movement guards. |
| `BattleUnits.SetGroupFormation` 91822 | Vanilla formation API used by #61 for command posture changes, including the existing `refuseflank` parameter when a close defensive flank emergency has a clear left/right threat. |
| `NavMesh.CalculatePath` via UnityEngine.AI | Reachability check used by `TacticalReserveAssemblyPlanner`/`TacticalNavMeshPlanner` before #61 hands movement targets to vanilla; vanilla still owns the actual movement order and path execution. |
| `BattleUI.CheckPathSetting` 168980 -> `BattleUI.CheckGroupRotation` 166042 -> `BattleUnits.SetWaypoint` 91232 | Regular non-W&L campaign/battle right-click movement path. Campaign formations are represented by the `Regiment` component; UI echelon labels come from `overridesymbol`, while `unittyp > 13` is the confirmed `SetGroupFormation` command/group-formation guard. |
| `BattleUnits.GetHierarchyTree` 92720 | Vanilla hierarchy reader used by `SetGroupFormation` to walk attached command nodes. |
| `AIBattle.CheckCurrentOrderUpdate` 8233 | W&L current-order/message bridge only. It hard-gates on `DLC_WL.dlc_scenarioactive`; do not treat it as the regular campaign movement API. |

The executor must not add broad replacements for these anchors. It only uses the vanilla APIs as bounded outputs after ledger and safety gates pass.

## Telemetry

Expected operations-ledger markers:

- `[TacticalOpsLedger]` from side-level operation signature changes.
- `[TacticalCommandAssignment]` from command-node task assignment changes.
- `[TacticalCommandPosture]` from #61 posture decisions and writes.
- `[TacticalPostureSummary]` from valid-idle, illegal-idle, stuck-recovery, attack, and reserve-wait summaries.
- `[TacticalReserveDrift]` from reserve-list drift inspection around `AssignReserves`.
- `[TacticalDoctrineCharge]` from #41 doctrine charge allow/deny decisions.
- `[TacticalGroupDecision]` from #45 doctrine stance decisions.
- `[B8]` / `[TacticalReserveOrderDelayGuard]` reserve/fallback decisions where the doctrine ledger accepts or rejects vanilla reserve movement.
- `[once:tactical-command-posture-executor]` first-fire marker when #61 wires.

`[TacticalCommandAssignment]` rows now include doctrine task, SOP authority, risk budget, reacquire seconds, support/fallback requirements, planner reason, primary/support/fallback target buckets, idle reason, and confidence. For reserve tuning, look for `battle-line-reserve-*`, `reserve-area`, and held-reserve legal-idle rows; those distinguish a deliberately held reserve from a reserve that should move to its assembly point.

Rows should be signature-gated or interval-bounded. Repeated `missing-anchor`, Harmony failure, `Exception`, or `ERROR` lines are smoke failures until proven unrelated.

## MonitorOnly Smoke Checkpoint

Use this checkpoint before or after an Active run when you need proof that the ledger is reading the battle correctly without writing vanilla state:

```ini
[Tactical Orchestrator]
Tactical Commander Mode = MonitorOnly
Enable Tactical Battle Orchestrator = true
Enable Tactical Orchestrator Army = true
Enable Tactical Orchestrator Intent Inference = true

[Tactical]
Enable Tactical Decision Matrix Logging = true
```

Pass criteria:

- `[TacticalCommanderMode] mode=MonitorOnly` appears after plugin load.
- `[TacticalOpsLedger]` appears for at least one AI side.
- `[TacticalCommandAssignment]` appears when command-node assignments materialize.
- `[TacticalPostureSummary]` appears and reports interpretable command counts.
- `[TacticalCommandPosture]` lines may appear for diagnostics, but none should report `applied=True`.
- No repeated `Exception`, `ERROR`, `missing-anchor`, or Harmony failure lines.

## Active Smoke Checklist

Use this checklist after deploying the current DLL and restarting the game:

1. Confirm config contains:

```ini
[Tactical Orchestrator]
Tactical Commander Mode = Active

[Tactical]
Enable Tactical Decision Matrix Logging = true
```

2. Start a fresh battle or load a save that reaches active AI tactical command ticks.
3. Search `BepInEx/LogOutput.log` for:

```bash
rg -n "TacticalOpsLedger|TacticalCommandAssignment|TacticalCommandPosture|TacticalPostureSummary|TacticalReserveDrift|tactical-command-posture-executor|Exception|ERROR|missing-anchor|Harmony|failed" "<GTCW>/BepInEx/LogOutput.log"
```

Pass criteria:

- `[TacticalOpsLedger]` appears.
- `[TacticalCommandAssignment]` appears.
- `[TacticalCommandPosture]` writes are bounded and explain action/reason/target.
- `[TacticalPostureSummary]` shows illegal idle trending down or staying explainably bounded during the run.
- `[TacticalReserveDrift]` has no repeated drift-failure warning.
- No player-side or player-subordinate retasking is observed.
- No repeated non-reserve command nodes remain in `MarchColumn + pathInterrupted=True + activeMove=False` with empty or stale paths without a valid ledger reason.
- Visible enemy-line contact near Hampton-style fights raises sector/objective confidence; high-odds `AttackWeakPoint` / main-effort orders should not resolve to defensive hold just because the strategic macro is defensive.
- Formed regiments commit only when the enemy line is exposed and outnumbered; skirmisher/screen-only contact must not pull the main line into a false assault.
- Doctrine reserve deny removes direct reserve paths and restores movement/order/cover/formation/target state rather than leaving a stale active path behind.
- No repeated `Exception`, `ERROR`, `missing-anchor`, Harmony failure, or #61 failure marker.

If the active smoke fails, set `Tactical Commander Mode = Off` for rollback. If evidence is needed before a fix, set `MonitorOnly` to keep ledger telemetry while suppressing writes.

Current Active smoke boundary: not passed for the current `562a61b5...` DLL. Only a fresh `LogOutput.log` and tuning manifest after restarting GTCW can prove this build. Fresh operations-ledger and doctrine-consumer markers are still required, including sustained objective movement after posture orders, no stale `activeMove=true paths=0` suppression, `approach-intercept` / `objective-lane` on corridor defense, `scourge-slot-flank-guard` / `scourge-slot-depth` on wide or nested line assembly, the parent command/HQ retained as a reserve anchor in nested single-division battles, and no broad `doctrine=none` fallback for unknown command roles.

## Risks

- #61 writes vanilla battle state when `Active`, so it must remain bounded by player/W&L/rout/order-pending/recent-order/close-engagement gates.
- The command system depends on command-tree discovery. Missing or unstable command nodes should fail open rather than invent writes.
- `SetWaypoint` calls must keep vanilla order-delay semantics; broad movement replacement remains out of scope.
- Fallback and reserve ownership still intersect older vanilla anchors and existing patches. Drift markers need review during smoke, not after release tagging.
- Existing config files can preserve old values. Always inspect the live BepInEx config before interpreting smoke.

## Documentation Lifecycle

This file is the living source for operations-ledger behavior, config, smoke expectations, and rollback. The implementation plan under `docs/superpowers/` is a point-in-time execution artifact. After Active smoke passes, update:

- `docs/handoff.md`
- `docs/tactical-orchestrator.md`
- `docs/patch-catalog.md`
- `MEMORY.md`
- this file
