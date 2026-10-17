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
            ("DetectionRecordsSightedWeaponQuality", DetectionRecordsSightedWeaponQuality),
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







}
