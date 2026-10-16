using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    // Reads one alliance's campaign state into a NeedSnapshot for the daily review. Runtime
    // adapter (not in the test csproj). Own-alliance and public values only. Each field
    // degrades to its neutral default on failure with one bounded warning.
    internal static class CampaignNeedRuntime
    {
        internal static NeedSnapshot Read(int alliance, EraStage era, int day, int month, int year, FiscalInput fiscalInput, FiscalOutput fiscalOutput,
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

            ReadElection(s, day, month, year);

            try
            {
                var alliances = GameVars.alliance;
                if (alliances == null || alliance < 0 || alliance > 1 || alliance >= alliances.Length || alliances[alliance] == null)
                    return s;
                var state = alliances[alliance];

                // CampaignController.UpdateAllianceData (81515-81614).
                s.RecruitsAvailable = state.volunteersavailable + state.draftsavailable;
                s.TroopsFielded = state.troopsfielded;
                s.ContractExpiries = state.recruitscontractexpiries;
                s.NationalMorale = state.nationalmorale;
                s.AverageSupport = state.averagesupport;
                s.OwnBlockadeRatio = state.averageblockaderatio;
                if (alliances.Length > 1 && alliances[1] != null)
                    s.CsaBlockadeRatio = alliances[1].averageblockaderatio;
                ReadArms(alliance, state, s);
                s.PeakTransportUse = Max(state.transportcapacitypeak);
                s.RecruitableStates = CountRecruitableStates(alliance);
                s.InterventionProbability = Math.Max(
                    GameVars.Alliance.GetInterventionProbability(2),
                    GameVars.Alliance.GetInterventionProbability(3));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("campaign-need:read", "[CampaignNeedRuntime] alliance state read failed: " + ex.Message);
            }
            return s;
        }

        // The 1864 election (ElectionMorale): live while vanilla's objective 26 is not deactivated,
        // which it is outside chapter 2 and its dates (IsDeactivated 178914-178946). The line is
        // vanilla's own pref. The countdown stays NoVerdictAhead (no election) when the objective is not live.
        private static void ReadElection(NeedSnapshot s, int day, int month, int year)
        {
            try
            {
                s.ElectionMoraleThreshold = GamePrefs.lincolnmaintainmoraleabove;
                s.ElectionDaysToVerdict = ElectionMorale.VerdictCountdown(CampaignObjective.GetObjectiveDeactivated(ElectionMorale.ObjectiveId), day, month, year);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("campaign-need:election", "[CampaignNeedRuntime] election objective read failed (no election pressure this review): " + ex.Message);
            }
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

        // Own arming (ArmsCoverage): the alliance tick's unit counts and average WSF per lane
        // (81589-81605), its stock and open orders, and which models it can order.
        private static void ReadArms(int alliance, GameVars.Alliance state, NeedSnapshot s)
        {
            try
            {
                s.Rifles = new ArmsLane { Units = state.otherunits, AverageStrength = state.averageweaponstrengthrifles };
                s.Guns = new ArmsLane { Units = state.artilleryunits, AverageStrength = state.averageweaponstrengthguns };
                s.MinEquippableShare = GamePrefs.aiminnumberofequippableunits;
                s.ImportCutAtFullBlockade = GamePrefs.influenceofblockadesonweaponimports;
                var weapons = GameVars.weapon;
                if (weapons == null || state.weaponstock == null) return;
                var models = new List<ArmsModel>(weapons.Length);
                for (int id = 0; id < weapons.Length; id++)
                {
                    var weapon = weapons[id];
                    if (weapon == null) continue;
                    float open = 0f;
                    if (state.weaponorders != null && id < state.weaponorders.Length && state.weaponorders[id] != null)
                        open = state.weaponorders[id].quantity - state.weaponorders[id].alreadydelivered;
                    var restriction = weapon.restriction;
                    models.Add(new ArmsModel
                    {
                        IsArtillery = weapon.isari,
                        FitsBattery = restriction != null && restriction.Contains(2),
                        FitsLine = restriction != null && (restriction.Contains(0) || restriction.Contains(1)),
                        Strength = AICampaign.GetWeaponStrengthFactor(id),
                        StockAndOpenOrders = (id < state.weaponstock.Length ? state.weaponstock[id] : 0f) + open,
                        Orderable = WeaponOrderability.IsOrderable(weapon.IsResearched(alliance), weapon.IsImported(alliance), weapon.productionmultiplier)
                    });
                }
                int riflesPerUnit = GamePrefs.unittypesize != null && GamePrefs.unittypesize.Length > 0 ? GamePrefs.unittypesize[0] : 0;
                ArmsCoverage.Fill(models, s.Rifles, s.Guns, GamePrefs.usedstatsgunsdummy, riflesPerUnit);
            }
            catch (Exception ex)
            {
                s.Rifles = new ArmsLane();
                s.Guns = new ArmsLane();
                OnceLog.Warning("campaign-need:arms", "[CampaignNeedRuntime] arms read failed (no arming need this review): " + ex.Message);
            }
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
