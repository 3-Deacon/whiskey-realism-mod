using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static ManeuverFeasibilityFixtures;
using static WorksFixtures;

// BC14W verify round 2: three behaviours the first round's plants showed no test depended on (V01, V13, V15), and the pin of a holding fix.
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] WorksTurnTests() => new (string, Action)[]
    {
        ("bc14w TURN-AFTER-DARK a turn whose body strikes after dark is not an available turn, so FocalAssault is not ruled out by it", WTurnAfterDark),
        ("bc14w WORKS-MEETING a Meeting side gets the works doctrine: a focal point, works strike points and the bar of three, as an Attacker does", WMeetingGetsWorks),
        ("bc14w TURN-HOLD-FIX the turn-not-struck hold needs a fix facing works: with the fix off the works an unstruck turn holds nothing", WTurnHoldNeedsFix),
        ("bc14w WORKS-PIN-FADED holders faded to memory 0.4 leave the fix pinned and post a look; the same segment with own regiments standing on it lifts the pin", WPinFaded),
        ("bc14w WORKS-PIN-LEDGER the holding fix of an unstruck turn is pinned, the wing is not; the pin lifts when its works are broken in and with the flag off", WPinLedger),
    };

    static void WTurnAfterDark()
    {
        FeasField lit = Scenario6();
        FeasibilityLedger litLedger = lit.Evaluate(WorksPlans);
        AssertEqual("turn-available", Result(litLedger, BattlePlanId.FocalAssault).Reason, "control: in daylight a turn is available");
        AssertTrue(litLedger.Job.WorksActive, "control: the works doctrine is on");

        // The same field with the day almost over: every turn's strike falls after dark (the Daylight term is 1), so none is an available turn.
        FeasField dark = Scenario6();
        dark.DaylightHours = 0.1f;
        FeasibilityLedger darkLedger = dark.Evaluate(WorksPlans);
        AssertNear(1f, FirstWing(BodiesArray(darkLedger, BattlePlanId.ManeuverAndFix)).Daylight, 1e-6f, "the turn's wing strikes after dark");
        CandidateResult focal = Result(darkLedger, BattlePlanId.FocalAssault);
        AssertTrue(!string.Equals(focal.Reason, "turn-available", StringComparison.Ordinal), "FocalAssault is not ruled out by a turn that ends after dark, got " + focal.Reason);
        AssertEqual("after-dark", focal.Reason, "it is judged on its own terms instead (its own strike is after dark too)");
    }

    static void WMeetingGetsWorks()
    {
        FeasField attacker = Scenario7(Aggressive());
        FeasibilityLedger a = attacker.Evaluate(WorksPlans);
        FeasField meeting = Scenario7(Aggressive());
        meeting.Stance = BattleStance.Meeting;
        FeasibilityLedger m = meeting.Evaluate(WorksPlans);
        AssertTrue(m.TryFocalPoint(out FocalPoint focal), "a Meeting side has the enemy's focal point");
        AssertEqual(FocalKind.Junction, focal.Kind, "the junction");
        AssertTrue(m.Job.WorksActive && m.HeldWorks != null, "its evaluation holds the enemy's works");
        BodyEstimate wing = FirstWing(BodiesArray(m, BattlePlanId.FocalAssault));
        BodyEstimate reference = FirstWing(BodiesArray(a, BattlePlanId.FocalAssault));
        AssertTrue(wing.StrikeOnWorks && wing.AssaultsWorks, "its column assaults works");
        AssertNear(reference.BarUsed, wing.BarUsed, 1e-4f, "and is held to the bar of three the Attacker's is");
        AssertNear(reference.Detail, wing.Detail, 1e-4f, "with the same detail");
        BodyEstimate generic = BodiesArray(m, BattlePlanId.GenericAggressive)[0];
        AssertTrue(generic.StrikeOnWorks && generic.AssaultsWorks, "a frontal play pays for the works too");
        AssertEqual(FeasibilityVerdict.Feasible, Result(m, BattlePlanId.FocalAssault).Verdict, "FocalAssault is offered to a Meeting side");
    }

    // Scenario 6 with the centre cluster in the open and the works only on the left: the turn's fix does not face works, so an unstruck turn holds nothing.
    static void WTurnHoldNeedsFix()
    {
        FeasField control = Scenario6();
        FeasibilityLedger on = control.Evaluate(WorksPlans);
        CandidateResult onResult = Result(on, BattlePlanId.ManeuverAndFix);
        BodyEstimate[] onBodies = BodiesArray(on, BattlePlanId.ManeuverAndFix);
        on.CaptureSelection(BattlePlanId.ManeuverAndFix, onResult.Mirrored);
        var hold = new FakeWorld();
        hold.Phases[FirstWing(onBodies).ChildId] = ManeuverPhase.Hold;
        on.RefreshWorksHold(hold);
        AssertTrue(on.WorksHold(out string reason) && reason == "turn-not-struck", "control: the fix on the works holds the unstruck turn");
        AssertTrue(on.FacesWorks(MainOf(onBodies).ChildId), "control: because the fix faces works");

        FeasField open = Scenario6();
        EnemyObservation centre = open.Enemies[1];
        AssertNear(X(0f), centre.X, 1f, "control: the second cluster is the centre one");
        centre.CoverValue = 0f;
        centre.CoverObject = 0;
        centre.Standing = false;
        open.Enemies[1] = centre;
        open.WorksPoints = new Points().Line(-600f, -300f, 50f, l => 600f);
        FeasibilityLedger off = open.Evaluate(WorksPlans);
        AssertTrue(off.HeldWorks != null, "the left of the line is still held works");
        CandidateResult offResult = Result(off, BattlePlanId.ManeuverAndFix);
        BodyEstimate[] offBodies = BodiesArray(off, BattlePlanId.ManeuverAndFix);
        BodyEstimate fix = MainOf(offBodies);
        AssertFalse(fix.StrikeOnWorks, "the fix's strike point is off the works");
        off.CaptureSelection(BattlePlanId.ManeuverAndFix, offResult.Mirrored);
        var wait = new FakeWorld();
        wait.Phases[FirstWing(offBodies).ChildId] = ManeuverPhase.Hold;
        off.RefreshWorksHold(wait);
        AssertFalse(off.WorksHold(out string none), "nothing faces works, so an unstruck turn holds nothing");
        AssertTrue(none == null, "with no reason");
        AssertFalse(off.FacesWorks(fix.ChildId), "the fix faces none");
    }

    // The pin: the fix that holds the works while a wing turns them is pinned at stance 2; the wing is not; a break-in, the flag off and a wing that
    // assaults a different segment each have their say.
    static void WPinLedger()
    {
        FeasField f = Scenario6();
        FeasibilityLedger ledger = f.Evaluate(WorksPlans);
        CandidateResult result = Result(ledger, BattlePlanId.ManeuverAndFix);
        BodyEstimate[] bodies = BodiesArray(ledger, BattlePlanId.ManeuverAndFix);
        BodyEstimate wing = FirstWing(bodies), fix = MainOf(bodies);
        ledger.CaptureSelection(BattlePlanId.ManeuverAndFix, result.Mirrored);
        var world = new FakeWorld();
        world.Phases[wing.ChildId] = ManeuverPhase.Hold;
        ledger.RefreshWorksHold(world);
        AssertTrue(ledger.PinsWorks(fix.ChildId), "the fix facing works is pinned");
        AssertFalse(ledger.PinsWorks(wing.ChildId), "the wing faces no works and is not");
        AssertFalse(ledger.PinsWorks("child-unknown"), "an unknown child is not");
        AssertFalse(ledger.PinsWorks(null), "nor is a missing id");
        ledger.WorksReport(out _, out _, out int assaulting, out int facing, out int pinned);
        AssertTrue(facing >= 1, "control: children face works");
        AssertEqual(facing, pinned, "the works hold row counts every holding fix that faces them as pinned");
        AssertEqual(0, assaulting, "none of which assaults");

        // The pin does not wait for the works hold: the wing reaching its Attack lifts the hold and the fix is pinned still.
        world.Phases[wing.ChildId] = ManeuverPhase.Attack;
        ledger.RefreshWorksHold(world);
        AssertFalse(ledger.WorksHold(out _), "the hold has lifted");
        AssertTrue(ledger.PinsWorks(fix.ChildId), "and the fix is still pinned: the works are unbroken");

        // Broken in at the fix's own strike point: its holders shaken.
        FeasField shaken = Scenario6();
        for (int i = 0; i < shaken.Enemies.Count; i++)
        {
            EnemyObservation e = shaken.Enemies[i];
            if (e.CoverValue > 0f) { e.Morale = 0.3f; shaken.Enemies[i] = e; }
        }
        FeasibilityLedger broken = shaken.Evaluate(WorksPlans);
        BodyEstimate[] brokenBodies = BodiesArray(broken, BattlePlanId.ManeuverAndFix);
        broken.CaptureSelection(BattlePlanId.ManeuverAndFix, Result(broken, BattlePlanId.ManeuverAndFix).Mirrored);
        broken.RefreshWorksHold(world);
        AssertTrue(broken.FacesWorks(MainOf(brokenBodies).ChildId), "control: the fix still faces works");
        AssertFalse(broken.PinsWorks(MainOf(brokenBodies).ChildId), "shaken holders: the works are broken in and the pin lifts");
        broken.WorksReport(out _, out _, out _, out int brokenFacing, out int brokenPinned);
        AssertTrue(brokenFacing >= 1, "control: the children still face works");
        AssertEqual(0, brokenPinned, "and the row counts none pinned");

        // Flag off: no works doctrine, no pin.
        bool saved = WhiskeyRealism.Plugin.EnemyWorksDoctrine;
        try
        {
            WhiskeyRealism.Plugin.EnemyWorksDoctrine = false;
            ledger.RefreshWorksHold(world);
            AssertFalse(ledger.PinsWorks(fix.ChildId), "with Enable Enemy Works Doctrine off nothing is pinned");
        }
        finally { WhiskeyRealism.Plugin.EnemyWorksDoctrine = saved; }
        ledger.RefreshWorksHold(world);
        AssertTrue(ledger.PinsWorks(fix.ChildId), "control: back on, it is");
    }

    // The coordinator's ruling (2026-10-17): holders lost to fog are what entrenched troops look like, so a fade is not a break-in. The centre cluster's
    // holders are memories at confidence 0.4 and nothing else is known of them: the fix stays pinned and the ledger posts a look at the faded strike point;
    // with an own regiment standing on the works there, the pin lifts and no look is posted.
    static void WPinFaded()
    {
        FeasField Faded()
        {
            FeasField f = Scenario6();
            EnemyObservation centre = f.Enemies[1];
            AssertNear(X(0f), centre.X, 1f, "control: the second cluster is the centre one");
            centre.State = ObservationState.Remembered;
            centre.Confidence01 = 0.4f;
            f.Enemies[1] = centre;
            return f;
        }

        FeasField fadedField = Faded();
        FeasibilityLedger faded = fadedField.Evaluate(WorksPlans);
        BodyEstimate[] bodies = BodiesArray(faded, BattlePlanId.ManeuverAndFix);
        BodyEstimate fix = MainOf(bodies);
        faded.CaptureSelection(BattlePlanId.ManeuverAndFix, Result(faded, BattlePlanId.ManeuverAndFix).Mirrored);
        faded.RefreshWorksHold(new FakeWorld());
        AssertTrue(fix.StrikeOnWorks, "the fix's strike point is still on held works: the faded holders hold their segment");
        AssertTrue(faded.FacesWorks(fix.ChildId), "so the fix still faces them");
        AssertTrue(faded.PinsWorks(fix.ChildId), "and stays pinned: nothing perceived says they left");
        var xs = new float[ManeuverFeasibility.MaxLookRequests];
        var zs = new float[ManeuverFeasibility.MaxLookRequests];
        int looks = faded.LookRequests(xs, zs);
        AssertTrue(looks >= 1, "a look is posted");
        bool atStrike = false;
        for (int k = 0; k < looks; k++)
            atStrike |= Math.Abs(xs[k] - fix.StrikeX) < 5f && Math.Abs(zs[k] - (fix.StrikeZ - (WhiskeyRealism.Tactical.Battle.WorksRules.WorksBandMeters + WhiskeyRealism.Tactical.Battle.WorksRules.WorksEndMeters))) < 5f;
        AssertTrue(atStrike, "at the fix's strike point, drawn back outside the works band");
        AssertFalse(faded.HeldWorks.InWorksBand(xs[0], zs[0]), "where the recon producer will not refuse it");

        // Control: with the centre cluster seen (Visible) there is no fade, so no look, and the pin is the plain one.
        FeasibilityLedger seen = Scenario6().Evaluate(WorksPlans);
        seen.CaptureSelection(BattlePlanId.ManeuverAndFix, Result(seen, BattlePlanId.ManeuverAndFix).Mirrored);
        seen.RefreshWorksHold(new FakeWorld());
        AssertTrue(seen.PinsWorks(MainOf(BodiesArray(seen, BattlePlanId.ManeuverAndFix)).ChildId), "control: pinned when seen");
        AssertEqual(0, seen.LookRequests(new float[2], new float[2]), "control: and no look is posted for it");

        // Own regiments standing on the works at the fix's strike point: positive evidence, the pin lifts and the look ends.
        FeasField held = Faded();
        held.AddPicket(fix.StrikeX - X(0f), fix.StrikeZ + 1200f + 20f, 0f);
        FeasibilityLedger onWorks = held.Evaluate(WorksPlans);
        BodyEstimate[] onBodies = BodiesArray(onWorks, BattlePlanId.ManeuverAndFix);
        onWorks.CaptureSelection(BattlePlanId.ManeuverAndFix, Result(onWorks, BattlePlanId.ManeuverAndFix).Mirrored);
        onWorks.RefreshWorksHold(new FakeWorld());
        AssertTrue(onWorks.FacesWorks(MainOf(onBodies).ChildId), "control: the fix still faces the works");
        AssertFalse(onWorks.PinsWorks(MainOf(onBodies).ChildId), "an own regiment on them: the strike has broken in and the pin lifts");
        AssertEqual(0, onWorks.LookRequests(new float[2], new float[2]), "and no look is posted");
    }
}
