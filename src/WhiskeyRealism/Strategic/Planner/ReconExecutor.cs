using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // The vanilla actions only the reconnaissance executor performs (plan G5d), on top of the move a mover also makes.
    internal interface IReconHost : IMoveHost
    {
        // Stage F's chain class of the formation (WlStrategicOrderBridge.ChainClassOf), read again before any act: the day's
        // snapshot is hours old and the player may have taken the group since.
        WlChainClass LiveChain(int instanceId);
        // BattleUnits.ChangeCampaignOrders(unit, orderCode) (90720-90770), the player's own order path.
        CavalrySend SendCavalryOrder(int instanceId, int orderCode);
    }

    // What the host did about a cavalry order: made the call, found the wanted mode already applied or on its way, found a different
    // mode waiting in the queue (vanilla cannot replace it; ReconOrders.CallFor), found no such unit, or could not reach BattleUnits.
    // NoBattleUnits: the campaign map's BattleUnits could not be found (a transient state), so nothing is known about the unit: the
    // stand-down it owes is kept. NoUnit: the group is gone, so nothing is owed to it.
    internal enum CavalrySend { NoUnit = 0, Sent = 1, AlreadySet = 2, QueuedOther = 3, NoBattleUnits = 4 }

    internal sealed class ReconTally
    {
        public int Issued, Retried, Failed, Queued, SkippedPlayer, SkippedBusy, Ordered;
        // One `[ReconMission]` row per mission whose state or reason changed since the last review.
        public readonly List<string> Rows = new List<string>();

        public bool Any => Issued + Retried + Failed + Queued + SkippedPlayer + SkippedBusy + Ordered > 0;

        // `[ReconMission]` once per review that did anything, the executor's own counts.
        public string Summary(int alliance)
        {
            return "[ReconMission] alliance=" + alliance + " issued=" + Issued + " retried=" + Retried + " failed=" + Failed + " queued=" + Queued
                + " ordered=" + Ordered + " skippedPlayer=" + SkippedPlayer + " skippedBusy=" + SkippedBusy;
        }
    }

    // The reconnaissance executor (plan G5d): the daily review's step that carries out the missions ReconMissionLedger holds,
    // pure over the host. Per mission, for a formation the planner may act on:
    //   - moves it to the mission's hold point (the recall point for a Recalled one) through the shared MoveIssue, on the
    //     recon budget (MoveTracker.MaxReconMovesPerDay), a recalled formation first; a Holding mission is not moved, and a
    //     formation whose last waypoint is already the post is left to walk;
    //   - gives a cavalry-majority Screen the Scouting order (121) and takes it off (120) when it is recalled, cleared or no
    //     longer a screen (ReconOrders.CavalryOrderFor: never on a group in the W&L player's chain), one call per formation
    //     however many missions it carries (the list holds a cleared mission for the one review that takes the order off);
    //   - logs a `[ReconMission]` row when a mission's state or reason changes.
    // The player's own command and a CIC side are never acted on (ReconOrders.MayAct, re-read live), and the map-group player
    // is order-only, which recon never gives him: G4 never picks him, and if the chain changed since the pick he is left alone.
    // Fog-honest: contact was judged by the ledger from beliefs and the battle flag; nothing here reads an enemy destination.
    internal sealed class ReconExecutor
    {
        private struct Seen
        {
            public ReconState State;
            public string Reason;
        }

        // Per mission id: what its last row said. Pruned of missions the ledger no longer holds.
        private readonly Dictionary<int, Seen> _logged = new Dictionary<int, Seen>();
        private readonly HashSet<int> _present = new HashSet<int>();
        private readonly List<int> _gone = new List<int>();
        private readonly List<WlChainClass> _chains = new List<WlChainClass>();
        private readonly List<bool> _acts = new List<bool>();
        private readonly List<int> _transit = new List<int>();

        public ReconTally Review(CampaignSnapshot s, ReconMissionLedger ledger, IReconHost host, MoveTracker tracker, bool wlPlayerAlliance)
        {
            var tally = new ReconTally();
            if (s == null || ledger == null || host == null || tracker == null) return tally;
            var missions = ledger.Missions;
            SettleQueuedMoves(s, ledger, tracker);
            NoteTransitions(s, missions, tally);
            ReadChains(s, ledger, host, tally);
            SendCavalryOrders(s, ledger, host, tally);
            Move(s, missions, host, tracker, wlPlayerAlliance, tally);
            return tally;
        }

        // A recon move queued behind a running order settles through MoveTracker.SettleQueued when that order has ended (the mover's
        // pass settles it first when it runs; this pass does not rely on it) or when no mission wants the formation any more, so it
        // is neither in transit nor withheld from vanilla for good (verify r2 R1). A live mission re-sends it below.
        private void SettleQueuedMoves(CampaignSnapshot s, ReconMissionLedger ledger, MoveTracker tracker)
        {
            tracker.SettleExpiredQueued(s.DaySerial);
            tracker.CollectInTransit(_transit);
            for (int k = 0; k < _transit.Count; k++)
            {
                int id = _transit[k];
                int i = s.IndexOfOwn(id);
                bool orderRunning = i >= 0 && (s.Own[i].Flags & FormationFlags.MoveQueued) != 0;
                tracker.SettleQueued(id, MoveBudget.Recon, ledger.IsRecon(id) || ledger.IsRecalling(id), orderRunning);
            }
        }

        private void NoteTransitions(CampaignSnapshot s, IReadOnlyList<ReconMission> missions, ReconTally tally)
        {
            _present.Clear();
            for (int i = 0; i < missions.Count; i++)
            {
                var m = missions[i];
                _present.Add(m.MissionId);
                if (_logged.TryGetValue(m.MissionId, out var last) && last.State == m.State && string.Equals(last.Reason, m.Reason, StringComparison.Ordinal)) continue;
                _logged[m.MissionId] = new Seen { State = m.State, Reason = m.Reason };
                int own = s.IndexOfOwn(m.InstanceId);
                string unit = own >= 0 ? s.Own[own].Name : m.UnitKey;
                tally.Rows.Add("[ReconMission] alliance=" + s.Alliance + " unit=" + unit + " kind=" + m.Kind + " state=" + m.State + " reason=" + (m.Reason ?? "-"));
            }
            _gone.Clear();
            foreach (var id in _logged.Keys)
                if (!_present.Contains(id)) _gone.Add(id);
            for (int i = 0; i < _gone.Count; i++) _logged.Remove(_gone[i]);
        }

        // The live chain class of each mission's formation and whether recon may act on it. Counted once per mission.
        private void ReadChains(CampaignSnapshot s, ReconMissionLedger ledger, IReconHost host, ReconTally tally)
        {
            var missions = ledger.Missions;
            _chains.Clear();
            _acts.Clear();
            for (int i = 0; i < missions.Count; i++)
            {
                var chain = host.LiveChain(missions[i].InstanceId);
                bool present = s.IndexOfOwn(missions[i].InstanceId) >= 0;
                // A Scouting order that can no longer be taken off is let go: the formation is gone, or its chain is not None any
                // more and order 120 at level -1 would reach the player's units (92572-92620). It stays until a readiness reset
                // (113960).
                if (missions[i].StandDownDue && (!present || chain != WlChainClass.None)) ledger.NoteStoodDown(missions[i].InstanceId);
                bool acts = present && ReconOrders.MayAct(chain) && !WlChainRules.OrderOnly(chain);
                if (present && !acts) tally.SkippedPlayer++;
                _chains.Add(chain);
                _acts.Add(acts);
            }
        }

        // One call per formation: the strongest order any of its missions asks for (121 beats 120 beats no call), so a re-targeted
        // screen keeps Scouting beside its own cleared mission and a re-tasked one loses it.
        private void SendCavalryOrders(CampaignSnapshot s, ReconMissionLedger ledger, IReconHost host, ReconTally tally)
        {
            var missions = ledger.Missions;
            for (int i = 0; i < missions.Count; i++)
            {
                if (!_acts[i] || FirstMissionOf(missions, missions[i].InstanceId) != i) continue;
                int order = -1;
                for (int j = i; j < missions.Count; j++)
                {
                    if (!_acts[j] || missions[j].InstanceId != missions[i].InstanceId) continue;
                    var f = s.Own[s.IndexOfOwn(missions[j].InstanceId)];
                    order = Math.Max(order, ReconOrders.CavalryOrderFor(missions[j].Kind, missions[j].State, f.CavalryShare, _chains[j]));
                }
                if (order < 0) continue;
                int id = missions[i].InstanceId;
                var sent = host.SendCavalryOrder(id, order);
                if (sent == CavalrySend.Sent) tally.Ordered++;
                bool set = sent == CavalrySend.Sent || sent == CavalrySend.AlreadySet;
                // Scouting given: the mission owes the stand-down. Stand-down delivered (or no formation to deliver it to): it is paid.
                if (order == ReconOrders.ScoutingOrder && set) ledger.NoteScouting(id);
                else if (order == ReconOrders.NoCavalryOrder && (set || sent == CavalrySend.NoUnit)) ledger.NoteStoodDown(id);
            }
        }

        private static int FirstMissionOf(IReadOnlyList<ReconMission> missions, int instanceId)
        {
            for (int i = 0; i < missions.Count; i++)
                if (missions[i].InstanceId == instanceId) return i;
            return -1;
        }

        // Recalled formations first (they are leaving contact or running out of supply), then the rest in the ledger's order,
        // which is the order the allocator took the tasks (highest priority first).
        private void Move(CampaignSnapshot s, IReadOnlyList<ReconMission> missions, IReconHost host, MoveTracker tracker, bool wlPlayerAlliance, ReconTally tally)
        {
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < missions.Count; i++)
                {
                    var m = missions[i];
                    if (!_acts[i] || (m.State == ReconState.Recalled) != (pass == 0)) continue;
                    if (m.State != ReconState.Assigned && m.State != ReconState.Moving && m.State != ReconState.Recalled) continue;
                    MoveToPost(s, m, s.Own[s.IndexOfOwn(m.InstanceId)], host, tracker, wlPlayerAlliance, tally);
                }
        }

        private static void MoveToPost(CampaignSnapshot s, in ReconMission m, in FormationRecord f, IReconHost host, MoveTracker tracker, bool wlPlayerAlliance, ReconTally tally)
        {
            // One owner per formation per review: a formation in transit now was sent today by the mover (or is a recon move still
            // waiting on its order, or one not yet judged), and a second move would overwrite its record. A queued record whose order
            // has ended or whose mission is over was settled above (and by the mover's pass), so it does not stand here for good.
            if (!TheaterPlanner.Movable(f) || host.PackageLocked(f.InstanceId) || tracker.InTransit(f.InstanceId))
            {
                tally.SkippedBusy++;
                return;
            }
            if (Distance(f.WaypointX, f.WaypointZ, m.HoldX, m.HoldZ) <= CampaignMover.ArrivalRadius) return;
            if (!tracker.ShouldIssue(f.InstanceId, m.HoldX, m.HoldZ, s.DaySerial, MoveBudget.Recon)) return;
            MoveIssue.Issue(host, tracker, in f, m.HoldX, m.HoldZ, -1, s.DaySerial, wlPlayerAlliance, MoveBudget.Recon, out var outcome);
            switch (outcome)
            {
                case MoveOutcome.Issued: tally.Issued++; break;
                case MoveOutcome.Retried: tally.Retried++; break;
                case MoveOutcome.Queued: tally.Queued++; break;
                case MoveOutcome.Failed: tally.Failed++; break;
            }
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
