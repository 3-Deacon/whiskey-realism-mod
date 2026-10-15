using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// What one side has had of the enemy army within range of its own
    /// regiments. Fed once per evidence build with the enemy strength in
    /// vanilla's per-regiment sight lists (deduped) and the part of it already
    /// in fire range. That picture is range-limited, not fog-limited (vanilla
    /// AI perception); fog-honest perception arrives with the battle-core
    /// snapshot. Everything is on doctrine time
    /// (<see cref="TacticalDoctrineTime"/>, game hours).
    ///
    /// Stands in for whole-army reads the side has no range-based access to:
    /// the enemy's <c>sideinformation.totalactiveforce</c>, its
    /// <c>reinforcementarrivalswithin24hrs</c> and the enemy AIBattle's
    /// <c>objectivechain[].reservegroups</c> designation.
    /// </summary>
    public sealed class TacticalEnemyContactLedger
    {
        /// <summary>Same sentinel ArmyReplanTriggers / phase doctrine treat as "unknown".</summary>
        public const float UnknownReserveCommitFraction = 0.35f;

        /// <summary>Largest-seen estimate fades 10% per battle hour without re-sighting.</summary>
        public const float LargestSeenDecayPerHour = 0.10f;

        /// <summary>No enemy in sight for this long after contact = contact broken.</summary>
        public const float ContactBrokenAfterHours = 0.25f;

        /// <summary>Contact older than this is no longer evidence of anything.</summary>
        public const float ContactMemoryHours = 2f;

        private float _largestSeen;
        private float _lastObserveHours = -1f;
        private float _lastContactHours = -1f;
        private float[] _bandObservedHours = new float[0];
        private float _entryShare = 1f;
        private bool _hasCoverage;

        public float EstimatedEnemyStrength => _largestSeen;
        public bool HadContact => _lastContactHours >= 0f;

        public void Reset()
        {
            _largestSeen = 0f;
            _lastObserveHours = -1f;
            _lastContactHours = -1f;
            for (int i = 0; i < _bandObservedHours.Length; i++) _bandObservedHours[i] = -1f;
            _entryShare = 1f;
            _hasCoverage = false;
        }

        /// <summary>
        /// Records what the side observed this tick: the front bands
        /// (<see cref="TacticalFrontAxis"/>) holding live own combat units whose
        /// sight lists were sampled, and how many of the enemy's pending
        /// reinforcement entry points an own unit watches. Size of the enemy
        /// plays no part.
        /// </summary>
        public void ObserveCoverage(float nowHours, bool[] observedBands, int bandCount, int watchedEntryPoints, int totalEntryPoints)
        {
            if (!Finite(nowHours) || bandCount <= 0) return;
            if (_bandObservedHours.Length != bandCount)
            {
                _bandObservedHours = new float[bandCount];
                for (int i = 0; i < bandCount; i++) _bandObservedHours[i] = -1f;
            }
            if (observedBands != null)
            {
                int n = Math.Min(bandCount, observedBands.Length);
                for (int i = 0; i < n; i++)
                    if (observedBands[i]) _bandObservedHours[i] = nowHours;
            }
            if (totalEntryPoints <= 0) _entryShare = 1f;
            else
            {
                float share = (float)Math.Max(0, watchedEntryPoints) / totalEntryPoints;
                _entryShare = share > 1f ? 1f : share;
            }
            _hasCoverage = true;
        }

        public void Observe(float nowHours, float visibleEnemyStrength, float engagedEnemyStrength)
        {
            if (!Finite(nowHours)) return;
            if (_lastObserveHours >= 0f && nowHours < _lastObserveHours) Reset();

            float visible = Sanitize(visibleEnemyStrength);
            if (_lastObserveHours >= 0f && _largestSeen > 0f)
            {
                float dt = nowHours - _lastObserveHours;
                float keep = 1f - LargestSeenDecayPerHour * dt;
                if (keep < 0f) keep = 0f;
                _largestSeen *= keep;
            }
            if (visible > _largestSeen) _largestSeen = visible;
            if (visible > 0f || Sanitize(engagedEnemyStrength) > 0f) _lastContactHours = nowHours;
            _lastObserveHours = nowHours;
        }

        /// <summary>
        /// Share of the known enemy force already in fire range of our line.
        /// The unseen remainder is the best available proxy for his reserve.
        /// </summary>
        public float ReserveCommitFraction(float engagedEnemyStrength)
        {
            if (_largestSeen <= 0f) return UnknownReserveCommitFraction;
            float f = Sanitize(engagedEnemyStrength) / _largestSeen;
            if (f > 1f) f = 1f;
            // Never emit the exact sentinel for a known value.
            if (Math.Abs(f - UnknownReserveCommitFraction) <= 0.0001f) f += 0.001f;
            return f;
        }

        public bool ContactBroken(float nowHours, float visibleEnemyStrength)
        {
            if (!HadContact || !Finite(nowHours)) return false;
            if (Sanitize(visibleEnemyStrength) > 0f) return false;
            float since = nowHours - _lastContactHours;
            return since >= ContactBrokenAfterHours && since <= ContactMemoryHours;
        }

        /// <summary>
        /// Intel confidence on [0, 1] in the current estimate:
        /// coverage × contact recency, independent of how large the enemy is.
        /// Coverage = mean per-band observation recency (a band not observed
        /// for <see cref="ContactMemoryHours"/> counts 0) × the share of
        /// pending enemy entry points watched (1 when none are pending).
        /// Contact recency fades the estimate itself: an old sighting carries
        /// no confidence however fully the front is watched now. Multiplicative
        /// by design: while enemy reinforcements are due at an unwatched entry
        /// point the confidence is 0 and the doctrine falls back to vanilla.
        /// </summary>
        public float Confidence(float nowHours)
        {
            if (!_hasCoverage || !HadContact || _largestSeen <= 0f || !Finite(nowHours)) return 0f;
            float bands = 0f;
            for (int i = 0; i < _bandObservedHours.Length; i++)
                bands += _bandObservedHours[i] < 0f ? 0f : Recency(nowHours - _bandObservedHours[i]);
            float bandTerm = _bandObservedHours.Length == 0 ? 0f : bands / _bandObservedHours.Length;
            return bandTerm * _entryShare * Recency(nowHours - _lastContactHours);
        }

        private static float Recency(float sinceHours)
        {
            float r = 1f - sinceHours / ContactMemoryHours;
            if (r < 0f) return 0f;
            return r > 1f ? 1f : r;
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static float Sanitize(float v) => !Finite(v) || v < 0f ? 0f : v;
    }
}
