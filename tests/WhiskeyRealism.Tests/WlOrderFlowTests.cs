using System;
using WhiskeyRealism.Tactical.PlayerOrders;

using static TestHarness;

// W&L order flow (plan docs/superpowers/plans/2026-10-15-wl-order-flow.md): the pure
// re-admit policy, capture gate, courier delay and courier queue (W1), then the
// delivery pass and runtime rules (W2+). Vanilla anchors: AIBattle.CheckCurrentOrderUpdate
// 8233-8667, the dedupe at 8640-8646, build 14703111.
internal static partial class WlOrderFlowTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            // readmit policy
            ("wl order readmit predict vanilla matches 8643 clause by clause", PredictVanillaMatches8643ClauseByClause),
            ("wl order readmit type zero keeps vanilla carve out", Type0KeepsVanillaCarveOut),
            ("wl order readmit same type new place needs hold and 110 m", SameTypeNewPlaceNeedsHoldAnd110m),
            ("wl order readmit same type near place drops", SameTypeNearPlaceDrops),
            ("wl order readmit exactly 110 m is near", ExactlyOneHundredTenIsNear),
            ("wl order readmit retreat over engage at will admits with clear", RetreatOverEngageAtWillAdmitsWithClear),
            ("wl order readmit retreat over retreat drops", RetreatOverRetreatDrops),
            ("wl order readmit defend over attack only when held or defensive", DefendOverAttackOnlyWhenHeldOrDefensive),
            ("wl order readmit defend over attack waits for hold", DefendOverAttackWaitsForHold),
            ("wl order readmit engage at will blocks all but retreat and release", EngageAtWillBlocksAllButRetreatAndRelease),
            ("wl order readmit clauses e f g never admitted", ClausesEFGNeverAdmitted),
            ("wl order readmit top level support over support stays dropped", TopLevelSupportOverSupportStaysDropped),
            ("wl order readmit unseen order counts past hold", UnseenOrderCountsPastHold),
            ("wl order readmit reserve never readmitted", ReserveNeverReadmitted),
            ("wl order readmit retarget inside hold is deferred", RetargetInsideHoldIsDeferred),
            ("wl order readmit one shot defensive switch inside hold is deferred", OneShotDefensiveSwitchInsideHoldIsDeferred),
            ("wl order readmit campaign call is never consulted", CampaignCallIsNeverDropped),
            // delivery pass and capture decision (D3, D8, D8a)
            ("wl order pass buffers before delivering", PassBuffersBeforeDelivering),
            ("wl order pass superseded uses highest accepted issue", SupersededUsesHighestAcceptedIssue),
            ("wl order pass check order superseded unit gone cross scope release stale", CheckOrderSupersededUnitGoneCrossScopeReleaseStale),
            ("wl order pass release stale when slot no longer thirteen", ReleaseStaleWhenSlotNoLongerThirteen),
            ("wl order pass cross scope uses whiskey battle set", CrossScopeUsesWhiskeyBattleSet),
            ("wl order pass accepted needs session and type", AcceptedNeedsSessionAndType),
            ("wl order pass refreshed is not accepted", RefreshedIsNotAccepted),
            ("wl order pass relay took slot not restored", RelayTookSlotNotRestored),
            ("wl order pass refused after clear restores only null slot", RefusedAfterClearRestoresOnlyNullSlot),
            ("wl order pass refused with foreign slot requeues", RefusedWithForeignSlotRequeues),
            ("wl order pass one row per outcome", OneRowPerOutcome),
            ("wl order pass p62 bookkeeping per outcome", P62BookkeepingPerOutcome),
            ("wl order pass delivery restores slot when session unchanged", DeliveryRestoresSlotWhenSessionUnchanged),
            ("wl order pass advanced session is never restored over", AdvancedSessionIsNeverRestoredOver),
            ("wl order pass delivery restores slot and flag on vanilla throw", DeliveryRestoresSlotAndFlagOnVanillaThrow),
            ("wl order pass defer requeues at defer until", DeferRequeuesAtDeferUntil),
            ("wl order pass retreat derived when slot null", RetreatDerivedWhenSlotNull),
            ("wl order pass retreat derived after load", RetreatDerivedAfterLoad),
            ("wl order pass retreat not derived when pending or active", RetreatNotDerivedWhenPendingOrActive),
            ("wl order pass retreat not derived for cic", RetreatNotDerivedForCic),
            ("wl order pass release derived over engage at will only", ReleaseDerivedOverEngageAtWillOnly),
            ("wl order pass release not derived twice while pending", ReleaseNotDerivedTwiceWhilePending),
            ("wl order release latch starts quiet when both false", ReleaseLatchStartsQuietWhenBothFalse),
            ("wl order pass non paying delivery stamps top units", NonPayingDeliveryStampsTopUnits),
            ("wl order pass paying origins untouched", PayingOriginsUntouched),
            ("wl order pass command change clears queue", CommandChangeClearsQueue),
            ("wl order pass eod clears battle queue", EodClearsBattleQueue),
            ("wl order pass cross scope entry discarded", CrossScopeEntryDiscarded),
            ("wl order pass delivery writes nothing when slot changed since read", DeliveryWritesNothingWhenSlotChangedSinceRead),
            ("wl order capture delivering flag bypasses capture", DeliveringFlagBypassesCapture),
            ("wl order capture nested relay captured unless delivering", NestedRelayCapturedUnlessDelivering),
            ("wl order capture off when both flags off", CaptureOffWhenBothFlagsOff),
            ("wl order capture cic passes everything", CicPassesEverything),
            ("wl order capture mode not active passes battle calls", ModeNotActivePassesBattleCalls),
            ("wl order capture routed drops vanilla thirteen and fourteen", RoutedDropsVanillaThirteenAndFourteen),
            ("wl order capture couriers off delivers next pass", CouriersOffDeliversNextPass),
            ("wl order capture drops against slot unless pending may change it", CaptureDropsAgainstSlotUnlessPendingMayChangeIt),
            ("wl order age tracker stamps sessions", OrderAgeTrackerStampsSessions),
            // source pins
            ("wl order pin capture returns true on exception", CaptureReturnsTrueOnException),
            ("wl order pin delivery only on player side instance", DeliveryOnlyOnPlayerSideInstance),
            ("wl order pin no player unit writer", NoPlayerUnitWriter),
            ("wl order pin capture delivery and emitters are wired", WiringCaptureDeliveryAndEmitters),
            ("wl order pin non paying stamp writes only last combat zone count", NonPayingStampWritesOnlyLastCombatZoneCount),
            ("wl order pin release contact test is fog honest", ReleaseContactTestIsFogHonest),
            ("wl order pin p62 entry unit is parent not own command", P62EntryUnitIsParentNotOwnCommand),
            ("wl order pin p62 runs only on player side", P62RunsOnlyOnPlayerSide),
            ("wl order pin p62 capture drop records attempt", P62CaptureDropRecordsAttempt),
            ("wl order pin p62 accept recorded on delivery callback", AcceptRecordedOnDeliveryCallback),
            ("wl order pin p62 refusal recorded on delivery callback", RefusalRecordedOnDeliveryCallback),
            // release
            ("wl order release needs quiet for hysteresis", ReleaseNeedsQuietForHysteresis),
            ("wl order release needs vanilla trigger false", ReleaseNeedsVanillaTriggerFalse),
            ("wl order release quiet since NaN is not quiet", QuietSinceNaNIsNotQuiet),
            ("wl order routed share half", RoutedShareHalf),
            ("wl order routed zero strength is not routed", RoutedZeroStrengthIsNotRouted),
            ("wl order routed releases immediately", RoutedReleasesImmediately),
            // capture gate
            ("wl order gate mirrors eod alliance co chain type two and campaign returns", GateMirrorsEodAllianceCoChainTypeTwoCampaign),
            ("wl order gate rejects enemy side call without allocation", GateRejectsEnemySideCallWithoutAllocation),
            // courier delay
            ("wl order delay bugle leg is max of processing and recognition", BugleLegIsMaxOfProcessingAndRecognition),
            ("wl order delay courier leg is processing plus travel", CourierLegIsProcessingPlusTravel),
            ("wl order delay relay leg adds route time", RelayLegAddsRouteTime),
            ("wl order delay bugle relay leg has no route time", BugleRelayLegHasNoRouteTime),
            ("wl order delay order delays off is zero", OrderDelaysOffIsZero),
            ("wl order delay default prefs give twelve hundredths at 500 m", DefaultPrefsGiveTwelveHundredthsAt500m),
            ("wl order delay campaign floor is ten over thousand", CampaignFloorIsTenOverThousand),
            ("wl order delay campaign factors multiply issuer only", CampaignFactorsMultiplyIssuerOnly),
            // courier queue
            ("wl order queue same issuer type within hold dropped", SameIssuerTypeWithinHoldDropped),
            ("wl order queue release does not rate limit vanilla defend", ReleaseDoesNotRateLimitVanillaDefend),
            ("wl order queue retreat never rate limited", RetreatNeverRateLimited),
            ("wl order queue same point pending dropped", SamePointPendingDropped),
            ("wl order queue due order delivery", DueOrderDelivery),
            ("wl order queue equal due ties by enqueue order", EqualDueTiesByEnqueueOrder),
            ("wl order queue older than accepted is superseded", OlderThanAcceptedIsSuperseded),
            ("wl order queue retreat never superseded", RetreatNeverSuperseded),
            ("wl order queue requeue bypasses cadence and en route", RequeueBypassesCadenceAndEnRoute),
            ("wl order queue overflow discards oldest and reports", OverflowDiscardsOldestAndReports),
            ("wl order queue overflow never evicts retreat", OverflowNeverEvictsRetreat),
            ("wl order queue clear reports every entry", ClearReportsEveryEntry),
            ("wl order queue clear forgets history", ClearForgetsHistory),
            ("wl order queue campaign window is one day in years", CampaignWindowIsOneDayInYears),
        });
    }

    // ------------------------------------------------------------------ fixtures

    private const int Attack = 0, Deploy = 1, Support = 2, AttackEnemy = 3, Assault = 4, Reserve = 11, Defend = 12,
        EngageAtWill = 13, ClearHold = 14, Retreat = 15;

    private static ActiveOrderView Active(int type, float x = 0f, float z = 0f, double age = 1.0, float y = 0f,
        float rotation = 0f, bool held = false, bool defensive = false, bool flag2 = false, double now = 10.0)
        => new ActiveOrderView(true, type, x, y, z, rotation, age, now, held, defensive, flag2);

    private static OrderCandidateView Cand(int type, float x = 0f, float z = 0f, float y = 0f, float rotation = 0f,
        OrderOrigin origin = OrderOrigin.Vanilla, bool campaign = false)
        => new OrderCandidateView(type, x, y, z, rotation, origin, campaign);

    private static ReadmitAction Decide(in ActiveOrderView a, in OrderCandidateView c, out string reason)
        => PlayerOrderReadmitPolicy.Decide(in a, in c, out _, out reason);

    // Independent transliteration of vanilla's dedupe expression, decompile 8643, kept
    // apart from the production code so the two can disagree.
    private static bool Vanilla8643(int at, float ax, float ay, float az, float arot, bool flag2,
        int t, float x, float y, float z, float rot)
    {
        bool flag4 = t == 11 && at == 11;
        float dx = x - ax, dy = y - ay, dz = z - az;
        float d = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        float ang = ((arot - (rot + 180f)) % 360f + 540f) % 360f - 180f;
        return (at == t && (t > 0 || d < 110f) && !flag4)
            || (at == t && t == 11 && d < 110f && Math.Abs(ang) < 35f)
            || (t == 12 && (at == 0 || at == 1 || at == 2 || at == 3 || at == 4 || at == 5 || at == 13))
            || (t != 13 && at == 13)
            || (at != 12 && t == 14)
            || (flag2 && (at == 0 || at == 1 || at == 2 || at == 3 || at == 4) && t == 2)
            || at == 15;
    }

    // ------------------------------------------------------------- readmit policy

    private static void PredictVanillaMatches8643ClauseByClause()
    {
        // 3D offsets (dx, dy, dz): 50 m, 200 m and a 120 m case whose horizontal part is 72 m,
        // so a flat-XZ implementation disagrees with vanilla's Vector3.Distance.
        float[][] offsets = { new[] { 30f, 40f, 0f }, new[] { 0f, 120f, 160f }, new[] { 72f, 96f, 0f }, new[] { 110f, 0f, 0f }, new[] { 0f, 0f, 0f } };
        float[] candidateRotations = { -70f, -40f }; // vs the active area rotation 90: differences 20 and 50 degrees
        int checkedPairs = 0;
        for (int at = 0; at <= 16; at++)
        for (int t = 0; t <= 16; t++)
        foreach (float[] o in offsets)
        foreach (float rot in candidateRotations)
        foreach (bool flag2 in new[] { false, true })
        {
            ActiveOrderView a = Active(at, 1000f, 2000f, y: 5f, rotation: 90f, flag2: flag2);
            OrderCandidateView c = Cand(t, 1000f + o[0], 2000f + o[2], 5f + o[1], rot);
            bool expected = Vanilla8643(at, 1000f, 5f, 2000f, 90f, flag2, t, c.X, c.Y, c.Z, rot);
            bool actual = PlayerOrderReadmitPolicy.PredictVanilla(in a, in c);
            AssertEqual(expected, actual, "8643 at=" + at + " t=" + t + " d=(" + o[0] + "," + o[1] + "," + o[2] + ") rot=" + rot + " flag2=" + flag2);
            checkedPairs++;
        }
        AssertEqual(17 * 17 * 5 * 2 * 2, checkedPairs, "every pair exercised");

        // Hand-picked rows of the table.
        AssertTrue(PlayerOrderReadmitPolicy.PredictVanilla(Active(Support, 0, 0), Cand(Support, 5000, 0)), "(a) same type 1-4 drops at any distance");
        AssertFalse(PlayerOrderReadmitPolicy.PredictVanilla(Active(Attack, 0, 0), Cand(Attack, 500, 0)), "type 0 re-targets past 110 m");
        AssertTrue(PlayerOrderReadmitPolicy.PredictVanilla(Active(Defend, 0, 0), Cand(Defend, 50, 0)), "same type 12 drops");
        AssertTrue(PlayerOrderReadmitPolicy.PredictVanilla(Active(Assault), Cand(Defend)), "(c) 12 never replaces an attack");
        AssertTrue(PlayerOrderReadmitPolicy.PredictVanilla(Active(EngageAtWill), Cand(Retreat)), "(d) nothing but 13 replaces 13");
        AssertTrue(PlayerOrderReadmitPolicy.PredictVanilla(Active(Attack), Cand(ClearHold)), "(e) 14 replaces only 12");
        AssertFalse(PlayerOrderReadmitPolicy.PredictVanilla(Active(Defend), Cand(ClearHold)), "14 replaces 12");
        AssertTrue(PlayerOrderReadmitPolicy.PredictVanilla(Active(Retreat), Cand(Attack, 9000, 9000)), "(g) 15 is terminal");
        AssertTrue(PlayerOrderReadmitPolicy.PredictVanilla(Active(Assault, flag2: true), Cand(Support, 9000, 9000)), "(f) top level support over 0-4");
        AssertFalse(PlayerOrderReadmitPolicy.PredictVanilla(default(ActiveOrderView), Cand(Attack)), "no active order never drops");
        AssertFalse(PlayerOrderReadmitPolicy.PredictVanilla(Active(Attack), Cand(Attack, 0, 0, campaign: true)), "campaign calls never dedupe (8640)");
    }

    private static void Type0KeepsVanillaCarveOut()
    {
        // Vanilla drops a type 0 only within 110 m; the policy admits it past that, untouched.
        OrderCandidateView far = Cand(Attack, 300f, 0f);
        AssertEqual(ReadmitAction.Admit, Decide(Active(Attack, 0, 0, age: 9.0), far, out string reason), "type 0 far");
        AssertEqual("vanilla-accepts", reason, "type 0 far reason");
        // Within 110 m vanilla drops it and the policy does not override that.
        AssertEqual(ReadmitAction.Drop, Decide(Active(Attack, 0, 0, age: 9.0), Cand(Attack, 60f, 0f), out reason), "type 0 near");
        AssertEqual("vanilla-drops", reason, "type 0 near reason");
    }

    private static void SameTypeNewPlaceNeedsHoldAnd110m()
    {
        foreach (int type in new[] { Deploy, Support, AttackEnemy, Assault, Defend })
        {
            double hold = PlayerOrderReadmitPolicy.MinHoldBattleHours;
            ReadmitAction past = Decide(Active(type, 0, 0, age: hold), Cand(type, 400f, 0f), out string reason);
            AssertEqual(ReadmitAction.AdmitWithClear, past, "type " + type + " past hold at 400 m");
            AssertEqual("retarget-after-hold", reason, "type " + type + " reason");
            // 120 m is past the 110 m radius and older than the hold.
            AssertEqual(ReadmitAction.AdmitWithClear, Decide(Active(type, 0, 0, age: 1.0), Cand(type, 120f, 0f), out _), "type " + type + " at 120 m");
        }
        AssertEqual(0.25, PlayerOrderReadmitPolicy.MinHoldBattleHours, "hold is the executor courier cadence");
        AssertEqual(110f, PlayerOrderReadmitPolicy.ReadmitMeters, "radius is vanilla's");
    }

    private static void SameTypeNearPlaceDrops()
    {
        foreach (int type in new[] { Deploy, Support, AttackEnemy, Assault, Defend })
        {
            ReadmitAction a = Decide(Active(type, 0, 0, age: 5.0), Cand(type, 60f, 0f), out string reason);
            AssertEqual(ReadmitAction.Drop, a, "type " + type + " near");
            AssertEqual("retarget-near", reason, "type " + type + " near reason");
        }
        // Types that are not re-targetable (a campaign 7 onto a 7) are vanilla's drop.
        AssertEqual(ReadmitAction.Drop, Decide(Active(7), Cand(7, 900f, 0f), out string r), "7 onto 7");
        AssertEqual("vanilla-drops", r, "7 onto 7 reason");
    }

    private static void ExactlyOneHundredTenIsNear()
    {
        AssertEqual(ReadmitAction.Drop, Decide(Active(Support, 0, 0, age: 5.0), Cand(Support, 110f, 0f), out string reason), "110 m is near");
        AssertEqual("retarget-near", reason, "110 m reason");
        AssertEqual(ReadmitAction.AdmitWithClear, Decide(Active(Support, 0, 0, age: 5.0), Cand(Support, 110.5f, 0f), out _), "110.5 m is new");
        // Type 0 at exactly 110 m: vanilla's own test (< 110) accepts it.
        AssertEqual(ReadmitAction.Admit, Decide(Active(Attack, 0, 0, age: 5.0), Cand(Attack, 110f, 0f), out _), "type 0 at 110 m");
        // Distance is vanilla's 3D distance.
        AssertEqual(ReadmitAction.AdmitWithClear, Decide(Active(Support, 0, 0, age: 5.0, y: 0f), Cand(Support, 72f, 0f, y: 96f), out _), "3D 120 m");
    }

    private static void RetreatOverEngageAtWillAdmitsWithClear()
    {
        ReadmitAction action = Decide(Active(EngageAtWill, age: 0.0), Cand(Retreat, 900f, 900f), out string reason);
        AssertEqual(ReadmitAction.AdmitWithClear, action, "15 over 13 even in the first instant");
        AssertEqual("retreat-over-engage", reason, "reason");
        // A retreat over any order vanilla accepts is plain Admit.
        AssertEqual(ReadmitAction.Admit, Decide(Active(Attack), Cand(Retreat, 900f, 900f), out reason), "15 over 0");
        AssertEqual("vanilla-accepts", reason, "15 over 0 reason");
        AssertEqual(ReadmitAction.Admit, Decide(Active(Defend), Cand(Retreat), out _), "15 over 12");
    }

    private static void RetreatOverRetreatDrops()
    {
        AssertEqual(ReadmitAction.Drop, Decide(Active(Retreat, age: 9.0), Cand(Retreat, 5000f, 5000f), out string reason), "15 over 15");
        AssertEqual("retreat-terminal", reason, "reason");
        // Nothing is re-admitted over a retreat.
        for (int t = 0; t <= 16; t++)
            AssertEqual(ReadmitAction.Drop, Decide(Active(Retreat, age: 99.0), Cand(t, 9000f, 9000f), out _), "over 15 type " + t);
    }

    private static void DefendOverAttackOnlyWhenHeldOrDefensive()
    {
        foreach (int active in new[] { Attack, Deploy, Support, AttackEnemy, Assault, 5 })
        {
            AssertEqual(ReadmitAction.Drop, Decide(Active(active, age: 5.0), Cand(Defend), out string none), "neither held nor defensive over " + active);
            AssertEqual("defend-over-attack", none, "neither reason");
            AssertEqual(ReadmitAction.AdmitWithClear, Decide(Active(active, age: 5.0, held: true), Cand(Defend), out string held), "objective held over " + active);
            AssertEqual("defend-objective-held", held, "held reason");
            AssertEqual(ReadmitAction.AdmitWithClear, Decide(Active(active, age: 5.0, defensive: true), Cand(Defend), out string def), "army defensive over " + active);
            AssertEqual("defend-army-defensive", def, "defensive reason");
        }
        // Both together: the objective reason wins.
        Decide(Active(Attack, age: 5.0, held: true, defensive: true), Cand(Defend), out string both);
        AssertEqual("defend-objective-held", both, "both together");
    }

    private static void DefendOverAttackWaitsForHold()
    {
        double hold = PlayerOrderReadmitPolicy.MinHoldBattleHours;
        ReadmitAction inside = PlayerOrderReadmitPolicy.Decide(Active(Assault, age: 0.1, held: true, now: 20.0), Cand(Defend), out double until, out string reason);
        AssertEqual(ReadmitAction.Defer, inside, "inside the hold");
        AssertEqual("defend-deferred", reason, "reason");
        AssertTrue(Math.Abs(until - (20.0 - 0.1 + hold)) < 1e-9, "deferUntil = ActiveSince + MinHold, got " + until);
        AssertEqual(ReadmitAction.AdmitWithClear, Decide(Active(Assault, age: hold, held: true), Cand(Defend), out _), "at the hold");
    }

    private static void EngageAtWillBlocksAllButRetreatAndRelease()
    {
        for (int t = 0; t <= 16; t++)
        {
            if (t == Retreat || t == EngageAtWill) continue;
            ReadmitAction action = Decide(Active(EngageAtWill, age: 99.0), Cand(t, 3000f, 3000f), out string reason);
            AssertEqual(ReadmitAction.Drop, action, "type " + t + " over 13");
            AssertTrue(reason == "engage-at-will" || reason == "clause-e", "reason for " + t + ": " + reason);
        }
        AssertEqual(ReadmitAction.Drop, Decide(Active(EngageAtWill, age: 99.0), Cand(EngageAtWill, 3000f, 3000f), out string same), "13 over 13");
        AssertEqual("engage-at-will", same, "13 over 13 reason");
        // The release (a type 12 from D5) is the one other exception.
        ReadmitAction release = Decide(Active(EngageAtWill, age: 0.0), Cand(Defend, origin: OrderOrigin.Release), out string rel);
        AssertEqual(ReadmitAction.AdmitWithClear, release, "release over 13");
        AssertEqual("release", rel, "release reason");
        // A type 12 from anywhere else is not the release.
        AssertEqual(ReadmitAction.Drop, Decide(Active(EngageAtWill, age: 0.0), Cand(Defend, origin: OrderOrigin.Vanilla), out _), "vanilla 12 over 13");
        AssertEqual(ReadmitAction.Drop, Decide(Active(EngageAtWill, age: 0.0), Cand(Defend, origin: OrderOrigin.FollowUp), out _), "follow-up 12 over 13");
    }

    private static void ClausesEFGNeverAdmitted()
    {
        // (e) 14 replaces only 12.
        for (int at = 0; at <= 16; at++)
        {
            if (at == 12) continue;
            ReadmitAction e = Decide(Active(at, age: 99.0), Cand(ClearHold, 5000f, 5000f), out string reason);
            AssertTrue(e != ReadmitAction.AdmitWithClear && e != ReadmitAction.Defer, "14 over " + at + " is not re-admitted");
            if (at != 15 && at != 13) { AssertEqual(ReadmitAction.Drop, e, "14 over " + at); AssertEqual("clause-e", reason, "14 over " + at + " reason"); }
        }
        // (f) a top level player's support never replaces 0-4, at any distance or age.
        foreach (int at in new[] { Attack, Deploy, Support, AttackEnemy, Assault })
        {
            ReadmitAction f = Decide(Active(at, 0, 0, age: 99.0, flag2: true), Cand(Support, 5000f, 5000f), out string reason);
            AssertEqual(ReadmitAction.Drop, f, "(f) over " + at);
            AssertEqual("clause-f", reason, "(f) reason over " + at);
        }
        // (g) nothing replaces 15.
        AssertEqual(ReadmitAction.Drop, Decide(Active(Retreat, age: 99.0), Cand(Defend, origin: OrderOrigin.Release), out string g), "release over 15");
        AssertTrue(g == "clause-g" || g == "retreat-terminal", "(g) reason " + g);
    }

    private static void TopLevelSupportOverSupportStaysDropped()
    {
        // Rule 2 would admit a support at a new place after the hold; clause (f) drops it first.
        AssertEqual(ReadmitAction.Drop, Decide(Active(Support, 0, 0, age: 99.0, flag2: true), Cand(Support, 4000f, 0f), out string reason), "top level 2 over 2");
        AssertEqual("clause-f", reason, "reason");
        // The same call for a subordinate (flag2 false) is re-admitted.
        AssertEqual(ReadmitAction.AdmitWithClear, Decide(Active(Support, 0, 0, age: 99.0, flag2: false), Cand(Support, 4000f, 0f), out _), "subordinate 2 over 2");
    }

    private static void UnseenOrderCountsPastHold()
    {
        AssertEqual(ReadmitAction.AdmitWithClear, Decide(Active(Support, 0, 0, age: double.PositiveInfinity), Cand(Support, 4000f, 0f), out _), "infinite age");
        AssertEqual(ReadmitAction.AdmitWithClear, Decide(Active(Support, 0, 0, age: double.NaN), Cand(Support, 4000f, 0f), out _), "NaN age is unseen");
        AssertEqual(ReadmitAction.AdmitWithClear, Decide(Active(Assault, age: double.PositiveInfinity, held: true), Cand(Defend), out _), "infinite age defend");
    }

    private static void ReserveNeverReadmitted()
    {
        // Type 11 onto an active 11 is vanilla's silent in-place refresh (flag4); never emptied.
        ReadmitAction onto11 = Decide(Active(Reserve, 0, 0, age: 99.0, rotation: 90f), Cand(Reserve, 20f, 0f, rotation: -70f), out string reason);
        AssertEqual(ReadmitAction.Drop, onto11, "11 near and aligned");
        AssertEqual("reserve-refresh", reason, "reason");
        // An 11 vanilla accepts (far, or a turned area) is plain Admit.
        AssertEqual(ReadmitAction.Admit, Decide(Active(Reserve, 0, 0, age: 99.0), Cand(Reserve, 4000f, 0f), out _), "11 far is vanilla's");
        // An 11 into a null slot is admitted (finding 15).
        AssertEqual(ReadmitAction.Admit, Decide(default(ActiveOrderView), Cand(Reserve, 100f, 0f), out _), "11 into empty slot");
        // An 11 onto another type is vanilla's.
        AssertEqual(ReadmitAction.Admit, Decide(Active(Attack, age: 99.0), Cand(Reserve, 0f, 0f), out _), "11 onto 0");
    }

    private static void RetargetInsideHoldIsDeferred()
    {
        foreach (int type in new[] { Deploy, Support, AttackEnemy, Assault, Defend })
        {
            ReadmitAction action = PlayerOrderReadmitPolicy.Decide(Active(type, 0, 0, age: 0.05, now: 7.0), Cand(type, 600f, 0f), out double until, out string reason);
            AssertEqual(ReadmitAction.Defer, action, "type " + type + " inside the hold is deferred, never dropped");
            AssertEqual("retarget-deferred", reason, "reason");
            AssertTrue(Math.Abs(until - (7.0 - 0.05 + PlayerOrderReadmitPolicy.MinHoldBattleHours)) < 1e-9, "deferUntil, got " + until);
        }
    }

    private static void OneShotDefensiveSwitchInsideHoldIsDeferred()
    {
        // CheckGlobalAIStrategy's type 12 (6463) is a one-shot: inside the hold it must wait, not vanish.
        ReadmitAction action = Decide(Active(Assault, age: 0.02, defensive: true), Cand(Defend), out string reason);
        AssertEqual(ReadmitAction.Defer, action, "defensive switch inside the hold");
        AssertEqual("defend-deferred", reason, "reason");
        // 12 onto 12 at a new place inside the hold is deferred too.
        AssertEqual(ReadmitAction.Defer, Decide(Active(Defend, 0, 0, age: 0.02), Cand(Defend, 900f, 0f), out _), "12 onto 12");
    }

    private static void CampaignCallIsNeverDropped()
    {
        AssertEqual(ReadmitAction.Admit, Decide(Active(8, 0, 0, age: 0.0), Cand(8, 0f, 0f, campaign: true), out string reason), "campaign call");
        AssertEqual("vanilla-accepts", reason, "reason");
    }

    // ------------------------------------------------------------------- release

    private static void ReleaseNeedsQuietForHysteresis()
    {
        double h = PlayerOrderReadmitPolicy.ReleaseHysteresisBattleHours;
        AssertEqual(0.1, h, "hysteresis");
        AssertFalse(PlayerOrderReadmitPolicy.ReleaseDue(false, false, quietSinceHours: 5.0, nowHours: 5.05, routed: false), "0.05 h quiet");
        AssertTrue(PlayerOrderReadmitPolicy.ReleaseDue(false, false, quietSinceHours: 5.0, nowHours: 5.0 + h, routed: false), "exactly the hysteresis");
        AssertTrue(PlayerOrderReadmitPolicy.ReleaseDue(false, false, quietSinceHours: 5.0, nowHours: 9.0, routed: false), "long quiet");
        AssertFalse(PlayerOrderReadmitPolicy.ReleaseDue(true, false, quietSinceHours: 5.0, nowHours: 9.0, routed: false), "visible enemy in range");
    }

    private static void ReleaseNeedsVanillaTriggerFalse()
    {
        AssertFalse(PlayerOrderReadmitPolicy.ReleaseDue(false, vanillaContactTrigger: true, quietSinceHours: 1.0, nowHours: 9.0, routed: false),
            "vanilla's 13 trigger still true: it would re-issue 13 within 30 frames");
        AssertTrue(PlayerOrderReadmitPolicy.ReleaseDue(false, vanillaContactTrigger: false, quietSinceHours: 1.0, nowHours: 9.0, routed: false), "trigger false");
    }

    private static void QuietSinceNaNIsNotQuiet()
    {
        AssertFalse(PlayerOrderReadmitPolicy.ReleaseDue(false, false, double.NaN, 9.0, false), "NaN");
        AssertFalse(PlayerOrderReadmitPolicy.ReleaseDue(false, false, double.PositiveInfinity, 9.0, false), "+inf");
    }

    private static void RoutedShareHalf()
    {
        var strength = new[] { 100f, 100f, 100f, 100f };
        AssertTrue(PlayerOrderReadmitPolicy.Routed(strength, new[] { true, true, false, false }, 4), "exactly half");
        AssertFalse(PlayerOrderReadmitPolicy.Routed(strength, new[] { true, false, false, false }, 4), "a quarter");
        AssertTrue(PlayerOrderReadmitPolicy.Routed(new[] { 300f, 100f }, new[] { true, false }, 2), "weighted by strength: 75 %");
        AssertFalse(PlayerOrderReadmitPolicy.Routed(new[] { 100f, 300f }, new[] { true, false }, 2), "weighted by strength: 25 %");
        AssertEqual(0.5f, PlayerOrderReadmitPolicy.RoutedShare, "share");
        // count is bounded by both spans.
        AssertTrue(PlayerOrderReadmitPolicy.Routed(strength, new[] { true, true }, 4), "count clamps to the shorter span");
    }

    private static void RoutedZeroStrengthIsNotRouted()
    {
        AssertFalse(PlayerOrderReadmitPolicy.Routed(new[] { 0f, 0f }, new[] { true, true }, 2), "zero total strength");
        AssertFalse(PlayerOrderReadmitPolicy.Routed(new float[0], new bool[0], 0), "empty");
        AssertFalse(PlayerOrderReadmitPolicy.Routed(new[] { float.NaN, 50f }, new[] { true, false }, 2), "NaN strength is ignored, not a rout");
    }

    private static void RoutedReleasesImmediately()
    {
        AssertTrue(PlayerOrderReadmitPolicy.ReleaseDue(true, true, double.NaN, 9.0, routed: true), "routed overrides contact and hysteresis");
        AssertTrue(PlayerOrderReadmitPolicy.ReleaseDue(false, false, double.NaN, 9.0, routed: true), "routed with no quiet clock");
    }
}
