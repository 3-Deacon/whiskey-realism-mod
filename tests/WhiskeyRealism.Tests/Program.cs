using System;
using System.Collections.Generic;
using System.Reflection;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Tactical;

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
            ("historical registry maps CSA northwest commands to Allegheny approaches", HistoricalRegistryMapsCsaNorthwest),
            ("historical registry leaves inactive full-war armies unassigned", HistoricalRegistryLeavesInactiveFullWarArmiesUnassigned),
            ("army area ledger holds historical area", ArmyAreaLedgerHoldsHistoricalArea),
            ("army area ledger redirects CSA northwest command to northwest Virginia", ArmyAreaLedgerRedirectsCsaNorthwestCommand),
            ("army area ledger leaves inactive full-war army in current area", ArmyAreaLedgerLeavesInactiveFullWarArmyInCurrentArea),
            ("army area ledger gives dynamic fallback local doctrine", ArmyAreaLedgerGivesDynamicFallbackLocalDoctrine),
            ("army area ledger lets dynamic fallback counterstroke its local plan area", ArmyAreaLedgerLetsDynamicFallbackCounterstrokeLocalPlanArea),
            ("army area ledger can redirect independent division input", ArmyAreaLedgerCanRedirectIndependentDivisionInput),
            ("battle history query matches inside spatial and date window", BattleHistoryQueryMatchesInsideSpatialAndDateWindow),
            ("battle history query rejects outside spatial window", BattleHistoryQueryRejectsOutsideSpatialWindow),
            ("battle history query rejects outside date window", BattleHistoryQueryRejectsOutsideDateWindow),
            ("theater pressure view sums own and enemy strength per theater", TheaterPressureViewSumsOwnAndEnemyPerTheater),
            ("daily cadence fires on first call and day rollover only", DailyCadenceFiresOnFirstCallAndDayRolloverOnly),
            ("daily cadence rejects invalid dates", DailyCadenceRejectsInvalidDates),
            ("strategic cadence alternates formation by alliance", StrategicCadenceAlternatesFormationByAlliance),
            ("strategic cadence refreshes weekly or on source change", StrategicCadenceRefreshesWeeklyOrSourceChange),
            ("strategic cadence stable source can skip downstream rebuild", StrategicCadenceStableSourceSkipsDownstreamRebuild),
            ("tactical telemetry maps macro names", TacticalTelemetryMapsMacroNames),
            ("tactical telemetry summary handles null", TacticalTelemetrySummaryHandlesNull),
            ("tactical telemetry maps player-order prefix", TacticalTelemetryMapsPlayerOrderPrefix),
            ("tactical telemetry signature changes on material fields", TacticalTelemetrySignatureChangesOnMaterialFields),
            ("tactical telemetry throttle suppresses repeated signature", TacticalTelemetryThrottleSuppressesRepeatedSignature),
            ("tactical telemetry delta formats before after counts", TacticalTelemetryDeltaFormatsBeforeAfterCounts),
            ("operational startup gate fires once when runtime becomes ready same day", OperationalStartupGateFiresOnceWhenRuntimeBecomesReadySameDay),
            ("wl career start gate defers until player command is selected", WlCareerStartGateDefersUntilCommandSelected),
            ("wl diary startup gate defers until diary dependencies are ready", WlDiaryStartupGateDefersUntilReady),
            ("wl start selection retry does not depend on campaign frame", WlStartSelectionRetryDoesNotDependOnCampaignFrame),
            ("wl start selection retry waits for panel before consuming attempt", WlStartSelectionRetryWaitsForPanel),
            ("wl start selection retry waits for vanilla ready frame", WlStartSelectionRetryWaitsForReadyFrame),
            ("wl start selection retry blocks early ready data before frame fifty", WlStartSelectionRetryBlocksEarlyReadyData),
            ("wl dispatch sanitizer fixes type 56 stance none", WlDispatchSanitizerFixesType56StanceNone),
            ("wl dispatch sanitizer fixes type 57 stance none", WlDispatchSanitizerFixesType57StanceNone),
            ("wl dispatch sanitizer fixes type 15 no-orders none", WlDispatchSanitizerFixesType15NoOrdersNone),
            ("wl dispatch sanitizer ignores non-candidate type", WlDispatchSanitizerIgnoresNonCandidateType),
            ("wl dispatch sanitizer handles null content", WlDispatchSanitizerHandlesNullContent),
            ("wl dispatch sanitizer leaves normal content unchanged", WlDispatchSanitizerLeavesNormalContentUnchanged),
            ("wl bridge inactive allows direct movement", WlBridgeInactiveAllowsDirectMovement),
            ("wl bridge non-player alliance allows direct movement", WlBridgeNonPlayerAllianceAllowsDirectMovement),
            ("wl bridge report only under wl player alliance blocks movement", WlBridgeReportOnlyUnderWlPlayerAllianceBlocksMovement),
            ("wl bridge report only inactive stays not wl", WlBridgeReportOnlyInactiveStaysNotWl),
            ("wl bridge report only non-player alliance stays direct", WlBridgeReportOnlyNonPlayerAllianceStaysDirect),
            ("wl bridge player cic skips movement", WlBridgePlayerCicSkipsMovement),
            ("wl bridge moved by player skips movement", WlBridgeMovedByPlayerSkipsMovement),
            ("wl bridge eligible under commander issues current order", WlBridgeEligibleUnderCommanderIssuesCurrentOrder),
            ("wl bridge ineligible under commander blocks direct fallback", WlBridgeIneligibleUnderCommanderBlocksDirectFallback),
            ("wl bridge failed vanilla call blocks direct fallback", WlBridgeFailedVanillaCallBlocksDirectFallback),
            ("wl bridge part of player unit not under commander stays direct for c0c", WlBridgePartOfPlayerUnitNotUnderCommanderStaysDirectForC0c),
            ("wl camp short camp credits normal rest", WlCampShortCampCreditsNormalRest),
            ("wl camp short camp credits wounded rest", WlCampShortCampCreditsWoundedRest),
            ("wl camp short camp credits preserve minimum proportions", WlCampShortCampCreditsPreserveMinimumProportions),
            ("wl camp short camp enough time no correction", WlCampShortCampEnoughTimeNoCorrection),
            ("wl camp short camp zero minimum no correction", WlCampShortCampZeroMinimumNoCorrection),
            ("wl camp responsive bonus weights recent included station", WlCampResponsiveBonusWeightsRecentIncludedStation),
            ("wl camp responsive bonus includes companion recent average", WlCampResponsiveBonusIncludesCompanionRecentAverage),
            ("wl camp responsive bonus partial companion history divides by window", WlCampResponsiveBonusPartialCompanionHistoryDividesByWindow),
            ("wl camp responsive bonus excluded stations stay vanilla", WlCampResponsiveBonusExcludedStationsStayVanilla),
            ("wl camp responsive bonus use average false stays vanilla", WlCampResponsiveBonusUseAverageFalseStaysVanilla),
            ("wl camp responsive bonus nonfinite input stays bounded", WlCampResponsiveBonusNonfiniteInputStaysBounded),
            ("wl camp rest reward cap makes six hours full reward", WlCampRestRewardCapMakesSixHoursFullReward),
            ("wl camp rest reward cap leaves non rest stations vanilla", WlCampRestRewardCapLeavesNonRestStationsVanilla),
            ("wl camp rest reward cap invalid config falls back", WlCampRestRewardCapInvalidConfigFallsBack),
            ("wl camp unit divisor clamps invalid cached counts", WlCampUnitDivisorClampsInvalidCachedCounts),
            ("wl camp unit divisor default power softens four and nine units", WlCampUnitDivisorDefaultPowerSoftensFourAndNineUnits),
            ("wl camp unit modifier clamps negative to zero", WlCampUnitModifierClampsNegativeToZero),
            ("wl camp unit modifier nonfinite input falls back", WlCampUnitModifierNonfiniteInputFallsBack),
            ("wl camp unit power one is vanilla equivalent", WlCampUnitPowerOneIsVanillaEquivalent),
            ("wl camp unit payoff excluded or undivided returns vanilla", WlCampUnitPayoffExcludedOrUndividedReturnsVanilla),
            ("wl camp short camp nonfinite input no correction", WlCampShortCampNonfiniteInputNoCorrection),
            ("assert near rejects nonfinite values", AssertNearRejectsNonfiniteValues),
            ("army group doctrine requires two committed formations", ArmyGroupDoctrineRequiresTwoCommittedFormations),
            ("army group doctrine exposes historical commander preference", ArmyGroupDoctrineExposesHistoricalCommanderPreference),
            ("union early profile favors blockade and river control", UnionEarlyProfileFavorsBlockadeAndRiver),
            ("asset role scorer flags csa blockade port from profile", AssetRoleScorerFlagsCsaBlockadePortFromProfile),
            ("asset role scorer flags union river hub from profile", AssetRoleScorerFlagsUnionRiverHubFromProfile),
            ("asset role scorer flags key fort from level", AssetRoleScorerFlagsKeyFortFromLevel),
            ("asset role scorer flags capital approach by distance", AssetRoleScorerFlagsCapitalApproachByDistance),
            ("asset role scorer returns none when no rules match", AssetRoleScorerReturnsNoneWhenNoRulesMatch),
            ("asset role scorer flags union forward base from profile", AssetRoleScorerFlagsUnionForwardBaseFromProfile),
            ("asset role scorer rejects union forward base when enemy owned", AssetRoleScorerRejectsUnionForwardBaseEnemyOwned),
            ("asset role scorer score town flags capital approach by distance", AssetRoleScorerScoreTownFlagsCapitalApproach),
            ("asset role catalog overrides scorer for named anchor", AssetRoleCatalogOverridesScorer),
            ("asset role catalog returns none for unknown name", AssetRoleCatalogReturnsNoneForUnknown),
            ("asset role catalog resolves real gtcw names", AssetRoleCatalogResolvesRealGtcwNames),
            ("csa early profile favors capital defense and foreign recognition", CsaEarlyProfileFavorsDefenseAndForeignRecognition),
            ("grand strategy tags affect objective score", GrandStrategyTagsAffectObjectiveScore),
            ("union early policy scorer favors legal blockade", UnionEarlyPolicyScorerFavorsLegalBlockade),
            ("csa early policy scorer favors trade and recognition over naval parity", CsaEarlyPolicyScorerFavorsTradeAndRecognition),
            ("theater classifier maps wl capitals to east", TheaterClassifierMapsWlCapitalsToEast),
            ("army area classifier maps W&L northwest Virginia towns", ArmyAreaClassifierMapsWlNorthwestVirginiaTowns),
            ("theater classifier uses state names before broad coordinates", TheaterClassifierUsesStateNamesBeforeCoordinates),
            ("campaign map ledger only maps states represented by towns", CampaignMapLedgerOnlyMapsRepresentedStates),
            ("campaign map ledger ranks owned capitals for defense", CampaignMapLedgerRanksOwnedCapitalsForDefense),
            ("campaign map ledger tracks ports and forts", CampaignMapLedgerTracksPortsAndForts),
            ("defense force sizer avoids oversized army for small threat", DefenseForceSizerAvoidsOversizedArmyForSmallThreat),
            ("defense force sizer accepts large force for large threat", DefenseForceSizerAcceptsLargeForceForLargeThreat),
            ("objective catalog maps known wl objectives", ObjectiveCatalogMapsKnownWlObjectives),
            ("objective catalog keeps unknown ids unresolved", ObjectiveCatalogKeepsUnknownIdsUnresolved),
            ("recruitment intent prefers supported volunteers", RecruitmentIntentPrefersSupportedVolunteers),
            ("recruitment intent does not leave preferred theater for raw pool", RecruitmentIntentDoesNotLeavePreferredTheaterForRawPool),
            ("recruitment intent keeps vanilla when preferred theater unavailable", RecruitmentIntentKeepsVanillaWhenPreferredTheaterUnavailable),
            ("recruitment intent keeps vanilla when draft would be forced at parity", RecruitmentIntentKeepsVanillaWhenDraftWouldBeForcedAtParity),
            ("recruitment intent protects threatened priority area", RecruitmentIntentProtectsThreatenedPriorityArea),
            ("recruitment intent ignores priority area without threat", RecruitmentIntentIgnoresPriorityAreaWithoutThreat),
            ("recruitment intent avoids enemy states when excluded", RecruitmentIntentAvoidsEnemyStatesWhenExcluded),
            ("recruitment log gate suppresses repeated replacements", RecruitmentLogGateSuppressesRepeatedReplacements),
            ("project doctrine catalog maps all active vanilla project rows", ProjectDoctrineCatalogMapsAllActiveRows),
            ("project doctrine catalog entries are immutable through public api", ProjectDoctrineCatalogEntriesAreImmutable),
            ("project doctrine catalog marks market reform fully broken", ProjectDoctrineCatalogMarksMarketReformBroken),
            ("project doctrine catalog maps representative buckets and lanes", ProjectDoctrineCatalogMapsRepresentativeBucketsAndLanes),
            ("project doctrine catalog maps organization reform aliases", ProjectDoctrineCatalogMapsOrganizationReformAliases),
            ("project doctrine catalog has no lane six or seven entries", ProjectDoctrineCatalogHasNoLaneSixOrSevenEntries),
            ("project doctrine signals clamp weapon and artillery deficits", ProjectDoctrineSignalsClampWeaponAndArtilleryDeficits),
            ("project doctrine signals map fiscal posture to credit stress", ProjectDoctrineSignalsMapFiscalPosture),
            ("project doctrine signals compute late war collapse risk", ProjectDoctrineSignalsComputeLateWarCollapseRisk),
            ("project doctrine signals keep recognition and port values bounded", ProjectDoctrineSignalsBoundRecognitionAndPort),
            ("project doctrine signals default blockade pressure is neutral", ProjectDoctrineSignalsDefaultBlockadePressureNeutral),
            ("project doctrine signals ignore nonfinite logistics pressure side", ProjectDoctrineSignalsIgnoreNonfiniteLogisticsPressureSide),
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
            ("operational probe assigns one bounded same-area formation", OperationalProbeAssignsOneBoundedSameAreaFormation),
            ("operational probe pauses on enemy reaction", OperationalProbePausesOnEnemyReaction),
            ("operational probe escalates after favorable contact", OperationalProbeEscalatesAfterFavorableContact),
            ("operational probe refuses critical hold donor", OperationalProbeRefusesCriticalHoldDonor),
            ("operational probe overlays formation directive", OperationalProbeOverlaysFormationDirective),
            ("operational probe stays continuing on no contact even after minimum days", OperationalProbeStaysContinuingOnNoContactAfterMinimumDays),
            ("operational probe state has single source on coordinator", OperationalProbeStateHasSingleSourceOnCoordinator),
            ("recompute pressure resets counters before counting", RecomputePressureResetsCountersBeforeCounting),
            ("operational tempo chapter one delays escalation", OperationalTempoChapterOneDelaysEscalation),
            ("operational tempo late union sustains pressure", OperationalTempoLateUnionSustainsPressure),
            ("operational tempo winter slows probes", OperationalTempoWinterSlowsProbes),
            ("operational tempo late csa is more conservative than union", OperationalTempoLateCsaMoreConservativeThanUnion),
            ("fiscal csa healthy credit stays balanced", FiscalCsaHealthyCreditStaysBalanced),
            ("fiscal enters credit defense before gate", FiscalEntersCreditDefenseBeforeGate),
            ("fiscal enters emergency before bond floor", FiscalEntersEmergencyBeforeBondFloor),
            ("fiscal protects supply before force growth", FiscalProtectsSupplyBeforeForceGrowth),
            ("fiscal force cap suppresses manpower policies", FiscalForceCapSuppressesManpowerPolicies),
            ("fiscal force costs suppress manpower policies", FiscalForceCostsSuppressManpowerPolicies),
            ("fiscal hysteresis prevents immediate recovery", FiscalHysteresisPreventsImmediateRecovery),
            ("fiscal credit defense requires stable exit ticks", FiscalCreditDefenseRequiresStableExitTicks),
            ("fiscal emergency residue clears after stable ticks", FiscalEmergencyResidueClearsAfterStableTicks),
            ("fiscal clamps disabled subsidy focus to zero", FiscalClampsDisabledSubsidyFocusToZero),
            ("fiscal clamps negative saved subsidy values", FiscalClampsNegativeSavedSubsidyValues),
            ("financial ai log gate suppresses repeated corrections", FinancialAiLogGateSuppressesRepeatedCorrections),
            ("economy alliance data guard suppresses only null references", EconomyAllianceDataGuardSuppressesOnlyNullReferences),
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
            ("fast forward scheduler keeps 5x vanilla only", FastForwardSchedulerKeepsFiveXVanillaOnly),
            ("fast forward scheduler boosts high speeds within cap", FastForwardSchedulerBoostsHighSpeedsWithinCap),
            ("fast forward scheduler disables cleanly", FastForwardSchedulerDisablesCleanly),
            ("fast forward scheduler stops when frame budget is spent", FastForwardSchedulerStopsWhenFrameBudgetIsSpent),
            ("fast forward scheduler throttles after slow frames", FastForwardSchedulerThrottlesAfterSlowFrames),
            ("fast forward scheduler cooldown expires by frame", FastForwardSchedulerCooldownExpiresByFrame),
            ("campaign ai governor caps vanilla passes", CampaignAiGovernorCapsVanillaPasses),
            ("campaign ai governor respects frame budget before first pass", CampaignAiGovernorRespectsFrameBudgetBeforeFirstPass),
            ("campaign ai governor skips paused vanilla update", CampaignAiGovernorSkipsPausedVanillaUpdate),
            ("commander assignment guard clears stale previous command", CommanderAssignmentGuardClearsStalePreviousCommand),
            ("campaign filter map guard bounds repeated no progress", CampaignFilterMapGuardBoundsRepeatedNoProgress),
            ("campaign filter map guard detects assign-filters bootstrap needs", CampaignFilterMapGuardDetectsAssignFiltersBootstrapNeeds),
            ("state handover guard requires decisive support", StateHandoverGuardRequiresDecisiveSupport),
            ("fleet patrol guard resets completed AI patrol", FleetPatrolGuardResetsCompletedAiPatrol),
            ("artillery combine gun transfer preserves source guns", ArtilleryCombineGunTransferPreservesSourceGuns),
            ("fast forward log gate suppresses repeated samples", FastForwardLogGateSuppressesRepeatedSamples),
            ("historical hard difficulty adds casualty tolerance only", HistoricalHardDifficultyAddsCasualtyToleranceOnly),
            ("perk scorer favors siege armies for fort pressure", PerkScorerFavorsSiegeArmiesForFortPressure),
            ("perk scorer favors raid armies for irregular pressure", PerkScorerFavorsRaidArmiesForIrregularPressure),
            ("perk scorer favors union blockade fleets", PerkScorerFavorsUnionBlockadeFleets),
            ("perk scorer favors csa raiding fleets", PerkScorerFavorsCsaRaidingFleets),
            ("perk scorer skips unavailable candidates", PerkScorerSkipsUnavailableCandidates),
            ("front sector signature ignores sub-bucket ratio jitter", FrontSectorSignatureIgnoresSubBucketRatioJitter),
            ("asset strategic role flags compose additively", AssetStrategicRoleFlagsComposeAdditively),
            ("campaign map ledger applies role catalog to towns and assets", CampaignMapLedgerAppliesRoleCatalog),
            ("campaign map ledger signature reflects role changes", CampaignMapLedgerSignatureReflectsRoleChanges),
            ("defense posture defaults to not-evaluated", DefensePostureDefaultsToNotEvaluated),
            ("defense threat carries signature and posture", DefenseThreatCarriesSignatureAndPosture),
            ("threat signature for sif uses instance and spot", ThreatSignatureForSifUsesInstanceAndSpot),
            ("threat signature for raid uses instance and asset", ThreatSignatureForRaidUsesInstanceAndAsset),
            ("threat signature for asset uses sorted top-n enemies", ThreatSignatureForAssetUsesSortedTopN),
            ("threat signature is stable across reordered enemies", ThreatSignatureIsStableAcrossReorderedEnemies),
            ("threat signature for raid handles null asset", ThreatSignatureForRaidHandlesNullAsset),
            ("threat signature for asset handles null name", ThreatSignatureForAssetHandlesNullName),
            ("threat signature for asset clamps topn at one", ThreatSignatureForAssetClampsTopNAtOne),
            ("package aggregator picks smaller adequate over remote oversized", PackageAggregatorPicksSmallerAdequateOverRemoteOversized),
            ("package aggregator stops at overshoot guard", PackageAggregatorStopsAtOvershootGuard),
            ("package aggregator emits understrength flag", PackageAggregatorEmitsUnderstrengthFlag),
            ("package aggregator suppresses overmatch reason", PackageAggregatorSuppressesOvermatchReason),
            ("package aggregator deterministic order on tied scores", PackageAggregatorDeterministicOrderOnTiedScores),
            ("cooldown table extends on threat re-detection", CooldownTableExtendsOnThreatRedetection),
            ("cooldown table decrements once per tick", CooldownTableDecrementsOncePerTick),
            ("cooldown table expires at zero", CooldownTableExpiresAtZero),
            ("defense ledger coastal guard forbids cross-map", DefenseLedgerCoastalGuardForbidsCrossMap),
            ("defense ledger minor raid forbids cross-map", DefenseLedgerMinorRaidForbidsCrossMap),
            ("defense ledger decisive landing allows cross-theater", DefenseLedgerDecisiveLandingAllowsCrossTheater),
            ("defense ledger same-theater adequate beats remote oversized", DefenseLedgerSameTheaterAdequateBeatsRemoteOversized),
            ("defense ledger guard budget caps low-value ports", DefenseLedgerGuardBudgetCapsLowValuePorts),
            ("defense ledger active invasion persists through favorable tick", DefenseLedgerActiveInvasionPersistsThroughFavorableTick),
            ("defense ledger recovered threat releases after cooldown", DefenseLedgerRecoveredThreatReleasesAfterCooldown),
            ("defense ledger player cic short-circuits alliance", DefenseLedgerPlayerCicShortCircuitsAlliance),
            ("defense ledger wl subordinate protects only marked unit", DefenseLedgerWlSubordinateProtectsOnlyMarkedUnit),
            ("defense ledger critical-front candidate rejected unless decisive", DefenseLedgerCriticalFrontCandidateRejectedUnlessDecisive),
            ("defense ledger river harbor detects without sif", DefenseLedgerRiverHarborDetectsWithoutSif),
            ("defense ledger raidforce coverage", DefenseLedgerRaidforceCoverage),
            ("defense ledger debug seainvasionsactive off falls back", DefenseLedgerDebugSeainvasionsactiveOffFallsBack),
            ("defense ledger telemetry signature non-empty and posture-prefixed", DefenseLedgerTelemetrySignaturePopulated),
            ("defense telemetry summary compresses response burst", DefenseTelemetrySummaryCompressesResponseBurst),
            ("defense ledger does not crash on europe alliance index", DefenseLedgerDoesNotCrashOnEuropeAllianceIndex),
            ("defense ledger asset proximity stays local and cannot custom order", DefenseLedgerAssetProximityStaysLocalAndCannotCustomOrder),
            ("defense ledger donor theater budget blocks critical front export", DefenseLedgerDonorTheaterBudgetBlocksCriticalFrontExport),
            ("defense ledger formation directive blocks defense movement", DefenseLedgerFormationDirectiveBlocksDefenseMovement),
            ("defense ledger capital defense package is capped", DefenseLedgerCapitalDefensePackageIsCapped),
            ("strategic movement budget blocks area export from hold sector", StrategicMovementBudgetBlocksAreaExportFromHoldSector),
            ("phase truth advances when target accomplished", PhaseTruthAdvancesWhenTargetAccomplished),
            ("phase truth replans when objective unavailable", PhaseTruthReplansWhenObjectiveUnavailable),
            ("phase truth recovers when force below threshold", PhaseTruthRecoversWhenForceBelowThreshold),
            ("phase truth deadline expired advances or replans", PhaseTruthDeadlineExpiredAdvancesOrReplans),
            ("phase truth no contact stays continue", PhaseTruthNoContactStaysContinue),
            ("contact evidence no contact when zero enemy and no battles", ContactEvidenceNoContactWhenZeroEnemyAndNoBattles),
            ("contact evidence enemy reacted on strength rise", ContactEvidenceEnemyReactedOnStrengthRise),
            ("contact evidence skirmish observed near target", ContactEvidenceSkirmishObservedNearTarget),
            ("contact evidence battle observed lost is overmatched", ContactEvidenceBattleObservedLostIsOvermatched),
            ("contact evidence favorable contact requires presence and ratio", ContactEvidenceFavorableRequiresPresenceAndRatio),
            ("persistence dto load tolerates legacy theater commanders field", PersistenceDtoLoadToleratesLegacyTheaterCommanders),
            ("campaign pace too fast collapse on early national morale crash", CampaignPaceTooFastCollapseOnEarlyMoraleCrash),
            ("campaign pace late war pressure on chapter three", CampaignPaceLateWarPressureOnChapterThree),
            ("campaign pace overheated on heavy 14-day battle volume", CampaignPaceOverheatedOnHeavy14DayBattles),
            ("campaign pace too quiet only outside chapter one winter", CampaignPaceTooQuietSuppressedInChapterOneWinter),
            ("campaign pace stalemated when chapter two front static", CampaignPaceStalematedWhenChapterTwoFrontStatic),
            ("campaign pace stable default", CampaignPaceStableDefault),
            ("collapse risk thresholds bound to break morale trigger", CollapseRiskThresholdsBoundToBreakMoraleTrigger),
            ("director cannot publish preserve for late csa under elevated risk", DirectorCannotPublishPreserveForLateCsaUnderElevatedRisk),
            ("campaign pace publishes theater priority from highest pressure theater", CampaignPacePublishesTheaterPriorityFromHighestPressureTheater),
            ("director clamps threshold modifier to half personality delta", DirectorClampsThresholdModifierToHalfPersonalityDelta),
            ("director maps overheated pace to recover-leaning intent", DirectorMapsOverheatedToRecoverLeaning),
            ("director blocks preserve intent for late csa under elevated risk", DirectorBlocksPreserveForLateCsaUnderElevatedRisk),
            ("director memory round trips through dto", DirectorMemoryRoundTripsThroughDto),
            ("cic review plan replans when phase truth says target accomplished", CicReviewPlanReplansWhenPhaseTruthSaysAccomplished),
            ("director publish clamp suppresses second publish in same real second", DirectorPublishClampSuppressesSecondPublishInSameRealSecond),
            ("director raises csa hold ratio under too fast collapse", DirectorRaisesCsaHoldRatioUnderTooFastCollapse),
            ("director raises recover floor under overheated", DirectorRaisesRecoverFloorUnderOverheated),
            ("director relaxes union mass ratio under late war pressure", DirectorRelaxesUnionMassRatioUnderLateWarPressure),
            ("director critical risk strongly favors supply construction", DirectorCriticalRiskFavorsSupplyConstruction),
            ("director too quiet healthy fiscal favors logistics", DirectorTooQuietFavorsLogistics),
            ("director too fast collapse damps expansion", DirectorTooFastCollapseDampsExpansion),
            ("director raises capital defense budget under too fast collapse", DirectorRaisesCapitalDefenseBudgetUnderTooFastCollapse),
            ("director lowers union guard budget under late war pressure", DirectorLowersUnionGuardUnderLateWarPressure)
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

    private static void StrategicCadenceAlternatesFormationByAlliance()
    {
        AssertTrue(StrategicCadencePolicy.ShouldRunAlternatingByAlliance(day: 1, alliance: 1),
            "CSA should run on odd days");
        AssertTrue(!StrategicCadencePolicy.ShouldRunAlternatingByAlliance(day: 1, alliance: 0),
            "Union should skip odd days without a force refresh");
        AssertTrue(StrategicCadencePolicy.ShouldRunAlternatingByAlliance(day: 2, alliance: 0),
            "Union should run on even days");
        AssertTrue(!StrategicCadencePolicy.ShouldRunAlternatingByAlliance(day: 2, alliance: 1),
            "CSA should skip even days without a force refresh");
        AssertTrue(StrategicCadencePolicy.ShouldRunAlternatingByAlliance(day: 2, alliance: 1, forceRefresh: true),
            "force refresh should override alternating cadence");
    }

    private static void StrategicCadenceRefreshesWeeklyOrSourceChange()
    {
        AssertTrue(StrategicCadencePolicy.ShouldRunWeeklyOrSourceChanged(
                day: 1,
                currentSourceSignature: "sig-a",
                previousSourceSignature: null,
                forceRefresh: false),
            "first source observation should run");
        AssertTrue(!StrategicCadencePolicy.ShouldRunWeeklyOrSourceChanged(
                day: 3,
                currentSourceSignature: "sig-a",
                previousSourceSignature: "sig-a",
                forceRefresh: false),
            "stable source before weekly boundary should skip");
        AssertTrue(StrategicCadencePolicy.ShouldRunWeeklyOrSourceChanged(
                day: 3,
                currentSourceSignature: "sig-b",
                previousSourceSignature: "sig-a",
                forceRefresh: false),
            "source change should run immediately");
        AssertTrue(StrategicCadencePolicy.ShouldRunWeeklyOrSourceChanged(
                day: 7,
                currentSourceSignature: "sig-a",
                previousSourceSignature: "sig-a",
                forceRefresh: false),
            "weekly boundary should run even when source is stable");
        AssertTrue(StrategicCadencePolicy.ShouldRunWeeklyOrSourceChanged(
                day: 3,
                currentSourceSignature: "sig-a",
                previousSourceSignature: "sig-a",
                forceRefresh: true),
            "force refresh should run immediately");
    }

    private static void StrategicCadenceStableSourceSkipsDownstreamRebuild()
    {
        AssertTrue(StrategicCadencePolicy.SourceChanged("front:2", "front:1"),
            "different front signatures should count as changed");
        AssertTrue(!StrategicCadencePolicy.SourceChanged("front:1", "front:1"),
            "same front signatures should be stable");
        AssertTrue(StrategicCadencePolicy.SourceChanged("front:1", null),
            "missing previous signature should force first rebuild");
    }

    private static void TacticalTelemetryMapsMacroNames()
    {
        AssertEqual("dynamic", TacticalTelemetry.MacroName(-1), "macro -1");
        AssertEqual("assault", TacticalTelemetry.MacroName(0), "macro 0");
        AssertEqual("attack", TacticalTelemetry.MacroName(1), "macro 1");
        AssertEqual("defend", TacticalTelemetry.MacroName(2), "macro 2");
        AssertEqual("retreat", TacticalTelemetry.MacroName(3), "macro 3");
        AssertEqual("unknown", TacticalTelemetry.MacroName(99), "macro unknown");
    }

    private static void TacticalTelemetrySummaryHandlesNull()
    {
        string summary = TacticalTelemetry.Summary(TacticalObservedEvent.Macro, null);
        AssertContains(summary, "[TacticalMacro]", "prefix");
        AssertContains(summary, "side=-1", "empty side");
        AssertContains(summary, "macro=unknown", "empty macro");
        AssertContains(summary, "sectorSource=none", "empty sector source");
    }

    private static void TacticalTelemetryMapsPlayerOrderPrefix()
    {
        string summary = TacticalTelemetry.Summary(TacticalObservedEvent.PlayerOrder, TacticalBattleContext.Empty());
        AssertContains(summary, "[TacticalPlayerOrder]", "player-order prefix");
    }

    private static void TacticalTelemetrySignatureChangesOnMaterialFields()
    {
        var baseline = new TacticalBattleContext
        {
            Side = 1,
            Alliance = 0,
            MacroAi = -1,
            GroupCount = 4,
            SectorSource = TacticalSectorSource.ObjectiveChain,
            SectorSignature = "chains=2"
        };
        var changed = new TacticalBattleContext
        {
            Side = 1,
            Alliance = 0,
            MacroAi = 1,
            GroupCount = 4,
            SectorSource = TacticalSectorSource.ObjectiveChain,
            SectorSignature = "chains=2"
        };

        string a = TacticalTelemetry.Signature(TacticalObservedEvent.Macro, baseline);
        string b = TacticalTelemetry.Signature(TacticalObservedEvent.Macro, changed);
        if (a == b) throw new Exception("expected tactical signature to change when macro changes");
    }

    private static void TacticalTelemetryThrottleSuppressesRepeatedSignature()
    {
        var emitted = new Dictionary<string, float>();
        bool first = TacticalTelemetry.ShouldEmit(emitted, "macro", "sig", 10f, 30f, verbose: false);
        bool second = TacticalTelemetry.ShouldEmit(emitted, "macro", "sig", 20f, 30f, verbose: false);
        bool third = TacticalTelemetry.ShouldEmit(emitted, "macro", "sig", 41f, 30f, verbose: false);
        if (!first) throw new Exception("expected first tactical signature emit");
        if (second) throw new Exception("expected repeated tactical signature to be throttled");
        if (!third) throw new Exception("expected tactical signature to emit after throttle window");
    }

    private static void TacticalTelemetryDeltaFormatsBeforeAfterCounts()
    {
        string delta = TacticalTelemetry.Delta(
            new TacticalObserverSnapshot { GroupCount = 2, ChargingCount = 0, ReserveGroupCount = 1 },
            new TacticalObserverSnapshot { GroupCount = 2, ChargingCount = 1, ReserveGroupCount = 2 });

        AssertContains(delta, "groups=2->2", "group delta");
        AssertContains(delta, "charging=0->1", "charging delta");
        AssertContains(delta, "reserves=1->2", "reserve delta");
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

    private static void HistoricalRegistryMapsCsaNorthwest()
    {
        var army = HistoricalArmyAreaRegistry.Resolve(1, "Army of the Northwest", "Garnett");
        AssertEqual("NorthwestVirginia", army.PrimaryAreaKey);
        AssertTrue(army.PreferredAreaKeys.Contains("ShenandoahValley"), "expected Shenandoah fallback preference");

        var porterfield = HistoricalArmyAreaRegistry.Resolve(1, "Porterfield's Division", "Porterfield");
        AssertEqual("NorthwestVirginia", porterfield.PrimaryAreaKey);
    }

    private static void HistoricalRegistryLeavesInactiveFullWarArmiesUnassigned()
    {
        AssertEqual("Unassigned", HistoricalArmyAreaRegistry.Resolve(0, "Army of the Tennessee", "Grant").PrimaryAreaKey);
        AssertEqual("Unassigned", HistoricalArmyAreaRegistry.Resolve(1, "Army of Tennessee", "Johnston").PrimaryAreaKey);
        AssertEqual("Unassigned", HistoricalArmyAreaRegistry.Resolve(1, "Army of Mississippi", "Pemberton").PrimaryAreaKey);
        AssertEqual("OhioValley", HistoricalArmyAreaRegistry.Resolve(0, "Army of the Ohio", "McClellan").PrimaryAreaKey);
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

    private static void ArmyAreaLedgerRedirectsCsaNorthwestCommand()
    {
        var ledger = ArmyAreaLedger.Build(new[]
        {
            new ArmyAreaInput
            {
                UnitKey = "northwest",
                AllianceId = 1,
                UnitName = "Army of the Northwest",
                CommanderName = "Garnett",
                CurrentAreaKey = "VirginiaCapitalCorridor",
                Strength = 6000f,
                Readiness = 0.5f
            }
        }, planTargetAreaKey: "VirginiaCapitalCorridor");

        var assignment = ledger.GetAssignment("northwest");
        AssertEqual("NorthwestVirginia", assignment.AssignedAreaKey);
        AssertEqual(true, assignment.OutOfArea);
        AssertEqual(ArmyAreaBehavior.Recover, assignment.Behavior);
    }

    private static void ArmyAreaLedgerLeavesInactiveFullWarArmyInCurrentArea()
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
        AssertEqual("VirginiaCapitalCorridor", assignment.AssignedAreaKey);
        AssertEqual(ArmyAreaBehavior.Hold, assignment.Behavior);
        AssertEqual(false, assignment.OutOfArea);
    }

    private static void ArmyAreaLedgerGivesDynamicFallbackLocalDoctrine()
    {
        var ledger = ArmyAreaLedger.Build(new[]
        {
            new ArmyAreaInput
            {
                UnitKey = "new-corps",
                AllianceId = 1,
                UnitName = "2nd Corps",
                CommanderName = "Jackson",
                CurrentAreaKey = "NorthwestVirginia",
                Strength = 9000f,
                Readiness = 0.6f
            }
        }, planTargetAreaKey: "VirginiaCapitalCorridor");

        var assignment = ledger.GetAssignment("new-corps");
        AssertEqual("csa-dynamic-NorthwestVirginia", assignment.Doctrine.DoctrineId);
        AssertEqual("NorthwestVirginia", assignment.Doctrine.PrimaryAreaKey);
        AssertTrue(assignment.Doctrine.PreferredAreaKeys.Contains("ShenandoahValley"), "expected adjacent active-map preference");
        AssertEqual("NorthwestVirginia", assignment.AssignedAreaKey);
        AssertEqual(false, assignment.OutOfArea);
        AssertEqual(ArmyAreaBehavior.Hold, assignment.Behavior);
    }

    private static void ArmyAreaLedgerLetsDynamicFallbackCounterstrokeLocalPlanArea()
    {
        var ledger = ArmyAreaLedger.Build(new[]
        {
            new ArmyAreaInput
            {
                UnitKey = "new-army",
                AllianceId = 1,
                UnitName = "Provisional Army",
                CommanderName = "Garnett",
                CurrentAreaKey = "NorthwestVirginia",
                Strength = 14000f,
                Readiness = 0.72f
            }
        }, planTargetAreaKey: "NorthwestVirginia");

        var assignment = ledger.GetAssignment("new-army");
        AssertEqual("NorthwestVirginia", assignment.AssignedAreaKey);
        AssertEqual(false, assignment.OutOfArea);
        AssertEqual(ArmyAreaBehavior.Counterstroke, assignment.Behavior);
        AssertEqual("plan-target-dynamic-area", assignment.Reason);
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
                CurrentAreaKey = "OhioValley",
                Strength = 5000f,
                Readiness = 0.75f
            }
        }, planTargetAreaKey: null);

        var assignment = ledger.GetAssignment("division");
        AssertEqual("VirginiaCapitalCorridor", assignment.AssignedAreaKey);
        AssertEqual(true, assignment.OutOfArea);
    }

    private static void BattleHistoryQueryMatchesInsideSpatialAndDateWindow()
    {
        var history = new List<BattleHistoryRecord>
        {
            new BattleHistoryRecord { BattleName = "near", PositionX = 100f, PositionZ = 100f, Day = 5, Month = 6, Year = 1862 }
        };
        int currentDay = 1862 * 372 + 6 * 31 + 8;
        var hits = new List<BattleHistoryRecord>(BattleHistoryQuery.Near(
            history, new UnityEngine.Vector3(105f, 0f, 105f), 50f, currentDay, withinDays: 7));
        AssertEqual(1, hits.Count, "expected 1 in-window hit");
    }

    private static void BattleHistoryQueryRejectsOutsideSpatialWindow()
    {
        var history = new List<BattleHistoryRecord>
        {
            new BattleHistoryRecord { BattleName = "far", PositionX = 1000f, PositionZ = 1000f, Day = 5, Month = 6, Year = 1862 }
        };
        int currentDay = 1862 * 372 + 6 * 31 + 6;
        var hits = new List<BattleHistoryRecord>(BattleHistoryQuery.Near(
            history, new UnityEngine.Vector3(0f, 0f, 0f), 50f, currentDay, withinDays: 7));
        AssertEqual(0, hits.Count, "expected 0 hits beyond spatial window");
    }

    private static void BattleHistoryQueryRejectsOutsideDateWindow()
    {
        var history = new List<BattleHistoryRecord>
        {
            new BattleHistoryRecord { BattleName = "old", PositionX = 100f, PositionZ = 100f, Day = 5, Month = 6, Year = 1862 }
        };
        int currentDay = 1862 * 372 + 7 * 31 + 5; // ~30 days later
        var hits = new List<BattleHistoryRecord>(BattleHistoryQuery.Near(
            history, new UnityEngine.Vector3(105f, 0f, 105f), 50f, currentDay, withinDays: 7));
        AssertEqual(0, hits.Count, "expected 0 hits beyond date window");
    }

    private static void TheaterPressureViewSumsOwnAndEnemyPerTheater()
    {
        var inputs = new List<FrontSectorInput>
        {
            new FrontSectorInput { SectorKey = "RichmondCorridor", Theater = Theater.East, OwnStrength = 8000f, EnemyStrength = 6000f, StrategicImportance = 1f },
            new FrontSectorInput { SectorKey = "ShenandoahValley", Theater = Theater.East, OwnStrength = 2000f, EnemyStrength = 1000f, StrategicImportance = 0.5f },
            new FrontSectorInput { SectorKey = "Vicksburg",        Theater = Theater.West, OwnStrength = 4000f, EnemyStrength = 5000f, StrategicImportance = 1f },
        };
        var ledger = FrontSectorLedger.Build(inputs);

        var view = TheaterPressureView.From(ledger);

        AssertEqual(10000f, view.OwnStrengthByTheater[Theater.East], "east own");
        AssertEqual(7000f,  view.EnemyStrengthByTheater[Theater.East], "east enemy");
        AssertEqual(4000f,  view.OwnStrengthByTheater[Theater.West], "west own");
        AssertEqual(5000f,  view.EnemyStrengthByTheater[Theater.West], "west enemy");
    }

    private static void DailyCadenceFiresOnFirstCallAndDayRolloverOnly()
    {
        var cadence = new DailyCadence();
        AssertTrue(cadence.ShouldFire(1, 6, 1861), "first call should fire");
        AssertTrue(!cadence.ShouldFire(1, 6, 1861), "same day should not fire again");
        AssertTrue(cadence.ShouldFire(2, 6, 1861), "next day should fire");
        AssertTrue(cadence.ShouldFire(1, 7, 1861), "month rollover should fire");
        AssertTrue(cadence.ShouldFire(1, 1, 1862), "year rollover should fire");
    }

    private static void DailyCadenceRejectsInvalidDates()
    {
        var cadence = new DailyCadence();
        AssertTrue(!cadence.ShouldFire(0, 6, 1861), "day 0 should be ignored");
        AssertTrue(!cadence.ShouldFire(1, 0, 1861), "month 0 should be ignored");
        AssertTrue(!cadence.ShouldFire(1, 6, 0), "year 0 should be ignored");
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

    private static void WlDiaryStartupGateDefersUntilReady()
    {
        AssertEqual(
            false,
            WlCareerStartGate.ShouldSkipDiaryEventUpdate(
                dlcScenarioActive: false,
                frame: 50,
                chosenCommanderId: -1,
                chosenCommanderRecordReady: false,
                chosenCommanderHasCommand: false,
                diaryEventsReady: false,
                foodReady: false,
                cardinalPointsReady: false,
                weatherReady: false,
                updateCycle: 0),
            "non-W&L diary updates should remain vanilla-owned");

        AssertEqual(
            true,
            WlCareerStartGate.ShouldSkipDiaryEventUpdate(
                dlcScenarioActive: true,
                frame: 50,
                chosenCommanderId: 12,
                chosenCommanderRecordReady: true,
                chosenCommanderHasCommand: false,
                diaryEventsReady: true,
                foodReady: true,
                cardinalPointsReady: true,
                weatherReady: true,
                updateCycle: 0),
            "W&L diary updates should wait until the player has a command");

        AssertEqual(
            true,
            WlCareerStartGate.ShouldSkipDiaryEventUpdate(
                dlcScenarioActive: true,
                frame: 50,
                chosenCommanderId: 12,
                chosenCommanderRecordReady: true,
                chosenCommanderHasCommand: true,
                diaryEventsReady: false,
                foodReady: true,
                cardinalPointsReady: true,
                weatherReady: true,
                updateCycle: 0),
            "W&L diary updates should wait for imported diary events");

        AssertEqual(
            true,
            WlCareerStartGate.ShouldSkipDiaryEventUpdate(
                dlcScenarioActive: true,
                frame: 50,
                chosenCommanderId: 12,
                chosenCommanderRecordReady: true,
                chosenCommanderHasCommand: true,
                diaryEventsReady: true,
                foodReady: false,
                cardinalPointsReady: true,
                weatherReady: true,
                updateCycle: 3),
            "W&L diary updates should wait for food data before food-quality event cycles");

        AssertEqual(
            true,
            WlCareerStartGate.ShouldSkipDiaryEventUpdate(
                dlcScenarioActive: true,
                frame: 50,
                chosenCommanderId: 12,
                chosenCommanderRecordReady: true,
                chosenCommanderHasCommand: true,
                diaryEventsReady: true,
                foodReady: true,
                cardinalPointsReady: true,
                weatherReady: false,
                updateCycle: 1),
            "weather cycle should wait for WeatherObj");

        AssertEqual(
            false,
            WlCareerStartGate.ShouldSkipDiaryEventUpdate(
                dlcScenarioActive: true,
                frame: 50,
                chosenCommanderId: 12,
                chosenCommanderRecordReady: true,
                chosenCommanderHasCommand: true,
                diaryEventsReady: true,
                foodReady: true,
                cardinalPointsReady: true,
                weatherReady: false,
                updateCycle: 2),
            "non-weather cycles should not require WeatherObj once core dependencies are ready");
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

    private static void WlStartSelectionRetryBlocksEarlyReadyData()
    {
        var gate = new WlStartSelectionRetryGate(maxAttempts: 3, retryEveryUnityFrames: 15, minReadyCampaignFrame: 50);

        AssertEqual(false, gate.ShouldAttempt(pending: true, listVisible: false, panelAvailable: true, campaignFrame: 49, startupDataReady: false, unityFrame: 1));
        AssertEqual(0, gate.Attempts);
        AssertEqual(false, gate.ShouldAttempt(pending: true, listVisible: false, panelAvailable: true, campaignFrame: 49, startupDataReady: true, unityFrame: 16));
        AssertEqual(0, gate.Attempts);
        AssertEqual(true, gate.ShouldAttempt(pending: true, listVisible: false, panelAvailable: true, campaignFrame: 50, startupDataReady: true, unityFrame: 31));
        AssertEqual(1, gate.Attempts);
    }

    private static void WlDispatchSanitizerFixesType56StanceNone()
    {
        var result = WlDispatchSanitizer.Sanitize(56, "I will carry on according to your instructions that are to none.");
        AssertEqual("I will hold position and await further instructions.", result.Content);
        AssertEqual(true, result.Changed);
        AssertTrue(!result.Content.Contains("to none"), "type 56 sanitized content must not contain stance none fragment");
    }

    private static void WlDispatchSanitizerFixesType57StanceNone()
    {
        var result = WlDispatchSanitizer.Sanitize(57, "I will carry on according to your instructions that are to none.");
        AssertEqual("I will hold position and await further instructions.", result.Content);
        AssertEqual(true, result.Changed);
        AssertTrue(!result.Content.Contains("to none"), "type 57 sanitized content must not contain stance none fragment");
    }

    private static void WlDispatchSanitizerFixesType15NoOrdersNone()
    {
        AssertEqual(true, WlDispatchSanitizer.IsCandidateType(15));

        var result = WlDispatchSanitizer.Sanitize(15, "I will none if no other orders are received");
        AssertEqual("I will hold position if no other orders are received", result.Content);
        AssertEqual(true, result.Changed);
        AssertTrue(!result.Content.Contains("will none"), "type 15 sanitized content must not contain no-orders none fragment");
    }

    private static void WlDispatchSanitizerIgnoresNonCandidateType()
    {
        const string content = "I will carry on according to your instructions that are to none.";
        AssertEqual(false, WlDispatchSanitizer.IsCandidateType(99));

        var result = WlDispatchSanitizer.Sanitize(99, content);
        AssertEqual(content, result.Content);
        AssertEqual(false, result.Changed);
    }

    private static void WlDispatchSanitizerHandlesNullContent()
    {
        var result = WlDispatchSanitizer.Sanitize(56, null);
        AssertEqual<string>(null, result.Content);
        AssertEqual(false, result.Changed);
    }

    private static void WlDispatchSanitizerLeavesNormalContentUnchanged()
    {
        const string content = "I will advance at once.";
        var result = WlDispatchSanitizer.Sanitize(56, content);
        AssertEqual(content, result.Content);
        AssertEqual(false, result.Changed);
    }

    private static void WlBridgeInactiveAllowsDirectMovement()
    {
        var decision = WlStrategicOrderBridge.Classify(
            WlStrategicIntent.Redeploy,
            new WlStrategicRoleFacts { WlActive = false, IsPlayerAlliance = true });

        AssertEqual(WlStrategicOrderResult.NotWl, decision.Result);
        AssertEqual(5, decision.WlOrderType);
        AssertEqual(true, decision.MayDirectMove);
        AssertEqual(true, decision.MayMutateOperationList);
    }

    private static void WlBridgeNonPlayerAllianceAllowsDirectMovement()
    {
        var decision = WlStrategicOrderBridge.Classify(
            WlStrategicIntent.Redeploy,
            new WlStrategicRoleFacts(wlActive: true, isPlayerAlliance: false));

        AssertEqual(WlStrategicOrderResult.DirectMovementAllowed, decision.Result);
        AssertEqual(true, decision.MayDirectMove);
        AssertEqual(true, decision.MayMutateOperationList);
    }

    private static void WlBridgeReportOnlyUnderWlPlayerAllianceBlocksMovement()
    {
        var decision = WlStrategicOrderBridge.Classify(
            WlStrategicIntent.ReportOnly,
            new WlStrategicRoleFacts(wlActive: true, isPlayerAlliance: true));

        AssertEqual(WlStrategicOrderResult.ReportOnly, decision.Result);
        AssertEqual(-1, decision.WlOrderType);
        AssertEqual(false, decision.MayDirectMove);
        AssertEqual(false, decision.MayMutateOperationList);
    }

    private static void WlBridgeReportOnlyInactiveStaysNotWl()
    {
        var decision = WlStrategicOrderBridge.Classify(
            WlStrategicIntent.ReportOnly,
            new WlStrategicRoleFacts(wlActive: false, isPlayerAlliance: true));

        AssertEqual(WlStrategicOrderResult.NotWl, decision.Result);
        AssertEqual(true, decision.MayDirectMove);
        AssertEqual(true, decision.MayMutateOperationList);
    }

    private static void WlBridgeReportOnlyNonPlayerAllianceStaysDirect()
    {
        var decision = WlStrategicOrderBridge.Classify(
            WlStrategicIntent.ReportOnly,
            new WlStrategicRoleFacts(wlActive: true, isPlayerAlliance: false));

        AssertEqual(WlStrategicOrderResult.DirectMovementAllowed, decision.Result);
        AssertEqual(true, decision.MayDirectMove);
        AssertEqual(true, decision.MayMutateOperationList);
    }

    private static void WlBridgePlayerCicSkipsMovement()
    {
        var decision = WlStrategicOrderBridge.Classify(
            WlStrategicIntent.Offensive,
            new WlStrategicRoleFacts { WlActive = true, IsPlayerAlliance = true, IsPlayerCic = true });

        AssertEqual(WlStrategicOrderResult.SkippedPlayerCic, decision.Result);
        AssertEqual(16, decision.WlOrderType);
        AssertEqual(false, decision.MayDirectMove);
        AssertEqual(false, decision.MayMutateOperationList);
    }

    private static void WlBridgeMovedByPlayerSkipsMovement()
    {
        var decision = WlStrategicOrderBridge.Classify(
            WlStrategicIntent.Probe,
            new WlStrategicRoleFacts { WlActive = true, IsPlayerAlliance = true, IsMovedByPlayer = true });

        AssertEqual(WlStrategicOrderResult.SkippedPlayerControlled, decision.Result);
        AssertEqual(5, decision.WlOrderType);
        AssertEqual(false, decision.MayDirectMove);
        AssertEqual(false, decision.MayMutateOperationList);
    }

    private static void WlBridgeEligibleUnderCommanderIssuesCurrentOrder()
    {
        var facts = new WlStrategicRoleFacts
        {
            WlActive = true,
            IsPlayerAlliance = true,
            IsUnderCommander = true,
            CurrentCommandIsCampaignGroup = true,
            CurrentCommandParentIsUnderTargetUnit = true
        };

        var decision = WlStrategicOrderBridge.Classify(WlStrategicIntent.OffensiveContinuation, facts);

        AssertEqual(WlStrategicOrderResult.IssuedWlCurrentOrder, decision.Result);
        AssertEqual(6, decision.WlOrderType);
        AssertEqual(false, decision.MayDirectMove);
        AssertEqual(false, decision.MayMutateOperationList);
    }

    private static void WlBridgeIneligibleUnderCommanderBlocksDirectFallback()
    {
        var facts = new WlStrategicRoleFacts
        {
            WlActive = true,
            IsPlayerAlliance = true,
            IsUnderCommander = true,
            CurrentCommandIsCampaignGroup = false,
            CurrentCommandParentIsUnderTargetUnit = true
        };

        var decision = WlStrategicOrderBridge.Classify(WlStrategicIntent.EngageEnemy, facts);

        AssertEqual(WlStrategicOrderResult.WlCurrentOrderIneligible, decision.Result);
        AssertEqual(7, decision.WlOrderType);
        AssertEqual(false, decision.MayDirectMove);
        AssertEqual(false, decision.MayMutateOperationList);
    }

    private static void WlBridgeFailedVanillaCallBlocksDirectFallback()
    {
        var facts = new WlStrategicRoleFacts
        {
            WlActive = true,
            IsPlayerAlliance = true,
            IsUnderCommander = true,
            CurrentCommandIsCampaignGroup = true,
            CurrentCommandParentIsUnderTargetUnit = true
        };

        var decision = WlStrategicOrderBridge.Classify(WlStrategicIntent.DefendCapital, facts, vanillaBridgeSucceeded: false);

        AssertEqual(WlStrategicOrderResult.FailedVanillaBridge, decision.Result);
        AssertEqual(8, decision.WlOrderType);
        AssertEqual(false, decision.MayDirectMove);
        AssertEqual(false, decision.MayMutateOperationList);
    }

    private static void WlBridgePartOfPlayerUnitNotUnderCommanderStaysDirectForC0c()
    {
        var decision = WlStrategicOrderBridge.Classify(
            WlStrategicIntent.ConstructFort,
            new WlStrategicRoleFacts
            {
                WlActive = true,
                IsPlayerAlliance = true,
                IsPartOfPlayerUnit = true
            });

        AssertEqual(WlStrategicOrderResult.DirectMovementAllowed, decision.Result);
        AssertEqual(9, decision.WlOrderType);
        AssertEqual(true, decision.MayDirectMove);
        AssertEqual(true, decision.MayMutateOperationList);
        AssertTrue(decision.Reason.Contains("part-of-player-unit"), "C0c direct fallback reason should name part-of-player-unit");
    }

    private static void WlCampShortCampCreditsNormalRest()
    {
        var corrected = new float[1];
        float minimumTotal;
        bool changed = WlCampRealism.TryCorrectShortCampMinimumCredits(
            2f, new[] { 3f }, corrected, out minimumTotal);
        AssertTrue(changed, "expected correction for 2h actual below 3h minimum");
        AssertNear(3f, minimumTotal, 0.0001f, "minimum total");
        AssertNear(2f, corrected[0], 0.0001f, "rest credit");
    }

    private static void WlCampShortCampCreditsWoundedRest()
    {
        var corrected = new float[1];
        float minimumTotal;
        bool changed = WlCampRealism.TryCorrectShortCampMinimumCredits(
            2f, new[] { 9f }, corrected, out minimumTotal);
        AssertTrue(changed, "expected correction for 2h actual below 9h wounded rest minimum");
        AssertNear(9f, minimumTotal, 0.0001f, "minimum total");
        AssertNear(2f, corrected[0], 0.0001f, "wounded rest credit");
    }

    private static void WlCampShortCampCreditsPreserveMinimumProportions()
    {
        var corrected = new float[4];
        float minimumTotal;
        bool changed = WlCampRealism.TryCorrectShortCampMinimumCredits(
            3f, new[] { 3f, 1f, 2f, 0f }, corrected, out minimumTotal);
        AssertTrue(changed, "expected correction for 3h actual below 6h minimum");
        AssertNear(6f, minimumTotal, 0.0001f, "minimum total");
        AssertNear(1.5f, corrected[0], 0.0001f, "station 0");
        AssertNear(0.5f, corrected[1], 0.0001f, "station 1");
        AssertNear(1.0f, corrected[2], 0.0001f, "station 2");
        AssertNear(0f, corrected[3], 0.0001f, "station 3");
        AssertNear(3f, corrected[0] + corrected[1] + corrected[2] + corrected[3], 0.0001f, "sum");
    }

    private static void WlCampShortCampEnoughTimeNoCorrection()
    {
        var corrected = new[] { -99f };
        float minimumTotal;
        bool changed = WlCampRealism.TryCorrectShortCampMinimumCredits(
            3f, new[] { 3f }, corrected, out minimumTotal);
        AssertTrue(!changed, "expected no correction when actual covers minimum");
        AssertNear(3f, minimumTotal, 0.0001f, "minimum total");
        AssertNear(-99f, corrected[0], 0.0001f, "sentinel unchanged");
    }

    private static void WlCampShortCampZeroMinimumNoCorrection()
    {
        var corrected = new[] { -99f, -88f };
        float minimumTotal;
        bool changed = WlCampRealism.TryCorrectShortCampMinimumCredits(
            2f, new[] { 0f, 0f }, corrected, out minimumTotal);
        AssertTrue(!changed, "expected no correction when minimum total is zero");
        AssertNear(0f, minimumTotal, 0.0001f, "minimum total");
        AssertNear(-99f, corrected[0], 0.0001f, "sentinel 0 unchanged");
        AssertNear(-88f, corrected[1], 0.0001f, "sentinel 1 unchanged");
    }

    private static void WlCampResponsiveBonusWeightsRecentIncludedStation()
    {
        float vanilla = (56f / 30f - 3f) / 5f;
        float result = WlCampRealism.ComputeResponsiveBonus(
            6, true, vanilla, 56f / 30f, 0f,
            new[] { 8f, 8f, 8f, 8f, 8f, 8f, 8f },
            new float[0],
            3f, 8f, 7, 0.35f);
        AssertTrue(result > vanilla, "responsive bonus should exceed long-average vanilla");
        AssertNear(0.202666f, result, 0.0005f, "responsive bonus");
    }

    private static void WlCampResponsiveBonusIncludesCompanionRecentAverage()
    {
        float vanilla = 0f;
        float result = WlCampRealism.ComputeResponsiveBonus(
            1, true, vanilla, 2f, 0f,
            new[] { 2f, 2f, 2f, 2f, 2f, 2f, 2f },
            new[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f },
            2f, 6f, 7, 0.35f);
        AssertTrue(result > vanilla, "recent companion time should lift bonus");
        AssertNear(0.0875f, result, 0.0005f, "companion responsive bonus");
    }

    private static void WlCampResponsiveBonusPartialCompanionHistoryDividesByWindow()
    {
        float result = WlCampRealism.ComputeResponsiveBonus(
            1, true, 0f, 2f, 0f,
            new float[0],
            new[] { 4f },
            2f, 6f, 4, 0.5f);
        AssertNear(-0.125f, result, 0.0005f, "partial companion history responsive bonus");
    }

    private static void WlCampResponsiveBonusExcludedStationsStayVanilla()
    {
        foreach (var stationId in new[] { 2, 5, 9, 12 })
        {
            float result = WlCampRealism.ComputeResponsiveBonus(
                stationId, true, 0.42f, 0f, 0f,
                new[] { 99f, 99f, 99f, 99f, 99f, 99f, 99f },
                new[] { 99f, 99f, 99f, 99f, 99f, 99f, 99f },
                0f, 1f, 7, 0.35f);
            AssertNear(0.42f, result, 0.0001f, "excluded station " + stationId);
        }
    }

    private static void WlCampResponsiveBonusUseAverageFalseStaysVanilla()
    {
        float result = WlCampRealism.ComputeResponsiveBonus(
            6, false, -0.25f, 0f, 0f,
            new[] { 99f, 99f, 99f, 99f, 99f, 99f, 99f },
            new float[0],
            0f, 1f, 7, 0.35f);
        AssertNear(-0.25f, result, 0.0001f, "useaverage=false should stay vanilla");
    }

    private static void WlCampResponsiveBonusNonfiniteInputStaysBounded()
    {
        float result = WlCampRealism.ComputeResponsiveBonus(
            6, true, float.PositiveInfinity, float.NaN, float.NegativeInfinity,
            new[] { 8f, float.NaN, float.PositiveInfinity },
            new[] { float.NegativeInfinity, 4f },
            float.NaN, float.PositiveInfinity, 7, float.NaN);
        AssertNear(0f, result, 0.0001f, "nonfinite responsive bonus fallback");
    }

    private static void WlCampRestRewardCapMakesSixHoursFullReward()
    {
        AssertTrue(WlCampRealism.UsesRestRewardCap(12), "station 12 should be Rest");
        AssertNear(3f, WlCampRealism.DefaultRestNeutralHours, 0.0001f, "default rest neutral");
        AssertNear(6f, WlCampRealism.DefaultRestMaxRewardHours, 0.0001f, "default rest max reward");
        float resultAtSix = WlCampRealism.ComputeRestRewardBonus(
            12, 0f, 6f, 0f,
            6f, 9f, WlCampRealism.DefaultRestNeutralHours, WlCampRealism.DefaultRestMaxRewardHours);
        float resultAtFourAndHalf = WlCampRealism.ComputeRestRewardBonus(
            12, 0f, 4.5f, 0f,
            6f, 9f, WlCampRealism.DefaultRestNeutralHours, WlCampRealism.DefaultRestMaxRewardHours);

        AssertNear(1f, resultAtSix, 0.0001f, "six-hour rest bonus");
        AssertNear(0.5f, resultAtFourAndHalf, 0.0001f, "halfway rest bonus");
    }

    private static void WlCampRestRewardCapLeavesNonRestStationsVanilla()
    {
        AssertTrue(!WlCampRealism.UsesRestRewardCap(6), "station 6 should not be Rest");
        float result = WlCampRealism.ComputeRestRewardBonus(
            6, 0.42f, 5f, 0f,
            1.75f, 5f, 3f, 5f);
        AssertNear(0.42f, result, 0.0001f, "non-rest vanilla fallback");
    }

    private static void WlCampRestRewardCapInvalidConfigFallsBack()
    {
        float result = WlCampRealism.ComputeRestRewardBonus(
            12, -0.333333f, 5f, 0f,
            6f, 9f, 5f, 5f);
        AssertNear(-0.333333f, result, 0.0001f, "invalid neutral/max fallback");
    }

    private static void WlCampUnitDivisorClampsInvalidCachedCounts()
    {
        AssertNear(1f, WlCampRealism.EffectiveCommandedUnitDivisor(-1, 0.5f), 0.0001f, "count -1");
        AssertNear(1f, WlCampRealism.EffectiveCommandedUnitDivisor(0, 0.5f), 0.0001f, "count 0");
        AssertNear(1f, WlCampRealism.EffectiveCommandedUnitDivisor(1, 0.5f), 0.0001f, "count 1");
    }

    private static void WlCampUnitDivisorDefaultPowerSoftensFourAndNineUnits()
    {
        AssertNear(2f, WlCampRealism.EffectiveCommandedUnitDivisor(4, 0.5f), 0.0001f, "count 4 divisor");
        AssertNear(3f, WlCampRealism.EffectiveCommandedUnitDivisor(9, 0.5f), 0.0001f, "count 9 divisor");
        AssertNear(1.5f, WlCampRealism.ComputeUnitPayoffModifier(6, true, 1.25f, 1f, 1f, 4, 0.5f), 0.0001f, "count 4 modifier");
        AssertNear(1.333333f, WlCampRealism.ComputeUnitPayoffModifier(6, true, 1.111f, 1f, 1f, 9, 0.5f), 0.0005f, "count 9 modifier");
    }

    private static void WlCampUnitModifierClampsNegativeToZero()
    {
        float result = WlCampRealism.ComputeUnitPayoffModifier(7, true, 1f, -1f, 1000f, 1, 0.5f);
        AssertNear(0f, result, 0.0001f, "negative modifier clamp");
    }

    private static void WlCampUnitModifierNonfiniteInputFallsBack()
    {
        AssertNear(0.66f, WlCampRealism.ComputeUnitPayoffModifier(7, true, 0.66f, float.NaN, 1f, 4, 0.5f), 0.0001f, "nan bonus fallback");
        AssertNear(0f, WlCampRealism.ComputeUnitPayoffModifier(7, true, float.PositiveInfinity, 1f, 1f, 4, 0.5f), 0.0001f, "nonfinite vanilla fallback");
        AssertNear(0.66f, WlCampRealism.ComputeUnitPayoffModifier(7, true, 0.66f, 1f, float.PositiveInfinity, 4, 0.5f), 0.0001f, "infinite max fallback");
    }

    private static void WlCampUnitPowerOneIsVanillaEquivalent()
    {
        AssertNear(9f, WlCampRealism.EffectiveCommandedUnitDivisor(9, 1.0f), 0.0001f, "power one divisor");
        float result = WlCampRealism.ComputeUnitPayoffModifier(8, true, 0f, 1f, 1f, 9, 1.0f);
        AssertNear(1.111111f, result, 0.0005f, "power one modifier");
    }

    private static void WlCampUnitPayoffExcludedOrUndividedReturnsVanilla()
    {
        AssertNear(0.77f, WlCampRealism.ComputeUnitPayoffModifier(5, true, 0.77f, 1f, 1f, 9, 0.5f), 0.0001f, "station 5 excluded");
        AssertNear(0.77f, WlCampRealism.ComputeUnitPayoffModifier(9, true, 0.77f, 1f, 1f, 9, 0.5f), 0.0001f, "station 9 excluded");
        AssertNear(0.77f, WlCampRealism.ComputeUnitPayoffModifier(12, true, 0.77f, 1f, 1f, 9, 0.5f), 0.0001f, "station 12 excluded");
        AssertNear(0.77f, WlCampRealism.ComputeUnitPayoffModifier(6, false, 0.77f, 1f, 1f, 9, 0.5f), 0.0001f, "undivided included station");
    }

    private static void WlCampShortCampNonfiniteInputNoCorrection()
    {
        var corrected = new[] { -99f, -88f };
        float minimumTotal;
        bool changed = WlCampRealism.TryCorrectShortCampMinimumCredits(
            float.NaN, new[] { 3f, float.PositiveInfinity }, corrected, out minimumTotal);
        AssertTrue(!changed, "expected no correction for nonfinite actual camp hours");
        AssertNear(3f, minimumTotal, 0.0001f, "finite minimum total");
        AssertNear(-99f, corrected[0], 0.0001f, "sentinel 0 unchanged");
        AssertNear(-88f, corrected[1], 0.0001f, "sentinel 1 unchanged");
    }

    private static void AssertNearRejectsNonfiniteValues()
    {
        AssertThrows(() => AssertNear(float.NaN, float.NaN, 0.0001f, "nan pair"), "nan pair");
        AssertThrows(() => AssertNear(float.PositiveInfinity, float.PositiveInfinity, 0.0001f, "infinity pair"), "infinity pair");
        AssertThrows(() => AssertNear(0f, float.NegativeInfinity, 0.0001f, "actual infinity"), "actual infinity");
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

        var northwest = ArmyGroupDoctrine.ResolveCommanderPreference(1, "NorthwestVirginia");
        AssertEqual("Garnett", northwest.PreferredLastNames[0]);
        AssertTrue(northwest.PreferredLastNames.Contains("Porterfield"), "expected Porterfield fallback");
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
        foreach (var id in new[] { 3, 4, 29, 30, 31, 32, 33, 34, 35, 36, 37 })
            AssertTrue(ObjectiveCatalog.TryResolve(id, out _), "expected objective metadata for ID " + id);

        AssertTrue(ObjectiveCatalog.TryResolve(3, out var richmond), "expected Richmond objective metadata");
        AssertEqual(Theater.East, richmond.Theater);
        AssertEqual(Category.CapitalThreat, richmond.Category);
        AssertEqual(false, richmond.IsDerived);
        AssertTrue(richmond.HasTag(StrategyTag.CapitalThreat), "Richmond should carry capital threat");
        AssertTrue(richmond.HasTag(StrategyTag.CapitalDefense), "Richmond should carry capital defense");

        AssertTrue(!ObjectiveCatalog.TryResolve(17, out _), "W&L active map should not hardcode Mississippi River objective metadata");

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

    private static void TheaterClassifierMapsWlCapitalsToEast()
    {
        AssertEqual(Theater.East, TheaterClassifier.FromPosition(1263f, -1010f));
        AssertEqual(Theater.East, TheaterClassifier.FromPosition(1350f, -631f));
    }

    private static void ArmyAreaClassifierMapsWlNorthwestVirginiaTowns()
    {
        AssertEqual("NorthwestVirginia", ArmyAreaClassifier.FromPosition(703f, -518f));
        AssertEqual("NorthwestVirginia", ArmyAreaClassifier.FromPosition(740f, -653f));
        AssertEqual("NorthwestVirginia", ArmyAreaClassifier.FromPosition(702f, -696f));
        AssertEqual("OhioValley", ArmyAreaClassifier.FromPosition(552f, -310f));
    }

    private static void TheaterClassifierUsesStateNamesBeforeCoordinates()
    {
        AssertEqual(Theater.East, TheaterClassifier.FromStateNameOrPosition("Virginia", 1263f, -1010f));
        AssertEqual(Theater.Coast, TheaterClassifier.FromStateNameOrPosition("North Carolina", 998f, -1492f));
        AssertEqual(Theater.Unknown, TheaterClassifier.FromStateName("Mississippi"));
        AssertEqual(Theater.Unknown, TheaterClassifier.FromStateName("Alabama"));
    }

    private static void CampaignMapLedgerOnlyMapsRepresentedStates()
    {
        var ledger = CampaignMapLedger.Build(new[]
        {
            new CampaignMapTown
            {
                CityName = "Richmond",
                StateId = 38,
                StateName = "Virginia",
                Owner = 1,
                OriginalOwner = 1,
                IsCapital = true,
                X = 1263f,
                Z = -1010f,
                CitySize = 0.69f,
                RepresentingPopulation = 112919f
            },
            new CampaignMapTown
            {
                CityName = "Columbus",
                StateId = 29,
                StateName = "Ohio",
                Owner = 0,
                OriginalOwner = 0,
                X = 55f,
                Z = -340f,
                CitySize = 0.58f,
                RepresentingPopulation = 50000f
            }
        });

        AssertEqual(Theater.East, ledger.GetStateTheaterOrUnknown(38));
        AssertEqual(Theater.West, ledger.GetStateTheaterOrUnknown(29));
        AssertEqual(Theater.Unknown, ledger.GetStateTheaterOrUnknown(21));
    }

    private static void CampaignMapLedgerRanksOwnedCapitalsForDefense()
    {
        var ledger = CampaignMapLedger.Build(new[]
        {
            new CampaignMapTown
            {
                CityName = "Richmond",
                StateId = 38,
                StateName = "Virginia",
                Owner = 1,
                OriginalOwner = 1,
                IsCapital = true,
                X = 1263f,
                Z = -1010f,
                CitySize = 0.69f,
                RepresentingPopulation = 112919f
            },
            new CampaignMapTown
            {
                CityName = "Norfolk",
                StateId = 38,
                StateName = "Virginia",
                Owner = 1,
                OriginalOwner = 1,
                X = 1515f,
                Z = -1199f,
                CitySize = 0.55f,
                RepresentingPopulation = 112919f
            }
        });

        AssertEqual("Richmond", ledger.BestDefenseTown(1).CityName);
        AssertTrue(ledger.TryGetTown("Norfolk", out var norfolk), "expected Norfolk in map ledger");
        AssertTrue((norfolk.Roles & CampaignTownRole.MajorCity) != 0, "Norfolk should be tagged as major city");
    }

    private static void CampaignMapLedgerTracksPortsAndForts()
    {
        var ledger = CampaignMapLedger.Build(
            new[]
            {
                new CampaignMapTown
                {
                    CityName = "Richmond",
                    StateId = 38,
                    StateName = "Virginia",
                    Owner = 1,
                    OriginalOwner = 1,
                    IsCapital = true,
                    X = 1263f,
                    Z = -1010f
                }
            },
            new[]
            {
                new CampaignMapAsset
                {
                    Kind = CampaignMapAssetKind.SeaHarbor,
                    Name = "Norfolk",
                    StateId = 38,
                    StateName = "Virginia",
                    Owner = 1,
                    X = 1515f,
                    Z = -1199f,
                    Capacity = 100f
                },
                new CampaignMapAsset
                {
                    Kind = CampaignMapAssetKind.Fort,
                    Name = "Fort Monroe",
                    StateId = 38,
                    StateName = "Virginia",
                    Owner = 0,
                    X = 1550f,
                    Z = -1220f,
                    Level = 2
                }
            });

        AssertEqual(2, ledger.Assets.Count);
        AssertEqual(CampaignMapAssetKind.SeaHarbor, ledger.Assets[0].Kind);
        AssertEqual(Theater.East, ledger.Assets[1].Theater);
        AssertTrue(ledger.Summary().Contains("forts=1"), "summary should include fort count");
    }

    private static void DefenseForceSizerAvoidsOversizedArmyForSmallThreat()
    {
        float division = DefenseForceSizer.ScoreCandidate(
            activeStrength: 6000f,
            morale: 0.9f,
            readinessStep: 2f,
            distance: 220f,
            desiredStrength: 4500f,
            inOffensiveOperation: false,
            caution: 0.2f,
            aggression: 0f);
        float army = DefenseForceSizer.ScoreCandidate(
            activeStrength: 22000f,
            morale: 0.9f,
            readinessStep: 2f,
            distance: 80f,
            desiredStrength: 4500f,
            inOffensiveOperation: false,
            caution: 0.2f,
            aggression: 0f);

        AssertTrue(division < army, "smaller sufficient force should beat nearby oversized army");
    }

    private static void DefenseForceSizerAcceptsLargeForceForLargeThreat()
    {
        float division = DefenseForceSizer.ScoreCandidate(
            activeStrength: 6000f,
            morale: 0.9f,
            readinessStep: 2f,
            distance: 140f,
            desiredStrength: 18000f,
            inOffensiveOperation: false,
            caution: 0.2f,
            aggression: 0f);
        float army = DefenseForceSizer.ScoreCandidate(
            activeStrength: 22000f,
            morale: 0.9f,
            readinessStep: 2f,
            distance: 160f,
            desiredStrength: 18000f,
            inOffensiveOperation: false,
            caution: 0.2f,
            aggression: 0f);

        AssertTrue(army < division, "large threat should allow the larger force");
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

    private static void RecruitmentIntentProtectsThreatenedPriorityArea()
    {
        var intent = new RecruitmentIntent
        {
            AllianceId = 1,
            PreferredTheater = Theater.East,
            ProtectedAreaKey = "VirginiaCapitalCorridor",
            ProtectedAreaThreatLevel = 0.8f,
            ProtectedAreaThreatThreshold = 0.35f,
            StrengthRatio = 0.85f,
            OwnStateSupportFloor = 0.5f
        };
        var candidates = new[]
        {
            new RecruitmentStateCandidate
            {
                StateId = 29,
                Theater = Theater.East,
                AreaKey = "NorthwestVirginia",
                Volunteers = 9000,
                Drafts = 0,
                Support = 0.9f,
                IsRecruitable = true,
                IsEnemyState = false,
                IsLocalArea = true
            },
            new RecruitmentStateCandidate
            {
                StateId = 38,
                Theater = Theater.East,
                AreaKey = "VirginiaCapitalCorridor",
                Volunteers = 6000,
                Drafts = 0,
                Support = 0.85f,
                IsRecruitable = true,
                IsEnemyState = false,
                IsLocalArea = false
            }
        };

        var decision = RecruitmentIntentLedger.SelectState(intent, candidates, vanillaStateId: 29, strengthNeeded: 5000, excludeEnemyStates: false);

        AssertEqual(true, decision.ShouldReplace);
        AssertEqual(38, decision.StateId);
        AssertEqual("protected-area-volunteers", decision.Reason);
    }

    private static void RecruitmentIntentIgnoresPriorityAreaWithoutThreat()
    {
        var intent = new RecruitmentIntent
        {
            AllianceId = 1,
            PreferredTheater = Theater.East,
            ProtectedAreaKey = "VirginiaCapitalCorridor",
            ProtectedAreaThreatLevel = 0.1f,
            ProtectedAreaThreatThreshold = 0.35f,
            StrengthRatio = 0.85f,
            OwnStateSupportFloor = 0.5f
        };
        var candidates = new[]
        {
            new RecruitmentStateCandidate
            {
                StateId = 29,
                Theater = Theater.East,
                AreaKey = "NorthwestVirginia",
                Volunteers = 9000,
                Drafts = 0,
                Support = 0.9f,
                IsRecruitable = true,
                IsEnemyState = false,
                IsLocalArea = true
            },
            new RecruitmentStateCandidate
            {
                StateId = 38,
                Theater = Theater.East,
                AreaKey = "VirginiaCapitalCorridor",
                Volunteers = 6000,
                Drafts = 0,
                Support = 0.85f,
                IsRecruitable = true,
                IsEnemyState = false,
                IsLocalArea = false
            }
        };

        var decision = RecruitmentIntentLedger.SelectState(intent, candidates, vanillaStateId: 29, strengthNeeded: 5000, excludeEnemyStates: false);

        AssertEqual(false, decision.ShouldReplace);
        AssertEqual(29, decision.StateId);
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

    private static void ProjectDoctrineCatalogMapsAllActiveRows()
    {
        var expectedIds = new HashSet<int>();
        for (int id = 0; id <= 19; id++)
            expectedIds.Add(id);
        for (int id = 30; id <= 41; id++)
            expectedIds.Add(id);
        for (int id = 88; id <= 124; id++)
            expectedIds.Add(id);

        var actualIds = new HashSet<int>();
        foreach (var entry in WhiskeyRealism.Strategic.Projects.ProjectDoctrineCatalog.AllActive)
        {
            AssertEqual(true, expectedIds.Contains(entry.ProjectId));
            AssertEqual(true, actualIds.Add(entry.ProjectId));
        }

        AssertEqual(69, WhiskeyRealism.Strategic.Projects.ProjectDoctrineCatalog.AllActive.Count);
        AssertEqual(expectedIds.Count, actualIds.Count);
        foreach (int expectedId in expectedIds)
        {
            AssertEqual(true, actualIds.Contains(expectedId));
            AssertEqual(true, WhiskeyRealism.Strategic.Projects.ProjectDoctrineCatalog.TryGet(expectedId, out var entry));
            AssertEqual(expectedId, entry.ProjectId);
        }

        AssertEqual(true, WhiskeyRealism.Strategic.Projects.ProjectDoctrineCatalog.IsInactiveProjectId(20));
        AssertEqual(true, WhiskeyRealism.Strategic.Projects.ProjectDoctrineCatalog.IsInactiveProjectId(87));
        AssertEqual(false, WhiskeyRealism.Strategic.Projects.ProjectDoctrineCatalog.IsInactiveProjectId(88));
    }

    private static void ProjectDoctrineCatalogEntriesAreImmutable()
    {
        var entryType = typeof(WhiskeyRealism.Strategic.Projects.ProjectDoctrineEntry);
        AssertEqual(0, entryType.GetFields(BindingFlags.Instance | BindingFlags.Public).Length);

        string[] propertyNames = { "ProjectId", "ShortName", "Bucket", "UiSide", "SubsidyLane", "BugReviewState" };
        foreach (string propertyName in propertyNames)
        {
            var property = entryType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            AssertTrue(property != null, propertyName + " should stay public");
            AssertTrue(property.GetMethod != null, propertyName + " should stay readable");
            AssertEqual(null, property.SetMethod);
        }
    }

    private static void ProjectDoctrineCatalogMarksMarketReformBroken()
    {
        var entry = WhiskeyRealism.Strategic.Projects.ProjectDoctrineCatalog.Get(98);
        AssertEqual(WhiskeyRealism.Strategic.Projects.ProjectDoctrineBucket.FinanceCreditAdmin, entry.Bucket);
        AssertEqual(WhiskeyRealism.Strategic.Projects.ProjectBugReviewState.FullyBrokenUntilReviewed, entry.BugReviewState);
    }

    private static void ProjectDoctrineCatalogMapsRepresentativeBucketsAndLanes()
    {
        AssertProjectDoctrine(0, WhiskeyRealism.Strategic.Projects.ProjectDoctrineBucket.ArmsImport, 5);
        AssertProjectDoctrine(35, WhiskeyRealism.Strategic.Projects.ProjectDoctrineBucket.NavalBlockade, 4);
        AssertProjectDoctrine(38, WhiskeyRealism.Strategic.Projects.ProjectDoctrineBucket.NavalBlockade, 5);
        AssertProjectDoctrine(96, WhiskeyRealism.Strategic.Projects.ProjectDoctrineBucket.FinanceCreditAdmin, 1);
        AssertProjectDoctrine(99, WhiskeyRealism.Strategic.Projects.ProjectDoctrineBucket.LogisticsRail, 3);
        AssertProjectDoctrine(100, WhiskeyRealism.Strategic.Projects.ProjectDoctrineBucket.LogisticsRail, 4);
        AssertProjectDoctrine(103, WhiskeyRealism.Strategic.Projects.ProjectDoctrineBucket.DiplomacyTradeRecognition, 5);
        AssertProjectDoctrine(105, WhiskeyRealism.Strategic.Projects.ProjectDoctrineBucket.AgricultureIndustry, 2);
        AssertProjectDoctrine(120, WhiskeyRealism.Strategic.Projects.ProjectDoctrineBucket.NavalBlockade, 2);
        AssertProjectDoctrine(124, WhiskeyRealism.Strategic.Projects.ProjectDoctrineBucket.ManpowerTrainingCivilOrder, 4);
    }

    private static void AssertProjectDoctrine(
        int projectId,
        WhiskeyRealism.Strategic.Projects.ProjectDoctrineBucket expectedBucket,
        int expectedLane)
    {
        var entry = WhiskeyRealism.Strategic.Projects.ProjectDoctrineCatalog.Get(projectId);
        AssertEqual(projectId, entry.ProjectId);
        AssertEqual(expectedBucket, entry.Bucket);
        AssertEqual(expectedLane, entry.SubsidyLane);
    }

    private static void ProjectDoctrineCatalogMapsOrganizationReformAliases()
    {
        var wl = WhiskeyRealism.Strategic.Projects.ProjectDoctrineCatalog.Get(89);
        var baseScenario = WhiskeyRealism.Strategic.Projects.ProjectDoctrineCatalog.Get(90);
        AssertEqual(WhiskeyRealism.Strategic.Projects.ProjectDoctrineBucket.ManpowerTrainingCivilOrder, wl.Bucket);
        AssertEqual(wl.Bucket, baseScenario.Bucket);
        AssertEqual(wl.SubsidyLane, baseScenario.SubsidyLane);
    }

    private static void ProjectDoctrineCatalogHasNoLaneSixOrSevenEntries()
    {
        foreach (var entry in WhiskeyRealism.Strategic.Projects.ProjectDoctrineCatalog.AllActive)
            AssertEqual(false, entry.SubsidyLane == 6 || entry.SubsidyLane == 7);
    }

    private static void ProjectDoctrineSignalsClampWeaponAndArtilleryDeficits()
    {
        var input = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignalInput
        {
            Alliance = 1,
            Era = EraStage.Operational1862,
            FiscalPosture = FiscalPosture.BalancedWar,
            OwnAverageRifles = 0.25f,
            EnemyBestAverageRifles = 0.75f,
            OwnAverageGuns = 0.2f,
            EnemyBestAverageGuns = 0.4f
        };

        var signals = WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignalBuilder.Build(input);

        AssertNear(0.6667f, signals.WeaponDeficit, 0.01f, "weapon deficit");
        AssertNear(0.5f, signals.ArtilleryDeficit, 0.01f, "artillery deficit");
    }

    private static void ProjectDoctrineSignalsMapFiscalPosture()
    {
        var input = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignalInput
        {
            Alliance = 0,
            Era = EraStage.Amateur1861,
            FiscalPosture = FiscalPosture.EmergencySolvency
        };

        var signals = WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignalBuilder.Build(input);

        AssertEqual(1f, signals.CreditStress);
    }

    private static void ProjectDoctrineSignalsComputeLateWarCollapseRisk()
    {
        var input = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignalInput
        {
            Alliance = 1,
            Era = EraStage.TotalWar1864,
            FiscalPosture = FiscalPosture.CreditDefense,
            ManpowerStressInput = 0.8f,
            StrengthRatio = 0.6f
        };

        var signals = WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignalBuilder.Build(input);

        AssertNear(0.7f, signals.LateWarCollapseRisk, 0.01f, "late war collapse risk");
    }

    private static void ProjectDoctrineSignalsBoundRecognitionAndPort()
    {
        var input = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignalInput
        {
            Alliance = 1,
            Era = EraStage.Amateur1861,
            FiscalPosture = FiscalPosture.BalancedWar,
            PortViabilityInput = 3f,
            RecognitionProbability = 2f
        };

        var signals = WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignalBuilder.Build(input);

        AssertEqual(1f, signals.PortViability);
        AssertEqual(1f, signals.RecognitionWindow);
    }

    private static void ProjectDoctrineSignalsDefaultBlockadePressureNeutral()
    {
        var nullSignals = WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignalBuilder.Build(null);
        AssertEqual(0.5f, nullSignals.BlockadePressure);

        var unionSignals = WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignalBuilder.Build(
            new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignalInput { Alliance = 0 });
        var csaSignals = WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignalBuilder.Build(
            new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignalInput { Alliance = 1 });

        AssertEqual(0.5f, unionSignals.BlockadePressure);
        AssertEqual(0.5f, csaSignals.BlockadePressure);
    }

    private static void ProjectDoctrineSignalsIgnoreNonfiniteLogisticsPressureSide()
    {
        var input = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignalInput
        {
            SupplyPressure = float.NaN,
            TransportPressure = 0.7f
        };

        var signals = WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignalBuilder.Build(input);

        AssertEqual(0.7f, signals.LogisticsTempoNeed);
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

    private static OperationalProbeInput BuildProbeInput()
    {
        var front = FrontSectorLedger.Build(new[]
        {
            new FrontSectorInput
            {
                SectorKey = "VirginiaCapitalCorridor",
                Theater = Theater.East,
                OwnStrength = 22000f,
                EnemyStrength = 14000f,
                StrategicImportance = 0.8f,
                IsCritical = false,
                IsPlanTarget = true,
                CommanderAudacity = 0.3f,
                CommanderCaution = 0.2f,
                AverageMorale = 0.8f,
                AverageSupply = 0.8f,
                AverageReadiness = 0.8f
            },
            new FrontSectorInput
            {
                SectorKey = "Richmond",
                Theater = Theater.East,
                OwnStrength = 20000f,
                EnemyStrength = 18000f,
                StrategicImportance = 1.0f,
                IsCritical = true,
                IsPlanTarget = false,
                CommanderAudacity = 0.1f,
                CommanderCaution = 0.5f,
                AverageMorale = 0.8f,
                AverageSupply = 0.8f,
                AverageReadiness = 0.8f
            }
        });

        return new OperationalProbeInput
        {
            AllianceId = 1,
            DaySerial = 100,
            PlanTargetAreaKey = "VirginiaCapitalCorridor",
            Fronts = front,
            FormationDirectives = FormationDirectiveLedger.Build(new[]
            {
                ProbeSnapshot("probe-corps", 1, 15, 7000f, 5000f, FormationLevel.Division, FrontPosture.Counterstroke, "VirginiaCapitalCorridor"),
                Snapshot("hold-army", 1, 16, 26000f, 18000f, FormationLevel.Army, FrontPosture.Hold)
            }, EraStage.Operational1862, "VirginiaCapitalCorridor")
        };
    }

    private static FormationSnapshot ProbeSnapshot(
        string key,
        int alliance,
        int unitType,
        float strength,
        float enemy,
        FormationLevel enemyLevel,
        FrontPosture posture,
        string sectorKey)
    {
        var snapshot = Snapshot(key, alliance, unitType, strength, enemy, enemyLevel, posture);
        snapshot.SectorKey = sectorKey;
        return snapshot;
    }

    private static void OperationalProbeAssignsOneBoundedSameAreaFormation()
    {
        var output = OperationalProbeLedger.Build(BuildProbeInput());

        AssertEqual(OperationalProbeDecision.Probe, output.Decision);
        AssertEqual("probe-corps", output.SelectedUnitKey);
        AssertEqual("VirginiaCapitalCorridor", output.TargetAreaKey);
        AssertEqual(false, output.RequiresMassCommitment);
    }

    private static void OperationalProbePausesOnEnemyReaction()
    {
        var input = BuildProbeInput();
        input.Previous = new OperationalProbeState
        {
            ProbeId = "1:VirginiaCapitalCorridor:probe-corps",
            UnitKey = "probe-corps",
            TargetAreaKey = "VirginiaCapitalCorridor",
            StartedDaySerial = 96,
            LastObservedEnemyStrength = 7000f,
            LastObservedFriendlyStrength = 7000f
        };
        input.CurrentEnemyStrength = 13000f;
        input.CurrentFriendlyStrength = 8000f;

        var output = OperationalProbeLedger.Build(input);

        AssertEqual(OperationalProbeDecision.Pause, output.Decision);
        AssertEqual("enemy-reaction", output.Reason);
        AssertEqual(false, output.RequiresMassCommitment);
    }

    private static void OperationalProbeEscalatesAfterFavorableContact()
    {
        var input = BuildProbeInput();
        input.DaySerial = 104;
        input.Previous = new OperationalProbeState
        {
            ProbeId = "1:VirginiaCapitalCorridor:probe-corps",
            UnitKey = "probe-corps",
            TargetAreaKey = "VirginiaCapitalCorridor",
            StartedDaySerial = 100,
            LastObservedEnemyStrength = 7000f,
            LastObservedFriendlyStrength = 7000f
        };
        input.CurrentEnemyStrength = 4000f;
        input.CurrentFriendlyStrength = 9000f;

        var output = OperationalProbeLedger.Build(input);

        AssertEqual(OperationalProbeDecision.Escalate, output.Decision);
        AssertEqual(true, output.RequiresMassCommitment);
    }

    private static void OperationalProbeRefusesCriticalHoldDonor()
    {
        var input = BuildProbeInput();
        input.FormationDirectives = FormationDirectiveLedger.Build(new[]
        {
            Snapshot("critical-army", 1, 16, 20000f, 18000f, FormationLevel.Army, FrontPosture.Hold)
        }, EraStage.Operational1862, "VirginiaCapitalCorridor");

        var output = OperationalProbeLedger.Build(input);

        AssertEqual(OperationalProbeDecision.None, output.Decision);
        AssertEqual("no-eligible-probe-formation", output.Reason);
    }

    private static void OperationalProbeOverlaysFormationDirective()
    {
        var input = BuildProbeInput();
        var output = OperationalProbeLedger.Build(input);

        bool changed = input.FormationDirectives.ApplyOperationalProbe(output);
        var assignment = input.FormationDirectives.GetAssignment("probe-corps");

        AssertEqual(true, changed);
        AssertEqual(FormationDirective.Probe, assignment.Directive);
        AssertEqual("limited-contact-probe", assignment.Reason);
        AssertEqual(false, assignment.TransferDonorAllowed);
    }

    private static void OperationalProbeStaysContinuingOnNoContactAfterMinimumDays()
    {
        var input = BuildProbeInput();
        input.DaySerial = 107;
        input.Previous = new OperationalProbeState
        {
            ProbeId = "1:VirginiaCapitalCorridor:probe-corps",
            UnitKey = "probe-corps",
            TargetAreaKey = "VirginiaCapitalCorridor",
            SourceSectorKey = "VirginiaCapitalCorridor",
            StartedDaySerial = 100,
            LastObservedEnemyStrength = 0f,
            LastObservedFriendlyStrength = 8000f
        };
        input.CurrentEnemyStrength = 0f;
        input.CurrentFriendlyStrength = 8000f;
        input.Options = new OperationalProbeOptions { MinimumProbeDays = 3, EscalateFriendlyRatio = 1.8f, WithdrawFriendlyRatio = 0.55f };
        input.ContactEvidence = ContactEvidence.NoContact;

        var output = OperationalProbeLedger.Build(input);
        AssertTrue(output.Decision != OperationalProbeDecision.Escalate, "no-contact must not escalate");
        AssertEqual(OperationalProbeDecision.Probe, output.Decision);
    }

    private static void OperationalProbeStateHasSingleSourceOnCoordinator()
    {
        // After Build returns, the output.State must be usable as Previous in a follow-up call.
        // This tests that OperationalProbeLedger's contract aligns with StrategicCoordinator's
        // single-source-of-truth pattern: the coordinator owns _operationalProbeStates[alliance],
        // and output.State references that slot.

        var input = BuildProbeInput();
        var first = OperationalProbeLedger.Build(input);
        AssertTrue(first.State != null, "fresh probe should publish a state");

        // Pass the same reference to the next call to confirm it's accepted as Previous.
        var second = OperationalProbeLedger.Build(new OperationalProbeInput
        {
            AllianceId = input.AllianceId,
            DaySerial = input.DaySerial + 1,
            PlanTargetAreaKey = input.PlanTargetAreaKey,
            Fronts = input.Fronts,
            FormationDirectives = input.FormationDirectives,
            Previous = first.State, // pass the same reference
            CurrentEnemyStrength = 1000f,
            CurrentFriendlyStrength = 4000f,
            Options = new OperationalProbeOptions()
        });
        AssertTrue(second.State != null, "continuing probe publishes state");
        AssertEqual(first.State.ProbeId, second.State.ProbeId);
    }

    private static void RecomputePressureResetsCountersBeforeCounting()
    {
        var snap = new FormationSnapshot
        {
            UnitKey = "U1",
            AllianceId = 0,
            UnitType = 16,
            IsTopUnit = true,
            AreaKey = "RichmondCorridor",
            SectorKey = "RichmondCorridor",
            GroupStrengthActive = 8000f,
            GroupStrengthDirect = 8000f,
            Morale = 0.2f,
            Readiness = 0.2f,
            RifleAmmo = 0.2f,
            ArtilleryAmmo = 0.2f,
            Supply = 0.2f
        };
        var ledger = FormationDirectiveLedger.Build(new[] { snap }, EraStage.Decisive1863, "RichmondCorridor");

        int recoverAfterBuild = ledger.Pressure.RecoverCount;
        AssertEqual(1, recoverAfterBuild, "recover after build");

        for (int i = 0; i < 100; i++)
        {
            ledger.ApplyOperationalProbe(new OperationalProbeOutput
            {
                Decision = OperationalProbeDecision.Probe,
                SelectedUnitKey = "U1",
                Reason = "test"
            });
        }

        AssertTrue(ledger.Pressure.RecoverCount <= 1,
            "RecoverCount must be bounded by Assignments.Count after 100 overlays — was " + ledger.Pressure.RecoverCount);
        AssertTrue(ledger.Pressure.LowSupplyCount <= 1, "LowSupplyCount bounded");
        AssertTrue(ledger.Pressure.LowAmmoCount <= 1,   "LowAmmoCount bounded");
    }

    private static void OperationalTempoChapterOneDelaysEscalation()
    {
        var input = BuildProbeInput();
        input.DaySerial = 103;
        input.Options = OperationalTempoDoctrine.For(
            allianceId: 0,
            era: EraStage.Amateur1861,
            policyChapter: 1,
            campaignMonth: 7,
            personality: new PersonalityVector());
        input.Previous = new OperationalProbeState
        {
            ProbeId = "0:VirginiaCapitalCorridor:probe-corps",
            UnitKey = "probe-corps",
            TargetAreaKey = "VirginiaCapitalCorridor",
            StartedDaySerial = 100,
            LastObservedEnemyStrength = 7000f,
            LastObservedFriendlyStrength = 7000f
        };
        input.CurrentEnemyStrength = 4000f;
        input.CurrentFriendlyStrength = 8500f;

        var output = OperationalProbeLedger.Build(input);

        AssertEqual(OperationalProbeDecision.Probe, output.Decision);
        AssertEqual("continue-probe", output.Reason);
    }

    private static void OperationalTempoLateUnionSustainsPressure()
    {
        var input = BuildProbeInput();
        input.DaySerial = 202;
        input.Options = OperationalTempoDoctrine.For(
            allianceId: 0,
            era: EraStage.TotalWar1864,
            policyChapter: 3,
            campaignMonth: 6,
            personality: new PersonalityVector());
        input.Previous = new OperationalProbeState
        {
            ProbeId = "0:VirginiaCapitalCorridor:probe-corps",
            UnitKey = "probe-corps",
            TargetAreaKey = "VirginiaCapitalCorridor",
            StartedDaySerial = 200,
            LastObservedEnemyStrength = 7000f,
            LastObservedFriendlyStrength = 7000f
        };
        input.CurrentEnemyStrength = 5000f;
        input.CurrentFriendlyStrength = 8500f;

        var output = OperationalProbeLedger.Build(input);

        AssertEqual(OperationalProbeDecision.Escalate, output.Decision);
        AssertEqual(true, output.RequiresMassCommitment);
    }

    private static void OperationalTempoWinterSlowsProbes()
    {
        var summer = OperationalTempoDoctrine.For(
            allianceId: 0,
            era: EraStage.Operational1862,
            policyChapter: 2,
            campaignMonth: 7,
            personality: new PersonalityVector());
        var winter = OperationalTempoDoctrine.For(
            allianceId: 0,
            era: EraStage.Operational1862,
            policyChapter: 2,
            campaignMonth: 1,
            personality: new PersonalityVector());

        AssertTrue(winter.MinimumProbeDays > summer.MinimumProbeDays,
            "winter should require a longer probe/operation pause");
        AssertTrue(winter.MaximumProbeStrengthFraction < summer.MaximumProbeStrengthFraction,
            "winter should limit probe size");
    }

    private static void OperationalTempoLateCsaMoreConservativeThanUnion()
    {
        var union = OperationalTempoDoctrine.For(
            allianceId: 0,
            era: EraStage.TotalWar1864,
            policyChapter: 3,
            campaignMonth: 6,
            personality: new PersonalityVector());
        var csa = OperationalTempoDoctrine.For(
            allianceId: 1,
            era: EraStage.TotalWar1864,
            policyChapter: 3,
            campaignMonth: 6,
            personality: new PersonalityVector());

        AssertTrue(csa.EscalateFriendlyRatio > union.EscalateFriendlyRatio,
            "late CSA should require better odds to escalate");
        AssertTrue(csa.MaximumProbeStrengthFraction < union.MaximumProbeStrengthFraction,
            "late CSA should commit smaller probes than Union");
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

    private static void FiscalCreditDefenseRequiresStableExitTicks()
    {
        var input = BuildFiscalInput();
        input.CurrentRating = 4;
        input.AnnualBalance = 500000f;
        input.Treasury = 2500000f;
        input.Memory.PreviousPosture = FiscalPosture.CreditDefense;
        input.Memory.EmergencyResidue = false;
        input.Memory.StableTicksAboveEmergency = 13;

        var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
        AssertEqual(FiscalPosture.CreditDefense, output.Posture);

        input.Memory.StableTicksAboveEmergency = 14;
        output = FiscalIntentLedger.Compute(input, new FiscalOptions());
        AssertEqual(FiscalPosture.BalancedWar, output.Posture);
    }

    private static void FiscalEmergencyResidueClearsAfterStableTicks()
    {
        var input = BuildFiscalInput();
        input.CurrentRating = 4;
        input.AnnualBalance = 500000f;
        input.Treasury = 2500000f;
        input.Memory.PreviousPosture = FiscalPosture.CreditDefense;
        input.Memory.EmergencyResidue = true;
        input.Memory.StableTicksAboveEmergency = 14;

        var output = FiscalIntentLedger.Compute(input, new FiscalOptions());
        AssertEqual(FiscalPosture.BalancedWar, output.Posture);
    }

    private static void FiscalClampsDisabledSubsidyFocusToZero()
    {
        var input = BuildFiscalInput();
        input.Subsidies[3] = 0.20f;
        input.SubsidyFocus[3] = -1f;

        var output = FiscalIntentLedger.Compute(input, new FiscalOptions());

        AssertEqual(0f, output.TargetSubsidyMin[3]);
        AssertEqual(0f, output.TargetSubsidyMax[3]);
    }

    private static void FiscalClampsNegativeSavedSubsidyValues()
    {
        var input = BuildFiscalInput();
        input.Subsidies[3] = -0.95f;
        input.SubsidyFocus[3] = 0.20f;

        var output = FiscalIntentLedger.Compute(input, new FiscalOptions());

        AssertEqual(0f, output.TargetSubsidyMin[3]);
        AssertEqual(0f, output.TargetSubsidyMax[3]);
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

    private static void EconomyAllianceDataGuardSuppressesOnlyNullReferences()
    {
        AssertEqual(true, EconomyAllianceDataGuard.ShouldSuppress(new NullReferenceException()));
        AssertEqual(false, EconomyAllianceDataGuard.ShouldSuppress(new InvalidOperationException()));
        AssertEqual(false, EconomyAllianceDataGuard.ShouldSuppress(null));
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

    private static void CampaignAiGovernorCapsVanillaPasses()
    {
        var options = new CampaignAiGovernorOptions
        {
            Enabled = true,
            MaxPassesAt20x = 2,
            MaxPassesAt50x = 3,
            FrameBudgetMs = 3f
        };

        AssertEqual(2, FastForwardAiScheduler.GovernedPassCap(5f, options));
        AssertEqual(2, FastForwardAiScheduler.GovernedPassCap(20f, options));
        AssertEqual(3, FastForwardAiScheduler.GovernedPassCap(50f, options));
    }

    private static void CampaignAiGovernorRespectsFrameBudgetBeforeFirstPass()
    {
        var options = new CampaignAiGovernorOptions
        {
            Enabled = true,
            MaxPassesAt50x = 3,
            FrameBudgetMs = 1.5f
        };

        AssertEqual(true, FastForwardAiScheduler.ShouldRunGovernedPass(0, 0f, 50f, options));
        AssertEqual(false, FastForwardAiScheduler.ShouldRunGovernedPass(0, 1.5f, 50f, options));
        AssertEqual(false, FastForwardAiScheduler.ShouldRunGovernedPass(3, 0f, 50f, options));
    }

    private static void CampaignAiGovernorSkipsPausedVanillaUpdate()
    {
        AssertEqual(true, FastForwardAiScheduler.ShouldSkipCampaignAiUpdate(gamePaused: true, gameSpeed: 1f));
        AssertEqual(true, FastForwardAiScheduler.ShouldSkipCampaignAiUpdate(gamePaused: false, gameSpeed: 0f));
        AssertEqual(false, FastForwardAiScheduler.ShouldSkipCampaignAiUpdate(gamePaused: false, gameSpeed: 1f));
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

    private static void FrontSectorSignatureIgnoresSubBucketRatioJitter()
    {
        // OwnStrength=1500, EnemyStrength=1000 → ratio 1.5 → bucket 1.5
        var a = FrontSectorLedger.Build(new[]
        {
            new FrontSectorInput
            {
                SectorKey = "East",
                Theater = Theater.East,
                OwnStrength = 1500f,
                EnemyStrength = 1000f,
                StrategicImportance = 0.5f,
                IsCritical = false,
                IsPlanTarget = false,
                CommanderAudacity = 0f,
                CommanderCaution = 0f,
                AverageMorale = 0.7f,
                AverageSupply = 0.7f,
                AverageReadiness = 0.7f
            }
        });
        // OwnStrength=1530, EnemyStrength=1000 → ratio 1.53 → still bucket 1.5
        var b = FrontSectorLedger.Build(new[]
        {
            new FrontSectorInput
            {
                SectorKey = "East",
                Theater = Theater.East,
                OwnStrength = 1530f,
                EnemyStrength = 1000f,
                StrategicImportance = 0.5f,
                IsCritical = false,
                IsPlanTarget = false,
                CommanderAudacity = 0f,
                CommanderCaution = 0f,
                AverageMorale = 0.7f,
                AverageSupply = 0.7f,
                AverageReadiness = 0.7f
            }
        });
        AssertEqual(a.Signature(), b.Signature());
    }

    private static void AssetStrategicRoleFlagsComposeAdditively()
    {
        var role = AssetStrategicRole.BlockadeRunnerPort | AssetStrategicRole.KeyFort;
        AssertTrue((role & AssetStrategicRole.BlockadeRunnerPort) != 0, "blockade flag missing");
        AssertTrue((role & AssetStrategicRole.KeyFort) != 0, "key-fort flag missing");
        AssertTrue((role & AssetStrategicRole.RearSafePort) == 0, "unset flag should not appear");
        AssertEqual(AssetStrategicRole.None, default(AssetStrategicRole));
    }

    private static void AssetRoleScorerFlagsCsaBlockadePortFromProfile()
    {
        var profile = GrandStrategyRegistry.Resolve(allianceId: 1, stage: EraStage.Amateur1861);
        var asset = new CampaignMapAsset
        {
            Kind = CampaignMapAssetKind.SeaHarbor,
            Name = "wilmington-harbor",
            StateAbbrev = "NC",
            Theater = Theater.Coast,
            Owner = 1,
            Capacity = 4f,
            Level = 2
        };
        var role = AssetRoleScorer.Score(asset, profile, capitalDistance: 250f, frontDistance: 80f);
        AssertTrue((role & AssetStrategicRole.BlockadeRunnerPort) != 0,
            "csa sea port should score blockade-runner when CSA profile has TradeWarfare or ArmsImports");
    }

    private static void AssetRoleScorerFlagsUnionRiverHubFromProfile()
    {
        var profile = GrandStrategyRegistry.Resolve(allianceId: 0, stage: EraStage.Amateur1861);
        var asset = new CampaignMapAsset
        {
            Kind = CampaignMapAssetKind.RiverHarbor,
            Name = "cairo-harbor",
            StateAbbrev = "IL",
            Theater = Theater.River,
            Owner = 0,
            Capacity = 2f
        };
        var role = AssetRoleScorer.Score(asset, profile, capitalDistance: 800f, frontDistance: 60f);
        AssertTrue((role & AssetStrategicRole.RiverControlHub) != 0,
            "union river hub should score river-control when Union profile has RiverControl");
    }

    private static void AssetRoleScorerFlagsKeyFortFromLevel()
    {
        var profile = GrandStrategyRegistry.Resolve(allianceId: 1, stage: EraStage.Amateur1861);
        var asset = new CampaignMapAsset { Kind = CampaignMapAssetKind.Fort, Name = "f", Level = 2 };
        var role = AssetRoleScorer.Score(asset, profile, capitalDistance: 9999f, frontDistance: 9999f);
        AssertTrue((role & AssetStrategicRole.KeyFort) != 0, "level-2 fort should score key-fort");

        var lowFort = new CampaignMapAsset { Kind = CampaignMapAssetKind.Fort, Name = "f", Level = 1 };
        var lowRole = AssetRoleScorer.Score(lowFort, profile, capitalDistance: 9999f, frontDistance: 9999f);
        AssertTrue((lowRole & AssetStrategicRole.KeyFort) == 0, "level-1 fort should NOT score key-fort");
    }

    private static void AssetRoleScorerFlagsCapitalApproachByDistance()
    {
        var profile = GrandStrategyRegistry.Resolve(allianceId: 1, stage: EraStage.Amateur1861);
        var asset = new CampaignMapAsset { Kind = CampaignMapAssetKind.SeaHarbor, Name = "norfolk-harbor" };

        var near = AssetRoleScorer.Score(asset, profile, capitalDistance: 100f, frontDistance: 200f);
        AssertTrue((near & AssetStrategicRole.CapitalApproach) != 0,
            "asset within 120 of capital should score capital-approach");

        var far = AssetRoleScorer.Score(asset, profile, capitalDistance: 500f, frontDistance: 200f);
        AssertTrue((far & AssetStrategicRole.CapitalApproach) == 0,
            "asset 500 from capital should NOT score capital-approach");
    }

    private static void AssetRoleScorerReturnsNoneWhenNoRulesMatch()
    {
        var profile = GrandStrategyRegistry.Resolve(allianceId: 0, stage: EraStage.Amateur1861);
        var asset = new CampaignMapAsset
        {
            Kind = CampaignMapAssetKind.SeaHarbor,
            Name = "unmapped-port",
            StateAbbrev = "??",
            Theater = Theater.Unknown
        };
        var role = AssetRoleScorer.Score(asset, profile, capitalDistance: 9999f, frontDistance: 9999f);
        AssertEqual(AssetStrategicRole.None, role);
    }

    private static void AssetRoleScorerFlagsUnionForwardBaseFromProfile()
    {
        var profile = GrandStrategyRegistry.Resolve(allianceId: 0, stage: EraStage.Amateur1861);
        var asset = new CampaignMapAsset
        {
            Kind = CampaignMapAssetKind.SeaHarbor,
            Name = "hampton-roads",
            StateAbbrev = "VA",
            Theater = Theater.Coast,
            Owner = 0,            // Union-owned
            Capacity = 5f
        };
        var role = AssetRoleScorer.Score(asset, profile, capitalDistance: 250f, frontDistance: 80f);
        AssertTrue((role & AssetStrategicRole.UnionForwardBase) != 0,
            "union sea port near front should score forward-base when union profile has Blockade or PortAccess");
    }

    private static void AssetRoleScorerRejectsUnionForwardBaseEnemyOwned()
    {
        var profile = GrandStrategyRegistry.Resolve(allianceId: 0, stage: EraStage.Amateur1861);
        var asset = new CampaignMapAsset
        {
            Kind = CampaignMapAssetKind.SeaHarbor,
            Name = "wilmington-harbor",
            StateAbbrev = "NC",
            Theater = Theater.Coast,
            Owner = 1,            // CSA-owned — must NOT count as Union forward base
            Capacity = 4f
        };
        var role = AssetRoleScorer.Score(asset, profile, capitalDistance: 250f, frontDistance: 80f);
        AssertTrue((role & AssetStrategicRole.UnionForwardBase) == 0,
            "csa-owned port must not flag UnionForwardBase even when union profile has the tag");
    }

    private static void AssetRoleScorerScoreTownFlagsCapitalApproach()
    {
        var profile = GrandStrategyRegistry.Resolve(allianceId: 1, stage: EraStage.Amateur1861);
        var town = new CampaignMapTown { CityName = "norfolk", Theater = Theater.Coast };

        var near = AssetRoleScorer.ScoreTown(town, profile, capitalDistance: 100f);
        AssertTrue((near & AssetStrategicRole.CapitalApproach) != 0,
            "town within 120 of capital should score capital-approach via ScoreTown");

        var far = AssetRoleScorer.ScoreTown(town, profile, capitalDistance: 500f);
        AssertEqual(AssetStrategicRole.None, far);
    }

    private static void AssetRoleCatalogOverridesScorer()
    {
        var role = AssetRoleCatalog.Lookup("wilmington-harbor");
        AssertTrue((role & AssetStrategicRole.BlockadeRunnerPort) != 0, "wilmington should be blockade-runner");
        AssertTrue((role & AssetStrategicRole.KeyFort) == 0, "wilmington should not be flagged key-fort by name alone");

        var norfolk = AssetRoleCatalog.Lookup("norfolk-harbor");
        AssertTrue((norfolk & AssetStrategicRole.CapitalApproach) != 0, "norfolk should be capital approach");
    }

    private static void AssetRoleCatalogReturnsNoneForUnknown()
    {
        AssertEqual(AssetStrategicRole.None, AssetRoleCatalog.Lookup("unmapped-port"));
        AssertEqual(AssetStrategicRole.None, AssetRoleCatalog.Lookup(null));
        AssertEqual(AssetStrategicRole.None, AssetRoleCatalog.Lookup(""));
    }

    private static void AssetRoleCatalogResolvesRealGtcwNames()
    {
        AssertTrue((AssetRoleCatalog.Lookup("Fort McHenry") & AssetStrategicRole.KeyFort) != 0,
            "Fort McHenry should be KeyFort");
        AssertTrue((AssetRoleCatalog.Lookup("Brooklyn Navy Yard") & AssetStrategicRole.UnionForwardBase) != 0,
            "Brooklyn Navy Yard should be UnionForwardBase");
        AssertTrue((AssetRoleCatalog.Lookup("Atlantic City") & AssetStrategicRole.RearSafePort) != 0,
            "Atlantic City should be RearSafePort");
        AssertTrue((AssetRoleCatalog.Lookup("baltimore port") & AssetStrategicRole.CapitalApproach) != 0,
            "case-insensitive lookup should still work");
        // Backward-compat: old hyphenated keys still resolve
        AssertTrue((AssetRoleCatalog.Lookup("wilmington-harbor") & AssetStrategicRole.BlockadeRunnerPort) != 0,
            "legacy hyphenated key should still resolve as fallback");
    }

    private static void CampaignMapLedgerAppliesRoleCatalog()
    {
        var towns = new[]
        {
            new CampaignMapTown { CityName = "Norfolk", StateId = 1, StateName = "Virginia", IsCapital = false, X = 100f, Z = 100f }
        };
        var assets = new[]
        {
            new CampaignMapAsset { Kind = CampaignMapAssetKind.SeaHarbor, Name = "wilmington-harbor", StateId = 2, StateName = "North Carolina", X = 200f, Z = 200f },
            new CampaignMapAsset { Kind = CampaignMapAssetKind.SeaHarbor, Name = "unmapped-port",     StateId = 3, StateName = "South Carolina", X = 300f, Z = 300f }
        };

        var ledger = CampaignMapLedger.Build(towns, assets);

        AssertTrue(ledger.TryGetTown("Norfolk", out var norfolk), "should resolve Norfolk by name");
        // Norfolk is a known catalog entry under "norfolk-harbor", but the town is keyed by CityName "Norfolk" — case-insensitive lookup should NOT match a different key. Verify None.
        AssertEqual(AssetStrategicRole.None, norfolk.StrategicRole);

        var wilmington = ledger.Assets[0];
        AssertTrue((wilmington.StrategicRole & AssetStrategicRole.BlockadeRunnerPort) != 0,
            "wilmington-harbor asset should pick up BlockadeRunnerPort from catalog");

        var unmapped = ledger.Assets[1];
        AssertEqual(AssetStrategicRole.None, unmapped.StrategicRole);
    }

    private static void CampaignMapLedgerSignatureReflectsRoleChanges()
    {
        var asset1 = new CampaignMapAsset
        {
            Kind = CampaignMapAssetKind.SeaHarbor, Name = "wilmington-harbor",
            StateId = 1, StateName = "North Carolina", X = 100f, Z = 100f
        };
        var asset2 = new CampaignMapAsset
        {
            Kind = CampaignMapAssetKind.SeaHarbor, Name = "unmapped-port",
            StateId = 1, StateName = "North Carolina", X = 100f, Z = 100f
        };

        var withCatalogHit = CampaignMapLedger.Build(null, new[] { asset1 });
        var withMiss = CampaignMapLedger.Build(null, new[] { asset2 });

        AssertTrue(withCatalogHit.Signature != withMiss.Signature,
            "signature must change when asset roles change");
    }

    private static void DefensePostureDefaultsToNotEvaluated()
    {
        AssertEqual(DefensePosture.NotEvaluated, default(DefensePosture));
        AssertEqual(ThreatScale.None, default(ThreatScale));
    }

    private static void DefenseThreatCarriesSignatureAndPosture()
    {
        var threat = new DefenseThreat
        {
            Signature = "sif:1234:Norfolk:Hampton",
            Posture = DefensePosture.ActiveInvasion,
            Scale = ThreatScale.Landing,
            AssetName = "norfolk-harbor",
            EnemyStrength = 4200f,
            DesiredStrength = 6500f,
            EscalationReason = "landed-port-threat"
        };
        AssertEqual("sif:1234:Norfolk:Hampton", threat.Signature);
        AssertEqual(DefensePosture.ActiveInvasion, threat.Posture);
        AssertEqual(ThreatScale.Landing, threat.Scale);
    }

    private static void ThreatSignatureForSifUsesInstanceAndSpot()
    {
        var sig = DefenseThreatSignature.ForSeaInvasion(
            invasionForceInstanceId: 42, spotName: "Hampton", sourcePortName: "Boston");
        AssertEqual("sif:42:Hampton:Boston", sig);

        var nullSpot = DefenseThreatSignature.ForSeaInvasion(
            invasionForceInstanceId: 42, spotName: null, sourcePortName: null);
        AssertEqual("sif:42:<no-spot>:<no-port>", nullSpot);
    }

    private static void ThreatSignatureForRaidUsesInstanceAndAsset()
    {
        var sig = DefenseThreatSignature.ForRaid(raidGroupInstanceId: 7, nearestAssetName: "wilmington-harbor");
        AssertEqual("raid:7:wilmington-harbor", sig);
    }

    private static void ThreatSignatureForAssetUsesSortedTopN()
    {
        var sig = DefenseThreatSignature.ForAsset(
            assetKind: CampaignMapAssetKind.SeaHarbor,
            assetName: "vicksburg-harbor",
            enemyInstanceIds: new[] { 9, 3, 5, 1, 7, 11 },
            topN: 3);
        AssertEqual("asset:SeaHarbor:vicksburg-harbor:1,3,5", sig);
    }

    private static void ThreatSignatureIsStableAcrossReorderedEnemies()
    {
        var a = DefenseThreatSignature.ForAsset(
            CampaignMapAssetKind.RiverHarbor, "memphis-harbor", new[] { 5, 3, 1 }, topN: 5);
        var b = DefenseThreatSignature.ForAsset(
            CampaignMapAssetKind.RiverHarbor, "memphis-harbor", new[] { 1, 5, 3 }, topN: 5);
        AssertEqual(a, b);
    }

    private static void ThreatSignatureForRaidHandlesNullAsset()
    {
        AssertEqual("raid:7:<no-asset>", DefenseThreatSignature.ForRaid(7, null));
        AssertEqual("raid:7:<no-asset>", DefenseThreatSignature.ForRaid(7, ""));
    }

    private static void ThreatSignatureForAssetHandlesNullName()
    {
        AssertEqual("asset:Fort:<no-asset>:1,2",
            DefenseThreatSignature.ForAsset(CampaignMapAssetKind.Fort, null, new[] { 2, 1 }, topN: 5));
        AssertEqual("asset:Fort:<no-asset>:<no-enemies>",
            DefenseThreatSignature.ForAsset(CampaignMapAssetKind.Fort, "", null, topN: 5));
        AssertEqual("asset:Fort:<no-asset>:<no-enemies>",
            DefenseThreatSignature.ForAsset(CampaignMapAssetKind.Fort, "", new int[0], topN: 5));
    }

    private static void ThreatSignatureForAssetClampsTopNAtOne()
    {
        // topN <= 0 must NOT collapse to <no-enemies>; must take at least one ID.
        var sig = DefenseThreatSignature.ForAsset(
            CampaignMapAssetKind.SeaHarbor, "norfolk-harbor", new[] { 7, 3, 5 }, topN: 0);
        AssertEqual("asset:SeaHarbor:norfolk-harbor:3", sig);

        var negativeTopN = DefenseThreatSignature.ForAsset(
            CampaignMapAssetKind.SeaHarbor, "norfolk-harbor", new[] { 7, 3, 5 }, topN: -2);
        AssertEqual("asset:SeaHarbor:norfolk-harbor:3", negativeTopN);
    }

    private static void AssertEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception("expected " + expected + " but got " + actual);
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(label + ": expected " + expected + " got " + actual);
    }

    private static void AssertContains(string value, string expected, string label)
    {
        if (value == null || !value.Contains(expected))
            throw new Exception(label + ": expected '" + value + "' to contain '" + expected + "'");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void AssertNear(float expected, float actual, float tolerance, string label)
    {
        if (float.IsNaN(expected) || float.IsInfinity(expected) || float.IsNaN(actual) || float.IsInfinity(actual))
            throw new Exception(label + ": expected finite values but got expected " + expected + " actual " + actual);
        if (Math.Abs(expected - actual) > tolerance)
            throw new Exception(label + ": expected " + expected + " got " + actual);
    }

    private static void AssertThrows(Action action, string label)
    {
        try
        {
            action();
        }
        catch
        {
            return;
        }

        throw new Exception(label + ": expected exception");
    }

    private static void PackageAggregatorPicksSmallerAdequateOverRemoteOversized()
    {
        var local1 = MakeDefenseCandidate(id: 1, str: 3000f, mor: 0.85f, ready: 2f, distance: 50f, tier: CandidateTier.Local);
        var local2 = MakeDefenseCandidate(id: 2, str: 3000f, mor: 0.85f, ready: 2f, distance: 60f, tier: CandidateTier.Local);
        var crossMap = MakeDefenseCandidate(id: 99, str: 20000f, mor: 0.9f, ready: 2f, distance: 800f, tier: CandidateTier.CrossMap);

        var result = DefensePackageAggregator.Select(
            candidates: new[] { local1, local2, crossMap },
            desiredStrength: 4500f,
            caution: 0.2f,
            aggression: 0f);

        AssertTrue(result.SelectedPackage.Count >= 1 && result.SelectedPackage.Count <= 2,
            "should pick 1-2 local candidates, not the cross-map army");
        AssertTrue(result.Adequate, "package should clear adequate threshold");
        AssertTrue(!result.Understrength, "should not be understrength");
        AssertTrue(result.Suppressed.Exists(s => s.UnitInstanceId == 99),
            "cross-map army should be suppressed");
        AssertTrue(!result.SelectedPackage.Exists(c => c.UnitInstanceId == 99),
            "cross-map army must not be in selected package");
    }

    private static void PackageAggregatorStopsAtOvershootGuard()
    {
        var local1 = MakeDefenseCandidate(1, 6000f, 0.9f, 2f, 50f, CandidateTier.Local);
        var local2 = MakeDefenseCandidate(2, 6000f, 0.9f, 2f, 50f, CandidateTier.Local);
        var local3 = MakeDefenseCandidate(3, 6000f, 0.9f, 2f, 50f, CandidateTier.Local);

        var result = DefensePackageAggregator.Select(
            candidates: new[] { local1, local2, local3 },
            desiredStrength: 5000f,
            caution: 0.2f, aggression: 0f);

        AssertEqual(1, result.SelectedPackage.Count);
        AssertTrue(result.Adequate, "single local should clear desired");
    }

    private static void PackageAggregatorEmitsUnderstrengthFlag()
    {
        var local1 = MakeDefenseCandidate(1, 1500f, 0.7f, 1f, 50f, CandidateTier.Local);

        var result = DefensePackageAggregator.Select(
            candidates: new[] { local1 },
            desiredStrength: 6000f,
            caution: 0.2f, aggression: 0f);

        AssertTrue(!result.Adequate, "single understrength brigade should not be adequate");
        AssertTrue(result.Understrength, "should be flagged understrength");
        AssertEqual(1, result.SelectedPackage.Count);
    }

    private static void PackageAggregatorSuppressesOvermatchReason()
    {
        var oversized = MakeDefenseCandidate(1, 30000f, 0.9f, 2f, 50f, CandidateTier.Local);
        var rightSized = MakeDefenseCandidate(2, 2500f, 0.85f, 2f, 60f, CandidateTier.Local);

        var result = DefensePackageAggregator.Select(
            candidates: new[] { oversized, rightSized },
            desiredStrength: 2000f,
            caution: 0.5f, aggression: 0f);

        AssertTrue(result.SelectedPackage.Count >= 1, "should select at least one candidate");
        AssertTrue(result.Suppressed.Exists(s => s.UnitInstanceId == 1 && s.Reason == "overmatch"),
            "oversized army should be suppressed for overmatch");
    }

    private static void PackageAggregatorDeterministicOrderOnTiedScores()
    {
        // Two candidates with identical strength/morale/readiness/distance/tier
        // produce identical scores. The tie-break on UnitInstanceId must put the
        // lower id first regardless of input enumeration order.
        var b = MakeDefenseCandidate(id: 7, str: 3000f, mor: 0.85f, ready: 2f, distance: 50f, tier: CandidateTier.Local);
        var a = MakeDefenseCandidate(id: 3, str: 3000f, mor: 0.85f, ready: 2f, distance: 50f, tier: CandidateTier.Local);

        var resultBA = DefensePackageAggregator.Select(new[] { b, a }, 4500f, 0.2f, 0f);
        var resultAB = DefensePackageAggregator.Select(new[] { a, b }, 4500f, 0.2f, 0f);

        AssertTrue(resultBA.SelectedPackage.Count == resultAB.SelectedPackage.Count,
            "package size must be input-order independent");
        for (int i = 0; i < resultBA.SelectedPackage.Count; i++)
        {
            AssertEqual(resultBA.SelectedPackage[i].UnitInstanceId,
                        resultAB.SelectedPackage[i].UnitInstanceId);
        }
        if (resultBA.SelectedPackage.Count > 0)
            AssertEqual(3, resultBA.SelectedPackage[0].UnitInstanceId);
    }

    private static DefenseCandidate MakeDefenseCandidate(int id, float str, float mor, float ready, float distance, CandidateTier tier)
    {
        return new DefenseCandidate
        {
            UnitInstanceId = id,
            ActiveStrength = str,
            Morale = mor,
            ReadinessStep = ready,
            DistanceToThreat = distance,
            Tier = tier
        };
    }

    private static void CooldownTableExtendsOnThreatRedetection()
    {
        var table = new DefenseCooldownTable();
        table.MarkActive("sif:42:Hampton:Boston", cooldownDays: 3);
        table.Tick();
        table.MarkActive("sif:42:Hampton:Boston", cooldownDays: 3);
        AssertEqual(3, table.RemainingDays("sif:42:Hampton:Boston"));
    }

    private static void CooldownTableDecrementsOncePerTick()
    {
        var table = new DefenseCooldownTable();
        table.MarkRecovered("raid:7:wilmington-harbor", cooldownDays: 4);
        AssertEqual(4, table.RemainingDays("raid:7:wilmington-harbor"));
        table.Tick();
        AssertEqual(3, table.RemainingDays("raid:7:wilmington-harbor"));
        table.Tick();
        AssertEqual(2, table.RemainingDays("raid:7:wilmington-harbor"));
    }

    private static void CooldownTableExpiresAtZero()
    {
        var table = new DefenseCooldownTable();
        table.MarkRecovered("asset:SeaHarbor:wilmington-harbor:1,2,3", cooldownDays: 1);
        table.Tick();
        AssertEqual(0, table.RemainingDays("asset:SeaHarbor:wilmington-harbor:1,2,3"));
        AssertTrue(!table.IsActive("asset:SeaHarbor:wilmington-harbor:1,2,3"),
            "expired entry should report not-active");
    }

    // -----------------------------------------------------------------------
    // DefenseIntentLedger tests (#1-#13)
    // -----------------------------------------------------------------------

    // Test #1: coastal guard response suppresses cross-map candidate.
    private static void DefenseLedgerCoastalGuardForbidsCrossMap()
    {
        var input = MakeDefenseInput(1);
        input.GuardCandidateAssets.Add(new CampaignMapAsset
        {
            Kind = CampaignMapAssetKind.SeaHarbor,
            Name = "wilmington-harbor",
            Owner = 1,
            StrategicRole = AssetStrategicRole.BlockadeRunnerPort
        });
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 99,
            UnitName = "XII Corps",
            ActiveStrength = 18000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.CrossMap,
            DistanceToThreat = 1200f
        });

        var output = DefenseIntentLedger.Build(input);

        AssertTrue(output.Responses.Count >= 1, "expected at least one response");
        var guardResponse = output.Responses.Find(r =>
            r.Threat != null && r.Threat.Posture == DefensePosture.CoastalGuard);
        AssertTrue(guardResponse != null, "expected a CoastalGuard response");
        AssertEqual(0, guardResponse.SelectedPackage.Count);
        var suppressed99 = guardResponse.Suppressed.Find(s => s.UnitInstanceId == 99);
        AssertTrue(suppressed99 != null, "expected id=99 to be suppressed");
        AssertEqual("forbidden-cross-map", suppressed99.Reason);
    }

    // Test #2: minor raid response suppresses cross-map candidate.
    private static void DefenseLedgerMinorRaidForbidsCrossMap()
    {
        var input = MakeDefenseInput(1);
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.RaidForce,
            RaidGroupInstanceId = 7,
            AssetName = "hampton-harbor",
            EnemyStrength = 1500f
        });
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 99,
            UnitName = "Far Army",
            ActiveStrength = 20000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.CrossMap,
            DistanceToThreat = 1200f
        });

        var output = DefenseIntentLedger.Build(input);

        AssertTrue(output.Responses.Count >= 1, "expected at least one response");
        var raidResponse = output.Responses[0];
        AssertEqual(DefensePosture.ActiveInvasion, raidResponse.Threat.Posture);
        AssertEqual(ThreatScale.Raid, raidResponse.Threat.Scale);
        var suppressed99 = raidResponse.Suppressed.Find(s => s.UnitInstanceId == 99);
        AssertTrue(suppressed99 != null, "expected id=99 to be suppressed in raid response");
        AssertEqual("forbidden-cross-map", suppressed99.Reason);
    }

    // Test #3: decisive landing allows cross-theater candidates.
    private static void DefenseLedgerDecisiveLandingAllowsCrossTheater()
    {
        var input = MakeDefenseInput(1);
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.SeaInvasion,
            InvasionForceInstanceId = 42,
            SpotName = "hampton-spot",
            SourcePortName = "boston",
            LandedSignal = true,
            AssetName = "norfolk-harbor",
            AssetRole = AssetStrategicRole.KeyFort,
            EnemyStrength = 12000f
        });
        // Same-theater understrength division
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 1,
            UnitName = "Local Division",
            ActiveStrength = 4000f,
            Morale = 0.8f,
            ReadinessStep = 2f,
            Tier = CandidateTier.SameTheater,
            DistanceToThreat = 80f
        });
        // Adjacent-theater strong army
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 2,
            UnitName = "Adjacent Army",
            ActiveStrength = 15000f,
            Morale = 0.85f,
            ReadinessStep = 2f,
            Tier = CandidateTier.AdjacentTheater,
            DistanceToThreat = 300f
        });

        var output = DefenseIntentLedger.Build(input);

        AssertTrue(output.Responses.Count >= 1, "expected at least one response");
        var response = output.Responses[0];
        AssertEqual(DefensePosture.ActiveInvasion, response.Threat.Posture);
        AssertEqual(ThreatScale.DecisiveLanding, response.Threat.Scale);

        // Adjacent-theater army should be in the selected package.
        bool hasAdjacentArmy = response.SelectedPackage.Find(c => c.UnitInstanceId == 2) != null;
        AssertTrue(hasAdjacentArmy, "expected adjacent-theater army (id=2) in SelectedPackage");

        // Escalation reason or adjacent-theater candidate present.
        bool hasEscalation = response.Threat.EscalationReason != null &&
                             response.Threat.EscalationReason.Contains("cross-theater");
        AssertTrue(hasEscalation, "expected EscalationReason to contain 'cross-theater'");
    }

    // Test #4: same-theater adequate package beats remote oversized cross-map.
    private static void DefenseLedgerSameTheaterAdequateBeatsRemoteOversized()
    {
        var input = MakeDefenseInput(1);
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.SeaInvasion,
            InvasionForceInstanceId = 5,
            SpotName = "charleston-spot",
            SourcePortName = "norfolk",
            LandedSignal = true,
            AssetName = "wilmington-harbor",
            AssetRole = AssetStrategicRole.BlockadeRunnerPort,
            EnemyStrength = 4000f
        });
        // Two same-theater brigades (combined EffectiveStrength ~5400 > desired 6000*0.75=4500)
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 1,
            UnitName = "Brigade Alpha",
            ActiveStrength = 3000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.SameTheater,
            DistanceToThreat = 60f
        });
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 2,
            UnitName = "Brigade Beta",
            ActiveStrength = 3000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.SameTheater,
            DistanceToThreat = 70f
        });
        // Cross-map oversized army (should be suppressed: Landing scale → forbidden-cross-map or overmatch)
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 99,
            UnitName = "Far Army",
            ActiveStrength = 20000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.CrossMap,
            DistanceToThreat = 1200f
        });

        var output = DefenseIntentLedger.Build(input);

        AssertTrue(output.Responses.Count >= 1, "expected at least one response");
        var response = output.Responses[0];

        // Same-theater brigades should be selected.
        bool has1 = response.SelectedPackage.Find(c => c.UnitInstanceId == 1) != null;
        bool has2 = response.SelectedPackage.Find(c => c.UnitInstanceId == 2) != null;
        AssertTrue(has1, "expected brigade 1 in SelectedPackage");
        AssertTrue(has2, "expected brigade 2 in SelectedPackage");

        // Cross-map unit should be suppressed with forbidden-cross-map or overmatch.
        var sup99 = response.Suppressed.Find(s => s.UnitInstanceId == 99);
        AssertTrue(sup99 != null, "expected id=99 to be suppressed");
        AssertTrue(sup99.Reason == "forbidden-cross-map" ||
                   sup99.Reason == "overmatch" ||
                   sup99.Reason == "worse-tier" ||
                   sup99.Reason == "national-emergency-required",
            $"expected reason forbidden-cross-map, overmatch, worse-tier, or national-emergency-required, got {sup99.Reason}");
    }

    // Test #5: guard budget caps low-value ports.
    // NOTE: Candidates are consumed globally; once a candidate is assigned to the first
    // guard response it is not available for subsequent ones. With 6 assets each
    // wanting one local unit at ~2250 effective strength and a budget of 6000, at most
    // floor(6000/2250)=2 units can be assigned before the budget is exhausted.
    private static void DefenseLedgerGuardBudgetCapsLowValuePorts()
    {
        var input = MakeDefenseInput(1);
        input.TotalAllianceEffectiveStrength = 60000f;
        input.GuardBudgetFraction = 0.10f; // budget = 6000

        for (int i = 0; i < 6; i++)
        {
            input.GuardCandidateAssets.Add(new CampaignMapAsset
            {
                Kind = CampaignMapAssetKind.SeaHarbor,
                Name = $"rear-port-{i}",
                Owner = 1,
                StrategicRole = AssetStrategicRole.RearSafePort
            });
            input.Candidates.Add(new DefenseCandidate
            {
                UnitInstanceId = 10 + i,
                UnitName = $"Guard Unit {i}",
                ActiveStrength = 2500f,
                Morale = 0.9f,
                ReadinessStep = 2f,
                Tier = CandidateTier.Local,
                DistanceToThreat = 50f
            });
        }

        var output = DefenseIntentLedger.Build(input);

        // Count cap-reached responses.
        int capReachedCount = 0;
        int assignedCount = 0;
        foreach (var r in output.Responses)
        {
            if (r.Threat.Posture != DefensePosture.CoastalGuard) continue;
            if (r.SelectedPackage.Count > 0)
                assignedCount++;
            else
            {
                bool hasCap = r.Suppressed.Find(s => s.Reason == "cap-reached") != null;
                if (hasCap) capReachedCount++;
            }
        }

        // EffectiveStrength per unit ≈ 2500 * 0.9 * 1.0 = 2250; budget = 6000
        // At most floor(6000/2250) = 2 can be assigned; at least 4 must be cap-reached.
        AssertTrue(assignedCount <= 2, $"expected at most 2 assigned packages, got {assignedCount}");
        AssertTrue(capReachedCount >= 4, $"expected at least 4 cap-reached responses, got {capReachedCount}");
    }

    // Test #6: active invasion persists through a favorable tick (strength drop doesn't flip to Recovered).
    private static void DefenseLedgerActiveInvasionPersistsThroughFavorableTick()
    {
        var input = MakeDefenseInput(1);
        var src = new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.SeaInvasion,
            InvasionForceInstanceId = 10,
            SpotName = "mobile-spot",
            SourcePortName = "new-orleans",
            LandedSignal = true,
            AssetName = "mobile-harbor",
            AssetRole = AssetStrategicRole.BlockadeRunnerPort,
            EnemyStrength = 5000f
        };
        input.Threats.Add(src);
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 1,
            UnitName = "Local Brigade",
            ActiveStrength = 8000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.Local,
            DistanceToThreat = 40f
        });

        // Tick 1: first build.
        var out1 = DefenseIntentLedger.Build(input);
        AssertTrue(out1.Responses.Count >= 1, "tick1: expected at least one response");
        AssertEqual(DefensePosture.ActiveInvasion, out1.Responses[0].Threat.Posture);
        string sig = out1.Responses[0].Threat.Signature;
        AssertTrue(input.Cooldown.IsActive(sig), "tick1: cooldown should be active after build");

        // Tick 2: slight strength drop — still not collapsed (LandedSignal=true, not Recovered).
        src.EnemyStrength = 4500f;
        var out2 = DefenseIntentLedger.Build(input);
        AssertTrue(out2.Responses.Count >= 1, "tick2: expected at least one response");
        AssertEqual(DefensePosture.ActiveInvasion, out2.Responses[0].Threat.Posture);
        // Cooldown counter should still be active (not counting down yet since we re-mark each Active tick).
        AssertTrue(input.Cooldown.IsActive(sig), "tick2: cooldown should still be active");
    }

    // Test #7: recovered threat releases after cooldown expires.
    // MarkRecovered is idempotent per Active cycle: the first call sets the
    // counter, subsequent calls while flagged are no-ops, MarkActive resets
    // the flag. The test runs MarkActive then a sequence of MarkRecovered+Tick
    // pairs and asserts the counter ticks down once per Tick, not once per call.
    private static void DefenseLedgerRecoveredThreatReleasesAfterCooldown()
    {
        var input = MakeDefenseInput(1);
        input.CooldownDays = 2;

        var src = new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.SeaInvasion,
            InvasionForceInstanceId = 20,
            SpotName = "savannah-spot",
            SourcePortName = "baltimore",
            LandedSignal = true,
            VanillaCollapsed = false,
            AssetName = "savannah-harbor",
            AssetRole = AssetStrategicRole.BlockadeRunnerPort,
            EnemyStrength = 5000f
        };
        input.Threats.Add(src);
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 1,
            UnitName = "Garrison",
            ActiveStrength = 8000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.Local,
            DistanceToThreat = 30f
        });

        // Tick 1: Active.
        var out1 = DefenseIntentLedger.Build(input);
        AssertEqual(DefensePosture.ActiveInvasion, out1.Responses[0].Threat.Posture);
        string sig = out1.Responses[0].Threat.Signature;
        AssertTrue(input.Cooldown.IsActive(sig), "tick1: should be active after Active build");
        input.Cooldown.Tick();

        // Tick 2: Recovered (VanillaCollapsed=true). Builder emits Recovered + calls MarkRecovered.
        src.VanillaCollapsed = true;
        src.LandedSignal = false;
        var out2 = DefenseIntentLedger.Build(input);
        AssertTrue(out2.Responses.Count >= 1, "tick2: expected response");
        AssertEqual(DefensePosture.Recovered, out2.Responses[0].Threat.Posture);
        // After MarkRecovered(sig, 2), IsActive should be true (r=2).
        AssertTrue(input.Cooldown.IsActive(sig), "tick2: IsActive should be true after MarkRecovered");
        input.Cooldown.Tick(); // r: 2→1

        // Tick 3: Recovered again. Cooldown still active (r=1). Builder should NOT re-mark
        // (so it stays at 1). Assert IsActive=true.
        var out3 = DefenseIntentLedger.Build(input);
        AssertEqual(DefensePosture.Recovered, out3.Responses[0].Threat.Posture);
        AssertTrue(input.Cooldown.IsActive(sig), "tick3: IsActive should still be true");
        input.Cooldown.Tick(); // r: 1→0

        // Tick 4: Recovered, cooldown expired (r=0). Builder emits empty response.
        var out4 = DefenseIntentLedger.Build(input);
        // Either the response is empty (no IsActive) or it's not included.
        AssertTrue(!input.Cooldown.IsActive(sig), "tick4: IsActive should be false (expired)");
        // The builder emits an empty response when cooldown is expired.
        var r4 = out4.Responses.Find(r => r.Threat != null && r.Threat.Signature == sig);
        bool emptyOrAbsent = r4 == null ||
                             (r4.SelectedPackage.Count == 0 && !r4.Adequate);
        AssertTrue(emptyOrAbsent, "tick4: response should be absent or empty when cooldown expired");
    }

    // Test #8: player CIC short-circuits the entire ledger.
    private static void DefenseLedgerPlayerCicShortCircuitsAlliance()
    {
        var input = MakeDefenseInput(0);
        input.PlayerIsCIC = true;
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.SeaInvasion,
            InvasionForceInstanceId = 1,
            SpotName = "annapolis-spot",
            SourcePortName = "norfolk",
            LandedSignal = true,
            EnemyStrength = 8000f
        });
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 1,
            UnitName = "Division A",
            ActiveStrength = 10000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.Local,
            DistanceToThreat = 50f
        });

        var output = DefenseIntentLedger.Build(input);

        AssertEqual(0, output.Responses.Count);
    }

    // Test #9: player-controlled candidate is suppressed; AI candidate is selected.
    private static void DefenseLedgerWlSubordinateProtectsOnlyMarkedUnit()
    {
        var input = MakeDefenseInput(1);
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.SeaInvasion,
            InvasionForceInstanceId = 30,
            SpotName = "beaufort-spot",
            SourcePortName = "savannah",
            LandedSignal = true,
            AssetName = "beaufort-harbor",
            AssetRole = AssetStrategicRole.BlockadeRunnerPort,
            EnemyStrength = 6000f
        });
        // Player-controlled candidate — must be suppressed.
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 1,
            UnitName = "Player Division",
            ActiveStrength = 8000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.Local,
            DistanceToThreat = 40f,
            PlayerControlled = true
        });
        // AI candidate — should be selected.
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 2,
            UnitName = "AI Brigade",
            ActiveStrength = 8000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.Local,
            DistanceToThreat = 50f,
            PlayerControlled = false
        });

        var output = DefenseIntentLedger.Build(input);

        AssertTrue(output.Responses.Count >= 1, "expected at least one response");
        var response = output.Responses[0];
        AssertEqual(DefensePosture.ActiveInvasion, response.Threat.Posture);

        var sup1 = response.Suppressed.Find(s => s.UnitInstanceId == 1);
        AssertTrue(sup1 != null, "expected id=1 (player) to be suppressed");
        AssertEqual("player-controlled", sup1.Reason);

        bool hasId2 = response.SelectedPackage.Find(c => c.UnitInstanceId == 2) != null;
        AssertTrue(hasId2, "expected AI candidate (id=2) in SelectedPackage");
    }

    // Test #10: critical-front candidate rejected unless decisive (two sub-fixtures).
    private static void DefenseLedgerCriticalFrontCandidateRejectedUnlessDecisive()
    {
        // Sub-fixture A: Landing scale (not decisive) — critical-front suppressed, package empty.
        {
            var input = MakeDefenseInput(1);
            input.Threats.Add(new DefenseThreatSource
            {
                Kind = DefenseThreatSourceKind.SeaInvasion,
                InvasionForceInstanceId = 50,
                SpotName = "port-royal-spot",
                SourcePortName = "norfolk",
                LandedSignal = true,
                AssetName = "port-royal-harbor",
                AssetRole = AssetStrategicRole.BlockadeRunnerPort,
                EnemyStrength = 4000f // Landing scale
            });
            input.Candidates.Add(new DefenseCandidate
            {
                UnitInstanceId = 1,
                UnitName = "Critical Front Division",
                ActiveStrength = 6000f,
                Morale = 0.9f,
                ReadinessStep = 2f,
                Tier = CandidateTier.SameTheater,
                DistanceToThreat = 100f,
                CriticalFront = true
            });

            var output = DefenseIntentLedger.Build(input);

            AssertTrue(output.Responses.Count >= 1, "fixture-A: expected response");
            var response = output.Responses[0];
            var sup1 = response.Suppressed.Find(s => s.UnitInstanceId == 1);
            AssertTrue(sup1 != null, "fixture-A: expected id=1 (critical-front) suppressed");
            AssertEqual("critical-front", sup1.Reason);
            AssertEqual(0, response.SelectedPackage.Count);
        }

        // Sub-fixture B: DecisiveLanding scale, critical-front is the only candidate — should be selected.
        {
            var input = MakeDefenseInput(1);
            input.Threats.Add(new DefenseThreatSource
            {
                Kind = DefenseThreatSourceKind.SeaInvasion,
                InvasionForceInstanceId = 51,
                SpotName = "richmond-spot",
                SourcePortName = "norfolk",
                LandedSignal = true,
                AssetName = "richmond-harbor",
                AssetRole = AssetStrategicRole.CapitalApproach,
                EnemyStrength = 12000f // DecisiveLanding scale
            });
            input.Candidates.Add(new DefenseCandidate
            {
                UnitInstanceId = 1,
                UnitName = "Critical Front Division",
                ActiveStrength = 6000f,
                Morale = 0.9f,
                ReadinessStep = 2f,
                Tier = CandidateTier.SameTheater,
                DistanceToThreat = 100f,
                CriticalFront = true
            });

            var output = DefenseIntentLedger.Build(input);

            AssertTrue(output.Responses.Count >= 1, "fixture-B: expected response");
            var response = output.Responses[0];
            bool hasId1 = response.SelectedPackage.Find(c => c.UnitInstanceId == 1) != null;
            AssertTrue(hasId1, "fixture-B: expected critical-front candidate (id=1) in SelectedPackage");
            var sup1 = response.Suppressed.Find(s => s.UnitInstanceId == 1);
            AssertTrue(sup1 == null, "fixture-B: critical-front candidate should NOT be in Suppressed");
        }
    }

    // Test #11: river harbor detected via AssetProximity (no SeaInvasionForce).
    private static void DefenseLedgerRiverHarborDetectsWithoutSif()
    {
        var input = MakeDefenseInput(0);
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.AssetProximity,
            AssetKind = CampaignMapAssetKind.RiverHarbor,
            AssetName = "vicksburg-harbor",
            EnemyStrength = 4000f,
            EnemyInstanceIds = new[] { 1, 2, 3 }
        });

        var output = DefenseIntentLedger.Build(input);

        AssertTrue(output.Responses.Count >= 1, "expected at least one response");
        var response = output.Responses[0];
        AssertEqual(DefensePosture.ActiveInvasion, response.Threat.Posture);
        AssertTrue(response.Threat.Signature.StartsWith("asset:RiverHarbor:"),
            $"expected signature to start with 'asset:RiverHarbor:', got '{response.Threat.Signature}'");
    }

    // Test #12: RaidForce source produces Raid scale and correct signature prefix.
    private static void DefenseLedgerRaidforceCoverage()
    {
        var input = MakeDefenseInput(1);
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.RaidForce,
            RaidGroupInstanceId = 7,
            AssetName = "hampton-spot",
            EnemyStrength = 2000f
        });

        var output = DefenseIntentLedger.Build(input);

        AssertTrue(output.Responses.Count >= 1, "expected at least one response");
        var response = output.Responses[0];
        AssertEqual(ThreatScale.Raid, response.Threat.Scale);
        AssertTrue(response.Threat.Signature.StartsWith("raid:7:"),
            $"expected signature to start with 'raid:7:', got '{response.Threat.Signature}'");
    }

    // Test #13: when SeaInvasion gate is off, AssetProximity fallback works without throw.
    private static void DefenseLedgerDebugSeainvasionsactiveOffFallsBack()
    {
        var input = MakeDefenseInput(1);
        // No SeaInvasion source — runtime fell back to AssetProximity.
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.AssetProximity,
            AssetKind = CampaignMapAssetKind.SeaHarbor,
            AssetName = "norfolk-harbor",
            EnemyStrength = 4000f,
            EnemyInstanceIds = new[] { 5, 6 }
        });
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 1,
            UnitName = "Local Guard",
            ActiveStrength = 6000f,
            Morale = 0.85f,
            ReadinessStep = 2f,
            Tier = CandidateTier.Local,
            DistanceToThreat = 50f
        });

        DefenseIntentLedgerOutput output = null;
        Exception caught = null;
        try { output = DefenseIntentLedger.Build(input); }
        catch (Exception ex) { caught = ex; }

        AssertTrue(caught == null, $"expected no exception, got: {caught}");
        AssertTrue(output != null && output.Responses.Count >= 1, "expected at least one response");
        AssertEqual(DefensePosture.ActiveInvasion, output.Responses[0].Threat.Posture);
    }

    // Test: every emitted DefenseResponse has a non-empty TelemetrySignature
    // that starts with the posture name, so Task 14 telemetry has a stable key.
    private static void DefenseLedgerTelemetrySignaturePopulated()
    {
        var input = MakeDefenseInput(1);
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.SeaInvasion,
            InvasionForceInstanceId = 99,
            SpotName = "charlestown-spot",
            SourcePortName = "hampton-roads",
            LandedSignal = true,
            AssetName = "charlestown-harbor",
            AssetRole = AssetStrategicRole.BlockadeRunnerPort,
            EnemyStrength = 6000f
        });
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 1,
            UnitName = "Guard Brigade",
            ActiveStrength = 9000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.Local,
            DistanceToThreat = 40f
        });

        var output = DefenseIntentLedger.Build(input);

        AssertTrue(output.Responses.Count >= 1, "expected at least one response");
        foreach (var r in output.Responses)
        {
            AssertTrue(!string.IsNullOrEmpty(r.TelemetrySignature),
                $"TelemetrySignature must be non-empty for sig={r.Threat?.Signature}");
            string expectedPrefix = r.Threat.Posture.ToString();
            AssertTrue(r.TelemetrySignature.StartsWith(expectedPrefix),
                $"TelemetrySignature '{r.TelemetrySignature}' must start with posture '{expectedPrefix}'");
        }
    }

    private static void DefenseTelemetrySummaryCompressesResponseBurst()
    {
        var output = new DefenseIntentLedgerOutput
        {
            AllianceId = 1,
            Signature = "sig-a"
        };
        output.Responses.Add(new DefenseResponse
        {
            Threat = new DefenseThreat { Posture = DefensePosture.ActiveInvasion },
            SelectedPackage = { new DefenseCandidate { UnitInstanceId = 1 } }
        });
        output.Responses.Add(new DefenseResponse
        {
            Threat = new DefenseThreat { Posture = DefensePosture.CoastalGuard },
            Suppressed = { new DefenseSuppression { UnitInstanceId = 2, Reason = "forbidden-cross-map" } }
        });

        string summary = DefenseIntentTelemetry.Summary(output);

        AssertEqual("responses=2 active=1 guard=1 selected=1 suppressed=1 signature=sig-a", summary);
    }

    // Test: pure builder must accept allianceId=2 (Europe) without throwing.
    // Vanilla AICampaign.aifaction includes alliance 2 for some CSA-side
    // scenarios (decompile lines 9226/9249/9271/9367). The runtime arrays on
    // StrategicCoordinator are length 2 — the bound-check that short-circuits
    // alliance 2+ lives at the coordinator / patch layer. The builder itself
    // is alliance-agnostic and must not throw on any non-negative ID.
    private static void DefenseLedgerDoesNotCrashOnEuropeAllianceIndex()
    {
        var input = MakeDefenseInput(allianceId: 2);
        DefenseIntentLedgerOutput output = null;
        Exception caught = null;
        try { output = DefenseIntentLedger.Build(input); }
        catch (Exception ex) { caught = ex; }

        AssertTrue(caught == null, $"expected no exception for allianceId=2, got: {caught}");
        AssertTrue(output != null, "Build must return a non-null output for allianceId=2");
        AssertEqual(2, output.AllianceId);
        AssertTrue(output.Responses != null,
            "Responses list must not be null even for europe alliance");
    }

    private static void DefenseLedgerAssetProximityStaysLocalAndCannotCustomOrder()
    {
        var input = MakeDefenseInput(1);
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.AssetProximity,
            AssetKind = CampaignMapAssetKind.SeaHarbor,
            AssetName = "annapolis-port",
            EnemyStrength = 9000f,
            EnemyInstanceIds = new[] { 1, 2, 3 },
            X = 10f,
            Z = 10f
        });
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 1,
            UnitName = "Local Garrison",
            ActiveStrength = 4000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Theater = Theater.East,
            Tier = CandidateTier.Local,
            DistanceToThreat = 20f
        });
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 2,
            UnitName = "Distant Army",
            ActiveStrength = 25000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Theater = Theater.West,
            Tier = CandidateTier.AdjacentTheater,
            TransferDonorAllowed = true,
            DirectMovementAllowed = true,
            DefensiveAllowed = true,
            DistanceToThreat = 400f
        });

        var output = DefenseIntentLedger.Build(input);

        AssertTrue(output.Responses.Count >= 1, "expected asset-proximity response");
        var response = output.Responses[0];
        AssertEqual(DefenseThreatSourceKind.AssetProximity, response.Threat.SourceKind);
        AssertTrue(response.SelectedPackage.Find(c => c.UnitInstanceId == 1) != null,
            "local garrison should remain eligible");
        AssertTrue(response.SelectedPackage.Find(c => c.UnitInstanceId == 2) == null,
            "asset proximity must not pull adjacent theater army");
        var suppressed = response.Suppressed.Find(s => s.UnitInstanceId == 2);
        AssertTrue(suppressed != null, "adjacent theater army should be suppressed");
        AssertEqual("asset-proximity-local-only", suppressed.Reason);
        AssertTrue(!DefenseCustomOrderPolicy.RequiresCustomOrder(response),
            "asset proximity must never issue custom MoveUnitTo orders");
    }

    private static void DefenseLedgerDonorTheaterBudgetBlocksCriticalFrontExport()
    {
        var input = MakeDefenseInput(1);
        input.FrontLedger = FrontSectorLedger.Build(new[]
        {
            new FrontSectorInput
            {
                SectorKey = "ThreatCoast",
                Theater = Theater.Coast,
                OwnStrength = 3000f,
                EnemyStrength = 9000f,
                StrategicImportance = 0.7f,
                IsCritical = false
            },
            new FrontSectorInput
            {
                SectorKey = "VirginiaCapitalCorridor",
                Theater = Theater.East,
                OwnStrength = 16000f,
                EnemyStrength = 13000f,
                StrategicImportance = 1.0f,
                IsCritical = true,
                AverageMorale = 0.8f,
                AverageReadiness = 0.8f,
                AverageSupply = 0.8f
            }
        }, new FrontLedgerOptions { MinimumHoldRatio = 0.9f, CriticalHoldRatioBonus = 0.25f });
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.SeaInvasion,
            InvasionForceInstanceId = 44,
            SpotName = "norfolk-spot",
            SourcePortName = "baltimore",
            LandedSignal = true,
            AssetName = "richmond-approach",
            AssetRole = AssetStrategicRole.CapitalApproach,
            EnemyStrength = 12000f,
            X = 150f,
            Z = 200f
        });
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 1,
            UnitName = "Eastern Field Army",
            ActiveStrength = 9000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Theater = Theater.East,
            SectorKey = "VirginiaCapitalCorridor",
            Tier = CandidateTier.AdjacentTheater,
            TransferDonorAllowed = true,
            DirectMovementAllowed = true,
            DefensiveAllowed = true
        });

        var output = DefenseIntentLedger.Build(input);
        var response = output.Responses[0];

        AssertTrue(response.SelectedPackage.Find(c => c.UnitInstanceId == 1) == null,
            "critical front army must not be exported below hold ratio");
        var suppressed = response.Suppressed.Find(s => s.UnitInstanceId == 1);
        AssertTrue(suppressed != null, "expected donor army to be suppressed");
        AssertTrue(suppressed.Reason == "min-hold" || suppressed.Reason == "critical-sector-budget",
            $"expected min-hold or critical-sector-budget, got {suppressed.Reason}");
    }

    private static void DefenseLedgerFormationDirectiveBlocksDefenseMovement()
    {
        var input = MakeDefenseInput(1);
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.SeaInvasion,
            InvasionForceInstanceId = 55,
            SpotName = "mobile-spot",
            SourcePortName = "new-orleans",
            LandedSignal = true,
            AssetName = "mobile",
            AssetRole = AssetStrategicRole.BlockadeRunnerPort,
            EnemyStrength = 4000f
        });
        input.Candidates.Add(new DefenseCandidate
        {
            UnitInstanceId = 1,
            UnitName = "Recovering Corps",
            ActiveStrength = 9000f,
            Morale = 0.9f,
            ReadinessStep = 2f,
            Tier = CandidateTier.Local,
            HasFormationDirective = true,
            DefensiveAllowed = false,
            DirectMovementAllowed = true,
            TransferDonorAllowed = true
        });

        var output = DefenseIntentLedger.Build(input);
        var response = output.Responses[0];

        AssertEqual(0, response.SelectedPackage.Count);
        var suppressed = response.Suppressed.Find(s => s.UnitInstanceId == 1);
        AssertTrue(suppressed != null, "expected directive-blocked unit to be suppressed");
        AssertEqual("formation-directive", suppressed.Reason);
    }

    private static void DefenseLedgerCapitalDefensePackageIsCapped()
    {
        var input = MakeDefenseInput(1);
        input.TotalAllianceEffectiveStrength = 60000f;
        input.CapitalDefenseBudgetFraction = 0.18f;
        input.Threats.Add(new DefenseThreatSource
        {
            Kind = DefenseThreatSourceKind.SeaInvasion,
            InvasionForceInstanceId = 66,
            SpotName = "richmond-spot",
            SourcePortName = "norfolk",
            LandedSignal = true,
            AssetName = "richmond",
            AssetRole = AssetStrategicRole.CapitalApproach,
            EnemyStrength = 20000f
        });
        for (int i = 0; i < 5; i++)
        {
            input.Candidates.Add(new DefenseCandidate
            {
                UnitInstanceId = 10 + i,
                UnitName = "Capital Reserve " + i,
                ActiveStrength = 7000f,
                Morale = 1f,
                ReadinessStep = 2f,
                Theater = Theater.East,
                Tier = CandidateTier.SameTheater,
                TransferDonorAllowed = true,
                DirectMovementAllowed = true,
                DefensiveAllowed = true
            });
        }

        var output = DefenseIntentLedger.Build(input);
        var response = output.Responses[0];

        float selected = 0f;
        foreach (var candidate in response.SelectedPackage)
            selected += candidate.EffectiveStrength;

        AssertTrue(selected <= 10800f, $"capital package should be capped at 10800 effective strength, got {selected}");
        AssertTrue(response.Suppressed.Find(s => s.Reason == "capital-defense-cap") != null,
            "expected extra capital candidates to be suppressed by cap");
    }

    private static void StrategicMovementBudgetBlocksAreaExportFromHoldSector()
    {
        var front = FrontSectorLedger.Build(new[]
        {
            new FrontSectorInput
            {
                SectorKey = "WashingtonDefenses",
                Theater = Theater.East,
                OwnStrength = 7000f,
                EnemyStrength = 9000f,
                StrategicImportance = 1.0f,
                IsCritical = true,
                AverageMorale = 0.8f,
                AverageReadiness = 0.8f,
                AverageSupply = 0.8f
            },
            new FrontSectorInput
            {
                SectorKey = "OhioValley",
                Theater = Theater.West,
                OwnStrength = 12000f,
                EnemyStrength = 6000f,
                StrategicImportance = 0.5f,
                IsCritical = false
            }
        });

        var decision = StrategicMovementBudget.EvaluateAreaMovement(
            front,
            "WashingtonDefenses",
            "OhioValley",
            4000f);

        AssertTrue(decision != null && !decision.Allowed, "expected area movement to be blocked");
        AssertEqual("min-hold", decision.Reason);
    }

    private static void CommanderAssignmentGuardClearsStalePreviousCommand()
    {
        AssertTrue(
            CommanderAssignmentGuard.ShouldClearPreviousCommand(
                priorCommandExists: true,
                priorIsAssignedTarget: false,
                priorCommandCommanderId: 7,
                assignedCommanderId: 7),
            "same commander on a different previous unit should be cleared");

        AssertTrue(
            !CommanderAssignmentGuard.ShouldClearPreviousCommand(
                priorCommandExists: true,
                priorIsAssignedTarget: true,
                priorCommandCommanderId: 7,
                assignedCommanderId: 7),
            "the target unit should not be cleared");

        AssertTrue(
            !CommanderAssignmentGuard.ShouldClearPreviousCommand(
                priorCommandExists: true,
                priorIsAssignedTarget: false,
                priorCommandCommanderId: 3,
                assignedCommanderId: 7,
                vanillaReplacementWillReadPriorCommander: false),
            "a previous unit already reassigned to another commander should not be touched");

        AssertTrue(
            !CommanderAssignmentGuard.ShouldClearPreviousCommand(
                priorCommandExists: true,
                priorIsAssignedTarget: false,
                priorCommandCommanderId: 7,
                assignedCommanderId: 7,
                vanillaReplacementWillReadPriorCommander: true),
            "vanilla ReplaceCommanderOfUnit still reads the subordinate commander id after AssignCommando");
    }

    private static void CampaignFilterMapGuardBoundsRepeatedNoProgress()
    {
        var guard = new CampaignFilterMapInitializationGuard(maxRepeatedNoProgressReturns: 3);
        var stuck = new CampaignFilterMapState(0, 0, 0, 3, 2, 2, 4);
        var advanced = new CampaignFilterMapState(1, 1, 0, 3, 2, 2, 4);

        AssertTrue(
            !guard.Observe(initialization: true, result: false, stuck, advanced).ForceComplete,
            "normal iterator progress should not be forced complete");

        guard = new CampaignFilterMapInitializationGuard(maxRepeatedNoProgressReturns: 3);
        AssertTrue(!guard.Observe(initialization: true, result: false, stuck, stuck).ForceComplete, "first stuck false should wait");
        AssertTrue(!guard.Observe(initialization: true, result: false, stuck, stuck).ForceComplete, "second stuck false should wait");
        var decision = guard.Observe(initialization: true, result: false, stuck, stuck);
        AssertTrue(decision.ForceComplete, "third repeated no-progress false should force completion");
        AssertEqual("no-progress", decision.Reason);

        decision = guard.ObserveException(initialization: true, new InvalidOperationException("boom"), stuck);
        AssertTrue(decision.ForceComplete, "initialization exception should force completion");

        decision = guard.ObserveException(initialization: false, new NullReferenceException("boom"), stuck);
        AssertTrue(decision.ForceComplete, "runtime null reference should advance one iterator slot and suppress");
        AssertEqual("runtime-exception:NullReferenceException", decision.Reason);

        decision = guard.ObserveException(initialization: false, new InvalidOperationException("boom"), stuck);
        AssertTrue(!decision.ForceComplete, "non-null runtime exceptions should fall through");

        guard = new CampaignFilterMapInitializationGuard(
            maxRepeatedNoProgressReturns: 3,
            maxRuntimeExceptionSuppressionsPerSignature: 1);
        decision = guard.ObserveException(initialization: false, new NullReferenceException("boom"), stuck);
        AssertTrue(decision.ForceComplete, "first runtime null for a cursor signature should suppress");
        decision = guard.ObserveException(initialization: false, new NullReferenceException("boom"), stuck);
        AssertTrue(!decision.ForceComplete, "repeated runtime null at the same cursor signature should surface after cap");

        AssertTrue(
            CampaignFilterMapInitializationGuard.TryAdvanceRuntimeCursor(stuck, out var runtimeAdvanced),
            "plausible runtime cursor should advance");
        AssertEqual(new CampaignFilterMapState(1, 1, 0, 3, 2, 2, 4).Signature(), runtimeAdvanced.Signature());

        AssertTrue(
            CampaignFilterMapInitializationGuard.TryAdvanceRuntimeCursor(
                new CampaignFilterMapState(2, 1, 3, 3, 2, 2, 4),
                out var reset),
            "complete runtime cursor should reset like vanilla");
        AssertEqual(new CampaignFilterMapState(0, 0, -1, 3, 2, 2, 4).Signature(), reset.Signature());

        AssertTrue(
            !CampaignFilterMapInitializationGuard.TryAdvanceRuntimeCursor(
                new CampaignFilterMapState(0, 0, 0, 3, -1, 2, 4),
                out _),
            "runtime cursor without smalltown data should not claim safe advancement");

        string diagnostic = CampaignFilterMapInitializationGuard.BuildRuntimeDiagnostic(
            stuck,
            runtimeAdvanced,
            "lists towns=3 smalltowns=2 iips=2 cbuildings=4");
        AssertContains(diagnostic, "cursor=0/3:0/2:0/2:0/4", "diagnostic should include failing cursor");
        AssertContains(diagnostic, "next=1/3:1/2:1/2:0/4", "diagnostic should include advanced cursor");
        AssertContains(diagnostic, "lists towns=3", "diagnostic should include runtime probe summary");
    }

    private static void CampaignFilterMapGuardDetectsAssignFiltersBootstrapNeeds()
    {
        string[] ready = CampaignFilterMapInitializationGuard.GetMissingAssignFiltersMapNames(
            availableWorkforceReady: true,
            slaveryReady: true,
            tradeAndSupplyReady: true,
            supplyReady: true,
            availableCapitalReady: true,
            transportBottlenecksReady: true,
            marketCapacityReady: true,
            hospitalsReady: true);
        AssertEqual(0, ready.Length);

        string[] missing = CampaignFilterMapInitializationGuard.GetMissingAssignFiltersMapNames(
            availableWorkforceReady: false,
            slaveryReady: true,
            tradeAndSupplyReady: false,
            supplyReady: true,
            availableCapitalReady: true,
            transportBottlenecksReady: false,
            marketCapacityReady: true,
            hospitalsReady: false);
        string joined = string.Join(",", missing);
        AssertContains(joined, "availableworkforce", "assign-filters readiness should name missing workforce map");
        AssertContains(joined, "tradeandsupply", "assign-filters readiness should name missing trade map");
        AssertContains(joined, "transportbottlenecks", "assign-filters readiness should name missing bottleneck map");
        AssertContains(joined, "hospitals", "assign-filters readiness should name missing hospital map");
    }

    private static void StateHandoverGuardRequiresDecisiveSupport()
    {
        AssertTrue(StateHandoverGuard.AllowsHandover(0, 0.70f, 0.30f), "decisive Union support should allow Union handover");
        AssertTrue(StateHandoverGuard.AllowsHandover(1, 0.30f, 0.70f), "decisive CSA support should allow CSA handover");
        AssertTrue(!StateHandoverGuard.AllowsHandover(0, 0.60f, 0.40f), "simple Union majority should not flip state");
        AssertTrue(!StateHandoverGuard.AllowsHandover(1, 0.45f, 0.55f), "simple CSA majority should not flip state");
        AssertTrue(!StateHandoverGuard.AllowsHandover(2, 0.80f, 0.20f), "non-USA/CSA alliance should not be handled");
    }

    private static void FleetPatrolGuardResetsCompletedAiPatrol()
    {
        AssertTrue(
            FleetPatrolResetGuard.ShouldResetAiPatrolToIdle(
                isPlayerFleet: false,
                unitType: 17,
                fleetOrders: 2,
                regimentPaths: 0,
                distanceToStart: 0.5f,
                completionRadius: 1f,
                isRouted: false,
                onRetreat: false,
                inBattle: false,
                withinRotationProcess: false),
            "idle AI fleet inside completion radius should reset to normal orders");

        AssertTrue(
            !FleetPatrolResetGuard.ShouldResetAiPatrolToIdle(
                isPlayerFleet: true,
                unitType: 17,
                fleetOrders: 2,
                regimentPaths: 0,
                distanceToStart: 0.5f,
                completionRadius: 1f,
                isRouted: false,
                onRetreat: false,
                inBattle: false,
                withinRotationProcess: false),
            "player patrol orders should not be changed");

        AssertTrue(
            !FleetPatrolResetGuard.ShouldResetAiPatrolToIdle(
                isPlayerFleet: false,
                unitType: 17,
                fleetOrders: 2,
                regimentPaths: 1,
                distanceToStart: 0.5f,
                completionRadius: 1f,
                isRouted: false,
                onRetreat: false,
                inBattle: false,
                withinRotationProcess: false),
            "moving patrol should not be reset");
    }

    private static void ArtilleryCombineGunTransferPreservesSourceGuns()
    {
        AssertEqual(6, ArtilleryCombineGunTransfer.CalculateGunsToTransfer(isArtillery: true, sourceGuns: 6, sourceTotalMen: 100, transferredMen: 100));
        AssertEqual(3, ArtilleryCombineGunTransfer.CalculateGunsToTransfer(isArtillery: true, sourceGuns: 6, sourceTotalMen: 100, transferredMen: 50));
        AssertEqual(1, ArtilleryCombineGunTransfer.CalculateGunsToTransfer(isArtillery: true, sourceGuns: 6, sourceTotalMen: 100, transferredMen: 1));
        AssertEqual(0, ArtilleryCombineGunTransfer.CalculateGunsToTransfer(isArtillery: false, sourceGuns: 6, sourceTotalMen: 100, transferredMen: 100));
    }

    // -----------------------------------------------------------------------
    // Helper: build a minimal DefenseIntentInput.
    // -----------------------------------------------------------------------

    private static DefenseIntentInput MakeDefenseInput(int allianceId)
    {
        return new DefenseIntentInput
        {
            AllianceId = allianceId,
            PlayerIsCIC = false,
            CICPersonality = default(PersonalityVector),
            TotalAllianceEffectiveStrength = 60000f
        };
    }

    // -----------------------------------------------------------------------
    // PhaseTruthLedger tests
    // -----------------------------------------------------------------------

    private static void PhaseTruthAdvancesWhenTargetAccomplished()
    {
        var input = new PhaseTruthInput
        {
            Plan = new OperationalPlan { Phases = { new Phase { TargetObjectiveId = 29, DeadlineMonth = 12, DeadlineYear = 1862 } } },
            TargetAccomplished = true,
            ObjectiveAvailable = true,
            TargetSectorOwnStrength = 10000f,
            RequiredForce = 5000f,
            CurrentMonth = 6, CurrentYear = 1862
        };
        var output = PhaseTruthLedger.Evaluate(input);
        AssertEqual(PhaseTruthVerdict.TargetAccomplished, output.Verdict);
        AssertEqual(PhaseTruthAction.Advance, output.RecommendedAction);
    }

    private static void PhaseTruthReplansWhenObjectiveUnavailable()
    {
        var input = new PhaseTruthInput
        {
            Plan = new OperationalPlan { Phases = { new Phase { TargetObjectiveId = 29, DeadlineMonth = 12, DeadlineYear = 1862 } } },
            TargetAccomplished = false,
            ObjectiveAvailable = false,
            TargetSectorOwnStrength = 10000f,
            RequiredForce = 5000f,
            CurrentMonth = 6, CurrentYear = 1862
        };
        var output = PhaseTruthLedger.Evaluate(input);
        AssertEqual(PhaseTruthVerdict.ObjectiveUnavailable, output.Verdict);
        AssertEqual(PhaseTruthAction.Replan, output.RecommendedAction);
    }

    private static void PhaseTruthRecoversWhenForceBelowThreshold()
    {
        var input = new PhaseTruthInput
        {
            Plan = new OperationalPlan { Phases = { new Phase { TargetObjectiveId = 29, DeadlineMonth = 12, DeadlineYear = 1862 } } },
            TargetAccomplished = false,
            ObjectiveAvailable = true,
            TargetSectorOwnStrength = 1000f,
            RequiredForce = 5000f,
            CurrentMonth = 6, CurrentYear = 1862
        };
        var output = PhaseTruthLedger.Evaluate(input);
        AssertEqual(PhaseTruthVerdict.ForceBelowThreshold, output.Verdict);
        AssertEqual(PhaseTruthAction.Recover, output.RecommendedAction);
    }

    private static void PhaseTruthDeadlineExpiredAdvancesOrReplans()
    {
        var input = new PhaseTruthInput
        {
            Plan = new OperationalPlan { Phases = { new Phase { TargetObjectiveId = 29, DeadlineMonth = 1, DeadlineYear = 1862 } } },
            TargetAccomplished = false,
            ObjectiveAvailable = true,
            TargetSectorOwnStrength = 10000f,
            RequiredForce = 5000f,
            CurrentMonth = 6, CurrentYear = 1862
        };
        var output = PhaseTruthLedger.Evaluate(input);
        AssertEqual(PhaseTruthVerdict.DeadlineExpired, output.Verdict);
        AssertTrue(output.RecommendedAction == PhaseTruthAction.Advance ||
                   output.RecommendedAction == PhaseTruthAction.Replan,
                   "deadline expired should advance or replan");
    }

    private static void PhaseTruthNoContactStaysContinue()
    {
        var input = new PhaseTruthInput
        {
            Plan = new OperationalPlan { Phases = { new Phase { TargetObjectiveId = 29, DeadlineMonth = 12, DeadlineYear = 1862 } } },
            TargetAccomplished = false,
            ObjectiveAvailable = true,
            TargetSectorOwnStrength = 10000f,
            RequiredForce = 5000f,
            TargetEngagedRecently = false,
            CurrentMonth = 6, CurrentYear = 1862
        };
        var output = PhaseTruthLedger.Evaluate(input);
        AssertEqual(PhaseTruthVerdict.Valid, output.Verdict);
        AssertEqual(PhaseTruthAction.Continue, output.RecommendedAction);
    }

    private static void ContactEvidenceNoContactWhenZeroEnemyAndNoBattles()
    {
        var input = new ContactEvidenceInput
        {
            TargetPosition = new UnityEngine.Vector3(100f, 0f, 100f),
            CurrentEnemyStrength = 0f,
            CurrentFriendlyStrength = 8000f,
            PreviousObservedEnemyStrength = 0f,
            EnemyReactionMultiplier = 1.45f,
            EscalateFriendlyRatio = 1.8f,
            WithdrawFriendlyRatio = 0.55f,
            BattleHistory = new List<BattleHistoryRecord>(),
            SpatialMaxDistance = 50f,
            CurrentDaySerial = 1862 * 372 + 6 * 31 + 6
        };
        var output = ContactEvidenceLedger.Build(input);
        AssertEqual(ContactEvidence.NoContact, output.Evidence);
        AssertTrue(!output.AllowsEscalation, "no-contact must not allow escalation");
    }

    private static void ContactEvidenceEnemyReactedOnStrengthRise()
    {
        var input = new ContactEvidenceInput
        {
            TargetPosition = new UnityEngine.Vector3(100f, 0f, 100f),
            CurrentEnemyStrength = 6000f,
            CurrentFriendlyStrength = 7000f,
            PreviousObservedEnemyStrength = 3000f,
            EnemyReactionMultiplier = 1.45f,
            EscalateFriendlyRatio = 1.8f,
            WithdrawFriendlyRatio = 0.55f,
            BattleHistory = new List<BattleHistoryRecord>(),
            SpatialMaxDistance = 50f,
            CurrentDaySerial = 1862 * 372 + 6 * 31 + 6
        };
        var output = ContactEvidenceLedger.Build(input);
        AssertEqual(ContactEvidence.EnemyReacted, output.Evidence);
    }

    private static void ContactEvidenceSkirmishObservedNearTarget()
    {
        var input = new ContactEvidenceInput
        {
            TargetPosition = new UnityEngine.Vector3(100f, 0f, 100f),
            CurrentEnemyStrength = 1000f,
            CurrentFriendlyStrength = 1500f,
            PreviousObservedEnemyStrength = 1000f,
            EnemyReactionMultiplier = 1.45f,
            EscalateFriendlyRatio = 1.8f,
            WithdrawFriendlyRatio = 0.55f,
            BattleHistory = new List<BattleHistoryRecord>
            {
                new BattleHistoryRecord {
                    BattleName = "skirmish", PositionX = 105f, PositionZ = 105f,
                    Day = 4, Month = 6, Year = 1862, BattleResultType = 0 // not major
                }
            },
            SpatialMaxDistance = 50f,
            CurrentDaySerial = 1862 * 372 + 6 * 31 + 6
        };
        var output = ContactEvidenceLedger.Build(input);
        AssertEqual(ContactEvidence.SkirmishObserved, output.Evidence);
    }

    private static void ContactEvidenceBattleObservedLostIsOvermatched()
    {
        int daySerial = 1862 * 372 + 6 * 31 + 6;
        var input = new ContactEvidenceInput
        {
            TargetPosition = new UnityEngine.Vector3(100f, 0f, 100f),
            ObservingAllianceId = 0,
            CurrentEnemyStrength = 5000f,
            CurrentFriendlyStrength = 6000f,
            PreviousObservedEnemyStrength = 5000f,
            EnemyReactionMultiplier = 1.45f,
            EscalateFriendlyRatio = 1.8f,
            WithdrawFriendlyRatio = 0.55f,
            BattleHistory = new List<BattleHistoryRecord>
            {
                new BattleHistoryRecord {
                    BattleName = "majorlost", PositionX = 105f, PositionZ = 105f,
                    Day = 4, Month = 6, Year = 1862, BattleResultType = 1 /* major */,
                    AllianceWon = 1 // observer is alliance 0, so this is a loss
                }
            },
            SpatialMaxDistance = 50f,
            CurrentDaySerial = daySerial
        };
        var output = ContactEvidenceLedger.Build(input);
        AssertEqual(ContactEvidence.OvermatchedContact, output.Evidence);
        AssertTrue(!output.AllowsEscalation, "overmatched must not allow escalation");
    }

    private static void ContactEvidenceFavorableRequiresPresenceAndRatio()
    {
        int daySerial = 1862 * 372 + 6 * 31 + 6;
        var input = new ContactEvidenceInput
        {
            TargetPosition = new UnityEngine.Vector3(100f, 0f, 100f),
            ObservingAllianceId = 0,
            CurrentEnemyStrength = 1000f,
            CurrentFriendlyStrength = 2500f,
            PreviousObservedEnemyStrength = 1000f,
            EnemyReactionMultiplier = 1.45f,
            EscalateFriendlyRatio = 1.8f,
            WithdrawFriendlyRatio = 0.55f,
            BattleHistory = new List<BattleHistoryRecord>(),
            SpatialMaxDistance = 50f,
            CurrentDaySerial = daySerial
        };
        var output = ContactEvidenceLedger.Build(input);
        AssertEqual(ContactEvidence.FavorableContact, output.Evidence);
        AssertTrue(output.AllowsEscalation, "favorable contact allows escalation");
    }

    private static CampaignPaceInput BuildPaceInput(
        int allianceId, int year, int month, int chapter,
        float ownNationalMorale, float enemyNationalMorale,
        int battlesIn14Days, int majorBattlesIn14Days,
        int capitalStreak, int daysSinceFrontChange,
        bool winter)
    {
        return new CampaignPaceInput
        {
            AllianceId = allianceId,
            Year = year, Month = month,
            PolicyChapter = chapter,
            OwnNationalMorale = ownNationalMorale,
            EnemyNationalMorale = enemyNationalMorale,
            BreakMoraleTrigger = 30f, // arbitrary stable test value
            MinNationalMoraleSurrender = 18f,
            BattlesIn14Days = battlesIn14Days,
            MajorBattlesIn14Days = majorBattlesIn14Days,
            CapitalDangerStreakDays = capitalStreak,
            DaysSinceFrontSignatureChange = daysSinceFrontChange,
            IsWinter = winter
        };
    }

    private static void CampaignPaceTooFastCollapseOnEarlyMoraleCrash()
    {
        var input = BuildPaceInput(allianceId: 1, year: 1862, month: 6, chapter: 2,
            ownNationalMorale: 30f * 1.10f, enemyNationalMorale: 90f,
            battlesIn14Days: 0, majorBattlesIn14Days: 0,
            capitalStreak: 0, daysSinceFrontChange: 5, winter: false);
        var output = CampaignPaceLedger.Build(input);
        AssertEqual(CampaignPace.TooFastCollapse, output.Pace);
        AssertEqual(CollapseRisk.Critical, output.Risk);
    }

    private static void CampaignPaceLateWarPressureOnChapterThree()
    {
        var input = BuildPaceInput(allianceId: 0, year: 1864, month: 6, chapter: 3,
            ownNationalMorale: 80f, enemyNationalMorale: 60f,
            battlesIn14Days: 0, majorBattlesIn14Days: 0,
            capitalStreak: 0, daysSinceFrontChange: 60, winter: false);
        var output = CampaignPaceLedger.Build(input);
        AssertEqual(CampaignPace.LateWarPressure, output.Pace);
    }

    private static void CampaignPaceOverheatedOnHeavy14DayBattles()
    {
        var input = BuildPaceInput(allianceId: 0, year: 1862, month: 8, chapter: 2,
            ownNationalMorale: 80f, enemyNationalMorale: 80f,
            battlesIn14Days: 6, majorBattlesIn14Days: 4,
            capitalStreak: 0, daysSinceFrontChange: 5, winter: false);
        var output = CampaignPaceLedger.Build(input);
        AssertEqual(CampaignPace.Overheated, output.Pace);
    }

    private static void CampaignPaceTooQuietSuppressedInChapterOneWinter()
    {
        var input = BuildPaceInput(allianceId: 0, year: 1861, month: 12, chapter: 1,
            ownNationalMorale: 95f, enemyNationalMorale: 90f,
            battlesIn14Days: 0, majorBattlesIn14Days: 0,
            capitalStreak: 0, daysSinceFrontChange: 30, winter: true);
        var output = CampaignPaceLedger.Build(input);
        AssertTrue(output.Pace != CampaignPace.TooQuiet,
            "chapter 1 winter is the historically correct quiet state and must not be flagged");
    }

    private static void CampaignPaceStalematedWhenChapterTwoFrontStatic()
    {
        var input = BuildPaceInput(allianceId: 0, year: 1862, month: 6, chapter: 2,
            ownNationalMorale: 80f, enemyNationalMorale: 80f,
            battlesIn14Days: 1, majorBattlesIn14Days: 0,
            capitalStreak: 0, daysSinceFrontChange: 75, winter: false);
        var output = CampaignPaceLedger.Build(input);
        AssertEqual(CampaignPace.Stalemated, output.Pace);
    }

    private static void CampaignPaceStableDefault()
    {
        var input = BuildPaceInput(allianceId: 0, year: 1862, month: 6, chapter: 2,
            ownNationalMorale: 80f, enemyNationalMorale: 80f,
            battlesIn14Days: 2, majorBattlesIn14Days: 1,
            capitalStreak: 0, daysSinceFrontChange: 10, winter: false);
        var output = CampaignPaceLedger.Build(input);
        AssertEqual(CampaignPace.Stable, output.Pace);
    }

    private static void CollapseRiskThresholdsBoundToBreakMoraleTrigger()
    {
        AssertEqual(CollapseRisk.Critical, CampaignPaceLedger.RiskFor(ownMorale: 30f * 1.10f, breakMoraleTrigger: 30f, minSurrender: 18f));
        AssertEqual(CollapseRisk.Elevated, CampaignPaceLedger.RiskFor(ownMorale: 30f * 1.40f, breakMoraleTrigger: 30f, minSurrender: 18f));
        AssertEqual(CollapseRisk.Low,      CampaignPaceLedger.RiskFor(ownMorale: 30f * 2.50f, breakMoraleTrigger: 30f, minSurrender: 18f));
    }

    private static void DirectorCannotPublishPreserveForLateCsaUnderElevatedRisk()
    {
        var input = BuildPaceInput(allianceId: 1, year: 1864, month: 6, chapter: 3,
            ownNationalMorale: 30f * 1.40f, enemyNationalMorale: 80f,
            battlesIn14Days: 1, majorBattlesIn14Days: 0,
            capitalStreak: 0, daysSinceFrontChange: 5, winter: false);
        var output = CampaignPaceLedger.Build(input);
        AssertTrue(output.Risk >= CollapseRisk.Elevated, "elevated risk expected");
        AssertTrue(output.IntentBlockedFromPreserve,
            "1864 CSA under elevated risk must not publish StrategicIntent.Preserve");
    }

    private static void CampaignPacePublishesTheaterPriorityFromHighestPressureTheater()
    {
        var view = new TheaterPressureView();
        view.OwnStrengthByTheater[Theater.East] = 10000f;
        view.EnemyStrengthByTheater[Theater.East] = 4000f;
        view.OwnStrengthByTheater[Theater.West] = 4000f;
        view.EnemyStrengthByTheater[Theater.West] = 8000f; // West is the hot theater for us
        var input = BuildPaceInput(allianceId: 0, year: 1862, month: 6, chapter: 2,
            ownNationalMorale: 80f, enemyNationalMorale: 80f,
            battlesIn14Days: 2, majorBattlesIn14Days: 0,
            capitalStreak: 0, daysSinceFrontChange: 10, winter: false);
        input.TheaterPressure = view;
        var output = CampaignPaceLedger.Build(input);
        AssertEqual(Theater.West, output.TheaterPriority);
    }

    private static void PersistenceDtoLoadToleratesLegacyTheaterCommanders()
    {
        // Old sidecar JSON included a theaterCommanders array on each faction.
        // After deleting TheaterCommander + FactionDto.TheaterCommanders, Newtonsoft
        // must silently ignore that field rather than throwing or losing the known fields.
        string legacyJson = @"{
            ""version"":1,
            ""factions"":[
                {
                    ""factionId"":0,
                    ""factionName"":""Union"",
                    ""currentEra"":""Early"",
                    ""cic"":{""officerName"":""Lincoln""},
                    ""theaterCommanders"":[{""theaterId"":1,""officerName"":""Grant""}]
                }
            ]
        }";
        var dto = Newtonsoft.Json.JsonConvert.DeserializeObject<SidecarDto>(legacyJson);
        AssertTrue(dto != null, "dto should deserialize");
        AssertTrue(dto.Factions != null && dto.Factions.Count == 1, "one faction loaded");
        AssertEqual("Lincoln", dto.Factions[0].Cic.OfficerName);
    }

    private static void DirectorClampsThresholdModifierToHalfPersonalityDelta()
    {
        var personality = new PersonalityVector { Audacity = 0.5f, Caution = 0.0f };
        // Personality contributes: MaximumProbeStrengthFraction += 0.05*audacity - 0.04*caution = +0.025
        float personalityDeltaOnFraction = 0.05f * personality.Audacity - 0.04f * personality.Caution;
        var posture = StrategicResilienceDirector.ProposePosture(
            allianceId: 0,
            pace: new CampaignPaceOutput { Pace = CampaignPace.Overheated, Risk = CollapseRisk.Low, IntentBlockedFromPreserve = false },
            personality: personality);
        AssertTrue(System.Math.Abs(posture.MaximumProbeStrengthFractionModifier) <= 0.5f * System.Math.Abs(personalityDeltaOnFraction) + 1e-6f,
            "director modifier must be ≤50% of personality delta — was " + posture.MaximumProbeStrengthFractionModifier);
    }

    private static void DirectorMapsOverheatedToRecoverLeaning()
    {
        var posture = StrategicResilienceDirector.ProposePosture(
            allianceId: 0,
            pace: new CampaignPaceOutput { Pace = CampaignPace.Overheated, Risk = CollapseRisk.Low, IntentBlockedFromPreserve = false },
            personality: new PersonalityVector());
        AssertTrue(posture.Intent == StrategicIntent.Recover || posture.Intent == StrategicIntent.Delay,
            "overheated pace should propose recover/delay intent");
    }

    private static void DirectorBlocksPreserveForLateCsaUnderElevatedRisk()
    {
        var posture = StrategicResilienceDirector.ProposePosture(
            allianceId: 1,
            pace: new CampaignPaceOutput { Pace = CampaignPace.LateWarPressure, Risk = CollapseRisk.Elevated, IntentBlockedFromPreserve = true },
            personality: new PersonalityVector { Caution = 0.6f });
        AssertTrue(posture.Intent != StrategicIntent.Preserve,
            "1864 CSA under elevated risk cannot publish Preserve");
    }

    // Task 14: MemoryToDto/MemoryFromDto preserve all key fields including LastPosture enum values
    // and RecentEventSummaries list. Null DTO input must yield a fresh DirectorMemory.
    private static void DirectorMemoryRoundTripsThroughDto()
    {
        var memory = new DirectorMemory
        {
            LastFullRefreshDay = 12345,
            CapitalDangerStreakDays = 3,
            DaysSinceLastBattle = 7,
            LastSourceSignature = "sig-abc",
            LastPosture = new DirectorPosture
            {
                AllianceId = 0,
                Pace = CampaignPace.LateWarPressure,
                Intent = StrategicIntent.Concentrate,
                Risk = CollapseRisk.Low,
                TheaterPriority = Theater.East
            }
        };
        memory.RecentEventSummaries.Add("battle:east:1864-06-15");

        var dto = StrategicResilienceDirector.MemoryToDto(memory);
        var rebuilt = StrategicResilienceDirector.MemoryFromDto(dto);

        AssertEqual(memory.LastFullRefreshDay, rebuilt.LastFullRefreshDay);
        AssertEqual(memory.CapitalDangerStreakDays, rebuilt.CapitalDangerStreakDays);
        AssertEqual(memory.LastSourceSignature, rebuilt.LastSourceSignature);
        AssertEqual(memory.LastPosture.Pace, rebuilt.LastPosture.Pace);
        AssertEqual(memory.LastPosture.Intent, rebuilt.LastPosture.Intent);
        AssertEqual(memory.LastPosture.Risk, rebuilt.LastPosture.Risk);
        AssertEqual(memory.LastPosture.TheaterPriority, rebuilt.LastPosture.TheaterPriority);
        AssertEqual(1, rebuilt.RecentEventSummaries.Count);
    }

    // Task 13: CicReviewRouter.RouteAction routes Advance → AdvancePhase, which exhausts the
    // last phase and marks the plan dirty. Single-phase plan: after accomplished truth,
    // IsDirty=true, return false (signals replan to caller).
    // CIC.ReviewPlanWithTruth delegates to CicReviewRouter, so this exercises the real routing
    // logic without requiring BepInEx/HarmonyLib in the test harness.
    private static void CicReviewPlanReplansWhenPhaseTruthSaysAccomplished()
    {
        var plan = new OperationalPlan
        {
            CurrentPhaseIndex = 0,
            PlanDeadlineMonth = 12,
            PlanDeadlineYear = 1862,
            IsDirty = false
        };
        plan.Phases.Add(new Phase
        {
            TargetObjectiveId = 42,
            ForceFractionRequired = 0.5f,
            Transition = PhaseTransition.TargetTaken,
            DeadlineMonth = 12,
            DeadlineYear = 1862
        });

        // Accomplished → PhaseTruthAction.Advance → AdvancePhase (no next phase) → IsDirty=true, false
        var truth = new PhaseTruthOutput
        {
            Verdict = PhaseTruthVerdict.TargetAccomplished,
            RecommendedAction = PhaseTruthAction.Advance,
            Reason = "target-accomplished"
        };

        bool result = CicReviewRouter.RouteAction(plan, truth, 6, 1862);
        AssertTrue(!result, "RouteAction should return false when last phase is exhausted by Advance");
        AssertTrue(plan.IsDirty, "plan.IsDirty should be true after last phase exhausted by Advance");
    }

    private static void DirectorPublishClampSuppressesSecondPublishInSameRealSecond()
    {
        var clamp = new DirectorPublishClamp();
        var stamp = new System.DateTime(2026, 5, 5, 12, 0, 0);
        AssertTrue(clamp.TryPublish(stamp), "first publish in second should succeed");
        AssertTrue(!clamp.TryPublish(stamp.AddMilliseconds(50)), "second publish 50ms later should be suppressed");
        AssertTrue(clamp.TryPublish(stamp.AddSeconds(1).AddMilliseconds(1)), "publish past 1s boundary should succeed");
    }

    private static void DirectorRaisesCsaHoldRatioUnderTooFastCollapse()
    {
        var posture = StrategicResilienceDirector.ProposePosture(
            allianceId: 1,
            pace: new CampaignPaceOutput { Pace = CampaignPace.TooFastCollapse, Risk = CollapseRisk.Critical, IntentBlockedFromPreserve = false },
            personality: new PersonalityVector());
        AssertTrue(posture.MinimumHoldRatioModifier > 0f,
            "TooFastCollapse for CSA must raise MinimumHoldRatio — was " + posture.MinimumHoldRatioModifier);
        AssertTrue(posture.MinimumHoldRatioModifier <= 0.10f,
            "MinimumHoldRatioModifier capped at +0.10");
    }

    private static void DirectorRaisesRecoverFloorUnderOverheated()
    {
        var posture = StrategicResilienceDirector.ProposePosture(
            allianceId: 0,
            pace: new CampaignPaceOutput { Pace = CampaignPace.Overheated, Risk = CollapseRisk.Low },
            personality: new PersonalityVector());
        AssertTrue(posture.RecoverFloorModifier > 0f, "overheated must raise recover floor");
    }

    private static void DirectorRelaxesUnionMassRatioUnderLateWarPressure()
    {
        var posture = StrategicResilienceDirector.ProposePosture(
            allianceId: 0,
            pace: new CampaignPaceOutput { Pace = CampaignPace.LateWarPressure, Risk = CollapseRisk.Low },
            personality: new PersonalityVector());
        AssertTrue(posture.MassRatioModifier < 0f, "Union late-war pressure must lower mass ratio gate");
    }

    private static void DirectorCriticalRiskFavorsSupplyConstruction()
    {
        var posture = StrategicResilienceDirector.ProposePosture(
            allianceId: 1,
            pace: new CampaignPaceOutput { Pace = CampaignPace.TooFastCollapse, Risk = CollapseRisk.Critical },
            personality: new PersonalityVector());
        AssertTrue(posture.SupplyConstructionBias >= 0.30f,
            "Critical risk must strongly favor supply — was " + posture.SupplyConstructionBias);
    }

    private static void DirectorTooQuietFavorsLogistics()
    {
        var posture = StrategicResilienceDirector.ProposePosture(
            allianceId: 0,
            pace: new CampaignPaceOutput { Pace = CampaignPace.TooQuiet, Risk = CollapseRisk.Low },
            personality: new PersonalityVector());
        AssertTrue(posture.LogisticsBias >= 0.20f, "TooQuiet must favor logistics");
    }

    private static void DirectorTooFastCollapseDampsExpansion()
    {
        var posture = StrategicResilienceDirector.ProposePosture(
            allianceId: 1,
            pace: new CampaignPaceOutput { Pace = CampaignPace.TooFastCollapse, Risk = CollapseRisk.Critical },
            personality: new PersonalityVector());
        AssertTrue(posture.ExpansionDamper >= 0.30f, "TooFastCollapse must damp expansion");
    }

    private static void DirectorRaisesCapitalDefenseBudgetUnderTooFastCollapse()
    {
        var posture = StrategicResilienceDirector.ProposePosture(
            allianceId: 1,
            pace: new CampaignPaceOutput { Pace = CampaignPace.TooFastCollapse, Risk = CollapseRisk.Critical },
            personality: new PersonalityVector());
        AssertTrue(posture.CapitalDefenseBudgetModifier >= 0.05f,
            "TooFastCollapse must raise capital defense budget");
    }

    private static void DirectorLowersUnionGuardUnderLateWarPressure()
    {
        var posture = StrategicResilienceDirector.ProposePosture(
            allianceId: 0,
            pace: new CampaignPaceOutput { Pace = CampaignPace.LateWarPressure, Risk = CollapseRisk.Low },
            personality: new PersonalityVector());
        AssertTrue(posture.GuardBudgetFractionModifier <= 0f,
            "Union late-war pressure can slightly lower guard for source-sector concentration");
    }
}
