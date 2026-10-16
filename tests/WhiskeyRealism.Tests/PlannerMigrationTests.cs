using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Util;

using static TestHarness;
using static TestFixtures;

// Stage G slice G6 (docs/superpowers/plans/2026-10-15-campaign-planner.md § G6): the readers the
// retired CIC / army-area / catalog layers fed now read the published plan. Pure halves are run;
// the runtime and patch halves are pinned in source, by the lines that matter (a reader that
// stops calling the one helper is the mutation these catch).
internal static class PlannerMigrationTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("MainTargetIsPublishedObjectivePosition", MainTargetIsPublishedObjectivePosition),
            ("MainTargetIsTheObjectivesOwnTownNotTheClampedFrontier", MainTargetIsTheObjectivesOwnTownNotTheClampedFrontier),
            ("FrontLedgerTargetFromPublishedPlan", FrontLedgerTargetFromPublishedPlan),
            ("FormationDirectiveTargetFromPublishedPlan", FormationDirectiveTargetFromPublishedPlan),
            ("AreaKeyIsTheSectorOrTheTheater", AreaKeyIsTheSectorOrTheTheater),
            ("AreaKeyOfMainTargetFollowsThePublishedPlan", AreaKeyOfMainTargetFollowsThePublishedPlan),
            ("EveryAreaKeyProducerGoesThroughPlannerAreaKeys", EveryAreaKeyProducerGoesThroughPlannerAreaKeys),
            ("ParentKeyOfReadsTheCommandHierarchy", ParentKeyOfReadsTheCommandHierarchy),
            ("DirectiveLedgerReadsNoTransformParent", DirectiveLedgerReadsNoTransformParent),
            ("AlliancePersonalityIsEraAndFaction", AlliancePersonalityIsEraAndFaction),
            ("DefenseInputTakesPlannerEligibilityNotACic", DefenseInputTakesPlannerEligibilityNotACic),
            ("PerkRoleFollowsTheFormationsAllocationRole", PerkRoleFollowsTheFormationsAllocationRole),
            ("FleetPerkRoleFollowsTheMainTheater", FleetPerkRoleFollowsTheMainTheater),
            ("PerkPatchReadsTheAllocationNotTheCatalog", PerkPatchReadsTheAllocationNotTheCatalog),
            ("ArmyGroupPatchGroupsByAllocationSectorAndSeatsNoCommander", ArmyGroupPatchGroupsByAllocationSectorAndSeatsNoCommander),
            ("RecruitmentIntentReadsThePublishedPlan", RecruitmentIntentReadsThePublishedPlan),
            ("SectorByIdFindsOnlyAPlannedSector", SectorByIdFindsOnlyAPlannedSector),
            ("MainLabelNamesTheMainEffortsTheaterAndObjective", MainLabelNamesTheMainEffortsTheaterAndObjective),
            ("HeartbeatReportsThePlannersMainEffort", HeartbeatReportsThePlannersMainEffort),
        });
    }

    // Code lines only: line comments dropped, so a pin on an absent name is not satisfied by a comment that explains it.
    private static string Code(string source)
    {
        return Regex.Replace(source, @"(?m)^\s*//.*$", "");
    }

    private static string Coordinator() => ReadRepoFile("src/WhiskeyRealism/Strategic/StrategicCoordinator.cs");

    private static TheaterPlan MainEffortPlan(out CampaignSnapshot s)
    {
        s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 200f, 20000f));
        return ExecutorFixtures.RealPlan(s, ExecutorFixtures.State());
    }

    private static void Publish(TheaterPlan plan, CampaignSnapshot s)
    {
        var allocation = new Allocation { Alliance = s.Alliance };
        CampaignPlanStore.Publish(s.Alliance, plan, allocation, ExecutorFixtures.Sectors(s));
        CampaignPlanStore.PreparePublish();
        CampaignPlanStore.SwapPublished();
    }

    private static void MainTargetIsPublishedObjectivePosition()
    {
        CampaignScope.ResetAll();
        try
        {
            var plan = MainEffortPlan(out var s);
            AssertTrue(plan.MainObjectiveId >= 0, "the fixture forms a main effort");
            int k = -1;
            for (int i = 0; i < s.Objectives.Count; i++)
                if (s.Objectives[i].ObjectiveId == plan.MainObjectiveId) k = i;
            AssertTrue(k >= 0, "the main objective is one of the snapshot's");
            AssertEqual(s.Objectives[k].X, plan.MainObjectiveX, "the plan carries the objective's X");
            AssertEqual(s.Objectives[k].Z, plan.MainObjectiveZ, "and its Z");

            AssertFalse(CampaignPlanStore.TryGetMainTarget(0, out _, out _, out _), "nothing published yet: no main target");
            Publish(plan, s);
            AssertTrue(CampaignPlanStore.TryGetMainTarget(0, out int id, out float x, out float z), "published: the main target");
            AssertEqual(plan.MainObjectiveId, id, "objective");
            AssertEqual(plan.MainObjectiveX, x, "x");
            AssertEqual(plan.MainObjectiveZ, z, "z");
            AssertFalse(CampaignPlanStore.TryGetMainTarget(1, out id, out _, out _), "the other alliance has none");
            AssertEqual(-1, id, "and its id reads -1");
            AssertFalse(CampaignPlanStore.TryGetMainTarget(2, out _, out _, out _), "alliances 0 and 1 only");

            var none = ExecutorFixtures.RealPlan(ExecutorFixtures.Line(0, 3), ExecutorFixtures.State());
            AssertEqual(-1, none.MainObjectiveId, "no objective, no main effort");
            Publish(none, ExecutorFixtures.Line(0, 3));
            AssertFalse(CampaignPlanStore.TryGetMainTarget(0, out id, out _, out _), "a published plan with no main effort has no main target");
            AssertEqual(-1, id, "id reads -1");
        }
        finally { CampaignScope.ResetAll(); }
    }

    // Scenario 001's mapped ground (G3c): the candidate position is the mapped town, and it is the town the readers get, not
    // the farthest in-reach point the movers get (SectorPlan.TargetX/Z, clamped to supply).
    private static void MainTargetIsTheObjectivesOwnTownNotTheClampedFrontier()
    {
        var s = ExecutorFixtures.Line(0, 7);
        s.Own.Add(ExecutorFixtures.Corps(1, 0f, 20000f));
        // Richmond, far beyond the single IIP the corps is supplied from.
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 3, X = 600f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = 1f });
        var plan = ExecutorFixtures.RealPlan(s, ExecutorFixtures.State());
        AssertEqual(3, plan.MainObjectiveId, "the only candidate is the main effort");
        AssertEqual(600f, plan.MainObjectiveX, "the town's own X");
        AssertEqual(0f, plan.MainObjectiveZ, "the town's own Z");
        var sector = plan.SectorById(plan.MainSectorId);
        AssertTrue(sector != null && sector.TargetObjectiveId == 3, "the main sector follows it");
        AssertTrue(sector.TargetX < 600f, "the movers' gather point stops short of the town: " + sector.TargetX);
    }

    private static void FrontLedgerTargetFromPublishedPlan()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/FrontSectorRuntime.cs");
        string build = BodyOf(src, "internal static FrontSectorLedger BuildForAlliance(");
        AssertContains(build, "CampaignPlanStore.TryGetMainTarget(allianceId, out _, out float targetX, out float targetZ)", "the plan target is the published main target");
        AssertContains(build, "bool isPlanTarget = hasMainTarget && targetTheater == theater;", "no main effort flags no theater");
        AssertFalse(Code(src).Contains("ObjectiveAdapter"), "the front ledger names no ObjectiveAdapter");
        string coordinator = Coordinator();
        AssertContains(BodyOf(coordinator, "private bool UpdateFrontLedger("), "FrontSectorRuntime.BuildForAlliance(alliance)", "the coordinator passes no objective id");
    }

    private static void FormationDirectiveTargetFromPublishedPlan()
    {
        string coordinator = Coordinator();
        string update = BodyOf(coordinator, "private void UpdateFormationDirectiveLedger(");
        AssertContains(update, "string planTargetAreaKey = PlannerAreaKeys.OfMainTarget(alliance);", "the directive ledger's target area is the published main target's key");
        AssertFalse(Code(update).Contains("ObjectiveAdapter"), "and no ObjectiveAdapter");
        string keys = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/PlannerAreaKeys.cs");
        AssertContains(BodyOf(keys, "internal static string OfMainTarget("), "CampaignPlanStore.TryGetMainTarget(alliance, out _, out float x, out float z)", "the key is read off TryGetMainTarget");
        AssertContains(BodyOf(coordinator, "private string FormationSourceSignature("), "CampaignPlanStore.TryGetMainTarget(alliance, out int targetObjectiveId, out _, out _);", "so is the source signature's target");
    }

    private static SectorModel TwoSectors(out CampaignSnapshot s)
    {
        s = ExecutorFixtures.Wide();
        return ExecutorFixtures.Sectors(s);
    }

    private static void AreaKeyIsTheSectorOrTheTheater()
    {
        var sectors = TwoSectors(out var s);
        int west = sectors.SectorAt(100f, 0f), east = sectors.SectorAt(500f, 0f);
        AssertTrue(west >= 0 && east >= 0, "both points lie in a sector");
        AssertEqual(PlannerAreaKeys.OfSector(west), PlannerAreaKeys.At(sectors, 110f, 5f), "a point takes its sector's key");
        AssertEqual("sector-" + west, PlannerAreaKeys.At(sectors, 110f, 5f), "the format");
        AssertEqual(PlannerAreaKeys.At(sectors, 100f, 0f), PlannerAreaKeys.At(sectors, 120f, 0f), "two points of one sector share a key");
        if (west != east)
            AssertTrue(PlannerAreaKeys.At(sectors, 100f, 0f) != PlannerAreaKeys.At(sectors, 500f, 0f), "two sectors, two keys");
        AssertEqual(TheaterClassifier.FromPosition(1300f, -800f).ToString(), PlannerAreaKeys.At((SectorModel)null, 1300f, -800f), "no sector model: the theater's name, so every producer still agrees");
        AssertEqual(TheaterClassifier.FromPosition(1300f, -800f).ToString(), PlannerAreaKeys.At(new SectorModel(), 1300f, -800f), "an empty model is no model");
    }

    private static void AreaKeyOfMainTargetFollowsThePublishedPlan()
    {
        CampaignScope.ResetAll();
        try
        {
            AssertTrue(PlannerAreaKeys.OfMainTarget(0) == null, "no plan, no key");
            var plan = MainEffortPlan(out var s);
            Publish(plan, s);
            var sectors = ExecutorFixtures.Sectors(s);
            AssertEqual(PlannerAreaKeys.At(sectors, plan.MainObjectiveX, plan.MainObjectiveZ), PlannerAreaKeys.OfMainTarget(0), "the key of the main objective's own sector");
            AssertEqual(PlannerAreaKeys.At(sectors, 100f, 0f), PlannerAreaKeys.At(0, 100f, 0f), "the alliance form reads the published sector model");
            AssertTrue(PlannerAreaKeys.OfMainTarget(1) == null, "the other alliance has no main target");
        }
        finally { CampaignScope.ResetAll(); }
    }

    // The key vocabulary must be one: a producer that keeps the old classifier, or builds its own string, makes every
    // equality with the others false for good, with no error.
    private static void EveryAreaKeyProducerGoesThroughPlannerAreaKeys()
    {
        string formation = Code(ReadRepoFile("src/WhiskeyRealism/Strategic/FormationDirectiveRuntime.cs"));
        AssertContains(formation, "string key = PlannerAreaKeys.At(sectors, u.transform.position.x, u.transform.position.z);", "the support index");
        AssertContains(formation, "snapshot.AreaKey = PlannerAreaKeys.At(sectors, unit.transform.position.x, unit.transform.position.z);", "the unit's key");
        AssertFalse(formation.Contains("ArmyAreaRuntime") || formation.Contains("ArmyAreaClassifier"), "no classifier key in the directive runtime");
        string construction = Code(ReadRepoFile("src/WhiskeyRealism/Strategic/Construction/ConstructionRuntime.cs"));
        AssertContains(construction, "PlannerAreaKeys.At(input.AllianceId, position.x, position.z)", "the site's key, compared with the directive ledger's");
        AssertFalse(construction.Contains("ArmyAreaClassifier"), "no classifier key in the construction corridor");
        string recruitment = Code(ReadRepoFile("src/WhiskeyRealism/Patches/RecruitmentPatch.cs"));
        AssertContains(recruitment, "return PlannerAreaKeys.At(alliance, position.x, position.z);", "the protected area is the capital's key");
        AssertContains(recruitment, "return PlannerAreaKeys.At(alliance, state.averageposition.x, state.averageposition.z);", "a candidate state's key");
        AssertFalse(recruitment.Contains("ArmyAreaClassifier") || recruitment.Contains("VirginiaCapitalCorridor") || recruitment.Contains("WashingtonDefenses"), "no literal area names");
        string defense = Code(ReadRepoFile("src/WhiskeyRealism/Strategic/DefenseIntentRuntime.cs"));
        AssertFalse(defense.Contains("ArmyAreaRuntime"), "the defence candidates carry no area key at all (it had no reader)");
        AssertFalse(Code(ReadRepoFile("src/WhiskeyRealism/Strategic/DefenseIntentTypes.cs")).Contains("string AreaKey;"), "DefenseCandidate.AreaKey is gone");
    }

    private static void ParentKeyOfReadsTheCommandHierarchy()
    {
        var s = new CampaignSnapshot();
        var army = ExecutorFixtures.Corps(10, 0f, 1000f); army.UnitKey = "army:10"; army.ParentInstanceId = -1;
        var corps = ExecutorFixtures.Corps(11, 0f, 1000f); corps.UnitKey = "corps:11"; corps.ParentInstanceId = 10;
        var division = ExecutorFixtures.Corps(12, 0f, 1000f); division.UnitKey = "div:12"; division.ParentInstanceId = 11;
        var orphan = ExecutorFixtures.Corps(13, 0f, 1000f); orphan.UnitKey = "orphan:13"; orphan.ParentInstanceId = 99;
        s.Own.Add(army); s.Own.Add(corps); s.Own.Add(division); s.Own.Add(orphan);
        AssertEqual("corps:11", s.ParentKeyOf(12), "a division's parent is the corps that holds it");
        AssertEqual("army:10", s.ParentKeyOf(11), "a corps' parent is the army");
        AssertTrue(s.ParentKeyOf(10) == null, "a root has none");
        AssertTrue(s.ParentKeyOf(13) == null, "a parent outside the snapshot is none");
        AssertTrue(s.ParentKeyOf(404) == null, "an unknown formation has none");
    }

    private static void DirectiveLedgerReadsNoTransformParent()
    {
        string formation = Code(ReadRepoFile("src/WhiskeyRealism/Strategic/FormationDirectiveRuntime.cs"));
        AssertFalse(formation.Contains("transform.parent"), "AGENTS.md: the command parent never comes from the Unity hierarchy");
        AssertContains(formation, "ParentUnitKey = commandTree?.ParentKeyOf(((UnityEngine.Object)unit).GetInstanceID()),", "the snapshot's D5 parent");
        AssertContains(formation, "var commandTree = CampaignPlannerRuntime.SnapshotOf(allianceId);", "today's snapshot");
        string runtime = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs");
        AssertContains(BodyOf(runtime, "internal static CampaignSnapshot SnapshotOf("), "PlanFreshness.IsToday(Snapshots[alliance].DaySerial) ? Snapshots[alliance] : null", "yesterday's tree is no tree");
    }

    private static void AlliancePersonalityIsEraAndFaction()
    {
        foreach (EraStage stage in new[] { EraStage.Amateur1861, EraStage.Operational1862, EraStage.Decisive1863, EraStage.TotalWar1864 })
        {
            var era = new EraStageManager { Stage = stage };
            foreach (int alliance in new[] { 0, 1 })
            {
                var expected = PersonalityVector.Compose(default(PersonalityVector), era.StageVector, FactionProfiles.For(alliance));
                var actual = PersonalityVector.ForAlliance(alliance, era);
                AssertEqual(expected.Aggression, actual.Aggression, stage + "/" + alliance + " aggression");
                AssertEqual(expected.Caution, actual.Caution, stage + "/" + alliance + " caution");
                AssertEqual(expected.Audacity, actual.Audacity, stage + "/" + alliance + " audacity");
            }
        }
        var amateurCsa = PersonalityVector.ForAlliance(1, new EraStageManager { Stage = EraStage.Amateur1861 });
        AssertNear(-0.3f + 0.2f, amateurCsa.Aggression, 0.0001f, "1861 CSA: the era's -0.3 and the faction's +0.2, no officer vector");
        AssertNear(0.5f + 0f, amateurCsa.Caution, 0.0001f, "caution");
        var noEra = PersonalityVector.ForAlliance(0, null);
        AssertEqual(FactionProfiles.Union.Caution, noEra.Caution, "no era: the faction alone");
    }

    private static void DefenseInputTakesPlannerEligibilityNotACic()
    {
        string src = Code(ReadRepoFile("src/WhiskeyRealism/Strategic/DefenseIntentRuntime.cs"));
        string build = BodyOf(src, "internal static DefenseIntentInput BuildInput(");
        AssertContains(build, "bool plannerRuns,", "the planner's eligibility, not a CIC object");
        AssertContains(build, "PlayerIsCIC = !plannerRuns,", "a stood-down alliance gets no defence response");
        AssertContains(build, "CICPersonality = PersonalityVector.ForAlliance(allianceId, era),", "the standing disposition");
        string coordinator = Coordinator();
        AssertContains(BodyOf(coordinator, "private void UpdateDefenseIntentCore("), "plannerRuns: ShouldRunPlannerFor(alliance, ResolvePlayerAlliance(), true),", "asked of the one eligibility predicate");
    }

    private static void PerkRoleFollowsTheFormationsAllocationRole()
    {
        AssertEqual(ArmyPerkRole.CapitalDefense, PerkSelectionScorer.RoleOfFormation(FormationRole.CapitalGuard, 0), "capital guard");
        AssertEqual(ArmyPerkRole.CapitalDefense, PerkSelectionScorer.RoleOfFormation(FormationRole.Hold, 1), "hold");
        AssertEqual(ArmyPerkRole.CapitalDefense, PerkSelectionScorer.RoleOfFormation(FormationRole.Reserve, 1), "reserve");
        AssertEqual(ArmyPerkRole.Siege, PerkSelectionScorer.RoleOfFormation(FormationRole.MainEffort, 0), "the Union's main effort sieges");
        AssertEqual(ArmyPerkRole.Raid, PerkSelectionScorer.RoleOfFormation(FormationRole.MainEffort, 1), "the CSA's main effort raids");
        AssertEqual(ArmyPerkRole.Maneuver, PerkSelectionScorer.RoleOfFormation(FormationRole.Support, 0), "support");
        AssertEqual(ArmyPerkRole.Recovery, PerkSelectionScorer.RoleOfFormation(FormationRole.Recover, 1), "recover");
        AssertEqual(ArmyPerkRole.Scouting, PerkSelectionScorer.RoleOfFormation(FormationRole.Recon, 0), "recon");
        AssertEqual(ArmyPerkRole.General, PerkSelectionScorer.RoleOfFormation(FormationRole.DepotBuilder, 0), "depot builder");
        foreach (FormationRole role in Enum.GetValues(typeof(FormationRole)))
            foreach (int alliance in new[] { 0, 1 })
                PerkSelectionScorer.RoleOfFormation(role, alliance); // every role maps (the switch default catches a new one)
    }

    private static void FleetPerkRoleFollowsTheMainTheater()
    {
        AssertEqual(FleetPerkRole.Blockade, PerkSelectionScorer.FleetRole(0, Theater.East, false), "the Union blockades");
        AssertEqual(FleetPerkRole.Blockade, PerkSelectionScorer.FleetRole(0, Theater.Coast, true), "wherever");
        AssertEqual(FleetPerkRole.PortDefense, PerkSelectionScorer.FleetRole(1, Theater.Coast, false), "the CSA defends a coastal main effort");
        AssertEqual(FleetPerkRole.PortDefense, PerkSelectionScorer.FleetRole(1, Theater.East, true), "and a threatened capital");
        AssertEqual(FleetPerkRole.Raid, PerkSelectionScorer.FleetRole(1, Theater.East, false), "otherwise it raids");
        AssertEqual(FleetPerkRole.Raid, PerkSelectionScorer.FleetRole(1, Theater.Unknown, false), "no plan: the old default");
    }

    private static void PerkPatchReadsTheAllocationNotTheCatalog()
    {
        string src = Code(ReadRepoFile("src/WhiskeyRealism/Patches/PerkSelectionPatch.cs"));
        AssertFalse(src.Contains("ObjectiveCatalog") || src.Contains("ObjectiveMetadata") || src.Contains("CICs") || src.Contains("ActivePlan"), "no catalog, metadata or CIC read");
        string army = BodyOf(src, "private static ArmyPerkRole ResolveArmyRole(");
        AssertContains(army, "CampaignPlanStore.TryGetAllocation(alliance, out var allocation)", "the published allocation");
        AssertContains(army, "allocation.TryGet(((UnityEngine.Object)unit).GetInstanceID(), out assignment)", "the formation's own assignment");
        AssertContains(army, "theater = plan.SectorById(assignment.SectorId)?.Theater ?? Theater.Unknown;", "the sector's theater");
        AssertTrue(army.IndexOf("PerkSelectionScorer.TryDirectiveRole(", StringComparison.Ordinal) < army.IndexOf("PerkSelectionScorer.RoleOfFormation(", StringComparison.Ordinal), "the directive's role still comes first");
        AssertContains(src, "var personality = PersonalityVector.ForAlliance(alliance, StrategicCoordinator.Instance.Eras[alliance]);", "the alliance's standing disposition");
        string fleet = BodyOf(src, "private static FleetPerkRole ResolveFleetRole(");
        AssertContains(fleet, "plan.CapitalFloorStrength > 0f", "a standing capital floor is the capital threat");
    }

    private static void ArmyGroupPatchGroupsByAllocationSectorAndSeatsNoCommander()
    {
        string src = Code(ReadRepoFile("src/WhiskeyRealism/Patches/ArmyGroupManagementPatch.cs"));
        AssertContains(src, "if (!CampaignPlannerRuntime.ExecutorsEnabled()) return;", "the planner's executors write the grouping");
        AssertContains(src, "var plans = ArmyGroupDoctrine.PlanGroups(allocation);", "grouped by the allocation's sector");
        AssertContains(src, "CampaignPlanStore.TryGetAllocation(allianceId, out var allocation)", "the published allocation");
        AssertFalse(src.Contains("AppointCommander") || src.Contains("AppointPreferredCommander") || src.Contains("ResolveCommanderPreference"),
            "commanders are the appointment executor's: no second authority seats a name inside the protection window");
        AssertFalse(src.Contains("ArmyAreaRuntime") || src.Contains("ArmyAreas"), "no army-area ledger");
        AssertContains(src, "if (!plan.InstanceIds.Contains(((UnityEngine.Object)unit).GetInstanceID())) continue;", "members are matched by instance id");
    }

    private static void RecruitmentIntentReadsThePublishedPlan()
    {
        string src = Code(ReadRepoFile("src/WhiskeyRealism/Patches/RecruitmentPatch.cs"));
        string intent = BodyOf(src, "private static RecruitmentIntent BuildIntent(");
        AssertContains(intent, "CampaignPlanStore.TryGetPlan(alliance, out var plan) && PlanFreshness.IsFresh(plan.DaySerial)", "a fresh published plan");
        AssertContains(intent, "var main = plan.SectorById(plan.MainSectorId);", "the main-effort sector");
        AssertContains(intent, "intent.PreferredTheater = main.Theater;", "its theater");
        AssertFalse(src.Contains("CICs") || src.Contains("ObjectiveCatalog") || src.Contains("ActivePlan"), "no CIC or catalog read");
    }

    private static void SectorByIdFindsOnlyAPlannedSector()
    {
        var plan = new TheaterPlan();
        plan.Sectors.Add(new SectorPlan { SectorId = 4, Theater = Theater.West });
        plan.Sectors.Add(new SectorPlan { SectorId = 7, Theater = Theater.Coast });
        AssertEqual(Theater.Coast, plan.SectorById(7).Theater, "found by id, not by position");
        AssertTrue(plan.SectorById(5) == null, "an unplanned sector");
        AssertTrue(plan.SectorById(-1) == null, "-1 is none");
    }

    // The [CampaignPlan] row's main field and the monthly heartbeat's (replaces the heartbeat's cic= and plan= fields).
    private static void MainLabelNamesTheMainEffortsTheaterAndObjective()
    {
        var plan = new TheaterPlan { Alliance = 0, MainObjectiveId = 29 };
        plan.Sectors.Add(new SectorPlan { SectorId = 1, Posture = TheaterPosture.Hold, Theater = Theater.East });
        plan.Sectors.Add(new SectorPlan { SectorId = 2, Posture = TheaterPosture.MainEffort, Theater = Theater.West });
        AssertEqual("West/29", TheaterPlanner.MainLabel(plan), "the main-effort sector's theater and the objective");
        AssertEqual("none", TheaterPlanner.MainLabel(new TheaterPlan { Alliance = 0, MainObjectiveId = -1 }), "no main effort");
        AssertEqual("none", TheaterPlanner.MainLabel(null), "no plan");
        AssertEqual("Unknown/7", TheaterPlanner.MainLabel(new TheaterPlan { Alliance = 0, MainObjectiveId = 7 }), "an objective with no main-effort sector");
    }

    private static void HeartbeatReportsThePlannersMainEffort()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/StrategicCoordinator.cs");
        string report = BodyOf(src, "private void RunReviewJob(");
        AssertTrue(HasLine(report, "string main = CampaignPlanStore.TryGetPlan(alliance, out var published) ? TheaterPlanner.MainLabel(published) : \"none\";"), "the heartbeat's main effort is the published plan's");
        AssertTrue(report.Contains("$\"era={a.Era.Stage} main={main} \""), "era and main, in the heartbeat row");
        AssertFalse(report.Contains("cic=") || report.Contains("plan="), "no cic= or plan= field survives");
    }
}
