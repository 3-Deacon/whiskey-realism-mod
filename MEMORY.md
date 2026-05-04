# Whiskey Realism — Repository Memory

> Last updated: 2026-05-04. Format aligned with Codex project-instruction behavior documented as of April 2026.

This file is the project memory index for agents and maintainers. It is intentionally short and durable. Keep detailed history in `docs/handoff.md`, shipped patch facts in `docs/patch-catalog.md`, and implementation details in source/spec/plan files.

## How Agents Should Use This

- `AGENTS.md` is the canonical auto-loaded instruction file for Codex.
- `MEMORY.md` is not assumed to be auto-loaded by Codex; read it when resuming this repo, investigating prior decisions, or updating handoff state.
- `docs/handoff.md` is the authoritative long-form session-start handoff.
- `docs/patch-catalog.md` is the authoritative shipped-patch inventory.
- Source-of-truth order remains: shipped code > `docs/patch-catalog.md` > per-patch design doc > umbrella spec > archived plan.

## Current Checkpoint

- Current release is `v0.2.2`.
- Current main is post-release Slice A cleanup plus construction steering Slice B, fast-forward diagnostics/throttle, campaign-map town/state/fort/harbor awareness, and proportional capital-defense force sizing.
- Current deployed DLL SHA-256, if no newer commit has superseded it, is `15c3e21be80ff6ae9c37225387287576494d86e0d6824251e3c319ef4277dc9d`.
- Default-off telegraph AI still needs a focused enabled smoke run.
- Full non-capital/coastal/patrol defense steering, fort/depot/railroad construction steering, tactical brain, W&L hierarchy AI, and additional historical flavor remain deferred.

## Load-Bearing Runtime Lessons

- W&L command selection is fragile. Vanilla invokes `CareerInformationPanel.ShowStartUnitSelectionList(true)` at campaign frame 50, then pauses. Do not invoke it before `GameVars.frame >= 50`.
- A visible `UnitSelectionListObject` is not enough. The picker is usable only when private `unitlineappointcommand` rows exist.
- Existing BepInEx config values override C# defaults after first plugin load; changing `Plugin.cs` defaults alone does not update an existing user's config.
- DLL-affecting changes are not ready until built, deployed, and verified by matching `sha256sum` between `dist/WhiskeyRealism.dll` and the BepInEx plugin DLL.
- Harmony patches must not mutate strategic mod state. State writes happen through coordinator/weekly/event paths; patches steer vanilla decisions and log bounded evidence.
- `CampaignMapLedger` is now the active-map source for towns, represented states, forts, sea harbors, and river harbors. Do not reintroduce hardcoded Mississippi/Alabama assumptions for W&L unless the runtime map actually exposes those states.
- #4 capital-defense add-on should stay proportional: readiness-gated, morale-adjusted, and penalizing gross overmatch for small threats.

## Current Priorities

1. Design/implement the full `DefenseIntentLedger` slice for non-capital towns, coastal lands, ports, forts, patrol zones, and vanilla `CheckForDefensiveOperations` steering.
2. Smoke construction steering Slice B and optional telegraph AI with `Enable Telegraph AI = true` only for a focused test.
3. Continue fort/depot/railroad construction doctrine only after observer data proves the gap.
4. Keep tactical and W&L hierarchy AI deferred unless the user explicitly redirects.

## Update Rules

- Update this file only for durable state that should survive compaction, handoff drift, or fresh agent sessions.
- Do not paste long logs here. Summarize the decision and link to the authoritative file or commit.
- If this file conflicts with shipped code or `docs/patch-catalog.md`, the shipped code/catalog wins and this file should be corrected.
- Keep this file repo-safe: no secrets, tokens, local-only account details, or unredacted private logs.
