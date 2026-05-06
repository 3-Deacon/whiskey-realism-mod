using System;
using WhiskeyRealism.Strategic.Fiscal;

namespace WhiskeyRealism.Strategic.Projects
{
    public static class ProjectDoctrineSignalBuilder
    {
        public static ProjectDoctrineSignals Build(ProjectDoctrineSignalInput input)
        {
            if (input == null)
                input = new ProjectDoctrineSignalInput();

            var signals = new ProjectDoctrineSignals
            {
                Alliance = input.Alliance,
                Era = input.Era,
                FiscalPosture = input.FiscalPosture,
                WeaponDeficit = RatioDeficit(input.EnemyBestAverageRifles, input.OwnAverageRifles, 0.01f),
                ArtilleryDeficit = RatioDeficit(input.EnemyBestAverageGuns, input.OwnAverageGuns, 0.01f),
                NavalDeficit = RatioDeficit(input.EnemyTotalTonnage, input.OwnTotalTonnage, 1f),
                BlockadePressure = Clamp01(input.Alliance == 1 ? input.BlockadeRatio : 1f - input.BlockadeRatio),
                PortViability = Clamp01(input.PortViabilityInput),
                CreditStress = CreditStress(input.FiscalPosture),
                ManpowerStress = Clamp01(input.ManpowerStressInput),
                LogisticsTempoNeed = Math.Max(Clamp01(input.SupplyPressure), Clamp01(input.TransportPressure)),
                IndustryGap = Clamp01(input.IndustryGapInput),
                AgricultureFoodStress = Clamp01(input.AgricultureFoodStressInput),
                CivilOrderRisk = Clamp01(input.CivilOrderRiskInput),
                RecognitionWindow = Clamp01(input.RecognitionProbability),
                OffensiveTempoNeed = Clamp01(input.OffensiveTempoInput)
            };

            float strengthCollapse = Clamp01(1f - input.StrengthRatio);
            signals.LateWarCollapseRisk = input.Era == EraStage.TotalWar1864
                ? Clamp01((0.4f * signals.CreditStress) + (0.4f * signals.ManpowerStress) + (0.2f * strengthCollapse))
                : 0f;

            return signals;
        }

        private static float RatioDeficit(float enemy, float own, float floor)
        {
            if (!IsFinite(enemy) || !IsFinite(own))
                return 0f;

            float denominator = Math.Max(enemy, floor);
            return Clamp01(Math.Max(enemy - own, 0f) / denominator);
        }

        private static float CreditStress(FiscalPosture posture)
        {
            if (posture == FiscalPosture.EmergencySolvency)
                return 1f;
            if (posture == FiscalPosture.CreditDefense)
                return 0.75f;
            if (posture == FiscalPosture.BalancedWar)
                return 0.25f;
            return 0f;
        }

        internal static float Clamp01(float value)
        {
            if (!IsFinite(value))
                return 0f;
            if (value < 0f)
                return 0f;
            if (value > 1f)
                return 1f;
            return value;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
