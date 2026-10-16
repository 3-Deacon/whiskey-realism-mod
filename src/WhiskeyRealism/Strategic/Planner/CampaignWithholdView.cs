using System;
using System.Collections.Generic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    // What the published plan hands the executors' patches besides the plan, the allocation and the sector model: the decision
    // the plan belongs to, the decision whose main-effort launch the chapter tempo roll released, whether the executors may run
    // at all (the pre-war gate) and the W&L map-group player's formation, whom the deferred-Support clause never withholds.
    // Published with the plan (CampaignPlanStore.PublishExecutorContext joins the day's atomic publish), so a patch between the
    // slices of a time-sliced review never sees today's release next to yesterday's plan.
    internal struct ExecutorContext
    {
        public int DecisionSerial;
        public int ReleasedDecision;       // PlannerState.TempoReleasedDecision, -1 none
        public bool ExecutorsAllowed;      // PlannerChapterRules.ExecutorsAllowed at the review
        public int MapGroupInstanceId;     // the formation of WlChainClass.PlayerMapGroup, -1 none

        internal static ExecutorContext From(PlannerState state, bool executorsAllowed, int mapGroupInstanceId)
        {
            return new ExecutorContext
            {
                DecisionSerial = state.DecisionSerial,
                ReleasedDecision = state.TempoReleasedDecision,
                ExecutorsAllowed = executorsAllowed,
                MapGroupInstanceId = mapGroupInstanceId
            };
        }

        // Released means TempoReleasedDecision == DecisionSerial, everywhere in the plan.
        internal bool Released => ReleasedDecision == DecisionSerial;
    }

    // A paused alliance or a failing review never steers on stale state: every planner patch ignores a published plan more than
    // MaxAgeDays review days older than today. Today is the latest review day any alliance's ReviewPlan noted (the Plan job
    // runs for both alliances, also while paused, as bookkeeping), so a published plan whose own day falls behind it is stale.
    public static class PlanFreshness
    {
        public const int MaxAgeDays = 2;
        private static int _today = -1;

        static PlanFreshness()
        {
            CampaignScope.Register("campaign-planner-freshness", () => _today = -1);
        }

        internal static void NoteToday(int daySerial)
        {
            _today = daySerial;
        }

        // The executors act only on today's picture: a Plan job that failed before it published leaves yesterday's plan in the
        // working copy, and its snapshot is yesterday's too.
        internal static bool IsToday(int daySerial)
        {
            return _today >= 0 && daySerial == _today;
        }

        // No day noted yet: the plan is read.
        internal static bool IsFresh(int planDay)
        {
            return IsFresh(planDay, _today);
        }

        // More than MaxAgeDays older is stale; a plan from the future (the clock went back after a load) is fresh.
        internal static bool IsFresh(int planDay, int today)
        {
            return today < 0 || today - planDay <= MaxAgeDays;
        }
    }

    // The view the availability Postfixes read (plan G5a). Pure, read-only: the published plan, allocation, sector model and
    // executor context (CampaignPlanStore) and the move tracker's in-transit set. Everything is ignored (nothing withheld)
    // when no plan is published, when it is stale, in a pre-war scenario, and for an alliance other than 0 and 1. The
    // availability Postfixes also test Enable Campaign Planner Executors before asking.
    public static class CampaignWithholdView
    {
        // The availability tests run per candidate unit per faction pass (14080-14158 is called for every own unit by every
        // lead): the allocation's assignments are indexed by instance id once per published allocation, so a call is a
        // dictionary read and allocates nothing after warm-up.
        private static Allocation _indexed;
        private static readonly Dictionary<int, int> Positions = new Dictionary<int, int>();

        static CampaignWithholdView()
        {
            CampaignScope.Register("campaign-planner-withhold-index", () =>
            {
                _indexed = null;
                Positions.Clear();
            });
        }

        private static bool TryFind(Allocation allocation, int instanceId, out Assignment assignment)
        {
            if (!ReferenceEquals(_indexed, allocation))
            {
                Positions.Clear();
                for (int i = 0; i < allocation.Assignments.Count; i++) Positions[allocation.Assignments[i].InstanceId] = i;
                _indexed = allocation;
            }
            if (Positions.TryGetValue(instanceId, out int at) && at < allocation.Assignments.Count)
            {
                assignment = allocation.Assignments[at];
                return true;
            }
            assignment = default(Assignment);
            return false;
        }

        // Vanilla's offensive pool (IsUnitAvailableForOffensiveOperations 14080-14158): the formation is withheld when the
        // allocation withholds it (Reserve, CapitalGuard, DepotBuilder, Recon, Recover, Hold at the sector's need), when it is in
        // transit, when it builds an active depot task (DepotTaskLedger.HoldsBuilder: a depot rises only while its builder stays
        // within bugle range, so the hold outlasts the allocation's role, G5c), when it is a MainEffort member of the plan's mass
        // (decision 5: only the mover launches the mass, so vanilla's own roll cannot also send it) or a Support formation (not the
        // map-group player's) of a decision whose launch the tempo roll has not released (no Support goes ahead of the main
        // effort). The mass and the deferred Support are offense-only clauses: WithheldFromDefense never applies them.
        public static bool WithheldFromOffense(int alliance, int instanceId)
        {
            if (!Read(alliance, out var allocation, out _, out var ctx)) return false;
            if (MoveTrackers.For(alliance).InTransit(instanceId)) return true;
            if (HeldBuilder(alliance, instanceId)) return true;
            if (!TryFind(allocation, instanceId, out var a)) return false;
            if (a.Withheld) return true;
            if (a.Role == FormationRole.MainEffort) return true;
            return a.Role == FormationRole.Support && !ctx.Released && instanceId != ctx.MapGroupInstanceId;
        }

        // Vanilla's defence (IsUnitAvailableForDefensiveOperation 13460): withheld when the allocation withholds it, it is in
        // transit or it builds an active depot task; never the mass or the deferred-Support clause, and a Hold formation withheld
        // at its sector's need stays available when the enemy vanilla asks about stands in that sector (vanilla passes
        // enemyunits[j]'s position, 13610).
        public static bool WithheldFromDefense(int alliance, int instanceId, float enemyX, float enemyZ)
        {
            if (!Read(alliance, out var allocation, out var sectors, out _)) return false;
            if (MoveTrackers.For(alliance).InTransit(instanceId)) return true;
            if (HeldBuilder(alliance, instanceId)) return true;
            if (!TryFind(allocation, instanceId, out var a) || !a.Withheld) return false;
            if (a.Role == FormationRole.Hold && a.SectorId >= 0 && sectors != null && sectors.SectorAt(enemyX, enemyZ) == a.SectorId) return false;
            return true;
        }

        // The importance-pointer Postfix's inputs (#2): the published plan and sector model, subject to the same freshness and
        // pre-war rules, and only once the decision is released. Before the tempo roll releases the launch #2 leaves vanilla's own
        // pick, so no formation vanilla leaves free in a Main or Support sector is pointed at the plan target ahead of the main
        // effort (decision 5, no piecemeal attack).
        internal static bool TrySteering(int alliance, out TheaterPlan plan, out SectorModel sectors)
        {
            plan = null;
            sectors = null;
            if (!Read(alliance, out _, out var model, out var ctx) || !ctx.Released) return false;
            if (!CampaignPlanStore.TryGetPlan(alliance, out var published) || model == null) return false;
            plan = published;
            sectors = model;
            return true;
        }

        // The depot-builder hold for a reader outside the availability Postfixes (the fort job, FortConstructionGovernorPatch): the
        // same picture test as every other clause here, Read (a published plan and allocation, executors allowed, the plan fresh),
        // so a stale plan or a paused review never holds a builder (plan G5c, verify r2 F6c).
        internal static bool HoldsDepotBuilder(int alliance, int instanceId)
        {
            return Read(alliance, out _, out _, out _) && HeldBuilder(alliance, instanceId);
        }

        private static bool HeldBuilder(int alliance, int instanceId)
        {
            return DepotTaskLedgers.For(alliance).HoldsBuilder(instanceId);
        }

        private static bool Read(int alliance, out Allocation allocation, out SectorModel sectors, out ExecutorContext context)
        {
            allocation = null;
            sectors = null;
            context = default(ExecutorContext);
            if (MoveTrackers.For(alliance) == null) return false;
            if (!CampaignPlanStore.TryGetPlan(alliance, out var plan) || !CampaignPlanStore.TryGetAllocation(alliance, out allocation)) return false;
            if (!CampaignPlanStore.TryGetExecutorContext(alliance, out context) || !context.ExecutorsAllowed) return false;
            if (!PlanFreshness.IsFresh(plan.DaySerial)) return false;
            CampaignPlanStore.TryGetSectors(alliance, out sectors);
            return true;
        }
    }
}
