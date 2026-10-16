using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static TestFixtures;
using static BattleMovementHonestyTests;
using static ManeuverFeasibilityFixtures;

// BC14 task 9: the spec's § 6 scenarios as emergent results (FEAS-SCN-*). The fixtures are the spec's: mid commander (agg01, caution01 and audacity01
// all 0.5), June, 10:00, cavalry fraction 0, jitter seed 7. Where the spec injects the NavMesh march legs "at the length stated", the fake
// IConfirmedLegs does the same, so the stated T values hold.
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] ScenarioTests() => new (string, Action)[]
    {
        ("bc14 FEAS-SCN-1 a superior force converges: dividing plays are feasible and one is selected", FeasScn1),
        ("bc14 FEAS-SCN-2 the same army attacks piecemeal without the synchronized release and does not with it", FeasScn2),
        ("bc14 FEAS-SCN-3 even odds with two corps selects a single-body play", FeasScn3),
        ("bc14 FEAS-SCN-4 a held crossing with an unwatched alternative: hold at even odds, pin and cross at two to one", FeasScn4),
        ("bc14 FEAS-SCN-HOLD at even odds with a held bridge the army holds its bank: the model decides, not the base scores", FeasScnHold),
        ("bc14 FEAS-SCN-AUDACITY a bold commander's dividing plays are not beaten in detail at even odds", FeasScnAudacity),
        ("bc14 FEAS-SCN-BOLD a bolder commander needs less of an edge to pin and cross", FeasScnBold),
    };

    // The mid commander's context for a selection over a ledger.
    static PlaybookContext MidContext(IFeasibilityLookup lookup, float odds, bool envelopmentPressure, BattleStance stance = BattleStance.Attacker)
        => new PlaybookContext(new PersonalityVector(0f, 0f, 0f, 0f, 0f), TerrainKind.Open, odds, 0f, 0, 7, envelopmentPressure, 0, 0f, 1f, 1f, false, false,
            stance, false, lookup);

    // Adds the confirmed legs the spec injects: a body member's leg to its rect anchor at `units` on grass (cost 2 per unit). Selects members by
    // rect kind: wings and flank marches take wingUnits, the Center takes fixUnits.
    static void InjectLegs(FeasField f, FakeLegs legs, BattlePlanId plan, Func<MemberEstimate, BodyEstimate, float?> unitsOf)
    {
        var probe = new FeasibilityLedger();
        ManeuverFeasibility.Evaluate(f.Build(plan), probe);
        CandidateResult r = Result(probe, plan);
        ReadOnlySpan<MemberEstimate> members = probe.Members(in r);
        ReadOnlySpan<BodyEstimate> bodies = probe.Bodies(in r);
        FeasibilityJob job = probe.Job;
        for (int i = 0; i < members.Length; i++)
        {
            float? units = unitsOf(members[i], bodies[members[i].Body]);
            if (units == null) continue;
            ChildFact child = job.Children[job.SlotOfChild(members[i].ChildId)];
            legs.Answers.Add((child.X, child.Z, members[i].TargetX, members[i].TargetZ, -1, units.Value * 2f, false));
        }
    }

    // The spec's scenario 1/2 injection: the wings' legs at `wing` units, the Center's at 200 (cost 2 per unit on grass).
    static FakeLegs ScenarioLegs(FeasField f, Func<MemberEstimate, BodyEstimate, float?> unitsOf, params BattlePlanId[] plans)
    {
        var legs = new FakeLegs();
        for (int i = 0; i < plans.Length; i++) InjectLegs(f, legs, plans[i], unitsOf);
        return legs;
    }

    // The NavMesh as the scenario tells it: every member's legs next to its route ends (child to target, and each to every link crossing) walk
    // plain grass at its length, so a river the leg skirts adds no bend of its own.
    static FakeLegs StraightLegs(FeasField f, params BattlePlanId[] plans)
    {
        var legs = new FakeLegs();
        RiverRegions regions = f.Regions;
        for (int p = 0; p < plans.Length; p++)
        {
            var probe = new FeasibilityLedger();
            ManeuverFeasibility.Evaluate(f.Build(plans[p]), probe);
            FeasibilityJob job = probe.Job;
            foreach (CandidateScratch side in new[] { job.SideA, job.SideB })
            {
                for (int m = 0; m < side.MemberCount; m++)
                {
                    MemberEstimate me = side.Members[m];
                    ChildFact child = job.Children[job.SlotOfChild(me.ChildId)];
                    legs.Answers.Add((child.X, child.Z, me.TargetX, me.TargetZ, -1, Dist(child.X, child.Z, me.TargetX, me.TargetZ) * 2f, false));
                    for (int l = 0; l < regions.LinkCount; l++)
                    {
                        regions.LinkCrossing(l, out float lx, out float lz, out int gridIndex, out _);
                        legs.Answers.Add((child.X, child.Z, lx, lz, gridIndex, Dist(child.X, child.Z, lx, lz) * 2f, false));
                        legs.Answers.Add((me.TargetX, me.TargetZ, lx, lz, gridIndex, Dist(me.TargetX, me.TargetZ, lx, lz) * 2f, false));
                    }
                }
            }
        }
        return legs;
    }

    static float Dist(float x0, float z0, float x1, float z1) => (float)Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));

    static void FeasScn1()
    {
        FeasField f = ThreeCorps();
        f.Legs = ScenarioLegs(f, (m, b) => b.Envelops ? 555f : (Math.Abs(m.TargetX - FeasField.X(0f)) < 1f ? (float?)200f : null),
            BattlePlanId.ManeuverAndFix, BattlePlanId.DoubleEnvelopment);
        FeasibilityLedger ledger = f.Evaluate(AllPlans);

        // ManeuverAndFix: the wing marches 555 units, the fix 200; the fix waits and the two strike 300 s apart.
        CandidateResult maf = Result(ledger, BattlePlanId.ManeuverAndFix);
        AssertEqual(FeasibilityVerdict.Feasible, maf.Verdict, "ManeuverAndFix is Feasible");
        BodyEstimate[] mb = ledger.Bodies(in maf).ToArray();
        AssertNear(1.50f, mb[0].StrikeHours, 0.05f, "T_wing = 0.32 + 1.11 + 0.07");
        AssertNear(0.79f, mb[1].StrikeHours, 0.03f, "T_main = 0.32 + 0.40 + 0.07");
        AssertNear(1.42f, mb[1].DesiredStrikeHours, 0.05f, "C* = max(0.79, 1.50 - 0.083)");
        AssertNear(1.50f, mb[0].DesiredStrikeHours, 0.05f, "S*_wing = C* + offset");
        AssertNear(0f, mb[0].ReleaseHours, 1e-4f, "the wing leaves at once");
        AssertNear(0.63f, mb[1].ReleaseHours, 0.05f, "the fix waits 0.63 h");
        AssertNear(15300f, mb[0].Pin, 100f, "the fix has struck by the wing's strike: 23,000 / 1.5");
        AssertNear(1.55f, mb[0].SupportHours, 0.05f, "sigma = 0.32 + 1.23");
        AssertNear(1.12f, mb[0].EnemyShiftHours, 0.03f, "tau = 0.22 + 0.90");
        AssertNear(10300f, mb[0].EnemyAgainst, 300f, "E = 9,200 + 0.38 x (27,500 - 15,300 - 9,200)");
        AssertNear(1.17f, mb[0].IsolatedOdds, 0.05f, "the wing's odds 12,000 / 10,300");
        AssertNear(0f, mb[0].Detail, 0.05f, "the wing's detail 0");
        AssertNear(0f, mb[1].Pin, 1e-3f, "no wing has struck by C*: the fix pins against nothing");
        AssertNear(27500f, mb[1].EnemyAgainst, 1f, "the enemy is at its strike point: E = N");
        AssertNear(1.25f, mb[1].IsolatedOdds, 0.05f, "the fix's odds 23,000 x 1.5 / 27,500");
        AssertNear(0f, mb[1].Detail, 0.05f, "the fix's detail 0");
        AssertNear(0f, mb[0].Exposure, 1e-6f, "the wing is out of the enemy's sight until its last 250 units");

        // DoubleEnvelopment: every T as above, so C* = S* = 1.50 for all bodies.
        CandidateResult de = Result(ledger, BattlePlanId.DoubleEnvelopment);
        AssertEqual(FeasibilityVerdict.Feasible, de.Verdict, "DoubleEnvelopment is Feasible");
        BodyEstimate[] db = ledger.Bodies(in de).ToArray();
        for (int i = 0; i < db.Length; i++) AssertNear(1.50f, db[i].DesiredStrikeHours, 0.05f, "S* of body " + i);
        AssertNear(15300f, db[0].Pin, 100f, "each wing: 11,000 / 1.5 + 12,000 / 1.5");
        AssertNear(1.17f, db[0].IsolatedOdds, 0.05f, "wing odds 1.17");
        AssertNear(16000f, db[2].Pin, 1f, "the fix: both wings pinned, 24,000 / 1.5");
        AssertNear(11500f, db[2].EnemyAgainst, 50f, "E = 11,500");
        AssertNear(1.43f, db[2].IsolatedOdds, 0.05f, "the fix's odds 16,500 / 11,500");
        for (int i = 0; i < db.Length; i++) AssertNear(0f, db[i].Detail, 0.05f, "detail of body " + i);

        // A dividing play is selected (today's EnvelopmentPressure bias, nothing in its way).
        TacticalPlaybook winner = SeedCatalog.AllHistoricalAndGeneric().Select(MidContext(ledger, 35000f / 25000f, true));
        AssertTrue(winner.Id == BattlePlanId.DoubleEnvelopment || winner.Id == BattlePlanId.ManeuverAndFix || winner.Id == BattlePlanId.MobileFlankMarch
            || winner.Id == BattlePlanId.WideFlankApproach, "a dividing play is selected, got " + winner.Id);
    }

    static void FeasScn2()
    {
        FeasField f = ThreeCorps();
        // The left wing's route crosses a creek and a swamp (march 2.01 h), the right wing's takes 0.81 h, the fix's 0.40 h.
        f.Legs = ScenarioLegs(f, (m, b) => b.Envelops ? (m.TargetX < FeasField.X(0f) ? 1005f : 405f) : 200f, BattlePlanId.DoubleEnvelopment);
        FeasibilityLedger ledger = f.Evaluate(BattlePlanId.DoubleEnvelopment);
        BodyEstimate[] sync = BodiesArray(ledger, BattlePlanId.DoubleEnvelopment);
        AssertEqual(3, sync.Length, "two wings and the main body");
        AssertNear(2.4f, sync[0].StrikeHours, 0.05f, "T_left = 2.4");
        AssertNear(1.2f, sync[1].StrikeHours, 0.05f, "T_right = 1.2");
        AssertNear(0.79f, sync[2].StrikeHours, 0.03f, "T_main = 0.79");
        for (int i = 0; i < sync.Length; i++)
        {
            AssertNear(2.4f, sync[i].DesiredStrikeHours, 0.05f, "with the synchronized release every body strikes at 2.4 (body " + i + ")");
            AssertNear(0f, sync[i].Detail, 0.05f, "and every detail is 0 (body " + i + ")");
        }
        AssertNear(1.2f, sync[1].ReleaseHours, 0.05f, "the right wing waits 1.2 h");
        AssertNear(1.61f, sync[2].ReleaseHours, 0.05f, "the fix waits 1.61 h");

        // Every body released at once (no BC14 release): the same pure function with each body's strike at its own T.
        FeasibilityJob job = ledger.Job;
        var bodies = new BodyEstimate[job.SideA.BodyCount];
        Array.Copy(job.SideA.Bodies, bodies, bodies.Length);
        var own = new float[bodies.Length];
        for (int i = 0; i < bodies.Length; i++) own[i] = bodies[i].StrikeHours;
        ManeuverFeasibility.DetailRisk(bodies, own, job, job.SideA, ManeuverFeasibility.Bar(f.Army));
        AssertNear(2.75f, bodies[1].ExposureWindowHours, 0.1f, "the right wing: W = (2.4 - 1.2) + 1.55");
        AssertNear(7300f, bodies[1].Pin, 100f, "only the fix has struck: 11,000 / 1.5");
        AssertNear(20200f, bodies[1].EnemyAgainst, 300f, "E = 27,500 - 7,300");
        AssertNear(0.59f, bodies[1].IsolatedOdds, 0.03f, "odds 0.59");
        AssertNear(1f, bodies[1].Detail, 1e-4f, "the right wing is beaten in detail");
        AssertNear(0.60f, bodies[2].IsolatedOdds, 0.03f, "the fix alone: 16,500 / 27,500");
        AssertNear(1f, bodies[2].Detail, 1e-4f, "so is the fix");
    }

    static readonly BattlePlanId[] SingleBody =
    {
        BattlePlanId.MeetingEngagement, BattlePlanId.GenericMethodical, BattlePlanId.HesitatedCommit, BattlePlanId.ContinuousAttrition,
        BattlePlanId.FrontalShockAssault, BattlePlanId.ForcedFrontalAttack, BattlePlanId.GenericAggressive, BattlePlanId.GenericDesperate,
    };

    static readonly BattlePlanId[] Dividing =
    {
        BattlePlanId.DoubleEnvelopment, BattlePlanId.MobileFlankMarch, BattlePlanId.WideFlankApproach, BattlePlanId.ManeuverAndFix,
    };

    static bool Contains(BattlePlanId[] set, BattlePlanId plan)
    {
        for (int i = 0; i < set.Length; i++) if (set[i] == plan) return true;
        return false;
    }

    static FeasField TwoCorps()
    {
        var f = new FeasField();
        f.AddCorps(-300f, 0f, 12500f);
        f.AddCorps(300f, 0f, 12500f);
        f.AddEnemyLine(25000f);
        return f;
    }

    static void FeasScn3()
    {
        FeasField f = TwoCorps();
        FeasibilityLedger ledger = f.Evaluate(AllPlans);
        // DoubleEnvelopment: each wing's only support is the other wing from the far line end.
        BodyEstimate[] de = BodiesArray(ledger, BattlePlanId.DoubleEnvelopment);
        AssertEqual(2, de.Length, "both corps are wings; no child is left for the Center");
        for (int i = 0; i < de.Length; i++)
        {
            AssertNear(2.12f, de[i].SupportHours, 0.05f, "sigma = 0.32 + 1.80");
            AssertTrue(de[i].SupportHours > de[i].EnemyShiftHours, "the wings stand on exterior lines: sigma > tau");
            AssertNear(0.69f, de[i].IsolatedOdds, 0.03f, "odds 0.69");
            AssertNear(1f, de[i].Detail, 1e-4f, "detail 1");
        }
        // The plays that fix with the Center alone: its odds 12,500 x 1.5 / 27,500.
        foreach (BattlePlanId plan in new[] { BattlePlanId.ManeuverAndFix, BattlePlanId.MobileFlankMarch, BattlePlanId.WideFlankApproach })
        {
            BodyEstimate[] bodies = BodiesArray(ledger, plan);
            BodyEstimate main = bodies[bodies.Length - 1];
            AssertTrue(main.Main, plan + ": the main body is last");
            AssertNear(0.68f, main.IsolatedOdds, 0.03f, plan + ": the Center alone fixes");
            AssertNear(1f, main.Detail, 1e-4f, plan + ": detail 1");
        }
        foreach (BattlePlanId plan in Dividing) AssertTrue(Result(ledger, plan).Penalty >= 0.3f, plan + " is penalized at least 0.3, got " + Result(ledger, plan).Penalty);
        // The single-body plays carry no detail term.
        foreach (BattlePlanId plan in SingleBody)
        {
            ReadOnlySpan<BodyEstimate> bodies = ledger.Bodies(Result(ledger, plan));
            for (int i = 0; i < bodies.Length; i++) AssertNear(0f, bodies[i].Detail, 1e-6f, plan + " has no detail term");
        }
        var catalog = SeedCatalog.AllHistoricalAndGeneric();
        TacticalPlaybook shipped = catalog.Select(MidContext(null, 1f, false));
        AssertEqual(BattlePlanId.ManeuverAndFix, shipped.Id, "today a dividing play has the best base score at odds 1.0");
        TacticalPlaybook winner = catalog.Select(MidContext(ledger, 1f, false));
        AssertTrue(Contains(SingleBody, winner.Id), "a single-body play is selected for the mid commander, got " + winner.Id);
    }

    // A horizontal river band at depth [from, to) with crossings (lateral, depth, id).
    static BattleTerrainGrid BandWithCrossings(float depthFrom, float depthTo, params (float lateral, float depth, int id)[] crossings)
    {
        int from = (int)(depthFrom + FeasField.Size * 0.5f), to = (int)(depthTo + FeasField.Size * 0.5f);
        var cx = new float[crossings.Length];
        var cz = new float[crossings.Length];
        var cid = new int[crossings.Length];
        for (int i = 0; i < crossings.Length; i++) { cx[i] = FeasField.X(crossings[i].lateral); cz[i] = FeasField.Z(crossings[i].depth); cid[i] = crossings[i].id; }
        return GridOf(FeasField.Size, FeasField.Size, (x, zi) => zi >= from && zi < to ? River : Grass, false, cx, cz, cid);
    }

    // Scenario 4 (Perryville-like): a river across the front on depths 240-260, bridge B at (0, 250) held by a 1,500-man brigade at (0, 300), ford F
    // at (-900, 250) Clear; 35,000 against 35,000 at scale 1.
    static FeasField Scenario4(float ownScale = 1f)
    {
        var f = new FeasField(BandWithCrossings(240f, 260f, (0f, 250f, Bridge), (-900f, 250f, Ford)));
        f.AddCorps(-300f, 0f, 12000f * ownScale);
        f.AddCorps(0f, 0f, 11000f * ownScale);
        f.AddCorps(300f, 0f, 12000f * ownScale);
        f.AddEnemy(-400f, 556f, 11000f);
        f.AddEnemy(0f, 556f, 11500f);
        f.AddEnemy(400f, 556f, 11000f);
        f.AddEnemy(0f, 300f, 1500f, heading: 180f, fireRange: 100f);       // the brigade holding B
        f.EnemyHalfExtent = 450f;
        return f;
    }

    // The scenario 4 evaluation with the NavMesh legs injected, every candidate.
    static FeasibilityLedger Evaluate4(FeasField f)
    {
        f.Legs = StraightLegs(f, AllPlans);
        return f.Evaluate(AllPlans);
    }

    static float WorstDetail(FeasibilityLedger ledger, BattlePlanId plan)
    {
        float worst = 0f;
        ReadOnlySpan<BodyEstimate> bodies = ledger.Bodies(Result(ledger, plan));
        for (int i = 0; i < bodies.Length; i++) if (bodies[i].Detail > worst) worst = bodies[i].Detail;
        return worst;
    }

    static readonly BattlePlanId[] Marchers = { BattlePlanId.FrontalShockAssault, BattlePlanId.MeetingEngagement, BattlePlanId.ForcedFrontalAttack, BattlePlanId.GenericDesperate };
    static readonly BattlePlanId[] BankHolders = { BattlePlanId.GenericAggressive, BattlePlanId.ContinuousAttrition, BattlePlanId.HesitatedCommit, BattlePlanId.GenericMethodical };

    static void FeasScn4()
    {
        // Even odds: 35,000 against 35,000.
        FeasField f = Scenario4();
        FeasibilityLedger ledger = Evaluate4(f);
        AssertNear(38500f, ledger.Job.Enemy.N, 1f, "N = 35,000 x 1.1");
        AssertNear(545f, ledger.Job.EnemyZ - ledger.Job.ArmyZ, 1f, "the enemy anchor stands 545 units ahead");
        AssertNear(900f, ledger.Job.Frontage, 1f, "the half frontage is 450");
        foreach (BattlePlanId plan in Dividing) AssertNear(1f, WorstDetail(ledger, plan), 1e-4f, plan + ": every dividing play pays detail 1");
        foreach (BattlePlanId plan in Marchers) AssertNear(0.645f, Result(ledger, plan).Penalty, 0.05f, plan + ": the Center march through B");
        foreach (BattlePlanId plan in BankHolders) AssertNear(0.379f, Result(ledger, plan).Penalty, 0.05f, plan + ": strike legs through B at half weight");
        // The mirrored sides of the two single-flank plays (the ones through F).
        foreach (var (plan, odds) in new[] { (BattlePlanId.ManeuverAndFix, 0.45f), (BattlePlanId.WideFlankApproach, 0.37f) })
        {
            FeasField g = Scenario4();
            FeasibilityLedger one = Evaluate4Plan(g, plan);
            AssertNear(odds, one.Job.SideB.Bodies[0].IsolatedOdds, 0.03f, plan + ": the mirrored wing's odds");
            AssertNear(1f, one.Job.SideB.Bodies[0].Detail, 1e-4f, plan + ": and its detail");
        }
        TacticalPlaybookCatalog catalog = SeedCatalog.AllHistoricalAndGeneric();
        AssertEqual(BattlePlanId.ManeuverAndFix, catalog.Select(MidContext(null, 1f, false)).Id, "today ManeuverAndFix is selected");
        TacticalPlaybook held = catalog.Select(MidContext(ledger, 1f, false));
        AssertTrue(Contains(BankHolders, held.Id), "a bank-holding single-body play is selected, got " + held.Id);

        // Two to one: pin and cross.
        FeasField g2 = Scenario4(2f);
        FeasibilityLedger l2 = Evaluate4(g2);
        CandidateResult maf = Result(l2, BattlePlanId.ManeuverAndFix);
        AssertTrue(maf.Mirrored, "ManeuverAndFix is scored on its mirrored side");
        AssertNear(0.379f, maf.Penalty, 0.05f, "its penalty is the bank holders' 0.379");
        MemberEstimate[] members = l2.Members(in maf).ToArray();
        MemberEstimate wing = members[0];
        foreach (MemberEstimate m in members) if (m.Kind == ManeuverFeasibility.MemberEnvelops) wing = m;
        AssertEqual(1, wing.MarchVia, "the wing crosses at F (link 1)");
        AssertEqual(-1, wing.AttackVia, "and its inward leg stays on the far bank");
        BodyEstimate[] bodies = l2.Bodies(in maf).ToArray();
        AssertNear(1.68f, bodies[0].IsolatedOdds, 0.05f, "the wing's odds 1.68");
        AssertNear(0f, bodies[0].Detail, 1e-4f, "detail 0");
        AssertNear(0f, bodies[0].Contested, 1e-4f, "F is Clear: contested 0");
        AssertNear(0.01f, bodies[0].Penalty, 0.02f, "the wing's penalty about 0.01");
        AssertNear(1.79f, bodies[1].IsolatedOdds, 0.05f, "the fix's own odds 46,000 x 1.5 / 38,500");
        foreach (BattlePlanId plan in AllPlans)
        {
            if (plan == BattlePlanId.CavalryRaid || plan == BattlePlanId.CavalryScreeningDelay) continue;     // not offered to a force with no cavalry
            AssertTrue(Result(l2, plan).Penalty >= maf.Penalty - 1e-4f, plan + " is not lower than the pin-and-cross candidate");
        }
        AssertTrue(Result(l2, BattlePlanId.DoubleEnvelopment).Penalty >= 0.6f, "DoubleEnvelopment goes through B: >= 0.6");
        FeasField g3 = Scenario4(2f);
        FeasibilityLedger authored = Evaluate4Plan(g3, BattlePlanId.ManeuverAndFix);
        AssertTrue(authored.Job.SideA.Result.Penalty >= 0.6f, "and so does the authored ManeuverAndFix: " + authored.Job.SideA.Result.Penalty);
        TacticalPlaybook crossing = catalog.Select(MidContext(l2, 2f, true));
        AssertEqual(BattlePlanId.ManeuverAndFix, crossing.Id, "at 2:1 the base lead under EnvelopmentPressure selects it");
        var army = new ArmyOrchestrator(0, catalog, new PersonalityVector(0f, 0f, 0f, 0f, 0f));
        army.SetFeasibility(l2);
        army.PickInitialPlan(new ArmyEvidence(2f, TerrainKind.Open, 0));
        AssertTrue(army.CurrentPlan.Mirrored || army.CurrentPlan.PlanId != BattlePlanId.ManeuverAndFix, "a ManeuverAndFix pick is published mirrored");
    }

    static FeasibilityLedger Evaluate4Plan(FeasField f, BattlePlanId plan)
    {
        f.Legs = StraightLegs(f, plan);
        return f.Evaluate(plan);
    }

    static void FeasScnHold()
    {
        FeasField f = Scenario4();
        FeasibilityLedger ledger = Evaluate4(f);
        TacticalPlaybookCatalog catalog = SeedCatalog.AllHistoricalAndGeneric();
        // (1) With the flag off the shipped scores pick the dividing play.
        var off = new SelectionReport();
        TacticalPlaybook shipped = catalog.Select(MidContext(null, 1f, false), off);
        AssertEqual(BattlePlanId.ManeuverAndFix, shipped.Id, "flag off: the shipped scores pick ManeuverAndFix");
        AssertNear(0.618f, off[0].BaseScore, 0.002f, "at 0.618");
        // (2) With it on a bank-holding single-body play is selected.
        var on = new SelectionReport();
        TacticalPlaybook held = catalog.Select(MidContext(ledger, 1f, false), on);
        AssertTrue(Contains(BankHolders, held.Id), "flag on: a bank-holding single-body play, got " + held.Id);
        AssertEqual(BattlePlanId.GenericAggressive, held.Id, "GenericAggressive");
        AssertNear(0.44f, on[0].Score, 0.01f, "at about 0.44");
        // (3) The hold does not rest on the base scores: every bridge-crossing candidate's term is at least 0.1 below every bank-holder's,
        // and every dividing play's term is -0.4.
        float worstHolder = float.PositiveInfinity, bestMarcher = float.NegativeInfinity;
        for (int i = 0; i < on.Count; i++)
        {
            BattlePlanId plan = on[i].Plan;
            if (Contains(BankHolders, plan)) worstHolder = Math.Min(worstHolder, on[i].Term);
            if (Contains(Marchers, plan)) bestMarcher = Math.Max(bestMarcher, on[i].Term);
            if (Contains(Dividing, plan)) AssertNear(-0.4f, on[i].Term, 1e-5f, plan + ": a dividing play's term is -0.4");
        }
        AssertTrue(worstHolder - bestMarcher >= 0.1f, "the marchers trail the bank holders by at least 0.1 in the term: " + (worstHolder - bestMarcher));
    }

    static void FeasScnAudacity()
    {
        var mid = TwoCorps();
        FeasibilityLedger lm = mid.Evaluate(AllPlans);
        var bold = TwoCorps();
        bold.Army = FeasField.Personality(0.5f, 0.5f, 0.9f);
        FeasibilityLedger lb = bold.Evaluate(AllPlans);
        foreach (BattlePlanId plan in Dividing)
        {
            AssertNear(1f, WorstDetail(lm, plan), 1e-4f, plan + ": the mid commander's detail is 1");
            float d = WorstDetail(lb, plan);
            AssertTrue(d < 1f && d > 0.7f, plan + ": audacity01 0.9 leaves it below 1, got " + d);
            AssertTrue(Result(lb, plan).Verdict != FeasibilityVerdict.Impossible, plan + " is not filtered");
        }
        BodyEstimate[] de = BodiesArray(lb, BattlePlanId.DoubleEnvelopment);
        AssertNear(0.87f, de[0].Detail, 0.05f, "DoubleEnvelopment's wing about 0.87");
        BodyEstimate[] maf = BodiesArray(lb, BattlePlanId.ManeuverAndFix);
        AssertNear(0.90f, maf[maf.Length - 1].Detail, 0.05f, "the single-fix plays' Center about 0.90");
    }

    // The own-to-enemy strength ratio at which the mirrored ManeuverAndFix wing's detail reaches 0, found by bisection to 0.01.
    static float PinAndCrossRatio(FeasField donor, OpportunityPersonality army)
    {
        float lo = 1f, hi = 3f;
        while (hi - lo > 0.01f)
        {
            float mid = (lo + hi) * 0.5f;
            var f = new FeasField(donor);
            f.Army = army;
            f.AddCorps(-300f, 0f, 12000f * mid);
            f.AddCorps(0f, 0f, 11000f * mid);
            f.AddCorps(300f, 0f, 12000f * mid);
            f.AddEnemy(-400f, 556f, 11000f);
            f.AddEnemy(0f, 556f, 11500f);
            f.AddEnemy(400f, 556f, 11000f);
            f.AddEnemy(0f, 300f, 1500f, heading: 180f, fireRange: 100f);
            f.EnemyHalfExtent = 450f;
            FeasibilityLedger one = Evaluate4Plan(f, BattlePlanId.ManeuverAndFix);
            if (one.Job.SideB.Bodies[0].Detail <= 1e-6f) hi = mid; else lo = mid;
        }
        return hi;
    }

    static void FeasScnBold()
    {
        var donor = Scenario4();
        float midRatio = PinAndCrossRatio(donor, FeasField.Personality(0.5f, 0.5f, 0.5f));
        float boldRatio = PinAndCrossRatio(donor, FeasField.Personality(0.5f, 0.5f, 0.9f));
        AssertNear(1.60f, midRatio, 0.05f, "the mid commander needs 1.6 to one (found " + midRatio + ")");
        AssertNear(1.50f, boldRatio, 0.05f, "audacity01 0.9 needs 1.5 (found " + boldRatio + ")");
        AssertTrue(boldRatio < midRatio, "the bolder commander needs less of an edge");
        AssertTrue(midRatio <= 2.0f, "and the mid one at most two to one (D14)");
    }
}
