# Tactical Brain Master Sequencing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert the Slice B tactical-brain umbrella spec into a sequential, verifiable implementation track that starts with observer telemetry and then ships bounded behavior slices.

**Architecture:** This is a master sequencing plan, not a monolithic behavior plan. Each B-slice below must get its own detailed implementation plan under `docs/superpowers/plans/` before source code changes begin, and each behavior slice depends on runtime proof from earlier slices. Runtime extraction and Harmony patches read vanilla battle state, log bounded evidence, and steer only the narrow vanilla method they own.

**Tech Stack:** BepInEx 5.4.x x64, HarmonyX, C# netstandard2.1, Unity 2021 Mono, console harness in `tests/WhiskeyRealism.Tests`, vanilla anchors from `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

---

## Current Inputs

Read these before creating any B-slice plan:

- `AGENTS.md`
- `docs/handoff.md`
- `docs/patch-catalog.md`
- `docs/superpowers/AGENTS.md`
- `docs/superpowers/specs/2026-05-05-tactical-brain-design.md`
- `docs/superpowers/specs/2026-05-05-tactical-brain-vanilla-verification.md`
- `src/WhiskeyRealism/Patches/AGENTS.md`
- `src/WhiskeyRealism/Strategic/AGENTS.md`
- `tests/WhiskeyRealism.Tests/AGENTS.md`

Implementation boundary:

- Do not implement the whole tactical brain from this master plan.
- Do not ship any behavior patch before `B0 Tactical Observer` is built, deployed, and smoke-reviewed.
- Do not merge the weapons/ammunition adjunct into this master tactical-brain track. The adjunct needs its own observer-first plan after core B0 proves battle telemetry shape.
- Keep tactical state runtime-only unless a later battle-resume spec is written. Do not write tactical state to `whiskeyrealism.json`.
- Existing dirty work in unrelated campaign systems must not be reverted while executing any B-slice.

## Verified Vanilla Anchors To Recheck In Every Slice

The following anchors were verified against `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` on 2026-05-05. Each slice plan must rerun the exact command below and update line numbers if the decompile changed.

Run:

```bash
rg -n "private void CheckGlobalAIStrategy\(|private void AdjustGroupAIStance\(|private void MicroAICheckForCharges\(|private void CheckForFeudGroupActions\(|private unsafe void CheckUseOfReserves\(|private void LinkReservesToLineGroup\(|private void AssignReserves\(|private void CheckAIBombardment\(|private unsafe void CheckLineFallbacks\(|private unsafe void MicroAICheckForRetreats\(|private static bool PerformAIActionDLCWL\(|public void ProcessOrders\(|private void AddOrderCourierline\(|GetLastTransmittedPathPos\(|public void SetOrderStatus\(|public void SetWaypoint\(" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
```

Expected current anchors:

| Surface | Current anchor | Ownership / implementation note |
|---|---:|---|
| Battle macro stance | `AIBattle.CheckGlobalAIStrategy()` line 6314 | Owns battle-level `macroai` values `-1` dynamic, `0` assault, `1` attack, `2` defend, `3` retreat. |
| Group stance | `AIBattle.AdjustGroupAIStance()` line 4221 | Owns group-level `ai_stance`; `ai_stance == 4` is charge, not battle macro assault. |
| Charge movement | `AIBattle.MicroAICheckForCharges(Regiment,int)` line 4905 | Initiates and cancels charge movement and writes `lastfeudactiontime`; B1 must not break cancellation side effects. |
| W&L action gate | `AIBattle.PerformAIActionDLCWL(Regiment,Regiment)` line 5101 | Existing W&L command-permission helper. It is absent from the charge method itself. |
| Feud auto-advance | `AIBattle.CheckForFeudGroupActions()` line 4931 | Moves feud groups toward closest enemy using delayed movement; no `PerformAIActionDLCWL` call in this method. |
| Local fallback | `AIBattle.CheckLineFallbacks(Regiment)` line 5118 | Local fallback surface with W&L gate already present. |
| Local retreat | `AIBattle.MicroAICheckForRetreats(Regiment)` line 4817 | Macro-retreat movement surface; do not reuse as line-relief behavior. |
| Reserve use | `AIBattle.CheckUseOfReserves(Regiment)` line 6062 | Emergency reserve use and reserve commitment logic. |
| Reserve linkage | `AIBattle.LinkReservesToLineGroup()` line 6642 | Mutates reserve/line group relationships; behavior patches must be high-risk gated. |
| Reserve assignment | `AIBattle.AssignReserves()` line 7017 | Mutates objective-chain reserve state. |
| Artillery bombardment | `AIBattle.CheckAIBombardment(Regiment)` line 3869 | Orders bombardment behavior for artillery under vanilla gates. |
| Battle movement order | `BattleUnits.SetWaypoint(...)` line 91225 | Safer deliberate-order surface because it honors `useorderdelay` and vanilla movement guards. |
| Courier/order delivery | `Regiment.AddOrderCourierline(...)` line 125009 | Creates bugle/courier order delivery paths. |
| Order processing | `Regiment.ProcessOrders()` line 125173 | Applies delivered parent/subordinate orders. |
| Order status | `Regiment.SetOrderStatus(...)` line 125484 | Existing order-state transition surface. |
| Transmitted position | `Regiment.GetLastTransmittedPathPos(bool ignoreorderdelay=false)` line 127552 | Distinguishes delivered/transmitted path from intended/future path under order delay. |

