# Construction Steering Slice B Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Activate the next safe construction slice: use `ConstructionIntentLedger` to steer private-building probabilities and add conservative connected-chain telegraph AI behind config.

**Architecture:** Keep private-building steering inside the existing #20 `EconomyConstructionPatch` consumer-state surface; do not replace `bestiipplaces` in this plan. Add a separate telegraph runtime helper that starts at most one connected telegraph construction per faction from weekly construction intent, only when vanilla connection/unit-support gates are satisfied. Forts, supply depots, and railroad active steering are explicitly deferred to separate plans because they use different vanilla hooks and risk surfaces.

**Tech Stack:** BepInEx 5.4.x x64, HarmonyX, C# netstandard2.1, Unity 2021 Mono, console harness in `tests/WhiskeyRealism.Tests`.

---

## Scope

This plan implements the next executable slice from `docs/superpowers/specs/2026-05-04-construction-intent-ledger-design.md`:

- ledger-driven private-building probability steering over #20;
- conservative telegraph construction AI behind `EnableTelegraphAI`;
- non-spam logs and telemetry for both.

This plan does not implement:

- `bestiipplaces[type]` IIP substitution;
- supply-depot steering;
- fort site steering;
- railroad filtering/rollback;
- Transpilers.

## File Structure

- Create `src/WhiskeyRealism/Strategic/Construction/ConstructionSteeringScorer.cs`  
  Pure private-building multiplier logic. Combines fiscal multiplier with construction-ledger preference/suppression.

- Create `src/WhiskeyRealism/Strategic/Construction/TelegraphIntent.cs`  
  Pure telegraph intent contracts and score helpers that do not reference Unity or game types.

- Create `src/WhiskeyRealism/Strategic/Construction/TelegraphConstructionRuntime.cs`  
  Runtime telegraph candidate extraction and `CBuilding.AddConstructionWish(CBuilding.id_telegraphstation, ...)` call. Game/Unity references allowed. No persistent mod state.

- Modify `src/WhiskeyRealism/Patches/EconomyConstructionPatch.cs`  
  Use `ConstructionIntents[alliance]` when `EnableConstructionSiteSteering` is true. Preserve current fiscal-only behavior when false.

- Modify `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`  
  After construction intent computation, call telegraph runtime only when `EnableTelegraphAI` is true and the weekly review is active.

- Modify `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`  
  Link new pure construction files.

- Modify `tests/WhiskeyRealism.Tests/Program.cs`  
  Add tests for private steering and pure telegraph scoring.

- Modify `docs/patch-catalog.md`  
  Update #20 behavior and add telegraph AI coordinator/runtime note if a new patch file is not introduced.

- Modify `docs/handoff.md`  
  Record implementation/deploy status and next deferred surfaces.

---

### Task 1: Pure Private Construction Steering Scorer

**Files:**
- Create: `src/WhiskeyRealism/Strategic/Construction/ConstructionSteeringScorer.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add failing tests**

In `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, add:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Strategic\Construction\ConstructionSteeringScorer.cs" Link="ConstructionSteeringScorer.cs" />
```

In `tests/WhiskeyRealism.Tests/Program.cs`, add these test registrations after the existing construction ledger tests:

```csharp
("construction steering boosts ledger top private candidate", ConstructionSteeringBoostsTopPrivateCandidate),
("construction steering suppresses ledger-suppressed candidate", ConstructionSteeringSuppressesSuppressedCandidate),
("construction steering preserves fiscal multiplier when no intent", ConstructionSteeringPreservesFiscalWhenNoIntent),
```

Add these test methods before `AssertEqual<T>`:

```csharp
private static void ConstructionSteeringBoostsTopPrivateCandidate()
{
    var output = new ConstructionOutput
    {
        Posture = ConstructionPosture.FieldSupply,
        TopPrivateBuilding = new ConstructionCandidate
        {
            Kind = ConstructionCandidateKind.PrivateBuilding,
            BuildingTypeId = 13,
            Name = "Market",
            Score = 1.25f,
            VanillaValid = true
        }
    };

    var decision = ConstructionSteeringScorer.DecidePrivateMultiplier(
        output,
        buildingTypeId: 13,
        buildingName: "Market",
        fiscalMultiplier: 1.1f);

    AssertTrue(decision.Multiplier > 1.5f, "expected strong ledger boost for top private candidate");
    AssertEqual("ledger-top-private", decision.Reason);
}

