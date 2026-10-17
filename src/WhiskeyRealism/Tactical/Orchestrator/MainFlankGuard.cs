using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// A main effort advances only with its flanks covered (smoke 2026-10-17: a lone main-effort brigade, 5th Massachusetts, advanced with no neighbour and
    /// no flank guard and was shot in the flank). A flank of the main is exposed when no friendly line unit stands on that side within a frontage plus a gap
    /// (<see cref="ReachMeters"/>). An exposed flank is covered when a guard is assigned on that side (a flank guard, RefuseLeft or RefuseRight, whose task is
    /// GuardFlank, or a SupportMain) or when it rests on terrain the snapshot marks impassable (a river, <c>TacticalTerrainLinePlanner.FlankAnchored</c>).
    /// Otherwise the main holds, and the allocation (<see cref="AssignGuards"/>) gives the exposed side a guard from the reserve, else from the support pool.
    /// Pure: the posture producer measures the neighbours; the leaf map's build assigns the guards.
    /// </summary>
    public static class MainFlankGuard
    {
        /// <summary>A brigade's frontage in line.</summary>
        public const float FrontageMeters = 450f;

        /// <summary>The gap a line may leave between neighbours before the flank beside it counts as open.</summary>
        public const float GapMeters = 250f;

        public const float ReachMeters = FrontageMeters + GapMeters;

        /// <summary>A unit nearer the main than this, sideways, is behind it or on top of it and covers no flank.</summary>
        public const float MinFlankOffsetMeters = 100f;

        /// <summary>The farthest a guard stands from the main and still covers its side.</summary>
        public const float GuardRangeMeters = 2500f;

        /// <summary>How far past the main's centre its flank end lies, for the terrain test (half a frontage).</summary>
        public const float FlankEndMeters = FrontageMeters * 0.5f;

        /// <summary>
        /// The longest a hold lasts (battle seconds, doctrine clock): 20 battle-minutes. Twenty minutes is a third of the plan commitment hold
        /// (<c>ArmyReplanTriggers.PlanCommitmentBattleSeconds</c>, an hour), so a hold never outlives the plan it serves; it is longer than the arrival hold's
        /// 15-minute budget (<c>MainArrivalHoldLatch.MaxHoldBattleSeconds</c>), which a main may also wait out after it; and it is long enough for a guard
        /// from the far end of its range (<see cref="GuardRangeMeters"/>) to march up at brigade pace. Past it the main advances with an open flank rather than
        /// never attack.
        /// </summary>
        public const float MaxHoldBattleSeconds = 20f * 60f;

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

        /// <summary>The roles that assign a guard to a flank: a flank guard (GuardFlank) or a supporting attack.</summary>
        public static bool IsGuard(DirectChildRole role)
        {
            return role == DirectChildRole.SupportMain || role == DirectChildRole.RefuseLeft || role == DirectChildRole.RefuseRight;
        }

        // The side a guard covers: a flank guard covers the side its role names, a supporting attack the side it stands on.
        private static bool GuardsLeft(DirectChildRole role, float offset)
        {
            if (role == DirectChildRole.RefuseLeft) return true;
            if (role == DirectChildRole.RefuseRight) return false;
            return offset < 0f;
        }

        /// <summary>One friendly unit seen from the main: <see cref="Lateral"/> is its offset sideways from the main along the front (negative is left).</summary>
        public readonly struct Neighbour
        {
            public Neighbour(float lateral, DirectChildRole role)
            {
                Lateral = lateral;
                Role = role;
            }

            public float Lateral { get; }
            public DirectChildRole Role { get; }
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
            public bool LeftGuarded { get; }
            public bool RightGuarded { get; }
            public bool LeftAnchored { get; }
            public bool RightAnchored { get; }

            /// <summary>The left flank has no line unit in reach and does not rest on a river (a guard assigned to it is still on its way).</summary>
            public bool LeftOpen => LeftExposed && !LeftAnchored;
            public bool RightOpen => RightExposed && !RightAnchored;

            /// <summary>A flank is open: the main does not advance (until the hold's bound).</summary>
            public bool Open => LeftOpen || RightOpen;

            /// <summary>An open flank has no guard assigned at all: nobody has been given to it.</summary>
            public bool Unguarded => (LeftOpen && !LeftGuarded) || (RightOpen && !RightGuarded);

            /// <summary>Every open flank has a guard assigned that has not yet come into reach.</summary>
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
                if (float.IsNaN(lateral) || float.IsInfinity(lateral)) continue;
                float distance = Math.Abs(lateral);
                if (distance < MinFlankOffsetMeters) continue;
                DirectChildRole role = others[i].Role;
                bool near = IsLine(role) && distance <= ReachMeters;
                bool guard = IsGuard(role) && distance <= GuardRangeMeters;
                if (lateral < 0f) leftNear |= near;
                else rightNear |= near;
                if (guard)
                {
                    if (GuardsLeft(role, lateral)) leftGuard = true;
                    else rightGuard = true;
                }
            }
            return new Cover(!leftNear, !rightNear, leftGuard, rightGuard, leftAnchored, rightAnchored);
        }

        /// <summary>
        /// The main's hold on the doctrine clock: whether it is holding, since when, since when its flanks have been covered (negative: not covered), and
        /// whether the hold ran out its bound and the main advances with the flank open (<see cref="Advanced"/>, kept until the flanks are covered again).
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
        /// restarts that count.
        /// </summary>
        public static HoldState Step(HoldState previous, bool open, float nowHours)
        {
            if (open)
            {
                if (previous.Advanced) return previous;
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

        /// <summary>A reserve or support unit given to a main's exposed flank.</summary>
        public readonly struct GuardAssignment
        {
            public GuardAssignment(int main, int guard, bool left, DirectChildRole from)
            {
                Main = main;
                Guard = guard;
                Left = left;
                From = from;
            }

            public int Main { get; }
            public int Guard { get; }
            public bool Left { get; }
            public DirectChildRole From { get; }
        }

        /// <summary>
        /// The allocation behind the hold: for each Main (in index order) and each side that is exposed and neither guarded nor anchored, the nearest Reserve on
        /// that side becomes the flank guard (RefuseLeft or RefuseRight, task GuardFlank); with none there, the nearest Reserve on the other side; with none, a
        /// SupportMain from the other side's support pool, the farthest from the main, provided that side stays covered without it. A side nobody can be given to
        /// stays open and the main holds. <paramref name="lateral"/> is each unit's position across the front (negative is left), <paramref name="roles"/> its role
        /// (changed in place); <paramref name="anchored"/> answers whether the main's left (true) or right (false) end rests on impassable terrain.
        /// </summary>
        public static int AssignGuards(IReadOnlyList<float> lateral, DirectChildRole[] roles, Func<int, bool, bool> anchored, List<GuardAssignment> report)
        {
            if (lateral == null || roles == null || lateral.Count != roles.Length) return 0;
            int assigned = 0;
            for (int m = 0; m < roles.Length; m++)
            {
                if (roles[m] != DirectChildRole.Main) continue;
                for (int side = 0; side < 2; side++)
                {
                    bool left = side == 0;
                    if (anchored != null && anchored(m, left)) continue;
                    if (SideCovered(m, left, lateral, roles)) continue;

                    int pick = NearestReserve(m, left, lateral, roles);
                    if (pick < 0) pick = NearestReserve(m, !left, lateral, roles);
                    DirectChildRole from = DirectChildRole.Reserve;
                    if (pick < 0)
                    {
                        pick = SparableSupport(m, !left, lateral, roles, anchored);
                        from = DirectChildRole.SupportMain;
                    }
                    if (pick < 0) continue;

                    roles[pick] = left ? DirectChildRole.RefuseLeft : DirectChildRole.RefuseRight;
                    assigned++;
                    report?.Add(new GuardAssignment(m, pick, left, from));
                }
            }
            return assigned;
        }

        // The side has a line unit in reach or an assigned guard in range.
        private static bool SideCovered(int main, bool left, IReadOnlyList<float> lateral, DirectChildRole[] roles, int without = -1)
        {
            for (int j = 0; j < roles.Length; j++)
            {
                if (j == main || j == without) continue;
                float offset = lateral[j] - lateral[main];
                float distance = Math.Abs(offset);
                if (distance < MinFlankOffsetMeters) continue;
                if ((offset < 0f) == left && IsLine(roles[j]) && distance <= ReachMeters) return true;
                if (IsGuard(roles[j]) && distance <= GuardRangeMeters && GuardsLeft(roles[j], offset) == left) return true;
            }
            return false;
        }

        private static int NearestReserve(int main, bool left, IReadOnlyList<float> lateral, DirectChildRole[] roles)
        {
            int pick = -1;
            float nearest = float.MaxValue;
            for (int j = 0; j < roles.Length; j++)
            {
                if (roles[j] != DirectChildRole.Reserve) continue;
                float offset = lateral[j] - lateral[main];
                float distance = Math.Abs(offset);
                if (distance < MinFlankOffsetMeters || (offset < 0f) != left || distance > GuardRangeMeters) continue;
                if (distance < nearest)
                {
                    nearest = distance;
                    pick = j;
                }
            }
            return pick;
        }

        // A SupportMain on the given side whose removal leaves that side covered (another line unit or guard there, or the end anchored): the farthest from the main.
        private static int SparableSupport(int main, bool left, IReadOnlyList<float> lateral, DirectChildRole[] roles, Func<int, bool, bool> anchored)
        {
            int pick = -1;
            float farthest = -1f;
            for (int j = 0; j < roles.Length; j++)
            {
                if (roles[j] != DirectChildRole.SupportMain) continue;
                float offset = lateral[j] - lateral[main];
                float distance = Math.Abs(offset);
                if (distance < MinFlankOffsetMeters || (offset < 0f) != left || distance > GuardRangeMeters) continue;
                bool stillCovered = (anchored != null && anchored(main, left)) || SideCovered(main, left, lateral, roles, without: j);
                if (!stillCovered) continue;
                if (distance > farthest)
                {
                    farthest = distance;
                    pick = j;
                }
            }
            return pick;
        }
    }
}
