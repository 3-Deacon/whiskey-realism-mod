# Tactical B7 + B8 Runtime Wiring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the pure scorers from the [2026-05-08 doctrine-inputs slice](2026-05-08-scourge-tactical-adaptation-implementation.md) into Harmony patches on the vanilla artillery (B7) and withdrawal (B8) decision points, behind default-off config flags, with full pure-harness test coverage on adapters and small doctrine scorers.

**Architecture:** Two phases. **Phase A (B7)** adds one adapter (Regiment → input structs), one small `TacticalArtilleryDoctrine` scorer that combines `TacticalSupportScreen` + ammo + range + B6 intent into a `Decision { PreserveFire / SuppressStrongpoint / CounterBattery / CancelBombard / DefensiveFallback }`, and one Postfix patch on `AIBattle.CheckAIBombardment` that selectively writes vanilla bombardment state. **Phase B (B8)** adds one adapter, one `TacticalWithdrawalDoctrine` scorer combining `TacticalMoralePressure` + `TacticalQuadrantThreatScorer` + `TacticalFatigueState` + commander profile into `Decision { HoldLine / Stabilize / Screen / RearGuard / FullRetreat }`, plus three Postfix patches: two observers (`CheckLineFallbacks`, `MicroAICheckForRetreats`) for telemetry only, and one selected writer (`CheckUseOfReserves` Postfix) that emits `TacticalSectorLedger.SetHelpRequest` and conditionally calls `BattleUnits.SetWithdrawal` for `WithdrawalCandidate` / `CollapseCandidate` units. A morale-snapshot sampler patch bootstraps the `TacticalMoraleSnapshotLedger`. **Every write surface defaults off.**

**Tech Stack:** C# `netstandard2.1` (main DLL), HarmonyX 2.10.2 from BepInEx NuGet, console test harness in `tests/WhiskeyRealism.Tests/Program.cs` (net8.0). Build via `./build.sh`. Tests via `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`.

**Worktree:** Per AGENTS.md operating rule 3, this plan executes in a dedicated worktree. After `git worktree add`, re-link `refs/`: `cd <worktree> && ln -s ../../refs refs`. Branch suggestion: `tactical-b7-b8-wiring`. Current main baseline: 484 PASS, 0 FAIL; deployed DLL hash `ec851625ba006f28176fdd09a7ea49b917bbbf7e71eebce2f353a2b4d7915aa2` (617472 bytes).

**Source-of-truth references:** [`docs/superpowers/specs/2026-05-08-scourge-tactical-adaptation-design.md`](../specs/2026-05-08-scourge-tactical-adaptation-design.md) (the doctrine spec). Older [`2026-05-07-tactical-b7-artillery-strongpoint-runtime.md`](2026-05-07-tactical-b7-artillery-strongpoint-runtime.md) and [`2026-05-07-tactical-b8-staged-withdrawal-runtime.md`](2026-05-07-tactical-b8-staged-withdrawal-runtime.md) are kept as **design context only** — this plan supersedes them as the execution artifact and reuses the shipped Slice B scorers in place of invented ones.

---

## Verified Anchor Map (audited 2026-05-08)

Every anchor below has been grepped against `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs` and matches:

### B7 patch surfaces and inputs

| Symbol | Line | Notes |
|---|---|---|
| `AIBattle.CheckAIBombardment(Regiment aigroup)` | 3869 | Postfix patch site — only writer this plan touches in B7 |
| `AIBattle.CheckCounterBatteryFire(Regiment aigroup)` | 3827 | Read-only inputs; vanilla owns the writes |
| `AIBattle.CheckArtyFallback(Regiment aigroup)` | 3499 | Vanilla owns artillery movement; B7 must NOT duplicate |
| `Regiment.combatbehaviorordered` | 111262 | Values 7=cb-pending, 8=bombarding, 9=cb-active |
| `Regiment.guns`, `unittyp == 2` | — | Artillery-unit gates |
| `Regiment.ammo` (float[]) | 111498 | `ammo[2]` is canister in GT (idiom at 119134); see `TacticalSupportScreen` spec correction |
| `Regiment.ai_feudstance` | 111248 | W&L gate |
| `Regiment.isplayeraiorfeud` (group field) | 3330 | W&L gate |
| `Regiment.lastfiredshottime` | 110794 | Volley dwell timer |
| `Regiment.morale` / `battlestartmorale` / `lastmoraleupdate` | 111146 / 110758 / 111148 | Morale gate inputs |
| `Regiment.unitrange.enemyinfirerangereg` | 109456 | Enemy fire-range list, filter to `unittyp == 2` for CB |
| `Regiment.unitrange.closestownunitnonrouted` | 109502 | Do NOT use raw — includes other artillery; scan inf/cav neighbors |
| `GamePrefs.artilleryfallbackenemyclosedist` | 49226 | Danger radius (used by vanilla at 3525) |
| `GamePrefs.moraletriggerforfallbackifenemyclose[ai_stance]` | 52466 | Stance-indexed morale threshold |
| `GamePrefs.aritimetowaitbeforemovingcloser` | 51228 | Volley dwell |
| `Tools.GetXZDistance` / `Tools.SumUp` | 151160 / 151348 | Distance + array sum |
| `Regiment.ReceivedFireFromUnit` / `CheckReceivedFireOtherUnit` | 121507 / 121482 | Fire-evidence helpers |

### B8 patch surfaces and inputs

| Symbol | Line | Notes |
|---|---|---|
| `AIBattle.CheckLineFallbacks(Regiment aigroup)` | 5118 | Observer-only Postfix |
| `AIBattle.MicroAICheckForRetreats(Regiment aigroup)` | 4817 | Observer-only Postfix |
| `AIBattle.CheckUseOfReserves(Regiment aigroup)` | 6062 | Selective Postfix — emits help-request and gated withdrawal |
| `AIBattle.MarchToSoundOfGuns(Regiment aigroup)` | 3663 | Read-only; vanilla owns the move |
| `AIBattle.CheckForSimilarPositions(Vector3, Regiment)` | 8669 | Reference for destination scorer; not patched |
| `Regiment.RegimentSetPath(...)` | 130791 | Movement-write surface — DO NOT call directly |
| `BattleUnits.SetWaypoint(Regiment, Vector3, ...)` | 91232 | Movement-write surface — DO NOT call directly |
| `BattleUnits.SetWithdrawal(float, List<Regiment>, int, Vector3, bool)` | 92821 | Static — selected use behind config flag |
| `Regiment.SetWithdrawal(float range, Vector3 positionfrom)` | 116116 | Instance — DO NOT call from this plan |
| `Regiment.SetMovementMode(int)` | 124704 | DO NOT call directly |
| `BattleUnits.SetGroupFormation(GameObject, ..., int refuseflank, ...)` | 91815 | Refuse-flank intent surface (DEFERRED to a later plan) |
| `BattleUnits.SetGroupFormation(Regiment, ..., int refuseflank, ...)` | 91822 | Same |
| `Regiment.outflanked` (int 0-7) | 111488 | Tier scoring |
| `Regiment.ownonflank` (int) | 111490 | Tier scoring |
| `Regiment.covervalue` / `coverobject` | 111404 / 111408 | Cover gates |
| `Regiment.friendlyroutednear` / `enemyroutednear` | 111158 / 111160 | Routed-neighbor pressure |
| `Regiment.fatigue` | 111494 | `TacticalFatigueState` input |
| `Regiment.regimentpaths` / `pathinterrupted` | 111068 / 111012 | Movement-state gates |
| `Regiment.lastsetwaypointposition` / `lastsetwaypointrotation` | 111096 / 111098 | Path geometry |
| `Regiment.GetLastTransmittedPathPos(bool ignoreorderdelay)` | 127552 | Path lookup |
| `Regiment.unitrange.closestenemyunitfarreg` / `closestenemyunitfardistance` | 109496 / 109500 | Closest-enemy state |
| `Regiment.unitrange.retreatangle` | 109518 | Withdrawal direction |
| `Regiment.unitrange.closestownunitdestination` / `closestenemyontargetdest` | 109474 / 109478 | Destination crowding |
| `Regiment.unitrange.enemystrengthwithinangle` (float[]) | 109510 | Quadrant scorer input |
| `Regiment.ai_stance` (0-4) / `ai_stanceordered` | 111246 / 111258 | Stance gates |
| `Regiment.lastaichargetime` | 110798 | Charge cooldown |
| `Regiment.chargetarget` (GameObject) | 111232 | Charge state |
| `Regiment.movementmode` | 111850 | Movement-state gate |
| `GamePrefs.aidefensiveslices` | 49310 | Slice count for `enemystrengthwithinangle` |
| `ObjectiveChain.reservegroups` | 2972 | Reserve list (exclude `ai_stance == 2` per 6672) |
| `ObjectiveChain.linegroup_centerunit` / `_leftunits` / `_rightunits` | 2992 / 2994 / 2996 | Sector identity |

