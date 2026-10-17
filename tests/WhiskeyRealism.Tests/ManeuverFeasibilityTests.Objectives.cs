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
        into.AddRange(ObjectivePressureTests());  // ManeuverFeasibilityTests.Objectives.Pressure.cs (task 2)
        into.AddRange(ObjectiveTableTests());     // ManeuverFeasibilityTests.Objectives.Table.cs (task 2)
        into.AddRange(ObjectivePinTests());       // ManeuverFeasibilityTests.Objectives.Pin.cs (task 2)
        into.AddRange(ObjectiveMainTests());      // ManeuverFeasibilityTests.Objectives.Main.cs (task 3)
        into.AddRange(ObjectiveScreenTests());    // ManeuverFeasibilityTests.Objectives.Screen.cs (task 4)
        into.AddRange(ObjectiveChainTests());     // ManeuverFeasibilityTests.Objectives.Chain.cs (task 5)
        into.AddRange(ObjectiveBoundsTests());    // ManeuverFeasibilityTests.Objectives.Bounds.cs (task 6)
    }
}
