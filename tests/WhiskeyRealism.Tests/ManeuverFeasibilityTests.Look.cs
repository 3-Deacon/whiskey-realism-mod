using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static TestFixtures;
using static BattleMovementHonestyTests;
using static ManeuverFeasibilityFixtures;
using static CommanderOpportunityTests;

// BC14 task 14: the crossing look (spec § 7 requirement-crossing-look) and the spec's scenario 5, the Defender's options (§ 6 item 5).
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] LookTests() => new (string, Action)[]
    {
        ("bc14 LOOK-PRIORITY the selected route's unknown crossing first, then the best candidates' routes, a Defender's threats last; at most two; a request ends when the crossing is seen", LookPriority),
        ("bc14 FEAS-SCN-5 the Defender's options: turning exposure, a look at the unknown ford, and a reserve released to guard it", FeasScn5),
    };

    // ---- LOOK-PRIORITY -------------------------------------------------------------------------------------------

    // Three fords across the front (no bridge: every wing must use one), none perceived and no enemy near any: all Unknown.
    static FeasField LookField(Func<float, float, bool> perceived = null)
    {
        var f = new FeasField(BandWithCrossings(240f, 260f, (-900f, 250f, Ford), (450f, 250f, Ford), (900f, 250f, Ford)));
        f.AddCorps(-300f, 0f, 12000f);
        f.AddCorps(0f, 0f, 11000f);
        f.AddCorps(300f, 0f, 12000f);
        f.AddEnemy(-400f, 556f, 11000f);
        f.AddEnemy(0f, 556f, 11500f);
        f.AddEnemy(400f, 556f, 11000f);
        f.EnemyHalfExtent = 450f;
        f.Perceived = perceived ?? ((x, z) => false);
        return f;
    }

    static readonly BattlePlanId[] LookPlans =
    {
        BattlePlanId.ManeuverAndFix, BattlePlanId.DoubleEnvelopment, BattlePlanId.FrontalShockAssault, BattlePlanId.GenericAggressive, BattlePlanId.WideFlankApproach,
    };

    static FeasibilityLedger LookLedger(FeasField f, BattlePlanId selected, bool mirrored)
    {
        f.Legs = StraightLegs(f, LookPlans);
        var ledger = new FeasibilityLedger();
        ManeuverFeasibility.Evaluate(f.Build(LookPlans), ledger);
        ledger.CaptureSelection(selected, mirrored);
        ManeuverFeasibility.Evaluate(f.Build(LookPlans), ledger);
        return ledger;
    }

    static int LinkAtPoint(RiverRegions regions, float x, float z)
    {
        for (int link = 0; link < regions.LinkCount; link++)
        {
            regions.LinkCrossing(link, out float lx, out float lz, out _, out _);
            if (Math.Abs(lx - x) < 1f && Math.Abs(lz - z) < 1f) return link;
        }
        return -1;
    }

    // The links on any member's route in the candidate's result.
    static List<int> RouteLinks(FeasibilityLedger ledger, BattlePlanId plan)
    {
        var links = new List<int>();
        CandidateResult r = Result(ledger, plan);
        foreach (MemberEstimate m in ledger.Members(in r).ToArray())
            foreach (RouteCrossing c in ledger.Crossings(in r, in m).ToArray()) links.Add(c.Link);
        return links;
    }

    static List<int> Looks(FeasibilityLedger ledger, RiverRegions regions)
    {
        var xs = new float[8];
        var zs = new float[8];
        int n = ledger.LookRequests(xs, zs);
        var links = new List<int>();
        for (int i = 0; i < n; i++) links.Add(LinkAtPoint(regions, xs[i], zs[i]));
        return links;
    }

    static void LookPriority()
    {
        // The selected route's crossing is the first request, whichever plan is selected: DoubleEnvelopment crosses at 0 and 1 and
        // ManeuverAndFix at 1 alone, so selecting the one or the other reorders the same two requests.
        FeasField f = LookField();
        FeasibilityLedger envelop = LookLedger(f, BattlePlanId.DoubleEnvelopment, false);
        List<int> envelopRoute = RouteLinks(envelop, BattlePlanId.DoubleEnvelopment);
        AssertEqual(2, envelopRoute.Count, "DoubleEnvelopment crosses twice");
        AssertEqual(1, RouteLinks(envelop, BattlePlanId.ManeuverAndFix).Count, "ManeuverAndFix once");
        List<int> first = Looks(envelop, f.Regions);
        AssertEqual(2, first.Count, "two requests");
        AssertEqual(envelopRoute[0], first[0], "the selected route's first crossing leads");
        AssertEqual(envelopRoute[1], first[1], "then its second");

        FeasField g = LookField();
        FeasibilityLedger fix = LookLedger(g, BattlePlanId.ManeuverAndFix, false);
        int fixLink = RouteLinks(fix, BattlePlanId.ManeuverAndFix)[0];
        List<int> second = Looks(fix, g.Regions);
        AssertEqual(2, second.Count, "two requests");
        AssertEqual(fixLink, second[0], "the other selection: its own crossing leads");
        AssertTrue(second[1] != fixLink && RouteLinks(fix, BattlePlanId.DoubleEnvelopment).Contains(second[1]),
            "and the second comes from a better-scored candidate's route");
        AssertTrue(fixLink != envelopRoute[0], "(the two selections lead with different crossings)");

        // At most two, however many are unknown and however much room the caller has.
        AssertEqual(3, g.Crossings.CountBy(CrossingState.Unknown), "three crossings are unknown");
        AssertEqual(ManeuverFeasibility.MaxLookRequests, Looks(fix, g.Regions).Count, "two of three are asked for");
        AssertEqual(1, fix.LookRequests(new float[1], new float[1]), "and never more than the caller's room");
        AssertEqual(0, fix.LookRequests(new float[0], new float[0]), "or none with no room");

        // The candidates past the best ConfirmCandidates add nothing: the selected plan alone, with the first three scored ones its own route.
        FeasField h = LookField();
        h.Legs = StraightLegs(h, LookPlans);
        var ranked = new FeasibilityLedger();
        BattlePlanId[] order = { BattlePlanId.GenericAggressive, BattlePlanId.FrontalShockAssault, BattlePlanId.ManeuverAndFix, BattlePlanId.DoubleEnvelopment };
        ManeuverFeasibility.Evaluate(h.Build(order), ranked);
        ranked.CaptureSelection(BattlePlanId.GenericAggressive, false);
        ManeuverFeasibility.Evaluate(h.Build(order), ranked);
        AssertEqual(0, RouteLinks(ranked, BattlePlanId.GenericAggressive).Count, "the selected plan crosses nowhere");
        List<int> ranks = Looks(ranked, h.Regions);
        AssertEqual(1, ranks.Count, "the fourth candidate's second crossing is beyond the confirm candidates: one request from the second and third");
        AssertEqual(RouteLinks(ranked, BattlePlanId.FrontalShockAssault)[0], ranks[0], "from the best-scored candidate's route");

        // A Held crossing is not asked about: an enemy brigade facing the middle ford holds it.
        FeasField held = LookField();
        held.AddEnemy(450f, 300f, 1500f, heading: 180f, fireRange: 100f);
        FeasibilityLedger heldLedger = LookLedger(held, BattlePlanId.DoubleEnvelopment, false);
        int ford = LinkAtPoint(held.Regions, FeasField.X(450f), FeasField.Z(250f));
        AssertTrue(ford >= 0, "(the middle ford's link)");
        AssertEqual(CrossingState.Held, held.Crossings.StateOf(ford), "the middle ford is held");
        AssertFalse(Looks(heldLedger, held.Regions).Contains(ford), "so it is not asked about");

        // The request ends when the crossing is seen: the same ledger, every crossing perceived now.
        FeasField seen = LookField();
        FeasibilityLedger seenLedger = LookLedger(seen, BattlePlanId.ManeuverAndFix, false);
        AssertTrue(Looks(seenLedger, seen.Regions).Count > 0, "unknown crossings are asked about");
        seen.Perceived = (x, z) => true;
        seen.Build(LookPlans);
        AssertEqual(0, seen.Crossings.CountBy(CrossingState.Unknown), "every crossing is now Clear");
        AssertEqual(0, Looks(seenLedger, seen.Regions).Count, "so nothing is asked about");

        // One crossing seen, the rest not: only the rest remain.
        FeasField partly = LookField();
        FeasibilityLedger partLedger = LookLedger(partly, BattlePlanId.DoubleEnvelopment, false);
        List<int> before = Looks(partLedger, partly.Regions);
        AssertEqual(2, before.Count, "two requests");
        partly.Regions.LinkCrossing(before[0], out float seenX, out float seenZ, out _, out _);
        partly.Perceived = (x, z) => Math.Abs(x - seenX) < 50f && Math.Abs(z - seenZ) < 50f;
        partly.Build(LookPlans);
        AssertEqual(CrossingState.Clear, partly.Crossings.StateOf(before[0]), "the crossing just looked at is Clear");
        List<int> after = Looks(partLedger, partly.Regions);
        AssertFalse(after.Contains(before[0]), "and no longer asked about");
        AssertTrue(after.Contains(before[1]), "while the other still is");

        // Before any evaluation, and with no regions, nothing is asked.
        AssertEqual(0, new FeasibilityLedger().LookRequests(new float[2], new float[2]), "no evaluation, no request");
    }

    // ---- FEAS-SCN-5 -----------------------------------------------------------------------------------------------

    static readonly BattlePlanId[] Scn5Plans =
    {
        BattlePlanId.PreparedDefense, BattlePlanId.DelayingDefense, BattlePlanId.GenericCautious, BattlePlanId.MeetingEngagement, BattlePlanId.GenericMethodical,
    };

    // Spec § 6 item 5. Own: three corps of 11,000 in line at lateral -300, 0 and +300 on depth 200, a deployed reserve of 8,000 at (0, -100), the army
    // headquarters at (0, -500), the middle corps' lead regiment at (0, 250) facing the bridge with fire range 150. The river is open on depths
    // [300, 320): bridge B at (0, 310) covered by that regiment, ford F at (+1,000, 310) uncovered. Enemy: 22,000 at (0, 700), 5,000 at (+-1,000, 420).
    static FeasField Scenario5(bool deployedReserve = true, float rightColumnDepth = 420f, Func<float, float, bool> perceived = null)
    {
        var f = new FeasField(BandWithCrossings(300f, 320f, (0f, 310f, Bridge), (1000f, 310f, Ford)));
        f.Stance = BattleStance.Defender;
        f.Tree.Place(f.ArmyIndex, FeasField.X(0f), FeasField.Z(-500f));
        f.AddCorps(-300f, 200f, 11000f);
        int middle = f.AddCorps(0f, 200f, 11000f);
        f.AddCorps(300f, 200f, 11000f);
        if (deployedReserve) f.AddCorps(0f, -100f, 8000f, deployedReserve: true);
        f.Tree.Place(middle + 2, FeasField.X(0f), FeasField.Z(250f), 150f);
        f.AddEnemy(0f, 700f, 22000f);
        f.AddEnemy(-1000f, 420f, 5000f);
        f.AddEnemy(1000f, rightColumnDepth, 5000f);
        if (perceived != null) f.Perceived = perceived;
        return f;
    }

    static FeasibilityLedger Scn5Ledger(FeasField f, BattlePlanId selected)
    {
        f.Legs = StraightLegs(f, Scn5Plans);
        var ledger = new FeasibilityLedger();
        ManeuverFeasibility.Evaluate(f.Build(Scn5Plans), ledger);
        ledger.CaptureSelection(selected, false);
        ManeuverFeasibility.Evaluate(f.Build(Scn5Plans), ledger);
        return ledger;
    }

    static int Scn5FordLink(FeasField f) => LinkAtPoint(f.Regions, FeasField.X(1000f), FeasField.Z(310f)) >= 0
        ? LinkAtPoint(f.Regions, FeasField.X(1000f), FeasField.Z(310f)) : NearestLink(f.Regions, FeasField.X(1000f), FeasField.Z(310f));

    static int NearestLink(RiverRegions regions, float x, float z)
    {
        int best = -1;
        float bestD = float.MaxValue;
        for (int link = 0; link < regions.LinkCount; link++)
        {
            regions.LinkCrossing(link, out float lx, out float lz, out _, out _);
            float d = (lx - x) * (lx - x) + (lz - z) * (lz - z);
            if (d < bestD) { bestD = d; best = link; }
        }
        return best;
    }

    static float TurnOf(FeasibilityLedger ledger, BattlePlanId plan)
    {
        CandidateResult r = Result(ledger, plan);
        return ledger.Bodies(in r)[0].Turn;
    }

    // The allocation the army makes: the play's rect kinds per child (Unknown for a deployed reserve, which no rect claims), the ledger's threats
    // and per-child times in the context, BC13's staggered release off.
    static IReadOnlyList<DirectChildIntent> Scn5Allocate(FeasibilityLedger ledger, PlayRectKind[] kinds, ReserveReleaseState state)
    {
        int children = kinds.Length;
        int threats = ledger.ThreatCount;
        var list = new CrossingThreat[Math.Max(threats, 1)];
        var shifts = new float[Math.Max(threats, 1) * children];
        for (int t = 0; t < threats; t++)
        {
            list[t] = Threat(ledger, t, out float[] byChild);
            for (int c = 0; c < children; c++) shifts[t * children + c] = byChild[c];
        }
        var evidence = new DirectChildEvidence[children];
        var zones = new List<CorpsAssignment>();
        for (int c = 0; c < children; c++)
        {
            bool deployed = kinds[c] == PlayRectKind.Unknown;
            evidence[c] = new DirectChildEvidence(3, 0, false, Math.Min(c, 2), 0, 0.5f, deployedReserve: deployed);
            if (!deployed) zones.Add(new CorpsAssignment("c" + c, kinds[c], c, 0f, 0f, 0f));
        }
        var personality = new PersonalityVector(0.2f, 0f, 0f, 0f, 0f);
        var context = new ReserveReleaseContext(0.5f, false, state, null, 10.0, 0.5f, 0.5f, 0.5f, false, list, threats, shifts, children);
        // A Defender's plan has no main effort of its own: the sector names no child, and no child is confident enough to be picked in its place.
        var plan = new TacticalBattlePlan(BattlePlanId.DelayingDefense, BattlePhase.MainEffort, mainEffortSector: 7, fixingSectors: new int[0],
            screeningSectors: new int[0], reserveCommitTriggerOdds: 50f, ageSeconds: 0f, jitterSeed: 0);
        return DirectChildAllocator.AllocateWithZones(plan, personality, ResChildren(children), evidence, null, zones, context);
    }

    static readonly PlayRectKind[] DelayingKinds = { PlayRectKind.CenterHold, PlayRectKind.ForwardScreen, PlayRectKind.ReserveLine, PlayRectKind.Unknown };
    static readonly PlayRectKind[] PreparedKinds = { PlayRectKind.LeftFlank, PlayRectKind.CenterHold, PlayRectKind.RightFlank, PlayRectKind.Unknown };

    static void FeasScn5()
    {
        // The turning exposure: B is covered by the lead regiment and counts for neither candidate; F, on the right flank and uncovered, is the one crossing.
        FeasField f = Scenario5();
        FeasibilityLedger ledger = Scn5Ledger(f, BattlePlanId.DelayingDefense);
        AssertEqual(1, ledger.ThreatCount, "B is covered and counts for neither: F is the one counted crossing");
        CrossingThreat ford = Threat(ledger, 0, out float[] shifts);
        AssertNear(FeasField.X(1000f), ford.X, 70f, "at F");
        AssertFalse(ford.Left, "on the right");
        AssertNear(0.30f, ford.EnemyAtCrossing, 0.05f, "the right column is the earliest body");
        AssertNear(2.32f, ford.EnemyAtFlank, 0.1f, "DelayingDefense: 2.02 h on to its flank end CenterHold");
        AssertNear(2.36f, ford.CandidateOwnShift, 0.1f, "the deployed reserve is its fastest source");
        AssertNear(0.04f, TurnOf(ledger, BattlePlanId.DelayingDefense), 0.05f, "DelayingDefense turn");
        AssertNear(0.72f, TurnOf(ledger, BattlePlanId.PreparedDefense), 0.05f, "PreparedDefense turn: its wide line can be turned before the reserve gets there");
        AssertNear(0.43f, Result(ledger, BattlePlanId.PreparedDefense).Penalty, 0.05f, "PreparedDefense penalty");
        AssertNear(1.75f, shifts[2], 0.05f, "the right corps reaches F in 1.75 h");
        AssertNear(2.36f, shifts[3], 0.1f, "the deployed reserve in 2.36 h");

        // The look. The right column stands 110 units from F, facing the river within 1.5 x its fire range, so F reads Held by the crossing-state rule
        // (the spec's "Clear" for this fixture is Held in the shipped rule; the Defender's numbers do not read the state): a known crossing is not
        // asked about, perceived or not. With the column 210 away, F unperceived is Unknown and asked about; perceived it is Clear and the request ends.
        var xs = new float[ManeuverFeasibility.MaxLookRequests];
        var zs = new float[ManeuverFeasibility.MaxLookRequests];
        Func<float, float, bool> awayFromF = (x, z) => Math.Abs(x - FeasField.X(1000f)) > 100f;
        FeasField watched = Scenario5(perceived: awayFromF);
        FeasibilityLedger watchedLedger = Scn5Ledger(watched, BattlePlanId.DelayingDefense);
        AssertEqual(CrossingState.Held, watched.Crossings.StateOf(Scn5FordLink(watched)), "F is held by the column beside it");
        AssertEqual(0, watchedLedger.LookRequests(xs, zs), "a held crossing is not asked about");
        AssertEqual(0, ledger.LookRequests(xs, zs), "nor a seen one");

        FeasField unseen = Scenario5(rightColumnDepth: 520f, perceived: awayFromF);
        FeasibilityLedger unseenLedger = Scn5Ledger(unseen, BattlePlanId.DelayingDefense);
        AssertEqual(CrossingState.Unknown, unseen.Crossings.StateOf(Scn5FordLink(unseen)), "with the column 210 away F is Unknown");
        AssertEqual(1, unseenLedger.ThreatCount, "and is still the counted crossing");
        AssertEqual(1, unseenLedger.LookRequests(xs, zs), "a look request for F");
        AssertNear(FeasField.X(1000f), xs[0], 70f, "at F");
        AssertNear(FeasField.Z(310f), zs[0], 70f, "at the river");
        unseen.Perceived = (x, z) => true;
        unseen.Build(Scn5Plans);
        AssertEqual(CrossingState.Clear, unseen.Crossings.StateOf(Scn5FordLink(unseen)), "seen, F is Clear");
        AssertEqual(0, unseenLedger.LookRequests(xs, zs), "and the request ends");

        // The release: DelayingDefense selected, the right corps (ReserveLine, 1.75 h) beats the deployed reserve (2.36 h) and guards F from its own bank.
        var state = new ReserveReleaseState();
        IReadOnlyList<DirectChildIntent> intents = Scn5Allocate(ledger, DelayingKinds, state);
        AssertEqual(DirectChildRole.RefuseRight, intents[2].Role, "the right corps is released to the right flank");
        AssertTrue(intents[2].HasGuardPoint, "with a guard point");
        AssertNear(FeasField.X(1000f), intents[2].Guard.X, 70f, "at F's lateral");
        AssertNear(FeasField.Z(210f), intents[2].Guard.Z, 70f, "100 behind it");
        AssertNear(0f, intents[2].Guard.Heading, 5f, "facing F");
        AssertEqual(DirectChildRole.Reserve, intents[3].Role, "the deployed reserve stays");
        AssertEqual(1, state.BoundCount, "one binding");

        // PreparedDefense has no ReserveLine child: its only Reserve child is the deployed reserve, which is eligible (BC10's reserve is a reserve under
        // every candidate) and is the one released; no line corps leaves its place.
        FeasibilityLedger prepared = Scn5Ledger(Scenario5(), BattlePlanId.PreparedDefense);
        var preparedState = new ReserveReleaseState();
        IReadOnlyList<DirectChildIntent> preparedIntents = Scn5Allocate(prepared, PreparedKinds, preparedState);
        for (int c = 0; c < 3; c++) AssertFalse(preparedIntents[c].HasGuardPoint, "no line corps is released, child " + c);
        AssertEqual(DirectChildRole.RefuseRight, preparedIntents[3].Role, "the deployed reserve guards F");
        AssertTrue(preparedIntents[3].HasGuardPoint, "with a guard point");

        // Without the deployed reserve PreparedDefense falls back to its RightFlank anchor: the line is no longer turnable.
        FeasField bare = Scenario5(deployedReserve: false);
        FeasibilityLedger bareLedger = Scn5Ledger(bare, BattlePlanId.PreparedDefense);
        AssertNear(0.03f, TurnOf(bareLedger, BattlePlanId.PreparedDefense), 0.05f, "PreparedDefense without the deployed reserve");
        var bareState = new ReserveReleaseState();
        IReadOnlyList<DirectChildIntent> bareIntents = Scn5Allocate(bareLedger, new[] { PlayRectKind.LeftFlank, PlayRectKind.CenterHold, PlayRectKind.RightFlank }, bareState);
        for (int c = 0; c < bareIntents.Count; c++) AssertFalse(bareIntents[c].HasGuardPoint, "no crossing release is possible, child " + c);

        // The right column far back: the earliest body at F is the main body, 2.61 h away, past the right corps' 1.75 h plus the lead: nothing is released.
        FeasField late = Scenario5(rightColumnDepth: 1500f);
        FeasibilityLedger lateLedger = Scn5Ledger(late, BattlePlanId.DelayingDefense);
        CrossingThreat lateFord = Threat(lateLedger, 0, out _);
        AssertNear(2.61f, lateFord.EnemyAtCrossing, 0.1f, "the main body is the earliest at F");
        var lateState = new ReserveReleaseState();
        IReadOnlyList<DirectChildIntent> lateIntents = Scn5Allocate(lateLedger, DelayingKinds, lateState);
        for (int c = 0; c < lateIntents.Count; c++) AssertFalse(lateIntents[c].HasGuardPoint, "no release, child " + c);
        AssertEqual(0, lateState.BoundCount, "no binding");
    }
}
