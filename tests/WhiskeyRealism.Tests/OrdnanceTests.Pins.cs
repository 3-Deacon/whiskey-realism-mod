using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Util;

using static TestHarness;
using static TestFixtures;

// Stage G slice G9: the ordnance runtime pins. The pure rules are OrdnanceTests; these read the runtime, the two patches, the detection
// runtime, the flags and the tags as source, because the code that touches vanilla is not compiled into this harness.
internal static class OrdnancePinTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("PurchaseKeepsVanillaLaneAndQuantity", PurchaseKeepsVanillaLaneAndQuantity),
            ("PurchaseBlockadeShowsInImportCost", PurchaseBlockadeShowsInImportCost),
            ("PurchaseEnemyEdgeFromBeliefsOnly", PurchaseEnemyEdgeFromBeliefsOnly),
            ("PurchaseNeverForCicPlayersAlliance", PurchaseNeverForCicPlayersAlliance),
            ("DetectionRecordsSightedWeaponQuality", DetectionRecordsSightedWeaponQuality),
            ("AlliancesTwoToFourIgnored", AlliancesTwoToFourIgnored),
            ("OrdnanceRearmPatchPinned", OrdnanceRearmPatchPinned),
            ("OrdnancePurchasePatchesPinned", OrdnancePurchasePatchesPinned),
            ("OrdnanceRuntimeReadsOnlyTheOwnAlliancesAverages", OrdnanceRuntimeReadsOnlyTheOwnAlliancesAverages),
            ("OrdnanceCostAndDeliveryComeFromVanillasOwnFigures", OrdnanceCostAndDeliveryComeFromVanillasOwnFigures),
            ("OrdnanceFlagsAreBoundAndRead", OrdnanceFlagsAreBoundAndRead),
            ("OrdnanceTagsAreDeclared", OrdnanceTagsAreDeclared),
        });
    }

    private const float OwnAverage = 1f;

    private static OrdnanceCandidate Cand(int id, float wsf = 2f, float bv = 1000f, float cost = 10f, float years = 0.5f, float std = 0.5f,
                                          bool researched = true, bool imported = false, float pm = 1f, bool open = false)
    {
        return new OrdnanceCandidate
        {
            Id = id, Researched = researched, Imported = imported, ProductionMultiplier = pm, OpenOrder = open,
            Wsf = wsf, BattleValue = bv, UnitCost = cost, DeliveryYears = years, Standardization = std
        };
    }

    private static OrdnanceChoice Pick(float enemy, int vanilla, params OrdnanceCandidate[] lane)
    {
        return OrdnancePicker.Pick(lane, OwnAverage, vanilla, enemy);
    }

    private static string Src(string path)
    {
        return ReadRepoFile("src/WhiskeyRealism/" + path);
    }

    private static int CountOccurrences(string text, string needle)
    {
        int count = 0, at = 0;
        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { count++; at += needle.Length; }
        return count;
    }

    private static void PurchaseKeepsVanillaLaneAndQuantity()
    {
        // The pure pick takes one lane's candidates and returns one of them; the lane and the amount are vanilla's, pinned in the runtime.
        string runtime = Src("Strategic/Planner/OrdnanceRuntime.cs");
        string tryPick = BodyOf(runtime, "internal static bool TryPick(");
        AssertTrue(HasLine(tryPick, "bool guns = vanilla.isari;"), "the lane is the isari of the model vanilla picked");
        AssertTrue(tryPick.Contains("weapons[j].isari != guns"), "a model of the other lane is never a candidate");
        AssertFalse(tryPick.Contains("quantity ="), "TryPick never edits the amount");
        string patch = Src("Patches/OrdnancePurchasePatch.cs");
        string pick = BodyOf(patch, "internal static void Prefix(int alliance, ref int weaponid, int quantity)");
        AssertFalse(Regex.IsMatch(pick, @"\bquantity\s*="), "the amount is read, never written");
        AssertFalse(Regex.IsMatch(pick, @"\balliance\s*="), "so is the alliance");
        AssertEqual(1, CountOccurrences(patch, "weaponid = "), "the model id is the only argument written");
        AssertFalse(Regex.IsMatch(patch, @"static\s+bool\s+Prefix"), "no skipping Prefix");
        var guns = new[] { Cand(2, wsf: 3f), Cand(3, wsf: 4f) };
        int[] laneIds = { 2, 3 };
        AssertTrue(Array.IndexOf(laneIds, OrdnancePicker.Pick(guns, OwnAverage, 2, 0f).Id) >= 0, "the pick is one of the lane's candidates");
    }

    private static void PurchaseBlockadeShowsInImportCost()
    {
        // GetProductionFigures carries the blockade factor in an import's share (64120-64124), so a blockaded import-only model costs
        // more and arrives later; the pick reads those two figures and nothing else about the blockade.
        var open = Cand(3, imported: true, researched: false, cost: 10f, years: 0.6f);
        var blockaded = Cand(2, imported: true, researched: false, cost: 22f, years: 1.7f);
        AssertEqual(3, Pick(0f, 2, blockaded, open).Id, "the same import under a blockade scores lower");
        var withBlockade = OrdnancePicker.Pick(new[] { blockaded, Cand(5, cost: 18f, years: 0.9f) }, OwnAverage, 2, 0f);
        AssertEqual(5, withBlockade.Id, "and loses to a domestic model that the blockade does not touch");
        string runtime = Src("Strategic/Planner/OrdnanceRuntime.cs");
        AssertFalse(runtime.Contains("averageblockaderatio"), "no private blockade term: vanilla's figures already carry it");
    }

    private static void PurchaseEnemyEdgeFromBeliefsOnly()
    {
        var beats = Cand(5, wsf: 2.0f);
        var trails = Cand(3, wsf: 1.2f);
        AssertEqual(3, Pick(0f, 5, beats, trails).Id, "an enemy never seen gives no edge term: the tie goes to the lower id");
        var edge = Pick(1.5f, 5, beats, trails);
        AssertEqual(5, edge.Id, "a believed enemy lane quality that one model beats and the other does not gives the edge");
        AssertEqual(PickReason.Edge, edge.Reason, "named");
        AssertEqual(3, Pick(2.5f, 5, beats, trails).Id, "a model that beats nothing the enemy fields gets no edge");
        // The believed quality is the beliefs' alone.
        AssertEqual(0f, BelievedQuality.Of(new EnemyBelief[0], false), "no belief: no quality");
        AssertEqual(0f, BelievedQuality.Of(null, true), "no list: no quality");
        string runtime = Src("Strategic/Planner/OrdnanceRuntime.cs");
        AssertTrue(runtime.Contains("EnemyDetectionRuntime.BeliefsFor(alliance)"), "the runtime asks for the beliefs of the steering alliance");
        AssertTrue(runtime.Contains("BelievedQuality.Of("), "and reduces them with the pure mean");
    }

    private static void PurchaseNeverForCicPlayersAlliance()
    {
        AssertFalse(SteeringGate.ShouldSteer(true, 0, 0, true, true, false, true), "the commander-in-chief's own alliance is never steered");
        AssertTrue(SteeringGate.ShouldSteer(true, 1, 0, true, false, false, true), "the enemy alliance of a commander-in-chief career is");
        AssertTrue(SteeringGate.ShouldSteer(true, 0, 0, true, false, false, true), "a corps-career player's alliance is steered: it is alliance-level buying, never the player's own command's arms");
        AssertFalse(SteeringGate.ShouldSteer(true, 0, 0, false, false, false, true), "the player of an ordinary campaign buys for himself");
        string runtime = Src("Strategic/Planner/OrdnanceRuntime.cs");
        AssertTrue(runtime.Contains("StrategicCoordinator.IsPlayerCICOf(alliance, GameVars.playeralliance)"), "the runtime asks vanilla's own commander-in-chief test (16263)");
    }

    private static void DetectionRecordsSightedWeaponQuality()
    {
        var q = default(SightedQuality);
        q.Add(false, 1.0f, 100f);
        q.Add(false, 2.0f, 300f);
        q.Add(true, 4.0f, 50f);
        AssertNear(1.75f, q.Rifle, 1e-5f, "the strength-weighted WSF of the rifle lane: (1x100 + 2x300) / 400");
        AssertNear(4.0f, q.Gun, 1e-5f, "of the gun lane");
        var none = default(SightedQuality);
        AssertEqual(0f, none.Rifle, "no armed unit seen: 0, not NaN (vanilla's 16211 would divide by zero)");
        none.Add(false, 3f, 0f);
        none.Add(false, float.NaN, 100f);
        none.Add(false, 2f, -5f);
        AssertEqual(0f, none.Rifle, "a unit with no strength, an unreadable WSF or a negative strength adds nothing");
        AssertEqual(0f, none.Gun, "the other lane stays empty");
        string runtime = Src("Strategic/Planner/EnemyDetectionRuntime.cs");
        AssertTrue(runtime.Contains("RifleQuality = ") && runtime.Contains("GunQuality = "), "the detection runtime records both lanes at the sighting");
        AssertTrue(runtime.Contains("SightedQuality") && runtime.Contains("AICampaign.GetWeaponStrengthFactor("), "from vanilla's own per-model WSF, split by the model's isari");
        AssertTrue(CountOccurrences(runtime, "QualityOf(") >= 3, "for a field group, an objective garrison and the rollback list");
    }

    private static void AlliancesTwoToFourIgnored()
    {
        for (int alliance = 2; alliance <= 4; alliance++)
        {
            AssertFalse(RearmPriority.ShouldReorder(true, alliance, 0, true, false, false, false, true), "the re-arm reorder ignores alliance " + alliance);
            AssertFalse(SteeringGate.ShouldSteer(true, alliance, 0, true, false, false, true), "the purchase steer ignores alliance " + alliance);
        }
        AssertFalse(SteeringGate.ShouldSteer(true, -1, 0, true, false, false, true), "a negative alliance");
        string runtime = Src("Strategic/Planner/OrdnanceRuntime.cs");
        AssertTrue(runtime.Contains("alliance < 0 || alliance >= Alliances"), "the runtime bound-checks its per-alliance state");
    }

    private static void OrdnanceRearmPatchPinned()
    {
        string patch = Src("Patches/RearmPriorityPatch.cs");
        AssertTrue(HasLine(patch, "[HarmonyPatch(typeof(AICampaign), \"CheckWeaponUpgrades\")]"), "vanilla's CheckWeaponUpgrades (16080)");
        AssertTrue(patch.Contains("[HarmonyPrefix]") && patch.Contains("[HarmonyFinalizer]"), "a Prefix and a Finalizer");
        AssertFalse(Regex.IsMatch(patch, @"static\s+bool\s+Prefix"), "no skipping Prefix");
        string prefix = BodyOf(patch, "internal static void Prefix(");
        string finalizer = BodyOf(patch, "internal static void Finalizer(");
        AssertTrue(HasLine(prefix, "PatchFirstFire.Mark(\"campaign.patch.ordnance-rearm.prefix\");"), "first-fire, prefix");
        AssertTrue(HasLine(finalizer, "PatchFirstFire.Mark(\"campaign.patch.ordnance-rearm.finalizer\");"), "first-fire, finalizer");
        AssertTrue(patch.Contains("TelemetryPerf.Scope(\"campaign.patch.ordnance-rearm.prefix\"") && patch.Contains("TelemetryPerf.Scope(\"campaign.patch.ordnance-rearm.finalizer\""), "a scope on each entry");
        AssertTrue(prefix.Contains("OrdnanceRuntime.BeginRearm(alliance, isinitialization)"), "the Prefix hands the call's two arguments to the runtime");
        AssertTrue(finalizer.Contains("OrdnanceRuntime.EndRearm(alliance, __state)"), "the Finalizer restores through the runtime, also when vanilla threw");
        string runtime = Src("Strategic/Planner/OrdnanceRuntime.cs");
        string begin = BodyOf(runtime, "internal static Regiment[] BeginRearm(");
        string end = BodyOf(runtime, "internal static void EndRearm(");
        AssertTrue(begin.Contains("RearmPriority.ShouldReorder("), "the pure gate decides");
        AssertTrue(begin.Contains("CampaignWithholdView.TryFreshAllocation(alliance, out var allocation)"), "on the published plan the planner's patches read");
        AssertTrue(begin.Contains("StrategicCoordinator.WlVanillaJobMachinePaused()"), "and the live job-machine pause (11368)");
        AssertTrue(begin.Contains("RearmPriority.Reorder(list, ranks)"), "the order is applied in place to vanilla's own list object");
        AssertTrue(end.Contains("RearmPriority.Restore(list, saved)"), "the restore writes vanilla's order back");
        AssertFalse(begin.Contains(".weapon =") || end.Contains(".weapon ="), "no unit is re-armed by Whiskey");
        AssertTrue(begin.Contains("GameVars.alliance[alliance].unitlist"), "the list is the alliance's unitlist (16091)");
    }

    private static void OrdnancePurchasePatchesPinned()
    {
        string patch = Src("Patches/OrdnancePurchasePatch.cs");
        AssertTrue(HasLine(patch, "[HarmonyPatch(typeof(AICampaign), \"CheckPurchaseWeapons\")]"), "the scope patch is on vanilla's CheckPurchaseWeapons (16259)");
        AssertTrue(HasLine(patch, "[HarmonyPatch(typeof(WeaponList), \"PlaceWeaponOrder\")]"), "the pick patch is on WeaponList.PlaceWeaponOrder (227136)");
        AssertTrue(patch.Contains("[ThreadStatic]"), "the scope is a thread-static, set by the first patch and cleared by its Finalizer");
        AssertTrue(patch.Contains("[HarmonyFinalizer]"), "cleared even when vanilla throws");
        foreach (string id in new[] { "campaign.patch.ordnance-purchase.scope.prefix", "campaign.patch.ordnance-purchase.scope.finalizer", "campaign.patch.ordnance-purchase.pick" })
        {
            AssertTrue(patch.Contains("TelemetryPerf.Scope(\"" + id + "\""), id + " has a scope");
            AssertTrue(HasLine(patch, "PatchFirstFire.Mark(\"" + id + "\");"), id + " has a first-fire mark");
        }
        string pick = BodyOf(patch, "internal static void Prefix(int alliance, ref int weaponid, int quantity)");
        AssertTrue(pick.Contains("OrdnanceRuntime.TryPick(alliance, weaponid, quantity, out int picked)"), "the pick is the runtime's");
        AssertTrue(pick.Contains("OrdnancePurchaseScopePatch.IsOpenFor(alliance)"), "acts only for the alliance whose purchase pass opened the scope");
        string scope = BodyOf(patch, "internal static void Prefix(int _alliance)");
        AssertTrue(scope.Contains("OrdnanceRuntime.PurchaseScopeOpens(_alliance)"), "the scope opens on the runtime's gate");
        string runtime = Src("Strategic/Planner/OrdnanceRuntime.cs");
        string opens = BodyOf(runtime, "internal static bool PurchaseScopeOpens(");
        AssertTrue(opens.Contains("SteeringGate.ShouldSteer("), "the shared stand-down rule");
        AssertTrue(opens.Contains("CampaignWithholdView.TryFreshAllocation(alliance, out _)"), "on a fresh published plan");
        AssertTrue(opens.Contains("StrategicCoordinator.WlVanillaJobMachinePaused()"), "with the job machine running");
        AssertTrue(opens.Contains("Plugin.EnableOrdnancePurchaseSteering"), "behind its flag");
        AssertTrue(opens.Contains("CampaignPlannerRuntime.ExecutorsEnabled()"), "and the planner's executors flag");
        string tryPick = BodyOf(runtime, "internal static bool TryPick(");
        AssertTrue(tryPick.Contains("OrdnancePicker.Pick("), "the decision is the pure one");
        AssertTrue(tryPick.Contains("OrdnancePicker.NeedsPriceRead("), "a price is read only for candidates that can win");
        AssertTrue(tryPick.Contains("GameVars.alliance[alliance].weaponorders[j].quantity > 0"), "vanilla's open-order test (16296)");
    }

    private static void OrdnanceRuntimeReadsOnlyTheOwnAlliancesAverages()
    {
        // Fog: G9 reads the steering alliance's OWN lane average (vanilla's candidate rule, 16295) and never another alliance's
        // (vanilla's 16266-16282 loop over the alliances at war with it).
        foreach (string file in new[] { "Strategic/Planner/OrdnanceRuntime.cs", "Patches/OrdnancePurchasePatch.cs", "Patches/RearmPriorityPatch.cs",
                                         "Strategic/Planner/OrdnancePicker.cs", "Strategic/Planner/EnemyWeaponQuality.cs", "Strategic/Planner/RearmPriority.cs" })
        {
            string code = string.Join("\n", PlannerReadCensusTests.CodeLines(Src(file)));
            foreach (string line in code.Split('\n'))
            {
                if (!line.Contains("averageweaponstrength")) continue;
                AssertTrue(line.Contains("GameVars.alliance[alliance].averageweaponstrength"), file + " reads an average only off the steering alliance: " + line.Trim());
            }
            AssertFalse(code.Contains("IsAtWarWith"), file + " does not ask who is at war: it has no enemy-alliance loop");
            AssertFalse(Regex.IsMatch(code, @"GameVars\.alliance\.Length"), file + " does not walk the alliances");
        }
        string runtime = Src("Strategic/Planner/OrdnanceRuntime.cs");
        AssertTrue(runtime.Contains("GameVars.alliance[alliance].averageweaponstrengthguns") && runtime.Contains("GameVars.alliance[alliance].averageweaponstrengthrifles"),
            "the own lane averages are read for the candidate rule");
    }

    private static void OrdnanceCostAndDeliveryComeFromVanillasOwnFigures()
    {
        string runtime = Src("Strategic/Planner/OrdnanceRuntime.cs");
        string tryPick = BodyOf(runtime, "internal static bool TryPick(");
        AssertTrue(tryPick.Contains("GetProductionFigures(alliance, j, quantity)"), "the figures are vanilla's, for the asking alliance and the order's amount (PlaceWeaponOrder 227140)");
        AssertTrue(tryPick.Contains("GetDeliveryTime(GameVars.playeralliance, quantity, j, figures)"), "the delivery time is taken as PlaceWeaponOrder takes it (227141)");
        AssertTrue(tryPick.Contains("Math.Abs(figures.cost) / quantity"), "the unit cost is the (negative) money delta per piece");
        AssertTrue(tryPick.Contains("standardizationforalliance"), "standardization is the model's own for the alliance");
        AssertFalse(runtime.Contains("consumegoods: true") || runtime.Contains("ConsumeWeapons("), "nothing is consumed: the price read is vanilla's dry run (31486)");
        AssertFalse(runtime.Contains("PlaceWeaponOrder("), "the order itself stays vanilla's");
    }

    private static void OrdnanceFlagsAreBoundAndRead()
    {
        string plugin = Src("Plugin.cs");
        foreach (var name in new[] { "Enable Rearm Priority", "Enable Ordnance Purchase Steering" })
            AssertTrue(plugin.Contains("\"Campaign Planner\", \"" + name + "\", true,"), name + " is bound, default on, in the planner's section");
        AssertTrue(Src("Strategic/Planner/OrdnanceRuntime.cs").Contains("Plugin.EnableRearmPriority"), "the re-arm gate reads its flag");
        AssertTrue(Src("Strategic/Planner/OrdnanceRuntime.cs").Contains("Plugin.EnableOrdnancePurchaseSteering"), "the purchase gate reads its flag");
    }

    private static void OrdnanceTagsAreDeclared()
    {
        string tags = Src("Telemetry/TelemetryTagPolicy.cs");
        AssertTrue(tags.Contains("AddCampaign(\"Ordnance\", TelemetryCategory.Decision)"), "[Ordnance] is a decision row");
        AssertTrue(tags.Contains("AddCampaign(\"OrdnancePurchase\", TelemetryCategory.Write)"), "[OrdnancePurchase] is a write row");
        string smoke = ReadRepoFile("tools/smoke-situational.txt");
        AssertTrue(Regex.IsMatch(smoke, @"^campaign\.patch\.ordnance-purchase\.pick\b", RegexOptions.Multiline), "the purchase pick fires only when vanilla places an AI order");
        AssertFalse(smoke.Contains("ordnance-rearm"), "the re-arm patch fires on every alliance tick: expected, not situational");
        AssertFalse(smoke.Contains("ordnance-purchase.scope"), "so does the purchase scope: CheckPurchaseWeapons is called every alliance tick (81757)");
    }
}
