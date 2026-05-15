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
            : this(sessionId, null, files, note)
        {
        }

        internal TelemetryIssueBundleManifest(string sessionId, string sessionDirectory, IEnumerable<string> files, string note)
        {
            SessionId = TelemetryIssueBundle.Redact(TelemetryEvent.Safe(sessionId));
            Files = new List<string>();
            if (files != null)
            {
                foreach (string file in files)
                {
                    if (TelemetryIssueBundle.IsTelemetryOwnedFile(file, sessionDirectory))
                        Files.Add(TelemetryIssueBundle.Redact(file));
                }
            }

            Note = TelemetryIssueBundle.Redact(note);
        }

        internal string SessionId { get; private set; }
        internal List<string> Files { get; private set; }
        internal string Note { get; private set; }

        internal string ToJson()
        {
            return JsonConvert.SerializeObject(
                new
                {
                    sessionId = SessionId,
                    files = Files,
                    note = Note
                },
                Formatting.None);
        }
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
            @"(?i)\b([A-Z0-9_-]*(?:API-KEY|API_KEY|TOKEN|SECRET|PASSWORD)|api_key|access_token|token|secret|password|client-secret|x-api-key)\s*=\s*(?:""[^""]*""|'[^']*'|[^&\s;,""']+)",
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

        private static readonly Regex BasicAuthorizationEquals = new Regex(
            @"(?i)\b(Authorization\s*=\s*Basic)\s+[^&\s;,""']+",
            RegexOptions.CultureInvariant);

        private static readonly Regex RotatedTelemetryFile = new Regex(
            @"(?i)^(?:health|failures|performance|tactical|campaign)\.[0-9]{3}\.jsonl$",
            RegexOptions.CultureInvariant);

        private static readonly HashSet<string> TelemetryBundleFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "manifest.json",
            "summary.md",
            "issue-bundle.json",
            "health.jsonl",
            "failures.jsonl",
            "performance.jsonl",
            "tactical.jsonl",
            "campaign.jsonl"
        };

        internal static string Redact(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            string jsonRedacted;
            if (TryRedactJson(value, out jsonRedacted))
                return jsonRedacted;

            return RedactFreeText(value);
        }

        private static string RedactFreeText(string value)
        {
            string redacted = WindowsUserPath.Replace(value, "$1<redacted>");
            redacted = WindowsForwardUserPath.Replace(redacted, "$1<redacted>");
            redacted = MntWindowsUserPath.Replace(redacted, "$1<redacted>");
            redacted = HomeUserPath.Replace(redacted, "$1<redacted>");
            redacted = SecretAssignment.Replace(redacted, m => m.Groups[1].Value + "=<redacted>");
            redacted = SecretColon.Replace(redacted, m => m.Groups[1].Value + ":<redacted>");
            redacted = BearerAuthorization.Replace(redacted, "$1 <redacted>");
            redacted = BearerAuthorizationEquals.Replace(redacted, "$1 <redacted>");
            redacted = BasicAuthorization.Replace(redacted, "$1 <redacted>");
            redacted = BasicAuthorizationEquals.Replace(redacted, "$1 <redacted>");
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
                token = RedactJsonToken(token, null);
                redacted = token.ToString(Formatting.None);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static JToken RedactJsonToken(JToken token, string key)
        {
            var obj = token as JObject;
            if (obj != null)
            {
                foreach (var property in obj.Properties())
                    property.Value = RedactJsonToken(property.Value, property.Name);

                return obj;
            }

            var array = token as JArray;
            if (array != null)
            {
                for (int i = 0; i < array.Count; i++)
                    array[i] = RedactJsonToken(array[i], null);

                return array;
            }

            if (IsSecretKey(key) && token.Type != JTokenType.Object && token.Type != JTokenType.Array)
                return new JValue("<redacted>");

            if (IsAuthorizationKey(key) && token.Type != JTokenType.Object && token.Type != JTokenType.Array)
                return new JValue(RedactAuthorizationScalar(token.Type == JTokenType.String ? (string)token : token.ToString(Formatting.None)));

            if (token.Type == JTokenType.String)
                return new JValue(RedactFreeText((string)token));

            return token;
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

        private static bool IsAuthorizationKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            string normalized = key.Replace("-", "_").ToUpperInvariant();
            return normalized == "AUTHORIZATION" || normalized == "AUTHORIZATION_HEADER";
        }

        private static string RedactAuthorizationScalar(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "<redacted>";

            if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return "Bearer <redacted>";

            if (value.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                return "Basic <redacted>";

            return "<redacted>";
        }

        internal static bool IsTelemetryOwnedFile(string file)
        {
            return false;
        }

        internal static bool IsTelemetryOwnedFile(string file, string sessionDirectory)
        {
            if (string.IsNullOrWhiteSpace(file))
                return false;

            string normalized = NormalizeSessionDirectory(file);
            string sessionRoot = NormalizeSessionDirectory(sessionDirectory);
            if (string.IsNullOrWhiteSpace(sessionRoot))
                return false;

            if (!normalized.StartsWith(sessionRoot + "/", StringComparison.OrdinalIgnoreCase))
                return false;

            string fileName = normalized.Substring(sessionRoot.Length + 1);
            if (fileName.IndexOf('/') >= 0)
                return false;

            if (!(TelemetryBundleFiles.Contains(fileName) || RotatedTelemetryFile.IsMatch(fileName)))
                return false;

            return true;
        }

        private static string NormalizeSessionDirectory(string sessionDirectory)
        {
            if (string.IsNullOrWhiteSpace(sessionDirectory))
                return string.Empty;

            string normalized = sessionDirectory.Trim().Replace('\\', '/');
            if (normalized.Length >= 3 && normalized[1] == ':' && normalized[2] == '/')
            {
                char drive = char.ToLowerInvariant(normalized[0]);
                if (drive >= 'a' && drive <= 'z')
                    normalized = "/mnt/" + drive + normalized.Substring(2);
            }

            while (normalized.IndexOf("//", StringComparison.Ordinal) >= 0)
                normalized = normalized.Replace("//", "/");

            return normalized.TrimEnd('/');
        }

        internal static TelemetryIssueBundleManifest CreateManifest(string sessionId, IEnumerable<string> files, string note)
        {
            return new TelemetryIssueBundleManifest(sessionId, files, note);
        }

        internal static TelemetryIssueBundleManifest CreateManifest(string sessionId, string sessionDirectory, IEnumerable<string> files, string note)
        {
            return new TelemetryIssueBundleManifest(sessionId, sessionDirectory, files, note);
        }
    }
}
