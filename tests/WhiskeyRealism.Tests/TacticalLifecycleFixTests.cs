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
}
