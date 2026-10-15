using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace WhiskeyRealism.Telemetry
{
    // Unity Profiler markers for TelemetryPerf scopes, so Whiskey's cost shows in the Unity
    // Profiler under its own scope names like any game system.
    //
    // ProfilerMarker.Begin/End are [Conditional("ENABLE_PROFILER")] (UnityEngine.CoreModule
    // 2021.3), so a plugin compiled outside Unity would have those calls stripped; this
    // class calls the same icalls the marker wraps instead (ProfilerUnsafeUtility.CreateMarker
    // / BeginSample / EndSample). Profiler.supported is false in a non-development player
    // (GTCW ships one), so in the shipped game Refresh leaves Active false and a scope pays
    // one static bool read. With a development player and the Profiler recording, every
    // scope opens a marker cached per name (identity alias table in front of an ordinal map,
    // per thread). Any failure (a non-Unity host such as the test harness, a missing icall)
    // disables markers for the session.
    internal static class TelemetryProfilerMarkers
    {
        internal const int MaxAliasesPerThread = 2048;
        internal const int MaxMarkersPerThread = 1024;

        // Scripts (ProfilerCategory.Scripts).
        private const ushort ScriptsCategory = 1;

        private static volatile bool _active;
        private static bool _unavailable;

        [ThreadStatic] private static Dictionary<string, IntPtr> t_byRef;
        [ThreadStatic] private static Dictionary<string, IntPtr> t_byName;

        internal static bool Active
        {
            get { return _active; }
        }

        // Once per frame from TelemetryFrameProbe.Tick (main thread): one icall pair.
        internal static void Refresh()
        {
            if (_unavailable)
                return;
            try
            {
                _active = ProfilerRecording();
            }
            catch
            {
                Disable();
            }
        }

        // Opens the scope's marker; returns its handle (IntPtr.Zero when inactive or failed).
        internal static IntPtr Begin(string name)
        {
            if (!_active || name == null)
                return IntPtr.Zero;
            try
            {
                IntPtr marker = Lookup(name);
                if (marker != IntPtr.Zero)
                    BeginSample(marker);
                return marker;
            }
            catch
            {
                Disable();
                return IntPtr.Zero;
            }
        }

        internal static void End(IntPtr marker)
        {
            if (marker == IntPtr.Zero)
                return;
            try
            {
                EndSample(marker);
            }
            catch
            {
                Disable();
            }
        }

        private static void Disable()
        {
            _unavailable = true;
            _active = false;
        }

        private static IntPtr Lookup(string name)
        {
            Dictionary<string, IntPtr> byRef = t_byRef ?? (t_byRef = new Dictionary<string, IntPtr>(ReferenceStringComparer.Instance));
            IntPtr marker;
            if (byRef.TryGetValue(name, out marker))
                return marker;

            Dictionary<string, IntPtr> byName = t_byName ?? (t_byName = new Dictionary<string, IntPtr>(StringComparer.Ordinal));
            if (!byName.TryGetValue(name, out marker))
            {
                if (byName.Count >= MaxMarkersPerThread)
                    return IntPtr.Zero;
                marker = CreateMarker(name);
                byName[name] = marker;
            }
            if (byRef.Count < MaxAliasesPerThread)
                byRef[name] = marker;
            return marker;
        }

        // Unity icalls live in their own methods so a non-Unity host fails inside the
        // callers' try/catch rather than when JIT-compiling them.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ProfilerRecording()
        {
            return UnityEngine.Profiling.Profiler.supported && UnityEngine.Profiling.Profiler.enabled;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IntPtr CreateMarker(string name)
        {
            return Unity.Profiling.LowLevel.Unsafe.ProfilerUnsafeUtility.CreateMarker(
                name, ScriptsCategory, Unity.Profiling.LowLevel.MarkerFlags.Default, 0);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void BeginSample(IntPtr marker)
        {
            Unity.Profiling.LowLevel.Unsafe.ProfilerUnsafeUtility.BeginSample(marker);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void EndSample(IntPtr marker)
        {
            Unity.Profiling.LowLevel.Unsafe.ProfilerUnsafeUtility.EndSample(marker);
        }
    }
}
