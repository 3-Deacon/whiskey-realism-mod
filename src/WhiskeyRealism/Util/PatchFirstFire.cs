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
    // Hot path: one identity lookup in an immutable alias set (ids are literals,
    // so a reference hash), no lock. A miss falls back to the canonical ordinal
    // set; an id that already fired under another string instance is then added
    // as an alias, up to MaxAliases, so an id built per call can neither re-fire
    // nor grow the alias set without bound. Both sets are replaced (copy-on-write)
    // under the lock. If the main log is unavailable or throws, the id stays
    // unrecorded and the next call retries.
    internal static class PatchFirstFire
    {
        internal const string LinePrefix = "[first-fire] ";
        internal const int MaxAliases = 2048;

        private static readonly object Gate = new object();
        private static volatile HashSet<string> _fired = new HashSet<string>(StringComparer.Ordinal);
        private static volatile HashSet<string> _firedRefs = new HashSet<string>(ReferenceStringComparer.Instance);

        internal static void Mark(string patchId)
        {
            try
            {
                if (patchId == null || _firedRefs.Contains(patchId))
                    return;
                if (_fired.Contains(patchId))
                {
                    AddAlias(patchId);
                    return;
                }

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
                    AddAlias(patchId);
                }
            }
            catch
            {
                // A patch entry must never throw; the next call retries.
            }
        }

        private static void AddAlias(string patchId)
        {
            lock (Gate)
            {
                HashSet<string> refs = _firedRefs;
                if (refs.Count >= MaxAliases || refs.Contains(patchId))
                    return;
                _firedRefs = new HashSet<string>(refs, ReferenceStringComparer.Instance) { patchId };
            }
        }

        internal static bool HasFired(string patchId)
        {
            return patchId != null && _fired.Contains(patchId);
        }
    }
}
