using System;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    // Reads one alliance's campaign state into a NeedSnapshot for the daily review. Runtime
    // adapter (not in the test csproj). Own-alliance and public values only. Each field
    // degrades to its neutral default on failure with one bounded warning.
    internal static class CampaignNeedRuntime
    {
        internal static NeedSnapshot Read(int alliance, EraStage era, FiscalInput fiscalInput, FiscalOutput fiscalOutput,
            bool emergencyCredit, bool emergencyManpower)
        {
            var s = new NeedSnapshot
            {
                Alliance = alliance,
                Era = era,
                FiscalPosture = fiscalOutput != null ? fiscalOutput.Posture : FiscalPosture.BalancedWar,
                EmergencyCredit = emergencyCredit,
                EmergencyManpower = emergencyManpower
            };

            if (fiscalInput != null)
            {
                // FiscalRuntime.BuildInput reads these (GetBalance, *upkeeppa, recruitmentcostpa,
                // interestcostpa, supplydepotpurchasespa) and the front supply pressure.
                s.AnnualBalance = fiscalInput.AnnualBalance;
                s.AnnualSpending = Math.Abs(fiscalInput.ArmyUpkeep) + Math.Abs(fiscalInput.NavyUpkeep)
                    + Math.Abs(fiscalInput.RecruitmentCost) + Math.Abs(fiscalInput.InterestCost)
                    + Math.Abs(fiscalInput.SupplyDepotPurchases);
                s.SupplyPressure = fiscalInput.SupplyPressure;
            }

            try
            {
                var alliances = GameVars.alliance;
                if (alliances == null || alliance < 0 || alliance > 1 || alliance >= alliances.Length || alliances[alliance] == null)
                    return s;
                var state = alliances[alliance];

                // CampaignController.UpdateAllianceData (81515-81614).
                s.RecruitsAvailable = state.volunteersavailable + state.draftsavailable;
                s.TroopsFielded = state.troopsfielded;
                s.TroopsInRecruitment = state.troopsinrecruitment;
                s.ContractExpiries = state.recruitscontractexpiries;
                s.NationalMorale = state.nationalmorale;
                s.AverageSupport = state.averagesupport;
                s.OwnBlockadeRatio = state.averageblockaderatio;
                if (alliances.Length > 1 && alliances[1] != null)
                    s.CsaBlockadeRatio = alliances[1].averageblockaderatio;
                s.WeaponStock = Sum(state.weaponstock);
                s.PeakTransportUse = Max(state.transportcapacitypeak);
                s.RecruitableStates = CountRecruitableStates(alliance);
                s.InterventionProbability = Math.Max(
                    GameVars.Alliance.GetInterventionProbability(2),
                    GameVars.Alliance.GetInterventionProbability(3));
                s.StrengthRatio = AICampaign.GetStrengthRatio(alliance);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("campaign-need:read", "[CampaignNeedRuntime] alliance state read failed: " + ex.Message);
            }
            return s;
        }

        // Same count vanilla's recruiting-emergency test uses (GetAIPersonality 62716-62731):
        // states of either side able to raise a max-size infantry unit for this alliance.
        private static int CountRecruitableStates(int alliance)
        {
            try
            {
                var nations = GameVars.nation;
                if (nations == null) return -1;
                int maxUnitSize = BattleUnits.GetMaxUnitSize(alliance, 0);
                int count = 0;
                for (int i = 0; i < nations.Length; i++)
                    if (nations[i] != null && nations[i].alliance <= 1 && nations[i].HasEnoughRecruits(maxUnitSize, 0, alliance))
                        count++;
                return count;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("campaign-need:recruitable-states", "[CampaignNeedRuntime] recruitable state count failed: " + ex.Message);
                return -1;
            }
        }

        private static float Sum(float[] values)
        {
            if (values == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < values.Length; i++)
                if (values[i] > 0f) sum += values[i];
            return sum;
        }

        private static float Max(float[] values)
        {
            if (values == null) return 0f;
            float max = 0f;
            for (int i = 0; i < values.Length; i++)
                if (values[i] > max) max = values[i];
            return max;
        }
    }
}
