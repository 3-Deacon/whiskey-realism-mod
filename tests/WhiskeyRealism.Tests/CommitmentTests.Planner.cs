using System;
using System.Collections.Generic;
using System.Linq;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;

using static TestHarness;
using static TestFixtures;

// F7, the planner's half: the last mass, row 4 of the ownership table (a dispatched member is committed), the capacity that counts it, the mass
// that takes it first and the end of the commitment. The world is ExecutorFixtures.Wide(): the west objective 98 (x 100) is the push, the
// minimum strength 2000; corps 1 and 2 (one army, 4000 men each, x 220 and 240) are the push, corps 3 (9000, x 20) the capital guard.
internal static partial class CommitmentTests
{
    private static (string name, Action run)[] PlannerCases()
    {
        return new (string name, Action run)[]
        {
            ("ADispatchedMemberStaysMassedThroughAReadinessDip", ADispatchedMemberStaysMassedThroughAReadinessDip),
            ("ThePushsOddsCountItsUnreadyCorps", ThePushsOddsCountItsUnreadyCorps),
            ("AnUndispatchedMemberIsNotCommitted", AnUndispatchedMemberIsNotCommitted),
            ("AnUncommittedFormationStillNeedsStepTwo", AnUncommittedFormationStillNeedsStepTwo),
            ("RetreatOrLowMoraleStillRelieves", RetreatOrLowMoraleStillRelieves),
            ("LowSupplyStillRecovers", LowSupplyStillRecovers),
            ("ABuilderIsNeverCommitted", ABuilderIsNeverCommitted),
            ("AGiveUpOnTheObjectiveIsNotCommitted", AGiveUpOnTheObjectiveIsNotCommitted),
            ("PlayerCommandIsNeverCommitted", PlayerCommandIsNeverCommitted),
            ("ANonEmptyMassReplacesTheLastMass", ANonEmptyMassReplacesTheLastMass),
            ("ANoCandidateReviewClearsTheLastMass", ANoCandidateReviewClearsTheLastMass),
            ("ABattleKeepsTheLastMassMembership", ABattleKeepsTheLastMassMembership),
            ("AVanillaDefenceLoanResumesTheCommitment", AVanillaDefenceLoanResumesTheCommitment),
            ("ABusyMemberOfAnEndedPushIsDroppedWithIt", ABusyMemberOfAnEndedPushIsDroppedWithIt),
            ("RowFourRefusesAStarvedOrDemoralisedFormation", RowFourRefusesAStarvedOrDemoralisedFormation),
            ("CommitmentEndsWhenD4EndsThePush", CommitmentEndsWhenD4EndsThePush),
            ("TheEndReviewWaitsInCommittedWait", TheEndReviewWaitsInCommittedWait),
            ("ACommittedWaitIsCountedInNoHoldInG3OrG4", ACommittedWaitIsCountedInNoHoldInG3OrG4),
            ("AnEndedPushsReadyCorpsMayJoinTheNewMassAsOrdinaryMembers", AnEndedPushsReadyCorpsMayJoinTheNewMassAsOrdinaryMembers),
            ("AnEndedDecisionsFormationIsUnclaimed", AnEndedDecisionsFormationIsUnclaimed),
            ("FlagOffStillUpdatesTheLastMass", FlagOffStillUpdatesTheLastMass),
            ("FlagOffRestoresD5", FlagOffRestoresD5),
        };
    }

    // ---- fixtures ----

    internal static FormationRecord Corps(int id, float x, float strength, int readiness = 3, int parent = -1, FormationFlags flags = 0)
    {
        var f = ExecutorFixtures.Corps(id, x, strength);
        f.ReadinessStep = readiness;
        f.ParentInstanceId = parent;
        f.Flags |= flags;
        return f;
    }

    // The push world: corps 1 and 2 (army 50), the capital guard 3. readiness2 and the tweak shape the second corps.
    private static CampaignSnapshot PushWorld(int readiness2 = 3, Func<FormationRecord, FormationRecord> tweak2 = null)
    {
        var s = ExecutorFixtures.Wide();
        s.MinStrengthForMovement = 2000f;
        s.Own.Add(Corps(1, 220f, 4000f, 3, 50));
        var second = Corps(2, 240f, 4000f, readiness2, 50);
        s.Own.Add(tweak2 != null ? tweak2(second) : second);
        s.Own.Add(Corps(3, 20f, 9000f));
        return s;
    }

