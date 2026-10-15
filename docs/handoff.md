# Whiskey Realism — Master Handoff

**Read first at session start.** Where the project stands, what just landed, and what comes next. Chronology lives in `git log`; this page keeps only current state and durable invariants.

---

## Current state (2026-10-15)

| | |
|---|---|
| **Branch** | `main` — roadmap stage D (battle core, BC0-BC7) merged 2026-10-15 at `fc0b8af`; BC8 (experience- and drill-aware AI) merged at `75a7000`; stage E need-driven policy merged at `99bf4eb`. In progress: stage F W&L campaign chain of command (in review), stage G campaign planner (plan in review), bug-fix slices `fix/campaign-perk-ids`, `fix/campaign-logistics-bugs`, `fix/wl-economy-bugs`, BC9 battle recon. |
| **`main`** | `75a7000` — battle core + BC8 on top of the 2026-10-15 checkpoint (`6695ecd`); last released line is v0.2.2 (2026-05-04). |
| **Build** | `./build.sh` — 0 warnings, 0 errors on `main`; the test build is 0 warnings too. |
| **Harness** | 2003 PASS / 0 FAIL on `main` (auto-discovered `*Tests.Run()`; exit 0). |
| **Tools** | `python3 -I tools/config-audit.py .` (0 findings over 101 entries), `python3 -I tools/orphan-sweep.py .` (0; `--include-test-only` lists only members that predate the battle core), `python3 -I tools/test_smoke_check.py` (passes). See AGENTS.md § Testing. |
| **Game install** | Steam build 14703111 (2026-10-14 update) wiped BepInEx. **BepInEx 5.4.23.5 x64 was reinstalled 2026-10-15.** Deployed 2026-10-15 from `main` `99bf4eb` (battle core + BC8 + stage E): `WhiskeyRealism.dll` 1,672,704 bytes, SHA-256 `055eb4409f1abc7d39e74f68126ee72b99a2a0a04f794bb17603127e1c555ddd` (dist and plugins match); harness 2003 PASS / 0 FAIL. Earlier DLLs (`0831547f…`, `4f6e01fa…`, `3257f907…`) were never smoke-tested and are superseded. No `config/` or `LogOutput.log` until first launch, which writes a fresh cfg with the C# defaults (all on). |
| **Decompile** | `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`, 266,136 lines for build 14703111. Regenerate per `docs/findings.md` if `/tmp` was wiped. |

### What the battle core adds (stage D, on `main`)

Spec [`superpowers/specs/2026-10-15-battle-core-design.md`](superpowers/specs/2026-10-15-battle-core-design.md), plan [`superpowers/plans/2026-10-15-battle-core.md`](superpowers/plans/2026-10-15-battle-core.md) (decisions D1-D11, per-slice As built, PROVE-MODE, the BC7 smoke checklist). Architecture: [`tactical-orchestrator.md`](tactical-orchestrator.md) § Battle Core.

