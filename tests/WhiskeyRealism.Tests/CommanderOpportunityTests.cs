using System;
using static TestHarness;

// BC13 (docs/superpowers/plans/2026-10-15-battle-core.md § BC13, spec docs/superpowers/specs/2026-10-15-battle-opportunity-design.md):
// commander opportunity and exploitation. One test array per partial file, joined here:
//   .Personality  PERS-*  (spec § 4)
//   .Detection    DET-*   (spec § 5)
//   .Probe        the perceived-ground lattice, penetration, Counterstroke (spec § 5)
//   .Decision     OPP-*   (spec § 6)
//   .Tick         OpportunityTick over a fixture: launch, step, collect, stand-down (spec § 6)
//   .Reserve      RES-*   (the staggered reserve release, spec § 6)
//   .Override     OVR-* (the posture pipeline's exploit override: pure halves and source pins)
//   .Lifecycle    LIFE-*, COUNTER-BOUNDS, OPP-CLAIM-RELEASE (spec § 6: exploit lifecycle, table, feedback)
internal static partial class CommanderOpportunityTests
{
    public static void Run()
    {
        RunAll(Join(PersonalityTests(), DetectionTests(), ProbeTests(), DecisionTests(), LifeTests(), TickTests(), OverrideTests(), ReserveTests()));
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
