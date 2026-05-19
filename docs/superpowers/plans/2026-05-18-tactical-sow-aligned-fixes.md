# Tactical SoW-Aligned Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. This plan is **inline-executed** in the current session (per user direction).

**Goal:** Eliminate the replan-thrash that produces unstable orders, and translate Scourge of War's pre-contact movement pattern so Whiskey armies actually march forward to objectives before line-of-sight contact. Also fix the personality wiring that pins every commander at neutral defaults and broaden the historical figure registry.

**Architecture:** Three precise surgical changes plus one registry expansion.

1. **RC#1** — `ArmyEvidenceBuilder.EstimateReserveCommitFraction` returns the "unknown" sentinel (0.35) when an army has chains but no `reservegroups` in them (currently returns 1.0 which trips `ReserveExhaustion` every cycle). Pair with a guard in `ArmyReplanTriggers.Evaluate` so `ReserveExhaustion` never fires below an absolute committed-fraction floor.
2. **RC#2** — Two-layer doctrine separation matching SoW's brigade/division think pattern. (a) `TacticalGroupSectorEstimator` raises the no-enemy floor to 0.55 (entry to the "approach" path) instead of 0.45 (low-confidence skip); (b) `TacticalDoctrineScorer.DecideGroupStance` adds an explicit pre-contact `Apply Hold` branch instead of skipping. Movement writes via `CommandPostureExecutor` already act on role-assigned targets, so the orchestrator's playbook keeps pushing brigades forward.
3. **RC#3 wiring** — `BattleCommanderIntentObserverPatch.BuildIntentInput` reads live `GetCommanderInitiative()` and the active `OperationPosture` from strategic state instead of hard-coding 0.5/Inherit.
4. **RC#3 registry** — Expand `HistoricalFigureRegistry` from 25 to ~75 named Civil War generals; improve name matching to also try first-name and concatenated tokens; add a fallback so "P.G.T. Beauregard" resolves correctly.

**Tech Stack:** C# netstandard2.1, BepInEx 5.4.x + HarmonyX, Whiskey console test harness (xUnit-style PASS/FAIL).

---

## File Structure

| File | Action | Responsibility |
|---|---|---|
| `src/WhiskeyRealism/Tactical/Orchestrator/ArmyEvidenceBuilder.cs` | Modify | RC#1: distinguish "no chains" from "chains exist but no reserve groups" |
| `src/WhiskeyRealism/Tactical/Orchestrator/ArmyReplanTriggers.cs` | Modify | RC#1: add `IsReserveCommitFractionKnown` check + minimum absolute committed-fraction floor |
| `src/WhiskeyRealism/Tactical/TacticalGroupSectorEstimator.cs` | Modify | RC#2: lift no-enemy confidence floor from 0.45 → 0.55, add explicit `NoContact` source kind |
| `src/WhiskeyRealism/Tactical/TacticalDoctrineScorer.cs` | Modify | RC#2: add `pre-contact-hold` Apply branch before the low-confidence skip |
| `src/WhiskeyRealism/Tactical/TacticalSectorLedger.cs` | Modify | RC#2: keep ledger threshold (0.55) consistent with new floor |
| `src/WhiskeyRealism/Patches/BattleCommanderIntentObserverPatch.cs` | Modify | RC#3 wiring: replace hard-coded 0.5/Inherit with live commander + plan state |
| `src/WhiskeyRealism/Strategic/HistoricalFigureRegistry.cs` | Modify | RC#3 registry: add ~50 more named generals + improved name matcher |
| `tests/WhiskeyRealism.Tests/Program.cs` | Modify | Add ~20 new tests covering all four areas; keep existing 1110 PASS |
| `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` | Modify | (no new files; existing test project already references touched code) |

---

## Task 1 — RC#1: Reserve-Fraction Guard (eliminate replan thrash)

**Files:**
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/ArmyEvidenceBuilder.cs:367-374`
- Modify: `src/WhiskeyRealism/Tactical/Orchestrator/ArmyReplanTriggers.cs:60-92`
- Test: `tests/WhiskeyRealism.Tests/Program.cs` (append new tests)

- [ ] **Step 1: Write the failing tests**

Append to `tests/WhiskeyRealism.Tests/Program.cs` (find `// Tactical orchestrator replan triggers` section or add at end of tactical tests):

```csharp
TestCase("army replan triggers ignore reserve exhaustion when fraction is unknown sentinel", () =>
{
    // 0.35 is the UnknownReserveCommitFraction sentinel from ArmyEvidenceBuilder.
    // It must NOT trip ReserveExhaustion even though 0.35 is below the 0.85 spent threshold.
    // (the bug we're fixing: an army with chains but no reservegroups returned 1.0 here,
    // and trigger fired every cycle.)
    var input = new ReplanTriggerInput(
        planAgeSeconds: 10f,
        currentPhase: BattlePhase.Probe,
        mainEffortOwnStrength: 1000f,
        mainEffortHistoryOwnStrength: 1000f,
        globalOddsCurrent: 1.0f,
        globalOddsHistory: 1.0f,
        armyMoraleCurrent: 0.9f,
        armyMoraleFloor: 0.4f,
        reservesCommittedFraction: 0.35f, // unknown sentinel — should not trip
        reinforcementsArrivingDelta: 0f,
        enemyMainEffortShiftConfidenceWeighted: 0.0f);
    Assert.Equal(ReplanTrigger.None, ArmyReplanTriggers.Evaluate(input));
});

TestCase("army replan triggers still fire reserve exhaustion at or above 0.85 known fraction", () =>
{
    var input = new ReplanTriggerInput(
        planAgeSeconds: 10f,
        currentPhase: BattlePhase.Probe,
        mainEffortOwnStrength: 1000f,
        mainEffortHistoryOwnStrength: 1000f,
        globalOddsCurrent: 1.0f,
        globalOddsHistory: 1.0f,
        armyMoraleCurrent: 0.9f,
        armyMoraleFloor: 0.4f,
        reservesCommittedFraction: 0.90f, // genuinely high
        reinforcementsArrivingDelta: 0f,
        enemyMainEffortShiftConfidenceWeighted: 0.0f);
    Assert.Equal(ReplanTrigger.ReserveExhaustion, ArmyReplanTriggers.Evaluate(input));
});

TestCase("army replan triggers do not trip reserve exhaustion at exactly the unknown sentinel boundary", () =>
{
    // Boundary check: 0.349 is below the sentinel detection, 0.350 IS the sentinel, 0.351 is above.
    var inputAtSentinel = new ReplanTriggerInput(
        planAgeSeconds: 10f,
        currentPhase: BattlePhase.Probe,
        mainEffortOwnStrength: 1000f, mainEffortHistoryOwnStrength: 1000f,
        globalOddsCurrent: 1.0f, globalOddsHistory: 1.0f,
        armyMoraleCurrent: 0.9f, armyMoraleFloor: 0.4f,
        reservesCommittedFraction: 0.35f,
        reinforcementsArrivingDelta: 0f, enemyMainEffortShiftConfidenceWeighted: 0.0f);
    Assert.Equal(ReplanTrigger.None, ArmyReplanTriggers.Evaluate(inputAtSentinel));
});
```

