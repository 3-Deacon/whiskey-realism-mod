namespace WhiskeyRealism.Tactical.Orchestrator
{
    public enum EchelonKind
    {
        Unknown = 0,
        Army = 1,
        Corps = 2,
        Division = 3,
        Brigade = 4,
    }

    public abstract class EchelonOrchestrator
    {
        protected EchelonOrchestrator(EchelonKind kind, int allianceId)
        {
            Kind = kind;
            AllianceId = allianceId;
        }

        public EchelonKind Kind { get; }
        public int AllianceId { get; }
    }
}
