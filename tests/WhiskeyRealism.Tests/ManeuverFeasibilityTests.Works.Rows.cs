using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using WhiskeyRealism.Util;
using static TestHarness;
using static TestFixtures;
using static ManeuverFeasibilityFixtures;

// BC14W task 10: the runtime wiring and the telemetry rows (spec requirement-telemetry, requirement-runtime-wiring). The rows go through the TelemetryRouter,
// so what is tested is what can be: the throttles that decide when a row is due (pure), the facts the rows read from the ledger, the fields each row
// carries, and the call sites that feed them.
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] WorksRowTests() => new (string, Action)[]
    {
        ("bc14w WORKS-ROW-DUE the works rows are due every 0.1 doctrine-h, the hold row on each change of the gate and every 0.1 h while it holds", WRowDue),
        ("bc14w WORKS-ROW-FIELDS every BC14W row carries the fields the spec names, and the existing rows gain theirs", WRowFields),
        ("bc14w WORKS-WILLINGNESS the ledger reports the army commander's willingness for the selection row, -1 with no evaluation or the flag off", WWillingness),
        ("bc14w WORKS-WIRING the runtime feeds the evaluation the side's works pieces, probes them in the crossing probes' handle and writes the rows", WWiring),
    };

    static void WRowDue()
    {
        BattleScope.ResetAll();
        // TacticalEnemyWorks: the first row at once, then 0.1 doctrine-h apart; a side's throttle is its own.
        AssertTrue(FeasibilityTelemetry.EnemyWorksRowDue(0, 10.0), "the first row is due");
        AssertFalse(FeasibilityTelemetry.EnemyWorksRowDue(0, 10.05), "not 0.05 h later");
        AssertTrue(FeasibilityTelemetry.EnemyWorksRowDue(1, 10.05), "the other side has its own clock");
        AssertTrue(FeasibilityTelemetry.EnemyWorksRowDue(0, 10.12), "0.12 h after the last row");

        // TacticalWorksHold: nothing while there is no hold and none was said; a row on each change, and every 0.1 h while it holds.
        const string hopeless = FeasibilityLedger.ReasonHopelessAssault, unstruck = FeasibilityLedger.ReasonTurnNotStruck;
        AssertFalse(FeasibilityTelemetry.WorksHoldRowDue(0, false, null, 10.0), "no hold, nothing said: no row");
        AssertFalse(FeasibilityTelemetry.WorksHoldRowDue(0, false, null, 11.0), "and none later");
        AssertTrue(FeasibilityTelemetry.WorksHoldRowDue(0, true, hopeless, 11.0), "a hold begins: a row");
        AssertFalse(FeasibilityTelemetry.WorksHoldRowDue(0, true, hopeless, 11.05), "it holds, 0.05 h later: none");
        AssertTrue(FeasibilityTelemetry.WorksHoldRowDue(0, true, hopeless, 11.12), "0.12 h later: a row while it holds");
        AssertTrue(FeasibilityTelemetry.WorksHoldRowDue(0, true, unstruck, 11.13), "the reason changes: a row at once");
        AssertTrue(FeasibilityTelemetry.WorksHoldRowDue(0, false, null, 11.14), "the hold lifts: a row");
        AssertFalse(FeasibilityTelemetry.WorksHoldRowDue(0, false, null, 12.0), "and then none");
        AssertTrue(FeasibilityTelemetry.WorksHoldRowDue(1, true, unstruck, 11.14), "the other side's gate is its own");
        BattleScope.ResetAll();
        AssertTrue(FeasibilityTelemetry.EnemyWorksRowDue(0, 0.0), "the battle's end resets the throttles");
        AssertTrue(FeasibilityTelemetry.WorksHoldRowDue(0, true, hopeless, 0.0), "(the hold row's too)");
        BattleScope.ResetAll();
        // A pin changes the gate too: a row when the first fix is pinned with no hold, none while it stays so, a row when the pin lifts.
        AssertTrue(FeasibilityTelemetry.WorksHoldRowDue(1, false, null, 20.0, pinned: true), "a pin with no hold is a change of the gate: a row");
        AssertFalse(FeasibilityTelemetry.WorksHoldRowDue(1, false, null, 20.5, pinned: true), "and then none while it stays pinned");
        AssertTrue(FeasibilityTelemetry.WorksHoldRowDue(1, false, null, 20.6), "the pin lifts: a row");
        BattleScope.ResetAll();
    }

    static void WRowFields()
    {
        string telemetry = FlankResponseTests.SourceText("Tactical", "Orchestrator", "Plays", "FeasibilityTelemetry.cs");
        string enemyWorks = FlankResponseTests.Between(telemetry, "public static void EnemyWorks(", "public static void WorksHold(");
        AssertTrue(enemyWorks.Contains("\"TacticalEnemyWorks\""), "TacticalEnemyWorks is emitted");
        foreach (string field in new[] { "segments", "heldSegments", "openEnds", "anchoredEnds", "salients", "junctions", "worksSamples", "worksPointsPerceived", "worksProbes",
            "worksPointsDropped", "focalX", "focalZ", "focalKind", "focalScore", "stationFocalX", "stationFocalZ", "stationChild", "doctrineHours" })
            AssertTrue(enemyWorks.Contains("WithField(\"" + field + "\""), "TacticalEnemyWorks carries " + field);
        FlankResponseTests.AssertPinned(enemyWorks, "TelemetryCategory.State", "a State row");

        string hold = FlankResponseTests.Between(telemetry, "public static void WorksHold(", "/// <summary>The first wing routed");
        AssertTrue(hold.Contains("\"TacticalWorksHold\""), "TacticalWorksHold is emitted");
        foreach (string field in new[] { "hold", "reason", "plan", "detail", "barUsed", "assaultChildren", "facingChildren", "pinnedChildren", "guarded", "advancesOnWorks", "bandEntries",
            "vanillaChainContest", "onsetContests", "doctrineHours" })
            AssertTrue(hold.Contains("WithField(\"" + field + "\""), "TacticalWorksHold carries " + field);
        FlankResponseTests.AssertPinned(hold, "TelemetryCategory.Decision", "a Decision row");
        FlankResponseTests.AssertPinned(hold, ".TakeCounts(out chain, out onset, out advances, out entries)", "the contest's counts are read and cleared by the row");

        foreach (string field in new[] { "assaultsWorks", "barUsed", "enemyOpen" })
            AssertTrue(telemetry.Contains("WithField(\"" + field + "\""), "TacticalManeuverBody carries " + field);
        AssertTrue(telemetry.Contains("WithField(\"willingness\""), "TacticalPlanSelection carries willingness");

        string executor = FlankResponseTests.SourceText("Tactical", "Battle", "OrderExecutorRuntime.cs");
        string bombard = FlankResponseTests.Between(executor, "private static bool StartBombard(", "// Artillery limber / unlimber");
        AssertTrue(bombard.Contains("\"TacticalBombardStart\""), "TacticalBombardStart is emitted");
        foreach (string field in new[] { "battery", "pointX", "pointZ", "range", "plan", "outcome" })
            AssertTrue(bombard.Contains("WithField(\"" + field + "\""), "TacticalBombardStart carries " + field);
        FlankResponseTests.AssertPinned(bombard, "EmitBombardStart(unit, x, z, \"issued\");", "after the writes, an issued start says so");
        FlankResponseTests.AssertPinned(bombard, "EmitBombardStart(unit, x, z, \"refused-by-guard\");", "a start the guard refused says so");
        AssertTrue(bombard.IndexOf("unit.bombardrange = ", StringComparison.Ordinal) < bombard.IndexOf("EmitBombardStart(unit, x, z, \"issued\");", StringComparison.Ordinal),
            "and only once the two field writes were made");

        string play = FlankResponseTests.SourceText("Tactical", "Orchestrator", "Plays", "PlayExecutorRuntime.cs");
        foreach (string field in new[] { "focalKind", "preparationHours", "preparationFloorHours", "formation" })
            AssertTrue(play.Contains("WithField(\"" + field + "\""), "TacticalEnvelopment carries " + field);
        string army = FlankResponseTests.SourceText("Tactical", "Orchestrator", "ArmyOrchestrator.cs");
        FlankResponseTests.AssertPinned(army, "\"works-station\"", "TacticalReserveRelease names the works station");
        foreach (string field in new[] { "focalX", "focalZ" })
            AssertTrue(army.Contains("WithField(\"" + field + "\""), "the works-station row carries " + field);
    }

    static void WWillingness()
    {
        FeasField f = new FeasField();
        f.AddCorps(-300f, 0f, 12000f);
        f.AddCorps(0f, 0f, 11000f);
        f.AddCorps(300f, 0f, 12000f);
        f.AddEnemyLine(25000f);
        f.Army = FeasField.Personality(0.9f, 0.1f, 0.7f);
        AssertNear(-1f, new FeasibilityLedger().Willingness, 1e-6f, "no evaluation: -1");
        FeasibilityLedger ledger = f.Evaluate(AllPlans);
        AssertNear(0.5f * 0.9f + 0.5f * 0.7f - 0.5f * 0.1f, ledger.Willingness, 1e-5f, "0.5 agg + 0.5 audacity - 0.5 caution of the army commander");
        bool saved = WhiskeyRealism.Plugin.EnemyWorksDoctrine;
        try
        {
            WhiskeyRealism.Plugin.EnemyWorksDoctrine = false;
            AssertNear(-1f, ledger.Willingness, 1e-6f, "the flag off: -1");
        }
        finally { WhiskeyRealism.Plugin.EnemyWorksDoctrine = saved; }
    }

    static void WWiring()
    {
        string runtime = FlankResponseTests.SourceText("Tactical", "Orchestrator", "Plays", "FeasibilityRuntime.cs");
        string classify = FlankResponseTests.Between(runtime, "private static void Classify(", "private static void Evaluate(");
        FlankResponseTests.AssertPinned(classify, "WorksPointTable works = FortificationPointsRuntime.TableOf(st.Alliance);", "the side's works table is probed");
        FlankResponseTests.AssertPinned(classify, "works?.ProbeStep(ground.Oracle, WorksRules.MaxWorksProbesPerTick);", "in the crossing probes' handle");
        AssertTrue(classify.IndexOf("PerceivedGroundRuntime.Open(", StringComparison.Ordinal) == classify.LastIndexOf("PerceivedGroundRuntime.Open(", StringComparison.Ordinal),
            "one handle for both");
        string input = FlankResponseTests.Between(runtime, "private static void BuildInput(", "// The direct children in the army's own order");
        FlankResponseTests.AssertPinned(input, "FortificationPointsRuntime.Refresh(bunits, st.Alliance, axis.OriginX, axis.OriginZ)", "a new evaluation re-reads the battle's lines");
        FlankResponseTests.AssertPinned(input, "FortificationPointsRuntime.TableOf(st.Alliance)", "the monitor reads the table as it stands");
        FlankResponseTests.AssertPinned(input, "Cache = st.Cache, Works = works,", "the input carries the side's works points");
        FlankResponseTests.AssertPinned(runtime, "ExploitTable.ForAlliance(army.AllianceId).SetWorksView(st.Ledger.WorksHold(out _), st.Ledger.HeldWorks);", "the exploit table's works view, each heavy tick");
        FlankResponseTests.AssertPinned(runtime, "FeasibilityTelemetry.EnemyWorks(army.AllianceId, st.Ledger, FortificationPointsRuntime.TableOf(army.AllianceId), now);", "the works row");
        FlankResponseTests.AssertPinned(runtime, "FeasibilityTelemetry.WorksHold(army.AllianceId, st.Ledger, army.WorksGuarded, now);", "the hold row, with what the guard rewrote");
        string guarded = FlankResponseTests.SourceText("Tactical", "Orchestrator", "ArmyOrchestrator.cs");
        FlankResponseTests.AssertPinned(guarded, "_guardedStates = 0;", "the count restarts with each store");
        FlankResponseTests.AssertPinned(guarded, "if (worksHold && WorksHoldGuard.Apply(in order, WorksContextOfNode(order.NodeId), out order)) _guardedOrderCount++;", "the guard pass counts the orders it rewrote");
        string recon = FlankResponseTests.SourceText("Tactical", "Battle", "Producers", "ReconIntentProducer.cs");
        FlankResponseTests.AssertPinned(recon, "p.Works = army?.Feasibility?.HeldWorks;", "recon gets the side's works query (null with the flag off)");
    }
}
