using System;
using System.Diagnostics;

namespace WhiskeyRealism.Telemetry
{
    internal static class TelemetryPerf
    {
        internal static IDisposable Scope(
            string scope,
            TelemetryLayer layer,
            TelemetryCategory category,
            double thresholdMs)
        {
            try
            {
                if (!TelemetryRouter.ShouldEmit(TelemetryRouter.CurrentProfile, layer, category))
                    return NoopScope.Instance;
                return new PerfScope(scope, layer, category, thresholdMs);
            }
            catch
            {
                return NoopScope.Instance;
            }
        }

        internal static void EmitAggregate(
            string scope,
            TelemetryLayer layer,
            TelemetryCategory category,
            double durationMs,
            double thresholdMs,
            long eventsEmitted,
            long eventsDropped,
            long bytesWritten)
        {
            try
            {
                if (!TelemetryRouter.ShouldEmit(TelemetryRouter.CurrentProfile, layer, category))
                    return;

                EmitPerformance(
                    scope,
                    layer,
                    category,
                    durationMs,
                    thresholdMs,
                    eventsEmitted,
                    eventsDropped,
                    bytesWritten);
            }
            catch
            {
            }
        }

        private static void EmitPerformance(
            string scope,
            TelemetryLayer layer,
            TelemetryCategory category,
            double durationMs,
            double thresholdMs,
            long eventsEmitted,
            long eventsDropped,
            long bytesWritten)
        {
            durationMs = TelemetryFields.SanitizedNumber(durationMs);
            thresholdMs = TelemetryFields.SanitizedNumber(thresholdMs);
            bool slow = durationMs >= thresholdMs;
            TelemetryRuntimeDiagnostics counters = TelemetryRouter.DiagnosticsSnapshot();

            TelemetryRouter.Emit(
                layer,
                category,
                "Performance",
                slow ? TelemetrySeverity.Warning : TelemetrySeverity.Info,
                ev => ev
                    .WithDurationMs(durationMs)
                    .WithField("scope", TelemetryEvent.Safe(scope))
                    .WithField("slow", slow)
                    .WithField("thresholdMs", thresholdMs)
                    .WithField("queueDepth", counters.QueueDepth)
                    .WithField("eventsEmitted", eventsEmitted)
                    .WithField("eventsDropped", eventsDropped)
                    .WithField("bytesWritten", bytesWritten)
                    .WithField("emittedCount", eventsEmitted)
                    .WithField("droppedCount", eventsDropped)
                    .WithField("queueDroppedCount", counters.QueueDroppedCount)
                    .WithField("protectedOverflowCount", counters.ProtectedOverflowCount)
                    .WithField("budgetDroppedCount", counters.BudgetDroppedCount)
                    .WithField("emittedBytes", bytesWritten)
                    .WithField("sinkFailureCount", counters.SinkFailureCount));
        }

        private sealed class PerfScope : IDisposable
        {
            private readonly string _scope;
            private readonly TelemetryLayer _layer;
            private readonly TelemetryCategory _category;
            private readonly double _thresholdMs;
            private readonly Stopwatch _watch;
            private bool _disposed;

            internal PerfScope(string scope, TelemetryLayer layer, TelemetryCategory category, double thresholdMs)
            {
                _scope = TelemetryEvent.Safe(scope);
                _layer = layer;
                _category = category;
                _thresholdMs = TelemetryFields.SanitizedNumber(thresholdMs);
                _watch = Stopwatch.StartNew();
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                try
                {
                    _watch.Stop();
                    TelemetryRuntimeDiagnostics counters = TelemetryRouter.DiagnosticsSnapshot();
                    EmitPerformance(
                        _scope,
                        _layer,
                        _category,
                        _watch.Elapsed.TotalMilliseconds,
                        _thresholdMs,
                        counters.EmittedCount,
                        counters.DroppedCount,
                        counters.EmittedBytes);
                }
                catch
                {
                }
            }
        }

        private sealed class NoopScope : IDisposable
        {
            internal static readonly NoopScope Instance = new NoopScope();

            public void Dispose()
            {
            }
        }
    }
}
