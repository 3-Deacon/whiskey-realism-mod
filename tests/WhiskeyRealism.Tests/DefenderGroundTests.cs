using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// Smoke 2026-10-17 (CSA Defender, DelayingDefense, objective enemy-line-1): brigades were given reserve posts, assembly points and flank
// guards that lay forward of the line and past the enemy centroid ("4th, 7th and others sent troops across the river"). Two causes:
//   1. the ledger built every command-node state from the intent alone, so each node was planned from the map origin (the evidence's
//      fallback targets all lay about 300 m from (0,0)); a node is now placed by the ledger's ground, and an unplaced node is planned nowhere;
//   2. an objective that is the enemy line has no defended-line anchor, so the slots were laid relative to the enemy line: a Defender formed
//      210 m short of it and 325 m past it (the depth slot added the approach direction instead of subtracting it). A Defender now stands on
//      its own front (the placed nodes' centre, the enemy-to-front axis), and no slot of any side lies past its objective.
// A position the ledger computes stays on the node's bank (OwnBank) unless the node's task goes to the enemy.
internal static class DefenderGroundTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("defender ground a node the ledger could not place is planned nowhere, not from the map origin", AnUnplacedNodeIsPlannedNowhere),
            ("defender ground command states are placed from the ground by node id and the rest stay unplaced", StatesArePlacedFromTheGround),
            ("defender ground the battlefront is measured over placed nodes only", TheBattlefrontIgnoresUnplacedNodes),
            ("defender ground a defender facing the enemy line has no slot, reserve or guard ahead of its own foremost node", ADefenderHasNothingAheadOfItsFront),
            ("defender ground a defender's reserve stands behind its own front, farther from the enemy than its line", ADefendersReserveStandsBehindItsFront),
            ("defender ground the defended front is named own-front and its line slots stand on the line", TheOwnFrontIsNamedAndLineSlotsStandOnIt),
            ("defender ground a defender whose objective is not the enemy line keeps the shipped anchor geometry", ANonEnemyLineObjectiveKeepsTheShippedGeometry),
            ("defender ground the evidence nodes at the map origin reproduce nothing: no node plans a post near (0,0)", NoNodePlansAPostNearTheOrigin),
            ("defender ground the depth slot of an unanchored line lies short of the objective, deeper than the line slots", TheDepthSlotLiesShortOfTheObjective),
            ("defender ground a slot across a river is pulled back to the node's bank", ASlotAcrossARiverIsPulledBack),
            ("defender ground a task that goes to the enemy keeps its target across the river", AnAttackKeepsItsTargetAcrossTheRiver),
            ("defender ground a target on the node's own bank, an unready picture and an unplaced node are left alone", OwnBankLeavesWhatItCannotJudge),
            ("defender ground a defender's every position stays on its bank across a river", ADefendersEveryPositionStaysOnItsBank),
            ("defender ground the ledger plans from the ground it was given", TheLedgerPlansFromItsGround),
            ("defender ground the coordinator gives each side's ledger its ground before the tick and the runtime reads the side's snapshot", TheCoordinatorWiresTheGround),
        });
    }

    // ---- fixture: the evidence's geometry, shifted to a straight east-west line ----------------------------------------------------------

    // CSA brigades along x 960..1150, z -1060..-1210, one straggler 600 m south (2nd Brigade); the enemy line due east at (1500,-1100).
    private static readonly float[,] Evidence =
    {
        { 960f, -1060f }, { 1000f, -1120f }, { 1046f, -1150f }, { 1080f, -1137f },
        { 1120f, -1117f }, { 1150f, -1190f }, { 1030f, -1210f }, { 1076f, -1791f },
    };

    private static readonly CommandNodeRole[] Roles =
    {
        CommandNodeRole.MainEffort, CommandNodeRole.SupportingAttack, CommandNodeRole.FixingForce, CommandNodeRole.ScreeningForce,
        CommandNodeRole.Reserve, CommandNodeRole.Defender, CommandNodeRole.FlankMarch, CommandNodeRole.SupportingAttack,
    };

    private const float EnemyX = 1500f, EnemyZ = -1100f;

    private static CommandNodeOperationalState[] EvidenceNodes(bool placed = true)
    {
        var nodes = new CommandNodeOperationalState[Evidence.GetLength(0)];
        for (int i = 0; i < nodes.Length; i++)
        {
            nodes[i] = placed
                ? CommandNodeOperationalState.Create("node-" + i, CommandEchelonKind.BrigadeLike, Roles[i], CommandTaskType.FormUp, Evidence[i, 0], Evidence[i, 1], 90f)
                : new CommandNodeOperationalState("node-" + i, CommandEchelonKind.BrigadeLike, Roles[i], CommandTaskType.FormUp, CommandTaskState.Planning);
        }

        return nodes;
    }

    private static BattlefieldPictureSnapshot EnemyLinePicture(float x = EnemyX, float z = EnemyZ, bool exposed = true)
        => new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate("enemy-line-1", TacticalObjectiveType.EnemyLine, 3000f, 0.7f, exposed, 0.4f, x, z, 0f, 0f),
        });

    private static CommandDoctrineOrder[] Build(CommandNodeOperationalState[] nodes, BattleStance stance, BattlefieldPictureSnapshot picture = default,
        ICrossingPicture crossings = null, TacticalOperationShape shape = TacticalOperationShape.DefensiveNetwork,
        TacticalOperationPhase phase = TacticalOperationPhase.Forming)
    {
        if (picture.Objectives == null) picture = EnemyLinePicture();
        var operation = new OperationRecord(shape, phase, "enemy-line-1", 900f);
        return CommandDoctrineAssignment.Build(nodes, operation, picture, 20000f, 100f, 0.3f, stance, null, false, crossings);
    }

    private static IEnumerable<DoctrineTargetPoint> Targets(CommandDoctrineOrder o)
    {
        yield return o.PrimaryTarget;
        yield return o.SupportTarget;
        yield return o.FallbackTarget;
    }

    // The distance a point lies ahead of the foremost node along the line from the nodes' mean to the enemy (positive: nearer the enemy).
    private static float AheadOfFront(CommandNodeOperationalState[] nodes, float x, float z)
    {
        float mx = 0f, mz = 0f;
        foreach (var n in nodes) { mx += n.X; mz += n.Z; }
        mx /= nodes.Length;
        mz /= nodes.Length;
        float ax = EnemyX - mx, az = EnemyZ - mz;
        float len = (float)Math.Sqrt(ax * ax + az * az);
        ax /= len;
        az /= len;
        float foremost = float.MinValue;
        foreach (var n in nodes) foremost = Math.Max(foremost, n.X * ax + n.Z * az);
        return x * ax + z * az - foremost;
    }

    // ---- the node is planned from where it stands ---------------------------------------------------------------------------------------

    private static void AnUnplacedNodeIsPlannedNowhere()
    {
        var node = new CommandNodeOperationalState("node-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.Reserve, CommandTaskType.ReserveWait, CommandTaskState.Planning);
        AssertFalse(node.HasPosition, "a state built from the intent alone is unplaced");
        var objective = new BattlefieldObjectiveEstimate("enemy-line-1", TacticalObjectiveType.EnemyLine, 3000f, 0.7f, true, 0.4f, EnemyX, EnemyZ, 0f, 0f);
        foreach (CommandTaskType task in new[] { CommandTaskType.ReserveWait, CommandTaskType.FormUp, CommandTaskType.GuardFlank, CommandTaskType.Screen, CommandTaskType.AttackObjective })
        {
            TacticalBattleLinePlan plan = TacticalBattleLinePlanner.PlanNode(node, task, objective, true, 4, default(TacticalBattlefrontSnapshot), 8);
            AssertFalse(plan.PrimaryTarget.HasValue, task + " has no primary target");
            AssertFalse(plan.SupportTarget.HasValue, task + " has no support target");
            AssertFalse(plan.FallbackTarget.HasValue, task + " has no fallback target (it was 300 m from (0,0))");
            AssertContains(plan.Reason, "no-position", task + " says why");
        }

        // The placed copy is planned.
        CommandNodeOperationalState placed = node.WithPosition(1076f, -1791f, 90f);
        AssertTrue(placed.HasPosition, "placed");
        AssertTrue(PlanNodeTarget(placed, objective).HasValue, "a placed node has a target");
    }

    private static DoctrineTargetPoint PlanNodeTarget(CommandNodeOperationalState node, BattlefieldObjectiveEstimate objective)
        => TacticalBattleLinePlanner.PlanNode(node, CommandTaskType.ReserveWait, objective, true, 4, default(TacticalBattlefrontSnapshot), 8).PrimaryTarget;

    private sealed class Ground : ICommandGround
    {
        internal readonly Dictionary<string, (float x, float z, float facing)> Nodes = new Dictionary<string, (float, float, float)>();
        public ICrossingPicture Crossings { get; set; }

        public bool TryPlace(string nodeId, out float x, out float z, out float facingDegrees)
        {
            if (Nodes.TryGetValue(nodeId, out var at)) { x = at.x; z = at.z; facingDegrees = at.facing; return true; }
            x = z = facingDegrees = 0f;
            return false;
        }
    }

    private static void StatesArePlacedFromTheGround()
    {
        var intents = new[]
        {
            new CommandNodeIntent("node--705032", "node--705032", DirectChildRole.Reserve, DirectChildAxis.None, 0, 0, 3),
            new CommandNodeIntent("node--706122", "node--706122", DirectChildRole.SupportMain, DirectChildAxis.None, 0, 0, 3),
        };
        var ground = new Ground();
        ground.Nodes["node--705032"] = (1076.5f, -1791f, 135f);
        var operation = new OperationRecord(TacticalOperationShape.DefensiveNetwork, TacticalOperationPhase.Forming, "enemy-line-1", 0f);

        var withGround = CommandNodeOperationsRuntime.Build(intents, operation, new ObjectiveRecord[0], BattleStance.Defender, ground);
        AssertTrue(withGround[0].HasPosition, "the node the ground knows is placed");
        AssertNear(1076.5f, withGround[0].X, 0.001f, "x from the ground");
        AssertNear(-1791f, withGround[0].Z, 0.001f, "z from the ground");
        AssertNear(135f, withGround[0].FacingDegrees, 0.001f, "facing from the ground");
        AssertFalse(withGround[1].HasPosition, "a node the ground does not know stays unplaced");

        var noGround = CommandNodeOperationsRuntime.Build(intents, operation, new ObjectiveRecord[0], BattleStance.Defender);
        AssertFalse(noGround[0].HasPosition, "with no ground no node is placed");
    }

    private static void TheBattlefrontIgnoresUnplacedNodes()
    {
        var objective = new BattlefieldObjectiveEstimate("enemy-line-1", TacticalObjectiveType.EnemyLine, 3000f, 0.7f, true, 0.4f, EnemyX, EnemyZ, 0f, 0f);
        var nodes = new[]
        {
            CommandNodeOperationalState.Create("a", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 1000f, -1000f, 0f),
            CommandNodeOperationalState.Create("b", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 1200f, -1000f, 0f),
            new CommandNodeOperationalState("c", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, CommandTaskState.Planning),
        };
        TacticalBattlefrontSnapshot front = TacticalBattlefrontGeometry.Build(nodes, objective);
        AssertTrue(front.HasFrontage, "two placed nodes make a frontage");
        AssertNear(1100f, front.Center.X, 0.001f, "centre over the placed nodes only");
        AssertNear(-1000f, front.Center.Z, 0.001f, "z over the placed nodes only");
        AssertNear(200f, front.FrontageWidth, 0.001f, "width over the placed nodes only");

        var none = new[] { nodes[2] };
        AssertFalse(TacticalBattlefrontGeometry.Build(none, objective).HasFrontage, "no placed node, no frontage (it was a zero-width frontage at the origin)");
    }

    private static void NoNodePlansAPostNearTheOrigin()
    {
        CommandDoctrineOrder[] orders = Build(EvidenceNodes(placed: false), BattleStance.Defender);
        AssertEqual(Evidence.GetLength(0), orders.Length, "an order per node");
        foreach (CommandDoctrineOrder o in orders)
        {
            foreach (DoctrineTargetPoint t in Targets(o))
                AssertFalse(t.HasValue, o.NodeId + " " + o.Task + " plans no post from an unplaced node (" + t.X + "," + t.Z + ")");
        }
    }

    // ---- the Defender stands on its own front ------------------------------------------------------------------------------------------

    private static void ADefenderHasNothingAheadOfItsFront()
    {
        CommandNodeOperationalState[] nodes = EvidenceNodes();
        int checkedTargets = 0;
        // With the enemy line seen (the roles' attack tasks become holds) and not seen (they form up): neither puts a post ahead of the front.
        foreach (bool exposed in new[] { true, false })
        {
            foreach (CommandDoctrineOrder o in Build(nodes, BattleStance.Defender, EnemyLinePicture(exposed: exposed)))
            {
                foreach (DoctrineTargetPoint t in Targets(o))
                {
                    if (!t.HasValue) continue;
                    checkedTargets++;
                    float ahead = AheadOfFront(nodes, t.X, t.Z);
                    AssertTrue(ahead <= 1f, o.NodeId + " " + o.Task + " " + o.Reason + " lies " + ahead + " m ahead of the foremost own node");
                }
            }
        }

        AssertTrue(checkedTargets >= 8, "the fixture plans posts (" + checkedTargets + ")");
    }

    private static void ADefendersReserveStandsBehindItsFront()
    {
        CommandNodeOperationalState[] nodes = EvidenceNodes();
        CommandDoctrineOrder[] orders = Build(nodes, BattleStance.Defender);
        CommandDoctrineOrder reserve = orders[4];
        AssertEqual(CommandTaskType.ReserveWait, reserve.Task, "the reserve role waits");
        AssertTrue(reserve.PrimaryTarget.HasValue, "and has a post");
        float meanX = 0f, meanZ = 0f;
        foreach (var n in nodes) { meanX += n.X; meanZ += n.Z; }
        meanX /= nodes.Length;
        meanZ /= nodes.Length;
        float toEnemy = Dist(meanX, meanZ, EnemyX, EnemyZ);
        float reserveToEnemy = Dist(reserve.PrimaryTarget.X, reserve.PrimaryTarget.Z, EnemyX, EnemyZ);
        AssertTrue(reserveToEnemy >= toEnemy + 400f, "the reserve post is at least 400 m farther from the enemy than the line's centre (" + reserveToEnemy + " vs " + toEnemy + ")");
    }

    private static void TheOwnFrontIsNamedAndLineSlotsStandOnIt()
    {
        CommandNodeOperationalState[] nodes = EvidenceNodes();
        CommandDoctrineOrder[] orders = Build(nodes, BattleStance.Defender, EnemyLinePicture(exposed: false));
        // A FormUp line slot (not a depth slot: index 1 and 6 of 8) stands on the line through the centre, across the enemy axis.
        CommandDoctrineOrder slot = orders[2];
        AssertEqual(CommandTaskType.FormUp, slot.Task, "a line slot");
        AssertContains(slot.Reason, "own-front", "the line is the defended front");
        float meanX = 0f, meanZ = 0f;
        foreach (var n in nodes) { meanX += n.X; meanZ += n.Z; }
        meanX /= nodes.Length;
        meanZ /= nodes.Length;
        float ax = EnemyX - meanX, az = EnemyZ - meanZ;
        float len = (float)Math.Sqrt(ax * ax + az * az);
        float along = ((slot.PrimaryTarget.X - meanX) * ax + (slot.PrimaryTarget.Z - meanZ) * az) / len;
        AssertNear(0f, along, 1f, "a line slot lies on the line through the front's centre, not 210 m behind it");
        CommandDoctrineOrder depth = orders[1];
        AssertContains(depth.Reason, "scourge-slot-depth", "index 1 of 8 is a depth slot");
        float depthAlong = ((depth.PrimaryTarget.X - meanX) * ax + (depth.PrimaryTarget.Z - meanZ) * az) / len;
        AssertNear(-325f, depthAlong, 1f, "a depth slot stands 325 m behind the line");
    }

    private static void ANonEnemyLineObjectiveKeepsTheShippedGeometry()
    {
        // A Ridge the Defender holds (not an enemy line) takes no own-front: the shipped reason has no own-front.
        CommandNodeOperationalState[] nodes = EvidenceNodes();
        var picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate("enemy-line-1", TacticalObjectiveType.Ridge, 3000f, 0.7f, true, 0.4f, EnemyX, EnemyZ, 0.2f, 0.2f),
        });
        foreach (CommandDoctrineOrder o in Build(nodes, BattleStance.Defender, picture))
            AssertFalse(o.Reason.Contains("own-front"), o.NodeId + " keeps the shipped geometry: " + o.Reason);
    }

    // ---- no slot lies past its objective ------------------------------------------------------------------------------------------------

    private static void TheDepthSlotLiesShortOfTheObjective()
    {
        // Six nodes in a row across z = 0 below an enemy line at z = 1000, an Attacker's FormUp: index 1 is a depth slot.
        var nodes = new CommandNodeOperationalState[6];
        for (int i = 0; i < nodes.Length; i++)
            nodes[i] = CommandNodeOperationalState.Create("n" + i, CommandEchelonKind.BrigadeLike, CommandNodeRole.SupportingAttack, CommandTaskType.FormUp, -500f + 200f * i, 0f, 0f);
        var objective = new BattlefieldObjectiveEstimate("enemy-line-1", TacticalObjectiveType.EnemyLine, 3000f, 0.7f, true, 0.4f, 0f, 1000f, 0f, 0f);
        TacticalBattlefrontSnapshot front = TacticalBattlefrontGeometry.Build(nodes, objective);
        TacticalBattleLinePlan line = TacticalBattleLinePlanner.PlanNode(nodes[2], CommandTaskType.FormUp, objective, true, 2, front, nodes.Length);
        TacticalBattleLinePlan depth = TacticalBattleLinePlanner.PlanNode(nodes[1], CommandTaskType.FormUp, objective, true, 1, front, nodes.Length);
        AssertContains(depth.Reason, "scourge-slot-depth", "index 1 of 6 is a depth slot");
        AssertTrue(depth.PrimaryTarget.Z < objective.Z - 200f, "the depth slot lies short of the enemy line, not past it (z " + depth.PrimaryTarget.Z + " vs " + objective.Z + ")");
        AssertTrue(depth.PrimaryTarget.Z < line.PrimaryTarget.Z, "and deeper than the line slots (" + depth.PrimaryTarget.Z + " vs " + line.PrimaryTarget.Z + ")");
    }

    // ---- positions stay on the node's bank ------------------------------------------------------------------------------------------------

    // RiverCrossingTests.Picture: x < 500 region 0, x < 1000 region 1, x < 1500 region 2, beyond region 3.
    private static CommandNodeOperationalState[] RiverNodes()
    {
        var nodes = new CommandNodeOperationalState[3];
        for (int i = 0; i < nodes.Length; i++)
            nodes[i] = CommandNodeOperationalState.Create("n" + i, CommandEchelonKind.BrigadeLike, CommandNodeRole.FixingForce, CommandTaskType.FormUp, 1150f + 100f * i, -1000f, 90f);
        return nodes;
    }

    private static void ASlotAcrossARiverIsPulledBack()
    {
        // The Meeting stance keeps the shipped geometry: the line slot 210 m short of the enemy line at x 700 is x 910 (region 1), across the
        // river from nodes in region 2. It is pulled back onto their bank.
        var picture = new RiverCrossingTests.Picture();
        CommandNodeOperationalState[] nodes = RiverNodes();
        BattlefieldPictureSnapshot enemy = EnemyLinePicture(700f, -1000f, exposed: false);
        CommandDoctrineOrder[] free = Build(nodes, BattleStance.Meeting, enemy, null, TacticalOperationShape.SingleMainEffort);
        AssertEqual(1, picture.RegionAt(free[1].PrimaryTarget.X, free[1].PrimaryTarget.Z), "unclamped, the slot is across the river (x " + free[1].PrimaryTarget.X + ")");
        CommandDoctrineOrder[] kept = Build(nodes, BattleStance.Meeting, enemy, picture, TacticalOperationShape.SingleMainEffort);
        for (int i = 0; i < kept.Length; i++)
        {
            AssertEqual(CommandTaskType.FormUp, kept[i].Task, "node " + i + " forms up");
            foreach (DoctrineTargetPoint t in Targets(kept[i]))
            {
                if (!t.HasValue) continue;
                AssertEqual(picture.RegionAt(nodes[i].X, nodes[i].Z), picture.RegionAt(t.X, t.Z), "node " + i + " keeps its bank (target x " + t.X + ")");
            }
        }
    }

    private static void AnAttackKeepsItsTargetAcrossTheRiver()
    {
        var picture = new RiverCrossingTests.Picture();
        var nodes = new[] { CommandNodeOperationalState.Create("n0", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 1300f, -1000f, 90f) };
        BattlefieldPictureSnapshot enemy = EnemyLinePicture(700f, -1000f);
        CommandDoctrineOrder[] orders = Build(nodes, BattleStance.Attacker, enemy, picture, TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Committed);
        AssertEqual(CommandTaskType.AttackObjective, orders[0].Task, "the main effort attacks");
        AssertEqual(1, picture.RegionAt(orders[0].PrimaryTarget.X, orders[0].PrimaryTarget.Z),
            "an attack target stays across the river: the crossing gate decides whether and by what crossing it is made (x " + orders[0].PrimaryTarget.X + ")");
    }

    private static void OwnBankLeavesWhatItCannotJudge()
    {
        var picture = new RiverCrossingTests.Picture();
        var node = CommandNodeOperationalState.Create("n", CommandEchelonKind.BrigadeLike, CommandNodeRole.Reserve, CommandTaskType.ReserveWait, 1200f, -100f, 0f);
        var same = DoctrineTargetPoint.From(1400f, -300f);
        AssertEqual(same.X, OwnBank.Keep(picture, node, same).X, "a target on the node's bank is kept");
        var across = DoctrineTargetPoint.From(700f, -100f);
        DoctrineTargetPoint pulled = OwnBank.Keep(picture, node, across);
        AssertEqual(2, picture.RegionAt(pulled.X, pulled.Z), "a target across is pulled to the bank");
        AssertTrue(pulled.X >= 1000f && pulled.X < 1200f, "along the line to the node, on its bank (x " + pulled.X + ")");
        AssertEqual(across.X, OwnBank.Keep(new RiverCrossingTests.Picture { Ready = false }, node, across).X, "an unready picture judges nothing");
        AssertEqual(across.X, OwnBank.Keep(null, node, across).X, "no picture judges nothing");
        var unplaced = new CommandNodeOperationalState("u", CommandEchelonKind.BrigadeLike, CommandNodeRole.Reserve, CommandTaskType.ReserveWait, CommandTaskState.Planning);
        AssertEqual(across.X, OwnBank.Keep(picture, unplaced, across).X, "an unplaced node has no bank");
        AssertFalse(OwnBank.Keep(picture, node, DoctrineTargetPoint.None).HasValue, "no target stays no target");
        var onCrossing = CommandNodeOperationalState.Create("c", CommandEchelonKind.BrigadeLike, CommandNodeRole.Reserve, CommandTaskType.ReserveWait, -50f, 0f, 0f);
        AssertEqual(across.X, OwnBank.Keep(picture, onCrossing, across).X, "a node off every bank has none to keep");
    }

    private static void ADefendersEveryPositionStaysOnItsBank()
    {
        // The Defender's nodes on the far bank of a river from its own-front axis: nodes at x 1150..1350 (region 2), the enemy line at x 700.
        // Own-front slots go east (away from the enemy) and stay in region 2; a Screen forward of the line would not.
        var picture = new RiverCrossingTests.Picture();
        CommandNodeOperationalState[] nodes = RiverNodes();
        CommandDoctrineOrder[] orders = Build(nodes, BattleStance.Defender, EnemyLinePicture(700f, -1000f, exposed: false), picture);
        foreach (CommandDoctrineOrder o in orders)
        {
            foreach (DoctrineTargetPoint t in Targets(o))
            {
                if (!t.HasValue) continue;
                AssertEqual(2, picture.RegionAt(t.X, t.Z), o.NodeId + " " + o.Task + " stays on its bank (x " + t.X + ")");
            }
        }
    }

    // ---- the ledger carries the ground to the doctrine build ----------------------------------------------------------------------------

    private static void TheLedgerPlansFromItsGround()
    {
        CommandNodeOperationalState[] nodes = EvidenceNodes();
        var ledger = new TacticalOperationsLedgerRuntime();
        ledger.SetRuntimeClock(0f);
        ledger.Replace(TacticalCommanderMode.Active,
            new[] { EnemyLineRecord() },
            new StrategicBattleIntentSnapshot(0.2f, 0.3f, "theater", "campaign"), new ForceAvailabilitySnapshot(3000f, 0.30f), new PersonalityVector(0.4f, 0f, 0f, 0f, 0f));
        var picture = new RiverCrossingTests.Picture();
        var ground = new Ground { Crossings = picture };
        ledger.SetGround(ground);
        AssertTrue(ReferenceEquals(ground, ledger.Ground), "the ledger holds the ground it was given");
        ledger.StoreDoctrineForCommandStates(nodes, BattleStance.Defender);
        AssertEqual(nodes.Length, ledger.CurrentDoctrineOrders.Count, "an order per node");
        bool ownFront = false;
        foreach (CommandDoctrineOrder o in ledger.CurrentDoctrineOrders) ownFront |= o.Reason.Contains("own-front");
        AssertTrue(ownFront, "the Defender stood on its own front, so the enemy-line objective reached the doctrine build");
        foreach (CommandDoctrineOrder o in ledger.CurrentDoctrineOrders)
        {
            foreach (DoctrineTargetPoint t in Targets(o))
            {
                if (!t.HasValue) continue;
                AssertTrue(AheadOfFront(nodes, t.X, t.Z) <= 1f, o.NodeId + " " + o.Task + " is ahead of the front");
                // Region 2 is the nodes' bank (x 1000..1500); the crossings picture reached the build when no target left it.
                AssertTrue(t.X < 1500f || picture.RegionAt(t.X, t.Z) == picture.RegionAt(nodes[0].X, nodes[0].Z), o.NodeId + " crossed the bank");
            }
        }
    }

    private static void TheCoordinatorWiresTheGround()
    {
        string coordinator = FlankResponseTests.SourceText("Tactical", "Orchestrator", "TacticalBattleCoordinatorRuntime.cs");
        FlankResponseTests.AssertPinned(coordinator,
            "side.OperationsLedger.SetGround(new CommandGroundRuntime(SideSnapshot(side), side.AllianceId)); side.TickOperationsLedger(",
            "the ledger has its ground before its tick");
        string ground = FlankResponseTests.SourceText("Tactical", "Orchestrator", "CommandGroundRuntime.cs");
        FlankResponseTests.AssertPinned(ground, "_snapshot.IndexOfOwn(TacticalBattleCoordinator.ParseCommandNodeInstanceId(nodeId))", "the node id is the group's GameObject id, the snapshot's key");
        FlankResponseTests.AssertPinned(ground, "public ICrossingPicture Crossings => FeasibilityRuntime.CrossingPicture(_alliance);", "the same picture the crossing gate asks");
    }

    private static ObjectiveRecord EnemyLineRecord()
        => new ObjectiveRecord(
            new ObjectiveObservationInput("enemy-line-1", TacticalObjectiveType.EnemyLine, TacticalObjectiveSource.VisibleEnemyLine,
                new TacticalMapPoint(EnemyX, EnemyZ), 0.7f, 0.4f, typeAnchorVerified: true),
            TacticalObjectiveStatus.Contested, 3000f, 20000f);

    private static float Dist(float ax, float az, float bx, float bz)
    {
        float dx = ax - bx, dz = az - bz;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }
}
