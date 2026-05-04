using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Fiscal
{
    public static class FiscalIntentLedger
    {
        public static FiscalOutput Compute(FiscalInput input, FiscalOptions options)
        {
            options = options != null ? options : new FiscalOptions();
            input = input != null ? input : new FiscalInput();

            int notches = input.RatingNotches <= 0 ? 12 : input.RatingNotches;
            int bondFloor = notches - 1;
            int earliestGate = MinPositive(
                input.EmergencyPolicyFailureRating,
                input.RecruitmentFailureRating,
                input.ConstructionFailureRating,
                input.WeaponFailureRating,
                bondFloor);

            var output = new FiscalOutput();
            output.DefendedGate = ResolveGate(input, earliestGate, bondFloor);
            output.MinimumAcceptableRating = earliestGate - options.CreditDefenseEntryBuffer;

            bool supplyStress = input.SupplyPressure >= options.MinimumSupplyProtection
                || input.AmmoPressure >= options.MinimumSupplyProtection
                || input.TransportPressure >= options.MinimumSupplyProtection
                || input.LowSupplyFormationCount > 0
                || input.LowAmmoFormationCount > 0;

            bool emergency = input.CurrentRating >= bondFloor - 1
                || input.CurrentRating >= earliestGate
                || input.Treasury < 0f;

            bool creditDefense = input.CurrentRating >= earliestGate - options.CreditDefenseEntryBuffer;

            if (emergency)
            {
                output.Posture = FiscalPosture.EmergencySolvency;
            }
            else if (creditDefense || input.Memory.PreviousPosture == FiscalPosture.CreditDefense || input.Memory.EmergencyResidue)
            {
                bool clearlyRecovered = input.CurrentRating <= earliestGate - options.CreditDefenseExitBuffer
                    && input.AnnualBalance >= 0f
                    && !input.Memory.EmergencyResidue;
                output.Posture = clearlyRecovered ? FiscalPosture.BalancedWar : FiscalPosture.CreditDefense;
            }
            else if (input.AnnualBalance > 1000000f && input.CurrentRating <= earliestGate - 3 && !supplyStress)
            {
                output.Posture = FiscalPosture.Expansion;
            }
            else
            {
                output.Posture = FiscalPosture.BalancedWar;
            }

            output.SupplyProtection = supplyStress;
            output.LogisticsExpansion = supplyStress && output.Posture != FiscalPosture.EmergencySolvency;
            output.ForceCapWarning = output.Posture == FiscalPosture.EmergencySolvency
                || supplyStress
                || input.ArmyUpkeep + input.NavyUpkeep + input.RecruitmentCost + input.SupplyDepotPurchases > input.AnnualBalance;
            output.TheaterSupplyPriority = supplyStress ? (input.TopSupplyTheater != null ? input.TopSupplyTheater : string.Empty) : string.Empty;

            FillTargets(input, output, options);
            output.Suppressions = BuildSuppressions(input, output);
            output.Signature = input.AllianceId + ":" + output.Posture + ":" + output.DefendedGate + ":" + output.SupplyProtection + ":" + output.ForceCapWarning;
            return output;
        }

        private static int MinPositive(params int[] values)
        {
            int result = int.MaxValue;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] >= 0 && values[i] < result)
                    result = values[i];
            }
            return result == int.MaxValue ? 0 : result;
        }

        private static FiscalGate ResolveGate(FiscalInput input, int earliestGate, int bondFloor)
        {
            if (earliestGate == input.EmergencyPolicyFailureRating) return FiscalGate.EmergencyPolicy;
            if (earliestGate == input.RecruitmentFailureRating) return FiscalGate.Recruitment;
            if (earliestGate == input.ConstructionFailureRating) return FiscalGate.Construction;
            if (earliestGate == input.WeaponFailureRating) return FiscalGate.WeaponPurchases;
            if (earliestGate == bondFloor) return FiscalGate.BondFloor;
            return FiscalGate.None;
        }

        private static void FillTargets(FiscalInput input, FiscalOutput output, FiscalOptions options)
        {
            CopyBand(input.Taxes, output.TargetTaxMin, output.TargetTaxMax);
            CopyBand(input.Subsidies, output.TargetSubsidyMin, output.TargetSubsidyMax);

            if (output.Posture == FiscalPosture.CreditDefense || output.Posture == FiscalPosture.EmergencySolvency)
            {
                RaiseTaxBand(output, 1, options.VanillaStep);
                RaiseTaxBand(output, 2, options.VanillaStep);
                LowerSubsidyBand(output, 3, options.VanillaStep);
            }

            if (input.AllianceId == 1 && (input.HasKingCotton || input.HasFreeTrade || input.HasOrganizedBlockadeRunning) && output.Posture <= FiscalPosture.BalancedWar)
            {
                output.TargetTaxMax[0] = input.Taxes[0] < 0.15f ? 0.15f : input.Taxes[0];
            }

            for (int i = 0; i < output.TargetSubsidyMax.Length && i < input.SubsidyFocus.Length; i++)
                if (output.TargetSubsidyMax[i] > input.SubsidyFocus[i])
                    output.TargetSubsidyMax[i] = input.SubsidyFocus[i];
        }

        private static void CopyBand(float[] source, float[] min, float[] max)
        {
            for (int i = 0; i < min.Length && i < source.Length; i++)
            {
                min[i] = source[i];
                max[i] = source[i];
            }
        }

        private static void RaiseTaxBand(FiscalOutput output, int lane, float step)
        {
            if (lane < 0 || lane >= output.TargetTaxMax.Length) return;
            output.TargetTaxMax[lane] += step;
            output.TargetTaxMin[lane] += step;
        }

        private static void LowerSubsidyBand(FiscalOutput output, int lane, float step)
        {
            if (lane < 0 || lane >= output.TargetSubsidyMax.Length) return;
            output.TargetSubsidyMax[lane] -= step;
            if (output.TargetSubsidyMax[lane] < 0f) output.TargetSubsidyMax[lane] = 0f;
            output.TargetSubsidyMin[lane] = output.TargetSubsidyMax[lane];
        }

        private static FiscalSuppression[] BuildSuppressions(FiscalInput input, FiscalOutput output)
        {
            var list = new List<FiscalSuppression>();
            if (output.Posture == FiscalPosture.EmergencySolvency)
            {
                list.Add(FiscalSuppression.NewForceGrowth);
                list.Add(FiscalSuppression.VanityNaval);
                list.Add(FiscalSuppression.DiscretionaryIndustry);
            }
            if (input.AllianceId == 1 && output.Posture <= FiscalPosture.BalancedWar && (input.HasKingCotton || input.HasFreeTrade))
                list.Add(FiscalSuppression.TariffConflict);
            return list.ToArray();
        }
    }
}
