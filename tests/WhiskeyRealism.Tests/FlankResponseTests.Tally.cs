using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using static TestHarness;

// BC11 decision 6: the flank tally patch (FlankThreatTallyFixPatch) and the measurement of vanilla's own flank moves.
internal static partial class FlankResponseTests
{
    private static (string, Action)[] TallyTests()
    {
        return new (string, Action)[]
        {
            ("bc11 FlankThreatTallyIsPerSubordinate", FlankThreatTallyIsPerSubordinate),
            ("bc11 TallyMatchesVanillaWeightTable", TallyMatchesVanillaWeightTable),
            ("bc11 TallyRunsWhenBuiNullOrOverride", TallyRunsWhenBuiNullOrOverride),
            ("bc11 TallySkippedOnVanillaEarlyReturn", TallySkippedOnVanillaEarlyReturn),
            ("bc11 TallyFilterPinnedToDecompile", TallyFilterPinnedToDecompile),
            ("bc11 TallyPatchIsScopedFlaggedAndReadOnlyToVanillasField", TallyPatchIsScopedFlaggedAndReadOnlyToVanillasField),
            ("bc11 VanillaFlankMoveRowOnLastAiOrderChange", VanillaFlankMoveRowOnLastAiOrderChange),
            ("bc11 FlankMoveWatchAllocatesNothingAfterWarmup", FlankMoveWatchAllocatesNothingAfterWarmup),
            ("bc11 SmokeLogLinesMatchTheChecklist", SmokeLogLinesMatchTheChecklist),
        };
    }

    // Vanilla UpdateGroupStrength 120566-120580 adds the weight of the GROUP's own outflanked code once per counted subordinate.
    private static int VanillaTally(int groupCode, int countedSubordinates)
    {
        return countedSubordinates * FlankThreatTally.Weight(groupCode);
    }

    private static int PatchedTally(int[] subordinateCodes)
    {
        int points = 0;
        for (int i = 0; i < subordinateCodes.Length; i++) points += FlankThreatTally.Weight(subordinateCodes[i]);
        return points;
    }

    // Four subordinates, two outflanked with code 1, the headquarters (the group's own code) 0: two weight points; vanilla gives 0.
    private static void FlankThreatTallyIsPerSubordinate()
    {
        int[] subordinates = { 1, 1, 0, 0 };
        AssertEqual(2, PatchedTally(subordinates), "each outflanked subordinate adds its own weight");
        AssertEqual(0, VanillaTally(groupCode: 0, countedSubordinates: 4), "vanilla reads the group's own code: 0 for a headquarters behind its line");
        AssertEqual(4, VanillaTally(groupCode: 1, countedSubordinates: 4), "and a group whose own code is set counts it for everybody");
        AssertEqual(6, PatchedTally(new[] { 3, 7, 0 }), "2 + 4 + 0");
    }

    private static void TallyMatchesVanillaWeightTable()
    {
        // 120566-120580: codes 1 and 2 -> 1, code 3 -> 2, codes 4, 5, 6 -> 3, code 7 -> 4; 0 and anything else adds nothing.
        int[] expected = { 0, 1, 1, 2, 3, 3, 3, 4 };
        for (int code = 0; code < expected.Length; code++) AssertEqual(expected[code], FlankThreatTally.Weight(code), "code " + code);
        AssertEqual(0, FlankThreatTally.Weight(8), "out of range");
        AssertEqual(0, FlankThreatTally.Weight(-1), "negative");
        // The consumer: strength / (1 + flanksthreated / flankinginfluenceonaidecisions (10 weight points)).
        AssertNear(100f / 1.3f, 100f / (1f + 3f / 10f), 0.001f, "GetGroupStrength divisor, for reference");
    }

    private static void TallyRunsWhenBuiNullOrOverride()
    {
        AssertTrue(FlankThreatTally.VanillaReachedLoop(battleUiMissing: true, selectedUnitIsThisGroup: false, overrideCalc: false), "no battle UI");
        AssertTrue(FlankThreatTally.VanillaReachedLoop(battleUiMissing: false, selectedUnitIsThisGroup: true, overrideCalc: false), "the selected unit is this group");
        AssertTrue(FlankThreatTally.VanillaReachedLoop(battleUiMissing: false, selectedUnitIsThisGroup: false, overrideCalc: true), "overridecalc");
    }

