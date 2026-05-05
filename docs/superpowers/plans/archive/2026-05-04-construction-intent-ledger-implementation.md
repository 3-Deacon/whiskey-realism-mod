# Construction Intent Ledger Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first safe slice of smart-construction AI: a pure `ConstructionIntentLedger` plus runtime telemetry/observers, with no construction steering yet.

**Architecture:** Add pure construction scoring models under `Strategic/Construction`, compute weekly construction intent after fiscal and formation ledgers, and add bounded Harmony observers for actual construction starts. This plan intentionally leaves `bestiipplaces` substitution, depot steering, fort steering, railroad filtering, and telegraph AI disabled for later plans after one-month runtime observation.

**Tech Stack:** BepInEx 5.4.x x64, HarmonyX, C# netstandard2.1, Unity 2021 Mono, console tests in `tests/WhiskeyRealism.Tests`.

---

## Scope

This plan implements only slices 1-2 from `docs/superpowers/specs/archive/2026-05-04-construction-intent-ledger-design.md`:

- pure ledger and tests;
- runtime input extraction;
- non-spam construction intent logging;
- actual-start observer for private/military buildings and railroads;
- scenario-start fort-site telemetry.

This plan does not implement:

- `bestiipplaces[type]` replacement;
- scanner-level Transpiler;
- supply depot steering;
- fort site steering;
- active railroad filtering/rollback;
- telegraph AI.

## File Structure

- Create `src/WhiskeyRealism/Strategic/Construction/ConstructionModels.cs`  
  Owns pure enums and data contracts: posture, candidate kind, input, candidate, output, observer events, config-free options.

- Create `src/WhiskeyRealism/Strategic/Construction/ConstructionIntentLedger.cs`  
  Pure scoring and posture selection. No game/Unity references.

- Create `src/WhiskeyRealism/Strategic/Construction/ConstructionRuntime.cs`  
  Runtime adapter. Reads vanilla state safely and builds `ConstructionInput`. This file may reference Unity/game types.

- Create `src/WhiskeyRealism/Strategic/Construction/ConstructionTelemetry.cs`  
  Tracks actual construction starts and builds compact heartbeat lines.

- Create `src/WhiskeyRealism/Patches/ConstructionObserverPatch.cs`  
  Harmony observer only. Postfixes `CBuilding.Place(...)` and `BattleUnits.Railroad.StartConstruction(...)`; records starts but never changes vanilla behavior.

- Modify `src/WhiskeyRealism/Plugin.cs`  
  Adds construction config valves. Steering config defaults stay `false`.

- Modify `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`  
  Stores `ConstructionIntents`, computes weekly/monthly construction intent, and logs non-spam signatures.

- Modify `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`  
  Links new pure construction files.

- Modify `tests/WhiskeyRealism.Tests/Program.cs`  
  Adds console tests for posture/scoring/signature behavior.

- Modify `docs/patch-catalog.md`  
  Adds the observer patch entry after implementation.

- Modify `docs/handoff.md`  
  Marks the construction ledger/observer slice as implemented after verification.

---

### Task 1: Pure Construction Models And Ledger Tests

**Files:**
- Create: `src/WhiskeyRealism/Strategic/Construction/ConstructionModels.cs`
- Create: `src/WhiskeyRealism/Strategic/Construction/ConstructionIntentLedger.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add failing tests to the console harness**

In `tests/WhiskeyRealism.Tests/Program.cs`, add `using WhiskeyRealism.Strategic.Construction;` with the other usings:

```csharp
using WhiskeyRealism.Strategic.Construction;
```

Add these entries to the `tests` array immediately after the fiscal construction scorer tests:

```csharp
("construction ledger chooses field supply from low-supply pressure", ConstructionLedgerChoosesFieldSupply),
("construction ledger allows csa early arms stress", ConstructionLedgerAllowsCsaEarlyArmsStress),
("construction ledger suppresses csa rail by doctrine", ConstructionLedgerSuppressesCsaRailByDoctrine),
("construction ledger makes emergency hold strict near bond floor", ConstructionLedgerEmergencyHoldNearBondFloor),
("construction ledger signature changes on top candidate", ConstructionLedgerSignatureChangesOnTopCandidate),
```

Add these test methods before `AssertEqual<T>`:

```csharp
private static ConstructionInput BaseConstructionInput(int alliance)
{
    return new ConstructionInput
    {
        AllianceId = alliance,
        EraStage = EraStage.Amateur1861,
        CurrentChapter = 1,
        FiscalPosture = FiscalPosture.BalancedWar,
        FiscalDefendedGate = FiscalGate.Construction,
        CurrentRating = 3,
        BondFloorRating = 11,
        SupplyProtection = false,
        LogisticsExpansion = false,
        ForceCapWarning = false,
        TopSupplyTheater = "",
        LowSupplyFormationCount = 0,
        LowAmmoFormationCount = 0,
        SupplyPressure = 0f,
        AmmoPressure = 0f,
        TransportPressure = 0f,
        CapitalThreat = 0f,
        ActiveRailroadStarts = 0
    };
}

private static void ConstructionLedgerChoosesFieldSupply()
{
    var input = BaseConstructionInput(1);
    input.SupplyProtection = true;
    input.LogisticsExpansion = true;
    input.LowSupplyFormationCount = 3;
    input.TopSupplyTheater = "East";
    input.Candidates.Add(new ConstructionCandidate
    {
        Kind = ConstructionCandidateKind.PrivateBuilding,
        BuildingTypeId = 13,
        Name = "Market",
        Theater = Theater.East,
        TransportPressure = 0.75f,
        SupplyPressure = 0.7f,
        VanillaValid = true
    });

    var output = ConstructionIntentLedger.Compute(input, new ConstructionOptions());

    AssertEqual(ConstructionPosture.FieldSupply, output.Posture);
    AssertEqual(13, output.TopPrivateBuilding.BuildingTypeId);
    AssertTrue(output.Signature.Contains("FieldSupply"), "expected FieldSupply in signature");
}

