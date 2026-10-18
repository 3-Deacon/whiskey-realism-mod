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
            ("ObservedLogIgnoresAllianceTwo", ObservedLogIgnoresAllianceTwo),
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
        AssertTrue(ObservedLog.ShouldEmit(1, signature), "and it is written the first time");
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
        AssertContains(observe, "ObservedLog.ShouldEmit(", "bounded by the per-alliance signature");
        // The filter row stays verbose-gated, as shipped.
        AssertContains(BodyOf(ReadRepoFile(PatchPath), "internal static void Prefix("), "removed > 0 && verbose", "the filter row is still behind the verbose flag");
    }

    private static void ObservedRowDedupesPerCampaign()
    {
        CampaignScope.ResetAll();
        string a = DefenseInterceptionGate.ObservedSignature(1, 8, "min-hold:8");
        AssertTrue(ObservedLog.ShouldEmit(1, a), "the first row is written");
        AssertFalse(ObservedLog.ShouldEmit(1, a), "the same signature is not written again");
        AssertTrue(ObservedLog.ShouldEmit(0, a.Replace("alliance=1", "alliance=0")), "the other alliance has a memory of its own");
        AssertTrue(ObservedLog.ShouldEmit(1, DefenseInterceptionGate.ObservedSignature(1, 7, "min-hold:7")), "a change of the count is written");
        AssertTrue(ObservedLog.ShouldEmit(1, a), "and back again");
        CampaignScope.ResetAll();
        AssertTrue(ObservedLog.ShouldEmit(1, a), "a new campaign starts afresh: the log registered its own reset with CampaignScope");
        string log = ReadRepoFile("src/WhiskeyRealism/Strategic/DefenseInterceptionGate.cs");
        AssertContains(BodyOf(log, "static ObservedLog()"), "CampaignScope.Register(\"defense-filter-observed-log\"", "registered from the static constructor");
    }

    private static void ObservedLogIgnoresAllianceTwo()
    {
        CampaignScope.ResetAll();
        foreach (int alliance in new[] { 2, 3, -1, 100 })
        {
            AssertFalse(ObservedLog.ShouldEmit(alliance, "x"), "alliance " + alliance + " is never indexed and never written");
            AssertFalse(ObservedLog.ShouldEmit(alliance, "x"), "again");
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
