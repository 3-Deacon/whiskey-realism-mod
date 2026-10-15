# Live Theater Campaign Planner — Design

**Status:** approved direction (user, 2026-10-14). Decisions:
- History is a **weight**; live scoring decides.
- Commander **personality is a modifier** on live scoring, taken from the real vanilla commanders (no hardcoded Lincoln/Davis).
- **Succession = both:** the ~12 historical succession events stay as a prior that fires when its historical conditions hold; every other appointment is by competence through vanilla APIs.
- **W&L:** Whiskey may move the player's parent formations the way vanilla does (never the player's own command) and writes deduped W&L dispatches (see `2026-10-14-wl-chain-of-command-design.md`).
- **Fog:** the planner and mod ledgers are fog-honest; vanilla's internal force sums stay as they are, but the targets the planner hands vanilla come from the honest picture.

This **supersedes Slice A locked choices #3-#6** (personality stack → modifier; scripted succession → prior + competence; one phased plan per side → per-theater plan; adjust-by-default/replan-on-invalidation → daily re-scoring with hysteresis). #1 and #2 unchanged. AGENTS.md, `docs/wl-dispatch-objective-bridge.md` and `docs/wl-player-order-doctrine.md` are updated in the same slice.

## Vanilla model (decompile, build 14703111)

- `AICampaign.Update` (11159) runs `UpdateUnitAI` `max(1, floor(√gamespeed))` times per frame; each pass runs one `CheckOffensiveMovements` (round-robin over `ownunits`), the operation-speed sync, and one step of a ~37-job per-faction machine (jobs: 0 `SeparateUnits`+heatmaps, 1 area points, 4 `CalculateMostValueableAIZones`+supply capacity, 6 offensive micro-moves, 8-9 recruiting, 15-16 defensive ops, 18 capital defense, 19 depots, 21 forts, 23 `LinkToFightingUnits`, 24 commander replacement, 25 theaters, 26 combination, 27 transfers, 32 army groups, 33 `PickCampaignObjective`, 34 final positions). `aifaction` excludes the player's alliance unless `ai_vs_ai` (W&L forces it), includes alliances 2-4, and `AIArea` arrays are indexed by faction index.
- **Real offensive lever:** per source area `AIArea.mostvalueableaiareaclose[f]` (job 4, 10964-11008); `CheckOffensiveMovements` (14166) gathers units to `aidominanceforoffensiveoperations` × initiative, rolls an aggression gate, dispatches via `MoveUnitTo` + `unitsinoffensiveoperations`; at most one offensive per faction per job cycle.
- `followedcampaignobjective` (`PickCampaignObjective` 17769) is a weak lever: importance bonus on objective towns/IIPs only.
- Defensive ops (13505) stop listed units with no enemy within bugle range (job 16, 13782) — never park planner-moved units in that list. Capital defense (11668) maintains `groupstodefendcapital`. Transfers (17232) reparent one brigade per call from the `ZoneRecruiting` heatmap deficit — not rail moves. `MoveUnitTo` (14479) uses rails when `userails` and transport capacity allow; it has no W&L guard. `theaterposition` (job 25) confines operations. Depots: `CBuilding.AddConstructionWish(id_supplydepot, pos, iip, alliance)` (97479). `ArmyGroup` has no objective field (command bonuses only).
- **No campaign fog for the AI:** `SeparateUnits` fills `enemyunits` from every campaign-level enemy group at true position/strength; vanilla never reads fog/intel/spotting.
- **Supply evaluation is zonal only** (`supplycapacity`, `SupplyInZoneAvailable`); no route/reach model.

## Architecture

```
daily review (per alliance, alliances 0/1; skipped when the W&L player is CIC; paused on W&L hierarchy −1 like vanilla 11368)
  CampaignSnapshotRuntime ──► CampaignSnapshot (immutable)
  EnemyArmyModel.Update(snapshot detections) ──► believed enemy armies (persisted)
  CommanderProfiles (vanilla commanders → personality modifiers)
  TheaterPlanner.Plan(snapshot, beliefs, priors, profiles, state, rng) ──► TheaterPlan
  LogisticsGate.Evaluate(plan, snapshot) ──► reachable targets + depot tasks
  ForceAllocator.Allocate(plan, snapshot, reliability) ──► Allocation
  CommanderAppointmentPlanner (competence + historical succession prior) ──► appointment intents
  Executors (idempotent, rate-limited, W&L-guarded) ──► vanilla hooks
```

### Components

