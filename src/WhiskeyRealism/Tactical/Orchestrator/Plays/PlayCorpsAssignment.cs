using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    /// <summary>
    /// One corps's position for the assignment pass.
    /// </summary>
    public readonly struct CorpsPosition
    {
        public CorpsPosition(string childId, float worldX, float worldZ, bool containsPlayerOwned = false)
        {
            ChildId = childId ?? string.Empty;
            WorldX = worldX;
            WorldZ = worldZ;
            ContainsPlayerOwned = containsPlayerOwned;
        }

        /// <summary>
        /// BC14W: the child holds a player-owned unit (<c>PlayAnchor.ContainsPlayerOwned</c>, the one source the executor and the evaluation both fill it
        /// from). Such a child is never given the focal assault's column or support rect.
        /// </summary>
        public bool ContainsPlayerOwned { get; }

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
        public CorpsAssignment(string childId, PlayRectKind rectKind, int rectIndex, float rectAnchorX, float rectAnchorZ, float startTimeOffsetSeconds,
            bool envelops = false, bool templateEnvelops = false, bool projectedMirrored = false, float authoredAnchorX = 0f, float authoredAnchorZ = 0f)
        {
            Envelops = envelops;
            TemplateEnvelops = templateEnvelops;
            ProjectedMirrored = projectedMirrored;
            AuthoredAnchorX = authoredAnchorX;
            AuthoredAnchorZ = authoredAnchorZ;
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
        /// <summary>BC11: the rect this corps was assigned has PlayRect.Envelops.</summary>
        public bool Envelops { get; }

        /// <summary>BC14: some rect of the template envelops (the fixing rects of such a template hold for the synchronized release).</summary>
        public bool TemplateEnvelops { get; }

        /// <summary>BC14: the template was projected mirrored; <see cref="AuthoredAnchorX"/>/<see cref="AuthoredAnchorZ"/> is the rect's un-mirrored anchor.</summary>
        public bool ProjectedMirrored { get; }
        public float AuthoredAnchorX { get; }
        public float AuthoredAnchorZ { get; }
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
            => Assign(rects, corps, objectives, null);

        /// <summary>The assignment with the executor's memory of the last one for the same plan and side (see <see cref="AssignInto(IReadOnlyList{WorldPlayRect}, IReadOnlyList{CorpsPosition}, IReadOnlyList{ObjectivePosition}, IReadOnlyList{CorpsAssignment}, Span{CorpsAssignment})"/>).</summary>
        public static IReadOnlyList<CorpsAssignment> Assign(
            IReadOnlyList<WorldPlayRect> rects,
            IReadOnlyList<CorpsPosition> corps,
            IReadOnlyList<ObjectivePosition> objectives,
            IReadOnlyList<CorpsAssignment> previous)
        {
            if (rects == null || rects.Count == 0) return Array.Empty<CorpsAssignment>();
            if (corps == null || corps.Count == 0) return Array.Empty<CorpsAssignment>();
            var into = new CorpsAssignment[rects.Count];
            int n = AssignInto(rects, corps, objectives, previous, into);
            var result = new List<CorpsAssignment>(n);
            for (int i = 0; i < n; i++) result.Add(into[i]);
            return result;
        }

        /// <summary>
        /// Allocation-free <see cref="Assign"/> (BC14's hypothetical layout): the same rules and the same result order, written to
        /// <paramref name="into"/> (at least the rect count long); returns how many rects got a corps.
        /// </summary>
        public static int AssignInto(
            IReadOnlyList<WorldPlayRect> rects,
            IReadOnlyList<CorpsPosition> corps,
            IReadOnlyList<ObjectivePosition> objectives,
            Span<CorpsAssignment> into)
            => AssignInto(rects, corps, objectives, null, into);

        /// <summary>
        /// The same assignment with the executor's memory: a corps that held a rect of this template (<paramref name="previous"/>: the last result for
        /// the same plan and side, the same rect index and kind) keeps it while it is still offered, whoever is closer now; only the vacated rects, and
        /// the corps without one, are paired by the closest-free rule. The old pairing re-ran the closest rule every few seconds, so a corps marching to
        /// its flank rect became the Center the moment it passed the Center's holder, and the Center was sent to the flank (smoke 2026-10-17). A child that
        /// holds player-owned strength is never kept in the focal assault's column or support. BC14's hypothetical layouts call the overload without
        /// <paramref name="previous"/>: they price the layout the pick would make, not the one the executor holds.
        /// </summary>
        public static int AssignInto(
            IReadOnlyList<WorldPlayRect> rects,
            IReadOnlyList<CorpsPosition> corps,
            IReadOnlyList<ObjectivePosition> objectives,
            IReadOnlyList<CorpsAssignment> previous,
            Span<CorpsAssignment> into)
        {
            if (rects == null || rects.Count == 0) return 0;
            if (corps == null || corps.Count == 0) return 0;

            // Rect indices by Priority desc, then original index (insertion sort: a handful of rects, no closure).
            Span<int> rectOrder = stackalloc int[rects.Count];
            for (int i = 0; i < rects.Count; i++)
            {
                int at = i;
                while (at > 0 && Before(rects, i, rectOrder[at - 1])) { rectOrder[at] = rectOrder[at - 1]; at--; }
                rectOrder[at] = i;
            }

            bool templateEnvelops = false;
            for (int i = 0; i < rects.Count; i++) if (rects[i].Envelops) templateEnvelops = true;

            Span<bool> assigned = stackalloc bool[corps.Count];       // indices into corps
            int result = 0;

            // Incumbents first, so no rect's closest-corps pick can take another rect's holder.
            Span<int> incumbent = stackalloc int[rects.Count];
            for (int i = 0; i < incumbent.Length; i++) incumbent[i] = -1;
            for (int p = 0; previous != null && p < previous.Count; p++)
            {
                CorpsAssignment held = previous[p];
                int ri = held.RectIndex;
                if (ri < 0 || ri >= rects.Count || incumbent[ri] >= 0 || rects[ri].Kind != held.RectKind) continue;
                bool column = rects[ri].Kind == PlayRectKind.AssaultColumn || rects[ri].Kind == PlayRectKind.AssaultSupport;
                for (int c = 0; c < corps.Count; c++)
                {
                    if (assigned[c] || !string.Equals(corps[c].ChildId, held.ChildId, StringComparison.Ordinal)) continue;
                    if (column && corps[c].ContainsPlayerOwned) break;
                    incumbent[ri] = c;
                    assigned[c] = true;
                    break;
                }
            }

            for (int ri = 0; ri < rectOrder.Length && result < into.Length; ri++)
            {
                int rectIdx = rectOrder[ri];
                var rect = rects[rectIdx];

                int pickedCorps = incumbent[rectIdx];
                // BC14W: a child that holds a player-owned unit is never given the focal assault's column or support (the player decides nothing in it).
                bool skipPlayerOwned = rect.Kind == PlayRectKind.AssaultColumn || rect.Kind == PlayRectKind.AssaultSupport;

                // VP override: any major VP inside this rect, and any unassigned
                // corps near it?
                if (pickedCorps < 0 && objectives != null && objectives.Count > 0)
                {
                    float rectDiagonal = Diagonal(rect);
                    float vpRadius = rectDiagonal * 1.5f;
                    for (int oi = 0; oi < objectives.Count; oi++)
                    {
                        var obj = objectives[oi];
                        if (!obj.MajorVp) continue;
                        if (!rect.ContainsPoint(obj.WorldX, obj.WorldZ)) continue;
                        int candidate = ClosestUnassignedCorps(corps, assigned, obj.WorldX, obj.WorldZ, vpRadius, skipPlayerOwned);
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
                    pickedCorps = ClosestUnassignedCorps(corps, assigned, rect.AnchorX, rect.AnchorZ, float.PositiveInfinity, skipPlayerOwned);
                }

                if (pickedCorps < 0) continue;
                assigned[pickedCorps] = true;
                into[result++] = new CorpsAssignment(
                    childId: corps[pickedCorps].ChildId,
                    rectKind: rect.Kind,
                    rectIndex: rectIdx,
                    rectAnchorX: rect.AnchorX,
                    rectAnchorZ: rect.AnchorZ,
                    startTimeOffsetSeconds: rect.StartTimeOffsetSeconds,
                    envelops: rect.Envelops,
                    templateEnvelops: templateEnvelops,
                    projectedMirrored: rect.Mirrored,
                    authoredAnchorX: rect.AuthoredX,
                    authoredAnchorZ: rect.AuthoredZ);
            }

            return result;
        }

        // Priority descending, then the lower index first: a sorts before b.
        private static bool Before(IReadOnlyList<WorldPlayRect> rects, int a, int b)
        {
            int pa = rects[a].Priority, pb = rects[b].Priority;
            if (pa != pb) return pa > pb;
            return a < b;
        }

        private static int ClosestUnassignedCorps(
            IReadOnlyList<CorpsPosition> corps,
            ReadOnlySpan<bool> assigned,
            float x,
            float z,
            float maxDistance,
            bool skipPlayerOwned = false)
        {
            int best = -1;
            float bestDistSq = maxDistance == float.PositiveInfinity
                ? float.MaxValue
                : maxDistance * maxDistance;
            for (int i = 0; i < corps.Count; i++)
            {
                if (assigned[i]) continue;
                if (skipPlayerOwned && corps[i].ContainsPlayerOwned) continue;
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
                case PlayRectKind.AssaultColumn:   return DirectChildRole.Main;
                case PlayRectKind.AssaultSupport:  return DirectChildRole.SupportMain;
                case PlayRectKind.Unknown:
                default:                           return DirectChildRole.Unknown;
            }
        }
    }
}
