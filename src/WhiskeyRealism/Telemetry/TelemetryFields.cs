using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Telemetry
{
    internal sealed class TelemetryFields
    {
        private readonly SortedDictionary<string, TelemetryFieldValue> _fields =
            new SortedDictionary<string, TelemetryFieldValue>(StringComparer.Ordinal);

        internal IEnumerable<KeyValuePair<string, TelemetryFieldValue>> Entries
        {
            get { return _fields; }
        }

        internal TelemetryFields Add(string key, string value)
        {
            _fields[SafeKey(key)] = TelemetryFieldValue.ForString(TelemetryEvent.Safe(value));
            return this;
        }

        internal TelemetryFields Add(string key, int value)
        {
            _fields[SafeKey(key)] = TelemetryFieldValue.ForInt(value);
            return this;
        }

        internal TelemetryFields Add(string key, bool value)
        {
            _fields[SafeKey(key)] = TelemetryFieldValue.ForBool(value);
            return this;
        }

        internal TelemetryFields Add(string key, double value)
        {
            bool invalid = double.IsNaN(value) || double.IsInfinity(value);
            _fields[SafeKey(key)] = TelemetryFieldValue.ForDouble(invalid ? 0.0 : value);
            if (invalid)
                Add("invalidFloat", true);
            return this;
        }

        internal string GetString(string key)
        {
            TelemetryFieldValue value;
            if (!_fields.TryGetValue(SafeKey(key), out value))
                return "-";

            return value.Kind == TelemetryFieldKind.String ? value.StringValue : "-";
        }

        internal double GetDouble(string key)
        {
            TelemetryFieldValue value;
            if (!_fields.TryGetValue(SafeKey(key), out value))
                return 0.0;

            if (value.Kind == TelemetryFieldKind.Double) return value.DoubleValue;
            if (value.Kind == TelemetryFieldKind.Int) return value.IntValue;
            return 0.0;
        }

        internal bool GetBool(string key)
        {
            TelemetryFieldValue value;
            if (!_fields.TryGetValue(SafeKey(key), out value))
                return false;

            return value.Kind == TelemetryFieldKind.Bool && value.BoolValue;
        }

        internal static double SanitizedNumber(double value)
        {
            return SanitizeNumber(value);
        }

        internal static double SanitizeNumber(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : value;
        }

        private static string SafeKey(string key)
        {
            return string.IsNullOrWhiteSpace(key) ? "-" : key.Trim();
        }
    }

    internal enum TelemetryFieldKind
    {
        String = 0,
        Int = 1,
        Bool = 2,
        Double = 3
    }

    internal struct TelemetryFieldValue
    {
        internal TelemetryFieldKind Kind;
        internal string StringValue;
        internal int IntValue;
        internal bool BoolValue;
        internal double DoubleValue;

        internal static TelemetryFieldValue ForString(string value)
        {
            return new TelemetryFieldValue { Kind = TelemetryFieldKind.String, StringValue = value };
        }

        internal static TelemetryFieldValue ForInt(int value)
        {
            return new TelemetryFieldValue { Kind = TelemetryFieldKind.Int, IntValue = value };
        }

        internal static TelemetryFieldValue ForBool(bool value)
        {
            return new TelemetryFieldValue { Kind = TelemetryFieldKind.Bool, BoolValue = value };
        }

        internal static TelemetryFieldValue ForDouble(double value)
        {
            return new TelemetryFieldValue { Kind = TelemetryFieldKind.Double, DoubleValue = value };
        }
    }
}
