namespace WhiskeyRealism.Strategic.Planner
{
    // The fog-of-war sight radius exactly as FogOfWar.UpdateViewRange paints it on the campaign
    // map (101296-101309), so the planner's viewer check (spec C14, G1) never claims sight the
    // grid lacks:
    //   maximumlineofsight[unittyp]
    //   x perk_expertscoutsvisibilityrangefactor[GetPerkLevel(8)] when HasPerk(8) (101298-101301)
    //   x (1 - climate(105) x influenceofrainsnowonvisibilitycampaign)            (101302)
    //   x fow_lineofsightreduction_layndown when lying down                        (101303-101305)
    //   x dlcwl_incidentscoutingaheadrange for the W&L player's command under incident 63 (101307-101309)
    // With Fix Perk Id Mismatches on, FogPerkSightPatch hides id 8 from every viewer that is not
    // on the battle perk table (VanillaPerkTables.HideExpertScoutsSight; campaign id 8 is
    // Ambulance Corps), so campaign groups (unittyp 14-16, 18) take no perk term at all and no
    // campaign perk (Expert Scouts is campaign id 16) changes campaign sight. With the flag off
    // this is vanilla's unfixed radius: the factor on any HasPerk(8). The caller passes 1 for
    // incident63Factor when incident 63 does not apply. hqinthesaddle is battle-only (101312).
    // A negative radius (rain/snow influence above 1) paints nothing; it is clamped to 0.
    public static class SightRadiusFormula
    {
        public static float Campaign(int unitTyp, float los, bool hasPerk8, float perk8Factor, float climate105,
                                     float rainSnowInfluence, bool liedown, float liedownFactor, float incident63Factor,
                                     bool fixPerkIdMismatches)
        {
            float radius = los;
            if (hasPerk8 && !VanillaPerkTables.HideExpertScoutsSight(fixPerkIdMismatches, unitTyp, hasPerk8))
                radius *= perk8Factor;
            radius -= radius * climate105 * rainSnowInfluence;
            if (liedown)
                radius *= liedownFactor;
            radius *= incident63Factor;
            return radius > 0f ? radius : 0f;
        }
    }
}
