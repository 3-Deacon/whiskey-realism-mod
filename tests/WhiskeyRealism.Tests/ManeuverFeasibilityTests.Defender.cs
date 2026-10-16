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
using static TestFixtures;
using static BattleMovementHonestyTests;
using static ManeuverFeasibilityFixtures;
using static CommanderOpportunityTests;

// BC14 task 13: the Defender (spec § 7, § 13.1 DEF-*, ENEMY-BODIES, SHIFT-COVERED, RELEASE-*, FEAS-PLAYER). The evaluation half runs on a river
// across the front with a bridge in front of the line's middle (frontal, never counted) and a ford on its left flank; the allocator half is pure.
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] DefenderTests() => new (string, Action)[]
    {
        ("bc14 DEF-FLANK only a crossing in the outer quarter of the line or beyond, inside one frontage behind and the enemy anchor, counts", DefFlank),
        ("bc14 DEF-ARRIVALS the threat list carries the earliest arrival, the arrival at the flank end, the reserve sources' time and the per-child times", DefArrivals),
        ("bc14 DEF-PENALTY a Defender candidate's penalty is 0.6 x turn alone", DefPenalty),
        ("bc14 ENEMY-BODIES the Defender run builds its own bodies by single-link clustering and the earliest triggers", EnemyBodies),
        ("bc14 SHIFT-COVERED-DEF the enemy's shift through a crossing an own regiment covers costs an hour; through any other nothing; a bound guard does not cover it", ShiftCoveredDefender),
        ("bc14 FEAS-PLAYER player-owned strength is not counted, a wholly player-owned child is not offered and has no time to a crossing", FeasPlayer),
        ("bc14 RELEASE-CROSSING one release per crossing, the soonest eligible reserve, only when the enemy gets there first", ReleaseCrossing),
        ("bc14 RELEASE-ORDER the crossing release takes what the crisis and the stagger leave", ReleaseOrder),
        ("bc14 RELEASE-ELIGIBLE an exploit asset or a child with player-owned strength is never released to a crossing", ReleaseEligible),
        ("bc14 RELEASE-ACROSS an enemy already across is contact, not a crossing threat; a distant ford does not suppress this one", ReleaseAcross),
        ("bc14 RELEASE-TARGET the released child gets the side's refuse role, the crossing's band and a guard point behind the crossing facing it", ReleaseTarget),
        ("bc14 RELEASE-LATCH a guard stays while the enemy's arrival stays inside its hold bound and never alternates with release", ReleaseLatch),
        ("bc14 RELEASE-FLOOR a crossing release leaves the held reserves at BC13's floor and the stagger's own memory untouched", ReleaseFloor),
        ("bc14 RELEASE-STATE-SCOPE one reserve state per alliance, cleared with the battle, a plan change, a new day and a stand-down", ReleaseStateScope),
        ("bc14 RELEASE-SHARED two armies of one alliance in one battle share their release state; the next battle starts clean", ReleaseShared),
        ("bc14 DEFENDER-WIRED the context, the guard point and the telemetry reason reach the allocation", DefenderWired),
    };

    // ---- the evaluation fixtures ----------------------------------------------------------------------------

    // Three corps at lateral -300, 0 and 300 on the own bank (depth 0), a river across depths 240-260 with a bridge in front of the middle
    // and a ford at lateral -900, three enemy clusters 400 apart beyond it: Scenario 4's ground, with the side Defender.
    static FeasField DefenderField(Action<FeasField> more = null)
    {
        var f = new FeasField(BandWithCrossings(240f, 260f, (0f, 250f, Bridge), (-900f, 250f, Ford)));
        f.Stance = BattleStance.Defender;
        f.AddCorps(-300f, 0f, 12000f);
        f.AddCorps(0f, 0f, 11000f);
        f.AddCorps(300f, 0f, 12000f);
        f.AddEnemy(-400f, 556f, 11000f);
        f.AddEnemy(0f, 556f, 11500f);
        f.AddEnemy(400f, 556f, 11000f);
        f.EnemyHalfExtent = 450f;
        more?.Invoke(f);
        return f;
    }

    static readonly BattlePlanId[] DefensivePlans = { BattlePlanId.PreparedDefense, BattlePlanId.GenericMethodical, BattlePlanId.MeetingEngagement };

    // The Defender's evaluation with PreparedDefense selected, so the ledger collects its crossing threats.
    static FeasibilityLedger DefenderLedger(FeasField f, BattlePlanId selected = BattlePlanId.PreparedDefense)
    {
        f.Legs = StraightLegs(f, DefensivePlans);
        var ledger = new FeasibilityLedger();
        ManeuverFeasibility.Evaluate(f.Build(DefensivePlans), ledger);
        ledger.CaptureSelection(selected, false);
        ManeuverFeasibility.Evaluate(f.Build(DefensivePlans), ledger);
        return ledger;
    }

    static CrossingThreat Threat(FeasibilityLedger ledger, int index, out float[] byChild)
    {
        byChild = new float[ManeuverFeasibility.MaxChildren];
        AssertTrue(ledger.TryThreat(index, out CrossingThreat threat, byChild, out int children), "threat " + index + " exists");
        Array.Resize(ref byChild, children);
        return threat;
    }

    static void DefFlank()
    {
        // The own line at lateral +-300 (600 wide), the enemy anchor 600 ahead, one frontage of 900.
        bool Counts(float lateral, float depth) => ManeuverFeasibility.CountsAsFlankCrossing(lateral, depth, -300f, 300f, 900f, 600f);
        AssertFalse(Counts(0f, 250f), "the middle of the front is faced already");
        AssertFalse(Counts(100f, 250f), "+100: inside the middle half");
        AssertFalse(Counts(-100f, 250f), "-100: inside the middle half");
        AssertFalse(Counts(-150f, 250f), "-150 is the quarter's edge: the quarter is exclusive");
        AssertTrue(Counts(-200f, 250f), "-200: the outer quarter");
        AssertTrue(Counts(200f, 250f), "+200: the outer quarter");
        AssertTrue(Counts(-700f, 250f), "-700: beyond the line");
        AssertTrue(Counts(700f, 250f), "+700: beyond the line");
        AssertFalse(Counts(-700f, -2000f), "2,000 behind the line is not on the way to a flank");
        AssertFalse(Counts(-700f, -901f), "beyond one frontage behind");
        AssertTrue(Counts(-700f, -899f), "inside one frontage behind");
        AssertFalse(Counts(-700f, 601f), "beyond the enemy anchor");

        // On the ground: the bridge in front of the middle is not a threat, the ford on the flank is.
        FeasField f = DefenderField();
        FeasibilityLedger ledger = DefenderLedger(f);
        AssertEqual(1, ledger.ThreatCount, "one counted crossing");
        CrossingThreat ford = Threat(ledger, 0, out _);
        AssertNear(FeasField.X(-900f), ford.X, 70f, "the ford at lateral -900");
        AssertTrue(ford.Left, "on the left");
    }

    static void DefArrivals()
    {
        FeasField f = DefenderField(g => g.AddCorps(-800f, 100f, 6000f, deployedReserve: true));
        FeasibilityLedger ledger = DefenderLedger(f);
        AssertEqual(1, ledger.ThreatCount, "the ford");
        CrossingThreat threat = Threat(ledger, 0, out float[] byChild);
        AssertFalse(threat.EnemyAcross, "no enemy is on the own bank");
        AssertTrue(threat.EnemyAtCrossing > 0f && !float.IsInfinity(threat.EnemyAtCrossing), "the earliest arrival at the ford is finite, " + threat.EnemyAtCrossing);
        AssertTrue(threat.EnemyAtFlank > threat.EnemyAtCrossing, "and the flank end is further on: " + threat.EnemyAtFlank);

        // The earliest body is the nearest to the ford (lateral -400), not the mass.
        DefenderPicture d = ledger.Job.Defender;
        AssertEqual(3, d.BodyCount, "three bodies, 400 apart");
        float nearest = float.PositiveInfinity;
        for (int b = 0; b < d.BodyCount; b++) nearest = Math.Min(nearest, d.BodyArrival[b * DefenderPicture.Links + threat.Link]);
        AssertTrue(nearest > 0f, "the bodies have arrivals");

        // The deployed reserve at (-800, 100) is beside the ford: it is the candidate's reserve source and far quicker than any line corps.
        AssertTrue(threat.CandidateOwnShift < 1.0f, "the deployed reserve's courier and march, " + threat.CandidateOwnShift);
        int reserve = f.ChildIds.Count - 1;
        AssertEqual(4, byChild.Length, "one time per direct child");
        for (int c = 0; c < byChild.Length; c++) AssertTrue(!float.IsNaN(byChild[c]) && byChild[c] > 0f, "child " + c + " reaches the ford");
        for (int c = 0; c < reserve; c++) AssertTrue(byChild[reserve] < byChild[c], "the reserve beside it is the quickest, child " + c);
        AssertNear(threat.CandidateOwnShift, byChild[reserve], 0.05f, "and its time is the candidate's own shift");
        AssertNear(1f, threat.GuardHeading >= 0f ? 1f : 0f, 1e-6f, "the guard faces the crossing");

        // The turn reads enemyAtFlank: with the reserve far from the ford the turn is larger.
        FeasField far = DefenderField(g => g.AddCorps(800f, -300f, 6000f, deployedReserve: true));
        FeasibilityLedger farLedger = DefenderLedger(far);
        CrossingThreat farThreat = Threat(farLedger, 0, out _);
        AssertTrue(farThreat.CandidateOwnShift > threat.CandidateOwnShift + 0.3f, "a reserve on the other wing answers later: " + farThreat.CandidateOwnShift);
        CandidateResult near = Result(ledger, BattlePlanId.PreparedDefense);
        CandidateResult awayResult = Result(farLedger, BattlePlanId.PreparedDefense);
        AssertTrue(awayResult.Penalty >= near.Penalty, "so the line is never safer: " + near.Penalty + " against " + awayResult.Penalty);

        // An Attacker's evaluation writes no threats.
        FeasField attacker = DefenderField();
        attacker.Stance = BattleStance.Attacker;
        FeasibilityLedger attackLedger = new FeasibilityLedger();
        attacker.Legs = StraightLegs(attacker, BattlePlanId.ManeuverAndFix);
        ManeuverFeasibility.Evaluate(attacker.Build(BattlePlanId.ManeuverAndFix), attackLedger);
        attackLedger.CaptureSelection(BattlePlanId.ManeuverAndFix, false);
        ManeuverFeasibility.Evaluate(attacker.Build(BattlePlanId.ManeuverAndFix), attackLedger);
        AssertEqual(0, attackLedger.ThreatCount, "an Attacker has no crossing threats");
    }

    static void DefPenalty()
    {
        // Dark already: an Attacker's body would carry daylight 1. The Defender's penalty is the turn term alone.
        foreach (bool reserveNear in new[] { true, false })
        {
            FeasField f = DefenderField(g =>
            {
                g.DaylightHours = 2.0f;
                g.AddCorps(reserveNear ? -800f : 800f, reserveNear ? 100f : -300f, 6000f, deployedReserve: true);
            });
            FeasibilityLedger ledger = DefenderLedger(f);
            CandidateResult r = Result(ledger, BattlePlanId.PreparedDefense);
            CrossingThreat threat = Threat(ledger, 0, out _);
            float turn = Math.Max(0f, Math.Min(1f, (threat.CandidateOwnShift - threat.EnemyAtFlank) / ManeuverFeasibility.LateHours));
            AssertNear(0.6f * turn, r.Penalty, 1e-4f, "1 - (1 - 0.6 x turn): turn " + turn);
            if (!reserveNear) AssertTrue(turn > 0f, "control: a reserve on the other wing leaves the flank turnable, own " + threat.CandidateOwnShift + " flank " + threat.EnemyAtFlank);
            foreach (BodyEstimate b in ledger.Bodies(in r).ToArray())
            {
                AssertNear(0f, b.Daylight, 1e-6f, "no daylight term for a Defender");
                AssertNear(0f, b.Detail, 1e-6f, "no detail term");
                AssertNear(0f, b.Approach, 1e-6f, "no approach term");
                AssertNear(0f, b.Exposure, 1e-6f, "no exposure term");
                AssertNear(0f, b.Contested, 1e-6f, "contested is replaced by the turn");
                AssertNear(turn, b.Turn, 1e-5f, "every body carries the line's turn");
            }
        }
    }

    static void EnemyBodies()
    {
        FeasField f = DefenderField();
        FeasibilityLedger ledger = DefenderLedger(f);
        DefenderPicture d = ledger.Job.Defender;
        AssertEqual(3, d.BodyCount, "three clusters 400 units apart");
        AssertTrue(d.Perceived, "perceived");
        CrossingThreat ford = Threat(ledger, 0, out _);
        // The ford is on the left: the body at lateral -400 gets there first, and the threat is its arrival.
        float best = float.PositiveInfinity;
        int bestBody = -1;
        for (int b = 0; b < d.BodyCount; b++)
        {
            float arrival = d.BodyArrival[b * DefenderPicture.Links + ford.Link];
            if (arrival < best) { best = arrival; bestBody = b; }
        }
        AssertTrue(f.Enemies[0].X - d.BodyX[bestBody] < 1f && f.Enemies[0].X - d.BodyX[bestBody] > -1f, "the left body arrives first");
        for (int b = 0; b < d.BodyCount; b++)
            if (b != bestBody) AssertTrue(d.BodyArrival[b * DefenderPicture.Links + ford.Link] > best + 0.1f, "the other bodies are later");
        AssertTrue(ford.EnemyAtCrossing >= best && ford.EnemyAtCrossing < best + 1.0f, "so the threat is the left body's arrival plus its courier: " + ford.EnemyAtCrossing + " from " + best);

        // Two clusters (the middle one gone) give two bodies, the nearer one triggers.
        FeasField two = DefenderField();
        two.Enemies.RemoveAt(1);
        FeasibilityLedger twoLedger = DefenderLedger(two);
        AssertEqual(2, twoLedger.Job.Defender.BodyCount, "two bodies");

        // More than MaxEnemyBodies: the weakest merge into the nearest; the weight is kept.
        FeasField many = DefenderField();
        many.Enemies.Clear();
        float total = 0f;
        for (int i = 0; i < 20; i++)
        {
            float lateral = -1000f + (i % 10) * 220f, depth = 556f + (i / 10) * 220f;
            many.AddEnemy(lateral, depth, 500f + i * 10f);
            total += 500f + i * 10f;
        }
        FeasibilityLedger manyLedger = DefenderLedger(many);
        DefenderPicture md = manyLedger.Job.Defender;
        AssertEqual(ManeuverFeasibility.MaxEnemyBodies, md.BodyCount, "capped at 16 bodies");
        float kept = 0f;
        for (int b = 0; b < md.BodyCount; b++) kept += md.BodyWeight[b];
        AssertNear(total, kept, 1f, "the merged weight is the whole");

        // No samples: one body at the assumed point; the penalty uses it but no threat leaves for a guard.
        FeasField none = DefenderField();
        none.Enemies.Clear();
        none.HasAssumedEnemy = true;
        none.AssumedEnemyLateral = -400f;
        none.AssumedEnemyDepth = 556f;
        FeasibilityLedger noneLedger = DefenderLedger(none);
        DefenderPicture nd = noneLedger.Job.Defender;
        AssertEqual(1, nd.BodyCount, "one body at the assumed point");
        AssertFalse(nd.Perceived, "not perceived");
        AssertEqual(0, noneLedger.ThreatCount, "and no crossing threat for a guard to answer");
    }

    // The enemy's route hours across the ford at x = -600 with an optional own corps standing at the ford. A bound guard (the child the
    // previous allocation sent to guard a crossing) stays out of the cover test: its regiments stand there because of the threat.
    static float EnemyFordHours(Action<FeasField> own, BattleStance stance, bool coverIsBoundGuard = false)
    {
        var f = new FeasField(BandWithCrossings(240f, 260f, (-600f, 250f, Ford)));
        f.Stance = stance;
        f.AddCorps(0f, 0f, 12000f);
        f.AddEnemy(-600f, 556f, 11000f);
        own?.Invoke(f);
        f.Legs = StraightLegs(f, BattlePlanId.PreparedDefense, BattlePlanId.ManeuverAndFix);
        var ledger = new FeasibilityLedger();
        FeasibilityInput input = f.Build(BattlePlanId.PreparedDefense);
        if (coverIsBoundGuard)
        {
            input.BoundGuardChildIds = new[] { f.ChildIds[f.ChildIds.Count - 1] };
            input.BoundGuardCount = 1;
        }
        ManeuverFeasibility.Evaluate(input, ledger);
        CrossingGraphView graph = ledger.Job.Graph;
        RouteResult r = graph.Route(FeasField.X(-600f), FeasField.Z(450f), FeasField.X(-600f), FeasField.Z(0f), 1f, 0f, true);
        AssertTrue(r.Found && !r.SameRegion, "the enemy's route crosses the ford");
        return r.Hours;
    }

    static void ShiftCoveredDefender()
    {
        // The enemy crosses at a ford; an own regiment facing it within 1.5 x its fire range covers it.
        float uncovered = EnemyFordHours(null, BattleStance.Defender);
        float covered = EnemyFordHours(f => f.AddCorps(-600f, 100f, 6000f, fireRange: 200f), BattleStance.Defender);
        AssertNear(ManeuverFeasibility.HeldCrossingHours, covered - uncovered, 0.02f, "an own-covered crossing costs the enemy an hour");
        float attacker = EnemyFordHours(f => f.AddCorps(-600f, 100f, 6000f, fireRange: 200f), BattleStance.Attacker);
        AssertNear(covered, attacker, 1e-4f, "the same rule in the attacker's run");
        // A crossing the enemy itself holds (a brigade standing on it) is Held to the side but costs the enemy nothing extra.
        float held = EnemyFordHours(f => f.AddEnemy(-600f, 250f, 1500f, heading: 180f, fireRange: 100f), BattleStance.Defender);
        AssertNear(uncovered, held, 0.02f, "a crossing the enemy holds is no cost to it");
        // RELEASE-LATCH: the cover is the guard the last allocation bound to the crossing: its regiments are left out of the cover test, so the
        // crossing it guards does not read covered by the guard itself (the latch would otherwise flip the threat away and back).
        float guard = EnemyFordHours(f => f.AddCorps(-600f, 100f, 6000f, fireRange: 200f), BattleStance.Defender, coverIsBoundGuard: true);
        AssertNear(uncovered, guard, 0.02f, "a bound guard's regiments do not count in the crossing's cover test");
    }

    static void FeasPlayer()
    {
        FeasField f = DefenderField(g =>
        {
            g.AddCorps(-800f, 100f, 6000f, deployedReserve: true);
            g.AddCorps(-700f, 150f, 5000f, regimentFlags: UnitFlags.PlayerOwned);     // wholly the player's
        });
        FeasibilityLedger ledger = DefenderLedger(f);
        string playerChild = f.ChildIds[f.ChildIds.Count - 1];
        CandidateResult r = Result(ledger, BattlePlanId.PreparedDefense);
        foreach (MemberEstimate m in ledger.Members(in r).ToArray()) AssertTrue(m.ChildId != playerChild, "a wholly player-owned child is not offered to the layout");
        AssertFalse(ledger.TryPlannedRelease(playerChild, out _, out _, out _, out _, out _, out _), "and has no release");
        CrossingThreat threat = Threat(ledger, 0, out float[] byChild);
        AssertTrue(float.IsNaN(byChild[byChild.Length - 1]), "its time to the crossing is not offered");
        AssertFalse(float.IsNaN(byChild[byChild.Length - 2]), "the other reserve's is");
        AssertTrue(threat.CandidateOwnShift > 0f, "the player's child is not the candidate's reserve source");
        AssertEqual(0, ledger.Job.Children[ledger.Job.SlotOfChild(playerChild)].Strength > 0f ? 1 : 0, "its strength is not counted");
    }

    // ---- the allocator fixtures -------------------------------------------------------------------------------

    // c0 the main effort in contact; the rest deployed reserves at the given sectors (BC13's fixture).
    static CrossingThreat Crossing(int link, bool left, float arrival, int sector = 0, bool across = false)
        => new CrossingThreat
        {
            Link = link, X = 100f + link, Z = 200f, Left = left, Sector = sector, OwnBankRegion = 0, GuardX = 10f + link, GuardZ = 20f, GuardHeading = 90f,
            EnemyAtCrossing = arrival, EnemyAtFlank = arrival + 0.3f, CandidateOwnShift = 9f, EnemyAcross = across,
        };

    // Shifts per threat row for the children c0..c(n-1); a NaN is a child the player holds.
    static IReadOnlyList<DirectChildIntent> CrossAllocate(DirectChildEvidence[] evidence, ReserveReleaseState state, double now, CrossingThreat[] threats,
        float[] shifts, float caution01 = 0.5f, float tolerance01 = 0.5f, bool[] exploiting = null, bool staggered = false, TacticalBattlePlan? plan = null,
        GuardPoint[] guardsOut = null)
    {
        var personality = new PersonalityVector(0.2f, 0f, 0f, 0f, 0f);
        var context = new ReserveReleaseContext(0.5f, false, state, exploiting, now, 0.5f, caution01, tolerance01, staggered, threats, threats.Length, shifts,
            evidence.Length);
        return DirectChildAllocator.AllocateWithZones(plan ?? ResPlan(BattlePhase.MainEffort), personality, ResChildren(evidence.Length), evidence, null, null, context);
    }

    static float[] Row(params float[] childShifts) => childShifts;

    // threats x children, every child's time to every crossing the same except c0 (the main effort, far away).
    static float[] Uniform(int threats, int children, float value)
    {
        var all = new float[threats * children];
        for (int t = 0; t < threats; t++) for (int c = 0; c < children; c++) all[t * children + c] = c == 0 ? 9f : value;
        return all;
    }

    static void ReleaseCrossing()
    {
        ReserveReleaseState.ResetAll();
        var state = new ReserveReleaseState();
        DirectChildEvidence[] ev = ResEvidence(1, 3, 4);                   // c0 main; c1..c3 deployed reserves
        float[] shifts = Row(9f, 0.4f, 0.2f, 0.9f);
        // The enemy gets there in 0.4 h: the quickest reserve (c2, 0.2 h) is released, since 0.4 <= 0.2 + 0.25 holds.
        IReadOnlyList<DirectChildIntent> intents = CrossAllocate(ev, state, 10.0, new[] { Crossing(5, true, 0.4f) }, shifts);
        AssertEqual(DirectChildRole.RefuseLeft, intents[2].Role, "the soonest reserve goes, on the crossing's side");
        AssertEqual(DirectChildRole.Reserve, intents[1].Role, "the others stay");
        AssertEqual(DirectChildRole.Reserve, intents[3].Role, "the others stay");
        AssertTrue(intents[2].HasGuardPoint, "with a guard point");
        AssertEqual(1, state.BoundCount, "and a binding");
        state.TryCrossingOf("c2", out CrossingBinding binding);
        AssertEqual(5, binding.Link, "to the crossing");
        AssertNear(0.2f, (float)binding.OwnShiftAtRelease, 1e-6f, "at its own shift");

        // The enemy is later than the reserve can answer plus the lead: no release (a fresh state, so nothing is bound).
        var late = new ReserveReleaseState();
        intents = CrossAllocate(ev, late, 10.0, new[] { Crossing(5, true, 0.46f) }, shifts);
        AssertEqual(DirectChildRole.Reserve, intents[2].Role, "0.46 > 0.2 + 0.25: not yet");
        AssertEqual(0, late.BoundCount, "nothing bound");

        // One release per crossing, the earliest first: two crossings take two different reserves (c2 is the soonest for both; once it has gone
        // the second crossing takes the next).
        var two = new ReserveReleaseState();
        float[] twoShifts = { 9f, 0.4f, 0.2f, 0.9f,   9f, 0.3f, 0.1f, 0.9f };
        intents = CrossAllocate(ev, two, 10.0, new[] { Crossing(5, true, 0.3f), Crossing(6, false, 0.35f) }, twoShifts);
        AssertEqual(DirectChildRole.RefuseLeft, intents[2].Role, "the first crossing (left) takes the soonest for it");
        AssertEqual(DirectChildRole.RefuseRight, intents[1].Role, "the second (right) takes the next, the first's guard being gone from the reserve");
        AssertEqual(DirectChildRole.Reserve, intents[3].Role, "the third stays");
        AssertEqual(2, two.BoundCount, "two crossings, two guards");

        // Never without a threat, and never without an eligible Reserve child.
        var plain = new ReserveReleaseState();
        intents = CrossAllocate(ev, plain, 10.0, new CrossingThreat[0], shifts);
        AssertEqual(DirectChildRole.Reserve, intents[2].Role, "no threat, no release (an Attacker's ledger lists none)");
        var onlyMain = new ReserveReleaseState();
        intents = CrossAllocate(new[] { new DirectChildEvidence(3, 1, true, 2, 0, 0.8f) }, onlyMain, 10.0, new[] { Crossing(5, true, 0.1f) }, Row(0.1f));
        AssertEqual(0, onlyMain.BoundCount, "no reserve to send");
    }

    static void ReleaseOrder()
    {
        ReserveReleaseState.ResetAll();
        var state = new ReserveReleaseState();
        // A crisis at sector 0 and an Exploit-phase stagger, with the stagger on: both take reserves first; the crossing takes a remaining one.
        var ev = new List<DirectChildEvidence>(ResEvidence(0, 1, 3, 4)) { Crisis(0) }.ToArray();     // c0 main, c1..c4 reserves, c5 in crisis
        float[] shifts = Row(9f, 0.1f, 0.1f, 0.1f, 0.1f, 9f);
        IReadOnlyList<DirectChildIntent> intents = CrossAllocate(ev, state, 10.0, new[] { Crossing(5, true, 0.2f) }, shifts, staggered: true,
            plan: ResPlan(BattlePhase.Exploit));
        AssertEqual(DirectChildRole.Fix, intents[1].Role, "the crisis takes the nearest reserve first");
        AssertEqual(DirectChildRole.SupportMain, intents[2].Role, "then the stagger releases one");
        int guards = 0;
        for (int i = 0; i < intents.Count; i++) if (intents[i].HasGuardPoint) { guards++; AssertTrue(intents[i].Role == DirectChildRole.RefuseLeft, "the guard"); }
        AssertEqual(1, guards, "and the crossing release takes from what is left");
        AssertTrue(!intents[1].HasGuardPoint && !intents[2].HasGuardPoint, "never one the crisis or the stagger took");
        string source = FlankResponseTests.SourceText("Tactical", "Orchestrator", "DirectChildAllocator.cs");
        int wing = source.IndexOf("ReleaseReserveToThreatenedWing(evidence, roles, axisOverride);", StringComparison.Ordinal);
        int crossing = source.IndexOf("ReleaseReserveToCrossings(roles, axisOverride, guards, release, snapshots);", StringComparison.Ordinal);
        AssertTrue(wing > 0 && crossing > wing, "the crossing release is appended after BC11's flank release");
    }

    static void ReleaseEligible()
    {
        ReserveReleaseState.ResetAll();
        DirectChildEvidence[] ev = ResEvidence(1, 3, 4);
        // c2 is the soonest but a live exploit asset: skipped for the next, c1.
        var state = new ReserveReleaseState();
        IReadOnlyList<DirectChildIntent> intents = CrossAllocate(ev, state, 10.0, new[] { Crossing(5, true, 0.4f) }, Row(9f, 0.4f, 0.2f, 0.9f),
            exploiting: new[] { false, false, true, false });
        AssertEqual(DirectChildRole.Reserve, intents[2].Role, "an exploit asset is never released to a crossing");
        AssertEqual(DirectChildRole.RefuseLeft, intents[1].Role, "the next soonest goes");
        // c2 holds player-owned strength (NaN): the same.
        var player = new ReserveReleaseState();
        intents = CrossAllocate(ev, player, 10.0, new[] { Crossing(5, true, 0.4f) }, Row(9f, 0.4f, float.NaN, 0.9f));
        AssertEqual(DirectChildRole.Reserve, intents[2].Role, "a child holding player-owned strength is never released");
        AssertEqual(DirectChildRole.RefuseLeft, intents[1].Role, "the next soonest goes");
        // A BC10 deployed reserve (every reserve of this fixture) is eligible: the release above is one.
        AssertEqual(1, player.BoundCount, "bound");
    }

    static void ReleaseAcross()
    {
        ReserveReleaseState.ResetAll();
        DirectChildEvidence[] ev = ResEvidence(1, 3);
        var state = new ReserveReleaseState();
        IReadOnlyList<DirectChildIntent> intents = CrossAllocate(ev, state, 10.0, new[] { Crossing(5, true, 0.05f, across: true) }, Row(9f, 0.1f, 0.1f));
        AssertEqual(0, state.BoundCount, "an enemy already across is contact: no new release");
        AssertEqual(DirectChildRole.Reserve, intents[1].Role, "the reserve stays");

        // A body that crossed at a distant ford does not suppress the release at this one.
        var other = new ReserveReleaseState();
        intents = CrossAllocate(ev, other, 10.0, new[] { Crossing(5, true, 0.05f, across: true), Crossing(7, false, 0.2f) }, Row(9f, 0.1f, 0.1f, 9f, 0.1f, 0.1f));
        AssertEqual(1, other.BoundCount, "the other crossing releases");
        other.TryCrossingOf("c1", out CrossingBinding binding);
        AssertEqual(7, binding.Link, "to link 7");

        // A guard already bound to a crossing stays bound when the enemy is across it (its arrival test holds at 0).
        var bound = new ReserveReleaseState();
        bound.BindCrossing(new CrossingBinding { ChildId = "c1", Link = 5, OwnShiftAtRelease = 0.1, ReleasedAtHours = 9.9 });
        intents = CrossAllocate(ev, bound, 10.0, new[] { Crossing(5, true, 0.05f, across: true) }, Row(9f, 0.1f, 0.1f));
        AssertEqual(DirectChildRole.RefuseLeft, intents[1].Role, "the guard stays");
        AssertEqual(1, bound.BoundCount, "bound");
    }

    static void ReleaseTarget()
    {
        ReserveReleaseState.ResetAll();
        DirectChildEvidence[] ev = ResEvidence(1, 3);
        var state = new ReserveReleaseState();
        IReadOnlyList<DirectChildIntent> intents = CrossAllocate(ev, state, 10.0, new[] { Crossing(5, false, 0.2f, sector: 4) }, Row(9f, 0.1f, 0.5f));
        AssertEqual(DirectChildRole.RefuseRight, intents[1].Role, "a crossing on the right: RefuseRight");
        AssertEqual(4, intents[1].AxisSector, "the crossing's band is the sector");
        AssertTrue(intents[1].HasGuardPoint, "a guard point");
        AssertEqual(15f, intents[1].Guard.X, "at the threat's guard x");
        AssertEqual(20f, intents[1].Guard.Z, "z");
        AssertEqual(90f, intents[1].Guard.Heading, "facing the crossing");

        // Where the ledger puts the guard: 100 behind the crossing along the front axis, snapped to own-bank land when that is river.
        FeasField f = DefenderField();
        FeasibilityLedger ledger = DefenderLedger(f);
        CrossingThreat ford = Threat(ledger, 0, out _);
        TacticalFrontAxis axis = ledger.Job.Input.Axis;
        AssertNear(ford.X - 100f * axis.ForwardX, ford.GuardX, 1f, "100 behind the crossing: x");
        AssertNear(ford.Z - 100f * axis.ForwardZ, ford.GuardZ, 1f, "z");
        AssertEqual(ford.OwnBankRegion, f.Regions.RegionAt(ford.GuardX, ford.GuardZ), "on the own bank");
        float toward = PostureSense.HeadingTo(ford.GuardX, ford.GuardZ, ford.X, ford.Z);
        AssertNear(toward, ford.GuardHeading, 0.5f, "facing the crossing");

        // A wide river: the stand-off point falls in the water and snaps to the own bank's land, never across.
        const int size = 1000;
        RiverRegions wide = RegionsOf(BandMap(size, 400, 600), size);
        int west = wide.RegionAt(300f, -500f), east = wide.RegionAt(700f, -500f);
        AssertTrue(west >= 0 && east >= 0 && west != east, "control: two banks");
        AssertTrue(wide.TrySnapToRegion(500f, -500f, west, out float wx, out _), "snaps to the west bank");
        AssertTrue(wx < 400f, "west of the river, " + wx);
        AssertTrue(wide.TrySnapToRegion(500f, -500f, east, out float ex, out _), "and to the east bank on request");
        AssertTrue(ex >= 600f, "east of it, " + ex);
        AssertTrue(wide.TrySnapToRegion(300f, -500f, west, out float sx, out _) && Math.Abs(sx - 300f) < 1e-3f, "a point already in the region stays");
    }

    static void ReleaseLatch()
    {
        ReserveReleaseState.ResetAll();
        DirectChildEvidence[] ev = ResEvidence(1, 3);
        var state = new ReserveReleaseState();
        float[] shifts = Row(9f, 0.2f, 0.5f);
        CrossAllocate(ev, state, 10.0, new[] { Crossing(5, true, 0.4f) }, shifts);
        AssertEqual(1, state.BoundCount, "released at 0.4 against an own shift of 0.2");
        // The hold bound is 0.2 + 0.25 + 0.25 = 0.7: at 0.6 the guard stays, at 0.8 it reverts.
        IReadOnlyList<DirectChildIntent> intents = CrossAllocate(ev, state, 10.05, new[] { Crossing(5, true, 0.6f) }, shifts);
        AssertEqual(DirectChildRole.RefuseLeft, intents[1].Role, "inside the hold bound the guard stays");
        intents = CrossAllocate(ev, state, 10.1, new[] { Crossing(5, true, 0.8f) }, shifts);
        AssertEqual(DirectChildRole.Reserve, intents[1].Role, "beyond it the guard reverts");
        AssertEqual(0, state.BoundCount, "the binding ends");
        // No threat listed (no enemy body perceived): a bound guard reverts.
        var bound = new ReserveReleaseState();
        CrossAllocate(ev, bound, 10.0, new[] { Crossing(5, true, 0.4f) }, shifts);
        intents = CrossAllocate(ev, bound, 10.05, new CrossingThreat[0], shifts);
        AssertEqual(DirectChildRole.Reserve, intents[1].Role, "with no enemy perceived the guard reverts");

        // 40 allocations against a stationary enemy at 0.4: released once, never alternating.
        var steady = new ReserveReleaseState();
        int changes = 0;
        DirectChildRole last = DirectChildRole.Unknown;
        for (int run = 0; run < 40; run++)
        {
            intents = CrossAllocate(ev, steady, 10.0 + run * 0.05, new[] { Crossing(5, true, 0.4f) }, shifts);
            if (intents[1].Role != last) { changes++; last = intents[1].Role; }
        }
        AssertEqual(1, changes, "released at the first run and never reverted");
        AssertEqual(DirectChildRole.RefuseLeft, last, "still on guard");
        // A plan change clears the binding.
        steady.Clear();
        AssertEqual(0, steady.BoundCount, "a plan change clears it");
    }

    static void ReleaseFloor()
    {
        ReserveReleaseState.ResetAll();
        // R0x = 4 reserves at the default share 0.30: two stay held, so only two of four go.
        DirectChildEvidence[] ev = ResEvidence(0, 1, 3, 4);
        var state = new ReserveReleaseState();
        float[] shifts = Uniform(4, ev.Length, 0.1f);
        var threats = new[] { Crossing(1, true, 0.1f), Crossing(2, true, 0.12f), Crossing(3, false, 0.14f), Crossing(4, false, 0.16f) };
        IReadOnlyList<DirectChildIntent> intents = CrossAllocate(ev, state, 10.0, threats, shifts);
        int held = 0;
        for (int i = 1; i < intents.Count; i++) if (intents[i].Role == DirectChildRole.Reserve) held++;
        AssertEqual(2, held, "floor 2 of 4");
        AssertEqual(2, state.BoundCount, "two guards");
        AssertEqual(0, state.InitialReserves, "BC13's R0 is untouched");
        AssertTrue(double.IsNegativeInfinity(state.LastReleaseHours), "and so is the stagger's last release hour: the next stagger is not delayed");

        // One reserve: it may go (BC13's rule).
        var single = new ReserveReleaseState();
        intents = CrossAllocate(ResEvidence(1), single, 10.0, new[] { Crossing(1, true, 0.1f) }, Row(9f, 0.1f));
        AssertEqual(DirectChildRole.RefuseLeft, intents[1].Role, "R0x = 1 may go");

        // The floor holds with the stagger off (every call above had it off) and the bound guards count in R0x on the next allocation.
        intents = CrossAllocate(ev, state, 10.05, threats, shifts);
        held = 0;
        for (int i = 1; i < intents.Count; i++) if (intents[i].Role == DirectChildRole.Reserve) held++;
        AssertEqual(2, held, "R0x stays 4 while the bindings hold: still two held");
        AssertEqual(2, state.BoundCount, "and the same two guards");
    }

    static void ReleaseStateScope()
    {
        ReserveReleaseState.ResetAll();
        AssertTrue(ReferenceEquals(ReserveReleaseState.ForAlliance(0), ReserveReleaseState.ForAlliance(0)), "one instance per alliance");
        AssertFalse(ReferenceEquals(ReserveReleaseState.ForAlliance(0), ReserveReleaseState.ForAlliance(1)), "a different one for the other");
        AssertEqual(null, ReserveReleaseState.ForAlliance(2), "none for any other alliance");
        ReserveReleaseState state = ReserveReleaseState.ForAlliance(0);
        state.BindCrossing(new CrossingBinding { ChildId = "c1", Link = 3, OwnShiftAtRelease = 0.2, ReleasedAtHours = 1.0 });
        AssertEqual(1, state.BoundCount, "bound");
        state.ClearTrigger();
        AssertEqual(1, state.BoundCount, "BC13's ClearTrigger leaves the bindings alone");
        BattleScope.ResetAll();
        AssertEqual(0, state.BoundCount, "the battle's end clears them");

        // A new battle day clears the state even when no plan is picked.
        state.BindCrossing(new CrossingBinding { ChildId = "c1", Link = 3, OwnShiftAtRelease = 0.2, ReleasedAtHours = 1.0 });
        var noPlan = new ArmyOrchestrator(0, SeedCatalog.AllHistoricalAndGeneric(), new PersonalityVector(0f, 0f, 0f, 0f, 0f));
        TacticalBattleCoordinator.ReplanForNewBattleDay(noPlan, new ArmyEvidence(1f, TerrainKind.Open, 0), QuietVisible());
        AssertEqual(0, state.BoundCount, "a new battle day clears it before it looks for a plan");

        string tick = FlankResponseTests.SourceText("Tactical", "Battle", "BattleCoreTick.cs");
        FlankResponseTests.AssertPinned(tick, "ReserveReleaseState.ForAlliance(alliance)?.Clear();", "a stand-down clears it even with no army");
        string army = FlankResponseTests.SourceText("Tactical", "Orchestrator", "ArmyOrchestrator.cs");
        FlankResponseTests.AssertPinned(army, "ReserveRelease.Clear();", "a plan change clears it");
        AssertFalse(FlankResponseTests.Flat(army).Contains("new ReserveReleaseState()"), "the army owns no copy of its own");
    }

    static void ReleaseShared()
    {
        ReserveReleaseState.ResetAll();
        var evidence = new ArmyEvidence(1.0f, TerrainKind.Open, 0);
        var first = new ArmyOrchestrator(0, SeedCatalog.AllHistoricalAndGeneric(), new PersonalityVector(0f, 0f, 0f, 0f, 0f));
        first.ReserveRelease.Record("c1", 10.0);
        first.ReserveRelease.BindCrossing(new CrossingBinding { ChildId = "c2", Link = 4, OwnShiftAtRelease = 0.2, ReleasedAtHours = 10.0 });
        var second = new ArmyOrchestrator(0, SeedCatalog.AllHistoricalAndGeneric(), new PersonalityVector(0f, 0f, 0f, 0f, 0f));
        AssertTrue(ReferenceEquals(first.ReserveRelease, second.ReserveRelease), "two armies of one alliance share the state");
        AssertTrue(second.ReserveRelease.IsReleased("c1"), "the second sees the first's release");
        AssertEqual(1, second.ReserveRelease.BoundCount, "and its binding: constructing it cleared nothing");

        BattleScope.ResetAll();
        var third = new ArmyOrchestrator(0, SeedCatalog.AllHistoricalAndGeneric(), new PersonalityVector(0f, 0f, 0f, 0f, 0f));
        AssertTrue(double.IsNegativeInfinity(third.ReserveRelease.LastReleaseHours), "the next battle starts clean: no release hour");
        AssertEqual(0, third.ReserveRelease.InitialReserves, "no initial reserves");
        AssertFalse(third.ReserveRelease.IsReleased("c1"), "no released child");
        AssertEqual(0, third.ReserveRelease.BoundCount, "no crossing binding");
        AssertTrue(evidence.CurrentOdds > 0f, "(fixture)");
    }

    static void DefenderWired()
    {
        string army = FlankResponseTests.SourceText("Tactical", "Orchestrator", "ArmyOrchestrator.cs");
        FlankResponseTests.AssertPinned(army, "if (!staggered && !feasible) return new ReserveReleaseContext(_lastGlobalOdds, flank);", "the shipped context stays when both flags are off");
        FlankResponseTests.AssertPinned(army, "staggered, _threatBuffer, threats, _threatShiftBuffer, threatChildren);", "the threats reach the context");
        FlankResponseTests.AssertPinned(army, "string reason = released.HasGuardPoint ? \"crossing-threat\" : \"flank-threat\";", "the release row names the crossing");
        FlankResponseTests.AssertPinned(army, "EnvelopmentManeuver.ExpandToNodes(_maneuvers, _commandNodeIntents, _directChildIntents)", "the guard point reaches the doctrine order");
        string maneuver = FlankResponseTests.SourceText("Tactical", "Orchestrator", "Plays", "EnvelopmentManeuver.cs");
        FlankResponseTests.AssertPinned(maneuver, "children[c].Guard.X, children[c].Guard.Z, children[c].Guard.Heading, \"guard-crossing\", isGuard: true", "as a GuardFlank node entry");
        string doctrine = FlankResponseTests.SourceText("Tactical", "Operations", "CommandDoctrineAssignment.cs");
        FlankResponseTests.AssertPinned(doctrine, "(stance != BattleStance.Defender || maneuver.IsGuard)", "a Defender's guard is applied, its maneuvers are not");
        string candidates = FlankResponseTests.SourceText("Tactical", "Orchestrator", "Plays", "ManeuverFeasibility.Candidates.cs");
        FlankResponseTests.AssertPinned(candidates, "if (target != null && target.CollectsThreats && plan == target.SelectedPlan && mirrored == target.SelectedMirrored) CaptureThreats(job);",
            "only the selected candidate on its selected side leaves threats");
    }
}