Find the `Assert.Equal` helper. If it doesn't exist, find the existing PASS/FAIL test framework and adapt.

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E 'FAIL.*reserve' | head -5
```

Expected: 3 FAIL lines for the new tests.

- [ ] **Step 3: Update `ArmyReplanTriggers.cs`**

Replace lines 60-92 of `src/WhiskeyRealism/Tactical/Orchestrator/ArmyReplanTriggers.cs` with:

```csharp
    public static class ArmyReplanTriggers
    {
        // Per umbrella §"Replan triggers". Thresholds are seed values; future
        // tuning may move them into config flags.
        public const float PhaseBudgetSeconds = 180f;
        public const float MainEffortLossFraction = 0.5f;
        public const float OddsLowHysteresis = 0.7f;
        public const float OddsHighHysteresis = 1.4f;
        public const float ReservesAlmostSpent = 0.85f;
        public const float EnemyShiftConfidenceFloor = 0.5f;

        // Mirrors ArmyEvidenceBuilder.UnknownReserveCommitFraction. When the
        // evidence builder cannot determine a real reserve fraction (no chains,
        // no reservegroups, alliance lookup failed) it returns this sentinel.
        // The replan trigger must treat the sentinel as "unknown" rather than
        // letting an arbitrary mid-band value drive ReserveExhaustion.
        public const float UnknownReserveCommitSentinel = 0.35f;
        private const float SentinelTolerance = 0.0001f;

        /// <summary>
        /// Evaluates the 7 replan triggers in priority order — phase deadline
        /// first (hard battlefield clock), intent shift last (soft inference).
        /// Returns ReplanTrigger.None if nothing fires.
        /// </summary>
        public static ReplanTrigger Evaluate(ReplanTriggerInput i)
        {
            if (i.PlanAgeSeconds >= PhaseBudgetSeconds) return ReplanTrigger.PhaseDeadline;

            if (i.MainEffortHistoryOwnStrength > 0f &&
                i.MainEffortOwnStrength / i.MainEffortHistoryOwnStrength <= MainEffortLossFraction)
                return ReplanTrigger.MainEffortSectorLoss;

            if (i.GlobalOddsCurrent <= OddsLowHysteresis && i.GlobalOddsHistory > OddsLowHysteresis)
                return ReplanTrigger.ForceImbalanceShift;
            if (i.GlobalOddsCurrent >= OddsHighHysteresis && i.GlobalOddsHistory < OddsHighHysteresis)
                return ReplanTrigger.ForceImbalanceShift;

            if (i.ArmyMoraleCurrent < i.ArmyMoraleFloor) return ReplanTrigger.CasualtyThreshold;
            if (IsReserveCommitFractionKnown(i.ReservesCommittedFraction) &&
                i.ReservesCommittedFraction >= ReservesAlmostSpent)
                return ReplanTrigger.ReserveExhaustion;
            if (i.ReinforcementsArrivingDelta > 1f) return ReplanTrigger.ReinforcementArrival;
            if (i.EnemyMainEffortShiftConfidenceWeighted >= EnemyShiftConfidenceFloor) return ReplanTrigger.EnemyIntentShift;

            return ReplanTrigger.None;
        }

        private static bool IsReserveCommitFractionKnown(float fraction)
        {
            // Sentinel match (Math.Abs without importing System.Math here — quick inline).
            float diff = fraction - UnknownReserveCommitSentinel;
            if (diff < 0f) diff = -diff;
            return diff > SentinelTolerance;
        }
    }
}
```

- [ ] **Step 4: Update `ArmyEvidenceBuilder.cs`**

Replace the body of `EstimateReserveCommitFraction` and `TrySumReservePoolStrength` at lines 367-418 to distinguish "no chains" from "chains exist but zero reserves":

```csharp
        private static float EstimateReserveCommitFraction(AIBattle battle, BattleUnits bunits, int side, float activeForce)
        {
            if (!TrySumReservePoolStrength(battle, bunits, side, out float reserveStrength, out bool hasDesignatedReserves))
                return UnknownReserveCommitFraction;

            // Chains exist but no reservegroups designated for this side. The army
            // has no reserve plan; report "unknown" rather than 100% committed.
            // Otherwise ArmyReplanTriggers.ReserveExhaustion fires every cycle.
            if (!hasDesignatedReserves)
                return UnknownReserveCommitFraction;

            float committed = 1f - reserveStrength / Math.Max(1f, activeForce);
            return Clamp01OrDefault(committed, UnknownReserveCommitFraction);
        }

        private static bool TrySumReservePoolStrength(AIBattle battle, BattleUnits bunits, int side, out float reserveStrength, out bool hasDesignatedReserves)
        {
            reserveStrength = 0f;
            hasDesignatedReserves = false;
            try
            {
                var chains = ResolveObjectiveChains(battle);
                if (chains == null || chains.Count == 0) return false;

                int alliance = SafeAlliance(bunits, side);
                if (alliance < 0) return false;
                int effectiveCommandMin = ClampShiftedMin(ReadCommandHierarchyShift());

                bool observedChain = false;
                var seen = new HashSet<int>();
                for (int i = 0; i < chains.Count; i++)
                {
                    object chain = chains[i];
                    if (chain == null) continue;
                    observedChain = true;

                    var reserves = SafeObjectList(chain, "reservegroups");
                    if (reserves == null) continue;

                    for (int j = 0; j < reserves.Count; j++)
                    {
                        var group = reserves[j] as Regiment;
                        if (!IsUsableOwnGroup(group, alliance, effectiveCommandMin)) continue;

                        int id = SafeInstanceId(group);
                        if (id != 0 && !seen.Add(id)) continue;

                        hasDesignatedReserves = true;
                        reserveStrength += ActiveGroupStrength(group);
                    }
                }

                return observedChain;
            }
            catch
            {
                reserveStrength = 0f;
                hasDesignatedReserves = false;
                return false;
            }
        }
