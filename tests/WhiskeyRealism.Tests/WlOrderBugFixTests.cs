using System;
using System.Text.RegularExpressions;
using WhiskeyRealism.Tactical.PlayerOrders;

using static TestHarness;

// The three vanilla defects the order-flow plan fixes beside the flow itself (D13, D14, D18):
// BUG-WL-019 the enemy's retreat order sent to the player, BUG-WL-020 the fog grid left hidden by a
// Lost Orders incident that does not apply, BUG-WL-022 the portrait of a replaced issuer.
internal static class WlOrderBugFixTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("wl order bugfix enemy side retreat dropped", EnemySideRetreatDropped),
            ("wl order bugfix player side retreat kept", PlayerSideRetreatKept),
            ("wl order bugfix removal stamp cleared on throw", StampClearedOnThrow),
            ("wl order bugfix enemy retreat guard runs before the flow's own switches", GuardRunsBeforeTheFlowSwitches),
            ("wl order bugfix fow restored only when not launched and paper map open", FowRestoredOnlyWhenNotLaunchedAndPaperMapOpen),
            ("wl order bugfix fow not restored on skipinfos relaunch", FowNotRestoredOnSkipInfosRelaunch),
            ("wl order bugfix fow patch shape", FowPatchShape),
            ("wl order bugfix stale issuer replaced only when not commanding", StaleIssuerReplacedOnlyWhenNotCommanding),
            ("wl order bugfix stale issuer wired to the delivery pass and the text switch", StaleIssuerWired),
            ("wl order bugfix flags are on and described", BugFixFlagsDescribed),
        });
    }

    private static string Code(string path)
        => Regex.Replace(TestFixtures.ReadRepoFile(path), @"//[^\n]*", string.Empty);

    // ------------------------------------------------------------ BUG-WL-019

    private static void EnemySideRetreatDropped()
    {
        // The stamp is the calling AIBattle's isplayeraiorfeud: 1 the player's side, 0 the enemy, 2 the feud.
        AssertTrue(EnemyRetreatGuard.IsEnemySideRetreat(15, removalStamp: 0), "the enemy instance's retreat");
        AssertTrue(EnemyRetreatGuard.IsEnemySideRetreat(15, removalStamp: 2), "the feud instance's retreat");
        AssertFalse(EnemyRetreatGuard.IsEnemySideRetreat(13, 0), "only a retreat is dropped (13)");
        AssertFalse(EnemyRetreatGuard.IsEnemySideRetreat(14, 0), "only a retreat is dropped (14)");
        AssertFalse(EnemyRetreatGuard.IsEnemySideRetreat(12, 2), "only a retreat is dropped (12)");
    }

    private static void PlayerSideRetreatKept()
    {
        AssertFalse(EnemyRetreatGuard.IsEnemySideRetreat(15, removalStamp: 1), "the player's own army retreating");
        AssertFalse(EnemyRetreatGuard.IsEnemySideRetreat(15, removalStamp: EnemyRetreatGuard.NoStamp), "a 15 made outside CheckRemovalOfOrders (the derived retreat) is not a vanilla caller");
    }

    private static void StampClearedOnThrow()
    {
        string patch = Code("src/WhiskeyRealism/Patches/WlRemovalOrderSourcePatch.cs");
        AssertTrue(patch.Contains("[HarmonyPatch(typeof(AIBattle), \"CheckRemovalOfOrders\")]"), "the private method");
        AssertTrue(patch.Contains("internal static void Prefix(AIBattle __instance)"), "a non-skipping Prefix");
        int fin = patch.IndexOf("[HarmonyFinalizer]", StringComparison.Ordinal);
        AssertTrue(fin >= 0, "a Finalizer, so a throw inside vanilla cannot leave the stamp set");
        string finalizer = patch.Substring(fin);
        AssertTrue(finalizer.Contains("EnemyRetreatGuard.Clear()") || finalizer.Contains("WlOrderFlowRuntime.ClearRemovalSource()"), "the Finalizer clears the stamp");
        AssertTrue(finalizer.Contains("return __exception;"), "and rethrows vanilla's own exception");
        AssertTrue(patch.Contains("tactical.patch.wl-removal-source.prefix") && patch.Contains("tactical.patch.wl-removal-source.finalizer"), "scope ids");
        AssertTrue(patch.Contains("PatchFirstFire.Mark(\"tactical.patch.wl-removal-source.prefix\")") && patch.Contains("PatchFirstFire.Mark(\"tactical.patch.wl-removal-source.finalizer\")"), "first-fire marks");
        AssertTrue(patch.Contains("__instance.isplayeraiorfeud"), "the stamp is the instance's side");
    }

    private static void GuardRunsBeforeTheFlowSwitches()
    {
        string flow = Code("src/WhiskeyRealism/Tactical/PlayerOrders/WlOrderFlowRuntime.cs");
        int start = flow.IndexOf("private static CaptureVerdict Route(", StringComparison.Ordinal);
        string route = flow.Substring(start, flow.IndexOf("private static CaptureVerdict RouteCampaign(", start, StringComparison.Ordinal) - start);
        int guard = route.IndexOf("TryDropEnemySideRetreat(", StringComparison.Ordinal);
        int switches = route.IndexOf("PassesUntouched(", StringComparison.Ordinal);
        AssertTrue(guard >= 0 && switches > guard, "a bug fix does not depend on the order flow's own switches or Commander Mode");
        string body = BodyOf(flow, "private static bool TryDropEnemySideRetreat(");
        AssertTrue(body.Contains("EnemyRetreatGuardOn()"), "behind its own flag");
        AssertTrue(body.Contains("ReachesPlayerPanel(call.Unit, call.Type, call.Campaign)"), "only a retreat that would have reached the panel is a drop");
        AssertTrue(body.Contains("EnemyRetreatGuard.IsEnemySideRetreat("), "the pure rule decides");
        AssertTrue(body.Contains("enemy-side-retreat"), "named in the row");
    }

    // ------------------------------------------------------------ BUG-WL-020

    private static void FowRestoredOnlyWhenNotLaunchedAndPaperMapOpen()
    {
        foreach (int id in new[] { 60, 61, 64 })
            AssertTrue(LostOrdersFow.ShouldRestore(id, useFow: true, launchedDirectly: true, skipInfos: false, launched: false, paperMapOpen: true), "incident " + id + " that did not apply with the paper map open");
        foreach (int id in new[] { 0, 59, 62, 63, 65, 67 })
            AssertFalse(LostOrdersFow.ShouldRestore(id, true, true, false, false, true), "incident " + id + " never touches the grid");
        AssertFalse(LostOrdersFow.ShouldRestore(60, true, true, false, launched: true, paperMapOpen: true), "an incident that applied hides the grid on purpose");
        AssertFalse(LostOrdersFow.ShouldRestore(60, true, true, false, false, paperMapOpen: false), "with the paper map closed the grid is hidden anyway and returns with the map (29015)");
        AssertFalse(LostOrdersFow.ShouldRestore(60, useFow: false, true, false, false, true), "no fog of war, no grid");
        AssertFalse(LostOrdersFow.ShouldRestore(60, true, launchedDirectly: false, false, false, true), "a roll that did not start here (60 returns before it hides the grid)");
    }

    private static void FowNotRestoredOnSkipInfosRelaunch()
    {
        // RelaunchIncidents re-applies an active incident with skipinfos: true, hides the grid on purpose
        // (46530) and returns false (46550-46552); that grid must stay hidden.
        foreach (int id in new[] { 60, 61, 64 })
            AssertFalse(LostOrdersFow.ShouldRestore(id, true, true, skipInfos: true, launched: false, paperMapOpen: true), "relaunch of incident " + id);
    }

    private static void FowPatchShape()
    {
        string patch = Code("src/WhiskeyRealism/Patches/LostOrdersFowFixPatch.cs");
        AssertTrue(patch.Contains("[HarmonyPatch(typeof(DLC_WL), nameof(DLC_WL.LaunchIncident))]"), "the incident launcher");
        AssertTrue(patch.Contains("[HarmonyPostfix]") && !patch.Contains("[HarmonyPrefix]") && !patch.Contains("[HarmonyTranspiler]"), "a Postfix only");
        AssertTrue(patch.Contains("LostOrdersFow.ShouldRestore("), "the pure rule decides");
        AssertTrue(patch.Contains("ShowFOWLayer(true)") || patch.Contains("ShowFOWLayer(active: true)"), "vanilla's own toggle, back on");
        AssertTrue(patch.Contains("campaign.patch.lost-orders-fow") && patch.Contains("PatchFirstFire.Mark(\"campaign.patch.lost-orders-fow\")"), "scope id and first-fire mark");
        AssertTrue(patch.Contains("FixLostOrdersFogGrid"), "behind its flag");
    }

    // ------------------------------------------------------------ BUG-WL-022

    private static void StaleIssuerReplacedOnlyWhenNotCommanding()
    {
        // Issuer 7 still commands its group: nothing to fix.
        AssertFalse(StaleIssuer.TryReplacement(7, issuerExists: true, issuerCommandOwnerId: 7, groupCommanderId: 9, chosenId: 3, out _), "the issuer still commands");
        // Dead or relieved: no command left (-1).
        AssertTrue(StaleIssuer.TryReplacement(7, true, issuerCommandOwnerId: -1, groupCommanderId: 9, chosenId: 3, out int a), "no command left");
        AssertEqual(9, a, "the order group's current commander");
        // Replaced: another commander commands the issuer's command now.
        AssertTrue(StaleIssuer.TryReplacement(7, true, issuerCommandOwnerId: 11, groupCommanderId: 9, chosenId: 3, out int b), "another commander commands it");
        AssertEqual(9, b, "replacement");
        // The stored id is gone from the roster.
        AssertTrue(StaleIssuer.TryReplacement(77, issuerExists: false, -1, 9, 3, out int c), "out of range");
        AssertEqual(9, c, "replacement for a missing commander");
        // Never the player himself, never an unknown group commander, never the same id.
        AssertFalse(StaleIssuer.TryReplacement(7, true, -1, groupCommanderId: 3, chosenId: 3, out _), "the order group is the player's own: no portrait of himself as superior");
        AssertFalse(StaleIssuer.TryReplacement(7, true, -1, groupCommanderId: -1, chosenId: 3, out _), "no group commander to show");
        AssertFalse(StaleIssuer.TryReplacement(7, true, -1, groupCommanderId: 7, chosenId: 3, out _), "already the same commander");
    }

    private static void StaleIssuerWired()
    {
        string flow = Code("src/WhiskeyRealism/Tactical/PlayerOrders/WlOrderFlowRuntime.cs");
        string pass = BodyOf(flow, "internal static void OnDeliveryPass(");
        AssertTrue(pass.Contains("CorrectStaleIssuer();"), "on each delivery pass");
        string body = BodyOf(flow, "private static void CorrectStaleIssuer(");
        AssertTrue(body.Contains("TextOn()"), "behind the text switch (N3)");
        AssertTrue(body.Contains("StaleIssuer.TryReplacement("), "the pure rule decides");
        AssertTrue(body.Contains(".commanderid = "), "it writes the portrait id");
        AssertFalse(Regex.IsMatch(body, @"\.(content|position|type|groupunit|headline)\s*=(?!=)"), "and nothing else of the order");
    }

    private static void BugFixFlagsDescribed()
    {
        string plugin = TestFixtures.ReadRepoFile("src/WhiskeyRealism/Plugin.cs");
        foreach ((string name, string bug) in new[] { ("Enable W&L Enemy Retreat Order Guard", "BUG-WL-019"), ("Fix Lost Orders Fog Grid", "BUG-WL-020") })
        {
            int at = plugin.IndexOf("\"" + name + "\"", StringComparison.Ordinal);
            AssertTrue(at >= 0, name + " exists");
            string block = plugin.Substring(at, 800);
            AssertTrue(block.Contains("true,"), name + " defaults on");
            AssertTrue(block.Contains("Default ON.") && block.Contains(bug), name + " is described honestly");
        }
    }

    private static string BodyOf(string code, string signature)
    {
        int start = code.IndexOf(signature, StringComparison.Ordinal);
        AssertTrue(start >= 0, signature + " exists");
        int brace = code.IndexOf('{', start);
        int depth = 0;
        for (int i = brace; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0) return code.Substring(start, i - start + 1);
        }
        throw new InvalidOperationException("unbalanced braces after " + signature);
    }
}
