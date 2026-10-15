using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical
{
    // Friendly-fire detection gate (2026-05-21). Ports Scourge of War's
    // `StopOverlapTarget(base)` check from `sdk/SowAiInf/unitai.cpp:1484`:
    //
    //   else if (!base.IsFlanked() && !bJustWheel && base.Closest()
    //            && !IsSquareType(base,true,false) && StopOverlapTarget(base))
    //   {
    //       int diff = base.Pos().GetAngleDiff(base.Closest().Loc(),true);
    //       if (diff!=0)
    //       {
    //           base.ICommand(eComState, eUnitStand);  // stop shooting
    //           badshot = true;  bMustWheel = true;
    //       }
    //   }
    //
    // Scourge unit stops firing and wheels when a friendly is between it
    // and its target. Vanilla GT has no equivalent — regiments will happily
    // fire through their own brigade-mates. This gate detects the condition
    // so Whiskey can emit telemetry (and later, optionally act on it).
    //
    // Pure-data scorer: no Unity API touches. Caller captures friendly
    // positions on main; gate runs as pure geometry.
    //
    // Algorithm: a friendly is "in the fire line" when it sits inside a
    // narrow rectangular corridor between the firing unit and the target.
    // We approximate the rectangle with:
    //   - distance projection along the firing line in [0, distanceToTarget]
    //   - lateral offset from the firing line <= LateralOffsetMeters
    // Equivalent to checking whether the friend's perpendicular distance
    // from the line segment falls under the corridor width.
    public static class TacticalFriendlyFireGate
    {
        // Lateral half-width of the fire corridor. A friendly inside this band
        // around the firing line is considered to be in the line of fire.
        // Calibrated to regiment frontage (~40-60m wide) — anything within ~30m
        // perpendicular to the firing line risks taking the volley.
        public const float LateralOffsetMeters = 30f;

        // Skip friends beyond this range from the firing unit; vanilla GT
        // weapons reach further but proximity-based fire-blocking only matters
        // when the friendly is within engagement distance.
        public const float MaxFireRangeMeters = 400f;

        public readonly struct BlockResult
        {
            public BlockResult(bool blocked, string reason, int blockerInstanceId, float blockerDistanceFromLine)
            {
                Blocked = blocked;
                Reason = string.IsNullOrEmpty(reason) ? "unspecified" : reason;
                BlockerInstanceId = blockerInstanceId;
                BlockerDistanceFromLine = blockerDistanceFromLine;
            }

            public bool Blocked { get; }
            public string Reason { get; }
            public int BlockerInstanceId { get; }
            // Perpendicular distance from the friendly to the firing line.
            // 0 = directly on the line. Smaller = more dangerous fire-block.
            public float BlockerDistanceFromLine { get; }

            public static BlockResult Clear => new BlockResult(false, "clear", 0, 0f);
        }

        // Evaluates whether any friendly is in the fire line between
        // (myX, myZ) and (targetX, targetZ). Returns the closest-to-line
        // blocker if multiple qualify.
        //
        // friendlies = list of friendly (instanceId, posX, posZ) captured on main.
        // Reuses the FriendlySnapshot struct from TacticalFriendlyPathBlockingGate
        // so callers can share a single snapshot for path-block + fire-block tests.
        public static BlockResult Evaluate(
            float myX,
            float myZ,
            float targetX,
            float targetZ,
            IList<TacticalFriendlyPathBlockingGate.FriendlySnapshot> friendlies)
        {
            if (friendlies == null || friendlies.Count == 0) return BlockResult.Clear;

            // Vector from me to target.
            float lineDx = targetX - myX;
            float lineDz = targetZ - myZ;
            float lineLengthSq = lineDx * lineDx + lineDz * lineDz;
            if (lineLengthSq <= 1f) return BlockResult.Clear; // target too close = no meaningful fire line
            float lineLength = (float)Math.Sqrt(lineLengthSq);

            float closestBlockerDist = float.MaxValue;
            int closestBlockerId = 0;
            string closestReason = null;

            for (int i = 0; i < friendlies.Count; i++)
            {
                var f = friendlies[i];
                float fdx = f.CurrentX - myX;
                float fdz = f.CurrentZ - myZ;

                // Project friend onto the firing line. t in [0,1] = friend is between me and target.
                // t < 0 means friend is behind me; t > 1 means friend is past the target.
                float t = (fdx * lineDx + fdz * lineDz) / lineLengthSq;
                if (t < 0.05f || t > 0.95f) continue; // clearly not in the fire corridor

                // Distance from friend to firing line (perpendicular).
                // Closest point on segment = me + t * (target - me).
                float closestX = myX + t * lineDx;
                float closestZ = myZ + t * lineDz;
                float perpDx = f.CurrentX - closestX;
                float perpDz = f.CurrentZ - closestZ;
                float perpDistSq = perpDx * perpDx + perpDz * perpDz;
                if (perpDistSq > LateralOffsetMeters * LateralOffsetMeters) continue;

                // Distance from me to friend along the line. Must be within fire range.
                float distAlong = t * lineLength;
                if (distAlong > MaxFireRangeMeters) continue;

                float perpDist = (float)Math.Sqrt(perpDistSq);
                if (perpDist < closestBlockerDist)
                {
                    closestBlockerDist = perpDist;
                    closestBlockerId = f.InstanceId;
                    closestReason = "friendly-in-fire-line";
                }
            }

            if (closestBlockerId != 0)
            {
                return new BlockResult(true, closestReason, closestBlockerId, closestBlockerDist);
            }
            return BlockResult.Clear;
        }
    }
}
