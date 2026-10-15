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
        // sector's believed army until the hold need is met. Only a committed defender counts
        // (WlChainRules.CountsAsCommittedDefender, spec C4): the W&L player's own command and a
        // map-group player never hold a sector, whatever his reliability, so the AI formations that
        // do are not massed away for a sector the player stands in. holdReserved[i] is the sector whose hold
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
                    if (SectorOf(sectors, f) != sector) continue;
                    if (!TheaterPlanner.MassEligible(f, s.MinMoraleForMovement) || capitalReserved[i])
                    {
                        if (WlChainRules.CountsAsCommittedDefender(f.Chain)) held += System.Math.Max(0f, f.Strength);
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