---

## File Structure

**New source files** under `src/WhiskeyRealism/Tactical/`:

| File | Responsibility |
|---|---|
| `TacticalArtilleryInputAdapter.cs` | Reflection-safe `Regiment` → input struct builder for `TacticalSupportScreen.Input` and supporting B7 fields. Returns `Unknown`-equivalent on reflection failure. |
| `TacticalArtilleryDoctrine.cs` | Pure scorer combining support-screen + ammo + range + behavior into `Decision { PreserveFire, SuppressStrongpoint, CounterBattery, CancelBombard, DefensiveFallback }`. Consumes the existing `TacticalSupportScreen.Result`, `Tools.SumUp(ammo)/ammo.Length` ratio, and `combatbehaviorordered`. |
| `TacticalWithdrawalInputAdapter.cs` | Reflection-safe `Regiment` → input structs for `TacticalMoralePressure.Input`, `TacticalQuadrantThreatScorer.Input`, `TacticalFatigueState` input. |
| `TacticalWithdrawalDoctrine.cs` | Pure scorer combining `TacticalMoralePressure.Result` + `TacticalQuadrantThreatScorer.Output.RearPressureFlag` + `TacticalFatigueState.Result` into `Decision { HoldLine, Stabilize, Screen, RearGuard, FullRetreat }`. |

**New Harmony patch files** under `src/WhiskeyRealism/Patches/`:

| File | Patch type | Vanilla site |
|---|---|---|
| `B7CheckAIBombardmentPatch.cs` | Postfix | `AIBattle.CheckAIBombardment(Regiment)` (3869) — selectively rewrites `combatbehaviorordered` 8↔9 (bombard ↔ counter-battery) and clears bombard state on `CancelBombard`. Default-off. |
| `B8CheckLineFallbacksObserverPatch.cs` | Postfix | `AIBattle.CheckLineFallbacks(Regiment)` (5118) — telemetry only. |
| `B8MicroAICheckForRetreatsObserverPatch.cs` | Postfix | `AIBattle.MicroAICheckForRetreats(Regiment)` (4817) — telemetry only. |
| `B8MoraleSnapshotSamplerPatch.cs` | Postfix | `AIBattle.MicroAICheckForCharges(Regiment, int)` (4905) — high-frequency cycle that already gates on `microaitaskupdatecycle == 5`; samples morale into `TacticalMoraleSnapshotLedger`. |
| `B8CheckUseOfReservesPatch.cs` | Postfix | `AIBattle.CheckUseOfReserves(Regiment)` (6062) — emits `TacticalSectorLedger.SetHelpRequest` always; calls `BattleUnits.SetWithdrawal(...)` for `WithdrawalCandidate`/`CollapseCandidate` units only when `Enable Tactical Withdrawal Doctrine` config is true. |

**Modified files:**

| File | Change |
|---|---|
| `src/WhiskeyRealism/Plugin.cs` | Add `Enable Tactical Artillery Doctrine` (default `false`) and `Enable Tactical Withdrawal Doctrine` (default `false`) ConfigEntries if absent. Register the five new patches via Harmony. |
| `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` | Add `<Compile Include>` lines for the two new adapters and two new doctrine scorers. |
| `tests/WhiskeyRealism.Tests/Program.cs` | Add test methods for adapter null-safety, doctrine scorer ladder, and ledger sampler invariants. |
| `docs/handoff.md` | Add ship note recording B7+B8 wiring deployment, DLL hash, smoke expectations. |

**Patch hygiene** (per AGENTS.md): every Postfix wraps reflection in try/catch + `Plugin.Log.LogWarning`, never throws, gates writes on the corresponding config flag, gates on the W&L predicate via `TacticalGateHelpers.PassesWlOwnership`, emits a `[once:...]` first-fire marker via `OnceLog`.

---

## Conventions

- **Pure-input-struct discipline.** Every Score function takes a struct of primitives. Adapters live in `*InputAdapter.cs` files; they're the only places that touch reflection. Doctrine scorers and existing Slice B scorers stay Unity-free.
- **Reflection failure path.** Adapter returns a `bool ok` plus the populated struct; on failure, ok is false, the patch logs once and returns. Never throw.
- **Default-off discipline.** No write happens unless the relevant config flag is true. Telemetry-only patches do not need flags but emit `[once:...]` markers.
- **W&L gate.** Replicated via `TacticalGateHelpers.PassesWlOwnership(aiFeudStance, isPlayerAiOrFeud)`. Captured once at patch top from the Regiment + group, passed into Input structs.
- **Tick budget.** Heavy-fan-out patches gate on `microaitaskupdatecycle == 5` where vanilla already does (e.g., the morale sampler).
- **Commits.** One commit per task, HEREDOC message with `Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>` trailer.

---

## Phase A — B7 Artillery Wiring

### Task 1: TacticalArtilleryInputAdapter

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalArtilleryInputAdapter.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Write the failing tests**

Insert into `Program.cs` adjacent to other tactical tests:

```csharp
private static void TacticalArtilleryInputAdapterReadsScalarFields()
{
    var snapshot = new TacticalArtilleryInputAdapter.Snapshot
    {
        UnitTyp = TacticalUnitType.Artillery,
        Guns = 4,
        IsRouted = false,
        MarkedForRout = false,
        AmmoTotalRatio = 0.55f,
        CanisterAmmo = 0.30f,
        Morale = 0.75f,
        BattleStartMorale = 0.85f,
        BattleStartMoraleInitialized = true,
        DangerRadius = 100f,
        ClosestEnemyDistance = 80f,
        InfCavScreenCount = 2,
        AiFeudStance = -1,
        IsPlayerAiOrFeud = 0,
        FallbackThreshold = 0.40f,
        CombatBehaviorOrdered = 8,
        VolleyDwellRemaining = 0f,
    };
    var input = TacticalArtilleryInputAdapter.ToSupportScreenInput(snapshot);
    AssertEqual(0.75f, input.ProtectedUnitMorale, "morale carried");
    AssertEqual(0.40f, input.MoraleFallbackThreshold, "threshold carried");
    AssertEqual(0.85f, input.BattleStartMorale, "battle start carried");
    AssertEqual(80f, input.EnemyDistance, "enemy distance carried");
    AssertEqual(100f, input.DangerRadius, "danger radius carried");
    AssertEqual(2, input.ScreenUnitCount, "inf/cav screen count carried");
    AssertEqual(-1, input.AiFeudStance, "feud stance carried");
}

private static void TacticalArtilleryInputAdapterRejectsNonArtillery()
{
    var snapshot = new TacticalArtilleryInputAdapter.Snapshot
    {
        UnitTyp = TacticalUnitType.Infantry,
        Guns = 0,
        AiFeudStance = -1,
    };
    AssertFalse(TacticalArtilleryInputAdapter.IsEligible(snapshot), "non-artillery rejected");
}

private static void TacticalArtilleryInputAdapterRejectsRouted()
{
    var snapshot = new TacticalArtilleryInputAdapter.Snapshot
    {
        UnitTyp = TacticalUnitType.Artillery,
        Guns = 4,
        IsRouted = true,
        AiFeudStance = -1,
    };
    AssertFalse(TacticalArtilleryInputAdapter.IsEligible(snapshot), "routed rejected");
}
```

Register in `Main()`:

```csharp
("tactical artillery input adapter reads scalar fields", TacticalArtilleryInputAdapterReadsScalarFields),
("tactical artillery input adapter rejects non-artillery", TacticalArtilleryInputAdapterRejectsNonArtillery),
("tactical artillery input adapter rejects routed", TacticalArtilleryInputAdapterRejectsRouted),
```

- [ ] **Step 2: Run; verify failure**

Run: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
Expected: build error — `TacticalArtilleryInputAdapter` not defined.

- [ ] **Step 3: Implement `TacticalArtilleryInputAdapter.cs`**

