using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Util;

using static TestHarness;
using static TestFixtures;

// Stage G slice G9 (docs/superpowers/plans/2026-10-15-campaign-planner.md § G9): ordnance. The re-arming priority (the unit list
// vanilla's CheckWeaponUpgrades walks, reordered by each unit's allocation role, restored after the call) and the scored,
// fog-honest weapon purchase (the model vanilla's PlaceWeaponOrder is given is replaced by the best of vanilla's own candidates
// that the order screen would allow), with the runtime pins at the end.
internal static class OrdnanceTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("RearmOrderFollowsAllocationRole", RearmOrderFollowsAllocationRole),
            ("RearmOrderStableWithinRole", RearmOrderStableWithinRole),
            ("RecoverAndTransitRearmLast", RecoverAndTransitRearmLast),
            ("UnassignedUnitsAfterAssignedRoles", UnassignedUnitsAfterAssignedRoles),
            ("RearmRestoresUnitListOrder", RearmRestoresUnitListOrder),
            ("RearmRestoreKeepsVanillaOrderWhenTheListChangedLength", RearmRestoreKeepsVanillaOrderWhenTheListChangedLength),
            ("RearmLeavesInitializationPassToVanilla", RearmLeavesInitializationPassToVanilla),
            ("RearmStandsDownWithStaleOrMissingPlan", RearmStandsDownWithStaleOrMissingPlan),
            ("RearmStandsDownWhileJobMachinePaused", RearmStandsDownWhileJobMachinePaused),
            ("RearmSmallestContainingGroupWins", RearmSmallestContainingGroupWins),
            ("RearmRowCountsUnitsByRank", RearmRowCountsUnitsByRank),
            ("SteeringGateIsTheRailRule", SteeringGateIsTheRailRule),
            ("PurchasePicksOnlyFromVanillaCandidatesThatAreOrderable", PurchasePicksOnlyFromVanillaCandidatesThatAreOrderable),
            ("ResearchedUnproducedModelNeverPicked", ResearchedUnproducedModelNeverPicked),
            ("PurchaseLeavesVanillaPickWhenNoOrderableCandidate", PurchaseLeavesVanillaPickWhenNoOrderableCandidate),
            ("PurchasePrefersValuePerCost", PurchasePrefersValuePerCost),
            ("PurchaseRateOfFireCountsInFull", PurchaseRateOfFireCountsInFull),
            ("PurchasePenalizesSlowDelivery", PurchasePenalizesSlowDelivery),
            ("PurchasePrefersStandardizedModel", PurchasePrefersStandardizedModel),
            ("PurchaseDeterministicOnTies", PurchaseDeterministicOnTies),
            ("PurchaseScoreStaysInUnitRange", PurchaseScoreStaysInUnitRange),
            ("PurchaseRowNamesTheChoice", PurchaseRowNamesTheChoice),
            ("BeliefCarriesSightedQualityAndKeepsItWhileUnseen", BeliefCarriesSightedQualityAndKeepsItWhileUnseen),
            ("BelievedQualityIgnoresLanesNoSightingShowed", BelievedQualityIgnoresLanesNoSightingShowed),
            ("OrdnancePureFilesHoldNoUnityTypes", OrdnancePureFilesHoldNoUnityTypes),
        });
    }

    private const int Alliance = 1;

    // ---- re-arming priority ----

    private static int Rank(FormationRole role, bool inTransit = false) => RearmPriority.RankOf(role, inTransit);

    private static void RearmOrderFollowsAllocationRole()
    {
        // The plan's table: MainEffort, Support, Reserve, Hold, CapitalGuard, DepotBuilder, Recon, Garrison, unassigned, then Recover.
        var roles = new FormationRole?[]
        {
            FormationRole.Recover, null, FormationRole.Garrison, FormationRole.Recon, FormationRole.DepotBuilder, FormationRole.CapitalGuard,
            FormationRole.Hold, FormationRole.Reserve, FormationRole.Support, FormationRole.MainEffort
        };
        var ranks = new int[roles.Length];
        for (int i = 0; i < roles.Length; i++) ranks[i] = RearmPriority.RankOf(roles[i], false);
        int[] order = RearmPriority.StableOrder(ranks);
        AssertEqual("9,8,7,6,5,4,3,2,1,0", string.Join(",", order), "main, support, reserve, hold, capital guard, depot builder, recon, garrison, unassigned, recover");
        AssertTrue(Rank(FormationRole.MainEffort) < Rank(FormationRole.Support) && Rank(FormationRole.Support) < Rank(FormationRole.Reserve)
            && Rank(FormationRole.Reserve) < Rank(FormationRole.Hold) && Rank(FormationRole.Hold) < Rank(FormationRole.CapitalGuard)
            && Rank(FormationRole.CapitalGuard) < Rank(FormationRole.DepotBuilder) && Rank(FormationRole.DepotBuilder) < Rank(FormationRole.Recon)
            && Rank(FormationRole.Recon) < Rank(FormationRole.Garrison), "the ranks rise in the plan's order");
    }

    private static void RearmOrderStableWithinRole()
    {
        int[] order = RearmPriority.StableOrder(new[] { 1, 0, 1, 0, 0, 1 });
        AssertEqual("1,3,4,0,2,5", string.Join(",", order), "vanilla's order is kept inside a rank");
        AssertEqual("0,1,2", string.Join(",", RearmPriority.StableOrder(new[] { 4, 4, 4 })), "one rank leaves the list as it is");
        AssertEqual(0, RearmPriority.StableOrder(new int[0]).Length, "an empty list");
        var list = new List<string> { "a", "b", "c", "d", "e" };
        RearmPriority.Reorder(list, new[] { 2, 0, 2, 0, 1 });
        AssertEqual("b,d,e,a,c", string.Join(",", list), "Reorder applies the stable order to the list in place");
    }

    private static void RecoverAndTransitRearmLast()
    {
        AssertEqual(RearmPriority.LastRank, Rank(FormationRole.Recover), "a Recover formation is re-armed last");
        AssertEqual(RearmPriority.LastRank, Rank(FormationRole.MainEffort, inTransit: true), "so is a main-effort formation in transit");
        AssertEqual(RearmPriority.LastRank, RearmPriority.RankOf(null, true), "and an unassigned one in transit");
        AssertTrue(Rank(FormationRole.Garrison) < RearmPriority.LastRank, "every other role comes first");
    }

    private static void UnassignedUnitsAfterAssignedRoles()
    {
        int unassigned = RearmPriority.RankOf(null, false);
        AssertEqual(RearmPriority.UnassignedRank, unassigned, "no role is the unassigned rank");
        foreach (FormationRole role in Enum.GetValues(typeof(FormationRole)))
        {
            if (role == FormationRole.Recover) continue;
            AssertTrue(Rank(role) < unassigned, role + " is re-armed before a unit the allocation does not name");
        }
        AssertTrue(unassigned < RearmPriority.LastRank, "an unassigned unit still comes before Recover and in-transit units");
    }

    private static void RearmRestoresUnitListOrder()
    {
        var list = new List<string> { "u0", "u1", "u2", "u3", "u4", "u5" };
        var sameObject = list;
        var original = list.ToArray();
        var saved = RearmPriority.Reorder(list, new[] { 3, 0, 3, 1, 0, 2 });
        AssertEqual("u1,u4,u3,u5,u0,u2", string.Join(",", list), "reordered by rank");
        AssertEqual(string.Join(",", original), string.Join(",", saved), "the saved copy is vanilla's order");
        AssertTrue(RearmPriority.Restore(list, saved), "restored");
        AssertEqual(string.Join(",", original), string.Join(",", list), "back to vanilla's order");
        AssertTrue(ReferenceEquals(sameObject, list), "the list object is the one vanilla and other patches hold");
        AssertEqual(6, saved.Length, "the saved copy is its own array");
        saved[0] = "x";
        AssertEqual("u0", list[0], "editing the saved copy does not touch the list");
    }

    private static void RearmRestoreKeepsVanillaOrderWhenTheListChangedLength()
    {
        // A list that gained a unit: vanilla's order for the units it had, the newcomer at the end.
        var grown = new List<string> { "a", "b", "c", "d" };
        var savedGrown = RearmPriority.Reorder(grown, new[] { 1, 0, 1, 0 });
        AssertEqual("b,d,a,c", string.Join(",", grown), "fixture: reordered");
        grown.Add("late");
        AssertFalse(RearmPriority.Restore(grown, savedGrown), "the list is not the one that was saved: reported");
        AssertEqual("a,b,c,d,late", string.Join(",", grown), "vanilla's order for the units still present, the newcomer at the end");

        // Two newcomers keep their current relative order, wherever they stand.
        var twice = new List<string> { "a", "b", "c" };
        var savedTwice = RearmPriority.Reorder(twice, new[] { 2, 1, 0 });
        twice.Insert(1, "n1");
        twice.Add("n2");
        RearmPriority.Restore(twice, savedTwice);
        AssertEqual("a,b,c,n1,n2", string.Join(",", twice), "newcomers keep their relative order after the saved units");

        // A list that lost a unit: vanilla's order without it.
        var shrunk = new List<string> { "a", "b", "c", "d" };
        var savedShrunk = RearmPriority.Reorder(shrunk, new[] { 1, 0, 1, 0 });
        shrunk.Remove("d");
        AssertFalse(RearmPriority.Restore(shrunk, savedShrunk), "reported");
        AssertEqual("a,b,c", string.Join(",", shrunk), "vanilla's order for the units that remain");

        // One lost and one gained (same length): not the saved list either.
        var swapped = new List<string> { "a", "b", "c" };
        var savedSwapped = RearmPriority.Reorder(swapped, new[] { 1, 0, 1 });
        swapped.Remove("a");
        swapped.Add("late");
        AssertFalse(RearmPriority.Restore(swapped, savedSwapped), "an equal length is not an equal list");
        AssertEqual("b,c,late", string.Join(",", swapped), "the lost unit is not resurrected, the gained one is kept");

        // Empty entries and the same list object.
        var withNull = new List<string> { "a", null, "c" };
        var sameObject = withNull;
        var savedNull = RearmPriority.Reorder(withNull, new[] { 1, 0, 1 });
        withNull.Add("late");
        RearmPriority.Restore(withNull, savedNull);
        AssertEqual("a,,c,late", string.Join(",", withNull), "a null entry is kept in place");
        AssertTrue(ReferenceEquals(sameObject, withNull), "the same list object throughout");
        AssertThrows(() => RearmPriority.Reorder(new List<string> { "a" }, new[] { 0, 1 }), "ranks must match the list");
    }

    private static void RearmLeavesInitializationPassToVanilla()
    {
        AssertTrue(RearmPriority.ShouldReorder(true, 1, 0, true, false, false, false, true), "baseline: a fresh plan, the daily pass");
        AssertFalse(RearmPriority.ShouldReorder(true, 1, 0, true, false, true, false, true), "the new-campaign pass shuffles and equips every unit: vanilla's");
        AssertFalse(RearmPriority.ShouldReorder(false, 1, 0, true, false, false, false, true), "the flag off is vanilla's order");
    }

    private static void RearmStandsDownWithStaleOrMissingPlan()
    {
        AssertFalse(RearmPriority.ShouldReorder(true, 1, 0, true, false, false, false, false), "no fresh plan: vanilla's order");
        // The freshness rule itself, on a published world: today's plan is read, one older than two review days is not, none is not.
        var w = ExecutorFixtures.PublishedWorld(out _);
        AssertTrue(CampaignWithholdView.TryFreshAllocation(0, out var allocation) && allocation != null, "today's published plan is read");
        AssertTrue(allocation.TryGet(w.Main, out _), "...with its assignments");
        PlanFreshness.NoteToday(ExecutorFixtures.Day + PlanFreshness.MaxAgeDays);
        AssertTrue(CampaignWithholdView.TryFreshAllocation(0, out _), "two days old is still read");
        PlanFreshness.NoteToday(ExecutorFixtures.Day + PlanFreshness.MaxAgeDays + 1);
        AssertFalse(CampaignWithholdView.TryFreshAllocation(0, out var stale) || stale != null, "three days old is stale");
        AssertFalse(CampaignWithholdView.TryFreshAllocation(1, out _), "the other alliance published nothing");
        AssertFalse(CampaignWithholdView.TryFreshAllocation(2, out _), "alliances 0 and 1 only");
        AssertFalse(CampaignWithholdView.TryFreshAllocation(-1, out _), "a negative alliance");
        PlanFreshness.NoteToday(ExecutorFixtures.Day);
        CampaignPlanStore.PublishExecutorContext(0, new ExecutorContext { Released = false, ExecutorsAllowed = false });
        AssertFalse(CampaignWithholdView.TryFreshAllocation(0, out _), "a pre-war scenario (executors not allowed) is vanilla's");
        CampaignScope.ResetAll();
    }

    private static void RearmStandsDownWhileJobMachinePaused()
    {
        AssertFalse(RearmPriority.ShouldReorder(true, 1, 0, true, false, false, true, true), "vanilla's job machine is paused (11368): the planner's picture is bookkeeping");
        AssertFalse(RearmPriority.ShouldReorder(true, 0, 0, true, true, false, false, true), "the W&L commander-in-chief's own alliance: the planner stands down there");
        AssertFalse(RearmPriority.ShouldReorder(true, 0, 0, false, false, false, false, true), "the player's alliance outside ai_vs_ai is the player's");
    }

    private static void RearmSmallestContainingGroupWins()
    {
        var corps = new object();
        var division = new object();
        var brigade = new object();
        var claims = new RankClaims<object>();
        claims.Claim(corps, Rank(FormationRole.Reserve), 30);
        claims.Claim(brigade, Rank(FormationRole.Reserve), 30);
        claims.Claim(division, Rank(FormationRole.Reserve), 30);
        // A division the allocation seats as main effort inside a corps it holds in reserve: its units claim the better rank.
        claims.Claim(brigade, Rank(FormationRole.MainEffort), 8);
        claims.Claim(brigade, Rank(FormationRole.Recover), 20);
        AssertEqual(Rank(FormationRole.MainEffort), claims.RankOf(brigade, RearmPriority.UnassignedRank), "the smallest group containing the unit names its rank");
        AssertEqual(Rank(FormationRole.Reserve), claims.RankOf(corps, RearmPriority.UnassignedRank), "a unit only the larger group contains takes the larger group's");
        AssertEqual(RearmPriority.UnassignedRank, claims.RankOf(new object(), RearmPriority.UnassignedRank), "no claim: the fallback");
        claims.Claim(division, Rank(FormationRole.Support), 30);
        AssertEqual(Rank(FormationRole.Reserve), claims.RankOf(division, RearmPriority.UnassignedRank), "two groups of one size: the first claim stands");
        claims.Clear();
        AssertEqual(RearmPriority.UnassignedRank, claims.RankOf(brigade, RearmPriority.UnassignedRank), "cleared between calls");
    }

    private static void RearmRowCountsUnitsByRank()
    {
        var counts = new int[RearmPriority.LastRank + 1];
        counts[Rank(FormationRole.MainEffort)] = 3;
        counts[Rank(FormationRole.Support)] = 4;
        counts[Rank(FormationRole.Reserve)] = 1;
        counts[Rank(FormationRole.Hold)] = 10;
        counts[Rank(FormationRole.CapitalGuard)] = 2;
        counts[Rank(FormationRole.DepotBuilder)] = 1;
        counts[Rank(FormationRole.Recon)] = 2;
        counts[Rank(FormationRole.Garrison)] = 3;
        counts[RearmPriority.UnassignedRank] = 5;
        counts[RearmPriority.LastRank] = 7;
        AssertEqual("[Ordnance] alliance=1 rearm main=3 support=4 reserve=1 hold=10 capital=2 other=11 last=7", RearmPriority.Row(1, counts),
            "the plan's row; depot builders, scouts, garrisons and unassigned units are one 'other'");
    }

    private static void SteeringGateIsTheRailRule()
    {
        for (int alliance = -1; alliance <= 3; alliance++)
            for (int mask = 0; mask < 32; mask++)
            {
                bool enabled = (mask & 1) != 0, aiVsAi = (mask & 2) != 0, cic = (mask & 4) != 0, paused = (mask & 8) != 0, fresh = (mask & 16) != 0;
                AssertEqual(SteeringGate.ShouldSteer(enabled, alliance, 0, aiVsAi, cic, paused, fresh),
                    RailLineSteering.ShouldSteer(enabled, alliance, 0, aiVsAi, cic, paused, fresh), "one stand-down rule for the planner's alliance-level steering");
            }
        AssertTrue(Src("Strategic/Planner/RailLineSteering.cs").Contains("SteeringGate.ShouldSteer("), "the rail rule delegates to the shared one");
    }

    // ---- the purchase pick ----

    private static OrdnanceCandidate Cand(int id, float wsf = 2f, float bv = 1000f, float cost = 10f, float years = 0.5f, float std = 0.5f,
                                          bool researched = true, bool imported = false, float pm = 1f, bool open = false)
    {
        return new OrdnanceCandidate
        {
            Id = id, Researched = researched, Imported = imported, ProductionMultiplier = pm, OpenOrder = open,
            Wsf = wsf, BattleValue = bv, UnitCost = cost, DeliveryYears = years, Standardization = std
        };
    }

    private const float OwnAverage = 1f;

    private static OrdnanceChoice Pick(float enemy, int vanilla, params OrdnanceCandidate[] lane)
    {
        return OrdnancePicker.Pick(lane, OwnAverage, vanilla, enemy);
    }

    // Vanilla's GetWeaponStrengthFactor (16214-16222) for a rifle lane model.
    private static float VanillaWsf(float range, float rpm, float firepower0, float accuracy)
    {
        return range * (float)Math.Pow(rpm, 0.2) * (firepower0 / 2f) * (float)Math.Pow(accuracy, 3) / 1000f;
    }


    private static void PurchasePicksOnlyFromVanillaCandidatesThatAreOrderable()
    {
        var best = Cand(1, wsf: 0.9f, bv: 5000f, cost: 1f, years: 0f, std: 1f);   // would win every term, but is not above the own average
        var open = Cand(2, bv: 5000f, cost: 1f, years: 0f, std: 1f, open: true);   // an open order: vanilla excludes it
        var unknown = Cand(3, bv: 5000f, cost: 1f, years: 0f, std: 1f, researched: false);   // neither researched nor imported
        var ok = Cand(4, bv: 800f, cost: 12f, years: 0.9f, std: 0.2f);
        var lesser = Cand(5, bv: 700f, cost: 14f, years: 1.0f, std: 0.1f);
        var choice = Pick(0f, 5, best, open, unknown, ok, lesser);
        AssertEqual(4, choice.Id, "only vanilla's own candidates are considered");
        AssertTrue(OrdnancePicker.InVanillaSet(ok, OwnAverage) && !OrdnancePicker.InVanillaSet(best, OwnAverage) && !OrdnancePicker.InVanillaSet(open, OwnAverage)
            && !OrdnancePicker.InVanillaSet(unknown, OwnAverage), "vanilla's test: researched or imported, no open order, WSF above the own lane average");
        AssertFalse(OrdnancePicker.InVanillaSet(Cand(6, wsf: OwnAverage), OwnAverage), "equal to the average is not above it (16298, 16315)");
        AssertTrue(OrdnancePicker.InVanillaSet(Cand(7, researched: false, imported: true), OwnAverage), "an import counts as vanilla's does (16296)");
    }

    private static void ResearchedUnproducedModelNeverPicked()
    {
        // The Gatling (37), the Coffee Mill (38) and the Williams gun (12): researched by projects 10/11 at productionmultiplier 0.
        var gatling = Cand(37, bv: 9000f, cost: 5f, years: 0f, std: 1f, pm: 0f);
        var mill = Cand(38, bv: 8000f, cost: 5f, years: 0f, std: 1f, pm: 0f);
        var williams = Cand(12, bv: 7000f, cost: 5f, years: 0f, std: 1f, pm: 0f);
        var musket = Cand(20, bv: 600f, cost: 20f, years: 1.2f, std: 0.1f);
        var choice = Pick(0f, 37, gatling, mill, williams, musket);
        AssertEqual(20, choice.Id, "never a researched model the order screen refuses, even when vanilla picked it and it scores best");
        AssertFalse(OrdnancePicker.NeedsPriceRead(gatling, OwnAverage), "no price is read for it");
        AssertTrue(OrdnancePicker.NeedsPriceRead(musket, OwnAverage), "one is read for an orderable candidate");
        var imported = Cand(12, pm: 0f, imported: true);
        AssertTrue(OrdnancePicker.NeedsPriceRead(imported, OwnAverage), "a model with no production but an import line is orderable (the screen's rule)");
        AssertEqual(12, Pick(0f, 12, imported, Cand(20, bv: 1f, cost: 100f, years: 2f, std: 0f)).Id, "and can be picked");
    }

    private static void PurchaseLeavesVanillaPickWhenNoOrderableCandidate()
    {
        var choice = Pick(0f, 37, Cand(37, pm: 0f), Cand(38, pm: 0f), Cand(40, open: true));
        AssertEqual(37, choice.Id, "nothing to choose from: vanilla's pick stands (without a skip G9 cannot cancel an order)");
        AssertEqual(PickReason.VanillaKept, choice.Reason, "and says so");
        AssertEqual(37, OrdnancePicker.Pick(new List<OrdnanceCandidate>(), OwnAverage, 37, 0f).Id, "an empty lane");
        AssertEqual(37, OrdnancePicker.Pick(null, OwnAverage, 37, 0f).Id, "a missing lane");
    }

    private static void PurchasePrefersValuePerCost()
    {
        var cheap = Cand(3, cost: 10f);
        var dear = Cand(2, cost: 25f);
        AssertEqual(3, Pick(0f, 2, dear, cheap).Id, "equal in every other term, the cheaper model wins");
        AssertEqual(PickReason.Value, Pick(0f, 2, dear, cheap).Reason, "for its value per cost");
        var strongButDear = Cand(2, bv: 1200f, cost: 60f);
        var plain = Cand(3, bv: 1000f, cost: 10f);
        AssertEqual(3, Pick(0f, 2, strongButDear, plain).Id, "a slightly better gun at six times the price loses");
    }

    private static void PurchaseRateOfFireCountsInFull()
    {
        // A repeater with the lower WSF (vanilla's rate-of-fire power is 0.2) out-fires a rifle-musket in a battle, where reload is 1/rpm.
        float musketWsf = VanillaWsf(300f, 2.5f, 3.2f, 0.88f);
        float repeaterWsf = VanillaWsf(150f, 7f, 3f, 0.9f);
        float musketBv = OrdnancePicker.BattleValue(300f, 2.5f, 3.2f, 0.88f, false, 0f);
        float repeaterBv = OrdnancePicker.BattleValue(150f, 7f, 3f, 0.9f, false, 0f);
        AssertTrue(repeaterWsf < musketWsf, "fixture: vanilla ranks the rifle-musket above the repeater (" + repeaterWsf + " < " + musketWsf + ")");
        AssertTrue(repeaterBv > musketBv, "the battle value counts rate of fire in full (" + repeaterBv + " > " + musketBv + ")");
        AssertNear(300f * 2.5f * 1.6f * (float)Math.Pow(0.88, 3) / 1000f, musketBv, 1e-3f, "range x rpm x firepower/2 x accuracy^3, on WSF's scale");
        var musket = Cand(1, wsf: musketWsf, bv: musketBv);
        var repeater = Cand(2, wsf: repeaterWsf, bv: repeaterBv);
        AssertEqual(2, OrdnancePicker.Pick(new[] { musket, repeater }, 0.1f, 1, 0f).Id, "at equal cost, delivery and standardization the repeater is bought");
        // Guns keep vanilla's tube-weight divisor (16219).
        float tube = 500f;
        float divisor = (float)Math.Pow(tube, 0.2) / 10f * 100f;
        AssertNear(OrdnancePicker.BattleValue(2000f, 2f, 10f, 0.9f, false, tube) / divisor, OrdnancePicker.BattleValue(2000f, 2f, 10f, 0.9f, true, tube), 1e-4f, "an artillery piece divides by the tube-weight term");
        AssertEqual(0f, OrdnancePicker.BattleValue(2000f, 2f, 10f, 0.9f, true, 0f), "a degenerate tube weight scores 0, never infinity");
        AssertEqual(0f, OrdnancePicker.BattleValue(float.NaN, 2f, 10f, 0.9f, false, 0f), "a degenerate stat scores 0");
    }

    private static void PurchasePenalizesSlowDelivery()
    {
        var fast = Cand(3, years: 0.1f);
        var slow = Cand(2, years: 1.9f);
        var choice = Pick(0f, 2, slow, fast);
        AssertEqual(3, choice.Id, "equal in every other term, the model that arrives sooner wins");
        AssertEqual(PickReason.Delivery, choice.Reason, "for its delivery time");
        AssertEqual(3, Pick(0f, 2, Cand(2, years: 2f), Cand(3, years: 1.99f)).Id, "the two-year cap (GetDeliveryTime 64000) is the floor of the term");
    }

    private static void PurchasePrefersStandardizedModel()
    {
        var standard = Cand(3, std: 0.9f);
        var odd = Cand(2, std: 0.05f);
        var choice = Pick(0f, 2, odd, standard);
        AssertEqual(3, choice.Id, "equal in every other term, the model the army already carries wins");
        AssertEqual(PickReason.Standardization, choice.Reason, "for its standardization");
    }




    private static void PurchaseDeterministicOnTies()
    {
        var a = Cand(9);
        var b = Cand(4);
        var c = Cand(7);
        AssertEqual(4, Pick(0f, 9, a, b, c).Id, "equal scores: the lower model id");
        AssertEqual(4, Pick(0f, 9, c, b, a).Id, "in any input order");
        AssertEqual(4, Pick(0f, 9, b, a, c).Id, "(again)");
        var again = Pick(0f, 9, a, b, c);
        AssertEqual(Pick(0f, 9, c, a, b).Score, again.Score, "and the same score");
    }

    private static void PurchaseScoreStaysInUnitRange()
    {
        AssertNear(1f, OrdnancePicker.ValuePerCostWeight + OrdnancePicker.DeliveryWeight + OrdnancePicker.BattleValueWeight
            + OrdnancePicker.StandardizationWeight + OrdnancePicker.EnemyEdgeWeight, 1e-6f, "the five weights sum to one");
        AssertEqual(0.35f, OrdnancePicker.ValuePerCostWeight, "value per cost");
        AssertEqual(0.20f, OrdnancePicker.DeliveryWeight, "delivery");
        AssertEqual(0.20f, OrdnancePicker.BattleValueWeight, "battle value");
        AssertEqual(0.15f, OrdnancePicker.StandardizationWeight, "standardization");
        AssertEqual(0.10f, OrdnancePicker.EnemyEdgeWeight, "enemy edge");
        var best = Pick(0.5f, 1, Cand(1, bv: 2000f, cost: 10f, years: 0f, std: 1f, wsf: 3f), Cand(2, bv: 1000f, cost: 10f, years: 0f, std: 1f));
        AssertNear(1f, best.Score, 1e-5f, "a model that is best in every term scores one");
        var worst = Pick(5f, 1, Cand(1, bv: 1f, cost: 1000f, years: 2f, std: 0f));
        AssertTrue(worst.Score >= 0f && worst.Score <= 1f, "a lone candidate stays in range: " + worst.Score);
        foreach (float bad in new[] { 0f, -5f, float.NaN })
        {
            var guarded = Pick(0f, 1, Cand(1, cost: bad), Cand(2, cost: 10f));
            AssertTrue(guarded.Score >= 0f && guarded.Score <= 1f, "an unreadable cost (" + bad + ") never leaves the range");
        }
        AssertEqual(2, Pick(0f, 1, Cand(1, cost: 0f), Cand(2, cost: 10f)).Id, "a model with no readable price earns no value-per-cost credit");
        AssertTrue(Pick(0f, 1, Cand(1, years: 7f), Cand(2, years: -3f)).Score <= 1f, "delivery outside [0, 2] is clamped");
    }

    private static void PurchaseRowNamesTheChoice()
    {
        var choice = new OrdnanceChoice { Id = 14, Score = 0.7134f, Reason = PickReason.Value, UnitCost = 105.24f, DeliveryYears = 0.836f };
        AssertEqual("[OrdnancePurchase] alliance=1 lane=rifles vanilla=12 picked=14 score=0.713 cost=105.2 delivery=0.84 reason=value",
            OrdnancePicker.Row(1, false, 12, choice), "the plan's row");
        AssertEqual("[OrdnancePurchase] alliance=0 lane=guns vanilla=40 picked=40 score=0.000 cost=0.0 delivery=0.00 reason=vanilla-kept",
            OrdnancePicker.Row(0, true, 40, new OrdnanceChoice { Id = 40, Reason = PickReason.VanillaKept }), "a pick left to vanilla");
        AssertEqual("delivery", OrdnancePicker.ReasonName(PickReason.Delivery), "reason names");
        AssertEqual("std", OrdnancePicker.ReasonName(PickReason.Standardization), "std");
        AssertEqual("edge", OrdnancePicker.ReasonName(PickReason.Edge), "edge");
        // The reason is the term group that put the winner ahead of the runner-up by the most.
        AssertEqual(PickReason.Value, Pick(0f, 2, Cand(5, cost: 10f), Cand(3, cost: 40f)).Reason, "value when the value terms decide");
        AssertEqual(PickReason.Value, Pick(0f, 2, Cand(5), Cand(3)).Reason, "a dead heat is credited to value");
        AssertEqual(PickReason.Value, Pick(0f, 2, Cand(5)).Reason, "so is a lone candidate");
        AssertEqual(PickReason.Delivery, Pick(0f, 2, Cand(5, years: 0.1f, std: 0.45f), Cand(3, years: 1.5f)).Reason, "delivery when it decides, however small the other terms' lead");
    }

    // ---- fog: what a sighting records and what a belief carries ----


    private static void BeliefCarriesSightedQualityAndKeepsItWhileUnseen()
    {
        var model = new EnemyArmyModel();
        int day = 100;
        var seen = new[] { new EnemyDetection { InstanceId = 1, UnitKey = "Hood:3", UnitTyp = 15, X = 10f, Z = 10f, Strength = 9000f, RifleQuality = 1.75f, GunQuality = 0f } };
        model.Update(seen, day, t => 0f, t => 50f);
        AssertNear(1.75f, model.Beliefs[0].RifleQuality, 1e-6f, "the belief takes the sighting's rifle quality");
        AssertEqual(0f, model.Beliefs[0].GunQuality, "and its gun quality");
        model.Update(null, day + 3, t => 0f, t => 50f);
        AssertEqual(1, model.Beliefs.Count, "fixture: the belief is remembered");
        AssertNear(1.75f, model.Beliefs[0].RifleQuality, 1e-6f, "never refreshed while unseen");
        var again = new[] { new EnemyDetection { InstanceId = 1, UnitKey = "Hood:3", UnitTyp = 15, X = 12f, Z = 10f, Strength = 9000f, RifleQuality = 0f, GunQuality = 3.5f } };
        model.Update(again, day + 4, t => 0f, t => 50f);
        AssertNear(1.75f, model.Beliefs[0].RifleQuality, 1e-6f, "a sighting that showed no rifles leaves the lane as it was");
        AssertNear(3.5f, model.Beliefs[0].GunQuality, 1e-6f, "a lane it did show is refreshed");
        var better = new[] { new EnemyDetection { InstanceId = 1, UnitKey = "Hood:3", UnitTyp = 15, X = 12f, Z = 10f, Strength = 9000f, RifleQuality = 2.5f, GunQuality = 3.5f } };
        model.Update(better, day + 5, t => 0f, t => 50f);
        AssertNear(2.5f, model.Beliefs[0].RifleQuality, 1e-6f, "the enemy re-armed: the newer sighting replaces the older reading");
        string codec = Src("Strategic/SidecarDtoCodec.cs");
        AssertEqual(2, CountOccurrences(codec, "RifleQuality = b.RifleQuality"), "the sidecar writes and reads the rifle quality");
        AssertEqual(2, CountOccurrences(codec, "GunQuality = b.GunQuality"), "and the gun quality");
    }

    private static void BelievedQualityIgnoresLanesNoSightingShowed()
    {
        var beliefs = new List<EnemyBelief>
        {
            new EnemyBelief { RifleQuality = 2.0f, Confidence01 = 1.0f },
            new EnemyBelief { RifleQuality = 1.0f, Confidence01 = 0.5f },
            new EnemyBelief { RifleQuality = 0f, Confidence01 = 1.0f, GunQuality = 5f },   // a garrison or cavalry group: no rifle reading
            new EnemyBelief { RifleQuality = 4.0f, Confidence01 = 0f }                      // no confidence left
        };
        AssertNear((2.0f * 1.0f + 1.0f * 0.5f) / 1.5f, BelievedQuality.Of(beliefs, false), 1e-5f, "the confidence-weighted mean over beliefs with a reading in the lane");
        AssertNear(5f, BelievedQuality.Of(beliefs, true), 1e-5f, "the gun lane has its own");
        AssertEqual(0f, BelievedQuality.Of(new[] { new EnemyBelief { RifleQuality = 0f, Confidence01 = 1f } }, false), "no reading: no edge, not a mean dragged to zero");
        AssertEqual(0f, BelievedQuality.Of(new[] { new EnemyBelief { RifleQuality = float.NaN, Confidence01 = 1f } }, false), "an unreadable reading is no reading");
    }


    // ---- runtime pins ----

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







    private static void OrdnancePureFilesHoldNoUnityTypes()
    {
        foreach (var file in new[] { "RearmPriority", "OrdnancePicker", "EnemyWeaponQuality", "SteeringGate" })
        {
            string src = Src("Strategic/Planner/" + file + ".cs");
            AssertFalse(src.Contains("using UnityEngine") || src.Contains("UnityEngine."), file + ".cs is pure: no Unity type");
            AssertFalse(src.Contains("EraStage"), file + ".cs reads no era stage (plan decision 3)");
        }
    }
}
