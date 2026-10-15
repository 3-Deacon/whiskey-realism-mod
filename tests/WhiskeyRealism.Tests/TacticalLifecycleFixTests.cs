using System;
using WhiskeyRealism.Tactical.Orchestrator;

// WS T2 (2026-10-14 remediation): battle lifecycle, worker lifecycle, caches,
// operations director and ledger clock. Pure-decision tests only; runtime
// adapters feed these from vanilla state (anchors in each production file).
internal static class TacticalLifecycleFixTests
{
    public static void Run()
    {
        Pass("t9 lifecycle first observation starts battle", T9FirstObservationStartsBattle);
        Pass("t9 lifecycle steady ticks report none", T9SteadyTicksReportNone);
        Pass("t9 lifecycle new battle units id restarts", T9NewBattleUnitsIdRestarts);
        Pass("t9 lifecycle clock rewind on same battle restarts", T9ClockRewindRestarts);
        Pass("t9 lifecycle battle day advance restarts", T9BattleDayAdvanceRestarts);
        Pass("t9 lifecycle battle units destroyed ends battle", T9BattleUnitsDestroyedEndsBattle);
        Pass("t9 lifecycle destroy of other battle units ignored", T9DestroyOfOtherBattleUnitsIgnored);
        Pass("t9 lifecycle zero units still ends after two ticks", T9ZeroUnitsStillEndsAfterTwoTicks);
        Pass("t9 lifecycle scene change restarts", T9SceneChangeRestarts);
        Pass("t9 heavy gate clock rewind runs immediately", T9HeavyGateClockRewindRuns);

        Pass("brm2 publish for stale battle sequence rejected", BrM2PublishForStaleBattleRejected);
        Pass("brm2 publish for live battle sequence accepted", BrM2PublishForLiveBattleAccepted);
        Pass("brm2 current hides snapshot after battle closed", BrM2CurrentHiddenAfterBattleClosed);
        Pass("brm2 carry-forward requires same battle and bounded age", BrM2CarryForwardBounded);
        Pass("brm2 snapshot keeps per-side data age", BrM2SnapshotKeepsPerSideDataAge);
        Pass("brm2 worker starts only after successful bootstrap", BrM2WorkerStartsOnlyWhenActive);
        Pass("brm2 worker capture only on heavy build", BrM2WorkerCaptureOnlyOnHeavyBuild);

        Pass("brh5 roster signature tracks rout and list identity", BrH5RosterSignatureTracksRoutAndListIdentity);
        Pass("brh5 command tree rebuilds on roster or shift change", BrH5CommandTreeRebuildPolicy);
        Pass("brh5 map objective content hash sees owner and active", BrH5MapObjectiveContentHash);
        Pass("brh5 avenue source signature sees arrivals and owners", BrH5AvenueSourceSignature);
        Pass("brh5 avenue content hash sees owner change", BrH5AvenueContentHash);

        Pass("t14 confidence dip inside commit window holds commitment", T14ConfidenceDipInsideWindowHolds);
        Pass("t14 confidence above exit floor after window holds commitment", T14ConfidenceAboveExitFloorHolds);
        Pass("t14 confidence below exit floor after window downgrades", T14ConfidenceBelowExitFloorDowngrades);
        Pass("t14 commit decision uses army before assignment", T14CommitOddsUseAssignedStrength);
        Pass("t14 soft abort odds use operation assigned strength", T14SoftAbortOddsUseAssignedStrength);

        Pass("t8 ledger clock is battle seconds", T8LedgerClockIsBattleSeconds);

        Pass("t15 released reserve lets vanilla commit", T15ReleasedReserveAllowsVanillaCommit);
        Pass("t15 unreleased reserve still held", T15UnreleasedReserveStillHeld);
        Pass("t15 released reserve permits reserve list bias", T15ReleasedReservePermitsListBias);
    }

    private static void Pass(string name, Action test)
    {
        test();
        Console.WriteLine("PASS " + name);
    }

    private static BattleLifecycleObservation Obs(int bunitsId, int days, float clock, int units = 100, int scene = 7)
    {
        return new BattleLifecycleObservation(bunitsId, days, scene, clock, units);
    }

    // ---- T9 lifecycle ----

    private static void T9FirstObservationStartsBattle()
    {
        var d = new TacticalBattleLifecycleDetector();
        var r = d.Observe(Obs(11, 0, 8.0f));
        TestHarness.AssertEqual(BattleLifecycleTransition.Start, r.Transition, "first");
        TestHarness.AssertTrue(d.InBattle, "inBattle");
    }

