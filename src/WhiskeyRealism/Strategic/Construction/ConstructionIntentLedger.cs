using System.Collections.Generic;
using WhiskeyRealism.Strategic.Fiscal;

namespace WhiskeyRealism.Strategic.Construction
{
    public static class ConstructionIntentLedger
    {
        public static ConstructionOutput Compute(ConstructionInput input, ConstructionOptions options)
        {
            options = options != null ? options : new ConstructionOptions();
            input = input != null ? input : new ConstructionInput();
            var candidates = input.Candidates != null ? input.Candidates : new List<ConstructionCandidate>();

            var output = new ConstructionOutput();
            var suppressions = new List<ConstructionSuppression>();

            output.Posture = ResolvePosture(input, options);

            foreach (var candidate in candidates)
            {
                if (IsSuppressed(input, options, candidate, out var suppressionReason))
                {
                    suppressions.Add(new ConstructionSuppression
                    {
                        Kind = candidate.Kind,
                        Name = CandidateName(candidate),
                        Reason = suppressionReason
                    });
                    continue;
                }

                var scored = candidate;
                scored.Score = Score(input, options, output.Posture, scored);
                scored.Reason = output.Posture.ToString();
                AssignTop(output, scored);
            }

            output.TopConstructionTheater = ResolveTopTheater(output);
            output.Suppressions = suppressions.ToArray();
            output.Signature = BuildSignature(input, output);
            return output;
        }

        private static ConstructionPosture ResolvePosture(ConstructionInput input, ConstructionOptions options)
        {
            if (IsNearBondFloor(input, options) || input.FiscalPosture == FiscalPosture.EmergencySolvency)
                return ConstructionPosture.EmergencyHold;

            if ((input.SupplyProtection || input.LogisticsExpansion) &&
                (input.LowSupplyFormationCount > 0 ||
                 input.LowAmmoFormationCount > 0 ||
                 input.SupplyPressure >= options.SupplyPressureThreshold ||
                 input.AmmoPressure >= options.SupplyPressureThreshold ||
                 input.TransportPressure >= options.SupplyPressureThreshold))
            {
                return ConstructionPosture.FieldSupply;
            }

            if (input.CapitalThreat >= options.DefensiveThreatThreshold)
                return ConstructionPosture.DefensiveWorks;

            if (input.FiscalPosture == FiscalPosture.Expansion)
                return ConstructionPosture.IndustrialExpansion;

            return ConstructionPosture.Infrastructure;
        }

        private static bool IsNearBondFloor(ConstructionInput input, ConstructionOptions options)
        {
            return input.BondFloorRating > 0 &&
                input.CurrentRating + options.MinimumRatingBufferFromBondFloor >= input.BondFloorRating;
        }

        private static bool IsSuppressed(
            ConstructionInput input,
            ConstructionOptions options,
            ConstructionCandidate candidate,
            out ConstructionSuppressionReason reason)
        {
            if (!candidate.VanillaValid)
            {
                reason = ConstructionSuppressionReason.VanillaInvalid;
                return true;
            }

            bool nearBondFloor = IsNearBondFloor(input, options);

            if (input.FiscalPosture == FiscalPosture.EmergencySolvency || nearBondFloor)
            {
                if (IsEmergencyAllowed(input, options, candidate, nearBondFloor))
                {
                    reason = ConstructionSuppressionReason.None;
                    return false;
                }

                reason = ConstructionSuppressionReason.EmergencyCreditFloor;
                return true;
            }

            if (input.AllianceId == 1 &&
                candidate.Kind == ConstructionCandidateKind.Railroad &&
                (input.ActiveRailroadStarts > 0 || !candidate.SupportsActiveArmyCorridor))
            {
                reason = ConstructionSuppressionReason.CsaRailDoctrineCap;
                return true;
            }

            if (input.FiscalPosture == FiscalPosture.CreditDefense &&
                candidate.Kind == ConstructionCandidateKind.PrivateBuilding &&
                IsDiscretionaryPrivateBuilding(input, options, candidate, nearBondFloor))
            {
                reason = ConstructionSuppressionReason.DiscretionaryIndustryCreditDefense;
                return true;
            }

            reason = ConstructionSuppressionReason.None;
            return false;
        }

        private static bool IsEmergencyAllowed(
            ConstructionInput input,
            ConstructionOptions options,
            ConstructionCandidate candidate,
            bool nearBondFloor)
        {
            if (candidate.CriticalDefense)
                return true;
            if (candidate.Kind == ConstructionCandidateKind.SupplyDepot)
                return true;
            if (candidate.Kind == ConstructionCandidateKind.PrivateBuilding && IsMarket(candidate))
                return true;
            return IsCsaEarlyArmsSurvival(input, options, candidate, nearBondFloor);
        }

