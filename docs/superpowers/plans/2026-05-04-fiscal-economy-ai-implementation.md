# Fiscal Economy AI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a fiscal economy AI that preserves vanilla economy mechanics while making Union and CSA budget, credit, policy, construction, project, recruitment, and military supply decisions coherent.

**Architecture:** Add a pure `FiscalIntentLedger` computed during weekly strategic review, then let bounded Harmony patches read that intent during vanilla finance, policy, project, and construction cycles. The CSA does not receive free parity; it competes by avoiding waste, protecting credit gates, sustaining supply for existing armies, and prioritizing asymmetric imports, diplomacy, logistics, and field-army usefulness.

**Tech Stack:** C# `netstandard2.1`, BepInEx 5.4.x, HarmonyX, Unity 2021 Mono, pure console tests in `tests/WhiskeyRealism.Tests`.

---

## Source Inputs

- Spec: `docs/superpowers/specs/2026-05-04-fiscal-economy-ai-design.md`
- Formation supply companion: `docs/superpowers/specs/2026-05-04-formation-directive-design.md`
- Decompile: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`
- Runtime patch catalog: `docs/patch-catalog.md`
- Master handoff: `docs/handoff.md`

## Verified Vanilla Anchors

- `AICampaign.UpdateFinancialAI(int alliance)` starts around decompile line 15352.
- Bond issue stops at worst rating notch: `treasury < 0f && currentrating < ratingnotches.Length - 1`.
- Vanilla tax raise/cut loops use `tax.Length - 1`, so land sales are skipped.
- Vanilla subsidy raises clamp to `GetAIPersonality(alliance).subsidyfocus[lane]`.
- `Economy.UpdateMacroEconomy(float)` divides non-player AI rating pressure by `sqrt(usedcampaignagressiveness)`.
- `Alliance.GetAIPersonality(int alliance)` swaps to the low-credit emergency policy personality when `IsRatingOkForRecruitment(useemergencylevel: true)` fails.
- `AICampaign.UpdateCompanyFoundations` respects construction rating except the subsidy-funded path immediately above the hard gate.
- `GameVars` tax constants are `tax_tarifs=0`, `tax_excise=1`, `tax_income=2`, `tax_corporate=3`, `tax_landsales=4`.

## File Structure

- Create `src/WhiskeyRealism/Strategic/Fiscal/FiscalModels.cs`
  Pure enums, inputs, options, result DTOs, and telemetry DTOs. No Unity, Harmony, or vanilla type references.

- Create `src/WhiskeyRealism/Strategic/Fiscal/FiscalIntentLedger.cs`
  Pure state-machine computation: posture, hysteresis, target tax/subsidy bands, supply flags, and suppressions.

- Create `src/WhiskeyRealism/Strategic/Fiscal/FiscalPolicyScorer.cs`
  Pure policy/project priority scoring by alliance, era, posture, fiscal stress, and intervention/supply pressure.

- Create `src/WhiskeyRealism/Strategic/Fiscal/FiscalConstructionScorer.cs`
  Pure building candidate scoring and suppressions.

- Create `src/WhiskeyRealism/Strategic/Fiscal/FiscalRuntime.cs`
  Reflection/runtime adapter that reads vanilla finance, rating, subsidy, construction, supply, and intervention data into pure inputs.

- Create `src/WhiskeyRealism/Patches/FinancialAIPatch.cs`
  Postfix on `AICampaign.UpdateFinancialAI(int alliance)` that applies bounded tax/subsidy corrections.

- Create `src/WhiskeyRealism/Patches/PolicySelectionPatch.cs`
  Prefix on `Policies.CheckAIPolicyChange(int alliance)` that selects a clearly better available fiscal policy.

- Modify `src/WhiskeyRealism/Patches/ProjectSelectionPatch.cs`
  Add fiscal project weights on top of existing grand-strategy project scoring.

- Create `src/WhiskeyRealism/Patches/EconomyConstructionPatch.cs`
  Bias vanilla-valid `bestiipplacesprob` candidates without direct construction calls.

- Modify `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`
  Store `FiscalIntentLedger[]`, compute during weekly/monthly review after front/army ledgers, and emit monthly telemetry.

- Modify `src/WhiskeyRealism/Strategic/FrontSectorLedger.cs`
  Retain average morale, supply, and readiness on `FrontSector` so fiscal runtime can read the supply signal already used by posture selection.

- Modify `src/WhiskeyRealism/Plugin.cs`
  Add `Fiscal Trace Logging` and optional `Fiscal Telemetry Csv` config entries.

- Modify `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
  Link the new pure fiscal files.

- Modify `tests/WhiskeyRealism.Tests/Program.cs`
  Add pure fiscal tests before Harmony integration.

## Implementation Tasks

### Task 1: Pure Fiscal Types

**Files:**
- Create: `src/WhiskeyRealism/Strategic/Fiscal/FiscalModels.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`

- [ ] **Step 1: Create the fiscal model file**

Add this file:

```csharp
namespace WhiskeyRealism.Strategic.Fiscal
{
    public enum FiscalPosture
    {
        Expansion = 0,
        BalancedWar = 1,
        CreditDefense = 2,
        EmergencySolvency = 3
    }

    public enum FiscalGate
    {
        None = 0,
        EmergencyPolicy = 1,
        Recruitment = 2,
        Construction = 3,
        WeaponPurchases = 4,
        BondFloor = 5
    }

    public enum FiscalSuppression
    {
        None = 0,
        DiscretionaryIndustry = 1,
        NewForceGrowth = 2,
        VanityNaval = 3,
        TariffConflict = 4,
        LowValueSubsidy = 5
    }

    public sealed class FiscalOptions
    {
        public float VanillaStep = 0.05f;
        public int CreditDefenseEntryBuffer = 1;
        public int CreditDefenseExitBuffer = 2;
        public int EmergencyExitWeeks = 2;
        public float MinimumSupplyProtection = 0.35f;
    }

    public sealed class FiscalStateMemory
    {
        public FiscalPosture PreviousPosture = FiscalPosture.BalancedWar;
        public int StableWeeksAboveEmergency;
        public bool EmergencyResidue;
    }

    public sealed class FiscalInput
    {
        public int AllianceId;
        public EraStage EraStage;
        public int CurrentChapter;
        public FiscalStateMemory Memory = new FiscalStateMemory();
        public int CurrentRating;
        public int RatingNotches;
        public int EmergencyPolicyFailureRating;
        public int RecruitmentFailureRating;
        public int ConstructionFailureRating;
        public int WeaponFailureRating;
        public float Treasury;
        public float Debt;
        public float AnnualBalance;
        public float InterestCost;
        public float ArmyUpkeep;
        public float NavyUpkeep;
        public float RecruitmentCost;
        public float SupplyDepotPurchases;
        public float[] Taxes = new float[5];
        public float[] TaxCaps = new float[5];
        public float[] Subsidies = new float[8];
        public float[] SubsidyFocus = new float[8];
        public bool HasWarBonds;
        public bool HasBankAct;
        public bool HasFreeTrade;
        public bool HasKingCotton;
        public bool HasOrganizedBlockadeRunning;
        public float InterventionProbability;
        public float SupplyPressure;
        public float AmmoPressure;
        public float TransportPressure;
        public int LowSupplyFormationCount;
        public int LowAmmoFormationCount;
        public string TopSupplyTheater;
    }

    public sealed class FiscalOutput
    {
        public FiscalPosture Posture;
        public FiscalGate DefendedGate;
        public int MinimumAcceptableRating;
        public bool SupplyProtection;
        public bool LogisticsExpansion;
        public bool ForceCapWarning;
        public string TheaterSupplyPriority;
        public float[] TargetTaxMin = new float[5];
        public float[] TargetTaxMax = new float[5];
        public float[] TargetSubsidyMin = new float[8];
        public float[] TargetSubsidyMax = new float[8];
        public FiscalSuppression[] Suppressions = new FiscalSuppression[0];
        public string Signature;
    }
}
```