    internal static DispatchedMove[] Moves(int objective, params int[] ids)
    {
        return ids.Select(id => new DispatchedMove { InstanceId = id, ObjectiveId = objective }).ToArray();
    }

    internal static TheaterPlan Replan(CampaignSnapshot s, PlannerState state, IReadOnlyList<DispatchedMove> dispatched, IReadOnlyList<GaveUpMove> gaveUp = null,
                                       IReadOnlyList<HeldBuilder> held = null)
    {
        return Replan(s, state, dispatched, out _, gaveUp, held);
    }

    internal static TheaterPlan Replan(CampaignSnapshot s, PlannerState state, IReadOnlyList<DispatchedMove> dispatched, out string trace,
                                       IReadOnlyList<GaveUpMove> gaveUp = null, IReadOnlyList<HeldBuilder> held = null)
    {
        return TheaterPlanner.Plan(s, ExecutorFixtures.Sectors(s), null, state, null, null, ExecutorFixtures.Speed(), out trace, out EnemyPicture _, gaveUp, held,
            null, dispatched);
    }

    // A real plan over a last-mass world for the ownership registry (OwnershipTableTests.PlanInputs): the push that goes on with its second corps
    // at readiness step 1, or the push D4 ended by accomplishing 98.
    internal static TheaterPlan PlanInputWorld(bool ended, out CampaignSnapshot s, out SectorModel sectors)
    {
        var state = new PlannerState();
        Replan(PushWorld(), state, null);
        s = ended ? AccomplishedWorld(1) : PushWorld(readiness2: 1);
        sectors = ExecutorFixtures.Sectors(s);
        return Replan(s, state, Moves(98, 1, 2));
    }

    // The first review: the push on 98 with corps 1 and 2 in the mass, which leaves the last mass in the state.
    private static PlannerState FirstDay(out TheaterPlan plan)
    {
        var state = new PlannerState();
        plan = Replan(PushWorld(), state, null);
        AssertEqual(98, plan.MainObjectiveId, "the first review pushes the west objective");
        AssertEqual("1,2", Ids(plan.MassInstanceIds), "with the army of corps 1 and 2 as its mass");
        return state;
    }

    private static string Ids(IEnumerable<int> ids)
    {
        return string.Join(",", ids.OrderBy(i => i));
    }

    private static Claim ClaimOf(TheaterPlan plan, int instanceId)
    {
        return plan.Claims.First(c => c.InstanceId == instanceId);
    }

    private static string Describe(TheaterPlan plan)
    {
        return string.Join(" ", plan.Claims.Select(c => c.InstanceId + "=" + c.Claimant + (c.Reason != null ? "/" + c.Reason : "")));
    }

    private static T WithCommitment<T>(bool on, Func<T> run)
    {
        bool before = PlannerSettings.MainEffortCommitment;
        PlannerSettings.MainEffortCommitment = on;
        try { return run(); }
        finally { PlannerSettings.MainEffortCommitment = before; }
    }

    // ---- the commitment ----

    // Root cause 4: the push's second corps dips to readiness step 1 on the march. It was dispatched, so it stays in the capacity and the mass.
    private static void ADispatchedMemberStaysMassedThroughAReadinessDip()
    {
        var state = FirstDay(out _);
        AssertEqual(98, state.LastMassObjective, "the first review recorded the last mass");
        AssertTrue(state.InLastMass("corps1:1") && state.InLastMass("corps2:2") && !state.InLastMass("corps3:3"), "the two corps of the mass, by unit key");
        var plan = Replan(PushWorld(readiness2: 1), state, Moves(98, 1, 2));
        AssertEqual(98, plan.MainObjectiveId, "the push is kept");
        AssertEqual("hold", plan.SwitchReason, "by the hold rule");
        AssertEqual("1,2", Ids(plan.MassInstanceIds), "the unready corps is still in the mass: " + Describe(plan));
        var claim = ClaimOf(plan, 2);
        AssertEqual(Claimant.Mass, claim.Claimant, "a Mass claim");
        AssertEqual(ClaimTable.ReasonCommitted, claim.Reason, "committed to the objective it marches on");
        AssertEqual(8000f, plan.MainCapacityStrength, "and its men are in the capacity the odds were judged on");
    }

