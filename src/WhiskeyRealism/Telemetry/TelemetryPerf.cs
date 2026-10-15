using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace WhiskeyRealism.Telemetry
{
    // Hot-path performance scopes. Every Harmony patch entry wraps its body in
    // `using (TelemetryPerf.Scope(...))`, including per-regiment patches that fire thousands
    // of times per second at high battle compression, so the per-call path must not
    // allocate:
    //   - scopes are pooled per thread and per nesting depth (LIFO under `using`);
    //   - timing uses Stopwatch.GetTimestamp(), no Stopwatch instance;
    //   - sub-threshold calls only update a per-thread, per-scope accumulator
    //     (count/total/max/slow count), flushed as one "PerformanceAggregate" row per scope
    //     per AggregateWindowMs of real time;
    //   - a call at/above its threshold also emits one full "Performance" row, at most
    //     MaxSlowRowsPerScopeWindow per scope per window, and only while the
    //     "Telemetry Performance Warnings" config is on.
    // Top-level scopes on the frame-probe thread are reported to TelemetryFrameProbe.
    internal static class TelemetryPerf
    {
        internal const int MaxSlowRowsPerScopeWindow = 8;
        internal const int MaxScopesPerThread = 1024;
        private const int MaxDepth = 32;

        // Real-time aggregation window. Internal (not const) so tests can shorten it.
        internal static long AggregateWindowMs = 1000;

        private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

        [ThreadStatic] private static PerfScope[] t_pool;
        [ThreadStatic] private static int t_depth;
        [ThreadStatic] private static ThreadAggregates t_aggregates;

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

                int depth = t_depth;
                if (depth < 0 || depth >= MaxDepth)
                    return NoopScope.Instance;

                PerfScope[] pool = t_pool ?? (t_pool = new PerfScope[MaxDepth]);
                PerfScope perfScope = pool[depth] ?? (pool[depth] = new PerfScope(depth));
                perfScope.Begin(scope, layer, category, thresholdMs);
                t_depth = depth + 1;
                return perfScope;
            }
            catch
            {
                return NoopScope.Instance;
            }
        }

        // Emits this thread's pending per-scope aggregates now. Called at telemetry shutdown
        // (main thread) so the final partial window is not lost.
        internal static void FlushAggregates()
        {
            try
            {
                ThreadAggregates aggregates = t_aggregates;
                if (aggregates != null)
                    aggregates.Flush(Stopwatch.GetTimestamp());
            }
            catch
            {
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

        private static void Complete(PerfScope scope, long endTimestamp)
        {
            double durationMs = (endTimestamp - scope.StartTimestamp) * TicksToMs;
            if (scope.Depth == 0)
                TelemetryFrameProbe.RecordTopLevelScope(scope.Name, durationMs);
            ThreadAggregates aggregates = t_aggregates ?? (t_aggregates = new ThreadAggregates(endTimestamp));
            ScopeAggregate aggregate = aggregates.Get(scope.Name, scope.Layer, scope.Category, scope.ThresholdMs);
            if (aggregate != null)
            {
                aggregate.Count++;
                aggregate.TotalMs += durationMs;
                if (durationMs > aggregate.MaxMs)
                    aggregate.MaxMs = durationMs;

                if (durationMs >= scope.ThresholdMs)
                {
                    aggregate.SlowCount++;
                    if (TelemetryRouter.PerformanceWarningsEnabled
                        && aggregate.SlowRowsEmitted < MaxSlowRowsPerScopeWindow)
                    {
                        aggregate.SlowRowsEmitted++;
                        TelemetryRuntimeDiagnostics counters = TelemetryRouter.DiagnosticsSnapshot();
                        EmitPerformance(
                            scope.Name,
                            scope.Layer,
                            scope.Category,
                            durationMs,
                            scope.ThresholdMs,
                            counters.EmittedCount,
                            counters.DroppedCount,
                            counters.EmittedBytes);
                    }
                }
            }

            if (endTimestamp - aggregates.WindowStart >= WindowTicks())
                aggregates.Flush(endTimestamp);
        }

        private static long WindowTicks()
        {
            long windowMs = Math.Max(1L, AggregateWindowMs);
            return windowMs * Stopwatch.Frequency / 1000L;
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

        private static void EmitAggregateRow(ScopeAggregate aggregate, double windowMs)
        {
            if (!TelemetryRouter.ShouldEmit(TelemetryRouter.CurrentProfile, aggregate.Layer, aggregate.Category))
                return;

            long count = aggregate.Count;
            long slowCount = aggregate.SlowCount;
            double totalMs = TelemetryFields.SanitizedNumber(aggregate.TotalMs);
            double maxMs = TelemetryFields.SanitizedNumber(aggregate.MaxMs);
            double thresholdMs = aggregate.ThresholdMs;
            string name = aggregate.Name;

            TelemetryRouter.Emit(
                aggregate.Layer,
                aggregate.Category,
                "PerformanceAggregate",
                slowCount > 0 ? TelemetrySeverity.Warning : TelemetrySeverity.Info,
                ev => ev
                    .WithDurationMs(maxMs)
                    .WithField("scope", name)
                    .WithField("aggregate", true)
                    .WithField("count", ClampToInt(count))
                    .WithField("slowCount", ClampToInt(slowCount))
                    .WithField("slow", slowCount > 0)
                    .WithField("totalMs", totalMs)
                    .WithField("maxMs", maxMs)
                    .WithField("meanMs", count > 0 ? totalMs / count : 0.0)
                    .WithField("thresholdMs", thresholdMs)
                    .WithField("windowMs", TelemetryFields.SanitizedNumber(windowMs)));
        }

        private static int ClampToInt(long value)
        {
            return value > int.MaxValue ? int.MaxValue : (int)value;
        }

        private sealed class PerfScope : IDisposable
        {
            internal PerfScope(int depth)
            {
                Depth = depth;
            }

            internal int Depth { get; private set; }
            internal string Name { get; private set; }
            internal TelemetryLayer Layer { get; private set; }
            internal TelemetryCategory Category { get; private set; }
            internal double ThresholdMs { get; private set; }
            internal long StartTimestamp { get; private set; }
            private bool _active;

            internal void Begin(string scope, TelemetryLayer layer, TelemetryCategory category, double thresholdMs)
            {
                Name = TelemetryEvent.Safe(scope);
                Layer = layer;
                Category = category;
                ThresholdMs = TelemetryFields.SanitizedNumber(thresholdMs);
                _active = true;
                StartTimestamp = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                if (!_active) return;
                long end = Stopwatch.GetTimestamp();
                _active = false;
                // Pop to this scope's depth (also heals a non-LIFO dispose).
                t_depth = Depth;

                try
                {
                    Complete(this, end);
                }
                catch
                {
                }
            }
        }

        private sealed class ScopeAggregate
        {
            internal ScopeAggregate(string name, TelemetryLayer layer, TelemetryCategory category, double thresholdMs)
            {
                Name = name;
                Layer = layer;
                Category = category;
                ThresholdMs = thresholdMs;
            }

            internal readonly string Name;
            internal readonly TelemetryLayer Layer;
            internal readonly TelemetryCategory Category;
            internal readonly double ThresholdMs;
            internal long Count;
            internal long SlowCount;
            internal int SlowRowsEmitted;
            internal double TotalMs;
            internal double MaxMs;

            internal void Reset()
            {
                Count = 0;
                SlowCount = 0;
                SlowRowsEmitted = 0;
                TotalMs = 0.0;
                MaxMs = 0.0;
            }
        }

        private sealed class ThreadAggregates
        {
            private readonly Dictionary<string, ScopeAggregate> _byName =
                new Dictionary<string, ScopeAggregate>(StringComparer.Ordinal);
            private readonly List<ScopeAggregate> _ordered = new List<ScopeAggregate>();
            private bool _flushing;

            internal ThreadAggregates(long windowStart)
            {
                WindowStart = windowStart;
            }

            internal long WindowStart { get; private set; }

            internal ScopeAggregate Get(string name, TelemetryLayer layer, TelemetryCategory category, double thresholdMs)
            {
                ScopeAggregate aggregate;
                if (_byName.TryGetValue(name, out aggregate))
                    return aggregate;
                if (_byName.Count >= MaxScopesPerThread)
                    return null;

                aggregate = new ScopeAggregate(name, layer, category, thresholdMs);
                _byName[name] = aggregate;
                _ordered.Add(aggregate);
                return aggregate;
            }

            internal void Flush(long now)
            {
                if (_flushing)
                    return;

                _flushing = true;
                try
                {
                    double windowMs = (now - WindowStart) * TicksToMs;
                    WindowStart = now;
                    for (int i = 0; i < _ordered.Count; i++)
                    {
                        ScopeAggregate aggregate = _ordered[i];
                        if (aggregate.Count <= 0)
                            continue;

                        try
                        {
                            EmitAggregateRow(aggregate, windowMs);
                        }
                        catch
                        {
                        }

                        aggregate.Reset();
                    }
                }
                finally
                {
                    _flushing = false;
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
