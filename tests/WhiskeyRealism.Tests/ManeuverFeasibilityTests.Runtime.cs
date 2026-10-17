using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Util;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static TestFixtures;
using static ManeuverFeasibilityFixtures;

// BC14 task 15: the pure halves of the runtime wiring (spec § 4 requirement-navmesh-confirm, requirement-runtime-wiring).
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] RuntimeTests() => new (string, Action)[]
    {
        ("bc14 EXPLOIT-LOOKUP an exploit asset and what is below it are not offered; an ancestor stays, at half strength", ExploitLookupSemantics),
        ("bc14 RUNTIME-WIRED the coordinator tick runs probes, evaluation, monitor and confirmation before the replan; the forced evaluations precede the picks", RuntimeWired),
        ("bc14 RUNTIME-ROWS the core telemetry rows exist under their names and the suppressions are named", RuntimeRows),
        ("bc14 ROUTE-CONFIRM-BUDGET one NavMesh query per side per tick, the selected plan's legs first, an answer cached for 0.25 doctrine hours", RouteConfirmBudget),
    };

    static void RouteConfirmBudget()
    {
        var legs = new ConfirmedLegs();
        legs.SetClock(10.0);
        var asked = new List<(float x0, float z0, float x1, float z1, int crossing)>();
        LegQuery scripted = (float x0, float z0, float x1, float z1, int crossing, out float cost, out bool noPath) =>
        {
            asked.Add((x0, z0, x1, z1, crossing));
            cost = Dist(x0, z0, x1, z1) * 2f;
            noPath = crossing == 99;
            return true;
        };

        // Five legs, three ticks of work: one query each, in the order the legs were asked for (the selected plan's first).
        for (int i = 0; i < 5; i++) legs.Request(100f * i, 0f, 100f * i, 300f, i);
        legs.Request(0f, 0f, 0f, 300f, 0);          // already queued: not twice
        AssertEqual(5, legs.QueuedCount, "five distinct legs queued");
        AssertEqual(1, legs.Process(scripted), "one query in a tick");
        AssertEqual(1, asked.Count, "and only one");
        AssertEqual(0, asked[0].crossing, "the first asked is the first answered");
        AssertEqual(1, legs.Process(scripted), "the next tick");
        AssertEqual(1, asked[1].crossing, "in order");
        AssertEqual(3, legs.QueuedCount, "three left");
        AssertEqual(ConfirmedLegs.ConfirmQueriesPerTick, 1, "the budget is one query per tick");

        // The answers: a cost the next evaluation reads, within the cache hours and not after.
        AssertTrue(legs.TryGet(0f, 0f, 0f, 300f, 0, out float cost, out bool noPath), "an answered leg is a hit");
        AssertNear(600f, cost, 1e-3f, "its polyline cost");
        AssertFalse(noPath, "a complete path");
        AssertTrue(legs.TryGet(0f, 300f, 0f, 0f, 0, out _, out _), "the same leg walked the other way");
        AssertTrue(legs.TryGet(10f, 10f, 10f, 310f, 0, out _, out _), "within the 50-unit buckets");
        AssertFalse(legs.TryGet(0f, 0f, 0f, 300f, 7, out _, out _), "another crossing is another leg");
        legs.SetClock(10.24);
        AssertTrue(legs.TryGet(0f, 0f, 0f, 300f, 0, out _, out _), "0.24 h later it still stands");
        legs.SetClock(10.25);
        AssertFalse(legs.TryGet(0f, 0f, 0f, 300f, 0, out _, out _), "0.25 h later it has expired");

        // A leg with a fresh answer is not asked twice; an expired one is asked again.
        legs.SetClock(10.0);
        legs.Request(0f, 0f, 0f, 300f, 0);
        AssertEqual(3, legs.QueuedCount, "an answered leg inside the hours is not queued again");
        legs.SetClock(10.3);
        legs.ClearQueue();
        legs.Request(0f, 0f, 0f, 300f, 0);
        asked.Clear();
        AssertEqual(1, legs.Process(scripted), "an expired leg is queried again");
        AssertEqual(1, asked.Count, "once");

        // No path is an answer in its own right and is cached as one.
        legs.ClearQueue();
        legs.Request(5f, 5f, 500f, 500f, 99);
        legs.Process(scripted);
        AssertTrue(legs.TryGet(5f, 5f, 500f, 500f, 99, out _, out bool refused), "the refusal is cached");
        AssertTrue(refused, "as a refusal");
        legs.ClearQueue();
        legs.Request(7f, 7f, 700f, 700f, 1);
        LegQuery throwing = (float x0, float z0, float x1, float z1, int crossing, out float c, out bool n) => throw new InvalidOperationException("nav");
        AssertEqual(1, legs.Process(throwing), "a query that throws is a query");
        AssertFalse(legs.TryGet(7f, 7f, 700f, 700f, 1, out _, out _), "but it caches nothing: the leg stays an estimate");
        legs.ClearQueue();
        legs.Request(7f, 7f, 700f, 700f, 1);
        LegQuery failing = (float x0, float z0, float x1, float z1, int crossing, out float c, out bool n) => { c = 0f; n = false; return false; };
        AssertEqual(1, legs.Process(failing), "a query that reports failure is a query");
        AssertFalse(legs.TryGet(7f, 7f, 700f, 700f, 1, out _, out _), "and caches nothing either");

        // The queue is bounded: a full queue drops further legs.
        legs.ClearQueue();
        for (int i = 0; i < ConfirmedLegs.MaxQueued + 10; i++) legs.Request(1000f + 100f * i, 0f, 1000f + 100f * i, 400f, 3);
        AssertEqual(ConfirmedLegs.MaxQueued, legs.QueuedCount, "the queue never grows");

        // From a real evaluation: the selected plan's legs go first, the keys are the ones the route screen asks, and answering them confirms the routes.
        FeasField f = Scenario4();
        var confirmed = new ConfirmedLegs();
        confirmed.SetClock(f.Now);
        f.Legs = confirmed;
        BattlePlanId[] plans = { BattlePlanId.FrontalShockAssault, BattlePlanId.ManeuverAndFix, BattlePlanId.DoubleEnvelopment, BattlePlanId.GenericAggressive, BattlePlanId.WideFlankApproach };
        var ledger = new FeasibilityLedger();
        ManeuverFeasibility.Evaluate(f.Build(plans), ledger);
        ledger.CaptureSelection(BattlePlanId.ManeuverAndFix, true);
        ManeuverFeasibility.Evaluate(f.Build(plans), ledger);
        ConfirmRequests.Collect(ledger, confirmed, f.Regions);
        int queued = confirmed.QueuedCount;
        AssertTrue(queued > 0, "the published routes queue legs");
        CandidateResult selected = Result(ledger, BattlePlanId.ManeuverAndFix);
        MemberEstimate firstMember = ledger.Members(in selected).ToArray()[0];
        ledger.TryChildPosition(firstMember.ChildId, out float childX, out float childZ);
        var firstAsked = new List<(float x0, float z0, float x1, float z1, int crossing)>();
        LegQuery record = (float x0, float z0, float x1, float z1, int crossing, out float c, out bool n) =>
        {
            firstAsked.Add((x0, z0, x1, z1, crossing));
            c = Dist(x0, z0, x1, z1) * 2f;
            n = false;
            return true;
        };
        confirmed.Process(record);
        AssertNear(childX, firstAsked[0].x0, 1f, "the selected plan's first member's leg is the first asked: x");
        AssertNear(childZ, firstAsked[0].z0, 1f, "z");
        int ticks = 1;
        while (confirmed.QueuedCount > 0 && ticks < 200) { confirmed.Process(record); ticks++; }
        AssertEqual(queued, firstAsked.Count, "every queued leg took one query, one per tick");
        AssertEqual(queued, ticks, "so as many ticks as legs");

        var after = new FeasibilityLedger();
        ManeuverFeasibility.Evaluate(f.Build(plans), after);
        after.CaptureSelection(BattlePlanId.ManeuverAndFix, true);
        ManeuverFeasibility.Evaluate(f.Build(plans), after);
        CandidateResult confirmedResult = Result(after, BattlePlanId.ManeuverAndFix);
        bool anyConfirmed = false;
        foreach (BodyEstimate b in after.Bodies(in confirmedResult).ToArray()) anyConfirmed |= b.RouteConfirmed;
        AssertTrue(anyConfirmed, "the next evaluation reads the answers: a body's route is NavMesh-confirmed");
        int again = 0;
        ConfirmRequests.Collect(after, confirmed, f.Regions);
        again = confirmed.QueuedCount;
        AssertEqual(0, again, "and nothing is asked again inside the cache hours");
    }

    static void ExploitLookupSemantics()
    {
        BattleScope.ResetAll();
        ExploitTable table = ExploitTable.ForAlliance(0);
        var entry = new ExploitEntry { Id = 1, AssetId = 50, Phase = ExploitPhase.Advance };
        AssertTrue(table.Add(in entry), "the exploit is live");
        table.SetSubtree(1, new[] { 51, 52 }, new[] { 40 });
        AssertTrue(table.IsAssetOrBelow(50), "the asset");
        AssertTrue(table.IsAssetOrBelow(51), "and what is below it");
        AssertFalse(table.IsAssetOrBelow(40), "an ancestor is not away from its slot");
        AssertFalse(table.IsAssetOrBelow(99), "a stranger is not");
        AssertTrue(table.IsAboveAsset(40), "an ancestor of the asset");
        AssertFalse(table.IsAboveAsset(50), "the asset is not above itself");
        AssertFalse(table.IsAboveAsset(51), "nor what is below it");
        AssertTrue(table.IsExploiting(40) && table.IsExploiting(50) && table.IsExploiting(52), "BC13's own test still answers for all three");
        BattleScope.ResetAll();
        AssertFalse(table.IsAssetOrBelow(50) || table.IsAboveAsset(40), "the battle's end clears them");
    }

    // The coordinator's order of work (spec: probes, evaluation, monitor, replan; the forced evaluation before a pick) and the runtime's seams.
    static void RuntimeWired()
    {
        string coordinator = FlankResponseTests.SourceText("Tactical", "Orchestrator", "TacticalBattleCoordinatorRuntime.cs");
        string flat = FlankResponseTests.Flat(coordinator);
        int feasibility = flat.IndexOf("DriveFeasibility(side, sideBattle); if (Plugin.EnableTacticalOrchestratorIntentInference", StringComparison.Ordinal);
        AssertTrue(feasibility > 0, "the feasibility step runs before the replan cycle");
        int drive = flat.IndexOf("RefreshPickInputs(side, sideBattle);", StringComparison.Ordinal);
        AssertTrue(drive > 0 && drive < feasibility, "after the pick inputs are refreshed");
        FlankResponseTests.AssertPinned(coordinator, "Plays.FeasibilityRuntime.Tick(side, ResolveBattleUnits(sideBattle), battleSnapshot, bundle.OwnEvidence, bundle.EnemyVisible.FrontAxis,",
            "the heavy path hands the runtime the published snapshot's evidence");
        FlankResponseTests.AssertPinned(coordinator, "() => Plays.FeasibilityRuntime.EvaluateNow(side, ResolveBattleUnits(sideBattle), snapshot, firstBundle.OwnEvidence, firstBundle.EnemyVisible.FrontAxis),",
            "the forced evaluation sits between the refresh and the initial pick");
        FlankResponseTests.AssertPinned(coordinator, "evaluate: () => Plays.FeasibilityRuntime.EvaluateNow(side, ResolveBattleUnits(battle), SideSnapshot(side), bundleForReplan.OwnEvidence,",
            "and before the night replan");
        FlankResponseTests.AssertPinned(coordinator, "Plays.FeasibilityRuntime.Attach(army, armyCommander);", "the army's ledger is pushed before the first pick");

        string runtime = FlankResponseTests.SourceText("Tactical", "Orchestrator", "Plays", "FeasibilityRuntime.cs");
        string order = FlankResponseTests.Flat(runtime);
        int classify = order.IndexOf("Classify(st, snapshot, regions, now);", StringComparison.Ordinal);
        int tick = order.IndexOf("FeasibilityTick.Run(ref work);", StringComparison.Ordinal);
        int confirm = order.IndexOf("st.Legs.Process(NavQuery);", StringComparison.Ordinal);
        AssertTrue(classify > 0 && tick > classify && confirm > tick, "probes, then the monitor and the evaluation in FeasibilityTick's order, then the confirmation query");
        FlankResponseTests.AssertPinned(runtime, "public bool MonitorPending => !Forced && (St.MonitorWanted || St.Ledger.MonitorRunning);", "a monitor run is pending when wanted or part-way, never in a forced run");
        FlankResponseTests.AssertPinned(runtime, "using (TelemetryPerf.Scope(\"tactical.feasibility.evaluate\", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))", "the evaluate scope at 2.0 ms");
        FlankResponseTests.AssertPinned(runtime, "using (TelemetryPerf.Scope(\"tactical.feasibility.monitor\", TelemetryLayer.Tactical, TelemetryCategory.Performance, 0.5))", "the monitor scope at 0.5 ms");
        FlankResponseTests.AssertPinned(runtime, "using (TelemetryPerf.Scope(\"tactical.feasibility.navmesh\", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))", "the navmesh scope at 2.0 ms");
        FlankResponseTests.AssertPinned(runtime, "if (!army.HasPlan || !ledger.HasSelection || !ledger.HasEvaluation) { st.MonitorWanted = false; return; }", "the monitor needs a standing plan and a published evaluation");
        FlankResponseTests.AssertPinned(runtime, "now - st.LastStartHours < ManeuverFeasibility.EvaluateIntervalHours", "a new evaluation waits out the interval after the last one's start");
        FlankResponseTests.AssertPinned(FlankResponseTests.SourceText("Tactical", "Orchestrator", "Plays", "ManeuverFeasibility.Candidates.cs"),
            "EvaluateIntervalHours = 0.05f, EvaluateSliceMs = 1.5f", "the spec's evaluation interval (0.05 h) and slice (1.5 ms)");
        // One slice budget per tick, shared by the evaluation and the monitor; the monitor resumes a part-run on the next tick.
        FlankResponseTests.AssertPinned(runtime, "Evaluate(St, Side, Army, Bunits, Snapshot, Evidence, Axis, Regions, Now, Forced, Deadline);", "the evaluation takes the tick's deadline");
        FlankResponseTests.AssertPinned(runtime, "Monitor(St, Side, Army, Bunits, Snapshot, Evidence, Axis, Regions, Now, Deadline);", "and so does the monitor");
        FlankResponseTests.AssertPinned(FlankResponseTests.SourceText("Tactical", "Orchestrator", "Plays", "FeasibilityTick.cs"),
            "if (work.MonitorPending) work.RunMonitor();", "the monitor goes first (FEAS-TICK-ORDER runs it)");
        FlankResponseTests.AssertPinned(runtime, "if (!ManeuverFeasibility.EvaluateSelectedSlice(in input, ledger, deadline, Stopwatch.GetTimestamp)) return;", "a monitor run that ran out of budget waits for the next tick");
        FlankResponseTests.AssertPinned(runtime, "st.MonitorWanted = true;", "a new heavy snapshot asks for a monitor run");
        FlankResponseTests.AssertPinned(runtime, "army.SetFeasibility(Plugin.ManeuverFeasibilityEnabled() ? st.Ledger : null);", "the ledger is pushed, null with the flag off");
        FlankResponseTests.AssertPinned(runtime, "OnceLog.Warning(\"tactical-battle-core:feasibility-nav-failed\"", "a failed confirmation leaves the leg an estimate and says so once");
        FlankResponseTests.AssertPinned(runtime, "OnceLog.Warning(\"tactical-battle-core:feasibility-daylight-missing\"", "a missing storm system means no daylight term and says so once");
        FlankResponseTests.AssertPinned(runtime, "return double.PositiveInfinity;", "no daylight term is an unbounded day");
        AssertFalse(FlankResponseTests.Flat(runtime).Contains("EnemyRecords") || runtime.Contains("scheduledarrival") || runtime.Contains("durationleft"), "the runtime reads no hidden enemy state");

        string army = FlankResponseTests.SourceText("Tactical", "Orchestrator", "ArmyOrchestrator.cs");
        FlankResponseTests.AssertPinned(army, "Plays.FeasibilityTelemetry.PlanSelection(AllianceId, trigger, kept, ctx.Stance, ctx.CurrentOdds, _selection, pb.Id, nextPlan.Mirrored, ledgerAge, now, _feasibility != null ? _feasibility.Willingness : -1f);",
            "every pick writes its selection row, with the flag off too");
        FlankResponseTests.AssertPinned(army, "PickInitialPlan(ArmyEvidence evidence)", "the initial pick");
        string catalog = FlankResponseTests.SourceText("Tactical", "Orchestrator", "TacticalPlaybookCatalog.cs");
        FlankResponseTests.AssertPinned(catalog, "report.StanceFiltered = stanceFiltered;", "the report counts the stance filter on its own");
        FlankResponseTests.AssertPinned(catalog, "report.CrestFiltered = crestFiltered;", "and the crest filter");
    }

    static void RuntimeRows()
    {
        string telemetry = FlankResponseTests.SourceText("Tactical", "Orchestrator", "Plays", "FeasibilityTelemetry.cs");
        foreach (string row in new[] { "TacticalPlanSelection", "TacticalManeuverFeasibility", "TacticalManeuverBody", "TacticalReplan", "TacticalCrossingWatch" })
            AssertTrue(telemetry.Contains("\"" + row + "\""), row + " is emitted");
        // The spec's row fields (telemetry § Rows): a body row carries each risk term and the times that feed them, a candidate row its verdict.
        foreach (string field in new[] { "doctrineHours", "plan", "body", "child", "kind", "strength", "routeUnits", "routeSource", "marchVia", "attackVia", "marchViaState",
            "attackViaState", "strikeContested", "courierHours", "marchHours", "inwardSurplusHours", "strikeHours", "desiredStrikeHours", "releaseHours", "enemyShiftHours",
            "windowHours", "pin", "enemyAgainst", "isolatedOdds", "bar", "ownRatio", "daylight", "detail", "contested", "approach", "exposure", "turn", "penalty", "assumption",
            "suppression", "mirrored", "maxHeldStrength", "verdict", "reason", "candidates", "impossible", "risky", "selected", "slices", "forced", "queries", "legsSampled", "legOverflow", "confirmQueued" })
            AssertTrue(telemetry.Contains("WithField(\"" + field + "\""), "the feasibility rows carry " + field);
        foreach (string key in new[] { "feasibility-first", "feasibility-filtered", "feasibility-replan", "feasibility-via" })
            AssertTrue(telemetry.Contains("tactical-battle-core:" + key), "the once-log " + key + " exists");
        foreach (string suppression in new[] { "SuppressedByCooldown", "SuppressedByMargin", "SuppressedByPreemption", "SuppressedByCommitment" })
            AssertTrue(FlankResponseTests.SourceText("Tactical", "Orchestrator", "ArmyTickCycle.cs").Contains(suppression), suppression + " is a named suppression");
        string runtime = FlankResponseTests.SourceText("Tactical", "Orchestrator", "Plays", "FeasibilityRuntime.cs");
        FlankResponseTests.AssertPinned(runtime, "ReplanOutcome.SuppressedByCommitment", "a committed plan's unspent reason is named on the body row");
        string play = FlankResponseTests.SourceText("Tactical", "Orchestrator", "Plays", "PlayExecutorRuntime.cs");
        foreach (string field in new[] { "doctrineHours", "plannedReleaseHours", "timeBudgetHours", "arrivedHours", "predictedArrivalHours", "predictedMarchHours" })
            AssertTrue(play.Contains("\"" + field + "\""), "TacticalEnvelopment carries " + field);
        string army = FlankResponseTests.SourceText("Tactical", "Orchestrator", "ArmyOrchestrator.cs");
        foreach (string field in new[] { "crossing", "guardX", "guardZ", "guardHeading", "ownShiftHours", "enemyArrivalHours", "held", "floor", "reverted" })
            AssertTrue(army.Contains("\"" + field + "\""), "TacticalReserveRelease carries " + field);
    }
}
