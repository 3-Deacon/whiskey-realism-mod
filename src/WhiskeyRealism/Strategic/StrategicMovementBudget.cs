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
                var budgetDecision = EvaluateFrontBudget(input?.FrontLedger, source, candidate);
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

        public static TransferBudgetDecision EvaluateAreaMovement(
            FrontSectorLedger frontLedger,
            string sourceSectorKey,
            string destinationSectorKey,
            float strengthToMove)
        {
            if (frontLedger == null ||
                string.IsNullOrEmpty(sourceSectorKey) ||
                string.IsNullOrEmpty(destinationSectorKey) ||
                string.Equals(sourceSectorKey, destinationSectorKey, StringComparison.OrdinalIgnoreCase))
            {
                return new TransferBudgetDecision
                {
                    Allowed = true,
                    Action = TransferBudgetAction.Allowed,
                    Reason = "local-area"
                };
            }

            return frontLedger.EvaluateTransfer(sourceSectorKey, destinationSectorKey, strengthToMove);
        }

        private static TransferBudgetDecision EvaluateFrontBudget(
            FrontSectorLedger frontLedger,
            DefenseThreatSource source,
            DefenseCandidate candidate)
        {
            if (frontLedger == null || candidate == null || string.IsNullOrEmpty(candidate.SectorKey))
                return null;

            string destination = FindDestinationSector(frontLedger, source);
            return frontLedger.EvaluateTransfer(
                candidate.SectorKey,
                destination,
                Math.Max(0f, candidate.ActiveStrength));
        }

        private static string FindDestinationSector(FrontSectorLedger frontLedger, DefenseThreatSource source)
        {
            if (frontLedger == null || source == null) return null;

            Theater theater = TheaterClassifier.FromPosition(source.X, source.Z);
            foreach (var sector in frontLedger.Sectors)
            {
                if (sector != null && sector.Theater == theater)
                    return sector.SectorKey;
            }

            return null;
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
