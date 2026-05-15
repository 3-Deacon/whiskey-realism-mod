using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WhiskeyRealism.Telemetry
{
    internal sealed class TelemetryRetentionCandidate
    {
        internal TelemetryRetentionCandidate(string directoryName, DateTime? manifestStartUtc, DateTime lastWriteUtc)
            : this(directoryName, directoryName, manifestStartUtc, lastWriteUtc)
        {
        }

        internal TelemetryRetentionCandidate(string directoryPath, string directoryName, DateTime? manifestStartUtc, DateTime lastWriteUtc)
        {
            DirectoryPath = string.IsNullOrWhiteSpace(directoryPath) ? "-" : directoryPath;
            DirectoryName = string.IsNullOrWhiteSpace(directoryName) ? "-" : directoryName;
            ManifestStartUtc = manifestStartUtc.HasValue ? manifestStartUtc.Value.ToUniversalTime() : (DateTime?)null;
            LastWriteUtc = lastWriteUtc.ToUniversalTime();
        }

        internal string DirectoryPath { get; private set; }
        internal string DirectoryName { get; private set; }
        internal DateTime? ManifestStartUtc { get; private set; }
        internal DateTime LastWriteUtc { get; private set; }

        internal DateTime EffectiveStartUtc
        {
            get { return ManifestStartUtc.HasValue ? ManifestStartUtc.Value : LastWriteUtc; }
        }
    }

    internal static class TelemetrySession
    {
        internal static string CreateSessionId(DateTime startUtc, int pid, string assemblyHash)
        {
            DateTime utc = startUtc.Kind == DateTimeKind.Utc ? startUtc : startUtc.ToUniversalTime();
            string hash = SafeHashPrefix(assemblyHash);
            return utc.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)
                + "-p" + Math.Max(0, pid).ToString(CultureInfo.InvariantCulture)
                + "-" + hash;
        }

        internal static string SessionDirectory(string gameRoot, string sessionId)
        {
            return Path.Combine(
                string.IsNullOrWhiteSpace(gameRoot) ? "." : gameRoot,
                "BepInEx",
                "WhiskeyRealism",
                "tuning-logs",
                TelemetryEvent.Safe(sessionId));
        }

        internal static List<TelemetryRetentionCandidate> OrderRetentionCandidates(IEnumerable<TelemetryRetentionCandidate> candidates)
        {
            var ordered = new List<TelemetryRetentionCandidate>();
            if (candidates != null)
            {
                foreach (var candidate in candidates)
                {
                    if (candidate != null)
                        ordered.Add(candidate);
                }
            }

            ordered.Sort(CompareNewestFirst);
            return ordered;
        }

        internal static List<TelemetryRetentionCandidate> SelectRetentionDeletes(IEnumerable<TelemetryRetentionCandidate> candidates, int keepNewest)
        {
            var ordered = OrderRetentionCandidates(candidates);
            int keep = Math.Max(0, keepNewest);
            var deletes = new List<TelemetryRetentionCandidate>();
            for (int i = keep; i < ordered.Count; i++)
                deletes.Add(ordered[i]);
            return deletes;
        }

        private static int CompareNewestFirst(TelemetryRetentionCandidate left, TelemetryRetentionCandidate right)
        {
            int byStart = SortStartUtc(right, left).CompareTo(SortStartUtc(left, right));
            if (byStart != 0)
                return byStart;

            int byName = string.CompareOrdinal(right.DirectoryName, left.DirectoryName);
            if (byName != 0)
                return byName;

            return right.LastWriteUtc.CompareTo(left.LastWriteUtc);
        }

        private static DateTime SortStartUtc(TelemetryRetentionCandidate candidate, TelemetryRetentionCandidate other)
        {
            if (candidate.ManifestStartUtc.HasValue)
                return candidate.ManifestStartUtc.Value;

            return other.ManifestStartUtc.HasValue ? candidate.LastWriteUtc : DateTime.MinValue;
        }

        private static string SafeHashPrefix(string assemblyHash)
        {
            var builder = new StringBuilder(12);
            if (!string.IsNullOrWhiteSpace(assemblyHash))
            {
                for (int i = 0; i < assemblyHash.Length && builder.Length < 12; i++)
                {
                    char c = assemblyHash[i];
                    if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))
                        builder.Append(char.ToLowerInvariant(c));
                }
            }

            while (builder.Length < 12)
                builder.Append('0');
            return builder.ToString();
        }
    }
}
