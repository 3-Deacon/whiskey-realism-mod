using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// A main effort advances only with its flanks covered (smoke 2026-10-17: a lone main-effort brigade, 5th Massachusetts, advanced with no neighbour and
    /// no flank guard and was shot in the flank). A flank of the main is exposed when no friendly line unit stands on that side within a frontage plus a gap
    /// sideways (<see cref="ReachMeters"/>) and within <see cref="DepthReachMeters"/> forward or back of the main's own position, so a main that has advanced
    /// ahead of its line is not covered by the line it left. An exposed flank is covered when a line unit has come within that reach or it rests on terrain the
    /// snapshot marks impassable (a river, <c>TacticalTerrainLinePlanner.FlankAnchored</c>). Otherwise the main holds, and the allocation
    /// (<see cref="AssignGuards"/>) gives the exposed side a guard from the reserve, else from the support pool: a unit sent to a post at the main's flank
    /// (<see cref="PostOf"/>: one frontage out, refused back, facing outward) by a real Move (FlankGuardLeaf). A guard moving toward its post is in transit and
    /// the hold goes on until it is in reach; one that is not moving, or none, leaves the flank unguarded. The hold is bounded (<see cref="MaxHoldBattleSeconds"/>):
    /// with no guard to give, or one that does not arrive, the main advances anyway, so a formation with nobody to spare still attacks.
    /// Pure: the posture producer measures the neighbours; the leaf map's overlay assigns the guards.
    /// </summary>
    public static class MainFlankGuard
    {
        /// <summary>A brigade's frontage in line.</summary>
        public const float FrontageMeters = 450f;

        /// <summary>The gap a line may leave between neighbours before the flank beside it counts as open.</summary>
        public const float GapMeters = 250f;

        public const float ReachMeters = FrontageMeters + GapMeters;

        /// <summary>How far forward or back of the main a unit may stand and still be abreast of it.</summary>
        public const float DepthReachMeters = 400f;

        /// <summary>A unit nearer the main than this, sideways, is behind it or on top of it and covers no flank.</summary>
        public const float MinFlankOffsetMeters = 100f;

        /// <summary>The farthest a unit stands from the main and can still be given its flank.</summary>
        public const float GuardRangeMeters = 2500f;

        /// <summary>How far past the main's centre its flank end lies, for the terrain test (half a frontage).</summary>
        public const float FlankEndMeters = FrontageMeters * 0.5f;

        /// <summary>The post of a flank guard: one frontage out from the main, sideways.</summary>
        public const float PostLateralMeters = FrontageMeters;

        /// <summary>The post is refused back: this far behind the main's own line, so the guard is not the first thing a flanking enemy meets.</summary>
        public const float PostRefusedBackMeters = 150f;

        /// <summary>The post faces outward, this many degrees off the front toward the open side.</summary>
        public const float PostFacingDegrees = 45f;

        /// <summary>A guard is "at" its post, and its order "toward" it, within this distance of it.</summary>
        public const float PostToleranceMeters = 150f;

        /// <summary>
        /// The longest a hold lasts (battle seconds, doctrine clock): 20 battle-minutes. Twenty minutes is a third of the plan commitment hold
        /// (<c>ArmyReplanTriggers.PlanCommitmentBattleSeconds</c>, an hour), so a hold never outlives the plan it serves; it is longer than the arrival hold's
        /// 15-minute budget (<c>MainArrivalHoldLatch.MaxHoldBattleSeconds</c>), which a main may also wait out after it; and it is long enough for a guard
        /// from the far end of its range (<see cref="GuardRangeMeters"/>) to march up at brigade pace. Past it the main advances with an open flank rather than
        /// never attack.
        /// </summary>
        public const float MaxHoldBattleSeconds = 20f * 60f;

        /// <summary>
        /// The flank must stay covered this long (battle seconds) before an advance with an open flank is cleared: five minutes, a quarter of the bound. One
        /// covered look in a flickering line (a guard passing in and out of reach) does not re-arm the hold.
        /// </summary>
        public const float AdvancedClearCoveredBattleSeconds = 5f * 60f;

        public const string HoldReason = "flank-unguarded-hold";
        public const string InTransitReason = "flank-guard-in-transit";
        public const string AdvanceReason = "flank-unguarded-advance";
        public const string ReleaseReason = "flank-covered-release";

        /// <summary>A hold lasts at least this long (battle seconds): a neighbour crossing the reach line does not flip the main between holding and advancing.</summary>
        public const float MinHoldBattleSeconds = 120f;

        /// <summary>The flanks must have stayed covered this long (battle seconds) before a hold ends.</summary>
        public const float CoveredBattleSeconds = 60f;

        /// <summary>The roles a friendly line unit beside the main has: a unit that fights or guards on the line, not one held back.</summary>
        public static bool IsLine(DirectChildRole role)
        {
            return role == DirectChildRole.Main || role == DirectChildRole.SupportMain || role == DirectChildRole.Fix || role == DirectChildRole.Screen
                || role == DirectChildRole.RefuseLeft || role == DirectChildRole.RefuseRight;
        }

        /// <summary>
        /// One friendly unit seen from the main: <see cref="Lateral"/> is its offset sideways from the main along the front (negative is left),
        /// <see cref="Depth"/> its offset forward (negative is behind). <see cref="PostSide"/> is -1 or 1 when the unit is this main's flank guard and is moving
        /// toward its post on the left or right, else 0.
        /// </summary>
        public readonly struct Neighbour
        {
            public Neighbour(float lateral, float depth, DirectChildRole role, int postSide = 0)
            {
                Lateral = lateral;
                Depth = depth;
                Role = role;
                PostSide = postSide;
            }

            public float Lateral { get; }
            public float Depth { get; }
            public DirectChildRole Role { get; }
            public int PostSide { get; }
        }

        public readonly struct Cover
        {
            public Cover(bool leftExposed, bool rightExposed, bool leftGuarded, bool rightGuarded, bool leftAnchored, bool rightAnchored)
            {
                LeftExposed = leftExposed;
                RightExposed = rightExposed;
                LeftGuarded = leftGuarded;
                RightGuarded = rightGuarded;
                LeftAnchored = leftAnchored;
                RightAnchored = rightAnchored;
            }

            public bool LeftExposed { get; }
            public bool RightExposed { get; }

            /// <summary>A flank guard of this main is moving toward its post on the left (it is not in reach yet).</summary>
            public bool LeftGuarded { get; }
            public bool RightGuarded { get; }
            public bool LeftAnchored { get; }
            public bool RightAnchored { get; }

            /// <summary>The left flank has no line unit in reach and does not rest on a river (a guard on its way to it is still on its way).</summary>
            public bool LeftOpen => LeftExposed && !LeftAnchored;
            public bool RightOpen => RightExposed && !RightAnchored;

            /// <summary>A flank is open: the main does not advance (until the hold's bound).</summary>
            public bool Open => LeftOpen || RightOpen;

            /// <summary>An open flank has no guard moving to it: nobody is on the way.</summary>
            public bool Unguarded => (LeftOpen && !LeftGuarded) || (RightOpen && !RightGuarded);

            /// <summary>Every open flank has a guard moving toward its post.</summary>
            public bool InTransit => Open && !Unguarded;

            public string Reason => LeftOpen ? (RightOpen ? "both-flanks-open" : "left-flank-open") : RightOpen ? "right-flank-open" : "flanks-covered";

            /// <summary>Why the main is held while this cover is open.</summary>
            public string HoldWhy => Unguarded ? HoldReason : InTransitReason;
        }

        public static Cover Assess(IReadOnlyList<Neighbour> others, bool leftAnchored, bool rightAnchored)
        {
            bool leftNear = false, rightNear = false, leftGuard = false, rightGuard = false;
            for (int i = 0; others != null && i < others.Count; i++)
            {
                float lateral = others[i].Lateral;
                float depth = others[i].Depth;
                if (float.IsNaN(lateral) || float.IsInfinity(lateral) || float.IsNaN(depth) || float.IsInfinity(depth)) continue;
                if (others[i].PostSide < 0) leftGuard = true;
                else if (others[i].PostSide > 0) rightGuard = true;
                if (!IsAbreast(lateral, depth, others[i].Role)) continue;
                if (lateral < 0f) leftNear = true;
                else rightNear = true;
            }
            return new Cover(!leftNear, !rightNear, leftGuard, rightGuard, leftAnchored, rightAnchored);
        }

        // A line unit beside the main: past the stacked-behind offset, within a frontage plus a gap sideways and within the depth reach.
        private static bool IsAbreast(float lateral, float depth, DirectChildRole role)
        {
            float distance = Math.Abs(lateral);
            return IsLine(role) && distance >= MinFlankOffsetMeters && distance <= ReachMeters && Math.Abs(depth) <= DepthReachMeters;
        }

        /// <summary>
        /// The post of the main's flank guard on the left (<paramref name="left"/>) or right: one frontage out sideways, <see cref="PostRefusedBackMeters"/> behind
        /// the main, facing <see cref="PostFacingDegrees"/> off the front toward the open side. The axis is the side's front axis (right is
        /// <c>(ForwardZ, -ForwardX)</c>, the sign of the lateral).
        /// </summary>
        public static void PostOf(in TacticalFrontAxis axis, float mainX, float mainZ, bool left, out float x, out float z, out float heading)
        {
            float side = left ? -1f : 1f;
            x = mainX + axis.RightX * side * PostLateralMeters - axis.ForwardX * PostRefusedBackMeters;
            z = mainZ + axis.RightZ * side * PostLateralMeters - axis.ForwardZ * PostRefusedBackMeters;
            double a = PostFacingDegrees * Math.PI / 180.0;
            double dx = axis.ForwardX * Math.Cos(a) + axis.RightX * side * Math.Sin(a);
            double dz = axis.ForwardZ * Math.Cos(a) + axis.RightZ * side * Math.Sin(a);
            double deg = Math.Atan2(dx, dz) * 180.0 / Math.PI;
            if (deg < 0) deg += 360.0;
            heading = (float)deg;
        }

        /// <summary>
        /// The main's hold on the doctrine clock: whether it is holding, since when, since when its flanks have been covered (negative: not covered), and
        /// whether the hold ran out its bound and the main advances with the flank open (<see cref="Advanced"/>, kept until the flanks have stayed covered).
        /// </summary>
        public readonly struct HoldState
        {
            public HoldState(bool holding, float heldSinceHours, float coveredSinceHours, bool advanced = false)
            {
                Holding = holding;
                HeldSinceHours = heldSinceHours;
                CoveredSinceHours = coveredSinceHours;
                Advanced = advanced;
            }

            public bool Holding { get; }
            public float HeldSinceHours { get; }
            public float CoveredSinceHours { get; }
            public bool Advanced { get; }
        }

        /// <summary>
        /// An open flank starts the hold at once and it runs until the flanks are covered or <see cref="MaxHoldBattleSeconds"/> have passed since it began, when
        /// the main advances anyway and stays released while the flank stays open. A hold ends on cover only after it has lasted
        /// <see cref="MinHoldBattleSeconds"/> and the flanks have been covered for <see cref="CoveredBattleSeconds"/> without a break; an open flank again
        /// restarts that count. The advance is cleared only after the flanks have stayed covered for <see cref="AdvancedClearCoveredBattleSeconds"/>, an open
        /// look restarting that count, so a flicker in the cover does not give the main a new 20-minute hold.
        /// </summary>
        public static HoldState Step(HoldState previous, bool open, float nowHours)
        {
            if (previous.Advanced)
            {
                if (open) return new HoldState(false, previous.HeldSinceHours, -1f, advanced: true);
                float since = previous.CoveredSinceHours >= 0f ? previous.CoveredSinceHours : nowHours;
                if (TacticalBattleClock.HoursToBattleSeconds(nowHours - since) >= AdvancedClearCoveredBattleSeconds) return default(HoldState);
                return new HoldState(false, previous.HeldSinceHours, since, advanced: true);
            }
            if (open)
            {
                if (!previous.Holding) return new HoldState(true, nowHours, -1f);
                if (TacticalBattleClock.HoursToBattleSeconds(nowHours - previous.HeldSinceHours) >= MaxHoldBattleSeconds)
                    return new HoldState(false, previous.HeldSinceHours, -1f, advanced: true);
                return new HoldState(true, previous.HeldSinceHours, -1f);
            }
            if (!previous.Holding) return default(HoldState);

            float coveredSince = previous.CoveredSinceHours >= 0f ? previous.CoveredSinceHours : nowHours;
            float held = TacticalBattleClock.HoursToBattleSeconds(nowHours - previous.HeldSinceHours);
            float covered = TacticalBattleClock.HoursToBattleSeconds(nowHours - coveredSince);
            if (held >= MinHoldBattleSeconds && covered >= CoveredBattleSeconds) return default(HoldState);
            return new HoldState(true, previous.HeldSinceHours, coveredSince);
        }

        /// <summary>The reason of the row a change of the hold writes, or null when the hold did not change.</summary>
        public static string ChangeReason(HoldState previous, HoldState next, Cover cover)
        {
            if (!previous.Holding && next.Holding) return cover.HoldWhy;
            if (previous.Holding && !next.Holding) return next.Advanced ? AdvanceReason : ReleaseReason;
            return null;
        }

        /// <summary>A flank post a unit holds for a main: the main's instance id and the side.</summary>
        public readonly struct Post
        {
            public Post(int mainId, bool left)
            {
                MainId = mainId;
                Left = left;
            }

            public int MainId { get; }
            public bool Left { get; }
        }

        /// <summary>A reserve or support unit given to a main's exposed flank (indexes into the allocation's arrays); <see cref="Kept"/>: the unit already held this post.</summary>
        public readonly struct GuardAssignment
        {
            public GuardAssignment(int main, int guard, bool left, DirectChildRole from, bool kept = false)
            {
                Main = main;
                Guard = guard;
                Left = left;
                From = from;
                Kept = kept;
            }

            public int Main { get; }
            public int Guard { get; }
            public bool Left { get; }
            public DirectChildRole From { get; }
            public bool Kept { get; }
        }

        /// <summary>
        /// The allocation behind the hold. A unit that already holds a post for a main that is still Main keeps it (<paramref name="incumbents"/>, by instance
        /// id): a guard that has arrived is the cover itself and must not be released for that. Then, for each Main (in index order) and each side that is exposed
        /// and has no post and does not rest on a river: the nearest Reserve on that side becomes the flank guard (RefuseLeft or RefuseRight, task GuardFlank),
        /// else a cascade flank guard (a RefuseLeft or RefuseRight) standing out on that side, else the nearest Reserve on the other side, else a SupportMain
        /// from the other side's support pool, the farthest from the main, provided that side stays covered without it. A side nobody can be given to stays open
        /// and the main holds. <paramref name="lateral"/> and <paramref name="depth"/> are each unit's position across and along the front (negative lateral is
        /// left), <paramref name="roles"/> its role (changed in place); <paramref name="anchored"/> answers whether the main's left (true) or right (false) end
        /// rests on impassable terrain.
        /// </summary>
        public static int AssignGuards(
            IReadOnlyList<int> ids,
            IReadOnlyList<float> lateral,
            IReadOnlyList<float> depth,
            DirectChildRole[] roles,
            Func<int, bool, bool> anchored,
            IReadOnlyDictionary<int, Post> incumbents,
            List<GuardAssignment> report)
        {
            if (ids == null || lateral == null || depth == null || roles == null) return 0;
            int n = roles.Length;
            if (ids.Count != n || lateral.Count != n || depth.Count != n) return 0;
            var posted = new bool[n * 2];
            int assigned = 0;

            if (incumbents != null)
            {
                for (int g = 0; g < n; g++)
                {
                    if (!incumbents.TryGetValue(ids[g], out Post post)) continue;
                    int m = IndexOf(ids, post.MainId);
                    if (m < 0 || m == g || roles[m] != DirectChildRole.Main) continue;
                    DirectChildRole role = roles[g];
                    if (role != DirectChildRole.Reserve && role != DirectChildRole.SupportMain && role != DirectChildRole.RefuseLeft && role != DirectChildRole.RefuseRight) continue;
                    int slot = m * 2 + (post.Left ? 0 : 1);
                    if (posted[slot]) continue;
                    posted[slot] = true;
                    roles[g] = post.Left ? DirectChildRole.RefuseLeft : DirectChildRole.RefuseRight;
                    assigned++;
                    report?.Add(new GuardAssignment(m, g, post.Left, role, kept: true));
                }
            }

            for (int m = 0; m < n; m++)
            {
                if (roles[m] != DirectChildRole.Main) continue;
                for (int side = 0; side < 2; side++)
                {
                    bool left = side == 0;
                    if (anchored != null && anchored(m, left)) continue;
                    if (posted[m * 2 + side]) continue;
                    if (SideCovered(m, left, lateral, depth, roles)) continue;

                    DirectChildRole same = left ? DirectChildRole.RefuseLeft : DirectChildRole.RefuseRight;
                    int pick = Nearest(m, left, DirectChildRole.Reserve, lateral, depth, roles);
                    DirectChildRole from = DirectChildRole.Reserve;
                    if (pick < 0)
                    {
                        pick = Nearest(m, left, same, lateral, depth, roles);
                        from = same;
                    }
                    if (pick < 0)
                    {
                        pick = Nearest(m, !left, DirectChildRole.Reserve, lateral, depth, roles);
                        from = DirectChildRole.Reserve;
                    }
                    if (pick < 0)
                    {
                        pick = SparableSupport(m, !left, lateral, depth, roles, anchored, posted);
                        from = DirectChildRole.SupportMain;
                    }
                    if (pick < 0) continue;

                    roles[pick] = same;
                    posted[m * 2 + side] = true;
                    assigned++;
                    report?.Add(new GuardAssignment(m, pick, left, from));
                }
            }
            return assigned;
        }

        private static int IndexOf(IReadOnlyList<int> ids, int id)
        {
            for (int i = 0; i < ids.Count; i++) if (ids[i] == id) return i;
            return -1;
        }

        // The side has a line unit abreast of the main (without = a unit not counted).
        private static bool SideCovered(int main, bool left, IReadOnlyList<float> lateral, IReadOnlyList<float> depth, DirectChildRole[] roles, int without = -1)
        {
            for (int j = 0; j < roles.Length; j++)
            {
                if (j == main || j == without) continue;
                float offset = lateral[j] - lateral[main];
                if ((offset < 0f) != left) continue;
                if (IsAbreast(offset, depth[j] - depth[main], roles[j])) return true;
            }
            return false;
        }

        // The nearest unit in the given role on the given side of the main, within the guard range.
        private static int Nearest(int main, bool left, DirectChildRole role, IReadOnlyList<float> lateral, IReadOnlyList<float> depth, DirectChildRole[] roles)
        {
            int pick = -1;
            float nearest = float.MaxValue;
            for (int j = 0; j < roles.Length; j++)
            {
                if (j == main || roles[j] != role) continue;
                float offset = lateral[j] - lateral[main];
                if (Math.Abs(offset) < MinFlankOffsetMeters || (offset < 0f) != left) continue;
                float distance = Distance(offset, depth[j] - depth[main]);
                if (distance > GuardRangeMeters || distance >= nearest) continue;
                nearest = distance;
                pick = j;
            }
            return pick;
        }

        // A SupportMain on the given side whose removal leaves that side covered (another line unit abreast, a post, or the end anchored): the farthest from the main.
        private static int SparableSupport(int main, bool left, IReadOnlyList<float> lateral, IReadOnlyList<float> depth, DirectChildRole[] roles,
            Func<int, bool, bool> anchored, bool[] posted)
        {
            int pick = -1;
            float farthest = -1f;
            for (int j = 0; j < roles.Length; j++)
            {
                if (roles[j] != DirectChildRole.SupportMain) continue;
                float offset = lateral[j] - lateral[main];
                if (Math.Abs(offset) < MinFlankOffsetMeters || (offset < 0f) != left) continue;
                float distance = Distance(offset, depth[j] - depth[main]);
                if (distance > GuardRangeMeters) continue;
                bool stillCovered = (anchored != null && anchored(main, left)) || posted[main * 2 + (left ? 0 : 1)]
                    || SideCovered(main, left, lateral, depth, roles, without: j);
                if (!stillCovered || distance <= farthest) continue;
                farthest = distance;
                pick = j;
            }
            return pick;
        }

        private static float Distance(float dx, float dz) => (float)Math.Sqrt((double)dx * dx + (double)dz * dz);
    }
}
