// Slice, flank and closest-enemy pressure from perceived enemies (BC2a, spec §2). Pure.
//
// Replaces doctrine reads of vanilla's per-unit pressure, which has no fog filter for
// either side (UpdateUnitRangeFast 122545-122937):
// - enemystrengthwithinangle (122707-122732): bearing = Tools.GetAngle(own, enemy) + 180
//   (GetAngle 151304 is atan2(dx, dz) - 180), normalized to [0, 360); bin =
//   floor(bearing / aidefensiveslices) into an array of aidefensiveslices slots; adds
//   enemy.strength (no morale) when within aidefensivemaxrange. The divisor is the slot
//   count, so each bin is aidefensiveslices degrees wide: with the shipped 20 (battleprefs)
//   bins 0-17 fill and slots 18-19 stay empty. MostEnemiesAreOutsideAngle (4800) reads
//   bin j as centred on j*slices + slices/2, which agrees; GetBestFallbackPosition (140263)
//   uses 360/slices instead (vanilla inconsistency, not copied). Vanilla's
//   Min(360, Max(0, bin)) clamp cannot stop an overrun for other slice counts; this
//   builder clamps to the last slot. Vanilla also range-gates on the distance to the first
//   enemy beyond 15 m found in the scan (val2, 122696-122719); here the gate is the
//   enemy's own distance.
// - flankstrength (122799-122820, positions 122903-122907): vanilla tests enemies within
//   flankpositionshift (150, unitprefs) of points shifted flankpositionshift sideways from
//   the unit's regression line ends, subtracts strength x morale for enemies, adds 9999999
//   for friendly units of other groups, and slot 2 is the line centre. Here the slots are
//   left / right / rear points flankpositionshift from the unit's own position along
//   RotationY (UnitRecord has no line ends or enemy morale), and the value is positive
//   enemy pressure only. Consumers porting vanilla flankstrength thresholds must not
//   reuse them unchanged.
// Weights: Visible 1, Remembered RememberedPressureWeight x Confidence01. Reinforcement
// columns are off-map and excluded (they feed the snapshot's reinforcement totals).

using System;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>Vanilla pressure geometry the snapshot does not carry, read once per battle by
    /// the probe: GamePrefs.aidefensivemaxrange and flankpositionshift. The slice count is
    /// BattleSnapshot.SliceCount (GamePrefs.aidefensiveslices, set by the builder).</summary>
    public struct SliceGeometry
    {
        public float MaxRangeMeters;
        public float FlankShiftMeters;
    }

    public static class SlicePressureBuilder
    {
        public const float RememberedPressureWeight = 0.5f;

        // Slice accumulator lives on the stack; vanilla ships 20.
        private const int MaxSlices = 360;

        /// <summary>
        /// Writes slice, flank and closest-visible pressure for every own unit of the
        /// snapshot from its observations, overwriting each unit's row (safe to repeat).
        /// Allocation-free.
        /// </summary>
        public static void Build(BattleSnapshot s, in SliceGeometry g)
        {
            if (s == null) return;
            int slices = Math.Min(s.SliceCount, MaxSlices);
            Span<float> slice = stackalloc float[Math.Max(slices, 1)];
            float maxRangeSq = g.MaxRangeMeters * g.MaxRangeMeters;
            float flankRadiusSq = g.FlankShiftMeters * g.FlankShiftMeters;
            UnitRecord[] own = s.Own;
            EnemyObservation[] obs = s.Observations;
            int obsCount = s.ObservationCount;

            for (int o = 0; o < s.OwnCount; o++)
            {
                float ox = own[o].X;
                float oz = own[o].Z;
                float facing = own[o].RotationY;
                AnglePos(ox, oz, facing - 90f, g.FlankShiftMeters, out float leftX, out float leftZ);
                AnglePos(ox, oz, facing + 90f, g.FlankShiftMeters, out float rightX, out float rightZ);
                AnglePos(ox, oz, facing + 180f, g.FlankShiftMeters, out float rearX, out float rearZ);

                slice.Clear();
                float left = 0f, right = 0f, rear = 0f;
                int closest = -1;
                float closestSq = float.MaxValue;

                for (int k = 0; k < obsCount; k++)
                {
                    if (obs[k].IsReinforcement) continue;
                    bool visible = obs[k].State == ObservationState.Visible;
                    float weighted = obs[k].Strength * (visible ? 1f : RememberedPressureWeight * obs[k].Confidence01);
                    float dx = obs[k].X - ox;
                    float dz = obs[k].Z - oz;
                    float distSq = dx * dx + dz * dz;

                    if (visible && distSq < closestSq)
                    {
                        closestSq = distSq;
                        closest = k;
                    }
                    if (slices > 0 && distSq <= maxRangeSq) slice[SliceIndex(Bearing(dx, dz), slices)] += weighted;
                    if (WithinSq(obs[k].X, obs[k].Z, leftX, leftZ, flankRadiusSq)) left += weighted;
                    if (WithinSq(obs[k].X, obs[k].Z, rightX, rightZ, flankRadiusSq)) right += weighted;
                    if (WithinSq(obs[k].X, obs[k].Z, rearX, rearZ, flankRadiusSq)) rear += weighted;
                }

                for (int k = 0; k < slices; k++) s.SetSlicePressure(o, k, slice[k]);
                s.SetFlankPressure(o, 0, left);   // 0 left, 1 right, 2 rear
                s.SetFlankPressure(o, 1, right);
                s.SetFlankPressure(o, 2, rear);
                s.SetClosestVisibleEnemy(o, closest, closest >= 0 ? (float)Math.Sqrt(closestSq) : 0f);
            }
        }

        /// <summary>Bearing from a unit to an offset (dx, dz), degrees clockwise from +z in
        /// [0, 360): vanilla's Tools.GetAngle(own, enemy) + 180, normalized (122726-122731).</summary>
        public static float Bearing(float dx, float dz)
        {
            float degrees = (float)(Math.Atan2(dx, dz) * (180.0 / Math.PI));
            if (degrees < 0f) degrees += 360f;
            if (degrees >= 360f) degrees -= 360f;
            return degrees;
        }

        /// <summary>Vanilla bin floor(bearing / sliceCount) (122732), clamped to the array.</summary>
        public static int SliceIndex(float bearingDegrees, int sliceCount)
        {
            if (sliceCount <= 0) return 0;
            int index = (int)Math.Floor(bearingDegrees / sliceCount);
            if (index < 0) return 0;
            return index >= sliceCount ? sliceCount - 1 : index;
        }

        // Tools.GetAnglePos (151207): angle in vanilla facing degrees (0 = +z, 90 = +x).
        private static void AnglePos(float x, float z, float angleDegrees, float distance, out float px, out float pz)
        {
            double radians = (angleDegrees - 90f) * (Math.PI / 180.0);
            px = x + (float)(Math.Cos(radians) * distance);
            pz = z - (float)(Math.Sin(radians) * distance);
        }

        private static bool WithinSq(float x, float z, float cx, float cz, float radiusSq)
        {
            float dx = x - cx;
            float dz = z - cz;
            return dx * dx + dz * dz <= radiusSq;
        }
    }
}
