using System;

namespace WhiskeyRealism.Telemetry
{
    internal static class TelemetryRouter
    {
        private static readonly object Gate = new object();
        private static TelemetryRuntime _runtime;
        private static TelemetryRuntime _shutdownRuntime;
        private static TelemetryProfile _profile = TelemetryProfile.Off;
        private static bool _legacySuppressionWarned;
        private static long _legacySuppressedCount;

        internal static TelemetryProfile CurrentProfile
        {
            get
            {
                lock (Gate)
                    return _profile;
            }
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

        internal static bool LegacyWarning(string line, TelemetryLayer fallbackLayer)
        {
            return Legacy(line, TelemetrySeverity.Warning);
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
                TelemetryRuntime runtime;
                TelemetryProfile profile;
                lock (Gate)
                {
                    runtime = _runtime ?? _shutdownRuntime;
                    profile = _profile;
                }

                if (!ShouldEmit(profile, layer, category))
                    return false;
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
                if (ev == null || !ShouldEmit(ev.Profile, ev.Layer, ev.Category))
                    return false;

                TelemetryRuntime runtime;
                lock (Gate)
                    runtime = _runtime ?? _shutdownRuntime;

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
                TelemetryRuntime runtime;
                lock (Gate)
                    runtime = _runtime ?? _shutdownRuntime;

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
                TelemetryRuntime runtime;
                lock (Gate)
                {
                    runtime = _runtime;
                    if (runtime == null)
                        return;
                    _runtime = null;
                    _shutdownRuntime = runtime;
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

                TelemetryRuntime runtime;
                TelemetryProfile profile;
                lock (Gate)
                {
                    runtime = _runtime ?? _shutdownRuntime;
                    profile = _profile;
                }

                TelemetryEvent ev;
                if (TelemetryLegacyParser.TryParse(
                    line,
                    profile,
                    runtime != null ? runtime.SessionId : "legacy-runtime-unavailable",
                    severity,
                    out ev))
                {
                    Emit(ev);
                }

                if (profile == TelemetryProfile.Off && !route.AllowMainLog)
                    NoteLegacySuppressedByProfile();

                return route.AllowMainLog;
            }
            catch
            {
                return true;
            }
        }

        private static void NoteLegacySuppressedByProfile()
        {
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
                        "[Telemetry] tuning telemetry disabled by profile; suppressing sidecar-only legacy tuning log lines");
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
