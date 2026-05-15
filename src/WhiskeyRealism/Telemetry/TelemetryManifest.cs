using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace WhiskeyRealism.Telemetry
{
    internal sealed class TelemetryManifest
    {
        internal TelemetryManifest()
        {
            ConfigSnapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            OutputFiles = new List<string>();
            DroppedCounters = new Dictionary<string, long>(StringComparer.Ordinal);
        }

        internal string Schema
        {
            get { return "wr.telemetry.manifest.v1"; }
        }

        internal string SessionId { get; set; }
        internal string PluginVersion { get; set; }
        internal string RuntimeAssemblySha256 { get; set; }
        internal TelemetryProfile Profile { get; set; }
        internal DateTime StartUtc { get; set; }
        internal DateTime? EndUtc { get; set; }
        internal Dictionary<string, string> ConfigSnapshot { get; private set; }
        internal List<string> OutputFiles { get; private set; }
        internal long TotalCapBytes { get; set; }
        internal long RotateBytes { get; set; }
        internal long EmittedBytes { get; set; }
        internal int RotationIndex { get; set; }
        internal Dictionary<string, long> DroppedCounters { get; private set; }
        internal int UnflushedCount { get; set; }

        internal string ToJson()
        {
            return JsonConvert.SerializeObject(new
            {
                schema = Schema,
                sessionId = SessionId,
                pluginVersion = PluginVersion,
                runtimeAssemblySha256 = RuntimeAssemblySha256,
                profile = Profile.ToString(),
                startUtc = StartUtc.ToUniversalTime(),
                endUtc = EndUtc.HasValue ? EndUtc.Value.ToUniversalTime() : (DateTime?)null,
                configSnapshot = ConfigSnapshot,
                outputFiles = OutputFiles,
                totalCapBytes = TotalCapBytes,
                rotateBytes = RotateBytes,
                emittedBytes = EmittedBytes,
                rotationIndex = RotationIndex,
                droppedCounters = DroppedCounters,
                unflushedCount = UnflushedCount
            }, Formatting.Indented);
        }

        internal static TelemetryManifest Create(
            string sessionId,
            string pluginVersion,
            string runtimeAssemblySha256,
            TelemetryProfile profile,
            DateTime startUtc,
            TelemetryBudget budget,
            int unflushedCount)
        {
            var manifest = new TelemetryManifest
            {
                SessionId = TelemetryEvent.Safe(sessionId),
                PluginVersion = TelemetryEvent.Safe(pluginVersion),
                RuntimeAssemblySha256 = TelemetryEvent.Safe(runtimeAssemblySha256),
                Profile = profile,
                StartUtc = startUtc.ToUniversalTime(),
                TotalCapBytes = budget != null ? budget.TotalBytes : 0L,
                RotateBytes = budget != null ? budget.RotateBytes : 0L,
                EmittedBytes = budget != null ? budget.EmittedBytes : 0L,
                RotationIndex = budget != null ? budget.RotationIndex : 0,
                UnflushedCount = Math.Max(0, unflushedCount)
            };

            if (budget != null)
            {
                foreach (var entry in budget.DroppedSnapshot())
                    manifest.DroppedCounters[entry.Key] = entry.Value;
            }

            return manifest;
        }
    }
}
