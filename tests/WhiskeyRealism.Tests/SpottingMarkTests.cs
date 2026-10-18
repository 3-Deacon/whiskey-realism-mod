using System;
using System.Collections.Generic;
using System.Linq;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;

using static TestHarness;
using static TestFixtures;

// Stage G strategic foresight, slice F4 (D11 requirements marks-player-side, marks-ai-side, mark-beliefs; decision 2): vanilla's spotting
// marks as beliefs on both sides. The AI side has no mark channel in vanilla (142938), so Whiskey mirrors vanilla's formula on the side's
// own grid (MarkRules, MarkLedger); the player side reads vanilla's own newest mark per hidden enemy group. A mark is a belief
// (EnemyArmyModel's second pass) that counts in every strength sum and never scouts, lands a group, raises a raid or counts as contact.
// Plan: docs/superpowers/plans/2026-10-18-strategic-foresight.md F4; cases TC-MARK-* in docs/loop/foresight/test-plan.md. The readers'
// cases that need the recon fixtures live in ReconPlannerTests; the sidecar cases in PlannerSidecarTests.
internal static class SpottingMarkTests
{
    private const int Today = 100000;
    private static readonly int[] Bands = { 50, 100, 500, 1000, 2000 };   // 002/A approximationvalues

    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("BandRoundingMatchesVanillasBands", BandRoundingMatchesVanillasBands),
            ("MarkRadiusFromStoredIntelligence", MarkRadiusFromStoredIntelligence),
            ("CreationChanceCappedAtVanillasMaximum", CreationChanceCappedAtVanillasMaximum),
            ("BugIntel001NegativeTimeConstantFloorsAtAThousandthOfAYear", BugIntel001NegativeTimeConstantFloorsAtAThousandthOfAYear),
            ("NoMarkUntilTheGroupMovesFiftyUnits", NoMarkUntilTheGroupMovesFiftyUnits),
            ("OffsetPointsTowardTheClosestEnemyAsVanillaDoes", OffsetPointsTowardTheClosestEnemyAsVanillaDoes),
            ("OffsetBlendsWithThePreviousByAge", OffsetBlendsWithThePreviousByAge),
            ("AFirstMarkIsNotBlended", AFirstMarkIsNotBlended),
            ("SeededRollsAreReproducible", SeededRollsAreReproducible),
            ("NoUnityRandomInTheMarkChannel", NoUnityRandomInTheMarkChannel),
            ("FirstMarkComesAtTheCap", FirstMarkComesAtTheCap),
            ("EvaluateReturnsTheStoredNewestMark", EvaluateReturnsTheStoredNewestMark),
            ("AStoredMarkIsReturnedForAnInactiveGroup", AStoredMarkIsReturnedForAnInactiveGroup),
            ("GarrisonsAndFleetsGetNoMark", GarrisonsAndFleetsGetNoMark),
            ("GridSeenStoredMarkDropsTheEntry", GridSeenStoredMarkDropsTheEntry),
            ("GridSeenCandidateIsRefusedAndTheStoredMarkStays", GridSeenCandidateIsRefusedAndTheStoredMarkStays),
            ("NoNewMarkWithTheGridBroken", NoNewMarkWithTheGridBroken),
            ("AnAbsentGroupsMarkIsPrunedAfterTenReviews", AnAbsentGroupsMarkIsPrunedAfterTenReviews),
            ("MarkLedgerIgnoresAllianceTwo", MarkLedgerIgnoresAllianceTwo),
            ("MarkDayDecodesTheCalendarDate", MarkDayDecodesTheCalendarDate),
            ("PlayerSideReadsOnlyTheNewestMark", PlayerSideReadsOnlyTheNewestMark),
            ("PlayerSideMarkNeedsAnUndetectedEnemyWithAMark", PlayerSideMarkNeedsAnUndetectedEnemyWithAMark),
            ("RuntimeReadsMarksOnlyForEnemyGroups", RuntimeReadsMarksOnlyForEnemyGroups),
            ("AFailedProbeKeepsEveryStoredMarkAndEveryKey", AFailedProbeKeepsEveryStoredMarkAndEveryKey),
            ("MarkMatchesByExactKeyOnly", MarkMatchesByExactKeyOnly),
            ("MarkNeverRetiresAnotherBelief", MarkNeverRetiresAnotherBelief),
            ("SameDaySightingBeatsAMark", SameDaySightingBeatsAMark),
            ("SameDaySightingAndMarkMakeOneBelief", SameDaySightingAndMarkMakeOneBelief),
            ("OlderMarkIgnored", OlderMarkIgnored),
            ("AStaleNewestMarkKeepsNoBeliefAlive", AStaleNewestMarkKeepsNoBeliefAlive),
            ("ASightingNeverRetiresAMarkBelief", ASightingNeverRetiresAMarkBelief),
            ("AMarkBeliefReKeysWithinItsBaseRadius", AMarkBeliefReKeysWithinItsBaseRadius),
            ("MarkRadiusGrowsWithDrift", MarkRadiusGrowsWithDrift),
            ("AMarkNeverWritesLastEmbarkedDay", AMarkNeverWritesLastEmbarkedDay),
            ("AnEmbarkedGroupsMarkIsAfloat", AnEmbarkedGroupsMarkIsAfloat),
            ("ALandGroupsMarkCarriesNoEmbarkedTrait", ALandGroupsMarkCarriesNoEmbarkedTrait),
            ("AMarkReadsTheLiveStrengthEachReview", AMarkReadsTheLiveStrengthEachReview),
            ("MarksCountAppliedMarks", MarksCountAppliedMarks),
            ("RememberedExcludesAppliedMarks", RememberedExcludesAppliedMarks),
            ("SeenCountExcludesMarks", SeenCountExcludesMarks),
            ("PictureRowCountsSeenAndMarks", PictureRowCountsSeenAndMarks),
            ("MarkBeliefNeverScouts", MarkBeliefNeverScouts),
            ("MarkBeliefCountsInTheTotals", MarkBeliefCountsInTheTotals),
            ("AMarkNeverLandsAGroup", AMarkNeverLandsAGroup),
            ("AMarkIsNoRaid", AMarkIsNoRaid),
            ("TheSeedIsSetBeforeDetect", TheSeedIsSetBeforeDetect),
            ("TheMarkFlagIsBoundOnAndReadByTheRuntime", TheMarkFlagIsBoundOnAndReadByTheRuntime),
        });
    }

    // ---- fixtures ----

    // The 002/A prefs with BUG-INTEL-001's concealment pair (-50, -60) where timefactorspottings (0.1, 0.006) is meant, intel 1, the first
    // mark certain (cap 1) so a case needs no roll luck.
    private static MarkInputs Inputs(float cap = 1f)
    {
        return new MarkInputs
        {
            Intel = 1f, Spotting0 = -50f, Spotting1 = -60f, SpottingVariation = 0.5f, PerkFactor = 1f, Cap = cap, MinDistance = 50f,
            DistanceVariation0 = 120f, DistanceVariation1 = 25f, HasClosestEnemy = true, Active = true, ClosestEnemyX = 1500f, ClosestEnemyZ = -600f
        };
    }

    private static readonly Func<float, float, bool> Hidden = (x, z) => true;

    private static Random Rng(string key = "Corps:1", int day = Today, int alliance = 1) => MarkRules.RngFor(0x5EED1234L, alliance, day, key);

    private static bool Evaluate(MarkLedger ledger, string key, float x, float z, int day, float years, in MarkInputs inputs,
                                 Func<float, float, bool> grid, out EnemyDetection mark)
    {
        return ledger.Evaluate(key, x, z, day, years, in inputs, Rng(key, day), grid, out mark);
    }

    private static EnemyDetection Sighting(string key, float x, float z, float strength, int instance = 1, int unitTyp = 15,
                                           DetectionSource source = DetectionSource.Seen, EnemyTraits traits = 0)
    {
        return new EnemyDetection { InstanceId = instance, UnitKey = key, UnitTyp = unitTyp, X = x, Z = z, Strength = strength, Source = source, Traits = traits };
    }

    private static EnemyDetection Mark(string key, float x, float z, float strength, int markDay, float radius = 25f, int unitTyp = 15, EnemyTraits traits = 0)
    {
        return new EnemyDetection
        {
            InstanceId = 7, UnitKey = key, UnitTyp = unitTyp, X = x, Z = z, Strength = strength, Source = DetectionSource.Mark, Traits = traits,
            MarkDay = markDay, MarkRadius = radius
        };
    }

    private static List<EnemyDetection> List(params EnemyDetection[] d) => new List<EnemyDetection>(d);

    private static Func<int, float> Drift(float perDay) => _ => perDay;
    private static readonly Func<int, float> NoMarch = _ => 0f;

    private static EnemyBelief Single(EnemyArmyModel m, string key)
    {
        var found = m.Beliefs.Where(b => b.UnitKey == key).ToList();
        AssertEqual(1, found.Count, "one belief for " + key);
        return found[0];
    }

    // ---- MarkRules ----

    private static void BandRoundingMatchesVanillasBands()
    {
        AssertNear(750f, MarkRules.BandRound(760f, Bands), 0.01f, "under 1000 rounds to 50");
        AssertNear(1000f, MarkRules.BandRound(999f, Bands), 0.01f, "999 to 1000");
        AssertNear(3000f, MarkRules.BandRound(3050f, Bands), 0.01f, "under 5000 rounds to 100, half to even (30.5 -> 30)");
        AssertNear(7000f, MarkRules.BandRound(7250f, Bands), 0.01f, "under 10000 rounds to 500, half to even (14.5 -> 14)");
        AssertNear(24000f, MarkRules.BandRound(23700f, Bands), 0.01f, "under 50000 rounds to 1000");
        AssertNear(60000f, MarkRules.BandRound(61000f, Bands), 0.01f, "from 50000 rounds to 2000, half to even");
        AssertNear(80f, MarkRules.BandRound(80f, Bands), 0.01f, "under 100 rounds to 5");
        AssertNear(100f, MarkRules.BandRound(99f, Bands), 0.01f, "99 to 100 at a step of 5");
        AssertNear(10f, MarkRules.BandRound(12f, Bands), 0.01f, "12 to 10");
        AssertNear(0f, MarkRules.BandRound(0f, Bands), 0.01f, "nothing stays nothing");
        AssertNear(0f, MarkRules.BandRound(float.NaN, Bands), 0.01f, "a bad strength reads 0");
    }

    private static void MarkRadiusFromStoredIntelligence()
    {
        var dv = new[] { 120f, 25f };
        AssertNear(25f, MarkRules.MarkRadius(1f, dv), 0.001f, "intelligence 1: 25 map units in 002/A (the map is off, so the stored intelligence is 1)");
        AssertNear(120f, MarkRules.MarkRadius(0f, dv), 0.001f, "intelligence 0: 120");
        AssertNear(72.5f, MarkRules.MarkRadius(0.5f, dv), 0.001f, "linear in between");
        AssertNear(0f, MarkRules.MarkRadius(1f, null), 0.001f, "no pref reads 0");
    }

    private static void CreationChanceCappedAtVanillasMaximum()
    {
        // timefactorspottings: 0.1 game years at intelligence 0, 0.006 at 1.
        AssertNear(0.5f, MarkRules.CreationChance(0.003f, 1f, 0.1f, 0.006f, 1f, 1f, 0.9f), 0.001f, "elapsed over the time constant");
        AssertNear(0.9f, MarkRules.CreationChance(1f, 1f, 0.1f, 0.006f, 1f, 1f, 0.9f), 0.001f, "capped at maximumcreationprobability");
        AssertNear(0.25f, MarkRules.CreationChance(0.003f, 1f, 0.1f, 0.006f, 1f, 0.5f, 0.9f), 0.001f, "a perk factor scales it before the cap");
        AssertNear(0f, MarkRules.CreationChance(-0.01f, 1f, 0.1f, 0.006f, 1f, 1f, 0.9f), 0.001f, "never negative");
        AssertNear(0.25f, MarkRules.CreationChance(0.003f, 1f, 0.1f, 0.006f, 2f, 1f, 0.9f), 0.001f, "a variation draw of 2 doubles the time constant");
    }

    // 142819 reads the concealment pair (-50, -60) where timefactorspottings is meant: the time constant is negative and 142820's
    // max(0.001, num3) turns it into a thousandth of a game year, so the chance is certain from 0.0009 years (about a third of a day).
    private static void BugIntel001NegativeTimeConstantFloorsAtAThousandthOfAYear()
    {
        AssertNear(0.5f, MarkRules.CreationChance(0.0005f, 1f, -50f, -60f, 1f, 1f, 0.9f), 0.001f, "half a thousandth of a year: 0.5");
        AssertNear(0.1f, MarkRules.CreationChance(0.0001f, 1f, -50f, -60f, 0.7f, 1f, 0.9f), 0.001f, "the variation draw does not move a negative constant");
        AssertNear(0.9f, MarkRules.CreationChance(0.0009f, 1f, -50f, -60f, 1f, 1f, 0.9f), 0.001f, "capped from about 0.33 game days");
        AssertNear(0.9f, MarkRules.CreationChance(0.01f, 1f, -50f, -60f, 1f, 1f, 0.9f), 0.001f, "and after");
    }

    private static void OffsetPointsTowardTheClosestEnemyAsVanillaDoes()
    {
        var dv = new[] { 120f, 25f };
        AssertNear(-90f, MarkRules.ReverseBearing(0f, 0f, 10f, 0f), 0.01f, "Tools.GetAngle is the reverse bearing: an enemy at +x reads -90");
        AssertNear(-180f, MarkRules.ReverseBearing(0f, 0f, 0f, 10f), 0.01f, "an enemy at +z reads -180");
        for (int seed = 0; seed < 200; seed++)
        {
            var rng = new Random(seed);
            MarkRules.Offset(rng, 1f, dv, true, MarkRules.ReverseBearing(0f, 0f, 10f, 0f), false, 0f, 0f, 0f, out float dx, out float dz);
            float magnitude = (float)Math.Sqrt(dx * dx + dz * dz);
            AssertTrue(dx > 0f, "seed " + seed + ": an enemy at +x is toward +x, got " + dx);
            AssertTrue(Math.Abs(dz) <= dx + 0.001f, "within the 45 degree cone, got " + dx + "," + dz);
            AssertTrue(magnitude >= 3f - 0.001f && magnitude <= 25f + 0.001f, "magnitude between 3 and the radius, got " + magnitude);
            var north = new Random(seed);
            MarkRules.Offset(north, 1f, dv, true, MarkRules.ReverseBearing(0f, 0f, 0f, 10f), false, 0f, 0f, 0f, out float nx, out float nz);
            AssertTrue(nz > 0f, "an enemy at +z is toward +z, got " + nz);
        }
        // No closest enemy: a box of +- the radius.
        for (int seed = 0; seed < 50; seed++)
        {
            MarkRules.Offset(new Random(seed), 0f, dv, false, 0f, false, 0f, 0f, 0f, out float bx, out float bz);
            AssertTrue(Math.Abs(bx) <= 120f && Math.Abs(bz) <= 120f, "inside the box of the radius at intelligence 0");
        }
    }

    private static void OffsetBlendsWithThePreviousByAge()
    {
        var dv = new[] { 120f, 25f };
        MarkRules.Offset(new Random(9), 1f, dv, true, -90f, false, 0f, 0f, 0f, out float rawX, out float rawZ);
        MarkRules.Offset(new Random(9), 1f, dv, true, -90f, true, 0f, 40f, -10f, out float sameX, out float sameZ);
        AssertNear(40f, sameX, 0.001f, "an age of 0 keeps the previous offset whole");
        AssertNear(-10f, sameZ, 0.001f, "x and z");
        MarkRules.Offset(new Random(9), 1f, dv, true, -90f, true, 0.04f, 40f, -10f, out float freshX, out float freshZ);
        AssertNear(rawX, freshX, 0.001f, "from 0.04 game years (25 x age = 1) the new offset stands alone");
        AssertNear(rawZ, freshZ, 0.001f, "z");
        MarkRules.Offset(new Random(9), 1f, dv, true, -90f, true, 0.02f, 40f, -10f, out float halfX, out float halfZ);
        AssertNear(rawX * 0.5f + 20f, halfX, 0.001f, "at 0.02 years half and half");
        AssertNear(rawZ * 0.5f - 5f, halfZ, 0.001f, "z");
    }

    private static void AFirstMarkIsNotBlended()
    {
        var dv = new[] { 120f, 25f };
        MarkRules.Offset(new Random(4), 1f, dv, true, -90f, false, 0.001f, 500f, 500f, out float x, out float z);
        MarkRules.Offset(new Random(4), 1f, dv, true, -90f, false, 99f, -3f, 8f, out float x2, out float z2);
        AssertNear(x, x2, 0.0001f, "with no previous mark the previous offset and the age are not read");
        AssertNear(z, z2, 0.0001f, "z");
        var ledger = new MarkLedger();
        var inputs = Inputs();
        AssertTrue(Evaluate(ledger, "Corps:1", 1000f, -500f, Today, 1861.4f, in inputs, Hidden, out var first), "the first mark is made");
        AssertTrue(Math.Abs(first.X - 1000f) <= 25.01f && Math.Abs(first.Z + 500f) <= 25.01f, "within the radius of the truth: " + first.X + "," + first.Z);
    }

    private static void SeededRollsAreReproducible()
    {
        var a = Rng("Corps:1");
        var b = Rng("Corps:1");
        for (int i = 0; i < 8; i++) AssertEqual(a.NextDouble(), b.NextDouble(), "the same seed, draw " + i);
        AssertTrue(Rng("Corps:1").NextDouble() != Rng("Corps:2").NextDouble(), "another group draws its own stream");
        AssertTrue(Rng("Corps:1", Today).NextDouble() != Rng("Corps:1", Today + 1).NextDouble(), "another day draws its own stream");
        AssertTrue(Rng("Corps:1", Today, 0).NextDouble() != Rng("Corps:1", Today, 1).NextDouble(), "the other alliance draws its own stream");
        var one = new MarkLedger();
        var two = new MarkLedger();
        var inputs = Inputs(0.9f);
        for (int day = 0; day < 20; day++)
        {
            bool r1 = Evaluate(one, "Corps:1", 1000f + day * 80f, -500f, Today + day, 1861.4f + day / 365.25f, in inputs, Hidden, out var m1);
            bool r2 = Evaluate(two, "Corps:1", 1000f + day * 80f, -500f, Today + day, 1861.4f + day / 365.25f, in inputs, Hidden, out var m2);
            AssertEqual(r1, r2, "day " + day + " same verdict");
            AssertEqual(m1.X, m2.X, "day " + day + " same mark");
            AssertEqual(m1.Z, m2.Z, "day " + day + " same mark z");
        }
    }

    // The mark channel draws from System.Random seeded per D11 (never UnityEngine.Random, which would shift vanilla's own stream).
    private static void NoUnityRandomInTheMarkChannel()
    {
        foreach (string path in new[] { "src/WhiskeyRealism/Strategic/Planner/MarkRules.cs", "src/WhiskeyRealism/Strategic/Planner/MarkLedger.cs" })
        {
            string code = CodeOnlyLines(ReadRepoFile(path));
            AssertFalse(code.Contains("UnityEngine"), path + " names no Unity type");
            AssertFalse(code.Contains("Random.Range") || code.Contains("Random.value"), path + " calls no UnityEngine.Random");
        }
        string runtime = CodeOnlyLines(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/EnemyDetectionRuntime.cs"));
        AssertFalse(runtime.Contains("UnityEngine.Random") || runtime.Contains("Random.Range("), "the runtime draws no Unity random either");
        AssertContains(runtime, "MarkRules.RngFor(", "the AI side seeds its stream per group per review");
    }

    // ---- MarkLedger ----

    private static void FirstMarkComesAtTheCap()
    {
        var inputs = Inputs(0.9f);
        int made = 0;
        for (int seed = 0; seed < 2000; seed++)
        {
            var ledger = new MarkLedger();
            if (ledger.Evaluate("Corps:" + seed, 1000f, -500f, Today, 1861.4f, in inputs, new Random(seed), Hidden, out _)) made++;
        }
        AssertTrue(made > 1700 && made < 1900, "the first evaluation marks at the cap (the time since 'no mark' is the whole date): " + made + " of 2000");
    }

    private static void NoMarkUntilTheGroupMovesFiftyUnits()
    {
        var ledger = new MarkLedger();
        var inputs = Inputs();
        AssertTrue(Evaluate(ledger, "Corps:1", 1000f, -500f, Today, 1861.4f, in inputs, Hidden, out var first), "the first mark (measured from the map origin)");
        // 20 units on: the distance to the stored mark stays under 50 (the offset is at most 25).
        AssertTrue(Evaluate(ledger, "Corps:1", 1020f, -500f, Today + 1, 1861.45f, in inputs, Hidden, out var kept), "the stored mark is still returned");
        AssertEqual(first.X, kept.X, "no new mark: the same position");
        AssertEqual(first.Z, kept.Z, "z");
        AssertEqual(Today, kept.MarkDay, "and the same day");
        // 100 units on: past the gate, a new mark.
        AssertTrue(Evaluate(ledger, "Corps:1", 1100f, -500f, Today + 2, 1861.5f, in inputs, Hidden, out var next), "a mark");
        AssertEqual(Today + 2, next.MarkDay, "a new mark after the group moved 50 units from the stored one");
        AssertTrue(next.X != kept.X, "at a new place");
    }

    private static void EvaluateReturnsTheStoredNewestMark()
    {
        var ledger = new MarkLedger();
        var inputs = Inputs();
        Evaluate(ledger, "Corps:1", 1000f, -500f, Today, 1861.4f, in inputs, Hidden, out var made);
        var idle = inputs;
        idle.Cap = 0f;
        AssertTrue(Evaluate(ledger, "Corps:1", 1500f, -100f, Today + 5, 1861.5f, in idle, Hidden, out var later), "a later review returns the stored mark though none is created");
        AssertEqual(made.X, later.X, "position");
        AssertEqual(made.Z, later.Z, "z");
        AssertEqual(Today, later.MarkDay, "the day it was made");
        AssertEqual(DetectionSource.Mark, later.Source, "a Mark detection");
        AssertEqual("Corps:1", later.UnitKey, "keyed by the group");
        AssertNear(25f, later.MarkRadius, 0.001f, "radius from the stored intelligence");
    }

    private static void AStoredMarkIsReturnedForAnInactiveGroup()
    {
        var ledger = new MarkLedger();
        var inputs = Inputs();
        Evaluate(ledger, "Corps:1", 1000f, -500f, Today, 1861.4f, in inputs, Hidden, out var made);
        var inactive = inputs;
        inactive.Active = false;
        AssertTrue(Evaluate(ledger, "Corps:1", 1400f, -500f, Today + 3, 1861.5f, in inactive, Hidden, out var kept), "still returned");
        AssertEqual(made.X, kept.X, "no new mark for an inactive group (142854-142862)");
        var noEnemy = inputs;
        noEnemy.HasClosestEnemy = false;
        Evaluate(ledger, "Corps:1", 1400f, -500f, Today + 4, 1861.5f, in noEnemy, Hidden, out var kept2);
        AssertEqual(made.X, kept2.X, "nor with no closest enemy");
    }

    // The ledger's pure half of GarrisonsAndFleetsGetNoMark: the gate that keeps them out is DetectionRules.RelationOf (a regression pin that
    // is green on shipped code), and a ledger fed one anyway creates no mark.
    private static void GarrisonsAndFleetsGetNoMark()
    {
        AssertEqual(UnitRelation.Ignore, DetectionRules.RelationOf(1, 0, false, true, true, false), "a fleet is no enemy army");
        AssertEqual(UnitRelation.Ignore, DetectionRules.RelationOf(1, 0, false, true, false, true), "nor is a garrison");
        AssertEqual(UnitRelation.Enemy, DetectionRules.RelationOf(1, 0, false, true, false, false), "an at-war army is");
        var inputs = Inputs();
        inputs.Garrison = true;
        AssertFalse(Evaluate(new MarkLedger(), "Garrison:1", 1000f, -500f, Today, 1861.4f, in inputs, Hidden, out _), "a garrison gets no mark");
        inputs.Garrison = false;
        inputs.Fleet = true;
        AssertFalse(Evaluate(new MarkLedger(), "Fleet:1", 1000f, -500f, Today, 1861.4f, in inputs, Hidden, out _), "nor a fleet");
    }

    private static void GridSeenStoredMarkDropsTheEntry()
    {
        var ledger = new MarkLedger();
        var inputs = Inputs();
        Evaluate(ledger, "Corps:1", 1000f, -500f, Today, 1861.4f, in inputs, Hidden, out var made);
        AssertEqual(1, ledger.Count, "a mark is held");
        AssertFalse(Evaluate(ledger, "Corps:1", 1000f, -500f, Today + 1, 1861.45f, in inputs, (x, z) => false, out _),
            "the side's grid sees the newest mark's position: vanilla removes the marks (142800-142807)");
        AssertEqual(0, ledger.Count, "the entry is gone");
    }

    private static void GridSeenCandidateIsRefusedAndTheStoredMarkStays()
    {
        var ledger = new MarkLedger();
        var inputs = Inputs();
        Evaluate(ledger, "Corps:1", 1000f, -500f, Today, 1861.4f, in inputs, Hidden, out var made);
        // The stored position stays hidden, every candidate is seen: AddMark refuses it (142724).
        Func<float, float, bool> storedHiddenOnly = (x, z) => Math.Abs(x - made.X) < 0.001f && Math.Abs(z - made.Z) < 0.001f;
        AssertTrue(Evaluate(ledger, "Corps:1", 1400f, -500f, Today + 2, 1861.5f, in inputs, storedHiddenOnly, out var kept), "the stored mark is returned");
        AssertEqual(made.X, kept.X, "no new mark");
        AssertEqual(Today, kept.MarkDay, "the stored day stands");
        var fresh = new MarkLedger();
        AssertFalse(Evaluate(fresh, "Corps:2", 1000f, -500f, Today, 1861.4f, in inputs, (x, z) => false, out _), "a first mark whose position the grid sees is refused");
        AssertEqual(0, fresh.Count, "and nothing is stored");
    }

    private static void NoNewMarkWithTheGridBroken()
    {
        var ledger = new MarkLedger();
        var inputs = Inputs();
        AssertFalse(Evaluate(ledger, "Corps:1", 1000f, -500f, Today, 1861.4f, in inputs, null, out _), "with the grid missing or broken the AI side creates no mark");
        Evaluate(ledger, "Corps:1", 1000f, -500f, Today, 1861.4f, in inputs, Hidden, out var made);
        AssertTrue(Evaluate(ledger, "Corps:1", 1500f, -500f, Today + 1, 1861.5f, in inputs, null, out var kept), "a stored mark is still returned");
        AssertEqual(made.X, kept.X, "unchanged");
    }

    private static void AnAbsentGroupsMarkIsPrunedAfterTenReviews()
    {
        var ledger = new MarkLedger();
        var inputs = Inputs();
        Evaluate(ledger, "Corps:1", 1000f, -500f, Today, 1861.4f, in inputs, Hidden, out _);
        Evaluate(ledger, "Corps:2", 1000f, -400f, Today, 1861.4f, in inputs, Hidden, out _);
        var present = new HashSet<string> { "Corps:2" };
        for (int review = 1; review < PlannerState.PruneAfterMissedReviews; review++)
        {
            ledger.PruneUnseen(present);
            AssertEqual(2, ledger.Count, "review " + review + ": still kept");
        }
        ledger.PruneUnseen(present);
        AssertEqual(1, ledger.Count, "dropped at the tenth missed review");
        ledger.Drop("Corps:2");
        AssertEqual(0, ledger.Count, "Drop removes a group's mark (detected, or in battle)");
    }

    private static void MarkLedgerIgnoresAllianceTwo()
    {
        AssertTrue(MarkLedgers.For(2) == null, "Europe has no ledger");
        AssertTrue(MarkLedgers.For(-1) == null, "nor a negative alliance");
        AssertTrue(MarkLedgers.For(0) != null && MarkLedgers.For(1) != null && !ReferenceEquals(MarkLedgers.For(0), MarkLedgers.For(1)), "one ledger per side");
        var inputs = Inputs();
        Evaluate(MarkLedgers.For(1), "Corps:1", 1000f, -500f, Today, 1861.4f, in inputs, Hidden, out _);
        WhiskeyRealism.Util.CampaignScope.ResetAll();
        AssertEqual(0, MarkLedgers.For(1).Count, "a campaign reset clears the marks");
    }

    // ---- the calendar decode and the player side ----

    // GameVars.GetCampaignFloatingDate in float arithmetic, as vanilla stamps timeshown (66617).
    private static float Stamp(int year, int month, int day, int hour = 0)
    {
        return (float)year + (float)(month - 1) / 12f + (float)(day - 1) / 365.25f + hour / 24f / 365.25f;
    }

    private static void MarkDayDecodesTheCalendarDate()
    {
        int feb28 = ReviewCadenceCatchUp.DaySerial(28, 2, 1862);
        int mar1 = ReviewCadenceCatchUp.DaySerial(1, 3, 1862);
        int far = mar1 + 2000;
        AssertEqual(feb28, MarkRules.MarkDay(Stamp(1862, 2, 28), far), "28 February");
        AssertEqual(mar1, MarkRules.MarkDay(Stamp(1862, 3, 1), far), "1 March");
        AssertEqual(1, MarkRules.MarkDay(Stamp(1862, 3, 1), far) - MarkRules.MarkDay(Stamp(1862, 2, 28), far),
            "28 February to 1 March is one day (a straight x 365.25 decode reads 3)");
        AssertEqual(ReviewCadenceCatchUp.DaySerial(31, 1, 1862), MarkRules.MarkDay(Stamp(1862, 1, 31, 23), far), "31 January 23:00 stays in January");
        AssertEqual(ReviewCadenceCatchUp.DaySerial(15, 6, 1861), MarkRules.MarkDay(Stamp(1861, 6, 15, 9), far), "an ordinary day");
        AssertEqual(ReviewCadenceCatchUp.DaySerial(1, 1, 1862), MarkRules.MarkDay(Stamp(1862, 1, 1), far), "the first day of a year");
        AssertEqual(ReviewCadenceCatchUp.DaySerial(31, 12, 1861), MarkRules.MarkDay(Stamp(1861, 12, 31, 6), far), "the last day of a year");
        AssertEqual(ReviewCadenceCatchUp.DaySerial(29, 2, 1864), MarkRules.MarkDay(Stamp(1864, 2, 29), far), "a leap day");
        int today = ReviewCadenceCatchUp.DaySerial(10, 6, 1861);
        AssertEqual(today, MarkRules.MarkDay(Stamp(1861, 6, 20), today), "at most today");
        AssertEqual(today, MarkRules.MarkDay(float.NaN, today), "a bad stamp reads today");
    }

    private static void PlayerSideReadsOnlyTheNewestMark()
    {
        AssertEqual(2, MarkRules.NewestMark(new List<float> { 1861.1f, 1861.2f, 1861.3f }), "AddMark appends: the newest is the last (142721)");
        AssertEqual(-1, MarkRules.NewestMark(new List<float>()), "none");
        AssertEqual(-1, MarkRules.NewestMark(null), "no list");
    }

    private static void PlayerSideMarkNeedsAnUndetectedEnemyWithAMark()
    {
        AssertTrue(MarkRules.PlayerSideMark(UnitRelation.Enemy, false, 1), "an enemy group, not detected today, with a mark");
        AssertFalse(MarkRules.PlayerSideMark(UnitRelation.Enemy, true, 3), "a group a rule a-d detected today is seen, not marked");
        AssertFalse(MarkRules.PlayerSideMark(UnitRelation.Enemy, false, 0), "no mark");
        AssertFalse(MarkRules.PlayerSideMark(UnitRelation.Viewer, false, 2), "an own group");
        AssertFalse(MarkRules.PlayerSideMark(UnitRelation.Ignore, false, 2), "an ignored group (a fleet, a garrison, a neutral)");
    }

    private static void RuntimeReadsMarksOnlyForEnemyGroups()
    {
        string runtime = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/EnemyDetectionRuntime.cs");
        string body = BodyOf(runtime, "private static void ReadPlayerSideMarks(");
        AssertContains(body, "spottedmarks", "the player side reads vanilla's mark list");
        AssertContains(body, "MarkRules.PlayerSideMark(", "through the pure filter");
        AssertContains(body, "DetectionRules.RelationOf(", "classed with the same relation rule as every enemy group (142938: the player alliance never has an entry)");
        AssertContains(body, "MarkRules.NewestMark(", "the newest mark only");
        AssertContains(body, "MarkRules.MarkDay(", "its calendar day");
    }

    // A probe that throws ends new marks for the rest of the review but not the stored ones, and the prune sees every key (a partial set
    // would age the groups after the failing one by a review they did not miss).
    private static void AFailedProbeKeepsEveryStoredMarkAndEveryKey()
    {
        string runtime = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/EnemyDetectionRuntime.cs");
        string body = BodyOf(runtime, "private static void EvaluateAiSideMarks(");
        int catchAt = body.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        AssertTrue(catchAt > 0, "the per-group probe is guarded");
        string handler = body.Substring(catchAt);
        handler = handler.Substring(0, handler.IndexOf("ledger.PruneUnseen(", StringComparison.Ordinal));
        AssertContains(handler, "gridUsable = false;", "the rest of the review passes no grid");
        AssertFalse(handler.Contains("break;"), "and the loop goes on, so every group is present and returns its stored mark");
        AssertContains(body, "PresentKeys.Add(key);", "the key is noted before the probe can throw");
        var model = new EnemyArmyModel();
        model.Update(List(Mark("Corps A:1", 120f, 0f, 9000f, Today)), Today, Drift(10f), NoMarch);
        model.Restore(new List<EnemyBelief>());
        AssertEqual(0, model.AppliedMarkKeys.Count, "a restore forgets the last review's applied marks");
        AssertEqual(0, model.RememberedCount(Today), "and its remembered count reads nothing");
    }

    // ---- EnemyArmyModel: the second pass ----

    private static void MarkMatchesByExactKeyOnly()
    {
        var model = new EnemyArmyModel();
        model.Update(List(Sighting("Corps A:1", 100f, 0f, 9000f)), Today, Drift(10f), NoMarch);
        // A mark under a key no belief has: a new belief (no re-key by name, no retirement), though a road march of 50 would bring it into reach.
        model.Update(List(Mark("Corps A:2", 105f, 0f, 9500f, Today + 1)), Today + 1, Drift(10f), EnemyArmyModelMarch);
        AssertEqual(2, model.Beliefs.Count, "the same name under another commander is another group for a mark");
        var control = new EnemyArmyModel();
        control.Update(List(Sighting("Corps A:1", 100f, 0f, 9000f)), Today, Drift(10f), NoMarch);
        control.Update(List(Sighting("Corps A:2", 105f, 0f, 9500f)), Today + 1, Drift(10f), EnemyArmyModelMarch);
        AssertEqual(1, control.Beliefs.Count, "control: a sighting under the new commander re-keys the old belief");
        var a1 = Single(model, "Corps A:1");
        var a2 = Single(model, "Corps A:2");
        AssertEqual(DetectionSource.Seen, a1.LastSource, "the old belief is untouched");
        AssertEqual(DetectionSource.Mark, a2.LastSource, "the mark is its own belief");
    }

    private static void MarkNeverRetiresAnotherBelief()
    {
        var model = new EnemyArmyModel();
        model.Update(List(Sighting("Old Name:1", 100f, 0f, 9000f)), Today, Drift(10f), NoMarch);
        // Step 4 retires the nearest unmatched belief of the same type within its plausibility radius for a still-new key: a sighting does,
        // a mark never does.
        model.Update(List(Mark("Renamed:1", 104f, 0f, 9000f, Today + 1)), Today + 1, Drift(10f), (EnemyArmyModelMarch));
        AssertEqual(2, model.Beliefs.Count, "the nearby mark retired nothing");
        var control = new EnemyArmyModel();
        control.Update(List(Sighting("Old Name:1", 100f, 0f, 9000f)), Today, Drift(10f), NoMarch);
        control.Update(List(Sighting("Renamed:1", 104f, 0f, 9000f)), Today + 1, Drift(10f), EnemyArmyModelMarch);
        AssertEqual(1, control.Beliefs.Count, "control: a sighting of the same renamed group retires the old belief");
    }

    private static readonly Func<int, float> EnemyArmyModelMarch = _ => 50f;

    private static void SameDaySightingBeatsAMark()
    {
        var model = new EnemyArmyModel();
        model.Update(List(Sighting("Corps A:1", 100f, 0f, 9000f), Mark("Corps A:1", 140f, 0f, 9500f, Today)), Today, Drift(10f), NoMarch);
        var belief = Single(model, "Corps A:1");
        AssertEqual(DetectionSource.Seen, belief.LastSource, "the sighting stands");
        AssertNear(100f, belief.X, 0.001f, "at the true position");
        AssertEqual(0f, belief.BaseRadius, "with no mark radius");
        AssertEqual(0, model.AppliedMarkKeys.Count, "the mark was not applied");
    }

    private static void SameDaySightingAndMarkMakeOneBelief()
    {
        var model = new EnemyArmyModel();
        model.Update(List(Sighting("Corps A:1", 100f, 0f, 9000f, instance: 3), Mark("Corps A:1", 120f, 0f, 9000f, Today - 1)), Today, Drift(10f), NoMarch);
        AssertEqual(1, model.Beliefs.Count, "one group, one belief");
        AssertEqual(DetectionSource.Seen, model.Beliefs[0].LastSource, "the sighting");
        var twice = new EnemyArmyModel();
        twice.Update(List(Mark("Corps A:1", 120f, 0f, 9000f, Today - 1)), Today, Drift(10f), NoMarch);
        twice.Update(List(Sighting("Corps A:1", 100f, 0f, 9000f), Mark("Corps A:1", 120f, 0f, 9000f, Today - 1)), Today + 1, Drift(10f), NoMarch);
        AssertEqual(1, twice.Beliefs.Count, "a mark belief re-seen is the same belief, not two");
    }

    private static void OlderMarkIgnored()
    {
        var model = new EnemyArmyModel();
        model.Update(List(Mark("Corps A:1", 120f, 0f, 9000f, Today)), Today, Drift(10f), NoMarch);
        model.Update(List(Mark("Corps A:1", 400f, 0f, 9000f, Today - 3)), Today + 1, Drift(10f), NoMarch);
        var belief = Single(model, "Corps A:1");
        AssertNear(120f, belief.X, 0.001f, "a mark older than the belief's last-seen day is ignored");
        AssertEqual(Today, belief.LastSeenDay, "the day stands");
        model.Update(List(Mark("Corps A:1", 160f, 0f, 9000f, Today)), Today + 2, Drift(10f), NoMarch);
        AssertNear(160f, Single(model, "Corps A:1").X, 0.001f, "the same mark day refreshes it (the mark is read every review)");
        model.Update(List(Mark("Corps A:1", 200f, 0f, 9000f, Today + 2)), Today + 2, Drift(10f), NoMarch);
        AssertNear(200f, Single(model, "Corps A:1").X, 0.001f, "a newer mark replaces it");
    }

    private static void AStaleNewestMarkKeepsNoBeliefAlive()
    {
        var model = new EnemyArmyModel();
        model.Update(List(Mark("Corps A:1", 120f, 0f, 9000f, Today - EnemyArmyModel.ExpireDays)), Today, Drift(10f), NoMarch);
        AssertEqual(0, model.Beliefs.Count, "vanilla keeps the newest mark while the group stays in fog, but a mark 30 days old makes no belief");
        model.Update(List(Mark("Corps B:1", 120f, 0f, 9000f, Today - 40)), Today, Drift(10f), NoMarch);
        AssertEqual(0, model.Beliefs.Count, "nor an older one");
        var decayed = new EnemyArmyModel();
        decayed.Update(List(Mark("Corps A:1", 120f, 0f, 9000f, Today - 35 + 10)), Today, Drift(10f), NoMarch);
        AssertEqual(1, decayed.Beliefs.Count, "a 25 day old mark is still above the confidence floor");
        // A belief that exists is not kept alive by a stale mark.
        var kept = new EnemyArmyModel();
        kept.Update(List(Sighting("Corps A:1", 100f, 0f, 9000f)), Today, Drift(10f), NoMarch);
        kept.Update(List(Mark("Corps A:1", 140f, 0f, 9000f, Today - 100)), Today + 29, Drift(10f), NoMarch);
        AssertEqual(1, kept.Beliefs.Count, "the sighting's belief is still there at 29 days");
        AssertEqual(DetectionSource.Seen, kept.Beliefs[0].LastSource, "unchanged by the stale mark");
        kept.Update(List(Mark("Corps A:1", 140f, 0f, 9000f, Today - 100)), Today + 30, Drift(10f), NoMarch);
        AssertEqual(0, kept.Beliefs.Count, "and it expires on its own clock at 30 days");
    }

    private static void ASightingNeverRetiresAMarkBelief()
    {
        var model = new EnemyArmyModel();
        model.Update(List(Mark("Old Name:1", 100f, 0f, 9000f, Today)), Today, Drift(10f), NoMarch);
        // Step 4 for a still-new key near the mark belief: the mark belief is not the retirement candidate.
        model.Update(List(Sighting("Other Group:9", 110f, 0f, 9000f)), Today + 1, Drift(10f), EnemyArmyModelMarch);
        AssertEqual(2, model.Beliefs.Count, "a sighting of another group does not retire a mark belief by plausibility");
        Single(model, "Old Name:1");
    }

    private static void AMarkBeliefReKeysWithinItsBaseRadius()
    {
        var model = new EnemyArmyModel();
        model.Update(List(Mark("Corps A:1", 100f, 0f, 9000f, Today, radius: 25f)), Today, Drift(10f), NoMarch);
        // The commander changed: the same name and type reappear under a new key. The plausible radius is a march of 0 here; only the
        // mark's own radius brings the sighting 20 units away into reach (step 3: PlausibleRadius + BaseRadius).
        model.Update(List(Sighting("Corps A:2", 120f, 0f, 9000f)), Today + 1, Drift(10f), NoMarch);
        AssertEqual(1, model.Beliefs.Count, "re-keyed within the mark's base radius, not duplicated");
        AssertEqual("Corps A:2", model.Beliefs[0].UnitKey, "under the new key");
        var far = new EnemyArmyModel();
        far.Update(List(Mark("Corps A:1", 100f, 0f, 9000f, Today, radius: 25f)), Today, Drift(10f), NoMarch);
        far.Update(List(Sighting("Corps A:2", 160f, 0f, 9000f)), Today + 1, Drift(10f), NoMarch);
        AssertEqual(2, far.Beliefs.Count, "beyond it a different group");
    }

    private static void MarkRadiusGrowsWithDrift()
    {
        var model = new EnemyArmyModel();
        model.Update(List(Mark("Corps A:1", 100f, 0f, 9000f, Today - 2, radius: 25f)), Today, Drift(10f), NoMarch);
        var belief = Single(model, "Corps A:1");
        AssertNear(25f, belief.BaseRadius, 0.001f, "the base radius is the mark's");
        AssertNear(45f, belief.UncertaintyRadius, 0.001f, "base + drift x days since the mark (2 x 10)");
        AssertNear(EnemyArmyModel.Confidence(2), belief.Confidence01, 0.0001f, "confidence decays from the mark day");
        AssertEqual(Today - 2, belief.LastSeenDay, "last seen on the mark's day");
        model.Update(new List<EnemyDetection>(), Today + 3, Drift(10f), NoMarch);
        AssertNear(25f + 50f, Single(model, "Corps A:1").UncertaintyRadius, 0.001f, "unseen, it keeps widening from the same base");
        model.Update(List(Sighting("Corps A:1", 100f, 0f, 9000f)), Today + 4, Drift(10f), NoMarch);
        AssertEqual(0f, Single(model, "Corps A:1").BaseRadius, "a sighting resets the base radius to 0");
    }

    private static void AMarkNeverWritesLastEmbarkedDay()
    {
        var model = new EnemyArmyModel();
        model.Update(List(Mark("Corps A:1", 100f, 0f, 9000f, Today, traits: EnemyTraits.Embarked | EnemyTraits.NearOwnCoast)), Today, Drift(10f), NoMarch);
        AssertEqual(-1, Single(model, "Corps A:1").LastEmbarkedDay, "no landing is ever inferred from a mark");
        var seenAfloat = new EnemyArmyModel();
        seenAfloat.Update(List(Sighting("Corps B:1", 100f, 0f, 9000f, traits: EnemyTraits.Embarked)), Today, Drift(10f), NoMarch);
        seenAfloat.Update(List(Mark("Corps B:1", 110f, 0f, 9000f, Today + 1, traits: EnemyTraits.OnOwnSoil)), Today + 1, Drift(10f), NoMarch);
        AssertEqual(Today, Single(seenAfloat, "Corps B:1").LastEmbarkedDay, "a mark leaves the day a sighting wrote");
    }

    private static void AnEmbarkedGroupsMarkIsAfloat()
    {
        var model = new EnemyArmyModel();
        model.Update(List(Mark("Corps A:1", 100f, 0f, 9000f, Today, traits: EnemyTraits.Embarked)), Today, Drift(10f), NoMarch);
        var belief = Single(model, "Corps A:1");
        AssertTrue((belief.Traits & EnemyTraits.Embarked) != 0, "the belief carries the Embarked trait");
        AssertTrue(ReserveRules.Afloat(belief), "so it is an afloat element, never land");
    }

    private static void ALandGroupsMarkCarriesNoEmbarkedTrait()
    {
        string runtime = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/EnemyDetectionRuntime.cs");
        string body = BodyOf(runtime, "private static EnemyDetection FillMark(");
        AssertContains(body, "TraitsOf(", "the traits come from the same reader as a sighting's, at the mark position");
        AssertContains(BodyOf(runtime, "private static EnemyTraits TraitsOf("), "SnapshotRules.IsEmbarked(", "Embarked exactly when the marked group is embarked");
        var model = new EnemyArmyModel();
        model.Update(List(Mark("Corps A:1", 100f, 0f, 9000f, Today, traits: EnemyTraits.OnOwnSoil)), Today, Drift(10f), NoMarch);
        AssertFalse(ReserveRules.Afloat(Single(model, "Corps A:1")), "a land group's mark is a land element");
    }

    private static void AMarkReadsTheLiveStrengthEachReview()
    {
        string runtime = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/EnemyDetectionRuntime.cs");
        string body = BodyOf(runtime, "private static EnemyDetection FillMark(");
        AssertContains(body, "MarkRules.BandRound(group.groupstrengthdirect, GamePrefs.approximationvalues)", "the live strength, in vanilla's band");
        AssertFalse(body.Contains("Strength = mark.Strength") , "never a strength stored with the mark (vanilla stores none, 143060)");
        var model = new EnemyArmyModel();
        model.Update(List(Mark("Corps A:1", 100f, 0f, 9000f, Today)), Today, Drift(10f), NoMarch);
        model.Update(List(Mark("Corps A:1", 100f, 0f, 6500f, Today)), Today + 1, Drift(10f), NoMarch);
        AssertNear(6500f, Single(model, "Corps A:1").Strength, 0.001f, "the next review's reading replaces it");
    }

    // ---- rows ----

    private static void MarksCountAppliedMarks()
    {
        var model = new EnemyArmyModel();
        model.Update(List(Sighting("Corps A:1", 100f, 0f, 9000f), Mark("Corps B:1", 300f, 0f, 9000f, Today), Mark("Corps C:1", 500f, 0f, 9000f, Today - EnemyArmyModel.ExpireDays)), Today, Drift(10f), NoMarch);
        AssertEqual(1, model.AppliedMarkKeys.Count, "one mark applied: the stale one is ignored");
        AssertEqual("Corps B:1", model.AppliedMarkKeys[0], "named by its key");
        model.Update(new List<EnemyDetection>(), Today + 1, Drift(10f), NoMarch);
        AssertEqual(0, model.AppliedMarkKeys.Count, "the list is per review");
    }

    private static void RememberedExcludesAppliedMarks()
    {
        var model = new EnemyArmyModel();
        model.Update(List(Sighting("Seen:1", 100f, 0f, 9000f), Sighting("Carried:1", 900f, 0f, 9000f)), Today - 1, Drift(10f), NoMarch);
        model.Update(List(Sighting("Seen:1", 100f, 0f, 9000f), Mark("Marked:1", 300f, 0f, 9000f, Today - 1)), Today, Drift(10f), NoMarch);
        AssertEqual(3, model.Beliefs.Count, "a sighting, a mark belief and a carried belief");
        AssertEqual(1, model.RememberedCount(Today), "only the carried belief is remembered: the mark belief was refreshed by a mark this review");
        model.Update(List(Sighting("Seen:1", 100f, 0f, 9000f)), Today + 1, Drift(10f), NoMarch);
        AssertEqual(2, model.RememberedCount(Today + 1), "next review, with no mark read, the mark belief is remembered too");
        string report = BodyOf(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/EnemyDetectionRuntime.cs"), "internal static void Report(");
        AssertContains(report, "model.RememberedCount(s.DaySerial)", "the report counts through the model");
        AssertContains(report, "\" marks=\" + model.AppliedMarkKeys.Count", "and prints the applied marks");
        int contact = report.IndexOf(" contact=", StringComparison.Ordinal);
        int marks = report.IndexOf(" marks=", StringComparison.Ordinal);
        AssertTrue(contact >= 0 && marks > contact, "marks= comes after seen remembered expired contact");
    }

    private static void SeenCountExcludesMarks()
    {
        var snapshot = new CampaignSnapshot();
        snapshot.Reset(1, 1, Today, PlayerCommandClass.NotCareer, -1, null);
        snapshot.Detections.Add(Sighting("A:1", 0f, 0f, 1000f));
        snapshot.Detections.Add(Sighting("B:1", 0f, 0f, 1000f, source: DetectionSource.Contact));
        snapshot.Detections.Add(Mark("C:1", 0f, 0f, 1000f, Today));
        snapshot.Detections.Add(Mark("D:1", 0f, 0f, 1000f, Today));
        AssertEqual(2, DetectionRules.SeenCount(snapshot.Detections), "the seen count is the non-mark detections");
        string report = BodyOf(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/EnemyDetectionRuntime.cs"), "internal static void Report(");
        AssertContains(report, "DetectionRules.SeenCount(s.Detections)", "[EnemyBeliefs] seen= counts the non-mark detections");
    }

    private static void PictureRowCountsSeenAndMarks()
    {
        var s = EnemyPictureTests.Csa(100000f);
        s.DetectionsSeen = 3;
        s.DetectionsMarked = 11;
        var picture = EnemyPicture.Build(s, (x, z) => false);
        AssertEqual(3, picture.Seen, "seen");
        AssertEqual(11, picture.Marks, "marks");
        string row = EnemyPicture.Row(picture, s);
        AssertContains(row, " seen=3 marks=11 ", "the row prints both");
    }

    // ---- the readers ----

    private static void MarkBeliefNeverScouts()
    {
        var s = EnemyPictureTests.Csa(100000f);
        var belief = EnemyPictureTests.Belief(1100f, -555f, 9000f);
        belief.LastSource = DetectionSource.Mark;
        s.Beliefs.Add(belief);
        var inputs = new TheaterPlanner.Inputs { S = s, Sectors = SectorModel.Build(s), Speed = ExecutorFixtures.Speed(), State = new PlannerState() };
        AssertFalse(TheaterPlanner.IsScouted(inputs, 1107f, -555f), "a mark belief within the relocation range scouts nothing");
        s.Beliefs[0] = EnemyPictureTests.Belief(1100f, -555f, 9000f);
        AssertTrue(TheaterPlanner.IsScouted(inputs, 1107f, -555f), "control: the same belief as a sighting does");
    }

    private static void MarkBeliefCountsInTheTotals()
    {
        var s = EnemyPictureTests.Csa(100000f);
        var belief = EnemyPictureTests.Belief(1100f, -555f, 9000f);
        belief.LastSource = DetectionSource.Mark;
        s.Beliefs.Add(belief);
        var picture = EnemyPicture.Build(s, (x, z) => false);
        AssertNear(9000f, picture.Believed, 0.5f, "the believed layer counts the mark's men");
        AssertNear(91000f, picture.Unaccounted, 0.5f, "and they are subtracted from the unaccounted pool");
        AssertNear(9000f, EnemyBeliefQuery.StrengthWithin(s.Beliefs, 1100f, -555f, 50f), 0.5f, "every strength query counts it");
    }

    private static void AMarkNeverLandsAGroup()
    {
        var landed = new EnemyBelief { Confidence01 = 1f, LastSeenDay = Today, LastSource = DetectionSource.Seen, Traits = EnemyTraits.OnOwnSoil, LastEmbarkedDay = Today - 1 };
        AssertTrue(EnemyBeliefReadings.IsSeaInvasionThreat(in landed, Today, out bool landing) && landing, "control: a group seen ashore after being seen afloat is a landing");
        var marked = landed;
        marked.LastSource = DetectionSource.Mark;
        AssertFalse(EnemyBeliefReadings.IsSeaInvasionThreat(in marked, Today, out bool markedLanding) && markedLanding, "a mark never raises a landing");
        var afloat = new EnemyBelief { Confidence01 = 1f, LastSeenDay = Today, LastSource = DetectionSource.Mark, Traits = EnemyTraits.Embarked | EnemyTraits.NearOwnCoast };
        AssertTrue(EnemyBeliefReadings.IsSeaInvasionThreat(in afloat, Today, out bool watchLanded) && !watchLanded, "an embarked mark near the coast may still raise the watch, as an afloat sighting does");
    }

    private static void AMarkIsNoRaid()
    {
        var raid = new EnemyBelief { Confidence01 = 1f, LastSeenDay = Today, LastSource = DetectionSource.Seen, Traits = EnemyTraits.CavalryMajority | EnemyTraits.OnOwnSoil };
        AssertTrue(EnemyBeliefReadings.IsRaidThreat(in raid, Today), "control: cavalry seen on own soil is a raid");
        raid.LastSource = DetectionSource.Mark;
        AssertFalse(EnemyBeliefReadings.IsRaidThreat(in raid, Today), "a mark raises no raid");
    }

    // ---- wiring pins ----

    // CampaignSeed is initialised before Detect on a campaign's first review and passed to it, so the first review's mark rolls are seeded.
    private static void TheSeedIsSetBeforeDetect()
    {
        string review = BodyOf(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs"), "internal static void ReviewPlan(");
        int seed = review.IndexOf("ComputeCampaignSeed(", StringComparison.Ordinal);
        int detect = review.IndexOf("EnemyDetectionRuntime.Detect(", StringComparison.Ordinal);
        AssertTrue(seed >= 0 && detect >= 0 && seed < detect, "the seed is computed before Detect");
        AssertContains(review, "EnemyDetectionRuntime.Detect(snapshot, GameVars.playeralliance, EnemyDetectionRuntime.FogHonestFlagsOn(), States[alliance].CampaignSeed)", "and handed to it");
        AssertEqual(1, review.Split(new[] { "ComputeCampaignSeed(" }, StringSplitOptions.None).Length - 1, "computed in one place");
    }

    private static void TheMarkFlagIsBoundOnAndReadByTheRuntime()
    {
        string plugin = ReadRepoFile("src/WhiskeyRealism/Plugin.cs");
        AssertContains(plugin, "\"Campaign Planner\", \"Enable Spotting Mark Beliefs\", true,", "bound in the planner section, default on");
        string runtime = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/EnemyDetectionRuntime.cs");
        AssertContains(runtime, "FlagOn(Plugin.EnableSpottingMarkBeliefs)", "the runtime reads it");
    }

    private static string CodeOnlyLines(string src)
    {
        var lines = new List<string>();
        foreach (string line in src.Split('\n'))
            if (!line.TrimStart().StartsWith("//", StringComparison.Ordinal)) lines.Add(line);
        return string.Join("\n", lines);
    }
}