- [ ] **Step 2: Link the fiscal model file into the console harness**

In `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, add this entry inside the existing `<ItemGroup>`:

```xml
    <Compile Include="..\..\src\WhiskeyRealism\Strategic\Fiscal\FiscalModels.cs" Link="FiscalModels.cs" />
```

- [ ] **Step 3: Run the harness and verify it still compiles**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected:

```text
PASS critical understrength sector holds
```

The command should exit `0`; the full output should include the existing project scorer tests.

- [ ] **Step 4: Commit**

```bash
git add src/WhiskeyRealism/Strategic/Fiscal/FiscalModels.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "feat: add fiscal model types"
```

### Task 2: Fiscal Intent Ledger State Machine

**Files:**
- Create: `src/WhiskeyRealism/Strategic/Fiscal/FiscalIntentLedger.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add failing fiscal posture tests**

Add `using WhiskeyRealism.Strategic.Fiscal;` to `tests/WhiskeyRealism.Tests/Program.cs`.

Add these entries to the `tests` array:

```csharp
("fiscal csa healthy credit stays balanced", FiscalCsaHealthyCreditStaysBalanced),
("fiscal enters credit defense before gate", FiscalEntersCreditDefenseBeforeGate),
("fiscal enters emergency before bond floor", FiscalEntersEmergencyBeforeBondFloor),
("fiscal protects supply before force growth", FiscalProtectsSupplyBeforeForceGrowth),
("fiscal hysteresis prevents immediate recovery", FiscalHysteresisPreventsImmediateRecovery)
```

Add these helper tests before `AssertEqual<T>`:

