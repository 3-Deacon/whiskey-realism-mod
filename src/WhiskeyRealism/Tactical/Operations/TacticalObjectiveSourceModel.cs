namespace WhiskeyRealism.Tactical.Operations
{
    public static class TacticalObjectiveSourceModel
    {
        public static ObjectiveObservationInput Normalize(ObjectiveObservationInput input)
        {
            if (input.TypeAnchorVerified || !IsSpecificPointOfInterest(input.Type))
            {
                return input;
            }

            float cappedValue = input.Value > 0.35f ? 0.35f : input.Value;
            return new ObjectiveObservationInput(
                input.ObjectiveId,
                TacticalObjectiveType.UnknownVanillaObjective,
                input.Source,
                input.Location,
                input.SourceConfidence,
                cappedValue,
                input.TypeAnchorVerified);
        }

        public static bool CanDriveTypedOperationScoring(ObjectiveObservationInput input)
        {
            if (!input.TypeAnchorVerified) return false;

            return IsSpecificPointOfInterest(input.Type) ||
                input.Type == TacticalObjectiveType.EnemyLine ||
                input.Type == TacticalObjectiveType.FriendlyLine;
        }

        private static bool IsSpecificPointOfInterest(TacticalObjectiveType type)
        {
            return type == TacticalObjectiveType.VictoryPoint ||
                type == TacticalObjectiveType.Bridge ||
                type == TacticalObjectiveType.Ford ||
                type == TacticalObjectiveType.RoadJunction ||
                type == TacticalObjectiveType.Town ||
                type == TacticalObjectiveType.Ridge ||
                type == TacticalObjectiveType.ChokePoint;
        }
    }
}
