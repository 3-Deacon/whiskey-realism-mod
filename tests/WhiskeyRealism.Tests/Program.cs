using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;

static class Program
{
    static int Main()
    {
        var tests = new (string name, Action run)[]
        {
            ("critical understrength sector holds", CriticalUnderstrengthSectorHolds),
            ("noncritical understrength sector is economy of force", NoncriticalUnderstrengthSectorEconomyOfForce),
            ("hold source blocks transfer", HoldSourceBlocksTransfer),
            ("economy source allows concession transfer", EconomySourceAllowsConcessionTransfer),
            ("historical registry maps ANV to Virginia corridor", HistoricalRegistryMapsAnv),
            ("historical registry maps Union Tennessee army to Mississippi river corridor", HistoricalRegistryMapsUnionArmyOfTheTennessee),
            ("army area ledger holds historical area", ArmyAreaLedgerHoldsHistoricalArea),
            ("army area ledger redirects out of area army to historical corridor", ArmyAreaLedgerRedirectsOutOfAreaArmy),
            ("weekly cadence fires on first seen week and week rollover only", WeeklyCadenceFiresOnFirstSeenWeekAndRollover),
            ("army group doctrine requires two committed formations", ArmyGroupDoctrineRequiresTwoCommittedFormations),
            ("army group doctrine exposes historical commander preference", ArmyGroupDoctrineExposesHistoricalCommanderPreference),
            ("union early profile favors blockade and river control", UnionEarlyProfileFavorsBlockadeAndRiver),
            ("csa early profile favors capital defense and foreign recognition", CsaEarlyProfileFavorsDefenseAndForeignRecognition),
            ("grand strategy tags affect objective score", GrandStrategyTagsAffectObjectiveScore),
            ("project scorer replaces weak vanilla candidate", ProjectScorerReplacesWeakCandidate),
            ("project scorer keeps close vanilla candidate", ProjectScorerKeepsCloseCandidate),
            ("project scorer requires margin for empty vanilla slot", ProjectScorerRequiresMarginForEmptyVanillaSlot)
        };

        foreach (var test in tests)
        {
            test.run();
            Console.WriteLine("PASS " + test.name);
        }

        return 0;
    }

    private static FrontSectorLedger BuildLedger()
    {
        return FrontSectorLedger.Build(new[]
        {
            new FrontSectorInput
            {
                SectorKey = "Richmond",
                Theater = Theater.East,
                OwnStrength = 9000f,
                EnemyStrength = 12000f,
                StrategicImportance = 1.0f,
                IsCritical = true,
                IsPlanTarget = false,
                CommanderAudacity = 0.2f,
                CommanderCaution = 0.4f,
                AverageMorale = 0.75f,
                AverageSupply = 0.8f,
                AverageReadiness = 0.8f
            },
            new FrontSectorInput
            {
                SectorKey = "Coast",
                Theater = Theater.Coast,
                OwnStrength = 6000f,
                EnemyStrength = 9000f,
                StrategicImportance = 0.35f,
                IsCritical = false,
                IsPlanTarget = false,
                CommanderAudacity = 0.1f,
                CommanderCaution = 0.2f,
                AverageMorale = 0.7f,
                AverageSupply = 0.7f,
                AverageReadiness = 0.7f
            },
            new FrontSectorInput
            {
                SectorKey = "Vicksburg",
                Theater = Theater.River,
                OwnStrength = 18000f,
                EnemyStrength = 10000f,
                StrategicImportance = 0.9f,
                IsCritical = true,
                IsPlanTarget = true,
                CommanderAudacity = 0.5f,
                CommanderCaution = 0.1f,
                AverageMorale = 0.85f,
                AverageSupply = 0.85f,
                AverageReadiness = 0.85f
            }
        }, new FrontLedgerOptions
        {
            MinimumHoldRatio = 0.9f,
            CriticalHoldRatioBonus = 0.25f,
            ConcessionRatio = 0.75f,
            ExploitRatio = 1.25f
        });
    }

    private static void CriticalUnderstrengthSectorHolds()
    {
        var ledger = BuildLedger();
        AssertEqual(FrontPosture.Hold, ledger.GetSector("Richmond").Posture);
    }

    private static void NoncriticalUnderstrengthSectorEconomyOfForce()
    {
        var ledger = BuildLedger();
        AssertEqual(FrontPosture.EconomyOfForce, ledger.GetSector("Coast").Posture);
    }

    private static void HoldSourceBlocksTransfer()
    {
        var ledger = BuildLedger();
        var decision = ledger.EvaluateTransfer("Richmond", "Vicksburg", 2000f);
        AssertEqual(false, decision.Allowed);
        AssertEqual(TransferBudgetAction.Blocked, decision.Action);
    }

    private static void EconomySourceAllowsConcessionTransfer()
    {
        var ledger = BuildLedger();
        var decision = ledger.EvaluateTransfer("Coast", "Vicksburg", 1000f);
        AssertEqual(true, decision.Allowed);
        AssertEqual(TransferBudgetAction.Concession, decision.Action);
    }

