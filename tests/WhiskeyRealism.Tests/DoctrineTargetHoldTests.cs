using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using static TestHarness;

// Smoke 2026-10-17: a brigade's assigned point (TacticalCommandAssignment primary) jumped 25 to 100 m every 36 s with its role stable (1146,-874 ->
// 1087,-860 -> 1065,-849 -> 1104,-863 -> 1050,-838 -> 1138,-866 -> 1042,-815). The battle line planner derives the point from the direction
// node -> objective and the perceived enemy line, and both drift a little every ledger tick; every new point is a new order. A point is kept while the
// fresh one is within DoctrineTargetHold.HoldMeters of it, and moves when the line, the rect or the contact it belongs to has really moved.
internal static class DoctrineTargetHoldTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("target hold keeps a point while the fresh one drifts inside the threshold", DriftInsideTheThresholdIsKept),
            ("target hold takes the fresh point once it is further than the threshold", AMoveBeyondTheThresholdIsTaken),
            ("target hold a new task or objective takes the fresh point at once", ANewTaskOrObjectiveIsTaken),
            ("target hold carries the support and fallback points the same way", SupportAndFallbackAreHeldToo),
            ("target hold a missing point is never held and a first point is taken", MissingPointsAreNotHeld),
            ("target hold forgets a node that has left the build", AVanishedNodeIsForgotten),
            ("target hold the smoke's brigade: a fixing force over a drifting enemy line keeps one point for the minute", TheSmokeSequenceKeepsOnePoint),
        });
    }

    private static DoctrineTargetPoint P(float x, float z) => DoctrineTargetPoint.From(x, z);

    private static TacticalBattleLinePlan Plan(DoctrineTargetPoint primary, DoctrineTargetPoint support = default(DoctrineTargetPoint), DoctrineTargetPoint fallback = default(DoctrineTargetPoint))
        => new TacticalBattleLinePlan(primary, support, fallback, "battle-line-fixing-force:frontage");

    private static float Dist(DoctrineTargetPoint a, DoctrineTargetPoint b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    private static void DriftInsideTheThresholdIsKept()
    {
        var hold = new DoctrineTargetHold();
        TacticalBattleLinePlan first = hold.Apply("node-1", CommandTaskType.FixEnemy, "enemy-line-0", Plan(P(1146f, -874f)));
        AssertEqual(1146f, first.PrimaryTarget.X, "the first point is taken");
        foreach (var fresh in new[] { P(1087f, -860f), P(1065f, -849f), P(1104f, -863f), P(1138f, -866f) })
        {
            TacticalBattleLinePlan kept = hold.Apply("node-1", CommandTaskType.FixEnemy, "enemy-line-0", Plan(fresh));
            AssertEqual(1146f, kept.PrimaryTarget.X, "drift inside the threshold keeps the point (x)");
            AssertEqual(-874f, kept.PrimaryTarget.Z, "(z)");
        }
    }

    private static void AMoveBeyondTheThresholdIsTaken()
    {
        var hold = new DoctrineTargetHold();
        hold.Apply("node-1", CommandTaskType.FixEnemy, "enemy-line-0", Plan(P(1000f, -1000f)));
        float far = DoctrineTargetHold.HoldMeters + 10f;
        TacticalBattleLinePlan moved = hold.Apply("node-1", CommandTaskType.FixEnemy, "enemy-line-0", Plan(P(1000f + far, -1000f)));
        AssertEqual(1000f + far, moved.PrimaryTarget.X, "the line moved: the point moves");
        // The new point is the held one now: a drift around it is kept.
        TacticalBattleLinePlan again = hold.Apply("node-1", CommandTaskType.FixEnemy, "enemy-line-0", Plan(P(1000f + far - 40f, -1000f)));
        AssertEqual(1000f + far, again.PrimaryTarget.X, "and holds from there");
    }

    private static void ANewTaskOrObjectiveIsTaken()
    {
        var hold = new DoctrineTargetHold();
        hold.Apply("node-1", CommandTaskType.FixEnemy, "enemy-line-0", Plan(P(1000f, -1000f)));
        AssertEqual(1010f, hold.Apply("node-1", CommandTaskType.SupportAttack, "enemy-line-0", Plan(P(1010f, -1000f))).PrimaryTarget.X, "a new task");
        AssertEqual(1020f, hold.Apply("node-1", CommandTaskType.SupportAttack, "Island_Ford", Plan(P(1020f, -1000f))).PrimaryTarget.X, "a new objective");
    }

    private static void SupportAndFallbackAreHeldToo()
    {
        var hold = new DoctrineTargetHold();
        hold.Apply("node-1", CommandTaskType.FixEnemy, "enemy-line-0", Plan(P(1000f, -1000f), P(900f, -900f), P(700f, -700f)));
        TacticalBattleLinePlan next = hold.Apply("node-1", CommandTaskType.FixEnemy, "enemy-line-0", Plan(P(1030f, -1000f), P(930f, -900f), P(900f, -700f)));
        AssertEqual(1000f, next.PrimaryTarget.X, "primary held");
        AssertEqual(900f, next.SupportTarget.X, "support held");
        AssertEqual(900f, next.FallbackTarget.X, "a fallback 200 m away is a new fallback");
        AssertEqual("battle-line-fixing-force:frontage", next.Reason, "the reason is the fresh plan's");
    }

    private static void MissingPointsAreNotHeld()
    {
        var hold = new DoctrineTargetHold();
        hold.Apply("node-1", CommandTaskType.FixEnemy, "enemy-line-0", Plan(P(1000f, -1000f)));
        AssertFalse(hold.Apply("node-1", CommandTaskType.FixEnemy, "enemy-line-0", Plan(DoctrineTargetPoint.None)).PrimaryTarget.HasValue, "no point now: no point");
        AssertEqual(1050f, hold.Apply("node-1", CommandTaskType.FixEnemy, "enemy-line-0", Plan(P(1050f, -1000f))).PrimaryTarget.X, "a point after none is a first point");
    }

    private static void AVanishedNodeIsForgotten()
    {
        var hold = new DoctrineTargetHold();
        hold.Apply("node-1", CommandTaskType.FixEnemy, "enemy-line-0", Plan(P(1000f, -1000f)));
        hold.Apply("node-2", CommandTaskType.FixEnemy, "enemy-line-0", Plan(P(500f, -500f)));
        hold.BeginBuild();
        hold.Apply("node-2", CommandTaskType.FixEnemy, "enemy-line-0", Plan(P(500f, -500f)));
        hold.EndBuild();
        AssertEqual(1030f, hold.Apply("node-1", CommandTaskType.FixEnemy, "enemy-line-0", Plan(P(1030f, -1000f))).PrimaryTarget.X, "node-1 was not in the last build: it starts over");
    }

    private static void TheSmokeSequenceKeepsOnePoint()
    {
        // The division's node walks toward the line and the perceived line jitters; the real planner's point moves 25 to 100 m each tick.
        var hold = new DoctrineTargetHold();
        var raw = new System.Collections.Generic.List<DoctrineTargetPoint>();
        var held = new System.Collections.Generic.List<DoctrineTargetPoint>();
        var rng = new Random(11);
        float nodeX = 900f, nodeZ = -1100f;
        for (int tick = 0; tick < 12; tick++)
        {
            nodeX += 30f; nodeZ += 20f;
            float ex = 1250f + (float)(rng.NextDouble() * 80 - 40), ez = -900f + (float)(rng.NextDouble() * 80 - 40);
            CommandNodeOperationalState[] nodes =
            {
                CommandNodeOperationalState.Create("node-a", CommandEchelonKind.DivisionLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 600f, -1000f, 0f),
                CommandNodeOperationalState.Create("node-b", CommandEchelonKind.DivisionLike, CommandNodeRole.FixingForce, CommandTaskType.FormUp, nodeX, nodeZ, 0f),
                CommandNodeOperationalState.Create("node-c", CommandEchelonKind.DivisionLike, CommandNodeRole.SupportingAttack, CommandTaskType.FormUp, 700f, -1200f, 0f),
            };
            var operation = new OperationRecord(TacticalOperationShape.FixAndFlank, TacticalOperationPhase.Committed, "enemy-line-0", 900f);
            var picture = new BattlefieldPictureSnapshot(new[]
            {
                new BattlefieldObjectiveEstimate("enemy-line-0", TacticalObjectiveType.EnemyLine, 9000f, 0.9f, true, 0.4f, ex, ez, 0f, 0f)
            });
            CommandDoctrineOrder[] without = CommandDoctrineAssignment.Build(nodes, operation, picture, 20000f, 100f + tick * 36f, 0.3f);
            CommandDoctrineOrder[] with = CommandDoctrineAssignment.Build(nodes, operation, picture, 20000f, 100f + tick * 36f, 0.3f, targetHold: hold);
            AssertEqual(CommandTaskType.FixEnemy, with[1].Task, "the fixing force fixes");
            raw.Add(without[1].PrimaryTarget);
            held.Add(with[1].PrimaryTarget);
        }
        float rawTravel = 0f, heldTravel = 0f;
        for (int i = 1; i < raw.Count; i++)
        {
            rawTravel = Math.Max(rawTravel, Dist(raw[i], raw[0]));
            heldTravel = Math.Max(heldTravel, Dist(held[i], held[0]));
        }
        AssertTrue(rawTravel > DoctrineTargetHold.HoldMeters, "the fixture really drifts the planner's point past the threshold (" + rawTravel + " m)");
        AssertTrue(heldTravel < 1f || heldTravel > DoctrineTargetHold.HoldMeters - 1f, "the held point either stands or has made a real move: " + heldTravel);
        int changes = 0;
        for (int i = 1; i < held.Count; i++) if (Dist(held[i], held[i - 1]) > 0.01f) changes++;
        int rawChanges = 0;
        for (int i = 1; i < raw.Count; i++) if (Dist(raw[i], raw[i - 1]) > 0.01f) rawChanges++;
        AssertTrue(changes < rawChanges, "fewer new points than the planner made (" + changes + " of " + rawChanges + ")");
        for (int i = 1; i < held.Count; i++)
        {
            float step = Dist(held[i], held[i - 1]);
            AssertTrue(step < 0.01f || step > DoctrineTargetHold.HoldMeters, "every change of the held point is a real move, tick " + i + " moved " + step);
        }
    }
}