Before patching `CheckGlobalAIStrategy`, also run:

```bash
rg -n "sideinformation\\[.*\\]\\.macroai\\s*=|GameVars\\.aistrategy\\s*=" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
```

Expected current interpretation:

- `GameVars.aistrategy >= 0` is a debug/UI override path. B4 must not overwrite it.
- `bunits.sideinformation[side].macroai >= 0` can be save-state restore or an intentional vanilla writer. B4 must detect this and skip its Postfix bias when vanilla short-circuited.

## File Ownership Map

New tactical files should live under `src/WhiskeyRealism/Tactical/`. Patch files stay under `src/WhiskeyRealism/Patches/`. Pure tactical scoring files must be added to `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` with explicit `<Compile Include>` entries.

Core model files:

- Create: `src/WhiskeyRealism/Tactical/TacticalBattleContext.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalCommanderProfile.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalCommandLedger.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalOrderFriction.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalContactLedger.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalOddsDoctrine.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalSectorLedger.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalBattlePlan.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalDoctrineScorer.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalRetreatDoctrine.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalTelemetry.cs`
- Create only if B0 proves the need: `src/WhiskeyRealism/Tactical/TacticalRuntime.cs`

Patch files by slice:

- B0: `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs`
- B1: `src/WhiskeyRealism/Patches/BattleChargeGatePatch.cs`, `src/WhiskeyRealism/Patches/BattleFeudActionGatePatch.cs`
- B4: `src/WhiskeyRealism/Patches/BattleMacroStrategyPatch.cs`
- B5: `src/WhiskeyRealism/Patches/BattleGroupStancePatch.cs`
- B6: `src/WhiskeyRealism/Patches/BattleReserveDoctrinePatch.cs`
- B7: `src/WhiskeyRealism/Patches/BattleBombardmentPatch.cs`
- B8: `src/WhiskeyRealism/Patches/BattleFallbackDoctrinePatch.cs`

Plugin config entries should be added only when a slice needs them:

- B0: `Enable Tactical Observer`, `Tactical Observer Verbose Logging`
- B1: `Enable W&L Tactical Charge Guard`
- B2: `Enable Tactical Order Friction Doctrine`
- B3: `Enable Tactical Odds Doctrine`
- B4: `Enable Tactical Macro Stance Scorer`
- B5: `Enable Tactical Group Sector Stance`
- B6: `Enable Tactical Reserve Relief Doctrine`
- B7: `Enable Tactical Artillery Doctrine`
- B8: `Enable Tactical Withdrawal Doctrine`

Default behavior:

- Observer config may default `true` while the slice is being smoke-tested, then default `false` if log volume is not proven safe.
- All behavior configs must default `false` for their first build unless the slice plan explicitly explains why default-on is safe.
- Existing BepInEx config files override C# defaults after first plugin load; every behavior plan must include manual config flip instructions for smoke.

## Sequential Slice Gates

Every B-slice must pass these common checks before moving to the next slice:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
git diff --check
```

For DLL-affecting changes, also deploy and hash-verify:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

The two SHA-256 hashes must match before runtime smoke.

If `cp` fails with `Invalid argument`, the game is running and the DLL is locked. Stop and tell the user to close the game before redeploying.

Runtime log check command:

```bash
rg -n "once:tactical|Tactical|BattleCharge|BattleFeud|Exception|TargetInvocationException|Harmony|ERROR|WARN" "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

## Task 1: B0 Tactical Observer Plan

**Files:**

