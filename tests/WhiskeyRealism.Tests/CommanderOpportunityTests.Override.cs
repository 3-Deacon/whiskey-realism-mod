using System;
using System.IO;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Util;
using static TestHarness;

// BC13 task 7, the pure halves and source pins of the exploit override in the posture pipeline (spec § 6,
// requirement-exploit-override). The pipeline itself is runtime (PosturePipeline.Run needs Unity types), so what can be
// tested without the game is tested through PostureIntentPlan, ExploitPosture and the arbiter priorities, and the shape of
// the runtime file is pinned at the points the spec names.
internal static partial class CommanderOpportunityTests
{
    private static (string, Action)[] OverrideTests()
    {
        return new (string, Action)[]
        {
            ("bc13 OVR-POSTURE an exploit Move is not delayed; a ledger Move still is", OvrPostureNoDelay),
            ("bc13 OVR-POSTURE the override state keeps the ledger identity and takes the exploit role and task", OvrOverlay),
            ("bc13 OVR-FOLLOW a follow order is its own kind and a follower keeps its ledger state", OvrFollowKind),
            ("bc13 OVR-FOLLOW a follower submits no Move and no Formation (pipeline pin)", OvrFollowPin),
            ("bc13 OVR-POSTURE the pipeline skips probe, GT recon, cavalry follow, army opportunity and the probe hold for an exploit (pin)", OvrSkipsPin),
            ("bc13 OVR-FEEDBACK the flank emergency of an exploit order is reported to the opportunity producer (pin)", OvrFeedbackPin),
            ("bc13 OVR-LEAF a leaf brigade asset runs the pipeline with the override before the slot path (pin)", OvrLeafPin),
            ("bc13 OVR-POSTURE the override is asked before the ledger order in the AI-pool loop (pin)", OvrPoolPin),
            ("bc13 OVR-PLAY the division play and the other movers lose to the exploit order; Safety beats it", OvrPriorities),
            ("bc13 OVR-MANEUVER the play executor takes no zone maneuver for a group with a live exploit (pin)", OvrManeuverPin),
            ("bc13 OVR-RECALL after Recall the override is gone and the ledger order resolves", OvrRecall),
        };
    }

    private static string ReadSrc(string relative) => File.ReadAllText(TestFixtures.FindRepoPath("src/WhiskeyRealism/" + relative));

    private static void OvrPostureNoDelay()
    {
        var request = new PostureIntentRequest
        {
            GroupId = 7, Action = PostureExecutionAction.SetFormationAndWaypoint, Task = CommandTaskType.AttackObjective,
            HasTarget = true, TargetX = 100f, TargetZ = 200f, TargetFormation = -1,
            Priority = IntentPriority.Executor, Source = IntentSource.PostureExecutor, Reason = "exploit-advance",
        };
        var plain = new CapturingSink();
        PostureIntentPlan.Submit(plain, in request);
        AssertTrue(plain.Moves.Count == 1 && (plain.Moves[0].Flags & IntentFlags.UseOrderDelay) != 0, "a ledger Move goes by courier");

        request.Exploit = true;
        var exploit = new CapturingSink();
        PostureIntentPlan.Submit(exploit, in request);
        AssertTrue(exploit.Moves.Count == 1, "an exploit request still submits its Move");
        AssertTrue((exploit.Moves[0].Flags & IntentFlags.UseOrderDelay) == 0, "and the exploit Move is not delayed");
        AssertFalse(CommandWaypointWritePolicy.ShouldUseOrderDelayForExecutorWaypoint(CommandTaskType.AttackObjective, true, false, exploit: true),
            "the policy refuses the courier for an exploit");
        AssertTrue(CommandWaypointWritePolicy.ShouldUseOrderDelayForExecutorWaypoint(CommandTaskType.AttackObjective, true, false),
            "and keeps it for every other planned Move");
        AssertTrue(CommandWaypointWritePolicy.ShouldUseOrderDelayForExecutorWaypoint(CommandTaskType.AttackObjective, false, false, exploit: true),
            "outside a battle nothing changes");
    }