    private static void TallySkippedOnVanillaEarlyReturn()
    {
        AssertFalse(FlankThreatTally.VanillaReachedLoop(battleUiMissing: false, selectedUnitIsThisGroup: false, overrideCalc: false),
            "vanilla returned at 120140-120143: a battle UI, another selected unit, no override");
    }

    // 120284: !(array[m] != null && array[m].unittyp <= 13 && (activeInHierarchy || ignoreactivestate || (ignoreactivestateonlyforthisunit == this && !flag) || surrenderstate > 0)) -> continue
    private static void TallyFilterPinnedToDecompile()
    {
        AssertTrue(FlankThreatTally.Counts(13, true, false, false, false), "an active regiment counts");
        AssertFalse(FlankThreatTally.Counts(14, true, true, true, true), "a group (unittyp > 13) never counts");
        AssertFalse(FlankThreatTally.Counts(5, false, false, false, false), "an inactive regiment does not");
        AssertTrue(FlankThreatTally.Counts(5, false, true, false, false), "ignoreactivestate counts it");
        AssertTrue(FlankThreatTally.Counts(5, false, false, true, false), "the single exception for this inactive group counts it");
        AssertTrue(FlankThreatTally.Counts(5, false, false, false, true), "a surrendering regiment counts");

        // The decompile text the predicate and the patch are pinned to (UpdateGroupStrength, build 14703111, line 120284).
        const string vanilla = "!(((Object)(object)array[m] != (Object)null) || array[m].unittyp > 13 || !((((Component)array[m]).gameObject.activeInHierarchy || ignoreactivestate) | ((Object)(object)ignoreactivestateonlyforthisunit == (Object)(object)this && !flag) | (array[m].surrenderstate > 0)))";
        AssertTrue(vanilla.Contains("array[m].unittyp > 13") && vanilla.Contains("surrenderstate > 0") && vanilla.Contains("ignoreactivestate"), "the vanilla predicate has the four terms");
        string patch = SourceText("Patches", "FlankThreatTallyFixPatch.cs");
        string pure = SourceText("Tactical", "Battle", "FlankFacing.cs");
        string counts = Between(pure, "public static bool Counts(", "}");
        AssertPinned(counts, "unitTyp <= 13 && (activeInHierarchy || ignoreActiveState || exceptionIsThisGroupAndInactive || surrendering)", "the pure predicate");
        AssertPinned(patch, "FlankThreatTally.Counts(unit.unittyp, unit.gameObject.activeInHierarchy, ignoreactivestate, exceptionIsThisAndInactive, unit.surrenderstate > 0)", "the patch feeds it vanilla's four inputs");
        AssertPinned(patch, "(UnityEngine.Object)ignoreactivestateonlyforthisunit == (UnityEngine.Object)__instance", "the exception is compared with this group, as 120284 does");
        AssertPinned(patch, "&& !ignoreactivestateonlyforthisunit.gameObject.activeInHierarchy;", "and applies when that group is inactive (!flag)");
        AssertPinned(patch, "points += FlankThreatTally.Weight(unit.outflanked);", "each subordinate's own outflanked, not the group's");
        AssertPinned(patch, "if (__instance.calledfromcampaign || __instance.unittyp <= 13) return;", "battle groups only");
    }

