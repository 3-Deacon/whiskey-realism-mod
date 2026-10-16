namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    /// <summary>The heavy path's two resumable jobs of one tick, as FeasibilityTick runs them.</summary>
    internal interface IFeasibilityTickWork
    {
        /// <summary>A monitor run is wanted (a new snapshot) or part-way.</summary>
        bool MonitorPending { get; }

        void RunMonitor();

        void RunEvaluation();
    }

    /// <summary>
    /// BC14 requirement-frame-budget: the order of the tick's work under its one slice budget. The monitor goes first when it is pending, so a
    /// replan it owes (the plan's assumptions broke) is never starved by a wide Defender evaluation that would otherwise take the whole slice;
    /// the evaluation still gets its guaranteed unit and the rest of the slice. A generic over a struct: nothing is allocated per tick.
    /// </summary>
    internal static class FeasibilityTick
    {
        public static void Run<T>(ref T work) where T : struct, IFeasibilityTickWork
        {
            if (work.MonitorPending) work.RunMonitor();
            work.RunEvaluation();
        }
    }
}
