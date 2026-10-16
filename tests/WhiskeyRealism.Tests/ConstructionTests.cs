using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WhiskeyRealism;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.PlayerOrders;
using WhiskeyRealism.Util;

using static TestFixtures;
using static TestHarness;

internal static class ConstructionTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("project lane intent estimates days from observed rate", ProjectLaneIntentEstimatesDaysFromObservedRate),
            ("construction scorer favors csa banks in balanced posture", ConstructionScorerFavorsCsaBanks),
            ("construction scorer favors logistics when supply is protected", ConstructionScorerFavorsLogistics),
            ("construction scorer suppresses csa naval under credit defense", ConstructionScorerSuppressesCsaNaval),
            ("construction scorer floors emergency industrial suppression", ConstructionScorerFloorsEmergencyIndustry),
            ("construction ledger chooses field supply from low-supply pressure", ConstructionLedgerChoosesFieldSupply),
            ("construction ledger allows csa early arms stress", ConstructionLedgerAllowsCsaEarlyArmsStress),
            ("construction ledger allows emergency csa arms away from bond floor", ConstructionLedgerAllowsEmergencyCsaArmsAwayFromBondFloor),
            ("construction ledger suppresses csa rail by doctrine", ConstructionLedgerSuppressesCsaRailByDoctrine),
            ("construction ledger makes emergency hold strict near bond floor", ConstructionLedgerEmergencyHoldNearBondFloor),
            ("construction ledger signature changes on top candidate", ConstructionLedgerSignatureChangesOnTopCandidate),
            ("construction ledger handles null input", ConstructionLedgerHandlesNullInput),
            ("construction ledger keeps credit-defense bank", ConstructionLedgerKeepsCreditDefenseBank),
            ("construction ledger suppresses union arms under credit defense", ConstructionLedgerSuppressesUnionArmsUnderCreditDefense),
            ("construction ledger suppresses late csa arms under credit defense", ConstructionLedgerSuppressesLateCsaArmsUnderCreditDefense),
            ("fort governor suppresses saturated low-threat local area", FortGovernorSuppressesSaturatedLowThreatLocalArea),
            ("fort governor allows threatened capital area up to hard cap", FortGovernorAllowsThreatenedCapitalAreaUpToHardCap),
            ("fort governor blocks capital area at hard cap", FortGovernorBlocksCapitalAreaAtHardCap),
            ("construction steering boosts ledger top private candidate", ConstructionSteeringCapsTopPrivateCandidate),
            ("construction steering suppresses ledger-suppressed candidate", ConstructionSteeringSuppressesSuppressedCandidate),
            ("construction steering preserves fiscal multiplier when no intent", ConstructionSteeringPreservesFiscalWhenNoIntent),
            ("construction steering treats nan top score as neutral floor", ConstructionSteeringTreatsNanTopScoreAsNeutralFloor),
            ("construction steering treats nan fiscal multiplier as neutral", ConstructionSteeringTreatsNanFiscalMultiplierAsNeutral),
            ("construction steering treats infinite top scores as neutral floor", ConstructionSteeringTreatsInfiniteTopScoresAsNeutralFloor),
            ("construction steering treats infinite fiscal multipliers as neutral", ConstructionSteeringTreatsInfiniteFiscalMultipliersAsNeutral),
            ("construction steering suppresses id-zero bank by type", ConstructionSteeringSuppressesIdZeroBankByType),
            ("construction steering ignores same-name id-zero suppression for different type", ConstructionSteeringIgnoresSameNameIdZeroSuppressionForDifferentType),
            ("construction steering uses missing suppression id name fallback", ConstructionSteeringUsesMissingSuppressionIdNameFallback),
            ("construction steering ignores same-name suppression with different type", ConstructionSteeringIgnoresSameNameSuppressionWithDifferentType),
            ("construction steering leaves non-top field-supply bank fiscal-only", ConstructionSteeringLeavesNonTopFieldSupplyBankFiscalOnly),
            ("construction probability sanitizer treats zero as normal skip", ConstructionProbabilitySanitizerTreatsZeroAsNormalSkip),
            ("telegraph intent rejects disconnected candidates", TelegraphIntentRejectsDisconnectedCandidates),
            ("telegraph intent rejects candidates without supporting unit", TelegraphIntentRejectsNoSupportingUnit),
            ("telegraph intent rejects unsafe corridor", TelegraphIntentRejectsUnsafeCorridor),
            ("telegraph intent rejects already covered candidate", TelegraphIntentRejectsAlreadyCoveredCandidate),
            ("telegraph intent favors active command corridor", TelegraphIntentFavorsActiveCommandCorridor),
            ("telegraph intent suppresses emergency noncritical build", TelegraphIntentSuppressesEmergencyNoncriticalBuild),
            ("telegraph intent treats nonfinite inputs as no pressure", TelegraphIntentTreatsNonfiniteInputsAsNoPressure),
            ("telegraph intent builds at exact threshold", TelegraphIntentBuildsAtExactThreshold),
            ("telegraph intent rejects noncorridor high pressure", TelegraphIntentRejectsNoncorridorHighPressure),
        });
    }

    private static void ProjectLaneIntentEstimatesDaysFromObservedRate()
    {
        var signals = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignals
        {
            Alliance = 0,
            Era = EraStage.Amateur1861,
            FiscalPosture = FiscalPosture.BalancedWar
        };

        var decision = WhiskeyRealism.Strategic.Projects.ProjectDoctrineScorer.Select(
            GrandStrategyRegistry.Resolve(0, EraStage.Amateur1861),
            signals,
            subsidyType: 4,
            vanillaProjectId: 100,
            vanillaWeight: 1f,
            candidates: new ProjectCandidateInput[0],
            fiscalWeight: null,
            runtimeFacts: id => new WhiskeyRealism.Strategic.Projects.ProjectRuntimeFacts { ProjectId = id, SubsidyLane = 4, Cost = 1000f },
            fundingAvailable: 250f,
            netFundingPerDay: 25f,
            constructionCurrentlyWins: true);

        AssertEqual(30f, decision.LaneIntent.TimeToFundEstimateDays);
        AssertEqual(true, decision.LaneIntent.ConstructionCurrentlyWins);
    }

    private static void ConstructionScorerFavorsCsaBanks()
    {
        var intent = new FiscalOutput { Posture = FiscalPosture.BalancedWar };
        AssertEqual(1.6f, FiscalConstructionScorer.Multiplier(intent, 1, "State Bank", 0));
        AssertEqual(1.35f, FiscalConstructionScorer.Multiplier(intent, 0, "National Bank", 0));
    }

    private static void ConstructionScorerFavorsLogistics()
    {
        var intent = new FiscalOutput
        {
            Posture = FiscalPosture.CreditDefense,
            SupplyProtection = true
        };

        AssertEqual(1.75f, FiscalConstructionScorer.Multiplier(intent, 1, "Rail Depot", -1));
        AssertEqual(1.35f, FiscalConstructionScorer.Multiplier(intent, 1, "Hospital", -1));
    }

    private static void ConstructionScorerSuppressesCsaNaval()
    {
        var intent = new FiscalOutput { Posture = FiscalPosture.CreditDefense };
        AssertEqual(0.5f, FiscalConstructionScorer.Multiplier(intent, 1, "Naval Shipyard", 2));
        AssertEqual(1f, FiscalConstructionScorer.Multiplier(intent, 0, "Naval Shipyard", 2));
    }

    private static void ConstructionScorerFloorsEmergencyIndustry()
    {
        var intent = new FiscalOutput { Posture = FiscalPosture.EmergencySolvency };
        AssertEqual(0.15f, FiscalConstructionScorer.Multiplier(intent, 1, "Naval Industrial Shipyard Foundry", 3));
    }

    private static ConstructionInput BaseConstructionInput(int alliance)
    {
        return new ConstructionInput
        {
            AllianceId = alliance,
            FiscalPosture = FiscalPosture.BalancedWar,
            CurrentRating = 3,
            BondFloorRating = 11,
            SupplyProtection = false,
            LogisticsExpansion = false,
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

    private static void ConstructionLedgerAllowsEmergencyCsaArmsAwayFromBondFloor()
    {
        var input = BaseConstructionInput(1);
        input.FiscalPosture = FiscalPosture.EmergencySolvency;
        input.CurrentRating = 7;
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

        var output = ConstructionIntentLedger.Compute(input, new ConstructionOptions());

        AssertEqual(ConstructionPosture.EmergencyHold, output.Posture);
        AssertEqual(10, output.TopPrivateBuilding.BuildingTypeId);
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

    private static void ConstructionLedgerHandlesNullInput()
    {
        var output = ConstructionIntentLedger.Compute(null, null);

        AssertEqual(ConstructionPosture.Infrastructure, output.Posture);
        AssertEqual(ConstructionCandidate.None.Name, output.TopPrivateBuilding.Name);
        AssertEqual(0, output.Suppressions.Length);
    }

    private static void ConstructionLedgerKeepsCreditDefenseBank()
    {
        var input = BaseConstructionInput(1);
        input.FiscalPosture = FiscalPosture.CreditDefense;
        input.Candidates.Add(new ConstructionCandidate
        {
            Kind = ConstructionCandidateKind.PrivateBuilding,
            BuildingTypeId = 2,
            Name = "State Bank",
            Theater = Theater.East,
            VanillaValid = true
        });

        var output = ConstructionIntentLedger.Compute(input, new ConstructionOptions());

        AssertEqual(2, output.TopPrivateBuilding.BuildingTypeId);
        AssertEqual(0, output.Suppressions.Length);
    }

    private static void ConstructionLedgerSuppressesUnionArmsUnderCreditDefense()
    {
        var input = BaseConstructionInput(0);
        input.FiscalPosture = FiscalPosture.CreditDefense;
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

        var output = ConstructionIntentLedger.Compute(input, new ConstructionOptions());

        AssertEqual(ConstructionCandidate.None.Name, output.TopPrivateBuilding.Name);
        AssertEqual(ConstructionSuppressionReason.DiscretionaryIndustryCreditDefense, output.Suppressions[0].Reason);
    }

    private static void ConstructionLedgerSuppressesLateCsaArmsUnderCreditDefense()
    {
        var input = BaseConstructionInput(1);
        input.FiscalPosture = FiscalPosture.CreditDefense;
        input.CurrentYear = 1864;
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

        var output = ConstructionIntentLedger.Compute(input, new ConstructionOptions());

        AssertEqual(ConstructionCandidate.None.Name, output.TopPrivateBuilding.Name);
        AssertEqual(ConstructionSuppressionReason.DiscretionaryIndustryCreditDefense, output.Suppressions[0].Reason);
    }

    private static void FortGovernorSuppressesSaturatedLowThreatLocalArea()
    {
        var decision = FortConstructionGovernor.Decide(new FortConstructionSiteContext
        {
            ExistingFortCount = 2,
            ActiveOrderCount = 0,
            NearCapital = false,
            ThreatRatio = 0.1f
        });

        AssertEqual(false, decision.Allow);
        AssertEqual("saturated-low-threat", decision.Reason);
        AssertEqual(2, decision.SoftCap);
    }

    private static void FortGovernorAllowsThreatenedCapitalAreaUpToHardCap()
    {
        var decision = FortConstructionGovernor.Decide(new FortConstructionSiteContext
        {
            ExistingFortCount = 4,
            ActiveOrderCount = 1,
            NearCapital = true,
            ThreatRatio = 0.6f
        });

        AssertEqual(true, decision.Allow);
        AssertEqual("allowed", decision.Reason);
        AssertEqual(4, decision.SoftCap);
        AssertEqual(7, decision.HardCap);
    }

    private static void FortGovernorBlocksCapitalAreaAtHardCap()
    {
        var decision = FortConstructionGovernor.Decide(new FortConstructionSiteContext
        {
            ExistingFortCount = 6,
            ActiveOrderCount = 1,
            NearCapital = true,
            ThreatRatio = 1.2f
        });

        AssertEqual(false, decision.Allow);
        AssertEqual("hard-cap", decision.Reason);
    }

    private static void ConstructionSteeringCapsTopPrivateCandidate()
    {
        var output = new ConstructionOutput
        {
            Posture = ConstructionPosture.FieldSupply,
            TopPrivateBuilding = new ConstructionCandidate
            {
                Kind = ConstructionCandidateKind.PrivateBuilding,
                BuildingTypeId = 13,
                Name = "Market",
                Score = 4f,
                VanillaValid = true
            }
        };

        var decision = ConstructionSteeringScorer.DecidePrivateMultiplier(
            output,
            buildingTypeId: 13,
            buildingName: "Market",
            fiscalMultiplier: 1.5f);

        AssertEqual(3f, decision.Multiplier);
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
                    BuildingTypeId = 5,
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

    private static void ConstructionSteeringTreatsNanTopScoreAsNeutralFloor()
    {
        var output = new ConstructionOutput
        {
            Posture = ConstructionPosture.FieldSupply,
            TopPrivateBuilding = new ConstructionCandidate
            {
                Kind = ConstructionCandidateKind.PrivateBuilding,
                BuildingTypeId = 13,
                Name = "Market",
                Score = float.NaN,
                VanillaValid = true
            }
        };

        var decision = ConstructionSteeringScorer.DecidePrivateMultiplier(
            output,
            buildingTypeId: 13,
            buildingName: "Market",
            fiscalMultiplier: 1.1f);

        AssertEqual(1.375f, decision.Multiplier);
        AssertEqual("ledger-top-private", decision.Reason);
    }

    private static void ConstructionSteeringTreatsNanFiscalMultiplierAsNeutral()
    {
        var decision = ConstructionSteeringScorer.DecidePrivateMultiplier(
            output: null,
            buildingTypeId: 13,
            buildingName: "Market",
            fiscalMultiplier: float.NaN);

        AssertEqual(1f, decision.Multiplier);
        AssertEqual("fiscal-only", decision.Reason);
    }

    private static void ConstructionSteeringTreatsInfiniteTopScoresAsNeutralFloor()
    {
        var output = new ConstructionOutput
        {
            Posture = ConstructionPosture.FieldSupply,
            TopPrivateBuilding = new ConstructionCandidate
            {
                Kind = ConstructionCandidateKind.PrivateBuilding,
                BuildingTypeId = 13,
                Name = "Market",
                VanillaValid = true
            }
        };

        output.TopPrivateBuilding.Score = float.PositiveInfinity;
        var positive = ConstructionSteeringScorer.DecidePrivateMultiplier(
            output,
            buildingTypeId: 13,
            buildingName: "Market",
            fiscalMultiplier: 1.1f);

        output.TopPrivateBuilding.Score = float.NegativeInfinity;
        var negative = ConstructionSteeringScorer.DecidePrivateMultiplier(
            output,
            buildingTypeId: 13,
            buildingName: "Market",
            fiscalMultiplier: 1.1f);

        AssertEqual(1.375f, positive.Multiplier);
        AssertEqual("ledger-top-private", positive.Reason);
        AssertEqual(1.375f, negative.Multiplier);
        AssertEqual("ledger-top-private", negative.Reason);
    }

    private static void ConstructionSteeringTreatsInfiniteFiscalMultipliersAsNeutral()
    {
        var positive = ConstructionSteeringScorer.DecidePrivateMultiplier(
            output: null,
            buildingTypeId: 13,
            buildingName: "Market",
            fiscalMultiplier: float.PositiveInfinity);
        var negative = ConstructionSteeringScorer.DecidePrivateMultiplier(
            output: null,
            buildingTypeId: 13,
            buildingName: "Market",
            fiscalMultiplier: float.NegativeInfinity);

        AssertEqual(1f, positive.Multiplier);
        AssertEqual("fiscal-only", positive.Reason);
        AssertEqual(1f, negative.Multiplier);
        AssertEqual("fiscal-only", negative.Reason);
    }

    private static void ConstructionSteeringSuppressesIdZeroBankByType()
    {
        var output = new ConstructionOutput
        {
            Posture = ConstructionPosture.Infrastructure,
            Suppressions = new[]
            {
                new ConstructionSuppression
                {
                    Kind = ConstructionCandidateKind.PrivateBuilding,
                    BuildingTypeId = 0,
                    Name = "Bank",
                    Reason = ConstructionSuppressionReason.EmergencyCreditFloor
                }
            }
        };

        var decision = ConstructionSteeringScorer.DecidePrivateMultiplier(
            output,
            buildingTypeId: 0,
            buildingName: "Bank",
            fiscalMultiplier: 1.5f);

        AssertEqual(0.1f, decision.Multiplier);
        AssertEqual("suppressed:EmergencyCreditFloor", decision.Reason);
    }

    private static void ConstructionSteeringIgnoresSameNameIdZeroSuppressionForDifferentType()
    {
        var output = new ConstructionOutput
        {
            Posture = ConstructionPosture.Infrastructure,
            Suppressions = new[]
            {
                new ConstructionSuppression
                {
                    Kind = ConstructionCandidateKind.PrivateBuilding,
                    BuildingTypeId = 0,
                    Name = "Bank",
                    Reason = ConstructionSuppressionReason.EmergencyCreditFloor
                }
            }
        };

        var decision = ConstructionSteeringScorer.DecidePrivateMultiplier(
            output,
            buildingTypeId: 1,
            buildingName: "Bank",
            fiscalMultiplier: 1.2f);

        AssertEqual(1.2f, decision.Multiplier);
        AssertEqual("fiscal-ledger-neutral", decision.Reason);
    }

    private static void ConstructionSteeringUsesMissingSuppressionIdNameFallback()
    {
        var output = new ConstructionOutput
        {
            Posture = ConstructionPosture.Infrastructure,
            Suppressions = new[]
            {
                new ConstructionSuppression
                {
                    Kind = ConstructionCandidateKind.PrivateBuilding,
                    BuildingTypeId = ConstructionSuppression.MissingBuildingTypeId,
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

    private static void ConstructionSteeringIgnoresSameNameSuppressionWithDifferentType()
    {
        var output = new ConstructionOutput
        {
            Posture = ConstructionPosture.Infrastructure,
            Suppressions = new[]
            {
                new ConstructionSuppression
                {
                    Kind = ConstructionCandidateKind.PrivateBuilding,
                    BuildingTypeId = 6,
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

        AssertEqual(1.2f, decision.Multiplier);
        AssertEqual("fiscal-ledger-neutral", decision.Reason);
    }

    private static void ConstructionSteeringLeavesNonTopFieldSupplyBankFiscalOnly()
    {
        var output = new ConstructionOutput
        {
            Posture = ConstructionPosture.FieldSupply
        };

        var decision = ConstructionSteeringScorer.DecidePrivateMultiplier(
            output,
            buildingTypeId: 0,
            buildingName: "State Bank",
            fiscalMultiplier: 1.35f);

        AssertEqual(1.35f, decision.Multiplier);
        AssertEqual("fiscal-ledger-neutral", decision.Reason);
    }

    private static void ConstructionProbabilitySanitizerTreatsZeroAsNormalSkip()
    {
        AssertEqual(ConstructionProbabilityStatus.Valid, ConstructionProbabilitySanitizer.Classify(0.25f));
        AssertEqual(ConstructionProbabilityStatus.Skip, ConstructionProbabilitySanitizer.Classify(0f));
        AssertEqual(ConstructionProbabilityStatus.Skip, ConstructionProbabilitySanitizer.Classify(-0.01f));
        AssertEqual(ConstructionProbabilityStatus.Invalid, ConstructionProbabilitySanitizer.Classify(float.NaN));
        AssertEqual(ConstructionProbabilityStatus.Invalid, ConstructionProbabilitySanitizer.Classify(float.PositiveInfinity));
    }

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

    private static void TelegraphIntentRejectsNoSupportingUnit()
    {
        var candidate = new TelegraphCandidateFacts
        {
            ConnectedToCapitalOrChain = true,
            SupportingUnitEligible = false,
            SupportsActiveCommandCorridor = true,
            SafeRear = true,
            CommandDelayPressure = 1f,
            FormationImportance = 1f
        };

        var decision = TelegraphIntentScorer.Score(candidate, ConstructionPosture.FieldSupply);

        AssertEqual(false, decision.ShouldBuild);
        AssertEqual(0f, decision.Score);
        AssertEqual("no-supporting-unit", decision.Reason);
    }

    private static void TelegraphIntentRejectsUnsafeCorridor()
    {
        var candidate = new TelegraphCandidateFacts
        {
            ConnectedToCapitalOrChain = true,
            SupportingUnitEligible = true,
            SupportsActiveCommandCorridor = true,
            SafeRear = false,
            CommandDelayPressure = 1f,
            FormationImportance = 1f
        };

        var decision = TelegraphIntentScorer.Score(candidate, ConstructionPosture.FieldSupply);

        AssertEqual(false, decision.ShouldBuild);
        AssertEqual(0f, decision.Score);
        AssertEqual("unsafe-corridor", decision.Reason);
    }

    private static void TelegraphIntentRejectsAlreadyCoveredCandidate()
    {
        var candidate = new TelegraphCandidateFacts
        {
            ConnectedToCapitalOrChain = true,
            SupportingUnitEligible = true,
            SupportsActiveCommandCorridor = true,
            SafeRear = true,
            AlreadyCoveredByTelegraph = true,
            CommandDelayPressure = 1f,
            FormationImportance = 1f
        };

        var decision = TelegraphIntentScorer.Score(candidate, ConstructionPosture.FieldSupply);

        AssertEqual(false, decision.ShouldBuild);
        AssertEqual(0f, decision.Score);
        AssertEqual("already-covered", decision.Reason);
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

    private static void TelegraphIntentTreatsNonfiniteInputsAsNoPressure()
    {
        var nanPressure = new TelegraphCandidateFacts
        {
            ConnectedToCapitalOrChain = true,
            SupportingUnitEligible = true,
            SupportsActiveCommandCorridor = true,
            SafeRear = true,
            CommandDelayPressure = float.NaN,
            FormationImportance = float.PositiveInfinity
        };

        var nanDecision = TelegraphIntentScorer.Score(nanPressure, ConstructionPosture.Infrastructure);

        AssertEqual(false, nanDecision.ShouldBuild);
        AssertTrue(!float.IsNaN(nanDecision.Score), "expected NaN inputs to produce a finite telegraph score");
        AssertTrue(!float.IsInfinity(nanDecision.Score), "expected infinite inputs to produce a finite telegraph score");
        AssertEqual("below-threshold", nanDecision.Reason);

        var infinitePressureOnly = new TelegraphCandidateFacts
        {
            ConnectedToCapitalOrChain = true,
            SupportingUnitEligible = true,
            SupportsActiveCommandCorridor = false,
            SafeRear = true,
            CommandDelayPressure = float.PositiveInfinity,
            FormationImportance = float.NegativeInfinity
        };

        var infiniteDecision = TelegraphIntentScorer.Score(infinitePressureOnly, ConstructionPosture.FieldSupply);

        AssertEqual(false, infiniteDecision.ShouldBuild);
        AssertTrue(!float.IsNaN(infiniteDecision.Score), "expected infinite pressure to produce a finite telegraph score");
        AssertTrue(!float.IsInfinity(infiniteDecision.Score), "expected infinite pressure to produce a finite telegraph score");
        AssertEqual("below-threshold", infiniteDecision.Reason);
    }

    private static void TelegraphIntentBuildsAtExactThreshold()
    {
        var candidate = new TelegraphCandidateFacts
        {
            ConnectedToCapitalOrChain = true,
            SupportingUnitEligible = true,
            SupportsActiveCommandCorridor = true,
            SafeRear = true,
            CommandDelayPressure = 2f / 3f,
            FormationImportance = 0f
        };

        var decision = TelegraphIntentScorer.Score(candidate, ConstructionPosture.Infrastructure);

        AssertEqual(true, decision.ShouldBuild);
        AssertEqual(1.0f, decision.Score);
        AssertEqual("active-command-corridor", decision.Reason);
    }

    private static void TelegraphIntentRejectsNoncorridorHighPressure()
    {
        var candidate = new TelegraphCandidateFacts
        {
            ConnectedToCapitalOrChain = true,
            SupportingUnitEligible = true,
            SupportsActiveCommandCorridor = false,
            SafeRear = true,
            CommandDelayPressure = 1f,
            FormationImportance = 1f
        };

        var decision = TelegraphIntentScorer.Score(candidate, ConstructionPosture.FieldSupply);

        AssertEqual(false, decision.ShouldBuild);
        AssertEqual("below-threshold", decision.Reason);
    }
}
