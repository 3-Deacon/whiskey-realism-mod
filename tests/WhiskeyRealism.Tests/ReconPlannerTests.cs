using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;

using static TestHarness;
using static TestFixtures;

// Stage G slice G1b, pure half (docs/superpowers/plans/2026-10-15-campaign-planner.md § G1b):
// ReconPlanner (approaches, coverage gaps, screen/picket/advance-guard/probe tasks, formation
// choice), ReconMissionLedger (mission state machine and recall rules), ReconOrders (the cavalry
// order the G5d executor sends, never to a group carrying the W&L player) and the directive
// cleanup (Screen = on a recon mission; RaidSupport, RaidAllowed and ArmyRole deleted).
internal static class ReconPlannerTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("ScreenTargetsOldestMostUncertainBelief", ScreenTargetsOldestMostUncertainBelief),
            ("ScreenCoversUnwatchedCapitalApproach", ScreenCoversUnwatchedCapitalApproach),
            ("ScreenCoversUnwatchedMainEffortAxis", ScreenCoversUnwatchedMainEffortAxis),
            ("WatchedApproachGetsNoScreen", WatchedApproachGetsNoScreen),
            ("ScreenOwnPostDoesNotWatchItsApproach", ScreenOwnPostDoesNotWatchItsApproach),
            ("ScreensCappedPerTheaterAndAlliance", ScreensCappedPerTheaterAndAlliance),
            ("ScreenHoldPointKeepsStandoffFromNearestBelief", ScreenHoldPointKeepsStandoffFromNearestBelief),
            ("ScreenHoldPointClampedToSupplyReach", ScreenHoldPointClampedToSupplyReach),
            ("NoScreenWhenNothingInReach", NoScreenWhenNothingInReach),
            ("CavalryMajorityPreferred", CavalryMajorityPreferred),
            ("MainEffortKeepsItsOwnScreen", MainEffortKeepsItsOwnScreen),
            ("NeverPlayerCommandChainClass", NeverPlayerCommandChainClass),
            ("MapGroupPlayerNeverSoleScreen", MapGroupPlayerNeverSoleScreen),
            ("CarryingGroupUsableAsScreen", CarryingGroupUsableAsScreen),
            ("BelowMovementMinimaNeverChosen", BelowMovementMinimaNeverChosen),
            ("ScreenStaysWithIncumbent", ScreenStaysWithIncumbent),
            ("RecalledFormationNotRepicked", RecalledFormationNotRepicked),
            ("PicketHoldsUnwatchedMajorTown", PicketHoldsUnwatchedMajorTown),
            ("PicketNeverTakesFinalReserve", PicketNeverTakesFinalReserve),
            ("AdvanceGuardOnlyWithSpareStrengthAndNoCloseContact", AdvanceGuardOnlyWithSpareStrengthAndNoCloseContact),
            ("AdvanceGuardRecalledOnCloseBelief", AdvanceGuardRecalledOnCloseBelief),
            ("ScreenPostureSectorGetsAScreen", ScreenPostureSectorGetsAScreen),
            ("ProbeWhenMainObjectiveBeliefsStale", ProbeWhenMainObjectiveBeliefsStale),
            ("NoProbeWhileMainEffortInContact", NoProbeWhileMainEffortInContact),
            ("SyncAssignsMissionIdsAndClearsUnassigned", SyncAssignsMissionIdsAndClearsUnassigned),
            ("ContactRecallsScreen", ContactRecallsScreen),
            ("LowSupplyRecallsScreen", LowSupplyRecallsScreen),
            ("RecalledMissionClearsAtRecallPoint", RecalledMissionClearsAtRecallPoint),
            ("MissingFormationClearsMission", MissingFormationClearsMission),
            ("MissionExpiresAfterTwentyDays", MissionExpiresAfterTwentyDays),
            ("ProbeEscalateClearsAndWithdrawRecalls", ProbeEscalateClearsAndWithdrawRecalls),
            ("MissionsKeyedByUnitKey", MissionsKeyedByUnitKey),
            ("AlliancesTwoToFourIgnored", AlliancesTwoToFourIgnored),
            ("ReconNeverReadsDestinations", ReconNeverReadsDestinations),
            ("ScoutingOrderOnlyForCavalryScreens", ScoutingOrderOnlyForCavalryScreens),
            ("RecallClearsScoutingOrder", RecallClearsScoutingOrder),
            ("ScoutingOrderNeverReachesPlayerCommand", ScoutingOrderNeverReachesPlayerCommand),
            ("PlayerCommandRefusedLive", PlayerCommandRefusedLive),
            ("ScreenDirectiveOnlyForReconMissions", ScreenDirectiveOnlyForReconMissions),
            ("ScreenMapsToScoutingPerkRole", ScreenMapsToScoutingPerkRole),
            ("DivisionVsArmyIsDelayNotScreen", DivisionVsArmyIsDelayNotScreen),
            ("DefaultFallbackIsHold", DefaultFallbackIsHold),
        });
    }

    private const int Today = 100;
    private const float Sight = 50f;
    private const float Standoff = ReconPlanner.StandoffFraction * Sight; // 40

    // ---- screens ----

    private static void ScreenTargetsOldestMostUncertainBelief()
    {
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, 3000f, 0.9f, 14));
        s.Beliefs.Add(Belief("old", 300f, 0f, 20000f, Today - 20, 0.06f, 200f));
        s.Beliefs.Add(Belief("fresh", 0f, 300f, 20000f, Today - 1, 0.9f, 10f));

        var tasks = Plan(s, null);
        AssertTrue(tasks.Count >= 1, "a screen task");
        AssertEqual(ReconKind.Screen, tasks[0].Kind, "highest priority task is a screen");
        AssertNear(300f - Standoff, tasks[0].HoldX, 0.5f, "toward the oldest, most uncertain belief, at the standoff");
        AssertNear(0f, tasks[0].HoldZ, 0.5f, "on the axis to it");
        AssertContains(tasks[0].Reason, "belief", "reason names the belief screen");
        AssertTrue(tasks[0].Priority > tasks[1].Priority, "the fresh belief ranks lower");
    }

    private static void ScreenCoversUnwatchedCapitalApproach()
    {
        var s = LineWorld();
        s.HasCapital = true;
        s.Own.Add(Unit(1, 0f, 0f, 20000f, 0f, 15));
        s.Own.Add(Unit(2, 10f, 0f, 3000f, 0.9f, 14));
        var sectors = SectorModel.Build(s);

        var tasks = Plan(s, sectors, null);
        int i = Find(tasks, ReconKind.Screen, "capital-approach");
        AssertTrue(i >= 0, "unwatched approach from the capital to the enemy-held sector gets a screen");
        AssertNear(750f, tasks[i].HoldX, 0.5f, "forward edge of the approach (the enemy sector's centroid)");
        AssertEqual(2, tasks[i].SectorId, "sector of the hold point");
        AssertEqual(0f, tasks[i].FromX, "axis starts at the capital");
    }

    private static void ScreenCoversUnwatchedMainEffortAxis()
    {
        var s = LineWorld();
        s.Own.Add(Unit(1, 400f, 0f, 30000f, 0f, 15));
        s.Own.Add(Unit(2, 410f, 0f, 3000f, 0.9f, 14));
        var sectors = SectorModel.Build(s);
        var plan = MainPlan(2, 800f);

        var tasks = Plan(s, sectors, plan);
        int i = Find(tasks, ReconKind.Screen, "main-axis");
        AssertTrue(i >= 0, "unwatched main-effort axis gets a screen");
        AssertEqual(2, tasks[i].SectorId, "the main effort's own screen carries the main sector");
        AssertNear(800f - Standoff, tasks[i].HoldX, 0.5f, "standoff short of the enemy-held objective");
        AssertNear(410f, tasks[i].FromX, 0.5f, "axis starts at the main effort's front");
    }

    private static void WatchedApproachGetsNoScreen()
    {
        var s = LineWorld();
        s.HasCapital = true;
        for (int k = 0; k <= 10; k++)
            s.Own.Add(Unit(10 + k, k * 80f, 0f, 20000f, 0f, 15));
        var sectors = SectorModel.Build(s);

        var tasks = Plan(s, sectors, null);
        AssertEqual(-1, Find(tasks, ReconKind.Screen, "capital-approach"), "every sample of the approach is inside a viewer's radius");
        AssertTrue(ReconPlanner.UnwatchedAlong(0f, 0f, 200f, 0f, 25f, new[] { 0f }, new[] { 0f }, new[] { 50f }, 1), "a gap along the segment");
        AssertFalse(ReconPlanner.UnwatchedAlong(0f, 0f, 90f, 0f, 25f, new[] { 0f, 80f }, new[] { 0f, 0f }, new[] { 50f, 50f }, 2), "two viewers cover it");
    }

    private static void ScreenOwnPostDoesNotWatchItsApproach()
    {
        // A screen already holding on the approach must not make its own approach "watched",
        // or the task would vanish the day it arrives and the mission would flap. Fillers cover
        // 0..690; only the screen at 750 covers the 700 sample.
        var s = LineWorld();
        s.HasCapital = true;
        s.Own.Add(Unit(1, 0f, 0f, 20000f, 0f, 15));
        s.Own.Add(Unit(2, 750f, 0f, 3000f, 0.9f, 14));
        for (int k = 1; k <= 8; k++)
            s.Own.Add(Unit(20 + k, k * 80f, 0f, 2000f, 0f, 15));
        var sectors = SectorModel.Build(s);
        var ledger = new ReconMissionLedger();
        AddMission(ledger, s, ReconKind.Screen, 2, 750f, 0f, 2);

        var tasks = new List<ReconTask>();
        ReconPlanner.PlanTasks(s, sectors, null, ledger, tasks);
        AssertTrue(Find(tasks, ReconKind.Screen, "capital-approach") >= 0, "the screen's own post does not count as a viewer");
        ReconPlanner.PlanTasks(s, sectors, null, null, tasks);
        AssertEqual(-1, Find(tasks, ReconKind.Screen, "capital-approach"), "without the ledger it would count");
    }

    private static void ScreensCappedPerTheaterAndAlliance()
    {
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, 3000f, 0.9f, 14));
        float[,] spots =
        {
            { 100f, 300f }, { 200f, 300f }, { 300f, 300f },           // West
            { 700f, 300f }, { 750f, 300f }, { 780f, 300f },           // East
            { -400f, 300f }, { -450f, 300f }, { -500f, 300f },        // TransMiss
            { 900f, -1400f }, { 950f, -1400f }, { 1000f, -1400f }     // Coast
        };
        for (int k = 0; k < spots.GetLength(0); k++)
            s.Beliefs.Add(Belief("b" + k, spots[k, 0], spots[k, 1], 10000f, Today - 15, 0.2f, 100f));

        var tasks = Plan(s, null);
        var perTheater = new Dictionary<Theater, int>();
        int screens = 0;
        foreach (var t in tasks)
        {
            if (t.Kind != ReconKind.Screen) continue;
            screens++;
            var theater = TheaterClassifier.FromPosition(t.HoldX, t.HoldZ);
            perTheater.TryGetValue(theater, out int n);
            perTheater[theater] = n + 1;
        }
        foreach (var kv in perTheater)
            AssertTrue(kv.Value <= ReconPlanner.MaxScreensPerTheater, "at most 2 screens in " + kv.Key + ", got " + kv.Value);
        AssertTrue(perTheater.Count >= 3, "screens spread over at least three theaters, got " + perTheater.Count);
        AssertEqual(ReconPlanner.MaxMissionsPerAlliance, tasks.Count, "6 tasks in all");
        AssertEqual(6, screens, "all six are screens");
    }

    private static void ScreenHoldPointKeepsStandoffFromNearestBelief()
    {
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, 3000f, 0.9f, 14));
        s.Beliefs.Add(Belief("far", 300f, 0f, 20000f, Today - 25, 0.05f, 300f));
        s.Beliefs.Add(Belief("flank", 250f, 30f, 5000f, Today - 2, 0.8f, 20f));

        var tasks = Plan(s, null);
        AssertTrue(tasks.Count >= 1, "screen tasks");
        int toward = -1;
        for (int k = 0; k < tasks.Count; k++)
        {
            float d = NearestBelief(s, tasks[k].HoldX, tasks[k].HoldZ);
            AssertTrue(d >= Standoff - 0.01f, "hold point " + k + " keeps 0.8 x sight from every belief, got " + d);
            if (Math.Abs(tasks[k].HoldZ) < 0.5f) toward = k;
        }
        AssertTrue(toward >= 0, "the screen toward the far belief exists");
        AssertTrue(tasks[toward].HoldX < 300f - Standoff - 1f, "pulled back behind the flank belief, got " + tasks[toward].HoldX);
    }

    private static void ScreenHoldPointClampedToSupplyReach()
    {
        var s = World();
        var cav = Unit(1, 0f, 0f, 3000f, 0.9f, 14);
        cav.CommandRange = 100f;
        s.Own.Add(cav);
        s.Beliefs.Add(Belief("b", 500f, 0f, 20000f, Today - 20, 0.1f, 100f));

        var tasks = Plan(s, null);
        AssertEqual(1, tasks.Count, "one screen");
        AssertTrue(tasks[0].HoldX <= 100.01f, "pulled back into supply reach, got " + tasks[0].HoldX);
        AssertTrue(LogisticsGate.Evaluate(s, tasks[0].HoldX, tasks[0].HoldZ, 100f, 0f).InReach, "hold point is in reach");
    }

    private static void NoScreenWhenNothingInReach()
    {
        var s = World();
        s.Supply[0] = new SupplySourceRecord { Id = 1, Kind = SupplySourceKind.Iip, Owner = 1, X = 0f, Z = 0f, Built = true, Condition = 1f };
        s.Own.Add(Unit(1, 0f, 0f, 3000f, 0.9f, 14));
        s.Beliefs.Add(Belief("b", 500f, 0f, 20000f, Today - 20, 0.1f, 100f));
        AssertEqual(0, Plan(s, null).Count, "no own supply anywhere: no task");
    }

    // ---- formation choice ----

    private static void CavalryMajorityPreferred()
    {
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, 20000f, 0f, 15));
        s.Own.Add(Unit(2, 100f, 0f, 9500f, 0.8f, 14));
        s.Own.Add(Unit(3, 5f, 0f, 9000f, 0.3f, 15));
        var task = ScreenTask(0f, 0f, 200f, 0f, -1);
        AssertEqual(2, Pick(s, null, null, task), "cavalry-majority group beats nearer and smaller non-cavalry groups");
        s.Own.RemoveAt(1);
        AssertEqual(3, Pick(s, null, null, task), "then the smallest fighting group");
    }

    private static void MainEffortKeepsItsOwnScreen()
    {
        var s = LineWorld();
        s.Own.Add(Unit(1, 400f, 0f, 30000f, 0f, 15));
        s.Own.Add(Unit(2, 410f, 0f, 3000f, 0.9f, 14));  // the main body's only cavalry
        s.Own.Add(Unit(3, 0f, 0f, 3000f, 0.9f, 14));    // cavalry far from the main body
        var sectors = SectorModel.Build(s);
        var plan = MainPlan(2, 800f);

        var elsewhere = ScreenTask(420f, 0f, 420f, 300f, 1);
        AssertEqual(3, Pick(s, sectors, plan, elsewhere, false), "the main effort's last cavalry is kept for its own screen");
        AssertEqual(2, Pick(s, sectors, plan, elsewhere, true), "once the main effort has its screen the group may go");
        var own = ScreenTask(410f, 0f, 760f, 0f, 2);
        AssertEqual(2, Pick(s, sectors, plan, own, false), "the main effort's own screen task takes it");
    }

    private static void NeverPlayerCommandChainClass()
    {
        var s = World();
        var player = Unit(2, 0f, 0f, 3000f, 0.9f, 14);
        player.Chain = WlChainClass.PlayerCommand;
        s.Own.Add(player);
        var task = ScreenTask(0f, 0f, 200f, 0f, -1);
        AssertEqual(-1, Pick(s, null, null, task), "the player's own command is never a recon formation");
        var cic = Unit(4, 0f, 0f, 3000f, 0.9f, 14);
        cic.Chain = WlChainClass.CicSide;
        s.Own.Add(cic);
        AssertEqual(-1, Pick(s, null, null, task), "nor the CIC player's side");
        s.Own.Add(Unit(3, 90f, 0f, 3000f, 0.9f, 14));
        AssertEqual(3, Pick(s, null, null, task), "the AI group is picked instead");
    }

    private static void MapGroupPlayerNeverSoleScreen()
    {
        var s = LineWorld();
        var player = Unit(2, 600f, 0f, 9000f, 0.9f, 15);
        player.Chain = WlChainClass.PlayerMapGroup;
        s.Own.Add(player);
        s.Own.Add(Unit(1, 500f, 0f, 30000f, 0f, 15));
        var sectors = SectorModel.Build(s);
        var plan = new TheaterPlan { Alliance = 0, DaySerial = Today };
        plan.Sectors.Add(new SectorPlan { SectorId = 1, Posture = TheaterPosture.Hold });

        AssertEqual(1, Pick(s, sectors, plan, ScreenTask(600f, 0f, 600f, 200f, 1)), "the map-group player is never a screen; the AI corps is");
        s.Own.RemoveAt(1);
        AssertEqual(-1, Pick(s, sectors, plan, ScreenTask(600f, 0f, 600f, 200f, 1)), "and never the sole screen");
        var picket = new ReconTask { Kind = ReconKind.Picket, SectorId = 1, FromX = 400f, HoldX = 400f, Reason = "picket" };
        AssertEqual(2, Pick(s, sectors, plan, picket), "a town picket may fall to it (order-only, last tier)");
    }

    private static void CarryingGroupUsableAsScreen()
    {
        var s = World();
        var carrier = Unit(2, 0f, 0f, 3000f, 0.9f, 14);
        carrier.Chain = WlChainClass.CarryingGroup;
        s.Own.Add(carrier);
        AssertEqual(2, Pick(s, null, null, ScreenTask(0f, 0f, 200f, 0f, -1)), "the group carrying a below-map-level player may screen");
    }

    private static void BelowMovementMinimaNeverChosen()
    {
        var s = World();
        var task = ScreenTask(0f, 0f, 200f, 0f, -1);
        var unready = Unit(1, 0f, 0f, 3000f, 0.9f, 14);
        unready.ReadinessStep = 1;
        var weak = Unit(2, 0f, 0f, 1000f, 0.9f, 14);   // == aiminimumstrengthformovement: vanilla's <= rejects (14099)
        var shaken = Unit(3, 0f, 0f, 3000f, 0.9f, 14);
        shaken.Morale = 0.3f;                           // == aiminimummoraleformovement (14103)
        var busy = Unit(4, 0f, 0f, 3000f, 0.9f, 14);
        busy.Flags |= FormationFlags.InDefensiveOps;
        s.Own.Add(unready);
        s.Own.Add(weak);
        s.Own.Add(shaken);
        s.Own.Add(busy);
        AssertEqual(-1, Pick(s, null, null, task), "readiness step < 2, strength or morale at the minimum, or busy: never");
        s.Own.Add(Unit(5, 150f, 0f, 1001f, 0.9f, 14));
        AssertEqual(5, Pick(s, null, null, task), "just above the minima");
    }

    private static void ScreenStaysWithIncumbent()
    {
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, 3000f, 0.9f, 14));
        s.Own.Add(Unit(2, 260f, 0f, 3000f, 0.9f, 14));
        var ledger = new ReconMissionLedger();
        AddMission(ledger, s, ReconKind.Screen, 2, 260f, 0f, -1);
        var task = ScreenTask(0f, 0f, 265f, 0f, -1);
        AssertEqual(2, ReconPlanner.PickFormation(s, null, null, ledger, task, new List<int>(), true), "the formation already on that post keeps it");
        AssertEqual(1, ReconPlanner.PickFormation(s, null, null, null, task, new List<int>(), true), "without the ledger the nearest to the axis start");
    }

    private static void RecalledFormationNotRepicked()
    {
        var s = World();
        s.Own.Add(Unit(2, 0f, 0f, 3000f, 0.9f, 14));
        var ledger = new ReconMissionLedger();
        AddMission(ledger, s, ReconKind.Screen, 2, 300f, 0f, -1);
        var cut = s.Own[0];
        cut.Supply01 = 0.3f;
        s.Own[0] = cut;
        ledger.OnReview(s, new Allocation { Alliance = 0 });
        AssertEqual(ReconState.Recalled, ledger.Missions[0].State, "recalled on low supply");
        AssertEqual(-1, ReconPlanner.PickFormation(s, null, null, ledger, ScreenTask(0f, 0f, 200f, 0f, -1), new List<int>(), true), "a recalling group is not re-tasked");
    }

    // ---- pickets, advance guard, screen posture, probe ----

    private static void PicketHoldsUnwatchedMajorTown()
    {
        var s = LineWorld();
        s.Own.Add(Unit(1, 600f, 0f, 20000f, 0f, 15));
        var sectors = SectorModel.Build(s);
        var plan = new TheaterPlan { Alliance = 0, DaySerial = Today };
        plan.Sectors.Add(new SectorPlan { SectorId = 1, Posture = TheaterPosture.Hold });

        var tasks = Plan(s, sectors, plan);
        int i = Find(tasks, ReconKind.Picket, null);
        AssertTrue(i >= 0, "the unwatched rail hub in a Hold sector gets a picket");
        AssertEqual(400f, tasks[i].HoldX, "held at the town");
        AssertEqual(1, tasks[i].SectorId, "its sector");
        AssertEqual(1, Pick(s, sectors, plan, tasks[i]), "from a formation already in that sector");

        plan.Sectors[0].Posture = TheaterPosture.EconomyOfForce;
        AssertEqual(-1, Find(Plan(s, sectors, plan), ReconKind.Picket, null), "no picket off a threatened axis");
    }

    private static void PicketNeverTakesFinalReserve()
    {
        var s = LineWorld();
        s.Own.Add(Unit(1, 600f, 0f, 20000f, 0f, 15));
        s.Beliefs.Add(Belief("threat", 550f, 60f, 30000f, Today, 1f, 5f)); // hold need 0.6 x 30000 = 18000
        var sectors = SectorModel.Build(s);
        var plan = new TheaterPlan { Alliance = 0, DaySerial = Today };
        plan.Sectors.Add(new SectorPlan { SectorId = 1, Posture = TheaterPosture.Hold });
        var picket = new ReconTask { Kind = ReconKind.Picket, SectorId = 1, FromX = 400f, HoldX = 400f, Reason = "picket" };

        AssertEqual(-1, Pick(s, sectors, plan, picket), "the sector's only formation is its final reserve");
        s.Own.Add(Unit(2, 500f, 0f, 25000f, 0f, 15));
        int got = Pick(s, sectors, plan, picket);
        AssertTrue(got == 1 || got == 2, "with 45000 held either may go and 18000 stays");

        // The director's tempo raises the hold ratio as G4's hold need does: 0.7 x 30000 = 21000.
        var tense = LineWorld(new DirectorPosture { MinimumHoldRatioModifier = 0.1f });
        tense.Own.Add(Unit(1, 600f, 0f, 20000f, 0f, 15));
        tense.Own.Add(Unit(2, 500f, 0f, 25000f, 0f, 15));
        tense.Beliefs.Add(Belief("threat", 550f, 60f, 30000f, Today, 1f, 5f));
        AssertEqual(1, Pick(tense, SectorModel.Build(tense), plan, picket), "unit 1 leaving keeps 25000; unit 2 leaving would keep only 20000");
    }

    private static void AdvanceGuardOnlyWithSpareStrengthAndNoCloseContact()
    {
        var s = LineWorld();
        s.Own.Add(Unit(1, 400f, 0f, 30000f, 0f, 15));
        s.Own.Add(Unit(2, 410f, 0f, 4000f, 0.9f, 14));
        var sectors = SectorModel.Build(s);
        var plan = MainPlan(2, 800f);

        var tasks = Plan(s, sectors, plan);
        int i = Find(tasks, ReconKind.AdvanceGuard, null);
        AssertTrue(i >= 0, "spare strength, no believed enemy near the front: an advance guard");
        AssertNear(410f + Sight, tasks[i].HoldX, 0.5f, "one sight radius ahead of the front on the axis");
        AssertEqual(2, Pick(s, sectors, plan, tasks[i]), "a group of at most a quarter of the main body");

        s.Beliefs.Add(Belief("near", 440f, 30f, 2000f, Today, 1f, 5f));
        AssertEqual(-1, Find(Plan(s, sectors, plan), ReconKind.AdvanceGuard, null), "a believed enemy within one sight radius of the front: none");
        s.Beliefs.Clear();
        s.Beliefs.Add(Belief("strong", 800f, 0f, 40000f, Today, 1f, 5f));
        AssertEqual(-1, Find(Plan(s, sectors, plan), ReconKind.AdvanceGuard, null), "no spare strength over the required ratio: none");
    }

    private static void AdvanceGuardRecalledOnCloseBelief()
    {
        var s = LineWorld();
        s.Own.Add(Unit(1, 400f, 0f, 30000f, 0f, 15));
        s.Own.Add(Unit(2, 460f, 0f, 4000f, 0.9f, 14));
        var ledger = new ReconMissionLedger();
        AddMission(ledger, s, ReconKind.AdvanceGuard, 2, 460f, 0f, 2);
        var allocation = new Allocation { Alliance = 0 };
        allocation.Assignments.Add(new Assignment { InstanceId = 1, Role = FormationRole.MainEffort, ReconMissionId = -1 });

        ledger.OnReview(s, allocation);
        AssertEqual(ReconState.Holding, ledger.Missions[0].State, "at its hold point");
        s.Beliefs.Add(Belief("closing", 490f, 0f, 5000f, Today, 1f, 5f));
        ledger.OnReview(s, allocation);
        AssertEqual(ReconState.Recalled, ledger.Missions[0].State, "a belief inside the standoff recalls the advance guard");
        AssertEqual(400f, ledger.Missions[0].HoldX, "back to the main body");
        AssertFalse(ledger.IsRecon(2), "a recalled group is no longer on a recon mission");
    }

    private static void ScreenPostureSectorGetsAScreen()
    {
        var s = LineWorld();
        s.Own.Add(Unit(1, 400f, 0f, 20000f, 0f, 15));
        s.Own.Add(Unit(2, 0f, 0f, 3000f, 0.9f, 14));
        s.Beliefs.Add(Belief("front", 600f, 0f, 30000f, Today - 1, 0.9f, 10f));
        for (int k = 0; k < 4; k++)
            s.Beliefs.Add(Belief("west" + k, 100f + 60f * k, 300f, 30000f, Today - 25, 0.05f, 300f));
        var sectors = SectorModel.Build(s);
        var plan = new TheaterPlan { Alliance = 0, DaySerial = Today };
        plan.Sectors.Add(new SectorPlan { SectorId = 1, Posture = TheaterPosture.Screen });

        var tasks = Plan(s, sectors, plan);
        int i = Find(tasks, ReconKind.Screen, "screen-posture");
        AssertTrue(i >= 0, "a Screen-posture sector always gets a screen, ahead of older beliefs in its theater");
        AssertEqual(1, tasks[i].SectorId, "for that sector");
        AssertNear(600f - Standoff, tasks[i].HoldX, 0.5f, "on its most threatened approach, at the standoff");
    }

    private static void ProbeWhenMainObjectiveBeliefsStale()
    {
        var s = LineWorld();
        s.Own.Add(Unit(1, 400f, 0f, 30000f, 0f, 15));
        s.Own.Add(Unit(2, 410f, 0f, 4000f, 0.9f, 14));
        s.Beliefs.Add(Belief("stale", 790f, 0f, 8000f, Today - 9, 0.3f, 80f));
        var sectors = SectorModel.Build(s);
        var plan = MainPlan(2, 800f);

        var tasks = Plan(s, sectors, plan);
        int i = Find(tasks, ReconKind.Probe, null);
        AssertTrue(i >= 0, "only a stale belief at the main objective: a probe");
        AssertTrue(tasks[i].HoldX <= 790f - Standoff + 0.01f, "short of the objective at the standoff, got " + tasks[i].HoldX);
        AssertEqual(2, Pick(s, sectors, plan, tasks[i]), "a small group, cavalry first");

        s.Beliefs[0] = Belief("fresh", 790f, 0f, 8000f, Today, 0.9f, 5f);
        AssertEqual(-1, Find(Plan(s, sectors, plan), ReconKind.Probe, null), "a fresh belief there: no probe");
    }

    private static void NoProbeWhileMainEffortInContact()
    {
        var s = LineWorld();
        s.Own.Add(Unit(1, 400f, 0f, 30000f, 0f, 15));
        s.Own.Add(Unit(2, 410f, 0f, 4000f, 0.9f, 14));
        var engaged = Unit(3, 700f, 0f, 20000f, 0f, 15);
        engaged.Flags |= FormationFlags.InBattle;
        s.Own.Add(engaged);
        var sectors = SectorModel.Build(s);
        var plan = MainPlan(2, 800f);
        AssertEqual(-1, Find(Plan(s, sectors, plan), ReconKind.Probe, null), "the main effort is already fighting at the objective");
        s.Own.RemoveAt(2);
        AssertTrue(Find(Plan(s, sectors, plan), ReconKind.Probe, null) >= 0, "without the contact the probe goes");
    }

    // ---- mission ledger ----

    private static void SyncAssignsMissionIdsAndClearsUnassigned()
    {
        var s = World();
        s.Own.Add(Unit(2, 0f, 0f, 3000f, 0.9f, 14));
        s.Own.Add(Unit(3, 50f, 0f, 3000f, 0.9f, 14));
        var ledger = new ReconMissionLedger();
        var tasks = new List<ReconTask> { ScreenTask(0f, 0f, 200f, 0f, -1), ScreenTask(0f, 0f, 0f, 200f, -1) };
        var a = new Allocation { Alliance = 0 };
        a.Assignments.Add(Recon(2, tasks[0]));
        a.Assignments.Add(Recon(3, tasks[1]));
        a.Assignments.Add(new Assignment { InstanceId = 9, Role = FormationRole.Hold, ReconMissionId = -1 });
        ledger.Sync(s, tasks, a);

        AssertEqual(2, ledger.Missions.Count, "one mission per recon assignment");
        AssertTrue(a.Assignments[0].ReconMissionId >= 0 && a.Assignments[1].ReconMissionId >= 0, "ids written back into the allocation");
        AssertTrue(a.Assignments[0].ReconMissionId != a.Assignments[1].ReconMissionId, "distinct ids");
        AssertEqual(-1, a.Assignments[2].ReconMissionId, "non-recon roles untouched");
        AssertEqual("u2:2", ledger.Missions[0].UnitKey, "persisted identity");
        AssertEqual(200f, ledger.Missions[0].HoldX, "hold point from the task");
        AssertEqual(ReconState.Assigned, ledger.Missions[0].State, "new");
        AssertTrue(ledger.IsRecon(2), "on a mission");
        int first = a.Assignments[0].ReconMissionId;

        var b = new Allocation { Alliance = 0 };
        b.Assignments.Add(Recon(2, tasks[0]));
        ledger.Sync(s, tasks, b);
        AssertEqual(first, b.Assignments[0].ReconMissionId, "kept mission keeps its id");
        AssertEqual(ReconState.Cleared, MissionOf(ledger, 3).State, "a mission whose group lost its recon role is cleared");
        AssertFalse(ledger.IsRecon(3), "not on a mission any more");
    }

    private static void ContactRecallsScreen()
    {
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, 20000f, 0f, 15));
        s.Own.Add(Unit(2, 300f, 0f, 3000f, 0.9f, 14));
        var hold = HoldAlloc();
        var ledger = new ReconMissionLedger();
        AddMission(ledger, s, ReconKind.Screen, 2, 300f, 0f, -1);
        s.Beliefs.Add(Belief("old", 305f, 0f, 9000f, Today - 3, 0.6f, 30f));
        ledger.OnReview(s, hold);
        AssertEqual(ReconState.Holding, ledger.Missions[0].State, "a belief last seen days ago is not contact");

        s.Beliefs[0] = Belief("contact", 305f, 0f, 9000f, Today, 1f, 5f);
        ledger.OnReview(s, hold);
        AssertEqual(ReconState.Recalled, ledger.Missions[0].State, "an enemy seen today inside bugle range recalls the screen");
        AssertEqual(Today, ledger.Missions[0].LastContactDay, "contact day stamped");
        AssertEqual(0f, ledger.Missions[0].HoldX, "toward the nearest own Hold/MainEffort group");

        var s2 = World();
        s2.Own.Add(Unit(1, 0f, 0f, 20000f, 0f, 15));
        var fighting = Unit(2, 300f, 0f, 3000f, 0.9f, 14);
        s2.Own.Add(fighting);
        var ledger2 = new ReconMissionLedger();
        AddMission(ledger2, s2, ReconKind.Screen, 2, 300f, 0f, -1);
        fighting.Flags |= FormationFlags.InBattle;
        s2.Own[1] = fighting;
        ledger2.OnReview(s2, hold);
        AssertEqual(ReconState.Recalled, ledger2.Missions[0].State, "a screen in battle is recalled");
    }

    private static void LowSupplyRecallsScreen()
    {
        foreach (var (supply, expected) in new[] { (0.45f, ReconState.Recalled), (0.55f, ReconState.Holding) })
        {
            var s = World();
            var screen = Unit(2, 300f, 0f, 3000f, 0.9f, 14);
            s.Own.Add(screen);
            var ledger = new ReconMissionLedger();
            AddMission(ledger, s, ReconKind.Screen, 2, 300f, 0f, -1);
            screen.Supply01 = supply;   // raw; vanilla's raid abort GetGroupSupplyState < 0.25 (9092) is 0.5 raw
            s.Own[0] = screen;
            ledger.OnReview(s, HoldAlloc());
            AssertEqual(expected, ledger.Missions[0].State, "raw supply " + supply);
        }
    }

    private static void RecalledMissionClearsAtRecallPoint()
    {
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, 20000f, 0f, 15));
        var screen = Unit(2, 300f, 0f, 3000f, 0.9f, 14);
        s.Own.Add(screen);
        var ledger = new ReconMissionLedger();
        AddMission(ledger, s, ReconKind.Screen, 2, 300f, 0f, -1);
        screen.Supply01 = 0.2f;
        s.Own[1] = screen;
        ledger.OnReview(s, HoldAlloc());
        AssertEqual(ReconState.Recalled, ledger.Missions[0].State, "recalled");
        screen.X = 5f;
        s.Own[1] = screen;
        ledger.OnReview(s, HoldAlloc());
        AssertEqual(ReconState.Cleared, ledger.Missions[0].State, "rejoined the main body: cleared");
    }

    private static void MissingFormationClearsMission()
    {
        var s = World();
        s.Own.Add(Unit(2, 300f, 0f, 3000f, 0.9f, 14));
        var ledger = new ReconMissionLedger();
        AddMission(ledger, s, ReconKind.Screen, 2, 300f, 0f, -1);
        s.Own.Clear();
        ledger.OnReview(s, HoldAlloc());
        AssertEqual(ReconState.Cleared, ledger.Missions[0].State, "formation gone");
        AssertFalse(ledger.IsRecon(2), "not on a mission");

        // A retreating group is vanilla's to move: the mission ends rather than recalling it.
        var r = World();
        var retreating = Unit(2, 300f, 0f, 3000f, 0.9f, 14);
        r.Own.Add(retreating);
        var ledger2 = new ReconMissionLedger();
        AddMission(ledger2, r, ReconKind.Screen, 2, 300f, 0f, -1);
        retreating.Flags |= FormationFlags.OnRetreat;
        r.Own[0] = retreating;
        ledger2.OnReview(r, HoldAlloc());
        AssertEqual(ReconState.Cleared, ledger2.Missions[0].State, "retreat clears");
        AssertEqual("retreat", ledger2.Missions[0].Reason, "reason");
    }

    private static void MissionExpiresAfterTwentyDays()
    {
        var ledger = new ReconMissionLedger();
        var s = World();
        s.Own.Add(Unit(2, 300f, 0f, 3000f, 0.9f, 14));
        AddMission(ledger, s, ReconKind.Screen, 2, 300f, 0f, -1);

        var day19 = WorldOn(Today + 19);
        day19.Own.Add(Unit(2, 300f, 0f, 3000f, 0.9f, 14));
        ledger.OnReview(day19, HoldAlloc());
        AssertEqual(ReconState.Holding, ledger.Missions[0].State, "day 19 still on the mission");

        var day20 = WorldOn(Today + 20);
        day20.Own.Add(Unit(2, 300f, 0f, 3000f, 0.9f, 14));
        ledger.OnReview(day20, HoldAlloc());
        AssertEqual(ReconState.Cleared, ledger.Missions[0].State, "cleared after 20 days, to be re-planned");
        AssertEqual(20, ReconMissionLedger.MaxMissionDays, "the plan's MaxMissionDays");
    }

    private static void ProbeEscalateClearsAndWithdrawRecalls()
    {
        var s = World();
        s.Own.Add(Unit(2, 300f, 0f, 3000f, 0.9f, 14));
        s.Own.Add(Unit(3, 0f, 300f, 3000f, 0.9f, 14));
        var ledger = new ReconMissionLedger();
        AddMission(ledger, s, ReconKind.Probe, 2, 300f, 0f, -1);
        AddMission(ledger, s, ReconKind.Probe, 3, 0f, 300f, -1, keep: 2);

        // A probe's own contact is its job: stamped, not recalled; the verdict decides.
        s.Beliefs.Add(Belief("met", 305f, 0f, 3000f, Today, 1f, 5f));
        ledger.OnReview(s, HoldAlloc());
        AssertEqual(ReconState.Holding, MissionOf(ledger, 2).State, "contact alone does not recall a probe");
        AssertEqual(Today, MissionOf(ledger, 2).LastContactDay, "contact stamped");

        var favorable = ContactEvidenceLedger.Build(Evidence(friendly: 9000f, enemy: 3000f));
        AssertTrue(favorable.AllowsEscalation, "fixture: favorable contact allows escalation");
        ledger.OnProbeContact(2, favorable, Today);
        AssertEqual(ReconState.Cleared, MissionOf(ledger, 2).State, "escalate: the probe's job is done, the planner's balance uses the fresh belief");

        var overmatched = ContactEvidenceLedger.Build(Evidence(friendly: 1000f, enemy: 9000f));
        AssertEqual(ContactEvidence.OvermatchedContact, overmatched.Evidence, "fixture: overmatched");
        ledger.OnProbeContact(3, overmatched, Today);
        AssertEqual(ReconState.Recalled, MissionOf(ledger, 3).State, "withdraw: recalled");
    }

    private static void MissionsKeyedByUnitKey()
    {
        var s = World();
        s.Own.Add(Unit(2, 300f, 0f, 3000f, 0.9f, 14));
        var ledger = new ReconMissionLedger();
        AddMission(ledger, s, ReconKind.Screen, 2, 300f, 0f, -1);

        var reloaded = new ReconMissionLedger();
        reloaded.Restore(ledger.Missions);
        var after = World();
        var same = Unit(99, 300f, 0f, 3000f, 0.9f, 14);
        same.UnitKey = "u2:2";                    // same unit, new session instance id
        after.Own.Add(same);
        reloaded.OnReview(after, HoldAlloc());
        AssertEqual(99, reloaded.Missions[0].InstanceId, "rebound by unitKey");
        AssertEqual(ReconState.Holding, reloaded.Missions[0].State, "the mission survives the reload");
        AssertTrue(reloaded.IsRecon(99), "found under the new instance id");
        AssertFalse(reloaded.IsRecon(2), "not under the old one");

        after.Own.Add(Unit(5, 0f, 0f, 3000f, 0.9f, 14));
        var next = new List<ReconTask> { ScreenTask(0f, 0f, 320f, 0f, -1), ScreenTask(0f, 0f, 0f, 300f, -1) };
        var a = new Allocation { Alliance = 0 };
        a.Assignments.Add(Recon(99, next[0]));
        a.Assignments.Add(Recon(5, next[1]));
        reloaded.Sync(after, next, a);
        AssertEqual(ledger.Missions[0].MissionId, a.Assignments[0].ReconMissionId, "the restored mission keeps its id");
        AssertTrue(a.Assignments[1].ReconMissionId > ledger.Missions[0].MissionId, "new ids continue after the restored ones");
    }

    private static void AlliancesTwoToFourIgnored()
    {
        for (int alliance = 2; alliance <= 4; alliance++)
        {
            var s = new CampaignSnapshot();
            s.Reset(alliance, alliance, Today, PlayerCommandClass.NotCareer, -1, true, null);
            s.Supply.Add(new SupplySourceRecord { Id = 1, Kind = SupplySourceKind.Iip, Owner = alliance, Built = true, Condition = 1f });
            s.MinStrengthForMovement = 1000f;
            s.MinMoraleForMovement = 0.3f;
            s.Own.Add(Unit(1, 0f, 0f, 3000f, 0.9f, 14));
            s.Beliefs.Add(Belief("b", 300f, 0f, 9000f, Today - 20, 0.1f, 100f));
            var tasks = new List<ReconTask>();
            ReconPlanner.PlanTasks(s, null, null, null, tasks);
            AssertEqual(0, tasks.Count, "no recon for alliance " + alliance);
            AssertEqual(-1, Pick(s, null, null, ScreenTask(0f, 0f, 200f, 0f, -1)), "no formation for alliance " + alliance);
        }
    }

    private static void ReconNeverReadsDestinations()
    {
        // Vanilla reads enemy destinations (GetLastTransmittedPathPos 127552 at 13121, 13600,
        // 13673, 9099, 94018); the planner's enemy records carry only what a sighting shows.
        foreach (var type in new[] { typeof(EnemyDetection), typeof(EnemyBelief) })
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                AssertFalse(Regex.IsMatch(field.Name, "Waypoint|Destination|Path|Target", RegexOptions.IgnoreCase), type.Name + "." + field.Name + " is no destination");
        foreach (var file in new[] { "ReconPlanner.cs", "ReconMissionLedger.cs", "ReconOrders.cs" })
        {
            string src = Regex.Replace(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/" + file), @"//[^\n]*", ""); // code, not the anchor comments
            AssertFalse(Regex.IsMatch(src, @"GetLastTransmittedPathPos|\bWaypoint[XZ]\b|closestenemyunitfar"), file + " reads no destination");
        }
    }

    // ---- ReconOrders (the G5d executor's cavalry order) ----

    private static void ScoutingOrderOnlyForCavalryScreens()
    {
        AssertEqual(121, ReconOrders.CavalryOrderFor(ReconKind.Screen, ReconState.Holding, 0.8f, WlChainClass.None), "cavalry screen on its mission: Scouting");
        AssertEqual(121, ReconOrders.CavalryOrderFor(ReconKind.Screen, ReconState.Moving, 0.5f, WlChainClass.None), "at the cavalry-majority threshold");
        AssertEqual(-1, ReconOrders.CavalryOrderFor(ReconKind.Screen, ReconState.Holding, 0.3f, WlChainClass.None), "infantry screen: no call");
        foreach (var kind in new[] { ReconKind.Picket, ReconKind.AdvanceGuard, ReconKind.Probe })
            AssertEqual(-1, ReconOrders.CavalryOrderFor(kind, ReconState.Holding, 0.9f, WlChainClass.None), kind + " gets no cavalry order");
    }

    private static void RecallClearsScoutingOrder()
    {
        AssertEqual(120, ReconOrders.CavalryOrderFor(ReconKind.Screen, ReconState.Recalled, 0.8f, WlChainClass.None), "recalled: order 0");
        AssertEqual(120, ReconOrders.CavalryOrderFor(ReconKind.Screen, ReconState.Cleared, 0.8f, WlChainClass.None), "cleared: order 0");
        AssertEqual(120, ReconOrders.CavalryOrderFor(ReconKind.Screen, ReconState.Cleared, 0.2f, WlChainClass.None), "cleared even below the threshold (90738 makes a repeat free)");
    }

    private static void ScoutingOrderNeverReachesPlayerCommand()
    {
        // ChangeCampaignOrders applies through PerformActionForGroupForAllUnitsOnLevel(-1) (90758,
        // 92572-92620): the group and every descendant three levels down, the player's units included.
        foreach (var chain in new[] { WlChainClass.CarryingGroup, WlChainClass.PlayerMapGroup, WlChainClass.PlayerCommand, WlChainClass.CicSide })
            foreach (ReconKind kind in Enum.GetValues(typeof(ReconKind)))
                foreach (ReconState state in Enum.GetValues(typeof(ReconState)))
                    AssertEqual(-1, ReconOrders.CavalryOrderFor(kind, state, 1f, chain), chain + "/" + kind + "/" + state);
    }

    private static void PlayerCommandRefusedLive()
    {
        AssertFalse(ReconOrders.MayAct(WlChainClass.PlayerCommand), "the player's own command");
        AssertFalse(ReconOrders.MayAct(WlChainClass.CicSide), "the CIC player's side");
        AssertTrue(ReconOrders.MayAct(WlChainClass.None), "AI group");
        AssertTrue(ReconOrders.MayAct(WlChainClass.CarryingGroup), "carrying group (moved; F's MoveUnitTo Postfix dispatches)");
        AssertTrue(ReconOrders.MayAct(WlChainClass.PlayerMapGroup), "map-group player (order-only)");
    }

    // ---- directive cleanup ----

    private static void ScreenDirectiveOnlyForReconMissions()
    {
        var snap = Snapshot("corps", 0, 15, 10000f, 0f, FormationLevel.Unknown, FrontPosture.Hold);
        var plain = FormationDirectiveLedger.Build(new[] { snap }, EraStage.Operational1862, null).GetAssignment("corps");
        AssertTrue(plain.Directive != FormationDirective.Screen, "no recon mission: no Screen");
        var recon = FormationDirectiveLedger.Build(new[] { snap }, EraStage.Operational1862, null, null, key => key == "corps").GetAssignment("corps");
        AssertEqual(FormationDirective.Screen, recon.Directive, "on a recon mission: Screen");
        AssertEqual("recon-mission", recon.Reason, "reason");
        AssertFalse(recon.OffensiveAllowed, "kept out of offensives");
        var other = FormationDirectiveLedger.Build(new[] { snap }, EraStage.Operational1862, null, null, key => key == "else").GetAssignment("corps");
        AssertTrue(other.Directive != FormationDirective.Screen, "another unit's mission does not count");
    }

    private static void ScreenMapsToScoutingPerkRole()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Patches/PerkSelectionPatch.cs");
        AssertTrue(Regex.IsMatch(src, @"case FormationDirective\.Screen:\s*return ArmyPerkRole\.Scouting;"), "a recon formation picks scouting perks");
        AssertFalse(src.Contains("RaidSupport"), "the raid-support directive is gone");
    }

    private static void DivisionVsArmyIsDelayNotScreen()
    {
        var snap = Snapshot("division", 1, 14, 4500f, 50000f, FormationLevel.Army, FrontPosture.Hold);
        var assignment = FormationDirectiveLedger.Build(new[] { snap }, EraStage.Amateur1861, null).GetAssignment("division");
        AssertEqual(FormationDirective.Delay, assignment.Directive, "a lone division facing an army delays");
        AssertEqual("division-vs-army-no-support", assignment.Reason, "reason kept");
        AssertFalse(assignment.OffensiveAllowed, "no offensive");
        AssertTrue(assignment.DefensiveAllowed, "defends");
    }

    private static void DefaultFallbackIsHold()
    {
        var snap = Snapshot("corps", 0, 15, 10000f, 0f, FormationLevel.Unknown, FrontPosture.Hold);
        var assignment = FormationDirectiveLedger.Build(new[] { snap }, EraStage.Operational1862, null).GetAssignment("corps");
        AssertEqual(FormationDirective.Hold, assignment.Directive, "the fallback holds");
        AssertEqual("default-hold", assignment.Reason, "reason");
    }

    // ---- fixtures ----

    // Alliance 0, review day 100, one own IIP at the origin, vanilla movement minima 1000 / 0.3.
    private static CampaignSnapshot World(DirectorPosture tempo = null)
    {
        return WorldOn(Today, tempo);
    }

    private static CampaignSnapshot WorldOn(int day, DirectorPosture tempo = null)
    {
        var s = new CampaignSnapshot();
        s.Reset(0, 0, day, PlayerCommandClass.NotCareer, -1, true, tempo);
        s.RelocationRange = 150f;
        s.OffensiveDominance = 1.1f;
        s.MinStrengthForMovement = 1000f;
        s.MinMoraleForMovement = 0.3f;
        s.Supply.Add(new SupplySourceRecord { Id = 1, Kind = SupplySourceKind.Iip, Owner = 0, X = 0f, Z = 0f, TransportCapacity = 10f, Condition = 1f, Built = true });
        return s;
    }

    // Nine areas on the x axis 100 apart (adjacent neighbours only), seeded by the own capital (0),
    // an own rail hub (400) and an enemy-held town (800): sectors {0,1,2} centroid 100, {3..6}
    // centroid 450, {7,8} centroid 750 (East; the others West).
    private static CampaignSnapshot LineWorld(DirectorPosture tempo = null)
    {
        var s = World(tempo);
        for (int i = 0; i <= 8; i++)
            s.Areas.Add(new AreaRecord { Index = i, X = i * 100f, Z = 0f, EdgeX = new[] { i * 100f }, EdgeZ = new[] { 0f }, Importance = 1f, SupplyCapacity = 1000f });
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 1, X = 0f, Z = 0f, HolderAlliance = 0, Roles = ObjectiveRole.Capital, Importance01 = 1f });
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 2, X = 400f, Z = 0f, HolderAlliance = 0, Roles = ObjectiveRole.RailHub, Importance01 = 0.8f });
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 3, X = 800f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = 0.9f });
        return s;
    }

    private static FormationRecord Unit(int id, float x, float z, float strength, float cavalryShare, int unitTyp)
    {
        return new FormationRecord
        {
            InstanceId = id, UnitKey = "u" + id + ":" + id, Name = "u" + id, UnitTyp = unitTyp,
            X = x, Z = z, WaypointX = x, WaypointZ = z, Strength = strength, Morale = 0.8f, Supply01 = 1f,
            ReadinessStep = 3, CommandRange = 5000f, BugleRange = 10f, SightRadius = Sight,
            CavalryShare = cavalryShare, CommanderId = id, ParentInstanceId = -1,
            AreaIndex = (int)Math.Round(Math.Max(0f, Math.Min(800f, x)) / 100f),
            Flags = unitTyp >= 15 ? FormationFlags.FightingForce : FormationFlags.None
        };
    }

    private static EnemyBelief Belief(string key, float x, float z, float strength, int lastSeen, float confidence, float radius)
    {
        return new EnemyBelief
        {
            InstanceId = -1, UnitKey = key + ":0", UnitTyp = 15, X = x, Z = z, Strength = strength,
            LastSeenDay = lastSeen, Confidence01 = confidence, UncertaintyRadius = radius, LastSource = DetectionSource.FogGrid
        };
    }

    private static TheaterPlan MainPlan(int sector, float targetX)
    {
        var plan = new TheaterPlan { Alliance = 0, DaySerial = Today, MainSectorId = sector, MainObjectiveId = 3, MainRequiredRatio = 1.1f };
        plan.Sectors.Add(new SectorPlan { SectorId = sector, Posture = TheaterPosture.MainEffort, TargetObjectiveId = 3, TargetX = targetX, TargetZ = 0f, RequiredRatio = 1.1f });
        return plan;
    }

    private static ReconTask ScreenTask(float fromX, float fromZ, float holdX, float holdZ, int sector)
    {
        return new ReconTask { Kind = ReconKind.Screen, SectorId = sector, FromX = fromX, FromZ = fromZ, HoldX = holdX, HoldZ = holdZ, Priority = 1f, Reason = "test" };
    }

    private static Assignment Recon(int instanceId, in ReconTask task)
    {
        return new Assignment { InstanceId = instanceId, Role = FormationRole.Recon, SectorId = task.SectorId, TargetX = task.HoldX, TargetZ = task.HoldZ, Reason = task.Reason, ReconMissionId = -1 };
    }

    // Unit 1 (when present) holds; the recall point of a recalled mission.
    private static Allocation HoldAlloc()
    {
        var a = new Allocation { Alliance = 0 };
        a.Assignments.Add(new Assignment { InstanceId = 1, Role = FormationRole.Hold, ReconMissionId = -1 });
        return a;
    }

    // Puts one formation on a mission through the real Sync path (keep: other recon formations whose
    // missions stay assigned in the same call).
    private static void AddMission(ReconMissionLedger ledger, CampaignSnapshot s, ReconKind kind, int instanceId, float hx, float hz, int sector, int keep = -1)
    {
        var tasks = new List<ReconTask>();
        var a = new Allocation { Alliance = 0 };
        foreach (var m in ledger.Missions)
        {
            if (m.InstanceId != keep) continue;
            var kept = new ReconTask { Kind = m.Kind, SectorId = m.SectorId, HoldX = m.HoldX, HoldZ = m.HoldZ, Reason = m.Reason };
            tasks.Add(kept);
            a.Assignments.Add(Recon(m.InstanceId, kept));
        }
        var task = new ReconTask { Kind = kind, SectorId = sector, FromX = hx, FromZ = hz, HoldX = hx, HoldZ = hz, Priority = 1f, Reason = "test" };
        tasks.Add(task);
        a.Assignments.Add(Recon(instanceId, task));
        ledger.Sync(s, tasks, a);
    }

    private static ReconMission MissionOf(ReconMissionLedger ledger, int instanceId)
    {
        foreach (var m in ledger.Missions)
            if (m.InstanceId == instanceId) return m;
        throw new Exception("no mission for " + instanceId);
    }

    private static ContactEvidenceInput Evidence(float friendly, float enemy)
    {
        return new ContactEvidenceInput
        {
            ObservingAllianceId = 0, CurrentFriendlyStrength = friendly, CurrentEnemyStrength = enemy,
            PreviousObservedEnemyStrength = enemy, EnemyReactionMultiplier = 1.5f,
            EscalateFriendlyRatio = 1.5f, WithdrawFriendlyRatio = 0.5f, SpatialMaxDistance = 50f, CurrentDaySerial = Today
        };
    }

    private static List<ReconTask> Plan(CampaignSnapshot s, TheaterPlan plan)
    {
        return Plan(s, SectorModel.Build(s), plan);
    }

    private static List<ReconTask> Plan(CampaignSnapshot s, SectorModel sectors, TheaterPlan plan)
    {
        var tasks = new List<ReconTask>();
        ReconPlanner.PlanTasks(s, sectors, plan, null, tasks);
        return tasks;
    }

    private static int Pick(CampaignSnapshot s, SectorModel sectors, TheaterPlan plan, in ReconTask task, bool mainEffortHasScreen = true)
    {
        return ReconPlanner.PickFormation(s, sectors, plan, null, task, new List<int>(), mainEffortHasScreen);
    }

    private static int Find(List<ReconTask> tasks, ReconKind kind, string reason)
    {
        for (int i = 0; i < tasks.Count; i++)
            if (tasks[i].Kind == kind && (reason == null || (tasks[i].Reason ?? "").Contains(reason))) return i;
        return -1;
    }

    private static float NearestBelief(CampaignSnapshot s, float x, float z)
    {
        EnemyBeliefQuery.Nearest(s.Beliefs, x, z, out float d);
        return d;
    }
}