1. **`CampaignSnapshotRuntime`** (runtime) → `CampaignSnapshot` (pure data): formations (position, destination, strength, morale, supply state, readiness, battle/retreat flags, `theaterposition`, player ownership `dlcw_isundercommander`, `IsMovedByPlayer`, operation-list membership, commander id), areas (importance, points, supply capacity, own/enemy close strengths, average position), objectives (live positions from `CampaignObjective.objectives` only — the hand-coded `ObjectiveCatalog` coordinates are removed), capitals, depots/IIPs/rail, `CampaignMapLedger` assets, battle history, the player's echelon (`GetPlayerCommandHierarchy`). One build per review; replaces the scattered per-runtime reflection.
2. **`EnemyArmyModel`** (pure, persisted): an enemy group is *detected* when it is not hidden in the AI side's own campaign fog grid — `ownTopUnit.fow.UnitIsHidden(enemy.GetPosition(), …)` with vanilla's 127225 arguments (`fows[1]` is painted every frame on the campaign map with real terrain raycasts, viewers = top units/garrisons, radius `maximumlineofsight` × rain/snow; `docs/vanilla-vision-and-movement.md` §Campaign vision, R3) — or when in battle with it. W&L own-side AI uses `!enemy.iswithinfog`. Fallback to vanilla's omniscient `enemyunits` when `!usefow` (tagged in telemetry). Stores last-seen position, strength, day; uncertainty radius grows with age; entries expire. Enemy-held objective towns imply garrisons. It never reads enemy positions except through this filter.
3. **`CommanderProfiles`** (pure + runtime reader): personality modifiers derived from the vanilla commander record of the CIC and each army/theater commander (map the actual attribute fields — aggression/initiative/cunning/leadership/experience — after reading `GameVars.Commander`); outputs per theater: required-odds shift, hold-vs-attack bias, risk tolerance. Era and faction remain priors.
4. **`TheaterPlanner`** (pure): sub-theater sectors built from AI areas (not the current 6 whole-theater buckets). Per objective/sector live score = value (importance, objective role, capital/rail/port role) + believed force balance near it + supply reach (from `LogisticsGate`) − march distance for the candidate force − exposure + threat to own capital (believed enemy strength over distance, streak counter) + `λ·prior` (historical catalog as time-decayed weight: 1 inside its window, fading over months; `Priority` sets magnitude; λ tuned so the prior breaks near-ties but cannot overturn a live margin above a fixed bound — harness-tested). Personality modifiers shift thresholds. **Hysteresis:** switch main effort only if `best − current > θ` for M days or a trigger fires (decisive battle, capital threat, objective taken/lost). **Bounded randomness:** softmax within ε of the best with a deterministic seed (campaign id ⊕ alliance ⊕ decision serial) persisted in the sidecar. Output `TheaterPlan`: main-effort theater + objective + required ratio; per-theater posture (MainEffort / Hold / EconomyOfForce / Screen / Recover); capital-defense floor; depot tasks; commander-need hints.
5. **`LogisticsGate`** (pure): refuses advance targets beyond supply reach (distance from depot/IIP/rail frontier, target zone `supplycapacity`), emits depot tasks along the axis instead ("don't outrun supply").
6. **`ForceAllocator`** (pure): maps each AI formation to (theater, role ∈ {MainEffort, Support, Hold, Reserve, CapitalGuard, DepotBuilder, Recover}, target). Hold theaters get `max(holdRatio × believed enemy, minimum garrison)`; the remainder goes to the main effort. Corps-level redeployment uses `MoveUnitTo` with rails when transport use is under cap; replacement flow uses vanilla's deficit/surplus brigade transfer. **Player handling:** the player's own command is never allocated a move. Parent formations of a brigade/division player are movable AI assets (dispatch emitted per chain-of-command spec). A corps/army/army-group player's formation is an *unreliable executor* (reliability from `PlayerComplianceLedger`): never the sole holder of a must-hold role (capital guard, sole main effort); discounted in allocation; reassigned around after N days of non-compliance. CIC player → planner stands down for that side.
7. **`CommanderAppointmentPlanner`** (pure + vanilla executor): scores available commanders (vanilla `CanBePickedForAIReplacement` pool) on attributes, rank and experience against theater importance; historical succession events (existing scheduler conditions) add a prior bonus so the historical swap happens when its conditions hold. Executes via `ArmyGroup.AppointCommander` / vanilla replacement APIs (as fixed in WS C2a). Never touches the player or the player's command.
8. **Executors** (runtime; one concern each, `dlcw`/`IsMovedByPlayer`-guarded, idempotent, ≤ K writes/day):
   - `PickCampaignObjectivePatch`: sets the main-effort objective; never blocks vanilla when there is no plan.
   - `ImportanceValuesPatch` (rewritten): per source-area pointer to that area's theater target, only for MainEffort/Support theaters and within vanilla's own distance/supply gates.
   - `theaterposition` writer (replaces #15's hardcoded anchors).
   - Availability filters: Postfixes on `IsUnitAvailableForOffensiveOperations` / defensive availability withhold Reserve, CapitalGuard, DepotBuilder, in-transit formations.
   - Main-effort mover: folds `OperationalProbeRuntime` + `CoordinatedOperationRuntime` commit/rollback; uses `MoveUnitTo` + the offensive list (never the defensive list). `MoveUnitTo(…, movearoundenemyunits:true)` returning true means *queued* (14519-14523); the mover confirms `regimentpaths > 0` next tick and retries/reassigns on failure. Targets pass vanilla's `CheckIfFinalWaypointIsPassable` semantics (25608) before dispatch.
   - Construction steering: supply depots (`CBuilding.AddConstructionWish`), forts and railroads along planned main-effort axes and threatened sectors (replaces the never-implemented `Enable Supply Depot/Fort/Railroad Steering` flags; default on).
   - Capital floor: Prefix pre-pledging `groupstodefendcapital` (folds `DefensiveOpsPatch`).
   - Replacement flow: `ZoneRecruiting` Postfix setting deficit/surplus (folds `TransferOfUnitsPatch`).
   - Depot executor: `CBuilding.AddConstructionWish`.
   - W&L dispatch emitter: from the chain-of-command spec.

### Replaced / removed (no dead code)

Removed: `ObjectiveScoring`, `ObjectiveCatalog` hand-coded coordinates, `CIC.Replan`/`SelectHistoricalPlan`/`BuildLegacyGenericPlan`/`WeightedPick`, the `OperationalPlan`/`Phase` model and its DTOs (replaced by `TheaterPlan` DTOs with a sidecar migration that drops old plans), `PhaseTruthLedger`/`OperationDynamicRuleEvaluator`/`CicReviewRouter` as phase routing (their triggers become planner reactions), the hardcoded CIC personality, `ArmyAreaRuntime` anchors + #15. Folded: `HistoricalOperationCatalog` → prior table; `HistoricalArmyAreaRegistry`/`DynamicArmyAreaDoctrine` → home-theater prior; probe/coordinated ledgers+runtimes → allocator + mover; construction-intent depot pick → `LogisticsGate`; `DefensiveOpsPatch` → capital floor; `TransferOfUnitsPatch` → replacement flow; `CampaignPaceLedger`/`StrategicResilienceDirector` → tempo input (deleted if redundant). Kept as inputs: `FrontSectorLedger` (re-keyed to sub-theaters), `FormationDirectiveLedger`, `DefenseIntentLedger`, `CampaignMapLedger`, `ContactEvidenceLedger` (feeds `EnemyArmyModel`), `BattleHistory`, fiscal/projects/policy/telegraph, vanilla bug guards. Migrated: `RecruitmentPatch`, `PerkSelectionPatch` read `TheaterPlan`. `SuccessionScheduler` kept only as the prior feeding `CommanderAppointmentPlanner`; `CommanderReplacementPatch`'s scripted swap path folds into the appointment executor. `EraStageManager` kept only if the prior uses it.

Every ledger that currently sums vanilla `enemyunits` directly (`FrontSectorRuntime`, `TransferOfUnitsPatch`, `DefenseIntentRuntime`, `DefensiveOpsPatch`, `FortConstructionGovernorPatch`) switches to `EnemyArmyModel` beliefs.

### Cadence & performance

One snapshot per daily review; scoring O(objectives × sectors + formations × sectors); alternate alliances across frames if a review exceeds ~4 ms; executor writes ≤ K/day, idempotent. Planner execution must not rely on vanilla job throughput beyond what the (fixed) governor guarantees.

## Testing

Pure units harness-tested with fixtures: both alliances; live margin beats prior / prior breaks tie; hysteresis prevents flapping; seeded RNG reproducible; enemy near capital raises the floor and pulls the main effort; out-of-supply target yields a depot task not an advance; economy-of-force theaters keep hold ratio; personality shifts thresholds (aggressive attacks at worse odds than cautious); historical succession prior fires when conditions hold, competence otherwise; W&L brigade career (parent movable, dispatch emitted, player command untouched); W&L corps career (player discounted, never sole must-hold, reassigned after N days); CIC player → stand down; hierarchy −1 → pause; fog (unseen enemy excluded, last-seen decays); faction index bounds (alliances 2-4); empty snapshot; sidecar migration from v2 plans.

## Docs updated in-slice

AGENTS.md (locked choices #3-#6 rewritten; architecture paragraph), `docs/historical-operation-doctrine.md` (prior semantics), `docs/wl-dispatch-objective-bridge.md` + `docs/wl-player-order-doctrine.md` (new invariants), `docs/strategic-recon-commitment.md`, `docs/patch-catalog.md`.
