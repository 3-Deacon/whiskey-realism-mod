using System;
using System.Collections.Generic;
using System.IO;
using WhiskeyRealism.Strategic.Planner;

using static TestHarness;
using static TestFixtures;

// Stage G strategic foresight, slice F4b (decision 3, BUG-INTEL-001): vanilla's mark timer reads the concealment array spottingfactor
// (-50, -60) where timefactorspottings (0.1, 0.006 game years) is meant, so a hidden group is marked every time it moves 50 units. The fix
// swaps the right pair into the call (a non-skipping Prefix with a Finalizer restore, patch #134) and gives the AI side's
// mark ledger the same rate: the chance vanilla's many rolls per game day make over a review's interval (MarkRules.IntervalChance).
// Plan: docs/superpowers/plans/2026-10-18-strategic-foresight.md F4b; cases TC-FIX-* in docs/loop/foresight/test-plan.md.
internal static class SpottingMarkTimingTests
{
    private const string PatchPath = "src/WhiskeyRealism/Patches/SpottingMarkTimingFixPatch.cs";
    private const int Today = 100000;
    private const float Day = 1f / 365.25f;

    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("FixedTimingUsesTimeFactorSpottings", FixedTimingUsesTimeFactorSpottings),
            ("FlagOffKeepsVanillasTiming", FlagOffKeepsVanillasTiming),
            ("ATimeFactorPairThatIsNotUsableKeepsVanillasTiming", ATimeFactorPairThatIsNotUsableKeepsVanillasTiming),
            ("BothSidesShareOneFormula", BothSidesShareOneFormula),
            ("ThePatchIsANonSkippingPrefixWithAFinalizerRestore", ThePatchIsANonSkippingPrefixWithAFinalizerRestore),
            ("ThePatchIsGatedOnItsFlagAndFogOnlyAndListedAsSituational", ThePatchIsGatedOnItsFlagAndFogOnlyAndListedAsSituational),
            ("IntervalChanceCompoundsVanillasTicks", IntervalChanceCompoundsVanillasTicks),
            ("IntervalChanceCountsOnlyTicksPastTheGate", IntervalChanceCountsOnlyTicksPastTheGate),
            ("AtTheCapTheIntervalChanceIsTheCappedProduct", AtTheCapTheIntervalChanceIsTheCappedProduct),
            ("NoFramesNoTicksNoMark", NoFramesNoTicksNoMark),
            ("TheGateFractionIsWhereThePathLeavesTheFiftyUnitCircle", TheGateFractionIsWhereThePathLeavesTheFiftyUnitCircle),
            ("TheLedgerMarksOnlyAGroupThatLeftTheCircleDuringTheInterval", TheLedgerMarksOnlyAGroupThatLeftTheCircleDuringTheInterval),
            ("TheLedgerAgesTheMarkFromTheIntervalsStartNotItsEnd", TheLedgerAgesTheMarkFromTheIntervalsStartNotItsEnd),
            ("TheLedgerCountsItsMarksAndTheGroupsItJudged", TheLedgerCountsItsMarksAndTheGroupsItJudged),
            ("TheRuntimeCountsFramesAndTicksForTheInterval", TheRuntimeCountsFramesAndTicksForTheInterval),
            ("TheReportCountsNewMarksPerHiddenGroup", TheReportCountsNewMarksPerHiddenGroup),
            ("TheTimingFlagIsBoundOnAndRead", TheTimingFlagIsBoundOnAndRead),
        });
    }

    // ---- TimingPair ----

    private static readonly float[] Concealment = { -50f, -60f };
    private static readonly float[] TimeFactors = { 0.1f, 0.006f };

    private static void FixedTimingUsesTimeFactorSpottings()
    {
        AssertTrue(MarkRules.TimingPair(true, Concealment, TimeFactors, out float s0, out float s1), "the fix is applied");
        AssertEqual(0.1f, s0, "intelligence 0: a tenth of a game year");
        AssertEqual(0.006f, s1, "intelligence 1: 0.006");
        // The chance then rises over days, not minutes: half a day after a mark it is well under the cap.
        float halfDay = MarkRules.CreationChance(0.5f * Day, 1f, s0, s1, 1f, 1f, 0.9f);
        AssertTrue(halfDay > 0.2f && halfDay < 0.5f, "half a day after a mark: " + halfDay);
        float bugged = MarkRules.CreationChance(0.5f * Day, 1f, Concealment[0], Concealment[1], 1f, 1f, 0.9f);
        AssertEqual(0.9f, bugged, "control: under the bug it sits at the cap");
    }

    private static void FlagOffKeepsVanillasTiming()
    {
        AssertFalse(MarkRules.TimingPair(false, Concealment, TimeFactors, out float s0, out float s1), "the fix is not applied");
        AssertEqual(-50f, s0, "vanilla's spottingfactor[0]");
        AssertEqual(-60f, s1, "and [1]");
    }

    private static void ATimeFactorPairThatIsNotUsableKeepsVanillasTiming()
    {
        foreach (var bad in new float[][] { null, new float[0], new[] { 0.1f }, new[] { 0f, 0.006f }, new[] { 0.1f, -1f }, new[] { float.NaN, 0.006f } })
        {
            AssertFalse(MarkRules.TimingPair(true, Concealment, bad, out float s0, out float s1), "an unusable pair is refused");
            AssertEqual(-50f, s0, "and vanilla's pair stands");
            AssertEqual(-60f, s1, "z");
        }
        AssertTrue(MarkRules.TimingPair(true, null, TimeFactors, out float n0, out float n1), "no concealment array is no crash");
        AssertEqual(0.1f, n0, "the fixed pair still comes from timefactorspottings");
        AssertEqual(0.006f, n1, "both elements");
        AssertFalse(MarkRules.TimingPair(false, null, null, out float z0, out float z1), "nothing at all");
        AssertEqual(0f, z0 + z1, "reads 0");
    }

    // The patch (the player's own map) and the AI side's inputs take the pair from the one function.
    private static void BothSidesShareOneFormula()
    {
        string patch = CodeOnlyOf(ReadRepoFile(PatchPath));
        AssertContains(patch, "MarkRules.TimingPair(", "the patch asks the shared function");
        string runtime = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/EnemyDetectionRuntime.cs");
        string inputs = BodyOf(runtime, "private static MarkInputs ReadMarkInputs(");
        AssertContains(inputs, "MarkRules.TimingPair(", "the AI side's inputs ask it too");
        AssertContains(inputs, "FlagOn(Plugin.FixSpottingMarkTiming)", "with the same flag");
        AssertFalse(CodeOnlyOf(inputs).Contains("GamePrefs.spottingfactor[0]") || CodeOnlyOf(inputs).Contains("timing[0]"), "and reads the concealment array nowhere else");
    }

    private static void ThePatchIsANonSkippingPrefixWithAFinalizerRestore()
    {
        string src = ReadRepoFile(PatchPath);
        string code = CodeOnlyOf(src);
        AssertContains(code, "AccessTools.Method(typeof(SpottedEnemy), \"UpdateMarks\", new[] { typeof(int) })", "UpdateMarks(int), private, decompile 142773");
        AssertContains(code, "internal static void Prefix(out TimingSwap __state)", "a void Prefix with a state: vanilla's body always runs");
        AssertFalse(code.Contains("bool Prefix(") || code.Contains("__runOriginal"), "no skipping Prefix");
        AssertFalse(code.Contains("HarmonyTranspiler") || code.Contains("Transpiler"), "no Transpiler");
        string finalizer = BodyOf(src, "internal static void Finalizer(");
        AssertContains(finalizer, "__state.Active", "the Finalizer restores only what the Prefix swapped");
        AssertContains(finalizer, "spotting[0] = __state.Saved0", "the first element");
        AssertContains(finalizer, "spotting[1] = __state.Saved1", "the second");
        string prefix = BodyOf(src, "internal static void Prefix(");
        AssertContains(prefix, "__state = default(TimingSwap);", "the state is assigned first, so a throw leaves nothing to restore");
        AssertContains(prefix, "MarkRules.TimingPair(true, spotting, GamePrefs.timefactorspottings, out float s0, out float s1)", "the pair to write");
        AssertContains(src, "campaign.patch.spotting-mark-timing.prefix", "scope and first-fire id of the Prefix");
        AssertContains(src, "campaign.patch.spotting-mark-timing.finalizer", "and of the Finalizer");
        AssertEqual(2, src.Split(new[] { "PatchFirstFire.Mark(" }, StringSplitOptions.None).Length - 1, "each entry marks its first fire");
        AssertEqual(2, src.Split(new[] { "TelemetryPerf.Scope(" }, StringSplitOptions.None).Length - 1, "and wraps its body in a perf scope");
    }

    private static void ThePatchIsGatedOnItsFlagAndFogOnlyAndListedAsSituational()
    {
        string src = CodeOnlyOf(ReadRepoFile(PatchPath));
        AssertContains(src, "Plugin.FixSpottingMarkTiming", "gated on its flag");
        AssertContains(src, "GameVars.usefow", "and on fog");
        AssertFalse(src.Contains("EnableCampaignPlanner") || src.Contains("EnableSpottingMarkBeliefs"), "and on nothing of the planner: it changes the player's own map whether or not the planner runs");
        bool listed = false;
        foreach (string line in ReadRepoFile("tools/smoke-situational.txt").Split('\n'))
            if (line.TrimStart().StartsWith("campaign.patch.spotting-mark-timing.*", StringComparison.Ordinal)) listed = true;
        AssertTrue(listed, "listed as fog-only in the smoke check (an entry, not a comment)");
    }

    // ---- IntervalChance ----

    // The brute-force product over the ticks, placed as the closed form assumes: the midpoint of each of the k equal sub-intervals (the
    // phase of vanilla's round-robin against the review is unknown, so the unbiased placement is the middle). The check is the agreement of
    // two computations of the same product, not a fitted tolerance.
    private static float BruteForce(float e0, float interval, int ticks, float timeConstant, float perks, float cap, float gate)
    {
        double product = 1.0;
        double rate = perks / Math.Max(MarkRules.MinTimeConstantYears, timeConstant);
        for (int i = 1; i <= ticks; i++)
        {
            double t = (i - 0.5) / ticks * interval;
            if (t < gate * interval) continue;
            double p = Math.Min(cap, Math.Max(0.0, rate * (e0 + t)));
            product *= 1.0 - p;
        }
        return (float)(1.0 - product);
    }

    private static void IntervalChanceCompoundsVanillasTicks()
    {
        var cases = new (float e0, int ticks, float timeConstant, float perks, float cap)[]
        {
            (0f, 6, 0.006f, 1f, 0.9f), (0f, 12, 0.1f, 1f, 0.9f), (0.0005f, 8, 0.1f, 1f, 0.9f), (0f, 40, 0.1f, 0.5f, 0.9f),
            (0f, 60, 0.5f, 1f, 0.99f), (0.002f, 100, 0.5f, 2f, 0.9f)
        };
        foreach (var c in cases)
        {
            float closed = MarkRules.IntervalChance(c.e0, Day, c.ticks, c.timeConstant, c.perks, c.cap, 0f);
            float brute = BruteForce(c.e0, Day, c.ticks, c.timeConstant, c.perks, c.cap, 0f);
            AssertNear(brute, closed, 0.005f, "e0 " + c.e0 + " ticks " + c.ticks + " tc " + c.timeConstant);
        }
        float one = MarkRules.IntervalChance(0f, Day, 12f, 0.1f, 1f, 0.9f, 0f);
        float two = MarkRules.IntervalChance(0f, Day, 24f, 0.1f, 1f, 0.9f, 0f);
        AssertTrue(two > one, "twice the evaluations make a mark likelier");
    }

    private static void IntervalChanceCountsOnlyTicksPastTheGate()
    {
        float whole = MarkRules.IntervalChance(0f, Day, 40f, 0.1f, 1f, 0.9f, 0f);
        float half = MarkRules.IntervalChance(0f, Day, 40f, 0.1f, 1f, 0.9f, 0.5f);
        AssertNear(BruteForce(0f, Day, 40, 0.1f, 1f, 0.9f, 0.5f), half, 0.005f, "ticks before the half are not counted, and the later ones are older");
        AssertTrue(half < whole, "fewer ticks, a smaller chance: " + half + " against " + whole);
        AssertEqual(0f, MarkRules.IntervalChance(0f, Day, 40f, 0.1f, 1f, 0.9f, 1f), "a group that never leaves the circle makes no mark");
        AssertEqual(whole, MarkRules.IntervalChance(0f, Day, 40f, 0.1f, 1f, 0.9f, -0.5f), "a negative fraction reads 0");
    }

    private static void AtTheCapTheIntervalChanceIsTheCappedProduct()
    {
        // The bugged constant (a thousandth of a year) puts every tick at the cap within hours: 1 - (1 - cap)^ticks.
        AssertNear(1f - (float)Math.Pow(0.1, 4), MarkRules.IntervalChance(0.01f, Day, 4f, -50f, 1f, 0.9f, 0f), 0.0005f, "four ticks at 0.9");
        AssertNear(1f - (float)Math.Pow(0.1, 2), MarkRules.IntervalChance(0.01f, Day, 4f, -50f, 1f, 0.9f, 0.5f), 0.0005f, "two of them past the gate");
        AssertNear(0.9f, MarkRules.IntervalChance(0.01f, Day, 1f, -50f, 1f, 0.9f, 0f), 0.0005f, "one tick at the cap is the cap");
        AssertEqual(1f, MarkRules.IntervalChance(0.01f, Day, 4f, -50f, 1f, 1f, 0f), "a cap of 1 is certain");
        AssertTrue(MarkRules.IntervalChance(0f, Day, 5f, 0.006f, 1f, 0.9f, 0f) <= 1f, "never above one");
    }

    private static void NoFramesNoTicksNoMark()
    {
        AssertEqual(0f, MarkRules.IntervalChance(0.01f, Day, 0f, 0.006f, 1f, 0.9f, 0f), "no ticks");
        AssertEqual(0f, MarkRules.IntervalChance(0f, 0f, 10f, 0.006f, 1f, 0.9f, 0f), "no interval (on the ramp, where an unguarded zero width divides to NaN)");
        AssertEqual(0f, MarkRules.IntervalChance(0.01f, Day, -3f, 0.006f, 1f, 0.9f, 0f), "no negative ticks");
        AssertEqual(0f, MarkRules.IntervalChance(0.01f, Day, 10f, 0.006f, 0f, 0.9f, 0f), "no perks factor, no chance");
        AssertEqual(0f, MarkRules.IntervalChance(0.01f, Day, 10f, 0.006f, 1f, 0f, 0f), "a cap of 0");
    }

    private static void TheGateFractionIsWhereThePathLeavesTheFiftyUnitCircle()
    {
        AssertEqual(0f, MarkRules.GateFraction(100f, 0f, 120f, 0f, 0f, 0f, 50f), "started outside: every tick counts");
        AssertEqual(1f, MarkRules.GateFraction(10f, 0f, 30f, 0f, 0f, 0f, 50f), "ends inside: none does");
        AssertNear(0.5f, MarkRules.GateFraction(0f, 0f, 100f, 0f, 0f, 0f, 50f), 0.0001f, "from the mark outward, the circle is half way");
        AssertNear(0.25f, MarkRules.GateFraction(0f, 0f, 200f, 0f, 0f, 0f, 50f), 0.0001f, "a longer march leaves it sooner");
        AssertNear(0.45f, MarkRules.GateFraction(-40f, 0f, 160f, 0f, 0f, 0f, 50f), 0.0001f, "a path through the mark leaves the far side of the circle: (50 + 40) / 200");
    }

    private static void TheLedgerMarksOnlyAGroupThatLeftTheCircleDuringTheInterval()
    {
        var ledger = new MarkLedger();
        var first = Interval(false);
        ledger.Evaluate("Corps:1", 1000f, -500f, Today, 1861.4f, in first, new Random(1), (x, z) => true, out var made);
        var idle = Interval(true);
        // 10 units on in a day with a thousand ticks: the path never leaves the circle, so nothing is made.
        ledger.Evaluate("Corps:1", 1010f, -500f, Today + 1, 1861.4f + Day, in idle, new Random(2), (x, z) => true, out var kept);
        AssertEqual(made.X, kept.X, "no new mark though a thousand evaluations passed");
        AssertEqual(Today, kept.MarkDay, "the stored day stands");
        // 400 units on: it left the circle early in the interval and the ticks after the gate make a mark certain.
        ledger.Evaluate("Corps:1", 1400f, -500f, Today + 2, 1861.4f + 2 * Day, in idle, new Random(3), (x, z) => true, out var fresh);
        AssertEqual(Today + 2, fresh.MarkDay, "a mark");
        // No frames, no ticks, no mark even for a group far from its mark.
        var none = idle;
        none.Ticks = 0f;
        ledger.Evaluate("Corps:1", 2400f, -500f, Today + 3, 1861.4f + 3 * Day, in none, new Random(4), (x, z) => true, out var still);
        AssertEqual(Today + 2, still.MarkDay, "no ticks: the newest mark stands");
    }

    // The chance of an interval's rolls is a function of the newest mark's age at the interval's START (e0), which the ledger takes as the age
    // now less the interval (MarkLedger.Evaluate). A mark made at the last review is e0 = 0 and a one-day interval ramps over a day; the age at
    // the interval's end (a day) puts every tick a day older and the chance far higher. A roll between the two chances tells them apart: the
    // right age makes no mark, and a roll under the right chance still makes one (so the path is live, not shut).
    private static void TheLedgerAgesTheMarkFromTheIntervalsStartNotItsEnd()
    {
        var inputs = Interval(true);
        inputs.Ticks = 1f;
        inputs.Cap = 0.9f;
        const float markYear = 1861.4f, groupX = 6000f, groupZ = -500f;

        // The mark of the last review: made at (1000, -500), certain (the first mark's chance is the cap of 1 over 1861 years of age).
        MarkLedger Seeded(out EnemyDetection made)
        {
            var l = new MarkLedger();
            var first = Interval(false);
            l.Evaluate("Corps:1", 1000f, -500f, Today, markYear, in first, new Random(1), (x, z) => true, out made);
            return l;
        }
        Seeded(out var seed);
        float gate = MarkRules.GateFraction(1000f, -500f, groupX, groupZ, seed.X, seed.Z, inputs.MinDistance);
        float timeConstant = inputs.Intel * inputs.Spotting1 + (1f - inputs.Intel) * inputs.Spotting0;
        float right = MarkRules.IntervalChance(0f, Day, inputs.Ticks, timeConstant, inputs.PerkFactor, inputs.Cap, gate);
        float shifted = MarkRules.IntervalChance(Day, Day, inputs.Ticks, timeConstant, inputs.PerkFactor, inputs.Cap, gate);
        AssertTrue(right > 0.05f && shifted > right + 0.2f, "fixture: the two ages give clearly different chances, " + right + " against " + shifted);

        // The roll the ledger will draw for a seed: the variation draw first, then the roll (MarkLedger.Evaluate).
        double RollOf(int seedValue)
        {
            var r = new Random(seedValue);
            MarkRules.VariationDraw(r, inputs.SpottingVariation);
            return r.NextDouble();
        }
        int between = -1, under = -1;
        for (int sd = 1; sd < 500 && (between < 0 || under < 0); sd++)
        {
            double roll = RollOf(sd);
            if (between < 0 && roll > right + 0.05 && roll < shifted - 0.05) between = sd;
            if (under < 0 && roll < right - 0.02) under = sd;
        }
        AssertTrue(between >= 0 && under >= 0, "fixture: seeds exist for a roll between the chances and under the right one");

        var quiet = Seeded(out _);
        quiet.Evaluate("Corps:1", groupX, groupZ, Today + 1, markYear + Day, in inputs, new Random(between), (x, z) => true, out var kept);
        AssertEqual(Today, kept.MarkDay, "a roll above the right chance makes no mark: the mark's age at the interval's start is 0, not a day");

        var live = Seeded(out _);
        live.Evaluate("Corps:1", groupX, groupZ, Today + 1, markYear + Day, in inputs, new Random(under), (x, z) => true, out var fresh);
        AssertEqual(Today + 1, fresh.MarkDay, "a roll under it makes one");
    }

    private static MarkInputs Interval(bool use)
    {
        return new MarkInputs
        {
            Intel = 1f, Spotting0 = 0.1f, Spotting1 = 0.006f, SpottingVariation = 0.5f, PerkFactor = 1f, Cap = 1f, MinDistance = 50f,
            DistanceVariation0 = 120f, DistanceVariation1 = 25f, HasClosestEnemy = true, Active = true, ClosestEnemyX = 1500f, ClosestEnemyZ = -600f,
            UseInterval = use, IntervalYears = Day, Ticks = 1000f
        };
    }

    private static void TheLedgerCountsItsMarksAndTheGroupsItJudged()
    {
        var ledger = new MarkLedger();
        var inputs = Interval(false);
        ledger.BeginReview();
        ledger.Evaluate("Corps:1", 1000f, -500f, Today, 1861.4f, in inputs, new Random(1), (x, z) => true, out _);
        ledger.Evaluate("Corps:2", 900f, -400f, Today, 1861.4f, in inputs, new Random(1), (x, z) => true, out _);
        AssertEqual(2, ledger.Judged, "two hidden groups judged");
        AssertEqual(2, ledger.Created, "and a mark each");
        ledger.BeginReview();
        var idle = Interval(true);
        ledger.Evaluate("Corps:1", 1005f, -500f, Today + 1, 1861.4f + Day, in idle, new Random(2), (x, z) => true, out _);
        AssertEqual(1, ledger.Judged, "the counts are per review");
        AssertEqual(0, ledger.Created, "a group that did not move makes none");
    }

    // ---- the runtime ----

    private static void TheRuntimeCountsFramesAndTicksForTheInterval()
    {
        string runtime = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/EnemyDetectionRuntime.cs");
        string ai = BodyOf(runtime, "private static void EvaluateAiSideMarks(");
        AssertContains(ai, "Time.frameCount", "the frames since the last review are counted (vanilla evaluates one unit per frame, 142646-142654)");
        AssertContains(ai, "unitsattached", "divided by the units the round-robin walks");
        AssertContains(ai, "FlagOn(Plugin.FixSpottingMarkTiming)", "the interval route is the fix's: with the bug kept the chance sits at the cap and one roll a review is the same rate");
        AssertContains(ai, "useInterval", "handed to the ledger");
        string inputs = BodyOf(runtime, "private static MarkInputs ReadMarkInputs(");
        AssertContains(inputs, "IntervalYears", "the interval in game years");
        AssertContains(inputs, "Ticks", "and its evaluations");
    }

    private static void TheReportCountsNewMarksPerHiddenGroup()
    {
        string runtime = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/EnemyDetectionRuntime.cs");
        string report = BodyOf(runtime, "internal static void Report(");
        AssertContains(report, "\" marksNew=\"", "the new marks of the review");
        AssertContains(report, "\" hiddenGroups=\"", "of the hidden groups judged");
        AssertContains(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/EnemyDetectionRuntime.cs"), "ledger.BeginReview();", "the AI side counts its creations per review");
        AssertContains(runtime, "NewestMarkTimes", "the player side counts newest-mark timeshown changes");
    }

    private static void TheTimingFlagIsBoundOnAndRead()
    {
        string plugin = ReadRepoFile("src/WhiskeyRealism/Plugin.cs");
        AssertContains(plugin, "\"Campaign Planner\", \"Fix Spotting Mark Timing\", true,", "bound in the planner section, default on");
        AssertContains(ReadRepoFile(PatchPath), "Plugin.FixSpottingMarkTiming", "the patch reads it");
        AssertContains(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/EnemyDetectionRuntime.cs"), "FlagOn(Plugin.FixSpottingMarkTiming)", "and the AI side's inputs");
    }

    private static string CodeOnlyOf(string src)
    {
        var lines = new List<string>();
        foreach (string line in src.Split('\n'))
            if (!line.TrimStart().StartsWith("//", StringComparison.Ordinal)) lines.Add(line);
        return string.Join("\n", lines);
    }
}
