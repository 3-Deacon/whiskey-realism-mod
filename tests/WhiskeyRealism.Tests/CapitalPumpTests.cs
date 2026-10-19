using System;
using System.Linq;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;

using static TestHarness;

// Hotfix 2026-10-19, the capital-floor pump (smoke17: "All of the CSA is just moving to Richmond"). The capital reserve counted only massable
// formations and started from nothing each review, so a guard that arrived and lost readiness stopped counting and the rule pulled the next
// nearest massable formation from anywhere, alternating with the covering force. It now counts first what stands within the relocation range of
// the capital, keeps an incumbent a guard where it stands, draws only inside that range, and the mover stops a guard still marching that the
// reserve no longer holds. The 200 limit is Whiskey's, on the user's forward-defence direction ("a large portion of Richmond defense .. is by
// keeping them from marching toward richmond but intelligently holding the front", user message 2026-10-19): vanilla weighs the enemy within the
// relocation range (AssignUnitToDefendCapital 11725), takes the own units nearer than the nearest enemy off its need (11735-11736) and draws within
// maxdistancedefensiveoperations (11791; 350 in 002/A), and its own capital defence still runs beside this rule.
internal static class CapitalPumpTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("AnUnreadyGuardAtTheCapitalIsStillCountedAndNothingNewIsDrawn", AnUnreadyGuardAtTheCapitalIsStillCountedAndNothingNewIsDrawn),
            ("NothingBeyondTheRelocationRangeIsEverDrawn", NothingBeyondTheRelocationRangeIsEverDrawn),
            ("AnIncumbentKeepsItsClaimWhereItStands", AnIncumbentKeepsItsClaimWhereItStands),
            ("AGuardInsideTheRangeIsNeverMarchedIntoTheCity", AGuardInsideTheRangeIsNeverMarchedIntoTheCity),
            ("OnlyTheFormationsThatCountAreCounted", OnlyTheFormationsThatCountAreCounted),
            ("AShortfallIsNotFilledFromAfar", AShortfallIsNotFilledFromAfar),
            ("TheCoveringForceAndTheCapitalNoLongerAlternateOutsideTheRange", TheCoveringForceAndTheCapitalNoLongerAlternateOutsideTheRange),
            ("AMarchingExGuardWithNoPostIsStopped", AMarchingExGuardWithNoPostIsStopped),
            ("AMarchingExGuardWithAPostIsRetargeted", AMarchingExGuardWithAPostIsRetargeted),
            ("OnlyAnExGuardIsStoppedByThisRule", OnlyAnExGuardIsStoppedByThisRule),
            ("AnExGuardIsStoppedOnceWhenTheHoldStepWouldStopItToo", AnExGuardIsStoppedOnceWhenTheHoldStepWouldStopItToo),
            ("AStoppedExGuardLeavesVanillasOffensiveList", AStoppedExGuardLeavesVanillasOffensiveList),
            ("AnExGuardWhoseNewTargetVanillaRefusesIsStillStopped", AnExGuardWhoseNewTargetVanillaRefusesIsStillStopped),
            ("AnExGuardWhoseNewTargetIsRestrictedIsStillStopped", AnExGuardWhoseNewTargetIsRestrictedIsStillStopped),
            ("AnExGuardTheDayHoldsBackIsStopped", AnExGuardTheDayHoldsBackIsStopped),
            ("ARestoredRecordWithNoRoleStillStopsAMarchToTheCapital", ARestoredRecordWithNoRoleStillStopsAMarchToTheCapital),
            ("AMarchWithNoPlannerRecordIsVanillasAndIsNeverStopped", AMarchWithNoPlannerRecordIsVanillasAndIsNeverStopped),
            ("AFormationInVanillasCapitalDefenceListIsNeverStopped", AFormationInVanillasCapitalDefenceListIsNeverStopped),
            ("AMarchForAnotherRoleOrAnotherPlaceIsLeftAlone", AMarchForAnotherRoleOrAnotherPlaceIsLeftAlone),
            ("AGuardInVanillasCapitalDefenceListIsNotDispatchedByTheMover", AGuardInVanillasCapitalDefenceListIsNotDispatchedByTheMover),
            ("ThePlayersFormationsAreNeverStoppedAsExGuards", ThePlayersFormationsAreNeverStoppedAsExGuards),
            ("ABusyFormationIsNeverStoppedAsAnExGuard", ABusyFormationIsNeverStoppedAsAnExGuard),
            ("TheReservesRowAndTheShortfallCountWhatTheRuleCounts", TheReservesRowAndTheShortfallCountWhatTheRuleCounts),
        });
    }

    // ---- the reserve ----

    // Capital at x 0, relocation range 150 (ExecutorFixtures.Line). A corps at x costs nothing to place.
    private static CampaignSnapshot World(params FormationRecord[] own)
    {
        var s = ExecutorFixtures.Wide();
        foreach (var f in own) s.Own.Add(f);
        return s;
    }

    private static FormationRecord Unready(FormationRecord f)
    {
        f.ReadinessStep = 0;
        return f;
    }

    private static Claim[] Reserve(CampaignSnapshot s, float floor)
    {
        var claims = ClaimTable.Fresh(s);
        ReserveRules.Capital(s, floor, claims);
        return claims;
    }

    // The pump itself: the guard stands at the capital, has lost readiness (it is no longer massable) and alone meets the floor. The shipped rule
    // did not count it, so it drew the nearest massable formations, one of them from beyond the range.
    private static void AnUnreadyGuardAtTheCapitalIsStillCountedAndNothingNewIsDrawn()
    {
        var s = World(Unready(ExecutorFixtures.Corps(1, 10f, 6000f)), ExecutorFixtures.Corps(2, 100f, 5000f), ExecutorFixtures.Corps(3, 400f, 5000f));
        var claims = Reserve(s, 6000f);
        AssertEqual(Claimant.Capital, claims[0].Claimant, "the unready guard is counted, and stays a guard");
        AssertEqual(Claimant.None, claims[1].Claimant, "a ready formation within the range is not drawn: the floor is met");
        AssertEqual(Claimant.None, claims[2].Claimant, "and nothing is pulled from afar");
    }

    private static void NothingBeyondTheRelocationRangeIsEverDrawn()
    {
        var s = World(ExecutorFixtures.Corps(1, 100f, 4000f), ExecutorFixtures.Corps(2, 151f, 4000f), ExecutorFixtures.Corps(3, 500f, 4000f),
                      ExecutorFixtures.Corps(4, 120f, 4000f), ExecutorFixtures.Corps(5, 400f, 4000f));
        var claims = ClaimTable.Fresh(s);
        claims[3].Claimant = Claimant.Committed;
        claims[4].Claimant = Claimant.DepartmentMove;
        ReserveRules.Capital(s, 20000f, claims);
        AssertEqual(Claimant.Capital, claims[0].Claimant, "the formation within the range is held");
        AssertEqual(Claimant.None, claims[1].Claimant, "one just past the range is not (the range is " + s.RelocationRange + ")");
        AssertEqual(Claimant.None, claims[2].Claimant, "nor one far away, whatever the floor");
        AssertEqual(Claimant.Capital, claims[3].Claimant, "a committed formation within the range is drawn (home defence first, as shipped)");
        AssertNear(120f, claims[3].TargetX, 0.01f, "held where it stands: nothing is marched into the city");
        AssertEqual(Claimant.DepartmentMove, claims[4].Claimant, "one on a department move beyond the range is left to its move");
    }

    // An incumbent: a formation standing within the range, ready or not, is claimed where it stands. Its target is its own place, so the
    // mover does not walk it into the capital's arms.
    private static void AnIncumbentKeepsItsClaimWhereItStands()
    {
        var ready = ExecutorFixtures.Corps(1, 120f, 3000f);
        var unready = Unready(ExecutorFixtures.Corps(2, 60f, 3000f));
        var s = World(ready, unready);
        var claims = Reserve(s, 6000f);
        AssertEqual(Claimant.Capital, claims[0].Claimant, "a ready incumbent stays a guard");
        AssertEqual(Claimant.Capital, claims[1].Claimant, "an unready one too");
        AssertNear(120f, claims[0].TargetX, 0.01f, "where it stands (the ready one)");
        AssertNear(60f, claims[1].TargetX, 0.01f, "where it stands (the unready one)");
    }

    // A guard within the range is never marched into the city (user direction 2026-10-19: Richmond is defended by holding the front, not by
    // gathering at the capital): its claim target is where it stands, even when its last waypoint is the capital. The mover, which sends a
    // CapitalGuard to its target, therefore halts a guard that was sent in by an earlier build.
    private static void AGuardInsideTheRangeIsNeverMarchedIntoTheCity()
    {
        var marching = ExecutorFixtures.Corps(1, 90f, 6000f);
        marching.WaypointX = 0f;
        marching.WaypointZ = 0f;
        var s = World(marching);
        var claims = Reserve(s, 6000f);
        AssertEqual(Claimant.Capital, claims[0].Claimant, "still a guard");
        AssertNear(90f, claims[0].TargetX, 0.01f, "held where it stands, not marched on to the capital");
        AssertNear(0f, claims[0].TargetZ, 0.01f, "z too");
    }

    // What does not count: a formation leaving for supply, one that does not count toward coverage (the player's own command), one another owner
    // holds (committed or on a department move; a pinned builder is another row's), and one outside the range.
    private static void OnlyTheFormationsThatCountAreCounted()
    {
        var starved = ExecutorFixtures.Corps(1, 10f, 6000f);
        starved.Supply01 = 0.05f;
        var player = ExecutorFixtures.Corps(2, 20f, 6000f);
        player.Chain = WlChainClass.PlayerCommand;
        var s = World(starved, player, ExecutorFixtures.Corps(3, 30f, 6000f), ExecutorFixtures.Corps(4, 40f, 6000f));
        var claims = ClaimTable.Fresh(s);
        claims[0].Claimant = Claimant.Recover;
        claims[2].Claimant = Claimant.PinnedBuilder;
        ReserveRules.Capital(s, 6000f, claims);
        AssertEqual(Claimant.Recover, claims[0].Claimant, "a formation leaving for supply holds nothing, and is not claimed");
        AssertEqual(Claimant.None, claims[1].Claimant, "the player's own command is never a guard");
        AssertEqual(Claimant.PinnedBuilder, claims[2].Claimant, "a pinned builder is another owner's");
        AssertEqual(Claimant.Capital, claims[3].Claimant, "so the floor is met by the one formation that counts");

        // The supply test is the reserve's own as well as row 3's: a starved formation nobody has claimed Recover yet holds nothing either.
        var raw = World(starved, ExecutorFixtures.Corps(5, 50f, 6000f));
        var unclaimed = ClaimTable.Fresh(raw);
        ReserveRules.Capital(raw, 6000f, unclaimed);
        AssertEqual(Claimant.None, unclaimed[0].Claimant, "the starved one is not claimed");
        AssertEqual(Claimant.Capital, unclaimed[1].Claimant, "and does not meet the floor");

        // Committed and a department move count for nothing at the capital (another owner has them), and home defence still comes first
        // through the draw, as shipped.
        var t = World(ExecutorFixtures.Corps(1, 10f, 6000f), ExecutorFixtures.Corps(2, 20f, 6000f));
        var held = ClaimTable.Fresh(t);
        held[0].Claimant = Claimant.Committed;
        held[1].Claimant = Claimant.DepartmentMove;
        ReserveRules.Capital(t, 12000f, held);
        AssertEqual(Claimant.Capital, held[0].Claimant, "the committed one is drawn, not counted");
        AssertEqual(Claimant.Capital, held[1].Claimant, "so is the department move");
        AssertNear(10f, held[0].TargetX, 0.01f, "held where it stands: nothing is marched into the city");
    }

    // The rule reports a shortfall (the [Reserves] capital=held/floor is the guards' strength against the floor); it does not reach for it.
    private static void AShortfallIsNotFilledFromAfar()
    {
        var s = World(ExecutorFixtures.Corps(1, 100f, 2000f), ExecutorFixtures.Corps(2, 300f, 9000f), ExecutorFixtures.Corps(3, 450f, 9000f));
        var claims = Reserve(s, 6000f);
        AssertEqual(Claimant.Capital, claims[0].Claimant, "what is in range is held");
        AssertEqual(Claimant.None, claims[1].Claimant, "the floor stays short rather than draw from beyond the range");
        AssertEqual(Claimant.None, claims[2].Claimant, "whatever the strength out there");
        float held = 0f;
        for (int i = 0; i < claims.Length; i++)
            if (claims[i].Claimant == Claimant.Capital) held += s.Own[i].Strength;
        AssertNear(2000f, held, 0.01f, "the shortfall shows as held under the floor");
    }

    // Fredericksburg: a formation just outside the range from the capital, two reviews running, the capital's own guard ready on one and unready
    // on the next. The shipped rule took the outsider into the reserve on the unready day and handed it back to the covering force on the ready
    // one, a march each time. Now it is the sector's reserve both days and the capital never has it.
    private static void TheCoveringForceAndTheCapitalNoLongerAlternateOutsideTheRange()
    {
        Claimant[] days = new Claimant[2];
        for (int day = 0; day < 2; day++)
        {
            var s = ExecutorFixtures.Wide();
            s.RelocationRange = 200f;
            s.CapitalX = 140f;
            var guard = ExecutorFixtures.Corps(1, 130f, 6000f);
            if (day == 1) guard.ReadinessStep = 0;
            s.Own.Add(guard);
            s.Own.Add(ExecutorFixtures.Corps(2, 350f, 5000f));   // 210 from the capital: outside 200
            var sectors = ExecutorFixtures.Sectors(s);
            int sector = sectors.SectorAt(350f, 0f);
            var need = new float[sectors.Count];
            var nearX = new float[sectors.Count];
            var nearZ = new float[sectors.Count];
            var sectorHeld = new float[sectors.Count];
            need[sector] = 4000f;
            nearX[sector] = 450f;
            var claims = ClaimTable.Build(s, sectors, need, nearX, nearZ, 6000f, null, null, null, null, ClaimTable.RowSources, null, out sectorHeld);
            AssertEqual(Claimant.Capital, claims[0].Claimant, "day " + day + ": the guard holds the capital");
            days[day] = claims[1].Claimant;
        }
        AssertEqual(Claimant.Hold, days[0], "ready guard: the outsider is the sector's reserve");
        AssertEqual(Claimant.Hold, days[1], "unready guard: the outsider is still the sector's reserve, not pulled to the capital");
    }

    // ---- the mover ----

    private static FormationRecord Marching(int id, float x)
    {
        var f = ExecutorFixtures.Corps(id, x, 6000f);
        f.WaypointX = 0f;
        f.WaypointZ = 0f;
        f.Flags |= FormationFlags.HasPath;
        return f;
    }

    private static MoveTracker GuardOnItsWay(CampaignSnapshot s, int id, float fromX)
    {
        var tracker = new MoveTracker();
        tracker.RecordIssued(id, fromX, 0f, 0f, 0f, s.DaySerial - 1, queued: false, role: "CapitalGuard");
        return tracker;
    }

    private static Allocation Only(Assignment a)
    {
        var allocation = new Allocation { Alliance = 0 };
        allocation.Assignments.Add(a);
        return allocation;
    }

    private static TheaterPlan NoPlan(CampaignSnapshot s) => new TheaterPlan { Alliance = 0, DaySerial = s.DaySerial };

    private static MoverTally Review(CampaignSnapshot s, Allocation a, FakeMoverHost host, MoveTracker tracker)
    {
        return CampaignMover.Review(ExecutorFixtures.Inputs(s, NoPlan(s), a, ExecutorFixtures.OpenTempo()), host, tracker, ExecutorFixtures.State());
    }

    private static Assignment Holds(int id, string reason)
    {
        return new Assignment { InstanceId = id, Role = FormationRole.Hold, SectorId = 0, TargetX = 90f, TargetZ = 0f, Reason = reason };
    }

    // A guard on the march that the reserve no longer holds, whose new role has no post, is stopped where it stands by vanilla's own campaign
    // stop, as an ended push's corps is; it was marching for a role it no longer has.
    private static void AMarchingExGuardWithNoPostIsStopped()
    {
        var s = World(Marching(1, 90f));
        var host = new FakeMoverHost();
        var tracker = GuardOnItsWay(s, 1, 150f);
        var tally = Review(s, Only(Holds(1, ForceAllocator.ReasonHoldReserve)), host, tracker);
        AssertEqual("1", string.Join(",", host.Stops), "vanilla's stop is sent to the ex-guard");
        AssertEqual(0, host.Moves.Count, "and no move");
        AssertTrue(tracker.IsStop(1) && tracker.InTransit(1), "recorded as a stop at its own place");
        AssertEqual(1, tally.Stopped, "counted");
    }

    // The shipped mover already re-targets a changed role that has a post (the covering force's, a department's, the main effort's): the ex-guard
    // is sent there by the ordinary dispatch, and the stop is not sent.
    private static void AMarchingExGuardWithAPostIsRetargeted()
    {
        var s = World(Marching(1, 90f));
        var host = new FakeMoverHost();
        var tracker = GuardOnItsWay(s, 1, 150f);
        var post = new Assignment { InstanceId = 1, Role = FormationRole.Redeploy, SectorId = 0, TargetX = 300f, TargetZ = 0f, Reason = ForceAllocator.ReasonCoveringForce, Withheld = true };
        var tally = Review(s, Only(post), host, tracker);
        AssertEqual(0, host.Stops.Count, "it has a post: not stopped");
        AssertEqual("1:300", string.Join(",", host.Moves.Select(m => m.id + ":" + (int)m.x)), "it is sent to the covering post");
        AssertEqual(1, tally.Issued, "as one move");
    }

    // This rule is for a guard only: a formation marching for another role (a main effort's march, say) that goes to a Hold is not touched by it.
    private static void OnlyAnExGuardIsStoppedByThisRule()
    {
        var s = World(Marching(1, 90f), Marching(2, 95f));
        var host = new FakeMoverHost();
        var tracker = GuardOnItsWay(s, 1, 150f);
        tracker.RecordIssued(2, 150f, 0f, 0f, 0f, s.DaySerial - 1, queued: false, role: "MainEffort");
        var allocation = Only(Holds(1, ForceAllocator.ReasonHoldReserve));
        allocation.Assignments.Add(Holds(2, ForceAllocator.ReasonHoldReserve));
        Review(s, allocation, host, tracker);
        AssertEqual("1", string.Join(",", host.Stops), "the ex-guard is stopped, the ex-main-effort corps is left as before");
        // A guard that is still a guard is never stopped, and nor is one standing still.
        var still = World(Marching(1, 90f), ExecutorFixtures.Corps(3, 50f, 6000f));
        var calm = new FakeMoverHost();
        var kept = GuardOnItsWay(still, 1, 150f);
        kept.RecordIssued(3, 50f, 0f, 0f, 0f, still.DaySerial - 1, queued: false, role: "CapitalGuard");
        var again = Only(new Assignment { InstanceId = 1, Role = FormationRole.CapitalGuard, SectorId = 0, TargetX = 0f, TargetZ = 0f, Reason = "capital-floor", Withheld = true });
        again.Assignments.Add(Holds(3, ForceAllocator.ReasonHoldReserve));
        Review(still, again, calm, kept);
        AssertEqual(0, calm.Stops.Count, "a guard still a guard keeps marching, and an ex-guard with nothing to stop (neither path nor order) is not stopped");
    }

    // An ex-guard whose new role is the stop of an ended push's corps (Hold, committed-wait) is the Hold step's to stop: this rule does not send
    // the stop a second time in one review.
    private static void AnExGuardIsStoppedOnceWhenTheHoldStepWouldStopItToo()
    {
        var s = World(Marching(1, 90f));
        var host = new FakeMoverHost();
        var tracker = GuardOnItsWay(s, 1, 150f);
        var tally = Review(s, Only(Holds(1, ForceAllocator.ReasonCommittedWait)), host, tracker);
        AssertEqual("1", string.Join(",", host.Stops), "one stop, not two");
        AssertEqual(1, tally.Stopped, "and one counted");
    }

    // Vanilla's job 6 re-targets every idle unit in unitsinoffensiveoperations to an enemy objective (13968-14061): a stopped ex-guard that stays in
    // the list is re-sent the same day. ReleaseFromOffense no longer names it (its role is not a guard's), so the stop takes it off the list.
    private static void AStoppedExGuardLeavesVanillasOffensiveList()
    {
        var marching = Marching(1, 90f);
        marching.Flags |= FormationFlags.InOffensiveOps;
        var s = World(marching, Marching(2, 95f));   // 2 is listed in nothing: no delist call for it
        var host = new FakeMoverHost();
        var tracker = GuardOnItsWay(s, 1, 150f);
        tracker.RecordIssued(2, 150f, 0f, 0f, 0f, s.DaySerial - 1, queued: false, role: "CapitalGuard");
        var allocation = Only(Holds(1, ForceAllocator.ReasonHoldReserve));
        allocation.Assignments.Add(Holds(2, ForceAllocator.ReasonHoldReserve));
        Review(s, allocation, host, tracker);
        AssertEqual("1,2", string.Join(",", host.Stops), "both are stopped");
        AssertEqual("1", string.Join(",", host.Listed.Where(l => !l.listed).Select(l => l.id)), "the listed one leaves vanilla's offensive list, the unlisted one is not touched");
    }

    private static Assignment CoveringPost(int id)
    {
        return new Assignment { InstanceId = id, Role = FormationRole.Redeploy, SectorId = 0, TargetX = 300f, TargetZ = 0f, Reason = ForceAllocator.ReasonCoveringForce, Withheld = true };
    }

    // The new post is dispatched, vanilla refuses it (MoveUnitTo false): the refusal overwrites the record, and the old march to the capital would
    // run on. It is stopped, and the refusal's record is kept (not a stop's) so a second refusal still gives the move up.
    private static void AnExGuardWhoseNewTargetVanillaRefusesIsStillStopped()
    {
        var marching = Marching(1, 90f);
        marching.Flags |= FormationFlags.InOffensiveOps;
        var s = World(marching);
        var host = new FakeMoverHost { MoveResult = false };
        var tracker = GuardOnItsWay(s, 1, 150f);
        var tally = Review(s, Only(CoveringPost(1)), host, tracker);
        AssertEqual("move:1,stop:1", string.Join(",", host.Calls), "the refused move, then the stop of the old march");
        AssertEqual(1, tally.Stopped, "counted");
        AssertFalse(tracker.IsStop(1), "the refusal's record stands, not a stop's");
        tracker.TryGetMove(1, out _, out _, out float tx, out _, out _, out _, out _, out string role);
        AssertTrue(tx == 300f && role == "Redeploy", "it is the refused move to the post");
        AssertEqual("1", string.Join(",", host.Listed.Where(l => !l.listed).Select(l => l.id).Distinct()), "and off the offensive list (the covering role's release names it too; the list call is idempotent)");
    }

    // Restricted ground refuses the post without a MoveUnitTo call at all (MoveIssue): the same.
    private static void AnExGuardWhoseNewTargetIsRestrictedIsStillStopped()
    {
        var s = World(Marching(1, 90f));
        var host = new FakeMoverHost { Restricted = (x, z) => x >= 300f };
        var tracker = GuardOnItsWay(s, 1, 150f);
        Review(s, Only(CoveringPost(1)), host, tracker);
        AssertEqual("stop:1", string.Join(",", host.Calls), "no move was made, and the old march is stopped");
        AssertFalse(tracker.IsStop(1), "the refusal is the record");
    }

    // A day's cap or gate that sends nothing leaves the old march running too: the stop is for every ex-guard the dispatch did not order, and the
    // untouched record is replaced by the stop's.
    private static void AnExGuardTheDayHoldsBackIsStopped()
    {
        var s = World(Marching(1, 90f));
        var host = new FakeMoverHost();
        var tracker = GuardOnItsWay(s, 1, 150f);
        for (int i = 10; i < 10 + MoveTracker.MaxMovesPerDay; i++) tracker.RecordIssued(i, 300f, 0f, 100f, 0f, s.DaySerial, queued: false);
        Review(s, Only(CoveringPost(1)), host, tracker);
        AssertEqual("stop:1", string.Join(",", host.Calls), "the cap is spent: no move, and the old march is stopped");
        AssertTrue(tracker.IsStop(1), "the guard's record, untouched, becomes the stop's");
    }

    // The user's save was made with the shipped DLL while guards were marching to Richmond. The sidecar restores a planner move with its target and
    // no role, so a role-only test would miss them. A restored record that targets the capital point, on a formation still marching, is the
    // ex-guard's march all the same: stopped, and off the offensive list.
    private static void ARestoredRecordWithNoRoleStillStopsAMarchToTheCapital()
    {
        var marching = Marching(1, 90f);
        marching.Flags |= FormationFlags.InOffensiveOps;
        marching.WaypointX = 400f;   // the live waypoint is elsewhere: the record alone says where it was going
        marching.WaypointZ = 0f;
        var s = World(marching);
        var tracker = new MoveTracker();
        tracker.Restore(new[] { new MoveRecord { InstanceId = 1, UnitKey = "corps1:1", TargetX = s.CapitalX, TargetZ = s.CapitalZ, IssuedDay = s.DaySerial - 3, Retries = 0, ObjectiveId = -1 } });
        tracker.TryGetMove(1, out _, out _, out _, out _, out _, out _, out _, out string role);
        AssertTrue(role == null, "fixture: a restored record carries no role");
        var host = new FakeMoverHost();
        var tally = Review(s, Only(Holds(1, ForceAllocator.ReasonHoldReserve)), host, tracker);
        AssertEqual("1", string.Join(",", host.Stops), "stopped");
        AssertEqual(1, tally.Stopped, "counted");
        AssertEqual("1", string.Join(",", host.Listed.Where(l => !l.listed).Select(l => l.id)), "and off vanilla's offensive list");
    }

    // A march the planner never issued is vanilla's own (AssignUnitToDefendCapital sends groups to the capital, 11668-11869): with a waypoint at the
    // capital and no planner record at all it is not an ex-guard's, and stopping it would make the AI worse than vanilla.
    private static void AMarchWithNoPlannerRecordIsVanillasAndIsNeverStopped()
    {
        var s = World(Marching(1, 90f));   // Marching's waypoint is the capital
        var host = new FakeMoverHost();
        Review(s, Only(Holds(1, ForceAllocator.ReasonHoldReserve)), host, new MoveTracker());
        AssertEqual(0, host.Stops.Count, "no planner record: not stopped, whatever the waypoint");
    }

    // A formation in vanilla's groupstodefendcapital (FormationFlags.CapitalDefense) is vanilla's to march, even with a guard's record: not stopped.
    private static void AFormationInVanillasCapitalDefenceListIsNeverStopped()
    {
        var pledged = Marching(1, 90f);
        pledged.Flags |= FormationFlags.CapitalDefense;
        var s = World(pledged);
        var host = new FakeMoverHost();
        Review(s, Only(Holds(1, ForceAllocator.ReasonHoldReserve)), host, GuardOnItsWay(s, 1, 150f));
        AssertEqual(0, host.Stops.Count, "in vanilla's capital-defence list: not stopped");
    }

    // Left alone: a restored record to some other place with the waypoint elsewhere, and a march a record names for another role even though
    // the waypoint is the capital's.
    private static void AMarchForAnotherRoleOrAnotherPlaceIsLeftAlone()
    {
        var elsewhere = Marching(1, 90f);
        elsewhere.WaypointX = 400f;
        var named = Marching(2, 95f);
        var s = World(elsewhere, named);
        var tracker = new MoveTracker();
        tracker.Restore(new[] { new MoveRecord { InstanceId = 1, UnitKey = "corps1:1", TargetX = 400f, TargetZ = 0f, IssuedDay = s.DaySerial - 3, ObjectiveId = -1 } });
        tracker.RecordIssued(2, 150f, 0f, 0f, 0f, s.DaySerial - 1, queued: false, role: "Redeploy");
        var allocation = Only(Holds(1, ForceAllocator.ReasonHoldReserve));
        allocation.Assignments.Add(Holds(2, ForceAllocator.ReasonHoldReserve));
        var host = new FakeMoverHost();
        Review(s, allocation, host, tracker);
        AssertEqual(0, host.Stops.Count, "neither is a march to the capital for a guard's role");
    }

    // #4 (DefensiveOpsPatch) pledges the allocation's nearest CapitalGuard into vanilla's groupstodefendcapital, and vanilla marches it to its slot
    // (AssignUnitToDefendCapital 11829). The guard's claim holds it where it stands, so the mover must leave a pledged guard to vanilla, or it
    // would send it back to its own place every review.
    private static void AGuardInVanillasCapitalDefenceListIsNotDispatchedByTheMover()
    {
        var guard = new Assignment { InstanceId = 1, Role = FormationRole.CapitalGuard, SectorId = 0, TargetX = 90f, TargetZ = 0f, Reason = "capital-floor", Withheld = true };
        var pledged = Marching(1, 90f);   // its waypoint is the capital: vanilla is marching it
        pledged.Flags |= FormationFlags.CapitalDefense;
        var host = new FakeMoverHost();
        Review(World(pledged), Only(guard), host, new MoveTracker());
        AssertEqual(0, host.Moves.Count, "a pledged guard is vanilla's to march: no move to its own place");
        var free = new FakeMoverHost();
        Review(World(Marching(1, 90f)), Only(guard), free, new MoveTracker());
        AssertEqual("1:90", string.Join(",", free.Moves.Select(m => m.id + ":" + (int)m.x)), "the control: an unpledged guard is sent to its place");
    }

    // The W&L player's own command and map group are never moved or stopped (WlChainRules.MayMove), whatever a restored record says.
    private static void ThePlayersFormationsAreNeverStoppedAsExGuards()
    {
        foreach (var chain in new[] { WlChainClass.PlayerCommand, WlChainClass.PlayerMapGroup })
        {
            var mine = Marching(1, 90f);
            mine.Chain = chain;
            var s = World(mine);
            var tracker = new MoveTracker();
            tracker.Restore(new[] { new MoveRecord { InstanceId = 1, UnitKey = "corps1:1", TargetX = s.CapitalX, TargetZ = s.CapitalZ, IssuedDay = s.DaySerial - 3, ObjectiveId = -1 } });
            var host = new FakeMoverHost();
            Review(s, Only(Holds(1, ForceAllocator.ReasonHoldReserve)), host, tracker);
            AssertEqual(0, host.Stops.Count, chain + ": never stopped");
            AssertEqual(0, host.Moves.Count, chain + ": nor moved");
        }
    }

    // A formation in battle is not the mover's to stop (TheaterPlanner.Movable).
    private static void ABusyFormationIsNeverStoppedAsAnExGuard()
    {
        var busy = Marching(1, 90f);
        busy.Flags |= FormationFlags.InBattle;
        var s = World(busy);
        var host = new FakeMoverHost();
        Review(s, Only(Holds(1, ForceAllocator.ReasonHoldReserve)), host, GuardOnItsWay(s, 1, 150f));
        AssertEqual(0, host.Stops.Count, "in battle: not stopped");
    }

    // [Reserves] capital= and the allocator's capital shortfall read what row 6 counted (the claims it made and the immobile bodies standing in
    // the range), not the CapitalGuard roles alone: a busy corps at the capital that meets the floor leaves no shortfall.
    private static void TheReservesRowAndTheShortfallCountWhatTheRuleCounts()
    {
        var busy = ExecutorFixtures.Corps(1, 20f, 7000f);
        busy.Flags |= FormationFlags.InBattle;
        var s = World(busy, ExecutorFixtures.Corps(2, 60f, 2000f));
        var claims = ClaimTable.Fresh(s);
        claims[0].Claimant = Claimant.Immobile;
        ReserveRules.Capital(s, 6000f, claims);
        AssertEqual(Claimant.None, claims[1].Claimant, "the busy corps meets the floor alone: nothing more is claimed");
        AssertNear(7000f, ReserveRules.CapitalHeld(s, claims), 0.01f, "and the held figure is its strength, though no formation is a CapitalGuard");
        claims[1].Claimant = Claimant.Capital;
        AssertNear(9000f, ReserveRules.CapitalHeld(s, claims), 0.01f, "a Capital claim adds its own");
    }
}