    private static void T9SteadyTicksReportNone()
    {
        var d = new TacticalBattleLifecycleDetector();
        d.Observe(Obs(11, 0, 8.0f));
        TestHarness.AssertEqual(BattleLifecycleTransition.None, d.Observe(Obs(11, 0, 8.0f)).Transition, "same-cycle");
        TestHarness.AssertEqual(BattleLifecycleTransition.None, d.Observe(Obs(11, 0, 8.015f)).Transition, "next-cycle");
    }

    private static void T9NewBattleUnitsIdRestarts()
    {
        // Battle 1 ends without an observed teardown, battle 2 is a new scene
        // (new BattleUnits instance; vanilla rebuilds the scene on quit, 24095-24113).
        var d = new TacticalBattleLifecycleDetector();
        d.Observe(Obs(11, 0, 15.0f));
        var r = d.Observe(Obs(22, 0, 9.0f));
        TestHarness.AssertEqual(BattleLifecycleTransition.Restart, r.Transition, "restart");
        TestHarness.AssertEqual("battle-units-changed", r.Reason, "reason");
        TestHarness.AssertEqual(BattleLifecycleTransition.None, d.Observe(Obs(22, 0, 9.015f)).Transition, "settled");
    }

    private static void T9ClockRewindRestarts()
    {
        // Same instance id (id recycled) but battle clock went backwards: reload.
        var d = new TacticalBattleLifecycleDetector();
        d.Observe(Obs(11, 0, 15.0f));
        var r = d.Observe(Obs(11, 0, 10.0f));
        TestHarness.AssertEqual(BattleLifecycleTransition.Restart, r.Transition, "restart");
        TestHarness.AssertEqual("battle-clock-rewound", r.Reason, "reason");
    }

    private static void T9BattleDayAdvanceRestarts()
    {
        // CheckTimeIssues 86384 increments battlepasseddays; AI redeploys at day start
        // (85754-85816), so the orchestrator re-bootstraps against the new deployment.
        var d = new TacticalBattleLifecycleDetector();
        d.Observe(Obs(11, 0, 23.9f));
        var r = d.Observe(Obs(11, 1, 24.2f));
        TestHarness.AssertEqual(BattleLifecycleTransition.Restart, r.Transition, "restart");
        TestHarness.AssertEqual("battle-day-changed", r.Reason, "reason");
    }

    private static void T9BattleUnitsDestroyedEndsBattle()
    {
        // BattleUnits.OnDestroy (79219) nulls completeunitlist; AI never ticks again.
        var d = new TacticalBattleLifecycleDetector();
        d.Observe(Obs(11, 0, 8.0f));
        var r = d.NotifyBattleUnitsDestroyed(11);
        TestHarness.AssertEqual(BattleLifecycleTransition.End, r.Transition, "end");
        TestHarness.AssertFalse(d.InBattle, "not in battle");
        TestHarness.AssertEqual(BattleLifecycleTransition.None, d.NotifyBattleUnitsDestroyed(11).Transition, "idempotent");
        TestHarness.AssertEqual(BattleLifecycleTransition.Start, d.Observe(Obs(33, 0, 8.0f)).Transition, "next battle starts");
    }

    private static void T9DestroyOfOtherBattleUnitsIgnored()
    {
        // The campaign-map BattleUnits (InitCampaignBattleUnits 79044) is destroyed
        // when the battle scene loads; that must not tear down the battle.
        var d = new TacticalBattleLifecycleDetector();
        d.Observe(Obs(11, 0, 8.0f));
        TestHarness.AssertEqual(BattleLifecycleTransition.None, d.NotifyBattleUnitsDestroyed(99).Transition, "other");
        TestHarness.AssertTrue(d.InBattle, "still in battle");
    }

    private static void T9ZeroUnitsStillEndsAfterTwoTicks()
    {
        var d = new TacticalBattleLifecycleDetector();
        d.Observe(Obs(11, 0, 8.0f));
        TestHarness.AssertEqual(BattleLifecycleTransition.None, d.Observe(Obs(11, 0, 8.0f, units: 0)).Transition, "one zero");
        TestHarness.AssertEqual(BattleLifecycleTransition.End, d.Observe(Obs(11, 0, 8.0f, units: 0)).Transition, "two zeros");
    }

