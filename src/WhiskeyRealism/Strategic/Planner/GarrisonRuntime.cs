using System.Collections.Generic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    // The runtime half of garrison manning (plan G8): the Plan job's two calls and the telemetry rows. Nothing here writes vanilla state.
    // A post is manned by the planner's existing executors: ForceAllocator seats a formation under FormationRole.Garrison (withheld),
    // the mover (CampaignMoverRuntime, behind Enable Campaign Planner Executors) marches it to the post, and the steering executor centres
    // its theater box on it once it stands there as a Hold formation. Vanilla's reactive recapture (job 29) is unchanged and complements this.
    //
    // Enable Garrison Manning gates the ordinary posts only: with it off no ordinary post enters the plan, and the ledger releases any it
    // held. D8's secession garrison is not behind it (it has its own rules and no flag); it still enters the same ledger.
    internal static class GarrisonRuntime
    {
        private static readonly string[] LastSummary = new string[2];

        static GarrisonRuntime()
        {
            CampaignScope.Register("campaign-planner-garrison-rows", () =>
            {
                for (int a = 0; a < LastSummary.Length; a++) LastSummary[a] = null;
            });
        }

        // After TheaterPlanner.Plan: the ordinary posts of this review join the plan's secession posts in plan.Garrisons, where the
        // allocator seats both sorts. The ledger's records name each post's incumbent from the last review.
        internal static void PlanPosts(CampaignSnapshot snapshot, TheaterPlan plan)
        {
            using (TelemetryPerf.Scope("campaign.planner.garrison", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                var ledger = GarrisonLedgers.For(snapshot.Alliance);
                if (ledger == null || !FlagOn(Plugin.EnableGarrisonManning)) return;
                var posts = new List<GarrisonPost>();
                GarrisonPlanner.PlanPosts(snapshot, plan, ledger.Posts, posts);
                plan.Garrisons.AddRange(posts);
            }
        }

        // After the allocation: the ledger records who holds which post, and the two rows say what changed ([GarrisonPost] per
        // transition, [Garrison] when its counts change).
        internal static void Sync(CampaignSnapshot snapshot, TheaterPlan plan, Allocation allocation)
        {
            using (TelemetryPerf.Scope("campaign.planner.garrison", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                int alliance = snapshot.Alliance;
                var ledger = GarrisonLedgers.For(alliance);
                if (ledger == null) return;
                var result = ledger.Sync(plan.Garrisons, allocation, snapshot);
                var transitions = ledger.Transitions;
                for (int t = 0; t < transitions.Count; t++)
                    TelemetryRouter.LegacyInfo(GarrisonRows.Transition(alliance, transitions[t]), TelemetryLayer.Campaign);
                string row = GarrisonRows.Summary(alliance, result);
                if (LastSummary[alliance] == row) return;
                LastSummary[alliance] = row;
                if (result.Posts > 0 || result.Released > 0) TelemetryRouter.LegacyInfo(row, TelemetryLayer.Campaign);
            }
        }

        private static bool FlagOn(BepInEx.Configuration.ConfigEntry<bool> entry)
        {
            try { return entry != null && entry.Value; }
            catch { return false; }
        }
    }
}
