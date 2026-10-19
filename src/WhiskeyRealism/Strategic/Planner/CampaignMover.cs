using System;
using WhiskeyRealism.Telemetry;
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
        // The mirror of vanilla's IsOnEnemySoil(false, point, returntrueifnotonstate: true, usestateoccupation: true) (139304-139336), the test
        // SetWaypoint's readiness refusal applies (91336-91350): ground vanilla refuses a formation of readiness step 0 or 1.
        bool OnEnemySoil(float x, float z);
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
        // Foresight F7: marches and retries not made because the formation is below readiness step 2 and the target is ground vanilla refuses it
        // (HeldReadiness), and the ended push's corps stopped where they stand (Stopped; a stop is no move and spends none of the day's cap).
        public int HeldReadiness, Stopped;
        // Of Deferred, the main-effort gatherings at a staged start line that lies in enemy-held ground: they wait for a release that is
        // only rolled once the objective is scouted (smoke 2026-10-17: 22 reviews of deferred=1 with no scout out), as against the
        // scouted march that waits for the chapter roll. The [CampaignMover] row tells them apart.
        public int DeferredStaged;
        // One row per move judged a miss this review (failed or given up): the unit, its role, where it was sent from and to and where its
        // waypoint stands. The tally row counts them; these say which (smoke 2026-10-18: gaveUp=2 with no name). At most MaxMovesPerDay a review.
        public readonly List<string> MissRows = new List<string>();
        // One row per move the mover made this review (outcome Issued, or Retried with the host taking it; a move queued behind a running
        // order and a refusal write none): the unit, its role and reason, where it was sent from and to, and for which objective. Foresight F1:
        // the investigation had to reconstruct who was sent where from geometry. At most MoveTracker.MaxMovesPerDay rows for the moves of a
        // review, since each is a move that spent the day's budget, and one more for each stop (role Hold, reason committed-wait, the formation's
        // own position as the target), which spends none.
        public readonly List<string> IssueRows = new List<string>();
        // The review day the rows are dated with (ReviewCadenceCatchUp.FormatDate).
        public int DaySerial;
        // The tempo roll of this review, when one was evaluated (a new decision's launch).
        public LaunchVerdict Tempo;
        public bool TempoFirstRow, TempoReleaseRow;
        // A kept main effort with no formation the mover can send (first review of the state; see Review). After G3 leaves out the
        // formations that gave up this state should not occur; the row is how a smoke run would see it.
        public bool MainUndispatchableRow;
        public int TempoDecision, TempoMainObjective, TempoChapter;

        public bool Any => Issued + Confirmed + Failed + Retried + GaveUp + Queued + SkippedPlayer + SkippedBusy + Deferred + HeldReadiness + Stopped > 0;

        // [CampaignMover] once per review when any count > 0.
        public string Row(int alliance)
        {
            if (!Any) return "";
            return ReviewCadenceCatchUp.Dated("[CampaignMover] alliance=" + alliance + " issued=" + Issued + " confirmed=" + Confirmed + " failed=" + Failed
                + " retried=" + Retried + " gaveUp=" + GaveUp + " queued=" + Queued + " skippedPlayer=" + SkippedPlayer
                + " skippedBusy=" + SkippedBusy + " deferred=" + Deferred + " deferredStaged=" + DeferredStaged + " heldReadiness=" + HeldReadiness
                + " stopped=" + Stopped, DaySerial);
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
    // MainEffort / Support / CapitalGuard / Garrison / Redeploy / Recover / DepotBuilder assignment whose formation is not where its role needs it
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
    //   - foresight F7 (D11 requirement-commitment-mover): a MainEffort, Support or Redeploy formation below readiness step 2 is sent nowhere onto
    //     ground vanilla would refuse it (SetWaypoint 91336-91350), the stop of an ended push's corps (Hold, committed-wait) is vanilla's own
    //     campaign stop and spends none of the day's moves, and the corps of a commitment that ended, and the capital guards, leave vanilla's
    //     offensive list. Enable Main Effort Commitment is the rollback of all four.
    // The W&L player: the player's own command, a CIC side and the map-group player's group are never moved or ordered (the
    // allocator gives the map-group player's group no role the mover sends: it is never mass, and outside the mass a formation holds,
    // smoke amendments 2026-10-16 D3); the AI group above a player below map level is moved as any AI formation (stage F's
    // MoveUnitTo Postfix dispatches the march; the player it carries is sent no order).
    internal static class CampaignMover
    {
        // Within this of the target, or a final waypoint this close to it, the formation is there or already underway.
        public const float ArrivalRadius = 10f;

        // One step of the order the roles are served in under the daily cap: a role, and the reason it must carry (null: any). Home defence
        // goes first; a covering member before the main effort, a department's reinforcement after the garrisons (D11 requirement-redeploy-role).
        // A Redeploy formation is sent with no tempo release: it marches over own or neutral ground, like a capital guard.
        private readonly struct PriorityStep
        {
            public readonly FormationRole Role;
            public readonly string Reason;
            public PriorityStep(FormationRole role, string reason) { Role = role; Reason = reason; }
        }

        private static readonly PriorityStep[] Priority =
        {
            new PriorityStep(FormationRole.CapitalGuard, null),
            new PriorityStep(FormationRole.Hold, ForceAllocator.ReasonCommittedWait),   // the stop of an ended push's corps (spends none of the cap)
            new PriorityStep(FormationRole.Redeploy, ForceAllocator.ReasonCoveringForce),
            new PriorityStep(FormationRole.MainEffort, null),
            new PriorityStep(FormationRole.Garrison, null),
            new PriorityStep(FormationRole.Redeploy, ForceAllocator.ReasonDepartmentReinforcement),
            new PriorityStep(FormationRole.Recover, null),
            new PriorityStep(FormationRole.DepotBuilder, null),
            new PriorityStep(FormationRole.Support, null),
        };

        // A published reason is compared ordinally, here and wherever else it is tested.
        private static bool Serves(in PriorityStep step, in Assignment a)
        {
            return a.Role == step.Role && (step.Reason == null || string.Equals(a.Reason, step.Reason, StringComparison.Ordinal));
        }

        // Does the mover send a formation of this role? (The roles G4 can give a formation that G5d's recon executor must then leave
        // to the mover: one owner per formation per review.) A Hold is never one, a committed-wait Hold included: a push that ended is
        // stopped, not sent, so the stop's step in Priority is not a march and a stop never gives up into a hold.
        internal static bool MovesRole(FormationRole role)
        {
            for (int k = 0; k < Priority.Length; k++)
                if (Priority[k].Role == role && role != FormationRole.Hold) return true;
            return false;
        }

        public static MoverTally Review(MoverInputs inputs, IMoverHost host, MoveTracker tracker, PlannerState state)
        {
            var tally = new MoverTally();
            if (inputs == null || host == null || tracker == null || state == null) return tally;
            var s = inputs.Snapshot;
            var plan = inputs.Plan;
            var allocation = inputs.Allocation;
            if (s == null || plan == null || allocation == null) return tally;
            tally.DaySerial = s.DaySerial;

            JudgeLastReview(s, allocation, tracker, tally);
            ReleaseFromOffense(s, plan, allocation, host);
            bool released = state.TempoReleasedDecision == state.DecisionSerial;
            bool dispatchable = plan.MainObjectiveId >= 0 && HasDispatchableMainEffort(s, allocation, host);
            NoteUndispatchable(inputs, state, tally, dispatchable);
            // D1: a staged (unscouted) main effort is only gathering at its start line; the tempo roll is the march's, so it waits
            // until the picture is scouted and the target is the objective.
            if (!released && dispatchable && !plan.MainStaged) released = EvaluateLaunch(inputs, host, state, tally);
            foreach (var step in Priority)
                for (int k = 0; k < allocation.Assignments.Count; k++)
                    if (Serves(in step, allocation.Assignments[k]))
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
                // A stop is judged by whether the formation still moves, so an order running is its answer and not a reason to wait.
                if (!tracker.InTransit(f.InstanceId) || (orderRunning && !tracker.IsStop(f.InstanceId))) continue;
                switch (tracker.Judge(f.InstanceId, f.X, f.Z, f.WaypointX, f.WaypointZ, s.DaySerial, moving: (f.Flags & (FormationFlags.HasPath | FormationFlags.MoveQueued)) != 0))
                {
                    case MoveOutcome.Confirmed: tally.Confirmed++; break;
                    case MoveOutcome.Failed: tally.Failed++; tally.MissRows.Add(MissRow(s, allocation, tracker, f, "failed")); break;
                    case MoveOutcome.GaveUp: tally.GaveUp++; tally.MissRows.Add(MissRow(s, allocation, tracker, f, "gaveUp")); break;
                }
            }
        }

        private static string MissRow(CampaignSnapshot s, Allocation allocation, MoveTracker tracker, in FormationRecord f, string outcome)
        {
            var inv = CultureInfo.InvariantCulture;
            tracker.TryGetMove(f.InstanceId, out float fromX, out float fromZ, out float tx, out float tz, out int issued, out int retries, out int objective, out string role);
            return ReviewCadenceCatchUp.Dated("[CampaignMoverMiss] alliance=" + s.Alliance + " unit=" + TelemetryLegacyParser.Quote(f.UnitKey ?? f.InstanceId.ToString(inv)) + " role=" + (role ?? "-") + " outcome=" + outcome
                + " target=" + tx.ToString("0", inv) + "," + tz.ToString("0", inv) + " objective=" + objective + " retries=" + retries
                + " from=" + fromX.ToString("0", inv) + "," + fromZ.ToString("0", inv) + " at=" + f.X.ToString("0", inv) + "," + f.Z.ToString("0", inv)
                + " waypoint=" + f.WaypointX.ToString("0", inv) + "," + f.WaypointZ.ToString("0", inv) + " days=" + (s.DaySerial - issued)
                + " strength=" + f.Strength.ToString("0", inv) + " readiness=" + f.ReadinessStep, s.DaySerial);
        }

        private static bool RoleStillSends(Allocation allocation, int instanceId)
        {
            return allocation.TryGet(instanceId, out var a) && MovesRole(a.Role);
        }

        // Vanilla never checks supply before or during an offensive (14080-14158 has no supply term, job 6's gate is dead,
        // 14009-14019): a Recover formation leaves unitsinoffensiveoperations as RelieveUnitsInOperations does for a retreating
        // or low-morale one (14068-14078), and is then moved toward supply. A Redeploy formation leaves it too: it is marching to a hold
        // point, and vanilla's offensive must not gather it on the way (D11 requirement-redeploy-role); so does a covering member holding its
        // post (a Hold, reason covering-force: it covers the road and is no push's, D11 requirement-covering-hold). Only a formation the planner
        // may move. Foresight F7 adds a capital guard (vanilla's job 6, 13968-14061, re-targets every idle listed unit to an enemy objective of
        // its area and reads no availability gate) and, when a commitment ended, each formation of TheaterPlan.CommitmentEnded still in the
        // list unless it is a MainEffort or Support again (D11 requirement-commitment-mover): the mover listed it, or vanilla's job 4 did
        // before Whiskey sent it, and Whiskey's order superseded vanilla's. Those two are Enable Main Effort Commitment's.
        private static void ReleaseFromOffense(CampaignSnapshot s, TheaterPlan plan, Allocation allocation, IMoverHost host)
        {
            bool commitment = PlannerSettings.MainEffortCommitment;
            for (int k = 0; k < allocation.Assignments.Count; k++)
            {
                var a = allocation.Assignments[k];
                bool released = a.Role == FormationRole.Recover || a.Role == FormationRole.Redeploy || ForceAllocator.IsCoveringMember(in a)
                    || (commitment && (a.Role == FormationRole.CapitalGuard || EndedAndNotPushingAgain(plan, in a)));
                if (!released) continue;
                int i = s.IndexOfOwn(a.InstanceId);
                if (i < 0 || !WlChainRules.MayMove(s.Own[i].Chain)) continue;
                if ((s.Own[i].Flags & FormationFlags.InOffensiveOps) != 0) host.ListOffensive(a.InstanceId, false);
            }
        }

        private static bool EndedAndNotPushingAgain(TheaterPlan plan, in Assignment a)
        {
            return a.Role != FormationRole.MainEffort && a.Role != FormationRole.Support && plan.CommitmentEnded.Contains(a.InstanceId);
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
            // The stop of an ended push's corps (Hold, committed-wait): not a march, so neither the arrival test, the readiness skip, the tempo gate
            // nor the tracker's ShouldIssue and the day's cap apply to it.
            if (a.Role == FormationRole.Hold)
            {
                StopWhereItStands(s, host, tracker, in a, in f, tally);
                return;
            }
            if (Distance(f.WaypointX, f.WaypointZ, tx, tz) <= ArrivalRadius) return;
            // Vanilla refuses the march of a formation at readiness step 0 or 1 onto enemy ground, in silence (SetWaypoint 91336-91350), so a
            // new march or a retry there would be a wasted one that the tracker counts as a miss. It is held; the record keeps its retry for the
            // formation's return to step 2, and low readiness alone never gives up (D11 requirement-commitment-mover).
            if (PlannerSettings.MainEffortCommitment && RefusedForReadiness(host, in a, in f, tx, tz))
            {
                tally.HeldReadiness++;
                return;
            }
            // The launch gates a march into enemy-held ground. The main effort's gathering at a staged start line is not one while
            // the line is in own or neutral ground; a start line that happens to lie in enemy ground still waits for the release.
            bool marches = a.Role == FormationRole.MainEffort ? !inputs.Plan.MainStaged || EnemyHeld(s, host, tx, tz)
                : a.Role == FormationRole.Support && EnemyHeld(s, host, tx, tz);
            if (!released && marches)
            {
                tally.Deferred++;
                if (a.Role == FormationRole.MainEffort && inputs.Plan.MainStaged) tally.DeferredStaged++;
                return;
            }
            if (!tracker.ShouldIssue(f.InstanceId, tx, tz, s.DaySerial)) return;

            int objective = ObjectiveOf(inputs.Plan, a);
            bool moved = MoveIssue.Issue(host, tracker, in f, tx, tz, objective, s.DaySerial, inputs.WlPlayerAlliance, MoveBudget.Planner, a.Role.ToString(), out var outcome);
            Count(tally, outcome);
            if (moved) tally.IssueRows.Add(IssueRow(s, in f, a, tx, tz, objective));
            if (moved && (a.Role == FormationRole.MainEffort || a.Role == FormationRole.Support)) host.ListOffensive(f.InstanceId, true);
        }

        // Whether vanilla would refuse this march in silence: a MainEffort, Support or Redeploy formation below readiness step 2, the target on
        // enemy soil as vanilla's IsOnEnemySoil judges it.
        private static bool RefusedForReadiness(IMoverHost host, in Assignment a, in FormationRecord f, float tx, float tz)
        {
            if (f.ReadinessStep >= TheaterPlanner.MinReadinessStep) return false;
            if (a.Role != FormationRole.MainEffort && a.Role != FormationRole.Support && a.Role != FormationRole.Redeploy) return false;
            return host.OnEnemySoil(tx, tz);
        }

        // Vanilla's campaign stop, sent only to a formation the snapshot shows still marching or with an order waiting (vanilla's
        // GetLastTransmittedPath() >= 0 and DefensiveMovingOrder.OrderRunning), so it cannot be sent twice: it clears both. It passes through
        // neither MoveIssue.Issue (there is no target to restrict, and its queued branch records a queued move and calls no host) nor
        // MoveTracker.ShouldIssue (it is not a MoveUnitTo call, which is what the day's cap bounds): the tracker records it at the formation's
        // own position as a stop, judged at the next review by whether the formation still moves.
        private static void StopWhereItStands(CampaignSnapshot s, IMoverHost host, MoveTracker tracker, in Assignment a, in FormationRecord f, MoverTally tally)
        {
            if (!PlannerSettings.MainEffortCommitment) return;
            if ((f.Flags & (FormationFlags.HasPath | FormationFlags.MoveQueued)) == 0) return;
            if (!host.Stop(f.InstanceId)) return;
            tracker.RecordStop(f.InstanceId, f.X, f.Z, s.DaySerial, f.UnitKey);
            tally.Stopped++;
            tally.IssueRows.Add(IssueRow(s, in f, a, f.X, f.Z, -1));
        }

        // [CampaignMoverIssue]: the field names and number formats of [CampaignMoverMiss] (the unit key quoted, coordinates whole numbers).
        private static string IssueRow(CampaignSnapshot s, in FormationRecord f, Assignment a, float tx, float tz, int objective)
        {
            var inv = CultureInfo.InvariantCulture;
            float fromX = f.X, fromZ = f.Z;   // coordinates: the quote sweep exempts from=<fromX>, which is a place, not a name
            return ReviewCadenceCatchUp.Dated("[CampaignMoverIssue] alliance=" + s.Alliance, s.DaySerial)
                + " unit=" + TelemetryLegacyParser.Quote(f.UnitKey ?? f.InstanceId.ToString(inv)) + " role=" + a.Role + " reason=" + (a.Reason ?? "-")
                + " target=" + tx.ToString("0", inv) + "," + tz.ToString("0", inv) + " objective=" + objective
                + " from=" + fromX.ToString("0", inv) + "," + fromZ.ToString("0", inv) + " readiness=" + f.ReadinessStep + " pick=" + ForceAllocator.PickOf(a);
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
