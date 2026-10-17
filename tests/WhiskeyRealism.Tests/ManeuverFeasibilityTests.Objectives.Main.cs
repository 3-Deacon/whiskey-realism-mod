using System;
using WhiskeyRealism;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static TestFixtures;
using static ManeuverFeasibilityFixtures;

/// <summary>BC14O task 3: one main effort on the best feasible objective, with hysteresis (requirement-main-effort-objective).</summary>
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] ObjectiveMainTests() => new (string, Action)[]
    {
        ("bc14o FEAS-SCN-10 behind on points as the day runs out: the main effort goes to B at 10:00 and to A at 19:00", FeasScn10),
        ("bc14o FEAS-SCN-11 ahead: a mid commander holds A, an aggressive one attacks B", FeasScn11),
        ("bc14o OBJ-MAIN-EFFORT the director takes the best feasible score, not the nearest, keeps its own terms and opens no parallel shape", ObjMainEffort),
        ("bc14o OBJ-MAIN-EFFORT-PLAN the plan's main effort follows the objective's band and the odds-based shift is not asked", ObjMainEffortPlan),
        ("bc14o OBJ-MAIN-EFFORT-SELECT the selection's risk weight and hold bias come from the pressure", ObjMainEffortSelect),
        ("bc14o OBJ-HYSTERESIS the main effort changes only for a rival 0.1 ahead; the latch clears with a new plan, a new day and a missing objective", ObjHysteresis),
    };

    // Scenario 10/11 rows: the fixture gives the penalties directly to the pure scorer, and every director term is 0.
    static ObjectiveEstimate ScoredRow(string id, int points, float maxPoints, bool ownHeld, float penalty, float hold, in ObjectivePressure pressure)
    {
        float v = ObjectiveScoring.Normalised(points, maxPoints);
        var e = new ObjectiveEstimate { Id = id, V = v, Value = ObjectiveScoring.Value(v, ownHeld), Penalty = penalty, Hold = hold, Feasible = true };
        e.Score = ObjectiveScoring.Score(in e, in pressure);
        return e;
    }

    static void FeasScn10()
    {
        // Mid commander, no objective held: the enemy holds A (70 points), B (30) is neutral, the bar is even; r_A 0.95, r_B 0.
        var zero = new float[2];
        ObjectivePressure morning = ObjectiveScoring.Pressure(Clock(0f, 0.7f, 0.5f, 10f), 0.5f);
        var rows = new[] { ScoredRow("A", 70, 70f, false, 0.95f, 0f, in morning), ScoredRow("B", 30, 70f, false, 0f, 0f, in morning) };
        AssertNear(0.42f, rows[0].Score, 0.005f, "10:00: A scores 0.80 - 0.4 x 0.95");
        AssertNear(0.46f, rows[1].Score, 0.005f, "and B about 0.46");
        AssertEqual(1, ObjectiveScoring.PickMainEffort(rows, zero, null), "a first choice at 10:00 is B");

        ObjectivePressure evening = ObjectiveScoring.Pressure(Clock(0f, 0.7f, 0.5f, 1f), 0.5f);
        rows = new[] { ScoredRow("A", 70, 70f, false, 0.95f, 0f, in evening), ScoredRow("B", 30, 70f, false, 0f, 0f, in evening) };
        AssertNear(0.94f, rows[0].Score, 0.005f, "19:00 with an hour of light: A scores about 0.94");
        AssertNear(0.70f, rows[1].Score, 0.005f, "and B about 0.70");
        AssertEqual(0, ObjectiveScoring.PickMainEffort(rows, zero, "B"), "with B current, A leads by more than the 0.1 margin and takes the main effort");
        // The same evening without the lateness factor would not move it: urgency needs the lateness.
        ObjectivePressure noLate = ObjectiveScoring.Pressure(Clock(0f, 0.7f, 0.5f, 10f), 0.5f);
        rows = new[] { ScoredRow("A", 70, 70f, false, 0.95f, 0f, in noLate), ScoredRow("B", 30, 70f, false, 0f, 0f, in noLate) };
        AssertEqual(1, ObjectiveScoring.PickMainEffort(rows, zero, "B"), "control: with the day young B stays");
    }

    static void FeasScn11()
    {
        // The side holds A (65 points) with an enemy within 250 of it; the enemy holds B (35); the bar is even; no penalties.
        var zero = new float[2];
        ObjectivePressure mid = ObjectiveScoring.Pressure(Clock(0.65f, 0.35f, 0.5f, 10f), 0.5f);
        var rows = new[]
        {
            ScoredRow("A", 65, 65f, true, 0f, ObjectiveScoring.HoldWeight * mid.H, in mid),
            ScoredRow("B", 35, 65f, false, 0f, 0f, in mid),
        };
        AssertNear(0.60f, rows[0].Score, 0.005f, "a mid commander: A scores 0.45 + 0.15");
        AssertNear(0.52f, rows[1].Score, 0.005f, "B about 0.52");
        AssertEqual(0, ObjectiveScoring.PickMainEffort(rows, zero, null), "the mid commander holds A");

        ObjectivePressure aggressive = ObjectiveScoring.Pressure(Clock(0.65f, 0.35f, 0.5f, 10f), 0.9f);
        rows = new[]
        {
            ScoredRow("A", 65, 65f, true, 0f, ObjectiveScoring.HoldWeight * aggressive.H, in aggressive),
            ScoredRow("B", 35, 65f, false, 0f, 0f, in aggressive),
        };
        AssertNear(0.48f, rows[0].Score, 0.005f, "the aggressive commander: A scores 0.45 + 0.03");
        AssertEqual(1, ObjectiveScoring.PickMainEffort(rows, zero, null), "he attacks B");
        AssertNear(0.03f, ObjectiveScoring.HoldBias(in mid, TacticalPlaybookCatalog.PlaybookStance.Defensive), 1e-6f, "the holding plays get HoldBiasWeight x H = 0.03 in Select");
    }

    // A director objective the side perceives enemy at, so the contact-elsewhere rule has nothing to redirect.
    static BattlefieldObjectiveEstimate Target(string id, float lateral, float depth, float terrain = 0.1f, TacticalObjectiveType type = TacticalObjectiveType.VictoryPoint,
        float value = 0.8f)
        => new BattlefieldObjectiveEstimate(id, type, 4000f, 0.9f, true, value, FeasField.X(lateral), FeasField.Z(depth), terrain, 0.1f);

    static TacticalOperationDirectorInput DirectorInput(BattlefieldObjectiveEstimate[] objectives, IFeasibilityLookup ledger, string latched = null)
        => TacticalOperationDirectorInput.ForTest(OperationRecord.Noop, 100f, 20000f, 0.3f, 0.5f, 0.5f, objectives).WithStance(BattleStance.Attacker)
            .WithObjectiveDoctrine(ledger, latched);

    static void ObjMainEffort()
    {
        // A holds 100 points 900 units out, B 20 points 200 units out; the enemy line is 300 from A. B comes first in the director's list.
        FeasField f = ObjField();
        f.AddObjective("B", 0f, 200f, 20f);
        f.AddObjective("A", 0f, 900f, 100f);
        FeasibilityLedger ledger = f.Evaluate();
        BattlefieldObjectiveEstimate[] list = { Target("B", 0f, 200f, value: 0.80f), Target("A", 0f, 900f, value: 0.80f) };

        TacticalOperationDirectorDecision shipped = TacticalOperationDirector.Decide(TacticalOperationDirectorInput.ForTest(OperationRecord.Noop, 100f, 20000f, 0.3f, 0.5f, 0.5f, list)
            .WithStance(BattleStance.Attacker));
        AssertEqual("B", shipped.Operation.PrimaryObjectiveId, "control: the shipped director, with both at the fixed 0.80, takes the first");
        AssertFalse(shipped.Pick.Has, "and makes no pick");

        TacticalOperationDirectorDecision decision = TacticalOperationDirector.Decide(DirectorInput(list, ledger));
        AssertTrue(decision.Pick.Has, "the doctrine picks a main-effort objective");
        AssertEqual("A", decision.Pick.Objective.ObjectiveId, "the main effort is the more valuable objective, not the nearest");
        AssertEqual("A", decision.Operation.PrimaryObjectiveId, "and the operation follows it");
        AssertTrue(decision.Pick.HasRow, "the pick carries its ledger row");
        AssertEqual("B", decision.Pick.RunnerUpId, "the rival is named");

        // The director's own terms are kept: ground that is strong for an enemy-held objective counts against it.
        BattlefieldObjectiveEstimate[] hard = { Target("B", 0f, 200f, terrain: 0.1f), Target("A", 0f, 900f, terrain: 0.9f) };
        AssertEqual("B", TacticalOperationDirector.Decide(DirectorInput(hard, ledger)).Pick.Objective.ObjectiveId, "the terrain term still weighs: the strong ground costs A its lead");

        // An infeasible row is out: A behind a river with no crossing.
        FeasField river = ObjField(HorizontalBand(470f, 490f));
        river.AddObjective("B", 0f, 200f, 20f);
        river.AddObjective("A", 0f, 900f, 100f);
        AssertEqual("B", TacticalOperationDirector.Decide(DirectorInput(list, river.Evaluate())).Pick.Objective.ObjectiveId, "an objective behind a river with no crossing is not taken");

        // An entry without points scores its value times (1 + U) plus the director's terms, as today but for U.
        FeasField late = ObjField();
        late.Clock = new VictoryClock { ShareOwn = 0f, ShareEnemy = 0.7f, Bar = 0.5f, DaylightHours = 1f };
        FeasibilityLedger pressed = late.Evaluate();
        float u = pressed.Pressure.U;
        AssertNear(0.525f, u, 1e-4f, "the clock presses");
        BattlefieldObjectiveEstimate line = Target("line", 0f, 600f, terrain: 0f, type: TacticalObjectiveType.EnemyLine);
        ObjectivePick pick = TacticalOperationDirector.PickMainEffortObjective(DirectorInput(new[] { line }, pressed));
        float terms = TacticalOperationDirector.ObjectiveSelectionScore(line, BattleStance.Attacker) - line.Value;
        AssertNear(line.Value * (1f + u) + terms, pick.Total, 1e-5f, "value x (1 + U) + the director's terms");
        AssertFalse(pick.HasRow, "it has no row");

        // One main effort: the parallel shape is not opened (the shipped fixture opens it).
        BattlefieldObjectiveEstimate[] two =
        {
            new BattlefieldObjectiveEstimate("left", TacticalObjectiveType.Ridge, 1000f, 0.85f, true, 0.8f, 100f, 100f, 0.3f, 0.2f),
            new BattlefieldObjectiveEstimate("right", TacticalObjectiveType.Town, 900f, 0.85f, true, 0.9f, 400f, 120f, 0.4f, 0.3f),
        };
        TacticalOperationDirectorInput strong = TacticalOperationDirectorInput.ForTest(OperationRecord.Noop, 120f, 5000f, 0.35f, 0.8f, 0.1f, two);
        AssertEqual(TacticalOperationShape.ParallelObjectives, TacticalOperationDirector.Decide(strong).Operation.Shape, "control: the shipped director opens the parallel shape");
        TacticalOperationShape shape = TacticalOperationDirector.Decide(strong.WithObjectiveDoctrine(new FeasibilityLedger(), null)).Operation.Shape;
        AssertTrue(shape != TacticalOperationShape.ParallelObjectives, "under the doctrine no ParallelObjectives operation opens, got " + shape);

        // Flag off: the ledger is carried but the shipped director runs.
        bool before = Plugin.ObjectiveDoctrine;
        try
        {
            Plugin.ObjectiveDoctrine = false;
            AssertEqual("B", TacticalOperationDirector.Decide(DirectorInput(list, ledger)).Operation.PrimaryObjectiveId, "flag off: the shipped choice");
            AssertEqual(TacticalOperationShape.ParallelObjectives, TacticalOperationDirector.Decide(strong.WithObjectiveDoctrine(new FeasibilityLedger(), null)).Operation.Shape,
                "flag off: the parallel shape opens again");
        }
        finally { Plugin.ObjectiveDoctrine = before; }
    }

    static void ObjMainEffortPlan()
    {
        // An army on sector 0 with the front axis banded in five; the latched objective lies in band 4. The tick moves the plan there and does not ask
        // the odds-based shift, whose decisive sector is 2.
        ArmyTickCycle.Reset();
        TacticalFrontAxis axis = TacticalFrontAxis.FromCentroids(0f, 0f, 0f, 500f).WithOwnExtent(-100f, 100f, 5);
        var lee = new PersonalityVector(0.7f, -0.3f, 0.6f, 0.5f, 0.4f);
        var orch = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        var evidence = new ArmyEvidence(1.3f, TerrainKind.Open, defaultMainEffortSector: 0);
        orch.PickInitialPlan(evidence);
        orch.SetFeasibility(new FeasibilityLedger());
        var sectors = new EnemyVisibleSector[]
        {
            new EnemyVisibleSector(sectorId: 0, ownStrength: 4000f, enemyStrength: 4000f, recentFire: false),
            new EnemyVisibleSector(sectorId: 1, ownStrength: 3000f, enemyStrength: 5000f, recentFire: false),
            new EnemyVisibleSector(sectorId: 2, ownStrength: 5000f, enemyStrength: 2000f, recentFire: false),
        };
        var visible = new EnemyVisibleState(sectors, 0.4f, anyContactSpotted: false, anyContactBroken: false, frontAxis: axis);
        ObjectiveLatch latch = ObjectiveLatch.ForAlliance(1);
        try
        {
            latch.Set("far", 80f, 300f);
            orch.ObserveFrontGeometry(visible);
            AssertTrue(orch.TryObjectiveSector(out int sector) && sector == 4, "the objective's band is read from the latch and the axis");
            ArmyTickCycle.MaybeReplan(orch, 5f, evidence, visible, 12000f, 1.0f, 0.2f, 0f, 60, 0.2f, 0.9f, 0f, 0f);
            AssertEqual(4, orch.CurrentPlan.MainEffortSector, "the plan's main effort is the objective's band");

            // A later change moves it again.
            latch.Set("near", -80f, 300f);
            ArmyTickCycle.MaybeReplan(orch, 5f, evidence, visible, 12000f, 1.0f, 0.2f, 0f, 60, 0.2f, 0.9f, 0f, 0f);
            AssertEqual(0, orch.CurrentPlan.MainEffortSector, "a change of objective moves the plan's sector");

            // Control: with no latched objective the odds-based shift runs as before.
            latch.Clear();
            ArmyTickCycle.MaybeReplan(orch, 5f, evidence, visible, 12000f, 1.0f, 0.2f, 0f, 60, 0.2f, 0.9f, 0f, 0f);
            AssertEqual(2, orch.CurrentPlan.MainEffortSector, "control: with no main-effort objective the decisive sector 2 takes it");

            // Refused in Withdraw and Consolidate, as the shift is; a FocalAssault plan keeps its own.
            orch.AdvancePhase(BattlePhase.Withdraw);
            AssertFalse(orch.SetObjectiveMainEffort(4), "refused in Withdraw");
            orch.AdvancePhase(BattlePhase.Consolidate);
            AssertFalse(orch.SetObjectiveMainEffort(4), "refused in Consolidate");
            AssertEqual(2, orch.CurrentPlan.MainEffortSector, "unchanged");
            var other = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
            other.PickInitialPlan(evidence);
            AssertTrue(other.SetObjectiveMainEffort(3), "control: a plan in Probe moves");
            AssertFalse(other.SetObjectiveMainEffort(3), "and the same sector is no change");
            AssertFalse(other.SetObjectiveMainEffort(-1), "nor is no sector");

            // Flag off: no objective band.
            bool before = Plugin.ObjectiveDoctrine;
            try
            {
                Plugin.ObjectiveDoctrine = false;
                latch.Set("far", 80f, 300f);
                AssertFalse(orch.TryObjectiveSector(out _), "flag off: no objective sector");
            }
            finally { Plugin.ObjectiveDoctrine = before; }
        }
        finally { latch.Clear(); ArmyTickCycle.Reset(); }
    }

    static void ObjMainEffortSelect()
    {
        // The risk weight is FeasibilityWeight x (1 - 0.5 U) in the selection as in the objective score.
        var p = new ObjectivePressure { U = 1f, H = 0.5f };
        AssertNear(ManeuverFeasibility.FeasibilityWeight * 0.5f, ObjectiveScoring.SelectRiskWeight(in p), 1e-6f, "U 1 halves the risk weight");
        AssertNear(ManeuverFeasibility.FeasibilityWeight, ObjectiveScoring.SelectRiskWeight(default(ObjectivePressure)), 1e-6f, "U 0 leaves it");
        // The hold bias reaches Defensive and Neutral plans, not Offensive.
        AssertNear(ObjectiveScoring.HoldBiasWeight * 0.5f, ObjectiveScoring.HoldBias(in p, TacticalPlaybookCatalog.PlaybookStance.Defensive), 1e-6f, "Defensive");
        AssertNear(ObjectiveScoring.HoldBiasWeight * 0.5f, ObjectiveScoring.HoldBias(in p, TacticalPlaybookCatalog.PlaybookStance.Neutral), 1e-6f, "Neutral");
        AssertNear(0f, ObjectiveScoring.HoldBias(in p, TacticalPlaybookCatalog.PlaybookStance.Offensive), 1e-9f, "not Offensive");

        // In the catalog: a candidate's penalty term scales with the pressure, and the hold bias lands on the plans it names.
        TacticalPlaybookCatalog catalog = SeedCatalog.AllHistoricalAndGeneric();
        var calm = new TableLookup { AnswerAll = true, Default = FeasibilityVerdict.Risky, DefaultPenalty = 0.5f };
        var pressed = new TableLookup { AnswerAll = true, Default = FeasibilityVerdict.Risky, DefaultPenalty = 0.5f, PressureValue = p };
        var a = new SelectionReport();
        var b = new SelectionReport();
        catalog.Select(LeeContext(calm), a);
        catalog.Select(LeeContext(pressed), b);
        int checkedPlans = 0;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Skipped) continue;
            for (int k = 0; k < b.Count; k++)
            {
                if (b[k].Plan != a[i].Plan) continue;
                AssertNear(-ManeuverFeasibility.FeasibilityWeight * 0.5f, a[i].Term, 1e-6f, a[i].Plan + ": the calm term");
                AssertNear(-ManeuverFeasibility.FeasibilityWeight * 0.5f * 0.5f, b[k].Term, 1e-6f, b[k].Plan + ": the pressed term is half the weight");
                float bias = TacticalPlaybookCatalog.StanceOf(a[i].Plan) == TacticalPlaybookCatalog.PlaybookStance.Offensive ? 0f : ObjectiveScoring.HoldBiasWeight * 0.5f;
                AssertNear(a[i].BaseScore + bias, b[k].BaseScore, 1e-6f, b[k].Plan + ": the hold bias is " + bias);
                checkedPlans++;
            }
        }
        AssertTrue(checkedPlans >= 12, "the whole field was compared (" + checkedPlans + ")");

        // Flag off: the pressure is not read.
        bool before = Plugin.ObjectiveDoctrine;
        try
        {
            Plugin.ObjectiveDoctrine = false;
            var off = new SelectionReport();
            catalog.Select(LeeContext(pressed), off);
            AssertNear(a[0].BaseScore, off[0].BaseScore, 1e-6f, "flag off: U = H = 0 in Select");
        }
        finally { Plugin.ObjectiveDoctrine = before; }
    }

    static void ObjHysteresis()
    {
        // PickMainEffort: the current objective keeps the main effort unless a rival's total is 0.1 more.
        var rows = new[]
        {
            new ObjectiveEstimate { Id = "A", Score = 0.59f, Feasible = true },
            new ObjectiveEstimate { Id = "B", Score = 0.50f, Feasible = true },
        };
        var zero = new float[2];
        AssertEqual(1, ObjectiveScoring.PickMainEffort(rows, zero, "B"), "a rival 0.09 ahead does not take it");
        rows[0].Score = 0.61f;
        AssertEqual(0, ObjectiveScoring.PickMainEffort(rows, zero, "B"), "a rival 0.11 ahead does");
        AssertEqual(0, ObjectiveScoring.PickMainEffort(rows, zero, null), "with no current the best is taken");
        AssertEqual(0, ObjectiveScoring.PickMainEffort(rows, zero, "gone"), "a current that is not in the table gives no margin");
        // The director's terms count in the totals.
        AssertEqual(1, ObjectiveScoring.PickMainEffort(rows, new[] { 0f, 0.15f }, null), "a term of 0.15 on B beats A's 0.11");
        // An infeasible current is not kept.
        rows[1].Feasible = false;
        AssertEqual(0, ObjectiveScoring.PickMainEffort(rows, zero, "B"), "an infeasible current loses the main effort");
        rows[0].Feasible = false;
        AssertEqual(-1, ObjectiveScoring.PickMainEffort(rows, zero, "B"), "none feasible: -1");

        // Through the ledger runtime: the latch follows the pick, and an objective gone from the picture clears the margin.
        ObjectiveLatch latch = ObjectiveLatch.ForAlliance(1);
        try
        {
            FeasField f = ObjField();
            f.AddObjective("A", 0f, 900f, 100f);
            f.AddObjective("B", 0f, 700f, 90f);
            FeasibilityLedger ledger = f.Evaluate();
            var states = new[] { CommandNodeOperationalState.Create("node-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 0f, 0f, 0f) };
            var runtime = new TacticalOperationsLedgerRuntime();
            runtime.SetRuntimeClock(0f);
            runtime.Replace(TacticalCommanderMode.Active,
                new[] { ObjectiveRecordFor("A", 4000f, 100f, status: TacticalObjectiveStatus.WeaklyHeld), ObjectiveRecordFor("B", 4000f, 100f, status: TacticalObjectiveStatus.WeaklyHeld) },
                new StrategicBattleIntentSnapshot(0.2f, 0.3f, "theater", "campaign"), new ForceAvailabilitySnapshot(3000f, 0.30f), new PersonalityVector(0.4f, 0f, 0f, 0f, 0f));
            runtime.StoreDoctrineForCommandStates(states, BattleStance.Attacker, null, false, ledger, 1);
            AssertTrue(latch.Has, "the pick is latched");
            string first = latch.Id;
            runtime.StoreDoctrineForCommandStates(states, BattleStance.Attacker, null, false, ledger, 1);
            AssertEqual(first, latch.Id, "and stays");

            // The latched objective leaves the picture: the next pick has no margin to beat.
            string other = first == "A" ? "B" : "A";
            runtime.Replace(TacticalCommanderMode.Active, new[] { ObjectiveRecordFor(other, 4000f, 100f, status: TacticalObjectiveStatus.WeaklyHeld) },
                new StrategicBattleIntentSnapshot(0.2f, 0.3f, "theater", "campaign"), new ForceAvailabilitySnapshot(3000f, 0.30f), new PersonalityVector(0.4f, 0f, 0f, 0f, 0f));
            runtime.StoreDoctrineForCommandStates(states, BattleStance.Attacker, null, false, ledger, 1);
            AssertEqual(other, latch.Id, "a deactivated objective's latch gives way at once");

            // Nothing feasible clears it.
            runtime.Replace(TacticalCommanderMode.Active, Array.Empty<ObjectiveRecord>(),
                new StrategicBattleIntentSnapshot(0.2f, 0.3f, "theater", "campaign"), new ForceAvailabilitySnapshot(3000f, 0.30f), new PersonalityVector(0.4f, 0f, 0f, 0f, 0f));
            runtime.StoreDoctrineForCommandStates(states, BattleStance.Attacker, null, false, ledger, 1);
            AssertFalse(latch.Has, "no objective, no latch");

            // The doctrine off: the latch is not touched.
            latch.Set("kept", 1f, 2f);
            bool before = Plugin.ObjectiveDoctrine;
            try
            {
                Plugin.ObjectiveDoctrine = false;
                runtime.StoreDoctrineForCommandStates(states, BattleStance.Attacker, null, false, ledger, 1);
                AssertEqual("kept", latch.Id, "flag off: the latch is left alone");
            }
            finally { Plugin.ObjectiveDoctrine = before; }
        }
        finally { latch.Clear(); }

        // A new plan and a new battle day clear it; a kept plan does not.
        try
        {
            var lee = new PersonalityVector(0.7f, -0.3f, 0.6f, 0.5f, 0.4f);
            var army = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
            var evidence = new ArmyEvidence(1.3f, TerrainKind.Open, defaultMainEffortSector: 0);
            army.PickInitialPlan(evidence);
            ObjectiveLatch.ForAlliance(1).Set("A", 1f, 1f);
            army.Replan(evidence, new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, null), hysteresis: true, "ManeuverAssumptionBroken");
            AssertTrue(ObjectiveLatch.ForAlliance(1).Has, "a kept plan keeps the latch");
            army.Replan(evidence, new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, null), hysteresis: false, "OpportunityChange");
            AssertFalse(ObjectiveLatch.ForAlliance(1).Has, "a re-picked plan clears it");
            ObjectiveLatch.ForAlliance(1).Set("A", 1f, 1f);
            TacticalBattleCoordinator.ReplanForNewBattleDay(army, evidence, new EnemyVisibleState(Array.Empty<EnemyVisibleSector>(), 0f, false, false));
            AssertFalse(ObjectiveLatch.ForAlliance(1).Has, "a new battle day clears it");
            ObjectiveLatch.ForAlliance(1).Set("A", 1f, 1f);
            TacticalBattleCoordinator.ReplanForNewBattleDay(new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee), evidence,
                new EnemyVisibleState(Array.Empty<EnemyVisibleSector>(), 0f, false, false));
            AssertFalse(ObjectiveLatch.ForAlliance(1).Has, "even when the army has no plan yet");
        }
        finally { ObjectiveLatch.ForAlliance(1).Clear(); }
    }
}