```csharp
private static FiscalInput BuildFiscalInput()
{
    return new FiscalInput
    {
        AllianceId = 1,
        EraStage = EraStage.Amateur1861,
        CurrentChapter = 1,
        CurrentRating = 4,
        RatingNotches = 12,
        EmergencyPolicyFailureRating = 7,
        RecruitmentFailureRating = 8,
        ConstructionFailureRating = 8,
        WeaponFailureRating = 9,
        Treasury = 15000000f,
        Debt = 75000000f,
        AnnualBalance = -5000000f,
        InterestCost = -3000000f,
        ArmyUpkeep = -25000000f,
        NavyUpkeep = -3000000f,
        RecruitmentCost = -4000000f,
        SupplyDepotPurchases = -2000000f,
        SupplyPressure = 0.15f,
        AmmoPressure = 0.10f,
        TransportPressure = 0.20f,
        LowSupplyFormationCount = 0,
        LowAmmoFormationCount = 0,
        TopSupplyTheater = "VirginiaCapitalCorridor"
    };
}

private static void FiscalCsaHealthyCreditStaysBalanced()
{
    var output = FiscalIntentLedger.Compute(BuildFiscalInput(), new FiscalOptions());
    AssertEqual(FiscalPosture.BalancedWar, output.Posture);
    AssertEqual(false, output.ForceCapWarning);
}

private static void FiscalEntersCreditDefenseBeforeGate()
{
    var input = BuildFiscalInput();
    input.CurrentRating = 6;
    var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
    AssertEqual(FiscalPosture.CreditDefense, output.Posture);
    AssertEqual(FiscalGate.EmergencyPolicy, output.DefendedGate);
}

private static void FiscalEntersEmergencyBeforeBondFloor()
{
    var input = BuildFiscalInput();
    input.CurrentRating = 11;
    input.Treasury = -1000000f;
    var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
    AssertEqual(FiscalPosture.EmergencySolvency, output.Posture);
    AssertEqual(true, output.ForceCapWarning);
}

private static void FiscalProtectsSupplyBeforeForceGrowth()
{
    var input = BuildFiscalInput();
    input.CurrentRating = 8;
    input.SupplyPressure = 0.85f;
    input.LowSupplyFormationCount = 4;
    input.LowAmmoFormationCount = 2;
    var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
    AssertEqual(true, output.SupplyProtection);
    AssertEqual(true, output.ForceCapWarning);
    AssertEqual("VirginiaCapitalCorridor", output.TheaterSupplyPriority);
}

private static void FiscalHysteresisPreventsImmediateRecovery()
{
    var input = BuildFiscalInput();
    input.CurrentRating = 6;
    input.Memory.PreviousPosture = FiscalPosture.CreditDefense;
    input.Memory.EmergencyResidue = true;
    var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
    AssertEqual(FiscalPosture.CreditDefense, output.Posture);
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: build fails with `The name 'FiscalIntentLedger' does not exist in the current context`.

- [ ] **Step 3: Create the ledger implementation**

Add `src/WhiskeyRealism/Strategic/Fiscal/FiscalIntentLedger.cs`:

```csharp
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Fiscal
{
    public static class FiscalIntentLedger
    {
        public static FiscalOutput Compute(FiscalInput input, FiscalOptions options)
        {
            options = options != null ? options : new FiscalOptions();
            input = input != null ? input : new FiscalInput();

            int notches = input.RatingNotches <= 0 ? 12 : input.RatingNotches;
            int bondFloor = notches - 1;
            int earliestGate = MinPositive(
                input.EmergencyPolicyFailureRating,
                input.RecruitmentFailureRating,
                input.ConstructionFailureRating,
                input.WeaponFailureRating,
                bondFloor);

            var output = new FiscalOutput();
            output.DefendedGate = ResolveGate(input, earliestGate, bondFloor);
            output.MinimumAcceptableRating = earliestGate - options.CreditDefenseEntryBuffer;

            bool supplyStress = input.SupplyPressure >= options.MinimumSupplyProtection
                || input.AmmoPressure >= options.MinimumSupplyProtection
                || input.TransportPressure >= options.MinimumSupplyProtection
                || input.LowSupplyFormationCount > 0
                || input.LowAmmoFormationCount > 0;

            bool emergency = input.CurrentRating >= bondFloor - 1
                || input.CurrentRating >= earliestGate
                || input.Treasury < 0f;

            bool creditDefense = input.CurrentRating >= earliestGate - options.CreditDefenseEntryBuffer;

            if (emergency)
            {
                output.Posture = FiscalPosture.EmergencySolvency;
            }
            else if (creditDefense || input.Memory.PreviousPosture == FiscalPosture.CreditDefense || input.Memory.EmergencyResidue)
            {
                bool clearlyRecovered = input.CurrentRating <= earliestGate - options.CreditDefenseExitBuffer
                    && input.AnnualBalance >= 0f
                    && !input.Memory.EmergencyResidue;
                output.Posture = clearlyRecovered ? FiscalPosture.BalancedWar : FiscalPosture.CreditDefense;
            }
            else if (input.AnnualBalance > 1000000f && input.CurrentRating <= earliestGate - 3 && !supplyStress)
            {
                output.Posture = FiscalPosture.Expansion;
            }
            else
            {
                output.Posture = FiscalPosture.BalancedWar;
            }

            output.SupplyProtection = supplyStress;
            output.LogisticsExpansion = supplyStress && output.Posture != FiscalPosture.EmergencySolvency;
            output.ForceCapWarning = output.Posture == FiscalPosture.EmergencySolvency
                || supplyStress
                || input.ArmyUpkeep + input.NavyUpkeep + input.RecruitmentCost + input.SupplyDepotPurchases < input.AnnualBalance;
            output.TheaterSupplyPriority = supplyStress ? (input.TopSupplyTheater != null ? input.TopSupplyTheater : string.Empty) : string.Empty;

            FillTargets(input, output, options);
            output.Suppressions = BuildSuppressions(input, output);
            output.Signature = input.AllianceId + ":" + output.Posture + ":" + output.DefendedGate + ":" + output.SupplyProtection + ":" + output.ForceCapWarning;
            return output;
        }

        private static int MinPositive(params int[] values)
        {
            int result = int.MaxValue;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] >= 0 && values[i] < result)
                    result = values[i];
            }
            return result == int.MaxValue ? 0 : result;
        }

        private static FiscalGate ResolveGate(FiscalInput input, int earliestGate, int bondFloor)
        {
            if (earliestGate == input.EmergencyPolicyFailureRating) return FiscalGate.EmergencyPolicy;
            if (earliestGate == input.RecruitmentFailureRating) return FiscalGate.Recruitment;
            if (earliestGate == input.ConstructionFailureRating) return FiscalGate.Construction;
            if (earliestGate == input.WeaponFailureRating) return FiscalGate.WeaponPurchases;
            if (earliestGate == bondFloor) return FiscalGate.BondFloor;
            return FiscalGate.None;
        }

        private static void FillTargets(FiscalInput input, FiscalOutput output, FiscalOptions options)
        {
            CopyBand(input.Taxes, output.TargetTaxMin, output.TargetTaxMax);
            CopyBand(input.Subsidies, output.TargetSubsidyMin, output.TargetSubsidyMax);

            if (output.Posture == FiscalPosture.CreditDefense || output.Posture == FiscalPosture.EmergencySolvency)
            {
                RaiseTaxBand(output, 1, options.VanillaStep);
                RaiseTaxBand(output, 2, options.VanillaStep);
                LowerSubsidyBand(output, 3, options.VanillaStep);
            }

            if (input.AllianceId == 1 && (input.HasKingCotton || input.HasFreeTrade || input.HasOrganizedBlockadeRunning) && output.Posture <= FiscalPosture.BalancedWar)
            {
                output.TargetTaxMax[0] = input.Taxes[0] < 0.15f ? 0.15f : input.Taxes[0];
            }

            for (int i = 0; i < output.TargetSubsidyMax.Length && i < input.SubsidyFocus.Length; i++)
                if (output.TargetSubsidyMax[i] > input.SubsidyFocus[i])
                    output.TargetSubsidyMax[i] = input.SubsidyFocus[i];
        }

        private static void CopyBand(float[] source, float[] min, float[] max)
        {
            for (int i = 0; i < min.Length && i < source.Length; i++)
            {
                min[i] = source[i];
                max[i] = source[i];
            }
        }

        private static void RaiseTaxBand(FiscalOutput output, int lane, float step)
        {
            if (lane < 0 || lane >= output.TargetTaxMax.Length) return;
            output.TargetTaxMax[lane] += step;
            output.TargetTaxMin[lane] += step;
        }

        private static void LowerSubsidyBand(FiscalOutput output, int lane, float step)
        {
            if (lane < 0 || lane >= output.TargetSubsidyMax.Length) return;
            output.TargetSubsidyMax[lane] -= step;
            if (output.TargetSubsidyMax[lane] < 0f) output.TargetSubsidyMax[lane] = 0f;
            output.TargetSubsidyMin[lane] = output.TargetSubsidyMax[lane];
        }

        private static FiscalSuppression[] BuildSuppressions(FiscalInput input, FiscalOutput output)
        {
            var list = new List<FiscalSuppression>();
            if (output.Posture == FiscalPosture.EmergencySolvency)
            {
                list.Add(FiscalSuppression.NewForceGrowth);
                list.Add(FiscalSuppression.VanityNaval);
                list.Add(FiscalSuppression.DiscretionaryIndustry);
            }
            if (input.AllianceId == 1 && output.Posture <= FiscalPosture.BalancedWar && (input.HasKingCotton || input.HasFreeTrade))
                list.Add(FiscalSuppression.TariffConflict);
            return list.ToArray();
        }
    }
}
```

- [ ] **Step 4: Link the ledger into the console harness**

In `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, add:

