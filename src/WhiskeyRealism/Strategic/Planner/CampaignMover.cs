using System;
using System.Collections.Generic;
using System.Globalization;

namespace WhiskeyRealism.Strategic.Planner
{
    // The vanilla actions the mover pass performs and the public map facts it asks (plan G5a). The runtime
    // (CampaignMoverRuntime) implements it over AICampaign.MoveUnitTo, the W&L order bridge and the faction lists; the harness
    // implements it with a recording fake. Nothing here names the defensive operation list: vanilla ends and relieves its own
    // defence (13782-13848, 14068-14078).
    internal interface IMoverHost : IMoveHost
    {
        // The alliance owning the state at a map point (GameVars.nation[GetStateOfField(point, true)].alliance), -1 unknown.
        int StateAllianceAt(float x, float z);
        // GameVars.alliance[reviewing alliance].IsAtWarWith(other) (63127).
        bool AtWar(int otherAlliance);
        // Adds to / removes from AIFaction.unitsinoffensiveoperations.
        void ListOffensive(int instanceId, bool listed);
    }

    // The tempo roll's public and own-side inputs, read by the runtime once per review.
    internal struct TempoWorld
    {
        public float ChapterAggression;      // GamePrefs.aiagressiveness[chapter]
        public float FactionAggression;      // GamePrefs.aiagressivenessoffensiveoperationfaction[alliance], 1 above alliance 1
        public float CampaignAggression;     // GameVars.usedcampaignagressiveness
        public float WinterModifier;         // 1 outside the winter quarters
        public bool WlActive;                // DLC_WL.dlc_scenarioactive
        public float WlAggressiveness;       // GamePrefs.dlcwl_careerincreasedaggressiveness
        public bool Forced;                  // the scripted 1861 push (ForcedInvasion) || debug_permanentaimovement
        public float CampaignDate;           // GameVars.GetCampaignFloatingDate()
    }

    internal sealed class MoverInputs
    {
        public CampaignSnapshot Snapshot;
        public TheaterPlan Plan;
        public Allocation Allocation;
        public bool WlPlayerAlliance;        // the reviewing alliance is the W&L player's (a career is running)
        public int Chapter;                  // Policy.CurrentChapter (telemetry)
        public TempoWorld Tempo;
    }

    internal sealed class MoverTally
    {
        public int Issued, Confirmed, Failed, Retried, GaveUp, Queued, SkippedPlayer, SkippedBusy, Deferred;
        // The tempo roll of this review, when one was evaluated (a new decision's launch).
        public LaunchVerdict Tempo;
        public bool TempoFirstRow, TempoReleaseRow;
        // A kept main effort with no formation the mover can send (first review of the state; see Review). After G3 leaves out the
        // formations that gave up this state should not occur; the row is how a smoke run would see it.
        public bool MainUndispatchableRow;
        public int TempoDecision, TempoMainObjective, TempoChapter;

        public bool Any => Issued + Confirmed + Failed + Retried + GaveUp + Queued + SkippedPlayer + SkippedBusy + Deferred > 0;

        // [CampaignMover] once per review when any count > 0.
        public string Row(int alliance)
        {
            if (!Any) return "";
            return "[CampaignMover] alliance=" + alliance + " issued=" + Issued + " confirmed=" + Confirmed + " failed=" + Failed + " retried=" + Retried
                + " gaveUp=" + GaveUp + " queued=" + Queued + " skippedPlayer=" + SkippedPlayer
                + " skippedBusy=" + SkippedBusy + " deferred=" + Deferred;
        }

        // [CampaignTempo] undispatchable=true, once per decision per entry into the state.
        public string UndispatchableRow(int alliance)
        {
            return "[CampaignTempo] alliance=" + alliance + " main=" + TempoMainObjective + " decision=" + TempoDecision + " chapter=" + TempoChapter
                + " undispatchable=true";
        }

