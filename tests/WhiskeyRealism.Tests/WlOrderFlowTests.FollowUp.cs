using System;
using System.Text.RegularExpressions;
using WhiskeyRealism.Tactical.PlayerOrders;

using static TestHarness;

// The follow-up (plan D17 (b)): after a reached attack order the superior says "hold the position you
// reached". A type 12 from the follow-up replaces a reached 1-4 order once the hold has passed and
// nothing newer waits; it pays no prestige and starts no compliance sample (D21).
internal static partial class WlOrderFlowTests
{
    private static CourierEntry FollowUpEntry() => QEntry(issuer: 5, type: 12, origin: OrderOrigin.FollowUp, x: 2000f, issuedAt: 10.0, dueAt: 10.0);

    private static void FollowUpOverReachedAttackAdmitsWithClear()
    {
        foreach (int attack in new[] { 1, 2, 3, 4 })
        {
            ReadmitAction action = Decide(Active(attack, 2000f, 0f, age: 0.0), Cand(12, 2000f, 0f, origin: OrderOrigin.FollowUp), out string reason);
            AssertEqual(ReadmitAction.AdmitWithClear, action, "follow-up over type " + attack);
            AssertEqual("follow-up", reason, "follow-up reason over type " + attack);
        }
        // Vanilla's own 12 over the same orders stays dropped (clause c).
        AssertEqual(ReadmitAction.Drop, Decide(Active(2, 2000f, 0f, age: 99.0), Cand(12, 2000f, 0f, origin: OrderOrigin.Vanilla), out _), "vanilla 12 over 2");
    }

    private static void FollowUpOnlyOverAttackTypes1To4()
    {
        foreach (int slot in new[] { 0, 5, 11, 12, 13, 14, 15 })
        {
            ReadmitAction action = Decide(Active(slot, 2000f, 0f, age: 99.0), Cand(12, 2000f, 0f, origin: OrderOrigin.FollowUp), out string reason);
            AssertTrue(reason != "follow-up", "the exception does not apply over slot type " + slot + " (" + reason + ")");
        }
        AssertEqual(ReadmitAction.Admit, Decide(default(ActiveOrderView), Cand(12, origin: OrderOrigin.FollowUp), out _), "an empty slot takes it as vanilla does");
    }

    private static void FollowUpAfterArrivalAndHold()
    {
        var host = new FakeHost { Slot = new FakeOrder { Type = 3, X = 2000f }, FollowUp = FollowUpEntry() };
        var flow = NewFlow(host);
        flow.RunPass();
        AssertEqual("12", string.Join(",", host.CalledTypes), "the hold replaced the reached order");
        AssertEqual(12, host.Slot.Type, "on the panel");
        AssertTrue(host.Rows.Contains("12:deliver:accepted"), RowsOf(host));
        AssertEqual(1, host.NonPayingMarks, "a follow-up pays no prestige (D21)");
        AssertEqual(1, host.FollowUpBuilds, "the host was asked once");
    }

    private static void FollowUpAskedOnlyOverAnAttackSlotAndOnePending()
    {
        foreach (int slot in new[] { -1, 0, 5, 11, 12, 13, 14, 15 })
        {
            var other = new FakeHost { Slot = slot < 0 ? null : new FakeOrder { Type = slot }, FollowUp = FollowUpEntry() };
            NewFlow(other).RunPass();
            AssertEqual(0, other.FollowUpBuilds, "no follow-up is asked for over slot type " + slot);
        }
        foreach (int slot in new[] { 1, 2, 3, 4 })
        {
            var host = new FakeHost { Slot = new FakeOrder { Type = slot } };
            NewFlow(host).RunPass();
            AssertEqual(1, host.FollowUpBuilds, "asked over slot type " + slot);
        }
    }

    private static void NoFollowUpWhenNewerPending()
    {
        var host = new FakeHost { Slot = new FakeOrder { Type = 2 }, FollowUp = FollowUpEntry(), Now = 10.0 };
        var flow = NewFlow(host);
        // A newer order is already riding (due later): the follow-up would only talk over it.
        CourierEntry newer = QEntry(issuer: 7, type: 3, x: 5000f, issuedAt: 10.0, dueAt: 12.0);
        flow.Queue.TryEnqueue(in newer, 0.0, out _);
        flow.RunPass();
        AssertEqual(0, host.FollowUpBuilds, "the host is not asked next to a pending order");
        AssertEqual(1, flow.Queue.Count, "the pending order waits");
    }

    private static void FollowUpStaleWhenTheSlotMovedOn()
    {
        var host = new FakeHost { Slot = new FakeOrder { Type = 2 }, FollowUpDue = false };
        var flow = NewFlow(host);
        CourierEntry entry = FollowUpEntry();
        flow.Queue.TryEnqueue(in entry, 0.0, out _);
        flow.RunPass();
        AssertEqual(0, host.Calls, "nothing is delivered");
        AssertTrue(host.Rows.Contains("12:discard:follow-up-stale"), "reported stale: " + RowsOf(host));
        AssertEqual(2, host.Slot.Type, "the slot is untouched");
    }

