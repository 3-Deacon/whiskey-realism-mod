# Scourge Tactical Adaptation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the pure-scorer doctrine inputs specified in [`docs/superpowers/specs/archive/2026-05-08-scourge-tactical-adaptation-design.md`](../specs/2026-05-08-scourge-tactical-adaptation-design.md) — nine read-only scorers plus a morale snapshot ledger plus tick-budget infrastructure — with full pure-harness test coverage. **No runtime writes.** Wiring these scorers into Harmony patches is the responsibility of separate B6c / B7 / B8 plans.

**Architecture:** All scorers live under `src/WhiskeyRealism/Tactical/`, follow the existing pure-input-struct → labelled-output-enum pattern (see `TacticalOddsDoctrine.cs`, `TacticalDoctrineScorer.cs`, `TacticalLocalReactionScorer.cs`), and emit telemetry via the existing `TacticalTelemetry` and `OnceLog` helpers. Reflection-failure paths produce `Unknown` results and degrade silently after the first log. The `TacticalMoraleSnapshotLedger` provides the prior-morale comparator GT vanilla does not expose; it is in-memory only (no JSON sidecar).

**Tech Stack:** C# `netstandard2.1` (main DLL); console test harness in `tests/WhiskeyRealism.Tests/Program.cs` targets `net8.0` and references `Assembly-CSharp.dll` + `UnityEngine.dll` via `refs/`. Build via `./build.sh`. Tests via `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`. Deploy verified by SHA-256 match against `dist/WhiskeyRealism.dll`.

**Worktree:** This plan should execute in a dedicated git worktree per AGENTS.md operating rule 3 (`using-git-worktrees` gate before plan execution). After creating the worktree, re-link `refs/` from the main repo: `cd <worktree> && ln -s ../../refs refs`.

---

## File Structure

**New source files** under `src/WhiskeyRealism/Tactical/`:

| File | Responsibility |
|---|---|
| `TacticalGateHelpers.cs` | Replicated W&L ownership gate, alliance bounds check, `OnceLog`-wrapped reflection helper. Used by every new scorer. |
| `TacticalScoreCache.cs` | Per-regiment score cache keyed on the invalidation field set defined in the spec's tick-budget section. Reused by all scorers. |
| `TacticalSupportScreen.cs` | Output `Screened / Shaken / Unsupported / Unknown` from inf/cav-filtered friendly scan. |
| `TacticalDestinationDiscipline.cs` | Output `ClearDestination / CrowdedSameType / CrowdedAdjacent / EnemyOnDestination / PathRiskUnknown` with unit-type-tiered thresholds. |
| `TacticalMoraleSnapshotLedger.cs` | Ring-buffer per-regiment morale samples. Identity = `GetInstanceID()` + name fallback. In-memory only. |
| `TacticalMoralePressure.cs` | Output ladder `Stable / UnderPressure / FallbackCandidate / WithdrawalCandidate / CollapseCandidate`. Consumes the snapshot ledger. |
| `TacticalHelpRequest.cs` | Output `RequestReserveScreen / RequestLineRelief / RequestArtillerySupport / RequestMainEffortShift / NoRequest`. |
| `TacticalQuadrantThreatScorer.cs` | Output four-arc strength sums plus `RearPressureFlag` from the existing `enemystrengthwithinangle` slice array. |
| `TacticalChargeViability.cs` | Output `Refuse / Allow / Encourage` from the vanilla weighting math plus cooldown / morale gates. |
| `TacticalRefuseFlankIntent.cs` | Output `NoRefuse / RefuseLeft / RefuseRight` for downstream `SetGroupFormation` consumers. |
| `TacticalFatigueState.cs` | Output `Fresh / Tiring / Spent / Exhausted`. Modulates morale and charge scorers. |

**Modified source files:**

| File | Change |
|---|---|
| `src/WhiskeyRealism/Tactical/TacticalSectorLedger.cs` | Add `HelpRequest` field of type `TacticalHelpRequest.Decision`, plus a setter consumed by future runtime adapters. |
| `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` | Add `<Compile Include="..\..\src\WhiskeyRealism\Tactical\<Name>.cs" Link="<Name>.cs" />` line per new source file. |
| `tests/WhiskeyRealism.Tests/Program.cs` | Add new `private static void` test methods + register each in the `tests` array in `Main()`. |

**No new Harmony patch files. No runtime wiring. No JSON sidecar changes.**

---

## Conventions for every task

- **TDD discipline.** Each task starts with a failing test, then minimal implementation, then green test.
- **Pure inputs.** Every scorer takes a struct of primitives (alliance id, ints, floats, enum values) — no `Regiment` or `UnitRange` references. The Regiment → input-struct adapter lives in a separate runtime plan.
- **Output enums** are nested inside their scorer class (e.g., `TacticalSupportScreen.Result`).
- **Reflection** is not needed in this plan because tests use synthetic primitive inputs. Reflection over vanilla fields belongs to the runtime adapter (B6c / B7 / B8 plans).
- **Telemetry** uses existing `TacticalTelemetry.Summary(...)` + `OnceLog`. New scorers add their own `TacticalObservedEvent` enum entries only if telemetry is needed in this plan; defer telemetry wiring to runtime plans where appropriate.
- **Commits** at the end of each task with a feat/fix/refactor message and the `Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>` trailer per AGENTS.md.

---

## Task 1: Foundation — gates and score cache

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalGateHelpers.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalScoreCache.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` (add Compile Include for both new files)
- Test: `tests/WhiskeyRealism.Tests/Program.cs` (add 3 test methods)

- [ ] **Step 1: Write the failing tests** in `Program.cs`

```csharp
private static void TacticalGateHelpersWlOwnership()
{
    AssertTrue(TacticalGateHelpers.PassesWlOwnership(aiFeudStance: -1, isPlayerAiOrFeud: 0), "feud=-1 passes");
    AssertTrue(TacticalGateHelpers.PassesWlOwnership(aiFeudStance: 5, isPlayerAiOrFeud: 2), "playerai=2 passes");
    AssertFalse(TacticalGateHelpers.PassesWlOwnership(aiFeudStance: 5, isPlayerAiOrFeud: 0), "neither passes");
}

private static void TacticalGateHelpersAllianceBounds()
{
    AssertTrue(TacticalGateHelpers.IsValidAllianceIndex(0, factionLength: 2), "0 in range");
    AssertTrue(TacticalGateHelpers.IsValidAllianceIndex(1, factionLength: 2), "1 in range");
    AssertFalse(TacticalGateHelpers.IsValidAllianceIndex(2, factionLength: 2), "2 (Europe) out of bounds");
    AssertFalse(TacticalGateHelpers.IsValidAllianceIndex(-1, factionLength: 2), "negative out of bounds");
}

private static void TacticalScoreCacheRoundtrip()
{
    var cache = new TacticalScoreCache<int>();
    var key = new TacticalScoreCache<int>.Key(unitId: 42, signature: "sig-A");
    AssertFalse(cache.TryGet(key, out _), "miss before write");
    cache.Set(key, 7);
    AssertTrue(cache.TryGet(key, out int value), "hit after write");
    AssertEqual(7, value, "round-tripped value");
    var staleKey = new TacticalScoreCache<int>.Key(unitId: 42, signature: "sig-B");
    AssertFalse(cache.TryGet(staleKey, out _), "different signature misses");
}
```

Register the tests in `Main()`:

```csharp
("tactical gate helpers W&L ownership", TacticalGateHelpersWlOwnership),
("tactical gate helpers alliance bounds", TacticalGateHelpersAllianceBounds),
("tactical score cache roundtrip", TacticalScoreCacheRoundtrip),
```

- [ ] **Step 2: Run tests; verify failure**

Run: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
Expected: build error — `TacticalGateHelpers` and `TacticalScoreCache` not defined.

- [ ] **Step 3: Implement `TacticalGateHelpers.cs`**

```csharp
namespace WhiskeyRealism.Tactical
{
    public static class TacticalGateHelpers
    {
        public static bool PassesWlOwnership(int aiFeudStance, int isPlayerAiOrFeud)
            => aiFeudStance == -1 || isPlayerAiOrFeud == 2;