        private static bool IsDiscretionaryPrivateBuilding(
            ConstructionInput input,
            ConstructionOptions options,
            ConstructionCandidate candidate,
            bool nearBondFloor)
        {
            if (IsBank(candidate) || IsMarket(candidate))
                return false;
            return !IsCsaEarlyArmsSurvival(input, options, candidate, nearBondFloor);
        }

        private static bool IsCsaEarlyArmsSurvival(
            ConstructionInput input,
            ConstructionOptions options,
            ConstructionCandidate candidate,
            bool nearBondFloor)
        {
            return candidate.Kind == ConstructionCandidateKind.PrivateBuilding &&
                candidate.ArmsIndustry &&
                input.FiscalPosture == FiscalPosture.CreditDefense &&
                input.AllianceId == 1 &&
                input.CurrentYear <= options.CsaArmsStressLastYear &&
                !nearBondFloor &&
                candidate.SupportsActiveArmyCorridor;
        }

        private static bool IsBank(ConstructionCandidate candidate)
        {
            return Contains(CandidateName(candidate), "bank");
        }

        private static bool IsMarket(ConstructionCandidate candidate)
        {
            return candidate.BuildingTypeId == 13 || Contains(CandidateName(candidate), "market");
        }

        private static bool Contains(string value, string fragment)
        {
            return value != null && value.ToLowerInvariant().Contains(fragment);
        }

        private static float Score(
            ConstructionInput input,
            ConstructionOptions options,
            ConstructionPosture posture,
            ConstructionCandidate candidate)
        {
            float score = 0.25f;

            if (candidate.SupportsActiveArmyCorridor)
                score += 0.2f;
            if (candidate.ArmsIndustry)
                score += input.AllianceId == 1 && input.CurrentYear <= options.CsaArmsStressLastYear ? 0.55f : 0.25f;
            if (candidate.CriticalDefense)
                score += 0.3f;

            score += candidate.SupplyPressure * 0.45f;
            score += candidate.AmmoPressure * 0.3f;
            score += candidate.TransportPressure * 0.35f;
            score += candidate.WoundedPressure * 0.35f;
            score += candidate.CapitalThreat * 0.45f;

            if (posture == ConstructionPosture.FieldSupply)
                score += (candidate.SupplyPressure + candidate.TransportPressure + candidate.AmmoPressure) * 0.25f;
            else if (posture == ConstructionPosture.DefensiveWorks)
                score += candidate.CapitalThreat * 0.35f;
            else if (posture == ConstructionPosture.IndustrialExpansion && candidate.Kind == ConstructionCandidateKind.PrivateBuilding)
                score += 0.2f;

            return score;
        }

        private static void AssignTop(ConstructionOutput output, ConstructionCandidate candidate)
        {
            switch (candidate.Kind)
            {
                case ConstructionCandidateKind.PrivateBuilding:
                    output.TopPrivateBuilding = Best(output.TopPrivateBuilding, candidate);
                    break;
                case ConstructionCandidateKind.SupplyDepot:
                    output.TopSupplyDepot = Best(output.TopSupplyDepot, candidate);
                    break;
                case ConstructionCandidateKind.Fort:
                    output.TopFort = Best(output.TopFort, candidate);
                    break;
                case ConstructionCandidateKind.Telegraph:
                    output.TopTelegraph = Best(output.TopTelegraph, candidate);
                    break;
                case ConstructionCandidateKind.Railroad:
                    output.TopRailroad = Best(output.TopRailroad, candidate);
                    break;
            }
        }

        private static ConstructionCandidate Best(ConstructionCandidate current, ConstructionCandidate candidate)
        {
            if (current.Kind == ConstructionCandidateKind.None || candidate.Score > current.Score)
                return candidate;
            return current;
        }

        private static string ResolveTopTheater(ConstructionOutput output)
        {
            var top = Best(
                Best(Best(output.TopPrivateBuilding, output.TopSupplyDepot), Best(output.TopFort, output.TopTelegraph)),
                output.TopRailroad);
            return top.Kind == ConstructionCandidateKind.None ? "" : top.Theater.ToString();
        }

        private static string BuildSignature(ConstructionInput input, ConstructionOutput output)
        {
            return input.AllianceId + "|" +
                output.Posture + "|" +
                CandidateName(output.TopPrivateBuilding) + "|" +
                CandidateName(output.TopSupplyDepot) + "|" +
                CandidateName(output.TopFort) + "|" +
                CandidateName(output.TopTelegraph) + "|" +
                CandidateName(output.TopRailroad);
        }

        private static string CandidateName(ConstructionCandidate candidate)
        {
            return string.IsNullOrEmpty(candidate.Name) ? "<unnamed>" : candidate.Name;
        }
    }
}