    private static void HistoricalRegistryMapsAnv()
    {
        var doctrine = HistoricalArmyAreaRegistry.Resolve(1, "Army of Northern Virginia", "Lee");
        AssertEqual("VirginiaCapitalCorridor", doctrine.PrimaryAreaKey);
        AssertTrue(doctrine.PreferredAreaKeys.Contains("ShenandoahValley"), "expected ShenandoahValley preference");
    }

    private static void HistoricalRegistryMapsUnionArmyOfTheTennessee()
    {
        var doctrine = HistoricalArmyAreaRegistry.Resolve(0, "Army of the Tennessee", "Grant");
        AssertEqual("MississippiRiverCorridor", doctrine.PrimaryAreaKey);
        AssertTrue(doctrine.PreferredAreaKeys.Contains("TennesseeGeorgiaCorridor"), "expected TennesseeGeorgiaCorridor preference");
    }

    private static void ArmyAreaLedgerHoldsHistoricalArea()
    {
        var ledger = ArmyAreaLedger.Build(new[]
        {
            new ArmyAreaInput
            {
                UnitKey = "anv",
                AllianceId = 1,
                UnitName = "Army of Northern Virginia",
                CommanderName = "Lee",
                CurrentAreaKey = "VirginiaCapitalCorridor",
                Strength = 45000f,
                Readiness = 0.8f
            }
        }, planTargetAreaKey: "VirginiaCapitalCorridor");

        var assignment = ledger.GetAssignment("anv");
        AssertEqual("VirginiaCapitalCorridor", assignment.AssignedAreaKey);
        AssertEqual(ArmyAreaBehavior.Hold, assignment.Behavior);
        AssertEqual(false, assignment.OutOfArea);
    }

    private static void ArmyAreaLedgerRedirectsOutOfAreaArmy()
    {
        var ledger = ArmyAreaLedger.Build(new[]
        {
            new ArmyAreaInput
            {
                UnitKey = "aot",
                AllianceId = 1,
                UnitName = "Army of Tennessee",
                CommanderName = "Johnston",
                CurrentAreaKey = "VirginiaCapitalCorridor",
                Strength = 30000f,
                Readiness = 0.75f
            }
        }, planTargetAreaKey: "VirginiaCapitalCorridor");

        var assignment = ledger.GetAssignment("aot");
        AssertEqual("TennesseeGeorgiaCorridor", assignment.AssignedAreaKey);
        AssertEqual(ArmyAreaBehavior.Recover, assignment.Behavior);
        AssertEqual(true, assignment.OutOfArea);
    }

    private static void WeeklyCadenceFiresOnFirstSeenWeekAndRollover()
    {
        var cadence = new WeeklyCadence();

        AssertEqual(true, cadence.ShouldFire(1, 6, 1861));
        AssertEqual(false, cadence.ShouldFire(6, 6, 1861));
        AssertEqual(true, cadence.ShouldFire(8, 6, 1861));
        AssertEqual(false, cadence.ShouldFire(13, 6, 1861));
        AssertEqual(true, cadence.ShouldFire(1, 7, 1861));
    }

    private static void ArmyGroupDoctrineRequiresTwoCommittedFormations()
    {
        var ledger = ArmyAreaLedger.Build(new[]
        {
            new ArmyAreaInput
            {
                UnitKey = "anv-main",
                AllianceId = 1,
                UnitName = "Army of Northern Virginia",
                CurrentAreaKey = "VirginiaCapitalCorridor",
                Strength = 42000f,
                Readiness = 0.8f
            },
            new ArmyAreaInput
            {
                UnitKey = "valley",
                AllianceId = 1,
                UnitName = "Army of the Valley",
                CurrentAreaKey = "ShenandoahValley",
                Strength = 9000f,
                Readiness = 0.7f
            },
            new ArmyAreaInput
            {
                UnitKey = "recovering-west",
                AllianceId = 1,
                UnitName = "Army of Tennessee",
                CurrentAreaKey = "VirginiaCapitalCorridor",
                Strength = 22000f,
                Readiness = 0.7f
            }
        });

        var groups = ArmyGroupDoctrine.PlanGroups(ledger, minimumUnitsPerGroup: 2);

        AssertEqual(1, groups.Count);
        AssertEqual("VirginiaCapitalCorridor", groups[0].AreaKey);
        AssertEqual(2, groups[0].UnitKeys.Count);
    }

    private static void ArmyGroupDoctrineExposesHistoricalCommanderPreference()
    {
        var preference = ArmyGroupDoctrine.ResolveCommanderPreference(1, "VirginiaCapitalCorridor");

        AssertEqual("Lee", preference.PreferredLastNames[0]);
        AssertTrue(preference.PreferredLastNames.Contains("Johnston"), "expected Johnston fallback");
    }