- Create: `docs/superpowers/plans/2026-05-05-tactical-b0-observer.md`
- Future create: `src/WhiskeyRealism/Tactical/TacticalBattleContext.cs`
- Future create: `src/WhiskeyRealism/Tactical/TacticalTelemetry.cs`
- Future create: `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs`
- Future modify: `src/WhiskeyRealism/Plugin.cs`
- Future modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Future modify: `tests/WhiskeyRealism.Tests/Program.cs`
- Future modify after ship: `docs/patch-catalog.md`, `docs/handoff.md`, `MEMORY.md`

- [ ] **Step 1: Create the B0 plan**

Create `docs/superpowers/plans/2026-05-05-tactical-b0-observer.md` with this exact scope:

- read-only observer patches only;
- no `macroai`, `ai_stance`, reserve-list, movement-order, artillery, charge, fallback, or retreat writes;
- bounded logs for `macroai`, `ai_stance`, W&L feud/charge path, objective-chain sector projection, order-delay/transmitted-path state, visible contact, reserves, artillery, reinforcement fields, and fallback/retreat triggers;
- extraction failures warn once and return to vanilla.

- [ ] **Step 2: Pin B0 patch surfaces**

The B0 plan must patch or observe no more than these surfaces:

- `AIBattle.CheckGlobalAIStrategy()` Postfix for macro summary.
- `AIBattle.AdjustGroupAIStance()` Postfix for group stance summary.
- `AIBattle.MicroAICheckForCharges(...)` Prefix/Postfix observer only; record whether vanilla initiated/cancelled charge by comparing state, never suppress.
- `AIBattle.CheckForFeudGroupActions()` Prefix/Postfix observer only; record movement/order state changes, never suppress.
- `AIBattle.CheckUseOfReserves(...)`, `LinkReservesToLineGroup()`, and `AssignReserves()` Postfix observers only.
- `AIBattle.CheckAIBombardment(...)` Postfix observer only.
- `AIBattle.CheckLineFallbacks(...)` and `MicroAICheckForRetreats(...)` Postfix observers only.

- [ ] **Step 3: Define B0 runtime smoke**

B0 smoke passes only if a fresh W&L land battle emits bounded lines matching these families without repeated exceptions:

```text
[once:tactical-observer] TacticalObserverPatch wired
[TacticalMacro]
[TacticalGroup]
[TacticalFeud]
[TacticalCharge]
[TacticalSector]
[TacticalOrder]
[TacticalReserve]
[TacticalArtillery]
[TacticalFallback]
```

- [ ] **Step 4: Commit B0 plan**

```bash
git add docs/superpowers/plans/2026-05-05-tactical-b0-observer.md
git commit -m "docs: plan tactical b0 observer"
```

Expected: commit includes only the B0 plan unless the user explicitly asks to batch docs.

## Task 2: B0 Tactical Observer Implementation

**Files:**

- Modify: `src/WhiskeyRealism/Plugin.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalBattleContext.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalTelemetry.cs`
- Create: `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`
- Modify after deploy/smoke: `docs/patch-catalog.md`, `docs/handoff.md`, `MEMORY.md`

- [ ] **Step 1: Add pure telemetry signature tests**

Add tests that verify telemetry summaries are stable, bounded, and non-empty:

- null input returns `side=<unknown> signature=empty`;
- macro `-1` formats as `dynamic`;
- macro `0..3` formats as `assault`, `attack`, `defend`, `retreat`;
- sector summary includes source confidence `objective-chain` or `angle-slice`;
- log signature changes when stance, sector mission, or reserve counts change.

- [ ] **Step 2: Implement pure model helpers**

Create `TacticalBattleContext` and `TacticalTelemetry` with no Unity dependencies except simple primitive/vector values if the tests require them. Keep extraction from live `AIBattle` objects inside the patch, not in pure tests.

- [ ] **Step 3: Wire observer patch**

Create one `TacticalObserverPatch` class with nested Harmony patches per method. Follow the `ConstructionObserverPatch` and #25 patterns:

- `Plugin.Instance == null` or disabled config returns immediately;
- `try/catch` inside every patch;
- `OnceLog.Info("tactical-observer", "TacticalObserverPatch wired")`;
- warnings use `OnceLog.Warning(...)`;
- log volume is signature-gated or cooldown-gated.