private static void ConstructionSteeringSuppressesSuppressedCandidate()
{
    var output = new ConstructionOutput
    {
        Posture = ConstructionPosture.EmergencyHold,
        Suppressions = new[]
        {
            new ConstructionSuppression
            {
                Kind = ConstructionCandidateKind.PrivateBuilding,
                Name = "Factory",
                Reason = ConstructionSuppressionReason.EmergencyCreditFloor
            }
        }
    };

    var decision = ConstructionSteeringScorer.DecidePrivateMultiplier(
        output,
        buildingTypeId: 5,
        buildingName: "Factory",
        fiscalMultiplier: 1.2f);

    AssertEqual(0.1f, decision.Multiplier);
    AssertEqual("suppressed:EmergencyCreditFloor", decision.Reason);
}

private static void ConstructionSteeringPreservesFiscalWhenNoIntent()
{
    var decision = ConstructionSteeringScorer.DecidePrivateMultiplier(
        output: null,
        buildingTypeId: 13,
        buildingName: "Market",
        fiscalMultiplier: 1.35f);

    AssertEqual(1.35f, decision.Multiplier);
    AssertEqual("fiscal-only", decision.Reason);
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile fails because `ConstructionSteeringScorer` does not exist.

- [ ] **Step 3: Add pure scorer**

Create `src/WhiskeyRealism/Strategic/Construction/ConstructionSteeringScorer.cs`:

```csharp
using System;

namespace WhiskeyRealism.Strategic.Construction
{
    public struct ConstructionSteeringDecision
    {
        public float Multiplier;
        public string Reason;
    }

    public static class ConstructionSteeringScorer
    {
        public static ConstructionSteeringDecision DecidePrivateMultiplier(
            ConstructionOutput output,
            int buildingTypeId,
            string buildingName,
            float fiscalMultiplier)
        {
            if (output == null)
                return Decision(Clamp(fiscalMultiplier, 0.1f, 3f), "fiscal-only");

            var suppressed = SuppressionFor(output, buildingName);
            if (suppressed.HasValue)
                return Decision(0.1f, "suppressed:" + suppressed.Value);

            if (output.TopPrivateBuilding.Kind == ConstructionCandidateKind.PrivateBuilding &&
                output.TopPrivateBuilding.BuildingTypeId == buildingTypeId)
            {
                float scoreBoost = Clamp(1f + Math.Max(0f, output.TopPrivateBuilding.Score), 1.25f, 2.5f);
                return Decision(Clamp(fiscalMultiplier * scoreBoost, 0.1f, 3f), "ledger-top-private");
            }

            if (output.Posture == ConstructionPosture.EmergencyHold)
                return Decision(Clamp(fiscalMultiplier * 0.35f, 0.1f, 1f), "emergency-hold");

            if (output.Posture == ConstructionPosture.FieldSupply && IsLogisticsBuilding(buildingTypeId, buildingName))
                return Decision(Clamp(fiscalMultiplier * 1.25f, 0.1f, 2.5f), "field-supply-logistics");

            return Decision(Clamp(fiscalMultiplier, 0.1f, 3f), "fiscal-ledger-neutral");
        }

        private static ConstructionSuppressionReason? SuppressionFor(ConstructionOutput output, string buildingName)
        {
            if (output.Suppressions == null) return null;
            string wanted = Normalize(buildingName);
            for (int i = 0; i < output.Suppressions.Length; i++)
            {
                var suppression = output.Suppressions[i];
                if (suppression.Kind == ConstructionCandidateKind.PrivateBuilding &&
                    Normalize(suppression.Name) == wanted)
                    return suppression.Reason;
            }
            return null;
        }

        private static bool IsLogisticsBuilding(int buildingTypeId, string buildingName)
        {
            string normalized = Normalize(buildingName);
            return buildingTypeId == 13 ||
                normalized.Contains("market") ||
                normalized.Contains("hospital") ||
                normalized.Contains("bank");
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.Trim().ToLowerInvariant();
        }

        private static ConstructionSteeringDecision Decision(float multiplier, string reason)
        {
            return new ConstructionSteeringDecision
            {
                Multiplier = multiplier,
                Reason = reason
            };
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
```

- [ ] **Step 4: Run tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/WhiskeyRealism/Strategic/Construction/ConstructionSteeringScorer.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: score construction steering multipliers"
```

---

### Task 2: Wire Private Building Steering Into #20

**Files:**
- Modify: `src/WhiskeyRealism/Patches/EconomyConstructionPatch.cs`

- [ ] **Step 1: Replace fiscal-only multiplier calculation**

In `EconomyConstructionPatch.Prefix`, replace:

```csharp
float mult = FiscalConstructionScorer.Multiplier(intent, alliance, type.name, type.subsidytype);
```

with:

```csharp
float fiscalMult = FiscalConstructionScorer.Multiplier(intent, alliance, type.name, type.subsidytype);
var constructionOutput = GetConstructionIntent(alliance);
var steering = ConstructionSteeringScorer.DecidePrivateMultiplier(
    ConstructionSteeringEnabled() ? constructionOutput : null,
    buildingType,
    type.name,
    fiscalMult);
float mult = steering.Multiplier;
string steeringReason = steering.Reason;
```

- [ ] **Step 2: Add helper methods**

Add these methods before `CandidateEligibleForVanillaPath`:

```csharp
private static bool ConstructionSteeringEnabled()
{
    try
    {
        return Plugin.Instance != null &&
            Plugin.Instance.EnableConstructionSiteSteering != null &&
            Plugin.Instance.EnableConstructionSiteSteering.Value;
    }
    catch
    {
        return false;
    }
}

private static ConstructionOutput GetConstructionIntent(int alliance)
{
    try
    {
        var coordinator = StrategicCoordinator.Instance;
        if (coordinator == null || coordinator.ConstructionIntents == null)
            return null;
        return alliance >= 0 && alliance < coordinator.ConstructionIntents.Length
            ? coordinator.ConstructionIntents[alliance]
            : null;
    }
    catch (Exception ex)
    {
        OnceLog.Warning("economy-construction:construction-intent", "[Patch:EconomyConstruction] construction intent read failed: " + ex.Message);
        return null;
    }
}
```

Add this using at the top:

```csharp
using WhiskeyRealism.Strategic.Construction;
```

- [ ] **Step 3: Update verbose logging**

Inside the existing verbose log, append the steering reason:

```csharp
$"newProb={newProb:F3} posture={intent.Posture} constructionReason={steeringReason}");
```

- [ ] **Step 4: Build**

Run:

```bash
./build.sh
```

Expected: build succeeds with `0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add src/WhiskeyRealism/Patches/EconomyConstructionPatch.cs
git commit -m "feat: steer private construction from ledger"
```

---

### Task 3: Pure Telegraph Intent Contracts

**Files:**
- Create: `src/WhiskeyRealism/Strategic/Construction/TelegraphIntent.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add failing tests**

