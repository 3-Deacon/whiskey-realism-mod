using System;
using System.Collections.Generic;
using System.Linq;
using WhiskeyRealism;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static TestFixtures;
using static BattleMovementHonestyTests;
using static ManeuverFeasibilityFixtures;
using static WorksFixtures;

/// <summary>
/// BC14O task 6: the objective doctrine's rows (requirement-telemetry), its time bound (FEAS-TIME-O) and its allocation bound (FEAS-ALLOC-O).
/// FEAS-TIME-O and FEAS-ALLOC-O are green first (bounds on code written in tasks 2 to 5); the plants "the objective table routed from every child" and
/// "the objective table allocated per evaluation" are their red controls (mutation.txt).
/// </summary>
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] ObjectiveBoundsTests() => new (string, Action)[]
    {
        ("bc14o OBJ-ROWS the pressure row every 0.1 h and the main-effort row on a change, a contest and the heartbeat; the contrast names the best feasible and the nearest", ObjRows),
        ("bc14o OBJ-ROWS-FIELDS the two rows carry the spec's fields and the heavy path emits them under the flag", ObjRowsFields),
        ("bc14o FEAS-TIME-O the objective table adds at most 96 path searches and keeps the legs under MaxLegCache", FeasTimeObjectives),
        ("bc14o FEAS-ALLOC-O a warmed objective table of 16, the pressure, the pick and the chains allocate nothing", FeasAllocObjectives),
    };

    static void ObjRows()
    {
        // The pressure row: every 0.1 doctrine-h, per side, taken when due.
        AssertTrue(FeasibilityTelemetry.PressureRowDue(0, 5.00), "the first pressure row is due");
        AssertFalse(FeasibilityTelemetry.PressureRowDue(0, 5.09), "0.09 h later: none");
        AssertTrue(FeasibilityTelemetry.PressureRowDue(0, 5.11), "0.11 h later: a row");
        AssertTrue(FeasibilityTelemetry.PressureRowDue(1, 5.11), "the other side keeps its own clock");

        // The main-effort row: a first pick, a change, a chain contest and the 0.25 h heartbeat; never without an objective.
        int c;
        FeasibilityTelemetry.MainEffortRowDue(0, null, 0, 0.0, out _);
        AssertTrue(FeasibilityTelemetry.MainEffortRowDue(0, "A", 0, 10.00, out c), "a first pick: a row");
        AssertFalse(FeasibilityTelemetry.MainEffortRowDue(0, "A", 0, 10.10, out _), "the same objective 0.10 h later: none");
        AssertTrue(FeasibilityTelemetry.MainEffortRowDue(0, "B", 0, 10.12, out _), "the objective changes: a row at once");
        AssertFalse(FeasibilityTelemetry.MainEffortRowDue(0, "B", 0, 10.20, out _), "and none after");
        AssertFalse(FeasibilityTelemetry.MainEffortRowDue(0, null, 1, 10.205, out _), "a contest with no objective picked gives no row, and stays pending");
        AssertTrue(FeasibilityTelemetry.MainEffortRowDue(0, "B", 2, 10.21, out c), "a chain move contested since the last row: a row");
        AssertEqual(3, c, "it reports all the contests since the last row, those pending included");
        AssertFalse(FeasibilityTelemetry.MainEffortRowDue(0, "B", 0, 10.30, out c), "and none after: the row took the contests");
        AssertEqual(0, c, "nothing to report on a row that is not due");
        AssertTrue(FeasibilityTelemetry.MainEffortRowDue(0, "B", 0, 10.50, out c), "the heartbeat: 0.25 h after the last row");
        AssertEqual(0, c, "with no contest to report");
        AssertFalse(FeasibilityTelemetry.MainEffortRowDue(0, null, 0, 10.90, out _), "no pick: no row");
        AssertTrue(FeasibilityTelemetry.MainEffortRowDue(0, "B", 0, 10.91, out _), "a pick that lapsed and came back is a change");
        FeasibilityTelemetry.MainEffortRowDue(1, null, 0, 0.0, out _);

        // The contrast: the best-valued row the side could take (r below RiskyPenalty), and the nearest it does not hold.
        FeasField f = ObjField();
        f.AddObjective("near", 0f, 300f, 20f);
        f.AddObjective("rich", 300f, 700f, 100f);
        f.AddObjective("mine", 0f, 100f, 100f, own: true, threatened: true);
        FeasibilityLedger ledger = f.Evaluate();
        ObjectiveContrast contrast = ObjectiveScoring.Contrast(ledger, ledger.Job.ArmyX, ledger.Job.ArmyZ);
        AssertEqual("rich", contrast.MaxFeasibleId, "the best-valued feasible row is the 100-point objective");
        AssertNear(Row(ledger, "rich").Value, contrast.MaxFeasibleValue, 1e-6f, "with its value");
        AssertEqual("near", contrast.NearestId, "the nearest the side does not hold: the own-held one, nearer still, is not it");
        // A row too risky to take is not the best feasible one.
        FeasField river = ObjField(HorizontalBand(470f, 490f));
        river.AddObjective("near", 0f, 300f, 20f);
        river.AddObjective("rich", 0f, 900f, 100f);
        ObjectiveContrast across = ObjectiveScoring.Contrast(river.Evaluate(), 0f, 0f);
        AssertEqual("near", across.MaxFeasibleId, "an objective across a river with no crossing is not the best feasible one");
        ObjectiveContrast none = ObjectiveScoring.Contrast(new FeasibilityLedger(), 0f, 0f);
        AssertTrue(none.MaxFeasibleId == null && none.NearestId == null && none.MaxFeasibleValue == 0f, "an empty table names nothing");
    }

    static void ObjRowsFields()
    {
        string rows = ReadRepoFile("src/WhiskeyRealism/Tactical/Orchestrator/Plays/FeasibilityTelemetry.Objectives.cs");
        foreach (string field in new[] { "sOwn", "sEnemy", "bar", "daylightHours", "behind", "ahead", "late", "U", "H", "dueChains" })
            AssertTrue(rows.Contains(".WithField(\"" + field + "\""), "TacticalObjectivePressure carries " + field);
        foreach (string field in new[] { "objective", "points", "value", "r", "prior", "hold", "U", "H", "score", "runnerUp", "runnerUpScore", "maxFeasibleValueObjective",
            "maxFeasibleValue", "nearestObjective", "band", "secondaryScreens", "vanillaChainContest" })
            AssertTrue(rows.Contains(".WithField(\"" + field + "\""), "TacticalMainEffort carries " + field);
        AssertTrue(rows.Contains("\"TacticalObjectivePressure\"") && rows.Contains("TelemetryCategory.State"), "the pressure row is a State row");
        AssertTrue(rows.Contains("\"TacticalMainEffort\"") && rows.Contains("TelemetryCategory.Decision"), "the main-effort row is a Decision row");
        AssertTrue(rows.Contains("contest.TakeChainReissues()"), "the contest count is read and cleared by the row");
        string runtime = ReadRepoFile("src/WhiskeyRealism/Tactical/Orchestrator/Plays/FeasibilityRuntime.cs");
        AssertTrue(runtime.Contains("if (Plugin.ObjectiveDoctrineEnabled())") && runtime.Contains("FeasibilityTelemetry.ObjectivePressure(army.AllianceId, st.Ledger, now);") &&
            runtime.Contains("FeasibilityTelemetry.MainEffort(army.AllianceId, st.Ledger, army.CurrentObjectivePick, band, army.SecondaryScreens, now);"),
            "the heavy path emits both rows under the doctrine flag");
    }

    // A fixture's 16 objectives spread over the front, enemy-held, each with its own points.
    static void AddSixteen(FeasField f)
    {
        for (int i = 0; i < 16; i++) f.AddObjective("o" + i, -700f + i * 93f, 300f + (i % 4) * 160f, 10f + i * 5f);
    }

    // The counted bound: the objective table adds at most 5 members x 16 objectives + 16 shift times = 96 path searches to FEAS-TIME-W's 8-child
    // Meeting fixture with FocalAssault, whose legs stay at 1,920 + 638 + 96 = 2,654 and under the cache.
    static void FeasTimeObjectives()
    {
        FeasibilityJob without = EvaluateTimeFixture(0, out _), with = EvaluateTimeFixture(16, out FeasibilityLedger ledger);
        AssertEqual(16, ledger.ObjectiveCount, "control: sixteen objectives are in the table");
        int added = with.Graph.Searches - without.Graph.Searches;
        AssertTrue(added > 0, "control: the table routes: " + added + " searches");
        AssertTrue(added <= 96, "at most 96 path searches more, got " + added);
        AssertTrue(with.Graph.Searches <= 638 + 96, "at most 734 path searches in all, got " + with.Graph.Searches);
        AssertTrue(with.Graph.LegsSampled <= 2654, "at most 2,654 sampled legs, got " + with.Graph.LegsSampled);
        AssertTrue(with.Graph.Cache.Count < LegCostCache.MaxLegCache, "and the legs stay under MaxLegCache: " + with.Graph.Cache.Count);
        Console.WriteLine("FEAS-TIME-O added=" + added + " searches=" + with.Graph.Searches + " legs=" + with.Graph.LegsSampled + " cache=" + with.Graph.Cache.Count);
    }

    static FeasibilityJob EvaluateTimeFixture(int objectives, out FeasibilityLedger ledger)
    {
        var fords = new (float lateral, float depth)[10];
        for (int i = 0; i < fords.Length; i++) fords[i] = (0f, -900f + i * 200f);
        BattleTerrainGrid grid = HorizontalBandWithFordsLateral(150f, 170f, -1000f, 800f, fords.Length);
        var f = new FeasField(grid);
        for (int c = 0; c < 8; c++) f.AddCorps(-420f + c * 120f, 0f, 6000f, regiments: 2);
        f.AddEnemy(-450f, 600f, 12000f, cover: 0.40f, coverObject: 7, standing: true);
        f.AddEnemy(0f, 600f, 12000f, cover: 0.40f, coverObject: 7, standing: true);
        f.AddEnemy(450f, 600f, 12000f, cover: 0.40f, coverObject: 7, standing: true);
        f.WorksPoints = new Points().Line(-600f, 600f, 50f, l => 600f);
        f.Stance = BattleStance.Meeting;
        if (objectives > 0) AddSixteen(f);
        ledger = new FeasibilityLedger();
        FeasibilityInput input = f.Build(AllPlansWithFocal);
        ManeuverFeasibility.Evaluate(in input, ledger);
        return ledger.Job;
    }

    static void FeasAllocObjectives()
    {
        FeasField f = ObjField();
        AddSixteen(f);
        f.Clock = new VictoryClock { ShareOwn = 0.2f, ShareEnemy = 0.6f, Bar = 0.4f, DaylightHours = 2f };
        f.DueChains.Add("o3");
        f.DueChains.Add("o9");
        var ledger = new FeasibilityLedger();
        FeasibilityInput input = f.Build(AllPlans);
        ManeuverFeasibility.Evaluate(in input, ledger);
        ManeuverFeasibility.Evaluate(in input, ledger);
        long ticks = 0;
        Func<long> clock = () => ticks++;
        while (!ManeuverFeasibility.EvaluateSlice(in input, ledger.Job, ledger, ticks + 1, clock)) { }
        AssertEqual(16, ledger.ObjectiveCount, "control: the table holds sixteen rows");

        // The pick over the same sixteen, with the director's terms.
        var list = new BattlefieldObjectiveEstimate[16];
        for (int i = 0; i < 16; i++) list[i] = Target("o" + i, -700f + i * 93f, 300f + (i % 4) * 160f, value: 0.8f);
        TacticalOperationDirectorInput director = DirectorInput(list, ledger, "o5");
        ObjectivePick pick = TacticalOperationDirector.PickMainEffortObjective(director);
        pick = TacticalOperationDirector.PickMainEffortObjective(director);
        AssertTrue(pick.Has, "control: the pick chose one");
        var secondaries = new SecondaryObjective[ObjectiveScoring.MaxSecondaryScreens];
        ObjectiveScoring.Secondaries(ledger, pick.Objective.ObjectiveId, secondaries);
        ObjectiveScoring.Contrast(ledger, ledger.Job.ArmyX, ledger.Job.ArmyZ);

        // The chains' due list over prebuilt chains and one mapping delegate.
        var heads = new Objectives[4];
        var chains = new AIBattle.ObjectiveChain[4];
        for (int i = 0; i < 4; i++)
        {
            heads[i] = UninitialisedObjective("c" + i, 1, 10);
            chains[i] = Chain(3, (heads[i], 1f));
        }
        var ids = new[] { "o0", "o1", "o2", "o3" };
        Func<Objectives, string> idOf = o => ids[Array.IndexOf(heads, o)];
        var due = new string[ManeuverFeasibility.MaxObjectives];
        ObjectiveChains.CollectDue(chains, 10f, idOf, due);

        long mark = BeginAllocationWindow();
        ManeuverFeasibility.Evaluate(in input, ledger);
        long full = AllocatedSince(mark);
        AssertEqual(0L, full, "one full evaluation with the objective table allocated " + full + " bytes");

        ticks = 0;
        mark = BeginAllocationWindow();
        while (!ManeuverFeasibility.EvaluateSlice(in input, ledger.Job, ledger, ticks + 1, clock)) { }
        long sliced = AllocatedSince(mark);
        AssertEqual(0L, sliced, "an evaluation in slices allocated " + sliced + " bytes");

        long[] parts = new long[5];
        mark = BeginAllocationWindow();
        ObjectivePick again = TacticalOperationDirector.PickMainEffortObjective(director);
        parts[0] = AllocatedSince(mark);
        mark = BeginAllocationWindow();
        ObjectiveScoring.Secondaries(ledger, again.Objective.ObjectiveId, secondaries);
        parts[1] = AllocatedSince(mark);
        mark = BeginAllocationWindow();
        ObjectiveScoring.Contrast(ledger, ledger.Job.ArmyX, ledger.Job.ArmyZ);
        parts[2] = AllocatedSince(mark);
        mark = BeginAllocationWindow();
        ObjectivePressure pressure = ObjectiveScoring.Pressure(f.Clock.Value, 0.5f);
        parts[3] = AllocatedSince(mark);
        mark = BeginAllocationWindow();
        int count = ObjectiveChains.CollectDue(chains, 10f, idOf, due);
        parts[4] = AllocatedSince(mark);
        long picked = parts.Sum();
        Console.WriteLine("FEAS-ALLOC-O parts pick=" + parts[0] + " secondaries=" + parts[1] + " contrast=" + parts[2] + " pressure=" + parts[3] + " chains=" + parts[4]);
        AssertEqual(4, count, "control: the chains' due list ran");
        AssertTrue(pressure.U > 0f && again.Has, "and the pressure and the pick answered");
        AssertEqual(0L, picked, "the pick, the secondaries, the contrast, the pressure and the chains' due list allocated " + picked + " bytes");
    }
}