    private sealed class CapturingSink : IIntentSink
    {
        public readonly System.Collections.Generic.List<BattleOrderIntent> Moves = new System.Collections.Generic.List<BattleOrderIntent>();
        public readonly System.Collections.Generic.List<BattleOrderIntent> All = new System.Collections.Generic.List<BattleOrderIntent>();
        public void Submit(in BattleOrderIntent intent)
        {
            All.Add(intent);
            if (intent.Channel == OrderChannel.Move) Moves.Add(intent);
        }
    }

    private static CommandNodeOperationalState LedgerState()
        => new CommandNodeOperationalState("node-100", CommandEchelonKind.BrigadeLike, CommandNodeRole.Reserve, CommandTaskType.ReserveWait,
            CommandTaskState.Committed, 12f, 34f, 90f);

    private static void OvrOverlay()
    {
        var table = ExploitTableWith(out ExploitEntry entry);
        AssertTrue(table.TryOverride(entry.AssetId, out CommandDoctrineOrder order, out CommandNodeOperationalState exploit), "override");
        ExploitOverrideKind kind = ExploitPosture.KindOf(in order);
        AssertEqual(ExploitOverrideKind.Exploit, kind, "an exploit order");
        CommandNodeOperationalState merged = ExploitPosture.Overlay(LedgerState(), true, exploit, kind);
        AssertEqual("node-100", merged.NodeId, "the ledger node id");
        AssertEqual(CommandEchelonKind.BrigadeLike, merged.Echelon, "the ledger echelon (the facing pulse's scope)");
        AssertEqual(CommandTaskState.Committed, merged.TaskState, "the ledger task state");
        AssertNear(12f, merged.X, 1e-4f, "the ledger position");
        AssertNear(90f, merged.FacingDegrees, 1e-4f, "the ledger facing");
        AssertEqual(exploit.Role, merged.Role, "the exploit role");
        AssertEqual(CommandTaskType.AttackObjective, merged.Task, "the exploit task");
        CommandNodeOperationalState alone = ExploitPosture.Overlay(default(CommandNodeOperationalState), false, exploit, kind);
        AssertEqual(exploit.NodeId, alone.NodeId, "with no ledger state the exploit's own");
        BattleScope.ResetAll();
    }

    private static void OvrFollowKind()
    {
        var table = ExploitTableWith(out ExploitEntry entry);
        table.SetSubtree(entry.Id, new[] { 501 }, new int[0]);
        AssertTrue(table.TryOverride(501, out CommandDoctrineOrder follow, out CommandNodeOperationalState followState), "a descendant is overridden");
        ExploitOverrideKind kind = ExploitPosture.KindOf(in follow);
        AssertEqual(ExploitOverrideKind.Follow, kind, "a follow order");
        AssertEqual(ExploitOverrideKind.None, ExploitPosture.KindOf(CommandDoctrineOrder.Create("node-1", CommandNodeRole.Reserve, CommandTaskType.ReserveWait, "o",
            DoctrineTargetPoint.None, DoctrineTargetPoint.None, DoctrineTargetPoint.None, DoctrineAllowedIdleReason.None, 0f, 0f, 1f, "r")), "an ordinary order is none");
        CommandNodeOperationalState ledger = LedgerState();
        CommandNodeOperationalState merged = ExploitPosture.Overlay(ledger, true, followState, kind);
        AssertEqual(ledger.Task, merged.Task, "a follower keeps its own task");
        AssertEqual(ledger.Role, merged.Role, "and its own role");
        BattleScope.ResetAll();
    }

