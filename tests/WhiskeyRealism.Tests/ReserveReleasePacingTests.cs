using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Battle;
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
            ("reserve release hold: the role must stand HoldMinutes in one role", TheHoldNeedsOneRoleForTheWholeHold),
            ("reserve release hold: a crisis release is not held", ACrisisReleaseIsNotHeld),
            ("reserve release dead-band: a courier still waiting holds the next move", ACourierStillWaitingHoldsTheNextMove),
            ("reserve release dead-band: a target inside the dedupe radius of the last is no new release", ATargetInsideTheDedupeRadiusIsNoNewRelease),
            ("reserve release dead-band: a move the executor never took is retried", AMoveTheExecutorNeverTookIsRetried),
            ("reserve release replay: the smoke's role churn sends no order", TheSmokeRoleChurnSendsNoOrder),
            ("reserve release producer: pinned to the real release, the hold and the dead-band", TheProducerIsWiredToTheRules),
        });
    }

    private const int Group = 705032;

    private static DirectChildIntent Intent(DirectChildRole role, bool released, int axisSector = 2, int groupId = Group)
        => new DirectChildIntent("child-" + groupId, 15, 15, "2nd_Brigade", 3, role, DirectChildAxis.SectorAxis, axisSector, 0.5f,
            new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, Array.Empty<EvidenceTag>()), releasedFromReserve: released);

    private static bool Released(DirectChildRole role, bool released, bool defender)
        => TacticalReserveCommitGate.TryReleasedDirectChild(new[] { Intent(role, released) }, Group, defender, out _, out _);

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
        TacticalReserveCommitGate.TryReleasedDirectChild(new[] { Intent(DirectChildRole.RefuseLeft, true, axisSector: 4) }, Group, true, out int sector, out DirectChildRole role);
        AssertEqual(4, sector, "the band");
        AssertEqual(DirectChildRole.RefuseLeft, role, "the role");
    }

    private static readonly double Minute = 1.0 / 60.0;

    private static void TheHoldNeedsOneRoleForTheWholeHold()
    {
        var pacing = new ReserveReleasePacing();
        double t = 10.0;
        AssertFalse(pacing.HoldElapsed(Group, true, DirectChildRole.RefuseRight, t), "the first observation starts the hold");
        AssertFalse(pacing.HoldElapsed(Group, true, DirectChildRole.RefuseRight, t + 2 * Minute), "inside the hold");
        AssertTrue(pacing.HoldElapsed(Group, true, DirectChildRole.RefuseRight, t + ReserveReleasePacing.HoldHours), "the hold stood");
        AssertTrue(Math.Abs(ReserveReleasePacing.HoldHours - pacing.HeldFor(Group, t + ReserveReleasePacing.HoldHours)) < 1e-9, "held for");

        // A swap of the role restarts the hold.
        AssertFalse(pacing.HoldElapsed(Group, true, DirectChildRole.RefuseLeft, t + 4 * Minute), "another role starts afresh");
        AssertFalse(pacing.HoldElapsed(Group, true, DirectChildRole.RefuseLeft, t + 6 * Minute), "inside the new hold");
        AssertTrue(pacing.HoldElapsed(Group, true, DirectChildRole.RefuseLeft, t + 4 * Minute + ReserveReleasePacing.HoldHours), "the new hold stood");

        // The allocation takes the release back inside the hold: nothing goes out, and the next release starts afresh.
        double u = 20.0;
        AssertFalse(pacing.HoldElapsed(Group, true, DirectChildRole.SupportMain, u), "start");
        AssertFalse(pacing.HoldElapsed(Group, false, DirectChildRole.Unknown, u + 2 * Minute), "taken back");
        AssertFalse(pacing.HoldElapsed(Group, true, DirectChildRole.SupportMain, u + 2.5 * Minute), "released again: a new hold");
        AssertFalse(pacing.HoldElapsed(Group, true, DirectChildRole.SupportMain, u + 2.5 * Minute + 2.9 * Minute), "the old start does not count");
        AssertTrue(pacing.HoldElapsed(Group, true, DirectChildRole.SupportMain, u + 2.5 * Minute + ReserveReleasePacing.HoldHours), "the new start counts");
        AssertTrue(pacing.HeldFor(999, u) == 0.0, "an unknown group has held nothing");
    }

    private static void ACrisisReleaseIsNotHeld()
    {
        var pacing = new ReserveReleasePacing();
        AssertTrue(pacing.HoldElapsed(Group, true, DirectChildRole.Fix, 10.0), "a crisis band release goes at once: the line it answers is in contact");
        AssertFalse(pacing.HoldElapsed(Group, true, DirectChildRole.SupportMain, 10.0 + Minute), "control: another role is held");
    }

    private static void ACourierStillWaitingHoldsTheNextMove()
    {
        var pacing = new ReserveReleasePacing();
        AssertEqual(ReserveReleasePacing.Admission.Go, pacing.Admit(Group, 1131f, -1399.5f, 10.0, 0, double.NaN), "nothing sent yet");
        pacing.NoteSubmitted(Group, 1131f, -1399.5f, 10.0);
        // The executor took it in the tick it was submitted; its courier is queued.
        double later = 10.0 + 0.015;
        AssertEqual(ReserveReleasePacing.Admission.CourierWait, pacing.Admit(Group, 900f, -1100f, later, 1, 10.0), "another target while the courier is queued");
        AssertEqual(ReserveReleasePacing.Admission.Go, pacing.Admit(Group, 900f, -1100f, later + 0.02, 0, 10.0), "the courier delivered: a far target goes");
        pacing.NoteSubmitted(Group, 900f, -1100f, later + 0.02);
        AssertEqual(ReserveReleasePacing.Admission.CourierWait, pacing.Admit(Group, 700f, -900f, later + 0.04, 2, later + 0.02), "queued again");
        AssertEqual(ReserveReleasePacing.Admission.Go, pacing.Admit(Group, 700f, -900f, later + 0.02 + ReserveReleasePacing.CourierWaitCapHours + 0.01, 2, later + 0.02),
            "a queue entry that outlasts the cap holds nothing back");
    }

    private static void ATargetInsideTheDedupeRadiusIsNoNewRelease()
    {
        var pacing = new ReserveReleasePacing();
        pacing.NoteSubmitted(Group, 1000f, -1000f, 10.0);
        double now = 10.5;
        float edge = OrderArbiter.DedupeMeters;
        AssertEqual(ReserveReleasePacing.Admission.DeadBand, pacing.Admit(Group, 1000f + edge, -1000f, now, 0, 10.0), "on the radius");
        AssertEqual(ReserveReleasePacing.Admission.DeadBand, pacing.Admit(Group, 1000f, -1000f, now, 0, 10.0), "the same target");
        AssertEqual(ReserveReleasePacing.Admission.Go, pacing.Admit(Group, 1000f + edge + 1f, -1000f, now, 0, 10.0), "beyond the radius");
        AssertEqual(ReserveReleasePacing.Admission.Go, pacing.Admit(Group + 1, 1000f, -1000f, now, 0, double.NaN), "another group has its own record");
    }

    private static void AMoveTheExecutorNeverTookIsRetried()
    {
        var pacing = new ReserveReleasePacing();
        pacing.NoteSubmitted(Group, 1000f, -1000f, 10.0);
        // The next tick: the executor has issued nothing for the group since (a cooldown, a refused crossing), so the same target is not a repeat.
        AssertEqual(ReserveReleasePacing.Admission.Go, pacing.Admit(Group, 1000f, -1000f, 10.015, 0, 9.0), "never issued: retried");
        AssertEqual(ReserveReleasePacing.Admission.Go, pacing.Admit(Group, 1000f, -1000f, 10.03, 0, 9.0), "and stays retried");
        // Control: the executor's move was issued at the submission's hour.
        pacing.NoteSubmitted(Group, 1000f, -1000f, 11.0);
        AssertEqual(ReserveReleasePacing.Admission.DeadBand, pacing.Admit(Group, 1000f, -1000f, 11.015, 0, 11.0), "issued: a repeat");
        pacing.Clear();
        AssertEqual(ReserveReleasePacing.Admission.Go, pacing.Admit(Group, 1000f, -1000f, 11.03, 0, 11.0), "a cleared record forgets");
    }

    // The smoke, as the producer sees it: for forty doctrine minutes the ledger holds the brigade while the allocator flips its role every
    // 20 to 90 seconds. The producer's loop is replayed here from the same rules; the count of orders it submits is the finding. The courier
    // keeps an order queued for CourierHours after it goes out; driftMeters is how far the siblings' centroid, and so the target, moves between orders.
    private const double CourierHours = 0.05;

    private static int Replay(Func<int, DirectChildRole> roleAt, Func<int, bool> releasedAt, bool defender, int seconds, int stepSeconds, float driftMeters)
    {
        var pacing = new ReserveReleasePacing();
        int submitted = 0;
        double lastIssued = double.NaN;
        for (int s = 0; s < seconds; s += stepSeconds)
        {
            double now = 10.0 + s / 3600.0;
            var intents = new[] { Intent(roleAt(s), releasedAt(s)) };
            bool released = TacticalReserveCommitGate.TryReleasedDirectChild(intents, Group, defender, out _, out DirectChildRole role);
            if (!pacing.HoldElapsed(Group, released, role, now)) continue;
            float x = 1000f + driftMeters * submitted;
            int queued = !double.IsNaN(lastIssued) && now - lastIssued < CourierHours ? 1 : 0;
            if (pacing.Admit(Group, x, -1400f, now, queued, lastIssued) != ReserveReleasePacing.Admission.Go) continue;
            pacing.NoteSubmitted(Group, x, -1400f, now);
            lastIssued = now;
            submitted++;
        }
        return submitted;
    }

    private static void TheSmokeRoleChurnSendsNoOrder()
    {
        var churn = new[] { DirectChildRole.SupportMain, DirectChildRole.Main, DirectChildRole.RefuseRight, DirectChildRole.RefuseLeft, DirectChildRole.SupportMain };
        // Every change an evidence role: no release at all.
        AssertEqual(0, Replay(s => churn[(s / 40) % churn.Length], s => false, defender: true, seconds: 2400, stepSeconds: 5, driftMeters: 90f), "evidence roles: nothing sent");
        // Even if every role were a real release, a role that changes every 40 s never stands the 3-minute hold.
        AssertEqual(0, Replay(s => churn[(s / 40) % churn.Length], s => true, defender: false, seconds: 2400, stepSeconds: 5, driftMeters: 90f), "a release that flips every 40 s: nothing sent");
        // A release that stands, its target steady: one order, then the dead-band.
        AssertEqual(1, Replay(s => DirectChildRole.RefuseRight, s => true, defender: true, seconds: 2400, stepSeconds: 5, driftMeters: 0f), "a steady release is carried out once");
        // A release whose target drifts 90 m between orders: never an order while the last is queued (at most one per hold-plus-courier cycle).
        int drifting = Replay(s => DirectChildRole.RefuseRight, s => true, defender: true, seconds: 2400, stepSeconds: 5, driftMeters: 90f);
        AssertTrue(drifting >= 2 && drifting <= 14, "a drifting target is paced by the courier, not sent per tick: " + drifting);
    }

    private static void TheProducerIsWiredToTheRules()
    {
        string producer = FlankResponseTests.SourceText("Tactical", "Battle", "Producers", "ReserveReleaseIntentProducer.cs");
        FlankResponseTests.AssertPinned(producer, "army.CurrentBattleStance == BattleStance.Defender", "the Defender rule reads the army's stance");
        FlankResponseTests.AssertPinned(producer, "TryReleasedDirectChild(army.CurrentDirectChildIntents, id, defender, out int sector, out DirectChildRole role)", "the real release, with the stance");
        FlankResponseTests.AssertPinned(producer, "pacing.HoldElapsed(id, released, role, snapshot.DoctrineHours)", "the hold, on the snapshot's doctrine clock");
        FlankResponseTests.AssertPinned(producer, "pacing.Admit(id,", "the dead-band");
        FlankResponseTests.AssertPinned(producer, "pacing.NoteSubmitted(id,", "the producer records what it sent");
        FlankResponseTests.AssertPinned(producer, "BattleScope.Register(\"reserve-release-producer\"", "the pacing is battle-scoped");
    }
}
