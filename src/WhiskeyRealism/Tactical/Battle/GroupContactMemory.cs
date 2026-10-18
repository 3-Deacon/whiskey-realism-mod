using System.Collections.Generic;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>
    /// When each group last had an enemy in sight, on the doctrine clock. The reconnaissance rule of the posture pipeline sends a group that sees
    /// nothing and is not under fire to screen or to probe instead of to assault ("infantry-screen-by-bounds"); it read the tick's own sight, so a brigade
    /// that lost the enemy behind a rise for one count went FixEnemy, Screen, FixEnemy, Screen, Probe in seven seconds (smoke 2026-10-18). A group has
    /// the contact it had within <see cref="HorizonHours"/> (the enemy line's own memory, an hour); past it the group has lost the enemy and looks for it.
    /// </summary>
    internal sealed class GroupContactMemory
    {
        /// <summary>How long a sighting stands in for sight, in doctrine hours: <see cref="EnemyLineMemory.HorizonSeconds"/>.</summary>
        public const double HorizonHours = EnemyLineMemory.HorizonSeconds / 3600.0;

        private readonly Dictionary<int, double> _seenAtHours = new Dictionary<int, double>();

        public void Clear() => _seenAtHours.Clear();

        /// <summary>
        /// Whether the group is in contact at <paramref name="nowHours"/>: it sees an enemy now (which is remembered), or it saw one within the horizon.
        /// A sighting later than now is a clock that ran backwards (a reload) and is not trusted.
        /// </summary>
        public bool InContact(int groupId, bool visibleNow, double nowHours)
        {
            if (visibleNow)
            {
                _seenAtHours[groupId] = nowHours;
                return true;
            }
            if (!_seenAtHours.TryGetValue(groupId, out double seen)) return false;
            double age = nowHours - seen;
            if (age >= 0.0 && age <= HorizonHours) return true;
            _seenAtHours.Remove(groupId);
            return false;
        }
    }
}
