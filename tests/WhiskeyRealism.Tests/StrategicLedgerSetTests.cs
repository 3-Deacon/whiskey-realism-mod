using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Strategic.Policies;

using static TestHarness;

// StrategicLedgerSet.PublishFrom (perf slice, M2): the published copy equals the working set
// field for field when it is taken, and the next day's in-place work on the working set
// (era, director memory, policy plan) never reaches it.
internal static class StrategicLedgerSetTests
{
    public static void Run()
    {
        RunAll(new (string name, Action run)[]
        {
            ("ledger set publish equals the working set", PublishEqualsWorking),
            ("ledger set publish shares rebuilt ledgers by reference", PublishSharesReplacedLedgers),
            ("ledger set in-place work after publish does not reach the published copy", InPlaceWorkIsIsolated),
            ("ledger set publish keeps nulls", PublishKeepsNulls),
            ("ledger set copied types are written only by the review", CopiedTypesWrittenOnlyByReview),
        });
    }

    private static StrategicLedgerSet Populated()
    {
        var set = new StrategicLedgerSet();
        for (int a = 0; a < 2; a++)
        {
            set.Eras[a] = new EraStageManager { Stage = EraStage.Decisive1863 };
            set.Fronts[a] = TestFixtures.BuildLedger();
            set.FormationDirectives[a] = Formations(a);
            set.FiscalIntents[a] = new FiscalOutput { Posture = FiscalPosture.CreditDefense, Signature = "f" + a };
            set.ConstructionIntents[a] = new ConstructionOutput { Signature = "c" + a };
            set.DefenseIntents[a] = new DefenseIntentLedgerOutput { AllianceId = a, Signature = "d" + a };
            set.Needs[a] = NeedVector.FromArray(new[] { 0.1f, 0.9f, 0.3f });
            set.NeedSnapshots[a] = new NeedSnapshot { Alliance = a };
            set.PolicyPlans[a].Update(new List<PolicyScore>
            {
                new PolicyScore { PolicyId = 1, Total = 0.5f },
                new PolicyScore { PolicyId = 2, Total = 0.9f },
                new PolicyScore { PolicyId = 3, Total = 0.1f }
            }, set.Needs[a], null);
            set.DirectorMemories[a].LastPosture = new DirectorPosture { AllianceId = a, Pace = CampaignPace.Stalemated, Reason = "x" };
            set.DirectorMemories[a].CapitalDangerStreakDays = 2;
            set.DirectorMemories[a].LastFullRefreshDay = 40;
            set.DirectorMemories[a].LastSourceSignature = "sig";
            set.DirectorMemories[a].RecentEventSummaries.Add("e1");
        }
        set.CampaignMap = CampaignMapLedger.Build(new[] { new CampaignMapTown { CityName = "Richmond", StateId = 1, Owner = 1 } });
        return set;
    }

    private static FormationDirectiveLedger Formations(int alliance)
    {
        var snapshots = new List<FormationSnapshot>
        {
            TestFixtures.Snapshot("army" + alliance, alliance, 16, 30000f, 10000f, FormationLevel.Corps, FrontPosture.Hold),
            TestFixtures.Snapshot("corps" + alliance, alliance, 15, 12000f, 20000f, FormationLevel.Army, FrontPosture.Delay)
        };
        snapshots[1].Supply = 0.2f;
        return FormationDirectiveLedger.Build(snapshots, EraStage.Decisive1863, "VirginiaCapitalCorridor");
    }

    private static void PublishEqualsWorking()
    {
        var working = Populated();
        var published = new StrategicLedgerSet();
        published.PublishFrom(working);
        AssertEqual(DailyReviewSchedulerTests.Signature(working), DailyReviewSchedulerTests.Signature(published), "signature");
        for (int a = 0; a < 2; a++)
        {
            AssertEqual(working.PolicyPlans[a].DescribeTop(5), published.PolicyPlans[a].DescribeTop(5), "policy ranking");
            AssertEqual(2, published.PolicyPlans[a].CurrentPick, "policy pick");
            AssertEqual("sig", published.DirectorMemories[a].LastSourceSignature, "memory signature");
            AssertTrue(ReferenceEquals(working.DirectorMemories[a].LastPosture, published.DirectorMemories[a].LastPosture), "posture shared (never mutated once stored)");
        }
    }