    // The odds are judged with the unready corps in them: the trace of the push's own candidate carries the whole 8000.
    private static void ThePushsOddsCountItsUnreadyCorps()
    {
        var state = FirstDay(out _);
        Replan(PushWorld(readiness2: 1), state, Moves(98, 1, 2), out string trace);
        string candidate = trace.Split('|').First(c => c.TrimStart().StartsWith("98@", StringComparison.Ordinal));
        AssertContains(candidate, " mass=8000", "the candidate's mass counts the dispatched corps whatever its readiness: " + candidate);
    }

    private static void AnUndispatchedMemberIsNotCommitted()
    {
        var state = FirstDay(out _);
        var plan = Replan(PushWorld(readiness2: 1), state, Moves(98, 1));
        AssertEqual("1", Ids(plan.MassInstanceIds), "a member the tempo gate deferred, or the mover has not sent, falls under D5's readiness rule: " + Describe(plan));
        AssertEqual(Claimant.None, ClaimOf(plan, 2).Claimant, "and is owned by nobody");
        var other = Replan(PushWorld(readiness2: 1), FirstDay(out _), Moves(97, 1, 2));
        AssertEqual("1", Ids(other.MassInstanceIds), "a dispatch toward another objective is no dispatch toward this one");
    }

    private static void AnUncommittedFormationStillNeedsStepTwo()
    {
        var state = FirstDay(out _);
        var s = PushWorld();
        s.Own.Add(Corps(4, 230f, 4000f, readiness: 1, parent: 51));
        var plan = Replan(s, state, Moves(98, 1, 2, 4));
        AssertFalse(plan.MassInstanceIds.Contains(4), "a formation outside the last mass is not exempt, though a record names it: " + Describe(plan));
        AssertEqual(Claimant.None, ClaimOf(plan, 4).Claimant, "and is nobody's");
    }

    private static void RetreatOrLowMoraleStillRelieves()
    {
        foreach (var variant in new (string name, Func<FormationRecord, FormationRecord> tweak)[]
        {
            ("morale", f => { f.Morale = 0.1f; return f; }),
            ("retreat", f => { f.Flags |= FormationFlags.OnRetreat; return f; }),
        })
        {
            var state = FirstDay(out _);
            var plan = Replan(PushWorld(readiness2: 1, tweak2: variant.tweak), state, Moves(98, 1, 2));
            AssertEqual("1", Ids(plan.MassInstanceIds), variant.name + ": vanilla relieves an operation member for retreat or morale (14072): " + Describe(plan));
            AssertFalse(ClaimOf(plan, 2).Claimant == Claimant.Mass || ClaimOf(plan, 2).Claimant == Claimant.Committed, variant.name + ": it is not committed");
            AssertTrue(plan.CommitmentEnded.Contains(2) || variant.name == "retreat", variant.name + ": a movable member the mass dropped has its commitment ended");
        }
    }

    private static void LowSupplyStillRecovers()
    {
        var state = FirstDay(out _);
        var plan = Replan(PushWorld(readiness2: 1, tweak2: f => { f.Supply01 = 0.1f; return f; }), state, Moves(98, 1, 2));
        AssertEqual(Claimant.Recover, ClaimOf(plan, 2).Claimant, "below the supply floor the corps recovers, whatever its dispatch: " + Describe(plan));
        AssertEqual("1", Ids(plan.MassInstanceIds), "and is out of the mass");
        AssertTrue(plan.CommitmentEnded.Contains(2), "its commitment ended");
    }

    private static void ABuilderIsNeverCommitted()
    {
        var state = FirstDay(out _);
        var held = new[] { new HeldBuilder { InstanceId = 2, SectorId = 0, TargetX = 240f, TargetZ = 0f, SiteX = 240f, SiteZ = 0f, Wished = false } };
        var plan = Replan(PushWorld(readiness2: 1), state, Moves(98, 1, 2), held: held);
        AssertFalse(plan.MassInstanceIds.Contains(2), "a Marching depot builder is the depot's, not the push's: " + Describe(plan));
        AssertFalse(ClaimOf(plan, 2).Claimant == Claimant.Committed || ClaimOf(plan, 2).Reason == ClaimTable.ReasonCommitted, "and not committed");
    }

