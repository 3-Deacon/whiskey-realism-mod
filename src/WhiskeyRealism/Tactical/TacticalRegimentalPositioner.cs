using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical
{
    // Scope C: regimental waypoint granularity (2026-05-22).
    //
    // Architectural shift: Whiskey previously wrote SetWaypoint at the brigade
    // level only — vanilla cascaded the order to attached regiments using its
    // default deployment logic (vanilla picks line/column/etc. and positions
    // regiments wherever its micro-AI thinks they should go). That left vanilla
    // making the per-regiment positioning calls, which is where "4th Michigan
    // does something weird" behaviors originated.
    //
    // Scope C: after the brigade-level stance + waypoint write, walk the
    // brigade's attached regiments and ISSUE PER-REGIMENT WAYPOINTS for their
    // specific slot in the formation. This makes Whiskey's per-regiment
    // intent explicit and gives us the Scourge `CanRedeployLine`
    // (offcmds.cpp:207) rule's natural scope — destination overlap between
    // SIBLING REGIMENTS of the same brigade.
    //
    // Pure function: no Unity API. Caller passes brigade position + target,
    // list of regiment IDs (or anything else identifying them), and gets back
    // per-regiment slot positions.
    //
    // Formation:
    //   - Frontline axis perpendicular to (brigade → target) direction
    //   - Regiments slotted left-to-right along the axis
    //   - Spacing: ~80m per regiment (typical ACW line-formation frontage)
    //   - Brigade waypoint = formation center (the target the user gave)
    //   - First regiment at center; subsequent alternate left/right outward
    //
    // Task-specific variants (future): currently uniform line. Future
    // enhancements can adjust:
    //   - Attack: form line slightly closer to enemy, double-rank possible
    //   - Defense: refused flanks (RefuseLeft/RefuseRight regiments pulled back)
    //   - Screen: wider spacing for screening
    //   - Reserve: depth (behind front line)
    public static class TacticalRegimentalPositioner
    {
        // Default per-regiment frontage at line formation (meters). Calibrated
        // to ACW typical: ~500-man regiment at 2-rank line ≈ 100yd frontage =
        // ~90m. Allowing small gap (~10m) between regiments → 100m total
        // center-to-center spacing.
        public const float RegimentFrontageMeters = 90f;
        public const float RegimentSpacingMeters = RegimentFrontageMeters + 10f;

        // Output: where a specific regiment should be positioned in the
        // formation, relative to the brigade frontline.
        public readonly struct RegimentSlot
        {
            public RegimentSlot(int regimentInstanceId, float worldX, float worldZ, float facingDegrees, int laneIndex)
            {
                RegimentInstanceId = regimentInstanceId;
                WorldX = worldX;
                WorldZ = worldZ;
                FacingDegrees = facingDegrees;
                LaneIndex = laneIndex;
            }
            public int RegimentInstanceId { get; }
            public float WorldX { get; }
            public float WorldZ { get; }
            public float FacingDegrees { get; }
            // 0 = center, -1 = first slot to left, +1 = first slot to right, etc.
            public int LaneIndex { get; }
        }

        // Computes per-regiment slot positions for a brigade moving toward
        // `targetX/Z`. `brigadeCenterX/Z` is the brigade's intended formation
        // center (typically the same as targetX/Z but can be offset if the
        // brigade should be set back from the named target).
        //
        // `regimentIds` is the ordered list of regiments. The list order
        // determines slot assignment: regiments[0] gets center, regiments[1]
        // gets first slot left, regiments[2] gets first slot right, etc.
        //
        // Returns the slot assignments. Empty list returns empty.
        public static List<RegimentSlot> ComputeSlots(
            float brigadeCenterX,
            float brigadeCenterZ,
            float targetX,
            float targetZ,
            IList<int> regimentIds)
        {
            var result = new List<RegimentSlot>();
            if (regimentIds == null || regimentIds.Count == 0) return result;

            // Direction from brigade center toward target. This determines the
            // frontline axis (perpendicular to this direction).
            float dx = targetX - brigadeCenterX;
            float dz = targetZ - brigadeCenterZ;
            float lenSq = dx * dx + dz * dz;
            float bearing;
            if (lenSq < 1f)
            {
                // Brigade at target — no direction info. Default to north-facing.
                bearing = 0f;
            }
            else
            {
                float len = (float)Math.Sqrt(lenSq);
                dx /= len;
                dz /= len;
                bearing = (float)(Math.Atan2(dx, dz) * 57.2957795);
                if (bearing < 0f) bearing += 360f;
            }

            // Perpendicular axis for slotting regiments left↔right.
            // Right perpendicular: rotate forward vector +90° clockwise (in
            // map terms: if facing north, right is east).
            float rightX = dz;       // sin(b+90) where b = atan2(dx,dz) → cos(b) = dz/len
            float rightZ = -dx;      // cos(b+90) = -sin(b) = -dx/len

            // Slot regiments. regimentIds[0] = lane 0 (center), then alternate.
            for (int i = 0; i < regimentIds.Count; i++)
            {
                int lane = LaneIndexFor(i);
                float offsetMeters = lane * RegimentSpacingMeters;
                float slotX = targetX + rightX * offsetMeters;
                float slotZ = targetZ + rightZ * offsetMeters;
                result.Add(new RegimentSlot(
                    regimentInstanceId: regimentIds[i],
                    worldX: slotX,
                    worldZ: slotZ,
                    facingDegrees: bearing,
                    laneIndex: lane));
            }
            return result;
        }

        // Maps the index in the regiment list to a lane index:
        //   0 → 0    (center)
        //   1 → -1   (first left)
        //   2 → +1   (first right)
        //   3 → -2   (second left)
        //   4 → +2   (second right)
        //   etc.
        // Produces a symmetric outward expansion from center.
        public static int LaneIndexFor(int orderIndex)
        {
            if (orderIndex == 0) return 0;
            int step = (orderIndex + 1) / 2;
            return (orderIndex % 2 == 1) ? -step : step;
        }
    }
}