    private static void TallyPatchIsScopedFlaggedAndReadOnlyToVanillasField()
    {
        string patch = SourceText("Patches", "FlankThreatTallyFixPatch.cs");
        AssertPinned(patch, "[HarmonyPatch(typeof(Regiment), \"UpdateGroupStrength\")]", "a Postfix on UpdateGroupStrength");
        AssertFalse(patch.Contains("[HarmonyPrefix]") || patch.Contains("[HarmonyTranspiler]"), "no Prefix, no Transpiler");
        AssertPinned(patch, "TelemetryPerf.Scope(\"tactical.patch.flank-threat-tally\"", "scope");
        AssertPinned(patch, "PatchFirstFire.Mark(\"tactical.patch.flank-threat-tally\");", "first fire");
        AssertPinned(patch, "BattleScope.Register(\"flank-threat-tally-log\"", "battle scope");
        AssertPinned(patch, "Plugin.EnableVanillaFlankThreatTallyFix != null && Plugin.EnableVanillaFlankThreatTallyFix.Value", "its own flag");
        AssertFalse(patch.Contains("SetWaypoint(") || patch.Contains("SetGroupFormation(") || patch.Contains("ChangeStance("), "not an order");
        string plugin = SourceText("Plugin.cs");
        AssertPinned(plugin, "\"Enable Vanilla Flank Threat Tally Fix\",\n                true,", "default on");
        AssertPinned(plugin, "\"Enable Flank Response And Envelopment\",\n                true,", "default on");
    }

    // One TacticalVanillaFlankMove row when an AI group's lastaiorder goes 0 -> 4 or 5 (117107).
    private static void VanillaFlankMoveRowOnLastAiOrderChange()
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        UnitFlags live = UnitFlags.Alive | UnitFlags.Active;
        s.AddOwn(new UnitRecord { InstanceId = 10, UnitTyp = 16, Flags = live | UnitFlags.IsGroup, LastAiOrder = 0 });
        s.AddOwn(new UnitRecord { InstanceId = 11, UnitTyp = 15, Flags = live | UnitFlags.IsGroup, LastAiOrder = 4 });
        s.AddOwn(new UnitRecord { InstanceId = 12, UnitTyp = 2, Flags = live | UnitFlags.IsInfantry, LastAiOrder = 4 });
        s.IndexCommandTree(null);
        var watch = new VanillaFlankMoveWatch();
        var ids = new int[8]; var directions = new int[8];
        AssertEqual(0, watch.Observe(s, ids, directions), "first sight records, reports nothing (group 11 was already mid-move)");

        s.Own[0].LastAiOrder = 4;
        AssertEqual(1, watch.Observe(s, ids, directions), "0 -> 4");
        AssertEqual(10, ids[0], "group 10");
        AssertEqual(0, directions[0], "direction = lastaiorder - 4: left");
        AssertEqual(0, watch.Observe(s, ids, directions), "4 -> 4 is the same move");
        s.Own[0].LastAiOrder = 0;
        AssertEqual(0, watch.Observe(s, ids, directions), "back to idle");
        s.Own[0].LastAiOrder = 5;
        s.Own[1].LastAiOrder = 0;
        AssertEqual(1, watch.Observe(s, ids, directions), "0 -> 5 (group 11 went idle)");
        AssertEqual(1, directions[0], "right");
        s.Own[1].LastAiOrder = 5;
        s.Own[2].LastAiOrder = 0;
        AssertEqual(1, watch.Observe(s, ids, directions), "group 11 starts again");
        AssertEqual(11, ids[0], "group 11");
        s.Own[2].LastAiOrder = 4;
        AssertEqual(0, watch.Observe(s, ids, directions), "a regiment's lastaiorder is not a group move");
        s.Own[0].LastAiOrder = 3;
        AssertEqual(0, watch.Observe(s, ids, directions), "only 4 and 5 are flank moves");

