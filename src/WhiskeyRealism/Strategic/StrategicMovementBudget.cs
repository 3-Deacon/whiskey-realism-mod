using System;

namespace WhiskeyRealism.Strategic
{
    public static class StrategicMovementBudget
    {
        public static DefenseSuppression EvaluateDefenseCandidate(
            DefenseIntentInput input,
            DefenseThreatSource source,
            DefensePosture posture,
            ThreatScale scale,
            DefenseCandidate candidate)
        {
            if (candidate == null) return null;

            if (candidate.HasFormationDirective)
            {
                if (!candidate.DefensiveAllowed || !candidate.DirectMovementAllowed)
                    return Suppress(candidate, "formation-directive");

                if (candidate.Tier > CandidateTier.SameTheater && !candidate.TransferDonorAllowed)
                    return Suppress(candidate, "formation-donor");
            }

            if (source != null && source.Kind == DefenseThreatSourceKind.AssetProximity)
            {
                if (candidate.Tier > CandidateTier.Local)
                    return Suppress(candidate, "asset-proximity-local-only");
                return null;
            }

            if (candidate.Tier == CandidateTier.CrossMap && !IsNationalEmergency(source, posture, scale))
                return Suppress(candidate, "national-emergency-required");

            if (candidate.Tier >= CandidateTier.AdjacentTheater)
            {
                var budgetDecision = EvaluateFrontBudget(input?.FrontLedger, candidate);
                if (budgetDecision != null && !budgetDecision.Allowed)
                    return Suppress(candidate, budgetDecision.Reason);
            }

            return null;
        }

        public static float CapitalDefenseCap(DefenseIntentInput input, DefenseThreatSource source)
        {
            if (input == null || source == null) return 0f;
            if ((source.AssetRole & AssetStrategicRole.CapitalApproach) == 0) return 0f;

            float allianceStrength = Math.Max(0f, input.TotalAllianceEffectiveStrength);
            float fraction = input.CapitalDefenseBudgetFraction > 0f
                ? input.CapitalDefenseBudgetFraction
                : 0.18f;

            return allianceStrength * fraction;
        }

        private static TransferBudgetDecision EvaluateFrontBudget(
            FrontSectorLedger frontLedger,
            DefenseCandidate candidate)
        {
            if (frontLedger == null || candidate == null || string.IsNullOrEmpty(candidate.SectorKey))
                return null;

            return frontLedger.EvaluateTransfer(candidate.SectorKey, Math.Max(0f, candidate.ActiveStrength));
        }

        private static bool IsNationalEmergency(DefenseThreatSource source, DefensePosture posture, ThreatScale scale)
        {
            if (source == null) return false;
            if (source.Kind != DefenseThreatSourceKind.SeaInvasion) return false;
            if (posture != DefensePosture.ActiveInvasion && posture != DefensePosture.ContainAndCounterattack) return false;
            if (!source.LandedSignal) return false;
            return scale == ThreatScale.DecisiveLanding;
        }

        private static DefenseSuppression Suppress(DefenseCandidate candidate, string reason)
        {
            return new DefenseSuppression
            {
                UnitInstanceId = candidate.UnitInstanceId,
                Reason = reason
            };
        }
    }
}