        public static bool IsValidAllianceIndex(int allianceId, int factionLength)
            => allianceId >= 0 && allianceId < factionLength;
    }
}
```

- [ ] **Step 4: Implement `TacticalScoreCache.cs`**

```csharp
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical
{
    public sealed class TacticalScoreCache<TValue>
    {
        public readonly struct Key
        {
            public readonly int UnitId;
            public readonly string Signature;
            public Key(int unitId, string signature) { UnitId = unitId; Signature = signature; }
            public override int GetHashCode() => (UnitId * 397) ^ (Signature?.GetHashCode() ?? 0);
            public override bool Equals(object obj)
                => obj is Key k && k.UnitId == UnitId && k.Signature == Signature;
        }

        private readonly Dictionary<Key, TValue> entries = new Dictionary<Key, TValue>();

        public bool TryGet(Key key, out TValue value) => entries.TryGetValue(key, out value);
        public void Set(Key key, TValue value) => entries[key] = value;
        public void Clear() => entries.Clear();
        public int Count => entries.Count;
    }
}
```

- [ ] **Step 5: Add csproj entries**

In `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, after the existing `Compile Include` lines for `TacticalTelemetry.cs`:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalGateHelpers.cs" Link="TacticalGateHelpers.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalScoreCache.cs" Link="TacticalScoreCache.cs" />
```

- [ ] **Step 6: Run tests; verify pass**

Run: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
Expected: all three new tests pass; existing tests unaffected.

- [ ] **Step 7: Commit**

```bash
git add src/WhiskeyRealism/Tactical/TacticalGateHelpers.cs src/WhiskeyRealism/Tactical/TacticalScoreCache.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add gate helpers and score cache foundation

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: TacticalSupportScreen scorer

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalSupportScreen.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

**Spec reference:** Section "TacticalSupportScreen" in the design doc; output is `Screened / Shaken / Unsupported / Unknown`.

- [ ] **Step 1: Write the failing tests**

```csharp
private static void TacticalSupportScreenSupportedAndSteady()
{
    var input = new TacticalSupportScreen.Input
    {
        ProtectedUnitMorale = 0.7f,
        MoraleFallbackThreshold = 0.4f,
        BattleStartMorale = 0.8f,
        EnemyDistance = 100f,
        DangerRadius = 200f,
        ScreenUnitCount = 1,
        AiFeudStance = -1,
        IsPlayerAiOrFeud = 0,
    };
    AssertEqual(TacticalSupportScreen.Result.Screened, TacticalSupportScreen.Score(input), "screened steady");
}

private static void TacticalSupportScreenShakenWithScreen()
{
    var input = new TacticalSupportScreen.Input
    {
        ProtectedUnitMorale = 0.30f,
        MoraleFallbackThreshold = 0.40f,
        BattleStartMorale = 0.80f,
        EnemyDistance = 100f,
        DangerRadius = 200f,
        ScreenUnitCount = 1,
        AiFeudStance = -1,
        IsPlayerAiOrFeud = 0,
    };
    AssertEqual(TacticalSupportScreen.Result.Shaken, TacticalSupportScreen.Score(input), "shaken with screen");
}

private static void TacticalSupportScreenUnsupportedNoScreen()
{
    var input = new TacticalSupportScreen.Input
    {
        ProtectedUnitMorale = 0.7f,
        MoraleFallbackThreshold = 0.4f,
        BattleStartMorale = 0.8f,
        EnemyDistance = 100f,
        DangerRadius = 200f,
        ScreenUnitCount = 0,
        AiFeudStance = -1,
        IsPlayerAiOrFeud = 0,
    };
    AssertEqual(TacticalSupportScreen.Result.Unsupported, TacticalSupportScreen.Score(input), "unsupported");
}

private static void TacticalSupportScreenUnknownOnUninitialized()
{
    var input = new TacticalSupportScreen.Input
    {
        ProtectedUnitMorale = 0.7f,
        MoraleFallbackThreshold = 0.4f,
        BattleStartMorale = -1f,    // uninitialized sentinel from vanilla
        EnemyDistance = 100f,
        DangerRadius = 200f,
        ScreenUnitCount = 1,
        AiFeudStance = -1,
        IsPlayerAiOrFeud = 0,
    };
    AssertEqual(TacticalSupportScreen.Result.Unknown, TacticalSupportScreen.Score(input), "uninitialized");
}

private static void TacticalSupportScreenWlGateBlocks()
{
    var input = new TacticalSupportScreen.Input
    {
        ProtectedUnitMorale = 0.7f,
        MoraleFallbackThreshold = 0.4f,
        BattleStartMorale = 0.8f,
        EnemyDistance = 100f,
        DangerRadius = 200f,
        ScreenUnitCount = 1,
        AiFeudStance = 5,           // not -1
        IsPlayerAiOrFeud = 0,       // not 2
    };
    AssertEqual(TacticalSupportScreen.Result.Unknown, TacticalSupportScreen.Score(input), "W&L gate blocks");
}
```

Register all five tests in `Main()`.

- [ ] **Step 2: Run tests; verify failure**

Expected: build error — `TacticalSupportScreen` not defined.

- [ ] **Step 3: Implement `TacticalSupportScreen.cs`**

```csharp
namespace WhiskeyRealism.Tactical
{
    public static class TacticalSupportScreen
    {
        public enum Result { Screened, Shaken, Unsupported, Unknown }

        public struct Input
        {
            public float ProtectedUnitMorale;
            public float MoraleFallbackThreshold;
            public float BattleStartMorale;
            public float EnemyDistance;
            public float DangerRadius;
            public int ScreenUnitCount;
            public int AiFeudStance;
            public int IsPlayerAiOrFeud;
        }

        public static Result Score(in Input input)
        {
            if (!TacticalGateHelpers.PassesWlOwnership(input.AiFeudStance, input.IsPlayerAiOrFeud))
                return Result.Unknown;
            if (input.BattleStartMorale < 0f)
                return Result.Unknown;

            bool enemyClose = input.EnemyDistance <= input.DangerRadius;
            bool screenPresent = input.ScreenUnitCount > 0;
            bool moraleSteady = input.ProtectedUnitMorale >= input.MoraleFallbackThreshold;

            if (screenPresent && moraleSteady) return Result.Screened;
            if (screenPresent && !moraleSteady) return Result.Shaken;
            if (enemyClose && !screenPresent) return Result.Unsupported;
            return Result.Screened;
        }
    }
}
```

- [ ] **Step 4: Add csproj entry**

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalSupportScreen.cs" Link="TacticalSupportScreen.cs" />
```

- [ ] **Step 5: Run tests; verify pass**

Expected: all five new tests pass; existing tests unaffected.

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Tactical/TacticalSupportScreen.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add TacticalSupportScreen scorer (Screened/Shaken/Unsupported/Unknown)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: TacticalDestinationDiscipline scorer

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalDestinationDiscipline.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

**Spec reference:** Section "TacticalDestinationDiscipline"; tier table by mover unittyp × peer unittyp.

- [ ] **Step 1: Write the failing tests**

```csharp
private static void TacticalDestinationDisciplineClearDestination()
{
    var input = new TacticalDestinationDiscipline.Input
    {
        MoverUnitTyp = 0,
        NearestSameTypePeerDistance = 9999f,
        NearestOtherCombatPeerDistance = 9999f,
        EnemyOnDestinationDistance = 9999f,
        MoverFireRange = 200f,
        MoverWidth = 50f,
        VanillaInterruptThreshold = 100f,
    };
    AssertEqual(TacticalDestinationDiscipline.Result.ClearDestination,
        TacticalDestinationDiscipline.Score(input), "clear");
}

