using System;
using System.IO;
using System.Text.RegularExpressions;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static BattleMovementHonestyTests;
using static ManeuverFeasibilityFixtures;

// BC14 task 8: the perceived enemy, its shift times, the detail risk and the penalty (spec § 13.1 S3). The fields are the § 6 frame; the
// numbers asserted without a stated source are the spec's own scenario values.
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] RiskTests() => new (string, Action)[]
    {
        ("bc14 ENEMY-FOG-PREMIUM confidence 0.8 and caution 0.5 scale the perceived enemy by 1.1, caution 1.0 by 1.15", EnemyFogPremium),
        ("bc14 ENEMY-NO-SAMPLES with nothing perceived the mass is the assumed point and the local enemy N/(bodies + 1)", EnemyNoSamples),
        ("bc14 SHIFT-PASSIVE a perceived passive enemy shifts 1.5 times slower", ShiftPassive),
        ("bc14 SHIFT-RIVER the enemy crosses its own held crossing at no cost", ShiftRiver),
        ("bc14 SHIFT-COVERED a crossing an own regiment covers costs the enemy an hour; a Watched or Clear one nothing", ShiftCovered),
        ("bc14 SHIFT-COURIER the enemy's courier is charged with the own side in contact as without", ShiftCourier),
        ("bc14 DETAIL-PIN a body that has struck pins its strength over the defence ratio of its strike point", DetailPin),
        ("bc14 DETAIL-WINDOW the shift share is 0 when tau reaches the window, 1 at twice, linear between", DetailWindow),
        ("bc14 DETAIL-NO-FIX a template whose fixing rect is unfilled pins nothing but the other wing", DetailNoFix),
        ("bc14 SUPPORT-TIME sigma is the least courier plus march of the other bodies, infinite with none", SupportTime),
        ("bc14 DETAIL-BAR the bar is 1 for a mid commander, caution raises it and audacity and aggression lower it", DetailBar),
        ("bc14 DETAIL-PENALTY odds 0.91 at bar 1.0 give 0.36 and odds 0.75 give 1", DetailPenaltyCases),
        ("bc14 EXPOSURE-FOREST route samples within a perceived regiment's reach count, forest at half, the last 250 units not", ExposureForest),
        ("bc14 ODDS-MORALE the enemy's perceived morale divides the odds and the main body carries its own ratio", OddsMorale),
        ("bc14 APPROACH-ANCHORED an enemy line end that rests on a river adds 0.5", ApproachAnchored),
        ("bc14 BC13-HALF a main-body child with a live exploit counts half in S and in the pin", Bc13Half),
        ("bc14 DAYLIGHT a strike past the end of day less a quarter hour is after-dark, Risky and never Impossible", DaylightCase),
        ("bc14 STRIKE-LEG a main-body strike leg counts at half weight; no path reads no-strike-crossing and is not impossible", StrikeLeg),
        ("bc14 FEAS-FOG a hidden enemy changes no crossing state, no N and no shift time", FeasFog),
        ("bc14 FEAS-PIN the BC14 file set reads no enemy record, scheduledarrival or durationleft", FeasPin),
    };

    static BodyEstimate[] BodiesArray(FeasibilityLedger ledger, BattlePlanId plan)
    {
        CandidateResult r = Result(ledger, plan);
        return ledger.Bodies(in r).ToArray();
    }

    // DoubleEnvelopment's wings in rect order: the left, then the right.
    static BodyEstimate RightWing(BodyEstimate[] bodies)
    {
        BodyEstimate best = bodies[0];
        for (int i = 1; i < bodies.Length; i++) if (bodies[i].Envelops && bodies[i].TargetX > best.TargetX) best = bodies[i];
        return best;
    }

    static float ExpectedShift(MarchRules rules, float ridePerUnitCost, float lateralUnits)
        => rules.ProcessingHours + rules.RouteProcessingHours + lateralUnits * ridePerUnitCost / rules.CourierSpeedPerHour
           + lateralUnits * ridePerUnitCost / rules.BasicUnitSpeed;

    static void EnemyFogPremium()
    {
        AssertNear(0.1f, ManeuverFeasibility.FogPremium(0.8f, 0.5f), 1e-6f, "confidence 0.8, caution 0.5: 0.5 x 0.2 x 1.0");
        AssertNear(0.15f, ManeuverFeasibility.FogPremium(0.8f, 1f), 1e-6f, "caution 1.0: 0.5 x 0.2 x 1.5");
        AssertNear(0f, ManeuverFeasibility.FogPremium(1f, 1f), 1e-6f, "full confidence: no premium");
        AssertNear(27500f, ThreeCorps().Evaluate(BattlePlanId.DoubleEnvelopment).Job.Enemy.N, 1f, "N = 25,000 x 1.1");
        FeasField bold = ThreeCorps();
        bold.Army = FeasField.Personality(0.5f, 1f, 0.5f);
        AssertNear(28750f, bold.Evaluate(BattlePlanId.DoubleEnvelopment).Job.Enemy.N, 1f, "a cautious commander assumes more: 25,000 x 1.15");
        FeasField estimate = ThreeCorps();
        estimate.EnemyEstimate = 40000f;
        AssertNear(44000f, estimate.Evaluate(BattlePlanId.DoubleEnvelopment).Job.Enemy.N, 1f, "the ledger's estimate beats a smaller perceived strength");
    }

    static void EnemyNoSamples()
    {
        var f = new FeasField();
        f.AddCorps(-300f, 0f, 12000f);
        f.AddCorps(0f, 0f, 11000f);
        f.AddCorps(300f, 0f, 12000f);
        f.EnemyEstimate = 20000f;
        f.HasAssumedEnemy = true;
        f.AssumedEnemyLateral = 100f;
        f.AssumedEnemyDepth = 700f;
        FeasibilityLedger ledger = f.Evaluate(BattlePlanId.DoubleEnvelopment);
        EnemyPicture enemy = ledger.Job.Enemy;
        AssertFalse(enemy.HasSamples, "nothing perceived");
        AssertNear(FeasField.X(100f), enemy.MassX, 0.01f, "the mass is BC10's assumed enemy point");
        AssertNear(FeasField.Z(700f), enemy.MassZ, 0.01f, "in z too");
        AssertNear(22000f, enemy.N, 1f, "N = the estimate x 1.1");
        BodyEstimate[] bodies = BodiesArray(ledger, BattlePlanId.DoubleEnvelopment);
        AssertEqual(3, bodies.Length, "two wings and the main body");
        for (int i = 0; i < bodies.Length; i++) AssertNear(22000f / 4f, bodies[i].LocalEnemy, 1f, "L = N / (bodies + 1) for body " + i);
    }

    // DoubleEnvelopment's right wing with the given intent, on the scenario 1 field.
    static float RightShift(Action<FeasField> setup = null)
    {
        FeasField f = ThreeCorps();
        setup?.Invoke(f);
        return RightWing(BodiesArray(f.Evaluate(BattlePlanId.DoubleEnvelopment), BattlePlanId.DoubleEnvelopment)).EnemyShiftHours;
    }

    static void ShiftPassive()
    {
        MarchRules rules = ShippedRules();
        // 450 units from the enemy mass to the right line end, grass (cost 2): the ride at the courier's speed and the march at infantry speed.
        float expected = ExpectedShift(rules, 2f, 450f);
        float baseShift = RightShift();
        AssertNear(expected, baseShift, 0.01f, "tau = 0.04 + 900/5126 + 900/1000 (spec: 0.22 + 0.90)");
        float passive = RightShift(f => f.EnemyIntent = new TacticalIntentModel(InferredIntent.Defend, 0, 0.5f, 0f, null));
        AssertNear(baseShift * ManeuverFeasibility.PassiveShiftFactor, passive, 0.01f, "intent Defend at confidence 0.5: tau x 1.5");
        float unsure = RightShift(f => f.EnemyIntent = new TacticalIntentModel(InferredIntent.Defend, 0, 0.2f, 0f, null));
        AssertNear(baseShift, unsure, 0.001f, "below the confidence floor the enemy is not read as passive");
        float attack = RightShift(f => f.EnemyIntent = new TacticalIntentModel(InferredIntent.Attack, 0, 0.9f, 0f, null));
        AssertNear(baseShift, attack, 0.001f, "an attacking enemy shifts at its own pace");
    }

    // A vertical river band at lateral [from, to) with the given fords (lateral, depth).
    static BattleTerrainGrid VerticalBandWithFords(float lateralFrom, float lateralTo, params (float lateral, float depth)[] fords)
    {
        int from = (int)FeasField.X(lateralFrom), to = (int)FeasField.X(lateralTo);
        var cx = new float[fords.Length];
        var cz = new float[fords.Length];
        var cid = new int[fords.Length];
        for (int i = 0; i < fords.Length; i++) { cx[i] = FeasField.X(fords[i].lateral); cz[i] = FeasField.Z(fords[i].depth); cid[i] = Ford; }
        return GridOf(FeasField.Size, FeasField.Size, (x, zi) => x >= from && x < to ? River : Grass, false, cx, cz, cid);
    }

    // The right wing's shift across a river band at lateral 200-220 whose ford at (210, 600) lies between the enemy mass and the right line end.
    static float ShiftAcrossFord(bool enemyHolds, bool ownCovers, bool enemyWatches = false)
    {
        FeasField f = ThreeCorps(VerticalBandWithFords(200f, 220f, (210f, 600f)));
        // A picket under the army, facing the ford (covering it) or away from it.
        f.AddPicket(210f, 520f, ownCovers ? 0f : 180f);
        if (enemyHolds) f.AddEnemy(210f, 640f, 400f, heading: 180f, fireRange: 100f);
        if (enemyWatches) f.AddEnemy(210f, 640f, 400f, heading: 0f, fireRange: 100f);
        FeasibilityLedger ledger = f.Evaluate(BattlePlanId.DoubleEnvelopment);
        CandidateResult r = Result(ledger, BattlePlanId.DoubleEnvelopment);
        AssertTrue(r.Verdict != FeasibilityVerdict.Impossible, "the fixture is routable: " + r.Verdict + " " + r.Reason);
        return RightWing(ledger.Bodies(in r).ToArray()).EnemyShiftHours;
    }

    static void ShiftRiver()
    {
        float clear = ShiftAcrossFord(false, false);
        AssertTrue(clear > 1.0f && clear < 1.4f, "control: the shift goes over the ford and takes about 1.1 h, got " + clear);
        float held = ShiftAcrossFord(true, false);
        AssertNear(clear, held, 0.02f, "the enemy's own held crossing costs it nothing");
        float watched = ShiftAcrossFord(false, false, enemyWatches: true);
        AssertNear(clear, watched, 0.02f, "a crossing watched by its own men costs it nothing either");
    }

    static void ShiftCovered()
    {
        float uncovered = ShiftAcrossFord(false, false);
        float covered = ShiftAcrossFord(false, true);
        AssertNear(uncovered + ManeuverFeasibility.HeldCrossingHours, covered, 0.02f, "an own regiment covering the ford costs the enemy an hour to carry it");
        // The marching enemy pays the same hour in the attacker's run whichever of the other crossings states the own side reads.
        float coveredAndHeld = ShiftAcrossFord(true, true);
        AssertNear(uncovered + ManeuverFeasibility.HeldCrossingHours, coveredAndHeld, 0.02f, "the enemy holding it too does not remove the own cover");
    }

    static void ShiftCourier()
    {
        float free = RightShift();
        float engaged = RightShift(f => f.Contact.Add(2));         // tree index 2 is the first corps' brigade: the left side is engaged
        AssertNear(free, engaged, 1e-4f, "the enemy's courier is charged with the own side in contact as without");
        MarchRules none = ShippedRules(orderDelays: false);
        float noDelays = RightShift(f => f.Rules = none);
        AssertNear(0.9f, noDelays, 0.01f, "with order delays off the shift is the march alone");
    }

    static void DetailPin()
    {
        // Open ground: the main body pins its fixing members' 23,000 over 1.5 for a wing that strikes after it.
        FeasibilityLedger flat = ThreeCorps().Evaluate(BattlePlanId.ManeuverAndFix);
        BodyEstimate[] bodies = BodiesArray(flat, BattlePlanId.ManeuverAndFix);
        BodyEstimate wing = bodies[0], main = bodies[1];
        AssertNear(23000f, main.PinStrength, 0.5f, "the main body pins its Center and Demonstration");
        AssertNear(1.5f, main.DefenseRatio, 1e-4f, "open ground: 1.5 + 0");
        AssertNear(23000f / 1.5f, wing.Pin, 1f, "the fix has struck by the wing's strike: S/(1.5 + TerrainStrength)");
        AssertNear(0f, main.Pin, 1e-4f, "no wing has struck by C*: the fix pins against nothing");

        // Water across the front of the enemy anchor: TerrainStrength 0.4, defence ratio 1.9 (a ford far on the flank keeps the plan possible).
        FeasField river = ThreeCorps(HorizontalBand(350f, 370f, (-900f, 360f)));
        FeasibilityLedger lr = river.Evaluate(BattlePlanId.ManeuverAndFix);
        CandidateResult cr = Result(lr, BattlePlanId.ManeuverAndFix);
        AssertTrue(cr.Verdict != FeasibilityVerdict.Impossible, "the river fixture is routable: " + cr.Reason);
        BodyEstimate[] rb = lr.Bodies(in cr).ToArray();
        AssertNear(1.9f, rb[1].DefenseRatio, 0.01f, "water across 100-300 units in front: TerrainStrength 0.4");
        AssertTrue(rb[0].DesiredStrikeHours >= rb[1].DesiredStrikeHours, "the fix strikes first");
        AssertNear(rb[1].PinStrength / rb[1].DefenseRatio, rb[0].Pin, 1f, "and pins at the strike point's defence ratio");
    }

    static void DetailWindow()
    {
        AssertNear(0f, ManeuverFeasibility.ShiftShare(1f, 1f), 1e-6f, "tau reaches the window: nothing arrives in time");
        AssertNear(0f, ManeuverFeasibility.ShiftShare(0.5f, 1f), 1e-6f, "tau beyond the window: 0");
        AssertNear(1f, ManeuverFeasibility.ShiftShare(2f, 1f), 1e-6f, "the window twice tau: all");
        AssertNear(1f, ManeuverFeasibility.ShiftShare(5f, 1f), 1e-6f, "and no more");
        AssertNear(0.5f, ManeuverFeasibility.ShiftShare(1.5f, 1f), 1e-6f, "linear between");
        AssertNear(0.1f, ManeuverFeasibility.ShiftShare(0.05f, 0.04f), 1e-5f, "a tiny tau is floored at 0.1 h");
        AssertNear(1f, ManeuverFeasibility.ShiftShare(float.PositiveInfinity, 1f), 1e-6f, "an unsupported body is fully exposed");

        // Through the evaluation: E = L + share x (N - Pin - L) for every body.
        FeasibilityLedger ledger = ThreeCorps().Evaluate(BattlePlanId.ManeuverAndFix);
        float n = ledger.Job.Enemy.N;
        BodyEstimate[] bodies = BodiesArray(ledger, BattlePlanId.ManeuverAndFix);
        for (int i = 0; i < bodies.Length; i++)
        {
            float share = ManeuverFeasibility.ShiftShare(bodies[i].ExposureWindowHours, bodies[i].EnemyShiftHours);
            AssertNear(bodies[i].LocalEnemy + share * Math.Max(0f, n - bodies[i].Pin - bodies[i].LocalEnemy), bodies[i].EnemyAgainst, 1f, "E of body " + i);
        }
    }

    static void DetailNoFix()
    {
        // Two corps: both wings are filled and no child is left for the Center.
        var f = new FeasField();
        f.AddCorps(-300f, 0f, 12500f);
        f.AddCorps(300f, 0f, 12500f);
        f.AddEnemyLine(25000f);
        FeasibilityLedger ledger = f.Evaluate(BattlePlanId.DoubleEnvelopment);
        BodyEstimate[] bodies = BodiesArray(ledger, BattlePlanId.DoubleEnvelopment);
        AssertEqual(2, bodies.Length, "two wings, no main body");
        for (int i = 0; i < bodies.Length; i++)
        {
            AssertTrue(!bodies[i].Main, "no fixing force");
            AssertNear(12500f / 1.5f, bodies[i].Pin, 1f, "each wing pins only the other: 12,500 / 1.5");
        }
    }

    static void SupportTime()
    {
        // Scenario 3: each wing's only support is the other wing from the far line end, 900 units at cost 2.
        var f = new FeasField();
        f.AddCorps(-300f, 0f, 12500f);
        f.AddCorps(300f, 0f, 12500f);
        f.AddEnemyLine(25000f);
        FeasibilityLedger ledger = f.Evaluate(BattlePlanId.DoubleEnvelopment);
        BodyEstimate[] bodies = BodiesArray(ledger, BattlePlanId.DoubleEnvelopment);
        MarchRules rules = ShippedRules();
        ReadOnlySpan<MemberEstimate> members = ledger.Members(Result(ledger, BattlePlanId.DoubleEnvelopment));
        float expected = members[1].CourierHours + 900f * 2f / rules.BasicUnitSpeed;
        AssertNear(expected, bodies[0].SupportHours, 0.01f, "sigma = the other wing's courier + 900 units of grass (spec: 0.32 + 1.80)");
        AssertTrue(bodies[0].SupportHours > 2.0f && bodies[0].SupportHours < 2.3f, "about 2.1 h, got " + bodies[0].SupportHours);

        // An engaged supporting body keeps its courier hours in sigma.
        var g = new FeasField();
        int left = g.AddCorps(-300f, 0f, 12500f);
        g.AddCorps(300f, 0f, 12500f);
        g.AddEnemyLine(25000f);
        g.Contact.Add(left);
        BodyEstimate[] engaged = BodiesArray(g.Evaluate(BattlePlanId.DoubleEnvelopment), BattlePlanId.DoubleEnvelopment);
        AssertNear(bodies[1].SupportHours, engaged[1].SupportHours, 1e-3f, "the engaged left wing still sends its courier to support the right");
        AssertTrue(engaged[1].SupportHours > 1.9f, "never zero: the courier is part of it");

        // No other body: no support comes. CavalryRaid with one corps is a lone enveloping body.
        var lone = new FeasField();
        lone.AddCorps(0f, 0f, 12000f);
        lone.AddEnemyLine(25000f);
        BodyEstimate[] raid = BodiesArray(lone.Evaluate(BattlePlanId.CavalryRaid), BattlePlanId.CavalryRaid);
        AssertEqual(1, raid.Length, "one body");
        AssertTrue(float.IsPositiveInfinity(raid[0].SupportHours), "no other body: sigma is infinite");

        // A Held crossing between the supporter and the body adds an hour: the fix supports the wing over a ford at (200, 410).
        float clear = SupportAcrossFord(false);
        float held = SupportAcrossFord(true);
        AssertNear(clear + ManeuverFeasibility.HeldCrossingHours, held, 0.02f, "a Held crossing on the support route costs an hour");
    }

    static float SupportAcrossFord(bool heldFord)
    {
        FeasField f = ThreeCorps(HorizontalBand(400f, 420f, (200f, 410f)));
        if (heldFord) f.AddEnemy(200f, 450f, 400f, heading: 180f, fireRange: 100f);
        FeasibilityLedger ledger = f.Evaluate(BattlePlanId.ManeuverAndFix);
        CandidateResult r = Result(ledger, BattlePlanId.ManeuverAndFix);
        AssertTrue(r.Verdict != FeasibilityVerdict.Impossible, "routable: " + r.Reason);
        return ledger.Bodies(in r)[0].SupportHours;
    }

    static void DetailBar()
    {
        OpportunityPersonality P(float agg, float caution, float audacity) => FeasField.Personality(agg, caution, audacity);
        AssertNear(1f, ManeuverFeasibility.Bar(P(0.5f, 0.5f, 0.5f)), 1e-6f, "a mid commander: 1.0");
        AssertNear(1.15f, ManeuverFeasibility.Bar(P(0.5f, 1f, 0.5f)), 1e-6f, "caution raises it: +0.3 x 0.5");
        AssertNear(0.85f, ManeuverFeasibility.Bar(P(0.5f, 0.5f, 1f)), 1e-6f, "audacity lowers it: -0.3 x 0.5");
        AssertNear(0.9f, ManeuverFeasibility.Bar(P(1f, 0.5f, 0.5f)), 1e-6f, "aggression lowers it: -0.2 x 0.5");
        AssertNear(1.4f, ManeuverFeasibility.Bar(P(0f, 1f, 0f)), 1e-5f, "the highest bar clamps at 1.4");
        AssertNear(0.6f, ManeuverFeasibility.Bar(P(1f, 0f, 1f)), 1e-5f, "the lowest at 0.6");
        AssertNear(0.88f, ManeuverFeasibility.Bar(P(0.5f, 0.5f, 0.9f)), 1e-5f, "audacity01 0.9: 0.88 (spec scenario 4)");
    }

    static void DetailPenaltyCases()
    {
        AssertNear(0.36f, ManeuverFeasibility.DetailPenalty(0.91f, 1f), 1e-5f, "odds 0.91 at bar 1.0");
        AssertNear(1f, ManeuverFeasibility.DetailPenalty(0.75f, 1f), 1e-6f, "odds a quarter below the bar: the full penalty");
        AssertNear(1f, ManeuverFeasibility.DetailPenalty(0.1f, 1f), 1e-6f, "and no more");
        AssertNear(0f, ManeuverFeasibility.DetailPenalty(1f, 1f), 1e-6f, "odds at the bar: none");
        AssertNear(0f, ManeuverFeasibility.DetailPenalty(2f, 1f), 1e-6f, "odds above it: none");
    }

    // The right wing's exposure with an enemy picket at (300, 300) and, optionally, forest over the ground the picket sees.
    static float WingExposure(bool forest, float picketLateral = 300f, float picketDepth = 300f)
    {
        BattleTerrainGrid grid = forest
            ? GridOf(FeasField.Size, FeasField.Size, (x, zi) =>
            {
                float lateral = x - FeasField.Size * 0.5f, depth = zi - FeasField.Size * 0.5f;
                return lateral >= 150f && lateral <= 600f && depth >= 50f && depth <= 480f ? Forest : Grass;
            })
            : null;
        FeasField f = ThreeCorps(grid);
        f.AddEnemy(picketLateral, picketDepth, 400f, heading: 180f, fireRange: 100f);
        FeasibilityLedger ledger = f.Evaluate(BattlePlanId.DoubleEnvelopment);
        BodyEstimate[] bodies = BodiesArray(ledger, BattlePlanId.DoubleEnvelopment);
        AssertTrue(bodies[bodies.Length - 1].Main, "the main body is last");
        AssertNear(0f, bodies[bodies.Length - 1].Exposure, 1e-6f, "the main body's route ends in contact by design: no exposure term");
        return RightWing(bodies).Exposure;
    }

    static void ExposureForest()
    {
        float open = WingExposure(false);
        AssertTrue(open > 0.1f && open < 0.9f, "part of the march lies in the picket's sight: " + open);
        float woods = WingExposure(true);
        AssertNear(open * 0.5f, woods, 1e-4f, "the same samples in forest count at half weight");
        // A picket that sees only the last 250 units of the route (the line end, depth 600) is not counted.
        AssertNear(0f, WingExposure(false, 450f, 650f), 1e-6f, "samples in the last 250 units do not count");
    }

    static void OddsMorale()
    {
        FeasibilityLedger one = ThreeCorps().Evaluate(BattlePlanId.ManeuverAndFix);
        BodyEstimate[] a = BodiesArray(one, BattlePlanId.ManeuverAndFix);
        BodyEstimate wing = a[0], main = a[1];
        AssertNear(1f, wing.OwnRatio, 1e-6f, "an enveloping body attacks: own 1");
        AssertNear(1.5f, main.OwnRatio, 1e-4f, "the main body holds its ground: 1.5 + TerrainStrength");
        AssertNear(wing.Strength * wing.Morale / (wing.EnemyAgainst * 1f), wing.IsolatedOdds, 1e-4f, "odds = S x m x own / (E x m_E)");
        AssertNear(main.Strength * main.Morale * 1.5f / (main.EnemyAgainst * 1f), main.IsolatedOdds, 1e-4f, "the main body's odds carry its ratio");

        var weak = new FeasField();
        weak.AddCorps(-300f, 0f, 12000f);
        weak.AddCorps(0f, 0f, 11000f);
        weak.AddCorps(300f, 0f, 12000f);
        weak.AddEnemyLine(25000f);
        for (int i = 0; i < weak.Enemies.Count; i++)
        {
            EnemyObservation o = weak.Enemies[i];
            o.Morale = 0.5f;
            weak.Enemies[i] = o;
        }
        BodyEstimate[] b = BodiesArray(weak.Evaluate(BattlePlanId.ManeuverAndFix), BattlePlanId.ManeuverAndFix);
        AssertNear(wing.IsolatedOdds * 2f, b[0].IsolatedOdds, 1e-3f, "the enemy at half morale doubles the wing's odds");
        AssertNear(main.IsolatedOdds * 2f, b[1].IsolatedOdds, 1e-3f, "and the main body's");
    }

    static void ApproachAnchored()
    {
        float plain = RightWing(BodiesArray(ThreeCorps().Evaluate(BattlePlanId.DoubleEnvelopment), BattlePlanId.DoubleEnvelopment)).Approach;
        AssertNear(0f, plain, 1e-4f, "open ground and an open flank: no approach term");
        // A river beyond the right line end (lateral 470-490, the end at 450): the flank rests on it.
        FeasField anchored = ThreeCorps(VerticalBand(470f, 490f));
        BodyEstimate[] bodies = BodiesArray(anchored.Evaluate(BattlePlanId.DoubleEnvelopment), BattlePlanId.DoubleEnvelopment);
        AssertNear(ManeuverFeasibility.AnchoredEndPenalty, RightWing(bodies).Approach, 1e-3f, "the turned flank is no flank: +0.5");
        BodyEstimate left = bodies[0].TargetX < bodies[1].TargetX ? bodies[0] : bodies[1];
        AssertNear(0f, left.Approach, 1e-3f, "the open left end is not anchored");
    }

    static void Bc13Half()
    {
        FeasField f = ThreeCorps();
        int middleInstance = int.Parse(f.ChildIds[1].Substring("child-".Length));
        f.Exploits = new FakeExploits { Ancestor = middleInstance };
        FeasibilityLedger ledger = f.Evaluate(BattlePlanId.ManeuverAndFix);
        BodyEstimate[] bodies = BodiesArray(ledger, BattlePlanId.ManeuverAndFix);
        AssertNear(11000f * 0.5f + 12000f, bodies[1].Strength, 0.5f, "the child with an exploit below it counts half in S");
        AssertNear(11000f * 0.5f + 12000f, bodies[1].PinStrength, 0.5f, "and in the pin");
        AssertNear((11000f * 0.5f + 12000f) / bodies[1].DefenseRatio, bodies[0].Pin, 1f, "the wing's pin carries the half");
    }

    static void DaylightCase()
    {
        FeasField f = ThreeCorps();
        FeasibilityLedger ledger = f.Evaluate(BattlePlanId.DoubleEnvelopment);
        BodyEstimate wing = RightWing(BodiesArray(ledger, BattlePlanId.DoubleEnvelopment));
        float strike = wing.DesiredStrikeHours;
        AssertTrue(strike > 0.5f, "the strike is a march away: " + strike);
        AssertNear(0f, wing.Daylight, 1e-6f, "ten hours of daylight: none");

        FeasField dark = ThreeCorps();
        dark.DaylightHours = strike + ManeuverFeasibility.DaylightMarginHours - 0.01f;
        FeasibilityLedger ld = dark.Evaluate(BattlePlanId.DoubleEnvelopment);
        CandidateResult r = Result(ld, BattlePlanId.DoubleEnvelopment);
        AssertNear(1f, RightWing(ld.Bodies(in r).ToArray()).Daylight, 1e-6f, "a strike past the end of day less a quarter hour");
        AssertEqual(FeasibilityVerdict.Risky, r.Verdict, "after dark is Risky, never Impossible");
        AssertEqual("after-dark", r.Reason, "reason");

        FeasField lit = ThreeCorps();
        lit.DaylightHours = strike + ManeuverFeasibility.DaylightMarginHours + 0.01f;
        AssertNear(0f, RightWing(BodiesArray(lit.Evaluate(BattlePlanId.DoubleEnvelopment), BattlePlanId.DoubleEnvelopment)).Daylight, 1e-6f, "just inside the margin: none");
    }

    // GenericAggressive holds the own bank with every rect at forward 0.4: its strike legs to the enemy anchor cross a river at depth 470-490.
    static BodyEstimate StrikeLegBody(BattleTerrainGrid grid, Action<FeasField> setup, BattlePlanId plan, out CandidateResult result)
    {
        FeasField f = ThreeCorps(grid);
        setup?.Invoke(f);
        FeasibilityLedger ledger = f.Evaluate(plan);
        result = Result(ledger, plan);
        return ledger.Bodies(in result)[0];
    }

    static void StrikeLeg()
    {
        // The ford is 255 units from the nearest enemy cluster, beyond its reach: Clear unless a regiment is put at it.
        BattleTerrainGrid ford = HorizontalBand(470f, 490f, (-225f, 480f));
        // Held by a regiment on the far bank facing the ford: half weight.
        BodyEstimate held = StrikeLegBody(ford, f => f.AddEnemy(-225f, 520f, 400f, heading: 180f, fireRange: 100f), BattlePlanId.GenericAggressive, out CandidateResult rh);
        AssertEqual(CrossingState.Held, CrossingStateOf(ford, 180f), "sanity: the fixture holds the ford");
        AssertEqual(CrossingState.Clear, CrossingStateOf(ford, float.NaN), "and leaves it Clear without the regiment");
        AssertNear(0.5f, held.Contested, 1e-4f, "a main-body strike leg through a Held crossing: 0.5");
        AssertNear(0.5f, held.StrikeContested, 1e-4f, "from the strike leg");
        // Watched: the regiment faces away.
        BodyEstimate watched = StrikeLegBody(ford, f => f.AddEnemy(-225f, 520f, 400f, heading: 0f, fireRange: 100f), BattlePlanId.GenericAggressive, out _);
        AssertNear(0.25f, watched.Contested, 1e-4f, "through a Watched one: 0.25");
        // Clear: nothing about.
        BodyEstimate clear = StrikeLegBody(ford, null, BattlePlanId.GenericAggressive, out _);
        AssertNear(0f, clear.Contested, 1e-4f, "through a Clear one: 0");

        // No path: the member can still hold its anchor; its term is 1 and the reason says why.
        BodyEstimate none = StrikeLegBody(HorizontalBand(470f, 490f), null, BattlePlanId.GenericAggressive, out CandidateResult rn);
        AssertNear(1f, none.Contested, 1e-4f, "no strike path: contested 1");
        AssertTrue(none.NoStrikePath, "marked");
        AssertTrue(rn.Verdict != FeasibilityVerdict.Impossible, "not impossible, got " + rn.Verdict);
        AssertEqual(FeasibilityVerdict.Risky, rn.Verdict, "0.6 is Risky");
        AssertEqual("no-strike-crossing", rn.Reason, "reason");

        // A march leg through the same kind of Held crossing counts in full: FrontalShockAssault's Center stands at forward 0.5 (depth 300).
        BattleTerrainGrid near = HorizontalBand(270f, 290f, (0f, 280f));
        BodyEstimate march = StrikeLegBody(near, f => f.AddEnemy(0f, 320f, 400f, heading: 180f, fireRange: 100f), BattlePlanId.FrontalShockAssault, out _);
        AssertNear(1f, march.Contested, 1e-4f, "a march leg through a Held crossing: 1");
    }

    // The state of the ford at (-225, 480) with a regiment on its far bank at the given heading (NaN: no regiment).
    static CrossingState CrossingStateOf(BattleTerrainGrid grid, float heading)
    {
        FeasField f = ThreeCorps(grid);
        if (!float.IsNaN(heading)) f.AddEnemy(-225f, 520f, 400f, heading: heading, fireRange: 100f);
        f.Build(BattlePlanId.GenericAggressive);
        return f.Crossings.StateOf(0);
    }

    static void FeasFog()
    {
        BattleTerrainGrid grid = HorizontalBand(470f, 490f, (-225f, 480f));
        UnitRecord hidden = new UnitRecord
        {
            InstanceId = 9000, UnitTyp = TacticalUnitType.Infantry, X = FeasField.X(-225f), Z = FeasField.Z(520f), Strength = 9000, Morale = 1f,
            RotationY = 180f, FireRange = 100f, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry,
        };
        FeasField baseline = ThreeCorps(grid);
        FeasibilityLedger lb = baseline.Evaluate(BattlePlanId.DoubleEnvelopment);
        BodyEstimate[] bb = BodiesArray(lb, BattlePlanId.DoubleEnvelopment);

        FeasField hiddenField = ThreeCorps(grid);
        hiddenField.HiddenEnemies.Add(hidden);
        FeasibilityLedger lh = hiddenField.Evaluate(BattlePlanId.DoubleEnvelopment);
        BodyEstimate[] bh = BodiesArray(lh, BattlePlanId.DoubleEnvelopment);
        AssertNear(lb.Job.Enemy.N, lh.Job.Enemy.N, 1e-3f, "a hidden regiment changes no N");
        AssertEqual(CrossingState.Clear, bh[0].AttackCrossing, "and no crossing state: the ford stays Clear");
        for (int i = 0; i < bb.Length; i++)
        {
            AssertNear(bb[i].EnemyShiftHours, bh[i].EnemyShiftHours, 1e-4f, "no shift time of body " + i);
            AssertNear(bb[i].Penalty, bh[i].Penalty, 1e-5f, "no penalty of body " + i);
        }

        // Contrast: the same regiment perceived changes the crossing state, N and the shift time.
        FeasField seen = ThreeCorps(grid);
        seen.AddEnemy(-225f, 520f, 9000f, heading: 180f, fireRange: 100f);
        FeasibilityLedger ls = seen.Evaluate(BattlePlanId.DoubleEnvelopment);
        BodyEstimate[] bs = BodiesArray(ls, BattlePlanId.DoubleEnvelopment);
        AssertEqual(CrossingState.Held, bs[0].AttackCrossing, "perceived, it holds the ford");
        AssertTrue(ls.Job.Enemy.N > lb.Job.Enemy.N + 8000f, "and counts in N");
        AssertTrue(Math.Abs(bs[0].EnemyShiftHours - bb[0].EnemyShiftHours) > 1e-3f, "and moves the enemy's mass, so its shift time");
    }

    // A scanner for the fog rules over the BC14 file set (requirement-enemy-picture): an enemy unit record is read only through a Visible
    // observation's row, and the vanilla arrival schedule and duration are never read.
    static readonly Regex FogReads = new Regex(@"EnemyRecords|scheduledarrival|durationleft", RegexOptions.Compiled);

    static string FirstFogRead(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            Match m = FogReads.Match(line);
            if (m.Success) return m.Value + " in: " + line.Trim();
        }
        return null;
    }

    static void FeasPin()
    {
        AssertTrue(FirstFogRead("var r = snapshot.EnemyRecords[i];") != null, "the scanner fires on a planted EnemyRecords read");
        AssertTrue(FirstFogRead("float t = reg.scheduledarrival - now;") != null, "and on scheduledarrival");
        AssertTrue(FirstFogRead("float d = reg.durationleft;") != null, "and on durationleft");
        AssertTrue(FirstFogRead("var o = snapshot.Observations[i];") == null, "it does not fire on the observation rows");
        string[] files = Bc14Files();
        AssertTrue(files.Length >= 8, "the BC14 file set is found (" + files.Length + " files)");
        foreach (string file in files)
        {
            string hit = FirstFogRead(File.ReadAllText(file));
            AssertTrue(hit == null, Path.GetFileName(file) + " reads hidden enemy state: " + hit);
        }
    }
}
