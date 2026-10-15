using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    // The planner's published state per AI alliance (0 and 1 only). Written only by
    // CampaignPlannerRuntime.ReviewPlan inside the daily review; Harmony patches read it through
    // the TryGet accessors (G5a/G5b/G5c) and never write it. Per campaign: cleared by
    // CampaignScope (StrategicCoordinator.ResetAll).
    public static class CampaignPlanStore
    {
        private const int Alliances = 2;
        private static readonly TheaterPlan[] Plans = new TheaterPlan[Alliances];
        private static readonly Allocation[] Allocations = new Allocation[Alliances];
        private static readonly SectorModel[] Sectors = new SectorModel[Alliances];

        static CampaignPlanStore()
        {
            CampaignScope.Register("campaign-planner-store", Reset);
        }

        public static bool TryGetPlan(int alliance, out TheaterPlan plan)
        {
            plan = InRange(alliance) ? Plans[alliance] : null;
            return plan != null;
        }

        public static bool TryGetAllocation(int alliance, out Allocation allocation)
        {
            allocation = InRange(alliance) ? Allocations[alliance] : null;
            return allocation != null;
        }

        // The defensive availability Postfix's SectorAt (G5a).
        public static bool TryGetSectors(int alliance, out SectorModel sectors)
        {
            sectors = InRange(alliance) ? Sectors[alliance] : null;
            return sectors != null;
        }

        internal static void Publish(int alliance, TheaterPlan plan, Allocation allocation, SectorModel sectors)
        {
            if (!InRange(alliance)) return;
            Plans[alliance] = plan;
            Allocations[alliance] = allocation;
            Sectors[alliance] = sectors;
        }

        private static void Reset()
        {
            for (int a = 0; a < Alliances; a++)
            {
                Plans[a] = null;
                Allocations[a] = null;
                Sectors[a] = null;
            }
        }

        private static bool InRange(int alliance)
        {
            return alliance >= 0 && alliance < Alliances;
        }
    }
}
