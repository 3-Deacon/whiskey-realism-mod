# Tactical B3-B5 Odds, Macro, And Sector Doctrine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Slice B tactical doctrine chain from odds/contact evidence through battle-level macro stance and group-level sector stance.

**Architecture:** Keep B3 pure and observable, then let B4/B5 consume that doctrine through two narrow Postfix patches. Runtime extraction stays in patch helpers; deterministic scoring lives under `src/WhiskeyRealism/Tactical/` and is covered by the console harness.

**Tech Stack:** BepInEx 5.4.x x64, HarmonyX, C# netstandard2.1, Unity 2021 Mono, `tests/WhiskeyRealism.Tests` console harness, vanilla anchors from `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

**Current status:** Implemented and hash-deployed on 2026-05-07. Follow-up probe/formation fix deployed as SHA-256 `c5fff3e2248774e1853570ef6c3c9936cfdffb4a99f0111ac2781b8b1b2d2f97` (588800 bytes): no-contact odds stay at zero, B3 sector extraction no longer floors no-contact enemy strength to one, B4 macro scoring reads actual `AIBattle.unitsused` contact/sector evidence instead of force-balance/no-sector inference, and B5 maps defensive weak points plus explicit Probe sectors to vanilla stance 1 screening/probe so vanilla can form probing groups into line. B4/B5 remain default-off until focused in-game smoke on the new build. This is required because B4 writes vanilla `macroai` and B5 writes vanilla `ai_stance`; those are behavior changes, unlike B3 read-only odds telemetry. Do not flip the defaults on until logs prove bounded `[TacticalMacroDecision]` / `[TacticalGroupDecision]` output, `reason=probe` / `reason=probe-weak-point` with `whiskeyStance=1`, stable Harmony anchors, no repeated exceptions, no player-subordinate retasking, no charge stance 4 writes, and no movement/reserve/artillery/fallback side effects. The last tailed log was stale relative to this deploy, so current runtime proof still requires restarting GTCW.

---

## Source Inputs

- Spec: `docs/superpowers/specs/2026-05-07-tactical-b3-b5-odds-macro-sector-design.md`
- Master sequence: `docs/superpowers/plans/2026-05-05-tactical-brain-master-sequencing.md`
- Current tactical files:
  - `src/WhiskeyRealism/Tactical/TacticalBattleContext.cs`
  - `src/WhiskeyRealism/Tactical/TacticalTelemetry.cs`
  - `src/WhiskeyRealism/Tactical/TacticalOrderFriction.cs`
  - `src/WhiskeyRealism/Tactical/TacticalCommandLedger.cs`
  - `src/WhiskeyRealism/Tactical/TacticalWlActionGuard.cs`
  - `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs`
  - `src/WhiskeyRealism/Patches/BattleChargeGatePatch.cs`
  - `src/WhiskeyRealism/Patches/BattleFeudActionGatePatch.cs`

## Vanilla Anchors To Recheck First

Run:

```bash
rg -n "private void CheckGlobalAIStrategy\\(|private void AdjustGroupAIStance\\(|private float GetGroupStrength\\(|public void UpdateUnitRangeFast\\(|class FogOfWar|public float GetArrivalTimeToBF\\(" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
```

Expected current anchors:

- `AIBattle.CheckGlobalAIStrategy()` line 6314
- `AIBattle.AdjustGroupAIStance()` line 4221
- `AIBattle.GetGroupStrength(...)` line 6025
- `Regiment.UpdateUnitRangeFast(...)` line 122545
- `FogOfWar` line 100570
- `Regiment.GetArrivalTimeToBF(...)` line 138862

Before B4 patching, also run:

```bash
rg -n "sideinformation\\[.*\\]\\.macroai\\s*=|GameVars\\.aistrategy\\s*=|macroai\\s*=" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs
```

Expected: B4 must skip `GameVars.aistrategy >= 0`, `bunits.sideinformation[sideofai].macroai >= 0`, and existing macro retreat state.

## File Ownership

- Create: `src/WhiskeyRealism/Tactical/TacticalContactLedger.cs` — contact classification, confidence, and aging.
- Create: `src/WhiskeyRealism/Tactical/TacticalSectorLedger.cs` — sector DTOs, mission scoring, decisive/economy sectors.
- Create: `src/WhiskeyRealism/Tactical/TacticalOddsDoctrine.cs` — current/projected odds and inferior-force posture.
- Create: `src/WhiskeyRealism/Tactical/TacticalBattlePlan.cs` — macro plan pressure DTOs and stance decision result.
- Create: `src/WhiskeyRealism/Tactical/TacticalDoctrineScorer.cs` — pure B4/B5 decision functions.
- Create: `src/WhiskeyRealism/Patches/BattleMacroStrategyPatch.cs` — B4 Postfix on `AIBattle.CheckGlobalAIStrategy()`.
- Create: `src/WhiskeyRealism/Patches/BattleGroupStancePatch.cs` — B5 Postfix on `AIBattle.AdjustGroupAIStance()`.
- Modify: `src/WhiskeyRealism/Tactical/TacticalBattleContext.cs` — add odds/sector summary fields.
- Modify: `src/WhiskeyRealism/Tactical/TacticalTelemetry.cs` — add `TacticalOdds` prefix and decision summaries.
- Modify: `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs` — runtime extraction and B3 telemetry.
- Modify: `src/WhiskeyRealism/Plugin.cs` — add default-off B4/B5 config entries.
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` — add explicit tactical source includes.
- Modify: `tests/WhiskeyRealism.Tests/Program.cs` — add pure tests.
- Modify after build/deploy/smoke: `docs/patch-catalog.md`, `docs/handoff.md`, `MEMORY.md`, `README.md` if behavior ships.

## Task 1: Add B3 Pure Tests

**Files:**
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`

- [ ] **Step 1: Add source includes that will fail until files exist**

Add to the tactical compile include block in `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalContactLedger.cs" Link="TacticalContactLedger.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalSectorLedger.cs" Link="TacticalSectorLedger.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalOddsDoctrine.cs" Link="TacticalOddsDoctrine.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalBattlePlan.cs" Link="TacticalBattlePlan.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalDoctrineScorer.cs" Link="TacticalDoctrineScorer.cs" />
```

- [ ] **Step 2: Register B3 tests**

Add these test registrations near the existing tactical tests in `Program.cs`:

```csharp
("tactical contact no sighting is none", TacticalContactNoSightingIsNone),
("tactical contact stale sighting ages down", TacticalContactStaleSightingAgesDown),
("tactical odds no contact avoids assault", TacticalOddsNoContactAvoidsAssault),
("tactical odds global superiority selects one decisive sector", TacticalOddsGlobalSuperioritySelectsOneDecisiveSector),
("tactical odds inferior no relief preserves force", TacticalOddsInferiorNoReliefPreservesForce),
("tactical odds inferior with relief delays", TacticalOddsInferiorWithReliefDelays),
```

- [ ] **Step 3: Add B3 test methods**

Append methods near the existing tactical test methods:

