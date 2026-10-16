using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;

using static TestHarness;

// The smoke of 2026-10-16 (docs/superpowers/specs/2026-10-14-campaign-planner-design.md § Smoke amendments 2026-10-16): the planner
// marched an unscouted mass at infinite odds. Fixtures are built from the smoke's numbers (the Union at 1861-06-02: 15 formations,
// 3 busy, 10 massed at 67669, objective 31 twice and 29, odds=inf/0.82 on every candidate, no belief near any target).
internal static class ScoutFirstTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("LowReadinessFormationsAreNeverMassed", LowReadinessFormationsAreNeverMassed),
            ("GaveUpFlipChainNeedsTheMassToBeRefusedFirst", GaveUpFlipChainNeedsTheMassToBeRefusedFirst),
        });
    }

    // ---- fixtures ----

    // Eleven areas (x 0..1000, relocation range 150), the own capital at x 0, own IIPs every 100 to x 600, enemy objectives 31 at
    // x 800 and 29 at x 900 (the smoke's candidate shape), and `count` corps from x 100 on, 40 apart, 6700+ strong, ready, 0.9 morale.
    // x <= 600 is the West theater and x > 600 the East theater (TheaterClassifier.FromPosition), so a corps at x 700 is native to
    // the targets and one at x 300 is foreign.
    private static CampaignSnapshot Union(int count = 12, int readiness = 3)
    {
        var s = ExecutorFixtures.Line(0, 11);
        s.MinStrengthForMovement = 5000f;
        s.MinMoraleForMovement = 0.65f;
        for (int i = 1; i <= 6; i++) s.Supply.Add(ExecutorFixtures.Iip(600 + i, 100f * i, 0));
        s.Objectives.Add(Objective(31, 800f, 0.8f));
        s.Objectives.Add(Objective(29, 900f, 0.1f));
        for (int i = 0; i < count; i++) s.Own.Add(Corps(i + 1, 100f + 40f * i, 6700f + 100f * i, readiness));
        return s;
    }

    private static ObjectiveRecord Objective(int id, float x, float importance)
    {
        return new ObjectiveRecord { ObjectiveId = id, X = x, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = importance };
    }

    private static FormationRecord Corps(int id, float x, float strength, int readiness = 3)
    {
        var f = ExecutorFixtures.Corps(id, x, strength);
        f.SightRadius = 50f;
        f.Morale = 0.9f;
        f.ReadinessStep = readiness;
        return f;
    }

    private static TheaterPlan Plan(CampaignSnapshot s, PlannerState state = null, IReadOnlyList<GaveUpMove> gaveUp = null)
    {
        return TheaterPlanner.Plan(s, ExecutorFixtures.Sectors(s), null, state ?? new PlannerState(), null, null, ExecutorFixtures.Speed(), out _, gaveUp);
    }

    // ---- D5 / D4 root cause: readiness ----

    // SetWaypoint refuses a readiness step 0/1 formation's march onto enemy ground without a word (91336-91355) and vanilla's own
    // offensive gate asks step >= 2 (14091): such a formation is neither massed nor allocated a march.
    private static void LowReadinessFormationsAreNeverMassed()
    {
        var s = Union(12, readiness: 1);
        var plan = Plan(s);
        AssertEqual(0, plan.MassInstanceIds.Count, "no formation of readiness step 1 is massed");
        AssertEqual(-1, plan.MainObjectiveId, "no force that can march: no main effort");

        var mixed = Union(12, readiness: 3);
        for (int i = 0; i < 6; i++)
        {
            var f = mixed.Own[i];
            f.ReadinessStep = i % 2;
            mixed.Own[i] = f;
        }
        var mixedPlan = Plan(mixed);
        for (int i = 0; i < 6; i++)
            AssertFalse(mixedPlan.MassInstanceIds.Contains(mixed.Own[i].InstanceId), "formation " + mixed.Own[i].InstanceId + " (step " + mixed.Own[i].ReadinessStep + ") is not massed");
        AssertTrue(mixedPlan.MassInstanceIds.Count > 0, "the ready ones are");
    }

    // The smoke's chain: formations the tracker gave up on for objective 31 left its mass, so objective 29 (whose mass still counted
    // them) won; they gave up there too and the main effort ended at -1. The tracker's per-objective exclusion is by design (a refusal
    // follows the target's state); what started the chain was sending formations that vanilla silently refuses.
    private static void GaveUpFlipChainNeedsTheMassToBeRefusedFirst()
    {
        var s = Union(8, readiness: 3);
        var state = new PlannerState();
        var first = Plan(s, state);
        AssertEqual(31, first.MainObjectiveId, "day 1: objective 31");

        var gaveUp31 = new List<GaveUpMove>();
        for (int i = 0; i < s.Own.Count; i++) gaveUp31.Add(new GaveUpMove { InstanceId = s.Own[i].InstanceId, TargetX = 800f, TargetZ = 0f, ObjectiveId = 31 });
        var second = Plan(s, state, gaveUp31);
        AssertEqual(29, second.MainObjectiveId, "every formation gave up on 31: the other objective takes over (P8, by design)");

        var gaveUpBoth = new List<GaveUpMove>(gaveUp31);
        for (int i = 0; i < s.Own.Count; i++) gaveUpBoth.Add(new GaveUpMove { InstanceId = s.Own[i].InstanceId, TargetX = 900f, TargetZ = 0f, ObjectiveId = 29 });
        var third = Plan(s, state, gaveUpBoth);
        AssertEqual(-1, third.MainObjectiveId, "and on 29 too: no main effort (the smoke's -1)");
    }
}
