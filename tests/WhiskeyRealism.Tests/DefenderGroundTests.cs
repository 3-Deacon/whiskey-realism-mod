using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using WhiskeyRealism.Util;
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
            ("defender ground a defender node that has already crossed is sent back: its reserve post, screen and fallback lie on the side's bank", ACrossedDefenderNodeIsSentBack),
            ("defender ground the home bank is the deployment zone's, else the nodes' centre, recorded once and fixed when the force crosses", TheHomeBankIsFixedForTheBattle),
            ("defender ground a majority-crossed attacker's nodes still at home fall back unclamped, not forward over the river", AMajorityCrossedAttackerKeepsItsHomeNodesHome),
            ("defender ground a majority-crossed defender's crossed nodes are sent back to the home bank and the nodes at home keep theirs", AMajorityCrossedDefenderIsSentHome),
            ("defender ground a fall back is never moved forward by the clamp", AFallBackIsNeverMovedForward),
            ("defender ground a crossed meeting node's posts stay on the bank it stands on, not the home bank", ACrossedMeetingNodesPostsStayOnItsBank),
            ("defender ground a node with no position holds: its movement tasks are no move, its reserve idles, its fallback stays", ANodeWithNoPositionHolds),
            ("defender ground an attacker's screen keeps its target across the river like its attack", AnAttackersScreenKeepsItsTarget),
            ("defender ground a maneuver's target across the river is left to the plan and the node's fall back is not moved forward", AManeuverTargetIsLeftToThePlan),
            ("defender ground the pull-back stops at the farthest point on the bank", ThePullBackStopsAtTheFarthestPointOnTheBank),
            ("defender ground an own front is kept for an enemy-line objective that has an approach avenue", TheOwnFrontOverridesAnAvenueAnchor),
            ("defender ground the position survives every copy of a state: nested division, works hold, stance guard, exploit overlay, role rewrite", ThePositionSurvivesEveryCopy),
            ("defender ground the W&L order text gets the node's own position, or no point when the node is unplaced", ThePlayerOrderSnapshotUsesTheNodePosition),
            ("defender ground a crossed attacker's soft-abort fall back goes home to the side's bank, as the target is without the clamp", ACrossedNodesFallBackGoesHome),
            ("defender ground an attacker's bridgehead post stays on the bridgehead bank while its fall back goes home", ABridgeheadPostIsNotPulledBack),
            ("defender ground a crossed maneuvering node keeps its support post and its fall back goes home", AManeuveringNodesSupportStaysAndFallBackGoesHome),
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
        TacticalOperationPhase phase = TacticalOperationPhase.Forming, int home = AutoHome)
    {
        if (picture.Objectives == null) picture = EnemyLinePicture();
        var operation = new OperationRecord(shape, phase, "enemy-line-1", 900f);
        // The home bank the ledger would have settled at the first tick: where the nodes' centre stands (not recorded), unless the test fixes it.
        if (home == AutoHome) home = crossings != null ? HomeBank.Settle(-1, crossings, null, nodes) : -1;
        return CommandDoctrineAssignment.Build(nodes, operation, picture, 20000f, 100f, 0.3f, stance, null, false, crossings, home);
    }

    private const int AutoHome = int.MinValue;

    // The posts of an order (what the bank rule keeps on a bank); the fall back has its own rule.
    private static IEnumerable<DoctrineTargetPoint> Posts(CommandDoctrineOrder o)
    {
        yield return o.PrimaryTarget;
        yield return o.SupportTarget;
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

        public int HomeBank(CommandNodeOperationalState[] placedNodes)
            => WhiskeyRealism.Tactical.Operations.HomeBank.Settle(-1, Crossings, null, placedNodes);

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
        var ridge = new BattlefieldObjectiveEstimate("ridge", TacticalObjectiveType.Ridge, 3000f, 0.7f, true, 0.4f, EnemyX, EnemyZ, 0.2f, 0.2f);
        var own = EvidenceNodes();
        TacticalBattlefrontSnapshot front = TacticalBattlefrontGeometry.Build(own, ridge);
        AssertFalse(TacticalDefensiveLineAnchorPlanner.PlanOwnFront(ridge, own, front).HasAnchor, "a ridge is not an enemy line to turn away from");
        var line = new BattlefieldObjectiveEstimate("enemy-line-1", TacticalObjectiveType.EnemyLine, 3000f, 0.7f, true, 0.4f, EnemyX, EnemyZ, 0f, 0f);
        TacticalDefensiveLineAnchor anchor = TacticalDefensiveLineAnchorPlanner.PlanOwnFront(line, own, front);
        AssertTrue(anchor.HasAnchor && anchor.OnOwnFront, "an enemy line gives the own front: " + anchor.Reason);
        AssertFalse(TacticalDefensiveLineAnchorPlanner.PlanOwnFront(line, EvidenceNodes(placed: false), default(TacticalBattlefrontSnapshot)).HasAnchor, "no placed node, no front");
        float meanX = 0f, meanZ = 0f;
        foreach (var n in own) { meanX += n.X / own.Length; meanZ += n.Z / own.Length; }
        var onTop = new BattlefieldObjectiveEstimate("enemy-line-1", TacticalObjectiveType.EnemyLine, 3000f, 0.7f, true, 0.4f, meanX, meanZ, 0f, 0f);
        AssertFalse(TacticalDefensiveLineAnchorPlanner.PlanOwnFront(onTop, own, front).HasAnchor, "an enemy line on top of the front gives no axis");

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
            foreach (DoctrineTargetPoint t in Posts(kept[i]))
            {
                if (!t.HasValue) continue;
                AssertEqual(picture.RegionAt(nodes[i].X, nodes[i].Z), picture.RegionAt(t.X, t.Z), "node " + i + " keeps its bank (post x " + t.X + ")");
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
        // Every eighth of the way lies on another bank or the river: the node's own position is the last post on its bank.
        var nearEdge = CommandNodeOperationalState.Create("e", CommandEchelonKind.BrigadeLike, CommandNodeRole.Reserve, CommandTaskType.ReserveWait, 1010f, -100f, 0f);
        DoctrineTargetPoint far = OwnBank.Keep(picture, nearEdge, DoctrineTargetPoint.From(60f, -100f));
        AssertNear(1010f, far.X, 0.001f, "no point of the line on the bank: the node's own x");
        AssertNear(-100f, far.Z, 0.001f, "and its own z");
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
        int posts = 0;
        foreach (CommandDoctrineOrder o in orders)
        {
            foreach (DoctrineTargetPoint t in Posts(o))
            {
                if (!t.HasValue) continue;
                posts++;
                AssertEqual(2, picture.RegionAt(t.X, t.Z), o.NodeId + " " + o.Task + " stays on its bank (x " + t.X + ")");
            }
        }

        AssertTrue(posts >= 3, "the Defender has posts to keep (" + posts + ")");
    }

    // ---- the ledger carries the ground to the doctrine build ----------------------------------------------------------------------------

    private static void TheLedgerPlansFromItsGround()
    {
        // The evidence front: the Defender stands on its own front, nothing ahead of it.
        CommandNodeOperationalState[] nodes = EvidenceNodes();
        TacticalOperationsLedgerRuntime ledger = LedgerWith(EnemyX, EnemyZ, new Ground());
        ledger.StoreDoctrineForCommandStates(nodes, BattleStance.Defender);
        AssertEqual(nodes.Length, ledger.CurrentDoctrineOrders.Count, "an order per node");
        bool ownFront = false;
        foreach (CommandDoctrineOrder o in ledger.CurrentDoctrineOrders)
        {
            ownFront |= o.Reason.Contains("own-front");
            foreach (DoctrineTargetPoint t in Targets(o))
                if (t.HasValue) AssertTrue(AheadOfFront(nodes, t.X, t.Z) <= 1f, o.NodeId + " " + o.Task + " is ahead of the front");
        }

        AssertTrue(ownFront, "the Defender stood on its own front, so the enemy-line objective reached the doctrine build");

        // The ground's crossing picture reaches the build: nodes on the bank x 1000..1500 with the enemy to the west put the own front's reserve
        // post (425 m behind it) on the far bank to the east; the ledger pulls it back.
        var picture = new RiverCrossingTests.Picture();
        var bankRoles = new[] { CommandNodeRole.Reserve, CommandNodeRole.ScreeningForce, CommandNodeRole.Reserve };
        CommandNodeOperationalState[] bank = RiverNodes();
        for (int i = 0; i < bank.Length; i++)
            bank[i] = CommandNodeOperationalState.Create(bank[i].NodeId, CommandEchelonKind.BrigadeLike, bankRoles[i], CommandTaskType.FormUp, bank[i].X, bank[i].Z, 90f);
        TacticalOperationsLedgerRuntime onBank = LedgerWith(700f, -1000f, new Ground { Crossings = picture });
        onBank.StoreDoctrineForCommandStates(bank, BattleStance.Defender);
        int posts = 0;
        foreach (CommandDoctrineOrder o in onBank.CurrentDoctrineOrders)
        {
            foreach (DoctrineTargetPoint t in Posts(o))
            {
                if (!t.HasValue) continue;
                posts++;
                AssertEqual(2, picture.RegionAt(t.X, t.Z), o.NodeId + " " + o.Task + " stays on the bank through the ledger (x " + t.X + ")");
            }
        }

        AssertTrue(posts >= 3, "the ledger planned posts to keep (" + posts + ")");
    }

    private static TacticalOperationsLedgerRuntime LedgerWith(float enemyX, float enemyZ, ICommandGround ground)
    {
        var ledger = new TacticalOperationsLedgerRuntime();
        ledger.SetRuntimeClock(0f);
        ledger.Replace(TacticalCommanderMode.Active,
            new[] { EnemyLineRecord(enemyX, enemyZ) },
            new StrategicBattleIntentSnapshot(0.2f, 0.3f, "theater", "campaign"), new ForceAvailabilitySnapshot(3000f, 0.30f), new PersonalityVector(0.4f, 0f, 0f, 0f, 0f));
        ledger.SetGround(ground);
        AssertTrue(ReferenceEquals(ground, ledger.Ground), "the ledger holds the ground it was given");
        return ledger;
    }

    private static void TheCoordinatorWiresTheGround()
    {
        string coordinator = FlankResponseTests.SourceText("Tactical", "Orchestrator", "TacticalBattleCoordinatorRuntime.cs");
        FlankResponseTests.AssertPinned(coordinator,
            "side.OperationsLedger.SetGround(CommandGroundRuntime.For(SideSnapshot(side), side.AllianceId)); side.TickOperationsLedger(",
            "the ledger has its ground before its tick");
        string ground = FlankResponseTests.SourceText("Tactical", "Orchestrator", "CommandGroundRuntime.cs");
        FlankResponseTests.AssertPinned(ground, "_snapshot.IndexOfOwn(TacticalBattleCoordinator.ParseCommandNodeInstanceId(nodeId))", "the node id is the group's GameObject id, the snapshot's key");
        FlankResponseTests.AssertPinned(ground, "public ICrossingPicture Crossings => FeasibilityRuntime.CrossingPicture(_alliance);", "the same picture the crossing gate asks");
        FlankResponseTests.AssertPinned(ground, "Operations.HomeBank.Settle(_alliance, Crossings, OwnDeploymentZoneRuntime.For(BattleCoreTick.ResolveBattleUnits(), _alliance), placedNodes);",
            "the home bank comes from the own deployment zone");
        string ledger = FlankResponseTests.SourceText("Tactical", "Orchestrator", "TacticalOperationsLedgerRuntime.cs");
        FlankResponseTests.AssertPinned(ledger, "Ground?.Crossings, Ground != null ? Ground.HomeBank(commandStates) : -1, _targetHold);", "the ledger hands the doctrine build the side's home bank");
    }

    // ---- a node already across, a node with no position, and the copies ---------------------------------------------------------------

    // The user's case: nodes stand on region 2 (RiverCrossingTests.Picture), the enemy line at x 300 (region 0), and one node is already across in region 1.
    private static CommandNodeOperationalState[] NodesWithOneCrossed(int crossed, out BattlefieldPictureSnapshot enemy)
    {
        var roles = new[] { CommandNodeRole.Defender, CommandNodeRole.Reserve, CommandNodeRole.SupportingAttack, CommandNodeRole.FixingForce, CommandNodeRole.ScreeningForce };
        float[] xs = { 1150f, 1250f, 1350f, 1300f, 1200f };
        xs[crossed] = 800f;
        var nodes = new CommandNodeOperationalState[xs.Length];
        for (int i = 0; i < xs.Length; i++)
            nodes[i] = CommandNodeOperationalState.Create("n" + i, CommandEchelonKind.BrigadeLike, roles[i], CommandTaskType.FormUp, xs[i], -1000f + 60f * i, 270f);
        enemy = EnemyLinePicture(300f, -1000f);
        return nodes;
    }

    private static void ACrossedDefenderNodeIsSentBack()
    {
        var picture = new RiverCrossingTests.Picture();
        foreach (int crossed in new[] { 1, 4 })   // the reserve, then the screen
        {
            CommandNodeOperationalState[] nodes = NodesWithOneCrossed(crossed, out BattlefieldPictureSnapshot enemy);
            AssertEqual(1, picture.RegionAt(nodes[crossed].X, nodes[crossed].Z), "the node stands across the river");
            CommandDoctrineOrder o = Build(nodes, BattleStance.Defender, enemy, picture)[crossed];
            AssertTrue(o.PrimaryTarget.HasValue, "node " + crossed + " has a post (" + o.Task + ")");
            AssertEqual(2, picture.RegionAt(o.PrimaryTarget.X, o.PrimaryTarget.Z), "node " + crossed + " " + o.Task + " post is back on the side's bank (x " + o.PrimaryTarget.X + ")");
            foreach (DoctrineTargetPoint t in Posts(o))
                if (t.HasValue) AssertEqual(2, picture.RegionAt(t.X, t.Z), "node " + crossed + " every post is on the side's bank (x " + t.X + ")");
        }
    }

    private static void TheHomeBankIsFixedForTheBattle()
    {
        BattleScope.ResetAll();
        try
        {
            var picture = new RiverCrossingTests.Picture();
            CommandNodeOperationalState[] home = CrossedAttackerNodes(CommandNodeRole.SupportingAttack);   // 2 of 5 across, centre in region 2
            for (int i = 0; i < home.Length; i++) home[i] = home[i].WithPosition(1100f + 50f * i, -1000f, 270f);
            AssertEqual(2, HomeBank.Settle(0, picture, null, home), "settled at the first tick from the nodes' centre: region 2");
            // The force crosses: the centre now stands on region 1. The home bank does not move.
            var across = new CommandNodeOperationalState[home.Length];
            for (int i = 0; i < across.Length; i++) across[i] = home[i].WithPosition(700f + 40f * i, -1000f, 270f);
            AssertEqual(2, HomeBank.Settle(0, picture, null, across), "the home bank stays region 2 when the whole force is across");
            AssertEqual(1, HomeBank.Settle(1, picture, null, across), "the other alliance settles from its own nodes");
            BattleScope.ResetAll();
            AssertEqual(1, HomeBank.Settle(0, picture, null, across), "a new battle settles anew");

            // The deployment zone wins over the centre: its nearest point to the centre is on region 1.
            BattleScope.ResetAll();
            AssertEqual(1, HomeBank.Settle(0, picture, new FixedZone(800f, -1000f), home), "the zone's bank, not the nodes' centre's");
            // A zone with no answer, an unready picture, nothing placed, and a centre on the river.
            BattleScope.ResetAll();
            AssertEqual(2, HomeBank.Settle(0, picture, new FixedZone(float.NaN, 0f), home), "a zone that does not answer gives the centre's bank");
            BattleScope.ResetAll();
            AssertEqual(-1, HomeBank.Settle(0, new RiverCrossingTests.Picture { Ready = false }, null, home), "an unready picture settles nothing");
            AssertEqual(-1, HomeBank.Settle(0, picture, null, EvidenceNodes(placed: false)), "no placed node settles nothing");
            AssertEqual(2, HomeBank.Settle(0, picture, null, home), "and nothing was recorded by those");
            BattleScope.ResetAll();
            var river = new[]
            {
                CommandNodeOperationalState.Create("a", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, -600f, -1000f, 0f),
                CommandNodeOperationalState.Create("b", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, -400f, -1000f, 0f),
                CommandNodeOperationalState.Create("c", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 200f, -1000f, 0f),
            };
            AssertEqual(-1, picture.RegionAt(-267f, -1000f), "the centre (x -267) is on no bank");
            AssertEqual(0, HomeBank.Settle(0, picture, null, river), "a centre on no bank gives the bank of the placed node nearest it");
        }
        finally { BattleScope.ResetAll(); }
    }

    private sealed class FixedZone : IOwnDeploymentZone
    {
        private readonly float _x, _z;
        internal FixedZone(float x, float z) { _x = x; _z = z; }
        public bool Active => true;
        public bool TryClamp(float fromX, float fromZ, ref float x, ref float z)
        {
            if (float.IsNaN(_x)) return false;
            x = _x;
            z = _z;
            return true;
        }
    }

    // The verify probe: 3 of 5 Attacker nodes are across (region 1, toward the enemy at x 300, region 0), n3 and n4 still on the start bank (region 2).
    private static CommandNodeOperationalState[] MajorityCrossedNodes(CommandNodeRole role)
    {
        float[] xs = { 800f, 850f, 900f, 1250f, 1350f };
        var nodes = new CommandNodeOperationalState[xs.Length];
        for (int i = 0; i < xs.Length; i++)
            nodes[i] = CommandNodeOperationalState.Create("n" + i, CommandEchelonKind.BrigadeLike, role, CommandTaskType.FormUp, xs[i], -1000f + 60f * i, 270f);
        return nodes;
    }

    private static void AMajorityCrossedAttackerKeepsItsHomeNodesHome()
    {
        var picture = new RiverCrossingTests.Picture();
        CommandNodeOperationalState[] nodes = MajorityCrossedNodes(CommandNodeRole.FallbackGuard);
        BattlefieldPictureSnapshot enemy = EnemyLinePicture(300f, -1000f);
        foreach (BattleStance stance in new[] { BattleStance.Attacker, BattleStance.Meeting })
        {
            CommandDoctrineOrder[] free = Build(nodes, stance, enemy, null, TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.SoftAbort);
            CommandDoctrineOrder[] kept = Build(nodes, stance, enemy, picture, TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.SoftAbort, home: 2);
            for (int i = 0; i < nodes.Length; i++)
            {
                AssertEqual(CommandTaskType.FallBackToLine, kept[i].Task, stance + " n" + i + " falls back");
                bool atHome = picture.RegionAt(nodes[i].X, nodes[i].Z) == 2;
                if (atHome)
                {
                    AssertNear(free[i].FallbackTarget.X, kept[i].FallbackTarget.X, 0.01f, stance + " n" + i + " (at home) falls back unclamped, behind it, x " + kept[i].FallbackTarget.X);
                    AssertTrue(kept[i].FallbackTarget.X > nodes[i].X, stance + " n" + i + " (at home) does not fall back forward over the river");
                }
                else
                {
                    AssertEqual(2, picture.RegionAt(kept[i].FallbackTarget.X, kept[i].FallbackTarget.Z), stance + " n" + i + " (across) falls back home (x " + kept[i].FallbackTarget.X + ")");
                }
            }
        }
    }

    private static void AMajorityCrossedDefenderIsSentHome()
    {
        var picture = new RiverCrossingTests.Picture();
        CommandNodeOperationalState[] nodes = MajorityCrossedNodes(CommandNodeRole.Reserve);
        nodes[1] = CommandNodeOperationalState.Create("n1", CommandEchelonKind.BrigadeLike, CommandNodeRole.ScreeningForce, CommandTaskType.FormUp, nodes[1].X, nodes[1].Z, 270f);
        CommandDoctrineOrder[] orders = Build(nodes, BattleStance.Defender, EnemyLinePicture(300f, -1000f, exposed: false), picture, home: 2);
        int posts = 0;
        for (int i = 0; i < nodes.Length; i++)
        {
            foreach (DoctrineTargetPoint t in Posts(orders[i]))
            {
                if (!t.HasValue) continue;
                posts++;
                AssertEqual(2, picture.RegionAt(t.X, t.Z), "n" + i + " " + orders[i].Task + " post is on the home bank even though most of the force is across (x " + t.X + ")");
            }
        }

        AssertTrue(posts >= 4, "the Defender has posts (" + posts + ")");
    }

    private static void AFallBackIsNeverMovedForward()
    {
        var picture = new RiverCrossingTests.Picture();
        var node = CommandNodeOperationalState.Create("n", CommandEchelonKind.BrigadeLike, CommandNodeRole.FallbackGuard, CommandTaskType.FallBackToLine, 1200f, -1000f, 270f);
        var nodes = new[] { node };
        // Enemy to the west (x 300). A fall back east, off the home bank (region 3): the clamp would pull it west, toward the enemy: unclamped.
        DoctrineTargetPoint behind = DoctrineTargetPoint.From(1600f, -1000f);
        DoctrineTargetPoint kept = OwnBank.KeepFallBack(picture, 2, nodes, node, behind, 300f, -1000f);
        AssertNear(1600f, kept.X, 0.01f, "a clamp that would move the fall back toward the enemy is dropped");
        // A fall back that lies forward of the node, across the river (a plan's fall back toward the enemy bank): the clamp moves it back toward the
        // node, away from the enemy, so the clamped target is kept.
        DoctrineTargetPoint forward = DoctrineTargetPoint.From(900f, -1000f);
        DoctrineTargetPoint homeward = OwnBank.KeepFallBack(picture, 2, nodes, node, forward, 300f, -1000f);
        AssertEqual(2, picture.RegionAt(homeward.X, homeward.Z), "a fall back off the home bank is pulled onto it when that does not move it forward (x " + homeward.X + ")");
        AssertTrue(homeward.X > forward.X, "and the pull is away from the enemy");
        // On the home bank, an unknown home bank, no enemy distance: unchanged.
        AssertNear(1300f, OwnBank.KeepFallBack(picture, 2, nodes, node, DoctrineTargetPoint.From(1300f, -1000f), 300f, -1000f).X, 0.01f, "on the home bank: unchanged");
        AssertNear(1600f, OwnBank.KeepFallBack(picture, -1, nodes, node, behind, 300f, -1000f).X, 0.01f, "an unknown home bank judges nothing");
        AssertFalse(OwnBank.KeepFallBack(picture, 2, nodes, node, DoctrineTargetPoint.None, 300f, -1000f).HasValue, "no target stays none");
    }

    private static void ACrossedMeetingNodesPostsStayOnItsBank()
    {
        var picture = new RiverCrossingTests.Picture();
        CommandNodeOperationalState[] nodes = CrossedAttackerNodes(CommandNodeRole.SupportingAttack);
        foreach (BattleStance stance in new[] { BattleStance.Meeting, BattleStance.Attacker })
        {
            CommandDoctrineOrder[] orders = Build(nodes, stance, EnemyLinePicture(300f, -1000f, exposed: false), picture, TacticalOperationShape.SingleMainEffort, home: 2);
            for (int i = 0; i < 2; i++)
                AssertEqual(1, picture.RegionAt(orders[i].PrimaryTarget.X, orders[i].PrimaryTarget.Z), stance + " n" + i + " post stays on the bank it stands on (x " + orders[i].PrimaryTarget.X + ")");
        }
    }

    private static void ANodeWithNoPositionHolds()
    {
        // The order of an unplaced node, from the doctrine build.
        CommandDoctrineOrder[] orders = Build(EvidenceNodes(placed: false), BattleStance.Defender, EnemyLinePicture(exposed: false));
        var physical = new CommandPhysicalState(false, false, false, 0, false, 0);
        var eligibility = new WriteEligibilitySnapshot(modeAllowsWrites: true, playerProtected: false, routed: false, orderPending: false, recentOrder: false);
        int moving = 0;
        foreach (CommandDoctrineOrder o in orders)
        {
            AssertTrue(o.HasNoPost, o.NodeId + " " + o.Task + " has no post: " + o.Reason);
            PostureExecutionDecision d = CommandPostureExecutor.Decide(o, physical, eligibility, 10f);
            if (o.Task == CommandTaskType.FormUp || o.Task == CommandTaskType.GuardFlank || o.Task == CommandTaskType.Screen)
            {
                moving++;
                AssertEqual(PostureExecutionAction.NoWrite, d.Action, o.NodeId + " " + o.Task + " is no move");
                AssertEqual("no-position", d.Reason, "and says why");
            }
        }

        AssertTrue(moving >= 4, "the fixture has movement tasks (" + moving + ")");
        // The same task with a post moves; a reserve with no post idles; a fall back keeps its target.
        CommandDoctrineOrder placed = Build(EvidenceNodes(), BattleStance.Defender, EnemyLinePicture(exposed: false))[2];
        AssertEqual(CommandTaskType.FormUp, placed.Task, "a placed node forms up");
        AssertEqual(PostureExecutionAction.SetFormationAndWaypoint, CommandPostureExecutor.Decide(placed, physical, eligibility, 10f).Action, "and moves to its post");
        CommandDoctrineOrder reserve = Build(EvidenceNodes(placed: false), BattleStance.Defender, EnemyLinePicture(exposed: false))[4];
        AssertEqual(CommandTaskType.ReserveWait, reserve.Task, "the reserve role waits");
        AssertEqual("legal-idle", CommandPostureExecutor.Decide(reserve, physical, eligibility, 10f).Reason, "an unplaced reserve idles");
        CommandDoctrineOrder fall = CommandDoctrineOrder.Create("n", CommandNodeRole.Defender, CommandTaskType.FallBackToLine, "enemy-line-1",
            DoctrineTargetPoint.None, DoctrineTargetPoint.None, DoctrineTargetPoint.None, DoctrineAllowedIdleReason.None, 0f, 0f, 0.7f, TacticalBattleLinePlanner.NoPositionReason);
        AssertEqual(PostureExecutionAction.FallbackToLine, CommandPostureExecutor.Decide(fall, new CommandPhysicalState(false, false, true, 0, false, 0), eligibility, 10f).Action,
            "an interrupted fall back is still recovered: the withdrawal keeps its own target");
        AssertTrue(FlankResponseTests.SourceText("Tactical", "Operations", "TacticalBattleLinePlanner.cs").Contains("NoPositionReason"), "the planner names the reason");
    }

    private static void AnAttackersScreenKeepsItsTarget()
    {
        var picture = new RiverCrossingTests.Picture();
        var nodes = new[] { CommandNodeOperationalState.Create("n0", CommandEchelonKind.BrigadeLike, CommandNodeRole.ScreeningForce, CommandTaskType.FormUp, 1300f, -1000f, 90f) };
        BattlefieldPictureSnapshot enemy = EnemyLinePicture(700f, -1000f, exposed: false);
        CommandDoctrineOrder screen = Build(nodes, BattleStance.Attacker, enemy, picture, TacticalOperationShape.SingleMainEffort)[0];
        AssertEqual(CommandTaskType.Screen, screen.Task, "the screening role screens");
        AssertEqual(1, picture.RegionAt(screen.PrimaryTarget.X, screen.PrimaryTarget.Z),
            "an attacker's screen stays out across the river; the crossing gate routes or refuses it (x " + screen.PrimaryTarget.X + ")");
        CommandDoctrineOrder defender = Build(nodes, BattleStance.Defender, enemy, picture)[0];
        AssertEqual(2, picture.RegionAt(defender.PrimaryTarget.X, defender.PrimaryTarget.Z), "a defender's screen stays on its bank");
    }

    private static void AManeuverTargetIsLeftToThePlan()
    {
        // The plan chose the crossing: the maneuver's own target (across the river) is the order's primary; the bank rule does not touch it,
        // and it does not run after the maneuver replaced the primary.
        var picture = new RiverCrossingTests.Picture();
        var nodes = new[] { CommandNodeOperationalState.Create("node-7", CommandEchelonKind.DivisionLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 1300f, -1000f, 270f) };
        ManeuverOrder turn = new ManeuverOrder { Phase = ManeuverPhase.Turn, Task = CommandTaskType.AdvanceToAssembly, TargetX = 700f, TargetZ = -900f, Heading = 270f, Reason = "turn-to-line-end" };
        var table = EnvelopmentManeuver.ExpandToNodes(new[] { new ChildManeuver("child-7", turn, 1f, 0f) }, new[] { FlankResponseTests.NodeIntent("node-7", "node-7") });
        var operation = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Forming, "enemy-line-1", 900f);
        CommandDoctrineOrder o = CommandDoctrineAssignment.Build(nodes, operation, EnemyLinePicture(500f, -1000f, exposed: false), 20000f, 100f, 0.3f,
            BattleStance.Meeting, table, false, picture, 2)[0];
        AssertEqual(270f, o.ManeuverHeading, "the maneuver reached the order");
        AssertNear(700f, o.PrimaryTarget.X, 0.01f, "its target across the river is the order's primary");
        AssertTrue(!o.FallbackTarget.HasValue || o.FallbackTarget.X >= nodes[0].X, "while the node's fall back is behind it, never forward over the river (x " + o.FallbackTarget.X + ")");
    }

    private static void ThePullBackStopsAtTheFarthestPointOnTheBank()
    {
        var picture = new RiverCrossingTests.Picture();
        var node = CommandNodeOperationalState.Create("n", CommandEchelonKind.BrigadeLike, CommandNodeRole.Reserve, CommandTaskType.ReserveWait, 1400f, -100f, 0f);
        // Eighths of the way from x 1400 to x 100 are 1237, 1075, 912, ...: region 2 holds the first two, so the post is the second.
        DoctrineTargetPoint kept = OwnBank.Keep(picture, node, DoctrineTargetPoint.From(100f, -100f));
        AssertNear(1075f, kept.X, 1f, "the farthest point still on the bank, not the nearest");
    }

    private static void TheOwnFrontOverridesAnAvenueAnchor()
    {
        // An enemy-line objective whose approach avenue would anchor an intercept beyond the enemy line: the Defender still stands on its own front.
        var avenue = TacticalApproachAvenueEstimate.Create(new TacticalMapPoint(2200f, -1100f), new TacticalMapPoint(EnemyX, EnemyZ),
            TacticalApproachAvenueSource.VisibleEnemyLine, 0.9f, roadAnchored: false, crossingAnchored: false, "test-avenue");
        var picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate("enemy-line-1", TacticalObjectiveType.EnemyLine, 0f, 0.7f, false, 0.4f, EnemyX, EnemyZ, 0f, 0f, avenue),
        });
        CommandNodeOperationalState[] nodes = EvidenceNodes();
        bool ownFront = false;
        foreach (CommandDoctrineOrder o in Build(nodes, BattleStance.Defender, picture))
        {
            ownFront |= o.Reason.Contains("own-front");
            foreach (DoctrineTargetPoint t in Targets(o))
                if (t.HasValue) AssertTrue(AheadOfFront(nodes, t.X, t.Z) <= 1f, o.NodeId + " " + o.Task + " is ahead of the front");
        }

        AssertTrue(ownFront, "own-front despite the avenue and no enemy strength");
    }

    private static void ThePositionSurvivesEveryCopy()
    {
        var placed = CommandNodeOperationalState.Create("node-9", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.AttackObjective, 12f, 34f, 90f);
        var unplaced = new CommandNodeOperationalState("node-9", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.AttackObjective, CommandTaskState.Committed);
        AssertTrue(placed.WithRoleAndTask(CommandNodeRole.Reserve, CommandTaskType.ReserveWait).HasPosition, "a role rewrite keeps the position");
        AssertFalse(unplaced.WithRoleAndTask(CommandNodeRole.Reserve, CommandTaskType.ReserveWait).HasPosition, "and an unplaced node stays unplaced");

        // Works-hold guard.
        var ctx = new WorksGuardContext { WorksHold = true, FacesWorks = true };
        AssertTrue(WorksHoldGuard.ApplyState(in placed, ctx, out CommandNodeOperationalState held), "the works guard holds the attack");
        AssertTrue(held.HasPosition && held.X == 12f && held.Z == 34f, "and keeps where the node stands");
        AssertTrue(WorksHoldGuard.ApplyState(in unplaced, ctx, out CommandNodeOperationalState heldUnplaced), "the works guard holds an unplaced attack");
        AssertFalse(heldUnplaced.HasPosition, "and does not place it");

        // Stance guard.
        CommandNodeOperationalState byStance = placed;
        CommandDoctrineOrder noOrder = default(CommandDoctrineOrder);
        AssertTrue(PostureStanceGuard.Apply(BattleStance.Defender, ref byStance, false, ref noOrder), "the stance guard rewrites a Defender's attack");
        AssertTrue(byStance.HasPosition && byStance.X == 12f, "and keeps where the node stands");
        CommandNodeOperationalState byStanceUnplaced = unplaced;
        PostureStanceGuard.Apply(BattleStance.Defender, ref byStanceUnplaced, false, ref noOrder);
        AssertFalse(byStanceUnplaced.HasPosition, "an unplaced node stays unplaced");

        // Exploit overlay: the ledger's position, whatever the exploit's.
        var exploit = CommandNodeOperationalState.Create("node-9", CommandEchelonKind.Unknown, CommandNodeRole.SupportingAttack, CommandTaskType.AttackObjective, 900f, 900f, 0f);
        CommandNodeOperationalState overlaid = ExploitPosture.Overlay(placed, true, exploit, ExploitOverrideKind.Exploit);
        AssertTrue(overlaid.HasPosition && overlaid.X == 12f && overlaid.Z == 34f, "the overlay keeps the ledger's position");
        AssertFalse(ExploitPosture.Overlay(unplaced, true, exploit, ExploitOverrideKind.Exploit).HasPosition, "and the ledger's lack of one");

        // Nested division play.
        var tree = CommandTreeBuilder.Build(new[]
        {
            new CommandTreeBuilder.CommandProbe(100, 0, 0, 14, "One Division", true, false, false),
            new CommandTreeBuilder.CommandProbe(201, 100, 0, 13, "1st Brigade", true, false, false),
            new CommandTreeBuilder.CommandProbe(202, 100, 0, 13, "2nd Brigade", true, false, false),
            new CommandTreeBuilder.CommandProbe(203, 100, 0, 13, "3rd Brigade", true, false, false),
            new CommandTreeBuilder.CommandProbe(204, 100, 0, 13, "4th Brigade", true, false, false),
        }, 0, -1);
        var states = new List<CommandNodeOperationalState>();
        for (int i = 0; i < tree.Nodes.Count; i++)
        {
            bool placeIt = i != 2;   // one brigade stays unplaced
            states.Add(placeIt
                ? CommandNodeOperationalState.Create(tree.Nodes[i].NodeId, CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 100f + i, 200f + i, 0f)
                : new CommandNodeOperationalState(tree.Nodes[i].NodeId, CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, CommandTaskState.Planning));
        }

        var nested = TacticalNestedDivisionPlayPlanner.Apply(tree, states);
        int placedCount = 0, unplacedCount = 0;
        for (int i = 0; i < nested.Count; i++)
        {
            int at = states.FindIndex(x => x.NodeId == nested[i].NodeId);
            AssertEqual(states[at].HasPosition, nested[i].HasPosition, nested[i].NodeId + " keeps its placement through the nested division play");
            if (nested[i].HasPosition) { placedCount++; AssertEqual(states[at].X, nested[i].X, nested[i].NodeId + " keeps its x"); }
            else unplacedCount++;
        }

        AssertTrue(placedCount >= 3 && unplacedCount >= 1, "the fixture mixes placed and unplaced nodes (" + placedCount + "/" + unplacedCount + ")");
    }

    private static void ThePlayerOrderSnapshotUsesTheNodePosition()
    {
        // PlayerOrderRuntimeAdapter (runtime, excluded from the test csproj): the command-operation fallback used to hand the W&L order text
        // the state's X/Z, which were the map origin for every node; it now gets the node's own position, or no point.
        string adapter = FlankResponseTests.SourceText("Tactical", "PlayerOrders", "PlayerOrderRuntimeAdapter.cs");
        FlankResponseTests.AssertPinned(adapter,
            "state.HasPosition ? new PlayerOrderPoint(state.X, state.Z) : default(PlayerOrderPoint), state.HasPosition ? new PlayerOrderPoint(state.X, state.Z) : default(PlayerOrderPoint), army.CurrentOperation.PrimaryObjectiveId, \"command-operation\");",
            "an unplaced node gives no point");
    }

    // ---- the way back: a fall back is home for every stance ------------------------------------------------------------------------------

    // The verify probe: an Attacker or Meeting side that has crossed. n0 and n1 stand in region 1 (across, toward the enemy at x 300, region 0), n2..n4 on the
    // start bank (region 2). SoftAbort: every node falls back (role FallbackGuard).
    private static CommandNodeOperationalState[] CrossedAttackerNodes(CommandNodeRole role)
    {
        float[] xs = { 800f, 850f, 1150f, 1250f, 1350f };
        var nodes = new CommandNodeOperationalState[xs.Length];
        for (int i = 0; i < xs.Length; i++)
            nodes[i] = CommandNodeOperationalState.Create("n" + i, CommandEchelonKind.BrigadeLike, role, CommandTaskType.FormUp, xs[i], -1000f + 60f * i, 270f);
        return nodes;
    }

    private static void ACrossedNodesFallBackGoesHome()
    {
        var picture = new RiverCrossingTests.Picture();
        CommandNodeOperationalState[] nodes = CrossedAttackerNodes(CommandNodeRole.FallbackGuard);
        BattlefieldPictureSnapshot enemy = EnemyLinePicture(300f, -1000f);
        foreach (BattleStance stance in new[] { BattleStance.Attacker, BattleStance.Meeting, BattleStance.Defender })
        {
            CommandDoctrineOrder[] free = Build(nodes, stance, enemy, null, TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.SoftAbort);
            CommandDoctrineOrder[] kept = Build(nodes, stance, enemy, picture, TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.SoftAbort, home: 2);
            for (int i = 0; i < 2; i++)
            {
                AssertEqual(CommandTaskType.FallBackToLine, kept[i].Task, stance + " n" + i + " falls back");
                AssertTrue(free[i].FallbackTarget.HasValue && kept[i].FallbackTarget.HasValue, stance + " n" + i + " has a fall-back post");
                AssertEqual(2, picture.RegionAt(kept[i].FallbackTarget.X, kept[i].FallbackTarget.Z), stance + " n" + i + " falls back to the side's bank (x " + kept[i].FallbackTarget.X + ")");
                AssertNear(free[i].FallbackTarget.X, kept[i].FallbackTarget.X, 0.01f, stance + " n" + i + " fall-back x is the target without the clamp");
                AssertNear(free[i].FallbackTarget.Z, kept[i].FallbackTarget.Z, 0.01f, stance + " n" + i + " fall-back z is the target without the clamp");
            }
        }
    }

    private static void ABridgeheadPostIsNotPulledBack()
    {
        // An Attacker's crossed nodes form up on the bridgehead bank (region 1): the bank rule keeps a post on the bank its node stands on, not the
        // start bank; the fall back of the same order is home.
        var picture = new RiverCrossingTests.Picture();
        CommandNodeOperationalState[] nodes = CrossedAttackerNodes(CommandNodeRole.SupportingAttack);
        CommandDoctrineOrder[] orders = Build(nodes, BattleStance.Attacker, EnemyLinePicture(300f, -1000f, exposed: false), picture, TacticalOperationShape.SingleMainEffort);
        for (int i = 0; i < 2; i++)
        {
            AssertEqual(CommandTaskType.FormUp, orders[i].Task, "n" + i + " forms up");
            AssertEqual(1, picture.RegionAt(orders[i].PrimaryTarget.X, orders[i].PrimaryTarget.Z), "n" + i + " bridgehead post stays on the bridgehead bank (x " + orders[i].PrimaryTarget.X + ")");
            AssertEqual(2, picture.RegionAt(orders[i].FallbackTarget.X, orders[i].FallbackTarget.Z), "n" + i + " fall back is home (x " + orders[i].FallbackTarget.X + ")");
        }

        for (int i = 2; i < nodes.Length; i++)
            AssertEqual(2, picture.RegionAt(orders[i].PrimaryTarget.X, orders[i].PrimaryTarget.Z), "n" + i + " on the start bank keeps it");
    }

    private static void AManeuveringNodesSupportStaysAndFallBackGoesHome()
    {
        // A crossed node whose maneuver is a support attack: the support post is the task's (made behind the crossing gate, left where the plan
        // put it, across the river), its fall back is home on the side's bank.
        var picture = new RiverCrossingTests.Picture();
        CommandNodeOperationalState[] nodes = CrossedAttackerNodes(CommandNodeRole.SupportingAttack);
        // Deep in the bridgehead: 300 m behind it (the task's own fall back) is still across the river from the start bank.
        nodes[0] = CommandNodeOperationalState.Create("n0", CommandEchelonKind.BrigadeLike, CommandNodeRole.SupportingAttack, CommandTaskType.FormUp, 600f, -1000f, 270f);
        ManeuverOrder support = new ManeuverOrder { Phase = ManeuverPhase.Attack, Task = CommandTaskType.SupportAttack, TargetX = 330f, TargetZ = -990f, Heading = 270f, Reason = "attack-line-end" };
        var table = EnvelopmentManeuver.ExpandToNodes(new[] { new ChildManeuver("child-0", support, 1f, 0f) }, new[] { FlankResponseTests.NodeIntent("n0", "child-0") });
        var operation = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Committed, "enemy-line-1", 900f);
        CommandDoctrineOrder o = CommandDoctrineAssignment.Build(nodes, operation, EnemyLinePicture(300f, -1000f, exposed: false), 20000f, 100f, 0.3f,
            BattleStance.Attacker, table, false, picture, 2)[0];
        AssertEqual(CommandTaskType.SupportAttack, o.Task, "the maneuver's task");
        AssertTrue(o.SupportTarget.HasValue, "a support post");
        AssertEqual(0, picture.RegionAt(o.SupportTarget.X, o.SupportTarget.Z), "the support post stays across the river (x " + o.SupportTarget.X + ")");
        AssertEqual(2, picture.RegionAt(o.FallbackTarget.X, o.FallbackTarget.Z), "the fall back is home (x " + o.FallbackTarget.X + ")");
        CommandDoctrineOrder free = CommandDoctrineAssignment.Build(nodes, operation, EnemyLinePicture(300f, -1000f, exposed: false), 20000f, 100f, 0.3f,
            BattleStance.Attacker, table, false, null)[0];
        AssertEqual(1, picture.RegionAt(free.FallbackTarget.X, free.FallbackTarget.Z), "(without the rule it lies across: x " + free.FallbackTarget.X + ")");
    }

    private static ObjectiveRecord EnemyLineRecord(float x, float z)
        => new ObjectiveRecord(
            new ObjectiveObservationInput("enemy-line-1", TacticalObjectiveType.EnemyLine, TacticalObjectiveSource.VisibleEnemyLine,
                new TacticalMapPoint(x, z), 0.7f, 0.4f, typeAnchorVerified: true),
            TacticalObjectiveStatus.Contested, 3000f, 20000f);

    private static float Dist(float ax, float az, float bx, float bz)
    {
        float dx = ax - bx, dz = az - bz;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }
}
