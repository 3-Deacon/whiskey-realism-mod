using System;
using WhiskeyRealism.Tactical.Battle;
using static TestHarness;
using static TestFixtures;

// BC16 (docs/superpowers/plans/2026-10-15-battle-core.md § BC16): formations, slots and lanes. This file holds step 0 only, the record-only
// observers (decision 9 and decision 2's tug-of-war row); steps 1 and 2 add their tests here after step 0's in-game results are in.
//   (this file)  LayoutMirror (the corps-level mirror classifier, its per-call tally and row budget) and FormationFlipDetector
//   .Jq          the verdict program tools/bc16-mirror-verdict.jq over the synthetic sidecars in data/bc16-mirror
//   .Runtime     source pins for LayoutObserverPatch, the telemetry routes and the flag text
internal static partial class FormationSlotTests
{
    public static void Run()
    {
        RunAll(Join(MirrorTests(), FlipTests(), JqTests(), RuntimeTests()));
    }

    private static (string, Action)[] Join(params (string, Action)[][] parts)
    {
        int n = 0;
        for (int i = 0; i < parts.Length; i++) n += parts[i].Length;
        var all = new (string, Action)[n];
        int at = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i].CopyTo(all, at);
            at += parts[i].Length;
        }
        return all;
    }

    private static (string, Action)[] MirrorTests()
    {
        return new (string, Action)[]
        {
            ("bc16 MIRROR the lateral axis is the right of the order's facing, as vanilla's placement lays a level-1/2 column", LateralMatchesVanillaPlacement),
            ("bc16 MIRROR a row whose lateral order follows its columns agrees", RowFollowingColumnsAgrees),
            ("bc16 MIRROR a row whose lateral order runs against its columns is reversed", RowAgainstColumnsIsReversed),
            ("bc16 MIRROR two rows are compared each within itself, never across", RowsAreComparedWithinThemselves),
            ("bc16 MIRROR a brigade whose rows disagree with each other is mixed", DisagreeingRowsAreMixed),
            ("bc16 MIRROR a brigade with no row of two units says nothing", SingletonRowsAreUninformative),
            ("bc16 MIRROR a pair closer than a metre is no evidence", ClosePairIsNoEvidence),
            ("bc16 MIRROR a vote tie says nothing", TieIsUninformative),
            ("bc16 MIRROR the order of the arrays does not matter, only the columns", ArrayOrderIsIrrelevant),
            ("bc16 MIRROR a call counts brigades per depth and never an uninformative one", CallTalliesPerDepth),
            ("bc16 MIRROR a depth outside 1-3 is not counted", CallIgnoresOtherDepths),
            ("bc16 MIRROR the row budget is per depth, so rare corps orders are not crowded out", BudgetIsPerDepth),
            ("bc16 MIRROR the budget resets with the battle", BudgetResets),
            ("bc16 MIRROR classification, tally and budget allocate nothing after warm-up", MirrorAllocatesNothing),
        };
    }

    // ---- the axis -------------------------------------------------------------------------------------------------

    // BattlefieldSetup.GetAngleTerrainPosXYStatic (decompile 26937-26980), restated without the terrain clamp: with a = angle - 90 degrees,
    //   x' = x - cos(a) * distancez - cos(a + 90) * distancex      z' = z + sin(a) * distancez + sin(a + 90) * distancex.
    private static (float x, float z) VanillaPlace(float x, float z, float angleDegrees, float distancex, float distancez)
    {
        double a = (angleDegrees - 90.0) * Math.PI / 180.0;
        double b = angleDegrees * Math.PI / 180.0;
        return ((float)(x - Math.Cos(a) * distancez - Math.Cos(b) * distancex), (float)(z + Math.Sin(a) * distancez + Math.Sin(b) * distancex));
    }

    private static void LateralMatchesVanillaPlacement()
    {
        foreach (float facing in new[] { 0f, 37f, 90f, 180f, 271.5f })
        {
            const float originX = 812f, originZ = -1340f;
            float baseLateral = LayoutMirror.Lateral(originX, originZ, facing);
            foreach (float offset in new[] { -90f, -12.5f, 0f, 30f, 140f })
            {
                // Levels 1 and 2 pass distancex = -offset.x (92086, 92126): the unit with the larger offset.x stands further right.
                (float px, float pz) = VanillaPlace(originX, originZ, facing, -offset, 55f);
                AssertNear(offset, LayoutMirror.Lateral(px, pz, facing) - baseLateral, 0.01f, "level 1-2 lateral at facing " + facing + " offset " + offset);
                // Level 3 passes distancex = +offset.x (92176): the same offset stands the same distance to the LEFT.
                (float qx, float qz) = VanillaPlace(originX, originZ, facing, offset, 55f);
                AssertNear(-offset, LayoutMirror.Lateral(qx, qz, facing) - baseLateral, 0.01f, "level 3 lateral at facing " + facing + " offset " + offset);
            }
        }
    }

    // ---- the classifier -------------------------------------------------------------------------------------------

    private static MirrorVerdict Classify(int[] row, int[] column, float[] lateral)
        => LayoutMirror.Classify(row, column, lateral, row.Length);

    private static void RowFollowingColumnsAgrees()
    {
        AssertEqual(MirrorVerdict.Agree, Classify(new[] { 0, 0, 0, 0 }, new[] { 0, 1, 2, 3 }, new[] { -150f, -50f, 50f, 150f }), "left to right");
    }

    private static void RowAgainstColumnsIsReversed()
    {
        AssertEqual(MirrorVerdict.Reversed, Classify(new[] { 0, 0, 0, 0 }, new[] { 0, 1, 2, 3 }, new[] { 150f, 50f, -50f, -150f }), "right to left");
    }

    private static void RowsAreComparedWithinThemselves()
    {
        // A double line: the second row stands far to the left of the first, which says nothing about either row's own order.
        // Columns run on across the rows (0 1 | 2 3), so every cross-row pair looks reversed here: a classifier that compared across rows would
        // out-vote each row's own order four to one.
        var row = new[] { 0, 0, 1, 1 };
        var column = new[] { 0, 1, 2, 3 };
        AssertEqual(MirrorVerdict.Agree, Classify(row, column, new[] { 400f, 500f, -500f, -400f }), "both rows left to right");
        AssertEqual(MirrorVerdict.Reversed, Classify(row, column, new[] { 500f, 400f, -400f, -500f }), "both rows right to left");
    }

    private static void DisagreeingRowsAreMixed()
    {
        AssertEqual(MirrorVerdict.Mixed, Classify(new[] { 0, 0, 1, 1 }, new[] { 0, 1, 0, 1 }, new[] { -50f, 50f, 50f, -50f }), "one row each way");
    }

    private static void SingletonRowsAreUninformative()
    {
        AssertEqual(MirrorVerdict.Uninformative, Classify(new[] { 0, 1, 2 }, new[] { 0, 0, 0 }, new[] { 0f, 100f, 200f }), "one unit to a row");
        AssertEqual(MirrorVerdict.Uninformative, LayoutMirror.Classify(Array.Empty<int>(), Array.Empty<int>(), Array.Empty<float>(), 0), "no units");
        AssertEqual(MirrorVerdict.Uninformative, Classify(new[] { 0 }, new[] { 0 }, new[] { 12f }), "a lone regiment");
    }

    private static void ClosePairIsNoEvidence()
    {
        AssertEqual(MirrorVerdict.Uninformative, Classify(new[] { 0, 0 }, new[] { 0, 1 }, new[] { 10f, 10.5f }), "half a metre apart");
        AssertEqual(MirrorVerdict.Agree, Classify(new[] { 0, 0 }, new[] { 0, 1 }, new[] { 10f, 11.5f }), "a metre and a half apart");
    }

    private static void TieIsUninformative()
    {
        // Three in a row, one pair each way and one pair too close to count: the votes tie 1-1.
        AssertEqual(MirrorVerdict.Uninformative, Classify(new[] { 0, 0, 0 }, new[] { 0, 1, 2 }, new[] { 0f, 100f, 0.2f }), "a tie");
    }

    private static void ArrayOrderIsIrrelevant()
    {
        // The arrays follow the group's attachedunit order, which is not the column order.
        AssertEqual(MirrorVerdict.Agree, Classify(new[] { 0, 0, 0 }, new[] { 2, 0, 1 }, new[] { 100f, -100f, 0f }), "shuffled");
        AssertEqual(MirrorVerdict.Reversed, Classify(new[] { 0, 0, 0 }, new[] { 2, 0, 1 }, new[] { -100f, 100f, 0f }), "shuffled and reversed");
    }

    // ---- the per-call tally and the budget ------------------------------------------------------------------------

    private static void CallTalliesPerDepth()
    {
        var call = new LayoutMirrorCall();
        call.Add(2, MirrorVerdict.Agree);
        call.Add(2, MirrorVerdict.Agree);
        call.Add(2, MirrorVerdict.Reversed);
        call.Add(2, MirrorVerdict.Mixed);
        call.Add(2, MirrorVerdict.Uninformative);
        call.Add(3, MirrorVerdict.Reversed);
        AssertEqual(4, call.Brigades(2), "depth 2 brigades: mixed counts, uninformative does not");
        AssertEqual(2, call.Agree(2), "depth 2 agree");
        AssertEqual(1, call.Reversed(2), "depth 2 reversed");
        AssertEqual(1, call.Brigades(3), "depth 3 brigades");
        AssertEqual(1, call.Reversed(3), "depth 3 reversed");
        AssertEqual(0, call.Brigades(1), "depth 1 untouched");
        call.Clear();
        AssertEqual(0, call.Brigades(2) + call.Brigades(3), "cleared");
    }

    private static void CallIgnoresOtherDepths()
    {
        var call = new LayoutMirrorCall();
        call.Add(0, MirrorVerdict.Agree);
        call.Add(4, MirrorVerdict.Agree);
        call.Add(-1, MirrorVerdict.Reversed);
        AssertEqual(0, call.Brigades(0) + call.Brigades(1) + call.Brigades(2) + call.Brigades(3), "nothing counted");
        AssertEqual(0, call.Brigades(4), "reading outside 1-3 is zero, not an exception");
    }

    private static void BudgetIsPerDepth()
    {
        AssertEqual(128, LayoutMirrorBudget.MaxRowsPerDepth, "the cap the build record states: 10 rows per depth are needed, so 128 leaves room");
        var budget = new LayoutMirrorBudget();
        for (int i = 0; i < LayoutMirrorBudget.MaxRowsPerDepth; i++) AssertTrue(budget.TryTake(1), "depth 1 row " + i);
        AssertFalse(budget.TryTake(1), "depth 1 is full");
        AssertTrue(budget.TryTake(3), "depth 3 still has its own budget");
        AssertTrue(budget.TryTake(2), "and so has depth 2");
        AssertFalse(budget.TryTake(0), "depth 0 has none");
        AssertFalse(budget.TryTake(4), "nor has depth 4");
        AssertEqual(LayoutMirrorBudget.MaxRowsPerDepth, budget.Taken(1), "taken at depth 1");
    }

    private static void BudgetResets()
    {
        var budget = new LayoutMirrorBudget();
        for (int i = 0; i < LayoutMirrorBudget.MaxRowsPerDepth; i++) budget.TryTake(2);
        budget.Reset();
        AssertEqual(0, budget.Taken(2), "reset");
        AssertTrue(budget.TryTake(2), "and takes again");
    }

    private static void MirrorAllocatesNothing()
    {
        var call = new LayoutMirrorCall();
        var budget = new LayoutMirrorBudget();
        int[] row = { 0, 0, 0, 1, 1, 1 };
        int[] column = { 0, 1, 2, 0, 1, 2 };
        float[] lateral = { -100f, 0f, 100f, -90f, 5f, 95f };
        MirrorVerdict v = LayoutMirror.Classify(row, column, lateral, row.Length);
        call.Add(1, v); budget.TryTake(1); call.Clear(); budget.Reset(); LayoutMirror.Lateral(1f, 2f, 3f);

        long mark = BeginAllocationWindow();
        for (int i = 0; i < 1000; i++)
        {
            v = LayoutMirror.Classify(row, column, lateral, row.Length);
            call.Add(1 + (i % 3), v);
            budget.TryTake(1 + (i % 3));
            LayoutMirror.Lateral(i, -i, i);
            call.Clear();
            budget.Reset();
        }
        AssertEqual(0L, AllocatedSince(mark), "bytes allocated by 1000 classifications");
    }
}
