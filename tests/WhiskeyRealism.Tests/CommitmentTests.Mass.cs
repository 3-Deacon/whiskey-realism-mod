using System;
using System.Collections.Generic;
using System.Linq;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;

using static TestHarness;
using static TestFixtures;

// F7, the mass: what the committed formations do to SelectMass, the reserve split, the hold pass and the other candidates' capacity; the end by
// the odds deadband; the trace; the sidecar; and the source pins of the commitment.
internal static partial class CommitmentTests
{
    private static (string name, Action run)[] MassCases()
    {
        return new (string name, Action run)[]
        {
            ("EveryFormationCommittedToTheChosenObjectiveIsTakenWhateverTheNeed", EveryFormationCommittedToTheChosenObjectiveIsTakenWhateverTheNeed),
            ("AnArmyWithACommittedMemberIsEntered", AnArmyWithACommittedMemberIsEntered),
            ("SplitReserveKeepsCommittedFormations", SplitReserveKeepsCommittedFormations),
            ("HoldNeverReservesACommittedFormation", HoldNeverReservesACommittedFormation),
            ("AChallengersCapacityCountsTheReadyCommittedCorps", AChallengersCapacityCountsTheReadyCommittedCorps),
            ("TheOddsDeadbandKeepsTheCommitmentForTwoReviewsAndTheThirdEndsIt", TheOddsDeadbandKeepsTheCommitmentForTwoReviewsAndTheThirdEndsIt),
            ("TheTraceAlwaysCarriesTheLastMassObjective", TheTraceAlwaysCarriesTheLastMassObjective),
            ("TheTraceCarriesTheObjectiveBeforeTheUpdate", TheTraceCarriesTheObjectiveBeforeTheUpdate),
            ("ADepotSurplusCountsNoCommittedFormation", ADepotSurplusCountsNoCommittedFormation),
            ("ListSurplusesSkipsCommittedWait", ListSurplusesSkipsCommittedWait),
            ("TheLastMassRoundTripsInTheSidecarByUnitKey", TheLastMassRoundTripsInTheSidecarByUnitKey),
            ("ACommanderChangeCarriesTheLastMassKey", ACommanderChangeCarriesTheLastMassKey),
            ("NoReturnRoleOrReasonExists", NoReturnRoleOrReasonExists),
            ("NoNewConstantDecidesTheEnd", NoNewConstantDecidesTheEnd),
        };
    }

    // A state in the middle of a push on 98 whose last mass is the given corps (as an earlier review would have left it).
    private static PlannerState PushOn98(params int[] ids)
    {
        var state = new PlannerState { CurrentMainObjective = 98, DecisionSerial = 1, CampaignSeed = 1861L };
        state.SetLastMass(98, ids.Select(i => "corps" + i + ":" + i));
        return state;
    }

    // Two armies of 9000 men, the nearer one (corps 5, army 51) alone meets the need, the farther one (corps 1, army 50, with corps 4) is committed.
    private static CampaignSnapshot TwoArmiesWorld()
    {
        var s = ExecutorFixtures.Wide();
        s.MinStrengthForMovement = 2000f;
        s.Own.Add(Corps(1, 170f, 9000f, 3, 50));
        s.Own.Add(Corps(4, 160f, 9000f, 3, 50));
        s.Own.Add(Corps(5, 110f, 9000f, 3, 51));
        s.Own.Add(Corps(3, 20f, 9000f));
        return s;
    }

    private static void EveryFormationCommittedToTheChosenObjectiveIsTakenWhateverTheNeed()
    {
        var s = PushWorld();
        s.Own[s.IndexOfOwn(1)] = Corps(1, 220f, 9000f, 3, 50);
        s.Own[s.IndexOfOwn(2)] = Corps(2, 240f, 9000f, 3, 51);
        var plan = Replan(s, PushOn98(1, 2), Moves(98, 1, 2));
        AssertEqual(98, plan.MainObjectiveId, "the push goes on");
        AssertEqual("1,2", Ids(plan.MassInstanceIds), "both armies are committed, so both are in the mass though one meets the need of 5500: " + Describe(plan));
        var control = WithCommitment(false, () => Replan(s, PushOn98(1, 2), Moves(98, 1, 2)));
        AssertEqual("1", Ids(control.MassInstanceIds), "without the commitment the nearest army alone is selected");
    }