```csharp
namespace WhiskeyRealism.Tactical
{
    public static class TacticalArtilleryInputAdapter
    {
        public struct Snapshot
        {
            public int UnitTyp;
            public int Guns;
            public bool IsRouted;
            public bool MarkedForRout;
            public float AmmoTotalRatio;
            public float CanisterAmmo;
            public float Morale;
            public float BattleStartMorale;
            public bool BattleStartMoraleInitialized;
            public float DangerRadius;
            public float ClosestEnemyDistance;
            public int InfCavScreenCount;
            public int AiFeudStance;
            public int IsPlayerAiOrFeud;
            public float FallbackThreshold;
            public int CombatBehaviorOrdered;
            public float VolleyDwellRemaining;
        }

        public static bool IsEligible(in Snapshot snapshot)
        {
            if (snapshot.UnitTyp != TacticalUnitType.Artillery) return false;
            if (snapshot.Guns <= 0) return false;
            if (snapshot.IsRouted) return false;
            if (snapshot.MarkedForRout) return false;
            return true;
        }

        public static TacticalSupportScreen.Input ToSupportScreenInput(in Snapshot snapshot)
        {
            return new TacticalSupportScreen.Input
            {
                ProtectedUnitMorale = snapshot.Morale,
                MoraleFallbackThreshold = snapshot.FallbackThreshold,
                BattleStartMorale = snapshot.BattleStartMorale,
                EnemyDistance = snapshot.ClosestEnemyDistance,
                DangerRadius = snapshot.DangerRadius,
                ScreenUnitCount = snapshot.InfCavScreenCount,
                AiFeudStance = snapshot.AiFeudStance,
                IsPlayerAiOrFeud = snapshot.IsPlayerAiOrFeud,
            };
        }
    }
}
```

- [ ] **Step 4: Add csproj entry**

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalArtilleryInputAdapter.cs" Link="TacticalArtilleryInputAdapter.cs" />
```

- [ ] **Step 5: Run; verify pass**

Expected: 487 PASS, 0 FAIL.

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Tactical/TacticalArtilleryInputAdapter.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add TacticalArtilleryInputAdapter (Regiment->SupportScreen.Input)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: TacticalArtilleryDoctrine scorer

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalArtilleryDoctrine.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
private static void TacticalArtilleryDoctrinePreservesFireWhenScreenedAndAmmoOk()
{
    var input = new TacticalArtilleryDoctrine.Input
    {
        ScreenResult = TacticalSupportScreen.Result.Screened,
        AmmoTotalRatio = 0.6f,
        CanisterAmmo = 0.3f,
        ClosestEnemyDistance = 600f,
        UnitFireRange = 800f,
        EnemyArtilleryVisible = false,
        CombatBehaviorOrdered = 8,
        AiFeudStance = -1,
        IsPlayerAiOrFeud = 0,
    };
    AssertEqual(TacticalArtilleryDoctrine.Decision.PreserveFire,
        TacticalArtilleryDoctrine.Score(input), "screened + ammo ok -> preserve fire");
}

private static void TacticalArtilleryDoctrineCounterBatteryWhenEnemyArtVisible()
{
    var input = new TacticalArtilleryDoctrine.Input
    {
        ScreenResult = TacticalSupportScreen.Result.Screened,
        AmmoTotalRatio = 0.6f,
        ClosestEnemyDistance = 700f,
        UnitFireRange = 800f,
        EnemyArtilleryVisible = true,
        CombatBehaviorOrdered = 8,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalArtilleryDoctrine.Decision.CounterBattery,
        TacticalArtilleryDoctrine.Score(input), "enemy art visible -> CB");
}

private static void TacticalArtilleryDoctrineCancelBombardWhenUnsupported()
{
    var input = new TacticalArtilleryDoctrine.Input
    {
        ScreenResult = TacticalSupportScreen.Result.Unsupported,
        AmmoTotalRatio = 0.5f,
        ClosestEnemyDistance = 80f,
        UnitFireRange = 800f,
        CombatBehaviorOrdered = 8,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalArtilleryDoctrine.Decision.CancelBombard,
        TacticalArtilleryDoctrine.Score(input), "unsupported -> cancel bombard");
}

private static void TacticalArtilleryDoctrineDefensiveFallbackWhenShakenAndUnsupported()
{
    var input = new TacticalArtilleryDoctrine.Input
    {
        ScreenResult = TacticalSupportScreen.Result.Shaken,
        AmmoTotalRatio = 0.5f,
        ClosestEnemyDistance = 90f,
        UnitFireRange = 800f,
        CombatBehaviorOrdered = 8,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalArtilleryDoctrine.Decision.DefensiveFallback,
        TacticalArtilleryDoctrine.Score(input), "shaken close enemy -> defensive fallback");
}

private static void TacticalArtilleryDoctrineCancelBombardOnLowAmmo()
{
    var input = new TacticalArtilleryDoctrine.Input
    {
        ScreenResult = TacticalSupportScreen.Result.Screened,
        AmmoTotalRatio = 0.05f,
        ClosestEnemyDistance = 600f,
        UnitFireRange = 800f,
        CombatBehaviorOrdered = 8,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalArtilleryDoctrine.Decision.CancelBombard,
        TacticalArtilleryDoctrine.Score(input), "low ammo -> cancel bombard");
}

private static void TacticalArtilleryDoctrineWlGateBlocks()
{
    var input = new TacticalArtilleryDoctrine.Input
    {
        ScreenResult = TacticalSupportScreen.Result.Screened,
        AmmoTotalRatio = 0.6f,
        ClosestEnemyDistance = 600f,
        UnitFireRange = 800f,
        CombatBehaviorOrdered = 8,
        AiFeudStance = 5,
        IsPlayerAiOrFeud = 0,
    };
    AssertEqual(TacticalArtilleryDoctrine.Decision.PreserveFire,
        TacticalArtilleryDoctrine.Score(input), "W&L gate -> safe default PreserveFire");
}
```

Register all six.

- [ ] **Step 2: Run; verify failure**

- [ ] **Step 3: Implement `TacticalArtilleryDoctrine.cs`**

```csharp
namespace WhiskeyRealism.Tactical
{
    public static class TacticalArtilleryDoctrine
    {
        public enum Decision
        {
            PreserveFire,
            SuppressStrongpoint,
            CounterBattery,
            CancelBombard,
            DefensiveFallback,
        }

        public struct Input
        {
            public TacticalSupportScreen.Result ScreenResult;
            public float AmmoTotalRatio;
            public float CanisterAmmo;
            public float ClosestEnemyDistance;
            public float UnitFireRange;
            public bool EnemyArtilleryVisible;
            public int CombatBehaviorOrdered;
            public int AiFeudStance;
            public int IsPlayerAiOrFeud;
        }

        public static Decision Score(in Input input)
        {
            // W&L gate: safe default is PreserveFire (no write).
            if (!TacticalGateHelpers.PassesWlOwnership(input.AiFeudStance, input.IsPlayerAiOrFeud))
                return Decision.PreserveFire;

            // Low ammo cancels regardless of screen.
            if (input.AmmoTotalRatio < 0.10f)
                return Decision.CancelBombard;

            // Unsupported with close enemy -> cancel.
            bool enemyClose = input.ClosestEnemyDistance <= input.UnitFireRange * 0.20f;
            if (input.ScreenResult == TacticalSupportScreen.Result.Unsupported && enemyClose)
                return Decision.CancelBombard;

            // Shaken close-enemy -> defensive fallback telemetry (vanilla writes movement).
            if (input.ScreenResult == TacticalSupportScreen.Result.Shaken && enemyClose)
                return Decision.DefensiveFallback;

            // Counterbattery if enemy artillery is visible and we are in fire range.
            if (input.EnemyArtilleryVisible && input.ClosestEnemyDistance <= input.UnitFireRange)
                return Decision.CounterBattery;

            // Default: preserve fire.
            return Decision.PreserveFire;
        }
    }
}
```

- [ ] **Step 4: Add csproj entry**

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalArtilleryDoctrine.cs" Link="TacticalArtilleryDoctrine.cs" />
```

- [ ] **Step 5: Run; verify pass**

Expected: 493 PASS, 0 FAIL.

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Tactical/TacticalArtilleryDoctrine.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add TacticalArtilleryDoctrine (PreserveFire/SuppressStrongpoint/CounterBattery/CancelBombard/DefensiveFallback)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: B7 CheckAIBombardment Postfix patch

**Files:**
- Create: `src/WhiskeyRealism/Patches/B7CheckAIBombardmentPatch.cs`
- Modify: `src/WhiskeyRealism/Plugin.cs` (config entry + patch registration)

This is a runtime-only patch; pure-harness coverage is provided by Tasks 1+2 (the adapter and doctrine). The patch itself is exercised by smoke; it is small, fully wrapped in try/catch, and gated on the config flag.

- [ ] **Step 1: Add config entry to `Plugin.cs`** (search for the existing config section, after the last existing `Config.Bind` call):

```csharp
public static ConfigEntry<bool> EnableTacticalArtilleryDoctrine;
// ... inside Awake() before Harmony.PatchAll():
EnableTacticalArtilleryDoctrine = Config.Bind(
    "Tactical Doctrine",
    "Enable Tactical Artillery Doctrine",
    false,
    "Default-off. When true, B7 may rewrite vanilla artillery combatbehaviorordered to favor counter-battery, preserve-fire, or cancel-bombard decisions based on doctrine. Read the patch source before enabling.");
