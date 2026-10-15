using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Strategic.BugFixes;
using static TestHarness;

// Armament bug fixes (docs/bug-fixes/vanilla-armament.md): pure rules and the IL rewrite
// run on the real refs/Assembly-CSharp.dll, plus source pins of the runtime wiring.
internal static class ArmsFixTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("arms autoresolve weapon floor", AutoresolveWeaponFloor),
            ("arms autoresolve il rewrites both weapon tests", AutoresolveIlRewritesBothWeaponTests),
            ("arms autoresolve il composes with the perk-id rewrite", AutoresolveIlComposesWithPerkIds),
            ("arms autoresolve il refuses a missing or duplicated site", AutoresolveIlRefusesMissOrDuplicate),
            ("arms autoresolve wiring", AutoresolveWiring),
        });
    }

    public static int FloorHook() => -1;
    public static int RemapHook(int id) => id;

    private static MethodInfo M(string name) => AccessTools.Method(typeof(ArmsFixTests), name);

    private static List<CodeInstruction> Il(Type type, string method)
    {
        var original = AccessTools.Method(type, method);
        AssertTrue(original != null, type.Name + "." + method + " exists");
        return PatchProcessor.ReadMethodBody(original).Select(kv => new CodeInstruction(kv.Key, kv.Value)).ToList();
    }

    private static string Dump(List<CodeInstruction> code) => string.Join("\n", code.Select(i => i.ToString()));

    // ------------------------------------------------------------------ BUG-BAT-002

    private static void AutoresolveWeaponFloor()
    {
        AssertEqual(-1, AutoresolveWeaponGateIl.WeaponFloor(true), "fix: weapon > -1, id 0 armed");
        AssertEqual(0, AutoresolveWeaponGateIl.WeaponFloor(false), "vanilla: weapon > 0");
        AssertTrue(0 > AutoresolveWeaponGateIl.WeaponFloor(true), "6-pounder (id 0) passes the fixed test");
        AssertFalse(-1 > AutoresolveWeaponGateIl.WeaponFloor(true), "no weapon (-1) still fails");
    }

    private static void AutoresolveIlRewritesBothWeaponTests()
    {
        var field = AutoresolveWeaponGateIl.ResolveWeaponField();
        AssertTrue(field != null, "Regiment.weapon resolves");
        var vanilla = Il(typeof(Autocalc), "FightUnit");
        AssertEqual(2, AutoresolveWeaponGateIl.Sites(vanilla, field).Count, "21085 and 21089");
        int loads = vanilla.Count(i => i.LoadsField(field));
        var result = AutoresolveWeaponGateIl.Transpile(vanilla, field, M(nameof(FloorHook)), out int matches);
        AssertEqual(2, matches, "two sites");
        AssertFalse(ReferenceEquals(result, vanilla), "rewritten copy");
        AssertEqual(vanilla.Count, result.Count, "constant replaced in place");
        AssertEqual(2, result.Count(i => i.Calls(M(nameof(FloorHook)))), "two hook calls");
        AssertEqual(0, AutoresolveWeaponGateIl.Sites(result, field).Count, "no vanilla > 0 test left");
        AssertEqual(loads, result.Count(i => i.LoadsField(field)), "every weapon load kept (firepower reads untouched)");
        for (int i = 0; i < result.Count; i++)
            if (result[i].Calls(M(nameof(FloorHook))))
            {
                AssertTrue(result[i - 1].LoadsField(field), "hook follows the weapon load");
                AssertTrue(result[i + 1].opcode == OpCodes.Ble || result[i + 1].opcode == OpCodes.Ble_S, "hook feeds the branch");
                AssertEqual(vanilla[i].labels.Count, result[i].labels.Count, "labels kept");
            }
        AssertEqual(Dump(Il(typeof(Autocalc), "FightUnit")), Dump(vanilla), "vanilla list not mutated");
    }

    // The game applies both FightUnit transpilers; the order Harmony picks must not matter.
    private static void AutoresolveIlComposesWithPerkIds()
    {
        var field = AutoresolveWeaponGateIl.ResolveWeaponField();
        var hook = M(nameof(FloorHook));
        var remap = M(nameof(RemapHook));

        var perkFirst = Il(typeof(Autocalc), "FightUnit");
        AssertTrue(PerkIdIlRewrites.RewriteFightUnit(perkFirst, remap, remap, remap), "perk rewrite first");
        var both = AutoresolveWeaponGateIl.Transpile(perkFirst, field, hook, out int matches);
        AssertEqual(2, matches, "weapon sites still found after the perk rewrite");

        var weaponFirst = AutoresolveWeaponGateIl.Transpile(Il(typeof(Autocalc), "FightUnit"), field, hook, out matches);
        AssertEqual(2, matches, "weapon rewrite first");
        AssertTrue(PerkIdIlRewrites.RewriteFightUnit(weaponFirst, remap, remap, remap), "perk sites still found after the weapon rewrite");

        AssertEqual(Dump(both), Dump(weaponFirst), "same body in either order");
        AssertEqual(2, both.Count(i => i.Calls(hook)), "two weapon hooks");
        AssertEqual(4, both.Count(i => i.Calls(remap)), "four perk remaps");
    }

    private static void AutoresolveIlRefusesMissOrDuplicate()
    {
        var field = AutoresolveWeaponGateIl.ResolveWeaponField();
        var hook = M(nameof(FloorHook));

        var missing = Il(typeof(Autocalc), "FightUnit");
        int first = AutoresolveWeaponGateIl.Sites(missing, field)[0];
        missing[first] = new CodeInstruction(OpCodes.Ldc_I4_1);
        string before = Dump(missing);
        var result = AutoresolveWeaponGateIl.Transpile(missing, field, hook, out int matches);
        AssertEqual(1, matches, "one site left");
        AssertTrue(ReferenceEquals(result, missing), "miss returns vanilla itself");
        AssertEqual(before, Dump(missing), "IL unchanged");

        var dup = Il(typeof(Autocalc), "FightUnit");
        int site = AutoresolveWeaponGateIl.Sites(dup, field)[0];
        var copy = dup.GetRange(site - 2, 4).Select(i => new CodeInstruction(i.opcode, i.operand)).ToList();
        dup.InsertRange(site + 2, copy);
        before = Dump(dup);
        result = AutoresolveWeaponGateIl.Transpile(dup, field, hook, out matches);
        AssertEqual(3, matches, "three sites");
        AssertTrue(ReferenceEquals(result, dup), "duplicate returns vanilla itself");
        AssertEqual(before, Dump(dup), "IL unchanged");

        AssertEqual(0, AutoresolveWeaponGateIl.Rewrite(Il(typeof(Autocalc), "FightUnit"), null, hook), "no field: no edit");
        AssertEqual(0, AutoresolveWeaponGateIl.Rewrite(Il(typeof(Autocalc), "FightUnit"), field, null), "no hook: no edit");
    }

    private static void AutoresolveWiring()
    {
        string src = PatchSource("AutoresolveStandardGunPatch.cs");
        string hook = MethodBody(src, "WeaponFloor");
        AssertTrue(hook.Contains("return AutoresolveWeaponGateIl.WeaponFloor(Enabled());"), "hook reads the flag live");
        string enabled = MethodBody(src, "Enabled");
        AssertTrue(enabled.Contains("Plugin.EnableAutoresolveStandardGunFix.Value"), "hook flag");
        string transpiler = MethodBody(src, "Transpiler");
        AssertTrue(transpiler.Contains("return result;") && transpiler.Contains("return vanilla;"), "transpiler emits the helper's choice, vanilla on failure");
        AssertTrue(src.Contains("[HarmonyPatch(typeof(Autocalc), \"FightUnit\")]"), "targets FightUnit");
    }

    // ------------------------------------------------------------------ helpers

    private static string PatchSource(string file)
    {
        return File.ReadAllText(Path.Combine(TestFixtures.FindRepoPath("src/WhiskeyRealism"), "Patches", file));
    }

    private static string MethodBody(string source, string name)
    {
        int at = source.IndexOf(" " + name + "(", StringComparison.Ordinal);
        if (at < 0) throw new Exception("method " + name + " not found");
        int open = source.IndexOf('{', at);
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
        }
        throw new Exception("unbalanced braces in " + name);
    }
}
