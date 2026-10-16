using System;
using System.IO;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static BattleMovementHonestyTests;
using static ManeuverFeasibilityFixtures;

// BC14 task 7: the hypothetical layout, the bodies, the verdict and the mirrored frame (spec § 13.1 S4). The fields are the spec's § 6
// frame: lateral and depth on a 2,400-unit map, three corps (12,000, 11,000, 12,000) at depth 0 against three clusters at depth 600.
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] VerdictTests() => new (string, Action)[]
    {
        ("bc14 CANDIDATE-CENTRED a centred template is evaluated once, a single off-centre flank on both sides, a Defender's candidates once", CandidateCentred),
        ("bc14 BODIES each enveloping child is a body, the main body is its fixing and reserve members, strengths exclude nothing here", BodiesOfAPlay),
        ("bc14 VERDICT-UNFILLED DoubleEnvelopment with one child is impossible: wing-unfilled", VerdictUnfilled),
        ("bc14 VERDICT-UNKNOWN a picture that is not ready gives Unknown at penalty 0", VerdictUnknown),
        ("bc14 VERDICT-RISKY penalty 0.35 is Risky and 0.34 Feasible", VerdictRisky),
        ("bc14 VERDICT-ORDER no axis, map unknown, wing-unfilled and no-crossing are taken in that order", VerdictOrder),
        ("bc14 SIDE-RANK sides rank by verdict then penalty, a tie keeps the authored side, and the better side is published", SideRank),
        ("bc14 BC13-EXCLUDE a child with a live exploit is not offered to the layout", Bc13Exclude),
        ("bc14 BC13-PERSONALITY the table entry with the flag on, the roster vector with it off, one conversion for both doctrines", Bc13Personality),
        ("bc14 INWARD-LEG a wing whose line end lies across an unlinked river is impossible; a linked one carries its via and surplus", InwardLeg),
    };

    static readonly BattlePlanId[] AllPlans =
    {
        BattlePlanId.DoubleEnvelopment, BattlePlanId.MobileFlankMarch, BattlePlanId.PreparedDefense, BattlePlanId.ManeuverAndFix,
        BattlePlanId.ContinuousAttrition, BattlePlanId.ReverseSlopeDefense, BattlePlanId.WideFlankApproach, BattlePlanId.FrontalShockAssault,
        BattlePlanId.ForcedFrontalAttack, BattlePlanId.HesitatedCommit, BattlePlanId.GenericAggressive, BattlePlanId.GenericCautious,
        BattlePlanId.GenericMethodical, BattlePlanId.GenericDesperate, BattlePlanId.CavalryScreeningDelay, BattlePlanId.CavalryRaid,
        BattlePlanId.MeetingEngagement, BattlePlanId.DelayingDefense,
    };

    // The scenario 1 army and enemy on a river-free field.
    static FeasField ThreeCorps(BattleTerrainGrid grid = null, float enemy = 25000f)
    {
        var f = new FeasField(grid);
        f.AddCorps(-300f, 0f, 12000f);
        f.AddCorps(0f, 0f, 11000f);
        f.AddCorps(300f, 0f, 12000f);
        f.AddEnemyLine(enemy);
        return f;
    }

    static CandidateResult Result(FeasibilityLedger ledger, BattlePlanId plan)
    {
        AssertTrue(ledger.TryGet(plan, out CandidateResult r), plan + " has a result");
        return r;
    }

    static void CandidateCentred()
    {
        FeasField f = ThreeCorps();
        f.Stance = BattleStance.Meeting;
        FeasibilityLedger ledger = f.Evaluate(AllPlans);
        for (int i = 0; i < AllPlans.Length; i++)
        {
            bool twoSided = AllPlans[i] == BattlePlanId.MobileFlankMarch || AllPlans[i] == BattlePlanId.WideFlankApproach || AllPlans[i] == BattlePlanId.ManeuverAndFix;
            AssertEqual(twoSided ? 2 : 1, ledger.Job.SidesEvaluated[i], AllPlans[i] + " is evaluated on " + (twoSided ? "both sides" : "one side"));
        }
        AssertFalse(Result(ledger, BattlePlanId.CavalryRaid).Mirrored, "CavalryRaid is centred: Mirrored false");
        AssertFalse(Result(ledger, BattlePlanId.DoubleEnvelopment).Mirrored, "DoubleEnvelopment is symmetric: Mirrored false");

        // A Defender's candidates are each evaluated once.
        FeasField d = ThreeCorps();
        d.Stance = BattleStance.Defender;
        BattlePlanId[] defensive =
        {
            BattlePlanId.PreparedDefense, BattlePlanId.DelayingDefense, BattlePlanId.GenericCautious, BattlePlanId.MeetingEngagement, BattlePlanId.GenericMethodical,
        };
        FeasibilityLedger defender = d.Evaluate(defensive);
        for (int i = 0; i < defensive.Length; i++) AssertEqual(1, defender.Job.SidesEvaluated[i], defensive[i] + " is evaluated once");
    }

    static void BodiesOfAPlay()
    {
        FeasField f = ThreeCorps();
        FeasibilityLedger ledger = f.Evaluate(BattlePlanId.ManeuverAndFix);
        CandidateResult r = Result(ledger, BattlePlanId.ManeuverAndFix);
        ReadOnlySpan<BodyEstimate> bodies = ledger.Bodies(in r);
        AssertEqual(2, bodies.Length, "the wing and the main body");
        AssertTrue(bodies[0].Envelops && !bodies[0].Main && bodies[0].Kind == PlayRectKind.RightWing, "the enveloping body comes first");
        AssertTrue(bodies[1].Main && !bodies[1].Envelops, "the main body comes last");
        AssertEqual(f.ChildIds[2], bodies[0].ChildId, "the right corps takes the right wing");
        AssertEqual(f.ChildIds[1], bodies[1].ChildId, "the main body is named by its Center member, the middle corps");
        AssertNear(12000f, bodies[0].Strength, 0.5f, "the wing is the right corps");
        AssertNear(23000f, bodies[1].Strength, 0.5f, "the main body is the Center and the Demonstration: 11,000 + 12,000");
        AssertEqual(2, bodies[1].ChildCount, "two members");
        AssertNear(300f / 3600f, bodies[0].OffsetHours, 1e-5f, "ManeuverAndFix' wing starts 300 s after the fix's contact");
        ReadOnlySpan<MemberEstimate> members = ledger.Members(in r);
        AssertEqual(3, members.Length, "three members");
        int fixes = 0, wings = 0;
        for (int i = 0; i < members.Length; i++)
        {
            if (members[i].Kind == ManeuverFeasibility.MemberFix) fixes++;
            if (members[i].Kind == ManeuverFeasibility.MemberEnvelops) wings++;
        }
        AssertEqual(2, fixes, "the Center and the Demonstration are fixing members");
        AssertEqual(1, wings, "and the right wing is the one enveloping member");
    }

    static void VerdictUnfilled()
    {
        var f = new FeasField();
        f.AddCorps(0f, 0f, 12000f);
        f.AddEnemyLine(25000f);
        CandidateResult r = Result(f.Evaluate(BattlePlanId.DoubleEnvelopment), BattlePlanId.DoubleEnvelopment);
        AssertEqual(FeasibilityVerdict.Impossible, r.Verdict, "an enveloping rect with no child cannot be done");
        AssertEqual("wing-unfilled", r.Reason, "reason");
        AssertNear(1f, r.Penalty, 1e-6f, "an impossible candidate ranks at penalty 1");

        // Control: with three children the same play is possible.
        CandidateResult control = Result(ThreeCorps().Evaluate(BattlePlanId.DoubleEnvelopment), BattlePlanId.DoubleEnvelopment);
        AssertTrue(control.Verdict == FeasibilityVerdict.Feasible || control.Verdict == FeasibilityVerdict.Risky, "control: three corps fill both wings, got " + control.Verdict);
    }

    static void VerdictUnknown()
    {
        FeasField f = ThreeCorps();
        FeasibilityInput input = f.Build(BattlePlanId.ManeuverAndFix);
        var ledger = new FeasibilityLedger();
        ManeuverFeasibility.Evaluate(in input, ledger);
        CandidateResult known = Result(ledger, BattlePlanId.ManeuverAndFix);
        AssertTrue(known.Verdict == FeasibilityVerdict.Feasible || known.Verdict == FeasibilityVerdict.Risky, "control: with the map ready the play has a verdict, got " + known.Verdict);

        input.Picture = new TerrainPicture(f.Grid, FeasField.Size, FeasField.Size);   // not Ready
        ManeuverFeasibility.Evaluate(in input, ledger);
        CandidateResult r = Result(ledger, BattlePlanId.ManeuverAndFix);
        AssertEqual(FeasibilityVerdict.Unknown, r.Verdict, "the picture is not ready");
        AssertEqual("map-unknown", r.Reason, "reason");
        AssertNear(0f, r.Penalty, 1e-6f, "an Unknown candidate carries no penalty");

        input = f.Build(BattlePlanId.ManeuverAndFix);
        input.Regions = new RiverRegions();                                           // not Ready
        ManeuverFeasibility.Evaluate(in input, ledger);
        AssertEqual(FeasibilityVerdict.Unknown, Result(ledger, BattlePlanId.ManeuverAndFix).Verdict, "the regions are not ready");
        input = f.Build(BattlePlanId.ManeuverAndFix);
        input.Grid = null;
        ManeuverFeasibility.Evaluate(in input, ledger);
        AssertEqual(FeasibilityVerdict.Unknown, Result(ledger, BattlePlanId.ManeuverAndFix).Verdict, "no grid");
    }

    static void VerdictRisky()
    {
        AssertEqual(FeasibilityVerdict.Risky, ManeuverFeasibility.VerdictOf(0.35f), "0.35 is Risky");
        AssertEqual(FeasibilityVerdict.Feasible, ManeuverFeasibility.VerdictOf(0.34f), "0.34 is Feasible");
        AssertEqual(FeasibilityVerdict.Feasible, ManeuverFeasibility.VerdictOf(0f), "0 is Feasible");
        AssertEqual(FeasibilityVerdict.Risky, ManeuverFeasibility.VerdictOf(0.9f), "0.9 is Risky");
    }

    // A vertical river band at lateral [from, to) with no crossing at all.
    static BattleTerrainGrid VerticalBand(float lateralFrom, float lateralTo)
    {
        int from = (int)FeasField.X(lateralFrom), to = (int)FeasField.X(lateralTo);
        return GridOf(FeasField.Size, FeasField.Size, (x, zi) => x >= from && x < to ? River : Grass);
    }

    static void VerdictOrder()
    {
        // The would-be no-crossing: one corps east of a band at lateral -100, DoubleEnvelopment's left wing (lateral -135 of a 300-unit
        // frontage) west of it, the right wing unfilled.
        BattleTerrainGrid band = VerticalBand(-100f, -80f);
        var one = new FeasField(band);
        one.AddCorps(0f, 0f, 12000f);
        one.AddEnemyLine(25000f);
        FeasibilityInput input = one.Build(BattlePlanId.DoubleEnvelopment);
        var ledger = new FeasibilityLedger();
        ManeuverFeasibility.Evaluate(in input, ledger);
        CandidateResult unfilled = Result(ledger, BattlePlanId.DoubleEnvelopment);
        AssertEqual("wing-unfilled", unfilled.Reason, "wing-unfilled is taken before no-crossing");

        // Control: with a second corps the left wing is filled and its anchor lies across the band: no-crossing.
        var two = new FeasField(band);
        two.AddCorps(-25f, 0f, 12000f);
        two.AddCorps(25f, 0f, 12000f);
        two.AddEnemyLine(25000f);
        CandidateResult crossing = Result(two.Evaluate(BattlePlanId.DoubleEnvelopment), BattlePlanId.DoubleEnvelopment);
        AssertEqual("no-crossing", crossing.Reason, "control: filled wings, a band with no crossing ");
        AssertEqual(FeasibilityVerdict.Impossible, crossing.Verdict, "impossible");

        // Map unknown comes before wing-unfilled.
        input.Picture = new TerrainPicture(one.Grid, FeasField.Size, FeasField.Size);
        ManeuverFeasibility.Evaluate(in input, ledger);
        CandidateResult unknown = Result(ledger, BattlePlanId.DoubleEnvelopment);
        AssertEqual(FeasibilityVerdict.Unknown, unknown.Verdict, "map unknown beats wing-unfilled");
        AssertEqual("map-unknown", unknown.Reason, "reason");

        // No axis comes before the map.
        input = one.Build(BattlePlanId.DoubleEnvelopment);
        input.Axis = TacticalFrontAxis.None;
        ManeuverFeasibility.Evaluate(in input, ledger);
        CandidateResult noAxis = Result(ledger, BattlePlanId.DoubleEnvelopment);
        AssertEqual(FeasibilityVerdict.Unknown, noAxis.Verdict, "no axis is Unknown");
        AssertEqual("no-axis", noAxis.Reason, "no-axis beats wing-unfilled");
        input.Picture = new TerrainPicture(one.Grid, FeasField.Size, FeasField.Size);
        ManeuverFeasibility.Evaluate(in input, ledger);
        AssertEqual("no-axis", Result(ledger, BattlePlanId.DoubleEnvelopment).Reason, "no-axis beats map-unknown");

        // No children at all.
        var none = new FeasField();
        none.AddCorps(0f, 0f, 12000f);
        none.ChildIds.Clear();                 // the army stands there, but no direct child is passed
        none.DeployedReserve.Clear();
        none.AddEnemyLine(25000f);
        CandidateResult noChildren = Result(none.Evaluate(BattlePlanId.DoubleEnvelopment), BattlePlanId.DoubleEnvelopment);
        AssertEqual("no-children", noChildren.Reason, "an army with no offered child");
    }

    static void SideRank()
    {
        CandidateResult Of(FeasibilityVerdict v, float penalty) => new CandidateResult { Verdict = v, Penalty = penalty };
        AssertTrue(ManeuverFeasibility.BetterSide(Of(FeasibilityVerdict.Feasible, 0.1f), Of(FeasibilityVerdict.Unknown, 0f)), "a Feasible side at 0.1 beats an Unknown side");
        AssertTrue(ManeuverFeasibility.BetterSide(Of(FeasibilityVerdict.Risky, 0.5f), Of(FeasibilityVerdict.Unknown, 0f)), "a Risky side beats an Unknown side");
        AssertFalse(ManeuverFeasibility.BetterSide(Of(FeasibilityVerdict.Unknown, 0f), Of(FeasibilityVerdict.Feasible, 0.1f)), "and not the other way");
        AssertTrue(ManeuverFeasibility.BetterSide(Of(FeasibilityVerdict.Unknown, 0f), Of(FeasibilityVerdict.Impossible, 1f)), "an Unknown side beats an Impossible one");
        AssertTrue(ManeuverFeasibility.BetterSide(Of(FeasibilityVerdict.Feasible, 0.2f), Of(FeasibilityVerdict.Impossible, 1f)), "a Feasible side beats an Impossible one");
        AssertFalse(ManeuverFeasibility.BetterSide(Of(FeasibilityVerdict.Impossible, 1f), Of(FeasibilityVerdict.Unknown, 0f)), "an Impossible side loses to Unknown");
        AssertTrue(ManeuverFeasibility.BetterSide(Of(FeasibilityVerdict.Feasible, 0.1f), Of(FeasibilityVerdict.Risky, 0.4f)), "within a class the lower penalty wins");
        AssertFalse(ManeuverFeasibility.BetterSide(Of(FeasibilityVerdict.Feasible, 0.2f), Of(FeasibilityVerdict.Feasible, 0.2f)), "equal penalties keep the authored side");

        // Evaluated: the authored side's right wing lies across a band with no crossing (impossible); the mirrored side stays on the bank.
        BattleTerrainGrid band = VerticalBand(225f, 245f);
        var f = new FeasField(band);
        f.AddCorps(-200f, 0f, 12000f);
        f.AddCorps(0f, 0f, 11000f);
        f.AddCorps(200f, 0f, 12000f);
        f.AddEnemyLine(25000f);
        FeasibilityLedger ledger = f.Evaluate(BattlePlanId.ManeuverAndFix);
        CandidateResult r = Result(ledger, BattlePlanId.ManeuverAndFix);
        AssertEqual(2, ledger.Job.SidesEvaluated[0], "both sides were evaluated");
        AssertTrue(r.Mirrored, "the mirrored side is published");
        AssertTrue(r.Verdict == FeasibilityVerdict.Feasible || r.Verdict == FeasibilityVerdict.Risky, "and it is possible, got " + r.Verdict + " " + r.Reason);
        ReadOnlySpan<BodyEstimate> bodies = ledger.Bodies(in r);
        AssertTrue(bodies[0].TargetX < FeasField.X(0f), "the wing marches to the left of the army's centre line");
    }

    // A fake exploit table: the listed instance is a live exploit asset.
    sealed class FakeExploits : IExploitLookup
    {
        public int Asset, Ancestor;
        public bool IsExploiting(int instanceId) => instanceId == Asset;
        public bool HasExploitingDescendant(int instanceId) => instanceId == Ancestor;
    }

    static void Bc13Exclude()
    {
        FeasField f = ThreeCorps();
        FeasibilityLedger control = f.Evaluate(BattlePlanId.ManeuverAndFix);
        CandidateResult cr = Result(control, BattlePlanId.ManeuverAndFix);
        bool middleOffered = false;
        ReadOnlySpan<MemberEstimate> all = control.Members(in cr);
        for (int i = 0; i < all.Length; i++) if (all[i].ChildId == f.ChildIds[1]) middleOffered = true;
        AssertTrue(middleOffered, "control: without an exploit the middle corps is a member");

        FeasField g = ThreeCorps();
        int middleInstance = int.Parse(g.ChildIds[1].Substring("child-".Length));
        g.Exploits = new FakeExploits { Asset = middleInstance };
        FeasibilityLedger ledger = g.Evaluate(BattlePlanId.ManeuverAndFix);
        CandidateResult r = Result(ledger, BattlePlanId.ManeuverAndFix);
        ReadOnlySpan<MemberEstimate> members = ledger.Members(in r);
        for (int i = 0; i < members.Length; i++) AssertTrue(members[i].ChildId != g.ChildIds[1], "the exploit asset is not offered: " + members[i].ChildId);
        AssertEqual(2, members.Length, "the other two corps fill the wing and the Center");

        // A deployed reserve is not offered either (BC10).
        var d = new FeasField();
        d.AddCorps(-300f, 0f, 12000f);
        d.AddCorps(0f, 0f, 11000f);
        d.AddCorps(300f, 0f, 12000f, deployedReserve: true);
        d.AddEnemyLine(25000f);
        FeasibilityLedger dl = d.Evaluate(BattlePlanId.ManeuverAndFix);
        CandidateResult dr = Result(dl, BattlePlanId.ManeuverAndFix);
        ReadOnlySpan<MemberEstimate> dm = dl.Members(in dr);
        for (int i = 0; i < dm.Length; i++) AssertTrue(dm[i].ChildId != d.ChildIds[2], "a deployed reserve is left out of the layout");
    }

    static void Bc13Personality()
    {
        var table = new PersonalityVector(0.8f, -0.4f, 0.6f, 0.2f, 0f);
        var roster = new PersonalityVector(-0.2f, 0.3f, 0.1f, 0.0f, 0f);
        PersonalityVector on = CommanderPersonalityDerivation.ChooseVector(true, true, in table, PersonalitySource.Derived, in roster, out PersonalitySource onSource);
        AssertNear(0.8f, on.Aggression, 1e-6f, "with the flag on and an entry, the table's vector");
        AssertEqual(PersonalitySource.Derived, onSource, "and its source");
        PersonalityVector off = CommanderPersonalityDerivation.ChooseVector(false, true, in table, PersonalitySource.Derived, in roster, out PersonalitySource offSource);
        AssertNear(-0.2f, off.Aggression, 1e-6f, "with the flag off, the roster vector");
        AssertEqual(PersonalitySource.Default, offSource, "from the default source");
        PersonalityVector missing = CommanderPersonalityDerivation.ChooseVector(true, false, in table, PersonalitySource.Derived, in roster, out _);
        AssertNear(-0.2f, missing.Aggression, 1e-6f, "an army commander with no entry reads the roster vector");

        OpportunityPersonality p = CommanderPersonalityDerivation.ToOpportunity(in table, PersonalitySource.Registry, 0.5f);
        AssertNear(0.9f, p.Agg01, 1e-6f, "Agg01 = To01(0.8)");
        AssertNear(0.3f, p.Caution01, 1e-6f, "Caution01 = To01(-0.4)");
        AssertNear(0.8f, p.Audacity01, 1e-6f, "Audacity01 = To01(0.6)");
        AssertNear(0.6f, p.CasualtyTolerance01, 1e-6f, "CasualtyTolerance01 = To01(0.2)");
        AssertNear(0.5f, p.Duty, 1e-6f, "duty is carried");
        AssertEqual(PersonalitySource.Registry, p.Source, "the source is carried");

        // BC13's opportunity producer makes its personality through the same two functions (one conversion, not two).
        string producer = File.ReadAllText(TestFixtures.FindRepoPath("src/WhiskeyRealism/Tactical/Battle/Producers/OpportunityIntentProducer.cs"));
        AssertContains(producer, "CommanderPersonalityDerivation.ChooseVector(", "the producer chooses the vector through the shared function");
        AssertContains(producer, "CommanderPersonalityDerivation.ToOpportunity(", "and converts it through the shared function");
        AssertFalse(producer.Contains("CasualtyTolerance01 = CommanderPersonalityDerivation.To01"), "the producer no longer converts by hand");
    }

    // A horizontal river band at depth [from, to) with the given fords (lateral, depth).
    static BattleTerrainGrid HorizontalBand(float depthFrom, float depthTo, params (float lateral, float depth)[] fords)
    {
        int from = (int)(depthFrom + FeasField.Size * 0.5f), to = (int)(depthTo + FeasField.Size * 0.5f);
        var cx = new float[fords.Length];
        var cz = new float[fords.Length];
        var cid = new int[fords.Length];
        for (int i = 0; i < fords.Length; i++) { cx[i] = FeasField.X(fords[i].lateral); cz[i] = FeasField.Z(fords[i].depth); cid[i] = Ford; }
        return GridOf(FeasField.Size, FeasField.Size, (x, zi) => zi >= from && zi < to ? River : Grass, false, cx, cz, cid);
    }

    static void InwardLeg()
    {
        // The river runs across the front at depth 470-490: the wings (forward 0.4 = depth 240) stand on the own bank, the line ends at depth 600 lie across it.
        FeasField none = ThreeCorps(HorizontalBand(470f, 490f));
        CandidateResult r0 = Result(none.Evaluate(BattlePlanId.DoubleEnvelopment), BattlePlanId.DoubleEnvelopment);
        AssertEqual(FeasibilityVerdict.Impossible, r0.Verdict, "no link: the inward leg crosses an open river");
        AssertEqual("river-on-leg", r0.Reason, "reason");

        // A Clear ford 210 units off the straight line of the left wing, beyond the reach of every perceived regiment (and inside the enemy's extent).
        FeasField clear = ThreeCorps(HorizontalBand(470f, 490f, (-225f, 480f)));
        FeasibilityLedger lc = clear.Evaluate(BattlePlanId.DoubleEnvelopment);
        CandidateResult rc = Result(lc, BattlePlanId.DoubleEnvelopment);
        AssertTrue(rc.Verdict != FeasibilityVerdict.Impossible, "a link makes the leg possible, got " + rc.Verdict + " " + rc.Reason);
        ReadOnlySpan<BodyEstimate> bc = lc.Bodies(in rc);
        AssertTrue(bc[0].AttackVia == 0, "the left wing's attack via is the ford, got " + bc[0].AttackVia);
        AssertEqual(CrossingState.Clear, bc[0].AttackCrossing, "the ford was probed: Clear");
        AssertTrue(bc[0].InwardSurplusHours > 0.05f, "the detour to the ford delays the strike, surplus " + bc[0].InwardSurplusHours);
        MarchRules rules = ShippedRules();
        float expected = bc[0].CourierHours + bc[0].MarchHours + ManeuverFeasibility.DeployHours(in rules) + bc[0].InwardSurplusHours;
        AssertNear(expected, bc[0].StrikeHours, 1e-4f, "T = courier + march + deploy + the inward surplus");
        AssertNear(0f, bc[0].Contested, 1e-6f, "a Clear ford adds no blood");

        // The same ford Held by a regiment on the far bank facing it.
        FeasField held = ThreeCorps(HorizontalBand(470f, 490f, (-225f, 480f)));
        held.AddEnemy(-225f, 520f, 400f, heading: 180f, fireRange: 100f);
        FeasibilityLedger lh = held.Evaluate(BattlePlanId.DoubleEnvelopment);
        CandidateResult rh = Result(lh, BattlePlanId.DoubleEnvelopment);
        ReadOnlySpan<BodyEstimate> bh = lh.Bodies(in rh);
        AssertEqual(CrossingState.Held, bh[0].AttackCrossing, "the regiment holds the ford");
        AssertNear(1f, bh[0].Contested, 1e-6f, "a Held crossing on the inward leg counts in full");
        AssertNear(bc[0].StrikeHours + ManeuverFeasibility.HeldCrossingHours, bh[0].StrikeHours, 0.02f, "and a Held one adds an hour to T");
    }
}
