namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Forrest-style cavalry raid: cavalry strikes lines of communication
    /// independently, rather than supporting an infantry main effort.
    /// Historical anchor: Forrest's Brice's Crossroads + Streight's Raid
    /// counter-pursuit + the Murfreesboro raid. Aggressive + audacious
    /// commanders only — caution kills raids.
    ///
    /// Distinct from JacksonValleyShuffle (Jackson is infantry-with-cavalry-
    /// supplement, Forrest is cavalry-with-infantry-rare). Pairs with the
    /// existing TacticalCavalryFollowDoctrine raid filters (hidden/officer/
    /// fort/square/supported targets get rejected before raid commits).
    /// </summary>
    public sealed class ForrestCavalryRaidPlaybook : TacticalPlaybook
    {
        public ForrestCavalryRaidPlaybook() : base(
            BattlePlanId.ForrestCavalryRaid,
            "forrest-cavalry-raid",
            // Personality fit deliberately moderate. The scoring model is
            // dot-product based, so a high-magnitude fit vector in the
            // "aggressive" orthant would dominate Lee/Sherman/Hood/Jackson
            // selection tests universally — but cavalry-raid distinction
            // historically came from CAVALRY-SPECIFIC traits we don't model
            // here (Forrest's lack of formal training + independence). To
            // avoid stomping the field-commander selection logic, this fit
            // is dialed back; the envelopment-affinity bonus (0.30 in
            // TacticalPlaybookCatalog) plus narrow open-only terrain plus
            // wide odds range carry the raid signature.
            new PersonalityFit(aggression: 0.5f, caution: -0.2f, audacity: 0.7f),
            // Cavalry raids hate woods — restricted mobility, lose the speed
            // and shock advantage. Set wooded/mountain low so the catalog
            // doesn't pick Forrest's raid doctrine for wooded engagements
            // (where Lee/Jackson envelopment fits would win anyway).
            new TerrainPreference(open: 0.9f, wooded: 0.3f, river: 0.2f, mountain: 0.3f),
            // Odds band narrowed to [0.7, 1.5] — historical Forrest raids
            // worked at near-parity through 1.5:1 advantage; he avoided
            // engagement when severely outnumbered (cavalry can't trade
            // attrition with infantry). Floor at 0.7 also keeps the catalog
            // from selecting Forrest as the "I'm getting crushed" playbook
            // (that's GenericDesperate's domain).
            new OddsRange(min: 0.7f, max: 1.5f),
            reserveCommitTriggerOdds: 1.0f)
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