        // [CampaignTempo] on the first review of a decision and on its release (two rows at most).
        public string TempoRow(int alliance)
        {
            var inv = CultureInfo.InvariantCulture;
            return "[CampaignTempo] alliance=" + alliance + " main=" + TempoMainObjective + " decision=" + TempoDecision + " chapter=" + TempoChapter
                + " gap=" + ((int)Tempo.GapDays).ToString(inv)
                + " p=" + (Tempo.NeedsRoll ? Tempo.Probability.ToString("0.000", inv) : "none")
                + " roll=" + (Tempo.NeedsRoll ? Tempo.Roll.ToString("0.000", inv) : "none")
                + " released=" + (Tempo.Released ? "true" : "false") + " forced=" + (Tempo.Forced ? "true" : "false");
        }
    }

    // The mover (plan G5a, spec C6): the daily review's movement step for one alliance, pure over the host. For each
    // MainEffort / Support / CapitalGuard / Recover / DepotBuilder assignment whose formation is not where its role needs it
    // it issues one MoveUnitTo (at most MoveTracker.MaxMovesPerDay a day), judges last review's moves first, removes a
    // Recover formation from vanilla's offensive list, and applies the chapter tempo gate:
    //   - the first MainEffort dispatch of a decision waits for vanilla's tempo roll (CampaignTempoGate), once per decision;
    //     a decision whose launch needs no roll is released before its first dispatch (TempoReleasedDecision ==
    //     DecisionSerial is the one release predicate); a STAGED main effort (TheaterPlan.MainStaged: the objective is unscouted)
    //     only gathers at its start line, which is not a march while the line is in own or neutral ground, and its roll is evaluated
    //     when the picture is scouted and the target is the objective;
    //   - a Support move (the one reinforcement from another theater) into enemy-held ground waits for that release; in own or
    //     neutral ground it is never held;
    //   - a decision whose mass has no dispatchable MainEffort formation dispatches nothing and is never released.
    // The W&L player: the player's own command, a CIC side and the map-group player's group are never moved or ordered (the
    // allocator gives the map-group player's group no role the mover sends: it is never mass, and outside the mass a formation holds,
    // smoke amendments 2026-10-16 D3); the AI group above a player below map level is moved as any AI formation (stage F's
    // MoveUnitTo Postfix dispatches the march).
    internal static class CampaignMover
    {
        // Within this of the target, or a final waypoint this close to it, the formation is there or already underway.
        public const float ArrivalRadius = 10f;

        // The order the roles are served in under the daily cap.
        private static readonly FormationRole[] Priority =
            { FormationRole.CapitalGuard, FormationRole.MainEffort, FormationRole.Garrison, FormationRole.Recover, FormationRole.DepotBuilder, FormationRole.Support };

        // Does the mover send a formation of this role? (The roles G4 can give a formation that G5d's recon executor must then leave
        // to the mover: one owner per formation per review.)
        internal static bool MovesRole(FormationRole role)
        {
            return Array.IndexOf(Priority, role) >= 0;
        }

        public static MoverTally Review(MoverInputs inputs, IMoverHost host, MoveTracker tracker, PlannerState state)
        {
            var tally = new MoverTally();
            if (inputs == null || host == null || tracker == null || state == null) return tally;
            var s = inputs.Snapshot;
            var plan = inputs.Plan;
            var allocation = inputs.Allocation;
            if (s == null || plan == null || allocation == null) return tally;

            JudgeLastReview(s, allocation, tracker, tally);
            ReleaseRecoverFromOffense(s, allocation, host);
            bool released = state.TempoReleasedDecision == state.DecisionSerial;
            bool dispatchable = plan.MainObjectiveId >= 0 && HasDispatchableMainEffort(s, allocation, host);
            NoteUndispatchable(inputs, state, tally, dispatchable);
            // D1: a staged (unscouted) main effort is only gathering at its start line; the tempo roll is the march's, so it waits
            // until the picture is scouted and the target is the objective.
            if (!released && dispatchable && !plan.MainStaged) released = EvaluateLaunch(inputs, host, state, tally);
            foreach (var role in Priority)
                for (int k = 0; k < allocation.Assignments.Count; k++)
                    if (allocation.Assignments[k].Role == role)
                        Dispatch(inputs, host, tracker, released, allocation.Assignments[k], tally);
            state.LastFullReviewDay = s.DaySerial;
            return tally;
        }

