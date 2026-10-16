using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;

using static TestHarness;
using static TestFixtures;

// Stage G slice G3c (docs/superpowers/plans/2026-10-15-campaign-planner.md § G3c): scenario 001's
// objectives name no towns (Config/campaignobjectives.dat), so the planner maps each ground
// objective to the towns vanilla's CheckIfAccomplished (179059-179313) tests. These tests pin the
// table row by row, the availability rule (GetAvailableObjectives 178838-178864), the planner
// forming a main effort on that ground, and the snapshot's mapped branch (source pins: the runtime
// adapter is outside the test project).
internal static class ObjectiveGroundTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("MappedGroundIsVanillasAccomplishmentTowns", MappedGroundIsVanillasAccomplishmentTowns),
            ("MappedOnlyIn001WithEmptyTargets", MappedOnlyIn001WithEmptyTargets),
            ("SubScenarioEHasNoWesternVirginiaOrCut", SubScenarioEHasNoWesternVirginiaOrCut),
            ("WashingtonEitherName", WashingtonEitherName),
            ("MappedAvailabilityIsVanillasRule", MappedAvailabilityIsVanillasRule),
            ("DeactivatedMappedObjectiveIsNoCandidate", DeactivatedMappedObjectiveIsNoCandidate),
            ("PlannerFormsMainEffortOn001Ground", PlannerFormsMainEffortOn001Ground),
            ("RichmondPriorRowMatchesIn001", RichmondPriorRowMatchesIn001),
            ("ReadObjectivesMappedBranchPinned", ReadObjectivesMappedBranchPinned),
            ("MappedCountIsResetAndLoggedOnFirstReview", MappedCountIsResetAndLoggedOnFirstReview),
        });
    }

    private const string Level = "001";

    // 1861-06-15: inside union-east-pressure (alliance 0, objective 3).
    private static readonly int Day = ReviewCadenceCatchUp.DaySerial(15, 6, 1861);

    // (objective, state, names tried in order), the plan's mapping table. The vanilla anchors are in
    // the plan; the names and states are the literals of 179059-179313.
    private static readonly (int id, string state, string[] names)[] Table =
    {
        (2, "", new[] { "Montgomery" }),
        (3, "", new[] { "Richmond" }),
        (4, "", new[] { "Washington", "Washington D.C." }),
        (14, "Virginia", new[] { "Wheeling", "Charleston", "Grafton" }),
        (15, "Kentucky", new[] { "Louisville" }),
        (16, "Missouri", new[] { "St.Louis" }),
        (17, "", new[] { "Vicksburg", "New Orleans", "Baton Rouge", "Memphis" }),
    };

    private static void MappedGroundIsVanillasAccomplishmentTowns()
    {
        foreach (var row in Table)
        {
            var ground = ObjectiveGround.TownsFor(Level, "A", row.id);
            // Washington is one town with two names; every other row is one town per name.
            int towns = row.id == 4 ? 1 : row.names.Length;
            AssertEqual(towns, ground.Count, "objective " + row.id + ": town count");
            for (int t = 0; t < ground.Count; t++)
            {
                AssertEqual(row.state, ground[t].State, "objective " + row.id + " town " + t + ": state");
                if (row.id == 4)
                {
                    AssertEqual(row.names.Length, ground[t].Names.Length, "Washington keeps both names");
                    for (int n = 0; n < row.names.Length; n++) AssertEqual(row.names[n], ground[t].Names[n], "Washington name " + n);
                }
                else
                {
                    AssertEqual(1, ground[t].Names.Length, "objective " + row.id + " town " + t + ": one name");
                    AssertEqual(row.names[t], ground[t].Names[0], "objective " + row.id + " town " + t + ": name");
                }
            }
        }
    }

    private static void MappedOnlyIn001WithEmptyTargets()
    {
        // Decision 6 covers scenario 001 only: 002's abstract ids (3 and 4 list 002 too, with empty
        // targets) stay unmapped, and so does everything without a town test in CheckIfAccomplished.
        foreach (var row in Table)
        {
            AssertEqual(0, ObjectiveGround.TownsFor("002", "A", row.id).Count, "002 maps nothing: " + row.id);
            AssertEqual(0, ObjectiveGround.TownsFor(null, "A", row.id).Count, "no scenario maps nothing: " + row.id);
            AssertEqual(0, ObjectiveGround.TownsFor("", "A", row.id).Count, "empty scenario maps nothing: " + row.id);
            AssertEqual(0, ObjectiveGround.TownsFor("0010", "A", row.id).Count, "the scenario matches exactly: " + row.id);
        }
        var mapped = new HashSet<int>();
        foreach (var row in Table) mapped.Add(row.id);
        for (int id = -1; id <= 40; id++)
        {
            if (mapped.Contains(id)) continue;
            AssertEqual(0, ObjectiveGround.TownsFor(Level, "A", id).Count, "ground-less objective " + id + " maps nothing");
        }
    }

    private static void SubScenarioEHasNoWesternVirginiaOrCut()
    {
        foreach (int id in new[] { 14, 17 })
        {
            AssertEqual(0, ObjectiveGround.TownsFor(Level, "E", id).Count, "sub-scenario E: objective " + id + " is vanilla's IsDeactivated, no ground");
            AssertTrue(ObjectiveGround.TownsFor(Level, "A", id).Count > 0, "sub-scenario A: objective " + id + " has ground");
            AssertTrue(ObjectiveGround.TownsFor(Level, "B", id).Count > 0, "sub-scenario B: objective " + id + " has ground");
            AssertTrue(ObjectiveGround.TownsFor(Level, null, id).Count > 0, "an unknown sub-scenario is not E: objective " + id);
        }
        foreach (int id in new[] { 2, 3, 4, 15, 16 })
            AssertTrue(ObjectiveGround.TownsFor(Level, "E", id).Count > 0, "E keeps the capitals and the border states: " + id);
    }

    private static void WashingtonEitherName()
    {
        // Vanilla resolves "Washington" and falls back to "Washington D.C." (179097-179108): one
        // town, two names, tried in that order.
        var ground = ObjectiveGround.TownsFor(Level, "A", 4);
        AssertEqual(1, ground.Count, "one town");
        AssertEqual(2, ground[0].Names.Length, "two names");
        AssertEqual("Washington", ground[0].Names[0], "plain name first");
        AssertEqual("Washington D.C.", ground[0].Names[1], "then the D.C. name");
        AssertEqual("", ground[0].State, "no state qualifier");
    }

    private static void MappedAvailabilityIsVanillasRule()
    {
        // GetAvailableObjectives 178838-178864: the objective's alliance is the asking alliance, it
        // is not deactivated, not accomplished, and at least one target is owned by an alliance at
        // war with the asking alliance (the caller counts those).
        AssertTrue(ObjectiveGround.Available(0, 0, false, false, 1), "one enemy-held town: available");
        AssertTrue(ObjectiveGround.Available(1, 1, false, false, 3), "several enemy-held towns: available");
        AssertFalse(ObjectiveGround.Available(0, 0, false, false, 0), "no enemy-held town: not available");
        AssertFalse(ObjectiveGround.Available(0, 1, false, false, 1), "the other alliance's objective: not available");
        AssertFalse(ObjectiveGround.Available(1, 0, false, false, 1), "the other alliance's objective: not available (CSA asking)");
        AssertFalse(ObjectiveGround.Available(0, 0, true, false, 1), "deactivated: not available");
        AssertFalse(ObjectiveGround.Available(0, 0, false, true, 1), "accomplished: not available");
        AssertFalse(ObjectiveGround.Available(0, 0, true, true, 0), "all of it at once: not available");
    }

    private static void DeactivatedMappedObjectiveIsNoCandidate()
    {
        // The planner takes Available && !Accomplished records (TheaterPlanner.Evaluate): a mapped
        // objective vanilla deactivated (Montgomery after policy 170) offers none.
        var s = Ground001(0, 2, 3);
        for (int i = 0; i < s.Objectives.Count; i++)
        {
            var o = s.Objectives[i];
            o.Available = ObjectiveGround.Available(0, 0, true, false, 1);
            s.Objectives[i] = o;
        }
        var plan = Plan(s);
        AssertEqual(-1, plan.MainObjectiveId, "no main effort on deactivated ground");
    }

    private static void PlannerFormsMainEffortOn001Ground()
    {
        // Union, 1861-06, a 001-shaped snapshot: the CSA-held mapped towns of the objectives vanilla
        // keeps live. Policy 170 (the capital moves) deactivates 2 and activates 3 (178927-178934).
        var before = Plan(Ground001(0, 2, 14, 17));
        AssertTrue(Array.IndexOf(new[] { 2, 14, 17 }, before.MainObjectiveId) >= 0,
            "before policy 170 the Union's main effort is on 2, 14 or 17, got " + before.MainObjectiveId);
        var after = Plan(Ground001(0, 3, 14, 17));
        AssertTrue(Array.IndexOf(new[] { 3, 14, 17 }, after.MainObjectiveId) >= 0,
            "after policy 170 the Union's main effort is on 3, 14 or 17, got " + after.MainObjectiveId);

        // The CSA's candidates are 4, 15, 16.
        var csa = Plan(Ground001(1, 4, 15, 16));
        AssertTrue(Array.IndexOf(new[] { 4, 15, 16 }, csa.MainObjectiveId) >= 0,
            "the CSA's main effort is on 4, 15 or 16, got " + csa.MainObjectiveId);

        // An objective of the other alliance is never the asking side's candidate.
        var foreign = Plan(Ground001(0, 4, 15));
        AssertEqual(-1, foreign.MainObjectiveId, "the CSA's objectives are no Union candidate");
    }

    private static void RichmondPriorRowMatchesIn001()
    {
        // union-east-pressure names objective 3 (HistoricalPriorTable); once Richmond is live as a
        // mapped record the prior reaches the candidate, as it can never on 001's empty target list.
        var s = Ground001(0, 3);
        var inputs = new TheaterPlanner.Inputs { S = s, Sectors = SectorModel.Build(s), Probe = null, Speed = Speed() };
        var list = new List<TheaterPlanner.Candidate>();
        TheaterPlanner.Evaluate(inputs, list);
        AssertEqual(1, list.Count, "Richmond is a candidate");
        AssertEqual(3, list[0].ObjectiveId, "objective 3");
        AssertEqual("union-east-pressure", list[0].PriorId, "the Richmond prior row matches");
        AssertTrue(list[0].PriorTerm > 0f, "and contributes");
    }

    // The runtime adapter is outside the test project, so the mapped branch is pinned on its text:
    // whole statement lines (HasLine: trimmed, exact), so a changed argument or a commented-out copy fails.
    private static void ReadObjectivesMappedBranchPinned()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignSnapshotRuntime.cs");
        string read = MethodBody(src, "private static void ReadObjectives(");
        string gate = "if (o.ObjectiveScenario == null || !o.ObjectiveScenario.Contains(scenario) || o.IsDeactivated()) continue;";
        string empty = "if (o.objectives.Count == 0)";
        string call = "ReadMappedGround(s, o, scenario, accomplished, alliance, followed, townRadius);";
        AssertTrue(HasLine(read, gate), "the shared gate skips other scenarios and deactivated objectives");
        AssertTrue(HasLine(read, empty), "the mapped branch is for an empty target list only");
        AssertTrue(HasLine(read, call), "the branch hands ReadMappedGround the scenario and vanilla's accomplished flag");
        int gateAt = read.IndexOf(gate, StringComparison.Ordinal);
        int emptyAt = read.IndexOf(empty, StringComparison.Ordinal);
        int callAt = read.IndexOf(call, StringComparison.Ordinal);
        AssertTrue(gateAt >= 0 && emptyAt > gateAt && callAt > emptyAt,
            "the scenario/IsDeactivated gate precedes the empty-target-list branch that calls ReadMappedGround");
        AssertEqual(2, Count(src, "ReadMappedGround("), "the definition and one call");

        string ground = MethodBody(src, "private static void ReadMappedGround(");
        AssertTrue(HasLine(ground, "var ground = ObjectiveGround.TownsFor(scenario, GamePrefs.subleveltoload, objectiveId);"), "the table is asked with the scenario and the sub-scenario");
        AssertTrue(HasLine(ground, "if (AtWar(town.Owner, alliance)) heldByEnemy++;"), "the at-war count uses the bounds-checked helper, counting enemy-held towns");
        AssertTrue(HasLine(ground, "bool available = ObjectiveGround.Available(alliance, o.ObjectiveAlliance, false, accomplished, heldByEnemy);"),
            "availability is the pure rule: the objective's own alliance, vanilla's accomplished flag, the enemy-held count");
        AssertTrue(HasLine(ground, "var seed = new ObjectiveRecord { ObjectiveId = objectiveId, Available = available, Accomplished = accomplished };"), "each record carries the pure rule's availability");
        AssertTrue(HasLine(ground, "RawImportance.Add(Math.Max(0f, importance));"), "each mapped record's importance is normalised with the rest");
        AssertTrue(HasLine(ground, "s.MappedObjectiveCount++;"), "the mapped records are counted");
        AssertFalse(ground.Contains("allcampaignobjectives") || ground.Contains("GetAvailableObjectives") || ground.Contains("AvailableObjectives"),
            "vanilla's availability is not consulted for empty target lists");

        string resolve = MethodBody(src, "private static Town ResolveGroundTown(");
        AssertTrue(HasLine(resolve, "for (int n = 0; n < ground.Names.Length && found == null; n++)"), "every name of the row is tried in order");
        AssertTrue(HasLine(resolve, "Town town = Town.GetTownFromName(ground.Names[n], ground.State);"), "towns resolve through vanilla's own lookup");
        AssertTrue(HasLine(resolve, "if ((UnityEngine.Object)cached != null) return cached; // else destroyed: resolve again"), "a destroyed cached town is resolved again");
        AssertTrue(HasLine(resolve, "GroundTownCache[key] = found;"), "the result, a miss included, is cached for the campaign");
        AssertContains(resolve, "campaign-planner:objective-ground-missing:", "a town that does not resolve is logged once per objective and name");
        AssertTrue(HasLine(MethodBody(src, "private static void ResetCaches("), "GroundTownCache.Clear();"), "the town cache is reset with the campaign");
        // One town-record builder for both branches (a mapped town is built exactly as a 002 town target).
        AssertEqual(3, Count(src, "TownRecord("), "the builder and its two callers");
    }

    private static void MappedCountIsResetAndLoggedOnFirstReview()
    {
        var s = new CampaignSnapshot();
        s.Reset(0, 0, Day, PlayerCommandClass.NotCareer, -1, null);
        AssertEqual(0, s.MappedObjectiveCount, "a fresh snapshot has none");
        s.MappedObjectiveCount = 5;
        s.Reset(0, 0, Day, PlayerCommandClass.NotCareer, -1, null);
        AssertEqual(0, s.MappedObjectiveCount, "Reset clears the pooled snapshot's count");
        string log = MethodBody(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs"), "private static void LogFirstReview(");
        int objectives = log.IndexOf("\" objectives=\" + s.Objectives.Count", StringComparison.Ordinal);
        int mapped = log.IndexOf("\" mapped=\" + s.MappedObjectiveCount", StringComparison.Ordinal);
        int supply = log.IndexOf("\" supply=\"", StringComparison.Ordinal);
        AssertTrue(objectives >= 0 && mapped > objectives && supply > mapped, "the first-review line names the mapped count right after the objectives");
    }

    // ---- fixtures ----

    // Areas every 100 on x from 0 (relocation range 150), own capital at x 0, own IIPs at x 100 and
    // 300, a corps at x 200; CSA-held (for alliance 0) or Union-held (for alliance 1) mapped towns of
    // the listed objectives at x 300, 400, 500 ... in table order, all in command reach of an IIP.
    private static CampaignSnapshot Ground001(int alliance, params int[] objectiveIds)
    {
        var s = new CampaignSnapshot();
        s.Reset(alliance, alliance, Day, PlayerCommandClass.NotCareer, -1, null);
        s.RelocationRange = 150f;
        s.OffensiveDominance = 1.1f;
        s.MinStrengthForMovement = 1000f;
        s.MinMoraleForMovement = 0.3f;
        s.HasCapital = true;
        for (int i = 0; i < 8; i++)
            s.Areas.Add(new AreaRecord { Index = 10 + i, X = i * 100f, Z = 0f, EdgeX = new[] { i * 100f }, EdgeZ = new[] { 0f }, Importance = 1f, SupplyCapacity = 100f });
        for (float x = 100f; x <= 700f; x += 200f) s.Supply.Add(Iip(500 + (int)x, x, alliance));
        s.Own.Add(Corps(1, 200f, 20000f));
        float at = 300f;
        foreach (int id in objectiveIds)
        {
            int objectiveAlliance = id == 4 || id == 15 || id == 16 ? 1 : 0;
            var ground = ObjectiveGround.TownsFor(Level, "A", id);
            AssertTrue(ground.Count > 0, "fixture: objective " + id + " has ground");
            int heldByEnemy = ground.Count;
            bool available = ObjectiveGround.Available(alliance, objectiveAlliance, false, false, heldByEnemy);
            for (int t = 0; t < ground.Count; t++)
            {
                s.Objectives.Add(new ObjectiveRecord
                {
                    ObjectiveId = id, X = at, Z = 0f, HolderAlliance = 1 - alliance, Available = available, Importance01 = 0.5f,
                    Roles = id == 2 || id == 3 || id == 4 ? ObjectiveRole.Capital : ObjectiveRole.None
                });
                at = at >= 700f ? 300f : at + 100f;
            }
        }
        return s;
    }

    private static TheaterPlan Plan(CampaignSnapshot s)
    {
        return TheaterPlanner.Plan(s, SectorModel.Build(s), null, new PlannerState(), null, null, Speed(), out _);
    }

    private static FormationRecord Corps(int id, float x, float strength)
    {
        return new FormationRecord
        {
            InstanceId = id, UnitKey = "corps" + id + ":" + id, UnitTyp = 15, X = x, Z = 0f, WaypointX = x, WaypointZ = 0f,
            Strength = strength, Morale = 1f, Supply01 = 1f, ReadinessStep = 3, CommandRange = 40f, BugleRange = 5f, CommanderId = id,
            AreaIndex = -1, Flags = FormationFlags.FightingForce, Name = "corps" + id, ParentInstanceId = -1,
            UseRails = true, UseRivers = true, GroupSpeed = 1f
        };
    }

    private static SupplySourceRecord Iip(int id, float x, int owner)
    {
        return new SupplySourceRecord { Id = id, Kind = SupplySourceKind.Iip, Owner = owner, X = x, Z = 0f, Condition = 1f, Built = true };
    }

    // unitbasicspeed[t, 1] = 2 for groups, basicunitspeed 1000 (unitprefs 853-854), factor 30
    // (campaignprefs 215-216), costs walk 4 / river 1 / road 2 / rail 1.
    private static MarchSpeedInputs Speed()
    {
        var mounted = new float[19];
        for (int t = 14; t <= 16; t++) mounted[t] = 2f;
        return new MarchSpeedInputs
        {
            MountedBaseSpeed = mounted, BasicUnitSpeed = 1000f, CampaignSpeedFactor = 30f,
            WalkCost = 4f, RiverCost = 1f, RoadCost = 2f, RailCost = 1f,
            RailroadSpeedAdj = 1f, TrainCapacityFree = 0f, TrainCapacityPerMan = 0.001f
        };
    }

    private static int Count(string text, string needle)
    {
        int count = 0, at = 0;
        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { count++; at += needle.Length; }
        return count;
    }

    private static string MethodBody(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        AssertTrue(start >= 0, "method present: " + signature);
        int open = src.IndexOf('{', start);
        int depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(start, i - start + 1);
        }
        throw new Exception("unbalanced body: " + signature);
    }
}
