namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>
    /// BC14W enemy works (docs/superpowers/specs/2026-10-15-battle-maneuver-feasibility-design.md § 7A): the constants and the two small rules
    /// every works piece shares. Pure. The ids and the 0.15 threshold are vanilla's own (GetCoverIDFromName, battleprefs cover values, the unit
    /// scan of AIBattle.UpdateBlockedCrossings at 7962); the weights are this slice's, tuned in smoke.
    /// </summary>
    public static class WorksRules
    {
        /// <summary>Works 150 units apart laterally are one segment; the works hold's band reads the same strip.</summary>
        public const float WorksGapMeters = 150f;

        /// <summary>A point lies on a segment up to this far past its lateral extent.</summary>
        public const float WorksEndMeters = 50f;

        /// <summary>A profile point this much nearer the own side than the chord between the profile points 300 units either side is a salient apex.</summary>
        public const float SalientDepthMeters = 150f;

        /// <summary>The lateral bin of the depth profile, the spacing of the focal points.</summary>
        public const float FocalSampleMeters = 50f;

        /// <summary>The second line behind the profile that is still on the segment (it does not move the profile).</summary>
        public const float SecondLineMeters = 100f;

        /// <summary>The lateral reach the focal point's frontage is measured over, either side.</summary>
        public const float FrontageReachMeters = 300f;

        public const float SalientFactor = 0.5f, JunctionFactor = 0.8f;

        /// <summary>The rule of thumb of about three to one at the point of attack (spec § 3): the bar and the defence ratio of works.</summary>
        public const float WorksOddsFactor = 3.0f, WorksDefenseRatio = 3.0f;

        /// <summary>A break is made within the hour or not at all (Cold Harbor).</summary>
        public const float AssaultHours = 1.0f;

        /// <summary>A commander whose willingness is below this declines the focal assault.</summary>
        public const float FocalWillingness = 0.2f;

        /// <summary>The column goes in no earlier than this after the first battery in range opens.</summary>
        public const float PreparationHours = 0.25f;

        /// <summary>The support follows the column's strike by this.</summary>
        public const float EchelonDelayHours = 0.1f;

        /// <summary>A Defender's reserve stands this far behind its most threatened focal point.</summary>
        public const float ReserveStationMeters = 150f;

        public const float FocalThreatHours = 1.0f, StationSwitchHours = 0.25f;

        /// <summary>A model piece counts as a works point from this completion.</summary>
        public const float WorksMinCompletion = 0.5f;

        /// <summary>The strip in front of a held segment's profile a works-held group is kept out of.</summary>
        public const float WorksBandMeters = 100f;

        /// <summary>
        /// A strike point is broken in (<see cref="WorksPicture.StrikeBrokenIn"/>) when this share of the holders' strength within
        /// <see cref="FrontageReachMeters"/> of it stands below this morale: the opening detector's shaken line (<c>OpeningRules.ShakenMorale</c>
        /// 0.45, <c>OpeningDetector.ShakenShareMin</c> 0.4), restated here because the works picture is pure and reads no opening rules.
        /// </summary>
        public const float ShakenMorale = 0.45f, ShakenShareMin = 0.4f;

        public const int MaxWorksPoints = 2048, MaxWorksProbesPerTick = 16, MaxSegments = 32, MaxFocalPoints = 256;

        /// <summary>Profile points kept across all segments (a lateral bin each); past it the farthest bins are not kept and are counted.</summary>
        public const int MaxProfilePoints = 512;

        /// <summary>Works holders kept (the works samples of the segments); past it the weakest are not kept and are counted.</summary>
        public const int MaxHolders = 512;

        /// <summary>
        /// The cover ids that make works: stone wall 2, embankment 4, earth parapet 7, reinforced parapet 8, parapet and abatis 9, parapet and
        /// obstacles 10, breastworks 11, sunken road 15, unfinished railroad 16. Forest (3), creek (5), road (6), fence (1) and the buildings
        /// (12-14) are not: the terrain picture already prices woods and towns.
        /// </summary>
        public static bool IsWorksCover(int coverObject)
        {
            switch (coverObject)
            {
                case 2:
                case 4:
                case 7:
                case 8:
                case 9:
                case 10:
                case 11:
                case 15:
                case 16:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// How willing the army commander is to throw his force at works: <c>0.5 agg01 + 0.5 audacity01 - 0.5 caution01</c> (Franklin and Cold
        /// Harbor against Kennesaw's second thoughts). A cautious commander declines the focal assault and holds.
        /// </summary>
        public static float Willingness(in OpportunityPersonality p) => 0.5f * p.Agg01 + 0.5f * p.Audacity01 - 0.5f * p.Caution01;
    }
}