private static void TacticalDestinationDisciplineGunCrowdedOnGun()
{
    // Scourge-style 5m tier for artillery-on-artillery
    var input = new TacticalDestinationDiscipline.Input
    {
        MoverUnitTyp = 2,
        PeerUnitTyp = 2,
        NearestSameTypePeerDistance = 4f,
        NearestOtherCombatPeerDistance = 9999f,
        EnemyOnDestinationDistance = 9999f,
        MoverFireRange = 1500f,
        MoverWidth = 30f,
        VanillaInterruptThreshold = 100f,
    };
    AssertEqual(TacticalDestinationDiscipline.Result.CrowdedSameType,
        TacticalDestinationDiscipline.Score(input), "gun on gun within 5m");
}

private static void TacticalDestinationDisciplineLineCrowdedOnLine()
{
    var input = new TacticalDestinationDiscipline.Input
    {
        MoverUnitTyp = 0,
        PeerUnitTyp = 0,
        NearestSameTypePeerDistance = 70f,   // inside fireRange/2 = 100 + width clamp
        NearestOtherCombatPeerDistance = 9999f,
        EnemyOnDestinationDistance = 9999f,
        MoverFireRange = 200f,
        MoverWidth = 50f,
        VanillaInterruptThreshold = 100f,
    };
    AssertEqual(TacticalDestinationDiscipline.Result.CrowdedSameType,
        TacticalDestinationDiscipline.Score(input), "line on line within firerange-scaled tier");
}

private static void TacticalDestinationDisciplineEnemyOnDestination()
{
    var input = new TacticalDestinationDiscipline.Input
    {
        MoverUnitTyp = 0,
        NearestSameTypePeerDistance = 9999f,
        NearestOtherCombatPeerDistance = 9999f,
        EnemyOnDestinationDistance = 50f,    // inside MoverFireRange
        MoverFireRange = 200f,
        MoverWidth = 50f,
        VanillaInterruptThreshold = 100f,
    };
    AssertEqual(TacticalDestinationDiscipline.Result.EnemyOnDestination,
        TacticalDestinationDiscipline.Score(input), "enemy on destination");
}

private static void TacticalDestinationDisciplinePathRiskUnknown()
{
    var input = new TacticalDestinationDiscipline.Input
    {
        MoverUnitTyp = 0,
        NearestSameTypePeerDistance = 9999f,
        NearestOtherCombatPeerDistance = 9999f,
        EnemyOnDestinationDistance = 9999f,
        MoverFireRange = -1f,                // sentinel: vanilla read failed
        MoverWidth = 50f,
        VanillaInterruptThreshold = 100f,
    };
    AssertEqual(TacticalDestinationDiscipline.Result.PathRiskUnknown,
        TacticalDestinationDiscipline.Score(input), "unknown on bad firerange");
}

private static void TacticalDestinationDisciplineSkirmisherInMotionSkipsCheck()
{
    var input = new TacticalDestinationDiscipline.Input
    {
        MoverUnitTyp = 0,
        PeerUnitTyp = 3,
        PeerHasActivePath = true,
        NearestSameTypePeerDistance = 9999f,
        NearestOtherCombatPeerDistance = 30f,    // close, but skirmisher in motion is exempt
        EnemyOnDestinationDistance = 9999f,
        MoverFireRange = 200f,
        MoverWidth = 50f,
        VanillaInterruptThreshold = 100f,
    };
    AssertEqual(TacticalDestinationDiscipline.Result.ClearDestination,
        TacticalDestinationDiscipline.Score(input), "skirmisher in motion exempt");
}
```

Register all six tests in `Main()`.

- [ ] **Step 2: Run; verify failure**

Expected: build error — `TacticalDestinationDiscipline` not defined.

- [ ] **Step 3: Implement `TacticalDestinationDiscipline.cs`**

```csharp
using System;

namespace WhiskeyRealism.Tactical
{
    public static class TacticalDestinationDiscipline
    {
        public enum Result
        {
            ClearDestination,
            CrowdedSameType,
            CrowdedAdjacent,
            EnemyOnDestination,
            PathRiskUnknown,
        }

        public struct Input
        {
            public int MoverUnitTyp;
            public int PeerUnitTyp;
            public bool PeerHasActivePath;
            public float NearestSameTypePeerDistance;
            public float NearestOtherCombatPeerDistance;
            public float EnemyOnDestinationDistance;
            public float MoverFireRange;
            public float MoverWidth;
            public float VanillaInterruptThreshold;
        }

        public static Result Score(in Input input)
        {
            if (input.MoverFireRange <= 0f) return Result.PathRiskUnknown;

            if (input.EnemyOnDestinationDistance <= input.MoverFireRange)
                return Result.EnemyOnDestination;

            // Skirmisher exemption when peer is type 3 AND in motion (Scourge offcmds.cpp:212).
            bool peerExempt = input.PeerUnitTyp == 3 && input.PeerHasActivePath;

            float sameTypeTier;
            if (input.MoverUnitTyp == 2)
            {
                sameTypeTier = 5f;
            }
            else
            {
                float scaled = Math.Max(input.MoverWidth, input.MoverFireRange * 0.5f);
                float clampMin = input.VanillaInterruptThreshold;
                float clampMax = 2f * input.MoverFireRange;
                sameTypeTier = Math.Max(clampMin, Math.Min(clampMax, scaled));
            }

            if (!peerExempt && input.NearestSameTypePeerDistance < sameTypeTier)
                return Result.CrowdedSameType;

            float adjacentTier = (input.MoverUnitTyp == 2) ? 5f : sameTypeTier;
            if (!peerExempt && input.NearestOtherCombatPeerDistance < adjacentTier)
                return Result.CrowdedAdjacent;

            return Result.ClearDestination;
        }
    }
}
```

- [ ] **Step 4: Add csproj entry**

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalDestinationDiscipline.cs" Link="TacticalDestinationDiscipline.cs" />
```

- [ ] **Step 5: Run; verify pass**

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Tactical/TacticalDestinationDiscipline.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add TacticalDestinationDiscipline scorer (unit-type tiered)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: TacticalMoraleSnapshotLedger

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalMoraleSnapshotLedger.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

**Spec reference:** Section "TacticalMoraleSnapshotLedger". Ring buffer N=4, identity = InstanceID + name fallback, in-memory only.

- [ ] **Step 1: Write the failing tests**

```csharp
private static void TacticalMoraleSnapshotLedgerStoresAndReads()
{
    var ledger = new TacticalMoraleSnapshotLedger(capacity: 4);
    var key = new TacticalMoraleSnapshotLedger.Key(unitInstanceId: 100, unitName: "1stVA");
    ledger.RecordSample(key, morale: 0.85f, timeFromStart: 10f);
    ledger.RecordSample(key, morale: 0.80f, timeFromStart: 20f);
    AssertTrue(ledger.TryGetLatest(key, out float morale, out float time), "has latest");
    AssertEqual(0.80f, morale, "latest morale");
    AssertEqual(20f, time, "latest time");
}

private static void TacticalMoraleSnapshotLedgerRingBufferDropsOldest()
{
    var ledger = new TacticalMoraleSnapshotLedger(capacity: 2);
    var key = new TacticalMoraleSnapshotLedger.Key(unitInstanceId: 100, unitName: "1stVA");
    ledger.RecordSample(key, morale: 0.9f, timeFromStart: 10f);
    ledger.RecordSample(key, morale: 0.8f, timeFromStart: 20f);
    ledger.RecordSample(key, morale: 0.7f, timeFromStart: 30f);
    AssertEqual(2, ledger.SampleCount(key), "buffer capped at 2");
    AssertTrue(ledger.TryGetOldestRetained(key, out float oldestMorale, out float oldestTime),
        "has oldest retained");
    AssertEqual(0.8f, oldestMorale, "10f sample dropped");
    AssertEqual(20f, oldestTime, "oldest retained time");
}

private static void TacticalMoraleSnapshotLedgerNameFallbackResolvesAcrossInstanceIdRoll()
{
    var ledger = new TacticalMoraleSnapshotLedger(capacity: 4);
    var oldKey = new TacticalMoraleSnapshotLedger.Key(unitInstanceId: 100, unitName: "1stVA");
    ledger.RecordSample(oldKey, morale: 0.9f, timeFromStart: 10f);
    var newKey = new TacticalMoraleSnapshotLedger.Key(unitInstanceId: 200, unitName: "1stVA");
    AssertTrue(ledger.TryGetLatest(newKey, out float morale, out _),
        "name fallback resolves after InstanceID roll");
    AssertEqual(0.9f, morale, "fallback returns prior sample");
}

private static void TacticalMoraleSnapshotLedgerSkipsWhenLastUpdateUnchanged()
{
    var ledger = new TacticalMoraleSnapshotLedger(capacity: 4);
    var key = new TacticalMoraleSnapshotLedger.Key(unitInstanceId: 100, unitName: "1stVA");
    bool firstWrote = ledger.RecordSampleIfNew(key, morale: 0.9f, timeFromStart: 10f, vanillaLastMoraleUpdate: 5f);
    bool secondWrote = ledger.RecordSampleIfNew(key, morale: 0.85f, timeFromStart: 11f, vanillaLastMoraleUpdate: 5f);
    AssertTrue(firstWrote, "first sample writes");
    AssertFalse(secondWrote, "skipped when vanilla timestamp unchanged");
    AssertEqual(1, ledger.SampleCount(key), "single sample");
}

private static void TacticalMoraleSnapshotLedgerPrune()
{
    var ledger = new TacticalMoraleSnapshotLedger(capacity: 4);
    var key = new TacticalMoraleSnapshotLedger.Key(unitInstanceId: 100, unitName: "1stVA");
    ledger.RecordSample(key, morale: 0.9f, timeFromStart: 10f);
    ledger.PruneRouted(key);
    AssertFalse(ledger.TryGetLatest(key, out _, out _), "prune removes entry");
}
```