    private static void AGiveUpOnTheObjectiveIsNotCommitted()
    {
        var state = FirstDay(out _);
        var gaveUp = new[] { new GaveUpMove { InstanceId = 2, TargetX = 100f, TargetZ = 0f, ObjectiveId = 98 } };
        var plan = Replan(PushWorld(readiness2: 1), state, Moves(98, 1, 2), gaveUp);
        AssertEqual("1", Ids(plan.MassInstanceIds), "a member that gave up on the objective is out of its mass: " + Describe(plan));
        AssertEqual(Claimant.None, ClaimOf(plan, 2).Claimant, "and row 4 skips it: P8 holds it in place");
    }

    private static void PlayerCommandIsNeverCommitted()
    {
        foreach (var chain in new[] { WlChainClass.PlayerCommand, WlChainClass.PlayerMapGroup })
        {
            var state = FirstDay(out _);
            var plan = Replan(PushWorld(readiness2: 1, tweak2: f => { f.Chain = chain; return f; }), state, Moves(98, 1, 2));
            AssertFalse(plan.MassInstanceIds.Contains(2), chain + ": the player's command, or his map group, is nobody's to push: " + Describe(plan));
            AssertFalse(ClaimOf(plan, 2).Claimant == Claimant.Committed || ClaimOf(plan, 2).Reason == ClaimTable.ReasonCommitted, chain + ": and never committed");
        }
    }

    // ---- the last mass ----

    private static void ANonEmptyMassReplacesTheLastMass()
    {
        var state = FirstDay(out _);
        var s = PushWorld();
        s.Own.RemoveAt(s.IndexOfOwn(2));
        s.Own.Add(Corps(5, 235f, 4000f, 3, 52));
        var plan = Replan(s, state, Moves(98, 1));
        AssertEqual(98, state.LastMassObjective, "the push goes on");
        AssertTrue(state.InLastMass("corps1:1"), "corps 1 is in it");
        AssertFalse(state.InLastMass("corps2:2"), "corps 2 left the snapshot and the mass: " + Describe(plan));
        AssertEqual(string.Join(",", plan.MassInstanceIds.OrderBy(i => i).Select(i => "corps" + i + ":" + i).OrderBy(k => k, StringComparer.Ordinal)),
            string.Join(",", state.LastMassKeys.OrderBy(k => k, StringComparer.Ordinal)), "the last mass is today's mass");
    }

    private static void ANoCandidateReviewClearsTheLastMass()
    {
        var state = FirstDay(out _);
        var s = PushWorld(readiness2: 1);
        for (int i = 0; i < s.Objectives.Count; i++)
        {
            var o = s.Objectives[i];
            o.HolderAlliance = 0;
            s.Objectives[i] = o;
        }
        var plan = Replan(s, state, Moves(98, 1, 2));
        AssertEqual(-1, plan.MainObjectiveId, "no main effort: " + plan.SwitchReason);
        AssertEqual(-1, state.LastMassObjective, "the last mass is cleared");
        AssertEqual(0, state.LastMassKeys.Count, "with its members");
        AssertEqual("1,2", Ids(plan.CommitmentEnded), "both corps' commitment ended");
        AssertEqual(Claimant.Committed, ClaimOf(plan, 1).Claimant, "the push is gone, so the ready corps waits too: " + Describe(plan));
        AssertEqual(Claimant.Committed, ClaimOf(plan, 2).Claimant, "and the unready one");
    }

    // The members that cannot be sent today stay in the last mass: a battle or a vanilla defensive operation is a loan, not a leaving.
    private static void ABattleKeepsTheLastMassMembership()
    {
        AKeptMembership(FormationFlags.InBattle, "a battle");
    }

    private static void AVanillaDefenceLoanResumesTheCommitment()
    {
        AKeptMembership(FormationFlags.InDefensiveOps, "a vanilla defensive operation");
    }

    // The membership kept for a loan is for as long as the push goes on: when D4 ends it, a busy member goes with it.
    private static void ABusyMemberOfAnEndedPushIsDroppedWithIt()
    {
        var state = FirstDay(out _);
        var s = AccomplishedWorld(3);
        var f = s.Own[s.IndexOfOwn(2)];
        f.Flags |= FormationFlags.InBattle;
        s.Own[s.IndexOfOwn(2)] = f;
        var plan = Replan(s, state, Moves(98, 1, 2));
        AssertTrue(plan.MainObjectiveId != 98, "the push ended");
        AssertFalse(state.InLastMass("corps2:2"), "the battle member is not in the new last mass");
        AssertTrue(plan.CommitmentEnded.Contains(2), "and its commitment ended");
    }

