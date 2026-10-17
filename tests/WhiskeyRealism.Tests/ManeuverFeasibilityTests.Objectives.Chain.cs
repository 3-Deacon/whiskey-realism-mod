using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using WhiskeyRealism;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static TestFixtures;
using static ManeuverFeasibilityFixtures;

/// <summary>BC14O task 5: the designer chains as a timed prior, and the chain contest (requirement-historical-chain).</summary>
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] ObjectiveChainTests() => new (string, Action)[]
    {
        ("bc14o HIST-PRIOR a designer chain's next objective gains the prior from its due hour and only then; the chain is read from the side's own AIBattle", HistPrior),
        ("bc14o HIST-PRIOR-ROW the prior reaches the objective's row and its score, once", HistPriorRow),
        ("bc14o OBJ-CHAIN-CONTEST vanilla's chain move against a Whiskey move under the plan is re-issued, once per distinct waypoint and per 0.05 h", ObjChainContest),
        ("bc14o OBJ-CHAIN-CONTEST-WIRING the posture pipeline asks the contest for a not-held group and sends the move again under the group's task", ObjChainContestWiring),
    };

    static AIBattle.ObjectiveChain Chain(int strategy, params (Objectives head, float due)[] entries)
    {
        var chain = new AIBattle.ObjectiveChain(strategy);
        foreach ((Objectives head, float due) in entries)
        {
            chain.objectives.Add(head);
            chain.objectivetimetomove.Add(due);
        }
        return chain;
    }

    static void HistPrior()
    {
        Objectives a = UninitialisedObjective("A", 1, 70), b = UninitialisedObjective("B", 1, 30), stranger = UninitialisedObjective("Z", 1, 5);
        Func<Objectives, string> idOf = o => ReferenceEquals(o, a) ? "A" : ReferenceEquals(o, b) ? "B" : null;
        var into = new string[8];
        // The battle hour is Hour + 24 x battlepasseddays: day 2, 14:00 is 38.
        float hours = 14f + 24f;

        AssertEqual(1, ObjectiveChains.CollectDue(new[] { Chain(3, (a, 38f)) }, hours, idOf, into), "a head due exactly now is due (the test is inclusive)");
        AssertEqual("A", into[0], "mapped to the capture's id by reference");
        AssertEqual(0, ObjectiveChains.CollectDue(new[] { Chain(3, (a, 38.01f)) }, hours, idOf, into), "before its due hour there is no prior");
        AssertEqual(1, ObjectiveChains.CollectDue(new[] { Chain(3, (a, 20f)) }, hours, idOf, into), "and after it there is");

        // Several chains naming one objective add it once; another chain's objective is its own.
        AssertEqual(2, ObjectiveChains.CollectDue(new[] { Chain(3, (a, 10f)), Chain(4, (a, 12f)), Chain(5, (b, 30f)) }, hours, idOf, into), "two chains naming A give A once, and B is another");
        AssertTrue(into.Take(2).Contains("A") && into.Take(2).Contains("B"), "A and B");

        // A chain switched to dynamic gives none, and a dynamic chain never does.
        AssertEqual(0, ObjectiveChains.CollectDue(new[] { Chain(-1, (a, 10f)) }, hours, idOf, into), "usedstrategyid -1 gives none");
        AssertEqual(0, ObjectiveChains.CollectDue(new[] { Chain(-1) }, hours, idOf, into), "a dynamic chain has no head");
        AssertEqual(0, ObjectiveChains.CollectDue(new[] { Chain(3) }, hours, idOf, into), "an emptied designer chain has none");

        // A null head is a timed wait: no prior until vanilla drops it and a real head is next.
        AIBattle.ObjectiveChain waiting = Chain(3, (null, 10f), (b, 12f));
        AssertEqual(0, ObjectiveChains.CollectDue(new[] { waiting }, hours, idOf, into), "a null head (a wait entry) gives no prior");
        waiting.objectives.RemoveAt(0);
        waiting.objectivetimetomove.RemoveAt(0);
        AssertEqual(1, ObjectiveChains.CollectDue(new[] { waiting }, hours, idOf, into), "the prior returns when the wait is dropped");
        AssertEqual("B", into[0], "on the next objective");

        // An objective that is not one of the battle's gives none; null chains are skipped.
        AssertEqual(0, ObjectiveChains.CollectDue(new[] { Chain(3, (stranger, 1f)) }, hours, idOf, into), "an unknown objective reference maps to nothing");
        AssertEqual(0, ObjectiveChains.CollectDue(new AIBattle.ObjectiveChain[] { null }, hours, idOf, into), "a null chain is skipped");
        AssertEqual(0, ObjectiveChains.CollectDue(null, hours, idOf, into), "no chains");

        // The side's own AIBattle: sideofai match, the feud instance skipped, never by position; no match is false.
        AssertEqual(1, ArmyEvidenceBuilder.SelectAiBattleIndexForSide(new[] { 0, 0, 1 }, new[] { 2, 0, 1 }, 0), "the feud instance first in aibattle is skipped");
        AssertEqual(-1, ArmyEvidenceBuilder.SelectAiBattleIndexForSide(new[] { 0 }, new[] { 2 }, 0), "only the feud instance: no match");
        AssertEqual(-1, ArmyEvidenceBuilder.SelectAiBattleIndexForSide(new[] { 1, 1 }, new[] { 1, 0 }, 0), "no sideofai match");
        string builder = ReadRepoFile("src/WhiskeyRealism/Tactical/Orchestrator/ArmyEvidenceBuilder.cs");
        string tryResolve = MethodBody(builder, "internal static bool TryResolveOwnAiBattle(");
        AssertTrue(tryResolve != null && tryResolve.Contains("ResolveSideFromAlliance(bunits, alliance)") && tryResolve.Contains("ResolveOwnAiBattle(bunits, side)") &&
            tryResolve.Contains("return battle != null;") && tryResolve.Contains("return false;"), "TryResolveOwnAiBattle maps no match to false, through the shared selector");
        string own = MethodBody(builder, "private static AIBattle ResolveOwnAiBattle(");
        AssertTrue(own != null && own.Contains("SelectAiBattleIndexForSide(") && !System.Text.RegularExpressions.Regex.IsMatch(own, @"aibattle\[\s*side\s*\]"),
            "the shared resolver goes through the selector, never aibattle[side]");
        string runtime = ReadRepoFile("src/WhiskeyRealism/Tactical/Battle/Objectives/ObjectiveRuntime.cs");
        AssertTrue(runtime.Contains("TryResolveOwnAiBattle(bunits, alliance, out AIBattle own)") && !runtime.Contains("ResolveAiBattleForAlliance"),
            "the runtime reads the chains only where the side's own AIBattle is found, never the fallback form");

        // The player's alliance outside ai_vs_ai: the commander (and so the evaluation that reads the chains) does not run for it.
        AssertFalse(TacticalBattleCoordinator.ShouldRunTacticalCommanderForSide(0, 0, false), "the player's alliance gets no commander outside ai_vs_ai, so no prior");
        AssertTrue(TacticalBattleCoordinator.ShouldRunTacticalCommanderForSide(0, 0, true), "control: in ai_vs_ai it does");
    }

    static void HistPriorRow()
    {
        FeasField f = ObjField();
        f.AddObjective("A", 0f, 900f, 100f);
        f.AddObjective("B", 0f, 700f, 100f);
        f.DueChains.Add("A");
        f.DueChains.Add("A");
        FeasibilityLedger ledger = f.Evaluate();
        ObjectiveEstimate a = Row(ledger, "A"), b = Row(ledger, "B");
        AssertNear(0.3f, a.Prior, 1e-6f, "the due chain's objective gains 0.3");
        AssertNear(0f, b.Prior, 1e-9f, "the other gains none");
        AssertNear(a.Value * (1f + ledger.Pressure.U) - ObjectiveScoring.SelectRiskWeight(ledger.Pressure) * a.Penalty + a.Prior + a.Hold, a.Score, 1e-5f, "and the prior is in its score");
        FeasField none = ObjField();
        none.AddObjective("A", 0f, 900f, 100f);
        AssertNear(0f, Row(none.Evaluate(), "A").Prior, 1e-9f, "before the chain is due the prior is 0");
    }

    static ContestInput ChainInput() => new ContestInput
    {
        Chain = true, HasPath = true, HasLastIssued = true, IssuedUnderPlan = true, LastSeenPath = true,
        LastSeenX = 100f, LastSeenZ = 100f, LastIssuedX = 100f, LastIssuedZ = 100f, WaypointX = 400f, WaypointZ = 400f, X = 90f, Z = 90f,
    };

    static ContestObservation ChainObservation(double now, float waypointX, float waypointZ, double issuedHours = 5.0, double planStart = 4.0) => new ContestObservation
    {
        Chain = true, HasPath = true, HasLastIssued = true, LastIssuedX = 100f, LastIssuedZ = 100f, LastIssuedHours = issuedHours, PlanStartHours = planStart,
        NowHours = now, WaypointX = waypointX, WaypointZ = waypointZ, X = 90f, Z = 90f,
    };

    static void ObjChainContest()
    {
        // Decide: a waypoint that moved more than 50 to a point that is not the move Whiskey issued is sent the move again.
        ContestInput move = ChainInput();
        AssertEqual(ContestAction.Reissue, MoveContest.Decide(in move), "vanilla's chain move against a Whiskey move under the plan: re-issued");
        ContestInput same = ChainInput();
        same.LastSeenX = 400f;
        same.LastSeenZ = 400f;
        AssertEqual(ContestAction.None, MoveContest.Decide(in same), "the same waypoint seen again: none");
        ContestInput near = ChainInput();
        near.WaypointX = 130f;
        near.WaypointZ = 100f;
        near.LastSeenX = 100f;
        AssertEqual(ContestAction.None, MoveContest.Decide(in near), "a waypoint within 50 of the last seen one is no new move");
        ContestInput ours = ChainInput();
        ours.WaypointX = 120f;
        ours.WaypointZ = 120f;
        ours.LastSeenX = 600f;
        AssertEqual(ContestAction.None, MoveContest.Decide(in ours), "a waypoint at the move Whiskey issued is Whiskey's own");
        ContestInput old = ChainInput();
        old.IssuedUnderPlan = false;
        AssertEqual(ContestAction.None, MoveContest.Decide(in old), "a move issued before the plan began is not defended");
        ContestInput hold = ChainInput();
        hold.HoldTask = true;
        AssertEqual(ContestAction.None, MoveContest.Decide(in hold), "a group on a hold task is not contested");
        ContestInput never = ChainInput();
        never.HasLastIssued = false;
        AssertEqual(ContestAction.None, MoveContest.Decide(in never), "a group with no Whiskey move has nothing to defend");
        ContestInput off = ChainInput();
        off.Chain = false;
        AssertEqual(ContestAction.None, MoveContest.Decide(in off), "the doctrine off or the group engaged: none");
        ContestInput recon = ChainInput();
        recon.Recon = true;
        AssertEqual(ContestAction.None, MoveContest.Decide(in recon), "a recon mission group is never contested");
        ContestInput idle = ChainInput();
        idle.HasPath = false;
        AssertEqual(ContestAction.None, MoveContest.Decide(in idle), "no path, no move");
        ContestInput firstSight = ChainInput();
        firstSight.LastSeenPath = false;
        AssertEqual(ContestAction.Reissue, MoveContest.Decide(in firstSight), "a path where there was none, at another point, is a new move");
        ContestInput held = ChainInput();
        held.Held = true;
        held.WaypointInBand = false;
        AssertEqual(ContestAction.None, MoveContest.Decide(in held), "a held group is the works contest's: a waypoint outside the band is none");
        AssertTrue(MoveContest.IsHoldTask(CommandTaskType.HoldObjective) && MoveContest.IsHoldTask(CommandTaskType.ReserveWait) && !MoveContest.IsHoldTask(CommandTaskType.AttackObjective)
            && !MoveContest.IsHoldTask(CommandTaskType.Screen), "the hold tasks");

        // The state: once per distinct new waypoint and per 0.05 h, counted, with memory apart from the works hold's.
        var state = MoveContestState.ForAlliance(0);
        state.TakeChainReissues();
        ContestObservation seed = ChainObservation(10.00, 100f, 100f);
        AssertEqual(ContestAction.None, state.Evaluate(7, 1, in seed, out _, out _), "Whiskey's own waypoint: none");
        ContestObservation chainMove = ChainObservation(10.01, 500f, 500f);
        ContestAction first = state.Evaluate(7, 2, in chainMove, out float tx, out float tz);
        AssertEqual(ContestAction.Reissue, first, "vanilla's chain move: re-issued");
        AssertTrue(Math.Abs(tx - 100f) < 1e-4f && Math.Abs(tz - 100f) < 1e-4f, "the target is the move Whiskey issued");
        ContestObservation again = ChainObservation(10.02, 500f, 500f);
        AssertEqual(ContestAction.None, state.Evaluate(7, 3, in again, out _, out _), "the same waypoint again: none");
        ContestObservation another = ChainObservation(10.03, 800f, 200f);
        AssertEqual(ContestAction.None, state.Evaluate(7, 4, in another, out _, out _), "another distinct waypoint within 0.05 h: bounded, none");
        ContestObservation backAtOurs = ChainObservation(10.05, 100f, 100f);
        AssertEqual(ContestAction.None, state.Evaluate(7, 5, in backAtOurs, out _, out _), "the re-issued move walking: none");
        ContestObservation resend = ChainObservation(10.30, 500f, 500f);
        AssertEqual(ContestAction.Reissue, state.Evaluate(7, 6, in resend, out _, out _), "when vanilla re-sends after the re-issued move ends, the contest answers again");
        AssertEqual(2, state.TakeChainReissues(), "each re-send is counted");
        AssertEqual(0, state.TakeChainReissues(), "and the count is taken");

        // A move before the plan began is not defended; a group the chain stops applying to forgets.
        ContestObservation stale = ChainObservation(11.0, 500f, 500f, issuedHours: 3.0, planStart: 4.0);
        AssertEqual(ContestAction.None, state.Evaluate(8, 1, in stale, out _, out _), "issued before the plan: none");
        ContestObservation off2 = ChainObservation(11.1, 500f, 500f);
        off2.Chain = false;
        AssertEqual(ContestAction.None, state.Evaluate(8, 2, in off2, out _, out _), "chain off: none");
        // The Reissue intent: the group's own task, the contest flag, the chain reason.
        BattleOrderIntent intent = PostureIntentPlan.ContestReissue(7, 100f, 100f, CommandTaskType.AttackObjective, IntentPriority.Executor, IntentSource.PostureExecutor);
        AssertEqual(OrderChannel.Move, intent.Channel, "a Move");
        AssertTrue((intent.Flags & IntentFlags.Contest) != 0, "carried with IntentFlags.Contest");
        AssertEqual("chain-contest", intent.Reason, "reason chain-contest");
        AssertEqual(CommandTaskType.AttackObjective, intent.Task, "under the group's own task");
        AssertTrue((intent.Flags & IntentFlags.UseOrderDelay) == 0, "no order delay");
    }

    static void ObjChainContestWiring()
    {
        string producer = ReadRepoFile("src/WhiskeyRealism/Tactical/Battle/Producers/PostureIntentProducer.cs");
        AssertTrue(producer.Contains("Plugin.EnemyWorksDoctrineEnabled() || Plugin.ObjectiveDoctrineEnabled()"), "the contest turn runs under either flag");
        string contest = MethodBody(producer, "private static int WorksContest(");
        AssertTrue(contest != null && contest.Contains("Chain = chain") && contest.Contains("!closeEngaged") && contest.Contains("case ContestAction.Reissue:") &&
            contest.Contains("PostureIntentPlan.ContestReissue(rec.InstanceId, holdX, holdZ, task, priority, source)"), "the Reissue answer is sent as the group's own task, and an engaged group is not contested");
        AssertTrue(contest.Contains("MoveContest.IsHoldTask(task)"), "a hold task is not contested");
    }
}
