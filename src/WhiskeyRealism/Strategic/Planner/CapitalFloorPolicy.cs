using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // One own formation the capital pledge may take, as DefensiveOpsPatch read it live. Vanilla's
    // own pick filters (AssignUnitToDefendCapital 11791-11817: distance, strength, morale,
    // readiness, battle, retreat, depot/fort/sea-invasion/raid/town work, operations theatre, the
    // W&L player's own units) are the patch's runtime reads and arrive as PassesVanillaFilters.
    internal struct PledgeCandidate
    {
        public int InstanceId;
        public float DistanceToCapital;
        public WlChainClass Chain;
        public bool PassesVanillaFilters;
        // The coastal-defense runner has a custom order on the unit (CoastalDefenseCustomOrderRunner).
        public bool CustomOrdered;
    }

    // The capital floor on the live planner (plan P7, spec C9). The floor itself is the plan's
    // CapitalFloorStrength (G3) and the excess guards are CapitalGuard assignments the G5a mover holds;
    // this policy is the part that stays vanilla's: the pre-pledge into groupstodefendcapital, made only
    // while vanilla's own need (num2) is positive, so vanilla keeps the pledge and its move loop (11829)
    // moves it this call instead of stripping it as the farthest defender (the num2 <= 0 branch, 11751-11781).
    // Pure: beliefs, formation records and the allocation in, an instance id out.
    internal static class CapitalFloorPolicy
    {
        // Scratch lists for VanillaNeed (main thread only, cleared on entry).
        private static readonly List<CapitalForce> EnemyForces = new List<CapitalForce>();
        private static readonly List<CapitalForce> OwnForces = new List<CapitalForce>();

        // Vanilla's num2 (AssignUnitToDefendCapital 11712-11741) with the believed enemy in place of
        // enemyunits: the movement minimum, plus for every enemy within the relocation range
        // min(str, str x morale) / max(10, d)^0.3 x 0.5, minus str x morale of every own group whose last
        // waypoint is nearer the capital than the nearest enemy (the W&L map-group player's group
        // included, as vanilla counts it, 11733-11740). A belief shows strength and position only, so its
        // strength is confidence-weighted and its morale is the caller's believedMorale (1: not seen).
        // `own` carries groupstrengthactive in Strength, vanilla's own measure there.
        internal static float VanillaNeed(float minStrengthForMovement, IReadOnlyList<EnemyBelief> beliefs, float believedMorale,
                                          float capitalX, float capitalZ, float relocationRange, IReadOnlyList<FormationRecord> own)
        {
            EnemyForces.Clear();
            OwnForces.Clear();
            if (beliefs != null)
            {
                for (int i = 0; i < beliefs.Count; i++)
                {
                    var belief = beliefs[i];
                    float weighted = EnemyBeliefReadings.Weighted(in belief);
                    if (weighted <= 0f) continue;
                    EnemyForces.Add(new CapitalForce(Distance(belief.X, belief.Z, capitalX, capitalZ), weighted, believedMorale));
                }
            }
            if (own != null)
            {
                for (int i = 0; i < own.Count; i++)
                {
                    var f = own[i];
                    bool hasWaypoint = IsFinite(f.WaypointX) && IsFinite(f.WaypointZ);
                    float wx = hasWaypoint ? f.WaypointX : f.X;
                    float wz = hasWaypoint ? f.WaypointZ : f.Z;
                    OwnForces.Add(new CapitalForce(Distance(wx, wz, capitalX, capitalZ), f.Strength, f.Morale));
                }
            }
            return CampaignMilitaryGates.VanillaCapitalDefenseNeed(minStrengthForMovement, relocationRange, EnemyForces, OwnForces);
        }

        // The one formation to pre-pledge: the allocation's CapitalGuard nearest the capital among the
        // candidates vanilla's own filters admit, none while vanilla's need is not positive (the excess
        // floor stays CapitalGuard: held by the mover, withheld by the availability Postfixes, never
        // pledged). Never a formation whose chain class forbids a commitment (the map-group player is
        // counted by vanilla's need but never pledged), a coastal custom-ordered unit, or a formation the
        // allocation does not hold as CapitalGuard. -1 when none.
        internal static int PickPledge(Allocation allocation, float vanillaNeed, IReadOnlyList<PledgeCandidate> pool)
        {
            if (allocation == null || pool == null || !(vanillaNeed > 0f)) return -1;
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < pool.Count; i++)
            {
                var c = pool[i];
                if (!c.PassesVanillaFilters || c.CustomOrdered || !WlChainRules.MayMove(c.Chain)) continue;
                if (!allocation.TryGet(c.InstanceId, out var assignment) || assignment.Role != FormationRole.CapitalGuard) continue;
                bool nearer = c.DistanceToCapital < bestDistance || (c.DistanceToCapital == bestDistance && c.InstanceId < best);
                if (!nearer) continue;
                bestDistance = c.DistanceToCapital;
                best = c.InstanceId;
            }
            return best;
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static bool IsFinite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }
    }
}
