using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // What the runtime reads off one hidden enemy group and the side's grid before the mark ledger judges it (foresight F4, D11
    // requirement-marks-ai-side). Every field is a vanilla pref or a live reading of the marked group, filled once per group per review.
    internal struct MarkInputs
    {
        public float Intel;                                 // fows[1].GetIntelligenceOnPos(position): 1 while UseIntelligenceMap is off (101234-101237)
        public float Spotting0, Spotting1;                  // GamePrefs.spottingfactor, or timefactorspottings once BUG-INTEL-001 is fixed (MarkRules.TimingPair)
        public float SpottingVariation;                     // GamePrefs.spottingvariationfactor
        public float PerkFactor;                            // informationdelay x informationdelaybureauofmilitaryinformation terms (142823-142844), 1 without perks
        public float Cap;                                   // GamePrefs.maximumcreationprobability
        public float MinDistance;                           // GamePrefs.minimumdistanceforspotmark
        public float DistanceVariation0, DistanceVariation1;   // GamePrefs.intelligencedistancevariationfactor
        public bool HasClosestEnemy, Active, Garrison, Fleet;
        public float ClosestEnemyX, ClosestEnemyZ;
    }

    // Vanilla's spotting-mark arithmetic as pure functions (SpottedEnemy, 142530-143122): the creation chance, the offset, the radius, the
    // approximation band and the calendar decode. The AI side mirrors it on its own grid (MarkLedger); the player side only reads vanilla's
    // marks (NewestMark, PlayerSideMark). BUG-INTEL-001 is kept: CreationChance takes the pair the caller passes, and MarkRules.TimingPair
    // (foresight F4b) decides which pair that is.
    internal static class MarkRules
    {
        // The unread pref's neighbour in 142820: a negative or tiny time constant floors here (game years).
        internal const float MinTimeConstantYears = 0.001f;
        // GetCampaignFloatingDate adds day-1 over 365.25 and month-1 over 12 (66617): a day's width in game years.
        private const double DayYears = 1.0 / 365.25;
        // A float32 year near 1861 resolves about 1.2e-4 years (an hour); the decode tolerates that much below a day's start.
        private const double DecodeSlackYears = 1.3e-4;

        // TransformNumber's band (80226-80255) then GetApproximateValue (150478), without the global multiplier of 80261: both sides read
        // the same rounding of a marked group's strength. approximationValues is GamePrefs.approximationvalues (five bands).
        internal static float BandRound(float strength, int[] approximationValues)
        {
            if (float.IsNaN(strength) || float.IsInfinity(strength)) return 0f;
            float magnitude = Math.Abs(strength);
            int approximateTo = 1;
            if (approximationValues != null && approximationValues.Length >= 5)
            {
                if (magnitude >= 50000f) approximateTo = approximationValues[4];
                if (magnitude < 50000f) approximateTo = approximationValues[3];
                if (magnitude < 10000f) approximateTo = approximationValues[2];
                if (magnitude < 5000f) approximateTo = approximationValues[1];
                if (magnitude < 1000f) approximateTo = approximationValues[0];
            }
            if (magnitude < 100f) approximateTo = 5;
            if (approximateTo < 1) approximateTo = 1;
            // GetApproximateValue's smaller rounding for small figures: under a tenth of the step, the step shrinks to a tenth (truncated).
            if (magnitude < (int)(approximateTo * 0.1f)) approximateTo = Math.Max(1, (int)(approximateTo * 0.1f));
            double rounded = Math.Round(strength / (double)approximateTo, MidpointRounding.ToEven) * approximateTo;
            return (float)rounded;
        }

        // 142695 / 142693: intel x intelligencedistancevariationfactor[1] + (1 - intel) x intelligencedistancevariationfactor[0].
        internal static float MarkRadius(float intel, float[] distanceVariation)
        {
            if (distanceVariation == null || distanceVariation.Length < 2) return 0f;
            return intel * distanceVariation[1] + (1f - intel) * distanceVariation[0];
        }

        // 142819-142846: the chance a mark is created this evaluation. elapsedYears since the newest mark's timeshown (the full date when
        // there is none: 142818 reads 0 as the time), variationDraw the drawn Random.Range(1 - v, 1 + v). The time constant is floored at
        // MinTimeConstantYears (142820), which is what BUG-INTEL-001's negative spottingfactor pair turns into.
        internal static float CreationChance(float elapsedYears, float intel, float spotting0, float spotting1, float variationDraw, float perkFactor, float cap)
        {
            float timeConstant = (intel * spotting1 + (1f - intel) * spotting0) * variationDraw;
            float chance = elapsedYears / Math.Max(MinTimeConstantYears, timeConstant);
            chance *= perkFactor;
            if (chance > cap) chance = cap;
            if (chance < 0f) chance = 0f;
            return chance;
        }

        // Random.Range(1 - v, 1 + v) from the seeded stream (142819).
        internal static float VariationDraw(Random rng, float variationFactor)
        {
            float low = 1f - variationFactor, high = 1f + variationFactor;
            return low + (float)rng.NextDouble() * (high - low);
        }

        // Tools.GetAngle (151304-151310): atan2(dx, dz) in degrees minus 180, the REVERSE bearing from "from" to "to".
        internal static float ReverseBearing(float fromX, float fromZ, float toX, float toZ)
        {
            return (float)(Math.Atan2(toX - fromX, toZ - fromZ) / Math.PI * 180.0) - 180f;
        }

        // 142695-142712: the offset of a new mark from the group's true position. Toward the closest enemy as vanilla does (the reverse
        // bearing + 180 points at it; the int angle draw is -45..44, the magnitude Random(3, radius)), else a box of +- the radius; blended
        // with the previous mark's offset by min(1, age x 25) only when a previous mark exists. Draw order: angle, magnitude (or x, z).
        internal static void Offset(Random rng, float intel, float[] distanceVariation, bool hasClosestEnemy, float angleToEnemyDeg,
                                    bool hasPrevious, float ageYears, float prevOffsetX, float prevOffsetZ, out float dx, out float dz)
        {
            float radius = MarkRadius(intel, distanceVariation);
            if (hasClosestEnemy)
            {
                float angle = angleToEnemyDeg + 180f + rng.Next(-45, 45);
                float distance = 3f + (float)rng.NextDouble() * (radius - 3f);
                // Tools.GetAnglePos (151207-151217): angle - 90 degrees, x + cos, z - sin.
                float radians = (angle - 90f) * (float)Math.PI / 180f;
                dx = (float)Math.Cos(radians) * distance;
                dz = -(float)Math.Sin(radians) * distance;
            }
            else
            {
                dx = -radius + (float)rng.NextDouble() * (2f * radius);
                dz = -radius + (float)rng.NextDouble() * (2f * radius);
            }
            if (hasPrevious)
            {
                float keep = Math.Max(0f, Math.Min(1f, ageYears * 25f));
                dx = dx * keep + prevOffsetX * (1f - keep);
                dz = dz * keep + prevOffsetZ * (1f - keep);
            }
        }

        // The calendar day a mark's timeshown (floating campaign years, GameVars.GetCampaignFloatingDate = year + (month - 1)/12 + (day - 1)/365.25
        // + hour/8766, 66617) stands for, at most today. The month and day terms overlap (a month is 1/12 year wide, 30.44 days, whatever its
        // length): the late hours of day 31 sit above the next month's day 1. The inverse takes the earliest calendar date whose day
        // window holds the value, so 31 January 23:00 stays in January and 1 March follows 28 February by one day.
        internal static int MarkDay(float timeshown, int todaySerial)
        {
            if (float.IsNaN(timeshown) || float.IsInfinity(timeshown)) return todaySerial;
            double t = timeshown;
            int year = (int)Math.Floor(t);
            double fraction = t - year;
            int foundMonth = 0, foundDay = 0, lastMonth = 1, lastDay = 1;
            for (int month = 1; month <= 12 && foundMonth == 0; month++)
            {
                int days = DaysInMonth(year, month);
                for (int day = 1; day <= days; day++)
                {
                    double start = (month - 1) / 12.0 + (day - 1) * DayYears;
                    if (fraction >= start - DecodeSlackYears && fraction < start + DayYears)
                    {
                        foundMonth = month;
                        foundDay = day;
                        break;
                    }
                    if (start <= fraction) { lastMonth = month; lastDay = day; }
                }
            }
            if (foundMonth == 0) { foundMonth = lastMonth; foundDay = lastDay; }   // between two days' windows (February's missing days): the day before
            return Math.Min(ReviewCadenceCatchUp.DaySerial(foundDay, foundMonth, year), todaySerial);
        }

        private static int DaysInMonth(int year, int month)
        {
            switch (month)
            {
                case 2: return (year % 4 == 0 && (year % 100 != 0 || year % 400 == 0)) ? 29 : 28;
                case 4: case 6: case 9: case 11: return 30;
                default: return 31;
            }
        }

        // AddMark appends (142721-142722), so the newest mark is the last one; -1 when the group holds none.
        internal static int NewestMark(IReadOnlyList<float> timeshown)
        {
            return timeshown == null || timeshown.Count == 0 ? -1 : timeshown.Count - 1;
        }

        // The player side reads a mark only for an enemy group no rule a-d detected today that has at least one mark.
        internal static bool PlayerSideMark(UnitRelation relation, bool detectedToday, int markCount)
        {
            return relation == UnitRelation.Enemy && !detectedToday && markCount > 0;
        }

        // The AI side's stream for one group on one review: CampaignSeed ^ (alliance x 7919) ^ DaySerial ^ FNV-1a(UnitKey), folded to an int.
        // Never UnityEngine.Random, which would shift vanilla's own stream.
        internal static Random RngFor(long campaignSeed, int alliance, int daySerial, string unitKey)
        {
            long seed = campaignSeed ^ ((long)alliance * 7919L) ^ daySerial ^ unchecked((long)PlannerState.Fnv1a64(unitKey));
            return new Random(unchecked((int)(seed ^ (seed >> 32))));
        }
    }
}
