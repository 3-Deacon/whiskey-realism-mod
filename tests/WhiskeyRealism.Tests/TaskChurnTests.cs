using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism;
using WhiskeyRealism.Util;
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
            ("task churn: a collapse that ended starts again from its next count", ACollapseThatEndedStartsAgain),
            ("task churn: a clock that ran backwards starts the collapse again", ABackwardsClockRestartsTheCollapse),
            ("task churn: a soft abort rests before the side commits again", ASoftAbortRests),
            ("task churn: the enemy line is remembered through a count without a glimpse of it", TheEnemyLineIsRemembered),
            ("task churn: the remembered enemy line ends with its horizon, a re-sighting or a clock that ran backwards", TheMemoryEnds),
            ("task churn: an operation on a remembered line keeps its objective and its orders through the gap", OrdersStayThroughTheGap),
            ("task churn: the memory of the enemy line ends with the ledger", TheMemoryEndsWithTheLedger),
            ("task churn: the change row names the director's reason and the forces it weighed", TheChangeRowNamesTheReason),
            ("task churn: a hold is stopped when vanilla's chain move walks it, a form-up or a screen goes back to its ledger point", AStayTaskIsContested),
            ("task churn: the stay contest leaves Whiskey's own move, a fight, a non-chain move, a recon group and a group off the chain alone", TheStayContestLeavesWhatIsNotItsOwn),
            ("task churn: the stay contest answers once per macro cycle and is off with the objective doctrine", TheStayContestIsBounded),
            ("task churn: a move is foreign when it is not the executor's last move for a group that is not in a fight", ForeignMoveTruthTable),
            ("task churn: the posture pipeline stops reading a foreign move as already correct", ThePipelineReadsForeignMoves),
            ("task churn: a child on its parent's slot follows the parent's march, a waypoint of its own is contested", AChildOnItsParentsSlotFollowsIt),
            ("task churn: smoke 2026-10-18 replay, the 4th Brigade is contested at the first stay row, then every macro cycle while it walks vanilla's march", TheSmokeReplay),
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

        // The set of enemies counted is the call's own: a second call, and a line of two enemies seen by the same three units, are counted afresh.
        ok = TacticalVisionRuntimeAdapter.TryVisibleEnemyLineFromAggregate_ForTest(snapshot, 0, out _, out enemyStrength, out _);
        AssertTrue(ok, "the next count sees it");
        AssertEqual(300f, enemyStrength, "the next count does not remember the last");
        var two = BattlePerceptionTests.ConsumerSnapshot(10.0,
            new[] { BattlePerceptionTests.OwnUnit(1, 100f, -50f, aiGroupStrength: 100), BattlePerceptionTests.OwnUnit(2, 400f, -50f, aiGroupStrength: 100),
                BattlePerceptionTests.OwnUnit(3, 420f, -50f, aiGroupStrength: 100) },
            BattlePerceptionTests.Seen(11, 100f, 0f, 300f), BattlePerceptionTests.Seen(12, 400f, 0f, 200f));
        TacticalVisionRuntimeAdapter.TryVisibleEnemyLineFromAggregate_ForTest(two, 0, out _, out enemyStrength, out _);
        AssertEqual(500f, enemyStrength, "two enemy regiments are 300 and 200, each once however many own units see it");
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
        var forming = new OperationRecord(TacticalOperationShape.DefensiveNetwork, TacticalOperationPhase.Forming, "line", 4000f);
        AssertFalse(TacticalOperationDirector.OddsCollapsed(TacticalOperationDirectorInput.ForTest(forming, 600f, 100f, 0.3f, 0.5f, 0.5f, new[] { Line(900f) })),
            "an operation that is not committed has no committed force to collapse (a weak army is why it forms, not a collapse)");
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

    // The assigned strength the runtime is given after each tick is the previous tick's choice: `assigned[k]` is measured after tick k and read by tick k+1.
    private static float FirstAbort(TacticalOperationsLedgerRuntime runtime, float startAt, float[] assigned, float[] times = null)
    {
        for (int k = 0; k < assigned.Length; k++)
        {
            float now = times != null ? times[k] : startAt + 36f * k;
            if (Tick(runtime, now, assigned[k], Map("Island_Ford"), EnemyLine(1500f)).Phase == TacticalOperationPhase.SoftAbort) return now;
        }
        return -1f;
    }

    private static void ACollapseThatEndedStartsAgain()
    {
        // Tick 0 commits; 1 to 8 are counted against 600; tick 8 measures the whole army again (3000) so tick 9 is not a collapse; from tick 9 on 600 again.
        var assigned = new float[80];
        for (int k = 0; k < assigned.Length; k++) assigned[k] = 600f;
        assigned[8] = 3000f;
        float abortedAt = FirstAbort(new TacticalOperationsLedgerRuntime(), 100f, assigned);
        float restart = 100f + 36f * 10f;   // tick 10 is the first count below the line after the recovery
        AssertTrue(abortedAt >= restart + TacticalOperationDirector.OddsCollapseDwellSeconds,
            "the dwell counts from the count after the recovery (" + restart + "), not from the first collapse: aborted at " + abortedAt);
        AssertTrue(abortedAt < restart + TacticalOperationDirector.OddsCollapseDwellSeconds + 40f, "and acts on it: aborted at " + abortedAt);
    }

    private static void ABackwardsClockRestartsTheCollapse()
    {
        var runtime = new TacticalOperationsLedgerRuntime();
        // Collapse counted from t=136; at t=712 the dwell is 576 s; the clock then runs back to 50 (a reload) and walks forward in 36 s steps.
        var times = new System.Collections.Generic.List<float>();
        for (float t = 100f; t <= 712f; t += 36f) times.Add(t);
        int backAt = times.Count;
        for (float t = 50f; t <= 900f; t += 36f) times.Add(t);
        var assigned = new float[times.Count];
        for (int k = 0; k < assigned.Length; k++) assigned[k] = 600f;
        float abortedAt = FirstAbort(runtime, 0f, assigned, times.ToArray());
        AssertTrue(abortedAt > 0f && abortedAt < 50f + 36f * 2f + TacticalOperationDirector.OddsCollapseDwellSeconds + 40f && abortedAt > 50f + TacticalOperationDirector.OddsCollapseDwellSeconds,
            "the collapse is timed from the first count after the clock ran back (aborted at " + abortedAt + ")");
        AssertTrue(backAt > 10, "the run reached the jump back");
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
        FlankResponseTests.AssertPinned(text, "static PosturePipeline() { BattleScope.Register(\"posture-contact\", _contact.Clear); }",
            "the memory is a battle's: it is cleared with the battle, from the owner's static constructor");
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

    private static void TheMemoryEndsWithTheLedger()
    {
        var runtime = new TacticalOperationsLedgerRuntime();
        Tick(runtime, 100f, 0f, Map("Island_Ford"), EnemyLine(1500f));
        var force = new ForceAvailabilitySnapshot(3000f, 0.3f);
        var personality = new PersonalityVector(0.4f, 0f, 0f, 0f, 0f);
        runtime.SetRuntimeClock(136f);
        runtime.Replace(TacticalCommanderMode.Off, new[] { Map("Island_Ford") }, Intent, force, personality);
        AssertEqual(0, runtime.CurrentObjectives.Count, "a ledger that does not run has no picture");
        runtime.SetRuntimeClock(172f);
        runtime.Replace(TacticalCommanderMode.Active, new[] { Map("Island_Ford") }, Intent, force, personality);
        AssertEqual(1, runtime.CurrentObjectives.Count, "and the line it knew before it stopped does not come back with it");
    }

    // ---- the stay contest (smoke 2026-10-18: a brigade on FormUp, Screen and Probe tasks walked vanilla's march to Kernstown) ----

    private sealed class Sink : IIntentSink
    {
        public readonly List<BattleOrderIntent> Intents = new List<BattleOrderIntent>();
        public void Submit(in BattleOrderIntent intent) => Intents.Add(intent);
    }

    // A group (61) at (90,90) on `task`, walking a move that is not Whiskey's own (the caller's verdict, MoveContest.IsForeignStayMove and not a parent's
    // march) to (505,495). It is a due chain's own group (WithChain), but nothing below needs it to be.
    private static ContestTurnFacts StayFacts(CommandTaskType task, long tick, double now, bool stayPoint = false)
    {
        return new ContestTurnFacts
        {
            Alliance = 0, GroupId = 61, Tick = tick, Task = task, Priority = IntentPriority.Executor, Source = IntentSource.PostureExecutor,
            HasPath = true, HasLastIssued = false, NowHours = now, PlanStartHours = now - 2.0,
            X = 90f, Z = 90f, WaypointX = 505f, WaypointZ = 495f,
            ForeignStayMove = true, HasStayPoint = stayPoint, StayX = 300f, StayZ = 300f,
        };
    }

    private static void WithChain(Action body)
    {
        bool before = Plugin.ObjectiveDoctrine;
        DueChainTable due = DueChainTable.ForAlliance(0);
        try
        {
            BattleScope.ResetAll();
            Plugin.ObjectiveDoctrine = true;
            due.Clear();
            due.Add(61, 500f, 500f);
            body();
        }
        finally
        {
            Plugin.ObjectiveDoctrine = before;
            due.Clear();
            BattleScope.ResetAll();
        }
    }

    private static void AStayTaskIsContested()
    {
        WithChain(() =>
        {
            var hold = new Sink();
            ContestTurnFacts holdFacts = StayFacts(CommandTaskType.HoldObjective, 1, 10.0);
            AssertEqual(1, ContestTurn.Run(in holdFacts, hold), "a hold walked by vanilla's chain move is contested, though Whiskey issued it no move");
            AssertEqual(OrderChannel.Stop, hold.Intents[0].Channel, "with a Stop where it stands");
            AssertTrue((hold.Intents[0].Flags & IntentFlags.Contest) != 0, "flagged Contest");

            var screen = new Sink();
            ContestTurnFacts screenFacts = StayFacts(CommandTaskType.Screen, 2, 11.0, stayPoint: true);
            AssertEqual(1, ContestTurn.Run(in screenFacts, screen), "a screen is contested");
            AssertEqual(OrderChannel.Move, screen.Intents[0].Channel, "with a move");
            AssertTrue(screen.Intents[0].TargetX == 300f && screen.Intents[0].TargetZ == 300f, "back to its ledger point");
            AssertEqual(CommandTaskType.HoldObjective, screen.Intents[0].Task, "as the contest's own halt");

            var formUp = new Sink();
            ContestTurnFacts near = StayFacts(CommandTaskType.FormUp, 3, 12.0, stayPoint: true);
            near.StayX = 100f; near.StayZ = 100f;
            AssertEqual(1, ContestTurn.Run(in near, formUp), "a form-up is contested");
            AssertEqual(OrderChannel.Stop, formUp.Intents[0].Channel, "and stopped when it stands at its point (the executor drops a Move onto it)");
        });
    }

    private static void TheStayContestLeavesWhatIsNotItsOwn()
    {
        WithChain(() =>
        {
            long tick = 0;
            double now = 10.0;
            int Answered(ContestTurnFacts f)
            {
                var sink = new Sink();
                f.Tick = ++tick;
                f.NowHours = now += 1.0;
                return ContestTurn.Run(in f, sink);
            }
            ContestTurnFacts own = StayFacts(CommandTaskType.Screen, 0, 0.0, stayPoint: true);
            own.ForeignStayMove = false;
            AssertEqual(0, Answered(own), "a move that is Whiskey's own, a fight, vanilla's assault or a parent's march is the caller's verdict: no foreign stay move, no contest");

            ContestTurnFacts fight = StayFacts(CommandTaskType.HoldObjective, 0, 0.0);
            fight.CloseEngaged = true;
            AssertEqual(0, Answered(fight), "a group in close contact is left to vanilla's reactions whatever the verdict");

            ContestTurnFacts recon = StayFacts(CommandTaskType.HoldObjective, 0, 0.0);
            recon.Recon = true;
            AssertEqual(0, Answered(recon), "a recon group is not contested");

            ContestTurnFacts idle = StayFacts(CommandTaskType.HoldObjective, 0, 0.0);
            idle.HasPath = false;
            AssertEqual(0, Answered(idle), "a group that does not walk has nothing to contest");

            ContestTurnFacts attack = StayFacts(CommandTaskType.AttackObjective, 0, 0.0);
            attack.ForeignStayMove = false;
            AssertEqual(0, Answered(attack), "an attack is no stay task: the caller gives no foreign stay move, and the BC14O re-issue needs the move it re-issues");

            // The widening (coordinator, after smoke16): the contest is not the due chain's. A group that is not on the table, and a move that does not end
            // at the chain's head, are contested as well.
            ContestTurnFacts stranger = StayFacts(CommandTaskType.HoldObjective, 0, 0.0);
            stranger.GroupId = 77;
            stranger.WaypointX = 900f; stranger.WaypointZ = 900f;
            AssertEqual(1, Answered(stranger), "a lone brigade off the chain table, walking a move that ends anywhere, is contested");
        });
    }

    private static void TheStayContestIsBounded()
    {
        WithChain(() =>
        {
            var sink = new Sink();
            ContestTurnFacts first = StayFacts(CommandTaskType.HoldObjective, 1, 10.0);
            AssertEqual(1, ContestTurn.Run(in first, sink), "the first count contests");
            ContestTurnFacts soon = StayFacts(CommandTaskType.HoldObjective, 2, 10.0 + MoveContest.ReissueMinHours * 0.5);
            AssertEqual(0, ContestTurn.Run(in soon, sink), "not again inside the macro cycle");
            ContestTurnFacts later = StayFacts(CommandTaskType.HoldObjective, 3, 10.0 + MoveContest.ReissueMinHours + 0.001);
            AssertEqual(1, ContestTurn.Run(in later, sink), "and again once it has passed, while vanilla's move is still being walked");
            Plugin.ObjectiveDoctrine = false;
            ContestTurnFacts off = StayFacts(CommandTaskType.HoldObjective, 4, 11.0);
            AssertEqual(0, ContestTurn.Run(in off, sink), "with the objective doctrine off vanilla's chain is left alone");
            AssertEqual(2, MoveContestState.ForAlliance(0).VanillaChainContests, "the contests are counted as moves the executor did not issue");
        });
    }

    private static void ForeignMoveTruthTable()
    {
        const CommandTaskType Screen = CommandTaskType.Screen;
        bool Foreign(bool active = true, bool fight = false, int stance = 3, CommandTaskType task = Screen, bool issued = true, float wx = 505f, float ix = 100f)
            => MoveContest.IsForeignStayMove(active, fight, stance, task, issued, wx, 495f, ix, 495f);
        AssertTrue(Foreign(), "a walking screen whose waypoint is not the executor's last move");
        AssertFalse(Foreign(ix: 505f), "the executor's last move is the group's own");
        AssertFalse(Foreign(ix: 530f), "within the contest's tolerance of it too");
        AssertTrue(Foreign(issued: false), "a group Whiskey never moved walks someone else's move");
        AssertFalse(Foreign(active: false), "a group that does not walk has no move to read");
        AssertFalse(Foreign(fight: true), "a group in a fight is left to what moves it");
        AssertFalse(Foreign(stance: MoveContest.AssaultStanceOf), "and a group in vanilla's assault stance");
        AssertFalse(Foreign(task: CommandTaskType.AttackObjective), "an attack is not a stay task");
        AssertTrue(Foreign(task: CommandTaskType.HoldObjective), "a hold is");
        AssertTrue(Foreign(task: CommandTaskType.FormUp), "a form-up is");
        AssertFalse(MoveContest.IsStayTask(CommandTaskType.FixEnemy) || MoveContest.IsStayTask(CommandTaskType.Probe) || MoveContest.IsStayTask(CommandTaskType.FallBackToLine),
            "a fix, a probe and a fall back go somewhere");
    }

    private static void ThePipelineReadsForeignMoves()
    {
        string text = FlankResponseTests.SourceText("Tactical", "Battle", "Producers", "PostureIntentProducer.cs");
        string run = FlankResponseTests.Between(text, "internal static bool Run(", "// #61 CanBypassStaleQueuedOrder");
        FlankResponseTests.AssertPinned(run, "bool foreignStay = recordTick && !exploit && !probe && physical.ActiveMove && Plugin.ObjectiveDoctrineEnabled() && !ParentMarches(s, i) && IsForeignStayMove(s, rec, closeEngaged || flankRisk, state.Task, physical);",
            "a foreign move of a stay task, behind the BC14O flag, not a recon or exploit group, not a parent's march");
        FlankResponseTests.AssertPinned(run, "physical = new CommandPhysicalState(physical.Routed, physical.PlayerProtected, physical.PathInterrupted, physical.Paths, false, physical.Formation);",
            "is no active move for the posture decision");
        FlankResponseTests.AssertPinned(run, "closeEngaged, foreignStay, state.Task, army, observed, priority, source, sink);",
            "and the contest turn is given the verdict and the move as observed");
        AssertTrue(run.IndexOf("IsForeignStayMove(s, rec", StringComparison.Ordinal) < run.IndexOf("TacticalIdleClassification idle", StringComparison.Ordinal),
            "before the idle classification and the eligibility read the move");
        FlankResponseTests.AssertPinned(text, "bool hasIssued = executor != null && executor.LastIssuedMove(rec.InstanceId, out issuedX, out issuedZ, out issuedHours);",
            "the executor's last outcome for the group is what the move is compared with");
        FlankResponseTests.AssertPinned(text, "if (MoveContest.FollowsParentMarch(hasPath, own, rec.LastWaypointX, rec.LastWaypointZ, parent.LastWaypointX, parent.LastWaypointZ)) return true;",
            "a group on the slot of a parent's own march is the parent's");
        FlankResponseTests.AssertPinned(text, "bool hasPath = parent.RegimentPaths > 0 || parent.MovementMode > 0;", "the parent walks by a path of its own, not by the moving children it is counted from");
        FlankResponseTests.AssertPinned(text, "ForeignStayMove = foreignStay,", "the contest turn is told the verdict");
        FlankResponseTests.AssertPinned(text, "HasStayPoint = hasOrder && order.PrimaryTarget.HasValue && (task == CommandTaskType.FormUp || task == CommandTaskType.Screen),",
            "a form-up and a screen carry their ledger point, a hold none");
    }

    private static void AChildOnItsParentsSlotFollowsIt()
    {
        AssertTrue(MoveContest.FollowsParentMarch(true, true, 1000f, -1000f, 1100f, -1000f), "a child 100 m from the parent's waypoint, the parent on a move of Whiskey's own, follows it");
        AssertFalse(MoveContest.FollowsParentMarch(true, true, 1000f, -1000f, 1300f, -1000f), "300 m off is an order of its own");
        AssertFalse(MoveContest.FollowsParentMarch(true, false, 1000f, -1000f, 1100f, -1000f), "a parent that walks a foreign move is not followed: the child is contested for its own waypoint");
        AssertFalse(MoveContest.FollowsParentMarch(false, true, 1000f, -1000f, 1100f, -1000f), "a parent with no path leads nobody");
    }

    // Smoke 2026-10-18, the 4th Brigade (#-927726): its rows from the tactical sidecar, the doctrine hour interpolated from the TacticalCrossingWatch rows
    // (the sidecar has wall-clock times only). The brigade walked vanilla's move to (677.5,-1417.5), 1,100 m, from 05:06:18.9 with no enemy in sight (the
    // matrix 'enemy' 0, stance 3); its posture rows carry the tasks below. Whiskey's last move for it is not in the sidecar: it is taken as the FixEnemy
    // point of 05:06:18.276, (1018,-1240.5); the 1st Division's last move is taken as its own waypoint, as its rows show it set by Whiskey's posture.
    // The group is NOT on the due chain table here: the replay shows the coverage does not depend on it.
    private static void TheSmokeReplay()
    {
        //          label,       hours,    task,                             x,       z,       wpx,    wpz,     walks, enemy, parentWalks, parentWpX, parentWpZ
        var rows = new (string, double, CommandTaskType, float, float, float, float, bool, float, bool, float, float)[]
        {
            ("05:06:18.906", 14.65896, CommandTaskType.FixEnemy,        1147.5f, -337.5f, 677.5f, -1417.5f, true,  0f, false, 758f,  -1234.5f),
            ("05:06:19.379", 14.66467, CommandTaskType.Screen,          1147.5f, -337.5f, 677.5f, -1417.5f, true,  0f, false, 758f,  -1234.5f),
            ("05:06:21.688", 14.73201, CommandTaskType.Screen,          1145f,   -352f,   677.5f, -1417.5f, true,  0f, false, 758f,  -1234.5f),
            ("05:06:25.571", 14.85997, CommandTaskType.Probe,           1145f,   -352f,   1125.5f, -377.5f, false, 0f, true,  758f,  -1234.5f),
            ("05:06:38.671", 15.07855, CommandTaskType.FallBackToLine,  1145f,   -352f,   677.5f, -1417.5f, true,  0f, true,  758f,  -1234.5f),
            ("05:06:42.318", 15.11069, CommandTaskType.FormUp,          1097.5f, -416f,   677.5f, -1417.5f, true,  0f, true,  1132f, -327f),
            ("05:07:13.906", 15.30516, CommandTaskType.FormUp,          1040.5f, -573.5f, 677.5f, -1417.5f, true,  0f, true,  1132f, -327f),
            ("05:07:38.609", 15.56489, CommandTaskType.Probe,           1052.5f, -573f,   677.5f, -1417.5f, false, 0f, true,  759.5f, -1235f),
        };
        // The stay contest answers the first Screen row, the next macro cycle's Screen row, and the two FormUp rows; the FixEnemy, Probe and FallBackToLine
        // rows are no stay tasks (those tasks go somewhere). The group is not on the due chain table: the shipped session's 45 TacticalMainEffort rows of side 0 all
        // read vanillaChainContest 0 while the brigade walked 11 minutes, so the existing chain contest never answered it. (On the table the BC14O re-issue would
        // also answer the FixEnemy and FallBackToLine rows, a Whiskey move existing for them, and the rate limit would shift the stay answers.)
        var expected = new[] { false, true, true, false, false, true, true, false };
        WithChain(() =>
        {
            DueChainTable due = DueChainTable.ForAlliance(0);
            due.Clear();
            for (int k = 0; k < rows.Length; k++)
            {
                var r = rows[k];
                bool foreign = MoveContest.IsForeignStayMove(r.Item8, r.Item9 > 0f, 3, r.Item3, true, r.Item6, r.Item7, 1018f, -1240.5f)
                    && !MoveContest.FollowsParentMarch(r.Item10, true, r.Item6, r.Item7, r.Item11, r.Item12);
                var sink = new Sink();
                var facts = new ContestTurnFacts
                {
                    Alliance = 0, GroupId = 927726, Tick = k + 1, Task = r.Item3, Priority = IntentPriority.Executor, Source = IntentSource.PostureExecutor,
                    HasPath = r.Item8, HasLastIssued = true, IssuedX = 1018f, IssuedZ = -1240.5f, IssuedHours = 14.6514, PlanStartHours = 12.0, NowHours = r.Item2,
                    X = r.Item4, Z = r.Item5, WaypointX = r.Item6, WaypointZ = r.Item7, ForeignStayMove = foreign,
                };
                int n = ContestTurn.Run(in facts, sink);
                AssertEqual(expected[k] ? 1 : 0, n, "row " + r.Item1 + " (" + r.Item3 + ", the group is off the chain table)");
                if (n > 0) AssertTrue((sink.Intents[0].Flags & IntentFlags.Contest) != 0, "contest-flagged at " + r.Item1);
            }
        });
    }
}
