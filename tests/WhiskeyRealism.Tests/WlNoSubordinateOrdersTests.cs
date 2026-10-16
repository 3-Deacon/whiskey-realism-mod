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
            ("wl no subordinate orders every order type is refused a carried player and kept for a map-group player", EveryOrderTypeByPlayerClass),
            ("wl no subordinate orders vanilla's own campaign gate passes a map-group player and no carried one", VanillaGateByPlayerClass),
            ("wl no subordinate orders a leftover campaign order of a carried player is cleared, a battle order and a map-group player's stay", StaleCarriedOrderRule),
            ("wl no subordinate orders the leftover clear is vanilla's own null write, ahead of delivery", StaleClearWiring),
            ("wl no subordinate orders a whiskey order queued for a map-group player is dropped when he was transferred under a corps", TransferredPlayerOrderDropped),
            ("wl no subordinate orders an old sidecar with a showZone key still loads", OldSidecarWithShowZoneLoads),
            ("wl no subordinate orders the delivery pass asks the pure rule with the player's command and map-group state", DeliveryPassWiring),
            ("wl no subordinate orders the order pass runs exactly its four statements and only the dispatcher enqueues", OrderPassExactStatements),
            ("wl no subordinate orders the compliance observation passes the map-group test unmodified", ComplianceExactStatements),
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
        int clear = patch.IndexOf("WlCampaignCourierRuntime.ClearStaleCarriedOrder();", StringComparison.Ordinal);
        int deliver = patch.IndexOf("WlCampaignCourierRuntime.DeliverDue();", StringComparison.Ordinal);
        int acknowledge = patch.IndexOf("WlCampaignCourierRuntime.AcknowledgeReached();", StringComparison.Ordinal);
        int observe = patch.IndexOf("PlayerComplianceRuntime.ObserveCampaign();", StringComparison.Ordinal);
        AssertTrue(clear >= 0 && deliver > clear && acknowledge > deliver && observe > acknowledge, "leftover clear, delivery, acknowledgement, then the compliance observation of what was delivered");
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
        // Every type the dispatcher maps: 5 redeploy, 6 offensive operation, 7 engage, 8 capital, 16 offensive.
        CampaignCourierDto old = null;
        foreach (int type in new[] { 5, 6, 7, 8, 16 })
        {
            old = new CampaignCourierDto
            {
                Unit = "Army of the Potomac", UnitCommander = 3, Type = type, X = 1000f, Y = 10f, Z = 1500f, Name = "Objective",
                Rotation = -1f, Width = 20f, Depth = 20f, Origin = "C69", Issuer = 4, PlayerCommand = "1st Brigade#2",
                IssuedAt = 3.25, DueAt = 3.2502,
            };
            AssertFalse(WlCampaignCourierSidecar.TryFromDto(old, out _), "a #69 entry of type " + type + " does not load");
        }
        var bridgeEntry = new CampaignCourierDto
        {
            Unit = null, UnitCommander = -1, Type = 5, X = 1000f, Y = 10f, Z = 1500f, Name = "Objective",
            Rotation = -1f, Width = 20f, Depth = 20f, Origin = "CampaignBridge", Issuer = 4, PlayerCommand = "1st Corps#2",
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
        AssertFalse(runtime.Contains("showZone") || runtime.Contains("ShowZone"), "no zone switch left");
    }

    private static WlStrategicRoleFacts CarryingFacts()
        => new WlStrategicRoleFacts(wlActive: true, isPlayerAlliance: true, isPartOfPlayerUnit: true, isPlayerCampaignGroup: true);

    private static WlStrategicRoleFacts MapGroupFacts()
        => new WlStrategicRoleFacts(wlActive: true, isPlayerAlliance: true, isUnderCommander: true, currentCommandIsCampaignGroup: true, currentCommandParentIsUnderTargetUnit: true);

    // Every intent the bridge maps, with the vanilla order type it stands for (WlOrderTypeForIntent).
    private static readonly (WlStrategicIntent intent, int type)[] IntentTypes =
    {
        (WlStrategicIntent.Redeploy, 5), (WlStrategicIntent.OffensiveContinuation, 6), (WlStrategicIntent.EngageEnemy, 7),
        (WlStrategicIntent.DefendCapital, 8), (WlStrategicIntent.ConstructFort, 9), (WlStrategicIntent.ConstructSupplyDepot, 10),
        (WlStrategicIntent.Offensive, 16),
    };

    private static void EveryOrderTypeByPlayerClass()
    {
        foreach (var (intent, type) in IntentTypes)
        {
            AssertEqual(type, WlStrategicOrderBridge.WlOrderTypeForIntent(intent), "intent " + intent + " is order type " + type);
            var carried = WlStrategicOrderBridge.Classify(intent, CarryingFacts());
            AssertFalse(carried.Result == WlStrategicOrderResult.IssuedWlCurrentOrder, "type " + type + ": a player carried by his corps is issued nothing");
            var cic = WlStrategicOrderBridge.Classify(intent, new WlStrategicRoleFacts(wlActive: true, isPlayerAlliance: true, isPlayerCic: true));
            AssertFalse(cic.Result == WlStrategicOrderResult.IssuedWlCurrentOrder, "type " + type + ": a CIC side is issued nothing");
            var own = WlStrategicOrderBridge.Classify(intent, new WlStrategicRoleFacts(wlActive: true, isPlayerAlliance: true, isUnderCommander: true));
            AssertFalse(own.Result == WlStrategicOrderResult.IssuedWlCurrentOrder, "type " + type + ": a unit under the player below map level is issued nothing");
            var mapGroup = WlStrategicOrderBridge.Classify(intent, MapGroupFacts());
            AssertEqual(WlStrategicOrderResult.IssuedWlCurrentOrder, mapGroup.Result, "type " + type + ": the map-group player keeps Whiskey's dispatcher order");
            AssertEqual(type, mapGroup.WlOrderType);
        }
    }

    // Vanilla's own campaign orders (the offensive 5/16, the micro-movement 6, engage 7, capital defense 8)
    // reach the capture gate; vanilla's gate (8366) lets only the map-group player's through.
    private static void VanillaGateByPlayerClass()
    {
        foreach (int type in new[] { 5, 6, 7, 8, 9, 10, 16 })
        {
            var mapGroup = new CaptureFacts
            {
                WlActive = true, HasCurrentCommand = true, UnitIsPlayerAlliance = true, CampaignCall = true,
                CampaignGroupIsOwnCommand = true, ParentIsUnderUnit = true, UnitIsUnderCommander = true, Type = type,
            };
            AssertTrue(WlOrderCaptureGate.ReachesDedupe(mapGroup), "type " + type + ": vanilla's order reaches the map-group player");
            var carried = mapGroup;
            carried.CampaignGroupIsOwnCommand = false;
            AssertFalse(WlOrderCaptureGate.ReachesDedupe(carried), "type " + type + ": vanilla sends the carried player nothing");
        }
    }

    private static void StaleCarriedOrderRule()
    {
        // wlActive, cic, ownMapGroup, hasOrder, isBattleOrder
        AssertTrue(WlCampaignCourierPolicy.HoldsStaleCarriedOrder(true, false, false, true, false), "a campaign order on a carried player's panel is a leftover");
        AssertFalse(WlCampaignCourierPolicy.HoldsStaleCarriedOrder(true, false, true, true, false), "the map-group player's order is his");
        AssertFalse(WlCampaignCourierPolicy.HoldsStaleCarriedOrder(true, false, false, true, true), "a battle order is the battle flow's");
        AssertFalse(WlCampaignCourierPolicy.HoldsStaleCarriedOrder(true, false, false, false, false), "an empty slot has nothing to clear");
        AssertFalse(WlCampaignCourierPolicy.HoldsStaleCarriedOrder(false, false, false, true, false), "outside a W&L career");
        AssertFalse(WlCampaignCourierPolicy.HoldsStaleCarriedOrder(true, true, false, true, false), "the CIC was never sent one and stays untouched");
    }

    private static void StaleClearWiring()
    {
        string courier = Code("src/WhiskeyRealism/Strategic/WlCampaignCourierRuntime.cs");
        string body = TestFixtures.BodyOf(courier, "internal static void ClearStaleCarriedOrder()");
        // Exact statements: a literal in place of a fact, or a dropped switch, changes who loses an order.
        AssertTrue(TestFixtures.HasLine(body, "if (!MasterEnabled() || !DLC_WL.dlc_scenarioactive) return;"), "behind the master switch, in a W&L career");
        AssertTrue(TestFixtures.HasLine(body, "DLC_WL.GivenOrders order = DLC_WL.givenorder;"), "the order slot as it stands");
        AssertTrue(TestFixtures.HasLine(body, "bool ownMapGroup = (UnityEngine.Object)BattleUnits.GetCampaignGroup(command) == (UnityEngine.Object)command;"), "the map-group test is vanilla's flag2 (8271-8274)");
        AssertTrue(TestFixtures.HasLine(body, "if (!WlCampaignCourierPolicy.HoldsStaleCarriedOrder(true, DLC_WL.IsCommanderInChief(), ownMapGroup, true, order.IsBattleOrder())) return;"), "the pure rule decides, from the live facts");
        AssertTrue(TestFixtures.HasLine(body, "DLC_WL.givenorder = null;"), "vanilla's own way to end an order");
        AssertTrue(TestFixtures.HasLine(body, "_watched = null;"), "no acknowledgement is shown for a cleared leftover");
        int rule = body.IndexOf("HoldsStaleCarriedOrder(", StringComparison.Ordinal);
        int write = body.IndexOf("DLC_WL.givenorder = null;", StringComparison.Ordinal);
        AssertTrue(rule >= 0 && write > rule, "the slot is emptied only after the rule says so");
        AssertEqual(1, System.Text.RegularExpressions.Regex.Matches(body, @"givenorder = ").Count, "one write to the slot");
        AssertTrue(body.Contains("catch (Exception"), "never throws out of the patch");
    }

    private sealed class NullSink : ICourierDiscardSink
    {
        public void Discarded(in CourierEntry e, string reason) { }
    }

    private static CourierEntry BridgeEntry(OrderOrigin origin, string commandKey) => new CourierEntry
    {
        UnitName = null, UnitCommanderId = -1, Type = 5, X = 1000f, Y = 10f, Z = 1500f, Name = "Objective", Rotation = -1f, Width = 20f, Depth = 20f,
        CampaignCall = true, Origin = origin, IssuerKey = 77, IssuerCommanderId = 4, IssuedAt = 3.0, DueAt = 3.001, PlayerCommandKey = commandKey,
    };

    // Queued while he led his own map group, he was then moved under a corps (a transfer keeps the command's
    // name, so the command key still matches), then it arrived.
    private static void TransferredPlayerOrderDropped()
    {
        var queue = new WlCourierQueue(new NullSink());
        CourierEntry queued = BridgeEntry(OrderOrigin.CampaignBridge, "1st Corps#2");
        AssertTrue(queue.TryEnqueue(in queued, WlCampaignDispatchPolicy.RateLimitYears, out _), "queued while he led his own map group");
        AssertTrue(queue.TryTakeDue(4.0, out CourierEntry arrived, out bool superseded) && !superseded, "arrived");
        AssertEqual(null, WlCampaignCourierPolicy.PreDeliveryDropReason(in arrived, "1st Corps#2", playerIsOwnMapGroup: true), "still his own map group: delivered");
        AssertEqual(WlCampaignCourierPolicy.CarriedPlayer, WlCampaignCourierPolicy.PreDeliveryDropReason(in arrived, "1st Corps#2", playerIsOwnMapGroup: false), "transferred under a corps: dropped, never shown");
        AssertEqual("carried-player", WlCampaignCourierPolicy.CarriedPlayer, "the logged reason");
        AssertEqual(WlCampaignCourierPolicy.CommandChange, WlCampaignCourierPolicy.PreDeliveryDropReason(in arrived, "2nd Corps#2", playerIsOwnMapGroup: true), "another command (a promotion): void");
        AssertEqual(WlCampaignCourierPolicy.CommandChange, WlCampaignCourierPolicy.PreDeliveryDropReason(in arrived, "2nd Corps#2", playerIsOwnMapGroup: false), "another command wins over the map-group test");
        CourierEntry unkeyed = BridgeEntry(OrderOrigin.CampaignBridge, null);
        AssertEqual(null, WlCampaignCourierPolicy.PreDeliveryDropReason(in unkeyed, "1st Corps#2", true), "an old entry without identity is not yet known to be wrong");
        // Vanilla's own campaign order is left to vanilla's gate (8366), which runs again at delivery.
        CourierEntry vanilla = BridgeEntry(OrderOrigin.Vanilla, "1st Corps#2");
        AssertEqual(null, WlCampaignCourierPolicy.PreDeliveryDropReason(in vanilla, "1st Corps#2", playerIsOwnMapGroup: false), "a vanilla order is not this rule's");
        AssertEqual(WlCampaignCourierPolicy.CommandChange, WlCampaignCourierPolicy.PreDeliveryDropReason(in vanilla, "2nd Corps#2", true), "but it never rides to another command either");
    }

    private static void DeliveryPassWiring()
    {
        string courier = Code("src/WhiskeyRealism/Strategic/WlCampaignCourierRuntime.cs");
        string body = TestFixtures.BodyOf(courier, "internal static void DeliverDue()");
        AssertTrue(TestFixtures.HasLine(body, "bool ownMapGroup = (UnityEngine.Object)BattleUnits.GetCampaignGroup(command) == (UnityEngine.Object)command;"), "vanilla's flag2 for the player's command");
        AssertTrue(TestFixtures.HasLine(body, "string commandKey = CurrentCommandKey();"), "the current command key");
        AssertTrue(TestFixtures.HasLine(body, "string preDrop = WlCampaignCourierPolicy.PreDeliveryDropReason(in entry, commandKey, ownMapGroup);"), "the pure rule decides before anything is shown");
        AssertTrue(TestFixtures.HasLine(body, "Row(\"drop\", preDrop, in entry);"), "a carried player's order is a drop row, never shown");
        AssertTrue(TestFixtures.HasLine(body, "Queue.Discard(in entry, preDrop);"), "another command's order is a discard");
        int pre = body.IndexOf("PreDeliveryDropReason(", StringComparison.Ordinal);
        int deliver = body.IndexOf("DeliverOne(in entry);", StringComparison.Ordinal);
        AssertTrue(pre >= 0 && deliver > pre, "delivery follows the check");
        AssertEqual(1, System.Text.RegularExpressions.Regex.Matches(body, @"DeliverOne\(").Count, "one way to deliver");
    }

    private static void OldSidecarWithShowZoneLoads()
    {
        var snap = SidecarSnapshot.Empty();
        CourierEntry e = BridgeEntry(OrderOrigin.CampaignBridge, "1st Corps#2");
        e.UnitName = "1st Corps";
        e.UnitCommanderId = 3;
        snap.CampaignCouriers = new List<CampaignCourierDto> { WlCampaignCourierSidecar.ToDto(in e) };
        string json = SidecarDtoCodec.Serialize(SidecarDtoCodec.Encode(snap));
        AssertFalse(json.Contains("showZone"), "a new sidecar no longer writes the key");
        string old = System.Text.RegularExpressions.Regex.Replace(json, "(\"origin\"\\s*:\\s*\"CampaignBridge\")", "$1,\"showZone\":true");
        AssertTrue(old != json, "the key was added to the saved entry (the sidecar as an older build wrote it)");
        AssertTrue(SidecarDtoCodec.TryDeserialize(old, out var dto, out _), "an older file deserializes");
        AssertTrue(SidecarDtoCodec.TryDecode(dto, out var back, out _), "and decodes");
        AssertEqual(1, back.CampaignCouriers.Count, "the pending order is kept");
        AssertTrue(WlCampaignCourierSidecar.TryFromDto(back.CampaignCouriers[0], out CourierEntry loaded), "and loads");
        AssertEqual(OrderOrigin.CampaignBridge, loaded.Origin);
        AssertEqual("1st Corps#2", loaded.PlayerCommandKey);
    }

    private static void OrderPassExactStatements()
    {
        string patch = OrderPassPatch();
        int at = System.Text.RegularExpressions.Regex.Match(patch, @"\btry\b").Index;
        int open = patch.IndexOf('{', at);
        int close = patch.IndexOf('}', open);
        var statements = patch.Substring(open + 1, close - open - 1).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        AssertEqual(string.Join("|", new[]
        {
            "WlCampaignCourierRuntime.ClearStaleCarriedOrder();",
            "WlCampaignCourierRuntime.DeliverDue();",
            "WlCampaignCourierRuntime.AcknowledgeReached();",
            "PlayerComplianceRuntime.ObserveCampaign();",
        }), string.Join("|", statements), "the order pass is exactly these four calls, in this order");
        // Only the dispatcher queues a Whiskey order: the definition and the one call in Emit.
        int enqueues = 0;
        foreach (string file in SourceFiles("src/WhiskeyRealism"))
            enqueues += System.Text.RegularExpressions.Regex.Matches(Strip(File.ReadAllText(file)), @"WlCampaignCourierRuntime\.EnqueueWhiskey\(").Count;
        AssertEqual(1, enqueues, "WlCampaignCourierRuntime.EnqueueWhiskey has one caller in src");
        string dispatcher = Code("src/WhiskeyRealism/Strategic/WlCampaignDispatchRuntime.cs");
        AssertEqual(1, System.Text.RegularExpressions.Regex.Matches(dispatcher, @"WlCampaignCourierRuntime\.EnqueueWhiskey\(").Count, "the call is the dispatcher's");
    }

    private static void ComplianceExactStatements()
    {
        string runtime = Code("src/WhiskeyRealism/Strategic/PlayerComplianceRuntime.cs");
        string body = TestFixtures.BodyOf(runtime, "internal static void ObserveCampaign()");
        AssertTrue(TestFixtures.HasLine(body, "bool ownMapGroup = (UnityEngine.Object)BattleUnits.GetCampaignGroup(command) == (UnityEngine.Object)command;"), "vanilla's flag2");
        AssertTrue(TestFixtures.HasLine(body, "Observe(ComplianceScope.Campaign, GameVars.GetCampaignFloatingDate() * HoursPerYear, playerDecides: ownMapGroup);"), "a campaign order is a sample only where the player decides the movement, unmodified");
        AssertEqual(1, System.Text.RegularExpressions.Regex.Matches(body, @"Observe\(").Count, "one observation");
        string battle = TestFixtures.BodyOf(runtime, "internal static void ObserveBattle(double nowBattleHours)");
        AssertTrue(TestFixtures.HasLine(battle, "Observe(ComplianceScope.Battle, nowBattleHours, playerDecides: true);"), "the battle observation is unchanged");
    }

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
