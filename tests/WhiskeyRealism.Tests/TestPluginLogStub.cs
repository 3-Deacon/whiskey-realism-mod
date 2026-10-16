namespace WhiskeyRealism
{
    internal static class Plugin
    {
        internal static TestLog Log = new TestLog();
        internal static TestConfig<bool> EnableTacticalMapKnowledgeDiagnostics = new TestConfig<bool>(false);
        internal static TestConfig<bool> EnableTacticalRegimentDiagnostics = new TestConfig<bool>(false);
        internal static TestConfig<string> TacticalRegimentDiagnosticNames = new TestConfig<string>(string.Empty);
        internal static TestConfig<float> OpportunityHysteresisMinHoldSeconds = new TestConfig<float>(6.0f);
        internal static TestConfig<bool> EnableCampaignChainDispatches = new TestConfig<bool>(true);
        internal static TestConfig<bool> EnableWlOrderCouriers = new TestConfig<bool>(true);
        internal static TestConfig<bool> EnableWlOrderText = new TestConfig<bool>(true);
        internal static TestConfig<bool> FixCapitalDefensePlayerOrder = new TestConfig<bool>(true);
        internal static TestConfig<bool> EnableWlOrderFollowUp = new TestConfig<bool>(true);
        internal static TestPluginInstance Instance = new TestPluginInstance();
        internal static bool FlankResponse = true;
        internal static bool FlankResponseEnabled() => FlankResponse;
        // BC13: Tactical / Enable Per-Commander Personality (the attack-threshold band and the casualty threshold's basis).
        internal static bool PerCommanderPersonality = true;
        internal static bool PerCommanderPersonalityEnabled() => PerCommanderPersonality;

        // BC13: Tactical / Enable Staggered Reserve Release (ArmyOrchestrator builds the allocator's release context from it).
        internal static bool StaggeredReserveRelease = true;
        internal static bool StaggeredReserveReleaseEnabled() => StaggeredReserveRelease;

        // BC14: Tactical / Enable Maneuver Feasibility.
        internal static bool ManeuverFeasibility = true;
        internal static bool ManeuverFeasibilityEnabled() => ManeuverFeasibility;
    }

    // Stands in for the BepInEx plugin instance's master switch.
    internal sealed class TestPluginInstance
    {
        internal TestConfig<bool> Enabled = new TestConfig<bool>(true);
    }

    internal sealed class TestConfig<T>
    {
        internal TestConfig(T value)
        {
            Value = value;
        }

        internal T Value { get; set; }
    }

    internal sealed class TestLog
    {
        internal bool ThrowOnInfo;
        internal int InfoCount;
        internal string LastInfo;
        internal int WarningCount;
        internal int ErrorCount;

        internal void LogInfo(string message)
        {
            if (ThrowOnInfo)
                throw new System.InvalidOperationException("test info logger unavailable");

            InfoCount++;
            LastInfo = message;
        }

        internal void LogWarning(string message)
        {
            WarningCount++;
        }

        internal void LogError(string message)
        {
            ErrorCount++;
        }
    }
}
