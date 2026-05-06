using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic.Fiscal;

namespace WhiskeyRealism.Strategic.Projects
{
    public static class ProjectDoctrineScorer
    {
        public const float ReplacementMargin = 0.35f;
        public const float MaxTimeToFundEstimateDays = 9999f;

        private const float FullyBrokenSuppressionScore = -1000f;
        private const float InactiveSuppressionScore = -999f;
        private const float OutOfWindowPenalty = ReplacementMargin + 0.75f;
        private const float ProfileWeightScale = 0.5f;
        private const float CriticalDoctrinePressure = 0.7f;

        public static ProjectDoctrineDecision Select(
            GrandStrategyProfile profile,
            ProjectDoctrineSignals signals,
            int subsidyType,
            int vanillaProjectId,
            float vanillaWeight,
            IEnumerable<ProjectCandidateInput> candidates,
            Func<int, float> fiscalWeight,
            Func<int, ProjectRuntimeFacts> runtimeFacts,
            float fundingAvailable,
            float netFundingPerDay,
            bool constructionCurrentlyWins)
        {
            if (signals == null)
                signals = new ProjectDoctrineSignals();
            else
                signals = SanitizeSignals(signals);

            ProjectRuntimeFacts vanillaFacts = Facts(runtimeFacts, vanillaProjectId, subsidyType);
            ProjectDoctrineScore vanillaScore = ScoreProject(profile, signals, vanillaProjectId, vanillaWeight, fiscalWeight, vanillaFacts);
            ProjectDoctrineScore bestScore = vanillaScore;
            int bestProjectId = vanillaProjectId;
            bool anyReplacementCandidate = false;
            float criticalPressure = !vanillaScore.Suppressed && !vanillaScore.OutOfWindow ? CriticalPressure(vanillaScore) : 0f;

            if (candidates != null)
            {
                foreach (var candidate in candidates)
                {
                    if (candidate == null || candidate.SubsidyType != subsidyType)
                        continue;

                    ProjectRuntimeFacts facts = Facts(runtimeFacts, candidate.ProjectId, subsidyType);
                    if (!CandidateLaneMatches(candidate.ProjectId, facts, subsidyType))
                        continue;

                    ProjectDoctrineScore score = ScoreProject(profile, signals, candidate.ProjectId, candidate.VanillaWeight, fiscalWeight, facts);
                    if (score.Suppressed || score.OutOfWindow)
                        continue;

                    anyReplacementCandidate = true;
                    criticalPressure = Math.Max(criticalPressure, CriticalPressure(score));
                    if (score.Total > bestScore.Total)
                    {
                        bestScore = score;
                        bestProjectId = candidate.ProjectId;
                    }
                }
            }

            var laneIntent = BuildLaneIntent(
                signals.Alliance,
                subsidyType,
                vanillaProjectId,
                fundingAvailable,
                vanillaFacts,
                netFundingPerDay,
                constructionCurrentlyWins,
                criticalPressure >= CriticalDoctrinePressure);

            bool hasReplacement = anyReplacementCandidate && bestProjectId >= 0 && bestProjectId != vanillaProjectId;
            if (vanillaScore.Suppressed && hasReplacement)
                return Decision(true, bestProjectId, bestScore.Total, vanillaScore.Total, "suppressed-vanilla", laneIntent);

            if (vanillaScore.OutOfWindow && hasReplacement && bestScore.Total >= vanillaScore.Total)
                return Decision(true, bestProjectId, bestScore.Total, vanillaScore.Total, "out-of-window-vanilla", laneIntent);

            float vanillaCost = vanillaFacts != null ? SafeNonNegative(vanillaFacts.Cost, MaxTimeToFundEstimateDays) : 0f;
            float availableFunding = SafeNonNegative(fundingAvailable, 0f);
            bool halfFunded = vanillaProjectId >= 0
                && vanillaCost > 0f
                && availableFunding >= vanillaCost * 0.5f;

            float margin = halfFunded ? ReplacementMargin * 2f : ReplacementMargin;
            bool shouldReplace = hasReplacement && bestScore.Total >= vanillaScore.Total + margin;

            if (shouldReplace)
                return Decision(true, bestProjectId, bestScore.Total, vanillaScore.Total, halfFunded ? "strategy-double-margin" : "strategy-margin", laneIntent);

            return Decision(false, vanillaProjectId, bestScore.Total, vanillaScore.Total, halfFunded ? "queued-half-funded" : "vanilla-close", laneIntent);
        }

        public static float ScoreDoctrineOnly(int projectId, ProjectDoctrineSignals signals)
        {
            return ScoreDoctrine(projectId, signals != null ? SanitizeSignals(signals) : new ProjectDoctrineSignals(), ProjectDoctrineCatalog.Get(projectId));
        }

