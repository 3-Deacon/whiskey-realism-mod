namespace WhiskeyRealism.Strategic
{
    public enum DefensePosture
    {
        NotEvaluated = 0,
        CoastalGuard,
        InvasionWatch,
        ActiveInvasion,
        ContainAndCounterattack,
        Recovered
    }

    public enum ThreatScale
    {
        None = 0,
        Raid,
        Landing,
        MajorLanding,
        DecisiveLanding
    }

    public enum CandidateTier
    {
        Local = 1,
        SameTheater = 2,
        AdjacentTheater = 3,
        CrossMap = 4
    }
}
