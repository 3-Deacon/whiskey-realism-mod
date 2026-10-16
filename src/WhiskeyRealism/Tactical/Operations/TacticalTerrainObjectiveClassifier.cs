using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Operations
{
    // BC12 decision 3-4: the terrain picture's reading of a vanilla objective. Pure.
    //
    // Within ClassifyRadiusMeters (60 m) of the objective: Bridge (a listed bridge crossing) > Ford (a listed ford) > Town >
    // RoadJunction > Ridge (a crest cell of at least 3 m prominence) > unchanged. A classified type is verified by the picture
    // (TypeAnchorVerified), so TacticalObjectiveSourceModel.Normalize keeps it; ChokePoint keeps its one producer (corridor
    // anchors). The observation also carries the terrain strength and approach difficulty of decision 4 against the threat axis.
    public static class TacticalTerrainObjectiveClassifier
    {
        /// <summary>The observation read against the picture; unchanged without one, or for a type the picture does not classify.</summary>
        public static ObjectiveObservationInput Classify(in ObjectiveObservationInput observation, TerrainPicture picture, float axisX, float axisZ)
        {
            if (picture == null || !picture.Ready) return observation;
            if (observation.Type != TacticalObjectiveType.UnknownVanillaObjective && observation.Type != TacticalObjectiveType.VictoryPoint)
                return observation;

            float x = observation.Location.X, z = observation.Location.Z;
            TacticalObjectiveType type = ClassifiedType(picture, x, z, observation.Type, out bool verified);
            return observation.WithTerrain(
                type,
                verified || observation.TypeAnchorVerified,
                picture.TerrainStrength(x, z, axisX, axisZ),
                picture.ApproachDifficulty(x, z, axisX, axisZ));
        }

        public static TacticalObjectiveType ClassifiedType(TerrainPicture picture, float x, float z, TacticalObjectiveType unchanged, out bool verified)
        {
            verified = true;
            float r = TerrainPicture.ClassifyRadiusMeters;
            if (picture.TryNearestCrossing(x, z, r, out _, out _, out _, BattleTerrainGrid.BridgeId)) return TacticalObjectiveType.Bridge;
            if (picture.TryNearestCrossing(x, z, r, out _, out _, out _, BattleTerrainGrid.FordId)) return TacticalObjectiveType.Ford;
            if (picture.TownNear(x, z, r)) return TacticalObjectiveType.Town;
            if (picture.RoadJunctionNear(x, z, r)) return TacticalObjectiveType.RoadJunction;
            if (picture.CrestNear(x, z, r, TerrainShapeRulesMinimum)) return TacticalObjectiveType.Ridge;
            verified = false;
            return unchanged;
        }

        // maxsteepnessofterrainforshape (3 m): the crest prominence a Ridge needs.
        private const float TerrainShapeRulesMinimum = 3f;
    }
}
