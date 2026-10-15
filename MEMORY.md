# Whiskey Realism — Repository Memory

> Durable index. Volatile state (branch, build hash, harness count, deploy status) lives only in [`docs/handoff.md`](docs/handoff.md). Last reviewed 2026-10-15 after the review remediation.

## How Agents Should Use This

- `AGENTS.md` is the canonical auto-loaded instruction file (`CLAUDE.md` is a symlink to it).
- `MEMORY.md` is not assumed to be auto-loaded; read it when resuming this repo or investigating prior decisions.
- `docs/handoff.md` — current state and next stages. `docs/patch-catalog.md` — every Harmony patch, its owner and rollback flag.
- `docs/bug-fixes/` — confirmed vanilla defects and runtime hazards that are not doctrine slices.
- `docs/superpowers/specs/` and `plans/` hold approved, not-yet-shipped work (2026-10-14/15: remediation plan, overhaul roadmap, battle core, campaign planner, W&L chain of command, need-driven policy, naval/river war). Shipped artifacts move to `archive/`.
- `docs/reviews/` — the 2026-10-14 full-mod reviews the remediation fixed.
- Source-of-truth order: shipped code > `docs/patch-catalog.md` > per-patch design doc > umbrella spec > archived plan.

## Roadmap

`docs/superpowers/plans/2026-10-15-full-overhaul-roadmap.md`: checkpoint to `main` → battle core (snapshot, fog-honest perception, ownership, order arbiter) → need-driven policy → W&L chain of command → live theater campaign planner → naval/river war → release. Everything defaults on; nothing ships as dead code or an off valve.

## Load-Bearing Runtime Lessons

