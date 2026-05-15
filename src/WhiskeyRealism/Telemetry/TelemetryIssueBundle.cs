using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace WhiskeyRealism.Telemetry
{
    internal sealed class TelemetryIssueBundleManifest
    {
        internal TelemetryIssueBundleManifest(string sessionId, IEnumerable<string> files, string note)
        {
            SessionId = TelemetryEvent.Safe(sessionId);
            Files = new List<string>();
            if (files != null)
            {
                foreach (string file in files)
                    Files.Add(TelemetryIssueBundle.Redact(file));
            }

            Note = TelemetryIssueBundle.Redact(note);
        }

        internal string SessionId { get; private set; }
        internal List<string> Files { get; private set; }
        internal string Note { get; private set; }
    }

    internal static class TelemetryIssueBundle
    {
        private static readonly Regex WindowsUserPath = new Regex(
            @"(?i)\b([A-Z]:\\Users\\)[^\\]+",
            RegexOptions.CultureInvariant);

        private static readonly Regex SecretAssignment = new Regex(
            @"(?i)\b(token|secret)\s*=\s*[^&\s;,""']+",
            RegexOptions.CultureInvariant);

        internal static string Redact(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            string redacted = WindowsUserPath.Replace(value, "$1<redacted>");
            redacted = SecretAssignment.Replace(redacted, m => m.Groups[1].Value + "=<redacted>");
            return redacted;
        }

        internal static TelemetryIssueBundleManifest CreateManifest(string sessionId, IEnumerable<string> files, string note)
        {
            return new TelemetryIssueBundleManifest(sessionId, files, note);
        }
    }
}
