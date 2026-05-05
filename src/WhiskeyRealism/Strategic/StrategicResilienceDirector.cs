using System;

namespace WhiskeyRealism.Strategic
{
    public static class StrategicResilienceDirector
    {
        public static DirectorPosture ProposePosture(
            int allianceId,
            CampaignPaceOutput pace,
            PersonalityVector personality)
        {
            var posture = new DirectorPosture
            {
                AllianceId = allianceId,
                Pace = pace?.Pace ?? CampaignPace.Stable,
                Risk = pace?.Risk ?? CollapseRisk.Low,
                Reason = pace?.Reason ?? "no-pace-input"
            };

            posture.Intent = ProposeIntent(posture.Pace, posture.Risk, personality);

            if (pace != null && pace.IntentBlockedFromPreserve && posture.Intent == StrategicIntent.Preserve)
                posture.Intent = StrategicIntent.Delay;

            ApplyThresholdModifiers(posture, personality);
            return posture;
        }

        private static StrategicIntent ProposeIntent(CampaignPace pace, CollapseRisk risk, PersonalityVector personality)
        {
            switch (pace)
            {
                case CampaignPace.TooFastCollapse: return StrategicIntent.Recover;
                case CampaignPace.Overheated:      return StrategicIntent.Recover;
                case CampaignPace.TooQuiet:        return StrategicIntent.Probe;
                case CampaignPace.LateWarPressure: return risk >= CollapseRisk.Elevated ? StrategicIntent.Delay : StrategicIntent.Concentrate;
                case CampaignPace.Stalemated:      return StrategicIntent.Probe;
                default:                            return StrategicIntent.Concentrate;
            }
        }

        // Personality contributions from OperationalTempoDoctrine.ApplyPersonality:
        //   MaximumProbeStrengthFraction += 0.05*audacity - 0.04*caution
        //   EscalateFriendlyRatio        += 0.15*caution  - 0.10*audacity
        //   MinimumProbeDays             ±1 on |audacity|/|caution| > 0.35
        // Director modifiers are bounded to ±50% of the absolute personality delta on the same field.
        private static void ApplyThresholdModifiers(DirectorPosture posture, PersonalityVector personality)
        {
            float audacity = personality.Audacity;
            float caution = personality.Caution;

            float pFraction = Math.Abs(0.05f * audacity - 0.04f * caution);
            float pEscalate = Math.Abs(0.15f * caution - 0.10f * audacity);
            float pReaction = 0.10f; // doctrine doesn't adjust this from personality; cap at 0.10
            float pWithdraw = 0.08f; // same — small fixed cap
            float pDays     = (Math.Abs(audacity) > 0.35f || Math.Abs(caution) > 0.35f) ? 1f : 0.5f;

            float fractionMod = 0f, escalateMod = 0f, reactionMod = 0f, withdrawMod = 0f, daysMod = 0f;

            switch (posture.Pace)
            {
                case CampaignPace.Overheated:
                    fractionMod = -0.5f * pFraction;
                    escalateMod = +0.5f * pEscalate;
                    daysMod     = +0.5f * pDays;
                    break;
                case CampaignPace.TooQuiet:
                    fractionMod = +0.5f * pFraction;
                    escalateMod = -0.5f * pEscalate;
                    daysMod     = -0.5f * pDays;
                    break;
                case CampaignPace.LateWarPressure:
                    if (posture.AllianceId == 0)
                    {
                        fractionMod = +0.5f * pFraction;
                        escalateMod = -0.5f * pEscalate;
                    }
                    else
                    {
                        withdrawMod = +0.5f * pWithdraw;
                    }
                    break;
                case CampaignPace.TooFastCollapse:
                    fractionMod = -0.5f * pFraction;
                    daysMod     = +0.5f * pDays;
                    reactionMod = -0.5f * pReaction;
                    break;
                case CampaignPace.Stalemated:
                    daysMod = -0.5f * pDays;
                    break;
            }

            posture.MaximumProbeStrengthFractionModifier = fractionMod;
            posture.EscalateFriendlyRatioModifier        = escalateMod;
            posture.EnemyReactionMultiplierModifier      = reactionMod;
            posture.WithdrawFriendlyRatioModifier        = withdrawMod;
            posture.MinimumProbeDaysModifier             = daysMod;

            float holdMod = 0f, concessionMod = 0f;
            switch (posture.Pace)
            {
                case CampaignPace.TooFastCollapse:
                    holdMod      = +0.10f;
                    concessionMod = +0.10f;
                    break;
                case CampaignPace.Overheated:
                    holdMod = +0.05f;
                    break;
                case CampaignPace.LateWarPressure:
                    if (posture.AllianceId == 0) concessionMod = -0.03f;
                    else                          holdMod       = +0.05f;
                    break;
                case CampaignPace.TooQuiet:
                    holdMod = -0.03f;
                    break;
            }
            posture.MinimumHoldRatioModifier = Clamp(holdMod, -0.05f, +0.10f);
            posture.ConcessionRatioModifier  = Clamp(concessionMod, -0.05f, +0.10f);
        }

