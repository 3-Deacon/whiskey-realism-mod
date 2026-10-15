using System;

namespace WhiskeyRealism.Tactical.Battle
{
    // The crossings and fire points the PERCEIVED enemy covers (BC10 decision 2). Vanilla's
    // AIBattle.UpdateBlockedCrossings (7917-7973) builds the same list from every enemy
    // regiment on the map, fogged or not, one unit per frame; BlockedMovingOrder (3054-3150)
    // only reads the list it is handed, so Whiskey hands it this one. Pure.
    //
    // Vanilla's two rules, per enemy regiment (unittyp != 2 artillery, <= 13):
    //   (a) every rivercrossingpositions point within 1.5 x firerange of the regiment whose
    //       bearing from it lies within 45 degrees of its facing:
    //       GetAbsAngleDifference(GetAngleTerrain(pos, crossing) + 180, eulerAngles.y) <= 45
    //       (GetAngleTerrain = atan2(dx, dz) - 180, so the +180 is the plain bearing);
    //   (b) a regiment in real cover (covervalue > 0.15, coverobject != 3 forest), not
    //       routed and without paths covers the point one firerange ahead of it:
    //       GetAngleTerrainPos(pos, eulerAngles.y, firerange) = (x + cos(a - 90) * range,
    //       z - sin(a - 90) * range) clamped to x in [0, sizeX - 1], z in [1 - sizeZ, 0]
    //       (26884-26933). A point already listed is not listed again (Contains).
    //
    // The enemies are the snapshot's observations: Visible ones, and Remembered ones seen
    // within the last 20 battle-minutes (confidence at least 0.5) at their last seen values.
    // Reinforcement columns and groups are not regiments in the field and are skipped.
    public static class CoveredCrossings
    {
        public const float RememberedMinConfidence = 0.5f;
        public const float ArcDegrees = 45f;
        public const float RangeFactor = 1.5f;
        public const float MinCoverValue = 0.15f;
        private const int ForestCover = 3;

        /// <summary>
        /// Writes the covered points into <paramref name="xs"/> / <paramref name="zs"/> (up to
        /// the shorter length) and returns how many; <paramref name="crossingPoints"/> is how
        /// many of them are listed river crossings (rule a), the rest are cover fire points
        /// (rule b). Allocation-free.
        /// </summary>
        public static int BuildPoints(IBattleUnitView s, BattleTerrainGrid grid, float[] xs, float[] zs, out int crossingPoints)
        {
            crossingPoints = 0;
            if (s == null || xs == null || zs == null) return 0;
            int capacity = Math.Min(xs.Length, zs.Length);
            int count = 0;
            EnemyObservation[] observations = s.Observations;
            for (int k = 0; k < s.ObservationCount && count < capacity; k++)
            {
                EnemyObservation o = observations[k];
                if (!Perceived(o)) continue;
                if (grid != null) AddCoveredCrossings(o, grid, xs, zs, ref count, capacity);
            }
            crossingPoints = count;
            for (int k = 0; k < s.ObservationCount && count < capacity; k++)
            {
                EnemyObservation o = observations[k];
                if (!Perceived(o) || !InCover(o)) continue;
                GetAngleTerrainPos(o.X, o.Z, o.Heading, o.FireRange, grid, out float px, out float pz);
                AddOnce(xs, zs, ref count, px, pz);
            }
            return count;
        }

        /// <summary>
        /// BuildPoints that grows the output buffers instead of truncating: a full list is rebuilt
        /// into doubled buffers up to <paramref name="maxCapacity"/>. <paramref name="truncated"/> is
        /// true when the list still fills the buffers at the ceiling (points may have been cut).
        /// </summary>
        public static int BuildPointsGrowing(IBattleUnitView s, BattleTerrainGrid grid, ref float[] xs, ref float[] zs,
            int maxCapacity, out int crossingPoints, out bool truncated)
        {
            int n = BuildPoints(s, grid, xs, zs, out crossingPoints);
            while (n > 0 && n == Math.Min(xs.Length, zs.Length) && n < maxCapacity)
            {
                int size = Math.Min(maxCapacity, Math.Max(8, n * 2));
                xs = new float[size];
                zs = new float[size];
                n = BuildPoints(s, grid, xs, zs, out crossingPoints);
            }
            truncated = n > 0 && n == Math.Min(xs.Length, zs.Length) && n >= maxCapacity;
            return n;
        }

        private static bool Perceived(in EnemyObservation o)
        {
            if (o.IsReinforcement || o.UnitTyp == TacticalUnitType.Artillery || o.UnitTyp > TacticalUnitType.MaxCombat) return false;
            return o.State == ObservationState.Visible ||
                (o.State == ObservationState.Remembered && o.Confidence01 >= RememberedMinConfidence);
        }

        private static bool InCover(in EnemyObservation o)
        {
            return o.Standing && o.CoverValue > MinCoverValue && o.CoverObject != ForestCover;
        }

        private static void AddCoveredCrossings(in EnemyObservation o, BattleTerrainGrid grid, float[] xs, float[] zs, ref int count, int capacity)
        {
            float reach = o.FireRange * RangeFactor;
            for (int c = 0; c < grid.CrossingCount && count < capacity; c++)
            {
                grid.Crossing(c, out float cx, out float cz, out _);
                float dx = cx - o.X;
                float dz = cz - o.Z;
                if (Math.Sqrt(dx * dx + dz * dz) >= reach) continue;
                float bearing = (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI);
                if (AbsAngleDifference(bearing, o.Heading) > ArcDegrees) continue;
                AddOnce(xs, zs, ref count, cx, cz);
            }
        }

        // Tools.GetAbsAngleDifference (151779): the wrapped, absolute difference.
        private static float AbsAngleDifference(float a, float b)
        {
            float d = a - b;
            while (d < -180f) d += 360f;
            while (d > 180f) d -= 360f;
            return Math.Abs(d);
        }

        // BattlefieldSetup.GetAngleTerrainPosStatic (26891-26933) without the height.
        private static void GetAngleTerrainPos(float x, float z, float angle, float distance, BattleTerrainGrid grid, out float px, out float pz)
        {
            float rad = (angle - 90f) * (float)Math.PI / 180f;
            px = x + (float)Math.Cos(rad) * distance;
            pz = z - (float)Math.Sin(rad) * distance;
            if (grid == null) return;
            if (px < 0f) px = 0f;
            if (px > grid.SizeX - 1f) px = grid.SizeX - 1f;
            if (pz > 0f) pz = 0f;
            if (pz < 0f - grid.SizeZ + 1f) pz = 0f - grid.SizeZ + 1f;
        }

        private static void AddOnce(float[] xs, float[] zs, ref int count, float x, float z)
        {
            for (int i = 0; i < count; i++)
                if (xs[i] == x && zs[i] == z) return;
            xs[count] = x;
            zs[count] = z;
            count++;
        }
    }
}