    private static void T9SceneChangeRestarts()
    {
        var d = new TacticalBattleLifecycleDetector();
        d.Observe(Obs(11, 0, 8.0f, scene: 1));
        var r = d.Observe(Obs(11, 0, 8.5f, scene: 2));
        TestHarness.AssertEqual(BattleLifecycleTransition.Restart, r.Transition, "restart");
    }

    private static void T9HeavyGateClockRewindRuns()
    {
        // Before: elapsed was clamped to 0 so a rewound clock never reached
        // max-interval and the stale snapshot was reused indefinitely.
        var sig = new TacticalBattleStateSignature(10, 5, 5, 1, 1, false, 3, 0, 0);
        var input = new TacticalHeavyPathGate.Input(sig, 9.0f, 15.0f, sig, 0.003f, false);
        var dec = TacticalHeavyPathGate.Decide(input);
        TestHarness.AssertTrue(dec.ShouldRun, "runs");
        TestHarness.AssertEqual("clock-rewind", dec.Reason, "reason");
    }

    // ---- BR-M2 worker lifecycle ----

    private static TacticalDecisionSnapshot Snap(int seq, float hours = 1f)
    {
        return new TacticalDecisionSnapshot(seq, 1L, hours, 0f,
            TacticalSideDecision.Empty, TacticalSideDecision.Empty, null, null);
    }

    private static void WithLiveBattle(int seq, Action body)
    {
        try
        {
            TacticalDecisionSnapshot.SetLiveBattleSequence(seq);
            body();
        }
        finally
        {
            TacticalDecisionSnapshot.ResetForBattleEnd();
            TacticalDecisionSnapshot.SetLiveBattleSequence(TacticalDecisionSnapshot.UngatedBattleSequence);
        }
    }

    private static void BrM2PublishForStaleBattleRejected()
    {
        WithLiveBattle(5, () =>
        {
            TestHarness.AssertFalse(TacticalDecisionSnapshot.TryPublish(Snap(4)), "stale rejected");
            TestHarness.AssertTrue(ReferenceEquals(TacticalDecisionSnapshot.Empty, TacticalDecisionSnapshot.Current), "current empty");
        });
    }

    private static void BrM2PublishForLiveBattleAccepted()
    {
        WithLiveBattle(5, () =>
        {
            TestHarness.AssertTrue(TacticalDecisionSnapshot.TryPublish(Snap(5)), "live accepted");
            TestHarness.AssertEqual(5, TacticalDecisionSnapshot.Current.BattleSequence, "current");
        });
    }

    private static void BrM2CurrentHiddenAfterBattleClosed()
    {
        // A worker that outlived Join(250) publishes after teardown: readers
        // (BattleGroupStancePatch tier-2) must never see it.
        WithLiveBattle(5, () =>
        {
            TacticalDecisionSnapshot.TryPublish(Snap(5));
            TacticalDecisionSnapshot.SetLiveBattleSequence(TacticalDecisionSnapshot.ClosedBattleSequence);
            TacticalDecisionSnapshot.Publish(Snap(5));
            TestHarness.AssertTrue(ReferenceEquals(TacticalDecisionSnapshot.Empty, TacticalDecisionSnapshot.Current), "hidden");
        });
    }

    private static void BrM2CarryForwardBounded()
    {
        float max = TacticalDecisionSnapshot.MaxCarryForwardBattleHours;
        TestHarness.AssertTrue(TacticalDecisionSnapshot.IsCarryForwardFresh(3, 3, 10.00f, 10.05f, max), "fresh");
        TestHarness.AssertFalse(TacticalDecisionSnapshot.IsCarryForwardFresh(3, 3, 10.00f, 10.00f + max + 0.01f, max), "too old");
        TestHarness.AssertFalse(TacticalDecisionSnapshot.IsCarryForwardFresh(2, 3, 10.00f, 10.01f, max), "other battle");
        TestHarness.AssertFalse(TacticalDecisionSnapshot.IsCarryForwardFresh(3, 3, 12.00f, 10.00f, max), "clock rewound");
    }

    private static void BrM2SnapshotKeepsPerSideDataAge()
    {
        var s = new TacticalDecisionSnapshot(1, 1L, 10f, 0f,
            TacticalSideDecision.Empty, TacticalSideDecision.Empty, null, null,
            side0DataBattleHours: 9.5f, side1DataBattleHours: 10f);
        TestHarness.AssertEqual(9.5f, s.GetSideDataBattleHours(0), "side0");
        TestHarness.AssertEqual(10f, s.GetSideDataBattleHours(1), "side1");
        TestHarness.AssertEqual(10f, Snap(1, 10f).GetSideDataBattleHours(0), "defaults to capture time");
    }

