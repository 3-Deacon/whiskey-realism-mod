# Full Overhaul Roadmap (2026-10-15)

Tracking doc for the user's direction: fix every review finding and the architecture problems the reviews exposed, leave no dead/orphaned code, merge straight to `main` (no PRs). Each stage ends with `./build.sh` (0 errors, no new warnings), the harness all-PASS, `tools/orphan-sweep.py` and `tools/config-audit.py` clean for touched files, and an independent review.

| # | Stage | Depends on | Output |
|---|---|---|---|
| A | Finish fix workstreams C1 ✔, C2a, C2b ✔, P, T1, T2, T3, T4 (`2026-10-14-full-review-remediation.md`) and merge into `feat/orchestrator-worker-thread` | — | reviewed merges |
| B | Integration pass | A | see below |
| C | Checkpoint: merge `feat/orchestrator-worker-thread` → `main`, build from `main`, record SHA-256 in `docs/handoff.md` | B | fixed baseline on `main` |
| D | Battle core: per-side `BattleSnapshot` (built once per tick, pooled), fog-honest perception from vanilla's AI-side fog grid, single `PlayerOwnership` model, single `OrderArbiter` all battle movers submit intents to; migrate #61, plays, division play, B8, artillery, #59, #42, #46; remove Prefix-skip/snapshot-restore patches the arbiter makes unnecessary | C, vision audit (`docs/vanilla-vision-and-movement.md`) | spec → plan → slice |
| E | Need-driven policy (`2026-10-14-need-driven-policy.md`) | C | slice |
| F | W&L chain of command (`specs/2026-10-14-wl-chain-of-command-design.md`), built on the arbiter (target resolution + dispatches in one place) | D | plan → slice |
| G | Live theater campaign planner (`specs/2026-10-14-campaign-planner-design.md`), `EnemyArmyModel` re-based on vanilla campaign spotting, reachability checks from the vision/movement audit | C, E | plan → slice |
| H | Naval & river war (`specs/2026-10-14-naval-river-war-design.md`) | G | plan → slice |
| I | Final docs (handoff, patch-catalog, findings, AGENTS.md locked choices, telemetry, MEMORY), archive specs/plans, merge → `main`, build hash; ask user before reinstalling BepInEx and deploying | H | release state |

## Stage B — integration pass (on `feat/orchestrator-worker-thread`)

1. **Cross-branch wiring** (from workstream reports): per-side AIBattle via `SelectAiBattleIndexForSide` in `Tick`; `ArmyPostureInputsRuntime.Refresh` per side per tick; reserve-release producer → `TacticalReserveCommitGate`/`PermitReserveListBias`; `StanceForLedgerTask` → `MapForGroupStanceScorer`; `BuildArmyEvidenceForSide` → `ArmyEvidenceBuilder.Build(...).OwnEvidence`.
2. **One doctrine clock**: `TacticalDoctrineClock.NowBattleSeconds` → `TacticalBattleCoordinator.DoctrineBattleHours` (night-frozen); every tactical doctrine timer (T1 phases/hysteresis/ledger decay, T2 commit windows, T4 arrival hold and artillery hold, B8/#56 `DecideReserve`) reads it. Night-jump fixture test.
3. **Scoped-state registry**: `BattleScope.Register(Action reset)` / `CampaignScope.Register(...)`; every static per-battle/per-campaign cache registers itself (ArmyEvidenceBuilder, TacticalStanceWriter, ArmyPostureInputsRuntime, OnceLog tactical keys, diagnostics caches, TacticalDoctrineClock, …); coordinator teardown and `StrategicCoordinator.ResetAll` call the registry. No hand-wired reset calls remain.
4. **Test auto-discovery**: harness discovers `internal static class *Tests { public static void Run() }` by reflection; `Program.cs` monolith split into per-area files; registration list deleted.
5. **Config**: `tools/config-audit.py` → 0 findings (delete dead flags, dedupe Player Order Doctrine binds, fix every description, rollback flag for #34, new values from T1/T4/P/C1 reports).
6. **Dead code**: delete worker/parity/regimental-intelligence leftovers, `TacticalDecisionSnapshot` if the T4 consumer is inert, help-request members, `IsLegalMacroAi`, `sameNonRootParent`, `ObjectiveChainAdvance`, writer leftovers; `tools/orphan-sweep.py` → 0 findings repo-wide (`--include-test-only` reviewed).
7. **Patch first-fire registry**: every patch reports first fire through one helper; `tools/` smoke check lists patches that never fired in a session log.
8. **Telemetry/logging**: Plugin `Update()` → `TelemetryFrameProbe.Tick`; static OnceLog keys everywhere; `TelemetryBudget` cuts Performance before Decision rows; `docs/telemetry.md` text from WS P.
9. **Docs**: AGENTS.md (writer list, BattleMacroAIOverridePatch removal, mode contract, transform.parent rule), patch-catalog (#41 removed, #44 owner), bug-fixes entries (MicroAICheckForCharges dead compare 4910; CIC-player fleets/army groups; type-16 prestige).
