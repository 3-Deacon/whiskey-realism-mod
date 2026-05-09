# Tactical B7 Artillery Strongpoint Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Use B6 commander intent and playbooks to make artillery support more realistic: preserve guns under defensive intent, suppress strongpoints for a selected main effort, counterbattery when safe, and cancel bombardment when guns are exposed or intent no longer supports it.

**Architecture:** B7 is a default-off artillery runtime slice. It owns only the vanilla bombardment surface. It consumes B6a/B6b decisions and B3 sector evidence, then writes vanilla artillery combat behavior through the same fields and methods `AIBattle.CheckAIBombardment(...)` already uses.

**Tech Stack:** BepInEx 5.4.x, HarmonyX, C# netstandard2.1, console harness, vanilla decompile anchors, DLL deploy/hash verification.

**Status:** Superseded as an execution artifact by [`2026-05-08-tactical-b7-b8-wiring-implementation.md`](2026-05-08-tactical-b7-b8-wiring-implementation.md). The shipped B7 wiring lives in patch #48 and was first verified in DLL `328c74a43f356df4ecb52f38a0df1ec89267eae714fd207af406529b1adffef0`; the currently deployed #53 DLL is `b07bbd39eaaf664d81d5930b42d5dc268b64c43dc98244a17015c49e329ce88a` and still includes #48. Focused smoke confirmed `[once:b7-check-ai-bombardment]` and `[once:b7-counterbattery]`. Conditional `[once:b7-cancel-bombard]` has not fired yet.

---

## Evidence Review

The updated B6 spec correctly keeps artillery outside B6c. Vanilla artillery behavior is concentrated in `AIBattle.CheckAIBombardment(...)`: it requires artillery units, guns, ammo, no active path, no W&L denial, enemy in range, and then switches combat behavior to bombardment. B7 should bias that surface, not movement or fallback code.

Recheck this anchor before editing:

```bash
rg -n "private void CheckAIBombardment\(|ChangeCombatBehavior\\(.*9\\)|ChangeCombatBehavior\\(.*7\\)|bombardposition|bombardrange|bombardstarttime|lastfeudactiontime|combatbehaviorordered|ammo\\[0\\]" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
```

Current expected anchor:

- `AIBattle.CheckAIBombardment(Regiment)` line 3869 is the artillery surface.
- Vanilla starts bombardment with `bunits.ChangeCombatBehavior(gameObject, 9)`, sets `lastfeudactiontime`, `bombardposition`, and `bombardrange`.
- Vanilla cancels bombardment with `bunits.ChangeCombatBehavior(gameObject, 7)` and `bombardstarttime = 0`.

## Files

Create:

- `src/WhiskeyRealism/Tactical/TacticalArtilleryDoctrine.cs`
- `src/WhiskeyRealism/Patches/BattleBombardmentPatch.cs`

Modify:

- `src/WhiskeyRealism/Plugin.cs`
- `src/WhiskeyRealism/Tactical/TacticalTelemetry.cs`
- `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- `tests/WhiskeyRealism.Tests/Program.cs`
- `docs/patch-catalog.md`
- `docs/handoff.md`
- `MEMORY.md`
- `docs/superpowers/plans/archive/2026-05-05-tactical-brain-master-sequencing.md`

## Config

Add `Enable Tactical Artillery Doctrine` with C# default `false`.

## Model Contract

Add:

- `TacticalArtilleryDecisionKind`: `PreserveFire`, `SuppressStrongpoint`, `CounterBattery`, `CancelBombard`, `DefensiveFallback`.
- `TacticalArtilleryInput`: B6 intent, playbook, sector role, artillery ammo ratio, gun count, routed flag, path active flag, current combat behavior, target visible flag, target in range flag, target strongpoint flag, enemy artillery visible flag, flank risk, W&L ownership-safe flag.
- `TacticalArtilleryDecision`: decision kind, target position, bombard range, allows runtime write bool, confidence, reason.

Rules:

- `SuppressStrongpoint` requires `Attack` or `AllOutAttack`, selected main effort, target strongpoint evidence, ammo ratio above `0.20`, no active path, and W&L ownership safety.
- `CounterBattery` requires visible enemy artillery, ammo ratio above `0.20`, no active path, and sector confidence at least `0.50`.
- `PreserveFire` is preferred for `ProbeIntent`, `Hold`, and low-confidence sectors.
- `CancelBombard` fires when guns are exposed, target vanished, ammo is low, or intent changed away from the bombardment purpose.
- `DefensiveFallback` is telemetry only for B7; B8 owns movement.

## Runtime Contract

`BattleBombardmentPatch` should Postfix `AIBattle.CheckAIBombardment(...)`:

- read current vanilla artillery state after vanilla runs;
- if config is off, return;
- if the unit is not artillery, has no guns, is routed, has active path movement, lacks ammo, or W&L ownership is unsafe, return;
- compute `TacticalArtilleryDecision`;
- for `SuppressStrongpoint` or `CounterBattery`, call `bunits.ChangeCombatBehavior(unit.gameObject, 9)`, set `lastfeudactiontime`, `bombardposition`, and `bombardrange` using the same vanilla field shape;
- for `CancelBombard`, call `bunits.ChangeCombatBehavior(unit.gameObject, 7)` and set `bombardstarttime = 0f`;
- warn once and return on reflection failure.

## Tasks

- [ ] Recheck the vanilla bombardment anchor with the command above.
- [ ] Add `Enable Tactical Artillery Doctrine` to `Plugin.cs`.
- [ ] Create `TacticalArtilleryDoctrine.cs` with the pure decision model and scorer.
- [ ] Create `BattleBombardmentPatch.cs` as a Postfix on `AIBattle.CheckAIBombardment(...)`.
- [ ] Extend `TacticalTelemetry.cs` with `[TacticalArtilleryDecision]`.
- [ ] Add explicit test-project compile entry for `TacticalArtilleryDoctrine.cs`.
- [ ] Add tests named:
  - `TacticalArtillerySuppressesStrongpointForMainEffort`
  - `TacticalArtilleryPreservesFireDuringProbeIntent`
  - `TacticalArtilleryCancelsBombardWhenTargetVanishes`
  - `TacticalArtilleryCounterBatteryRequiresVisibleEnemyGuns`
  - `TacticalArtilleryBlocksRuntimeWriteForWlPlayerSubordinate`
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
Enable Tactical Artillery Doctrine = true
```

Expected bounded marker:

```text
[TacticalArtilleryDecision]
```

Smoke passes only if artillery decisions remain bounded, no repeated exceptions appear, no movement or retreat APIs are called, W&L player-subordinate units are skipped, and bombard/cancel writes match vanilla field shape.

## Rollback And Runtime Gate

Disable `Enable Tactical Artillery Doctrine` to remove all B7 writes. If config rollback is insufficient, revert `BattleBombardmentPatch.cs`, `TacticalArtilleryDoctrine.cs`, the config entry, and the telemetry/test entries for B7 only.
