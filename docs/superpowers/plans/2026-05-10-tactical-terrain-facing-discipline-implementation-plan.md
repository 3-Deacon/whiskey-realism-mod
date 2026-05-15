# Tactical Terrain And Facing Discipline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Current state:** implemented and merged to `main`. #60 was hash-deployed in DLL `b00e03bd7e635e981380459e09a0d52a19d635c22c49bd340b403dacfbdf4cf8` (841216 bytes; 717 PASS), now superseded by the current operations-ledger `main` DLL `f2e7705b96c55ea371ca08a3a56d28ebf324bfc114618c184ccba375d17ee1f1` (1027072 bytes; 893 PASS). Focused #60 runtime smoke is pending before this plan can be archived. Living runtime reference: [`docs/tactical-terrain-facing-discipline.md`](../../tactical-terrain-facing-discipline.md). Use that living doc for current config, deployed hash, smoke checklist, rollback, and post-implementation deltas.

**Goal:** Add terrain/facing evidence and default-off AI deployment discipline so tactical groups avoid water/weird deployment positions and face visible enemies more naturally without replacing vanilla pathfinding.

**Architecture:** Keep Grand Tactician's native NavMesh, formation placement, deployment-zone, and water-correction surfaces. Add pure, testable Whiskey scoring for terrain/facing decisions, then add runtime adapters and bounded Harmony patches that read vanilla state and only mutate AI deployment under explicit config. Facing behavior is applied where Whiskey owns the decision or deployment correction, not as a generic player/vanilla movement override.

**Tech Stack:** C# netstandard2.1 BepInEx plugin, HarmonyX patches, Unity/Grand Tactician runtime adapters, existing net8 console test harness.

---

## Preconditions

- Work from an isolated worktree before executing code changes. This planning session was run in the main checkout because the design spec was untracked there; implementation should begin by using `superpowers:using-git-worktrees`.
- Re-link `refs` in the implementation worktree with `ln -s ../../refs refs` if the new worktree does not inherit the main checkout's ignored symlink.
- Read `src/WhiskeyRealism/Tactical/AGENTS.md`, `src/WhiskeyRealism/Patches/AGENTS.md`, and `tests/WhiskeyRealism.Tests/AGENTS.md` before editing those subtrees.
- Keep the design spec open: `docs/superpowers/specs/2026-05-10-tactical-terrain-facing-discipline-design.md`.

## Vanilla Anchors Confirmed For This Plan

Use these exact anchors when implementing or reviewing the patch surfaces:

- `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:168982-169019` — `BattleUI.CheckPathSetting()` raycasts `NavTarget` and creates `SetWaypointData(... manualfinalrotation: -1f ...)`.
- `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:91304-91323` — `BattleUnits.SetWaypoint(...)` group deployment mode clamps group move targets through `frontline2.GetClosestPointInDeploymentZone(...)`, then calls `RegimentSetPath(...)` and immediate movement.
- `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:91453-91560` — deployment mode bypasses normal order delay, directly positions land units or calls `SetGroupFormation(...)`, then calls water and deployment-zone checks.
- `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:130259-130527` — `Regiment.AddPath(...)` owns NavMesh area costs, calls `NavMesh.CalculatePath(...)`, returns failure on partial/invalid/empty paths, and requires exact endpoint equality.
- `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:131073-131128` — `Regiment.RegimentSetPath(...)` clamps tactical targets off terrain id `4` only, then sets target height.
- `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:131167-131188` — `RegimentSetPath(...)` retry loop can step from failed last corner with `Vector3.MoveTowards(..., -0.5f)`, which is already guarded by Whiskey #53.
- `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:131211-131260` — final waypoint rotation uses `manualfinalrotation` when supplied, otherwise `GetLastWaypointAngle()`.
- `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:85524-85872` — `BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew(int foralliance)` skips player side outside AI-vs-AI, picks deployment positions, sometimes derives `manualfinalrotation` from enemy evidence, calls `SetGroupFormation(... immediateplacement: true ...)`, then `MoveAllUnitsIntoDeploymentZone()` and `RestrictTerrainPosition()`.
- `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:131303-131358` — `Regiment.SetNewPosition(...)` sets position/facing and then runs deployment-zone and water checks for land units.
- `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:131432-131556` — `CheckIfPositionIsOnWater(...)` and `CheckIfPositionIsOnWaterBlocks(...)` only move center/block points off terrain id `4` in a chosen direction.
- `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:25574-25605` — `BattlefieldSetup.CheckIfFinalWaypointIsOnTerrain(...)` walks a final point backward until it is not the searched terrain id.
- `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:26727-26770` — `BattlefieldSetup.GetCurrentTerrainOnPos(...)` reads terrain ids from `terrainspecs`.
- `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:27002-27040` — `BattlefieldSetup.GetTerrainHeight(...)` samples terrain/NavMesh height and floors to sea level.
- `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:27638-27675` — `BattlefieldSetup.CheckTerrainLine(...)` samples terrain ids along a line.
- `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:122545-122935` — `Regiment.UpdateUnitRangeFast(...)` builds visible enemy lists with fog checks, but closest-enemy fields are assigned outside that same visible-list guard, so closest-enemy fields alone are not enough for Whiskey facing decisions.

## Files

- Create: `src/WhiskeyRealism/Tactical/TacticalTerrainFacingDiscipline.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalTerrainFacingTelemetry.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalTerrainProbe.cs`
- Create: `src/WhiskeyRealism/Patches/TacticalDeploymentTerrainDisciplinePatch.cs`
- Modify: `src/WhiskeyRealism/Tactical/TacticalDeploymentTelemetry.cs`
- Modify: `src/WhiskeyRealism/Patches/TacticalDeploymentObserverPatch.cs`
- Modify: `src/WhiskeyRealism/Plugin.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `docs/patch-catalog.md`
- Modify: `docs/handoff.md`

## Task 1: Add Pure Terrain/Facing Decision Types

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalTerrainFacingDiscipline.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`

- [ ] **Step 1: Add the pure model file**

