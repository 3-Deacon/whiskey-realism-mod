using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WhiskeyRealism.Tactical
{
    public sealed class TacticalDeploymentGroupSnapshot
    {
        public TacticalDeploymentGroupSnapshot(
            string key,
            string name,
            int alliance,
            int unitType,
            float x,
            float z,
            int formation,
            int formationOrdered,
            int pathCount,
            bool routed,
            bool active,
            int terrainId = -1,
            bool centerWater = false,
            bool footprintWater = false,
            bool insideDeploymentZone = true,
            float facing = 0f,
            float nearestVisibleEnemyBearing = 0f,
            float nearestVisibleEnemyDistance = 0f)
        {
            Key = Safe(key);
            Name = Safe(name);
            Alliance = alliance;
            UnitType = unitType;
            X = Sanitize(x);
            Z = Sanitize(z);
            Formation = formation;
            FormationOrdered = formationOrdered;
            PathCount = Math.Max(0, pathCount);
            Routed = routed;
            Active = active;
            TerrainId = terrainId;
            CenterWater = centerWater;
            FootprintWater = footprintWater;
            InsideDeploymentZone = insideDeploymentZone;
            Facing = Sanitize(facing);
            NearestVisibleEnemyBearing = Sanitize(nearestVisibleEnemyBearing);
            NearestVisibleEnemyDistance = Sanitize(nearestVisibleEnemyDistance);
        }

        public string Key { get; }
        public string Name { get; }
        public int Alliance { get; }
        public int UnitType { get; }
        public float X { get; }
        public float Z { get; }
        public int Formation { get; }
        public int FormationOrdered { get; }
        public int PathCount { get; }
        public bool Routed { get; }
        public bool Active { get; }
        public int TerrainId { get; }
        public bool CenterWater { get; }
        public bool FootprintWater { get; }
        public bool InsideDeploymentZone { get; }
        public float Facing { get; }
        public float NearestVisibleEnemyBearing { get; }
        public float NearestVisibleEnemyDistance { get; }
        public bool HasTerrainEvidence => TerrainId >= 0 || CenterWater || FootprintWater;
        public bool HasVisibleEnemyBearing => NearestVisibleEnemyDistance > 0f;

        public float DistanceTo(TacticalDeploymentGroupSnapshot other)
        {
            if (other == null) return 0f;
            float dx = X - other.X;
            float dz = Z - other.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Replace(' ', '_');
        }
    }

    public sealed class TacticalDeploymentSnapshot
    {
        public TacticalDeploymentSnapshot(
            string label,
            int alliance,
            int eodCycle,
            int battlePassedDays,
            IEnumerable<TacticalDeploymentGroupSnapshot> groups,
            string phase = null)
        {
            Label = string.IsNullOrWhiteSpace(label) ? "-" : label.Replace(' ', '_');
            Alliance = alliance;
            EodCycle = eodCycle;
            BattlePassedDays = battlePassedDays;
            Phase = TacticalDeploymentTelemetry.NormalizePhase(phase, eodCycle, battlePassedDays);
            Groups = (groups ?? Array.Empty<TacticalDeploymentGroupSnapshot>())
                .Where(g => g != null)
                .ToList();
        }

        public string Label { get; }
        public int Alliance { get; }
        public int EodCycle { get; }
        public int BattlePassedDays { get; }
        public string Phase { get; }
        public IReadOnlyList<TacticalDeploymentGroupSnapshot> Groups { get; }

        public static TacticalDeploymentSnapshot Empty(string label, int alliance, int eodCycle, int battlePassedDays, string phase = null)
        {
            return new TacticalDeploymentSnapshot(label, alliance, eodCycle, battlePassedDays, Array.Empty<TacticalDeploymentGroupSnapshot>(), phase);
        }
    }

    public sealed class TacticalDeploymentDelta
    {
        public string Surface { get; set; }
        public string Phase { get; set; }
        public int Alliance { get; set; }
        public int EodCycle { get; set; }
        public int BattlePassedDays { get; set; }
        public int BeforeGroups { get; set; }
        public int AfterGroups { get; set; }
        public int MatchedGroups { get; set; }
        public int MovedGroups { get; set; }
        public int LargeMoves { get; set; }
        public int NewGroups { get; set; }
        public int RemovedGroups { get; set; }
        public float MaxMoveDistance { get; set; }
        public float AverageMoveDistance { get; set; }
    }

    public static class TacticalDeploymentTelemetry
    {
        public const float MovementEpsilon = 1f;
        public const float LargeMoveThreshold = 100f;
        public const string PhaseInitialPositioning = "initial-positioning";
        public const string PhaseInitial = "initial";
        public const string PhaseEod = "eod";
        public const string PhaseSkipped = "skipped";

        public static string PhaseFromPrefix(bool skipped, bool initialPositioning, int eodCycle, int battlePassedDays)
        {
            if (skipped) return PhaseSkipped;
            if (initialPositioning) return PhaseInitialPositioning;
            return eodCycle > 0 || battlePassedDays > 0 ? PhaseEod : PhaseInitial;
        }

        public static string NormalizePhase(string phase, int eodCycle, int battlePassedDays)
        {
            switch (Safe(phase))
            {
                case PhaseInitialPositioning:
                    return PhaseInitialPositioning;
                case PhaseInitial:
                    return PhaseInitial;
                case PhaseEod:
                    return PhaseEod;
                case PhaseSkipped:
                    return PhaseSkipped;
                default:
                    return eodCycle > 0 || battlePassedDays > 0 ? PhaseEod : PhaseInitial;
            }
        }

        public static TacticalDeploymentDelta Delta(
            string surface,
            TacticalDeploymentSnapshot before,
            TacticalDeploymentSnapshot after)
        {
            before = before ?? TacticalDeploymentSnapshot.Empty("before", -1, 0, 0);
            after = after ?? TacticalDeploymentSnapshot.Empty("after", before.Alliance, before.EodCycle, before.BattlePassedDays);

            var beforeByKey = before.Groups.GroupBy(g => g.Key).ToDictionary(g => g.Key, g => g.First());
            var afterByKey = after.Groups.GroupBy(g => g.Key).ToDictionary(g => g.Key, g => g.First());

            int matched = 0;
            int moved = 0;
            int large = 0;
            float totalDistance = 0f;
            float maxDistance = 0f;

            foreach (var pair in beforeByKey)
            {
                if (!afterByKey.TryGetValue(pair.Key, out var afterGroup)) continue;
                matched++;
                float distance = pair.Value.DistanceTo(afterGroup);
                if (distance > MovementEpsilon)
                {
                    moved++;
                    totalDistance += distance;
                    if (distance > maxDistance) maxDistance = distance;
                    if (distance >= LargeMoveThreshold) large++;
                }
            }

            return new TacticalDeploymentDelta
            {
                Surface = Safe(surface),
                Phase = before.Phase,
                Alliance = before.Alliance,
                EodCycle = before.EodCycle,
                BattlePassedDays = before.BattlePassedDays,
                BeforeGroups = before.Groups.Count,
                AfterGroups = after.Groups.Count,
                MatchedGroups = matched,
                MovedGroups = moved,
                LargeMoves = large,
                NewGroups = afterByKey.Keys.Count(k => !beforeByKey.ContainsKey(k)),
                RemovedGroups = beforeByKey.Keys.Count(k => !afterByKey.ContainsKey(k)),
                MaxMoveDistance = maxDistance,
                AverageMoveDistance = moved <= 0 ? 0f : totalDistance / moved
            };
        }

        public static string FormatSummary(TacticalDeploymentDelta delta)
        {
            if (delta == null) delta = new TacticalDeploymentDelta { Surface = "-", Phase = PhaseInitial, Alliance = -1 };
            return "[TacDeployObs]" +
                   " surface=" + Safe(delta.Surface) +
                   " phase=" + Safe(delta.Phase) +
                   " alliance=" + delta.Alliance +
                   " eod=" + delta.EodCycle +
                   " days=" + delta.BattlePassedDays +
                   " beforeGroups=" + delta.BeforeGroups +
                   " afterGroups=" + delta.AfterGroups +
                   " matched=" + delta.MatchedGroups +
                   " moved=" + delta.MovedGroups +
                   " largeMoves=" + delta.LargeMoves +
                   " new=" + delta.NewGroups +
                   " removed=" + delta.RemovedGroups +
                   " maxMove=" + FormatFloat(delta.MaxMoveDistance) +
                   " avgMove=" + FormatFloat(delta.AverageMoveDistance) +
                   " signature=" + Signature(delta);
        }

        public static string Signature(TacticalDeploymentDelta delta)
        {
            if (delta == null) delta = new TacticalDeploymentDelta();
            return "surface=" + Safe(delta.Surface) +
                   "|phase=" + Safe(delta.Phase) +
                   "|alliance=" + delta.Alliance +
                   "|eod=" + delta.EodCycle +
                   "|days=" + delta.BattlePassedDays +
                   "|matched=" + delta.MatchedGroups +
                   "|moved=" + delta.MovedGroups +
                   "|large=" + delta.LargeMoves +
                   "|new=" + delta.NewGroups +
                   "|removed=" + delta.RemovedGroups +
                   "|max=" + Bucket(delta.MaxMoveDistance);
        }

        private static string FormatFloat(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0.0";
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string Bucket(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0";
            return (Math.Round(value / 25f) * 25f).ToString("0", CultureInfo.InvariantCulture);
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Replace(' ', '_');
        }
    }
}
