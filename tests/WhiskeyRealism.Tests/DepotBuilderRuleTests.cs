using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;

using static TestHarness;

// Stage G strategic foresight, slice F9 (D11 requirement-depot-builder): the investigation's Army of Virginia, unready and the Fredericksburg
// screen, was the smallest free formation of the depot site's sector, so it was sent to build. A depot builder now comes only from a
// formation the mass may use (TheaterPlanner.MassEligible: readiness step 2 and above), and on the first pass only from a sector surplus
// (the sector's standing hold less this formation still meets the sector's target); the second pass takes a member of the mass, never its last
// free body. A Marching task's builder keeps its pick through a readiness dip. Plan: docs/superpowers/plans/2026-10-18-strategic-foresight.md F9;
// cases TC-DEPOT-* in docs/loop/foresight/test-plan.md. The fixture world is ExecutorFixtures.Wide(): sectors {0,100,200}, {300,400}, {500,600},
// the depot site at x 300 in the middle one, the plan's minimum 2000 (the 002/A read).
internal static class DepotBuilderRuleTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("AQuietSectorsSurplusMayBuild", AQuietSectorsSurplusMayBuild),
            ("AnUnreadyFormationNeverBuilds", AnUnreadyFormationNeverBuilds),
            ("ABuilderLeavesItsSectorsHoldMet", ABuilderLeavesItsSectorsHoldMet),
            ("TheMassGivesABuilderOnlyWhenNoSurplusExists", TheMassGivesABuilderOnlyWhenNoSurplusExists),
            ("TheMassKeepsItsLastBody", TheMassKeepsItsLastBody),
            ("TheMassBuilderStandsInTheTasksSector", TheMassBuilderStandsInTheTasksSector),
            ("NoHoldReserveBuilds", NoHoldReserveBuilds),
            ("TheInvestigationsArmyOfVirginiaIsNotPicked", TheInvestigationsArmyOfVirginiaIsNotPicked),
            ("AMarchingBuilderKeepsItsTaskThroughAReadinessDip", AMarchingBuilderKeepsItsTaskThroughAReadinessDip),
            ("TheIncumbentIsAssignedAtThePlanSite", TheIncumbentIsAssignedAtThePlanSite),
            ("AReserveThatTookTheIncumbentWins", AReserveThatTookTheIncumbentWins),
            ("TheIncumbentSkipsTheSurplusTest", TheIncumbentSkipsTheSurplusTest),
            ("AnIncumbentTheTaskCannotUseIsNotClaimed", AnIncumbentTheTaskCannotUseIsNotClaimed),
            ("AWishedBuilderKeepsItsWishPosition", AWishedBuilderKeepsItsWishPosition),
        });
    }

    private const float Minimum = 2000f;
    private const float SiteX = 300f;

    private static int TaskSector(CampaignSnapshot s)
    {
        return ExecutorFixtures.Sectors(s).SectorAt(350f, 0f);
    }

    private static FormationRecord Formation(int id, float x, float strength, int readiness = 3)
    {
        var f = ExecutorFixtures.Corps(id, x, strength);
        f.ReadinessStep = readiness;
        return f;
    }

    // A hand-built plan with no main effort, no hold need anywhere and the live minimum, and one depot task at the site.
    private static TheaterPlan QuietPlan(CampaignSnapshot s, float taskZ = 0f, float taskX = SiteX)
    {
        var plan = new TheaterPlan { Alliance = s.Alliance, DaySerial = s.DaySerial, MinGarrisonStrength = Minimum };
        plan.DepotTasks.Add(new DepotTask { X = taskX, Z = taskZ, SectorId = TaskSector(s), Priority = 1f });
        return plan;
    }

    // The same with a main effort at objective 98 over the given mass.
    private static TheaterPlan MassPlan(CampaignSnapshot s, params int[] mass)
    {
        var plan = ExecutorFixtures.HandPlan(s, mass);
        plan.MinGarrisonStrength = Minimum;
        plan.DepotTasks.Add(new DepotTask { X = SiteX, Z = 0f, SectorId = TaskSector(s), Priority = 1f });
        return plan;
    }

    private static Allocation Run(TheaterPlan plan, CampaignSnapshot s, IReadOnlyList<HeldBuilder> held = null, IReadOnlyList<GaveUpMove> gaveUp = null,
                                  IReadOnlyList<ReconTask> recon = null)
    {
        return ForceAllocator.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, recon, null, gaveUp, held);
    }

    private static Assignment Of(Allocation a, int id)
    {
        AssertTrue(a.TryGet(id, out var x), "formation " + id + " has an assignment");
        return x;
    }

    private static string Builders(Allocation a)
    {
        var ids = new List<int>();
        foreach (var x in a.Assignments)
            if (x.Role == FormationRole.DepotBuilder) ids.Add(x.InstanceId);
        ids.Sort();
        return string.Join(",", ids);
    }

    private static HeldBuilder Marching(int id, float siteX, int sector)
    {
        return new HeldBuilder { InstanceId = id, SectorId = sector, Wished = false, SiteX = siteX, SiteZ = 0f, TargetX = siteX, TargetZ = 0f };
    }

    // ---- readiness and surplus ----

    // A sector with no hold need has a hold tally of 0 (ReserveRules.Hold reserves nothing there), so the surplus is measured on the
    // formations that stand in it: three together hold 10500, and the smallest leaves 7500, above the minimum of 2000.
    private static void AQuietSectorsSurplusMayBuild()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(Formation(10, 330f, 3000f));
        s.Own.Add(Formation(11, 350f, 3500f));
        s.Own.Add(Formation(12, 380f, 4000f));
        var a = Run(QuietPlan(s), s);
        AssertEqual("10", Builders(a), "the smallest formation of a sector that holds more than it needs");
        AssertEqual(ForceAllocator.ReasonDepotTask, Of(a, 10).Reason, "a new pick, not an incumbent");
        AssertEqual(FormationRole.Hold, Of(a, 11).Role, "the others keep holding");
        AssertEqual(FormationRole.Hold, Of(a, 12).Role, "the others keep holding");
    }

    private static void AnUnreadyFormationNeverBuilds()
    {
        foreach (int step in new[] { 0, 1 })
        {
            var s = ExecutorFixtures.Wide();
            s.Own.Add(Formation(10, 330f, 3000f, step));   // the smallest, but unready
            s.Own.Add(Formation(11, 350f, 3500f));
            s.Own.Add(Formation(12, 380f, 4000f));
            var a = Run(QuietPlan(s), s);
            AssertEqual("11", Builders(a), "readiness step " + step + ": the smallest READY formation builds, not the unready one");
            AssertEqual(FormationRole.Hold, Of(a, 10).Role, "the unready formation keeps holding (and keeps counting toward the sector's hold)");
        }
        var lone = ExecutorFixtures.Wide();
        lone.Own.Add(Formation(10, 330f, 3000f, 1));
        lone.Own.Add(Formation(11, 350f, 9000f, 1));
        AssertEqual("", Builders(Run(QuietPlan(lone), lone)), "a sector with only unready formations builds nothing: the task waits");
    }

    // 1500 + 1000 = 2500 held, the minimum is 2000: neither can leave. 1500 + 2500: the 1500 can, leaving 2500.
    private static void ABuilderLeavesItsSectorsHoldMet()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(Formation(10, 330f, 1500f));
        s.Own.Add(Formation(11, 350f, 1000f));
        AssertEqual("", Builders(Run(QuietPlan(s), s)), "either leaving would take the sector under its target");

        var t = ExecutorFixtures.Wide();
        t.Own.Add(Formation(10, 330f, 1500f));
        t.Own.Add(Formation(11, 350f, 2500f));
        AssertEqual("10", Builders(Run(QuietPlan(t), t)), "the smaller leaves 2500 behind, above the target");

    }

    // ---- the mass ----

    // N (2500) is the smaller formation but the sector's only non-mass one: leaving would take its hold to 0, so it is no surplus, and the
    // second pass takes the smallest mass member (6000), never N. With another non-mass formation beside N the surplus exists and N builds.
    private static void TheMassGivesABuilderOnlyWhenNoSurplusExists()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(Formation(20, 350f, 2500f));
        s.Own.Add(Formation(21, 330f, 6000f));
        s.Own.Add(Formation(22, 380f, 7000f));
        var a = Run(MassPlan(s, 21, 22), s);
        AssertEqual("21", Builders(a), "no surplus: the smallest member of the mass builds");
        AssertEqual(FormationRole.MainEffort, Of(a, 22).Role, "the rest of the mass stays in the main effort");
        AssertEqual(FormationRole.Hold, Of(a, 20).Role, "the non-mass formation that is no surplus is not taken on the second pass");

        var t = ExecutorFixtures.Wide();
        t.Own.Add(Formation(20, 350f, 2500f));
        t.Own.Add(Formation(21, 330f, 6000f));
        t.Own.Add(Formation(22, 380f, 7000f));
        t.Own.Add(Formation(23, 360f, 9000f));
        var b = Run(MassPlan(t, 21, 22), t);
        AssertEqual("20", Builders(b), "a surplus exists (11500 held, 9000 left): it builds, and the mass gives nothing");
        AssertEqual(FormationRole.MainEffort, Of(b, 21).Role, "the mass is whole");
        AssertEqual(FormationRole.MainEffort, Of(b, 22).Role, "the mass is whole");
    }

    private static void TheMassKeepsItsLastBody()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(Formation(20, 350f, 2500f));
        s.Own.Add(Formation(21, 330f, 6000f));
        var a = Run(MassPlan(s, 21), s);
        AssertEqual("", Builders(a), "the mass's one free body is not given away");
        AssertEqual(FormationRole.MainEffort, Of(a, 21).Role, "it stays in the main effort");

        // The rule counts the mass's free bodies, not those of the task's sector: a second member elsewhere lets this one go.
        var t = ExecutorFixtures.Wide();
        t.Own.Add(Formation(20, 350f, 2500f));
        t.Own.Add(Formation(21, 330f, 6000f));
        t.Own.Add(Formation(24, 200f, 8000f));
        var b = Run(MassPlan(t, 21, 24), t);
        AssertEqual("21", Builders(b), "with another free body in the mass the sector's member may build");
        AssertEqual(FormationRole.MainEffort, Of(b, 24).Role, "the other member is the main effort");
    }

    private static void TheMassBuilderStandsInTheTasksSector()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(Formation(21, 330f, 6000f));
        s.Own.Add(Formation(24, 200f, 2000f));   // smaller, but in another sector
        var a = Run(MassPlan(s, 21, 24), s);
        AssertEqual("21", Builders(a), "the member that stands in the task's sector, though another is smaller");
        AssertEqual(FormationRole.MainEffort, Of(a, 24).Role, "a mass member elsewhere is never a builder");
    }

    // ---- holds ----

    // A formation the hold reserve claimed (the sector faces a believed army) is not a builder; the free ones beside it are, as before.
    // A regression pin: the hold reserve is claimed before the depot step, so the shipped allocator already agrees.
    private static void NoHoldReserveBuilds()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(Formation(20, 330f, 3000f));
        s.Own.Add(Formation(21, 350f, 3000f));
        s.Own.Add(Formation(22, 380f, 3500f));
        s.Own.Add(Formation(23, 390f, 9000f));
        s.Beliefs.Add(ExecutorFixtures.Belief(430f, 0f, 4000f));
        var plan = QuietPlan(s);
        plan.Sectors.Add(new SectorPlan { SectorId = TaskSector(s), Posture = TheaterPosture.Hold, HoldNeed = 5000f });
        var a = Run(plan, s);
        AssertEqual(FormationRole.Hold, Of(a, 23).Role, "the formation nearest the believed army is the hold reserve, not a builder");
        AssertEqual(ForceAllocator.ReasonHoldReserve, Of(a, 23).Reason, "kept as the sector's hold reserve");
        AssertEqual("20", Builders(a), "a free formation beside the reserve builds");
    }

    private static void TheInvestigationsArmyOfVirginiaIsNotPicked()
    {
        var s = ExecutorFixtures.Wide();
        var aov = Formation(30, 350f, 4000f, 1);   // the Army of Virginia: the smallest, readiness step 1, the screen at Fredericksburg
        aov.Name = "Army of Virginia";
        s.Own.Add(aov);
        s.Own.Add(Formation(31, 330f, 5000f));
        s.Own.Add(Formation(32, 380f, 6000f));
        var a = Run(QuietPlan(s), s);
        AssertEqual(FormationRole.Hold, Of(a, 30).Role, "the unready army keeps its screen");
        AssertEqual("31", Builders(a), "the smallest ready formation of the sector builds");
    }

    // ---- the incumbent ----

    private static void AMarchingBuilderKeepsItsTaskThroughAReadinessDip()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(Formation(40, 350f, 3000f, 1));   // its readiness dipped on the march
        s.Own.Add(Formation(41, 330f, 5000f));      // a ready alternative with a surplus behind it
        var held = new[] { Marching(40, SiteX, TaskSector(s)) };
        var a = Run(QuietPlan(s), s, held);
        AssertEqual("40", Builders(a), "the task keeps its builder: a dip must not re-bind it");
        AssertEqual(ForceAllocator.ReasonPinnedBuilder, Of(a, 40).Reason, "the reason of a kept pick");
        AssertEqual("pinned", ForceAllocator.PickOf(Of(a, 40)), "so the mover's row reads pick=pinned");

        var control = Run(QuietPlan(s), s);
        AssertEqual("41", Builders(control), "control: with no task bound to it the unready formation is not picked");
    }

    private static void TheIncumbentIsAssignedAtThePlanSite()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(Formation(40, 350f, 3000f));
        var plan = QuietPlan(s, taskZ: 6f, taskX: 310f);   // the plan's site moved with the frontier since the task was made
        var a = Run(plan, s, new[] { Marching(40, SiteX, TaskSector(s)) });   // the held site is 11.7 away, within the ledger's match radius
        var x = Of(a, 40);
        AssertEqual(FormationRole.DepotBuilder, x.Role, "still the builder");
        AssertEqual(310f, x.TargetX, "assigned at the PLAN task's x: the ledger abandons a task whose builder is not at the new site");
        AssertEqual(6f, x.TargetZ, "and z");
        AssertTrue(x.Withheld, "withheld like any builder");

        var far = Run(QuietPlan(s, taskX: 310f), s, new[] { Marching(40, SiteX + 100f, TaskSector(s)) });   // 90 from the plan task: another site
        AssertEqual("", Builders(far), "a held site beyond the match radius of the plan task is not this task's incumbent (and the lone formation is no surplus)");
    }

    // The hold reserve claimed the incumbent first (the sector faces a believed army and it stands nearest it): it keeps that role, and the
    // task is served by another formation as a new pick.
    private static void AReserveThatTookTheIncumbentWins()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(Formation(20, 330f, 3000f));
        s.Own.Add(Formation(21, 350f, 3000f));
        s.Own.Add(Formation(22, 380f, 3500f));
        s.Own.Add(Formation(23, 390f, 9000f));
        s.Beliefs.Add(ExecutorFixtures.Belief(430f, 0f, 4000f));
        var plan = QuietPlan(s);
        plan.Sectors.Add(new SectorPlan { SectorId = TaskSector(s), Posture = TheaterPosture.Hold, HoldNeed = 5000f });
        var a = Run(plan, s, new[] { Marching(23, SiteX, TaskSector(s)) });
        AssertEqual(FormationRole.Hold, Of(a, 23).Role, "the reserve keeps it");
        AssertEqual(ForceAllocator.ReasonHoldReserve, Of(a, 23).Reason, "as the hold reserve");
        AssertEqual("20", Builders(a), "another formation is the new pick");
        AssertEqual("new", ForceAllocator.PickOf(Of(a, 20)), "a new pick, not a kept one");
    }

    // A sector with one formation has no surplus (3000 held, none left), yet the task's own builder stays: it was picked when it was.
    private static void TheIncumbentSkipsTheSurplusTest()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(Formation(40, 350f, 3000f));
        var plan = QuietPlan(s);
        AssertEqual("", Builders(Run(plan, s)), "control: a new pick needs a surplus");
        var a = Run(plan, s, new[] { Marching(40, SiteX, TaskSector(s)) });
        AssertEqual("40", Builders(a), "the incumbent is exempt from the surplus test");
        AssertEqual(ForceAllocator.ReasonPinnedBuilder, Of(a, 40).Reason, "pinned");
    }

    // The claim is for a formation the planner may still send there: one that is busy, one the tracker gave up on at the site, and one that no
    // longer stands in the task's sector are not claimed, and the task is served by the next candidate.
    private static void AnIncumbentTheTaskCannotUseIsNotClaimed()
    {
        int sector;

        var busy = ExecutorFixtures.Wide();
        sector = TaskSector(busy);
        var inBattle = Formation(40, 350f, 3000f);
        inBattle.Flags |= FormationFlags.InBattle;
        busy.Own.Add(inBattle);
        busy.Own.Add(Formation(41, 330f, 5000f));
        busy.Own.Add(Formation(42, 380f, 6000f));
        var a = Run(QuietPlan(busy), busy, new[] { Marching(40, SiteX, sector) });
        AssertEqual(ForceAllocator.ReasonBusy, Of(a, 40).Reason, "a busy incumbent stays busy");
        AssertEqual("41", Builders(a), "and the task is served by the next candidate");

        var gave = ExecutorFixtures.Wide();
        gave.Own.Add(Formation(40, 350f, 3000f));
        gave.Own.Add(Formation(41, 330f, 5000f));
        gave.Own.Add(Formation(42, 380f, 6000f));
        var given = new[] { new GaveUpMove { InstanceId = 40, TargetX = SiteX, TargetZ = 0f, ObjectiveId = -1 } };
        var b = Run(QuietPlan(gave), gave, new[] { Marching(40, SiteX, sector) }, given);
        AssertEqual(FormationRole.Hold, Of(b, 40).Role, "the tracker gave up on it at the site: it is not claimed and holds");
        AssertEqual("41", Builders(b), "the next candidate builds");

        var away = ExecutorFixtures.Wide();
        away.Own.Add(Formation(40, 150f, 3000f));   // it has since stood in the west sector
        away.Own.Add(Formation(41, 330f, 5000f));
        away.Own.Add(Formation(42, 380f, 6000f));
        var c = Run(QuietPlan(away), away, new[] { Marching(40, SiteX, sector) });
        AssertEqual(FormationRole.Hold, Of(c, 40).Role, "an incumbent outside the task's sector is not claimed");
        AssertEqual("41", Builders(c), "the sector's own formation builds");
    }

    // A Wished builder is the pinned-builder step's (AssignPinnedBuilders, at the wish position), not the incumbent claim's.
    private static void AWishedBuilderKeepsItsWishPosition()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(Formation(40, 350f, 3000f, 1));
        var wished = new HeldBuilder { InstanceId = 40, SectorId = TaskSector(s), Wished = true, SiteX = SiteX, SiteZ = 0f, TargetX = 345f, TargetZ = 2f };
        var a = Run(QuietPlan(s), s, new[] { wished });
        var x = Of(a, 40);
        AssertEqual(FormationRole.DepotBuilder, x.Role, "a Wished builder stays");
        AssertEqual(345f, x.TargetX, "at the wish position, not the plan task");
        AssertEqual(2f, x.TargetZ, "z");
    }
}
