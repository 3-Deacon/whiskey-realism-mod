using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static TestFixtures;
using static ManeuverFeasibilityFixtures;

/// <summary>BC14O task 2: the victory clock and the live objectives it reads (requirement-victory-pressure).</summary>
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] ObjectivePressureTests() => new (string, Action)[]
    {
        ("bc14o OBJ-PRESSURE behind, ahead, late, U and H by their formulas; the bar counts as well as the points; 0.5 reads even", ObjPressure),
        ("bc14o OBJ-PRESSURE-SHARES the held shares are over the live points with no 0.01 term, an inactive objective changes neither, continuous mode uses the accrued totals", ObjPressureShares),
        ("bc14o OBJ-THREAT an own-held objective is threatened only by a perceived sample within 250", ObjThreat),
        ("bc14o OBJ-ALLIANCEPOINTS the value is points in both modes and alliancepoints is not read", ObjAlliancePoints),
    };

    static VictoryClock Clock(float own, float enemy, float bar, float daylight) => new VictoryClock { ShareOwn = own, ShareEnemy = enemy, Bar = bar, DaylightHours = daylight };

    static void ObjPressure()
    {
        // Scenario 10 at 19:00: the enemy holds 70 of 100 points, the bar is even, one hour of daylight, a mid commander.
        ObjectivePressure p = ObjectiveScoring.Pressure(Clock(0f, 0.7f, 0.5f, 1f), 0.5f);
        AssertNear(0.7f, p.Behind, 1e-5f, "behind is the share deficit");
        AssertNear(0f, p.Ahead, 1e-5f, "not ahead");
        AssertNear(0.75f, p.Late, 1e-5f, "an hour left of four is 0.75 late");
        AssertNear(0.525f, p.U, 1e-5f, "U = behind x late x (0.5 + agg01) = 0.7 x 0.75 x 1.0");
        AssertNear(0f, p.H, 1e-5f, "a side behind does not hold");

        // The day young: no urgency. The clock four hours out is still not late.
        AssertNear(0f, ObjectiveScoring.Pressure(Clock(0f, 0.7f, 0.5f, 10f), 0.5f).U, 1e-6f, "U is 0 with the day young");
        AssertNear(0f, ObjectiveScoring.Pressure(Clock(0f, 0.7f, 0.5f, 4f), 0.5f).Late, 1e-6f, "four hours of daylight is not late");
        AssertNear(1f, ObjectiveScoring.Pressure(Clock(0f, 0.7f, 0.5f, 0f), 0.5f).Late, 1e-6f, "no daylight is fully late");

        // The commander's aggression scales the urgency: an aggressive one presses harder, a cautious one less.
        float cautious = ObjectiveScoring.Pressure(Clock(0f, 0.7f, 0.5f, 1f), 0f).U;
        float aggressive = ObjectiveScoring.Pressure(Clock(0f, 0.7f, 0.5f, 1f), 1f).U;
        AssertNear(0.7f * 0.75f * 0.5f, cautious, 1e-5f, "agg01 0 gives half the urgency");
        AssertNear(Math.Min(1f, 0.7f * 0.75f * 1.5f), aggressive, 1e-5f, "agg01 1 gives one and a half times, clamped to 1");

        // The bar's deficit counts as well as the points share: even shares with the bar at 0.3 are 0.4 behind.
        ObjectivePressure bar = ObjectiveScoring.Pressure(Clock(0.3f, 0.3f, 0.3f, 1f), 0.5f);
        AssertNear(0.4f, bar.Behind, 1e-5f, "behind from the bar alone: 2 x (0.5 - 0.3)");
        AssertNear(0.4f * 0.75f, bar.U, 1e-5f, "and U follows it");
        // The larger of the two counts, not their sum.
        AssertNear(0.7f, ObjectiveScoring.Pressure(Clock(0f, 0.7f, 0.4f, 1f), 0.5f).Behind, 1e-5f, "the share deficit 0.7 beats the bar's 0.2");
        AssertNear(0.6f, ObjectiveScoring.Pressure(Clock(0.5f, 0.5f, 0.2f, 1f), 0.5f).Behind, 1e-5f, "and the bar's 0.6 beats the even shares");

        // 0.5 reads as even, whatever produced it (a degenerate vanilla case among them).
        ObjectivePressure even = ObjectiveScoring.Pressure(Clock(0.4f, 0.4f, 0.5f, 1f), 0.5f);
        AssertNear(0f, even.Behind, 1e-6f, "even shares and an even bar: not behind");
        AssertNear(0f, even.Ahead, 1e-6f, "nor ahead");

        // Scenario 11: ahead by 0.3; H = ahead x (1 - agg01).
        ObjectivePressure ahead = ObjectiveScoring.Pressure(Clock(0.65f, 0.35f, 0.5f, 10f), 0.5f);
        AssertNear(0.3f, ahead.Ahead, 1e-5f, "ahead is the share lead");
        AssertNear(0.15f, ahead.H, 1e-5f, "H = 0.3 x 0.5 for a mid commander");
        AssertNear(0.03f, ObjectiveScoring.Pressure(Clock(0.65f, 0.35f, 0.5f, 10f), 0.9f).H, 1e-5f, "an aggressive commander (0.9) holds little: 0.03");
        AssertNear(0f, ObjectiveScoring.Pressure(Clock(0.65f, 0.35f, 0.5f, 10f), 1f).H, 1e-6f, "agg01 1 never holds");
        AssertNear(0.6f, ObjectiveScoring.Pressure(Clock(0.4f, 0.4f, 0.8f, 10f), 0f).Ahead, 1e-5f, "ahead from the bar alone: 2 x (0.8 - 0.5)");
    }

    static MapObjectiveCapture LiveCapture(string id, int owner, int points, float x = 120f, float z = 220f, bool live = true)
        => new MapObjectiveCapture(id, new TacticalMapPoint(x, z), owner, live, points, live);

    static void ObjPressureShares()
    {
        // Own alliance 0. The enemy (1) holds A (70); B (30) is neutral. An inactive objective with 500 points is in neither share.
        var captures = new[]
        {
            LiveCapture("A", 1, 70, 100f, 100f), LiveCapture("B", -1, 30, 200f, 100f),
            new MapObjectiveCapture("X", new TacticalMapPoint(300f, 100f), 1, false, 500, false),
        };
        var facts = new ObjectiveFact[8];
        int n = ObjectiveScoring.FactsOf(captures, 0, null, facts);
        AssertEqual(2, n, "the inactive objective is not a fact");
        VictoryClock c = ObjectiveScoring.Clock(facts, n, false, 0f, 0f, 0.5f, 10f);
        AssertNear(0f, c.ShareOwn, 1e-6f, "the side holds none");
        AssertNear(0.7f, c.ShareEnemy, 1e-6f, "the enemy holds 70 of 100, with no 0.01 per objective and no dead objective in the divisor");
        AssertTrue(facts[0].EnemyHeld && !facts[0].OwnHeld, "an objective another alliance holds is enemy-held");
        AssertTrue(!facts[1].EnemyHeld && !facts[1].OwnHeld, "a neutral one is neither");

        // Scenario 11: the side holds A (65), the enemy B (35).
        var held = new[] { LiveCapture("A", 0, 65), LiveCapture("B", 1, 35) };
        n = ObjectiveScoring.FactsOf(held, 0, null, facts);
        c = ObjectiveScoring.Clock(facts, n, false, 0f, 0f, 0.5f, 10f);
        AssertNear(0.65f, c.ShareOwn, 1e-6f, "own share");
        AssertNear(0.35f, c.ShareEnemy, 1e-6f, "enemy share");
        // The same captures from the other alliance's side.
        n = ObjectiveScoring.FactsOf(held, 1, null, facts);
        c = ObjectiveScoring.Clock(facts, n, false, 0f, 0f, 0.5f, 10f);
        AssertNear(0.35f, c.ShareOwn, 1e-6f, "from the other side the shares swap");

        // No live points at all: the shares are 0 over max(1, 0), not NaN.
        c = ObjectiveScoring.Clock(new ObjectiveFact[1], 0, false, 0f, 0f, 0.5f, 10f);
        AssertNear(0f, c.ShareOwn + c.ShareEnemy, 1e-6f, "an empty table has no shares");

        // Continuous mode: the accrued totals, each over max(1, both); the held points are not read.
        c = ObjectiveScoring.Clock(facts, n, true, 30f, 10f, 0.5f, 10f);
        AssertNear(0.75f, c.ShareOwn, 1e-6f, "accrued own 30 of 40");
        AssertNear(0.25f, c.ShareEnemy, 1e-6f, "accrued enemy 10 of 40");
        c = ObjectiveScoring.Clock(facts, n, true, 0f, 0f, 0.5f, 10f);
        AssertNear(0f, c.ShareOwn + c.ShareEnemy, 1e-6f, "nothing accrued yet: no shares");
        c = ObjectiveScoring.Clock(facts, n, false, 30f, 10f, 0.5f, 10f);
        AssertNear(0.35f, c.ShareOwn, 1e-6f, "outside continuous mode the accrued totals are ignored");

        // More live objectives than the table holds: the most valuable are kept.
        var many = new List<MapObjectiveCapture>();
        for (int i = 0; i < ManeuverFeasibility.MaxObjectives + 4; i++) many.Add(LiveCapture("O" + i, 1, 10 + i, 100f + i, 100f));
        var table = new ObjectiveFact[ManeuverFeasibility.MaxObjectives];
        AssertEqual(ManeuverFeasibility.MaxObjectives, ObjectiveScoring.FactsOf(many, 0, null, table), "the table is full");
        float least = float.MaxValue;
        for (int i = 0; i < table.Length; i++) least = Math.Min(least, table[i].Points);
        AssertNear(14f, least, 1e-6f, "the four least valuable (10-13 points) were dropped");
    }

    static void ObjThreat()
    {
        var f = new FeasField();
        f.AddCorps(0f, 0f, 12000f);
        // A perceived (Visible) sample 200 from the objective at (0, 300): it threatens.
        f.AddEnemy(0f, 500f, 4000f);
        BattleSnapshot s = f.Snapshot();
        float x = FeasField.X(0f), z = FeasField.Z(300f);
        AssertTrue(ObjectiveScoring.Threatened(s, x, z), "a perceived sample within 250 threatens the objective");
        AssertFalse(ObjectiveScoring.Threatened(s, x, FeasField.Z(200f)), "contrast: the same sample 300 away does not");

        // An enemy regiment within 250 that the side does not perceive (a true record, no observation) threatens nothing.
        var hidden = new FeasField();
        hidden.AddCorps(0f, 0f, 12000f);
        hidden.HiddenEnemies.Add(new UnitRecord { InstanceId = 7001, UnitTyp = TacticalUnitType.Infantry, X = x, Z = z, Strength = 4000, Flags = UnitFlags.Alive | UnitFlags.Active });
        AssertFalse(ObjectiveScoring.Threatened(hidden.Snapshot(), x, z), "an enemy regiment the side does not perceive is no threat");

        // A remembered sample below the confidence floor, and a reinforcement column, are not samples.
        var weak = new FeasField();
        weak.AddCorps(0f, 0f, 12000f);
        weak.AddEnemy(0f, 400f, 4000f, state: ObservationState.Remembered, confidence: 0.3f);
        AssertFalse(ObjectiveScoring.Threatened(weak.Snapshot(), x, z), "a remembered sample below 0.5 confidence is not one");
        var memory = new FeasField();
        memory.AddCorps(0f, 0f, 12000f);
        memory.AddEnemy(0f, 400f, 4000f, state: ObservationState.Remembered, confidence: 0.8f);
        AssertTrue(ObjectiveScoring.Threatened(memory.Snapshot(), x, z), "contrast: a remembered one at 0.8 is");

        // Through the facts: only an own-held objective is threatened; an enemy-held one is the target, not the held ground.
        var captures = new[] { LiveCapture("own", 0, 50, x, z), LiveCapture("theirs", 1, 50, x, z) };
        var facts = new ObjectiveFact[4];
        ObjectiveScoring.FactsOf(captures, 0, s, facts);
        AssertTrue(facts[0].Threatened, "the own-held objective is threatened");
        AssertFalse(facts[1].Threatened, "the enemy-held one is not marked");
        AssertFalse(ObjectiveScoring.Threatened(null, x, z), "no snapshot, no threat");
    }

    static void ObjAlliancePoints()
    {
        // vanilla's alliancepoints is held-points bookkeeping, 0 for everyone but the holder: an enemy-held objective with alliancepoints[own] 0 still has
        // its points. The cached capture reads points only; the objective's alliancepoints is planted so a read would show.
        var theirs = UninitialisedObjective("Little Round Top", 1, 100);
        theirs.alliancepoints = new float[] { 0f, 0f };
        var ours = UninitialisedObjective("Culp's Hill", 0, 40);
        ours.alliancepoints = new float[] { 9000f, 0f };
        try
        {
            FillSceneCache(new[] { theirs, ours });
            MapObjectiveCapture[] captures = TacticalStableSceneCache.GetOrCaptureMapObjectives();
            AssertEqual(100, captures[0].Points, "the enemy-held objective has its points although alliancepoints[own] is 0");
            AssertEqual(40, captures[1].Points, "and an own one its points, not its accrued alliancepoints");
            // The native liveness flag reads false here: the same captures, live, as the normalisation sees them.
            var live = new List<MapObjectiveCapture>();
            foreach (MapObjectiveCapture c in captures) live.Add(new MapObjectiveCapture(c.ObjectiveId, c.Point, c.Owner, true, c.Points, true));
            AssertNear(100f, TacticalVisionRuntimeAdapter.MaxLivePoints(live), 1e-6f, "the normalising max is over points, not the 9000 accrued");
        }
        finally { TacticalStableSceneCache.Clear(); }
    }
}
