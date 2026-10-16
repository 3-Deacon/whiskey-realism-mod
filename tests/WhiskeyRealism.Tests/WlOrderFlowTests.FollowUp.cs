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
}