- W&L command selection is fragile. Vanilla invokes `CareerInformationPanel.ShowStartUnitSelectionList(true)` at campaign frame 50, then pauses. Do not invoke it before `GameVars.frame >= 50`.
- A visible `UnitSelectionListObject` is not enough. The picker is usable only when private `unitlineappointcommand` rows exist.
- Existing BepInEx config values override C# defaults after first plugin load; changing `Plugin.cs` defaults alone does not update an existing user's config.
- DLL-affecting changes are not ready until built, deployed, and verified by matching `sha256sum` between `dist/WhiskeyRealism.dll` and the BepInEx plugin DLL.
- Harmony patches must not mutate strategic mod state. State writes happen through coordinator/daily/event paths; patches steer vanilla decisions and log bounded evidence.
- Defense Intent Ledger (2026-05-04) migrated the operational tick from weekly to daily; `EmergencyExitStableTicks=14` rescale and `FrontSectorRuntime.Signature` bucket coarsening make daily safe from thrash.
- Campaign-stutter diagnosis on 2026-05-05 found vanilla 20x/50x AI as the baseline hotspot: `AICampaign.Update` runs `floor(sqrt(GameVars.gamespeed))` `UpdateUnitAI()` passes per rendered frame and a live 20x frame measured `26.35 ms` before Whiskey extra catch-up. Current deployed code compacts default `[DefenseIntent]` logs, caches #25/AICampaign/#4 reflection, de-jitters daily strategic review (`front`/`fiscal`/`defense` daily; `formation` alternating/forced; `army-area` and construction full scans weekly/source-driven), enables #26 `CampaignAiUpdateGovernorPatch` by default to cap vanilla high-speed passes while preserving vanilla side effects, removes reflection from `DefenseIntentRuntime.GetXZDistance()`, uses typed `Regiment` reads in defense hot loops, and resolves `Frontline2` once per defense pass. The #21 catch-up patch stands down while #26 is enabled. The shipped performance plan is archived at `docs/superpowers/plans/archive/2026-05-05-campaign-ai-performance-governor.md`.
- `BattlefieldSetup.AssignFilters` runs at campaign frame 29 and assumes all economy filter maps already have textures; `BattleUnits.CampaignDataRuns` does not run until frame 31. If W&L startup reaches `AssignFilters` before `Economy.UpdateFilterMaps` has created those maps, vanilla throws and never creates `BattlefieldSetup.productionmapsymbols`, causing later `Economy.UpdateFilterMaps` full-cycle resets to NRE at `productionmapsymbols.ResetValues()`. #37 mirrors vanilla map initialization before `AssignFilters`; #31 remains the bounded tail guard and now logs production-symbol health if it still fires.
- `Diary.UpdateEventQueue` starts calling `Diary.UpdateEvents` at campaign frame 50, but `UpdateEvents` assumes W&L commander/current-command, imported diaryevents, cardinal points, food, and weather are ready. If W&L startup has not selected a live command yet, vanilla can throw early under `BattleUnits.Update`. #29 now skips only W&L diary event updates until those dependencies are ready, then falls through to vanilla; do not convert this into broad diary exception suppression.
- Fort-spam diagnosis on 2026-05-05 found vanilla `AICampaign.CheckFortConstruction(int)` has one active fort order per faction and a nearby-fort spacing check, but no area/capital cap once prior orders finish; capital-defense units get a looser `0.6x` spacing multiplier. #27 `FortConstructionGovernorPatch` now filters saturated `fortconstructionsites` per vanilla call, using local fort/order counts plus local enemy threat. Details live in `docs/fort-construction-governor.md`.
- `DefenseCooldownTable._recoveredStarted` is an idempotency guard: set it on the same tick that the cooldown entry transitions from recovering → active to prevent double-triggers across multiple daily ticks.
- `CampaignMapLedger` is now the active-map source for towns, represented states, forts, sea harbors, and river harbors. Do not reintroduce hardcoded Mississippi/Alabama assumptions for W&L unless the runtime map actually exposes those states.
- #4 capital-defense add-on should stay proportional: readiness-gated, morale-adjusted, and penalizing gross overmatch for small threats.
- Strategic anti-zerg doctrine is now load-bearing: `AssetProximity` is observation/local pressure, not an invasion movement trigger; theater/front budgets decide whether units can export; cross-map defense requires a real national emergency; capital defense uses a capped package; and release/return flows must not strand units or strip other theaters.
- Operational probe doctrine is now load-bearing: a strategic plan may test an area with one bounded same-area formation before mass commitment; enemy reaction should pause instead of pulling armies from other theaters; favorable contact can escalate; probes must never draw from critical hold sectors; and tempo must vary by vanilla chapter/Whiskey era/season/faction/personality so 1861 probing is slower, 1864 Union pressure is more sustained, late CSA commitment is more conservative, and winter operations slow down.
- `AICampaign.aifaction` can include alliance 2 (Europe). Per-alliance arrays sized to Union+CSA (length 2) must bound-check against `arr.Length` before indexing; `AICampaignReflect.GetAllianceId(_aifaction)` returns the underlying alliance, which can be 2. Slice 2 patches (#25 + custom-order runner) early-return for `allianceId > 1` rather than indexing.

- A Steam game update can rewrite the whole install (build 14703111, 2026-10-14, removed BepInEx, plugin, cfg and logs). After an update: reinstall BepInEx 5.4.x x64, re-decompile, re-check patch anchors, redeploy.
- Battle write ownership: every write passes `TacticalWriteGuard` (Commander Mode `Active`, AI-controlled unit, W&L player command protected, fail closed); `ai_stance` only via `Util/TacticalStanceWriter`; `macroai` only via #44 `BattleMacroStrategyPatch`. Two writers on one vanilla field oscillate — that was the root of several 2026-10 review findings.
- W&L runs a third `AIBattle` (`isplayeraiorfeud == 2`, the feud instance) that duplicates side 0's macro cycle. Battle-level writers and the coordinator must skip it; per-side reads pick the side's own instance (`SelectAiBattleIndexForSide`).
- Battle doctrine time is `TacticalDoctrineTime`: game-hours from `GameVars.currenttimefromstart`, frozen across vanilla's night end-of-day cycle (the raw clock jumps ~9 h overnight). Never use wall-clock time for doctrine.
- Vanilla AI enemy lists (`enemyinrangereg` etc.) are range-limited, not fog-filtered; see `docs/vanilla-vision-and-movement.md` before claiming "fog-honest" perception.
- Per-battle/per-campaign static state registers a reset with `BattleScope`/`CampaignScope` (`Util/ScopedState.cs`); hand-wired resets drifted and leaked state between battles and campaigns.
- Restores of temporarily rewritten vanilla state belong in a `[HarmonyFinalizer]`; a Postfix restore is skipped when vanilla throws.
- The command hierarchy is `allattachedunits`/`GetAttachedUnitsReg`, not `transform.parent` (corps/divisions are scene-graph siblings).
- `tools/config-audit.py` and `tools/orphan-sweep.py` keep config honest (all on, read, bound once, described truthfully) and dead code out; both must report 0.

## Update Rules

- Update this file only for durable state that should survive compaction, handoff drift, or fresh agent sessions.
- Do not paste long logs here. Summarize the decision and link to the authoritative file or commit.
- If this file conflicts with shipped code or `docs/patch-catalog.md`, the shipped code/catalog wins and this file should be corrected.
- Keep this file repo-safe: no secrets, tokens, local-only account details, or unredacted private logs.
