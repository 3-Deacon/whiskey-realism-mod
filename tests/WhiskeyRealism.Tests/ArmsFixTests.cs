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
            ("arms combine pieces count guns for batteries", CombinePiecesCountGuns),
            ("arms combine moves only the guns that change hands", CombineMovesOnlyGunsThatChangeHands),
            ("arms combine shortfall returns guns not men", CombineShortfallReturnsGuns),
            ("arms combine keeps the model when stock covers the gain", CombineKeepsModelWhenCovered),
            ("arms combine standard target is not re-armed for free", CombineStandardTargetNoLeak),
            ("arms combine same model never downgrades", CombineSameModelNeverDowngrades),
            ("arms combine infantry matches vanilla where vanilla is right", CombineInfantryMatchesVanilla),
            ("arms combine no merge or no weapon is a no-op", CombineNoMergeNoOp),
            ("arms combine wiring", CombineWiring),
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

    // ------------------------------------------------------------------ BUG-BAT-003

    private const int SixPounder = 0, Napoleon = 1, ThreeInch = 4, MixedMuskets = 42, Springfield = 14;

    private static CombineArming Arming(bool artillery, int source, int target, int standard,
        int sourcePieces, int targetPieces, params (int id, float stock)[] stock)
    {
        var a = new CombineArming
        {
            IsArtillery = artillery, SourceWeapon = source, TargetWeapon = target, StandardWeapon = standard,
            SourcePiecesBefore = sourcePieces, TargetPiecesBefore = targetPieces
        };
        foreach (int id in new[] { source, target, standard })
            if (id >= 0 && !a.StockBefore.ContainsKey(id)) a.StockBefore[id] = 0f;
        foreach (var (id, value) in stock) a.StockBefore[id] = value;
        return a;
    }

    private static void CombinePiecesCountGuns()
    {
        AssertEqual(6, CombineWeaponLedger.Pieces(true, 120, 10, 5, 6), "battery: statsguns (DeleteUnit 90631, tick 81586)");
        AssertEqual(135, CombineWeaponLedger.Pieces(false, 120, 10, 5, 0), "line unit: strength + wounded + sick");
    }

    // Source battery keeps 2 of its 6 guns; target gains 4. Vanilla: A += 6 (2 phantom), B -= 6.
    private static void CombineMovesOnlyGunsThatChangeHands()
    {
        var before = Arming(true, ThreeInch, Napoleon, SixPounder, 6, 4, (ThreeInch, 0f), (Napoleon, 10f));
        var r = CombineWeaponLedger.Settle(before, sourcePiecesAfter: 2, targetPiecesAfter: 8);
        AssertEqual(4f, r.Stock[ThreeInch], "source returns the 4 guns it gave up");
        AssertEqual(6f, r.Stock[Napoleon], "target draws the 4 guns it gained");
        AssertEqual(Napoleon, r.TargetWeapon, "model kept");
        AssertFalse(r.Downgraded, "no downgrade");
        r = CombineWeaponLedger.Settle(before, sourcePiecesAfter: 0, targetPiecesAfter: 10);
        AssertEqual(6f, r.Stock[ThreeInch], "deleted source returns all its guns");
        AssertEqual(4f, r.Stock[Napoleon], "target draws 6");
    }

    // Target model short: vanilla returns the target's MEN (93232) as guns and re-arms it with
    // the standard gun. The fix returns its guns.
    private static void CombineShortfallReturnsGuns()
    {
        var before = Arming(true, ThreeInch, Napoleon, SixPounder, 6, 4, (Napoleon, 2f), (SixPounder, 7f));
        var r = CombineWeaponLedger.Settle(before, 0, 10);
        AssertTrue(r.Downgraded, "short of Napoleons: standard gun, vanilla's rule");
        AssertEqual(SixPounder, r.TargetWeapon, "re-armed with the standard weapon");
        AssertEqual(6f, r.Stock[Napoleon], "2 + the 4 guns it held, not ~120 men");
        AssertEqual(6f, r.Stock[ThreeInch], "source guns returned");
        AssertEqual(7f, r.Stock[SixPounder], "standard guns free, as on recruitment (vanilla)");
    }

    // Vanilla demands the source's whole 6 guns; the target gains 4 and 5 are in stock.
    private static void CombineKeepsModelWhenCovered()
    {
        var before = Arming(true, ThreeInch, Napoleon, SixPounder, 6, 4, (Napoleon, 5f));
        var r = CombineWeaponLedger.Settle(before, 2, 8);
        AssertFalse(r.Downgraded, "stock covers the 4 gained guns");
        AssertEqual(Napoleon, r.TargetWeapon, "model kept");
        AssertEqual(1f, r.Stock[Napoleon], "4 drawn");
        r = CombineWeaponLedger.Settle(before, 1, 9);
        AssertFalse(r.Downgraded, "5 gained, 5 in stock is enough");
        r = CombineWeaponLedger.Settle(before, 0, 10);
        AssertTrue(r.Downgraded, "6 gained, 5 in stock: a real shortfall");
    }

    private static void CombineStandardTargetNoLeak()
    {
        var before = Arming(false, Springfield, MixedMuskets, MixedMuskets, 800, 1000, (MixedMuskets, 100f));
        var r = CombineWeaponLedger.Settle(before, 300, 1500);
        AssertFalse(r.Downgraded, "already on the standard weapon");
        AssertEqual(MixedMuskets, r.TargetWeapon, "same model");
        AssertEqual(100f, r.Stock[MixedMuskets], "vanilla added the 1000 muskets it already held");
        AssertEqual(500f, r.Stock[Springfield], "source rifles returned");

        var battery = Arming(true, ThreeInch, SixPounder, SixPounder, 6, 4, (SixPounder, 0f));
        r = CombineWeaponLedger.Settle(battery, 0, 10);
        AssertEqual(0f, r.Stock[SixPounder], "no phantom 6-pounders");
        AssertEqual(SixPounder, r.TargetWeapon, "kept");
    }

    private static void CombineSameModelNeverDowngrades()
    {
        var before = Arming(true, Napoleon, Napoleon, SixPounder, 3, 4, (Napoleon, 0f));
        var r = CombineWeaponLedger.Settle(before, 0, 8);
        AssertFalse(r.Downgraded, "the source's guns stay with the merged battery");
        AssertEqual(Napoleon, r.TargetWeapon, "kept");
        AssertEqual(0f, r.Stock[Napoleon], "rounding gain clamps at 0");
        r = CombineWeaponLedger.Settle(Arming(true, Napoleon, Napoleon, SixPounder, 6, 4, (Napoleon, 3f)), 2, 8);
        AssertEqual(3f, r.Stock[Napoleon], "4 out, 4 in");
    }

    // Line units: vanilla counts men correctly; the outcome is vanilla's.
    private static void CombineInfantryMatchesVanilla()
    {
        var keep = Arming(false, MixedMuskets, Springfield, MixedMuskets, 800, 1000, (Springfield, 600f));
        var r = CombineWeaponLedger.Settle(keep, 300, 1500);
        AssertEqual(100f, r.Stock[Springfield], "500 drawn (vanilla 93225)");
        AssertEqual(500f, r.Stock[MixedMuskets], "500 returned (vanilla 93209)");
        AssertEqual(Springfield, r.TargetWeapon, "kept");

        var shortStock = Arming(false, MixedMuskets, Springfield, MixedMuskets, 800, 1000, (Springfield, 100f));
        r = CombineWeaponLedger.Settle(shortStock, 300, 1500);
        AssertTrue(r.Downgraded, "short: vanilla's standard re-arm");
        AssertEqual(1100f, r.Stock[Springfield], "its 1000 rifles returned (vanilla 93232)");
        AssertEqual(MixedMuskets, r.TargetWeapon, "standard");
    }

    private static void CombineNoMergeNoOp()
    {
        var before = Arming(true, ThreeInch, Napoleon, SixPounder, 6, 4, (ThreeInch, 3f), (Napoleon, 0f));
        var r = CombineWeaponLedger.Settle(before, 6, 4);
        AssertEqual(3f, r.Stock[ThreeInch], "unchanged");
        AssertEqual(0f, r.Stock[Napoleon], "unchanged");
        AssertEqual(Napoleon, r.TargetWeapon, "unchanged");
        AssertFalse(r.Downgraded, "early return: nothing moved");

        r = CombineWeaponLedger.Settle(Arming(true, ThreeInch, -1, SixPounder, 6, 4, (ThreeInch, 0f)), 0, 10);
        AssertEqual(6f, r.Stock[ThreeInch], "unarmed target: source still returns");
        AssertEqual(-1, r.TargetWeapon, "unarmed target untouched (vanilla 93220)");
        r = CombineWeaponLedger.Settle(Arming(true, -1, Napoleon, SixPounder, 6, 4, (Napoleon, 10f)), 0, 10);
        AssertEqual(4f, r.Stock[Napoleon], "unarmed source: target still draws");
        AssertEqual(0, CombineWeaponLedger.Settle(null, 0, 0).Stock.Count, "null snapshot");
    }

    private static void CombineWiring()
    {
        string src = PatchSource("ArtilleryCombineGunTransferPatch.cs");
        string prefix = MethodBody(src, "Prefix");
        AssertTrue(prefix.Contains("if (StockFixEnabled()) __state = Snapshot(unitsource, unitto);"), "snapshot only with the stock flag on");
        string postfix = MethodBody(src, "Postfix");
        AssertTrue(postfix.Contains("if (__state != null) SettleStock(unitsource, unitto, __result, __state);"), "settles whenever a snapshot exists");
        AssertTrue(postfix.Contains("if (Enabled() && (UnityEngine.Object)unitto != null && unitto.unittyp == 2)"), "#34 gun sync keeps its own flag");
        AssertTrue(postfix.IndexOf("SyncGuns(unitto)", StringComparison.Ordinal) < postfix.IndexOf("SettleStock(", StringComparison.Ordinal), "stock settled after the gun sync");
        string settle = MethodBody(src, "SettleStock");
        AssertTrue(settle.Contains("int sourceAfter = sourceAlive ? Pieces(source, before.IsArtillery) : 0;"), "deleted source holds nothing");
        AssertTrue(settle.Contains("CombineWeaponLedger.Settle(before, sourceAfter, Pieces(target, before.IsArtillery))"), "pure ledger decides");
        AssertTrue(settle.Contains("alliance.weaponstock[cell.Key] = cell.Value;") && settle.Contains("target.weapon = settled.TargetWeapon;"), "writes stock and weapon");
        string snapshot = MethodBody(src, "Snapshot");
        AssertTrue(snapshot.Contains("if (source.alliance != target.alliance)"), "cross-alliance merge keeps vanilla");
        AssertTrue(snapshot.Contains("if (id >= alliance.weaponstock.Length) return null;"), "ids bound-checked");
        AssertTrue(MethodBody(src, "StockFixEnabled").Contains("Plugin.EnableCombineWeaponStockFix.Value"), "stock flag");
    }

    // ------------------------------------------------------------------ helpers

    private static string PatchSource(string file)
    {
        return File.ReadAllText(Path.Combine(TestFixtures.FindRepoPath("src/WhiskeyRealism"), "Patches", file));
    }

    // Body of the method declared as `static ... name(` (calls to it are skipped).
    private static string MethodBody(string source, string name)
    {
        int at = -1;
        for (int from = 0; ; from = at + 1)
        {
            at = source.IndexOf(" " + name + "(", from, StringComparison.Ordinal);
            if (at < 0) throw new Exception("method " + name + " not found");
            int lineStart = source.LastIndexOf('\n', at) + 1;
            if (source.Substring(lineStart, at - lineStart).Contains("static ")) break;
        }
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