In `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, add:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Strategic\Construction\TelegraphIntent.cs" Link="TelegraphIntent.cs" />
```

In `Program.cs`, add these registrations after the construction steering tests:

```csharp
("telegraph intent rejects disconnected candidates", TelegraphIntentRejectsDisconnectedCandidates),
("telegraph intent favors active command corridor", TelegraphIntentFavorsActiveCommandCorridor),
("telegraph intent suppresses emergency noncritical build", TelegraphIntentSuppressesEmergencyNoncriticalBuild),
```

Add these methods before `AssertEqual<T>`:

```csharp
private static void TelegraphIntentRejectsDisconnectedCandidates()
{
    var candidate = new TelegraphCandidateFacts
    {
        ConnectedToCapitalOrChain = false,
        SupportingUnitEligible = true,
        SupportsActiveCommandCorridor = true,
        SafeRear = true
    };

    var decision = TelegraphIntentScorer.Score(candidate, ConstructionPosture.FieldSupply);

    AssertEqual(false, decision.ShouldBuild);
    AssertEqual("not-connected", decision.Reason);
}

private static void TelegraphIntentFavorsActiveCommandCorridor()
{
    var candidate = new TelegraphCandidateFacts
    {
        ConnectedToCapitalOrChain = true,
        SupportingUnitEligible = true,
        SupportsActiveCommandCorridor = true,
        SafeRear = true,
        CommandDelayPressure = 0.8f,
        FormationImportance = 0.7f
    };

    var decision = TelegraphIntentScorer.Score(candidate, ConstructionPosture.FieldSupply);

    AssertEqual(true, decision.ShouldBuild);
    AssertTrue(decision.Score > 1.0f, "expected active telegraph command corridor score above build threshold");
    AssertEqual("active-command-corridor", decision.Reason);
}

private static void TelegraphIntentSuppressesEmergencyNoncriticalBuild()
{
    var candidate = new TelegraphCandidateFacts
    {
        ConnectedToCapitalOrChain = true,
        SupportingUnitEligible = true,
        SupportsActiveCommandCorridor = false,
        SafeRear = true,
        CommandDelayPressure = 0.4f,
        FormationImportance = 0.2f
    };

    var decision = TelegraphIntentScorer.Score(candidate, ConstructionPosture.EmergencyHold);

    AssertEqual(false, decision.ShouldBuild);
    AssertEqual("emergency-noncritical", decision.Reason);
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile fails because telegraph intent types do not exist.

- [ ] **Step 3: Add pure telegraph intent file**

Create `src/WhiskeyRealism/Strategic/Construction/TelegraphIntent.cs`:

```csharp
namespace WhiskeyRealism.Strategic.Construction
{
    public struct TelegraphCandidateFacts
    {
        public bool ConnectedToCapitalOrChain;
        public bool SupportingUnitEligible;
        public bool SupportsActiveCommandCorridor;
        public bool SafeRear;
        public bool AlreadyCoveredByTelegraph;
        public float CommandDelayPressure;
        public float FormationImportance;
    }