    private static void BrM2WorkerStartsOnlyWhenActive()
    {
        TestHarness.AssertTrue(TacticalBattleCoordinator.ShouldStartWorker(workerEnabled: true, coordinatorActive: true), "start");
        TestHarness.AssertFalse(TacticalBattleCoordinator.ShouldStartWorker(workerEnabled: true, coordinatorActive: false), "failed bootstrap");
        TestHarness.AssertFalse(TacticalBattleCoordinator.ShouldStartWorker(workerEnabled: false, coordinatorActive: true), "disabled");
    }

    private static void BrM2WorkerCaptureOnlyOnHeavyBuild()
    {
        TestHarness.AssertTrue(TacticalBattleCoordinator.ShouldCaptureWorkerInput(workerRunning: true, heavyBuiltThisTick: true), "capture");
        TestHarness.AssertFalse(TacticalBattleCoordinator.ShouldCaptureWorkerInput(workerRunning: true, heavyBuiltThisTick: false), "reuse");
        TestHarness.AssertFalse(TacticalBattleCoordinator.ShouldCaptureWorkerInput(workerRunning: false, heavyBuiltThisTick: true), "no worker");
    }

    // ---- BR-H5 caches ----

    private static void BrH5RosterSignatureTracksRoutAndListIdentity()
    {
        var a = new TacticalRosterSignature(listIdentity: 1, unitCount: 40, activeCount: 30, routedCount: 2, markedForRoutCount: 1, idHash: 99);
        var same = new TacticalRosterSignature(1, 40, 30, 2, 1, 99);
        TestHarness.AssertTrue(a.Equals(same), "same");
        TestHarness.AssertFalse(a.Equals(new TacticalRosterSignature(1, 40, 30, 3, 1, 99)), "routed");
        TestHarness.AssertFalse(a.Equals(new TacticalRosterSignature(1, 40, 29, 2, 1, 99)), "active");
        // DetachUnits 90239 / ReattachCompany 90609 / CreateUnitDefault 89670 call
        // UpdateUnitList (79974), which replaces completeunitlist with a new list.
        TestHarness.AssertFalse(a.Equals(new TacticalRosterSignature(2, 40, 30, 2, 1, 99)), "list replaced");
        TestHarness.AssertFalse(a.Equals(new TacticalRosterSignature(1, 40, 30, 2, 1, 98)), "ids");
    }

    private static void BrH5CommandTreeRebuildPolicy()
    {
        var r = new TacticalRosterSignature(1, 40, 30, 2, 1, 99);
        var routed = new TacticalRosterSignature(1, 40, 30, 3, 1, 99);
        TestHarness.AssertTrue(TacticalCacheSignatures.ShouldRebuildCommandTree(false, 0, 0, r, r), "invalid");
        TestHarness.AssertTrue(TacticalCacheSignatures.ShouldRebuildCommandTree(true, 0, 1, r, r), "shift");
        TestHarness.AssertTrue(TacticalCacheSignatures.ShouldRebuildCommandTree(true, 0, 0, r, routed), "roster");
        TestHarness.AssertFalse(TacticalCacheSignatures.ShouldRebuildCommandTree(true, 0, 0, r, r), "hit");
    }

    private static void BrH5MapObjectiveContentHash()
    {
        var p = new WhiskeyRealism.Tactical.Operations.TacticalMapPoint(100f, 200f);
        var baseline = new[] { new MapObjectiveCapture("Hill", p, 0, true) };
        int h = TacticalCacheSignatures.MapObjectivesContentHash(baseline);
        TestHarness.AssertEqual(h, TacticalCacheSignatures.MapObjectivesContentHash(new[] { new MapObjectiveCapture("Hill", p, 0, true) }), "stable");
        // Objectives.owner flips on capture at runtime (105746).
        TestHarness.AssertTrue(h != TacticalCacheSignatures.MapObjectivesContentHash(new[] { new MapObjectiveCapture("Hill", p, 1, true) }), "owner");
        TestHarness.AssertTrue(h != TacticalCacheSignatures.MapObjectivesContentHash(new[] { new MapObjectiveCapture("Hill", p, 0, false) }), "active");
    }

