namespace WhiskeyRealism
{
    internal static class Plugin
    {
        internal static TestLog Log = new TestLog();
        internal static TestConfig<bool> EnableTacticalMapKnowledgeDiagnostics = new TestConfig<bool>(false);
        internal static TestConfig<bool> EnableTacticalRegimentDiagnostics = new TestConfig<bool>(false);
        internal static TestConfig<string> TacticalRegimentDiagnosticNames = new TestConfig<string>(string.Empty);
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
        internal bool ThrowOnWarning;
        internal int InfoCount;
        internal int WarningCount;
        internal int ErrorCount;

        internal void LogInfo(string message)
        {
            if (ThrowOnInfo)
                throw new System.InvalidOperationException("test info logger unavailable");

            InfoCount++;
        }

        internal void LogWarning(string message)
        {
            if (ThrowOnWarning)
                throw new System.InvalidOperationException("test warning logger unavailable");

            WarningCount++;
        }

        internal void LogError(string message)
        {
            ErrorCount++;
        }
    }
}
