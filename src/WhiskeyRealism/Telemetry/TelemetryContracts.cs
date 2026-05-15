using System;

namespace WhiskeyRealism.Telemetry
{
    internal enum TelemetryProfile
    {
        Off = 0,
        TacticalTuning = 1,
        CampaignTuning = 2,
        FullTuning = 3
    }

    internal enum TelemetryLayer
    {
        System = 0,
        Tactical = 1,
        Campaign = 2
    }

    internal enum TelemetryCategory
    {
        Health = 0,
        Failure = 1,
        Performance = 2,
        Decision = 3,
        Gate = 4,
        Write = 5,
        State = 6,
        Trace = 7
    }

    internal enum TelemetrySeverity
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }

    internal sealed class TelemetryEvent
    {
        internal const string Schema = "wr.telemetry.v1";

        private TelemetryEvent(
            string sessionId,
            TelemetryProfile profile,
            TelemetryLayer layer,
            TelemetryCategory category,
            string eventName,
            TelemetrySeverity severity)
        {
            Utc = DateTime.UtcNow;
            SessionId = Safe(sessionId);
            Profile = profile;
            Layer = layer;
            Category = category;
            EventName = Safe(eventName);
            Severity = severity;
            CampaignDate = "-";
            BattleId = "-";
            Unit = "-";
            Phase = "-";
            Decision = "-";
            DecisionReason = "-";
            InputSignature = "-";
            Side = -1;
            Alliance = -1;
            DurationMs = 0.0;
            Fields = new TelemetryFields();
        }

        internal string SessionId { get; private set; }
        internal DateTime Utc { get; private set; }
        internal TelemetryProfile Profile { get; private set; }
        internal TelemetryLayer Layer { get; private set; }
        internal TelemetryCategory Category { get; private set; }
        internal string EventName { get; private set; }
        internal TelemetrySeverity Severity { get; private set; }
        internal string CampaignDate { get; private set; }
        internal string BattleId { get; private set; }
        internal int Side { get; private set; }
        internal int Alliance { get; private set; }
        internal string Unit { get; private set; }
        internal string Phase { get; private set; }
        internal double DurationMs { get; private set; }
        internal string Decision { get; private set; }
        internal string DecisionReason { get; private set; }
        internal string InputSignature { get; private set; }
        internal TelemetryFields Fields { get; private set; }

        internal static TelemetryEvent Create(
            string sessionId,
            TelemetryProfile profile,
            TelemetryLayer layer,
            TelemetryCategory category,
            string eventName,
            TelemetrySeverity severity)
        {
            return new TelemetryEvent(sessionId, profile, layer, category, eventName, severity);
        }

        internal TelemetryEvent WithBattleId(string battleId)
        {
            BattleId = Safe(battleId);
            return this;
        }

        internal TelemetryEvent WithCampaignDate(string campaignDate)
        {
            CampaignDate = Safe(campaignDate);
            return this;
        }

        internal TelemetryEvent WithSide(int side)
        {
            Side = side;
            return this;
        }

        internal TelemetryEvent WithAlliance(int alliance)
        {
            Alliance = alliance;
            return this;
        }

        internal TelemetryEvent WithUnit(string unit)
        {
            Unit = Safe(unit);
            return this;
        }

        internal TelemetryEvent WithPhase(string phase)
        {
            Phase = Safe(phase);
            return this;
        }

        internal TelemetryEvent WithDurationMs(double durationMs)
        {
            bool invalid = double.IsNaN(durationMs) || double.IsInfinity(durationMs);
            DurationMs = invalid ? 0.0 : durationMs;
            if (invalid)
                Fields.Add("invalidFloat", true);
            return this;
        }

        internal TelemetryEvent WithField(string key, string value)
        {
            Fields.Add(key, value);
            return this;
        }

        internal TelemetryEvent WithField(string key, int value)
        {
            Fields.Add(key, value);
            return this;
        }

        internal TelemetryEvent WithField(string key, bool value)
        {
            Fields.Add(key, value);
            return this;
        }

        internal TelemetryEvent WithField(string key, double value)
        {
            Fields.Add(key, value);
            return this;
        }

        internal TelemetryEvent WithDecision(string decision, string reason, string inputSignature)
        {
            if (string.IsNullOrWhiteSpace(inputSignature))
                throw new ArgumentException("Decision telemetry requires a nonblank input signature.", nameof(inputSignature));

            Decision = Safe(decision);
            DecisionReason = Safe(reason);
            InputSignature = inputSignature.Trim();
            return this;
        }

        internal static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }
    }
}