    private static void PublishSharesReplacedLedgers()
    {
        var working = Populated();
        var published = new StrategicLedgerSet();
        published.PublishFrom(working);
        for (int a = 0; a < 2; a++)
        {
            AssertTrue(ReferenceEquals(working.Fronts[a], published.Fronts[a]), "front shared");
            AssertTrue(ReferenceEquals(working.FormationDirectives[a], published.FormationDirectives[a]), "formation directives shared");
            AssertTrue(ReferenceEquals(working.FiscalIntents[a], published.FiscalIntents[a]), "fiscal shared");
            AssertTrue(ReferenceEquals(working.ConstructionIntents[a], published.ConstructionIntents[a]), "construction shared");
            AssertTrue(ReferenceEquals(working.DefenseIntents[a], published.DefenseIntents[a]), "defense shared");
            AssertTrue(ReferenceEquals(working.Needs[a], published.Needs[a]), "needs shared");
            AssertTrue(ReferenceEquals(working.NeedSnapshots[a], published.NeedSnapshots[a]), "need snapshot shared");
        }
        AssertTrue(ReferenceEquals(working.CampaignMap, published.CampaignMap), "map shared");
        working.Fronts[0] = null;
        AssertTrue(published.Fronts[0] != null, "replacing a working ledger leaves the published one");
    }

    private static void InPlaceWorkIsIsolated()
    {
        var working = Populated();
        var published = new StrategicLedgerSet();
        published.PublishFrom(working);
        string before = DailyReviewSchedulerTests.Signature(published);

        for (int a = 0; a < 2; a++)
        {
            working.Eras[a].CheckTransition(11, 1864, false, 3);
            working.DirectorMemories[a].LastPosture = new DirectorPosture { Pace = CampaignPace.Overheated };
            working.DirectorMemories[a].CapitalDangerStreakDays = 9;
            working.DirectorMemories[a].RecentEventSummaries.Add("e2");
            working.PolicyPlans[a].Update(new List<PolicyScore> { new PolicyScore { PolicyId = 3, Total = 5f } }, working.Needs[a], working.Needs[a]);
        }

        AssertEqual(before, DailyReviewSchedulerTests.Signature(published), "published copy unchanged by in-place work");
        AssertTrue(DailyReviewSchedulerTests.Signature(working) != before, "the working set did change");
        AssertEqual(EraStage.Decisive1863, published.Eras[0].Stage, "era");
        AssertEqual(2, published.DirectorMemories[0].CapitalDangerStreakDays, "memory");
        AssertEqual(1, published.DirectorMemories[0].RecentEventSummaries.Count, "event list copied");
    }

    private static void PublishKeepsNulls()
    {
        var working = new StrategicLedgerSet();
        working.Eras[1] = new EraStageManager { Stage = EraStage.Amateur1861 };
        var published = Populated();
        published.PublishFrom(working);
        AssertTrue(published.Eras[0] == null && published.Fronts[0] == null && published.FormationDirectives[0] == null, "null ledgers");
        AssertTrue(published.FiscalIntents[1] == null && published.Needs[1] == null, "null intents");
        AssertEqual(-1, published.PolicyPlans[0].CurrentPick, "fresh policy plan");
        AssertTrue(published.DirectorMemories[0].LastPosture == null, "fresh memory");
        published.PublishFrom(published);
        AssertEqual(EraStage.Amateur1861, published.Eras[1].Stage, "publishing onto itself is a no-op");
    }

    // Patches read published copies of the era, director memory and policy plan, and the shared
    // formation directive ledger; a write from a patch would land on a copy the next publish
    // discards (or, on the shared ledger, on the published day itself). These types are written only
    // by the review (StrategicCoordinator), their own files and the sidecar codec/restore.
    private static void CopiedTypesWrittenOnlyByReview()
    {
        var rules = new (string pattern, string[] allowed)[]
        {
            (@"\.(CheckTransition|ResetToDate)\(|Eras\[[^\]]*\]\.Stage\s*=[^=]", new[] { "StrategicCoordinator.cs", "EraStageManager.cs" }),
            (@"\.(CapitalDangerStreakDays|DaysSinceLastBattle|LastFullRefreshDay|LastSourceSignature|LastPosture)\s*=[^=]|RecentEventSummaries\.(Add|Clear|Remove|Insert)", new[] { "StrategicCoordinator.cs", "StrategicResilienceDirector.cs", "DirectorMemory.cs" }),
            (@"PolicyPlans\[[^\]]*\]\.(Update|Restore)\(|\bplan\.Update\(", new[] { "StrategicCoordinator.cs" }),
            (@"GetAssignment\([^)]*\)\??\.\w+\s*=[^=]|Assignments\[[^\]]*\]\.\w+\s*=[^=]", new[] { "StrategicCoordinator.cs", "FormationDirectiveLedger.cs" }),
        };
        string root = TestFixtures.FindRepoPath("src/WhiskeyRealism");
        var hits = new List<string>();
        foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            string[] lines = File.ReadAllLines(file);
            foreach (var (pattern, allowed) in rules)
            {
                if (Array.IndexOf(allowed, name) >= 0) continue;
                var regex = new Regex(pattern);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].TrimStart();
                    if (line.StartsWith("//")) continue;
                    if (regex.IsMatch(line)) hits.Add(name + ":" + (i + 1) + ": " + line);
                }
            }
        }
        AssertEqual("", string.Join("\n", hits), "writes to published copies outside the review");
    }
}
