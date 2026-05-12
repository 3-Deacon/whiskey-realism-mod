namespace WhiskeyRealism
{
    internal static class Plugin
    {
        internal static readonly TestLog Log = new TestLog();
    }

    internal sealed class TestLog
    {
        internal void LogInfo(string message) { }
        internal void LogWarning(string message) { }
        internal void LogError(string message) { }
    }
}