    private static void BrH5AvenueSourceSignature()
    {
        var s = new TacticalAvenueSourceSignature(scheduledArrivals: 3, entryPointOwnerHash: 11, battlePassedDays: 0);
        TestHarness.AssertTrue(s.Equals(new TacticalAvenueSourceSignature(3, 11, 0)), "same");
        // scheduledarrival.RemoveAt 87620 when a column arrives; entrypoint.owner 87601.
        TestHarness.AssertFalse(s.Equals(new TacticalAvenueSourceSignature(2, 11, 0)), "arrived");
        TestHarness.AssertFalse(s.Equals(new TacticalAvenueSourceSignature(3, 12, 0)), "owner");
        TestHarness.AssertFalse(s.Equals(new TacticalAvenueSourceSignature(3, 11, 1)), "day");
    }

    private static void BrH5AvenueContentHash()
    {
        var a = new[] { Avenue(0) };
        TestHarness.AssertEqual(TacticalCacheSignatures.AvenuesContentHash(a), TacticalCacheSignatures.AvenuesContentHash(new[] { Avenue(0) }), "stable");
        TestHarness.AssertTrue(TacticalCacheSignatures.AvenuesContentHash(a) != TacticalCacheSignatures.AvenuesContentHash(new[] { Avenue(1) }), "owner");
    }

    private static WhiskeyRealism.Tactical.Operations.TacticalApproachAvenueObservation Avenue(int owner)
    {
        return new WhiskeyRealism.Tactical.Operations.TacticalApproachAvenueObservation(
            WhiskeyRealism.Tactical.Operations.TacticalApproachAvenueSource.EntryPoint,
            owner, 0f, 0f, 100f, 100f, 0.7f, true, false, "entrypoint-0");
    }

    // ---- T14 operation flicker ----

    private static WhiskeyRealism.Tactical.Operations.BattlefieldObjectiveEstimate Line(string id, float enemy, float confidence, bool mainLine = true)
    {
        return new WhiskeyRealism.Tactical.Operations.BattlefieldObjectiveEstimate(
            id, WhiskeyRealism.Tactical.Operations.TacticalObjectiveType.EnemyLine, enemy, confidence, mainLine, 0.65f, 300f, 400f, 0.1f, 0.1f);
    }

    private static WhiskeyRealism.Tactical.Operations.TacticalOperationDirectorDecision DecideCommitted(
        float commitUntil, float now, float confidence)
    {
        var current = WhiskeyRealism.Tactical.Operations.OperationRecord.CreateCommittedForTest(
            WhiskeyRealism.Tactical.Operations.TacticalOperationShape.SingleMainEffort, "line", minCommitUntilSeconds: commitUntil);
        var input = WhiskeyRealism.Tactical.Operations.TacticalOperationDirectorInput.ForTest(
            current, now, 5000f, 0.30f, 0.5f, 0.5f, new[] { Line("line", 900f, confidence) });
        return WhiskeyRealism.Tactical.Operations.TacticalOperationDirector.Decide(input);
    }

    private static void T14ConfidenceDipInsideWindowHolds()
    {
        var d = DecideCommitted(commitUntil: 1800f, now: 600f, confidence: 0.55f);
        TestHarness.AssertEqual(WhiskeyRealism.Tactical.Operations.TacticalOperationPhase.Committed, d.Operation.Phase, "phase");
        TestHarness.AssertEqual("commit-window", d.Reason, "reason");
        var low = DecideCommitted(commitUntil: 1800f, now: 600f, confidence: 0.40f);
        TestHarness.AssertEqual(WhiskeyRealism.Tactical.Operations.TacticalOperationPhase.Committed, low.Operation.Phase, "below exit floor still inside window");
    }

    private static void T14ConfidenceAboveExitFloorHolds()
    {
        var d = DecideCommitted(commitUntil: 300f, now: 600f, confidence: 0.55f);
        TestHarness.AssertEqual(WhiskeyRealism.Tactical.Operations.TacticalOperationPhase.Committed, d.Operation.Phase, "phase");
        TestHarness.AssertEqual("line", d.Operation.PrimaryObjectiveId, "primary");
    }

    private static void T14ConfidenceBelowExitFloorDowngrades()
    {
        var d = DecideCommitted(commitUntil: 300f, now: 600f, confidence: 0.40f);
        TestHarness.AssertEqual(WhiskeyRealism.Tactical.Operations.TacticalOperationPhase.Scouting, d.Operation.Phase, "phase");
        TestHarness.AssertEqual("contact-downgraded", d.Reason, "reason");
    }

