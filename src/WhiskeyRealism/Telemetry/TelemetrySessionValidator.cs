using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WhiskeyRealism.Telemetry
{
    internal sealed class TelemetryValidationIssue
    {
        internal TelemetryValidationIssue(string file, int line, string message)
        {
            File = TelemetryEvent.Safe(file);
            Line = Math.Max(0, line);
            Message = TelemetryEvent.Safe(message);
        }

        internal string File { get; private set; }
        internal int Line { get; private set; }
        internal string Message { get; private set; }

        public override string ToString()
        {
            return File + ":" + Line.ToString(CultureInfo.InvariantCulture) + " " + Message;
        }
    }

    internal sealed class TelemetryValidationResult
    {
        internal TelemetryValidationResult(
            bool success,
            int files,
            int validRows,
            int invalidRows,
            int partialFinalLines,
            IEnumerable<TelemetryValidationIssue> issues)
        {
            Success = success;
            Files = Math.Max(0, files);
            ValidRows = Math.Max(0, validRows);
            InvalidRows = Math.Max(0, invalidRows);
            PartialFinalLines = Math.Max(0, partialFinalLines);
            Issues = new List<TelemetryValidationIssue>();
            if (issues != null)
            {
                foreach (TelemetryValidationIssue issue in issues)
                {
                    if (issue != null)
                        Issues.Add(issue);
                }
            }

            Summary = BuildSummary();
        }

        internal bool Success { get; private set; }
        internal int Files { get; private set; }
        internal int ValidRows { get; private set; }
        internal int InvalidRows { get; private set; }
        internal int PartialFinalLines { get; private set; }
        internal List<TelemetryValidationIssue> Issues { get; private set; }
        internal string Summary { get; private set; }

        private string BuildSummary()
        {
            var parts = new List<string>
            {
                "success=" + (Success ? "true" : "false"),
                "files=" + Files.ToString(CultureInfo.InvariantCulture),
                "validRows=" + ValidRows.ToString(CultureInfo.InvariantCulture),
                "invalidRows=" + InvalidRows.ToString(CultureInfo.InvariantCulture),
                "partialFinalLines=" + PartialFinalLines.ToString(CultureInfo.InvariantCulture)
            };

            if (Issues.Count > 0)
            {
                var issueText = new List<string>();
                foreach (TelemetryValidationIssue issue in Issues)
                    issueText.Add(issue.ToString());
                parts.Add("issues=[" + string.Join("; ", issueText) + "]");
            }

            return string.Join(" ", parts);
        }
    }

    internal static class TelemetrySessionValidator
    {
        internal static TelemetryValidationResult ValidateDirectory(string path)
        {
            var issues = new List<TelemetryValidationIssue>();
            int files = 0;
            int validRows = 0;
            int invalidRows = 0;
            int partialFinalLines = 0;

            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                issues.Add(new TelemetryValidationIssue(TelemetryEvent.Safe(path), 0, "directory not found"));
                return new TelemetryValidationResult(false, 0, 0, 1, 0, issues);
            }

            foreach (string file in OrderedJsonlFiles(path))
            {
                files++;
                string[] lines;
                try
                {
                    lines = File.ReadAllLines(file);
                }
                catch (Exception ex)
                {
                    invalidRows++;
                    issues.Add(new TelemetryValidationIssue(Path.GetFileName(file), 0, "read failed: " + ex.GetType().Name));
                    continue;
                }

                int lastNonBlank = LastNonBlankLine(lines);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    try
                    {
                        ValidateTelemetryRow(JToken.Parse(line));
                        validRows++;
                    }
                    catch (JsonException ex)
                    {
                        int lineNumber = i + 1;
                        if (i == lastNonBlank && LooksLikePartialJson(line))
                        {
                            partialFinalLines++;
                            continue;
                        }

                        invalidRows++;
                        issues.Add(new TelemetryValidationIssue(Path.GetFileName(file), lineNumber, "invalid JSON: " + ex.GetType().Name));
                    }
                    catch (Exception ex)
                    {
                        int lineNumber = i + 1;
                        invalidRows++;
                        issues.Add(new TelemetryValidationIssue(Path.GetFileName(file), lineNumber, "invalid telemetry row: " + ex.Message));
                    }
                }
            }

            if (files == 0)
            {
                invalidRows++;
                issues.Add(new TelemetryValidationIssue(TelemetryEvent.Safe(path), 0, "no telemetry JSONL files found"));
            }
            else if (validRows == 0)
            {
                invalidRows++;
                issues.Add(new TelemetryValidationIssue(TelemetryEvent.Safe(path), 0, "no valid telemetry rows found"));
            }

            return new TelemetryValidationResult(invalidRows == 0, files, validRows, invalidRows, partialFinalLines, issues);
        }

        private static void ValidateTelemetryRow(JToken token)
        {
            var row = token as JObject;
            if (row == null)
                throw new InvalidDataException("row is not an object");

            RequireString(row, "schema", TelemetryEvent.Schema);
            RequireDate(row, "ts");
            RequireString(row, "sessionId");
            RequireEnumName<TelemetryProfile>(row, "profile");
            RequireEnumName<TelemetryLayer>(row, "layer");
            RequireEnumName<TelemetryCategory>(row, "category");
            RequireString(row, "event");
            RequireEnumName<TelemetrySeverity>(row, "severity");
        }

        private static void RequireString(JObject row, string propertyName, string expected)
        {
            JToken token = row[propertyName];
            if (token == null || token.Type != JTokenType.String || !string.Equals((string)token, expected, StringComparison.Ordinal))
                throw new InvalidDataException(propertyName + " mismatch");
        }

        private static string RequireString(JObject row, string propertyName)
        {
            JToken token = row[propertyName];
            if (token == null || token.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)token))
                throw new InvalidDataException(propertyName + " missing");
            return (string)token;
        }

        private static void RequireDate(JObject row, string propertyName)
        {
            JToken token = row[propertyName];
            if (token != null && token.Type == JTokenType.Date)
                return;

            string value = RequireString(row, propertyName);
            DateTime parsed;
            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
                throw new InvalidDataException(propertyName + " invalid");
        }

        private static void RequireEnumName<TEnum>(JObject row, string propertyName) where TEnum : struct
        {
            string value = RequireString(row, propertyName);
            if (!Enum.IsDefined(typeof(TEnum), value))
                throw new InvalidDataException(propertyName + " invalid");
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

        private static int LastNonBlankLine(string[] lines)
        {
            if (lines == null)
                return -1;

            for (int i = lines.Length - 1; i >= 0; i--)
            {
                if (!string.IsNullOrWhiteSpace(lines[i]))
                    return i;
            }

            return -1;
        }

        private static bool LooksLikePartialJson(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            string trimmed = line.Trim();
            if (!(trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal)))
                return false;

            int objectDepth = 0;
            int arrayDepth = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                    continue;

                if (c == '{')
                    objectDepth++;
                else if (c == '}')
                    objectDepth--;
                else if (c == '[')
                    arrayDepth++;
                else if (c == ']')
                    arrayDepth--;
            }

            return inString
                || objectDepth > 0
                || arrayDepth > 0
                || !(trimmed.EndsWith("}", StringComparison.Ordinal) || trimmed.EndsWith("]", StringComparison.Ordinal));
        }
    }
}
