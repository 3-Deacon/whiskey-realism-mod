using System;
using System.Text.RegularExpressions;

using static TestHarness;

// Source pins for the order flow's never-throw, player-side-only and never-move-the-player rules:
// facts about the shipped patch and runtime source that no pure test can observe.
internal static partial class WlOrderFlowTests
{
    private static readonly Regex LineComment = new Regex(@"//[^\n]*");

    private static string Code(string relativePath)
        => LineComment.Replace(TestFixtures.ReadRepoFile(relativePath), string.Empty);

    // The body of the first method whose signature contains `signature`, to its closing brace.
    private static string BodyOf(string code, string signature)
    {
        int at = code.IndexOf(signature, StringComparison.Ordinal);
        AssertTrue(at >= 0, "method not found: " + signature);
        int open = code.IndexOf('{', at);
        int depth = 0;
        for (int i = open; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0) return code.Substring(open, i - open + 1);
        }
        throw new Exception("unbalanced braces after: " + signature);
    }

    private const string FlowRuntime = "src/WhiskeyRealism/Tactical/PlayerOrders/WlOrderFlowRuntime.cs";
    private const string FlowPass = "src/WhiskeyRealism/Tactical/PlayerOrders/WlOrderFlow.cs";

    private static void CaptureReturnsTrueOnException()
    {
        // A throw anywhere in the capture must hand the call to vanilla: the Prefix returns the
        // runtime's verdict, and the runtime's catch returns true.
        string patch = Code("src/WhiskeyRealism/Patches/WlOrderCapturePatch.cs");
        AssertTrue(Regex.IsMatch(patch, @"return\s+WlOrderFlowRuntime\.Capture\("), "the Prefix returns the runtime's verdict");
        string capture = BodyOf(Code(FlowRuntime), "internal static bool Capture(");
        Match m = Regex.Match(capture, @"catch\s*\(\s*Exception[^)]*\)\s*\{(?<body>[^}]*)\}");
        AssertTrue(m.Success, "Capture has a catch (Exception)");
        AssertTrue(Regex.IsMatch(m.Groups["body"].Value, @"\breturn\s+true\s*;"), "the catch passes the call to vanilla");
        AssertFalse(Regex.IsMatch(m.Groups["body"].Value, @"\bthrow\b"), "the catch does not rethrow");
        // The Whiskey emitters swallow too: an exception must not escape into the executor or #62.
        string dispatch = Code("src/WhiskeyRealism/Tactical/Battle/TacticalPlayerDispatchRuntime.cs");
        AssertTrue(Regex.IsMatch(dispatch, @"catch\s*\(\s*Exception[^)]*\)\s*\{[^}]*return\s+false\s*;"), "Emit returns false on a throw");
        // The delivery Postfix swallows as well.
        string delivery = Code("src/WhiskeyRealism/Patches/WlOrderDeliveryPatch.cs");
        AssertTrue(Regex.IsMatch(delivery, @"catch\s*\(\s*Exception[^)]*\)\s*\{[^}]*OnceLog\.Warning"), "the delivery Postfix logs once and returns");
    }

    private static void DeliveryOnlyOnPlayerSideInstance()
    {
        string body = BodyOf(Code(FlowRuntime), "internal static void OnDeliveryPass(");
        int gate = body.IndexOf("isplayeraiorfeud != 1", StringComparison.Ordinal);
        int run = body.IndexOf("Flow.RunPass()", StringComparison.Ordinal);
        AssertTrue(gate >= 0, "the delivery pass tests isplayeraiorfeud");
        AssertTrue(run > gate, "the instance test comes before the pass runs");
        AssertTrue(Regex.IsMatch(body.Substring(gate, 40), @"isplayeraiorfeud != 1\)\s*return"), "other instances return");
        string patch = Code("src/WhiskeyRealism/Patches/WlOrderDeliveryPatch.cs");
        AssertTrue(patch.Contains("[HarmonyPatch(typeof(AIBattle), \"UpdateDLCPlayerOrders\")]"), "the delivery patch targets UpdateDLCPlayerOrders");
        AssertTrue(patch.Contains("WlOrderFlowRuntime.OnDeliveryPass(__instance)"), "and hands the instance to the runtime");
    }

    // Vanilla writers of a unit's movement, stance, formation, facing and combat behaviour, and the
    // unit fields they stand for. The order flow writes the order slot, the order session's
    // bookkeeping and nothing a player-owned unit does.
    private static readonly Regex PlayerUnitWriter = new Regex(
        @"\.(SetWaypoint|ChangeStance|ChangeCombatBehavior|SetGroupFormation|SetFormation|ChangeRegimentFormation|RotateRegiment|SetMovementMode|RegimentSetPath|BlockedMovingOrder|PerformActionForGroupForAllUnitsOnLevel|AddToOrderQueue|DeleteOrderQueue|SetRetreat\w*)\(" +
        @"|\.(running|doublequick|movementmode|ai_stance|macroai|formation|regimentpaths|combatbehavior\w*)\s*=(?!=)");

    private static readonly string[] OrderFlowFiles =
    {
        "src/WhiskeyRealism/Tactical/PlayerOrders/WlOrderFlowRuntime.cs",
        "src/WhiskeyRealism/Tactical/PlayerOrders/WlOrderFlow.cs",
        "src/WhiskeyRealism/Tactical/PlayerOrders/WlOrderCaptureGate.cs",
        "src/WhiskeyRealism/Tactical/PlayerOrders/WlCourierQueue.cs",
        "src/WhiskeyRealism/Tactical/PlayerOrders/WlCourierDelay.cs",
        "src/WhiskeyRealism/Tactical/PlayerOrders/PlayerOrderReadmitPolicy.cs",
        "src/WhiskeyRealism/Patches/WlOrderCapturePatch.cs",
        "src/WhiskeyRealism/Patches/WlOrderDeliveryPatch.cs",
        "src/WhiskeyRealism/Tactical/Battle/TacticalPlayerDispatchRuntime.cs",
    };

    private static void NoPlayerUnitWriter()
    {
        foreach (string file in OrderFlowFiles)
        {
            Match m = PlayerUnitWriter.Match(Code(file));
            AssertFalse(m.Success, file + " writes a unit: " + m.Value);
        }
        // The pin catches what it claims to.
        AssertTrue(PlayerUnitWriter.IsMatch("bunits.SetWaypoint(unit, p);"), "SetWaypoint");
        AssertTrue(PlayerUnitWriter.IsMatch("unit.running = true;"), "running field");
        AssertTrue(PlayerUnitWriter.IsMatch("TacticalStanceWriter.Stance.ChangeStance(go, 2);"), "ChangeStance");
        AssertFalse(PlayerUnitWriter.IsMatch("if (unit.running == true) { }"), "a comparison is no write");
    }

    // Wiring: the pieces are connected the way the design says, so a refactor cannot unhook one in silence.
    private static void WiringCaptureDeliveryAndEmitters()
    {
        string capture = Code("src/WhiskeyRealism/Patches/WlOrderCapturePatch.cs");
        AssertTrue(capture.Contains("[HarmonyPatch(typeof(AIBattle), \"CheckCurrentOrderUpdate\")]"), "capture targets CheckCurrentOrderUpdate");
        AssertTrue(Regex.IsMatch(capture, @"\[HarmonyPrefix\]\s*\[HarmonyPriority\(Priority\.Last\)\]\s*internal static bool Prefix\("),
            "a skipping Prefix at Priority.Last, after the observer Prefix");
        AssertTrue(Regex.IsMatch(BodyOf(capture, "internal static void Finalizer("), @"ClearCapturedFlag\(\)"), "the Finalizer clears the captured flag");
        string delivery = Code("src/WhiskeyRealism/Patches/WlOrderDeliveryPatch.cs");
        AssertTrue(Regex.IsMatch(delivery, @"\[HarmonyPostfix\]\s*\[HarmonyPriority\(Priority\.Low\)\]"), "the delivery Postfix runs after #62's");
        AssertTrue(Code("src/WhiskeyRealism/Patches/TacticalObserverPatch.cs").Contains("if (WlOrderFlowRuntime.CapturedThisCall) return;"),
            "the observer Postfix skips a captured call");
        AssertTrue(Code("src/WhiskeyRealism/Tactical/Battle/OrderExecutorRuntime.cs").Contains("OrderOrigin.Executor"), "the executor's dispatch is an Executor entry");
        AssertTrue(Code("src/WhiskeyRealism/Patches/PlayerSubordinateOrderPatch.cs").Contains("OrderOrigin.P62"), "#62's dispatch is a P62 entry");
        string runtime = Code(FlowRuntime);
        AssertTrue(runtime.Contains("Plugin.EnableWlOrderReadmit") && runtime.Contains("Plugin.EnableWlOrderCouriers"), "both order-flow switches are read");
        AssertTrue(runtime.Contains("BattleScope.Register(\"wl-order-flow\""), "the battle-scoped state registers its reset");
        // The delivery transaction is the only place the runtime empties or refills the slot.
        AssertEqual(2, Regex.Matches(runtime, @"DLC_WL\.givenorder\s*=(?!=)").Count, "ClearSlot and RestoreSlot are the two slot writes");
        AssertTrue(Regex.IsMatch(Code(FlowPass), @"_host\.ClearSlot\(\)"), "the pass empties the slot through the host");
    }
}
