using System;
using WhiskeyRealism.Tactical.Battle;

internal static class BattleCoreContractsTests
{
    public static void Run()
    {
        IntentPriorityLadderOrdersSafetyAboveExecutorAbovePlay();
        OrderChannelsCountMatchesChannelEnum();
        UnitFlagsAreDistinctSingleBits();
        IntentFlagsAreDistinctSingleBits();
        PerceptionAndObservationValuesArePinned();
    }

    private static void IntentPriorityLadderOrdersSafetyAboveExecutorAbovePlay()
    {
        TestHarness.AssertTrue(IntentPriority.Safety > IntentPriority.Executor, "Safety > Executor");
        TestHarness.AssertTrue(IntentPriority.Executor > IntentPriority.Play, "Executor > Play");
        TestHarness.AssertTrue(IntentPriority.Play > IntentPriority.Support, "Play > Support");
        TestHarness.AssertTrue(IntentPriority.Support > IntentPriority.Scorer, "Support > Scorer");
        TestHarness.AssertEqual(1, (int)IntentPriority.Scorer, "Scorer is the lowest rung");
        Console.WriteLine("PASS battle-core intent priority ladder Safety > Executor > Play > Support > Scorer");
    }

    private static void OrderChannelsCountMatchesChannelEnum()
    {
        var channels = (OrderChannel[])Enum.GetValues(typeof(OrderChannel));
        TestHarness.AssertEqual(channels.Length, OrderChannels.Count, "OrderChannels.Count");
        for (int i = 0; i < channels.Length; i++)
            TestHarness.AssertEqual(i, (int)channels[i], "channel values are dense 0..Count-1");
        Console.WriteLine("PASS battle-core order channel count matches the channel enum");
    }

    private static void UnitFlagsAreDistinctSingleBits()
    {
        uint seen = 0;
        foreach (UnitFlags flag in Enum.GetValues(typeof(UnitFlags)))
        {
            if (flag == UnitFlags.None) continue;
            uint bit = (uint)flag;
            TestHarness.AssertTrue((bit & (bit - 1)) == 0, "single bit: " + flag);
            TestHarness.AssertTrue((seen & bit) == 0, "distinct bit: " + flag);
            seen |= bit;
        }
        Console.WriteLine("PASS battle-core unit flags are distinct single bits");
    }

    private static void IntentFlagsAreDistinctSingleBits()
    {
        int seen = 0;
        foreach (IntentFlags flag in Enum.GetValues(typeof(IntentFlags)))
        {
            if (flag == IntentFlags.None) continue;
            int bit = (int)flag;
            TestHarness.AssertTrue((bit & (bit - 1)) == 0, "single bit: " + flag);
            TestHarness.AssertTrue((seen & bit) == 0, "distinct bit: " + flag);
            seen |= bit;
        }
        Console.WriteLine("PASS battle-core intent flags are distinct single bits");
    }

    private static void PerceptionAndObservationValuesArePinned()
    {
        TestHarness.AssertEqual(0, (int)PerceptionMode.FogGrid, "FogGrid");
        TestHarness.AssertEqual(1, (int)ObservationState.Visible, "Visible");
        TestHarness.AssertEqual(2, (int)ObservationState.Remembered, "Remembered");
        Console.WriteLine("PASS battle-core perception mode and observation state values are pinned");
    }
}