```xml
    <Compile Include="..\..\src\WhiskeyRealism\Strategic\Fiscal\FiscalIntentLedger.cs" Link="FiscalIntentLedger.cs" />
```

- [ ] **Step 5: Run tests to verify pass**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: output includes:

```text
PASS fiscal csa healthy credit stays balanced
PASS fiscal enters credit defense before gate
PASS fiscal enters emergency before bond floor
PASS fiscal protects supply before force growth
PASS fiscal hysteresis prevents immediate recovery
```

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Strategic/Fiscal/FiscalIntentLedger.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: compute fiscal intent ledger"
```

### Task 3: Runtime Fiscal Snapshot and Coordinator Storage

**Files:**
- Create: `src/WhiskeyRealism/Strategic/Fiscal/FiscalRuntime.cs`
- Modify: `src/WhiskeyRealism/Strategic/FrontSectorLedger.cs`
- Modify: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`

- [ ] **Step 1: Retain supply values on front sectors**

In `src/WhiskeyRealism/Strategic/FrontSectorLedger.cs`, add these fields to `FrontSector`:

```csharp
        public float AverageMorale;
        public float AverageSupply;
        public float AverageReadiness;
```

In the `new FrontSector` initializer inside `Build`, add:

```csharp
                    AverageMorale = input.AverageMorale,
                    AverageSupply = input.AverageSupply,
                    AverageReadiness = input.AverageReadiness
```

- [ ] **Step 2: Create runtime adapter**

Add `src/WhiskeyRealism/Strategic/Fiscal/FiscalRuntime.cs`:

```csharp
using System;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Fiscal
{
    public static class FiscalRuntime
    {
        public static FiscalInput BuildInput(int alliance, EraStage era, FiscalStateMemory memory)
        {
            var input = new FiscalInput
            {
                AllianceId = alliance,
                EraStage = era,
                CurrentChapter = SafePolicyChapter(),
                Memory = memory != null ? memory : new FiscalStateMemory(),
                RatingNotches = GamePrefs.ratingnotches != null ? GamePrefs.ratingnotches.Length : 12,
                CurrentRating = SafeRating(alliance),
                Treasury = SafeTreasury(alliance),
                Debt = SafeDebt(alliance),
                AnnualBalance = SafeBalance(alliance),
                InterestCost = SafeInterestCost(alliance),
                ArmyUpkeep = SafeArmyUpkeep(alliance),
                NavyUpkeep = SafeNavyUpkeep(alliance),
                RecruitmentCost = SafeRecruitmentCost(alliance),
                SupplyDepotPurchases = SafeSupplyDepotPurchases(alliance),
                TopSupplyTheater = string.Empty
            };

            input.EmergencyPolicyFailureRating = RatingFailureFromFraction(GamePrefs.emergencyaicredittrigger, input.RatingNotches);
            input.RecruitmentFailureRating = RatingFailureFromFraction(GamePrefs.minimumratingforrecruitment, input.RatingNotches);
            input.ConstructionFailureRating = RatingFailureFromFraction(GamePrefs.minimumratingforconstruction, input.RatingNotches);
            input.WeaponFailureRating = RatingFailureFromFraction(GamePrefs.minimumratingforweaponorders, input.RatingNotches);

            CopyArray(SafeTaxes(alliance), input.Taxes);
            CopyArray(SafeTaxCaps(alliance), input.TaxCaps);
            CopyArray(SafeSubsidies(alliance), input.Subsidies);
            CopyArray(SafeSubsidyFocus(alliance), input.SubsidyFocus);

            FillPolicyFlags(input);
            FillSupplyPressure(input);
            return input;
        }

        private static int RatingFailureFromFraction(float fraction, int notches)
        {
            return UnityEngine.Mathf.CeilToInt((1f - fraction) * notches);
        }

        private static int SafePolicyChapter()
        {
            try { return Policy.CurrentChapter; }
            catch { return -1; }
        }

        private static int SafeRating(int alliance)
        {
            try { return GameVars.alliance[alliance].currentrating; }
            catch { return 0; }
        }

        private static float SafeTreasury(int alliance)
        {
            try { return GameVars.alliance[alliance].treasury; }
            catch { return 0f; }
        }

        private static float SafeDebt(int alliance)
        {
            try { return GameVars.alliance[alliance].debt; }
            catch { return 0f; }
        }

        private static float SafeBalance(int alliance)
        {
            try { return GameVars.alliance[alliance].GetBalance(); }
            catch { return 0f; }
        }

        private static float SafeInterestCost(int alliance)
        {
            try { return GameVars.alliance[alliance].interestcostpa; }
            catch { return 0f; }
        }

        private static float SafeArmyUpkeep(int alliance)
        {
            try { return GameVars.alliance[alliance].armyupkeeppa; }
            catch { return 0f; }
        }

        private static float SafeNavyUpkeep(int alliance)
        {
            try { return GameVars.alliance[alliance].navyupkeeppa; }
            catch { return 0f; }
        }

        private static float SafeRecruitmentCost(int alliance)
        {
            try { return GameVars.alliance[alliance].recruitmentcostpa; }
            catch { return 0f; }
        }

        private static float SafeSupplyDepotPurchases(int alliance)
        {
            try { return GameVars.alliance[alliance].supplydepotpurchasespa; }
            catch { return 0f; }
        }

        private static float[] SafeTaxes(int alliance)
        {
            try { return GameVars.alliance[alliance].tax; }
            catch { return null; }
        }

        private static float[] SafeTaxCaps(int alliance)
        {
            try { return GameVars.alliance[alliance].taxcaps; }
            catch { return null; }
        }

        private static float[] SafeSubsidies(int alliance)
        {
            try { return GameVars.alliance[alliance].subsidies; }
            catch { return null; }
        }

        private static float[] SafeSubsidyFocus(int alliance)
        {
            try
            {
                var personality = GameVars.alliance[alliance].GetAIPersonality(alliance);
                return personality != null ? personality.subsidyfocus : null;
            }
            catch { return null; }
        }

        private static void CopyArray(float[] source, float[] target)
        {
            if (source == null || target == null) return;
            for (int i = 0; i < source.Length && i < target.Length; i++)
                target[i] = source[i];
        }

        private static void FillPolicyFlags(FiscalInput input)
        {
            try
            {
                input.HasWarBonds = Policy.GetStatusByPolicyID(input.AllianceId == 1 ? 122 : 22);
                input.HasBankAct = Policy.GetStatusByPolicyID(input.AllianceId == 1 ? 123 : 23);
                input.HasKingCotton = Policy.GetStatusByPolicyID(103) || Policy.GetStatusByPolicyID(104) || Policy.GetStatusByPolicyID(105) || Policy.GetStatusByPolicyID(106);
                input.HasFreeTrade = Policy.GetStatusByPolicyID(141);
                input.HasOrganizedBlockadeRunning = Policy.GetStatusByPolicyID(142);
                input.InterventionProbability = GameVars.Alliance.GetInterventionProbability(2) + GameVars.Alliance.GetInterventionProbability(3);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("fiscal-runtime:policy-flags", "[FiscalRuntime] policy flag read failed: " + ex.Message);
            }
        }

        private static void FillSupplyPressure(FiscalInput input)
        {
            var coordinator = StrategicCoordinator.Instance;
            var front = coordinator != null && input.AllianceId >= 0 && input.AllianceId < coordinator.Fronts.Length
                ? coordinator.Fronts[input.AllianceId]
                : null;

            if (front == null) return;

            int sectors = 0;
            float supplyStress = 0f;
            foreach (var sector in front.Sectors)
            {
                sectors++;
                supplyStress += 1f - sector.AverageSupply;
                if (sector.AverageSupply < 0.45f)
                    input.LowSupplyFormationCount++;
                if (sector.Posture == FrontPosture.Hold && sector.AverageSupply < 0.65f)
                    input.TopSupplyTheater = sector.Theater.ToString();
            }
            input.SupplyPressure = sectors > 0 ? supplyStress / sectors : 0f;
            input.TransportPressure = input.SupplyPressure;
            input.AmmoPressure = input.SupplyPressure * 0.5f;
        }
    }
}
```

