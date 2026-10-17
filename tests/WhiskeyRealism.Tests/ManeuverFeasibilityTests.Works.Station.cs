using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static CommanderOpportunityTests;

// BC14W task 9: the Defender's works station (spec § 7A requirement-defender-works-reserve; § 13.2 DEF-WORKS-STATION). The allocator half is pure and runs
// here on scripted station points and times; the ledger half (the own works, the most threatened focal point, the times) runs on a Defender field in
// ManeuverFeasibilityTests.Works.StationLedger.cs.
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] WorksStationTests() => new (string, Action)[]
    {
        ("bc14w DEF-WORKS-STATION-PICK the eligible reserve with the least time to the station is given the guard point there and stays Reserve", WStationPick),
        ("bc14w DEF-WORKS-STATION-HOLD the binding keeps its child, moves to another focal point only when its arrival is earlier by the switch hours, and ends with the child's role", WStationHold),
        ("bc14w DEF-WORKS-STATION-ELIGIBLE a child the player holds or an exploit asset is never stationed, and the station is not a release: no floor, no hysteresis", WStationEligible),
        ("bc14w DEF-WORKS-STATION-SCOPE the binding is cleared with the battle's state and not by the trigger, and a crossing guard comes first", WStationScope),
        ("bc14w DEF-WORKS-STATION-LEDGER the Defender's own works give the station behind the focal point the enemy reaches first, and each child's time to it", WStationLedger),
        ("bc14w DEF-WORKS-STATION-NONE no station without own works, a perceived enemy within the threat hours, the Defender stance or the flag", WStationNone),
        ("bc14w DEF-WORKS-STATION-TIE equal arrivals go to the focal point with the larger enemy strength near it", WStationTie),
        ("bc14w DEF-WORKS-STATION-FILL the ledger's station and times reach the allocation by child id, NaN for a child it did not time", WStationFill),
        ("bc14w DEF-WORKS-STATION-GATE a station that appears, moves or ends reaches the operations ledger's change gate although the child's role does not change", WStationGate),
        ("bc14w DEF-WORKS-STATION-NODES a stationed Reserve child's nodes go to the station facing the focal point as ReserveWait, and a crossing guard is still a GuardFlank", WStationNodes),
    };

    static StationPoint StationAt(float focalX, float focalZ, float arrival, float x = 100f, float z = 50f, float heading = 90f)
        => new StationPoint { Has = true, FocalX = focalX, FocalZ = focalZ, X = x, Z = z, Heading = heading, ArrivalHours = arrival };

    static DirectChildSnapshot[] StationChildren(int n)
    {
        var snaps = new DirectChildSnapshot[n];
        for (int i = 0; i < n; i++) snaps[i] = new DirectChildSnapshot("c" + i, "a", 15, 0, "Corps" + i, true);
        return snaps;
    }

    static IReadOnlyList<DirectChildIntent> StationAllocate(DirectChildEvidence[] evidence, ReserveReleaseState state, double now, StationPoint station, float[] shifts,
        bool[] exploiting = null, CrossingThreat[] threats = null, float[] threatShifts = null, DirectChildSnapshot[] children = null)
    {
        var personality = new PersonalityVector(0.2f, 0f, 0f, 0f, 0f);
        var context = new ReserveReleaseContext(0.5f, false, state, exploiting, now, 0.5f, 0.5f, 0.5f, false, threats ?? new CrossingThreat[0],
            threats == null ? 0 : threats.Length, threatShifts, evidence.Length, null, station, shifts, evidence.Length);
        return DirectChildAllocator.AllocateWithZones(ResPlan(BattlePhase.MainEffort), personality, children ?? StationChildren(evidence.Length), evidence, null, null, context);
    }

    static void WStationPick()
    {
        ReserveReleaseState.ResetAll();
        var state = new ReserveReleaseState();
        DirectChildEvidence[] ev = ResEvidence(1, 3, 4);                   // c0 main; c1..c3 deployed reserves
        StationPoint station = StationAt(300f, 400f, 0.6f, x: 250f, z: 150f, heading: 45f);
        IReadOnlyList<DirectChildIntent> intents = StationAllocate(ev, state, 10.0, station, new[] { 9f, 0.4f, 0.2f, 0.9f });

        AssertEqual(DirectChildRole.Reserve, intents[2].Role, "the stationed child stays a Reserve");
        AssertTrue(intents[2].HasGuardPoint, "with the station as its guard point");
        AssertEqual(250f, intents[2].Guard.X, "at the station (x)");
        AssertEqual(150f, intents[2].Guard.Z, "(z)");
        AssertEqual(45f, intents[2].Guard.Heading, "facing the focal point");
        AssertTrue(state.Station.Has && state.Station.ChildId == "c2", "and bound");
        AssertEqual(0.6f, (float)state.Station.ArrivalAtChoice, "at the arrival it was chosen on");
        AssertTrue(state.Station.FocalX == 300f && state.Station.FocalZ == 400f, "for that focal point");
        for (int i = 0; i < intents.Count; i++) if (i != 2) AssertFalse(intents[i].HasGuardPoint, "one child per side: c" + i + " has no station");
        AssertEqual(DirectChildRole.Reserve, intents[1].Role, "the other reserves are untouched");
        AssertEqual(DirectChildRole.Reserve, intents[3].Role, "likewise");

        // No station: no guard, no binding.
        var none = new ReserveReleaseState();
        intents = StationAllocate(ev, none, 10.0, default(StationPoint), new[] { 9f, 0.4f, 0.2f, 0.9f });
        for (int i = 0; i < intents.Count; i++) AssertFalse(intents[i].HasGuardPoint, "no station, no guard (c" + i + ")");
        AssertFalse(none.Station.Has, "and no binding");
    }

    static void WStationHold()
    {
        ReserveReleaseState.ResetAll();
        var state = new ReserveReleaseState();
        DirectChildEvidence[] ev = ResEvidence(1, 3, 4);
        StationPoint first = StationAt(300f, 400f, 0.6f, x: 250f, z: 150f, heading: 45f);
        StationAllocate(ev, state, 10.0, first, new[] { 9f, 0.4f, 0.2f, 0.9f });
        AssertEqual("c2", state.Station.ChildId, "c2 is stationed");

        // Another reserve is nearer on the next allocation: the binding keeps its child.
        IReadOnlyList<DirectChildIntent> intents = StationAllocate(ev, state, 10.05, first, new[] { 9f, 0.05f, 0.2f, 0.9f });
        AssertTrue(intents[2].HasGuardPoint && !intents[1].HasGuardPoint, "the binding keeps c2");
        AssertEqual("c2", state.Station.ChildId, "still bound");

        // A different focal point 0.2 h earlier: not enough (0.25), the standing station stays, at its own point.
        StationPoint other = StationAt(-300f, 400f, 0.4f, x: -250f, z: 150f, heading: 315f);
        intents = StationAllocate(ev, state, 10.1, other, new[] { 9f, 0.05f, 0.2f, 0.9f });
        AssertEqual(250f, intents[2].Guard.X, "inside the switch margin the station stays at the first point");
        AssertEqual(300f, state.Station.FocalX, "bound to the first focal point");
        // 0.3 h earlier: the station moves, to the reserve nearest the new one.
        StationPoint earlier = StationAt(-300f, 400f, 0.3f, x: -250f, z: 150f, heading: 315f);
        intents = StationAllocate(ev, state, 10.15, earlier, new[] { 9f, 0.05f, 0.2f, 0.9f });
        AssertEqual("c1", state.Station.ChildId, "beyond the margin it is chosen anew: the nearest to the new station");
        AssertTrue(intents[1].HasGuardPoint && intents[1].Guard.X == -250f, "at the new point");
        AssertFalse(intents[2].HasGuardPoint, "and c2 is a plain reserve again");
        AssertEqual(0.3f, (float)state.Station.ArrivalAtChoice, "its arrival is the new reference");

        // The stationed child stops being a Reserve (a crisis release makes it a Fix): the binding ends and the next-nearest reserve is stationed.
        DirectChildEvidence[] crisis = ResEvidence(1, 3, 4);
        crisis[1] = Crisis(1);                                              // c1 now in crisis: c1 is no longer a reserve
        StationAllocate(crisis, state, 10.2, earlier, new[] { 9f, 0.05f, 0.2f, 0.9f });
        AssertTrue(state.Station.Has && state.Station.ChildId != "c1", "a child that is no longer a Reserve is not stationed: " + state.Station.ChildId);
    }

    static void WStationEligible()
    {
        ReserveReleaseState.ResetAll();
        DirectChildEvidence[] ev = ResEvidence(1, 3, 4);
        StationPoint station = StationAt(300f, 400f, 0.6f);

        // A child the player holds (NaN) is never stationed; the next nearest is.
        var state = new ReserveReleaseState();
        IReadOnlyList<DirectChildIntent> intents = StationAllocate(ev, state, 10.0, station, new[] { 9f, 0.4f, float.NaN, 0.9f });
        AssertFalse(intents[2].HasGuardPoint, "a player's child is not stationed");
        AssertTrue(intents[1].HasGuardPoint, "the next nearest is");

        // An exploit asset is not stationed.
        var exploit = new ReserveReleaseState();
        intents = StationAllocate(ev, exploit, 10.0, station, new[] { 9f, 0.4f, 0.2f, 0.9f }, exploiting: new[] { false, false, true, false });
        AssertFalse(intents[2].HasGuardPoint, "an exploit asset is not stationed");
        AssertTrue(intents[1].HasGuardPoint, "the next nearest is");

        // No eligible reserve: no station, no binding.
        var empty = new ReserveReleaseState();
        intents = StationAllocate(ev, empty, 10.0, station, new[] { 9f, float.NaN, float.NaN, float.NaN });
        for (int i = 0; i < intents.Count; i++) AssertFalse(intents[i].HasGuardPoint, "no eligible reserve (c" + i + ")");
        AssertFalse(empty.Station.Has, "and nothing bound");

        // A station is not a release: with a single reserve (BC13's floor of held reserves is 1) it is still stationed and still counts as a reserve.
        DirectChildEvidence[] single = ResEvidence(1);
        var one = new ReserveReleaseState();
        intents = StationAllocate(single, one, 10.0, station, new[] { 9f, 0.3f });
        AssertEqual(DirectChildRole.Reserve, intents[1].Role, "the only reserve keeps its role");
        AssertTrue(intents[1].HasGuardPoint, "and is stationed: the floor is not consulted");
        AssertEqual(0, one.ReleasedCount, "no release was recorded");
        // And it is not walked off by the release hysteresis: many allocations, one binding, never a role change.
        for (int k = 1; k < 30; k++)
        {
            intents = StationAllocate(single, one, 10.0 + 0.02 * k, station, new[] { 9f, 0.3f });
            AssertEqual(DirectChildRole.Reserve, intents[1].Role, "allocation " + k + ": still a Reserve");
            AssertTrue(intents[1].HasGuardPoint, "allocation " + k + ": still stationed");
        }
    }

    static void WStationScope()
    {
        ReserveReleaseState.ResetAll();
        DirectChildEvidence[] ev = ResEvidence(1, 3, 4);
        StationPoint station = StationAt(300f, 400f, 0.6f);
        var state = new ReserveReleaseState();
        StationAllocate(ev, state, 10.0, station, new[] { 9f, 0.4f, 0.2f, 0.9f });
        AssertTrue(state.Station.Has, "stationed");
        state.ClearTrigger();
        AssertTrue(state.Station.Has, "a station is not a trigger's release: ClearTrigger keeps it");
        state.Clear();
        AssertFalse(state.Station.Has, "Clear (a plan change, a new day, a stand-down) ends it");
        StationAllocate(ev, state, 10.1, station, new[] { 9f, 0.4f, 0.2f, 0.9f });
        ReserveReleaseState.ResetAll();
        AssertFalse(ReserveReleaseState.ForAlliance(0).Station.Has, "the battle's state starts clean");

        // The crossing release comes first: the child it takes is a RefuseLeft/Right guard, not a station; the station takes the next nearest reserve.
        var both = new ReserveReleaseState();
        var threat = new CrossingThreat { Link = 5, X = 100f, Z = 200f, Left = true, Sector = 0, OwnBankRegion = 0, GuardX = 10f, GuardZ = 20f, GuardHeading = 90f,
            EnemyAtCrossing = 0.4f, EnemyAtFlank = 0.7f, CandidateOwnShift = 9f };
        IReadOnlyList<DirectChildIntent> intents = StationAllocate(ev, both, 10.0, station, new[] { 9f, 0.4f, 0.2f, 0.9f },
            threats: new[] { threat }, threatShifts: new[] { 9f, 0.4f, 0.2f, 0.9f });
        AssertEqual(DirectChildRole.RefuseLeft, intents[2].Role, "c2 answers the crossing");
        AssertEqual(1, both.BoundCount, "bound to it");
        AssertTrue(both.Station.Has && both.Station.ChildId == "c1", "the station takes the next nearest reserve: " + both.Station.ChildId);
        AssertTrue(intents[1].HasGuardPoint && intents[1].Role == DirectChildRole.Reserve, "which stays a Reserve");
    }

    static void WStationGate()
    {
        ReserveReleaseState.ResetAll();
        DirectChildEvidence[] ev = ResEvidence(1, 3, 4);
        var state = new ReserveReleaseState();
        StationPoint first = StationAt(300f, 400f, 0.6f, x: 250f, z: 150f, heading: 45f);
        int none = DirectChildAllocator.GuardHash(StationAllocate(ev, new ReserveReleaseState(), 10.0, default(StationPoint), new[] { 9f, 0.4f, 0.2f, 0.9f }));
        int stationed = DirectChildAllocator.GuardHash(StationAllocate(ev, state, 10.0, first, new[] { 9f, 0.4f, 0.2f, 0.9f }));
        AssertTrue(stationed != none, "a station that appears changes the guard hash (the roles are all as before)");
        int jitter = DirectChildAllocator.GuardHash(StationAllocate(ev, state, 10.05, StationAt(300f, 400f, 0.6f, x: 260f, z: 150f, heading: 46f), new[] { 9f, 0.4f, 0.2f, 0.9f }));
        AssertEqual(stationed, jitter, "the same station is the same hash");
        StationPoint other = StationAt(-300f, 400f, 0.2f, x: -250f, z: 150f, heading: 315f);
        int moved = DirectChildAllocator.GuardHash(StationAllocate(ev, state, 10.1, other, new[] { 9f, 0.4f, 0.2f, 0.9f }));
        AssertTrue(moved != stationed, "a station that moves to another place or child changes it");
        int ended = DirectChildAllocator.GuardHash(StationAllocate(ev, state, 10.2, default(StationPoint), new[] { 9f, 0.4f, 0.2f, 0.9f }));
        AssertEqual(none, ended, "a station that ends is the hash of none");
        string runtime = FlankResponseTests.SourceText("Tactical", "Orchestrator", "TacticalBattleCoordinatorRuntime.cs");
        FlankResponseTests.AssertPinned(runtime, "* 31 + worksHoldHash) * 31 + side.Army.GuardHash);", "the coordinator's ledger gate reads the guard hash");
    }

    static void WStationNodes()
    {
        // Child 12 is a stationed reserve: its corps node and its division node take the station as their primary target, facing the focal point, as
        // ReserveWait; child 13 is a crossing guard (RefuseLeft) and is still a GuardFlank.
        var station = new DirectChildIntent("child-12", 15, 15, "Corps12", 1, DirectChildRole.Reserve, DirectChildAxis.SectorAxis, -1, 0f, default(TacticalIntentModel),
            guardPoint: new GuardPoint(250f, 150f, 45f));
        var guard = new DirectChildIntent("child-13", 15, 15, "Corps13", 2, DirectChildRole.RefuseLeft, DirectChildAxis.SectorAxis, 2, 0f, default(TacticalIntentModel),
            guardPoint: new GuardPoint(-250f, 150f, 315f));
        var nodeIntents = new[]
        {
            FlankResponseTests.NodeIntent("node-12", "node-12"), FlankResponseTests.NodeIntent("node-120", "node-12"),
            FlankResponseTests.NodeIntent("node-13", "node-13"),
        };
        ManeuverNodeTable table = EnvelopmentManeuver.ExpandToNodes(new ChildManeuver[0], nodeIntents, new[] { station, guard });
        bool hasCorps = table.TryGet("node-12", out ManeuverNodeEntry corps), hasDivision = table.TryGet("node-120", out ManeuverNodeEntry division);
        AssertTrue(hasCorps && hasDivision, "the stationed child's nodes are in the table");
        AssertEqual(CommandTaskType.ReserveWait, corps.Task, "a stationed reserve waits there");
        AssertEqual(CommandTaskType.ReserveWait, division.Task, "every node of it");
        AssertTrue(corps.TargetX == 250f && corps.TargetZ == 150f && corps.Heading == 45f, "at the station, facing the focal point");
        AssertTrue(corps.IsGuard, "a guard entry, so a Defender's build applies it");
        AssertEqual("station-works", corps.Reason, "named for the station");
        table.TryGet("node-13", out ManeuverNodeEntry crossing);
        AssertEqual(CommandTaskType.GuardFlank, crossing.Task, "a crossing guard is still a GuardFlank");
        AssertEqual("guard-crossing", crossing.Reason, "its reason is unchanged");

        // The doctrine order: ReserveWait with the idle a held reserve is allowed, the station as its target; the posture executor walks it there and,
        // once there, leaves it be.
        var nodes = new[]
        {
            new CommandNodeOperationalState("node-12", CommandEchelonKind.CorpsLike, CommandNodeRole.Reserve, CommandTaskType.ReserveWait, CommandTaskState.Planning),
            new CommandNodeOperationalState("node-120", CommandEchelonKind.DivisionLike, CommandNodeRole.Reserve, CommandTaskType.ReserveWait, CommandTaskState.Planning),
        };
        CommandDoctrineOrder[] orders = FlankResponseTests.DoctrineOrders(nodes, BattleStance.Defender, table);
        AssertEqual(CommandTaskType.ReserveWait, orders[0].Task, "the order waits");
        AssertTrue(orders[0].AllowsIdle, "a held reserve may stand");
        AssertTrue(orders[0].PrimaryTarget.HasValue && orders[0].PrimaryTarget.X == 250f && orders[0].PrimaryTarget.Z == 150f, "at the station");
        AssertEqual(45f, orders[0].ManeuverHeading, "facing the focal point");
        var away = new CommandPhysicalState(false, false, false, 0, false, 0);
        PostureExecutionDecision walk = CommandPostureExecutor.Decide(orders[0], away,
            new WriteEligibilitySnapshot(modeAllowsWrites: true, playerProtected: false, routed: false, orderPending: false, recentOrder: false), 10f);
        AssertEqual(PostureExecutionAction.SetWaypoint, walk.Action, "the group is walked to the station");
        AssertEqual(PostureExecutionTarget.DoctrinePrimaryTarget, walk.Target, "the order's target");
        PostureExecutionDecision there = CommandPostureExecutor.Decide(orders[0], away,
            new WriteEligibilitySnapshot(modeAllowsWrites: true, playerProtected: false, routed: false, orderPending: false, recentOrder: false, atAssignedLocation: true), 10f);
        AssertEqual(PostureExecutionAction.NoWrite, there.Action, "and left where it stands once there");
    }
}
