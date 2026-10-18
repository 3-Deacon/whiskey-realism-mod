using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Util;

using static TestHarness;
using static TestFixtures;

// Stage G strategic foresight, slice F2 (D11 requirement-interception, decision 1): patch #25 stops filtering vanilla's defensive
// operations while the planner runs for the alliance and only reports what it would have removed. The gate is pure
// (DefenseInterceptionGate), the Prefix and Postfix are pinned by source, and [Reserves] gains the two counts the criterion reads
// (enemyOnOwnSoil, vanillaDefActive). Plan: docs/superpowers/plans/2026-10-18-strategic-foresight.md F2; cases TC-INT-* in
// docs/loop/foresight/test-plan.md.
internal static class DefenseInterceptionTests
{
    private const string PatchPath = "src/WhiskeyRealism/Patches/CheckForDefensiveOperationsCandidateFilterPatch.cs";

    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("ObservesWhileThePlannerRunsForTheAlliance", ObservesWhileThePlannerRunsForTheAlliance),
            ("FiltersWhenTheFlagIsOff", FiltersWhenTheFlagIsOff),
            ("FiltersWhenThePlanIsStale", FiltersWhenThePlanIsStale),
            ("FiltersForEurope", FiltersForEurope),
            ("FiltersForACicSide", FiltersForACicSide),
            ("FiltersWhenThePlannerOrItsExecutorsAreOff", FiltersWhenThePlannerOrItsExecutorsAreOff),
            ("FilterRowNamesWhy", FilterRowNamesWhy),
            ("ObservedRowCountsWhatWouldBeRemoved", ObservedRowCountsWhatWouldBeRemoved),
            ("ObservedRowEmittedWithNothingToRemove", ObservedRowEmittedWithNothingToRemove),
            ("ObservedRowNotGatedOnVerbose", ObservedRowNotGatedOnVerbose),
            ("ObservedRowDedupesPerCampaign", ObservedRowDedupesPerCampaign),
            ("TheFingerprintFollowsTheReasonsNotTheOrder", TheFingerprintFollowsTheReasonsNotTheOrder),
            ("ObservedDedupePathAllocatesNothing", ObservedDedupePathAllocatesNothing),
            ("ObservedLogIgnoresAllianceTwo", ObservedLogIgnoresAllianceTwo),
            ("TheDefenceAvailabilityClauseWithholdsWhatWhiskeyMayNotMove", TheDefenceAvailabilityClauseWithholdsWhatWhiskeyMayNotMove),
            ("TheAvailabilityPatchesReadTheChainOfTheUnit", TheAvailabilityPatchesReadTheChainOfTheUnit),
            ("ObservingPrefixRemovesNothingAndPostfixRevertsNothing", ObservingPrefixRemovesNothingAndPostfixRevertsNothing),
            ("TheFlagIsBoundOnAndReadByThePatch", TheFlagIsBoundOnAndReadByThePatch),
            ("ReservesRowCountsEnemiesOnOwnSoilAndVanillasActiveDefence", ReservesRowCountsEnemiesOnOwnSoilAndVanillasActiveDefence),
        });
    }

    // ---- the gate ----

    // alliance 1, every flag on, a plan published and fresh, an allocation with assignments.
    private static DefenseFilterMode Mode(out string why, int alliance = 1, bool interception = true, bool planner = true, bool executors = true,
                                          bool published = true, bool fresh = true, int assignments = 12)
    {
        return DefenseInterceptionGate.ModeFor(alliance, interception, planner, executors, published, fresh, assignments, out why);
    }

    private static void ObservesWhileThePlannerRunsForTheAlliance()
    {
        foreach (int alliance in new[] { 0, 1 })
        {
            AssertEqual(DefenseFilterMode.Observe, Mode(out string why, alliance: alliance), "alliance " + alliance + ": the planner runs for it, so vanilla's defence is left whole");
            AssertEqual("", why, "an observed call has no filtering cause");
        }
    }

    private static void FiltersWhenTheFlagIsOff()
    {
        AssertEqual(DefenseFilterMode.Filter, Mode(out string why, interception: false), "the rollback restores the legacy enforcement");
        AssertEqual("flag", why, "named for the flag");
    }

    private static void FiltersWhenThePlanIsStale()
    {
        AssertEqual(DefenseFilterMode.Filter, Mode(out string why, fresh: false), "a published plan that is not fresh (or whose executors may not run yet) is no plan to count on");
        AssertEqual("stale", why, "named stale");
        AssertEqual(DefenseFilterMode.Filter, Mode(out string none, published: false, fresh: false), "no plan published at all");
        AssertEqual("no-plan", none, "named no-plan");
    }

    private static void FiltersForEurope()
    {
        foreach (int alliance in new[] { 2, 3, -1 })
        {
            AssertEqual(DefenseFilterMode.Filter, Mode(out string why, alliance: alliance), "alliance " + alliance + " has no planner");
            AssertEqual("europe", why, "named europe");
        }
    }

    // A CIC side's allocation is empty (ForceAllocator.StandsDown): the planner issues it nothing.
    private static void FiltersForACicSide()
    {
        AssertEqual(DefenseFilterMode.Filter, Mode(out string why, assignments: 0), "an empty allocation is not a planner running for the side");
        AssertEqual("empty", why, "named empty");
    }

    private static void FiltersWhenThePlannerOrItsExecutorsAreOff()
    {
        AssertEqual(DefenseFilterMode.Filter, Mode(out string why, planner: false), "no planner");
        AssertEqual("planner-off", why, "named planner-off");
        AssertEqual(DefenseFilterMode.Filter, Mode(out string why2, executors: false), "no executors: #127 does not gate vanilla's defence either");
        AssertEqual("planner-off", why2, "named planner-off");
    }

    // The cause is the first that applies, in a fixed order (europe, flag, planner-off, no-plan, stale, empty), so a row names one cause.
    private static void FilterRowNamesWhy()
    {
        Mode(out string all, alliance: 2, interception: false, planner: false, executors: false, published: false, fresh: false, assignments: 0);
        AssertEqual("europe", all, "Europe first");
        Mode(out string flag, interception: false, planner: false, published: false, fresh: false, assignments: 0);
        AssertEqual("flag", flag, "then the flag");
        Mode(out string off, planner: false, published: false, fresh: false, assignments: 0);
        AssertEqual("planner-off", off, "then the planner");
        Mode(out string noPlan, published: false, fresh: false, assignments: 0);
        AssertEqual("no-plan", noPlan, "then no plan");
        Mode(out string stale, fresh: false, assignments: 0);
        AssertEqual("stale", stale, "then a stale one");
        Mode(out string empty, assignments: 0);
        AssertEqual("empty", empty, "then an empty allocation");

        string patch = ReadRepoFile(PatchPath);
        string filter = BodyOf(patch, "private static void EmitDefenseIntentFilter(");
        AssertContains(filter, "\"mode\"", "the filter row carries mode=filter:<why>");
        AssertContains(patch, "DefenseInterceptionGate.FilterModeText(why)", "built from the why the gate returned");
        AssertTrue(Regex.IsMatch(DefenseInterceptionGate.FilterModeText("stale"), "^filter:stale$"), "the text is filter:<why>");
    }

    // ---- the observed row ----

    private static void ObservedRowCountsWhatWouldBeRemoved()
    {
        var forbidden = new HashSet<int> { 5, 6, 7, 8 };
        AssertEqual(2, DefenseInterceptionGate.WouldRemove(new List<int> { 1, 5, 2, 7, 3 }, forbidden), "the forbidden ids present in ownunits: what removed= counted");
        AssertEqual(0, DefenseInterceptionGate.WouldRemove(new List<int> { 1, 2, 3 }, forbidden), "none present");
        AssertEqual(0, DefenseInterceptionGate.WouldRemove(new List<int>(), forbidden), "no formations");
        var into = new List<int>();
        AssertEqual(2, DefenseInterceptionGate.WouldRemove(new List<int> { 1, 5, 2, 7, 3 }, forbidden, into), "and the ids it counted can be collected for the reasons");
        AssertEqual("5,7", string.Join(",", into), "in the order they stand");
        AssertEqual("alliance=1|wouldRemove=2|reasons=min-hold:2", DefenseInterceptionGate.ObservedSignature(1, 2, "min-hold:2"), "the signature the dedupe compares");
    }

    // The ledger forbade formations, none of which is in ownunits today: the row still goes out (wouldRemove=0), once.
    private static void ObservedRowEmittedWithNothingToRemove()
    {
        CampaignScope.ResetAll();
        string signature = DefenseInterceptionGate.ObservedSignature(1, 0, "-");
        AssertEqual("alliance=1|wouldRemove=0|reasons=-", signature, "a zero count is a signature");
        AssertTrue(ObservedLog.ShouldEmit(1, 0, DefenseFilterTally.Fingerprint(new Dictionary<int, string>(), new List<int>())), "and it is written the first time");
        string observe = BodyOf(ReadRepoFile(PatchPath), "private static void ObserveCandidates(");
        AssertFalse(observe.Contains("wouldRemove > 0") || observe.Contains("wouldRemove == 0) return") || observe.Contains("if (would.Count == 0)"), "the observe path does not skip a zero count");
        string prefix = BodyOf(ReadRepoFile(PatchPath), "internal static void Prefix(");
        AssertInOrder(prefix, "Prefix", "if (forbidden.Count == 0) return;", "ObserveCandidates(");
    }

    private static void ObservedRowNotGatedOnVerbose()
    {
        string observe = BodyOf(ReadRepoFile(PatchPath), "private static void ObserveCandidates(");
        AssertFalse(observe.Contains("DefenseIntentVerboseLogging") || observe.Contains("verbose"), "the observed branch tests no verbose flag");
        AssertContains(observe, "\"filter-observed\"", "it writes the filter-observed decision");
        AssertContains(observe, "ObservedLog.ShouldEmit(", "bounded by the per-alliance state");
        // The filter row stays verbose-gated, as shipped.
        AssertContains(BodyOf(ReadRepoFile(PatchPath), "internal static void Prefix("), "removed > 0 && verbose", "the filter row is still behind the verbose flag");
    }

    private static void ObservedRowDedupesPerCampaign()
    {
        CampaignScope.ResetAll();
        var reasons = new Dictionary<int, string> { { 1, "min-hold" }, { 2, "min-hold" }, { 3, "player-controlled" } };
        long some = DefenseFilterTally.Fingerprint(reasons, new List<int> { 1, 2 });
        long other = DefenseFilterTally.Fingerprint(reasons, new List<int> { 1, 3 });
        AssertTrue(ObservedLog.ShouldEmit(1, 2, some), "the first row is written");
        AssertFalse(ObservedLog.ShouldEmit(1, 2, some), "the same state is not written again");
        AssertTrue(ObservedLog.ShouldEmit(0, 2, some), "the other alliance has a memory of its own");
        AssertTrue(ObservedLog.ShouldEmit(1, 2, other), "a change of the reasons is written");
        AssertTrue(ObservedLog.ShouldEmit(1, 3, other), "so is a change of the count");
        AssertTrue(ObservedLog.ShouldEmit(1, 2, some), "and back again");
        CampaignScope.ResetAll();
        AssertTrue(ObservedLog.ShouldEmit(1, 2, some), "a new campaign starts afresh: the log registered its own reset with CampaignScope");
        string log = ReadRepoFile("src/WhiskeyRealism/Strategic/DefenseInterceptionGate.cs");
        AssertContains(BodyOf(log, "static ObservedLog()"), "CampaignScope.Register(\"defense-filter-observed-log\"", "registered from the static constructor");
    }

    // The fingerprint is the reasons tally without its text: the same reasons in any order, the same value; different reasons, a different one.
    private static void TheFingerprintFollowsTheReasonsNotTheOrder()
    {
        var reasons = new Dictionary<int, string> { { 1, "min-hold" }, { 2, "player-controlled" }, { 3, "min-hold" }, { 4, "formation-donor" } };
        long a = DefenseFilterTally.Fingerprint(reasons, new List<int> { 1, 2, 3 });
        AssertEqual(a, DefenseFilterTally.Fingerprint(reasons, new List<int> { 3, 1, 2 }), "order does not matter");
        AssertTrue(a != DefenseFilterTally.Fingerprint(reasons, new List<int> { 1, 2, 4 }), "another reason changes it");
        AssertTrue(a != DefenseFilterTally.Fingerprint(reasons, new List<int> { 1, 2 }), "so does a count");
        AssertEqual(0L, DefenseFilterTally.Fingerprint(reasons, new List<int>()), "nothing counted is 0");
        AssertEqual(0L, DefenseFilterTally.Fingerprint(null, new List<int> { 1 }), "no reasons is 0");
    }

    // The observed branch's dedupe path allocates nothing when the state is the one the last row said: the reasons text is built only for a row
    // that is written.
    private static void ObservedDedupePathAllocatesNothing()
    {
        CampaignScope.ResetAll();
        var reasons = new Dictionary<int, string> { { 1, "min-hold" }, { 2, "player-controlled" }, { 3, "min-hold" } };
        var present = new List<int> { 1, 2, 3, 4, 5 };
        var forbidden = new HashSet<int> { 1, 2, 3, 9 };
        var would = new List<int>(8);
        would.Clear();
        int count = DefenseInterceptionGate.WouldRemove(present, forbidden, would);
        AssertTrue(ObservedLog.ShouldEmit(1, count, DefenseFilterTally.Fingerprint(reasons, would)), "the first call writes");
        would.Clear();
        long begin = BeginAllocationWindow();
        for (int round = 0; round < 50; round++)
        {
            would.Clear();
            int n = DefenseInterceptionGate.WouldRemove(present, forbidden, would);
            if (ObservedLog.ShouldEmit(1, n, DefenseFilterTally.Fingerprint(reasons, would))) throw new Exception("the unchanged state was written again");
        }
        AssertEqual(0L, AllocatedSince(begin), "fifty unchanged calls allocate nothing");
        string observe = BodyOf(ReadRepoFile(PatchPath), "private static void ObserveCandidates(");
        AssertTrue(observe.IndexOf("ObservedLog.ShouldEmit(", StringComparison.Ordinal) < observe.IndexOf("DefenseFilterTally.Reasons(", StringComparison.Ordinal),
            "and the patch builds the text only after the compare");
    }

    private static void ObservedLogIgnoresAllianceTwo()
    {
        CampaignScope.ResetAll();
        foreach (int alliance in new[] { 2, 3, -1, 100 })
        {
            AssertFalse(ObservedLog.ShouldEmit(alliance, 1, 7L), "alliance " + alliance + " is never indexed and never written");
            AssertFalse(ObservedLog.ShouldEmit(alliance, 1, 7L), "again");
        }
    }

    // ---- the patch ----

    // In the observing mode the Prefix ends before it takes any snapshot or removes anything, so the Postfix finds no snapshot to revert
    // against and the Finalizer nothing to restore.
    private static void ObservingPrefixRemovesNothingAndPostfixRevertsNothing()
    {
        string patch = ReadRepoFile(PatchPath);
        string prefix = BodyOf(patch, "internal static void Prefix(");
        int observe = prefix.IndexOf("if (mode == DefenseFilterMode.Observe)", StringComparison.Ordinal);
        int returned = prefix.IndexOf("return;", observe, StringComparison.Ordinal);
        AssertTrue(observe > 0 && returned > observe, "the observe branch returns");
        AssertTrue(prefix.IndexOf("ownUnits.RemoveAt(", StringComparison.Ordinal) > returned, "the removal is after the observe return");
        AssertTrue(prefix.IndexOf("_ownUnitsSnapshot[_aifaction] =", StringComparison.Ordinal) > returned, "so is the ownunits snapshot");
        AssertTrue(prefix.IndexOf("_defensiveOpsBefore[_aifaction] =", StringComparison.Ordinal) > returned, "and the defensive-operations snapshot");
        string observed = BodyOf(patch, "private static void ObserveCandidates(");
        foreach (string write in new[] { ".RemoveAt(", ".Remove(", ".Insert(", "_ownUnitsSnapshot", "_defensiveOpsBefore", "SafeMoveUnitTo", "CancelQueuedDefensiveMove", "MoveUnitTo", "RemoveFromStaticDefensiveOperation" })
            AssertFalse(observed.Contains(write), "the observe path never calls " + write);
        foreach (Match add in Regex.Matches(observed, @"(\w+)\.Add\("))
            AssertTrue(add.Groups[1].Value == "PresentIds", "the only list it adds to is its own buffer, never ownunits: " + add.Value);
        // The Postfix only reverts what the Prefix set aside (a snapshot of this call), and the Finalizer clears every snapshot by call.
        string postfix = BodyOf(patch, "internal static void Postfix(");
        AssertContains(postfix, "_defensiveOpsBefore.TryGetValue(_aifaction, out var beforeIds)", "the Postfix reverts only against this call's snapshot");
    }

    private static void TheFlagIsBoundOnAndReadByThePatch()
    {
        string plugin = ReadRepoFile("src/WhiskeyRealism/Plugin.cs");
        AssertTrue(Regex.IsMatch(plugin, "\"Campaign Planner\", \"Enable Vanilla Defensive Interception\", true,"), "bound in the Campaign Planner section, default on");
        AssertContains(ReadRepoFile(PatchPath), "FlagOn(Plugin.EnableVanillaDefensiveInterception)", "the patch reads it");
    }

    // ---- #127 and the player's units (integrator decision 2026-10-18 under decision 1, BUG-WL-024) ----

    // While #25 observes, vanilla's defence chooses from the whole list; the clause that keeps the player's units out of it is #127's:
    // every formation Whiskey may not move is withheld from vanilla's defensive and offensive pools, an AI unit and the group that carries the
    // player below map level are not.
    private static void TheDefenceAvailabilityClauseWithholdsWhatWhiskeyMayNotMove()
    {
        var world = ExecutorFixtures.PublishedWorld(out _);
        int free = world.FreeHold;   // a Hold formation the allocation does not withhold: nothing but the chain can hold it back
        AssertEqual(DefenseFilterMode.Observe, DefenseInterceptionGate.ModeFor(0, true, true, true, true, true, 6, out _), "#25 observes in this world");
        AssertFalse(CampaignWithholdView.WithheldFromDefense(0, free, 560f, 0f), "the control: an AI unit with no chain is not withheld");

        var takeover = new WlStrategicRoleFacts(true, true, isMovedByPlayer: true);   // in his operation, not under his command
        var carrying = new WlStrategicRoleFacts(true, true, isPartOfPlayerUnit: true, isPlayerCampaignGroup: true);
        var underCommand = new WlStrategicRoleFacts(true, true, isUnderCommander: true);
        var mapGroup = new WlStrategicRoleFacts(true, true, isUnderCommander: true, currentCommandIsCampaignGroup: true);
        var cic = new WlStrategicRoleFacts(true, true, isPlayerCic: true);
        var notWl = new WlStrategicRoleFacts(false, false);
        foreach (var (name, facts, withheld) in new[]
        {
            ("a unit in the player's takeover operation", takeover, true), ("a unit under his command", underCommand, true), ("his map group", mapGroup, true),
            ("a CIC side", cic, true), ("the group carrying him below map level", carrying, false), ("an AI unit", notWl, false)
        })
        {
            bool mayMove = WlChainRules.MayMove(WlChainRules.Classify(in facts));
            AssertEqual(withheld, CampaignWithholdView.WithheldFromDefense(0, free, 560f, 0f, mayMove), name + ": withheld from vanilla's defence = " + withheld);
            AssertEqual(withheld, CampaignWithholdView.WithheldFromOffense(0, free, mayMove), name + ": and from its offence");
        }
        // Planner off or no plan: the clause is the planner's, so nothing is withheld and #25 filters as before.
        CampaignScope.ResetAll();
        AssertFalse(CampaignWithholdView.WithheldFromDefense(0, free, 560f, 0f, false), "no published plan, no clause");
    }

    // The patches hand the unit's chain to the view; the clause is not in the view alone.
    private static void TheAvailabilityPatchesReadTheChainOfTheUnit()
    {
        string patch = ReadRepoFile("src/WhiskeyRealism/Patches/CampaignAvailabilityFilterPatch.cs");
        AssertContains(patch, "CampaignWithholdView.WithheldFromOffense(alliance, unit.GetInstanceID(), PlannerChainGate.MayMove(alliance, unit))", "the offensive Postfix passes the chain");
        AssertContains(patch, "enemyposition.x, enemyposition.z, PlannerChainGate.MayMove(alliance, unit))", "and the defensive one");
        string gate = BodyOf(patch, "internal static bool MayMove(int alliance, Regiment unit)");
        AssertContains(gate, "WlChainRules.MayMove(WlStrategicOrderBridge.ChainClassOf(in chain, alliance, unit))", "the chain class is the one every other Whiskey writer asks");
        AssertContains(gate, "if (!DLC_WL.dlc_scenarioactive || GameVars.playeralliance != alliance) return true;", "read only for the player's alliance in a W&L career");
        string view = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignWithholdView.cs");
        AssertContains(BodyOf(view, "public static bool WithheldFromDefense("), "if (!mayMove) return true;", "the view withholds what Whiskey may not move");
        AssertContains(BodyOf(view, "public static bool WithheldFromOffense("), "if (!mayMove) return true;", "from the offensive pool too");
    }

    // ---- [Reserves] ----

    // enemyOnOwnSoil: live land beliefs standing on the side's own soil; vanillaDefActive: own formations in a defensive operation today.
    private static void ReservesRowCountsEnemiesOnOwnSoilAndVanillasActiveDefence()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 10f, 9000f));
        s.Own.Add(ExecutorFixtures.Corps(2, 550f, 7000f));
        var plan = ExecutorFixtures.HandPlan(s, new int[0]);
        var alloc = new Allocation { Alliance = 0 };
        alloc.Assignments.Add(ExecutorFixtures.Assign(1, FormationRole.Hold, 0, 10f, 0f));
        alloc.Assignments.Add(ExecutorFixtures.Assign(2, FormationRole.Hold, 0, 550f, 0f));
        string quiet = ReservesRow.Build(s, ExecutorFixtures.Sectors(s), plan, alloc, ExecutorFixtures.State(), null, null);
        AssertContains(quiet, " enemyOnOwnSoil=0 vanillaDefActive=0 vanillaDefWithheld=0 ", "the two counts sit between dept= and the other defence counts: " + quiet);

        EnemyBelief Soil(float strength, EnemyTraits traits, float confidence = 1f)
        {
            var b = ExecutorFixtures.Belief(300f, 0f, strength);
            b.Traits = traits;
            b.Confidence01 = confidence;
            return b;
        }
        s.Beliefs.Add(Soil(4000f, EnemyTraits.OnOwnSoil));
        s.Beliefs.Add(Soil(3000f, EnemyTraits.OnOwnSoil | EnemyTraits.CavalryMajority));
        s.Beliefs.Add(Soil(2000f, 0));                                        // on neutral or enemy ground
        s.Beliefs.Add(Soil(5000f, EnemyTraits.OnOwnSoil | EnemyTraits.Embarked)); // afloat: not a land army
        s.Beliefs.Add(Soil(5000f, EnemyTraits.OnOwnSoil, confidence: 0f));    // a belief that has faded out of the picture
        s.Own[1] = WithDefence(s.Own[1]);
        string row = ReservesRow.Build(s, ExecutorFixtures.Sectors(s), plan, alloc, ExecutorFixtures.State(), null, null);
        AssertContains(row, " enemyOnOwnSoil=2 vanillaDefActive=1 ", "two live land armies on own soil, one formation in vanilla's defence: " + row);
    }

    private static FormationRecord WithDefence(FormationRecord f)
    {
        f.Flags |= FormationFlags.InDefensiveOps;
        return f;
    }

    private static void AssertInOrder(string body, string label, params string[] anchors)
    {
        int at = -1;
        foreach (string anchor in anchors)
        {
            int next = body.IndexOf(anchor, at + 1, StringComparison.Ordinal);
            AssertTrue(next > at, label + " has \"" + anchor + "\" after the one before it");
            at = next;
        }
    }
}
