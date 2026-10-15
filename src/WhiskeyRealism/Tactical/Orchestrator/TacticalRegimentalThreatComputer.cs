using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    // Pure threat computation for a single regiment. Walks the per-side
    // observation array, finds nearby enemies, computes quadrant threats
    // relative to the enemy centroid direction, and detects isolation
    // from friendly brigade-mates.
    //
    // No Unity API touches; reads only the immutable TacticalUnitObservation
    // structs already captured on main. Safe to run on the worker thread.
    public static class TacticalRegimentalThreatComputer
    {
        // Distances calibrated to GT's battle scale. Scourge uses meters;
        // GT uses world units which are roughly meters at our zoom levels.
        public const float EnemyRangeMeters = 300f;        // anything within = "nearby" for threat purposes
        public const float WheelDistanceMeters = 120f;     // enemies within = "pressed", can't safely wheel
        public const float IsolationDistanceMeters = 400f; // no friendly brigade-mate within = "isolated"
        // Flank-vs-front ratio that triggers IsFlanked. A flank quadrant must
        // carry at least 20% more threat than the front to qualify — guards
        // against trivial noise (one isolated enemy on the flank doesn't
        // count when there's a major front threat).
        public const float FlankExceedsFrontRatio = 1.2f;
        // Minimum absolute flank threat to qualify as "flanked." Prevents
        // tiny enemy detachments (a routed regiment behind us) from
        // triggering a wheel/fallback.
        public const float MinFlankThreatAbsolute = 50f;

        // Computes the assessment for a single own-regiment given the
        // observation array. `ownObservation` is the regiment we're scoring;
        // `allObservations` is the full per-side observation array; `alliedIndices`
        // and `enemyIndices` partition it.
        //
        // Returns Empty when there are no nearby enemies (NoAction case downstream).
        public static TacticalRegimentalThreatAssessment Compute(
            TacticalUnitObservation ownObservation,
            IReadOnlyList<TacticalUnitObservation> allObservations,
            IReadOnlyList<int> alliedIndices,
            IReadOnlyList<int> enemyIndices)
        {
            if (allObservations == null || enemyIndices == null || alliedIndices == null)
                return TacticalRegimentalThreatAssessment.Empty;

            float ownX = ownObservation.WorldX;
            float ownZ = ownObservation.WorldZ;

            // First pass: find the enemy centroid for "front" direction reference.
            // Weighted by strength so a big enemy mass anchors front, not a lone
            // routed unit.
            float weightedX = 0f, weightedZ = 0f, weightTotal = 0f;
            int nearbyCount = 0;
            for (int i = 0; i < enemyIndices.Count; i++)
            {
                int idx = enemyIndices[i];
                if (idx < 0 || idx >= allObservations.Count) continue;
                var e = allObservations[idx];
                if (e.IsRouted || e.PermanentlyDetached) continue;
                float dx = e.WorldX - ownX;
                float dz = e.WorldZ - ownZ;
                float distSq = dx * dx + dz * dz;
                float maxRangeSq = EnemyRangeMeters * EnemyRangeMeters;
                if (distSq > maxRangeSq) continue;

                float w = e.Strength > 1f ? e.Strength : 1f;
                weightedX += e.WorldX * w;
                weightedZ += e.WorldZ * w;
                weightTotal += w;
                nearbyCount++;
            }

            if (nearbyCount == 0 || weightTotal <= 0f)
            {
                // No nearby enemies → no threat. Still check isolation; an
                // isolated regiment with no enemies nearby may want to fall back
                // to brigade preemptively (Scourge does NOT do this — GT
                // enhancement).
                bool noEnemyIsolated = IsIsolatedFromAllies(ownObservation, allObservations, alliedIndices);
                return new TacticalRegimentalThreatAssessment(
                    frontThreat: 0f,
                    leftFlankThreat: 0f,
                    rearThreat: 0f,
                    rightFlankThreat: 0f,
                    pressedCount: 0,
                    isFlanked: false,
                    isIsolated: noEnemyIsolated,
                    totalEnemiesInRange: 0,
                    frontBearingDegrees: 0f);
            }

            float centroidX = weightedX / weightTotal;
            float centroidZ = weightedZ / weightTotal;
            float frontBearing = BearingDegrees(ownX, ownZ, centroidX, centroidZ);

            // Second pass: assign each enemy to a quadrant relative to the
            // front bearing, accumulate quadrant threats, count pressed flanks.
            float[] quadrantThreat = new float[4];
            bool[] quadrantPressed = new bool[4];
            for (int i = 0; i < enemyIndices.Count; i++)
            {
                int idx = enemyIndices[i];
                if (idx < 0 || idx >= allObservations.Count) continue;
                var e = allObservations[idx];
                if (e.IsRouted || e.PermanentlyDetached) continue;
                float dx = e.WorldX - ownX;
                float dz = e.WorldZ - ownZ;
                float distSq = dx * dx + dz * dz;
                if (distSq > EnemyRangeMeters * EnemyRangeMeters) continue;
                float dist = MathSqrt(distSq);

                float enemyBearing = BearingDegrees(ownX, ownZ, e.WorldX, e.WorldZ);
                float relativeBearing = NormalizeBearingDegrees(enemyBearing - frontBearing);
                RegimentalQuadrant quad = QuadrantFromRelativeBearing(relativeBearing);

                // Threat formula: Scourge uses men*100 - dist. We use
                // strength * (1 - dist/range) so closer enemies count more.
                float strength = e.Strength > 1f ? e.Strength : 1f;
                float proximityFactor = 1f - dist / EnemyRangeMeters;
                if (proximityFactor < 0f) proximityFactor = 0f;
                float threat = strength * proximityFactor;
                quadrantThreat[(int)quad] += threat;

                if (dist <= WheelDistanceMeters)
                    quadrantPressed[(int)quad] = true;
            }

            int pressedCount = 0;
            for (int q = 0; q < 4; q++) if (quadrantPressed[q]) pressedCount++;

            float front = quadrantThreat[(int)RegimentalQuadrant.Front];
            float left = quadrantThreat[(int)RegimentalQuadrant.LeftFlank];
            float rear = quadrantThreat[(int)RegimentalQuadrant.Rear];
            float right = quadrantThreat[(int)RegimentalQuadrant.RightFlank];
            float maxFlank = left;
            if (right > maxFlank) maxFlank = right;
            if (rear > maxFlank) maxFlank = rear;
            bool isFlanked = maxFlank >= MinFlankThreatAbsolute
                && maxFlank > front * FlankExceedsFrontRatio;

            bool isolated = IsIsolatedFromAllies(ownObservation, allObservations, alliedIndices);

            return new TacticalRegimentalThreatAssessment(
                frontThreat: front,
                leftFlankThreat: left,
                rearThreat: rear,
                rightFlankThreat: right,
                pressedCount: pressedCount,
                isFlanked: isFlanked,
                isIsolated: isolated,
                totalEnemiesInRange: nearbyCount,
                frontBearingDegrees: frontBearing);
        }

        // Returns true when no friendly regiment is within IsolationDistanceMeters.
        // Excludes the own regiment itself (skipped by InstanceId match).
        private static bool IsIsolatedFromAllies(
            TacticalUnitObservation own,
            IReadOnlyList<TacticalUnitObservation> all,
            IReadOnlyList<int> alliedIndices)
        {
            if (alliedIndices == null || alliedIndices.Count == 0) return true;
            float ownX = own.WorldX;
            float ownZ = own.WorldZ;
            int ownId = own.InstanceId;
            float isoSq = IsolationDistanceMeters * IsolationDistanceMeters;
            for (int i = 0; i < alliedIndices.Count; i++)
            {
                int idx = alliedIndices[i];
                if (idx < 0 || idx >= all.Count) continue;
                var ally = all[idx];
                if (ally.InstanceId == ownId) continue;
                if (ally.IsRouted || ally.PermanentlyDetached) continue;
                float dx = ally.WorldX - ownX;
                float dz = ally.WorldZ - ownZ;
                if (dx * dx + dz * dz <= isoSq) return false;
            }
            return true;
        }

        // Compass-style bearing from (fromX, fromZ) to (toX, toZ) in degrees [0, 360).
        // 0° = north (+Z), 90° = east (+X). Returns 0 when source and target coincide.
        public static float BearingDegrees(float fromX, float fromZ, float toX, float toZ)
        {
            float dx = toX - fromX;
            float dz = toZ - fromZ;
            if (dx == 0f && dz == 0f) return 0f;
            float radians = (float)Math.Atan2(dx, dz);
            float degrees = radians * 57.2957795f; // 180 / PI
            if (degrees < 0f) degrees += 360f;
            return degrees;
        }

        // Normalizes a degree value to (-180, 180] for quadrant assignment.
        public static float NormalizeBearingDegrees(float degrees)
        {
            float d = degrees;
            while (d > 180f) d -= 360f;
            while (d <= -180f) d += 360f;
            return d;
        }

        // Maps a relative bearing (relative to front-direction) to a quadrant.
        // -45..45 = Front; 45..135 = RightFlank; |b|>135 = Rear; -135..-45 = LeftFlank.
        // Scourge uses 90° cones (each quadrant); we mirror that exactly.
        public static RegimentalQuadrant QuadrantFromRelativeBearing(float relativeBearingDegrees)
        {
            float abs = relativeBearingDegrees < 0f ? -relativeBearingDegrees : relativeBearingDegrees;
            if (abs <= 45f) return RegimentalQuadrant.Front;
            if (abs >= 135f) return RegimentalQuadrant.Rear;
            return relativeBearingDegrees > 0f ? RegimentalQuadrant.RightFlank : RegimentalQuadrant.LeftFlank;
        }

        private static float MathSqrt(float v)
        {
            if (v <= 0f) return 0f;
            return (float)Math.Sqrt(v);
        }
    }
}
