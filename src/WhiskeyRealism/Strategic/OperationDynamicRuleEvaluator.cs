using System;

namespace WhiskeyRealism.Strategic
{
    public static class OperationDynamicRuleEvaluator
    {
        public static PhaseTruthOutput Evaluate(
            PhaseTruthOutput baseOutput,
            HistoricalOperationProfile profile,
            HistoricalOperationContext context,
            int allianceId,
            int daySerial)
        {
            var output = baseOutput ?? new PhaseTruthOutput
            {
                Verdict = PhaseTruthVerdict.MissingTargetPosition,
                RecommendedAction = PhaseTruthAction.Replan,
                Reason = "missing-base-truth"
            };

            if (profile == null) return output;
            output.OperationId = profile.OperationId;

            var rules = profile.DynamicRules ?? Array.Empty<OperationDynamicRule>();
            OperationDynamicRule best = null;
            for (int i = 0; i < rules.Length; i++)
            {
                var rule = rules[i];
                if (rule == null || !Matches(rule, output, context)) continue;
                if (best == null ||
                    rule.Priority < best.Priority ||
                    (rule.Priority == best.Priority && string.CompareOrdinal(rule.RuleId, best.RuleId) < 0))
                    best = rule;
            }

            if (best == null) return output;

            output.RuleId = best.RuleId;
            output.Reason = string.IsNullOrEmpty(best.Reason) ? best.Trigger.ToString() : best.Reason;
            output.AlternateOperationId = best.AlternateOperationId;
            output.RecommendedAction = MapAction(best, profile, context, out var reasonSuffix);
            if (!string.IsNullOrEmpty(reasonSuffix))
                output.Reason = reasonSuffix;
            return output;
        }

        private static bool Matches(OperationDynamicRule rule, PhaseTruthOutput truth, HistoricalOperationContext context)
        {
            switch (rule.Trigger)
            {
                case OperationDynamicTrigger.ObjectiveUnavailable:
                    return truth.Verdict == PhaseTruthVerdict.ObjectiveUnavailable;
                case OperationDynamicTrigger.ObjectiveAccomplished:
                    return truth.Verdict == PhaseTruthVerdict.TargetAccomplished ||
                        (context != null && context.ObjectiveAccomplished);
                case OperationDynamicTrigger.TargetEngaged:
                    return truth.Verdict == PhaseTruthVerdict.TargetEngaged ||
                        (context != null && context.TargetEngagedRecently);
                case OperationDynamicTrigger.MajorFriendlyVictoryNearTarget:
                    return context != null && context.MajorFriendlyVictoryNearTarget && RatioMatches(rule, context);
                case OperationDynamicTrigger.MajorFriendlyDefeatNearTarget:
                    return context != null && context.MajorFriendlyDefeatNearTarget && RatioMatches(rule, context);
                case OperationDynamicTrigger.EnemyThreatensCapitalCorridor:
                    return context != null && context.EnemyThreatensCapitalCorridor;
                case OperationDynamicTrigger.EnemyConcentratesInTheater:
                    return context != null && context.EnemyConcentratesInTheater;
                case OperationDynamicTrigger.EmptyTarget:
                    return context != null && context.TargetSectorEnemyStrength <= 0f;
                case OperationDynamicTrigger.ForceBelowThreshold:
                    return truth.Verdict == PhaseTruthVerdict.ForceBelowThreshold;
                case OperationDynamicTrigger.ReplanThrash:
                    return context != null && context.RecentReplanCount >= 3;
                default:
                    return false;
            }
        }

        private static bool RatioMatches(OperationDynamicRule rule, HistoricalOperationContext context)
        {
            float ratio = context.TargetSectorRatio;
            if (rule.MinOwnEnemyRatio > 0f && ratio < rule.MinOwnEnemyRatio) return false;
            if (rule.MaxOwnEnemyRatio > 0f && ratio > rule.MaxOwnEnemyRatio) return false;
            return true;
        }

        private static PhaseTruthAction MapAction(
            OperationDynamicRule rule,
            HistoricalOperationProfile profile,
            HistoricalOperationContext context,
            out string reason)
        {
            reason = null;
            switch (rule.Action)
            {
                case OperationDynamicAction.AdvancePhase:
                    return PhaseTruthAction.Advance;
                case OperationDynamicAction.CompleteOperation:
                    return PhaseTruthAction.Complete;
                case OperationDynamicAction.Recover:
                    return PhaseTruthAction.Recover;
                case OperationDynamicAction.Pause:
                    return PhaseTruthAction.Pause;
                case OperationDynamicAction.PivotToAlternateOperation:
                    if (string.IsNullOrEmpty(rule.AlternateOperationId) ||
                        !ProfileAllowsAlternate(profile, rule.AlternateOperationId) ||
                        !HistoricalOperationCatalog.TryGetById(rule.AlternateOperationId, out _))
                    {
                        reason = "alternate-missing";
                        return PhaseTruthAction.Abort;
                    }
                    return PhaseTruthAction.Pivot;
                case OperationDynamicAction.AbortOperation:
                    return PhaseTruthAction.Abort;
                case OperationDynamicAction.Exploit:
                    return PhaseTruthAction.Exploit;
                case OperationDynamicAction.Counterstroke:
                    return PhaseTruthAction.Counterstroke;
                case OperationDynamicAction.ScreenAndDelay:
                    return PhaseTruthAction.ScreenAndDelay;
                case OperationDynamicAction.Continue:
                default:
                    return PhaseTruthAction.Continue;
            }
        }

        private static bool ProfileAllowsAlternate(HistoricalOperationProfile profile, string operationId)
        {
            var alternates = profile?.AlternateOperationIds;
            if (alternates == null) return false;
            for (int i = 0; i < alternates.Length; i++)
                if (alternates[i] == operationId) return true;
            return false;
        }
    }
}