    private static void T14CommitOddsUseAssignedStrength()
    {
        // Before commitment nothing is assigned: the commit decision weighs the army,
        // and a stray assigned value is ignored.
        var input = WhiskeyRealism.Tactical.Operations.TacticalOperationDirectorInput.ForTest(
            WhiskeyRealism.Tactical.Operations.OperationRecord.Noop, 120f, 10000f, 0.05f, 0.5f, 0.5f,
            new[] { Line("line", 2000f, 0.9f) }).WithOperationAssignedStrength(500f);
        var d = WhiskeyRealism.Tactical.Operations.TacticalOperationDirector.Decide(input);
        TestHarness.AssertEqual(WhiskeyRealism.Tactical.Operations.TacticalOperationPhase.Committed, d.Operation.Phase, "commit on army odds");
    }

    private static void T14SoftAbortOddsUseAssignedStrength()
    {
        var current = WhiskeyRealism.Tactical.Operations.OperationRecord.CreateCommittedForTest(
            WhiskeyRealism.Tactical.Operations.TacticalOperationShape.SingleMainEffort, "line", minCommitUntilSeconds: 1800f);
        var input = WhiskeyRealism.Tactical.Operations.TacticalOperationDirectorInput.ForTest(
            current, 600f, 5000f, 0.30f, 0.5f, 0.5f, new[] { Line("line", 900f, 0.9f) });
        var army = WhiskeyRealism.Tactical.Operations.TacticalOperationDirector.Decide(input);
        TestHarness.AssertEqual(WhiskeyRealism.Tactical.Operations.TacticalOperationPhase.Committed, army.Operation.Phase, "army odds would hide the collapse");
        var d = WhiskeyRealism.Tactical.Operations.TacticalOperationDirector.Decide(input.WithOperationAssignedStrength(600f));
        TestHarness.AssertEqual(WhiskeyRealism.Tactical.Operations.TacticalOperationPhase.SoftAbort, d.Operation.Phase, "phase");
        TestHarness.AssertEqual("odds-collapse", d.Reason, "reason");
    }

    // ---- T8 ledger clock ----

    private static void T8LedgerClockIsBattleSeconds()
    {
        // GameVars.currenttimefromstart is battle hours (Hour + battlepasseddays*24,
        // SetTimeVariables 93545); commit windows (420/900/1200 s) are battle seconds.
        TestHarness.AssertEqual(30600f, TacticalBattleCoordinator.LedgerClockSecondsFromBattleHours(8.5f), "8.5h");
        TestHarness.AssertEqual(0f, TacticalBattleCoordinator.LedgerClockSecondsFromBattleHours(float.NaN), "nan");
        TestHarness.AssertEqual(0f, TacticalBattleCoordinator.LedgerClockSecondsFromBattleHours(-1f), "negative");
    }

    // ---- T15 reserve gate ----

    private static CommandIntentResolution ReserveResolution()
    {
        return new CommandIntentResolution(true,
            new CommandNodeIntent("node-200", "node-200", DirectChildRole.Reserve, DirectChildAxis.Hold,
                primarySector: 2, supportPriority: 50, aggressionBias01: 0.5f, depth: 1),
            "exact-command-node");
    }

    private static void T15ReleasedReserveAllowsVanillaCommit()
    {
        var input = new TacticalReserveCommitGate.Input(true, ReserveResolution(), false, false, 1f, 1f, reserveReleased: true);
        var d = TacticalReserveCommitGate.Decide(input);
        TestHarness.AssertEqual(TacticalReserveCommitGate.Action.Allow, d.Action, "action");
        TestHarness.AssertEqual("reserve-released", d.Reason, "reason");
    }

    private static void T15UnreleasedReserveStillHeld()
    {
        var input = new TacticalReserveCommitGate.Input(true, ReserveResolution(), false, false, 1f, 1f);
        TestHarness.AssertEqual(TacticalReserveCommitGate.Action.Deny, TacticalReserveCommitGate.Decide(input).Action, "deny");
    }

    private static void T15ReleasedReservePermitsListBias()
    {
        TestHarness.AssertFalse(TacticalReserveCommitGate.PermitReserveListBias(ReserveResolution()), "held");
        TestHarness.AssertTrue(TacticalReserveCommitGate.PermitReserveListBias(ReserveResolution(), reserveReleased: true), "released");
    }
}