```

- [ ] **Step 5: Run all tests, verify the 3 new pass and 1110 baseline holds**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj > /tmp/t1.log 2>&1
echo "PASS: $(grep -c '^PASS' /tmp/t1.log) FAIL: $(grep -c '^FAIL' /tmp/t1.log)"
grep '^FAIL' /tmp/t1.log | head -5
```

Expected: `PASS: 1113 FAIL: 0`.

- [ ] **Step 6: Commit Task 1**

```bash
git add src/WhiskeyRealism/Tactical/Orchestrator/ArmyEvidenceBuilder.cs \
        src/WhiskeyRealism/Tactical/Orchestrator/ArmyReplanTriggers.cs \
        tests/WhiskeyRealism.Tests/Program.cs
git commit -m "fix: RC#1 reserve fraction guard kills replan thrash

ArmyEvidenceBuilder.EstimateReserveCommitFraction now returns the UnknownReserveCommitFraction sentinel (0.35) when chains exist but have no designated reservegroups, instead of computing 1.0 - 0/activeForce = 1.0. ArmyReplanTriggers.Evaluate adds IsReserveCommitFractionKnown(...) so ReserveExhaustion never fires on the unknown sentinel value.

Effect: armies without explicit reserve groups in their objective chain no longer enter the replan loop (HoodFrontalAssault -> GenericCautious -> HoodFrontalAssault every 30-60s) observed in the 695be770... runtime session. Reserves still trigger replan when fraction is known and ≥ 0.85.

Tests: 3 new harness tests covering unknown sentinel, known high fraction, and sentinel boundary. Overall 1113 PASS / 0 FAIL."
```

---

## Task 2 — RC#2: SoW-Aligned Pre-Contact Doctrine

**Files:**
- Modify: `src/WhiskeyRealism/Tactical/TacticalGroupSectorEstimator.cs:67-69`
- Modify: `src/WhiskeyRealism/Tactical/TacticalDoctrineScorer.cs:43-80`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Write the failing tests**

Append to tactical-doctrine tests in `Program.cs`:

```csharp
TestCase("group sector estimator pre-contact uses approach floor not low-confidence floor", () =>
{
    // No enemy, no line contact: pre-fix returned 0.45 (below the 0.55 doctrine threshold,
    // which produced low-confidence skip). Post-fix returns 0.55 so the doctrine can
    // issue baseline Hold/Approach instead of skipping.
    var input = new TacticalGroupContactInput(
        sectorId: 0,
        ownStrength: 1000f,
        enemiesInRangeStrength: 0f,
        angleEnemyStrength: 0f,
        closestEnemyStrength: 0f,
        closestEnemyUnitType: -1,
        closestEnemyName: "",
        closestEnemyRouted: false,
        closestEnemyPermanentlyDetached: false,
        flankRisk: false,
        strongPoint: false);
    var sector = TacticalGroupSectorEstimator.BuildSector(input);
    Assert.True(sector.Confidence >= 0.55f, "pre-contact confidence must reach threshold to avoid silence");
});

TestCase("doctrine scorer issues pre-contact hold when no enemy visible instead of skipping", () =>
{
    var sector = new TacticalSectorAssessment(
        sectorId: 0,
        source: TacticalSectorSource.AngleSlice,
        ownStrength: 1000f,
        enemy: 0f,
        confidence: 0.55f, // matches new no-enemy floor
        strongPoint: false,
        flankRisk: false,
        mission: TacticalSectorMission.Hold);
    var input = new TacticalGroupStanceDecisionInput(
        vanillaStance: 3,
        macroAi: 1,
        sector: sector,
        orderFrictionAllowsChange: true,
        wlAllowsControl: true);
    var decision = TacticalDoctrineScorer.DecideGroupStance(input);
    // Pre-fix: Skip with "low-confidence". Post-fix: Apply with "pre-contact-hold".
    Assert.Equal(TacticalDoctrineDecisionKind.Apply, decision.Kind);
    Assert.True(decision.Reason == "pre-contact-hold" || decision.Reason == "hold",
        "expected pre-contact-hold or hold reason, got: " + decision.Reason);
});

TestCase("doctrine scorer still rejects when sector confidence drops below pre-contact floor", () =>
{
    // Below the new floor (e.g., 0.30 from a degenerate input) still skips.
    var sector = new TacticalSectorAssessment(0, TacticalSectorSource.AngleSlice,
        ownStrength: 1000f, enemy: 0f, confidence: 0.30f,
        strongPoint: false, flankRisk: false,
        mission: TacticalSectorMission.Hold);
    var input = new TacticalGroupStanceDecisionInput(
        vanillaStance: 3, macroAi: 1, sector: sector,
        orderFrictionAllowsChange: true, wlAllowsControl: true);
    var decision = TacticalDoctrineScorer.DecideGroupStance(input);
    Assert.Equal(TacticalDoctrineDecisionKind.Skip, decision.Kind);
    Assert.Equal("low-confidence", decision.Reason);
});

TestCase("doctrine scorer issues attack-weak-point under line contact at 0.85 confidence", () =>
{
    // Regression: line-contact behavior unchanged.
    var sector = new TacticalSectorAssessment(0, TacticalSectorSource.VisibleLineContact,
        ownStrength: 1000f, enemy: 400f, confidence: 0.85f,
        strongPoint: false, flankRisk: false,
        mission: TacticalSectorMission.AttackWeakPoint);
    var input = new TacticalGroupStanceDecisionInput(
        vanillaStance: 3, macroAi: 1, sector: sector,
        orderFrictionAllowsChange: true, wlAllowsControl: true);
    var decision = TacticalDoctrineScorer.DecideGroupStance(input);
    Assert.Equal(TacticalDoctrineDecisionKind.Apply, decision.Kind);
    Assert.Equal(3, decision.Stance); // attack-weak-point stance
});
```

