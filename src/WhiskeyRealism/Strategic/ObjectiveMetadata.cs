using System;

namespace WhiskeyRealism.Strategic
{
    public struct ObjectiveMetadata
    {
        public Theater  Theater;
        public Category Category;
        public StrategyTag[] StrategyTags;
        public float    SupplyReachWeight;
        public float    ForeignRecognitionWeight;
        public float    AttritionWeight;
        public float    GeographicCentroidX;
        public float    GeographicCentroidY;

        public bool IsDerived;

        public static ObjectiveMetadata DefaultDerived(Theater theater, float cx, float cy)
        {
            return new ObjectiveMetadata
            {
                Theater = theater,
                Category = Category.Other,
                StrategyTags = Array.Empty<StrategyTag>(),
                SupplyReachWeight        = 0.5f,
                ForeignRecognitionWeight = 0.5f,
                AttritionWeight          = 0.5f,
                GeographicCentroidX      = cx,
                GeographicCentroidY      = cy,
                IsDerived = true
            };
        }

        public ObjectiveMetadata WithTag(StrategyTag tag)
        {
            if (HasTag(tag))
                return this;

            var existing = StrategyTags ?? Array.Empty<StrategyTag>();
            var tags = new StrategyTag[existing.Length + 1];
            Array.Copy(existing, tags, existing.Length);
            tags[existing.Length] = tag;

            var copy = this;
            copy.StrategyTags = tags;
            return copy;
        }

        public bool HasTag(StrategyTag tag)
        {
            var tags = StrategyTags;
            if (tags == null) return false;

            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i] == tag) return true;
            }

            return false;
        }

        public float StrategyWeight(GrandStrategyProfile strategy)
        {
            if (strategy == null || StrategyTags == null)
                return 0f;

            float weight = 0f;
            for (int i = 0; i < StrategyTags.Length; i++)
                weight += strategy.WeightFor(StrategyTags[i]);

            return weight;
        }
    }
}
