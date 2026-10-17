using System;
using WhiskeyRealism;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static BattleMovementHonestyTests;
using static ManeuverFeasibilityFixtures;

/// <summary>BC14O task 2: the ledger's objective table, the feasibility penalty of the army's main body for each objective (requirement-objective-feasibility).</summary>
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] ObjectiveTableTests() => new (string, Action)[]
    {
        ("bc14o OBJ-FEASIBILITY a river with no crossing makes an objective infeasible; after dark it stays feasible at full penalty; works raise the bar", ObjFeasibility),
        ("bc14o OBJ-PENALTY r uses the one main body, own 1 to take and the ground's ratio to hold, and ContestedWeight 0.6 on the strike legs", ObjPenalty),
        ("bc14o OBJ-TABLE which objectives are rows, the value and hold terms, the pressure published, flag off no table", ObjTable),
    };

    // Three corps abreast against an enemy line at depth 600 (the scenarios 1-3 field), on a flat map or the given one.
    static FeasField ObjField(BattleTerrainGrid grid = null, float corps = 12000f)
    {
        FeasField f = grid == null ? new FeasField() : new FeasField(grid);
        f.AddCorps(-300f, 0f, corps);
        f.AddCorps(0f, 0f, corps);
        f.AddCorps(300f, 0f, corps);
        f.AddEnemyLine(25000f);
        return f;
    }

    static ObjectiveEstimate Row(FeasibilityLedger ledger, string id)
    {
        for (int i = 0; i < ledger.ObjectiveCount; i++)
        {
            ledger.TryObjective(i, out ObjectiveEstimate e);
            if (e.Id == id) return e;
        }
        throw new Exception("no row " + id + " in a table of " + ledger.ObjectiveCount);
    }

    static void ObjFeasibility()
    {
        // A river across the front at depth 470-490 with no link: the objective behind it cannot be reached, the one on the own bank can.
        FeasField dry = ObjField(HorizontalBand(470f, 490f));
        dry.AddObjective("across", 0f, 700f, 50f);
        dry.AddObjective("near", 0f, 300f, 50f);
        FeasibilityLedger ledger = dry.Evaluate();
        ObjectiveEstimate across = Row(ledger, "across");
        AssertFalse(across.Feasible, "an objective behind a river with no crossing is infeasible for the main effort");
        AssertEqual("no-crossing", across.Reason, "named by the route screen's reason");
        AssertTrue(Row(ledger, "near").Feasible, "the objective on the own bank is feasible");

        // A Clear ford makes the same objective feasible.
        FeasField ford = ObjField(HorizontalBand(470f, 490f, (0f, 480f)));
        ford.AddObjective("across", 0f, 700f, 50f);
        AssertTrue(Row(ford.Evaluate(), "across").Feasible, "control: a ford makes the objective reachable");

        // After dark: T past the daylight left is daylight 1, never infeasible (D16).
        FeasField dark = ObjField();
        dark.DaylightHours = 0.3f;
        dark.AddObjective("far", 0f, 900f, 50f);
        ObjectiveEstimate late = Row(dark.Evaluate(), "far");
        AssertTrue(late.Feasible, "an objective reached after dark stays feasible");
        AssertNear(1f, late.Penalty, 1e-6f, "at the full daylight penalty");
        AssertEqual("after-dark", late.Reason, "named after dark");
        FeasField day = ObjField();
        day.AddObjective("far", 0f, 900f, 50f);
        AssertTrue(Row(day.Evaluate(), "far").Penalty < 1f, "control: with the day ahead it is not");

        // Works: an objective on the enemy's held works is held to three times the bar (the works doctrine on), not the open bar.
        bool worksBefore = Plugin.EnemyWorksDoctrine;
        try
        {
            FeasField withWorks = WorksFixtures.Scenario6();
            withWorks.AddObjective("works", 0f, 600f, 50f);
            Plugin.EnemyWorksDoctrine = true;
            ObjectiveEstimate onWorks = Row(withWorks.Evaluate(), "works");
            FeasField open = WorksFixtures.Scenario6();
            open.AddObjective("works", 0f, 600f, 50f);
            Plugin.EnemyWorksDoctrine = false;
            ObjectiveEstimate priced = Row(open.Evaluate(), "works");
            AssertTrue(onWorks.Penalty > priced.Penalty + 0.1f, "works raise the penalty: " + onWorks.Penalty + " against " + priced.Penalty + " priced as open ground");
            AssertEqual("works", onWorks.Reason, "named by the works bar");
        }
        finally { Plugin.EnemyWorksDoctrine = worksBefore; }
    }

    static void ObjPenalty()
    {
        // The objective is on the enemy line, the army a little short of the odds the commander wants: a partial detail penalty.
        FeasField baseline = ObjField(corps: 8000f);
        baseline.AddObjective("t", 0f, 600f, 50f);
        float take = Row(baseline.Evaluate(), "t").Penalty;
        AssertTrue(take > 0.2f && take < 0.9f, "the fixture gives a partial penalty, got " + take);

        // One hypothetical main body: a deployed reserve and a corps holding player-owned strength are not in it, however strong.
        FeasField reserve = ObjField(corps: 8000f);
        reserve.AddCorps(0f, 0f, 60000f, deployedReserve: true);
        reserve.AddObjective("t", 0f, 600f, 50f);
        AssertNear(take, Row(reserve.Evaluate(), "t").Penalty, 1e-6f, "the deployed reserve is not in the main body");
        FeasField player = ObjField(corps: 8000f);
        player.AddCorps(0f, 0f, 60000f, regimentFlags: UnitFlags.PlayerOwned);
        player.AddObjective("t", 0f, 600f, 50f);
        AssertNear(take, Row(player.Evaluate(), "t").Penalty, 1e-6f, "a corps holding player-owned strength is not in it");
        FeasField fourth = ObjField(corps: 8000f);
        fourth.AddCorps(0f, 0f, 8000f);
        fourth.AddObjective("t", 0f, 600f, 50f);
        AssertTrue(Row(fourth.Evaluate(), "t").Penalty < take - 0.1f, "control: an ordinary fourth corps strengthens the body and lowers the penalty");

        // Taking is an attack (own 1); holding an own objective is a defence from its ground (1.5 on open ground).
        FeasField hold = ObjField(corps: 8000f);
        hold.AddObjective("t", 0f, 600f, 50f, own: true, threatened: true);
        float held = Row(hold.Evaluate(), "t").Penalty;
        AssertTrue(held < take - 0.2f, "holding is priced at the ground's ratio, not 1: " + held + " against " + take);

        // The strike legs are valued at StrikeLegWeight and enter at ContestedWeight: a Held ford on every route is 0.6 x 0.5 x 1 = 0.3 with nothing else to pay.
        FeasField ford = new FeasField(HorizontalBand(470f, 490f, (0f, 480f)));
        ford.AddCorps(-300f, 0f, 12000f);
        ford.AddCorps(0f, 0f, 12000f);
        ford.AddCorps(300f, 0f, 12000f);
        ford.AddEnemy(0f, 520f, 400f, heading: 180f, fireRange: 100f);
        ford.AddObjective("t", 0f, 700f, 50f);
        ObjectiveEstimate across = Row(ford.Evaluate(), "t");
        AssertTrue(across.Feasible, "the ford carries the main body across");
        AssertNear(ObjectiveScoring.ContestedWeight * ManeuverFeasibility.StrikeLegWeight, across.Penalty, 1e-4f, "r is the Held ford's contested value alone");
        AssertEqual("contested", across.Reason, "named contested");
    }

    static void ObjTable()
    {
        // An own-held objective nobody threatens is no row; an own-held one under threat, an enemy-held and a neutral one are.
        FeasField f = ObjField();
        f.AddObjective("calm", -300f, 100f, 100f, own: true);
        f.AddObjective("hot", 300f, 100f, 40f, own: true, threatened: true);
        f.AddObjective("theirs", 0f, 500f, 50f);
        f.AddObjective("empty", 0f, 300f, 20f, neutral: true);
        // Ahead on points: the side holds 140 of 210.
        f.Clock = new VictoryClock { ShareOwn = 0.66f, ShareEnemy = 0.24f, Bar = 0.5f, DaylightHours = 10f };
        FeasibilityLedger ledger = f.Evaluate();
        AssertEqual(3, ledger.ObjectiveCount, "the calm own-held objective has no row");
        ObjectiveEstimate hot = Row(ledger, "hot"), theirs = Row(ledger, "theirs"), empty = Row(ledger, "empty");
        // v over every live objective's points, the calm one's 100 included.
        AssertNear(0.4f, hot.V, 1e-6f, "v is points over the largest live objective");
        AssertNear(ObjectiveScoring.Value(0.4f, true), hot.Value, 1e-6f, "an own one is worth 0.5625 of the value");
        AssertNear(ObjectiveScoring.Value(0.5f, false), theirs.Value, 1e-6f, "an enemy-held one is worth the full value");
        AssertNear(ObjectiveScoring.Value(0.2f, false), empty.Value, 1e-6f, "so is a neutral one");

        // The pressure is published with the table: ahead, so H = ahead x (1 - agg01); only the threatened own objective gains the hold term.
        ObjectivePressure p = ledger.Pressure;
        AssertNear(0.42f, p.Ahead, 1e-5f, "ahead by the share lead");
        AssertNear(0.42f * 0.5f, p.H, 1e-5f, "H for a mid commander");
        AssertNear(ObjectiveScoring.HoldWeight * p.H, hot.Hold, 1e-6f, "the threatened own objective gains HoldWeight x H");
        AssertNear(0f, theirs.Hold, 1e-9f, "an objective to take gains no hold term");

        // No clock, no pressure; the rows stand.
        FeasField noClock = ObjField();
        noClock.AddObjective("theirs", 0f, 500f, 50f);
        FeasibilityLedger l2 = noClock.Evaluate();
        AssertEqual(1, l2.ObjectiveCount, "a row without a clock");
        AssertNear(0f, l2.Pressure.U + l2.Pressure.H + l2.Pressure.Behind + l2.Pressure.Ahead, 1e-9f, "and no pressure");

        // The table holds at most MaxObjectives rows.
        FeasField many = ObjField();
        for (int i = 0; i < ManeuverFeasibility.MaxObjectives + 4; i++) many.AddObjective("o" + i, -450f + 50f * (i % 18), 300f + 10f * i, 10f + i);
        AssertEqual(ManeuverFeasibility.MaxObjectives, many.Evaluate().ObjectiveCount, "no more rows than MaxObjectives");

        // Flag off: no table and no pressure, whatever the input carries.
        bool before = Plugin.ObjectiveDoctrine;
        try
        {
            Plugin.ObjectiveDoctrine = false;
            FeasField off = ObjField();
            off.AddObjective("theirs", 0f, 500f, 50f);
            off.Clock = new VictoryClock { ShareOwn = 0f, ShareEnemy = 0.7f, Bar = 0.5f, DaylightHours = 1f };
            FeasibilityLedger lo = off.Evaluate();
            AssertEqual(0, lo.ObjectiveCount, "with Enable Objective Doctrine off there is no table");
            AssertNear(0f, lo.Pressure.U, 1e-9f, "and no pressure");
        }
        finally { Plugin.ObjectiveDoctrine = before; }
    }
}