Register all five.

- [ ] **Step 2: Run; verify failure**

- [ ] **Step 3: Implement `TacticalMoraleSnapshotLedger.cs`**

```csharp
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical
{
    public sealed class TacticalMoraleSnapshotLedger
    {
        public readonly struct Key
        {
            public readonly int InstanceId;
            public readonly string UnitName;
            public Key(int unitInstanceId, string unitName) { InstanceId = unitInstanceId; UnitName = unitName; }
        }

        private struct Sample { public float Morale; public float TimeFromStart; public float VanillaLastUpdate; }

        private readonly int capacity;
        private readonly Dictionary<int, List<Sample>> byInstanceId = new Dictionary<int, List<Sample>>();
        private readonly Dictionary<string, List<Sample>> byName = new Dictionary<string, List<Sample>>();

        public TacticalMoraleSnapshotLedger(int capacity) { this.capacity = capacity; }

        public void RecordSample(Key key, float morale, float timeFromStart)
        {
            RecordInternal(key, morale, timeFromStart, vanillaLastUpdate: timeFromStart);
        }

        public bool RecordSampleIfNew(Key key, float morale, float timeFromStart, float vanillaLastMoraleUpdate)
        {
            if (byInstanceId.TryGetValue(key.InstanceId, out var existing) &&
                existing.Count > 0 &&
                existing[existing.Count - 1].VanillaLastUpdate == vanillaLastMoraleUpdate)
            {
                return false;
            }
            RecordInternal(key, morale, timeFromStart, vanillaLastMoraleUpdate);
            return true;
        }

        private void RecordInternal(Key key, float morale, float timeFromStart, float vanillaLastUpdate)
        {
            if (!byInstanceId.TryGetValue(key.InstanceId, out var listById))
            {
                listById = new List<Sample>(capacity);
                byInstanceId[key.InstanceId] = listById;
            }
            listById.Add(new Sample { Morale = morale, TimeFromStart = timeFromStart, VanillaLastUpdate = vanillaLastUpdate });
            if (listById.Count > capacity) listById.RemoveAt(0);

            byName[key.UnitName] = listById;
        }

        public bool TryGetLatest(Key key, out float morale, out float timeFromStart)
        {
            if (byInstanceId.TryGetValue(key.InstanceId, out var listById) && listById.Count > 0)
            {
                var s = listById[listById.Count - 1];
                morale = s.Morale; timeFromStart = s.TimeFromStart; return true;
            }
            if (key.UnitName != null && byName.TryGetValue(key.UnitName, out var listByName) && listByName.Count > 0)
            {
                var s = listByName[listByName.Count - 1];
                morale = s.Morale; timeFromStart = s.TimeFromStart; return true;
            }
            morale = 0f; timeFromStart = 0f; return false;
        }

        public bool TryGetOldestRetained(Key key, out float morale, out float timeFromStart)
        {
            if (byInstanceId.TryGetValue(key.InstanceId, out var listById) && listById.Count > 0)
            {
                var s = listById[0];
                morale = s.Morale; timeFromStart = s.TimeFromStart; return true;
            }
            morale = 0f; timeFromStart = 0f; return false;
        }

        public int SampleCount(Key key)
            => byInstanceId.TryGetValue(key.InstanceId, out var listById) ? listById.Count : 0;

        public void PruneRouted(Key key)
        {
            byInstanceId.Remove(key.InstanceId);
            if (key.UnitName != null) byName.Remove(key.UnitName);
        }
    }
}
```

- [ ] **Step 4: Add csproj entry**

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalMoraleSnapshotLedger.cs" Link="TacticalMoraleSnapshotLedger.cs" />
```

- [ ] **Step 5: Run; verify pass**

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Tactical/TacticalMoraleSnapshotLedger.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add TacticalMoraleSnapshotLedger ring buffer with InstanceID+name keying

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: TacticalMoralePressure scorer

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalMoralePressure.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

**Spec reference:** Section "TacticalMoralePressure"; output ladder `Stable / UnderPressure / FallbackCandidate / WithdrawalCandidate / CollapseCandidate`. Uses outflanked tier (0-7), morale fallback predicate, and `battlestartmorale` baseline.

- [ ] **Step 1: Write the failing tests**

```csharp
private static void TacticalMoralePressureStable()
{
    var input = new TacticalMoralePressure.Input
    {
        CurrentMorale = 0.85f,
        BattleStartMorale = 0.90f,
        FallbackThreshold = 0.40f,
        Outflanked = 0,
        FriendlyRoutedNear = 0f,
        EnemyRoutedNear = 0f,
        ReceivedFireFromClosestFar = false,
        CoverValue = 0.5f,
        CoverObject = 0,
        AiFeudStance = -1,
        IsPlayerAiOrFeud = 0,
        BattleStartMoraleInitialized = true,
    };
    AssertEqual(TacticalMoralePressure.Result.Stable, TacticalMoralePressure.Score(input), "stable");
}

private static void TacticalMoralePressureUnderPressureFromOutflankedTier()
{
    var input = new TacticalMoralePressure.Input
    {
        CurrentMorale = 0.85f,
        BattleStartMorale = 0.90f,
        FallbackThreshold = 0.40f,
        Outflanked = 1,
        BattleStartMoraleInitialized = true,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalMoralePressure.Result.UnderPressure,
        TacticalMoralePressure.Score(input), "outflanked tier 1 → under pressure");
}

private static void TacticalMoralePressureFallbackCandidate()
{
    var input = new TacticalMoralePressure.Input
    {
        CurrentMorale = 0.45f,
        BattleStartMorale = 0.85f,
        FallbackThreshold = 0.40f,
        Outflanked = 0,
        ReceivedFireFromClosestFar = true,
        BattleStartMoraleInitialized = true,
        AiFeudStance = -1,
    };
    // current/(threshold*1.2) = 0.45/0.48 < 1 AND fired-on
    AssertEqual(TacticalMoralePressure.Result.FallbackCandidate,
        TacticalMoralePressure.Score(input), "fallback candidate");
}