```csharp
private static void TacticalContactNoSightingIsNone()
{
    var contact = TacticalContactLedger.Classify(new TacticalContactInput(
        visibleEnemyStrength: 0f,
        recentEnemyStrength: 0f,
        inferredEnemyStrength: 0f,
        secondsSinceLastConfirmed: 9999f,
        receivedFire: false,
        inFog: true));

    AssertEqual(TacticalContactState.None, contact.State, "state");
    AssertTrue(contact.Confidence < 0.2f, "confidence should be low without contact");
}

private static void TacticalContactStaleSightingAgesDown()
{
    var recent = TacticalContactLedger.Classify(new TacticalContactInput(
        visibleEnemyStrength: 1000f,
        recentEnemyStrength: 1000f,
        inferredEnemyStrength: 0f,
        secondsSinceLastConfirmed: 5f,
        receivedFire: false,
        inFog: false));
    var stale = TacticalContactLedger.Classify(new TacticalContactInput(
        visibleEnemyStrength: 0f,
        recentEnemyStrength: 1000f,
        inferredEnemyStrength: 0f,
        secondsSinceLastConfirmed: 900f,
        receivedFire: false,
        inFog: true));

    AssertEqual(TacticalContactState.Confirmed, recent.State, "recent state");
    AssertTrue(stale.State == TacticalContactState.Inferred || stale.State == TacticalContactState.None, "stale state");
    AssertTrue(stale.Confidence < recent.Confidence, "stale confidence should decay");
}

private static void TacticalOddsNoContactAvoidsAssault()
{
    var output = TacticalOddsDoctrine.Evaluate(new TacticalOddsInput(
        ownStrength: 12000f,
        enemyStrengthConfirmed: 0f,
        enemyStrengthRecent: 0f,
        enemyStrengthInferred: 0f,
        reinforcementStrength24h: 0f,
        terrainAdvantage: 0f,
        contact: new TacticalContactAssessment(TacticalContactState.None, 0f, 0f, "none"),
        sectors: Array.Empty<TacticalSectorAssessment>()));

    AssertEqual(TacticalInferiorForcePosture.ProbeOrHold, output.InferiorForcePosture, "posture");
    AssertTrue(!output.AllowAssault, "no contact should not permit assault");
}

private static void TacticalOddsGlobalSuperioritySelectsOneDecisiveSector()
{
    var sectors = new[]
    {
        new TacticalSectorAssessment(0, TacticalSectorSource.AngleSlice, 3000f, 2500f, 0.7f, false, false, TacticalSectorMission.Hold),
        new TacticalSectorAssessment(1, TacticalSectorSource.AngleSlice, 5000f, 1800f, 0.9f, false, false, TacticalSectorMission.Probe),
        new TacticalSectorAssessment(2, TacticalSectorSource.AngleSlice, 4000f, 3200f, 0.8f, false, false, TacticalSectorMission.Hold)
    };

    var output = TacticalOddsDoctrine.Evaluate(new TacticalOddsInput(
        ownStrength: 12000f,
        enemyStrengthConfirmed: 7500f,
        enemyStrengthRecent: 7500f,
        enemyStrengthInferred: 7500f,
        reinforcementStrength24h: 0f,
        terrainAdvantage: 0f,
        contact: new TacticalContactAssessment(TacticalContactState.Confirmed, 0.9f, 7500f, "visible"),
        sectors: sectors));

    AssertEqual(1, output.DecisiveSectorId, "decisive sector");
    AssertTrue(output.EconomyOfForceSectorIds.Length >= 1, "other sectors should remain economy/fix candidates");
}

private static void TacticalOddsInferiorNoReliefPreservesForce()
{
    var output = TacticalOddsDoctrine.Evaluate(new TacticalOddsInput(
        ownStrength: 4000f,
        enemyStrengthConfirmed: 12000f,
        enemyStrengthRecent: 12000f,
        enemyStrengthInferred: 12000f,
        reinforcementStrength24h: 0f,
        terrainAdvantage: 0f,
        contact: new TacticalContactAssessment(TacticalContactState.Confirmed, 0.9f, 12000f, "visible"),
        sectors: Array.Empty<TacticalSectorAssessment>()));

    AssertEqual(TacticalInferiorForcePosture.PreserveOrRetreat, output.InferiorForcePosture, "posture");
}

private static void TacticalOddsInferiorWithReliefDelays()
{
    var output = TacticalOddsDoctrine.Evaluate(new TacticalOddsInput(
        ownStrength: 4000f,
        enemyStrengthConfirmed: 12000f,
        enemyStrengthRecent: 12000f,
        enemyStrengthInferred: 12000f,
        reinforcementStrength24h: 5000f,
        terrainAdvantage: 0.8f,
        contact: new TacticalContactAssessment(TacticalContactState.Confirmed, 0.9f, 12000f, "visible"),
        sectors: Array.Empty<TacticalSectorAssessment>()));

    AssertEqual(TacticalInferiorForcePosture.DelayOnStrongGround, output.InferiorForcePosture, "posture");
}
```

- [ ] **Step 4: Run tests to verify failure**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile fails because `TacticalContactLedger`, `TacticalSectorLedger`, `TacticalOddsDoctrine`, `TacticalBattlePlan`, and `TacticalDoctrineScorer` do not exist yet.

## Task 2: Implement B3 Pure Ledgers

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalContactLedger.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalSectorLedger.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalOddsDoctrine.cs`

- [ ] **Step 1: Create `TacticalContactLedger.cs`**

```csharp
using System;

namespace WhiskeyRealism.Tactical
{
    public enum TacticalContactState
    {
        None = 0,
        Inferred = 1,
        Recent = 2,
        Confirmed = 3
    }

