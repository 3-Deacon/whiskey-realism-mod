using System;

namespace WhiskeyRealism.Telemetry
{
    internal static class TelemetryRouter
    {
        private static readonly object Gate = new object();
        // Writes happen under Gate; hot-path reads (Emit, DiagnosticsSnapshot) are lock-free.
        private static volatile TelemetryRuntime _runtime;
        private static volatile TelemetryRuntime _shutdownRuntime;
        // Read on every TelemetryPerf.Scope call; volatile so the hot path never takes Gate.
        private static volatile TelemetryProfile _profile = TelemetryProfile.Off;
        private static volatile bool _performanceWarnings = true;
        private static bool _legacySuppressionWarned;
        private static long _legacySuppressedCount;

        internal static TelemetryProfile CurrentProfile
        {
            get { return _profile; }
        }

        // "Telemetry Performance Warnings" config: when false, slow TelemetryPerf scopes are
        // folded into the per-scope aggregate rows instead of emitting individual rows.
        internal static bool PerformanceWarningsEnabled
        {
            get { return _performanceWarnings; }
        }

        internal static TelemetryProfile ParseProfile(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return TelemetryProfile.Off;

            string normalized = value.Trim()
                .Replace(" ", string.Empty)
                .Replace("-", string.Empty)
                .Replace("_", string.Empty);

            if (string.Equals(normalized, "TacticalTuning", StringComparison.OrdinalIgnoreCase))
                return TelemetryProfile.TacticalTuning;
            if (string.Equals(normalized, "CampaignTuning", StringComparison.OrdinalIgnoreCase))
                return TelemetryProfile.CampaignTuning;
            if (string.Equals(normalized, "FullTuning", StringComparison.OrdinalIgnoreCase))
                return TelemetryProfile.FullTuning;
            if (string.Equals(normalized, "Off", StringComparison.OrdinalIgnoreCase))
                return TelemetryProfile.Off;

            return TelemetryProfile.Off;
        }

        internal static bool ShouldEmit(TelemetryProfile profile, TelemetryLayer layer, TelemetryCategory category)
        {
            if (IsProtected(category))
                return true;

            if (profile == TelemetryProfile.FullTuning)
                return true;

            if (profile == TelemetryProfile.Off)
                return false;

            if (layer == TelemetryLayer.System)
                return IsSystemTuningCategory(category);

            if (profile == TelemetryProfile.TacticalTuning)
                return layer == TelemetryLayer.Tactical;

            if (profile == TelemetryProfile.CampaignTuning)
                return layer == TelemetryLayer.Campaign;

            return false;
        }

        internal static bool ShouldBehaviorRun(TelemetryProfile profile, bool behaviorEnabled)
        {
            return behaviorEnabled;
        }

        internal static void AttachRuntime(TelemetryRuntime runtime)
        {
            lock (Gate)
            {
                _runtime = runtime;
                _shutdownRuntime = null;
                _profile = runtime != null ? runtime.Profile : TelemetryProfile.Off;
                _performanceWarnings = runtime == null || runtime.PerformanceWarnings;
                _legacySuppressionWarned = false;
                _legacySuppressedCount = 0L;
            }
        }

        internal static bool LegacyInfo(string line)
        {
            return Legacy(line, TelemetrySeverity.Info);
        }

        internal static bool LegacyInfo(string line, TelemetryLayer fallbackLayer)
        {
            return Legacy(line, TelemetrySeverity.Info);
        }

        internal static bool LegacyWarning(string line)
        {
            return Legacy(line, TelemetrySeverity.Warning);
        }

        internal static void LegacyInfoToMainLogIfAllowed(string line, TelemetryLayer fallbackLayer)
        {
            try
            {
                if (!LegacyInfo(line, fallbackLayer))
                    return;

                if (Plugin.Log != null)
                    Plugin.Log.LogInfo(line);
            }
            catch
            {
            }
        }

        internal static bool Emit(
            TelemetryLayer layer,
            TelemetryCategory category,
            string eventName,
            TelemetrySeverity severity,
            Action<TelemetryEvent> configure = null)
        {
            try
            {
                TelemetryRuntime runtime = _runtime ?? _shutdownRuntime;
                TelemetryProfile profile = _profile;

                if (!ShouldEmit(profile, layer, category))
                {
                    NoteTuningSuppressedByProfile(profile, category);
                    return false;
                }
                if (runtime == null || !runtime.IsRunning)
                    return false;

                var ev = TelemetryEvent.Create(
                    runtime.SessionId,
                    profile,
                    layer,
                    category,
                    eventName,
                    severity);
                if (configure != null)
                    configure(ev);

                return runtime.TryEmit(ev);
            }
            catch
            {
                return false;
            }
        }

