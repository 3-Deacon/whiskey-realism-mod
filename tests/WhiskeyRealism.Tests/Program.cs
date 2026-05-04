using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Fiscal;

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
            ("army area ledger can redirect independent division input", ArmyAreaLedgerCanRedirectIndependentDivisionInput),
            ("weekly cadence fires on first seen week and week rollover only", WeeklyCadenceFiresOnFirstSeenWeekAndRollover),
            ("operational startup gate fires once when runtime becomes ready same day", OperationalStartupGateFiresOnceWhenRuntimeBecomesReadySameDay),
            ("army group doctrine requires two committed formations", ArmyGroupDoctrineRequiresTwoCommittedFormations),
            ("army group doctrine exposes historical commander preference", ArmyGroupDoctrineExposesHistoricalCommanderPreference),
            ("union early profile favors blockade and river control", UnionEarlyProfileFavorsBlockadeAndRiver),
            ("csa early profile favors capital defense and foreign recognition", CsaEarlyProfileFavorsDefenseAndForeignRecognition),
            ("grand strategy tags affect objective score", GrandStrategyTagsAffectObjectiveScore),
            ("objective catalog maps known wl objectives", ObjectiveCatalogMapsKnownWlObjectives),
            ("objective catalog keeps unknown ids unresolved", ObjectiveCatalogKeepsUnknownIdsUnresolved),
            ("recruitment intent prefers supported volunteers", RecruitmentIntentPrefersSupportedVolunteers),
            ("recruitment intent does not leave preferred theater for raw pool", RecruitmentIntentDoesNotLeavePreferredTheaterForRawPool),
            ("recruitment intent keeps vanilla when draft would be forced at parity", RecruitmentIntentKeepsVanillaWhenDraftWouldBeForcedAtParity),
            ("recruitment intent avoids enemy states when excluded", RecruitmentIntentAvoidsEnemyStatesWhenExcluded),
            ("recruitment log gate suppresses repeated replacements", RecruitmentLogGateSuppressesRepeatedReplacements),
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
            ("formation directive summary changes when assignment changes", FormationDirectiveSummaryChangesWhenAssignmentChanges),
            ("fiscal csa healthy credit stays balanced", FiscalCsaHealthyCreditStaysBalanced),
            ("fiscal enters credit defense before gate", FiscalEntersCreditDefenseBeforeGate),
            ("fiscal enters emergency before bond floor", FiscalEntersEmergencyBeforeBondFloor),
            ("fiscal protects supply before force growth", FiscalProtectsSupplyBeforeForceGrowth),
            ("fiscal force cap suppresses manpower policies", FiscalForceCapSuppressesManpowerPolicies),
            ("fiscal force costs suppress manpower policies", FiscalForceCostsSuppressManpowerPolicies),
            ("fiscal hysteresis prevents immediate recovery", FiscalHysteresisPreventsImmediateRecovery),
            ("fiscal credit defense requires stable exit weeks", FiscalCreditDefenseRequiresStableExitWeeks),
            ("fiscal emergency residue clears after stable weeks", FiscalEmergencyResidueClearsAfterStableWeeks),
            ("construction scorer favors csa banks in balanced posture", ConstructionScorerFavorsCsaBanks),
            ("construction scorer favors logistics when supply is protected", ConstructionScorerFavorsLogistics),
            ("construction scorer suppresses csa naval under credit defense", ConstructionScorerSuppressesCsaNaval),
            ("construction scorer floors emergency industrial suppression", ConstructionScorerFloorsEmergencyIndustry),
            ("fast forward scheduler keeps 5x vanilla only", FastForwardSchedulerKeepsFiveXVanillaOnly),
            ("fast forward scheduler boosts high speeds within cap", FastForwardSchedulerBoostsHighSpeedsWithinCap),
            ("fast forward scheduler disables cleanly", FastForwardSchedulerDisablesCleanly),
            ("fast forward scheduler stops when frame budget is spent", FastForwardSchedulerStopsWhenFrameBudgetIsSpent),
            ("fast forward log gate suppresses repeated samples", FastForwardLogGateSuppressesRepeatedSamples)
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

    private static void ArmyAreaLedgerCanRedirectIndependentDivisionInput()
    {
        var ledger = ArmyAreaLedger.Build(new[]
        {
            new ArmyAreaInput
            {
                UnitKey = "division",
                AllianceId = 1,
                UnitName = "Army of Northern Virginia",
                CommanderName = "Lee",
                CurrentAreaKey = "TennesseeGeorgiaCorridor",
                Strength = 5000f,
                Readiness = 0.75f
            }
        }, planTargetAreaKey: null);

        var assignment = ledger.GetAssignment("division");
        AssertEqual("VirginiaCapitalCorridor", assignment.AssignedAreaKey);
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

    private static void OperationalStartupGateFiresOnceWhenRuntimeBecomesReadySameDay()
    {
        var gate = new OperationalStartupGate();

        AssertEqual(true, gate.ShouldNotify(dateChanged: true, runtimeReady: false));
        AssertEqual(false, gate.ShouldNotify(dateChanged: false, runtimeReady: false));
        AssertEqual(true, gate.ShouldNotify(dateChanged: false, runtimeReady: true));
        AssertEqual(false, gate.ShouldNotify(dateChanged: false, runtimeReady: true));
        AssertEqual(true, gate.ShouldNotify(dateChanged: true, runtimeReady: true));
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

    private static void ObjectiveCatalogMapsKnownWlObjectives()
    {
        foreach (var id in new[] { 3, 4, 17, 29, 30, 31, 32, 33, 34, 35, 36, 37 })
            AssertTrue(ObjectiveCatalog.TryResolve(id, out _), "expected objective metadata for ID " + id);

        AssertTrue(ObjectiveCatalog.TryResolve(3, out var richmond), "expected Richmond objective metadata");
        AssertEqual(Theater.East, richmond.Theater);
        AssertEqual(Category.CapitalThreat, richmond.Category);
        AssertEqual(false, richmond.IsDerived);
        AssertTrue(richmond.HasTag(StrategyTag.CapitalThreat), "Richmond should carry capital threat");
        AssertTrue(richmond.HasTag(StrategyTag.CapitalDefense), "Richmond should carry capital defense");

        AssertTrue(ObjectiveCatalog.TryResolve(17, out var mississippi), "expected Mississippi River objective metadata");
        AssertEqual(Theater.River, mississippi.Theater);
        AssertEqual(Category.RiverControl, mississippi.Category);
        AssertTrue(mississippi.HasTag(StrategyTag.RiverControl), "Mississippi should carry river control");

        AssertTrue(ObjectiveCatalog.TryResolve(35, out var coastalNc), "expected Coastal NC objective metadata");
        AssertEqual(Theater.Coast, coastalNc.Theater);
        AssertEqual(Category.SupplyHub, coastalNc.Category);
        AssertTrue(coastalNc.HasTag(StrategyTag.Blockade), "Coastal NC should carry blockade");
        AssertTrue(coastalNc.HasTag(StrategyTag.PortAccess), "Coastal NC should carry port access");

        AssertTrue(ObjectiveCatalog.TryResolve(36, out var saltville), "expected Saltville objective metadata");
        AssertEqual(Theater.West, saltville.Theater);
        AssertEqual(Category.SupplyHub, saltville.Category);
        AssertTrue(saltville.HasTag(StrategyTag.RailHub), "Saltville should carry supply/rail pressure");
    }

    private static void ObjectiveCatalogKeepsUnknownIdsUnresolved()
    {
        AssertEqual(false, ObjectiveCatalog.TryResolve(9999, out _));
    }

    private static void RecruitmentIntentPrefersSupportedVolunteers()
    {
        var intent = new RecruitmentIntent
        {
            AllianceId = 1,
            PreferredTheater = Theater.East,
            StrengthRatio = 0.85f,
            OwnStateSupportFloor = 0.5f
        };
        var candidates = new[]
        {
            new RecruitmentStateCandidate
            {
                StateId = 38,
                Theater = Theater.East,
                Volunteers = 7000,
                Drafts = 2000,
                Support = 0.9f,
                IsRecruitable = true,
                IsEnemyState = false,
                IsLocalArea = true
            },
            new RecruitmentStateCandidate
            {
                StateId = 15,
                Theater = Theater.River,
                Volunteers = 8000,
                Drafts = 5000,
                Support = 0.75f,
                IsRecruitable = true,
                IsEnemyState = false,
                IsLocalArea = true
            }
        };

        var decision = RecruitmentIntentLedger.SelectState(intent, candidates, vanillaStateId: 15, strengthNeeded: 5000, excludeEnemyStates: false);

        AssertEqual(true, decision.ShouldReplace);
        AssertEqual(38, decision.StateId);
        AssertEqual("preferred-theater-volunteers", decision.Reason);
    }

    private static void RecruitmentIntentKeepsVanillaWhenDraftWouldBeForcedAtParity()
    {
        var intent = new RecruitmentIntent
        {
            AllianceId = 1,
            PreferredTheater = Theater.East,
            StrengthRatio = 1.05f,
            OwnStateSupportFloor = 0.5f
        };
        var candidates = new[]
        {
            new RecruitmentStateCandidate
            {
                StateId = 38,
                Theater = Theater.East,
                Volunteers = 1000,
                Drafts = 7000,
                Support = 0.9f,
                IsRecruitable = true,
                IsEnemyState = false,
                IsLocalArea = true
            },
            new RecruitmentStateCandidate
            {
                StateId = 15,
                Theater = Theater.River,
                Volunteers = 6000,
                Drafts = 0,
                Support = 0.7f,
                IsRecruitable = true,
                IsEnemyState = false,
                IsLocalArea = true
            }
        };

        var decision = RecruitmentIntentLedger.SelectState(intent, candidates, vanillaStateId: 15, strengthNeeded: 5000, excludeEnemyStates: false);

        AssertEqual(false, decision.ShouldReplace);
        AssertEqual(15, decision.StateId);
    }

    private static void RecruitmentIntentDoesNotLeavePreferredTheaterForRawPool()
    {
        var intent = new RecruitmentIntent
        {
            AllianceId = 1,
            PreferredTheater = Theater.East,
            StrengthRatio = 0.8f,
            OwnStateSupportFloor = 0.5f
        };
        var candidates = new[]
        {
            new RecruitmentStateCandidate
            {
                StateId = 38,
                Theater = Theater.East,
                Volunteers = 2000,
                Drafts = 0,
                Support = 0.9f,
                IsRecruitable = true,
                IsEnemyState = false,
                IsLocalArea = true
            },
            new RecruitmentStateCandidate
            {
                StateId = 0,
                Theater = Theater.West,
                Volunteers = 20000,
                Drafts = 0,
                Support = 0.9f,
                IsRecruitable = true,
                IsEnemyState = false,
                IsLocalArea = true
            }
        };

        var decision = RecruitmentIntentLedger.SelectState(intent, candidates, vanillaStateId: 38, strengthNeeded: 60, excludeEnemyStates: false);

        AssertEqual(false, decision.ShouldReplace);
        AssertEqual(38, decision.StateId);
    }

    private static void RecruitmentIntentAvoidsEnemyStatesWhenExcluded()
    {
        var intent = new RecruitmentIntent
        {
            AllianceId = 0,
            PreferredTheater = Theater.East,
            StrengthRatio = 0.8f,
            OwnStateSupportFloor = 0.5f
        };
        var candidates = new[]
        {
            new RecruitmentStateCandidate
            {
                StateId = 39,
                Theater = Theater.East,
                Volunteers = 8000,
                Drafts = 5000,
                Support = 0.7f,
                IsRecruitable = true,
                IsEnemyState = true,
                IsLocalArea = true
            },
            new RecruitmentStateCandidate
            {
                StateId = 31,
                Theater = Theater.East,
                Volunteers = 7000,
                Drafts = 0,
                Support = 0.85f,
                IsRecruitable = true,
                IsEnemyState = false,
                IsLocalArea = false
            }
        };

        var decision = RecruitmentIntentLedger.SelectState(intent, candidates, vanillaStateId: 39, strengthNeeded: 5000, excludeEnemyStates: true);

        AssertEqual(true, decision.ShouldReplace);
        AssertEqual(31, decision.StateId);
    }

    private static void RecruitmentLogGateSuppressesRepeatedReplacements()
    {
        var gate = new RecruitmentLogGate();
        string first = RecruitmentLogGate.Signature(1, 38, 0, 60, Theater.East, "volunteer-high-support");
        string repeat = RecruitmentLogGate.Signature(1, 38, 0, 60, Theater.East, "volunteer-high-support");
        string changed = RecruitmentLogGate.Signature(1, 8, 0, 60, Theater.East, "volunteer-high-support");

        AssertEqual(true, gate.ShouldLog(first));
        AssertEqual(false, gate.ShouldLog(repeat));
        AssertEqual(true, gate.ShouldLog(changed));
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

    private static void FiscalForceCapSuppressesManpowerPolicies()
    {
        var input = BuildFiscalInput();
        input.CurrentRating = 10;
        input.SupplyPressure = 0.80f;
        input.LowSupplyFormationCount = 5;

        var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
        float weight = FiscalPolicyScorer.PolicyWeight(output, 1, 136);

        AssertTrue(weight < 0f, "force-cap state should suppress CSA draft escalation");
    }

    private static void FiscalForceCostsSuppressManpowerPolicies()
    {
        var input = BuildFiscalInput();
        input.CurrentRating = 4;
        input.SupplyPressure = 0.10f;
        input.LowSupplyFormationCount = 0;
        input.LowAmmoFormationCount = 0;
        input.ArmyUpkeep = -50000000f;
        input.NavyUpkeep = -5000000f;
        input.RecruitmentCost = -5000000f;
        input.SupplyDepotPurchases = 0f;
        input.AnnualBalance = -30000000f;

        var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
        float weight = FiscalPolicyScorer.PolicyWeight(output, 1, 136);

        AssertEqual(true, output.ForceCapWarning);
        AssertTrue(weight < 0f, "force-cost pressure should suppress CSA draft escalation");
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

    private static void FiscalCreditDefenseRequiresStableExitWeeks()
    {
        var input = BuildFiscalInput();
        input.CurrentRating = 4;
        input.AnnualBalance = 500000f;
        input.Treasury = 2500000f;
        input.Memory.PreviousPosture = FiscalPosture.CreditDefense;
        input.Memory.EmergencyResidue = false;
        input.Memory.StableWeeksAboveEmergency = 1;

        var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
        AssertEqual(FiscalPosture.CreditDefense, output.Posture);

        input.Memory.StableWeeksAboveEmergency = 2;
        output = FiscalIntentLedger.Compute(input, new FiscalOptions());
        AssertEqual(FiscalPosture.BalancedWar, output.Posture);
    }

    private static void FiscalEmergencyResidueClearsAfterStableWeeks()
    {
        var input = BuildFiscalInput();
        input.CurrentRating = 4;
        input.AnnualBalance = 500000f;
        input.Treasury = 2500000f;
        input.Memory.PreviousPosture = FiscalPosture.CreditDefense;
        input.Memory.EmergencyResidue = true;
        input.Memory.StableWeeksAboveEmergency = 2;

        var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
        AssertEqual(FiscalPosture.BalancedWar, output.Posture);
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

    private static void FastForwardSchedulerKeepsFiveXVanillaOnly()
    {
        var options = new FastForwardAiOptions();
        AssertEqual(1, FastForwardAiScheduler.VanillaPasses(1f));
        AssertEqual(2, FastForwardAiScheduler.VanillaPasses(5f));
        AssertEqual(0, FastForwardAiScheduler.MaxExtraPasses(5f, options));
    }

    private static void FastForwardSchedulerBoostsHighSpeedsWithinCap()
    {
        var options = new FastForwardAiOptions();
        AssertEqual(4, FastForwardAiScheduler.VanillaPasses(20f));
        AssertEqual(7, FastForwardAiScheduler.VanillaPasses(50f));
        AssertEqual(2, FastForwardAiScheduler.MaxExtraPasses(20f, options));
        AssertEqual(4, FastForwardAiScheduler.MaxExtraPasses(50f, options));
    }

    private static void FastForwardSchedulerDisablesCleanly()
    {
        var options = new FastForwardAiOptions { Enabled = false };
        AssertEqual(0, FastForwardAiScheduler.MaxExtraPasses(50f, options));
        AssertEqual(false, FastForwardAiScheduler.ShouldRunExtraPass(0, 0.1f, 50f, options));
    }

    private static void FastForwardSchedulerStopsWhenFrameBudgetIsSpent()
    {
        var options = new FastForwardAiOptions { MaxExtraPassesAt50x = 4, FrameBudgetMs = 1.5f };
        AssertEqual(true, FastForwardAiScheduler.ShouldRunExtraPass(3, 1.49f, 50f, options));
        AssertEqual(false, FastForwardAiScheduler.ShouldRunExtraPass(4, 1.49f, 50f, options));
        AssertEqual(false, FastForwardAiScheduler.ShouldRunExtraPass(3, 1.5f, 50f, options));
    }

    private static void FastForwardLogGateSuppressesRepeatedSamples()
    {
        var gate = new FastForwardAiLogGate();
        string first = FastForwardAiScheduler.LogSignature(50f, 7, 4, 4, budgetExhausted: true);
        string repeat = FastForwardAiScheduler.LogSignature(50f, 7, 2, 4, budgetExhausted: true);
        string changed = FastForwardAiScheduler.LogSignature(20f, 4, 2, 2, budgetExhausted: false);

        AssertEqual(true, gate.ShouldLog(first));
        AssertEqual(false, gate.ShouldLog(repeat));
        AssertEqual(true, gate.ShouldLog(changed));
        AssertEqual(false, gate.ShouldLog(first));
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