    private static ExploitTable ExploitTableWith(out ExploitEntry entry)
    {
        BattleScope.ResetAll();
        ExploitTable table = ExploitTable.ForAlliance(0);
        var asset = new ExploitAsset { Id = 100, OwnIndex = 3, X = 0f, Z = 0f, LossFraction = 0.05f };
        var opening = new Opening { Id = 5, Kind = OpeningKind.Gap, AimX = 0f, AimZ = 600f, TargetObservation = -1 };
        entry = ExploitLifecycle.Launch(1, 7, 50, in asset, in opening, 1f, T0);
        table.Add(in entry);
        return table;
    }

    // The pins read the runtime file at the points the spec names. They fail if the override is moved after the ledger order, a
    // rewrite is no longer skipped, or the follower path starts submitting.
    private static void OvrFollowPin()
    {
        string src = ReadSrc("Tactical/Battle/Producers/PostureIntentProducer.cs");
        int run = src.IndexOf("internal static bool Run(", StringComparison.Ordinal);
        AssertTrue(run > 0, "PosturePipeline.Run exists");
        string body = src.Substring(run);
        int follow = body.IndexOf("ExploitPosture.SubmitsMoveOrFormation(exploitKind)", StringComparison.Ordinal);
        int decide = body.IndexOf("CommandPostureExecutor.Decide(", StringComparison.Ordinal);
        int submit = body.IndexOf("PostureIntentPlan.Submit(", StringComparison.Ordinal);
        AssertTrue(follow > 0 && decide > follow && submit > follow, "the follow early return precedes the decision and every submit");
        // BC14W: the follower of a non-breach exploit on unbroken works submits the pinned stance (a stance, never a Move or a Formation); a stance is no
        // order, so it still returns false and the follower keeps its facing pulse and fire control.
        int followReturn = body.IndexOf("return false;", follow, StringComparison.Ordinal);
        AssertTrue(followReturn > follow && followReturn < decide, "the follower returns before the decision");
        string followBlock = body.Substring(follow, followReturn - follow + "return false;".Length);
        AssertFalse(followBlock.Contains("sink.Submit") || followBlock.Contains("PostureIntentPlan.Submit(") || followBlock.Contains("SubmitMove") || followBlock.Contains("SubmitFormation"),
            "and the follower block submits no Move and no Formation");
        string withoutPin = followBlock.Replace("PostureIntentPlan.SubmitWorksPinStance(", "");
        AssertFalse(withoutPin.Contains("PostureIntentPlan."), "its one submission is the pinned stance");
    }

    private static void OvrSkipsPin()
    {
        string body = RunBody();
        AssertContains(body, "bool probe = recordTick && !exploit && ReconIntentProducer.TryProbe(", "no probe nomination for an exploit");
        AssertContains(body, "hasCavalry && !probe && !exploit &&", "no cavalry follow rewrite for an exploit");
        AssertContains(body, "!exploit && !WorksHoldGuard.Holds(state.Task, state.Reason) &&", "nor for a group the works hold guard holds");
        AssertContains(body, "TacticalGrandTacticianReconDecision recon = exploit", "no GT recon rewrite for an exploit");
        AssertContains(body, "CommandTaskType overridden = exploit", "no army opportunity override for an exploit");
        AssertContains(body, "bool holding = recordTick && !probe && !exploit && ReconIntentProducer.TryHold(", "no probe hold for an exploit");
        AssertContains(body, "Exploit = exploit,", "the request carries the exploit flag (no courier delay)");
        AssertContains(body, "PostureStanceGuard.Apply(stance, ref state, hasOrder, ref order);", "the stance guard still runs");
    }

    private static void OvrFeedbackPin()
    {
        string body = RunBody();
        int emergency = body.IndexOf("TaskForLocalFlankEmergency(", StringComparison.Ordinal);
        int feedback = body.IndexOf("ExploitFeedback.SetFlankEmergency(rec.InstanceId)", StringComparison.Ordinal);
        AssertTrue(emergency > 0 && feedback > emergency, "the local flank emergency stays and reports to ExploitFeedback");
        AssertContains(body, "if (exploit && recordTick) ExploitFeedback.SetFlankEmergency", "only for an exploit order, once per tick");
    }

