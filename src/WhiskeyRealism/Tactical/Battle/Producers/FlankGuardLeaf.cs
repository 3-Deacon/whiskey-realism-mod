using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tactical.Battle.Producers
{
    /// <summary>
    /// The leaf posture pass's half of the flank guard (MainFlankGuard), pure over the BattleSnapshot and the leaf map. The producer calls it from ProduceLeaf:
    /// <see cref="MainHolds"/> for a Main brigade about to attack, <see cref="PlanGuard"/> for a brigade the leaf map made a main's flank guard.
    /// A flank guard's task is GuardFlank, which LeafBrigadeMovePolicy treats as a non-movement task, so the guard's move to its post is submitted here, through
    /// the same leaf Move intent (PostureIntentPlan.LeafMove, Priority.Executor), to the post at the main's flank (MainFlankGuard.PostOf).
    /// </summary>
    internal static class FlankGuardLeaf
    {
        /// <summary>What the guard pass did with a flank guard this tick.</summary>
        internal enum GuardOutcome
        {
            /// <summary>It has no post to go to (no main, or no axis): the normal leaf pass runs.</summary>
            NoPost = 0,
            /// <summary>It is at its post or on its way to it, in contact, or its orders are pending: its standing claim is the intent to submit.</summary>
            Handled,
            /// <summary>A Move to the post is the intent to submit.</summary>
            Move,
            /// <summary>Its group or its formation is withdrawing: no claim and no move, so the withdrawal (Support-priority fallbacks included) is not outranked.</summary>
            Withdrawing,
        }

        /// <summary>
        /// Whether the Main brigade at <paramref name="mainIndex"/> must hold: the friendly brigades beside it by the leaf map's roles (2D against its current
        /// position), the flank guards moving toward their posts, and whether either end rests on a river. <paramref name="holds"/> is the per-battle hold memory
        /// (by instance id); <paramref name="change"/> is the reason of the row a change of the hold writes (null when it did not change).
        /// </summary>
        internal static bool MainHolds(
            BattleSnapshot s,
            int mainIndex,
            IReadOnlyDictionary<int, TacticalLeafBrigadeMap.LeafAssignment> leafMap,
            Dictionary<int, MainFlankGuard.HoldState> holds,
            TerrainPicture picture,
            List<MainFlankGuard.Neighbour> scratch,
            out MainFlankGuard.Cover cover,
            out string change,
            out string reason)
        {
            reason = null;
            change = null;
            cover = default(MainFlankGuard.Cover);
            UnitRecord[] own = s.Own;
            int id = own[mainIndex].InstanceId;
            TacticalFrontAxis axis = s.FrontAxis;
            if (!axis.IsValid || leafMap == null)
            {
                holds.Remove(id);
                return false;
            }

            float mx = own[mainIndex].X, mz = own[mainIndex].Z;
            float mainLateral = axis.LateralOf(mx, mz);
            float mainDepth = axis.DepthOf(mx, mz);
            scratch.Clear();
            for (int k = 0; k < s.OwnCount; k++)
            {
                if (k == mainIndex || own[k].UnitTyp != TacticalUnitType.BattleGroupBrigade) continue;
                if ((own[k].Flags & UnitFlags.Alive) == 0 || PostureSense.Routed(own[k])) continue;
                DirectChildRole role;
                int postSide = 0;
                if (leafMap.TryGetValue(own[k].InstanceId, out TacticalLeafBrigadeMap.LeafAssignment la))
                {
                    role = la.LeafRole;
                    if (la.FlankPostMain == id && MovingToPost(in own[k], in axis, mx, mz, la.FlankPostLeft)) postSide = la.FlankPostLeft ? -1 : 1;
                }
                else if ((own[k].Flags & UnitFlags.PlayerOwned) != 0) role = DirectChildRole.Fix; // a player-owned brigade on the line is a neighbour
                else continue;
                scratch.Add(new MainFlankGuard.Neighbour(
                    axis.LateralOf(own[k].X, own[k].Z) - mainLateral, axis.DepthOf(own[k].X, own[k].Z) - mainDepth, role, postSide));
            }

            bool leftAnchored = false, rightAnchored = false;
            if (picture != null && picture.Ready)
            {
                float half = MainFlankGuard.FlankEndMeters;
                leftAnchored = TacticalTerrainLinePlanner.FlankAnchored(picture, mx - axis.RightX * half, mz - axis.RightZ * half, -axis.RightX, -axis.RightZ, includeCrest: false);
                rightAnchored = TacticalTerrainLinePlanner.FlankAnchored(picture, mx + axis.RightX * half, mz + axis.RightZ * half, axis.RightX, axis.RightZ, includeCrest: false);
            }
            cover = MainFlankGuard.Assess(scratch, leftAnchored, rightAnchored);

            holds.TryGetValue(id, out MainFlankGuard.HoldState previous);
            MainFlankGuard.HoldState next = MainFlankGuard.Step(previous, cover.Open, (float)s.DoctrineHours);
            holds[id] = next;
            change = MainFlankGuard.ChangeReason(previous, next, cover);
            if (next.Holding) reason = cover.HoldWhy;
            return next.Holding;
        }

        /// <summary>
        /// The flank guard's move: to the post at its main's flank, at <see cref="IntentPriority.Post"/>. A unit in contact, with a pending order, already at the
        /// post or whose order already leads there writes nothing, but it keeps its claim: <paramref name="intent"/> is then the standing claim, renewed every
        /// tick the guard is posted, so the move of the division it sits in fans out around it (the executor's claim model: a descendant's higher claim shields
        /// it). A unit whose main is gone or routed has no post, no claim, and runs the normal leaf pass. A guard whose own group or formation is withdrawing
        /// (<paramref name="withdrawing"/>: a withdrawal task on it or an ancestor, or a recent withdrawal move) holds no claim and takes no move: a Post claim
        /// outranks the Support-priority fallbacks (WithdrawalIntentProducer.TryPlanStalledFallback), and a formation that withdraws must withdraw whole.
        /// </summary>
        internal static GuardOutcome PlanGuard(
            BattleSnapshot s,
            int guardIndex,
            in TacticalLeafBrigadeMap.LeafAssignment leaf,
            bool closeEngaged,
            bool pending,
            bool withdrawing,
            out BattleOrderIntent intent,
            out string why)
        {
            intent = default(BattleOrderIntent);
            why = null;
            TacticalFrontAxis axis = s.FrontAxis;
            if (!leaf.HasFlankPost || !axis.IsValid) return GuardOutcome.NoPost;
            int mainIndex = s.IndexOfOwn(leaf.FlankPostMain);
            if (mainIndex < 0) return GuardOutcome.NoPost;
            ref UnitRecord main = ref s.Own[mainIndex];
            if ((main.Flags & UnitFlags.Alive) == 0 || PostureSense.Routed(main)) return GuardOutcome.NoPost;

            if (withdrawing) { why = "withdrawing"; return GuardOutcome.Withdrawing; }

            MainFlankGuard.PostOf(in axis, main.X, main.Z, leaf.FlankPostLeft, out float px, out float pz, out float heading);
            ref UnitRecord guard = ref s.Own[guardIndex];
            intent = PostureIntentPlan.FlankPostClaim(guard.InstanceId, px, pz);
            if (PostureSense.Distance(guard.X, guard.Z, px, pz) <= MainFlankGuard.PostToleranceMeters) { why = "at-post"; return GuardOutcome.Handled; }
            if (closeEngaged) { why = "close-engaged"; return GuardOutcome.Handled; }
            if (MovingToPost(in guard, in axis, main.X, main.Z, leaf.FlankPostLeft)) { why = "toward-post"; return GuardOutcome.Handled; }
            if (pending) { why = "order-pending"; return GuardOutcome.Handled; }

            intent = PostureIntentPlan.FlankPostMove(guard.InstanceId, px, pz, heading);
            why = "move-to-post";
            return GuardOutcome.Move;
        }

        // The unit has an order that leads to the post: a path or queued order, and its last waypoint at the post.
        internal static bool MovingToPost(in UnitRecord unit, in TacticalFrontAxis axis, float mainX, float mainZ, bool left)
        {
            if (unit.RegimentPaths <= 0 && unit.QueuedOrders <= 0 && unit.MovementMode <= 0) return false;
            if (!PostureSense.HasPoint(unit.LastWaypointX, unit.LastWaypointZ)) return false;
            MainFlankGuard.PostOf(in axis, mainX, mainZ, left, out float px, out float pz, out _);
            return PostureSense.Distance(unit.LastWaypointX, unit.LastWaypointZ, px, pz) <= MainFlankGuard.PostToleranceMeters;
        }
    }
}
