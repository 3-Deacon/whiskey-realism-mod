using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // The two reserves G3 sets aside before it masses (the capital reserve and each threatened
    // sector's hold reserve) and the belief readings they use. TheaterPlanner.MassFor subtracts
    // them from the massable set; ForceAllocator (G4) assigns CapitalGuard and Hold from the very
    // same picks, so the mass the odds were judged on and the roles the allocator hands out cannot
    // diverge (G3 verify r1 F4/F9). Pure: snapshot in, flags out.
    internal static class ReserveRules
    {
        // A belief's live weight: strength x confidence, 0 once expired.
        internal static float Live(in EnemyBelief b)
        {
            return b.Confidence01 > 0f ? System.Math.Max(0f, b.Strength) * System.Math.Min(1f, b.Confidence01) : 0f;
        }

        // Afloat armies cannot be engaged by land (EstablishCampaignBattle 80632-80635).
        internal static bool Afloat(in EnemyBelief b)
        {
            return (b.Traits & EnemyTraits.Embarked) != 0;
        }

        // The sector a formation stands in: its closest AI area's sector, else the point lookup.
        internal static int SectorOf(SectorModel sectors, in FormationRecord f)
        {
            int sector = f.AreaIndex >= 0 ? sectors.SectorOfArea(f.AreaIndex) : -1;
            return sector >= 0 ? sector : sectors.SectorAt(f.X, f.Z);
        }

        // Index of the live land belief nearest (x, z), -1 when none: a sector's hold reserve and
        // hold order are measured from the army that sector faces.
        internal static int NearestLandBelief(CampaignSnapshot s, float x, float z)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < s.Beliefs.Count; i++)
            {
                var b = s.Beliefs[i];
                if (Live(b) <= 0f || Afloat(b)) continue;
                float d = DistanceSq(b.X, b.Z, x, z);
                if (d >= bestDistance) continue;
                bestDistance = d;
                best = i;
            }
            return best;
        }

        // Indexes into s.Own ordered by squared distance to (x, z), the index breaking ties.
        internal static void SortByDistance(CampaignSnapshot s, List<int> order, float x, float z)
        {
            order.Sort((a, b) =>
            {
                int d = DistanceSq(s.Own[a].X, s.Own[a].Z, x, z).CompareTo(DistanceSq(s.Own[b].X, s.Own[b].Z, x, z));
                return d != 0 ? d : a.CompareTo(b);
            });
        }

        // The point a formation holds ground from: its position, except the map-group player's group,
        // which holds where its last waypoint lies (vanilla's own capital count reads GetLastWaypointPos,
        // AssignUnitToDefendCapital 11733-11740), so a player marching away stops covering the sector
        // he is leaving the day his order is given.
        internal static void HoldPoint(in FormationRecord f, out float x, out float z)
        {
            bool byWaypoint = f.Chain == WlChainClass.PlayerMapGroup && IsFinite(f.WaypointX) && IsFinite(f.WaypointZ);
            x = byWaypoint ? f.WaypointX : f.X;
            z = byWaypoint ? f.WaypointZ : f.Z;
        }

        // The sector a formation holds: SectorOf for the AI, the sector of the hold point for the
        // map-group player (falling back to his position when his waypoint is not a usable point, e.g. NaN).
        internal static int HoldSectorOf(SectorModel sectors, in FormationRecord f)
        {
            if (f.Chain != WlChainClass.PlayerMapGroup) return SectorOf(sectors, f);
            HoldPoint(f, out float x, out float z);
            int sector = sectors.SectorAt(x, z);
            return sector >= 0 ? sector : SectorOf(sectors, f);
        }

        // What a formation adds to a hold sum (WlChainRules.CountsTowardCoverage decides who counts): its
        // strength, and for the map-group player strength x morale, vanilla's own measure (11733-11740).
        internal static float HoldStrength(in FormationRecord f)
        {
            float strength = System.Math.Max(0f, f.Strength);
            return f.Chain == WlChainClass.PlayerMapGroup ? strength * SnapshotRules.Clamp01(f.Morale) : strength;
        }

        private static bool IsFinite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }

        // A formation the allocator sends to Recover: movable, and below the raw supply floor (spec C21)
        // or the movement morale minimum. It is leaving for a supply source, so it holds nothing.
        internal static bool Recovers(in FormationRecord f, float minMorale)
        {
            return TheaterPlanner.Movable(f) && WlChainRules.MayMove(f.Chain)
                && (f.Supply01 < TheaterPlanner.RecoverSupplyBelow || f.Morale < minMorale);
        }

        // The capital reserve: the massable formations nearest the own capital until their
        // strength reaches the capital floor (P7).
        internal static void Capital(CampaignSnapshot s, float capitalFloor, bool[] reserved)
        {
            for (int i = 0; i < reserved.Length; i++) reserved[i] = false;
            if (!s.HasCapital || capitalFloor <= 0f) return;
            var order = new List<int>();
            for (int i = 0; i < s.Own.Count; i++)
                if (TheaterPlanner.MassEligible(s.Own[i], s.MinMoraleForMovement)) order.Add(i);
            SortByDistance(s, order, s.CapitalX, s.CapitalZ);
            float held = 0f;
            for (int k = 0; k < order.Count && held < capitalFloor; k++)
            {
                reserved[order[k]] = true;
                held += System.Math.Max(0f, s.Own[order[k]].Strength);
            }
        }

        // Each threatened sector's hold reserve: what holds there anyway (formations that cannot
        // be massed, and the capital reserve) counts first, then the massable formations nearest the
        // sector's believed army until the hold need is met. What holds counts the way vanilla counts
        // it (WlChainRules.CountsTowardCoverage, integrator decision 2026-10-15): a committed defender at
        // its strength and the map-group player's group at strength x morale, in the sector where its
        // last waypoint lies (HoldSectorOf), so a player standing in a sector releases the AI corps
        // that would hold it and a player marching away stops covering it. The player's own command and
        // a CIC side hold nothing. A formation that Recovers is leaving for supply and counts 0 (G4
        // verify r2 N2). The player's group is never in the massable list (TheaterPlanner.MassEligible),
        // so it is never reserved. holdReserved[i] is the sector whose hold
        // keeps formation i, -1 none. need/nearX/nearZ are per sector.
        internal static void Hold(CampaignSnapshot s, SectorModel sectors, float[] need, float[] nearX, float[] nearZ,
                                  bool[] capitalReserved, int[] holdReserved)
        {
            int n = s.Own.Count;
            for (int i = 0; i < holdReserved.Length; i++) holdReserved[i] = -1;
            var order = new List<int>();
            for (int sector = 0; sector < sectors.Count; sector++)
            {
                if (need[sector] <= 0f) continue;
                float held = 0f;
                order.Clear();
                for (int i = 0; i < n; i++)
                {
                    var f = s.Own[i];
                    if (HoldSectorOf(sectors, f) != sector) continue;
                    if (!TheaterPlanner.MassEligible(f, s.MinMoraleForMovement) || capitalReserved[i])
                    {
                        if (WlChainRules.CountsTowardCoverage(f.Chain) && !Recovers(f, s.MinMoraleForMovement))
                            held += HoldStrength(f);
                    }
                    else order.Add(i);
                }
                SortByDistance(s, order, nearX[sector], nearZ[sector]);
                for (int k = 0; k < order.Count && held < need[sector]; k++)
                {
                    holdReserved[order[k]] = sector;
                    held += System.Math.Max(0f, s.Own[order[k]].Strength);
                }
            }
        }

        internal static float DistanceSq(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return dx * dx + dz * dz;
        }
    }
}
