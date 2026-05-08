# Tactical B8 Staged Withdrawal Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add staged withdrawal doctrine so a battle can stabilize, screen, bulk-withdraw, hold a rear guard, and only trigger full retreat after collapse evidence rather than snapping from pressure to global retreat.

**Architecture:** B8 is a default-off withdrawal runtime slice. It consumes B6 intent, B3 sector state, vanilla morale/routing evidence, and B2 order-friction evidence. B6b defines a `LocalFallbackPressure` model value but the shipped scorer does not emit it; B8 must either derive fallback pressure here or extend the pure scorer before executing withdrawal behavior. B8 owns fallback/withdrawal/retreat surfaces and does not alter artillery, reserve lists, or attack stance scoring.

**Tech Stack:** BepInEx 5.4.x, HarmonyX, C# netstandard2.1, console harness, vanilla decompile anchors, DLL deploy/hash verification.

---

## Evidence Review

The updated B6 spec correctly keeps withdrawal execution outside B6c. Grand Tactician exposes separate fallback and retreat surfaces: local fallback at `CheckLineFallbacks(...)`, unit retreat movement at `MicroAICheckForRetreats(...)`, group withdrawal through `BattleUnits.SetWithdrawal(...)`, and full battle retreat timer through `TimePanel.SetRetreatTimer(...)`. B8 must stage these deliberately.

Recheck these anchors before editing:

```bash
rg -n "private unsafe void CheckLineFallbacks\(|private unsafe void MicroAICheckForRetreats\(|public void SetWithdrawal\(|public void SetRetreatTimer\(|SetEndOfBattle\(|allianceretreating|onretreat|StopRegiment|SetWaypoint\\(" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
```

Current expected anchors:

- `AIBattle.CheckLineFallbacks(Regiment)` line 5118 handles local fallback.
- `AIBattle.MicroAICheckForRetreats(Regiment)` line 4817 handles retreat movement.
- `BattleUnits.SetWithdrawal(...)` line 92821 applies withdrawal to selected units.
- `Regiment.SetWithdrawal(...)` line 116116 sets regiment withdrawal state.
- `TimePanel.SetRetreatTimer(...)` line 221271 starts the full retreat timer.
- `AIBattle.CheckGlobalAIStrategy()` line 6314 can call `SetEndOfBattle` and `SetRetreatTimer`; B8 does not replace global macro retreat.

## Files

Create:

- `src/WhiskeyRealism/Tactical/TacticalRetreatDoctrine.cs`
- `src/WhiskeyRealism/Patches/BattleFallbackDoctrinePatch.cs`

Modify:

