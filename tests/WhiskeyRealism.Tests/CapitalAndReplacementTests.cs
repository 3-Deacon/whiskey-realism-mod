using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Util;

using static TestHarness;
using static TestFixtures;

// Stage G slice G5b (docs/superpowers/plans/2026-10-15-campaign-planner.md § G5b): the capital floor's
// vanilla-need pledge, the replacement flow (recruit routing, the transfer swap, the main-target steer),
// the W&L career transfer fix and the runtime pins of the patches that apply them. The allocation and
// plan fixtures are the real G3/G4 ones where the shape matters (the shortfall amounts must agree with
// the allocator's carriers) and hand-built where a rule is isolated.
internal static class CapitalAndReplacementTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("VanillaNeedMatchesFormulaFixture", VanillaNeedMatchesFormulaFixture),
            ("VanillaNeedReadsWaypointsAndLiveBeliefsOnly", VanillaNeedReadsWaypointsAndLiveBeliefsOnly),
            ("VanillaNeedCountsEveryChainClassAtItsWaypoint", VanillaNeedCountsEveryChainClassAtItsWaypoint),
            ("PledgeOnlyWhileVanillaNeedPositive", PledgeOnlyWhileVanillaNeedPositive),
            ("PledgeNeverPlayerOrCustomOrdered", PledgeNeverPlayerOrCustomOrdered),
            ("PledgeKeepsChainGuard", PledgeKeepsChainGuard),
            ("PledgeIsOnlyAnAllocatedCapitalGuard", PledgeIsOnlyAnAllocatedCapitalGuard),
            ("ExcessFloorStaysCapitalGuard", ExcessFloorStaysCapitalGuard),
            ("SteerOnlyWhenPlanShortfallWorse", SteerOnlyWhenPlanShortfallWorse),
            ("NoSteerWithoutMainEffort", NoSteerWithoutMainEffort),
            ("PlanShortfallCountsCoverageByHoldPoint", PlanShortfallCountsCoverageByHoldPoint),
            ("SteerReadsNoVanillaEnemyTerm", SteerReadsNoVanillaEnemyTerm),
            ("TransferUsesBeliefsNotHeatmapEnemy", TransferUsesBeliefsNotHeatmapEnemy),
            ("CareerFixNeverForPlayerUnits", CareerFixNeverForPlayerUnits),
            ("RecruitRoutedToNearestShortFormation", RecruitRoutedToNearestShortFormation),
            ("RecruitNotRoutedBeyondRange", RecruitNotRoutedBeyondRange),
            ("RecruitRoutingNeverToPlayerFormation", RecruitRoutingNeverToPlayerFormation),
            ("RecruitRoutingOnlyInsideZoneRecruiting", RecruitRoutingOnlyInsideZoneRecruiting),
            ("TransferDeficitIsShortFormationSurplusIsReserveExcess", TransferDeficitIsShortFormationSurplusIsReserveExcess),
            ("TransferSurplusNeverFromMainEffort", TransferSurplusNeverFromMainEffort),
            ("TransferNeedsAPositiveShortfallAndASource", TransferNeedsAPositiveShortfallAndASource),
            ("TransferSourceKeepsVanillasDonorPickOffTheFront", TransferSourceKeepsVanillasDonorPickOffTheFront),
            ("ReinforcementRoutingNeverTargetsAPlayerFormation", ReinforcementRoutingNeverTargetsAPlayerFormation),
            ("ShortfallStrengthIsTheCarriersNeedByRole", ShortfallStrengthIsTheCarriersNeedByRole),
            ("ShortfallAmountsAgreeWithTheAllocatorsCarriers", ShortfallAmountsAgreeWithTheAllocatorsCarriers),
            ("PatchesWireTheLivePlanAndBothFlags", PatchesWireTheLivePlanAndBothFlags),
            ("LiveReadsBehindThePatchesArePinned", LiveReadsBehindThePatchesArePinned),
            ("TransferFinalizerRestoresBothRecords", TransferFinalizerRestoresBothRecords),
            ("CareerFixIsArgumentOnlyInsideTheTransferScope", CareerFixIsArgumentOnlyInsideTheTransferScope),
            ("PolicyFilesArePureAndFogHonestBySource", PolicyFilesArePureAndFogHonestBySource),
            ("SupersededHelpersAreGone", SupersededHelpersAreGone),
        });
    }

    // vanilla's donor-pick radius (GamePrefs.aimaximumdistancetosearchforunitrelocations, 12824-12846), as the fixtures use it
    private const float DonorRadius = 150f;

    private static readonly int Day = ReviewCadenceCatchUp.DaySerial(15, 6, 1861);

    // ---- vanilla's capital need on beliefs ----

    // 11712-11741: num2 = minimum + Σ min(str, str x morale) / max(10, d)^0.3 x 0.5 over enemies within the
    // relocation range - Σ str x morale of every own group whose last waypoint is nearer than the nearest enemy.
    private static void VanillaNeedMatchesFormulaFixture()
    {
        var beliefs = new List<EnemyBelief> { Belief(100f, 0f, 10000f), Belief(700f, 0f, 5000f) };
        var own = new List<FormationRecord> { Active(1, 50f, 2000f, 0.5f), Active(2, 150f, 9000f, 1f) };
        float need = CapitalFloorPolicy.VanillaNeed(500f, beliefs, 0.8f, 0f, 0f, 650f, own);
        // closest enemy 100; only the first is inside 650; pressure = min(10000, 8000) / 100^0.3 x 0.5
        float pressure = 8000f / (float)Math.Pow(100f, 0.3f) * 0.5f;
        // own: waypoint 50 < 100 subtracts 2000 x 0.5; waypoint 150 does not
        float expected = 500f + pressure - 1000f;
        AssertNear(expected, need, 0.01f, "vanilla num2 on beliefs");

        float quiet = CapitalFloorPolicy.VanillaNeed(500f, new List<EnemyBelief>(), 1f, 0f, 0f, 650f, own);
        AssertNear(500f - 1000f - 9000f, quiet, 0.01f, "no enemy believed: the closest enemy stays 9999, every own waypoint counts");
        AssertNear(500f, CapitalFloorPolicy.VanillaNeed(500f, null, 1f, 0f, 0f, 650f, null), 0.001f, "no beliefs and no groups: the movement minimum");
    }

    private static void VanillaNeedReadsWaypointsAndLiveBeliefsOnly()
    {
        // The group stands far from the capital but its last waypoint is at the gate: vanilla counts the waypoint.
        var marching = Active(1, 900f, 6000f, 1f);
        marching.WaypointX = 20f;
        var beliefs = new List<EnemyBelief> { Belief(100f, 0f, 10000f) };
        float withWaypoint = CapitalFloorPolicy.VanillaNeed(500f, beliefs, 1f, 0f, 0f, 650f, new List<FormationRecord> { marching });
        float atPosition = CapitalFloorPolicy.VanillaNeed(500f, beliefs, 1f, 0f, 0f, 650f, new List<FormationRecord> { Active(1, 900f, 6000f, 1f) });
        AssertTrue(withWaypoint < atPosition - 5999f, "the waypoint, not the position, decides: " + withWaypoint + " vs " + atPosition);

        // A waypoint that is not a point falls back to the position.
        var nan = Active(2, 20f, 6000f, 1f);
        nan.WaypointX = float.NaN;
        AssertNear(withWaypoint, CapitalFloorPolicy.VanillaNeed(500f, beliefs, 1f, 0f, 0f, 650f, new List<FormationRecord> { nan }), 0.01f,
            "a NaN waypoint reads the position");

        // A belief weighs strength x confidence; an expired one counts nowhere, not even as the closest enemy.
        var half = Belief(100f, 0f, 10000f);
        half.Confidence01 = 0.5f;
        var expired = Belief(5f, 0f, 90000f);
        expired.Confidence01 = 0f;
        float full = CapitalFloorPolicy.VanillaNeed(500f, new List<EnemyBelief> { Belief(100f, 0f, 10000f) }, 1f, 0f, 0f, 650f, null);
        float halved = CapitalFloorPolicy.VanillaNeed(500f, new List<EnemyBelief> { half, expired }, 1f, 0f, 0f, 650f, null);
        AssertNear(500f + (full - 500f) / 2f, halved, 0.01f, "confidence weighs the pressure and the expired belief is ignored");
    }

    // 11733-11740: vanilla subtracts str x morale of EVERY own group whose last waypoint is nearer the capital than
    // the nearest enemy, with no player exclusion. The need (and so #4's pledge) must see the map-group player's
    // group, the player's own command and the carrying group alike; only the pick is limited by MayMove.
    private static void VanillaNeedCountsEveryChainClassAtItsWaypoint()
    {
        var beliefs = new List<EnemyBelief> { Belief(100f, 0f, 10000f) };
        float baseline = CapitalFloorPolicy.VanillaNeed(500f, beliefs, 1f, 0f, 0f, 650f, new List<FormationRecord>());
        foreach (var chain in new[] { WlChainClass.PlayerMapGroup, WlChainClass.PlayerCommand, WlChainClass.CarryingGroup })
        {
            var group = WithChain(Active(1, 900f, 4000f, 0.5f), chain); // stands far away, last waypoint inside the 100 ring
            group.WaypointX = 20f;
            float need = CapitalFloorPolicy.VanillaNeed(500f, beliefs, 1f, 0f, 0f, 650f, new List<FormationRecord> { group });
            AssertNear(baseline - 2000f, need, 0.01f, chain + ": strength x morale is subtracted at its waypoint");
            var outside = group;
            outside.WaypointX = 300f; // nearer than no enemy... but beyond the nearest enemy (100): not subtracted
            AssertNear(baseline, CapitalFloorPolicy.VanillaNeed(500f, beliefs, 1f, 0f, 0f, 650f, new List<FormationRecord> { outside }), 0.01f,
                chain + ": a waypoint beyond the closest enemy does not count");
        }

        // A map-group player standing at the capital covers it: the AI guard is not pledged through VanillaNeed.
        var player = WithChain(Active(2, 5f, 20000f, 1f), WlChainClass.PlayerMapGroup);
        var guards = Guards(7);
        var pool = new[] { Pledgeable(7, 10f) };
        float uncovered = CapitalFloorPolicy.VanillaNeed(500f, beliefs, 1f, 0f, 0f, 650f, new List<FormationRecord> { Active(3, 900f, 100f, 1f) });
        float covered = CapitalFloorPolicy.VanillaNeed(500f, beliefs, 1f, 0f, 0f, 650f, new List<FormationRecord> { player });
        AssertEqual(7, CapitalFloorPolicy.PickPledge(guards, uncovered, pool), "short without him: the guard is pledged");
        AssertEqual(-1, CapitalFloorPolicy.PickPledge(guards, covered, pool), "he covers the capital: nothing is pledged");
    }

    // ---- the pledge ----

    private static void PledgeOnlyWhileVanillaNeedPositive()
    {
        var a = Guards(1);
        var pool = new[] { Pledgeable(1, 10f) };
        AssertEqual(1, CapitalFloorPolicy.PickPledge(a, 1f, pool), "vanilla needs defenders: pledge");
        AssertEqual(-1, CapitalFloorPolicy.PickPledge(a, 0f, pool), "need exactly 0: vanilla would strip the pledge next call");
        AssertEqual(-1, CapitalFloorPolicy.PickPledge(a, -50f, pool), "vanilla sufficient: nothing to pledge");
        AssertEqual(-1, CapitalFloorPolicy.PickPledge(a, float.NaN, pool), "an unreadable need pledges nothing");
        AssertEqual(-1, CapitalFloorPolicy.PickPledge(null, 100f, pool), "no allocation, no pledge");
    }

    private static void PledgeNeverPlayerOrCustomOrdered()
    {
        var a = Guards(1, 2, 3, 4, 5, 6);
        var custom = Pledgeable(1, 10f);
        custom.CustomOrdered = true;
        var mapGroup = Pledgeable(2, 20f);
        mapGroup.Chain = WlChainClass.PlayerMapGroup;
        var playerCommand = Pledgeable(3, 30f);
        playerCommand.Chain = WlChainClass.PlayerCommand;
        var cicSide = Pledgeable(4, 40f);
        cicSide.Chain = WlChainClass.CicSide;
        var vanillaRefuses = Pledgeable(5, 50f); // IsMovedByPlayer, in battle, retreating... (11791-11817)
        vanillaRefuses.PassesVanillaFilters = false;
        var pool = new List<PledgeCandidate> { custom, mapGroup, playerCommand, cicSide, vanillaRefuses, Pledgeable(6, 600f) };
        AssertEqual(6, CapitalFloorPolicy.PickPledge(a, 100f, pool), "the only admissible guard, however far");
        pool.RemoveAt(pool.Count - 1);
        AssertEqual(-1, CapitalFloorPolicy.PickPledge(a, 100f, pool), "the player's, a custom-ordered and a refused unit are never pledged");
    }

    private static void PledgeKeepsChainGuard()
    {
        var a = Guards(1, 2);
        var carrying = Pledgeable(1, 10f);
        carrying.Chain = WlChainClass.CarryingGroup; // the AI corps carrying a below-map-level player: a movable AI asset
        var mapGroup = Pledgeable(2, 5f);
        mapGroup.Chain = WlChainClass.PlayerMapGroup;
        AssertEqual(1, CapitalFloorPolicy.PickPledge(a, 100f, new[] { mapGroup, carrying }), "the carrying group is pledged, the nearer map-group player is not");
        AssertEqual(-1, CapitalFloorPolicy.PickPledge(a, 100f, new[] { mapGroup }), "the map-group player alone is never pledged");

        string patch = ReadRepoFile("src/WhiskeyRealism/Patches/DefensiveOpsPatch.cs");
        AssertTrue(HasLine(patch, "Chain = WlStrategicOrderBridge.ChainClassOf(in chain, allianceId, unit),"), "#4 hands every candidate's chain class to the pick");
        AssertFalse(patch.Contains("CountsTowardCoverage"), "#4 never asks the coverage question");
        string policy = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CapitalFloorPolicy.cs");
        AssertTrue(HasLine(policy, "if (!c.PassesVanillaFilters || c.CustomOrdered || !WlChainRules.MayMove(c.Chain)) continue;"), "the pick asks may-commit");
    }

    private static void PledgeIsOnlyAnAllocatedCapitalGuard()
    {
        var a = new Allocation();
        a.Assignments.Add(new Assignment { InstanceId = 1, Role = FormationRole.Reserve });
        a.Assignments.Add(new Assignment { InstanceId = 2, Role = FormationRole.MainEffort });
        a.Assignments.Add(new Assignment { InstanceId = 3, Role = FormationRole.CapitalGuard });
        var pool = new[] { Pledgeable(1, 1f), Pledgeable(2, 2f), Pledgeable(4, 3f), Pledgeable(3, 900f) };
        AssertEqual(3, CapitalFloorPolicy.PickPledge(a, 100f, pool), "only a CapitalGuard of the allocation, not the nearer Reserve, MainEffort or unallocated unit");
    }

    // The floor above vanilla's own need is the planner's: the allocator holds it as CapitalGuard (withheld, held
    // by the mover), and the pledge takes one guard at a time only while vanilla itself needs defenders.
    private static void ExcessFloorStaysCapitalGuard()
    {
        var s = Line(0, 5);
        s.Objectives.Add(Target(99, 300f, 1f));
        s.Own.Add(Corps(1, 200f, 20000f));
        s.Own.Add(Corps(2, 100f, 15000f));
        s.Own.Add(Corps(3, 50f, 3000f));
        s.Beliefs.Add(Belief(20f, 0f, 40000f));
        var plan = PlanOf(s);
        var a = Alloc(plan, s);
        AssertEqual(FormationRole.CapitalGuard, Of(a, 2).Role, "the planner holds two guards for the floor");
        AssertEqual(FormationRole.CapitalGuard, Of(a, 3).Role, "...");

        var pool = new List<PledgeCandidate>();
        foreach (var f in s.Own) pool.Add(new PledgeCandidate { InstanceId = f.InstanceId, DistanceToCapital = f.X, Chain = f.Chain, PassesVanillaFilters = true });
        AssertEqual(3, CapitalFloorPolicy.PickPledge(a, 10f, pool), "vanilla short: the nearest guard, one unit");
        AssertEqual(-1, CapitalFloorPolicy.PickPledge(a, -10f, pool), "vanilla's own need met: the excess floor is not pledged");
        AssertEqual(FormationRole.CapitalGuard, Of(a, 2).Role, "choosing a pledge does not edit the allocation");
        AssertTrue(Of(a, 2).Withheld && Of(a, 3).Withheld, "the guards stay withheld from vanilla's offensive pool");
    }

    // ---- the main-target steer ----

    private static void SteerOnlyWhenPlanShortfallWorse()
    {
        var plan = MainPlan(Wide(), 100f);
        AssertTrue(ReplacementFlowPolicy.ShouldSteer(plan, 20000f, 5000f, out float x, out float z), "an army believed at the target and a brigade-sized shortfall: steer");
        AssertNear(100f, x, 0.001f, "the gather point (x)");
        AssertNear(0f, z, 0.001f, "the gather point (z)");
        AssertFalse(ReplacementFlowPolicy.ShouldSteer(plan, 20000f, ReplacementFlowPolicy.MinimumShortfall - 1f, out _, out _), "under a brigade's worth: nothing to steer");
        AssertTrue(ReplacementFlowPolicy.ShouldSteer(plan, 20000f, ReplacementFlowPolicy.MinimumShortfall, out _, out _), "the floor itself steers");
        AssertFalse(ReplacementFlowPolicy.ShouldSteer(plan, 20000f, 0f, out _, out _), "no shortfall");
        AssertFalse(ReplacementFlowPolicy.ShouldSteer(plan, 20000f, -8000f, out _, out _), "the main target is over-supplied");
        AssertFalse(ReplacementFlowPolicy.ShouldSteer(plan, 0f, 9000f, out _, out _), "no army believed there: nothing is known to counter");
        AssertFalse(ReplacementFlowPolicy.ShouldSteer(plan, float.NaN, 9000f, out _, out _), "an unreadable belief steers nothing");
    }

    private static void NoSteerWithoutMainEffort()
    {
        var none = new TheaterPlan { Alliance = 0, DaySerial = Day };
        none.Sectors.Add(new SectorPlan { SectorId = 0, Posture = TheaterPosture.Hold, TargetX = 100f, TargetZ = 0f });
        AssertFalse(ReplacementFlowPolicy.ShouldSteer(none, 20000f, 9000f, out _, out _), "a plan with no main effort steers nothing");
        AssertFalse(ReplacementFlowPolicy.ShouldSteer(null, 20000f, 9000f, out _, out _), "no plan steers nothing");
        var dangling = new TheaterPlan { Alliance = 0, DaySerial = Day, MainSectorId = 7 };
        dangling.Sectors.Add(new SectorPlan { SectorId = 0, Posture = TheaterPosture.Hold });
        AssertFalse(ReplacementFlowPolicy.ShouldSteer(dangling, 20000f, 9000f, out _, out _), "a main sector with no sector entry has no gather point");
        AssertFalse(ReplacementFlowPolicy.TryMainTarget(dangling, out _, out _), "TryMainTarget agrees");
    }

    private static void PlanShortfallCountsCoverageByHoldPoint()
    {
        var plan = MainPlan(Wide(), 100f);
        plan.MainRequiredRatio = 1.5f;
        var near = Corps(1, 110f, 10000f);
        var far = Corps(2, 500f, 50000f);
        var command = Corps(3, 100f, 50000f);
        command.Chain = WlChainClass.PlayerCommand; // inside his group's count, never counted twice
        var own = new List<FormationRecord> { near, far, command };
        AssertNear(1.5f * 20000f - 10000f, ReplacementFlowPolicy.PlanShortfall(plan, 20000f, own, 100f, 0f, 150f), 0.01f, "required less what stands within the radius");

        // The map-group player counts where his last waypoint lies, at strength x morale.
        var group = Corps(4, 600f, 8000f);
        group.Chain = WlChainClass.PlayerMapGroup;
        group.Morale = 0.5f;
        group.WaypointX = 120f;
        own.Add(group);
        AssertNear(1.5f * 20000f - 10000f - 4000f, ReplacementFlowPolicy.PlanShortfall(plan, 20000f, own, 100f, 0f, 150f), 0.01f, "his group at its waypoint, strength x morale");
        AssertNear(0f, ReplacementFlowPolicy.PlanShortfall(plan, 0f, own, 100f, 0f, 150f), 0.001f, "no believed enemy, no requirement");
        AssertNear(0f, ReplacementFlowPolicy.PlanShortfall(null, 20000f, own, 100f, 0f, 150f), 0.001f, "no plan");
    }

    private static void SteerReadsNoVanillaEnemyTerm()
    {
        string transfer = ReadRepoFile("src/WhiskeyRealism/Patches/TransferOfUnitsPatch.cs");
        foreach (string banned in new[] { "enemystrength", "recruitmentheatmapenemy", "recruitmentheatmapown", "Heatmap", "ObjectiveAdapter", "ActivePlan", "CICs" })
            AssertFalse(transfer.Contains(banned), "TransferOfUnitsPatch.cs mentions " + banned);
        AssertTrue(transfer.Contains("EnemyDetectionRuntime.BeliefsFor(allianceId)"), "the main-target steer reads the side's beliefs");
    }

    private static void TransferUsesBeliefsNotHeatmapEnemy()
    {
        foreach (string file in new[]
        {
            "src/WhiskeyRealism/Strategic/Planner/ReinforcementRouting.cs",
            "src/WhiskeyRealism/Strategic/Planner/ReplacementFlowPolicy.cs",
            "src/WhiskeyRealism/Patches/RecruitRoutingPatch.cs",
        })
        {
            string source = ReadRepoFile(file);
            foreach (string banned in new[] { "enemystrength", "Heatmap", "enemyunits", "completeunitlist", "GetClosestEnemyUnitReg" })
                AssertFalse(source.Contains(banned), file + " mentions " + banned);
        }
        // The amounts a transfer moves come from the plan's published numbers (built on beliefs), not a grid.
        var plan = new TheaterPlan { Alliance = 0, DaySerial = Day, MainSectorId = 1, MainRequiredRatio = 2f, MainEnemyStrength = 10000f };
        var a = new Allocation();
        a.Assignments.Add(new Assignment { InstanceId = 1, Role = FormationRole.MainEffort, SectorId = 1 });
        var own = new List<FormationRecord> { Corps(1, 100f, 12000f) };
        AssertNear(8000f, ReinforcementRouting.ShortfallStrength(plan, a, own, 1), 0.01f, "2 x 10000 believed less 12000 held");
        plan.MainEnemyStrength = 0f;
        AssertNear(0f, ReinforcementRouting.ShortfallStrength(plan, a, own, 1), 0.001f, "no believed enemy: no need");
    }

    // ---- the W&L career transfer fix ----

    private static void CareerFixNeverForPlayerUnits()
    {
        AssertTrue(WlTransferFixDecision.Allow(true, true, false, false), "two AI units inside CheckTransferOfUnits in a career: allowed");
        AssertFalse(WlTransferFixDecision.Allow(false, true, false, false), "outside the transfer scope: vanilla's guard stands");
        AssertFalse(WlTransferFixDecision.Allow(true, false, false, false), "outside a career vanilla's guard is already off");
        AssertFalse(WlTransferFixDecision.Allow(true, true, true, false), "the moved unit is the player's: never re-parented");
        AssertFalse(WlTransferFixDecision.Allow(true, true, false, true), "the destination is the player's: never");
        AssertFalse(WlTransferFixDecision.Allow(true, true, true, true), "two player units: vanilla allows it, nothing to fix");
    }

    // ---- recruit routing ----

    private static void RecruitRoutedToNearestShortFormation()
    {
        var own = new List<FormationRecord> { Corps(1, 110f, 5000f), Corps(2, 300f, 5000f), Corps(3, 260f, 40000f) };
        var a = new Allocation();
        a.Shortfalls.Add(1);
        a.Shortfalls.Add(2);
        // cell at 250 with range 150: limit 225; formation 2 (300) is 50 away, formation 1 (110) 140 away
        AssertTrue(ReinforcementRouting.TryRouteRecruit(a, own, 150f, 250f, 0f, out float x, out float z, out int to), "a short formation in reach");
        AssertEqual(2, to, "the nearest shortfall formation");
        AssertNear(300f, x, 0.001f, "routed to its position (x)");
        AssertNear(0f, z, 0.001f, "routed to its position (z)");
        a.Shortfalls.Insert(0, 3);
        AssertTrue(ReinforcementRouting.TryRouteRecruit(a, own, 150f, 250f, 0f, out _, out _, out to), "...");
        AssertEqual(3, to, "a nearer shortfall formation takes the recruit");
        AssertFalse(ReinforcementRouting.TryRouteRecruit(new Allocation(), own, 150f, 250f, 0f, out _, out _, out _), "no shortfall, no routing");
        AssertFalse(ReinforcementRouting.TryRouteRecruit(a, own, 0f, 250f, 0f, out _, out _, out _), "no relocation range, no routing");
    }

    private static void RecruitNotRoutedBeyondRange()
    {
        var own = new List<FormationRecord> { Corps(1, 110f, 5000f), Corps(2, 300f, 5000f) };
        var a = new Allocation();
        a.Shortfalls.Add(1);
        a.Shortfalls.Add(2);
        AssertNear(1.5f, ReinforcementRouting.MaxRouteFactor, 0.0001f, "plan: 1.5 x the relocation range from the recruiting cell");
        float limit = ReinforcementRouting.MaxRouteFactor * 150f; // 225
        AssertTrue(ReinforcementRouting.TryRouteRecruit(a, own, 150f, 300f + limit, 0f, out _, out _, out int to), "exactly at the limit still routes");
        AssertEqual(2, to, "...to formation 2");
        AssertFalse(ReinforcementRouting.TryRouteRecruit(a, own, 150f, 300f + limit + 1f, 0f, out _, out _, out _), "one unit beyond MaxRouteFactor x range: vanilla's own cell stands");
        AssertFalse(ReinforcementRouting.TryRouteRecruit(a, new List<FormationRecord>(), 150f, 300f, 0f, out _, out _, out _), "a shortfall id with no live formation routes nowhere");
    }

    private static void RecruitRoutingNeverToPlayerFormation()
    {
        var player = Corps(5, 550f, 3000f);
        player.Chain = WlChainClass.PlayerMapGroup;
        player.WaypointX = 480f;
        var own = new List<FormationRecord> { player };
        var a = new Allocation();
        a.Shortfalls.Add(5);
        AssertTrue(ReinforcementRouting.TryRouteRecruit(a, own, 150f, 500f, 0f, out float x, out float z, out int to), "his marker is a position to route toward");
        AssertEqual(-1, to, "...never his formation: no instance id of a player formation is a destination");
        AssertNear(480f, x, 0.001f, "his hold point is his last waypoint, not where he stands");
        AssertNear(0f, z, 0.001f, "(z)");
        // A carrying group is an AI asset and is a destination as any AI corps.
        var carrying = Corps(6, 520f, 3000f);
        carrying.Chain = WlChainClass.CarryingGroup;
        own.Add(carrying);
        a.Shortfalls.Clear();
        a.Shortfalls.Add(6);
        ReinforcementRouting.TryRouteRecruit(a, own, 150f, 500f, 0f, out _, out _, out to);
        AssertEqual(6, to, "the carrying group may be fed");
    }

    // The patch acts only inside vanilla's ZoneRecruiting (so the transfer's own GrabExistingGroup call at
    // 17319 is untouched), edits the by-ref position and skips nothing.
    private static void RecruitRoutingOnlyInsideZoneRecruiting()
    {
        string patch = ReadRepoFile("src/WhiskeyRealism/Patches/RecruitRoutingPatch.cs");
        AssertTrue(patch.Contains("[HarmonyPatch(typeof(AICampaign), \"GrabExistingGroup\")]"), "targets vanilla's GrabExistingGroup by name");
        string prefix = BodyOf(patch, "internal static void Prefix(ref Vector3 position)");
        int zone = prefix.IndexOf("if (!RecruitmentPatch.InZoneRecruiting) return;", StringComparison.Ordinal);
        int scope = prefix.IndexOf("TelemetryPerf.Scope(", StringComparison.Ordinal);
        AssertTrue(zone >= 0 && scope > zone, "the zone test is the first statement, before the perf scope and the first-fire mark");
        AssertTrue(prefix.Contains("PatchFirstFire.Mark(\"campaign.patch.recruit-routing\");"), "first-fire id");
        AssertTrue(prefix.Contains("int aifaction = RecruitmentPatch.ActiveAifaction;"), "the faction is the one ZoneRecruiting recruits for");
        AssertTrue(prefix.Contains("position = new Vector3(routeX, position.y, routeZ);"), "an argument edit that keeps the cell's height");
        AssertFalse(patch.Contains("AIFaction"), "no declared AIFaction parameter: the faction type is private");
        AssertFalse(patch.Contains("static bool Prefix"), "a non-skipping Prefix");
        AssertTrue(prefix.Contains("alliance < 0 || alliance > 1"), "alliance bound");

        string recruitment = ReadRepoFile("src/WhiskeyRealism/Patches/RecruitmentPatch.cs");
        AssertTrue(HasLine(recruitment, "internal static bool InZoneRecruiting => _zoneDepth > 0;"), "the zone accessor");
        AssertTrue(HasLine(recruitment, "internal static int ActiveAifaction => _activeAifaction;"), "the faction accessor");
    }

    // ---- the transfer pair ----

    private static void TransferDeficitIsShortFormationSurplusIsReserveExcess()
    {
        var plan = new TheaterPlan { Alliance = 0, DaySerial = Day };
        plan.Sectors.Add(new SectorPlan { SectorId = 4, Posture = TheaterPosture.Hold, HoldNeed = 6000f });
        var own = new List<FormationRecord> { Corps(5, 560f, 3000f), Corps(7, 200f, 8000f), Corps(8, 250f, 9000f) };
        var a = new Allocation();
        a.Assignments.Add(new Assignment { InstanceId = 5, Role = FormationRole.Hold, SectorId = 4 });
        a.Assignments.Add(new Assignment { InstanceId = 7, Role = FormationRole.Reserve, SectorId = 1 });
        a.Assignments.Add(new Assignment { InstanceId = 8, Role = FormationRole.Hold, SectorId = 1 });
        a.Shortfalls.Add(5);
        a.Surpluses.Add(8);
        a.Surpluses.Add(7);
        AssertTrue(ReinforcementRouting.TryTransferPair(plan, a, own, DonorRadius, out var pair), "a shortfall and a surplus make a transfer");
        AssertEqual(5, pair.DeficitInstanceId, "the deficit is the short formation");
        AssertNear(560f, pair.DeficitX, 0.001f, "...at its hold point");
        AssertNear(3000f, pair.DeficitStrength, 0.01f, "...short of max(6000, garrison) by 3000");
        AssertEqual(8, pair.SurplusInstanceId, "the source is the first surplus formation (largest first)");
        AssertNear(250f, pair.SurplusX, 0.001f, "...at its position");
        AssertNear(9000f, pair.SurplusStrength, 0.01f, "...and its strength");

        // A Reserve formation is a surplus the same way.
        a.Surpluses.Clear();
        a.Surpluses.Add(7);
        ReinforcementRouting.TryTransferPair(plan, a, own, DonorRadius, out pair);
        AssertEqual(7, pair.SurplusInstanceId, "the reserve excess");
    }

    private static void TransferSurplusNeverFromMainEffort()
    {
        var plan = new TheaterPlan { Alliance = 0, DaySerial = Day };
        plan.Sectors.Add(new SectorPlan { SectorId = 4, Posture = TheaterPosture.Hold, HoldNeed = 6000f });
        var own = new List<FormationRecord> { Corps(5, 560f, 3000f), Corps(1, 110f, 20000f), Corps(2, 50f, 20000f), Corps(3, 300f, 4000f) };
        var a = new Allocation();
        a.Assignments.Add(new Assignment { InstanceId = 5, Role = FormationRole.Hold, SectorId = 4 });
        a.Assignments.Add(new Assignment { InstanceId = 1, Role = FormationRole.MainEffort, SectorId = 1 });
        a.Assignments.Add(new Assignment { InstanceId = 2, Role = FormationRole.CapitalGuard, SectorId = 0 });
        a.Assignments.Add(new Assignment { InstanceId = 3, Role = FormationRole.Reserve, SectorId = 2 });
        a.Shortfalls.Add(5);
        // A stale list naming the front-line corps first: they are skipped, the reserve is drawn on.
        a.Surpluses.Add(1);
        a.Surpluses.Add(2);
        a.Surpluses.Add(3);
        AssertTrue(ReinforcementRouting.TryTransferPair(plan, a, own, DonorRadius, out var pair), "the reserve is still available");
        AssertEqual(3, pair.SurplusInstanceId, "never the main effort or a capital guard");
        a.Surpluses.RemoveAt(2);
        AssertFalse(ReinforcementRouting.TryTransferPair(plan, a, own, DonorRadius, out _), "with only the main effort and the guard as sources nothing is taken from them");
    }

    private static void TransferNeedsAPositiveShortfallAndASource()
    {
        var plan = new TheaterPlan { Alliance = 0, DaySerial = Day };
        plan.Sectors.Add(new SectorPlan { SectorId = 4, Posture = TheaterPosture.Hold, HoldNeed = 1000f });
        var own = new List<FormationRecord> { Corps(5, 560f, 9000f), Corps(7, 200f, 8000f) };
        var a = new Allocation();
        a.Assignments.Add(new Assignment { InstanceId = 5, Role = FormationRole.Hold, SectorId = 4 });
        a.Assignments.Add(new Assignment { InstanceId = 7, Role = FormationRole.Reserve, SectorId = 1 });
        a.Shortfalls.Add(5);
        a.Surpluses.Add(7);
        AssertFalse(ReinforcementRouting.TryTransferPair(plan, a, own, DonorRadius, out _), "a carrier that already holds 9000 of a 5000 garrison is not short");
        plan.Sectors[0].HoldNeed = 20000f;
        AssertTrue(ReinforcementRouting.TryTransferPair(plan, a, own, DonorRadius, out var pair), "...but short of 20000 it is");
        AssertNear(11000f, pair.DeficitStrength, 0.01f, "the need is the target less what holds");
        a.Surpluses.Clear();
        AssertFalse(ReinforcementRouting.TryTransferPair(plan, a, own, DonorRadius, out _), "no surplus formation, no transfer");
        a.Surpluses.Add(5);
        AssertFalse(ReinforcementRouting.TryTransferPair(plan, a, own, DonorRadius, out _), "the deficit is never its own source");
        a.Surpluses.Clear();
        a.Surpluses.Add(99);
        AssertFalse(ReinforcementRouting.TryTransferPair(plan, a, own, DonorRadius, out _), "a surplus id with no live formation is skipped");
        AssertFalse(ReinforcementRouting.TryTransferPair(null, a, own, DonorRadius, out _), "no plan");
        AssertFalse(ReinforcementRouting.TryTransferPair(plan, null, own, DonorRadius, out _), "no allocation");
    }

    // Vanilla's donor is the first top group within the relocation range of the source POINT (GrabOwnTopUnitsNearPosition
    // 12824, no role or player filter at 17284), so a surplus is used only where no protected formation stands in reach.
    private static void TransferSourceKeepsVanillasDonorPickOffTheFront()
    {
        TheaterPlan Plan()
        {
            var plan = new TheaterPlan { Alliance = 0, DaySerial = Day };
            plan.Sectors.Add(new SectorPlan { SectorId = 4, Posture = TheaterPosture.Hold, HoldNeed = 6000f });
            return plan;
        }
        Allocation Alloc(params (int id, FormationRole role, bool withheld)[] roles)
        {
            var a = new Allocation();
            a.Assignments.Add(new Assignment { InstanceId = 5, Role = FormationRole.Hold, SectorId = 4 });
            foreach (var r in roles) a.Assignments.Add(new Assignment { InstanceId = r.id, Role = r.role, SectorId = 1, Withheld = r.withheld });
            a.Shortfalls.Add(5);
            a.Surpluses.Add(7);
            a.Surpluses.Add(8);
            foreach (var r in roles)
                if (r.role == FormationRole.Reserve && r.id != 7 && r.id != 8) a.Surpluses.Add(r.id); // the allocator lists every Reserve
            return a;
        }
        // deficit 5 at 560; surplus 7 (largest) at 250 and surplus 8 at 20, 230 apart (beyond the 150 donor radius)
        List<FormationRecord> Own(params FormationRecord[] extra)
        {
            var own = new List<FormationRecord> { Corps(5, 560f, 3000f), Corps(7, 250f, 9000f), Corps(8, 20f, 6000f) };
            own.AddRange(extra);
            return own;
        }
        var plan0 = Plan();

        // A MainEffort corps within reach of the largest surplus: the swap uses the next surplus.
        var front = Corps(1, 300f, 20000f);
        AssertTrue(ReinforcementRouting.TryTransferPair(plan0, Alloc((7, FormationRole.Reserve, true), (8, FormationRole.Reserve, true), (1, FormationRole.MainEffort, false)), Own(front), DonorRadius, out var pair), "the second surplus qualifies");
        AssertEqual(8, pair.SurplusInstanceId, "a MainEffort corps near the largest surplus: the next surplus");

        // ...and none when both surpluses have a protected neighbour.
        var frontNearSecond = Corps(2, 60f, 20000f);
        AssertFalse(ReinforcementRouting.TryTransferPair(plan0, Alloc((7, FormationRole.Reserve, true), (8, FormationRole.Reserve, true), (1, FormationRole.MainEffort, false), (2, FormationRole.MainEffort, false)),
            Own(front, frontNearSecond), DonorRadius, out _), "no qualifying surplus: no swap, vanilla's records stay");

        // A surplus that is itself in transit is not a source (a marching corps does not donate): the next surplus is used, and none when it is the only one.
        var marching = Own();
        marching[1] = WithFlags(marching[1], FormationFlags.InTransfer);
        AssertEqual(8, PairSource(plan0, Alloc((7, FormationRole.Reserve, true), (8, FormationRole.Reserve, true)), marching), "the largest surplus is marching: the next surplus");
        var onlyMarching = Alloc((7, FormationRole.Reserve, true));
        onlyMarching.Surpluses.Remove(8);
        AssertEqual(-1, PairSource(plan0, onlyMarching, marching), "the only surplus is marching: no pair");

        // The radius is vanilla's: a protected formation just beyond it does not block, just inside does.
        var edge = Corps(1, 250f + DonorRadius + 1f, 20000f);
        AssertTrue(ReinforcementRouting.TryTransferPair(plan0, Alloc((7, FormationRole.Reserve, true), (8, FormationRole.Reserve, true), (1, FormationRole.MainEffort, false)), Own(edge), DonorRadius, out pair), "beyond the radius");
        AssertEqual(7, pair.SurplusInstanceId, "the largest surplus is used when nothing protected is within reach");
        edge.X = 250f + DonorRadius;
        AssertTrue(ReinforcementRouting.TryTransferPair(plan0, Alloc((7, FormationRole.Reserve, true), (8, FormationRole.Reserve, true), (1, FormationRole.MainEffort, false)), Own(edge), DonorRadius, out pair), "at the radius");
        AssertEqual(8, pair.SurplusInstanceId, "vanilla's range test is <=: a corps exactly at the radius blocks");

        // Withheld non-surplus formations (a capital guard), formations in transit, and formations the planner may not move block too.
        var guard = Corps(3, 300f, 5000f);
        AssertEqual(8, PairSource(plan0, Alloc((7, FormationRole.Reserve, true), (8, FormationRole.Reserve, true), (3, FormationRole.CapitalGuard, true)), Own(guard)), "a capital guard in reach blocks");
        AssertEqual(8, PairSource(plan0, Alloc((7, FormationRole.Reserve, true), (8, FormationRole.Reserve, true), (3, FormationRole.CapitalGuard, false)), Own(guard)), "...whether or not it is marked withheld");
        AssertEqual(8, PairSource(plan0, Alloc((7, FormationRole.Reserve, true), (8, FormationRole.Reserve, true), (3, FormationRole.Recon, true)), Own(guard)), "a withheld formation of any other role in reach blocks");
        var transit = Corps(3, 300f, 5000f);
        transit.Flags |= FormationFlags.InTransfer;
        AssertEqual(8, PairSource(plan0, Alloc((7, FormationRole.Reserve, true), (8, FormationRole.Reserve, true), (3, FormationRole.Support, false)), Own(transit)), "a formation in transit in reach blocks");
        var playerGroup = WithChain(Corps(3, 300f, 5000f), WlChainClass.PlayerMapGroup);
        AssertEqual(8, PairSource(plan0, Alloc((7, FormationRole.Reserve, true), (8, FormationRole.Reserve, true), (3, FormationRole.Hold, false)), Own(playerGroup)), "the map-group player's group in reach blocks");

        // A source is judged by the same predicate as its neighbours: a stale surplus list naming the front, a capital guard
        // (marked withheld or not), an unallocated formation or an empty one yields no pair, with nothing else near.
        foreach (var role in new[] { FormationRole.MainEffort, FormationRole.CapitalGuard })
        {
            var stale = Alloc((7, role, false));
            stale.Surpluses.Remove(8);
            AssertEqual(-1, PairSource(plan0, stale, Own()), role + " named as the only surplus is never a source");
        }
        var unallocated = Alloc();
        unallocated.Surpluses.Clear();
        unallocated.Surpluses.Add(7); // formation 7 has no assignment
        AssertEqual(-1, PairSource(plan0, unallocated, Own()), "a surplus with no assignment is never a source");
        var empty = Own();
        empty[1] = Corps(7, 250f, 0f);
        var emptyAllocation = Alloc((7, FormationRole.Reserve, true));
        emptyAllocation.Surpluses.Remove(8);
        AssertEqual(-1, PairSource(plan0, emptyAllocation, empty), "a surplus with no strength is never a source");

        // A withheld Reserve is itself a surplus, and a plain Support corps is no front: neither blocks.
        var otherReserve = Corps(3, 300f, 5000f);
        AssertEqual(7, PairSource(plan0, Alloc((7, FormationRole.Reserve, true), (8, FormationRole.Reserve, true), (3, FormationRole.Reserve, true)), Own(otherReserve)), "a neighbouring Reserve is a legitimate donor");
        var support = Corps(3, 300f, 5000f);
        AssertEqual(7, PairSource(plan0, Alloc((7, FormationRole.Reserve, true), (8, FormationRole.Reserve, true), (3, FormationRole.Support, false)), Own(support)), "a neighbouring Support corps is not protected");
    }

    private static int PairSource(TheaterPlan plan, Allocation a, List<FormationRecord> own)
    {
        return ReinforcementRouting.TryTransferPair(plan, a, own, DonorRadius, out var pair) ? pair.SurplusInstanceId : -1;
    }

    // G4 verify r2 N1: a map-group player's formation may carry a Shortfall as a position marker only.
    private static void ReinforcementRoutingNeverTargetsAPlayerFormation()
    {
        var plan = new TheaterPlan { Alliance = 0, DaySerial = Day };
        plan.Sectors.Add(new SectorPlan { SectorId = 4, Posture = TheaterPosture.Screen, HoldNeed = 6000f });
        var player = Corps(5, 550f, 3000f);
        player.Chain = WlChainClass.PlayerMapGroup;
        player.WaypointX = 480f; // where his group counts (ReserveRules.HoldPoint)
        player.Morale = 0.5f;    // counted at strength x morale, vanilla's measure
        var ai = Corps(8, 250f, 9000f);
        var secondGroup = WithChain(Corps(6, 900f, 30000f), WlChainClass.PlayerMapGroup); // a larger player group, named as a surplus
        var command = WithChain(Corps(9, 910f, 30000f), WlChainClass.PlayerCommand);
        var own = new List<FormationRecord> { player, ai, secondGroup, command };
        var a = new Allocation();
        a.Assignments.Add(new Assignment { InstanceId = 5, Role = FormationRole.Hold, SectorId = 4 });
        a.Assignments.Add(new Assignment { InstanceId = 8, Role = FormationRole.Reserve, SectorId = 1 });
        a.Assignments.Add(new Assignment { InstanceId = 6, Role = FormationRole.Reserve, SectorId = 1 });
        a.Assignments.Add(new Assignment { InstanceId = 9, Role = FormationRole.Reserve, SectorId = 1 });
        a.Shortfalls.Add(5);
        a.Surpluses.Add(6); // stale entries: even named as surpluses, a player formation is never a source
        a.Surpluses.Add(9);
        a.Surpluses.Add(5);
        a.Surpluses.Add(8);

        AssertTrue(ReinforcementRouting.TryRouteRecruit(a, own, 150f, 500f, 0f, out float rx, out _, out int routedTo), "a shortfall list whose only carrier is the player routes to his hold point");
        AssertNear(480f, rx, 0.001f, "...his last waypoint");
        AssertEqual(-1, routedTo, "...and never names his formation");

        AssertTrue(ReinforcementRouting.TryTransferPair(plan, a, own, DonorRadius, out var pair), "a pair built from a marker");
        AssertEqual(-1, pair.DeficitInstanceId, "his instance id is never a destination");
        AssertNear(480f, pair.DeficitX, 0.001f, "the deficit is his hold point");
        AssertNear(4500f, pair.DeficitStrength, 0.01f, "...sized by the hold target less what counts there (his group at strength x morale: 1500)");
        AssertEqual(8, pair.SurplusInstanceId, "the source is the AI reserve");
        AssertFalse(pair.SurplusInstanceId == 5 || pair.DeficitInstanceId == 5, "his id is neither source nor destination");

        // With nothing but player formations to draw on there is no pair at all.
        a.Surpluses.Clear();
        a.Surpluses.Add(6);
        a.Surpluses.Add(9);
        a.Surpluses.Add(5);
        AssertFalse(ReinforcementRouting.TryTransferPair(plan, a, own, DonorRadius, out _), "a candidate that fails MayMove is dropped from the source side");
    }

    private static void ShortfallStrengthIsTheCarriersNeedByRole()
    {
        var plan = new TheaterPlan
        {
            Alliance = 0, DaySerial = Day, MainSectorId = 1, MainRequiredRatio = 1.5f, MainEnemyStrength = 20000f, CapitalFloorStrength = 10000f
        };
        plan.Sectors.Add(new SectorPlan { SectorId = 2, Posture = TheaterPosture.Screen, HoldNeed = 8000f });
        plan.Sectors.Add(new SectorPlan { SectorId = 3, Posture = TheaterPosture.EconomyOfForce, HoldNeed = 0f });
        var own = new List<FormationRecord>
        {
            Corps(1, 110f, 15000f), Corps(2, 300f, 10000f), Corps(3, 20f, 4000f), Corps(4, 560f, 3000f), Corps(5, 580f, 1000f), Corps(6, 600f, 2000f),
            Corps(8, 570f, 9999f)
        };
        own[6] = WithChain(own[6], WlChainClass.PlayerCommand); // inside the player's own group: counts nothing anywhere
        var a = new Allocation();
        a.Assignments.Add(new Assignment { InstanceId = 1, Role = FormationRole.MainEffort, SectorId = 1 });
        a.Assignments.Add(new Assignment { InstanceId = 2, Role = FormationRole.MainEffort, SectorId = 1 });
        a.Assignments.Add(new Assignment { InstanceId = 3, Role = FormationRole.CapitalGuard, SectorId = 0 });
        a.Assignments.Add(new Assignment { InstanceId = 4, Role = FormationRole.Hold, SectorId = 2 });
        a.Assignments.Add(new Assignment { InstanceId = 5, Role = FormationRole.Hold, SectorId = 2 });
        a.Assignments.Add(new Assignment { InstanceId = 6, Role = FormationRole.Hold, SectorId = 3 });
        a.Assignments.Add(new Assignment { InstanceId = 8, Role = FormationRole.Hold, SectorId = 2 });
        AssertNear(5000f, ReinforcementRouting.ShortfallStrength(plan, a, own, 1), 0.01f, "main effort: 1.5 x 20000 less 25000");
        AssertNear(5000f, ReinforcementRouting.ShortfallStrength(plan, a, own, 2), 0.01f, "...whichever main-effort formation carries it");
        AssertNear(6000f, ReinforcementRouting.ShortfallStrength(plan, a, own, 3), 0.01f, "capital: the floor less the guards");
        AssertNear(4000f, ReinforcementRouting.ShortfallStrength(plan, a, own, 4), 0.01f, "hold: max(8000, garrison) less the sector's 4000");
        AssertNear(3000f, ReinforcementRouting.ShortfallStrength(plan, a, own, 6), 0.01f, "a quiet sector: the 5000 garrison less 2000");
        AssertNear(0f, ReinforcementRouting.ShortfallStrength(plan, a, own, 99), 0.001f, "an unknown id");
        a.Assignments.Add(new Assignment { InstanceId = 7, Role = FormationRole.Reserve, SectorId = 1 });
        own.Add(Corps(7, 100f, 9000f));
        AssertNear(0f, ReinforcementRouting.ShortfallStrength(plan, a, own, 7), 0.001f, "a Reserve formation carries no need");
        plan.MainEnemyStrength = 1000f;
        AssertNear(0f, ReinforcementRouting.ShortfallStrength(plan, a, own, 1), 0.001f, "never negative");
    }

    // The amounts above restate the allocator's carrier definitions (ForceAllocator.ListShortfalls); this pins them
    // to the real allocator so they cannot drift: every carrier it reports has a positive need here.
    private static void ShortfallAmountsAgreeWithTheAllocatorsCarriers()
    {
        var s = Wide();
        s.Own.Add(Corps(1, 110f, 15000f));
        s.Own.Add(Corps(2, 300f, 10000f));
        s.Own.Add(Corps(5, 560f, 3000f));
        s.Beliefs.Add(Belief(120f, 0f, 20000f));
        var plan = MainPlan(s, new[] { 1, 2 }, 100f);
        plan.MainRequiredRatio = 1.5f;
        plan.Sectors.Add(new SectorPlan { SectorId = Sectors(s).SectorAt(550f, 0f), Posture = TheaterPosture.Screen, HoldNeed = 6000f });
        var a = Alloc(plan, s);
        AssertEqual("5,1", string.Join(",", a.Shortfalls), "the allocator's carriers: the emptier hold, then the main effort");
        AssertNear(3000f, ReinforcementRouting.ShortfallStrength(plan, a, s.Own, 5), 0.01f, "hold sector: 6000 less 3000");
        AssertNear(5000f, ReinforcementRouting.ShortfallStrength(plan, a, s.Own, 1), 0.01f, "main effort: 30000 less 25000");

        var c = Line(0, 5);
        c.Own.Add(Corps(6, 50f, 4000f));
        c.Beliefs.Add(Belief(20f, 0f, 70000f));
        var capital = new TheaterPlan { Alliance = 0, DaySerial = Day, CapitalFloorStrength = 10000f };
        var ca = Alloc(capital, c);
        AssertEqual("6", string.Join(",", ca.Shortfalls), "the lone guard is short of the floor");
        AssertNear(6000f, ReinforcementRouting.ShortfallStrength(capital, ca, c.Own, 6), 0.01f, "capital: 10000 less 4000");

        // A shortfall pair built over the real allocator (donor radius 1: the geometry guard is tested on its own): the carrier's need is the deficit and the first surplus is the source.
        var wide = Wide();
        wide.Own.Add(Corps(1, 110f, 8000f));
        wide.Own.Add(Corps(2, 200f, 8000f));
        wide.Own.Add(Corps(3, 350f, 8000f));
        wide.Own.Add(Corps(4, 10f, 5000f));
        wide.Own.Add(Corps(5, 520f, 3000f));
        wide.Own.Add(Corps(6, 580f, 3000f));
        wide.Own.Add(Corps(7, 550f, 3000f));
        wide.Beliefs.Add(Belief(120f, 0f, 10000f));
        var widePlan = MainPlan(wide, new[] { 1, 2, 3 }, 100f);
        widePlan.CapitalFloorStrength = 4000f;
        widePlan.Sectors.Add(new SectorPlan { SectorId = Sectors(wide).SectorAt(550f, 0f), Posture = TheaterPosture.Screen, HoldNeed = 20000f });
        var wa = Alloc(widePlan, wide);
        foreach (int id in wa.Shortfalls)
            AssertTrue(ReinforcementRouting.ShortfallStrength(widePlan, wa, wide.Own, id) > 0f, "allocator carrier " + id + " has a positive need");
        if (wa.Shortfalls.Count > 0 && wa.Surpluses.Count > 0)
        {
            AssertTrue(ReinforcementRouting.TryTransferPair(widePlan, wa, wide.Own, 1f, out var pair), "a real pair when the allocator names both sides");
            AssertEqual(wa.Shortfalls[0], pair.DeficitInstanceId, "the worst carrier is the deficit");
            AssertFalse(wa.TryGet(pair.SurplusInstanceId, out var source) && (source.Role == FormationRole.MainEffort || source.Role == FormationRole.CapitalGuard), "the source is not the front");
        }
    }

    // ---- runtime pins ----

    private static void PatchesWireTheLivePlanAndBothFlags()
    {
        string defensive = ReadRepoFile("src/WhiskeyRealism/Patches/DefensiveOpsPatch.cs");
        string defensivePrefix = BodyOf(defensive, "internal static void Prefix(int _aifaction)");
        AssertTrue(defensivePrefix.Contains("if (!FlagOn(Plugin.EnableCampaignPlanner) || !FlagOn(Plugin.EnableCampaignPlannerExecutors)) return;"), "#4 gated by both planner flags");
        AssertTrue(defensivePrefix.Contains("allianceId < 0 || allianceId >= LastRowSignature.Length"), "#4 alliance bound");
        AssertTrue(defensivePrefix.Contains("CampaignPlanStore.TryGetPlan(allianceId, out var plan)"), "#4 reads the published plan");
        AssertTrue(defensivePrefix.Contains("CampaignPlanStore.TryGetAllocation(allianceId, out var allocation)"), "#4 reads the published allocation");
        AssertTrue(defensivePrefix.Contains("CapitalFloorPolicy.VanillaNeed("), "#4's need is the belief-based formula");
        AssertTrue(defensivePrefix.Contains("EnemyDetectionRuntime.BeliefsFor(allianceId)"), "...on the side's beliefs");
        AssertTrue(defensivePrefix.Contains("CapitalFloorPolicy.PickPledge(allocation, vanillaNeed, CandidatePool)"), "#4 pledges the allocation's guard");
        AssertTrue(defensivePrefix.Contains("EmitCapitalFloor(allianceId, vanillaNeed, plan.CapitalFloorStrength, plan.CapitalReach, pledged);"), "#4 reports the floor");
        AssertTrue(defensivePrefix.Contains("PatchFirstFire.Mark(\"campaign.patch.defensive-ops\");"), "first-fire id kept");
        AssertFalse(defensive.Contains("enemyunits"), "#4 no longer reads vanilla's enemy list");
        foreach (string gone in new[] { "CICs", "Effective(", "DirectorMemories", "EraStageManager", "PersonalityVector", "SearchRadiusMultiplier" })
            AssertFalse(defensive.Contains(gone), "#4 no longer reads " + gone);
        AssertTrue(defensive.Contains("\"[CapitalFloor] alliance=\""), "the [CapitalFloor] row");
        AssertTrue(defensive.Contains("CampaignScope.Register(\"defensive-ops-capital-floor-row\", ResetRows);"), "its per-campaign state resets with the campaign");
        AssertFalse(defensive.Contains("static bool Prefix"), "#4 stays a non-skipping Prefix");

        string transfer = ReadRepoFile("src/WhiskeyRealism/Patches/TransferOfUnitsPatch.cs");
        string transferPrefix = BodyOf(transfer, "internal static void Prefix(int _aifaction)");
        AssertTrue(transferPrefix.Contains("if (!FlagOn(Plugin.EnableCampaignPlanner) || !FlagOn(Plugin.EnableCampaignPlannerExecutors)) return;"), "#3 gated by both planner flags");
        AssertTrue(transferPrefix.Contains("allianceId < 0 || allianceId > 1"), "#3 alliance bound");
        AssertTrue(transferPrefix.Contains("ReinforcementRouting.TryTransferPair(plan, allocation, Own, GamePrefs.aimaximumdistancetosearchforunitrelocations, out var pair)"), "#3 swaps in the allocation's pair first");
        AssertTrue(transferPrefix.Contains("ReplacementFlowPolicy.ShouldSteer("), "#3 falls back to the main-target steer");
        AssertTrue(transferPrefix.Contains("ReplacementFlowRow.Emit(allianceId, ReplacementFlowRow.KindTransfer, toName, fromName);"), "#3 reports the flow");
        AssertTrue(transferPrefix.Contains("PatchFirstFire.Mark(\"campaign.patch.transfer-of-units.prefix\");"), "first-fire id kept");

        string recruit = ReadRepoFile("src/WhiskeyRealism/Patches/RecruitRoutingPatch.cs");
        AssertTrue(recruit.Contains("if (!FlagOn(Plugin.EnableCampaignPlanner) || !FlagOn(Plugin.EnableCampaignPlannerExecutors)) return;"), "recruit routing gated by both planner flags");
        AssertTrue(recruit.Contains("CampaignPlanStore.TryGetAllocation(alliance, out var allocation)"), "recruit routing reads the published allocation");
        AssertTrue(recruit.Contains("StrategicCoordinator.IsPlayerCICOf(alliance, StrategicCoordinator.ResolvePlayerAlliance())"), "the W&L CIC player's side is left alone");

        string smoke = ReadRepoFile("tools/smoke-situational.txt");
        AssertTrue(smoke.Contains("campaign.patch.recruit-routing"), "the recruit-routing id is situational");
        AssertTrue(smoke.Contains("campaign.patch.wl-career-transfer.*"), "the career ids are situational");
    }

    // The swap replaces both of vanilla's records, so the Finalizer (it runs when vanilla throws too) restores both.
    private static void TransferFinalizerRestoresBothRecords()
    {
        string transfer = ReadRepoFile("src/WhiskeyRealism/Patches/TransferOfUnitsPatch.cs");
        string finalizer = BodyOf(transfer, "internal static void Finalizer(int _aifaction)");
        AssertTrue(HasLine(finalizer, "state.DeficitField?.SetValue(state.Faction, state.OriginalDeficit);"), "the deficit record is restored");
        AssertTrue(HasLine(finalizer, "state.SurplusField?.SetValue(state.Faction, state.OriginalSurplus);"), "the surplus record is restored");
        AssertTrue(transfer.Contains("SurplusField = surplusData != null ? surplusField : null,"), "the surplus is restored only when it was replaced");
        AssertTrue(HasLine(transfer, "deficitField.SetValue(faction, deficitData);"), "the deficit is written after the state is stored");
        AssertTrue(transfer.IndexOf("_pending[_aifaction] = new TransferSteerState", StringComparison.Ordinal)
            < transfer.IndexOf("deficitField.SetValue(faction, deficitData);", StringComparison.Ordinal), "state before write");
        // The Postfix outcome block (BUG-WL-010) is unchanged.
        AssertTrue(transfer.Contains("WlEconomyFixes.ClassifyTransfer(transit, career)"), "the outcome classification stays");
    }

    // The live reads the pure policies cannot see: each is one whole line, so a commented-out or altered copy fails.
    private static void LiveReadsBehindThePatchesArePinned()
    {
        string defensive = ReadRepoFile("src/WhiskeyRealism/Patches/DefensiveOpsPatch.cs");
        AssertTrue(HasLine(defensive, "CustomOrdered = CoastalDefenseCustomOrderRunner.IsCustomOrdered(allianceId, instanceId)"), "#4 reads the coastal runner's custom orders");
        AssertTrue(HasLine(defensive, "if (DLC_WL.IsMovedByPlayer(unit)) return false;"), "#4 keeps vanilla's own player-unit filter (11791)");
        AssertTrue(HasLine(defensive, "LiveFormationReader.Read(ownUnits, allianceId, true, OwnPool);"), "#4's need reads groupstrengthactive, vanilla's measure (11733-11740)");
        AssertTrue(HasLine(defensive, "if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return;"), "#4 leaves the W&L CIC player's side alone");

        string transfer = ReadRepoFile("src/WhiskeyRealism/Patches/TransferOfUnitsPatch.cs");
        AssertTrue(HasLine(transfer, "if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return;"), "#3 leaves the W&L CIC player's side alone");
        AssertTrue(HasLine(transfer, "LiveFormationReader.Read(ownUnits, allianceId, false, Own);"), "#3 reads groupstrengthdirect, the allocation's measure");

        string reader = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/LiveFormationReader.cs");
        AssertTrue(HasLine(reader, "Strength = activeStrength ? unit.groupstrengthactive : unit.groupstrengthdirect,"), "the reader honours the strength contract");
        AssertTrue(HasLine(reader, "Flags = unit.transfertime > 0f ? FormationFlags.InTransfer : default(FormationFlags),"), "a unit in transit is flagged, as the snapshot flags it");
        AssertTrue(HasLine(reader, "Chain = WlStrategicOrderBridge.ChainClassOf(in chain, allianceId, unit)"), "every record carries its chain class");

        string recruit = ReadRepoFile("src/WhiskeyRealism/Patches/RecruitRoutingPatch.cs");
        AssertTrue(HasLine(recruit, "LiveFormationReader.Read(ownUnits, alliance, false, Own);"), "recruit routing reads groupstrengthdirect");
    }

    private static void CareerFixIsArgumentOnlyInsideTheTransferScope()
    {
        string patch = ReadRepoFile("src/WhiskeyRealism/Patches/WlCareerTransferFixPatch.cs");
        AssertTrue(patch.Contains("[HarmonyPatch(typeof(AICampaign), \"CheckTransferOfUnits\")]"), "the scope wraps CheckTransferOfUnits");
        AssertTrue(patch.Contains("[HarmonyPatch(typeof(BattleUnits), \"MoveUnit\")]"), "the argument edit is on BattleUnits.MoveUnit");
        string scope = BodyOf(patch, "internal static void Prefix(out bool __state)");
        AssertTrue(scope.Contains("if (!DLC_WL.dlc_scenarioactive) return;"), "no career, no scope");
        AssertTrue(scope.Contains("Plugin.EnableWlCareerTransferFix"), "the scope is behind the flag");
        AssertTrue(scope.Contains("PatchFirstFire.Mark(\"campaign.patch.wl-career-transfer.scope\");"), "scope first-fire id");
        string finalizer = BodyOf(patch, "internal static void Finalizer(bool __state)");
        AssertTrue(finalizer.Contains("if (!__state) return;"), "only a call that opened the scope closes it");
        string move = BodyOf(patch, "internal static void Prefix(GameObject unittomove, GameObject unittomovebelow, ref bool skipdlccheck)");
        int scoped = move.IndexOf("if (!WlCareerTransferScopePatch.Inside) return;", StringComparison.Ordinal);
        int component = move.IndexOf("GetComponent<Regiment>()", StringComparison.Ordinal);
        int allow = move.IndexOf("WlTransferFixDecision.Allow(true, DLC_WL.dlc_scenarioactive, source.dlcw_isundercommander, destination.dlcw_isundercommander)", StringComparison.Ordinal);
        int write = move.IndexOf("skipdlccheck = true;", StringComparison.Ordinal);
        AssertTrue(scoped >= 0 && scoped < component, "the scope test precedes any component lookup");
        AssertTrue(allow > component && write > allow, "the flag is set only after the decision allows it");
        AssertTrue(move.Contains("PatchFirstFire.Mark(\"campaign.patch.wl-career-transfer.move-unit\");"), "move-unit first-fire id");
        AssertFalse(patch.Contains("static bool Prefix"), "neither patch skips vanilla");
        AssertTrue(patch.Contains("OnceLog.Info(\"campaign-planner:wl-transfer-fix-first\""), "the first-fire row uses its static key");
        AssertTrue(patch.Contains("if (!_firstLogged)"), "...and formats it behind a first-log flag");

        string plugin = ReadRepoFile("src/WhiskeyRealism/Plugin.cs");
        int bind = plugin.IndexOf("\"Enable W&L Career Transfer Fix\"", StringComparison.Ordinal);
        AssertTrue(bind > 0 && plugin.Substring(bind, 80).Contains("true,"), "the flag is bound, default on");
    }

    private static void PolicyFilesArePureAndFogHonestBySource()
    {
        foreach (string file in new[]
        {
            "src/WhiskeyRealism/Strategic/Planner/CapitalFloorPolicy.cs",
            "src/WhiskeyRealism/Strategic/Planner/ReinforcementRouting.cs",
            "src/WhiskeyRealism/Strategic/Planner/ReplacementFlowPolicy.cs",
        })
        {
            string source = ReadRepoFile(file);
            foreach (string banned in new[] { "UnityEngine", "DateTime", "Environment.TickCount", "Time.realtimeSinceStartup", "System.Random", "AccessTools", "Plugin." })
                AssertFalse(source.Contains(banned), file + " mentions " + banned);
        }
    }

    // The personality-weighted need, the plan-target transfer helpers and the heatmap grid names are replaced by the
    // policies above, not kept beside them.
    private static void SupersededHelpersAreGone()
    {
        string gates = ReadRepoFile("src/WhiskeyRealism/Strategic/CampaignMilitaryGates.cs");
        foreach (string gone in new[] { "TransferHeatmapFields", "PlanTransferRequiredAtTarget", "ShouldSteerTransfer", "PersonalityCapitalDefenseNeed", "ShouldPrePledgeCapitalDefender", "PlanTransferMinimumPresence" })
            AssertFalse(gates.Contains(gone), "CampaignMilitaryGates still has " + gone);
        AssertTrue(gates.Contains("internal static float VanillaCapitalDefenseNeed("), "the one 11712-11741 formula stays");
    }

    // ---- fixtures ----

    private static FormationRecord Active(int id, float waypointX, float strength, float morale)
    {
        var f = Corps(id, waypointX, strength);
        f.Morale = morale;
        return f;
    }

    private static FormationRecord WithFlags(FormationRecord f, FormationFlags flags)
    {
        f.Flags |= flags;
        return f;
    }

    private static FormationRecord WithChain(FormationRecord f, WlChainClass chain)
    {
        f.Chain = chain;
        return f;
    }

    private static PledgeCandidate Pledgeable(int id, float distance)
    {
        return new PledgeCandidate { InstanceId = id, DistanceToCapital = distance, Chain = WlChainClass.None, PassesVanillaFilters = true };
    }

    private static Allocation Guards(params int[] ids)
    {
        var a = new Allocation();
        foreach (int id in ids) a.Assignments.Add(new Assignment { InstanceId = id, Role = FormationRole.CapitalGuard });
        return a;
    }

    private static CampaignSnapshot Line(int alliance, int areas)
    {
        var s = new CampaignSnapshot();
        s.Reset(alliance, alliance, Day, PlayerCommandClass.NotCareer, -1, null);
        s.RelocationRange = 150f;
        s.OffensiveDominance = 1.1f;
        s.MinStrengthForMovement = 1000f;
        s.MinMoraleForMovement = 0.3f;
        s.HasCapital = true;
        for (int i = 0; i < areas; i++)
            s.Areas.Add(new AreaRecord { Index = 10 + i, X = i * 100f, Z = 0f, EdgeX = new[] { i * 100f }, EdgeZ = new[] { 0f }, Importance = 1f, SupplyCapacity = 100f });
        s.Supply.Add(Iip(501, 100f, alliance));
        s.Supply.Add(Iip(503, 300f, alliance));
        return s;
    }

    // Seven areas (x 0..600), objectives at 100 / 300 / 500; own capital at x 0.
    private static CampaignSnapshot Wide()
    {
        var s = Line(0, 7);
        s.Supply.Add(Iip(505, 500f, 0));
        s.Objectives.Add(Target(98, 100f, 1f));
        s.Objectives.Add(Target(99, 300f, 0f));
        s.Objectives.Add(Target(97, 500f, 0f));
        return s;
    }

    private static ObjectiveRecord Target(int id, float x, float importance)
    {
        return new ObjectiveRecord { ObjectiveId = id, X = x, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = importance };
    }

    private static FormationRecord Corps(int id, float x, float strength)
    {
        return new FormationRecord
        {
            InstanceId = id, UnitKey = "corps" + id + ":" + id, UnitTyp = 15, X = x, Z = 0f, WaypointX = x, WaypointZ = 0f,
            Strength = strength, Morale = 1f, Supply01 = 1f, ReadinessStep = 3, CommandRange = 40f, BugleRange = 5f, CommanderId = id,
            AreaIndex = -1, Flags = FormationFlags.FightingForce, Name = "corps" + id, ParentInstanceId = -1,
            UseRails = true, UseRivers = true, GroupSpeed = 1f
        };
    }

    private static SupplySourceRecord Iip(int id, float x, int owner)
    {
        return new SupplySourceRecord { Id = id, Kind = SupplySourceKind.Iip, Owner = owner, X = x, Z = 0f, Condition = 1f, Built = true };
    }

    private static EnemyBelief Belief(float x, float z, float strength)
    {
        return new EnemyBelief { InstanceId = -1, UnitKey = "enemy:" + x, UnitTyp = 15, X = x, Z = z, Strength = strength, Confidence01 = 1f, LastSeenDay = Day };
    }

    private static SectorModel Sectors(CampaignSnapshot s) => SectorModel.Build(s);

    private static TheaterPlan ManualPlan(CampaignSnapshot s)
    {
        return new TheaterPlan { Alliance = s.Alliance, DaySerial = s.DaySerial };
    }

    // The real G3 plan (its mass, floor and published hold needs are what the allocator reads).
    private static TheaterPlan PlanOf(CampaignSnapshot s)
    {
        return TheaterPlanner.Plan(s, Sectors(s), null, new PlannerState(), null, null, Speed(), out _);
    }

    // A hand-built main effort at objective 98 (sector of x 100) over the given mass.
    private static TheaterPlan MainPlan(CampaignSnapshot s, int[] mass, float targetX)
    {
        var plan = ManualPlan(s);
        int sector = Sectors(s).SectorAt(targetX, 0f);
        plan.MainSectorId = sector;
        plan.MainObjectiveId = 98;
        plan.MainRequiredRatio = 1.1f;
        plan.MainEnemyStrength = EnemyBeliefQuery.StrengthWithin(s.Beliefs, targetX, 0f, s.RelocationRange);
        plan.Sectors.Add(new SectorPlan { SectorId = sector, Posture = TheaterPosture.MainEffort, TargetObjectiveId = 98, TargetX = targetX, TargetZ = 0f, RequiredRatio = 1.1f });
        plan.MassInstanceIds.AddRange(mass);
        return plan;
    }

    // A main effort whose gather point is (targetX, 0), no mass.
    private static TheaterPlan MainPlan(CampaignSnapshot s, float targetX) => MainPlan(s, new int[0], targetX);

    private static MarchSpeedInputs Speed()
    {
        var mounted = new float[19];
        for (int t = 14; t <= 16; t++) mounted[t] = 2f;
        return new MarchSpeedInputs
        {
            MountedBaseSpeed = mounted, BasicUnitSpeed = 1000f, CampaignSpeedFactor = 30f,
            WalkCost = 4f, RiverCost = 1f, RoadCost = 2f, RailCost = 1f,
            RailroadSpeedAdj = 1f, TrainCapacityFree = 0f, TrainCapacityPerMan = 0.001f
        };
    }

    private static Allocation Alloc(TheaterPlan plan, CampaignSnapshot s)
    {
        return ForceAllocator.Allocate(plan, s, Sectors(s), null, null, null);
    }

    private static Assignment Of(Allocation a, int instanceId)
    {
        AssertTrue(a.TryGet(instanceId, out var x), "formation " + instanceId + " has an assignment");
        return x;
    }
}
