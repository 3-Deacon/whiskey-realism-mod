using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // One transfer the allocation asks for: strength to move to a short formation from a surplus one.
    // A map-group player formation can be a Shortfall carrier (a position marker, G4 N1); its id is
    // never a destination, so DeficitInstanceId is -1 and the deficit is its hold point only.
    internal struct TransferPair
    {
        public int DeficitInstanceId;
        public int SurplusInstanceId;
        public float DeficitX, DeficitZ, DeficitStrength;
        public float SurplusX, SurplusZ, SurplusStrength;
    }

    // Where the replacement flow goes (spec C8): new recruits and vanilla's own CheckTransferOfUnits
    // are pointed at the formations the allocation reports short, from the formations it reports
    // surplus. Vanilla keeps every gate and does the move; this only chooses the points it works on.
    // Pure: allocation and live formation records in, points out. Beliefs enter through the plan's
    // published numbers (MainEnemyStrength, HoldNeed, CapitalFloorStrength) only.
    //
    // A player formation is a position marker, never a target (G4 verify r2 N1, spec C4): a routing
    // point on one is its hold point (ReserveRules.HoldPoint: the map-group player's last waypoint,
    // where his group counts), no instance id of one is ever returned as a destination, and any
    // candidate whose chain class fails WlChainRules.MayMove is dropped from the source side.
    internal static class ReinforcementRouting
    {
        // x the relocation range from the recruiting cell: how far a recruit is routed (vanilla's
        // GrabExistingGroup searches that range itself and widens it by 1.25 steps, 12739-12806).
        public const float MaxRouteFactor = 1.5f;

        // New recruits (ZoneRecruiting's GrabExistingGroup at 12558): the position vanilla's division
        // search starts from becomes the nearest shortfall formation's hold point when one lies within
        // MaxRouteFactor x relocation range of the recruiting cell. toInstanceId is that formation, or
        // -1 when it is a player formation (position marker only).
        internal static bool TryRouteRecruit(Allocation allocation, IReadOnlyList<FormationRecord> own, float relocationRange,
                                             float cellX, float cellZ, out float routeX, out float routeZ, out int toInstanceId)
        {
            routeX = routeZ = 0f;
            toInstanceId = -1;
            if (allocation == null || own == null || !(relocationRange > 0f)) return false;
            float limit = MaxRouteFactor * relocationRange;
            float limitSq = limit * limit;
            int best = -1;
            float bestSq = float.MaxValue;
            for (int k = 0; k < allocation.Shortfalls.Count; k++)
            {
                int index = IndexOf(own, allocation.Shortfalls[k]);
                if (index < 0) continue;
                ReserveRules.HoldPoint(own[index], out float hx, out float hz);
                float d = ReserveRules.DistanceSq(hx, hz, cellX, cellZ);
                if (d > limitSq || d >= bestSq) continue;
                bestSq = d;
                best = index;
            }
            if (best < 0) return false;
            ReserveRules.HoldPoint(own[best], out routeX, out routeZ);
            toInstanceId = WlChainRules.MayMove(own[best].Chain) ? own[best].InstanceId : -1;
            return true;
        }

        // CheckTransferOfUnits (17232): the worst shortfall (first of Allocation.Shortfalls whose need
        // is positive) as the deficit and the largest surplus formation vanilla may take from. A source
        // is dropped when it is not the planner's to move (!MayMove), is the deficit itself, or is held
        // as MainEffort or CapitalGuard (the allocator never lists those; the check keeps a stale list
        // from draining the front).
        internal static bool TryTransferPair(TheaterPlan plan, Allocation allocation, IReadOnlyList<FormationRecord> own, out TransferPair pair)
        {
            pair = default(TransferPair);
            if (plan == null || allocation == null || own == null) return false;

            int deficit = -1;
            float need = 0f;
            for (int k = 0; k < allocation.Shortfalls.Count && deficit < 0; k++)
            {
                int index = IndexOf(own, allocation.Shortfalls[k]);
                if (index < 0) continue;
                float strength = ShortfallStrength(plan, allocation, own, own[index].InstanceId);
                if (!(strength > 0f)) continue;
                deficit = index;
                need = strength;
            }
            if (deficit < 0) return false;

            int source = -1;
            for (int k = 0; k < allocation.Surpluses.Count && source < 0; k++)
            {
                int index = IndexOf(own, allocation.Surpluses[k]);
                if (index < 0 || index == deficit) continue;
                var f = own[index];
                if (!WlChainRules.MayMove(f.Chain) || !(f.Strength > 0f)) continue;
                if (!allocation.TryGet(f.InstanceId, out var assignment)) continue;
                if (assignment.Role == FormationRole.MainEffort || assignment.Role == FormationRole.CapitalGuard) continue;
                source = index;
            }
            if (source < 0) return false;

            ReserveRules.HoldPoint(own[deficit], out float dx, out float dz);
            pair.DeficitInstanceId = WlChainRules.MayMove(own[deficit].Chain) ? own[deficit].InstanceId : -1;
            pair.DeficitX = dx;
            pair.DeficitZ = dz;
            pair.DeficitStrength = need;
            pair.SurplusInstanceId = own[source].InstanceId;
            pair.SurplusX = own[source].X;
            pair.SurplusZ = own[source].Z;
            pair.SurplusStrength = own[source].Strength;
            return true;
        }

        // The strength a Shortfall carrier is short of, by the need the allocator reported it for
        // (ForceAllocator.ListShortfalls): the main effort's required force less what the MainEffort
        // formations hold, the capital floor less the CapitalGuards, a sector's hold target
        // (max(HoldNeed, MinGarrisonStrength)) less what counts there. 0 for any other role.
        internal static float ShortfallStrength(TheaterPlan plan, Allocation allocation, IReadOnlyList<FormationRecord> own, int instanceId)
        {
            if (plan == null || allocation == null || own == null || !allocation.TryGet(instanceId, out var carrier)) return 0f;
            switch (carrier.Role)
            {
                case FormationRole.MainEffort:
                    return Math.Max(0f, plan.MainRequiredRatio * plan.MainEnemyStrength - RoleStrength(allocation, own, FormationRole.MainEffort, -1));
                case FormationRole.CapitalGuard:
                    return Math.Max(0f, plan.CapitalFloorStrength - RoleStrength(allocation, own, FormationRole.CapitalGuard, -1));
                case FormationRole.Hold:
                    float target = Math.Max(HoldNeedOf(plan, carrier.SectorId), ForceAllocator.MinGarrisonStrength);
                    return Math.Max(0f, target - RoleStrength(allocation, own, FormationRole.Hold, carrier.SectorId));
                default:
                    return 0f;
            }
        }

        private static float HoldNeedOf(TheaterPlan plan, int sectorId)
        {
            for (int k = 0; k < plan.Sectors.Count; k++)
                if (plan.Sectors[k] != null && plan.Sectors[k].SectorId == sectorId) return plan.Sectors[k].HoldNeed;
            return 0f;
        }

        // Σ of the strength the allocation holds in a role (sector -1: any). Hold counts the way the
        // allocator counts it: only formations that count toward coverage, the map-group player's group
        // at strength x morale (ReserveRules.HoldStrength).
        private static float RoleStrength(Allocation allocation, IReadOnlyList<FormationRecord> own, FormationRole role, int sectorId)
        {
            float sum = 0f;
            for (int i = 0; i < own.Count; i++)
            {
                if (!allocation.TryGet(own[i].InstanceId, out var assignment) || assignment.Role != role) continue;
                if (sectorId >= 0 && assignment.SectorId != sectorId) continue;
                if (role == FormationRole.Hold)
                {
                    if (WlChainRules.CountsTowardCoverage(own[i].Chain)) sum += ReserveRules.HoldStrength(own[i]);
                }
                else sum += Math.Max(0f, own[i].Strength);
            }
            return sum;
        }

        private static int IndexOf(IReadOnlyList<FormationRecord> own, int instanceId)
        {
            for (int i = 0; i < own.Count; i++)
                if (own[i].InstanceId == instanceId) return i;
            return -1;
        }
    }
}
