using System;
using System.Globalization;

namespace WhiskeyRealism.Tactical
{
    public readonly struct TacticalTerrainFacingLogRow
    {
        public TacticalTerrainFacingLogRow(
            string surface,
            string phase,
            int alliance,
            string unit,
            int terrainId,
            bool centerWater,
            bool footprintWater,
            bool insideDeploymentZone,
            float facing,
            float enemyBearing,
            float enemyDistance,
            TacticalTerrainDecision decision)
        {
            Surface = Safe(surface);
            Phase = Safe(phase);
            Alliance = alliance;
            Unit = Safe(unit);
            TerrainId = terrainId;
            CenterWater = centerWater;
            FootprintWater = footprintWater;
            InsideDeploymentZone = insideDeploymentZone;
            Facing = Sanitize(facing);
            EnemyBearing = Sanitize(enemyBearing);
            EnemyDistance = Sanitize(enemyDistance);
            Decision = decision;
        }

        public string Surface { get; }
        public string Phase { get; }
        public int Alliance { get; }
        public string Unit { get; }
        public int TerrainId { get; }
        public bool CenterWater { get; }
        public bool FootprintWater { get; }
        public bool InsideDeploymentZone { get; }
        public float Facing { get; }
        public float EnemyBearing { get; }
        public float EnemyDistance { get; }
        public TacticalTerrainDecision Decision { get; }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Replace(' ', '_');
        }
    }

    public static class TacticalTerrainFacingTelemetry
    {
        public static string Format(TacticalTerrainFacingLogRow row)
        {
            return "[TacDeployTerrain]" +
                   " surface=" + row.Surface +
                   " phase=" + row.Phase +
                   " alliance=" + row.Alliance +
                   " unit=" + row.Unit +
                   " terrain=" + row.TerrainId +
                   " centerWater=" + Bool(row.CenterWater) +
                   " footprintWater=" + Bool(row.FootprintWater) +
                   " inZone=" + Bool(row.InsideDeploymentZone) +
                   " facing=" + Float(row.Facing) +
                   " enemyBearing=" + Float(row.EnemyBearing) +
                   " enemyDistance=" + Float(row.EnemyDistance) +
                   " decision=" + row.Decision.Reason +
                   " accepted=" + Bool(row.Decision.Accepted) +
                   " signature=" + row.Decision.Signature;
        }

        private static string Bool(bool value) => value ? "true" : "false";

        private static string Float(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0.0";
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
