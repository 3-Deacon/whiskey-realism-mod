using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;

// Smoke 2026-10-17: within one plan the rect assignment rotated every few seconds (GenericMethodical 18:20:46-18:22:35, MobileFlankMarch
// 18:20:10-18:20:59: the corps marching the flank became the Center, the Center became the flank march), because PlayCorpsAssignment pairs
// each rect with the closest free corps and has no memory. A division then got a new rect, and a new rect sends it elsewhere, often behind it
// ("5th Pennsylvania walking backwards toward the enemy"). A corps keeps its rect while the template stands.
internal static class PlayAssignmentIncumbencyTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("play assignment a corps keeps its rect when another corps comes closer", IncumbentsKeepTheirRects),
            ("play assignment with no incumbents it is the closest-corps assignment", NoIncumbentsIsTheMemorylessAssignment),
            ("play assignment a vacated rect goes to the closest free corps", AVacatedRectGoesToTheClosestFreeCorps),
            ("play assignment a rect whose kind changed (another template) has no incumbent", AChangedTemplateHasNoIncumbent),
            ("play assignment a child holding player-owned strength is not kept in the focal assault's column", APlayerOwnedIncumbentIsNotKeptInTheColumn),
            ("play assignment one minute of marching under the same enemy keeps every corps in one rect", AMinuteOfMarchingKeepsOneRectPerCorps),
        });
    }

    private static readonly TerrainLine NoCrest = default(TerrainLine);

    private static WorldPlayRect[] Rects(BattlePlanId plan, float armyX = 0f, float armyZ = 0f, bool mirrored = false)
    {
        PlayTemplate template = PlayTemplateCatalog.Lookup(plan);
        var projected = PlayProjection.Project(template, armyX, armyZ, armyX, armyZ + 600f, 600f, in NoCrest, mirrored);
        var rects = new WorldPlayRect[projected.Count];
        for (int i = 0; i < rects.Length; i++) rects[i] = projected[i];
        return rects;
    }

    private static List<CorpsPosition> Corps(params (string id, float x, float z)[] at)
    {
        var list = new List<CorpsPosition>();
        foreach (var c in at) list.Add(new CorpsPosition(c.id, c.x, c.z));
        return list;
    }

    private static string RectOf(IReadOnlyList<CorpsAssignment> a, string childId)
    {
        for (int i = 0; i < a.Count; i++) if (a[i].ChildId == childId) return a[i].RectKind + "#" + a[i].RectIndex;
        return "none";
    }

    private static CorpsAssignment[] Assign(IReadOnlyList<WorldPlayRect> rects, IReadOnlyList<CorpsPosition> corps, IReadOnlyList<CorpsAssignment> previous, out int n)
    {
        var into = new CorpsAssignment[rects.Count];
        n = PlayCorpsAssignment.AssignInto(rects, corps, Array.Empty<ObjectivePosition>(), previous, into);
        var result = new CorpsAssignment[n];
        Array.Copy(into, result, n);
        return result;
    }

    private static void IncumbentsKeepTheirRects()
    {
        var rects = Rects(BattlePlanId.ManeuverAndFix);
        var corps = Corps(("a", -400f, 0f), ("b", -200f, 0f), ("c", 0f, 0f), ("d", 200f, 0f), ("e", 400f, 0f));
        CorpsAssignment[] first = Assign(rects, corps, null, out int n1);
        AssertTrue(n1 >= 3, "the template assigned corps");
        // Two corps trade places: each is now the closest to the other's rect, so the memoryless pairing swaps them.
        string x = first[0].ChildId, y = first[1].ChildId;
        var swapped = new List<CorpsPosition>();
        for (int i = 0; i < corps.Count; i++)
        {
            CorpsPosition p = corps[i];
            CorpsPosition px = corps.Find(q => q.ChildId == x), py = corps.Find(q => q.ChildId == y);
            if (p.ChildId == x) p = new CorpsPosition(x, py.WorldX, py.WorldZ);
            else if (p.ChildId == y) p = new CorpsPosition(y, px.WorldX, px.WorldZ);
            swapped.Add(p);
        }
        CorpsAssignment[] memoryless = Assign(rects, swapped, null, out _);
        AssertTrue(RectOf(memoryless, x) != RectOf(first, x) || RectOf(memoryless, y) != RectOf(first, y), "the fixture really makes the closest pairing change");
        CorpsAssignment[] kept = Assign(rects, swapped, first, out _);
        for (int i = 0; i < first.Length; i++)
            AssertEqual(RectOf(first, first[i].ChildId), RectOf(kept, first[i].ChildId), first[i].ChildId + " keeps its rect");
    }

    private static void NoIncumbentsIsTheMemorylessAssignment()
    {
        var rects = Rects(BattlePlanId.ManeuverAndFix);
        var corps = Corps(("a", -400f, 0f), ("b", -200f, 0f), ("c", 0f, 0f), ("d", 200f, 0f));
        var expected = PlayCorpsAssignment.Assign(rects, corps, Array.Empty<ObjectivePosition>());
        CorpsAssignment[] none = Assign(rects, corps, null, out int n);
        CorpsAssignment[] empty = Assign(rects, corps, Array.Empty<CorpsAssignment>(), out int m);
        AssertEqual(expected.Count, n, "same count with null");
        AssertEqual(expected.Count, m, "same count with an empty memory");
        for (int i = 0; i < expected.Count; i++)
        {
            AssertEqual(expected[i].ChildId, none[i].ChildId, "same child " + i);
            AssertEqual(expected[i].ChildId, empty[i].ChildId, "same child (empty) " + i);
        }
    }

    private static void AVacatedRectGoesToTheClosestFreeCorps()
    {
        var rects = Rects(BattlePlanId.ManeuverAndFix);
        var corps = Corps(("a", -400f, 0f), ("b", -200f, 0f), ("c", 0f, 0f), ("d", 200f, 0f), ("e", 400f, 0f));
        CorpsAssignment[] first = Assign(rects, corps, null, out _);
        string gone = first[0].ChildId;
        corps.RemoveAll(c => c.ChildId == gone);
        CorpsAssignment[] after = Assign(rects, corps, first, out _);
        AssertEqual("none", RectOf(after, gone), "the routed corps holds nothing");
        for (int i = 1; i < first.Length; i++)
            AssertEqual(RectOf(first, first[i].ChildId), RectOf(after, first[i].ChildId), first[i].ChildId + " keeps its rect");
        bool vacatedFilled = false;
        for (int i = 0; i < after.Length; i++) if (after[i].RectIndex == first[0].RectIndex) vacatedFilled = true;
        AssertTrue(vacatedFilled, "the vacated rect went to a free corps");
    }

    private static void AChangedTemplateHasNoIncumbent()
    {
        var corps = Corps(("a", -400f, 0f), ("b", -200f, 0f), ("c", 0f, 0f), ("d", 200f, 0f), ("e", 400f, 0f));
        CorpsAssignment[] flank = Assign(Rects(BattlePlanId.ManeuverAndFix), corps, null, out _);
        var other = Rects(BattlePlanId.FrontalShockAssault);
        CorpsAssignment[] viaMemory = Assign(other, corps, flank, out _);
        var memoryless = PlayCorpsAssignment.Assign(other, corps, Array.Empty<ObjectivePosition>());
        for (int i = 0; i < memoryless.Count; i++)
            AssertEqual(memoryless[i].ChildId, viaMemory[i].ChildId, "another template pairs by distance alone " + i);
    }

    private static void APlayerOwnedIncumbentIsNotKeptInTheColumn()
    {
        var rects = Rects(BattlePlanId.FocalAssault);
        int column = -1;
        for (int i = 0; i < rects.Length; i++) if (rects[i].Kind == PlayRectKind.AssaultColumn) column = i;
        AssertTrue(column >= 0, "the focal assault has a column");
        var previous = new[] { new CorpsAssignment("held", PlayRectKind.AssaultColumn, column, rects[column].AnchorX, rects[column].AnchorZ, 0f) };
        var corps = new List<CorpsPosition> { new CorpsPosition("held", rects[column].AnchorX, rects[column].AnchorZ, containsPlayerOwned: true), new CorpsPosition("free", 0f, 0f) };
        CorpsAssignment[] after = Assign(rects, corps, previous, out _);
        AssertTrue(RectOf(after, "held") != "AssaultColumn#" + column, "a child that holds player-owned strength never takes the column");
    }

    // The smoke's minute, as a corps level fixture: the same enemy, the same template, every corps marches a few hundred metres (the flank
    // corps toward the enemy's side, the others shuffling), and the pairing the closest rule would make changes four times in the minute.
    private static void AMinuteOfMarchingKeepsOneRectPerCorps()
    {
        var corps = new List<(string id, float x, float z)>
        {
            ("m706558", -400f, 0f), ("m707212", -200f, 0f), ("m707866", 0f, 0f), ("m708520", 200f, 0f), ("m709392", 400f, 0f),
        };
        CorpsAssignment[] held = null;
        var rectsByStep = new List<string>();
        var rng = new Random(17);
        int memorylessChanges = 0;
        CorpsAssignment[] memorylessPrev = null;
        for (int step = 0; step < 12; step++)
        {
            // 5-second steps: each corps drifts up to 40 m in x and z; the army centroid, hence the rects, drift with them.
            for (int i = 0; i < corps.Count; i++)
                corps[i] = (corps[i].id, corps[i].x + (float)(rng.NextDouble() * 80 - 40), corps[i].z + (float)(rng.NextDouble() * 80 - 40));
            float cx = 0f, cz = 0f;
            foreach (var c in corps) { cx += c.x / corps.Count; cz += c.z / corps.Count; }
            var rects = Rects(BattlePlanId.MobileFlankMarch, cx, cz);
            var positions = Corps(corps.ToArray());
            CorpsAssignment[] memoryless = Assign(rects, positions, null, out _);
            if (memorylessPrev != null)
                for (int i = 0; i < memoryless.Length; i++) if (RectOf(memoryless, memoryless[i].ChildId) != RectOf(memorylessPrev, memoryless[i].ChildId)) { memorylessChanges++; break; }
            memorylessPrev = memoryless;
            CorpsAssignment[] now = Assign(rects, positions, held, out _);
            if (held != null)
                for (int i = 0; i < held.Length; i++)
                    AssertEqual(RectOf(held, held[i].ChildId), RectOf(now, held[i].ChildId), "step " + step + ": " + held[i].ChildId + " stays in its rect");
            held = now;
        }
        AssertTrue(memorylessChanges >= 1, "the closest-corps rule alone would have moved a corps between rects in this minute (" + memorylessChanges + " times)");
    }
}