    private static string RunBody()
    {
        string src = ReadSrc("Tactical/Battle/Producers/PostureIntentProducer.cs");
        int run = src.IndexOf("internal static bool Run(", StringComparison.Ordinal);
        AssertTrue(run > 0, "PosturePipeline.Run exists");
        return src.Substring(run);
    }

    private static void OvrLeafPin()
    {
        string src = ReadSrc("Tactical/Battle/Producers/PostureIntentProducer.cs");
        int leaf = src.IndexOf("private static void ProduceLeaf(", StringComparison.Ordinal);
        AssertTrue(leaf > 0, "ProduceLeaf exists");
        string body = src.Substring(leaf);
        int over = body.IndexOf("exploits.TryOverride(id", StringComparison.Ordinal);
        int slot = body.IndexOf("LeafSlotTarget.TryResolve(", StringComparison.Ordinal);
        int run = body.IndexOf("PosturePipeline.Run(", StringComparison.Ordinal);
        AssertTrue(over > 0 && run > over && slot > run, "the override and its pipeline run come before the leaf slot path");
    }

    private static void OvrPoolPin()
    {
        string src = ReadSrc("Tactical/Battle/Producers/PostureIntentProducer.cs");
        int produce = src.IndexOf("public void Produce(BattleSnapshot s, IIntentSink sink)", StringComparison.Ordinal);
        string body = src.Substring(produce, 5200);
        int over = body.IndexOf("exploits.TryOverride(rec.InstanceId", StringComparison.Ordinal);
        int ledger = body.IndexOf("PostureLedger.TryResolveDoctrineOrder(", StringComparison.Ordinal);
        AssertTrue(over > 0 && ledger > over, "the exploit table is asked before the ledger's doctrine order");
        AssertContains(body, "ExploitTable.ForAlliance(s.Alliance)", "the side's own table");
    }

    private static void OvrPriorities()
    {
        AssertTrue(IntentPriority.Executor > IntentPriority.Play, "the division play loses to the posture executor's exploit order");
        AssertTrue(IntentPriority.Executor > IntentPriority.Support && IntentPriority.Executor > IntentPriority.Scorer, "and so do the support and scorer claims");
        AssertTrue(IntentPriority.Safety > IntentPriority.Executor, "a Safety claim beats it");
        string reg = string.Join(",", BattleProducerRegistry.Order);
        AssertContains(reg, "Opportunity,Posture", "the producer that publishes the table runs just before the one that executes it");
    }

    private static void OvrManeuverPin()
    {
        string src = ReadSrc("Tactical/Orchestrator/Plays/PlayExecutorRuntime.cs");
        int step = src.IndexOf("public static IReadOnlyList<ChildManeuver> StepManeuvers(", StringComparison.Ordinal);
        AssertTrue(step > 0, "StepManeuvers exists");
        string body = src.Substring(step, 7000);
        AssertContains(body, "exploits.IsExploiting(snapshot.Own[index].InstanceId)", "a group with a live exploit takes no zone maneuver");
        AssertContains(src, "internal static int ManeuveringChildren(", "and the opportunity producer reads the maneuvering children");
    }

    private static void OvrRecall()
    {
        BattleScope.ResetAll();
        TickFx t = TickFx.Attacker();
        AssertTrue(t.RunToLaunch(), "launched");
        int assetId = t.Table.At(0).AssetId;
        AssertTrue(t.Table.TryOverride(assetId, out _, out _), "overridden while live");
        t.World.Safety.Add(assetId);
        t.Run();
        t.World.Safety.Clear();
        AssertFalse(t.Table.TryOverride(assetId, out _, out _), "after Recall the override is gone on that tick: the ledger order resolves");
        BattleScope.ResetAll();
    }
}
