using System.Collections.Generic;
using WhiskeyRealism.Telemetry;

namespace WhiskeyRealism.Util
{
    internal static class OnceLog
    {
        private static readonly object Gate = new object();
        private static readonly HashSet<string> _fired = new HashSet<string>();

        internal static void Info(string key, string message)
        {
            TryLog(key, message, warning: false);
        }

        internal static void Warning(string key, string message)
        {
            TryLog(key, message, warning: true);
        }

        internal static void Reset()
        {
            try
            {
                lock (Gate)
                    _fired.Clear();
            }
            catch
            {
            }
        }

        private static void TryLog(string key, string message, bool warning)
        {
            try
            {
                string firedKey = key ?? string.Empty;
                string line = "[once:" + firedKey + "] " + (message ?? string.Empty);

                lock (Gate)
                {
                    if (_fired.Contains(firedKey))
                        return;

                    bool allowMainLog = warning
                        ? TelemetryRouter.LegacyWarning(line)
                        : TelemetryRouter.LegacyInfo(line);

                    if (allowMainLog && !TryWriteMainLog(line, warning))
                        return;

                    _fired.Add(firedKey);
                }
            }
            catch
            {
            }
        }

        private static bool TryWriteMainLog(string line, bool warning)
        {
            try
            {
                var log = Plugin.Log;
                if (log == null)
                    return false;

                if (warning)
                    log.LogWarning(line);
                else
                    log.LogInfo(line);

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
