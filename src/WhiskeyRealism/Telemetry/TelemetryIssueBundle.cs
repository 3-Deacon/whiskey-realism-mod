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

        private static readonly Regex MntWindowsUserPath = new Regex(
            @"(?i)(/mnt/[a-z]/Users/)[^/]+",
            RegexOptions.CultureInvariant);

        private static readonly Regex HomeUserPath = new Regex(
            @"(?i)(/home/)[^/\s]+",
            RegexOptions.CultureInvariant);

        private static readonly Regex SecretAssignment = new Regex(
            @"(?i)\b([A-Z0-9_]*(?:API_KEY|TOKEN|SECRET|PASSWORD)|api_key|access_token|token|secret|password)\s*=\s*(?:""[^""]*""|'[^']*'|[^&\s;,""']+)",
            RegexOptions.CultureInvariant);

        private static readonly Regex BearerAuthorization = new Regex(
            @"(?i)\b(Authorization\s*:\s*Bearer)\s+[^&\s;,""']+",
            RegexOptions.CultureInvariant);

        internal static string Redact(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            string redacted = WindowsUserPath.Replace(value, "$1<redacted>");
            redacted = MntWindowsUserPath.Replace(redacted, "$1<redacted>");
            redacted = HomeUserPath.Replace(redacted, "$1<redacted>");
            redacted = SecretAssignment.Replace(redacted, m => m.Groups[1].Value + "=<redacted>");
            redacted = BearerAuthorization.Replace(redacted, "$1 <redacted>");
            return redacted;
        }

        internal static TelemetryIssueBundleManifest CreateManifest(string sessionId, IEnumerable<string> files, string note)
        {
            return new TelemetryIssueBundleManifest(sessionId, files, note);
        }
    }
}