private static void ConstructionLedgerAllowsCsaEarlyArmsStress()
{
    var input = BaseConstructionInput(1);
    input.EraStage = EraStage.Amateur1861;
    input.FiscalPosture = FiscalPosture.CreditDefense;
    input.CurrentRating = 6;
    input.BondFloorRating = 11;
    input.Candidates.Add(new ConstructionCandidate
    {
        Kind = ConstructionCandidateKind.PrivateBuilding,
        BuildingTypeId = 10,
        Name = "Iron Works",
        Theater = Theater.East,
        ArmsIndustry = true,
        SupportsActiveArmyCorridor = true,
        VanillaValid = true
    });
    input.Candidates.Add(new ConstructionCandidate
    {
        Kind = ConstructionCandidateKind.PrivateBuilding,
        BuildingTypeId = 12,
        Name = "Factories",
        Theater = Theater.Coast,
        ArmsIndustry = false,
        SupportsActiveArmyCorridor = false,
        VanillaValid = true
    });

    var output = ConstructionIntentLedger.Compute(input, new ConstructionOptions());

    AssertEqual(10, output.TopPrivateBuilding.BuildingTypeId);
    AssertTrue(output.TopPrivateBuilding.Score > 0.5f, "expected early CSA arms industry to remain viable");
}

private static void ConstructionLedgerSuppressesCsaRailByDoctrine()
{
    var input = BaseConstructionInput(1);
    input.ActiveRailroadStarts = 1;
    input.Candidates.Add(new ConstructionCandidate
    {
        Kind = ConstructionCandidateKind.Railroad,
        Name = "Low value rail",
        Theater = Theater.West,
        SupportsActiveArmyCorridor = false,
        VanillaValid = true
    });

    var output = ConstructionIntentLedger.Compute(input, new ConstructionOptions());

    AssertEqual(ConstructionCandidate.None.Name, output.TopRailroad.Name);
    AssertTrue(output.Suppressions.Length > 0, "expected rail suppression");
}

private static void ConstructionLedgerEmergencyHoldNearBondFloor()
{
    var input = BaseConstructionInput(1);
    input.FiscalPosture = FiscalPosture.EmergencySolvency;
    input.CurrentRating = 10;
    input.BondFloorRating = 11;
    input.Candidates.Add(new ConstructionCandidate
    {
        Kind = ConstructionCandidateKind.PrivateBuilding,
        BuildingTypeId = 10,
        Name = "Iron Works",
        ArmsIndustry = true,
        SupportsActiveArmyCorridor = true,
        VanillaValid = true
    });

    var output = ConstructionIntentLedger.Compute(input, new ConstructionOptions());

    AssertEqual(ConstructionPosture.EmergencyHold, output.Posture);
    AssertEqual(ConstructionCandidate.None.Name, output.TopPrivateBuilding.Name);
}

private static void ConstructionLedgerSignatureChangesOnTopCandidate()
{
    var input = BaseConstructionInput(0);
    input.Candidates.Add(new ConstructionCandidate
    {
        Kind = ConstructionCandidateKind.PrivateBuilding,
        BuildingTypeId = 13,
        Name = "Market",
        Theater = Theater.East,
        TransportPressure = 0.8f,
        VanillaValid = true
    });
    var first = ConstructionIntentLedger.Compute(input, new ConstructionOptions());

    input.Candidates.Clear();
    input.Candidates.Add(new ConstructionCandidate
    {
        Kind = ConstructionCandidateKind.PrivateBuilding,
        BuildingTypeId = 9,
        Name = "Hospital",
        Theater = Theater.East,
        WoundedPressure = 0.9f,
        VanillaValid = true
    });
    var second = ConstructionIntentLedger.Compute(input, new ConstructionOptions());

    AssertTrue(first.Signature != second.Signature, "expected signature to change when top candidate changes");
}
```

- [ ] **Step 2: Link missing files and run the tests to verify failure**

Modify `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` by adding these compile links inside the existing `<ItemGroup>`:

```xml
    <Compile Include="..\..\src\WhiskeyRealism\Strategic\Construction\ConstructionModels.cs" Link="ConstructionModels.cs" />
    <Compile Include="..\..\src\WhiskeyRealism\Strategic\Construction\ConstructionIntentLedger.cs" Link="ConstructionIntentLedger.cs" />
```

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: `FAIL`/compile errors because `WhiskeyRealism.Strategic.Construction` and its types do not exist.

- [ ] **Step 3: Create `ConstructionModels.cs`**

Create `src/WhiskeyRealism/Strategic/Construction/ConstructionModels.cs`:

```csharp
using System.Collections.Generic;
using WhiskeyRealism.Strategic.Fiscal;

namespace WhiskeyRealism.Strategic.Construction
{
    public enum ConstructionPosture
    {
        Infrastructure = 0,
        FieldSupply = 1,
        DefensiveWorks = 2,
        IndustrialExpansion = 3,
        EmergencyHold = 4
    }

    public enum ConstructionCandidateKind
    {
        None = 0,
        PrivateBuilding = 1,
        SupplyDepot = 2,
        Fort = 3,
        Telegraph = 4,
        Railroad = 5
    }

    public enum ConstructionSuppressionReason
    {
        None = 0,
        VanillaInvalid = 1,
        EmergencyCreditFloor = 2,
        CsaRailDoctrineCap = 3,
        DiscretionaryIndustryCreditDefense = 4,
        NoSupportingUnit = 5,
        UnsafeRear = 6
    }

    public sealed class ConstructionOptions
    {
        public float SupplyPressureThreshold = 0.35f;
        public float DefensiveThreatThreshold = 0.65f;
        public int CsaArmsStressLastYear = 1863;
        public int MinimumRatingBufferFromBondFloor = 1;
    }

    public sealed class ConstructionInput
    {
        public int AllianceId;
        public EraStage EraStage;
        public int CurrentChapter;
        public int CurrentYear = 1861;
        public FiscalPosture FiscalPosture = FiscalPosture.BalancedWar;
        public FiscalGate FiscalDefendedGate = FiscalGate.None;
        public int CurrentRating;
        public int BondFloorRating = 11;
        public bool SupplyProtection;
        public bool LogisticsExpansion;
        public bool ForceCapWarning;
        public string TopSupplyTheater = "";
        public int LowSupplyFormationCount;
        public int LowAmmoFormationCount;
        public float SupplyPressure;
        public float AmmoPressure;
        public float TransportPressure;
        public float CapitalThreat;
        public int ActiveRailroadStarts;
        public List<ConstructionCandidate> Candidates = new List<ConstructionCandidate>();
    }