    private static void FollowUpWiredToTheReachedFlagAndTheHold()
    {
        string flow = Code(FlowRuntime);
        string body = BodyOf(flow, "public bool TryBuildFollowUp(");
        AssertTrue(body.Contains("firstunitreached"), "only a reached order is followed up");
        AssertTrue(body.Contains("PlayerOrderReadmitPolicy.MinHoldBattleHours"), "after the hold");
        AssertTrue(body.Contains("FollowUpOn()") && body.Contains("ReadmitOn()"), "both switches");
        AssertTrue(body.Contains("OrderOrigin.FollowUp"), "origin follow-up");
        AssertTrue(body.Contains("OrderIntentKind.FollowUp"), "its text is the follow-up's");
        AssertTrue(Code("src/WhiskeyRealism/Tactical/PlayerOrders/WlOrderFlow.cs").Contains("DeriveFollowUp();"), "the pass derives it");
        AssertTrue(Code("src/WhiskeyRealism/Plugin.cs").Contains("\"Enable W&L Order Follow-Up\""), "the flag exists");
    }

    private static void PrestigeDeltaRowIsSampledPerSession()
    {
        string flow = Code(FlowRuntime);
        string sample = BodyOf(flow, "private static void SamplePrestige(");
        AssertTrue(sample.Contains("DLC_WL.prestige") && !Regex.IsMatch(sample, @"DLC_WL\.prestige\s*[-+*/]?=[^=]"), "prestige is only read");
        AssertTrue(sample.Contains("WlOrderFlow.IsNonPaying("), "an automatic hold always gets its row, the control for the criterion");
        AssertTrue(BodyOf(flow, "internal static void OnDeliveryPass(").Contains("SamplePrestige();"), "sampled on every delivery pass");
        AssertTrue(BodyOf(flow, "private static void EmitPrestigeRow(").Contains("\"prestige-delta\""), "the row the criterion reads");
        AssertTrue(BodyOf(flow, "public void Accepted(").Contains("_originOfSession = e.Origin;"), "the session's origin comes from the delivery that began it");
    }

    // The verifier's loop (verify r1, F1): a reached 4, the follow-up 12, then AssaultMove re-issuing the same
    // 4 every time its ancestor is idle. Vanilla's same-type dedupe no longer sees the reached order behind
    // the hold; the flow remembers it.
    private static void FollowUpDoesNotReopenTheReplacedOrder()
    {
        var host = new FakeHost { Slot = new FakeOrder { Type = 4, X = 2000f }, FollowUp = FollowUpEntry(), Now = 10.0, Age = 5.0 };
        var flow = NewFlow(host);
        flow.RunPass();
        AssertEqual(12, host.Slot.Type, "the follow-up is the order on the panel");
        AssertEqual(1, host.AcceptedTypes.Count, "one order delivered so far");
        int sessionAfterFollowUp = host.SessionNo;
        int ordersDelivered = 0;
        for (int step = 0; step < 16; step++)   // four battle hours, AssaultMove re-sends every 0.25 h
        {
            host.Now += 0.25;
            host.Age = 0.25 * (step + 1);
            CaptureInput c = Capturable(type: 4, x: 2000f + (step % 2) * 60f);   // same point, and 60 m off
            c.Hold = flow.Hold();
            ActiveOrderView active = host.ReadActive();
            CaptureVerdict v = WlOrderFlow.DecideCapture(in c, in active, out _, out string reason);
            AssertEqual(CaptureVerdict.Drop, v, "re-send " + step + " is dropped at capture");
            AssertEqual(WlOrderFlow.FollowUpHolds, reason, "re-send " + step + " reason");
            // And a copy that was queued before the follow-up arrived is voided at delivery.
            Put(flow, 4, host.Now, host.Now, unit: 1, x: 2000f);
            flow.RunPass();
            ordersDelivered += host.AcceptedTypes.Count - 1;
        }
        AssertEqual(sessionAfterFollowUp, host.SessionNo, "no new order session was opened");
        AssertEqual(0, ordersDelivered, "no order was admitted behind the hold");
        AssertEqual(1, host.NonPayingMarks, "and the only prestige-free session is the follow-up's");
        AssertTrue(ordersDelivered <= 6 * 4, "within the six-per-battle-hour guardrail");
        AssertEqual(12, host.Slot.Type, "the follow-up still stands");
    }

