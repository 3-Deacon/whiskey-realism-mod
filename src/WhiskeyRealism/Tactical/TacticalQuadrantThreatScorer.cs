namespace WhiskeyRealism.Tactical
{
    public static class TacticalQuadrantThreatScorer
    {
        public enum Direction { Front, LeftFlank, RightFlank, Rear }

        public struct Input
        {
            public float[] Slices;
            public float SliceWidthDegrees;
            public float UnitFacingDegrees;
        }

        public struct Output
        {
            public float FrontStrength;
            public float LeftFlankStrength;
            public float RightFlankStrength;
            public float RearStrength;
            public Direction DominantDirection;
            public bool RearPressureFlag;
        }

        public static Output Score(in Input input)
        {
            var output = new Output();
            if (input.Slices == null || input.Slices.Length == 0 || input.SliceWidthDegrees <= 0f)
                return output;

            for (int i = 0; i < input.Slices.Length; i++)
            {
                float sliceCenter = i * input.SliceWidthDegrees + input.SliceWidthDegrees * 0.5f;
                float relative = sliceCenter - input.UnitFacingDegrees;
                while (relative < 0f) relative += 360f;
                while (relative >= 360f) relative -= 360f;

                if (relative < 45f || relative >= 315f) output.FrontStrength += input.Slices[i];
                else if (relative < 135f) output.RightFlankStrength += input.Slices[i];
                else if (relative < 225f) output.RearStrength += input.Slices[i];
                else output.LeftFlankStrength += input.Slices[i];
            }

            float maxFlank = output.LeftFlankStrength > output.RightFlankStrength
                ? output.LeftFlankStrength : output.RightFlankStrength;
            output.RearPressureFlag = output.RearStrength > output.FrontStrength + maxFlank;

            float top = output.FrontStrength;
            output.DominantDirection = Direction.Front;
            if (output.RearStrength > top) { top = output.RearStrength; output.DominantDirection = Direction.Rear; }
            if (output.LeftFlankStrength > top) { top = output.LeftFlankStrength; output.DominantDirection = Direction.LeftFlank; }
            if (output.RightFlankStrength > top) { output.DominantDirection = Direction.RightFlank; }

            return output;
        }
    }
}