    public struct ConstructionCandidate
    {
        public static readonly ConstructionCandidate None = new ConstructionCandidate
        {
            Kind = ConstructionCandidateKind.None,
            Name = "<none>",
            Reason = "none",
            VanillaValid = false,
            Score = 0f
        };

        public ConstructionCandidateKind Kind;
        public int BuildingTypeId;
        public string Name;
        public Theater Theater;
        public float SupplyPressure;
        public float AmmoPressure;
        public float TransportPressure;
        public float WoundedPressure;
        public float CapitalThreat;
        public bool VanillaValid;
        public bool SafeRear;
        public bool ArmsIndustry;
        public bool SupportsActiveArmyCorridor;
        public bool CriticalDefense;
        public float Score;
        public string Reason;
    }

    public struct ConstructionSuppression
    {
        public ConstructionCandidateKind Kind;
        public string Name;
        public ConstructionSuppressionReason Reason;
    }

    public sealed class ConstructionOutput
    {
        public ConstructionPosture Posture;
        public string TopConstructionTheater = "";
        public ConstructionCandidate TopPrivateBuilding = ConstructionCandidate.None;
        public ConstructionCandidate TopSupplyDepot = ConstructionCandidate.None;
        public ConstructionCandidate TopFort = ConstructionCandidate.None;
        public ConstructionCandidate TopTelegraph = ConstructionCandidate.None;
        public ConstructionCandidate TopRailroad = ConstructionCandidate.None;
        public ConstructionSuppression[] Suppressions = new ConstructionSuppression[0];
        public string Signature = "";
    }

    public struct ConstructionStartEvent
    {
        public int AllianceId;
        public ConstructionCandidateKind Kind;
        public int BuildingTypeId;
        public string Name;
        public Theater Theater;
        public string SiteKey;
        public int Year;
        public int Month;
        public int Day;
    }
}
```

- [ ] **Step 4: Create `ConstructionIntentLedger.cs`**

Create `src/WhiskeyRealism/Strategic/Construction/ConstructionIntentLedger.cs`:

```csharp
using System.Collections.Generic;
using WhiskeyRealism.Strategic.Fiscal;

namespace WhiskeyRealism.Strategic.Construction
{
    public static class ConstructionIntentLedger
    {
        public static ConstructionOutput Compute(ConstructionInput input, ConstructionOptions options)
        {
            input = input != null ? input : new ConstructionInput();
            options = options != null ? options : new ConstructionOptions();

            var output = new ConstructionOutput();
            output.Posture = ResolvePosture(input, options);

            var suppressions = new List<ConstructionSuppression>();
            for (int i = 0; i < input.Candidates.Count; i++)
            {
                var candidate = input.Candidates[i];
                var reason = SuppressionReason(input, options, candidate, output.Posture);
                if (reason != ConstructionSuppressionReason.None)
                {
                    suppressions.Add(new ConstructionSuppression
                    {
                        Kind = candidate.Kind,
                        Name = candidate.Name ?? "<unnamed>",
                        Reason = reason
                    });
                    continue;
                }

                candidate.Score = Score(input, candidate, output.Posture);
                candidate.Reason = Reason(candidate, output.Posture);
                AssignTop(output, candidate);
            }

            output.Suppressions = suppressions.ToArray();
            output.TopConstructionTheater = ResolveTopTheater(output);
            output.Signature = BuildSignature(input, output);
            return output;
        }

        private static ConstructionPosture ResolvePosture(ConstructionInput input, ConstructionOptions options)
        {
            if (input.FiscalPosture == FiscalPosture.EmergencySolvency ||
                input.CurrentRating >= input.BondFloorRating - options.MinimumRatingBufferFromBondFloor)
                return ConstructionPosture.EmergencyHold;

            if (input.CapitalThreat >= options.DefensiveThreatThreshold)
                return ConstructionPosture.DefensiveWorks;

            if (input.SupplyProtection ||
                input.LogisticsExpansion ||
                input.SupplyPressure >= options.SupplyPressureThreshold ||
                input.AmmoPressure >= options.SupplyPressureThreshold ||
                input.LowSupplyFormationCount > 0 ||
                input.LowAmmoFormationCount > 0)
                return ConstructionPosture.FieldSupply;

            if (input.FiscalPosture == FiscalPosture.Expansion)
                return ConstructionPosture.IndustrialExpansion;

            return ConstructionPosture.Infrastructure;
        }

        private static ConstructionSuppressionReason SuppressionReason(
            ConstructionInput input,
            ConstructionOptions options,
            ConstructionCandidate candidate,
            ConstructionPosture posture)
        {
            if (!candidate.VanillaValid)
                return ConstructionSuppressionReason.VanillaInvalid;

            if (posture == ConstructionPosture.EmergencyHold &&
                !(candidate.CriticalDefense || IsMinimumSupply(candidate) || IsAllowedEmergencyCsaArms(input, options, candidate)))
                return ConstructionSuppressionReason.EmergencyCreditFloor;

            if (input.AllianceId == 1 &&
                candidate.Kind == ConstructionCandidateKind.Railroad &&
                (input.ActiveRailroadStarts > 0 || !candidate.SupportsActiveArmyCorridor))
                return ConstructionSuppressionReason.CsaRailDoctrineCap;

            if (input.FiscalPosture >= FiscalPosture.CreditDefense &&
                candidate.Kind == ConstructionCandidateKind.PrivateBuilding &&
                !candidate.ArmsIndustry &&
                !IsMinimumSupply(candidate) &&
                !ContainsName(candidate, "bank") &&
                !ContainsName(candidate, "market"))
                return ConstructionSuppressionReason.DiscretionaryIndustryCreditDefense;

            return ConstructionSuppressionReason.None;
        }

        private static bool IsAllowedEmergencyCsaArms(ConstructionInput input, ConstructionOptions options, ConstructionCandidate candidate)
        {
            return input.AllianceId == 1 &&
                input.CurrentYear <= options.CsaArmsStressLastYear &&
                input.CurrentRating < input.BondFloorRating - options.MinimumRatingBufferFromBondFloor &&
                candidate.ArmsIndustry &&
                candidate.SupportsActiveArmyCorridor;
        }

