using System;
using System.Globalization;

namespace WhiskeyRealism.Tactical
{
    public readonly struct TacticalMapKnowledgeInventoryRow
    {
        public TacticalMapKnowledgeInventoryRow(
            string source,
            int objectives,
            int entryPoints,
            int aiFortifications,
            int fortificationGroups,
            int roads,
            int railroads,
            string sampleObjective,
            string sampleEntryPoint,
            string sampleFortification)
        {
            Source = Safe(source);
            Objectives = Math.Max(0, objectives);
            EntryPoints = Math.Max(0, entryPoints);
            AiFortifications = Math.Max(0, aiFortifications);
            FortificationGroups = Math.Max(0, fortificationGroups);
            Roads = Math.Max(0, roads);
            Railroads = Math.Max(0, railroads);
            SampleObjective = Safe(sampleObjective);
            SampleEntryPoint = Safe(sampleEntryPoint);
            SampleFortification = Safe(sampleFortification);
        }

        public string Source { get; }
        public int Objectives { get; }
        public int EntryPoints { get; }
        public int AiFortifications { get; }
        public int FortificationGroups { get; }
        public int Roads { get; }
        public int Railroads { get; }
        public string SampleObjective { get; }
        public string SampleEntryPoint { get; }
        public string SampleFortification { get; }

        private static string Safe(string value) => TacticalMapKnowledgeTelemetry.SafeToken(value);
    }

    public readonly struct TacticalMapKnowledgeUnitContextRow
    {
        public TacticalMapKnowledgeUnitContextRow(
            string unit,
            int alliance,
            float x,
            float z,
            float facing,
            int aiStance,
            int formation,
            string currentObjective,
            string nearestObjective,
            float objectiveBearing,
            float objectiveDistance,
            string nearestEntryPoint,
            float entryPointBearing,
            float entryPointDistance,
            string nearestFortification,
            float fortificationBearing,
            float fortificationDistance,
            float coverValue,
            float coverValueSubordinates,
            int coverObject,
            float threatBearing,
            float threatDistance,
            string threatConfidence)
        {
            Unit = Safe(unit);
            Alliance = alliance;
            X = Sanitize(x);
            Z = Sanitize(z);
            Facing = NormalizeAngle(facing);
            AiStance = aiStance;
            Formation = formation;
            CurrentObjective = Safe(currentObjective);
            NearestObjective = Safe(nearestObjective);
            ObjectiveBearing = NormalizeAngle(objectiveBearing);
            ObjectiveDistance = NonNegative(objectiveDistance);
            NearestEntryPoint = Safe(nearestEntryPoint);
            EntryPointBearing = NormalizeAngle(entryPointBearing);
            EntryPointDistance = NonNegative(entryPointDistance);
            NearestFortification = Safe(nearestFortification);
            FortificationBearing = NormalizeAngle(fortificationBearing);
            FortificationDistance = NonNegative(fortificationDistance);
            CoverValue = Sanitize(coverValue);
            CoverValueSubordinates = Sanitize(coverValueSubordinates);
            CoverObject = coverObject;
            ThreatBearing = NormalizeAngle(threatBearing);
            ThreatDistance = NonNegative(threatDistance);
            ThreatConfidence = Safe(threatConfidence);
        }

        public string Unit { get; }
        public int Alliance { get; }
        public float X { get; }
        public float Z { get; }
        public float Facing { get; }
        public int AiStance { get; }
        public int Formation { get; }
        public string CurrentObjective { get; }
        public string NearestObjective { get; }
        public float ObjectiveBearing { get; }
        public float ObjectiveDistance { get; }
        public string NearestEntryPoint { get; }
        public float EntryPointBearing { get; }
        public float EntryPointDistance { get; }
        public string NearestFortification { get; }
        public float FortificationBearing { get; }
        public float FortificationDistance { get; }
        public float CoverValue { get; }
        public float CoverValueSubordinates { get; }
        public int CoverObject { get; }
        public float ThreatBearing { get; }
        public float ThreatDistance { get; }
        public string ThreatConfidence { get; }

        private static string Safe(string value) => TacticalMapKnowledgeTelemetry.SafeToken(value);

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private static float NonNegative(float value)
        {
            value = Sanitize(value);
            return value < 0f ? 0f : value;
        }

        private static float NormalizeAngle(float value)
        {
            value = Sanitize(value) % 360f;
            return value < 0f ? value + 360f : value;
        }
    }

