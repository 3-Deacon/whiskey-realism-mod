using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Side-level battle stance — corresponds to vanilla
    /// <c>BattleUnits.GetBattleType(alliance)</c> at decompile line 86095.
    /// Set at battle start (and rarely updated) from objective-points-held
    /// ratio. The doctrine + playbook layer hard-filter on this so we never
    /// produce attack output on a defender side, or pure-delay output on
    /// an attacker side. Mirrors Scourge of War's <c>OpenType()</c> +
    /// <c>Rank(eRankSide)</c> pattern (offai.cpp:1151-1159).
    /// </summary>
    public enum BattleStance
    {
        /// <summary>Neither side holds enough objectives — collide-while-moving.</summary>
        Meeting = 0,
        /// <summary>We're attacking (enemy holds the defensive objectives).</summary>
        Attacker = 1,
        /// <summary>We're defending (we hold the defensive objectives).</summary>
        Defender = 2,
    }


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
    /// needs: own deployed strength and own scheduled reinforcements (the
    /// side's own knowledge), the enemy strength the side has had in range
    /// (contact ledger), and the commander's initiative/aggression along with
    /// the ledger's intel confidence.
    ///
    /// There is no enemy-arrivals list: the side cannot see the enemy's
    /// schedule. Enemy reinforcements count once they are on the map and in
    /// range, through the ledger estimate.
    ///
    /// Troop quality (BC8): own and perceived enemy mean morale / experience turn the
    /// headcount ratio into vanilla's force balance (84862: × (1 + Δmorale) × (1 +
    /// Δexperience)); the enemy is compared in own-equivalent men
    /// (<see cref="EffectiveEnemyStrength"/>) everywhere the doctrine weighs the two forces.
    /// <see cref="InitiativeTriggerInfluence"/> is vanilla's
    /// influenceofcommanderinitiativeonattackdefendtrigger (6396-6430), 0 = no division.
    ///
    /// Test-friendly: no Unity types, no vanilla types.
    /// </summary>
    public readonly struct TacticalForceBalanceEvidence
    {
        public TacticalForceBalanceEvidence(
            float ownDeployedStrength,
            float enemyDeployedStrength,
            IReadOnlyList<ReinforcementArrival> ownArrivals,
            float commanderInitiative01,
            float commanderAggression01,
            float intelConfidence01,
            float ownCasualtyRatio01 = 0f,
            float ownRoutedFraction01 = 0f,
            float battleHoursElapsed = 0f,
            BattleStance stance = BattleStance.Meeting,
            SideQuality ownQuality = default,
            SideQuality enemyQuality = default,
            float initiativeTriggerInfluence = 0f,
            float commanderCasualtyTolerance01 = 0.5f)
        {
            OwnDeployedStrength = SanitizeNonNeg(ownDeployedStrength);
            EnemyDeployedStrength = SanitizeNonNeg(enemyDeployedStrength);
            OwnArrivals = ownArrivals ?? Array.Empty<ReinforcementArrival>();
            CommanderInitiative01 = Clamp01(commanderInitiative01);
            CommanderAggression01 = Clamp01(commanderAggression01);
            CommanderCasualtyTolerance01 = Clamp01(commanderCasualtyTolerance01);
            IntelConfidence01 = Clamp01(intelConfidence01);
            OwnCasualtyRatio01 = Clamp01(ownCasualtyRatio01);
            OwnRoutedFraction01 = Clamp01(ownRoutedFraction01);
            BattleHoursElapsed = SanitizeNonNeg(battleHoursElapsed);
            Stance = stance;
            QualityFactor = TroopQuality.ForceBalanceFactor(ownQuality, enemyQuality);
            InitiativeTriggerInfluence = SanitizeNonNeg(initiativeTriggerInfluence);
        }

        // Floor of the quality factor when converting the enemy into own-equivalent men, so a
        // zero factor (own morale or experience a full point below the enemy's) reads as an
        // overwhelming enemy instead of a division by zero.
        public const float MinQualityFactor = 0.01f;

        public float OwnDeployedStrength { get; }
        public float EnemyDeployedStrength { get; }
        public IReadOnlyList<ReinforcementArrival> OwnArrivals { get; }
        public float CommanderInitiative01 { get; }
        public float CommanderAggression01 { get; }

        /// <summary>
        /// The army commander's casualty tolerance mapped to [0, 1] (BC13): the basis of the casualty
        /// escalation threshold with Enable Per-Commander Personality on. 0.5 mid-band when absent.
        /// </summary>
        public float CommanderCasualtyTolerance01 { get; }
        public float IntelConfidence01 { get; }

        /// <summary>
        /// Own casualty ratio: cumulative losses / total active force.
        /// Mirrors vanilla <c>BattleUnits.sideinformation[side].casualtyratio</c>
        /// at decompile line 84662. Used by doctrine to escalate from
        /// DelayingDefense → WithdrawalToFightLater when losses mount.
        /// </summary>
        public float OwnCasualtyRatio01 { get; }

        /// <summary>
        /// Fraction of own brigades that have routed or been destroyed.
        /// Mirrors vanilla <c>routedratio + numberofrouts</c>. Used by doctrine
        /// as a secondary escalation trigger (sudden routs imply morale
        /// collapse even when raw casualty count is low).
        /// </summary>
        public float OwnRoutedFraction01 { get; }

        /// <summary>
        /// Real-time hours since battle start. Used by doctrine: very long
        /// battles in DelayingDefense without parity relief should escalate
        /// to WithdrawalToFightLater (the delay window has elapsed without
        /// the situation improving).
        /// </summary>
        public float BattleHoursElapsed { get; }

        /// <summary>
        /// Battle stance from vanilla <c>GetBattleType(alliance)</c>. Hard-filters
        /// the doctrine + playbook layer so they can't produce attack outcomes
        /// on a defender side or pure-delay outcomes on an attacker side.
        /// </summary>
        public BattleStance Stance { get; }

        /// <summary>Vanilla's force-balance quality factor (84862); 1 when either side's
        /// quality is unknown.</summary>
        public float QualityFactor { get; }

        /// <summary>influenceofcommanderinitiativeonattackdefendtrigger; 0 = none.</summary>
        public float InitiativeTriggerInfluence { get; }

        /// <summary>The enemy in own-equivalent men: deployed strength / quality factor.</summary>
        public float EffectiveEnemyStrength => EnemyDeployedStrength / Math.Max(MinQualityFactor, QualityFactor);

        /// <summary>
        /// Vanilla's force balance of own vs enemy deployed strength (84862): the headcount
        /// ratio × the quality factor. Returns 1.0 when both are zero and 10 with no enemy,
        /// so callers don't divide-by-zero into a weird state.
        /// </summary>
        public float CurrentRatio
        {
            get
            {
                if (EnemyDeployedStrength <= 0f) return OwnDeployedStrength > 0f ? 10f : 1f;
                return OwnDeployedStrength / EffectiveEnemyStrength;
            }
        }

        /// <summary>
        /// Own force expected within the given window (hours): deployed plus
        /// own scheduled arrivals that land within it.
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
        /// Hours until enemy force ≥ own force (parity for enemy, in own-equivalent
        /// men): 0 when the known enemy already matches us, otherwise HoursUntilNever (999).
        /// The enemy's known strength does not grow on a schedule (no
        /// enemy-arrivals input), and own arrivals only widen the gap.
        /// </summary>
        public float ParityHoursForEnemy()
        {
            const float HoursUntilNever = 999f;
            return EffectiveEnemyStrength >= OwnDeployedStrength ? 0f : HoursUntilNever;
        }

        /// <summary>
        /// Hours until own force ≥ the known enemy force (own-equivalent men). Returns
        /// HoursUntilNever (= 999f) when own arrivals never catch up.
        /// </summary>
        public float ParityHoursForOwn()
        {
            const float HoursUntilNever = 999f;
            float enemy = EffectiveEnemyStrength;
            if (OwnDeployedStrength >= enemy) return 0f;
            var events = OwnArrivalEvents();
            for (int i = 0; i < events.Count; i++)
            {
                float h = events[i];
                if (OwnStrengthAtHour(h) >= enemy) return h;
            }
            return HoursUntilNever;
        }

        private List<float> OwnArrivalEvents()
        {
            // Sorted, deduped own arrival times, plus hour 0.
            var set = new SortedSet<float>();
            for (int i = 0; i < OwnArrivals.Count; i++) set.Add(OwnArrivals[i].HoursUntilArrival);
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