        private static bool IsMinimumSupply(ConstructionCandidate candidate)
        {
            return candidate.Kind == ConstructionCandidateKind.SupplyDepot ||
                ContainsName(candidate, "market");
        }

        private static float Score(ConstructionInput input, ConstructionCandidate candidate, ConstructionPosture posture)
        {
            float score = 0.25f;
            score += candidate.SupplyPressure * 0.35f;
            score += candidate.AmmoPressure * 0.25f;
            score += candidate.TransportPressure * 0.30f;
            score += candidate.WoundedPressure * 0.20f;
            score += candidate.CapitalThreat * 0.30f;

            if (candidate.SupportsActiveArmyCorridor) score += 0.25f;
            if (candidate.CriticalDefense) score += 0.25f;
            if (candidate.SafeRear) score += 0.10f;

            if (candidate.ArmsIndustry && input.AllianceId == 1 && input.CurrentYear <= 1863)
                score += 0.30f;
            if (candidate.Kind == ConstructionCandidateKind.Railroad && input.AllianceId == 1)
                score -= 0.35f;
            if (posture == ConstructionPosture.FieldSupply && IsMinimumSupply(candidate))
                score += 0.35f;
            if (posture == ConstructionPosture.DefensiveWorks && candidate.CriticalDefense)
                score += 0.35f;

            return score < 0f ? 0f : score;
        }

        private static string Reason(ConstructionCandidate candidate, ConstructionPosture posture)
        {
            if (candidate.CriticalDefense) return "critical-defense";
            if (candidate.SupportsActiveArmyCorridor) return "active-army-corridor";
            if (candidate.ArmsIndustry) return "arms-survival";
            if (candidate.TransportPressure > 0.5f) return "transport-pressure";
            if (candidate.WoundedPressure > 0.5f) return "wounded-pressure";
            return posture.ToString();
        }

        private static void AssignTop(ConstructionOutput output, ConstructionCandidate candidate)
        {
            if (candidate.Kind == ConstructionCandidateKind.PrivateBuilding && candidate.Score > output.TopPrivateBuilding.Score)
                output.TopPrivateBuilding = candidate;
            if (candidate.Kind == ConstructionCandidateKind.SupplyDepot && candidate.Score > output.TopSupplyDepot.Score)
                output.TopSupplyDepot = candidate;
            if (candidate.Kind == ConstructionCandidateKind.Fort && candidate.Score > output.TopFort.Score)
                output.TopFort = candidate;
            if (candidate.Kind == ConstructionCandidateKind.Telegraph && candidate.Score > output.TopTelegraph.Score)
                output.TopTelegraph = candidate;
            if (candidate.Kind == ConstructionCandidateKind.Railroad && candidate.Score > output.TopRailroad.Score)
                output.TopRailroad = candidate;
        }

        private static string ResolveTopTheater(ConstructionOutput output)
        {
            var top = output.TopPrivateBuilding;
            if (output.TopSupplyDepot.Score > top.Score) top = output.TopSupplyDepot;
            if (output.TopFort.Score > top.Score) top = output.TopFort;
            if (output.TopTelegraph.Score > top.Score) top = output.TopTelegraph;
            if (output.TopRailroad.Score > top.Score) top = output.TopRailroad;
            return top.Kind == ConstructionCandidateKind.None ? "" : top.Theater.ToString();
        }

        private static string BuildSignature(ConstructionInput input, ConstructionOutput output)
        {
            return input.AllianceId + ":" +
                output.Posture + ":" +
                output.TopPrivateBuilding.Name + ":" +
                output.TopSupplyDepot.Name + ":" +
                output.TopFort.Name + ":" +
                output.TopTelegraph.Name + ":" +
                output.TopRailroad.Name;
        }

