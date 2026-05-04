namespace WhiskeyRealism.Strategic
{
    public static class TheaterClassifier
    {
        public static Theater FromStateNameOrPosition(string stateName, float x, float z)
        {
            var fromState = FromStateName(stateName);
            return fromState == Theater.Unknown ? FromPosition(x, z) : fromState;
        }

        public static Theater FromStateName(string stateName)
        {
            if (string.IsNullOrEmpty(stateName)) return Theater.Unknown;

            switch (stateName.ToLowerInvariant())
            {
                case "district of columbia":
                case "maryland":
                case "pennsylvania":
                case "virginia":
                case "west virginia":
                    return Theater.East;

                case "delaware":
                case "florida":
                case "new jersey":
                case "new york":
                case "north carolina":
                case "south carolina":
                case "the bahamas":
                    return Theater.Coast;

                case "ohio":
                    return Theater.West;

                default:
                    return Theater.Unknown;
            }
        }

        public static Theater FromPosition(float x, float z)
        {
            if (x < -200f) return Theater.TransMiss;

            // W&L scenario 002 uses an eastern-seaboard campaign map where the
            // capitals sit near x=1260..1350 and z=-1010..-630. The older
            // broad-map rule classified that whole zone as Coast.
            if (x > 800f)
            {
                if (z < -1250f) return Theater.Coast;
                if (z < -100f) return Theater.East;
            }

            if (x > 250f && z < -250f) return Theater.West;
            if (x > 600f) return Theater.East;
            return Theater.West;
        }
    }
}
