using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Operations
{
    /// <summary>
    /// What the side last saw of the enemy line, kept for <see cref="HorizonSeconds"/> after it was last in sight. The enemy-line objective is built
    /// each count from the enemy that is visible at that tick, so one count without a glimpse took it from the picture: the operation worked on it
    /// ("objective-picture-missing") while the doctrine planned from the first objective left, an empty map objective with no enemy, and every node
    /// went back to FormUp, then to Probe, then back to the line (smoke 2026-10-18: Forming, Scouting, Committed, SoftAbort in a minute of real time).
    /// A commander who has lost sight of a line does not forget where it was a minute ago. The record is what the side saw, unchanged, at the place it
    /// saw it; it is fog-honest because nothing is added to it.
    /// </summary>
    internal sealed class EnemyLineMemory
    {
        /// <summary>How long a line out of sight stays in the picture: the operation's own commitment (an hour of the doctrine clock).</summary>
        public const float HorizonSeconds = TacticalOperationDirector.CommitWindowSeconds;

        private const string Prefix = "enemy-line-";

        private ObjectiveRecord _last;
        private bool _has;
        private float _seenAtSeconds;

        public void Clear()
        {
            _has = false;
            _last = default(ObjectiveRecord);
            _seenAtSeconds = 0f;
        }

        /// <summary>
        /// The picture to plan on at <paramref name="nowSeconds"/>: <paramref name="objectives"/> as seen, with the last enemy line appended while it is out of
        /// sight and inside the horizon. A line in sight replaces the memory. The remembered record goes last, so the first objective of the picture is
        /// the one the picture had without it.
        /// </summary>
        public IReadOnlyList<ObjectiveRecord> Apply(IReadOnlyList<ObjectiveRecord> objectives, float nowSeconds)
        {
            if (objectives != null)
            {
                for (int i = 0; i < objectives.Count; i++)
                {
                    if (!IsEnemyLine(objectives[i])) continue;
                    _last = objectives[i];
                    _has = true;
                    _seenAtSeconds = nowSeconds;
                    return objectives;
                }
            }

            if (!_has) return objectives;
            float age = nowSeconds - _seenAtSeconds;
            // An age below zero is a clock that ran backwards (a reload to an earlier save): the memory is not trusted.
            if (age < 0f || age > HorizonSeconds)
            {
                Clear();
                return objectives;
            }

            int count = objectives != null ? objectives.Count : 0;
            var merged = new ObjectiveRecord[count + 1];
            for (int i = 0; i < count; i++) merged[i] = objectives[i];
            merged[count] = _last;
            return merged;
        }

        private static bool IsEnemyLine(in ObjectiveRecord record)
        {
            string id = record.Observation.ObjectiveId;
            return id != null && id.StartsWith(Prefix, StringComparison.Ordinal);
        }
    }
}