        private static bool ContainsName(ConstructionCandidate candidate, string needle)
        {
            return (candidate.Name ?? "").ToLowerInvariant().Contains(needle);
        }
    }
}
```

- [ ] **Step 5: Run tests and commit**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all existing tests plus the five new construction tests print `PASS`.

Commit:

```bash
git add src/WhiskeyRealism/Strategic/Construction/ConstructionModels.cs src/WhiskeyRealism/Strategic/Construction/ConstructionIntentLedger.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: add construction intent ledger"
```

---

### Task 2: Runtime Input Extraction

**Files:**
- Create: `src/WhiskeyRealism/Strategic/Construction/ConstructionRuntime.cs`
- Modify: `src/WhiskeyRealism/WhiskeyRealism.csproj` if the SDK does not automatically include the new file

- [ ] **Step 1: Create runtime adapter**

Create `src/WhiskeyRealism/Strategic/Construction/ConstructionRuntime.cs`:

```csharp
using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Construction
{
    public static class ConstructionRuntime
    {
        public static ConstructionInput BuildInput(
            int alliance,
            EraStage era,
            FiscalOutput fiscal,
            FrontSectorLedger front,
            FormationDirectiveLedger formation)
        {
            var input = new ConstructionInput
            {
                AllianceId = alliance,
                EraStage = era,
                CurrentChapter = SafePolicyChapter(),
                CurrentYear = SafeYear(),
                FiscalPosture = fiscal != null ? fiscal.Posture : FiscalPosture.BalancedWar,
                FiscalDefendedGate = fiscal != null ? fiscal.DefendedGate : FiscalGate.None,
                CurrentRating = SafeRating(alliance),
                BondFloorRating = SafeBondFloor(),
                SupplyProtection = fiscal != null && fiscal.SupplyProtection,
                LogisticsExpansion = fiscal != null && fiscal.LogisticsExpansion,
                ForceCapWarning = fiscal != null && fiscal.ForceCapWarning,
                TopSupplyTheater = fiscal != null ? fiscal.TheaterSupplyPriority ?? "" : "",
                ActiveRailroadStarts = CountActiveRailroads(alliance)
            };

            FillPressure(input, front, formation);
            AddPrivateBuildingCandidates(input);
            AddRailroadCandidates(input);
            AddFortSiteObservationCandidates(input);
            return input;
        }

        private static int SafePolicyChapter()
        {
            try { return Policy.CurrentChapter; }
            catch { return -1; }
        }

        private static int SafeYear()
        {
            try { return Mathf.FloorToInt(GameVars.GetCampaignFloatingDate()); }
            catch { return 1861; }
        }

        private static int SafeRating(int alliance)
        {
            try { return GameVars.alliance[alliance].currentrating; }
            catch { return 0; }
        }

        private static int SafeBondFloor()
        {
            try { return GamePrefs.ratingnotches != null ? GamePrefs.ratingnotches.Length - 1 : 11; }
            catch { return 11; }
        }

        private static void FillPressure(ConstructionInput input, FrontSectorLedger front, FormationDirectiveLedger formation)
        {
            if (formation != null && formation.Pressure != null)
            {
                input.LowSupplyFormationCount = formation.Pressure.LowSupplyCount;
                input.LowAmmoFormationCount = formation.Pressure.LowAmmoCount;
                if (!string.IsNullOrEmpty(formation.Pressure.TopSupplyAreaKey))
                    input.TopSupplyTheater = formation.Pressure.TopSupplyAreaKey;
            }

            if (front == null || front.Sectors == null) return;

            int count = 0;
            float supply = 0f;
            float threat = 0f;
            foreach (var sector in front.Sectors)
            {
                if (sector == null) continue;
                count++;
                supply += 1f - sector.AverageSupply;
                if (sector.IsCritical && sector.Posture == FrontPosture.Hold)
                    threat += sector.StrategicImportance;
            }

            if (count > 0)
            {
                input.SupplyPressure = supply / count;
                input.AmmoPressure = input.SupplyPressure * 0.5f;
                input.TransportPressure = input.SupplyPressure;
                input.CapitalThreat = threat / count;
            }
        }

        private static void AddPrivateBuildingCandidates(ConstructionInput input)
        {
            try
            {
                if (GameVars.alliance == null || GameVars.buildingtypes == null) return;
                if (input.AllianceId < 0 || input.AllianceId >= GameVars.alliance.Length) return;
                var state = GameVars.alliance[input.AllianceId];
                if (state == null || state.bestiipplaces == null || state.bestiipplacesprob == null) return;

                int count = Math.Min(state.bestiipplaces.Length, GameVars.buildingtypes.Count);
                for (int typeId = 0; typeId < count; typeId++)
                {
                    var place = state.bestiipplaces[typeId];
                    if (place == null || state.bestiipplacesprob[typeId] <= 0f) continue;
                    var type = GameVars.buildingtypes[typeId];
                    if (type == null) continue;

                    input.Candidates.Add(new ConstructionCandidate
                    {
                        Kind = ConstructionCandidateKind.PrivateBuilding,
                        BuildingTypeId = typeId,
                        Name = type.name,
                        Theater = TheaterFromPosition(((Component)place).transform.position),
                        TransportPressure = SafeFloat(place, "transportbottlenecks") / 10f,
                        SupplyPressure = input.SupplyPressure,
                        AmmoPressure = input.AmmoPressure,
                        WoundedPressure = NearbyWoundedPressure(place, input.AllianceId),
                        VanillaValid = place.allianceowner == input.AllianceId && type.aiplacement,
                        SafeRear = SafeRear(((Component)place).transform.position, input.AllianceId),
                        ArmsIndustry = IsArmsIndustry(typeId, type.name),
                        SupportsActiveArmyCorridor = SupportsActiveCorridor(((Component)place).transform.position, input)
                    });
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("construction-runtime:private-candidates", "[ConstructionRuntime] private candidate read failed: " + ex.Message);
            }
        }

        private static void AddRailroadCandidates(ConstructionInput input)
        {
            try
            {
                var railroads = AccessTools.Field(typeof(BattleUnits), "railroad")?.GetValue(null) as IList;
                if (railroads == null) return;
                for (int i = 0; i < railroads.Count; i++)
                {
                    var railroad = railroads[i];
                    if (railroad == null) continue;
                    var type = railroad.GetType();
                    float progress = Convert.ToSingle(AccessTools.Field(type, "constructionprogress")?.GetValue(railroad) ?? 0f);
                    if (progress > 0f) continue;

                    string name = AccessTools.Field(type, "scriptref")?.GetValue(railroad)?.ToString() ?? "railroad-" + i;
                    input.Candidates.Add(new ConstructionCandidate
                    {
                        Kind = ConstructionCandidateKind.Railroad,
                        Name = name,
                        Theater = Theater.West,
                        VanillaValid = true,
                        SupplyPressure = input.SupplyPressure,
                        TransportPressure = input.TransportPressure,
                        SupportsActiveArmyCorridor = input.LogisticsExpansion || input.SupplyProtection
                    });
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("construction-runtime:rail-candidates", "[ConstructionRuntime] railroad candidate read failed: " + ex.Message);
            }
        }

        private static void AddFortSiteObservationCandidates(ConstructionInput input)
        {
            try
            {
                var sites = AccessTools.Field(typeof(AICampaign), "fortconstructionsites")?.GetValue(null) as IList;
                if (sites == null) return;
                for (int i = 0; i < sites.Count; i++)
                {
                    if (!(sites[i] is Vector3 position)) continue;
                    input.Candidates.Add(new ConstructionCandidate
                    {
                        Kind = ConstructionCandidateKind.Fort,
                        Name = "fort-site-" + i,
                        Theater = TheaterFromPosition(position),
                        VanillaValid = true,
                        SafeRear = SafeRear(position, input.AllianceId),
                        CriticalDefense = input.CapitalThreat > 0.35f,
                        CapitalThreat = input.CapitalThreat
                    });
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("construction-runtime:fort-sites", "[ConstructionRuntime] fort site read failed: " + ex.Message);
            }
        }

        private static int CountActiveRailroads(int alliance)
        {
            try { return Mathf.FloorToInt(BattleUnits.Railroad.GetConstructedRailroads(alliance)); }
            catch { return 0; }
        }

        private static float SafeFloat(object target, string field)
        {
            try
            {
                var value = AccessTools.Field(target.GetType(), field)?.GetValue(target);
                return value == null ? 0f : Convert.ToSingle(value);
            }
            catch { return 0f; }
        }

        private static float NearbyWoundedPressure(IIP place, int alliance)
        {
            try
            {
                if (place == null || place.unitsinrange == null) return 0f;
                float wounded = 0f;
                for (int i = 0; i < place.unitsinrange.Count; i++)
                {
                    var unit = place.unitsinrange[i];
                    if (unit != null && unit.alliance == alliance)
                        wounded += unit.groupwounded;
                }
                return Mathf.Min(1f, wounded / 10000f);
            }
            catch { return 0f; }
        }

        private static bool SafeRear(Vector3 position, int alliance)
        {
            try
            {
                var bunits = GameObject.Find("GameController")?.GetComponent<BattleUnits>();
                if (bunits == null || bunits.frontline2 == null || bunits.frontline2.numberofupdates <= 0) return true;
                return bunits.frontline2.GetSideOnPosition(position) == alliance;
            }
            catch { return true; }
        }

        private static bool SupportsActiveCorridor(Vector3 position, ConstructionInput input)
        {
            return input.SupplyProtection || input.LogisticsExpansion || input.CapitalThreat > 0.35f;
        }

        private static bool IsArmsIndustry(int buildingTypeId, string name)
        {
            string lower = (name ?? "").ToLowerInvariant();
            return buildingTypeId == 8 || buildingTypeId == 10 || buildingTypeId == 12 ||
                lower.Contains("foundr") || lower.Contains("iron") || lower.Contains("factor");
        }

        private static Theater TheaterFromPosition(Vector3 position)
        {
            if (position.x < -200f) return Theater.TransMiss;
            if (position.x > 800f && position.z < -100f) return Theater.Coast;
            if (position.x > 600f) return Theater.East;
            return Theater.West;
        }
    }
}
```

- [ ] **Step 2: Build to verify runtime references**

Run:

```bash
./build.sh
```

Expected: build succeeds with `0 Error(s)`. If `BattleUnits.Railroad.GetConstructedRailroads` is inaccessible because the nested type cannot be referenced from this scope, replace `CountActiveRailroads` with reflection over `BattleUnits.railroad` and count `constructionprogress > 0 && constructionprogress < 1 && alliancetoconstruct == alliance`.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Strategic/Construction/ConstructionRuntime.cs
git commit -m "feat: read construction intent runtime inputs"
```

---

### Task 3: Telemetry And Actual-Start Observer

**Files:**
- Create: `src/WhiskeyRealism/Strategic/Construction/ConstructionTelemetry.cs`
- Create: `src/WhiskeyRealism/Patches/ConstructionObserverPatch.cs`
- Modify: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`

- [ ] **Step 1: Create `ConstructionTelemetry.cs`**

Create `src/WhiskeyRealism/Strategic/Construction/ConstructionTelemetry.cs`:

```csharp
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Construction
{
    public sealed class ConstructionTelemetry
    {
        private readonly List<ConstructionStartEvent> _recentStarts = new List<ConstructionStartEvent>();

        public void Record(ConstructionStartEvent start)
        {
            _recentStarts.Add(start);
            while (_recentStarts.Count > 64)
                _recentStarts.RemoveAt(0);
        }

        public string Summary(int alliance)
        {
            int buildings = 0;
            int rail = 0;
            string last = "<none>";
            for (int i = 0; i < _recentStarts.Count; i++)
            {
                if (_recentStarts[i].AllianceId != alliance) continue;
                if (_recentStarts[i].Kind == ConstructionCandidateKind.Railroad) rail++;
                else buildings++;
                last = _recentStarts[i].Kind + ":" + (_recentStarts[i].Name ?? "<unnamed>");
            }
            return "starts_building=" + buildings + " starts_rail=" + rail + " last=" + last;
        }
    }
}
```

- [ ] **Step 2: Create observer patch**

Create `src/WhiskeyRealism/Patches/ConstructionObserverPatch.cs`:

```csharp
using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    [HarmonyPatch]
    internal static class ConstructionObserverPatch
    {
        [HarmonyPatch(typeof(CBuilding), "Place")]
        [HarmonyPostfix]
        internal static void CBuildingPlacePostfix(
            CBuilding __result,
            int type,
            int owner,
            bool newlycreated,
            bool pay)
        {
            if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
            if (!newlycreated || !pay || __result == null) return;
            if (owner < 0 || owner >= 2) return;

            try
            {
                OnceLog.Info("construction-observer:building", "ConstructionObserverPatch wired (CBuilding.Place)");
                var evt = new ConstructionStartEvent
                {
                    AllianceId = owner,
                    Kind = KindForBuilding(type),
                    BuildingTypeId = type,
                    Name = SafeBuildingName(type),
                    Theater = TheaterFromPosition(((Component)__result).transform.position),
                    SiteKey = ((Component)__result).transform.position.x.ToString("0") + "," + ((Component)__result).transform.position.z.ToString("0"),
                    Year = SafeYear(),
                    Month = SafeMonth(),
                    Day = SafeDay()
                };
                StrategicCoordinator.Instance?.RecordConstructionStart(evt);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("construction-observer:building-failed", "[ConstructionObserver] building observer failed: " + ex.Message);
            }
        }

        [HarmonyPatch(typeof(BattleUnits.Railroad), "StartConstruction")]
        [HarmonyPostfix]
        internal static void RailroadStartPostfix(
            BattleUnits.Railroad __instance,
            int _alliancetoconstruct,
            bool checkonly,
            bool __result)
        {
            if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
            if (checkonly || !__result) return;
            if (_alliancetoconstruct < 0 || _alliancetoconstruct >= 2) return;

            try
            {
                OnceLog.Info("construction-observer:railroad", "ConstructionObserverPatch wired (Railroad.StartConstruction)");
                var evt = new ConstructionStartEvent
                {
                    AllianceId = _alliancetoconstruct,
                    Kind = ConstructionCandidateKind.Railroad,
                    BuildingTypeId = -1,
                    Name = __instance != null && __instance.scriptref != null ? __instance.scriptref.RailroadName : "<railroad>",
                    Theater = Theater.West,
                    SiteKey = __instance != null && __instance.scriptref != null ? __instance.scriptref.RailroadName : "<railroad>",
                    Year = SafeYear(),
                    Month = SafeMonth(),
                    Day = SafeDay()
                };
                StrategicCoordinator.Instance?.RecordConstructionStart(evt);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("construction-observer:railroad-failed", "[ConstructionObserver] railroad observer failed: " + ex.Message);
            }
        }

        private static ConstructionCandidateKind KindForBuilding(int type)
        {
            if (type == CBuilding.id_supplydepot) return ConstructionCandidateKind.SupplyDepot;
            if (type == CBuilding.id_fort) return ConstructionCandidateKind.Fort;
            if (type == CBuilding.id_telegraphstation) return ConstructionCandidateKind.Telegraph;
            return ConstructionCandidateKind.PrivateBuilding;
        }

        private static string SafeBuildingName(int type)
        {
            try
            {
                return GameVars.buildingtypes != null && type >= 0 && type < GameVars.buildingtypes.Count
                    ? GameVars.buildingtypes[type].name
                    : "building-" + type;
            }
            catch { return "building-" + type; }
        }

        private static int SafeYear()
        {
            try { return Mathf.FloorToInt(GameVars.GetCampaignFloatingDate()); }
            catch { return 0; }
        }

        private static int SafeMonth()
        {
            try { return GameVars.GetCampaignFloatingDateSep().month; }
            catch { return 0; }
        }

        private static int SafeDay()
        {
            try { return GameVars.GetCampaignFloatingDateSep().day; }
            catch { return 0; }
        }

        private static Theater TheaterFromPosition(Vector3 position)
        {
            if (position.x < -200f) return Theater.TransMiss;
            if (position.x > 800f && position.z < -100f) return Theater.Coast;
            if (position.x > 600f) return Theater.East;
            return Theater.West;
        }
    }
}
```

- [ ] **Step 3: Add coordinator recording method**

In `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`, add the field next to the other public ledger arrays:

```csharp
public ConstructionOutput[] ConstructionIntents = new ConstructionOutput[2];
public ConstructionTelemetry ConstructionTelemetry = new ConstructionTelemetry();
```

Add the using:

```csharp
using WhiskeyRealism.Strategic.Construction;
```

Add this method near `RecordBattleOutcome`:

```csharp
internal void RecordConstructionStart(ConstructionStartEvent start)
{
    ConstructionTelemetry.Record(start);
    if (Plugin.Instance != null && Plugin.Instance.ConstructionVerboseLogging.Value)
    {
        Plugin.Log.LogInfo(
            $"[ConstructionStart] alliance={start.AllianceId} kind={start.Kind} " +
            $"name={start.Name ?? "<unnamed>"} theater={start.Theater} site={start.SiteKey ?? "<none>"}");
    }
}
```

- [ ] **Step 4: Build and commit**

Run:

```bash
./build.sh
```

Expected: build succeeds with `0 Error(s)`.

Commit:

```bash
git add src/WhiskeyRealism/Strategic/Construction/ConstructionTelemetry.cs src/WhiskeyRealism/Patches/ConstructionObserverPatch.cs src/WhiskeyRealism/Strategic/StrategicCoordinator.cs
git commit -m "feat: observe construction starts"
```

---

### Task 4: Coordinator And Config Wiring

**Files:**
- Modify: `src/WhiskeyRealism/Plugin.cs`
- Modify: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`

- [ ] **Step 1: Add config entries**

In `src/WhiskeyRealism/Plugin.cs`, add fields after `FiscalTelemetryCsv`:

```csharp
internal ConfigEntry<bool> EnableConstructionIntentLedger;
internal ConfigEntry<bool> EnableConstructionSiteSteering;
internal ConfigEntry<bool> EnableSupplyDepotSteering;
internal ConfigEntry<bool> EnableFortSteering;
internal ConfigEntry<bool> EnableTelegraphAI;
internal ConfigEntry<bool> EnableRailroadSteering;
internal ConfigEntry<bool> ConstructionTelemetryEnabled;
internal ConfigEntry<bool> ConstructionVerboseLogging;
internal ConfigEntry<int> MaxActiveTelegraphConstructionsPerFaction;
internal ConfigEntry<int> MaxRailroadStartsPerFactionPerMonth;
```

Bind them after fiscal config binds:

```csharp
EnableConstructionIntentLedger = Config.Bind(
    "Construction", "Enable Construction Intent Ledger", true,
    "Compute weekly construction intent for telemetry and later steering. Does not directly change vanilla construction by itself.");
EnableConstructionSiteSteering = Config.Bind(
    "Construction", "Enable Construction Site Steering", false,
    "Default OFF. Future valve for bestiipplaces site substitution after observation validates it.");
EnableSupplyDepotSteering = Config.Bind(
    "Construction", "Enable Supply Depot Steering", false,
    "Default OFF. Future valve for supply depot steering after observer telemetry proves safe candidate selection.");
EnableFortSteering = Config.Bind(
    "Construction", "Enable Fort Steering", false,
    "Default OFF. Future valve for fort site steering after fort-site and unit-range telemetry prove realizable sites.");
EnableTelegraphAI = Config.Bind(
    "Construction", "Enable Telegraph AI", false,
    "Default OFF. Future valve for conservative connected-chain telegraph construction.");
EnableRailroadSteering = Config.Bind(
    "Construction", "Enable Railroad Steering", false,
    "Default OFF. Future valve for per-line railroad steering. Observation remains active through telemetry.");
ConstructionTelemetryEnabled = Config.Bind(
    "Construction", "Construction Telemetry", true,
    "Emit no-spam construction intent and actual-start heartbeat lines.");
ConstructionVerboseLogging = Config.Bind(
    "Construction", "Construction Verbose Logging", false,
    "Emit verbose construction candidate and actual-start details.");
MaxActiveTelegraphConstructionsPerFaction = Config.Bind(
    "Construction", "Max Active Telegraph Constructions Per Faction", 1,
    "Future telegraph AI cap. Current slice records the value but does not build telegraphs.");
MaxRailroadStartsPerFactionPerMonth = Config.Bind(
    "Construction", "Max Railroad Starts Per Faction Per Month", 1,
    "Future railroad steering cap. Current slice observes vanilla railroad starts only.");
```

- [ ] **Step 2: Compute construction intent in coordinator**

In `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`, add private signatures next to `_fiscalSignatures`:

```csharp
private readonly string[] _constructionSignatures = new string[2];
```

In `RunStrategicReview`, immediately after `UpdateFiscalIntent(...)`, add:

```csharp
UpdateConstructionIntent(alliance, era.Stage, logHeartbeat);
```

Add this method near `UpdateFiscalIntent`:

```csharp
private void UpdateConstructionIntent(int alliance, EraStage era, bool logHeartbeat)
{
    if (Plugin.Instance == null || !Plugin.Instance.EnableConstructionIntentLedger.Value)
        return;

    var fiscal = alliance >= 0 && alliance < FiscalIntents.Length ? FiscalIntents[alliance] : null;
    var front = alliance >= 0 && alliance < Fronts.Length ? Fronts[alliance] : null;
    var formation = alliance >= 0 && alliance < FormationDirectives.Length ? FormationDirectives[alliance] : null;
    var input = ConstructionRuntime.BuildInput(alliance, era, fiscal, front, formation);
    var output = ConstructionIntentLedger.Compute(input, new ConstructionOptions());
    ConstructionIntents[alliance] = output;

    if (Plugin.Instance.VerboseLogging.Value ||
        Plugin.Instance.ConstructionVerboseLogging.Value ||
        _constructionSignatures[alliance] != output.Signature)
    {
        Plugin.Log.LogInfo(
            $"[ConstructionIntent] alliance={alliance} posture={output.Posture} " +
            $"theater={output.TopConstructionTheater ?? ""} " +
            $"private={output.TopPrivateBuilding.Name} depot={output.TopSupplyDepot.Name} " +
            $"fort={output.TopFort.Name} rail={output.TopRailroad.Name}");
        _constructionSignatures[alliance] = output.Signature;
    }

    if (logHeartbeat && Plugin.Instance.ConstructionTelemetryEnabled.Value)
    {
        Plugin.Log.LogInfo(
            $"[ConstructionTelemetry] alliance={alliance} posture={output.Posture} " +
            $"{ConstructionTelemetry.Summary(alliance)}");
    }
}
```

- [ ] **Step 3: Run tests and build**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected:

- console tests print `PASS ...` for all tests;
- build succeeds with `0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add src/WhiskeyRealism/Plugin.cs src/WhiskeyRealism/Strategic/StrategicCoordinator.cs
git commit -m "feat: log construction intent telemetry"
```

---

### Task 5: Docs, Catalog, Verification, Deploy

**Files:**
- Modify: `docs/patch-catalog.md`
- Modify: `docs/handoff.md`

- [ ] **Step 1: Update patch catalog**

Add a new row to `docs/patch-catalog.md` after #22:

```markdown
| 23 | `ConstructionObserverPatch` | Postfix | `Patches/ConstructionObserverPatch.cs` | `CBuilding.Place` (96163), `BattleUnits.Railroad.StartConstruction` (77818) | Construction telemetry observer. Records actual building and railroad starts for comparison against `ConstructionIntentLedger`; does not alter construction, placement, funding, unit eligibility, or railroad selection. |
```

- [ ] **Step 2: Update handoff**

In `docs/handoff.md`, add a dated status bullet under recent checkpoints:

```markdown
- **2026-05-04 — construction intent ledger observer slice implemented.** Adds pure `ConstructionIntentLedger`, runtime candidate extraction, config-gated non-spam `[ConstructionIntent]` / `[ConstructionTelemetry]` logs, and #23 `ConstructionObserverPatch` for actual building/railroad starts. This slice intentionally does not steer `bestiipplaces`, supply depots, forts, railroads, or telegraphs; observer data is required before enabling those later patches.
```

Update the backlog item for construction to say the next action is one-month observation and then the private building type/site decision.

- [ ] **Step 3: Run final verification**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected:

- all console tests pass;
- `./build.sh` produces `dist/WhiskeyRealism.dll` with `0 Error(s)`.

- [ ] **Step 4: Deploy and verify SHA-256**

Close GTCW if running, then run:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected:

- `stat` shows the deployed DLL timestamp updated;
- both `sha256sum` lines have the same hash.

- [ ] **Step 5: Runtime smoke log checks**

Launch GTCW, start or load a campaign, let one strategic review run, then check:

```bash
rg -n "ConstructionIntent|ConstructionTelemetry|ConstructionObserverPatch|construction-observer|Exception|TargetInvocationException" "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Expected:

- at least one `[ConstructionIntent]` line for an AI alliance;
- one `[ConstructionTelemetry]` heartbeat line after monthly heartbeat;
- `ConstructionObserverPatch wired` appears only when vanilla starts a building or railroad;
- no repeated warnings;
- no `TargetInvocationException`.

- [ ] **Step 6: Commit docs and final implementation**

```bash
git add docs/patch-catalog.md docs/handoff.md src/WhiskeyRealism tests/WhiskeyRealism.Tests
git commit -m "feat: observe ai construction intent"
```

- [ ] **Step 7: Push and verify remote**

```bash
git push origin main
git ls-remote origin refs/heads/main
git status --short --branch
```

Expected:

- `origin/main` points to the final commit hash;
- worktree is clean and synced.

---

## Self-Review Notes

Spec coverage:

- Pure ledger and posture decisions: Task 1.
- Weekly recompute, no persistence: Task 4; no sidecar changes.
- Actual-start telemetry: Task 3.
- No-spam logging: Tasks 3-4 use signatures and verbose gates.
- Vanilla-safe first slice: no steering patches are implemented.
- CSA arms stress and CSA rail suppression: Task 1 tests and ledger scoring.
- Fort site observation before steering: Task 2 extracts fort-site candidates; Task 4 logs intent.
- Measurable proxies: Task 3 telemetry starts; Task 5 runtime smoke checks.

Known intentional gaps for later plans:

- `bestiipplaces[type]` substitution validity contract implementation.
- Supply depot unit/site steering.
- Fort site re-ranking.
- Active railroad per-line steering or rollback.
- Telegraph AI.

These gaps are intentional because the review-corrected spec requires observer data before enabling them.
