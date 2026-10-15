using System;
using System.Collections.Generic;
using System.IO;
using WhiskeyRealism.Patches;

using static TestHarness;

// W&L economy and battle-stat patches (plan 2026-10-15-wl-economy-bugs).
// The write/restore state lives in Patches/WlEffectSwaps.cs, which the harness
// compiles and runs here. The Harmony glue cannot run without the game, so its
// shape is pinned from source: a writing Prefix stores its state in __state
// before it writes, the Finalizer restores it unconditionally, each patch reads
// its flag and routes its decision and write through the tested helpers, and a
// first-fire message with formatted values is built only behind a first-log flag.
internal static class WlEconomyPatchPinTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("wl effect slot write restores only its own untouched write", EffectSlotWriteRestore),
            ("wl effect slot set hides and restores every written slot", EffectSlotSetRestore),
            ("wl ownership raise sets and clears the flag once", OwnershipRaiseRestore),
            ("wl economy patches store state before writing and restore unconditionally", PatchesPublishThenWriteThenRestore),
            ("wl economy patches read their flags and route through the tested helpers", PatchesReadFlagsAndHelpers),
            ("wl economy patches format first-fire messages only once", PatchesFormatMessagesOnce),
        });
    }

    private static void EffectSlotWriteRestore()
    {
        float[] running = { 1f, 0.5f, 1f };
        var write = EffectSlotWrite.Prepare(running, 1, 1f);
        AssertTrue(write.Active, "a slot that differs is recorded");
        AssertNear(0.5f, running[1], 1e-6f, "Prepare writes nothing");
        write.Commit();
        AssertNear(1f, running[1], 1e-6f, "Commit writes");
        write.Restore(running);
        AssertNear(0.5f, running[1], 1e-6f, "Restore puts the old value back");
        write.Restore(running);
        AssertNear(0.5f, running[1], 1e-6f, "a second Restore is harmless");

        var untouched = EffectSlotWrite.Prepare(running, 1, 1f);
        untouched.Commit();
        running[1] = 0.7f;
        untouched.Restore(running);
        AssertNear(0.7f, running[1], 1e-6f, "a value rewritten meanwhile is kept");

        var replaced = EffectSlotWrite.Prepare(running, 1, 1f);
        replaced.Commit();
        float[] rebuilt = { 1f, 1f, 1f };
        replaced.Restore(rebuilt);
        AssertNear(1f, rebuilt[1], 1e-6f, "a replaced array is left alone");

        AssertFalse(EffectSlotWrite.Prepare(running, 0, 1f).Active, "slot already holds the value");
        AssertFalse(EffectSlotWrite.Prepare(null, 0, 1f).Active, "no array");
        AssertFalse(EffectSlotWrite.Prepare(running, 9, 1f).Active, "short array");
        default(EffectSlotWrite).Restore(running);
    }

    private static void EffectSlotSetRestore()
    {
        var set = new EffectSlotSet(new[] { 0, 2, 5 });
        float[] running = { 1.25f, 1.1f, 1f, 1f, 1f, 0.9f };
        set.Prepare(running, 1f);
        AssertTrue(set.Active, "slots differ");
        AssertNear(1.25f, running[0], 1e-6f, "Prepare writes nothing");
        set.Commit();
        AssertNear(1f, running[0], 1e-6f, "first slot hidden");
        AssertNear(1f, running[5], 1e-6f, "last slot hidden");
        AssertNear(1.1f, running[1], 1e-6f, "other slots untouched");
        running[5] = 0.8f;
        set.Restore(running);
        AssertNear(1.25f, running[0], 1e-6f, "restored");
        AssertNear(1f, running[2], 1e-6f, "an unwritten slot stays");
        AssertNear(0.8f, running[5], 1e-6f, "a rewritten slot is kept");
        AssertFalse(set.Active, "inactive after restore");
    }

    private sealed class Unit
    {
        public bool Owned;
    }

    private static void OwnershipRaiseRestore()
    {
        Action<Unit, bool> set = (u, owned) => u.Owned = owned;
        var unit = new Unit();
        var raise = OwnershipRaise<Unit>.Prepare(unit);
        AssertFalse(unit.Owned, "Prepare writes nothing");
        raise.Commit(set);
        AssertTrue(unit.Owned, "Commit raises");
        raise.Restore(set);
        AssertFalse(unit.Owned, "Restore clears");
        unit.Owned = true;
        raise.Restore(set);
        AssertTrue(unit.Owned, "a second Restore does nothing");
        default(OwnershipRaise<Unit>).Restore(set);
    }

    private static readonly string[] L3FinalizerGates =
    {
        "if (!__state.Group.Active && !__state.Attrition.Active && !DLC_WL.dlc_scenarioactive) return;",
        "if (!__state.Active && !DLC_WL.dlc_scenarioactive) return;",
    };

    // file -> (state store, write) pairs in the Prefix, and calls the Finalizer must make.
    private static readonly (string File, (string Store, string Write)[] Pairs, string[] Restores)[] Writers =
    {
        ("WlUnitStatsScopePatch.cs",
            new[]
            {
                ("__state.Group = OwnershipRaise<Regiment>.Prepare(groupunit);", "__state.Group.Commit(SetOwned);"),
                ("__state.Attrition = EffectSlotWrite.Prepare(", "__state.Attrition.Commit();"),
            },
            new[] { "__state.Group.Restore(SetOwned);", "__state.Attrition.Restore(WlEffectArrays.Running());" }),
        ("WlMoraleScopePatch.cs",
            new[] { ("__state = OwnershipRaise<Regiment>.Prepare(parentcampaignunit);", "__state.Commit(SetOwned);") },
            new[] { "__state.Restore(SetOwned);" }),
        ("WlSupplyStaffScopePatch.cs",
            new[]
            {
                ("__state.Group = OwnershipRaise<Regiment>.Prepare(__instance);", "__state.Group.Commit(SetOwned);"),
                ("__state.Efficiency = EffectSlotWrite.Prepare(", "__state.Efficiency.Commit();"),
            },
            new[] { "__state.Group.Restore(SetOwned);", "__state.Efficiency.Restore(WlEffectArrays.Running());" }),
        ("WlStaffPanelScopePatch.cs",
            new[] { ("__state = Slots;", "__state.Commit();") },
            new[] { "__state?.Restore(WlEffectArrays.Running());" }),
    };

    private static void PatchesPublishThenWriteThenRestore()
    {
        foreach (var w in Writers)
        {
            string src = Patch(w.File);
            string prefix = Section(src, "[HarmonyPrefix]", w.File);
            string finalizer = Section(src, "[HarmonyFinalizer]", w.File);
            foreach (var (store, write) in w.Pairs)
            {
                int s = prefix.IndexOf(store, StringComparison.Ordinal);
                int c = prefix.IndexOf(write, StringComparison.Ordinal);
                AssertTrue(s >= 0, w.File + ": Prefix stores " + store);
                AssertTrue(c > s, w.File + ": Prefix writes after storing: " + write);
                AssertTrue(prefix.IndexOf(write, c + 1, StringComparison.Ordinal) < 0, w.File + ": one write per store: " + write);
            }
            foreach (string restore in w.Restores)
                AssertTrue(finalizer.Contains(restore), w.File + ": Finalizer restores " + restore);
            AssertFalse(finalizer.Contains("Plugin."), w.File + ": Finalizer restores whatever the flags say");
            // Perf L3: the only early return allowed is "nothing stored and no W&L career".
            string restoring = finalizer;
            foreach (string gate in L3FinalizerGates)
                restoring = restoring.Replace(gate, string.Empty);
            AssertFalse(restoring.Contains("return"), w.File + ": Finalizer has no early return");
        }
        foreach (string file in new[] { "WlUnitStatsScopePatch.cs", "WlMoraleScopePatch.cs", "WlSupplyStaffScopePatch.cs" })
            AssertTrue(Patch(file).Contains("SetOwned = (unit, owned) => unit.dlcw_isundercommander = owned;"), file + ": the raise writes the ownership flag");
        foreach (string file in PatchFiles())
        {
            string src = Patch(file);
            if (src.Contains(".Commit(") && src.Contains("[HarmonyPrefix]"))
                AssertTrue(src.Contains("[HarmonyFinalizer]"), file + ": a writing Prefix has a Finalizer");
        }
    }

    // file -> source the patch must contain (flags it reads, helpers it routes through).
    private static readonly (string File, string[] Needles)[] Wiring =
    {
        ("WlUnitStatsScopePatch.cs", new[]
        {
            "Plugin.EnableWlStaffScopeFix", "Plugin.EnableWlAttritionScopeFix",
            "WlEconomyFixes.RaiseGroupForPlayerUnit(true, groupOwned, unitOwned, WlPlayerMapGroup.Is(groupunit))",
            "WlEconomyFixes.NeutralizeAttritionBonus(true, groupOwned, unitOwned)",
        }),
        ("WlMoraleScopePatch.cs", new[]
        {
            "Plugin.EnableWlStaffScopeFix",
            "WlEconomyFixes.RaiseGroupForPlayerUnit(true, parentcampaignunit.dlcw_isundercommander,",
            "__instance.dlcw_isundercommander, WlPlayerMapGroup.Is(parentcampaignunit))",
        }),
        ("WlSupplyStaffScopePatch.cs", new[]
        {
            "Plugin.EnableWlStaffScopeFix", "Plugin.EnableWlSupplyEfficiencySignFix",
            "WlEconomyFixes.TreatMapGroupAsPlayerOwned(true, owned, WlPlayerMapGroup.Is(__instance))",
            "WlEconomyFixes.SupplyEfficiencyForCall(owned, __state.Group.Active, signFix, effect, camp)",
            "WlEconomyFixes.CampStationInspectLogistics",
        }),
        ("WlStaffPanelScopePatch.cs", new[]
        {
            "Plugin.EnableWlStaffPanelScopeFix",
            "WlEconomyFixes.HidePlayerStaffEffectsOnPanel(true, reg.dlcw_isundercommander, WlPlayerMapGroup.Is(reg))",
            "new EffectSlotSet(WlEconomyFixes.PanelStaffEffects)",
        }),
        ("WlActionOnceEffectsPatch.cs", new[]
        {
            "Plugin.EnableWlActionOnceEffectsFix", "Plugin.EnableWlSupplyEfficiencySignFix",
            "WlEconomyFixes.FoldEffects(WlEffectArrays.Running(), WlEffectArrays.Once(), SignFixOn())",
        }),
        ("WlBuySuppliesSubordinateTypePatch.cs", new[]
        {
            "Plugin.EnableWlBuySuppliesFix",
            "WlEconomyFixes.NeedsSubordinateTypeCorrection(command.unittyp)",
            "WlEconomyFixes.IsSupplyRecipient(", "WlEconomyFixes.AverageShortage(weighted, total)", "WlEconomyFixes.FillRatio(amount, totalShortage)",
        }),
        ("BattleEntrenchmentAverageFixPatch.cs", new[]
        {
            "Plugin.EnableBattleEntrenchmentAverageFix",
            "BattleStatFixes.CountsForEnemyAverage(", "float corrected = BattleStatFixes.SideAverage(sum, count);",
            "__instance.enemyentrenchmentlevel = corrected;",
        }),
        ("BattleHqConditionFixPatch.cs", new[]
        {
            "Plugin.EnableBattleHqConditionFix",
            "float horses = BattleStatFixes.HqCondition(", "float guns = BattleStatFixes.HqCondition(",
            "hq.conditionhorses = horses;", "hq.conditionguns = guns;",
        }),
        ("TransferOfUnitsPatch.cs", new[]
        {
            "WlEconomyFixes.TransferAddedCount(state.BeforeTransferCount, state.StaleBeforeCount, after)",
            "WlEconomyFixes.ClassifyTransfer(transit, career)",
            "WlEconomyFixes.CountsAsStaleTransfer(!ReferenceEquals(unit, null),",
        }),
    };

    private static void PatchesReadFlagsAndHelpers()
    {
        foreach (var (file, needles) in Wiring)
        {
            string src = Patch(file);
            foreach (string needle in needles)
                AssertTrue(src.Contains(needle), file + " contains " + needle);
        }
    }

    // A first-fire message that formats values allocates on every call unless a
    // first-log flag is tested before it is built.
    private static void PatchesFormatMessagesOnce()
    {
        int formatted = 0;
        foreach (string file in PatchFiles())
        {
            string src = Patch(file);
            int at = 0;
            while ((at = src.IndexOf("OnceLog.Info(", at, StringComparison.Ordinal)) >= 0)
            {
                int end = src.IndexOf(");", at, StringComparison.Ordinal);
                string call = src.Substring(at, end - at);
                at = end;
                if (!call.Contains("\" +") && !call.Contains("+ \"")) continue;
                formatted++;
                string before = src.Substring(Math.Max(0, at - call.Length - 160), 160);
                AssertTrue(System.Text.RegularExpressions.Regex.IsMatch(before, @"if \([^\n]*!_\w*[Ll]ogged\)|if \(_logged\) return;"), file + ": formatted message behind a first-log flag: " + call.Substring(0, Math.Min(60, call.Length)));
            }
        }
        AssertTrue(formatted >= 7, "the formatted first-fire messages were found (" + formatted + ")");
    }

    private static IEnumerable<string> PatchFiles()
    {
        yield return "WlUnitStatsScopePatch.cs";
        yield return "WlMoraleScopePatch.cs";
        yield return "WlSupplyStaffScopePatch.cs";
        yield return "WlStaffPanelScopePatch.cs";
        yield return "WlActionOnceEffectsPatch.cs";
        yield return "WlBuySuppliesSubordinateTypePatch.cs";
        yield return "BattleEntrenchmentAverageFixPatch.cs";
        yield return "BattleHqConditionFixPatch.cs";
        yield return "TransferOfUnitsPatch.cs";
    }

    private static string Patch(string file) =>
        File.ReadAllText(Path.Combine(TestFixtures.FindRepoPath("src/WhiskeyRealism/Patches"), file));

    // Text from the attribute to the next Harmony attribute or the first private helper after it.
    private static string Section(string src, string attribute, string file)
    {
        int start = src.IndexOf(attribute, StringComparison.Ordinal);
        AssertTrue(start >= 0, file + " has " + attribute);
        int end = src.Length;
        int next = src.IndexOf("[Harmony", start + attribute.Length, StringComparison.Ordinal);
        if (next >= 0) end = Math.Min(end, next);
        int helper = src.IndexOf("        private static", start, StringComparison.Ordinal);
        if (helper >= 0) end = Math.Min(end, helper);
        return src.Substring(start, end - start);
    }
}