private static void TacticalMoralePressureWithdrawalCandidateFlankNoCover()
{
    var input = new TacticalMoralePressure.Input
    {
        CurrentMorale = 0.45f,
        BattleStartMorale = 0.85f,
        FallbackThreshold = 0.40f,
        Outflanked = 4,
        ReceivedFireFromClosestFar = true,
        CoverValue = 0f,
        CoverObject = 3,
        BattleStartMoraleInitialized = true,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalMoralePressure.Result.WithdrawalCandidate,
        TacticalMoralePressure.Score(input), "flank tier 4 + no cover → withdrawal");
}

private static void TacticalMoralePressureCollapseCandidate()
{
    var input = new TacticalMoralePressure.Input
    {
        CurrentMorale = 0.30f,
        BattleStartMorale = 0.85f,
        FallbackThreshold = 0.40f,
        BattleStartMoraleInitialized = true,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalMoralePressure.Result.CollapseCandidate,
        TacticalMoralePressure.Score(input), "morale below threshold → collapse");
}

private static void TacticalMoralePressureUnknownOnUninitialized()
{
    var input = new TacticalMoralePressure.Input
    {
        CurrentMorale = 0.45f,
        BattleStartMorale = -1f,
        BattleStartMoraleInitialized = false,
        FallbackThreshold = 0.4f,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalMoralePressure.Result.Stable,
        TacticalMoralePressure.Score(input), "uninitialized → stable (caller separates)");
}
```

Register all six.

- [ ] **Step 2: Run; verify failure**

- [ ] **Step 3: Implement `TacticalMoralePressure.cs`**

```csharp
namespace WhiskeyRealism.Tactical
{
    public static class TacticalMoralePressure
    {
        public enum Result { Stable, UnderPressure, FallbackCandidate, WithdrawalCandidate, CollapseCandidate }

        public struct Input
        {
            public float CurrentMorale;
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
        }

        public static Result Score(in Input input)
        {
            if (!TacticalGateHelpers.PassesWlOwnership(input.AiFeudStance, input.IsPlayerAiOrFeud))
                return Result.Stable;
            if (!input.BattleStartMoraleInitialized || input.BattleStartMorale < 0f)
                return Result.Stable;

            // Collapse: matches vanilla MicroAICheckForRetreats predicate (decompile 4515).
            if (input.CurrentMorale < input.FallbackThreshold)
                return Result.CollapseCandidate;

            // Fallback: vanilla CheckLineFallbacks predicate at 5047 (morale < threshold * 1.2 + fired on).
            bool fallback = input.CurrentMorale < input.FallbackThreshold * 1.2f && input.ReceivedFireFromClosestFar;
            bool noCover = input.CoverValue <= 0f || input.CoverObject == 3;
            if (fallback && input.Outflanked > 0 && noCover)
                return Result.WithdrawalCandidate;
            if (fallback)
                return Result.FallbackCandidate;

            // Under pressure: outflanked tier ≥ 1, friendly rout near, or 10-20% drop from baseline.
            float drop = input.BattleStartMorale - input.CurrentMorale;
            bool moralePressure = drop >= input.BattleStartMorale * 0.10f;
            if (input.Outflanked >= 1 || input.FriendlyRoutedNear > 0f || moralePressure)
                return Result.UnderPressure;

            return Result.Stable;
        }
    }
}
```

- [ ] **Step 4: Add csproj entry**

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalMoralePressure.cs" Link="TacticalMoralePressure.cs" />
```

- [ ] **Step 5: Run; verify pass**

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Tactical/TacticalMoralePressure.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add TacticalMoralePressure scorer (Stable→Collapse ladder)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: TacticalHelpRequest scorer + sector ledger sink

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalHelpRequest.cs`
- Modify: `src/WhiskeyRealism/Tactical/TacticalSectorLedger.cs` (add `HelpRequest` field with setter)
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

**Spec reference:** Section "TacticalHelpRequest"; output `RequestReserveScreen / RequestLineRelief / RequestArtillerySupport / RequestMainEffortShift / NoRequest`. Sink: `TacticalSectorLedger.HelpRequest`.

- [ ] **Step 1: Read `TacticalSectorLedger.cs` to identify the existing field set**

Run: `Read /home/onebodyamerica/Projects/whiskey-realism-mod/src/WhiskeyRealism/Tactical/TacticalSectorLedger.cs`

- [ ] **Step 2: Write the failing tests**

```csharp
private static void TacticalHelpRequestNoRequestWhenSafe()
{
    var input = new TacticalHelpRequest.Input
    {
        SectorPressureRatio = 0.4f,
        OutflankedTierMax = 0,
        ArtilleryCounterBatteryNeeded = false,
        MainEffortStalled = false,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalHelpRequest.Decision.NoRequest,
        TacticalHelpRequest.Score(input), "no request when safe");
}

private static void TacticalHelpRequestReserveScreenOnFlank()
{
    var input = new TacticalHelpRequest.Input
    {
        SectorPressureRatio = 0.5f,
        OutflankedTierMax = 3,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalHelpRequest.Decision.RequestReserveScreen,
        TacticalHelpRequest.Score(input), "reserve screen on outflanked tier 3");
}

private static void TacticalHelpRequestLineReliefOnHighPressure()
{
    var input = new TacticalHelpRequest.Input
    {
        SectorPressureRatio = 1.4f,
        OutflankedTierMax = 0,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalHelpRequest.Decision.RequestLineRelief,
        TacticalHelpRequest.Score(input), "line relief on high pressure");
}

private static void TacticalHelpRequestArtillerySupport()
{
    var input = new TacticalHelpRequest.Input
    {
        SectorPressureRatio = 0.6f,
        OutflankedTierMax = 0,
        ArtilleryCounterBatteryNeeded = true,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalHelpRequest.Decision.RequestArtillerySupport,
        TacticalHelpRequest.Score(input), "artillery support");
}

private static void TacticalHelpRequestMainEffortShift()
{
    var input = new TacticalHelpRequest.Input
    {
        SectorPressureRatio = 0.8f,
        MainEffortStalled = true,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalHelpRequest.Decision.RequestMainEffortShift,
        TacticalHelpRequest.Score(input), "main effort shift");
}

private static void TacticalSectorLedgerStoresHelpRequest()
{
    var ledger = new TacticalSectorLedger();
    int sectorId = 5;
    ledger.SetHelpRequest(sectorId, TacticalHelpRequest.Decision.RequestLineRelief);
    AssertEqual(TacticalHelpRequest.Decision.RequestLineRelief,
        ledger.GetHelpRequest(sectorId), "sector ledger stores help request");
}
```

Register all six.

- [ ] **Step 3: Run; verify failure**

- [ ] **Step 4: Implement `TacticalHelpRequest.cs`**

```csharp
namespace WhiskeyRealism.Tactical
{
    public static class TacticalHelpRequest
    {
        public enum Decision
        {
            NoRequest,
            RequestReserveScreen,
            RequestLineRelief,
            RequestArtillerySupport,
            RequestMainEffortShift,
        }

        public struct Input
        {
            public float SectorPressureRatio;
            public int OutflankedTierMax;
            public bool ArtilleryCounterBatteryNeeded;
            public bool MainEffortStalled;
            public int AiFeudStance;
            public int IsPlayerAiOrFeud;
        }

        public static Decision Score(in Input input)
        {
            if (!TacticalGateHelpers.PassesWlOwnership(input.AiFeudStance, input.IsPlayerAiOrFeud))
                return Decision.NoRequest;

            if (input.OutflankedTierMax >= 3)
                return Decision.RequestReserveScreen;
            if (input.SectorPressureRatio >= 1.25f)
                return Decision.RequestLineRelief;
            if (input.ArtilleryCounterBatteryNeeded)
                return Decision.RequestArtillerySupport;
            if (input.MainEffortStalled)
                return Decision.RequestMainEffortShift;
            return Decision.NoRequest;
        }
    }
}
```

- [ ] **Step 5: Modify `TacticalSectorLedger.cs`**

Add (location: end of class body, before the closing brace):

```csharp
private readonly System.Collections.Generic.Dictionary<int, TacticalHelpRequest.Decision> helpRequests
    = new System.Collections.Generic.Dictionary<int, TacticalHelpRequest.Decision>();

