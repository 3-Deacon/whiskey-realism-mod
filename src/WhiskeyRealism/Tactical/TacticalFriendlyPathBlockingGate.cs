using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical
{
    // Friendly-path blocking gate (2026-05-21). Ports Scourge of War's three-layer
    // waypoint-issuance gate from `sdk/SowAiInf/offcmds.cpp`:
    //
    //   Layer 1 — FriendAheadMe (offcmds.cpp:448): 30°/45°/70° angle cones
    //     forward of `me`. If a friendly is inside the cone AND within range,
    //     the movement is blocked. The wider cones (45°, 70°) only fire when
    //     the moving unit AND/OR the friendly is in line formation.
    //
    //   Layer 2 — CanRedeployLine (offcmds.cpp:207): destination overlap check.
    //     If `myProposedTarget` is within BrigadeSpacingMeters of any sibling's
    //     last-issued waypoint target, refuse the order. **This is the
    //     direct H-formation gate** — prevents two brigades from being told to
    //     move to overlapping positions in opposite directions.
    //
    //   Layer 3 — SupportUnits cohesion (offai.cpp:564): if any sibling is
    //     >BrigadeCohesionMaxMeters from us right NOW, the brigade is already
    //     spread thin; refuse new movements that would worsen the spread.
    //
    // Pure-data scorer: no Unity API touches. Callers capture friendly state
    // into a FriendlySnapshot list on the calling thread (main, since these
    // reads come from `Regiment.transform.position` and `Regiment.lastsetwaypointposition`)
    // and pass to Evaluate(). Single linear scan over the list per evaluation.
    //
    // Constants are tuned from Scourge values scaled to GT meters (Scourge's
    // LongRangeYds ≈ 100-200 yd, GT's analogous engagement range ≈ 140 m).
    public static class TacticalFriendlyPathBlockingGate
    {
        // Layer 1 cone radii (Scourge: 30/45/70 degrees).
        public const float NarrowConeDegrees = 30f;
        public const float MediumConeDegrees = 45f;
        public const float WideConeDegrees = 70f;

        // Layer 1 distance thresholds (Scourge: LongRangeYds/2, LongRangeYds).
        public const float NarrowConeRangeMeters = 70f;   // close-block (any formation)
        public const float MediumConeRangeMeters = 140f;  // line-formation block
        public const float WideConeRangeMeters = 140f;    // both-line block

        // Layer 2: destination overlap. CALIBRATION HISTORY + CORRECT
        // INTERPRETATION (2026-05-22):
        //   140m initial → 78% false-positive (over-blocked cross-corps)
        //   280m mid-fix → reduced cross-corps but over-blocked siblings
        //   80m         → still over-blocks (606 sibling-blocks in last smoke)
        //   30m current → effective near-disable, only catches "literal same spot"
        //
        // Root cause of the prior calibration churn: **misapplied Scourge rule**.
        // Scourge's CanRedeployLine (offcmds.cpp:207) iterates `off.NumSubs()`
        // — i.e., a parent brigade's SUBORDINATE REGIMENTS — and checks if a
        // proposed regiment-level waypoint overlaps a SIBLING REGIMENT's
        // destination. The actual caller signature is
        //   CanRedeployLine(lead, unit, pt)  // lead=brigade, unit=regiment, pt=dest
        //
        // That rule applies when issuing PER-REGIMENT waypoints, with the
        // overlap measured between SIBLING REGIMENTS of the SAME BRIGADE.
        // It is NOT a brigade-vs-brigade spacing rule. Whiskey writes
        // waypoints at the BRIGADE level (`bunits.SetWaypoint(brigade, target)`)
        // so this rule has no direct analog in our current write surface.
        //
        // For cross-brigade path-conflict avoidance, Scourge uses
        // `FriendAheadMe` (offcmds.cpp:448) — our Layer 1 angle cones — which
        // fires for any friendly in the forward arc regardless of relationship.
        // That IS the right rule for brigade-level movement and is unchanged.
        //
        // Layer 2 stays present (so future per-regiment waypoint writes can
        // use it) but at 30m it functions as a near-disable for brigade-level
        // calls: only literal same-spot destinations trigger.
        public const float BrigadeSpacingMeters = 30f;

        // Layer 3: cohesion catch-up. Scourge uses DIST2OFFCATCHUP = 125²,
        // i.e. 125 yd ≈ 115 m. We honor that for sibling-spread detection.
        public const float BrigadeCohesionMaxMeters = 115f;

        // Snapshot of one friendly unit's state at the moment the calling
        // patch decides whether to issue a waypoint. Captured on main from
        // BattleUnits.completeunitlist; immutable thereafter; passed to the
        // pure scorer.
        public readonly struct FriendlySnapshot
        {
            public FriendlySnapshot(
                int instanceId,
                float currentX,
                float currentZ,
                float waypointX,
                float waypointZ,
                bool hasWaypoint,
                bool isLineFormation,
                int parentBrigadeInstanceId = 0)
            {
                InstanceId = instanceId;
                CurrentX = currentX;
                CurrentZ = currentZ;
                WaypointX = waypointX;
                WaypointZ = waypointZ;
                HasWaypoint = hasWaypoint;
                IsLineFormation = isLineFormation;
                ParentBrigadeInstanceId = parentBrigadeInstanceId;
            }

            public int InstanceId { get; }
            public float CurrentX { get; }
            public float CurrentZ { get; }
            public float WaypointX { get; }
            public float WaypointZ { get; }
            public bool HasWaypoint { get; }
            public bool IsLineFormation { get; }
            // Parent brigade/group identity (2026-05-22 scope fix #5). Used to
            // limit Layer 2 destination-overlap checks to SIBLING brigades.
            // 0 = unknown (treat as "no sibling relationship known", which
            // means destination-overlap won't fire — safer than over-blocking).
            public int ParentBrigadeInstanceId { get; }
        }

        public readonly struct BlockResult
        {
            public BlockResult(bool blocked, string reason, int blockerInstanceId)
            {
                Blocked = blocked;
                Reason = string.IsNullOrEmpty(reason) ? "unspecified" : reason;
                BlockerInstanceId = blockerInstanceId;
            }

            public bool Blocked { get; }
            public string Reason { get; }
            public int BlockerInstanceId { get; }

            public static BlockResult Clear => new BlockResult(false, "clear", 0);
        }

        // Evaluates whether a proposed waypoint would conflict with friendlies.
        //
        // myCurrentX/Z = position of the unit about to be ordered.
        // myTargetX/Z = the proposed destination of the new waypoint.
        // myIsLineFormation = whether the ordered unit is currently in line.
        // friendlies = list of sibling friendly snapshots (caller excludes self).
        public static BlockResult Evaluate(
            float myCurrentX,
            float myCurrentZ,
            float myTargetX,
            float myTargetZ,
            bool myIsLineFormation,
            IList<FriendlySnapshot> friendlies,
            int myParentBrigadeInstanceId = 0)
        {
            if (friendlies == null || friendlies.Count == 0) return BlockResult.Clear;

            // Movement bearing in degrees [0, 360). Used for cone tests in Layer 1.
            float bearing = BearingDegrees(myCurrentX, myCurrentZ, myTargetX, myTargetZ);

            // Layer 2 (destination overlap) — sibling-scoped (2026-05-22 fix #5).
            // Scourge's CanRedeployLine rule applies to SIBLING REGIMENTS within
            // a brigade. Cross-brigade destination overlap is normal (independent
            // commands coordinating). The 78%-false-positive smoke result came
            // from applying this check across all friendlies regardless of
            // parent. Now requires same parent brigade/group to fire.
            //
            // If myParentBrigadeInstanceId is 0 (unknown), skip Layer 2 entirely
            // — we can't determine sibling relationships, so don't block. The
            // angle-cone Layer 1 still fires for actual path conflicts.
            if (myParentBrigadeInstanceId != 0)
            {
                for (int i = 0; i < friendlies.Count; i++)
                {
                    var f = friendlies[i];
                    if (!f.HasWaypoint) continue;
                    if (f.ParentBrigadeInstanceId == 0) continue;
                    if (f.ParentBrigadeInstanceId != myParentBrigadeInstanceId) continue;
                    float dx = f.WaypointX - myTargetX;
                    float dz = f.WaypointZ - myTargetZ;
                    float distSq = dx * dx + dz * dz;
                    if (distSq <= BrigadeSpacingMeters * BrigadeSpacingMeters)
                    {
                        return new BlockResult(
                            true,
                            "destination-overlap-sibling-waypoint",
                            f.InstanceId);
                    }
                }
            }

            // Layer 1 (frontal cones) — friendlies in our forward arc are
            // ahead and would be in our path during the move.
            for (int i = 0; i < friendlies.Count; i++)
            {
                var f = friendlies[i];
                float dx = f.CurrentX - myCurrentX;
                float dz = f.CurrentZ - myCurrentZ;
                float distSq = dx * dx + dz * dz;
                // Pre-filter on the widest range to avoid sqrt cost for far units.
                if (distSq > MediumConeRangeMeters * MediumConeRangeMeters) continue;
                float dist = MathSqrt(distSq);

                float friendlyBearing = BearingDegrees(myCurrentX, myCurrentZ, f.CurrentX, f.CurrentZ);
                float relativeBearing = NormalizeBearingDegrees(friendlyBearing - bearing);
                float absRel = relativeBearing < 0f ? -relativeBearing : relativeBearing;

                // Narrow cone (30°): always blocks at close range.
                if (absRel <= NarrowConeDegrees && dist <= NarrowConeRangeMeters)
                {
                    return new BlockResult(true, "friendly-in-narrow-cone", f.InstanceId);
                }
                // Medium cone (45°): blocks if I'm in line OR friend is in line.
                if (absRel <= MediumConeDegrees && dist <= MediumConeRangeMeters
                    && (myIsLineFormation || f.IsLineFormation))
                {
                    return new BlockResult(true, "friendly-in-medium-cone-line", f.InstanceId);
                }
                // Wide cone (70°): blocks if BOTH in line.
                if (absRel <= WideConeDegrees && dist <= WideConeRangeMeters
                    && myIsLineFormation && f.IsLineFormation)
                {
                    return new BlockResult(true, "friendly-in-wide-cone-both-line", f.InstanceId);
                }
            }

            // Layer 3 (cohesion gate) — if a brigade-mate is already spread
            // far from me, issuing a new movement risks worsening the spread.
            // We don't outright block; we only fire when the spread exceeds
            // the catch-up threshold AND the new movement extends it further.
            // (Conservative: this layer is informational unless extension is
            // detected.)
            for (int i = 0; i < friendlies.Count; i++)
            {
                var f = friendlies[i];
                float dxCur = f.CurrentX - myCurrentX;
                float dzCur = f.CurrentZ - myCurrentZ;
                float curDistSq = dxCur * dxCur + dzCur * dzCur;
                if (curDistSq <= BrigadeCohesionMaxMeters * BrigadeCohesionMaxMeters) continue;
                // Currently spread. Would the new movement widen the gap?
                float dxNew = f.CurrentX - myTargetX;
                float dzNew = f.CurrentZ - myTargetZ;
                float newDistSq = dxNew * dxNew + dzNew * dzNew;
                if (newDistSq > curDistSq * 1.20f) // worsens spread by >20%
                {
                    return new BlockResult(true, "cohesion-spread-worsens", f.InstanceId);
                }
            }

            return BlockResult.Clear;
        }

        // Compass-style bearing from (fromX, fromZ) to (toX, toZ) in degrees [0, 360).
        public static float BearingDegrees(float fromX, float fromZ, float toX, float toZ)
        {
            float dx = toX - fromX;
            float dz = toZ - fromZ;
            if (dx == 0f && dz == 0f) return 0f;
            float radians = (float)Math.Atan2(dx, dz);
            float degrees = radians * 57.2957795f;
            if (degrees < 0f) degrees += 360f;
            return degrees;
        }

        // Normalizes a degree value to (-180, 180].
        public static float NormalizeBearingDegrees(float degrees)
        {
            float d = degrees;
            while (d > 180f) d -= 360f;
            while (d <= -180f) d += 360f;
            return d;
        }

        private static float MathSqrt(float v)
        {
            if (v <= 0f) return 0f;
            return (float)Math.Sqrt(v);
        }
    }
}
