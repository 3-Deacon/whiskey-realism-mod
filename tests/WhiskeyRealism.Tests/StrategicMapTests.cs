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

internal static class StrategicMapTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
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
            ("asset role catalog overrides scorer for named anchor", AssetRoleCatalogOverridesScorer),
            ("asset role catalog returns none for unknown name", AssetRoleCatalogReturnsNoneForUnknown),
            ("asset role catalog distinguishes known neutral anchors from unknown names", AssetRoleCatalogDistinguishesKnownNeutralAnchors),
            ("asset role catalog resolves real gtcw names", AssetRoleCatalogResolvesRealGtcwNames),
            ("csa early profile favors capital defense and foreign recognition", CsaEarlyProfileFavorsDefenseAndForeignRecognition),
            ("grand strategy tags affect objective score", GrandStrategyTagsAffectObjectiveScore),
            ("theater classifier maps wl capitals to east", TheaterClassifierMapsWlCapitalsToEast),
            ("army area classifier maps W&L northwest Virginia towns", ArmyAreaClassifierMapsWlNorthwestVirginiaTowns),
            ("campaign map ledger only maps states represented by towns", CampaignMapLedgerOnlyMapsRepresentedStates),
            ("campaign map ledger ranks owned capitals for defense", CampaignMapLedgerRanksOwnedCapitalsForDefense),
            ("campaign map ledger tracks ports and forts", CampaignMapLedgerTracksPortsAndForts),
            ("objective catalog maps known wl objectives", ObjectiveCatalogMapsKnownWlObjectives),
            ("objective catalog exposes known objective positions", ObjectiveCatalogExposesKnownObjectivePositions),
            ("objective adapter falls back to catalog positions when vanilla objectives are unavailable", ObjectiveAdapterFallsBackToCatalogWhenVanillaUnavailable),
            ("objective catalog keeps unknown ids unresolved", ObjectiveCatalogKeepsUnknownIdsUnresolved),
            ("recompute pressure resets counters before counting", RecomputePressureResetsCountersBeforeCounting),
            ("perk scorer favors siege armies for fort pressure", PerkScorerFavorsSiegeArmiesForFortPressure),
            ("perk scorer favors raid armies for irregular pressure", PerkScorerFavorsRaidArmiesForIrregularPressure),
            ("perk scorer favors union blockade fleets", PerkScorerFavorsUnionBlockadeFleets),
            ("perk scorer favors csa raiding fleets", PerkScorerFavorsCsaRaidingFleets),
            ("perk scorer skips unavailable candidates", PerkScorerSkipsUnavailableCandidates),
            ("front sector signature ignores sub-bucket ratio jitter", FrontSectorSignatureIgnoresSubBucketRatioJitter),
            ("asset strategic role flags compose additively", AssetStrategicRoleFlagsComposeAdditively),
            ("campaign map ledger applies role catalog to towns and assets", CampaignMapLedgerAppliesRoleCatalog),
            ("campaign map ledger signature reflects role changes", CampaignMapLedgerSignatureReflectsRoleChanges),
            ("contact evidence no contact when zero enemy and no battles", ContactEvidenceNoContactWhenZeroEnemyAndNoBattles),
            ("contact evidence enemy reacted on strength rise", ContactEvidenceEnemyReactedOnStrengthRise),
            ("contact evidence skirmish observed near target", ContactEvidenceSkirmishObservedNearTarget),
            ("contact evidence battle observed lost is overmatched", ContactEvidenceBattleObservedLostIsOvermatched),
            ("contact evidence favorable contact requires presence and ratio", ContactEvidenceFavorableRequiresPresenceAndRatio),
            ("persistence dto load tolerates legacy theater commanders field", PersistenceDtoLoadToleratesLegacyTheaterCommanders),
            ("historical registry covers Hunter and Beauregard", HistoricalRegistryCoversHunterAndBeauregard),
            ("historical registry normalizes initials and punctuation", HistoricalRegistryNormalizesInitialsAndPunctuation),
            ("historical registry covers major Civil War commanders", HistoricalRegistryCoversMajorCivilWarCommanders),
        });
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

    private static void ObjectiveCatalogExposesKnownObjectivePositions()
    {
        AssertTrue(ObjectiveCatalog.TryResolvePosition(3, out float richmondX, out float richmondZ),
            "objective 3 should expose a catalog position");
        AssertNear(760f, richmondX, 0.01f, "objective 3 catalog x");
        AssertNear(60f, richmondZ, 0.01f, "objective 3 catalog z");

        AssertTrue(ObjectiveCatalog.TryResolvePosition(4, out float washingtonX, out float washingtonZ),
            "objective 4 should expose a catalog position");
        AssertNear(720f, washingtonX, 0.01f, "objective 4 catalog x");
        AssertNear(160f, washingtonZ, 0.01f, "objective 4 catalog z");
    }

    private static void ObjectiveAdapterFallsBackToCatalogWhenVanillaUnavailable()
    {
        var position = ObjectiveAdapter.ResolveObjectivePosition(3);

        AssertTrue(position.HasValue, "objective adapter should use catalog position when CampaignObjective runtime state is unavailable");
        AssertNear(760f, position.Value.x, 0.01f, "objective adapter catalog fallback x");
        AssertNear(60f, position.Value.z, 0.01f, "objective adapter catalog fallback z");
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

        AssertTrue(ledger.TryGetStateTheater(38, out var virginia) && virginia == Theater.East, "Virginia east");
        AssertTrue(ledger.TryGetStateTheater(29, out var ohio) && ohio == Theater.West, "Ohio west");
        AssertFalse(ledger.TryGetStateTheater(21, out _), "unrepresented state");
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
        var norfolk = TownNamed(ledger, "Norfolk");
        AssertTrue(norfolk != null, "expected Norfolk in map ledger");
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

    private static void ObjectiveCatalogKeepsUnknownIdsUnresolved()
    {
        AssertEqual(false, ObjectiveCatalog.TryResolve(9999, out _));
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

    private static void AssetRoleCatalogDistinguishesKnownNeutralAnchors()
    {
        AssertTrue(AssetRoleCatalog.TryLookup("Port Tobacco", out var knownNeutral),
            "Port Tobacco should be a known neutral low-priority anchor");
        AssertEqual(AssetStrategicRole.None, knownNeutral);

        AssertEqual(false, AssetRoleCatalog.TryLookup("unmapped-port", out _),
            "unmapped names should remain distinguishable from explicit neutral anchors");
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

        var norfolk = TownNamed(ledger, "Norfolk");
        AssertTrue(norfolk != null, "should resolve Norfolk by name");
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

    private static void ContactEvidenceNoContactWhenZeroEnemyAndNoBattles()
    {
        var input = new ContactEvidenceInput
        {
            TargetX = 100f,
            TargetZ = 100f,
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
            TargetX = 100f,
            TargetZ = 100f,
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
            TargetX = 100f,
            TargetZ = 100f,
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
            TargetX = 100f,
            TargetZ = 100f,
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
            TargetX = 100f,
            TargetZ = 100f,
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

    private static void HistoricalRegistryCoversHunterAndBeauregard()
    {
        // Telemetry showed TacticalCommanderUnknown for Hunter and Beauregard.
        // Hunter was unregistered. Beauregard was registered but failed name match
        // because the GTCW combinedname was 'P.G.T. Beauregard'. Both must now resolve
        // by their full vanilla names (T12: surnames alone never match).
        AssertTrue(LookupEntry("Union", "David Hunter").HasValue, "Hunter (full name) must match");
        AssertTrue(LookupEntry("CSA", "P.G.T. Beauregard").HasValue, "Beauregard (full name with initials) must match");
        AssertTrue(!LookupEntry("CSA", "Beauregard").HasValue, "bare surname never matches");
    }

    private static void HistoricalRegistryNormalizesInitialsAndPunctuation()
    {
        AssertEqual("pgt beauregard", HistoricalFigureRegistry.NormalizeFullNameForTest("P.G.T. Beauregard"), "PGT initials compacted");
        AssertEqual("ambrose p hill", HistoricalFigureRegistry.NormalizeFullNameForTest("Ambrose P. Hill"), "middle initial kept");
        AssertEqual("edward oc ord", HistoricalFigureRegistry.NormalizeFullNameForTest("Edward O.C.  Ord"), "whitespace collapsed");
        AssertEqual("nathan b forrest", HistoricalFigureRegistry.NormalizeFullNameForTest(" Nathan B. Forrest "), "trimmed");
    }

    private static void HistoricalRegistryCoversMajorCivilWarCommanders()
    {
        string[] mustCover = new[]
        {
            "Robert E. Lee", "Thomas J. Jackson", "James Longstreet", "J.E.B. Stuart", "John B. Hood",
            "Braxton Bragg", "Joseph E. Johnston", "P.G.T. Beauregard", "Nathan B. Forrest", "Leonidas Polk",
            "Richard S. Ewell", "Ambrose P. Hill", "Jubal A. Early", "William J. Hardee", "Patrick R. Cleburne",
            "George E. Pickett", "Joseph Wheeler", "John H. Morgan", "Wade Hampton",
            "Ulysses S. Grant", "William T. Sherman", "Philip H. Sheridan", "George G. Meade", "George H. Thomas",
            "Joseph Hooker", "Ambrose E. Burnside", "George B. McClellan", "John Pope", "Henry W. Halleck",
            "David Hunter", "Franz Sigel", "Nathaniel P. Banks", "Don Carlos Buell", "William Rosecrans",
            "John M. Schofield", "James B. McPherson", "John F. Reynolds", "Winfield S. Hancock", "John Sedgwick",
            "John Buford", "George A. Custer",
        };
        var missing = new System.Collections.Generic.List<string>();
        foreach (var full in mustCover)
        {
            var union = LookupEntry("Union", full);
            var csa = LookupEntry("CSA", full);
            if (!union.HasValue && !csa.HasValue) missing.Add(full);
        }
        AssertTrue(missing.Count == 0, "registry missing: " + string.Join(",", missing));
    }

    private static PersonalityVector? LookupEntry(string allianceTag, string fullName)
    {
        string normalized = HistoricalFigureRegistry.NormalizeFullNameForTest(fullName);
        foreach (var entry in HistoricalFigureRegistry.EntriesForTest)
        {
            if (entry.AllianceTag != allianceTag || entry.FullNames == null) continue;
            foreach (var n in entry.FullNames)
            {
                if (HistoricalFigureRegistry.NormalizeFullNameForTest(n) == normalized)
                    return entry.V;
            }
        }
        return null;
    }

    private static CampaignMapTown TownNamed(CampaignMapLedger ledger, string cityName)
    {
        foreach (var town in ledger.Towns)
            if (town.CityName == cityName) return town;
        return null;
    }
}