```

- [ ] **Step 2: Implement `B7CheckAIBombardmentPatch.cs`**

```csharp
using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AIBattle.CheckAIBombardment(Regiment) at decompile line 3869 evaluates
    // each artillery sub-unit and may set combatbehaviorordered to 8 (bombard) or 9
    // (counter-battery), or cancel an active bombardment. This Postfix reads the
    // post-vanilla state, runs TacticalArtilleryDoctrine.Score, and selectively
    // rewrites combatbehaviorordered when the doctrine output disagrees with vanilla.
    // Default-off behind Plugin.EnableTacticalArtilleryDoctrine.
    [HarmonyPatch(typeof(AIBattle), "CheckAIBombardment")]
    public static class B7CheckAIBombardmentPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Regiment aigroup)
        {
            if (Plugin.EnableTacticalArtilleryDoctrine == null) return;
            if (!Plugin.EnableTacticalArtilleryDoctrine.Value) return;
            if (aigroup == null) return;

            try
            {
                OnceLog.Fire("b7-check-ai-bombardment-first-fire", () =>
                    Plugin.Log.LogInfo("[once:b7-check-ai-bombardment]"));

                var allUnits = aigroup.allattachedunits;
                if (allUnits == null) return;

                int isPlayerAiOrFeud = aigroup.isplayeraiorfeud;

                for (int i = 0; i < allUnits.Count; i++)
                {
                    var unit = allUnits[i];
                    if (unit == null) continue;

                    var snapshot = BuildSnapshot(unit, aigroup, isPlayerAiOrFeud);
                    if (!TacticalArtilleryInputAdapter.IsEligible(snapshot)) continue;

                    var screenInput = TacticalArtilleryInputAdapter.ToSupportScreenInput(snapshot);
                    var screenResult = TacticalSupportScreen.Score(screenInput);

                    var doctrineInput = new TacticalArtilleryDoctrine.Input
                    {
                        ScreenResult = screenResult,
                        AmmoTotalRatio = snapshot.AmmoTotalRatio,
                        CanisterAmmo = snapshot.CanisterAmmo,
                        ClosestEnemyDistance = snapshot.ClosestEnemyDistance,
                        UnitFireRange = unit.firerange,
                        EnemyArtilleryVisible = HasEnemyArtilleryInFireRange(unit),
                        CombatBehaviorOrdered = snapshot.CombatBehaviorOrdered,
                        AiFeudStance = snapshot.AiFeudStance,
                        IsPlayerAiOrFeud = snapshot.IsPlayerAiOrFeud,
                    };
                    var decision = TacticalArtilleryDoctrine.Score(doctrineInput);

                    ApplyDecision(unit, snapshot, decision);
                }
            }
            catch (System.Exception ex)
            {
                OnceLog.Fire("b7-check-ai-bombardment-error", () =>
                    Plugin.Log.LogWarning("[B7] CheckAIBombardment Postfix error: " + ex.Message));
            }
        }

        private static TacticalArtilleryInputAdapter.Snapshot BuildSnapshot(Regiment unit, Regiment aigroup, int isPlayerAiOrFeud)
        {
            float ammoTotal = 0f;
            float canister = 0f;
            int slots = 0;
            if (unit.ammo != null)
            {
                slots = unit.ammo.Length;
                for (int i = 0; i < slots; i++) ammoTotal += unit.ammo[i];
                if (slots > 2) canister = unit.ammo[2];
            }
            float ammoRatio = (slots > 0) ? ammoTotal / slots : 0f;

            float fallbackThreshold = 0.4f;
            try
            {
                if (GamePrefs.moraletriggerforfallbackifenemyclose != null
                    && aigroup.ai_stance >= 0
                    && aigroup.ai_stance < GamePrefs.moraletriggerforfallbackifenemyclose.Length)
                {
                    fallbackThreshold = GamePrefs.moraletriggerforfallbackifenemyclose[aigroup.ai_stance];
                }
            }
            catch { /* use default */ }

            float closestEnemy = 9999f;
            try
            {
                if (unit.unitrange != null && unit.unitrange.closestenemyunitfardistance > 0f)
                    closestEnemy = unit.unitrange.closestenemyunitfardistance;
            }
            catch { }

            int infCavScreen = CountInfCavScreen(unit);

            float dangerRadius = GamePrefs.artilleryfallbackenemyclosedist;

            float volleyDwell = System.Math.Max(0f,
                (unit.lastfiredshottime + GamePrefs.aritimetowaitbeforemovingcloser) - GameVars.currenttimefromstart);

            return new TacticalArtilleryInputAdapter.Snapshot
            {
                UnitTyp = unit.unittyp,
                Guns = unit.guns,
                IsRouted = unit.isrouted,
                MarkedForRout = unit.markedforrout,
                AmmoTotalRatio = ammoRatio,
                CanisterAmmo = canister,
                Morale = unit.morale,
                BattleStartMorale = unit.battlestartmorale,
                BattleStartMoraleInitialized = unit.battlestartmorale >= 0f,
                DangerRadius = dangerRadius,
                ClosestEnemyDistance = closestEnemy,
                InfCavScreenCount = infCavScreen,
                AiFeudStance = aigroup.ai_feudstance,
                IsPlayerAiOrFeud = isPlayerAiOrFeud,
                FallbackThreshold = fallbackThreshold,
                CombatBehaviorOrdered = unit.combatbehaviorordered,
                VolleyDwellRemaining = volleyDwell,
            };
        }

        private static int CountInfCavScreen(Regiment unit)
        {
            int count = 0;
            try
            {
                if (unit.unitrange == null || unit.unitrange.temp_owninrangeregs == null) return 0;
                for (int i = 0; i < unit.unitrange.temp_owninrangeregs.Count; i++)
                {
                    var friend = unit.unitrange.temp_owninrangeregs[i];
                    if (friend == null) continue;
                    if (friend.isrouted || friend.markedforrout) continue;
                    if (friend.unittyp != TacticalUnitType.Infantry
                        && friend.unittyp != TacticalUnitType.Cavalry) continue;
                    count++;
                }
            }
            catch { }
            return count;
        }

        private static bool HasEnemyArtilleryInFireRange(Regiment unit)
        {
            try
            {
                if (unit.unitrange == null || unit.unitrange.enemyinfirerangereg == null) return false;
                for (int i = 0; i < unit.unitrange.enemyinfirerangereg.Count; i++)
                {
                    var enemy = unit.unitrange.enemyinfirerangereg[i];
                    if (enemy == null) continue;
                    if (enemy.isrouted) continue;
                    if (enemy.unittyp == TacticalUnitType.Artillery) return true;
                }
            }
            catch { }
            return false;
        }

        private static void ApplyDecision(Regiment unit, in TacticalArtilleryInputAdapter.Snapshot snapshot, TacticalArtilleryDoctrine.Decision decision)
        {
            switch (decision)
            {
                case TacticalArtilleryDoctrine.Decision.CancelBombard:
                    if (snapshot.CombatBehaviorOrdered == 8 || snapshot.CombatBehaviorOrdered == 9)
                    {
                        unit.combatbehaviorordered = 0;
                        OnceLog.Fire("b7-cancel-bombard-first-fire", () =>
                            Plugin.Log.LogInfo("[once:b7-cancel-bombard]"));
                    }
                    break;
                case TacticalArtilleryDoctrine.Decision.CounterBattery:
                    if (snapshot.CombatBehaviorOrdered == 8)
                    {
                        unit.combatbehaviorordered = 9;
                        OnceLog.Fire("b7-counterbattery-first-fire", () =>
                            Plugin.Log.LogInfo("[once:b7-counterbattery]"));
                    }
                    break;
                case TacticalArtilleryDoctrine.Decision.PreserveFire:
                case TacticalArtilleryDoctrine.Decision.SuppressStrongpoint:
                case TacticalArtilleryDoctrine.Decision.DefensiveFallback:
                    // Telemetry only; vanilla owns these write paths.
                    break;
            }
        }
    }
}
```

- [ ] **Step 3: Register patch in `Plugin.Awake()`**

The existing Harmony.PatchAll() should pick up the new attribute-decorated class automatically. Verify that PatchAll is called after the assembly's patches namespace, or add an explicit Harmony patch call if Plugin.cs uses individual patch calls.

- [ ] **Step 4: Build the DLL**

Run: `./build.sh`
Expected: 0 warnings, 0 errors. dist/WhiskeyRealism.dll exists.

- [ ] **Step 5: Verify harness still passes**

Run: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
Expected: 493 PASS, 0 FAIL (no new tests; the patch itself is runtime-only).

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Patches/B7CheckAIBombardmentPatch.cs src/WhiskeyRealism/Plugin.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add B7 CheckAIBombardment Postfix consuming TacticalArtilleryDoctrine

Default-off behind 'Enable Tactical Artillery Doctrine' config. Selectively
rewrites combatbehaviorordered (cancel/counterbattery) when doctrine
disagrees with vanilla. Wraps reflection in try/catch + OnceLog.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Phase B — B8 Withdrawal Wiring

### Task 4: TacticalWithdrawalInputAdapter

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalWithdrawalInputAdapter.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
private static void TacticalWithdrawalInputAdapterToMoralePressureInput()
{
    var snapshot = new TacticalWithdrawalInputAdapter.Snapshot
    {
        Morale = 0.55f,
        BattleStartMorale = 0.85f,
        BattleStartMoraleInitialized = true,
        FallbackThreshold = 0.40f,
        Outflanked = 2,
        FriendlyRoutedNear = 1f,
        EnemyRoutedNear = 0f,
        ReceivedFireFromClosestFar = true,
        CoverValue = 0.2f,
        CoverObject = 0,
        AiFeudStance = -1,
        IsPlayerAiOrFeud = 0,
    };
    var input = TacticalWithdrawalInputAdapter.ToMoralePressureInput(snapshot);
    AssertEqual(0.55f, input.CurrentMorale, "morale carried");
    AssertEqual(2, input.Outflanked, "outflanked carried");
    AssertEqual(true, input.ReceivedFireFromClosestFar, "fire flag carried");
    AssertEqual(true, input.BattleStartMoraleInitialized, "init flag carried");
}

private static void TacticalWithdrawalInputAdapterToQuadrantInput()
{
    var slices = new float[36];
    var snapshot = new TacticalWithdrawalInputAdapter.Snapshot
    {
        EnemyStrengthWithinAngle = slices,
        SliceWidthDegrees = 10f,
        UnitFacingDegrees = 90f,
    };
    var input = TacticalWithdrawalInputAdapter.ToQuadrantInput(snapshot);
    AssertEqual(slices.Length, input.Slices.Length, "slices carried");
    AssertEqual(10f, input.SliceWidthDegrees, "slice width carried");
    AssertEqual(90f, input.UnitFacingDegrees, "facing carried");
}
```

