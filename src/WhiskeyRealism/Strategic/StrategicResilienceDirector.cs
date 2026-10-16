using System;

namespace WhiskeyRealism.Strategic
{
    public static class StrategicResilienceDirector
    {
        public static DirectorPosture ProposePosture(
            int allianceId,
            CampaignPaceOutput pace)
        {
            var posture = new DirectorPosture
            {
                AllianceId = allianceId,
                Pace = pace?.Pace ?? CampaignPace.Stable,
                Risk = pace?.Risk ?? CollapseRisk.Low,
                Reason = pace?.Reason ?? "no-pace-input"
            };

            posture.Intent = ProposeIntent(posture.Pace, posture.Risk);

            if (pace != null && pace.IntentBlockedFromPreserve && posture.Intent == StrategicIntent.Preserve)
                posture.Intent = StrategicIntent.Delay;

            ApplyThresholdModifiers(posture);
            return posture;
        }

        private static StrategicIntent ProposeIntent(CampaignPace pace, CollapseRisk risk)
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

        // The pace's threshold modifiers, each bounded: the front ledger's hold and concession ratios, the formation
        // directives' recover floor and mass ratios, the fiscal and construction biases and the defence guard budget.
        private static void ApplyThresholdModifiers(DirectorPosture posture)
        {
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

            float recoverMod = 0f, massRatioMod = 0f;
            switch (posture.Pace)
            {
                case CampaignPace.Overheated:
                    recoverMod    = +0.07f;
                    massRatioMod  = +0.05f;
                    break;
                case CampaignPace.TooFastCollapse:
                    recoverMod    = +0.10f;
                    massRatioMod  = +0.10f;
                    break;
                case CampaignPace.LateWarPressure:
                    if (posture.AllianceId == 0) massRatioMod = -0.10f;
                    else                          recoverMod   = +0.05f;
                    break;
                case CampaignPace.TooQuiet:
                    recoverMod = -0.03f;
                    break;
            }
            posture.RecoverFloorModifier = Clamp(recoverMod, -0.05f, +0.10f);
            posture.MassRatioModifier    = Clamp(massRatioMod, -0.10f, +0.10f);

            float supplyBias = 0f, logisticsBias = 0f, expansionDamper = 0f;
            if (posture.Risk == CollapseRisk.Critical) supplyBias += 0.40f;
            if (posture.Risk == CollapseRisk.Elevated) supplyBias += 0.20f;
            switch (posture.Pace)
            {
                case CampaignPace.Overheated:
                    supplyBias      += 0.25f;
                    expansionDamper += 0.20f;
                    break;
                case CampaignPace.TooQuiet:
                    logisticsBias += 0.30f;
                    break;
                case CampaignPace.TooFastCollapse:
                    expansionDamper += 0.50f;
                    break;
                case CampaignPace.LateWarPressure:
                    if (posture.AllianceId == 0) logisticsBias += 0.20f;
                    break;
            }
            posture.SupplyConstructionBias = Clamp(supplyBias,      -0.20f, +0.40f);
            posture.LogisticsBias          = Clamp(logisticsBias,   -0.20f, +0.40f);
            posture.ExpansionDamper        = Clamp(expansionDamper,  0f,    +0.50f);

            float guardMod = 0f;
            switch (posture.Pace)
            {
                case CampaignPace.TooFastCollapse:
                    guardMod = +0.03f;
                    break;
                case CampaignPace.Overheated:
                    guardMod = +0.05f;
                    break;
                case CampaignPace.LateWarPressure:
                    if (posture.AllianceId == 0) guardMod = -0.03f;
                    break;
            }
            posture.GuardBudgetFractionModifier = Clamp(guardMod, -0.05f, +0.05f);
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
            if (dto.Pace.HasValue && dto.Intent.HasValue && dto.Risk.HasValue)
            {
                memory.LastPosture = new DirectorPosture
                {
                    Pace            = (CampaignPace)dto.Pace.Value,
                    Intent          = (StrategicIntent)dto.Intent.Value,
                    Risk            = (CollapseRisk)dto.Risk.Value,
                    TheaterPriority = (Theater)(dto.TheaterPriority ?? (int)Theater.Unknown)
                };
            }
            return memory;
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
