using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // The pure halves of the ledgers G1 moved from vanilla's omniscient enemy reads onto
    // EnemyArmyModel beliefs (spec C13/C25). Every sum is confidence-weighted (Strength x
    // Confidence01) at the belief's last-seen position; an expired belief (Confidence01 <= 0)
    // counts nowhere, as in EnemyBeliefQuery. Beliefs carry no morale: a sighting shows strength,
    // type and position only.
    internal static class EnemyBeliefReadings
    {
        // A force seen ashore within this many days of being seen afloat is a landing.
        internal const int LandingWindowDays = 3;
        // The order-raising threats (a raid, a landed sea invasion) come only from a sighting this
        // recent (integrator decision 2026-10-15, verify-g1 F1): the landing window. Vanilla itself
        // keeps a defender only while a real enemy stays near its destination
        // (CheckForEndOfDefensiveOperations 13795-13846) and aborts a raid on the enemy's current
        // position (9099-9101); a remembered belief must not hold a custom order beyond that.
        // Strength sums stay confidence-weighted over every live belief.
        internal const int RecentSightingDays = LandingWindowDays;

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
        // formation and the highest believed formation level (Unknown none).
        internal static void LocalPressure(IReadOnlyList<EnemyBelief> beliefs, float x, float z, float range,
                                           out float strength, out FormationLevel visibleLevel)
        {
            strength = 0f;
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

        internal static bool SeenRecently(in EnemyBelief belief, int today)
        {
            return belief.Confidence01 > 0f && today - belief.LastSeenDay <= RecentSightingDays;
        }

        // DefenseIntentRuntime sea-invasion threat, inferred from what was seen (C25), never from
        // vanilla's seainvasionforce plan state: a group seen afloat off our coast (watch), or one
        // seen ashore on our soil within LandingWindowDays of being seen afloat and seen recently
        // (landed, which raises orders).
        internal static bool IsSeaInvasionThreat(in EnemyBelief belief, int today, out bool landed)
        {
            landed = false;
            if (belief.Confidence01 <= 0f) return false;
            if ((belief.Traits & EnemyTraits.Embarked) != 0)
                return (belief.Traits & EnemyTraits.NearOwnCoast) != 0;
            if ((belief.Traits & EnemyTraits.OnOwnSoil) == 0 || belief.LastEmbarkedDay < 0) return false;
            if (belief.LastSource == DetectionSource.Mark) return false;   // a mark never lands a group (foresight F4)
            landed = belief.LastSeenDay - belief.LastEmbarkedDay <= LandingWindowDays && SeenRecently(in belief, today);
            return landed;
        }

        // DefenseIntentRuntime raid threat: a cavalry-majority group seen recently on our soil
        // (never vanilla's raidforces state).
        internal static bool IsRaidThreat(in EnemyBelief belief, int today)
        {
            if (!SeenRecently(in belief, today) || belief.LastSource == DetectionSource.Mark) return false;   // a mark raises no raid (foresight F4)
            const EnemyTraits raid = EnemyTraits.CavalryMajority | EnemyTraits.OnOwnSoil;
            return (belief.Traits & raid) == raid && (belief.Traits & EnemyTraits.Embarked) == 0;
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

    // A sea-invasion or raid threat raised last review whose belief no longer qualifies (left
    // sight past the recency, re-embarked off the coast, expired) is reported once more as
    // SightingLapsed: the ledger answers Recovered, so the coastal runner holds its units for
    // the cooldown and releases them when the signature leaves the output (verify-g1 F1). One per
    // alliance, held by DefenseIntentRuntime (CampaignScope).
    internal sealed class LapsedThreatTracker
    {
        private readonly Dictionary<string, DefenseThreatSource> _previous = new Dictionary<string, DefenseThreatSource>(StringComparer.Ordinal);
        private readonly HashSet<string> _current = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<DefenseThreatSource> _lapsed = new List<DefenseThreatSource>();

        // Appends this review's lapsed sources to `threats`, then remembers the qualifying ones.
        internal void Close(List<DefenseThreatSource> threats, Func<DefenseThreatSource, string> signature)
        {
            if (threats == null || signature == null) return;
            _current.Clear();
            for (int i = 0; i < threats.Count; i++)
                if (threats[i] != null) _current.Add(signature(threats[i]));

            _lapsed.Clear();
            foreach (var pair in _previous)
                if (!_current.Contains(pair.Key)) _lapsed.Add(Lapsed(pair.Value));

            // Remembered before the lapsed copies are appended, so a lapse is reported once.
            _previous.Clear();
            for (int i = 0; i < threats.Count; i++)
            {
                var t = threats[i];
                if (t == null) continue;
                if (t.Kind != DefenseThreatSourceKind.SeaInvasion && t.Kind != DefenseThreatSourceKind.RaidForce) continue;
                _previous[signature(t)] = t;
            }
            threats.AddRange(_lapsed);
        }

        internal void Clear()
        {
            _previous.Clear();
        }

        // A threat that keeps the proximity pass off its asset (verify-g1 R2-4). A lapsed copy keeps
        // its AssetName only for the signature the coastal runner releases on; it covers nothing,
        // so a believed enemy still close to that asset raises its proximity threat.
        internal static bool CoversAsset(DefenseThreatSource t)
        {
            return t != null && !t.SightingLapsed && !string.IsNullOrEmpty(t.AssetName);
        }

        private static DefenseThreatSource Lapsed(DefenseThreatSource t)
        {
            return new DefenseThreatSource
            {
                Kind = t.Kind,
                EnemyUnitKey = t.EnemyUnitKey,
                AssetName = t.AssetName,
                AssetKind = t.AssetKind,
                AssetRole = t.AssetRole,
                X = t.X,
                Z = t.Z,
                EnemyStrength = t.EnemyStrength,
                SightingLapsed = true
            };
        }
    }
}