        // P8: the moves issued at an earlier review, judged against today's picture. A formation whose order still waits in
        // vanilla's queue (DefensiveMovingOrder.UpdateOrders serves one order at a time, 9966-10052) is not judged yet. A queued
        // record settles through MoveTracker.SettleQueued once the order ahead of it has ended or the role no longer sends the
        // formation (the planner's records only: a recon record's owner is the recon executor, which settles its own), and
        // after MoveTracker.QueuedExpiryDays whatever happens; the dispatch below then re-issues the move or drops it.
        private static void JudgeLastReview(CampaignSnapshot s, Allocation allocation, MoveTracker tracker, MoverTally tally)
        {
            tracker.SettleExpiredQueued(s.DaySerial);
            var ids = new List<int>();
            tracker.CollectInTransit(ids);
            for (int k = 0; k < ids.Count; k++)
            {
                int i = s.IndexOfOwn(ids[k]);
                if (i < 0)
                {
                    tracker.Forget(ids[k]);
                    continue;
                }
                var f = s.Own[i];
                bool orderRunning = (f.Flags & FormationFlags.MoveQueued) != 0;
                tracker.SettleQueued(f.InstanceId, MoveBudget.Planner, RoleStillSends(allocation, f.InstanceId), orderRunning);
                tracker.SettleQueued(f.InstanceId, MoveBudget.Recon, wanted: true, orderRunning);
                if (!tracker.InTransit(f.InstanceId) || orderRunning) continue;
                switch (tracker.Judge(f.InstanceId, f.X, f.Z, f.WaypointX, f.WaypointZ, s.DaySerial))
                {
                    case MoveOutcome.Confirmed: tally.Confirmed++; break;
                    case MoveOutcome.Failed: tally.Failed++; break;
                    case MoveOutcome.GaveUp: tally.GaveUp++; break;
                }
            }
        }

        private static bool RoleStillSends(Allocation allocation, int instanceId)
        {
            return allocation.TryGet(instanceId, out var a) && MovesRole(a.Role);
        }

        // Vanilla never checks supply before or during an offensive (14080-14158 has no supply term, job 6's gate is dead,
        // 14009-14019): a Recover formation leaves unitsinoffensiveoperations as RelieveUnitsInOperations does for a retreating
        // or low-morale one (14068-14078), and is then moved toward supply. Only a formation the planner may move.
        private static void ReleaseRecoverFromOffense(CampaignSnapshot s, Allocation allocation, IMoverHost host)
        {
            for (int k = 0; k < allocation.Assignments.Count; k++)
            {
                var a = allocation.Assignments[k];
                if (a.Role != FormationRole.Recover) continue;
                int i = s.IndexOfOwn(a.InstanceId);
                if (i < 0 || !WlChainRules.MayMove(s.Own[i].Chain)) continue;
                if ((s.Own[i].Flags & FormationFlags.InOffensiveOps) != 0) host.ListOffensive(a.InstanceId, false);
            }
        }

        // A decision with a main objective whose MainEffort assignments are none the mover can send (the planner's mass is
        // G3's; formations that gave up are left out of it, so this should not occur): one telemetry row on entering the state.
        private static void NoteUndispatchable(MoverInputs inputs, PlannerState state, MoverTally tally, bool dispatchable)
        {
            if (inputs.Plan.MainObjectiveId < 0 || dispatchable)
            {
                state.UndispatchableRowDecision = -1;
                return;
            }
            if (state.UndispatchableRowDecision == state.DecisionSerial) return;
            state.UndispatchableRowDecision = state.DecisionSerial;
            tally.MainUndispatchableRow = true;
            tally.TempoDecision = state.DecisionSerial;
            tally.TempoMainObjective = inputs.Plan.MainObjectiveId;
            tally.TempoChapter = inputs.Chapter;
        }

