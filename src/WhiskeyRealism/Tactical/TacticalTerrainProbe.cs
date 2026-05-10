using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical
{
    internal readonly struct TacticalTerrainRuntimeSample
    {
        public TacticalTerrainRuntimeSample(int terrainId, bool water, bool inDeploymentZone)
        {
            TerrainId = terrainId;
            Water = water;
            InDeploymentZone = inDeploymentZone;
        }

        public int TerrainId { get; }
        public bool Water { get; }
        public bool InDeploymentZone { get; }

        public TacticalTerrainSample ToPure()
        {
            return new TacticalTerrainSample(TerrainId, Water, InDeploymentZone, known: TerrainId >= 0);
        }

        public static TacticalTerrainRuntimeSample Unknown =>
            new TacticalTerrainRuntimeSample(-1, false, true);
    }

    internal static class TacticalTerrainProbe
    {
        private const int WaterTerrainId = 4;
        private const float DeploymentZoneToleranceMeters = 1.5f;
        private const int MaxFootprintSamples = 64;

        private static readonly FieldInfo BattleUnitsBfsField =
            typeof(BattleUnits).GetField("bfs", BindingFlags.Static | BindingFlags.NonPublic);

        internal static TacticalTerrainRuntimeSample SamplePoint(
            Regiment regiment,
            Frontline2 deploymentZone,
            Vector3 point)
        {
            try
            {
                int terrainId = ReadTerrainId(point);
                bool water = IsWater(point, terrainId);
                bool inDeploymentZone = IsInsideDeploymentZone(regiment, deploymentZone, point);
                return new TacticalTerrainRuntimeSample(terrainId, water, inDeploymentZone);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-terrain-probe:point",
                    "TacticalTerrainProbe point sample failed: " + ex.GetType().Name);
                return TacticalTerrainRuntimeSample.Unknown;
            }
        }

        internal static TacticalTerrainRuntimeSample SampleCenter(
            Regiment regiment,
            Frontline2 deploymentZone,
            Vector3 position)
        {
            return SamplePoint(regiment, deploymentZone, position);
        }

        internal static IReadOnlyList<TacticalTerrainRuntimeSample> SampleFootprint(
            Regiment regiment,
            Frontline2 deploymentZone)
        {
            try
            {
                Vector3 center = regiment != null ? regiment.transform.position : default(Vector3);
                return SampleFootprint(regiment, deploymentZone, center, useOffset: false);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-terrain-probe:footprint",
                    "TacticalTerrainProbe footprint sample failed: " + ex.GetType().Name);
                return new[] { TacticalTerrainRuntimeSample.Unknown };
            }
        }

        internal static TacticalEnemyBearingEvidence GetVisibleEnemyBearing(Regiment regiment, Vector3 origin)
        {
            try
            {
                if (regiment == null ||
                    regiment.unitrange == null ||
                    regiment.unitrange.enemyinrangereg == null ||
                    regiment.unitrange.enemyinrangereg.Count == 0)
                {
                    return new TacticalEnemyBearingEvidence(false, 0f, 0f, 0f);
                }

                Regiment best = null;
                float bestDistance = float.MaxValue;
                float bestStrength = 0f;

                for (int i = 0; i < regiment.unitrange.enemyinrangereg.Count; i++)
                {
                    Regiment enemy = regiment.unitrange.enemyinrangereg[i];
                    if (!IsBearingEligibleEnemy(regiment, enemy))
                        continue;

                    float distance = XzDistance(origin, enemy.transform.position);
                    if (distance < bestDistance)
                    {
                        best = enemy;
                        bestDistance = distance;
                        bestStrength = Math.Max(0f, enemy.strength);
                    }
                }

                if (best == null)
                    return new TacticalEnemyBearingEvidence(false, 0f, 0f, 0f);

                float bearing = Tools.GetAngle(origin, best.transform.position) + 180f;
                return new TacticalEnemyBearingEvidence(true, bearing, bestDistance, bestStrength);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-terrain-probe:enemy-bearing",
                    "TacticalTerrainProbe visible enemy bearing failed: " + ex.GetType().Name);
                return new TacticalEnemyBearingEvidence(false, 0f, 0f, 0f);
            }
        }

        internal static bool TryBuildCandidate(
            Regiment regiment,
            Frontline2 deploymentZone,
            Vector3 point,
            float fallbackFacingDegrees,
            out TacticalTerrainCandidate candidate)
        {
            return TryBuildCandidate(
                regiment,
                deploymentZone,
                point,
                fallbackFacingDegrees,
                new TacticalEnemyBearingEvidence(false, 0f, 0f, 0f),
                out candidate);
        }

        internal static bool TryBuildCandidate(
            Regiment regiment,
            Frontline2 deploymentZone,
            Vector3 point,
            float fallbackFacingDegrees,
            TacticalEnemyBearingEvidence enemy,
            out TacticalTerrainCandidate candidate)
        {
            try
            {
                Vector3 correctedPoint = WithTerrainHeight(point);
                float facing = enemy.Visible ? enemy.BearingDegrees : fallbackFacingDegrees;
                TacticalTerrainRuntimeSample center = SamplePoint(regiment, deploymentZone, correctedPoint);
                IReadOnlyList<TacticalTerrainRuntimeSample> footprint =
                    SampleFootprint(regiment, deploymentZone, correctedPoint, facing, projectCandidate: true);

                candidate = new TacticalTerrainCandidate(
                    new TacticalPoint2(correctedPoint.x, correctedPoint.z),
                    facing,
                    center.ToPure(),
                    footprint.Select(sample => sample.ToPure()));

                return center.TerrainId >= 0 && footprint.All(sample => sample.TerrainId >= 0);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-terrain-probe:candidate",
                    "TacticalTerrainProbe candidate build failed: " + ex.GetType().Name);
                candidate = UnknownCandidate(point, fallbackFacingDegrees);
                return false;
            }
        }

        internal static Vector3 WithTerrainHeight(Vector3 point)
        {
            try
            {
                BattlefieldSetup setup = GetBattlefieldSetup();
                if ((object)setup == null) return point;
                return new Vector3(point.x, setup.GetTerrainHeight(point), point.z);
            }
            catch
            {
                return point;
            }
        }

        internal static bool CrossesWater(Vector3 from, Vector3 to)
        {
            try
            {
                BattlefieldSetup setup = GetBattlefieldSetup();
                if ((object)setup == null) return true;
                return setup.CheckTerrainLine(from, to, new[] { WaterTerrainId }, 2f);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-terrain-probe:water-line",
                    "TacticalTerrainProbe water line sample failed: " + ex.GetType().Name);
                return true;
            }
        }

        private static IReadOnlyList<TacticalTerrainRuntimeSample> SampleFootprint(
            Regiment regiment,
            Frontline2 deploymentZone,
            Vector3 candidateCenter,
            bool useOffset)
        {
            float facing = 0f;
            if (regiment != null)
                facing = regiment.transform.eulerAngles.y;

            return SampleFootprint(regiment, deploymentZone, candidateCenter, facing, useOffset);
        }

        private static IReadOnlyList<TacticalTerrainRuntimeSample> SampleFootprint(
            Regiment regiment,
            Frontline2 deploymentZone,
            Vector3 candidateCenter,
            float candidateFacingDegrees,
            bool projectCandidate)
        {
            var samples = new List<TacticalTerrainRuntimeSample>();
            if (regiment == null || regiment.blockobject == null || regiment.blockobject.Length == 0)
            {
                samples.Add(SamplePoint(regiment, deploymentZone, candidateCenter));
                return samples;
            }

            Vector3 currentCenter = regiment.transform.position;
            float facingDelta = projectCandidate
                ? candidateFacingDegrees - regiment.transform.eulerAngles.y
                : 0f;

            int count = regiment.blockobjectsused > 0
                ? Math.Min(regiment.blockobjectsused, regiment.blockobject.Length)
                : Math.Min(regiment.blockobjects, regiment.blockobject.Length);

            count = Math.Max(0, count);
            for (int i = 0; i < count && samples.Count < MaxFootprintSamples; i++)
            {
                GameObject block = regiment.blockobject[i];
                if ((object)block == null) continue;

                Renderer renderer = block.GetComponentInChildren<Renderer>();
                if ((object)renderer != null)
                {
                    AddBoundsSamples(
                        samples,
                        regiment,
                        deploymentZone,
                        renderer.bounds,
                        currentCenter,
                        candidateCenter,
                        facingDelta);
                }
                else
                {
                    samples.Add(SamplePoint(
                        regiment,
                        deploymentZone,
                        ProjectFootprintPoint(block.transform.position, currentCenter, candidateCenter, facingDelta)));
                }
            }

            if (samples.Count == 0)
                samples.Add(SamplePoint(regiment, deploymentZone, candidateCenter));

            return samples;
        }

        private static void AddBoundsSamples(
            List<TacticalTerrainRuntimeSample> samples,
            Regiment regiment,
            Frontline2 deploymentZone,
            Bounds bounds,
            Vector3 currentCenter,
            Vector3 candidateCenter,
            float facingDelta)
        {
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            AddSample(samples, regiment, deploymentZone, ProjectFootprintPoint(center, currentCenter, candidateCenter, facingDelta));
            AddSample(samples, regiment, deploymentZone, ProjectFootprintPoint(center + new Vector3(extents.x, 0f, extents.z), currentCenter, candidateCenter, facingDelta));
            AddSample(samples, regiment, deploymentZone, ProjectFootprintPoint(center + new Vector3(extents.x, 0f, -extents.z), currentCenter, candidateCenter, facingDelta));
            AddSample(samples, regiment, deploymentZone, ProjectFootprintPoint(center + new Vector3(-extents.x, 0f, extents.z), currentCenter, candidateCenter, facingDelta));
            AddSample(samples, regiment, deploymentZone, ProjectFootprintPoint(center + new Vector3(-extents.x, 0f, -extents.z), currentCenter, candidateCenter, facingDelta));
        }

        private static void AddSample(
            List<TacticalTerrainRuntimeSample> samples,
            Regiment regiment,
            Frontline2 deploymentZone,
            Vector3 point)
        {
            if (samples.Count >= MaxFootprintSamples) return;
            samples.Add(SamplePoint(regiment, deploymentZone, point));
        }

        private static int ReadTerrainId(Vector3 point)
        {
            return BattlefieldSetup.GetCurrentTerrainOnPos(point);
        }

        private static bool IsWater(Vector3 point, int terrainId)
        {
            return terrainId == WaterTerrainId;
        }

        private static bool IsBearingEligibleEnemy(Regiment regiment, Regiment enemy)
        {
            if (enemy == null || regiment == null) return false;
            if (enemy.alliance == regiment.alliance) return false;
            if (enemy.isrouted || !enemy.gameObject.activeInHierarchy) return false;
            if (enemy.unittyp == 3 || enemy.unittyp == 4) return false;
            if (enemy.inmelee > 0f) return false;
            return true;
        }

        private static bool IsInsideDeploymentZone(
            Regiment regiment,
            Frontline2 deploymentZone,
            Vector3 point)
        {
            try
            {
                if (regiment == null || (object)deploymentZone == null)
                    return true;

                Vector3 closest = deploymentZone.GetClosestPointInDeploymentZone(
                    point,
                    regiment.alliance,
                    regiment.transform.position);

                if (closest == default(Vector3) && point != default(Vector3))
                    return false;

                return XzDistance(closest, point) <= DeploymentZoneToleranceMeters;
            }
            catch
            {
                return true;
            }
        }

        private static Vector3 ProjectFootprintPoint(
            Vector3 sample,
            Vector3 currentCenter,
            Vector3 candidateCenter,
            float facingDeltaDegrees)
        {
            float radians = facingDeltaDegrees * (float)Math.PI / 180f;
            float sin = (float)Math.Sin(radians);
            float cos = (float)Math.Cos(radians);
            float dx = sample.x - currentCenter.x;
            float dz = sample.z - currentCenter.z;

            return new Vector3(
                candidateCenter.x + dx * cos - dz * sin,
                sample.y,
                candidateCenter.z + dx * sin + dz * cos);
        }

        private static BattlefieldSetup GetBattlefieldSetup()
        {
            try
            {
                BattlefieldSetup setup = BattleUnitsBfsField?.GetValue(null) as BattlefieldSetup;
                if ((object)setup != null) return setup;
            }
            catch
            {
            }

            try
            {
                return UnityEngine.Object.FindObjectOfType<BattlefieldSetup>();
            }
            catch
            {
                return null;
            }
        }

        private static TacticalTerrainCandidate UnknownCandidate(Vector3 point, float facingDegrees)
        {
            return new TacticalTerrainCandidate(
                new TacticalPoint2(point.x, point.z),
                facingDegrees,
                TacticalTerrainSample.Unknown,
                new[] { TacticalTerrainSample.Unknown });
        }

        private static float XzDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