- [ ] **Step 4: Verify B0**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
git diff --check
```

Deploy and hash-verify the DLL, then smoke a W&L land battle.

- [ ] **Step 5: Close out B0 docs**

Update:

- `docs/patch-catalog.md` with an unnumbered or next-numbered observer entry, depending on the chosen catalog policy at implementation time;
- `docs/handoff.md` with deployed hash and smoke result;
- `MEMORY.md` only if the smoke result changes durable Slice B state.

## Task 3: B1 W&L Feud And Charge Guard Plan

**Files:**

- Create: `docs/superpowers/plans/2026-05-05-tactical-b1-wl-feud-charge-guard.md`
- Future create: `src/WhiskeyRealism/Patches/BattleChargeGatePatch.cs`
- Future create: `src/WhiskeyRealism/Patches/BattleFeudActionGatePatch.cs`
- Future create or modify: `src/WhiskeyRealism/Tactical/TacticalDoctrineScorer.cs`

- [ ] **Step 1: Require B0 evidence**

The B1 plan must begin with pasted B0 smoke evidence proving:

- `MicroAICheckForCharges(...)` runs in W&L land battles;
- `CheckForFeudGroupActions()` runs in W&L land battles;
- charge initiation/cancellation can be observed without exceptions;
- feud auto-advance is visible as movement/order state change or explicitly not reproduced.

- [ ] **Step 2: Re-run W&L guard grep**

Run:

```bash
rg -n "PerformAIActionDLCWL" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
```

Expected current result includes many tactical call sites and no call inside `MicroAICheckForCharges(...)` or `CheckForFeudGroupActions()`.

- [ ] **Step 3: Define B1 narrow behavior**

B1 may only add the W&L action guard around the two missing surfaces. It must preserve:

- charge cancellation branch;
- `lastfeudactiontime` updates where vanilla would update them;
- vanilla movement/order delay calls when the W&L gate permits action;
- non-W&L AI-vs-AI behavior unless config says otherwise.

- [ ] **Step 4: Commit B1 plan**

```bash
git add docs/superpowers/plans/2026-05-05-tactical-b1-wl-feud-charge-guard.md
git commit -m "docs: plan tactical b1 wl feud charge guard"
```

## Task 4: B1 W&L Feud And Charge Guard Implementation

**Files:**

- Modify: `src/WhiskeyRealism/Plugin.cs`
- Create: `src/WhiskeyRealism/Patches/BattleChargeGatePatch.cs`
- Create: `src/WhiskeyRealism/Patches/BattleFeudActionGatePatch.cs`
- Modify or create: `src/WhiskeyRealism/Tactical/TacticalDoctrineScorer.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`
- Modify after deploy/smoke: `docs/patch-catalog.md`, `docs/handoff.md`, `MEMORY.md`

- [ ] **Step 1: Add pure tests**

Tests must cover:

- W&L gate denies command means charge initiation is suppressed;
- W&L gate denies command does not strand a currently charging unit if vanilla cancellation is required;
- W&L gate allows command means vanilla behavior is left alone;
- non-W&L side returns vanilla behavior.

- [ ] **Step 2: Implement guard patches**

Prefer the narrowest implementation:

- Prefix may suppress only the action branch that is missing `PerformAIActionDLCWL`.
- If a full Prefix replacement is unavoidable, the plan must quote the current vanilla body and explicitly list every preserved side effect.
- Use `OnceLog` for first-fire and failures.

- [ ] **Step 3: Verify B1**

Run tests/build/diff check, deploy, hash-verify, then smoke a W&L subordinate battle.

Expected log evidence:

```text
[once:tactical-charge-guard]
[once:tactical-feud-guard]
[TacticalChargeGuard]
[TacticalFeudGuard]
```

No repeated warnings/errors.

## Task 5: B2 Command Hierarchy And Order Friction Plan

**Files:**

- Create: `docs/superpowers/plans/2026-05-05-tactical-b2-command-order-friction.md`
- Future create: `src/WhiskeyRealism/Tactical/TacticalCommandLedger.cs`
- Future create: `src/WhiskeyRealism/Tactical/TacticalOrderFriction.cs`

- [ ] **Step 1: Require B0/B1 evidence**

B2 starts only after B0 shipped and B1 either shipped or was explicitly deferred by the user. The plan must cite B0 `[TacticalOrder]` output showing transmitted-path and order-delay fields are readable in runtime.

- [ ] **Step 2: Re-read order-delay anchors**

Read current bodies for:

- `Regiment.AddOrderCourierline(...)` line 125009;
- `Regiment.ProcessOrders()` line 125173;
- `Regiment.GetLastTransmittedPathPos(...)` line 127552;
- `Regiment.GetLastTransmittedPath(...)` line 127591;
- `Regiment.SetOrderStatus(...)` line 125484;
- `BattleUnits.SetWaypoint(...)` line 91225.

- [ ] **Step 3: Define B2 doctrine**

B2 creates pure read-only doctrine state:

- command tier;
- parent/child relationship;
- order source;
- intended position;
- transmitted position;
- friction state: `Immediate`, `Bugle`, `Courier`, `Pending`, `Delivered`, `Stale`, `Failed`;
- local initiative allowance.

B2 does not issue movement orders. It only gives later slices a safe interpretation of order state.

## Task 6: B2 Command Hierarchy And Order Friction Implementation

**Files:**

- Create: `src/WhiskeyRealism/Tactical/TacticalCommanderProfile.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalCommandLedger.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalOrderFriction.cs`
- Modify: `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add pure tests**

