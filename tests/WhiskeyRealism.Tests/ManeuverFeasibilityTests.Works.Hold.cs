using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;

// BC14W task 6: the works hold's reach into BC11's maneuver and the phase machine (spec § 7A "BC11 reads it" and "The phase"; § 13.2 WORKS-HOLD-BC11,
// WORKS-HOLD-GATE). The runtime wiring (PlayExecutorRuntime.StepManeuvers, ArmyOrchestrator.EvaluateAndAdvancePhase) is pinned in the source.
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] WorksHoldTests() => new (string, Action)[]
    {
        ("bc14w WORKS-HOLD-BC11-FOR HoldForWorksFor holds a works-assaulting body and a start delay under hopeless-assault, and never under turn-not-struck", WHoldFor),
        ("bc14w WORKS-HOLD-BC11 a held maneuver stays in Hold past the ceiling, clears its latched release and budget, and a release latched before the hold does not launch it", WHoldManeuver),
        ("bc14w WORKS-HOLD-BC11-CLEAR a cleared hold latches its hour and the ceiling counts from it", WHoldClear),
        ("bc14w WORKS-HOLD-BC11-DELAY a start delay and a fix hold stay unreleased under the flag and release when it clears", WHoldStartDelay),
        ("bc14w WORKS-HOLD-BC11-LAUNCHED a wing already marching is not recalled by the flag, and a flag that was never held latches no clear", WHoldLaunched),
        ("bc14w WORKS-HOLD-GATE the phase never moves into MainEffort while the works hold is on, and is never stepped back out of it", WHoldGate),
        ("bc14w WORKS-HOLD-WIRING PlayExecutorRuntime sets HoldForWorks through HoldForWorksFor and publishes no planned release, and the orchestrator passes the hold to the phase", WHoldWiring),
    };

    static void WHoldFor()
    {
        AssertTrue(EnvelopmentManeuver.HoldForWorksFor("hopeless-assault", assaultsWorks: true, startDelay: false), "a works-assaulting body is held");
        AssertTrue(EnvelopmentManeuver.HoldForWorksFor("hopeless-assault", assaultsWorks: false, startDelay: true), "and a start delay of the plan, whatever it faces");
        AssertFalse(EnvelopmentManeuver.HoldForWorksFor("hopeless-assault", assaultsWorks: false, startDelay: false), "a body that assaults no works marches");
        AssertFalse(EnvelopmentManeuver.HoldForWorksFor("turn-not-struck", assaultsWorks: true, startDelay: true), "the turning wing must march");
        AssertFalse(EnvelopmentManeuver.HoldForWorksFor(null, assaultsWorks: true, startDelay: true), "no hold, no flag");
        AssertFalse(EnvelopmentManeuver.HoldForWorksFor(string.Empty, assaultsWorks: true, startDelay: true), "an empty reason is no hold");
    }

    static ManeuverInput Held(ManeuverInput input)
    {
        input.HoldForWorks = true;
        return input;
    }

    static void WHoldManeuver()
    {
        // The release 10.5 is planned and latched before the hold (activation 10.0; the ceiling would be 11.5).
        ManeuverInput input = WithRelease(10.5);
        var state = default(ManeuverState);
        EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.0));
        AssertTrue(state.HasFirstPlannedRelease && state.FirstPlannedReleaseHours == 10.5, "the release is latched before the hold");
        state.HasBudget = true;
        state.BudgetHours = 3.0;

        ManeuverOrder order = default(ManeuverOrder);
        foreach (double now in new[] { 10.1, 10.6, 11.0, 11.6, 12.5, 13.9 })
        {
            order = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(Held(input), now));
            AssertEqual(ManeuverPhase.Hold, order.Phase, "held at " + now + " (past the release, past the ceiling)");
            AssertEqual("works-hold", order.Reason, "and says why at " + now);
        }
        AssertFalse(state.HasFirstPlannedRelease, "the latched release is cleared");
        AssertFalse(state.HasBudget, "and so is the budget");

        // The hold clears at 14.0 and the ledger plans a new release 14.4: the wing waits for it; the old latch (ceiling 11.5) must not launch it.
        input.PlannedReleaseHours = 14.4;
        order = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 14.0));
        AssertEqual(ManeuverPhase.Hold, order.Phase, "a release latched before the hold does not launch the wing at the first tick");
        AssertEqual("await-planned-release", order.Reason, "it waits for the new release");
        AssertTrue(state.HasFirstPlannedRelease && state.FirstPlannedReleaseHours == 14.4, "the new release is latched");
        order = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 14.4));
        AssertEqual(ManeuverPhase.March, order.Phase, "and it marches at it");
        AssertEqual("planned-release", state.ProceedReason, "by the release, not the ceiling");
    }

    static void WHoldClear()
    {
        // No planned release; the Center child never makes contact: only the ceiling can end the Hold.
        ManeuverInput input = FlankResponseTests.Maneuver();
        var state = default(ManeuverState);
        EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(Held(input), 10.0));
        EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(Held(input), 12.0));
        AssertFalse(state.HasWorksClear, "held: nothing has cleared");

        ManeuverOrder order = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 13.0));
        AssertTrue(state.HasWorksClear && state.WorksClearHours == 13.0, "the hour the hold cleared is latched");
        AssertEqual(ManeuverPhase.Hold, order.Phase, "the BC11 Hold goes on (no contact yet)");
        AssertEqual("await-center-contact", order.Reason, "for the Center's contact");
        AssertEqual(ManeuverPhase.Hold, EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 13.99)).Phase, "the ceiling is 1 h past the clear, not past the activation");
        order = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 14.0));
        AssertEqual(ManeuverPhase.March, order.Phase, "the ceiling ends it at max(activation, clear) + MaxManeuverHours");
        AssertEqual("hold-ceiling", state.ProceedReason, "and says so");

        // The ceiling is the greatest of the three: a first planned release later than the clear wins.
        ManeuverInput late = WithRelease(15.5);
        var lateState = default(ManeuverState);
        EnvelopmentManeuver.Step(ref lateState, FlankResponseTests.After(Held(late), 10.0));
        late.PlannedReleaseHours = 20.0;
        EnvelopmentManeuver.Step(ref lateState, FlankResponseTests.After(late, 12.0));
        AssertEqual(12.0, lateState.WorksClearHours, "cleared at 12");
        AssertEqual(ManeuverPhase.Hold, EnvelopmentManeuver.Step(ref lateState, FlankResponseTests.After(late, 13.0)).Phase, "the first release planned after the clear (20.0) holds it past clear + 1 h");
        AssertEqual(ManeuverPhase.March, EnvelopmentManeuver.Step(ref lateState, FlankResponseTests.After(late, 20.0)).Phase, "until its own release");

        // A second hold: the clear hour is the last one.
        ManeuverInput again = FlankResponseTests.Maneuver();
        var againState = default(ManeuverState);
        EnvelopmentManeuver.Step(ref againState, FlankResponseTests.After(Held(again), 10.0));
        EnvelopmentManeuver.Step(ref againState, FlankResponseTests.After(again, 10.2));
        EnvelopmentManeuver.Step(ref againState, FlankResponseTests.After(Held(again), 10.3));
        EnvelopmentManeuver.Step(ref againState, FlankResponseTests.After(again, 10.4));
        AssertEqual(10.4, againState.WorksClearHours, "the latest clear is the one the ceiling counts from");
    }

    static void WHoldStartDelay()
    {
        // A start-delay wing (HesitatedCommit): 600 battle-s after the activation it is Released, unless the works hold keeps it.
        ManeuverInput delay = FlankResponseTests.Maneuver();
        delay.Kind = PlayRectKind.LeftWing;
        delay.Envelops = false;
        delay.StartOffsetSeconds = 600f;
        var state = default(ManeuverState);
        ManeuverOrder order = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(Held(delay), 10.0));
        AssertEqual(ManeuverPhase.Hold, order.Phase, "held from the start");
        order = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(Held(delay), 11.5));
        AssertEqual(ManeuverPhase.Hold, order.Phase, "the offset has long passed: still unreleased");
        AssertEqual("works-hold", order.Reason, "for the works");
        order = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(delay, 11.6));
        AssertEqual(ManeuverPhase.Released, order.Phase, "released when the hold clears");

        // A fix with a planned release (the focal assault's support): a fix with no planned release is not a hold, but under the works hold it waits too.
        ManeuverInput fix = FlankResponseTests.Maneuver();
        fix.Kind = PlayRectKind.Center;
        fix.Envelops = false;
        fix.HoldsForRelease = true;
        fix.StartOffsetSeconds = 0f;
        var fixState = default(ManeuverState);
        order = EnvelopmentManeuver.Step(ref fixState, FlankResponseTests.After(Held(fix), 10.0));
        AssertEqual(ManeuverPhase.Hold, order.Phase, "a fix with no planned release is held under the works hold");
        AssertEqual("works-hold", order.Reason, "for the works");
        AssertEqual(ManeuverPhase.Hold, EnvelopmentManeuver.Step(ref fixState, FlankResponseTests.After(Held(fix), 12.5)).Phase, "and past the ceiling");
        order = EnvelopmentManeuver.Step(ref fixState, FlankResponseTests.After(fix, 12.6));
        AssertEqual(ManeuverPhase.Released, order.Phase, "released when the hold clears");
        AssertTrue(fixState.HasWorksClear, "the clear is latched for the fix too");

        // A Defender never gets it.
        ManeuverInput defender = Held(delay);
        defender.Stance = BattleStance.Defender;
        var defenderState = default(ManeuverState);
        AssertEqual(ManeuverPhase.None, EnvelopmentManeuver.Step(ref defenderState, FlankResponseTests.After(defender, 10.0)).Phase, "no maneuver for a Defender");
    }

    static void WHoldLaunched()
    {
        // A wing already marching keeps marching: the flag is a Hold's, and the stored-product guard handles the rest.
        ManeuverInput input = FlankResponseTests.Maneuver();
        input.CenterContactHours = 10.0;
        var state = default(ManeuverState);
        EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.0));
        ManeuverOrder go = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.3));
        AssertEqual(ManeuverPhase.March, go.Phase, "marching");
        ManeuverOrder held = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(Held(input), 10.4));
        AssertEqual(ManeuverPhase.March, held.Phase, "the flag does not recall a marching wing");
        AssertFalse(state.HasWorksClear, "and it latches no clear");

        // A flag that was never set latches nothing.
        var plain = default(ManeuverState);
        EnvelopmentManeuver.Step(ref plain, FlankResponseTests.After(FlankResponseTests.Maneuver(), 10.0));
        EnvelopmentManeuver.Step(ref plain, FlankResponseTests.After(FlankResponseTests.Maneuver(), 10.5));
        AssertFalse(plain.HasWorksClear, "no hold, no clear");
    }

    static TacticalPhaseProgressionDoctrine.Input PhaseInput(BattlePhase phase, float ageSeconds = 0f, float global = 1.2f, float main = 1.2f,
        float history = 1.2f, float reserves = 0.1f, bool worksHold = false, float morale = 1f)
        => new TacticalPhaseProgressionDoctrine.Input(phase, ageSeconds, global, 1.0f, main, history, morale, 0.3f, reserves, 0.5f,
            TacticalSectorReadinessDoctrine.Result.PushReady, 1f, worksHold);

    static void WHoldGate()
    {
        float pastBudget = TacticalPhaseProgressionDoctrine.ProbeBudgetForAggression(0.5f) + 1f;

        // Without the hold the shipped rules: odds clear the floor, the probe commits; the budget commits too.
        AssertEqual(BattlePhase.MainEffort, TacticalPhaseProgressionDoctrine.Decide(PhaseInput(BattlePhase.Probe)).NextPhase, "shipped: odds commit the probe");
        AssertEqual(BattlePhase.MainEffort, TacticalPhaseProgressionDoctrine.Decide(PhaseInput(BattlePhase.Probe, pastBudget, global: 1.0f, main: 1.0f)).NextPhase,
            "shipped: the budget commits the probe");

        // With it the probe stays a probe, odds or budget.
        TacticalPhaseProgressionDoctrine.Decision byOdds = TacticalPhaseProgressionDoctrine.Decide(PhaseInput(BattlePhase.Probe, worksHold: true));
        AssertEqual(BattlePhase.Probe, byOdds.NextPhase, "works hold: favourable odds do not commit the probe");
        AssertEqual("works-hold", byOdds.Reason, "and say why");
        AssertEqual(BattlePhase.Probe, TacticalPhaseProgressionDoctrine.Decide(PhaseInput(BattlePhase.Probe, pastBudget, global: 1.0f, main: 1.0f, worksHold: true)).NextPhase,
            "works hold: nor does the probe budget");
        // Poor odds past the budget still consolidate: the hold blocks only the move into MainEffort.
        AssertEqual(BattlePhase.Consolidate, TacticalPhaseProgressionDoctrine.Decide(PhaseInput(BattlePhase.Probe, pastBudget, global: 0.5f, main: 0.5f, worksHold: true)).NextPhase,
            "a poor-odds probe still consolidates");

        // Consolidate does not resume into MainEffort.
        AssertEqual(BattlePhase.MainEffort, TacticalPhaseProgressionDoctrine.Decide(PhaseInput(BattlePhase.Consolidate, global: 1.5f)).NextPhase, "shipped: odds recovered, resume");
        TacticalPhaseProgressionDoctrine.Decision resume = TacticalPhaseProgressionDoctrine.Decide(PhaseInput(BattlePhase.Consolidate, global: 1.5f, worksHold: true));
        AssertEqual(BattlePhase.Consolidate, resume.NextPhase, "works hold: Consolidate stays");
        AssertEqual("works-hold", resume.Reason, "and says why");

        // A plan already in MainEffort or Exploit is not stepped back, and its own rules run.
        AssertEqual(BattlePhase.MainEffort, TacticalPhaseProgressionDoctrine.Decide(PhaseInput(BattlePhase.MainEffort, worksHold: true)).NextPhase, "MainEffort stays");
        AssertEqual("main-effort-continuing", TacticalPhaseProgressionDoctrine.Decide(PhaseInput(BattlePhase.MainEffort, worksHold: true)).Reason, "by its own rule");
        AssertEqual(BattlePhase.Exploit, TacticalPhaseProgressionDoctrine.Decide(PhaseInput(BattlePhase.MainEffort, global: 1.2f, main: 2.0f, reserves: 0.5f, worksHold: true)).NextPhase,
            "a breakthrough still exploits");
        AssertEqual(BattlePhase.Exploit, TacticalPhaseProgressionDoctrine.Decide(PhaseInput(BattlePhase.Exploit, worksHold: true)).NextPhase, "Exploit stays");
        AssertEqual(BattlePhase.Consolidate, TacticalPhaseProgressionDoctrine.Decide(PhaseInput(BattlePhase.MainEffort, global: 0.5f, reserves: 0.7f, worksHold: true)).NextPhase,
            "MainEffort still consolidates by its own rule");

        // Withdraw is never gated: the morale floor wins, and Withdraw stays absorbing.
        AssertEqual(BattlePhase.Withdraw, TacticalPhaseProgressionDoctrine.Decide(PhaseInput(BattlePhase.Probe, morale: 0.1f, worksHold: true)).NextPhase, "the morale floor still withdraws");
        AssertEqual(BattlePhase.Withdraw, TacticalPhaseProgressionDoctrine.Decide(PhaseInput(BattlePhase.Withdraw, worksHold: true)).NextPhase, "Withdraw stays");
    }

    static void WHoldWiring()
    {
        string play = FlankResponseTests.SourceText("Tactical", "Orchestrator", "Plays", "PlayExecutorRuntime.cs");
        FlankResponseTests.AssertPinned(play, "input.HoldForWorks = EnvelopmentManeuver.HoldForWorksFor(", "StepManeuvers sets the flag through the one rule");
        FlankResponseTests.AssertPinned(play, "feasibility.WorksHold(out", "from the ledger's hold and its reason");
        FlankResponseTests.AssertPinned(play, "feasibility.AssaultsWorks(", "for a body that assaults works");
        FlankResponseTests.AssertPinned(play, "!input.HoldForWorks", "and publishes no planned release for a held body");
        string army = FlankResponseTests.SourceText("Tactical", "Orchestrator", "ArmyOrchestrator.cs");
        FlankResponseTests.AssertPinned(army, "worksHold: WorksHoldOn", "the phase doctrine is told the hold");
    }
}
