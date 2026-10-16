using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.PlayerOrders;

using static TestHarness;

// A W&L player whose command is not its own campaign-map group (BelowMapLevel: a brigade or division
// carried by an AI corps or army) receives no campaign order, from Whiskey or from the #69 pass, for
// his carrying group's moves. Vanilla sends him none (AIBattle.CheckCurrentOrderUpdate gate 8366:
// !flag2 returns), and he cannot move himself, so a "battle order" on the campaign map is noise. The
// player who IS a map group keeps vanilla's orders and Whiskey's bridge orders (couriers included).
//
// The dispatcher and the courier runtime call Unity objects and cannot run in the harness, so the
// absence of the subordinate path is pinned in the source text, the way WlCampaignCourierTests pins
// the rest of the campaign order flow; the pure surface (sidecar, origins, bridge classification) is
// exercised directly.
internal static class WlNoSubordinateOrdersTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("wl no subordinate orders no patch on the campaign move callers", NoPatchOnCampaignMoveCallers),
            ("wl no subordinate orders the order pass drives delivery, acknowledgement and compliance only", OrderPassHasNoDispatchFlush),
            ("wl no subordinate orders the dispatcher records no vanilla move", DispatcherRecordsNoMove),
            ("wl no subordinate orders the dispatch policy has no subordinate trigger", PolicyHasNoSubordinateTrigger),
            ("wl no subordinate orders the dispatcher is reached only from the bridge", DispatcherReachedOnlyFromBridge),
            ("wl no subordinate orders the order origins have no #69 member", OriginsHaveNoC69),
            ("wl no subordinate orders a saved #69 courier entry is dropped on load", SavedC69EntryDroppedOnLoad),
            ("wl no subordinate orders a whiskey order rides from the senior headquarters and shows its zone", WhiskeyOrderRidesFromSeniorHeadquarters),
            ("wl no subordinate orders the carrying group is never ordered by the bridge", CarryingGroupNeverOrdered),
            ("wl no subordinate orders the map-group player still gets the bridge order", MapGroupPlayerStillOrdered),
            ("wl no subordinate orders campaign compliance samples only a map-group player", ComplianceSamplesOnlyMapGroupPlayer),
            ("wl no subordinate orders no order, no compliance sample", NoOrderNoSample),
            ("wl no subordinate orders the chain dispatch flag describes the map-group player only", FlagDescribesMapGroupPlayerOnly),
            ("wl no subordinate orders the battle order paths are untouched", BattlePathsUntouched),
        });
    }

    private static IEnumerable<string> SourceFiles(string folder)
    {
        string root = TestFixtures.FindRepoPath(folder);
        return Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
    }

    private static string Strip(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"//[^\n]*", string.Empty);

    private static string Code(string path) => Strip(TestFixtures.ReadRepoFile(path));

    private static void NoPatchOnCampaignMoveCallers()
    {
        foreach (string file in SourceFiles("src/WhiskeyRealism/Patches"))
        {
            string code = Strip(File.ReadAllText(file));
            foreach (string caller in new[] { "AICampaign.MoveUnitTo", "AICampaign.CheckFinalPositions", "AICampaign.RollUpEnemyObjectivesInZone" })
                AssertFalse(code.Contains("[HarmonyPatch(typeof(AICampaign), nameof(" + caller + "))]"), Path.GetFileName(file) + " must not patch " + caller);
            AssertFalse(code.Contains("EnterMoveSource") || code.Contains("ExitMoveSource"), Path.GetFileName(file) + " marks no move source");
        }
    }

    private static string OrderPassPatch()
    {
        var files = SourceFiles("src/WhiskeyRealism/Patches").Where(f => File.ReadAllText(f).Contains("WlCampaignCourierRuntime.DeliverDue();")).ToList();
        AssertEqual(1, files.Count, "exactly one patch drives the campaign order pass");
        return Strip(File.ReadAllText(files[0]));
    }

    private static void OrderPassHasNoDispatchFlush()
    {
        string patch = OrderPassPatch();
        AssertTrue(patch.Contains("[HarmonyPatch(typeof(AICampaign), \"UpdateUnitAI\")]"), "the pass is the UpdateUnitAI Postfix");
        AssertFalse(patch.Contains("WlCampaignDispatchRuntime.Flush("), "no pending-move flush");
        AssertFalse(patch.Contains("OnMoveUnitTo"), "no move recording");
        int deliver = patch.IndexOf("WlCampaignCourierRuntime.DeliverDue();", StringComparison.Ordinal);
        int acknowledge = patch.IndexOf("WlCampaignCourierRuntime.AcknowledgeReached();", StringComparison.Ordinal);
        int observe = patch.IndexOf("PlayerComplianceRuntime.ObserveCampaign();", StringComparison.Ordinal);
        AssertTrue(deliver >= 0 && acknowledge > deliver && observe > acknowledge, "delivery, then acknowledgement, then the compliance observation of what was delivered");
    }

    private static void DispatcherRecordsNoMove()
    {
        string runtime = Code("src/WhiskeyRealism/Strategic/WlCampaignDispatchRuntime.cs");
        foreach (string gone in new[] { "OnMoveUnitTo", "_pending", "Flush(", "vanilla-move", "VanillaMoveTrigger", "_corrections", "_moveSource", "EmitPending", "WlMoveSource", "WlMoveFacts", "WlMoveContext" })
            AssertFalse(runtime.Contains(gone), "the dispatcher has no " + gone);
        AssertTrue(runtime.Contains("internal static string EmitForPlayerCommand("), "its one entry is the bridge's order for a map-group player");
    }

    private static void PolicyHasNoSubordinateTrigger()
    {
        string policy = Code("src/WhiskeyRealism/Strategic/WlCampaignDispatchPolicy.cs");
        foreach (string gone in new[] { "IsTrigger", "WlMoveFacts", "WlMoveContext", "WlMoveSource", "WlCorrectionMemory", "ClassifyType", "PendingSkipReason", "IsLastMoveOfUnit", "VanillaEnemyAtTarget", "SubordinateDispatchesShowZone", "ShowsZone" })
            AssertFalse(policy.Contains(gone), "the dispatch policy has no " + gone);
        AssertTrue(policy.Contains("public static string Decide("), "the rate limit and the duplicate rule the bridge order still passes");
    }

    private static void DispatcherReachedOnlyFromBridge()
    {
        var callers = new List<string>();
        foreach (string file in SourceFiles("src/WhiskeyRealism"))
        {
            string code = Strip(File.ReadAllText(file));
            if (code.Contains("EmitForPlayerCommand(") && !code.Contains("internal static string EmitForPlayerCommand("))
                callers.Add(Path.GetFileName(file));
        }
        AssertEqual("WlStrategicOrderBridge.cs", string.Join(",", callers), "the only caller of the dispatcher");
        // The bridge reaches it only for the verdict that vanilla's own gate (8366) would pass.
        string bridge = Code("src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs");
        AssertTrue(bridge.Contains("decision.Result != WlStrategicOrderResult.IssuedWlCurrentOrder"), "only an Issued verdict reaches the dispatcher");
    }

    private static void OriginsHaveNoC69()
    {
        AssertFalse(Enum.GetNames(typeof(OrderOrigin)).Contains("C69"), "no order origin names the subordinate dispatcher");
        AssertTrue(Enum.GetNames(typeof(OrderOrigin)).Contains("CampaignBridge"), "the bridge's origin stays");
    }

    private static void SavedC69EntryDroppedOnLoad()
    {
        // A sidecar written before this fix can hold a pending #69 order for a subordinate player; it must
        // not be shown after the load (the entry is dropped, never trusted).
        var old = new CampaignCourierDto
        {
            Unit = "Army of the Potomac", UnitCommander = 3, Type = 5, X = 1000f, Y = 10f, Z = 1500f, Name = "Objective",
            Rotation = -1f, Width = 20f, Depth = 20f, Origin = "C69", Issuer = 4, ShowZone = true, PlayerCommand = "1st Brigade#2",
            IssuedAt = 3.25, DueAt = 3.2502,
        };
        AssertFalse(WlCampaignCourierSidecar.TryFromDto(old, out _), "a #69 entry does not load");
        var bridgeEntry = new CampaignCourierDto
        {
            Unit = null, UnitCommander = -1, Type = 5, X = 1000f, Y = 10f, Z = 1500f, Name = "Objective",
            Rotation = -1f, Width = 20f, Depth = 20f, Origin = "CampaignBridge", Issuer = 4, ShowZone = true, PlayerCommand = "1st Corps#2",
            IssuedAt = 3.25, DueAt = 3.2502,
        };
        var loaded = new List<CourierEntry>();
        WlCampaignCourierSidecar.Sanitize(new List<CampaignCourierDto> { old, bridgeEntry }, loaded);
        AssertEqual(1, loaded.Count, "the map-group player's bridge order survives next to it");
        AssertEqual(OrderOrigin.CampaignBridge, loaded[0].Origin);
    }

    private static void WhiskeyOrderRidesFromSeniorHeadquarters()
    {
        string courier = Code("src/WhiskeyRealism/Strategic/WlCampaignCourierRuntime.cs");
        AssertTrue(courier.Contains("Regiment hq = SeniorHeadquarters();"), "a Whiskey campaign order is sent by the senior commander");
        AssertFalse(courier.Contains("issuerUnit : SeniorHeadquarters()"), "no moving-formation headquarters branch");
        string runtime = Code("src/WhiskeyRealism/Strategic/WlCampaignDispatchRuntime.cs");
        AssertTrue(runtime.Contains("_showzone: true"), "the map-group player's order shows its zone, as vanilla's does");
        AssertFalse(runtime.Contains("showZone"), "no zone switch left");
    }

    private static WlStrategicRoleFacts CarryingFacts()
        => new WlStrategicRoleFacts(wlActive: true, isPlayerAlliance: true, isPartOfPlayerUnit: true, isPlayerCampaignGroup: true);

    private static WlStrategicRoleFacts MapGroupFacts()
        => new WlStrategicRoleFacts(wlActive: true, isPlayerAlliance: true, isUnderCommander: true, currentCommandIsCampaignGroup: true, currentCommandParentIsUnderTargetUnit: true);

    private static void CarryingGroupNeverOrdered()
    {
        foreach (WlStrategicIntent intent in Enum.GetValues(typeof(WlStrategicIntent)))
        {
            var decision = WlStrategicOrderBridge.Classify(intent, CarryingFacts());
            AssertFalse(decision.Result == WlStrategicOrderResult.IssuedWlCurrentOrder, "the group carrying a BelowMapLevel player is never issued a W&L order: " + intent);
        }
        var offensive = WlStrategicOrderBridge.Classify(WlStrategicIntent.Offensive, CarryingFacts());
        AssertEqual(WlStrategicOrderResult.DirectMovementAllowed, offensive.Result);
        AssertEqual("carrying-group", offensive.Reason);
        AssertTrue(offensive.MayDirectMove, "vanilla moves it like any AI group");
    }

    private static void MapGroupPlayerStillOrdered()
    {
        var decision = WlStrategicOrderBridge.Classify(WlStrategicIntent.Offensive, MapGroupFacts());
        AssertEqual(WlStrategicOrderResult.IssuedWlCurrentOrder, decision.Result);
        AssertEqual(16, decision.WlOrderType);
        AssertFalse(decision.MayDirectMove, "an order only: Whiskey never moves the map-group player's group");
    }

    private static void ComplianceSamplesOnlyMapGroupPlayer()
    {
        string runtime = Code("src/WhiskeyRealism/Strategic/PlayerComplianceRuntime.cs");
        AssertTrue(runtime.Contains("bool ownMapGroup = (UnityEngine.Object)BattleUnits.GetCampaignGroup(command) == (UnityEngine.Object)command;"), "the map-group test");
        AssertTrue(runtime.Contains("playerDecides: ownMapGroup"), "a campaign order is a sample only where the player decides the movement");
    }

    private static void NoOrderNoSample()
    {
        var ledger = new PlayerComplianceLedger();
        for (double hours = 0; hours < 1000; hours += 24)
            AssertEqual(ComplianceOutcome.None, ledger.Tick(ComplianceScope.Campaign, hours), "no order, no outcome at " + hours);
        AssertEqual(0, ledger.Samples, "no sample without an order");
        AssertNear(1f, ledger.Reliability, 1e-6f, "reliability untouched");
    }

    private static void FlagDescribesMapGroupPlayerOnly()
    {
        string plugin = TestFixtures.ReadRepoFile("src/WhiskeyRealism/Plugin.cs");
        int at = plugin.IndexOf("\"Enable Campaign Chain Dispatches\"", StringComparison.Ordinal);
        AssertTrue(at >= 0, "the flag exists");
        string block = plugin.Substring(at, plugin.IndexOf(");", at, StringComparison.Ordinal) - at);
        AssertTrue(block.Contains("true,"), "default on");
        AssertTrue(block.Contains("Default ON."), "described");
        foreach (string stale in new[] { "subordinate", "inside a corps", "#69", "commander of the moving formation", "vanilla moves" })
            AssertFalse(block.Contains(stale), "the description no longer promises orders for " + stale);
        AssertTrue(block.Contains("no order") || block.Contains("never receives"), "it says the carried player gets none");
    }

    private static void BattlePathsUntouched()
    {
        // The battle order flow has its own origins and entry points; this fix leaves them as they were.
        foreach (string origin in new[] { "Vanilla", "P62", "Executor", "Release", "FollowUp", "Retreat" })
            AssertTrue(Enum.GetNames(typeof(OrderOrigin)).Contains(origin), "battle origin " + origin);
        string flow = Code("src/WhiskeyRealism/Tactical/PlayerOrders/WlOrderFlowRuntime.cs");
        AssertTrue(flow.Contains("if (call.Campaign) return RouteCampaign(in call);"), "campaign calls route to the campaign branch");
        string dispatch = Code("src/WhiskeyRealism/Tactical/Battle/TacticalPlayerDispatchRuntime.cs");
        AssertTrue(dispatch.Contains("internal static bool Emit("), "the battle dispatch emitter stays");
    }
}