    public struct TelegraphIntentDecision
    {
        public bool ShouldBuild;
        public float Score;
        public string Reason;
    }

    public static class TelegraphIntentScorer
    {
        public static TelegraphIntentDecision Score(TelegraphCandidateFacts candidate, ConstructionPosture posture)
        {
            if (!candidate.ConnectedToCapitalOrChain)
                return Decision(false, 0f, "not-connected");
            if (!candidate.SupportingUnitEligible)
                return Decision(false, 0f, "no-supporting-unit");
            if (!candidate.SafeRear)
                return Decision(false, 0f, "unsafe-corridor");
            if (candidate.AlreadyCoveredByTelegraph)
                return Decision(false, 0f, "already-covered");
            if (posture == ConstructionPosture.EmergencyHold && !candidate.SupportsActiveCommandCorridor)
                return Decision(false, 0f, "emergency-noncritical");

            float score = 0.25f;
            if (candidate.SupportsActiveCommandCorridor)
                score += 0.45f;
            score += Clamp01(candidate.CommandDelayPressure) * 0.45f;
            score += Clamp01(candidate.FormationImportance) * 0.35f;
            if (posture == ConstructionPosture.FieldSupply || posture == ConstructionPosture.DefensiveWorks)
                score += 0.2f;

            return score >= 1.0f
                ? Decision(true, score, "active-command-corridor")
                : Decision(false, score, "below-threshold");
        }