        // The chapter tempo roll for a new decision's launch (decision 5). Evaluated only when the mass has a dispatchable
        // MainEffort formation (the caller checks); the verdict releases the decision (TempoReleasedDecision = DecisionSerial) or defers it.
        private static bool EvaluateLaunch(MoverInputs inputs, IMoverHost host, PlannerState state, MoverTally tally)
        {
            var s = inputs.Snapshot;
            var plan = inputs.Plan;

            var idle = new List<float>();
            bool playerInMass = false;
            for (int k = 0; k < plan.MassInstanceIds.Count; k++)
            {
                int i = s.IndexOfOwn(plan.MassInstanceIds[k]);
                if (i < 0) continue;
                idle.Add(s.Own[i].TimeOfNotMoving);
                if (s.Own[i].Chain == WlChainClass.CarryingGroup) playerInMass = true;
            }
            MainTarget(s, plan, out float tx, out float tz);
            int ground = host.StateAllianceAt(tx, tz);
            var w = inputs.Tempo;
            var ti = new TempoInputs
            {
                ChapterAggression = w.ChapterAggression, FactionAggression = w.FactionAggression, CampaignAggression = w.CampaignAggression,
                WinterModifier = w.WinterModifier, WlActive = w.WlActive, WlAggressiveness = w.WlAggressiveness, PlayerGroupInMass = playerInMass,
                Forced = w.Forced, IdleYears = CampaignTempoGate.IdleYears(w.CampaignDate, idle),
                TodayDay = s.DaySerial, LastFullReviewDay = state.LastFullReviewDay, CampaignSeed = state.CampaignSeed,
                Alliance = s.Alliance, DecisionSerial = state.DecisionSerial,
                TargetInAnotherAllianceState = ground >= 0 && ground != s.Alliance, BelievedEnemyAtTarget = plan.MainEnemyStrength
            };
            var verdict = CampaignTempoGate.Evaluate(in ti);
            tally.Tempo = verdict;
            tally.TempoDecision = state.DecisionSerial;
            tally.TempoMainObjective = plan.MainObjectiveId;
            tally.TempoChapter = inputs.Chapter;
            tally.TempoFirstRow = state.TempoRowDecision != state.DecisionSerial;
            state.TempoRowDecision = state.DecisionSerial;
            if (verdict.Released)
            {
                state.TempoReleasedDecision = state.DecisionSerial;
                tally.TempoReleaseRow = true;
            }
            return verdict.Released;
        }

        private static bool HasDispatchableMainEffort(CampaignSnapshot s, Allocation allocation, IMoverHost host)
        {
            for (int k = 0; k < allocation.Assignments.Count; k++)
            {
                var a = allocation.Assignments[k];
                if (a.Role != FormationRole.MainEffort) continue;
                int i = s.IndexOfOwn(a.InstanceId);
                if (i < 0) continue;
                var f = s.Own[i];
                if (WlChainRules.MayMove(f.Chain) && TheaterPlanner.Movable(f)) return true;
            }
            return false;
        }