        internal static bool Emit(TelemetryEvent ev)
        {
            try
            {
                if (ev == null)
                    return false;

                if (!ShouldEmit(ev.Profile, ev.Layer, ev.Category))
                {
                    NoteTuningSuppressedByProfile(ev.Profile, ev.Category);
                    return false;
                }

                TelemetryRuntime runtime = _runtime ?? _shutdownRuntime;
                return runtime != null && runtime.IsRunning && runtime.TryEmit(ev);
            }
            catch
            {
                return false;
            }
        }

        internal static TelemetryRuntimeDiagnostics DiagnosticsSnapshot()
        {
            try
            {
                TelemetryRuntime runtime = _runtime ?? _shutdownRuntime;
                return runtime != null
                    ? runtime.DiagnosticsSnapshot()
                    : TelemetryRuntimeDiagnostics.Empty;
            }
            catch
            {
                return TelemetryRuntimeDiagnostics.Empty;
            }
        }

        internal static void Shutdown(string reason)
        {
            try
            {
                // Write every thread's final partial aggregate window before stopping.
                TelemetryPerf.FlushAggregates();

                TelemetryRuntime runtime;
                lock (Gate)
                {
                    runtime = _runtime;
                    if (runtime == null)
                        return;
                    // Publish the shutdown runtime before clearing the live one: readers are
                    // lock-free (`_runtime ?? _shutdownRuntime`) and must never see both null.
                    _shutdownRuntime = runtime;
                    _runtime = null;
                    _profile = runtime.Profile;
                }

                runtime.Shutdown(reason);

                lock (Gate)
                {
                    if (object.ReferenceEquals(_shutdownRuntime, runtime))
                    {
                        _shutdownRuntime = null;
                        _profile = TelemetryProfile.Off;
                    }
                }
            }
            catch
            {
                lock (Gate)
                {
                    _runtime = null;
                    _shutdownRuntime = null;
                    _profile = TelemetryProfile.Off;
                }
            }
        }

        private static bool IsProtected(TelemetryCategory category)
        {
            return category == TelemetryCategory.Health || category == TelemetryCategory.Failure;
        }

        private static bool Legacy(string line, TelemetrySeverity severity)
        {
            try
            {
                var route = TelemetryTagPolicy.Route(line);
                if (!route.RouteToSidecar)
                    return route.AllowMainLog;

                TelemetryRuntime runtime = _runtime ?? _shutdownRuntime;
                TelemetryProfile profile = _profile;

                TelemetryEvent ev;
                bool emitted = false;
                if (TelemetryLegacyParser.TryParse(
                    line,
                    profile,
                    runtime != null ? runtime.SessionId : "legacy-runtime-unavailable",
                    severity,
                    out ev))
                {
                    emitted = Emit(ev);
                }

                bool protectMainLog = !emitted && ShouldProtectLegacyMainLog(route, severity);

                if (profile == TelemetryProfile.Off && !route.AllowMainLog && !protectMainLog)
                    NoteTuningSuppressedByProfile(profile, route.Category);

                return route.AllowMainLog || protectMainLog;
            }
            catch
            {
                return true;
            }
        }

        private static bool ShouldProtectLegacyMainLog(TelemetryTagRoute route, TelemetrySeverity severity)
        {
            return IsProtected(route.Category)
                || route.Severity >= TelemetrySeverity.Warning
                || severity >= TelemetrySeverity.Warning;
        }

        private static void NoteTuningSuppressedByProfile(TelemetryProfile profile, TelemetryCategory category)
        {
            if (profile != TelemetryProfile.Off || IsProtected(category))
                return;

            bool shouldWarn = false;
            lock (Gate)
            {
                _legacySuppressedCount++;
                if (!_legacySuppressionWarned)
                {
                    _legacySuppressionWarned = true;
                    shouldWarn = true;
                }
            }

            if (!shouldWarn)
                return;

            try
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning(
                        "[Telemetry] tuning telemetry disabled by profile; suppressing sidecar-only tuning telemetry rows");
                }
            }
            catch
            {
            }
        }

        private static bool IsSystemTuningCategory(TelemetryCategory category)
        {
            return category == TelemetryCategory.Health
                || category == TelemetryCategory.Failure
                || category == TelemetryCategory.Performance;
        }
    }
}
