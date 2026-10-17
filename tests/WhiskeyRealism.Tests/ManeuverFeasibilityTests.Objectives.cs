using System;
using System.Collections.Generic;

/// <summary>
/// BC14O objective doctrine (spec § 13.3). The slice's tests are registered here, one group per concern, each group in its own partial file
/// (ManeuverFeasibilityTests.Objectives.*.cs). Names start <c>bc14o &lt;TEST-ID&gt;</c> so <c>--only ManeuverFeasibilityTests "bc14o OBJ-VALUE"</c>
/// runs one case.
/// </summary>
internal static partial class ManeuverFeasibilityTests
{
    static partial void AddObjectiveTests(List<(string, Action)> into)
    {
        into.AddRange(ObjectiveValueTests());     // ManeuverFeasibilityTests.Objectives.Value.cs (task 1)
    }
}