Tests must cover:

- army/corps intent does not retask all regiments directly;
- division mission can map to brigade actions;
- order outside bugle range is delayed;
- delivered transmitted path differs from intended path while delayed;
- stale delayed order downgrades when contact changed materially;
- high initiative reduces delay pressure without making orders instant.

- [ ] **Step 2: Implement pure ledgers**

Keep Unity object reads in runtime extraction. Pure ledgers should use DTO inputs so the console harness can test them.

- [ ] **Step 3: Extend B0 telemetry**

Add signature-gated `[TacticalCommand]` and `[TacticalOrder]` lines from the B2 ledger. No behavior change.

## Task 7: B3 Tactical Odds Doctrine Plan

**Files:**

- Create: `docs/superpowers/plans/2026-05-05-tactical-b3-odds-doctrine.md`
- Future create: `src/WhiskeyRealism/Tactical/TacticalContactLedger.cs`
- Future create: `src/WhiskeyRealism/Tactical/TacticalSectorLedger.cs`
- Future create: `src/WhiskeyRealism/Tactical/TacticalOddsDoctrine.cs`

- [ ] **Step 1: Require B0 sector/contact evidence**

B3 starts only after B0 logs prove whether sector projection can use objective-chain data or must fall back to angle slices.

- [ ] **Step 2: Re-read contact and force-balance anchors**

Read:

- `BattleUnits.sideinformation` fields around 78524-78568, 83504-83572, 84614-84832;
- `AIBattle.GetGroupStrength(...)` line 6025;
- `Regiment.UpdateUnitRangeFast(...)` line 122545;
- `FogOfWar` class line 100570 and related visibility methods;
- `Regiment.GetArrivalTimeToBF(...)` line 138862.

- [ ] **Step 3: Define B3 scoring outputs**

B3 outputs:

- `currentGlobalOdds`;
- `projectedGlobalOdds`;
- `localSectorOdds`;
- `decisivePoint`;
- `economyOfForceSectors`;
- `inferiorForcePosture`;
- confidence ranges for confirmed/recent/inferred enemy strength.

## Task 8: B3 Tactical Odds Doctrine Implementation

**Files:**

