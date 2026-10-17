using System;
using System.Collections.Generic;

/// <summary>
/// BC14W enemy works (spec § 13.2). The slice's tests are registered here, one group per concern, each group in its own partial file
/// (ManeuverFeasibilityTests.Works.*.cs). Names start <c>bc14w &lt;TEST-ID&gt;</c> so <c>--only ManeuverFeasibilityTests "bc14w WORKS-GUARD"</c>
/// runs one case.
/// </summary>
internal static partial class ManeuverFeasibilityTests
{
    static partial void AddWorksTests(List<(string, Action)> into)
    {
        into.AddRange(WorksReaderTests());       // ManeuverFeasibilityTests.Works.Readers.cs (task 1)
        into.AddRange(WorksCopyTests());         // ManeuverFeasibilityTests.Works.Copy.cs (tasks 2, 6, 8)
        into.AddRange(WorksPictureTests());      // ManeuverFeasibilityTests.Works.Picture.cs (task 3)
        into.AddRange(WorksPricingTests());      // ManeuverFeasibilityTests.Works.Pricing.cs (task 4)
        into.AddRange(WorksFocalTests());        // ManeuverFeasibilityTests.Works.Focal.cs (task 5)
        into.AddRange(WorksGuardTests());        // ManeuverFeasibilityTests.Works.Guard.cs (task 6)
        into.AddRange(WorksOrderTests());        // ManeuverFeasibilityTests.Works.Orders.cs (task 6)
        into.AddRange(WorksStanceTests());       // ManeuverFeasibilityTests.Works.Stance.cs (task 6)
        into.AddRange(WorksHoldTests());         // ManeuverFeasibilityTests.Works.Hold.cs (task 6)
        into.AddRange(WorksContestTests());      // ManeuverFeasibilityTests.Works.Contest.cs (task 7)
    }
}
