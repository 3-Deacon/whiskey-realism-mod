namespace WhiskeyRealism.Strategic
{
    /// <summary>
    /// Rate-limits Director posture publishes to at most one per real second
    /// across all alliances combined.  A single shared instance is held by
    /// StrategicCoordinator; the clamp serialises the two per-alliance
    /// evaluations so that a burst of both alliances updating on the same frame
    /// only pushes one full publish through.
    /// </summary>
    public sealed class DirectorPublishClamp
    {
        private System.DateTime _lastPublishUtc = System.DateTime.MinValue;

        /// <summary>
        /// Returns true and records <paramref name="nowUtc"/> as the last
        /// publish time when at least one real second has elapsed since the
        /// previous call that returned true.  Returns false otherwise.
        /// </summary>
        public bool TryPublish(System.DateTime nowUtc)
        {
            if ((nowUtc - _lastPublishUtc).TotalSeconds < 1.0) return false;
            _lastPublishUtc = nowUtc;
            return true;
        }
    }
}