        public static void ApplyTo(OperationalProbeOptions options, DirectorPosture posture)
        {
            if (options == null || posture == null) return;
            options.MaximumProbeStrengthFraction = Clamp(options.MaximumProbeStrengthFraction + posture.MaximumProbeStrengthFractionModifier, 0.15f, 0.55f);
            options.EscalateFriendlyRatio        = Clamp(options.EscalateFriendlyRatio        + posture.EscalateFriendlyRatioModifier,        1.35f, 2.60f);
            options.EnemyReactionMultiplier      = Clamp(options.EnemyReactionMultiplier      + posture.EnemyReactionMultiplierModifier,      1.15f, 1.85f);
            options.WithdrawFriendlyRatio        = Clamp(options.WithdrawFriendlyRatio        + posture.WithdrawFriendlyRatioModifier,        0.35f, 0.85f);
            options.MinimumProbeDays             = ClampInt(options.MinimumProbeDays + (int)Math.Round(posture.MinimumProbeDaysModifier), 1, 9);
        }

        internal static DirectorMemoryDto MemoryToDto(DirectorMemory memory)
        {
            if (memory == null) return null;
            var dto = new DirectorMemoryDto
            {
                LastFullRefreshDay     = memory.LastFullRefreshDay,
                CapitalDangerStreakDays = memory.CapitalDangerStreakDays,
                DaysSinceLastBattle    = memory.DaysSinceLastBattle,
                LastSourceSignature    = memory.LastSourceSignature,
                RecentEventSummaries   = new System.Collections.Generic.List<string>(memory.RecentEventSummaries ?? new System.Collections.Generic.List<string>())
            };
            if (memory.LastPosture != null)
            {
                dto.Pace           = (int)memory.LastPosture.Pace;
                dto.Intent         = (int)memory.LastPosture.Intent;
                dto.Risk           = (int)memory.LastPosture.Risk;
                dto.TheaterPriority = (int)memory.LastPosture.TheaterPriority;
            }
            return dto;
        }

        internal static DirectorMemory MemoryFromDto(DirectorMemoryDto dto)
        {
            var memory = new DirectorMemory();
            if (dto == null) return memory;
            memory.LastFullRefreshDay      = dto.LastFullRefreshDay;
            memory.CapitalDangerStreakDays  = dto.CapitalDangerStreakDays;
            memory.DaysSinceLastBattle     = dto.DaysSinceLastBattle;
            memory.LastSourceSignature     = dto.LastSourceSignature;
            memory.RecentEventSummaries    = dto.RecentEventSummaries ?? new System.Collections.Generic.List<string>();
            memory.LastPosture = new DirectorPosture
            {
                Pace            = (CampaignPace)dto.Pace,
                Intent          = (StrategicIntent)dto.Intent,
                Risk            = (CollapseRisk)dto.Risk,
                TheaterPriority = (Theater)dto.TheaterPriority
            };
            return memory;
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
        private static int   ClampInt(int v, int lo, int hi)    => v < lo ? lo : (v > hi ? hi : v);
    }
}
