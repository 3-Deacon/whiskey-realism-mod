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

            bool supplyStress = HasSupplyStress(input, options);

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
                bool stableExit = input.Memory.StableTicksAboveEmergency >= options.EmergencyExitStableTicks;
                bool clearlyRecovered = IsEmergencyRecoveryStable(input, options, earliestGate) && stableExit;
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
                || ForceCostsThreatenBalance(input);
            output.TheaterSupplyPriority = supplyStress ? (input.TopSupplyTheater != null ? input.TopSupplyTheater : string.Empty) : string.Empty;

            FillTargets(input, output, options);
            output.Suppressions = BuildSuppressions(input, output);
            output.Signature = input.AllianceId + ":" + output.Posture + ":" + output.DefendedGate + ":" + output.SupplyProtection + ":" + output.ForceCapWarning;
            return output;
        }

        // One bounded correction per call: move at most one vanilla step toward the band,
        // never past its edge. Inside the band the value (vanilla's own nudge) stands.
        public static float StepTowardBand(float current, float min, float max, float step)
        {
            if (min > max) min = max;
            if (step <= 0f) step = 0.05f;
            if (current < min) return current + step > min ? min : current + step;
            if (current > max) return current - step < max ? max : current - step;
            return current;
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

        public static bool IsEmergencyRecoveryStable(FiscalInput input, FiscalOptions options)
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

            return IsEmergencyRecoveryStable(input, options, earliestGate);
        }

        private static bool IsEmergencyRecoveryStable(FiscalInput input, FiscalOptions options, int earliestGate)
        {
            return input.CurrentRating <= earliestGate - options.CreditDefenseExitBuffer
                && input.AnnualBalance >= 0f
                && input.Treasury >= 0f;
        }

        private static bool HasSupplyStress(FiscalInput input, FiscalOptions options)
        {
            return input.SupplyPressure >= options.MinimumSupplyProtection
                || input.AmmoPressure >= options.MinimumSupplyProtection
                || input.TransportPressure >= options.MinimumSupplyProtection
                || LowShare(input.LowSupplyFormationCount, input.SectorCount) >= options.LowSupplySectorShare
                || LowShare(input.LowAmmoFormationCount, input.SectorCount) >= options.LowSupplySectorShare;
        }

        // A single starving sector/formation out of many is local, not systemic; it must
        // not hold Expansion / force-growth policy research or construction posture
        // indefinitely. Total unknown (0) => the count itself is the denominator
        // (legacy: any low => stress). Shared with ConstructionIntentLedger.
        public static float LowShare(int lowCount, int sectorCount)
        {
            if (lowCount <= 0) return 0f;
            int denominator = sectorCount > lowCount ? sectorCount : lowCount;
            return lowCount / (float)denominator;
        }

        private static bool ForceCostsThreatenBalance(FiscalInput input)
        {
            float forceCosts = -(input.ArmyUpkeep + input.NavyUpkeep + input.RecruitmentCost + input.SupplyDepotPurchases);
            if (forceCosts <= 0f) return false;

            float nonForceCapacity = input.AnnualBalance + forceCosts;
            if (nonForceCapacity <= 0f) return true;
            return forceCosts > nonForceCapacity * 1.25f;
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

        // Bands are directional so vanilla UpdateFinancialAI nudges (decompile 15386-15447)
        // in the allowed direction survive: open [0,1] by default; credit stress sets a
        // tax FLOOR (snapshot + step) on excise/income and an industry-subsidy CEILING
        // (snapshot - step). Only EmergencySolvency pins the industry subsidy to a point.
        private static void FillTargets(FiscalInput input, FiscalOutput output, FiscalOptions options)
        {
            OpenBand(output.TargetTaxMin, output.TargetTaxMax);
            OpenBand(output.TargetSubsidyMin, output.TargetSubsidyMax);

            if (output.Posture == FiscalPosture.CreditDefense || output.Posture == FiscalPosture.EmergencySolvency)
            {
                RaiseTaxFloor(input.Taxes, output, GameTaxExcise, options.VanillaStep);
                RaiseTaxFloor(input.Taxes, output, GameTaxIncome, options.VanillaStep);
                LowerSubsidyCeiling(input.Subsidies, output, GameSubsidyIndustry, options.VanillaStep,
                    pinFloor: output.Posture == FiscalPosture.EmergencySolvency);
            }

            // King Cotton / free trade / blockade running: tariff (GameVars.tax_tarifs = 0)
            // ceiling. Vanilla may still move it below the ceiling.
            if (input.AllianceId == 1 && (input.HasKingCotton || input.HasFreeTrade || input.HasOrganizedBlockadeRunning) && output.Posture <= FiscalPosture.BalancedWar)
            {
                if (output.TargetTaxMax[GameTaxTariffs] > KingCottonTariffCeiling)
                    output.TargetTaxMax[GameTaxTariffs] = KingCottonTariffCeiling;
                if (output.TargetTaxMin[GameTaxTariffs] > output.TargetTaxMax[GameTaxTariffs])
                    output.TargetTaxMin[GameTaxTariffs] = output.TargetTaxMax[GameTaxTariffs];
            }

            for (int i = 0; i < output.TargetSubsidyMax.Length && i < input.SubsidyFocus.Length; i++)
            {
                float cap = SubsidyFocusCap(input.SubsidyFocus[i]);
                if (output.TargetSubsidyMax[i] > cap)
                    output.TargetSubsidyMax[i] = cap;
                if (output.TargetSubsidyMin[i] > output.TargetSubsidyMax[i])
                    output.TargetSubsidyMin[i] = output.TargetSubsidyMax[i];
            }
        }

        // GameVars.tax_tarifs/tax_excise/tax_income and subsidy_industry (decompile 64309-64327).
        public const int GameTaxTariffs = 0;
        public const int GameTaxExcise = 1;
        public const int GameTaxIncome = 2;
        public const int GameSubsidyIndustry = 3;
        public const float KingCottonTariffCeiling = 0.15f;

        private static void OpenBand(float[] min, float[] max)
        {
            for (int i = 0; i < min.Length && i < max.Length; i++)
            {
                min[i] = 0f;
                max[i] = 1f;
            }
        }

        private static float SubsidyFocusCap(float value)
        {
            return value < 0f ? 0f : Clamp01(value);
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private static void RaiseTaxFloor(float[] taxes, FiscalOutput output, int lane, float step)
        {
            if (taxes == null || lane < 0 || lane >= taxes.Length || lane >= output.TargetTaxMin.Length) return;
            output.TargetTaxMin[lane] = Clamp01(Clamp01(taxes[lane]) + step);
        }

        private static void LowerSubsidyCeiling(float[] subsidies, FiscalOutput output, int lane, float step, bool pinFloor)
        {
            if (subsidies == null || lane < 0 || lane >= subsidies.Length || lane >= output.TargetSubsidyMax.Length) return;
            output.TargetSubsidyMax[lane] = Clamp01(Clamp01(subsidies[lane]) - step);
            if (pinFloor) output.TargetSubsidyMin[lane] = output.TargetSubsidyMax[lane];
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
