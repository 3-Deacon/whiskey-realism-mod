using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// The reserve-release producer moves a group only on a real release, after the release has stood for a hold, and never twice for one order
// (smoke 2026-10-17: 18 queued "move-new" orders for one brigade in 40 minutes). Pure halves: DirectChildAllocator's ReleasedFromReserve,
// TacticalReserveCommitGate.TryReleasedDirectChild and ReserveReleasePacing; the producer's wiring is pinned in source.
internal static class ReserveReleasePacingTests
{
    public static void Run()
    {
        RunAll(new (string name, Action run)[]
        {
            ("reserve release: a role the evidence tiers gave moves nothing", AnEvidenceRoleIsNoRelease),
            ("reserve release: the allocator marks only a base reserve it released", TheAllocatorMarksOnlyItsOwnReleases),
            ("reserve release: a Defender never releases a held reserve into Main or SupportMain", ADefenderNeverReleasesIntoAnAttackRole),
            ("reserve release hold: a release stands HoldMinutes, whatever the release role, and the move is for the role standing then", TheHoldIsOnTheReleaseNotOnOneRole),
            ("reserve release hold: a release that swaps between release roles still goes within the hold (19:27 smoke)", AReleaseThatSwapsRolesStillGoes),
            ("reserve release hold: a crisis release is not held", ACrisisReleaseIsNotHeld),
            ("reserve release dead-band: a courier still waiting holds the next move", ACourierStillWaitingHoldsTheNextMove),
            ("reserve release dead-band: a target inside the dedupe radius of the last is no new release", ATargetInsideTheDedupeRadiusIsNoNewRelease),
            ("reserve release dead-band: a move the executor never took is retried", AMoveTheExecutorNeverTookIsRetried),
            ("reserve release dead-band: a release that lost the arbitration is not delivered and is retried", ALostArbitrationIsRetried),
            ("reserve release dead-band: a second release to the same line is judged afresh, the courier wait outlives the first", ASecondReleaseIsJudgedAfresh),
            ("reserve release replay: the smoke's role churn sends no order, a swapping release goes once per courier cycle", TheSmokeRoleChurnSendsNoOrder),
            ("reserve commit #59: only a real release lets vanilla commit a ledger-held group's reserve", OnlyARealReleaseAllowsVanillasCommit),
            ("reserve commit #59: a Defender's Main or SupportMain release allows no commit; the patch reads the stance", ADefenderAttackRoleAllowsNoCommit),
            ("reserve release marks: re-derived from this tick's allocation, never carried by a kept intent (replan-thrash seam)", TheMarkIsReDerivedEachTick),
            ("reserve release rows: the throttle is decided before anything is built", TheRowsAreThrottledBeforeAnythingIsBuilt),
            ("reserve release: the allocator reads the base reserves from the one pooled array, allocating none", TheAllocatorSharesTheOnePooledArray),
            ("reserve release producer: pinned to the real release, the hold and the dead-band", TheProducerIsWiredToTheRules),
        });
    }

    private const int Group = 705032;

