using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using WhiskeyRealism.Util;
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
        ("bc14o OBJ-CHAIN-CONTEST-WIRING the posture pipeline runs the contest turn for a not-held group, which sends the move again under the group's task", ObjChainContestWiring),
        ("bc14o OBJ-CHAIN-GROUPS only a due designer chain's own group, near its head objective, is contested; vanilla's march to the guns, assault and retreat are not", ObjChainGroups),
        ("bc14o OBJ-CHAIN-FLAG the contest turn submits a re-issue only with Enable Objective Doctrine on, and the works contest does not need the flag", ObjChainFlag),
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
        Chain = true, ChainMove = true, HasPath = true, HasLastIssued = true, IssuedUnderPlan = true, LastSeenPath = true,
        LastSeenX = 100f, LastSeenZ = 100f, LastIssuedX = 100f, LastIssuedZ = 100f, WaypointX = 400f, WaypointZ = 400f, X = 90f, Z = 90f,
    };

    static ContestObservation ChainObservation(double now, float waypointX, float waypointZ, double issuedHours = 5.0, double planStart = 4.0) => new ContestObservation
    {
        Chain = true, HasPath = true, HasLastIssued = true, LastIssuedX = 100f, LastIssuedZ = 100f, LastIssuedHours = issuedHours, PlanStartHours = planStart,
        NowHours = now, WaypointX = waypointX, WaypointZ = waypointZ, X = 90f, Z = 90f,
        // The chain's head stands where vanilla sends the group: at the waypoint.
        HasChainObjective = true, ChainObjectiveX = waypointX, ChainObjectiveZ = waypointZ,
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
        // The producer builds the facts and runs the turn; the turn is pure and run below (OBJ-CHAIN-FLAG, OBJ-CHAIN-GROUPS).
        string producer = ReadRepoFile("src/WhiskeyRealism/Tactical/Battle/Producers/PostureIntentProducer.cs");
        AssertTrue(producer.Contains("Plugin.EnemyWorksDoctrineEnabled() || Plugin.ObjectiveDoctrineEnabled()"), "the contest turn runs under either flag");
        string contest = MethodBody(producer, "private static int WorksContest(");
        AssertTrue(contest != null && contest.Contains("CloseEngaged = closeEngaged") && contest.Contains("Task = task") && contest.Contains("ContestTurn.Run(in facts, sink)"),
            "the producer hands the turn the group's task and whether it is engaged");
    }

    // A recording sink for the contest turn.
    sealed class TurnSink : IIntentSink
    {
        public readonly List<BattleOrderIntent> Intents = new List<BattleOrderIntent>();
        public void Submit(in BattleOrderIntent intent) => Intents.Add(intent);
    }

    // A due chain's own group at 700 (group 41) with its head at (500, 500); the move Whiskey issued it, at (100, 100) 1 h ago, under a plan 2 h old.
    static ContestTurnFacts TurnFacts(int group, long tick, double now, float waypointX, float waypointZ, bool engaged = false, CommandTaskType task = CommandTaskType.AttackObjective)
        => new ContestTurnFacts
        {
            Alliance = 0, GroupId = group, Tick = tick, Task = task, Priority = IntentPriority.Executor, Source = IntentSource.PostureExecutor,
            CloseEngaged = engaged, HasPath = true, HasLastIssued = true, IssuedX = 100f, IssuedZ = 100f, IssuedHours = now - 1.0, PlanStartHours = now - 2.0, NowHours = now,
            X = 90f, Z = 90f, WaypointX = waypointX, WaypointZ = waypointZ,
        };

    static void ObjChainFlag()
    {
        bool before = Plugin.ObjectiveDoctrine;
        DueChainTable due = DueChainTable.ForAlliance(0);
        MoveContestState.ForAlliance(0).TakeChainReissues();
        try
        {
            due.Clear();
            due.Add(41, 500f, 500f);
            // Doctrine on: the group first seen without a path is not a new move; then vanilla's chain move to the head: one re-issue of Whiskey's move.
            Plugin.ObjectiveDoctrine = true;
            var quiet = new TurnSink();
            ContestTurnFacts idle = TurnFacts(41, 1, 10.0, 100f, 100f);
            AssertEqual(0, ContestTurn.Run(in idle, quiet), "the group walks Whiskey's own move: nothing");
            var on = new TurnSink();
            ContestTurnFacts moved = TurnFacts(41, 2, 10.1, 505f, 495f);
            AssertEqual(1, ContestTurn.Run(in moved, on), "doctrine on: vanilla's chain move to the head is answered");
            AssertEqual(OrderChannel.Move, on.Intents[0].Channel, "with a Move");
            AssertTrue((on.Intents[0].Flags & IntentFlags.Contest) != 0 && on.Intents[0].Reason == "chain-contest", "flagged Contest, reason chain-contest");
            AssertTrue(on.Intents[0].TargetX == 100f && on.Intents[0].TargetZ == 100f, "back to the move Whiskey issued");
            AssertEqual(CommandTaskType.AttackObjective, on.Intents[0].Task, "under the group's own task");

            // Doctrine off: the same facts, the same group, the same table: no contest Move.
            MoveContestState.ForAlliance(0).TakeChainReissues();
            Plugin.ObjectiveDoctrine = false;
            var off = new TurnSink();
            ContestTurnFacts again = TurnFacts(41, 3, 11.0, 100f, 100f);
            ContestTurn.Run(in again, off);
            ContestTurnFacts movedOff = TurnFacts(41, 4, 11.1, 505f, 495f);
            AssertEqual(0, ContestTurn.Run(in movedOff, off), "doctrine off: vanilla's chain move is left alone");
            AssertEqual(0, off.Intents.Count, "and no intent is submitted at all");
            AssertEqual(0, MoveContestState.ForAlliance(0).TakeChainReissues(), "nor counted");

            // The works contest is the works doctrine's: a held group walking into the band is stopped with the objective doctrine off.
            var works = new TurnSink();
            ContestTurnFacts held = TurnFacts(52, 5, 12.0, 100f, 100f);
            held.Held = true; held.Works = Strip; held.HasPath = false; held.HasLastIssued = false; held.X = 100f; held.Z = 100f;
            ContestTurn.Run(in held, works);
            ContestTurnFacts walks = held;
            walks.Tick = 6; walks.HasPath = true; walks.WaypointX = 100f; walks.WaypointZ = 450f; walks.Z = 120f;
            AssertEqual(1, ContestTurn.Run(in walks, works), "a held group walking into the works band is contested with the objective doctrine off");
            AssertEqual(OrderChannel.Stop, works.Intents[0].Channel, "with the hold's stop");
        }
        finally
        {
            Plugin.ObjectiveDoctrine = before;
            due.Clear();
            MoveContestState.ForAlliance(0).TakeChainReissues();
            BattleScope.ResetAll();
        }
    }

    static void ObjChainGroups()
    {
        // The table: a due chain's own group and where its head stands; dynamic chains, wait entries, undue heads, unknown heads and missing groups add none.
        Objectives a = UninitialisedObjective("A", 1, 70), b = UninitialisedObjective("B", 1, 30), unknown = UninitialisedObjective("Z", 1, 5);
        Regiment g1 = (Regiment)RuntimeHelpers.GetUninitializedObject(typeof(Regiment)), g2 = (Regiment)RuntimeHelpers.GetUninitializedObject(typeof(Regiment));
        Func<Objectives, string> idOf = o => ReferenceEquals(o, a) ? "A" : ReferenceEquals(o, b) ? "B" : null;
        Func<Regiment, int> groupIdOf = r => ReferenceEquals(r, g1) ? 41 : ReferenceEquals(r, g2) ? 42 : 0;
        DueChainTable.PointLookup pointOf = (string id, out float x, out float z) =>
        {
            x = id == "A" ? 500f : 800f;
            z = id == "A" ? 500f : 200f;
            return id == "A" || id == "B";
        };
        var table = new DueChainTable();
        AIBattle.ObjectiveChain due = Chain(3, (a, 10f)), later = Chain(3, (b, 99f)), dynamic = Chain(-1, (b, 10f)), stranger = Chain(3, (unknown, 10f)), noGroup = Chain(3, (b, 10f));
        due.linegroup_centerunit = g1;
        later.linegroup_centerunit = g2;
        dynamic.linegroup_centerunit = g2;
        stranger.linegroup_centerunit = g2;
        // noGroup has no linegroup_centerunit.
        AssertEqual(1, ObjectiveChains.FillGroups(new[] { due, later, dynamic, stranger, noGroup }, 38f, idOf, groupIdOf, pointOf, table), "one due chain with a group and a known head");
        AssertTrue(table.TryFind(41, out float ox, out float oz) && ox == 500f && oz == 500f, "its group and where its head stands");
        AssertFalse(table.TryFind(42, out _, out _), "the others' groups are not in it");
        // A null head (a wait entry) adds none until vanilla drops it.
        AIBattle.ObjectiveChain waiting = Chain(3, (null, 10f), (b, 12f));
        waiting.linegroup_centerunit = g2;
        AssertEqual(0, ObjectiveChains.FillGroups(new[] { waiting }, 38f, idOf, groupIdOf, pointOf, table), "a wait entry names no head");
        AssertEqual(0, table.Count, "the table was cleared first");

        // The decision: an idle group 200 m from the fight that vanilla's march to the sound of guns moved (no chain, no head near the waypoint) is not
        // contested even though Whiskey moved it under this plan; the due chain's own group moved to its head is.
        ContestInput guns = ChainInput();
        guns.ChainMove = false;
        AssertEqual(ContestAction.None, MoveContest.Decide(in guns), "a march to the sound of guns is vanilla's reaction to the fight: not contested");
        ContestInput toHead = ChainInput();
        AssertEqual(ContestAction.Reissue, MoveContest.Decide(in toHead), "a due chain's own move to its head is");

        var state = new MoveContestState();
        ContestObservation march = ChainObservation(10.0, 500f, 500f);       // the chain's head is not at the waypoint for these two groups
        march.HasChainObjective = false;
        AssertEqual(ContestAction.None, state.Evaluate(61, 1, in march, out _, out _), "a group with no chain: the memory has nothing to compare");
        ContestObservation guns2 = ChainObservation(10.2, 560f, 100f);
        guns2.HasChainObjective = false;
        AssertEqual(ContestAction.None, state.Evaluate(61, 2, in guns2, out _, out _), "an idle group 200 m from the fight, sent to the guns, is left to vanilla");
        ContestObservation chainGroup = ChainObservation(10.3, 500f, 500f);
        chainGroup.ChainObjectiveX = 500f;
        chainGroup.ChainObjectiveZ = 500f;
        AssertEqual(ContestAction.Reissue, state.Evaluate(62, 1, in chainGroup, out _, out _), "a due chain's own group sent to its head is re-issued");
        // The same chain group sent to the guns, 400 m from its head, is not.
        ContestObservation chainToGuns = ChainObservation(10.6, 900f, 100f);
        chainToGuns.ChainObjectiveX = 500f;
        chainToGuns.ChainObjectiveZ = 500f;
        AssertEqual(ContestAction.None, state.Evaluate(62, 2, in chainToGuns, out _, out _), "the chain's own group moved by vanilla's march to the guns, away from its head, is not contested");
        // A move that ends 200 m from the head is not near it (the radius is 150), and one at 85 m is.
        ContestObservation far = ChainObservation(10.7, 500f, 700f);
        far.ChainObjectiveX = 500f;
        far.ChainObjectiveZ = 500f;
        AssertEqual(ContestAction.None, state.Evaluate(63, 1, in far, out _, out _), "a waypoint 200 m from the head is not the chain's move");
        // A retreat that ends 50 m from the head but farther than the group stands is not the chain's move; the same waypoint for a group 200 m out closes.
        ContestObservation retreat = ChainObservation(10.8, 500f, 450f);
        retreat.X = 500f; retreat.Z = 520f;
        retreat.ChainObjectiveX = 500f;
        retreat.ChainObjectiveZ = 500f;
        AssertEqual(ContestAction.None, state.Evaluate(64, 1, in retreat, out _, out _), "a retreat ending 50 m from the head, from 20 m, moves away from it: not contested");
        ContestObservation closing = ChainObservation(10.8, 500f, 450f);
        closing.X = 500f; closing.Z = 700f;
        closing.ChainObjectiveX = 500f;
        closing.ChainObjectiveZ = 500f;
        AssertEqual(ContestAction.Reissue, state.Evaluate(65, 1, in closing, out _, out _), "the same waypoint for a group 200 m out closes on the head: contested");
        // The verifier's probe geometry (a group 100 m beyond the head, a retreat to 50 m from it) gets nearer the head by distance, so it closes.
        ContestObservation probe = ChainObservation(10.9, 500f, 450f);
        probe.X = 500f; probe.Z = 600f;
        probe.ChainObjectiveX = 500f;
        probe.ChainObjectiveZ = 500f;
        AssertEqual(ContestAction.Reissue, state.Evaluate(66, 1, in probe, out _, out _), "distance alone cannot tell the verifier's probe from a close: it ends nearer the head and is contested");
        ContestObservation shifted = ChainObservation(10.9, 560f, 560f);
        shifted.ChainObjectiveX = 500f;
        shifted.ChainObjectiveZ = 500f;
        AssertEqual(ContestAction.Reissue, state.Evaluate(62, 3, in shifted, out _, out _), "a waypoint 85 m from the head is still the chain's (vanilla's spacing shift)");

        // Through the turn: the table decides which groups the contest sees at all.
        bool before = Plugin.ObjectiveDoctrine;
        DueChainTable alliance = DueChainTable.ForAlliance(0);
        try
        {
            Plugin.ObjectiveDoctrine = true;
            alliance.Clear();
            alliance.Add(71, 500f, 500f);
            var sink = new TurnSink();
            ContestTurnFacts idle71 = TurnFacts(71, 1, 20.0, 100f, 100f), idle72 = TurnFacts(72, 1, 20.0, 100f, 100f);
            ContestTurn.Run(in idle71, sink);
            ContestTurn.Run(in idle72, sink);
            ContestTurnFacts chain71 = TurnFacts(71, 2, 20.1, 500f, 500f), guns72 = TurnFacts(72, 2, 20.1, 500f, 500f);
            AssertEqual(1, ContestTurn.Run(in chain71, sink), "the table's group moved to its head: contested");
            AssertEqual(0, ContestTurn.Run(in guns72, sink), "another group moved to the very same place by vanilla: not in the table, not contested");
            ContestTurnFacts engaged71 = TurnFacts(71, 3, 20.3, 560f, 520f, engaged: true);   // a new move near the head, 63 m from the last waypoint seen
            AssertEqual(0, ContestTurn.Run(in engaged71, sink), "a chain group in close contact is left to vanilla's reaction");
        }
        finally
        {
            Plugin.ObjectiveDoctrine = before;
            alliance.Clear();
            BattleScope.ResetAll();
        }

        // The runtime fills the table with the evaluation and clears it when the doctrine is off or the chains cannot be read.
        string runtime = ReadRepoFile("src/WhiskeyRealism/Tactical/Battle/Objectives/ObjectiveRuntime.cs");
        AssertTrue(runtime.Contains("groups?.Clear();") && runtime.Contains("ObjectiveChains.FillGroups(own.objectivechain,"), "the evaluation refreshes the chains' groups and clears them when it cannot");
    }
}