    private static void AnArmyWithACommittedMemberIsEntered()
    {
        var s = TwoArmiesWorld();
        var plan = Replan(s, PushOn98(1), Moves(98, 1));
        AssertEqual("1,4", Ids(plan.MassInstanceIds), "army 50 is entered whole (its other capacity member comes with the committed corps) and the nearer army is not needed: " + Describe(plan));
        var control = WithCommitment(false, () => Replan(s, PushOn98(1), Moves(98, 1)));
        AssertEqual("5", Ids(control.MassInstanceIds), "otherwise the army nearest the target is the mass");
    }

    private static void SplitReserveKeepsCommittedFormations()
    {
        var s = PushWorld();
        s.Own[s.IndexOfOwn(1)] = Corps(1, 220f, 9000f, 3, 50);
        s.Own[s.IndexOfOwn(2)] = Corps(2, 240f, 9000f, 3, 51);
        var plan = Replan(s, PushOn98(1, 2), Moves(98, 1, 2));
        var a = ForceAllocator.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, null, null);
        a.TryGet(1, out var one);
        a.TryGet(2, out var two);
        AssertEqual(FormationRole.MainEffort, one.Role, "the nearer committed corps is the main effort");
        AssertEqual(FormationRole.MainEffort, two.Role, "and the farther one is not split off as a Reserve though the mass is far above its odds: the excess comes from the others");
        var uncommitted = WithCommitment(false, () =>
        {
            var p = Replan(s, PushOn98(1, 2), Moves(98, 1, 2));
            p.MassInstanceIds.Add(2);
            p.Claims[s.IndexOfOwn(2)] = new Claim { InstanceId = 2, Claimant = Claimant.Mass, SectorId = -1, TargetX = 240f, TargetZ = 0f };
            return ForceAllocator.Allocate(p, s, ExecutorFixtures.Sectors(s), null, null, null);
        });
        uncommitted.TryGet(2, out var split);
        AssertEqual(FormationRole.Reserve, split.Role, "control: the same Mass claim without the committed reason is the surplus the split takes (farthest first)");
    }

    private static void HoldNeverReservesACommittedFormation()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(Corps(1, 330f, 9000f));
        var sectors = ExecutorFixtures.Sectors(s);
        var claims = ClaimTable.Fresh(s);
        claims[0].Claimant = Claimant.Committed;
        var need = new float[sectors.Count];
        need[sectors.SectorAt(330f, 0f)] = 4000f;
        var held = new float[sectors.Count];
        ReserveRules.Hold(s, sectors, need, new float[sectors.Count], new float[sectors.Count], claims, null, null, held);
        AssertEqual(Claimant.Committed, claims[0].Claimant, "the hold pass takes only unowned formations");
        AssertEqual(-1, claims[0].SectorId, "and counts the committed formation in no sector");
        AssertEqual(0f, held[sectors.SectorAt(330f, 0f)], "so the sector holds nothing of it");
    }

    // The trace's candidate for another objective counts a committed corps only when it is ready: the exemption is the last mass's objective's.
    private static void AChallengersCapacityCountsTheReadyCommittedCorps()
    {
        string Candidate(string trace, int objective) =>
            trace.Split('|').First(c => c.TrimStart().StartsWith(objective + "@", StringComparison.Ordinal));
        var ready = Replan(PushWorld(), PushOn98(1, 2), Moves(98, 1, 2), out string readyTrace);
        AssertContains(Candidate(readyTrace, 99), " mass=8000", "a challenger is judged with the push's ready corps, as today: " + Candidate(readyTrace, 99));
        var dipped = Replan(PushWorld(readiness2: 1), PushOn98(1, 2), Moves(98, 1, 2), out string dippedTrace);
        AssertContains(Candidate(dippedTrace, 98), " mass=8000", "the push itself counts the unready corps");
        AssertContains(Candidate(dippedTrace, 99), " mass=4000", "a challenger does not: the unready corps is nobody's for it (D5)");
    }

    // ---- the end by D4's odds deadband ----

    private static void TheOddsDeadbandKeepsTheCommitmentForTwoReviewsAndTheThirdEndsIt()
    {
        var state = FirstDay(out _);
        CampaignSnapshot Hard()
        {
            var s = PushWorld(readiness2: 1);
            s.OffensiveDominance = 2.05f;   // the push's odds, 2.0, sit inside the deadband below the required ratio
            return s;
        }
        for (int day = 1; day <= TheaterPlanner.SwitchDays - 1; day++)
        {
            var plan = Replan(Hard(), state, Moves(98, 1, 2));
            AssertEqual(98, plan.MainObjectiveId, "review " + day + " inside the deadband: the push is kept: " + plan.SwitchReason);
            AssertEqual("1,2", Ids(plan.MassInstanceIds), "review " + day + ": with its unready corps: " + Describe(plan));
            AssertEqual(0, plan.CommitmentEnded.Count, "review " + day + ": nothing has ended");
        }
        var last = Replan(Hard(), state, Moves(98, 1, 2));
        AssertEqual(-1, last.MainObjectiveId, "the third review below the ratio ends D4's push: " + last.SwitchReason);
        AssertEqual("1,2", Ids(last.CommitmentEnded), "and with it the commitment of both corps");
        AssertEqual(Claimant.Committed, ClaimOf(last, 2).Claimant, "the unready corps waits where it stands: " + Describe(last));
        AssertEqual(-1, state.LastMassObjective, "no main effort, so no last mass");
    }

    // ---- the trace ----

    private static CampaignSnapshot ManyObjectivesWorld()
    {
        var s = PushWorld();
        for (int i = 0; i < s.Objectives.Count; i++)
        {
            var o = s.Objectives[i];
            if (o.ObjectiveId == 98) { o.Importance01 = 0f; s.Objectives[i] = o; }
        }
        for (int k = 0; k < 24; k++)
            s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 200 + k, X = 330f + (k % 12) * 20f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = 1f });
        return s;
    }

    private static void TheTraceAlwaysCarriesTheLastMassObjective()
    {
        var state = PushOn98(1, 2);
        var plan = Replan(ManyObjectivesWorld(), state, Moves(98, 1, 2), out string trace);
        var shown = trace.Split('|').Skip(1).Select(c => c.Trim()).ToList();
        AssertTrue(shown.Count(c => !c.StartsWith("98@", StringComparison.Ordinal)) <= TheaterPlanner.MaxTraceCandidates, "the other candidates keep the cap");
        AssertTrue(shown.Any(c => c.StartsWith("98@", StringComparison.Ordinal)), "the candidate of the last mass's objective is there though it ranks below the cap: " + shown.Count + " shown");
        var plain = Replan(ManyObjectivesWorld(), new PlannerState(), null, out string plainTrace);
        AssertTrue(plainTrace.Split('|').Length - 1 <= TheaterPlanner.MaxTraceCandidates, "with no last mass the trace keeps its cap: " + (plainTrace.Split('|').Length - 1));
    }

    // The objective is read as the last mass stood BEFORE today's update, so a review that switches away still shows the old objective's candidate.
    private static void TheTraceCarriesTheObjectiveBeforeTheUpdate()
    {
        var world = ManyObjectivesWorld();
        for (int i = 0; i < world.Objectives.Count; i++)
        {
            var o = world.Objectives[i];
            if (o.ObjectiveId >= 200) { o.Importance01 = 0f; world.Objectives[i] = o; }
            if (o.ObjectiveId == 98) { o.Importance01 = 1f; world.Objectives[i] = o; }
        }
        var state = PushOn98(1, 2);
        var plan = Replan(world, state, Moves(98, 1, 2), out string trace);
        AssertTrue(plan.MainObjectiveId != 98 || plan.SwitchReason == "hold", "a review of the world");
        AssertTrue(trace.Split('|').Any(c => c.TrimStart().StartsWith("98@", StringComparison.Ordinal)), "the old objective's candidate is in the trace though the last mass is rewritten");
        // A review that ends the push (no objective is within the odds) rewrites the last mass before the trace is built: the objective carried
        // is the one the review began with, though its candidate ranks below the cap.
        var hard = ManyObjectivesWorld();
        hard.OffensiveDominance = 50f;
        var cleared = PushOn98(1, 2);
        var none = Replan(hard, cleared, Moves(98, 1, 2), out string noneTrace);
        AssertEqual(-1, none.MainObjectiveId, "no objective is within the odds");
        AssertEqual(-1, cleared.LastMassObjective, "so the last mass was cleared before the trace was built");
        AssertTrue(noneTrace.Split('|').Any(c => c.TrimStart().StartsWith("98@", StringComparison.Ordinal)), "and the trace still carries the objective it began with, ranked below the cap");
        var done = AccomplishedWorld(3);
        var ended = Replan(done, PushOn98(1, 2), Moves(98, 1, 2), out string endedTrace);
        AssertFalse(endedTrace.Split('|').Any(c => c.TrimStart().StartsWith("98@", StringComparison.Ordinal)), "a review that skipped the objective (accomplished) has no such candidate");
    }

    // ---- the depot ----

    // A committed-wait formation is counted in no hold, so it adds nothing to the sector's surplus a depot builder may be drawn from (D11
    // requirement-depot-builder): the smallest formation builds only when the sector holds its target without it.
    private static void ADepotSurplusCountsNoCommittedFormation()
    {
        var s = ExecutorFixtures.Wide();
        s.MinStrengthForMovement = 2000f;
        s.Own.Add(Corps(10, 330f, 3000f));
        s.Own.Add(Corps(12, 380f, 4000f));
        var sectors = ExecutorFixtures.Sectors(s);
        TheaterPlan Build()
        {
            var p = new TheaterPlan { Alliance = s.Alliance, DaySerial = s.DaySerial, MinGarrisonStrength = 2000f };
            p.DepotTasks.Add(new DepotTask { X = 350f, Z = 0f, SectorId = sectors.SectorAt(350f, 0f), Priority = 1f });
            return TestClaims.For(p, s, sectors);
        }
        var counted = ForceAllocator.Allocate(Build(), s, sectors, null, null, null);
        counted.TryGet(10, out var control);
        AssertEqual(FormationRole.DepotBuilder, control.Role, "control: with 12 holding the sector builds from the surplus");
        var plan = Build();
        int i12 = s.IndexOfOwn(12);
        var claim = plan.Claims[i12];
        claim.Claimant = Claimant.Committed;
        claim.SectorId = -1;
        claim.Share = 0f;
        plan.Claims[i12] = claim;
        var waiting = ForceAllocator.Allocate(plan, s, sectors, null, null, null);
        waiting.TryGet(12, out var wait);
        AssertEqual(ForceAllocator.ReasonCommittedWait, wait.Reason, "12 waits out its ended push");
        waiting.TryGet(10, out var small);
        AssertEqual(FormationRole.Hold, small.Role, "and the sector, holding 3000 without it, has no surplus for 10 to build from");
    }

    // A sector with no hold need lists its unheld remainder as surplus G5b may draw on; a corps waiting out an ended push is no one's surplus.
    private static void ListSurplusesSkipsCommittedWait()
    {
        var s = Wide(Marcher(1, 340f, 0), Marcher(2, 350f, 0));
        var sectors = ExecutorFixtures.Sectors(s);
        TheaterPlan Build()
        {
            var p = new TheaterPlan { Alliance = s.Alliance, DaySerial = s.DaySerial, MinGarrisonStrength = 2000f };
            return TestClaims.For(p, s, sectors);
        }
        var control = ForceAllocator.Allocate(Build(), s, sectors, null, null, null);
        AssertEqual(1, control.Surpluses.Count, "control: of two remainder Holds the first meets the sector's minimum and is withheld, the other is a surplus: " + string.Join(",", control.Surpluses));
        int spare = control.Surpluses[0];
        var plan = Build();
        int k = plan.Claims.ToList().FindIndex(c => c.InstanceId == spare);
        var claim = plan.Claims[k];
        claim.Claimant = Claimant.Committed;
        plan.Claims[k] = claim;
        var waiting = ForceAllocator.Allocate(plan, s, sectors, null, null, null);
        waiting.TryGet(spare, out var wait);
        AssertEqual(ForceAllocator.ReasonCommittedWait, wait.Reason, "the corps waits");
        AssertFalse(waiting.Surpluses.Contains(spare), "and is not listed as a surplus, though it is a Hold that is not withheld");
    }

    // ---- the sidecar and the key ----

    private static void TheLastMassRoundTripsInTheSidecarByUnitKey()
    {
        var planner = new PlannerSnapshot();
        planner.State.RestoreLastMass(98, new[] { "1st Corps:7", "2nd Corps:9", "", null });
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(SidecarDtoCodec.PlannerToDto(planner));
        AssertContains(json, "\"lastMass\"", "the sidecar names the field");
        var back = SidecarDtoCodec.PlannerFromDto(Newtonsoft.Json.JsonConvert.DeserializeObject<PlannerStateDto>(json)).State;
        AssertEqual(98, back.LastMassObjective, "the objective");
        AssertEqual("1st Corps:7,2nd Corps:9", string.Join(",", back.LastMassKeys.OrderBy(k => k, StringComparer.Ordinal)), "the members by unit key; a record with no key is skipped");
        var older = SidecarDtoCodec.PlannerFromDto(Newtonsoft.Json.JsonConvert.DeserializeObject<PlannerStateDto>("{\"currentMainObjective\":3}")).State;
        AssertTrue(older.LastMassObjective == -1 && older.LastMassKeys.Count == 0, "an older file has no last mass");
        var none = new PlannerSnapshot();
        none.State.RestoreLastMass(-1, new[] { "x:1" });
        AssertEqual(0, none.State.LastMassKeys.Count, "no objective is no last mass");
    }

    private static void ACommanderChangeCarriesTheLastMassKey()
    {
        var state = new PlannerState();
        state.Observe(7, "1st Corps:7", Theater.East);
        state.SetLastMass(98, new[] { "1st Corps:7" });
        state.RekeyInstance(7, "1st Corps:8");
        AssertFalse(state.InLastMass("1st Corps:7"), "the appointment moved the key");
        AssertTrue(state.InLastMass("1st Corps:8"), "to the new one, with the objective kept: " + state.LastMassObjective);
        var other = new PlannerState();
        other.Observe(9, "2nd Corps:9", Theater.East);
        other.SetLastMass(98, new[] { "2nd Corps:9" });
        other.Observe(9, "2nd Corps:10", Theater.East);
        AssertTrue(other.InLastMass("2nd Corps:10") && !other.InLastMass("2nd Corps:9"), "so does the next review's observation");
    }

    // ---- source pins ----

    private static void NoReturnRoleOrReasonExists()
    {
        foreach (string path in new[] { "Strategic/Planner/CampaignPlannerContracts.cs", "Strategic/Planner/ForceAllocator.cs", "Strategic/Planner/ClaimTable.cs",
                                        "Strategic/Planner/Commitment.cs", "Strategic/Planner/CampaignMover.cs", "Strategic/Planner/TheaterPlanner.cs" })
        {
            string code = string.Join("\n", PlannerReadCensusTests.CodeLines(ReadRepoFile("src/WhiskeyRealism/" + path)));
            AssertFalse(code.Contains("return-home") || code.Contains("Returning") || code.Contains("ReturnHome"), path + ": there is no return lifecycle (D11 requirement-decision-end)");
        }
        foreach (string name in Enum.GetNames(typeof(Claimant)))
            AssertFalse(name.Contains("Return"), "no claimant sends a formation back: " + name);
        foreach (string name in Enum.GetNames(typeof(FormationRole)))
            AssertFalse(name.Contains("Return"), "no role sends a formation back: " + name);
    }

    // The commitment ends when D4 ends the push: the chosen objective is no longer the last mass's. No number decides it.
    private static void NoNewConstantDecidesTheEnd()
    {
        string commitment = string.Join("\n", PlannerReadCensusTests.CodeLines(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/Commitment.cs")));
        AssertFalse(System.Text.RegularExpressions.Regex.IsMatch(commitment, @"\bconst\s+(int|float|double)\b"), "Commitment.cs declares no numeric constant");
        string planner = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/TheaterPlanner.cs");
        string plan = BodyOf(planner, "internal static TheaterPlan Plan(CampaignSnapshot s, SectorModel sectors, Func<int, CommanderProfile> sectorProfile, PlannerState state,\n                                         IReadOnlyList<BattleHistoryRecord> battles, ITerrainLineSource terrain, in MarchSpeedInputs speed, out string trace,\n                                         out EnemyPicture picture");
        int decide = plan.IndexOf("int main = Decide(", StringComparison.Ordinal);
        int continues = plan.IndexOf("Commitment.Continues(state, candidates[main].ObjectiveId)", StringComparison.Ordinal);
        int update = plan.IndexOf("Commitment.UpdateLastMass(s, state, plan);", StringComparison.Ordinal);
        int leave = plan.IndexOf("CoveringForce.LeaveBehind(", StringComparison.Ordinal);
        AssertTrue(decide >= 0 && decide < continues && continues < update && update < leave, "the end is read from what Decide chose, and the last mass is updated after the mass is in the table and before the covering left-behind rewrite");
    }
}
