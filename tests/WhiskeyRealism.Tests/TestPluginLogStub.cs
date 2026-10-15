namespace WhiskeyRealism
{
    internal static class Plugin
    {
        internal static TestLog Log = new TestLog();
        internal static TestConfig<bool> EnableTacticalMapKnowledgeDiagnostics = new TestConfig<bool>(false);
        internal static TestConfig<bool> EnableTacticalRegimentDiagnostics = new TestConfig<bool>(false);
        internal static TestConfig<string> TacticalRegimentDiagnosticNames = new TestConfig<string>(string.Empty);
        internal static TestConfig<float> OpportunityHysteresisMinHoldSeconds = new TestConfig<float>(6.0f);
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