        private static TelegraphIntentDecision Decision(bool shouldBuild, float score, string reason)
        {
            return new TelegraphIntentDecision
            {
                ShouldBuild = shouldBuild,
                Score = score,
                Reason = reason
            };
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
```

- [ ] **Step 4: Run tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/WhiskeyRealism/Strategic/Construction/TelegraphIntent.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: score telegraph construction intent"
```

---

### Task 4: Runtime Telegraph Construction Helper

**Files:**
- Create: `src/WhiskeyRealism/Strategic/Construction/TelegraphConstructionRuntime.cs`

- [ ] **Step 1: Create runtime helper**

Create `src/WhiskeyRealism/Strategic/Construction/TelegraphConstructionRuntime.cs`:

```csharp
using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Construction
{
    public static class TelegraphConstructionRuntime
    {
        public static bool TryStartTelegraph(int alliance, ConstructionOutput construction)
        {
            try
            {
                if (construction == null) return false;
                if (construction.Posture == ConstructionPosture.EmergencyHold) return false;
                if (alliance < 0 || alliance >= 2) return false;
                if (CBuilding.companyfoundings >= GameVars.debug_maxcompanyfoundings) return false;
                if (CountActiveTelegraphs(alliance) >= MaxActiveTelegraphs()) return false;

                var unit = BestSupportingUnit(alliance);
                if (unit == null) return false;
                if (!UnitEligible(unit)) return false;
                if (UnitAlreadyCovered(unit)) return false;

                Vector3 site = SiteTowardUnit(alliance, unit);
                if (!ConnectedToCapitalOrChain(alliance, site)) return false;
                if (!SafeRear(site, alliance)) return false;
                if (EnemyNearby(unit)) return false;

                var facts = new TelegraphCandidateFacts
                {
                    ConnectedToCapitalOrChain = true,
                    SupportingUnitEligible = true,
                    SupportsActiveCommandCorridor = true,
                    SafeRear = true,
                    AlreadyCoveredByTelegraph = false,
                    CommandDelayPressure = 0.8f,
                    FormationImportance = 0.8f
                };
                var decision = TelegraphIntentScorer.Score(facts, construction.Posture);
                if (!decision.ShouldBuild) return false;

                var iip = unit.closestiipforsupply;
                if (iip == null) return false;
                var building = CBuilding.AddConstructionWish(
                    CBuilding.id_telegraphstation,
                    site,
                    iip,
                    alliance,
                    overridealreadyconstructing: false);

                if (building == null)
                {
                    OnceLog.Warning("telegraph-ai:null-start:" + alliance, "[TelegraphAI] AddConstructionWish returned null");
                    return false;
                }

                OnceLog.Info(
                    "telegraph-ai:start:" + alliance,
                    $"[TelegraphAI] alliance={alliance} action=start site={site.x:0},{site.z:0} unit={unit.name} reason={decision.Reason} score={decision.Score:F2}");
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("telegraph-ai:failed:" + alliance, "[TelegraphAI] start failed: " + ex.Message);
                return false;
            }
        }

        private static int MaxActiveTelegraphs()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.MaxActiveTelegraphConstructionsPerFaction != null
                    ? Math.Max(0, Plugin.Instance.MaxActiveTelegraphConstructionsPerFaction.Value)
                    : 1;
            }
            catch { return 1; }
        }

        private static int CountActiveTelegraphs(int alliance)
        {
            try
            {
                int count = 0;
                if (BattleUnits.telegraphstation == null) return 0;
                for (int i = 0; i < BattleUnits.telegraphstation.Count; i++)
                {
                    var station = BattleUnits.telegraphstation[i];
                    if (station != null &&
                        station.Owner == alliance &&
                        station.BuildingType == CBuilding.id_telegraphstation &&
                        station.constructiontimer > 0f)
                        count++;
                }
                return count;
            }
            catch { return 0; }
        }

        private static Regiment BestSupportingUnit(int alliance)
        {
            try
            {
                var units = BattleUnits.completeunitlist;
                if (units == null) return null;
                Regiment best = null;
                float bestStrength = 0f;
                for (int i = 0; i < units.Count; i++)
                {
                    var unit = units[i];
                    if (unit == null || unit.alliance != alliance) continue;
                    if (!UnitEligible(unit)) continue;
                    if (UnitAlreadyCovered(unit)) continue;
                    if (unit.groupstrengthdirect > bestStrength)
                    {
                        best = unit;
                        bestStrength = unit.groupstrengthdirect;
                    }
                }
                return best;
            }
            catch { return null; }
        }

        private static bool UnitEligible(Regiment unit)
        {
            if (unit == null) return false;
            if (unit.onretreat || unit.isrouted || unit.inbattle) return false;
            if (unit.garrisonreference != null) return false;
            if (unit.closestiipforsupply == null) return false;
            return unit.groupstrengthdirect > 1000f;
        }

        private static bool UnitAlreadyCovered(Regiment unit)
        {
            try { return AccessTools.Field(typeof(Regiment), "hastelegraphconnection")?.GetValue(unit) != null; }
            catch { return false; }
        }

        private static bool EnemyNearby(Regiment unit)
        {
            try { return unit.GetClosestEnemyUnitReg(unit.buglerange) != null; }
            catch { return true; }
        }

        private static Vector3 SiteTowardUnit(int alliance, Regiment unit)
        {
            Vector3 unitPos = ((Component)unit).transform.position;
            Vector3 anchor = ClosestConnectedAnchor(alliance, unitPos);
            float range = Math.Max(0.1f, GamePrefs.standardtelegraphrange * 0.85f);
            Vector3 delta = unitPos - anchor;
            if (delta.magnitude > range)
                delta = delta.normalized * range;
            return anchor + delta;
        }

        private static Vector3 ClosestConnectedAnchor(int alliance, Vector3 target)
        {
            Vector3 best = CapitalPosition(alliance);
            float bestDistance = XzDistance(best, target);
            try
            {
                if (BattleUnits.telegraphstation == null) return best;
                for (int i = 0; i < BattleUnits.telegraphstation.Count; i++)
                {
                    var station = BattleUnits.telegraphstation[i];
                    if (station == null || station.Owner != alliance || !station.isconnected) continue;
                    Vector3 position = ((Component)station).transform.position;
                    float distance = XzDistance(position, target);
                    if (distance < bestDistance)
                    {
                        best = position;
                        bestDistance = distance;
                    }
                }
            }
            catch { }
            return best;
        }

        private static bool ConnectedToCapitalOrChain(int alliance, Vector3 site)
        {
            if (XzDistance(CapitalPosition(alliance), site) < GamePrefs.standardtelegraphrange)
                return true;
            try
            {
                if (BattleUnits.telegraphstation == null) return false;
                for (int i = 0; i < BattleUnits.telegraphstation.Count; i++)
                {
                    var station = BattleUnits.telegraphstation[i];
                    if (station == null || station.Owner != alliance || !station.isconnected) continue;
                    if (XzDistance(((Component)station).transform.position, site) < GamePrefs.standardtelegraphrange)
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static Vector3 CapitalPosition(int alliance)
        {
            try { return ((Component)GameVars.alliance[alliance].capital).transform.position; }
            catch { return default(Vector3); }
        }

        private static bool SafeRear(Vector3 site, int alliance)
        {
            try
            {
                var battleUnits = GameObject.Find("GameController")?.GetComponent<BattleUnits>();
                if (battleUnits == null || battleUnits.frontline2 == null || battleUnits.frontline2.numberofupdates <= 0)
                    return true;
                return battleUnits.frontline2.GetSideOnPosition(site) == alliance;
            }
            catch { return true; }
        }

        private static float XzDistance(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x;
            float z = a.z - b.z;
            return Mathf.Sqrt(x * x + z * z);
        }
    }
}
```

- [ ] **Step 2: Build**

Run:

```bash
./build.sh
```

Expected: build succeeds. If any direct field access is inaccessible, replace only that access with `AccessTools.Field(...).GetValue(...)` and keep the same behavior.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Strategic/Construction/TelegraphConstructionRuntime.cs
git commit -m "feat: add telegraph construction runtime"
```

---

### Task 5: Wire Telegraph AI Behind Config

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`

- [ ] **Step 1: Call telegraph runtime after construction intent**

In `UpdateConstructionIntent`, after the `[ConstructionTelemetry]` heartbeat block and before the `catch`, add:

```csharp
if (ConfigValue(plugin.EnableTelegraphAI))
{
    TelegraphConstructionRuntime.TryStartTelegraph(alliance, output);
}
```

This keeps telegraph starts weekly because `UpdateConstructionIntent` only runs during strategic review cadence.

- [ ] **Step 2: Build**

Run:

```bash
./build.sh
```

Expected: build succeeds with `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Strategic/StrategicCoordinator.cs
git commit -m "feat: enable conservative telegraph ai"
```

---

### Task 6: Verification, Docs, Deploy

**Files:**
- Modify: `docs/patch-catalog.md`
- Modify: `docs/handoff.md`

- [ ] **Step 1: Update patch catalog**

Update #20 in `docs/patch-catalog.md` to say:

```markdown
| 20 | `EconomyConstructionPatch` | Prefix | `Patches/EconomyConstructionPatch.cs` | `AICampaign.UpdateCompanyFoundations` (15000) | AI private-building steering. Preserves vanilla-selected `bestiipplaces` and all vanilla construction/funding/rating gates. When construction site steering is disabled, applies fiscal-only multipliers; when enabled, combines fiscal multipliers with `ConstructionIntentLedger` top-private and suppression signals. Does not replace IIPs or call `CBuilding.AddConstructionWish` directly. |
```

If the catalog does not have a separate telegraph row, add a note row after #23:

```markdown
| 24 | `TelegraphConstructionRuntime` | Coordinator helper | `Strategic/Construction/TelegraphConstructionRuntime.cs` | Weekly `StrategicCoordinator.UpdateConstructionIntent` | Optional net-new AI telegraph construction behind `EnableTelegraphAI`. Starts at most one connected telegraph construction per faction when an eligible friendly unit can support progress. Preserves vanilla `CBuilding.AddConstructionWish` placement/search and observer telemetry; default config remains off. |
```

- [ ] **Step 2: Update handoff**

Add a dated status bullet:

```markdown
- **2026-05-04 — construction steering Slice B implemented.** Private construction steering can now combine fiscal multipliers with `ConstructionIntentLedger` when `Enable Construction Site Steering` is enabled, without replacing `bestiipplaces` or bypassing vanilla gates. Optional `Enable Telegraph AI` starts conservative connected-chain telegraph construction through `CBuilding.AddConstructionWish(CBuilding.id_telegraphstation, ...)`, capped by `Max Active Telegraph Constructions Per Faction` and requiring eligible friendly unit support. Forts, depots, and railroads remain deferred to separate patch-surface plans.
```

- [ ] **Step 3: Run final verification**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected:

- all console tests pass;
- `./build.sh` produces `dist/WhiskeyRealism.dll`;
- build has `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 4: Deploy and verify SHA-256**

Close GTCW if running, then run:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: both SHA-256 lines match.

- [ ] **Step 5: Runtime smoke log check**

Launch GTCW, load/start a W&L campaign, allow one weekly strategic review, then run:

```bash
rg -n "ConstructionIntent|ConstructionTelemetry|TelegraphAI|EconomyConstruction|ConstructionObserverPatch|TargetInvocationException|Exception|ERROR|WARN" "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Expected with default config:

- `[ConstructionIntent]` lines appear;
- no `[TelegraphAI] alliance=... action=start` line unless `Enable Telegraph AI = true`;
- no repeated warnings;
- no `TargetInvocationException`.

Expected with `Enable Telegraph AI = true`:

- either one `[TelegraphAI] alliance=<id> action=start ...` line when a connected eligible site exists, or no telegraph line when gates are not satisfied;
- `ConstructionObserverPatch wired (CBuilding.Place)` may fire when vanilla starts the telegraph wish;
- no isolated/disconnected telegraph starts.

- [ ] **Step 6: Commit final docs if deploy hash was added**

If `docs/handoff.md` is updated with the deploy SHA, commit:

```bash
git add docs/patch-catalog.md docs/handoff.md
git commit -m "docs: document construction steering slice b"
```

- [ ] **Step 7: Push and verify remote**

```bash
git push origin main
git ls-remote origin refs/heads/main
git status --short --branch
```

Expected:

- remote `refs/heads/main` points at the final commit;
- worktree is clean and synced.

---

## Self-Review Notes

Spec coverage:

- Private-building steering advances the low-risk #20 surface without IIP substitution.
- Telegraph AI is implemented as command infrastructure, not cosmetic flavor.
- Telegraph placement remains connected-chain and unit-support gated.
- Forts, depots, and railroads are intentionally deferred because the spec identifies separate patch risks.

Safety:

- No Transpilers.
- No `bestiipplaces` replacement.
- No direct private-building `AddConstructionWish` calls.
- Telegraph AI is default-off and capped.
- Build/deploy/hash verification is required before asking for runtime smoke.