    // Row 4 is self-contained: whatever the order of the rows, it refuses a formation vanilla would relieve (its raw supply or morale below the floors).
    private static void RowFourRefusesAStarvedOrDemoralisedFormation()
    {
        foreach (var tweak in new Func<FormationRecord, FormationRecord>[] { f => { f.Supply01 = 0.1f; return f; }, f => { f.Morale = 0.1f; return f; } })
        {
            var s = PushWorld();
            var f2 = tweak(s.Own[s.IndexOfOwn(2)]);
            s.Own[s.IndexOfOwn(2)] = f2;
            var state = PushOn98(1, 2);
            var ctx = new ClaimContext(s, ExecutorFixtures.Sectors(s), null, state, null, null, Moves(98, 1, 2));
            var claims = ClaimTable.Fresh(s);
            ClaimTable.ApplySource(Commitment.Source, in ctx, claims);
            AssertEqual(Claimant.Committed, claims[s.IndexOfOwn(1)].Claimant, "the healthy dispatched corps is committed");
            AssertEqual(Claimant.None, claims[s.IndexOfOwn(2)].Claimant, "the corps below the supply or morale floor is not");
        }
    }

    private static void AKeptMembership(FormationFlags busy, string what)
    {
        var state = FirstDay(out _);
        var away = Replan(PushWorld(tweak2: f => { f.Flags |= busy; return f; }), state, Moves(98, 1, 2));
        AssertEqual("1", Ids(away.MassInstanceIds), what + ": the busy corps is not in today's mass: " + Describe(away));
        AssertTrue(state.InLastMass("corps2:2"), what + ": but it stays in the last mass");
        AssertFalse(away.CommitmentEnded.Contains(2), what + ": and its commitment has not ended");
        var back = Replan(PushWorld(readiness2: 1), state, Moves(98, 1, 2));
        AssertEqual("1,2", Ids(back.MassInstanceIds), what + ": once it is back, unready as it is, it is committed again: " + Describe(back));
    }

    // ---- the end ----

    // D4 ends the push (the objective accomplished): the unready corps waits where it stands, the ready one is an ordinary member of the new mass.
    private static CampaignSnapshot AccomplishedWorld(int readiness2)
    {
        var s = PushWorld(readiness2);
        for (int i = 0; i < s.Objectives.Count; i++)
        {
            var o = s.Objectives[i];
            if (o.ObjectiveId == 98) { o.Accomplished = true; s.Objectives[i] = o; }
        }
        return s;
    }

    private static void CommitmentEndsWhenD4EndsThePush()
    {
        var state = FirstDay(out _);
        var plan = Replan(AccomplishedWorld(1), state, Moves(98, 1, 2));
        AssertEqual("accomplished", plan.SwitchReason, "D4's own trigger ends the push");
        AssertTrue(plan.MainObjectiveId != 98, "on another objective now: " + plan.MainObjectiveId);
        AssertEqual(Claimant.Committed, ClaimOf(plan, 2).Claimant, "the unready corps the new mass cannot use is held: " + Describe(plan));
        AssertEqual(Claimant.Mass, ClaimOf(plan, 1).Claimant, "the ready one is an ordinary member of the new mass");
        AssertTrue(ClaimOf(plan, 1).Reason != ClaimTable.ReasonCommitted, "not a committed one (the split may take it)");
        AssertEqual("2", Ids(plan.CommitmentEnded), "the ended commitment is the corps the new push did not take");
        AssertEqual(plan.MainObjectiveId, state.LastMassObjective, "the last mass is the new push's");
    }