- Create: `src/WhiskeyRealism/Tactical/TacticalContactLedger.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalSectorLedger.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalOddsDoctrine.cs`
- Modify: `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add pure tests**

Tests must cover:

- no contact chooses probe/hold pressure, not assault pressure;
- stale contact ages out;
- strongpoint confidence is not permanent after one volley;
- global superiority does not imply all-sector attack;
- local superiority picks one decisive sector;
- 4,000 versus 12,000 with no relief chooses withdrawal pressure;
- 4,000 versus 12,000 with strong terrain and near relief chooses delay pressure.

- [ ] **Step 2: Implement B3 pure scoring**

Implement deterministic scoring with clamped non-finite inputs. No Harmony behavior patch in B3.

- [ ] **Step 3: Extend telemetry**

Add `[TacticalOdds]` and `[TacticalSector]` signature summaries. No behavior change.

## Task 9: B4 Macro Stance Scorer Plan

**Files:**

- Create: `docs/superpowers/plans/2026-05-05-tactical-b4-macro-stance-scorer.md`
- Future create: `src/WhiskeyRealism/Tactical/TacticalBattlePlan.cs`
- Future create: `src/WhiskeyRealism/Tactical/TacticalDoctrineScorer.cs`
- Future create: `src/WhiskeyRealism/Patches/BattleMacroStrategyPatch.cs`

- [ ] **Step 1: Require B3 evidence**

B4 starts only after B3 logs show stable odds and sector signatures in at least one W&L land battle.

- [ ] **Step 2: Re-read macro method body**

Read all of `AIBattle.CheckGlobalAIStrategy()` and record:

- early return when `GameVars.aistrategy >= 0`;
- early return when `bunits.sideinformation[side].macroai >= 0`;
- retreat-timer calls;
- force-balance and reinforcement checks;
- commander initiative use.

- [ ] **Step 3: Define B4 behavior limit**

B4 may bias or clamp `macroai` only after vanilla ran the normal dynamic path. It must not override debug/UI macro selection or save-state restore short-circuit.

## Task 10: B4 Macro Stance Scorer Implementation

**Files:**

- Modify: `src/WhiskeyRealism/Plugin.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalBattlePlan.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalDoctrineScorer.cs`
- Create: `src/WhiskeyRealism/Patches/BattleMacroStrategyPatch.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`
- Modify after deploy/smoke: `docs/patch-catalog.md`, `docs/handoff.md`, `MEMORY.md`

- [ ] **Step 1: Add pure tests**

Tests must cover:

- `macroai = -1` is treated as dynamic, not attack;
- no-contact plan remains dynamic or defend;
- strong terrain plus near relief avoids immediate retreat;
- bad odds with no relief selects retreat pressure;
- commander aggression shifts attack threshold but cannot force impossible assault;
- debug/UI override makes scorer return "skip";
- save-state macro restore makes scorer return "skip".

- [ ] **Step 2: Implement Postfix bias**

Patch `AIBattle.CheckGlobalAIStrategy()` with a Postfix that:

- detects and skips short-circuit override conditions;
- reads B3 doctrine state;
- writes macro only when config enabled and confidence threshold passes;
- logs `[TacticalMacroDecision]` only on signature change.

- [ ] **Step 3: Runtime smoke**

Smoke meeting engagement and outnumbered defensive battle. Pass requires no instant all-army assault at no contact and no repeated retreat flip-flop.

## Task 11: B5 Group Sector Stance Plan

**Files:**

- Create: `docs/superpowers/plans/2026-05-05-tactical-b5-group-sector-stance.md`
- Future create or modify: `src/WhiskeyRealism/Tactical/TacticalSectorLedger.cs`
- Future create: `src/WhiskeyRealism/Patches/BattleGroupStancePatch.cs`

- [ ] **Step 1: Require B2/B3/B4 evidence**

B5 starts only after B2 order state and B3 sector/odds telemetry are stable, and B4 macro scorer is either shipped or explicitly deferred.

- [ ] **Step 2: Re-read group stance method**

Read all of `AIBattle.AdjustGroupAIStance()` and record:

- existing W&L `PerformAIActionDLCWL` call;
- stance ladder assignments;
- timing gates;
- how macro stance affects group stance;
- OOB symbol and screening behavior.

- [ ] **Step 3: Define B5 behavior limit**

B5 steers group stance by sector mission only. It does not move reserves, issue fallback paths, or control artillery shell behavior.

## Task 12: B5 Group Sector Stance Implementation

**Files:**

- Modify: `src/WhiskeyRealism/Plugin.cs`
- Modify: `src/WhiskeyRealism/Tactical/TacticalSectorLedger.cs`
- Modify: `src/WhiskeyRealism/Tactical/TacticalDoctrineScorer.cs`
- Create: `src/WhiskeyRealism/Patches/BattleGroupStancePatch.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`
- Modify after deploy/smoke: `docs/patch-catalog.md`, `docs/handoff.md`, `MEMORY.md`

- [ ] **Step 1: Add pure tests**

Tests must cover:

- chosen decisive sector gets attack pressure;
- adjacent sector gets fix/support pressure;
- strongpoint sector gets hold/bombard pressure;
- flank-risk sector gets refuse/hold pressure;
- sector confidence too low leaves vanilla stance unchanged;
- W&L gate denies player-subordinate control.

- [ ] **Step 2: Implement group stance Postfix**

Patch `AdjustGroupAIStance()` with a Postfix that only changes stance when:

- config enabled;
- B3 sector confidence is high enough;
- B2 order-friction state permits a new local intent;
- W&L gate permits AI control;
- new stance is not a charge unless B1/B5 charge permission says it is safe.

## Task 13: B6 Reserve Relief And Flank Doctrine Plan

**Files:**

- Create: `docs/superpowers/plans/2026-05-05-tactical-b6-reserve-relief-flank.md`
- Future create: `src/WhiskeyRealism/Patches/BattleReserveDoctrinePatch.cs`

- [ ] **Step 1: Require B5 evidence**

B6 starts only after B5 smoke proves sector missions are stable and not causing all-sector attacks.

- [ ] **Step 2: Re-read reserve methods**

Read:

- `AIBattle.CheckUseOfReserves(...)`;
- `AIBattle.LinkReservesToLineGroup()`;
- `AIBattle.AssignReserves()`;
- `FindExchangeUnitForUnit(...)` near line 5088.

- [ ] **Step 3: Define B6 behavior limit**

B6 may steer reserve role and relief timing. It must not directly mutate `objectivechain.reservegroups` unless the slice plan quotes the exact vanilla mutation pattern and defines rollback.

## Task 14: B6 Reserve Relief And Flank Doctrine Implementation

**Files:**

- Modify: `src/WhiskeyRealism/Plugin.cs`
- Modify: `src/WhiskeyRealism/Tactical/TacticalDoctrineScorer.cs`
- Create: `src/WhiskeyRealism/Patches/BattleReserveDoctrinePatch.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`
- Modify after deploy/smoke: `docs/patch-catalog.md`, `docs/handoff.md`, `MEMORY.md`

- [ ] **Step 1: Add pure tests**

Tests must cover:

- battered frontline with good reserve selects line relief;
- reserve too far or unsafe does not get pulled;
- last reserve is held when flank security requires it;
- reserve is not stacked onto an already committed target;
- local flank exposure prefers flank guard over exploitation.

- [ ] **Step 2: Implement narrow reserve steering**

Start with scorer bias and observer comparison. Direct list mutation is allowed only after runtime proof and only with snapshot/restore or vanilla API parity.

## Task 15: B7 Artillery And Strongpoint Doctrine Plan

**Files:**

- Create: `docs/superpowers/plans/2026-05-05-tactical-b7-artillery-strongpoint.md`
- Future create: `src/WhiskeyRealism/Patches/BattleBombardmentPatch.cs`

- [ ] **Step 1: Require B3/B5 evidence**

B7 starts only after sector/strongpoint confidence and group sector missions are stable.

- [ ] **Step 2: Re-read artillery and terrain anchors**

Read:

- `AIBattle.CheckAIBombardment(...)`;
- `CheckCounterBatteryFire()`;
- `UnlimberArtilleryAIMicro`;
- `CheckArtyFallback`;
- `BattlefieldSetup` terrain/cover helpers;
- `Regiment` artillery fields `combatbehaviorordered`, `bombardrange`, `bombardposition`, `targetedenemyunit`.

- [ ] **Step 3: Define B7 behavior limit**

B7 steers bombardment permission and target priority for strongpoints. It does not change projectile physics or ammunition pools; those belong to the weapons/ammunition adjunct.

## Task 16: B7 Artillery And Strongpoint Doctrine Implementation

**Files:**

- Modify: `src/WhiskeyRealism/Plugin.cs`
- Modify: `src/WhiskeyRealism/Tactical/TacticalDoctrineScorer.cs`
- Create: `src/WhiskeyRealism/Patches/BattleBombardmentPatch.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`
- Modify after deploy/smoke: `docs/patch-catalog.md`, `docs/handoff.md`, `MEMORY.md`

- [ ] **Step 1: Add pure tests**

Tests must cover:

- strongpoint plus artillery support selects bombard before assault;
- weak exposed sector permits infantry pressure;
- artillery under close threat selects fallback/defensive behavior;
- bombardment times out or downgrades when ineffective;
- no passive forever-battle when objective pressure rises.

- [ ] **Step 2: Implement bombardment Postfix**

Patch `CheckAIBombardment(...)` only. Preserve vanilla counterbattery and fallback behavior unless the scorer has a higher-confidence reason.

## Task 17: B8 Withdrawal Doctrine Plan

**Files:**

- Create: `docs/superpowers/plans/2026-05-05-tactical-b8-withdrawal-doctrine.md`
- Future create: `src/WhiskeyRealism/Tactical/TacticalRetreatDoctrine.cs`
- Future create: `src/WhiskeyRealism/Patches/BattleFallbackDoctrinePatch.cs`

- [ ] **Step 1: Require B2/B3/B5/B6 evidence**

B8 starts only after order-friction, odds, sector stance, and reserve relief are stable.

- [ ] **Step 2: Re-read withdrawal anchors**

Read:

- `AIBattle.CheckGlobalAIStrategy()`;
- `AIBattle.CheckLineFallbacks(...)`;
- `AIBattle.MicroAICheckForRetreats(...)`;
- `TimePanel.SetRetreatTimer(...)`;
- `BattleUnits.SetWaypoint(...)`;
- `BattleUnits.SetWithdrawal(...)`.

- [ ] **Step 3: Define B8 stages**

B8 stages:

- `Stabilize`;
- `Screen`;
- `BulkWithdraw`;
- `RearGuard`;
- `RearGuardWithdraw`;
- `FullRetreat`.

Full retreat must be last, not first.

## Task 18: B8 Withdrawal Doctrine Implementation

**Files:**

- Modify: `src/WhiskeyRealism/Plugin.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalRetreatDoctrine.cs`
- Create: `src/WhiskeyRealism/Patches/BattleFallbackDoctrinePatch.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`
- Modify after deploy/smoke: `docs/patch-catalog.md`, `docs/handoff.md`, `MEMORY.md`

- [ ] **Step 1: Add pure tests**

Tests must cover:

- sustained bad odds with no relief escalates to staged withdrawal;
- strong terrain plus near relief stays in delay posture;
- rear guard follows main body, not the other way around;
- full retreat only after preservation threshold is met;
- hysteresis prevents retreat/attack oscillation.

- [ ] **Step 2: Implement fallback/retreat steering**

Use `CheckLineFallbacks(...)`-style local movement for local fallback and preserve `MicroAICheckForRetreats(...)` for macro retreat behavior. Do not call battle-level withdrawal APIs for local ammo or line relief.

## Task 19: B9 Tuning And Telemetry Soak Plan

**Files:**

- Create: `docs/superpowers/plans/2026-05-05-tactical-b9-tuning-telemetry-soak.md`
- Future modify: tactical scorer files as dictated by smoke evidence
- Future modify: docs only after measured results

- [ ] **Step 1: Define the smoke matrix**

B9 must include these battle scenarios:

- meeting engagement with no initial contact;
- defensive battle against superior force;
- attack against fortified or entrenched objective;
- reinforcement arrival during battle;
- artillery-heavy battle;
- large multi-division battle with order delays on;
- delayed reserve-release scenario;
- W&L player-subordinate battle;
- badly outnumbered battle requiring staged withdrawal.

- [ ] **Step 2: Define threshold review**

Threshold edits must come from log evidence, not taste. Review:

- stance changes per 10 battle minutes;
- sector mission signature churn;
- retreat stage churn;
- reserve commitment count;
- artillery bombardment duration/effectiveness;
- charge denial and permission counts;
- warning/error count.

## Task 20: B9 Tuning And Telemetry Soak Execution

**Files:**

- Modify only the scorer/config files justified by B9 logs.
- Modify: `docs/handoff.md`
- Modify: `MEMORY.md`
- Modify: `docs/patch-catalog.md` if any patch behavior changes.

- [ ] **Step 1: Run full verification**

Run console tests, build, deploy, hash-verify, and smoke the full matrix.

- [ ] **Step 2: Adjust thresholds**

For each threshold change, record:

- old value;
- new value;
- log line or battle result that justified it;
- expected behavior change;
- rollback value.

- [ ] **Step 3: Final docs closeout**

Update handoff with:

- deployed DLL SHA-256;
- exact B-slices shipped;
- which configs are default-on/default-off;
- smoke matrix result;
- residual Slice B follow-ups;
- whether the weapons/ammunition adjunct is now ready for its own observer plan.

## Documentation And Archive Rules

After each B-slice ships:

- update `docs/patch-catalog.md` if a patch/runtime has shipped;
- update `docs/handoff.md` "What just shipped" and "Next concrete action";
- update `MEMORY.md` only for durable state;
- leave active slice specs in place until the full Slice B tactical brain ships and is smoke-verified;
- archive completed B-slice implementation plans only after ship and smoke verification;
- do not mutate archived plans after archiving.

## Self-Review Checklist

- Spec coverage: B0 through B9 from `2026-05-05-tactical-brain-design.md` are represented here in sequence.
- Anchor coverage: each behavior slice requires re-reading its specific decompile method body before code.
- Observer-first rule: B0 is the first source slice; every behavior slice depends on B0 evidence.
- W&L safety: B1 is narrow and preserves charge cancellation plus feud timing side effects.
- Macro safety: B4 must not overwrite debug/UI or save-state macro overrides.
- Reserve safety: B6 cannot mutate reserve membership without runtime proof and rollback.
- Retreat safety: B8 comes after sector, order, odds, and reserve context.
- Weapons/ammo adjunct: explicitly out of this master behavior track until its own observer-first plan.
- Verification: every DLL-affecting slice requires console tests, build, deploy, hash verification, and runtime smoke.
