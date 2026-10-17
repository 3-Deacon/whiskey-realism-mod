using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static TestFixtures;
using static ManeuverFeasibilityFixtures;
using static WorksFixtures;

// BC14W task 4: works pricing and turn first (spec § 7A requirement-works-pricing, requirement-turn-first; § 13.2 WORKS-BAR, WORKS-PIN, WORKS-SINGLE,
// TURN-FIX-OPEN). The § 6 scenario 6 field: the authored ManeuverAndFix strikes 300 units beyond the open right end, so it pays no works bar.
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] WorksPricingTests() => new (string, Action)[]
    {
        ("bc14w WORKS-BAR a body striking on held works is held to three times the commander's bar, one striking beyond the open end to the bar", WBar),
        ("bc14w WORKS-PIN a body whose strike point lies on held works pins at a defence ratio of at least 3.0", WPin),
        ("bc14w WORKS-SINGLE a single-body plan that strikes held works gets a detail term of its own: the window is the assault hour, nothing is pinned, the odds are an attacker's", WSingle),
        ("bc14w TURN-FIX-OPEN a turning plan's fix on held works keeps the bar and counts only the enemy outside works", WTurnFixOpen),
    };

    // ManeuverAndFix on the § 6 scenario 6 field as the evaluation scored it: the authored side and the mirrored side (the ledger keeps the better).
    static void MafSides(FeasField f, out BodyEstimate[] authored, out BodyEstimate[] mirrored)
    {
        FeasibilityJob job = f.Evaluate(BattlePlanId.ManeuverAndFix).Job;
        authored = new BodyEstimate[job.SideA.BodyCount];
        Array.Copy(job.SideA.Bodies, authored, authored.Length);
        mirrored = new BodyEstimate[job.SideB.BodyCount];
        Array.Copy(job.SideB.Bodies, mirrored, mirrored.Length);
    }

    static BodyEstimate MainOf(BodyEstimate[] bodies)
    {
        for (int i = 0; i < bodies.Length; i++) if (bodies[i].Main) return bodies[i];
        throw new Exception("the plan has no main body");
    }

    static BodyEstimate FirstWing(BodyEstimate[] bodies)
    {
        for (int i = 0; i < bodies.Length; i++) if (bodies[i].Envelops) return bodies[i];
        throw new Exception("the plan has no enveloping body");
    }

    static void WBar()
    {
        FeasField f = Scenario6();
        MafSides(f, out BodyEstimate[] authored, out BodyEstimate[] mirrored);
        float bar = ManeuverFeasibility.Bar(f.Army);
        AssertNear(1.0f, bar, 1e-4f, "the mid commander's bar");

        // Authored on the right: the wing strikes (450, 600), 300 units beyond the works' open end.
        BodyEstimate wing = FirstWing(authored);
        AssertFalse(wing.StrikeOnWorks, "its strike point is beyond the works' open end");
        AssertFalse(wing.AssaultsWorks, "so it assaults nothing");
        AssertNear(bar, wing.BarUsed, 1e-4f, "and is held to the commander's bar");

        // Mirrored: the wing strikes (-450, 600) on the works.
        BodyEstimate mirroredWing = FirstWing(mirrored);
        AssertTrue(mirroredWing.StrikeOnWorks && mirroredWing.AssaultsWorks, "the mirrored wing strikes the works");
        AssertNear(WorksRules.WorksOddsFactor * bar, mirroredWing.BarUsed, 1e-4f, "and is held to three times the bar");
        AssertNear(1f, mirroredWing.Detail, 0.01f, "its detail is the full penalty");

        // DoubleEnvelopment: the left wing strikes the works, the right wing the open end.
        BodyEstimate[] de = BodiesArray(f.Evaluate(BattlePlanId.DoubleEnvelopment), BattlePlanId.DoubleEnvelopment);
        BodyEstimate right = RightWing(de), left = de[0].Envelops && de[0].TargetX < right.TargetX ? de[0] : de[1];
        AssertTrue(left.TargetX < right.TargetX, "the left wing stands left of the right wing");
        AssertTrue(left.AssaultsWorks && !right.AssaultsWorks, "the left wing assaults the works, the right goes round");
        AssertNear(3f * bar, left.BarUsed, 1e-4f, "three times the bar on the left");
        AssertNear(bar, right.BarUsed, 1e-4f, "the bar on the right");
    }

    static void WPin()
    {
        FeasField f = Scenario6();
        MafSides(f, out BodyEstimate[] authored, out _);
        BodyEstimate wing = FirstWing(authored), fix = MainOf(authored);
        AssertTrue(fix.StrikeOnWorks, "the fix strikes the enemy anchor, which lies on the held segment");
        AssertTrue(fix.DefenseRatio >= WorksRules.WorksDefenseRatio - 1e-4f, "so it pins at a defence ratio of at least 3.0, got " + fix.DefenseRatio);
        AssertNear(23000f / 3f, wing.Pin, 120f, "the wing's pinned enemy is the fix's 23,000 / 3.0 = 7,700 (15,300 on open ground)");
        AssertTrue(wing.DefenseRatio < WorksRules.WorksDefenseRatio, "the wing's own strike point is off the works: the ground's ratio");

        // The same army against the same enemy not in works: the fix pins at the ground's 1.5.
        var open = new FeasField();
        open.AddCorps(-300f, 0f, 12000f);
        open.AddCorps(0f, 0f, 11000f);
        open.AddCorps(300f, 0f, 12000f);
        open.AddEnemyLine(25000f);
        MafSides(open, out BodyEstimate[] openAuthored, out _);
        AssertNear(1.5f, MainOf(openAuthored).DefenseRatio, 1e-3f, "off works the fix pins at 1.5");
        AssertNear(23000f / 1.5f, FirstWing(openAuthored).Pin, 120f, "and the wing's pinned enemy is 15,300");
    }

    static void WSingle()
    {
        FeasField f = Scenario6();
        FeasibilityLedger ledger = f.Evaluate(BattlePlanId.GenericAggressive);
        BodyEstimate[] bodies = BodiesArray(ledger, BattlePlanId.GenericAggressive);
        AssertEqual(1, bodies.Length, "a single-body plan");
        BodyEstimate body = bodies[0];
        AssertTrue(body.StrikeOnWorks && body.AssaultsWorks, "its main body strikes the enemy anchor, on held works");
        AssertNear(WorksRules.AssaultHours, body.ExposureWindowHours, 1e-4f, "its window is the assault hour: W = 1.0");
        AssertNear(0f, body.Pin, 1e-4f, "nothing is pinned");
        AssertNear(1f, body.OwnRatio, 1e-4f, "the odds are an attacker's: own 1");
        AssertNear(3f, body.BarUsed, 1e-4f, "against three times the bar");
        AssertNear(27500f, body.EnemyAgainst, 100f, "the whole free enemy is against it");
        AssertNear(35000f / 27500f, body.IsolatedOdds, 0.01f, "35,000 against 27,500 is odds 1.27");
        AssertNear(1f, body.Detail, 0.01f, "so the detail term is the full penalty");
        AssertEqual("works", Result(ledger, BattlePlanId.GenericAggressive).Reason, "and the reason literal reads works");

        // Off works the same plan against the same enemy in the open has no detail term.
        var open = new FeasField();
        open.AddCorps(-300f, 0f, 12000f);
        open.AddCorps(0f, 0f, 11000f);
        open.AddCorps(300f, 0f, 12000f);
        open.AddEnemyLine(25000f);
        BodyEstimate off = BodiesArray(open.Evaluate(BattlePlanId.GenericAggressive), BattlePlanId.GenericAggressive)[0];
        AssertFalse(off.AssaultsWorks, "off works it assaults none");
        AssertNear(0f, off.Detail, 1e-6f, "and has no detail term");

        // With the flag off the works are priced as open ground.
        bool saved = WhiskeyRealism.Plugin.EnemyWorksDoctrine;
        try
        {
            WhiskeyRealism.Plugin.EnemyWorksDoctrine = false;
            BodyEstimate flagOff = BodiesArray(Scenario6().Evaluate(BattlePlanId.GenericAggressive), BattlePlanId.GenericAggressive)[0];
            AssertFalse(flagOff.AssaultsWorks, "with Enable Enemy Works Doctrine off nothing is a works assault");
            AssertNear(0f, flagOff.Detail, 1e-6f, "and a single-body plan has no detail term");
        }
        finally { WhiskeyRealism.Plugin.EnemyWorksDoctrine = saved; }
    }

    static void WTurnFixOpen()
    {
        FeasField f = Scenario6();
        MafSides(f, out BodyEstimate[] authored, out _);
        BodyEstimate wing = FirstWing(authored), fix = MainOf(authored);
        float bar = ManeuverFeasibility.Bar(f.Army);
        AssertTrue(fix.StrikeOnWorks && !fix.AssaultsWorks, "the fix faces works and holds: it is not a works assault");
        AssertNear(bar, fix.BarUsed, 1e-4f, "it keeps the commander's bar");
        AssertNear(25000f / 3f * 1.1f, fix.EnemyOpen, 5f, "N_open = 27,500 x 8,333 / 25,000: the open cluster alone");
        AssertNear(1470f, fix.EnemyAgainst, 250f, "so the enemy against it is the open cluster shifted by 0.16 (spec: about 1,470)");
        AssertTrue(fix.IsolatedOdds > 15f, "its odds are high (spec: about 23), got " + fix.IsolatedOdds);
        AssertNear(0f, fix.Detail, 0.02f, "and its detail is 0");

        // The wing carries the plan's risk: the whole free enemy can shift on it while the works fix pins a third of the open ground's.
        AssertNear(13300f, wing.EnemyAgainst, 700f, "the wing's E ~ 9,200 + 0.38 x 10,700");
        AssertNear(0.90f, wing.IsolatedOdds, 0.08f, "the wing's odds ~ 0.90");
        AssertNear(0.38f, wing.Detail, 0.06f, "and its detail ~ 0.38");
        AssertEqual(FeasibilityVerdict.Risky, Result(f.Evaluate(BattlePlanId.ManeuverAndFix), BattlePlanId.ManeuverAndFix).Verdict,
            "the plan is Risky (penalty at least 0.35)");

        // Control: every cluster in works leaves a fix with no enemy outside works against it at all.
        FeasField sealedField = Scenario6();
        sealedField.Enemies.RemoveAt(2);
        sealedField.AddEnemy(100f, 600f, 25000f / 3f, cover: 0.40f, coverObject: 7, standing: true);
        MafSides(sealedField, out BodyEstimate[] sealedAuthored, out _);
        BodyEstimate sealedFix = MainOf(sealedAuthored);
        AssertTrue(sealedFix.StrikeOnWorks && !sealedFix.AssaultsWorks, "control: the fix still faces works");
        AssertNear(0f, sealedFix.EnemyOpen, 1e-3f, "control: no enemy stands outside works");
        AssertNear(0f, sealedFix.EnemyAgainst, 1e-3f, "so none is against it");
    }
}
