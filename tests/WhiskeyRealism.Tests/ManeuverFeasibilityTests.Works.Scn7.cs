using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using WhiskeyRealism.Strategic;
using static TestHarness;
using static TestFixtures;
using static ManeuverFeasibilityFixtures;
using static WorksFixtures;

// BC14W task 8: the § 6 scenario 7 end to end (spec § 13.2 FEAS-SCN-7): an entrenched line with both ends anchored. The aggressive commander's selection is
// the mirrored ManeuverAndFix: its wing strikes the weak 6,000-man end at odds 2.67 and its Center fix is pinned at stance 2 until the break-in (the
// works-pin rule, § 7A). FocalAssault is priced Feasible for him too and its column launches when it is the plan (forced here, as it is not his pick); the
// cautious and the mid commanders are held by the works hold, whichever plan wins, and no assaulting child leaves its Hold. The runtime (BuildInput, the
// executor) is the harness's stand-in here: the ledger's own release and focal point feed BC11's machine.
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] WorksScn7Tests() => new (string, Action)[]
    {
        ("bc14w FEAS-SCN-7 the junction is the focal point at odds 2.34; FocalAssault is Feasible for the aggressive commander and, as the plan, its column launches", WScn7),
        ("bc14w FEAS-SCN-7 the aggressive commander selects the mirrored ManeuverAndFix: the wing strikes the weak end at 2.67, the Center is pinned at stance 2 until the break-in and takes 3 after", WScn7Selection),
        ("bc14w FEAS-SCN-7 the cautious and the mid commanders are held for 3 hours with no release, whichever plan wins", WScn7Held),
    };

    // Steps a maneuver the way the runtime feeds it: the ledger's planned release (when not held), the vias, the focal point, the works hold's flag.
    static ManeuverInput RuntimeInput(FeasibilityLedger ledger, BattlePlanId plan, BodyEstimate body, double activated)
    {
        ManeuverInput input = FlankResponseTests.Maneuver();
        input.Kind = body.Kind;
        input.Envelops = body.Envelops;
        input.PlayActivatedHours = activated;
        input.TemplateHasCenter = false;
        input.StartOffsetSeconds = 10f;
        input.MarchRouteKnown = true;
        input.HasEnemyExtent = false;
        if (body.Kind == PlayRectKind.AssaultColumn && ledger.TryFocalPoint(out FocalPoint focal))
        {
            input.HasFocal = true;
            input.FocalX = focal.X;
            input.FocalZ = focal.Z;
        }
        bool held = false;
        if (ledger.WorksHold(out string reason)) held = EnvelopmentManeuver.HoldForWorksFor(reason, ledger.AssaultsWorks(body.ChildId), !body.Envelops);
        input.HoldForWorks = held;
        if (!held && ledger.TryPlannedRelease(body.ChildId, out double release, out double budget, out _, out _, out _, out _))
        {
            input.HasPlannedRelease = true;
            input.PlannedReleaseHours = release;
            input.HasTimeBudget = budget > 0.0;
            input.TimeBudgetHours = budget;
        }
        return input;
    }

    static void WScn7()
    {
        // The focal point is the junction (-300, 600) and the assault body's odds are the spec's 2.34.
        FeasField aggressiveField = Scenario7(Aggressive());
        FeasibilityLedger aggressive = aggressiveField.Evaluate(WorksPlans);
        AssertTrue(aggressive.TryFocalPoint(out FocalPoint focal), "the junction is a focal point");
        AssertNear(0f, focal.X - FeasField.X(-300f), 1f, "at lateral -300");
        AssertNear(0f, focal.Z - FeasField.Z(600f), 1f, "at depth 600");
        AssertEqual(FocalKind.Junction, focal.Kind, "a junction");
        BodyEstimate[] bodies = BodiesArray(aggressive, BattlePlanId.FocalAssault);
        BodyEstimate column = FirstWing(bodies);
        AssertNear(2.34f, column.IsolatedOdds, 0.1f, "the assault's odds ~ 2.34");
        AssertNear(0f, column.Detail, 0.02f, "the aggressive commander's bar of three: detail 0");
        AssertEqual(PlayRectKind.AssaultColumn, column.Kind, "the assault body is the column");
        AssertTrue(column.AssaultsWorks, "and assaults the works");

        // FocalAssault is priced Feasible for him and scores above every frontal play; it is not his pick (the next test): the mirrored ManeuverAndFix
        // scores 0.654 against its 0.638.
        var report = new SelectionReport();
        SeedCatalog.AllHistoricalAndGeneric().Select(WorksContext(aggressive, 51000f / 25000f, false, AggressiveVector()), report);
        float focalScore = float.NaN;
        for (int r = 0; r < report.Count; r++) if (report[r].Plan == BattlePlanId.FocalAssault) focalScore = report[r].Score;
        AssertFalse(float.IsNaN(focalScore), "FocalAssault is scored");
        AssertEqual(FeasibilityVerdict.Feasible, Result(aggressive, BattlePlanId.FocalAssault).Verdict, "and Feasible");
        foreach (BattlePlanId frontal in new[] { BattlePlanId.GenericAggressive, BattlePlanId.FrontalShockAssault, BattlePlanId.ContinuousAttrition, BattlePlanId.ForcedFrontalAttack })
        {
            for (int r = 0; r < report.Count; r++)
                if (report[r].Plan == frontal) AssertTrue(report[r].Score < focalScore, frontal + " scores below FocalAssault: " + report[r].Score + " vs " + focalScore);
            BodyEstimate[] frontalBodies = BodiesArray(aggressive, frontal);
            AssertTrue(frontalBodies[0].Detail > 0.2f, frontal + " pays for the whole army against the works: detail " + frontalBodies[0].Detail);
        }
        aggressive.CaptureSelection(BattlePlanId.FocalAssault, false);
        aggressive.RefreshWorksHold(new FakeWorld());
        AssertFalse(aggressive.WorksHold(out _), "his assault is priced worth making: no works hold");

        // Were FocalAssault the plan (forced here), his column launches: Hold at the release, March in column to the form-up point, Turn, Attack on the focal point.
        ManeuverInput input = RuntimeInput(aggressive, BattlePlanId.FocalAssault, column, 10.0);
        AssertTrue(input.HasPlannedRelease, "the ledger published the column's release");
        var state = default(ManeuverState);
        ManeuverOrder order = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.0));
        AssertEqual(ManeuverPhase.Hold, order.Phase, "held for the synchronized release");
        bool marched = false, turned = false, attacked = false;
        double now = 10.0;
        for (int step = 0; step < 80 && !attacked; step++)
        {
            now += 0.05;
            input = RuntimeInput(aggressive, BattlePlanId.FocalAssault, column, 10.0);
            if (marched) { input.ChildArrived = true; input.ChildX = -300f; input.ChildZ = 400f; }
            order = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, now));
            if (order.Phase == ManeuverPhase.March) { marched = true; AssertTrue(order.HasFormation && order.Formation == 4, "marches in column"); }
            if (order.Phase == ManeuverPhase.Turn) turned = true;
            if (order.Phase == ManeuverPhase.Attack) attacked = true;
            AssertTrue(order.Phase != ManeuverPhase.Abandoned, "the column is not abandoned: " + order.Reason);
        }
        AssertTrue(marched && turned && attacked, "the column marches, turns and attacks");
        AssertTrue(order.HasFormation && order.Formation == 4, "the Attack is in column");
        AssertNear(focal.X, order.TargetX, 1f, "on the focal point (x)");
        AssertNear(focal.Z, order.TargetZ, 1f, "on the focal point (z)");
    }

    // The real outcome of the aggressive commander (verify round 2): what Select() picks, the side the evaluation chose, and what the works doctrine does to
    // it. The Center fix faces the works too (its strike point is on them, E 0), so it is pinned at stance 2 until the strike it pins for has broken in.
    static void WScn7Selection()
    {
        const float centerLateral = 18f;
        FeasField field = Scenario7(Aggressive());
        FeasibilityLedger aggressive = field.Evaluate(WorksPlans);
        var report = new SelectionReport();
        BattlePlanId chosen = SeedCatalog.AllHistoricalAndGeneric().Select(WorksContext(aggressive, 51000f / 25000f, false, AggressiveVector()), report).Id;
        AssertEqual(BattlePlanId.ManeuverAndFix, chosen, "the aggressive commander selects ManeuverAndFix, not FocalAssault");
        float mafScore = float.NaN, focalScore = float.NaN;
        for (int r = 0; r < report.Count; r++)
        {
            if (report[r].Plan == BattlePlanId.ManeuverAndFix) mafScore = report[r].Score;
            if (report[r].Plan == BattlePlanId.FocalAssault) focalScore = report[r].Score;
        }
        AssertNear(0.654f, mafScore, 0.01f, "at 0.654");
        AssertNear(0.638f, focalScore, 0.01f, "against FocalAssault's 0.638");

        CandidateResult result = Result(aggressive, chosen);
        AssertTrue(result.Mirrored, "on the mirrored side: the better-side rule takes the 6,000-man end, not the authored 13,000-man one");
        AssertEqual(FeasibilityVerdict.Feasible, result.Verdict, "Feasible");
        BodyEstimate[] bodies = BodiesArray(aggressive, chosen);
        BodyEstimate wing = FirstWing(bodies);
        BodyEstimate center = bodies[Array.FindIndex(bodies, b => b.Main)];
        AssertNear(FeasField.X(-450f), wing.StrikeX, 30f, "the wing strikes the weak end at lateral -450");
        AssertNear(2.67f, wing.IsolatedOdds, 0.05f, "at odds 2.67");
        AssertNear(0f, wing.Detail, 0.02f, "the aggressive commander's bar of three: detail 0");
        AssertTrue(wing.StrikeOnWorks && wing.AssaultsWorks, "a works assault");
        AssertTrue(center.StrikeOnWorks && !center.AssaultsWorks, "the Center's strike point lies on the works and it assaults nothing");
        AssertNear(0f, center.EnemyAgainst, 1f, "it is a pin: no enemy against it (E 0)");
        AssertNear(FeasField.X(centerLateral), center.StrikeX, 30f, "at the middle of the line");

        aggressive.CaptureSelection(chosen, result.Mirrored);
        aggressive.RefreshWorksHold(new FakeWorld());
        AssertFalse(aggressive.WorksHold(out _), "the wing is priced worth making: no works hold");
        AssertTrue(aggressive.AssaultsWorks(wing.ChildId) && !aggressive.PinsWorks(wing.ChildId), "the wing assaults and is not pinned");
        AssertTrue(aggressive.FacesWorks(center.ChildId) && !aggressive.AssaultsWorks(center.ChildId), "the Center faces works and assaults none");

        // Before the break-in: the Center holds at stance 2, whatever task BC11's release gives it.
        AssertTrue(aggressive.PinsWorks(center.ChildId), "the Center is pinned: the works its strike point lies on are unbroken");
        AssertEqual(TacticalCommandTaskToStanceMapper.StanceDefend, TacticalCommandTaskToStanceMapper.Map(CommandTaskType.FixEnemy, aggressive.PinsWorks(center.ChildId)),
            "its FixEnemy takes stance 2");
        AssertEqual(TacticalCommandTaskToStanceMapper.StanceAttack, TacticalCommandTaskToStanceMapper.Map(CommandTaskType.FixEnemy, false),
            "control: without the pin the same task takes the attack stance");

        // After: the 6,000-man end the wing struck is shaken (morale 0.3 is below 0.45, 6,000 of the 11,000 within 300 of the strike).
        FeasField shaken = Scenario7(Aggressive());
        EnemyObservation end = shaken.Enemies[0];
        AssertNear(FeasField.X(-450f), end.X, 1f, "control: the first enemy is the weak end");
        end.Morale = 0.3f;
        shaken.Enemies[0] = end;
        FeasibilityLedger after = shaken.Evaluate(WorksPlans);
        after.CaptureSelection(chosen, result.Mirrored);
        after.RefreshWorksHold(new FakeWorld());
        BodyEstimate[] afterBodies = BodiesArray(after, chosen);
        BodyEstimate afterCenter = afterBodies[Array.FindIndex(afterBodies, b => b.Main)];
        AssertTrue(after.FacesWorks(afterCenter.ChildId), "control: the Center still faces the works");
        AssertFalse(after.PinsWorks(afterCenter.ChildId), "the strike it pins for has broken in: no pin");
        AssertEqual(TacticalCommandTaskToStanceMapper.StanceAttack, TacticalCommandTaskToStanceMapper.Map(CommandTaskType.FixEnemy, after.PinsWorks(afterCenter.ChildId)),
            "its FixEnemy takes stance 3");
    }

    static void WScn7Held()
    {
        // The cautious commander: FocalAssault is declined, and whatever wins is held.
        FeasibilityLedger cautious = Scenario7(Cautious()).Evaluate(WorksPlans);
        AssertEqual("declined", Result(cautious, BattlePlanId.FocalAssault).Reason, "FocalAssault is declined");
        HeldRun(cautious, SeedCatalog.AllHistoricalAndGeneric().Select(WorksContext(cautious, 51000f / 25000f, true, new PersonalityVector(-0.6f, 0.8f, -0.6f, 0f, 0f))).Id, "cautious");

        // The mid commander: with EnvelopmentPressure ManeuverAndFix (on its mirrored side: its wing strikes the works), without it, by the spec's scores, FocalAssault (detail ~ 0.88).
        FeasibilityLedger mid = Scenario7().Evaluate(WorksPlans);
        BattlePlanId withPressure = SeedCatalog.AllHistoricalAndGeneric().Select(MidContext(mid, 51000f / 25000f, true)).Id;
        AssertEqual(BattlePlanId.ManeuverAndFix, withPressure, "with EnvelopmentPressure the mid commander selects ManeuverAndFix");
        // The spec's sentence prices the right wing, detail 1; the evaluation picks the cheaper side by its own rule (the left, the 6,000 body: E 6,600, odds
        // 2.58 against the bar of three) and reads 0.57. Either way the wing's works assault is Risky, at or above RiskyPenalty, which is what the hold keys on.
        AssertTrue(FirstWing(BodiesArray(mid, BattlePlanId.ManeuverAndFix)).Detail >= ManeuverFeasibility.RiskyPenalty, "whose wings strike the works: a Risky detail");
        HeldRun(mid, withPressure, "mid with pressure");
        // Without it the spec's FocalAssault scores are reproduced (0.52 - 0.35 = 0.172 against ContinuousAttrition's 0.156). What differs is the wing of
        // ManeuverAndFix: the spec prices the right side, detail 1, term -0.4 (E 13,200, odds 1.29), but the evaluation selects the cheaper mirrored side
        // (the left, 6,000 body: detail 0.57, term -0.23), which scores 0.321 and wins. The fixture is not symmetrical, so the spec's sentence and its
        // better-side rule disagree and the rule governs (spec § 7A, SCN-7 as restated). Either selection is held by the works hold.
        var without = new SelectionReport();
        BattlePlanId chosen = SeedCatalog.AllHistoricalAndGeneric().Select(MidContext(mid, 51000f / 25000f, false), without).Id;
        float focalMid = float.NaN, attritionMid = float.NaN;
        for (int r = 0; r < without.Count; r++)
        {
            if (without[r].Plan == BattlePlanId.FocalAssault) focalMid = without[r].Score;
            if (without[r].Plan == BattlePlanId.ContinuousAttrition) attritionMid = without[r].Score;
        }
        AssertNear(0.172f, focalMid, 0.01f, "FocalAssault scores 0.52 - 0.35");
        AssertNear(0.156f, attritionMid, 0.01f, "ContinuousAttrition 0.156");
        AssertTrue(focalMid > attritionMid, "FocalAssault beats the frontal plays");
        AssertNear(0.88f, FirstWing(BodiesArray(mid, BattlePlanId.FocalAssault)).Detail, 0.05f, "its detail ~ 0.88");
        HeldRun(mid, chosen, "mid without pressure (" + chosen + ")");
        HeldRun(mid, BattlePlanId.FocalAssault, "mid with FocalAssault chosen");
    }

    // A selection the works hold keeps: hopeless-assault, the assaulting children HoldForWorks, none leaves its Hold over 3 doctrine-h, none is published a release.
    static void HeldRun(FeasibilityLedger ledger, BattlePlanId plan, string who)
    {
        ledger.CaptureSelection(plan, Result(ledger, plan).Mirrored);
        ledger.RefreshWorksHold(new FakeWorld());
        AssertTrue(ledger.WorksHold(out string reason), who + ": " + plan + " is held");
        AssertEqual("hopeless-assault", reason, who + ": for a hopeless assault");
        // The TacticalWorksHold row's facts: the worst works assault's detail and the bar it was held to, and who assaults.
        ledger.WorksReport(out float reportDetail, out float reportBar, out int assaulting, out int facing, out _);
        AssertTrue(reportDetail >= ManeuverFeasibility.RiskyPenalty, who + ": the row's detail is the hopeless one: " + reportDetail);
        AssertTrue(reportBar > 0f, who + ": and names the bar it was held to: " + reportBar);
        AssertTrue(assaulting > 0 && facing >= assaulting, who + ": " + assaulting + " children assault, " + facing + " face works");
        BodyEstimate[] bodies = BodiesArray(ledger, plan);
        int held = 0;
        foreach (BodyEstimate body in bodies)
        {
            if (!ledger.AssaultsWorks(body.ChildId) && body.Envelops == false && !body.Main) continue;
            if (!ledger.AssaultsWorks(body.ChildId)) continue;
            held++;
            ManeuverInput input = RuntimeInput(ledger, plan, body, 10.0);
            AssertTrue(input.HoldForWorks, who + ": HoldForWorks is set for " + body.Kind);
            AssertFalse(input.HasPlannedRelease, who + ": and no planned release is published");
            var state = default(ManeuverState);
            for (double t = 10.0; t <= 13.0 + 1e-9; t += 0.1)
            {
                ManeuverOrder order = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, t));
                AssertEqual(ManeuverPhase.Hold, order.Phase, who + ": " + body.Kind + " stays in Hold at " + t);
            }
        }
        AssertTrue(held > 0, who + ": some child assaults the works");
    }
}