- [ ] **Step 3: Store fiscal intent in the coordinator**

In `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`, add:

```csharp
using WhiskeyRealism.Strategic.Fiscal;
```

Add fields near existing ledgers:

```csharp
        public FiscalOutput[] FiscalIntents = new FiscalOutput[2];
        private readonly FiscalStateMemory[] _fiscalMemory = new FiscalStateMemory[2]
        {
            new FiscalStateMemory(),
            new FiscalStateMemory()
        };
        private readonly string[] _fiscalSignatures = new string[2];
```

After `UpdateArmyAreaLedger(alliance, cic);` in `RunStrategicReview`, insert:

```csharp
                UpdateFiscalIntent(alliance, era.Stage, day, month, year, logHeartbeat);
```

Add this method near `UpdateArmyAreaLedger`:

```csharp
        private void UpdateFiscalIntent(int alliance, EraStage era, int day, int month, int year, bool logHeartbeat)
        {
            var input = FiscalRuntime.BuildInput(alliance, era, _fiscalMemory[alliance]);
            var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
            FiscalIntents[alliance] = output;
            _fiscalMemory[alliance].PreviousPosture = output.Posture;
            _fiscalMemory[alliance].EmergencyResidue = output.Posture == FiscalPosture.EmergencySolvency || _fiscalMemory[alliance].EmergencyResidue && output.Posture == FiscalPosture.CreditDefense;

            if (Plugin.Instance.VerboseLogging.Value || _fiscalSignatures[alliance] != output.Signature)
            {
                Plugin.Log.LogInfo($"[FiscalIntent] alliance={alliance} posture={output.Posture} gate={output.DefendedGate} supply={output.SupplyProtection} forceCap={output.ForceCapWarning}");
                _fiscalSignatures[alliance] = output.Signature;
            }

            if (logHeartbeat)
            {
                Plugin.Log.LogInfo($"[FiscalTelemetry] alliance={alliance} posture={output.Posture} gate={output.DefendedGate} supply={output.SupplyProtection} theater={output.TheaterSupplyPriority}");
            }
        }
```

- [ ] **Step 4: Build**

Run:

```bash
./build.sh
```

Expected: build exits `0`.

- [ ] **Step 5: Commit**

```bash
git add src/WhiskeyRealism/Strategic/Fiscal/FiscalRuntime.cs src/WhiskeyRealism/Strategic/FrontSectorLedger.cs src/WhiskeyRealism/Strategic/StrategicCoordinator.cs
git commit -m "feat: compute runtime fiscal intent"
```

### Task 4: Financial AI Patch

**Files:**
- Create: `src/WhiskeyRealism/Patches/FinancialAIPatch.cs`
- Modify: `docs/patch-catalog.md`

- [ ] **Step 1: Add the financial patch**

Create `src/WhiskeyRealism/Patches/FinancialAIPatch.cs`:

```csharp
using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla UpdateFinancialAI issues/redeems bonds, nudges one random tax lane,
    // and raises/cuts one random subsidy lane. This Postfix keeps vanilla bond
    // behavior intact, then applies bounded corrections from FiscalIntent.
    [HarmonyPatch(typeof(AICampaign), "UpdateFinancialAI")]
    internal static class FinancialAIPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(int alliance)
        {
            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                if (StrategicCoordinator.Instance == null) return;
                if (alliance < 0 || alliance >= 2) return;
                if (GameVars.frame <= 50) return;
                if (alliance == GameVars.playeralliance && !GameVars.automanagefinances && !GameVars.ai_vs_ai) return;

                var intent = StrategicCoordinator.Instance.FiscalIntents[alliance];
                if (intent == null) return;
                var state = GameVars.alliance[alliance];
                if (state == null || state.tax == null || state.subsidies == null) return;

                OnceLog.Info("financial-ai", "FinancialAIPatch wired");

                int moves = 0;
                moves += MoveTaxTowardTarget(alliance, state.tax, intent, 1);
                if (moves < 2) moves += MoveTaxTowardTarget(alliance, state.tax, intent, 2);
                if (moves < 2) moves += MoveSubsidyTowardTarget(alliance, state.subsidies, intent, 3);
                if (moves < 2) moves += MoveSubsidyTowardTarget(alliance, state.subsidies, intent, 4);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("financial-ai:postfix", "[Patch:FinancialAI] postfix failed: " + ex.Message);
            }
        }

        private static int MoveTaxTowardTarget(int alliance, float[] tax, FiscalOutput intent, int lane)
        {
            if (lane < 0 || lane >= tax.Length || lane >= intent.TargetTaxMin.Length) return 0;
            float old = tax[lane];
            float targetMin = intent.TargetTaxMin[lane];
            float targetMax = intent.TargetTaxMax[lane];
            float step = GamePrefs.taxstepsai;

            if (old < targetMin)
                tax[lane] = UnityEngine.Mathf.Min(targetMin, old + step);
            else if (old > targetMax)
                tax[lane] = UnityEngine.Mathf.Max(targetMax, old - step);
            else
                return 0;

            Plugin.Log.LogInfo($"[Patch:FinancialAI] alliance={alliance} taxLane={lane} old={old:F2} new={tax[lane]:F2} posture={intent.Posture}");
            return 1;
        }

        private static int MoveSubsidyTowardTarget(int alliance, float[] subsidies, FiscalOutput intent, int lane)
        {
            if (lane < 0 || lane >= subsidies.Length || lane >= intent.TargetSubsidyMin.Length) return 0;
            float old = subsidies[lane];
            float targetMin = intent.TargetSubsidyMin[lane];
            float targetMax = intent.TargetSubsidyMax[lane];
            float step = GamePrefs.taxstepsai;

            if (old < targetMin)
                subsidies[lane] = UnityEngine.Mathf.Min(targetMin, old + step);
            else if (old > targetMax)
                subsidies[lane] = UnityEngine.Mathf.Max(targetMax, old - step);
            else
                return 0;

            Plugin.Log.LogInfo($"[Patch:FinancialAI] alliance={alliance} subsidyLane={lane} old={old:F2} new={subsidies[lane]:F2} posture={intent.Posture}");
            return 1;
        }
    }
}
```

