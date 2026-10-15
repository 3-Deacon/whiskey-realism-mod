using System;
using System.Collections.Generic;
using WhiskeyRealism.Telemetry;

namespace WhiskeyRealism.Util
{
    // One "[first-fire] <patchId>" main-log line per Harmony patch entry per
    // process, so a smoke log shows which patches actually ran.
    // tools/smoke-check.py collects the ids from the Mark calls in source and
    // reports the ones a LogOutput.log never mentions.
    //
    // Every Prefix/Postfix/Finalizer calls Mark first thing inside its
    // TelemetryPerf scope, with the scope name as the id. Ids are string
    // literals, so the fired set is bounded by the number of patch entries.
    // The set is never reset: battle and campaign teardown must not re-fire.
    //
    // Hot path: one lookup in an immutable set, no lock. The set is replaced
    // (copy-on-write) under the lock the first time an id fires. If the main log
    // is unavailable or throws, the id stays unrecorded and the next call retries.
    internal static class PatchFirstFire
    {
        internal const string LinePrefix = "[first-fire] ";

        private static readonly object Gate = new object();
        private static volatile HashSet<string> _fired = new HashSet<string>(StringComparer.Ordinal);

        internal static void Mark(string patchId)
        {
            try
            {
                if (patchId == null || _fired.Contains(patchId))
                    return;

                lock (Gate)
                {
                    if (_fired.Contains(patchId))
                        return;

                    string line = LinePrefix + patchId;
                    if (TelemetryRouter.LegacyInfo(line))
                    {
                        var log = Plugin.Log;
                        if (log == null)
                            return;
                        log.LogInfo(line);
                    }

                    _fired = new HashSet<string>(_fired, StringComparer.Ordinal) { patchId };
                }
            }
            catch
            {
                // A patch entry must never throw; the next call retries.
            }
        }

        internal static bool HasFired(string patchId)
        {
            return patchId != null && _fired.Contains(patchId);
        }
    }
}