    private static void FollowUpHoldLeavesOtherOrdersToTheNormalRules()
    {
        var host = new FakeHost { Slot = new FakeOrder { Type = 4, X = 2000f }, FollowUp = FollowUpEntry(), Now = 10.0, Age = 5.0 };
        var flow = NewFlow(host);
        flow.RunPass();
        FollowUpHoldView hold = flow.Hold();
        AssertTrue(hold.Active && hold.Type == 4, "the hold remembers the replaced type");
        AssertTrue(hold.Blocks(4, 2000f, 0f), "same type, same point");
        AssertTrue(hold.Blocks(4, 2110f, 0f), "exactly 110 m is inside");
        AssertFalse(hold.Blocks(4, 2111f, 0f), "beyond 110 m follows the normal rules");
        AssertFalse(hold.Blocks(3, 2000f, 0f), "another type follows the normal rules");
        AssertFalse(hold.Blocks(2, 2000f, 0f), "a support order follows the normal rules");
        host.Age = 99.0;
        CaptureInput far = Capturable(type: 4, x: 6000f);
        far.Hold = hold;
        ActiveOrderView active = host.ReadActive();
        AssertEqual(CaptureVerdict.Queue, WlOrderFlow.DecideCapture(in far, in active, out _, out string reason), "a new place is re-admitted by the policy: " + reason);
    }

    private static void FollowUpHoldEndsWithTheFollowUp()
    {
        var host = new FakeHost { Slot = new FakeOrder { Type = 4, X = 2000f }, FollowUp = FollowUpEntry(), Now = 10.0, Age = 5.0 };
        var flow = NewFlow(host);
        AssertFalse(flow.Hold().Active, "nothing is held before the follow-up");
        flow.RunPass();
        AssertTrue(flow.Hold().Active, "held while the follow-up is on the panel");
        host.Slot = new FakeOrder { Type = 3, X = 9000f };   // another order shown (vanilla's or Whiskey's)
        host.SessionNo++;
        AssertFalse(flow.Hold().Active, "a new order ends the memory");
        host.Slot = new FakeOrder { Type = 12 };            // a 12 again, but not the follow-up's session
        AssertFalse(flow.Hold().Active, "a later 12 is not the follow-up");
        var cleared = new FakeHost { Slot = new FakeOrder { Type = 4, X = 2000f }, FollowUp = FollowUpEntry(), Now = 10.0, Age = 5.0 };
        var flow3 = NewFlow(cleared);
        flow3.RunPass();
        AssertTrue(flow3.Hold().Active, "held");
        cleared.Slot = null;                                // the slot emptied without a new session (end of day, a scene load)
        AssertFalse(flow3.Hold().Active, "an empty slot ends the memory");
        var again = new FakeHost { Slot = new FakeOrder { Type = 4, X = 2000f }, FollowUp = FollowUpEntry(), Now = 10.0, Age = 5.0 };
        var flow2 = NewFlow(again);
        flow2.RunPass();
        flow2.ResetHold();
        AssertFalse(flow2.Hold().Active, "a battle reset clears it");
    }

    private static void FollowUpHoldVoidsAQueuedRepeat()
    {
        var host = new FakeHost { Slot = new FakeOrder { Type = 4, X = 2000f }, FollowUp = FollowUpEntry(), Now = 10.0, Age = 5.0 };
        var flow = NewFlow(host);
        flow.RunPass();
        host.Now += 1.0;
        Put(flow, 4, host.Now, host.Now, unit: 3, x: 2050f);
        host.Rows.Clear();
        flow.RunPass();
        AssertTrue(host.Rows.Contains("4:discard:follow-up-holds"), "reported: " + RowsOf(host));
        AssertEqual(1, host.AcceptedTypes.Count, "only the follow-up was delivered");
    }

    // F2: a Whiskey emitter's type 11 onto an active 11 is vanilla's in-place refresh of the reserve zone.
    private static void ReserveRefreshFromAnEmitterGoesToVanilla()
    {
        string flow = Code(FlowRuntime);
        string body = BodyOf(flow, "private static bool EnqueueCall(");
        AssertTrue(Regex.IsMatch(body, @"verdict == CaptureVerdict\.Pass && call\.Type == 11 && DLC_WL\.givenorder != null && DLC_WL\.givenorder\.type == 11"), "a passed reserve refresh");
        AssertTrue(Regex.IsMatch(body, @"givenorder\.type == 11\)\s*CallVanillaDirect\(in call\);"), "is given to vanilla by that very branch");
        CaptureInput refresh = Capturable(type: 11);
        ActiveOrderView active11 = new ActiveOrderView(true, 11, 0, 0, 0, 0f, 99.0, 10.0, false, false, false);
        AssertEqual(CaptureVerdict.Pass, WlOrderFlow.DecideCapture(in refresh, in active11, out _, out string reason), "the capture passes it");
        AssertEqual("reserve-refresh", reason, "reason");
    }

    // F3 (V21): vanilla's 15 comes from CheckRemovalOfOrders only while the instance's macroai == 3 (6788).
    private static void DerivedRetreatNeedsMacroAiThree()
    {
        string flow = Code(FlowRuntime);
        string body = BodyOf(flow, "public bool TryBuildRetreat(");
        AssertTrue(body.Contains("_playerBattle.macroai != 3) return false;"), "the army must be retreating");
    }
}