Register both.

- [ ] **Step 2: Run; verify failure**

- [ ] **Step 3: Implement `TacticalWithdrawalInputAdapter.cs`**

```csharp
namespace WhiskeyRealism.Tactical
{
    public static class TacticalWithdrawalInputAdapter
    {
        public struct Snapshot
        {
            public float Morale;
            public float BattleStartMorale;
            public bool BattleStartMoraleInitialized;
            public float FallbackThreshold;
            public int Outflanked;
            public float FriendlyRoutedNear;
            public float EnemyRoutedNear;
            public bool ReceivedFireFromClosestFar;
            public float CoverValue;
            public int CoverObject;
            public int AiFeudStance;
            public int IsPlayerAiOrFeud;
            public float Fatigue;
            public float[] EnemyStrengthWithinAngle;
            public float SliceWidthDegrees;
            public float UnitFacingDegrees;
        }

        public static TacticalMoralePressure.Input ToMoralePressureInput(in Snapshot snapshot)
        {
            return new TacticalMoralePressure.Input
            {
                CurrentMorale = snapshot.Morale,
                BattleStartMorale = snapshot.BattleStartMorale,
                BattleStartMoraleInitialized = snapshot.BattleStartMoraleInitialized,
                FallbackThreshold = snapshot.FallbackThreshold,
                Outflanked = snapshot.Outflanked,
                FriendlyRoutedNear = snapshot.FriendlyRoutedNear,
                EnemyRoutedNear = snapshot.EnemyRoutedNear,
                ReceivedFireFromClosestFar = snapshot.ReceivedFireFromClosestFar,
                CoverValue = snapshot.CoverValue,
                CoverObject = snapshot.CoverObject,
                AiFeudStance = snapshot.AiFeudStance,
                IsPlayerAiOrFeud = snapshot.IsPlayerAiOrFeud,
            };
        }

        public static TacticalQuadrantThreatScorer.Input ToQuadrantInput(in Snapshot snapshot)
        {
            return new TacticalQuadrantThreatScorer.Input
            {
                Slices = snapshot.EnemyStrengthWithinAngle,
                SliceWidthDegrees = snapshot.SliceWidthDegrees,
                UnitFacingDegrees = snapshot.UnitFacingDegrees,
            };
        }
    }
}
```

- [ ] **Step 4: Add csproj entry**

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalWithdrawalInputAdapter.cs" Link="TacticalWithdrawalInputAdapter.cs" />
```

- [ ] **Step 5: Run; verify pass** — expected 495 PASS.

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Tactical/TacticalWithdrawalInputAdapter.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add TacticalWithdrawalInputAdapter (Regiment->MoralePressure+Quadrant inputs)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: TacticalWithdrawalDoctrine scorer

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalWithdrawalDoctrine.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
private static void TacticalWithdrawalDoctrineHoldLineWhenStable()
{
    var input = new TacticalWithdrawalDoctrine.Input
    {
        MoralePressure = TacticalMoralePressure.Result.Stable,
        RearPressureFlag = false,
        Fatigue = TacticalFatigueState.Result.Fresh,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalWithdrawalDoctrine.Decision.HoldLine,
        TacticalWithdrawalDoctrine.Score(input), "stable -> hold line");
}

private static void TacticalWithdrawalDoctrineStabilizeUnderPressure()
{
    var input = new TacticalWithdrawalDoctrine.Input
    {
        MoralePressure = TacticalMoralePressure.Result.UnderPressure,
        Fatigue = TacticalFatigueState.Result.Tiring,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalWithdrawalDoctrine.Decision.Stabilize,
        TacticalWithdrawalDoctrine.Score(input), "under pressure -> stabilize");
}

private static void TacticalWithdrawalDoctrineScreenForFallbackCandidate()
{
    var input = new TacticalWithdrawalDoctrine.Input
    {
        MoralePressure = TacticalMoralePressure.Result.FallbackCandidate,
        Fatigue = TacticalFatigueState.Result.Tiring,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalWithdrawalDoctrine.Decision.Screen,
        TacticalWithdrawalDoctrine.Score(input), "fallback candidate -> screen");
}

private static void TacticalWithdrawalDoctrineRearGuardForWithdrawalCandidate()
{
    var input = new TacticalWithdrawalDoctrine.Input
    {
        MoralePressure = TacticalMoralePressure.Result.WithdrawalCandidate,
        Fatigue = TacticalFatigueState.Result.Spent,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalWithdrawalDoctrine.Decision.RearGuard,
        TacticalWithdrawalDoctrine.Score(input), "withdrawal candidate -> rear guard");
}

private static void TacticalWithdrawalDoctrineFullRetreatOnCollapse()
{
    var input = new TacticalWithdrawalDoctrine.Input
    {
        MoralePressure = TacticalMoralePressure.Result.CollapseCandidate,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalWithdrawalDoctrine.Decision.FullRetreat,
        TacticalWithdrawalDoctrine.Score(input), "collapse -> full retreat");
}

private static void TacticalWithdrawalDoctrineRearPressureBumpsLadder()
{
    var input = new TacticalWithdrawalDoctrine.Input
    {
        MoralePressure = TacticalMoralePressure.Result.UnderPressure,
        RearPressureFlag = true,
        Fatigue = TacticalFatigueState.Result.Spent,
        AiFeudStance = -1,
    };
    // Rear-pressure + Spent fatigue bumps UnderPressure to Screen (mid-ladder).
    AssertEqual(TacticalWithdrawalDoctrine.Decision.Screen,
        TacticalWithdrawalDoctrine.Score(input), "rear pressure + spent fatigue bumps to screen");
}

private static void TacticalWithdrawalDoctrineWlGateBlocks()
{
    var input = new TacticalWithdrawalDoctrine.Input
    {
        MoralePressure = TacticalMoralePressure.Result.CollapseCandidate,
        AiFeudStance = 5,
        IsPlayerAiOrFeud = 0,
    };
    AssertEqual(TacticalWithdrawalDoctrine.Decision.HoldLine,
        TacticalWithdrawalDoctrine.Score(input), "W&L gate -> safe default HoldLine");
}
```

