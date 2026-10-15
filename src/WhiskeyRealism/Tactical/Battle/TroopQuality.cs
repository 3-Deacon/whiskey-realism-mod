// Troop quality in the mod's AI decisions (battle-core BC8, plan
// docs/superpowers/plans/2026-10-15-battle-core.md § "BC8 — experience and drill"). Pure: no
// Unity or vanilla types; TroopQualityRuntime reads vanilla's prefs once per battle.
//
// Vanilla anchors (Assembly-CSharp build 14703111):
// - Force balance (CalculateSideStatsAndUpdateAITasks 84862-84863): weighted strength ratio
//   × (1 + averagemorale_own − averagemorale_enemy) × (1 + averageexperience_own −
//   averageexperience_enemy). Averages: 84654 / 84758 / 84771, divided 84792 / 84836.
// - The macro triggers it is compared with are divided by
//   1 + initiative × influenceofcommanderinitiativeonattackdefendtrigger (6396-6430).
// - Experience shown to the player: UpdateUnitIcon draws only for a visible unit
//   (121594-121598; isvisible is false under iswithinfog, 127461) and the star block has no
//   alliance gate: floor(Experience() × 5) stars, 0-5, for a regiment (121832-121852; a battle
//   group shows 0). So a visible enemy regiment's experience is known to the star.
// - Regiment.Experience() (140114) is a pure getter; GetTraining() (140537-140549) is not
//   (under W&L it adds DLC effect 20 to training on every call), so drill is read from the
//   raw training field and grouptraining (120473, 120671), never through GetTraining().

using System;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>
    /// One side's mean morale and experience, or Unknown when nothing was sampled. Unknown on
    /// either side makes every quality factor neutral.
    /// </summary>
    public readonly struct SideQuality
    {
        public SideQuality(float morale, float experience)
        {
            Known = true;
            Morale = Sanitize(morale);
            Experience = Sanitize(experience);
        }

        public bool Known { get; }
        public float Morale { get; }
        public float Experience { get; }

        public static SideQuality Unknown => default;

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }

    /// <summary>
    /// Vanilla prefs the quality rules read, captured once per battle by TroopQualityRuntime.
    /// With <see cref="Enabled"/> off (Enable Experience And Drill AI Weighting = false) every
    /// accessor is neutral, which is the BC7 headcount behaviour.
    /// </summary>
    public struct TroopQualityPrefs
    {
        public bool Enabled;
        public float ExpInfluenceOnAiDecisions;   // GamePrefs.expinfluenceonaidecisions (54714; battleprefs 0.25)
        public float InitiativeTriggerInfluence;  // influenceofcommanderinitiativeonattackdefendtrigger (54680; 0.7)

        public float ExpInfluence => Enabled ? Clamp01(ExpInfluenceOnAiDecisions) : 0f;
        public float InitiativeInfluence => Enabled ? NonNegative(InitiativeTriggerInfluence) : 0f;

        /// <summary>The side quality a decision may use: Unknown (neutral) when off.</summary>
        public SideQuality Quality(in SideQuality quality)
        {
            return Enabled ? quality : SideQuality.Unknown;
        }

        internal static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value < 0f ? 0f : value > 1f ? 1f : value;
        }

        internal static float NonNegative(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value < 0f ? 0f : value;
        }
    }

    public static class TroopQuality
    {
        public const int MaxStars = 5;

        /// <summary>
        /// What the player sees of a visible enemy regiment's experience: its star bucket
        /// floor(5e) clamped to 0-5 (121832-121852), read as the bucket midpoint
        /// (stars + 0.5) / 5 and capped at 1 for five stars. NaN reads as no stars.
        /// </summary>
        public static float PerceivedExperience(float experience)
        {
            if (float.IsNaN(experience)) experience = 0f;
            int stars = experience >= 1f ? MaxStars : experience <= 0f ? 0 : (int)Math.Floor(experience * MaxStars);
            if (stars >= MaxStars) return 1f;
            return (stars + 0.5f) / MaxStars;
        }

        /// <summary>
        /// Vanilla's force-balance quality factor (84862): (1 + Δmorale) × (1 + Δexperience),
        /// own minus enemy. Each term floors at 0 so two large deficits never multiply into a
        /// positive factor; Unknown on either side is neutral (1).
        /// </summary>
        public static float ForceBalanceFactor(in SideQuality own, in SideQuality enemy)
        {
            if (!own.Known || !enemy.Known) return 1f;
            float morale = 1f + own.Morale - enemy.Morale;
            float experience = 1f + own.Experience - enemy.Experience;
            return Math.Max(0f, morale) * Math.Max(0f, experience);
        }

        /// <summary>
        /// The side's own mean morale and experience over alive, active, unrouted combat
        /// regiments (unittyp 0-3, strength &gt; 0: the OwnAverages filter, vanilla
        /// unitsonfield 84800). Unweighted, like vanilla's per-unit morale mean. No allocation.
        /// </summary>
        public static SideQuality OwnQuality(UnitRecord[] own, int count)
        {
            if (own == null) return SideQuality.Unknown;
            if (count > own.Length) count = own.Length;
            float morale = 0f, experience = 0f;
            int sampled = 0;
            for (int i = 0; i < count; i++)
            {
                ref UnitRecord u = ref own[i];
                if (u.UnitTyp > 3 || (u.Flags & UnitFlags.IsGroup) != 0) continue;
                if (u.Strength <= 0 || (u.Flags & UnitFlags.Active) == 0 || (u.Flags & UnitFlags.Routed) != 0) continue;
                if (float.IsNaN(u.Morale) || float.IsNaN(u.Experience)) continue;
                morale += u.Morale;
                experience += u.Experience;
                sampled++;
            }
            return sampled == 0 ? SideQuality.Unknown : new SideQuality(morale / sampled, experience / sampled);
        }

        /// <summary>
        /// The perceived enemy's mean morale and experience over the side's observations:
        /// visible weight 1, remembered weight Confidence01, reinforcement columns (unseen
        /// groups, no stars) excluded. No allocation.
        /// </summary>
        public static SideQuality PerceivedQuality(EnemyObservation[] observations, int count)
        {
            if (observations == null) return SideQuality.Unknown;
            if (count > observations.Length) count = observations.Length;
            float weight = 0f, morale = 0f, experience = 0f;
            for (int i = 0; i < count; i++)
            {
                ref EnemyObservation o = ref observations[i];
                if (o.IsReinforcement) continue;
                float w = o.State == ObservationState.Visible ? 1f : o.Confidence01;
                if (!(w > 0f)) continue;
                weight += w;
                morale += o.Morale * w;
                experience += o.Experience * w;
            }
            return weight > 0f ? new SideQuality(morale / weight, experience / weight) : SideQuality.Unknown;
        }
    }
}
