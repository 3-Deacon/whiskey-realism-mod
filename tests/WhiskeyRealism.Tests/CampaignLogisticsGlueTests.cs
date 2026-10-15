using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Strategic.BugFixes;
using static TestHarness;

// Campaign logistics patch glue (verify r1 F2): every save, write and restore the patches
// make runs here on stand-ins (Patches/LogisticsSwaps.cs), and the runtime wiring that
// cannot move is pinned in the patch sources.
internal static class CampaignLogisticsGlueTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("logistics glue value swap writes and restores", ValueSwapWritesAndRestores),
            ("logistics glue value swap restores only its own write", ValueSwapRestoresOnlyItsOwnWrite),
            ("logistics glue value swap refuses a nested apply", ValueSwapRefusesNestedApply),
            ("logistics glue fog flip clears and restores fog and sound", FogFlipClearsAndRestores),
            ("logistics glue fog flip keeps a fog value vanilla recomputed", FogFlipKeepsRecomputedFog),
            ("logistics glue fog flip leaves a field someone else replaced", FogFlipLeavesReplacedField),
            ("logistics glue model sync hides shows and skips", ModelSyncHidesShowsAndSkips),
            ("logistics glue field mask masks and restores", FieldMaskMasksAndRestores),
            ("logistics glue list contents replaced only when different", ListContentsReplacedOnlyWhenDifferent),
            ("logistics glue shared sound array per engine", SharedSoundArrayPerEngine),
            ("logistics glue embarkation recompute and sound sharing rules", EmbarkationRecomputeAndSoundRules),
            ("logistics glue raid rescale keeps the w&l player's raid groups", RaidRescaleKeepsPlayerRaidGroups),
            ("logistics glue random transpile returns vanilla on a mismatch", RandomTranspileReturnsVanillaOnMismatch),
            ("logistics glue trim transpile returns vanilla on a mismatch", TrimTranspileReturnsVanillaOnMismatch),
            ("logistics glue random sites carry static log keys", RandomSitesCarryStaticLogKeys),
            ("logistics glue finalizers restore before anything else", FinalizersRestoreFirst),
            ("logistics glue prefixes write only through the swaps", PrefixesWriteOnlyThroughSwaps),
            ("logistics glue patches pass the decisions vanilla state", PatchesPassDecisionsVanillaState),
        });
    }

    private sealed class Unit
    {
        internal bool Fog;
        internal int[] Sounds;
    }

    private sealed class Model
    {
        internal bool Gone;
        internal bool Active;
    }

    private sealed class Depot
    {
        internal int StartedBy;
        internal float Timer;
        internal bool Gone;
    }

    private static void ValueSwapWritesAndRestores()
    {
        float pref = 0.75f;
        var swap = new ValueSwap<float>(() => pref, v => pref = v);
        AssertTrue(swap.Apply(0.375f), "applied");
        AssertTrue(swap.Active, "active");
        AssertNear(0.375f, pref, 0.0001f, "written for the call");
        AssertEqual(null, swap.Restore(), "clean restore");
        AssertNear(0.75f, pref, 0.0001f, "restored");
        AssertFalse(swap.Active, "inactive");
        AssertEqual(null, swap.Restore(), "second restore is a no-op");
        AssertNear(0.75f, pref, 0.0001f, "still restored");
    }

    private static void ValueSwapRestoresOnlyItsOwnWrite()
    {
        float pref = 0.2f;
        var swap = new ValueSwap<float>(() => pref, v => pref = v);
        swap.Apply(0.05f);
        pref = 0.3f;   // someone else wrote during the call
        swap.Restore();
        AssertNear(0.3f, pref, 0.0001f, "a foreign write survives");
        float throwing = 1f;
        var broken = new ValueSwap<float>(() => throw new InvalidOperationException("boom"), v => throwing = v);
        AssertThrows(() => broken.Apply(2f), "apply surfaces a getter failure to the patch's catch");
    }

    private static void ValueSwapRefusesNestedApply()
    {
        float pref = 1f;
        var swap = new ValueSwap<float>(() => pref, v => pref = v);
        swap.Apply(2f);
        AssertFalse(swap.Apply(3f), "nested apply refused");
        AssertNear(2f, pref, 0.0001f, "first write kept");
        swap.Restore();
        AssertNear(1f, pref, 0.0001f, "original value back");
    }

    private static UnitFogFlip<Unit, int[]> SoundFlip(int[] muted)
    {
        return new UnitFogFlip<Unit, int[]>(u => u.Fog, (u, f) => u.Fog = f, u => u.Sounds, (u, s) => u.Sounds = s, muted);
    }

    private static void FogFlipClearsAndRestores()
    {
        var muted = new int[0];
        var sounds = new[] { 4, 7 };
        var unit = new Unit { Fog = true, Sounds = sounds };
        var flip = SoundFlip(muted);
        AssertTrue(flip.Apply(unit), "applied");
        AssertFalse(unit.Fog, "fog cleared for the call");
        AssertTrue(ReferenceEquals(muted, unit.Sounds), "bugle muted");
        AssertFalse(flip.Apply(new Unit { Fog = true }), "nested apply refused");
        AssertEqual(null, flip.Restore(vanillaRecomputedFog: false), "clean restore");
        AssertTrue(unit.Fog, "fog back");
        AssertTrue(ReferenceEquals(sounds, unit.Sounds), "sounds back");
        AssertFalse(flip.Active, "inactive");
        AssertEqual(null, flip.Restore(false), "idle restore is a no-op");
        AssertFalse(flip.Apply(null), "null unit refused");
    }

    private static void FogFlipKeepsRecomputedFog()
    {
        var unit = new Unit { Fog = true };
        var flip = new UnitFogFlip<Unit, object>(u => u.Fog, (u, f) => u.Fog = f);
        flip.Apply(unit);
        flip.Restore(vanillaRecomputedFog: true);   // CheckRegimentVisibility found it visible
        AssertFalse(unit.Fog, "vanilla's fresh visibility stands");

        var other = new Unit { Fog = true };
        flip.Apply(other);
        other.Fog = true;   // recomputed as hidden: true either way
        flip.Restore(vanillaRecomputedFog: false);
        AssertTrue(other.Fog, "hidden stays hidden");
    }

    private static void FogFlipLeavesReplacedField()
    {
        var muted = new int[0];
        var replaced = new[] { 9 };
        var unit = new Unit { Fog = true, Sounds = new[] { 1 } };
        var flip = SoundFlip(muted);
        flip.Apply(unit);
        unit.Sounds = replaced;   // vanilla rebuilt the array during the call
        flip.Restore(false);
        AssertTrue(ReferenceEquals(replaced, unit.Sounds), "a field vanilla replaced is not overwritten");
    }

    private static int Sync(Model m, bool active)
    {
        return ModelActiveSync.Apply(m, x => x == null || x.Gone, x => x.Active, (x, on) => x.Active = on, active);
    }

    private static void ModelSyncHidesShowsAndSkips()
    {
        var model = new Model { Active = true };
        AssertEqual(-1, Sync(model, false), "hidden");
        AssertFalse(model.Active, "inactive");
        AssertEqual(0, Sync(model, false), "already hidden");
        AssertEqual(1, Sync(model, true), "shown");
        AssertTrue(model.Active, "active");
        AssertEqual(0, Sync(null, false), "no model");
        AssertEqual(0, Sync(new Model { Gone = true, Active = true }, false), "destroyed model left alone");
    }

    private static void FieldMaskMasksAndRestores()
    {
        var finished = new Depot { StartedBy = 1, Timer = 0f };
        var building = new Depot { StartedBy = 1, Timer = 0.5f };
        var enemy = new Depot { StartedBy = 0, Timer = 0f };
        var gone = new Depot { StartedBy = 1, Timer = 0f };
        var list = new List<Depot> { finished, building, enemy, null, gone };
        var mask = new FieldMask<Depot, int>(d => d.StartedBy, (d, v) => d.StartedBy = v, -1);
        int masked = mask.Mask(list, d => SupplyDepotAiRules.ShouldMaskFinishedOwnDepot(d.StartedBy, 1, d.Timer));
        AssertEqual(2, masked, "finished own-started depots");
        AssertEqual(-1, finished.StartedBy, "masked");
        AssertEqual(1, building.StartedBy, "under construction untouched");
        AssertEqual(0, enemy.StartedBy, "enemy untouched");
        AssertEqual(0, mask.Mask(list, d => true), "a second mask while active is refused");
        gone.Gone = true;
        AssertEqual(null, mask.Restore(d => d.Gone), "clean restore");
        AssertEqual(1, finished.StartedBy, "restored");
        AssertEqual(-1, gone.StartedBy, "destroyed depot skipped");
        AssertFalse(mask.Active, "inactive");

        var again = new Depot { StartedBy = 1 };
        var m2 = new FieldMask<Depot, int>(d => d.StartedBy, (d, v) => d.StartedBy = v, -1);
        m2.Mask(new List<Depot> { again }, d => true);
        again.StartedBy = 0;   // changed hands during the call
        m2.Restore(d => false);
        AssertEqual(0, again.StartedBy, "a value changed during the call is kept");
    }

    private static void ListContentsReplacedOnlyWhenDifferent()
    {
        var a = new object();
        var b = new object();
        var c = new object();
        var target = new List<object> { a, b };
        AssertFalse(ListContents.ReplaceIfDifferent(target, new List<object> { b, a }), "same set, any order");
        AssertTrue(ListContents.ReplaceIfDifferent(target, new List<object> { c }), "different set");
        AssertEqual(1, target.Count, "replaced");
        AssertTrue(ReferenceEquals(c, target[0]), "new member");
        AssertTrue(ListContents.ReplaceIfDifferent(target, new List<object>()), "emptied");
        AssertFalse(ListContents.ReplaceIfDifferent(null, new List<object>()), "null target");
    }

    private static void SharedSoundArrayPerEngine()
    {
        var shared = new SharedArrayIndex();
        var engine = new object();
        AssertFalse(shared.TryGet(engine, out _), "nothing remembered yet");
        shared.Remember(engine, 42);
        AssertTrue(shared.TryGet(engine, out int index) && index == 42, "reused");
        AssertFalse(shared.TryGet(new object(), out _), "a new SoundEngine (new scene) starts over");
        shared.Remember(engine, -1);
        AssertTrue(shared.TryGet(engine, out index) && index == 42, "an invalid index is not remembered");
    }

    private static void EmbarkationRecomputeAndSoundRules()
    {
        AssertTrue(FoggedModelRule.VanillaRecomputedFog(true, false), "model removed: CheckRegimentVisibility re-ran");
        AssertFalse(FoggedModelRule.VanillaRecomputedFog(false, true), "model created");
        AssertFalse(FoggedModelRule.VanillaRecomputedFog(true, true), "model kept");
        AssertFalse(FoggedModelRule.VanillaRecomputedFog(false, false), "no model");
        AssertTrue(FoggedModelRule.ShouldShareFiringSounds(true, 25, 25), "ship firing inside the fix");
        AssertFalse(FoggedModelRule.ShouldShareFiringSounds(false, 25, 25), "outside the fix");
        AssertFalse(FoggedModelRule.ShouldShareFiringSounds(true, 7, 25), "other purpose");
    }

    private static void RaidRescaleKeepsPlayerRaidGroups()
    {
        AssertTrue(RaidSupplyThreshold.ShouldRescale(true, true, false, false), "AI raid group");
        AssertTrue(RaidSupplyThreshold.ShouldRescale(true, true, true, false), "W&L, not the player's");
        AssertTrue(RaidSupplyThreshold.ShouldRescale(true, true, false, true), "stale W&L flag outside a career");
        AssertFalse(RaidSupplyThreshold.ShouldRescale(true, true, true, true), "W&L player's raid group keeps vanilla");
        AssertFalse(RaidSupplyThreshold.ShouldRescale(false, true, false, false), "flag off");
        AssertFalse(RaidSupplyThreshold.ShouldRescale(true, false, false, false), "no raid group");
    }

    public static int OffsetHook()
    {
        return 1;
    }

    public static float TrimHook(float w)
    {
        return w;
    }

    private static void RandomTranspileReturnsVanillaOnMismatch()
    {
        var hook = AccessTools.Method(typeof(CampaignLogisticsGlueTests), nameof(OffsetHook));
        var range = RandomPickRangeIl.ResolveRandomRangeInt();
        var grab = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(AICampaign), "GrabExistingGroup"));
        var ok = RandomPickRangeIl.Transpile(grab, "GrabExistingGroup", range, hook, out var site, out int matches);
        AssertTrue(site.HasValue && matches == 2, "known site matched");
        AssertFalse(ReferenceEquals(ok, grab), "rewritten copy");

        var raid = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(AICampaign), "CheckCreationOfRaidForces"));
        var wrong = RandomPickRangeIl.Transpile(raid, "GrabExistingGroup", range, hook, out site, out matches);
        AssertTrue(site.HasValue && matches == 1, "count differs from the site's expected 2");
        AssertTrue(ReferenceEquals(wrong, raid), "mismatch returns the vanilla list itself");

        var unknown = RandomPickRangeIl.Transpile(raid, "NotASite", range, hook, out site, out matches);
        AssertFalse(site.HasValue, "unknown method");
        AssertTrue(ReferenceEquals(unknown, raid), "unknown method returns vanilla");
    }

    private static void TrimTranspileReturnsVanillaOnMismatch()
    {
        var hook = AccessTools.Method(typeof(CampaignLogisticsGlueTests), nameof(TrimHook));
        var factor = OffensiveWeaponTrimIl.ResolveWeaponStrengthFactor();
        var influence = OffensiveWeaponTrimIl.ResolveInfluenceField();
        var offensive = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(AICampaign), "CheckOffensiveMovements"));
        var ok = OffensiveWeaponTrimIl.Transpile(offensive, factor, influence, hook, out int matches);
        AssertEqual(1, matches, "one site");
        AssertFalse(ReferenceEquals(ok, offensive), "rewritten copy");
        AssertEqual(offensive.Count + 1, ok.Count, "hook inserted in the copy only");
        var zone = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(AICampaign), "CalculateZoneRecruitingProbs"));
        AssertTrue(ReferenceEquals(zone, OffensiveWeaponTrimIl.Transpile(zone, factor, influence, hook, out matches)), "no site: vanilla list");
    }

    private static void RandomSitesCarryStaticLogKeys()
    {
        var keys = new HashSet<string>();
        foreach (var site in RandomPickRangeIl.Sites)
        {
            AssertTrue(site.AppliedKey.StartsWith("random-candidate-pick:applied:", StringComparison.Ordinal), site.Method + " applied key");
            AssertTrue(site.PatternKey.StartsWith("random-candidate-pick:pattern:", StringComparison.Ordinal), site.Method + " pattern key");
            AssertTrue(keys.Add(site.AppliedKey) && keys.Add(site.PatternKey), site.Method + " keys unique");
        }
        string source = PatchSource("RandomCandidatePickPatch.cs");
        AssertFalse(source.Contains("OnceLog.Warning(\"random-candidate-pick:pattern:\" +"), "no key built from the method name");
        AssertFalse(source.Contains("OnceLog.Info(\"random-candidate-pick:applied:\" +"), "no key built from the method name");
    }

    // Each Finalizer's first statement after the perf scope and first-fire mark is the
    // restore, outside any condition, inside a try.
    private static void FinalizersRestoreFirst()
    {
        AssertRestoreFirst("FoggedCampPatch.cs", "Finalizer", "Flip.Restore(vanillaRecomputedFog: false)");
        AssertRestoreFirst("FoggedEmbarkationPatch.cs", "Finalizer", "Flip.Restore(FoggedModelRule.VanillaRecomputedFog(__state, hasModel))");
        AssertRestoreFirst("RaidSupplyThresholdPatch.cs", "Finalizer", "Threshold.Restore()");
        AssertRestoreFirst("WinterOffensiveModifierPatch.cs", "Finalizer", "DefensivePref.Restore()");
        AssertRestoreFirst("SupplyDepotAiFixPatch.cs", "Finalizer", "FinishedOwnDepots.Restore(");
    }

    private static void AssertRestoreFirst(string file, string method, string restoreCall)
    {
        string body = MethodBody(PatchSource(file), method);
        int restore = body.IndexOf(restoreCall, StringComparison.Ordinal);
        AssertTrue(restore >= 0, file + " " + method + " calls " + restoreCall);
        var tryMatch = System.Text.RegularExpressions.Regex.Match(body, @"\btry\b");
        int tryAt = tryMatch.Success ? tryMatch.Index : -1;
        AssertTrue(tryAt >= 0 && tryAt < restore, file + " restore inside the try");
        string before = body.Substring(0, restore);
        AssertFalse(before.Contains("return"), file + " nothing returns before the restore");
        AssertFalse(before.Contains("if ("), file + " the restore is unconditional");
    }

    private static void PrefixesWriteOnlyThroughSwaps()
    {
        AssertPrefixApplies("FoggedCampPatch.cs", "Flip.Apply(__instance)");
        AssertPrefixApplies("FoggedEmbarkationPatch.cs", "Flip.Apply(__instance)");
        AssertPrefixApplies("RaidSupplyThresholdPatch.cs", "Threshold.Apply(");
        AssertPrefixApplies("WinterOffensiveModifierPatch.cs", "DefensivePref.Apply(");
        AssertPrefixApplies("SupplyDepotAiFixPatch.cs", "FinishedOwnDepots.Mask(");
    }

    private static void AssertPrefixApplies(string file, string applyCall)
    {
        string body = MethodBody(PatchSource(file), "Prefix");
        AssertTrue(body.Contains(applyCall), file + " Prefix calls " + applyCall);
        foreach (string write in new[] { "iswithinfog =", "sound_objectref =", "allianceconstructionstarted =", "GamePrefs.minimumsupplyoffensivetargets =", "GamePrefs.winterdefensiveoperationsmodifier =" })
            AssertFalse(body.Contains(write), file + " Prefix writes " + write + " only through its swap");
    }

    // The decisions get the vanilla state they are meant to judge.
    private static void PatchesPassDecisionsVanillaState()
    {
        string camp = PatchSource("FoggedCampPatch.cs");
        AssertTrue(camp.Contains("bool active = !FoggedModelRule.ShouldHideModels(isPlayerAlliance, __instance.iswithinfog);"), "camp visibility from the restored fog flag");
        AssertTrue(camp.Contains("SyncModel(__instance.campbuilding, active)") && camp.Contains("SyncModel(__instance.entrenchment, active)"), "camp and entrenchment both synced");
        AssertTrue(camp.Contains("if (isPlayerAlliance) return;"), "player units never touched");
        string embark = PatchSource("FoggedEmbarkationPatch.cs");
        AssertTrue(embark.Contains("!FoggedModelRule.ShouldHideModels(isPlayerAlliance, __instance.iswithinfog));"), "model visibility from the restored fog flag");
        AssertTrue(embark.Contains("if (isPlayerAlliance || __instance.unittyp == 17) return;"), "player units and fleets never touched");
        AssertTrue(embark.Contains("__state = (UnityEngine.Object)__instance.shownmodel != null;"), "model presence captured before vanilla runs");
        string sounds = embark.Substring(embark.IndexOf("class SharedFiringSounds", StringComparison.Ordinal));
        string share = "if (!FoggedModelRule.ShouldShareFiringSounds(Flip.Active, soundpurposeid, Regiment.csound_shipfiring)) return true;";
        string soundPrefix = MethodBody(sounds, "Prefix");
        AssertTrue(soundPrefix.Contains(share), "the sound Prefix skips vanilla only inside the fix and for ship-firing arrays");
        AssertTrue(soundPrefix.IndexOf(share, StringComparison.Ordinal) < soundPrefix.IndexOf("return false;", StringComparison.Ordinal), "the sound Prefix decides before it skips vanilla");
        AssertTrue(soundPrefix.Contains("if (!FiringSounds.TryGet(__instance, out int index)) return true;"), "the sound Prefix skips only with an array of this engine");
        string soundPostfix = MethodBody(sounds, "Postfix");
        AssertTrue(soundPostfix.Contains("if (FoggedModelRule.ShouldShareFiringSounds(Flip.Active, soundpurposeid, Regiment.csound_shipfiring))") && soundPostfix.Contains("FiringSounds.Remember(__instance, __result);"), "the sound Postfix remembers only inside the fix");
        string raid = PatchSource("RaidSupplyThresholdPatch.cs");
        AssertTrue(raid.Contains("RaidSupplyThreshold.ShouldRescale(Enabled(), hasGroup, DLC_WL.dlc_scenarioactive, hasGroup && __instance.raidgroup.dlcw_isundercommander)"), "W&L raid guard wired");
        AssertTrue(raid.Contains("Threshold.Apply(RaidSupplyThreshold.ThresholdOnGroupSupplyScale(GamePrefs.minimumsupplyoffensivetargets))"), "raid threshold rescaled");
        string winter = PatchSource("WinterOffensiveModifierPatch.cs");
        AssertTrue(winter.Contains("WinterOperationsModifier.OffensiveRollModifier(true, true, GamePrefs.winteroffensiveoperationsmodifier, GamePrefs.winterdefensiveoperationsmodifier)"), "winter roll gets the offensive modifier");
        string random = PatchSource("RandomCandidatePickPatch.cs");
        AssertTrue(random.Contains("return result;") && random.Contains("if (site == null) return vanilla;"), "random transpiler emits the helper's choice");
        string trim = PatchSource("OffensiveWeaponTrimPatch.cs");
        AssertTrue(trim.Contains("return result;"), "trim transpiler emits the helper's choice");
    }

    private static string PatchSource(string file)
    {
        return File.ReadAllText(Path.Combine(TestFixtures.FindRepoPath("src/WhiskeyRealism"), "Patches", file));
    }

    // Body of the first method named `name` (from its first '{' to the matching '}').
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