    public static class TacticalMapKnowledgeTelemetry
    {
        public static string FormatInventory(TacticalMapKnowledgeInventoryRow row)
        {
            return "[TacticalMapKnowledge]" +
                   " surface=inventory" +
                   " source=" + row.Source +
                   " objectives=" + row.Objectives +
                   " entryPoints=" + row.EntryPoints +
                   " aiFortifications=" + row.AiFortifications +
                   " fortificationGroups=" + row.FortificationGroups +
                   " roads=" + row.Roads +
                   " railroads=" + row.Railroads +
                   " sampleObjective=" + row.SampleObjective +
                   " sampleEntryPoint=" + row.SampleEntryPoint +
                   " sampleFortification=" + row.SampleFortification +
                   " signature=" + InventorySignature(row);
        }

        public static string FormatUnitContext(TacticalMapKnowledgeUnitContextRow row)
        {
            return "[TacticalMapKnowledge]" +
                   " surface=unit" +
                   " unit=" + row.Unit +
                   " alliance=" + row.Alliance +
                   " pos=(" + Float1(row.X) + "," + Float1(row.Z) + ")" +
                   " facing=" + Float1(row.Facing) +
                   " stance=" + row.AiStance +
                   " formation=" + row.Formation +
                   " currentObjective=" + row.CurrentObjective +
                   " nearestObjective=" + row.NearestObjective +
                   " objBearing=" + Float1(row.ObjectiveBearing) +
                   " objDistance=" + Float1(row.ObjectiveDistance) +
                   " nearestEntryPoint=" + row.NearestEntryPoint +
                   " entryBearing=" + Float1(row.EntryPointBearing) +
                   " entryDistance=" + Float1(row.EntryPointDistance) +
                   " nearestFortification=" + row.NearestFortification +
                   " fortBearing=" + Float1(row.FortificationBearing) +
                   " fortDistance=" + Float1(row.FortificationDistance) +
                   " cover=" + Float2(row.CoverValue) +
                   " coverSubs=" + Float2(row.CoverValueSubordinates) +
                   " coverObject=" + row.CoverObject +
                   " threatBearing=" + Float1(row.ThreatBearing) +
                   " threatDistance=" + Float1(row.ThreatDistance) +
                   " threatConfidence=" + row.ThreatConfidence +
                   " signature=" + UnitSignature(row);
        }

        public static string InventorySignature(TacticalMapKnowledgeInventoryRow row)
        {
            return "objectives=" + row.Objectives +
                   "|entryPoints=" + row.EntryPoints +
                   "|fortGroups=" + row.FortificationGroups +
                   "|roads=" + Bucket(row.Roads) +
                   "|railroads=" + Bucket(row.Railroads);
        }

        public static string UnitSignature(TacticalMapKnowledgeUnitContextRow row)
        {
            return "unit=" + row.Unit +
                   "|alliance=" + row.Alliance +
                   "|stance=" + row.AiStance +
                   "|formation=" + row.Formation +
                   "|objective=" + row.CurrentObjective +
                   "|nearObj=" + row.NearestObjective +
                   "|entry=" + row.NearestEntryPoint +
                   "|fort=" + row.NearestFortification +
                   "|cover=" + Bucket(row.CoverValue * 100f) +
                   "|coverSubs=" + Bucket(row.CoverValueSubordinates * 100f) +
                   "|threat=" + row.ThreatConfidence +
                   "|threatBearing=" + Bucket(row.ThreatBearing);
        }

        internal static string SafeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "-";

            var chars = value.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (char.IsControl(c) || char.IsWhiteSpace(c) || c == '|' || c == '=' || c == '{' || c == '}')
                    chars[i] = '_';
            }

            return new string(chars);
        }

        private static string Float1(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0.0";
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string Float2(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0.00";
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string Bucket(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0";
            return (Math.Round(value / 10f) * 10f).ToString("0", CultureInfo.InvariantCulture);
        }
    }
}
