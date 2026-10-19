using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // Main effort commitment (foresight F7, D11 requirement-commitment and requirement-decision-end; root cause 4). The CSA main effort went
    // initial, no-candidate, initial, no-candidate in six days because the march dropped a corps' readiness below step 2 and D5 then took it out
    // of the capacity. A member of the last non-empty mass that the mover has DISPATCHED toward the main objective now stays in that objective's
    // capacity, whatever its readiness, for exactly as long as D4's own rules keep the objective; vanilla relieves an operation member only for
    // retreat or morale (14072). No constant is added: the commitment ends the review Decide no longer keeps the objective.
    //   row 4 of the ownership table  CommittedSource claims the dispatched members of the last mass while its objective is the main effort;
    //   TheaterPlanner.MassFor        counts a committed formation in the capacity of the last mass's objective (Massable), whatever its step;
    //   TheaterPlanner.SelectMass     takes the committed formations of the chosen objective first, whatever the need (row 8);
    //   UpdateLastMass                after the mass is selected, replaces the last mass and reports the commitments that ended;
    //   when the push ends            its members are claimed Committed from rows 1-7 but the push is gone: the new mass takes the ones it may as
    //                                 ordinary members, every other one is Hold committed-wait in place for the review (ForceAllocator.RoleOf),
    //                                 which the mover stops where it stands; from the next review the holds, the capital reserve and
    //                                 department reinforcement place it.
    internal static class Commitment
    {
        // The producer of row 4. It claims nothing without the state, the tracker's dispatch list or the flag.
        internal static readonly IClaimSource Source = new CommittedSource();

        private sealed class CommittedSource : IClaimSource
        {
            public int Row => ClaimTable.RowCommitted;

            public void Produce(in ClaimContext ctx, Claim[] claims)
            {
                var s = ctx.Snapshot;
                if (s == null || ctx.State == null || ctx.Dispatched == null) return;
                for (int i = 0; i < claims.Length; i++)
                    if (ClaimTable.Takes(ClaimTable.RowCommitted, claims[i].Claimant) && IsCommitted(in ctx, s.Own[i]))
                        claims[i].Claimant = Claimant.Committed;
            }
        }

        // A dispatched member of the last mass is committed while the last mass's objective is the main effort, if the planner may move it, it is
        // movable and not on retreat, its morale and raw supply clear the Recover floors, it is no depot builder and it has not given up on the
        // objective. Its readiness is not asked.
        internal static bool IsCommitted(in ClaimContext ctx, in FormationRecord f)
        {
            var state = ctx.State;
            if (!PlannerSettings.MainEffortCommitment || state == null || ctx.Dispatched == null) return false;
            int objective = state.LastMassObjective;
            if (objective < 0 || objective != state.CurrentMainObjective || !state.InLastMass(f.UnitKey)) return false;
            if (!WlChainRules.MayMove(f.Chain) || !TheaterPlanner.Movable(f) || ReserveRules.Recovers(f, ctx.Snapshot.MinMoraleForMovement)) return false;
            if (ReserveRules.IsHeldBuilder(ctx.Held, f.InstanceId) || ClaimTable.GaveUpOn(ctx.GaveUp, f.InstanceId, objective)) return false;
            return WasDispatched(ctx.Dispatched, f.InstanceId, objective);
        }

        private static bool WasDispatched(IReadOnlyList<DispatchedMove> dispatched, int instanceId, int objectiveId)
        {
            for (int k = 0; k < dispatched.Count; k++)
                if (dispatched[k].InstanceId == instanceId && dispatched[k].ObjectiveId == objectiveId) return true;
            return false;
        }

        // Whether the push the plan chose is the one the last mass was on: the commitment goes on.
        internal static bool Continues(PlannerState state, int chosenObjectiveId)
        {
            return state != null && chosenObjectiveId >= 0 && state.LastMassObjective == chosenObjectiveId;
        }

        // The last-mass update, once per review after SelectMass and the mass is in the table (D11 requirement-commitment). A non-empty mass
        // replaces the last mass; while the push goes on, a member that is in the snapshot but cannot be sent today (in battle, in a vanilla
        // defensive operation, embarked, in transfer) is kept in it even when today's mass does not hold it, and a movable member the mass did
        // not take is dropped. With no main effort the last mass is cleared. The commitments that ended (the old members the new last mass does
        // not hold, still in the snapshot) go to plan.CommitmentEnded in snapshot order.
        internal static void UpdateLastMass(CampaignSnapshot s, PlannerState state, TheaterPlan plan)
        {
            var kept = new HashSet<string>(StringComparer.Ordinal);
            if (plan.MainObjectiveId >= 0)
            {
                for (int k = 0; k < plan.MassInstanceIds.Count; k++)
                {
                    int i = s.IndexOfOwn(plan.MassInstanceIds[k]);
                    if (i >= 0 && !string.IsNullOrEmpty(s.Own[i].UnitKey)) kept.Add(s.Own[i].UnitKey);
                }
                if (kept.Count > 0 && Continues(state, plan.MainObjectiveId))
                    for (int i = 0; i < s.Own.Count; i++)
                        if (state.InLastMass(s.Own[i].UnitKey) && !TheaterPlanner.Movable(s.Own[i])) kept.Add(s.Own[i].UnitKey);
            }
            plan.CommitmentEnded.Clear();
            for (int i = 0; i < s.Own.Count; i++)
                if (state.InLastMass(s.Own[i].UnitKey) && !kept.Contains(s.Own[i].UnitKey)) plan.CommitmentEnded.Add(s.Own[i].InstanceId);
            state.SetLastMass(plan.MainObjectiveId, kept);
        }
    }
}