    private static void UnionEarlyProfileFavorsBlockadeAndRiver()
    {
        var profile = GrandStrategyRegistry.Resolve(0, EraStage.Amateur1861);

        AssertEqual("Union Early Anaconda", profile.Name);
        AssertTrue(profile.WeightFor(StrategyTag.Blockade) > profile.WeightFor(StrategyTag.CapitalDefense),
            "Union early should prioritize blockade over capital defense");
        AssertTrue(profile.WeightFor(StrategyTag.RiverControl) > 0.9f,
            "Union early should strongly weight river control");
    }

    private static void CsaEarlyProfileFavorsDefenseAndForeignRecognition()
    {
        var profile = GrandStrategyRegistry.Resolve(1, EraStage.Amateur1861);

        AssertEqual("CSA Early Cordon", profile.Name);
        AssertTrue(profile.WeightFor(StrategyTag.CapitalDefense) > profile.WeightFor(StrategyTag.Blockade),
            "CSA early should prioritize capital defense over blockade");
        AssertTrue(profile.WeightFor(StrategyTag.ForeignRecognition) > 0.9f,
            "CSA early should strongly weight foreign recognition");
    }

    private static void GrandStrategyTagsAffectObjectiveScore()
    {
        var profile = GrandStrategyRegistry.Resolve(0, EraStage.Amateur1861);
        var p = new PersonalityVector(0f, 0f, 0f, 0f, 0f);

        var blockade = ObjectiveMetadata.DefaultDerived(Theater.Coast, 900f, -200f)
            .WithTag(StrategyTag.Blockade)
            .WithTag(StrategyTag.PortAccess);
        var capital = ObjectiveMetadata.DefaultDerived(Theater.East, 700f, 100f)
            .WithTag(StrategyTag.CapitalDefense);

        AssertTrue(
            ObjectiveScoring.Score(0, p, profile, blockade) > ObjectiveScoring.Score(0, p, profile, capital),
            "Union early strategy tags should lift blockade objectives above capital defense");

        var tagged = ObjectiveStrategyTagger.ApplyDefaultTags(
            ObjectiveMetadata.DefaultDerived(Theater.Coast, 900f, -200f));

        AssertTrue(tagged.HasTag(StrategyTag.Blockade), "expected coast fallback to add Blockade");
        AssertTrue(tagged.HasTag(StrategyTag.PortAccess), "expected coast fallback to add PortAccess");
    }

    private static void ProjectScorerReplacesWeakCandidate()
    {
        var profile = GrandStrategyRegistry.Resolve(0, EraStage.Amateur1861);
        var candidates = new[]
        {
            new ProjectCandidateInput { ProjectId = 41, SubsidyType = 5, VanillaWeight = 0.2f },
            new ProjectCandidateInput { ProjectId = 96, SubsidyType = 5, VanillaWeight = 0.6f }
        };

        var decision = ProjectSelectionScorer.Select(profile, subsidyType: 5, vanillaProjectId: 96, vanillaWeight: 0.6f, candidates);

        AssertEqual(true, decision.ShouldReplace);
        AssertEqual(41, decision.ProjectId);
        AssertEqual("strategy-margin", decision.Reason);
    }

    private static void ProjectScorerKeepsCloseCandidate()
    {
        var profile = GrandStrategyRegistry.Resolve(1, EraStage.Amateur1861);
        var candidates = new[]
        {
            new ProjectCandidateInput { ProjectId = 1, SubsidyType = 5, VanillaWeight = 1.0f },
            new ProjectCandidateInput { ProjectId = 6, SubsidyType = 5, VanillaWeight = 0.9f }
        };

        var decision = ProjectSelectionScorer.Select(profile, subsidyType: 5, vanillaProjectId: 1, vanillaWeight: 1.0f, candidates);

        AssertEqual(false, decision.ShouldReplace);
        AssertEqual(1, decision.ProjectId);
    }

    private static void ProjectScorerRequiresMarginForEmptyVanillaSlot()
    {
        var profile = GrandStrategyRegistry.Resolve(0, EraStage.Amateur1861);
        var candidates = new[]
        {
            new ProjectCandidateInput { ProjectId = 96, SubsidyType = 5, VanillaWeight = 0.1f }
        };

        var decision = ProjectSelectionScorer.Select(profile, subsidyType: 5, vanillaProjectId: -1, vanillaWeight: 0f, candidates);

        AssertEqual(false, decision.ShouldReplace);
        AssertEqual(-1, decision.ProjectId);

        var strongCandidates = new[]
        {
            new ProjectCandidateInput { ProjectId = 41, SubsidyType = 5, VanillaWeight = 0.2f }
        };

        var strongDecision = ProjectSelectionScorer.Select(profile, subsidyType: 5, vanillaProjectId: -1, vanillaWeight: 0f, strongCandidates);

        AssertEqual(true, strongDecision.ShouldReplace);
        AssertEqual(41, strongDecision.ProjectId);
        AssertEqual("vanilla-empty-strategy-margin", strongDecision.Reason);
    }

    private static void AssertEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception("expected " + expected + " but got " + actual);
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
