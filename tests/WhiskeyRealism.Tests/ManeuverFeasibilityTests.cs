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
        return all.ToArray();
    }

    static (string, Action)[] StanceTests() => new (string, Action)[]
    {
        ("bc14 FEAS-STANCE-INITIAL a Defender's initial pick refreshes the pick inputs first and is not offensive", FeasStanceInitial),
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
        TacticalBattleCoordinator.PickInitialPlanInOrder(
            army,
            refreshPickInputs: () =>
            {
                order.Add("refresh");
                hadPlanAtRefresh = army.HasPlan;
                army.UpdateBattleStance(BattleStance.Defender);
            },
            evaluate: () => order.Add("evaluate"),
            evidence);
        AssertEqual("refresh,evaluate", string.Join(",", order), "refresh, then evaluate");
        AssertFalse(hadPlanAtRefresh, "the pick inputs are refreshed before the plan exists");
        AssertTrue(army.HasPlan, "a plan is picked");
        AssertTrue(TacticalPlaybookCatalog.StanceOf(army.CurrentPlan.PlanId) != TacticalPlaybookCatalog.PlaybookStance.Offensive,
            "a Defender's initial pick is not offensive, got " + army.CurrentPlan.PlanId);
    }
}
