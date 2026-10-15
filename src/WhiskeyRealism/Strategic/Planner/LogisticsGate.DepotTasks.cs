using System;

namespace WhiskeyRealism.Strategic.Planner
{
    // "Don't outrun supply" (spec component 5): when the main-effort target is out of supply reach
    // for the force that would take it, the plan gets depot tasks along the axis instead of an
    // advance past reach (G5c marches a builder there and wishes a vanilla supply depot,
    // CBuilding.AddConstructionWish 97479-97536).
    //   Site 1: the farthest in-reach point on the axis (ClampToReach), or the force's own position
    //     when nothing on the axis is in reach (vanilla's job 19 wishes a depot at the unit's
    //     position when it has no supply, 14770-14775).
    //   Site 2: one reach range further toward the target, when the target lies beyond the reach a
    //     depot at site 1 would give.
    //   A site within reach range of an own or allied depot that is already there or still being
    //     built is skipped: that depot already extends the reach.
    // The plan's G0 signature (s, plan, sectors) has no axis: the caller passes the force position,
    // the target and the force's reach terms (TheaterPlanner.Plan is the caller).
    public static partial class LogisticsGate
    {
        public const int MaxDepotTasksPerPlan = 2;

        public static void AddDepotTasks(CampaignSnapshot s, TheaterPlan plan, SectorModel sectors, float fromX, float fromZ,
                                         float toX, float toZ, float formationCommandRange, float supplySlack)
        {
            if (s == null || plan == null) return;
            float range = Range(formationCommandRange, supplySlack);
            if (range <= 0f || Evaluate(s, toX, toZ, formationCommandRange, supplySlack).InReach) return;

            float siteX = toX, siteZ = toZ;
            if (!ClampToReach(s, fromX, fromZ, ref siteX, ref siteZ, formationCommandRange, supplySlack))
            {
                siteX = fromX;
                siteZ = fromZ;
            }

            float axisX = toX - siteX, axisZ = toZ - siteZ;
            float remaining = (float)Math.Sqrt(axisX * axisX + axisZ * axisZ);
            float priority = 1f;
            for (int k = 0; k < MaxDepotTasksPerPlan && plan.DepotTasks.Count < MaxDepotTasksPerPlan; k++)
            {
                if (!DepotNear(s, siteX, siteZ, range))
                {
                    plan.DepotTasks.Add(new DepotTask
                    {
                        X = siteX,
                        Z = siteZ,
                        SectorId = sectors != null ? sectors.SectorAt(siteX, siteZ) : -1,
                        Priority = priority
                    });
                }
                if (remaining <= range) return;
                float step = range / remaining;
                siteX += axisX * step;
                siteZ += axisZ * step;
                axisX = toX - siteX;
                axisZ = toZ - siteZ;
                remaining -= range;
                priority *= 0.5f;
            }
        }

        private static bool DepotNear(CampaignSnapshot s, float x, float z, float range)
        {
            float rangeSq = range * range;
            for (int i = 0; i < s.Supply.Count; i++)
            {
                var source = s.Supply[i];
                if (source.Kind != SupplySourceKind.Depot) continue;
                float dx = source.X - x;
                float dz = source.Z - z;
                if (dx * dx + dz * dz <= rangeSq) return true;
            }
            return false;
        }
    }
}
