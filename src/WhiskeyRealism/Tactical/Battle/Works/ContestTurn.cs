using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>What the posture producer knows of one group at its contest turn (plain data, read from the snapshot and the executor's memory).</summary>
    public struct ContestTurnFacts
    {
        public int Alliance, GroupId, Stance;
        public long Tick;
        public bool Held, Recon, HasPath, HasLane, CloseEngaged, HasLastIssued;
        /// <summary>The group is on a stay task and walks a move that is not Whiskey's own (<see cref="MoveContest.IsForeignStayMove"/> and not a parent's march); a form-up or a screen carries its ledger point (<see cref="StayX"/>, <see cref="StayZ"/>).</summary>
        public bool ForeignStayMove, HasStayPoint;
        public float StayX, StayZ;
        public float X, Z, WaypointX, WaypointZ, LaneX, LaneZ, IssuedX, IssuedZ;
        public double IssuedHours, PlanStartHours, NowHours;
        public CommandTaskType Task;
        public IWorksQuery Works;
        public IntentPriority Priority;
        public IntentSource Source;
    }

    /// <summary>
    /// One group's turn of the move contest (BC14W works hold, BC14O chain contest), pure over <see cref="ContestTurnFacts"/>: the observation the memory
    /// is asked, and the answer turned into the executor's intent. The posture producer builds the facts and calls this; it is where the flags are read,
    /// so the harness can run a turn with a flag on and off.
    /// </summary>
    public static class ContestTurn
    {
        /// <summary>
        /// Submits the Stop, Move or re-issue the contest answers with, and returns how many intents it submitted. The chain contest runs only with
        /// <c>Enable Objective Doctrine</c>, for a group not in close contact (vanilla's reactions to a fight are no-delay moves the executor leaves
        /// alone), and only for a due designer chain's own group whose waypoint is near the chain's head objective (see <see cref="MoveContest.Decide"/>).
        /// </summary>
        public static int Run(in ContestTurnFacts f, IIntentSink sink)
        {
            MoveContestState contest = MoveContestState.ForAlliance(f.Alliance);
            if (contest == null) return 0;
            bool chain = Plugin.ObjectiveDoctrineEnabled() && !f.CloseEngaged;
            bool hasObjective = false;
            float objectiveX = 0f, objectiveZ = 0f;
            if (chain)
            {
                DueChainTable due = DueChainTable.ForAlliance(f.Alliance);
                hasObjective = due != null && due.TryFind(f.GroupId, out objectiveX, out objectiveZ);
            }
            var observation = new ContestObservation
            {
                Held = f.Held, Recon = f.Recon, HasPath = f.HasPath,
                HasLastIssued = f.HasLastIssued, LastIssuedX = f.IssuedX, LastIssuedZ = f.IssuedZ,
                X = f.X, Z = f.Z, WaypointX = f.WaypointX, WaypointZ = f.WaypointZ,
                HasLane = f.HasLane, LaneX = f.LaneX, LaneZ = f.LaneZ,
                Stance = f.Stance, Works = f.Works,
                Chain = chain, HoldTask = MoveContest.IsHoldTask(f.Task),
                ForeignStayMove = f.ForeignStayMove, HasStayPoint = f.HasStayPoint, StayX = f.StayX, StayZ = f.StayZ,
                HasChainObjective = hasObjective, ChainObjectiveX = objectiveX, ChainObjectiveZ = objectiveZ,
                LastIssuedHours = f.IssuedHours, PlanStartHours = f.PlanStartHours, NowHours = f.NowHours,
            };
            ContestAction action = contest.Evaluate(f.GroupId, f.Tick, observation, out float holdX, out float holdZ);
            switch (action)
            {
                case ContestAction.HoldStop:
                    sink.Submit(PostureIntentPlan.ContestStop(f.GroupId, f.Priority, f.Source));
                    return 1;
                case ContestAction.HoldMove:
                    sink.Submit(PostureIntentPlan.ContestMove(f.GroupId, holdX, holdZ, f.Priority, f.Source));
                    return 1;
                case ContestAction.Reissue:
                    sink.Submit(PostureIntentPlan.ContestReissue(f.GroupId, holdX, holdZ, f.Task, f.Priority, f.Source));
                    return 1;
                default:
                    return 0;
            }
        }
    }
}
