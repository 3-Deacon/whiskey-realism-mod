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
            ("project scorer requires margin for empty vanilla slot", ProjectScorerRequiresMarginForEmptyVanillaSlot),
            ("formation level maps vanilla unit types", FormationLevelMapsVanillaUnitTypes),
            ("independent top division requires top unit and strength floor", IndependentTopDivisionRequiresTopAndStrengthFloor),
            ("attached division is not directly controllable", AttachedDivisionIsNotDirectlyControllable),
            ("division refuses enemy army without support", DivisionRefusesEnemyArmyWithoutSupport),
            ("csa coherent outnumbered division delays instead of retreating", CsaCoherentOutnumberedDivisionDelays),
            ("low ammo formation recovers", LowAmmoFormationRecovers),
            ("army masses for plan target when hierarchy exists", ArmyMassesForPlanTargetWhenHierarchyExists),
            ("raid support maps only to cavalry capable formations", RaidSupportMapsOnlyToCavalryCapableFormations),
            ("formation directive summary changes when assignment changes", FormationDirectiveSummaryChangesWhenAssignmentChanges)
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

    private static void FormationLevelMapsVanillaUnitTypes()
    {
        AssertEqual(FormationLevel.Division, FormationSnapshot.LevelFromUnitType(14));
        AssertEqual(FormationLevel.Corps, FormationSnapshot.LevelFromUnitType(15));
        AssertEqual(FormationLevel.Army, FormationSnapshot.LevelFromUnitType(16));
        AssertEqual(FormationLevel.Unknown, FormationSnapshot.LevelFromUnitType(17));
    }

    private static void IndependentTopDivisionRequiresTopAndStrengthFloor()
    {
        var snap = new FormationSnapshot
        {
            UnitKey = "div:1",
            UnitType = 14,
            IsTopUnit = true,
            IsGarrisoned = false,
            GroupStrengthDirect = 1500f
        };

        AssertEqual(true, snap.IsIndependentTopDivision);

        snap.IsTopUnit = false;
        AssertEqual(false, snap.IsIndependentTopDivision);

        snap.IsTopUnit = true;
        snap.GroupStrengthDirect = 999f;
        AssertEqual(false, snap.IsIndependentTopDivision);
    }

    private static void AttachedDivisionIsNotDirectlyControllable()
    {
        var snap = new FormationSnapshot
        {
            UnitKey = "attached:1",
            ParentUnitKey = "corps:1",
            UnitType = 14,
            IsTopUnit = false,
            GroupStrengthDirect = 3000f
        };

        AssertEqual(FormationLevel.Division, snap.Level);
        AssertEqual(true, snap.IsAttachedDivision);
        AssertEqual(false, snap.CanReceiveDirectDirective);
    }

    private static FormationSnapshot Snapshot(
        string key,
        int alliance,
        int unitType,
        float strength,
        float enemy,
        FormationLevel enemyLevel,
        FrontPosture posture)
    {
        return new FormationSnapshot
        {
            UnitKey = key,
            AllianceId = alliance,
            UnitName = key,
            UnitType = unitType,
            IsTopUnit = true,
            GroupStrengthActive = strength,
            GroupStrengthDirect = strength,
            Morale = 0.8f,
            Readiness = 0.8f,
            RifleAmmo = 0.8f,
            ArtilleryAmmo = 0.8f,
            Supply = 0.8f,
            WeaponFirepower = 1.0f,
            AreaKey = "VirginiaCapitalCorridor",
            SectorKey = "Richmond",
            LocalEnemyStrength = enemy,
            VisibleEnemyLevel = enemyLevel,
            FrontPosture = posture
        };
    }

    private static void DivisionRefusesEnemyArmyWithoutSupport()
    {
        var snap = Snapshot("division", 1, 14, 4500f, 50000f, FormationLevel.Army, FrontPosture.Hold);
        var ledger = FormationDirectiveLedger.Build(new[] { snap }, EraStage.Amateur1861, null);
        var assignment = ledger.GetAssignment("division");

        AssertEqual(FormationDirective.Screen, assignment.Directive);
        AssertEqual(false, assignment.OffensiveAllowed);
    }

    private static void CsaCoherentOutnumberedDivisionDelays()
    {
        var snap = Snapshot("csa-delay", 1, 14, 6000f, 14000f, FormationLevel.Corps, FrontPosture.Delay);
        snap.LocalFriendlySupportStrength = 5000f;
        snap.SupportCanReach = true;

        var ledger = FormationDirectiveLedger.Build(new[] { snap }, EraStage.Amateur1861, null);
        var assignment = ledger.GetAssignment("csa-delay");

        AssertEqual(FormationDirective.Delay, assignment.Directive);
        AssertEqual(false, assignment.OffensiveAllowed);
        AssertEqual(true, assignment.DefensiveAllowed);
    }

    private static void LowAmmoFormationRecovers()
    {
        var snap = Snapshot("low-ammo", 0, 15, 16000f, 10000f, FormationLevel.Corps, FrontPosture.Counterstroke);
        snap.RifleAmmo = 0.1f;
        snap.ArtilleryAmmo = 0.2f;

        var ledger = FormationDirectiveLedger.Build(new[] { snap }, EraStage.Operational1862, null);
        var assignment = ledger.GetAssignment("low-ammo");

        AssertEqual(FormationDirective.Recover, assignment.Directive);
        AssertEqual(false, assignment.OffensiveAllowed);
    }

    private static void ArmyMassesForPlanTargetWhenHierarchyExists()
    {
        var snap = Snapshot("army", 0, 16, 50000f, 30000f, FormationLevel.Army, FrontPosture.Exploit);
        snap.GrandArmyStructureAvailable = true;
        snap.IsPlanTargetArea = true;

        var ledger = FormationDirectiveLedger.Build(new[] { snap }, EraStage.TotalWar1864, "VirginiaCapitalCorridor");
        var assignment = ledger.GetAssignment("army");

        AssertEqual(FormationDirective.Mass, assignment.Directive);
        AssertEqual(true, assignment.OffensiveAllowed);
    }

    private static void RaidSupportMapsOnlyToCavalryCapableFormations()
    {
        var cavalry = Snapshot("cav", 1, 14, 2500f, 1000f, FormationLevel.Division, FrontPosture.EconomyOfForce);
        cavalry.IsCavalryCapable = true;
        cavalry.Supply = 0.9f;
        cavalry.Readiness = 0.9f;

        var infantry = Snapshot("inf", 1, 14, 2500f, 1000f, FormationLevel.Division, FrontPosture.EconomyOfForce);
        infantry.IsCavalryCapable = false;

        var ledger = FormationDirectiveLedger.Build(new[] { cavalry, infantry }, EraStage.Operational1862, null);

        AssertEqual(true, ledger.GetAssignment("cav").RaidAllowed);
        AssertEqual(FormationDirective.RaidSupport, ledger.GetAssignment("cav").Directive);
        AssertEqual(false, ledger.GetAssignment("inf").RaidAllowed);
    }

    private static void FormationDirectiveSummaryChangesWhenAssignmentChanges()
    {
        var a = Snapshot("unit", 0, 15, 15000f, 10000f, FormationLevel.Corps, FrontPosture.Hold);
        var b = Snapshot("unit", 0, 15, 15000f, 10000f, FormationLevel.Corps, FrontPosture.Counterstroke);

        string first = FormationDirectiveLedger.Build(new[] { a }, EraStage.Operational1862, null).Summary();
        string second = FormationDirectiveLedger.Build(new[] { b }, EraStage.Operational1862, null).Summary();

        AssertEqual(false, string.Equals(first, second, StringComparison.Ordinal));
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
