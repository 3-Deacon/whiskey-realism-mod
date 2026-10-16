using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // Vanilla's chapter tempo roll as pure arithmetic (plan G5a, chapters decision 5, "Keep chapter pacing").
    //
    // Vanilla rolls once per offensive package whose lead targets enemy ground (CheckOffensiveMovements,
    // 14316-14350): tval = aiagressiveness[CurrentChapter] x timediff x 10 x aiagressivenessoffensive
    // operationfaction[alliance] x usedcampaignagressiveness x winter + clamp01(idle^3), times
    // dlcwl_careerincreasedaggressiveness in W&L (once more when the player's campaign group is in the
    // package); the offensive goes ahead when roll <= tval, always under the scripted 1861 Union push
    // (14303-14306) or GameVars.debug_permanentaimovement (14350). The planner rolls once per new main
    // effort, for the planned mass, instead of vanilla (the mass is withheld from vanilla's offensive,
    // CampaignWithholdView), so the chapter pacing survives: this class carries the same terms, the tempo
    // clock and the seeded draw. Everything here is own-side or public prefs, so it is fog-honest.
    public static class CampaignTempoGate
    {
        // One daily review's worth: the cap on the tempo clock (TimeDiffYears).
        public const int ReviewGapDays = 1;

        // Moved verbatim from CoordinatedOffensiveVanillaGates (14336-14350):
        //   tval = chapter x timediff x 10 x faction x campaign x winter + clamp01(idle^3)   (idle = date - mean timeofnotmoving)
        //   W&L scenario: tval *= wlAggressiveness (14344), and again when the player's group is in the package (14346).
        public static float TempoProbability(float chapterAggression, float timediff, float factionAggression,
            float campaignAggression, float winterModifier, float idleYears,
            bool wlScenarioActive, float wlAggressiveness, bool playerGroupInPackage)
        {
            float tval = chapterAggression * timediff * 10f * factionAggression * campaignAggression * winterModifier;
            float idle = Math.Max(0f, Math.Min(1f, idleYears * idleYears * idleYears));
            tval += idle;
            if (wlScenarioActive)
            {
                tval *= wlAggressiveness;
                if (playerGroupInPackage) tval *= wlAggressiveness;
            }
            return tval;
        }

        // GamePrefs.aiagressiveness (five values, loaded 56244-56248 from the scenario's prefs) at the chapter; 0 for a chapter
        // outside the array (Policy.CurrentChapter is -1 before vanilla's frame-50 update, 211208/29857).
        public static float ChapterAggression(float[] aiagressiveness, int chapter)
        {
            if (aiagressiveness == null || chapter < 0 || chapter >= aiagressiveness.Length) return 0f;
            return aiagressiveness[chapter];
        }

        // Vanilla's scripted flag at 14303-14306: scenario 001, the Union, objective 6 still open, past 1861.513.
        public static bool ForcedInvasion(string scenario, bool objective6Accomplished, int alliance, float floatingDate)
        {
            return scenario == "001" && !objective6Accomplished && alliance == 0 && floatingDate > 1861.513f;
        }

        // 14350: vanilla returns when `roll > tval && !debug_permanentaimovement && !flag`. Called as
        // Releases(roll, tval, ForcedInvasion(...) || debug_permanentaimovement).
        public static bool Releases(float roll, float tval, bool forced)
        {
            return forced || roll <= tval;
        }

        // The tempo clock (decision 5): min(today - lastFullReviewDay, ReviewGapDays) / 365.25, one review's worth when no
        // previous executors-allowed review is recorded (-1: a v1/v2 sidecar, 001's pre-war idle, a first review) or the clock
        // went back (a load). Vanilla's own first pass uses timediff 0 (11315-11317) and carries a paused gap (its lastupdate
        // is not advanced while its job machine is paused, 11327); the planner does neither.
        public static float TimeDiffYears(int today, int lastFullReviewDay)
        {
            return GapDays(today, lastFullReviewDay) / 365.25f;
        }

        internal static int GapDays(int today, int lastFullReviewDay)
        {
            if (lastFullReviewDay < 0) return ReviewGapDays;
            int gap = today - lastFullReviewDay;
            if (gap < 0) return ReviewGapDays;
            return Math.Min(gap, ReviewGapDays);
        }

        // 14321-14327: the campaign date less the package's mean timeofnotmoving (an empty package reads as mean 0).
        public static float IdleYears(float campaignDate, IReadOnlyList<float> timeOfNotMoving)
        {
            float sum = 0f;
            int count = timeOfNotMoving != null ? timeOfNotMoving.Count : 0;
            for (int i = 0; i < count; i++) sum += timeOfNotMoving[i];
            return campaignDate - sum / Math.Max(1f, (float)count);
        }

        // The seeded draw in [0, 1): FNV-1a over the little-endian bytes of (campaign seed, alliance, decision serial, day
        // serial), finished with the 64-bit murmur3 mixer so consecutive days and decisions are not correlated, the top 24 bits
        // as a float. Reproducible (never string.GetHashCode, which differs between runtimes), like P5's draw.
        public static float Roll01(long campaignSeed, int alliance, int decisionSerial, int daySerial)
        {
            unchecked
            {
                ulong h = 14695981039346656037UL;
                h = Feed(h, (ulong)campaignSeed);
                h = Feed(h, (ulong)(uint)alliance);
                h = Feed(h, (ulong)(uint)decisionSerial);
                h = Feed(h, (ulong)(uint)daySerial);
                h ^= h >> 33;
                h *= 0xff51afd7ed558ccdUL;
                h ^= h >> 33;
                h *= 0xc4ceb9fe1a85ec53UL;
                h ^= h >> 33;
                return (h >> 40) / 16777216f;
            }
        }

        private static ulong Feed(ulong hash, ulong value)
        {
            unchecked
            {
                for (int i = 0; i < 8; i++)
                {
                    hash ^= (value >> (8 * i)) & 0xffUL;
                    hash *= 1099511628211UL;
                }
                return hash;
            }
        }

        // Vanilla rolls only for ground in a state of another alliance (14335) and only when an enemy weight stands at the
        // target (num2 > 0, 14336); the believed land strength within relocation range of the target stands in for num2.
        public static bool NeedsRoll(bool targetInAnotherAllianceState, float believedEnemyAtTarget)
        {
            return targetInAnotherAllianceState && believedEnemyAtTarget > 0f;
        }

        // The whole decision for one new main effort: no roll when vanilla would not roll (released at once, before the first
        // dispatch), else the probability, the seeded draw and the release.
        internal static LaunchVerdict Evaluate(in TempoInputs inputs)
        {
            var verdict = new LaunchVerdict { Forced = inputs.Forced, GapDays = GapDays(inputs.TodayDay, inputs.LastFullReviewDay) };
            if (!NeedsRoll(inputs.TargetInAnotherAllianceState, inputs.BelievedEnemyAtTarget))
            {
                verdict.Released = true;
                return verdict;
            }
            verdict.NeedsRoll = true;
            verdict.Probability = TempoProbability(inputs.ChapterAggression, TimeDiffYears(inputs.TodayDay, inputs.LastFullReviewDay),
                inputs.FactionAggression, inputs.CampaignAggression, inputs.WinterModifier, inputs.IdleYears,
                inputs.WlActive, inputs.WlAggressiveness, inputs.PlayerGroupInMass);
            verdict.Roll = Roll01(inputs.CampaignSeed, inputs.Alliance, inputs.DecisionSerial, inputs.TodayDay);
            verdict.Released = Releases(verdict.Roll, verdict.Probability, inputs.Forced);
            return verdict;
        }
    }

    internal struct TempoInputs
    {
        public float ChapterAggression, FactionAggression, CampaignAggression, WinterModifier;
        public bool WlActive;
        public float WlAggressiveness;
        public bool PlayerGroupInMass;
        public bool Forced;
        public float IdleYears;
        public int TodayDay, LastFullReviewDay;
        public long CampaignSeed;
        public int Alliance, DecisionSerial;
        public bool TargetInAnotherAllianceState;
        public float BelievedEnemyAtTarget;
    }

    internal struct LaunchVerdict
    {
        public bool NeedsRoll, Released, Forced;
        public float Probability, Roll, GapDays;
    }
}
