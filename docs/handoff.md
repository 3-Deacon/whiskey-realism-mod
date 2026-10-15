# Whiskey Realism — Master Handoff

**Read first at session start.** Where the project stands, what just landed, and what comes next. Chronology lives in `git log`; this page keeps only current state and durable invariants.

---

## Current state (2026-10-15)

| | |
|---|---|
| **Branch** | `feat/orchestrator-worker-thread` (despite the name, the worker thread is deleted). Review remediation merged at `08f4a3c`. Not yet merged to `main`. |
| **`main`** | `262a50d` — last released line is v0.2.2 (2026-05-04); `main` carries later Slice B / orchestrator work but none of the 2026-10-14/15 remediation. |
| **Build** | `./build.sh` — 0 warnings, 0 errors at `08f4a3c`. |
| **Harness** | 1680 PASS / 0 FAIL at `08f4a3c`. The harness restructure (auto-discovered `*Tests.Run()`, `Program.cs` split) is in flight on `fix/integ-tests` and may change the count. |
| **Tools** | `python3 -I tools/config-audit.py .` (0 findings over 102 entries) and `python3 -I tools/orphan-sweep.py . [--include-test-only]` (0 findings). See AGENTS.md § Testing. |
| **Game install** | Steam build 14703111 (2026-10-14 update) wiped BepInEx. **BepInEx 5.4.23.5 x64 was reinstalled 2026-10-15**; `BepInEx/plugins/` is empty — **no Whiskey DLL is deployed**, no `config/` or `LogOutput.log` exists yet. First launch after deploy writes a fresh cfg with the C# defaults (all on). |
| **Decompile** | `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`, 266,136 lines for build 14703111. Regenerate per `docs/findings.md` if `/tmp` was wiped. |

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

### Open items on this branch

- Harness auto-discovery / `Program.cs` split (roadmap B.4) — in flight on `fix/integ-tests`.
- Patch first-fire registry + session-log smoke check (roadmap B.7) — not started.
- No in-game smoke of the remediated DLL yet (nothing deployed).

## Next stages

From [`superpowers/plans/2026-10-15-full-overhaul-roadmap.md`](superpowers/plans/2026-10-15-full-overhaul-roadmap.md). Each stage ends with a clean build, all-PASS harness, clean `config-audit`/`orphan-sweep`, and an independent review.

1. **C — Checkpoint.** Merge `feat/orchestrator-worker-thread` → `main`, build from `main`, record the DLL SHA-256 here. Ask the user before deploying.
2. **D — Battle core.** Per-side `BattleSnapshot`, fog-honest perception from vanilla's AI fog grid, one `PlayerOwnership` model, one `OrderArbiter` for every battle mover, infantry charge doctrine. Spec: [`superpowers/specs/2026-10-15-battle-core-design.md`](superpowers/specs/2026-10-15-battle-core-design.md).
3. **E — Need-driven policy.** Plan: [`superpowers/plans/2026-10-14-need-driven-policy.md`](superpowers/plans/2026-10-14-need-driven-policy.md); spec [`superpowers/specs/2026-10-14-need-driven-policy-design.md`](superpowers/specs/2026-10-14-need-driven-policy-design.md).
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

Per battle: `TacticalBattleCoordinator` → per-side `TacticalBattleOrchestrator` → `ArmyOrchestrator` → command-node tree and direct-child roles, ticked on the coordinator's doctrine clock. Writes into vanilla battle state go through `TacticalWriteGuard`; `ai_stance` only through `Util/TacticalStanceWriter`; `macroai` only through #44. `Tactical Commander Mode`: `Active` (release default), `MonitorOnly` (ledger and telemetry run, nothing is written), `Off` (rollback: no ledger). Details: [`tactical-orchestrator.md`](tactical-orchestrator.md).

---

## Conventions

- **Source-of-truth order:** shipped code > [`patch-catalog.md`](patch-catalog.md) > per-patch design doc > umbrella spec > archived plan.
- **Stable patch ordinals.** Removed patches keep their ordinal in the catalog's removed section; ordinals are never reused.
- **One concern per patch file**, every Prefix/Postfix/Finalizer inside `TelemetryPerf.Scope`.
- **All config on.** Only `Force All Succession Events` defaults off; `config-audit` enforces it.
- **Merges** go straight to `main` (no PRs) after review; record the build hash here.