        string tick = SourceText("Tactical", "Battle", "BattleCoreTick.cs");
        string row = Between(tick, "private static void WatchVanillaFlankMoves(", "// AIBattle.bunits is a private static field");
        AssertPinned(row, "\"TacticalVanillaFlankMove\"", "the row");
        AssertPinned(row, ".WithField(\"tallyFix\", tallyFix)", "carries the flag's value");
        AssertPinned(row, ".WithField(\"macroai\", macroAi)", "the side's macroai");
        AssertPinned(row, ".WithField(\"stance\", stance)", "and stance");
        AssertPinned(row, "Plugin.Instance.EnableTacticalObserver.Value", "under Enable Tactical Observer");
        AssertPinned(tick, "WatchVanillaFlankMoves(battle, alliance, snapshot);", "called each arbiter tick");
    }

    // The BC7 smoke checklist (items 2.26-2.29) names these lines and fields; the greps in the checklist find them.
    private static void SmokeLogLinesMatchTheChecklist()
    {
        string runtime = SourceText("Tactical", "Battle", "OrderExecutorRuntime.cs");
        AssertPinned(runtime, "OnceLog.Info(\"tactical-battle-core:refuse-first-fire\",", "2.26 key");
        AssertPinned(runtime, "\"[OrderExecutor] first refused flank: mode=\" + mode + \" flank=\" + side + \" regiments=\" + regiments + \" skipped=\" + skipped);", "2.26 line");
        AssertPinned(runtime, "NoteRefuseFirstFire(\"InPlace\", w.Refuse, rotated, skipped);", "2.26 in-place mode");
        AssertPinned(runtime, "NoteRefuseFirstFire(\"Wheel\", refuse,", "2.26 wheel mode");
        string producers = SourceText("Tactical", "Battle", "Producers", "PostureIntentProducer.cs");
        AssertPinned(producers, "\"TacticalFacingPulse\", TelemetrySeverity.Info,", "the facing pulse row");
        AssertPinned(producers, ".WithField(\"facingSource\", facingSource ?? PostureTargets.NoFacingSource)\n                        .WithField(\"writeResult\"", "2.27 facingSource on both rows");
        string plays = SourceText("Tactical", "Orchestrator", "Plays", "PlayExecutorRuntime.cs");
        AssertPinned(plays, "OnceLog.Info(\"tactical-battle-core:envelopment-first-fire\",", "2.28 key");
        AssertPinned(plays, "\"[Envelopment] first flank march: plan=\" + planId + \" rect=\" + kind + \" child=\" + childId);", "2.28 line");
        AssertPinned(plays, "abandoned.AbandonReason = \"plan-changed\";", "2.28 plan-changed");
        AssertPinned(plays, ".WithField(\"mirrored\", mirrored)", "2.28 mirrored");
        string pure = SourceText("Tactical", "Orchestrator", "Plays", "EnvelopmentManeuver.cs");
        foreach (string reason in new[] { "river-on-leg", "anchored-high-ground", "lost-strength", "timeout" })
            AssertTrue(pure.Contains("\"" + reason + "\""), "2.28 reason " + reason);
        string patch = SourceText("Patches", "FlankThreatTallyFixPatch.cs");
        AssertPinned(patch, "OnceLog.Info(\"tactical-flank-threat:first-correction\",", "2.29 key");
        AssertPinned(patch, "\"[FlankThreatTally] group flanks threatened re-tallied per subordinate: vanilla=\" + vanilla + \" corrected=\" + points);", "2.29 line");
        AssertPinned(patch, "OnceLog.Warning(\"tactical-flank-threat:failed\",", "2.29 failure key");
        string army = SourceText("Tactical", "Orchestrator", "ArmyOrchestrator.cs");
        AssertPinned(army, "\"TacticalReserveRelease\",", "2.29 reserve release row");
        AssertPinned(army, "released.HasGuardPoint ? \"crossing-threat\" : \"flank-threat\"", "2.29 reason (a crossing guard names its own)");
    }

    private static void FlankMoveWatchAllocatesNothingAfterWarmup()
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        for (int i = 0; i < 30; i++)
            s.AddOwn(new UnitRecord { InstanceId = 100 + i, UnitTyp = 16, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup });
        s.IndexCommandTree(null);
        var watch = new VanillaFlankMoveWatch();
        var ids = new int[8]; var directions = new int[8];
        int flip = 0, sink = 0;
        AssertNoAllocation(() =>
        {
            flip ^= 4;
            s.Own[3].LastAiOrder = flip;
            sink += watch.Observe(s, ids, directions);
        }, 100, "flank-move watch");
        GC.KeepAlive(sink);
    }
}