public void SetHelpRequest(int sectorId, TacticalHelpRequest.Decision decision)
{
    helpRequests[sectorId] = decision;
}

public TacticalHelpRequest.Decision GetHelpRequest(int sectorId)
{
    return helpRequests.TryGetValue(sectorId, out var d) ? d : TacticalHelpRequest.Decision.NoRequest;
}
```

If `TacticalSectorLedger` is a static class, convert the field/methods to use a static `Dictionary` and static methods. Pattern-match to whichever shape the existing class uses.

- [ ] **Step 6: Add csproj entry**

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalHelpRequest.cs" Link="TacticalHelpRequest.cs" />
```

- [ ] **Step 7: Run; verify pass**

- [ ] **Step 8: Commit**

```bash
git add src/WhiskeyRealism/Tactical/TacticalHelpRequest.cs src/WhiskeyRealism/Tactical/TacticalSectorLedger.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add TacticalHelpRequest scorer with sector ledger sink

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: TacticalQuadrantThreatScorer

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalQuadrantThreatScorer.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

**Spec reference:** Section "TacticalQuadrantThreatScorer"; uses existing `enemystrengthwithinangle` slices; produces four-arc strength + dominant direction + rear-pressure flag.

- [ ] **Step 1: Write the failing tests**

```csharp
private static void TacticalQuadrantThreatScorerComputesArcs()
{
    // 36 slices (10 deg each), enemy concentrated in front (slice 0..8 + 27..35) and rear (9..27)
    var slices = new float[36];
    for (int i = 0; i < 9; i++) slices[i] = 10f;       // 0-90 (front-right area, depends on facing)
    for (int i = 27; i < 36; i++) slices[i] = 10f;     // 270-360 (front-left)
    var input = new TacticalQuadrantThreatScorer.Input
    {
        Slices = slices,
        SliceWidthDegrees = 10f,
        UnitFacingDegrees = 0f,
    };
    var output = TacticalQuadrantThreatScorer.Score(input);
    AssertTrue(output.FrontStrength > output.RearStrength, "front > rear when enemy is front");
    AssertEqual(TacticalQuadrantThreatScorer.Direction.Front, output.DominantDirection, "dominant = front");
    AssertFalse(output.RearPressureFlag, "no rear pressure");
}

private static void TacticalQuadrantThreatScorerDetectsRearPressure()
{
    var slices = new float[36];
    for (int i = 12; i < 24; i++) slices[i] = 50f;     // 120-240 (rear arc when facing 0)
    var input = new TacticalQuadrantThreatScorer.Input
    {
        Slices = slices,
        SliceWidthDegrees = 10f,
        UnitFacingDegrees = 0f,
    };
    var output = TacticalQuadrantThreatScorer.Score(input);
    AssertTrue(output.RearPressureFlag, "rear pressure when rear > front + max(L,R)");
    AssertEqual(TacticalQuadrantThreatScorer.Direction.Rear, output.DominantDirection, "dominant = rear");
}

private static void TacticalQuadrantThreatScorerNullSlicesDegradesGracefully()
{
    var input = new TacticalQuadrantThreatScorer.Input
    {
        Slices = null,
        SliceWidthDegrees = 10f,
        UnitFacingDegrees = 0f,
    };
    var output = TacticalQuadrantThreatScorer.Score(input);
    AssertEqual(0f, output.FrontStrength, "null slices → zero");
    AssertFalse(output.RearPressureFlag, "no flag");
}
```

Register all three.

- [ ] **Step 2: Run; verify failure**

- [ ] **Step 3: Implement `TacticalQuadrantThreatScorer.cs`**

```csharp
namespace WhiskeyRealism.Tactical
{
    public static class TacticalQuadrantThreatScorer
    {
        public enum Direction { Front, LeftFlank, RightFlank, Rear }

        public struct Input
        {
            public float[] Slices;
            public float SliceWidthDegrees;
            public float UnitFacingDegrees;
        }

        public struct Output
        {
            public float FrontStrength;
            public float LeftFlankStrength;
            public float RightFlankStrength;
            public float RearStrength;
            public Direction DominantDirection;
            public bool RearPressureFlag;
        }

        public static Output Score(in Input input)
        {
            var output = new Output();
            if (input.Slices == null || input.Slices.Length == 0 || input.SliceWidthDegrees <= 0f)
                return output;

            for (int i = 0; i < input.Slices.Length; i++)
            {
                float sliceCenter = i * input.SliceWidthDegrees + input.SliceWidthDegrees * 0.5f;
                float relative = sliceCenter - input.UnitFacingDegrees;
                while (relative < 0f) relative += 360f;
                while (relative >= 360f) relative -= 360f;

                if (relative < 45f || relative >= 315f) output.FrontStrength += input.Slices[i];
                else if (relative < 135f) output.RightFlankStrength += input.Slices[i];
                else if (relative < 225f) output.RearStrength += input.Slices[i];
                else output.LeftFlankStrength += input.Slices[i];
            }

            float maxFlank = output.LeftFlankStrength > output.RightFlankStrength
                ? output.LeftFlankStrength : output.RightFlankStrength;
            output.RearPressureFlag = output.RearStrength > output.FrontStrength + maxFlank;

            float top = output.FrontStrength;
            output.DominantDirection = Direction.Front;
            if (output.RearStrength > top) { top = output.RearStrength; output.DominantDirection = Direction.Rear; }
            if (output.LeftFlankStrength > top) { top = output.LeftFlankStrength; output.DominantDirection = Direction.LeftFlank; }
            if (output.RightFlankStrength > top) { output.DominantDirection = Direction.RightFlank; }

            return output;
        }
    }
}
```

- [ ] **Step 4: Add csproj entry**

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalQuadrantThreatScorer.cs" Link="TacticalQuadrantThreatScorer.cs" />
```

- [ ] **Step 5: Run; verify pass**

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Tactical/TacticalQuadrantThreatScorer.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add TacticalQuadrantThreatScorer (front/flanks/rear + RearPressureFlag)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: TacticalChargeViability

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalChargeViability.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

**Spec reference:** Section "TacticalChargeViability"; output `Refuse / Allow / Encourage`. Mirrors vanilla weighting + cooldown + morale gates.

- [ ] **Step 1: Write the failing tests**

```csharp
private static void TacticalChargeViabilityRefuseOnCooldown()
{
    var input = new TacticalChargeViability.Input
    {
        ChargeScore = 5f,
        ScoreThreshold = 1f,
        TargetMorale = 0.4f,
        TargetMoraleThreshold = 0.7f,
        TargetUnitTyp = 0,
        DistanceToTarget = 50f,
        MaxChargeRadius = 200f,
        TimeSinceLastCharge = 1f,
        ChargeCooldown = 5f,
        VolleyDwellRemaining = 0f,
        TargetOutflanked = 0,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalChargeViability.Result.Refuse,
        TacticalChargeViability.Score(input), "cooldown refuses");
}

private static void TacticalChargeViabilityRefuseOnMoraleHigh()
{
    var input = new TacticalChargeViability.Input
    {
        ChargeScore = 5f,
        ScoreThreshold = 1f,
        TargetMorale = 0.9f,
        TargetMoraleThreshold = 0.7f,
        TargetUnitTyp = 0,           // not artillery
        DistanceToTarget = 50f,
        MaxChargeRadius = 200f,
        TimeSinceLastCharge = 99f,
        ChargeCooldown = 5f,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalChargeViability.Result.Refuse,
        TacticalChargeViability.Score(input), "high target morale refuses");
}

private static void TacticalChargeViabilityAllowAtThreshold()
{
    var input = new TacticalChargeViability.Input
    {
        ChargeScore = 1.1f,
        ScoreThreshold = 1f,
        TargetMorale = 0.5f,
        TargetMoraleThreshold = 0.7f,
        TargetUnitTyp = 0,
        DistanceToTarget = 50f,
        MaxChargeRadius = 200f,
        TimeSinceLastCharge = 99f,
        ChargeCooldown = 5f,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalChargeViability.Result.Allow,
        TacticalChargeViability.Score(input), "score just above threshold → allow");
}

private static void TacticalChargeViabilityEncourageOnFlankedTarget()
{
    var input = new TacticalChargeViability.Input
    {
        ChargeScore = 2f,             // 2x threshold; > 25% margin
        ScoreThreshold = 1f,
        TargetMorale = 0.5f,
        TargetMoraleThreshold = 0.7f,
        TargetUnitTyp = 0,
        TargetOutflanked = 4,
        DistanceToTarget = 50f,
        MaxChargeRadius = 200f,
        TimeSinceLastCharge = 99f,
        ChargeCooldown = 5f,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalChargeViability.Result.Encourage,
        TacticalChargeViability.Score(input), "flanked target + high score → encourage");
}

private static void TacticalChargeViabilityArtilleryTargetIgnoresMoraleGate()
{
    var input = new TacticalChargeViability.Input
    {
        ChargeScore = 1.1f,
        ScoreThreshold = 1f,
        TargetMorale = 0.95f,
        TargetMoraleThreshold = 0.7f,
        TargetUnitTyp = 2,            // artillery — vanilla allows charge regardless of morale
        DistanceToTarget = 50f,
        MaxChargeRadius = 200f,
        TimeSinceLastCharge = 99f,
        ChargeCooldown = 5f,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalChargeViability.Result.Allow,
        TacticalChargeViability.Score(input), "artillery target bypasses morale gate");
}
```

