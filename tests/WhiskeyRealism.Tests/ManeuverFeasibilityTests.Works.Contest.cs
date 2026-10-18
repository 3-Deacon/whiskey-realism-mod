using System;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Util;
using static TestHarness;

// BC14W task 7: the move contest (spec § 7A requirement-works-hold, "One contest rule for moves made outside the stored orders"; § 13.2 WORKS-CONTEST,
// WORKS-ADVANCE). The pipeline that calls it is a runtime file (not in the harness): its calls are pinned in the source, and the pure rule, the
// per-group memory and the intents it submits are exercised here.
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] WorksContestTests() => new (string, Action)[]
    {
        ("bc14w WORKS-CONTEST-DECIDE a new move into the works band is answered with the hold: a stop near the hold point, a move back beyond it; every other case is none", WContestDecide),
        ("bc14w WORKS-CONTEST-RECOVERY a held group's recovery into the works band is refused", WContestRecovery),
        ("bc14w WORKS-CONTEST-STATE the memory sees a waypoint once, contests once per tick, latches the hold point at the first hold and counts each contest", WContestState),
        ("bc14w WORKS-CONTEST-INTENTS the contest's stop and move are executor-priority, flagged Contest, and the move pays no order delay", WContestIntents),
        ("bc14w WORKS-CONTEST-PINS the posture pipeline asks the contest for each held group after its stance and plans a refused recovery as the hold", WContestPins),
        ("bc14w WORKS-ADVANCE the guarded orders of a group facing works give it no waypoint in the band, and the counts see an executor advance, a stance 4 and a band entry once", WAdvance),
    };

    // A band from z 400 on, the works themselves from z 500 on.
    sealed class StripWorks : IWorksQuery
    {
        public bool StrikeBrokenIn(float x, float z) => false;
        public bool OnHeldWorks(float x, float z) => z >= 500f;
        public bool InWorksBand(float x, float z) => z >= 400f;
    }

    static readonly IWorksQuery Strip = new StripWorks();

    static ContestInput HeldInput() => new ContestInput
    {
        Held = true, X = 100f, Z = 100f, HoldX = 100f, HoldZ = 100f, HasPath = true, WaypointX = 100f, WaypointZ = 450f, WaypointInBand = true,
    };

    static void WContestDecide()
    {
        // A vanilla move (a path where there was none) into the band, not a move Whiskey issued: the group at its hold point is stopped.
        ContestInput c = HeldInput();
        AssertEqual(ContestAction.HoldStop, MoveContest.Decide(in c), "within 60 of the hold point: a stop");
        c.X = 100f;
        c.Z = 160f;
        AssertEqual(ContestAction.HoldStop, MoveContest.Decide(in c), "60 away is still within the dedupe radius");
        c.Z = 161f;
        AssertEqual(ContestAction.HoldMove, MoveContest.Decide(in c), "beyond it: a move back to the hold point");

        // The same waypoint seen again: none. A move within 50 of the last seen one: none. Beyond 50: a new move.
        c = HeldInput();
        c.LastSeenPath = true;
        c.LastSeenX = 100f;
        c.LastSeenZ = 450f;
        AssertEqual(ContestAction.None, MoveContest.Decide(in c), "the same waypoint");
        c.LastSeenZ = 405f;
        AssertEqual(ContestAction.None, MoveContest.Decide(in c), "45 from the last seen");
        c.LastSeenZ = 399f;
        AssertEqual(ContestAction.HoldStop, MoveContest.Decide(in c), "51 from the last seen");

        // A new waypoint outside the band, a group with no path, a group that is not held and a recon group are never contested.
        c = HeldInput();
        c.WaypointInBand = false;
        AssertEqual(ContestAction.None, MoveContest.Decide(in c), "outside the band");
        c = HeldInput();
        c.HasPath = false;
        AssertEqual(ContestAction.None, MoveContest.Decide(in c), "no path, no move");
        c = HeldInput();
        c.Held = false;
        AssertEqual(ContestAction.None, MoveContest.Decide(in c), "not held");
        c = HeldInput();
        c.Recon = true;
        AssertEqual(ContestAction.None, MoveContest.Decide(in c), "a recon group");

        // A waypoint equal to (within 50 of) the move Whiskey last issued is Whiskey's own; farther away it is not.
        c = HeldInput();
        c.HasLastIssued = true;
        c.LastIssuedX = 100f;
        c.LastIssuedZ = 420f;
        AssertEqual(ContestAction.None, MoveContest.Decide(in c), "within 50 of the last issued move");
        c.LastIssuedZ = 390f;
        AssertEqual(ContestAction.HoldStop, MoveContest.Decide(in c), "60 from it: not Whiskey's");

        // A march under way at the hold's onset is turned back even though Whiskey issued it.
        c = HeldInput();
        c.HasLastIssued = true;
        c.LastIssuedX = 100f;
        c.LastIssuedZ = 450f;
        c.OnsetMoving = true;
        AssertEqual(ContestAction.HoldStop, MoveContest.Decide(in c), "the onset overrides the exemption");
    }

    static void WContestRecovery()
    {
        AssertFalse(MoveContest.RecoveryAllowed(true, 100f, 450f, Strip), "a held group's recovery into the band");
        AssertFalse(MoveContest.RecoveryAllowed(true, 100f, 600f, Strip), "or onto the works");
        AssertTrue(MoveContest.RecoveryAllowed(true, 100f, 300f, Strip), "a recovery outside the band");
        AssertTrue(MoveContest.RecoveryAllowed(false, 100f, 450f, Strip), "a group that is not held");
        AssertTrue(MoveContest.RecoveryAllowed(true, 100f, 450f, null), "no works query (the works doctrine off)");
    }

    static ContestObservation Obs(float x, float z, bool path = false, float wx = 0f, float wz = 0f, int stance = 1, bool held = true, bool recon = false,
        bool lastIssued = false, float lx = 0f, float lz = 0f)
        => new ContestObservation
        {
            Held = held, Recon = recon, X = x, Z = z, HasPath = path, WaypointX = wx, WaypointZ = wz, Stance = stance, Works = Strip,
            HasLastIssued = lastIssued, LastIssuedX = lx, LastIssuedZ = lz,
        };

    static void WContestState()
    {
        var state = new MoveContestState();
        const int g = 7;

        // Tick 1: the first hold latches the group's position as its hold point; nothing to contest.
        AssertEqual(ContestAction.None, state.Evaluate(g, 1, Obs(0f, 0f), out float hx, out float hz), "the first hold contests nothing");
        AssertTrue(hx == 0f && hz == 0f, "the hold point is where the group stood");

        // Tick 2: vanilla's chain move into the band.
        ContestObservation chain = Obs(0f, 20f, path: true, wx: 0f, wz: 450f);
        AssertEqual(ContestAction.HoldStop, state.Evaluate(g, 2, chain, out hx, out hz), "a vanilla move into the band is stopped near the hold point");
        AssertTrue(hx == 0f && hz == 0f, "the latched hold point is returned");
        AssertEqual(1, state.VanillaChainContests, "counted as a vanilla contest");
        AssertEqual(0, state.OnsetContests, "not an onset");
        AssertEqual(ContestAction.None, state.Evaluate(g, 2, chain, out _, out _), "one contest per group per tick");

        // Tick 3: the stop took (no path); the stale waypoint is the same: none.
        AssertEqual(ContestAction.None, state.Evaluate(g, 3, Obs(0f, 25f, path: false, wx: 0f, wz: 450f), out _, out _), "the stop took");
        // Tick 4: vanilla re-issues the same waypoint (a path where there was none): contested again.
        AssertEqual(ContestAction.HoldStop, state.Evaluate(g, 4, chain, out _, out _), "a path where there was none");
        AssertEqual(2, state.VanillaChainContests, "counted again");
        // Tick 5: the group is moving on the same waypoint: seen already.
        AssertEqual(ContestAction.None, state.Evaluate(g, 5, Obs(0f, 90f, path: true, wx: 0f, wz: 450f), out _, out _), "the same waypoint seen again");
        // Tick 6: farther from the hold point than the dedupe radius, a new vanilla waypoint: a move back.
        AssertEqual(ContestAction.HoldMove, state.Evaluate(g, 6, Obs(0f, 200f, path: true, wx: 0f, wz: 600f), out hx, out hz), "beyond 60 m of the hold point");
        AssertTrue(hx == 0f && hz == 0f, "back to the latched point");
        AssertEqual(3, state.VanillaChainContests, "three vanilla contests");
        // The move back is Whiskey's own: the next waypoint equals the executor's last issued move.
        AssertEqual(ContestAction.None, state.Evaluate(g, 7, Obs(0f, 150f, path: true, wx: 0f, wz: 0f, lastIssued: true, lx: 0f, lz: 0f), out _, out _),
            "the contest's own move back is not contested");

        // A group that stops being held forgets its hold point: the next hold is a new onset.
        state.Evaluate(g, 8, Obs(500f, 0f, held: false), out _, out _);
        state.Evaluate(g, 9, Obs(500f, 10f), out hx, out hz);
        AssertTrue(hx == 500f && hz == 10f, "re-held: a new hold point");

        // An onset: a march under way into the band when the hold begins is turned back and counted as an onset, not as a vanilla contest or an advance.
        var onset = new MoveContestState();
        ContestObservation march = Obs(0f, 100f, path: true, wx: 0f, wz: 450f, lastIssued: true, lx: 0f, lz: 450f);
        AssertEqual(ContestAction.HoldStop, onset.Evaluate(8, 1, march, out _, out _), "at the hold's onset the march is turned back");
        AssertEqual(1, onset.OnsetContests, "counted as an onset");
        AssertEqual(0, onset.VanillaChainContests, "not as a vanilla contest");
        AssertEqual(0, onset.AdvancesOnWorks, "nor as an advance");

        // A recon group and a Europe-sized junk id are never contested or counted.
        var recon = new MoveContestState();
        AssertEqual(ContestAction.None, recon.Evaluate(9, 1, Obs(0f, 100f, path: true, wx: 0f, wz: 450f, recon: true), out _, out _), "a recon group");
        AssertEqual(0, recon.VanillaChainContests + recon.OnsetContests + recon.AdvancesOnWorks + recon.BandEntries, "and nothing is counted for it");

        // The per-alliance state is bound-checked and BattleScope resets it.
        AssertTrue(MoveContestState.ForAlliance(0) != null && MoveContestState.ForAlliance(1) != null, "alliances 0 and 1");
        AssertTrue(MoveContestState.ForAlliance(0) != MoveContestState.ForAlliance(1), "one per side");
        AssertTrue(MoveContestState.ForAlliance(2) == null && MoveContestState.ForAlliance(-1) == null, "Europe and negative alliances have none");
        MoveContestState.ForAlliance(0).Evaluate(1, 1, Obs(0f, 0f), out _, out _);
        MoveContestState.ForAlliance(0).Evaluate(1, 2, Obs(0f, 20f, path: true, wx: 0f, wz: 450f), out _, out _);
        AssertEqual(1, MoveContestState.ForAlliance(0).VanillaChainContests, "counted");
        BattleScope.ResetAll();
        AssertEqual(0, MoveContestState.ForAlliance(0).VanillaChainContests, "BattleScope clears the counts");
        AssertEqual(ContestAction.HoldStop, MoveContestState.ForAlliance(0).Evaluate(1, 3, Obs(0f, 20f, path: true, wx: 0f, wz: 450f), out _, out _),
            "and the memory: a first look at a march into the band is an onset");
        AssertEqual(1, MoveContestState.ForAlliance(0).OnsetContests, "counted as one");
        AssertEqual(0, MoveContestState.ForAlliance(0).VanillaChainContests, "and not as a vanilla contest");
        BattleScope.ResetAll();
    }

    static void WContestIntents()
    {
        BattleOrderIntent stop = PostureIntentPlan.ContestStop(42, IntentPriority.Executor, IntentSource.PostureExecutor);
        AssertEqual(OrderChannel.Stop, stop.Channel, "a stop");
        AssertEqual(42, stop.GroupId, "of the group");
        AssertEqual(IntentPriority.Executor, stop.Priority, "at executor priority");
        AssertTrue((stop.Flags & IntentFlags.Contest) != 0, "flagged Contest");
        AssertEqual(PostureIntentPlan.ContestReason, stop.Reason, "with the contest's reason");
        AssertEqual(CommandTaskType.HoldObjective, stop.Task, "as a hold");

        BattleOrderIntent move = PostureIntentPlan.ContestMove(42, 10f, 20f, IntentPriority.Executor, IntentSource.PostureExecutor);
        AssertEqual(OrderChannel.Move, move.Channel, "a move");
        AssertTrue(move.TargetX == 10f && move.TargetZ == 20f, "to the hold point");
        AssertTrue((move.Flags & IntentFlags.Contest) != 0, "flagged Contest");
        AssertTrue((move.Flags & IntentFlags.UseOrderDelay) == 0, "and it pays no order delay: it restores the standing order");
        AssertEqual(CommandTaskType.HoldObjective, move.Task, "as a hold");
        AssertEqual("works-hold-contest", PostureIntentPlan.ContestReason, "the reason's text");
    }

    static void WContestPins()
    {
        string producer = FlankResponseTests.SourceText("Tactical", "Battle", "Producers", "PostureIntentProducer.cs");
        string run = FlankResponseTests.Between(producer, "internal static bool Run(", "// #61 CanBypassStaleQueuedOrder");
        int stance = run.IndexOf("SubmitWorksHoldStance", StringComparison.Ordinal);
        int contest = run.IndexOf("= WorksContest(", StringComparison.Ordinal);
        int decide = run.IndexOf("CommandPostureExecutor.Decide(order", StringComparison.Ordinal);
        AssertTrue(stance >= 0 && contest > stance && decide > contest, "the contest runs after the works-hold stance and before the posture decision");
        // Regression: the contest's early return recorded no posture for the group (recon's ledger-task test, the facing pulse and fire control read it).
        string contested = FlankResponseTests.Between(run, "= WorksContest(", "// BC9: a probe that ended short");
        FlankResponseTests.AssertPinned(contested, "contestedGroup.Has = true;", "the contest branch records the group's posture");
        FlankResponseTests.AssertPinned(contested, "contestedGroup.State = state;", "its state");
        AssertTrue(contested.IndexOf("contestedGroup.Has = true;", StringComparison.Ordinal) < contested.IndexOf("return true;", StringComparison.Ordinal),
            "before the contest returns");
        // The turn itself (BC14O moved it into the pure ContestTurn so the harness can run it): the side's memory, once per arbiter tick, the answers.
        string turn = FlankResponseTests.SourceText("Tactical", "Battle", "Works", "ContestTurn.cs");
        FlankResponseTests.AssertPinned(run, "ContestTurn.Run(", "the producer runs the turn");
        FlankResponseTests.AssertPinned(turn, "MoveContestState.ForAlliance(f.Alliance)", "the side's memory");
        FlankResponseTests.AssertPinned(turn, ".Evaluate(f.GroupId, f.Tick,", "asked for each held group once per arbiter tick");
        FlankResponseTests.AssertPinned(turn, "PostureIntentPlan.ContestStop(", "a stop near the hold point");
        FlankResponseTests.AssertPinned(turn, "PostureIntentPlan.ContestMove(", "a move back beyond it");
        FlankResponseTests.AssertPinned(run, "Plugin.EnemyWorksDoctrineEnabled()", "behind Enable Enemy Works Doctrine");
        FlankResponseTests.AssertPinned(run, "ReconIntentProducer.MissionScoutId(", "a recon mission group is not contested");
        FlankResponseTests.AssertPinned(run, "executor.LastIssuedMove(", "against the move Whiskey last issued");

        // The recovery: after the target resolves, a refused recovery becomes the hold, and its decision clears the interrupted flag.
        int resolve = run.IndexOf("PostureTargets.TryResolve(", StringComparison.Ordinal);
        int recovery = run.IndexOf("MoveContest.RecoveryAllowed(", StringComparison.Ordinal);
        AssertTrue(resolve >= 0 && recovery > resolve, "RecoveryAllowed is asked after the target resolves");
        FlankResponseTests.AssertPinned(run, "PostureExecutionTarget.RecoveryPath", "for a recovery");
        FlankResponseTests.AssertPinned(run, "new PostureExecutionDecision(PostureExecutionAction.SetFormation, \"works-hold-recovery\"", "the plan becomes the hold: a formation, no waypoint");
        FlankResponseTests.AssertPinned(run, "clearInterruptedPaths: true", "and the decision clears the interrupted flag");

        string executor = FlankResponseTests.SourceText("Tactical", "Battle", "OrderExecutor.cs");
        string cooldown = FlankResponseTests.Between(executor, "private bool InCooldown(", "private bool TryRotatedTarget(");
        FlankResponseTests.AssertPinned(cooldown, "(w.Flags & IntentFlags.Contest) != 0", "the executor's cooldown knows the contest flag");
        string runtime = FlankResponseTests.SourceText("Tactical", "Battle", "Producers", "ReconIntentProducer.cs");
        FlankResponseTests.AssertPinned(runtime, "internal static int MissionScoutId(", "the recon producer names its mission scout");
    }

    static void WAdvance()
    {
        // The guarded orders of the works-facing child's nodes give no waypoint: a formation only (today's code, the ledger's own list, gives the
        // 75-unit waypoint at the line objective, which lies on the works profile).
        var lookup = new HoldLookup { Hold = true };
        lookup.Faces.Add("child-2");
        ArmyOrchestrator army = TwoCorpsArmy(lookup);
        TacticalOperationsLedgerRuntime ledger = ActiveLedger();
        army.UpdateOperationsLedger(ledger, FourStates());
        var physical = new CommandPhysicalState(false, false, false, 0, false, 0);
        var eligibility = new WriteEligibilitySnapshot(modeAllowsWrites: true, playerProtected: false, routed: false, orderPending: false, recentOrder: false);
        foreach (string node in new[] { "node-2", "node-10" })
        {
            CommandDoctrineOrder unguarded = OrderFor(ledger.CurrentDoctrineOrders, node);
            CommandDoctrineOrder guarded = OrderFor(army.CurrentDoctrineOrders, node);
            AssertEqual(PostureExecutionAction.SetFormationAndWaypoint, CommandPostureExecutor.Decide(unguarded, physical, eligibility, 10f).Action,
                node + ": the unguarded order walks to the objective");
            AssertEqual(PostureExecutionAction.SetFormation, CommandPostureExecutor.Decide(guarded, physical, eligibility, 10f).Action,
                node + ": the guarded order is a formation and no waypoint");
        }

        // The counts. An executor-issued waypoint entering the band is an advance, once per entry.
        var s = new MoveContestState();
        s.Evaluate(1, 1, Obs(0f, 0f), out _, out _);
        ContestObservation issued = Obs(0f, 10f, path: true, wx: 0f, wz: 450f, lastIssued: true, lx: 0f, lz: 450f);
        s.Evaluate(1, 2, issued, out _, out _);
        AssertEqual(1, s.AdvancesOnWorks, "an executor-issued move into the band is an advance");
        AssertEqual(0, s.VanillaChainContests, "and not a contest (it is Whiskey's own)");
        s.Evaluate(1, 3, issued, out _, out _);
        AssertEqual(1, s.AdvancesOnWorks, "counted once per entry");
        s.Evaluate(1, 4, Obs(0f, 10f, path: false, wx: 0f, wz: 100f), out _, out _);
        s.Evaluate(1, 5, issued, out _, out _);
        AssertEqual(2, s.AdvancesOnWorks, "leaving and re-entering is a new entry");

        // A group placed at stance 4 counts one, once.
        var stance = new MoveContestState();
        stance.Evaluate(2, 1, Obs(0f, 0f, stance: 2), out _, out _);
        stance.Evaluate(2, 2, Obs(0f, 0f, stance: 4), out _, out _);
        AssertEqual(1, stance.AdvancesOnWorks, "stance 4 counts");
        stance.Evaluate(2, 3, Obs(0f, 0f, stance: 4), out _, out _);
        AssertEqual(1, stance.AdvancesOnWorks, "once");
        stance.Evaluate(2, 4, Obs(0f, 0f, stance: 2), out _, out _);
        stance.Evaluate(2, 5, Obs(0f, 0f, stance: 4), out _, out _);
        AssertEqual(2, stance.AdvancesOnWorks, "and again after it left stance 4");

        // A group already in the band at the hold's start counts only after leaving and re-entering.
        var inside = new MoveContestState();
        inside.Evaluate(3, 1, Obs(0f, 450f, stance: 4), out _, out _);
        inside.Evaluate(3, 2, Obs(0f, 450f, stance: 4), out _, out _);
        inside.Evaluate(3, 3, Obs(0f, 450f, stance: 4), out _, out _);
        AssertEqual(0, inside.AdvancesOnWorks, "already in at the start: no advance");
        AssertEqual(0, inside.BandEntries, "and no entry");
        inside.Evaluate(3, 4, Obs(0f, 300f, stance: 2), out _, out _);
        inside.Evaluate(3, 5, Obs(0f, 450f, stance: 2), out _, out _);
        AssertEqual(0, inside.BandEntries, "a position in the band for one observation is a touch, not an entry");
        inside.Evaluate(3, 6, Obs(0f, 450f, stance: 2), out _, out _);
        AssertEqual(1, inside.BandEntries, "still inside one tick later: an entry");
        inside.Evaluate(3, 7, Obs(0f, 450f, stance: 2), out _, out _);
        AssertEqual(1, inside.BandEntries, "once");

        // A group that touches the band and is turned back is no entry; a recon group counts for nothing.
        var touch = new MoveContestState();
        touch.Evaluate(4, 1, Obs(0f, 300f), out _, out _);
        touch.Evaluate(4, 2, Obs(0f, 410f), out _, out _);
        touch.Evaluate(4, 3, Obs(0f, 350f), out _, out _);
        AssertEqual(0, touch.BandEntries, "turned back after one tick");
        var recon = new MoveContestState();
        recon.Evaluate(5, 1, Obs(0f, 0f, recon: true), out _, out _);
        recon.Evaluate(5, 2, Obs(0f, 450f, stance: 4, recon: true, path: true, wx: 0f, wz: 450f, lastIssued: true, lx: 0f, lz: 450f), out _, out _);
        AssertEqual(0, recon.AdvancesOnWorks + recon.BandEntries, "a recon scout does not count");
    }
}