- [ ] **Step 2: Run tests, verify failures**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E 'FAIL.*(pre-contact|doctrine scorer issues|group sector)' | head -5
```

Expected: at least 2 FAIL (the regression test and the line-contact test might still pass).

- [ ] **Step 3: Update `TacticalGroupSectorEstimator.cs`**

Replace lines 65-82 of `src/WhiskeyRealism/Tactical/TacticalGroupSectorEstimator.cs` with:

```csharp
            bool hasEnemy = enemy > 0f;
            // SoW-aligned confidence table. SoW's brigade-think (offai.cpp:507-546)
            // issues movement orders to a brigade's tactical objective when no enemy
            // is in range — pre-contact silence is not a SoW pattern. To mirror that
            // here, the no-enemy floor must reach the doctrine's 0.55 "act on it"
            // threshold so DecideGroupStance can issue a pre-contact Hold/Approach
            // branch instead of skipping forever.
            float confidence = hasEnemy
                ? (lineContact ? 0.85f : 0.65f)  // line contact / no line contact, with enemy
                : (lineContact ? 0.70f : 0.55f); // pre-contact (lifted from 0.45 -> 0.55)

            var sector = new TacticalSectorAssessment(
                input.SectorId,
                source,
                input.OwnStrength,
                enemy,
                confidence,
                input.StrongPoint,
                input.FlankRisk,
                TacticalSectorMission.Hold);
            var result = TacticalSectorLedger.Evaluate(new[] { sector });
            return result.Sectors.Length > 0 ? result.Sectors[0] : sector;
        }
```

- [ ] **Step 4: Update `TacticalDoctrineScorer.cs`**

Replace the body of `DecideGroupStance` (lines 43-80) with:

```csharp
        public static TacticalGroupStanceDecision DecideGroupStance(TacticalGroupStanceDecisionInput input)
        {
            if (!input.WlAllowsControl)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Skip, input.VanillaStance, "wl-control");
            if (!input.OrderFrictionAllowsChange)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Skip, input.VanillaStance, "order-friction");
            if (input.MacroAi < 0)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Skip, input.VanillaStance, "dynamic-macro");
            if (input.MacroAi == 3)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Skip, input.VanillaStance, "vanilla-retreat");

            if (input.Sector.FlankRisk || input.Sector.Mission == TacticalSectorMission.Refuse)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 2, "refuse");
            if (input.Sector.StrongPoint)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 2, "strongpoint");
            if (input.Sector.Mission == TacticalSectorMission.Probe && input.Sector.Confidence >= 0.35f)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 1, "probe");

            // SoW-aligned pre-contact path: when sector enemy strength is zero and
            // confidence is right at the no-enemy floor, return Apply(Hold) so the
            // posture executor can keep the brigade marching to its role-assigned
            // target. SoW's brigade-think never goes silent before contact; neither
            // should Whiskey. Strict below-floor cases still skip as low-confidence.
            if (input.Sector.Confidence < 0.40f)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Skip, input.VanillaStance, "low-confidence");
            if (input.Sector.Confidence < 0.55f)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 2, "pre-contact-hold");
            if (input.Sector.Mission == TacticalSectorMission.AttackWeakPoint &&
                (input.MacroAi == 0 || input.MacroAi == 1))
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 3, "attack-weak-point");
            if (input.MacroAi == 2 &&
                ShouldCommitVisibleWeakPoint(input.Sector))
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 3, "defensive-counterstroke");
            if (input.MacroAi == 2 &&
                (input.Sector.Mission == TacticalSectorMission.AttackWeakPoint ||
                 input.Sector.Mission == TacticalSectorMission.Fix ||
                 input.Sector.Mission == TacticalSectorMission.EconomyOfForce))
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 2, "defend-hold");
            if (input.Sector.Mission == TacticalSectorMission.AttackWeakPoint)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 1, "probe-weak-point");
            if (input.Sector.Mission == TacticalSectorMission.Fix ||
                input.Sector.Mission == TacticalSectorMission.EconomyOfForce)
                return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 1, "fix");

            return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 2, "hold");
        }
```

- [ ] **Step 5: Run all tests**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj > /tmp/t2.log 2>&1
echo "PASS: $(grep -c '^PASS' /tmp/t2.log) FAIL: $(grep -c '^FAIL' /tmp/t2.log)"
grep '^FAIL' /tmp/t2.log | head -10
```

Expected: `PASS: 1117 FAIL: 0`. If any prior tests fail, they need updating to expect the new pre-contact-hold reason instead of low-confidence-skip — that's intentional; update them.

- [ ] **Step 6: Commit Task 2**

```bash
git add src/WhiskeyRealism/Tactical/TacticalGroupSectorEstimator.cs \
        src/WhiskeyRealism/Tactical/TacticalDoctrineScorer.cs \
        tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: RC#2 SoW-aligned pre-contact movement doctrine

Translates SoW offai.cpp brigade-think (lines 507-546) pattern where a brigade marches to its tactical objective when no enemy is in range. Pre-fix, Whiskey was silent before line-of-sight contact because TacticalGroupSectorEstimator returned confidence 0.45 (below the 0.55 doctrine threshold) and TacticalDoctrineScorer.DecideGroupStance skipped on low-confidence.

Two changes:
- TacticalGroupSectorEstimator.BuildSector lifts the no-enemy/no-line-contact floor from 0.45 to 0.55; the bracket-with-enemy/no-line-contact moves from 0.55 to 0.65; the no-enemy/line-contact (rare) moves from 0.60 to 0.70. Hard line-contact remains 0.85.
- TacticalDoctrineScorer.DecideGroupStance adds an explicit pre-contact-hold branch: < 0.40 still skips as low-confidence, [0.40, 0.55) returns Apply(stance=2, reason=pre-contact-hold), >= 0.55 falls through to existing branches.

Net effect: brigades that previously sat in march column waiting for line-of-sight now receive a baseline defensive-hold stance write, which lets CommandPostureExecutor issue role-assigned waypoint movement toward the orchestrator's playbook target.

Tests: 4 new harness tests + existing low-confidence-skip case held; 1117 PASS / 0 FAIL."
```

