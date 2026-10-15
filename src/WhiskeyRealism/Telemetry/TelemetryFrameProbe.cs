using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace WhiskeyRealism.Telemetry
{
    // Per-frame hitch probe (WS P, P-probe). Plugin.Update calls Tick(Time.unscaledDeltaTime)
    // once per frame. For a frame of >= 250 ms it emits one protected Health "FrameHitch" row
    // with the frame duration, GC collection-count deltas, GC/Mono heap sizes, telemetry
    // queue depth, and the top-level TelemetryPerf scopes that ran on the main thread since
    // the previous Tick. Session max frame and the >500 ms count go to the manifest
    // ("frameProbe") and the summary. Normal frames do three GC.CollectionCount reads and
    // no allocation.
    internal static class TelemetryFrameProbe
    {
        internal const int MaxHitchRowsPerSession = 2000;

        private static readonly FrameProbeState State = new FrameProbeState();

        [ThreadStatic] private static bool t_isProbeThread;
        private static long _lastTickTimestamp;
        private static int _hitchRowsEmitted;
        private static bool _monoProfilerUnavailable;
        private static readonly bool[] _collectionCountUnsupported = new bool[3];

        // Session statistics, read by the writer thread when it writes the manifest.
        private static long _statHitchCount;
        private static long _statSevereCount;
        private static long _statMaxFrameMicros;
        private static long _statFrameCount;

        internal static void Tick(float unscaledDeltaTime)
        {
            try
            {
                t_isProbeThread = true;
                long now = Stopwatch.GetTimestamp();
                double wallMs = _lastTickTimestamp == 0L
                    ? 0.0
                    : (now - _lastTickTimestamp) * 1000.0 / Stopwatch.Frequency;
                _lastTickTimestamp = now;

                double deltaMs = TelemetryFields.SanitizedNumber(unscaledDeltaTime * 1000.0);
                double frameMs = Math.Max(deltaMs, wallMs);

                TelemetryFrameHitch hitch;
                bool isHitch = State.Observe(
                    frameMs,
                    GC.CollectionCount(0),
                    SafeCollectionCount(1),
                    SafeCollectionCount(2),
                    out hitch);
                PublishStats();

                if (isHitch)
                {
                    hitch.WallIntervalMs = wallMs;
                    EmitHitch(hitch);
                }
            }
            catch (Exception ex)
            {
                WhiskeyRealism.Util.OnceLog.Warning(
                    "telemetry-frame-probe:tick-failed",
                    "[Telemetry] frame hitch probe tick failed: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                State.ResetFrameScopes();
            }
        }

        // Boehm (Unity Mono) is non-generational; if a generation query is unsupported the
        // probe keeps running with 0 for that generation instead of failing every frame.
        private static long SafeCollectionCount(int generation)
        {
            if (_collectionCountUnsupported[generation])
                return 0L;
            try
            {
                return GC.CollectionCount(generation);
            }
            catch
            {
                _collectionCountUnsupported[generation] = true;
                return 0L;
            }
        }

        // True on the thread that runs Tick (Unity main thread).
        internal static bool IsProbeThread
        {
            get { return t_isProbeThread; }
        }

        // Called by TelemetryPerf when a depth-0 scope completes. Only the thread that runs
        // Tick (Unity main thread) records; worker/writer threads are ignored.
        internal static void RecordTopLevelScope(string scope, double durationMs)
        {
            if (!t_isProbeThread)
                return;
            State.RecordScope(scope, durationMs);
        }

        internal static void AddManifestStats(System.Collections.Generic.Dictionary<string, double> target)
        {
            if (target == null)
                return;
            target["frameCount"] = Interlocked.Read(ref _statFrameCount);
            target["hitchCount"] = Interlocked.Read(ref _statHitchCount);
            target["severeHitchCount"] = Interlocked.Read(ref _statSevereCount);
            target["maxFrameMs"] = Interlocked.Read(ref _statMaxFrameMicros) / 1000.0;
            target["hitchThresholdMs"] = FrameProbeState.HitchThresholdMs;
            target["severeThresholdMs"] = FrameProbeState.SevereThresholdMs;
        }

        internal static void ResetForTests()
        {
            State.Reset();
            _lastTickTimestamp = 0L;
            _hitchRowsEmitted = 0;
            Interlocked.Exchange(ref _statHitchCount, 0L);
            Interlocked.Exchange(ref _statSevereCount, 0L);
            Interlocked.Exchange(ref _statMaxFrameMicros, 0L);
            Interlocked.Exchange(ref _statFrameCount, 0L);
        }

        private static void PublishStats()
        {
            Interlocked.Exchange(ref _statFrameCount, State.FrameCount);
            Interlocked.Exchange(ref _statHitchCount, State.HitchCount);
            Interlocked.Exchange(ref _statSevereCount, State.SevereHitchCount);
            Interlocked.Exchange(ref _statMaxFrameMicros, (long)(State.MaxFrameMs * 1000.0));
        }

        private static void EmitHitch(TelemetryFrameHitch hitch)
        {
            if (_hitchRowsEmitted >= MaxHitchRowsPerSession)
                return;
            _hitchRowsEmitted++;

            long totalMemory = SafeTotalMemory();
            long monoUsed = -1L;
            long monoHeap = -1L;
            if (!_monoProfilerUnavailable)
            {
                try
                {
                    monoUsed = MonoUsedSize();
                    monoHeap = MonoHeapSize();
                }
                catch
                {
                    _monoProfilerUnavailable = true;
                }
            }

            int queueDepth = TelemetryRouter.DiagnosticsSnapshot().QueueDepth;
            TelemetryRouter.Emit(
                TelemetryLayer.System,
                TelemetryCategory.Health,
                "FrameHitch",
                TelemetrySeverity.Warning,
                ev => ev
                    .WithDurationMs(hitch.FrameMs)
                    .WithField("severe", hitch.Severe)
                    .WithField("frameIndex", (double)hitch.FrameIndex)
                    .WithField("wallIntervalMs", hitch.WallIntervalMs)
                    .WithField("gen0Delta", (double)hitch.Gen0Delta)
                    .WithField("gen1Delta", (double)hitch.Gen1Delta)
                    .WithField("gen2Delta", (double)hitch.Gen2Delta)
                    .WithField("gcTotalMemoryBytes", (double)totalMemory)
                    .WithField("monoUsedBytes", (double)monoUsed)
                    .WithField("monoHeapBytes", (double)monoHeap)
                    .WithField("queueDepth", queueDepth)
                    .WithField("scopeCount", hitch.ScopeCount)
                    .WithField("scopeNamesEvicted", hitch.ScopeNamesEvicted)
                    .WithField("scopeTotalMs", hitch.ScopeTotalMs)
                    .WithField("topScopes", hitch.TopScopes)
                    .WithField("protectedSummary", true));
        }

        private static long SafeTotalMemory()
        {
            try
            {
                return GC.GetTotalMemory(false);
            }
            catch
            {
                return -1L;
            }
        }

        // Unity icalls live in their own methods so a non-Unity host (the test harness)
        // fails inside the try/catch above rather than when JIT-compiling EmitHitch.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long MonoUsedSize()
        {
            return UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long MonoHeapSize()
        {
            return UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong();
        }
    }

    internal sealed class TelemetryFrameHitch
    {
        internal double FrameMs;
        internal double WallIntervalMs;
        internal long FrameIndex;
        internal bool Severe;
        internal long Gen0Delta;
        internal long Gen1Delta;
        internal long Gen2Delta;
        internal int ScopeCount;
        internal int ScopeNamesEvicted;
        internal double ScopeTotalMs;
        internal string TopScopes;
    }

    // Pure, single-threaded frame accounting used by TelemetryFrameProbe (main thread only).
    internal sealed class FrameProbeState
    {
        internal const double HitchThresholdMs = 250.0;
        internal const double SevereThresholdMs = 500.0;
        internal const int MaxScopesPerFrame = 64;
        internal const int TopScopesInRow = 8;

        // Per-frame totals keyed by scope NAME (scope names are static literals), so
        // thousands of cheap per-regiment calls cannot crowd out one slow tick. Up to
        // MaxScopesPerFrame distinct names; a new name when full replaces the smallest.
        private readonly string[] _scopeNames = new string[MaxScopesPerFrame];
        private readonly double[] _scopeMs = new double[MaxScopesPerFrame];
        private readonly int[] _scopeCalls = new int[MaxScopesPerFrame];
        private int _distinctScopes;
        private int _scopeCallTotal;
        private int _scopeNamesEvicted;
        private double _scopeTotalMs;
        private bool _hasGcBaseline;
        private long _lastGen0;
        private long _lastGen1;
        private long _lastGen2;

        internal long FrameCount { get; private set; }
        internal long HitchCount { get; private set; }
        internal long SevereHitchCount { get; private set; }
        internal double MaxFrameMs { get; private set; }

        internal void RecordScope(string name, double durationMs)
        {
            _scopeTotalMs += durationMs;
            _scopeCallTotal++;

            int slot = -1;
            for (int i = 0; i < _distinctScopes; i++)
            {
                if (string.Equals(_scopeNames[i], name, StringComparison.Ordinal))
                {
                    slot = i;
                    break;
                }
            }

            if (slot < 0)
            {
                if (_distinctScopes < MaxScopesPerFrame)
                {
                    slot = _distinctScopes++;
                }
                else
                {
                    slot = 0;
                    for (int i = 1; i < _distinctScopes; i++)
                    {
                        if (_scopeMs[i] < _scopeMs[slot])
                            slot = i;
                    }
                    _scopeNamesEvicted++;
                }

                _scopeNames[slot] = name;
                _scopeMs[slot] = 0.0;
                _scopeCalls[slot] = 0;
            }

            _scopeMs[slot] += durationMs;
            _scopeCalls[slot]++;
        }

        internal void ResetFrameScopes()
        {
            for (int i = 0; i < _distinctScopes; i++)
                _scopeNames[i] = null;
            _distinctScopes = 0;
            _scopeCallTotal = 0;
            _scopeNamesEvicted = 0;
            _scopeTotalMs = 0.0;
        }

        internal void Reset()
        {
            ResetFrameScopes();
            _hasGcBaseline = false;
            FrameCount = 0;
            HitchCount = 0;
            SevereHitchCount = 0;
            MaxFrameMs = 0.0;
        }

        // Records one frame. Returns true (and a populated hitch) when frameMs crosses the
        // hitch threshold; the only allocation happens on that path.
        internal bool Observe(double frameMs, long gen0, long gen1, long gen2, out TelemetryFrameHitch hitch)
        {
            hitch = null;
            frameMs = TelemetryFields.SanitizedNumber(frameMs);
            long gen0Delta = _hasGcBaseline ? gen0 - _lastGen0 : 0L;
            long gen1Delta = _hasGcBaseline ? gen1 - _lastGen1 : 0L;
            long gen2Delta = _hasGcBaseline ? gen2 - _lastGen2 : 0L;
            _lastGen0 = gen0;
            _lastGen1 = gen1;
            _lastGen2 = gen2;
            _hasGcBaseline = true;

            FrameCount++;
            if (frameMs > MaxFrameMs)
                MaxFrameMs = frameMs;
            if (frameMs < HitchThresholdMs)
                return false;

            HitchCount++;
            bool severe = frameMs >= SevereThresholdMs;
            if (severe)
                SevereHitchCount++;

            hitch = new TelemetryFrameHitch
            {
                FrameMs = frameMs,
                FrameIndex = FrameCount,
                Severe = severe,
                Gen0Delta = gen0Delta,
                Gen1Delta = gen1Delta,
                Gen2Delta = gen2Delta,
                ScopeCount = _scopeCallTotal,
                ScopeNamesEvicted = _scopeNamesEvicted,
                ScopeTotalMs = _scopeTotalMs,
                TopScopes = FormatTopScopes()
            };
            return true;
        }

        private string FormatTopScopes()
        {
            if (_distinctScopes == 0)
                return "-";

            var order = new int[_distinctScopes];
            for (int i = 0; i < _distinctScopes; i++)
                order[i] = i;
            Array.Sort(order, (left, right) =>
            {
                int byMs = _scopeMs[right].CompareTo(_scopeMs[left]);
                return byMs != 0 ? byMs : left.CompareTo(right);
            });

            var builder = new StringBuilder(256);
            int take = Math.Min(TopScopesInRow, _distinctScopes);
            for (int i = 0; i < take; i++)
            {
                if (i > 0)
                    builder.Append(';');
                int slot = order[i];
                builder.Append(TelemetryEvent.Safe(_scopeNames[slot]));
                builder.Append(':');
                builder.Append(_scopeMs[slot].ToString("0.0", CultureInfo.InvariantCulture));
                builder.Append('x');
                builder.Append(_scopeCalls[slot].ToString(CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}
