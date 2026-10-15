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

internal static class ProjectDoctrineTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("project doctrine catalog maps all active vanilla project rows", ProjectDoctrineCatalogMapsAllActiveRows),
            ("project doctrine catalog entries are immutable through public api", ProjectDoctrineCatalogEntriesAreImmutable),
            ("project doctrine catalog marks market reform fully broken", ProjectDoctrineCatalogMarksMarketReformBroken),
            ("project doctrine catalog maps representative buckets and lanes", ProjectDoctrineCatalogMapsRepresentativeBucketsAndLanes),
            ("project doctrine catalog maps organization reform aliases", ProjectDoctrineCatalogMapsOrganizationReformAliases),
            ("project doctrine catalog has no lane six or seven entries", ProjectDoctrineCatalogHasNoLaneSixOrSevenEntries),
            ("project doctrine scorer suppresses fully broken market reform", ProjectDoctrineScorerSuppressesMarketReform),
            ("project doctrine scorer keeps civil order casualty value without raiding value", ProjectDoctrineScorerPartialCivilOrder),
            ("project doctrine scorer excludes offensive tempo from civil order", ProjectDoctrineScorerExcludesCivilOrderOffensiveTempo),
            ("project doctrine scorer penalizes out of window projects", ProjectDoctrineScorerPenalizesOutOfWindow),
            ("project doctrine scorer protects half funded queue", ProjectDoctrineScorerProtectsHalfFundedQueue),
            ("project doctrine scorer lets suppression bypass hysteresis", ProjectDoctrineScorerSuppressionBypassesHysteresis),
            ("project doctrine scorer rejects stale candidate lane metadata", ProjectDoctrineScorerRejectsStaleCandidateLane),
            ("project doctrine scorer will not replace out of window with out of window", ProjectDoctrineScorerRejectsOutOfWindowReplacement),
            ("project doctrine scorer sanitizes public signal inputs", ProjectDoctrineScorerSanitizesPublicSignals),
            ("project doctrine scorer keeps lane intent numeric fields finite", ProjectDoctrineScorerKeepsLaneIntentFinite),
            ("project doctrine scorer does not mark high vanilla only lane critical", ProjectDoctrineScorerIgnoresVanillaOnlyCriticality),
            ("project doctrine scorer marks best doctrine replacement critical", ProjectDoctrineScorerMarksBestDoctrineReplacementCritical),
            ("project doctrine log gate suppresses repeated signatures", ProjectDoctrineLogGateSuppressesRepeatedSignatures),
            ("project doctrine log gate ignores empty signatures", ProjectDoctrineLogGateIgnoresEmptySignatures),
            ("project doctrine starved lane signature includes funding trajectory", ProjectDoctrineStarvedLaneSignatureIncludesFundingTrajectory),
        });
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

    private static void ProjectDoctrineScorerSuppressesMarketReform()
    {
        var signals = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignals
        {
            Alliance = 0,
            Era = EraStage.Amateur1861,
            FiscalPosture = FiscalPosture.BalancedWar
        };

        var candidates = new[]
        {
            new ProjectCandidateInput { ProjectId = 96, SubsidyType = 1, VanillaWeight = 0.2f }
        };

        var decision = WhiskeyRealism.Strategic.Projects.ProjectDoctrineScorer.Select(
            GrandStrategyRegistry.Resolve(0, EraStage.Amateur1861),
            signals,
            subsidyType: 1,
            vanillaProjectId: 98,
            vanillaWeight: 1f,
            candidates: candidates,
            fiscalWeight: null,
            runtimeFacts: id => new WhiskeyRealism.Strategic.Projects.ProjectRuntimeFacts { ProjectId = id, SubsidyLane = 1, Cost = 1000f },
            fundingAvailable: 0f,
            netFundingPerDay: 0f,
            constructionCurrentlyWins: false);

        AssertEqual(true, decision.ShouldReplace);
        AssertEqual(96, decision.ProjectId);
        AssertEqual("suppressed-vanilla", decision.Reason);
    }

    private static void ProjectDoctrineScorerPartialCivilOrder()
    {
        var signals = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignals
        {
            Alliance = 1,
            Era = EraStage.Decisive1863,
            FiscalPosture = FiscalPosture.BalancedWar,
            CivilOrderRisk = 1f
        };

        float score = WhiskeyRealism.Strategic.Projects.ProjectDoctrineScorer.ScoreDoctrineOnly(107, signals);

        AssertEqual(true, score > 0f);
        AssertEqual(true, score < 1.5f);
    }

    private static void ProjectDoctrineScorerExcludesCivilOrderOffensiveTempo()
    {
        var signals = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignals
        {
            Alliance = 1,
            Era = EraStage.Decisive1863,
            FiscalPosture = FiscalPosture.BalancedWar,
            CivilOrderRisk = 0f,
            ManpowerStress = 0f,
            OffensiveTempoNeed = 1f
        };

        float score = WhiskeyRealism.Strategic.Projects.ProjectDoctrineScorer.ScoreDoctrineOnly(107, signals);

        AssertEqual(0f, score);
    }

    private static void ProjectDoctrineScorerPenalizesOutOfWindow()
    {
        var signals = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignals
        {
            Alliance = 0,
            Era = EraStage.Amateur1861,
            FiscalPosture = FiscalPosture.BalancedWar,
            NavalDeficit = 1f
        };

        var candidates = new[]
        {
            new ProjectCandidateInput { ProjectId = 31, SubsidyType = 4, VanillaWeight = 0.2f }
        };

        var decision = WhiskeyRealism.Strategic.Projects.ProjectDoctrineScorer.Select(
            GrandStrategyRegistry.Resolve(0, EraStage.Amateur1861),
            signals,
            subsidyType: 4,
            vanillaProjectId: 35,
            vanillaWeight: 1f,
            candidates: candidates,
            fiscalWeight: null,
            runtimeFacts: id => new WhiskeyRealism.Strategic.Projects.ProjectRuntimeFacts
            {
                ProjectId = id,
                SubsidyLane = 4,
                Cost = 1000f,
                DateFromKnown = id == 35,
                DateFromYear = id == 35 ? 1864 : 0,
                DateFromMonth = 1,
                DateFromDay = 1
            },
            fundingAvailable: 0f,
            netFundingPerDay: 0f,
            constructionCurrentlyWins: false);

        AssertEqual(true, decision.ShouldReplace);
        AssertEqual(31, decision.ProjectId);
    }

    private static void ProjectDoctrineScorerProtectsHalfFundedQueue()
    {
        var signals = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignals
        {
            Alliance = 1,
            Era = EraStage.Operational1862,
            FiscalPosture = FiscalPosture.BalancedWar,
            WeaponDeficit = 1f
        };

        var candidates = new[]
        {
            new ProjectCandidateInput { ProjectId = 6, SubsidyType = 4, VanillaWeight = 0.8f }
        };

        var decision = WhiskeyRealism.Strategic.Projects.ProjectDoctrineScorer.Select(
            GrandStrategyRegistry.Resolve(1, EraStage.Operational1862),
            signals,
            subsidyType: 4,
            vanillaProjectId: 11,
            vanillaWeight: 1f,
            candidates: candidates,
            fiscalWeight: null,
            runtimeFacts: id => new WhiskeyRealism.Strategic.Projects.ProjectRuntimeFacts { ProjectId = id, SubsidyLane = 4, Cost = 1000f },
            fundingAvailable: 600f,
            netFundingPerDay: 20f,
            constructionCurrentlyWins: false);

        AssertEqual(false, decision.ShouldReplace);
        AssertEqual(11, decision.ProjectId);
        AssertEqual("queued-half-funded", decision.Reason);
        AssertEqual(1, decision.LaneIntent.Alliance);
        AssertEqual(4, decision.LaneIntent.SubsidyLane);
        AssertEqual(11, decision.LaneIntent.QueuedProjectId);
        AssertEqual(600f, decision.LaneIntent.FundingAvailable);
        AssertEqual(1000f, decision.LaneIntent.FundingNeeded);
        AssertEqual(20f, decision.LaneIntent.NetFundingPerDay);
        AssertEqual(20f, decision.LaneIntent.TimeToFundEstimateDays);
        AssertEqual(false, decision.LaneIntent.ConstructionCurrentlyWins);
        AssertEqual(true, decision.LaneIntent.CriticalDoctrineProject);
    }

    private static void ProjectDoctrineScorerSuppressionBypassesHysteresis()
    {
        var signals = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignals
        {
            Alliance = 0,
            Era = EraStage.Operational1862,
            FiscalPosture = FiscalPosture.BalancedWar
        };

        var candidates = new[]
        {
            new ProjectCandidateInput { ProjectId = 96, SubsidyType = 1, VanillaWeight = 0.1f }
        };

        var decision = WhiskeyRealism.Strategic.Projects.ProjectDoctrineScorer.Select(
            GrandStrategyRegistry.Resolve(0, EraStage.Operational1862),
            signals,
            subsidyType: 1,
            vanillaProjectId: 98,
            vanillaWeight: 1f,
            candidates: candidates,
            fiscalWeight: null,
            runtimeFacts: id => new WhiskeyRealism.Strategic.Projects.ProjectRuntimeFacts { ProjectId = id, SubsidyLane = 1, Cost = 1000f },
            fundingAvailable: 900f,
            netFundingPerDay: 50f,
            constructionCurrentlyWins: false);

        AssertEqual(true, decision.ShouldReplace);
        AssertEqual(96, decision.ProjectId);
    }

    private static void ProjectDoctrineScorerRejectsStaleCandidateLane()
    {
        var signals = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignals
        {
            Alliance = 1,
            Era = EraStage.Operational1862,
            FiscalPosture = FiscalPosture.BalancedWar,
            WeaponDeficit = 1f,
            RecognitionWindow = 1f
        };

        var candidates = new[]
        {
            new ProjectCandidateInput { ProjectId = 103, SubsidyType = 1, VanillaWeight = 10f },
            new ProjectCandidateInput { ProjectId = 6, SubsidyType = 1, VanillaWeight = 10f }
        };

        var decision = WhiskeyRealism.Strategic.Projects.ProjectDoctrineScorer.Select(
            GrandStrategyRegistry.Resolve(1, EraStage.Operational1862),
            signals,
            subsidyType: 1,
            vanillaProjectId: 96,
            vanillaWeight: 0.2f,
            candidates: candidates,
            fiscalWeight: null,
            runtimeFacts: id =>
            {
                var entry = WhiskeyRealism.Strategic.Projects.ProjectDoctrineCatalog.Get(id);
                return new WhiskeyRealism.Strategic.Projects.ProjectRuntimeFacts
                {
                    ProjectId = id,
                    SubsidyLane = entry != null ? entry.SubsidyLane : 1,
                    Cost = 1000f
                };
            },
            fundingAvailable: 0f,
            netFundingPerDay: 0f,
            constructionCurrentlyWins: false);

        AssertEqual(false, decision.ShouldReplace);
        AssertEqual(96, decision.ProjectId);
    }

    private static void ProjectDoctrineScorerRejectsOutOfWindowReplacement()
    {
        var signals = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignals
        {
            Alliance = 0,
            Era = EraStage.Amateur1861,
            FiscalPosture = FiscalPosture.BalancedWar,
            NavalDeficit = 1f
        };

        var candidates = new[]
        {
            new ProjectCandidateInput { ProjectId = 31, SubsidyType = 4, VanillaWeight = 10f }
        };

        var decision = WhiskeyRealism.Strategic.Projects.ProjectDoctrineScorer.Select(
            GrandStrategyRegistry.Resolve(0, EraStage.Amateur1861),
            signals,
            subsidyType: 4,
            vanillaProjectId: 35,
            vanillaWeight: 1f,
            candidates: candidates,
            fiscalWeight: null,
            runtimeFacts: id => new WhiskeyRealism.Strategic.Projects.ProjectRuntimeFacts
            {
                ProjectId = id,
                SubsidyLane = 4,
                Cost = 1000f,
                DateFromKnown = true,
                DateFromYear = 1864,
                DateFromMonth = 1,
                DateFromDay = 1
            },
            fundingAvailable: 0f,
            netFundingPerDay: 0f,
            constructionCurrentlyWins: false);

        AssertEqual(false, decision.ShouldReplace);
        AssertEqual(35, decision.ProjectId);
    }

    private static void ProjectDoctrineScorerSanitizesPublicSignals()
    {
        var signals = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignals
        {
            Alliance = 1,
            Era = EraStage.Operational1862,
            FiscalPosture = FiscalPosture.CreditDefense,
            WeaponDeficit = float.NaN,
            ArtilleryDeficit = float.PositiveInfinity,
            NavalDeficit = float.NegativeInfinity,
            BlockadePressure = float.NaN,
            PortViability = float.NaN,
            CreditStress = float.PositiveInfinity,
            ManpowerStress = float.NaN,
            LogisticsTempoNeed = float.PositiveInfinity,
            IndustryGap = float.NaN,
            AgricultureFoodStress = float.PositiveInfinity,
            CivilOrderRisk = float.NaN,
            RecognitionWindow = float.PositiveInfinity,
            OffensiveTempoNeed = float.NaN,
            LateWarCollapseRisk = float.NegativeInfinity
        };

        float score = WhiskeyRealism.Strategic.Projects.ProjectDoctrineScorer.ScoreDoctrineOnly(6, signals);
        AssertFinite(score, "doctrine-only score");

        var decision = WhiskeyRealism.Strategic.Projects.ProjectDoctrineScorer.Select(
            GrandStrategyRegistry.Resolve(1, EraStage.Operational1862),
            signals,
            subsidyType: 4,
            vanillaProjectId: 11,
            vanillaWeight: float.PositiveInfinity,
            candidates: new[]
            {
                new ProjectCandidateInput { ProjectId = 6, SubsidyType = 4, VanillaWeight = float.NaN }
            },
            fiscalWeight: id => float.PositiveInfinity,
            runtimeFacts: id => new WhiskeyRealism.Strategic.Projects.ProjectRuntimeFacts { ProjectId = id, SubsidyLane = 4, Cost = float.NaN },
            fundingAvailable: float.PositiveInfinity,
            netFundingPerDay: float.NaN,
            constructionCurrentlyWins: false);

        AssertFinite(decision.BestScore, "best score");
        AssertFinite(decision.VanillaScore, "vanilla score");
        AssertEqual(true, decision.ShouldReplace);
        AssertEqual(6, decision.ProjectId);
        AssertEqual(false, decision.LaneIntent.CriticalDoctrineProject);
    }

    private static void ProjectDoctrineScorerKeepsLaneIntentFinite()
    {
        var decision = WhiskeyRealism.Strategic.Projects.ProjectDoctrineScorer.Select(
            GrandStrategyRegistry.Resolve(0, EraStage.Amateur1861),
            new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignals
            {
                Alliance = 0,
                Era = EraStage.Amateur1861,
                FiscalPosture = FiscalPosture.BalancedWar
            },
            subsidyType: 1,
            vanillaProjectId: 96,
            vanillaWeight: 0f,
            candidates: null,
            fiscalWeight: null,
            runtimeFacts: id => new WhiskeyRealism.Strategic.Projects.ProjectRuntimeFacts
            {
                ProjectId = id,
                SubsidyLane = 1,
                Cost = id == 96 ? float.PositiveInfinity : float.NaN
            },
            fundingAvailable: float.NaN,
            netFundingPerDay: 0f,
            constructionCurrentlyWins: false);

        AssertFinite(decision.LaneIntent.FundingAvailable, "funding available");
        AssertFinite(decision.LaneIntent.FundingNeeded, "funding needed");
        AssertFinite(decision.LaneIntent.NetFundingPerDay, "net funding per day");
        AssertFinite(decision.LaneIntent.TimeToFundEstimateDays, "time to fund");
        AssertEqual(WhiskeyRealism.Strategic.Projects.ProjectDoctrineScorer.MaxTimeToFundEstimateDays, decision.LaneIntent.TimeToFundEstimateDays);
    }

    private static void ProjectDoctrineScorerIgnoresVanillaOnlyCriticality()
    {
        var decision = WhiskeyRealism.Strategic.Projects.ProjectDoctrineScorer.Select(
            GrandStrategyRegistry.Resolve(0, EraStage.Amateur1861),
            new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignals
            {
                Alliance = 0,
                Era = EraStage.Amateur1861,
                FiscalPosture = FiscalPosture.BalancedWar
            },
            subsidyType: 1,
            vanillaProjectId: 96,
            vanillaWeight: 10f,
            candidates: null,
            fiscalWeight: null,
            runtimeFacts: id => new WhiskeyRealism.Strategic.Projects.ProjectRuntimeFacts { ProjectId = id, SubsidyLane = 1, Cost = 1000f },
            fundingAvailable: 0f,
            netFundingPerDay: 0f,
            constructionCurrentlyWins: false);

        AssertEqual(false, decision.LaneIntent.CriticalDoctrineProject);
    }

    private static void ProjectDoctrineScorerMarksBestDoctrineReplacementCritical()
    {
        var signals = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignals
        {
            Alliance = 0,
            Era = EraStage.Operational1862,
            FiscalPosture = FiscalPosture.CreditDefense,
            CreditStress = 1f
        };

        var decision = WhiskeyRealism.Strategic.Projects.ProjectDoctrineScorer.Select(
            GrandStrategyRegistry.Resolve(0, EraStage.Operational1862),
            signals,
            subsidyType: 1,
            vanillaProjectId: 96,
            vanillaWeight: 0.1f,
            candidates: new[]
            {
                new ProjectCandidateInput { ProjectId = 97, SubsidyType = 1, VanillaWeight = 0.1f }
            },
            fiscalWeight: null,
            runtimeFacts: id => new WhiskeyRealism.Strategic.Projects.ProjectRuntimeFacts { ProjectId = id, SubsidyLane = 1, Cost = 1000f },
            fundingAvailable: 0f,
            netFundingPerDay: 0f,
            constructionCurrentlyWins: false);

        AssertEqual(true, decision.ShouldReplace);
        AssertEqual(97, decision.ProjectId);
        AssertEqual(true, decision.LaneIntent.CriticalDoctrineProject);
    }

    private static void ProjectDoctrineLogGateSuppressesRepeatedSignatures()
    {
        var gate = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineLogGate();
        string first = WhiskeyRealism.Strategic.Projects.ProjectDoctrineLogGate.SelectionSignature(1, 4, 11, 6, "strategy-margin");
        string repeat = WhiskeyRealism.Strategic.Projects.ProjectDoctrineLogGate.SelectionSignature(1, 4, 11, 6, "strategy-margin");
        string changed = WhiskeyRealism.Strategic.Projects.ProjectDoctrineLogGate.SelectionSignature(1, 4, 11, 118, "strategy-margin");

        AssertEqual(true, gate.ShouldLog(first));
        AssertEqual(false, gate.ShouldLog(repeat));
        AssertEqual(true, gate.ShouldLog(changed));
        AssertEqual(false, gate.ShouldLog(first));
    }

    private static void ProjectDoctrineLogGateIgnoresEmptySignatures()
    {
        var gate = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineLogGate();

        AssertEqual(false, gate.ShouldLog(null));
        AssertEqual(false, gate.ShouldLog(""));
        AssertEqual(true, gate.ShouldLog("material"));
        AssertEqual(false, gate.ShouldLog(""));
        AssertEqual(false, gate.ShouldLog("material"));
    }

    private static void ProjectDoctrineStarvedLaneSignatureIncludesFundingTrajectory()
    {
        var baseline = new WhiskeyRealism.Strategic.Projects.ProjectLaneIntent
        {
            Alliance = 0,
            SubsidyLane = 4,
            QueuedProjectId = 100,
            FundingAvailable = 250f,
            FundingNeeded = 1000f,
            NetFundingPerDay = 25f,
            TimeToFundEstimateDays = 30f,
            ConstructionCurrentlyWins = true,
            CriticalDoctrineProject = true
        };

        var differentFunding = new WhiskeyRealism.Strategic.Projects.ProjectLaneIntent
        {
            Alliance = 0,
            SubsidyLane = 4,
            QueuedProjectId = 100,
            FundingAvailable = 300f,
            FundingNeeded = 1000f,
            NetFundingPerDay = 25f,
            TimeToFundEstimateDays = 28f,
            ConstructionCurrentlyWins = true,
            CriticalDoctrineProject = true
        };

        var differentRate = new WhiskeyRealism.Strategic.Projects.ProjectLaneIntent
        {
            Alliance = 0,
            SubsidyLane = 4,
            QueuedProjectId = 100,
            FundingAvailable = 250f,
            FundingNeeded = 1000f,
            NetFundingPerDay = 50f,
            TimeToFundEstimateDays = 15f,
            ConstructionCurrentlyWins = true,
            CriticalDoctrineProject = true
        };

        string baselineSignature = WhiskeyRealism.Strategic.Projects.ProjectDoctrineLogGate.StarvedLaneSignature(baseline);
        AssertEqual(false, baselineSignature == WhiskeyRealism.Strategic.Projects.ProjectDoctrineLogGate.StarvedLaneSignature(differentFunding));
        AssertEqual(false, baselineSignature == WhiskeyRealism.Strategic.Projects.ProjectDoctrineLogGate.StarvedLaneSignature(differentRate));
    }
}