---

## Task 3 — RC#3 Wiring: Live Commander Initiative + Posture

**Files:**
- Modify: `src/WhiskeyRealism/Patches/BattleCommanderIntentObserverPatch.cs:130-142`
- Test: `tests/WhiskeyRealism.Tests/Program.cs` (limited — `BuildIntentInput` reads vanilla state through Harmony, so unit tests cover only the pure intent-resolver behavior)

- [ ] **Step 1: Read the patch surface and find the call site**

```bash
grep -n 'BuildIntentInput\|commander\[.*commander\]\|GetCommanderInitiative' src/WhiskeyRealism/Patches/BattleCommanderIntentObserverPatch.cs | head -20
```

Identify which method calls `BuildIntentInput(macro)` and what scope it runs in (likely a Postfix on `AIBattle.CheckGlobalAIStrategy` or similar). The plan assumes the call site has access to the AIBattle / side / commander.

- [ ] **Step 2: Find the existing call site context and add side parameter to BuildIntentInput**

In `BattleCommanderIntentObserverPatch.cs`, find every `BuildIntentInput(...)` call and add a `side` (int) parameter. Then rewrite `BuildIntentInput` itself to:

```csharp
        private static TacticalIntentInput BuildIntentInput(int macro, int side, AIBattle battle)
        {
            // Live commander initiative read from GameVars.commander[id].GetCommanderInitiative()
            // when the battle has a resolvable commander for this side. Defaults to 0.5 only
            // when the lookup fails, never as the headline path. Same pattern for OperationPosture
            // (pulled from strategic CIC state) and oddsConfidence (live odds doctrine output).
            float commanderInitiative01 = ResolveCommanderInitiative(side, battle);
            float oddsConfidence = ResolveOddsConfidence(side, battle);
            OperationPosture posture = ResolveOperationPosture(side, battle);
            bool weakPointConfirmed = ResolveWeakPointConfirmed(side, battle);
            bool hasPlan = posture != OperationPosture.Inherit;

            return new TacticalIntentInput(
                operationPosture: posture,
                hasPlan: hasPlan,
                vanillaMacro: macro,
                commanderInitiative01: commanderInitiative01,
                oddsConfidence: oddsConfidence,
                weakPointConfirmed: weakPointConfirmed);
        }

        private static float ResolveCommanderInitiative(int side, AIBattle battle)
        {
            try
            {
                int commanderId = ResolveSideCommanderId(side, battle);
                if (commanderId < 0 || GameVars.commander == null || commanderId >= GameVars.commander.Length)
                    return 0.5f;
                var commander = GameVars.commander[commanderId];
                if (commander == null) return 0.5f;
                float init = commander.GetCommanderInitiative();
                if (float.IsNaN(init) || float.IsInfinity(init)) return 0.5f;
                if (init < 0f) return 0f;
                if (init > 1f) return 1f;
                return init;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("intent-commander-initiative",
                    "[BattleCommanderIntentObserverPatch] commander initiative read failed: " + ex.Message);
                return 0.5f;
            }
        }

        private static int ResolveSideCommanderId(int side, AIBattle battle)
        {
            try
            {
                if (battle == null) return -1;
                // Vanilla AIBattle exposes commander roster via bunits or via private fields.
                // Most resilient: walk battle.bunits.sideinformation[side].toptopgroup.commander.
                var bunitsField = AccessTools.Field(typeof(AIBattle), "bunits");
                var bunits = bunitsField?.GetValue(battle) as BattleUnits;
                if (bunits?.sideinformation == null || side < 0 || side >= bunits.sideinformation.Length)
                    return -1;
                var sideInfo = bunits.sideinformation[side];
                if (sideInfo == null) return -1;
                var topGroup = sideInfo.toptopgroup as Regiment;
                if (topGroup == null) return -1;
                return topGroup.commander;
            }
            catch
            {
                return -1;
            }
        }

        private static float ResolveOddsConfidence(int side, AIBattle battle)
        {
            try
            {
                var odds = TacticalReactionContext.Shared.GetOddsDecision(side);
                if (odds.HasValue) return Math.Max(0f, Math.Min(1f, odds.Value.Confidence));
                return 0.5f;
            }
            catch
            {
                return 0.5f;
            }
        }

        private static OperationPosture ResolveOperationPosture(int side, AIBattle battle)
        {
            try
            {
                // Strategic state lookup — if the active CIC has a plan with an explicit
                // operation posture for this battle's theater, use it. Otherwise Inherit.
                // We avoid coupling to CIC internals here; TacticalReactionContext is the
                // shared per-battle blackboard.
                var posture = TacticalReactionContext.Shared.GetOperationPosture(side);
                return posture ?? OperationPosture.Inherit;
            }
            catch
            {
                return OperationPosture.Inherit;
            }
        }

        private static bool ResolveWeakPointConfirmed(int side, AIBattle battle)
        {
            try
            {
                var sector = TacticalReactionContext.Shared.GetDecisiveSector(side);
                return sector.HasValue
                    && sector.Value.Source == TacticalSectorSource.VisibleLineContact
                    && sector.Value.Mission == TacticalSectorMission.AttackWeakPoint
                    && sector.Value.Confidence >= 0.75f;
            }
            catch
            {
                return false;
            }
        }
```

If `TacticalReactionContext.Shared.GetOperationPosture` / `GetOddsDecision` / `GetDecisiveSector` don't yet exist, add minimal getters; they're trivial wrappers over the existing `Set*` calls.

- [ ] **Step 3: Update every call site to pass `side` and `battle`**

```bash
grep -n 'BuildIntentInput(' src/WhiskeyRealism/Patches/BattleCommanderIntentObserverPatch.cs
```

