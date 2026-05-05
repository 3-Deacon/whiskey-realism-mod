# Whiskey Realism — Repository Memory

> Last updated: 2026-05-05. Format aligned with Codex project-instruction behavior documented as of April 2026.

This file is the project memory index for agents and maintainers. It is intentionally short and durable. Keep detailed history in `docs/handoff.md`, shipped patch facts in `docs/patch-catalog.md`, and implementation details in source/spec/plan files.

## How Agents Should Use This

- `AGENTS.md` is the canonical auto-loaded instruction file for Codex.
- `MEMORY.md` is not assumed to be auto-loaded by Codex; read it when resuming this repo, investigating prior decisions, or updating handoff state.
- `docs/handoff.md` is the authoritative long-form session-start handoff.
- `docs/patch-catalog.md` is the authoritative shipped-patch inventory.
- `docs/superpowers/specs/` holds active design specs; shipped specs live in `docs/superpowers/specs/archive/` (see archive `README.md` for the index). Same split for `docs/superpowers/plans/`.
- Source-of-truth order remains: shipped code > `docs/patch-catalog.md` > per-patch design doc > umbrella spec > archived plan.

## Current Checkpoint

- Current release is `v0.2.2`.
- **Slice A is fully shipped and verified.** Defense Intent Ledger Slice 1+2 was smoke-validated end-to-end on 2026-05-05 (alliance 2 Europe bound-check fix in commit `e44c0cb`): `[DefenseIntent] custom-order ...` lines emit for both ActiveInvasion and CoastalGuard, shipped #4 capital-defense path coexists, zero warnings/errors. See `docs/handoff.md` for the current deployed DLL SHA-256 (it churns frequently; do not pin it here).
- **Slice B (tactical brain) is in design phase.** Umbrella spec at `docs/superpowers/specs/2026-05-05-tactical-brain-design.md`; vanilla verification at `docs/superpowers/specs/2026-05-05-tactical-brain-vanilla-verification.md`. No plan committed yet, no code yet.
- Default-off telegraph AI (#24) still needs a focused enabled smoke run; that's an opportunistic Slice A follow-up, not a blocker.
- Slices C (W&L hierarchy AI) and D (additional historical flavor) remain deferred.

## Load-Bearing Runtime Lessons

- W&L command selection is fragile. Vanilla invokes `CareerInformationPanel.ShowStartUnitSelectionList(true)` at campaign frame 50, then pauses. Do not invoke it before `GameVars.frame >= 50`.
- A visible `UnitSelectionListObject` is not enough. The picker is usable only when private `unitlineappointcommand` rows exist.
- Existing BepInEx config values override C# defaults after first plugin load; changing `Plugin.cs` defaults alone does not update an existing user's config.
- DLL-affecting changes are not ready until built, deployed, and verified by matching `sha256sum` between `dist/WhiskeyRealism.dll` and the BepInEx plugin DLL.
- Harmony patches must not mutate strategic mod state. State writes happen through coordinator/daily/event paths; patches steer vanilla decisions and log bounded evidence.
- Defense Intent Ledger (2026-05-04) migrated the operational tick from weekly to daily; `EmergencyExitStableTicks=14` rescale and `FrontSectorRuntime.Signature` bucket coarsening make daily safe from thrash.
- `DefenseCooldownTable._recoveredStarted` is an idempotency guard: set it on the same tick that the cooldown entry transitions from recovering → active to prevent double-triggers across multiple daily ticks.
- `CampaignMapLedger` is now the active-map source for towns, represented states, forts, sea harbors, and river harbors. Do not reintroduce hardcoded Mississippi/Alabama assumptions for W&L unless the runtime map actually exposes those states.
- #4 capital-defense add-on should stay proportional: readiness-gated, morale-adjusted, and penalizing gross overmatch for small threats.
- `AICampaign.aifaction` can include alliance 2 (Europe). Per-alliance arrays sized to Union+CSA (length 2) must bound-check against `arr.Length` before indexing; `AICampaignReflect.GetAllianceId(_aifaction)` returns the underlying alliance, which can be 2. Slice 2 patches (#25 + custom-order runner) early-return for `allianceId > 1` rather than indexing.
- Test project (`tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`) uses explicit `<Compile Include>` entries per source file — there is no glob. Adding a new strategic file requires a matching csproj entry; deleting one requires removing the entry. Build will succeed against stale entries until the file is actually consumed by a test.

## Current Priorities

1. **Slice B planning**: brainstorm-finalize the existing tactical-brain umbrella spec, then write the implementation plan under `docs/superpowers/plans/`. No code until the plan exists.
2. Slice A follow-ups (opportunistic, none blocking):
   - Slice 3 guard-budget tuning from runtime telemetry — adjust `GuardBudgetFraction` (default 0.10), `cooldownDays` (default 4), and aggregator thresholds (0.75 / 1.0 / 1.25) per faction/era from observed telemetry.
   - `AssetRoleCatalog` refinement from non-East scenarios — current ~50 entries cover the W&L East-coast smoke; look for `[DefenseIntent:asset] missing-role` lines in CSA Western/Trans-Miss campaigns.
   - Optional telegraph AI smoke with `Enable Telegraph AI = true`.
   - Fort/depot/railroad construction doctrine only after observer data proves the gap.
3. Keep Slices C (W&L hierarchy AI) and D (additional historical flavor) deferred unless the user explicitly redirects.

## Update Rules

- Update this file only for durable state that should survive compaction, handoff drift, or fresh agent sessions.
- Do not paste long logs here. Summarize the decision and link to the authoritative file or commit.
- If this file conflicts with shipped code or `docs/patch-catalog.md`, the shipped code/catalog wins and this file should be corrected.
- Keep this file repo-safe: no secrets, tokens, local-only account details, or unredacted private logs.
