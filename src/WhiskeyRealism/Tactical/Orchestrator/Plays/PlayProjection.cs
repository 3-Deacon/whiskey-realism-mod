using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    /// <summary>
    /// Projected play rect in world coordinates. Anchor = center of the rect;
    /// HalfExtentX / HalfExtentZ are half-sizes along the lateral / forward
    /// axes respectively. Kind + StartTimeOffset + Priority are passed through
    /// from the source <see cref="PlayRect"/>.
    /// </summary>
    public readonly struct WorldPlayRect
    {
        public WorldPlayRect(
            PlayRectKind kind,
            float anchorX, float anchorZ,
            float halfExtentX, float halfExtentZ,
            float startTimeOffsetSeconds,
            int priority,
            bool envelops = false,
            bool mirrored = false,
            float authoredX = 0f,
            float authoredZ = 0f)
        {
            Kind = kind;
            Envelops = envelops;
            Mirrored = mirrored;
            AuthoredX = authoredX;
            AuthoredZ = authoredZ;
            AnchorX = anchorX;
            AnchorZ = anchorZ;
            HalfExtentX = halfExtentX < 0f ? 0f : halfExtentX;
            HalfExtentZ = halfExtentZ < 0f ? 0f : halfExtentZ;
            StartTimeOffsetSeconds = startTimeOffsetSeconds < 0f ? 0f : startTimeOffsetSeconds;
            Priority = priority;
        }

        public PlayRectKind Kind { get; }
        public float AnchorX { get; }
        public float AnchorZ { get; }
        public float HalfExtentX { get; }
        public float HalfExtentZ { get; }
        public float StartTimeOffsetSeconds { get; }
        public int Priority { get; }
        /// <summary>PlayRect.Envelops, carried to the zone assignment.</summary>
        public bool Envelops { get; }

        /// <summary>BC14: the template was projected mirrored (every rect's lateral offset negated in the projection frame).</summary>
        public bool Mirrored { get; }

        /// <summary>BC14: with <see cref="Mirrored"/>, the rect's un-mirrored anchor, the authored side BC11's one mirrored try falls back to; 0 otherwise.</summary>
        public float AuthoredX { get; }
        public float AuthoredZ { get; }

        public bool ContainsPoint(float x, float z)
        {
            float dx = x - AnchorX;
            float dz = z - AnchorZ;
            if (dx < 0f) dx = -dx;
            if (dz < 0f) dz = -dz;
            return dx <= HalfExtentX && dz <= HalfExtentZ;
        }
    }

    /// <summary>
    /// Pure: projects a <see cref="PlayTemplate"/>'s army-relative rects to
    /// world rects given the current battlefield geometry.
    ///
    /// Frame convention:
    ///   - origin = army centroid
    ///   - forwardAxis = normalize(enemy centroid - army centroid)
    ///   - rightAxis  = perpendicular to forwardAxis (90° clockwise in XZ)
    ///   - PlayRect.RelativeAnchor.x scales by frontage half-width along rightAxis
    ///   - PlayRect.RelativeAnchor.y scales by army↔enemy distance along forwardAxis
    ///
    /// This gives the same template the same tactical layout regardless of
    /// what compass direction the enemy is in — solving Scourge of War's
    /// StartQuad rotation problem without per-quadrant case tables.
    /// </summary>
    public static class PlayProjection
    {
        /// <summary>
        /// Default fallback frontage (world units) when own forces lack
        /// positional spread to compute one from data. ~600m matches typical
        /// army deployment frontage in vanilla GTCW.
        /// </summary>
        public const float DefaultFrontageMeters = 600f;

        /// <summary>
        /// Default fallback own↔enemy distance when the enemy centroid coincides
        /// with the army centroid (no visible enemy → bootstrap case).
        /// </summary>
        public const float DefaultEngagementDistanceMeters = 800f;

        /// <summary>
        /// Projects the template's rects to world coordinates.
        /// </summary>
        public static IReadOnlyList<WorldPlayRect> Project(
            PlayTemplate template,
            float armyCentroidX, float armyCentroidZ,
            float enemyCentroidX, float enemyCentroidZ,
            float frontageMeters)
        {
            var noCrest = default(TerrainLine);
            return Project(template, armyCentroidX, armyCentroidZ, enemyCentroidX, enemyCentroidZ, frontageMeters, in noCrest);
        }

        /// <summary>
        /// BC12 decision 8: with a crest line (<see cref="TerrainLine.Has"/>) the ReverseSlopeDefense template is anchored to it
        /// instead of to proportions of the engagement distance: the forward screen stands on the crest centre, and the centre
        /// and flank holds sit <see cref="TacticalTerrainLinePlanner.ReverseSlopeSetbackMeters"/> behind the crest along the
        /// forward axis (their lateral layout stays the template's). Other templates ignore the crest.
        /// </summary>
        public static IReadOnlyList<WorldPlayRect> Project(
            PlayTemplate template,
            float armyCentroidX, float armyCentroidZ,
            float enemyCentroidX, float enemyCentroidZ,
            float frontageMeters,
            in TerrainLine crest)
        {
            return Project(template, armyCentroidX, armyCentroidZ, enemyCentroidX, enemyCentroidZ, frontageMeters, in crest, false);
        }

        /// <summary>
        /// BC14 requirement-selected-side: the same projection with every rect's lateral offset negated in the projection frame when
        /// <paramref name="mirrored"/>, so the executor's layout matches the side the feasibility evaluation timed. A mirrored rect
        /// carries <see cref="WorldPlayRect.Mirrored"/> and its un-mirrored anchor.
        /// </summary>
        public static IReadOnlyList<WorldPlayRect> Project(
            PlayTemplate template,
            float armyCentroidX, float armyCentroidZ,
            float enemyCentroidX, float enemyCentroidZ,
            float frontageMeters,
            in TerrainLine crest,
            bool mirrored)
        {
            if (template == null || template.Rects == null || template.Rects.Count == 0)
                return Array.Empty<WorldPlayRect>();
            var result = new WorldPlayRect[template.Rects.Count];
            ProjectInto(template, armyCentroidX, armyCentroidZ, enemyCentroidX, enemyCentroidZ, frontageMeters, in crest, mirrored, result);
            return result;
        }

        /// <summary>
        /// Allocation-free <see cref="Project(PlayTemplate, float, float, float, float, float, in TerrainLine, bool)"/>: writes the rects
        /// to <paramref name="into"/> (at least the template's rect count long) and returns how many.
        /// </summary>
        public static int ProjectInto(
            PlayTemplate template,
            float armyCentroidX, float armyCentroidZ,
            float enemyCentroidX, float enemyCentroidZ,
            float frontageMeters,
            in TerrainLine crest,
            bool mirrored,
            Span<WorldPlayRect> into)
        {
            if (template == null || template.Rects == null || template.Rects.Count == 0) return 0;
            int count = Math.Min(template.Rects.Count, into.Length);

            float frontage = Sanitize(frontageMeters, DefaultFrontageMeters);
            if (frontage <= 0f) frontage = DefaultFrontageMeters;
            float halfFrontage = frontage * 0.5f;

            // Forward axis = enemy - army (XZ plane).
            float dx = enemyCentroidX - armyCentroidX;
            float dz = enemyCentroidZ - armyCentroidZ;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            if (dist < 1f)
            {
                // Degenerate: enemy on top of own centroid (or no enemy data).
                // Assume forward = +Z, lateral = +X.
                dx = 0f;
                dz = 1f;
                dist = DefaultEngagementDistanceMeters;
            }
            float forwardX = dx / dist;
            float forwardZ = dz / dist;
            // Right axis = forward rotated 90° clockwise in XZ:
            //   (forwardX, forwardZ) → (forwardZ, -forwardX)
            float rightX = forwardZ;
            float rightZ = -forwardX;

            for (int i = 0; i < count; i++)
            {
                var rect = template.Rects[i];

                // Sanitize relative inputs.
                float relAx = Sanitize(rect.RelativeAnchorX, 0f);
                float relAy = Sanitize(rect.RelativeAnchorY, 0f);
                float relSx = Sanitize(rect.RelativeSizeX, 0.3f);
                float relSy = Sanitize(rect.RelativeSizeY, 0.3f);

                // Lateral offset = relAx * halfFrontage along rightAxis.
                // Forward offset = relAy * dist along forwardAxis.
                float lateralOffset = relAx * halfFrontage;
                float forwardOffset = relAy * dist;
                // The authored side (a mirrored projection negates the lateral offset below and keeps this anchor for BC11's fallback).
                float authoredWorldX = armyCentroidX + rightX * lateralOffset + forwardX * forwardOffset;
                float authoredWorldZ = armyCentroidZ + rightZ * lateralOffset + forwardZ * forwardOffset;
                bool crestAnchored = crest.Has && template.PlaybookId == BattlePlanId.ReverseSlopeDefense;
                if (crestAnchored)
                    AnchorToCrest(rect.Kind, crest, armyCentroidX, armyCentroidZ, forwardX, forwardZ, ref authoredWorldX, ref authoredWorldZ);
                float anchorWorldX = authoredWorldX;
                float anchorWorldZ = authoredWorldZ;
                if (mirrored)
                {
                    anchorWorldX = armyCentroidX - rightX * lateralOffset + forwardX * forwardOffset;
                    anchorWorldZ = armyCentroidZ - rightZ * lateralOffset + forwardZ * forwardOffset;
                    if (crestAnchored)
                        AnchorToCrest(rect.Kind, crest, armyCentroidX, armyCentroidZ, forwardX, forwardZ, ref anchorWorldX, ref anchorWorldZ);
                }

                // Half-extents in world units. Lateral extent scales with
                // frontage; forward extent scales with engagement distance.
                float halfExtentX = relSx * halfFrontage * 0.5f;
                float halfExtentZ = relSy * dist * 0.5f;

                into[i] = new WorldPlayRect(
                    rect.Kind,
                    anchorWorldX, anchorWorldZ,
                    halfExtentX, halfExtentZ,
                    rect.StartTimeOffsetSeconds,
                    rect.Priority,
                    rect.Envelops,
                    mirrored,
                    mirrored ? authoredWorldX : 0f,
                    mirrored ? authoredWorldZ : 0f);
            }
            return count;
        }

        // The screen stands on the crest centre; the holds keep their lateral place and move along the forward axis to
        // ReverseSlopeSetbackMeters behind the crest's forward position.
        private static void AnchorToCrest(PlayRectKind kind, in TerrainLine crest, float armyX, float armyZ, float forwardX, float forwardZ,
            ref float anchorX, ref float anchorZ)
        {
            if (kind == PlayRectKind.ForwardScreen)
            {
                anchorX = crest.CenterX;
                anchorZ = crest.CenterZ;
                return;
            }
            if (kind != PlayRectKind.CenterHold && kind != PlayRectKind.LeftFlank && kind != PlayRectKind.RightFlank) return;
            float crestForward = (crest.CenterX - armyX) * forwardX + (crest.CenterZ - armyZ) * forwardZ;
            float current = (anchorX - armyX) * forwardX + (anchorZ - armyZ) * forwardZ;
            float shift = crestForward - TacticalTerrainLinePlanner.ReverseSlopeSetbackMeters - current;
            anchorX += forwardX * shift;
            anchorZ += forwardZ * shift;
        }

        /// <summary>
        /// Computes a centroid (weighted by uniform weight) from a list of
        /// world XZ positions. Returns (0,0) and weight=0 when the list is
        /// empty so callers can detect the degenerate case.
        /// </summary>
        public static void ComputeCentroid(
            IReadOnlyList<float> xs,
            IReadOnlyList<float> zs,
            out float centroidX,
            out float centroidZ,
            out int sampleCount)
        {
            centroidX = 0f;
            centroidZ = 0f;
            sampleCount = 0;
            if (xs == null || zs == null) return;
            int n = xs.Count;
            if (zs.Count < n) n = zs.Count;
            if (n == 0) return;
            float sumX = 0f, sumZ = 0f;
            int valid = 0;
            for (int i = 0; i < n; i++)
            {
                float x = xs[i];
                float z = zs[i];
                if (float.IsNaN(x) || float.IsInfinity(x)) continue;
                if (float.IsNaN(z) || float.IsInfinity(z)) continue;
                sumX += x; sumZ += z; valid++;
            }
            if (valid == 0) return;
            centroidX = sumX / valid;
            centroidZ = sumZ / valid;
            sampleCount = valid;
        }

        /// <summary>
        /// Estimates army frontage as 2× the max lateral spread along the
        /// rightAxis (perpendicular to enemy direction). Returns
        /// DefaultFrontageMeters on degenerate input.
        /// </summary>
        public static float EstimateFrontage(
            IReadOnlyList<float> ownXs,
            IReadOnlyList<float> ownZs,
            float forwardX,
            float forwardZ)
        {
            if (ownXs == null || ownZs == null) return DefaultFrontageMeters;
            int n = ownXs.Count;
            if (ownZs.Count < n) n = ownZs.Count;
            if (n < 2) return DefaultFrontageMeters;
            // Right axis is forward rotated 90° clockwise.
            float rightX = forwardZ;
            float rightZ = -forwardX;
            float minProj = float.MaxValue;
            float maxProj = float.MinValue;
            int valid = 0;
            for (int i = 0; i < n; i++)
            {
                float x = ownXs[i];
                float z = ownZs[i];
                if (float.IsNaN(x) || float.IsInfinity(x)) continue;
                if (float.IsNaN(z) || float.IsInfinity(z)) continue;
                float proj = x * rightX + z * rightZ;
                if (proj < minProj) minProj = proj;
                if (proj > maxProj) maxProj = proj;
                valid++;
            }
            if (valid < 2) return DefaultFrontageMeters;
            float spread = maxProj - minProj;
            // Add a buffer so rects extend slightly beyond the literal unit
            // spread (corps can fan out into the assigned zone).
            float frontage = spread * 1.5f;
            if (frontage < DefaultFrontageMeters * 0.5f) frontage = DefaultFrontageMeters * 0.5f;
            return frontage;
        }

        private static float Sanitize(float v, float fallback)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return fallback;
            return v;
        }
    }
}
