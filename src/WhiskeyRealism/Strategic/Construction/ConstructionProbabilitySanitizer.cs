namespace WhiskeyRealism.Strategic.Construction
{
    public enum ConstructionProbabilityStatus
    {
        Valid,
        Skip,
        Invalid
    }

    public static class ConstructionProbabilitySanitizer
    {
        public static ConstructionProbabilityStatus Classify(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return ConstructionProbabilityStatus.Invalid;

            return value > 0f
                ? ConstructionProbabilityStatus.Valid
                : ConstructionProbabilityStatus.Skip;
        }
    }
}