Create `src/WhiskeyRealism/Tactical/TacticalTerrainFacingDiscipline.cs` with these types. Keep it free of Unity, Harmony, and vanilla game references.

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WhiskeyRealism.Tactical
{
    public enum TacticalTerrainDecisionReason
    {
        Accepted,
        VanillaKept,
        NonFiniteCandidate,
        WaterCenter,
        WaterFootprint,
        OutsideDeploymentZone,
        ExcessiveCorrectionDistance,
        MissingVisibleEnemy,
        NoSafeCandidate
    }

    public readonly struct TacticalPoint2
    {
        public TacticalPoint2(float x, float z)
        {
            X = Sanitize(x);
            Z = Sanitize(z);
        }

        public float X { get; }
        public float Z { get; }
        public bool IsFinite => IsFiniteValue(X) && IsFiniteValue(Z);

        public float DistanceTo(TacticalPoint2 other)
        {
            float dx = X - other.X;
            float dz = Z - other.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? float.NaN : value;
        }

        private static bool IsFiniteValue(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public readonly struct TacticalTerrainSample
    {
        public TacticalTerrainSample(int terrainId, bool isWater, bool isInsideDeploymentZone, bool known = true)
        {
            TerrainId = terrainId;
            IsWater = isWater;
            IsInsideDeploymentZone = isInsideDeploymentZone;
            Known = known;
        }

        public int TerrainId { get; }
        public bool IsWater { get; }
        public bool IsInsideDeploymentZone { get; }
        public bool Known { get; }

        public static TacticalTerrainSample Unknown => new TacticalTerrainSample(-1, false, true, known: false);
    }

    public readonly struct TacticalEnemyBearingEvidence
    {
        public TacticalEnemyBearingEvidence(bool visible, float bearingDegrees, float distanceMeters, float strength)
        {
            Visible = visible;
            BearingDegrees = NormalizeAngle(bearingDegrees);
            DistanceMeters = SanitizeNonNegative(distanceMeters);
            Strength = SanitizeNonNegative(strength);
        }

        public bool Visible { get; }
        public float BearingDegrees { get; }
        public float DistanceMeters { get; }
        public float Strength { get; }

        private static float SanitizeNonNegative(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value < 0f ? 0f : value;
        }

        internal static float NormalizeAngle(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            while (value < 0f) value += 360f;
            while (value >= 360f) value -= 360f;
            return value;
        }
    }

    public readonly struct TacticalTerrainCandidate
    {
        public TacticalTerrainCandidate(
            TacticalPoint2 point,
            float facingDegrees,
            TacticalTerrainSample center,
            IEnumerable<TacticalTerrainSample> footprint)
        {
            Point = point;
            FacingDegrees = TacticalEnemyBearingEvidence.NormalizeAngle(facingDegrees);
            Center = center;
            Footprint = (footprint ?? Array.Empty<TacticalTerrainSample>())
                .Where(s => s.Known)
                .ToArray();
        }

        public TacticalPoint2 Point { get; }
        public float FacingDegrees { get; }
        public TacticalTerrainSample Center { get; }
        public IReadOnlyList<TacticalTerrainSample> Footprint { get; }
    }

    public readonly struct TacticalTerrainRules
    {
        public TacticalTerrainRules(float maxCorrectionMeters, float preferredFacingDeltaDegrees, bool requireDeploymentZone, bool requireVisibleEnemyForFacing)
        {
            MaxCorrectionMeters = ClampPositive(maxCorrectionMeters, 60f);
            PreferredFacingDeltaDegrees = ClampPositive(preferredFacingDeltaDegrees, 90f);
            RequireDeploymentZone = requireDeploymentZone;
            RequireVisibleEnemyForFacing = requireVisibleEnemyForFacing;
        }

        public float MaxCorrectionMeters { get; }
        public float PreferredFacingDeltaDegrees { get; }
        public bool RequireDeploymentZone { get; }
        public bool RequireVisibleEnemyForFacing { get; }

        public static TacticalTerrainRules DeploymentDefault =>
            new TacticalTerrainRules(60f, 90f, requireDeploymentZone: true, requireVisibleEnemyForFacing: false);

        private static float ClampPositive(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f) return fallback;
            return value;
        }
    }

    public readonly struct TacticalTerrainDecision
    {
        public TacticalTerrainDecision(
            bool accepted,
            TacticalTerrainDecisionReason reason,
            TacticalTerrainCandidate candidate,
            float correctionDistance,
            float facingDelta)
        {
            Accepted = accepted;
            Reason = reason;
            Candidate = candidate;
            CorrectionDistance = Sanitize(correctionDistance);
            FacingDelta = Sanitize(facingDelta);
        }

        public bool Accepted { get; }
        public TacticalTerrainDecisionReason Reason { get; }
        public TacticalTerrainCandidate Candidate { get; }
        public float CorrectionDistance { get; }
        public float FacingDelta { get; }

        public string Signature =>
            "accepted=" + Accepted.ToString(CultureInfo.InvariantCulture).ToLowerInvariant() +
            "|reason=" + Reason +
            "|dist=" + Bucket(CorrectionDistance) +
            "|faceDelta=" + Bucket(FacingDelta);

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private static string Bucket(float value)
        {
            return (Math.Round(Sanitize(value) / 5f) * 5f).ToString("0", CultureInfo.InvariantCulture);
        }
    }

    public static class TacticalTerrainFacingDiscipline
    {
        public static TacticalTerrainDecision Choose(
            TacticalPoint2 vanillaPoint,
            float vanillaFacingDegrees,
            IEnumerable<TacticalTerrainCandidate> candidates,
            TacticalEnemyBearingEvidence enemy,
            TacticalTerrainRules rules)
        {
            var best = default(TacticalTerrainCandidate);
            float bestScore = float.MinValue;
            float bestDistance = 0f;
            float bestFacingDelta = 0f;
            bool found = false;

            foreach (var candidate in candidates ?? Array.Empty<TacticalTerrainCandidate>())
            {
                var rejection = Reject(vanillaPoint, candidate, enemy, rules, out float distance, out float facingDelta);
                if (rejection != TacticalTerrainDecisionReason.Accepted)
                    continue;

                float score = Score(distance, facingDelta, rules, enemy);
                if (!found || score > bestScore)
                {
                    found = true;
                    best = candidate;
                    bestScore = score;
                    bestDistance = distance;
                    bestFacingDelta = facingDelta;
                }
            }

            if (!found)
            {
                var kept = new TacticalTerrainCandidate(
                    vanillaPoint,
                    vanillaFacingDegrees,
                    TacticalTerrainSample.Unknown,
                    Array.Empty<TacticalTerrainSample>());
                return new TacticalTerrainDecision(false, TacticalTerrainDecisionReason.NoSafeCandidate, kept, 0f, 0f);
            }

            return new TacticalTerrainDecision(true, TacticalTerrainDecisionReason.Accepted, best, bestDistance, bestFacingDelta);
        }

        public static TacticalTerrainDecisionReason Reject(
            TacticalPoint2 vanillaPoint,
            TacticalTerrainCandidate candidate,
            TacticalEnemyBearingEvidence enemy,
            TacticalTerrainRules rules,
            out float correctionDistance,
            out float facingDelta)
        {
            correctionDistance = 0f;
            facingDelta = 0f;

            if (!candidate.Point.IsFinite)
                return TacticalTerrainDecisionReason.NonFiniteCandidate;

            correctionDistance = vanillaPoint.DistanceTo(candidate.Point);
            if (correctionDistance > rules.MaxCorrectionMeters)
                return TacticalTerrainDecisionReason.ExcessiveCorrectionDistance;

            if (candidate.Center.Known && candidate.Center.IsWater)
                return TacticalTerrainDecisionReason.WaterCenter;

            if (candidate.Footprint.Any(s => s.IsWater))
                return TacticalTerrainDecisionReason.WaterFootprint;

            if (rules.RequireDeploymentZone && candidate.Center.Known && !candidate.Center.IsInsideDeploymentZone)
                return TacticalTerrainDecisionReason.OutsideDeploymentZone;

            if (rules.RequireVisibleEnemyForFacing && !enemy.Visible)
                return TacticalTerrainDecisionReason.MissingVisibleEnemy;

            facingDelta = enemy.Visible ? AngleDelta(candidate.FacingDegrees, enemy.BearingDegrees) : 0f;
            return TacticalTerrainDecisionReason.Accepted;
        }

        public static float AngleDelta(float a, float b)
        {
            float delta = Math.Abs(TacticalEnemyBearingEvidence.NormalizeAngle(a) - TacticalEnemyBearingEvidence.NormalizeAngle(b));
            return delta > 180f ? 360f - delta : delta;
        }

        private static float Score(float distance, float facingDelta, TacticalTerrainRules rules, TacticalEnemyBearingEvidence enemy)
        {
            float score = 1000f - distance;
            if (enemy.Visible)
            {
                score += Math.Max(0f, rules.PreferredFacingDeltaDegrees - facingDelta) * 2f;
                score += Math.Min(5000f, enemy.Strength) / 100f;
            }
            return score;
        }
    }
}
```

- [ ] **Step 2: Include the new pure file in the test project**

Add this compile include near the other tactical includes in `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalTerrainFacingDiscipline.cs" Link="TacticalTerrainFacingDiscipline.cs" />
```

- [ ] **Step 3: Run the harness and verify compilation fails only because tests are not registered yet**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: existing tests compile and run. If compilation fails for `TacticalTerrainFacingDiscipline.cs`, fix type or namespace errors before continuing.

- [ ] **Step 4: Commit Task 1**

```bash
git add src/WhiskeyRealism/Tactical/TacticalTerrainFacingDiscipline.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "feat(tactical): add terrain facing discipline model"
```

## Task 2: Add Pure Tests For Candidate Rejection And Facing Preference

**Files:**
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Register test cases**

Add these names to the test tuple list near the existing tactical deployment telemetry tests:

```csharp
("tactical terrain rejects water center", TacticalTerrainRejectsWaterCenter),
("tactical terrain rejects water footprint", TacticalTerrainRejectsWaterFootprint),
("tactical terrain rejects outside deployment zone", TacticalTerrainRejectsOutsideDeploymentZone),
("tactical terrain picks closest safe candidate", TacticalTerrainPicksClosestSafeCandidate),
("tactical terrain prefers visible enemy facing", TacticalTerrainPrefersVisibleEnemyFacing),
("tactical terrain no safe candidate keeps vanilla", TacticalTerrainNoSafeCandidateKeepsVanilla),
("tactical terrain missing visible enemy rejects when required", TacticalTerrainMissingVisibleEnemyRejectsWhenRequired),
```

- [ ] **Step 2: Add test helper methods**

Add these helpers near the existing tactical helper methods in `Program.cs`:

```csharp
private static TacticalTerrainCandidate TerrainCandidate(
    float x,
    float z,
    float facing,
    bool centerWater = false,
    bool footprintWater = false,
    bool insideZone = true)
{
    return new TacticalTerrainCandidate(
        new TacticalPoint2(x, z),
        facing,
        new TacticalTerrainSample(centerWater ? 4 : 0, centerWater, insideZone),
        new[]
        {
            new TacticalTerrainSample(0, false, insideZone),
            new TacticalTerrainSample(footprintWater ? 4 : 0, footprintWater, insideZone)
        });
}

private static TacticalEnemyBearingEvidence VisibleEnemy(float bearing = 90f, float distance = 600f, float strength = 1200f)
{
    return new TacticalEnemyBearingEvidence(true, bearing, distance, strength);
}
```

- [ ] **Step 3: Add the test methods**

Add these methods to `Program.cs`:

```csharp
private static void TacticalTerrainRejectsWaterCenter()
{
    var decision = TacticalTerrainFacingDiscipline.Choose(
        new TacticalPoint2(100f, 100f),
        0f,
        new[] { TerrainCandidate(100f, 100f, 90f, centerWater: true) },
        VisibleEnemy(),
        TacticalTerrainRules.DeploymentDefault);

    AssertFalse(decision.Accepted, "water center should not be accepted");
    AssertEqual(TacticalTerrainDecisionReason.NoSafeCandidate, decision.Reason, "no accepted candidates");
}

private static void TacticalTerrainRejectsWaterFootprint()
{
    var decision = TacticalTerrainFacingDiscipline.Choose(
        new TacticalPoint2(100f, 100f),
        0f,
        new[] { TerrainCandidate(100f, 100f, 90f, footprintWater: true) },
        VisibleEnemy(),
        TacticalTerrainRules.DeploymentDefault);

    AssertFalse(decision.Accepted, "water footprint should not be accepted");
}

private static void TacticalTerrainRejectsOutsideDeploymentZone()
{
    var decision = TacticalTerrainFacingDiscipline.Choose(
        new TacticalPoint2(100f, 100f),
        0f,
        new[] { TerrainCandidate(105f, 100f, 90f, insideZone: false) },
        VisibleEnemy(),
        TacticalTerrainRules.DeploymentDefault);

    AssertFalse(decision.Accepted, "outside deployment zone should not be accepted");
}

private static void TacticalTerrainPicksClosestSafeCandidate()
{
    var decision = TacticalTerrainFacingDiscipline.Choose(
        new TacticalPoint2(100f, 100f),
        0f,
        new[]
        {
            TerrainCandidate(140f, 100f, 90f),
            TerrainCandidate(110f, 100f, 90f)
        },
        VisibleEnemy(),
        TacticalTerrainRules.DeploymentDefault);

    AssertTrue(decision.Accepted, "safe candidate should be accepted");
    AssertNear(110f, decision.Candidate.Point.X, 0.01f, "closest x");
}

private static void TacticalTerrainPrefersVisibleEnemyFacing()
{
    var decision = TacticalTerrainFacingDiscipline.Choose(
        new TacticalPoint2(100f, 100f),
        0f,
        new[]
        {
            TerrainCandidate(110f, 100f, 270f),
            TerrainCandidate(111f, 100f, 90f)
        },
        VisibleEnemy(bearing: 90f),
        TacticalTerrainRules.DeploymentDefault);

    AssertTrue(decision.Accepted, "visible enemy candidate should be accepted");
    AssertNear(90f, decision.Candidate.FacingDegrees, 0.01f, "enemy-facing candidate");
}

private static void TacticalTerrainNoSafeCandidateKeepsVanilla()
{
    var decision = TacticalTerrainFacingDiscipline.Choose(
        new TacticalPoint2(100f, 100f),
        45f,
        new[]
        {
            TerrainCandidate(200f, 100f, 90f),
            TerrainCandidate(100f, 100f, 90f, centerWater: true)
        },
        VisibleEnemy(),
        TacticalTerrainRules.DeploymentDefault);

    AssertFalse(decision.Accepted, "unsafe candidates should not be accepted");
    AssertEqual(TacticalTerrainDecisionReason.NoSafeCandidate, decision.Reason, "reason");
    AssertNear(45f, decision.Candidate.FacingDegrees, 0.01f, "vanilla facing preserved");
}

private static void TacticalTerrainMissingVisibleEnemyRejectsWhenRequired()
{
    var rules = new TacticalTerrainRules(60f, 90f, requireDeploymentZone: true, requireVisibleEnemyForFacing: true);
    var decision = TacticalTerrainFacingDiscipline.Choose(
        new TacticalPoint2(100f, 100f),
        0f,
        new[] { TerrainCandidate(100f, 100f, 90f) },
        new TacticalEnemyBearingEvidence(false, 0f, 0f, 0f),
        rules);

    AssertFalse(decision.Accepted, "missing visible enemy should reject when required");
}
```

- [ ] **Step 4: Run tests and verify the new cases pass**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass. The exact pass count will increase by 7 from the baseline on the implementation branch.

- [ ] **Step 5: Commit Task 2**

```bash
git add tests/WhiskeyRealism.Tests/Program.cs
git commit -m "test(tactical): cover terrain facing candidate decisions"
```

## Task 3: Add Terrain/Facing Telemetry Formatting

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalTerrainFacingTelemetry.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add telemetry formatter**

Create `src/WhiskeyRealism/Tactical/TacticalTerrainFacingTelemetry.cs`:

```csharp
using System;
using System.Globalization;

namespace WhiskeyRealism.Tactical
{
    public readonly struct TacticalTerrainFacingLogRow
    {
        public TacticalTerrainFacingLogRow(
            string surface,
            string phase,
            int alliance,
            string unit,
            int terrainId,
            bool centerWater,
            bool footprintWater,
            bool insideDeploymentZone,
            float facing,
            float enemyBearing,
            float enemyDistance,
            TacticalTerrainDecision decision)
        {
            Surface = Safe(surface);
            Phase = Safe(phase);
            Alliance = alliance;
            Unit = Safe(unit);
            TerrainId = terrainId;
            CenterWater = centerWater;
            FootprintWater = footprintWater;
            InsideDeploymentZone = insideDeploymentZone;
            Facing = Sanitize(facing);
            EnemyBearing = Sanitize(enemyBearing);
            EnemyDistance = Sanitize(enemyDistance);
            Decision = decision;
        }

        public string Surface { get; }
        public string Phase { get; }
        public int Alliance { get; }
        public string Unit { get; }
        public int TerrainId { get; }
        public bool CenterWater { get; }
        public bool FootprintWater { get; }
        public bool InsideDeploymentZone { get; }
        public float Facing { get; }
        public float EnemyBearing { get; }
        public float EnemyDistance { get; }
        public TacticalTerrainDecision Decision { get; }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Replace(' ', '_');
        }
    }

    public static class TacticalTerrainFacingTelemetry
    {
        public static string Format(TacticalTerrainFacingLogRow row)
        {
            return "[TacDeployTerrain]" +
                   " surface=" + row.Surface +
                   " phase=" + row.Phase +
                   " alliance=" + row.Alliance +
                   " unit=" + row.Unit +
                   " terrain=" + row.TerrainId +
                   " centerWater=" + Bool(row.CenterWater) +
                   " footprintWater=" + Bool(row.FootprintWater) +
                   " inZone=" + Bool(row.InsideDeploymentZone) +
                   " facing=" + Float(row.Facing) +
                   " enemyBearing=" + Float(row.EnemyBearing) +
                   " enemyDistance=" + Float(row.EnemyDistance) +
                   " decision=" + row.Decision.Reason +
                   " accepted=" + Bool(row.Decision.Accepted) +
                   " signature=" + row.Decision.Signature;
        }

        private static string Bool(bool value) => value ? "true" : "false";

        private static string Float(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0.0";
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
```

- [ ] **Step 2: Include telemetry file in tests**

Add this compile include in `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalTerrainFacingTelemetry.cs" Link="TacticalTerrainFacingTelemetry.cs" />
```

- [ ] **Step 3: Register and add telemetry tests**

Register:

```csharp
("tactical terrain telemetry formats bounded row", TacticalTerrainTelemetryFormatsBoundedRow),
```

Add:

```csharp
private static void TacticalTerrainTelemetryFormatsBoundedRow()
{
    var candidate = TerrainCandidate(100f, 100f, 90f);
    var decision = new TacticalTerrainDecision(
        true,
        TacticalTerrainDecisionReason.Accepted,
        candidate,
        correctionDistance: 10f,
        facingDelta: 5f);

    string line = TacticalTerrainFacingTelemetry.Format(new TacticalTerrainFacingLogRow(
        "DoPlacementAIUnitsWithinDeploymentzoneNew",
        TacticalDeploymentTelemetry.PhaseInitial,
        1,
        "Test Division",
        0,
        centerWater: false,
        footprintWater: false,
        insideDeploymentZone: true,
        facing: 90f,
        enemyBearing: 95f,
        enemyDistance: 600f,
        decision));

    AssertContains(line, "[TacDeployTerrain]", "marker");
    AssertContains(line, "surface=DoPlacementAIUnitsWithinDeploymentzoneNew", "surface");
    AssertContains(line, "unit=Test_Division", "safe unit");
    AssertContains(line, "centerWater=false", "center water");
    AssertContains(line, "decision=Accepted", "reason");
    AssertContains(line, "accepted=true", "accepted");
}
```

- [ ] **Step 4: Run tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 5: Commit Task 3**

```bash
git add src/WhiskeyRealism/Tactical/TacticalTerrainFacingTelemetry.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat(tactical): format terrain facing telemetry"
```

## Task 4: Extend Deployment Snapshots With Terrain/Facing Fields

**Files:**
- Modify: `src/WhiskeyRealism/Tactical/TacticalDeploymentTelemetry.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Extend `TacticalDeploymentGroupSnapshot` constructor without breaking existing tests**

Modify the constructor signature to add optional values after `active`:

```csharp
bool active,
int terrainId = -1,
bool centerWater = false,
bool footprintWater = false,
bool insideDeploymentZone = true,
float facing = 0f,
float nearestVisibleEnemyBearing = 0f,
float nearestVisibleEnemyDistance = 0f)
```

Set new properties in the constructor:

```csharp
TerrainId = terrainId;
CenterWater = centerWater;
FootprintWater = footprintWater;
InsideDeploymentZone = insideDeploymentZone;
Facing = Sanitize(facing);
NearestVisibleEnemyBearing = Sanitize(nearestVisibleEnemyBearing);
NearestVisibleEnemyDistance = Sanitize(nearestVisibleEnemyDistance);
```

Add properties:

```csharp
public int TerrainId { get; }
public bool CenterWater { get; }
public bool FootprintWater { get; }
public bool InsideDeploymentZone { get; }
public float Facing { get; }
public float NearestVisibleEnemyBearing { get; }
public float NearestVisibleEnemyDistance { get; }
public bool HasTerrainEvidence => TerrainId >= 0 || CenterWater || FootprintWater;
public bool HasVisibleEnemyBearing => NearestVisibleEnemyDistance > 0f;
```

- [ ] **Step 2: Add a terrain/facing unit test**

Register:

```csharp
("tactical deployment snapshot carries terrain facing evidence", TacticalDeploymentSnapshotCarriesTerrainFacingEvidence),
```

Add:

```csharp
private static void TacticalDeploymentSnapshotCarriesTerrainFacingEvidence()
{
    var group = new TacticalDeploymentGroupSnapshot(
        "key",
        "Unit Name",
        1,
        15,
        10f,
        20f,
        1,
        1,
        0,
        routed: false,
        active: true,
        terrainId: 4,
        centerWater: true,
        footprintWater: false,
        insideDeploymentZone: false,
        facing: 180f,
        nearestVisibleEnemyBearing: 175f,
        nearestVisibleEnemyDistance: 500f);

    AssertEqual(4, group.TerrainId, "terrain id");
    AssertTrue(group.CenterWater, "center water");
    AssertFalse(group.InsideDeploymentZone, "zone");
    AssertTrue(group.HasTerrainEvidence, "terrain evidence");
    AssertTrue(group.HasVisibleEnemyBearing, "enemy bearing evidence");
    AssertNear(180f, group.Facing, 0.01f, "facing");
}
```

- [ ] **Step 3: Run tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass, including older deployment telemetry constructor calls.

- [ ] **Step 4: Commit Task 4**

```bash
git add src/WhiskeyRealism/Tactical/TacticalDeploymentTelemetry.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat(tactical): carry deployment terrain facing evidence"
```

## Task 5: Add Runtime Terrain Probe

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalTerrainProbe.cs`

- [ ] **Step 1: Add runtime-only probe**

Create `src/WhiskeyRealism/Tactical/TacticalTerrainProbe.cs`. Do not include this file in the test csproj because it references vanilla/Unity runtime types directly.

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical
{
    internal readonly struct TacticalTerrainRuntimeSample
    {
        public TacticalTerrainRuntimeSample(int terrainId, bool water, bool inDeploymentZone)
        {
            TerrainId = terrainId;
            Water = water;
            InDeploymentZone = inDeploymentZone;
        }

        public int TerrainId { get; }
        public bool Water { get; }
        public bool InDeploymentZone { get; }

        public TacticalTerrainSample ToPure()
        {
            return new TacticalTerrainSample(TerrainId, Water, InDeploymentZone, known: TerrainId >= 0);
        }
    }

    internal static class TacticalTerrainProbe
    {
        private const int WaterTerrainId = 4;

        internal static TacticalTerrainRuntimeSample SampleCenter(Regiment regiment, Frontline2 deploymentZone, Vector3 position)
        {
            try
            {
                int terrain = BattlefieldSetup.GetCurrentTerrainOnPos(position);
                bool inZone = IsInsideDeploymentZone(regiment, deploymentZone, position);
                return new TacticalTerrainRuntimeSample(terrain, terrain == WaterTerrainId, inZone);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-terrain-probe:center", "TacticalTerrainProbe center sample failed: " + ex.GetType().Name);
                return new TacticalTerrainRuntimeSample(-1, false, true);
            }
        }

        internal static IReadOnlyList<TacticalTerrainRuntimeSample> SampleFootprint(Regiment regiment, Frontline2 deploymentZone)
        {
            var samples = new List<TacticalTerrainRuntimeSample>();
            try
            {
                if (regiment == null || regiment.blockobject == null) return samples;
                for (int i = 0; i < regiment.blockobjects && i < regiment.blockobject.Length; i++)
                {
                    GameObject block = regiment.blockobject[i];
                    if ((object)block == null) continue;
                    samples.Add(SampleCenter(regiment, deploymentZone, block.transform.position));
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-terrain-probe:footprint", "TacticalTerrainProbe footprint sample failed: " + ex.GetType().Name);
            }
            return samples;
        }

        internal static Vector3 WithTerrainHeight(Vector3 position)
        {
            try
            {
                if ((object)BattlefieldSetup.bfs == null) return position;
                return new Vector3(position.x, BattlefieldSetup.bfs.GetTerrainHeight(position), position.z);
            }
            catch
            {
                return position;
            }
        }

        internal static bool CrossesWater(Vector3 from, Vector3 to)
        {
            try
            {
                if ((object)BattlefieldSetup.bfs == null) return false;
                return BattlefieldSetup.bfs.CheckTerrainLine(from, to, new[] { WaterTerrainId }, 2f);
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsInsideDeploymentZone(Regiment regiment, Frontline2 deploymentZone, Vector3 position)
        {
            try
            {
                if (regiment == null || (object)deploymentZone == null) return true;
                return deploymentZone.CheckIfWithinZone(position, regiment.alliance, regiment.oldposition) >= 0;
            }
            catch
            {
                return true;
            }
        }
    }
}
```

- [ ] **Step 2: Build**

Run:

```bash
./build.sh
```

Expected: build succeeds. If `BattlefieldSetup.bfs` is not public/static in current refs, replace that call with the same field access pattern used in existing runtime patches before continuing.

- [ ] **Step 3: Commit Task 5**

```bash
git add src/WhiskeyRealism/Tactical/TacticalTerrainProbe.cs
git commit -m "feat(tactical): add runtime terrain probe"
```

## Task 6: Extend Deployment Observer To Emit Terrain/Facing Evidence

**Files:**
- Modify: `src/WhiskeyRealism/Patches/TacticalDeploymentObserverPatch.cs`

- [ ] **Step 1: Capture terrain and facing in `SnapshotGroup`**

In `SnapshotGroup(Regiment regiment, BattleUnits.Grp group)`, add runtime samples before the return:

```csharp
float facing = SafeFloat(() => ((Component)regiment).transform.eulerAngles.y);
var centerSample = TacticalTerrainProbe.SampleCenter(regiment, null, position);
var footprintSamples = TacticalTerrainProbe.SampleFootprint(regiment, null);
bool footprintWater = footprintSamples.Any(s => s.Water);
var enemy = NearestVisibleEnemy(regiment);
```

Extend the snapshot constructor call:

```csharp
return new TacticalDeploymentGroupSnapshot(
    key,
    name,
    regiment.alliance,
    regiment.unittyp,
    position.x,
    position.z,
    formation,
    formationOrdered,
    pathCount,
    routed,
    active,
    centerSample.TerrainId,
    centerSample.Water,
    footprintWater,
    centerSample.InDeploymentZone,
    facing,
    enemy.bearing,
    enemy.distance);
```

- [ ] **Step 2: Add safe float and visible enemy helper**

Add these helpers to `TacticalDeploymentObserverPatch`:

```csharp
private static float SafeFloat(Func<float> read)
{
    try
    {
        float value = read();
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }
    catch { return 0f; }
}

private static (float bearing, float distance) NearestVisibleEnemy(Regiment regiment)
{
    try
    {
        if (regiment == null || regiment.unitrange == null || regiment.unitrange.enemyinrangereg == null)
            return (0f, 0f);

        Regiment best = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < regiment.unitrange.enemyinrangereg.Count; i++)
        {
            Regiment enemy = regiment.unitrange.enemyinrangereg[i];
            if (enemy == null || enemy.isrouted || !enemy.gameObject.activeInHierarchy) continue;
            float distance = Vector3.Distance(((Component)regiment).transform.position, ((Component)enemy).transform.position);
            if (distance < bestDistance)
            {
                best = enemy;
                bestDistance = distance;
            }
        }

        if (best == null) return (0f, 0f);
        float bearing = Tools.GetAngle(((Component)regiment).transform.position, ((Component)best).transform.position) + 180f;
        return (bearing, bestDistance);
    }
    catch
    {
        return (0f, 0f);
    }
}
```

Use `unitrange.enemyinrangereg`, not `closestenemyunitfarreg`, because the vanilla anchor shows closest-enemy assignment is not guaranteed to be visibility-filtered.

- [ ] **Step 3: Emit sparse terrain rows for large moves and terrain failures**

In `TopMoveLines(...)`, after the existing `[TacDeployObsMove]` line, yield a second line when terrain evidence exists or water is detected:

```csharp
if (move.After.HasTerrainEvidence || move.After.CenterWater || move.After.FootprintWater)
{
    var candidate = new TacticalTerrainCandidate(
        new TacticalPoint2(move.After.X, move.After.Z),
        move.After.Facing,
        new TacticalTerrainSample(move.After.TerrainId, move.After.CenterWater, move.After.InsideDeploymentZone, move.After.TerrainId >= 0),
        new[]
        {
            new TacticalTerrainSample(move.After.TerrainId, move.After.FootprintWater, move.After.InsideDeploymentZone, move.After.TerrainId >= 0)
        });
    var decision = new TacticalTerrainDecision(
        false,
        TacticalTerrainDecisionReason.VanillaKept,
        candidate,
        move.Distance,
        move.After.HasVisibleEnemyBearing
            ? TacticalTerrainFacingDiscipline.AngleDelta(move.After.Facing, move.After.NearestVisibleEnemyBearing)
            : 0f);
    yield return TacticalTerrainFacingTelemetry.Format(new TacticalTerrainFacingLogRow(
        surface,
        before.Phase,
        move.After.Alliance,
        move.After.Name,
        move.After.TerrainId,
        move.After.CenterWater,
        move.After.FootprintWater,
        move.After.InsideDeploymentZone,
        move.After.Facing,
        move.After.NearestVisibleEnemyBearing,
        move.After.NearestVisibleEnemyDistance,
        decision));
}
```

- [ ] **Step 4: Build and run tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected: tests pass and build succeeds.

- [ ] **Step 5: Commit Task 6**

```bash
git add src/WhiskeyRealism/Patches/TacticalDeploymentObserverPatch.cs
git commit -m "feat(tactical): observe deployment terrain facing evidence"
```

## Task 7: Add Default-Off Config Flags

**Files:**
- Modify: `src/WhiskeyRealism/Plugin.cs`

- [ ] **Step 1: Add config entries**

Add fields near `EnableTacticalDeploymentObserver`:

```csharp
public static ConfigEntry<bool> EnableTacticalDeploymentTerrainDiscipline;
public static ConfigEntry<float> TacticalDeploymentTerrainMaxCorrectionMeters;
public static ConfigEntry<int> TacticalDeploymentTerrainMaxCandidates;
public static ConfigEntry<float> TacticalDeploymentFacingPreferredDeltaDegrees;
```

Bind them near the existing tactical observer binding:

```csharp
EnableTacticalDeploymentTerrainDiscipline = Config.Bind(
    "Tactical",
    "Enable Tactical Deployment Terrain Discipline",
    false,
    "Default off. When enabled, AI deployment placement may correct clear water/out-of-zone terrain failures after vanilla deployment.");

TacticalDeploymentTerrainMaxCorrectionMeters = Config.Bind(
    "Tactical",
    "Tactical Deployment Terrain Discipline Max Correction Meters",
    60f,
    "Maximum distance Whiskey may move an AI deployment group while correcting a terrain failure.");

TacticalDeploymentTerrainMaxCandidates = Config.Bind(
    "Tactical",
    "Tactical Deployment Terrain Discipline Max Candidates",
    16,
    "Maximum candidate points sampled around a failed AI deployment placement.");

TacticalDeploymentFacingPreferredDeltaDegrees = Config.Bind(
    "Tactical",
    "Tactical Deployment Facing Preferred Delta Degrees",
    90f,
    "Preferred maximum final facing delta from visible enemy bearing for deployment terrain corrections.");
```

- [ ] **Step 2: Build**

Run:

```bash
./build.sh
```

Expected: build succeeds.

- [ ] **Step 3: Commit Task 7**

```bash
git add src/WhiskeyRealism/Plugin.cs
git commit -m "feat(tactical): add terrain discipline config"
```

## Task 8: Add Default-Off Deployment Terrain Discipline Patch

**Files:**
- Create: `src/WhiskeyRealism/Patches/TacticalDeploymentTerrainDisciplinePatch.cs`

- [ ] **Step 1: Add the Harmony patch skeleton**

Create `src/WhiskeyRealism/Patches/TacticalDeploymentTerrainDisciplinePatch.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla DoPlacementAIUnitsWithinDeploymentzoneNew places AI groups immediately,
    // then runs deployment-zone and water checks. This default-off patch only corrects
    // clear AI deployment terrain failures after vanilla has completed placement.
    [HarmonyPatch(typeof(BattleUnits), "DoPlacementAIUnitsWithinDeploymentzoneNew")]
    internal static class TacticalDeploymentTerrainDisciplinePatch
    {
        [HarmonyPostfix]
        internal static void Postfix(BattleUnits __instance, int foralliance)
        {
            if (!Enabled()) return;
            try
            {
                if (__instance == null) return;
                if (GameVars.playeralliance == foralliance && !GameVars.ai_vs_ai) return;

                var groups = ReadGroups(__instance);
                if (groups == null || groups.Length == 0) return;

                foreach (BattleUnits.Grp group in groups)
                {
                    if (group == null || group.regref == null) continue;
                    Regiment regiment = group.regref;
                    if (regiment.alliance != foralliance) continue;
                    if (regiment.unittyp <= 13) continue;
                    if (regiment.isrouted || !regiment.gameObject.activeInHierarchy) continue;

                    TryCorrectGroup(__instance, group, regiment);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-deployment-terrain:failed", "TacticalDeploymentTerrainDisciplinePatch failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                   Plugin.Instance.Enabled != null &&
                   Plugin.Instance.Enabled.Value &&
                   Plugin.EnableTacticalDeploymentTerrainDiscipline != null &&
                   Plugin.EnableTacticalDeploymentTerrainDiscipline.Value;
        }
    }
}
```

- [ ] **Step 2: Add group reflection helper**

Add inside the class:

```csharp
private static readonly System.Reflection.FieldInfo GrpField = AccessTools.Field(typeof(BattleUnits), "grp");

private static BattleUnits.Grp[] ReadGroups(BattleUnits battleUnits)
{
    try
    {
        return GrpField?.GetValue(battleUnits) as BattleUnits.Grp[] ?? Array.Empty<BattleUnits.Grp>();
    }
    catch (Exception ex)
    {
        OnceLog.Warning("tactical-deployment-terrain:grp", "Failed reading BattleUnits.grp: " + ex.GetType().Name);
        return Array.Empty<BattleUnits.Grp>();
    }
}
```

- [ ] **Step 3: Add candidate generation and correction**

Add inside the class:

```csharp
private static void TryCorrectGroup(BattleUnits battleUnits, BattleUnits.Grp group, Regiment regiment)
{
    Vector3 original = regiment.transform.position;
    float originalFacing = regiment.transform.eulerAngles.y;
    var center = TacticalTerrainProbe.SampleCenter(regiment, battleUnits.frontline2, original);
    var footprint = TacticalTerrainProbe.SampleFootprint(regiment, battleUnits.frontline2);
    bool footprintWater = footprint.Any(s => s.Water);

    if (!center.Water && !footprintWater && center.InDeploymentZone)
        return;

    var enemy = VisibleEnemy(regiment, original);
    var rules = new TacticalTerrainRules(
        Plugin.TacticalDeploymentTerrainMaxCorrectionMeters != null ? Plugin.TacticalDeploymentTerrainMaxCorrectionMeters.Value : 60f,
        Plugin.TacticalDeploymentFacingPreferredDeltaDegrees != null ? Plugin.TacticalDeploymentFacingPreferredDeltaDegrees.Value : 90f,
        requireDeploymentZone: true,
        requireVisibleEnemyForFacing: false);

    var candidates = BuildCandidates(regiment, battleUnits.frontline2, original, originalFacing, enemy);
    var decision = TacticalTerrainFacingDiscipline.Choose(
        new TacticalPoint2(original.x, original.z),
        originalFacing,
        candidates,
        enemy,
        rules);

    Emit(group, regiment, center, footprintWater, enemy, decision);
    if (!decision.Accepted) return;

    Vector3 corrected = new Vector3(decision.Candidate.Point.X, original.y, decision.Candidate.Point.Z);
    corrected = TacticalTerrainProbe.WithTerrainHeight(corrected);
    float correctedFacing = decision.Candidate.FacingDegrees;
    battleUnits.SetGroupFormation(
        group.go,
        regiment.groupformation,
        correctedFacing,
        corrected,
        immediateplacement: true,
        newpath: true,
        modifylastwaypoint: false,
        2,
        -1,
        ignoredeplyomentzone: false,
        skiprotation: false,
        showmovementoptions: false);
}
```

- [ ] **Step 4: Add candidate helpers**

Add:

```csharp
private static IEnumerable<TacticalTerrainCandidate> BuildCandidates(
    Regiment regiment,
    Frontline2 deploymentZone,
    Vector3 original,
    float originalFacing,
    TacticalEnemyBearingEvidence enemy)
{
    int max = Math.Max(1, Plugin.TacticalDeploymentTerrainMaxCandidates != null ? Plugin.TacticalDeploymentTerrainMaxCandidates.Value : 16);
    float[] radii = { 0f, 8f, 16f, 32f, 48f, 60f };
    int produced = 0;

    foreach (float radius in radii)
    {
        for (int angle = 0; angle < 360 && produced < max; angle += 45)
        {
            Vector3 point = radius <= 0f
                ? original
                : Tools.GetAnglePos(original, angle, radius);
            point = TacticalTerrainProbe.WithTerrainHeight(point);
            var center = TacticalTerrainProbe.SampleCenter(regiment, deploymentZone, point);
            var samples = new[] { center.ToPure() };
            float facing = enemy.Visible ? enemy.BearingDegrees : originalFacing;
            produced++;
            yield return new TacticalTerrainCandidate(
                new TacticalPoint2(point.x, point.z),
                facing,
                center.ToPure(),
                samples);
        }
    }
}

private static TacticalEnemyBearingEvidence VisibleEnemy(Regiment regiment, Vector3 origin)
{
    try
    {
        if (regiment == null || regiment.unitrange == null || regiment.unitrange.enemyinrangereg == null)
            return new TacticalEnemyBearingEvidence(false, 0f, 0f, 0f);

        Regiment best = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < regiment.unitrange.enemyinrangereg.Count; i++)
        {
            Regiment enemy = regiment.unitrange.enemyinrangereg[i];
            if (enemy == null || enemy.isrouted || !enemy.gameObject.activeInHierarchy) continue;
            float distance = Vector3.Distance(origin, enemy.transform.position);
            if (distance < bestDistance)
            {
                best = enemy;
                bestDistance = distance;
            }
        }

        if (best == null)
            return new TacticalEnemyBearingEvidence(false, 0f, 0f, 0f);

        return new TacticalEnemyBearingEvidence(
            true,
            Tools.GetAngle(origin, best.transform.position) + 180f,
            bestDistance,
            Math.Max(0f, best.strength));
    }
    catch
    {
        return new TacticalEnemyBearingEvidence(false, 0f, 0f, 0f);
    }
}
```

- [ ] **Step 5: Add bounded log helper**

Add:

```csharp
private static void Emit(
    BattleUnits.Grp group,
    Regiment regiment,
    TacticalTerrainRuntimeSample center,
    bool footprintWater,
    TacticalEnemyBearingEvidence enemy,
    TacticalTerrainDecision decision)
{
    try
    {
        string name = group != null && !string.IsNullOrEmpty(group.name) ? group.name : regiment != null ? regiment.name : "-";
        Plugin.Log.LogInfo(TacticalTerrainFacingTelemetry.Format(new TacticalTerrainFacingLogRow(
            "DoPlacementAIUnitsWithinDeploymentzoneNew",
            TacticalDeploymentTelemetry.PhaseInitial,
            regiment != null ? regiment.alliance : -1,
            name,
            center.TerrainId,
            center.Water,
            footprintWater,
            center.InDeploymentZone,
            regiment != null ? regiment.transform.eulerAngles.y : 0f,
            enemy.BearingDegrees,
            enemy.DistanceMeters,
            decision)));
    }
    catch { }
}
```

- [ ] **Step 6: Build**

Run:

```bash
./build.sh
```

Expected: build succeeds. If `SetGroupFormation` is not accessible from the patch due overload or access, use `AccessTools.Method(typeof(BattleUnits), "SetGroupFormation", ...)` with the signature proven by the decompile lines in this plan and invoke it inside try/catch.

- [ ] **Step 7: Commit Task 8**

```bash
git add src/WhiskeyRealism/Patches/TacticalDeploymentTerrainDisciplinePatch.cs
git commit -m "feat(tactical): discipline AI deployment terrain"
```

## Task 9: Documentation And Patch Catalog

**Files:**
- Modify: `docs/patch-catalog.md`
- Modify: `docs/handoff.md`

- [ ] **Step 1: Update patch catalog**

Add the observer extension under #58 and add a new ordinal for `TacticalDeploymentTerrainDisciplinePatch` using the next available patch number at implementation time. Record:

```markdown
- Default-off tactical deployment terrain discipline.
- Harmony surface: `BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew(int foralliance)` Postfix.
- Vanilla anchors: `Assembly-CSharp.decompiled.cs:85524-85872`, `131303-131358`, `131432-131556`.
- Config: `Enable Tactical Deployment Terrain Discipline = false`.
- Safety: AI side only, no player-side correction outside AI-vs-AI, bounded candidate count, leaves vanilla unchanged when no safe candidate exists.
```

- [ ] **Step 2: Update handoff**

In `docs/handoff.md`, add a short current-state note:

```markdown
Terrain/facing discipline plan exists at `docs/superpowers/plans/2026-05-10-tactical-terrain-facing-discipline-implementation-plan.md`. The implementation adds telemetry first, then a default-off AI deployment correction that uses visible enemy evidence and terrain samples without replacing vanilla NavMesh pathfinding.
```

- [ ] **Step 3: Commit Task 9**

```bash
git add docs/patch-catalog.md docs/handoff.md
git commit -m "docs(tactical): document terrain facing discipline"
```

## Task 10: Verification, Deploy, And Smoke

**Files:**
- No source changes unless verification exposes a defect.

- [ ] **Step 1: Run the full test harness**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 2: Build the DLL**

```bash
./build.sh
```

Expected: `dist/WhiskeyRealism.dll` exists and build reports `0 Error(s)`.

- [ ] **Step 3: Check diff hygiene**

```bash
git diff --check
git status --short --branch
```

Expected: no whitespace errors. Status shows only coherent task changes or a clean branch after commits.

- [ ] **Step 4: Deploy the DLL**

Close Grand Tactician first, then run:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
```

Expected: copy succeeds. If it fails with `Invalid argument`, the game is still holding the DLL lock; close the game and rerun the same command.

- [ ] **Step 5: Verify deployed hash**

```bash
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: file sizes match and SHA-256 hashes match.

- [ ] **Step 6: Smoke telemetry with behavior off**

Set in BepInEx config:

```text
Enable Tactical Deployment Observer = true
Enable Tactical Deployment Terrain Discipline = false
```

Launch the game, start or load a tactical battle, then inspect:

```bash
rg -n "TacDeployTerrain|TacDeployObs|TacticalDeployment|Exception" "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Expected:

- `[TacDeployObs]` remains bounded.
- `[TacDeployTerrain]` appears only around deployment movement/terrain evidence.
- No repeated Harmony exceptions.
- No unit movement changes from this telemetry-only config.

- [ ] **Step 7: Smoke behavior with discipline on**

Set:

```text
Enable Tactical Deployment Observer = true
Enable Tactical Deployment Terrain Discipline = true
```

Use an AI-vs-AI battle or a battle where the AI side deploys. Inspect the same log:

```bash
rg -n "TacDeployTerrain|decision=|accepted=|Exception" "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Expected:

- Water/out-of-zone failures are either corrected once or logged as `accepted=false`.
- No player-side deployment correction outside AI-vs-AI.
- No repeated Harmony exceptions.
- Existing #53 pathfinder discipline logs do not regress.

- [ ] **Step 8: Final commit if smoke caused doc/config updates**

If smoke required documentation or config sample changes:

```bash
git add docs/patch-catalog.md docs/handoff.md
git commit -m "docs(tactical): record terrain discipline smoke state"
```

## Defer Boundaries

- Do not patch every `BattleUnits.SetWaypoint(...)` call for final facing in this implementation. The decompile confirms `manualfinalrotation` is available, but generic interception risks player order mutation.
- Do not alter `Regiment.AddPath(...)` beyond existing #53 discipline in this implementation.
- Do not use `unitrange.closestenemyunitfarreg` by itself as spotted-enemy evidence.
- Do not change scenario-defined `battledata.startposition` behavior beyond telemetry until a live save proves correction is safe.
- Do not enable behavior by default before a focused in-game smoke passes.