Register all seven.

- [ ] **Step 2: Run; verify failure**

- [ ] **Step 3: Implement `TacticalWithdrawalDoctrine.cs`**

```csharp
namespace WhiskeyRealism.Tactical
{
    public static class TacticalWithdrawalDoctrine
    {
        public enum Decision { HoldLine, Stabilize, Screen, RearGuard, FullRetreat }

        public struct Input
        {
            public TacticalMoralePressure.Result MoralePressure;
            public bool RearPressureFlag;
            public TacticalFatigueState.Result Fatigue;
            public int AiFeudStance;
            public int IsPlayerAiOrFeud;
        }

        public static Decision Score(in Input input)
        {
            // W&L gate: safe default is HoldLine (no write).
            if (!TacticalGateHelpers.PassesWlOwnership(input.AiFeudStance, input.IsPlayerAiOrFeud))
                return Decision.HoldLine;

            // Base ladder from morale pressure.
            Decision baseDecision;
            switch (input.MoralePressure)
            {
                case TacticalMoralePressure.Result.Stable:
                    baseDecision = Decision.HoldLine;
                    break;
                case TacticalMoralePressure.Result.UnderPressure:
                    baseDecision = Decision.Stabilize;
                    break;
                case TacticalMoralePressure.Result.FallbackCandidate:
                    baseDecision = Decision.Screen;
                    break;
                case TacticalMoralePressure.Result.WithdrawalCandidate:
                    baseDecision = Decision.RearGuard;
                    break;
                case TacticalMoralePressure.Result.CollapseCandidate:
                    baseDecision = Decision.FullRetreat;
                    break;
                default:
                    baseDecision = Decision.HoldLine;
                    break;
            }

            // Rear pressure + tired/spent/exhausted bumps the ladder one step.
            bool tiredOrWorse = input.Fatigue == TacticalFatigueState.Result.Spent
                || input.Fatigue == TacticalFatigueState.Result.Exhausted;
            if (input.RearPressureFlag && tiredOrWorse)
            {
                baseDecision = BumpUpOne(baseDecision);
            }

            return baseDecision;
        }

        private static Decision BumpUpOne(Decision d)
        {
            switch (d)
            {
                case Decision.HoldLine: return Decision.Stabilize;
                case Decision.Stabilize: return Decision.Screen;
                case Decision.Screen: return Decision.RearGuard;
                case Decision.RearGuard: return Decision.FullRetreat;
                case Decision.FullRetreat: return Decision.FullRetreat;
                default: return d;
            }
        }
    }
}
```

- [ ] **Step 4: Add csproj entry**

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalWithdrawalDoctrine.cs" Link="TacticalWithdrawalDoctrine.cs" />
```

- [ ] **Step 5: Run; verify pass** — expected 502 PASS.

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Tactical/TacticalWithdrawalDoctrine.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add TacticalWithdrawalDoctrine (HoldLine/Stabilize/Screen/RearGuard/FullRetreat)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: B8 CheckLineFallbacks observer Postfix

**Files:**
- Create: `src/WhiskeyRealism/Patches/B8CheckLineFallbacksObserverPatch.cs`

This is observer-only — emits a `[once:...]` first-fire marker and counts fallbacks per battle. No writes. No config flag (telemetry only). Pure-harness coverage isn't applicable.

- [ ] **Step 1: Implement**

```csharp
using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AIBattle.CheckLineFallbacks(Regiment) at decompile line 5118 writes
    // fallback paths/movement modes for line units in morale danger. This Postfix
    // observes only — no writes — and emits a first-fire marker so smoke can verify
    // the patch loaded.
    [HarmonyPatch(typeof(AIBattle), "CheckLineFallbacks")]
    public static class B8CheckLineFallbacksObserverPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Regiment aigroup)
        {
            try
            {
                OnceLog.Fire("b8-check-line-fallbacks-first-fire", () =>
                    Plugin.Log.LogInfo("[once:b8-check-line-fallbacks]"));
            }
            catch (System.Exception ex)
            {
                OnceLog.Fire("b8-check-line-fallbacks-error", () =>
                    Plugin.Log.LogWarning("[B8] CheckLineFallbacks observer error: " + ex.Message));
            }
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `./build.sh` — expect 0 warnings, 0 errors.

- [ ] **Step 3: Verify harness still passes**

Run: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
Expected: 502 PASS, 0 FAIL.

- [ ] **Step 4: Commit**

```bash
git add src/WhiskeyRealism/Patches/B8CheckLineFallbacksObserverPatch.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add B8 CheckLineFallbacks observer Postfix (telemetry only)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: B8 MicroAICheckForRetreats observer Postfix

**Files:**
- Create: `src/WhiskeyRealism/Patches/B8MicroAICheckForRetreatsObserverPatch.cs`

- [ ] **Step 1: Implement**

```csharp
using HarmonyLib;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AIBattle.MicroAICheckForRetreats(Regiment) at decompile line 4817
    // writes retreat paths/modes for units past the morale fallback threshold.
    // Postfix observer only.
    [HarmonyPatch(typeof(AIBattle), "MicroAICheckForRetreats")]
    public static class B8MicroAICheckForRetreatsObserverPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Regiment aigroup)
        {
            try
            {
                OnceLog.Fire("b8-microai-check-retreats-first-fire", () =>
                    Plugin.Log.LogInfo("[once:b8-microai-check-retreats]"));
            }
            catch (System.Exception ex)
            {
                OnceLog.Fire("b8-microai-check-retreats-error", () =>
                    Plugin.Log.LogWarning("[B8] MicroAICheckForRetreats observer error: " + ex.Message));
            }
        }
    }
}
```

- [ ] **Step 2: Build + harness re-run + commit**

```bash
./build.sh
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj  # expect 502 PASS
git add src/WhiskeyRealism/Patches/B8MicroAICheckForRetreatsObserverPatch.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add B8 MicroAICheckForRetreats observer Postfix (telemetry only)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: B8 morale snapshot sampler Postfix

**Files:**
- Create: `src/WhiskeyRealism/Patches/B8MoraleSnapshotSamplerPatch.cs`
- Modify: `src/WhiskeyRealism/Plugin.cs` (add a static `TacticalMoraleSnapshotLedger` instance + capacity constant)

The sampler writes morale samples into the singleton ledger every time vanilla `MicroAICheckForCharges` fires, which already gates on the 5-tick microai cycle. No config flag needed — the ledger is in-memory and side-effect-free for vanilla.

- [ ] **Step 1: Add the singleton ledger to `Plugin.cs`**

After existing static config entries, add:

```csharp
public static TacticalMoraleSnapshotLedger MoraleSnapshotLedger;
// ... in Awake() before Harmony.PatchAll():
MoraleSnapshotLedger = new TacticalMoraleSnapshotLedger(capacity: 4);
```

- [ ] **Step 2: Implement `B8MoraleSnapshotSamplerPatch.cs`**

