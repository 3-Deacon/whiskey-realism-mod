using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// A single scheduled arrival: time-until-arrival in hours, expected
    /// strength of the arriving force.
    /// </summary>
    public readonly struct ReinforcementArrival
    {
        public ReinforcementArrival(float hoursUntilArrival, float estimatedStrength)
        {
            HoursUntilArrival = Sanitize(hoursUntilArrival);
            EstimatedStrength = Sanitize(estimatedStrength);
        }

        public float HoursUntilArrival { get; }
        public float EstimatedStrength { get; }

        private static float Sanitize(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            return v < 0f ? 0f : v;
        }
    }

    /// <summary>
    /// Pure DTO holding everything the reinforcement-opportunity doctrine
    /// needs: own and enemy deployed strength right now, both sides'
    /// scheduled reinforcements (sorted by arrival time), and the commander's
    /// initiative/aggression along with an intel-confidence factor (lower
    /// when scout coverage is poor).
    ///
    /// Test-friendly: no Unity types, no vanilla types. Built by the runtime
    /// from BattleUnits.sideinformation + BattleUnits.scheduledarrival; pure
    /// callers in tests can construct directly.
    /// </summary>
    public readonly struct TacticalForceBalanceEvidence
    {
        public TacticalForceBalanceEvidence(
            float ownDeployedStrength,
            float enemyDeployedStrength,
            IReadOnlyList<ReinforcementArrival> ownArrivals,
            IReadOnlyList<ReinforcementArrival> enemyArrivals,
            float commanderInitiative01,
            float commanderAggression01,
            float intelConfidence01)
        {
            OwnDeployedStrength = SanitizeNonNeg(ownDeployedStrength);
            EnemyDeployedStrength = SanitizeNonNeg(enemyDeployedStrength);
            OwnArrivals = ownArrivals ?? Array.Empty<ReinforcementArrival>();
            EnemyArrivals = enemyArrivals ?? Array.Empty<ReinforcementArrival>();
            CommanderInitiative01 = Clamp01(commanderInitiative01);
            CommanderAggression01 = Clamp01(commanderAggression01);
            IntelConfidence01 = Clamp01(intelConfidence01);
        }

        public float OwnDeployedStrength { get; }
        public float EnemyDeployedStrength { get; }
        public IReadOnlyList<ReinforcementArrival> OwnArrivals { get; }
        public IReadOnlyList<ReinforcementArrival> EnemyArrivals { get; }
        public float CommanderInitiative01 { get; }
        public float CommanderAggression01 { get; }
        public float IntelConfidence01 { get; }

        /// <summary>
        /// Current ratio of own vs enemy deployed strength. Returns 1.0 when
        /// both are zero so callers don't divide-by-zero into a weird state.
        /// </summary>
        public float CurrentRatio
        {
            get
            {
                if (EnemyDeployedStrength <= 0f) return OwnDeployedStrength > 0f ? 10f : 1f;
                return OwnDeployedStrength / EnemyDeployedStrength;
            }
        }

        /// <summary>
        /// Total enemy strength expected within the given window (in hours),
        /// including currently-deployed force plus arrivals that land within
        /// the window.
        /// </summary>
        public float EnemyStrengthAtHour(float hoursFromNow)
        {
            float total = EnemyDeployedStrength;
            for (int i = 0; i < EnemyArrivals.Count; i++)
            {
                if (EnemyArrivals[i].HoursUntilArrival <= hoursFromNow)
                    total += EnemyArrivals[i].EstimatedStrength;
            }
            return total;
        }

        /// <summary>
        /// Same as EnemyStrengthAtHour but for own force.
        /// </summary>
        public float OwnStrengthAtHour(float hoursFromNow)
        {
            float total = OwnDeployedStrength;
            for (int i = 0; i < OwnArrivals.Count; i++)
            {
                if (OwnArrivals[i].HoursUntilArrival <= hoursFromNow)
                    total += OwnArrivals[i].EstimatedStrength;
            }
            return total;
        }

        /// <summary>
        /// Hours until enemy force ≥ own force (parity for enemy). Returns
        /// HoursUntilNever (= 999f) when the parity is never reached within
        /// the next 48 hours of arrivals (enemy never catches up).
        /// </summary>
        public float ParityHoursForEnemy()
        {
            const float HoursUntilNever = 999f;
            // Step through arrival events sorted by time. At each event,
            // recompute own/enemy strengths and check for parity.
            var events = MergedArrivalEvents();
            for (int i = 0; i < events.Count; i++)
            {
                float h = events[i];
                if (EnemyStrengthAtHour(h) >= OwnStrengthAtHour(h)) return h;
            }
            return HoursUntilNever;
        }

        /// <summary>
        /// Hours until own force ≥ enemy force. Returns HoursUntilNever (= 999f)
        /// when own never catches enemy.
        /// </summary>
        public float ParityHoursForOwn()
        {
            const float HoursUntilNever = 999f;
            if (OwnDeployedStrength >= EnemyDeployedStrength) return 0f;
            var events = MergedArrivalEvents();
            for (int i = 0; i < events.Count; i++)
            {
                float h = events[i];
                if (OwnStrengthAtHour(h) >= EnemyStrengthAtHour(h)) return h;
            }
            return HoursUntilNever;
        }

        private List<float> MergedArrivalEvents()
        {
            // Returns a sorted+deduped list of all distinct arrival times
            // from both sides. The doctrine evaluates parity at each
            // transition point.
            var set = new SortedSet<float>();
            for (int i = 0; i < OwnArrivals.Count; i++) set.Add(OwnArrivals[i].HoursUntilArrival);
            for (int i = 0; i < EnemyArrivals.Count; i++) set.Add(EnemyArrivals[i].HoursUntilArrival);
            // Always evaluate at hour 0 (current state) for completeness.
            set.Add(0f);
            return new List<float>(set);
        }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0.5f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static float SanitizeNonNeg(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            return v < 0f ? 0f : v;
        }
    }
}
