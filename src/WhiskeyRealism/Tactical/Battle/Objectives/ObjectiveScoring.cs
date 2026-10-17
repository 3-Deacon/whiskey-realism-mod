using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>One live objective (gameObject.activeInHierarchy), read once per evaluation. Its state is public on the map: no fog applies.</summary>
    public struct ObjectiveFact
    {
        /// <summary>The capture's <c>ObjectiveId</c>, the director's own string.</summary>
        public string Id;
        public float X, Z, Points;
        public bool OwnHeld, EnemyHeld;
        /// <summary>A perceived enemy sample stands within <see cref="ObjectiveScoring.ThreatRadiusMeters"/> of it (the samples, never vanilla's omniscient distance test).</summary>
        public bool Threatened;
    }

    /// <summary>
    /// The victory clock (requirement-victory-pressure): each side's held share of the live points, the victory bar as the player sees it and the
    /// hours of daylight left.
    /// </summary>
    public struct VictoryClock
    {
        public float ShareOwn, ShareEnemy, Bar, DaylightHours;
    }

    /// <summary>What the clock asks of a commander: how far behind and ahead, how late, the urgency to take risk and the bias to hold.</summary>
    public struct ObjectivePressure
    {
        public float Behind, Ahead, Late, U, H;
    }

    /// <summary>
    /// One table row of the ledger: an objective's value, its feasibility penalty for the army's main body, the prior and the hold term. The
    /// director's own terms are not here; <see cref="Score"/> is without them.
    /// </summary>
    public struct ObjectiveEstimate
    {
        public string Id;
        public float V, Value, Penalty, Prior, Hold, Score;
        public bool Feasible;
        /// <summary>A static literal: why the objective is infeasible, or the largest penalty term; null with none.</summary>
        public string Reason;
    }

    /// <summary>
    /// BC14O objective doctrine (spec § 7B), the pure rules. An objective's value comes from vanilla's own points, normalised across the
    /// live objectives; everything here is a function of those numbers and of nothing else (no Unity or vanilla types).
    /// </summary>
    public static class ObjectiveScoring
    {
        /// <summary>Value = ValueFloor + ValueSpan x v, so the best objective keeps the 0.80 the director has always given an enemy-held one.</summary>
        public const float ValueFloor = 0.2f, ValueSpan = 0.6f;

        /// <summary>An own-held objective is worth this much of the same value: today's 0.45 / 0.80.</summary>
        public const float OwnHeldFactor = 0.5625f;

        /// <summary>The day is late when fewer than this many hours of daylight are left.</summary>
        public const float LateDayHours = 4f;

        /// <summary>A perceived enemy sample this close to an own-held objective threatens it (vanilla's defend test, 7473-7481, with samples).</summary>
        public const float ThreatRadiusMeters = 250f;

        /// <summary>The weight of the hold term: an own-held objective under threat gains this times H, the hold bias of a side that is ahead.</summary>
        public const float HoldWeight = 1f;

        /// <summary>How much of an objective's strike-leg state value enters its penalty.</summary>
        public const float ContestedWeight = 0.6f;

        /// <summary>The most direct children whose routes price an objective: the same member bound as a candidate play's.</summary>
        public const int MaxObjectiveMembers = 5;

        /// <summary>
        /// <c>v</c>: an objective's points over the largest of the live objectives' points. With every live objective at 0 points
        /// (<paramref name="maxPoints"/> not above 0) nothing tells them apart and every <c>v</c> is 0.5.
        /// </summary>
        public static float Normalised(float points, float maxPoints)
        {
            if (!(maxPoints > 0f)) return 0.5f;
            float v = points / maxPoints;
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        /// <summary>The objective's value for the side: <see cref="ValueFloor"/> + <see cref="ValueSpan"/> x v, times <see cref="OwnHeldFactor"/> for one it holds.</summary>
        public static float Value(float v, bool ownHeld)
        {
            float value = ValueFloor + ValueSpan * v;
            return ownHeld ? value * OwnHeldFactor : value;
        }

        /// <summary>
        /// The live objectives as the evaluation reads them: the captures whose <c>gameObject.activeInHierarchy</c> is true (an inactive objective is in
        /// neither the table nor the shares) and whose position is usable, owned by <paramref name="alliance"/> (OwnHeld), by another alliance
        /// (EnemyHeld) or by none. An own-held objective is Threatened by a perceived sample within <see cref="ThreatRadiusMeters"/>. Past the size of
        /// <paramref name="into"/> the most valuable are kept. Returns how many were written.
        /// </summary>
        internal static int FactsOf(IReadOnlyList<MapObjectiveCapture> captures, int alliance, BattleSnapshot snapshot, ObjectiveFact[] into)
        {
            int n = 0;
            for (int i = 0; captures != null && i < captures.Count; i++)
            {
                MapObjectiveCapture c = captures[i];
                if (!c.LiveInHierarchy || !TacticalVisionRuntimeAdapter.IsUsableMapPoint(c.Point)) continue;
                var fact = new ObjectiveFact
                {
                    Id = c.ObjectiveId, X = c.Point.X, Z = c.Point.Z, Points = c.Points,
                    OwnHeld = c.Owner == alliance, EnemyHeld = c.Owner >= 0 && c.Owner != alliance,
                };
                fact.Threatened = fact.OwnHeld && Threatened(snapshot, fact.X, fact.Z);
                if (n < into.Length) { into[n++] = fact; continue; }
                int least = 0;
                for (int k = 1; k < n; k++) if (into[k].Points < into[least].Points) least = k;
                if (fact.Points > into[least].Points) into[least] = fact;
            }
            return n;
        }

        /// <summary>
        /// The clock from the live objectives: the held shares are Σ the points of the live objectives a side holds over max(1, Σ the live points) (the
        /// 0.01 per held objective and the inactive objectives that vanilla's own <c>objectivepointsheld</c> counts are not here). In continuous mode
        /// the shares are the accrued totals instead, each side's over max(1, both).
        /// </summary>
        public static VictoryClock Clock(ObjectiveFact[] facts, int count, bool continuous, float accruedOwn, float accruedEnemy, float bar, float daylightHours)
        {
            var clock = new VictoryClock { Bar = bar, DaylightHours = daylightHours };
            if (continuous)
            {
                float both = Math.Max(1f, accruedOwn + accruedEnemy);
                clock.ShareOwn = accruedOwn / both;
                clock.ShareEnemy = accruedEnemy / both;
                return clock;
            }
            float live = 0f, own = 0f, enemy = 0f;
            for (int i = 0; i < count; i++)
            {
                live += facts[i].Points;
                if (facts[i].OwnHeld) own += facts[i].Points;
                else if (facts[i].EnemyHeld) enemy += facts[i].Points;
            }
            float total = Math.Max(1f, live);
            clock.ShareOwn = own / total;
            clock.ShareEnemy = enemy / total;
            return clock;
        }

        /// <summary>
        /// The pressure of the clock on a commander (<paramref name="agg01"/>, 0 to 1): behind by the points or by the bar, ahead likewise; late as the
        /// daylight runs under <see cref="LateDayHours"/>; urgency <c>behind x late x (0.5 + agg01)</c>, a side behind with the day running out and more
        /// so under an aggressive commander; hold <c>ahead x (1 - agg01)</c>, a side ahead holding what it has unless its commander is aggressive.
        /// The bar is read 0.5 as even, whatever produced that.
        /// </summary>
        public static ObjectivePressure Pressure(in VictoryClock clock, float agg01)
        {
            var p = new ObjectivePressure();
            p.Behind = Math.Max(Clamp01(clock.ShareEnemy - clock.ShareOwn), 2f * Math.Max(0f, 0.5f - clock.Bar));
            p.Ahead = Math.Max(Clamp01(clock.ShareOwn - clock.ShareEnemy), 2f * Math.Max(0f, clock.Bar - 0.5f));
            p.Late = Clamp01(1f - clock.DaylightHours / LateDayHours);
            p.U = Clamp01(p.Behind * p.Late * (0.5f + agg01));
            p.H = p.Ahead * (1f - agg01);
            return p;
        }

        /// <summary>The objective's feasibility penalty r: <c>1 - (1 - daylight)(1 - detail)(1 - ContestedWeight x contested)</c>.</summary>
        public static float Penalty(float daylight, float detail, float contested)
            => 1f - (1f - daylight) * (1f - detail) * (1f - ContestedWeight * contested);

        /// <summary>
        /// A perceived enemy sample within <see cref="ThreatRadiusMeters"/> of the point. Only the snapshot's samples count: an enemy regiment the side
        /// does not perceive threatens nothing it cannot know of.
        /// </summary>
        public static bool Threatened(BattleSnapshot snapshot, float x, float z)
        {
            if (snapshot == null) return false;
            EnemyObservation[] obs = snapshot.Observations;
            float r2 = ThreatRadiusMeters * ThreatRadiusMeters;
            for (int i = 0; i < snapshot.ObservationCount; i++)
            {
                if (!EnemySamples.Accepts(in obs[i])) continue;
                float dx = obs[i].X - x, dz = obs[i].Z - z;
                if (dx * dx + dz * dz <= r2) return true;
            }
            return false;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
