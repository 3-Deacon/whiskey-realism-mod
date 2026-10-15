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
            ("arms weapon budget spent only by the player's alliance", BudgetSpentOnlyByPlayerAlliance),
            ("arms weapon budget purchase needs the prestige to pay", BudgetPurchaseNeedsPrestige),
            ("arms weapon budget wiring", BudgetWiring),
            ("arms orderability follows the order screen", OrderabilityFollowsOrderScreen),
            ("arms list member mask hides and restores", ListMemberMaskHidesAndRestores),
            ("arms purchase orderable wiring", PurchaseOrderableWiring),
            ("arms group weapon factor nan guard", GroupWeaponFactorNanGuard),
            ("arms realism lock stays on the setup screen", RealismLockSetupScreenOnly),
            ("arms need runtime reads each lane from its own fields", NeedRuntimeReadsLanes),
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

        // A look-alike test with another branch (an equality test, not `> 0`) is not a site.
        foreach (var other in new[] { OpCodes.Bne_Un_S, OpCodes.Beq, OpCodes.Bgt_S })
        {
            var lookAlike = Il(typeof(Autocalc), "FightUnit");
            int at = AutoresolveWeaponGateIl.Sites(lookAlike, field)[1];
            var copy2 = lookAlike.GetRange(at - 2, 4).Select(i => new CodeInstruction(i.opcode, i.operand)).ToList();
            copy2[3] = new CodeInstruction(other, copy2[3].operand);
            lookAlike.InsertRange(at + 2, copy2);
            AssertEqual(2, AutoresolveWeaponGateIl.Sites(lookAlike, field).Count, other.Name + " after weapon/0 is not a site");
            AutoresolveWeaponGateIl.Transpile(lookAlike, field, hook, out matches);
            AssertEqual(2, matches, other.Name + " look-alike leaves the two real sites");
        }

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
        string count = src.Substring(src.IndexOf("class AutoresolveStandardGunCountPatch", StringComparison.Ordinal));
        AssertTrue(HasLine(count, "CampaignScope.Register(\"autoresolve-standard-gun-count\", () => _counted = false);"), "count flag resets per campaign");
        AssertTrue(count.IndexOf("static AutoresolveStandardGunCountPatch()", StringComparison.Ordinal) >= 0, "registered from the owner's static constructor");
        AssertTrue(HasLine(MethodBody(count, "Postfix"), "if (_counted) return;") && HasLine(MethodBody(count, "Postfix"), "_counted = true;"), "counts once until the reset");
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
        AssertTrue(settle.Contains("if (settled.Downgraded)"), "a re-arm logs its own line");
        string snapshot = MethodBody(src, "Snapshot");
        AssertTrue(snapshot.Contains("if (source.alliance != target.alliance)"), "cross-alliance merge keeps vanilla");
        AssertTrue(snapshot.Contains("if (id >= alliance.weaponstock.Length) return null;"), "ids bound-checked");
        AssertTrue(MethodBody(src, "StockFixEnabled").Contains("Plugin.EnableCombineWeaponStockFix.Value"), "stock flag");
    }

    // ------------------------------------------------------------------ BUG-WL-016 / 017

    private static void BudgetSpentOnlyByPlayerAlliance()
    {
        AssertTrue(ArmsRules.OrderSpendsCareerBudget(0, 0), "own alliance (player or the AI for it)");
        AssertFalse(ArmsRules.OrderSpendsCareerBudget(1, 0), "enemy");
        AssertFalse(ArmsRules.OrderSpendsCareerBudget(2, 0), "Europe");
        AssertEqual(5f, ArmsRules.BudgetAfterOrder(true, 1, 0, 5f, 0f), "enemy order: budget kept");
        AssertEqual(2f, ArmsRules.BudgetAfterOrder(true, 0, 0, 5f, 2f), "own order: vanilla charge");
        AssertEqual(0f, ArmsRules.BudgetAfterOrder(false, 1, 0, 5f, 0f), "flag off: vanilla");
    }

    // dlcwl_config.dat: cost -500; vanilla charge prestige - 500, gate prestige >= -500.
    private static void BudgetPurchaseNeedsPrestige()
    {
        AssertTrue(ArmsRules.BudgetPurchaseUnaffordable(0f, -500f), "0 prestige cannot pay 500");
        AssertTrue(ArmsRules.BudgetPurchaseUnaffordable(-500f, -500f), "vanilla lets -500 buy");
        AssertTrue(ArmsRules.BudgetPurchaseUnaffordable(499f, -500f), "one short");
        AssertFalse(ArmsRules.BudgetPurchaseUnaffordable(500f, -500f), "exactly enough");
        AssertFalse(ArmsRules.BudgetPurchaseUnaffordable(-600f, -500f), "vanilla refuses already");
        AssertFalse(ArmsRules.BudgetPurchaseUnaffordable(-10f, 0f), "free purchase config: vanilla");
        AssertFalse(-499f >= float.MaxValue, "the swapped cost makes vanilla's own gate refuse");
    }

    private static void BudgetWiring()
    {
        string src = PatchSource("WlWeaponBudgetPatch.cs");
        AssertTrue(MethodBody(src, "Prefix").Contains("__state = DLC_WL.accumulatedweaponbudget;"), "budget snapshot");
        string fin = MethodBody(src, "Finalizer");
        AssertTrue(fin.Contains("ArmsRules.BudgetAfterOrder(ScopeEnabled(), alliance, GameVars.playeralliance, __state, vanilla)"), "restore decided by the pure rule, flag read live");
        AssertTrue(fin.Contains("DLC_WL.accumulatedweaponbudget = kept;"), "restore written");
        AssertTrue(src.Contains("[HarmonyPatch(typeof(WeaponList), nameof(WeaponList.PlaceWeaponOrder))]"), "order target");
        string gate = src.Substring(src.IndexOf("class PurchaseGate", StringComparison.Ordinal));
        string gatePrefix = MethodBody(gate, "Prefix");
        AssertTrue(gatePrefix.Contains("if (!GateEnabled()) return;"), "gate flag");
        AssertTrue(gatePrefix.Contains("ArmsRules.BudgetPurchaseUnaffordable(DLC_WL.prestige, GamePrefs.dlcwl_weaponbudgetprestigecost)") && gatePrefix.Contains("Cost.Apply(float.MaxValue);"), "unaffordable: vanilla's gate made to refuse");
        string gateFin = MethodBody(gate, "Finalizer");
        AssertTrue(gateFin.Contains("string failure = Cost.Restore();"), "gate restored unconditionally");
        AssertFalse(gateFin.Contains("if (") && gateFin.IndexOf("if (", StringComparison.Ordinal) < gateFin.IndexOf("Cost.Restore()", StringComparison.Ordinal), "restore before any condition");
        AssertTrue(MethodBody(src, "ScopeEnabled").Contains("Plugin.EnableWlWeaponBudgetScopeFix.Value"), "scope flag");
        AssertTrue(MethodBody(src, "GateEnabled").Contains("Plugin.EnableWlWeaponBudgetPrestigeGateFix.Value"), "gate flag");
    }

    // ------------------------------------------------------------------ BUG-AI-017

    // weapons.txt: pm 0 for 12 Williams, 37 Gatling, 38 Coffee Mill (researched by projects
    // 10/11, UpdateProjectEffects 213091-213092 and 213096); 1 Napoleon pm 3.2, imported by project 3.
    private static void OrderabilityFollowsOrderScreen()
    {
        AssertFalse(WhiskeyRealism.Strategic.WeaponOrderability.IsOrderable(true, false, 0f), "Gatling: researched, not produced");
        AssertTrue(WhiskeyRealism.Strategic.WeaponOrderability.IsResearchedButUnorderable(true, false, 0f), "the AI's extra candidate");
        AssertTrue(WhiskeyRealism.Strategic.WeaponOrderability.IsOrderable(true, false, 3.2f), "Napoleon produced");
        AssertTrue(WhiskeyRealism.Strategic.WeaponOrderability.IsOrderable(false, true, 3.2f), "Napoleon imported");
        AssertTrue(WhiskeyRealism.Strategic.WeaponOrderability.IsOrderable(true, true, 0f), "imported overrides pm 0 (GetAvailability 'Import only')");
        AssertFalse(WhiskeyRealism.Strategic.WeaponOrderability.IsOrderable(false, false, 3.2f), "not researched, not imported");
        AssertFalse(WhiskeyRealism.Strategic.WeaponOrderability.IsResearchedButUnorderable(false, false, 0f), "never a candidate in vanilla either");
        AssertFalse(WhiskeyRealism.Strategic.WeaponOrderability.IsResearchedButUnorderable(true, true, 0f), "imported: vanilla candidate stays");
    }

    private sealed class Model
    {
        public List<int> Tech = new List<int>();
    }

    private static void ListMemberMaskHidesAndRestores()
    {
        var mask = new ListMemberMask<Model>(m => m.Tech, (m, l) => m.Tech = l);
        var gatling = new Model { Tech = new List<int> { 0, 1 } };
        var napoleon = new Model { Tech = new List<int> { 0 } };
        var originalGatling = gatling.Tech;

        AssertFalse(mask.Hide(gatling, 1), "no pass active");
        AssertTrue(mask.Begin(), "pass starts");
        AssertFalse(mask.Begin(), "nested pass refused");
        AssertTrue(mask.Hide(gatling, 1), "CSA hidden from the Gatling");
        AssertFalse(mask.Hide(napoleon, 1), "nothing to hide");
        AssertFalse(gatling.Tech.Contains(1), "IsResearched(1) false during the call");
        AssertTrue(originalGatling.Contains(1), "original list untouched");
        AssertEqual(1, mask.Count, "one masked");
        AssertEqual(null, mask.Restore(), "clean restore");
        AssertTrue(ReferenceEquals(originalGatling, gatling.Tech), "original list back");
        AssertTrue(mask.Begin(), "pass over: a new one can start");
        AssertEqual(null, mask.Restore(), "empty pass restores cleanly");
        AssertEqual(null, mask.Restore(), "second restore is a no-op");

        mask.Begin();
        mask.Hide(gatling, 0);
        var replaced = new List<int> { 7 };
        gatling.Tech = replaced;   // vanilla replaced the list during the call
        mask.Restore();
        AssertTrue(ReferenceEquals(replaced, gatling.Tech), "a list that is no longer ours is left alone");

        var broken = new ListMemberMask<Model>(m => m.Tech, (m, l) => { if (l.Count > 1) throw new InvalidOperationException("boom"); m.Tech = l; });
        var both = new Model { Tech = new List<int> { 0, 1 } };
        broken.Begin();
        AssertTrue(broken.Hide(both, 1), "copy of one written");
        AssertEqual("boom", broken.Restore(), "restore failure reported");
        AssertTrue(broken.Begin(), "pass over after a failure");
    }

    private static void PurchaseOrderableWiring()
    {
        string src = PatchSource("AiWeaponPurchaseOrderablePatch.cs");
        string prefix = MethodBody(src, "Prefix");
        AssertTrue(prefix.Contains("if (!Enabled() || !Research.Begin()) return;"), "flag and single pass");
        AssertTrue(HasLine(prefix, "if (weapons[id] == null || !WeaponOrderabilityRuntime.IsResearchedButUnorderable(_alliance, id)) continue;"), "skips every model the alliance can order (polarity pinned on the whole line)");
        AssertTrue(HasLine(prefix, "Research.Hide(weapons[id], _alliance);"), "hides the ordering alliance from the rest");
        AssertFalse(prefix.Contains("return false"), "never skips vanilla");
        string fin = MethodBody(src, "Finalizer");
        AssertTrue(fin.Contains("string failure = Research.Restore();"), "restore");
        AssertTrue(fin.IndexOf("Research.Restore()", StringComparison.Ordinal) < (fin.IndexOf("if (", StringComparison.Ordinal) < 0 ? int.MaxValue : fin.IndexOf("if (", StringComparison.Ordinal)), "restore before any condition");
        AssertTrue(MethodBody(src, "Enabled").Contains("Plugin.EnableAiWeaponPurchaseOrderableFix.Value"), "flag");
        AssertTrue(src.Contains("weapon => weapon.techavailableforalliance") && src.Contains("weapon.techavailableforalliance = list"), "masks the research list vanilla's IsResearched reads");
    }

    // ------------------------------------------------------------------ BUG-AI-018 / #14

    private static void GroupWeaponFactorNanGuard()
    {
        AssertEqual(0f, ArmsRules.GroupWeaponFactor(0f / 0f), "0/0 -> 0");
        AssertEqual(0f, ArmsRules.GroupWeaponFactor(float.PositiveInfinity), "infinity -> 0");
        AssertEqual(0.249f, ArmsRules.GroupWeaponFactor(0.249f), "real value kept");
        string src = PatchSource("GroupWeaponStrengthGuardPatch.cs");
        string postfix = MethodBody(src, "Postfix");
        AssertTrue(postfix.Contains("float guarded = ArmsRules.GroupWeaponFactor(__result);") && postfix.Contains("if (guarded == __result || !Enabled()) return;") && postfix.Contains("__result = guarded;"), "postfix applies the rule behind the flag");
        AssertTrue(src.Contains("new[] { typeof(Regiment) }"), "the group overload only");
        AssertTrue(MethodBody(src, "Enabled").Contains("Plugin.EnableGroupWeaponStrengthGuard.Value"), "flag");
    }

    private static void RealismLockSetupScreenOnly()
    {
        string postfix = MethodBody(PatchSource("RealismCheckboxesLockPatch.cs"), "Postfix");
        int guard = postfix.IndexOf("if (SceneManagement.currentoperation > 0) return;", StringComparison.Ordinal);
        AssertTrue(guard > 0, "mirrors vanilla's setup-screen guard (193616)");
        AssertTrue(guard < postfix.IndexOf("SetValue(", StringComparison.Ordinal) && guard < postfix.IndexOf("_setAutomanageMethod", StringComparison.Ordinal), "before any write");
    }

    // CampaignNeedRuntime.ReadArms (not in the harness): rifles = other units, guns = artillery
    // units (alliance tick 81589-81605), and Fill's lane / pieces-per-unit argument order.
    private static void NeedRuntimeReadsLanes()
    {
        string src = File.ReadAllText(Path.Combine(TestFixtures.FindRepoPath("src/WhiskeyRealism"), "Strategic", "CampaignNeedRuntime.cs"));
        string read = MethodBody(src, "ReadArms");
        AssertTrue(HasLine(read, "s.Rifles = new ArmsLane { Units = state.otherunits, AverageStrength = state.averageweaponstrengthrifles };"), "rifle lane from other units");
        AssertTrue(HasLine(read, "s.Guns = new ArmsLane { Units = state.artilleryunits, AverageStrength = state.averageweaponstrengthguns };"), "gun lane from artillery units");
        AssertTrue(HasLine(read, "ArmsCoverage.Fill(models, s.Rifles, s.Guns, GamePrefs.usedstatsgunsdummy, riflesPerUnit);"), "Fill(rifles, guns, guns per unit, rifles per unit)");
        AssertTrue(HasLine(read, "IsArtillery = weapon.isari,"), "lane split on isari, as vanilla (16290)");
        AssertTrue(HasLine(read, "s.MinEquippableShare = GamePrefs.aiminnumberofequippableunits;") && HasLine(read, "s.ImportCutAtFullBlockade = GamePrefs.influenceofblockadesonweaponimports;"), "vanilla prefs");
    }

    // True when one line of the body, trimmed, is exactly `line`.
    private static bool HasLine(string body, string line)
    {
        foreach (var raw in body.Split('\n'))
            if (raw.Trim() == line) return true;
        return false;
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
