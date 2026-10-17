using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // Everything one alliance's planner keeps across a save (sidecar v3, FactionDto.planner), as plain data: the
    // hysteresis and seed state, the telemetry run of the map-group player's unreliable days, the belief picture, the
    // appointment protection windows and the four ledgers that name a formation (the mover's tracked moves, the depot
    // task ledger, the recon missions, the garrison posts). Every reference to a formation is its UnitKey (name:commander), never a Unity
    // instance id (those change on every load); PlannerRebind turns the keys back into this session's ids on the first
    // review after a load. A null PlannerSnapshot is a sidecar without planner state (v1/v2): the planner starts fresh.
    public sealed class PlannerSnapshot
    {
        public PlannerState State = new PlannerState();
        public int UnreliableDays;
        public int UnreliableLastDay = int.MinValue;
        public readonly List<EnemyBelief> Beliefs = new List<EnemyBelief>();
        public readonly List<KeyValuePair<int, int>> Appointments = new List<KeyValuePair<int, int>>();
        public readonly List<DepotTaskRecord> DepotTasks = new List<DepotTaskRecord>();
        public readonly List<MoveRecord> Moves = new List<MoveRecord>();
        public readonly List<ReconMission> ReconMissions = new List<ReconMission>();
        public readonly List<GarrisonPostRecord> GarrisonPosts = new List<GarrisonPostRecord>();
    }

    // The first review after a load: the saved formation references (unit keys) become this session's formations.
    // Resolution is the snapshot's IndexOfKeyOrName (the exact key, else the one formation of that name when the commander
    // part no longer matches). A move or a recon mission whose formation is gone is dropped (the scout's stand-down debt
    // with it: there is no screen left to stand down); a depot task keeps its record, with its builder bound at once (the
    // allocation pins a held builder before the ledger's Sync runs), and the depot ledger's own Sync abandons an active
    // one whose builder is gone, so the site still cools down.
    public static class PlannerRebind
    {
        public struct Result
        {
            public int MovesKept, MovesDropped, MissionsKept, MissionsDropped, DepotTasksRebound;
        }

        public static Result Resolve(PlannerSnapshot saved, CampaignSnapshot s,
            List<MoveRecord> moves, List<DepotTaskRecord> depotTasks, List<ReconMission> missions)
        {
            var result = new Result();
            moves.Clear();
            depotTasks.Clear();
            missions.Clear();
            if (saved == null || s == null) return result;

            for (int i = 0; i < saved.Moves.Count; i++)
            {
                var move = saved.Moves[i];
                int own = s.IndexOfKeyOrName(move.UnitKey);
                if (own < 0) { result.MovesDropped++; continue; }
                move.InstanceId = s.Own[own].InstanceId;
                move.UnitKey = s.Own[own].UnitKey;
                moves.Add(move);
                result.MovesKept++;
            }

            for (int i = 0; i < saved.DepotTasks.Count; i++)
            {
                var task = saved.DepotTasks[i];
                int own = s.IndexOfKeyOrName(task.BuilderUnitKey);
                if (own >= 0)
                {
                    task.BuilderInstanceId = s.Own[own].InstanceId; // pinned from this review on: the allocation reads it before the ledger's Sync
                    task.BuilderUnitKey = s.Own[own].UnitKey;
                    result.DepotTasksRebound++;
                }
                depotTasks.Add(task);
            }

            for (int i = 0; i < saved.ReconMissions.Count; i++)
            {
                var mission = saved.ReconMissions[i];
                int own = s.IndexOfKeyOrName(mission.UnitKey);
                if (own < 0) { result.MissionsDropped++; continue; }
                mission.InstanceId = s.Own[own].InstanceId;
                mission.UnitKey = s.Own[own].UnitKey;
                missions.Add(mission);
                result.MissionsKept++;
            }
            return result;
        }
    }
}