- [ ] **Step 2: Update patch catalog**

Add a row to `docs/patch-catalog.md` for `FinancialAIPatch`:

```markdown
| 18 | `FinancialAIPatch` | Postfix | `Patches/FinancialAIPatch.cs` | `AICampaign.UpdateFinancialAI` (15352) | Fiscal intent correction after vanilla bond/tax/subsidy logic; bounded to at most two lane moves and respects player automanage gates. |
```

- [ ] **Step 3: Build**

Run:

```bash
./build.sh
```

Expected: build exits `0`.

- [ ] **Step 4: Commit**

```bash
git add src/WhiskeyRealism/Patches/FinancialAIPatch.cs docs/patch-catalog.md
git commit -m "feat: steer ai financial sliders"
```

### Task 5: Fiscal Policy and Project Priorities

**Files:**
- Create: `src/WhiskeyRealism/Strategic/Fiscal/FiscalPolicyScorer.cs`
- Modify: `src/WhiskeyRealism/Patches/ProjectSelectionPatch.cs`
- Create: `src/WhiskeyRealism/Patches/PolicySelectionPatch.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `docs/patch-catalog.md`

- [ ] **Step 1: Create fiscal policy scorer**

Add `src/WhiskeyRealism/Strategic/Fiscal/FiscalPolicyScorer.cs`:

```csharp
namespace WhiskeyRealism.Strategic.Fiscal
{
    public static class FiscalPolicyScorer
    {
        public static float ProjectWeight(FiscalOutput intent, int alliance, int projectId, int subsidyType)
        {
            if (intent == null) return 0f;
            float score = 0f;

            if (projectId == 97 && intent.Posture >= FiscalPosture.CreditDefense)
                score += 1.25f;
            if (projectId == 103 && alliance == 1)
                score += intent.Posture <= FiscalPosture.BalancedWar ? 0.9f : 0.35f;
            if ((projectId == 0 || projectId == 1 || projectId == 2 || projectId == 3 || projectId == 4) && alliance == 1)
                score += 1.0f;
            if ((projectId == 35 || projectId == 38 || projectId == 39 || projectId == 40 || projectId == 41) && alliance == 1 && intent.Posture >= FiscalPosture.CreditDefense)
                score -= 1.0f;
            if ((projectId == 99 || projectId == 100 || projectId == 119) && (intent.SupplyProtection || intent.LogisticsExpansion))
                score += 1.1f;
            if (projectId == 120 && alliance == 1 && intent.Posture >= FiscalPosture.CreditDefense)
                score -= 0.6f;

            return score;
        }

        public static float PolicyWeight(FiscalOutput intent, int alliance, int policyId)
        {
            if (intent == null) return 0f;
            float score = 0f;

            if ((policyId == 22 || policyId == 122 || policyId == 23 || policyId == 123) && intent.Posture >= FiscalPosture.CreditDefense)
                score += 1.5f;
            if (alliance == 1 && (policyId == 103 || policyId == 104 || policyId == 105 || policyId == 106) && intent.Posture <= FiscalPosture.BalancedWar)
                score += 0.8f;
            if (alliance == 1 && policyId == 141 && intent.Posture <= FiscalPosture.BalancedWar)
                score += 0.9f;
            if (alliance == 1 && policyId == 142 && intent.SupplyProtection)
                score += 0.7f;
            if ((policyId == 36 || policyId == 136) && intent.ForceCapWarning)
                score -= 0.8f;

            return score;
        }
    }
}
```

- [ ] **Step 2: Link fiscal scorer into the console harness**

In `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, add:

```xml
    <Compile Include="..\..\src\WhiskeyRealism\Strategic\Fiscal\FiscalPolicyScorer.cs" Link="FiscalPolicyScorer.cs" />
```

- [ ] **Step 3: Modify project scoring patch**

In `ProjectSelectionPatch.Prefix`, after resolving `profile`, add:

```csharp
                var fiscalIntent = StrategicCoordinator.Instance.FiscalIntents[alliance];
```

Replace the selection call with:

```csharp
                    var decision = ProjectSelectionScorer.Select(
                        profile,
                        subsidyType,
                        vanillaProjectId,
                        vanillaWeight,
                        candidates,
                        projectId => FiscalPolicyScorer.ProjectWeight(fiscalIntent, alliance, projectId, subsidyType));
```

Update `ProjectSelectionScorer.Select` signature to accept `System.Func<int, float> extraWeight`, and update `Score` to add that extra weight:

```csharp
        public static ProjectSelectionDecision Select(
            GrandStrategyProfile profile,
            int subsidyType,
            int vanillaProjectId,
            float vanillaWeight,
            IEnumerable<ProjectCandidateInput> candidates,
            System.Func<int, float> extraWeight = null)
```

In `Score`, use:

```csharp
            float profileWeight = profile != null ? profile.ProjectWeightFor(projectId) : 0f;
            float fiscalWeight = extraWeight != null ? extraWeight.Invoke(projectId) : 0f;
            return vanillaWeight + profileWeight + fiscalWeight;
```

- [ ] **Step 4: Add policy selection patch**

Create `src/WhiskeyRealism/Patches/PolicySelectionPatch.cs`:

