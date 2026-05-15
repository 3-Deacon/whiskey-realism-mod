using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WhiskeyRealism.Telemetry
{
    internal static class TelemetryLegacyParser
    {
        internal static bool TryParse(string line, TelemetryProfile profile, string sessionId, out TelemetryEvent ev)
        {
            return TryParse(line, profile, sessionId, null, out ev);
        }

        internal static bool TryParse(
            string line,
            TelemetryProfile profile,
            string sessionId,
            TelemetrySeverity? severityOverride,
            out TelemetryEvent ev)
        {
            ev = null;
            try
            {
                var route = TelemetryTagPolicy.Route(line);
                if (!route.RouteToSidecar)
                    return false;

                Dictionary<string, string> fields = ParseKeyValues(line);
                ev = TelemetryEvent.Create(
                    sessionId,
                    profile,
                    route.Layer,
                    route.Category,
                    route.EventName,
                    severityOverride.HasValue ? severityOverride.Value : route.Severity);

                ApplyKnownFields(ev, fields);
                foreach (var pair in fields)
                    ApplyField(ev, pair.Key, pair.Value);

                string signature = BuildInputSignature(route.EventName, fields);
                if (route.Category == TelemetryCategory.Decision)
                {
                    ev.WithDecision(
                        Get(fields, "decision", route.EventName),
                        Get(fields, "reason", "legacy"),
                        signature);
                }
                else
                {
                    ev.WithField("inputSignature", signature);
                    ev.WithField("inputSignatureSource", "coarse");
                }

                return true;
            }
            catch
            {
                ev = null;
                return false;
            }
        }

        private static Dictionary<string, string> ParseKeyValues(string line)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(line))
                return fields;

            int i = 0;
            while (i < line.Length)
            {
                while (i < line.Length && char.IsWhiteSpace(line[i]))
                    i++;

                int keyStart = i;
                while (i < line.Length && IsKeyChar(line[i]))
                    i++;

                if (keyStart == i || i >= line.Length || line[i] != '=')
                {
                    i = Math.Max(i + 1, keyStart + 1);
                    continue;
                }

                string key = line.Substring(keyStart, i - keyStart).Trim();
                i++;

                string value;
                if (i < line.Length && line[i] == '"')
                {
                    i++;
                    var builder = new StringBuilder();
                    while (i < line.Length)
                    {
                        char c = line[i++];
                        if (c == '"')
                            break;
                        if (c == '\\' && i < line.Length)
                        {
                            builder.Append(line[i++]);
                            continue;
                        }
                        builder.Append(c);
                    }
                    value = builder.ToString();
                }
                else
                {
                    int valueStart = i;
                    while (i < line.Length && !char.IsWhiteSpace(line[i]))
                        i++;
                    value = line.Substring(valueStart, i - valueStart);
                }

                if (!string.IsNullOrWhiteSpace(key))
                    fields[key] = TelemetryEvent.Safe(value);
            }

            return fields;
        }

        private static bool IsKeyChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == ':';
        }

        private static void ApplyKnownFields(TelemetryEvent ev, Dictionary<string, string> fields)
        {
            ev.WithBattleId(Get(fields, "battle", Get(fields, "battleId", "-")));
            ev.WithCampaignDate(Get(fields, "campaignDate", Get(fields, "date", "-")));
            ev.WithSide(ParseInt(Get(fields, "side", "-"), -1));
            ev.WithAlliance(ParseInt(Get(fields, "alliance", "-"), -1));
            ev.WithUnit(Get(fields, "unit", Get(fields, "unitId", "-")));
            ev.WithPhase(Get(fields, "phase", "-"));

            string duration = Get(fields, "durationMs", Get(fields, "duration", null));
            double durationMs;
            if (duration != null && double.TryParse(duration, NumberStyles.Float, CultureInfo.InvariantCulture, out durationMs))
                ev.WithDurationMs(durationMs);
        }

        private static void ApplyField(TelemetryEvent ev, string key, string value)
        {
            bool boolValue;
            int intValue;
            double doubleValue;

            if (bool.TryParse(value, out boolValue))
                ev.WithField(key, boolValue);
            else if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
                ev.WithField(key, intValue);
            else if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValue))
                ev.WithField(key, doubleValue);
            else
                ev.WithField(key, value);
        }

        private static int ParseInt(string value, int fallback)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : fallback;
        }

        private static string Get(Dictionary<string, string> fields, string key, string fallback)
        {
            string value;
            return fields.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : fallback;
        }

        private static string BuildInputSignature(string eventName, Dictionary<string, string> fields)
        {
            var builder = new StringBuilder(TelemetryEvent.Safe(eventName));
            foreach (var key in SortedKeys(fields))
            {
                if (IsDecisionOutputKey(key))
                    continue;

                builder.Append('|');
                builder.Append(key);
                builder.Append('=');
                builder.Append(TelemetryEvent.Safe(fields[key]));
            }

            if (builder.Length == 0)
                return "legacy";

            return builder.ToString();
        }

        private static bool IsDecisionOutputKey(string key)
        {
            return string.Equals(key, "decision", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "reason", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "inputSignature", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "inputSignatureSource", StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> SortedKeys(Dictionary<string, string> fields)
        {
            var keys = new List<string>(fields.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            return keys;
        }
    }
}
