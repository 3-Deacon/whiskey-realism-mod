using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WhiskeyRealism.Telemetry
{
    internal static class TelemetryJson
    {
        internal static string ToJsonLine(TelemetryEvent ev)
        {
            if (ev == null)
            {
                ev = TelemetryEvent.Create(
                    "-", TelemetryProfile.Off, TelemetryLayer.System,
                    TelemetryCategory.Health, "-", TelemetrySeverity.Debug);
            }

            var builder = new StringBuilder(384);
            builder.Append('{');
            AppendString(builder, "schema", TelemetryEvent.Schema, first: true);
            AppendString(builder, "ts", ev.Utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
            AppendString(builder, "sessionId", ev.SessionId);
            AppendString(builder, "profile", ev.Profile.ToString());
            AppendString(builder, "layer", ev.Layer.ToString());
            AppendString(builder, "category", ev.Category.ToString());
            AppendString(builder, "event", ev.EventName);
            AppendString(builder, "severity", ev.Severity.ToString());
            AppendString(builder, "campaignDate", ev.CampaignDate);
            AppendString(builder, "battleId", ev.BattleId);
            AppendNumber(builder, "side", ev.Side);
            AppendNumber(builder, "alliance", ev.Alliance);
            AppendString(builder, "unit", ev.Unit);
            AppendString(builder, "phase", ev.Phase);
            AppendNumber(builder, "durationMs", ev.DurationMs);
            AppendString(builder, "decision", ev.Decision);
            AppendString(builder, "reason", ev.DecisionReason);
            AppendString(builder, "inputSignature", ev.InputSignature);
            builder.Append(",\"fields\":");
            AppendFields(builder, ev.Fields);
            builder.Append('}');
            builder.Append('\n');
            return builder.ToString();
        }

        private static void AppendFields(StringBuilder builder, TelemetryFields fields)
        {
            builder.Append('{');
            bool first = true;
            if (fields != null)
            {
                foreach (KeyValuePair<string, TelemetryFieldValue> entry in fields.Entries)
                {
                    if (!first)
                        builder.Append(',');
                    first = false;

                    AppendQuoted(builder, entry.Key);
                    builder.Append(':');
                    AppendValue(builder, entry.Value);
                }
            }
            builder.Append('}');
        }

        private static void AppendValue(StringBuilder builder, TelemetryFieldValue value)
        {
            switch (value.Kind)
            {
                case TelemetryFieldKind.Int:
                    builder.Append(value.IntValue.ToString(CultureInfo.InvariantCulture));
                    break;
                case TelemetryFieldKind.Bool:
                    builder.Append(value.BoolValue ? "true" : "false");
                    break;
                case TelemetryFieldKind.Double:
                    builder.Append(TelemetryFields.SanitizeNumber(value.DoubleValue).ToString("R", CultureInfo.InvariantCulture));
                    break;
                default:
                    AppendQuoted(builder, value.StringValue);
                    break;
            }
        }

        private static void AppendString(StringBuilder builder, string name, string value, bool first = false)
        {
            if (!first)
                builder.Append(',');
            AppendQuoted(builder, name);
            builder.Append(':');
            AppendQuoted(builder, TelemetryEvent.Safe(value));
        }

        private static void AppendNumber(StringBuilder builder, string name, int value)
        {
            builder.Append(',');
            AppendQuoted(builder, name);
            builder.Append(':');
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendNumber(StringBuilder builder, string name, double value)
        {
            builder.Append(',');
            AppendQuoted(builder, name);
            builder.Append(':');
            builder.Append(TelemetryFields.SanitizeNumber(value).ToString("R", CultureInfo.InvariantCulture));
        }

        private static void AppendQuoted(StringBuilder builder, string value)
        {
            builder.Append('"');
            AppendEscaped(builder, TelemetryEvent.Safe(value));
            builder.Append('"');
        }

        private static void AppendEscaped(StringBuilder builder, string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '/':
                        builder.Append("\\/");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }
        }
    }
}
