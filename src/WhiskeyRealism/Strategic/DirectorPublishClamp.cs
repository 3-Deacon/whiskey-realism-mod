namespace WhiskeyRealism.Strategic
{
    /// <summary>
    /// Rate-limits Director posture publishes to at most one per alliance per game day.
    /// R16: the previous single shared 1-publish-per-real-second clamp let alliance 0 take
    /// the slot every review and froze alliance 1's posture; it was also wall-clock based,
    /// which starves the director at 20x fast-forward. Keyed on the campaign day serial now.
    /// </summary>
    public sealed class DirectorPublishClamp
    {
        private readonly int[] _lastPublishDay = { int.MinValue, int.MinValue };

        /// <summary>
        /// Returns true and records <paramref name="daySerial"/> when this alliance has
        /// not published yet on that game day. Out-of-range alliances (Europe = 2) never publish.
        /// </summary>
        public bool TryPublish(int allianceId, int daySerial)
        {
            if (allianceId < 0 || allianceId >= _lastPublishDay.Length) return false;
            if (_lastPublishDay[allianceId] == daySerial) return false;
            _lastPublishDay[allianceId] = daySerial;
            return true;
        }
    }
}