        private static ProjectDoctrineScore ScoreProject(
            GrandStrategyProfile profile,
            ProjectDoctrineSignals signals,
            int projectId,
            float vanillaWeight,
            Func<int, float> fiscalWeight,
            ProjectRuntimeFacts facts)
        {
            var entry = ProjectDoctrineCatalog.Get(projectId);
            if (projectId < 0 || ProjectDoctrineCatalog.IsInactiveProjectId(projectId) || entry == null)
            {
                return new ProjectDoctrineScore
                {
                    ProjectId = projectId,
                    Total = InactiveSuppressionScore,
                    Reason = "inactive",
                    Suppressed = true
                };
            }

            if (entry.BugReviewState == ProjectBugReviewState.FullyBrokenUntilReviewed)
            {
                return new ProjectDoctrineScore
                {
                    ProjectId = projectId,
                    Total = FullyBrokenSuppressionScore,
                    Reason = "fully-broken",
                    Suppressed = true
                };
            }

            float profileWeight = profile != null ? profile.ProjectWeightFor(projectId) * ProfileWeightScale : 0f;
            float fiscal = fiscalWeight != null ? SafeWeight(fiscalWeight.Invoke(projectId)) : 0f;
            float doctrine = ScoreDoctrine(projectId, signals, entry);
            bool outOfWindow = IsOutOfWindow(signals.Era, facts);
            if (outOfWindow)
                doctrine -= OutOfWindowPenalty;

            float safeVanillaWeight = SafeWeight(vanillaWeight);
            float total = safeVanillaWeight + profileWeight + fiscal + doctrine;
            return new ProjectDoctrineScore
            {
                ProjectId = projectId,
                VanillaWeight = safeVanillaWeight,
                ProfileWeight = profileWeight,
                FiscalWeight = fiscal,
                DoctrineWeight = doctrine,
                Total = total,
                Reason = outOfWindow ? "out-of-window" : entry.Bucket.ToString(),
                Suppressed = false,
                OutOfWindow = outOfWindow
            };
        }

        private static float ScoreDoctrine(int projectId, ProjectDoctrineSignals signals, ProjectDoctrineEntry entry)
        {
            if (entry == null)
                return 0f;

            float score = 0f;
            if (entry.Bucket == ProjectDoctrineBucket.ArmsImport)
                score += signals.Alliance == 1 ? 0.8f + signals.WeaponDeficit : Math.Max(0f, signals.WeaponDeficit - 0.35f);
            if (entry.Bucket == ProjectDoctrineBucket.DomesticWeapons)
                score += (0.7f * signals.WeaponDeficit) + (0.5f * signals.ArtilleryDeficit) + (0.3f * signals.IndustryGap);
            if (entry.Bucket == ProjectDoctrineBucket.NavalBlockade)
                score += (0.8f * signals.NavalDeficit) + (0.6f * signals.BlockadePressure) + (signals.PortViability < 0.25f && signals.Alliance == 1 ? -0.8f : 0f);
            if (entry.Bucket == ProjectDoctrineBucket.LogisticsRail)
                score += 0.9f * signals.LogisticsTempoNeed;
            if (entry.Bucket == ProjectDoctrineBucket.FinanceCreditAdmin)
                score += 1.1f * signals.CreditStress;
            if (entry.Bucket == ProjectDoctrineBucket.AgricultureIndustry)
                score += (0.7f * signals.IndustryGap) + (0.7f * signals.AgricultureFoodStress);
            if (entry.Bucket == ProjectDoctrineBucket.DiplomacyTradeRecognition)
                score += signals.Alliance == 1 ? 0.9f * signals.RecognitionWindow : 0.45f * signals.RecognitionWindow;
            if (entry.Bucket == ProjectDoctrineBucket.ManpowerTrainingCivilOrder && projectId != 107)
                score += (0.7f * signals.ManpowerStress) + (0.5f * signals.OffensiveTempoNeed) + (0.6f * signals.CivilOrderRisk);
            if (projectId == 107)
                score += (0.7f * signals.ManpowerStress) + (0.6f * signals.CivilOrderRisk);

            if (projectId == 97 && signals.CreditStress >= 0.75f)
                score += 1.0f;
            if (projectId == 107)
                score += 0.4f * signals.CivilOrderRisk;
            if (projectId == 106 && signals.Alliance == 1)
                score += 0.5f * signals.BlockadePressure;
            if (projectId == 118)
                score += 0.5f * signals.LateWarCollapseRisk;

            if (signals.FiscalPosture >= FiscalPosture.CreditDefense && (projectId == 35 || projectId == 38 || projectId == 39 || projectId == 40 || projectId == 41))
                score -= 1.0f;

            return score;
        }

