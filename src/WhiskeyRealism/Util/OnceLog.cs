using System.Collections.Generic;
using WhiskeyRealism.Telemetry;

namespace WhiskeyRealism.Util
{
    internal static class OnceLog
    {
        private static readonly HashSet<string> _fired = new HashSet<string>();

        internal static void Info(string key, string message)
        {
            if (_fired.Contains(key)) return;
            _fired.Add(key);
            string line = "[once:" + key + "] " + message;
            if (TelemetryRouter.LegacyInfo(line))
                Plugin.Log.LogInfo(line);
        }

        internal static void Warning(string key, string message)
        {
            if (_fired.Contains(key)) return;
            _fired.Add(key);
            string line = "[once:" + key + "] " + message;
            if (TelemetryRouter.LegacyWarning(line))
                Plugin.Log.LogWarning(line);
        }

        internal static void Reset()
        {
            _fired.Clear();
        }
    }
}
