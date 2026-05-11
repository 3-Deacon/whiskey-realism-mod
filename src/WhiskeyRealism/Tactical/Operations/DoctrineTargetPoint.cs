namespace WhiskeyRealism.Tactical.Operations
{
    public readonly struct DoctrineTargetPoint
    {
        public DoctrineTargetPoint(bool hasValue, float x, float z)
        {
            HasValue = hasValue && IsFinite(x) && IsFinite(z);
            X = HasValue ? x : 0f;
            Z = HasValue ? z : 0f;
        }

        public bool HasValue { get; }
        public float X { get; }
        public float Z { get; }

        public static DoctrineTargetPoint None { get { return new DoctrineTargetPoint(false, 0f, 0f); } }

        public static DoctrineTargetPoint From(float x, float z)
        {
            return new DoctrineTargetPoint(true, x, z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