    public readonly struct TacticalContactInput
    {
        public TacticalContactInput(float visibleEnemyStrength, float recentEnemyStrength, float inferredEnemyStrength, float secondsSinceLastConfirmed, bool receivedFire, bool inFog)
        {
            VisibleEnemyStrength = Sanitize(visibleEnemyStrength);
            RecentEnemyStrength = Sanitize(recentEnemyStrength);
            InferredEnemyStrength = Sanitize(inferredEnemyStrength);
            SecondsSinceLastConfirmed = Sanitize(secondsSinceLastConfirmed);
            ReceivedFire = receivedFire;
            InFog = inFog;
        }

        public float VisibleEnemyStrength { get; }
        public float RecentEnemyStrength { get; }
        public float InferredEnemyStrength { get; }
        public float SecondsSinceLastConfirmed { get; }
        public bool ReceivedFire { get; }
        public bool InFog { get; }

        private static float Sanitize(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(0f, value);
    }

    public readonly struct TacticalContactAssessment
    {
        public TacticalContactAssessment(TacticalContactState state, float confidence, float estimatedEnemyStrength, string reason)
        {
            State = state;
            Confidence = Clamp01(confidence);
            EstimatedEnemyStrength = Sanitize(estimatedEnemyStrength);
            Reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason.Trim();
        }

        public TacticalContactState State { get; }
        public float Confidence { get; }
        public float EstimatedEnemyStrength { get; }
        public string Reason { get; }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private static float Sanitize(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(0f, value);
    }

    public static class TacticalContactLedger
    {
        public static TacticalContactAssessment Classify(TacticalContactInput input)
        {
            if (input.VisibleEnemyStrength > 0f || input.ReceivedFire)
                return new TacticalContactAssessment(TacticalContactState.Confirmed, input.ReceivedFire ? 0.95f : 0.9f, Math.Max(input.VisibleEnemyStrength, input.RecentEnemyStrength), input.ReceivedFire ? "received-fire" : "visible");

            if (input.RecentEnemyStrength > 0f && input.SecondsSinceLastConfirmed <= 300f)
            {
                float confidence = 0.75f * (1f - input.SecondsSinceLastConfirmed / 600f);
                return new TacticalContactAssessment(TacticalContactState.Recent, confidence, input.RecentEnemyStrength, "recent");
            }

            if (input.InferredEnemyStrength > 0f || (input.RecentEnemyStrength > 0f && input.SecondsSinceLastConfirmed <= 1200f))
            {
                float estimated = Math.Max(input.InferredEnemyStrength, input.RecentEnemyStrength * 0.5f);
                return new TacticalContactAssessment(TacticalContactState.Inferred, input.InFog ? 0.35f : 0.45f, estimated, "inferred");
            }

            return new TacticalContactAssessment(TacticalContactState.None, 0f, 0f, "none");
        }
    }
}
```

- [ ] **Step 2: Create `TacticalSectorLedger.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace WhiskeyRealism.Tactical
{
    public enum TacticalSectorMission
    {
        Hold = 0,
        Fix = 1,
        Probe = 2,
        Refuse = 3,
        AttackWeakPoint = 4,
        EconomyOfForce = 5,
        Preserve = 6
    }

    public readonly struct TacticalSectorAssessment
    {
        public TacticalSectorAssessment(int sectorId, TacticalSectorSource source, float ownStrength, float enemyStrength, float confidence, bool strongPoint, bool flankRisk, TacticalSectorMission mission)
        {
            SectorId = sectorId;
            Source = source;
            OwnStrength = Sanitize(ownStrength);
            EnemyStrength = Sanitize(enemyStrength);
            Confidence = Clamp01(confidence);
            StrongPoint = strongPoint;
            FlankRisk = flankRisk;
            Mission = mission;
        }

        public int SectorId { get; }
        public TacticalSectorSource Source { get; }
        public float OwnStrength { get; }
        public float EnemyStrength { get; }
        public float Confidence { get; }
        public bool StrongPoint { get; }
        public bool FlankRisk { get; }
        public TacticalSectorMission Mission { get; }
        public float Odds => OwnStrength / Math.Max(1f, EnemyStrength);

        public TacticalSectorAssessment WithMission(TacticalSectorMission mission)
        {
            return new TacticalSectorAssessment(SectorId, Source, OwnStrength, EnemyStrength, Confidence, StrongPoint, FlankRisk, mission);
        }

        private static float Sanitize(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(0f, value);
        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }

    public readonly struct TacticalSectorLedgerResult
    {
        public TacticalSectorLedgerResult(TacticalSectorAssessment[] sectors, int decisiveSectorId, int[] economyOfForceSectorIds)
        {
            Sectors = sectors ?? Array.Empty<TacticalSectorAssessment>();
            DecisiveSectorId = decisiveSectorId;
            EconomyOfForceSectorIds = economyOfForceSectorIds ?? Array.Empty<int>();
        }

        public TacticalSectorAssessment[] Sectors { get; }
        public int DecisiveSectorId { get; }
        public int[] EconomyOfForceSectorIds { get; }
    }

    public static class TacticalSectorLedger
    {
        public static TacticalSectorLedgerResult Evaluate(IEnumerable<TacticalSectorAssessment> rawSectors)
        {
            var sectors = (rawSectors ?? Array.Empty<TacticalSectorAssessment>()).ToArray();
            if (sectors.Length == 0)
                return new TacticalSectorLedgerResult(Array.Empty<TacticalSectorAssessment>(), -1, Array.Empty<int>());

            int decisive = -1;
            float bestScore = 0f;
            for (int i = 0; i < sectors.Length; i++)
            {
                float score = sectors[i].Odds * sectors[i].Confidence;
                if (sectors[i].StrongPoint) score *= 0.65f;
                if (sectors[i].FlankRisk) score *= 0.55f;
                if (score > bestScore && sectors[i].Confidence >= 0.55f)
                {
                    bestScore = score;
                    decisive = sectors[i].SectorId;
                }
            }

            var resolved = new TacticalSectorAssessment[sectors.Length];
            var economy = new List<int>();
            for (int i = 0; i < sectors.Length; i++)
            {
                TacticalSectorMission mission;
                if (sectors[i].FlankRisk) mission = TacticalSectorMission.Refuse;
                else if (sectors[i].StrongPoint) mission = TacticalSectorMission.Hold;
                else if (sectors[i].SectorId == decisive && sectors[i].Odds >= 1.35f) mission = TacticalSectorMission.AttackWeakPoint;
                else if (sectors[i].Confidence < 0.45f) mission = TacticalSectorMission.Probe;
                else if (decisive >= 0) mission = TacticalSectorMission.Fix;
                else mission = TacticalSectorMission.Hold;

                if (mission == TacticalSectorMission.Fix || mission == TacticalSectorMission.Hold)
                    economy.Add(sectors[i].SectorId);

                resolved[i] = sectors[i].WithMission(mission);
            }

            return new TacticalSectorLedgerResult(resolved, decisive, economy.ToArray());
        }
    }
}
```

- [ ] **Step 3: Create `TacticalOddsDoctrine.cs`**

```csharp
using System;
using System.Linq;

namespace WhiskeyRealism.Tactical
{
    public enum TacticalInferiorForcePosture
    {
        None = 0,
        ProbeOrHold = 1,
        DelayOnStrongGround = 2,
        PreserveOrRetreat = 3
    }

    public readonly struct TacticalOddsInput
    {
        public TacticalOddsInput(float ownStrength, float enemyStrengthConfirmed, float enemyStrengthRecent, float enemyStrengthInferred, float reinforcementStrength24h, float terrainAdvantage, TacticalContactAssessment contact, TacticalSectorAssessment[] sectors)
        {
            OwnStrength = Sanitize(ownStrength);
            EnemyStrengthConfirmed = Sanitize(enemyStrengthConfirmed);
            EnemyStrengthRecent = Sanitize(enemyStrengthRecent);
            EnemyStrengthInferred = Sanitize(enemyStrengthInferred);
            ReinforcementStrength24h = Sanitize(reinforcementStrength24h);
            TerrainAdvantage = Clamp01(terrainAdvantage);
            Contact = contact;
            Sectors = sectors ?? Array.Empty<TacticalSectorAssessment>();
        }

        public float OwnStrength { get; }
        public float EnemyStrengthConfirmed { get; }
        public float EnemyStrengthRecent { get; }
        public float EnemyStrengthInferred { get; }
        public float ReinforcementStrength24h { get; }
        public float TerrainAdvantage { get; }
        public TacticalContactAssessment Contact { get; }
        public TacticalSectorAssessment[] Sectors { get; }

        private static float Sanitize(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(0f, value);
        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }

    public readonly struct TacticalOddsAssessment
    {
        public TacticalOddsAssessment(float currentGlobalOdds, float projectedGlobalOdds, int decisiveSectorId, int[] economyOfForceSectorIds, TacticalInferiorForcePosture inferiorForcePosture, float confidence, bool allowAssault)
        {
            CurrentGlobalOdds = currentGlobalOdds;
            ProjectedGlobalOdds = projectedGlobalOdds;
            DecisiveSectorId = decisiveSectorId;
            EconomyOfForceSectorIds = economyOfForceSectorIds ?? Array.Empty<int>();
            InferiorForcePosture = inferiorForcePosture;
            Confidence = confidence;
            AllowAssault = allowAssault;
        }

        public float CurrentGlobalOdds { get; }
        public float ProjectedGlobalOdds { get; }
        public int DecisiveSectorId { get; }
        public int[] EconomyOfForceSectorIds { get; }
        public TacticalInferiorForcePosture InferiorForcePosture { get; }
        public float Confidence { get; }
        public bool AllowAssault { get; }
    }

    public static class TacticalOddsDoctrine
    {
        public static TacticalOddsAssessment Evaluate(TacticalOddsInput input)
        {
            var sectorLedger = TacticalSectorLedger.Evaluate(input.Sectors);
            float enemyCurrent = Math.Max(input.EnemyStrengthConfirmed, Math.Max(input.EnemyStrengthRecent * 0.75f, input.EnemyStrengthInferred * 0.5f));
            float current = input.OwnStrength / Math.Max(1f, enemyCurrent);
            float projected = (input.OwnStrength + input.ReinforcementStrength24h) / Math.Max(1f, enemyCurrent);
            TacticalInferiorForcePosture posture = TacticalInferiorForcePosture.None;

            if (input.Contact.State == TacticalContactState.None)
            {
                posture = TacticalInferiorForcePosture.ProbeOrHold;
            }
            else if (projected < 0.55f && input.TerrainAdvantage < 0.5f)
            {
                posture = TacticalInferiorForcePosture.PreserveOrRetreat;
            }
            else if (current < 0.6f && (input.ReinforcementStrength24h > input.OwnStrength * 0.5f || input.TerrainAdvantage >= 0.5f))
            {
                posture = TacticalInferiorForcePosture.DelayOnStrongGround;
            }

            bool allowAssault = input.Contact.State == TacticalContactState.Confirmed
                && input.Contact.Confidence >= 0.8f
                && current >= 1.75f
                && sectorLedger.DecisiveSectorId >= 0
                && sectorLedger.Sectors.Any(s => s.SectorId == sectorLedger.DecisiveSectorId && !s.StrongPoint && !s.FlankRisk);

            return new TacticalOddsAssessment(current, projected, sectorLedger.DecisiveSectorId, sectorLedger.EconomyOfForceSectorIds, posture, input.Contact.Confidence, allowAssault);
        }
    }
}
```

- [ ] **Step 4: Run tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: B3 tests pass or reveal threshold mismatches to fix in the pure ledgers.

## Task 3: Add B4/B5 Pure Decision Tests

**Files:**
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Register decision tests**

Add:

```csharp
("tactical macro dynamic is not attack", TacticalMacroDynamicIsNotAttack),
("tactical macro debug override skips", TacticalMacroDebugOverrideSkips),
("tactical macro inferior no relief retreats", TacticalMacroInferiorNoReliefRetreats),
("tactical group decisive sector attacks without charge", TacticalGroupDecisiveSectorAttacksWithoutCharge),
("tactical group low confidence keeps vanilla", TacticalGroupLowConfidenceKeepsVanilla),
("tactical group wl player subordinate skips", TacticalGroupWlPlayerSubordinateSkips),
```

- [ ] **Step 2: Add decision test methods**

```csharp
private static TacticalOddsAssessment Odds(float current, int decisive = -1, TacticalInferiorForcePosture posture = TacticalInferiorForcePosture.None, float confidence = 0.9f, bool assault = false)
{
    return new TacticalOddsAssessment(current, current, decisive, Array.Empty<int>(), posture, confidence, assault);
}

private static void TacticalMacroDynamicIsNotAttack()
{
    var decision = TacticalDoctrineScorer.DecideMacro(new TacticalMacroDecisionInput(
        vanillaMacro: -1,
        debugOverrideActive: false,
        saveRestoreMacroActive: false,
        vanillaRetreatActive: false,
        commanderAggression01: 0.5f,
        odds: Odds(1.2f, confidence: 0.1f)));

    AssertEqual(TacticalDoctrineDecisionKind.Apply, decision.Kind, "kind");
    AssertTrue(decision.MacroAi == -1 || decision.MacroAi == 2, "no-contact dynamic should stay dynamic/defend");
}

private static void TacticalMacroDebugOverrideSkips()
{
    var decision = TacticalDoctrineScorer.DecideMacro(new TacticalMacroDecisionInput(
        vanillaMacro: 1,
        debugOverrideActive: true,
        saveRestoreMacroActive: false,
        vanillaRetreatActive: false,
        commanderAggression01: 1f,
        odds: Odds(3f, decisive: 1, confidence: 1f, assault: true)));

    AssertEqual(TacticalDoctrineDecisionKind.Skip, decision.Kind, "debug override must skip");
}

private static void TacticalMacroInferiorNoReliefRetreats()
{
    var decision = TacticalDoctrineScorer.DecideMacro(new TacticalMacroDecisionInput(
        vanillaMacro: 2,
        debugOverrideActive: false,
        saveRestoreMacroActive: false,
        vanillaRetreatActive: false,
        commanderAggression01: 0.5f,
        odds: Odds(0.33f, posture: TacticalInferiorForcePosture.PreserveOrRetreat, confidence: 0.9f)));

    AssertEqual(3, decision.MacroAi, "macro retreat pressure");
}

private static void TacticalGroupDecisiveSectorAttacksWithoutCharge()
{
    var sector = new TacticalSectorAssessment(4, TacticalSectorSource.ObjectiveChain, 5000f, 2000f, 0.9f, strongPoint: false, flankRisk: false, TacticalSectorMission.AttackWeakPoint);
    var decision = TacticalDoctrineScorer.DecideGroupStance(new TacticalGroupStanceDecisionInput(
        vanillaStance: 2,
        macroAi: 1,
        sector: sector,
        orderFrictionAllowsChange: true,
        wlAllowsControl: true));

    AssertEqual(TacticalDoctrineDecisionKind.Apply, decision.Kind, "kind");
    AssertEqual(3, decision.GroupStance, "attack stance, not charge");
}

private static void TacticalGroupLowConfidenceKeepsVanilla()
{
    var sector = new TacticalSectorAssessment(4, TacticalSectorSource.AngleSlice, 5000f, 2000f, 0.2f, strongPoint: false, flankRisk: false, TacticalSectorMission.AttackWeakPoint);
    var decision = TacticalDoctrineScorer.DecideGroupStance(new TacticalGroupStanceDecisionInput(
        vanillaStance: 2,
        macroAi: 1,
        sector: sector,
        orderFrictionAllowsChange: true,
        wlAllowsControl: true));

    AssertEqual(TacticalDoctrineDecisionKind.Skip, decision.Kind, "low confidence");
}

private static void TacticalGroupWlPlayerSubordinateSkips()
{
    var sector = new TacticalSectorAssessment(4, TacticalSectorSource.ObjectiveChain, 5000f, 2000f, 0.9f, strongPoint: false, flankRisk: false, TacticalSectorMission.AttackWeakPoint);
    var decision = TacticalDoctrineScorer.DecideGroupStance(new TacticalGroupStanceDecisionInput(
        vanillaStance: 2,
        macroAi: 1,
        sector: sector,
        orderFrictionAllowsChange: true,
        wlAllowsControl: false));

    AssertEqual(TacticalDoctrineDecisionKind.Skip, decision.Kind, "wl ownership");
}
```

- [ ] **Step 3: Run tests to verify failure**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile fails because `TacticalDoctrineScorer` decision types do not exist yet.

## Task 4: Implement B4/B5 Pure Scorer

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalBattlePlan.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalDoctrineScorer.cs`

- [ ] **Step 1: Create `TacticalBattlePlan.cs`**

```csharp
using System;

namespace WhiskeyRealism.Tactical
{
    public enum TacticalDoctrineDecisionKind
    {
        Skip = 0,
        Apply = 1
    }

    public readonly struct TacticalMacroDecisionInput
    {
        public TacticalMacroDecisionInput(int vanillaMacro, bool debugOverrideActive, bool saveRestoreMacroActive, bool vanillaRetreatActive, float commanderAggression01, TacticalOddsAssessment odds)
        {
            VanillaMacro = vanillaMacro;
            DebugOverrideActive = debugOverrideActive;
            SaveRestoreMacroActive = saveRestoreMacroActive;
            VanillaRetreatActive = vanillaRetreatActive;
            CommanderAggression01 = Clamp01(commanderAggression01);
            Odds = odds;
        }

        public int VanillaMacro { get; }
        public bool DebugOverrideActive { get; }
        public bool SaveRestoreMacroActive { get; }
        public bool VanillaRetreatActive { get; }
        public float CommanderAggression01 { get; }
        public TacticalOddsAssessment Odds { get; }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0.5f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }

    public readonly struct TacticalMacroDecision
    {
        public TacticalMacroDecision(TacticalDoctrineDecisionKind kind, int macroAi, string reason)
        {
            Kind = kind;
            MacroAi = macroAi;
            Reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason.Trim();
        }

        public TacticalDoctrineDecisionKind Kind { get; }
        public int MacroAi { get; }
        public string Reason { get; }
    }

    public readonly struct TacticalGroupStanceDecisionInput
    {
        public TacticalGroupStanceDecisionInput(int vanillaStance, int macroAi, TacticalSectorAssessment sector, bool orderFrictionAllowsChange, bool wlAllowsControl)
        {
            VanillaStance = vanillaStance;
            MacroAi = macroAi;
            Sector = sector;
            OrderFrictionAllowsChange = orderFrictionAllowsChange;
            WlAllowsControl = wlAllowsControl;
        }

        public int VanillaStance { get; }
        public int MacroAi { get; }
        public TacticalSectorAssessment Sector { get; }
        public bool OrderFrictionAllowsChange { get; }
        public bool WlAllowsControl { get; }
    }

    public readonly struct TacticalGroupStanceDecision
    {
        public TacticalGroupStanceDecision(TacticalDoctrineDecisionKind kind, int groupStance, string reason)
        {
            Kind = kind;
            GroupStance = groupStance;
            Reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason.Trim();
        }

        public TacticalDoctrineDecisionKind Kind { get; }
        public int GroupStance { get; }
        public string Reason { get; }
    }
}
```

- [ ] **Step 2: Create `TacticalDoctrineScorer.cs`**

```csharp
namespace WhiskeyRealism.Tactical
{
    public static class TacticalDoctrineScorer
    {
        public static TacticalMacroDecision DecideMacro(TacticalMacroDecisionInput input)
        {
            if (input.DebugOverrideActive)
                return new TacticalMacroDecision(TacticalDoctrineDecisionKind.Skip, input.VanillaMacro, "debug-override");
            if (input.SaveRestoreMacroActive)
                return new TacticalMacroDecision(TacticalDoctrineDecisionKind.Skip, input.VanillaMacro, "save-restore");
            if (input.VanillaRetreatActive || input.VanillaMacro == 3)
                return new TacticalMacroDecision(TacticalDoctrineDecisionKind.Skip, input.VanillaMacro, "vanilla-retreat");

            if (input.Odds.InferiorForcePosture == TacticalInferiorForcePosture.PreserveOrRetreat && input.Odds.Confidence >= 0.7f)
                return new TacticalMacroDecision(TacticalDoctrineDecisionKind.Apply, 3, "inferior-no-relief");

            if (input.Odds.InferiorForcePosture == TacticalInferiorForcePosture.DelayOnStrongGround)
                return new TacticalMacroDecision(TacticalDoctrineDecisionKind.Apply, 2, "inferior-delay");

            if (input.Odds.Confidence < 0.35f || input.Odds.InferiorForcePosture == TacticalInferiorForcePosture.ProbeOrHold)
                return new TacticalMacroDecision(TacticalDoctrineDecisionKind.Apply, input.VanillaMacro < 0 ? -1 : 2, "no-reliable-contact");

            if (input.Odds.AllowAssault && input.CommanderAggression01 >= 0.45f)
                return new TacticalMacroDecision(TacticalDoctrineDecisionKind.Apply, 0, "confirmed-weak-point");

            float attackThreshold = 1.25f - input.CommanderAggression01 * 0.15f;
            if (input.Odds.DecisiveSectorId >= 0 && input.Odds.CurrentGlobalOdds >= attackThreshold)
                return new TacticalMacroDecision(TacticalDoctrineDecisionKind.Apply, 1, "decisive-sector");

            return new TacticalMacroDecision(TacticalDoctrineDecisionKind.Apply, 2, "hold");
        }

        public static TacticalGroupStanceDecision DecideGroupStance(TacticalGroupStanceDecisionInput input)
        {
            if (!input.WlAllowsControl)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Skip, input.VanillaStance, "wl-control");
            if (!input.OrderFrictionAllowsChange)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Skip, input.VanillaStance, "order-friction");
            if (input.Sector.Confidence < 0.55f)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Skip, input.VanillaStance, "low-confidence");
            if (input.MacroAi < 0)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Skip, input.VanillaStance, "dynamic-macro");

            if (input.Sector.FlankRisk || input.Sector.Mission == TacticalSectorMission.Refuse)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 2, "refuse");
            if (input.Sector.StrongPoint)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 2, "strongpoint");
            if (input.Sector.Mission == TacticalSectorMission.AttackWeakPoint && (input.MacroAi == 0 || input.MacroAi == 1))
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 3, "attack-weak-point");
            if (input.Sector.Mission == TacticalSectorMission.Fix || input.Sector.Mission == TacticalSectorMission.EconomyOfForce)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 1, "fix");
            if (input.Sector.Mission == TacticalSectorMission.Probe)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 1, "probe");

            return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 2, "hold");
        }
    }
}
```

- [ ] **Step 3: Run tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: pure B3/B4/B5 tests pass.

## Task 5: Add Config And Telemetry Surface

**Files:**
- Modify: `src/WhiskeyRealism/Plugin.cs`
- Modify: `src/WhiskeyRealism/Tactical/TacticalBattleContext.cs`
- Modify: `src/WhiskeyRealism/Tactical/TacticalTelemetry.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add config fields**

In `Plugin.cs`, add fields after `EnableWlTacticalChargeGuard`:

```csharp
internal ConfigEntry<bool> EnableTacticalMacroStanceScorer;
internal ConfigEntry<bool> EnableTacticalGroupSectorStance;
```

Bind them in `Awake()` after `EnableWlTacticalChargeGuard`:

```csharp
EnableTacticalMacroStanceScorer = Config.Bind(
    "Tactical",
    "Enable Tactical Macro Stance Scorer",
    false,
    "Default OFF for Slice B4. Uses B3 odds doctrine to bias battle-level macroai after vanilla dynamic macro logic runs.");
EnableTacticalGroupSectorStance = Config.Bind(
    "Tactical",
    "Enable Tactical Group Sector Stance",
    false,
    "Default OFF for Slice B5. Uses B3 sector doctrine to bias group ai_stance without issuing movement, reserve, artillery, fallback, or charge orders.");
```

- [ ] **Step 2: Add context fields**

In `TacticalBattleContext`, add:

```csharp
public string OddsSignature { get; set; }
public string OddsSummary { get; set; }
public int DecisiveSectorId { get; set; }
public float CurrentGlobalOdds { get; set; }
public float ProjectedGlobalOdds { get; set; }
```

Initialize `OddsSignature = ""` and `OddsSummary = ""` in `Empty()`.

- [ ] **Step 3: Add odds telemetry event**

Add `Odds = 11` to `TacticalObservedEvent`.

In `TacticalTelemetry.Prefix(...)`, add:

```csharp
case TacticalObservedEvent.Odds: return "[TacticalOdds]";
```

In `Summary(...)`, append:

```csharp
+ " currentOdds=" + FormatFloat(context.CurrentGlobalOdds)
+ " projectedOdds=" + FormatFloat(context.ProjectedGlobalOdds)
+ " decisive=" + context.DecisiveSectorId
+ " oddsSig=" + Safe(context.OddsSignature)
+ " odds=" + Safe(context.OddsSummary);
```

In `Signature(...)`, append `CurrentGlobalOdds`, `ProjectedGlobalOdds`, `DecisiveSectorId`, and `OddsSignature` with existing bucket/safe helpers.

- [ ] **Step 4: Add telemetry tests**

Add a test registration:

```csharp
("tactical telemetry maps odds prefix", TacticalTelemetryMapsOddsPrefix),
```

Add method:

```csharp
private static void TacticalTelemetryMapsOddsPrefix()
{
    var context = TacticalBattleContext.Empty();
    context.OddsSummary = "posture=probe";
    string summary = TacticalTelemetry.Summary(TacticalObservedEvent.Odds, context);
    AssertTrue(summary.StartsWith("[TacticalOdds]"), "odds prefix");
    AssertContains(summary, "posture=probe", "odds summary");
}
```

- [ ] **Step 5: Run tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: tests pass.

## Task 6: Extend Tactical Observer With B3 Runtime Extraction

**Files:**
- Modify: `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs`

- [ ] **Step 1: Add B3 extraction helpers**

Add helper methods near `BuildContext(...)`:

```csharp
private static TacticalOddsAssessment BuildOddsDoctrine(AIBattle battle, BattleUnits bunits, IList unitsUsed, int side, TacticalBattleContext context)
{
    float own = SafeSideInfoFloat(bunits, side, "totalactiveforce");
    float confirmedEnemy = EstimateVisibleEnemyStrength(unitsUsed);
    float inferredEnemy = EstimateInferredEnemyStrength(unitsUsed);
    float reinf = SafeReinforcements(bunits, side);
    var contact = TacticalContactLedger.Classify(new TacticalContactInput(
        confirmedEnemy,
        confirmedEnemy,
        inferredEnemy,
        secondsSinceLastConfirmed: confirmedEnemy > 0f ? 0f : 9999f,
        receivedFire: AnyReceivedFire(unitsUsed),
        inFog: confirmedEnemy <= 0f));
    var sectors = BuildSectorAssessments(battle, unitsUsed);

    return TacticalOddsDoctrine.Evaluate(new TacticalOddsInput(
        own,
        confirmedEnemy,
        confirmedEnemy,
        inferredEnemy,
        reinf,
        terrainAdvantage: 0f,
        contact,
        sectors));
}
```

Add these support helpers with reflection-free safe reads where possible:

```csharp
private static float EstimateVisibleEnemyStrength(IList units)
{
    if (units == null) return 0f;
    float total = 0f;
    for (int i = 0; i < units.Count; i++)
    {
        var unit = units[i] as Regiment;
        if (unit == null || unit.unitrange == null) continue;
        if (unit.unitrange.closestenemyunitfarreg != null) total += Math.Max(0, unit.unitrange.closestenemyunitfarreg.strength);
        else if (unit.unitrange.closestenemyunit != null) total += 100f;
    }
    return total;
}

private static float EstimateInferredEnemyStrength(IList units)
{
    if (units == null) return 0f;
    float total = 0f;
    for (int i = 0; i < units.Count; i++)
    {
        var unit = units[i] as Regiment;
        if (unit == null || unit.unitrange == null || unit.unitrange.enemystrengthwithinangle == null) continue;
        for (int j = 0; j < unit.unitrange.enemystrengthwithinangle.Length; j++)
            total += Math.Max(0f, unit.unitrange.enemystrengthwithinangle[j]);
    }
    return total;
}

private static bool AnyReceivedFire(IList units)
{
    if (units == null) return false;
    for (int i = 0; i < units.Count; i++)
    {
        var unit = units[i] as Regiment;
        if (unit != null && unit.receivedfire != null && unit.receivedfire.Count > 0) return true;
    }
    return false;
}
```

- [ ] **Step 2: Add sector assessment extraction**

Add:

```csharp
private static TacticalSectorAssessment[] BuildSectorAssessments(AIBattle battle, IList units)
{
    if (units == null) return Array.Empty<TacticalSectorAssessment>();
    var sectors = new List<TacticalSectorAssessment>();
    int sectorId = 0;
    for (int i = 0; i < units.Count; i++)
    {
        var group = units[i] as Regiment;
        if (group == null || group.unittyp <= 13) continue;
        float own = Math.Max(group.groupowninrange, group.groupstrengthaigroup);
        float enemy = Math.Max(1f, group.groupenemiesinrange);
        float confidence = group.unitrange != null && group.unitrange.closestenemyunitfarreg != null ? 0.8f : 0.45f;
        bool flankRisk = group.flanksthreated > 0f;
        bool strongPoint = false;
        sectors.Add(new TacticalSectorAssessment(sectorId++, TacticalSectorSource.AngleSlice, own, enemy, confidence, strongPoint, flankRisk, TacticalSectorMission.Hold));
    }
    return sectors.ToArray();
}
```

This extraction intentionally uses public group fields already referenced by `TacticalObserverPatch`; do not add a broad reflection scan in the battle tick.

- [ ] **Step 3: Populate context in `BuildContext(...)`**

After `context.ReinforcementsWithin24Hours = SafeReinforcements(bunits, side);`, add:

```csharp
var odds = BuildOddsDoctrine(battle, bunits, unitsUsed, side, context);
context.CurrentGlobalOdds = odds.CurrentGlobalOdds;
context.ProjectedGlobalOdds = odds.ProjectedGlobalOdds;
context.DecisiveSectorId = odds.DecisiveSectorId;
context.OddsSignature = "cur=" + BucketForObserver(odds.CurrentGlobalOdds) + ",proj=" + BucketForObserver(odds.ProjectedGlobalOdds) + ",decisive=" + odds.DecisiveSectorId + ",posture=" + odds.InferiorForcePosture;
context.OddsSummary = "posture=" + odds.InferiorForcePosture + ",confidence=" + odds.Confidence.ToString("0.00") + ",assault=" + (odds.AllowAssault ? "1" : "0");
```

Add:

```csharp
private static string BucketForObserver(float value)
{
    if (float.IsNaN(value) || float.IsInfinity(value)) return "0.0";
    return (Math.Round(value * 2f) / 2f).ToString("0.0");
}
```

- [ ] **Step 4: Emit odds telemetry**

In `CheckGlobalAIStrategyPostfix`, after macro observe:

```csharp
Observe(__instance, TacticalObservedEvent.Odds, null, null);
```

- [ ] **Step 5: Run tests and build**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected: tests pass and build succeeds with 0 errors.

## Task 7: Implement B4 Macro Postfix

**Files:**
- Create: `src/WhiskeyRealism/Patches/BattleMacroStrategyPatch.cs`

- [ ] **Step 1: Create patch skeleton**

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    [HarmonyPatch(typeof(AIBattle), "CheckGlobalAIStrategy")]
    internal static class BattleMacroStrategyPatch
    {
        private static readonly Dictionary<string, float> _lastLoggedAt = new Dictionary<string, float>();
        private static FieldInfo _macroAiField;
        private static FieldInfo _sideOfAiField;
        private static FieldInfo _bunitsField;

        [HarmonyPostfix]
        internal static void Postfix(AIBattle __instance)
        {
            if (!Enabled() || __instance == null) return;
            try
            {
                Apply(__instance);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-macro:failed", "Tactical macro stance scorer failed: " + ex.Message);
            }
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null
                && Plugin.Instance.Enabled.Value
                && Plugin.Instance.EnableTacticalMacroStanceScorer.Value;
        }
    }
}
```

- [ ] **Step 2: Add guarded apply logic**

Inside the class, add:

```csharp
private static void Apply(AIBattle battle)
{
    int side = SafeIntField(battle, ref _sideOfAiField, "sideofai", -1);
    int vanillaMacro = SafeIntField(battle, ref _macroAiField, "macroai", -99);
    var bunits = SafeField<BattleUnits>(battle, ref _bunitsField, "bunits");
    if (side < 0 || bunits == null) return;

    bool debugOverride = GameVars.aistrategy >= 0;
    bool saveRestore = SideInfoMacro(bunits, side) >= 0;
    bool vanillaRetreat = vanillaMacro == 3 || EndBattleActive(bunits);

    var odds = BuildRuntimeOdds(battle, bunits, side);
    float aggression = CommanderAggression01(bunits, side);
    var decision = TacticalDoctrineScorer.DecideMacro(new TacticalMacroDecisionInput(
        vanillaMacro,
        debugOverride,
        saveRestore,
        vanillaRetreat,
        aggression,
        odds));

    if (decision.Kind != TacticalDoctrineDecisionKind.Apply) return;
    if (decision.MacroAi == vanillaMacro) return;
    if (decision.MacroAi < -1 || decision.MacroAi > 3) return;

    _macroAiField.SetValue(battle, decision.MacroAi);
    LogDecision(side, vanillaMacro, decision, odds);
}
```

Reuse or duplicate narrowly scoped safe helpers from `TacticalObserverPatch`; do not make those helpers public until a later cleanup needs shared runtime extraction.

- [ ] **Step 3: Add runtime odds helper**

Implement `BuildRuntimeOdds(...)` with the same DTO extraction shape as Task 6. Keep it local to this patch for now:

```csharp
private static TacticalOddsAssessment BuildRuntimeOdds(AIBattle battle, BattleUnits bunits, int side)
{
    float forceBalance = SafeSideInfoFloat(bunits, side, "forcebalance");
    float own = Math.Max(1f, SafeSideInfoFloat(bunits, side, "totalactiveforce"));
    float enemy = forceBalance > 0f ? own * Math.Max(0.1f, (1f - forceBalance) / Math.Max(0.1f, forceBalance)) : own;
    float reinf = SafeSideInfoFloat(bunits, side, "reinforcementarrivalswithin24hrs");
    var contact = TacticalContactLedger.Classify(new TacticalContactInput(enemy, enemy, enemy, 0f, receivedFire: false, inFog: false));
    return TacticalOddsDoctrine.Evaluate(new TacticalOddsInput(own, enemy, enemy, enemy, reinf, terrainAdvantage: 0f, contact, Array.Empty<TacticalSectorAssessment>()));
}
```

- [ ] **Step 4: Add logging and safe helpers**

Add bounded log:

```csharp
private static void LogDecision(int side, int vanillaMacro, TacticalMacroDecision decision, TacticalOddsAssessment odds)
{
    string signature = side + "|" + vanillaMacro + "|" + decision.MacroAi + "|" + decision.Reason + "|" + odds.DecisiveSectorId + "|" + odds.InferiorForcePosture;
    if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "macro-decision", signature, UnityEngine.Time.realtimeSinceStartup, 30f, false)) return;
    Plugin.Log.LogInfo("[TacticalMacroDecision] side=" + side + " old=" + TacticalTelemetry.MacroName(vanillaMacro) + " whiskey=" + TacticalTelemetry.MacroName(decision.MacroAi) + " reason=" + decision.Reason + " current=" + odds.CurrentGlobalOdds.ToString("0.00") + " projected=" + odds.ProjectedGlobalOdds.ToString("0.00") + " confidence=" + odds.Confidence.ToString("0.00"));
}
```

Add safe field helpers:

```csharp
private static int SafeIntField(object instance, ref FieldInfo field, string name, int fallback)
{
    try
    {
        if (instance == null) return fallback;
        if (field == null) field = AccessTools.Field(instance.GetType(), name);
        if (field == null) return fallback;
        return Convert.ToInt32(field.GetValue(instance));
    }
    catch { return fallback; }
}

private static T SafeField<T>(object instance, ref FieldInfo field, string name) where T : class
{
    try
    {
        if (instance == null) return null;
        if (field == null) field = AccessTools.Field(instance.GetType(), name);
        return field == null ? null : field.GetValue(instance) as T;
    }
    catch { return null; }
}
```

Add side-info helpers:

```csharp
private static float SafeSideInfoFloat(BattleUnits bunits, int side, string fieldName)
{
    try
    {
        if (bunits == null || bunits.sideinformation == null || side < 0 || side >= bunits.sideinformation.Length) return 0f;
        var info = bunits.sideinformation[side];
        var field = AccessTools.Field(info.GetType(), fieldName);
        if (field == null) return 0f;
        object value = field.GetValue(info);
        return value == null ? 0f : Convert.ToSingle(value);
    }
    catch { return 0f; }
}

private static int SideInfoMacro(BattleUnits bunits, int side)
{
    try
    {
        if (bunits == null || bunits.sideinformation == null || side < 0 || side >= bunits.sideinformation.Length) return -1;
        return bunits.sideinformation[side].macroai;
    }
    catch { return -1; }
}

private static bool EndBattleActive(BattleUnits bunits)
{
    try { return bunits != null && bunits.endbattle >= 0; }
    catch { return false; }
}

private static float CommanderAggression01(BattleUnits bunits, int side)
{
    try
    {
        if (bunits == null || side < 0 || side >= bunits.alliance.Length) return 0.5f;
        int commanderId = bunits.GetCommandingOfficerFromSide(side);
        if (commanderId < 0 || commanderId >= GameVars.commander.Length) return 0.5f;
        float initiative = GameVars.commander[commanderId].GetCommanderInitiative();
        if (float.IsNaN(initiative) || float.IsInfinity(initiative)) return 0.5f;
        if (initiative < 0f) return 0f;
        if (initiative > 1f) return 1f;
        return initiative;
    }
    catch { return 0.5f; }
}
```

- [ ] **Step 5: Run tests and build**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected: tests pass and build succeeds with 0 errors.

## Task 8: Implement B5 Group Stance Postfix

**Files:**
- Create: `src/WhiskeyRealism/Patches/BattleGroupStancePatch.cs`

- [ ] **Step 1: Create patch skeleton**

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    [HarmonyPatch(typeof(AIBattle), "AdjustGroupAIStance")]
    internal static class BattleGroupStancePatch
    {
        private static readonly Dictionary<string, float> _lastLoggedAt = new Dictionary<string, float>();
        private static FieldInfo _macroAiField;
        private static FieldInfo _sideOfAiField;
        private static FieldInfo _bunitsField;
        private static FieldInfo _unitsUsedField;
        private static FieldInfo _orderedStanceField;

        [HarmonyPostfix]
        internal static void Postfix(AIBattle __instance)
        {
            if (!Enabled() || __instance == null) return;
            try
            {
                Apply(__instance);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-group-stance:failed", "Tactical group sector stance failed: " + ex.Message);
            }
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null
                && Plugin.Instance.Enabled.Value
                && Plugin.Instance.EnableTacticalGroupSectorStance.Value;
        }
    }
}
```

- [ ] **Step 2: Add group loop**

Add:

```csharp
private static void Apply(AIBattle battle)
{
    int side = SafeIntField(battle, ref _sideOfAiField, "sideofai", -1);
    int macro = SafeIntField(battle, ref _macroAiField, "macroai", -99);
    var bunits = SafeField<BattleUnits>(battle, ref _bunitsField, "bunits");
    var units = SafeList(battle, ref _unitsUsedField, "unitsused");
    if (side < 0 || macro < 0 || bunits == null || units == null) return;

    for (int i = 0; i < units.Count; i++)
    {
        var group = units[i] as Regiment;
        if (group == null || group.unittyp <= 13) continue;
        ApplyGroup(bunits, side, macro, group, i);
    }
}
```

- [ ] **Step 3: Add group decision apply**

Add:

```csharp
private static void ApplyGroup(BattleUnits bunits, int side, int macro, Regiment group, int index)
{
    if (!WlAllowsControl(group)) return;
    if (!OrderFrictionAllowsChange(group)) return;

    var sector = BuildGroupSector(group, index);
    var decision = TacticalDoctrineScorer.DecideGroupStance(new TacticalGroupStanceDecisionInput(
        SafeIntField(group, ref _orderedStanceField, "ai_" + "stanceordered", group.ai_stanceordered),
        macro,
        sector,
        orderFrictionAllowsChange: true,
        wlAllowsControl: true));

    if (decision.Kind != TacticalDoctrineDecisionKind.Apply) return;
    if (decision.GroupStance == group.ai_stanceordered) return;
    if (decision.GroupStance == 4) return;
    if (decision.GroupStance < 0 || decision.GroupStance > 3) return;

    bunits.ChangeStance(UnityObject(group), decision.GroupStance, immediate: false, overwriteaigroups: false);
    group.ai_stance = decision.GroupStance;
    group.ai_stanceordered = decision.GroupStance;
    group.lastaistancechangetime = GameVars.currenttimefromstart;
    LogDecision(side, group, sector, decision);
}
```

Use `((UnityEngine.Component)group).gameObject` inside a helper named `UnityObject(...)` so Unity casts stay isolated.

- [ ] **Step 4: Add B5 guard helpers**

Add:

```csharp
private static bool WlAllowsControl(Regiment group)
{
    var decision = TacticalWlActionGuard.Decide(
        configEnabled: true,
        dlcScenarioActive: DLC_WL.dlc_scenarioactive,
        action: TacticalWlGuardAction.FeudMovement,
        unitUnderCommander: group != null && group.dlcw_isundercommander,
        groupUnderCommander: group != null && group.dlcw_isundercommander,
        attachedUnitUnderCommander: AttachedUnitUnderPlayerCommander(group));
    return decision.Allow;
}

private static bool AttachedUnitUnderPlayerCommander(Regiment group)
{
    if (group == null || group.allattachedunits == null) return false;
    for (int i = 0; i < group.allattachedunits.Length; i++)
    {
        var unit = group.allattachedunits[i];
        if (unit != null && unit.dlcw_isundercommander) return true;
    }
    return false;
}

private static bool OrderFrictionAllowsChange(Regiment group)
{
    if (group == null) return false;
    if (group.regimentpaths > 0 && group.pathinterrupted) return false;
    if (group.regimentpaths > 0 && group.movementmode == 3) return false;
    return true;
}
```

- [ ] **Step 5: Add sector builder and logging**

Add:

```csharp
private static TacticalSectorAssessment BuildGroupSector(Regiment group, int index)
{
    float own = Math.Max(group.groupowninrange, group.groupstrengthaigroup);
    float enemy = Math.Max(1f, group.groupenemiesinrange);
    float confidence = group.unitrange != null && group.unitrange.closestenemyunitfarreg != null ? 0.8f : 0.45f;
    bool flankRisk = group.flanksthreated > 0f || group.outflanked > 0;
    bool strongPoint = group.covervalue > 0.5f || group.fortinrange;
    var sector = new TacticalSectorAssessment(index, TacticalSectorSource.AngleSlice, own, enemy, confidence, strongPoint, flankRisk, TacticalSectorMission.Hold);
    var result = TacticalSectorLedger.Evaluate(new[] { sector });
    return result.Sectors.Length > 0 ? result.Sectors[0] : sector;
}

private static void LogDecision(int side, Regiment group, TacticalSectorAssessment sector, TacticalGroupStanceDecision decision)
{
    string signature = side + "|" + SafeInstanceId(group) + "|" + sector.SectorId + "|" + decision.GroupStance + "|" + decision.Reason;
    if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "group-decision", signature, UnityEngine.Time.realtimeSinceStartup, 30f, false)) return;
    Plugin.Log.LogInfo("[TacticalGroupDecision] side=" + side + " group=" + SafeInstanceId(group) + " sector=" + sector.SectorId + " stance=" + decision.GroupStance + " mission=" + sector.Mission + " reason=" + decision.Reason + " odds=" + sector.Odds.ToString("0.00") + " confidence=" + sector.Confidence.ToString("0.00"));
}
```

- [ ] **Step 6: Run tests and build**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected: tests pass and build succeeds with 0 errors.

## Task 9: Deploy And Smoke

**Files:**
- Runtime DLL: `dist/WhiskeyRealism.dll`
- Game plugin DLL: `/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll`
- Log: `/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log`
- Config: `/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/config/dev.kyle.whiskey-realism.cfg`

- [ ] **Step 1: Run final local verification**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
git diff --check
```

Expected:

- console harness prints PASS with 0 FAIL;
- build prints `Build succeeded.` and `0 Error(s)`;
- `git diff --check` exits 0.

- [ ] **Step 2: Deploy and hash verify**

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: the two SHA-256 hashes match. If `cp` fails with `Invalid argument`, the game is running; stop and ask the user to close it.

- [ ] **Step 3: Enable focused smoke config**

Set these values in `dev.kyle.whiskey-realism.cfg`:

```ini
[Tactical]
Enable Tactical Observer = true
Enable Tactical Macro Stance Scorer = true
Enable Tactical Group Sector Stance = true
Enable W&L Tactical Charge Guard = true
Enable Tactical Bug Telemetry = true
Tactical Observer Verbose Logging = false
Tactical Observer Min Seconds Between Summaries = 30
```

- [ ] **Step 4: Runtime smoke**

Start or continue a W&L land battle, then run:

```bash
rg -n "TacticalOdds|TacticalSector|TacticalMacroDecision|TacticalGroupDecision|TacticalChargeGuard|TacticalFeudGuard|Exception|TargetInvocationException|Harmony|missing-anchor|failed-owned" "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Pass criteria:

- `[TacticalOdds]` and `[TacticalSector]` appear;
- `[TacticalMacroDecision]` appears only on bounded signature changes;
- `[TacticalGroupDecision]` appears only on bounded signature changes;
- no repeated `Exception`, `TargetInvocationException`, or Harmony failure;
- no no-contact instant all-army assault;
- no repeated macro flip-flop;
- no group decision writes charge stance;
- no protected W&L player-subordinate retask when B1 guard applies.

## Task 10: Documentation Closeout

**Files:**
- Modify: `docs/patch-catalog.md`
- Modify: `docs/handoff.md`
- Modify: `MEMORY.md`
- Modify: `README.md`

- [ ] **Step 1: Update patch catalog**

Add:

- #44 `BattleMacroStrategyPatch`
- #45 `BattleGroupStancePatch`

Keep B3 pure ledgers listed as helper/runtime rows, not numbered Harmony patches.

- [ ] **Step 2: Update handoff**

Record:

- B3 odds doctrine implemented;
- B4 macro scorer implemented;
- B5 group sector stance implemented;
- build/deploy/hash result;
- smoke result, including whether B4/B5 behavior exercised.

- [ ] **Step 3: Update repo memory and README**

Add a concise current-state bullet to `MEMORY.md` and adjust README tactical status only after deployed hash verification.

- [ ] **Step 4: Final verification**

Run:

```bash
git diff --check
git status --short
```

Expected: no whitespace errors; status shows only intended source/doc changes.

- [ ] **Step 5: Commit**

```bash
git add src/WhiskeyRealism tests/WhiskeyRealism.Tests docs README.md MEMORY.md
git commit -m "feat: add tactical odds macro sector doctrine"
```

Commit only after tests/build/deploy/hash verification are complete.
