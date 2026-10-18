using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static BattleMovementHonestyTests;
using static ManeuverFeasibilityFixtures;

// BC14W task 9, the ledger's half of the Defender's works station (spec § 7A requirement-defender-works-reserve): the own works picture a Defender builds
// from its regiments standing in works cover, the focal point the enemy reaches first within FocalThreatHours, the station behind it and every child's
// time to it. The allocator's half is in ManeuverFeasibilityTests.Works.Station.cs.
internal static partial class ManeuverFeasibilityTests
{
    // Flat ground (or Scenario 4's river with its bridge and ford) with the side Defender and its middle and right corps in earth parapets (cover 0.45,
    // object 7): the own works are two held segments.
    static FeasField WorksDefenderField(Action<FeasField> more = null, bool river = false, float enemyDepth = 300f)
    {
        var f = river ? new FeasField(BandWithCrossings(240f, 260f, (0f, 250f, Bridge), (-900f, 250f, Ford))) : new FeasField();
        f.Stance = BattleStance.Defender;
        f.AddCorps(-300f, 0f, 12000f);
        f.AddCorps(0f, 0f, 11000f, cover: 0.45f, coverObject: 7);
        f.AddCorps(300f, 0f, 12000f, cover: 0.45f, coverObject: 7);
        f.AddEnemy(-400f, enemyDepth, 11000f);
        f.AddEnemy(0f, enemyDepth, 11500f);
        f.AddEnemy(400f, enemyDepth, 11000f);
        f.EnemyHalfExtent = 450f;
        more?.Invoke(f);
        return f;
    }

    static void WStationLedger()
    {
        FeasField f = WorksDefenderField();
        FeasibilityLedger ledger = DefenderLedger(f);
        AssertFalse(ledger.HeldWorks != null, "the Defender's own works are not the enemy's: the held-works query stays empty");
        var shifts = new float[ManeuverFeasibility.MaxChildren];
        AssertTrue(ledger.TryStation(out StationPoint station, shifts, out int children), "the Defender's ledger has a station");
        AssertTrue(station.Has, "which is set");
        AssertEqual(f.ChildIds.Count, children, "with a time for every child");
        for (int k = 0; k < children; k++) AssertEqual(f.ChildIds[k], ledger.StationChildId(k), "column " + k + " is child " + f.ChildIds[k]);
        AssertTrue(ledger.StationChildId(children) == null, "and no column past the last child");
        AssertTrue(station.ArrivalHours > 0f && station.ArrivalHours <= WhiskeyRealism.Tactical.Battle.WorksRules.FocalThreatHours, "the enemy reaches the focal point within the threat hours: " + station.ArrivalHours);

        // The focal point stands on the own works (the middle or the right corps' segment), and the station 150 behind it along the front axis, facing it.
        float lateral = station.FocalX - FeasField.X(0f);
        AssertTrue(Math.Abs(lateral) < 60f || Math.Abs(lateral - 300f) < 60f, "the focal point is on a held segment of the own works: lateral " + lateral);
        AssertNear(station.FocalZ, FeasField.Z(0f), 1f, "at the works' depth");
        AssertNear(station.FocalX, station.X, 1f, "the station stands straight behind it (forward is +z)");
        AssertNear(station.FocalZ - WhiskeyRealism.Tactical.Battle.WorksRules.ReserveStationMeters, station.Z, 1f, "ReserveStationMeters behind it");
        AssertNear(0f, station.Heading, 1f, "facing the focal point, which is straight ahead (+z)");

        // Every child's time is courier plus march, positive, and the nearest corps to the station is the quickest.
        for (int c = 0; c < children; c++) AssertTrue(shifts[c] > 0f && !float.IsNaN(shifts[c]), "child " + c + " has a time: " + shifts[c]);

        // The enemy's march is priced over the river's crossings: the same enemy across the river reaches the focal point later than on flat ground.
        float flatArrival = station.ArrivalHours;
        FeasibilityLedger across = DefenderLedger(WorksDefenderField(river: true));
        AssertTrue(across.TryStation(out StationPoint crossed, shifts, out _), "a station across the river");
        AssertTrue(crossed.ArrivalHours > flatArrival, "the crossing costs the enemy time: " + crossed.ArrivalHours + " against " + flatArrival);

        // A child with player-owned strength is not timed (NaN: never stationed).
        FeasField player = WorksDefenderField(g => g.AddCorps(600f, 0f, 9000f, regimentFlags: UnitFlags.PlayerOwned));
        FeasibilityLedger playerLedger = DefenderLedger(player);
        AssertTrue(playerLedger.TryStation(out _, shifts, out children), "a station with the player's corps present");
        AssertTrue(float.IsNaN(shifts[children - 1]), "the player's corps has no time");
        AssertFalse(float.IsNaN(shifts[0]), "an AI corps has");
    }

