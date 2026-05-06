# Whiskey Realism — Repository Memory

> Last updated: 2026-05-05. Format aligned with Codex project-instruction behavior documented as of April 2026.

This file is the project memory index for agents and maintainers. It is intentionally short and durable. Keep detailed history in `docs/handoff.md`, shipped patch facts in `docs/patch-catalog.md`, and implementation details in source/spec/plan files.

## How Agents Should Use This

- `AGENTS.md` is the canonical auto-loaded instruction file for Codex.
- `MEMORY.md` is not assumed to be auto-loaded by Codex; read it when resuming this repo, investigating prior decisions, or updating handoff state.
- `docs/handoff.md` is the authoritative long-form session-start handoff.
- `docs/patch-catalog.md` is the authoritative shipped-patch inventory.
- `docs/bug-fixes/` is the cross-cutting vanilla bug-fix workstream for narrow confirmed defects and runtime hazards that are not new doctrine slices.
- `docs/superpowers/specs/` holds active design specs; shipped specs live in `docs/superpowers/specs/archive/` (see archive `README.md` for the index). Same split for `docs/superpowers/plans/`.
- Source-of-truth order remains: shipped code > `docs/patch-catalog.md` > per-patch design doc > umbrella spec > archived plan.

## Current Checkpoint

- Current release is `v0.2.2`.
- **Three parallel work streams merged into main on 2026-05-05** (`origin/main` at `a40f1a5`):
  1. **Strategic Resilience Director slice** — pure ledgers (`PhaseTruthLedger`, `ContactEvidenceLedger`, `CampaignPaceLedger`, `BattleHistoryQuery`, `TheaterPressureView`), `OffensiveAvailabilityWrapper` mirroring the 13 vanilla `IsUnitAvailableForOffensiveOperations` gates, `StrategicResilienceDirector` publishing per-alliance `DirectorPosture` with personality-clamped (±50% of personality delta) threshold modifiers, `DirectorPublishClamp` (one publish per real second), `DirectorMemory` sidecar persistence. Modulates probe options, transfer/hold thresholds, formation directive gates, fiscal/construction scoring, and defense budgets.
  2. **W&L Camp Realism Slice 1** — `#29 WlCampRealismPatch` corrects short-camp minimum undercrediting, retunes station 12 Rest from `6h/9h` to `3h/6h`, suppresses responsive weighting inside diary/event threshold scopes while keeping it for normal payoff/UI paths, softens command-count dilution for Drill/Motivate/Recruitment/Readiness.
  3. **Vanilla bug fixes** — `#28` economy NRE guard, `#30` commander previous-command cleanup, `#31` filter-map initialization guard, `#32` policy-36 state handover thresholding, `#33` AI fleet patrol reset, `#34` artillery combine gun preservation.
