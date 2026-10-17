using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;
using static TestFixtures;

/// <summary>
/// BC14 maneuver feasibility (docs/superpowers/specs/2026-10-15-battle-maneuver-feasibility-design.md § 13). One class for the
/// three slices: the core's cases are here, BC14W and BC14O add theirs through the two partial methods. Test names start with
/// the slice tag and the spec's test id (<c>bc14 FEAS-SCN-3 ...</c>) so <c>--only ManeuverFeasibilityTests "bc14 FEAS-SCN-3"</c>
/// runs one case.
/// </summary>
internal static partial class ManeuverFeasibilityTests
{
    public static void Run()
    {
        var all = new List<(string, Action)>();
        all.AddRange(CoreTests());
        AddWorksTests(all);      // ManeuverFeasibilityTests.Works.cs (BC14W)
        AddObjectiveTests(all);  // ManeuverFeasibilityTests.Objectives.cs (BC14O)
        RunAll(all.ToArray());
    }

    static partial void AddWorksTests(List<(string, Action)> into);
    static partial void AddObjectiveTests(List<(string, Action)> into);

    // The core's tests, one group per concern, each group in its own partial file (the CommanderOpportunityTests precedent).
    static (string, Action)[] CoreTests()
    {
        var all = new List<(string, Action)>();
        all.AddRange(StanceTests());
        all.AddRange(CopySiteTests());       // ManeuverFeasibilityTests.Copy.cs
        all.AddRange(ExtractionTests());     // ManeuverFeasibilityTests.Extraction.cs
        all.AddRange(RegionTests());         // ManeuverFeasibilityTests.Regions.cs
        all.AddRange(CrossingTests());       // ManeuverFeasibilityTests.Crossings.cs
        all.AddRange(RouteTests());          // ManeuverFeasibilityTests.Routes.cs
        all.AddRange(VerdictTests());        // ManeuverFeasibilityTests.Verdicts.cs
        all.AddRange(RiskTests());           // ManeuverFeasibilityTests.Risk.cs
        all.AddRange(SelectionTests());      // ManeuverFeasibilityTests.Selection.cs
        all.AddRange(ScenarioTests());       // ManeuverFeasibilityTests.Scenarios.cs
        all.AddRange(ManeuverTests());       // ManeuverFeasibilityTests.Maneuvers.cs
        all.AddRange(MonitorTests());        // ManeuverFeasibilityTests.Monitor.cs
        all.AddRange(DefenderTests());       // ManeuverFeasibilityTests.Defender.cs
        all.AddRange(LookTests());           // ManeuverFeasibilityTests.Look.cs
        all.AddRange(RuntimeTests());        // ManeuverFeasibilityTests.Runtime.cs
        all.AddRange(AllocTests());          // ManeuverFeasibilityTests.Alloc.cs
        all.AddRange(GuardTests());          // ManeuverFeasibilityTests.Guards.cs (verify round 1)
        return all.ToArray();
    }

    static (string, Action)[] StanceTests() => new (string, Action)[]
    {
        ("bc14 FEAS-STANCE-INITIAL a Defender's initial pick refreshes the pick inputs first and is not offensive", FeasStanceInitial),
        ("bc14 FEAS-NIGHT-EVAL the night replan runs the forced evaluation once, with the plan standing, before it re-plans", FeasNightEvaluation),
    };

    // The pick inputs are refreshed before the first pick, so a Defender's first plan comes out of the stance-filtered catalog.
    // Control: the same army and evidence at the default Meeting stance picks DoubleEnvelopment, an offensive play.
    static void FeasStanceInitial()
    {
        var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
        var evidence = new ArmyEvidence(currentOdds: 1.1f, terrain: TerrainKind.Wooded, defaultMainEffortSector: 0);

        var control = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        control.PickInitialPlan(evidence);
        AssertEqual(BattlePlanId.DoubleEnvelopment, control.CurrentPlan.PlanId, "control: at Meeting this army opens with DoubleEnvelopment");

        var army = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        var order = new List<string>();
        bool hadPlanAtRefresh = true;
        var children = new[]
        {
            new DirectChildSnapshot("child-10", "army-1", 14, 0, "1st_Brigade", true),
            new DirectChildSnapshot("child-11", "army-1", 14, 0, "2nd_Brigade", true),
        };
        int childrenAtEvaluate = -1;
        TacticalBattleCoordinator.PickInitialPlanInOrder(
            army,
            registerChildren: () =>
            {
                order.Add("register");
                army.RegisterDirectChildren(children);
            },
            refreshPickInputs: () =>
            {
                order.Add("refresh");
                hadPlanAtRefresh = army.HasPlan;
                army.UpdateBattleStance(BattleStance.Defender);
            },
            evaluate: () =>
            {
                order.Add("evaluate");
                childrenAtEvaluate = army.CurrentDirectChildIntents.Count;
            },
            evidence);
        AssertEqual("register,refresh,evaluate", string.Join(",", order), "register the children, refresh, then evaluate");
        AssertEqual(2, childrenAtEvaluate, "the feasibility evaluation prices the plans against the army's real children (smoke 2026-10-17: it saw none)");
        AssertFalse(hadPlanAtRefresh, "the pick inputs are refreshed before the plan exists");
        AssertTrue(army.HasPlan, "a plan is picked");
        AssertTrue(TacticalPlaybookCatalog.StanceOf(army.CurrentPlan.PlanId) != TacticalPlaybookCatalog.PlaybookStance.Offensive,
            "a Defender's initial pick is not offensive, got " + army.CurrentPlan.PlanId);
    }

    // The new battle day's replan reads a ledger of the morning's ground: the forced evaluation runs first, with the plan still standing,
    // then the replan restarts the plan's age. A side with no plan evaluates nothing (its first pick does).
    static void FeasNightEvaluation()
    {
        var evidence = new ArmyEvidence(1.0f, TerrainKind.Open, 0);
        var visible = new EnemyVisibleState(new[] { new EnemyVisibleSector(0, 5000f, 9000f, true) }, 0.5f, anyContactSpotted: true, anyContactBroken: false);
        var army = new ArmyOrchestrator(1, BuiltInPlaybooks.SeedCatalog(), default);
        army.PickInitialPlan(evidence);
        army.AdvancePlanAge(100f);
        int calls = 0;
        float ageAtEvaluate = -1f;
        TacticalBattleCoordinator.ReplanForNewBattleDay(army, evidence, visible, () => { calls++; ageAtEvaluate = army.PlanAgeSeconds; });
        AssertEqual(1, calls, "the evaluation runs once");
        AssertNear(100f, ageAtEvaluate, 1e-4f, "before the replan (the plan was still 100 s old)");
        AssertNear(0f, army.PlanAgeSeconds, 1e-6f, "and the replan followed");

        var idle = new ArmyOrchestrator(1, BuiltInPlaybooks.SeedCatalog(), default);
        int idleCalls = 0;
        TacticalBattleCoordinator.ReplanForNewBattleDay(idle, evidence, visible, () => idleCalls++);
        AssertEqual(0, idleCalls, "a side with no plan evaluates nothing");
    }
}
