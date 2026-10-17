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
    }
}