Each call should become `BuildIntentInput(macro, side, __instance)` (or whatever the AIBattle parameter is called).

- [ ] **Step 4: Run build, fix any compile errors**

```bash
./build.sh 2>&1 | tail -15
```

Expected: 0 warnings / 0 errors. If `TacticalReactionContext.Shared.GetOperationPosture` doesn't exist, add it:

In `src/WhiskeyRealism/Tactical/TacticalReactionContext.cs` (find the existing `Set*` helpers):

```csharp
        private OperationPosture?[] _operationPosture = new OperationPosture?[2];

        public void SetOperationPosture(int side, OperationPosture? posture)
        {
            if (side < 0 || side >= _operationPosture.Length) return;
            _operationPosture[side] = posture;
        }

        public OperationPosture? GetOperationPosture(int side)
        {
            if (side < 0 || side >= _operationPosture.Length) return null;
            return _operationPosture[side];
        }
```

Mirror the existing `GetOddsDecision` / `GetDecisiveSector` accessors if missing.

- [ ] **Step 5: Run all tests**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj > /tmp/t3.log 2>&1
echo "PASS: $(grep -c '^PASS' /tmp/t3.log) FAIL: $(grep -c '^FAIL' /tmp/t3.log)"
grep '^FAIL' /tmp/t3.log | head -10
```

Expected: `PASS: 1117 FAIL: 0` (no new tests — wiring is verified by runtime smoke after deploy).

- [ ] **Step 6: Commit Task 3**

```bash
git add src/WhiskeyRealism/Patches/BattleCommanderIntentObserverPatch.cs \
        src/WhiskeyRealism/Tactical/TacticalReactionContext.cs
git commit -m "fix: RC#3 wire live commander initiative + posture into TacticalIntent

BattleCommanderIntentObserverPatch.BuildIntentInput previously hard-coded commanderInitiative01: 0.5f, oddsConfidence: 0.5f, operationPosture: Inherit, hasPlan: false. Every TacticalIntent telemetry row consequently showed commanderInit=0.50, confidence=0.50, intent=Attack, posture=Inherit regardless of the live commander.

Now reads:
- commanderInitiative01 from GameVars.commander[id].GetCommanderInitiative() via bunits.sideinformation[side].toptopgroup.commander, with NaN/range guards.
- oddsConfidence from TacticalReactionContext.Shared.GetOddsDecision(side).
- operationPosture from TacticalReactionContext.Shared.GetOperationPosture(side).
- weakPointConfirmed from TacticalReactionContext.Shared.GetDecisiveSector(side).
- hasPlan = posture != Inherit.

Missing TacticalReactionContext accessors added (GetOperationPosture).

Effect: TacticalIntent rows now vary with the actual commander assigned to each side. Aggressive commanders (Hood, Jackson, Grant) get higher initiative; cautious commanders (McClellan, Johnston) get lower. Combined with the RC#2 pre-contact path, brigades will now move forward at varying tempos based on commander personality."
```

---

## Task 4 — RC#3 Registry: Expanded Historical Coverage

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/HistoricalFigureRegistry.cs`
- Test: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Write the failing tests**

Append:

```csharp
TestCase("registry resolves Hunter (Union) without falling back to derived defaults", () =>
{
    var entry = HistoricalFigureRegistryTestAccess.LookupByName("Union", "David Hunter");
    Assert.True(entry.HasValue, "Hunter must be in the registry");
});

TestCase("registry resolves Beauregard regardless of preceding initials", () =>
{
    var fromFullName = HistoricalFigureRegistryTestAccess.LookupByName("CSA", "P.G.T. Beauregard");
    var fromLastName = HistoricalFigureRegistryTestAccess.LookupByName("CSA", "Beauregard");
    Assert.True(fromFullName.HasValue, "Beauregard with initials must match");
    Assert.True(fromLastName.HasValue, "Beauregard alone must match");
});

TestCase("registry covers all major army commanders both alliances", () =>
{
    string[] mustCover = new[]
    {
        "Lee", "Jackson", "Longstreet", "Stuart", "Hood", "Bragg", "Johnston",
        "Beauregard", "Forrest", "Polk", "Ewell", "Hill", "Early", "Hardee",
        "Cleburne", "Pickett", "Wheeler", "Morgan",
        "Grant", "Sherman", "Sheridan", "Meade", "Thomas", "Hooker", "Burnside",
        "McClellan", "Pope", "Halleck", "Hunter", "Sigel", "Banks", "Buell",
        "Rosecrans", "Schofield", "McPherson", "Reynolds", "Hancock", "Sedgwick",
    };
    foreach (var last in mustCover)
    {
        // Pick either alliance (Union vs CSA) by trial.
        var union = HistoricalFigureRegistryTestAccess.LookupByName("Union", last);
        var csa = HistoricalFigureRegistryTestAccess.LookupByName("CSA", last);
        Assert.True(union.HasValue || csa.HasValue, "registry missing: " + last);
    }
});
```

You will need a tiny `HistoricalFigureRegistryTestAccess` helper exposing the registry lookup. Add it to the registry file as `internal` so the test project (which already compiles `Strategic/HistoricalFigureRegistry.cs`) can reach it:

```csharp
    // Test access layer — used by the harness to verify registry coverage without
    // needing live vanilla commander objects.
    internal static class HistoricalFigureRegistryTestAccess
    {
        public static PersonalityVector? LookupByName(string allianceTag, string lastNameOrFull)
        {
            string normalized = HistoricalFigureRegistry.NormalizeLastNameForTest(lastNameOrFull);
            foreach (var entry in HistoricalFigureRegistry.EntriesForTest)
            {
                if (entry.AllianceTag == allianceTag && entry.CanonicalName == normalized)
                    return entry.V;
            }
            return null;
        }
    }
```

And in `HistoricalFigureRegistry`, expose minimal test surface:

```csharp
        internal static IReadOnlyList<Entry> EntriesForTest => _entries;
        internal static string NormalizeLastNameForTest(string combinedName) => NormalizeLastName(combinedName);
```

Make `Entry`, `AllianceTag`, `CanonicalName`, `V` `internal` on the struct accessors.

