using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using WhiskeyRealism;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;
using static TestFixtures;

/// <summary>BC14O task 1: an objective's value from vanilla's own points, read live (requirement-objective-value).</summary>
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] ObjectiveValueTests() => new (string, Action)[]
    {
        ("bc14o OBJ-CAPTURE-CACHE the cached capture reads points live, so a resolved objective falls to the value floor", ObjCaptureCache),
        ("bc14o OBJ-CAPTURE-CACHE-PIN both capture builders read activeInHierarchy into LiveInHierarchy through the one reader", ObjCaptureCachePin),
        ("bc14o OBJ-VALUE value is 0.2 + 0.6 v for a held-by-no-one objective and 0.5625 of it for an own one, over the live objectives", ObjValue),
        ("bc14o OBJ-VALUE-OFF with the flag off the fixed 0.45 and 0.80 stand", ObjValueFlagOff),
    };

    static Objectives UninitialisedObjective(string name, int owner, int points)
    {
        var obj = (Objectives)RuntimeHelpers.GetUninitializedObject(typeof(Objectives));
        obj.objectivename = name;
        obj.owner = owner;
        obj.points = points;
        return obj;
    }

    // A capture through TacticalStableSceneCache's cached path from a real Objectives object. Populate itself cannot run here (an objective's
    // position is a native read), so the cache's fields are filled the way Populate fills them; the capture under test is the real one. The
    // native flags read false in the harness; the managed fields can be read, and points is the one vanilla zeroes mid-battle (105726-105735).
    static void FillSceneCache(Objectives[] refs)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        Type cache = typeof(TacticalStableSceneCache);
        var ids = new string[refs.Length];
        var points = new TacticalMapPoint[refs.Length];
        for (int i = 0; i < refs.Length; i++)
        {
            ids[i] = TacticalVisionRuntimeAdapter.MapObjectiveId(refs[i], i);
            points[i] = new TacticalMapPoint(100f + i, 200f);
        }
        cache.GetField("_objectiveRefs", flags).SetValue(null, refs);
        cache.GetField("_objectiveIds", flags).SetValue(null, ids);
        cache.GetField("_objectivePoints", flags).SetValue(null, points);
        cache.GetField("_populated", flags).SetValue(null, true);
    }

    static void ObjCaptureCache()
    {
        var cemetery = UninitialisedObjective("Cemetery Hill", 1, 70);
        var peach = UninitialisedObjective("Peach Orchard", -1, 70);
        try
        {
            FillSceneCache(new[] { cemetery, peach });
            AssertTrue(TacticalStableSceneCache.IsPopulated, "the cache is populated: the cached path is the one under test");
            MapObjectiveCapture[] first = TacticalStableSceneCache.GetOrCaptureMapObjectives();
            AssertEqual(2, first.Length, "both objectives are captured");
            AssertEqual("Cemetery Hill", first[0].ObjectiveId, "by the director's own id");
            AssertEqual(70, first[0].Points, "the cached capture carries the objective's current points");
            AssertEqual(1, first[0].Owner, "and its owner");

            cemetery.points = 0;   // a resolved Touch objective
            MapObjectiveCapture[] second = TacticalStableSceneCache.GetOrCaptureMapObjectives();
            AssertEqual(0, second[0].Points, "after vanilla zeroes the points the next capture carries the 0");
            AssertEqual(70, second[1].Points, "and the other objective keeps its own");
            float max = Math.Max(second[0].Points, second[1].Points);
            AssertNear(0.2f, ObjectiveScoring.Value(ObjectiveScoring.Normalised(second[0].Points, max), false), 0.0001f, "the resolved objective's v falls to the floor: value 0.2");
            AssertNear(0.8f, ObjectiveScoring.Value(ObjectiveScoring.Normalised(second[1].Points, max), false), 0.0001f, "beside the one that still has points");
        }
        finally { TacticalStableSceneCache.Clear(); }
    }

    // The native flag cannot be set in the harness, so the pin reads the source: one reader makes the capture for both builders, and it takes the
    // liveness vanilla itself tests (gameObject.activeInHierarchy, CheckIfObjectivesTaken 7377), not isActiveAndEnabled.
    static void ObjCaptureCachePin()
    {
        string adapter = ReadRepoFile("src/WhiskeyRealism/Tactical/Orchestrator/TacticalVisionRuntimeAdapter.cs");
        string cache = ReadRepoFile("src/WhiskeyRealism/Tactical/Orchestrator/TacticalStableSceneCache.cs");
        string reader = MethodBody(adapter, "internal static MapObjectiveCapture ReadObjectiveLive(");
        AssertTrue(reader != null, "the one reader is found");
        AssertTrue(System.Text.RegularExpressions.Regex.IsMatch(reader, @"live\s*=\s*IsActiveInHierarchy\(obj\)\s*;"), "LiveInHierarchy is read through the in-hierarchy test");
        AssertTrue(System.Text.RegularExpressions.Regex.IsMatch(reader, @"active\s*=\s*IsActiveAndEnabled\(obj\)\s*;"), "and IsActive keeps the shipped isActiveAndEnabled");
        AssertTrue(System.Text.RegularExpressions.Regex.IsMatch(adapter, @"bool IsActiveInHierarchy\(Objectives obj\)\s*=>\s*obj\.gameObject\.activeInHierarchy;"),
            "the in-hierarchy test is gameObject.activeInHierarchy");
        AssertTrue(cache.Contains("TacticalVisionRuntimeAdapter.ReadObjectiveLive("), "the scene cache's builder reads through the one reader");
        string scan = MethodBody(adapter, "internal static MapObjectiveCapture[] CaptureMapObjectivesLive(");
        AssertTrue(scan != null && scan.Contains("ReadObjectiveLive("), "and so does the live scan");
        AssertFalse(cache.Contains("obj.points") || cache.Contains("isActiveAndEnabled;"), "the cache builder keeps no read of its own to drift from the reader");
    }

    // The body of the method whose signature starts with <paramref name="signature"/>: from its opening brace to the matching close.
    static string MethodBody(string text, string signature)
    {
        int at = text.IndexOf(signature, StringComparison.Ordinal);
        if (at < 0) return null;
        int open = text.IndexOf('{', at);
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return text.Substring(open, i - open + 1);
        }
        return null;
    }

    static List<ObjectiveObservationInput> Observe(MapObjectiveCapture[] captures, int alliance)
    {
        var observations = new List<ObjectiveObservationInput>();
        TacticalVisionRuntimeAdapter.AddMapObjectiveObservations(captures, alliance, observations, new List<TacticalObjectiveStatus>(),
            new List<float>(), new List<float>(), new HashSet<string>(StringComparer.Ordinal));
        return observations;
    }

    static MapObjectiveCapture Capture(string id, int owner, int points, bool live = true, bool active = true)
        => new MapObjectiveCapture(id, new TacticalMapPoint(100f + id.Length, 200f), owner, active, points, live);

    static float ValueOf(List<ObjectiveObservationInput> observations, string id)
    {
        foreach (ObjectiveObservationInput o in observations) if (o.ObjectiveId == id) return o.Value;
        throw new InvalidOperationException("no observation " + id);
    }

    static void ObjValue()
    {
        // A 70-point objective the enemy holds, a 30-point neutral one, a 50-point one the side holds, a resolved Touch objective (points 0),
        // and a deactivated one with 200 points that is in neither the table nor the max.
        var captures = new[]
        {
            Capture("A", 1, 70), Capture("B", -1, 30), Capture("C", 0, 50), Capture("T", -1, 0),
            new MapObjectiveCapture("X", new TacticalMapPoint(50f, 50f), 1, false, 200, false),
        };
        AssertNear(70f, TacticalVisionRuntimeAdapter.MaxLivePoints(captures), 0.0001f, "the max is over the live objectives: the inactive 200 is not in it");
        List<ObjectiveObservationInput> seen = Observe(captures, 0);
        AssertEqual(4, seen.Count, "the deactivated objective is not scored");
        AssertNear(0.8f, ValueOf(seen, "A"), 0.0001f, "the highest-valued one keeps today's 0.80");
        AssertNear(0.2f + 0.6f * (30f / 70f), ValueOf(seen, "B"), 0.0001f, "0.2 + 0.6 v for a neutral one");
        AssertNear(0.5625f * (0.2f + 0.6f * (50f / 70f)), ValueOf(seen, "C"), 0.0001f, "0.5625 of that for an own one");
        AssertNear(0.2f, ValueOf(seen, "T"), 0.0001f, "a resolved Touch objective has v 0 and sits at the floor");
        AssertNear(0.2f, ValueOf(Observe(captures, 0), "T"), 0.0001f, "for both sides");
        AssertNear(0.2f, ValueOf(Observe(captures, 1), "T"), 0.0001f, "the other side too: points are public");

        var flat = new[] { Capture("P", 1, 0), Capture("Q", 0, 0) };
        AssertNear(0.2f + 0.6f * 0.5f, ValueOf(Observe(flat, 0), "P"), 0.0001f, "every live objective at 0 points: v is 0.5");
        AssertNear(0.5625f * (0.2f + 0.6f * 0.5f), ValueOf(Observe(flat, 0), "Q"), 0.0001f, "own one at 0.5 as well");

        AssertNear(0.5f, ObjectiveScoring.Normalised(5f, 0f), 0.0001f, "no max: 0.5");
        AssertNear(1f, ObjectiveScoring.Normalised(90f, 70f), 0.0001f, "clamped to 1");
    }

    static void ObjValueFlagOff()
    {
        bool before = Plugin.ObjectiveDoctrine;
        try
        {
            Plugin.ObjectiveDoctrine = false;
            var captures = new[] { Capture("A", 1, 70), Capture("B", -1, 30), Capture("C", 0, 50) };
            List<ObjectiveObservationInput> seen = Observe(captures, 0);
            AssertNear(0.80f, ValueOf(seen, "A"), 0.0001f, "enemy-held: today's 0.80");
            AssertNear(0.80f, ValueOf(seen, "B"), 0.0001f, "neutral: 0.80");
            AssertNear(0.45f, ValueOf(seen, "C"), 0.0001f, "own: 0.45");
        }
        finally { Plugin.ObjectiveDoctrine = before; }
    }
}
