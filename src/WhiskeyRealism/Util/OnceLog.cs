using System.Collections.Generic;
using WhiskeyRealism.Telemetry;

namespace WhiskeyRealism.Util
{
    // First-fire / one-shot logging keyed by a caller-chosen string.
    //
    // Keys MUST be static (a literal, or a literal plus a value from a small fixed set such
    // as a vanilla method name). Never embed instance ids, unit names, signatures, or other
    // unbounded values in a key: the fired set lives for the whole process and is capped at
    // MaxKeys; once full, every new key is suppressed (one warning is written when the cap
    // is first hit). Use Claim(key) to gate expensive message construction.
    internal static class OnceLog
    {
        internal const int MaxKeys = 4096;

        private static readonly object Gate = new object();
        private static readonly HashSet<string> _fired = new HashSet<string>(System.StringComparer.Ordinal);
        private static bool _capWarned;
        private static long _suppressedOverCap;

        internal static int FiredCount
        {
            get
            {
                lock (Gate)
                    return _fired.Count;
            }
        }

        internal static long SuppressedOverCapCount
        {
            get
            {
                lock (Gate)
                    return _suppressedOverCap;
            }
        }

        internal static void Info(string key, string message)
        {
            TryLog(key, message, warning: false);
        }

        internal static void Warning(string key, string message)
        {
            TryLog(key, message, warning: true);
        }

        // Returns true exactly once per key (and marks it fired); false when the key has
        // already fired or the fired set is at MaxKeys. Callers that log through Claim own
        // their own output.
        internal static bool Claim(string key)
        {
            try
            {
                string firedKey = key ?? string.Empty;
                lock (Gate)
                {
                    if (_fired.Contains(firedKey))
                        return false;
                    if (!HasCapacityLocked())
                        return false;
                    _fired.Add(firedKey);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        internal static void Reset()
        {
            try
            {
                lock (Gate)
                {
                    _fired.Clear();
                    _capWarned = false;
                    _suppressedOverCap = 0L;
                }
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
                lock (Gate)
                {
                    // Check before building the line so repeat calls do no string work.
                    if (_fired.Contains(firedKey))
                        return;
                    if (!HasCapacityLocked())
                        return;

                    string line = "[once:" + firedKey + "] " + (message ?? string.Empty);
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

        private static bool HasCapacityLocked()
        {
            if (_fired.Count < MaxKeys)
                return true;

            _suppressedOverCap++;
            if (!_capWarned)
            {
                _capWarned = true;
                TryWriteMainLog(
                    "[OnceLog] fired-key cap " + MaxKeys + " reached; further new once-keys are suppressed. "
                        + "A caller is likely building dynamic OnceLog keys.",
                    warning: true);
            }

            return false;
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