    private static void TheEndReviewWaitsInCommittedWait()
    {
        var state = FirstDay(out _);
        var s = AccomplishedWorld(1);
        var plan = Replan(s, state, Moves(98, 1, 2));
        var a = ForceAllocator.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, null, null);
        a.TryGet(2, out var wait);
        AssertEqual(FormationRole.Hold, wait.Role, "a Hold");
        AssertEqual(ForceAllocator.ReasonCommittedWait, wait.Reason, "committed-wait");
        AssertEqual(-1, wait.SectorId, "counted in no sector");
        AssertFalse(wait.Withheld, "not withheld");
        AssertTrue(wait.TargetX == 240f && wait.TargetZ == 0f, "its target is where it stands");
        var sectors = ExecutorFixtures.Sectors(s);
        AssertEqual(9000f, ForceAllocator.CountedHold(s, sectors, a, sectors.SectorAt(240f, 0f)), "and it adds nothing to the hold of the sector it stands in (the capital guard's 9000 alone is counted there)");
    }

    // G3 counts it in no sector (its claim's sector is -1 and share 0, so SectorHeld never holds its men), G4's Counted rule skips the reason.
    private static void ACommittedWaitIsCountedInNoHoldInG3OrG4()
    {
        var state = FirstDay(out _);
        var s = AccomplishedWorld(1);
        var plan = Replan(s, state, Moves(98, 1, 2));
        var claim = ClaimOf(plan, 2);
        AssertTrue(claim.Claimant == Claimant.Committed && claim.SectorId == -1 && claim.Share == 0f, "G3: counted nowhere");
        AssertEqual(plan.Claims.Where(c => c.SectorId >= 0).Sum(c => c.Share), plan.SectorHeld.Sum(), "so no sector's held sum holds its men: the sums are the shares of the formations counted in a sector");
        var sectors = ExecutorFixtures.Sectors(s);
        var a = ForceAllocator.Allocate(plan, s, sectors, null, null, null);
        float counted = 0f;
        for (int k = 0; k < sectors.Count; k++) counted += ForceAllocator.CountedHold(s, sectors, a, k);
        AssertEqual(9000f, counted, "G4: only the capital guard's men are counted in any sector");
    }

    // The new push's mass may take a ready corps of the ended push as an ordinary member: row 8 takes a Committed claim for it, with no committed
    // reason, so the reserve split may take it.
    private static void AnEndedPushsReadyCorpsMayJoinTheNewMassAsOrdinaryMembers()
    {
        var state = FirstDay(out _);
        var plan = Replan(AccomplishedWorld(3), state, Moves(98, 1, 2));
        AssertTrue(plan.MainObjectiveId != 98, "the push moved on");
        foreach (int id in new[] { 1, 2 })
            if (plan.MassInstanceIds.Contains(id))
                AssertEqual(Claimant.Mass, ClaimOf(plan, id).Claimant, "corps " + id + " is in the new mass as a Mass claim");
        AssertTrue(plan.MassInstanceIds.Contains(1) || plan.MassInstanceIds.Contains(2), "the new push is made of the corps the old one had: " + Describe(plan));
        foreach (var c in plan.Claims) AssertTrue(c.Reason != ClaimTable.ReasonCommitted, "none of them keeps the committed reason: the commitment ended");
    }

    private static void AnEndedDecisionsFormationIsUnclaimed()
    {
        var state = FirstDay(out _);
        Replan(AccomplishedWorld(1), state, Moves(98, 1, 2));
        var next = Replan(AccomplishedWorld(3), state, Moves(98));
        AssertFalse(ClaimOf(next, 2).Claimant == Claimant.Committed, "from the next review the corps is placed by the holds, the capital reserve or the new push: " + Describe(next));
        AssertEqual(0, next.CommitmentEnded.Count, "nothing ends again");
    }

    // ---- the flag ----

    private static void FlagOffStillUpdatesTheLastMass()
    {
        WithCommitment(false, () =>
        {
            var state = new PlannerState();
            Replan(PushWorld(), state, null);
            AssertEqual(98, state.LastMassObjective, "the last mass is recorded with the flag off");
            AssertEqual(2, state.LastMassKeys.Count, "with its members");
            var plan = Replan(AccomplishedWorld(1), state, Moves(98, 1, 2));
            AssertEqual("1,2", Ids(plan.CommitmentEnded.Concat(plan.MassInstanceIds)), "and CommitmentEnded is still published as bookkeeping: " + Describe(plan));
            AssertEqual(0, plan.Claims.Count(c => c.Claimant == Claimant.Committed), "but nothing is committed");
            return 0;
        });
    }

    private static void FlagOffRestoresD5()
    {
        WithCommitment(false, () =>
        {
            var state = new PlannerState();
            Replan(PushWorld(), state, null);
            var plan = Replan(PushWorld(readiness2: 1), state, Moves(98, 1, 2));
            AssertEqual("1", Ids(plan.MassInstanceIds), "D5's readiness rule for every member: " + Describe(plan));
            AssertEqual(0, plan.Claims.Count(c => c.Claimant == Claimant.Committed || c.Reason == ClaimTable.ReasonCommitted), "no committed claim");
            return 0;
        });
    }
}
