# Campaign AI Performance Governor Implementation Plan

> **Archived:** Shipped on 2026-05-05 as strategic cadence de-jitter plus #26 `CampaignAiUpdateGovernorPatch`. Current deployed-hash state lives in `docs/handoff.md`; shipped patch facts live in `docs/patch-catalog.md`.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce high-speed campaign stutter by de-jittering Whiskey daily ledgers and adding a guarded vanilla `AICampaign.Update()` governor.

**Architecture:** Keep threat, front, and fiscal observation daily; run expensive Whiskey ledgers on source-signature, alternating, or weekly cadence. Add a Harmony Prefix wrapper for vanilla `AICampaign.Update()` that delegates to vanilla private methods rather than copying the private AI job ladder.

**Tech Stack:** BepInEx 5, HarmonyX Prefix/Postfix patches, C# netstandard2.1, console harness tests.

---

### Task 1: Pure Cadence Policy

**Files:**
- Create: `src/WhiskeyRealism/Strategic/StrategicCadencePolicy.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`

- [x] Add pure tests proving formation alternates by alliance, army/construction full scans run weekly or on source change, and front signatures skip unchanged downstream work.
- [x] Implement `StrategicCadencePolicy` with deterministic helpers and no Unity references.
- [x] Run the console harness and confirm the new tests pass.

### Task 2: Coordinator And Runtime De-Jitter

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`
- Modify: `src/WhiskeyRealism/Strategic/Construction/ConstructionRuntime.cs`
- Modify: `src/WhiskeyRealism/Strategic/CampaignMapRuntime.cs`

- [x] Cache campaign map rebuilds and only force full rebuild when missing, weekly, or dynamic map signature changes.
- [x] Keep front/fiscal/defense daily, but skip dependent expensive ledgers from stable source signatures.
- [x] Run formation alternating by alliance, forced by front, plan, or defense signature change.
- [x] Run army-area weekly or when plan/formation source changes.
- [x] Gate construction full scans weekly or on fiscal/front/formation/map source changes.

### Task 3: Vanilla AI Governor

**Files:**
- Modify: `src/WhiskeyRealism/Plugin.cs`
- Modify: `src/WhiskeyRealism/Patches/FastForwardAiCatchUpPatch.cs`
- Create: `src/WhiskeyRealism/Patches/CampaignAiUpdateGovernorPatch.cs`
- Modify: `docs/patch-catalog.md`

- [x] Add config for governor enable, max vanilla passes, and frame budget; default governor on because this slice is active stutter mitigation.
- [x] Disable catch-up extra passes when the governor is enabled so two schedulers do not fight.
- [x] Add Prefix wrapper preserving vanilla debug-off, initialize-and-return, debug map text fallback, `UpdateCompanyFoundationList()`, and `CBuilding.WorkDownConstructionWishes()`.
- [x] Log bounded `[Patch:CampaignAIGovernor]` samples with vanilla desired passes, executed passes, budget, and elapsed time.

### Task 4: Verification And Docs

**Files:**
- Modify: `docs/handoff.md`
- Modify: `MEMORY.md`
- Modify: `docs/patch-catalog.md`

- [x] Run `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`.
- [x] Run `./build.sh`.
- [x] Deploy `dist/WhiskeyRealism.dll` to the BepInEx plugin folder.
- [x] Verify deployed DLL timestamp/size and `sha256sum` match.
- [x] Update living docs with the new cadence and governor config boundary.
