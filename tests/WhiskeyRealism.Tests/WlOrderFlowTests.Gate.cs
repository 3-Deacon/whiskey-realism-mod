using System;
using WhiskeyRealism.Tactical.PlayerOrders;

using static TestHarness;

// The capture gate mirrors AIBattle.CheckCurrentOrderUpdate's returns before the dedupe
// (decompile 8251-8369): which calls can reach the player's panel at all.
internal static partial class WlOrderFlowTests
{
    // A battle call from an AI ancestor of a subordinate player: the one shape the order flow
    // exists for. Each case flips one fact.
    private static CaptureFacts AncestorCall() => new CaptureFacts
    {
        WlActive = true,
        HasCurrentCommand = true,
        Type = 0,
        UnitIsPlayerAlliance = true,
        ParentIsUnderUnit = true,      // flag: the player's parent unit is a child of the call's unit
        SameGroupAiObject = true,
    };

    private static void GateMirrorsEodAllianceCoChainTypeTwoCampaign()
    {
        AssertTrue(WlOrderCaptureGate.ReachesDedupe(AncestorCall()), "an AI ancestor's order reaches a subordinate");

        // 8251: W&L inactive, or no current command.
        var c = AncestorCall(); c.WlActive = false;
        AssertFalse(WlOrderCaptureGate.ReachesDedupe(c), "W&L inactive");
        c = AncestorCall(); c.HasCurrentCommand = false;
        AssertFalse(WlOrderCaptureGate.ReachesDedupe(c), "no current command");

        // 8259: end-of-day cycle, a null unit, a unit with no transform parent, another alliance.
        c = AncestorCall(); c.EodCycle = true;
        AssertFalse(WlOrderCaptureGate.ReachesDedupe(c), "eodcycle > 0");
        c = AncestorCall(); c.UnitMissing = true;
        AssertFalse(WlOrderCaptureGate.ReachesDedupe(c), "null unit or no parent");
        c = AncestorCall(); c.UnitIsPlayerAlliance = false;
        AssertFalse(WlOrderCaptureGate.ReachesDedupe(c), "other alliance");

        // 8329: a battle call returns when the player is the battle commanding officer.
        c = AncestorCall(); c.PlayerIsBattleCo = true;
        AssertFalse(WlOrderCaptureGate.ReachesDedupe(c), "player is the battle CO");

        // 8337: a unit that is not the player's campaign group's chain, or not an ancestor with
        // the parent's groupaiobject.
        c = AncestorCall(); c.SameGroupAiObject = false;
        AssertFalse(WlOrderCaptureGate.ReachesDedupe(c), "ancestor with another groupaiobject");
        c = AncestorCall(); c.ParentIsUnderUnit = false;
        AssertFalse(WlOrderCaptureGate.ReachesDedupe(c), "a unit that is not an ancestor of the player's parent");

        // A subordinate's own command (parent is not under it, not the campaign group): vanilla
        // returns, so the player's own command is never a chain target. This is shipped defect
        // BUG-WL-023: #62 passed exactly this unit and never reached a subordinate.
        c = new CaptureFacts { WlActive = true, HasCurrentCommand = true, UnitIsPlayerAlliance = true, UnitIsPlayerCommand = true, UnitIsUnderCommander = true, ParentIsUnderUnit = false };
        AssertFalse(WlOrderCaptureGate.ReachesDedupe(c), "unit == a subordinate's own command");

        // A top-level player (flag2): the player's own command is a target (8337 passes).
        c = new CaptureFacts { WlActive = true, HasCurrentCommand = true, UnitIsPlayerAlliance = true, CampaignGroupIsOwnCommand = true, UnitIsPlayerCommand = true, UnitIsUnderCommander = true, ParentIsUnderUnit = true, Type = 12 };
        AssertTrue(WlOrderCaptureGate.ReachesDedupe(c), "top level player's own command");

        // 8362-8365: a type 2 aimed at the player's own subtree is dropped by vanilla.
        c = AncestorCall(); c.Type = 2; c.UnitInPlayerSubtree = true;
        AssertFalse(WlOrderCaptureGate.ReachesDedupe(c), "type 2 at the player's own subtree");
        c = AncestorCall(); c.Type = 2;
        AssertTrue(WlOrderCaptureGate.ReachesDedupe(c), "type 2 at an ancestor");
        c = AncestorCall(); c.Type = 0; c.UnitInPlayerSubtree = true;
        AssertTrue(WlOrderCaptureGate.ReachesDedupe(c), "the subtree test is type 2 only");

        // 8366: a campaign call returns for a CIC, a non-top-level player, a parent that is not
        // under the unit, or a top-level player whose unit is not player owned.
        var camp = new CaptureFacts { WlActive = true, HasCurrentCommand = true, UnitIsPlayerAlliance = true, CampaignCall = true, CampaignGroupIsOwnCommand = true, ParentIsUnderUnit = true, UnitIsUnderCommander = true };
        AssertTrue(WlOrderCaptureGate.ReachesDedupe(camp), "campaign call to a top-level player's group");
        c = camp; c.PlayerIsCic = true;
        AssertFalse(WlOrderCaptureGate.ReachesDedupe(c), "campaign: CIC");
        c = camp; c.CampaignGroupIsOwnCommand = false;
        AssertFalse(WlOrderCaptureGate.ReachesDedupe(c), "campaign: not top level");
        c = camp; c.ParentIsUnderUnit = false;
        AssertFalse(WlOrderCaptureGate.ReachesDedupe(c), "campaign: parent not under the unit");
        c = camp; c.UnitIsUnderCommander = false;
        AssertFalse(WlOrderCaptureGate.ReachesDedupe(c), "campaign: unit not player owned");
        // The battle-only returns do not apply to a campaign call.
        c = camp; c.PlayerIsBattleCo = true;
        AssertTrue(WlOrderCaptureGate.ReachesDedupe(c), "campaign calls skip the battle CO test");
    }

    private static void GateRejectsEnemySideCallWithoutAllocation()
    {
        CaptureFacts enemy = AncestorCall(); enemy.UnitIsPlayerAlliance = false;
        CaptureFacts nonChain = AncestorCall(); nonChain.ParentIsUnderUnit = false; nonChain.SameGroupAiObject = false;
        CaptureFacts chain = AncestorCall();
        AssertTrue(WlOrderCaptureGate.CheapReject(enemy), "an enemy-side call is rejected by the cheap gates");
        AssertFalse(WlOrderCaptureGate.CheapReject(chain), "a player-side call needs the chain test");

        WlOrderCaptureGate.ReachesDedupe(chain);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int hits = 0;
        for (int i = 0; i < 10000; i++)
        {
            if (WlOrderCaptureGate.CheapReject(enemy)) hits++;
            if (!WlOrderCaptureGate.ReachesDedupe(enemy)) hits++;
            if (!WlOrderCaptureGate.ReachesDedupe(nonChain)) hits++;
            if (WlOrderCaptureGate.ReachesDedupe(chain)) hits++;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        AssertEqual(40000, hits, "gate verdicts");
        AssertEqual(0L, allocated, "capture gate allocated bytes");
    }
}