```csharp
using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AIBattle.MicroAICheckForCharges(Regiment, int) at decompile line 4905
    // is the existing 5-tick microai cycle. Postfix uses this cadence to sample
    // morale into TacticalMoraleSnapshotLedger so B8 doctrine has a prior-morale
    // comparator. RecordSampleIfNew dedupes by vanilla lastmoraleupdate.
    [HarmonyPatch(typeof(AIBattle), "MicroAICheckForCharges")]
    public static class B8MoraleSnapshotSamplerPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Regiment aigroup, int restrictunittypes)
        {
            if (Plugin.MoraleSnapshotLedger == null) return;
            if (aigroup == null) return;

            try
            {
                OnceLog.Fire("b8-morale-snapshot-sampler-first-fire", () =>
                    Plugin.Log.LogInfo("[once:b8-morale-snapshot-sampler]"));

                var allUnits = aigroup.allattachedunits;
                if (allUnits == null) return;

                for (int i = 0; i < allUnits.Count; i++)
                {
                    var unit = allUnits[i];
                    if (unit == null) continue;
                    if (unit.unittyp > TacticalUnitType.MaxCombat) continue;
                    if (unit.unittyp == TacticalUnitType.Excluded) continue;
                    if (unit.permanentlydetached) continue;

                    if (unit.isrouted)
                    {
                        var routedKey = new TacticalMoraleSnapshotLedger.Key(
                            unit.GetInstanceID(),
                            ((UnityEngine.Object)unit).name);
                        Plugin.MoraleSnapshotLedger.PruneRouted(routedKey);
                        continue;
                    }

                    var key = new TacticalMoraleSnapshotLedger.Key(
                        unit.GetInstanceID(),
                        ((UnityEngine.Object)unit).name);
                    Plugin.MoraleSnapshotLedger.RecordSampleIfNew(
                        key,
                        morale: unit.morale,
                        timeFromStart: GameVars.currenttimefromstart,
                        vanillaLastMoraleUpdate: unit.lastmoraleupdate);
                }
            }
            catch (System.Exception ex)
            {
                OnceLog.Fire("b8-morale-snapshot-error", () =>
                    Plugin.Log.LogWarning("[B8] morale snapshot sampler error: " + ex.Message));
            }
        }
    }
}
```

- [ ] **Step 3: Build + harness re-run + commit**

```bash
./build.sh
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj  # expect 502 PASS
git add src/WhiskeyRealism/Patches/B8MoraleSnapshotSamplerPatch.cs src/WhiskeyRealism/Plugin.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add B8 morale snapshot sampler patch (5-tick microai cycle)

Singleton TacticalMoraleSnapshotLedger in Plugin samples morale per unit on
the existing MicroAICheckForCharges Postfix cadence. RecordSampleIfNew dedupes
by vanilla lastmoraleupdate so we don't burn cycles on no-change ticks.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 9: B8 CheckUseOfReserves Postfix (help-request emitter + gated withdrawal)

**Files:**
- Create: `src/WhiskeyRealism/Patches/B8CheckUseOfReservesPatch.cs`
- Modify: `src/WhiskeyRealism/Plugin.cs` (add `EnableTacticalWithdrawalDoctrine` config entry)

This patch always emits `TacticalSectorLedger.SetHelpRequest` (telemetry — no config flag). It additionally calls `BattleUnits.SetWithdrawal(...)` for individual units classified as `WithdrawalCandidate` or `CollapseCandidate` — but only when `EnableTacticalWithdrawalDoctrine` is true. Default-off.

- [ ] **Step 1: Add config entry to `Plugin.cs`**

```csharp
public static ConfigEntry<bool> EnableTacticalWithdrawalDoctrine;
// ... in Awake():
EnableTacticalWithdrawalDoctrine = Config.Bind(
    "Tactical Doctrine",
    "Enable Tactical Withdrawal Doctrine",
    false,
    "Default-off. When true, B8 may call BattleUnits.SetWithdrawal for individual units classified as WithdrawalCandidate or CollapseCandidate by TacticalWithdrawalDoctrine. Read the patch source before enabling.");
```

- [ ] **Step 2: Implement `B8CheckUseOfReservesPatch.cs`**

```csharp
using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AIBattle.CheckUseOfReserves(Regiment) at decompile line 6062 supports
    // outflanked friendly units by moving an unengaged reserve toward them. This
    // Postfix uses the same input set to compute TacticalWithdrawalDoctrine.Decision
    // per attached unit, emits a help-request telemetry sink, and conditionally
    // issues SetWithdrawal calls when the config flag is on. Reserve-list mutation
    // is NOT performed here — vanilla owns that.
    [HarmonyPatch(typeof(AIBattle), "CheckUseOfReserves")]
    public static class B8CheckUseOfReservesPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Regiment aigroup)
        {
            if (aigroup == null) return;

            try
            {
                OnceLog.Fire("b8-check-reserves-first-fire", () =>
                    Plugin.Log.LogInfo("[once:b8-check-reserves]"));

                var allUnits = aigroup.allattachedunits;
                if (allUnits == null) return;

                int isPlayerAiOrFeud = aigroup.isplayeraiorfeud;
                bool writesEnabled = Plugin.EnableTacticalWithdrawalDoctrine != null
                    && Plugin.EnableTacticalWithdrawalDoctrine.Value;

                List<Regiment> withdrawalList = null;

                for (int i = 0; i < allUnits.Count; i++)
                {
                    var unit = allUnits[i];
                    if (unit == null) continue;
                    if (unit.unittyp > TacticalUnitType.Cavalry) continue; // inf/cav only
                    if (unit.isrouted || unit.markedforrout) continue;
                    if (unit.permanentlydetached) continue;
                    if (!TacticalGateHelpers.PassesWlOwnership(aigroup.ai_feudstance, isPlayerAiOrFeud))
                        continue;

                    var snapshot = BuildSnapshot(unit, aigroup, isPlayerAiOrFeud);
                    var moraleInput = TacticalWithdrawalInputAdapter.ToMoralePressureInput(snapshot);
                    var moraleResult = TacticalMoralePressure.Score(moraleInput);

                    var quadrantInput = TacticalWithdrawalInputAdapter.ToQuadrantInput(snapshot);
                    var quadrantOutput = TacticalQuadrantThreatScorer.Score(quadrantInput);

                    var fatigueResult = TacticalFatigueState.Score(snapshot.Fatigue);

                    var doctrineInput = new TacticalWithdrawalDoctrine.Input
                    {
                        MoralePressure = moraleResult,
                        RearPressureFlag = quadrantOutput.RearPressureFlag,
                        Fatigue = fatigueResult,
                        AiFeudStance = snapshot.AiFeudStance,
                        IsPlayerAiOrFeud = snapshot.IsPlayerAiOrFeud,
                    };
                    var decision = TacticalWithdrawalDoctrine.Score(doctrineInput);

                    EmitHelpRequest(aigroup, decision);

                    if (writesEnabled
                        && (decision == TacticalWithdrawalDoctrine.Decision.RearGuard
                            || decision == TacticalWithdrawalDoctrine.Decision.FullRetreat))
                    {
                        if (withdrawalList == null) withdrawalList = new List<Regiment>();
                        withdrawalList.Add(unit);
                    }
                }

                if (withdrawalList != null && withdrawalList.Count > 0)
                {
                    float endDate = GameVars.currenttimefromstart + 600f; // 10 minutes game time
                    Vector3 fromPosition = new Vector3();
                    BattleUnits.SetWithdrawal(endDate, withdrawalList, aigroup.alliance, fromPosition, false);
                    OnceLog.Fire("b8-set-withdrawal-first-fire", () =>
                        Plugin.Log.LogInfo("[once:b8-set-withdrawal] count=" + withdrawalList.Count));
                }
            }
            catch (System.Exception ex)
            {
                OnceLog.Fire("b8-check-reserves-error", () =>
                    Plugin.Log.LogWarning("[B8] CheckUseOfReserves Postfix error: " + ex.Message));
            }
        }

        private static TacticalWithdrawalInputAdapter.Snapshot BuildSnapshot(Regiment unit, Regiment aigroup, int isPlayerAiOrFeud)
        {
            float fallbackThreshold = 0.4f;
            try
            {
                if (GamePrefs.moraletriggerforfallbackifenemyclose != null
                    && aigroup.ai_stance >= 0
                    && aigroup.ai_stance < GamePrefs.moraletriggerforfallbackifenemyclose.Length)
                {
                    fallbackThreshold = GamePrefs.moraletriggerforfallbackifenemyclose[aigroup.ai_stance];
                }
            }
            catch { }

            bool fired = false;
            try
            {
                if (unit.unitrange != null && unit.unitrange.closestenemyunitfarreg != null)
                {
                    fired = unit.ReceivedFireFromUnit(unit.unitrange.closestenemyunitfarreg)
                        || unit.CheckReceivedFireOtherUnit(unit.unitrange.closestenemyunitfarreg);
                }
            }
            catch { }

            float[] slices = null;
            float sliceWidth = 10f;
            try
            {
                if (unit.unitrange != null) slices = unit.unitrange.enemystrengthwithinangle;
                if (GamePrefs.aidefensiveslices > 0) sliceWidth = 360f / GamePrefs.aidefensiveslices;
            }
            catch { }

            float facing = 0f;
            try { facing = ((UnityEngine.Component)unit).transform.eulerAngles.y; } catch { }

            return new TacticalWithdrawalInputAdapter.Snapshot
            {
                Morale = unit.morale,
                BattleStartMorale = unit.battlestartmorale,
                BattleStartMoraleInitialized = unit.battlestartmorale >= 0f,
                FallbackThreshold = fallbackThreshold,
                Outflanked = unit.outflanked,
                FriendlyRoutedNear = unit.friendlyroutednear,
                EnemyRoutedNear = unit.enemyroutednear,
                ReceivedFireFromClosestFar = fired,
                CoverValue = unit.covervalue,
                CoverObject = unit.coverobject,
                AiFeudStance = aigroup.ai_feudstance,
                IsPlayerAiOrFeud = isPlayerAiOrFeud,
                Fatigue = unit.fatigue,
                EnemyStrengthWithinAngle = slices,
                SliceWidthDegrees = sliceWidth,
                UnitFacingDegrees = facing,
            };
        }

        private static void EmitHelpRequest(Regiment aigroup, TacticalWithdrawalDoctrine.Decision decision)
        {
            int sectorId = aigroup.GetInstanceID(); // sector identity = group id; refined later
            TacticalHelpRequest.Decision request;
            switch (decision)
            {
                case TacticalWithdrawalDoctrine.Decision.Screen:
                    request = TacticalHelpRequest.Decision.RequestReserveScreen;
                    break;
                case TacticalWithdrawalDoctrine.Decision.RearGuard:
                    request = TacticalHelpRequest.Decision.RequestLineRelief;
                    break;
                case TacticalWithdrawalDoctrine.Decision.FullRetreat:
                    request = TacticalHelpRequest.Decision.RequestMainEffortShift;
                    break;
                default:
                    request = TacticalHelpRequest.Decision.NoRequest;
                    break;
            }
            TacticalSectorLedger.SetHelpRequest(sectorId, request);
        }
    }
}
```

- [ ] **Step 3: Build + harness re-run + commit**

```bash
./build.sh
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj  # expect 502 PASS
git add src/WhiskeyRealism/Patches/B8CheckUseOfReservesPatch.cs src/WhiskeyRealism/Plugin.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add B8 CheckUseOfReserves Postfix (help-request emitter + default-off SetWithdrawal)

