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
                    double durationMs = TelemetryFields.SanitizedNumber(_watch.Elapsed.TotalMilliseconds);
                    bool slow = durationMs >= _thresholdMs;
                    TelemetryRuntimeDiagnostics counters = TelemetryRouter.DiagnosticsSnapshot();

                    TelemetryRouter.Emit(
                        _layer,
                        _category,
                        "Performance",
                        slow ? TelemetrySeverity.Warning : TelemetrySeverity.Info,
                        ev => ev
                            .WithDurationMs(durationMs)
                            .WithField("scope", _scope)
                            .WithField("slow", slow)
                            .WithField("thresholdMs", _thresholdMs)
                            .WithField("queueDepth", counters.QueueDepth)
                            .WithField("emittedCount", counters.EmittedCount)
                            .WithField("droppedCount", counters.DroppedCount)
                            .WithField("queueDroppedCount", counters.QueueDroppedCount)
                            .WithField("protectedOverflowCount", counters.ProtectedOverflowCount)
                            .WithField("budgetDroppedCount", counters.BudgetDroppedCount)
                            .WithField("emittedBytes", counters.EmittedBytes)
                            .WithField("sinkFailureCount", counters.SinkFailureCount));
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
