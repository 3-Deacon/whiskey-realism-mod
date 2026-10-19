namespace WhiskeyRealism.Strategic.Planner
{
    // The vanilla side of issuing one planner move, shared by the mover (G5a) and the reconnaissance executor (G5d): the two
    // passes decide who goes where, this is how one formation is sent. Implemented by the runtimes over vanilla and by the
    // harness with a recording fake.
    internal interface IMoveHost
    {
        // BattlefieldSetup.IsTerrainPositionRestricted (25755): a target no final waypoint can be put on.
        bool TargetRestricted(float x, float z);
        // AICampaign.MoveUnitTo(unit, point, movearoundenemyunits: true): true when vanilla took the move.
        bool Move(int instanceId, float x, float z);
        // userails = userivers = true on the group: vanilla's AI default (112862-112867).
        void RaiseTransportModes(int instanceId);
        // Vanilla's own campaign stop on the formation's regiment (foresight F7, D11 requirement-commitment-mover): StopRegiment, then the removal
        // of every order waiting for it in AICampaign's queue, as the port operation of CheckFleetMovements does (13350-13351). No move is made.
        // False when the formation is not one of the reviewing alliance's groups.
        bool Stop(int instanceId);
    }

    // One move through the tracker (P8): a retry after a failed attempt at the same target is reported as Retried, a move queued
    // behind a running order as Queued (it spends none of the budget), a refusal (restricted ground, or vanilla refused the
    // target) as a miss without the waiting. The caller has asked MoveTracker.ShouldIssue for the same budget.
    internal static class MoveIssue
    {
        // Returns true when vanilla took the move now (not queued, not refused). `outcome` is one of Issued, Retried, Queued,
        // Failed: Retried also covers a refused retry.
        internal static bool Issue(IMoveHost host, MoveTracker tracker, in FormationRecord f, float tx, float tz, int objectiveId,
                                   int daySerial, bool raiseTransportModes, MoveBudget budget, string role, out MoveOutcome outcome)
        {
            bool queued = (f.Flags & FormationFlags.MoveQueued) != 0;
            bool retry = tracker.IssueOutcome(f.InstanceId, tx, tz, queued) == MoveOutcome.Retried;
            if (host.TargetRestricted(tx, tz))
            {
                tracker.RecordRefused(f.InstanceId, tx, tz, daySerial, f.UnitKey, objectiveId, budget, role);
                outcome = retry ? MoveOutcome.Retried : MoveOutcome.Failed;
                return false;
            }
            if (raiseTransportModes && (!f.UseRails || !f.UseRivers)) host.RaiseTransportModes(f.InstanceId);
            if (queued)
            {
                tracker.RecordIssued(f.InstanceId, f.X, f.Z, tx, tz, daySerial, queued: true, unitKey: f.UnitKey, objectiveId: objectiveId, budget: budget, role: role);
                outcome = MoveOutcome.Queued;
                return false;
            }
            if (!host.Move(f.InstanceId, tx, tz))
            {
                tracker.RecordRefused(f.InstanceId, tx, tz, daySerial, f.UnitKey, objectiveId, budget, role);
                outcome = retry ? MoveOutcome.Retried : MoveOutcome.Failed;
                return false;
            }
            tracker.RecordIssued(f.InstanceId, f.X, f.Z, tx, tz, daySerial, queued: false, unitKey: f.UnitKey, objectiveId: objectiveId, budget: budget, role: role);
            outcome = retry ? MoveOutcome.Retried : MoveOutcome.Issued;
            return true;
        }
    }
}
