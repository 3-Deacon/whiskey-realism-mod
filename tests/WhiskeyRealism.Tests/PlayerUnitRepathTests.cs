using System;
using System.Linq;
using WhiskeyRealism.Util;
using System.Reflection;
using System.Text.RegularExpressions;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using static TestHarness;
using static TestFixtures;

// BUG-WL-024 (smoke 2026-10-18): a W&L player's detached skirmisher company walked backwards toward his regiment and ignored his orders. Vanilla's
// unit-level skirmisher routines act on it with no W&L test and call RegimentSetPath directly, so no earlier row named them. This build only records
// which routine re-paths or re-moves a player-owned unit (PlayerUnitRepathObserverPatch) and fixes the queue-holder relation label. The runtime half
// is not compiled into this project, so its wiring is pinned at the source.
internal static class PlayerUnitRepathTests
{
    private const string PatchFile = "src/WhiskeyRealism/Patches/PlayerUnitRepathObserverPatch.cs";

    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("wl-skirmisher MARKER a routine names itself for its own unit only; a click names itself for every unit; nothing on the stack reads unattributed", MarkerAttribution),
            ("wl-skirmisher MARKER leaving restores the outer marker, a throw cannot leave one behind, and the battle reset clears it", MarkerNestingAndReset),
            ("wl-skirmisher MARKER every routine has its own name, and the observer's player-order test reads only the click", MarkerNames),
            ("wl-skirmisher BUDGET a key writes one row per 3 s and counts the repeats it hid; another unit, routine or kind is its own key", BudgetPerKey),
            ("wl-skirmisher BUDGET 24 rows per 10 s over all keys, the rows lost counted on the next row, a new window after 10 s", BudgetWindow),
            ("wl-skirmisher BUDGET the key cap, a clock that goes backwards, and reset", BudgetCapAndClock),
            ("wl-skirmisher BUDGET the marker and the budget allocate nothing once a key has been seen", HotPathAllocatesNothing),
            ("wl-skirmisher RELATION the queue holder is not the issuer: a player-owned target in an AI queue is ai-queue-to-player-unit, and the old label is gone", RelationLabels),
            ("wl-skirmisher RELATION the observer uses the pure classifier and names a click origin=player-ui", ObserverWiring),
            ("wl-skirmisher PATCH every suspect routine and both movers resolve on the real Assembly-CSharp with the parameter names the Postfixes bind", TargetsResolveOnTheGame),
            ("wl-skirmisher PATCH each routine has a Prefix that enters its marker and a Finalizer that leaves it, with no skip and no Transpiler", MarkerPatchesAreNonSkipping),
            ("wl-skirmisher PATCH a patch entry has its scope and first-fire mark, the observer flag, a player-owned battle unit test, never throws, and builds no string per call", EntryDiscipline),
            ("wl-skirmisher PATCH the observer writes nothing into the battle and reads no stack", WritesNothing),
            ("wl-skirmisher ROWS the row carries the routine and the context the next smoke needs, and is routed and described", RowsCarryTheFields),
            ("wl-skirmisher DOCS the bug entry, the catalog row, the telemetry rows and the smoke items name the observer", DocsNameTheObserver),
        });
    }

    // ------------------------------------------------------------------------------------------------------------ marker

    private static void MarkerAttribution()
    {
        RepathRoutineMarker.Reset();
        AssertEqual(RepathRoutine.Unattributed, RepathRoutineMarker.For(11), "nothing on the stack");
        RepathMarker none = RepathRoutineMarker.Enter(RepathRoutine.Fallback, 11);
        AssertEqual(RepathRoutine.Fallback, RepathRoutineMarker.For(11), "the fallback names itself for the unit it runs on");
        AssertEqual(RepathRoutine.Unattributed, RepathRoutineMarker.For(12), "and not for another unit");
        AssertFalse(RepathRoutineMarker.PlayerOrderActive, "a fallback is not the player's order");
        RepathRoutineMarker.Leave(none);
        RepathRoutineMarker.Enter(RepathRoutine.PlayerOrder, RepathRoutineMarker.AnyUnit);
        AssertEqual(RepathRoutine.PlayerOrder, RepathRoutineMarker.For(11), "a click names itself for the ordered unit");
        AssertEqual(RepathRoutine.PlayerOrder, RepathRoutineMarker.For(-999), "and for every unit beneath it, whatever its id");
        AssertTrue(RepathRoutineMarker.PlayerOrderActive, "the observer reads the click");
        RepathRoutineMarker.Reset();
    }

    private static void MarkerNestingAndReset()
    {
        RepathRoutineMarker.Reset();
        RepathMarker outer = RepathRoutineMarker.Enter(RepathRoutine.UnitLink, 5);
        RepathMarker inner = RepathRoutineMarker.Enter(RepathRoutine.Reattach, 5);
        AssertEqual(RepathRoutine.Reattach, RepathRoutineMarker.For(5), "the inner routine");
        RepathRoutineMarker.Leave(inner);
        AssertEqual(RepathRoutine.UnitLink, RepathRoutineMarker.For(5), "leaving the inner restores the outer");
        RepathRoutineMarker.Leave(outer);
        AssertEqual(RepathRoutine.Unattributed, RepathRoutineMarker.For(5), "leaving the outer restores none");

        try
        {
            RepathMarker entered = RepathRoutineMarker.Enter(RepathRoutine.Fallback, 5);
            try { throw new InvalidOperationException("vanilla threw"); }
            finally { RepathRoutineMarker.Leave(entered); }
        }
        catch (InvalidOperationException)
        {
        }
        AssertEqual(RepathRoutine.Unattributed, RepathRoutineMarker.For(5), "a throw through the routine leaves no marker (the Finalizer leaves)");

        RepathRoutineMarker.Enter(RepathRoutine.Fallback, 5);
        BattleScope.ResetAll();
        AssertEqual(RepathRoutine.Unattributed, RepathRoutineMarker.For(5), "the battle teardown clears a marker left over");
        AssertTrue(BattleScope.RegisteredNames.Contains("player-unit-repath-marker"), "the marker registers its own reset with BattleScope");
    }

    private static void MarkerNames()
    {
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (RepathRoutine routine in Enum.GetValues(typeof(RepathRoutine)))
        {
            string name = RepathRoutineMarker.Name(routine);
            AssertTrue(name != "unknown" && name.Length > 0, routine + " has a name");
            AssertTrue(seen.Add(name), "and it is its own: " + name);
        }
        AssertEqual("unknown", RepathRoutineMarker.Name((RepathRoutine)99), "a value outside the enum");
        AssertEqual("fallback", RepathRoutineMarker.Name(RepathRoutine.Fallback), "the telemetry text the docs promise");
        AssertEqual("player-order", RepathRoutineMarker.Name(RepathRoutine.PlayerOrder), "player-order");
        AssertEqual("skirmisher-target", RepathRoutineMarker.Name(RepathRoutine.SkirmisherTarget), "skirmisher-target");
        AssertEqual("unit-link", RepathRoutineMarker.Name(RepathRoutine.UnitLink), "unit-link");
        AssertEqual("reattach", RepathRoutineMarker.Name(RepathRoutine.Reattach), "reattach");
        AssertEqual("contact-interruption", RepathRoutineMarker.Name(RepathRoutine.ContactInterruption), "contact-interruption");
        AssertEqual("unattributed", RepathRoutineMarker.Name(RepathRoutine.Unattributed), "unattributed");
        RepathRoutineMarker.Reset();
        RepathRoutineMarker.Enter(RepathRoutine.Reattach, 1);
        AssertFalse(RepathRoutineMarker.PlayerOrderActive, "only the click is a player order");
        RepathRoutineMarker.Reset();
    }

    // ------------------------------------------------------------------------------------------------------------ budget

    private static void BudgetPerKey()
    {
        var budget = new RepathRowBudget();
        AssertTrue(budget.TryTake(7, RepathRoutine.Fallback, RepathKind.Path, 100f, out int suppressed, out int dropped), "the first row for a key is written");
        AssertEqual(0, suppressed, "nothing hidden yet");
        AssertEqual(0, dropped, "nothing lost");
        AssertFalse(budget.TryTake(7, RepathRoutine.Fallback, RepathKind.Path, 101f, out _, out _), "the same key a second later is hidden");
        AssertFalse(budget.TryTake(7, RepathRoutine.Fallback, RepathKind.Path, 102.9f, out _, out _), "and again just inside 3 s");
        AssertTrue(budget.TryTake(7, RepathRoutine.Fallback, RepathKind.Path, 103f, out suppressed, out _), "at 3 s the key writes again");
        AssertEqual(2, suppressed, "and says how many repeats it hid");
        AssertTrue(budget.TryTake(7, RepathRoutine.Fallback, RepathKind.Mode, 101f, out _, out _), "the mode kind of the same routine is its own key");
        AssertTrue(budget.TryTake(7, RepathRoutine.UnitLink, RepathKind.Path, 101f, out _, out _), "another routine on the same unit is its own key: the loop of two routines stays visible");
        AssertTrue(budget.TryTake(8, RepathRoutine.Fallback, RepathKind.Path, 101f, out _, out _), "another unit is its own key");
        AssertTrue(budget.TryTake(-7, RepathRoutine.Fallback, RepathKind.Path, 101f, out _, out _), "a negative instance id (Unity ids are) is its own key, not 7's");
        AssertEqual(5, budget.KeyCount, "five keys");
    }

    private static void BudgetWindow()
    {
        var budget = new RepathRowBudget();
        int taken = 0;
        for (int unit = 1; unit <= 40; unit++)
            if (budget.TryTake(unit, RepathRoutine.Fallback, RepathKind.Path, 50f, out _, out _)) taken++;
        AssertEqual(RepathRowBudget.WindowRows, taken, "24 rows in the window, whatever the number of units");
        AssertEqual(16, budget.Dropped, "the 16 lost are counted");
        AssertFalse(budget.TryTake(99, RepathRoutine.Fallback, RepathKind.Path, 59.9f, out _, out _), "still inside the 10 s window");
        AssertEqual(17, budget.Dropped, "and counted");
        AssertTrue(budget.TryTake(41, RepathRoutine.Fallback, RepathKind.Path, 60f, out int suppressed, out int dropped), "a new window opens after 10 s");
        AssertEqual(17, dropped, "the first row of it carries what the last window lost");
        AssertEqual(0, suppressed, "a new key hid nothing");
        AssertEqual(0, budget.Dropped, "and the count starts again");
        // A key refused by the window keeps its place: it is not marked as written, so it writes as soon as there is room.
        var tight = new RepathRowBudget();
        for (int unit = 1; unit <= RepathRowBudget.WindowRows; unit++) tight.TryTake(unit, RepathRoutine.Fallback, RepathKind.Path, 0f, out _, out _);
        AssertFalse(tight.TryTake(500, RepathRoutine.Fallback, RepathKind.Path, 1f, out _, out _), "a full window refuses a new key");
        AssertTrue(tight.TryTake(500, RepathRoutine.Fallback, RepathKind.Path, 10f, out _, out int lost), "and takes it in the next window");
        AssertEqual(1, lost, "having counted the refusal");
    }

    private static void BudgetCapAndClock()
    {
        var capped = new RepathRowBudget();
        for (int i = 0; i < RepathRowBudget.MaxKeys; i++)
            capped.TryTake(i + 1, RepathRoutine.Fallback, RepathKind.Path, i * 20f, out _, out _);
        AssertEqual(RepathRowBudget.MaxKeys, capped.KeyCount, "the cap is reached");
        AssertFalse(capped.TryTake(RepathRowBudget.MaxKeys + 5, RepathRoutine.Fallback, RepathKind.Path, 1e6f, out _, out _), "a new key past the cap is refused");
        AssertEqual(1, capped.Dropped, "and counted");
        AssertEqual(RepathRowBudget.MaxKeys, capped.KeyCount, "the table does not grow");
        AssertTrue(capped.TryTake(1, RepathRoutine.Fallback, RepathKind.Path, 1e6f, out _, out _), "a key already known still writes");

        var clock = new RepathRowBudget();
        AssertTrue(clock.TryTake(1, RepathRoutine.Fallback, RepathKind.Path, 500f, out _, out _), "a row at 500 s");
        AssertTrue(clock.TryTake(1, RepathRoutine.Fallback, RepathKind.Path, 5f, out _, out _), "a clock that restarted (a new session) does not hide the key for ever");

        var full = new RepathRowBudget();
        for (int unit = 1; unit <= RepathRowBudget.WindowRows; unit++) full.TryTake(unit, RepathRoutine.Fallback, RepathKind.Path, 500f, out _, out _);
        AssertTrue(full.TryTake(900, RepathRoutine.Fallback, RepathKind.Path, 5f, out _, out _), "a full window at 500 s does not refuse rows for ever once the clock restarts at 5 s");

        clock.Reset();
        AssertEqual(0, clock.KeyCount, "reset empties the keys");
        AssertEqual(0, clock.Dropped, "and the count");
        AssertTrue(clock.TryTake(1, RepathRoutine.Fallback, RepathKind.Path, 5f, out _, out _), "and the window");
    }

    private static void HotPathAllocatesNothing()
    {
        var budget = new RepathRowBudget();
        for (int unit = 1; unit <= 8; unit++) budget.TryTake(unit, RepathRoutine.Fallback, RepathKind.Path, 0f, out _, out _);
        RepathRoutineMarker.Reset();
        long mark = BeginAllocationWindow();
        int sink = 0;
        for (int i = 0; i < 2000; i++)
        {
            RepathMarker previous = RepathRoutineMarker.Enter(RepathRoutine.Fallback, 3);
            if (RepathRoutineMarker.For(3) == RepathRoutine.Fallback) sink++;
            RepathRoutineMarker.Leave(previous);
            if (budget.TryTake(1 + (i & 7), RepathRoutine.Fallback, RepathKind.Path, 0.5f, out int suppressed, out _)) sink += 1000;
            sink += suppressed & 0;
        }
        AssertEqual(2000, sink, "the marker saw every call, the budget wrote none inside 3 s");
        AssertTrue(AllocatedSince(mark) < 2048, "2000 calls allocate nothing: " + AllocatedSince(mark) + " bytes");
    }

    // ------------------------------------------------------------------------------------------------------------ relation

    private static void RelationLabels()
    {
        AssertEqual("ai-queue-to-player-unit", OrderRelation.Classify(false, true), "the player's click on his own unit sits in the brigade's queue (the scene parent holds it)");
        AssertEqual("player-chain", OrderRelation.Classify(true, true), "a player-owned holder, a player-owned target");
        AssertEqual("player-to-ai", OrderRelation.Classify(true, false), "a player-owned holder, an AI target");
        AssertEqual("ai-chain", OrderRelation.Classify(false, false), "AI to AI");
        string observer = FlankResponseTests.SourceText("Patches", "TacticalObserverPatch.cs");
        AssertFalse(observer.Contains("ai-to-player-subordinate"), "the label that named an issuer the queue cannot show is gone from the observer");
        AssertFalse(Regex.IsMatch(observer, @"string OrderRelation\("), "and so is the private copy of the rule");
    }

    private static void ObserverWiring()
    {
        string observer = UnitChurnTests.Code(FlankResponseTests.SourceText("Patches", "TacticalObserverPatch.cs"));
        AssertEqual(2, Regex.Matches(FlankResponseTests.Flat(observer), Regex.Escape("string relation = WhiskeyRealism.Tactical.OrderRelation.Classify(sourceUnderCommander, targetUnderCommander);")).Count,
            "the queued row and the courier row classify through the pure rule with the holder's and the target's flags");
        AssertEqual(2, Regex.Matches(observer, Regex.Escape("OrderRelation.Classify(")).Count, "and nothing else classifies");
        UnitChurnTests.PinStatement(observer,
            "string origin = WhiskeyRealism.Tactical.Battle.OrderIssueOrigin.Current ?? (WhiskeyRealism.Tactical.Battle.RepathRoutineMarker.PlayerOrderActive ? \"player-ui\" : \"other\");",
            "the executor's source first, then a click through SetWaypoint, else other");
    }

    // ------------------------------------------------------------------------------------------------------------ patch

    private static void TargetsResolveOnTheGame()
    {
        foreach (string name in new[] { "CheckFallbackDueToEnemyClose", "CheckNewTargetsOfSkirmishers", "UpdateUnitLink", "CheckForReattachmentOrder", "CheckEnemyContact" })
        {
            MethodInfo m = typeof(Regiment).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            AssertTrue(m != null, "Regiment." + name + " exists");
            AssertEqual(0, m.GetParameters().Length, name + " takes no argument, as the markers' Prefix and Finalizer assume");
            AssertEqual(1, typeof(Regiment).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Count(x => x.Name == name), name + " has one overload, so the attribute's name is enough");
        }

        MethodInfo path = typeof(Regiment).GetMethod("RegimentSetPath");
        AssertTrue(path != null, "Regiment.RegimentSetPath exists");
        AssertEqual(1, typeof(Regiment).GetMethods().Count(x => x.Name == "RegimentSetPath"), "one overload");
        string[] names = path.GetParameters().Select(p => p.Name).ToArray();
        foreach (string bound in new[] { "target", "newpath", "modifylastwaypoint", "state", "uimovement" })
            AssertTrue(names.Contains(bound), "RegimentSetPath parameter " + bound + " (the Postfix binds it by name)");
        AssertEqual(typeof(UnityEngine.Vector3), path.GetParameters().First(p => p.Name == "target").ParameterType, "target is a Vector3");

        MethodInfo mode = typeof(Regiment).GetMethod("SetMovementMode");
        AssertTrue(mode != null && mode.GetParameters().Length == 1 && mode.GetParameters()[0].Name == "newmode", "SetMovementMode(int newmode)");
        AssertEqual(1, typeof(Regiment).GetMethods().Count(x => x.Name == "SetMovementMode"), "one overload");

        var setWaypoint = typeof(BattleUnits).GetMethod("SetWaypoint", new[]
        {
            typeof(Regiment), typeof(UnityEngine.Vector3), typeof(bool), typeof(bool), typeof(float), typeof(bool), typeof(bool), typeof(float), typeof(int),
            typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool)
        });
        AssertTrue(setWaypoint != null, "BattleUnits.SetWaypoint(Regiment, ...) with 15 arguments");
        AssertEqual("showmovementoptions", setWaypoint.GetParameters()[9].Name, "the Prefix binds showmovementoptions by name");

        foreach (string field in new[] { "dlcw_isundercommander", "calledfromcampaign", "reattachmentorder", "unitlinkedto", "parentregiment", "regimentpaths", "movementmode", "ai_stance", "combatbehavior", "unittyp", "alliance", "unitrange" })
            AssertTrue(typeof(Regiment).GetField(field) != null, "Regiment." + field);
        AssertEqual(typeof(UnityEngine.GameObject), typeof(Regiment).GetField("unitlinkedto").FieldType, "unitlinkedto is a GameObject");
        AssertEqual(typeof(UnityEngine.GameObject), typeof(Regiment).GetField("parentregiment").FieldType, "parentregiment is a GameObject");
        AssertTrue(typeof(Regiment).GetField("unitrange").FieldType.GetField("closestenemyunitfarreg") != null, "unitrange.closestenemyunitfarreg");
    }

    private static void MarkerPatchesAreNonSkipping()
    {
        string patch = ReadRepoFile(PatchFile);
        string code = UnitChurnTests.Code(patch);
        AssertFalse(code.Contains("HarmonyTranspiler"), "no Transpiler");
        AssertFalse(Regex.IsMatch(code, @"internal static bool \w+\(") , "no Prefix returns bool, so none can skip vanilla");
        AssertFalse(Regex.IsMatch(code, @"__result\b"), "and none overrides a result");
        foreach (string routine in new[] { "CheckFallbackDueToEnemyClose", "CheckNewTargetsOfSkirmishers", "UpdateUnitLink", "CheckForReattachmentOrder", "CheckEnemyContact" })
        {
            AssertEqual(1, Regex.Matches(code, @"\[HarmonyPatch\(typeof\(Regiment\), """ + routine + @"""\)\]\s*\[HarmonyPrefix\]").Count, routine + ": one Prefix");
            AssertEqual(1, Regex.Matches(code, @"\[HarmonyPatch\(typeof\(Regiment\), """ + routine + @"""\)\]\s*\[HarmonyFinalizer\]").Count, routine + ": one Finalizer that leaves the marker even when vanilla throws");
        }
        AssertEqual(1, Regex.Matches(code, @"\[HarmonyFinalizer\]\s*internal static void SetWaypointFinalizer\(").Count, "the click marker leaves in a Finalizer");
        UnitChurnTests.PinStatement(code, "if (!showmovementoptions || !Enabled()) return;", "only a UI call (showmovementoptions) is the player's order");
        UnitChurnTests.PinStatement(code, "__state.Previous = RepathRoutineMarker.Enter(RepathRoutine.PlayerOrder, RepathRoutineMarker.AnyUnit);", "a click names every unit beneath it");
        AssertEqual(7, Regex.Matches(code, Regex.Escape("if (!Enabled() || !Tracked(__instance)) return;")).Count, "only a player-owned unit's routine (or mover) is observed: five routine Prefixes and two mover Prefixes");
        AssertEqual(6, Regex.Matches(code, Regex.Escape("if (!__state.Entered) return;")).Count, "a routine or click that never entered leaves nothing to leave (five Finalizers and the click's)");
        UnitChurnTests.PinStatement(code, "RepathRoutineMarker.Leave(state.Previous);", "the routine Finalizer restores the marker it replaced");
        UnitChurnTests.PinStatement(code, "RepathRoutineMarker.Leave(__state.Previous);", "and so does the click's");
        // Each vanilla routine is marked with its own name, not another's.
        foreach (var (method, routine) in new[]
        {
            ("CheckFallbackDueToEnemyClose", "Fallback"), ("CheckNewTargetsOfSkirmishers", "SkirmisherTarget"), ("UpdateUnitLink", "UnitLink"),
            ("CheckForReattachmentOrder", "Reattach"), ("CheckEnemyContact", "ContactInterruption"),
        })
        {
            string head = @"\[HarmonyPatch\(typeof\(Regiment\), """ + method + @"""\)\]\s*\[Harmony";
            AssertTrue(Regex.IsMatch(code, head + @"Prefix\]\s*internal static void \w+\([^)]*\)\s*\{[^}]*?Enter\(__instance, RepathRoutine\." + routine + @", ref __state\);"), method + " enters " + routine);
            AssertTrue(Regex.IsMatch(code, head + @"Finalizer\]\s*internal static void \w+\([^)]*\)\s*\{[^}]*?Leave\(__instance, RepathRoutine\." + routine + @", __state\);"), method + " leaves " + routine);
        }
        UnitChurnTests.PinStatement(code, "state.Previous = RepathRoutineMarker.Enter(routine, unit.GetInstanceID());", "a routine names its own unit, not every unit");
        UnitChurnTests.PinStatement(code, "if (linked == state.LinkedId && reattach == state.Reattach) return;", "a routine that changed neither link nor reattach writes no routine row");
    }

    private static void EntryDiscipline()
    {
        string code = UnitChurnTests.Code(ReadRepoFile(PatchFile));
        AssertEqual(16, Regex.Matches(code, @"\[Harmony(Prefix|Postfix|Finalizer)\]").Count, "16 patch entries: the click, ten routine entries, RegimentSetPath and SetMovementMode");
        AssertEqual(16, Regex.Matches(code, @"using \(TelemetryPerf\.Scope\(").Count, "each opens a scope");
        AssertEqual(16, Regex.Matches(code, @"PatchFirstFire\.Mark\(").Count, "and marks first-fire");
        // PerfHotPathTests pins literal names; here, that no entry that fires for every regiment opens its scope before it has tested the unit.
        foreach (Match entry in Regex.Matches(code, @"internal static void (\w+)\((?<args>[^)]*)\)\s*\{(?<body>.*?)using \(TelemetryPerf\.Scope\(", RegexOptions.Singleline))
        {
            string name = entry.Groups[1].Value, head = entry.Groups["body"].Value;
            bool gated = head.Contains("return;");
            AssertTrue(gated, name + " returns before its scope when there is nothing to observe");
        }
        AssertTrue(Regex.IsMatch(code, @"Plugin\.Instance\.Enabled\.Value\s*&&\s*Plugin\.Instance\.EnableTacticalObserver\.Value"), "under Enable Tactical Observer, the rollback valve");
        UnitChurnTests.PinStatement(code, "return (UnityEngine.Object)unit != null && !unit.calledfromcampaign && unit.dlcw_isundercommander;", "a battle unit the W&L player owns, the flag DetachCompany copies to a company");
        AssertTrue(code.Contains("catch (Exception ex)") && code.Contains("Warn(ex);"), "a failure is caught, never thrown out of the patch");
        AssertTrue(code.Contains("OnceLog.Warning(\"tactical-battle-core:player-unit-repath-failed\""), "and warned once, by a static key");
        AssertTrue(code.Contains("BattleScope.Register(\"player-unit-repath-budget\", Budget.Reset);"), "the budget's reset is registered");
        AssertFalse(Regex.IsMatch(code, @"Scope\([^)]*\+"), "no scope id is concatenated per call");
        AssertFalse(Regex.IsMatch(code, @"Mark\([^)]*\+"), "nor a first-fire id");
        // The Postfix tests its saved state before it opens a scope: an AI unit's call costs one field read.
        AssertEqual(2, Regex.Matches(code, Regex.Escape("if (!__state.Tracked) return;")).Count, "in both Postfixes");
    }

    private static void WritesNothing()
    {
        string code = UnitChurnTests.Code(ReadRepoFile(PatchFile));
        foreach (string call in new[] { ".RegimentSetPath(", ".SetWaypoint(", ".StopRegiment(", ".ChangeStance(", ".SetGroupFormation(", ".ReattachUnits(", "ReattachUnits(", ".SetMovementMode(", ".AddToOrderQueue(" })
            AssertFalse(code.Contains(call), "no call to " + call);
        foreach (string field in new[] { "unitlinkedto", "reattachmentorder", "movementmode", "regimentpaths", "ai_stance", "combatbehavior" })
            AssertFalse(Regex.IsMatch(code, @"\." + field + @"\s*=[^=]"), "no assignment to " + field);
        AssertFalse(code.Contains("StackTrace") || code.Contains("System.Diagnostics"), "no stack is read: the callers are named by markers (the brief's rule)");
        AssertFalse(code.Contains("GetClosestEnemyUnit"), "no vanilla search is run to fill a row: the range list's own field is read");
        AssertTrue(code.Contains("EventName = \"TacticalPlayerUnitRepath\""), "one event name");
    }

    // ------------------------------------------------------------------------------------------------------------ rows

    private static void RowsCarryTheFields()
    {
        string code = UnitChurnTests.Code(ReadRepoFile(PatchFile));
        foreach (string field in new[] { "kind", "routine", "unittyp", "detached", "toX", "toZ", "newPath", "modifyLast", "state", "uiMove", "paths", "mode", "linkedTo", "reattach", "stance", "behavior", "enemyM", "parentM", "suppressed", "dropped", "requested" })
            AssertTrue(code.Contains(".WithField(\"" + field + "\""), "the row carries " + field);
        foreach (string kind in new[] { "path", "mode", "routine" })
            AssertTrue(code.Contains(".WithField(\"kind\", \"" + kind + "\")"), "kind=" + kind);
        // One budget gate per row kind, each before the row is built; a mode that did not change writes nothing.
        UnitChurnTests.PinStatement(code, "if (!Budget.TryTake(id, routine, RepathKind.Path, Time.realtimeSinceStartup, out int suppressed, out int dropped)) return;", "the path row");
        UnitChurnTests.PinStatement(code, "if (!Budget.TryTake(id, routine, RepathKind.Mode, Time.realtimeSinceStartup, out int suppressed, out int dropped)) return;", "the mode row");
        UnitChurnTests.PinStatement(code, "if (!Budget.TryTake(id, routine, RepathKind.Routine, Time.realtimeSinceStartup, out int suppressed, out int dropped)) return;", "the routine row");
        UnitChurnTests.PinStatement(code, "if (after == __state.Mode) return;", "a SetMovementMode that changed nothing writes no row");
        AssertEqual(2, Regex.Matches(code, Regex.Escape("RepathRoutine routine = RepathRoutineMarker.For(id);")).Count, "the path and the mode row read the routine on the stack for this unit's id");
        string policy = FlankResponseTests.SourceText("Telemetry", "TelemetryTagPolicy.cs");
        FlankResponseTests.AssertPinned(policy, "AddTactical(\"TacticalPlayerUnitRepath\", TelemetryCategory.State);", "the tag is routed as State (Trace sheds first)");
        string plugin = FlankResponseTests.SourceText("Plugin.cs");
        FlankResponseTests.AssertPinned(plugin, "BUG-WL-024 record-only PlayerUnitRepathObserverPatch", "the observer flag describes the rows");
        FlankResponseTests.AssertPinned(plugin, "origin=player-ui", "and the origin");
        FlankResponseTests.AssertPinned(plugin, "\"Enable Tactical Observer\",\n                true,", "the flag stays default on");
    }

    private static void DocsNameTheObserver()
    {
        string bug = ReadRepoFile("docs/bug-fixes/vanilla-wl-career.md");
        AssertEqual(1, Regex.Matches(bug, @"\| `BUG-WL-024` \|").Count, "one BUG-WL-024 entry, the next id after BUG-WL-023");
        foreach (string anchor in new[] { "128924", "128847", "124394", "129520", "5101-5115", "131277-131290", "90477", "PlayerUnitRepathObserverPatch", "ai-queue-to-player-unit" })
            AssertContains(bug, anchor, "the entry cites " + anchor);
        AssertContains(ReadRepoFile("docs/bug-fixes/README.md"), "BUG-WL-024", "the index lists it");
        AssertContains(ReadRepoFile("docs/patch-catalog.md"), "PlayerUnitRepathObserverPatch", "the catalog lists the patch");
        string telemetry = ReadRepoFile("docs/telemetry.md");
        foreach (string text in new[] { "TacticalPlayerUnitRepath", "routine=player-order", "unattributed", "origin=player-ui", "ai-queue-to-player-unit", "player-unit-repath-failed", "S13", "S14" })
            AssertContains(telemetry, text, "docs/telemetry.md names " + text);
        AssertFalse(telemetry.Contains("relation=ai-to-player-subordinate"), "and no longer promises the old label");
    }
}
