using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public sealed class ProjectCandidateInput
    {
        public int ProjectId;
        public int SubsidyType;
        public float VanillaWeight;
    }

    public sealed class ProjectSelectionDecision
    {
        public bool ShouldReplace;
        public int ProjectId;
        public float BestScore;
        public float VanillaScore;
        public string Reason;
    }

    public static class ProjectSelectionScorer
    {
        private const float ReplacementMargin = 0.35f;

        public static ProjectSelectionDecision Select(
            GrandStrategyProfile profile,
            int subsidyType,
            int vanillaProjectId,
            float vanillaWeight,
            IEnumerable<ProjectCandidateInput> candidates,
            System.Func<int, float> extraWeight = null)
        {
            int bestProjectId = vanillaProjectId;
            float vanillaScore = Score(profile, vanillaProjectId, vanillaWeight, extraWeight);
            float bestScore = vanillaScore;

            if (candidates != null)
            {
                foreach (var candidate in candidates)
                {
                    if (candidate == null) continue;
                    if (candidate.SubsidyType != subsidyType) continue;

                    float candidateScore = Score(profile, candidate.ProjectId, candidate.VanillaWeight, extraWeight);
                    if (candidate.ProjectId == vanillaProjectId)
                        vanillaScore = candidateScore;

                    if (candidateScore > bestScore)
                    {
                        bestProjectId = candidate.ProjectId;
                        bestScore = candidateScore;
                    }
                }
            }

            bool shouldReplace;
            if (vanillaProjectId < 0)
            {
                shouldReplace = bestProjectId >= 0 && bestScore >= ReplacementMargin;
            }
            else
            {
                shouldReplace = bestProjectId >= 0
                    && bestProjectId != vanillaProjectId
                    && bestScore >= vanillaScore + ReplacementMargin;
            }

            return new ProjectSelectionDecision
            {
                ShouldReplace = shouldReplace,
                ProjectId = shouldReplace ? bestProjectId : vanillaProjectId,
                BestScore = bestScore,
                VanillaScore = vanillaScore,
                Reason = shouldReplace
                    ? (vanillaProjectId < 0 ? "vanilla-empty-strategy-margin" : "strategy-margin")
                    : "vanilla-close"
            };
        }

        private static float Score(GrandStrategyProfile profile, int projectId, float vanillaWeight, System.Func<int, float> extraWeight)
        {
            if (projectId < 0) return -999f;
            float profileWeight = profile != null ? profile.ProjectWeightFor(projectId) : 0f;
            float fiscalWeight = extraWeight != null ? extraWeight.Invoke(projectId) : 0f;
            return vanillaWeight + profileWeight + fiscalWeight;
        }
    }
}