- [ ] **Step 2: Run tests to verify failures**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj 2>&1 | grep -E 'FAIL.*registry' | head -10
```

Expected: 3 FAILs (Hunter missing, Beauregard initials path untested, must-cover list misses several).

- [ ] **Step 3: Expand the registry**

Replace the `_entries` initializer in `HistoricalFigureRegistry.cs` with the full set (add the following to the existing 25 entries; they're additive — do NOT remove existing ones, just append before the closing brace):

```csharp
            // CSA additions
            new Entry { AllianceTag = "CSA",   CanonicalName = "polk",       V = new PersonalityVector(+0.3f, +0.2f, +0.0f, +0.2f, +0.3f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "ewell",      V = new PersonalityVector(+0.4f, +0.1f, +0.2f, +0.2f, +0.0f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "hill",       V = new PersonalityVector(+0.5f, -0.2f, +0.3f, +0.4f, -0.1f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "early",      V = new PersonalityVector(+0.5f, -0.1f, +0.4f, +0.3f, +0.0f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "hardee",     V = new PersonalityVector(+0.2f, +0.3f, +0.1f, +0.0f, +0.1f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "cleburne",   V = new PersonalityVector(+0.6f, -0.2f, +0.5f, +0.4f, -0.3f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "pickett",    V = new PersonalityVector(+0.5f, -0.2f, +0.4f, +0.5f, +0.0f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "wheeler",    V = new PersonalityVector(+0.5f, -0.3f, +0.6f, +0.3f, -0.2f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "morgan",     V = new PersonalityVector(+0.6f, -0.4f, +0.7f, +0.4f, -0.3f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "hampton",    V = new PersonalityVector(+0.4f, -0.1f, +0.4f, +0.2f, +0.0f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "fitzhughlee",V = new PersonalityVector(+0.4f, -0.2f, +0.4f, +0.2f, +0.0f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "anderson",   V = new PersonalityVector(+0.3f, +0.2f, +0.1f, +0.1f, +0.1f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "mahone",     V = new PersonalityVector(+0.4f, +0.0f, +0.3f, +0.2f, +0.0f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "stephens",   V = new PersonalityVector(+0.0f, +0.4f, -0.2f, -0.2f, +0.6f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "smith",      V = new PersonalityVector(+0.1f, +0.4f, -0.1f, -0.3f, +0.4f) },  // Kirby Smith
            new Entry { AllianceTag = "CSA",   CanonicalName = "vandorn",    V = new PersonalityVector(+0.5f, -0.4f, +0.5f, +0.4f, +0.2f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "magruder",   V = new PersonalityVector(+0.2f, +0.4f, +0.0f, +0.1f, +0.5f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "huger",      V = new PersonalityVector(-0.1f, +0.5f, -0.2f, -0.3f, +0.3f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "pemberton",  V = new PersonalityVector(-0.1f, +0.6f, -0.3f, -0.4f, +0.2f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "loring",     V = new PersonalityVector(+0.1f, +0.3f, +0.0f, -0.2f, +0.4f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "price",      V = new PersonalityVector(+0.3f, +0.0f, +0.2f, +0.0f, +0.3f) },
            new Entry { AllianceTag = "CSA",   CanonicalName = "taylor",     V = new PersonalityVector(+0.4f, -0.1f, +0.3f, +0.2f, +0.1f) },  // Richard Taylor
            // Union additions
            new Entry { AllianceTag = "Union", CanonicalName = "hunter",     V = new PersonalityVector(+0.3f, +0.0f, +0.1f, +0.4f, +0.4f) },
            new Entry { AllianceTag = "Union", CanonicalName = "sigel",      V = new PersonalityVector(-0.2f, +0.3f, +0.0f, -0.3f, +0.5f) },
            new Entry { AllianceTag = "Union", CanonicalName = "schofield",  V = new PersonalityVector(+0.3f, +0.2f, +0.1f, +0.1f, +0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "mcpherson",  V = new PersonalityVector(+0.5f, +0.0f, +0.4f, +0.3f, +0.0f) },
            new Entry { AllianceTag = "Union", CanonicalName = "reynolds",   V = new PersonalityVector(+0.4f, +0.1f, +0.3f, +0.3f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "hancock",    V = new PersonalityVector(+0.5f, -0.1f, +0.3f, +0.4f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "sedgwick",   V = new PersonalityVector(+0.3f, +0.2f, +0.1f, +0.2f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "couch",      V = new PersonalityVector(+0.1f, +0.3f, +0.0f, +0.1f, +0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "warren",     V = new PersonalityVector(+0.3f, +0.2f, +0.2f, +0.2f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "porter",     V = new PersonalityVector(+0.2f, +0.3f, +0.0f, +0.0f, +0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "humphreys",  V = new PersonalityVector(+0.3f, +0.1f, +0.2f, +0.2f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "logan",      V = new PersonalityVector(+0.5f, -0.2f, +0.3f, +0.4f, +0.3f) },
            new Entry { AllianceTag = "Union", CanonicalName = "howard",     V = new PersonalityVector(+0.2f, +0.2f, +0.1f, +0.1f, +0.4f) },
            new Entry { AllianceTag = "Union", CanonicalName = "slocum",     V = new PersonalityVector(+0.2f, +0.3f, +0.0f, +0.0f, +0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "buford",     V = new PersonalityVector(+0.5f, -0.2f, +0.5f, +0.3f, -0.2f) },
            new Entry { AllianceTag = "Union", CanonicalName = "kilpatrick", V = new PersonalityVector(+0.6f, -0.4f, +0.6f, +0.3f, +0.0f) },
            new Entry { AllianceTag = "Union", CanonicalName = "custer",     V = new PersonalityVector(+0.8f, -0.6f, +0.7f, +0.7f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "ord",        V = new PersonalityVector(+0.3f, +0.2f, +0.1f, +0.1f, +0.1f) },
            new Entry { AllianceTag = "Union", CanonicalName = "wallace",    V = new PersonalityVector(+0.3f, +0.1f, +0.3f, +0.2f, +0.2f) },  // Lew Wallace
            new Entry { AllianceTag = "Union", CanonicalName = "fremont",    V = new PersonalityVector(+0.0f, +0.4f, +0.0f, -0.2f, +0.7f) },
            new Entry { AllianceTag = "Union", CanonicalName = "butler",     V = new PersonalityVector(-0.1f, +0.4f, -0.1f, -0.3f, +0.8f) },
            new Entry { AllianceTag = "Union", CanonicalName = "stoneman",   V = new PersonalityVector(+0.2f, +0.2f, +0.3f, +0.1f, +0.1f) },
```

Also enhance the `NormalizeLastName` helper to handle the "P.G.T. Beauregard" case (already works because `LastIndexOf(' ')` returns the position of the last space, but only if the name has a space — verify "Beauregard" alone still works):

The existing `NormalizeLastName` already handles both forms. The test failure for "P.G.T. Beauregard" is because the test passes that string directly through `NormalizeLastNameForTest` — verify it lowercases and strips initials correctly. If the test still fails, normalize harder by stripping all `.` and tokens shorter than 3 chars before taking the last word.

Update `NormalizeLastName` to:

```csharp
        private static string NormalizeLastName(string combinedName)
        {
            if (string.IsNullOrWhiteSpace(combinedName)) return "";
            // Strip periods (handles "P.G.T. Beauregard" -> "PGT Beauregard"),
            // then take the last whitespace-delimited token.
            string compact = combinedName.Replace(".", "").Trim();
            string[] tokens = compact.Split(' ', '\t');
            for (int i = tokens.Length - 1; i >= 0; i--)
            {
                string t = tokens[i];
                if (t.Length >= 3) return t.ToLowerInvariant();
            }
            // Fallback: last non-empty token, even if short.
            for (int i = tokens.Length - 1; i >= 0; i--)
            {
                if (!string.IsNullOrEmpty(tokens[i])) return tokens[i].ToLowerInvariant();
            }
            return compact.ToLowerInvariant();
        }
```

- [ ] **Step 4: Run tests, verify all pass**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj > /tmp/t4.log 2>&1
echo "PASS: $(grep -c '^PASS' /tmp/t4.log) FAIL: $(grep -c '^FAIL' /tmp/t4.log)"
grep '^FAIL' /tmp/t4.log | head -10
```

Expected: `PASS: 1120 FAIL: 0` (3 new tests).

- [ ] **Step 5: Commit Task 4**

```bash
git add src/WhiskeyRealism/Strategic/HistoricalFigureRegistry.cs \
        tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: RC#3 expand historical figure registry to ~70 named Civil War generals

Telemetry showed TacticalCommanderUnknown for Hunter (Union, not registered) and Beauregard (CSA, registered but failed name match due to 'P.G.T. Beauregard' format). Registry now covers:

CSA additions: Polk, Ewell, A.P. Hill, Early, Hardee, Cleburne, Pickett, Wheeler, Morgan, Hampton, Fitzhugh Lee, Anderson, Mahone, Stephens, Kirby Smith, Van Dorn, Magruder, Huger, Pemberton, Loring, Price, Taylor.

Union additions: Hunter, Sigel, Schofield, McPherson, Reynolds, Hancock, Sedgwick, Couch, Warren, Porter, Humphreys, Logan, Howard, Slocum, Buford, Kilpatrick, Custer, Ord, Lew Wallace, Fremont, Butler, Stoneman.

NormalizeLastName now strips periods and skips tokens shorter than 3 chars before taking the last token, so 'P.G.T. Beauregard' normalizes to 'beauregard' instead of failing.

Tests: 3 new registry coverage tests + name matcher tests; 1120 PASS / 0 FAIL."
```

---

## Task 5 — Build, Deploy, Hash Verify (per AGENTS.md)

- [ ] **Step 1: Clean build**

```bash
./build.sh 2>&1 | tail -8
```

Expected: 0 warnings / 0 errors.

- [ ] **Step 2: Deploy DLL**

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
```

If this fails with `Invalid argument`, GTCW is running — ask the user to close it.

- [ ] **Step 3: Hash-verify deployment**

```bash
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: both files identical timestamp/size, matching SHA-256.

- [ ] **Step 4: Update live config to pick up new defaults**

Read the current live config and back it up, then update flag values for the 21 flipped flags. Tell the user explicitly which flags changed. Alternatively (and simpler): rename the live config so it gets regenerated from the new defaults on next launch:

```bash
mv "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/config/dev.kyle.whiskey-realism.cfg" \
   "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/config/dev.kyle.whiskey-realism.cfg.pre-sow-fixes.bak"
```

Note in the final report so user is aware.

---

## Task 6 — Doc Updates + Final Commit

- [ ] **Step 1: Update `docs/handoff.md`** with a new "What just shipped" entry covering RC#1, RC#2, RC#3 + new SHA256.

- [ ] **Step 2: Update `docs/tactical-orchestrator.md`** with the SoW pre-contact translation pattern note.

- [ ] **Step 3: Update `docs/scourge-of-war-ai-anchors.md`** to record that brigade-think pre-contact movement (offai.cpp:507-546) is now translated into Whiskey.

- [ ] **Step 4: Commit docs**

```bash
git add docs/handoff.md docs/tactical-orchestrator.md docs/scourge-of-war-ai-anchors.md
git commit -m "docs: record SoW-aligned tactical fixes (RC#1 reserve guard, RC#2 pre-contact, RC#3 personality wiring + registry)"
```

- [ ] **Step 5: Merge to main**

Return to main repo, merge worktree branch with `--no-ff` to preserve the feature-branch shape:

```bash
cd /home/onebodyamerica/Projects/whiskey-realism-mod
git merge --no-ff feat/tactical-sow-aligned-fixes -m "Merge tactical SoW-aligned fixes (RC#1/RC#2/RC#3) into main"
```

- [ ] **Step 6: Final report to user**

Report:
- Final SHA-256 of the deployed DLL
- Test count (expected 1120 PASS / 0 FAIL)
- Live config backup path (so they can restore if needed)
- That fresh in-game smoke is the next step (GTCW restart, start a battle, watch for: stable plans without ReserveExhaustion churn, brigades advancing pre-contact, TacticalIntent rows showing varied commanderInit values per side, no TacticalCommanderUnknown for Hunter/Beauregard)
