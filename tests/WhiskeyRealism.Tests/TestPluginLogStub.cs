namespace WhiskeyRealism
{
    internal static class Plugin
    {
        internal static TestLog Log = new TestLog();
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
