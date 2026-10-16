using System;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    // The planner's plan state per AI alliance (0 and 1 only). Written only by
    // CampaignPlannerRuntime.ReviewPlan inside the daily review (Publish writes the working copy);
    // Harmony patches read it through the TryGet accessors (G5a/G5b/G5c) and never write it.
    // A reader inside a review job (the planner's own executors) gets the working copy; a reader
    // outside one gets the copy published at the last day's commit (DailyReviewView,
    // PublishedDay, StrategicCoordinator.PublishStrategicState), so a patch between the slices of a
    // time-sliced review never sees today's plan next to yesterday's ledgers. A plan, allocation
    // and sector model are built fresh by each review and not changed after Publish, so the
    // published copy shares them. Per campaign: cleared by CampaignScope
    // (StrategicCoordinator.ResetAll).
    public static class CampaignPlanStore
    {
        private const int Alliances = 2;

        private sealed class PlanDay
        {
            internal readonly TheaterPlan[] Plans = new TheaterPlan[Alliances];
            internal readonly Allocation[] Allocations = new Allocation[Alliances];
            internal readonly SectorModel[] Sectors = new SectorModel[Alliances];
            // G5a: what the executors' patches read besides the plan (the tempo release, the pre-war gate), published with it.
            internal readonly ExecutorContext[] Contexts = new ExecutorContext[Alliances];
            internal readonly bool[] HasContext = new bool[Alliances];
        }

        private static readonly PlanDay Working = new PlanDay();
        private static readonly PublishedDay<PlanDay> Day = new PublishedDay<PlanDay>(new PlanDay(), new PlanDay());
        private static readonly Action<PlanDay> Fill = FillFromWorking;
        private static readonly Action<PlanDay> ClearDay = Clear;

        static CampaignPlanStore()
        {
            CampaignScope.Register("campaign-planner-store", Reset);
        }

        private static PlanDay View => DailyReviewView.ReadsWorking ? Working : Day.Published;

        public static bool TryGetPlan(int alliance, out TheaterPlan plan)
        {
            plan = InRange(alliance) ? View.Plans[alliance] : null;
            return plan != null;
        }

        public static bool TryGetAllocation(int alliance, out Allocation allocation)
        {
            allocation = InRange(alliance) ? View.Allocations[alliance] : null;
            return allocation != null;
        }

        // The defensive availability Postfix's SectorAt (G5a).
        public static bool TryGetSectors(int alliance, out SectorModel sectors)
        {
            sectors = InRange(alliance) ? View.Sectors[alliance] : null;
            return sectors != null;
        }

        // The executor context of the plan (G5a): written by ReviewPlan with the plan and again by ReviewExecute once the
        // mover has settled the tempo release, always inside the day's review, so it joins the atomic day publish.
        internal static bool TryGetExecutorContext(int alliance, out ExecutorContext context)
        {
            context = default(ExecutorContext);
            if (!InRange(alliance) || !View.HasContext[alliance]) return false;
            context = View.Contexts[alliance];
            return true;
        }

        internal static void PublishExecutorContext(int alliance, in ExecutorContext context)
        {
            if (!InRange(alliance)) return;
            Working.Contexts[alliance] = context;
            Working.HasContext[alliance] = true;
        }

        internal static void Publish(int alliance, TheaterPlan plan, Allocation allocation, SectorModel sectors)
        {
            if (!InRange(alliance)) return;
            Working.Plans[alliance] = plan;
            Working.Allocations[alliance] = allocation;
            Working.Sectors[alliance] = sectors;
        }

        // The day's commit (StrategicCoordinator.PublishStrategicState).
        internal static void PreparePublish()
        {
            Day.Prepare(Fill);
        }

        internal static void SwapPublished()
        {
            Day.Swap();
        }

        private static void FillFromWorking(PlanDay copy)
        {
            for (int a = 0; a < Alliances; a++)
            {
                copy.Plans[a] = Working.Plans[a];
                copy.Allocations[a] = Working.Allocations[a];
                copy.Sectors[a] = Working.Sectors[a];
                copy.Contexts[a] = Working.Contexts[a];
                copy.HasContext[a] = Working.HasContext[a];
            }
        }

        private static void Clear(PlanDay day)
        {
            for (int a = 0; a < Alliances; a++)
            {
                day.Plans[a] = null;
                day.Allocations[a] = null;
                day.Sectors[a] = null;
                day.Contexts[a] = default(ExecutorContext);
                day.HasContext[a] = false;
            }
        }

        private static void Reset()
        {
            Clear(Working);
            Day.Reset(ClearDay);
        }

        private static bool InRange(int alliance)
        {
            return alliance >= 0 && alliance < Alliances;
        }
    }
}
