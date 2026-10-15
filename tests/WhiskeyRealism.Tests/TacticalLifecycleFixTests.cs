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
        Pass("t9 lifecycle night rollover keeps battle", T9NightRolloverKeepsBattle);
        Pass("t9 lifecycle midnight wrap without day count is not a reload", T9MidnightWrapIsNotReload);
        Pass("t9 end-of-day gate suspends then resumes once", T9EndOfDayGateSuspendsThenResumes);
        Pass("t9 doctrine clock frozen overnight and monotonic", T9DoctrineClockFrozenOvernight);
        Pass("t9 doctrine clock ignores midnight wrap", T9DoctrineClockIgnoresMidnightWrap);
        Pass("t9 lifecycle battle units destroyed ends battle", T9BattleUnitsDestroyedEndsBattle);
        Pass("t9 lifecycle destroy of other battle units ignored", T9DestroyOfOtherBattleUnitsIgnored);
        Pass("t9 lifecycle zero units still ends after two ticks", T9ZeroUnitsStillEndsAfterTwoTicks);
        Pass("t9 lifecycle scene change restarts", T9SceneChangeRestarts);
        Pass("t9 heavy gate clock rewind runs immediately", T9HeavyGateClockRewindRuns);

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

        Pass("perf ledger ticks on new snapshot", PerfLedgerTicksOnNewSnapshot);
        Pass("perf ledger skips unchanged snapshot within interval", PerfLedgerSkipsUnchangedSnapshot);
        Pass("perf ledger ticks on mode change or max interval", PerfLedgerTicksOnModeChangeOrMaxInterval);
        Pass("perf ledger ticks after clock rewind", PerfLedgerTicksAfterClockRewind);
        Pass("perf command tree hash sees parent change", PerfCommandTreeHashSeesParentChange);
        Pass("perf frame cache hits within one frame only", PerfFrameCacheHitsWithinOneFrame);
        Pass("perf intents hash equal for reallocated identical intents", PerfIntentsHashEqualForReallocatedIntents);
        Pass("perf intents hash sees role change", PerfIntentsHashSeesRoleChange);

        Pass("dead wl cic alliance suppressed under ai_vs_ai", WlCicAllianceSuppressedUnderAiVsAi);
        Pass("dead player alliance suppressed outside ai_vs_ai", PlayerAllianceSuppressedOutsideAiVsAi);
        Pass("dead w&l subordinate career suppresses nothing", WlSubordinateCareerSuppressesNothing);

        Pass("t14 operation strength counts AI combat units only", OperationStrengthCountsAiCombatUnitsOnly);

        Pass("t9 feud instance never drives the orchestrator", FeudInstanceNeverDrivesOrchestrator);
    }

    private static void Pass(string name, Action test)
    {
        test();
        Console.WriteLine("PASS " + name);
    }

    private static BattleLifecycleObservation Obs(int bunitsId, int calendarDay, float clock, int units = 100, int scene = 7)
    {
        return new BattleLifecycleObservation(bunitsId, scene, calendarDay, clock, units);
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

    private static void T9NightRolloverKeepsBattle()
    {
        // CheckTimeIssues 86384 increments battlepasseddays mid-night (eodcycle 2->3);
        // vanilla keeps all AI state, so the orchestrator must too.
        var d = new TacticalBattleLifecycleDetector();
        d.Observe(Obs(11, 10, 23.9f));
        TestHarness.AssertEqual(BattleLifecycleTransition.None, d.Observe(Obs(11, 11, 24.2f)).Transition, "same battle");
    }

    private static void T9MidnightWrapIsNotReload()
    {
        // Midnight passing while eodcycle != 2 skips battlepasseddays++, so
        // currenttimefromstart (Hour + days*24) drops ~24h; the calendar day advanced.
        var d = new TacticalBattleLifecycleDetector();
        d.Observe(Obs(11, 10, 23.9f));
        TestHarness.AssertEqual(BattleLifecycleTransition.None, d.Observe(Obs(11, 11, 0.1f)).Transition, "wrap");
        TestHarness.AssertEqual(BattleLifecycleTransition.None, d.Observe(Obs(11, 11, 0.2f)).Transition, "after wrap");
    }

    private static void T9EndOfDayGateSuspendsThenResumes()
    {
        var g = new TacticalEndOfDayGate();
        TestHarness.AssertEqual(EndOfDayPhase.Fighting, g.Observe(0), "day");
        TestHarness.AssertEqual(EndOfDayPhase.Suspended, g.Observe(1), "eat");
        TestHarness.AssertEqual(EndOfDayPhase.Suspended, g.Observe(2), "night");
        TestHarness.AssertEqual(EndOfDayPhase.Suspended, g.Observe(5), "deployment");
        // SetActiveDeploymentPhase(false) sets eodcycle = 0 (164874-164882).
        TestHarness.AssertEqual(EndOfDayPhase.Resumed, g.Observe(0), "morning");
        TestHarness.AssertEqual(EndOfDayPhase.Fighting, g.Observe(0), "once");
    }

    private static void T9DoctrineClockFrozenOvernight()
    {
        var c = new TacticalDoctrineClock();
        TestHarness.AssertEqual(8.0f, c.Advance(8.0f, suspended: false), "start");
        TestHarness.AssertEqual(10.0f, c.Advance(10.0f, suspended: false), "day");
        TestHarness.AssertEqual(10.0f, c.Advance(20.0f, suspended: true), "evening");
        TestHarness.AssertEqual(10.0f, c.Advance(31.0f, suspended: true), "night");
        TestHarness.AssertEqual(10.5f, c.Advance(31.5f, suspended: false), "next morning resumes without a jump");
    }

    private static void T9DoctrineClockIgnoresMidnightWrap()
    {
        var c = new TacticalDoctrineClock();
        c.Advance(23.5f, suspended: false);
        TestHarness.AssertEqual(23.5f, c.Advance(0.1f, suspended: false), "no rewind");
        TestHarness.AssertTrue(Math.Abs(c.Advance(0.2f, suspended: false) - 23.6f) < 0.0001f, "keeps advancing");
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
        var detached = new TacticalRosterSignature(2, 41, 30, 2, 1, 98);
        TestHarness.AssertEqual(CommandTreeRefresh.Rebuild, TacticalCacheSignatures.ClassifyCommandTreeRefresh(false, 0, 0, r, r), "invalid");
        TestHarness.AssertEqual(CommandTreeRefresh.Rebuild, TacticalCacheSignatures.ClassifyCommandTreeRefresh(true, 0, 1, r, r), "shift");
        TestHarness.AssertEqual(CommandTreeRefresh.Rebuild, TacticalCacheSignatures.ClassifyCommandTreeRefresh(true, 0, 0, r, detached), "list replaced");
        // A rout only changes live flags: re-filter the cached probes, no hierarchy walk.
        TestHarness.AssertEqual(CommandTreeRefresh.RefreshLiveFlags, TacticalCacheSignatures.ClassifyCommandTreeRefresh(true, 0, 0, r, routed), "rout");
        TestHarness.AssertEqual(CommandTreeRefresh.Hit, TacticalCacheSignatures.ClassifyCommandTreeRefresh(true, 0, 0, r, r), "hit");
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

    // ---- Coordinator perf ----

    private static void PerfLedgerTicksOnNewSnapshot()
    {
        TestHarness.AssertTrue(TacticalBattleCoordinator.ShouldTickOperationsLedger(
            snapshotChanged: true, modeChanged: false, nowBattleSeconds: 100f, lastTickBattleSeconds: 99f), "new snapshot");
    }

    private static void PerfLedgerSkipsUnchangedSnapshot()
    {
        TestHarness.AssertFalse(TacticalBattleCoordinator.ShouldTickOperationsLedger(
            snapshotChanged: false, modeChanged: false, nowBattleSeconds: 100f, lastTickBattleSeconds: 90f), "unchanged");
    }

    private static void PerfLedgerTicksOnModeChangeOrMaxInterval()
    {
        TestHarness.AssertTrue(TacticalBattleCoordinator.ShouldTickOperationsLedger(false, true, 100f, 99f), "mode");
        float max = TacticalBattleCoordinator.OperationsLedgerMaxIntervalBattleSeconds;
        TestHarness.AssertTrue(TacticalBattleCoordinator.ShouldTickOperationsLedger(false, false, 100f + max, 100f), "max interval");
    }

    private static void PerfLedgerTicksAfterClockRewind()
    {
        TestHarness.AssertTrue(TacticalBattleCoordinator.ShouldTickOperationsLedger(false, false, 50f, 100f), "rewind");
    }

    private static void PerfCommandTreeHashSeesParentChange()
    {
        var a = TreeOf(parentOfChild: "node-1");
        var b = TreeOf(parentOfChild: "node-1");
        var c = TreeOf(parentOfChild: "node-3");
        TestHarness.AssertEqual(TacticalBattleCoordinator.CommandTreeHash(a), TacticalBattleCoordinator.CommandTreeHash(b), "stable");
        TestHarness.AssertTrue(TacticalBattleCoordinator.CommandTreeHash(a) != TacticalBattleCoordinator.CommandTreeHash(c), "parent");
    }

    private static CommandTreeSnapshot TreeOf(string parentOfChild)
    {
        var probes = new[]
        {
            new CommandTreeBuilder.CommandProbe(1, 0, 0, 17, "army", true, false, false),
            new CommandTreeBuilder.CommandProbe(3, 1, 0, 16, "corps", true, false, false),
            new CommandTreeBuilder.CommandProbe(2, parentOfChild == "node-1" ? 1 : 3, 0, 15, "div", true, false, false),
        };
        return CommandTreeBuilder.Build(probes, 0, 0);
    }

    private static void PerfFrameCacheHitsWithinOneFrame()
    {
        var cache = new TacticalFrameCache<string>();
        TestHarness.AssertFalse(cache.TryGet(10, 7, out _), "empty");
        cache.Set(10, 7, "enemy");
        TestHarness.AssertTrue(cache.TryGet(10, 7, out string hit) && hit == "enemy", "same frame");
        TestHarness.AssertFalse(cache.TryGet(10, 8, out _), "other unit");
        TestHarness.AssertFalse(cache.TryGet(11, 7, out _), "next frame");
        TestHarness.AssertFalse(cache.TryGet(10, 7, out _), "previous frame entries dropped");
    }

    private static CommandNodeIntent[] Intents(DirectChildRole role)
    {
        return new[]
        {
            new CommandNodeIntent("node-1", "node-1", role, DirectChildAxis.Hold,
                primarySector: 1, supportPriority: 50, aggressionBias01: 0.5f, depth: 1),
            new CommandNodeIntent("node-2", "node-1", DirectChildRole.Reserve, DirectChildAxis.Hold,
                primarySector: 2, supportPriority: 10, aggressionBias01: 0.2f, depth: 2),
        };
    }

    private static void PerfIntentsHashEqualForReallocatedIntents()
    {
        // ArmyOrchestrator.ObserveDirectChildEvidenceWithZones reallocates the list
        // every call; the ledger gate must compare content, not references.
        TestHarness.AssertEqual(
            TacticalBattleCoordinator.CommandNodeIntentsHash(Intents(DirectChildRole.Main)),
            TacticalBattleCoordinator.CommandNodeIntentsHash(Intents(DirectChildRole.Main)), "same content");
    }

    private static void PerfIntentsHashSeesRoleChange()
    {
        TestHarness.AssertTrue(
            TacticalBattleCoordinator.CommandNodeIntentsHash(Intents(DirectChildRole.Main))
            != TacticalBattleCoordinator.CommandNodeIntentsHash(Intents(DirectChildRole.SupportMain)), "role");
    }

    // ---- Suppressed alliance ----

    private static void WlCicAllianceSuppressedUnderAiVsAi()
    {
        // W&L forces ai_vs_ai (29789); a CIC player owns every unit of that alliance.
        TestHarness.AssertEqual(1, TacticalBattleCoordinator.ResolveSuppressedAllianceId(aiVsAi: true, playerAllianceId: -1, playerCicAllianceId: 1), "cic");
    }

    private static void PlayerAllianceSuppressedOutsideAiVsAi()
    {
        TestHarness.AssertEqual(0, TacticalBattleCoordinator.ResolveSuppressedAllianceId(false, 0, -1), "player");
    }

    private static void WlSubordinateCareerSuppressesNothing()
    {
        // Below CIC the player's own command is protected per unit (TacticalWriteGuard).
        TestHarness.AssertEqual(-1, TacticalBattleCoordinator.ResolveSuppressedAllianceId(true, -1, -1), "none");
    }

    private static void OperationStrengthCountsAiCombatUnitsOnly()
    {
        TestHarness.AssertTrue(TacticalBattleCoordinator.CountsTowardOperationStrength(0, false, false, false, true, false), "line infantry");
        // The W&L player's own command stays in allattachedunits but is player-owned.
        TestHarness.AssertFalse(TacticalBattleCoordinator.CountsTowardOperationStrength(0, false, false, false, true, true), "player-owned");
        TestHarness.AssertFalse(TacticalBattleCoordinator.CountsTowardOperationStrength(3, false, false, false, true, false), "skirmishers");
        TestHarness.AssertFalse(TacticalBattleCoordinator.CountsTowardOperationStrength(5, false, false, false, true, false), "unittyp 5");
        TestHarness.AssertFalse(TacticalBattleCoordinator.CountsTowardOperationStrength(14, false, false, false, true, false), "group");
        TestHarness.AssertFalse(TacticalBattleCoordinator.CountsTowardOperationStrength(0, true, false, false, true, false), "dechained");
        TestHarness.AssertFalse(TacticalBattleCoordinator.CountsTowardOperationStrength(0, false, true, false, true, false), "routed");
        TestHarness.AssertFalse(TacticalBattleCoordinator.CountsTowardOperationStrength(0, false, false, false, false, false), "inactive");
    }

    private static void FeudInstanceNeverDrivesOrchestrator()
    {
        // AIBattle.SetSide (3408): 0 = enemy AI, 1 = player side, 2 = feud instance.
        TestHarness.AssertTrue(TacticalBattleCoordinator.DrivesOrchestrator(0), "enemy ai");
        TestHarness.AssertTrue(TacticalBattleCoordinator.DrivesOrchestrator(1), "player side (ai_vs_ai)");
        TestHarness.AssertFalse(TacticalBattleCoordinator.DrivesOrchestrator(2), "feud");
    }
}