```csharp
using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla CheckAIPolicyChange walks the active AI personality policy list.
    // This Prefix only intervenes when a fiscal policy has a clear score win
    // and passes vanilla availability checks.
    [HarmonyPatch(typeof(Policies), "CheckAIPolicyChange")]
    internal static class PolicySelectionPatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(int alliance)
        {
            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return true;
                if (StrategicCoordinator.Instance == null) return true;
                if (alliance < 0 || alliance >= 2) return true;
                if (GameVars.frame <= 50) return true;

                var intent = StrategicCoordinator.Instance.FiscalIntents[alliance];
                if (intent == null) return true;
                var personality = GameVars.alliance[alliance].GetAIPersonality(alliance);
                if (personality == null || personality.policies == null) return true;

                int bestPolicy = -1;
                float bestScore = 0.75f;
                for (int i = 0; i < personality.policies.Count; i++)
                {
                    int policyId = personality.policies[i];
                    var policy = Policy.GetPolicyFromID(policyId);
                    if (!IsAvailable(policy, alliance)) continue;

                    float score = FiscalPolicyScorer.PolicyWeight(intent, alliance, policyId);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestPolicy = policyId;
                    }
                }

                if (bestPolicy < 0) return true;

                var selected = Policy.GetPolicyFromID(bestPolicy);
                Policies.AddResearch(selected, alliance);
                OnceLog.Info("policy-selection", "PolicySelectionPatch wired");
                Plugin.Log.LogInfo($"[Patch:PolicySelection] alliance={alliance} policy={bestPolicy} posture={intent.Posture} score={bestScore:F2}");
                return false;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("policy-selection:prefix", "[Patch:PolicySelection] prefix failed: " + ex.Message);
                return true;
            }
        }

        private static bool IsAvailable(Policy policy, int alliance)
        {
            if (policy == null) return false;
            if (GameVars.alliance[alliance].activatedpolicies.Contains(policy)) return false;
            if (GameVars.alliance[alliance].activatedacts.Contains(policy)) return false;
            if (policy.ReferedChapter > Policy.CurrentChapter) return false;
            if (!policy.HasPrePolicies()) return false;
            if (Policies.IsDeactivated(policy)) return false;
            if (policy.blocked) return false;
            if (!policy.IsAvailableInScenario()) return false;
            return true;
        }
    }
}
```

- [ ] **Step 5: Build**

Run:

```bash
./build.sh
```

Expected: build exits `0`. If `Policies.AddResearch` or `Policies.IsDeactivated` access is not public, replace those calls with `AccessTools.Method` reflection and keep the same availability semantics.

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Strategic/Fiscal/FiscalPolicyScorer.cs src/WhiskeyRealism/Patches/ProjectSelectionPatch.cs src/WhiskeyRealism/Patches/PolicySelectionPatch.cs src/WhiskeyRealism/Strategic/ProjectSelectionScorer.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj docs/patch-catalog.md
git commit -m "feat: steer fiscal policies and projects"
```

### Task 6: Construction Scoring and Patch

**Files:**
- Create: `src/WhiskeyRealism/Strategic/Fiscal/FiscalConstructionScorer.cs`
- Create: `src/WhiskeyRealism/Patches/EconomyConstructionPatch.cs`
- Modify: `docs/patch-catalog.md`

- [ ] **Step 1: Create construction scorer**

Add `src/WhiskeyRealism/Strategic/Fiscal/FiscalConstructionScorer.cs`:

```csharp
namespace WhiskeyRealism.Strategic.Fiscal
{
    public static class FiscalConstructionScorer
    {
        public static float Multiplier(FiscalOutput intent, int alliance, string buildingName, int subsidyType)
        {
            if (intent == null || string.IsNullOrEmpty(buildingName)) return 1f;
            string name = buildingName.ToLowerInvariant();
            float mult = 1f;

            if (name.Contains("bank") && intent.Posture <= FiscalPosture.BalancedWar)
                mult += alliance == 1 ? 0.60f : 0.35f;
            if ((name.Contains("market") || name.Contains("rail") || name.Contains("depot")) && (intent.SupplyProtection || intent.LogisticsExpansion))
                mult += 0.75f;
            if (name.Contains("hospital") && intent.SupplyProtection)
                mult += 0.35f;
            if ((name.Contains("shipyard") || name.Contains("naval")) && alliance == 1 && intent.Posture >= FiscalPosture.CreditDefense)
                mult -= 0.50f;
            if ((name.Contains("factory") || name.Contains("foundry") || name.Contains("industrial")) && intent.Posture == FiscalPosture.EmergencySolvency)
                mult -= 0.45f;

            if (mult < 0.15f) mult = 0.15f;
            return mult;
        }
    }
}
```

- [ ] **Step 2: Add construction patch**

Create `src/WhiskeyRealism/Patches/EconomyConstructionPatch.cs`:

```csharp
using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla UpdateCompanyFoundations fills bestiipplaces/bestiipplacesprob
    // and later consumes the best valid candidate. This Postfix biases
    // probabilities only after vanilla has validated candidates.
    [HarmonyPatch(typeof(AICampaign), "UpdateCompanyFoundations")]
    internal static class EconomyConstructionPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(int alliancerunthrough)
        {
            try
            {
                int alliance = alliancerunthrough;
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                if (StrategicCoordinator.Instance == null) return;
                if (alliance < 0 || alliance >= 2) return;
                if (alliance == GameVars.playeralliance && !GameVars.automanageconstructiong && !GameVars.ai_vs_ai) return;

                var intent = StrategicCoordinator.Instance.FiscalIntents[alliance];
                if (intent == null) return;

                var state = GameVars.alliance[alliance];
                if (state == null || state.bestiipplaces == null || state.bestiipplacesprob == null) return;

                OnceLog.Info("economy-construction", "EconomyConstructionPatch wired");

                int count = UnityEngine.Mathf.Min(state.bestiipplaces.Length, state.bestiipplacesprob.Length);
                for (int buildingType = 0; buildingType < count && buildingType < GameVars.buildingtypes.Count; buildingType++)
                {
                    if (state.bestiipplaces[buildingType] == null) continue;
                    if (state.bestiipplacesprob[buildingType] <= 0f) continue;
                    var type = GameVars.buildingtypes[buildingType];
                    if (type == null || !type.aiplacement) continue;

                    float oldProb = state.bestiipplacesprob[buildingType];
                    float mult = FiscalConstructionScorer.Multiplier(intent, alliance, type.name, type.subsidytype);
                    if (UnityEngine.Mathf.Abs(mult - 1f) < 0.01f) continue;
                    state.bestiipplacesprob[buildingType] = oldProb * mult;
                    if (Plugin.Instance.VerboseLogging.Value)
                        Plugin.Log.LogInfo($"[Patch:EconomyConstruction] alliance={alliance} building={type.name} oldProb={oldProb:F3} newProb={state.bestiipplacesprob[buildingType]:F3} posture={intent.Posture}");
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("economy-construction:postfix", "[Patch:EconomyConstruction] postfix failed: " + ex.Message);
            }
        }
    }
}
```

- [ ] **Step 3: Build**

Run:

```bash
./build.sh
```

Expected: build exits `0`. If `BuildingType.name` is inaccessible, use reflection via `AccessTools.Field(type.GetType(), "name")` and log a bounded warning on failure.

- [ ] **Step 4: Commit**

```bash
git add src/WhiskeyRealism/Strategic/Fiscal/FiscalConstructionScorer.cs src/WhiskeyRealism/Patches/EconomyConstructionPatch.cs docs/patch-catalog.md
git commit -m "feat: bias ai economy construction"
```

### Task 7: Recruitment and Supply Guardrails

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/Fiscal/FiscalIntentLedger.cs`
- Modify: `src/WhiskeyRealism/Strategic/Fiscal/FiscalPolicyScorer.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add force-cap tests**

Add this test entry:

```csharp
("fiscal force cap suppresses manpower policies", FiscalForceCapSuppressesManpowerPolicies)
```

Add this test method:

```csharp
private static void FiscalForceCapSuppressesManpowerPolicies()
{
    var input = BuildFiscalInput();
    input.CurrentRating = 10;
    input.SupplyPressure = 0.80f;
    input.LowSupplyFormationCount = 5;
    var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
    float draftWeight = FiscalPolicyScorer.PolicyWeight(output, 1, 136);
    AssertTrue(draftWeight < 0f, "force-cap state should suppress CSA draft escalation");
}
```

- [ ] **Step 2: Run tests to verify pass**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected:

```text
PASS fiscal force cap suppresses manpower policies
```

- [ ] **Step 3: Build**

Run:

```bash
./build.sh
```

Expected: build exits `0`.

- [ ] **Step 4: Commit**

```bash
git add src/WhiskeyRealism/Strategic/Fiscal/FiscalIntentLedger.cs src/WhiskeyRealism/Strategic/Fiscal/FiscalPolicyScorer.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: guard fiscal manpower growth"
```

### Task 8: Diagnostics, Telemetry Config, and Deploy Verification

**Files:**
- Modify: `src/WhiskeyRealism/Plugin.cs`
- Modify: `src/WhiskeyRealism/Strategic/StrategicCoordinator.cs`
- Modify: `docs/patch-catalog.md`
- Modify: `docs/handoff.md`

- [ ] **Step 1: Add config entries**

In `Plugin.cs`, add fields:

```csharp
        internal ConfigEntry<bool> FiscalTrace;
        internal ConfigEntry<bool> FiscalTelemetryCsv;