        private static bool IsOutOfWindow(EraStage era, ProjectRuntimeFacts facts)
        {
            if (facts == null || !facts.DateFromKnown)
                return false;

            if (facts.DateFromYear >= 1864 && era != EraStage.TotalWar1864)
                return true;

            if (facts.DateFromYear >= 1863 && era == EraStage.Amateur1861)
                return true;

            return false;
        }

        private static ProjectRuntimeFacts Facts(Func<int, ProjectRuntimeFacts> runtimeFacts, int projectId, int subsidyType)
        {
            if (runtimeFacts != null)
            {
                var facts = runtimeFacts.Invoke(projectId);
                if (facts != null)
                    return facts;
            }

            return new ProjectRuntimeFacts { ProjectId = projectId, SubsidyLane = subsidyType };
        }

        private static bool CandidateLaneMatches(int projectId, ProjectRuntimeFacts facts, int subsidyType)
        {
            var entry = ProjectDoctrineCatalog.Get(projectId);
            if (entry == null || entry.SubsidyLane != subsidyType)
                return false;

            return facts == null || facts.SubsidyLane == subsidyType;
        }

        private static float CriticalPressure(ProjectDoctrineScore score)
        {
            if (score == null)
                return 0f;

            return Math.Max(Math.Max(0f, SafeWeight(score.DoctrineWeight)), Math.Max(0f, SafeWeight(score.FiscalWeight)));
        }

        private static ProjectLaneIntent BuildLaneIntent(
            int alliance,
            int subsidyType,
            int queuedProjectId,
            float fundingAvailable,
            ProjectRuntimeFacts facts,
            float netFundingPerDay,
            bool constructionCurrentlyWins,
            bool criticalDoctrineProject)
        {
            float cost = facts != null ? SafeNonNegative(facts.Cost, MaxTimeToFundEstimateDays) : 0f;
            float available = SafeNonNegative(fundingAvailable, 0f);
            float rate = SafeNonNegative(netFundingPerDay, 0f);
            float costToGo = Math.Max(0f, cost - available);
            float days = costToGo <= 0f ? 0f : (rate > 0f ? Math.Min(costToGo / rate, MaxTimeToFundEstimateDays) : MaxTimeToFundEstimateDays);

            return new ProjectLaneIntent
            {
                Alliance = alliance,
                SubsidyLane = subsidyType,
                QueuedProjectId = queuedProjectId,
                FundingAvailable = available,
                FundingNeeded = cost,
                NetFundingPerDay = rate,
                TimeToFundEstimateDays = days,
                ConstructionCurrentlyWins = constructionCurrentlyWins,
                CriticalDoctrineProject = criticalDoctrineProject
            };
        }

        private static ProjectDoctrineDecision Decision(
            bool replace,
            int projectId,
            float best,
            float vanilla,
            string reason,
            ProjectLaneIntent laneIntent)
        {
            return new ProjectDoctrineDecision
            {
                ShouldReplace = replace,
                ProjectId = projectId,
                BestScore = best,
                VanillaScore = vanilla,
                Reason = reason,
                LaneIntent = laneIntent
            };
        }

        private static float SafeWeight(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private static float SafeNonNegative(float value, float positiveInfinityFallback)
        {
            if (float.IsNaN(value) || value < 0f)
                return 0f;
            if (float.IsPositiveInfinity(value))
                return positiveInfinityFallback;
            if (float.IsNegativeInfinity(value))
                return 0f;

            return value;
        }

        private static ProjectDoctrineSignals SanitizeSignals(ProjectDoctrineSignals signals)
        {
            return new ProjectDoctrineSignals
            {
                Alliance = signals.Alliance,
                Era = signals.Era,
                FiscalPosture = signals.FiscalPosture,
                WeaponDeficit = SafeSignal(signals.WeaponDeficit),
                ArtilleryDeficit = SafeSignal(signals.ArtilleryDeficit),
                NavalDeficit = SafeSignal(signals.NavalDeficit),
                BlockadePressure = SafeSignal(signals.BlockadePressure),
                PortViability = SafeSignal(signals.PortViability),
                CreditStress = SafeSignal(signals.CreditStress),
                ManpowerStress = SafeSignal(signals.ManpowerStress),
                LogisticsTempoNeed = SafeSignal(signals.LogisticsTempoNeed),
                IndustryGap = SafeSignal(signals.IndustryGap),
                AgricultureFoodStress = SafeSignal(signals.AgricultureFoodStress),
                CivilOrderRisk = SafeSignal(signals.CivilOrderRisk),
                RecognitionWindow = SafeSignal(signals.RecognitionWindow),
                OffensiveTempoNeed = SafeSignal(signals.OffensiveTempoNeed),
                LateWarCollapseRisk = SafeSignal(signals.LateWarCollapseRisk)
            };
        }

        private static float SafeSignal(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                return 0f;

            return value > 1f ? 1f : value;
        }
    }
}