- **BC1 snapshot.** One pooled `BattleSnapshot` per AI side per frame, built at `CheckGlobalAIStrategy` (5866): own/enemy `UnitRecord`s, the D5 command parent map (smallest containing `allattachedunits`), derived arrays; `DirectChildDiscoveryRuntime` and `CommandTreeRuntime` read it.
- **BC2 perception.** Vanilla's AI-side fog grid (`UnitIsHidden`, 127225) or `iswithinfog` for a player-alliance AI side, range-only fallback; remembered enemies decay; every Whiskey battle reader (evidence, contact ledger, objective records, plays, posture, artillery, charge, #42/#44) reads observations.
- **BC3 ownership.** `PlayerOwnership` (player-owned, chain flags, group-level feud), `TacticalChainOrderPlanner` (fan-out around exposed player-owned descendants), `TacticalPlayerDispatchRuntime.Emit` (one W&L dispatch emitter shared with #62).
- **BC4 arbiter/executor.** #68 `BattleCoreArbiterPatch` (Postfix on `AdjustGroupFormations` 5870): `OrderArbiter` (one winner per group and channel) and `OrderExecutor` (guard, chain, `WaypointValidator`, silent-exit pre-check, dedupe, cooldowns; outcomes Moved/Failed/Retried/GaveUp/Superseded).
- **BC5 migration.** #61 → Posture, FacingPulse, FireControl, DivisionPlay producers; #45/B7/veto → GroupStance, ChargeDemotion, ArtilleryDoctrine, ArtilleryPointBlank; B8/#52 → Withdrawal and #59's merged deny; reserve release producer; #42 and #56 run the executor inline.
- **BC6 charge doctrine.** AI infantry in assault arms combat behavior 3 for the two leading regiments under vanilla's charge rules, restores vanilla's dead stance-drop cancel (`BUG-TAC-016/017`).
- **BC7 integration.** Explicit producer registry (`BattleProducerRegistry`; the producers' static constructors had never run), dead flags/code removed, Plugin descriptions rewritten, grep gates, one smoke checklist, docs.

Patches deleted: #45, #48, #49, #50, #52, #61, #65 (see [`patch-catalog.md`](patch-catalog.md) § Removed patches). Flags deleted: `Enable Single-Pass Objective Records`, `Battle Group Stance Min Interval Seconds`, `Enable Artillery Waypoint Veto`. Flags added (BC0): `Enable Battle Fog-Honest Perception`, `Enable Infantry Charge Doctrine`.

### What the remediation shipped

Inputs: [`reviews/2026-10-14-full-mod-review.md`](reviews/2026-10-14-full-mod-review.md), [`reviews/2026-10-14-reanchor-on-branch.md`](reviews/2026-10-14-reanchor-on-branch.md), [`reviews/2026-10-14-tactical-logic-review.md`](reviews/2026-10-14-tactical-logic-review.md), [`reviews/2026-10-14-branch-delta-review.md`](reviews/2026-10-14-branch-delta-review.md). Plan: [`superpowers/plans/2026-10-14-full-review-remediation.md`](superpowers/plans/2026-10-14-full-review-remediation.md). Every workstream was reviewed against the vanilla decompile before merge; commit messages carry the anchors.

- **Task 0 (base).** `Tactical/TacticalWriteGuard` — one gate for every Whiskey battle write: Commander Mode must be `Active`, the unit must be AI-controlled, W&L player command/chain/DLC-taken-over units are protected, fail closed. Commander mode is parsed once and cached.
- **C1 — campaign lifecycle.** PhaseTruth Recover deadlock broken; only decisive in-theater battles re-plan; per-alliance director clamp, no CIC for player-run alliances; historical catalog validated against `campaignobjectives.dat`, generic fallback uses vanilla's candidate set, vanilla picks when there is no plan, defensive operations never steer `followedcampaignobjective` (CSA defense ops retired); `CIC.ReviewPlanWithTruth` delegates to `CicReviewRouter`; all per-campaign state resets between campaigns; atomic, versioned, transactional sidecar with `.bak`/`.tmp` fallback, persisting probe, fiscal, front-change, capital-streak and written-objective state; skipped weekly/alternating gates catch up, monthly review on the real day; capital-threat/front-static signals from live state; operational probe stops on pause/withdraw with local odds and hysteresis.
- **C2a — campaign military.** Coordinated ops commit only through vanilla's offensive gates with a vanilla-exact tempo gate; transfer steering reads vanilla's recruitment heatmaps and restores in a Finalizer; #25 defense-intent filter restored in a Finalizer; army-group appointment only on groups holding plan units; succession swaps resolve the role and refill vacated commands; coastal defense and capital-defense commit the way vanilla does; army-area reuses vanilla's busy set; importance redirect only through vanilla's gates.
- **C2b — campaign economy.** Construction bias applies to selection only and restores raw values; subsidy-group normalisation and weighted-roll cap match vanilla; fiscal bands are directional with a King Cotton tariff ceiling and a posture-entry tax floor; policy research never stalls; fort governor mirrors vanilla's early returns; campaign-AI governor honours the combined frame budget; perk window matches vanilla; recruitment never steers into vanilla's drafts abort.
- **P — telemetry and infra.** Pooled allocation-free perf scopes with 5 s aggregates; rank-ring queue; persistent per-file streams with a retrying, duplicate-free writer; single final manifest + bounded summary; per-frame hitch probe (`FrameHitch` rows); OnceLog never suppresses warnings and has a battle-end reset; reflection warnings bounded; null-guard Finalizers narrowed to known vanilla faults. Detail: [`telemetry.md`](telemetry.md).
- **T1 — tactical evidence/geometry.** Vanilla per-unit ammo rule; arrival ETA in game hours; geometric front axis with lateral bands; intel confidence from observation coverage × map-entry factor × recency (gate 0.35), army odds unknown below it; edge-triggered, phase-preserving replans with one cooldown clock; `DefensiveHold` is "no opinion", never retreat; reserve release is a role change; main-role hysteresis; real battlefield terrain for playbooks; evidence built once per side per frame.
- **T2 — tactical lifecycle.** Battle lifecycle keyed on `BattleUnits` identity with `OnDestroy` teardown; battle survives the night, doctrine pauses at end of day; the W&L feud AIBattle never drives lifecycle or Tick; a W&L CIC player's alliance is suppressed; player-owned units excluded from operation strength; operations-ledger windows on battle time; orchestrator worker thread deleted.
- **T3 — battle writers.** #44 is the single `macroai` owner (value-restoring Prefix + Postfix, never writes retreat 3, W&L player side only on a genuine ledger change, no opinion from an army in Commander Mode Off); #41 charge gate deleted (unreachable); #46 objective-chain guard deleted (BUG-TAC-005 is vanilla behaviour); #42/#53/#55/#56/#59/#60/#62/B7/B8/artillery fixes against vanilla rules; #34 uses vanilla `GetSollGuns`.
- **T4 — posture and stance.** `Util/TacticalStanceWriter` is the single `ai_stance` writer, with yields to vanilla assault escalation and weak-group defend/screen (`Tactical/TacticalStanceOwnership`) and explicit charge denial first; #61 cooldowns on battle time, courier per child, never re-lays a group in contact or wheels one with enemies in range, stands down for macroai 3 / end of day / feud instance, keeps the W&L player's command out; leaf pass to formation slots clamped to the map; facing pulse rotates only and skips artillery; regimental-intelligence consumers removed.
- **Config pass.** Every flag defaults on except `Force All Succession Events`; flags nothing read were deleted; descriptions match code; #34 and the frame probe got rollback flags; W&L player-order keys bound once.
- **Follow-ups.** Artillery all-stance audit reads ammo as the slot mean and is on; Face trigger includes vanilla's cover widening; telegraph placement hooks moved into `Patches/` and refund rejected placements without rerunning `UpdateMacroEconomy`; charge denial demotes doctrine-denied assaults with the orchestrator on.
- **Integration wiring.** One self-advancing doctrine clock (night-frozen) for every tactical doctrine timer; per-side AIBattle, evidence and posture inputs in the coordinator tick; `Util/ScopedState.cs` (`BattleScope`/`CampaignScope`) replaces hand-wired resets; dead worker/parity, regimental-intelligence, help-request, orphaned and test-only code deleted; telemetry budget sheds Performance before Decision rows; OnceLog keys are static.

Per-patch state: [`patch-catalog.md`](patch-catalog.md). Vanilla bugs found along the way: [`bug-fixes/`](bug-fixes/README.md).

### Open items

- **Battle core + BC8:** merged and deployed 2026-10-15 (`75a7000`, SHA-256 `3257f907…`). Next: run the ordered smoke checklist in the battle-core plan § BC7 (`python3 -I tools/smoke-check.py "<GTCW>/BepInEx/LogOutput.log"` first). After the smoke: archive the battle-core spec and plan (`docs/superpowers/{specs,plans}/archive/`), rewrite their cross-references, record the result here. Watch items that may need tuning are listed in the checklist (close-engagement and flank-risk proxies, leaf slots, point-blank watchdog, charge rarity at the 50 m vanilla radius).
- **BC8 experience and drill:** merged `75a7000` (smoke items 2.12-2.14 in the battle-core plan).
- **`[CIC:diag]`** was never written on `main` before `fc0b8af`; fixed in BC7 landing.
- **E need-driven policy** (2026-10-15, merged to `main` at `99bf4eb` and deployed, SHA-256 `055eb440…`; awaiting smoke): daily per-alliance need vector + ranked policy plan (`Strategic/Policies/`, `CampaignNeedBuilder`), #19 consumes it, holds irreversible acts out of vanilla's fallback walk too, and protects the player's in-progress research from vanilla's emergency cancel (`BUG-WL-003`), #17 reads the same needs. New flag `Enable Need-Driven Policy Selection` (config entries 103 after the main merge).  Smoke items and PROVE-MODE: [`superpowers/plans/2026-10-14-need-driven-policy.md`](superpowers/plans/2026-10-14-need-driven-policy.md) § PROVE-MODE / Smoke expectations. A W&L Commander-in-Chief player's alliance gets the need-driven picks too (user decision 2026-10-15). Harness after the landing revision, the main merge and the verify round 3 items: 2003 PASS / 0 FAIL.

## Next stages

From [`superpowers/plans/2026-10-15-full-overhaul-roadmap.md`](superpowers/plans/2026-10-15-full-overhaul-roadmap.md). Each stage ends with a clean build, all-PASS harness, clean `config-audit`/`orphan-sweep`, and an independent review.

1. **C — Checkpoint.** Done: `main` at `6695ecd`, deployed 2026-10-15 (SHA-256 `0831547f…`).
2. **D — Battle core.** Merged to `main` at `fc0b8af` and deployed 2026-10-15; awaiting the in-game smoke (see Open items). BC8 (experience/drill) follows. Spec: [`superpowers/specs/2026-10-15-battle-core-design.md`](superpowers/specs/2026-10-15-battle-core-design.md).
3. **E — Need-driven policy.** Merged to `main` at `99bf4eb` after three verify rounds and deployed 2026-10-15; awaiting smoke. Plan: [`superpowers/plans/2026-10-14-need-driven-policy.md`](superpowers/plans/2026-10-14-need-driven-policy.md); spec [`superpowers/specs/2026-10-14-need-driven-policy-design.md`](superpowers/specs/2026-10-14-need-driven-policy-design.md).
4. **F — W&L chain of command** on the arbiter. Spec: [`superpowers/specs/2026-10-14-wl-chain-of-command-design.md`](superpowers/specs/2026-10-14-wl-chain-of-command-design.md) (§D-§F; §A-§C moved to battle core).
5. **G — Live theater campaign planner.** Spec: [`superpowers/specs/2026-10-14-campaign-planner-design.md`](superpowers/specs/2026-10-14-campaign-planner-design.md). Supersedes locked design choices #3-#6 once it ships.
6. **H — Naval and river war.** Spec: [`superpowers/specs/2026-10-14-naval-river-war-design.md`](superpowers/specs/2026-10-14-naval-river-war-design.md).
7. **I — Release.** Final docs, archive specs/plans, merge → `main`, build hash, deploy with the user's go-ahead.

Vanilla reference docs for D/F/G: [`vanilla-vision-and-movement.md`](vanilla-vision-and-movement.md), [`wl-player-model.md`](wl-player-model.md).

---

## Living docs

| Doc | Covers |
|---|---|
| [`patch-catalog.md`](patch-catalog.md) | Every Harmony patch: ordinal, target, owner, rollback flag |
| [`findings.md`](findings.md) | Decompile workflow, class map, reflection gotchas, vanilla anchors |
| [`telemetry.md`](telemetry.md) | Telemetry pipeline, scopes, budgets, hitch probe |
| [`tactical-orchestrator.md`](tactical-orchestrator.md), [`tactical-operations-ledger.md`](tactical-operations-ledger.md), [`tactical-terrain-facing-discipline.md`](tactical-terrain-facing-discipline.md) | Battle-layer architecture and behaviour |
| [`wl-player-order-doctrine.md`](wl-player-order-doctrine.md), [`wl-dispatch-objective-bridge.md`](wl-dispatch-objective-bridge.md), [`wl-player-model.md`](wl-player-model.md) | W&L player model and order surfaces |
| [`historical-operation-doctrine.md`](historical-operation-doctrine.md), [`coordinated-operation-packages.md`](coordinated-operation-packages.md), [`operational-tempo-doctrine.md`](operational-tempo-doctrine.md), [`fort-construction-governor.md`](fort-construction-governor.md) | Campaign-layer doctrine |
| [`scourge-of-war-ai-anchors.md`](scourge-of-war-ai-anchors.md) | Scourge of War reference anchors |
| [`bug-fixes/`](bug-fixes/README.md) | Vanilla bug backlog and fixes |

---

## Strategic layer — durable architecture

**Six locked design choices** (2026-05-02; #3-#6 will be superseded by the campaign planner, stage G):

1. Slice A first — campaign layer before tactical.
2. Tier 3 scope — replace weak vanilla decisions, extend, add operational plans.
3. Era × faction × officer personality, composed additively with a `[-1, 1]` clamp.
4. Triggered-scripted officer succession (~12 historical events gated on date and war state).
5. Phased operational plans (2-4 phases, one active per side).
6. Daily + event-triggered cadence; adjust the current plan by default, replan only on assumption-invalidating events. Weekly/alternating gates catch up when fast-forward skips days; monthly is a heartbeat on the real month boundary.

**Two-tier hierarchy.** `StrategicCoordinator` → `CIC[alliance]` → theater commanders. CICs decide target, force level and deadline; theater commanders decide route, tempo and posture. No CIC for a player-run alliance.

**Invariants.**

- Harmony patches read mod state; writes happen only in the daily review, event handlers and the per-battle orchestrator tick. A patch that snapshots and restores vanilla state restores in a `[HarmonyFinalizer]` (runs even when vanilla throws).
- Per-battle and per-campaign static state registers a reset with `BattleScope`/`CampaignScope` (`Util/ScopedState.cs`); no hand-wired resets.
- `AICampaign.aifaction` includes alliance 2 (Europe): bound-check every per-alliance array.
- The first campaign date can arrive before `aifaction` exists: the heartbeat may run, operational ledgers defer until it is populated.
- `Policy.CurrentChapter` is `-1` until vanilla's `Policy.CheckForChapterUpdate()` runs; `StrategicCoordinator` invokes it before reading the chapter.
- W&L command selection: vanilla opens `ShowStartUnitSelectionList` once at campaign frame 50; `WlCareerStartSelectionRetryPatch` retries only while no command is selected.
- Sidecar: `<GTCW>/Campaigns/<level>/<sublevel>/<save>/whiskeyrealism.json` (save paths are CWD-relative to the game install), written atomically with `.bak`/`.tmp` fallback on load.
- Vanilla methods with default-valued tail parameters need the full parameter list in reflection lookups.
- W&L scenario "002" has few town objectives; candidate scoring uses `mintownobjectives = 0`, vanilla's own pick uses 1.

Archived design rationale: [`superpowers/specs/archive/2026-05-02-strategic-brain-design.md`](superpowers/specs/archive/2026-05-02-strategic-brain-design.md).

## Tactical layer — durable architecture

Per battle: `TacticalBattleCoordinator` → per-side `TacticalBattleOrchestrator` → `ArmyOrchestrator` → command-node tree and direct-child roles, ticked on the coordinator's doctrine clock. With the battle core (`feat/battle-core`): one `BattleSnapshot` per AI side per tick (fog-honest perception, the D5 parent map, ownership flags) feeds every battle reader, and every Whiskey movement, stance, formation, facing and combat-behavior write is an intent resolved by the arbiter (#68) and issued by the order executor behind `TacticalWriteGuard`; `ai_stance` only through `Util/TacticalStanceWriter`; `macroai` only through #44. `Tactical Commander Mode`: `Active` (release default), `MonitorOnly` (snapshot, perception, ledger and telemetry run, nothing is written), `Off` (rollback: no ledger). Details: [`tactical-orchestrator.md`](tactical-orchestrator.md).

---

## Conventions

- **Source-of-truth order:** shipped code > [`patch-catalog.md`](patch-catalog.md) > per-patch design doc > umbrella spec > archived plan.
- **Stable patch ordinals.** Removed patches keep their ordinal in the catalog's removed section; ordinals are never reused.
- **One concern per patch file**, every Prefix/Postfix/Finalizer inside `TelemetryPerf.Scope`.
- **All config on.** Only `Force All Succession Events` defaults off; `config-audit` enforces it.
- **Merges** go straight to `main` (no PRs) after review; record the build hash here.
