namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Meeting engagement: both sides arrive piecemeal, neither set up.
    /// Rush to seize high ground, fight where you stand. Distinct from
    /// Probe (which assumes deploy-from-march) and from a deliberate
    /// attack (which assumes prepared positions). Historical anchor:
    /// McPherson's Ridge morning of Gettysburg Day 1 — Reynolds rides
    /// forward with I Corps' lead division, Heth's lead division
    /// already engaged with Buford's cavalry; both sides commit as
    /// units arrive rather than waiting for the full force.
    ///
    /// Maps to SoW's GetMeetingPlay() at offai.cpp:1115-1320, which is
    /// the third army-tier play branch alongside Attack and Defend.
    /// Generic-aggressive personality fit because anyone caught in a
    /// meeting engagement is forced to commit; aggressive commanders
    /// just commit faster.
    /// </summary>
    public sealed class MeetingEngagementPlaybook : TacticalPlaybook
    {
        public MeetingEngagementPlaybook() : base(
            BattlePlanId.MeetingEngagement,
            "meeting-engagement",
            new PersonalityFit(aggression: 0.5f, caution: 0.0f, audacity: 0.5f),
            new TerrainPreference(open: 0.6f, wooded: 0.6f, river: 0.5f, mountain: 0.6f),
            new OddsRange(min: 0.7f, max: 1.5f),
            reserveCommitTriggerOdds: 0.9f)
        { }

        public override TacticalBattlePlan Instantiate(PlaybookContext ctx) =>
            new TacticalBattlePlan(
                Id,
                BattlePhase.Probe,
                ctx.DefaultMainEffortSector,
                fixingSectors: null,
                screeningSectors: null,
                ReserveCommitTriggerOdds,
                ageSeconds: 0f,
                ctx.JitterSeed);
    }
}
