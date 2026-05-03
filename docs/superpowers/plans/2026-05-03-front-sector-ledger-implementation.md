# Front Sector Ledger Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a campaign-map front ledger that keeps strategic transfers from hollowing out critical sectors unless the CIC has made an explicit concession.

**Architecture:** Implement pure strategic scoring types first, with a small console test harness. Then add a runtime adapter that snapshots vanilla `AIFaction`/unit data by reflection and exposes the ledger to `TransferOfUnitsPatch` without taking over vanilla movement/pathing.

**Tech Stack:** C# `netstandard2.1`, BepInEx 5/HarmonyX, Unity `Vector3`, no external test packages.

---

### Task 1: Pure Ledger

**Files:**
- Create: `src/WhiskeyRealism/Strategic/FrontSectorLedger.cs`
- Create: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Create: `tests/WhiskeyRealism.Tests/Program.cs`

- [x] **Step 1: Write failing console tests**

Add tests that assert:

- a critical sector below its minimum hold ratio is `Hold`,
- a non-critical understrength sector can be `EconomyOfForce`,
- transfers from a `Hold` source are blocked,
- transfers from `EconomyOfForce` are allowed but marked as concession.

Run: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
Expected: compile failure because `FrontSectorLedger` does not exist.

- [x] **Step 2: Implement pure ledger**

Create `FrontSectorLedger`, `FrontSector`, `FrontPosture`, `ArmyRole`, `TransferBudgetDecision`, and supporting scoring logic without Unity or Harmony dependencies.

- [x] **Step 3: Run tests**

Run: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
Expected: all tests pass.

### Task 2: Runtime Snapshot

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`
- Create: `src/WhiskeyRealism/Strategic/FrontSectorRuntime.cs`

- [x] **Step 1: Add runtime snapshot reader**

Read vanilla `AICampaign.aifaction`, `positiondeficit`, `positionsurplus`, `ownunits`, and `enemyunits` via reflection. Bucket positions with `ObjectiveAdapter` theater buckets where possible and fall back to `Theater.Unknown`.

- [x] **Step 2: Store latest ledger**

Expose `StrategicCoordinator.Fronts[alliance]` and update it once per monthly tick. Log a bounded `[FrontLedger]` summary only on posture changes or when verbose logging is on.

### Task 3: Transfer Budget Integration

**Files:**
- Modify: `src/WhiskeyRealism/Patches/TransferOfUnitsPatch.cs`

- [x] **Step 1: Consult ledger before steering**

Before replacing `positiondeficit`, ask the latest ledger whether moving strength from the surplus sector to the plan-target sector is allowed.

- [x] **Step 2: Bounded logging**

Log `[Patch:TransferBudget]` only when a transfer is blocked or explicitly allowed as a concession. Keep existing `[Patch:Transfer]` queue log unchanged.

### Task 4: Verification

**Files:**
- Modify: `docs/handoff.md`
- Modify: `docs/patch-catalog.md` if a new Harmony patch is added; otherwise do not change catalog ordinals.

- [x] **Step 1: Run tests**

Run: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`

- [x] **Step 2: Build**

Run: `./build.sh`
Expected: 0 warnings / 0 errors.

- [x] **Step 3: Deploy and verify hash**

Copy `dist/WhiskeyRealism.dll` to the GTCW BepInEx plugins folder, then compare SHA-256 hashes.

- [x] **Step 4: Tail logs**

After game restart, tail `BepInEx/LogOutput.log` and verify first-fire plus bounded front/transfer budget logging.

Result: deployed DLL hash matched `dist/WhiskeyRealism.dll` (`31766edfc8e7b451abc340161c2048bb5f5c2f09170fe4a6d4817c5e2eddd7e3`). Existing log tail was from the prior runtime; `[FrontLedger]` / `[Patch:TransferBudget]` smoke still requires a game restart and monthly campaign tick.
