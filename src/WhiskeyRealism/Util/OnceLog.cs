using System.Collections.Generic;
using WhiskeyRealism.Telemetry;

namespace WhiskeyRealism.Util
{
    // First-fire / one-shot logging keyed by a caller-chosen string.
    //
    // Keys MUST be static (a literal, or a literal plus a value from a small fixed set such
    // as a vanilla method name). Never embed instance ids, unit names, signatures, or other
    // unbounded values in a key: the fired set lives for the whole process. Info keys are
    // capped at MaxKeys; once full, new Info keys are suppressed (one warning is written when
    // the cap is first hit). Warnings are never suppressed by the cap (they are still
    // deduplicated by key). Keys starting with "tactical" are per-battle: ResetTactical()
    // releases them at battle end so first-fire lines re-fire in the next battle.
    internal static class OnceLog
    {
        static OnceLog()
        {
            BattleScope.Register("oncelog-tactical", ResetTactical);
            CampaignScope.Register("oncelog", Reset);
        }

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

        internal const string TacticalKeyPrefix = "tactical";

        internal static void Info(string key, string message)
        {
            TryLog(key, message, warning: false);
        }

        internal static void Warning(string key, string message)
        {
            TryLog(key, message, warning: true);
        }

        // Battle-end hook: forget every key starting with TacticalKeyPrefix.
        internal static void ResetTactical()
        {
            try
            {
                lock (Gate)
                    _fired.RemoveWhere(key => key.StartsWith(TacticalKeyPrefix, System.StringComparison.Ordinal));
            }
            catch
            {
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
                    if (!warning && !HasCapacityLocked())
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
