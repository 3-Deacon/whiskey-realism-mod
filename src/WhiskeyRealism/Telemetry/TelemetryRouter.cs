using System;

namespace WhiskeyRealism.Telemetry
{
    internal static class TelemetryRouter
    {
        private static readonly object Gate = new object();
        private static TelemetryRuntime _runtime;
        private static TelemetryProfile _profile = TelemetryProfile.Off;

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
            if (profile == TelemetryProfile.FullTuning)
                return true;

            if (profile == TelemetryProfile.Off)
                return IsProtected(category);

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
                _profile = runtime != null ? runtime.Profile : TelemetryProfile.Off;
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
                TelemetryRuntime runtime;
                TelemetryProfile profile;
                lock (Gate)
                {
                    runtime = _runtime;
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
                    runtime = _runtime;

                return runtime != null && runtime.IsRunning && runtime.TryEmit(ev);
            }
            catch
            {
                return false;
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
                    _runtime = null;
                    _profile = TelemetryProfile.Off;
                }

                if (runtime != null)
                    runtime.Shutdown(reason);
            }
            catch
            {
            }
        }

        private static bool IsProtected(TelemetryCategory category)
        {
            return category == TelemetryCategory.Health || category == TelemetryCategory.Failure;
        }

        private static bool IsSystemTuningCategory(TelemetryCategory category)
        {
            return category == TelemetryCategory.Health
                || category == TelemetryCategory.Failure
                || category == TelemetryCategory.Performance;
        }
    }
}
