using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WhiskeyRealism.Telemetry
{
    internal sealed class TelemetrySummaryItem
    {
        internal TelemetrySummaryItem(string name, long count)
        {
            Name = TelemetryEvent.Safe(name);
            Count = Math.Max(0L, count);
        }

        internal string Name { get; private set; }
        internal long Count { get; private set; }

        public override string ToString()
        {
            return Name + "=" + Count.ToString(CultureInfo.InvariantCulture);
        }
    }

    internal sealed class TelemetrySlowScope
    {
        internal TelemetrySlowScope(string scope, string eventName, double durationMs)
        {
            Scope = TelemetryEvent.Safe(scope);
            EventName = TelemetryEvent.Safe(eventName);
            DurationMs = double.IsNaN(durationMs) || double.IsInfinity(durationMs) ? 0.0 : Math.Max(0.0, durationMs);
        }

        internal string Scope { get; private set; }
        internal string EventName { get; private set; }
        internal double DurationMs { get; private set; }

        public override string ToString()
        {
            return Scope + " " + DurationMs.ToString("0.###", CultureInfo.InvariantCulture) + "ms " + EventName;
        }
    }

    internal sealed class TelemetrySummary
    {
        private readonly SortedDictionary<string, long> _countByLayer = new SortedDictionary<string, long>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, long> _countByCategory = new SortedDictionary<string, long>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, long> _countByEvent = new SortedDictionary<string, long>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, long> _decisionCounts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, long> _reasonCounts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, long> _writeResults = new SortedDictionary<string, long>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, long> _failureCounts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, long> _queueDrops = new SortedDictionary<string, long>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, long> _budgetDrops = new SortedDictionary<string, long>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, long> _capTransitions = new SortedDictionary<string, long>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, long> _runtimeCounters = new SortedDictionary<string, long>(StringComparer.Ordinal);
        private readonly SortedSet<string> _campaignDates = new SortedSet<string>(StringComparer.Ordinal);
        private readonly SortedSet<string> _battleIds = new SortedSet<string>(StringComparer.Ordinal);
        private readonly List<string> _deniedGates = new List<string>();
        private readonly List<TelemetrySlowScope> _slowestScopes = new List<TelemetrySlowScope>();
        private readonly SortedSet<string> _missingAnchors = new SortedSet<string>(StringComparer.Ordinal);
        private readonly List<string> _recommendedInspectionQueries = new List<string>();
        private long _manifestSinkFailures;
        private long _observedSinkFailures;

        private TelemetrySummary()
        {
            SessionId = "-";
            Profile = "-";
            PluginVersion = "-";
            RuntimeAssemblySha256 = "-";
        }

        internal string SessionId { get; private set; }
        internal string Profile { get; private set; }
        internal DateTime? StartUtc { get; private set; }
        internal DateTime? EndUtc { get; private set; }
        internal string PluginVersion { get; private set; }
        internal string RuntimeAssemblySha256 { get; private set; }
        internal List<TelemetrySummaryItem> CountByLayer { get { return ItemsFrom(_countByLayer, int.MaxValue); } }
        internal List<TelemetrySummaryItem> CountByCategory { get { return ItemsFrom(_countByCategory, int.MaxValue); } }
        internal List<TelemetrySummaryItem> CountByEvent { get { return ItemsFrom(_countByEvent, int.MaxValue); } }
        internal List<TelemetrySummaryItem> TopDecisions { get { return ItemsFrom(_decisionCounts, 8); } }
        internal List<TelemetrySummaryItem> TopReasons { get { return ItemsFrom(_reasonCounts, 8); } }
        internal List<string> DeniedGates { get { return new List<string>(_deniedGates); } }
        internal List<TelemetrySummaryItem> WriteResults { get { return ItemsFrom(_writeResults, int.MaxValue); } }
        internal List<TelemetrySlowScope> SlowestScopes
        {
            get
            {
                var copy = new List<TelemetrySlowScope>(_slowestScopes);
                copy.Sort(CompareSlowScope);
                if (copy.Count > 8)
                    copy.RemoveRange(8, copy.Count - 8);
                return copy;
            }
        }
        internal List<TelemetrySummaryItem> QueueDrops { get { return ItemsFrom(_queueDrops, int.MaxValue); } }
        internal List<TelemetrySummaryItem> BudgetDrops { get { return ItemsFrom(_budgetDrops, int.MaxValue); } }
        internal List<TelemetrySummaryItem> CapTransitions { get { return ItemsFrom(_capTransitions, int.MaxValue); } }
        internal List<TelemetrySummaryItem> RuntimeCounters { get { return ItemsFrom(_runtimeCounters, int.MaxValue); } }
        internal List<TelemetrySummaryItem> RepeatedFailures
        {
            get
            {
                var repeated = new SortedDictionary<string, long>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, long> entry in _failureCounts)
                {
                    if (entry.Value > 1L)
                        repeated[entry.Key] = entry.Value;
                }

                return ItemsFrom(repeated, int.MaxValue);
            }
        }
        internal List<string> MissingAnchors { get { return new List<string>(_missingAnchors); } }
        internal List<string> RecommendedInspectionQueries { get { return new List<string>(_recommendedInspectionQueries); } }

        internal static TelemetrySummary FromDirectory(string sessionDirectory)
        {
            var summary = new TelemetrySummary();
            if (string.IsNullOrWhiteSpace(sessionDirectory) || !Directory.Exists(sessionDirectory))
                return summary;

            summary.ReadManifest(Path.Combine(sessionDirectory, "manifest.json"));
            foreach (string file in OrderedJsonlFiles(sessionDirectory))
                summary.ReadJsonl(file);
            summary.ReconcileManifestCounters();
            summary.BuildRecommendedQueries();
            return summary;
        }

        internal string ToMarkdown()
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Whiskey Realism Telemetry Summary");
            builder.AppendLine();
            builder.AppendLine("- sessionId: " + SessionId);
            builder.AppendLine("- profile: " + Profile);
            builder.AppendLine("- startUtc: " + FormatDate(StartUtc));
            builder.AppendLine("- endUtc: " + FormatDate(EndUtc));
            builder.AppendLine("- pluginVersion: " + PluginVersion);
            builder.AppendLine("- runtimeAssemblySha256: " + RuntimeAssemblySha256);
            builder.AppendLine("- campaignDates: " + JoinSet(_campaignDates));
            builder.AppendLine("- battles: " + JoinSet(_battleIds));
            AppendItems(builder, "countsByLayer", CountByLayer);
            AppendItems(builder, "countsByCategory", CountByCategory);
            AppendItems(builder, "countsByEvent", CountByEvent);
            AppendItems(builder, "topDecisions", TopDecisions);
            AppendItems(builder, "topReasons", TopReasons);
            AppendStrings(builder, "deniedGates", _deniedGates);
            AppendItems(builder, "writeResults", WriteResults);
            AppendSlowScopes(builder);
            AppendItems(builder, "queueDrops", QueueDrops);
            AppendItems(builder, "budgetDrops", BudgetDrops);
            AppendItems(builder, "capTransitions", CapTransitions);
            AppendItems(builder, "runtimeCounters", RuntimeCounters);
            AppendRepeatedFailures(builder);
            AppendStrings(builder, "missingAnchors", MissingAnchors);
            AppendStrings(builder, "recommendedInspectionQueries", RecommendedInspectionQueries);
            return builder.ToString();
        }

        internal static string WriteIssueBundleManifest(string sessionDirectory, string sessionId, string note)
        {
            if (string.IsNullOrWhiteSpace(sessionDirectory))
                throw new ArgumentException("Session directory is required.", nameof(sessionDirectory));
            if (!Directory.Exists(sessionDirectory))
                throw new DirectoryNotFoundException(sessionDirectory);

            var files = new List<string>();
            foreach (string file in Directory.GetFiles(sessionDirectory))
            {
                if (TelemetryIssueBundle.IsTelemetryOwnedFile(file, sessionDirectory))
                    files.Add(file);
            }

            files.Sort(StringComparer.OrdinalIgnoreCase);
            string safeNote = string.IsNullOrWhiteSpace(note)
                ? "Telemetry issue bundle manifest generated for session " + TelemetryEvent.Safe(sessionId) + "."
                : note;
            string json = TelemetryIssueBundle.CreateManifest(sessionId, sessionDirectory, files, safeNote).ToJson();
            string outputPath = Path.Combine(sessionDirectory, "issue-bundle.json");
            File.WriteAllText(outputPath, json);
            return outputPath;
        }

        private void ReadManifest(string manifestPath)
        {
            if (!File.Exists(manifestPath))
                return;

            try
            {
                JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
                SessionId = SafeTokenString(manifest["sessionId"], SessionId);
                PluginVersion = SafeTokenString(manifest["pluginVersion"], PluginVersion);
                RuntimeAssemblySha256 = SafeTokenString(manifest["runtimeAssemblySha256"], RuntimeAssemblySha256);
                Profile = SafeTokenString(manifest["profile"], Profile);
                StartUtc = ParseDate(manifest["startUtc"]);
                EndUtc = ParseDate(manifest["endUtc"]);

                JObject dropped = manifest["droppedCounters"] as JObject;
                if (dropped != null)
                {
                    foreach (JProperty property in dropped.Properties())
                    {
                        long value = NumberAsLong(property.Value);
                        if (value <= 0L)
                            continue;

                        if (property.Name.IndexOf("queue", StringComparison.OrdinalIgnoreCase) >= 0)
                            _queueDrops[property.Name] = value;
                        else if (property.Name.StartsWith("capTransition.", StringComparison.OrdinalIgnoreCase))
                            _capTransitions[property.Name.Substring("capTransition.".Length)] = value;
                        else if (property.Name.IndexOf("cap", StringComparison.OrdinalIgnoreCase) >= 0)
                            _capTransitions[property.Name] = value;
                        else if (string.Equals(property.Name, "sinkFailures", StringComparison.OrdinalIgnoreCase))
                        {
                            _manifestSinkFailures = value;
                            _runtimeCounters["sinkFailures"] = value;
                        }
                        else if (string.Equals(property.Name, "writerShutdownTimedOut", StringComparison.OrdinalIgnoreCase))
                            _runtimeCounters["writerShutdownTimedOut"] = value;
                        else if (IsTelemetryCategoryName(property.Name))
                            _budgetDrops[property.Name] = value;
                        else
                            _runtimeCounters[property.Name] = value;
                    }
                }
            }
            catch
            {
            }
        }

        private void ReadJsonl(string path)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch
            {
                return;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;

                try
                {
                    var row = JObject.Parse(lines[i]);
                    AccumulateRow(row);
                }
                catch (JsonException)
                {
                }
            }
        }

        private void AccumulateRow(JObject row)
        {
            if (row == null)
                return;

            string sessionId = TokenString(row["sessionId"]);
            if (SessionId == "-" && !IsBlank(sessionId))
                SessionId = sessionId;
            string profile = TokenString(row["profile"]);
            if (Profile == "-" && !IsBlank(profile))
                Profile = profile;

            string layer = TokenString(row["layer"]);
            string category = TokenString(row["category"]);
            string eventName = TokenString(row["event"]);
            string campaignDate = TokenString(row["campaignDate"]);
            string battleId = TokenString(row["battleId"]);
            string decision = TokenString(row["decision"]);
            string reason = TokenString(row["reason"]);
            double durationMs = NumberAsDouble(row["durationMs"]);
            JObject fields = row["fields"] as JObject;

            AddCount(_countByLayer, layer);
            AddCount(_countByCategory, category);
            AddCount(_countByEvent, eventName);
            AddSet(_campaignDates, campaignDate);
            AddSet(_battleIds, battleId);
            AddCount(_decisionCounts, decision);
            AddCount(_reasonCounts, reason);

            if (IsDeniedGate(category, decision, fields))
                AddUnique(_deniedGates, eventName + " " + FirstNonBlank(reason, FieldString(fields, "reason"), FieldString(fields, "gateReason")));

            if (string.Equals(category, "Write", StringComparison.OrdinalIgnoreCase))
                AddCount(_writeResults, eventName + " " + FirstNonBlank(FieldString(fields, "result"), FieldString(fields, "writeResult"), reason, decision));

            if (string.Equals(category, "Performance", StringComparison.OrdinalIgnoreCase) && durationMs > 0.0)
                _slowestScopes.Add(new TelemetrySlowScope(FirstNonBlank(FieldString(fields, "scope"), eventName), eventName, durationMs));

            if (string.Equals(category, "Failure", StringComparison.OrdinalIgnoreCase))
            {
                string failureReason = FirstNonBlank(FieldString(fields, "reason"), reason);
                AddCount(_failureCounts, eventName + " " + failureReason);
                if (string.Equals(eventName, "TelemetrySinkFailure", StringComparison.OrdinalIgnoreCase))
                    _observedSinkFailures++;
                CaptureMissingAnchor(eventName, reason, fields);
            }
        }

        private void ReconcileManifestCounters()
        {
            if (_manifestSinkFailures <= _observedSinkFailures)
                return;

            long missing = _manifestSinkFailures - _observedSinkFailures;
            AddCount(_countByCategory, "Failure", missing);
            AddCount(_countByEvent, "TelemetrySinkFailure", missing);
            _failureCounts["TelemetrySinkFailure manifest-sinkFailures"] = missing;
        }

        private static bool IsTelemetryCategoryName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            return Enum.IsDefined(typeof(TelemetryCategory), value);
        }

        private void CaptureMissingAnchor(string eventName, string reason, JObject fields)
        {
            string anchor = FirstNonBlank(FieldString(fields, "anchor"), FieldString(fields, "missingAnchor"), FieldString(fields, "method"));
            if (IsBlank(anchor))
                return;

            if (ContainsMissing(eventName) || ContainsMissing(reason) || ContainsMissing(FieldString(fields, "reason")))
                _missingAnchors.Add(anchor);
        }

        private void BuildRecommendedQueries()
        {
            _recommendedInspectionQueries.Clear();
            if (_failureCounts.Count > 0)
                _recommendedInspectionQueries.Add("category=Failure");
            if (_deniedGates.Count > 0)
                _recommendedInspectionQueries.Add("category=Gate decision=deny");
            if (_slowestScopes.Count > 0)
                _recommendedInspectionQueries.Add("category=Performance durationMs>=10");
            if (_queueDrops.Count > 0 || _budgetDrops.Count > 0 || _capTransitions.Count > 0 || _runtimeCounters.Count > 0)
                _recommendedInspectionQueries.Add("manifest droppedCounters");
            if (_missingAnchors.Count > 0)
                _recommendedInspectionQueries.Add("event=MissingAnchor");
        }

        private static List<string> OrderedJsonlFiles(string path)
        {
            var files = new List<string>();
            try
            {
                foreach (string file in Directory.GetFiles(path, "*.jsonl"))
                    files.Add(file);
            }
            catch
            {
            }

            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        private static void AddCount(SortedDictionary<string, long> counts, string key)
        {
            if (counts == null || IsBlank(key))
                return;

            AddCount(counts, key, 1L);
        }

        private static void AddCount(SortedDictionary<string, long> counts, string key, long delta)
        {
            if (counts == null || IsBlank(key) || delta <= 0L)
                return;

            long count;
            counts.TryGetValue(key, out count);
            counts[key] = count + delta;
        }

        private static void AddSet(SortedSet<string> set, string value)
        {
            if (set != null && !IsBlank(value))
                set.Add(value);
        }

        private static void AddUnique(List<string> values, string value)
        {
            if (values == null || IsBlank(value))
                return;
            if (!values.Contains(value))
                values.Add(value);
        }

        private static bool IsDeniedGate(string category, string decision, JObject fields)
        {
            if (!string.Equals(category, "Gate", StringComparison.OrdinalIgnoreCase))
                return false;
            if (string.Equals(decision, "deny", StringComparison.OrdinalIgnoreCase)
                || string.Equals(decision, "denied", StringComparison.OrdinalIgnoreCase))
                return true;

            JToken allowed = fields != null ? fields["allowed"] : null;
            if (allowed != null && allowed.Type == JTokenType.Boolean)
                return !(bool)allowed;

            string result = FirstNonBlank(FieldString(fields, "result"), FieldString(fields, "gateResult"));
            return string.Equals(result, "deny", StringComparison.OrdinalIgnoreCase)
                || string.Equals(result, "denied", StringComparison.OrdinalIgnoreCase)
                || string.Equals(result, "blocked", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsMissing(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.IndexOf("missing", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FieldString(JObject fields, string key)
        {
            if (fields == null || string.IsNullOrWhiteSpace(key))
                return "-";
            return TokenString(fields[key]);
        }

        private static string FirstNonBlank(params string[] values)
        {
            if (values == null)
                return "-";

            for (int i = 0; i < values.Length; i++)
            {
                if (!IsBlank(values[i]))
                    return values[i];
            }

            return "-";
        }

        private static string SafeTokenString(JToken token, string fallback)
        {
            string value = TokenString(token);
            return IsBlank(value) ? TelemetryEvent.Safe(fallback) : value;
        }

        private static string TokenString(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return "-";

            var scalar = token as JValue;
            string value = scalar != null
                ? Convert.ToString(scalar.Value, CultureInfo.InvariantCulture)
                : token.ToString(Formatting.None);
            return TelemetryEvent.Safe(value);
        }

        private static bool IsBlank(string value)
        {
            return string.IsNullOrWhiteSpace(value) || value.Trim() == "-";
        }

        private static long NumberAsLong(JToken token)
        {
            if (token == null)
                return 0L;

            try
            {
                return token.Value<long>();
            }
            catch
            {
                return 0L;
            }
        }

        private static double NumberAsDouble(JToken token)
        {
            if (token == null)
                return 0.0;

            try
            {
                double value = token.Value<double>();
                return double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : value;
            }
            catch
            {
                return 0.0;
            }
        }

        private static DateTime? ParseDate(JToken token)
        {
            string value = TokenString(token);
            if (IsBlank(value))
                return null;

            DateTime parsed;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
                return parsed.ToUniversalTime();
            return null;
        }

        private static string FormatDate(DateTime? value)
        {
            return value.HasValue ? value.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : "-";
        }

        private static string JoinSet(SortedSet<string> set)
        {
            if (set == null || set.Count == 0)
                return "-";
            return string.Join(", ", set);
        }

        private static List<TelemetrySummaryItem> ItemsFrom(SortedDictionary<string, long> counts, int limit)
        {
            var items = new List<TelemetrySummaryItem>();
            if (counts != null)
            {
                foreach (KeyValuePair<string, long> entry in counts)
                    items.Add(new TelemetrySummaryItem(entry.Key, entry.Value));
            }

            items.Sort(CompareSummaryItem);
            if (limit >= 0 && items.Count > limit)
                items.RemoveRange(limit, items.Count - limit);
            return items;
        }

        private static int CompareSummaryItem(TelemetrySummaryItem left, TelemetrySummaryItem right)
        {
            int count = right.Count.CompareTo(left.Count);
            if (count != 0)
                return count;
            return string.Compare(left.Name, right.Name, StringComparison.Ordinal);
        }

        private static int CompareSlowScope(TelemetrySlowScope left, TelemetrySlowScope right)
        {
            int duration = right.DurationMs.CompareTo(left.DurationMs);
            if (duration != 0)
                return duration;
            return string.Compare(left.Scope, right.Scope, StringComparison.Ordinal);
        }

        private static void AppendItems(StringBuilder builder, string label, List<TelemetrySummaryItem> items)
        {
            builder.Append("- ");
            builder.Append(label);
            builder.Append(": ");
            if (items == null || items.Count == 0)
            {
                builder.AppendLine("-");
                return;
            }

            var values = new List<string>();
            foreach (TelemetrySummaryItem item in items)
                values.Add(item.ToString());
            builder.AppendLine(string.Join(", ", values));
        }

        private static void AppendStrings(StringBuilder builder, string label, IEnumerable<string> values)
        {
            var list = new List<string>();
            if (values != null)
            {
                foreach (string value in values)
                {
                    if (!IsBlank(value))
                        list.Add(value);
                }
            }

            builder.Append("- ");
            builder.Append(label);
            builder.Append(": ");
            builder.AppendLine(list.Count == 0 ? "-" : string.Join(", ", list));
        }

        private void AppendSlowScopes(StringBuilder builder)
        {
            var scopes = SlowestScopes;
            builder.Append("- slowestScopes: ");
            if (scopes.Count == 0)
            {
                builder.AppendLine("-");
                return;
            }

            var values = new List<string>();
            foreach (TelemetrySlowScope scope in scopes)
                values.Add(scope.ToString());
            builder.AppendLine(string.Join(", ", values));
        }

        private void AppendRepeatedFailures(StringBuilder builder)
        {
            var failures = RepeatedFailures;
            builder.Append("- repeatedFailures: ");
            if (failures.Count == 0)
            {
                builder.AppendLine("-");
                return;
            }

            var values = new List<string>();
            foreach (TelemetrySummaryItem failure in failures)
                values.Add(failure.Name + " x" + failure.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(string.Join(", ", values));
        }
    }
}