        // The main objective's own position (a believed enemy and the state it stands in are judged there, as vanilla judges
        // GetClosestObjectivePosOfArea, 14329): the objective record nearest the main sector's target, else that target.
        private static void MainTarget(CampaignSnapshot s, TheaterPlan plan, out float x, out float z)
        {
            x = 0f;
            z = 0f;
            for (int k = 0; k < plan.Sectors.Count; k++)
                if (plan.Sectors[k] != null && plan.Sectors[k].SectorId == plan.MainSectorId)
                {
                    x = plan.Sectors[k].TargetX;
                    z = plan.Sectors[k].TargetZ;
                    break;
                }
            float best = float.MaxValue;
            float bx = x, bz = z;
            for (int k = 0; k < s.Objectives.Count; k++)
            {
                var o = s.Objectives[k];
                if (o.ObjectiveId != plan.MainObjectiveId) continue;
                float d = (o.X - x) * (o.X - x) + (o.Z - z) * (o.Z - z);
                if (d >= best) continue;
                best = d;
                bx = o.X;
                bz = o.Z;
            }
            x = bx;
            z = bz;
        }

        private static void Dispatch(MoverInputs inputs, IMoverHost host, MoveTracker tracker, bool released, Assignment a, MoverTally tally)
        {
            var s = inputs.Snapshot;
            int i = s.IndexOfOwn(a.InstanceId);
            if (i < 0) return;
            var f = s.Own[i];
            float tx = a.TargetX, tz = a.TargetZ;

            // The player's own command, a CIC side and the map-group player (an order-only formation, never mass, D3) are not moved.
            if (!WlChainRules.MayMove(f.Chain))
            {
                tally.SkippedPlayer++;
                return;
            }
            if (!TheaterPlanner.Movable(f))
            {
                tally.SkippedBusy++;
                return;
            }
            if (Distance(f.WaypointX, f.WaypointZ, tx, tz) <= ArrivalRadius) return;
            // The launch gates a march into enemy-held ground. The main effort's gathering at a staged start line is not one while
            // the line is in own or neutral ground; a start line that happens to lie in enemy ground still waits for the release.
            bool marches = a.Role == FormationRole.MainEffort ? !inputs.Plan.MainStaged || EnemyHeld(s, host, tx, tz)
                : a.Role == FormationRole.Support && EnemyHeld(s, host, tx, tz);
            if (!released && marches)
            {
                tally.Deferred++;
                return;
            }
            if (!tracker.ShouldIssue(f.InstanceId, tx, tz, s.DaySerial)) return;

            bool moved = MoveIssue.Issue(host, tracker, in f, tx, tz, ObjectiveOf(inputs.Plan, a), s.DaySerial, inputs.WlPlayerAlliance, MoveBudget.Planner, out var outcome);
            Count(tally, outcome);
            if (moved && (a.Role == FormationRole.MainEffort || a.Role == FormationRole.Support)) host.ListOffensive(f.InstanceId, true);
        }

        // The objective a move is toward: a MainEffort or Support assignment goes to its sector's target, the objective of that
        // sector's plan; any other role's target (a depot, a hold point, a supply source, the capital) is none. G3 reads it from
        // the tracker when a formation gives up (MoveTracker, GaveUpMove.ObjectiveId).
        private static int ObjectiveOf(TheaterPlan plan, Assignment a)
        {
            if (a.Role != FormationRole.MainEffort && a.Role != FormationRole.Support) return -1;
            for (int k = 0; k < plan.Sectors.Count; k++)
                if (plan.Sectors[k] != null && plan.Sectors[k].SectorId == a.SectorId) return plan.Sectors[k].TargetObjectiveId;
            return -1;
        }

        private static void Count(MoverTally tally, MoveOutcome outcome)
        {
            switch (outcome)
            {
                case MoveOutcome.Issued: tally.Issued++; break;
                case MoveOutcome.Retried: tally.Retried++; break;
                case MoveOutcome.Queued: tally.Queued++; break;
                case MoveOutcome.Failed: tally.Failed++; break;
            }
        }

        // 25714 as at 14335-14336: the state at the point belongs to an alliance at war with this one.
        private static bool EnemyHeld(CampaignSnapshot s, IMoverHost host, float x, float z)
        {
            int ground = host.StateAllianceAt(x, z);
            return ground >= 0 && ground != s.Alliance && host.AtWar(ground);
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
