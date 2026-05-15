using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WhiskeyRealism.Telemetry
{
    internal sealed class TelemetrySessionDirectory
    {
        internal TelemetrySessionDirectory(string sessionId, string directoryPath)
        {
            SessionId = string.IsNullOrWhiteSpace(sessionId) ? "session" : sessionId;
            DirectoryPath = string.IsNullOrWhiteSpace(directoryPath) ? "." : directoryPath;
        }

        internal string SessionId { get; private set; }
        internal string DirectoryPath { get; private set; }
    }

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
            return ContainedSessionDirectory(gameRoot, SafeSessionDirectoryName(sessionId));
        }

        internal static string SafeSessionDirectoryName(string sessionId)
        {
            string raw = string.IsNullOrWhiteSpace(sessionId) ? "session" : sessionId.Trim();
            if (raw == "." || raw == "..")
                return "session";

            var builder = new StringBuilder(raw.Length);
            bool lastWasDash = false;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                bool safe =
                    (c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'z')
                    || (c >= 'A' && c <= 'Z')
                    || c == '-'
                    || c == '_';

                if (safe)
                {
                    builder.Append(c);
                    lastWasDash = c == '-';
                }
                else if (!lastWasDash)
                {
                    builder.Append('-');
                    lastWasDash = true;
                }
            }

            string safeName = builder.ToString().Trim('-', '_');
            if (string.IsNullOrWhiteSpace(safeName) || safeName == "." || safeName == "..")
                return "session";

            return safeName;
        }

        internal static string TuningLogRoot(string gameRoot)
        {
            return Path.Combine(
                string.IsNullOrWhiteSpace(gameRoot) ? "." : gameRoot,
                "BepInEx",
                "WhiskeyRealism",
                "tuning-logs");
        }

        internal static string CreateSessionDirectory(string gameRoot, string sessionId)
        {
            string directory = SessionDirectory(gameRoot, sessionId);
            Directory.CreateDirectory(directory);
            return directory;
        }

        internal static TelemetrySessionDirectory CreateUniqueSessionDirectory(string gameRoot, string baseSessionId)
        {
            string safeBase = SafeSessionDirectoryName(baseSessionId);
            for (int attempt = 0; attempt < 1000; attempt++)
            {
                string sessionId = attempt == 0
                    ? safeBase
                    : safeBase + "-r" + attempt.ToString("000", CultureInfo.InvariantCulture);
                string directory = ContainedSessionDirectory(gameRoot, sessionId);
                if (Directory.Exists(directory))
                    continue;

                Directory.CreateDirectory(directory);
                return new TelemetrySessionDirectory(sessionId, directory);
            }

            throw new IOException("Unable to allocate a unique telemetry session directory.");
        }

        internal static List<TelemetryRetentionCandidate> ScanRetentionCandidates(string gameRoot)
        {
            var candidates = new List<TelemetryRetentionCandidate>();
            string root = TuningLogRoot(gameRoot);
            if (!Directory.Exists(root))
                return candidates;

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(root);
            }
            catch
            {
                return candidates;
            }

            foreach (string directory in directories)
            {
                string name = Path.GetFileName(directory);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                candidates.Add(new TelemetryRetentionCandidate(
                    directory,
                    name,
                    TryReadManifestStartUtc(Path.Combine(directory, "manifest.json")),
                    SafeDirectoryLastWriteUtc(directory)));
            }

            return candidates;
        }

        internal static List<TelemetryRetentionCandidate> ApplyRetention(string gameRoot, string currentSessionId, int keepNewest)
        {
            return DeleteRetentionCandidates(gameRoot, SelectRetentionDeletes(
                ScanRetentionCandidates(gameRoot),
                keepNewest,
                currentSessionId));
        }

        internal static List<TelemetryRetentionCandidate> DeleteRetentionCandidates(IEnumerable<TelemetryRetentionCandidate> candidates)
        {
            // Rootless deletion cannot prove tuning-log containment; callers must use the root-scoped overload.
            return new List<TelemetryRetentionCandidate>();
        }

        internal static List<TelemetryRetentionCandidate> DeleteRetentionCandidates(string gameRoot, IEnumerable<TelemetryRetentionCandidate> candidates)
        {
            var deleted = new List<TelemetryRetentionCandidate>();
            if (candidates == null)
                return deleted;

            string root = CanonicalTuningLogRoot(gameRoot);
            foreach (var candidate in candidates)
            {
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.DirectoryPath) || !Directory.Exists(candidate.DirectoryPath))
                    continue;

                string candidatePath;
                try
                {
                    candidatePath = Path.GetFullPath(candidate.DirectoryPath);
                }
                catch
                {
                    continue;
                }

                if (!IsUnderDirectory(candidatePath, root) || string.Equals(NormalizeDirectory(candidatePath), NormalizeDirectory(root), StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    Directory.Delete(candidatePath, true);
                    deleted.Add(candidate);
                }
                catch
                {
                }
            }

            return deleted;
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

        internal static List<TelemetryRetentionCandidate> SelectRetentionDeletes(IEnumerable<TelemetryRetentionCandidate> candidates, int keepNewest, string currentSessionId)
        {
            var ordered = OrderRetentionCandidates(candidates);
            string currentName = SafeSessionDirectoryName(currentSessionId);
            var retained = new HashSet<TelemetryRetentionCandidate>();
            int keep = Math.Max(string.IsNullOrWhiteSpace(currentName) ? 0 : 1, keepNewest);

            if (!string.IsNullOrWhiteSpace(currentName))
            {
                foreach (var candidate in ordered)
                {
                    if (string.Equals(candidate.DirectoryName, currentName, StringComparison.OrdinalIgnoreCase))
                    {
                        retained.Add(candidate);
                        break;
                    }
                }
            }

            foreach (var candidate in ordered)
            {
                if (retained.Count >= keep)
                    break;

                retained.Add(candidate);
            }

            var deletes = new List<TelemetryRetentionCandidate>();
            foreach (var candidate in ordered)
            {
                if (!retained.Contains(candidate))
                    deletes.Add(candidate);
            }

            return deletes;
        }

        private static int CompareNewestFirst(TelemetryRetentionCandidate left, TelemetryRetentionCandidate right)
        {
            if (left.ManifestStartUtc.HasValue && right.ManifestStartUtc.HasValue)
            {
                int byStart = right.ManifestStartUtc.Value.CompareTo(left.ManifestStartUtc.Value);
                if (byStart != 0)
                    return byStart;
            }

            int byName = string.CompareOrdinal(right.DirectoryName, left.DirectoryName);
            if (byName != 0)
                return byName;

            return right.LastWriteUtc.CompareTo(left.LastWriteUtc);
        }

        private static string ContainedSessionDirectory(string gameRoot, string safeSessionName)
        {
            string root = CanonicalTuningLogRoot(gameRoot);
            string directory = Path.GetFullPath(Path.Combine(root, safeSessionName));
            if (!IsUnderDirectory(directory, root) || string.Equals(NormalizeDirectory(directory), NormalizeDirectory(root), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Telemetry session directory escaped the tuning log root.");

            return directory;
        }

        private static string CanonicalTuningLogRoot(string gameRoot)
        {
            return Path.GetFullPath(TuningLogRoot(gameRoot));
        }

        private static bool IsUnderDirectory(string path, string root)
        {
            string fullPath = NormalizeDirectory(path);
            string fullRoot = NormalizeDirectory(root);
            return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeDirectory(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static DateTime SafeDirectoryLastWriteUtc(string directory)
        {
            try
            {
                return Directory.GetLastWriteTimeUtc(directory);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private static DateTime? TryReadManifestStartUtc(string manifestPath)
        {
            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
                return null;

            try
            {
                JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
                JToken token = manifest["startUtc"];
                if (token == null || token.Type == JTokenType.Null)
                    return null;

                if (token.Type == JTokenType.Date)
                    return token.Value<DateTime>().ToUniversalTime();

                DateTime parsed;
                if (DateTime.TryParse(
                    token.Type == JTokenType.String ? (string)token : token.ToString(Formatting.None),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out parsed))
                    return parsed.ToUniversalTime();
            }
            catch
            {
            }

            return null;
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