    private static DirectChildIntent Intent(DirectChildRole role, bool released, int axisSector = 2, int groupId = Group)
        => new DirectChildIntent("child-" + groupId, 15, 15, "2nd_Brigade", 3, role, DirectChildAxis.SectorAxis, axisSector, 0.5f,
            new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, Array.Empty<EvidenceTag>()), releasedFromReserve: released);

    private static bool Released(DirectChildRole role, bool released, bool defender)
        => TacticalReserveCommitGate.TryReleasedDirectChild(new[] { Intent(role, released) }, Group, defender, out _, out _, out _);

    private static void AnEvidenceRoleIsNoRelease()
    {
        // The smoke: the ledger held 2nd Brigade in ReserveWait while the allocator's evidence tiers gave it SupportMain, Main and the refuse roles.
        foreach (DirectChildRole role in new[] { DirectChildRole.SupportMain, DirectChildRole.Main, DirectChildRole.RefuseRight, DirectChildRole.RefuseLeft, DirectChildRole.Screen })
        {
            AssertFalse(Released(role, released: false, defender: false), role + " from the evidence is no release (attacker)");
            AssertFalse(Released(role, released: false, defender: true), role + " from the evidence is no release (defender)");
        }
        AssertTrue(Released(DirectChildRole.SupportMain, released: true, defender: false), "control: the same role from a release moves the group");
        AssertFalse(Released(DirectChildRole.Reserve, released: true, defender: false), "a child still in Reserve is not released");
    }

    private static TacticalBattlePlan Plan() =>
        new TacticalBattlePlan(BattlePlanId.GenericAggressive, BattlePhase.MainEffort, mainEffortSector: 2,
            fixingSectors: new int[0], screeningSectors: new int[0], reserveCommitTriggerOdds: 50f, ageSeconds: 0f, jitterSeed: 0);

    private static DirectChildSnapshot[] Children(int n)
    {
        var snaps = new DirectChildSnapshot[n];
        for (int i = 0; i < n; i++) snaps[i] = new DirectChildSnapshot("c" + i, "a", 15, 0, "Corps" + i, true);
        return snaps;
    }

    private static void TheAllocatorMarksOnlyItsOwnReleases()
    {
        var personality = new PersonalityVector(0.2f, 0f, 0f, 0f, 0f);
        // c0 the threatened left wing, c1 a deployed reserve (the base allocation holds it), c2 the main effort,
        // c3 an adjacent brigade the evidence tiers make SupportMain, c4 the right wing.
        var evidence = new[]
        {
            new DirectChildEvidence(3, 1, true, 0, 3, 0.8f),
            new DirectChildEvidence(3, 0, false, 1, 0, 0.7f, deployedReserve: true),
            new DirectChildEvidence(3, 1, true, 2, 0, 0.8f),
            new DirectChildEvidence(3, 1, false, 3, 0, 0.7f),
            new DirectChildEvidence(3, 1, false, 4, 0, 0.8f),
        };
        var intents = DirectChildAllocator.AllocateWithZones(Plan(), personality, Children(5), evidence, null, null, new ReserveReleaseContext(0.5f));
        AssertEqual(DirectChildRole.RefuseLeft, intents[1].Role, "the flank release took the reserve out");
        AssertTrue(intents[1].ReleasedFromReserve, "a base reserve the release took out is a release");
        AssertEqual(DirectChildRole.SupportMain, intents[3].Role, "the evidence tiers gave the adjacent brigade SupportMain");
        AssertFalse(intents[3].ReleasedFromReserve, "a role from the evidence is no release");
        AssertFalse(intents[2].ReleasedFromReserve, "Main from the evidence is no release");

        var held = DirectChildAllocator.AllocateWithZones(Plan(), personality, Children(5),
            new[]
            {
                new DirectChildEvidence(3, 1, false, 0, 0, 0.8f),
                new DirectChildEvidence(3, 0, false, 1, 0, 0.7f, deployedReserve: true),
                new DirectChildEvidence(3, 1, true, 2, 0, 0.8f),
                new DirectChildEvidence(3, 1, false, 3, 0, 0.7f),
                new DirectChildEvidence(3, 1, false, 4, 0, 0.8f),
            }, null, null, new ReserveReleaseContext(0.5f));
        AssertEqual(DirectChildRole.Reserve, held[1].Role, "no release: the deployed reserve stays");
        AssertFalse(held[1].ReleasedFromReserve, "a reserve that stays is not released");

        // A fall-through Reserve (weak, distant) is base Reserve too: released only if a release takes it.
        AssertFalse(Intent(DirectChildRole.Reserve, released: false).ReleasedFromReserve, "control: the default is not a release");
    }

    private static void ADefenderNeverReleasesIntoAnAttackRole()
    {
        AssertFalse(Released(DirectChildRole.Main, released: true, defender: true), "Main");
        AssertFalse(Released(DirectChildRole.SupportMain, released: true, defender: true), "SupportMain");
        AssertTrue(Released(DirectChildRole.Fix, released: true, defender: true), "a crisis band release");
        AssertTrue(Released(DirectChildRole.RefuseLeft, released: true, defender: true), "a flank guard");
        AssertTrue(Released(DirectChildRole.RefuseRight, released: true, defender: true), "a crossing guard");
        AssertTrue(Released(DirectChildRole.Main, released: true, defender: false), "control: an attacker's Main release stands");
        TacticalReserveCommitGate.TryReleasedDirectChild(new[] { Intent(DirectChildRole.RefuseLeft, true, axisSector: 4) }, Group, true, out int sector, out DirectChildRole role, out _);
        var guarded = new DirectChildIntent("child-" + Group, 15, 15, "2nd_Brigade", 3, DirectChildRole.RefuseLeft, DirectChildAxis.SectorAxis, 4, 0.5f,
            new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, Array.Empty<EvidenceTag>()), guardPoint: new GuardPoint(250f, 150f, 45f), releasedFromReserve: true);
        AssertTrue(TacticalReserveCommitGate.TryReleasedDirectChild(new[] { guarded }, Group, true, out _, out _, out bool guard), "a crossing guard is a release");
        AssertTrue(guard, "and says it carries a guard point");
        AssertFalse(TacticalReserveCommitGate.TryReleasedDirectChild(new[] { Intent(DirectChildRole.RefuseLeft, true) }, Group, true, out _, out _, out bool none) && none,
            "a flank guard carries none");
        AssertEqual(4, sector, "the band");
        AssertEqual(DirectChildRole.RefuseLeft, role, "the role");
    }

    private static readonly double Minute = 1.0 / 60.0;

    // The executor's last Move for the group, as the producer reads it back.
    private static ExecutorMove Mine(double hours, float x, float z) => new ExecutorMove(true, hours, x, z);


    private static void TheHoldIsOnTheReleaseNotOnOneRole()
    {
        var pacing = new ReserveReleasePacing();
        double t = 10.0;
        AssertFalse(pacing.HoldElapsed(Group, true, DirectChildRole.RefuseRight, false, t), "the first observation starts the hold");
        AssertFalse(pacing.HoldElapsed(Group, true, DirectChildRole.RefuseRight, false, t + 2 * Minute), "inside the hold");
        AssertTrue(pacing.HoldElapsed(Group, true, DirectChildRole.RefuseRight, false, t + ReserveReleasePacing.HoldHours), "the hold stood");
        AssertTrue(Math.Abs(ReserveReleasePacing.HoldHours - pacing.HeldFor(Group, t + ReserveReleasePacing.HoldHours)) < 1e-9, "held for");

        // A swap between release roles is the same release: the hold keeps counting from its start.
        AssertTrue(pacing.HoldElapsed(Group, true, DirectChildRole.RefuseLeft, false, t + 4 * Minute), "another release role: the hold has stood");

        // The allocation takes the release back inside the hold: nothing goes out, and the next release starts afresh.
        pacing.HoldElapsed(Group, false, DirectChildRole.Unknown, false, t + 10 * Minute);
        double u = 20.0;
        AssertFalse(pacing.HoldElapsed(Group, true, DirectChildRole.SupportMain, false, u), "start");
        AssertFalse(pacing.HoldElapsed(Group, false, DirectChildRole.Unknown, false, u + 2 * Minute), "taken back");
        AssertFalse(pacing.HoldElapsed(Group, true, DirectChildRole.SupportMain, false, u + 2.5 * Minute), "released again: a new hold");
        AssertFalse(pacing.HoldElapsed(Group, true, DirectChildRole.SupportMain, false, u + 2.5 * Minute + 2.9 * Minute), "the old start does not count");
        AssertTrue(pacing.HoldElapsed(Group, true, DirectChildRole.SupportMain, false, u + 2.5 * Minute + ReserveReleasePacing.HoldHours), "the new start counts");
        AssertTrue(pacing.HeldFor(999, u) == 0.0, "an unknown group has held nothing");
    }

    // 19:27 in the smoke: 2nd Brigade's real release alternated RefuseLeft and RefuseRight (and SupportMain) every few seconds to a minute. Held on one
    // role, it never went out; held as a release, it goes once the hold is over, for the role standing at that tick.
    private static void AReleaseThatSwapsRolesStillGoes()
    {
        var pacing = new ReserveReleasePacing();
        DirectChildRole[] roles = { DirectChildRole.RefuseLeft, DirectChildRole.RefuseRight, DirectChildRole.SupportMain };
        int goes = 0;
        DirectChildRole standing = DirectChildRole.Unknown;
        double firstGo = double.NaN;
        for (int s = 0; s < 600; s += 5)
        {
            DirectChildRole role = roles[(s / 20) % roles.Length];
            double now = 10.0 + s / 3600.0;
            if (!pacing.HoldElapsed(Group, true, role, false, now)) continue;
            goes++;
            if (double.IsNaN(firstGo)) { firstGo = now; standing = role; }
        }
        AssertTrue(goes > 0, "a release that swaps roles every 20 s goes within ten minutes");
        AssertTrue(Math.Abs(firstGo - (10.0 + ReserveReleasePacing.HoldHours)) < 6.0 / 3600.0, "at the end of the hold (" + (firstGo - 10.0) * 60.0 + " min)");
        AssertTrue(standing != DirectChildRole.Unknown, "for the role standing when the hold completed: " + standing);
    }

    private static void ACrisisReleaseIsNotHeld()
    {
        var pacing = new ReserveReleasePacing();
        AssertTrue(pacing.HoldElapsed(Group, true, DirectChildRole.Fix, false, 10.0), "a crisis band release goes at once: the line it answers is in contact");
        AssertFalse(pacing.HoldElapsed(Group, true, DirectChildRole.SupportMain, false, 10.0 + Minute), "control: another role is held");
        // A crossing guard is timed to the enemy's arrival and holds its own binding: not held either.
        AssertTrue(pacing.HoldElapsed(Group + 1, true, DirectChildRole.RefuseRight, guardPoint: true, 10.0), "a crossing guard goes at once");
        AssertFalse(pacing.HoldElapsed(Group + 2, true, DirectChildRole.RefuseRight, guardPoint: false, 10.0), "control: a flank guard without a guard point is held");
    }

    private static void ACourierStillWaitingHoldsTheNextMove()
    {
        var pacing = new ReserveReleasePacing();
        AssertEqual(ReserveReleasePacing.Admission.Go, pacing.Admit(Group, 1131f, -1399.5f, 10.0, 0, ExecutorMove.None), "nothing sent yet");
        pacing.NoteSubmitted(Group, 1131f, -1399.5f, 10.0);
        // The executor took it in the tick it was submitted; its courier is queued.
        double later = 10.0 + 0.015;
        AssertEqual(ReserveReleasePacing.Admission.CourierWait, pacing.Admit(Group, 900f, -1100f, later, 1, Mine(10.0, 1131f, -1399.5f)), "another target while the courier is queued");
        AssertEqual(ReserveReleasePacing.Admission.Go, pacing.Admit(Group, 900f, -1100f, later + 0.02, 0, Mine(10.0, 1131f, -1399.5f)), "the courier delivered: a far target goes");
        pacing.NoteSubmitted(Group, 900f, -1100f, later + 0.02);
        AssertEqual(ReserveReleasePacing.Admission.CourierWait, pacing.Admit(Group, 700f, -900f, later + 0.04, 2, Mine(later + 0.02, 900f, -1100f)), "queued again");
        AssertEqual(ReserveReleasePacing.Admission.Go,
            pacing.Admit(Group, 700f, -900f, later + 0.02 + ReserveReleasePacing.CourierWaitCapHours + 0.01, 2, Mine(later + 0.02, 900f, -1100f)),
            "a queue entry that outlasts the cap holds nothing back");
    }

    private static void ATargetInsideTheDedupeRadiusIsNoNewRelease()
    {
        var pacing = new ReserveReleasePacing();
        pacing.NoteSubmitted(Group, 1000f, -1000f, 10.0);
        double now = 10.5;
        float edge = OrderArbiter.DedupeMeters;
        ExecutorMove took = Mine(10.0, 1000f, -1000f);
        AssertEqual(ReserveReleasePacing.Admission.DeadBand, pacing.Admit(Group, 1000f + edge, -1000f, now, 0, took), "on the radius");
        AssertEqual(ReserveReleasePacing.Admission.DeadBand, pacing.Admit(Group, 1000f, -1000f, now, 0, took), "the same target");
        AssertEqual(ReserveReleasePacing.Admission.Go, pacing.Admit(Group, 1000f + edge + 1f, -1000f, now, 0, took), "beyond the radius");
        AssertEqual(ReserveReleasePacing.Admission.Go, pacing.Admit(Group + 1, 1000f, -1000f, now, 0, ExecutorMove.None), "another group has its own record");
    }

    private static void AMoveTheExecutorNeverTookIsRetried()
    {
        var pacing = new ReserveReleasePacing();
        pacing.NoteSubmitted(Group, 1000f, -1000f, 10.0);
        // The next tick: the executor has issued nothing for the group since (a cooldown, a refused crossing), so the same target is not a repeat.
        AssertEqual(ReserveReleasePacing.Admission.Go, pacing.Admit(Group, 1000f, -1000f, 10.015, 0, Mine(9.0, 1000f, -1000f)), "never issued: retried");
        AssertEqual(ReserveReleasePacing.Admission.Go, pacing.Admit(Group, 1000f, -1000f, 10.03, 0, Mine(9.0, 1000f, -1000f)), "and stays retried");
        // The record was dropped at the first tick that saw nothing: an executor move at that target a tick later (a retry of its own) is not read back into it.
        AssertEqual(ReserveReleasePacing.Admission.Go, pacing.Admit(Group, 1000f, -1000f, 10.045, 0, Mine(10.04, 1000f, -1000f)), "a record dropped for want of a move is not revived by a later one");
        // Control: the executor's move was issued at the submission's hour, to the target.
        pacing.NoteSubmitted(Group, 1000f, -1000f, 11.0);
        AssertEqual(ReserveReleasePacing.Admission.DeadBand, pacing.Admit(Group, 1000f, -1000f, 11.015, 0, Mine(11.0, 1000f, -1000f)), "issued: a repeat");
        pacing.Clear();
        AssertEqual(ReserveReleasePacing.Admission.Go, pacing.Admit(Group, 1000f, -1000f, 11.03, 0, Mine(11.0, 1000f, -1000f)), "a cleared record forgets");
    }

    // Verify round 1: a move counted as delivered on its hour alone. The release intent lost the arbitration to another producer's Move for the group
    // (or a parent's fanned move) at the same tick: the executor's last move is that tick's, but not this producer's and not at the recorded target.
    private static void ALostArbitrationIsRetried()
    {
        var pacing = new ReserveReleasePacing();
        double t = 10.0;
        pacing.NoteSubmitted(Group, 1000f, -1400f, t);
        var posture = new ExecutorMove(false, t, 400f, -2000f);
        AssertEqual(ReserveReleasePacing.Admission.Go, pacing.Admit(Group, 1000f, -1400f, t + 0.015, 0, posture), "another producer's move at the same hour confirms nothing");
        AssertEqual(ReserveReleasePacing.Admission.Go, pacing.Admit(Group, 1000f, -1400f, t + 1.0, 0, posture), "and the target is not dead-banded an hour later");

        pacing.NoteSubmitted(Group, 1000f, -1400f, t);
        var sameSpotOtherProducer = new ExecutorMove(false, t, 1000f, -1400f);
        AssertEqual(ReserveReleasePacing.Admission.Go, pacing.Admit(Group, 1000f, -1400f, t + 0.015, 0, sameSpotOtherProducer),
            "another producer's move to the same spot is not this producer's order");
        pacing.NoteSubmitted(Group, 1000f, -1400f, t);
        var elsewhere = new ExecutorMove(true, t, 400f, -2000f);
        AssertEqual(ReserveReleasePacing.Admission.Go, pacing.Admit(Group, 1000f, -1400f, t + 0.015, 0, elsewhere),
            "a release-sourced move to another target (an earlier release's) confirms nothing");
        pacing.NoteSubmitted(Group, 1000f, -1400f, t);
        var nearTarget = new ExecutorMove(true, t, 1000f + OrderArbiter.DedupeMeters, -1400f);
        AssertEqual(ReserveReleasePacing.Admission.DeadBand, pacing.Admit(Group, 1000f, -1400f, t + 0.015, 0, nearTarget), "control: this producer's move within the dedupe radius confirms");
    }

    // Verify round 1: the dead-band outlived its release, so a second real release to within 60 m of the first was held for ever.
    private static void ASecondReleaseIsJudgedAfresh()
    {
        var pacing = new ReserveReleasePacing();
        double t = 10.0;
        pacing.HoldElapsed(Group, true, DirectChildRole.RefuseRight, false, t);
        AssertTrue(pacing.HoldElapsed(Group, true, DirectChildRole.RefuseRight, false, t + 3 * Minute), "the first release stood");
        double sent = t + 3 * Minute;
        AssertEqual(ReserveReleasePacing.Admission.Go, pacing.Admit(Group, 1000f, -1400f, sent, 0, ExecutorMove.None), "first move goes");
        pacing.NoteSubmitted(Group, 1000f, -1400f, sent);
        AssertEqual(ReserveReleasePacing.Admission.DeadBand, pacing.Admit(Group, 1000f, -1400f, t + 4 * Minute, 0, Mine(sent, 1000f, -1400f)), "while it stands, a repeat is dead-banded");
        for (int m = 5; m < 35; m++) pacing.HoldElapsed(Group, false, DirectChildRole.Unknown, false, t + m * Minute);
        double u = t + 35 * Minute;
        pacing.HoldElapsed(Group, true, DirectChildRole.RefuseRight, false, u);
        AssertTrue(pacing.HoldElapsed(Group, true, DirectChildRole.RefuseRight, false, u + 3 * Minute), "the second release stood");
        AssertEqual(ReserveReleasePacing.Admission.Go, pacing.Admit(Group, 1020f, -1410f, u + 3 * Minute, 0, Mine(sent, 1000f, -1400f)),
            "a second release to the same line, 30 minutes on, goes");
        AssertEqual(ReserveReleasePacing.Admission.Go, pacing.Admit(Group, 1020f, -1410f, u + 60 * Minute, 0, Mine(sent, 1000f, -1400f)), "and is not held an hour later");

        // The courier wait outlives the release: a release that ends and starts again while the first order is still queued waits for it.
        var quick = new ReserveReleasePacing();
        quick.HoldElapsed(Group, true, DirectChildRole.RefuseLeft, false, t);
        quick.HoldElapsed(Group, true, DirectChildRole.RefuseLeft, false, t + 3 * Minute);
        quick.NoteSubmitted(Group, 700f, -900f, sent);
        quick.Admit(Group, 700f, -900f, sent + 0.001, 1, Mine(sent, 700f, -900f));
        quick.HoldElapsed(Group, false, DirectChildRole.Unknown, false, sent + 0.002);
        quick.HoldElapsed(Group, true, DirectChildRole.Fix, false, sent + 0.01);
        AssertEqual(ReserveReleasePacing.Admission.CourierWait, quick.Admit(Group, 300f, -300f, sent + 0.01, 1, Mine(sent, 700f, -900f)),
            "a new release does not queue behind an order still in transit");
    }

    // The smoke, as the producer sees it: for forty doctrine minutes the ledger holds the brigade while the allocator flips its role every
    // 20 to 90 seconds. The producer's loop is replayed here from the same rules; the count of orders it submits is the finding. The courier
    // keeps an order queued for CourierHours after it goes out; driftMeters is how far the siblings' centroid, and so the target, moves between orders.
    private const double CourierHours = 0.05;

    private static int Replay(Func<int, DirectChildRole> roleAt, Func<int, bool> releasedAt, bool defender, int seconds, int stepSeconds, float driftMeters)
    {
        var pacing = new ReserveReleasePacing();
        int submitted = 0;
        ExecutorMove lastIssued = ExecutorMove.None;
        double lastIssuedHours = double.NaN;
        for (int s = 0; s < seconds; s += stepSeconds)
        {
            double now = 10.0 + s / 3600.0;
            var intents = new[] { Intent(roleAt(s), releasedAt(s)) };
            bool released = TacticalReserveCommitGate.TryReleasedDirectChild(intents, Group, defender, out _, out DirectChildRole role, out bool guard);
            if (!pacing.HoldElapsed(Group, released, role, guard, now)) continue;
            float x = 1000f + driftMeters * submitted;
            int queued = !double.IsNaN(lastIssuedHours) && now - lastIssuedHours < CourierHours ? 1 : 0;
            if (pacing.Admit(Group, x, -1400f, now, queued, lastIssued) != ReserveReleasePacing.Admission.Go) continue;
            pacing.NoteSubmitted(Group, x, -1400f, now);
            lastIssued = Mine(now, x, -1400f);
            lastIssuedHours = now;
            submitted++;
        }
        return submitted;
    }

    private static void TheSmokeRoleChurnSendsNoOrder()
    {
        var churn = new[] { DirectChildRole.SupportMain, DirectChildRole.Main, DirectChildRole.RefuseRight, DirectChildRole.RefuseLeft, DirectChildRole.SupportMain };
        // Every change an evidence role: no release at all.
        AssertEqual(0, Replay(s => churn[(s / 40) % churn.Length], s => false, defender: true, seconds: 2400, stepSeconds: 5, driftMeters: 90f), "evidence roles: nothing sent");
        // A release the allocation takes back every 40 s never stands the 3-minute hold: nothing sent.
        AssertEqual(0, Replay(s => churn[(s / 40) % churn.Length], s => (s / 40) % 2 == 0, defender: false, seconds: 2400, stepSeconds: 5, driftMeters: 90f),
            "a release taken back every 80 s: nothing sent");
        // A release that swaps between release roles is one release: it goes, paced by the courier (verify round 1).
        int swapping = Replay(s => churn[(s / 40) % churn.Length], s => true, defender: false, seconds: 2400, stepSeconds: 5, driftMeters: 90f);
        AssertTrue(swapping >= 1 && swapping <= 14, "a release that swaps release roles is carried out, not per tick: " + swapping);
        // A release that stands, its target steady: one order, then the dead-band.
        AssertEqual(1, Replay(s => DirectChildRole.RefuseRight, s => true, defender: true, seconds: 2400, stepSeconds: 5, driftMeters: 0f), "a steady release is carried out once");
        // A release whose target drifts 90 m between orders: never an order while the last is queued (at most one per hold-plus-courier cycle).
        int drifting = Replay(s => DirectChildRole.RefuseRight, s => true, defender: true, seconds: 2400, stepSeconds: 5, driftMeters: 90f);
        AssertTrue(drifting >= 2 && drifting <= 14, "a drifting target is paced by the courier, not sent per tick: " + drifting);
    }

    // #59 (BattleReserveCommitGatePatch): the allow path reads the command-node intent. The mark rides from the direct child through the command tree to
    // every descendant and through the resolver's direct-child fallback.
    private static void OnlyARealReleaseAllowsVanillasCommit()
    {
        var tree = CommandTreeBuilder.Build(new[]
        {
            new CommandTreeBuilder.CommandProbe(100, 0, 1, 17, "Army", true, false, false),
            new CommandTreeBuilder.CommandProbe(200, 100, 1, 15, "Corps", true, false, false),
            new CommandTreeBuilder.CommandProbe(300, 200, 1, 14, "Division", true, false, false),
        }, 1, 0);
        var held = TacticalReserveCommitGate.HeldResolution(
            new[] { new CommandNodeOperationalState("node-300", CommandEchelonKind.DivisionLike, CommandNodeRole.Reserve, CommandTaskType.ReserveWait, CommandTaskState.Committed) },
            300, 0, new CommandIntentResolution(false, default, "unused"));
        AssertEqual(DirectChildRole.Reserve, held.Intent.Role, "the ledger holds the division in reserve");

        foreach (bool released in new[] { true, false })
        {
            var nodes = CommandTreeIntentAllocator.Allocate(tree, new[] { Intent(DirectChildRole.SupportMain, released, groupId: 200) });
            CommandIntentResolution viaTree = CommandIntentResolver.ResolveForInstance(300, nodes);
            AssertTrue(viaTree.Found, "the division resolves through the tree");
            AssertEqual(released, viaTree.Intent.ReleasedFromReserve, "the division carries its corps' mark (" + released + ")");
            AssertEqual(released, TacticalReserveCommitGate.ReleasedByAllocator(held, viaTree, defender: false),
                "vanilla's commit is allowed " + (released ? "for a real release" : "never for a role from the evidence"));

            CommandIntentResolution viaFallback = CommandIntentResolver.ResolveForInstance(200, Array.Empty<CommandNodeIntent>(),
                new[] { Intent(DirectChildRole.SupportMain, released, groupId: 200) });
            AssertEqual(released, viaFallback.Intent.ReleasedFromReserve, "the direct-child fallback carries the mark (" + released + ")");
        }
        var decision = TacticalReserveCommitGate.Decide(new TacticalReserveCommitGate.Input(playerControlled: false,
            reserveReleased: TacticalReserveCommitGate.ReleasedByAllocator(held,
                CommandIntentResolver.ResolveForInstance(300, CommandTreeIntentAllocator.Allocate(tree, new[] { Intent(DirectChildRole.SupportMain, false, groupId: 200) })),
                defender: false)));
        AssertEqual(TacticalReserveCommitGate.Action.Deny, decision.Action, "the gate denies the evidence-tier role");
    }

    private static CommandIntentResolution Node(DirectChildRole role, bool released)
        => new CommandIntentResolution(true, new CommandNodeIntent("node-300", "node-300", role, DirectChildAxis.Hold, 2, 50, 1, released), "test");

    private static void ADefenderAttackRoleAllowsNoCommit()
    {
        var held = new CommandIntentResolution(true, new CommandNodeIntent("node-300", "node-300", DirectChildRole.Reserve, DirectChildAxis.Hold, 0, 0, 0), "ledger");
        foreach (DirectChildRole role in new[] { DirectChildRole.Main, DirectChildRole.SupportMain })
        {
            AssertTrue(TacticalReserveCommitGate.ReleasedByAllocator(held, Node(role, true), defender: false), role + ": a real release on an attacking side commits");
            AssertFalse(TacticalReserveCommitGate.ReleasedByAllocator(held, Node(role, true), defender: true), role + ": never on a Defender side");
        }
        foreach (DirectChildRole role in new[] { DirectChildRole.Fix, DirectChildRole.RefuseLeft, DirectChildRole.RefuseRight })
            AssertTrue(TacticalReserveCommitGate.ReleasedByAllocator(held, Node(role, true), defender: true), role + ": a Defender's crisis or guard release commits");
        // The patch reads the stance from the side's orchestrator, read-only state (CurrentBattleStance), and hands it to the rule.
        string patch = FlankResponseTests.SourceText("Patches", "BattleReserveCommitGatePatch.cs");
        FlankResponseTests.AssertPinned(patch, "defender = side.Army.CurrentBattleStance == BattleStance.Defender;", "the stance comes from the army orchestrator");
        FlankResponseTests.AssertPinned(patch, "ReleasedByAllocator(resolution, allocator, defender)", "and reaches the rule");
    }

    // The replan-thrash branch's DirectChildRoleHold.Apply keeps an old intent (its old role and, with it, its old mark) when a role is held. The mark is
    // a fact about the allocation that just ran: the army restamps every kept intent from the fresh one.
    private static void TheMarkIsReDerivedEachTick()
    {
        DirectChildIntent heldOld = Intent(DirectChildRole.SupportMain, released: true);           // kept from an earlier tick, released then
        DirectChildIntent freshNotReleased = Intent(DirectChildRole.Reserve, released: false);     // this tick: the release was taken back
        var restamped = DirectChildAllocator.RestampReleases(new[] { heldOld }, new[] { freshNotReleased });
        AssertFalse(restamped[0].ReleasedFromReserve, "a release the allocation took back is not carried by the intent a role hold keeps");
        AssertEqual(DirectChildRole.SupportMain, restamped[0].Role, "the role the hold keeps is untouched");

        DirectChildIntent evidenceFresh = Intent(DirectChildRole.Main, released: false);
        AssertFalse(DirectChildAllocator.RestampReleases(new[] { heldOld }, new[] { evidenceFresh })[0].ReleasedFromReserve, "nor one the evidence tiers replaced");
        DirectChildIntent releasedFresh = Intent(DirectChildRole.RefuseLeft, released: true);
        AssertTrue(DirectChildAllocator.RestampReleases(new[] { Intent(DirectChildRole.Main, released: false) }, new[] { releasedFresh })[0].ReleasedFromReserve,
            "a child the fresh allocation released is marked even when a hold keeps another committed role for it");
        AssertFalse(DirectChildAllocator.RestampReleases(new[] { Intent(DirectChildRole.Reserve, released: true) }, new[] { releasedFresh })[0].ReleasedFromReserve,
            "a kept Reserve is released from nothing");
        // Matched by child id, not by position; a child missing from the fresh list is unmarked.
        var kept = new[] { Intent(DirectChildRole.Main, true, groupId: 11), Intent(DirectChildRole.Main, true, groupId: 12) };
        var fresh = new[] { Intent(DirectChildRole.Main, false, groupId: 12), Intent(DirectChildRole.Main, true, groupId: 11) };
        var byId = DirectChildAllocator.RestampReleases(kept, fresh);
        AssertTrue(byId[0].ReleasedFromReserve && !byId[1].ReleasedFromReserve, "matched by child id");
        AssertFalse(DirectChildAllocator.RestampReleases(new[] { heldOld }, new DirectChildIntent[0])[0].ReleasedFromReserve, "absent from the fresh allocation: unmarked");
        // Nothing to change: the same list comes back (no allocation in the steady state).
        var stable = new[] { Intent(DirectChildRole.Main, false) };
        AssertTrue(ReferenceEquals(stable, DirectChildAllocator.RestampReleases(stable, new[] { Intent(DirectChildRole.Main, false) })), "unchanged marks: the list itself");
        string army = FlankResponseTests.SourceText("Tactical", "Orchestrator", "ArmyOrchestrator.cs");
        // The restamp is the LAST step before the command tree reads the intents: nothing sits between it and the Allocate call, so a pass merged in
        // from another branch (a role hold) cannot land after it without conflicting with these lines or failing this pin.
        FlankResponseTests.AssertPinned(army,
            "_directChildIntents = DirectChildAllocator.RestampReleases(_directChildIntents, allocated); _commandNodeIntents = CommandTreeIntentAllocator.Allocate(_commandTree, _directChildIntents);",
            "the restamp is immediately before the command tree's Allocate");
        string observe = FlankResponseTests.Between(army, "IReadOnlyList<DirectChildIntent> allocated = DirectChildAllocator.AllocateWithZones(", "EmitFlankReserveReleaseTelemetry(previousIntents");
        int stabilize = observe.IndexOf("StabilizeMain(", StringComparison.Ordinal), screen = observe.IndexOf("ScreenSecondaryObjectives();", StringComparison.Ordinal);
        int restamp = observe.IndexOf("RestampReleases(", StringComparison.Ordinal), tree = observe.IndexOf("CommandTreeIntentAllocator.Allocate(", StringComparison.Ordinal);
        AssertTrue(stabilize >= 0 && screen > stabilize && restamp > screen && tree > restamp, "the order is the main hysteresis, the screening, the restamp, the command tree");
        AssertEqual(1, System.Text.RegularExpressions.Regex.Matches(observe, "RestampReleases\\(").Count, "and one restamp");
    }

    private static void TheRowsAreThrottledBeforeAnythingIsBuilt()
    {
        var rows = new RowThrottle(30f);
        long key = RowThrottle.Key(1, 705032, 2, 7, 12);
        AssertTrue(rows.Due(key, 100f), "the first row is due");
        AssertFalse(rows.Due(key, 110f), "inside the throttle");
        AssertTrue(rows.Due(RowThrottle.Key(1, 705032, 2, 7, 13), 110f), "another identity (a new target cell) is due at once");
        AssertTrue(rows.Due(key, 131f), "due again after the throttle");
        long begin = TestFixtures.BeginAllocationWindow();
        for (int i = 0; i < 1000; i++) rows.Due(key, 131.5f);
        AssertEqual(0L, TestFixtures.AllocatedSince(begin), "a row that is not due allocates nothing");
        // Bounded: past the cap the table clears rather than growing.
        for (int i = 0; i < RowThrottle.MaxKeys * 3; i++) rows.Due(RowThrottle.Key(i), 500f);
        AssertTrue(rows.Count <= RowThrottle.MaxKeys, "the table stayed bounded: " + rows.Count);
        // The runtimes decide the throttle before they build a string or a closure: the closure lives in the method that is called once due.
        string gate = FlankResponseTests.SourceText("Tactical", "Battle", "RiverCrossingGateRuntime.cs");
        string due = FlankResponseTests.Between(gate, "private static void EmitRow(", "private static void WriteRow(");
        AssertFalse(due.Contains("=>"), "EmitRow creates no lambda (a closure would be allocated before the throttle)");
        AssertTrue(due.IndexOf("Rows.Due(", StringComparison.Ordinal) >= 0 && due.IndexOf("WriteRow(", StringComparison.Ordinal) > due.IndexOf("Rows.Due(", StringComparison.Ordinal), "the throttle comes first");
        string producer = FlankResponseTests.SourceText("Tactical", "Battle", "Producers", "ReserveReleaseIntentProducer.cs");
        string moveDue = FlankResponseTests.Between(producer, "private static void EmitMoveRow(", "private static void WriteMoveRow(");
        AssertFalse(moveDue.Contains("=>"), "EmitMoveRow creates no lambda");
        AssertFalse(moveDue.Contains("signature"), "and builds no signature string");
        AssertTrue(moveDue.IndexOf("_moveRows.Due(", StringComparison.Ordinal) >= 0, "the throttle decides first");
    }

    private static void TheAllocatorSharesTheOnePooledArray()
    {
        string allocator = FlankResponseTests.SourceText("Tactical", "Orchestrator", "DirectChildAllocator.cs");
        FlankResponseTests.AssertPinned(allocator, "bool[] heldByBase = ReserveFlags(roles);", "the base reserves come from the pooled array");
        FlankResponseTests.AssertPinned(allocator, "ReleaseReserves(plan, evidence, roles, release, snapshots, guards, heldByBase);", "and the release reads that same array");
        FlankResponseTests.AssertPinned(allocator, "(baseReserve ?? ReserveFlags(roles))", "a caller with none (the leaf release) gets the pooled one");
        AssertFalse(FlankResponseTests.Flat(allocator).Contains("heldByBase = new bool["), "no array of its own per allocation");

        // Behaviour across calls of different sizes: a stale tail of the pool never marks a child of a smaller allocation.
        var personality = new PersonalityVector(0.2f, 0f, 0f, 0f, 0f);
        DirectChildEvidence[] five =
        {
            new DirectChildEvidence(3, 1, true, 0, 3, 0.8f), new DirectChildEvidence(3, 0, false, 1, 0, 0.7f, deployedReserve: true),
            new DirectChildEvidence(3, 1, true, 2, 0, 0.8f), new DirectChildEvidence(3, 0, false, 3, 0, 0.7f, deployedReserve: true),
            new DirectChildEvidence(3, 1, true, 4, 3, 0.8f),
        };
        var big = DirectChildAllocator.AllocateWithZones(Plan(), personality, Children(5), five, null, null, new ReserveReleaseContext(0.5f));
        AssertTrue(big[1].ReleasedFromReserve && big[3].ReleasedFromReserve, "both reserves of the big allocation were released");
        DirectChildEvidence[] two = { new DirectChildEvidence(3, 1, false, 0, 0, 0.8f), new DirectChildEvidence(3, 1, true, 2, 0, 0.8f) };
        var small = DirectChildAllocator.AllocateWithZones(Plan(), personality, Children(2), two, null, null, new ReserveReleaseContext(0.5f));
        AssertFalse(small[0].ReleasedFromReserve || small[1].ReleasedFromReserve, "a smaller allocation after a bigger one marks nothing the pool still held");
    }

    private static void TheProducerIsWiredToTheRules()
    {
        string producer = FlankResponseTests.SourceText("Tactical", "Battle", "Producers", "ReserveReleaseIntentProducer.cs");
        FlankResponseTests.AssertPinned(producer, "army.CurrentBattleStance == BattleStance.Defender", "the Defender rule reads the army's stance");
        FlankResponseTests.AssertPinned(producer, "TryReleasedDirectChild(army.CurrentDirectChildIntents, id, defender, out int sector, out DirectChildRole role, out bool guardPoint)", "the real release, with the stance");
        FlankResponseTests.AssertPinned(producer, "pacing.HoldElapsed(id, released, role, guardPoint, snapshot.DoctrineHours)", "the hold, on the snapshot's doctrine clock");
        FlankResponseTests.AssertPinned(producer, "pacing.Admit(id,", "the dead-band");
        FlankResponseTests.AssertPinned(producer, "pacing.NoteSubmitted(id,", "the producer records what it sent");
        FlankResponseTests.AssertPinned(producer, "snapshot.Own[index].QueuedOrders, ExecutorMoveOf(executor, id));", "the executor's last move is read back with its target");
        FlankResponseTests.AssertPinned(producer, "outcome.Source == IntentSource.ReserveRelease", "and only this producer's move confirms");
        FlankResponseTests.AssertPinned(producer, "BattleScope.Register(\"reserve-release-producer\"", "the pacing is battle-scoped");
    }
}