- `src/WhiskeyRealism/Plugin.cs`
- `src/WhiskeyRealism/Tactical/TacticalTelemetry.cs`
- `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- `tests/WhiskeyRealism.Tests/Program.cs`
- `docs/patch-catalog.md`
- `docs/handoff.md`
- `MEMORY.md`
- `docs/superpowers/plans/2026-05-05-tactical-brain-master-sequencing.md`

## Config

Add `Enable Tactical Withdrawal Doctrine` with C# default `false`.

## Model Contract

Add:

- `TacticalWithdrawalStage`: `Stabilize`, `Screen`, `BulkWithdraw`, `RearGuard`, `RearGuardWithdraw`, `FullRetreat`.
- `TacticalWithdrawalInput`: intent, playbook, B8-derived local fallback pressure count, battered-line count, routed-neighbor pressure, morale average, ammo average, casualty ratio, flank risk, reserve coverage flag, enemy contact age, order-friction state, W&L ownership-safe flag, path-risk flag, vanilla macro value, full-retreat timer active flag.
- `TacticalWithdrawalDecision`: stage, target unit keys, rear-guard unit keys, allows fallback write bool, allows withdrawal write bool, allows full-retreat timer bool, confidence, reason.

Rules:

- `Stabilize` is selected when morale and line integrity are recoverable.
- `Screen` is selected when B8-derived local fallback pressure exists but main body remains coherent.
- `BulkWithdraw` requires sustained pressure, battered line evidence, and a safe covered path or reserve screen.
- `RearGuard` holds a selected screen while main body begins withdrawal.
- `RearGuardWithdraw` releases the screen after main-body withdrawal state is observed.
- `FullRetreat` requires collapse evidence: severe routed-neighbor pressure, low morale, high casualty pressure, no stable reserve screen, and vanilla macro not already ending the battle.
- Stale order friction blocks fresh withdrawal writes unless collapse evidence requires `FullRetreat`.
- W&L ownership denial blocks player-subordinate withdrawal writes.
- `BUG-TAC-010` path-risk evidence blocks broad movement writes; `SetWithdrawal(...)` remains allowed only for selected unit lists with W&L ownership safety.

## Runtime Contract

`BattleFallbackDoctrinePatch` should use Postfix patches first:

- observe `AIBattle.CheckLineFallbacks(...)` for local fallback pressure evidence and B8 stage telemetry;
- observe `AIBattle.MicroAICheckForRetreats(...)` for retreat-state transitions;
- apply selected withdrawal through `BattleUnits.SetWithdrawal(enddate, unitlist, alliance, fromposition, removemonument:false)` only when `Enable Tactical Withdrawal Doctrine` is true and the decision allows withdrawal;
- call `TimePanel.SetRetreatTimer(alliance)` only for `FullRetreat` and only when vanilla has not already started the timer;
- never call artillery APIs or reserve-list mutation APIs;
- warn once and return on reflection failure.

Movement-path writes are not the first B8 runtime write. If the implementation needs `BattleUnits.SetWaypoint(...)` for a rear-guard screen, add it as a separate named task inside this plan and require path-risk checks, W&L ownership safety, use-order-delay preservation, and bounded smoke before enabling that branch.

## Tasks

- [ ] Recheck the fallback, withdrawal, and retreat anchors with the command above.
- [ ] Add `Enable Tactical Withdrawal Doctrine` to `Plugin.cs`.
- [ ] Create `TacticalRetreatDoctrine.cs` with the pure stage decision model and scorer.
- [ ] Create `BattleFallbackDoctrinePatch.cs` with Postfix observation and selected `SetWithdrawal(...)` / `SetRetreatTimer(...)` writes.
- [ ] Extend `TacticalTelemetry.cs` with `[TacticalWithdrawalDecision]`.
- [ ] Add explicit test-project compile entry for `TacticalRetreatDoctrine.cs`.
- [ ] Add tests named:
  - `TacticalWithdrawalStabilizesWhenMoraleRecoverable`
  - `TacticalWithdrawalScreensBeforeBulkWithdraw`
  - `TacticalWithdrawalRearGuardHoldsUntilMainBodyMoving`
  - `TacticalWithdrawalFullRetreatOnlyAfterCollapse`
  - `TacticalWithdrawalStaleOrderBlocksFreshWithdrawal`
  - `TacticalWithdrawalWlOwnershipBlocksPlayerSubordinateWrite`
  - `TacticalWithdrawalPathRiskBlocksWaypointBranch`
- [ ] Update `docs/patch-catalog.md`, `docs/handoff.md`, `MEMORY.md`, and the master sequencing plan after deploy/hash verification.

## Verification

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
git diff --check
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

## Smoke Expectations

Enable focused configs:

```text
Enable Tactical Observer = true
Enable Tactical Commander Intent Doctrine = true
Enable Tactical Local Reaction Doctrine = true
Enable Tactical Withdrawal Doctrine = true
```

Expected bounded marker:

```text
[TacticalWithdrawalDecision]
```

Smoke passes only if staged withdrawal does not trigger full retreat on first contact, W&L player-subordinate units are skipped, no repeated exceptions appear, no artillery or reserve-list writes occur, `SetRetreatTimer` fires only for `FullRetreat`, and any withdrawal write names a bounded selected unit list.

## Rollback And Runtime Gate

Disable `Enable Tactical Withdrawal Doctrine` to remove all B8 writes. If config rollback is insufficient, revert `BattleFallbackDoctrinePatch.cs`, `TacticalRetreatDoctrine.cs`, the config entry, and the telemetry/test entries for B8 only.
