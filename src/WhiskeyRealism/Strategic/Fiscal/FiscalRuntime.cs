using System;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Fiscal
{
    public static class FiscalRuntime
    {
        public static FiscalInput BuildInput(int alliance, EraStage era, FiscalStateMemory memory)
        {
            var input = new FiscalInput
            {
                AllianceId = alliance,
                EraStage = era,
                CurrentChapter = SafePolicyChapter(),
                Memory = memory != null ? memory : new FiscalStateMemory(),
                RatingNotches = GamePrefs.ratingnotches != null ? GamePrefs.ratingnotches.Length : 12,
                CurrentRating = SafeRating(alliance),
                Treasury = SafeTreasury(alliance),
                Debt = SafeDebt(alliance),
                AnnualBalance = SafeBalance(alliance),
                InterestCost = SafeInterestCost(alliance),
                ArmyUpkeep = SafeArmyUpkeep(alliance),
                NavyUpkeep = SafeNavyUpkeep(alliance),
                RecruitmentCost = SafeRecruitmentCost(alliance),
                SupplyDepotPurchases = SafeSupplyDepotPurchases(alliance),
                TopSupplyTheater = string.Empty
            };

            input.EmergencyPolicyFailureRating = RatingFailureFromFraction(GamePrefs.emergencyaicredittrigger, input.RatingNotches);
            input.RecruitmentFailureRating = RatingFailureFromFraction(GamePrefs.minimumratingforrecruitment, input.RatingNotches);
            input.ConstructionFailureRating = RatingFailureFromFraction(GamePrefs.minimumratingforconstruction, input.RatingNotches);
            input.WeaponFailureRating = RatingFailureFromFraction(GamePrefs.minimumratingforweaponorders, input.RatingNotches);

            CopyArray(SafeTaxes(alliance), input.Taxes);
            CopyArray(SafeTaxCaps(alliance), input.TaxCaps);
            CopyArray(SafeSubsidies(alliance), input.Subsidies);
            CopyArray(SafeSubsidyFocus(alliance), input.SubsidyFocus);

            FillPolicyFlags(input);
            FillSupplyPressure(input);
            return input;
        }

        private static int RatingFailureFromFraction(float fraction, int notches)
        {
            return Mathf.CeilToInt((1f - fraction) * notches);
        }

        private static int SafePolicyChapter()
        {
            try { return Policy.CurrentChapter; }
            catch { return -1; }
        }

        private static int SafeRating(int alliance)
        {
            try { return GameVars.alliance[alliance].currentrating; }
            catch { return 0; }
        }

        private static float SafeTreasury(int alliance)
        {
            try { return GameVars.alliance[alliance].treasury; }
            catch { return 0f; }
        }

        private static float SafeDebt(int alliance)
        {
            try { return GameVars.alliance[alliance].debt; }
            catch { return 0f; }
        }

        private static float SafeBalance(int alliance)
        {
            try { return GameVars.alliance[alliance].GetBalance(); }
            catch { return 0f; }
        }

        private static float SafeInterestCost(int alliance)
        {
            try { return GameVars.alliance[alliance].interestcostpa; }
            catch { return 0f; }
        }

        private static float SafeArmyUpkeep(int alliance)
        {
            try { return GameVars.alliance[alliance].armyupkeeppa; }
            catch { return 0f; }
        }

        private static float SafeNavyUpkeep(int alliance)
        {
            try { return GameVars.alliance[alliance].navyupkeeppa; }
            catch { return 0f; }
        }

        private static float SafeRecruitmentCost(int alliance)
        {
            try { return GameVars.alliance[alliance].recruitmentcostpa; }
            catch { return 0f; }
        }

        private static float SafeSupplyDepotPurchases(int alliance)
        {
            try { return GameVars.alliance[alliance].supplydepotpurchasespa; }
            catch { return 0f; }
        }

        private static float[] SafeTaxes(int alliance)
        {
            try { return GameVars.alliance[alliance].tax; }
            catch { return null; }
        }

        private static float[] SafeTaxCaps(int alliance)
        {
            try { return GameVars.alliance[alliance].taxcaps; }
            catch { return null; }
        }

        private static float[] SafeSubsidies(int alliance)
        {
            try { return GameVars.alliance[alliance].subsidies; }
            catch { return null; }
        }

        private static float[] SafeSubsidyFocus(int alliance)
        {
            try
            {
                var personality = GameVars.alliance[alliance].GetAIPersonality(alliance);
                return personality != null ? personality.subsidyfocus : null;
            }
            catch { return null; }
        }

        private static void CopyArray(float[] source, float[] target)
        {
            if (source == null || target == null) return;
            for (int i = 0; i < source.Length && i < target.Length; i++)
                target[i] = source[i];
        }

        private static void FillPolicyFlags(FiscalInput input)
        {
            try
            {
                input.HasWarBonds = Policy.GetStatusByPolicyID(input.AllianceId == 1 ? 122 : 22);
                input.HasBankAct = Policy.GetStatusByPolicyID(input.AllianceId == 1 ? 123 : 23);
                input.HasKingCotton = Policy.GetStatusByPolicyID(103) || Policy.GetStatusByPolicyID(104) || Policy.GetStatusByPolicyID(105) || Policy.GetStatusByPolicyID(106);
                input.HasFreeTrade = Policy.GetStatusByPolicyID(141);
                input.HasOrganizedBlockadeRunning = Policy.GetStatusByPolicyID(142);
                input.InterventionProbability = GameVars.Alliance.GetInterventionProbability(2) + GameVars.Alliance.GetInterventionProbability(3);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("fiscal-runtime:policy-flags", "[FiscalRuntime] policy flag read failed: " + ex.Message);
            }
        }

        private static void FillSupplyPressure(FiscalInput input)
        {
            var coordinator = StrategicCoordinator.Instance;
            var front = coordinator != null && input.AllianceId >= 0 && input.AllianceId < coordinator.Fronts.Length
                ? coordinator.Fronts[input.AllianceId]
                : null;

            if (front == null) return;

            int sectors = 0;
            float supplyStress = 0f;
            foreach (var sector in front.Sectors)
            {
                if (sector == null) continue;
                sectors++;
                supplyStress += 1f - sector.AverageSupply;
                if (sector.AverageSupply < 0.45f)
                    input.LowSupplyFormationCount++;
                if (sector.Posture == FrontPosture.Hold && sector.AverageSupply < 0.65f)
                    input.TopSupplyTheater = sector.Theater.ToString();
            }

            input.SupplyPressure = sectors > 0 ? supplyStress / sectors : 0f;
            input.TransportPressure = input.SupplyPressure;
            input.AmmoPressure = input.SupplyPressure * 0.5f;
        }
    }
}