    static void WStationNone()
    {
        var shifts = new float[ManeuverFeasibility.MaxChildren];
        // Control: the station exists.
        AssertTrue(DefenderLedger(WorksDefenderField()).TryStation(out _, shifts, out _), "control: a station");

        // No own works: no regiment in cover.
        FeasField open = DefenderField();
        AssertFalse(DefenderLedger(open).TryStation(out _, shifts, out _), "no own works, no station");

        // No perceived enemy: BC10's assumed point never drives a station (as it never drives a crossing guard).
        FeasField blind = WorksDefenderField(g => { g.Enemies.Clear(); g.HasAssumedEnemy = true; g.AssumedEnemyLateral = 0f; g.AssumedEnemyDepth = 300f; });
        AssertFalse(DefenderLedger(blind).TryStation(out _, shifts, out _), "an assumed enemy point is not a threat to station against");

        // An enemy beyond the threat hours: 1,100 units away, a march of over two hours.
        FeasField far = WorksDefenderField(g => { g.Enemies.Clear(); g.AddEnemy(0f, 1100f, 30000f); });
        AssertFalse(DefenderLedger(far).TryStation(out _, shifts, out _), "the enemy is more than an hour from every focal point");

        // The Attacker's own regiments in cover are not a Defender's works.
        FeasField attacker = WorksDefenderField(g => g.Stance = BattleStance.Attacker);
        FeasibilityLedger attackerLedger = new FeasibilityLedger();
        attacker.Legs = StraightLegs(attacker, DefensivePlans);
        ManeuverFeasibility.Evaluate(attacker.Build(DefensivePlans), attackerLedger);
        AssertFalse(attackerLedger.TryStation(out _, shifts, out _), "an Attacker stations no reserve");

        // The flag.
        bool saved = WhiskeyRealism.Plugin.EnemyWorksDoctrine;
        try
        {
            WhiskeyRealism.Plugin.EnemyWorksDoctrine = false;
            AssertFalse(DefenderLedger(WorksDefenderField()).TryStation(out _, shifts, out _), "Enable Enemy Works Doctrine off: no station");
        }
        finally { WhiskeyRealism.Plugin.EnemyWorksDoctrine = saved; }
    }

    static void WStationTie()
    {
        // A flat field, the own works at lateral -400 and +400 (two segments), the enemy in two bodies straight ahead of them: equal arrivals by symmetry.
        FeasibilityLedger Build(float left, float right)
        {
            var f = new FeasField();
            f.Stance = BattleStance.Defender;
            f.AddCorps(-400f, 0f, 9000f, cover: 0.45f, coverObject: 7);
            f.AddCorps(0f, 0f, 9000f);
            f.AddCorps(400f, 0f, 9000f, cover: 0.45f, coverObject: 7);
            f.AddEnemy(-400f, 300f, left);
            f.AddEnemy(400f, 300f, right);
            f.EnemyHalfExtent = 450f;
            return DefenderLedger(f);
        }
        var shifts = new float[ManeuverFeasibility.MaxChildren];
        AssertTrue(Build(8000f, 15000f).TryStation(out StationPoint right, shifts, out _), "a station");
        AssertTrue(right.FocalX - FeasField.X(0f) > 300f, "the larger body is on the right: its focal point wins the tie, lateral " + (right.FocalX - FeasField.X(0f)));
        AssertTrue(Build(15000f, 8000f).TryStation(out StationPoint left, shifts, out _), "a station");
        AssertTrue(left.FocalX - FeasField.X(0f) < -300f, "the larger body is on the left: lateral " + (left.FocalX - FeasField.X(0f)));
    }

    static void WStationFill()
    {
        FeasField f = WorksDefenderField();
        FeasibilityLedger ledger = DefenderLedger(f);
        var snapshots = new DirectChildSnapshot[f.ChildIds.Count + 1];
        for (int i = 0; i < f.ChildIds.Count; i++) snapshots[i] = new DirectChildSnapshot(f.ChildIds[i], "a", 15, 0, "Corps" + i, true);
        snapshots[snapshots.Length - 1] = new DirectChildSnapshot("child-new", "a", 15, 0, "New", true);
        var buffer = new float[ManeuverFeasibility.MaxChildren];
        var row = new float[ManeuverFeasibility.MaxChildren];
        ArmyOrchestrator.FillStation(ledger, snapshots, buffer, row, out StationPoint station, out int count);
        AssertTrue(station.Has, "the station reaches the allocation");
        AssertEqual(snapshots.Length, count, "with a time per allocation child");
        for (int c = 0; c < f.ChildIds.Count; c++) AssertFalse(float.IsNaN(buffer[c]), "child " + c + " is timed");
        AssertTrue(float.IsNaN(buffer[snapshots.Length - 1]), "a child the ledger did not time is NaN: never stationed");
        // The allocation's order is its own: reversed snapshots read the same times by id.
        var reversed = new DirectChildSnapshot[f.ChildIds.Count];
        for (int i = 0; i < reversed.Length; i++) reversed[i] = snapshots[reversed.Length - 1 - i];
        var other = new float[ManeuverFeasibility.MaxChildren];
        ArmyOrchestrator.FillStation(ledger, reversed, other, row, out _, out _);
        for (int c = 0; c < reversed.Length; c++) AssertEqual(buffer[reversed.Length - 1 - c], other[c], "reversed order: child " + c);
        // More children than the ledger's cap: no station.
        var many = new DirectChildSnapshot[ManeuverFeasibility.MaxChildren + 1];
        for (int i = 0; i < many.Length; i++) many[i] = snapshots[0];
        ArmyOrchestrator.FillStation(ledger, many, buffer, row, out station, out count);
        AssertFalse(station.Has, "over the cap: none");
        AssertEqual(0, count, "and no children");
        // The orchestrator hands the station to the allocation and the allocator's guard point to the doctrine.
        string army = FlankResponseTests.SourceText("Tactical", "Orchestrator", "ArmyOrchestrator.cs");
        FlankResponseTests.AssertPinned(army, "if (feasible && _feasibility != null && Plugin.EnemyWorksDoctrineEnabled())", "the station is read behind both flags");
        FlankResponseTests.AssertPinned(army, "FillStation(_feasibility, _directChildSnapshots, _stationShiftBuffer, _stationRow, out station, out stationChildren);", "from the ledger");
        // An empty ledger: none.
        ArmyOrchestrator.FillStation(new FeasibilityLedger(), snapshots, buffer, row, out station, out count);
        AssertFalse(station.Has, "no evaluation: none");
    }
}
