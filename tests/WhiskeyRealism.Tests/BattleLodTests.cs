using System;
using static TestHarness;

// BC15 (docs/superpowers/plans/2026-10-15-battle-core.md § BC15): battle AI level of detail. One test array per partial file, joined here:
//   .Latch      the observation change latch and the drop log (BattlePerception.Update, Publish, ClearChangeLatch)
//   .Tiers      BattleLod.Classify: the Distant conditions, the schedule and the stagger
//   .Promotion  the promotion triggers (a)-(f) and the contact test
//   .Equivalence the fixture battle run with and without the tiers
//   .Arbiter    OrderArbiter carried claims and the executor's unsettled test
//   .Nav        the stable NavPlanBudget cursor
//   .Runtime    source pins for the runtime halves (BattleCoreTick, the producers, the flag, the telemetry row)
internal static partial class BattleLodTests
{
    public static void Run()
    {
        RunAll(Join(LatchTests(), TierTests(), PromotionTests()));
    }

    private static (string, Action)[] Join(params (string, Action)[][] parts)
    {
        int n = 0;
        for (int i = 0; i < parts.Length; i++) n += parts[i].Length;
        var all = new (string, Action)[n];
        int at = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i].CopyTo(all, at);
            at += parts[i].Length;
        }
        return all;
    }
}
