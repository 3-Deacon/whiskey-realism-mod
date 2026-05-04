using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Construction;
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
            ("wl career start gate defers until player command is selected", WlCareerStartGateDefersUntilCommandSelected),
            ("wl start selection retry does not depend on campaign frame", WlStartSelectionRetryDoesNotDependOnCampaignFrame),
            ("wl start selection retry waits for panel before consuming attempt", WlStartSelectionRetryWaitsForPanel),
            ("wl start selection retry waits for vanilla ready frame", WlStartSelectionRetryWaitsForReadyFrame),
            ("wl start selection retry allows stalled ready data before frame fifty", WlStartSelectionRetryAllowsStalledReadyData),
            ("army group doctrine requires two committed formations", ArmyGroupDoctrineRequiresTwoCommittedFormations),
            ("army group doctrine exposes historical commander preference", ArmyGroupDoctrineExposesHistoricalCommanderPreference),
            ("union early profile favors blockade and river control", UnionEarlyProfileFavorsBlockadeAndRiver),
            ("csa early profile favors capital defense and foreign recognition", CsaEarlyProfileFavorsDefenseAndForeignRecognition),
            ("grand strategy tags affect objective score", GrandStrategyTagsAffectObjectiveScore),
            ("union early policy scorer favors legal blockade", UnionEarlyPolicyScorerFavorsLegalBlockade),
            ("csa early policy scorer favors trade and recognition over naval parity", CsaEarlyPolicyScorerFavorsTradeAndRecognition),
            ("objective catalog maps known wl objectives", ObjectiveCatalogMapsKnownWlObjectives),
            ("objective catalog keeps unknown ids unresolved", ObjectiveCatalogKeepsUnknownIdsUnresolved),
            ("recruitment intent prefers supported volunteers", RecruitmentIntentPrefersSupportedVolunteers),
            ("recruitment intent does not leave preferred theater for raw pool", RecruitmentIntentDoesNotLeavePreferredTheaterForRawPool),
            ("recruitment intent keeps vanilla when preferred theater unavailable", RecruitmentIntentKeepsVanillaWhenPreferredTheaterUnavailable),
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
            ("financial ai log gate suppresses repeated corrections", FinancialAiLogGateSuppressesRepeatedCorrections),
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
            ("telegraph intent rejects disconnected candidates", TelegraphIntentRejectsDisconnectedCandidates),
            ("telegraph intent rejects candidates without supporting unit", TelegraphIntentRejectsNoSupportingUnit),
            ("telegraph intent rejects unsafe corridor", TelegraphIntentRejectsUnsafeCorridor),
            ("telegraph intent rejects already covered candidate", TelegraphIntentRejectsAlreadyCoveredCandidate),
            ("telegraph intent favors active command corridor", TelegraphIntentFavorsActiveCommandCorridor),
            ("telegraph intent suppresses emergency noncritical build", TelegraphIntentSuppressesEmergencyNoncriticalBuild),
            ("telegraph intent treats nonfinite inputs as no pressure", TelegraphIntentTreatsNonfiniteInputsAsNoPressure),
            ("telegraph intent builds at exact threshold", TelegraphIntentBuildsAtExactThreshold),
            ("telegraph intent rejects noncorridor high pressure", TelegraphIntentRejectsNoncorridorHighPressure),
            ("fast forward scheduler keeps 5x vanilla only", FastForwardSchedulerKeepsFiveXVanillaOnly),
            ("fast forward scheduler boosts high speeds within cap", FastForwardSchedulerBoostsHighSpeedsWithinCap),
            ("fast forward scheduler disables cleanly", FastForwardSchedulerDisablesCleanly),
            ("fast forward scheduler stops when frame budget is spent", FastForwardSchedulerStopsWhenFrameBudgetIsSpent),
            ("fast forward scheduler throttles after slow frames", FastForwardSchedulerThrottlesAfterSlowFrames),
            ("fast forward scheduler cooldown expires by frame", FastForwardSchedulerCooldownExpiresByFrame),
            ("fast forward log gate suppresses repeated samples", FastForwardLogGateSuppressesRepeatedSamples),
            ("historical hard difficulty adds casualty tolerance only", HistoricalHardDifficultyAddsCasualtyToleranceOnly),
            ("perk scorer favors siege armies for fort pressure", PerkScorerFavorsSiegeArmiesForFortPressure),
            ("perk scorer favors raid armies for irregular pressure", PerkScorerFavorsRaidArmiesForIrregularPressure),
            ("perk scorer favors union blockade fleets", PerkScorerFavorsUnionBlockadeFleets),
            ("perk scorer favors csa raiding fleets", PerkScorerFavorsCsaRaidingFleets),
            ("perk scorer skips unavailable candidates", PerkScorerSkipsUnavailableCandidates)
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

    private static void HistoricalHardDifficultyAddsCasualtyToleranceOnly()
    {
        var hard = DifficultyPersonalityModifier.ForLockedHistoricalDifficulty(
            overrideVanillaSettings: true,
            lockedDifficultyIndex: 3);

        AssertEqual(0f, hard.Aggression);
        AssertEqual(0f, hard.Caution);
        AssertEqual(0f, hard.Audacity);
        AssertEqual(0.10f, hard.CasualtyTolerance);
        AssertEqual(0f, hard.PoliticalResponsiveness);

        var disabled = DifficultyPersonalityModifier.ForLockedHistoricalDifficulty(
            overrideVanillaSettings: false,
            lockedDifficultyIndex: 3);
        AssertEqual(0f, disabled.CasualtyTolerance);

        var veryHard = DifficultyPersonalityModifier.ForLockedHistoricalDifficulty(
            overrideVanillaSettings: true,
            lockedDifficultyIndex: 4);
        AssertEqual(0f, veryHard.CasualtyTolerance);

        var outOfRange = DifficultyPersonalityModifier.ForLockedHistoricalDifficulty(
            overrideVanillaSettings: true,
            lockedDifficultyIndex: 99);
        AssertEqual(0f, outOfRange.CasualtyTolerance);
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

    private static void WlCareerStartGateDefersUntilCommandSelected()
    {
        AssertEqual(false, WlCareerStartGate.ShouldDeferStrategicReview(dlcScenarioActive: false, chosenCommanderId: -1, chosenCommanderHasCommand: false));
        AssertEqual(true, WlCareerStartGate.ShouldDeferStrategicReview(dlcScenarioActive: true, chosenCommanderId: -1, chosenCommanderHasCommand: false));
        AssertEqual(true, WlCareerStartGate.ShouldDeferStrategicReview(dlcScenarioActive: true, chosenCommanderId: 12, chosenCommanderHasCommand: false));
        AssertEqual(false, WlCareerStartGate.ShouldDeferStrategicReview(dlcScenarioActive: true, chosenCommanderId: 12, chosenCommanderHasCommand: true));
    }

    private static void WlStartSelectionRetryDoesNotDependOnCampaignFrame()
    {
        var gate = new WlStartSelectionRetryGate(maxAttempts: 3, retryEveryUnityFrames: 15);

        AssertEqual(true, gate.ShouldAttempt(pending: true, listVisible: false, unityFrame: 1));
        AssertEqual(false, gate.ShouldAttempt(pending: true, listVisible: false, unityFrame: 10));
        AssertEqual(true, gate.ShouldAttempt(pending: true, listVisible: false, unityFrame: 16));
        AssertEqual(false, gate.ShouldAttempt(pending: true, listVisible: true, unityFrame: 31));
    }

    private static void WlStartSelectionRetryWaitsForPanel()
    {
        var gate = new WlStartSelectionRetryGate(maxAttempts: 3, retryEveryUnityFrames: 15);

        AssertEqual(false, gate.ShouldAttempt(pending: true, listVisible: false, panelAvailable: false, unityFrame: 1));
        AssertEqual(0, gate.Attempts);
        AssertEqual(true, gate.ShouldAttempt(pending: true, listVisible: false, panelAvailable: true, unityFrame: 1));
        AssertEqual(1, gate.Attempts);
    }

    private static void WlStartSelectionRetryWaitsForReadyFrame()
    {
        var gate = new WlStartSelectionRetryGate(maxAttempts: 3, retryEveryUnityFrames: 15, minReadyCampaignFrame: 50);

        AssertEqual(false, gate.ShouldAttempt(pending: true, listVisible: false, panelAvailable: true, campaignFrame: 49, unityFrame: 1));
        AssertEqual(0, gate.Attempts);
        AssertEqual(true, gate.ShouldAttempt(pending: true, listVisible: false, panelAvailable: true, campaignFrame: 50, unityFrame: 2));
        AssertEqual(1, gate.Attempts);
    }

    private static void WlStartSelectionRetryAllowsStalledReadyData()
    {
        var gate = new WlStartSelectionRetryGate(maxAttempts: 3, retryEveryUnityFrames: 15, minReadyCampaignFrame: 50);

        AssertEqual(false, gate.ShouldAttempt(pending: true, listVisible: false, panelAvailable: true, campaignFrame: 49, startupDataReady: false, unityFrame: 1));
        AssertEqual(0, gate.Attempts);
        AssertEqual(true, gate.ShouldAttempt(pending: true, listVisible: false, panelAvailable: true, campaignFrame: 49, startupDataReady: true, unityFrame: 16));
        AssertEqual(1, gate.Attempts);
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

    private static void UnionEarlyPolicyScorerFavorsLegalBlockade()
    {
        var profile = GrandStrategyRegistry.Resolve(0, EraStage.Amateur1861);

        float legalBlockade = GrandStrategyPolicyScorer.PolicyWeight(profile, 0, 41);
        float civilianShips = GrandStrategyPolicyScorer.PolicyWeight(profile, 0, 35);
        float enrollment = GrandStrategyPolicyScorer.PolicyWeight(profile, 0, 39);

        AssertTrue(legalBlockade > enrollment, "Union early policy should prioritize legal blockade over raw enrollment");
        AssertTrue(civilianShips > enrollment, "Union early policy should prioritize blockade capacity before enrollment");
    }

    private static void CsaEarlyPolicyScorerFavorsTradeAndRecognition()
    {
        var profile = GrandStrategyRegistry.Resolve(1, EraStage.Amateur1861);

        float kingCotton = GrandStrategyPolicyScorer.PolicyWeight(profile, 1, 103);
        float freeTrade = GrandStrategyPolicyScorer.PolicyWeight(profile, 1, 141);
        float blockadeRunning = GrandStrategyPolicyScorer.PolicyWeight(profile, 1, 142);
        float diplomacy = GrandStrategyPolicyScorer.PolicyWeight(profile, 1, 115);
        float navalParity = GrandStrategyPolicyScorer.PolicyWeight(profile, 1, 135);

        AssertTrue(kingCotton > navalParity, "CSA early policy should prioritize cotton leverage over naval parity");
        AssertTrue(freeTrade > navalParity, "CSA early policy should prioritize trade access over naval parity");
        AssertTrue(blockadeRunning > navalParity, "CSA early policy should prioritize blockade running over naval parity");
        AssertTrue(diplomacy > navalParity, "CSA early policy should prioritize foreign recognition over naval parity");
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

    private static void RecruitmentIntentKeepsVanillaWhenPreferredTheaterUnavailable()
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
                StateId = 8,
                Theater = Theater.Coast,
                Volunteers = 60,
                Drafts = 0,
                Support = 0.5f,
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
                Support = 0.95f,
                IsRecruitable = true,
                IsEnemyState = false,
                IsLocalArea = false
            }
        };

        var exploratory = RecruitmentIntentLedger.SelectState(intent, candidates, vanillaStateId: 8, strengthNeeded: 0, excludeEnemyStates: false);
        var concrete = RecruitmentIntentLedger.SelectState(intent, candidates, vanillaStateId: 8, strengthNeeded: 60, excludeEnemyStates: false);

        AssertEqual(false, exploratory.ShouldReplace);
        AssertEqual(8, exploratory.StateId);
        AssertEqual(false, concrete.ShouldReplace);
        AssertEqual(8, concrete.StateId);
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

    private static void FinancialAiLogGateSuppressesRepeatedCorrections()
    {
        var gate = new FinancialAiLogGate();
        string first = FinancialAiLogGate.Signature(1, "subsidy", 4, 0.15f, 0.10f, FiscalPosture.BalancedWar);
        string repeat = FinancialAiLogGate.Signature(1, "subsidy", 4, 0.15f, 0.10f, FiscalPosture.BalancedWar);
        string changed = FinancialAiLogGate.Signature(1, "subsidy", 3, 0.50f, 0.20f, FiscalPosture.BalancedWar);

        AssertEqual(true, gate.ShouldLog(first));
        AssertEqual(false, gate.ShouldLog(repeat));
        AssertEqual(true, gate.ShouldLog(changed));
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

    private static void FastForwardSchedulerThrottlesAfterSlowFrames()
    {
        var options = new FastForwardAiOptions
        {
            SlowFrameThresholdMs = 8f,
            SlowFrameCooldownFrames = 180
        };

        AssertEqual(false, FastForwardAiScheduler.ShouldThrottleAfterFrame(3.5f, 0f, options));
        AssertEqual(true, FastForwardAiScheduler.ShouldThrottleAfterFrame(8f, 0f, options));
        AssertEqual(true, FastForwardAiScheduler.ShouldThrottleAfterFrame(1f, 9f, options));
        AssertEqual(1180, FastForwardAiScheduler.CooldownUntilFrame(1000, options));
    }

    private static void FastForwardSchedulerCooldownExpiresByFrame()
    {
        AssertEqual(true, FastForwardAiScheduler.InCooldown(1179, 1180));
        AssertEqual(false, FastForwardAiScheduler.InCooldown(1180, 1180));
        AssertEqual(false, FastForwardAiScheduler.InCooldown(1181, 1180));
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

    private static void PerkScorerFavorsSiegeArmiesForFortPressure()
    {
        int selected = PerkSelectionScorer.SelectArmyPerk(
            allianceId: 0,
            theater: Theater.East,
            role: ArmyPerkRole.Siege,
            personality: new PersonalityVector(0.1f, 0.2f, 0f, 0f, 0f),
            availablePerks: new[] { 1, 5, 10, 12 });

        AssertEqual(10, selected);
    }

    private static void PerkScorerFavorsRaidArmiesForIrregularPressure()
    {
        int selected = PerkSelectionScorer.SelectArmyPerk(
            allianceId: 1,
            theater: Theater.West,
            role: ArmyPerkRole.Raid,
            personality: new PersonalityVector(0.5f, -0.2f, 0.7f, 0.1f, -0.4f),
            availablePerks: new[] { 3, 5, 12, 13 });

        AssertEqual(12, selected);
    }

    private static void PerkScorerFavorsUnionBlockadeFleets()
    {
        int selected = PerkSelectionScorer.SelectFleetPerk(
            allianceId: 0,
            role: FleetPerkRole.Blockade,
            availablePerks: new[] { 0, 2, 5, 8 });

        AssertEqual(5, selected);
    }

    private static void PerkScorerFavorsCsaRaidingFleets()
    {
        int selected = PerkSelectionScorer.SelectFleetPerk(
            allianceId: 1,
            role: FleetPerkRole.Raid,
            availablePerks: new[] { 1, 5, 6, 8, 10 });

        AssertEqual(6, selected);
    }

    private static void PerkScorerSkipsUnavailableCandidates()
    {
        int selected = PerkSelectionScorer.SelectArmyPerk(
            allianceId: 0,
            theater: Theater.River,
            role: ArmyPerkRole.River,
            personality: default(PersonalityVector),
            availablePerks: new[] { 1, 3 });

        AssertEqual(3, selected);

        int none = PerkSelectionScorer.SelectFleetPerk(
            allianceId: 0,
            role: FleetPerkRole.Blockade,
            availablePerks: Array.Empty<int>());

        AssertEqual(-1, none);
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
