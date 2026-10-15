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
// (CIC plan, era, director memory, policy plan, probe overlay) never reaches it.
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
            ("ledger set formation clone keeps lookup and order", FormationCloneKeepsLookupAndOrder),
            ("ledger set copied types are written only by the review", CopiedTypesWrittenOnlyByReview),
        });
    }

    private static StrategicLedgerSet Populated()
    {
        var set = new StrategicLedgerSet();
        for (int a = 0; a < 2; a++)
        {
            set.CICs[a] = new CIC
            {
                AllianceId = a,
                OfficerName = "cic" + a,
                OfficerPersonality = new PersonalityVector(0.1f * a, 0.2f, -0.3f, 0.4f, 0.5f),
                ActivePlan = new OperationalPlan
                {
                    OperationId = "op" + a,
                    OperationName = "Op " + a,
                    CurrentPhaseIndex = 1,
                    PendingRetarget = true,
                    PendingRetargetReason = "r",
                    IsDirty = true,
                    Phases =
                    {
                        new Phase { PhaseId = "p0", TargetObjectiveId = 3, DeadlineMonth = 5, DeadlineYear = 1862, Fallback = new Phase { PhaseId = "fb", TargetObjectiveId = 9 } },
                        new Phase { PhaseId = "p1", TargetObjectiveId = 7, ForceFractionRequired = 0.4f }
                    }
                }
            };
            set.Eras[a] = new EraStageManager { Stage = EraStage.Decisive1863 };
            set.Fronts[a] = TestFixtures.BuildLedger();
            set.ArmyAreas[a] = ArmyAreaLedger.Build(new[] { new ArmyAreaInput { UnitKey = "u" + a, AllianceId = a, CurrentAreaKey = "A", Strength = 5000f } }, "A");
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
            var w = working.CICs[a];
            var p = published.CICs[a];
            AssertFalse(ReferenceEquals(w, p), "CIC copied");
            AssertEqual(w.OfficerPersonality.ToString(), p.OfficerPersonality.ToString(), "personality");
            AssertEqual(w.ActivePlan.OperationName, p.ActivePlan.OperationName, "operation name");
            AssertEqual(w.ActivePlan.PendingRetarget, p.ActivePlan.PendingRetarget, "pending retarget");
            AssertEqual(w.ActivePlan.PendingRetargetReason, p.ActivePlan.PendingRetargetReason, "retarget reason");
            AssertEqual(w.ActivePlan.CurrentPhase.TargetObjectiveId, p.ActivePlan.CurrentPhase.TargetObjectiveId, "current phase");
            AssertEqual(5, p.ActivePlan.Phases[0].DeadlineMonth, "phase deadline");
            AssertEqual(9, p.ActivePlan.Phases[0].Fallback.TargetObjectiveId, "fallback copied");
            AssertNear(0.4f, p.ActivePlan.Phases[1].ForceFractionRequired, 1e-6f, "phase force fraction");
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
            AssertTrue(ReferenceEquals(working.ArmyAreas[a], published.ArmyAreas[a]), "army areas shared");
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
            var plan = working.CICs[a].ActivePlan;
            plan.CurrentPhaseIndex = 0;
            plan.IsDirty = false;
            plan.Phases[1].TargetObjectiveId = 99;
            plan.Phases[0].Fallback.TargetObjectiveId = 98;
            plan.Phases.Add(new Phase { TargetObjectiveId = 50 });
            working.CICs[a].OfficerName = "replaced";
            working.Eras[a].CheckTransition(1, 1864, false, false);
            working.DirectorMemories[a].LastPosture = new DirectorPosture { Pace = CampaignPace.Overheated };
            working.DirectorMemories[a].CapitalDangerStreakDays = 9;
            working.DirectorMemories[a].RecentEventSummaries.Add("e2");
            working.PolicyPlans[a].Update(new List<PolicyScore> { new PolicyScore { PolicyId = 3, Total = 5f } }, working.Needs[a], working.Needs[a]);
            AssertTrue(working.FormationDirectives[a].ApplyOperationalProbe(new OperationalProbeOutput
            {
                SelectedUnitKey = "corps" + a,
                Decision = OperationalProbeDecision.Withdraw,
                Reason = "w"
            }), "overlay applied to the working ledger");
        }

        AssertEqual(before, DailyReviewSchedulerTests.Signature(published), "published copy unchanged by in-place work");
        AssertTrue(DailyReviewSchedulerTests.Signature(working) != before, "the working set did change");
        AssertEqual(1, published.CICs[0].ActivePlan.CurrentPhaseIndex, "plan phase");
        AssertEqual(EraStage.Decisive1863, published.Eras[0].Stage, "era");
        AssertEqual(2, published.DirectorMemories[0].CapitalDangerStreakDays, "memory");
        AssertEqual(1, published.DirectorMemories[0].RecentEventSummaries.Count, "event list copied");
        AssertEqual("w", working.FormationDirectives[0].GetAssignment("corps0").Reason, "overlay on the working ledger");
        AssertTrue(published.FormationDirectives[0].GetAssignment("corps0").Reason != "w", "overlay not published");
    }

    private static void PublishKeepsNulls()
    {
        var working = new StrategicLedgerSet();
        working.CICs[1] = new CIC { AllianceId = 1, OfficerName = "Davis" };
        var published = Populated();
        published.PublishFrom(working);
        AssertTrue(published.CICs[0] == null, "null CIC published as null");
        AssertTrue(published.CICs[1].ActivePlan == null, "CIC without a plan");
        AssertTrue(published.Eras[0] == null && published.Fronts[0] == null && published.FormationDirectives[0] == null, "null ledgers");
        AssertTrue(published.FiscalIntents[1] == null && published.Needs[1] == null, "null intents");
        AssertEqual(-1, published.PolicyPlans[0].CurrentPick, "fresh policy plan");
        AssertTrue(published.DirectorMemories[0].LastPosture == null, "fresh memory");
        published.PublishFrom(published);
        AssertEqual("Davis", published.CICs[1].OfficerName, "publishing onto itself is a no-op");
    }

    private static void FormationCloneKeepsLookupAndOrder()
    {
        var ledger = Formations(0);
        var clone = ledger.Clone();
        AssertEqual(ledger.Summary(), clone.Summary(), "order and fields");
        AssertEqual(ledger.Assignments.Count, clone.Assignments.Count, "count");
        foreach (var assignment in ledger.Assignments)
        {
            var copy = clone.GetAssignment(assignment.UnitKey);
            AssertTrue(copy != null && !ReferenceEquals(copy, assignment), "lookup returns the copy for " + assignment.UnitKey);
            AssertEqual(assignment.Directive, copy.Directive, "directive");
        }
        AssertEqual(ledger.Pressure.LowSupplyCount, clone.Pressure.LowSupplyCount, "pressure");
        AssertEqual(ledger.Pressure.TopSupplyAreaKey, clone.Pressure.TopSupplyAreaKey, "pressure area");
        AssertFalse(ReferenceEquals(ledger.Pressure, clone.Pressure), "pressure copied");
        AssertTrue(clone.ApplyOperationalProbe(new OperationalProbeOutput { SelectedUnitKey = "corps0", Decision = OperationalProbeDecision.Probe }), "overlay on the clone");
        AssertTrue(ledger.GetAssignment("corps0").Directive != FormationDirective.Probe, "original untouched");
    }

    // Patches read published copies of the CIC (and plan), era, director memory, policy plan and
    // formation directives; a write from a patch would land on a copy the next publish discards.
    // These types are written only by the review (StrategicCoordinator), their own files, the
    // CIC plan router and the sidecar codec/restore.
    private static void CopiedTypesWrittenOnlyByReview()
    {
        var rules = new (string pattern, string[] allowed)[]
        {
            (@"\.(OfficerName|OfficerPersonality|ActivePlan)\s*=[^=]", new[] { "StrategicCoordinator.cs", "CIC.cs", "SidecarDtoCodec.cs" }),
            (@"ActivePlan\.\w+\s*=[^=]", new[] { "StrategicCoordinator.cs", "CIC.cs", "SidecarDtoCodec.cs", "CicReviewRouter.cs" }),
            (@"\.(CheckTransition|ResetToDate)\(|Eras\[[^\]]*\]\.Stage\s*=[^=]", new[] { "StrategicCoordinator.cs", "EraStageManager.cs" }),
            (@"\.(CapitalDangerStreakDays|DaysSinceLastBattle|LastFullRefreshDay|LastSourceSignature|LastPosture)\s*=[^=]|RecentEventSummaries\.(Add|Clear|Remove|Insert)", new[] { "StrategicCoordinator.cs", "StrategicResilienceDirector.cs", "DirectorMemory.cs" }),
            (@"PolicyPlans\[[^\]]*\]\.(Update|Restore)\(|\bplan\.Update\(", new[] { "StrategicCoordinator.cs" }),
            (@"ApplyOperationalProbe\(|GetAssignment\([^)]*\)\??\.\w+\s*=[^=]|Assignments\[[^\]]*\]\.\w+\s*=[^=]", new[] { "StrategicCoordinator.cs", "FormationDirectiveLedger.cs" }),
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
