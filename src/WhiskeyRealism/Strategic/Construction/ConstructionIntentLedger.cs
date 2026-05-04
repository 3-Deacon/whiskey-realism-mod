using System.Collections.Generic;
using WhiskeyRealism.Strategic.Fiscal;

namespace WhiskeyRealism.Strategic.Construction
{
    public static class ConstructionIntentLedger
    {
        public static ConstructionOutput Compute(ConstructionInput input, ConstructionOptions options)
        {
            if (options == null)
                options = new ConstructionOptions();

            var output = new ConstructionOutput();
            var suppressions = new List<ConstructionSuppression>();

            output.Posture = ResolvePosture(input, options);
            if (output.Posture == ConstructionPosture.EmergencyHold)
            {
                SuppressAll(input, suppressions, ConstructionSuppressionReason.EmergencyCreditFloor);
                output.Suppressions = suppressions.ToArray();
                output.Signature = BuildSignature(input, output);
                return output;
            }

            foreach (var candidate in input.Candidates)
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
                scored.Score = Score(input, output.Posture, scored);
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

            if (input.AllianceId == 1 &&
                candidate.Kind == ConstructionCandidateKind.Railroad &&
                (input.ActiveRailroadStarts > 0 || !candidate.SupportsActiveArmyCorridor))
            {
                reason = ConstructionSuppressionReason.CsaRailDoctrineCap;
                return true;
            }

            if (input.FiscalPosture == FiscalPosture.CreditDefense &&
                candidate.Kind == ConstructionCandidateKind.PrivateBuilding &&
                !candidate.ArmsIndustry)
            {
                reason = ConstructionSuppressionReason.DiscretionaryIndustryCreditDefense;
                return true;
            }

            if (candidate.Kind == ConstructionCandidateKind.PrivateBuilding &&
                candidate.ArmsIndustry &&
                input.FiscalPosture == FiscalPosture.CreditDefense &&
                input.AllianceId == 1 &&
                input.CurrentYear <= options.CsaArmsStressLastYear)
            {
                reason = ConstructionSuppressionReason.None;
                return false;
            }

            reason = ConstructionSuppressionReason.None;
            return false;
        }

        private static float Score(ConstructionInput input, ConstructionPosture posture, ConstructionCandidate candidate)
        {
            float score = 0.25f;

            if (candidate.SupportsActiveArmyCorridor)
                score += 0.2f;
            if (candidate.ArmsIndustry)
                score += input.AllianceId == 1 && input.CurrentYear <= 1863 ? 0.55f : 0.25f;
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

        private static void SuppressAll(
            ConstructionInput input,
            List<ConstructionSuppression> suppressions,
            ConstructionSuppressionReason reason)
        {
            foreach (var candidate in input.Candidates)
            {
                suppressions.Add(new ConstructionSuppression
                {
                    Kind = candidate.Kind,
                    Name = CandidateName(candidate),
                    Reason = reason
                });
            }
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