- **Merged main state:** console harness 265 PASS / 0 FAIL, build clean (0/0), merged DLL deployed and SHA-256 verified `cc2ed52108abee06b3989c643db46459796603e68964353d57df06ff3b6ff369` (380928 bytes). Full merged-surface smoke is pending — the game must be relaunched on the merged DLL before runtime claims.
- **Pre-existing perf concern carried over:** `[DailyOps:Perf]` reports `formationMs` median ~163 ms post-hotfix (cheap days ~90 ms / rebuild days ~250 ms) and `defenseMs` ~165 ms. Flagged for a follow-up perf hotfix targeting `DefenseIntentRuntime` and the rebuild-day formation path.
- **Slice B (tactical brain) is paused.** Umbrella spec at `docs/superpowers/specs/2026-05-05-tactical-brain-design.md`; vanilla verification at `docs/superpowers/specs/2026-05-05-tactical-brain-vanilla-verification.md`; focused weapons/ammunition adjunct spec at `docs/superpowers/specs/2026-05-05-tactical-weapons-ammunition-design.md`; master sequencing plan at `docs/superpowers/plans/2026-05-05-tactical-brain-master-sequencing.md`; B0 observer plan at `docs/superpowers/plans/2026-05-05-tactical-b0-observer.md`. Do not advance tactical code or plans unless the user explicitly reopens tactical work.
- Default-off telegraph AI (#24) still needs a focused enabled smoke run; that's an opportunistic Slice A follow-up, not a blocker.
- Bug Fixes now has a durable workstream at `docs/bug-fixes/`. The active queues are `docs/bug-fixes/vanilla-ai-economy.md`, `docs/bug-fixes/vanilla-tick-system.md`, and `docs/bug-fixes/vanilla-hotfix-parity.md`: `BUG-ECO-001` has regression coverage for the runtime-confirmed fiscal subsidy sentinel leak; `BUG-ECO-003` has #28 guard code for repeated `Economy.UpdateEconomyAllianceData` Player.log NREs; `BUG-TICK-001` extends #26 with pause/high-speed AI governor behavior; `BUG-TICK-002` has #31 bounded filter-map initialization; Community Hotfix parity has #30 commander previous-command cleanup, #32 policy-36 state handover thresholding, #33 AI fleet patrol reset, and #34 artillery combine gun preservation. Current deployed bug-fix DLL SHA-256 is `7da618bfd0bbabfb0cf4288d2ed478f0b9758baac3b549fb75144a40f1216956`; fresh-launch smoke is still required before marking these new fixes shipped. Policy null-personality, supply-depot construction, and railroad construction remain repro/backlog items.
- Slices C (W&L hierarchy AI) and D (additional historical flavor) remain deferred.

## Load-Bearing Runtime Lessons

- W&L command selection is fragile. Vanilla invokes `CareerInformationPanel.ShowStartUnitSelectionList(true)` at campaign frame 50, then pauses. Do not invoke it before `GameVars.frame >= 50`.
- A visible `UnitSelectionListObject` is not enough. The picker is usable only when private `unitlineappointcommand` rows exist.
- Existing BepInEx config values override C# defaults after first plugin load; changing `Plugin.cs` defaults alone does not update an existing user's config.
- DLL-affecting changes are not ready until built, deployed, and verified by matching `sha256sum` between `dist/WhiskeyRealism.dll` and the BepInEx plugin DLL.
- Harmony patches must not mutate strategic mod state. State writes happen through coordinator/daily/event paths; patches steer vanilla decisions and log bounded evidence.
- Defense Intent Ledger (2026-05-04) migrated the operational tick from weekly to daily; `EmergencyExitStableTicks=14` rescale and `FrontSectorRuntime.Signature` bucket coarsening make daily safe from thrash.
- Campaign-stutter diagnosis on 2026-05-05 found vanilla 20x/50x AI as the baseline hotspot: `AICampaign.Update` runs `floor(sqrt(GameVars.gamespeed))` `UpdateUnitAI()` passes per rendered frame and a live 20x frame measured `26.35 ms` before Whiskey extra catch-up. Current deployed code compacts default `[DefenseIntent]` logs, caches #25/AICampaign/#4 reflection, de-jitters daily strategic review (`front`/`fiscal`/`defense` daily; `formation` alternating/forced; `army-area` and construction full scans weekly/source-driven), enables #26 `CampaignAiUpdateGovernorPatch` by default to cap vanilla high-speed passes while preserving vanilla side effects, removes reflection from `DefenseIntentRuntime.GetXZDistance()`, uses typed `Regiment` reads in defense hot loops, and resolves `Frontline2` once per defense pass. The #21 catch-up patch stands down while #26 is enabled. The shipped performance plan is archived at `docs/superpowers/plans/archive/2026-05-05-campaign-ai-performance-governor.md`.
- Fort-spam diagnosis on 2026-05-05 found vanilla `AICampaign.CheckFortConstruction(int)` has one active fort order per faction and a nearby-fort spacing check, but no area/capital cap once prior orders finish; capital-defense units get a looser `0.6x` spacing multiplier. #27 `FortConstructionGovernorPatch` now filters saturated `fortconstructionsites` per vanilla call, using local fort/order counts plus local enemy threat. Details live in `docs/fort-construction-governor.md`.
- `DefenseCooldownTable._recoveredStarted` is an idempotency guard: set it on the same tick that the cooldown entry transitions from recovering → active to prevent double-triggers across multiple daily ticks.
- `CampaignMapLedger` is now the active-map source for towns, represented states, forts, sea harbors, and river harbors. Do not reintroduce hardcoded Mississippi/Alabama assumptions for W&L unless the runtime map actually exposes those states.
- #4 capital-defense add-on should stay proportional: readiness-gated, morale-adjusted, and penalizing gross overmatch for small threats.
- Strategic anti-zerg doctrine is now load-bearing: `AssetProximity` is observation/local pressure, not an invasion movement trigger; theater/front budgets decide whether units can export; cross-map defense requires a real national emergency; capital defense uses a capped package; and release/return flows must not strand units or strip other theaters.
- Operational probe doctrine is now load-bearing: a strategic plan may test an area with one bounded same-area formation before mass commitment; enemy reaction should pause instead of pulling armies from other theaters; favorable contact can escalate; probes must never draw from critical hold sectors; and tempo must vary by vanilla chapter/Whiskey era/season/faction/personality so 1861 probing is slower, 1864 Union pressure is more sustained, late CSA commitment is more conservative, and winter operations slow down.
- `AICampaign.aifaction` can include alliance 2 (Europe). Per-alliance arrays sized to Union+CSA (length 2) must bound-check against `arr.Length` before indexing; `AICampaignReflect.GetAllianceId(_aifaction)` returns the underlying alliance, which can be 2. Slice 2 patches (#25 + custom-order runner) early-return for `allianceId > 1` rather than indexing.
- Test project (`tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`) uses explicit `<Compile Include>` entries per source file — there is no glob. Adding a new strategic file requires a matching csproj entry; deleting one requires removing the entry. Build will succeed against stale entries until the file is actually consumed by a test.

## Current Priorities

1. **Strategic operational-probe runtime smoke**: restart GTCW with the deployed DLL, tail `LogOutput.log`, and confirm `[OperationalProbe]` decisions are bounded and warning-free while anti-zerg checks still pass. See `docs/superpowers/plans/2026-05-05-strategic-operational-probe-contact.md`.
2. Slice A follow-ups (opportunistic, none blocking):
   - Slice 3 guard-budget tuning from runtime telemetry — adjust `GuardBudgetFraction` (default 0.10), `cooldownDays` (default 4), and aggregator thresholds (0.75 / 1.0 / 1.25) per faction/era from observed telemetry.
   - `AssetRoleCatalog` refinement from non-East scenarios — current ~50 entries cover the W&L East-coast smoke; look for `[DefenseIntent:asset] missing-role` lines in CSA Western/Trans-Miss campaigns.
   - Optional telegraph AI smoke with `Enable Telegraph AI = true`.
   - Supply-depot/railroad construction doctrine only after observer data proves the gap. Fort saturation guard #27 has shipped; future fort work should be tuning from runtime `[Patch:FortGovernor]` evidence, not a new broad construction rewrite.
3. Keep Slice B tactical work, Slices C (W&L hierarchy AI), and D (additional historical flavor) deferred unless the user explicitly redirects.

## Update Rules

- Update this file only for durable state that should survive compaction, handoff drift, or fresh agent sessions.
- Do not paste long logs here. Summarize the decision and link to the authoritative file or commit.
- If this file conflicts with shipped code or `docs/patch-catalog.md`, the shipped code/catalog wins and this file should be corrected.
- Keep this file repo-safe: no secrets, tokens, local-only account details, or unredacted private logs.
