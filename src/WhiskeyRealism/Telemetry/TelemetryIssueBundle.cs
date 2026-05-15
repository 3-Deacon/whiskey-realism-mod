using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

        private static readonly Regex WindowsForwardUserPath = new Regex(
            @"(?i)\b([A-Z]:/Users/)[^/]+",
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

        private static readonly Regex SecretColon = new Regex(
            @"(?i)(""?[A-Z0-9_-]*(?:API-KEY|API_KEY|TOKEN|SECRET|PASSWORD)""?|""?api_key""?|""?access_token""?|""?token""?|""?secret""?|""?password""?|""?client-secret""?|""?x-api-key""?)\s*:\s*(?:""[^""]*""|'[^']*'|[^&\s;,""']+)",
            RegexOptions.CultureInvariant);

        private static readonly Regex BearerAuthorization = new Regex(
            @"(?i)\b(Authorization\s*:\s*Bearer)\s+[^&\s;,""']+",
            RegexOptions.CultureInvariant);

        private static readonly Regex BearerAuthorizationEquals = new Regex(
            @"(?i)\b(Authorization\s*=\s*Bearer)\s+[^&\s;,""']+",
            RegexOptions.CultureInvariant);

        private static readonly Regex BasicAuthorization = new Regex(
            @"(?i)\b(Authorization\s*:\s*Basic)\s+[^&\s;,""']+",
            RegexOptions.CultureInvariant);

        internal static string Redact(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            string jsonRedacted;
            if (TryRedactJson(value, out jsonRedacted))
                return jsonRedacted;

            string redacted = WindowsUserPath.Replace(value, "$1<redacted>");
            redacted = WindowsForwardUserPath.Replace(redacted, "$1<redacted>");
            redacted = MntWindowsUserPath.Replace(redacted, "$1<redacted>");
            redacted = HomeUserPath.Replace(redacted, "$1<redacted>");
            redacted = SecretAssignment.Replace(redacted, m => m.Groups[1].Value + "=<redacted>");
            redacted = SecretColon.Replace(redacted, m => m.Groups[1].Value + ":<redacted>");
            redacted = BearerAuthorization.Replace(redacted, "$1 <redacted>");
            redacted = BearerAuthorizationEquals.Replace(redacted, "$1 <redacted>");
            redacted = BasicAuthorization.Replace(redacted, "$1 <redacted>");
            return redacted;
        }

        private static bool TryRedactJson(string value, out string redacted)
        {
            redacted = null;
            string trimmed = value.Trim();
            if (!(trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal)))
                return false;

            try
            {
                JToken token = JToken.Parse(value);
                RedactJsonToken(token);
                redacted = token.ToString(Formatting.None);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static void RedactJsonToken(JToken token)
        {
            var obj = token as JObject;
            if (obj != null)
            {
                foreach (var property in obj.Properties())
                {
                    if (IsSecretKey(property.Name) && property.Value.Type != JTokenType.Object && property.Value.Type != JTokenType.Array)
                        property.Value = "<redacted>";
                    else
                        RedactJsonToken(property.Value);
                }

                return;
            }

            var array = token as JArray;
            if (array != null)
            {
                foreach (JToken item in array)
                    RedactJsonToken(item);
            }
        }

        private static bool IsSecretKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            string normalized = key.Replace("-", "_").ToUpperInvariant();
            return normalized == "TOKEN"
                || normalized == "SECRET"
                || normalized == "PASSWORD"
                || normalized == "API_KEY"
                || normalized == "X_API_KEY"
                || normalized == "CLIENT_SECRET"
                || normalized == "ACCESS_TOKEN"
                || normalized.EndsWith("_TOKEN", StringComparison.Ordinal)
                || normalized.EndsWith("_SECRET", StringComparison.Ordinal)
                || normalized.EndsWith("_PASSWORD", StringComparison.Ordinal)
                || normalized.EndsWith("_API_KEY", StringComparison.Ordinal);
        }

        internal static TelemetryIssueBundleManifest CreateManifest(string sessionId, IEnumerable<string> files, string note)
        {
            return new TelemetryIssueBundleManifest(sessionId, files, note);
        }
    }
}