Always emits TacticalSectorLedger.SetHelpRequest. SetWithdrawal calls for
WithdrawalCandidate/CollapseCandidate units gated on
'Enable Tactical Withdrawal Doctrine' (default off).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Phase C — Build, Deploy, Smoke Prep, Handoff

### Task 10: Build, deploy, hash verify, handoff

**Files:**
- Build: `dist/WhiskeyRealism.dll`
- Deploy: `<GTCW>/BepInEx/plugins/WhiskeyRealism.dll`
- Modify: `docs/handoff.md`

- [ ] **Step 1: Final harness pass**

Run: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
Expected: 502 PASS, 0 FAIL.

- [ ] **Step 2: Build the production DLL**

Run: `./build.sh`
Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Confirm GTCW is closed before deploying**

Verify with the user. If GTCW is open, the `cp` will fail with `cp: cannot create regular file ...: Invalid argument`.

- [ ] **Step 4: Deploy**

Run:
```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
```

- [ ] **Step 5: Verify hash match**

Run:
```bash
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```
Expected: timestamps and sizes match; SHA-256 hashes are identical. Record the hash.

- [ ] **Step 6: Update `docs/handoff.md`**

Add a new "Slice B B7+B8 wiring shipped" entry near the top noting:
- Adapters and doctrine scorers that landed
- Five new Harmony patches (one B7 writer, two B8 observers, one snapshot sampler, one B8 selective writer)
- Both config keys default-off
- 502 harness PASS / 0 FAIL
- DLL hash and byte size from Step 5
- Smoke expectations: launch GTCW, start battle, verify the four `[once:...]` markers appear in `BepInEx/LogOutput.log` (`b8-check-line-fallbacks`, `b8-microai-check-retreats`, `b8-morale-snapshot-sampler`, `b8-check-reserves`); verify `[once:b7-check-ai-bombardment]` appears when artillery engages (config off — no rewrites expected); enable both flags one at a time and verify cancel-bombard / counterbattery / set-withdrawal `[once:...]` markers fire.

- [ ] **Step 7: Commit handoff**

```bash
git add docs/handoff.md
git commit -m "$(cat <<'EOF'
docs(handoff): record B7+B8 wiring ship (5 patches, default-off, 502 PASS)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 8: Smoke-test instructions for the user**

Hand off to the user with:

1. Close GTCW if running.
2. (DLL already deployed — hash recorded.)
3. Launch GTCW, load any campaign with a battle in progress or trigger a fresh battle.
4. Tail `BepInEx/LogOutput.log`. Expected first-fire markers: `[once:b7-check-ai-bombardment]`, `[once:b8-check-line-fallbacks]`, `[once:b8-microai-check-retreats]`, `[once:b8-morale-snapshot-sampler]`, `[once:b8-check-reserves]`.
5. With both config flags **off** (default), no behavior change versus pre-wiring.
6. To exercise the writer surfaces: enable `Enable Tactical Artillery Doctrine = true` in the config, restart, observe `[once:b7-cancel-bombard]` / `[once:b7-counterbattery]`. Then enable `Enable Tactical Withdrawal Doctrine = true`, restart, observe `[once:b8-set-withdrawal] count=N`. Disable both if not desired.

---

## Self-Review

**1. Spec coverage check.** This plan implements the wiring for the doctrine inputs spec.

| Doctrine input (Slice B) | Consumed by | Task |
|---|---|---|
| `TacticalSupportScreen` | B7 doctrine | Task 1, 2, 3 |
| `TacticalDestinationDiscipline` | (deferred — no movement-write surface in this plan calls it) | — |
| `TacticalMoralePressure` | B8 doctrine | Task 4, 5, 9 |
| `TacticalMoraleSnapshotLedger` | B8 sampler | Task 8 |
| `TacticalHelpRequest` | B8 reserves emitter | Task 9 |
| `TacticalQuadrantThreatScorer` | B8 doctrine (rear pressure) | Task 4, 5, 9 |
| `TacticalChargeViability` | (deferred — B6c plan owns; no charge writers here) | — |
| `TacticalRefuseFlankIntent` | (deferred — would require `SetGroupFormation` writer in a later plan) | — |
| `TacticalFatigueState` | B8 doctrine ladder bump | Task 5, 9 |
| `TacticalGateHelpers` | every doctrine + every patch | Task 1, 2, 3, 4, 5, 9 |
| `TacticalUnitType` | adapters and patches | Task 1, 3, 8, 9 |

Three scorers are deliberately **not** consumed by this plan: `TacticalDestinationDiscipline` (no destination writer here), `TacticalChargeViability` (B6c owns charge gating), `TacticalRefuseFlankIntent` (requires `SetGroupFormation` writer; deferred to a later plan). Recorded in handoff for future slices.

**2. Placeholder scan.** Searched for "TBD", "TODO", "fill in details", "implement later", "Similar to Task N" — none present. All code blocks are complete.

**3. Type consistency.** Verified across tasks:
- `TacticalArtilleryInputAdapter.Snapshot` field set is consistent between Task 1 (struct definition) and Task 3 (patch builder).
- `TacticalArtilleryDoctrine.Decision` enum values match in Task 2 tests, Task 2 implementation, and Task 3 patch consumer.
- `TacticalWithdrawalInputAdapter.Snapshot` consistent between Task 4 and Task 9.
- `TacticalWithdrawalDoctrine.Decision` consistent across Task 5 and Task 9.
- `TacticalGateHelpers.PassesWlOwnership(int, int)` signature consistent with Slice B foundation.

**4. Scope check.** All 10 tasks are in the same subsystem (B7+B8 Harmony patch wiring of Slice B doctrine inputs). Cohesive. Could be split into B7 (Tasks 1-3) and B8 (Tasks 4-9) sub-plans if execution prefers; the dependency chain only crosses phases at Task 8 (sampler patch — needed by Task 9's doctrine consumer). No decomposition required.

---

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/2026-05-08-tactical-b7-b8-wiring-implementation.md`.**

Two execution options:

1. **Subagent-Driven (recommended)** — dispatch a fresh subagent per task, review between tasks, fast iteration. Required sub-skill: `superpowers:subagent-driven-development`.
2. **Inline Execution** — execute tasks in this session via `superpowers:executing-plans`, batch execution with checkpoints.

Worktree gate: per AGENTS.md operating rule 3, plan execution requires `superpowers:using-git-worktrees`. Suggested branch: `tactical-b7-b8-wiring`. Both options invoke the gate first.
