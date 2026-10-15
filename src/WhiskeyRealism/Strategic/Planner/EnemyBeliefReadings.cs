using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // The pure halves of the ledgers G1 moved from vanilla's omniscient enemy reads onto
    // EnemyArmyModel beliefs (spec C13/C25). Every sum is confidence-weighted (Strength x
    // Confidence01) at the belief's last-seen position; an expired belief (Confidence01 <= 0)
    // counts nowhere, as in EnemyBeliefQuery. Beliefs carry no morale: a sighting shows strength,
    // guns, type and position only.
    internal static class EnemyBeliefReadings
    {
        // FormationDirectiveRuntime's exchange pressure: strength + guns x 65 (unchanged scale).
        internal const float GunExchangeWeight = 65f;
        // A force seen ashore within this many days of being seen afloat is a landing.
        internal const int LandingWindowDays = 3;

        internal static float Weighted(in EnemyBelief belief)
        {
            if (belief.Confidence01 <= 0f) return 0f;
            return Math.Max(0f, belief.Strength) * Math.Min(1f, belief.Confidence01);
        }

        // FrontSectorRuntime: believed strength per key (its theater bucket).
        internal static void SumByKey<TKey>(IReadOnlyList<EnemyBelief> beliefs, Func<float, float, TKey> classify, Dictionary<TKey, float> into)
        {
            if (beliefs == null || classify == null || into == null) return;
            for (int i = 0; i < beliefs.Count; i++)
            {
                var belief = beliefs[i];
                float weighted = Weighted(in belief);
                if (weighted <= 0f) continue;
                TKey key = classify(belief.X, belief.Z);
                into.TryGetValue(key, out float sum);
                into[key] = sum + weighted;
            }
        }

        // FormationDirectiveRuntime.PopulateLocalPressure: the believed enemy within range of a
        // formation, its exchange pressure and the highest believed formation level (Unknown none).
        internal static void LocalPressure(IReadOnlyList<EnemyBelief> beliefs, float x, float z, float range,
                                           out float strength, out float exchange, out FormationLevel visibleLevel)
        {
            strength = 0f;
            exchange = 0f;
            visibleLevel = FormationLevel.Unknown;
            if (beliefs == null || range <= 0f) return;
            float rangeSq = range * range;
            for (int i = 0; i < beliefs.Count; i++)
            {
                var belief = beliefs[i];
                if (belief.Confidence01 <= 0f) continue;
                float dx = belief.X - x;
                float dz = belief.Z - z;
                if (dx * dx + dz * dz > rangeSq) continue;
                float confidence = Math.Min(1f, belief.Confidence01);
                strength += Math.Max(0f, belief.Strength) * confidence;
                exchange += (Math.Max(0f, belief.Strength) + Math.Max(0f, belief.Guns) * GunExchangeWeight) * confidence;
                var level = FormationSnapshot.LevelFromUnitType(belief.UnitTyp);
                if ((int)level > (int)visibleLevel) visibleLevel = level;
            }
        }

        // TelegraphConstructionRuntime.EnemyNearby / EnemyNearPosition: a believed enemy within range.
        internal static bool AnyWithin(IReadOnlyList<EnemyBelief> beliefs, float x, float z, float range)
        {
            if (range <= 0f) return false;
            return EnemyBeliefQuery.Nearest(beliefs, x, z, out float distance) >= 0 && distance <= range;
        }

        // DefenseIntentRuntime sea-invasion threat, inferred from what was seen (C25), never from
        // vanilla's seainvasionforce plan state: a group seen afloat off our coast (watch), or one
        // seen ashore on our soil within LandingWindowDays of being seen afloat (landed).
        internal static bool IsSeaInvasionThreat(in EnemyBelief belief, out bool landed)
        {
            landed = false;
            if (belief.Confidence01 <= 0f) return false;
            if ((belief.Traits & EnemyTraits.Embarked) != 0)
                return (belief.Traits & EnemyTraits.NearOwnCoast) != 0;
            if ((belief.Traits & EnemyTraits.OnOwnSoil) == 0 || belief.LastEmbarkedDay < 0) return false;
            landed = belief.LastSeenDay - belief.LastEmbarkedDay <= LandingWindowDays;
            return landed;
        }

        // DefenseIntentRuntime raid threat: a cavalry-majority group seen on our soil (never
        // vanilla's raidforces state).
        internal static bool IsRaidThreat(in EnemyBelief belief)
        {
            if (belief.Confidence01 <= 0f) return false;
            const EnemyTraits raid = EnemyTraits.CavalryMajority | EnemyTraits.OnOwnSoil;
            return (belief.Traits & raid) == raid && (belief.Traits & EnemyTraits.Embarked) == 0;
        }

        // The sea-invasion "collapsed" signal is belief expiry: the keys whose belief expired this
        // review and whose threat the previous review raised (previous sea-invasion signatures).
        internal static void CollapsedSeaInvasionKeys(IReadOnlyList<string> expiredKeys, ISet<string> previousSignatures, List<string> into)
        {
            if (expiredKeys == null || previousSignatures == null || into == null) return;
            for (int i = 0; i < expiredKeys.Count; i++)
                if (previousSignatures.Contains(DefenseThreatSignature.ForSeaInvasion(expiredKeys[i])))
                    into.Add(expiredKeys[i]);
        }

        // RecruitmentPatch.SafeStrengthRatio: vanilla GetStrengthRatio's shape (12366-12404) with
        // the believed enemy in place of enemyunits: own / Max(1, believed). The caller's own
        // strength keeps vanilla's own terms (direct strength, contract expiries, recruits).
        internal static float StrengthRatio(float ownStrength, IReadOnlyList<EnemyBelief> beliefs)
        {
            float enemy = 0f;
            if (beliefs != null)
                for (int i = 0; i < beliefs.Count; i++)
                {
                    var belief = beliefs[i];
                    enemy += Weighted(in belief);
                }
            return ownStrength / Math.Max(1f, enemy);
        }

        // StrategicCoordinator.UpdateCapitalThreat: vanilla's capital-need samples (11712-11741) on
        // beliefs. Morale is 1 (not seen): vanilla's min(str, str x morale) becomes the strength.
        internal static void CapitalSamples(IReadOnlyList<EnemyBelief> beliefs, float x, float z, List<CapitalForceSample> into)
        {
            if (beliefs == null || into == null) return;
            for (int i = 0; i < beliefs.Count; i++)
            {
                var belief = beliefs[i];
                float weighted = Weighted(in belief);
                if (weighted <= 0f) continue;
                float dx = belief.X - x;
                float dz = belief.Z - z;
                into.Add(new CapitalForceSample { Distance = (float)Math.Sqrt(dx * dx + dz * dz), Strength = weighted, Morale = 1f });
            }
        }
    }
}
