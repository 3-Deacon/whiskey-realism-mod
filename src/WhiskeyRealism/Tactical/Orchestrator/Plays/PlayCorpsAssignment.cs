using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    /// <summary>
    /// One corps's position for the assignment pass.
    /// </summary>
    public readonly struct CorpsPosition
    {
        public CorpsPosition(string childId, float worldX, float worldZ)
        {
            ChildId = childId ?? string.Empty;
            WorldX = worldX;
            WorldZ = worldZ;
        }

        public string ChildId { get; }
        public float WorldX { get; }
        public float WorldZ { get; }
    }

    /// <summary>
    /// One objective the assigner may use as a VP override anchor.
    /// </summary>
    public readonly struct ObjectivePosition
    {
        public ObjectivePosition(string objectiveId, float worldX, float worldZ, bool majorVp)
        {
            ObjectiveId = objectiveId ?? string.Empty;
            WorldX = worldX;
            WorldZ = worldZ;
            MajorVp = majorVp;
        }

        public string ObjectiveId { get; }
        public float WorldX { get; }
        public float WorldZ { get; }
        public bool MajorVp { get; }
    }

    /// <summary>
    /// Result row: corps assigned to a rect, with which rect kind.
    /// </summary>
    public readonly struct CorpsAssignment
    {
        public CorpsAssignment(string childId, PlayRectKind rectKind, int rectIndex, float rectAnchorX, float rectAnchorZ, float startTimeOffsetSeconds)
        {
            ChildId = childId ?? string.Empty;
            RectKind = rectKind;
            RectIndex = rectIndex;
            RectAnchorX = rectAnchorX;
            RectAnchorZ = rectAnchorZ;
            StartTimeOffsetSeconds = startTimeOffsetSeconds;
        }

        public string ChildId { get; }
        public PlayRectKind RectKind { get; }
        public int RectIndex { get; }
        public float RectAnchorX { get; }
        public float RectAnchorZ { get; }
        public float StartTimeOffsetSeconds { get; }
    }

    /// <summary>
    /// Pure: assigns each corps to one rect by closest-distance, with a
    /// major-VP override that pulls the corps tracking that objective onto
    /// the rect containing it.
    ///
    /// Mirrors Scourge of War offai.cpp:1289 — "make sure that my corp gets
    /// the major vp" override on top of "I assign this quadrant to the nearest
    /// corps from his center" baseline.
    ///
    /// Rules:
    ///   1. Process rects in descending Priority order. Higher-priority rects
    ///      (Main / Center) get first pick of corps.
    ///   2. For each rect, if any major-VP objective lies inside the rect,
    ///      and any unassigned corps is within 1.5× the rect's diagonal
    ///      distance of that objective, that corps gets this rect.
    ///   3. Otherwise, the unassigned corps closest to the rect's anchor
    ///      gets this rect.
    ///   4. A corps can only be assigned to one rect; once assigned it is
    ///      removed from consideration for subsequent rects.
    ///   5. Rects with no corps available are silently skipped.
    /// </summary>
    public static class PlayCorpsAssignment
    {
        public static IReadOnlyList<CorpsAssignment> Assign(
            IReadOnlyList<WorldPlayRect> rects,
            IReadOnlyList<CorpsPosition> corps,
            IReadOnlyList<ObjectivePosition> objectives)
        {
            if (rects == null || rects.Count == 0) return Array.Empty<CorpsAssignment>();
            if (corps == null || corps.Count == 0) return Array.Empty<CorpsAssignment>();

            // Sort rect indices by Priority desc, then by original index for stability.
            var rectOrder = new int[rects.Count];
            for (int i = 0; i < rects.Count; i++) rectOrder[i] = i;
            Array.Sort(rectOrder, (a, b) =>
            {
                int pa = rects[a].Priority;
                int pb = rects[b].Priority;
                if (pa != pb) return pb - pa;
                return a - b;
            });

            var assigned = new HashSet<int>();          // indices into corps
            var result = new List<CorpsAssignment>(rects.Count);

            for (int ri = 0; ri < rectOrder.Length; ri++)
            {
                int rectIdx = rectOrder[ri];
                var rect = rects[rectIdx];

                int pickedCorps = -1;

                // VP override: any major VP inside this rect, and any unassigned
                // corps near it?
                if (objectives != null && objectives.Count > 0)
                {
                    float rectDiagonal = Diagonal(rect);
                    float vpRadius = rectDiagonal * 1.5f;
                    for (int oi = 0; oi < objectives.Count; oi++)
                    {
                        var obj = objectives[oi];
                        if (!obj.MajorVp) continue;
                        if (!rect.ContainsPoint(obj.WorldX, obj.WorldZ)) continue;
                        int candidate = ClosestUnassignedCorps(corps, assigned, obj.WorldX, obj.WorldZ, vpRadius);
                        if (candidate >= 0)
                        {
                            pickedCorps = candidate;
                            break;
                        }
                    }
                }

                // Fallback: closest unassigned corps to the rect anchor.
                if (pickedCorps < 0)
                {
                    pickedCorps = ClosestUnassignedCorps(corps, assigned, rect.AnchorX, rect.AnchorZ, float.PositiveInfinity);
                }

                if (pickedCorps < 0) continue;
                assigned.Add(pickedCorps);
                result.Add(new CorpsAssignment(
                    childId: corps[pickedCorps].ChildId,
                    rectKind: rect.Kind,
                    rectIndex: rectIdx,
                    rectAnchorX: rect.AnchorX,
                    rectAnchorZ: rect.AnchorZ,
                    startTimeOffsetSeconds: rect.StartTimeOffsetSeconds));
            }

            return result;
        }

        private static int ClosestUnassignedCorps(
            IReadOnlyList<CorpsPosition> corps,
            HashSet<int> assigned,
            float x,
            float z,
            float maxDistance)
        {
            int best = -1;
            float bestDistSq = maxDistance == float.PositiveInfinity
                ? float.MaxValue
                : maxDistance * maxDistance;
            for (int i = 0; i < corps.Count; i++)
            {
                if (assigned.Contains(i)) continue;
                float dx = corps[i].WorldX - x;
                float dz = corps[i].WorldZ - z;
                float dSq = dx * dx + dz * dz;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    best = i;
                }
            }
            return best;
        }

        private static float Diagonal(WorldPlayRect rect)
        {
            float w = rect.HalfExtentX * 2f;
            float h = rect.HalfExtentZ * 2f;
            return (float)Math.Sqrt(w * w + h * h);
        }

        /// <summary>
        /// Returns the role a corps should adopt given the rect kind it was
        /// assigned to. Maps the geometric play layer to the existing
        /// <see cref="DirectChildRole"/> taxonomy.
        /// </summary>
        public static DirectChildRole RoleForRectKind(PlayRectKind kind)
        {
            switch (kind)
            {
                case PlayRectKind.LeftWing:        return DirectChildRole.Main;
                case PlayRectKind.RightWing:       return DirectChildRole.Main;
                case PlayRectKind.FlankMarch:      return DirectChildRole.Main;
                case PlayRectKind.Center:          return DirectChildRole.Fix;
                case PlayRectKind.CenterHold:      return DirectChildRole.RefuseLeft;  // Defender via Refuse role
                case PlayRectKind.LeftFlank:       return DirectChildRole.RefuseLeft;
                case PlayRectKind.RightFlank:      return DirectChildRole.RefuseRight;
                case PlayRectKind.ForwardScreen:   return DirectChildRole.Screen;
                case PlayRectKind.Reserve:         return DirectChildRole.Reserve;
                case PlayRectKind.ReserveLine:     return DirectChildRole.Reserve;
                case PlayRectKind.DeepRaid:        return DirectChildRole.Main;
                case PlayRectKind.Demonstration:   return DirectChildRole.Screen;
                case PlayRectKind.Unknown:
                default:                           return DirectChildRole.Unknown;
            }
        }
    }
}
