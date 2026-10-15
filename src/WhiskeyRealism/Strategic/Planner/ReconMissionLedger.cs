using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    public enum ReconState
    {
        Assigned = 0, // new or re-targeted; no move under way yet
        Moving = 1,   // a path or a queued MoveUnitTo order (FormationFlags.HasPath / MoveQueued)
        Holding = 2,  // at its hold point
        Recalled = 3, // going back to the main body (contact, low supply, probe withdraw)
        Cleared = 4   // finished; dropped at the next review
    }

    public struct ReconMission
    {
        public int MissionId;
        public ReconKind Kind;
        public int InstanceId;   // in-session
        public string UnitKey;   // persisted; rebound by IndexOfKey after a load
        public float HoldX, HoldZ;
        public int SectorId;
        public ReconState State;
        public int IssuedDay;
        public int LastContactDay; // -1 never
        public string Reason;
        // The confidence-weighted enemy strength near the objective at the probe's last verdict (the
        // reference for the enemy-reaction test); -1 before the first look. Probes only.
        public float ObservedEnemyStrength;
    }

    // One alliance's recon missions (plan § G1b), written only inside the daily review: OnReview
    // ages and transitions the missions from today's snapshot (before the planner picks tasks),
    // Sync binds G4's Recon assignments to missions (after the allocation) and writes the mission id
    // back into each assignment for the G5d executor. Per review: OnReview -> ReconPlanner.PlanTasks
    // -> ForceAllocator (PickFormation) -> Sync.
    //   Assigned/Moving/Holding  derived each review from the formation: at its hold point (within
    //                            max(bugle range, a quarter sight radius)) Holding, else a path or a
    //                            queued move Moving, else Assigned (the executor re-issues).
    //   -> Recalled  contact: in battle, or a belief seen today within bugle range (an advance guard:
    //                within the standoff of 0.8 x sight); raw Supply01 < 0.5 (vanilla's raid abort,
    //                GetGroupSupplyState < 0.25 at 9092, is on the halved metric of 14063-14066); a
    //                probe's ContactEvidenceLedger verdict to withdraw. A probe's own contact only
    //                stamps LastContactDay: contact is its job, and the verdict decides.
    //                The hold point becomes the recall point: the nearest own formation the
    //                allocation holds with (Hold or MainEffort), else the own capital, else the
    //                nearest own formation; never the W&L player's own command or a map-group
    //                player (WlChainRules.CountsAsCommittedDefender, C4).
    //   -> Cleared   formation missing; retreating (vanilla owns the move); MaxMissionDays after it
    //                was issued (the planner re-plans it); a recalled group back at its recall point;
    //                its Recon assignment gone or re-tasked to another kind; a probe's verdict to
    //                escalate (the planner's balance uses the fresh belief).
    // Contact is judged from beliefs and the battle flag only, never from vanilla's omniscient
    // closestenemyunitfar* or an enemy destination (which vanilla's raid abort reads, 9099).
    public sealed class ReconMissionLedger
    {
        public const int MaxMissionDays = 20;
        internal const float RecallSupplyBelow = 0.5f; // raw Supply01
        private const float ArrivalSightFraction = 0.25f;

        private readonly List<ReconMission> _missions = new List<ReconMission>();
        private readonly List<int> _kept = new List<int>(); // Sync's scratch, so a steady review allocates nothing
        private int _nextMissionId = 1;

        public IReadOnlyList<ReconMission> Missions => _missions;

        public void Sync(CampaignSnapshot s, IReadOnlyList<ReconTask> tasks, Allocation a)
        {
            if (s == null || a == null) return;
            var kept = _kept;
            kept.Clear();
            for (int i = 0; i < a.Assignments.Count; i++)
            {
                var assignment = a.Assignments[i];
                if (assignment.Role != FormationRole.Recon) continue;
                assignment.ReconMissionId = -1;
                int task = TaskFor(tasks, assignment);
                int own = s.IndexOfOwn(assignment.InstanceId);
                if (task >= 0 && own >= 0 && !IsRecalling(assignment.InstanceId))
                    assignment.ReconMissionId = Bind(s, s.Own[own], tasks[task]);
                if (assignment.ReconMissionId >= 0) kept.Add(assignment.ReconMissionId);
                a.Assignments[i] = assignment;
            }
            for (int m = 0; m < _missions.Count; m++)
                if (IsActive(_missions[m].State) && !kept.Contains(_missions[m].MissionId))
                    SetState(m, ReconState.Cleared, "unassigned");
        }

        public void OnReview(CampaignSnapshot s, Allocation a)
        {
            if (s == null) return;
            _missions.RemoveAll(m => m.State == ReconState.Cleared);
            for (int i = 0; i < _missions.Count; i++)
            {
                var m = _missions[i];
                int own = s.IndexOfOwn(m.InstanceId);
                if (own < 0) own = s.IndexOfKey(m.UnitKey);
                if (own < 0)
                {
                    m.State = ReconState.Cleared;
                    m.Reason = "missing";
                    _missions[i] = m;
                    continue;
                }
                var f = s.Own[own];
                m.InstanceId = f.InstanceId;
                _missions[i] = Review(s, a, m, f);
            }
        }

        // The probe's ContactEvidenceLedger verdict (fed by beliefs and battle history).
        // observedEnemyStrength (>= 0) is the strength this verdict was judged on; it becomes the next
        // verdict's reference for the enemy-reaction test.
        internal void OnProbeContact(int instanceId, ContactEvidenceOutput evidence, int daySerial, float observedEnemyStrength = -1f)
        {
            if (evidence == null) return;
            int i = ActiveIndexOf(instanceId);
            if (i < 0 || _missions[i].Kind != ReconKind.Probe) return;
            var m = _missions[i];
            if (observedEnemyStrength >= 0f) m.ObservedEnemyStrength = observedEnemyStrength;
            if (evidence.Evidence != ContactEvidence.NoContact) m.LastContactDay = daySerial;
            if (evidence.AllowsEscalation)
            {
                m.State = ReconState.Cleared;
                m.Reason = "probe-escalate";
            }
            else if (evidence.Evidence == ContactEvidence.OvermatchedContact || evidence.Evidence == ContactEvidence.EnemyReacted)
            {
                m.State = ReconState.Recalled;
                m.Reason = "probe-withdraw";
            }
            _missions[i] = m;
        }

        // On a recon mission (not recalled or cleared): the formation directive's Screen test.
        public bool IsRecon(int instanceId)
        {
            return ActiveIndexOf(instanceId) >= 0;
        }

        internal bool IsRecalling(int instanceId)
        {
            for (int i = 0; i < _missions.Count; i++)
                if (_missions[i].InstanceId == instanceId && _missions[i].State == ReconState.Recalled) return true;
            return false;
        }

        // Sidecar (G6): instance ids are stale after a load; OnReview rebinds them by UnitKey.
        public void Restore(IEnumerable<ReconMission> missions)
        {
            _missions.Clear();
            _nextMissionId = 1;
            if (missions == null) return;
            foreach (var m in missions)
            {
                _missions.Add(m);
                _nextMissionId = Math.Max(_nextMissionId, m.MissionId + 1);
            }
        }

        internal static bool IsActive(ReconState state)
        {
            return state == ReconState.Assigned || state == ReconState.Moving || state == ReconState.Holding;
        }

        private ReconMission Review(CampaignSnapshot s, Allocation a, ReconMission m, in FormationRecord f)
        {
            if (m.State == ReconState.Recalled)
            {
                RecallPoint(s, a, f, out m.HoldX, out m.HoldZ);
                if (Distance(f.X, f.Z, m.HoldX, m.HoldZ) <= ArrivalRadius(f))
                    return Set(m, ReconState.Cleared, "rejoined");
                if (s.DaySerial - m.IssuedDay >= MaxMissionDays)
                    return Set(m, ReconState.Cleared, "expired");
                return m;
            }

            if ((f.Flags & FormationFlags.OnRetreat) != 0) return Set(m, ReconState.Cleared, "retreat");
            if (s.DaySerial - m.IssuedDay >= MaxMissionDays) return Set(m, ReconState.Cleared, "expired");

            if (InContact(s, m.Kind, f))
            {
                m.LastContactDay = s.DaySerial;
                if (m.Kind != ReconKind.Probe) return Recall(s, a, m, f, "contact");
            }
            if (f.Supply01 < RecallSupplyBelow) return Recall(s, a, m, f, "low-supply");

            if (Distance(f.X, f.Z, m.HoldX, m.HoldZ) <= ArrivalRadius(f)) m.State = ReconState.Holding;
            else if ((f.Flags & (FormationFlags.HasPath | FormationFlags.MoveQueued)) != 0) m.State = ReconState.Moving;
            else m.State = ReconState.Assigned;
            return m;
        }

        private static ReconMission Recall(CampaignSnapshot s, Allocation a, ReconMission m, in FormationRecord f, string reason)
        {
            RecallPoint(s, a, f, out m.HoldX, out m.HoldZ);
            return Set(m, ReconState.Recalled, reason);
        }

        private static ReconMission Set(ReconMission m, ReconState state, string reason)
        {
            m.State = state;
            m.Reason = reason;
            return m;
        }

        private static bool InContact(CampaignSnapshot s, ReconKind kind, in FormationRecord f)
        {
            if ((f.Flags & FormationFlags.InBattle) != 0) return true;
            float radius = kind == ReconKind.AdvanceGuard
                ? Math.Max(f.BugleRange, ReconPlanner.StandoffFraction * f.SightRadius)
                : f.BugleRange;
            for (int i = 0; i < s.Beliefs.Count; i++)
            {
                var b = s.Beliefs[i];
                if (b.Confidence01 <= 0f || b.LastSeenDay != s.DaySerial) continue;
                if (Distance(b.X, b.Z, f.X, f.Z) <= radius) return true;
            }
            return false;
        }

        private static void RecallPoint(CampaignSnapshot s, Allocation a, in FormationRecord f, out float x, out float z)
        {
            x = f.X;
            z = f.Z;
            float best = float.MaxValue;
            if (a != null)
            {
                for (int i = 0; i < a.Assignments.Count; i++)
                {
                    var assignment = a.Assignments[i];
                    if (assignment.InstanceId == f.InstanceId) continue;
                    if (assignment.Role != FormationRole.Hold && assignment.Role != FormationRole.MainEffort) continue;
                    int own = s.IndexOfOwn(assignment.InstanceId);
                    if (own < 0 || !WlChainRules.CountsAsCommittedDefender(s.Own[own].Chain)) continue;
                    float d = Distance(s.Own[own].X, s.Own[own].Z, f.X, f.Z);
                    if (d >= best) continue;
                    best = d;
                    x = s.Own[own].X;
                    z = s.Own[own].Z;
                }
            }
            if (best < float.MaxValue) return;
            if (s.HasCapital)
            {
                x = s.CapitalX;
                z = s.CapitalZ;
                return;
            }
            for (int i = 0; i < s.Own.Count; i++)
            {
                if (s.Own[i].InstanceId == f.InstanceId || !WlChainRules.CountsAsCommittedDefender(s.Own[i].Chain)) continue;
                float d = Distance(s.Own[i].X, s.Own[i].Z, f.X, f.Z);
                if (d >= best) continue;
                best = d;
                x = s.Own[i].X;
                z = s.Own[i].Z;
            }
        }

        // G4 copies the task's SectorId and hold point into the Recon assignment (SectorId, TargetX/Z).
        private static int TaskFor(IReadOnlyList<ReconTask> tasks, in Assignment a)
        {
            if (tasks == null) return -1;
            for (int i = 0; i < tasks.Count; i++)
                if (tasks[i].SectorId == a.SectorId && tasks[i].HoldX == a.TargetX && tasks[i].HoldZ == a.TargetZ) return i;
            return -1;
        }

        // Keeps the formation's active mission when the kind matches (re-targeted in place), else
        // clears it and opens a new one.
        private int Bind(CampaignSnapshot s, in FormationRecord f, in ReconTask task)
        {
            int i = ActiveIndexOf(f.InstanceId);
            if (i >= 0 && _missions[i].Kind == task.Kind)
            {
                var m = _missions[i];
                m.HoldX = task.HoldX;
                m.HoldZ = task.HoldZ;
                m.SectorId = task.SectorId;
                m.Reason = task.Reason;
                _missions[i] = m;
                return m.MissionId;
            }
            if (i >= 0) SetState(i, ReconState.Cleared, "re-tasked");
            var mission = new ReconMission
            {
                MissionId = _nextMissionId++,
                Kind = task.Kind,
                InstanceId = f.InstanceId,
                UnitKey = f.UnitKey,
                HoldX = task.HoldX,
                HoldZ = task.HoldZ,
                SectorId = task.SectorId,
                State = ReconState.Assigned,
                IssuedDay = s.DaySerial,
                LastContactDay = -1,
                Reason = task.Reason,
                ObservedEnemyStrength = -1f
            };
            _missions.Add(mission);
            return mission.MissionId;
        }

        private int ActiveIndexOf(int instanceId)
        {
            for (int i = 0; i < _missions.Count; i++)
                if (_missions[i].InstanceId == instanceId && IsActive(_missions[i].State)) return i;
            return -1;
        }

        private void SetState(int index, ReconState state, string reason)
        {
            _missions[index] = Set(_missions[index], state, reason);
        }

        private static float ArrivalRadius(in FormationRecord f)
        {
            return Math.Max(f.BugleRange, ArrivalSightFraction * f.SightRadius);
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
