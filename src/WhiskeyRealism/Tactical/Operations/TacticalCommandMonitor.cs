namespace WhiskeyRealism.Tactical.Operations
{
    public enum TacticalIdleClassification
    {
        ValidIdle,
        IllegalIdle,
        ProtectedNoWrite
    }

    public readonly struct CommandPhysicalState
    {
        public bool Routed { get; }
        public bool PlayerProtected { get; }
        public bool PathInterrupted { get; }
        public int Paths { get; }
        public bool ActiveMove { get; }
        public int Formation { get; }

        public CommandPhysicalState(
            bool routed,
            bool playerProtected,
            bool pathInterrupted,
            int paths,
            bool activeMove,
            int formation)
        {
            Routed = routed;
            PlayerProtected = playerProtected;
            PathInterrupted = pathInterrupted;
            Paths = paths < 0 ? 0 : paths;
            ActiveMove = activeMove;
            Formation = formation;
        }
    }

    public static class TacticalCommandMonitor
    {
        public static TacticalIdleClassification ClassifyIdle(
            CommandNodeOperationalState state,
            CommandPhysicalState physical)
        {
            if (physical.PlayerProtected || physical.Routed)
            {
                return TacticalIdleClassification.ProtectedNoWrite;
            }

            if (state.Task == CommandTaskType.ReserveWait ||
                state.Task == CommandTaskType.HoldObjective ||
                state.Task == CommandTaskType.HoldChoke ||
                state.Task == CommandTaskType.FallBackToLine)
            {
                return TacticalIdleClassification.ValidIdle;
            }

            if (physical.PathInterrupted && physical.Paths <= 0 && !physical.ActiveMove)
            {
                return TacticalIdleClassification.IllegalIdle;
            }

            if (state.Task == CommandTaskType.None)
            {
                return TacticalIdleClassification.IllegalIdle;
            }

            return TacticalIdleClassification.ValidIdle;
        }
    }
}
