using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using WhiskeyRealism.Strategic.BugFixes;
using static TestHarness;

// Campaign logistics vanilla bug fixes (docs/bug-fixes/vanilla-campaign-logistics.md,
// BUG-LOG-001..013). Each test pins a pure decision a patch takes, or runs an IL
// rewrite against the real vanilla method body (refs/Assembly-CSharp.dll).
internal static class CampaignLogisticsFixTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("logistics vanilla group supply state never reaches the raid threshold", VanillaGroupSupplyStateNeverReachesRaidThreshold),
            ("logistics rescaled raid threshold means real supply", RescaledRaidThresholdMeansRealSupply),
            ("logistics fogged ai land unit keeps its camp", FoggedAiLandUnitKeepsItsCamp),
            ("logistics camp rule never touches player or visible units", CampRuleNeverTouchesPlayerOrVisibleUnits),
            ("logistics camp models hidden only for fogged ai units", CampModelsHiddenOnlyForFoggedAiUnits),
            ("logistics fogged ai land unit keeps its embarkation", FoggedAiLandUnitKeepsItsEmbarkation),
            ("logistics area membership registers only after vanilla stops", AreaMembershipRegistersOnlyAfterVanillaStops),
            ("logistics area membership adds without duplicates", AreaMembershipAddsWithoutDuplicates),
            ("logistics area membership prunes destroyed members", AreaMembershipPrunesDestroyedMembers),
            ("logistics winter offensive roll uses offensive modifier", WinterOffensiveRollUsesOffensiveModifier),
            ("logistics winter months match vanilla bounds", WinterMonthsMatchVanillaBounds),
            ("logistics only own or allied depots are supply sources", OnlyOwnOrAlliedDepotsAreSupplySources),
            ("logistics only finished own-started depots are masked", OnlyFinishedOwnStartedDepotsAreMasked),
            ("logistics depot builders must stand in bugle range", DepotBuildersMustStandInBugleRange),
            ("logistics depot keeps its smallest builder only", DepotKeepsItsSmallestBuilderOnly),
            ("logistics depot builder pins one depot and skips ineligible units", DepotBuilderPinsOneDepotAndSkipsIneligible),
            ("logistics subordinate grab accumulates to the target", SubordinateGrabAccumulatesToTheTarget),
            ("logistics subordinate grab honours vanilla filters and count cap", SubordinateGrabHonoursVanillaFilters),
            ("logistics offensive trim uses the blended weapon factor", OffensiveTrimUsesTheBlendedWeaponFactor),
            ("logistics offensive trim il rewrite hits the one vanilla site", OffensiveTrimIlRewriteHitsTheOneVanillaSite),
            ("logistics offensive trim il rewrite leaves other methods alone", OffensiveTrimIlRewriteLeavesOtherMethodsAlone),
            ("logistics offensive trim il rewrite needs the exact shape once", OffensiveTrimIlRewriteNeedsTheExactShapeOnce),
            ("logistics random pick upper offset", RandomPickUpperOffset),
            ("logistics random pick il rewrite hits every campaign site", RandomPickIlRewriteHitsEveryCampaignSite),
            ("logistics random pick il rewrite refuses an unexpected count", RandomPickIlRewriteRefusesUnexpectedCount),
            ("logistics confiscation factor shortens the supply duration", ConfiscationFactorShortensTheSupplyDuration),
            ("logistics raid scale matches vanilla group supply state il", RaidScaleMatchesVanillaGroupSupplyStateIl),
            ("logistics patch targets bind the vanilla names", PatchTargetsBindTheVanillaNames),
        });
    }

    // AICampaign.GetGroupSupplyState (14063-14066) = max(avg(groupsupplystate) * 0.5,
    // supplystate * 0.5); both inputs are 0..1, so a fully supplied group scores 0.5,
    // below minimumsupplyoffensivetargets 0.75 (campaignprefs 1411) that
    // RaidForce.SearchObjective requires (9135).
    private static void VanillaGroupSupplyStateNeverReachesRaidThreshold()
    {
        float full = VanillaGroupSupplyState(1f, 1f, 1f, 1f, 1f);
        AssertNear(0.5f, full, 0.0001f, "full supply scores 0.5");
        AssertTrue(full < 0.75f, "vanilla full supply fails the 0.75 raid gate");
        AssertNear(0.4f, VanillaGroupSupplyState(0.8f, 0.8f, 0.8f, 0.8f, 0.2f), 0.0001f, "max of the halved terms");
        AssertNear(0.45f, VanillaGroupSupplyState(0.1f, 0.1f, 0.1f, 0.1f, 0.9f), 0.0001f, "supplystate term wins");
    }

    // Mirror of AICampaign.GetGroupSupplyState (14063-14066).
    private static float VanillaGroupSupplyState(float rifleAmmo, float artilleryAmmo, float provisions, float forage, float supplyState)
    {
        float scale = RaidSupplyThreshold.GroupSupplyStateScale;
        return Math.Max((rifleAmmo + artilleryAmmo + provisions + forage) / 4f * scale, supplyState * scale);
    }

    private static void RescaledRaidThresholdMeansRealSupply()
    {
        float threshold = RaidSupplyThreshold.ThresholdOnGroupSupplyScale(0.75f);
        AssertNear(0.375f, threshold, 0.0001f, "0.75 on the halved scale");
        AssertTrue(VanillaGroupSupplyState(1f, 1f, 1f, 1f, 1f) >= threshold, "full supply passes");
        AssertTrue(VanillaGroupSupplyState(0.75f, 0.75f, 0.75f, 0.75f, 0f) >= threshold, "75 percent real supply passes");
        AssertFalse(VanillaGroupSupplyState(0.7f, 0.7f, 0.7f, 0.7f, 0.7f) >= threshold, "70 percent real supply fails");
        AssertNear(0.125f, RaidSupplyThreshold.ThresholdOnGroupSupplyScale(0.25f), 0.0001f, "scale is linear");
    }

    // Regiment.UpdateCampsAndEntrenchments (115392-115470) places a camp only when
    // !iswithinfog (115408) and destroys it when iswithinfog (115426); for a
    // non-player unit iswithinfog means hidden from the player (127221-127226).
    private static void FoggedAiLandUnitKeepsItsCamp()
    {
        AssertTrue(FoggedModelRule.ShouldTreatAsVisibleForCamp(true, true, true, false, true, false), "fogged AI land unit on the campaign map");
        AssertFalse(FoggedModelRule.ShouldTreatAsVisibleForCamp(false, true, true, false, true, false), "flag off leaves vanilla");
        AssertFalse(FoggedModelRule.ShouldTreatAsVisibleForCamp(true, true, true, false, true, true), "fogged unit on river or sea gets no camp (vanilla shows a boat there)");
        AssertFalse(FoggedModelRule.ShouldTreatAsVisibleForCamp(true, false, true, false, true, false), "battle map untouched");
        AssertFalse(FoggedModelRule.ShouldTreatAsVisibleForCamp(true, true, false, false, true, false), "fog of war off");
    }

    private static void CampRuleNeverTouchesPlayerOrVisibleUnits()
    {
        AssertFalse(FoggedModelRule.ShouldTreatAsVisibleForCamp(true, true, true, true, true, false), "player alliance unit");
        AssertFalse(FoggedModelRule.ShouldTreatAsVisibleForCamp(true, true, true, false, false, false), "visible AI unit already camps in vanilla");
    }

    private static void CampModelsHiddenOnlyForFoggedAiUnits()
    {
        AssertTrue(FoggedModelRule.ShouldHideModels(false, true), "fogged AI unit hides its camp");
        AssertFalse(FoggedModelRule.ShouldHideModels(false, false), "visible AI unit shows its camp");
        AssertFalse(FoggedModelRule.ShouldHideModels(true, true), "player unit never hidden");
        AssertFalse(FoggedModelRule.ShouldHideModels(true, false), "player unit shown");
    }

    // Regiment.CheckModelReplacement (116372-116503) creates the train/boat model and sets
    // currentmodeltype only when !iswithinfog (116383, 116399, 116446) and removes it when
    // fogged (116461); speed (133401, 132891, 133958), transport capacity (116471-116501),
    // consumption (114186, 133900-133915) and reinforcements (114501) read them.
    private static void FoggedAiLandUnitKeepsItsEmbarkation()
    {
        AssertTrue(FoggedModelRule.ShouldTreatAsVisibleForEmbarkation(true, true, true, false, true, false), "fogged AI land unit");
        AssertFalse(FoggedModelRule.ShouldTreatAsVisibleForEmbarkation(false, true, true, false, true, false), "flag off");
        AssertFalse(FoggedModelRule.ShouldTreatAsVisibleForEmbarkation(true, true, true, true, true, false), "player alliance");
        AssertFalse(FoggedModelRule.ShouldTreatAsVisibleForEmbarkation(true, true, true, false, false, false), "visible unit is vanilla already");
        AssertFalse(FoggedModelRule.ShouldTreatAsVisibleForEmbarkation(true, true, true, false, true, true), "fleets are out of scope");
        AssertFalse(FoggedModelRule.ShouldTreatAsVisibleForEmbarkation(true, false, true, false, true, false), "battle map");
        AssertFalse(FoggedModelRule.ShouldTreatAsVisibleForEmbarkation(true, true, false, false, true, false), "fog of war off");
    }

    // AICampaign.UpdateImportanceValues fills AIArea.iips/cbuildings/towns only while
    // airuns <= 1 (14927, 14946, 14965).
    private static void AreaMembershipRegistersOnlyAfterVanillaStops()
    {
        AssertFalse(AIAreaMembership.ShouldRegister(true, 0), "run 0 vanilla registers");
        AssertFalse(AIAreaMembership.ShouldRegister(true, 1), "run 1 vanilla registers");
        AssertTrue(AIAreaMembership.ShouldRegister(true, 2), "run 2 vanilla stopped");
        AssertTrue(AIAreaMembership.ShouldRegister(true, 40), "late runs");
        AssertFalse(AIAreaMembership.ShouldRegister(false, 40), "flag off");
    }

    private static void AreaMembershipAddsWithoutDuplicates()
    {
        var a = new object();
        var b = new object();
        var members = new List<object> { a };
        AssertFalse(AIAreaMembership.AddIfMissing(members, a), "existing member not re-added");
        AssertTrue(AIAreaMembership.AddIfMissing(members, b), "new member added");
        AssertFalse(AIAreaMembership.AddIfMissing(members, b), "second add is a no-op");
        AssertFalse(AIAreaMembership.AddIfMissing(members, null), "null never added");
        AssertEqual(2, members.Count, "member count");
        AssertFalse(AIAreaMembership.AddIfMissing<object>(null, b), "null list tolerated");
    }

    private static void AreaMembershipPrunesDestroyedMembers()
    {
        var keep = "keep";
        var gone = "gone";
        var members = new List<string> { gone, keep, null, gone };
        int removed = AIAreaMembership.RemoveWhere(members, m => m == null || m == gone);
        AssertEqual(3, removed, "removed count");
        AssertEqual(1, members.Count, "survivors");
        AssertEqual(keep, members[0], "survivor kept in place");
        AssertEqual(0, AIAreaMembership.RemoveWhere<string>(null, m => true), "null list tolerated");
    }

    // CheckOffensiveMovements rolls with winterdefensiveoperationsmodifier (14310-14312);
    // winteroffensiveoperationsmodifier (campaignprefs 1546, 0.05) is loaded (56381)
    // and never read.
    private static void WinterOffensiveRollUsesOffensiveModifier()
    {
        AssertNear(0.05f, WinterOperationsModifier.OffensiveRollModifier(true, true, 0.05f, 0.2f), 0.0001f, "winter fixed");
        AssertNear(0.2f, WinterOperationsModifier.OffensiveRollModifier(true, false, 0.05f, 0.2f), 0.0001f, "winter vanilla");
        AssertNear(1f, WinterOperationsModifier.OffensiveRollModifier(false, true, 0.05f, 0.2f), 0.0001f, "summer unaffected");
    }

    private static void WinterMonthsMatchVanillaBounds()
    {
        AssertTrue(WinterOperationsModifier.IsWinterMonth(3, 3, 11), "March is winter (<= until)");
        AssertFalse(WinterOperationsModifier.IsWinterMonth(4, 3, 11), "April");
        AssertFalse(WinterOperationsModifier.IsWinterMonth(10, 3, 11), "October");
        AssertTrue(WinterOperationsModifier.IsWinterMonth(11, 3, 11), "November is winter (>= from)");
        AssertTrue(WinterOperationsModifier.IsWinterMonth(1, 3, 11), "January");
    }

    // CheckSupplyDepotConstruction (14738-14761) admits a depot when it is own, allied,
    // or merely has no enemy units in range (14740). Regiment.UpdateIIPSupplyLink
    // (114296-114306) draws only from own or allied depots.
    private static void OnlyOwnOrAlliedDepotsAreSupplySources()
    {
        AssertTrue(SupplyDepotAiRules.IsFriendlySupplySource(1, 1, false), "own depot");
        AssertTrue(SupplyDepotAiRules.IsFriendlySupplySource(2, 1, true), "allied depot");
        AssertFalse(SupplyDepotAiRules.IsFriendlySupplySource(0, 1, false), "enemy depot, defended or not");
        AssertFalse(SupplyDepotAiRules.IsFriendlySupplySource(-1, 1, false), "unowned depot");
    }

    // The builder loop (14690-14713) sets the skip flag for any depot the alliance
    // started within commander range, finished or not (14697).
    private static void OnlyFinishedOwnStartedDepotsAreMasked()
    {
        AssertTrue(SupplyDepotAiRules.ShouldMaskFinishedOwnDepot(1, 1, 0f), "finished own-started depot masked");
        AssertTrue(SupplyDepotAiRules.ShouldMaskFinishedOwnDepot(1, 1, -0.1f), "overshot timer counts as finished");
        AssertFalse(SupplyDepotAiRules.ShouldMaskFinishedOwnDepot(1, 1, 0.4f), "depot under construction keeps its builders");
        AssertFalse(SupplyDepotAiRules.ShouldMaskFinishedOwnDepot(0, 1, 0f), "enemy-started depot never read by the loop");
        AssertFalse(SupplyDepotAiRules.ShouldMaskFinishedOwnDepot(-1, 1, 0f), "pre-placed depot");
    }

    // CBuilding.IncreaseCondition advances construction only while a unit of the
    // starting alliance stands within its own buglerange (96626-96655).
    private static void DepotBuildersMustStandInBugleRange()
    {
        var units = new List<SupplyDepotAiRules.BuilderUnit>
        {
            Unit(eligible: true, strength: 3000, x: 50f, z: 0f, bugle: 20f),   // in commander range only
            Unit(eligible: true, strength: 8000, x: 10f, z: 0f, bugle: 20f),   // in bugle range
        };
        var depots = new List<SupplyDepotAiRules.DepotSite> { Depot(0f, 0f, underConstruction: true) };
        var builders = SupplyDepotAiRules.SelectBuilders(units, depots);
        AssertEqual(1, builders.Count, "one builder");
        AssertEqual(1, builders[0], "the unit inside bugle range builds");
    }

    private static void DepotKeepsItsSmallestBuilderOnly()
    {
        var units = new List<SupplyDepotAiRules.BuilderUnit>
        {
            Unit(true, 2000, 5f, 0f, 20f),
            Unit(true, 9000, 6f, 0f, 20f),   // bigger, arrives later: vanilla added it anyway (14709-14711)
            Unit(true, 1500, 7f, 0f, 20f),   // smaller: replaces the 2000 builder
        };
        var depots = new List<SupplyDepotAiRules.DepotSite> { Depot(0f, 0f, true) };
        var builders = SupplyDepotAiRules.SelectBuilders(units, depots);
        AssertEqual(1, builders.Count, "one builder per depot");
        AssertEqual(2, builders[0], "smallest unit kept");
    }

    private static void DepotBuilderPinsOneDepotAndSkipsIneligible()
    {
        var units = new List<SupplyDepotAiRules.BuilderUnit>
        {
            Unit(true, 2000, 0f, 0f, 30f),     // near both depots: pins only the first
            Unit(false, 100, 1f, 0f, 30f),     // moving or garrison: never a builder
            Unit(true, 4000, 20f, 0f, 30f),
        };
        var depots = new List<SupplyDepotAiRules.DepotSite>
        {
            Depot(5f, 0f, true),
            Depot(15f, 0f, true),
            Depot(2f, 0f, false),              // finished: no builders
        };
        var builders = SupplyDepotAiRules.SelectBuilders(units, depots);
        AssertEqual(2, builders.Count, "two depots, two builders");
        AssertTrue(builders.Contains(0) && builders.Contains(2), "unit 0 on depot 0, unit 2 on depot 1");
        AssertFalse(builders.Contains(1), "ineligible unit skipped");
        AssertEqual(0, SupplyDepotAiRules.SelectBuilders(null, depots).Count, "null units");
    }

    private static SupplyDepotAiRules.BuilderUnit Unit(bool eligible, int strength, float x, float z, float bugle)
    {
        return new SupplyDepotAiRules.BuilderUnit { Eligible = eligible, Strength = strength, X = x, Z = z, BugleRange = bugle };
    }

    private static SupplyDepotAiRules.DepotSite Depot(float x, float z, bool underConstruction)
    {
        return new SupplyDepotAiRules.DepotSite { X = x, Z = z, UnderConstruction = underConstruction };
    }

    // BattleUnits.GrabSubordinateByStrength (93549-93597) tests num + num5 <= target
    // but then sets num = num5 (93594) instead of accumulating.
    private static void SubordinateGrabAccumulatesToTheTarget()
    {
        var candidates = new List<SubordinateStrengthSelector.Candidate>
        {
            Brigade(5), Brigade(5), Brigade(4), Brigade(1),
        };
        var picked = SubordinateStrengthSelector.Select(candidates, target: 10, minimumAmount: 1, maxCount: 10);
        AssertEqual(2, picked.Count, "vanilla's last-only compare would take all four (15 > 10)");
        AssertEqual(0, picked[0], "first");
        AssertEqual(1, picked[1], "second");

        var later = SubordinateStrengthSelector.Select(
            new List<SubordinateStrengthSelector.Candidate> { Brigade(2), Brigade(2), Brigade(7), Brigade(6) },
            target: 10, minimumAmount: 1, maxCount: 10);
        AssertEqual(3, later.Count, "a later brigade that still fits is taken");
        AssertEqual(3, later[2], "the 6 fits after 2+2, the 7 does not");
    }

    private static void SubordinateGrabHonoursVanillaFilters()
    {
        var candidates = new List<SubordinateStrengthSelector.Candidate>
        {
            new SubordinateStrengthSelector.Candidate { Eligible = false, Strength = 1 },
            Brigade(50),                                                   // below minimumamount
            new SubordinateStrengthSelector.Candidate { Eligible = true, Strength = 300, InTransfer = true },
            Brigade(300),
            Brigade(300),
            Brigade(300),
        };
        var picked = SubordinateStrengthSelector.Select(candidates, target: 5000, minimumAmount: 100, maxCount: 2);
        AssertEqual(2, picked.Count, "count cap num3 - leaveminimumbrigades");
        AssertEqual(3, picked[0], "first eligible");
        AssertEqual(4, picked[1], "second eligible");
        AssertEqual(0, SubordinateStrengthSelector.Select(candidates, 5000, 100, 0).Count, "zero cap");
        AssertEqual(0, SubordinateStrengthSelector.Select(null, 5000, 100, 3).Count, "null candidates");
    }

    private static SubordinateStrengthSelector.Candidate Brigade(int strength)
    {
        return new SubordinateStrengthSelector.Candidate { Eligible = true, Strength = strength };
    }

    // CheckOffensiveMovements adds and counts units with the blend
    // 1 * (1 - influence) + influence * w (14239, 14274) but trims with
    // w *= influence * w + (1 - influence) (14294).
    private static void OffensiveTrimUsesTheBlendedWeaponFactor()
    {
        float w = 1.6f;
        float influence = 0.3f;
        float blend = OffensiveWeaponTrimIl.Blend(w, influence);
        AssertNear(1.18f, blend, 0.0001f, "blend");
        AssertNear(w * blend, OffensiveWeaponTrimIl.LeftOperand(w, false) * blend, 0.0001f, "vanilla trim squares the factor");
        AssertNear(blend, OffensiveWeaponTrimIl.LeftOperand(w, true) * blend, 0.0001f, "fixed trim uses the blend");
    }

    private static void OffensiveTrimIlRewriteHitsTheOneVanillaSite()
    {
        var method = AccessTools.Method(typeof(AICampaign), "CheckOffensiveMovements");
        AssertTrue(method != null, "vanilla method resolves");
        var code = PatchProcessor.GetOriginalInstructions(method);
        int before = code.Count;
        var hook = AccessTools.Method(typeof(CampaignLogisticsFixTests), nameof(TrimHookStub));
        int matches = OffensiveWeaponTrimIl.Rewrite(code, OffensiveWeaponTrimIl.ResolveWeaponStrengthFactor(), OffensiveWeaponTrimIl.ResolveInfluenceField(), hook);
        AssertEqual(1, matches, "exactly the trim site");
        AssertEqual(before + 1, code.Count, "one hook call inserted");
        int at = code.FindIndex(ci => ci.opcode == OpCodes.Call && Equals(ci.operand, hook));
        AssertTrue(at > 0 && code[at - 1].IsLdloc(), "hook consumes the left operand load");
        AssertTrue(code[at + 1].opcode == OpCodes.Ldsfld, "influence read follows");
    }

    private static void OffensiveTrimIlRewriteLeavesOtherMethodsAlone()
    {
        var method = AccessTools.Method(typeof(AICampaign), "CalculateZoneRecruitingProbs");
        AssertTrue(method != null, "vanilla method with the correct blend resolves");
        var code = PatchProcessor.GetOriginalInstructions(method);
        int before = code.Count;
        var hook = AccessTools.Method(typeof(CampaignLogisticsFixTests), nameof(TrimHookStub));
        int matches = OffensiveWeaponTrimIl.Rewrite(code, OffensiveWeaponTrimIl.ResolveWeaponStrengthFactor(), OffensiveWeaponTrimIl.ResolveInfluenceField(), hook);
        AssertEqual(0, matches, "the correct blend is not a match");
        AssertEqual(before, code.Count, "unchanged");
    }

    private static void OffensiveTrimIlRewriteNeedsTheExactShapeOnce()
    {
        var factor = OffensiveWeaponTrimIl.ResolveWeaponStrengthFactor();
        var influence = OffensiveWeaponTrimIl.ResolveInfluenceField();
        var hook = AccessTools.Method(typeof(CampaignLogisticsFixTests), nameof(TrimHookStub));

        var exact = TrimShape(factor, influence, OpCodes.Mul);
        AssertEqual(1, OffensiveWeaponTrimIl.Rewrite(exact, factor, influence, hook), "exact shape");
        AssertEqual(15, exact.Count, "hook inserted");

        var otherOuterOp = TrimShape(factor, influence, OpCodes.Add);
        AssertEqual(0, OffensiveWeaponTrimIl.Rewrite(otherOuterOp, factor, influence, hook), "w + w' is not the trim");
        AssertEqual(14, otherOuterOp.Count, "untouched");

        var twice = TrimShape(factor, influence, OpCodes.Mul);
        twice.AddRange(TrimShape(factor, influence, OpCodes.Mul));
        AssertEqual(2, OffensiveWeaponTrimIl.Rewrite(twice, factor, influence, hook), "two sites reported");
        AssertEqual(28, twice.Count, "two sites: nothing rewritten");
    }

    private static List<CodeInstruction> TrimShape(MethodInfo factor, FieldInfo influence, OpCode outer)
    {
        return new List<CodeInstruction>
        {
            new CodeInstruction(OpCodes.Call, factor),
            new CodeInstruction(OpCodes.Stloc_S, (byte)8),
            new CodeInstruction(OpCodes.Ldloc_S, (byte)8),
            new CodeInstruction(OpCodes.Ldsfld, influence),
            new CodeInstruction(OpCodes.Ldloc_S, (byte)8),
            new CodeInstruction(OpCodes.Mul),
            new CodeInstruction(OpCodes.Ldc_R4, 1f),
            new CodeInstruction(OpCodes.Ldsfld, influence),
            new CodeInstruction(OpCodes.Sub),
            new CodeInstruction(OpCodes.Ldc_R4, 1f),
            new CodeInstruction(OpCodes.Mul),
            new CodeInstruction(OpCodes.Add),
            new CodeInstruction(outer),
            new CodeInstruction(OpCodes.Stloc_S, (byte)8),
        };
    }

    public static float TrimHookStub(float w)
    {
        return w;
    }

    private static void RandomPickUpperOffset()
    {
        AssertEqual(0, RandomPickRangeIl.UpperOffset(true), "fixed: Range(0, Count) includes the last candidate");
        AssertEqual(1, RandomPickRangeIl.UpperOffset(false), "vanilla: Range(0, Count - 1)");
    }

    // Random.Range(int, int) excludes its upper bound; these AICampaign sites pass
    // Count - 1 (12146, 12241, 12302, 12793, 12801, 12928, 13040, 17611).
    private static void RandomPickIlRewriteHitsEveryCampaignSite()
    {
        var hook = AccessTools.Method(typeof(CampaignLogisticsFixTests), nameof(OffsetHookStub));
        var range = RandomPickRangeIl.ResolveRandomRangeInt();
        AssertTrue(range != null, "UnityEngine.Random.Range(int, int) resolves");
        int total = 0;
        foreach (var site in RandomPickRangeIl.Sites)
        {
            var method = AccessTools.Method(typeof(AICampaign), site.Method);
            AssertTrue(method != null, site.Method + " resolves");
            var code = PatchProcessor.GetOriginalInstructions(method);
            int before = code.Count;
            int matches = RandomPickRangeIl.Rewrite(code, range, hook, site.Expected);
            AssertEqual(site.Expected, matches, site.Method + " site count");
            AssertEqual(before, code.Count, site.Method + " rewrite is in place");
            AssertEqual(site.Expected, code.Count(ci => ci.opcode == OpCodes.Call && Equals(ci.operand, hook)), site.Method + " hooks");
            total += matches;
        }
        AssertEqual(8, total, "eight vanilla sites");
    }

    private static void RandomPickIlRewriteRefusesUnexpectedCount()
    {
        var hook = AccessTools.Method(typeof(CampaignLogisticsFixTests), nameof(OffsetHookStub));
        var method = AccessTools.Method(typeof(AICampaign), "GrabExistingGroup");
        var code = PatchProcessor.GetOriginalInstructions(method);
        var snapshot = code.Select(ci => ci.opcode).ToList();
        int matches = RandomPickRangeIl.Rewrite(code, RandomPickRangeIl.ResolveRandomRangeInt(), hook, 1);
        AssertEqual(2, matches, "two sites found");
        AssertTrue(snapshot.SequenceEqual(code.Select(ci => ci.opcode)), "count mismatch leaves vanilla IL untouched");
        AssertEqual(0, code.Count(ci => ci.opcode == OpCodes.Call && Equals(ci.operand, hook)), "no hook inserted");
    }

    public static int OffsetHookStub()
    {
        return 1;
    }

    // Policies.LaunchPolicyEffect case 25 multiplies supplyenemysoilfactor by
    // confiscationsupplyflow 1.15 (209893, campaignprefs 2788 "supply flow
    // improvement ... [>1]"); Regiment.UpdateIIPSupplyLink multiplies the resupply
    // duration num15 by it (114330-114335), so the act slowed supply.
    private static void ConfiscationFactorShortensTheSupplyDuration()
    {
        float fixedFactor = ConfiscationSupplyFlow.DurationFactor(1.15f);
        AssertNear(1f / 1.15f, fixedFactor, 0.0001f, "duration divided by the improvement");
        AssertTrue(fixedFactor < 1f, "supply is faster on enemy soil");
        AssertNear(1f, ConfiscationSupplyFlow.DurationFactor(1f), 0.0001f, "no act, no change");
        AssertNear(1f / (1.15f * 1.15f), ConfiscationSupplyFlow.DurationFactor(1.15f * 1.15f), 0.0001f, "stacked effect inverted whole");
        AssertNear(0f, ConfiscationSupplyFlow.DurationFactor(0f), 0.0001f, "degenerate factor left alone");
        AssertNear(-1f, ConfiscationSupplyFlow.DurationFactor(-1f), 0.0001f, "negative factor left alone");
    }

    // GetGroupSupplyState multiplies both of its terms by 0.5 and takes Mathf.Max.
    private static void RaidScaleMatchesVanillaGroupSupplyStateIl()
    {
        var method = AccessTools.Method(typeof(AICampaign), "GetGroupSupplyState");
        AssertTrue(method != null, "GetGroupSupplyState resolves");
        var code = PatchProcessor.GetOriginalInstructions(method);
        int halves = code.Count(ci => ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f && f == RaidSupplyThreshold.GroupSupplyStateScale);
        AssertEqual(2, halves, "both terms scaled by GroupSupplyStateScale");
        AssertTrue(code.Any(ci => ci.operand is MethodInfo m && m.Name == "Max"), "max of the two terms");
    }

    // Harmony binds patch parameters by name; a renamed vanilla parameter or field would
    // fail PatchAll.
    private static void PatchTargetsBindTheVanillaNames()
    {
        AssertHasParameters(AccessTools.Method(typeof(AICampaign.RaidForce), "SearchObjective"));
        AssertHasParameters(AccessTools.Method(typeof(Regiment), "UpdateCampsAndEntrenchments"), "forceupdate");
        AssertHasParameters(AccessTools.Method(typeof(AICampaign), "UpdateImportanceValues"));
        foreach (var field in new[] { "currentiip", "currentcbuild", "currenttown", "airuns" })
            AssertTrue(AccessTools.Field(typeof(AICampaign), field) != null, "AICampaign." + field);
        AssertHasParameters(AccessTools.Method(typeof(AICampaign), "CheckOffensiveMovements"), "_aifaction", "unit", "timediff");
        AssertHasParameters(AccessTools.Method(typeof(AICampaign), "CheckSupplyDepotConstruction"), "_aifaction");
        AssertHasParameters(AccessTools.Method(typeof(CBuilding), "EnemyUnitsInRange"), "alliancecalling", "usecombatrange", "rangefactor");
        AssertHasParameters(AccessTools.Method(typeof(BattleUnits), "GrabSubordinateByStrength"), "groupunit", "strengthtosearchfor", "minimumamount", "leaveminimumbrigades", "overridetype");
        AssertHasParameters(AccessTools.Method(typeof(Policies), "UpdatePolicyEffects"), "alliance", "timediff", "startsetup");
        AssertTrue(AccessTools.Field(typeof(Regiment), "sound_objectref") != null, "Regiment.sound_objectref");
        AssertHasParameters(AccessTools.Method(typeof(Regiment), "CheckModelReplacement"));
        AssertHasParameters(AccessTools.Method(typeof(Regiment), "OnDestroy"));
        foreach (var field in new[] { "shownmodel", "currentmodeltype", "campbuilding", "entrenchment", "iswithinfog" })
            AssertTrue(AccessTools.Field(typeof(Regiment), field) != null, "Regiment." + field);
    }

    private static void AssertHasParameters(MethodInfo method, params string[] names)
    {
        AssertTrue(method != null, "method resolves");
        var parameters = method.GetParameters().Select(p => p.Name).ToArray();
        AssertTrue(parameters.SequenceEqual(names), method.Name + " parameters: " + string.Join(",", parameters));
    }
}
