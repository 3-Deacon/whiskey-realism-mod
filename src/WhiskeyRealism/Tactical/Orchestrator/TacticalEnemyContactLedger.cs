using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// What one side knows about the enemy army from its own eyes. Fed once per
    /// evidence build with the enemy strength the side can currently see
    /// (vanilla's fog-filtered sight lists, deduped) and the part of it already
    /// in fire range. Everything is on battle time (game hours,
    /// <c>GameVars.currenttimefromstart</c>).
    ///
    /// Replaces omniscient reads: the enemy's <c>sideinformation.totalactiveforce</c>,
    /// its <c>reinforcementarrivalswithin24hrs</c> and the enemy AIBattle's
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

        /// <summary>
        /// Prior for confidence: until we have seen an enemy at least this share
        /// of our own strength, we cannot claim to know his force. Relative, so
        /// it holds for a cavalry detachment and a field army alike.
        /// </summary>
        public const float ExpectedEnemyFractionOfOwn = 0.5f;

        private float _largestSeen;
        private float _lastObserveHours = -1f;
        private float _lastContactHours = -1f;

        public float EstimatedEnemyStrength => _largestSeen;
        public bool HadContact => _lastContactHours >= 0f;

        public void Reset()
        {
            _largestSeen = 0f;
            _lastObserveHours = -1f;
            _lastContactHours = -1f;
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
        /// Intel confidence on [0, 1]: how much of an expected-size enemy we have
        /// actually seen (coverage), discounted by how stale the last contact is.
        /// Replaces the fixed 0.8 "enemy deployed" constant that let the
        /// opportunity doctrine act on deployment-list strength it never saw.
        /// </summary>
        public float Confidence(float nowHours, float ownStrength)
        {
            if (!HadContact || _largestSeen <= 0f) return 0f;
            float expected = Math.Max(1f, ExpectedEnemyFractionOfOwn * Sanitize(ownStrength));
            float coverage = _largestSeen / expected;
            if (coverage > 1f) coverage = 1f;
            float since = Finite(nowHours) ? nowHours - _lastContactHours : ContactMemoryHours;
            float recency = 1f - since / ContactMemoryHours;
            if (recency < 0f) recency = 0f;
            if (recency > 1f) recency = 1f;
            return coverage * (0.5f * recency + (recency > 0f ? 0.5f : 0f));
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static float Sanitize(float v) => !Finite(v) || v < 0f ? 0f : v;
    }
}
