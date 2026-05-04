# Perk Selection Steering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace random campaign-level AI perk picks with role-aware army and fleet perk scoring while preserving vanilla assignment constraints.

**Architecture:** Add a pure `PerkSelectionScorer` for tests and a Harmony Prefix `PerkSelectionPatch` that mirrors vanilla `AICampaign.CheckPerkSelection(int)` loops. The patch calls vanilla `Regiment.ChoosePerk(int)` and never edits `Regiment.perks` directly.

**Tech Stack:** C# netstandard2.1, HarmonyX, BepInEx, console test harness.

---

### Task 1: Pure Scorer

**Files:**
- Create: `src/WhiskeyRealism/Strategic/PerkSelectionScorer.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`

- [x] Add failing tests for army siege, army raid, Union fleet blockade, CSA fleet raiding, and unavailable candidates.
- [x] Run `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`; expected missing `PerkSelectionScorer` failure observed.
- [x] Implement `PerkSelectionScorer.SelectArmyPerk(...)` and `SelectFleetPerk(...)`.
- [x] Re-run the console harness; all tests passed.

### Task 2: Harmony Patch

**Files:**
- Create: `src/WhiskeyRealism/Patches/PerkSelectionPatch.cs`
- Modify: `docs/patch-catalog.md`
- Modify: `docs/handoff.md`

- [x] Add Prefix on `AICampaign.CheckPerkSelection(int)`.
- [x] Mirror vanilla army/fleet loops and W&L army skip.
- [x] Call `ChoosePerk(perkId)` only for a scorer-selected candidate.
- [x] Add bounded `[once:perks]` and `[Patch:Perks]` logging.
- [x] Update docs so #7 is no longer reserved.

### Task 3: Verification

**Files:**
- `dist/WhiskeyRealism.dll`
- GTCW BepInEx plugin folder

- [x] Run `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`.
- [x] Run `./build.sh`.
- [x] Copy `dist/WhiskeyRealism.dll` to the BepInEx plugin folder.
- [x] Run `stat` and `sha256sum` for dist and deployed DLL; hashes matched at `5852e56aaa613aa636767fb96d75546f3ef4ee8ed1b99c016aff2a16483ec29b`.
- [x] Commit and push one focused change: `2ccc743`.
