using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public static class ObjectiveCatalog
    {
        private static readonly Dictionary<int, ObjectiveMetadata> Table =
            new Dictionary<int, ObjectiveMetadata>
            {
                { 3,  EastCapital(760f, 60f) },     // Richmond
                { 4,  EastCapital(720f, 160f) },    // Washington
                { 29, East(Category.SupplyHub, 520f, 170f).WithTag(StrategyTag.DefensiveDepth) }, // West Virginia Union
                { 30, East(Category.SupplyHub, 500f, 120f).WithTag(StrategyTag.DefensiveDepth) }, // West Virginia CSA
                { 31, East(Category.SupplyHub, 610f, 120f).WithTag(StrategyTag.RailHub) }, // Shenandoah Valley
                { 32, East(Category.RailroadCut, 650f, 170f) }, // B&O lines
                { 33, East(Category.ForeignRecognition, 680f, 210f).WithTag(StrategyTag.ForeignRecognition) }, // Maryland
                { 34, East(Category.ForeignRecognition, 690f, 270f).WithTag(StrategyTag.ForeignRecognition) }, // Pennsylvania
                { 35, Coast(Category.SupplyHub, 880f, -120f) }, // Coastal North Carolina
                { 36, West(Category.SupplyHub, 500f, -260f) }, // Saltville
                { 37, Coast(Category.SupplyHub, 820f, 20f) } // Norfolk / Portsmouth / Suffolk
            };

        public static bool TryResolve(int objectiveId, out ObjectiveMetadata metadata)
        {
            return Table.TryGetValue(objectiveId, out metadata);
        }

        public static bool TryResolvePosition(int objectiveId, out float x, out float z)
        {
            if (TryResolve(objectiveId, out var metadata))
            {
                x = metadata.GeographicCentroidX;
                z = metadata.GeographicCentroidY;
                return true;
            }

            x = 0f;
            z = 0f;
            return false;
        }

        private static ObjectiveMetadata EastCapital(float x, float y)
        {
            return ObjectiveStrategyTagger.ApplyDefaultTags(new ObjectiveMetadata
            {
                Theater = Theater.East,
                Category = Category.CapitalThreat,
                StrategyTags = System.Array.Empty<StrategyTag>(),
                GeographicCentroidX = x,
                GeographicCentroidY = y
            });
        }

        private static ObjectiveMetadata East(Category category, float x, float y)
        {
            return ObjectiveStrategyTagger.ApplyDefaultTags(Base(Theater.East, category, x, y));
        }

        private static ObjectiveMetadata West(Category category, float x, float y)
        {
            return ObjectiveStrategyTagger.ApplyDefaultTags(Base(Theater.West, category, x, y));
        }

        private static ObjectiveMetadata Coast(Category category, float x, float y)
        {
            return ObjectiveStrategyTagger.ApplyDefaultTags(Base(Theater.Coast, category, x, y));
        }

        private static ObjectiveMetadata River(Category category, float x, float y)
        {
            return ObjectiveStrategyTagger.ApplyDefaultTags(Base(Theater.River, category, x, y));
        }

        private static ObjectiveMetadata Base(Theater theater, Category category, float x, float y)
        {
            return new ObjectiveMetadata
            {
                Theater = theater,
                Category = category,
                StrategyTags = System.Array.Empty<StrategyTag>(),
                GeographicCentroidX = x,
                GeographicCentroidY = y
            };
        }
    }
}
