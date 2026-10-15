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
// - Group strength for AI decisions (GetGroupStrength 6024-6060, AdjustGroupAIStance 4238,
//   also 5962 / 6269-6270): own / enemy × (1 - mi + groupmorale × mi) / max(0.01, 1 - mi +
//   enemymorale × mi) (6052-6055, includemorale) × (1 - ei + groupexperience × ei) /
//   max(0.01, 1 - ei + enemyexp × ei) (6056-6057, includeexp); mi / ei =
//   moraleinfluenceonaidecisions / expinfluenceonaidecisions (54712 / 54714, both 0.25).
//   The enemy morale term is the closest enemy group's unitrange.ownmoraleinrange:
//   Σ morale × strength over its units in range (122750) divided by ownstrengthinrange
//   (122892), which for a group also adds the group's own groupstrengthdirect (122886-122889),
//   so it is not a plain strength-weighted mean; here the perceived morale (the blend's
//   intended shape, as for experience). The enemy experience term is the closest enemy
//   group's unitrange.ownexpinrange (6039-6042), which vanilla accumulates as
//   Σ Experience() × morale without a strength factor (122751) and divides by
//   ownstrengthinrange (122894), so it is ≈ 0 for any real force. Not ported: the enemy
//   side here uses the blend's intended shape with the perceived enemy experience.
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
    /// Vanilla's unbearable-losses rule inputs that are the same for every unit of a battle
    /// (GetUnbearableLossesTrigger 113359-113404; unitprefs 53594-53604, 53927-53929;
    /// GameVars.aicheat_shiftedbonusmoralelosstrigger, ai_vs_ai, usedcampaignbonusrunning;
    /// campaignprefs aibonus_restoringmorale 56359).
    /// </summary>
    public struct BreakToleranceRules
    {
        public float Trigger0, Trigger100;                                    // unbearablelossesmoraletrigger0 / 100
        public float InfluenceExperience, InfluenceTraining, InfluenceLeadership; // influence…unbearablelossestrigger
        public float SkirmisherFactor;                                        // unbearablelossestriggerskirmishers
        public float AiBonusRestoringMorale, AiShiftedBonus, UsedCampaignBonusRunning;
        public bool AiVsAi;
        public float PerDayExperience0, PerDayExperience100;                  // unbearablelosstriggerincreaseperdayfactorexp0 / 100
    }

    /// <summary>One regiment's inputs to its unbearable-losses trigger.</summary>
    public struct BreakToleranceInput
    {
        public float MilitaryExperience; // GameVars.alliance[alliance].militaryexperience
        public float Experience;         // Experience()
        public float Training;           // raw training (vanilla calls GetTraining(), which mutates; equal for AI units)
        public float Leadership;         // GameVars.commander[commander].GetCommanderLeadership()
        public int UnitTyp;
        public bool PlayerAlliance;      // alliance == GameVars.playeralliance: no AI bonus
        public int BattlePassedDays;
    }

    /// <summary>
    /// Vanilla's micro-charge odds weights and trigger (UpdateChargeTarget 122229-122259;
    /// battleprefs microaitriggerforcharge 54776, weightingfactorsformicroaicharge 54777-54781).
    /// A Trigger of 0 or less means no pre-filter.
    /// </summary>
    public struct ChargeOddsRules
    {
        public float Trigger;
        public float WeightStrength, WeightExperience, WeightMorale, WeightFatigue;
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
        public float MoraleInfluenceOnAiDecisions; // GamePrefs.moraleinfluenceonaidecisions (54712; battleprefs 0.25)
        public BreakToleranceRules Break;
        public ChargeOddsRules ChargeOdds;

        /// <summary>The snapshot carries each regiment's break point only when on.</summary>
        public bool BreakToleranceOn => Enabled;

        /// <summary>The charge pre-filter's rules; default (no pre-filter) when off.</summary>
        public ChargeOddsRules ChargeOddsRulesIfEnabled => Enabled ? ChargeOdds : default;

        public float ExpInfluence => Enabled ? Clamp01(ExpInfluenceOnAiDecisions) : 0f;
        public float InitiativeInfluence => Enabled ? NonNegative(InitiativeTriggerInfluence) : 0f;
        public float MoraleInfluence => Enabled ? Clamp01(MoraleInfluenceOnAiDecisions) : 0f;

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
        /// Vanilla's quality blend for AI strength decisions (GetGroupStrength 6052-6057):
        /// 1 - influence + x × influence, x (experience or morale) clamped to [0, 1] (NaN reads
        /// as 0) and influence the expinfluenceonaidecisions / moraleinfluenceonaidecisions
        /// pref (0 = neutral).
        /// </summary>
        public static float AiDecisionBlend(float quality, float influence)
        {
            float w = TroopQualityPrefs.Clamp01(influence);
            return 1f - w + TroopQualityPrefs.Clamp01(quality) * w;
        }

        /// <summary>
        /// Side-level blends for global odds: own mean experience and morale against the
        /// perceived enemy means, each through <see cref="AiDecisionBlend"/>, multiplied as
        /// GetGroupStrength multiplies its morale and experience factors (6054, 6057).
        /// Neutral (1, 1) when either side is unknown, so one side is never weighed alone.
        /// </summary>
        public static void SideBlends(in SideQuality own, in SideQuality enemy, float expInfluence, float moraleInfluence,
            out float ownBlend, out float enemyBlend)
        {
            ownBlend = 1f;
            enemyBlend = 1f;
            if (!own.Known || !enemy.Known) return;
            ownBlend = AiDecisionBlend(own.Experience, expInfluence) * AiDecisionBlend(own.Morale, moraleInfluence);
            enemyBlend = AiDecisionBlend(enemy.Experience, expInfluence) * AiDecisionBlend(enemy.Morale, moraleInfluence);
        }

        /// <summary>
        /// Blends for one own unit's sector, GetGroupStrength(includemorale, includeexp) as
        /// AdjustGroupAIStance reads it (4238; 6052-6057): the group's own groupexperience and
        /// groupmorale against the perceived experience (stars) and morale of the closest
        /// visible enemy (vanilla's closest enemy group, 6039), else the side's perceived means
        /// (visible plus confidence-weighted remembered). Neutral (1, 1) when nothing is
        /// perceived, the index is out of range or both influences are 0. No allocation.
        /// </summary>
        public static void SectorBlends(BattleSnapshot s, int ownIndex, float expInfluence, float moraleInfluence,
            out float ownBlend, out float enemyBlend)
        {
            ownBlend = 1f;
            enemyBlend = 1f;
            if (s == null || ownIndex < 0 || ownIndex >= s.OwnCount) return;
            if (!(expInfluence > 0f) && !(moraleInfluence > 0f)) return;
            float enemyExperience, enemyMorale;
            int closest = s.ClosestVisibleEnemy(ownIndex, out _);
            if (closest >= 0 && closest < s.ObservationCount)
            {
                enemyExperience = s.Observations[closest].Experience;
                enemyMorale = s.Observations[closest].Morale;
            }
            else if (s.PerceivedEnemyQuality.Known)
            {
                enemyExperience = s.PerceivedEnemyQuality.Experience;
                enemyMorale = s.PerceivedEnemyQuality.Morale;
            }
            else return;
            ref UnitRecord own = ref s.Own[ownIndex];
            ownBlend = AiDecisionBlend(own.Experience, expInfluence) * AiDecisionBlend(own.GroupMorale, moraleInfluence);
            enemyBlend = AiDecisionBlend(enemyExperience, expInfluence) * AiDecisionBlend(enemyMorale, moraleInfluence);
        }

        /// <summary>
        /// A regiment's loss fraction as vanilla's rout-on-losses test reads it
        /// (LossesAreUnbearable 139829-139843): with L = Σ losses − losses[3] (the missing do
        /// not count), vanilla routs only when L / max(1, L + strength) AND
        /// L / max(1, L + strength + detached regiment's strength) both exceed the trigger
        /// (139832, 139834-139839), so the smaller of the two is the fraction compared with
        /// the trigger. No allocation.
        /// </summary>
        public static float LossFraction(int[] losses, int strength, int detachedStrength)
        {
            if (losses == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < losses.Length; i++)
                if (i != 3) sum += losses[i];
            if (!(sum > 0f)) return 0f;
            float own = sum + Math.Max(0, strength);
            float first = sum / Math.Max(1f, own);
            float second = sum / Math.Max(1f, own + Math.Max(0, detachedStrength));
            return Math.Min(first, second);
        }

        /// <summary>
        /// Vanilla's unbearable-losses trigger, the loss fraction at which the regiment routs
        /// (GetUnbearableLossesTrigger 113363-113400): a base retained fraction interpolated on
        /// the alliance's military experience, reduced by experience, drill and leadership,
        /// then (1 − that) × the skirmisher and AI bonus factors × the per-day factor, capped
        /// at 1. Tools.Interpolate over {0, 1} is an unclamped lerp.
        /// </summary>
        public static float BreakLossFraction(in BreakToleranceRules rules, in BreakToleranceInput unit)
        {
            float num = Lerp(rules.Trigger0, rules.Trigger100, unit.MilitaryExperience);
            num *= 1f - unit.Experience * (1f - rules.InfluenceExperience);
            num *= 1f - unit.Training * (1f - rules.InfluenceTraining);
            num *= 1f - unit.Leadership * (1f - rules.InfluenceLeadership);
            float factor = unit.UnitTyp == 3 || unit.UnitTyp == 4 ? rules.SkirmisherFactor : 1f;
            if (!unit.PlayerAlliance)
                factor *= 1f + ((rules.AiVsAi ? rules.AiShiftedBonus : 0f) + rules.UsedCampaignBonusRunning) * rules.AiBonusRestoringMorale;
            float perDay = (Lerp(rules.PerDayExperience0, rules.PerDayExperience100, unit.Experience) - 1f) * unit.BattlePassedDays + 1f;
            float trigger = Math.Min(1f, (1f - num) * factor * perDay);
            return float.IsNaN(trigger) || float.IsInfinity(trigger) ? 0f : trigger;
        }

        /// <summary>
        /// One side of vanilla's micro-charge odds (122229-122230): strength × (1 + w0) ×
        /// (1 + experience × w1) × (1 + morale × w2) × (1 + (1 − fatigue) × w3).
        /// </summary>
        public static float VanillaChargeStrength(float strength, float experience, float morale, float fatigue, in ChargeOddsRules w)
        {
            return strength * (1f + w.WeightStrength) * (1f + experience * w.WeightExperience)
                * (1f + morale * w.WeightMorale) * (1f + (1f - fatigue) * w.WeightFatigue);
        }

        /// <summary>
        /// Vanilla's charge odds own / max(1, enemy) (122231; infantry, so no meleemounted
        /// factor, 122232-122235). Vanilla charges when this exceeds microaitriggerforcharge.
        /// </summary>
        public static float VanillaChargeOdds(float ownStrength, float ownExperience, float ownMorale, float ownFatigue,
            float targetStrength, float targetExperience, float targetMorale, float targetFatigue, in ChargeOddsRules w)
        {
            float own = VanillaChargeStrength(ownStrength, ownExperience, ownMorale, ownFatigue, w);
            float target = VanillaChargeStrength(targetStrength, targetExperience, targetMorale, targetFatigue, w);
            return own / Math.Max(1f, target);
        }

        private static float Lerp(float at0, float at1, float t)
        {
            return at0 + (at1 - at0) * t;
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
