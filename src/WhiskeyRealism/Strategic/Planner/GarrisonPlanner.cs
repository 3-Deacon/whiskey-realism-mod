using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    public static class GarrisonPlanner
    {
        public const float ThreatShare = 0.5f;
        public const int MaxPostsPerAlliance = 6;
        public const int ReleaseQuietDays = 14;
        public const float SiteRadius = 25f;

        public static void PlanPosts(CampaignSnapshot s, TheaterPlan plan, IReadOnlyList<GarrisonPostRecord> retained, List<GarrisonPost> into)
        {
        }

        public static int PickFormation(CampaignSnapshot s, GarrisonPost post, bool[] pool)
        {
            return -1;
        }
    }
}