Register all five.

- [ ] **Step 2: Run; verify failure**

- [ ] **Step 3: Implement `TacticalChargeViability.cs`**

```csharp
namespace WhiskeyRealism.Tactical
{
    public static class TacticalChargeViability
    {
        public enum Result { Refuse, Allow, Encourage }

        public struct Input
        {
            public float ChargeScore;
            public float ScoreThreshold;
            public float TargetMorale;
            public float TargetMoraleThreshold;
            public int TargetUnitTyp;
            public float DistanceToTarget;
            public float MaxChargeRadius;
            public float TimeSinceLastCharge;
            public float ChargeCooldown;
            public float VolleyDwellRemaining;
            public int TargetOutflanked;
            public int AiFeudStance;
            public int IsPlayerAiOrFeud;
        }

        public static Result Score(in Input input)
        {
            if (!TacticalGateHelpers.PassesWlOwnership(input.AiFeudStance, input.IsPlayerAiOrFeud))
                return Result.Refuse;
            if (input.TimeSinceLastCharge < input.ChargeCooldown) return Result.Refuse;
            if (input.DistanceToTarget > input.MaxChargeRadius) return Result.Refuse;
            if (input.VolleyDwellRemaining > 0f) return Result.Refuse;
            if (input.ChargeScore < input.ScoreThreshold) return Result.Refuse;

            // Vanilla artillery-charge bypass at decompile 5050 (charge if target is artillery OR target.morale < threshold).
            bool moralePass = input.TargetUnitTyp == 2
                || input.TargetMorale < input.TargetMoraleThreshold;
            if (!moralePass) return Result.Refuse;

            float margin = input.ChargeScore / input.ScoreThreshold;
            bool wideMargin = margin >= 1.25f;
            bool soft = input.TargetOutflanked >= 3 || input.TargetMorale < input.TargetMoraleThreshold * 0.5f;
            if (wideMargin && soft) return Result.Encourage;
            return Result.Allow;
        }
    }
}
```

- [ ] **Step 4: Add csproj entry**

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalChargeViability.cs" Link="TacticalChargeViability.cs" />
```

- [ ] **Step 5: Run; verify pass**

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Tactical/TacticalChargeViability.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add TacticalChargeViability scorer (Refuse/Allow/Encourage)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: TacticalRefuseFlankIntent

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalRefuseFlankIntent.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

**Spec reference:** Section "TacticalRefuseFlankIntent"; output `NoRefuse / RefuseLeft / RefuseRight`. Surface for `SetGroupFormation`'s `refuseflank` parameter; default-off.

- [ ] **Step 1: Write the failing tests**

```csharp
private static void TacticalRefuseFlankIntentNoRefuseWhenBalanced()
{
    var input = new TacticalRefuseFlankIntent.Input
    {
        LeftFlankStrength = 50f,
        RightFlankStrength = 50f,
        SectorPosture = TacticalRefuseFlankIntent.Posture.Defensive,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalRefuseFlankIntent.Decision.NoRefuse,
        TacticalRefuseFlankIntent.Score(input), "no refuse when balanced");
}

private static void TacticalRefuseFlankIntentRefuseLeftWhenLeftThreatened()
{
    var input = new TacticalRefuseFlankIntent.Input
    {
        LeftFlankStrength = 200f,
        RightFlankStrength = 50f,
        SectorPosture = TacticalRefuseFlankIntent.Posture.Defensive,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalRefuseFlankIntent.Decision.RefuseLeft,
        TacticalRefuseFlankIntent.Score(input), "refuse left under left pressure");
}

private static void TacticalRefuseFlankIntentRefuseRightWhenRightThreatened()
{
    var input = new TacticalRefuseFlankIntent.Input
    {
        LeftFlankStrength = 50f,
        RightFlankStrength = 200f,
        SectorPosture = TacticalRefuseFlankIntent.Posture.Defensive,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalRefuseFlankIntent.Decision.RefuseRight,
        TacticalRefuseFlankIntent.Score(input), "refuse right under right pressure");
}

private static void TacticalRefuseFlankIntentNoRefuseOnOffensivePosture()
{
    var input = new TacticalRefuseFlankIntent.Input
    {
        LeftFlankStrength = 200f,
        RightFlankStrength = 50f,
        SectorPosture = TacticalRefuseFlankIntent.Posture.Offensive,
        AiFeudStance = -1,
    };
    AssertEqual(TacticalRefuseFlankIntent.Decision.NoRefuse,
        TacticalRefuseFlankIntent.Score(input), "offensive posture suppresses refuse");
}
```

Register all four.

- [ ] **Step 2: Run; verify failure**

- [ ] **Step 3: Implement `TacticalRefuseFlankIntent.cs`**

```csharp
namespace WhiskeyRealism.Tactical
{
    public static class TacticalRefuseFlankIntent
    {
        public enum Decision { NoRefuse, RefuseLeft, RefuseRight }
        public enum Posture { Offensive, Defensive }

        public struct Input
        {
            public float LeftFlankStrength;
            public float RightFlankStrength;
            public Posture SectorPosture;
            public int AiFeudStance;
            public int IsPlayerAiOrFeud;
        }

        public static Decision Score(in Input input)
        {
            if (!TacticalGateHelpers.PassesWlOwnership(input.AiFeudStance, input.IsPlayerAiOrFeud))
                return Decision.NoRefuse;
            if (input.SectorPosture != Posture.Defensive) return Decision.NoRefuse;

            const float threatRatio = 2f;
            if (input.LeftFlankStrength > input.RightFlankStrength * threatRatio)
                return Decision.RefuseLeft;
            if (input.RightFlankStrength > input.LeftFlankStrength * threatRatio)
                return Decision.RefuseRight;
            return Decision.NoRefuse;
        }
    }
}
```

- [ ] **Step 4: Add csproj entry**

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalRefuseFlankIntent.cs" Link="TacticalRefuseFlankIntent.cs" />
```

- [ ] **Step 5: Run; verify pass**

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Tactical/TacticalRefuseFlankIntent.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add TacticalRefuseFlankIntent (NoRefuse/RefuseLeft/RefuseRight)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 10: TacticalFatigueState

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalFatigueState.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

**Spec reference:** Section "TacticalFatigueState"; output `Fresh / Tiring / Spent / Exhausted`.

- [ ] **Step 1: Write the failing tests**

