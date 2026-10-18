using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// Smoke 2026-10-18 (main a6b49fcf, DLL 11c2eae4): the Union's divisions swapped FixEnemy, ReserveWait, AttackObjective, FallBackToLine and FormUp every
// 30 to 60 s of real time, in lockstep with the operation's phase (Forming, Committed, SoftAbort, Scouting), and the 4th Brigade carried a path 1.1 km to the
// vanilla objective with no enemy in sight. The evidence the operation is decided on was a one-tick sample of what is visible.
internal static class TaskChurnTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("task churn: the visible enemy line counts an enemy once however many own units see it", EnemyLineCountsAnEnemyOnce),
            ("task churn: an operation committed on the whole army and judged on the assigned part does not cycle", CommitAndAbortDoNotCycle),
            ("task churn: a chosen operation is held for the plan's commitment, not a quarter hour", AChosenOperationIsHeldForThePlanCommitment),
            ("task churn: the committed force must stand below the abort line for a dwell before the operation aborts", AbortNeedsTheCollapseToLast),
            ("task churn: the ledger runtime times the collapse from its first count", TheRuntimeTimesTheCollapse),
            ("task churn: a soft abort rests before the side commits again", ASoftAbortRests),
            ("task churn: the enemy line is remembered through a count without a glimpse of it", TheEnemyLineIsRemembered),
            ("task churn: the remembered enemy line ends with its horizon, a re-sighting or a clock that ran backwards", TheMemoryEnds),
            ("task churn: an operation on a remembered line keeps its objective and its orders through the gap", OrdersStayThroughTheGap),
            ("task churn: the change row names the director's reason and the forces it weighed", TheChangeRowNamesTheReason),
            ("task churn: a group that saw the enemy within the horizon is still in contact", AGroupKeepsItsContact),
            ("task churn: the reconnaissance rule reads the group's remembered contact, not the tick's sight", ReconReadsRememberedContact),
        });
    }

    private static void EnemyLineCountsAnEnemyOnce()
    {
        // Three own units, each with the same enemy regiment (300 men) as its closest visible enemy: the old sum was 900.
        var snapshot = BattlePerceptionTests.ConsumerSnapshot(10.0,
            new[] { BattlePerceptionTests.OwnUnit(1, 100f, -50f, aiGroupStrength: 100), BattlePerceptionTests.OwnUnit(2, 110f, -50f, aiGroupStrength: 100),
                BattlePerceptionTests.OwnUnit(3, 120f, -50f, aiGroupStrength: 100) },
            BattlePerceptionTests.Seen(11, 110f, 0f, 300f));
        bool ok = TacticalVisionRuntimeAdapter.TryVisibleEnemyLineFromAggregate_ForTest(
            snapshot, 0, out TacticalMapPoint point, out float enemyStrength, out _);
        AssertTrue(ok, "the line is seen");
        AssertEqual(300f, enemyStrength, "one enemy regiment of 300 is 300, not once per own unit that sees it");
        AssertNear(110f, point.X, 0.01f, "and it stands where the enemy stands");
    }

    // ---- the operation's ledger, driven as the coordinator drives it ----

    private static ObjectiveRecord Map(string id) => new ObjectiveRecord(
        new ObjectiveObservationInput(id, TacticalObjectiveType.Ford, TacticalObjectiveSource.VerifiedSceneObject, new TacticalMapPoint(1200f, -1300f), 0.9f, 1.0f, typeAnchorVerified: true),
        TacticalObjectiveStatus.Scouting, 0f, 0f);

    private static ObjectiveRecord EnemyLine(float enemy, float x = 900f, float z = -950f) => new ObjectiveRecord(
        new ObjectiveObservationInput("enemy-line-0", TacticalObjectiveType.EnemyLine, TacticalObjectiveSource.VisibleEnemyLine, new TacticalMapPoint(x, z), 0.7f, 0.4f, typeAnchorVerified: true),
        TacticalObjectiveStatus.Contested, enemy, 3000f);

    private static readonly StrategicBattleIntentSnapshot Intent = new StrategicBattleIntentSnapshot(0.2f, 0.3f, "theater", "campaign");

    private static CommandNodeOperationalState[] States() => new[]
    {
        CommandNodeOperationalState.Create("node-1", CommandEchelonKind.DivisionLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 0f, 0f, 0f),
    };

    // One ledger update as the coordinator makes it: the objectives of this tick, the force, the assigned strength measured after the last tick (the
    // Attack and Support nodes of a Committed operation: `assigned` while it is Committed, none before).
    private static OperationRecord Tick(TacticalOperationsLedgerRuntime runtime, float now, float assignedWhenCommitted, params ObjectiveRecord[] objectives)
    {
        runtime.SetRuntimeClock(now);
        runtime.Replace(TacticalCommanderMode.Active, objectives, Intent, new ForceAvailabilitySnapshot(3000f, 0.3f), new PersonalityVector(0.4f, 0f, 0f, 0f, 0f));
        runtime.StoreDoctrineForCommandStates(States(), BattleStance.Attacker);
        runtime.SetOperationAssignedStrength(runtime.CurrentOperation.Phase == TacticalOperationPhase.Committed ? assignedWhenCommitted : 0f);
        return runtime.CurrentOperation;
    }

    // Smoke 2026-10-18: Forming, Committed, SoftAbort, Committed, Forming, Committed ... one change a ledger tick. The army (3000) is twice the enemy
    // line (1500), so the operation commits; the nodes it then tasks to attack are 600 of them against 1500 (0.4): the abort line. The next tick the
    // operation is no longer Committed, the force is the army again, and it commits again.
    private static void CommitAndAbortDoNotCycle()
    {
        var runtime = new TacticalOperationsLedgerRuntime();
        var phases = new List<TacticalOperationPhase>();
        float now = 100f;
        for (int k = 0; k < 100; k++, now += 36f)
            phases.Add(Tick(runtime, now, 600f, Map("Island_Ford"), EnemyLine(1500f)).Phase);
        int changes = 0;
        for (int k = 1; k < phases.Count; k++) if (phases[k] != phases[k - 1]) changes++;
        AssertTrue(changes <= 3, "an hour of ledger ticks on one stable picture changes phase at most three times (took " + changes + "): " + string.Join(",", phases));
    }

    // ---- the director ----

    private static BattlefieldObjectiveEstimate Line(float enemy) =>
        new BattlefieldObjectiveEstimate("line", TacticalObjectiveType.EnemyLine, enemy, 0.9f, true, 0.8f, 100f, 100f, 0.5f, 0.6f);

    private static void AChosenOperationIsHeldForThePlanCommitment()
    {
        TacticalOperationDirectorDecision committed = TacticalOperationDirector.Decide(TacticalOperationDirectorInput.ForTest(
            OperationRecord.Noop, 1000f, 5000f, 0.3f, 0.5f, 0.5f, new[] { Line(1000f) }));
        AssertEqual(TacticalOperationPhase.Committed, committed.Operation.Phase, "odds of five: committed");
        AssertEqual(1000f + ArmyReplanTriggers.PlanCommitmentBattleSeconds, committed.Operation.MinimumCommitSeconds, "held for the plan's commitment");
        TacticalOperationDirectorDecision forming = TacticalOperationDirector.Decide(TacticalOperationDirectorInput.ForTest(
            OperationRecord.Noop, 1000f, 1000f, 0.3f, 0.5f, 0.5f, new[] { Line(2000f) }));
        AssertEqual(TacticalOperationPhase.Forming, forming.Operation.Phase, "outnumbered: forming");
        AssertEqual(1000f + ArmyReplanTriggers.PlanCommitmentBattleSeconds, forming.Operation.MinimumCommitSeconds, "forming is held as long");
    }

    private static TacticalOperationDirectorInput Collapsing(OperationRecord current, float now)
    {
        // The army (5000) is five times the line (900), the nodes tasked to attack are 400 of them: 0.44, below the abort line.
        return TacticalOperationDirectorInput.ForTest(current, now, 5000f, 0.3f, 0.5f, 0.5f, new[] { Line(900f) }).WithOperationAssignedStrength(400f);
    }

    private static void AbortNeedsTheCollapseToLast()
    {
        OperationRecord committed = OperationRecord.CreateCommittedForTest(TacticalOperationShape.SingleMainEffort, "line", minCommitUntilSeconds: 300f);
        TacticalOperationDirectorInput input = Collapsing(committed, 600f);
        AssertTrue(TacticalOperationDirector.OddsCollapsed(input), "the committed force is below the abort line");
        AssertEqual(TacticalOperationPhase.Committed, TacticalOperationDirector.Decide(input).Operation.Phase, "one count is no collapse");
        AssertEqual(TacticalOperationPhase.Committed, TacticalOperationDirector.Decide(input.WithOddsCollapseSeconds(TacticalOperationDirector.OddsCollapseDwellSeconds - 1f)).Operation.Phase,
            "nor is a collapse that has not lasted the dwell");
        TacticalOperationDirectorDecision aborted = TacticalOperationDirector.Decide(input.WithOddsCollapseSeconds(TacticalOperationDirector.OddsCollapseDwellSeconds));
        AssertEqual(TacticalOperationPhase.SoftAbort, aborted.Operation.Phase, "a collapse that lasted the dwell aborts");
        AssertEqual("odds-collapse", aborted.Reason, "for the odds");
        AssertEqual(600f + TacticalOperationDirector.SoftAbortRestSeconds, aborted.Operation.MinimumCommitSeconds, "and the abort rests");
        AssertFalse(TacticalOperationDirector.OddsCollapsed(TacticalOperationDirectorInput.ForTest(committed, 600f, 5000f, 0.3f, 0.5f, 0.5f, new[] { Line(900f) })),
            "with nothing assigned yet the army's odds are the odds: no collapse");
    }

    private static void ASoftAbortRests()
    {
        OperationRecord committed = OperationRecord.CreateCommittedForTest(TacticalOperationShape.SingleMainEffort, "line", minCommitUntilSeconds: 300f);
        OperationRecord aborted = TacticalOperationDirector.Decide(
            Collapsing(committed, 600f).WithOddsCollapseSeconds(TacticalOperationDirector.OddsCollapseDwellSeconds)).Operation;
        // The next count has no committed force to judge (the nodes are no longer tasked to attack): the army against the line, 5.5 to one.
        var army = TacticalOperationDirectorInput.ForTest(aborted, 700f, 5000f, 0.3f, 0.5f, 0.5f, new[] { Line(900f) });
        TacticalOperationDirectorDecision rest = TacticalOperationDirector.Decide(army);
        AssertEqual(TacticalOperationPhase.SoftAbort, rest.Operation.Phase, "the side that fell back does not commit on the next count");
        AssertEqual("soft-abort-rest", rest.Reason, "it rests");
        TacticalOperationDirectorDecision after = TacticalOperationDirector.Decide(
            TacticalOperationDirectorInput.ForTest(aborted, aborted.MinimumCommitSeconds + 1f, 5000f, 0.3f, 0.5f, 0.5f, new[] { Line(900f) }));
        AssertEqual(TacticalOperationPhase.Committed, after.Operation.Phase, "when the rest is over the army's odds commit it again");
    }

    // ---- the ledger runtime ----

    private static void TheRuntimeTimesTheCollapse()
    {
        var runtime = new TacticalOperationsLedgerRuntime();
        float firstCollapse = -1f, abortedAt = -1f;
        float now = 100f;
        for (int k = 0; k < 60 && abortedAt < 0f; k++, now += 36f)
        {
            OperationRecord op = Tick(runtime, now, 600f, Map("Island_Ford"), EnemyLine(1500f));
            if (k == 1) firstCollapse = now;
            if (op.Phase == TacticalOperationPhase.SoftAbort) abortedAt = now;
        }
        AssertTrue(abortedAt > 0f, "the collapse is acted on");
        AssertTrue(abortedAt - firstCollapse >= TacticalOperationDirector.OddsCollapseDwellSeconds,
            "not before it has lasted the dwell: first counted at " + firstCollapse + ", aborted at " + abortedAt);
        AssertTrue(abortedAt - firstCollapse < TacticalOperationDirector.OddsCollapseDwellSeconds + 40f,
            "and on the first count after it: first counted at " + firstCollapse + ", aborted at " + abortedAt);
    }

    // ---- the enemy line's memory ----

    private static void TheEnemyLineIsRemembered()
    {
        var memory = new EnemyLineMemory();
        IReadOnlyList<ObjectiveRecord> seen = memory.Apply(new[] { Map("Island_Ford"), EnemyLine(1500f) }, 100f);
        AssertEqual(2, seen.Count, "a line in sight is the picture as it is");
        IReadOnlyList<ObjectiveRecord> gap = memory.Apply(new[] { Map("Island_Ford") }, 136f);
        AssertEqual(2, gap.Count, "a count without it keeps it");
        AssertEqual("Island_Ford", gap[0].Observation.ObjectiveId, "the first objective of the picture is the one it had without the memory");
        AssertEqual("enemy-line-0", gap[1].Observation.ObjectiveId, "the line is last");
        AssertEqual(1500f, gap[1].EnemyStrength, "as it was seen: its strength");
        AssertNear(900f, gap[1].Observation.Location.X, 0.01f, "and where");
        AssertEqual(TacticalObjectiveStatus.Contested, gap[1].Status, "and what it was doing");
        IReadOnlyList<ObjectiveRecord> never = new EnemyLineMemory().Apply(new[] { Map("Island_Ford") }, 100f);
        AssertEqual(1, never.Count, "a line never seen is not invented");
    }

    private static void TheMemoryEnds()
    {
        var memory = new EnemyLineMemory();
        memory.Apply(new[] { EnemyLine(1500f, x: 900f) }, 100f);
        AssertEqual(1, memory.Apply(new ObjectiveRecord[0], 100f + EnemyLineMemory.HorizonSeconds).Count, "kept to the horizon");
        AssertEqual(0, memory.Apply(new ObjectiveRecord[0], 100f + EnemyLineMemory.HorizonSeconds + 1f).Count, "gone after it");
        AssertEqual(0, memory.Apply(new ObjectiveRecord[0], 120f).Count, "and gone for good: an expired line does not come back");

        memory.Apply(new[] { EnemyLine(1500f, x: 900f) }, 1000f);
        IReadOnlyList<ObjectiveRecord> moved = memory.Apply(new[] { EnemyLine(2200f, x: 400f) }, 1100f);
        AssertEqual(1, moved.Count, "a line in sight again is the picture");
        IReadOnlyList<ObjectiveRecord> later = memory.Apply(new ObjectiveRecord[0], 1200f);
        AssertEqual(2200f, later[0].EnemyStrength, "and it is what is remembered after that");
        AssertNear(400f, later[0].Observation.Location.X, 0.01f, "where it was last seen");

        memory.Apply(new[] { EnemyLine(1500f) }, 5000f);
        AssertEqual(0, memory.Apply(new ObjectiveRecord[0], 4000f).Count, "a clock that ran backwards (a reload) forgets it");
    }

    private static void OrdersStayThroughTheGap()
    {
        var runtime = new TacticalOperationsLedgerRuntime();
        OperationRecord committed = Tick(runtime, 100f, 2500f, Map("Island_Ford"), EnemyLine(1500f));
        AssertEqual(TacticalOperationPhase.Committed, committed.Phase, "the line is in sight: committed");
        CommandDoctrineOrder before = runtime.CurrentDoctrineOrders[0];
        OperationRecord gap = Tick(runtime, 136f, 2500f, Map("Island_Ford"));
        AssertEqual(TacticalOperationPhase.Committed, gap.Phase, "a count without a glimpse leaves it committed");
        AssertEqual("enemy-line-0", gap.PrimaryObjectiveId, "on the same line");
        CommandDoctrineOrder during = runtime.CurrentDoctrineOrders[0];
        AssertEqual(before.Task, during.Task, "the node's task does not change for a count without a glimpse");
        AssertEqual(before.ObjectiveId, during.ObjectiveId, "nor its objective");
        AssertNear(before.PrimaryTarget.X, during.PrimaryTarget.X, 0.01f, "nor the point it goes to (x)");
        AssertNear(before.PrimaryTarget.Z, during.PrimaryTarget.Z, 0.01f, "nor (z)");
    }

    // ---- the posture pipeline ----

    private static void AGroupKeepsItsContact()
    {
        var memory = new GroupContactMemory();
        AssertFalse(memory.InContact(7, visibleNow: false, nowHours: 12.0), "a group that never saw the enemy is not in contact");
        AssertTrue(memory.InContact(7, visibleNow: true, nowHours: 12.0), "in sight is in contact");
        AssertTrue(memory.InContact(7, visibleNow: false, nowHours: 12.2), "a quarter hour behind a rise: still in contact");
        AssertFalse(memory.InContact(8, visibleNow: false, nowHours: 12.2), "the memory is the group's own");
        AssertTrue(memory.InContact(7, visibleNow: false, nowHours: 12.0 + GroupContactMemory.HorizonHours), "to the horizon");
        AssertFalse(memory.InContact(7, visibleNow: false, nowHours: 12.0 + GroupContactMemory.HorizonHours + 0.01), "past it the enemy is lost");
        AssertFalse(memory.InContact(7, visibleNow: false, nowHours: 12.3), "and a lost enemy is not found again by the clock");
        memory.InContact(9, visibleNow: true, nowHours: 20.0);
        AssertFalse(memory.InContact(9, visibleNow: false, nowHours: 19.0), "a clock that ran backwards does not keep a contact");
    }

    private static void ReconReadsRememberedContact()
    {
        string text = FlankResponseTests.SourceText("Tactical", "Battle", "Producers", "PostureIntentProducer.cs");
        string run = FlankResponseTests.Between(text, "internal static bool Run(", "private static bool CanBypassStaleQueuedOrder(");
        FlankResponseTests.AssertPinned(run, "bool visibleEnemy = _contact.InContact(rec.InstanceId, s.ClosestVisibleEnemy(i, out _) >= 0, s.DoctrineHours);",
            "the pipeline asks the group's contact memory, with this tick's sight as the sighting");
        FlankResponseTests.AssertPinned(run, "new TacticalGrandTacticianReconInput(state.Role, state.Task, visibleEnemy, underRecentFire: closeEngaged,",
            "and the reconnaissance rule is given that");
    }

    // ---- telemetry ----

    private static void TheChangeRowNamesTheReason()
    {
        string ledger = FlankResponseTests.SourceText("Tactical", "Orchestrator", "TacticalOperationsLedgerRuntime.cs");
        FlankResponseTests.AssertPinned(ledger, "EmitOperationChange(allianceId, _lastDecidedOperation, decision, input); _lastDecidedOperation = decision.Operation;",
            "the row compares with what the director decided last, then that becomes the reference");
        FlankResponseTests.AssertPinned(ledger, ".WithDecision(\"TacticalOperationChange\", after.Phase.ToString(), decision.Reason)", "the director's reason");
        FlankResponseTests.AssertPinned(ledger, ".WithField(\"ownStrength\", input.OwnStrength)", "the army it weighed");
        FlankResponseTests.AssertPinned(ledger, ".WithField(\"assignedStrength\", input.OperationAssignedStrength)", "the part of it tasked to attack");
        FlankResponseTests.AssertPinned(ledger, ".WithField(\"enemyStrength\", enemy)", "the enemy at the objective");
        FlankResponseTests.AssertPinned(ledger, ".WithField(\"collapseSeconds\", input.OddsCollapseSeconds)", "and how long the collapse had lasted");
        string tags = FlankResponseTests.SourceText("Telemetry", "TelemetryTagPolicy.cs");
        FlankResponseTests.AssertPinned(tags, "AddTactical(\"TacticalOperationChange\", TelemetryCategory.Decision);", "the event is routed to the tactical sidecar");
    }
}