```

After `SuccessionTrace = Config.Bind(...)`, add:

```csharp
            FiscalTrace = Config.Bind(
                "Diagnostics", "Fiscal Trace Logging", false,
                "Emit fiscal posture, gate, supply, and finance override reasoning.");
            FiscalTelemetryCsv = Config.Bind(
                "Diagnostics", "Fiscal Telemetry Csv", false,
                "Write monthly fiscal telemetry rows next to the save sidecar for baseline-vs-modded comparisons.");
```

- [ ] **Step 2: Gate detailed fiscal logs**

In `StrategicCoordinator.UpdateFiscalIntent`, replace the verbose check with:

```csharp
            if (Plugin.Instance.VerboseLogging.Value || Plugin.Instance.FiscalTrace.Value || _fiscalSignatures[alliance] != output.Signature)
```

Keep `[FiscalTelemetry]` monthly logs in the heartbeat path so smoke tests can see them without verbose logging.

- [ ] **Step 3: Build and run pure tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected: test command exits `0`; build exits `0`.

- [ ] **Step 4: Deploy and hash-verify the DLL**

Run:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: both SHA-256 lines match exactly. If `cp` fails with `Invalid argument`, close GTCW and rerun the deploy/hash commands.

- [ ] **Step 5: Runtime smoke**

Start GTCW, load or start a W&L career, and inspect:

```bash
rg -n "FinancialAIPatch wired|PolicySelectionPatch wired|EconomyConstructionPatch wired|FiscalIntent|FiscalTelemetry|Patch:FinancialAI|Patch:PolicySelection|Patch:EconomyConstruction" "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Expected after a campaign AI tick:

```text
[once:financial-ai] FinancialAIPatch wired
[FiscalIntent] alliance=
[FiscalTelemetry] alliance=
```

Policy/construction override lines appear only when vanilla produces a candidate that Whiskey changes.

- [ ] **Step 6: Update docs and commit**

Update `docs/handoff.md` with a short v0.2.3 local note:

```markdown
- **2026-05-04 — v0.2.3 fiscal economy AI implemented locally.** Adds FiscalIntentLedger, bounded finance/policy/project/construction steering, supply-coupled fiscal posture, and monthly fiscal telemetry. Console tests and `./build.sh` pass; DLL deployed and SHA-256 verified. Runtime smoke confirmed first-fire markers after a campaign AI tick.
```

Commit:

```bash
git add src/WhiskeyRealism/Plugin.cs src/WhiskeyRealism/Strategic/StrategicCoordinator.cs docs/patch-catalog.md docs/handoff.md
git commit -m "docs: record fiscal economy implementation"
```

## Self-Review Checklist

- [ ] Spec coverage: credit cushion, `subsidyfocus`, land-sales skip, bond floor, bank pre-positioning, hysteresis, emergency residue, CSA tariffs, imports before vanity naval, telemetry, and military supply coupling each map to a task above.
- [ ] Tests cover the pure ledger before any Harmony patch touches game state.
- [ ] Patches do not mutate strategic state; they only read `StrategicCoordinator.Instance.FiscalIntents`.
- [ ] Harmony patches catch exceptions and use bounded logging.
- [ ] No game install config files are edited.
- [ ] Final implementation includes build, deploy, and SHA-256 verification before smoke-test claims.

## Execution Options

1. **Subagent-Driven (recommended)** - Dispatch a fresh worker per task, review between tasks, and keep commits small.
2. **Inline Execution** - Execute this plan in the current session with checkpoints after each task.
