using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public sealed class GrandStrategyProfile
    {
        private readonly Dictionary<StrategyTag, float> tagWeights = new Dictionary<StrategyTag, float>();
        private readonly Dictionary<int, float> projectWeights = new Dictionary<int, float>();

        public int AllianceId { get; internal set; }
        public EraStage EraStage { get; internal set; }
        public string Name { get; internal set; }

        public float WeightFor(StrategyTag tag)
        {
            return tagWeights.TryGetValue(tag, out var weight) ? weight : 0f;
        }

        public float ProjectWeightFor(int projectId)
        {
            return projectWeights.TryGetValue(projectId, out var weight) ? weight : 0f;
        }

        internal GrandStrategyProfile WithTag(StrategyTag tag, float weight)
        {
            tagWeights[tag] = weight;
            return this;
        }

        internal GrandStrategyProfile WithProject(int projectId, float weight)
        {
            projectWeights[projectId] = weight;
            return this;
        }
    }
}