```csharp
private static void TacticalFatigueStateBands()
{
    AssertEqual(TacticalFatigueState.Result.Fresh, TacticalFatigueState.Score(0.10f), "0.10 fresh");
    AssertEqual(TacticalFatigueState.Result.Fresh, TacticalFatigueState.Score(0.24f), "boundary < 0.25");
    AssertEqual(TacticalFatigueState.Result.Tiring, TacticalFatigueState.Score(0.25f), "0.25 tiring");
    AssertEqual(TacticalFatigueState.Result.Tiring, TacticalFatigueState.Score(0.54f), "boundary < 0.55");
    AssertEqual(TacticalFatigueState.Result.Spent, TacticalFatigueState.Score(0.55f), "0.55 spent");
    AssertEqual(TacticalFatigueState.Result.Spent, TacticalFatigueState.Score(0.79f), "boundary < 0.80");
    AssertEqual(TacticalFatigueState.Result.Exhausted, TacticalFatigueState.Score(0.80f), "0.80 exhausted");
    AssertEqual(TacticalFatigueState.Result.Exhausted, TacticalFatigueState.Score(1.00f), "1.00 exhausted");
}

private static void TacticalFatigueStateClampsBelow()
{
    AssertEqual(TacticalFatigueState.Result.Fresh, TacticalFatigueState.Score(-0.5f), "negative clamps fresh");
}

private static void TacticalFatigueStateClampsAbove()
{
    AssertEqual(TacticalFatigueState.Result.Exhausted, TacticalFatigueState.Score(2.0f), "above 1 clamps exhausted");
}
```

Register all three.

- [ ] **Step 2: Run; verify failure**

- [ ] **Step 3: Implement `TacticalFatigueState.cs`**

```csharp
namespace WhiskeyRealism.Tactical
{
    public static class TacticalFatigueState
    {
        public enum Result { Fresh, Tiring, Spent, Exhausted }

        public static Result Score(float fatigue)
        {
            if (fatigue < 0.25f) return Result.Fresh;
            if (fatigue < 0.55f) return Result.Tiring;
            if (fatigue < 0.80f) return Result.Spent;
            return Result.Exhausted;
        }
    }
}
```

- [ ] **Step 4: Add csproj entry**

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalFatigueState.cs" Link="TacticalFatigueState.cs" />
```

- [ ] **Step 5: Run; verify pass**

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Tactical/TacticalFatigueState.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add TacticalFatigueState (Fresh/Tiring/Spent/Exhausted bands)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 11: Build, deploy, hash-verify

**Files:**
- Build: `dist/WhiskeyRealism.dll`
- Deploy: `/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll`

Per AGENTS.md, every DLL-affecting change requires build, deploy, and SHA-256 hash match. The new types are compiled into the DLL even though they're not yet wired into Harmony patches.

**Pre-flight:** Confirm GTCW is closed before deploy (Windows holds an exclusive lock on loaded DLLs and the copy will fail with `cp: cannot create regular file ...: Invalid argument`).

- [ ] **Step 1: Run the test harness one more time**

Run: `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
Expected: every existing test plus all 47 new tests pass; no warnings; no errors.

- [ ] **Step 2: Build the production DLL**

Run: `./build.sh`
Expected: 0 warnings, 0 errors. Output at `dist/WhiskeyRealism.dll`.

- [ ] **Step 3: Confirm GTCW is closed**

Manually verify with the user before deploying. The user must close the game window if running.

- [ ] **Step 4: Deploy the DLL**

Run:
```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
```
Expected: silent success. If `cp: cannot create regular file ...: Invalid argument` appears, GTCW is still running — close it and retry.

- [ ] **Step 5: Verify deployed DLL matches built DLL**

Run:
```bash
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```
Expected: timestamps and sizes match; both SHA-256 hashes are identical.

- [ ] **Step 6: Update `docs/handoff.md`**

Add a "What just shipped" entry at the top noting:
- Slice B doctrine inputs landed: 9 pure scorers + 1 ledger + foundation helpers
- All harness tests pass (existing + 47 new)
- DLL hash matches (paste both hashes for the audit trail)
- No runtime wiring; B6c / B7 / B8 plans own the next step

- [ ] **Step 7: Commit handoff doc**

```bash
git add docs/handoff.md
git commit -m "$(cat <<'EOF'
docs(handoff): record Slice B doctrine inputs ship

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 8: Smoke-test prep (no in-game smoke yet)**

Because nothing in this plan calls the new scorers from a Harmony patch, a runtime smoke test cannot meaningfully exercise them — the vanilla AI never runs the new code. **Defer in-game smoke until B6c / B7 / B8 plans wire the scorers into runtime patches.** Document this explicitly in `docs/handoff.md`: "Pure-logic scorers shipped; runtime smoke deferred to wiring plans."

---

## Self-Review (run after writing the plan)

**Spec coverage check.** Tasks map to spec sections:

| Spec section | Implementing task |
|---|---|
| TacticalSupportScreen | Task 2 |
| TacticalDestinationDiscipline | Task 3 |
| TacticalMoraleSnapshotLedger | Task 4 |
| TacticalMoralePressure | Task 5 |
| TacticalHelpRequest + sector ledger sink | Task 6 |
| TacticalQuadrantThreatScorer | Task 7 |
| TacticalChargeViability | Task 8 |
| TacticalRefuseFlankIntent | Task 9 |
| TacticalFatigueState | Task 10 |
| Cross-cutting Gates (W&L gate, alliance bounds) | Task 1 (TacticalGateHelpers) |
| Tick budget cache | Task 1 (TacticalScoreCache) |
| Header pass continuation (constants, helper sigs, EArtyAmmo correction) | Reference-only — no implementation in this plan; consumed at runtime adapter time |
| Concepts Not Translatable | No implementation — explicit non-goals; nothing to do |
| Adversarial Review (correction list) | Already baked into the corrected spec sections; this plan implements the corrected versions |

**Spec sections not covered (deliberate deferrals):**
- Slice Integration (B6c / B7 / B8) — owned by separate plans per spec.
- Verification Expectations runtime smoke — deferred until wiring plans land.
- Reflection-based input adapters — owned by runtime wiring plans (B6c / B7 / B8).

**Placeholder scan:** No `TBD` / `TODO` / `fill in details` / "similar to Task N" — every code block is complete.

**Type consistency check:**
- `TacticalSupportScreen.Result` (Screened / Shaken / Unsupported / Unknown) — used consistently in tests and impl.
- `TacticalDestinationDiscipline.Result` (5 values) — consistent.
- `TacticalMoraleSnapshotLedger.Key` constructor signature `(int unitInstanceId, string unitName)` — consistent across tests.
- `TacticalHelpRequest.Decision` — used in both the scorer and `TacticalSectorLedger.SetHelpRequest` / `GetHelpRequest`.
- `TacticalQuadrantThreatScorer.Direction` (Front / LeftFlank / RightFlank / Rear) — consistent.
- `TacticalChargeViability.Result` (Refuse / Allow / Encourage) — consistent.
- `TacticalRefuseFlankIntent.Decision` (NoRefuse / RefuseLeft / RefuseRight) — consistent.
- `TacticalFatigueState.Result` (Fresh / Tiring / Spent / Exhausted) — consistent.
- `TacticalGateHelpers.PassesWlOwnership(int, int)` — consistent across all consumers.
- `TacticalScoreCache<T>.Key` struct — consistent.

No drift detected.

**Scope check:** All 11 tasks are in the same subsystem (pure tactical scorers). Cohesive plan; no decomposition needed.

---

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/archive/2026-05-08-scourge-tactical-adaptation-implementation.md`.**

Two execution options:

**1. Subagent-Driven (recommended)** — dispatch a fresh subagent per task, review between tasks, fast iteration. Required sub-skill: `superpowers:subagent-driven-development`.

**2. Inline Execution** — execute tasks in this session using `superpowers:executing-plans`, batch execution with checkpoints.

Worktree gate: per AGENTS.md operating rule 3, plan execution requires `superpowers:using-git-worktrees`. Confirm with the user whether the work happens in a fresh worktree or in the current main checkout before invoking either execution skill.
